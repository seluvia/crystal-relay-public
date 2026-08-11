using System.Net;
using System.Net.Sockets;
using System.IO;
using System.Reflection;
using System.Text.RegularExpressions;
using VRC.OSCQuery;
using VrcTwitchOscBridge.Models;
using VrcTwitchOscBridge.Services;
using Xunit;

namespace VrcTwitchOscBridge.Tests;

public sealed class OscRouterRuntimeLifetimeTests
{
    [Fact]
    public void StartAsync_UsesCallerTokenForStartupChecksAndOwnsRuntimeCancellation()
    {
        var source = File.ReadAllText(FindSourceFile("VrcTwitchOscBridge", "Services", "OscRouterService.cs"));
        var body = NormalizeWhitespace(GetMethodBody(source, "StartAsync("));

        Assert.Contains("cancellationToken.ThrowIfCancellationRequested()", body, StringComparison.Ordinal);
        Assert.Contains("var cancellation = new CancellationTokenSource()", body, StringComparison.Ordinal);
        Assert.Contains("cancellation.Token", body, StringComparison.Ordinal);
        Assert.DoesNotContain("runtimeCancellation.Token", body, StringComparison.Ordinal);

        var stopBody = NormalizeWhitespace(GetMethodBody(source, "private async Task StopCoreAsync("));
        Assert.Contains("var tasks = runtimeTasks", stopBody, StringComparison.Ordinal);
        Assert.Contains("var listener = receiveListener", stopBody, StringComparison.Ordinal);
        Assert.Contains("var sender = sendClient", stopBody, StringComparison.Ordinal);
        Assert.Contains("var service = oscQueryService", stopBody, StringComparison.Ordinal);
    }

    [Fact]
    public void LifecycleMethods_SerializeThroughTheSameGate()
    {
        var source = File.ReadAllText(FindSourceFile("VrcTwitchOscBridge", "Services", "OscRouterService.cs"));
        var startBody = NormalizeWhitespace(GetMethodBody(source, "StartAsync("));
        var stopBody = NormalizeWhitespace(GetMethodBody(source, "public async Task StopAsync()"));
        var gateEntryBody = NormalizeWhitespace(GetMethodBody(source, "private async Task EnterLifecycleGateAsync"));
        var disposeBody = NormalizeWhitespace(GetMethodBody(source, "private async Task RunDisposeAsync"));

        Assert.Contains("private readonly SemaphoreSlim lifecycleGate = new(1, 1);", source, StringComparison.Ordinal);
        Assert.Contains("EnterLifecycleGateAsync(cancellationToken, allowDisposing: false)", startBody, StringComparison.Ordinal);
        Assert.Contains("lifecycleGateUsers++", stopBody, StringComparison.Ordinal);
        Assert.Contains("await lifecycleGate.WaitAsync().ConfigureAwait(false)", stopBody, StringComparison.Ordinal);
        Assert.Contains("await lifecycleGate.WaitAsync(cancellationToken).ConfigureAwait(false)", gateEntryBody, StringComparison.Ordinal);
        Assert.Contains("ExitLifecycleGate()", startBody, StringComparison.Ordinal);
        Assert.Contains("ExitLifecycleGate()", stopBody, StringComparison.Ordinal);
        Assert.Contains("await lifecycleGate.WaitAsync().ConfigureAwait(false)", disposeBody, StringComparison.Ordinal);
        Assert.Contains("lifecycleGate.Dispose()", disposeBody, StringComparison.Ordinal);
    }

    [Fact]
    public void PublicDiscoveryOperations_LeaseSessionAndAvatarValueUsesPrivateCore()
    {
        var source = File.ReadAllText(FindSourceFile("VrcTwitchOscBridge", "Services", "OscRouterService.cs"));

        foreach (var method in new[]
        {
            "public async Task ForceRefreshAsync",
            "public async Task<IReadOnlyList<VrChatOscParameterSummary>> GetCurrentAvatarParametersAsync",
            "public async Task<OscObservedValue?> GetCurrentOscValueAsync"
        })
        {
            var body = NormalizeWhitespace(GetMethodBody(source, method));
            Assert.Contains("EnterSessionOperation(cancellationToken)", body, StringComparison.Ordinal);
            Assert.Contains("operationLease.Dispose()", body, StringComparison.Ordinal);
            Assert.DoesNotContain("lifecycleGate", body, StringComparison.Ordinal);
        }

        var avatarValueBody = NormalizeWhitespace(GetMethodBody(source, "public async Task<OscObservedValue?> GetCurrentAvatarParameterValueAsync"));
        Assert.Contains("GetCurrentOscValueCoreAsync", avatarValueBody, StringComparison.Ordinal);
        Assert.DoesNotContain("GetCurrentOscValueAsync(normalizedAddress", avatarValueBody, StringComparison.Ordinal);
    }

    [Fact]
    public void PublicDiscoveryOperations_RevalidateAfterEveryRefreshBeforeUsingSessionState()
    {
        var source = File.ReadAllText(FindSourceFile("VrcTwitchOscBridge", "Services", "OscRouterService.cs"));

        foreach (var method in new[]
        {
            "private async Task ForceRefreshCoreAsync",
            "private async Task<IReadOnlyList<VrChatOscParameterSummary>> GetCurrentAvatarParametersCoreAsync",
            "private async Task<OscObservedValue?> GetCurrentOscValueCoreAsync"
        })
        {
            var body = NormalizeWhitespace(GetMethodBody(source, method));
            var refreshIndex = body.IndexOf("await RefreshDiscoveredServicesAsync", StringComparison.Ordinal);
            var targetIndex = body.IndexOf("var target = GetCurrentTarget", refreshIndex, StringComparison.Ordinal);
            var validationIndex = body.IndexOf("ThrowIfSessionIsNotCurrent", refreshIndex, StringComparison.Ordinal);

            Assert.True(refreshIndex >= 0, $"{method} must await discovery refresh.");
            Assert.True(validationIndex > refreshIndex, $"{method} must validate the session after discovery refresh.");
            if (targetIndex >= 0)
            {
                Assert.True(validationIndex < targetIndex, $"{method} must validate the session before using the current target.");
            }
        }
    }

