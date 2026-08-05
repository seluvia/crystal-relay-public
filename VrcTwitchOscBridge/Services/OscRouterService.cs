using System.Buffers.Binary;
using System.Globalization;
using System.Net;
using System.Net.Sockets;
using System.Text;
using VRC.OSCQuery;
using VrcTwitchOscBridge.Models;

namespace VrcTwitchOscBridge.Services;

public sealed record OscObservedValue(string Address, OscParameterType ParameterType, object Value);

public enum OscDiscoveryState
{
    Idle,
    Discovering,
    Discovered,
    Lost
}

public sealed class OscRouterService : IAsyncDisposable
{
    private static readonly TimeSpan ServiceRefreshInterval = TimeSpan.FromSeconds(6);
    private static readonly TimeSpan DiscoveryLogThrottle = TimeSpan.FromSeconds(20);
    private const int StartupRetryCount = 4;

    private readonly object stateGate = new();
    private readonly SemaphoreSlim discoveryRefreshGate = new(1, 1);

    private CancellationTokenSource? runtimeCancellation;
    private Task[] runtimeTasks = [];
    private UdpClient? sendClient;
    private UdpClient? receiveListener;
    private OSCQueryService? oscQueryService;
    private Dictionary<string, OscParameterType> advertisedEndpoints = new(StringComparer.Ordinal);
    private DiscoveredOscTarget? activeVrChatTarget;
    private IPEndPoint? cachedVrChatEndPoint;
    private int localUdpPort;
    private int localTcpPort;
    private string localServiceName = string.Empty;
    private DateTimeOffset nextDiscoveryLogAt = DateTimeOffset.MinValue;
    private OscDiscoveryState discoveryState = OscDiscoveryState.Idle;

    public event Action<string>? LogWritten;

    public event Action<OscObservedValue>? ObservedValueReceived;

    public event Action<OscDiscoveryState>? DiscoveryStateChanged;

    public bool IsRunning => runtimeTasks.Any(task => !task.IsCompleted);

    public bool HasDiscoveredVrChat
    {
        get
        {
            lock (stateGate)
            {
                return activeVrChatTarget is not null;
            }
        }
    }

    public OscDiscoveryState DiscoveryState
    {
        get
        {
            lock (stateGate)
            {
                return discoveryState;
            }
        }
    }

    public Task StartAsync(IReadOnlyList<TriggerRuleSnapshot> rules, CancellationToken cancellationToken = default)
    {
        if (IsRunning)
        {
            return Task.CompletedTask;
        }

        UdpClient? listener = null;
        OSCQueryService? service = null;
        var udpPort = 0;
        var tcpPort = 0;
        Exception? startupException = null;

        for (var attempt = 1; attempt <= StartupRetryCount; attempt++)
        {
            cancellationToken.ThrowIfCancellationRequested();
            try
            {
                udpPort = VRC.OSCQuery.Extensions.GetAvailableUdpPort();
                tcpPort = VRC.OSCQuery.Extensions.GetAvailableTcpPort();
                listener = CreateListener(
                    udpPort,
                    "Crystal Relay OSCQuery receiver",
                    "Close the other app using that UDP port and relaunch Crystal Relay.");

                localServiceName = $"Crystal Relay Twitch to OSC ({Environment.ProcessId})";
                service = new OSCQueryServiceBuilder()
                    .WithServiceName(localServiceName)
                    .WithHostIP(IPAddress.Loopback)
                    .WithOscIP(IPAddress.Loopback)
                    .WithTcpPort(tcpPort)
                    .WithUdpPort(udpPort)
                    .WithDefaults()
                    .Build();

                service.OnOscQueryServiceAdded += HandleOscQueryServiceAdded;
                SyncAdvertisedEndpoints(service, rules);
                startupException = null;
                break;
            }
            catch (Exception ex) when (attempt < StartupRetryCount && LooksLikePortStartupCollision(ex))
            {
                listener?.Dispose();
                service?.Dispose();
                listener = null;
                service = null;
                startupException = ex;
            }
            catch
            {
                listener?.Dispose();
                service?.Dispose();
                throw;
            }
        }

        if (listener is null || service is null)
        {
            throw startupException ?? new InvalidOperationException("Crystal Relay could not start its OSCQuery receiver.");
        }

        runtimeCancellation = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        runtimeTasks =
        [
            Task.Run(() => RunReceiveLoopAsync(listener, runtimeCancellation.Token), runtimeCancellation.Token),
            Task.Run(() => RunDiscoveryLoopAsync(service, runtimeCancellation.Token), runtimeCancellation.Token)
        ];

        sendClient = new UdpClient();
        receiveListener = listener;
        oscQueryService = service;
        localUdpPort = udpPort;
        localTcpPort = tcpPort;
        lock (stateGate)
        {
            activeVrChatTarget = null;
            cachedVrChatEndPoint = null;
            nextDiscoveryLogAt = DateTimeOffset.MinValue;
            discoveryState = OscDiscoveryState.Discovering;
        }

        LogWritten?.Invoke($"OSCQuery service '{localServiceName}' is live. Crystal Relay is listening for VRChat values on UDP {localUdpPort} and serving OSCQuery on TCP {localTcpPort}.");
        LogDiscoveryWaiting(force: true);
        return Task.CompletedTask;
    }

