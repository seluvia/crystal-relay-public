using System.Collections;
using System.IO;
using System.Reflection;
using System.Windows.Threading;
using VrcTwitchOscBridge.Models;
using VrcTwitchOscBridge.Services;
using Xunit;

namespace VrcTwitchOscBridge.Tests;

public sealed class BridgeCoordinatorDevScaleSetTests
{
    [Fact]
    public void ExecuteDevSetAvatarScale_UsesAtomicEmergencyOperationStart()
    {
        var source = File.ReadAllText(FindSourceFile("VrcTwitchOscBridge", "Services", "BridgeCoordinator.cs"));
        var executeBody = GetMethodBody(source, "private async Task ExecuteDevSetAvatarScaleAsync");

        Assert.Contains("TryBeginAvatarScaleOperationForDevScaleSet", executeBody, StringComparison.Ordinal);
    }

    [Fact]
    public void ResumeActivity_UsesStopDrainedAdmissionGateBeforeStopClearsRuntimeState()
    {
        var source = File.ReadAllText(FindSourceFile("VrcTwitchOscBridge", "Services", "BridgeCoordinator.cs"));
        var resumeBody = GetMethodBody(source, "private async Task ResumeActivityAsync(");
        var stopBody = GetMethodBody(source, "public async Task StopAsync()");

        Assert.Contains("EnterActivityResumeGateUser()", resumeBody, StringComparison.Ordinal);
        Assert.Contains("await activityResumeGate.WaitAsync", resumeBody, StringComparison.Ordinal);
        Assert.Contains("activityResumeGate.Release()", resumeBody, StringComparison.Ordinal);
        Assert.Contains("ExitActivityResumeGateUser()", resumeBody, StringComparison.Ordinal);

        var stopWaitIndex = stopBody.IndexOf(
            "await WaitForActivityResumeGateUsersAsync()",
            StringComparison.Ordinal);
        Assert.True(stopWaitIndex >= 0);

        foreach (var stateClear in new[] { "ClearActiveConfiguration()", "ClearRuntimeState()" })
        {
            var stateClearIndex = stopBody.IndexOf(stateClear, StringComparison.Ordinal);
            Assert.True(stateClearIndex > stopWaitIndex, stateClear);
        }
    }

    [Fact]
    public void ResumeTaskCompletion_ObservesExceptionBeforeRemovingTrackedTask()
    {
        var source = File.ReadAllText(FindSourceFile("VrcTwitchOscBridge", "Services", "BridgeCoordinator.cs"));
        var resumeEntryBody = GetMethodBody(source, "public Task TryResumePendingActivitiesAsync()");
        var exceptionIndex = resumeEntryBody.IndexOf(
            "completedTask.Exception",
            StringComparison.Ordinal);
        var removeIndex = resumeEntryBody.IndexOf(
            "pendingActivityResumeTasks.Remove(completedTask)",
            StringComparison.Ordinal);

        Assert.True(exceptionIndex >= 0);
        Assert.True(removeIndex > exceptionIndex);
    }

    [Fact]
    public void ExecuteDevSetAvatarScale_ObservesCancellationBeforeEmergencyInvalidation()
    {
        var source = File.ReadAllText(FindSourceFile("VrcTwitchOscBridge", "Services", "BridgeCoordinator.cs"));
        var executeBody = GetMethodBody(source, "private async Task ExecuteDevSetAvatarScaleAsync");
        var permissionIndex = executeBody.IndexOf(
            "await TryGetAvatarScalingAllowedAsync(cancellationToken)",
            StringComparison.Ordinal);
        var cancellationIndex = executeBody.IndexOf(
            "cancellationToken.ThrowIfCancellationRequested();",
            permissionIndex,
            StringComparison.Ordinal);
        var operationIndex = executeBody.IndexOf(
            "TryBeginAvatarScaleOperationForDevScaleSet",
            permissionIndex,
            StringComparison.Ordinal);

        Assert.True(permissionIndex >= 0);
        Assert.True(cancellationIndex > permissionIndex);
        Assert.True(operationIndex > cancellationIndex);
    }

    [Fact]
    public void ExecuteAvatarScaleRule_FencesFirstSendStateCommitToCurrentOperation()
    {
        var source = File.ReadAllText(FindSourceFile("VrcTwitchOscBridge", "Services", "BridgeCoordinator.cs"));
        var executeBody = GetMethodBody(source, "private async Task<bool> ExecuteAvatarScaleRuleAsync");
        var firstStateWrite = executeBody.IndexOf(
            "firstScaleSendStarted = true",
            StringComparison.Ordinal);
        var currentOperationCheck = executeBody.LastIndexOf(
            "IsAvatarScaleOperationCurrent(operation)",
            firstStateWrite,
            StringComparison.Ordinal);

        Assert.True(firstStateWrite >= 0);
        Assert.True(currentOperationCheck >= 0);
    }

    [Fact]
    public void ExecuteAvatarScaleRule_UsesAtomicContinuationCommitAfterSuccessfulSend()
    {
        var source = File.ReadAllText(FindSourceFile("VrcTwitchOscBridge", "Services", "BridgeCoordinator.cs"));
        var executeBody = GetMethodBody(source, "private async Task<bool> ExecuteAvatarScaleRuleAsync");
        var sendIndex = executeBody.IndexOf(
            "SendAvatarHeightForOperationAsync",
            StringComparison.Ordinal);
        var continuationIndex = executeBody.IndexOf(
            "CommitAvatarScaleContinuationAsync",
            sendIndex,
            StringComparison.Ordinal);
        var directRestoreIndex = executeBody.IndexOf(
            "ScheduleAvatarScaleRestoreSequence",
            sendIndex,
            StringComparison.Ordinal);

        Assert.True(sendIndex >= 0);
        Assert.True(continuationIndex > sendIndex);
        Assert.True(directRestoreIndex < 0);
    }

    [Fact]
    public void TryBeginAvatarScaleOperation_UsesTheAvatarScaleWriteGate()
    {
        var source = File.ReadAllText(FindSourceFile("VrcTwitchOscBridge", "Services", "BridgeCoordinator.cs"));
        var operationBody = GetMethodBody(
            source,
            "private async Task<ActiveAvatarScaleOperationTicket?> TryBeginAvatarScaleOperation(");

        Assert.Contains("EnterAvatarScaleWriteGateUser", operationBody, StringComparison.Ordinal);
        Assert.Contains("await avatarScaleWriteGate.WaitAsync", operationBody, StringComparison.Ordinal);
        Assert.Contains("avatarScaleWriteGate.Release()", operationBody, StringComparison.Ordinal);
    }

    [Fact]
    public void TryGetCurrentAvatarHeight_DoesNotObserveQueryValueWhileStateGateIsHeld()
    {
        var source = File.ReadAllText(FindSourceFile("VrcTwitchOscBridge", "Services", "BridgeCoordinator.cs"));
        var queryBody = GetMethodBody(
            source,
            "private async Task<double?> TryGetCurrentAvatarHeightAsync");
        var queryResultIndex = queryBody.IndexOf(
            "var observedValue = await oscRouterService.GetCurrentOscValueAsync",
            StringComparison.Ordinal);
        var observeIndex = queryBody.IndexOf(
            "ObserveOscValue(observedValue)",
            queryResultIndex,
            StringComparison.Ordinal);
        var lockIndex = queryBody.IndexOf("lock (stateGate)", queryResultIndex, StringComparison.Ordinal);

        Assert.True(queryResultIndex >= 0);
        Assert.True(observeIndex >= 0);
        Assert.True(lockIndex >= 0);
        var openingBrace = queryBody.IndexOf('{', lockIndex);
        Assert.True(openingBrace >= 0);
        var closingBrace = FindMatchingBrace(queryBody, openingBrace);

        Assert.False(
            observeIndex > openingBrace && observeIndex < closingBrace,
            "The query observation must notify outside the state gate.");
    }

    [Fact]
    public async Task NormalAvatarScaleContinuation_IsAtomicAgainstEmergencyInvalidation()
    {
        var activityResume = new BlockingActivityResumeService();
        var coordinator = CreateCoordinator(activityResume);
        var operation = await InvokePrivateAsync(
            coordinator,
            "TryBeginAvatarScaleOperation",
            Guid.NewGuid(),
            "Normal Scale",
            Enum.Parse(
                typeof(BridgeCoordinator).GetNestedType("AvatarScaleOperationPriority", BindingFlags.NonPublic)!,
                "LiveRedeem"),
            false,
            CancellationToken.None,
            null);
        Assert.NotNull(operation);

        var runtimeGeneration = (long)InvokePrivate(
            coordinator,
            "GetAvatarScaleRuntimeGeneration")!;
        var rule = BridgeRuntimeConfiguration.CreateManualTestSnapshot(new AvatarScaleRule
        {
            Id = Guid.NewGuid(),
            Name = "Normal Scale",
            ActiveTimeSeconds = 30,
            RestoreMode = AvatarScaleRestoreMode.ConfiguredHeight,
            RestoreHeightMeters = 1.6
        });

        var continuationTask = Assert.IsAssignableFrom<Task<bool>>(InvokePrivate(
            coordinator,
            "CommitAvatarScaleContinuationAsync",
            operation,
            runtimeGeneration,
            rule,
            false,
            false,
            2.25,
            1.6,
            CancellationToken.None));
        await activityResume.ActivityStarted.Task.WaitAsync(TimeSpan.FromSeconds(5));

        var emergencyTask = Task.Run(() => InvokePrivateAsync(
            coordinator,
            "TryBeginAvatarScaleOperationForDevScaleSet",
            CancellationToken.None));
        await Task.Delay(50);
        Assert.False(emergencyTask.IsCompleted);

        activityResume.ReleaseActivityStarted();
        Assert.True(await continuationTask.WaitAsync(TimeSpan.FromSeconds(5)));
        var emergencyOperation = await emergencyTask.WaitAsync(TimeSpan.FromSeconds(5));

        Assert.NotNull(emergencyOperation);
        Assert.Null(GetField(coordinator, "activeAvatarScaleRestoreSequence"));
        Assert.DoesNotContain(
            activityResume.Activities,
            activity => activity.RuleId == rule.Id && activity.Type == ResumeActivityType.AvatarScale);
        await coordinator.StopAsync().WaitAsync(TimeSpan.FromSeconds(5));
    }

