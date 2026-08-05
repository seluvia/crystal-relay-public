using System.Collections.Generic;
using System.Reflection;
using System.Runtime.CompilerServices;
using VrcTwitchOscBridge.Models;
using VrcTwitchOscBridge.Services;
using Xunit;

namespace VrcTwitchOscBridge.Tests;

public class ScaleTimerFairnessTests
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

    [Fact]
    public void ScheduleAvatarScaleRestoreSequence_Tier1ThirtySecondAfterSixtySecond_UsesHighestSeenSixty()
    {
        var coordinator = CreateUninitializedCoordinator();
        var stateGate = new object();
        var runtimeCancellation = new CancellationTokenSource();
        SetPrivateField(coordinator, "stateGate", stateGate);
        SetPrivateField(coordinator, "runtimeCancellation", runtimeCancellation);
        SetPrivateField(coordinator, "nextAvatarScaleRestoreSequenceId", 0L);
        SetPrivateField(coordinator, "currentScaleWindowHighestSeenActiveTimeSeconds", 0.0);
        SetPrivateField(coordinator, "currentScaleWindowIsPaySystemTier", false);
        // ApplyAvatarScaleHeightLimits -> TryGetObservedFloatLocked reads these dictionaries.
        // GetUninitializedObject skips field initializers, so they must be provided explicitly.
        SetPrivateField(coordinator, "avatarParameterValues", new Dictionary<string, OscObservedValue>());
        SetPrivateField(coordinator, "avatarScaleValues", new Dictionary<string, OscObservedValue>(System.StringComparer.Ordinal));

        var safety = new AvatarScaleSafetySettings();
        var sixtySecondRule = new AvatarScaleRule
        {
            Name = "Big Grow",
            ActiveTimeSeconds = 60,
            RestoreHeightMeters = 1.6,
            TargetHeightMeters = 2.0,
        };
        var sixtySnapshot = BridgeRuntimeConfiguration.CreateManualTestSnapshot(sixtySecondRule, safety);

        var thirtySecondRule = new AvatarScaleRule
        {
            Name = "Small Shrink",
            ActiveTimeSeconds = 30,
            RestoreHeightMeters = 1.6,
            TargetHeightMeters = 0.8,
        };
        var thirtySnapshot = BridgeRuntimeConfiguration.CreateManualTestSnapshot(thirtySecondRule, safety);

        var scheduleMethod = typeof(BridgeCoordinator).GetMethod(
            "ScheduleAvatarScaleRestoreSequence",
            BindingFlags.NonPublic | BindingFlags.Instance);
        Assert.NotNull(scheduleMethod);

        try
        {
            // First trigger: 60s grow
            scheduleMethod!.Invoke(coordinator, [sixtySnapshot, false, 2.0, null, null, null]);

            var sequenceAfterFirst = GetPrivateField(coordinator, "activeAvatarScaleRestoreSequence");
            Assert.NotNull(sequenceAfterFirst);
            var activeUntilAfterFirst = (DateTimeOffset)sequenceAfterFirst!.GetType()
                .GetProperty("ActiveUntil", BindingFlags.Public | BindingFlags.Instance)!
                .GetValue(sequenceAfterFirst)!;
            var highestAfterFirst = (double)GetPrivateField(coordinator, "currentScaleWindowHighestSeenActiveTimeSeconds")!;
            Assert.Equal(60, highestAfterFirst);
            Assert.True((activeUntilAfterFirst - DateTimeOffset.UtcNow).TotalSeconds >= 55);

            // Second trigger: 30s shrink — should use highest-seen 60s, not 30s
            scheduleMethod.Invoke(coordinator, [thirtySnapshot, false, 0.8, null, null, null]);

            var sequenceAfterSecond = GetPrivateField(coordinator, "activeAvatarScaleRestoreSequence");
            Assert.NotNull(sequenceAfterSecond);
            var activeUntilAfterSecond = (DateTimeOffset)sequenceAfterSecond!.GetType()
                .GetProperty("ActiveUntil", BindingFlags.Public | BindingFlags.Instance)!
                .GetValue(sequenceAfterSecond)!;
            var highestAfterSecond = (double)GetPrivateField(coordinator, "currentScaleWindowHighestSeenActiveTimeSeconds")!;
            Assert.Equal(60, highestAfterSecond);
            // Timer should be ~60s from now (the highest seen), not 30s
            var remainingAfterSecond = (activeUntilAfterSecond - DateTimeOffset.UtcNow).TotalSeconds;
            Assert.True(remainingAfterSecond >= 55, $"Expected >= 55s remaining after 60s highest-seen, got {remainingAfterSecond}");
        }
        finally
        {
            // Cancel the linked restore-sequence tokens so the background restore tasks exit cleanly.
            runtimeCancellation.Cancel();
            runtimeCancellation.Dispose();
        }
    }

    [Fact]
    public void ScheduleAvatarScaleRestoreSequence_Tier2PaySystem_ResetsToOwnActiveTime()
    {
        var coordinator = CreateUninitializedCoordinator();
        var stateGate = new object();
        var runtimeCancellation = new CancellationTokenSource();
        SetPrivateField(coordinator, "stateGate", stateGate);
        SetPrivateField(coordinator, "runtimeCancellation", runtimeCancellation);
        SetPrivateField(coordinator, "nextAvatarScaleRestoreSequenceId", 0L);
        SetPrivateField(coordinator, "currentScaleWindowHighestSeenActiveTimeSeconds", 0.0);
        SetPrivateField(coordinator, "currentScaleWindowIsPaySystemTier", false);
        SetPrivateField(coordinator, "avatarParameterValues", new Dictionary<string, OscObservedValue>());
        SetPrivateField(coordinator, "avatarScaleValues", new Dictionary<string, OscObservedValue>(System.StringComparer.Ordinal));

        var safety = new AvatarScaleSafetySettings();
        var tier1Rule = new AvatarScaleRule { Name = "Grow", ActiveTimeSeconds = 60, RestoreHeightMeters = 1.6, TargetHeightMeters = 2.0 };
        var tier1Snapshot = BridgeRuntimeConfiguration.CreateManualTestSnapshot(tier1Rule, safety);
        var tier2Rule = new AvatarScaleRule { Name = "Cash Pay", ActiveTimeSeconds = 20, RestoreHeightMeters = 1.6, TargetHeightMeters = 1.4 };
        var tier2Snapshot = BridgeRuntimeConfiguration.CreateManualTestSnapshot(tier2Rule, safety) with { IsPaySystemTrigger = true };

        var scheduleMethod = typeof(BridgeCoordinator).GetMethod("ScheduleAvatarScaleRestoreSequence", BindingFlags.NonPublic | BindingFlags.Instance);
        Assert.NotNull(scheduleMethod);

        try
        {
            scheduleMethod!.Invoke(coordinator, [tier1Snapshot, false, 2.0, null, null, null]);
            scheduleMethod.Invoke(coordinator, [tier2Snapshot, false, 1.4, null, null, null]);

            var sequence = GetPrivateField(coordinator, "activeAvatarScaleRestoreSequence");
            Assert.NotNull(sequence);
            var activeUntil = (DateTimeOffset)sequence!.GetType().GetProperty("ActiveUntil")!.GetValue(sequence)!;
            var remaining = (activeUntil - DateTimeOffset.UtcNow).TotalSeconds;
            Assert.True(remaining <= 21 && remaining >= 15, $"Tier-2 should reset to ~20s, got {remaining}");
            Assert.True((bool)sequence.GetType().GetProperty("IsPaySystemTier")!.GetValue(sequence)!);
        }
        finally
        {
            runtimeCancellation.Cancel();
            runtimeCancellation.Dispose();
        }
    }

    [Fact]
    public void ScheduleAvatarScaleRestoreSequence_Tier1AfterTier2Active_DoesNotPreempt()
    {
        var coordinator = CreateUninitializedCoordinator();
        var stateGate = new object();
        var runtimeCancellation = new CancellationTokenSource();
        SetPrivateField(coordinator, "stateGate", stateGate);
        SetPrivateField(coordinator, "runtimeCancellation", runtimeCancellation);
        SetPrivateField(coordinator, "nextAvatarScaleRestoreSequenceId", 0L);
        SetPrivateField(coordinator, "currentScaleWindowHighestSeenActiveTimeSeconds", 0.0);
        SetPrivateField(coordinator, "currentScaleWindowIsPaySystemTier", false);
        SetPrivateField(coordinator, "avatarParameterValues", new Dictionary<string, OscObservedValue>());
        SetPrivateField(coordinator, "avatarScaleValues", new Dictionary<string, OscObservedValue>(System.StringComparer.Ordinal));

        var safety = new AvatarScaleSafetySettings();
        var tier2Rule = new AvatarScaleRule { Name = "Cash Pay", ActiveTimeSeconds = 20, RestoreHeightMeters = 1.6, TargetHeightMeters = 1.4 };
        var tier2Snapshot = BridgeRuntimeConfiguration.CreateManualTestSnapshot(tier2Rule, safety) with { IsPaySystemTrigger = true };
        var tier1Rule = new AvatarScaleRule { Name = "Grow", ActiveTimeSeconds = 60, RestoreHeightMeters = 1.6, TargetHeightMeters = 2.0 };
        var tier1Snapshot = BridgeRuntimeConfiguration.CreateManualTestSnapshot(tier1Rule, safety);

        var scheduleMethod = typeof(BridgeCoordinator).GetMethod("ScheduleAvatarScaleRestoreSequence", BindingFlags.NonPublic | BindingFlags.Instance);
        Assert.NotNull(scheduleMethod);

        try
        {
            scheduleMethod!.Invoke(coordinator, [tier2Snapshot, false, 1.4, null, null, null]);
            var sequenceAfterTier2 = GetPrivateField(coordinator, "activeAvatarScaleRestoreSequence");
            Assert.NotNull(sequenceAfterTier2);

            scheduleMethod.Invoke(coordinator, [tier1Snapshot, false, 2.0, null, null, null]);
            var sequenceAfterTier1 = GetPrivateField(coordinator, "activeAvatarScaleRestoreSequence");
            Assert.NotNull(sequenceAfterTier1);

            Assert.Equal(
                sequenceAfterTier2!.GetType().GetProperty("SequenceId")!.GetValue(sequenceAfterTier2),
                sequenceAfterTier1!.GetType().GetProperty("SequenceId")!.GetValue(sequenceAfterTier1));
        }
        finally
        {
            runtimeCancellation.Cancel();
            runtimeCancellation.Dispose();
        }
    }
}