    public void UpdateRuleSubscriptions(IReadOnlyList<TriggerRuleSnapshot> rules)
    {
        OSCQueryService? service;

        lock (stateGate)
        {
            service = oscQueryService;
        }

        if (service is null)
        {
            return;
        }

        SyncAdvertisedEndpoints(service, rules);
    }

    public async Task ForceRefreshAsync(CancellationToken cancellationToken = default)
    {
        OSCQueryService service;

        lock (stateGate)
        {
            service = oscQueryService
                ?? throw new InvalidOperationException("OSCQuery is not running yet. Start OSC testing or the background bridge first.");
            activeVrChatTarget = null;
            cachedVrChatEndPoint = null;
            discoveryState = OscDiscoveryState.Discovering;
            nextDiscoveryLogAt = DateTimeOffset.MinValue;
        }

        LogWritten?.Invoke("Forcing an OSCQuery refresh so Crystal Relay can reconnect to VRChat.");
        await RefreshDiscoveredServicesAsync(service, cancellationToken, logWhenWaiting: true, forceDiscoveryLog: true);
    }

    public async Task<IReadOnlyList<VrChatOscParameterSummary>> GetCurrentAvatarParametersAsync(CancellationToken cancellationToken = default)
    {
        OSCQueryService service;
        var shouldRefreshDiscovery = false;

        lock (stateGate)
        {
            service = oscQueryService
                ?? throw new InvalidOperationException("OSCQuery is not running yet. Start OSC testing or the background bridge first.");
            shouldRefreshDiscovery = activeVrChatTarget is null;
        }

        if (shouldRefreshDiscovery)
        {
            await RefreshDiscoveredServicesAsync(service, cancellationToken, logWhenWaiting: true, forceDiscoveryLog: true);
        }

        DiscoveredOscTarget target;
        lock (stateGate)
        {
            target = activeVrChatTarget
                ?? throw new InvalidOperationException("Crystal Relay could not find VRChat through OSCQuery yet. Open VRChat with OSC enabled, then try refreshing again.");
        }

        OSCQueryRootNode? tree;
        try
        {
            tree = await VRC.OSCQuery.Extensions.GetOSCTree(target.Address, target.QueryPort);
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            throw;
        }
        catch (Exception ex)
        {
            throw CreateTargetLostException("reading live avatar parameters", ex);
        }

        var parametersRoot = tree?.GetNodeWithPath("/avatar/parameters")
            ?? throw new InvalidOperationException("VRChat did not expose any avatar parameters through OSCQuery yet.");

        var parameters = new List<VrChatOscParameterSummary>();
        CollectAvatarParameters(parametersRoot, parameters);
        return parameters
            .OrderBy(parameter => parameter.Name, StringComparer.OrdinalIgnoreCase)
            .ThenBy(parameter => parameter.Address, StringComparer.OrdinalIgnoreCase)
            .ToArray();
    }

    public async Task<OscObservedValue?> GetCurrentAvatarParameterValueAsync(string parameterName, CancellationToken cancellationToken = default)
    {
        var normalizedAddress = VrChatOscClient.NormalizeAvatarParameterAddress(parameterName);
        return await GetCurrentOscValueAsync(normalizedAddress, cancellationToken);
    }