    [Fact]
    public async Task StaleAvatarScaleContinuation_DoesNotScheduleRestoreOrResumeActivity()
    {
        var activityResume = new RecordingActivityResumeService();
        await using var coordinator = CreateCoordinator(activityResume);
        var operation = await InvokePrivateAsync(
            coordinator,
            "TryBeginAvatarScaleOperation",
            Guid.NewGuid(),
            "Stale Scale",
            Enum.Parse(
                typeof(BridgeCoordinator).GetNestedType("AvatarScaleOperationPriority", BindingFlags.NonPublic)!,
                "LiveRedeem"),
            false,
            CancellationToken.None,
            null);
        Assert.NotNull(operation);

        var runtimeGeneration = (long)InvokePrivate(
            coordinator,
            "GetAvatarScaleRuntimeGeneration")!;
        _ = await InvokePrivateAsync(
            coordinator,
            "TryBeginAvatarScaleOperationForDevScaleSet",
            CancellationToken.None);

        var rule = BridgeRuntimeConfiguration.CreateManualTestSnapshot(new AvatarScaleRule
        {
            Id = Guid.NewGuid(),
            Name = "Stale Scale",
            ActiveTimeSeconds = 30,
            RestoreMode = AvatarScaleRestoreMode.ConfiguredHeight,
            RestoreHeightMeters = 1.6
        });
        var continuationTask = Assert.IsAssignableFrom<Task<bool>>(InvokePrivate(
            coordinator,
            "CommitAvatarScaleContinuationAsync",
            operation,
            runtimeGeneration,
            rule,
            false,
            false,
            2.25,
            1.6,
            CancellationToken.None));

        Assert.False(await continuationTask);
        Assert.Null(GetField(coordinator, "activeAvatarScaleRestoreSequence"));
        Assert.Empty(activityResume.Activities);
    }

    [Fact]
    public async Task EmergencyInvalidation_ClearsAffectedAvatarScaleResumeIdsOnly()
    {
        var affectedRuleId = Guid.NewGuid();
        var secondAffectedRuleId = Guid.NewGuid();
        var unrelatedRuleId = Guid.NewGuid();
        var activityResume = new RecordingActivityResumeService();
        activityResume.Activities.AddRange(
        [
            new ResumeActivity { Type = ResumeActivityType.AvatarScale, RuleId = affectedRuleId },
            new ResumeActivity { Type = ResumeActivityType.AvatarScale, RuleId = secondAffectedRuleId },
            new ResumeActivity { Type = ResumeActivityType.Movement, RuleId = unrelatedRuleId }
        ]);
        await using var coordinator = CreateCoordinator(activityResume);

        var activeEffects = (IDictionary)GetField(coordinator, "activeAvatarScaleEffects");
        activeEffects[affectedRuleId] = DateTimeOffset.UtcNow.AddMinutes(1);
        activeEffects[secondAffectedRuleId] = DateTimeOffset.UtcNow.AddMinutes(1);

        _ = await InvokePrivateAsync(
            coordinator,
            "TryBeginAvatarScaleOperationForDevScaleSet",
            CancellationToken.None);
        await activityResume.WaitForActivityEndAsync(affectedRuleId);
        await activityResume.WaitForActivityEndAsync(secondAffectedRuleId);

        Assert.DoesNotContain(activityResume.Activities, activity => activity.RuleId == affectedRuleId);
        Assert.DoesNotContain(activityResume.Activities, activity => activity.RuleId == secondAffectedRuleId);
        Assert.Contains(activityResume.Activities, activity =>
            activity.RuleId == unrelatedRuleId && activity.Type == ResumeActivityType.Movement);
    }

    [Fact]
    public async Task EmergencyInvalidation_RemovesPersistedOnlyAvatarScaleResumeEntry()
    {
        var persistedOnlyRuleId = Guid.NewGuid();
        var unrelatedMovementRuleId = Guid.NewGuid();
        var activityResume = new CleanupTrackingActivityResumeService();
        activityResume.Activities.AddRange(
        [
            new ResumeActivity
            {
                Type = ResumeActivityType.AvatarScale,
                RuleId = persistedOnlyRuleId
            },
            new ResumeActivity
            {
                Type = ResumeActivityType.Movement,
                RuleId = unrelatedMovementRuleId
            }
        ]);
        var coordinator = CreateCoordinator(activityResume);
        try
        {
            _ = await InvokePrivateAsync(
                coordinator,
                "TryBeginAvatarScaleOperationForDevScaleSet",
                CancellationToken.None);

            var cleanupObserved = await Task.WhenAny(
                activityResume.RemovalAttempted.Task,
                Task.Delay(TimeSpan.FromSeconds(1)));
            Assert.Same(activityResume.RemovalAttempted.Task, cleanupObserved);

            Assert.DoesNotContain(
                activityResume.Activities,
                activity => activity.RuleId == persistedOnlyRuleId && activity.Type == ResumeActivityType.AvatarScale);
            Assert.Contains(
                activityResume.Activities,
                activity => activity.RuleId == unrelatedMovementRuleId && activity.Type == ResumeActivityType.Movement);
        }
        finally
        {
            await coordinator.StopAsync().WaitAsync(TimeSpan.FromSeconds(5));
        }
    }

    [Fact]
    public async Task EmergencyInvalidation_PreservesNewerSameRuleAndUnrelatedActivities()
    {
        var ruleId = Guid.NewGuid();
        var oldActivity = new ResumeActivity
        {
            Type = ResumeActivityType.AvatarScale,
            RuleId = ruleId
        };
        var activityResume = new RacingActivityResumeService();
        activityResume.Activities.Add(oldActivity);
        await using var coordinator = CreateCoordinator(activityResume);

        var activeEffects = (IDictionary)GetField(coordinator, "activeAvatarScaleEffects");
        activeEffects[ruleId] = DateTimeOffset.UtcNow.AddMinutes(1);

        Assert.NotNull(await InvokePrivateAsync(
            coordinator,
            "TryBeginAvatarScaleOperationForDevScaleSet",
            CancellationToken.None));
        Assert.Contains(activityResume.NewerActivity, activityResume.Activities);
        Assert.Contains(activityResume.UnrelatedActivity, activityResume.Activities);
        Assert.DoesNotContain(oldActivity, activityResume.Activities);
    }

    [Fact]
    public async Task EmergencyInvalidation_ClearsActivityForActiveOperationRuleId()
    {
        var ruleId = Guid.NewGuid();
        var activityResume = new RecordingActivityResumeService();
        activityResume.Activities.Add(new ResumeActivity
        {
            Type = ResumeActivityType.AvatarScale,
            RuleId = ruleId
        });
        await using var coordinator = CreateCoordinator(activityResume);

        var liveRedeemPriority = Enum.Parse(
            typeof(BridgeCoordinator).GetNestedType("AvatarScaleOperationPriority", BindingFlags.NonPublic)!,
            "LiveRedeem");
        Assert.NotNull(await InvokePrivateAsync(
            coordinator,
            "TryBeginAvatarScaleOperation",
            ruleId,
            "Active Scale",
            liveRedeemPriority,
            false,
            CancellationToken.None,
            null));

        Assert.NotNull(await InvokePrivateAsync(
            coordinator,
            "TryBeginAvatarScaleOperationForDevScaleSet",
            CancellationToken.None));

        Assert.DoesNotContain(activityResume.Activities, activity => activity.RuleId == ruleId);
    }

    [Fact]
    public async Task PendingRestoreCompletion_PreservesNewerStateAfterRuntimeInvalidation()
    {
        await using var coordinator = CreateCoordinator();
        SetField(coordinator, "currentVrChatAvatarId", "avtr_test");

        var pendingRestores = (IDictionary)GetField(coordinator, "pendingAvatarScaleHeightRestores");
        var pendingRestoreType = typeof(BridgeCoordinator).GetNestedType(
            "PendingAvatarScaleHeightRestoreState",
            BindingFlags.NonPublic)!;
        var oldRestore = Activator.CreateInstance(
            pendingRestoreType,
            BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic,
            binder: null,
            args: [1.25, DateTimeOffset.UtcNow.AddMinutes(1), "Old Scale", Guid.NewGuid()],
            culture: null)!;
        pendingRestores["avtr_test"] = oldRestore;

        var idleRestorePriority = Enum.Parse(
            typeof(BridgeCoordinator).GetNestedType("AvatarScaleOperationPriority", BindingFlags.NonPublic)!,
            "IdleRestore");
        var operation = await InvokePrivateAsync(
            coordinator,
            "TryBeginAvatarScaleOperation",
            Guid.NewGuid(),
            "Pending Restore",
            idleRestorePriority,
            false,
            CancellationToken.None,
            null);
        Assert.NotNull(operation);
        var operationId = (long)GetProperty(operation!, "OperationId");
        var runtimeGeneration = (long)InvokePrivate(coordinator, "GetAvatarScaleRuntimeGeneration")!;

        var sequenceType = typeof(BridgeCoordinator).GetNestedType(
            "ActiveAvatarScaleRestoreSequenceState",
            BindingFlags.NonPublic)!;
        var sequence = Activator.CreateInstance(
            sequenceType,
            BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic,
            binder: null,
            args:
            [
                9L,
                "avtr_test",
                1.7,
                1.6,
                DateTimeOffset.UtcNow.AddMinutes(1),
                "Pending Restore",
                0d,
                false,
                false,
                null,
                0d,
                false,
                1.6
            ],
            culture: null)!;
        SetField(coordinator, "activeAvatarScaleRestoreSequence", sequence);

        Assert.NotNull(await InvokePrivateAsync(
            coordinator,
            "TryBeginAvatarScaleOperationForDevScaleSet",
            CancellationToken.None));

        var newerRestore = Activator.CreateInstance(
            pendingRestoreType,
            BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic,
            binder: null,
            args: [1.8, DateTimeOffset.UtcNow.AddMinutes(2), "New Scale", Guid.NewGuid()],
            culture: null)!;
        pendingRestores["avtr_test"] = newerRestore;

        var removed = (bool)InvokePrivate(
            coordinator,
            "TryRemovePendingAvatarScaleHeightRestore",
            "avtr_test",
            oldRestore,
            9L,
            operationId,
            runtimeGeneration,
            null,
            null,
            null)!;

        Assert.False(removed);
        Assert.Same(newerRestore, pendingRestores["avtr_test"]);
    }