    [Fact]
    public void DiscoveryReads_PropagateCurrentServiceAndCancellationIdentity()
    {
        var source = File.ReadAllText(FindSourceFile("VrcTwitchOscBridge", "Services", "OscRouterService.cs"));

        foreach (var method in new[]
        {
            "private async Task ForceRefreshCoreAsync",
            "private async Task<IReadOnlyList<VrChatOscParameterSummary>> GetCurrentAvatarParametersCoreAsync",
            "private async Task<OscObservedValue?> GetCurrentOscValueCoreAsync"
        })
        {
            var body = NormalizeWhitespace(GetMethodBody(source, method));
            Assert.Contains("sessionCancellation", body, StringComparison.Ordinal);
            Assert.Contains("sessionToken", body, StringComparison.Ordinal);
            Assert.Contains("IsCurrentRuntimeSessionLocked(service, sessionCancellation", body, StringComparison.Ordinal);
            var refreshIndex = body.IndexOf("RefreshDiscoveredServicesAsync", StringComparison.Ordinal);
            var sessionIndex = body.IndexOf("sessionCancellation", refreshIndex, StringComparison.Ordinal);
            Assert.True(refreshIndex >= 0 && sessionIndex > refreshIndex, $"{method} must pass session identity to discovery refresh.");
        }

        var refreshBody = NormalizeWhitespace(GetMethodBody(source, "private async Task RefreshDiscoveredServicesAsync"));
        Assert.DoesNotContain("CancellationTokenSource? sessionCancellation = null", refreshBody, StringComparison.Ordinal);
        Assert.Contains("IsCurrentRuntimeSession(service, sessionCancellation", refreshBody, StringComparison.Ordinal);

        var discoveryLoopBody = NormalizeWhitespace(GetMethodBody(source, "private async Task RunDiscoveryLoopAsync"));
        var firstLoopRefresh = discoveryLoopBody.IndexOf("await RefreshDiscoveredServicesAsync", StringComparison.Ordinal);
        var firstLoopValidation = discoveryLoopBody.IndexOf("IsCurrentRuntimeSession(service, sessionCancellation", firstLoopRefresh, StringComparison.Ordinal);
        Assert.True(firstLoopRefresh >= 0 && firstLoopValidation > firstLoopRefresh, "The discovery loop must revalidate after its awaited refresh.");

        var targetBody = NormalizeWhitespace(GetMethodBody(source, "private async Task TryRegisterVrChatTargetAsync"));
        Assert.Contains("IsCurrentRuntimeSession(service, sessionCancellation", targetBody, StringComparison.Ordinal);
        var targetPublicationIndex = targetBody.IndexOf("activeVrChatTarget = match", StringComparison.Ordinal);
        var targetValidationIndex = targetBody.LastIndexOf("IsCurrentRuntimeSessionLocked(service, sessionCancellation", StringComparison.Ordinal);
        Assert.True(targetValidationIndex >= 0 && targetValidationIndex < targetPublicationIndex, "Target publication must follow post-await session validation.");

        foreach (var method in new[]
        {
            "private async Task<IReadOnlyList<VrChatOscParameterSummary>> GetCurrentAvatarParametersCoreAsync",
            "private async Task<OscObservedValue?> GetCurrentOscValueCoreAsync"
        })
        {
            var body = NormalizeWhitespace(GetMethodBody(source, method));
            var treeIndex = body.IndexOf("GetOSCTree", StringComparison.Ordinal);
            var treeValidationIndex = body.IndexOf("ThrowIfSessionIsNotCurrent", treeIndex, StringComparison.Ordinal);
            Assert.True(treeIndex >= 0 && treeValidationIndex > treeIndex, $"{method} must validate the session after its tree await.");
        }
    }

    [Fact]
    public void RuleSubscriptionSync_RevalidatesTheServiceWhileHoldingStateGate()
    {
        var source = File.ReadAllText(FindSourceFile("VrcTwitchOscBridge", "Services", "OscRouterService.cs"));
        var body = NormalizeWhitespace(GetMethodBody(source, "private void SyncAdvertisedEndpoints("));

        Assert.Contains("lock (stateGate)", body, StringComparison.Ordinal);
        Assert.Contains("ReferenceEquals(runtimeCancellation, sessionCancellation)", body, StringComparison.Ordinal);
        Assert.Contains("ReferenceEquals(oscQueryService, service)", body, StringComparison.Ordinal);
        Assert.Contains("PublishSessionLog(service, sessionCancellation, sessionToken", body, StringComparison.Ordinal);
        Assert.Contains("SyncAdvertisedEndpointsForState(service, desiredEndpoints, advertisedEndpoints)", body, StringComparison.Ordinal);
    }

    [Fact]
    public void DiscoveryNotifications_RevalidateSessionBeforeInvokingCallbacks()
    {
        var source = File.ReadAllText(FindSourceFile("VrcTwitchOscBridge", "Services", "OscRouterService.cs"));
        var targetBody = NormalizeWhitespace(GetMethodBody(source, "private async Task TryRegisterVrChatTargetAsync"));
        var waitingBody = NormalizeWhitespace(GetMethodBody(source, "private void LogDiscoveryWaiting"));
        var lostBody = NormalizeWhitespace(GetMethodBody(source, "private void MarkTargetLost"));

        Assert.Contains("PublishSessionStateChanged", targetBody, StringComparison.Ordinal);
        Assert.Contains("PublishSessionLog", targetBody, StringComparison.Ordinal);
        Assert.Contains("PublishSessionLog", waitingBody, StringComparison.Ordinal);
        Assert.Contains("PublishSessionStateChanged", lostBody, StringComparison.Ordinal);
    }

    [Fact]
    public void SessionNotifications_LeaseTheSessionAcrossDelegateInvocation()
    {
        var source = File.ReadAllText(FindSourceFile("VrcTwitchOscBridge", "Services", "OscRouterService.cs"));

        var stateBody = GetMethodBody(source, "private void PublishSessionStateChanged");
        Assert.Contains("TryEnterNotificationLease(service, sessionCancellation, operationToken)", NormalizeWhitespace(stateBody), StringComparison.Ordinal);
        Assert.Contains("using (notificationLease)", NormalizeWhitespace(stateBody), StringComparison.Ordinal);
        Assert.Contains("DiscoveryStateChanged?.Invoke(state)", NormalizeWhitespace(stateBody), StringComparison.Ordinal);

        var logBody = GetMethodBody(source, "private void PublishSessionLog");
        Assert.Contains("TryEnterNotificationLease(service, sessionCancellation, operationToken)", NormalizeWhitespace(logBody), StringComparison.Ordinal);
        Assert.Contains("using (notificationLease)", NormalizeWhitespace(logBody), StringComparison.Ordinal);
        Assert.Contains("LogWritten?.Invoke(message)", NormalizeWhitespace(logBody), StringComparison.Ordinal);
        Assert.DoesNotContain("activeSessionNotifications", source, StringComparison.Ordinal);
    }