    public async Task<OscObservedValue?> GetCurrentOscValueAsync(string address, CancellationToken cancellationToken = default)
    {
        OSCQueryService service;
        var shouldRefreshDiscovery = false;

        lock (stateGate)
        {
            service = oscQueryService
                ?? throw new InvalidOperationException("OSCQuery is not running yet. Start OSC testing or the background bridge first.");
            shouldRefreshDiscovery = activeVrChatTarget is null;
        }

        if (shouldRefreshDiscovery)
        {
            await RefreshDiscoveredServicesAsync(service, cancellationToken, logWhenWaiting: true, forceDiscoveryLog: true);
        }

        DiscoveredOscTarget target;
        lock (stateGate)
        {
            target = activeVrChatTarget
                ?? throw new InvalidOperationException("Crystal Relay could not find VRChat through OSCQuery yet. Open VRChat with OSC enabled, then try again.");
        }

        var normalizedAddress = VrChatOscClient.NormalizeOscAddress(address);
        OSCQueryRootNode? tree;
        try
        {
            tree = await VRC.OSCQuery.Extensions.GetOSCTree(target.Address, target.QueryPort);
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            throw;
        }
        catch (Exception ex)
        {
            throw CreateTargetLostException($"reading the live value for {normalizedAddress}", ex);
        }

        var node = tree?.GetNodeWithPath(normalizedAddress)
            ?? throw new InvalidOperationException($"VRChat did not expose {normalizedAddress} through OSCQuery yet.");

        return TryReadNodeValue(node);
    }

    public async Task StopAsync()
    {
        if (runtimeCancellation is null)
        {
            return;
        }

        var cancellation = runtimeCancellation;
        var tasks = runtimeTasks;
        var listener = receiveListener;
        var sender = sendClient;
        var service = oscQueryService;

        runtimeCancellation = null;
        runtimeTasks = [];
        receiveListener = null;
        sendClient = null;
        oscQueryService = null;
        advertisedEndpoints = new Dictionary<string, OscParameterType>(StringComparer.Ordinal);
        lock (stateGate)
        {
            activeVrChatTarget = null;
            cachedVrChatEndPoint = null;
            localUdpPort = 0;
            localTcpPort = 0;
            localServiceName = string.Empty;
            discoveryState = OscDiscoveryState.Idle;
            nextDiscoveryLogAt = DateTimeOffset.MinValue;
        }

        cancellation.Cancel();
        listener?.Dispose();

        try
        {
            await Task.WhenAll(tasks);
        }
        catch (OperationCanceledException)
        {
        }
        catch (ObjectDisposedException)
        {
        }
        catch (SocketException)
        {
        }
        finally
        {
            cancellation.Dispose();
            listener?.Dispose();
            sender?.Dispose();
            service?.Dispose();
        }
    }

    public async Task SendToVrChatAsync(byte[] packet, CancellationToken cancellationToken = default)
    {
        IPEndPoint endPoint;
        UdpClient client;

        lock (stateGate)
        {
            var target = activeVrChatTarget
                ?? throw new InvalidOperationException("Crystal Relay has not discovered VRChat through OSCQuery yet. Start VRChat with OSC enabled and leave it open so Crystal Relay can find it.");
            client = sendClient ?? throw new InvalidOperationException("OSC sender is not available.");

            if (cachedVrChatEndPoint is null
                || cachedVrChatEndPoint.Port != target.OscPort
                || !cachedVrChatEndPoint.Address.Equals(target.Address))
            {
                cachedVrChatEndPoint = new IPEndPoint(target.Address, target.OscPort);
            }

            endPoint = cachedVrChatEndPoint;
        }

        cancellationToken.ThrowIfCancellationRequested();
        try
        {
            await client.SendAsync(packet, packet.Length, endPoint);
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            throw;
        }
        catch (Exception ex)
        {
            throw CreateTargetLostException("sending OSC actions", ex);
        }
    }

    public async ValueTask DisposeAsync()
    {
        await StopAsync();
        discoveryRefreshGate.Dispose();
    }

