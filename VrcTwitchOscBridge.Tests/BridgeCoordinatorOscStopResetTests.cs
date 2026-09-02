using System.Buffers.Binary;
using System.Collections;
using System.IO;
using System.Reflection;
using System.Text;
using System.Text.RegularExpressions;
using System.Windows.Threading;
using VrcTwitchOscBridge.Models;
using VrcTwitchOscBridge.Services;
using Xunit;

namespace VrcTwitchOscBridge.Tests;

public sealed class BridgeCoordinatorOscStopResetTests
{
    [Fact]
    public void StopAsync_ResetsBeforeClosingOsc()
    {
        var source = File.ReadAllText(FindSourceFile("VrcTwitchOscBridge", "Services", "BridgeCoordinator.cs"));
        var stopBody = NormalizeWhitespace(GetMethodBody(source, "private async Task StopCoreAsync"));
        var resetHelperBody = NormalizeWhitespace(
            GetMethodBody(source, "private async Task ResetRuntimeEffectsBeforeOscShutdownAsync"));

        var resetHelperIndex = stopBody.IndexOf("ResetRuntimeEffectsBeforeOscShutdownAsync()", StringComparison.Ordinal);
        var pendingResetIndex = resetHelperBody.IndexOf("ResetPendingRulesAsync()", StringComparison.Ordinal);
        var oscCloseIndex = stopBody.IndexOf("StopOscRouterSafelyAsync()", StringComparison.Ordinal);
        var clearIndex = stopBody.IndexOf("ClearRuntimeState()", StringComparison.Ordinal);

        Assert.True(resetHelperIndex >= 0);
        Assert.True(pendingResetIndex >= 0);
        Assert.True(oscCloseIndex > resetHelperIndex);
        Assert.True(clearIndex > resetHelperIndex);
    }

    [Fact]
    public void StopAsync_CannotSkipEffectResetForAnyRuntimeState()
    {
        var source = File.ReadAllText(FindSourceFile("VrcTwitchOscBridge", "Services", "BridgeCoordinator.cs"));
        var stopBody = NormalizeWhitespace(GetMethodBody(source, "private async Task StopCoreAsync"));
        var resetIndex = stopBody.IndexOf(
            "ResetRuntimeEffectsBeforeOscShutdownAsync()",
            StringComparison.Ordinal);
        var oscCloseIndex = stopBody.IndexOf("StopOscRouterSafelyAsync()", StringComparison.Ordinal);

        Assert.True(resetIndex >= 0);
        Assert.True(oscCloseIndex > resetIndex);
        Assert.DoesNotContain("if (runtimeTask is null)", stopBody, StringComparison.Ordinal);
        Assert.DoesNotContain("if (runtimeCancellation is null)", stopBody, StringComparison.Ordinal);
    }

    [Fact]
    public void StopAsync_AwaitsRuntimeTaskEvenWhenCancellationSourceIsMissing()
    {
        var source = File.ReadAllText(FindSourceFile("VrcTwitchOscBridge", "Services", "BridgeCoordinator.cs"));
        var stopBody = NormalizeWhitespace(GetMethodBody(source, "private async Task StopCoreAsync"));

        var taskCaptureIndex = stopBody.IndexOf(
            "var runtimeTaskToAwait = runtimeTask",
            StringComparison.Ordinal);
        var cancellationCaptureIndex = stopBody.IndexOf(
            "var runtimeCancellationToDispose = runtimeCancellation",
            StringComparison.Ordinal);
        var cancelIndex = stopBody.IndexOf(
            "runtimeCancellationToDispose?.Cancel()",
            StringComparison.Ordinal);
        var awaitIndex = stopBody.IndexOf("await runtimeTaskToAwait", StringComparison.Ordinal);
        var disposeIndex = stopBody.IndexOf(
            "runtimeCancellationToDispose?.Dispose()",
            StringComparison.Ordinal);
        var resetIndex = stopBody.IndexOf(
            "ResetRuntimeEffectsBeforeOscShutdownAsync()",
            StringComparison.Ordinal);
        var oscCloseIndex = stopBody.IndexOf("StopOscRouterSafelyAsync()", StringComparison.Ordinal);

        Assert.True(taskCaptureIndex >= 0);
        Assert.True(cancellationCaptureIndex > taskCaptureIndex);
        Assert.True(cancelIndex > cancellationCaptureIndex);
        Assert.True(awaitIndex > cancelIndex);
        Assert.True(disposeIndex > awaitIndex);
        Assert.True(resetIndex > disposeIndex);
        Assert.True(oscCloseIndex > resetIndex);
        Assert.DoesNotContain("if (runtimeTask is null)", stopBody, StringComparison.Ordinal);
        Assert.DoesNotContain("if (runtimeCancellation is null)", stopBody, StringComparison.Ordinal);
    }

    [Fact]
    public void ResetRuntimeEffects_ClosesPersistentAdmissionDrainsWorkersAndSendsFinalPlansBeforeRouterClose()
    {
        var source = File.ReadAllText(FindSourceFile("VrcTwitchOscBridge", "Services", "BridgeCoordinator.cs"));
        var resetBody = NormalizeWhitespace(
            GetMethodBody(source, "private async Task ResetRuntimeEffectsBeforeOscShutdownAsync"));
        var finalResetBody = NormalizeWhitespace(
            GetMethodBody(source, "private async Task SendPersistentEffectResetPlansAsync"));
        var stopBody = NormalizeWhitespace(GetMethodBody(source, "private async Task StopCoreAsync"));

        var closeAdmissionIndex = resetBody.IndexOf("ClosePersistentEffectAdmission()", StringComparison.Ordinal);
        var capturePlansIndex = resetBody.IndexOf("CapturePersistentEffectResetPlansLocked()", StringComparison.Ordinal);
        var cancelWorkersIndex = resetBody.IndexOf("CancelPersistentEffectWorkers", StringComparison.Ordinal);
        var workerDrainIndex = resetBody.IndexOf("WaitForPersistentEffectTasksAsync", StringComparison.Ordinal);
        var scaleWriteDrainIndex = resetBody.IndexOf("WaitForAvatarScaleWriteUsersAsync", StringComparison.Ordinal);
        var finalResetIndex = resetBody.IndexOf("SendPersistentEffectResetPlansAsync", StringComparison.Ordinal);
        var routerCloseIndex = stopBody.IndexOf("StopOscRouterSafelyAsync()", StringComparison.Ordinal);
        var runtimeResetIndex = stopBody.IndexOf("ResetRuntimeEffectsBeforeOscShutdownAsync()", StringComparison.Ordinal);

        Assert.True(closeAdmissionIndex >= 0);
        Assert.True(capturePlansIndex > closeAdmissionIndex);
        Assert.True(cancelWorkersIndex > capturePlansIndex);
        Assert.True(workerDrainIndex > cancelWorkersIndex);
        Assert.True(scaleWriteDrainIndex > workerDrainIndex);
        Assert.True(finalResetIndex > scaleWriteDrainIndex);
        Assert.True(runtimeResetIndex >= 0);
        Assert.True(routerCloseIndex > runtimeResetIndex);
        Assert.Contains("CancellationToken.None", finalResetBody, StringComparison.Ordinal);
    }