    [Fact]
    public void DisposeAsync_WaitsForStopBeforeDisposingLifecycleGates()
    {
        var source = File.ReadAllText(FindSourceFile("VrcTwitchOscBridge", "Services", "OscRouterService.cs"));
        var disposeBody = NormalizeWhitespace(GetMethodBody(source, "public ValueTask DisposeAsync"));
        var runnerBody = NormalizeWhitespace(GetMethodBody(source, "private async Task RunDisposeAsync"));
        var stopIndex = runnerBody.IndexOf("await StopCoreAsync", StringComparison.Ordinal);
        var discoveryGateDisposeIndex = runnerBody.IndexOf("discoveryRefreshGate.Dispose()", StringComparison.Ordinal);
        var lifecycleGateDisposeIndex = runnerBody.IndexOf("lifecycleGate.Dispose()", StringComparison.Ordinal);

        Assert.Contains("isDisposing = true", disposeBody, StringComparison.Ordinal);
        Assert.True(stopIndex >= 0, "DisposeAsync must await StopCoreAsync before disposing lifecycle gates.");
        Assert.True(discoveryGateDisposeIndex > stopIndex);
        Assert.True(lifecycleGateDisposeIndex > stopIndex);
    }

    [Fact]
    public void StopCoreAsync_SnapshotsAndClearsEveryRuntimeFieldBeforeAwaitingTasks()
    {
        var source = File.ReadAllText(FindSourceFile("VrcTwitchOscBridge", "Services", "OscRouterService.cs"));
        var body = NormalizeWhitespace(GetMethodBody(source, "private async Task StopCoreAsync("));

        foreach (var capture in new[]
        {
            "var cancellation = runtimeCancellation",
            "var tasks = runtimeTasks",
            "var listener = receiveListener",
            "var sender = sendClient",
            "var service = oscQueryService"
        })
        {
            Assert.Contains(capture, body, StringComparison.Ordinal);
        }

        foreach (var clear in new[]
        {
            "runtimeCancellation = null",
            "runtimeTasks = []",
            "receiveListener = null",
            "sendClient = null",
            "oscQueryService = null",
            "oscQueryServiceAddedHandler = null",
            "advertisedEndpoints = new Dictionary<string, OscParameterType>(StringComparer.Ordinal)",
            "activeVrChatTarget = null",
            "cachedVrChatEndPoint = null",
            "localUdpPort = 0",
            "localTcpPort = 0",
            "localServiceName = string.Empty",
            "discoveryState = OscDiscoveryState.Idle",
            "nextDiscoveryLogAt = DateTimeOffset.MinValue"
        })
        {
            Assert.Contains(clear, body, StringComparison.Ordinal);
        }

        var awaitTasksIndex = body.IndexOf("await Task.WhenAll(tasks)", StringComparison.Ordinal);
        Assert.True(awaitTasksIndex >= 0, "StopCoreAsync must await every captured runtime task.");
        Assert.DoesNotContain("Task.CurrentId", body, StringComparison.Ordinal);
        foreach (var clear in new[]
        {
            "runtimeCancellation = null",
            "runtimeTasks = []",
            "receiveListener = null",
            "sendClient = null",
            "oscQueryService = null",
            "oscQueryServiceAddedHandler = null",
            "advertisedEndpoints = new Dictionary<string, OscParameterType>(StringComparer.Ordinal)",
            "activeVrChatTarget = null",
            "cachedVrChatEndPoint = null",
            "localUdpPort = 0",
            "localTcpPort = 0",
            "localServiceName = string.Empty",
            "discoveryState = OscDiscoveryState.Idle",
            "nextDiscoveryLogAt = DateTimeOffset.MinValue"
        })
        {
            var clearIndex = body.IndexOf(clear, StringComparison.Ordinal);
            Assert.True(clearIndex >= 0 && clearIndex < awaitTasksIndex, $"{clear} must precede Task.WhenAll.");
        }

        Assert.Contains("cancellation?.Cancel()", body, StringComparison.Ordinal);
        Assert.Contains("listener?.Dispose()", body, StringComparison.Ordinal);
        Assert.Contains("sender?.Dispose()", body, StringComparison.Ordinal);
        Assert.Contains("service?.Dispose()", body, StringComparison.Ordinal);
    }

    [Fact]
    public void StopCoreAsync_ProtectsCancellationAndClearsTrackedWorkerOwnership()
    {
        var source = File.ReadAllText(FindSourceFile("VrcTwitchOscBridge", "Services", "OscRouterService.cs"));
        var stopBody = NormalizeWhitespace(GetMethodBody(source, "private async Task StopCoreAsync("));
        var cancelIndex = stopBody.IndexOf("cancellation?.Cancel()", StringComparison.Ordinal);
        var cleanupTryIndex = stopBody.IndexOf("try", StringComparison.Ordinal);

        Assert.True(cancelIndex > cleanupTryIndex, "Cancellation must run inside the protected cleanup region.");
        Assert.Contains("SensitiveTextSanitizer.Sanitize", stopBody, StringComparison.Ordinal);

        var workerBody = NormalizeWhitespace(GetMethodBody(source, "private Task CreateTrackedRuntimeTask"));
        Assert.Contains("finally", workerBody, StringComparison.Ordinal);
        Assert.Contains("currentRuntimeTask.Value = null", workerBody, StringComparison.Ordinal);
        Assert.DoesNotContain("Task.CurrentId", stopBody, StringComparison.Ordinal);
    }

    [Fact]
    public async Task StopAsync_DrainsRuntimeStateWhenCancellationSourceIsMissing_AndSecondStopIsIdempotent()
    {
        await using var router = new OscRouterService();
        using var listener = new UdpClient(0);
        using var sender = new UdpClient(0);
        using var service = CreateOscQueryService();
        var taskRelease = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var runtimeTask = taskRelease.Task;

        SetField(router, "runtimeCancellation", null);
        SetField(router, "runtimeTasks", new[] { runtimeTask });
        SetField(router, "receiveListener", listener);
        SetField(router, "sendClient", sender);
        SetField(router, "oscQueryService", service);

        var stopTask = router.StopAsync();
        Assert.False(stopTask.IsCompleted);

        taskRelease.SetResult();
        await stopTask.WaitAsync(TimeSpan.FromSeconds(1));
        await router.StopAsync();

        Assert.Null(GetField(router, "runtimeCancellation"));
        Assert.Empty(Assert.IsType<Task[]>(GetField(router, "runtimeTasks")));
        Assert.Null(GetField(router, "receiveListener"));
        Assert.Null(GetField(router, "sendClient"));
        Assert.Null(GetField(router, "oscQueryService"));
        Assert.Throws<ObjectDisposedException>(() => listener.Send([1], 1, new IPEndPoint(IPAddress.Loopback, 9)));
        Assert.Throws<ObjectDisposedException>(() => sender.Send([1], 1, new IPEndPoint(IPAddress.Loopback, 9)));
    }

