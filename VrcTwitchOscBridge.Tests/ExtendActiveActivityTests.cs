using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using System.Reflection;
using System.Runtime.CompilerServices;
using VrcTwitchOscBridge.Services;
using VrcTwitchOscBridge.Models;
using Xunit;

namespace VrcTwitchOscBridge.Tests;

public class ExtendActiveActivityTests
{
    private static BridgeCoordinator CreateUninitializedCoordinator()
    {
        return (BridgeCoordinator)RuntimeHelpers.GetUninitializedObject(typeof(BridgeCoordinator));
    }

    private static void SetPrivateField(object target, string fieldName, object? value)
    {
        var field = typeof(BridgeCoordinator).GetField(fieldName, BindingFlags.NonPublic | BindingFlags.Instance);
        Assert.NotNull(field);
        field!.SetValue(target, value);
    }

    private static object? GetPrivateField(object target, string fieldName)
    {
        var field = typeof(BridgeCoordinator).GetField(fieldName, BindingFlags.NonPublic | BindingFlags.Instance);
        Assert.NotNull(field);
        return field!.GetValue(target);
    }

    private static object? InvokePrivate(BridgeCoordinator coordinator, string methodName, params object?[] arguments) =>
        typeof(BridgeCoordinator)
            .GetMethod(methodName, BindingFlags.NonPublic | BindingFlags.Instance)!
            .Invoke(coordinator, arguments);

    private static object CreateTypedDictionary(Type keyType, Type valueType)
    {
        var dictType = typeof(Dictionary<,>).MakeGenericType(keyType, valueType);
        return Activator.CreateInstance(dictType)!;
    }

    private static void InitializeRuntimeStateDictionaries(BridgeCoordinator coordinator)
    {
        var floatSessionStateType = typeof(BridgeCoordinator).GetNestedType(
            "ActiveFloatRedeemSessionState", BindingFlags.NonPublic);
        var pendingResetStateType = typeof(BridgeCoordinator).GetNestedType(
            "PendingResetState", BindingFlags.NonPublic);
        var movementLaneStateType = typeof(BridgeCoordinator).GetNestedType(
            "ActiveMovementLaneState", BindingFlags.NonPublic);
        var supporterGrowthStateType = typeof(BridgeCoordinator).GetNestedType(
            "ActiveAvatarScaleSupporterGrowthState", BindingFlags.NonPublic);

        SetPrivateField(coordinator, "activeFloatRedeemSessions",
            CreateTypedDictionary(typeof(Guid), floatSessionStateType!));
        SetPrivateField(coordinator, "pendingResets",
            CreateTypedDictionary(typeof(Guid), pendingResetStateType!));
        SetPrivateField(coordinator, "actionLanes",
            CreateTypedDictionary(typeof(string), movementLaneStateType!));
        SetPrivateField(coordinator, "avatarScaleSupporterGrowthStates",
            CreateTypedDictionary(typeof(Guid), supporterGrowthStateType!));
        SetPrivateField(coordinator, "activeAvatarScaleEffects", new Dictionary<Guid, DateTimeOffset>());
        SetPrivateField(coordinator, "avatarScaleEffectStateNotifications", new Dictionary<Guid, CancellationTokenSource>());
        var heightSessionStateType = typeof(BridgeCoordinator).GetNestedType(
            "ActiveAvatarScaleHeightSessionState", BindingFlags.NonPublic);
        SetPrivateField(coordinator, "activeAvatarScaleHeightSessions",
            CreateTypedDictionary(typeof(Guid), heightSessionStateType!));
    }

    [Fact]
    public void ExtendActiveActivityTimers_WithActiveScaleSequence_ExtendsActiveUntil()
    {
        var coordinator = CreateUninitializedCoordinator();
        SetPrivateField(coordinator, "stateGate", new object());
        InitializeRuntimeStateDictionaries(coordinator);

        var originalActiveUntil = DateTimeOffset.UtcNow.AddSeconds(20);
        var safety = new AvatarScaleSafetySettings();
        var rule = new AvatarScaleRule { Name = "Grow", ActiveTimeSeconds = 20, RestoreHeightMeters = 1.6 };
        var snapshot = BridgeRuntimeConfiguration.CreateManualTestSnapshot(rule, safety);

        // Simulate an active restore sequence using the record's constructor
        var sequenceType = typeof(BridgeCoordinator).GetNestedType(
            "ActiveAvatarScaleRestoreSequenceState", BindingFlags.NonPublic);
        Assert.NotNull(sequenceType);
        var sequence = Activator.CreateInstance(sequenceType!,
            1L, "avatar-id", 2.0, 1.6, originalActiveUntil, "Grow", 0.0,
            true, false, snapshot, 20.0, false, 1.6);
        sequenceType!.GetProperty("ActivityDeadline")!.SetValue(
            sequence,
            new ReschedulableActivityDeadline(originalActiveUntil));
        SetPrivateField(coordinator, "activeAvatarScaleRestoreSequence", sequence);

        var extendMethod = typeof(BridgeCoordinator).GetMethod(
            "ExtendActiveActivityTimers", BindingFlags.NonPublic | BindingFlags.Instance);
        Assert.NotNull(extendMethod);

        extendMethod!.Invoke(coordinator, [TimeSpan.FromSeconds(15), "Cheer 100"]);

        var updatedSequence = GetPrivateField(coordinator, "activeAvatarScaleRestoreSequence");
        Assert.NotNull(updatedSequence);
        var updatedActiveUntil = (DateTimeOffset)updatedSequence!.GetType().GetProperty("ActiveUntil")!.GetValue(updatedSequence)!;
        var expected = originalActiveUntil.AddSeconds(15);
        Assert.True(Math.Abs((updatedActiveUntil - expected).TotalSeconds) < 2,
            $"Expected ~{expected:O}, got {updatedActiveUntil:O}");
    }