    [Fact]
    public void PersistentEffectWorkers_AllUseTheCoordinatorAdmissionTracker()
    {
        var source = File.ReadAllText(FindSourceFile("VrcTwitchOscBridge", "Services", "BridgeCoordinator.cs"));
        var normalizedSource = NormalizeWhitespace(source);

        Assert.Contains("HashSet<Task> persistentEffectTasks", source, StringComparison.Ordinal);
        Assert.Contains("persistentEffectTaskGate", source, StringComparison.Ordinal);
        Assert.Contains("TryStartPersistentEffectWorker", source, StringComparison.Ordinal);
        Assert.Contains("WaitForPersistentEffectTasksAsync", source, StringComparison.Ordinal);

        foreach (var workerMethod in new[]
        {
            "ScheduleActiveFloatRedeemCompletion",
            "ScheduleActiveFloatRedeemCompletionAfterGracePeriod",
            "ScheduleFloatPulseRestore",
            "RunGlitchyLoopAsync",
            "RunAvatarScaleRestoreSequenceAsync",
            "RunSupporterGrowthScaleSessionAsync",
            "EnsureQueuedRuleDrain",
            "EnsureQueuedLaneDrain",
            "EnsureQueuedAvatarSwitchDrain"
        })
        {
            Assert.Contains(
                $"{workerMethod}",
                normalizedSource,
                StringComparison.Ordinal);
        }

        Assert.Contains("TryStartPersistentEffectWorker", NormalizeWhitespace(
            GetMethodBody(source, "private void ScheduleActiveFloatRedeemCompletion(")), StringComparison.Ordinal);
        Assert.Contains("TryStartPersistentEffectWorker", NormalizeWhitespace(
            GetMethodBody(source, "private bool ScheduleActiveFloatRedeemCompletionAfterGracePeriod(")), StringComparison.Ordinal);
        Assert.Contains("TryStartPersistentEffectWorker", NormalizeWhitespace(
            GetMethodBody(source, "private void ScheduleFloatPulseRestore(")), StringComparison.Ordinal);
        Assert.Contains("TryStartPersistentEffectWorker", normalizedSource, StringComparison.Ordinal);
        Assert.Contains("RunGlitchyLoopAsync", normalizedSource, StringComparison.Ordinal);
        Assert.Contains("RunAvatarScaleRestoreSequenceAsync", normalizedSource, StringComparison.Ordinal);
        Assert.Contains("RunSupporterGrowthScaleSessionAsync", normalizedSource, StringComparison.Ordinal);
        Assert.Contains("TryStartPersistentEffectWorker", NormalizeWhitespace(
            GetMethodBody(source, "private void EnsureQueuedRuleDrain(")), StringComparison.Ordinal);
        Assert.Contains("TryStartPersistentEffectWorker", NormalizeWhitespace(
            GetMethodBody(source, "private void EnsureQueuedLaneDrain(")), StringComparison.Ordinal);
        Assert.Contains("TryStartPersistentEffectWorker", NormalizeWhitespace(
            GetMethodBody(source, "private void EnsureQueuedAvatarSwitchDrain(")), StringComparison.Ordinal);
    }

    [Fact]
    public void GracePeriodCompletion_TransfersSessionOwnershipToReplacementWorker()
    {
        var source = File.ReadAllText(FindSourceFile("VrcTwitchOscBridge", "Services", "BridgeCoordinator.cs"));
        var completionBody = NormalizeWhitespace(GetMethodBody(
            source,
            "private void ScheduleActiveFloatRedeemCompletion("));
        var graceBody = NormalizeWhitespace(GetMethodBody(
            source,
            "private bool ScheduleActiveFloatRedeemCompletionAfterGracePeriod("));

        var handoffDeclarationIndex = completionBody.IndexOf(
            "handoffToReplacementWorker",
            StringComparison.Ordinal);
        var scheduleIndex = completionBody.IndexOf(
            "handoffToReplacementWorker = ScheduleActiveFloatRedeemCompletionAfterGracePeriod",
            StringComparison.Ordinal);
        var finishGuardIndex = completionBody.IndexOf(
            "if (!handoffToReplacementWorker)",
            StringComparison.Ordinal);
        var finishIndex = completionBody.IndexOf(
            "FinishActiveFloatRedeemSession",
            StringComparison.Ordinal);

        Assert.True(handoffDeclarationIndex >= 0);
        Assert.True(scheduleIndex >= handoffDeclarationIndex);
        Assert.True(finishGuardIndex > scheduleIndex);
        Assert.True(finishIndex > finishGuardIndex);
        Assert.Contains("return TryStartPersistentEffectWorker", graceBody, StringComparison.Ordinal);
    }

    [Fact]
    public async Task GracePeriodCompletion_AdmissionHandoffKeepsSessionUntilReplacementOwnsIt()
    {
        var coordinator = new BridgeCoordinator(
            new DesktopInputLockService(Dispatcher.CurrentDispatcher),
            new WorldCommandBlacklistService(),
            new VrChatLocalOscCacheService());
        var rule = TestTriggerRuleSnapshotBuilder.Build() with
        {
            Name = "grace handoff float",
            ParameterName = "/avatar/parameters/GraceHandoffFloat"
        };
        var cancellation = new CancellationTokenSource();
        var session = CreatePrivateInstance(
            "ActiveFloatRedeemSessionState",
            rule,
            "/avatar/parameters/GraceHandoffFloat",
            0.8d,
            0.2d,
            string.Empty,
            DateTimeOffset.UtcNow.AddMinutes(1),
            cancellation,
            Array.Empty<string>(),
            Guid.Empty,
            false,
            false);
        AddDictionaryValue(coordinator, "activeFloatRedeemSessions", rule.Id, session);

        var scheduleMethod = typeof(BridgeCoordinator).GetMethod(
            "ScheduleActiveFloatRedeemCompletionAfterGracePeriod",
            BindingFlags.Instance | BindingFlags.NonPublic);
        var waitMethod = typeof(BridgeCoordinator).GetMethod(
            "WaitForPersistentEffectTasksAsync",
            BindingFlags.Instance | BindingFlags.NonPublic);
        Assert.NotNull(scheduleMethod);
        Assert.NotNull(waitMethod);

        try
        {
            var accepted = scheduleMethod!.Invoke(
                coordinator,
                [session, cancellation, 0d, 1d, 0d]);

            Assert.True(Assert.IsType<bool>(accepted));
            Assert.Same(session, GetDictionaryValue(coordinator, "activeFloatRedeemSessions", rule.Id));
        }
        finally
        {
            cancellation.Cancel();
            var waitTask = Assert.IsAssignableFrom<Task>(waitMethod!.Invoke(coordinator, null));
            await waitTask.WaitAsync(TimeSpan.FromSeconds(5));
        }
    }