    [Fact]
    public async Task StopAsync_ContinuesCleanupWhenCancellationCallbackThrows()
    {
        await using var router = new OscRouterService();
        using var listener = new UdpClient(0);
        using var sender = new UdpClient(0);
        using var service = CreateOscQueryService();
        var cancellationEntered = NewSignal();
        var taskRelease = NewSignal();
        var runtimeTask = taskRelease.Task;
        var cancellation = new CancellationTokenSource();
        cancellation.Token.Register(() =>
        {
            cancellationEntered.SetResult();
            throw new InvalidOperationException("router cleanup callback failure");
        });

        SetField(router, "runtimeCancellation", cancellation);
        SetField(router, "runtimeTasks", new[] { runtimeTask });
        SetField(router, "receiveListener", listener);
        SetField(router, "sendClient", sender);
        SetField(router, "oscQueryService", service);

        var stopTask = router.StopAsync();
        await cancellationEntered.Task.WaitAsync(TimeSpan.FromSeconds(1));
        Assert.False(stopTask.IsCompleted);

        taskRelease.SetResult();
        try
        {
            await stopTask.WaitAsync(TimeSpan.FromSeconds(1));
        }
        catch (Exception)
        {
        }

        Assert.True(runtimeTask.IsCompleted);
        Assert.Null(GetField(router, "runtimeCancellation"));
        Assert.Empty(Assert.IsType<Task[]>(GetField(router, "runtimeTasks")));
        Assert.Null(GetField(router, "receiveListener"));
        Assert.Null(GetField(router, "sendClient"));
        Assert.Null(GetField(router, "oscQueryService"));
        Assert.Throws<ObjectDisposedException>(() => listener.Send([1], 1, new IPEndPoint(IPAddress.Loopback, 9)));
        Assert.Throws<ObjectDisposedException>(() => sender.Send([1], 1, new IPEndPoint(IPAddress.Loopback, 9)));
        Assert.False(GetHttpListener(service).IsListening);
    }

    [Fact]
    public async Task AbandonedStartup_DoesNotPoisonNextSessionEndpointSynchronization()
    {
        OSCQueryService? abandonedService = null;
        var failFirstStartup = 1;
        var expectedFailure = new InvalidOperationException("abandoned startup barrier");
        var router = new OscRouterService(
            null,
            service =>
            {
                if (Interlocked.Exchange(ref failFirstStartup, 0) == 1)
                {
                    abandonedService = service;
                    return Task.FromException(expectedFailure);
                }

                return Task.CompletedTask;
            });
        await using (router)
        {
            var rules = CreateEndpointRules();
            const string endpoint = "/avatar/parameters/PoisonedEndpoint";

            await Assert.ThrowsAsync<InvalidOperationException>(() => router.StartAsync(rules));

            Assert.NotNull(abandonedService);
            Assert.NotNull(abandonedService.RootNode.GetNodeWithPath(endpoint));
            Assert.Empty(Assert.IsType<Dictionary<string, OscParameterType>>(GetField(router, "advertisedEndpoints")));
            Assert.False(GetHttpListener(abandonedService).IsListening);

            await router.StartAsync(rules);

            var currentService = Assert.IsType<OSCQueryService>(GetField(router, "oscQueryService"));
            Assert.NotNull(currentService.RootNode.GetNodeWithPath(endpoint));
        }
    }

    [Fact]
    public async Task ConcurrentStartAndStop_WaitsForStartupBeforePublicationHook()
    {
        var publicationReached = NewSignal();
        var releasePublication = NewSignal();
        await using var router = new OscRouterService(
            null,
            async _ =>
            {
                publicationReached.SetResult();
                await releasePublication.Task.ConfigureAwait(false);
            });

        try
        {
            var startTask = Task.Run(() => router.StartAsync([]));
            await publicationReached.Task.WaitAsync(TimeSpan.FromSeconds(5));
            Assert.False(router.IsRunning);

            var stopTask = router.StopAsync();
            Assert.False(stopTask.IsCompleted);
            Assert.False(startTask.IsCompleted);

            releasePublication.SetResult();
            await startTask.WaitAsync(TimeSpan.FromSeconds(5));
            await stopTask.WaitAsync(TimeSpan.FromSeconds(5));

            Assert.False(router.IsRunning);
            Assert.Equal(OscDiscoveryState.Idle, router.DiscoveryState);
        }
        finally
        {
            releasePublication.TrySetResult();
        }
    }

    [Fact]
    public async Task DisposeAsync_DrainsPreExistingLifecycleWaiterBeforeDisposingTheGate()
    {
        var publicationReached = NewSignal();
        var releasePublication = NewSignal();
        var router = new OscRouterService(
            null,
            async _ =>
            {
                publicationReached.SetResult();
                await releasePublication.Task.ConfigureAwait(false);
            });

        try
        {
            var firstStart = Task.Run(() => router.StartAsync([]));
            await publicationReached.Task.WaitAsync(TimeSpan.FromSeconds(5));

            var secondStart = Task.Run(() => router.StartAsync([]));
            Assert.True(SpinWait.SpinUntil(
                () => Convert.ToInt32(GetField(router, "lifecycleGateUsers")) >= 2,
                TimeSpan.FromSeconds(5)));

            var disposeTask = router.DisposeAsync().AsTask();
            Assert.False(disposeTask.IsCompleted);
            releasePublication.SetResult();

            await firstStart.WaitAsync(TimeSpan.FromSeconds(5));
            var secondStartException = await Record.ExceptionAsync(
                () => secondStart.WaitAsync(TimeSpan.FromSeconds(5)));
            Assert.IsType<ObjectDisposedException>(secondStartException);
            await disposeTask.WaitAsync(TimeSpan.FromSeconds(5));
        }
        finally
        {
            releasePublication.TrySetResult();
            try
            {
                await router.DisposeAsync();
            }
            catch (Exception)
            {
            }
        }
    }

    [Fact]
    public async Task StopReservation_PreventsDisposeFromDisposingTheGateBeforeStopAdmission()
    {
        var startupReached = NewSignal();
        var releaseStartup = NewSignal();
        var stopAdmissionReached = NewSignal();
        var releaseStopAdmission = NewSignal();
        await using var router = CreateRouterWithStopAdmissionHook(
            async _ =>
            {
                startupReached.SetResult();
                await releaseStartup.Task.ConfigureAwait(false);
            },
            async () =>
            {
                stopAdmissionReached.SetResult();
                await releaseStopAdmission.Task.ConfigureAwait(false);
            });

        try
        {
            var firstStart = Task.Run(() => router.StartAsync([]));
            await startupReached.Task.WaitAsync(TimeSpan.FromSeconds(5));

            var stopTask = router.StopAsync();
            await stopAdmissionReached.Task.WaitAsync(TimeSpan.FromSeconds(5));
            Assert.True(Convert.ToInt32(GetField(router, "lifecycleGateUsers")) >= 2);

            var startAttempt = router.StartAsync([]);
            var disposeTask = router.DisposeAsync().AsTask();

            var startException = await Record.ExceptionAsync(
                () => startAttempt.WaitAsync(TimeSpan.FromSeconds(5)));
            Assert.IsType<InvalidOperationException>(startException);

            releaseStartup.SetResult();
            await firstStart.WaitAsync(TimeSpan.FromSeconds(5));
            Assert.False(disposeTask.IsCompleted);

            releaseStopAdmission.SetResult();
            await stopTask.WaitAsync(TimeSpan.FromSeconds(5));
            await disposeTask.WaitAsync(TimeSpan.FromSeconds(5));
            Assert.False(router.IsRunning);
        }
        finally
        {
            releaseStartup.TrySetResult();
            releaseStopAdmission.TrySetResult();
        }
    }

