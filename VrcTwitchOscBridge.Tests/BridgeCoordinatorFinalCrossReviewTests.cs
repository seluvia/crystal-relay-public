using System.Collections;
using System.Collections.Concurrent;
using System.IO;
using System.Net;
using System.Reflection;
using System.Windows.Threading;
using VRC.OSCQuery;
using VrcTwitchOscBridge.Models;
using VrcTwitchOscBridge.Services;
using Xunit;

namespace VrcTwitchOscBridge.Tests;

[Collection("BridgeCoordinator integration")]
public sealed class BridgeCoordinatorFinalCrossReviewTests
{
    [Fact]
    public void RuntimeLifecycleEntryPointsShareOneGateAndPrivateStopCore()
    {
        var source = File.ReadAllText(FindSourceFile("VrcTwitchOscBridge", "Services", "BridgeCoordinator.cs"));
        var startBody = NormalizeWhitespace(GetMethodBody(source, "public async Task StartAsync("));
        var oscOnlyBody = NormalizeWhitespace(GetMethodBody(source, "public async Task StartOscOnlyAsync("));
        var stopBody = NormalizeWhitespace(GetMethodBody(source, "public async Task StopAsync("));
        var disposeBody = NormalizeWhitespace(GetMethodBody(source, "public async ValueTask DisposeAsync("));

        Assert.Contains("runtimeLifecycleGate.WaitAsync", startBody, StringComparison.Ordinal);
        Assert.Contains("runtimeLifecycleGate.WaitAsync", oscOnlyBody, StringComparison.Ordinal);
        Assert.Contains("runtimeLifecycleGate.WaitAsync", stopBody, StringComparison.Ordinal);
        Assert.Contains("runtimeLifecycleGate.WaitAsync", disposeBody, StringComparison.Ordinal);
        Assert.Contains("StopCoreAsync", stopBody, StringComparison.Ordinal);
        Assert.DoesNotContain("await StopAsync()", disposeBody, StringComparison.Ordinal);
    }

    [Theory]
    [InlineData("ActiveFloatRedeemSessionState")]
    [InlineData("ActiveFloatPulseRestoreState")]
    [InlineData("ActiveFloatGlitchyRedeemSessionState")]
    public async Task AvatarScopedFloatEffectsDoNotSendAfterSourceAvatarChanges(string stateTypeName)
    {
        const string sourceAvatarId = "avatar-source";
        var coordinator = CreateCoordinator();
        var sendCount = 0;
        SetPrivateField(coordinator, "currentVrChatAvatarId", sourceAvatarId);
        SetPrivateField(
            coordinator,
            "testSendToVrChatAsync",
            (Func<byte[], CancellationToken, Task>)((_, _) =>
            {
                Interlocked.Increment(ref sendCount);
                return Task.CompletedTask;
            }));
        SetPrivateField(coordinator, "testStopOscRouterAsync", (Func<Task>)(() => Task.CompletedTask));
        SetPrivateField(coordinator, "testForceReleaseDesktopInputAsync", (Func<CancellationToken, Task>)(_ => Task.CompletedTask));

        try
        {
            ArrangeExpiredAvatarScopedFloatState(coordinator, stateTypeName, sourceAvatarId);

            switch (stateTypeName)
            {
                case "ActiveFloatRedeemSessionState":
                {
                    var session = GetDictionaryValue(coordinator, "activeFloatRedeemSessions", GetRuleId(coordinator, stateTypeName));
                    coordinator.UpdateCurrentVrChatAvatar("avatar-new");
                    SetPrivateField(coordinator, "lastAvatarChangeAt", DateTimeOffset.MinValue);
                    InvokePrivate(coordinator, "ScheduleActiveFloatRedeemCompletion", session!, GetPrivateProperty<CancellationTokenSource>(session!, "CompletionCancellation"), 0d, 1d, 0d);
                    break;
                }
                case "ActiveFloatPulseRestoreState":
                {
                    var pulse = GetDictionaryValue(coordinator, "activeFloatPulseRestores", GetRuleId(coordinator, stateTypeName));
                    var sourceRule = new TriggerRule
                    {
                        Id = GetPrivateProperty<Guid>(pulse!, "RuleId"),
                        Name = GetPrivateProperty<string>(pulse!, "RuleName"),
                        FloatPulseSeconds = 0.2d
                    };
                    InvokePrivate(
                        coordinator,
                        "ScheduleFloatPulseRestore",
                        sourceRule,
                        GetPrivateProperty<string>(pulse!, "Address"),
                        new byte[] { 1 },
                        GetPrivateProperty<double>(pulse!, "ResetValue"),
                        sourceAvatarId);
                    coordinator.UpdateCurrentVrChatAvatar("avatar-new");
                    break;
                }
                case "ActiveFloatGlitchyRedeemSessionState":
                {
                    var glitchy = GetDictionaryValue(coordinator, "activeGlitchyRedeemSessions", GetRuleId(coordinator, stateTypeName));
                    var cancellation = GetPrivateProperty<CancellationTokenSource>(glitchy!, "CompletionCancellation");
                    var worker = (Func<Task>)(() => Assert.IsAssignableFrom<Task>(
                        InvokePrivate(coordinator, "RunGlitchyLoopAsync", glitchy!)));
                    Assert.True(Assert.IsType<bool>(InvokePrivate(
                        coordinator,
                        "TryStartPersistentEffectWorker",
                        worker,
                        cancellation)));
                    coordinator.UpdateCurrentVrChatAvatar("avatar-new");
                    break;
                }
                default:
                    throw new ArgumentOutOfRangeException(nameof(stateTypeName), stateTypeName, "Unknown float state type.");
            }

            await DrainPersistentEffectWorkersAsync(coordinator);
            Assert.Equal(0, Volatile.Read(ref sendCount));
            var stateFieldName = stateTypeName switch
            {
                "ActiveFloatRedeemSessionState" => "activeFloatRedeemSessions",
                "ActiveFloatPulseRestoreState" => "activeFloatPulseRestores",
                "ActiveFloatGlitchyRedeemSessionState" => "activeGlitchyRedeemSessions",
                _ => throw new ArgumentOutOfRangeException(nameof(stateTypeName), stateTypeName, "Unknown float state type.")
            };
            Assert.NotNull(GetDictionaryValue(coordinator, stateFieldName, GetRuleId(coordinator, stateTypeName)));
        }
        finally
        {
            await coordinator.StopAsync().WaitAsync(TimeSpan.FromSeconds(5));
        }
    }

    [Fact]
    public async Task InitialAvatarScopedFloatSend_SkipsStalePacketAfterAsyncReadAvatarChange()
    {
        const string sourceAvatarId = "avatar-source";
        const string replacementAvatarId = "avatar-replacement";
        const string parameterAddress = "/avatar/parameters/InitialProfileFloat";
        var coordinator = CreateCoordinator();
        using var queryHost = new BlockingAvatarHeightQueryHost(
            0.25f,
            address: parameterAddress);
        var sentPackets = new ConcurrentQueue<byte[]>();
        var router = GetPrivateField<OscRouterService>(coordinator, "oscRouterService");
        var rule = CreateProfileFloatRule(parameterAddress);
        SetPrivateField(coordinator, "currentVrChatAvatarId", sourceAvatarId);
        SetPrivateField(
            coordinator,
            "testSendToVrChatAsync",
            (Func<byte[], CancellationToken, Task>)((packet, _) =>
            {
                sentPackets.Enqueue(packet);
                return Task.CompletedTask;
            }));
        SetPrivateField(coordinator, "testStopOscRouterAsync", (Func<Task>)(() => Task.CompletedTask));
        SetPrivateField(coordinator, "testForceReleaseDesktopInputAsync", (Func<CancellationToken, Task>)(_ => Task.CompletedTask));

        try
        {
            await router.StartAsync([]).WaitAsync(TimeSpan.FromSeconds(5));
            SetPrivateField(
                router,
                "activeVrChatTarget",
                new OscRouterService.DiscoveredOscTarget(
                    "Initial float test VRChat",
                    IPAddress.Loopback,
                    9010,
                    queryHost.Port));

            var sendTask = InvokePrivateTask(
                coordinator,
                "ExecuteFloatAvatarParameterWithTransitionAsync",
                rule,
                CancellationToken.None);
            await queryHost.BlockedRequestStarted.Task.WaitAsync(TimeSpan.FromSeconds(5));

            coordinator.UpdateCurrentVrChatAvatar(replacementAvatarId);
            queryHost.Release();
            await sendTask;

            Assert.Empty(sentPackets);
        }
        finally
        {
            queryHost.Release();
            await coordinator.StopAsync().WaitAsync(TimeSpan.FromSeconds(5));
            await router.DisposeAsync().AsTask().WaitAsync(TimeSpan.FromSeconds(5));
        }
    }