    private async Task RunReceiveLoopAsync(UdpClient listener, CancellationToken cancellationToken)
    {
        while (!cancellationToken.IsCancellationRequested)
        {
            try
            {
                var result = await listener.ReceiveAsync(cancellationToken);
                if (!IsLoopbackEndpoint(result.RemoteEndPoint))
                {
                    continue;
                }

                var observedValue = TryReadObservedValue(result.Buffer);
                if (observedValue is not null)
                {
                    ObservedValueReceived?.Invoke(observedValue);
                }
            }
            catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
            {
                return;
            }
            catch (ObjectDisposedException) when (cancellationToken.IsCancellationRequested)
            {
                return;
            }
            catch (SocketException ex)
            {
                if (cancellationToken.IsCancellationRequested)
                {
                    return;
                }

                LogWritten?.Invoke($"OSC receive error: {ex.Message}");
                await Task.Delay(500, cancellationToken);
            }
        }
    }

    private async Task RunDiscoveryLoopAsync(OSCQueryService service, CancellationToken cancellationToken)
    {
        await RefreshDiscoveredServicesAsync(service, cancellationToken, logWhenWaiting: true, forceDiscoveryLog: true);

        using var timer = new PeriodicTimer(ServiceRefreshInterval);

        while (await timer.WaitForNextTickAsync(cancellationToken))
        {
            if (HasDiscoveredVrChat)
            {
                continue;
            }

            await RefreshDiscoveredServicesAsync(service, cancellationToken, logWhenWaiting: false, forceDiscoveryLog: false);
        }
    }

    private async Task RefreshDiscoveredServicesAsync(
        OSCQueryService service,
        CancellationToken cancellationToken,
        bool logWhenWaiting,
        bool forceDiscoveryLog)
    {
        await discoveryRefreshGate.WaitAsync(cancellationToken);

        try
        {
            try
            {
                service.RefreshServices();
            }
            catch (Exception ex) when (!cancellationToken.IsCancellationRequested)
            {
                LogWritten?.Invoke($"OSCQuery discovery refresh failed: {ex.Message}");
                return;
            }

            foreach (var profile in service.GetOSCQueryServices())
            {
                await TryRegisterVrChatTargetAsync(profile, cancellationToken);
            }
        }
        finally
        {
            discoveryRefreshGate.Release();
        }

        if (logWhenWaiting)
        {
            LogDiscoveryWaiting(force: forceDiscoveryLog);
        }
    }

    private void HandleOscQueryServiceAdded(OSCQueryServiceProfile profile)
    {
        var cancellationToken = runtimeCancellation?.Token ?? CancellationToken.None;
        _ = Task.Run(() => TryRegisterVrChatTargetAsync(profile, cancellationToken), CancellationToken.None);
    }

    private async Task TryRegisterVrChatTargetAsync(OSCQueryServiceProfile profile, CancellationToken cancellationToken)
    {
        if (cancellationToken.IsCancellationRequested)
        {
            return;
        }

        if (string.Equals(profile.name, localServiceName, StringComparison.Ordinal))
        {
            return;
        }

        var match = await CreateVrChatTargetAsync(profile, cancellationToken);
        if (match is null)
        {
            return;
        }

        lock (stateGate)
        {
            if (activeVrChatTarget is not null && !ShouldReplaceTarget(activeVrChatTarget, match))
            {
                return;
            }

            activeVrChatTarget = match;
            discoveryState = OscDiscoveryState.Discovered;
        }

        DiscoveryStateChanged?.Invoke(OscDiscoveryState.Discovered);
        LogWritten?.Invoke($"Discovered VRChat through OSCQuery: {match.Name}. Crystal Relay will send actions to {match.Address}:{match.OscPort} and receive values on 127.0.0.1:{localUdpPort}.");
    }