    [Fact]
    public async Task DiscoveryStateCallback_CanStopTheRouterWithoutSelfDeadlock()
    {
        var stopResult = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
        var profile = CreateProfile("VRChat callback stop", 9015);
        await using var router = new OscRouterService(
            (discoveredProfile, _) => Task.FromResult<OscRouterService.DiscoveredOscTarget?>(CreateTarget(discoveredProfile.name)),
            null);

        router.DiscoveryStateChanged += state =>
        {
            if (state != OscDiscoveryState.Discovered)
            {
                return;
            }

            var stopTask = router.StopAsync();
            stopResult.TrySetResult(stopTask.Wait(TimeSpan.FromSeconds(1)));
        };

        await router.StartAsync([]);
        var sessionCancellation = Assert.IsType<CancellationTokenSource>(GetField(router, "runtimeCancellation"));
        InvokeServiceAdded(router, profile, sessionCancellation);

        Assert.True(await stopResult.Task.WaitAsync(TimeSpan.FromSeconds(5)));
        Assert.False(router.IsRunning);
    }

    [Fact]
    public async Task PublicRead_IsDrainedBeforeDisposeDisposesTheSession()
    {
        using var queryHost = new DelayedOscQueryHost();
        await using var router = new OscRouterService();
        Task? disposeTask = null;
        OSCQueryService? service = null;
        UdpClient? sender = null;
        try
        {
            await router.StartAsync([]);

            service = Assert.IsType<OSCQueryService>(GetField(router, "oscQueryService"));
            sender = Assert.IsType<UdpClient>(GetField(router, "sendClient"));
            SetField(
                router,
                "activeVrChatTarget",
                new OscRouterService.DiscoveredOscTarget("VRChat read barrier", IPAddress.Loopback, 9010, queryHost.Port));

            var readTask = router.GetCurrentAvatarParametersAsync();
            await queryHost.RequestStarted.Task.WaitAsync(TimeSpan.FromSeconds(5));

            disposeTask = router.DisposeAsync().AsTask();
            await Assert.ThrowsAsync<ObjectDisposedException>(
                () => router.StartAsync([]).WaitAsync(TimeSpan.FromSeconds(1)));
            Assert.False(disposeTask.IsCompleted);
            Assert.True(GetHttpListener(service).IsListening);

            queryHost.ReleaseWithEmptyTree();
            try
            {
                await readTask.WaitAsync(TimeSpan.FromSeconds(5));
            }
            catch (Exception)
            {
            }

            await disposeTask.WaitAsync(TimeSpan.FromSeconds(5));
            Assert.Throws<ObjectDisposedException>(() => sender!.Send([1], 1, new IPEndPoint(IPAddress.Loopback, 9)));
        }
        finally
        {
            queryHost.ReleaseWithEmptyTree();
            if (disposeTask is not null)
            {
                try
                {
                    await disposeTask.WaitAsync(TimeSpan.FromSeconds(5));
                }
                catch (Exception)
                {
                }
            }
        }
    }

    [Fact]
    public async Task PublicRead_IsDrainedBeforeNormalStopDisposesTheSession()
    {
        using var queryHost = new DelayedOscQueryHost();
        await using var router = new OscRouterService();
        Task? stopTask = null;
        OSCQueryService? service = null;
        UdpClient? sender = null;
        try
        {
            await router.StartAsync([]);

            service = Assert.IsType<OSCQueryService>(GetField(router, "oscQueryService"));
            sender = Assert.IsType<UdpClient>(GetField(router, "sendClient"));
            SetField(
                router,
                "activeVrChatTarget",
                new OscRouterService.DiscoveredOscTarget("VRChat stop read barrier", IPAddress.Loopback, 9010, queryHost.Port));

            var readTask = router.GetCurrentAvatarParametersAsync();
            await queryHost.RequestStarted.Task.WaitAsync(TimeSpan.FromSeconds(5));

            stopTask = router.StopAsync();
            Assert.False(stopTask.IsCompleted);
            Assert.True(GetHttpListener(service).IsListening);

            queryHost.ReleaseWithEmptyTree();
            try
            {
                await readTask.WaitAsync(TimeSpan.FromSeconds(5));
            }
            catch (Exception)
            {
            }

            await stopTask.WaitAsync(TimeSpan.FromSeconds(5));
            Assert.Throws<ObjectDisposedException>(() => sender!.Send([1], 1, new IPEndPoint(IPAddress.Loopback, 9)));
        }
        finally
        {
            queryHost.ReleaseWithEmptyTree();
            if (stopTask is not null)
            {
                try
                {
                    await stopTask.WaitAsync(TimeSpan.FromSeconds(5));
                }
                catch (Exception)
                {
                }
            }
        }
    }

    [Fact]
    public async Task InFlightSend_IsDrainedBeforeStopDisposesTheSender()
    {
        var sendStarted = NewSignal();
        var releaseSend = NewSignal();
        await using var router = new OscRouterService();
        await router.StartAsync([]);

        var service = Assert.IsType<OSCQueryService>(GetField(router, "oscQueryService"));
        var sender = Assert.IsType<UdpClient>(GetField(router, "sendClient"));
        SetField(
            router,
            "activeVrChatTarget",
            new OscRouterService.DiscoveredOscTarget("VRChat send barrier", IPAddress.Loopback, 9010, 9011));
        SetField(
            router,
            "sendAsyncOverride",
            (Func<UdpClient, byte[], IPEndPoint, CancellationToken, Task>)(async (_, _, _, _) =>
            {
                sendStarted.SetResult();
                await releaseSend.Task.ConfigureAwait(false);
            }));

        Task? stopTask = null;
        try
        {
            var sendTask = router.SendToVrChatAsync([1]);
            await sendStarted.Task.WaitAsync(TimeSpan.FromSeconds(5));

            stopTask = router.StopAsync();
            Assert.False(stopTask.IsCompleted);
            Assert.True(GetHttpListener(service).IsListening);

            releaseSend.SetResult();
            try
            {
                await sendTask.WaitAsync(TimeSpan.FromSeconds(5));
            }
            catch (Exception)
            {
            }

            await stopTask.WaitAsync(TimeSpan.FromSeconds(5));
            Assert.Throws<ObjectDisposedException>(() => sender!.Send([1], 1, new IPEndPoint(IPAddress.Loopback, 9)));
        }
        finally
        {
            releaseSend.TrySetResult();
        }
    }