    [Fact]
    public void FinishActiveFloatRedeemSession_ChecksStoppingAndGateDisposalUnderStateGate()
    {
        var source = File.ReadAllText(FindSourceFile("VrcTwitchOscBridge", "Services", "BridgeCoordinator.cs"));
        var finishBody = NormalizeWhitespace(GetMethodBody(
            source,
            "private void FinishActiveFloatRedeemSession("));
        var lockIndex = finishBody.IndexOf("lock (stateGate)", StringComparison.Ordinal);
        var stoppingIndex = finishBody.IndexOf("if (isStopping)", StringComparison.Ordinal);
        var removalIndex = finishBody.IndexOf("activeFloatRedeemSessions.Remove", StringComparison.Ordinal);
        var disposalDecisionIndex = finishBody.IndexOf("disposeSendGate = true", StringComparison.Ordinal);
        var gateDisposeIndex = finishBody.IndexOf("session.SendGate.Dispose()", StringComparison.Ordinal);

        Assert.True(lockIndex >= 0);
        Assert.True(stoppingIndex > lockIndex);
        Assert.True(removalIndex > stoppingIndex);
        Assert.True(disposalDecisionIndex > removalIndex);
        Assert.True(gateDisposeIndex > disposalDecisionIndex);
        Assert.DoesNotContain("IsRuntimeStopping()", finishBody, StringComparison.Ordinal);
    }

    [Fact]
    public void FinishActiveFloatRedeemSession_DisposesGateForNormalCompletion()
    {
        var coordinator = new BridgeCoordinator(
            new DesktopInputLockService(Dispatcher.CurrentDispatcher),
            new WorldCommandBlacklistService(),
            new VrChatLocalOscCacheService());
        var rule = TestTriggerRuleSnapshotBuilder.Build() with
        {
            Name = "normal gate disposal",
            ParameterName = "/avatar/parameters/NormalGateDisposal"
        };
        var cancellation = new CancellationTokenSource();
        var session = CreatePrivateInstance(
            "ActiveFloatRedeemSessionState",
            rule,
            "/avatar/parameters/NormalGateDisposal",
            0.8d,
            0.2d,
            string.Empty,
            DateTimeOffset.UtcNow.AddMinutes(1),
            cancellation,
            Array.Empty<string>(),
            Guid.Empty,
            false,
            false);
        AddDictionaryValue(coordinator, "activeFloatRedeemSessions", rule.Id, session);
        var gate = GetProperty<SemaphoreSlim>(session, "SendGate");
        var finishMethod = typeof(BridgeCoordinator).GetMethod(
            "FinishActiveFloatRedeemSession",
            BindingFlags.Instance | BindingFlags.NonPublic);
        Assert.NotNull(finishMethod);

        finishMethod!.Invoke(coordinator, [session, cancellation, false]);

        Assert.Throws<ObjectDisposedException>(() => gate.Wait(0));
    }

    [Fact]
    public async Task PersistentWorkerFault_IsObservedAndSanitizedBeforeTrackerRemoval()
    {
        var coordinator = new BridgeCoordinator(
            new DesktopInputLockService(Dispatcher.CurrentDispatcher),
            new WorldCommandBlacklistService(),
            new VrChatLocalOscCacheService());
        var logs = new List<string>();
        coordinator.LogWritten += logs.Add;
        var workerEntered = NewSignal();
        var releaseWorker = NewSignal();
        var startWorker = typeof(BridgeCoordinator).GetMethod(
            "TryStartPersistentEffectWorker",
            BindingFlags.Instance | BindingFlags.NonPublic);
        var waitForWorkers = typeof(BridgeCoordinator).GetMethod(
            "WaitForPersistentEffectTasksAsync",
            BindingFlags.Instance | BindingFlags.NonPublic);
        Assert.NotNull(startWorker);
        Assert.NotNull(waitForWorkers);
        var fixtureRoot = Path.Combine(
            Path.GetPathRoot(Environment.CurrentDirectory)
                ?? throw new InvalidOperationException("The test environment has no filesystem root."),
            "CrystalRelayFixture");

        var accepted = Assert.IsType<bool>(startWorker!.Invoke(
            coordinator,
            [
                (Func<Task>)(async () =>
                {
                    workerEntered.SetResult();
                    await releaseWorker.Task;
                    throw new InvalidOperationException(Path.Combine(fixtureRoot, "token=FIXTURE_VALUE"));
                }),
                null
            ]));
        Assert.True(accepted);
        await workerEntered.Task.WaitAsync(TimeSpan.FromSeconds(5));

        var waitTask = Assert.IsAssignableFrom<Task>(waitForWorkers!.Invoke(coordinator, null));
        releaseWorker.SetResult();
        await waitTask.WaitAsync(TimeSpan.FromSeconds(5));

        var workerFaultLog = Assert.Single(logs, log => log.Contains("Persistent effect worker failed", StringComparison.Ordinal));
        Assert.DoesNotContain(fixtureRoot, workerFaultLog, StringComparison.Ordinal);
        Assert.DoesNotContain("token=FIXTURE_VALUE", workerFaultLog, StringComparison.Ordinal);
        Assert.Contains("<local path>", workerFaultLog, StringComparison.Ordinal);
    }

    [Fact]
    public void PersistentWorkerFault_ObservesTheExceptionBeforeRemovingTheCompletionTask()
    {
        var source = File.ReadAllText(FindSourceFile("VrcTwitchOscBridge", "Services", "BridgeCoordinator.cs"));
        var workerBody = NormalizeWhitespace(GetMethodBody(
            source,
            "private async Task RunTrackedPersistentEffectWorkerAsync("));
        var faultIndex = workerBody.IndexOf("completion.TrySetException", StringComparison.Ordinal);
        var observationIndex = workerBody.IndexOf(
            "ObservePersistentEffectWorkerFault",
            StringComparison.Ordinal);
        var removalIndex = workerBody.IndexOf(
            "persistentEffectTasks.Remove(completion.Task)",
            StringComparison.Ordinal);

        Assert.True(faultIndex >= 0);
        Assert.True(observationIndex > faultIndex);
        Assert.True(removalIndex > observationIndex);
    }