    private async Task<DiscoveredOscTarget?> CreateVrChatTargetAsync(OSCQueryServiceProfile profile, CancellationToken cancellationToken)
    {
        try
        {
            if (!IsLoopbackAddress(profile.address) || !IsValidPort(profile.port))
            {
                return null;
            }

            if (!await LooksLikeVrChatAsync(profile, cancellationToken))
            {
                return null;
            }

            var hostInfo = await VRC.OSCQuery.Extensions.GetHostInfo(profile.address, profile.port);
            if (hostInfo is null
                || !IsValidPort(hostInfo.oscPort)
                || !TryParseLoopbackAddress(hostInfo.oscIP, out var oscAddress))
            {
                return null;
            }

            return new DiscoveredOscTarget(profile.name, oscAddress, hostInfo.oscPort, profile.port);
        }
        catch (Exception ex) when (!cancellationToken.IsCancellationRequested)
        {
            if (profile.name.Contains("vrchat", StringComparison.OrdinalIgnoreCase))
            {
                LogWritten?.Invoke($"Crystal Relay found VRChat's OSCQuery service '{profile.name}', but it is not ready to use yet: {ex.Message}");
            }

            return null;
        }
    }

    private async Task<bool> LooksLikeVrChatAsync(OSCQueryServiceProfile profile, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();

        if (profile.name.Contains("vrchat", StringComparison.OrdinalIgnoreCase))
        {
            return true;
        }

        var tree = await VRC.OSCQuery.Extensions.GetOSCTree(profile.address, profile.port);
        if (tree is null)
        {
            return false;
        }

        return tree.GetNodeWithPath("/avatar/change") is not null
            || tree.GetNodeWithPath("/avatar/eyeheight") is not null
            || tree.GetNodeWithPath("/avatar/parameters") is not null
            || tree.GetNodeWithPath("/input") is not null;
    }

    private void SyncAdvertisedEndpoints(OSCQueryService service, IReadOnlyList<TriggerRuleSnapshot> rules)
    {
        var desiredEndpoints = BuildDesiredEndpoints(rules);

        lock (stateGate)
        {
            foreach (var obsoletePath in advertisedEndpoints.Keys.Except(desiredEndpoints.Keys, StringComparer.Ordinal).ToArray())
            {
                service.RemoveEndpoint(obsoletePath);
                advertisedEndpoints.Remove(obsoletePath);
            }

            foreach (var endpoint in desiredEndpoints)
            {
                if (advertisedEndpoints.TryGetValue(endpoint.Key, out var existingType))
                {
                    if (existingType == endpoint.Value)
                    {
                        continue;
                    }

                    service.RemoveEndpoint(endpoint.Key);
                    advertisedEndpoints.Remove(endpoint.Key);
                }

                AddAdvertisedEndpoint(service, endpoint.Key, endpoint.Value);
                advertisedEndpoints[endpoint.Key] = endpoint.Value;
            }
        }
    }

    private Dictionary<string, OscParameterType> BuildDesiredEndpoints(IReadOnlyList<TriggerRuleSnapshot> rules)
    {
        var endpoints = new Dictionary<string, OscParameterType>(StringComparer.Ordinal)
        {
            ["/avatar/change"] = OscParameterType.String,
            ["/avatar/eyeheight"] = OscParameterType.Float,
            ["/avatar/eyeheightmin"] = OscParameterType.Float,
            ["/avatar/eyeheightmax"] = OscParameterType.Float,
            ["/avatar/eyeheightscalingallowed"] = OscParameterType.Bool
        };

        foreach (var rule in rules.Where(rule => rule.ActionType == OscActionType.AvatarParameter))
        {
            try
            {
                var address = VrChatOscClient.NormalizeAvatarParameterAddress(rule.ParameterName);
                endpoints[address] = rule.ParameterType;
            }
            catch (InvalidOperationException ex)
            {
                LogWritten?.Invoke($"Skipped OSCQuery endpoint for '{rule.Name}' because the avatar parameter path is incomplete: {ex.Message}");
            }
        }

        foreach (var rule in rules.Where(rule => rule.ActionType == OscActionType.SetTrigger))
        {
            foreach (var action in rule.SetTriggerActions.Where(action =>
                         action.ParameterType is OscParameterType.Bool or OscParameterType.Int or OscParameterType.Float
                         && !string.IsNullOrWhiteSpace(action.ParameterName)))
            {
                try
                {
                    var address = VrChatOscClient.NormalizeAvatarParameterAddress(action.ParameterName);
                    if (VrChatLocalAvatarDataService.IsHeightOrScaleParameter(address))
                    {
                        LogWritten?.Invoke($"Skipped Set Trigger OSCQuery endpoint for '{rule.Name}' because {address} is height or avatar scale related.");
                        continue;
                    }

                    if (endpoints.TryGetValue(address, out var existingType))
                    {
                        if (existingType != action.ParameterType)
                        {
                            LogWritten?.Invoke($"Skipped Set Trigger OSCQuery endpoint for '{rule.Name}' because {address} is already tracked as {existingType}, not {action.ParameterType}.");
                        }

                        continue;
                    }

                    endpoints[address] = action.ParameterType;
                }
                catch (InvalidOperationException ex)
                {
                    LogWritten?.Invoke($"Skipped Set Trigger OSCQuery endpoint for '{rule.Name}' because the avatar parameter path is incomplete: {ex.Message}");
                }
            }
        }

        return endpoints;
    }