    [Fact]
    public async Task CallbackOwnedStop_DrainsForeignNotificationBeforeDisposingTheSession()
    {
        var foreignNotificationEntered = NewSignal();
        var releaseForeignNotification = NewSignal();
        var callbackStopCompleted = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
        await using var router = new OscRouterService();
        await router.StartAsync([]);

        var service = Assert.IsType<OSCQueryService>(GetField(router, "oscQueryService"));
        var sessionCancellation = Assert.IsType<CancellationTokenSource>(GetField(router, "runtimeCancellation"));
        router.LogWritten += message =>
        {
            if (string.Equals(message, "foreign notification", StringComparison.Ordinal))
            {
                foreignNotificationEntered.SetResult();
                releaseForeignNotification.Task.GetAwaiter().GetResult();
                return;
            }

            if (string.Equals(message, "callback stop", StringComparison.Ordinal))
            {
                var stopTask = router.StopAsync();
                callbackStopCompleted.SetResult(stopTask.Wait(TimeSpan.FromSeconds(1)));
            }
        };

        var foreignTask = Task.Run(() => InvokeSessionLog(
            router,
            service,
            sessionCancellation,
            sessionCancellation.Token,
            "foreign notification"));
        try
        {
            await foreignNotificationEntered.Task.WaitAsync(TimeSpan.FromSeconds(5));

            var callbackTask = Task.Run(() => InvokeSessionLog(
                router,
                service,
                sessionCancellation,
                sessionCancellation.Token,
                "callback stop"));
            Assert.False(await callbackStopCompleted.Task.WaitAsync(TimeSpan.FromSeconds(5)));
            Assert.True(GetHttpListener(service).IsListening);

            releaseForeignNotification.SetResult();
            await foreignTask.WaitAsync(TimeSpan.FromSeconds(5));
            await callbackTask.WaitAsync(TimeSpan.FromSeconds(5));
            await router.StopAsync().WaitAsync(TimeSpan.FromSeconds(5));
            Assert.False(GetHttpListener(service).IsListening);
        }
        finally
        {
            releaseForeignNotification.TrySetResult();
        }
    }

    [Fact]
    public async Task PublicOperationDelegate_CanSynchronouslyWaitForStop()
    {
        var callbackStopCompleted = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
        await using var router = new OscRouterService();
        await router.StartAsync([]);

        router.LogWritten += message =>
        {
            if (!message.StartsWith("Forcing an OSCQuery refresh", StringComparison.Ordinal))
            {
                return;
            }

            var stopTask = router.StopAsync();
            callbackStopCompleted.SetResult(stopTask.Wait(TimeSpan.FromSeconds(1)));
        };

        var refreshTask = router.ForceRefreshAsync();
        Assert.True(await callbackStopCompleted.Task.WaitAsync(TimeSpan.FromSeconds(5)));
        try
        {
            await refreshTask.WaitAsync(TimeSpan.FromSeconds(5));
        }
        catch (Exception)
        {
        }

        Assert.False(router.IsRunning);
    }

    [Fact]
    public async Task CancellationCallback_CanRequestStopAfterSessionFieldsAreCleared()
    {
        var registrationCreated = NewSignal();
        var releaseNotification = NewSignal();
        var callbackStopCompleted = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
        var router = new OscRouterService();
        await router.StartAsync([]);

        router.LogWritten += message =>
        {
            if (!message.StartsWith("Forcing an OSCQuery refresh", StringComparison.Ordinal))
            {
                return;
            }

            var sessionCancellation = Assert.IsType<CancellationTokenSource>(GetField(router, "runtimeCancellation"));
            using var registration = sessionCancellation.Token.Register(() =>
            {
                var stopTask = router.StopAsync();
                callbackStopCompleted.TrySetResult(stopTask.Wait(TimeSpan.FromSeconds(1)));
            });
            registrationCreated.SetResult();
            releaseNotification.Task.GetAwaiter().GetResult();
        };

        try
        {
            var refreshTask = Task.Run(() => router.ForceRefreshAsync());
            await registrationCreated.Task.WaitAsync(TimeSpan.FromSeconds(5));
            var stopTask = Task.Run(() => router.StopAsync());
            Assert.True(await callbackStopCompleted.Task.WaitAsync(TimeSpan.FromSeconds(5)));
            Assert.False(stopTask.IsCompleted);

            releaseNotification.SetResult();
            try
            {
                await refreshTask.WaitAsync(TimeSpan.FromSeconds(5));
            }
            catch (Exception)
            {
            }

            await stopTask.WaitAsync(TimeSpan.FromSeconds(5));
        }
        finally
        {
            releaseNotification.TrySetResult();
            try
            {
                await Task.Run(() => router.DisposeAsync().AsTask()).WaitAsync(TimeSpan.FromSeconds(5));
            }
            catch (Exception)
            {
            }
        }
    }

    [Fact]
    public async Task StopAsync_DrainsServiceAddedCallbackAndRejectsStalePublication()
    {
        var callbackStarted = NewSignal();
        var stopRequested = NewSignal();
        var releaseCallback = NewSignal();
        var callbackProfile = CreateProfile("VRChat stale callback", 9011);
        await using var router = new OscRouterService(
            async (profile, cancellationToken) =>
            {
                if (!string.Equals(profile.name, callbackProfile.name, StringComparison.Ordinal))
                {
                    return null;
                }

                using var registration = cancellationToken.Register(() => stopRequested.TrySetResult());
                callbackStarted.SetResult();
                await releaseCallback.Task.ConfigureAwait(false);
                return CreateTarget(profile.name);
            },
            null);
        try
        {
            await router.StartAsync([]);

            var service = Assert.IsType<OSCQueryService>(GetField(router, "oscQueryService"));
            var sessionCancellation = Assert.IsType<CancellationTokenSource>(GetField(router, "runtimeCancellation"));
            InvokeServiceAdded(router, callbackProfile, sessionCancellation);

            await callbackStarted.Task.WaitAsync(TimeSpan.FromSeconds(5));
            var callbackTask = Assert.IsType<Task[]>(GetField(router, "runtimeTasks")).Last();
            var stopTask = router.StopAsync();

            await stopRequested.Task.WaitAsync(TimeSpan.FromSeconds(5));
            Assert.Equal(0, GetEventHandlerCount(service, "OnOscQueryServiceAdded"));

            releaseCallback.SetResult();
            await stopTask.WaitAsync(TimeSpan.FromSeconds(5));

            Assert.True(callbackTask.IsCompleted);
            Assert.Empty(Assert.IsType<Task[]>(GetField(router, "runtimeTasks")));
            Assert.False(router.HasDiscoveredVrChat);
            Assert.Equal(OscDiscoveryState.Idle, router.DiscoveryState);
            Assert.Null(GetField(router, "oscQueryServiceAddedHandler"));
            Assert.False(GetHttpListener(service).IsListening);
        }
        finally
        {
            releaseCallback.TrySetResult();
        }
    }