    [Fact]
    public void ResumeAvatarScaleActivity_UsesRuntimeGenerationAtEveryWriteBoundary()
    {
        var source = File.ReadAllText(FindSourceFile("VrcTwitchOscBridge", "Services", "BridgeCoordinator.cs"));
        var resumeBody = GetMethodBody(source, "private async Task ResumePendingActivitiesSingleFlightAsync");
        var activityBody = GetMethodBody(source, "private async Task ResumeActivityCoreAsync");

        Assert.Contains(
            "avatarScaleRuntimeGeneration = await WaitForAvatarScaleActivityResumeCleanupAsync(cancellationToken);",
            resumeBody,
            StringComparison.Ordinal);
        Assert.Contains(
            "await ResumeActivityAsync(\n                    activity,\n                    avatarScaleRuntimeGeneration,\n                    expectedSessionGeneration);",
            resumeBody,
            StringComparison.Ordinal);
        Assert.Contains("expectedRuntimeGeneration", activityBody, StringComparison.Ordinal);
        Assert.Contains("IsAvatarScaleRuntimeGenerationCurrent(expectedRuntimeGeneration)", activityBody, StringComparison.Ordinal);
        Assert.Contains("TryGetCurrentAvatarHeightAsync(", activityBody, StringComparison.Ordinal);
        Assert.Contains("cancellationToken", activityBody, StringComparison.Ordinal);
        Assert.Contains("expectedRuntimeGeneration", activityBody, StringComparison.Ordinal);
        Assert.Contains("expectedRuntimeGeneration: expectedRuntimeGeneration", activityBody, StringComparison.Ordinal);
        Assert.Contains("expectedRuntimeGeneration)", activityBody, StringComparison.Ordinal);
    }

    [Fact]
    public void PendingRestoreCompletion_UsesSequenceOperationAndGenerationIdentity()
    {
        var source = File.ReadAllText(FindSourceFile("VrcTwitchOscBridge", "Services", "BridgeCoordinator.cs"));
        var restoreBody = GetMethodBody(source, "private async Task RunAvatarScaleRestoreSequenceAsync");
        var supporterBody = GetMethodBody(source, "private async Task RunSupporterGrowthScaleSessionAsync");
        var pendingBody = GetMethodBody(source, "private async Task RestorePendingAvatarScaleHeightForCurrentAvatarAsync");

        Assert.Contains("TryRemovePendingAvatarScaleHeightRestore", restoreBody, StringComparison.Ordinal);
        Assert.Contains("operation.OperationId", restoreBody, StringComparison.Ordinal);
        Assert.Contains("sequence.RuntimeGeneration", restoreBody, StringComparison.Ordinal);
        Assert.Contains("TryRemovePendingAvatarScaleHeightRestore", supporterBody, StringComparison.Ordinal);
        Assert.Contains("sessionCancellation", supporterBody, StringComparison.Ordinal);
        Assert.Contains("TryRemovePendingAvatarScaleHeightRestore", pendingBody, StringComparison.Ordinal);
        Assert.Contains("operation.OperationId", pendingBody, StringComparison.Ordinal);
        Assert.Contains("runtimeGeneration", pendingBody, StringComparison.Ordinal);
    }

    [Fact]
    public void RunAvatarScaleRestoreSequence_CompletesActivityResumeForCurrentSequenceRule()
    {
        var source = File.ReadAllText(FindSourceFile("VrcTwitchOscBridge", "Services", "BridgeCoordinator.cs"));
        var restoreBody = GetMethodBody(source, "private async Task RunAvatarScaleRestoreSequenceAsync");
        var sendIndex = restoreBody.IndexOf(
            "if (!await SendAvatarHeightForOperationAsync(",
            StringComparison.Ordinal);
        var completionIndex = restoreBody.IndexOf(
            "if (expectedPendingRestore is not null)",
            sendIndex,
            StringComparison.Ordinal);
        var returnIndex = restoreBody.IndexOf("return;", completionIndex, StringComparison.Ordinal);
        var completionBody = completionIndex >= 0 && returnIndex > completionIndex
            ? restoreBody[completionIndex..returnIndex]
            : string.Empty;

        Assert.True(sendIndex >= 0);
        Assert.True(completionIndex > sendIndex);
        Assert.Contains("RecordActivityEndedAsync", completionBody, StringComparison.Ordinal);
        Assert.Contains("sequence.Rule", completionBody, StringComparison.Ordinal);
    }

    [Fact]
    public async Task EmergencyInvalidation_SwallowsActivityResumeCleanupFailures()
    {
        var activityResume = new ThrowingActivityResumeService();
        var coordinator = CreateCoordinator(activityResume);
        var activeEffects = (IDictionary)GetField(coordinator, "activeAvatarScaleEffects");
        var ruleId = Guid.NewGuid();
        activeEffects[ruleId] = DateTimeOffset.UtcNow.AddMinutes(1);
        activityResume.Activities.Add(new ResumeActivity
        {
            Type = ResumeActivityType.AvatarScale,
            RuleId = ruleId
        });

        var exception = await Record.ExceptionAsync(() => InvokePrivateAsync(
            coordinator,
            "TryBeginAvatarScaleOperationForDevScaleSet",
            CancellationToken.None));

        Assert.Null(exception);
        await activityResume.CleanupAttempted.Task.WaitAsync(TimeSpan.FromSeconds(5));
        await coordinator.StopAsync();
    }

    [Fact]
    public void SendAvatarHeightPipeline_FencesFirstSendCallbackInsideWriteGate()
    {
        var source = File.ReadAllText(FindSourceFile("VrcTwitchOscBridge", "Services", "BridgeCoordinator.cs"));
        var normalizedSource = NormalizeWhitespace(source);
        var sendBody = GetMethodBody(source, "private async Task<bool> SendAvatarHeightAsync");
        var valueBody = GetMethodBody(source, "private async Task<bool> SendAvatarHeightValueAsync");

        Assert.Contains(
            "Action? afterSuccessfulSend = null",
            normalizedSource,
            StringComparison.Ordinal);
        Assert.Contains("afterSuccessfulSend?.Invoke()", valueBody, StringComparison.Ordinal);
        Assert.DoesNotContain("afterFirstSuccessfulSend?.Invoke()", sendBody, StringComparison.Ordinal);
        Assert.Contains(
            "SendAvatarHeightValueAsync( targetHeight, cancellationToken, rule, shouldContinue, afterFirstSuccessfulSend)",
            NormalizeWhitespace(sendBody),
            StringComparison.Ordinal);
        Assert.Contains(
            "SendAvatarHeightValueAsync( value, cancellationToken, rule, shouldContinue, afterFirstSuccessfulSend)",
            NormalizeWhitespace(sendBody),
            StringComparison.Ordinal);

        var packetSendIndex = valueBody.IndexOf(
            "await oscRouterService.SendToVrChatAsync(packet, cancellationToken)",
            StringComparison.Ordinal);
        var finalWriteCheckIndex = valueBody.LastIndexOf(
            "if (shouldContinue?.Invoke() == false)",
            StringComparison.Ordinal);
        var callbackIndex = valueBody.IndexOf(
            "afterSuccessfulSend?.Invoke()",
            StringComparison.Ordinal);
        var gateReleaseIndex = valueBody.IndexOf(
            "avatarScaleWriteGate.Release()",
            callbackIndex,
            StringComparison.Ordinal);

        Assert.True(packetSendIndex >= 0);
        Assert.True(finalWriteCheckIndex > packetSendIndex);
        Assert.True(callbackIndex > finalWriteCheckIndex);
        Assert.True(gateReleaseIndex > callbackIndex);
    }

    [Fact]
    public void TemporaryDevAvatarScaleCommands_CaptureGenerationBeforePermissionAwait()
    {
        var source = File.ReadAllText(FindSourceFile("VrcTwitchOscBridge", "Services", "BridgeCoordinator.cs"));
        var methodNames = new[]
        {
            "private async Task ExecuteDevRelativeAvatarScaleAsync",
            "private async Task ExecuteDevRandomAvatarScaleAsync"
        };

        foreach (var methodName in methodNames)
        {
            var methodBody = GetMethodBody(source, methodName);
            var captureIndex = methodBody.IndexOf(
                "var runtimeGeneration = GetAvatarScaleRuntimeGeneration();",
                StringComparison.Ordinal);
            var permissionAwaitIndex = methodBody.IndexOf(
                "await TryGetAvatarScalingAllowedAsync(cancellationToken)",
                StringComparison.Ordinal);
            var permissionFenceIndex = methodBody.IndexOf(
                "if (!IsAvatarScaleRuntimeGenerationCurrent(runtimeGeneration))",
                permissionAwaitIndex,
                StringComparison.Ordinal);
            var currentHeightIndex = methodBody.IndexOf(
                "var previousHeight = await TryGetCurrentAvatarHeightAsync",
                StringComparison.Ordinal);

            Assert.True(captureIndex >= 0, methodName);
            Assert.True(permissionAwaitIndex > captureIndex, methodName);
            Assert.True(permissionFenceIndex > permissionAwaitIndex, methodName);
            Assert.True(currentHeightIndex > permissionFenceIndex, methodName);
        }
    }

