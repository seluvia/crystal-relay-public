using System;
using System.Collections.Generic;
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

        SetPrivateField(coordinator, "activeFloatRedeemSessions",
            CreateTypedDictionary(typeof(Guid), floatSessionStateType!));
        SetPrivateField(coordinator, "pendingResets",
            CreateTypedDictionary(typeof(Guid), pendingResetStateType!));
        SetPrivateField(coordinator, "actionLanes",
            CreateTypedDictionary(typeof(string), movementLaneStateType!));
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
            true, false, snapshot, 20.0, false);
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
}