    [Fact]
    public async Task StartupFailure_DrainsCallbackAndUnsubscribesBeforeDisposingService()
    {
        var callbackStarted = NewSignal();
        var stopRequested = NewSignal();
        var releaseCallback = NewSignal();
        var failureReached = NewSignal();
        var callbackProfile = CreateProfile("VRChat startup failure", 9012);
        OSCQueryService? serviceAtFailure = null;
        Task? callbackTaskAtFailure = null;
        var expectedFailure = new InvalidOperationException("startup failure barrier");
        var releaseFailure = NewSignal();

        var router = new OscRouterService(
            async (profile, cancellationToken) =>
            {
                if (!string.Equals(profile.name, callbackProfile.name, StringComparison.Ordinal))
                {
                    return null;
                }

                using var registration = cancellationToken.Register(() => stopRequested.TrySetResult());
                callbackStarted.SetResult();
                await releaseCallback.Task.ConfigureAwait(false);
                return CreateTarget(profile.name);
            },
            null);

        try
        {
            router.LogWritten += message =>
            {
                if (!message.Contains(" is live.", StringComparison.Ordinal))
                {
                    return;
                }

                serviceAtFailure = Assert.IsType<OSCQueryService>(GetField(router, "oscQueryService"));
                var sessionCancellation = Assert.IsType<CancellationTokenSource>(GetField(router, "runtimeCancellation"));
                InvokeServiceAdded(router, callbackProfile, sessionCancellation);
                callbackTaskAtFailure = Assert.IsType<Task[]>(GetField(router, "runtimeTasks")).Last();
                callbackStarted.Task.GetAwaiter().GetResult();
                failureReached.SetResult();
                releaseFailure.Task.GetAwaiter().GetResult();
                throw expectedFailure;
            };

            var startTask = Task.Run(() => router.StartAsync([]));
            await callbackStarted.Task.WaitAsync(TimeSpan.FromSeconds(5));
            await failureReached.Task.WaitAsync(TimeSpan.FromSeconds(5));
            releaseFailure.SetResult();
            await stopRequested.Task.WaitAsync(TimeSpan.FromSeconds(5));

            Assert.NotNull(serviceAtFailure);
            Assert.Equal(0, GetEventHandlerCount(serviceAtFailure, "OnOscQueryServiceAdded"));

            releaseCallback.SetResult();

            var actualFailure = await Assert.ThrowsAsync<InvalidOperationException>(
                () => startTask.WaitAsync(TimeSpan.FromSeconds(5)));

            Assert.Same(expectedFailure, actualFailure);
            Assert.NotNull(serviceAtFailure);
            Assert.NotNull(callbackTaskAtFailure);
            Assert.True(callbackTaskAtFailure.IsCompleted);
            Assert.Empty(Assert.IsType<Task[]>(GetField(router, "runtimeTasks")));
            Assert.False(router.IsRunning);
            Assert.Null(GetField(router, "oscQueryServiceAddedHandler"));
            Assert.False(GetHttpListener(serviceAtFailure).IsListening);
        }
        finally
        {
            releaseFailure.TrySetResult();
            releaseCallback.TrySetResult();
            await router.DisposeAsync();
        }
    }

    [Fact]
    public async Task SessionReplacement_DoesNotAcceptStaleCallbackAndPublishesCurrentSession()
    {
        var oldCallbackStarted = NewSignal();
        var oldStopRequested = NewSignal();
        var releaseOldCallback = NewSignal();
        var oldProfile = CreateProfile("VRChat old session", 9013);
        var newProfile = CreateProfile("VRChat current session", 9014);
        var router = new OscRouterService(
            async (profile, cancellationToken) =>
            {
                if (string.Equals(profile.name, oldProfile.name, StringComparison.Ordinal))
                {
                    using var registration = cancellationToken.Register(() => oldStopRequested.TrySetResult());
                    oldCallbackStarted.SetResult();
                    await releaseOldCallback.Task.ConfigureAwait(false);
                    return CreateTarget(profile.name);
                }

                return string.Equals(profile.name, newProfile.name, StringComparison.Ordinal)
                    ? CreateTarget(profile.name)
                    : null;
            },
            null);
        try
        {
            await router.StartAsync([]);
            var oldSessionCancellation = Assert.IsType<CancellationTokenSource>(GetField(router, "runtimeCancellation"));
            InvokeServiceAdded(router, oldProfile, oldSessionCancellation);
            await oldCallbackStarted.Task.WaitAsync(TimeSpan.FromSeconds(5));
            var oldCallbackTask = Assert.IsType<Task[]>(GetField(router, "runtimeTasks")).Last();
            var firstStopTask = router.StopAsync();
            await oldStopRequested.Task.WaitAsync(TimeSpan.FromSeconds(5));
            releaseOldCallback.SetResult();
            await firstStopTask.WaitAsync(TimeSpan.FromSeconds(5));

            Assert.True(oldCallbackTask.IsCompleted);
            Assert.Empty(Assert.IsType<Task[]>(GetField(router, "runtimeTasks")));
            Assert.False(router.HasDiscoveredVrChat);

            var discovered = NewStateSignal();
            router.DiscoveryStateChanged += state =>
            {
                if (state == OscDiscoveryState.Discovered)
                {
                    discovered.TrySetResult(state);
                }
            };

            await router.StartAsync([]);
            var newSessionCancellation = Assert.IsType<CancellationTokenSource>(GetField(router, "runtimeCancellation"));
            InvokeServiceAdded(router, newProfile, newSessionCancellation);
            await discovered.Task.WaitAsync(TimeSpan.FromSeconds(5));

            Assert.True(router.HasDiscoveredVrChat);
            var activeTarget = Assert.IsType<OscRouterService.DiscoveredOscTarget>(GetField(router, "activeVrChatTarget"));
            Assert.Equal("VRChat current session", activeTarget.Name);
        }
        finally
        {
            releaseOldCallback.TrySetResult();
            await router.DisposeAsync();
        }
    }

    private static OSCQueryService CreateOscQueryService()
    {
        return new OSCQueryServiceBuilder()
            .WithHostIP(IPAddress.Loopback)
            .WithTcpPort(Extensions.GetAvailableTcpPort())
            .StartHttpServer()
            .Build();
    }

    private static OscRouterService CreateRouterWithStopAdmissionHook(
        Func<OSCQueryService, Task> startupHook,
        Func<Task> stopAdmissionHook)
    {
        return new OscRouterService(null, startupHook, null, stopAdmissionHook);
    }