    [Fact]
    public void ExtendActiveActivityTimers_WithNoActiveActivity_DoesNothing()
    {
        var coordinator = CreateUninitializedCoordinator();
        SetPrivateField(coordinator, "stateGate", new object());
        InitializeRuntimeStateDictionaries(coordinator);
        SetPrivateField(coordinator, "activeAvatarScaleRestoreSequence", null);

        var extendMethod = typeof(BridgeCoordinator).GetMethod(
            "ExtendActiveActivityTimers", BindingFlags.NonPublic | BindingFlags.Instance);
        Assert.NotNull(extendMethod);

        // Should not throw when nothing is active
        extendMethod!.Invoke(coordinator, [TimeSpan.FromSeconds(15), "Cheer 100"]);

        Assert.Null(GetPrivateField(coordinator, "activeAvatarScaleRestoreSequence"));
    }

    [Fact]
    public void ExtendActiveActivityTimers_ExtendsLinkedScaleWindowsAndTheirCleanupState()
    {
        var coordinator = CreateUninitializedCoordinator();
        SetPrivateField(coordinator, "stateGate", new object());
        InitializeRuntimeStateDictionaries(coordinator);

        var ruleId = Guid.NewGuid();
        var sessionId = Guid.NewGuid();
        var originalActiveUntil = DateTimeOffset.UtcNow.AddSeconds(20);
        var heightSessionType = typeof(BridgeCoordinator).GetNestedType(
            "ActiveAvatarScaleHeightSessionState", BindingFlags.NonPublic);
        Assert.NotNull(heightSessionType);
        var heightSession = Activator.CreateInstance(
            heightSessionType!,
            ruleId,
            sessionId,
            "Grow",
            "avatar-id",
            1.6,
            2.0,
            originalActiveUntil);
        Assert.NotNull(heightSession);
        heightSessionType!.GetProperty("ActivityDeadline")!.SetValue(
            heightSession,
            new ReschedulableActivityDeadline(originalActiveUntil));

        var heightSessions = (IDictionary)GetPrivateField(coordinator, "activeAvatarScaleHeightSessions")!;
        heightSessions.Add(ruleId, heightSession);
        var carryoverType = typeof(BridgeCoordinator).GetNestedType(
            "ActiveAvatarScaleCarryoverState", BindingFlags.NonPublic);
        Assert.NotNull(carryoverType);
        var carryover = Activator.CreateInstance(
            carryoverType!,
            Guid.NewGuid(),
            ruleId,
            sessionId,
            0L,
            "Grow",
            "avatar-id",
            2.0,
            1.6,
            originalActiveUntil,
            true);
        SetPrivateField(coordinator, "activeAvatarScaleCarryover", carryover);
        var activeEffects = (Dictionary<Guid, DateTimeOffset>)GetPrivateField(coordinator, "activeAvatarScaleEffects")!;
        activeEffects[ruleId] = originalActiveUntil;
        var notifications = (Dictionary<Guid, CancellationTokenSource>)GetPrivateField(
            coordinator,
            "avatarScaleEffectStateNotifications")!;
        using var oldNotification = new CancellationTokenSource();
        notifications[ruleId] = oldNotification;

        var extendMethod = typeof(BridgeCoordinator).GetMethod(
            "ExtendActiveActivityTimers", BindingFlags.NonPublic | BindingFlags.Instance);
        Assert.NotNull(extendMethod);

        extendMethod!.Invoke(coordinator, [TimeSpan.FromSeconds(15), "Cheer 100"]);

        var updatedHeightSession = heightSessions[ruleId];
        var updatedHeightUntil = (DateTimeOffset)updatedHeightSession!.GetType().GetProperty("ActiveUntil")!.GetValue(updatedHeightSession)!;
        var expected = originalActiveUntil.AddSeconds(15);
        Assert.True(Math.Abs((updatedHeightUntil - expected).TotalSeconds) < 2,
            $"Expected ~{expected:O}, got {updatedHeightUntil:O}");
        Assert.True(Math.Abs((activeEffects[ruleId] - expected).TotalSeconds) < 2,
            $"Expected effect expiry ~{expected:O}, got {activeEffects[ruleId]:O}");
        var updatedCarryover = GetPrivateField(coordinator, "activeAvatarScaleCarryover");
        var updatedCarryoverUntil = (DateTimeOffset)updatedCarryover!.GetType().GetProperty("ActiveUntil")!.GetValue(updatedCarryover)!;
        Assert.True(Math.Abs((updatedCarryoverUntil - expected).TotalSeconds) < 2,
            $"Expected carryover expiry ~{expected:O}, got {updatedCarryoverUntil:O}");
        Assert.False(ReferenceEquals(oldNotification, notifications[ruleId]),
            "Extending an active scale effect must rearm its notification CTS.");
        notifications[ruleId].Cancel();
        notifications[ruleId].Dispose();
    }