    [Fact]
    public async Task ResolvedProfileFloatAction_SkipsStalePacketAndDoesNotInstallReplacementReset()
    {
        const string sourceAvatarId = "avatar-source";
        const string replacementAvatarId = "avatar-replacement";
        const string parameterAddress = "/avatar/parameters/ResolvedProfileFloat";
        var coordinator = CreateCoordinator();
        using var queryHost = new BlockingAvatarHeightQueryHost(
            0.25f,
            address: parameterAddress);
        var sentPackets = new ConcurrentQueue<byte[]>();
        var runtimeCancellation = new CancellationTokenSource();
        var router = GetPrivateField<OscRouterService>(coordinator, "oscRouterService");
        var rule = CreateProfileFloatRule(parameterAddress) with
        {
            DurationSeconds = 30,
            ResetValue = "0"
        };
        SetPrivateField(coordinator, "currentVrChatAvatarId", sourceAvatarId);
        SetPrivateField(coordinator, "runtimeCancellation", runtimeCancellation);
        SetPrivateField(
            coordinator,
            "testSendToVrChatAsync",
            (Func<byte[], CancellationToken, Task>)((packet, _) =>
            {
                sentPackets.Enqueue(packet);
                return Task.CompletedTask;
            }));
        SetPrivateField(coordinator, "testStopOscRouterAsync", (Func<Task>)(() => Task.CompletedTask));
        SetPrivateField(coordinator, "testForceReleaseDesktopInputAsync", (Func<CancellationToken, Task>)(_ => Task.CompletedTask));

        try
        {
            await router.StartAsync([]).WaitAsync(TimeSpan.FromSeconds(5));
            SetPrivateField(
                router,
                "activeVrChatTarget",
                new OscRouterService.DiscoveredOscTarget(
                    "Resolved float test VRChat",
                    IPAddress.Loopback,
                    9010,
                    queryHost.Port));

            var executeTask = InvokePrivateTask(
                coordinator,
                "ExecuteRuleActionAsync",
                rule,
                null,
                CancellationToken.None,
                true,
                false,
                false,
                false,
                null,
                null,
                null);
            await queryHost.BlockedRequestStarted.Task.WaitAsync(TimeSpan.FromSeconds(5));

            coordinator.UpdateCurrentVrChatAvatar(replacementAvatarId);
            queryHost.Release();
            await executeTask;

            Assert.Empty(sentPackets);
            Assert.Empty(GetPendingResets(coordinator));
        }
        finally
        {
            queryHost.Release();
            runtimeCancellation.Cancel();
            await coordinator.StopAsync().WaitAsync(TimeSpan.FromSeconds(5));
            await router.DisposeAsync().AsTask().WaitAsync(TimeSpan.FromSeconds(5));
            runtimeCancellation.Dispose();
        }
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task TimedProfileFloatInitialRejection_ReleasesOrPreservesExactOwnership(bool shutdownWins)
    {
        const string sourceAvatarId = "avatar-source";
        const string replacementAvatarId = "avatar-replacement";
        const string parameterAddress = "/avatar/parameters/TimedProfileFloat";
        var coordinator = CreateCoordinator();
        using var queryHost = new BlockingAvatarHeightQueryHost(
            0.25f,
            address: parameterAddress);
        var sentPackets = new ConcurrentQueue<byte[]>();
        var runtimeCancellation = new CancellationTokenSource();
        var router = GetPrivateField<OscRouterService>(coordinator, "oscRouterService");
        var rule = CreateProfileFloatRule(parameterAddress) with
        {
            DurationSeconds = 30,
            CooldownSeconds = 30,
            TemporarilyDisabledRuleIds = [Guid.NewGuid()]
        };
        SetPrivateField(coordinator, "currentVrChatAvatarId", sourceAvatarId);
        SetPrivateField(coordinator, "runtimeCancellation", runtimeCancellation);
        SetPrivateField(
            coordinator,
            "testSendToVrChatAsync",
            (Func<byte[], CancellationToken, Task>)((packet, _) =>
            {
                sentPackets.Enqueue(packet);
                return Task.CompletedTask;
            }));
        SetPrivateField(coordinator, "testStopOscRouterAsync", (Func<Task>)(() => Task.CompletedTask));
        SetPrivateField(coordinator, "testForceReleaseDesktopInputAsync", (Func<CancellationToken, Task>)(_ => Task.CompletedTask));

        Task? executeTask = null;
        try
        {
            await router.StartAsync([]).WaitAsync(TimeSpan.FromSeconds(5));
            SetPrivateField(
                router,
                "activeVrChatTarget",
                new OscRouterService.DiscoveredOscTarget(
                    "Timed float test VRChat",
                    IPAddress.Loopback,
                    9010,
                    queryHost.Port));

            executeTask = InvokePrivateTaskImmediately(
                coordinator,
                "ExecuteTimedFloatAvatarParameterRuleActionAsync",
                rule,
                null,
                CancellationToken.None,
                false,
                false,
                null,
                Guid.Empty,
                30);
            await queryHost.BlockedRequestStarted.Task.WaitAsync(TimeSpan.FromSeconds(5));

            SetPrivateField(coordinator, "currentVrChatAvatarId", replacementAvatarId);
            if (shutdownWins)
            {
                SetPrivateField(coordinator, "isStopping", true);
            }

            queryHost.Release();
            await executeTask.WaitAsync(TimeSpan.FromSeconds(5));

            Assert.Empty(sentPackets);
            var activeSessions = Assert.IsAssignableFrom<IDictionary>(GetPrivateField(coordinator, "activeFloatRedeemSessions"));
            var actionLanes = Assert.IsAssignableFrom<IDictionary>(GetPrivateField(coordinator, "actionLanes"));
            var cooldowns = Assert.IsAssignableFrom<IDictionary>(GetPrivateField(coordinator, "cooldowns"));
            var lockouts = Assert.IsAssignableFrom<IDictionary>(GetPrivateField(coordinator, "activeRuleLockouts"));
            var cooldownNotifications = Assert.IsAssignableFrom<IDictionary>(GetPrivateField(coordinator, "cooldownStateNotifications"));

            if (shutdownWins)
            {
                Assert.Contains(rule.Id, activeSessions.Keys.Cast<object>());
                Assert.NotEmpty(actionLanes);
                Assert.Contains(rule.Id, cooldowns.Keys.Cast<object>());
                Assert.Contains(rule.Id, lockouts.Keys.Cast<object>());
                Assert.Contains(rule.Id, cooldownNotifications.Keys.Cast<object>());
            }
            else
            {
                Assert.Empty(activeSessions);
                Assert.Empty(actionLanes);
                Assert.Empty(cooldowns);
                Assert.Empty(lockouts);
                Assert.Empty(cooldownNotifications);
            }
        }
        finally
        {
            queryHost.Release();
            if (executeTask is not null)
            {
                await executeTask.WaitAsync(TimeSpan.FromSeconds(5));
            }

            runtimeCancellation.Cancel();
            await coordinator.StopAsync().WaitAsync(TimeSpan.FromSeconds(5));
            await router.DisposeAsync().AsTask().WaitAsync(TimeSpan.FromSeconds(5));
        }
    }

    [Theory]
    [InlineData(FloatActionMode.Pulse)]
    [InlineData(FloatActionMode.Glitchy)]
    public async Task RejectedResolvedFloatInitialState_CancelsOnlyExactPersistentState(FloatActionMode actionMode)
    {
        const string sourceAvatarId = "avatar-source";
        const string replacementAvatarId = "avatar-replacement";
        var parameterAddress = actionMode == FloatActionMode.Pulse
            ? "/avatar/parameters/PulseInitialFloat"
            : "/avatar/parameters/GlitchyInitialFloat";
        var coordinator = CreateCoordinator();
        using var queryHost = new BlockingAvatarHeightQueryHost(
            0.25f,
            address: parameterAddress);
        var sentPackets = new ConcurrentQueue<byte[]>();
        var router = GetPrivateField<OscRouterService>(coordinator, "oscRouterService");
        var rule = CreateProfileFloatRule(parameterAddress) with
        {
            DurationSeconds = 30
        };
        rule.Rule.Id = rule.Id;
        rule.Rule.DurationSeconds = 30;
        rule.Rule.FloatActionMode = actionMode;
        if (actionMode == FloatActionMode.Pulse)
        {
            rule.Rule.FloatPulseSeconds = 60;
        }
        else
        {
            rule.Rule.FloatGlitchyIntervalMs = 60000;
        }

        SetPrivateField(coordinator, "currentVrChatAvatarId", sourceAvatarId);
        SetPrivateField(
            coordinator,
            "testSendToVrChatAsync",
            (Func<byte[], CancellationToken, Task>)((packet, _) =>
            {
                sentPackets.Enqueue(packet);
                return Task.CompletedTask;
            }));
        SetPrivateField(coordinator, "testStopOscRouterAsync", (Func<Task>)(() => Task.CompletedTask));
        SetPrivateField(coordinator, "testForceReleaseDesktopInputAsync", (Func<CancellationToken, Task>)(_ => Task.CompletedTask));

        var avatarWriteGate = GetPrivateField<SemaphoreSlim>(coordinator, "avatarScopedResetSendGate");
        Assert.True(avatarWriteGate.Wait(0));
        var gateHeld = true;
        Task? executeTask = null;
        object? originalState = null;
        CancellationTokenSource? originalCancellation = null;
        object? replacementState = null;
        CancellationTokenSource? replacementCancellation = null;
        try
        {
            await router.StartAsync([]).WaitAsync(TimeSpan.FromSeconds(5));
            SetPrivateField(
                router,
                "activeVrChatTarget",
                new OscRouterService.DiscoveredOscTarget(
                    "Resolved effect test VRChat",
                    IPAddress.Loopback,
                    9010,
                    queryHost.Port));

            executeTask = InvokePrivateTaskImmediately(
                coordinator,
                "ExecuteRuleActionAsync",
                rule,
                null,
                CancellationToken.None,
                true,
                false,
                false,
                false,
                null,
                null,
                null);
            await queryHost.BlockedRequestStarted.Task.WaitAsync(TimeSpan.FromSeconds(5));
            queryHost.Release();

            var stateField = actionMode == FloatActionMode.Pulse
                ? "activeFloatPulseRestores"
                : "activeGlitchyRedeemSessions";
            await WaitUntilAsync(() =>
            {
                var states = Assert.IsAssignableFrom<IDictionary>(GetPrivateField(coordinator, stateField));
                return states.Contains(rule.Id);
            });

            originalState = GetDictionaryValue(coordinator, stateField, rule.Id);
            originalCancellation = actionMode == FloatActionMode.Pulse
                ? GetPrivateProperty<CancellationTokenSource>(originalState!, "Cancellation")
                : GetPrivateProperty<CancellationTokenSource>(originalState!, "CompletionCancellation");

            replacementCancellation = new CancellationTokenSource();
            replacementState = CreateReplacementFloatInitialState(
                actionMode,
                rule,
                sourceAvatarId,
                replacementCancellation,
                Guid.NewGuid());
            var statesForReplacement = Assert.IsAssignableFrom<IDictionary>(GetPrivateField(coordinator, stateField));
            statesForReplacement[rule.Id] = replacementState;
            SetPrivateField(coordinator, "currentVrChatAvatarId", replacementAvatarId);

            avatarWriteGate.Release();
            gateHeld = false;
            await executeTask.WaitAsync(TimeSpan.FromSeconds(5));

            Assert.Empty(sentPackets);
            Assert.Same(replacementState, statesForReplacement[rule.Id]);
            Assert.True(originalCancellation.IsCancellationRequested);
            Assert.False(replacementCancellation.IsCancellationRequested);
        }
        finally
        {
            queryHost.Release();
            if (gateHeld)
            {
                avatarWriteGate.Release();
            }

            if (executeTask is not null)
            {
                await executeTask.WaitAsync(TimeSpan.FromSeconds(5));
            }

            await coordinator.StopAsync().WaitAsync(TimeSpan.FromSeconds(5));
            await router.DisposeAsync().AsTask().WaitAsync(TimeSpan.FromSeconds(5));
            replacementCancellation?.Dispose();
        }
    }

    [Fact]
    public async Task ResolvedProfileIntAction_SkipsStalePacketAndPublishesNoObservedOrResetState()
    {
        const string sourceAvatarId = "avatar-source";
        const string replacementAvatarId = "avatar-replacement";
        const string parameterAddress = "/avatar/parameters/ResolvedProfileInt";
        var coordinator = CreateCoordinator();
        using var queryHost = new BlockingAvatarHeightQueryHost(
            0,
            address: parameterAddress,
            parameterType: OscParameterType.Int,
            integerValue: 2);
        var sentPackets = new ConcurrentQueue<byte[]>();
        var router = GetPrivateField<OscRouterService>(coordinator, "oscRouterService");
        var rule = TestTriggerRuleSnapshotBuilder.Build() with
        {
            Id = Guid.NewGuid(),
            Name = "profile int race",
            AvatarProfileId = Guid.NewGuid(),
            ParameterName = parameterAddress,
            ParameterType = OscParameterType.Int,
            IntZeroDurationMode = IntZeroDurationMode.Cycle,
            RangeMinimum = 0,
            RangeMaximum = 5,
            ParameterValue = "1",
            DurationSeconds = 0
        };
        rule.Rule.Id = rule.Id;
        rule.Rule.IntZeroDurationMode = IntZeroDurationMode.Cycle;
        rule.Rule.RangeMinimum = 0;
        rule.Rule.RangeMaximum = 5;
        SetPrivateField(coordinator, "currentVrChatAvatarId", sourceAvatarId);
        SetPrivateField(
            coordinator,
            "testSendToVrChatAsync",
            (Func<byte[], CancellationToken, Task>)((packet, _) =>
            {
                sentPackets.Enqueue(packet);
                return Task.CompletedTask;
            }));
        SetPrivateField(coordinator, "testStopOscRouterAsync", (Func<Task>)(() => Task.CompletedTask));
        SetPrivateField(coordinator, "testForceReleaseDesktopInputAsync", (Func<CancellationToken, Task>)(_ => Task.CompletedTask));

        Task? executeTask = null;
        try
        {
            await router.StartAsync([]).WaitAsync(TimeSpan.FromSeconds(5));
            SetPrivateField(
                router,
                "activeVrChatTarget",
                new OscRouterService.DiscoveredOscTarget(
                    "Resolved int test VRChat",
                    IPAddress.Loopback,
                    9010,
                    queryHost.Port));

            executeTask = InvokePrivateTaskImmediately(
                coordinator,
                "ExecuteRuleActionAsync",
                rule,
                null,
                CancellationToken.None,
                true,
                false,
                false,
                false,
                null,
                null,
                null);
            await queryHost.BlockedRequestStarted.Task.WaitAsync(TimeSpan.FromSeconds(5));

            SetPrivateField(coordinator, "currentVrChatAvatarId", replacementAvatarId);
            queryHost.Release();
            await executeTask.WaitAsync(TimeSpan.FromSeconds(5));

            var observedValues = Assert.IsAssignableFrom<IDictionary>(GetPrivateField(coordinator, "avatarParameterValues"));
            Assert.Empty(sentPackets);
            Assert.Empty(GetPendingResets(coordinator));
            Assert.False(observedValues.Contains(parameterAddress));
        }
        finally
        {
            queryHost.Release();
            if (executeTask is not null)
            {
                await executeTask.WaitAsync(TimeSpan.FromSeconds(5));
            }

            await coordinator.StopAsync().WaitAsync(TimeSpan.FromSeconds(5));
            await router.DisposeAsync().AsTask().WaitAsync(TimeSpan.FromSeconds(5));
        }
    }

    [Fact]
    public async Task ResolvedProfileSetTriggerAction_SkipsStalePacketsBeforeInstallingReset()
    {
        const string sourceAvatarId = "avatar-source";
        const string replacementAvatarId = "avatar-replacement";
        var coordinator = CreateCoordinator();
        var sentPackets = new ConcurrentQueue<byte[]>();
        var rule = TestTriggerRuleSnapshotBuilder.Build() with
        {
            Id = Guid.NewGuid(),
            Name = "profile Set Trigger race",
            ActionType = OscActionType.SetTrigger,
            AvatarProfileId = Guid.NewGuid(),
            DurationSeconds = 30
        };
        rule.Rule.Id = rule.Id;
        var action = CreateResolvedAction();
        SetProperty(action, "SourceAvatarId", sourceAvatarId);
        SetPrivateField(coordinator, "currentVrChatAvatarId", sourceAvatarId);
        SetPrivateField(
            coordinator,
            "testSendToVrChatAsync",
            (Func<byte[], CancellationToken, Task>)((packet, _) =>
            {
                sentPackets.Enqueue(packet);
                return Task.CompletedTask;
            }));
        SetPrivateField(coordinator, "testStopOscRouterAsync", (Func<Task>)(() => Task.CompletedTask));
        SetPrivateField(coordinator, "testForceReleaseDesktopInputAsync", (Func<CancellationToken, Task>)(_ => Task.CompletedTask));

        var avatarWriteGate = GetPrivateField<SemaphoreSlim>(coordinator, "avatarScopedResetSendGate");
        Assert.True(avatarWriteGate.Wait(0));
        var gateHeld = true;
        Task? executeTask = null;
        try
        {
            executeTask = InvokePrivateTaskImmediately(
                coordinator,
                "ExecuteRuleActionAsync",
                rule,
                null,
                CancellationToken.None,
                true,
                false,
                false,
                false,
                null,
                action,
                null);

            SetPrivateField(coordinator, "currentVrChatAvatarId", replacementAvatarId);
            avatarWriteGate.Release();
            gateHeld = false;
            await executeTask.WaitAsync(TimeSpan.FromSeconds(5));

            Assert.Empty(sentPackets);
            Assert.Empty(GetPendingResets(coordinator));
        }
        finally
        {
            if (gateHeld)
            {
                avatarWriteGate.Release();
            }

            if (executeTask is not null)
            {
                await executeTask.WaitAsync(TimeSpan.FromSeconds(5));
            }

            await coordinator.StopAsync().WaitAsync(TimeSpan.FromSeconds(5));
        }
    }

    [Fact]
    public async Task AvatarCallbackQueuedDuringViewModelShutdownDoesNotCallDisposedCoordinator()
    {
        var source = File.ReadAllText(FindSourceFile("VrcTwitchOscBridge", "ViewModels", "MainWindowViewModel.cs"));
        var handlerBody = NormalizeWhitespace(GetMethodBody(
            source,
            "private void HandleVrChatAvatarChangedByBridge("));

        var shutdownCheck = handlerBody.IndexOf("isShuttingDown", StringComparison.Ordinal);
        var updateCall = handlerBody.IndexOf("bridgeCoordinator.UpdateCurrentVrChatAvatar", StringComparison.Ordinal);

        Assert.True(shutdownCheck >= 0);
        Assert.True(updateCall > shutdownCheck);
    }

    [Fact]
    public async Task ActivityResumeScale_DoesNotSendSavedHeightAfterAvatarChangesDuringHeightRead()
    {
        const string sourceAvatarId = "avatar-source";
        const string replacementAvatarId = "avatar-replacement";
        var ruleId = Guid.NewGuid();
        var activityResume = new PendingAvatarScaleResumeService(new ResumeActivity
        {
            Type = ResumeActivityType.AvatarScale,
            RuleId = ruleId,
            ExpiresAt = DateTimeOffset.UtcNow.AddMinutes(1),
            CurrentValue = 1.25
        });
        var coordinator = CreateCoordinator(activityResume);
        using var queryHost = new BlockingAvatarHeightQueryHost(1.6f);
        var sentPackets = new ConcurrentQueue<byte[]>();
        var router = GetPrivateField<OscRouterService>(coordinator, "oscRouterService");
        SetPrivateField(
            coordinator,
            "testSendToVrChatAsync",
            (Func<byte[], CancellationToken, Task>)((packet, _) =>
            {
                sentPackets.Enqueue(packet);
                return Task.CompletedTask;
            }));
        SetPrivateField(coordinator, "testStopOscRouterAsync", (Func<Task>)(() => Task.CompletedTask));
        SetPrivateField(coordinator, "testForceReleaseDesktopInputAsync", (Func<CancellationToken, Task>)(_ => Task.CompletedTask));

        try
        {
            ConfigurePendingAvatarScaleResume(coordinator, ruleId, sourceAvatarId);
            await router.StartAsync([]).WaitAsync(TimeSpan.FromSeconds(5));
            SetPrivateField(
                router,
                "activeVrChatTarget",
                new OscRouterService.DiscoveredOscTarget(
                    "Activity resume test VRChat",
                    IPAddress.Loopback,
                    9010,
                    queryHost.Port));

            var resumeTask = coordinator.TryResumePendingActivitiesAsync();
            await queryHost.BlockedRequestStarted.Task.WaitAsync(TimeSpan.FromSeconds(5));

            SetPrivateField(coordinator, "currentVrChatAvatarId", replacementAvatarId);
            queryHost.Release();
            await resumeTask.WaitAsync(TimeSpan.FromSeconds(5));

            Assert.Empty(sentPackets);
        }
        finally
        {
            queryHost.Release();
            await coordinator.StopAsync().WaitAsync(TimeSpan.FromSeconds(5));
            var routerDisposeTask = router.DisposeAsync().AsTask();
            await routerDisposeTask.WaitAsync(TimeSpan.FromSeconds(5));
        }
    }

    [Fact]
    public async Task ActivityResumeScale_DoesNotSendSavedHeightAfterAvatarChangesDuringInnerHeightRead()
    {
        const string sourceAvatarId = "avatar-source";
        const string replacementAvatarId = "avatar-replacement";
        var ruleId = Guid.NewGuid();
        var activityResume = new PendingAvatarScaleResumeService(new ResumeActivity
        {
            Type = ResumeActivityType.AvatarScale,
            RuleId = ruleId,
            ExpiresAt = DateTimeOffset.UtcNow.AddMinutes(1),
            CurrentValue = 1.25
        });
        var coordinator = CreateCoordinator(activityResume);
        using var queryHost = new BlockingAvatarHeightQueryHost(
            1.6f,
            blockedRequestNumber: 2,
            firstHeightResponseInvalid: true);
        var sentPackets = new ConcurrentQueue<byte[]>();
        var logs = new ConcurrentQueue<string>();
        coordinator.LogWritten += logs.Enqueue;
        var router = GetPrivateField<OscRouterService>(coordinator, "oscRouterService");
        SetPrivateField(
            coordinator,
            "testSendToVrChatAsync",
            (Func<byte[], CancellationToken, Task>)((packet, _) =>
            {
                sentPackets.Enqueue(packet);
                return Task.CompletedTask;
            }));
        SetPrivateField(coordinator, "testStopOscRouterAsync", (Func<Task>)(() => Task.CompletedTask));
        SetPrivateField(coordinator, "testForceReleaseDesktopInputAsync", (Func<CancellationToken, Task>)(_ => Task.CompletedTask));

        try
        {
            ConfigurePendingAvatarScaleResume(coordinator, ruleId, sourceAvatarId);
            await router.StartAsync([]).WaitAsync(TimeSpan.FromSeconds(5));
            SetPrivateField(
                router,
                "activeVrChatTarget",
                new OscRouterService.DiscoveredOscTarget(
                    "Activity resume inner-read test VRChat",
                    IPAddress.Loopback,
                    9010,
                    queryHost.Port));

            var resumeTask = coordinator.TryResumePendingActivitiesAsync();
            try
            {
                await queryHost.BlockedRequestStarted.Task.WaitAsync(TimeSpan.FromSeconds(5));
            }
            catch (TimeoutException)
            {
                throw new Xunit.Sdk.XunitException(
                    $"Expected inner height read, observed {queryHost.RequestCount} OSCQuery request(s); "
                    + $"resumeCompleted={resumeTask.IsCompleted}; logs={string.Join(" | ", logs)}.");
            }

            SetPrivateField(coordinator, "currentVrChatAvatarId", replacementAvatarId);
            queryHost.Release();
            await resumeTask.WaitAsync(TimeSpan.FromSeconds(5));

            Assert.Empty(sentPackets);
        }
        finally
        {
            queryHost.Release();
            await coordinator.StopAsync().WaitAsync(TimeSpan.FromSeconds(5));
            await router.DisposeAsync().AsTask().WaitAsync(TimeSpan.FromSeconds(5));
        }
    }

    [Fact]
    public async Task ActivityResumeScale_MismatchDuringCleanupRemainsEligibleForRetry()
    {
        const string sourceAvatarId = "avatar-source";
        const string replacementAvatarId = "avatar-replacement";
        var ruleId = Guid.NewGuid();
        var activityResume = new BlockingPendingAvatarScaleResumeService(new ResumeActivity
        {
            Type = ResumeActivityType.AvatarScale,
            RuleId = ruleId,
            ExpiresAt = DateTimeOffset.UtcNow.AddMinutes(1),
            CurrentValue = 1.25
        });
        var coordinator = CreateCoordinator(activityResume);
        using var queryHost = new BlockingAvatarHeightQueryHost(1.6f);
        var sentPackets = new ConcurrentQueue<byte[]>();
        var router = GetPrivateField<OscRouterService>(coordinator, "oscRouterService");
        SetPrivateField(
            coordinator,
            "testSendToVrChatAsync",
            (Func<byte[], CancellationToken, Task>)((packet, _) =>
            {
                sentPackets.Enqueue(packet);
                return Task.CompletedTask;
            }));
        SetPrivateField(coordinator, "testStopOscRouterAsync", (Func<Task>)(() => Task.CompletedTask));
        SetPrivateField(coordinator, "testForceReleaseDesktopInputAsync", (Func<CancellationToken, Task>)(_ => Task.CompletedTask));

        try
        {
            ConfigurePendingAvatarScaleResume(coordinator, ruleId, sourceAvatarId);
            await router.StartAsync([]).WaitAsync(TimeSpan.FromSeconds(5));
            SetPrivateField(
                router,
                "activeVrChatTarget",
                new OscRouterService.DiscoveredOscTarget(
                    "Activity resume cleanup test VRChat",
                    IPAddress.Loopback,
                    9010,
                    queryHost.Port));

            var firstResumeTask = coordinator.TryResumePendingActivitiesAsync();
            await activityResume.CleanupStarted.Task.WaitAsync(TimeSpan.FromSeconds(5));

            SetPrivateField(coordinator, "currentVrChatAvatarId", replacementAvatarId);
            activityResume.ReleaseCleanup();
            await firstResumeTask.WaitAsync(TimeSpan.FromSeconds(5));

            Assert.False(GetPrivateField<bool>(coordinator, "hasAttemptedResume"));
            Assert.Empty(sentPackets);

            SetPrivateField(coordinator, "currentVrChatAvatarId", sourceAvatarId);
            var retryTask = coordinator.TryResumePendingActivitiesAsync();
            await queryHost.RequestStarted.Task.WaitAsync(TimeSpan.FromSeconds(5));
            queryHost.Release();
            await retryTask.WaitAsync(TimeSpan.FromSeconds(5));

            Assert.True(GetPrivateField<bool>(coordinator, "hasAttemptedResume"));
            Assert.NotEmpty(sentPackets);
        }
        finally
        {
            activityResume.ReleaseCleanup();
            queryHost.Release();
            await coordinator.StopAsync().WaitAsync(TimeSpan.FromSeconds(5));
            await router.DisposeAsync().AsTask().WaitAsync(TimeSpan.FromSeconds(5));
        }
    }

    [Fact]
    public async Task ActivityResumeScale_MismatchDuringPerActivityCleanupRemainsEligibleForRetry()
    {
        const string sourceAvatarId = "avatar-source";
        const string replacementAvatarId = "avatar-replacement";
        var activityResume = new BlockingPendingActivityCleanupResumeService(new ResumeActivity
        {
            Type = ResumeActivityType.AvatarScale,
            RuleId = Guid.NewGuid(),
            ExpiresAt = DateTimeOffset.UtcNow.AddMinutes(-1),
            CurrentValue = 1.25
        });
        var coordinator = CreateCoordinator(activityResume);
        using var queryHost = new BlockingAvatarHeightQueryHost(1.6f);
        var router = GetPrivateField<OscRouterService>(coordinator, "oscRouterService");
        SetPrivateField(coordinator, "testStopOscRouterAsync", (Func<Task>)(() => Task.CompletedTask));
        SetPrivateField(coordinator, "testForceReleaseDesktopInputAsync", (Func<CancellationToken, Task>)(_ => Task.CompletedTask));

        try
        {
            ConfigurePendingAvatarScaleResume(
                coordinator,
                activityResume.Activity.RuleId,
                sourceAvatarId);
            await router.StartAsync([]).WaitAsync(TimeSpan.FromSeconds(5));
            SetPrivateField(
                router,
                "activeVrChatTarget",
                new OscRouterService.DiscoveredOscTarget(
                    "Activity resume per-activity cleanup test VRChat",
                    IPAddress.Loopback,
                    9010,
                    queryHost.Port));

            var firstResumeTask = coordinator.TryResumePendingActivitiesAsync();
            await activityResume.ActivityCleanupStarted.Task.WaitAsync(TimeSpan.FromSeconds(5));

            SetPrivateField(coordinator, "currentVrChatAvatarId", replacementAvatarId);
            activityResume.ReleaseActivityCleanup();
            await firstResumeTask.WaitAsync(TimeSpan.FromSeconds(5));

            Assert.False(GetPrivateField<bool>(coordinator, "hasAttemptedResume"));
            Assert.Equal(1, activityResume.RemoveActivityCallCount);

            SetPrivateField(coordinator, "currentVrChatAvatarId", sourceAvatarId);
            var retryTask = coordinator.TryResumePendingActivitiesAsync();
            await retryTask.WaitAsync(TimeSpan.FromSeconds(5));

            Assert.True(GetPrivateField<bool>(coordinator, "hasAttemptedResume"));
            Assert.Equal(2, activityResume.RemoveActivityCallCount);
        }
        finally
        {
            activityResume.ReleaseActivityCleanup();
            await coordinator.StopAsync().WaitAsync(TimeSpan.FromSeconds(5));
            await router.DisposeAsync().AsTask().WaitAsync(TimeSpan.FromSeconds(5));
        }
    }

    [Fact]
    public async Task SetCurrentVrChatAvatar_ReentrantNotificationDoesNotDeadlock()
    {
        var coordinator = CreateCoordinator();
        var sendGate = GetPrivateField<SemaphoreSlim>(coordinator, "avatarScopedResetSendGate");
        SetPrivateField(coordinator, "testStopOscRouterAsync", (Func<Task>)(() => Task.CompletedTask));
        SetPrivateField(coordinator, "testForceReleaseDesktopInputAsync", (Func<CancellationToken, Task>)(_ => Task.CompletedTask));
        var avatarChangeMode = Enum.Parse(
            typeof(BridgeCoordinator).GetNestedType(
                "AvatarScaleAvatarChangeCarryoverMode",
                BindingFlags.NonPublic)!,
            "Auto");
        var callbackCount = 0;
        var recoveryNeeded = 0;
        var nestedUpdateCompleted = NewSignal();
        var allowCallbackReturn = NewSignal();
        coordinator.VrChatAvatarChanged += _ =>
        {
            if (Interlocked.Increment(ref callbackCount) == 1)
            {
                coordinator.UpdateCurrentVrChatAvatar("avatar-from-callback");
                nestedUpdateCompleted.TrySetResult();
                if (Volatile.Read(ref recoveryNeeded) == 1)
                {
                    allowCallbackReturn.Task.GetAwaiter().GetResult();
                }
            }
        };

        var mutationTask = Task.Run(() =>
            InvokePrivate(
                coordinator,
                "SetCurrentVrChatAvatar",
                "avatar-notified",
                true,
                avatarChangeMode));

        try
        {
            await mutationTask.WaitAsync(TimeSpan.FromSeconds(1));

            Assert.Equal(1, Volatile.Read(ref callbackCount));
            Assert.Equal("avatar-from-callback", coordinator.CurrentVrChatAvatarId);
        }
        finally
        {
            if (!mutationTask.IsCompleted)
            {
                // The pre-fix implementation owns the gate across the callback; release it after the bounded assertion so cleanup can finish.
                Volatile.Write(ref recoveryNeeded, 1);
                try
                {
                    sendGate.Release();
                }
                catch (SemaphoreFullException)
                {
                }

                await nestedUpdateCompleted.Task.WaitAsync(TimeSpan.FromSeconds(5));
                Assert.True(sendGate.Wait(0));
                allowCallbackReturn.TrySetResult();
            }

            await mutationTask.WaitAsync(TimeSpan.FromSeconds(5));
            await coordinator.StopAsync().WaitAsync(TimeSpan.FromSeconds(5));
        }
    }

    [Fact]
    public async Task ScheduleReset_AvatarScopedSendSerializesConcurrentAvatarMutation()
    {
        var coordinator = CreateCoordinator();
        var sourceAvatarId = "avatar-source";
        var rule = CreateRule("scheduled avatar reset") with
        {
            AvatarProfileId = Guid.NewGuid()
        };
        var action = CreateResolvedAction();
        SetPrivateField(coordinator, "runtimeCancellation", new CancellationTokenSource());
        SetPrivateField(coordinator, "currentVrChatAvatarId", sourceAvatarId);

        var sendStarted = NewSignal();
        var releaseSend = NewSignal();
        var avatarChangeAttemptStarted = NewSignal();
        var sendCount = 0;
        Task? avatarChangeTask = null;
        SetPrivateField(
            coordinator,
            "testSendToVrChatAsync",
            (Func<byte[], CancellationToken, Task>)(async (_, _) =>
            {
                Interlocked.Increment(ref sendCount);
                sendStarted.TrySetResult();
                avatarChangeTask = Task.Run(() =>
                {
                    avatarChangeAttemptStarted.TrySetResult();
                    coordinator.UpdateCurrentVrChatAvatar("avatar-new");
                });
                await releaseSend.Task;
            }));
        SetPrivateField(coordinator, "testStopOscRouterAsync", (Func<Task>)(() => Task.CompletedTask));
        SetPrivateField(coordinator, "testForceReleaseDesktopInputAsync", (Func<CancellationToken, Task>)(_ => Task.CompletedTask));

        try
        {
            InvokePrivate(
                coordinator,
                "ScheduleReset",
                rule,
                action,
                0d,
                null,
                Guid.Empty,
                true,
                false,
                true,
                null);
            await sendStarted.Task.WaitAsync(TimeSpan.FromSeconds(5));
            await avatarChangeAttemptStarted.Task.WaitAsync(TimeSpan.FromSeconds(5));

            Assert.NotNull(avatarChangeTask);
            Assert.False(
                avatarChangeTask.IsCompleted,
                "A current-avatar mutation must wait for a normal avatar-scoped reset send.");
            Assert.Equal(sourceAvatarId, coordinator.CurrentVrChatAvatarId);

            releaseSend.TrySetResult();
            await avatarChangeTask.WaitAsync(TimeSpan.FromSeconds(5));
            await WaitForMovementCleanupAsync(coordinator);

            Assert.Equal(1, Volatile.Read(ref sendCount));
            Assert.Equal("avatar-new", coordinator.CurrentVrChatAvatarId);
        }
        finally
        {
            releaseSend.TrySetResult();
            if (avatarChangeTask is not null)
            {
                await avatarChangeTask.WaitAsync(TimeSpan.FromSeconds(5));
            }

            await coordinator.StopAsync().WaitAsync(TimeSpan.FromSeconds(5));
        }
    }

    [Fact]
    public async Task ScheduleReset_ReplacementWaitsForInFlightAvatarScopedSendBeforeTransferringOwnership()
    {
        var coordinator = CreateCoordinator();
        var runtimeCancellation = new CancellationTokenSource();
        var rule = CreateRule("in-flight replacement avatar reset") with
        {
            AvatarProfileId = Guid.NewGuid()
        };
        var action = CreateResolvedAction();
        var laneKey = "cross-review-in-flight-lane";
        var replacementLaneKey = "cross-review-in-flight-replacement-lane";
        var oldLaneLeaseId = Guid.NewGuid();
        var replacementLaneLeaseId = Guid.NewGuid();
        var pendingResets = GetPendingResets(coordinator);
        var actionLanes = Assert.IsAssignableFrom<IDictionary>(GetPrivateField(coordinator, "actionLanes"));
        var oldSendStarted = NewSignal();
        var releaseOldSend = NewSignal();
        var replacementEnteredGateWait = NewSignal();
        SetPrivateField(coordinator, "runtimeCancellation", runtimeCancellation);
        SetPrivateField(coordinator, "currentVrChatAvatarId", "avatar-source");
        actionLanes[laneKey] = CreateActiveMovementLaneState(
            oldLaneLeaseId,
            DateTimeOffset.UtcNow.AddMinutes(1),
            rule.Id);
        SetPrivateField(
            coordinator,
            "testSendToVrChatAsync",
            (Func<byte[], CancellationToken, Task>)(async (_, _) =>
            {
                oldSendStarted.TrySetResult();
                await releaseOldSend.Task;
            }));
        SetPrivateField(coordinator, "testStopOscRouterAsync", (Func<Task>)(() => Task.CompletedTask));
        SetPrivateField(coordinator, "testForceReleaseDesktopInputAsync", (Func<CancellationToken, Task>)(_ => Task.CompletedTask));

        Task? replacementScheduleTask = null;
        try
        {
            InvokePrivate(
                coordinator,
                "ScheduleReset",
                rule,
                action,
                0d,
                new[] { laneKey },
                oldLaneLeaseId,
                true,
                false,
                true,
                null);
            await oldSendStarted.Task.WaitAsync(TimeSpan.FromSeconds(5));

            var oldPendingReset = pendingResets[rule.Id]!;
            var oldCancellation = (CancellationTokenSource)oldPendingReset
                .GetType()
                .GetProperty("Cancellation")!
                .GetValue(oldPendingReset)!;
            actionLanes[replacementLaneKey] = CreateActiveMovementLaneState(
                replacementLaneLeaseId,
                DateTimeOffset.UtcNow.AddMinutes(1),
                rule.Id);
            SetPrivateField(
                coordinator,
                "testAvatarScopedResetReplacementGateWait",
                replacementEnteredGateWait);

            replacementScheduleTask = Task.Run(() => InvokePrivate(
                coordinator,
                "ScheduleReset",
                rule,
                action,
                60d,
                new[] { replacementLaneKey },
                replacementLaneLeaseId,
                true,
                false,
                false,
                null));

            await replacementEnteredGateWait.Task.WaitAsync(TimeSpan.FromSeconds(5));
            Assert.False(replacementScheduleTask.IsCompleted);
            Assert.Same(oldPendingReset, pendingResets[rule.Id]);
            Assert.Equal("avatar-source", coordinator.CurrentVrChatAvatarId);
            Assert.True(actionLanes.Contains(laneKey));

            releaseOldSend.TrySetResult();
            await replacementScheduleTask.WaitAsync(TimeSpan.FromSeconds(5));

            var replacementPendingReset = pendingResets[rule.Id]!;
            var replacementCancellation = (CancellationTokenSource)replacementPendingReset
                .GetType()
                .GetProperty("Cancellation")!
                .GetValue(replacementPendingReset)!;
            Assert.NotSame(oldPendingReset, replacementPendingReset);
            Assert.Same(replacementPendingReset, pendingResets[rule.Id]);
            Assert.Throws<ObjectDisposedException>(() => _ = oldCancellation.Token);
            Assert.False(replacementCancellation.IsCancellationRequested);
            Assert.False(actionLanes.Contains(laneKey));
            Assert.True(actionLanes.Contains(replacementLaneKey));
        }
        finally
        {
            releaseOldSend.TrySetResult();
            if (replacementScheduleTask is not null)
            {
                await replacementScheduleTask.WaitAsync(TimeSpan.FromSeconds(5));
            }

            runtimeCancellation.Cancel();
            await coordinator.StopAsync().WaitAsync(TimeSpan.FromSeconds(5));
        }
    }

    [Fact]
    public async Task PauseActiveMovementTimerForDev_DisposesRemovedPendingResetCancellation()
    {
        var coordinator = CreateCoordinator();
        var runtimeCancellation = new CancellationTokenSource();
        var rule = CreateRule("paused movement reset") with
        {
            ActionType = OscActionType.PlayerMovement,
            MovementDirection = PlayerMovementDirection.Forward,
            DurationSeconds = 60
        };
        var action = CreateResolvedAction();
        var laneKey = "player-movement-vertical";
        var laneLeaseId = Guid.NewGuid();
        var actionLanes = Assert.IsAssignableFrom<IDictionary>(GetPrivateField(coordinator, "actionLanes"));
        var pendingResets = GetPendingResets(coordinator);
        SetPrivateField(coordinator, "runtimeCancellation", runtimeCancellation);
        actionLanes[laneKey] = CreateActiveMovementLaneState(
            laneLeaseId,
            DateTimeOffset.UtcNow.AddMinutes(1),
            rule.Id);

        try
        {
            InvokePrivate(
                coordinator,
                "ScheduleReset",
                rule,
                action,
                60d,
                new[] { laneKey },
                laneLeaseId,
                true,
                false,
                false,
                null);
            await WaitUntilAsync(() => pendingResets.Contains(rule.Id));

            var pendingReset = pendingResets[rule.Id]!;
            var cancellation = (CancellationTokenSource)pendingReset
                .GetType()
                .GetProperty("Cancellation")!
                .GetValue(pendingReset)!;
            var pausedTimer = InvokePrivate(
                coordinator,
                "PauseActiveMovementTimerForDev",
                laneKey,
                TimeSpan.FromSeconds(1),
                null);

            Assert.NotNull(pausedTimer);
            await WaitForMovementCleanupAsync(coordinator);

            Assert.Throws<ObjectDisposedException>(() => _ = cancellation.Token);
            Assert.False(pendingResets.Contains(rule.Id));
            Assert.False(actionLanes.Contains(laneKey));
        }
        finally
        {
            runtimeCancellation.Cancel();
            await coordinator.StopAsync().WaitAsync(TimeSpan.FromSeconds(5));
        }
    }

    [Fact]
    public async Task AvatarScopedResetSendDisposal_RejectsNewUsersWhileExistingUserDrains()
    {
        var coordinator = CreateCoordinator();
        var drainTask = Task.CompletedTask;
        var disposed = false;

        try
        {
            InvokePrivate(coordinator, "EnterAvatarScopedResetSendUser");
            InvokePrivate(coordinator, "MarkAvatarScopedResetSendDisposalStarted");

            drainTask = Assert.IsAssignableFrom<Task>(InvokePrivate(
                coordinator,
                "WaitForAvatarScopedResetSendUsersAsync"));
            Assert.False(drainTask.IsCompleted);

            var admissionException = Assert.Throws<TargetInvocationException>(() =>
                InvokePrivate(coordinator, "EnterAvatarScopedResetSendUser"));
            Assert.IsType<ObjectDisposedException>(admissionException.InnerException);

            InvokePrivate(coordinator, "ExitAvatarScopedResetSendUser");
            await drainTask.WaitAsync(TimeSpan.FromSeconds(5));

            var sendGate = GetPrivateField<SemaphoreSlim>(coordinator, "avatarScopedResetSendGate");
            await coordinator.DisposeAsync();
            disposed = true;
            Assert.Throws<ObjectDisposedException>(() => sendGate.Wait(0));
        }
        finally
        {
            if (!disposed)
            {
                while (GetPrivateField<int>(coordinator, "avatarScopedResetSendUserCount") > 0)
                {
                    InvokePrivate(coordinator, "ExitAvatarScopedResetSendUser");
                }

                await drainTask.WaitAsync(TimeSpan.FromSeconds(5));
                await coordinator.DisposeAsync();
            }
        }
    }

    [Fact]
    public async Task ResetPendingRulesCore_SkipsAvatarScopedPacketWhenCurrentAvatarDoesNotMatch()
    {
        var coordinator = CreateCoordinator();
        var rule = CreateRule("shutdown avatar reset") with
        {
            AvatarProfileId = Guid.NewGuid()
        };
        var action = CreateResolvedAction();
        var cancellation = new CancellationTokenSource();
        var pendingReset = CreatePendingResetState(
            rule,
            action,
            cancellation,
            DateTimeOffset.UtcNow.AddMilliseconds(-1),
            new ReschedulableActivityDeadline(DateTimeOffset.UtcNow.AddMilliseconds(-1)),
            "avatar-source",
            isWaitingForSourceAvatarReturn: false,
            new[] { new byte[] { 2 } });
        GetPendingResets(coordinator).Add(rule.Id, pendingReset);
        SetPrivateField(coordinator, "currentVrChatAvatarId", "avatar-other");

        var sendCount = 0;
        SetPrivateField(
            coordinator,
            "testSendToVrChatAsync",
            (Func<byte[], CancellationToken, Task>)((_, _) =>
            {
                Interlocked.Increment(ref sendCount);
                return Task.CompletedTask;
            }));

        try
        {
            var resetTask = Assert.IsAssignableFrom<Task>(InvokePrivate(
                coordinator,
                "ResetPendingRulesCoreAsync"));
            await resetTask.WaitAsync(TimeSpan.FromSeconds(5));

            Assert.Equal(0, Volatile.Read(ref sendCount));
        }
        finally
        {
            await coordinator.StopAsync().WaitAsync(TimeSpan.FromSeconds(5));
        }
    }

    [Fact]
    public async Task PendingAvatarScopedResetResume_DoesNotAllowAvatarChangeToOvertakeClaimedSend()
    {
        var coordinator = CreateCoordinator();
        var sourceAvatarId = "avatar-source";
        var pendingCancellation = new CancellationTokenSource();
        var rule = CreateRule("claimed avatar reset");
        var action = CreateResolvedAction();
        var pendingReset = CreatePendingResetState(
            rule,
            action,
            pendingCancellation,
            DateTimeOffset.UtcNow.AddMilliseconds(-1),
            new ReschedulableActivityDeadline(DateTimeOffset.UtcNow.AddMilliseconds(-1)),
            sourceAvatarId,
            isWaitingForSourceAvatarReturn: true,
            new[] { new byte[] { 2 } });
        var pendingResets = GetPendingResets(coordinator);
        pendingResets.Add(rule.Id, pendingReset);
        SetPrivateField(coordinator, "currentVrChatAvatarId", sourceAvatarId);

        var sendStarted = NewSignal();
        var releaseSend = NewSignal();
        var avatarChangeAttemptStarted = NewSignal();
        var sendCount = 0;
        Task? avatarChangeTask = null;
        SetPrivateField(
            coordinator,
            "testSendToVrChatAsync",
            (Func<byte[], CancellationToken, Task>)(async (_, _) =>
            {
                Interlocked.Increment(ref sendCount);
                sendStarted.TrySetResult();
                avatarChangeTask = Task.Run(() =>
                {
                    avatarChangeAttemptStarted.TrySetResult();
                    coordinator.UpdateCurrentVrChatAvatar("avatar-new");
                });
                await releaseSend.Task;
            }));
        SetPrivateField(coordinator, "testStopOscRouterAsync", (Func<Task>)(() => Task.CompletedTask));
        SetPrivateField(coordinator, "testForceReleaseDesktopInputAsync", (Func<CancellationToken, Task>)(_ => Task.CompletedTask));

        try
        {
            InvokePrivate(coordinator, "ResumePendingAvatarScopedResetsForCurrentAvatar", sourceAvatarId);
            await sendStarted.Task.WaitAsync(TimeSpan.FromSeconds(5));
            await avatarChangeAttemptStarted.Task.WaitAsync(TimeSpan.FromSeconds(5));

            Assert.NotNull(avatarChangeTask);
            Assert.False(
                avatarChangeTask.IsCompleted,
                "A current-avatar mutation must wait for the claimed avatar-scoped reset send.");
            Assert.Equal(sourceAvatarId, coordinator.CurrentVrChatAvatarId);

            releaseSend.TrySetResult();
            await avatarChangeTask.WaitAsync(TimeSpan.FromSeconds(5));
            await WaitForMovementCleanupAsync(coordinator);

            Assert.Equal(1, Volatile.Read(ref sendCount));
            Assert.Equal("avatar-new", coordinator.CurrentVrChatAvatarId);
        }
        finally
        {
            releaseSend.TrySetResult();
            if (avatarChangeTask is not null)
            {
                await avatarChangeTask.WaitAsync(TimeSpan.FromSeconds(5));
            }

            await coordinator.StopAsync().WaitAsync(TimeSpan.FromSeconds(5));
        }
    }

    [Fact]
    public async Task PendingAvatarScopedResetResume_RepublishesClaimWhenAvatarMismatchesImmediatelyBeforeSend()
    {
        var coordinator = CreateCoordinator();
        var sourceAvatarId = "avatar-source";
        var pendingCancellation = new CancellationTokenSource();
        var rule = CreateRule("mismatched avatar reset");
        var action = CreateResolvedAction();
        var pendingReset = CreatePendingResetState(
            rule,
            action,
            pendingCancellation,
            DateTimeOffset.UtcNow.AddMilliseconds(-1),
            new ReschedulableActivityDeadline(DateTimeOffset.UtcNow.AddMilliseconds(-1)),
            sourceAvatarId,
            isWaitingForSourceAvatarReturn: true,
            new[] { new byte[] { 2 } });
        var pendingResets = GetPendingResets(coordinator);
        pendingResets.Add(rule.Id, pendingReset);
        SetPrivateField(coordinator, "currentVrChatAvatarId", sourceAvatarId);

        var sendCount = 0;
        SetPrivateField(
            coordinator,
            "testSendToVrChatAsync",
            (Func<byte[], CancellationToken, Task>)((_, _) =>
            {
                Interlocked.Increment(ref sendCount);
                return Task.CompletedTask;
            }));

        var sendGate = GetPrivateField<SemaphoreSlim>(coordinator, "avatarScopedResetSendGate");
        Assert.True(sendGate.Wait(0));
        var sendGateHeld = true;
        SetPrivateField(coordinator, "testStopOscRouterAsync", (Func<Task>)(() => Task.CompletedTask));
        SetPrivateField(coordinator, "testForceReleaseDesktopInputAsync", (Func<CancellationToken, Task>)(_ => Task.CompletedTask));
        try
        {
            InvokePrivate(coordinator, "ResumePendingAvatarScopedResetsForCurrentAvatar", sourceAvatarId);
            await WaitUntilAsync(() => !pendingResets.Contains(rule.Id));

            SetPrivateField(coordinator, "currentVrChatAvatarId", "avatar-other");
            sendGate.Release();
            sendGateHeld = false;

            await WaitForMovementCleanupAsync(coordinator);

            Assert.Equal(0, Volatile.Read(ref sendCount));
            var republishedReset = pendingResets[rule.Id];
            Assert.NotNull(republishedReset);
            Assert.True(
                (bool)republishedReset.GetType().GetProperty("IsWaitingForSourceAvatarReturn")!.GetValue(republishedReset)!);
        }
        finally
        {
            if (sendGateHeld)
            {
                sendGate.Release();
            }

            await coordinator.StopAsync().WaitAsync(TimeSpan.FromSeconds(5));
        }
    }

    [Fact]
    public async Task ScheduleReset_ReplacementDisposesRequeuedCancellationAndReleasesOldLane()
    {
        var coordinator = CreateCoordinator();
        var runtimeCancellation = new CancellationTokenSource();
        var rule = CreateRule("replaced avatar reset") with
        {
            AvatarProfileId = Guid.NewGuid()
        };
        var action = CreateResolvedAction();
        var laneKey = "cross-review-lane";
        var replacementLaneKey = "cross-review-replacement-lane";
        var oldLaneLeaseId = Guid.NewGuid();
        var replacementLaneLeaseId = Guid.NewGuid();
        var pendingResets = GetPendingResets(coordinator);
        var actionLanes = Assert.IsAssignableFrom<IDictionary>(GetPrivateField(coordinator, "actionLanes"));
        SetPrivateField(coordinator, "runtimeCancellation", runtimeCancellation);
        SetPrivateField(coordinator, "currentVrChatAvatarId", "avatar-source");
        actionLanes[laneKey] = CreateActiveMovementLaneState(
            oldLaneLeaseId,
            DateTimeOffset.UtcNow.AddMinutes(1),
            rule.Id);
        SetPrivateField(
            coordinator,
            "testSendToVrChatAsync",
            (Func<byte[], CancellationToken, Task>)((_, _) => Task.CompletedTask));
        SetPrivateField(coordinator, "testStopOscRouterAsync", (Func<Task>)(() => Task.CompletedTask));
        SetPrivateField(coordinator, "testForceReleaseDesktopInputAsync", (Func<CancellationToken, Task>)(_ => Task.CompletedTask));

        try
        {
            InvokePrivate(
                coordinator,
                "ScheduleReset",
                rule,
                action,
                0d,
                new[] { laneKey },
                oldLaneLeaseId,
                true,
                false,
                false,
                null);
            SetPrivateField(coordinator, "currentVrChatAvatarId", "avatar-other");

            await WaitUntilAsync(() =>
            {
                if (!pendingResets.Contains(rule.Id))
                {
                    return false;
                }

                var current = pendingResets[rule.Id]!;
                return (bool)current.GetType().GetProperty("IsWaitingForSourceAvatarReturn")!.GetValue(current)!;
            });

            var oldPendingReset = pendingResets[rule.Id]!;
            var oldCancellation = (CancellationTokenSource)oldPendingReset
                .GetType()
                .GetProperty("Cancellation")!
                .GetValue(oldPendingReset)!;
            actionLanes[replacementLaneKey] = CreateActiveMovementLaneState(
                replacementLaneLeaseId,
                DateTimeOffset.UtcNow.AddMinutes(1),
                rule.Id);

            InvokePrivate(
                coordinator,
                "ScheduleReset",
                rule,
                action,
                60d,
                new[] { replacementLaneKey },
                replacementLaneLeaseId,
                true,
                false,
                false,
                null);

            var replacementPendingReset = pendingResets[rule.Id]!;
            var replacementCancellation = (CancellationTokenSource)replacementPendingReset
                .GetType()
                .GetProperty("Cancellation")!
                .GetValue(replacementPendingReset)!;

            Assert.NotSame(oldPendingReset, replacementPendingReset);
            Assert.Same(replacementPendingReset, pendingResets[rule.Id]);
            Assert.Throws<ObjectDisposedException>(() => _ = oldCancellation.Token);
            Assert.False(replacementCancellation.IsCancellationRequested);
            _ = replacementCancellation.Token;
            Assert.False(actionLanes.Contains(laneKey));
            Assert.True(actionLanes.Contains(replacementLaneKey));
            var replacementLane = actionLanes[replacementLaneKey]!;
            Assert.Equal(
                replacementLaneLeaseId,
                (Guid)replacementLane.GetType().GetProperty("OwnerId")!.GetValue(replacementLane)!);
        }
        finally
        {
            runtimeCancellation.Cancel();
            await coordinator.StopAsync().WaitAsync(TimeSpan.FromSeconds(5));
        }
    }

    [Fact]
    public async Task CancelStalePendingResets_DoesNotRemoveReplacementInstalledDuringCancellation()
    {
        var coordinator = CreateCoordinator();
        var rule = CreateRule("stale replacement reset");
        var action = CreateResolvedAction();
        var oldCancellation = new CancellationTokenSource();
        var replacementCancellation = new CancellationTokenSource();
        var oldPendingReset = CreatePendingResetState(
            rule,
            action,
            oldCancellation,
            DateTimeOffset.UtcNow.AddMinutes(1),
            new ReschedulableActivityDeadline(DateTimeOffset.UtcNow.AddMinutes(1)),
            "avatar-old",
            isWaitingForSourceAvatarReturn: true,
            Array.Empty<byte[]>());
        var replacementPendingReset = CreatePendingResetState(
            rule,
            action,
            replacementCancellation,
            DateTimeOffset.UtcNow.AddMinutes(1),
            new ReschedulableActivityDeadline(DateTimeOffset.UtcNow.AddMinutes(1)),
            "avatar-new",
            isWaitingForSourceAvatarReturn: true,
            Array.Empty<byte[]>());
        var pendingResets = GetPendingResets(coordinator);
        pendingResets.Add(rule.Id, oldPendingReset);
        using var replacementDuringCancel = oldCancellation.Token.Register(
            () => pendingResets[rule.Id] = replacementPendingReset);

        try
        {
            InvokePrivate(coordinator, "CancelStalePendingResets", "avatar-new");

            Assert.Same(replacementPendingReset, pendingResets[rule.Id]);
        }
        finally
        {
            await coordinator.DisposeAsync();
        }
    }

    [Fact]
    public void TimedSupporterOverrideCompletion_UsesPersistentWorkerAdmissionAndPreservesStateAfterCancelledReset()
    {
        var source = File.ReadAllText(FindSourceFile(
            "VrcTwitchOscBridge",
            "Services",
            "BridgeCoordinator.cs"));
        var scheduleBody = NormalizeWhitespace(GetMethodBody(
            source,
            "private void ScheduleTimedSupporterOverrideCompletion("));
        var graceBody = NormalizeWhitespace(GetMethodBody(
            source,
            "private void ScheduleTimedSupporterOverrideCompletionAfterGracePeriod("));
        var completionBody = NormalizeWhitespace(GetMethodBody(
            source,
            "private async Task CompleteTimedSupporterOverrideCoreAsync("));

        Assert.Contains("TryStartPersistentEffectWorker", scheduleBody, StringComparison.Ordinal);
        Assert.Contains("completionCancellation", scheduleBody, StringComparison.Ordinal);
        Assert.Contains("TryStartPersistentEffectWorker", graceBody, StringComparison.Ordinal);
        Assert.Contains("completionCancellation", graceBody, StringComparison.Ordinal);
        Assert.Contains("cancellationToken.IsCancellationRequested", completionBody, StringComparison.Ordinal);
        Assert.Contains("activeSupporterOverride = null", completionBody, StringComparison.Ordinal);
        Assert.Contains("StartTimedSupporterOverrideAsync", completionBody, StringComparison.Ordinal);
    }

    [Fact]
    public async Task StopAsync_DrainsCancelledTimedSupporterCompletionBeforeFinalResetAndRouterClose()
    {
        var coordinator = CreateCoordinator();
        var events = new ConcurrentQueue<string>();
        var sendStarted = NewSignal();
        var cancellationSeen = NewSignal();
        var releaseFirstSend = NewSignal();
        var sendCount = 0;
        var runtimeCancellation = new CancellationTokenSource();
        SetPrivateField(coordinator, "runtimeCancellation", runtimeCancellation);

        var rule = CreateRule("shutdown supporter override");
        var action = CreateResolvedAction();
        var completionCancellation = CancellationTokenSource.CreateLinkedTokenSource(runtimeCancellation.Token);
        var activeState = CreateActiveSupporterOverrideState(
            rule,
            action,
            completionCancellation,
            DateTimeOffset.UtcNow.AddMilliseconds(-1));
        SetPrivateField(coordinator, "activeSupporterOverride", activeState);

        SetPrivateField(
            coordinator,
            "testSendToVrChatAsync",
            (Func<byte[], CancellationToken, Task>)(async (_, cancellationToken) =>
            {
                var call = Interlocked.Increment(ref sendCount);
                events.Enqueue($"send:{call}");
                if (call == 1)
                {
                    sendStarted.TrySetResult();
                    using var registration = cancellationToken.Register(() => cancellationSeen.TrySetResult());
                    await releaseFirstSend.Task;
                    cancellationToken.ThrowIfCancellationRequested();
                }
            }));
        SetPrivateField(
            coordinator,
            "testStopOscRouterAsync",
            (Func<Task>)(() =>
            {
                events.Enqueue("router-close");
                return Task.CompletedTask;
            }));
        SetPrivateField(
            coordinator,
            "testForceReleaseDesktopInputAsync",
            (Func<CancellationToken, Task>)(_ => Task.CompletedTask));

        Task? stopTask = null;
        try
        {
            InvokePrivate(
                coordinator,
                "ScheduleTimedSupporterOverrideCompletion",
                activeState,
                completionCancellation);
            await sendStarted.Task.WaitAsync(TimeSpan.FromSeconds(5));

            stopTask = coordinator.StopAsync();
            await cancellationSeen.Task.WaitAsync(TimeSpan.FromSeconds(5));
            var completedBeforeRelease = await Task.WhenAny(
                stopTask,
                Task.Delay(TimeSpan.FromMilliseconds(100)));
            Assert.False(
                ReferenceEquals(stopTask, completedBeforeRelease),
                "StopAsync must wait for the cancelled timed supporter completion worker.");

            releaseFirstSend.TrySetResult();
            await stopTask.WaitAsync(TimeSpan.FromSeconds(5));

            Assert.Equal(2, Volatile.Read(ref sendCount));
            Assert.Equal("router-close", events.Last());
        }
        finally
        {
            releaseFirstSend.TrySetResult();
            if (stopTask is not null)
            {
                await stopTask.WaitAsync(TimeSpan.FromSeconds(5));
            }

            await coordinator.DisposeAsync();
        }
    }

    private static void ConfigurePendingAvatarScaleResume(
        BridgeCoordinator coordinator,
        Guid ruleId,
        string currentAvatarId)
    {
        var rule = new AvatarScaleRule
        {
            Id = ruleId,
            Name = "Resume identity scale",
            TriggerType = AvatarScaleTriggerType.Follow,
            ScaleMode = AvatarScaleMode.SetHeight,
            TargetHeightMeters = 1.25,
            ActiveTimeSeconds = 60,
            RestoreMode = AvatarScaleRestoreMode.ConfiguredHeight,
            RestoreHeightMeters = 1.6
        };
        var settings = new AppSettings();
        settings.AvatarScaleRules.Add(rule);

        SetPrivateField(
            coordinator,
            "activeConfiguration",
            BridgeRuntimeConfiguration.FromSettings(settings, RuntimeConfig.CreateDefault()));
        SetPrivateField(coordinator, "currentVrChatAvatarId", currentAvatarId);

        var avatarScaleValues = Assert.IsAssignableFrom<IDictionary>(
            GetPrivateField(coordinator, "avatarScaleValues"));
        avatarScaleValues["/avatar/eyeheightscalingallowed"] = new OscObservedValue(
            "/avatar/eyeheightscalingallowed",
            OscParameterType.Bool,
            true);
    }

    private static BridgeCoordinator CreateCoordinator(IActivityResumeService? activityResumeService = null) => new(
        new DesktopInputLockService(Dispatcher.CurrentDispatcher),
        new WorldCommandBlacklistService(),
        new VrChatLocalOscCacheService(),
        activityResumeService ?? new NoOpActivityResumeService());

    private static TriggerRuleSnapshot CreateProfileFloatRule(string parameterAddress) =>
        TestTriggerRuleSnapshotBuilder.Build() with
        {
            Id = Guid.NewGuid(),
            Name = "profile float race",
            AvatarProfileId = Guid.NewGuid(),
            ParameterName = parameterAddress,
            ParameterType = OscParameterType.Float,
            ParameterValue = "0.75",
            ResetValue = "0",
            FloatTransitionInSeconds = 0
        };

    private static TriggerRuleSnapshot CreateRule(string name) =>
        TestTriggerRuleSnapshotBuilder.Build() with
        {
            Id = Guid.NewGuid(),
            Name = name,
            ParameterName = "/avatar/parameters/CrossReview",
            ParameterValue = "1",
            ResetValue = "0"
        };

    private static object CreateReplacementFloatInitialState(
        FloatActionMode actionMode,
        TriggerRuleSnapshot rule,
        string sourceAvatarId,
        CancellationTokenSource cancellation,
        Guid leaseId)
    {
        if (actionMode == FloatActionMode.Pulse)
        {
            return CreatePrivateInstance(
                "ActiveFloatPulseRestoreState",
                rule.Id,
                "replacement pulse",
                rule.ParameterName,
                0.2d,
                sourceAvatarId,
                cancellation,
                leaseId);
        }

        var glitchy = CreatePrivateInstance("ActiveFloatGlitchyRedeemSessionState");
        SetProperty(glitchy, "Rule", rule);
        SetProperty(glitchy, "Address", rule.ParameterName);
        SetProperty(glitchy, "ActiveUntil", DateTimeOffset.UtcNow.AddMinutes(1));
        SetProperty(glitchy, "ResetValue", 0.2d);
        SetProperty(glitchy, "CurrentValue", 0.8d);
        SetProperty(glitchy, "SourceAvatarId", sourceAvatarId);
        SetProperty(glitchy, "CompletionCancellation", cancellation);
        SetProperty(glitchy, "LeaseId", leaseId);
        return glitchy;
    }

    private static object CreateResolvedAction()
    {
        var actionType = typeof(BridgeCoordinator).GetNestedType(
            "ResolvedRuleAction",
            BindingFlags.NonPublic);
        Assert.NotNull(actionType);
        return Activator.CreateInstance(
            actionType!,
            BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic,
            binder: null,
            args:
            [
                new[] { new byte[] { 1 } },
                new[] { new byte[] { 2 } },
                "value",
                string.Empty,
                string.Empty,
                string.Empty,
                string.Empty,
                Array.Empty<OscObservedValue>(),
                Array.Empty<OscObservedValue>(),
                null
            ],
            culture: null)!;
    }

    private static void ArrangeExpiredAvatarScopedFloatState(
        BridgeCoordinator coordinator,
        string stateTypeName,
        string sourceAvatarId)
    {
        var rule = TestTriggerRuleSnapshotBuilder.Build() with
        {
            Id = Guid.NewGuid(),
            AvatarProfileId = Guid.NewGuid(),
            Name = stateTypeName,
            ParameterName = "/avatar/parameters/CrossReviewFloat"
        };
        var cancellation = new CancellationTokenSource();
        var leaseId = Guid.NewGuid();

        switch (stateTypeName)
        {
            case "ActiveFloatRedeemSessionState":
                AddDictionaryValue(
                    coordinator,
                    "activeFloatRedeemSessions",
                    rule.Id,
                    CreatePrivateInstance(
                        stateTypeName,
                        rule,
                        rule.ParameterName,
                        0.8d,
                        0.2d,
                        sourceAvatarId,
                        DateTimeOffset.UtcNow.AddMilliseconds(-1),
                        cancellation,
                        Array.Empty<string>(),
                        leaseId,
                        false,
                        false));
                break;
            case "ActiveFloatPulseRestoreState":
                AddDictionaryValue(
                    coordinator,
                    "activeFloatPulseRestores",
                    rule.Id,
                    CreatePrivateInstance(
                        stateTypeName,
                        rule.Id,
                        rule.Name,
                        rule.ParameterName,
                        0.2d,
                        sourceAvatarId,
                        cancellation,
                        leaseId));
                break;
            case "ActiveFloatGlitchyRedeemSessionState":
                var glitchy = CreatePrivateInstance(stateTypeName);
                SetProperty(glitchy, "Rule", rule);
                SetProperty(glitchy, "Address", rule.ParameterName);
                SetProperty(glitchy, "ActiveUntil", DateTimeOffset.UtcNow.AddMilliseconds(-1));
                SetProperty(glitchy, "ResetValue", 0.2d);
                SetProperty(glitchy, "CurrentValue", 0.8d);
                SetProperty(glitchy, "SourceAvatarId", sourceAvatarId);
                SetProperty(glitchy, "CompletionCancellation", cancellation);
                SetProperty(glitchy, "LeaseId", leaseId);
                AddDictionaryValue(coordinator, "activeGlitchyRedeemSessions", rule.Id, glitchy);
                break;
            default:
                throw new ArgumentOutOfRangeException(nameof(stateTypeName), stateTypeName, "Unknown float state type.");
        }
    }

    private static Guid GetRuleId(BridgeCoordinator coordinator, string stateTypeName) =>
        stateTypeName switch
        {
            "ActiveFloatRedeemSessionState" => GetPrivateProperty<TriggerRuleSnapshot>(
                GetDictionaryValue(coordinator, "activeFloatRedeemSessions", GetFirstDictionaryKey(coordinator, "activeFloatRedeemSessions"))!,
                "Rule").Id,
            "ActiveFloatPulseRestoreState" => GetPrivateProperty<Guid>(
                GetDictionaryValue(coordinator, "activeFloatPulseRestores", GetFirstDictionaryKey(coordinator, "activeFloatPulseRestores"))!,
                "RuleId"),
            "ActiveFloatGlitchyRedeemSessionState" => GetPrivateProperty<TriggerRuleSnapshot>(
                GetDictionaryValue(coordinator, "activeGlitchyRedeemSessions", GetFirstDictionaryKey(coordinator, "activeGlitchyRedeemSessions"))!,
                "Rule").Id,
            _ => throw new ArgumentOutOfRangeException(nameof(stateTypeName), stateTypeName, "Unknown float state type.")
        };

    private static object GetFirstDictionaryKey(BridgeCoordinator coordinator, string fieldName)
    {
        var dictionary = Assert.IsAssignableFrom<IDictionary>(GetPrivateField(coordinator, fieldName));
        var enumerator = dictionary.Keys.GetEnumerator();
        Assert.True(enumerator.MoveNext());
        return enumerator.Current!;
    }

    private static async Task DrainPersistentEffectWorkersAsync(BridgeCoordinator coordinator)
    {
        var method = typeof(BridgeCoordinator).GetMethod(
            "WaitForPersistentEffectTasksAsync",
            BindingFlags.Instance | BindingFlags.NonPublic);
        Assert.NotNull(method);
        var task = Assert.IsAssignableFrom<Task>(method!.Invoke(coordinator, null));
        await task.WaitAsync(TimeSpan.FromSeconds(5));
    }

    private static object CreatePendingResetState(
        TriggerRuleSnapshot rule,
        object action,
        CancellationTokenSource cancellation,
        DateTimeOffset dueAt,
        ReschedulableActivityDeadline activityDeadline,
        string sourceAvatarId,
        bool isWaitingForSourceAvatarReturn,
        IReadOnlyList<byte[]> packets)
    {
        var pendingType = typeof(BridgeCoordinator).GetNestedType(
            "PendingResetState",
            BindingFlags.NonPublic);
        Assert.NotNull(pendingType);
        var pendingReset = Activator.CreateInstance(
            pendingType!,
            BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic,
            binder: null,
            args:
            [
                rule.Id,
                rule.Name,
                rule,
                action,
                packets,
                cancellation,
                dueAt,
                string.Empty,
                string.Empty,
                sourceAvatarId,
                isWaitingForSourceAvatarReturn,
                Array.Empty<OscObservedValue>(),
                Array.Empty<string>(),
                Guid.Empty
            ],
            culture: null);
        Assert.NotNull(pendingReset);
        pendingType!.GetProperty("ActivityDeadline")!.SetValue(pendingReset, activityDeadline);
        return pendingReset!;
    }

    private static object CreateActiveMovementLaneState(
        Guid ownerId,
        DateTimeOffset busyUntil,
        Guid ruleId)
    {
        var laneType = typeof(BridgeCoordinator).GetNestedType(
            "ActiveMovementLaneState",
            BindingFlags.NonPublic);
        Assert.NotNull(laneType);
        return Activator.CreateInstance(
            laneType!,
            BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic,
            binder: null,
            args: [ownerId, busyUntil, ruleId, false],
            culture: null)!;
    }

    private static object CreateActiveSupporterOverrideState(
        TriggerRuleSnapshot rule,
        object action,
        CancellationTokenSource completionCancellation,
        DateTimeOffset activeUntil)
    {
        var stateType = typeof(BridgeCoordinator).GetNestedType(
            "ActiveSupporterOverrideState",
            BindingFlags.NonPublic);
        Assert.NotNull(stateType);
        var state = Activator.CreateInstance(
            stateType!,
            BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic,
            binder: null,
            args:
            [
                rule,
                null,
                action,
                activeUntil,
                1L,
                completionCancellation,
                null
            ],
            culture: null);
        Assert.NotNull(state);
        return state!;
    }

    private static IDictionary GetPendingResets(BridgeCoordinator coordinator) =>
        Assert.IsAssignableFrom<IDictionary>(GetPrivateField(coordinator, "pendingResets"));

    private static TaskCompletionSource NewSignal() =>
        new(TaskCreationOptions.RunContinuationsAsynchronously);

    private static async Task InvokePrivateTask(
        BridgeCoordinator coordinator,
        string methodName,
        params object?[] arguments)
    {
        var method = typeof(BridgeCoordinator).GetMethod(
            methodName,
            BindingFlags.Instance | BindingFlags.NonPublic);
        Assert.NotNull(method);
        var task = Assert.IsAssignableFrom<Task>(method!.Invoke(coordinator, arguments));
        await task.WaitAsync(TimeSpan.FromSeconds(5));
    }

    private static Task InvokePrivateTaskImmediately(
        BridgeCoordinator coordinator,
        string methodName,
        params object?[] arguments)
    {
        var method = typeof(BridgeCoordinator).GetMethod(
            methodName,
            BindingFlags.Instance | BindingFlags.NonPublic);
        Assert.NotNull(method);
        return Assert.IsAssignableFrom<Task>(method!.Invoke(coordinator, arguments));
    }

    private static async Task WaitForMovementCleanupAsync(BridgeCoordinator coordinator)
    {
        var method = typeof(BridgeCoordinator).GetMethod(
            "WaitForActiveMovementCleanupTasksAsync",
            BindingFlags.Instance | BindingFlags.NonPublic);
        Assert.NotNull(method);
        var task = Assert.IsAssignableFrom<Task>(method!.Invoke(coordinator, null));
        await task.WaitAsync(TimeSpan.FromSeconds(5));
    }

    private static async Task WaitUntilAsync(Func<bool> condition)
    {
        var timeoutAt = DateTimeOffset.UtcNow.AddSeconds(5);
        while (!condition())
        {
            if (DateTimeOffset.UtcNow >= timeoutAt)
            {
                throw new TimeoutException("The expected coordinator state was not reached.");
            }

            await Task.Delay(TimeSpan.FromMilliseconds(10));
        }
    }

    private static object? InvokePrivate(
        BridgeCoordinator coordinator,
        string methodName,
        params object?[] arguments)
    {
        var method = typeof(BridgeCoordinator).GetMethod(
            methodName,
            BindingFlags.Instance | BindingFlags.NonPublic);
        Assert.NotNull(method);
        return method!.Invoke(coordinator, arguments);
    }

    private static void SetPrivateField(object target, string fieldName, object? value)
    {
        var field = target.GetType().GetField(
            fieldName,
            BindingFlags.Instance | BindingFlags.NonPublic);
        Assert.NotNull(field);
        field!.SetValue(target, value);
    }

    private static object? GetPrivateField(object target, string fieldName)
    {
        var field = target.GetType().GetField(
            fieldName,
            BindingFlags.Instance | BindingFlags.NonPublic);
        Assert.NotNull(field);
        return field!.GetValue(target);
    }

    private static T GetPrivateField<T>(object target, string fieldName)
    {
        return Assert.IsType<T>(GetPrivateField(target, fieldName));
    }

    private static object CreatePrivateInstance(string typeName, params object?[] arguments)
    {
        var type = typeof(BridgeCoordinator).GetNestedType(typeName, BindingFlags.NonPublic);
        Assert.NotNull(type);
        return Activator.CreateInstance(
            type!,
            BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic,
            binder: null,
            args: arguments,
            culture: null)!;
    }

    private static void SetProperty(object instance, string name, object? value)
    {
        var property = instance.GetType().GetProperty(
            name,
            BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic);
        Assert.NotNull(property);
        property!.SetValue(instance, value);
    }

    private static void AddDictionaryValue(
        object instance,
        string fieldName,
        object key,
        object value)
    {
        var dictionary = Assert.IsAssignableFrom<IDictionary>(GetPrivateField(instance, fieldName));
        dictionary.Add(key, value);
    }

    private static object? GetDictionaryValue(object instance, string fieldName, object key)
    {
        var dictionary = Assert.IsAssignableFrom<IDictionary>(GetPrivateField(instance, fieldName));
        return dictionary[key];
    }

    private static T GetPrivateProperty<T>(object target, string propertyName)
    {
        var property = target.GetType().GetProperty(
            propertyName,
            BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic);
        Assert.NotNull(property);
        return Assert.IsType<T>(property!.GetValue(target));
    }

    private static string GetMethodBody(string source, string methodSignatureStart)
    {
        var methodStart = source.IndexOf(methodSignatureStart, StringComparison.Ordinal);
        Assert.True(methodStart >= 0, $"Could not find method signature starting with '{methodSignatureStart}'.");
        var bodyStart = source.IndexOf('{', methodStart);
        Assert.True(bodyStart >= 0, $"Could not find method body for '{methodSignatureStart}'.");

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

        throw new InvalidOperationException($"Could not find method body end for '{methodSignatureStart}'.");
    }

    private static string NormalizeWhitespace(string source) =>
        System.Text.RegularExpressions.Regex.Replace(source, @"\s+", " ");

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

    private class PendingAvatarScaleResumeService(ResumeActivity activity) : IActivityResumeService
    {
        public ResumeActivity Activity { get; } = activity;

        public Task LoadPendingAsync() => Task.CompletedTask;

        public bool HasPendingResume => true;

        public bool IsPendingForAvatar(string avatarId) => true;

        public IReadOnlyList<ResumeActivity> GetPendingActivities() => [Activity];

        public virtual Task RemoveExpiredActivitiesAsync() => Task.CompletedTask;

        public Task RecordActivityStartedAsync(ResumeActivity activity, string avatarId) => Task.CompletedTask;

        public Task RecordActivityEndedAsync(Guid ruleId, ResumeActivity? expectedActivity = null) => Task.CompletedTask;

        public virtual Task RemoveActivityAsync(ResumeActivity activity) => Task.CompletedTask;

        public Task ClearAllAsync() => Task.CompletedTask;

        public Task CommitAsync() => Task.CompletedTask;

        public Task DeleteStaleFileIfPresentAsync() => Task.CompletedTask;
    }

    private sealed class BlockingPendingAvatarScaleResumeService(ResumeActivity activity) : PendingAvatarScaleResumeService(activity)
    {
        private readonly TaskCompletionSource releaseCleanup =
            new(TaskCreationOptions.RunContinuationsAsynchronously);

        public TaskCompletionSource CleanupStarted { get; } =
            new(TaskCreationOptions.RunContinuationsAsynchronously);

        public void ReleaseCleanup() => releaseCleanup.TrySetResult();

        public override async Task RemoveExpiredActivitiesAsync()
        {
            CleanupStarted.TrySetResult();
            await releaseCleanup.Task;
        }
    }

    private sealed class BlockingPendingActivityCleanupResumeService(ResumeActivity activity)
        : PendingAvatarScaleResumeService(activity)
    {
        private readonly TaskCompletionSource releaseActivityCleanup =
            new(TaskCreationOptions.RunContinuationsAsynchronously);

        public TaskCompletionSource ActivityCleanupStarted { get; } =
            new(TaskCreationOptions.RunContinuationsAsynchronously);

        public int RemoveActivityCallCount => Volatile.Read(ref removeActivityCallCount);

        private int removeActivityCallCount;

        public void ReleaseActivityCleanup() => releaseActivityCleanup.TrySetResult();

        public override Task RemoveExpiredActivitiesAsync() => Task.CompletedTask;

        public override async Task RemoveActivityAsync(ResumeActivity activity)
        {
            Interlocked.Increment(ref removeActivityCallCount);
            ActivityCleanupStarted.TrySetResult();
            await releaseActivityCleanup.Task;
        }
    }

    private sealed class BlockingAvatarHeightQueryHost : IDisposable
    {
        private readonly HttpListener listener = new();
        private readonly TaskCompletionSource releaseBlockedResponse =
            new(TaskCreationOptions.RunContinuationsAsynchronously);
        private readonly float height;
        private readonly int blockedRequestNumber;
        private readonly bool firstHeightResponseInvalid;
        private readonly string address;
        private readonly OscParameterType parameterType;
        private readonly int integerValue;
        private int requestCount;

        public BlockingAvatarHeightQueryHost(
            float height,
            int blockedRequestNumber = 1,
            bool firstHeightResponseInvalid = false,
            string address = "/avatar/eyeheight",
            OscParameterType parameterType = OscParameterType.Float,
            int integerValue = 0)
        {
            this.height = height;
            this.blockedRequestNumber = blockedRequestNumber;
            this.firstHeightResponseInvalid = firstHeightResponseInvalid;
            this.address = address;
            this.parameterType = parameterType;
            this.integerValue = integerValue;
            Port = Extensions.GetAvailableTcpPort();
            listener.Prefixes.Add($"http://{IPAddress.Loopback}:{Port}/");
            listener.Start();
            _ = AcceptRequestsAsync();
        }

        public int Port { get; }

        public TaskCompletionSource RequestStarted { get; } =
            new(TaskCreationOptions.RunContinuationsAsynchronously);

        public TaskCompletionSource BlockedRequestStarted { get; } =
            new(TaskCreationOptions.RunContinuationsAsynchronously);

        public int RequestCount => Volatile.Read(ref requestCount);

        public void Release() => releaseBlockedResponse.TrySetResult();

        public void Dispose()
        {
            Release();
            listener.Close();
        }

        private async Task AcceptRequestsAsync()
        {
            try
            {
                while (true)
                {
                    var context = await listener.GetContextAsync().ConfigureAwait(false);
                    var currentRequestNumber = Interlocked.Increment(ref requestCount);
                    if (currentRequestNumber == 1)
                    {
                        RequestStarted.TrySetResult();
                    }

                    if (currentRequestNumber == blockedRequestNumber)
                    {
                        BlockedRequestStarted.TrySetResult();
                        await releaseBlockedResponse.Task.ConfigureAwait(false);
                    }

                    var root = new OSCQueryRootNode();
                    var responseIsValidHeight = !(currentRequestNumber == 1 && firstHeightResponseInvalid);
                    var responseType = parameterType switch
                    {
                        OscParameterType.Int => "i",
                        OscParameterType.Bool => "T",
                        _ => "f"
                    };
                    object[] responseValue = parameterType switch
                    {
                        OscParameterType.Int => [integerValue],
                        OscParameterType.Bool => [true],
                        _ => [height]
                    };
                    root.AddNode(new OSCQueryNode(address)
                    {
                        OscType = responseIsValidHeight ? responseType : "s",
                        Value = responseIsValidHeight
                            ? responseValue
                            : new object[] { "not-a-height" }
                    });
                    var bytes = System.Text.Encoding.UTF8.GetBytes(root.ToString());
                    context.Response.ContentLength64 = bytes.Length;
                    context.Response.ContentType = "application/json";
                    await context.Response.OutputStream.WriteAsync(bytes).ConfigureAwait(false);
                    context.Response.Close();
                }
            }
            catch (HttpListenerException)
            {
            }
            catch (ObjectDisposedException)
            {
            }
        }
    }

    private sealed class NoOpActivityResumeService : IActivityResumeService
    {
        public Task LoadPendingAsync() => Task.CompletedTask;

        public bool HasPendingResume => false;

        public bool IsPendingForAvatar(string avatarId) => false;

        public IReadOnlyList<ResumeActivity> GetPendingActivities() => [];

        public Task RemoveExpiredActivitiesAsync() => Task.CompletedTask;

        public Task RecordActivityStartedAsync(ResumeActivity activity, string avatarId) => Task.CompletedTask;

        public Task RecordActivityEndedAsync(Guid ruleId, ResumeActivity? expectedActivity = null) => Task.CompletedTask;

        public Task RemoveActivityAsync(ResumeActivity activity) => Task.CompletedTask;

        public Task ClearAllAsync() => Task.CompletedTask;

        public Task CommitAsync() => Task.CompletedTask;

        public Task DeleteStaleFileIfPresentAsync() => Task.CompletedTask;
    }
}