    private static void AddAdvertisedEndpoint(OSCQueryService service, string path, OscParameterType parameterType)
    {
        const Attributes.AccessValues access = Attributes.AccessValues.WriteOnly;
        const string description = "Crystal Relay listens for this OSC value so Twitch rules can react without fixed VRChat ports.";

        switch (parameterType)
        {
            case OscParameterType.Bool:
                service.AddEndpoint<bool>(path, access, description: description);
                break;
            case OscParameterType.Int:
                service.AddEndpoint<int>(path, access, description: description);
                break;
            case OscParameterType.Float:
                service.AddEndpoint<float>(path, access, description: description);
                break;
            case OscParameterType.String:
                service.AddEndpoint<string>(path, access, description: description);
                break;
            default:
                throw new InvalidOperationException($"Unsupported OSC parameter type: {parameterType}");
        }
    }

    private void LogDiscoveryWaiting(bool force)
    {
        bool hasTarget;

        lock (stateGate)
        {
            hasTarget = activeVrChatTarget is not null;
        }

        if (hasTarget)
        {
            return;
        }

        var now = DateTimeOffset.UtcNow;
        if (!force && now < nextDiscoveryLogAt)
        {
            return;
        }

        nextDiscoveryLogAt = now.Add(DiscoveryLogThrottle);
        LogWritten?.Invoke("Searching for VRChat through OSCQuery. Leave VRChat open with OSC enabled so Crystal Relay can discover it automatically.");
    }

    private InvalidOperationException CreateTargetLostException(string operation, Exception ex)
    {
        MarkTargetLost($"Crystal Relay lost the OSCQuery connection to VRChat while {operation}. It will wait for VRChat to come back automatically.");
        return new InvalidOperationException(
            $"Crystal Relay lost the OSCQuery connection to VRChat while {operation}. Wait a moment for VRChat to finish loading.",
            ex);
    }

    private void MarkTargetLost(string reason)
    {
        var shouldLog = false;
        var shouldNotify = false;

        lock (stateGate)
        {
            if (activeVrChatTarget is null && discoveryState == OscDiscoveryState.Lost)
            {
                return;
            }

            activeVrChatTarget = null;
            cachedVrChatEndPoint = null;
            discoveryState = OscDiscoveryState.Lost;
            nextDiscoveryLogAt = DateTimeOffset.MinValue;
            shouldLog = true;
            shouldNotify = true;
        }

        if (shouldNotify)
        {
            DiscoveryStateChanged?.Invoke(OscDiscoveryState.Lost);
        }

        if (shouldLog)
        {
            LogWritten?.Invoke(reason);
        }
    }

    private static bool ShouldReplaceTarget(DiscoveredOscTarget currentTarget, DiscoveredOscTarget candidate)
    {
        if (currentTarget.Equals(candidate))
        {
            return false;
        }

        var currentLooksLikeVrChat = currentTarget.Name.Contains("vrchat", StringComparison.OrdinalIgnoreCase);
        var candidateLooksLikeVrChat = candidate.Name.Contains("vrchat", StringComparison.OrdinalIgnoreCase);

        if (candidateLooksLikeVrChat)
        {
            return true;
        }

        return !currentLooksLikeVrChat;
    }