    [Fact]
    public async Task TryCreateAvatarScaleCarryoverSnapshot_DoesNotInsertPendingRestoreState()
    {
        await using var coordinator = CreateCoordinator();
        var sequenceType = typeof(BridgeCoordinator).GetNestedType(
            "ActiveAvatarScaleRestoreSequenceState",
            BindingFlags.NonPublic)!;
        var sequence = Activator.CreateInstance(
            sequenceType,
            BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic,
            binder: null,
            args:
            [
                1L,
                "avtr_previous",
                1.7,
                1.6,
                DateTimeOffset.UtcNow.AddMinutes(1),
                "Test Scale",
                0d,
                false,
                true,
                null,
                0d,
                false,
                0d
            ],
            culture: null)!;
        var pendingRestores = (IDictionary)GetField(coordinator, "pendingAvatarScaleHeightRestores");
        SetField(coordinator, "activeAvatarScaleRestoreSequence", sequence);

        var snapshot = InvokePrivate(
            coordinator,
            "TryCreateAvatarScaleCarryoverSnapshot",
            "avtr_previous",
            1.7d,
            Enum.Parse(
                typeof(BridgeCoordinator).GetNestedType(
                    "AvatarScaleAvatarChangeCarryoverMode",
                    BindingFlags.NonPublic)!,
                "Auto"));

        Assert.NotNull(snapshot);
        Assert.Empty(pendingRestores);
        var pendingRestore = snapshot!.GetType().GetProperty("PendingRestore")!.GetValue(snapshot);
        Assert.NotNull(pendingRestore);
        Assert.Equal("avtr_previous", GetProperty(pendingRestore!, "AvatarId"));
    }

    [Fact]
    public async Task RecordPendingAvatarScaleHeightRestore_RejectsConflictingExistingState()
    {
        await using var coordinator = CreateCoordinator();
        var pendingRestores = (IDictionary)GetField(coordinator, "pendingAvatarScaleHeightRestores");

        Assert.True((bool)InvokePrivate(
            coordinator,
            "RecordPendingAvatarScaleHeightRestore",
            "avtr_existing",
            1.25,
            DateTimeOffset.UtcNow.AddMinutes(1),
            "Existing Scale",
            Guid.Empty,
            0L,
            0L)!);

        Assert.False((bool)InvokePrivate(
            coordinator,
            "RecordPendingAvatarScaleHeightRestore",
            "avtr_existing",
            1.75,
            DateTimeOffset.UtcNow.AddMinutes(2),
            "Conflicting Scale",
            Guid.NewGuid(),
            0L,
            0L)!);

        var stored = pendingRestores["avtr_existing"]!;
        Assert.Equal(1.25, GetProperty(stored, "RestoreHeightMeters"));
        Assert.Equal("Existing Scale", GetProperty(stored, "SourceRuleName"));
    }

    [Fact]
    public async Task TryBeginAvatarScaleOperationForDevScaleSet_ContainsNotificationFailures()
    {
        await using var coordinator = CreateCoordinator();
        var ruleId = Guid.NewGuid();
        var activeEffects = (IDictionary)GetField(coordinator, "activeAvatarScaleEffects");
        activeEffects[ruleId] = DateTimeOffset.UtcNow.AddMinutes(1);
        coordinator.ManagedRewardAvailabilityChanged += () => throw new InvalidOperationException("test notification failure");

        var operation = await InvokePrivateAsync(
            coordinator,
            "TryBeginAvatarScaleOperationForDevScaleSet",
            CancellationToken.None);

        Assert.NotNull(operation);
        Assert.Equal(
            GetProperty(operation!, "OperationId"),
            GetProperty(GetField(coordinator, "activeAvatarScaleOperation"), "OperationId"));
    }

    [Fact]
    public async Task TryBeginAvatarScaleOperationForDevScaleSet_ContainsCancellationCallbackFailures()
    {
        await using var coordinator = CreateCoordinator();
        using var restoreCancellation = new CancellationTokenSource();
        restoreCancellation.Token.Register(() => throw new InvalidOperationException("test cancellation failure"));
        SetField(coordinator, "avatarScaleRestoreSequenceCancellation", restoreCancellation);

        var operation = await InvokePrivateAsync(
            coordinator,
            "TryBeginAvatarScaleOperationForDevScaleSet",
            CancellationToken.None);

        Assert.NotNull(operation);
        Assert.Equal(
            GetProperty(operation!, "OperationId"),
            GetProperty(GetField(coordinator, "activeAvatarScaleOperation"), "OperationId"));
    }

    [Theory]
    [InlineData(0.001, 0.01)]
    [InlineData(1.6, 1.6)]
    [InlineData(12000, 10000)]
    public void ClampDevAvatarScaleHeight_UsesDeveloperRange(double input, double expected)
    {
        var method = typeof(BridgeCoordinator).GetMethod(
            "ClampDevAvatarScaleHeight",
            BindingFlags.Static | BindingFlags.NonPublic)!;

        var actual = (double)method.Invoke(null, [input])!;

        Assert.Equal(expected, actual, precision: 6);
    }

    [Fact]
    public void ExecuteDevSetAvatarScale_UsesVrChatScalingPermissionGate()
    {
        var source = File.ReadAllText(FindSourceFile("VrcTwitchOscBridge", "Services", "BridgeCoordinator.cs"));
        var methodBlock = GetMethodBody(source, "private async Task ExecuteDevSetAvatarScaleAsync");

        Assert.Contains("TryGetAvatarScalingAllowedAsync", methodBlock, StringComparison.Ordinal);
        Assert.Contains("if (scalingAllowed == false)", methodBlock, StringComparison.Ordinal);
        Assert.Contains("/avatar/eyeheightscalingallowed", methodBlock, StringComparison.Ordinal);

        var deniedGuardStart = methodBlock.IndexOf(
            "if (scalingAllowed == false)",
            StringComparison.Ordinal);
        Assert.True(deniedGuardStart >= 0);

        var deniedGuardOpeningBrace = methodBlock.IndexOf('{', deniedGuardStart);
        var deniedGuardClosingBrace = methodBlock.IndexOf('}', deniedGuardOpeningBrace);
        var deniedGuard = methodBlock[deniedGuardOpeningBrace..(deniedGuardClosingBrace + 1)];
        var returnIndex = deniedGuard.IndexOf("return;", StringComparison.Ordinal);
        var operationIndex = methodBlock.IndexOf(
            "TryBeginAvatarScaleOperationForDevScaleSet",
            deniedGuardStart,
            StringComparison.Ordinal);
        var sendIndex = methodBlock.IndexOf(
            "SendAvatarHeightForOperationAsync",
            deniedGuardStart,
            StringComparison.Ordinal);

        Assert.True(returnIndex >= 0, "The permission-denied guard must return without starting the operation.");
        Assert.True(operationIndex > deniedGuardStart, "The operation start must remain after the permission gate.");
        Assert.True(sendIndex > deniedGuardStart, "The OSC send path must remain after the permission gate.");
        Assert.True(
            deniedGuardOpeningBrace + returnIndex < operationIndex,
            "The permission-denied return must happen before the operation start.");
        Assert.True(
            deniedGuardOpeningBrace + returnIndex < sendIndex,
            "The permission-denied return must happen before the OSC send path.");
    }

    [Fact]
    public void ExecuteDevSetAvatarScale_DoesNotScheduleRestoreOrMutateRules()
    {
        var source = File.ReadAllText(FindSourceFile("VrcTwitchOscBridge", "Services", "BridgeCoordinator.cs"));
        var methodBlock = GetMethodBody(source, "private async Task ExecuteDevSetAvatarScaleAsync");

        Assert.DoesNotContain("ScheduleAvatarScaleRestoreSequence", methodBlock, StringComparison.Ordinal);
        Assert.DoesNotContain("RestoreHeightMeters =", methodBlock, StringComparison.Ordinal);
        Assert.DoesNotContain("RestoreMode =", methodBlock, StringComparison.Ordinal);
    }

    [Fact]
    public async Task SendAvatarHeightValueAsync_RejectsInvalidatedWriteBeforeSending()
    {
        await using var coordinator = CreateCoordinator();
        var method = typeof(BridgeCoordinator).GetMethod(
            "SendAvatarHeightValueAsync",
            BindingFlags.Instance | BindingFlags.NonPublic,
            binder: null,
            [
                typeof(double),
                typeof(CancellationToken),
                typeof(AvatarScaleRuleSnapshot),
                typeof(Func<bool>),
                typeof(Action)
            ],
            modifiers: null);

        Assert.NotNull(method);

        var sendTask = Assert.IsAssignableFrom<Task<bool>>(method!.Invoke(
            coordinator,
            [1.6, CancellationToken.None, null, (Func<bool>)(() => false), null]));

        Assert.False(await sendTask);
    }

    [Fact]
    public async Task SendAvatarHeightValueAsync_PreservesCancellationWhenWaitIsCanceled()
    {
        await using var coordinator = CreateCoordinator();
        var method = typeof(BridgeCoordinator).GetMethod(
            "SendAvatarHeightValueAsync",
            BindingFlags.Instance | BindingFlags.NonPublic,
            binder: null,
            [
                typeof(double),
                typeof(CancellationToken),
                typeof(AvatarScaleRuleSnapshot),
                typeof(Func<bool>),
                typeof(Action)
            ],
            modifiers: null)!;
        using var cancellation = new CancellationTokenSource();
        cancellation.Cancel();

        var canceledTask = Assert.IsAssignableFrom<Task<bool>>(method.Invoke(
            coordinator,
            [1.6, cancellation.Token, null, null, null]));

        await Assert.ThrowsAnyAsync<OperationCanceledException>(async () => await canceledTask);

        var followUpTask = Assert.IsAssignableFrom<Task<bool>>(method.Invoke(
            coordinator,
            [1.6, CancellationToken.None, null, (Func<bool>)(() => false), null]));

        Assert.False(await followUpTask);
    }