    [Fact]
    public void ShutdownResetPlansCaptureFloatGlitchyScaleAndSupporterGrowthBeforeStateClear()
    {
        var source = File.ReadAllText(FindSourceFile("VrcTwitchOscBridge", "Services", "BridgeCoordinator.cs"));
        var resetBody = NormalizeWhitespace(
            GetMethodBody(source, "private async Task ResetRuntimeEffectsBeforeOscShutdownAsync"));
        var captureBody = NormalizeWhitespace(
            GetMethodBody(source, "private PersistentEffectResetPlans CapturePersistentEffectResetPlansLocked"));
        var finalResetBody = NormalizeWhitespace(
            GetMethodBody(source, "private async Task SendPersistentEffectResetPlansAsync"));
        var stopBody = NormalizeWhitespace(GetMethodBody(source, "private async Task StopCoreAsync"));

        foreach (var planMarker in new[]
        {
            "activeFloatRedeemSessions",
            "activeGlitchyRedeemSessions",
            "activeAvatarScaleRestoreSequence",
            "avatarScaleSupporterGrowthStates",
            "PersistentFloatResetPlan",
            "PersistentGlitchyFloatResetPlan",
            "PersistentAvatarScaleRestorePlan",
            "PersistentSupporterGrowthResetPlan"
        })
        {
            Assert.Contains(planMarker, captureBody, StringComparison.Ordinal);
        }

        Assert.Contains("RestoreMode.None", captureBody, StringComparison.Ordinal);
        var restoreModeCheckIndex = captureBody.IndexOf(
            "activeSequence.Rule?.RestoreMode != AvatarScaleRestoreMode.None",
            StringComparison.Ordinal);
        var scalePlanIndex = captureBody.IndexOf(
            "new PersistentAvatarScaleRestorePlan(",
            StringComparison.Ordinal);
        Assert.True(restoreModeCheckIndex >= 0 && scalePlanIndex > restoreModeCheckIndex);
        Assert.Contains("CancellationToken.None", finalResetBody, StringComparison.Ordinal);
        Assert.DoesNotContain("ClearRuntimeState()", resetBody, StringComparison.Ordinal);
        Assert.Contains("ClearRuntimeState()", stopBody, StringComparison.Ordinal);
    }

    [Fact]
    public async Task PersistentWorkerAdmission_DrainsBeforeFakeTransportCloseAndRejectsLateWork()
    {
        var coordinatorType = typeof(BridgeCoordinator);
        var startWorker = coordinatorType.GetMethod(
            "TryStartPersistentEffectWorker",
            BindingFlags.Instance | BindingFlags.NonPublic);
        var stopAdmission = coordinatorType.GetMethod(
            "ClosePersistentEffectAdmission",
            BindingFlags.Instance | BindingFlags.NonPublic);
        var waitForWorkers = coordinatorType.GetMethod(
            "WaitForPersistentEffectTasksAsync",
            BindingFlags.Instance | BindingFlags.NonPublic);
        var sendPackets = coordinatorType.GetMethod(
            "SendPacketsToVrChatAsync",
            BindingFlags.Instance | BindingFlags.NonPublic);

        Assert.NotNull(startWorker);
        Assert.NotNull(stopAdmission);
        Assert.NotNull(waitForWorkers);
        Assert.NotNull(sendPackets);

        var coordinator = new BridgeCoordinator(
            new DesktopInputLockService(Dispatcher.CurrentDispatcher),
            new WorldCommandBlacklistService(),
            new VrChatLocalOscCacheService());

        var workerEntered = NewSignal();
        var releaseWorker = NewSignal();
        var sendCount = 0;
        var transportClosed = false;
        SetField(
            coordinator,
            "testSendToVrChatAsync",
            (Func<byte[], CancellationToken, Task>)((_, _) =>
            {
                Assert.False(transportClosed, "A tracked worker sent after the transport-close marker.");
                Interlocked.Increment(ref sendCount);
                return Task.CompletedTask;
            }));
        SetField(
            coordinator,
            "testStopOscRouterAsync",
            (Func<Task>)(() =>
            {
                transportClosed = true;
                return Task.CompletedTask;
            }));
        SetField(
            coordinator,
            "testForceReleaseDesktopInputAsync",
            (Func<CancellationToken, Task>)(_ => Task.CompletedTask));

        var worker = (Func<Task>)(async () =>
        {
            workerEntered.SetResult();
            await releaseWorker.Task;
            await InvokeSendPacketsAsync(coordinator, sendPackets!);
        });
        var accepted = Assert.IsType<bool>(startWorker!.Invoke(
            coordinator,
            new object?[] { worker, null }));
        Assert.True(accepted);
        await workerEntered.Task.WaitAsync(TimeSpan.FromSeconds(1));

        var stopTask = coordinator.StopAsync();
        Assert.False(stopTask.IsCompleted);

        var lateWorkerAccepted = Assert.IsType<bool>(startWorker.Invoke(
            coordinator,
            new object?[] { (Func<Task>)(() => Task.CompletedTask), null }));
        Assert.False(lateWorkerAccepted);
        Assert.False(transportClosed);

        releaseWorker.SetResult();
        await stopTask.WaitAsync(TimeSpan.FromSeconds(5));

        Assert.Equal(1, sendCount);
        Assert.True(transportClosed);
    }

    [Fact]
    public async Task StopAsync_UsesProductionRuntimeCleanupToRestoreAvatarScaleBeforeRouterClose()
    {
        var coordinator = new BridgeCoordinator(
            new DesktopInputLockService(Dispatcher.CurrentDispatcher),
            new WorldCommandBlacklistService(),
            new VrChatLocalOscCacheService());
        var events = new List<string>();
        var sentHeights = new List<float>();
        SetField(
            coordinator,
            "testSendToVrChatAsync",
            (Func<byte[], CancellationToken, Task>)((packet, _) =>
            {
                var captured = ReadFloatPacket(packet);
                sentHeights.Add(captured.Value);
                events.Add($"send:{captured.Address}");
                return Task.CompletedTask;
            }));
        SetField(
            coordinator,
            "testStopOscRouterAsync",
            (Func<Task>)(() =>
            {
                events.Add("router-close");
                return Task.CompletedTask;
            }));
        SetField(
            coordinator,
            "testForceReleaseDesktopInputAsync",
            (Func<CancellationToken, Task>)(_ => Task.CompletedTask));

        var rule = BridgeRuntimeConfiguration.CreateManualTestSnapshot(new AvatarScaleRule
        {
            Id = Guid.NewGuid(),
            Name = "Production shutdown scale",
            RestoreMode = AvatarScaleRestoreMode.ConfiguredHeight,
            RestoreHeightMeters = 1.65,
            ActiveTimeSeconds = 60
        });
        SetField(
            coordinator,
            "activeAvatarScaleRestoreSequence",
            CreatePrivateInstance(
                "ActiveAvatarScaleRestoreSequenceState",
                1L,
                "avtr_shutdown",
                1.2d,
                1.65d,
                DateTimeOffset.UtcNow.AddMinutes(1),
                "Production shutdown scale",
                0d,
                false,
                false,
                rule,
                0d,
                false,
                1.2d));
        SetField(coordinator, "avatarScaleRestoreSequenceCancellation", new CancellationTokenSource());

        var transitionGate = Assert.IsType<SemaphoreSlim>(GetField(
            coordinator,
            "timedSupporterOverrideTransitionGate"));
        Assert.True(transitionGate.Wait(0));
        var gateHeld = true;
        try
        {
            var runtimeCancellation = new CancellationTokenSource();
            runtimeCancellation.Cancel();
            SetField(coordinator, "runtimeCancellation", runtimeCancellation);

            var runBridgeMethod = typeof(BridgeCoordinator).GetMethod(
                "RunBridgeAsync",
                BindingFlags.Instance | BindingFlags.NonPublic);
            Assert.NotNull(runBridgeMethod);
            var runtimeTask = Assert.IsAssignableFrom<Task>(runBridgeMethod!.Invoke(
                coordinator,
                new object?[] { runtimeCancellation.Token }));
            Assert.False(runtimeTask.IsCompleted);
            SetField(coordinator, "runtimeTask", runtimeTask);

            var stopTask = coordinator.StopAsync();
            Assert.False(stopTask.IsCompleted);

            transitionGate.Release();
            gateHeld = false;
            await stopTask.WaitAsync(TimeSpan.FromSeconds(5));
        }
        finally
        {
            if (gateHeld)
            {
                transitionGate.Release();
            }
        }

        Assert.Equal([1.65f], sentHeights);
        var routerCloseIndex = events.IndexOf("router-close");
        Assert.True(routerCloseIndex > 0);
        Assert.All(events.Take(routerCloseIndex), entry => Assert.StartsWith("send:", entry, StringComparison.Ordinal));
    }

