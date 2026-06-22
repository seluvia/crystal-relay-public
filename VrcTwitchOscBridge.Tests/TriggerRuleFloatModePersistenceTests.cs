using System.Reflection;
using VrcTwitchOscBridge.Models;
using VrcTwitchOscBridge.Services;
using Xunit;

namespace VrcTwitchOscBridge.Tests;

public sealed class TriggerRuleFloatModePersistenceTests
{
    [Fact]
    public void ToRule_MissingNewFields_AppliesSafeDefaults()
    {
        // Simulates a JSON file written by an older Crystal Relay that did
        // not know about the float action fields.
        var old = new SettingsStore.PersistedTriggerRule
        {
            Id = Guid.NewGuid(),
            ParameterName = "VRCEmote",
            ParameterType = OscParameterType.Float,
            ParameterValue = "0.5",
            ResetValue = "0",
            FloatValueMode = FloatValueMode.Decimal,
            // Intentionally do NOT set FloatActionMode / FloatRangeMin / etc.
        };
        var rule = SettingsStore.ToRule(old);
        Assert.Equal(FloatActionMode.Set, rule.FloatActionMode);
        Assert.Equal(0.0, rule.FloatRangeMin);
        Assert.Equal(1.0, rule.FloatRangeMax);
        Assert.Equal(0.1, rule.FloatCycleStep);
        Assert.Equal(0.1, rule.FloatAddAmount);
        Assert.Equal(0.1, rule.FloatSubtractAmount);
        Assert.Equal(0.1, rule.FloatAddSubtractAmount);
        Assert.Equal(1.5, rule.FloatMultiplyFactor);
        Assert.Equal(1.0, rule.FloatToggleOnValue);
        Assert.Equal(0.0, rule.FloatToggleOffValue);
        Assert.Equal(200, rule.FloatGlitchyIntervalMs);
        Assert.Equal(0.5, rule.FloatPulseSeconds);
        Assert.Equal(FloatClampMode.ZeroToOne, rule.FloatClampMode);
    }

    [Fact]
    public void RoundTrip_AllNewFieldsPreserved()
    {
        var original = new TriggerRule
        {
            ParameterType = OscParameterType.Float,
            FloatActionMode = FloatActionMode.Glitchy,
            FloatRangeMin = 0.1,
            FloatRangeMax = 0.9,
            FloatCycleStep = 0.05,
            FloatAddAmount = 0.2,
            FloatSubtractAmount = 0.3,
            FloatAddSubtractAmount = -0.4,
            FloatMultiplyFactor = 2.5,
            FloatToggleOnValue = 0.8,
            FloatToggleOffValue = 0.2,
            FloatGlitchyIntervalMs = 350,
            FloatPulseSeconds = 1.25,
            FloatClampMode = FloatClampMode.MinToMax,
        };
        var persisted = ToPersistedViaReflection(original);
        var roundTripped = SettingsStore.ToRule(persisted);
        Assert.Equal(original.FloatActionMode, roundTripped.FloatActionMode);
        Assert.Equal(original.FloatRangeMin, roundTripped.FloatRangeMin);
        Assert.Equal(original.FloatRangeMax, roundTripped.FloatRangeMax);
        Assert.Equal(original.FloatCycleStep, roundTripped.FloatCycleStep);
        Assert.Equal(original.FloatAddAmount, roundTripped.FloatAddAmount);
        Assert.Equal(original.FloatSubtractAmount, roundTripped.FloatSubtractAmount);
        Assert.Equal(original.FloatAddSubtractAmount, roundTripped.FloatAddSubtractAmount);
        Assert.Equal(original.FloatMultiplyFactor, roundTripped.FloatMultiplyFactor);
        Assert.Equal(original.FloatToggleOnValue, roundTripped.FloatToggleOnValue);
        Assert.Equal(original.FloatToggleOffValue, roundTripped.FloatToggleOffValue);
        Assert.Equal(original.FloatGlitchyIntervalMs, roundTripped.FloatGlitchyIntervalMs);
        Assert.Equal(original.FloatPulseSeconds, roundTripped.FloatPulseSeconds);
        Assert.Equal(original.FloatClampMode, roundTripped.FloatClampMode);
    }

    [Fact]
    public void ToRule_OutOfRangeFloatActionMode_FallsBackToSet()
    {
        var old = new SettingsStore.PersistedTriggerRule
        {
            ParameterType = OscParameterType.Float,
            FloatActionMode = (FloatActionMode)999,
        };
        var rule = SettingsStore.ToRule(old);
        Assert.Equal(FloatActionMode.Set, rule.FloatActionMode);
    }

    // The ToPersistedRule method on SettingsStore is private. We invoke it
    // via reflection so we can verify the writing side of the round-trip
    // without touching the real AppData folder.
    private static SettingsStore.PersistedTriggerRule ToPersistedViaReflection(TriggerRule rule)
    {
        var method = typeof(SettingsStore).GetMethod(
            "ToPersistedRule",
            BindingFlags.Static | BindingFlags.NonPublic);
        Assert.NotNull(method);
        return (SettingsStore.PersistedTriggerRule)method!.Invoke(null, new object[] { rule })!;
    }
}