    internal static bool IsLoopbackAddress(IPAddress? address)
    {
        // The receiver and OSCQuery library use IPv4, so reject IPv6 endpoints rather than accepting an unusable loopback address.
        return address?.AddressFamily == AddressFamily.InterNetwork && IPAddress.IsLoopback(address);
    }

    internal static bool IsLoopbackEndpoint(IPEndPoint? endPoint)
    {
        return endPoint is not null && IsLoopbackAddress(endPoint.Address);
    }

    private static bool TryParseLoopbackAddress(string? address, out IPAddress parsed)
    {
        if (IPAddress.TryParse(address, out var candidate) && IsLoopbackAddress(candidate))
        {
            parsed = candidate;
            return true;
        }

        parsed = IPAddress.None;
        return false;
    }

    private static bool IsValidPort(int port)
    {
        return port is > 0 and <= 65535;
    }

    private static UdpClient CreateListener(int port, string purpose, string resolutionHint)
    {
        try
        {
            return new UdpClient(new IPEndPoint(IPAddress.Loopback, port));
        }
        catch (SocketException ex) when (ex.SocketErrorCode == SocketError.AddressAlreadyInUse)
        {
            throw new InvalidOperationException(
                $"{purpose} could not start because UDP port {port} is already in use. {resolutionHint}",
                ex);
        }
    }

    private static bool LooksLikePortStartupCollision(Exception exception)
    {
        if (exception is SocketException socketException
            && socketException.SocketErrorCode == SocketError.AddressAlreadyInUse)
        {
            return true;
        }

        return exception.Message.Contains("already in use", StringComparison.OrdinalIgnoreCase)
            || (exception.InnerException is not null && LooksLikePortStartupCollision(exception.InnerException));
    }

    private static OscObservedValue? TryReadObservedValue(byte[] packet)
    {
        var index = 0;
        var address = ReadPaddedString(packet, ref index);
        if (string.IsNullOrWhiteSpace(address))
        {
            return null;
        }

        var typeTags = ReadPaddedString(packet, ref index);
        if (string.IsNullOrWhiteSpace(typeTags) || typeTags[0] != ',' || typeTags.Length < 2)
        {
            return null;
        }

        return typeTags[1] switch
        {
            'T' => new OscObservedValue(address, OscParameterType.Bool, true),
            'F' => new OscObservedValue(address, OscParameterType.Bool, false),
            'i' when index + 4 <= packet.Length => new OscObservedValue(
                address,
                OscParameterType.Int,
                BinaryPrimitives.ReadInt32BigEndian(packet.AsSpan(index, 4))),
            'f' when index + 4 <= packet.Length => new OscObservedValue(
                address,
                OscParameterType.Float,
                ReadSingleBigEndian(packet, index)),
            's' => new OscObservedValue(address, OscParameterType.String, ReadPaddedString(packet, ref index)),
            _ => null
        };
    }

    private static string ReadPaddedString(byte[] packet, ref int index)
    {
        if (index >= packet.Length)
        {
            return string.Empty;
        }

        var end = index;
        while (end < packet.Length && packet[end] != 0)
        {
            end++;
        }

        var value = Encoding.UTF8.GetString(packet, index, end - index);
        if (end >= packet.Length)
        {
            index = packet.Length;
            return value;
        }

        end++;
        while (end % 4 != 0 && end < packet.Length)
        {
            end++;
        }

        index = end;
        return value;
    }

    private static float ReadSingleBigEndian(byte[] packet, int index)
    {
        var bits = BinaryPrimitives.ReadInt32BigEndian(packet.AsSpan(index, 4));
        return BitConverter.Int32BitsToSingle(bits);
    }

    private static void CollectAvatarParameters(OSCQueryNode node, ICollection<VrChatOscParameterSummary> parameters)
    {
        if (node.Contents is not null)
        {
            foreach (var child in node.Contents.Values)
            {
                CollectAvatarParameters(child, parameters);
            }
        }

        if (TryMapParameterType(node.OscType, out var parameterType))
        {
            parameters.Add(new VrChatOscParameterSummary(
                node.FullPath ?? string.Empty,
                node.Name,
                parameterType));
        }
    }