    [Fact]
    public void PersistentScaleWorkerCleanup_ClaimsStoppingDecisionUnderStateGate()
    {
        var source = File.ReadAllText(FindSourceFile("VrcTwitchOscBridge", "Services", "BridgeCoordinator.cs"));

        foreach (var methodSignature in new[]
        {
            "private async Task RunGlitchyLoopAsync",
            "private async Task RunSupporterGrowthScaleSessionAsync"
        })
        {
            var body = NormalizeWhitespace(GetMethodBody(source, methodSignature));
            var lockIndex = body.LastIndexOf("lock (stateGate)", StringComparison.Ordinal);
            Assert.True(lockIndex >= 0, methodSignature);
            var stoppingIndex = body.IndexOf("isStopping", lockIndex, StringComparison.Ordinal);
            Assert.True(stoppingIndex > lockIndex, methodSignature);
            var removalIndex = body.IndexOf(".Remove", stoppingIndex, StringComparison.Ordinal);

            Assert.True(removalIndex > stoppingIndex, methodSignature);
            Assert.DoesNotContain("IsRuntimeStopping()", body, StringComparison.Ordinal);
        }
    }

    [Fact]
    public void ShutdownCoordinatorExceptionLogs_SanitizeExceptionMessages()
    {
        var source = File.ReadAllText(FindSourceFile("VrcTwitchOscBridge", "Services", "BridgeCoordinator.cs"));

        foreach (var methodSignature in new[]
        {
            "private async Task RunBridgeAsync",
            "private async Task RunValidationLoopAsync",
            "private async Task RunTriggerInfoAnnouncementLoopAsync",
            "private async Task WaitForPendingActivityResumeTasksAsync",
            "private async Task CompleteTrackedMovementCleanupAsync",
            "private async Task ResetPendingRulesCoreAsync",
            "private async Task RemoveAvatarScaleActivityResumeEntryAsync",
            "private async Task StopOscRouterSafelyAsync"
        })
        {
            var body = NormalizeWhitespace(GetMethodBody(source, methodSignature));
            Assert.DoesNotContain("{ex.Message}", body, StringComparison.Ordinal);
            Assert.DoesNotContain("{exception.Message}", body, StringComparison.Ordinal);
        }
    }

    [Fact]
    public async Task GlitchyWorker_StopWinningLeavesSessionAndCancellationForShutdownCapture()
    {
        var coordinator = new BridgeCoordinator(
            new DesktopInputLockService(Dispatcher.CurrentDispatcher),
            new WorldCommandBlacklistService(),
            new VrChatLocalOscCacheService());
        var rule = TestTriggerRuleSnapshotBuilder.Build() with
        {
            Name = "stopping glitchy float",
            ParameterName = "/avatar/parameters/StoppingGlitchyFloat"
        };
        var cancellation = new CancellationTokenSource();
        cancellation.Cancel();
        var session = CreatePrivateInstance("ActiveFloatGlitchyRedeemSessionState");
        SetProperty(session, "Rule", rule);
        SetProperty(session, "Address", rule.ParameterName);
        SetProperty(session, "ActiveUntil", DateTimeOffset.UtcNow.AddMinutes(1));
        SetProperty(session, "ResetValue", 0.35d);
        SetProperty(session, "CurrentValue", 0.8d);
        SetProperty(session, "CompletionCancellation", cancellation);
        SetProperty(session, "LeaseId", Guid.NewGuid());
        AddDictionaryValue(coordinator, "activeGlitchyRedeemSessions", rule.Id, session);
        SetField(coordinator, "isStopping", true);

        try
        {
            await InvokePrivateTaskAsync(coordinator, "RunGlitchyLoopAsync", session);

            Assert.Same(session, GetDictionaryValue(coordinator, "activeGlitchyRedeemSessions", rule.Id));
            Assert.True(cancellation.Token.CanBeCanceled);
        }
        finally
        {
            var sessions = Assert.IsAssignableFrom<IDictionary>(GetField(coordinator, "activeGlitchyRedeemSessions"));
            sessions.Remove(rule.Id);
            cancellation.Dispose();
        }
    }

    [Fact]
    public async Task GlitchyWorker_NormalCompletionRemovesAndDisposesSession()
    {
        var coordinator = new BridgeCoordinator(
            new DesktopInputLockService(Dispatcher.CurrentDispatcher),
            new WorldCommandBlacklistService(),
            new VrChatLocalOscCacheService());
        var rule = TestTriggerRuleSnapshotBuilder.Build() with
        {
            Name = "normal glitchy float",
            ParameterName = "/avatar/parameters/NormalGlitchyFloat"
        };
        var cancellation = new CancellationTokenSource();
        cancellation.Cancel();
        var session = CreatePrivateInstance("ActiveFloatGlitchyRedeemSessionState");
        SetProperty(session, "Rule", rule);
        SetProperty(session, "Address", rule.ParameterName);
        SetProperty(session, "ActiveUntil", DateTimeOffset.UtcNow.AddMinutes(1));
        SetProperty(session, "ResetValue", 0.35d);
        SetProperty(session, "CurrentValue", 0.8d);
        SetProperty(session, "CompletionCancellation", cancellation);
        SetProperty(session, "LeaseId", Guid.NewGuid());
        AddDictionaryValue(coordinator, "activeGlitchyRedeemSessions", rule.Id, session);

        await InvokePrivateTaskAsync(coordinator, "RunGlitchyLoopAsync", session);

        Assert.Null(GetDictionaryValue(coordinator, "activeGlitchyRedeemSessions", rule.Id));
        Assert.Throws<ObjectDisposedException>(() => _ = cancellation.Token);
    }