    [Fact]
    public async Task DisposeAsync_WaitsForAvatarScaleWriteUsersToExitBeforeDisposingGate()
    {
        var coordinator = CreateCoordinator();
        var source = File.ReadAllText(FindSourceFile("VrcTwitchOscBridge", "Services", "BridgeCoordinator.cs"));
        var disposeBody = GetMethodBody(source, "public async ValueTask DisposeAsync");

        Assert.Contains("MarkAvatarScaleWriteGateDisposalStarted", disposeBody, StringComparison.Ordinal);
        Assert.Contains("WaitForAvatarScaleWriteUsersAsync", disposeBody, StringComparison.Ordinal);
        Assert.True(
            disposeBody.IndexOf("MarkAvatarScaleWriteGateDisposalStarted", StringComparison.Ordinal)
                < disposeBody.IndexOf("await StopAsync()", StringComparison.Ordinal));
        Assert.True(
            disposeBody.IndexOf("await StopAsync()", StringComparison.Ordinal)
                < disposeBody.IndexOf("WaitForAvatarScaleWriteUsersAsync", StringComparison.Ordinal));

        InvokePrivate(coordinator, "EnterAvatarScaleWriteGateUser");

        var drainTask = Assert.IsAssignableFrom<Task>(InvokePrivate(
            coordinator,
            "WaitForAvatarScaleWriteUsersAsync"));

        Assert.False(drainTask.IsCompleted);

        InvokePrivate(coordinator, "ExitAvatarScaleWriteGateUser");
        await drainTask.WaitAsync(TimeSpan.FromSeconds(5));
    }

    [Fact]
    public async Task TryBeginAvatarScaleOperationForDevScaleSet_InvalidatesCapturedRuntimeGeneration()
    {
        await using var coordinator = CreateCoordinator();

        var capturedGeneration = (long)InvokePrivate(
            coordinator,
            "GetAvatarScaleRuntimeGeneration")!;

        Assert.True((bool)InvokePrivate(
            coordinator,
            "IsAvatarScaleRuntimeGenerationCurrent",
            capturedGeneration)!);

        Assert.NotNull(await InvokePrivateAsync(
            coordinator,
            "TryBeginAvatarScaleOperationForDevScaleSet",
            CancellationToken.None));

        Assert.False((bool)InvokePrivate(
            coordinator,
            "IsAvatarScaleRuntimeGenerationCurrent",
            capturedGeneration)!);

        var currentGeneration = (long)InvokePrivate(
            coordinator,
            "GetAvatarScaleRuntimeGeneration")!;
        Assert.True((bool)InvokePrivate(
            coordinator,
            "IsAvatarScaleRuntimeGenerationCurrent",
            currentGeneration)!);
    }

    [Fact]
    public async Task RestoreDevAvatarScaleHeightAsync_RejectsStaleGenerationBeforeStartingOperation()
    {
        await using var coordinator = CreateCoordinator();
        var capturedGeneration = (long)InvokePrivate(
            coordinator,
            "GetAvatarScaleRuntimeGeneration")!;

        var emergencyOperation = await InvokePrivateAsync(
            coordinator,
            "TryBeginAvatarScaleOperationForDevScaleSet",
            CancellationToken.None);
        Assert.NotNull(emergencyOperation);

        var restoreTask = Assert.IsAssignableFrom<Task>(InvokePrivate(
            coordinator,
            "RestoreDevAvatarScaleHeightAsync",
            "Dev Grow",
            1.6,
            0d,
            CancellationToken.None,
            capturedGeneration));

        await restoreTask.WaitAsync(TimeSpan.FromSeconds(5));
        Assert.Equal(
            GetProperty(emergencyOperation!, "OperationId"),
            GetProperty(GetField(coordinator, "activeAvatarScaleOperation"), "OperationId"));
    }

    [Fact]
    public void ResumePausedAvatarScaleTimerAfterDevAsync_UsesStaleGenerationFenceBeforeRestore()
    {
        var source = File.ReadAllText(FindSourceFile("VrcTwitchOscBridge", "Services", "BridgeCoordinator.cs"));
        var methodBody = GetMethodBody(
            source,
            "private async Task ResumePausedAvatarScaleTimerAfterDevAsync");

        Assert.Contains("if (!IsAvatarScaleRuntimeGenerationCurrent(runtimeGeneration))", methodBody, StringComparison.Ordinal);
        Assert.Contains("expectedRuntimeGeneration: runtimeGeneration", methodBody, StringComparison.Ordinal);
        Assert.Contains("shouldContinue: () => IsAvatarScaleRuntimeGenerationCurrent(runtimeGeneration)", methodBody, StringComparison.Ordinal);
        Assert.Contains("runtimeGeneration))", methodBody, StringComparison.Ordinal);
    }

    [Fact]
    public void TemporaryDevAvatarScaleOperations_RejectStaleGenerationBeforeStarting()
    {
        var source = File.ReadAllText(FindSourceFile("VrcTwitchOscBridge", "Services", "BridgeCoordinator.cs"));
        var relativeBody = GetMethodBody(source, "private async Task ExecuteDevRelativeAvatarScaleAsync");
        var randomBody = GetMethodBody(source, "private async Task ExecuteDevRandomAvatarScaleAsync");

        Assert.Contains("expectedRuntimeGeneration: runtimeGeneration", relativeBody, StringComparison.Ordinal);
        Assert.Contains("expectedRuntimeGeneration: runtimeGeneration", randomBody, StringComparison.Ordinal);
    }

    [Fact]
    public void TemporaryDevAvatarScaleOperations_FenceHeightQueryBeforePause()
    {
        var source = File.ReadAllText(FindSourceFile("VrcTwitchOscBridge", "Services", "BridgeCoordinator.cs"));
        var relativeBody = GetMethodBody(source, "private async Task ExecuteDevRelativeAvatarScaleAsync");
        var randomBody = GetMethodBody(source, "private async Task ExecuteDevRandomAvatarScaleAsync");
        var pauseBody = GetMethodBody(source, "private PausedAvatarScaleTimerSnapshot? PauseActiveAvatarScaleTimerForDev");
        var queryBody = GetMethodBody(source, "private async Task<double?> TryGetCurrentAvatarHeightAsync");
        var normalizedSource = NormalizeWhitespace(source);

        Assert.Contains("TryGetCurrentAvatarHeightAsync(cancellationToken, runtimeGeneration)", relativeBody, StringComparison.Ordinal);
        Assert.Contains("TryGetCurrentAvatarHeightAsync(cancellationToken, runtimeGeneration)", randomBody, StringComparison.Ordinal);
        Assert.Contains("PauseActiveAvatarScaleTimerForDev(devRuleName, devDuration, runtimeGeneration)", relativeBody, StringComparison.Ordinal);
        Assert.Contains("PauseActiveAvatarScaleTimerForDev(devRuleName, devDuration, runtimeGeneration)", randomBody, StringComparison.Ordinal);
        Assert.Contains(
            "private PausedAvatarScaleTimerSnapshot? PauseActiveAvatarScaleTimerForDev( string devRuleName, TimeSpan devDuration, long expectedRuntimeGeneration)",
            normalizedSource,
            StringComparison.Ordinal);
        Assert.Contains("avatarScaleRuntimeGeneration != expectedRuntimeGeneration", pauseBody, StringComparison.Ordinal);
        Assert.Contains(
            "private async Task<double?> TryGetCurrentAvatarHeightAsync( CancellationToken cancellationToken, long? expectedRuntimeGeneration = null)",
            normalizedSource,
            StringComparison.Ordinal);
        Assert.Contains("avatarScaleRuntimeGeneration != expectedGeneration", queryBody, StringComparison.Ordinal);
    }

    [Fact]
    public async Task PauseActiveAvatarScaleTimerForDev_RejectsStaleGenerationWithoutMutatingState()
    {
        await using var coordinator = CreateCoordinator();
        var sequenceType = typeof(BridgeCoordinator).GetNestedType(
            "ActiveAvatarScaleRestoreSequenceState",
            BindingFlags.NonPublic)!;
        var activeUntil = DateTimeOffset.UtcNow.AddMinutes(1);
        var sequence = Activator.CreateInstance(
            sequenceType,
            BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic,
            binder: null,
            args:
            [
                1L,
                "avtr_test",
                1.7,
                1.6,
                activeUntil,
                "Test Scale",
                0d,
                false,
                true,
                null,
                0d,
                false,
                0d
            ],
            culture: null)!;
        var ruleId = Guid.NewGuid();
        var activeEffects = (IDictionary)GetField(coordinator, "activeAvatarScaleEffects");
        var activeEffectUntil = DateTimeOffset.UtcNow.AddMinutes(1);

        SetField(coordinator, "avatarScaleRuntimeGeneration", 4L);
        SetField(coordinator, "activeAvatarScaleRestoreSequence", sequence);
        activeEffects[ruleId] = activeEffectUntil;

        var paused = InvokePrivate(
            coordinator,
            "PauseActiveAvatarScaleTimerForDev",
            "Dev Scale",
            TimeSpan.FromSeconds(5),
            3L);

        Assert.Null(paused);
        Assert.Same(sequence, GetField(coordinator, "activeAvatarScaleRestoreSequence"));
        Assert.Equal(activeEffectUntil, activeEffects[ruleId]);
        Assert.Null(GetField(coordinator, "pausedDevAvatarScaleTimerSnapshot"));
    }