    [Fact]
    public async Task HeightSessionExpiry_DoesNotRemoveReplacementWithTheSameSessionId()
    {
        var coordinator = CreateUninitializedCoordinator();
        SetPrivateField(coordinator, "stateGate", new object());
        SetPrivateField(coordinator, "runtimeCancellation", new CancellationTokenSource());
        InitializeRuntimeStateDictionaries(coordinator);

        var ruleId = Guid.NewGuid();
        var sessionId = Guid.NewGuid();
        var oldDelayStarted = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var oldDelay = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var oldDeadline = new ReschedulableActivityDeadline(
            DateTimeOffset.UtcNow.AddMinutes(1),
            (_, _) =>
            {
                oldDelayStarted.TrySetResult();
                return oldDelay.Task;
            });
        var newDeadline = new ReschedulableActivityDeadline(DateTimeOffset.UtcNow.AddMinutes(1));
        var sessionType = typeof(BridgeCoordinator).GetNestedType(
            "ActiveAvatarScaleHeightSessionState", BindingFlags.NonPublic);
        Assert.NotNull(sessionType);
        var oldSession = Activator.CreateInstance(
            sessionType!,
            ruleId,
            sessionId,
            "Old scale",
            "avatar-id",
            1.6,
            2.0,
            DateTimeOffset.UtcNow.AddMinutes(1));
        var replacementSession = Activator.CreateInstance(
            sessionType!,
            ruleId,
            sessionId,
            "Replacement scale",
            "avatar-id",
            1.6,
            2.0,
            DateTimeOffset.UtcNow.AddMinutes(1));
        Assert.NotNull(oldSession);
        Assert.NotNull(replacementSession);
        sessionType!.GetProperty("ActivityDeadline")!.SetValue(oldSession, oldDeadline);
        sessionType.GetProperty("ActivityDeadline")!.SetValue(replacementSession, newDeadline);

        var heightSessions = (IDictionary)GetPrivateField(coordinator, "activeAvatarScaleHeightSessions")!;
        heightSessions[ruleId] = oldSession;
        var scheduleMethod = typeof(BridgeCoordinator).GetMethod(
            "ScheduleAvatarScaleHeightSessionEnd", BindingFlags.NonPublic | BindingFlags.Instance);
        Assert.NotNull(scheduleMethod);

        var oldWorker = Assert.IsAssignableFrom<Task>(scheduleMethod!.Invoke(
            coordinator,
            [ruleId, sessionId, TimeSpan.FromMinutes(1), CancellationToken.None]));
        await oldDelayStarted.Task.WaitAsync(TimeSpan.FromSeconds(1));

        heightSessions[ruleId] = replacementSession;
        oldDelay.TrySetResult();
        await oldWorker;

        Assert.Same(replacementSession, heightSessions[ruleId]);

        ((CancellationTokenSource)GetPrivateField(coordinator, "runtimeCancellation")!).Cancel();
    }