    [Fact]
    public async Task SupporterGrowthWorker_StopWinningLeavesStateAndCancellationForShutdownCapture()
    {
        var coordinator = new BridgeCoordinator(
            new DesktopInputLockService(Dispatcher.CurrentDispatcher),
            new WorldCommandBlacklistService(),
            new VrChatLocalOscCacheService());
        var rule = BridgeRuntimeConfiguration.CreateManualTestSnapshot(new AvatarScaleRule
        {
            Id = Guid.NewGuid(),
            Name = "stopping supporter growth",
            RestoreMode = AvatarScaleRestoreMode.ConfiguredHeight,
            RestoreHeightMeters = 1.55,
            ActiveTimeSeconds = 60
        });
        var cancellation = new CancellationTokenSource();
        cancellation.Cancel();
        var supporterState = CreatePrivateInstance("ActiveAvatarScaleSupporterGrowthState");
        SetProperty(supporterState, "NormalHeightMeters", 1.55d);
        SetProperty(supporterState, "SessionCancellation", cancellation);
        AddDictionaryValue(coordinator, "avatarScaleSupporterGrowthStates", rule.Id, supporterState);
        SetField(coordinator, "isStopping", true);

        var operationPriorityType = typeof(BridgeCoordinator).GetNestedType(
            "AvatarScaleOperationPriority",
            BindingFlags.NonPublic);
        Assert.NotNull(operationPriorityType);
        var operation = CreatePrivateInstance(
            "ActiveAvatarScaleOperationTicket",
            1L,
            rule.Id,
            rule.Name,
            Enum.Parse(operationPriorityType!, "SupporterGrowth"),
            false);
        var runtimeGeneration = Assert.IsType<long>(GetField(coordinator, "avatarScaleRuntimeGeneration"));

        try
        {
            await InvokePrivateTaskAsync(
                coordinator,
                "RunSupporterGrowthScaleSessionAsync",
                operation,
                rule,
                rule.Id,
                rule.Name,
                1.8d,
                1.55d,
                0d,
                false,
                cancellation,
                runtimeGeneration);

            Assert.Same(supporterState, GetDictionaryValue(
                coordinator,
                "avatarScaleSupporterGrowthStates",
                rule.Id));
            Assert.True(cancellation.Token.CanBeCanceled);
        }
        finally
        {
            var states = Assert.IsAssignableFrom<IDictionary>(GetField(
                coordinator,
                "avatarScaleSupporterGrowthStates"));
            states.Remove(rule.Id);
            cancellation.Dispose();
        }
    }

    [Fact]
    public async Task SupporterGrowthWorker_NormalCompletionRemovesAndDisposesState()
    {
        var coordinator = new BridgeCoordinator(
            new DesktopInputLockService(Dispatcher.CurrentDispatcher),
            new WorldCommandBlacklistService(),
            new VrChatLocalOscCacheService());
        var rule = BridgeRuntimeConfiguration.CreateManualTestSnapshot(new AvatarScaleRule
        {
            Id = Guid.NewGuid(),
            Name = "normal supporter growth",
            RestoreMode = AvatarScaleRestoreMode.ConfiguredHeight,
            RestoreHeightMeters = 1.55,
            ActiveTimeSeconds = 60
        });
        var cancellation = new CancellationTokenSource();
        cancellation.Cancel();
        var supporterState = CreatePrivateInstance("ActiveAvatarScaleSupporterGrowthState");
        SetProperty(supporterState, "NormalHeightMeters", 1.55d);
        SetProperty(supporterState, "SessionCancellation", cancellation);
        AddDictionaryValue(coordinator, "avatarScaleSupporterGrowthStates", rule.Id, supporterState);

        var operationPriorityType = typeof(BridgeCoordinator).GetNestedType(
            "AvatarScaleOperationPriority",
            BindingFlags.NonPublic);
        Assert.NotNull(operationPriorityType);
        var operation = CreatePrivateInstance(
            "ActiveAvatarScaleOperationTicket",
            1L,
            rule.Id,
            rule.Name,
            Enum.Parse(operationPriorityType!, "SupporterGrowth"),
            false);
        var runtimeGeneration = Assert.IsType<long>(GetField(coordinator, "avatarScaleRuntimeGeneration"));

        await InvokePrivateTaskAsync(
            coordinator,
            "RunSupporterGrowthScaleSessionAsync",
            operation,
            rule,
            rule.Id,
            rule.Name,
            1.8d,
            1.55d,
            0d,
            false,
            cancellation,
            runtimeGeneration);

        Assert.Null(GetDictionaryValue(coordinator, "avatarScaleSupporterGrowthStates", rule.Id));
        Assert.Throws<ObjectDisposedException>(() => _ = cancellation.Token);
    }

    [Fact]
    public async Task ShutdownReset_SendsCapturedFloatGlitchyScaleAndSupporterPlansWithCancellationIndependentFakeSends()
    {
        var coordinator = new BridgeCoordinator(
            new DesktopInputLockService(Dispatcher.CurrentDispatcher),
            new WorldCommandBlacklistService(),
            new VrChatLocalOscCacheService());
        var sentTokens = new List<CancellationToken>();
        SetField(
            coordinator,
            "testSendToVrChatAsync",
            (Func<byte[], CancellationToken, Task>)((_, cancellationToken) =>
            {
                sentTokens.Add(cancellationToken);
                return Task.CompletedTask;
            }));
        SetField(
            coordinator,
            "testForceReleaseDesktopInputAsync",
            (Func<CancellationToken, Task>)(_ => Task.CompletedTask));
        SetField(coordinator, "isStopping", true);

        var floatRule = TestTriggerRuleSnapshotBuilder.Build() with
        {
            Name = "active float",
            ParameterName = "/avatar/parameters/ActiveFloat"
        };
        var glitchyRule = TestTriggerRuleSnapshotBuilder.Build() with
        {
            Name = "glitchy float",
            ParameterName = "/avatar/parameters/GlitchyFloat"
        };
        AddDictionaryValue(
            coordinator,
            "activeFloatRedeemSessions",
            floatRule.Id,
            CreatePrivateInstance(
                "ActiveFloatRedeemSessionState",
                floatRule,
                "/avatar/parameters/ActiveFloat",
                0.9d,
                0.25d,
                string.Empty,
                DateTimeOffset.UtcNow.AddMinutes(1),
                new CancellationTokenSource(),
                Array.Empty<string>(),
                Guid.Empty,
                false,
                false));
        AddDictionaryValue(
            coordinator,
            "activeGlitchyRedeemSessions",
            glitchyRule.Id,
            CreatePrivateInstance("ActiveFloatGlitchyRedeemSessionState"));
        var glitchySession = GetDictionaryValue(coordinator, "activeGlitchyRedeemSessions", glitchyRule.Id)!;
        SetProperty(glitchySession, "Rule", glitchyRule);
        SetProperty(glitchySession, "Address", "/avatar/parameters/GlitchyFloat");
        SetProperty(glitchySession, "ResetValue", 0.35d);
        SetProperty(glitchySession, "CurrentValue", 0.8d);
        SetProperty(glitchySession, "CompletionCancellation", new CancellationTokenSource());
        SetProperty(glitchySession, "LeaseId", Guid.NewGuid());

        var scaleSequence = CreatePrivateInstance(
            "ActiveAvatarScaleRestoreSequenceState",
            1L,
            "avtr_shutdown",
            1.2d,
            1.65d,
            DateTimeOffset.UtcNow.AddMinutes(1),
            "scale restore",
            0d,
            true,
            false,
            null,
            0d,
            false,
            1.2d);
        SetField(coordinator, "activeAvatarScaleRestoreSequence", scaleSequence);
        SetField(coordinator, "avatarScaleRestoreSequenceCancellation", new CancellationTokenSource());

        var supporterState = CreatePrivateInstance("ActiveAvatarScaleSupporterGrowthState");
        SetProperty(supporterState, "NormalHeightMeters", 1.55d);
        SetProperty(supporterState, "SessionCancellation", new CancellationTokenSource());
        AddDictionaryValue(
            coordinator,
            "avatarScaleSupporterGrowthStates",
            Guid.NewGuid(),
            supporterState);

        var resetMethod = typeof(BridgeCoordinator).GetMethod(
            "ResetRuntimeEffectsBeforeOscShutdownAsync",
            BindingFlags.Instance | BindingFlags.NonPublic);
        Assert.NotNull(resetMethod);
        var resetTask = Assert.IsAssignableFrom<Task>(resetMethod!.Invoke(coordinator, null));
        await resetTask.WaitAsync(TimeSpan.FromSeconds(5));

        Assert.Equal(4, sentTokens.Count);
        Assert.All(sentTokens, cancellationToken => Assert.False(cancellationToken.CanBeCanceled));
    }