    [Fact]
    public async Task RecordPendingAvatarScaleHeightRestore_RejectsStaleAvatarChangeProducer()
    {
        await using var coordinator = CreateCoordinator();
        var pendingRestores = (IDictionary)GetField(coordinator, "pendingAvatarScaleHeightRestores");
        SetField(coordinator, "nextAvatarScaleAvatarChangeSequenceId", 8L);
        SetField(coordinator, "avatarScaleRuntimeGeneration", 5L);

        var recorded = (bool)InvokePrivate(
            coordinator,
            "RecordPendingAvatarScaleHeightRestore",
            "avtr_new",
            1.6,
            DateTimeOffset.UtcNow.AddMinutes(1),
            "Stale Scale",
            Guid.NewGuid(),
            7L,
            4L)!;

        Assert.False(recorded);
        Assert.Empty(pendingRestores);
    }

    [Fact]
    public async Task TryBeginAvatarScaleOperationForDevScaleSet_ToleratesDisposedInvalidationCancellations()
    {
        await using var coordinator = CreateCoordinator();
        var restoreCancellation = new CancellationTokenSource();
        restoreCancellation.Dispose();
        SetField(coordinator, "avatarScaleRestoreSequenceCancellation", restoreCancellation);

        var effectNotifications = (IDictionary)GetField(coordinator, "avatarScaleEffectStateNotifications");
        var activeEffects = (IDictionary)GetField(coordinator, "activeAvatarScaleEffects");
        var effectNotification = new CancellationTokenSource();
        effectNotification.Dispose();
        var ruleId = Guid.NewGuid();
        effectNotifications[ruleId] = effectNotification;
        activeEffects[ruleId] = DateTimeOffset.UtcNow.AddMinutes(1);

        var operation = await InvokePrivateAsync(
            coordinator,
            "TryBeginAvatarScaleOperationForDevScaleSet",
            CancellationToken.None);

        Assert.NotNull(operation);
        Assert.Equal(
            GetProperty(operation!, "OperationId"),
            GetProperty(GetField(coordinator, "activeAvatarScaleOperation"), "OperationId"));
    }

    [Fact]
    public void RestorePendingAvatarScaleHeightForCurrentAvatarAsync_RechecksRuntimeGenerationAtAllWriteBoundaries()
    {
        var source = File.ReadAllText(FindSourceFile("VrcTwitchOscBridge", "Services", "BridgeCoordinator.cs"));
        var methodBody = GetMethodBody(
            source,
            "private async Task RestorePendingAvatarScaleHeightForCurrentAvatarAsync");

        Assert.Contains("runtimeGeneration = avatarScaleRuntimeGeneration", methodBody, StringComparison.Ordinal);
        Assert.Contains("expectedRuntimeGeneration", methodBody, StringComparison.Ordinal);
        Assert.Contains("IsAvatarScaleRuntimeGenerationCurrent(runtimeGeneration)", methodBody, StringComparison.Ordinal);
        Assert.Contains("shouldContinue: () => IsAvatarScaleRuntimeGenerationCurrent(runtimeGeneration)", methodBody, StringComparison.Ordinal);
        Assert.True(
            methodBody.IndexOf("TryBeginAvatarScaleOperation", StringComparison.Ordinal)
                < methodBody.IndexOf("SendAvatarHeightForOperationAsync", StringComparison.Ordinal));
        Assert.True(
            methodBody.LastIndexOf("IsAvatarScaleRuntimeGenerationCurrent(runtimeGeneration)", StringComparison.Ordinal)
                < methodBody.LastIndexOf("TryRemovePendingAvatarScaleHeightRestore", StringComparison.Ordinal));
    }

    [Fact]
    public async Task TryBeginAvatarScaleOperationForDevScaleSet_ClearsActiveEffectStateWithoutChangingRuleConfiguration()
    {
        await using var coordinator = CreateCoordinator();
        var source = File.ReadAllText(FindSourceFile("VrcTwitchOscBridge", "Services", "BridgeCoordinator.cs"));
        var invalidationHelperBody = GetMethodBody(
            source,
            "private AvatarScaleRuntimeInvalidation InvalidateAvatarScaleRuntimeForDevScaleSetLocked");

        Assert.DoesNotContain("RestoreHeightMeters =", invalidationHelperBody, StringComparison.Ordinal);
        Assert.DoesNotContain("RestoreMode =", invalidationHelperBody, StringComparison.Ordinal);

        var ruleId = Guid.NewGuid();
        var configuredRule = new AvatarScaleRule
        {
            Id = ruleId,
            RestoreMode = AvatarScaleRestoreMode.ConfiguredHeight,
            RestoreHeightMeters = 1.75
        };
        var previousHeightRule = new AvatarScaleRule
        {
            RestoreMode = AvatarScaleRestoreMode.PreviousHeight,
            RestoreHeightMeters = 2.25
        };
        var activeEffects = (IDictionary)GetField(coordinator, "activeAvatarScaleEffects");
        var restoreCancellation = new CancellationTokenSource();
        var liveRedeemPriority = Enum.Parse(
            typeof(BridgeCoordinator).GetNestedType("AvatarScaleOperationPriority", BindingFlags.NonPublic)!,
            "LiveRedeem");

        activeEffects[ruleId] = DateTimeOffset.UtcNow.AddMinutes(1);
        SetField(coordinator, "avatarScaleRestoreSequenceCancellation", restoreCancellation);
        Assert.NotNull(await InvokePrivateAsync(
            coordinator,
            "TryBeginAvatarScaleOperation",
            ruleId,
            "Test Scale",
            liveRedeemPriority,
            false,
            CancellationToken.None,
            null));

        var developerOperation = await InvokePrivateAsync(
            coordinator,
            "TryBeginAvatarScaleOperationForDevScaleSet",
            CancellationToken.None);

        Assert.Empty(activeEffects);
        Assert.NotNull(developerOperation);
        Assert.NotNull(GetField(coordinator, "activeAvatarScaleOperation"));
        Assert.True(restoreCancellation.IsCancellationRequested);
        Assert.Equal(AvatarScaleRestoreMode.ConfiguredHeight, configuredRule.RestoreMode);
        Assert.Equal(1.75, configuredRule.RestoreHeightMeters);
        Assert.Equal(AvatarScaleRestoreMode.PreviousHeight, previousHeightRule.RestoreMode);
        Assert.Equal(2.25, previousHeightRule.RestoreHeightMeters);
    }

    [Fact]
    public async Task TryBeginAvatarScaleOperationForDevScaleSet_PreservesQueuedNormalRewards()
    {
        await using var coordinator = CreateCoordinator();
        var queuedRuleId = Guid.NewGuid();
        var queuedRule = BridgeRuntimeConfiguration.CreateManualTestSnapshot(new AvatarScaleRule
        {
            Id = queuedRuleId,
            Name = "Queued Scale"
        });
        var incomingEventType = typeof(BridgeCoordinator).GetNestedType(
            "UniversalIncomingEvent",
            BindingFlags.NonPublic)!;
        var testEvent = incomingEventType
            .GetProperty("Test", BindingFlags.Static | BindingFlags.Public | BindingFlags.NonPublic)!
            .GetValue(null)!;

        SetField(coordinator, "drainingQueuedAvatarScaleOperations", true);
        _ = InvokePrivate(
            coordinator,
            "QueueAvatarScaleRuleExecutionAsync",
            queuedRule,
            testEvent,
            false,
            false,
            CancellationToken.None);

        Assert.Equal([queuedRuleId], coordinator.GetQueuedAvatarScaleRuleIds());

        Assert.NotNull(await InvokePrivateAsync(
            coordinator,
            "TryBeginAvatarScaleOperationForDevScaleSet",
            CancellationToken.None));

        Assert.Equal([queuedRuleId], coordinator.GetQueuedAvatarScaleRuleIds());
    }

    [Fact]
    public async Task TryBeginAvatarScaleOperationForDevScaleSet_CancelsSupporterGrowthWorkers()
    {
        await using var coordinator = CreateCoordinator();
        var stateType = typeof(BridgeCoordinator).GetNestedType(
            "ActiveAvatarScaleSupporterGrowthState",
            BindingFlags.NonPublic)!;
        var state = Activator.CreateInstance(stateType, nonPublic: true)!;
        var sessionCancellation = new CancellationTokenSource();
        stateType.GetProperty("SessionCancellation")!.SetValue(state, sessionCancellation);
        var states = (IDictionary)GetField(coordinator, "avatarScaleSupporterGrowthStates");
        states[Guid.NewGuid()] = state;

        Assert.NotNull(await InvokePrivateAsync(
            coordinator,
            "TryBeginAvatarScaleOperationForDevScaleSet",
            CancellationToken.None));

        Assert.Empty(states);
        Assert.True(sessionCancellation.IsCancellationRequested);
        sessionCancellation.Dispose();
    }

    [Fact]
    public async Task TryInstallSupporterGrowthSession_RejectsInvalidatedOperationWithoutRegisteringState()
    {
        await using var coordinator = CreateCoordinator();
        var supporterGrowthPriority = Enum.Parse(
            typeof(BridgeCoordinator).GetNestedType("AvatarScaleOperationPriority", BindingFlags.NonPublic)!,
            "SupporterGrowth");
        var operation = await InvokePrivateAsync(
            coordinator,
            "TryBeginAvatarScaleOperation",
            Guid.NewGuid(),
            "Supporter Growth",
            supporterGrowthPriority,
            false,
            CancellationToken.None,
            null);
        Assert.NotNull(operation);

        Assert.NotNull(await InvokePrivateAsync(
            coordinator,
            "TryBeginAvatarScaleOperationForDevScaleSet",
            CancellationToken.None));

        var rule = BridgeRuntimeConfiguration.CreateManualTestSnapshot(new AvatarScaleRule
        {
            TriggerType = AvatarScaleTriggerType.SupporterGrowth,
            SupporterGrowthTier1HeightMeters = 0.1,
            SupporterGrowthTier1Seconds = 60
        });

        var installation = InvokePrivate(
            coordinator,
            "TryInstallSupporterGrowthSession",
            operation,
            rule,
            1.6,
            0.1,
            60d);

        Assert.Null(installation);
        Assert.Empty((IDictionary)GetField(coordinator, "avatarScaleSupporterGrowthStates"));
    }