    [Fact]
    public async Task AvatarScaleEffectNotification_EarlyWakeRearmsWhileEffectRemainsActive()
    {
        var coordinator = CreateUninitializedCoordinator();
        SetPrivateField(coordinator, "stateGate", new object());
        InitializeRuntimeStateDictionaries(coordinator);

        var ruleId = Guid.NewGuid();
        var firstDelayStarted = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var extendedDelayStarted = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var rearmedDelayStarted = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var firstDelay = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var extendedDelay = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var rearmedDelay = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var delayCall = 0;
        SetPrivateField(
            coordinator,
            "avatarScaleEffectNotificationDelayFactory",
            new Func<TimeSpan, CancellationToken, Task>((_, _) =>
            {
                return Interlocked.Increment(ref delayCall) switch
                {
                    1 => SignalAndReturn(firstDelayStarted, firstDelay.Task),
                    2 => SignalAndReturn(extendedDelayStarted, extendedDelay.Task),
                    _ => SignalAndReturn(rearmedDelayStarted, rearmedDelay.Task)
                };
        }));

        var activeEffects = (Dictionary<Guid, DateTimeOffset>)GetPrivateField(coordinator, "activeAvatarScaleEffects")!;
        var originalActiveUntil = DateTimeOffset.UtcNow.AddMinutes(10);
        activeEffects[ruleId] = originalActiveUntil;
        var scheduleMethod = typeof(BridgeCoordinator).GetMethod(
            "ScheduleAvatarScaleEffectStateNotification", BindingFlags.NonPublic | BindingFlags.Instance);
        Assert.NotNull(scheduleMethod);
        scheduleMethod!.Invoke(coordinator, [ruleId, TimeSpan.FromMinutes(10), null]);
        await firstDelayStarted.Task.WaitAsync(TimeSpan.FromSeconds(1));

        var extendMethod = typeof(BridgeCoordinator).GetMethod(
            "ExtendActiveActivityTimers", BindingFlags.NonPublic | BindingFlags.Instance);
        Assert.NotNull(extendMethod);
        extendMethod!.Invoke(coordinator, [TimeSpan.FromMinutes(10), "test"]);
        await extendedDelayStarted.Task.WaitAsync(TimeSpan.FromSeconds(1));
        var currentNotification = ((Dictionary<Guid, CancellationTokenSource>)GetPrivateField(
            coordinator,
            "avatarScaleEffectStateNotifications")!)[ruleId];
        scheduleMethod.Invoke(coordinator, [ruleId, TimeSpan.FromMinutes(10), originalActiveUntil]);
        Assert.Same(
            currentNotification,
            ((Dictionary<Guid, CancellationTokenSource>)GetPrivateField(
                coordinator,
                "avatarScaleEffectStateNotifications")!)[ruleId]);

        extendedDelay.TrySetResult();
        await rearmedDelayStarted.Task.WaitAsync(TimeSpan.FromSeconds(1));

        Assert.Contains(ruleId, activeEffects.Keys);
        var notifications = (Dictionary<Guid, CancellationTokenSource>)GetPrivateField(
            coordinator,
            "avatarScaleEffectStateNotifications")!;
        Assert.Contains(ruleId, notifications.Keys);

        currentNotification = notifications[ruleId];
        currentNotification.Cancel();
        notifications.Remove(ruleId);
        firstDelay.TrySetResult();
        rearmedDelay.TrySetResult();
        currentNotification.Dispose();
    }

    [Fact]
    public void PendingAvatarScopedResetResume_RechecksCurrentSameCancellationRecord()
    {
        var source = File.ReadAllText(FindSourceFile("VrcTwitchOscBridge", "Services", "BridgeCoordinator.cs"));
        var body = NormalizeWhitespace(GetMethodBody(
            source,
            "private void ResumePendingAvatarScopedResetsForCurrentAvatar("));

        Assert.Contains("ActivityDeadline.WaitAsync(cancellation.Token)", body, StringComparison.Ordinal);
        Assert.Contains("ReferenceEquals(current.Cancellation, cancellation)", body, StringComparison.Ordinal);
        Assert.Contains("current.SourceAvatarId", body, StringComparison.Ordinal);
        Assert.Contains("currentVrChatAvatarId", body, StringComparison.Ordinal);
        Assert.DoesNotContain("ReferenceEquals(currentPendingReset, pendingReset)", body, StringComparison.Ordinal);
    }