    [Fact]
    public async Task ShutdownReset_SkipsMismatchedAvatarScopedFloatAndPulsePlans()
    {
        var coordinator = CreateCoordinator();
        var sentTokens = new List<CancellationToken>();
        SetField(
            coordinator,
            "testSendToVrChatAsync",
            (Func<byte[], CancellationToken, Task>)((_, cancellationToken) =>
            {
                sentTokens.Add(cancellationToken);
                return Task.CompletedTask;
            }));
        SetField(coordinator, "testForceReleaseDesktopInputAsync", (Func<CancellationToken, Task>)(_ => Task.CompletedTask));
        SetField(coordinator, "isStopping", true);
        SetField(coordinator, "currentVrChatAvatarId", "avatar-other");
        AddSourceScopedShutdownFloatPlans(coordinator, "avatar-source");

        try
        {
            await InvokePrivateTaskAsync(coordinator, "ResetRuntimeEffectsBeforeOscShutdownAsync");

            Assert.Empty(sentTokens);
        }
        finally
        {
            InvokePrivate(coordinator, "ClearRuntimeState");
            await coordinator.DisposeAsync();
        }
    }

    [Fact]
    public async Task ShutdownReset_SendsMatchingAvatarScopedFloatAndPulsePlansWithCancellationIndependentSends()
    {
        var coordinator = CreateCoordinator();
        var sentTokens = new List<CancellationToken>();
        SetField(
            coordinator,
            "testSendToVrChatAsync",
            (Func<byte[], CancellationToken, Task>)((_, cancellationToken) =>
            {
                sentTokens.Add(cancellationToken);
                return Task.CompletedTask;
            }));
        SetField(coordinator, "testForceReleaseDesktopInputAsync", (Func<CancellationToken, Task>)(_ => Task.CompletedTask));
        SetField(coordinator, "isStopping", true);
        SetField(coordinator, "currentVrChatAvatarId", "avatar-source");
        AddSourceScopedShutdownFloatPlans(coordinator, "avatar-source");

        try
        {
            await InvokePrivateTaskAsync(coordinator, "ResetRuntimeEffectsBeforeOscShutdownAsync");

            Assert.Equal(3, sentTokens.Count);
            Assert.All(sentTokens, cancellationToken => Assert.False(cancellationToken.CanBeCanceled));
        }
        finally
        {
            InvokePrivate(coordinator, "ClearRuntimeState");
            await coordinator.DisposeAsync();
        }
    }

    [Fact]
    public async Task ShutdownReset_CapturesActualValuesThroughHeldGateBeforeTransportClose()
    {
        var coordinator = new BridgeCoordinator(
            new DesktopInputLockService(Dispatcher.CurrentDispatcher),
            new WorldCommandBlacklistService(),
            new VrChatLocalOscCacheService());
        var capturedPackets = new List<(string Address, float Value)>();
        var events = new List<string>();
        var transportClosed = 0;
        SetField(
            coordinator,
            "testSendToVrChatAsync",
            (Func<byte[], CancellationToken, Task>)((packet, _) =>
            {
                Assert.Equal(0, Volatile.Read(ref transportClosed));
                var captured = ReadFloatPacket(packet);
                capturedPackets.Add(captured);
                events.Add($"send:{captured.Address}:{captured.Value.ToString("R", System.Globalization.CultureInfo.InvariantCulture)}");
                return Task.CompletedTask;
            }));
        SetField(
            coordinator,
            "testStopOscRouterAsync",
            (Func<Task>)(() =>
            {
                Interlocked.Exchange(ref transportClosed, 1);
                events.Add("transport-close");
                return Task.CompletedTask;
            }));
        SetField(
            coordinator,
            "testForceReleaseDesktopInputAsync",
            (Func<CancellationToken, Task>)(_ => Task.CompletedTask));

        var floatRule = TestTriggerRuleSnapshotBuilder.Build() with
        {
            Name = "captured active float",
            ParameterName = "/avatar/parameters/CapturedActiveFloat"
        };
        var floatSession = CreatePrivateInstance(
            "ActiveFloatRedeemSessionState",
            floatRule,
            "/avatar/parameters/CapturedActiveFloat",
            0.9d,
            0.25d,
            string.Empty,
            DateTimeOffset.UtcNow.AddMinutes(1),
            new CancellationTokenSource(),
            Array.Empty<string>(),
            Guid.Empty,
            false,
            false);
        AddDictionaryValue(coordinator, "activeFloatRedeemSessions", floatRule.Id, floatSession);
        var heldSendGate = GetProperty<SemaphoreSlim>(floatSession, "SendGate");
        Assert.True(heldSendGate.Wait(0));

        var glitchyRule = TestTriggerRuleSnapshotBuilder.Build() with
        {
            Name = "captured glitchy float",
            ParameterName = "/avatar/parameters/CapturedGlitchyFloat"
        };
        AddDictionaryValue(
            coordinator,
            "activeGlitchyRedeemSessions",
            glitchyRule.Id,
            CreatePrivateInstance("ActiveFloatGlitchyRedeemSessionState"));
        var glitchySession = GetDictionaryValue(coordinator, "activeGlitchyRedeemSessions", glitchyRule.Id)!;
        SetProperty(glitchySession, "Rule", glitchyRule);
        SetProperty(glitchySession, "Address", "/avatar/parameters/CapturedGlitchyFloat");
        SetProperty(glitchySession, "ResetValue", 0.35d);
        SetProperty(glitchySession, "CurrentValue", 0.8d);
        SetProperty(glitchySession, "CompletionCancellation", new CancellationTokenSource());
        SetProperty(glitchySession, "LeaseId", Guid.NewGuid());

        var scaleSequence = CreatePrivateInstance(
            "ActiveAvatarScaleRestoreSequenceState",
            1L,
            "avtr_shutdown",
            1.2d,
            1.65d,
            DateTimeOffset.UtcNow.AddMinutes(1),
            "captured scale restore",
            0d,
            true,
            false,
            null,
            0d,
            false,
            1.2d);
        SetField(coordinator, "activeAvatarScaleRestoreSequence", scaleSequence);
        SetField(coordinator, "avatarScaleRestoreSequenceCancellation", new CancellationTokenSource());

        var supporterRuleId = Guid.NewGuid();
        var supporterState = CreatePrivateInstance("ActiveAvatarScaleSupporterGrowthState");
        SetProperty(supporterState, "NormalHeightMeters", 1.55d);
        SetProperty(supporterState, "SessionCancellation", new CancellationTokenSource());
        AddDictionaryValue(coordinator, "avatarScaleSupporterGrowthStates", supporterRuleId, supporterState);

        var stopTask = coordinator.StopAsync();
        try
        {
            Assert.False(stopTask.IsCompleted);
            Assert.Empty(capturedPackets);
            events.Add("gate-release");
        }
        finally
        {
            heldSendGate.Release();
        }

        await stopTask.WaitAsync(TimeSpan.FromSeconds(5));

        Assert.Equal(4, capturedPackets.Count);
        Assert.Equal(
            [
                ("/avatar/parameters/CapturedActiveFloat", 0.25f),
                ("/avatar/parameters/CapturedGlitchyFloat", 0.35f),
                ("/avatar/eyeheight", 1.65f),
                ("/avatar/eyeheight", 1.55f)
            ],
            capturedPackets);
        var transportCloseIndex = events.IndexOf("transport-close");
        var gateReleaseIndex = events.IndexOf("gate-release");
        Assert.True(gateReleaseIndex >= 0);
        Assert.Equal(1, Volatile.Read(ref transportClosed));
        Assert.True(transportCloseIndex == events.Count - 1);
        Assert.All(events.Take(transportCloseIndex), entry => Assert.NotEqual("transport-close", entry));
        Assert.True(events.FindIndex(entry => entry.StartsWith("send:", StringComparison.Ordinal)) > gateReleaseIndex);
    }