    private static object? GetField(OscRouterService router, string name)
    {
        return typeof(OscRouterService)
            .GetField(name, BindingFlags.Instance | BindingFlags.NonPublic)
            ?.GetValue(router);
    }

    private static void SetField(OscRouterService router, string name, object? value)
    {
        var field = typeof(OscRouterService).GetField(name, BindingFlags.Instance | BindingFlags.NonPublic);
        Assert.NotNull(field);
        field.SetValue(router, value);
    }

    private static void InvokeServiceAdded(
        OscRouterService router,
        OSCQueryServiceProfile profile,
        CancellationTokenSource sessionCancellation)
    {
        var service = Assert.IsType<OSCQueryService>(GetField(router, "oscQueryService"));
        var method = typeof(OscRouterService).GetMethod(
            "HandleOscQueryServiceAdded",
            BindingFlags.Instance | BindingFlags.NonPublic)
            ?? throw new Xunit.Sdk.XunitException("Expected HandleOscQueryServiceAdded to exist.");
        method.Invoke(router, new object?[] { service, profile, sessionCancellation, sessionCancellation.Token });
    }

    private static void InvokeSessionLog(
        OscRouterService router,
        OSCQueryService service,
        CancellationTokenSource sessionCancellation,
        CancellationToken operationToken,
        string message)
    {
        var method = typeof(OscRouterService).GetMethod(
            "PublishSessionLog",
            BindingFlags.Instance | BindingFlags.NonPublic)
            ?? throw new Xunit.Sdk.XunitException("Expected PublishSessionLog to exist.");
        method.Invoke(router, new object?[] { service, sessionCancellation, operationToken, message });
    }

    private static OSCQueryServiceProfile CreateProfile(string name, int port)
    {
        return new OSCQueryServiceProfile(
            name,
            IPAddress.Loopback,
            port,
            OSCQueryServiceProfile.ServiceType.OSCQuery);
    }

    private static OscRouterService.DiscoveredOscTarget CreateTarget(string name)
    {
        return new OscRouterService.DiscoveredOscTarget(name, IPAddress.Loopback, 9010, 9011);
    }

    private static int GetEventHandlerCount(object instance, string name)
    {
        var field = instance.GetType().GetField(name, BindingFlags.Instance | BindingFlags.NonPublic)
            ?? throw new Xunit.Sdk.XunitException($"Expected event field {name} to exist.");
        return (field.GetValue(instance) as Delegate)?.GetInvocationList().Length ?? 0;
    }

    private static HttpListener GetHttpListener(OSCQueryService service)
    {
        var httpServer = typeof(OSCQueryService)
            .GetField("_http", BindingFlags.Instance | BindingFlags.NonPublic)
            ?.GetValue(service)
            ?? throw new Xunit.Sdk.XunitException("Expected OSCQuery HTTP server to exist.");
        return Assert.IsType<HttpListener>(
            httpServer.GetType().GetField("_listener", BindingFlags.Instance | BindingFlags.NonPublic)?.GetValue(httpServer));
    }

    private static TaskCompletionSource NewSignal()
    {
        return new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
    }

    private static TaskCompletionSource<OscDiscoveryState> NewStateSignal()
    {
        return new TaskCompletionSource<OscDiscoveryState>(TaskCreationOptions.RunContinuationsAsynchronously);
    }

    private static IReadOnlyList<TriggerRuleSnapshot> CreateEndpointRules()
    {
        return [
            TriggerRuleSnapshot.FromRule(new TriggerRule
            {
                ParameterName = "PoisonedEndpoint",
                ParameterType = OscParameterType.Float
            })
        ];
    }

    private sealed class DelayedOscQueryHost : IDisposable
    {
        private readonly HttpListener listener = new();
        private readonly TaskCompletionSource<HttpListenerContext> request = new(TaskCreationOptions.RunContinuationsAsynchronously);
        private int responseReleased;

        public DelayedOscQueryHost()
        {
            Port = Extensions.GetAvailableTcpPort();
            listener.Prefixes.Add($"http://{IPAddress.Loopback}:{Port}/");
            listener.Start();
            _ = AcceptRequestAsync();
        }

        public int Port { get; }

        public TaskCompletionSource RequestStarted { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);

        public void ReleaseWithEmptyTree()
        {
            if (!request.Task.IsCompletedSuccessfully)
            {
                return;
            }

            if (Interlocked.Exchange(ref responseReleased, 1) != 0)
            {
                return;
            }

            var context = request.Task.GetAwaiter().GetResult();
            var bytes = System.Text.Encoding.UTF8.GetBytes(new OSCQueryRootNode().ToString());
            context.Response.ContentLength64 = bytes.Length;
            context.Response.ContentType = "application/json";
            context.Response.OutputStream.Write(bytes, 0, bytes.Length);
            context.Response.Close();
        }

        public void Dispose()
        {
            listener.Close();
        }

        private async Task AcceptRequestAsync()
        {
            try
            {
                var context = await listener.GetContextAsync().ConfigureAwait(false);
                request.SetResult(context);
                RequestStarted.SetResult();
            }
            catch (Exception ex)
            {
                request.TrySetException(ex);
                RequestStarted.TrySetException(ex);
            }
        }
    }

    private static string GetMethodBody(string source, string methodSignatureStart)
    {
        var methodStart = source.IndexOf(methodSignatureStart, StringComparison.Ordinal);
        Assert.True(methodStart >= 0, $"Could not find method signature starting with '{methodSignatureStart}'.");

        var bodyStart = source.IndexOf('{', methodStart);
        Assert.True(bodyStart >= 0, $"Could not find method body for '{methodSignatureStart}'.");
        return GetBalancedBlock(source, bodyStart);
    }

    private static string GetBalancedBlock(string source, int bodyStart)
    {
        var depth = 0;
        for (var index = bodyStart; index < source.Length; index++)
        {
            if (source[index] == '{')
            {
                depth++;
            }
            else if (source[index] == '}')
            {
                depth--;
                if (depth == 0)
                {
                    return source[bodyStart..(index + 1)];
                }
            }
        }

        throw new InvalidOperationException("Could not find the end of a source block.");
    }

    private static string NormalizeWhitespace(string source) => Regex.Replace(source, @"\s+", " ");

    private static string FindSourceFile(params string[] relativeParts)
    {
        var directory = new DirectoryInfo(AppContext.BaseDirectory);
        while (directory is not null)
        {
            var candidate = Path.Combine(new[] { directory.FullName }.Concat(relativeParts).ToArray());
            if (File.Exists(candidate))
            {
                return candidate;
            }

            directory = directory.Parent;
        }

        throw new FileNotFoundException("Could not find source file.", Path.Combine(relativeParts));
    }
}