    private static OscObservedValue? TryReadNodeValue(OSCQueryNode node)
    {
        if (node is null || string.IsNullOrWhiteSpace(node.FullPath) || !TryMapParameterType(node.OscType, out var parameterType))
        {
            return null;
        }

        var rawValue = node.Value is { Length: > 0 } ? node.Value[0] : null;
        if (rawValue is null)
        {
            return null;
        }

        return parameterType switch
        {
            OscParameterType.Bool when TryConvertToBool(rawValue, out var boolValue)
                => new OscObservedValue(node.FullPath, OscParameterType.Bool, boolValue),
            OscParameterType.Int when TryConvertToInt(rawValue, out var intValue)
                => new OscObservedValue(node.FullPath, OscParameterType.Int, intValue),
            OscParameterType.Float when TryConvertToFloat(rawValue, out var floatValue)
                => new OscObservedValue(node.FullPath, OscParameterType.Float, floatValue),
            OscParameterType.String => new OscObservedValue(node.FullPath, OscParameterType.String, rawValue.ToString() ?? string.Empty),
            _ => null
        };
    }

    private static bool TryConvertToBool(object rawValue, out bool value)
    {
        switch (rawValue)
        {
            case bool boolValue:
                value = boolValue;
                return true;
            case string stringValue when bool.TryParse(stringValue, out var parsedBool):
                value = parsedBool;
                return true;
            case string stringValue when int.TryParse(stringValue, NumberStyles.Integer, CultureInfo.InvariantCulture, out var parsedInt):
                value = parsedInt != 0;
                return true;
            case long longValue:
                value = longValue != 0;
                return true;
            case int intValue:
                value = intValue != 0;
                return true;
            case double doubleValue:
                value = Math.Abs(doubleValue) > double.Epsilon;
                return true;
            case float floatValue:
                value = Math.Abs(floatValue) > float.Epsilon;
                return true;
            default:
                value = false;
                return false;
        }
    }

    private static bool TryConvertToInt(object rawValue, out int value)
    {
        switch (rawValue)
        {
            case int intValue:
                value = intValue;
                return true;
            case long longValue when longValue is >= int.MinValue and <= int.MaxValue:
                value = (int)longValue;
                return true;
            case double doubleValue when doubleValue >= int.MinValue && doubleValue <= int.MaxValue:
                value = (int)Math.Round(doubleValue, MidpointRounding.AwayFromZero);
                return true;
            case float floatValue when floatValue >= int.MinValue && floatValue <= int.MaxValue:
                value = (int)Math.Round(floatValue, MidpointRounding.AwayFromZero);
                return true;
            case string stringValue when int.TryParse(stringValue, NumberStyles.Integer, CultureInfo.InvariantCulture, out var parsedInt):
                value = parsedInt;
                return true;
            default:
                value = 0;
                return false;
        }
    }

    private static bool TryConvertToFloat(object rawValue, out float value)
    {
        switch (rawValue)
        {
            case float floatValue:
                value = floatValue;
                return true;
            case double doubleValue when doubleValue >= float.MinValue && doubleValue <= float.MaxValue:
                value = (float)doubleValue;
                return true;
            case long longValue when longValue >= float.MinValue && longValue <= float.MaxValue:
                value = longValue;
                return true;
            case int intValue:
                value = intValue;
                return true;
            case string stringValue when float.TryParse(stringValue, NumberStyles.Float | NumberStyles.AllowThousands, CultureInfo.InvariantCulture, out var parsedFloat):
                value = parsedFloat;
                return true;
            default:
                value = 0f;
                return false;
        }
    }

    private static bool TryMapParameterType(string? oscType, out OscParameterType parameterType)
    {
        parameterType = OscParameterType.Int;
        if (string.IsNullOrWhiteSpace(oscType))
        {
            return false;
        }

        switch (oscType.Trim())
        {
            case "T":
            case "F":
                parameterType = OscParameterType.Bool;
                return true;
            case "i":
                parameterType = OscParameterType.Int;
                return true;
            case "f":
                parameterType = OscParameterType.Float;
                return true;
            case "s":
                parameterType = OscParameterType.String;
                return true;
            default:
                return false;
        }
    }

    private sealed record DiscoveredOscTarget(string Name, IPAddress Address, int OscPort, int QueryPort);
}
