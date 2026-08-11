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
    private readonly SemaphoreSlim lifecycleGate = new(1, 1);
    private readonly SemaphoreSlim discoveryRefreshGate = new(1, 1);
    private readonly AsyncLocal<Task?> currentRuntimeTask = new();
    private readonly AsyncLocal<SessionLease?> currentSessionLease = new();
    private readonly Func<OSCQueryServiceProfile, CancellationToken, Task<DiscoveredOscTarget?>>? targetFactoryOverride;
    private readonly Func<OSCQueryService, Task>? startupBeforePublicationHook;
    private readonly Func<UdpClient, byte[], IPEndPoint, CancellationToken, Task>? sendAsyncOverride;
    private readonly Func<Task>? stopBeforeLifecycleAdmissionHook;

    private CancellationTokenSource? runtimeCancellation;
    private Task[] runtimeTasks = [];
    private readonly List<SessionLease> activeSessionLeases = [];
    private TaskCompletionSource<bool> sessionLeaseSignal = CreateSignal();
    private int lifecycleGateUsers;
    private TaskCompletionSource<bool>? lifecycleGateDrained;
    private bool isDisposing;
    private bool isDisposed;
    private Task? disposeTask;
    private bool stopRequested;
    private TaskCompletionSource<bool>? stopCompletion;
    private TaskCompletionSource<bool>? stopStateCleared;
    private OSCQueryService? stoppingService;
    private CancellationTokenSource? stoppingCancellation;
    private UdpClient? sendClient;
    private UdpClient? receiveListener;
    private OSCQueryService? oscQueryService;
    private Action<OSCQueryServiceProfile>? oscQueryServiceAddedHandler;
    private Dictionary<string, OscParameterType> advertisedEndpoints = new(StringComparer.Ordinal);
    private DiscoveredOscTarget? activeVrChatTarget;
    private IPEndPoint? cachedVrChatEndPoint;
    private int localUdpPort;
    private int localTcpPort;
    private string localServiceName = string.Empty;
    private DateTimeOffset nextDiscoveryLogAt = DateTimeOffset.MinValue;
    private OscDiscoveryState discoveryState = OscDiscoveryState.Idle;

    public OscRouterService()
        : this(null, null)
    {
    }

    internal OscRouterService(
        Func<OSCQueryServiceProfile, CancellationToken, Task<DiscoveredOscTarget?>>? targetFactory,
        Func<OSCQueryService, Task>? startupBeforePublicationHook,
        Func<UdpClient, byte[], IPEndPoint, CancellationToken, Task>? sendAsyncOverride = null,
        Func<Task>? stopBeforeLifecycleAdmissionHook = null)
    {
        targetFactoryOverride = targetFactory;
        this.startupBeforePublicationHook = startupBeforePublicationHook;
        this.sendAsyncOverride = sendAsyncOverride;
        this.stopBeforeLifecycleAdmissionHook = stopBeforeLifecycleAdmissionHook;
    }

    public event Action<string>? LogWritten;

    public event Action<OscObservedValue>? ObservedValueReceived;

    public event Action<OscDiscoveryState>? DiscoveryStateChanged;

    public bool IsRunning => HasRuntimeState();

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

    private bool HasRuntimeState()
    {
        return runtimeCancellation is not null
            || runtimeTasks.Length > 0
            || receiveListener is not null
            || sendClient is not null
            || oscQueryService is not null;
    }

    public async Task StartAsync(IReadOnlyList<TriggerRuleSnapshot> rules, CancellationToken cancellationToken = default)
    {
        await EnterLifecycleGateAsync(cancellationToken, allowDisposing: false).ConfigureAwait(false);
        string? startupLog = null;
        string[] startupWarnings = [];
        OSCQueryService? publishedService = null;
        CancellationTokenSource? publishedCancellation = null;
        var publishedSessionToken = CancellationToken.None;

        try
        {
            if (HasRuntimeState())
            {
                return;
            }

            UdpClient? listener = null;
            UdpClient? sender = null;
            OSCQueryService? service = null;
            CancellationTokenSource? cancellationSource = null;
            Task[] startedTasks = [];
            Dictionary<string, OscParameterType>? stagedAdvertisedEndpoints = null;
            var udpPort = 0;
            var tcpPort = 0;
            var serviceName = string.Empty;
            List<string>? stagedStartupWarnings = null;
            Exception? startupException = null;

            try
            {
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

                        serviceName = $"Crystal Relay Twitch to OSC ({Environment.ProcessId})";
                        service = new OSCQueryServiceBuilder()
                            .WithServiceName(serviceName)
                            .WithHostIP(IPAddress.Loopback)
                            .WithOscIP(IPAddress.Loopback)
                            .WithTcpPort(tcpPort)
                            .WithUdpPort(udpPort)
                            .WithDefaults()
                            .Build();

                        var serviceAdvertisedEndpoints = new Dictionary<string, OscParameterType>(StringComparer.Ordinal);
                        var startupMessages = new List<string>();
                        var desiredEndpoints = BuildDesiredEndpoints(rules, startupMessages.Add);
                        SyncAdvertisedEndpointsForState(service, desiredEndpoints, serviceAdvertisedEndpoints);
                        stagedAdvertisedEndpoints = serviceAdvertisedEndpoints;
                        stagedStartupWarnings = startupMessages;
                        if (startupBeforePublicationHook is not null)
                        {
                            await startupBeforePublicationHook(service).ConfigureAwait(false);
                        }

                        startupException = null;
                        break;
                    }
                    catch (Exception ex) when (attempt < StartupRetryCount && LooksLikePortStartupCollision(ex))
                    {
                        listener?.Dispose();
                        service?.Dispose();
                        listener = null;
                        service = null;
                        stagedAdvertisedEndpoints = null;
                        stagedStartupWarnings = null;
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

                cancellationToken.ThrowIfCancellationRequested();
                var localListener = listener;
                var localService = service;
                var cancellation = new CancellationTokenSource();
                cancellationSource = cancellation;
                sender = new UdpClient();

                if (stagedAdvertisedEndpoints is null)
                {
                    throw new InvalidOperationException("Crystal Relay could not prepare its OSCQuery endpoints.");
                }

                var sessionToken = cancellation.Token;
                lock (stateGate)
                {
                    var serviceAddedHandler = new Action<OSCQueryServiceProfile>(
                        profile => HandleOscQueryServiceAdded(localService, profile, cancellation, sessionToken));

                    runtimeCancellation = cancellation;
                    runtimeTasks = [];
                    receiveListener = localListener;
                    sendClient = sender;
                    oscQueryService = localService;
                    oscQueryServiceAddedHandler = serviceAddedHandler;
                    advertisedEndpoints = stagedAdvertisedEndpoints;
                    activeVrChatTarget = null;
                    cachedVrChatEndPoint = null;
                    localUdpPort = udpPort;
                    localTcpPort = tcpPort;
                    localServiceName = serviceName;
                    nextDiscoveryLogAt = DateTimeOffset.MinValue;
                    discoveryState = OscDiscoveryState.Discovering;
                    localService.OnOscQueryServiceAdded += serviceAddedHandler;

                    publishedService = localService;
                    publishedCancellation = cancellation;
                    publishedSessionToken = sessionToken;
                }

                listener = null;
                service = null;
                sender = null;
                cancellationSource = null;

                var receiveTask = CreateTrackedRuntimeTask(
                    () => RunReceiveLoopAsync(localListener, localService, cancellation, sessionToken),
                    localService,
                    cancellation,
                    cancellation.Token);
                startedTasks = [receiveTask];
                var discoveryTask = CreateTrackedRuntimeTask(
                    () => RunDiscoveryLoopAsync(localService, cancellation, sessionToken),
                    localService,
                    cancellation,
                    cancellation.Token);
                startedTasks = [receiveTask, discoveryTask];
                lock (stateGate)
                {
                    runtimeTasks = [.. runtimeTasks, .. startedTasks];
                }

                startupLog = $"OSCQuery service '{serviceName}' is live. Crystal Relay is listening for VRChat values on UDP {udpPort} and serving OSCQuery on TCP {tcpPort}.";
                startupWarnings = stagedStartupWarnings?.ToArray() ?? [];
            }
            catch
            {
                try
                {
                    if (HasRuntimeState())
                    {
                        await StopCoreAsync().ConfigureAwait(false);
                    }
                    else
                    {
                        lock (stateGate)
                        {
                            advertisedEndpoints = new Dictionary<string, OscParameterType>(StringComparer.Ordinal);
                        }

                        cancellationSource?.Cancel();
                    }

                    try
                    {
                        await Task.WhenAll(startedTasks).ConfigureAwait(false);
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
                }
                finally
                {
                    cancellationSource?.Dispose();
                    listener?.Dispose();
                    sender?.Dispose();
                    service?.Dispose();
                }

                throw;
            }
        }
        finally
        {
            ExitLifecycleGate();
        }

        try
        {
            if (startupLog is not null
                && publishedService is not null
                && publishedCancellation is not null)
            {
                PublishSessionLog(publishedService, publishedCancellation, publishedSessionToken, startupLog);
                foreach (var warning in startupWarnings)
                {
                    PublishSessionLog(publishedService, publishedCancellation, publishedSessionToken, warning);
                }
                LogDiscoveryWaiting(
                    publishedService,
                    publishedCancellation,
                    publishedSessionToken,
                    force: true);
            }
        }
        catch
        {
            await StopAsync().ConfigureAwait(false);
            throw;
        }
    }

    public void UpdateRuleSubscriptions(IReadOnlyList<TriggerRuleSnapshot> rules)
    {
        var operationLease = TryEnterSessionOperation();
        if (operationLease is null)
        {
            return;
        }

        try
        {
            SyncAdvertisedEndpoints(
                operationLease.Lease.Service,
                operationLease.Lease.Cancellation,
                operationLease.Lease.Cancellation.Token,
                rules);
        }
        finally
        {
            operationLease.Dispose();
        }
    }

    public async Task ForceRefreshAsync(CancellationToken cancellationToken = default)
    {
        var operationLease = EnterSessionOperation(cancellationToken);
        try
        {
            await ForceRefreshCoreAsync(cancellationToken, operationLease.Lease).ConfigureAwait(false);
        }
        finally
        {
            operationLease.Dispose();
        }
    }

    private async Task ForceRefreshCoreAsync(CancellationToken cancellationToken, SessionLease operationLease)
    {
        var service = operationLease.Service;
        var sessionCancellation = operationLease.Cancellation;
        CancellationToken sessionToken;

        lock (stateGate)
        {
            if (!IsCurrentRuntimeSessionLocked(service, sessionCancellation, sessionCancellation.Token))
            {
                throw new InvalidOperationException("The OSCQuery session changed while Crystal Relay was preparing a refresh.");
            }

            sessionToken = sessionCancellation.Token;
            activeVrChatTarget = null;
            cachedVrChatEndPoint = null;
            discoveryState = OscDiscoveryState.Discovering;
            nextDiscoveryLogAt = DateTimeOffset.MinValue;
        }

        using var operationCancellation = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken, sessionToken);
        var operationToken = operationCancellation.Token;
        operationToken.ThrowIfCancellationRequested();
        PublishSessionLog(service, sessionCancellation, operationToken, "Forcing an OSCQuery refresh so Crystal Relay can reconnect to VRChat.");
        await RefreshDiscoveredServicesAsync(
            service,
            sessionCancellation,
            operationToken,
            logWhenWaiting: true,
            forceDiscoveryLog: true);
        ThrowIfSessionIsNotCurrent(service, sessionCancellation, operationToken);
    }

    public async Task<IReadOnlyList<VrChatOscParameterSummary>> GetCurrentAvatarParametersAsync(CancellationToken cancellationToken = default)
    {
        var operationLease = EnterSessionOperation(cancellationToken);
        try
        {
            return await GetCurrentAvatarParametersCoreAsync(cancellationToken, operationLease.Lease).ConfigureAwait(false);
        }
        finally
        {
            operationLease.Dispose();
        }
    }

    private async Task<IReadOnlyList<VrChatOscParameterSummary>> GetCurrentAvatarParametersCoreAsync(
        CancellationToken cancellationToken,
        SessionLease operationLease)
    {
        var service = operationLease.Service;
        var sessionCancellation = operationLease.Cancellation;
        CancellationToken sessionToken;
        var shouldRefreshDiscovery = false;
        lock (stateGate)
        {
            if (!IsCurrentRuntimeSessionLocked(service, sessionCancellation, sessionCancellation.Token))
            {
                throw new InvalidOperationException("The OSCQuery session changed while Crystal Relay was preparing an avatar read.");
            }

            sessionToken = sessionCancellation.Token;
            shouldRefreshDiscovery = activeVrChatTarget is null;
        }

        using var operationCancellation = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken, sessionToken);
        var operationToken = operationCancellation.Token;
        if (shouldRefreshDiscovery)
        {
            await RefreshDiscoveredServicesAsync(
                service,
                sessionCancellation,
                operationToken,
                logWhenWaiting: true,
                forceDiscoveryLog: true);
            ThrowIfSessionIsNotCurrent(service, sessionCancellation, operationToken);
        }

        var target = GetCurrentTarget(
            service,
            sessionCancellation,
            operationToken,
            "Crystal Relay could not find VRChat through OSCQuery yet. Open VRChat with OSC enabled, then try refreshing again.");

        OSCQueryRootNode? tree;
        try
        {
            tree = await VRC.OSCQuery.Extensions.GetOSCTree(target.Address, target.QueryPort);
        }
        catch (OperationCanceledException) when (operationToken.IsCancellationRequested)
        {
            throw;
        }
        catch (Exception ex)
        {
            ThrowIfSessionIsNotCurrent(service, sessionCancellation, operationToken);
            throw CreateTargetLostException("reading live avatar parameters", service, sessionCancellation, operationToken, ex);
        }

        ThrowIfSessionIsNotCurrent(service, sessionCancellation, operationToken);
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
        var operationLease = EnterSessionOperation(cancellationToken);
        try
        {
            return await GetCurrentOscValueCoreAsync(normalizedAddress, cancellationToken, operationLease.Lease).ConfigureAwait(false);
        }
        finally
        {
            operationLease.Dispose();
        }
    }

    public async Task<OscObservedValue?> GetCurrentOscValueAsync(string address, CancellationToken cancellationToken = default)
    {
        var operationLease = EnterSessionOperation(cancellationToken);
        try
        {
            return await GetCurrentOscValueCoreAsync(address, cancellationToken, operationLease.Lease).ConfigureAwait(false);
        }
        finally
        {
            operationLease.Dispose();
        }
    }

    private async Task<OscObservedValue?> GetCurrentOscValueCoreAsync(
        string address,
        CancellationToken cancellationToken,
        SessionLease operationLease)
    {
        var service = operationLease.Service;
        var sessionCancellation = operationLease.Cancellation;
        CancellationToken sessionToken;
        var shouldRefreshDiscovery = false;

        lock (stateGate)
        {
            if (!IsCurrentRuntimeSessionLocked(service, sessionCancellation, sessionCancellation.Token))
            {
                throw new InvalidOperationException("The OSCQuery session changed while Crystal Relay was preparing an OSC read.");
            }

            sessionToken = sessionCancellation.Token;
            shouldRefreshDiscovery = activeVrChatTarget is null;
        }

        using var operationCancellation = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken, sessionToken);
        var operationToken = operationCancellation.Token;
        if (shouldRefreshDiscovery)
        {
            await RefreshDiscoveredServicesAsync(
                service,
                sessionCancellation,
                operationToken,
                logWhenWaiting: true,
                forceDiscoveryLog: true);
            ThrowIfSessionIsNotCurrent(service, sessionCancellation, operationToken);
        }

        var target = GetCurrentTarget(
            service,
            sessionCancellation,
            operationToken,
            "Crystal Relay could not find VRChat through OSCQuery yet. Open VRChat with OSC enabled, then try again.");

        var normalizedAddress = VrChatOscClient.NormalizeOscAddress(address);
        OSCQueryRootNode? tree;
        try
        {
            tree = await VRC.OSCQuery.Extensions.GetOSCTree(target.Address, target.QueryPort);
        }
        catch (OperationCanceledException) when (operationToken.IsCancellationRequested)
        {
            throw;
        }
        catch (Exception ex)
        {
            ThrowIfSessionIsNotCurrent(service, sessionCancellation, operationToken);
            throw CreateTargetLostException(
                $"reading the live value for {normalizedAddress}",
                service,
                sessionCancellation,
                operationToken,
                ex);
        }

        ThrowIfSessionIsNotCurrent(service, sessionCancellation, operationToken);
        var node = tree?.GetNodeWithPath(normalizedAddress)
            ?? throw new InvalidOperationException($"VRChat did not expose {normalizedAddress} through OSCQuery yet.");

        return TryReadNodeValue(node);
    }

    public async Task StopAsync()
    {
        TaskCompletionSource<bool> completion;
        Task waitTask;
        SessionLease[] excludedLeases;
        var ownsStop = false;
        lock (stateGate)
        {
            if (isDisposed)
            {
                return;
            }

            if (stopCompletion is not null)
            {
                waitTask = IsCurrentSessionContextLocked()
                    ? stopStateCleared?.Task ?? stopCompletion.Task
                    : stopCompletion.Task;
                completion = stopCompletion;
                excludedLeases = [];
            }
            else
            {
                completion = CreateSignal();
                stopCompletion = completion;
                stopStateCleared = CreateSignal();
                stopRequested = true;
                lifecycleGateUsers++;
                excludedLeases = GetCurrentSessionLeasesLocked();
                waitTask = completion.Task;
                ownsStop = true;
            }
        }

        if (!ownsStop)
        {
            await waitTask.ConfigureAwait(false);
            return;
        }

        var enteredLifecycleGate = false;
        var lifecycleAdmissionReserved = ownsStop;
        try
        {
            if (stopBeforeLifecycleAdmissionHook is not null)
            {
                await stopBeforeLifecycleAdmissionHook().ConfigureAwait(false);
            }

            await lifecycleGate.WaitAsync().ConfigureAwait(false);
            enteredLifecycleGate = true;
            lifecycleAdmissionReserved = false;
            await StopCoreAsync(excludedLeases).ConfigureAwait(false);
            completion.TrySetResult(true);
        }
        catch (Exception ex)
        {
            completion.TrySetException(ex);
            throw;
        }
        finally
        {
            if (enteredLifecycleGate)
            {
                ExitLifecycleGate();
            }
            else if (lifecycleAdmissionReserved)
            {
                ReleaseLifecycleGateAdmission();
            }

            lock (stateGate)
            {
                if (ReferenceEquals(stopCompletion, completion))
                {
                    stopCompletion = null;
                    stopStateCleared = null;
                    stopRequested = isDisposing;
                    stoppingService = null;
                    stoppingCancellation = null;
                }
            }
        }
    }

    private async Task StopCoreAsync(IReadOnlyCollection<SessionLease>? excludedLeases = null)
    {
        var cancellation = runtimeCancellation;
        var tasks = runtimeTasks;
        var listener = receiveListener;
        var sender = sendClient;
        var service = oscQueryService;
        var serviceAddedHandler = oscQueryServiceAddedHandler;
        excludedLeases ??= new HashSet<SessionLease>();

        lock (stateGate)
        {
            cancellation = runtimeCancellation;
            tasks = runtimeTasks;
            listener = receiveListener;
            sender = sendClient;
            service = oscQueryService;
            serviceAddedHandler = oscQueryServiceAddedHandler;

            stoppingService = service;
            stoppingCancellation = cancellation;
            runtimeCancellation = null;
            runtimeTasks = [];
            receiveListener = null;
            sendClient = null;
            oscQueryService = null;
            oscQueryServiceAddedHandler = null;
            advertisedEndpoints = new Dictionary<string, OscParameterType>(StringComparer.Ordinal);
            activeVrChatTarget = null;
            cachedVrChatEndPoint = null;
            localUdpPort = 0;
            localTcpPort = 0;
            localServiceName = string.Empty;
            discoveryState = OscDiscoveryState.Idle;
            nextDiscoveryLogAt = DateTimeOffset.MinValue;
            stopStateCleared?.TrySetResult(true);
        }

        if (currentRuntimeTask.Value is Task taskToSkip
            && tasks.Any(task => ReferenceEquals(task, taskToSkip)))
        {
            tasks = tasks.Where(task => !ReferenceEquals(task, taskToSkip)).ToArray();
        }

        if (service is not null && serviceAddedHandler is not null)
        {
            service.OnOscQueryServiceAdded -= serviceAddedHandler;
        }

        try
        {
            try
            {
                cancellation?.Cancel();
            }
            catch (Exception ex)
            {
                DebugLogService.Write(
                    $"OSC router cleanup cancellation callback failed: {SensitiveTextSanitizer.Sanitize(ex.Message)}");
            }

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
            catch (Exception ex)
            {
                DebugLogService.Write(
                    $"OSC router cleanup task failed: {SensitiveTextSanitizer.Sanitize(ex.Message)}");
            }

            await WaitForForeignSessionLeasesAsync(service, cancellation, excludedLeases).ConfigureAwait(false);
        }
        finally
        {
            cancellation?.Dispose();
            listener?.Dispose();
            sender?.Dispose();
            service?.Dispose();
        }
    }

    public async Task SendToVrChatAsync(byte[] packet, CancellationToken cancellationToken = default)
    {
        var operationLease = EnterSessionOperation(cancellationToken);
        try
        {
            var service = operationLease.Lease.Service;
            var sessionCancellation = operationLease.Lease.Cancellation;
            var sessionToken = sessionCancellation.Token;
            IPEndPoint endPoint;
            UdpClient client;
            lock (stateGate)
            {
                if (!IsCurrentRuntimeSessionLocked(service, sessionCancellation, sessionToken))
                {
                    throw new InvalidOperationException("The OSCQuery session changed while Crystal Relay was preparing to send an OSC action.");
                }

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

            using var operationCancellation = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken, sessionToken);
            var operationToken = operationCancellation.Token;
            operationToken.ThrowIfCancellationRequested();
            try
            {
                if (sendAsyncOverride is not null)
                {
                    await sendAsyncOverride(client, packet, endPoint, operationToken).ConfigureAwait(false);
                }
                else
                {
                    await client.SendAsync(packet, packet.Length, endPoint).ConfigureAwait(false);
                }

                ThrowIfSessionIsNotCurrent(service, sessionCancellation, operationToken);
            }
            catch (OperationCanceledException) when (operationToken.IsCancellationRequested)
            {
                throw;
            }
            catch (Exception ex)
            {
                ThrowIfSessionIsNotCurrent(service, sessionCancellation, operationToken);
                throw CreateTargetLostException(
                    "sending OSC actions",
                    service,
                    sessionCancellation,
                    operationToken,
                    ex);
            }
        }
        finally
        {
            operationLease.Dispose();
        }
    }

    public ValueTask DisposeAsync()
    {
        Task disposalTask;
        TaskCompletionSource<bool> disposalCompletion;
        SessionLease[] excludedLeases;
        var startDisposal = false;
        lock (stateGate)
        {
            if (disposeTask is not null)
            {
                return new ValueTask(disposeTask);
            }

            isDisposing = true;
            if (stopCompletion is null)
            {
                stopCompletion = CreateSignal();
                stopStateCleared = CreateSignal();
            }

            stopRequested = true;
            disposalCompletion = CreateSignal();
            excludedLeases = GetCurrentSessionLeasesLocked();
            disposeTask = disposalCompletion.Task;
            disposalTask = disposalCompletion.Task;
            startDisposal = true;
        }

        if (startDisposal)
        {
            _ = RunDisposeAsync(disposalCompletion, excludedLeases);
        }

        return new ValueTask(disposalTask);
    }

    private static TaskCompletionSource<bool> CreateSignal()
    {
        return new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
    }

    private async Task EnterLifecycleGateAsync(CancellationToken cancellationToken, bool allowDisposing)
    {
        var acquired = false;
        lock (stateGate)
        {
            if (isDisposed || isDisposing)
            {
                throw new ObjectDisposedException(nameof(OscRouterService));
            }

            if (!allowDisposing && stopRequested)
            {
                throw new InvalidOperationException("The OSCQuery router is stopping and cannot start a new session yet.");
            }

            lifecycleGateUsers++;
        }

        try
        {
            await lifecycleGate.WaitAsync(cancellationToken).ConfigureAwait(false);
            acquired = true;
            lock (stateGate)
            {
                if (isDisposed || isDisposing)
                {
                    throw new ObjectDisposedException(nameof(OscRouterService));
                }

                if (!allowDisposing && stopRequested)
                {
                    throw new InvalidOperationException("The OSCQuery router is stopping and cannot start a new session yet.");
                }
            }
        }
        catch
        {
            if (acquired)
            {
                lifecycleGate.Release();
            }

            ReleaseLifecycleGateAdmission();

            throw;
        }
    }

    private void ExitLifecycleGate()
    {
        lifecycleGate.Release();
        ReleaseLifecycleGateAdmission();
    }

    private void ReleaseLifecycleGateAdmission()
    {
        lock (stateGate)
        {
            lifecycleGateUsers--;
            if (lifecycleGateUsers == 0)
            {
                lifecycleGateDrained?.TrySetResult(true);
                lifecycleGateDrained = null;
            }
        }
    }

    private Task WaitForLifecycleGateUsersToDrainAsync()
    {
        lock (stateGate)
        {
            if (lifecycleGateUsers == 0)
            {
                return Task.CompletedTask;
            }

            lifecycleGateDrained ??= CreateSignal();
            return lifecycleGateDrained.Task;
        }
    }

    private SessionLeaseScope EnterSessionOperation(CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        lock (stateGate)
        {
            if (isDisposed || isDisposing)
            {
                throw new ObjectDisposedException(nameof(OscRouterService));
            }

            if (stopRequested
                || oscQueryService is null
                || runtimeCancellation is null)
            {
                throw new InvalidOperationException("OSCQuery is not running yet. Start OSC testing or the background bridge first.");
            }

            return EnterSessionLeaseLocked(
                oscQueryService,
                runtimeCancellation,
                SessionLeaseKind.Operation);
        }
    }

    private SessionLeaseScope? TryEnterSessionOperation()
    {
        lock (stateGate)
        {
            if (isDisposed
                || isDisposing
                || stopRequested
                || oscQueryService is null
                || runtimeCancellation is null)
            {
                return null;
            }

            return EnterSessionLeaseLocked(
                oscQueryService,
                runtimeCancellation,
                SessionLeaseKind.Operation);
        }
    }

    private SessionLeaseScope? TryEnterRuntimeTaskLease(
        OSCQueryService service,
        CancellationTokenSource sessionCancellation,
        CancellationToken sessionToken)
    {
        lock (stateGate)
        {
            if (!IsCurrentRuntimeSessionLocked(service, sessionCancellation, sessionToken))
            {
                return null;
            }

            return EnterSessionLeaseLocked(service, sessionCancellation, SessionLeaseKind.RuntimeTask);
        }
    }

    private SessionLeaseScope? TryEnterNotificationLease(
        OSCQueryService service,
        CancellationTokenSource sessionCancellation,
        CancellationToken operationToken)
    {
        lock (stateGate)
        {
            if (!IsCurrentRuntimeSessionLocked(service, sessionCancellation, operationToken))
            {
                return null;
            }

            return EnterSessionLeaseLocked(service, sessionCancellation, SessionLeaseKind.Notification);
        }
    }

    private SessionLeaseScope EnterSessionLeaseLocked(
        OSCQueryService service,
        CancellationTokenSource sessionCancellation,
        SessionLeaseKind kind)
    {
        var previous = currentSessionLease.Value;
        var lease = new SessionLease(service, sessionCancellation, kind, previous);
        activeSessionLeases.Add(lease);

        currentSessionLease.Value = lease;
        return new SessionLeaseScope(this, lease, previous);
    }

    private void ReleaseSessionLease(SessionLease lease)
    {
        lock (stateGate)
        {
            if (!activeSessionLeases.Remove(lease))
            {
                return;
            }

            var signal = sessionLeaseSignal;
            sessionLeaseSignal = CreateSignal();
            signal.TrySetResult(true);
        }
    }

    private SessionLease[] GetCurrentSessionLeasesLocked()
    {
        var service = oscQueryService;
        var cancellation = runtimeCancellation;
        if (service is null || cancellation is null)
        {
            return [];
        }

        var leases = new HashSet<SessionLease>();
        for (var lease = currentSessionLease.Value; lease is not null; lease = lease.Previous)
        {
            if (ReferenceEquals(lease.Service, service)
                && ReferenceEquals(lease.Cancellation, cancellation))
            {
                leases.Add(lease);
            }
        }

        return [.. leases];
    }

    private bool IsCurrentSessionContextLocked()
    {
        var service = oscQueryService ?? stoppingService;
        var cancellation = runtimeCancellation ?? stoppingCancellation;
        if (service is null || cancellation is null)
        {
            return false;
        }

        for (var lease = currentSessionLease.Value; lease is not null; lease = lease.Previous)
        {
            if (ReferenceEquals(lease.Service, service)
                && ReferenceEquals(lease.Cancellation, cancellation))
            {
                return true;
            }
        }

        return false;
    }

    private async Task WaitForForeignSessionLeasesAsync(
        OSCQueryService? service,
        CancellationTokenSource? sessionCancellation,
        IReadOnlyCollection<SessionLease> excludedLeases)
    {
        if (service is null || sessionCancellation is null)
        {
            return;
        }

        while (true)
        {
            Task signal;
            lock (stateGate)
            {
                if (!activeSessionLeases.Any(lease =>
                        ReferenceEquals(lease.Service, service)
                        && ReferenceEquals(lease.Cancellation, sessionCancellation)
                        && !excludedLeases.Contains(lease)))
                {
                    return;
                }

                signal = sessionLeaseSignal.Task;
            }

            await signal.ConfigureAwait(false);
        }
    }

    private async Task RunDisposeAsync(
        TaskCompletionSource<bool> disposalCompletion,
        IReadOnlyCollection<SessionLease> excludedLeases)
    {
        var gateOwned = false;
        var gatesDisposed = false;
        try
        {
            await WaitForLifecycleGateUsersToDrainAsync().ConfigureAwait(false);
            await lifecycleGate.WaitAsync().ConfigureAwait(false);
            gateOwned = true;

            await StopCoreAsync(excludedLeases).ConfigureAwait(false);

            TaskCompletionSource<bool>? pendingStop;
            lock (stateGate)
            {
                pendingStop = stopCompletion;
                stopCompletion = null;
                stopStateCleared = null;
                stoppingService = null;
                stoppingCancellation = null;
                isDisposed = true;
            }

            pendingStop?.TrySetResult(true);
            discoveryRefreshGate.Dispose();
            lifecycleGate.Dispose();
            gatesDisposed = true;
            disposalCompletion.TrySetResult(true);
        }
        catch (Exception ex)
        {
            disposalCompletion.TrySetException(ex);
        }
        finally
        {
            if (gateOwned && !gatesDisposed)
            {
                lifecycleGate.Release();
            }
        }
    }

    private async Task RunReceiveLoopAsync(
        UdpClient listener,
        OSCQueryService service,
        CancellationTokenSource sessionCancellation,
        CancellationToken sessionToken)
    {
        while (!sessionToken.IsCancellationRequested)
        {
            try
            {
                var result = await listener.ReceiveAsync(sessionToken);
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
            catch (OperationCanceledException) when (sessionToken.IsCancellationRequested)
            {
                return;
            }
            catch (ObjectDisposedException) when (sessionToken.IsCancellationRequested)
            {
                return;
            }
            catch (SocketException ex)
            {
                if (sessionToken.IsCancellationRequested)
                {
                    return;
                }

                PublishSessionLog(service, sessionCancellation, sessionToken, $"OSC receive error: {ex.Message}");
                await Task.Delay(500, sessionToken);
            }
        }
    }

    private async Task RunDiscoveryLoopAsync(
        OSCQueryService service,
        CancellationTokenSource sessionCancellation,
        CancellationToken sessionToken)
    {
        await RefreshDiscoveredServicesAsync(
            service,
            sessionCancellation,
            sessionToken,
            logWhenWaiting: true,
            forceDiscoveryLog: true);
        if (!IsCurrentRuntimeSession(service, sessionCancellation, sessionToken))
        {
            return;
        }

        using var timer = new PeriodicTimer(ServiceRefreshInterval);

        while (await timer.WaitForNextTickAsync(sessionToken))
        {
            if (HasDiscoveredVrChat)
            {
                continue;
            }

            await RefreshDiscoveredServicesAsync(
                service,
                sessionCancellation,
                sessionToken,
                logWhenWaiting: false,
                forceDiscoveryLog: false);
            if (!IsCurrentRuntimeSession(service, sessionCancellation, sessionToken))
            {
                return;
            }
        }
    }

    private async Task RefreshDiscoveredServicesAsync(
        OSCQueryService service,
        CancellationTokenSource sessionCancellation,
        CancellationToken operationToken,
        bool logWhenWaiting,
        bool forceDiscoveryLog)
    {
        if (!IsCurrentRuntimeSession(service, sessionCancellation, operationToken))
        {
            return;
        }

        await discoveryRefreshGate.WaitAsync(operationToken);

        try
        {
            if (!IsCurrentRuntimeSession(service, sessionCancellation, operationToken))
            {
                return;
            }

            try
            {
                service.RefreshServices();
            }
            catch (Exception ex) when (!operationToken.IsCancellationRequested
                                       && IsCurrentRuntimeSession(service, sessionCancellation, operationToken))
            {
                PublishSessionLog(
                    service,
                    sessionCancellation,
                    operationToken,
                    $"OSCQuery discovery refresh failed: {ex.Message}");
                return;
            }

            foreach (var profile in service.GetOSCQueryServices())
            {
                await TryRegisterVrChatTargetAsync(
                    service,
                    profile,
                    sessionCancellation,
                    operationToken);
                if (!IsCurrentRuntimeSession(service, sessionCancellation, operationToken))
                {
                    return;
                }
            }
        }
        finally
        {
            discoveryRefreshGate.Release();
        }

        if (logWhenWaiting)
        {
            LogDiscoveryWaiting(
                service,
                sessionCancellation,
                operationToken,
                force: forceDiscoveryLog);
        }
    }

    private void HandleOscQueryServiceAdded(
        OSCQueryService service,
        OSCQueryServiceProfile profile,
        CancellationTokenSource sessionCancellation,
        CancellationToken sessionToken)
    {
        Task callbackTask;
        lock (stateGate)
        {
            if (!IsCurrentRuntimeSessionLocked(service, sessionCancellation, sessionToken))
            {
                return;
            }

            callbackTask = CreateTrackedRuntimeTask(
                () => TryRegisterVrChatTargetAsync(
                    service,
                    profile,
                    sessionCancellation,
                    sessionToken),
                service,
                sessionCancellation,
                sessionToken);
            runtimeTasks = [.. runtimeTasks, callbackTask];
        }
    }

    private Task CreateTrackedRuntimeTask(
        Func<Task> operation,
        OSCQueryService service,
        CancellationTokenSource sessionCancellation,
        CancellationToken cancellationToken)
    {
        var taskReady = new TaskCompletionSource<Task>(TaskCreationOptions.RunContinuationsAsynchronously);
        var task = Task.Run(
            async () =>
            {
                SessionLeaseScope? runtimeLease = null;
                var inheritedSessionLease = currentSessionLease.Value;
                try
                {
                    var trackedTask = await taskReady.Task.ConfigureAwait(false);
                    currentRuntimeTask.Value = trackedTask;
                    currentSessionLease.Value = null;
                    runtimeLease = TryEnterRuntimeTaskLease(service, sessionCancellation, cancellationToken);
                    if (runtimeLease is not null)
                    {
                        await operation().ConfigureAwait(false);
                    }
                }
                finally
                {
                    runtimeLease?.Dispose();
                    currentSessionLease.Value = inheritedSessionLease;
                    currentRuntimeTask.Value = null;
                }
            },
            cancellationToken);
        taskReady.SetResult(task);
        return task;
    }

    private async Task TryRegisterVrChatTargetAsync(
        OSCQueryService service,
        OSCQueryServiceProfile profile,
        CancellationTokenSource sessionCancellation,
        CancellationToken operationToken)
    {
        if (!IsCurrentRuntimeSession(service, sessionCancellation, operationToken))
        {
            return;
        }

        lock (stateGate)
        {
            if (!IsCurrentRuntimeSessionLocked(service, sessionCancellation, operationToken)
                || string.Equals(profile.name, localServiceName, StringComparison.Ordinal))
            {
                return;
            }
        }

        DiscoveredOscTarget? match;
        if (targetFactoryOverride is not null)
        {
            match = await targetFactoryOverride(profile, operationToken);
        }
        else
        {
            match = await CreateVrChatTargetAsync(
                profile,
                operationToken,
                service,
                sessionCancellation);
        }

        if (!IsCurrentRuntimeSession(service, sessionCancellation, operationToken))
        {
            return;
        }

        if (match is null)
        {
            return;
        }

        var receivePort = 0;
        lock (stateGate)
        {
            if (!IsCurrentRuntimeSessionLocked(service, sessionCancellation, operationToken))
            {
                return;
            }

            if (activeVrChatTarget is not null && !ShouldReplaceTarget(activeVrChatTarget, match))
            {
                return;
            }

            activeVrChatTarget = match;
            discoveryState = OscDiscoveryState.Discovered;
            receivePort = localUdpPort;
        }

        PublishSessionStateChanged(
            service,
            sessionCancellation,
            operationToken,
            OscDiscoveryState.Discovered);
        PublishSessionLog(
            service,
            sessionCancellation,
            operationToken,
            $"Discovered VRChat through OSCQuery: {match.Name}. Crystal Relay will send actions to {match.Address}:{match.OscPort} and receive values on 127.0.0.1:{receivePort}.");
    }

    private bool IsCurrentRuntimeSession(
        OSCQueryService service,
        CancellationTokenSource? sessionCancellation,
        CancellationToken cancellationToken)
    {
        if (cancellationToken.IsCancellationRequested)
        {
            return false;
        }

        lock (stateGate)
        {
            return IsCurrentRuntimeSessionLocked(service, sessionCancellation, cancellationToken);
        }
    }

    private bool IsCurrentRuntimeSessionLocked(
        OSCQueryService service,
        CancellationTokenSource? sessionCancellation,
        CancellationToken cancellationToken)
    {
        return !cancellationToken.IsCancellationRequested
            && !stopRequested
            && !isDisposing
            && !isDisposed
            && sessionCancellation is not null
            && ReferenceEquals(runtimeCancellation, sessionCancellation)
            && ReferenceEquals(oscQueryService, service);
    }

    private async Task<DiscoveredOscTarget?> CreateVrChatTargetAsync(
        OSCQueryServiceProfile profile,
        CancellationToken cancellationToken,
        OSCQueryService service,
        CancellationTokenSource sessionCancellation)
    {
        try
        {
            if (!IsLoopbackAddress(profile.address) || !IsValidPort(profile.port))
            {
                return null;
            }

            if (!await LooksLikeVrChatAsync(
                       profile,
                       cancellationToken,
                       service,
                       sessionCancellation))
            {
                return null;
            }

            var hostInfo = await VRC.OSCQuery.Extensions.GetHostInfo(profile.address, profile.port);
            ThrowIfSessionIsNotCurrent(service, sessionCancellation, cancellationToken);
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
                PublishSessionLog(
                    service,
                    sessionCancellation,
                    cancellationToken,
                    $"Crystal Relay found VRChat's OSCQuery service '{profile.name}', but it is not ready to use yet: {ex.Message}");
            }

            return null;
        }
    }

    private async Task<bool> LooksLikeVrChatAsync(
        OSCQueryServiceProfile profile,
        CancellationToken cancellationToken,
        OSCQueryService service,
        CancellationTokenSource sessionCancellation)
    {
        cancellationToken.ThrowIfCancellationRequested();

        if (profile.name.Contains("vrchat", StringComparison.OrdinalIgnoreCase))
        {
            return true;
        }

        var tree = await VRC.OSCQuery.Extensions.GetOSCTree(profile.address, profile.port);
        ThrowIfSessionIsNotCurrent(service, sessionCancellation, cancellationToken);
        if (tree is null)
        {
            return false;
        }

        return tree.GetNodeWithPath("/avatar/change") is not null
            || tree.GetNodeWithPath("/avatar/eyeheight") is not null
            || tree.GetNodeWithPath("/avatar/parameters") is not null
            || tree.GetNodeWithPath("/input") is not null;
    }

    private void SyncAdvertisedEndpoints(
        OSCQueryService service,
        CancellationTokenSource sessionCancellation,
        CancellationToken sessionToken,
        IReadOnlyList<TriggerRuleSnapshot> rules)
    {
        var desiredEndpoints = BuildDesiredEndpoints(
            rules,
            message => PublishSessionLog(service, sessionCancellation, sessionToken, message));
        lock (stateGate)
        {
            if (!ReferenceEquals(runtimeCancellation, sessionCancellation)
                || !ReferenceEquals(oscQueryService, service))
            {
                return;
            }

            SyncAdvertisedEndpointsForState(service, desiredEndpoints, advertisedEndpoints);
        }
    }

    private void SyncAdvertisedEndpointsForState(
        OSCQueryService service,
        Dictionary<string, OscParameterType> desiredEndpoints,
        Dictionary<string, OscParameterType> endpointState)
    {
        foreach (var obsoletePath in endpointState.Keys.Except(desiredEndpoints.Keys, StringComparer.Ordinal).ToArray())
        {
            service.RemoveEndpoint(obsoletePath);
            endpointState.Remove(obsoletePath);
        }

        foreach (var endpoint in desiredEndpoints)
        {
            if (endpointState.TryGetValue(endpoint.Key, out var existingType))
            {
                if (existingType == endpoint.Value)
                {
                    continue;
                }

                service.RemoveEndpoint(endpoint.Key);
                endpointState.Remove(endpoint.Key);
            }

            AddAdvertisedEndpoint(service, endpoint.Key, endpoint.Value);
            endpointState[endpoint.Key] = endpoint.Value;
        }
    }

    private Dictionary<string, OscParameterType> BuildDesiredEndpoints(
        IReadOnlyList<TriggerRuleSnapshot> rules,
        Action<string>? log = null)
    {
        log ??= message => LogWritten?.Invoke(message);
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
                log($"Skipped OSCQuery endpoint for '{rule.Name}' because the avatar parameter path is incomplete: {ex.Message}");
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
                        log($"Skipped Set Trigger OSCQuery endpoint for '{rule.Name}' because {address} is height or avatar scale related.");
                        continue;
                    }

                    if (endpoints.TryGetValue(address, out var existingType))
                    {
                        if (existingType != action.ParameterType)
                        {
                            log($"Skipped Set Trigger OSCQuery endpoint for '{rule.Name}' because {address} is already tracked as {existingType}, not {action.ParameterType}.");
                        }

                        continue;
                    }

                    endpoints[address] = action.ParameterType;
                }
                catch (InvalidOperationException ex)
                {
                    log($"Skipped Set Trigger OSCQuery endpoint for '{rule.Name}' because the avatar parameter path is incomplete: {ex.Message}");
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

    private void LogDiscoveryWaiting(
        OSCQueryService service,
        CancellationTokenSource sessionCancellation,
        CancellationToken operationToken,
        bool force)
    {
        string? message = null;

        lock (stateGate)
        {
            if (!IsCurrentRuntimeSessionLocked(service, sessionCancellation, operationToken)
                || activeVrChatTarget is not null)
            {
                return;
            }

            var now = DateTimeOffset.UtcNow;
            if (!force && now < nextDiscoveryLogAt)
            {
                return;
            }

            nextDiscoveryLogAt = now.Add(DiscoveryLogThrottle);
            message = "Searching for VRChat through OSCQuery. Leave VRChat open with OSC enabled so Crystal Relay can discover it automatically.";
        }

        if (message is not null)
        {
            PublishSessionLog(service, sessionCancellation, operationToken, message);
        }
    }

    private InvalidOperationException CreateTargetLostException(
        string operation,
        OSCQueryService service,
        CancellationTokenSource sessionCancellation,
        CancellationToken operationToken,
        Exception ex)
    {
        MarkTargetLost(
            service,
            sessionCancellation,
            operationToken,
            $"Crystal Relay lost the OSCQuery connection to VRChat while {operation}. It will wait for VRChat to come back automatically.");
        return new InvalidOperationException(
            $"Crystal Relay lost the OSCQuery connection to VRChat while {operation}. Wait a moment for VRChat to finish loading.",
            ex);
    }

    private void MarkTargetLost(
        OSCQueryService service,
        CancellationTokenSource sessionCancellation,
        CancellationToken operationToken,
        string reason)
    {
        var shouldPublish = false;

        lock (stateGate)
        {
            if (!IsCurrentRuntimeSessionLocked(service, sessionCancellation, operationToken)
                || (activeVrChatTarget is null && discoveryState == OscDiscoveryState.Lost))
            {
                return;
            }

            activeVrChatTarget = null;
            cachedVrChatEndPoint = null;
            discoveryState = OscDiscoveryState.Lost;
            nextDiscoveryLogAt = DateTimeOffset.MinValue;
            shouldPublish = true;
        }

        if (shouldPublish)
        {
            PublishSessionStateChanged(
                service,
                sessionCancellation,
                operationToken,
                OscDiscoveryState.Lost);
            PublishSessionLog(service, sessionCancellation, operationToken, reason);
        }
    }

    private void PublishSessionStateChanged(
        OSCQueryService service,
        CancellationTokenSource sessionCancellation,
        CancellationToken operationToken,
        OscDiscoveryState state)
    {
        var notificationLease = TryEnterNotificationLease(service, sessionCancellation, operationToken);
        if (notificationLease is null)
        {
            return;
        }

        using (notificationLease)
        {
            DiscoveryStateChanged?.Invoke(state);
        }
    }

    private void PublishSessionLog(
        OSCQueryService service,
        CancellationTokenSource sessionCancellation,
        CancellationToken operationToken,
        string message)
    {
        var notificationLease = TryEnterNotificationLease(service, sessionCancellation, operationToken);
        if (notificationLease is null)
        {
            return;
        }

        using (notificationLease)
        {
            LogWritten?.Invoke(message);
        }
    }

    private DiscoveredOscTarget GetCurrentTarget(
        OSCQueryService service,
        CancellationTokenSource sessionCancellation,
        CancellationToken operationToken,
        string missingTargetMessage)
    {
        lock (stateGate)
        {
            if (!IsCurrentRuntimeSessionLocked(service, sessionCancellation, operationToken))
            {
                ThrowIfSessionIsNotCurrent(service, sessionCancellation, operationToken);
            }

            return activeVrChatTarget ?? throw new InvalidOperationException(missingTargetMessage);
        }
    }

    private void ThrowIfSessionIsNotCurrent(
        OSCQueryService service,
        CancellationTokenSource sessionCancellation,
        CancellationToken operationToken)
    {
        if (operationToken.IsCancellationRequested)
        {
            throw new OperationCanceledException(operationToken);
        }

        lock (stateGate)
        {
            if (!IsCurrentRuntimeSessionLocked(service, sessionCancellation, operationToken))
            {
                throw new InvalidOperationException("The OSCQuery session was stopped or replaced while Crystal Relay was reading VRChat data.");
            }
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

    private enum SessionLeaseKind
    {
        Operation,
        Notification,
        RuntimeTask
    }

    private sealed class SessionLease(
        OSCQueryService service,
        CancellationTokenSource cancellation,
        SessionLeaseKind kind,
        SessionLease? previous)
    {
        public OSCQueryService Service { get; } = service;

        public CancellationTokenSource Cancellation { get; } = cancellation;

        public SessionLeaseKind Kind { get; } = kind;

        public SessionLease? Previous { get; } = previous;
    }

    private sealed class SessionLeaseScope(
        OscRouterService owner,
        SessionLease lease,
        SessionLease? previous) : IDisposable
    {
        private int disposed;

        public SessionLease Lease => lease;

        public void Dispose()
        {
            if (Interlocked.Exchange(ref disposed, 1) != 0)
            {
                return;
            }

            owner.currentSessionLease.Value = previous;
            owner.ReleaseSessionLease(lease);
        }
    }

    internal sealed record DiscoveredOscTarget(string Name, IPAddress Address, int OscPort, int QueryPort);
}