    [Fact]
    public async Task TryBeginAvatarScaleOperationForDevScaleSet_NotifiesClearedEffectState()
    {
        await using var coordinator = CreateCoordinator();
        var ruleId = Guid.NewGuid();
        var activeEffects = (IDictionary)GetField(coordinator, "activeAvatarScaleEffects");
        var effectNotifications = (IDictionary)GetField(coordinator, "avatarScaleEffectStateNotifications");
        var notification = new CancellationTokenSource();
        activeEffects[ruleId] = DateTimeOffset.UtcNow.AddMinutes(1);
        effectNotifications[ruleId] = notification;

        var managedRewardAvailabilityNotifications = 0;
        var cooldownColorNotifications = new List<Guid>();
        coordinator.ManagedRewardAvailabilityChanged += () => managedRewardAvailabilityNotifications++;
        coordinator.RewardCooldownColorChanged += id => cooldownColorNotifications.Add(id);

         Assert.NotNull(await InvokePrivateAsync(
             coordinator,
             "TryBeginAvatarScaleOperationForDevScaleSet",
             CancellationToken.None));

        Assert.Equal(1, managedRewardAvailabilityNotifications);
        Assert.Equal([ruleId], cooldownColorNotifications);
        Assert.True(notification.IsCancellationRequested);
    }

    [Fact]
    public async Task ResumePendingActivities_WaitsForAvatarScaleInvalidationCleanupBeforeUsingSavedActivity()
    {
        var ruleId = Guid.NewGuid();
        const double emergencyHeight = 2.5;
        var activityResume = new BlockingCleanupActivityResumeService();
        activityResume.Activities.Add(new ResumeActivity
        {
            Type = ResumeActivityType.AvatarScale,
            RuleId = ruleId,
            CurrentValue = emergencyHeight
        });
        var coordinator = CreateCoordinator(activityResume);
        ConfigureCoordinatorForPendingAvatarScaleResume(coordinator, ruleId, emergencyHeight);

        var activeEffects = (IDictionary)GetField(coordinator, "activeAvatarScaleEffects");
        activeEffects[ruleId] = DateTimeOffset.UtcNow.AddMinutes(1);

        var invalidationTask = InvokePrivateAsync(
            coordinator,
            "TryBeginAvatarScaleOperationForDevScaleSet",
            CancellationToken.None);
        await activityResume.CleanupStarted.Task.WaitAsync(TimeSpan.FromSeconds(5));

        var activityQueriesBeforeResume = activityResume.PendingActivityQueryCount;
        var resumeTask = coordinator.TryResumePendingActivitiesAsync();

        try
        {
            Assert.False(resumeTask.IsCompleted);
            Assert.Equal(activityQueriesBeforeResume, activityResume.PendingActivityQueryCount);
            Assert.Null(GetField(coordinator, "activeAvatarScaleRestoreSequence"));
            Assert.Equal(emergencyHeight, GetObservedAvatarScaleHeight(coordinator), precision: 3);
        }
        finally
        {
            activityResume.ReleaseCleanup();
            await Task.WhenAll(
                invalidationTask,
                resumeTask).WaitAsync(TimeSpan.FromSeconds(5));
        }

        Assert.Null(GetField(coordinator, "activeAvatarScaleRestoreSequence"));
        Assert.Equal(emergencyHeight, GetObservedAvatarScaleHeight(coordinator), precision: 3);
        await coordinator.StopAsync().WaitAsync(TimeSpan.FromSeconds(5));
    }

    [Fact]
    public async Task ResumePendingActivities_ConcurrentCallersShareTheInFlightResume()
    {
        var ruleId = Guid.NewGuid();
        const double savedHeight = 1.6;
        var activityResume = new BlockingPendingActivitiesResumeService();
        activityResume.Activities.Add(new ResumeActivity
        {
            Type = ResumeActivityType.AvatarScale,
            RuleId = ruleId,
            CurrentValue = savedHeight
        });
        var coordinator = CreateCoordinator(activityResume);
        ConfigureCoordinatorForPendingAvatarScaleResume(coordinator, ruleId, savedHeight);

        var firstResumeTask = Task.Run(() => coordinator.TryResumePendingActivitiesAsync());
        await activityResume.PendingActivitiesRequested.Task.WaitAsync(TimeSpan.FromSeconds(5));

        var secondResumeTask = coordinator.TryResumePendingActivitiesAsync();
        var pendingActivitiesReleased = false;

        try
        {
            Assert.False(secondResumeTask.IsCompleted);

            activityResume.ReleasePendingActivities();
            pendingActivitiesReleased = true;
            await Task.WhenAll(
                firstResumeTask,
                secondResumeTask).WaitAsync(TimeSpan.FromSeconds(5));

            Assert.Equal(1, activityResume.PendingActivityQueryCount);
            Assert.Equal(1L, GetField(coordinator, "nextAvatarScaleRestoreSequenceId"));
            Assert.NotNull(GetField(coordinator, "activeAvatarScaleRestoreSequence"));
        }
        finally
        {
            if (!pendingActivitiesReleased)
            {
                activityResume.ReleasePendingActivities();
            }

            await Task.WhenAll(
                firstResumeTask,
                secondResumeTask).WaitAsync(TimeSpan.FromSeconds(5));
            await coordinator.StopAsync().WaitAsync(TimeSpan.FromSeconds(5));
        }
    }

    [Fact]
    public async Task PendingActivityResume_FromPreviousRuntimeSession_DoesNotSetNewSessionStateOrReplay()
    {
        var ruleId = Guid.NewGuid();
        const double savedHeight = 1.6;
        var activityResume = new BlockingPendingActivitiesResumeService();
        activityResume.Activities.Add(new ResumeActivity
        {
            Type = ResumeActivityType.AvatarScale,
            RuleId = ruleId,
            CurrentValue = savedHeight
        });
        var coordinator = CreateCoordinator(activityResume);
        var cleanupGate = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        SetField(coordinator, "avatarScaleActivityResumeCleanupTask", cleanupGate.Task);

        Task? firstResumeTask = null;
        try
        {
            ConfigureCoordinatorForPendingAvatarScaleResume(coordinator, ruleId, savedHeight);
            firstResumeTask = coordinator.TryResumePendingActivitiesAsync();
            await activityResume.PendingAvatarCheckRequested.Task.WaitAsync(TimeSpan.FromSeconds(5));

            await coordinator.StopAsync().WaitAsync(TimeSpan.FromSeconds(5));
            ConfigureCoordinatorForPendingAvatarScaleResume(coordinator, ruleId, savedHeight);

            cleanupGate.TrySetResult();
            activityResume.ReleasePendingActivities();
            await firstResumeTask!.WaitAsync(TimeSpan.FromSeconds(5));

            Assert.False((bool)GetField(coordinator, "hasAttemptedResume"));
            Assert.Equal(0L, GetField(coordinator, "nextAvatarScaleRestoreSequenceId"));
            Assert.Null(GetField(coordinator, "activeAvatarScaleRestoreSequence"));
        }
        finally
        {
            cleanupGate.TrySetResult();
            activityResume.ReleasePendingActivities();
            if (firstResumeTask is not null)
            {
                await firstResumeTask.WaitAsync(TimeSpan.FromSeconds(5));
            }

            await coordinator.StopAsync().WaitAsync(TimeSpan.FromSeconds(5));
        }
    }

    private static void ConfigureCoordinatorForPendingAvatarScaleResume(
        BridgeCoordinator coordinator,
        Guid ruleId,
        double observedHeight)
    {
        var rule = new AvatarScaleRule
        {
            Id = ruleId,
            Name = "Pending Scale",
            TriggerType = AvatarScaleTriggerType.Follow,
            ScaleMode = AvatarScaleMode.SetHeight,
            TargetHeightMeters = 1.25,
            ActiveTimeSeconds = 60,
            RestoreMode = AvatarScaleRestoreMode.ConfiguredHeight,
            RestoreHeightMeters = 1.6
        };
        var settings = new AppSettings();
        settings.AvatarScaleRules.Add(rule);

        SetField(
            coordinator,
            "activeConfiguration",
            BridgeRuntimeConfiguration.FromSettings(settings, RuntimeConfig.CreateDefault()));
        SetField(coordinator, "currentVrChatAvatarId", "avtr_test");

        var avatarScaleValues = (IDictionary)GetField(coordinator, "avatarScaleValues");
        avatarScaleValues["/avatar/eyeheight"] = new OscObservedValue(
            "/avatar/eyeheight",
            OscParameterType.Float,
            (float)observedHeight);

        var oscRouterService = GetField(coordinator, "oscRouterService");
        var routerType = oscRouterService.GetType();
        var targetType = routerType.GetNestedType("DiscoveredOscTarget", BindingFlags.NonPublic)!;
        var target = Activator.CreateInstance(
            targetType,
            BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic,
            binder: null,
            args: ["Test VRChat", System.Net.IPAddress.Loopback, 9001, 9002],
            culture: null)!;
        routerType
            .GetField("activeVrChatTarget", BindingFlags.Instance | BindingFlags.NonPublic)!
            .SetValue(oscRouterService, target);
    }