    private static TaskCompletionSource NewSignal() =>
        new(TaskCreationOptions.RunContinuationsAsynchronously);

    private static async Task InvokeSendPacketsAsync(
        BridgeCoordinator coordinator,
        MethodInfo sendPackets)
    {
        var task = Assert.IsAssignableFrom<Task>(sendPackets.Invoke(
            coordinator,
            new object?[] { new[] { new byte[] { 1 } }, CancellationToken.None, null }));
        await task;
    }

    private static async Task InvokePrivateTaskAsync(
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

    private static void SetField(object instance, string name, object? value)
    {
        var field = instance.GetType().GetField(name, BindingFlags.Instance | BindingFlags.NonPublic);
        Assert.NotNull(field);
        field.SetValue(instance, value);
    }

    private static object? GetField(object instance, string name)
    {
        var field = instance.GetType().GetField(name, BindingFlags.Instance | BindingFlags.NonPublic);
        Assert.NotNull(field);
        return field!.GetValue(instance);
    }

    private static T GetProperty<T>(object instance, string name)
    {
        var property = instance.GetType().GetProperty(
            name,
            BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic);
        Assert.NotNull(property);
        return Assert.IsType<T>(property!.GetValue(instance));
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

    private static BridgeCoordinator CreateCoordinator() => new(
        new DesktopInputLockService(Dispatcher.CurrentDispatcher),
        new WorldCommandBlacklistService(),
        new VrChatLocalOscCacheService());

    private static void AddSourceScopedShutdownFloatPlans(
        BridgeCoordinator coordinator,
        string sourceAvatarId)
    {
        var floatRule = TestTriggerRuleSnapshotBuilder.Build() with
        {
            Name = "shutdown source float",
            AvatarProfileId = Guid.NewGuid(),
            ParameterName = "/avatar/parameters/ShutdownSourceFloat"
        };
        var activeFloat = CreatePrivateInstance(
            "ActiveFloatRedeemSessionState",
            floatRule,
            floatRule.ParameterName,
            0.9d,
            0.25d,
            sourceAvatarId,
            DateTimeOffset.UtcNow.AddMinutes(1),
            new CancellationTokenSource(),
            Array.Empty<string>(),
            Guid.NewGuid(),
            false,
            false);
        AddDictionaryValue(coordinator, "activeFloatRedeemSessions", floatRule.Id, activeFloat);

        var glitchyRule = TestTriggerRuleSnapshotBuilder.Build() with
        {
            Name = "shutdown source glitchy float",
            AvatarProfileId = Guid.NewGuid(),
            ParameterName = "/avatar/parameters/ShutdownSourceGlitchyFloat"
        };
        var glitchy = CreatePrivateInstance("ActiveFloatGlitchyRedeemSessionState");
        SetProperty(glitchy, "Rule", glitchyRule);
        SetProperty(glitchy, "Address", glitchyRule.ParameterName);
        SetProperty(glitchy, "ResetValue", 0.35d);
        SetProperty(glitchy, "CurrentValue", 0.8d);
        SetProperty(glitchy, "SourceAvatarId", sourceAvatarId);
        SetProperty(glitchy, "CompletionCancellation", new CancellationTokenSource());
        SetProperty(glitchy, "LeaseId", Guid.NewGuid());
        AddDictionaryValue(coordinator, "activeGlitchyRedeemSessions", glitchyRule.Id, glitchy);

        var pulseRule = TestTriggerRuleSnapshotBuilder.Build() with
        {
            Name = "shutdown source pulse",
            AvatarProfileId = Guid.NewGuid(),
            ParameterName = "/avatar/parameters/ShutdownSourcePulse"
        };
        AddDictionaryValue(
            coordinator,
            "activeFloatPulseRestores",
            pulseRule.Id,
            CreatePrivateInstance(
                "ActiveFloatPulseRestoreState",
                pulseRule.Id,
                pulseRule.Name,
                pulseRule.ParameterName,
                0.15d,
                sourceAvatarId,
                new CancellationTokenSource(),
                Guid.NewGuid()));
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
        var dictionary = Assert.IsAssignableFrom<System.Collections.IDictionary>(GetField(instance, fieldName));
        dictionary.Add(key, value);
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

    private static (string Address, float Value) ReadFloatPacket(byte[] packet)
    {
        var addressTerminator = Array.IndexOf(packet, (byte)0);
        Assert.True(addressTerminator > 0);
        var address = Encoding.UTF8.GetString(packet, 0, addressTerminator);
        var typeStart = AlignOscSegment(addressTerminator + 1);
        var typeTerminator = Array.IndexOf(packet, (byte)0, typeStart);
        Assert.True(typeTerminator > typeStart);
        Assert.Equal(",f", Encoding.UTF8.GetString(packet, typeStart, typeTerminator - typeStart));
        var valueStart = AlignOscSegment(typeTerminator + 1);
        return (address, BinaryPrimitives.ReadSingleBigEndian(packet.AsSpan(valueStart, sizeof(float))));
    }

    private static int AlignOscSegment(int length) => (length + 3) & ~3;

    private static object? GetDictionaryValue(object instance, string fieldName, object key)
    {
        var dictionary = Assert.IsAssignableFrom<System.Collections.IDictionary>(GetField(instance, fieldName));
        return dictionary[key];
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