    [Fact]
    public async Task PendingAvatarScopedResetResume_CleansReplacementWithTheSameCancellation()
    {
        var coordinator = CreateUninitializedCoordinator();
        SetPrivateField(coordinator, "stateGate", new object());
        SetPrivateField(coordinator, "runtimeCancellation", new CancellationTokenSource());
        SetPrivateField(coordinator, "currentVrChatAvatarId", "avatar-id");
        InitializeRuntimeStateDictionaries(coordinator);
        SetPrivateField(coordinator, "activeMovementCleanupTasks", new HashSet<Task>());

        var ruleId = Guid.NewGuid();
        var rule = BridgeRuntimeConfiguration.CreateManualTestSnapshot(
            new TriggerRule
            {
                Id = ruleId,
                Name = "Pending scale",
                ActionType = OscActionType.AvatarParameter,
                ParameterName = "VRCEmote",
                ParameterValue = "1",
                ResetValue = "0"
            },
            isGlobalOverride: true,
            profile: null);
        var actionType = typeof(BridgeCoordinator).GetNestedType("ResolvedRuleAction", BindingFlags.NonPublic);
        var pendingType = typeof(BridgeCoordinator).GetNestedType("PendingResetState", BindingFlags.NonPublic);
        Assert.NotNull(actionType);
        Assert.NotNull(pendingType);
        var action = Activator.CreateInstance(
            actionType!,
            BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic,
            binder: null,
            args:
            [
                new[] { new byte[] { 1 } },
                Array.Empty<byte[]>(),
                "pending",
                string.Empty,
                string.Empty,
                string.Empty,
                string.Empty,
                Array.Empty<OscObservedValue>(),
                Array.Empty<OscObservedValue>(),
                null
            ],
            culture: null);
        Assert.NotNull(action);

        var cancellation = new CancellationTokenSource();
        var oldDelayStarted = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var oldDelay = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var replacementDelayStarted = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var replacementDelay = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var oldDelayCall = 0;
        var replacementDelayCall = 0;
        var oldDeadline = new ReschedulableActivityDeadline(
            DateTimeOffset.UtcNow.AddMilliseconds(25),
            (remaining, cancellationToken) =>
            {
                if (Interlocked.Increment(ref oldDelayCall) == 1)
                {
                    oldDelayStarted.TrySetResult();
                    return oldDelay.Task;
                }

                return Task.Delay(remaining, cancellationToken);
            });
        var replacementDeadline = new ReschedulableActivityDeadline(
            DateTimeOffset.UtcNow.AddMilliseconds(50),
            (remaining, cancellationToken) =>
            {
                if (Interlocked.Increment(ref replacementDelayCall) == 1)
                {
                    replacementDelayStarted.TrySetResult();
                    return replacementDelay.Task;
                }

                return Task.Delay(remaining, cancellationToken);
            });
        var pendingResets = (IDictionary)GetPrivateField(coordinator, "pendingResets")!;
        var oldPendingReset = CreatePendingResetState(
            pendingType!,
            rule,
            action,
            cancellation,
            DateTimeOffset.UtcNow.AddMilliseconds(25),
            oldDeadline);
        pendingResets[ruleId] = oldPendingReset;

        InvokePrivate(coordinator, "ResumePendingAvatarScopedResetsForCurrentAvatar", "avatar-id");
        await oldDelayStarted.Task.WaitAsync(TimeSpan.FromSeconds(1));
        var cleanupTask = Assert.IsAssignableFrom<Task>(typeof(BridgeCoordinator).GetMethod(
            "WaitForActiveMovementCleanupTasksAsync",
            BindingFlags.NonPublic | BindingFlags.Instance)!.Invoke(coordinator, null));

        var replacementPendingReset = CreatePendingResetState(
            pendingType,
            rule,
            action,
            cancellation,
            DateTimeOffset.UtcNow.AddMilliseconds(50),
            replacementDeadline);
        pendingResets[ruleId] = replacementPendingReset;
        oldDelay.TrySetResult();
        await replacementDelayStarted.Task.WaitAsync(TimeSpan.FromSeconds(1));
        Assert.Same(replacementPendingReset, pendingResets[ruleId]);

        replacementDelay.TrySetResult();
        await cleanupTask;
        Assert.Empty(pendingResets);
        ((CancellationTokenSource)GetPrivateField(coordinator, "runtimeCancellation")!).Cancel();
    }