    private static double GetObservedAvatarScaleHeight(BridgeCoordinator coordinator)
    {
        var avatarScaleValues = (IDictionary)GetField(coordinator, "avatarScaleValues");
        var observedValue = Assert.IsType<OscObservedValue>(avatarScaleValues["/avatar/eyeheight"]);
        return Assert.IsType<float>(observedValue.Value);
    }

    private static BridgeCoordinator CreateCoordinator(IActivityResumeService? activityResumeService = null) => new(
        new DesktopInputLockService(Dispatcher.CurrentDispatcher),
        new WorldCommandBlacklistService(),
        new VrChatLocalOscCacheService(),
        activityResumeService);

    private static object GetField(BridgeCoordinator coordinator, string name) =>
        typeof(BridgeCoordinator)
            .GetField(name, BindingFlags.Instance | BindingFlags.NonPublic)!
            .GetValue(coordinator)!;

    private static object GetProperty(object instance, string name) =>
        instance.GetType()
            .GetProperty(name, BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic)!
            .GetValue(instance)!;

    private static void SetField(BridgeCoordinator coordinator, string name, object? value) =>
        typeof(BridgeCoordinator)
            .GetField(name, BindingFlags.Instance | BindingFlags.NonPublic)!
            .SetValue(coordinator, value);

    private static object? InvokePrivate(BridgeCoordinator coordinator, string name, params object?[] arguments) =>
        typeof(BridgeCoordinator)
            .GetMethod(name, BindingFlags.Instance | BindingFlags.NonPublic)!
            .Invoke(coordinator, arguments);

    private static async Task<object?> InvokePrivateAsync(
        BridgeCoordinator coordinator,
        string name,
        params object?[] arguments)
    {
        var invocation = InvokePrivate(coordinator, name, arguments);
        if (invocation is not Task task)
        {
            return invocation;
        }

        await task;
        return task.GetType().GetProperty("Result")?.GetValue(task);
    }

    private static string FindSourceFile(params string[] relativeParts)
    {
        var current = AppContext.BaseDirectory;
        while (!string.IsNullOrWhiteSpace(current))
        {
            var candidate = Path.Combine(new[] { current }.Concat(relativeParts).ToArray());
            if (File.Exists(candidate))
            {
                return candidate;
            }

            current = Directory.GetParent(current)?.FullName ?? string.Empty;
        }

        throw new FileNotFoundException($"Could not find source file {Path.Combine(relativeParts)}.");
    }

    private static string GetMethodBody(string source, string signature)
    {
        var methodStart = source.IndexOf(signature, StringComparison.Ordinal);
        Assert.True(methodStart >= 0, $"Could not find method signature: {signature}");

        var openingBrace = source.IndexOf('{', methodStart);
        Assert.True(openingBrace >= 0, $"Could not find method body: {signature}");

        var depth = 0;
        for (var index = openingBrace; index < source.Length; index++)
        {
            if (source[index] == '{')
            {
                depth++;
            }
            else if (source[index] == '}' && --depth == 0)
            {
                return source[openingBrace..(index + 1)];
            }
        }

        throw new InvalidOperationException($"Could not close method body: {signature}");
    }

    private static string NormalizeWhitespace(string value) =>
        string.Join(' ', value.Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries));

    private static int FindMatchingBrace(string source, int openingBrace)
    {
        var depth = 0;
        for (var index = openingBrace; index < source.Length; index++)
        {
            if (source[index] == '{')
            {
                depth++;
            }
            else if (source[index] == '}' && --depth == 0)
            {
                return index;
            }
        }

        throw new InvalidOperationException("Could not find matching brace.");
    }

    private class RecordingActivityResumeService : IActivityResumeService
    {
        public List<ResumeActivity> Activities { get; } = [];

        public Task LoadPendingAsync() => Task.CompletedTask;

        public bool HasPendingResume => Activities.Count > 0;

        public virtual bool IsPendingForAvatar(string avatarId) => Activities.Count > 0;

        public virtual IReadOnlyList<ResumeActivity> GetPendingActivities() => Activities;

        public virtual Task RemoveExpiredActivitiesAsync()
        {
            Activities.RemoveAll(
                activity => activity.ExpiresAt is { } expiresAt
                    && expiresAt <= DateTimeOffset.UtcNow);
            return Task.CompletedTask;
        }

        public virtual Task RecordActivityStartedAsync(ResumeActivity activity, string avatarId)
        {
            Activities.RemoveAll(existing => existing.RuleId == activity.RuleId);
            Activities.Add(activity);
            return Task.CompletedTask;
        }

        public virtual Task RecordActivityEndedAsync(Guid ruleId, ResumeActivity? expectedActivity = null)
        {
            if (expectedActivity is null)
            {
                Activities.RemoveAll(activity => activity.RuleId == ruleId);
            }
            else
            {
                Activities.Remove(expectedActivity);
            }
            return Task.CompletedTask;
        }

        public virtual Task RemoveActivityAsync(ResumeActivity activity)
        {
            Activities.Remove(activity);
            return Task.CompletedTask;
        }

        public Task ClearAllAsync()
        {
            Activities.Clear();
            return Task.CompletedTask;
        }

        public Task CommitAsync() => Task.CompletedTask;

        public Task DeleteStaleFileIfPresentAsync() => Task.CompletedTask;

        public async Task WaitForActivityEndAsync(Guid ruleId)
        {
            for (var attempt = 0; attempt < 100 && Activities.Any(activity => activity.RuleId == ruleId); attempt++)
            {
                await Task.Delay(10);
            }
        }
    }

    private sealed class BlockingActivityResumeService : RecordingActivityResumeService
    {
        public TaskCompletionSource ActivityStarted { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);

        private readonly TaskCompletionSource releaseActivityStarted =
            new(TaskCreationOptions.RunContinuationsAsynchronously);

        public void ReleaseActivityStarted() => releaseActivityStarted.TrySetResult();

        public override async Task RecordActivityStartedAsync(ResumeActivity activity, string avatarId)
        {
            Activities.RemoveAll(existing => existing.RuleId == activity.RuleId);
            Activities.Add(activity);
            ActivityStarted.TrySetResult();
            await releaseActivityStarted.Task;
        }
    }

    private sealed class BlockingCleanupActivityResumeService : RecordingActivityResumeService
    {
        public TaskCompletionSource CleanupStarted { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);

        private readonly TaskCompletionSource releaseCleanup =
            new(TaskCreationOptions.RunContinuationsAsynchronously);

        private int pendingActivityQueryCount;

        public int PendingActivityQueryCount => Volatile.Read(ref pendingActivityQueryCount);

        public void ReleaseCleanup() => releaseCleanup.TrySetResult();

        public override IReadOnlyList<ResumeActivity> GetPendingActivities()
        {
            Interlocked.Increment(ref pendingActivityQueryCount);
            return base.GetPendingActivities();
        }

        public override async Task RemoveActivityAsync(ResumeActivity activity)
        {
            CleanupStarted.TrySetResult();
            await releaseCleanup.Task;
            await base.RemoveActivityAsync(activity);
        }
    }

    private sealed class BlockingPendingActivitiesResumeService : RecordingActivityResumeService
    {
        public TaskCompletionSource PendingAvatarCheckRequested { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);

        public TaskCompletionSource PendingActivitiesRequested { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);

        private readonly TaskCompletionSource releasePendingActivities =
            new(TaskCreationOptions.RunContinuationsAsynchronously);

        private int pendingActivityQueryCount;

        public int PendingActivityQueryCount => Volatile.Read(ref pendingActivityQueryCount);

        public void ReleasePendingActivities() => releasePendingActivities.TrySetResult();

        public override bool IsPendingForAvatar(string avatarId)
        {
            PendingAvatarCheckRequested.TrySetResult();
            return base.IsPendingForAvatar(avatarId);
        }

        public override IReadOnlyList<ResumeActivity> GetPendingActivities()
        {
            Interlocked.Increment(ref pendingActivityQueryCount);
            PendingActivitiesRequested.TrySetResult();
            releasePendingActivities.Task.GetAwaiter().GetResult();
            return base.GetPendingActivities();
        }
    }

    private sealed class CleanupTrackingActivityResumeService : RecordingActivityResumeService
    {
        public TaskCompletionSource RemovalAttempted { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);

        public override Task RemoveActivityAsync(ResumeActivity activity)
        {
            RemovalAttempted.TrySetResult();
            return base.RemoveActivityAsync(activity);
        }
    }

    private sealed class ThrowingActivityResumeService : RecordingActivityResumeService
    {
        public TaskCompletionSource CleanupAttempted { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);

        public override Task RemoveActivityAsync(ResumeActivity activity)
        {
            CleanupAttempted.TrySetResult();
            throw new InvalidOperationException("test cleanup failure");
        }
    }

    private sealed class RacingActivityResumeService : RecordingActivityResumeService
    {
        public ResumeActivity NewerActivity { get; } = new()
        {
            Type = ResumeActivityType.AvatarScale
        };

        public ResumeActivity UnrelatedActivity { get; } = new()
        {
            Type = ResumeActivityType.Movement
        };

        public override Task RecordActivityEndedAsync(Guid ruleId, ResumeActivity? expectedActivity = null)
        {
            NewerActivity.RuleId = ruleId;
            UnrelatedActivity.RuleId = ruleId;
            Activities.Add(NewerActivity);
            Activities.Add(UnrelatedActivity);
            Activities.RemoveAll(activity => activity.RuleId == ruleId);
            return Task.CompletedTask;
        }

        public override Task RemoveActivityAsync(ResumeActivity activity)
        {
            NewerActivity.RuleId = activity.RuleId;
            UnrelatedActivity.RuleId = activity.RuleId;
            Activities.Add(NewerActivity);
            Activities.Add(UnrelatedActivity);
            Activities.Remove(activity);
            return Task.CompletedTask;
        }
    }
}