    [Fact]
    public void ExtendActiveActivityTimers_ExtendsSupporterDerivedWindowsWithTheirTransitionOffset()
    {
        var coordinator = CreateUninitializedCoordinator();
        SetPrivateField(coordinator, "stateGate", new object());
        InitializeRuntimeStateDictionaries(coordinator);

        var ruleId = Guid.NewGuid();
        var sessionId = Guid.NewGuid();
        var extension = TimeSpan.FromSeconds(15);
        var paidActiveUntil = DateTimeOffset.UtcNow.AddSeconds(20);
        var transitionOffset = TimeSpan.FromSeconds(5);
        var heightActiveUntil = paidActiveUntil.Add(transitionOffset);
        var supporterStateType = typeof(BridgeCoordinator).GetNestedType(
            "ActiveAvatarScaleSupporterGrowthState", BindingFlags.NonPublic);
        var supporterState = Activator.CreateInstance(supporterStateType!, nonPublic: true);
        Assert.NotNull(supporterState);
        supporterStateType!.GetProperty("PaidActiveUntil")!.SetValue(supporterState, paidActiveUntil);
        supporterStateType.GetProperty("ActivityDeadline")!.SetValue(
            supporterState,
            new ReschedulableActivityDeadline(paidActiveUntil));
        supporterStateType.GetProperty("SessionCancellation")!.SetValue(
            supporterState,
            new CancellationTokenSource());

        var supporterStates = (IDictionary)GetPrivateField(coordinator, "avatarScaleSupporterGrowthStates")!;
        supporterStates.Add(ruleId, supporterState);

        var heightSessionType = typeof(BridgeCoordinator).GetNestedType(
            "ActiveAvatarScaleHeightSessionState", BindingFlags.NonPublic);
        var heightSession = Activator.CreateInstance(
            heightSessionType!,
            ruleId,
            sessionId,
            "Supporter Growth",
            "avatar-id",
            1.6,
            2.0,
            heightActiveUntil);
        Assert.NotNull(heightSession);
        heightSessionType!.GetProperty("ActivityDeadline")!.SetValue(
            heightSession,
            new ReschedulableActivityDeadline(heightActiveUntil));
        var heightSessions = (IDictionary)GetPrivateField(coordinator, "activeAvatarScaleHeightSessions")!;
        heightSessions.Add(ruleId, heightSession);

        var carryoverType = typeof(BridgeCoordinator).GetNestedType(
            "ActiveAvatarScaleCarryoverState", BindingFlags.NonPublic);
        var carryover = Activator.CreateInstance(
            carryoverType!,
            Guid.NewGuid(),
            ruleId,
            sessionId,
            0L,
            "Supporter Growth",
            "avatar-id",
            2.0,
            1.6,
            heightActiveUntil,
            true);
        SetPrivateField(coordinator, "activeAvatarScaleCarryover", carryover);

        var extendMethod = typeof(BridgeCoordinator).GetMethod(
            "ExtendActiveActivityTimers", BindingFlags.NonPublic | BindingFlags.Instance);
        Assert.NotNull(extendMethod);
        extendMethod!.Invoke(coordinator, [extension, "Cheer 100"]);

        var updatedSupporterState = supporterStates[ruleId];
        var updatedPaidUntil = (DateTimeOffset)updatedSupporterState!.GetType().GetProperty("PaidActiveUntil")!.GetValue(updatedSupporterState)!;
        var updatedHeightSession = heightSessions[ruleId];
        var updatedHeightUntil = (DateTimeOffset)updatedHeightSession!.GetType().GetProperty("ActiveUntil")!.GetValue(updatedHeightSession)!;
        var updatedCarryover = GetPrivateField(coordinator, "activeAvatarScaleCarryover");
        var updatedCarryoverUntil = (DateTimeOffset)updatedCarryover!.GetType().GetProperty("ActiveUntil")!.GetValue(updatedCarryover)!;

        Assert.True(Math.Abs((updatedPaidUntil - paidActiveUntil.Add(extension)).TotalSeconds) < 2);
        Assert.True(Math.Abs((updatedHeightUntil - heightActiveUntil.Add(extension)).TotalSeconds) < 2);
        Assert.True(Math.Abs((updatedCarryoverUntil - heightActiveUntil.Add(extension)).TotalSeconds) < 2);
        Assert.Equal(
            transitionOffset.TotalSeconds,
            (updatedHeightUntil - updatedPaidUntil).TotalSeconds,
            precision: 1);

        ((CancellationTokenSource)supporterStateType.GetProperty("SessionCancellation")!.GetValue(supporterState)!).Dispose();
    }

    [Fact]
    public void HeightSessionCompletion_RechecksIdentityThroughItsReschedulableDeadline()
    {
        var source = File.ReadAllText(FindSourceFile("VrcTwitchOscBridge", "Services", "BridgeCoordinator.cs"));
        var body = NormalizeWhitespace(GetMethodBody(source, "private Task ScheduleAvatarScaleHeightSessionEnd("));
        var endBody = NormalizeWhitespace(GetMethodBody(source, "private bool EndAvatarScaleHeightSession("));

        Assert.Contains("expectedActivityDeadline.WaitAsync", body, StringComparison.Ordinal);
        Assert.Contains("session.SessionId != sessionId", body, StringComparison.Ordinal);
        Assert.Contains("ReferenceEquals(currentSession.ActivityDeadline, expectedActivityDeadline)", body, StringComparison.Ordinal);
        Assert.Contains("EndAvatarScaleHeightSession(ruleId, sessionId, expectedActivityDeadline)", body, StringComparison.Ordinal);
        Assert.Contains("session.ActiveUntil <= DateTimeOffset.UtcNow", endBody, StringComparison.Ordinal);
        Assert.Contains("session.ActivityDeadline.Deadline <= DateTimeOffset.UtcNow", endBody, StringComparison.Ordinal);
    }

    [Fact]
    public void ExtendActiveActivityTimers_ExtendsMatchingResumeExpiryThroughSerializedWriter()
    {
        var coordinator = CreateUninitializedCoordinator();
        SetPrivateField(coordinator, "stateGate", new object());
        InitializeRuntimeStateDictionaries(coordinator);
        var resumeService = new RecordingActivityResumeService();
        SetPrivateField(coordinator, "activityResumeService", resumeService);

        var originalExpiresAt = DateTimeOffset.UtcNow.AddSeconds(20);
        var activity = new ResumeActivity
        {
            Type = ResumeActivityType.AvatarScale,
            RuleId = Guid.NewGuid(),
            ExpiresAt = originalExpiresAt
        };
        var rule = new AvatarScaleRule { Name = "Grow", ActiveTimeSeconds = 20, RestoreHeightMeters = 1.6 };
        var snapshot = BridgeRuntimeConfiguration.CreateManualTestSnapshot(rule, new AvatarScaleSafetySettings());
        var sequenceType = typeof(BridgeCoordinator).GetNestedType(
            "ActiveAvatarScaleRestoreSequenceState", BindingFlags.NonPublic);
        Assert.NotNull(sequenceType);
        var sequence = Activator.CreateInstance(sequenceType!,
            1L, "avatar-id", 2.0, 1.6, originalExpiresAt, "Grow", 0.0,
            true, false, snapshot, 20.0, false, 1.6);
        Assert.NotNull(sequence);
        sequenceType!.GetProperty("ActivityResumeEntry")!.SetValue(sequence, activity);
        sequenceType.GetProperty("ActivityDeadline")!.SetValue(
            sequence,
            new ReschedulableActivityDeadline(originalExpiresAt));
        SetPrivateField(coordinator, "activeAvatarScaleRestoreSequence", sequence);

        var extendMethod = typeof(BridgeCoordinator).GetMethod(
            "ExtendActiveActivityTimers", BindingFlags.NonPublic | BindingFlags.Instance);
        Assert.NotNull(extendMethod);

        extendMethod!.Invoke(coordinator, [TimeSpan.FromSeconds(15), "Cheer 100"]);

        var expected = originalExpiresAt.AddSeconds(15);
        Assert.True(Math.Abs((activity.ExpiresAt!.Value - expected).TotalSeconds) < 2,
            $"Expected resume expiry ~{expected:O}, got {activity.ExpiresAt:O}");
        Assert.True(resumeService.CommitCalled.Wait(TimeSpan.FromSeconds(1)),
            "Extending a persisted activity must use the resume service writer.");
    }

    [Fact]
    public async Task ExtendActiveActivityTimers_RoundTripsExtendedResumeExpiryThroughActivityResumeFile()
    {
        var testDirectory = Path.Combine(
            Path.GetTempPath(),
            "CrystalRelayActivityResumeTests",
            Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(testDirectory);
        var snapshotPath = Path.Combine(testDirectory, "activity-resume.json");
        var ruleId = Guid.NewGuid();
        var originalExpiresAt = DateTimeOffset.UtcNow.AddMinutes(1);

        try
        {
            await using var activityResumeService = new ActivityResumeService(snapshotPath);
            var activity = new ResumeActivity
            {
                Type = ResumeActivityType.AvatarScale,
                RuleId = ruleId,
                ExpiresAt = originalExpiresAt
            };
            await activityResumeService.RecordActivityStartedAsync(activity, "avatar-id");
            var observingService = new PersistingActivityResumeService(activityResumeService);

            var coordinator = CreateUninitializedCoordinator();
            SetPrivateField(coordinator, "stateGate", new object());
            InitializeRuntimeStateDictionaries(coordinator);
            SetPrivateField(coordinator, "activityResumeService", observingService);

            var sequenceType = typeof(BridgeCoordinator).GetNestedType(
                "ActiveAvatarScaleRestoreSequenceState", BindingFlags.NonPublic);
            Assert.NotNull(sequenceType);
            var rule = new AvatarScaleRule { Id = ruleId, Name = "Grow", ActiveTimeSeconds = 60, RestoreHeightMeters = 1.6 };
            var snapshot = BridgeRuntimeConfiguration.CreateManualTestSnapshot(rule, new AvatarScaleSafetySettings());
            var sequence = Activator.CreateInstance(
                sequenceType!,
                1L,
                "avatar-id",
                2.0,
                1.6,
                originalExpiresAt,
                "Grow",
                0.0,
                true,
                false,
                snapshot,
                60.0,
                false,
                1.6);
            Assert.NotNull(sequence);
            sequenceType!.GetProperty("ActivityResumeEntry")!.SetValue(sequence, activity);
            sequenceType.GetProperty("ActivityDeadline")!.SetValue(
                sequence,
                new ReschedulableActivityDeadline(originalExpiresAt));
            SetPrivateField(coordinator, "activeAvatarScaleRestoreSequence", sequence);

            var extendMethod = typeof(BridgeCoordinator).GetMethod(
                "ExtendActiveActivityTimers", BindingFlags.NonPublic | BindingFlags.Instance);
            Assert.NotNull(extendMethod);
            extendMethod!.Invoke(coordinator, [TimeSpan.FromMinutes(1), "Cheer 100"]);
            await observingService.CommitCompleted.Task.WaitAsync(TimeSpan.FromSeconds(1));

            await using var reloadedService = new ActivityResumeService(snapshotPath);
            await reloadedService.LoadPendingAsync();
            var persistedActivity = Assert.Single(reloadedService.GetPendingActivities());
            Assert.True(persistedActivity.ExpiresAt >= originalExpiresAt.AddMinutes(1));
        }
        finally
        {
            if (Directory.Exists(testDirectory))
            {
                Directory.Delete(testDirectory, recursive: true);
            }
        }
    }

    private sealed class RecordingActivityResumeService : IActivityResumeService
    {
        public ManualResetEventSlim CommitCalled { get; } = new();

        public Task LoadPendingAsync() => Task.CompletedTask;

        public bool HasPendingResume => false;

        public bool IsPendingForAvatar(string avatarId) => false;

        public IReadOnlyList<ResumeActivity> GetPendingActivities() => [];

        public Task RemoveExpiredActivitiesAsync() => Task.CompletedTask;

        public Task RecordActivityStartedAsync(ResumeActivity activity, string avatarId) => Task.CompletedTask;

        public Task RecordActivityEndedAsync(Guid ruleId, ResumeActivity? expectedActivity = null) => Task.CompletedTask;

        public Task RemoveActivityAsync(ResumeActivity activity) => Task.CompletedTask;

        public Task ClearAllAsync() => Task.CompletedTask;

        public Task CommitAsync()
        {
            CommitCalled.Set();
            return Task.CompletedTask;
        }

        public Task DeleteStaleFileIfPresentAsync() => Task.CompletedTask;
    }

    private sealed class PersistingActivityResumeService : IActivityResumeService
    {
        private readonly IActivityResumeService inner;

        public PersistingActivityResumeService(IActivityResumeService inner)
        {
            this.inner = inner;
        }

        public TaskCompletionSource CommitCompleted { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);

        public Task LoadPendingAsync() => inner.LoadPendingAsync();

        public bool HasPendingResume => inner.HasPendingResume;

        public bool IsPendingForAvatar(string avatarId) => inner.IsPendingForAvatar(avatarId);

        public IReadOnlyList<ResumeActivity> GetPendingActivities() => inner.GetPendingActivities();

        public Task RemoveExpiredActivitiesAsync() => inner.RemoveExpiredActivitiesAsync();

        public Task RecordActivityStartedAsync(ResumeActivity activity, string avatarId) =>
            inner.RecordActivityStartedAsync(activity, avatarId);

        public Task RecordActivityEndedAsync(Guid ruleId, ResumeActivity? expectedActivity = null) =>
            inner.RecordActivityEndedAsync(ruleId, expectedActivity);

        public Task RemoveActivityAsync(ResumeActivity activity) => inner.RemoveActivityAsync(activity);

        public Task ClearAllAsync() => inner.ClearAllAsync();

        public async Task CommitAsync()
        {
            await inner.CommitAsync();
            CommitCompleted.TrySetResult();
        }

        public Task DeleteStaleFileIfPresentAsync() => inner.DeleteStaleFileIfPresentAsync();
    }

    private static object CreatePendingResetState(
        Type pendingType,
        TriggerRuleSnapshot rule,
        object action,
        CancellationTokenSource cancellation,
        DateTimeOffset dueAt,
        ReschedulableActivityDeadline activityDeadline)
    {
        var pendingReset = Activator.CreateInstance(
            pendingType,
            BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic,
            binder: null,
            args:
            [
                rule.Id,
                rule.Name,
                rule,
                action,
                Array.Empty<byte[]>(),
                cancellation,
                dueAt,
                string.Empty,
                string.Empty,
                "avatar-id",
                true,
                Array.Empty<OscObservedValue>(),
                Array.Empty<string>(),
                Guid.Empty
            ],
            culture: null);
        Assert.NotNull(pendingReset);
        pendingType.GetProperty("ActivityDeadline")!.SetValue(pendingReset, activityDeadline);
        return pendingReset!;
    }

    private static Task SignalAndReturn(TaskCompletionSource signal, Task task)
    {
        signal.TrySetResult();
        return task;
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
}
