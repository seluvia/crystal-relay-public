using System.Reflection;
using System.Text.Json;
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

    [Fact]
    public void ToRule_OldFloatTransitionSeconds_MigratesToInAndOut()
    {
        // Simulates a JSON file written by a Crystal Relay that only had
        // the single FloatTransitionSeconds field.
        var persisted = new SettingsStore.PersistedTriggerRule
        {
            Id = Guid.NewGuid(),
            ParameterType = OscParameterType.Float,
            ParameterValue = "0.5",
            ResetValue = "0",
            FloatValueMode = FloatValueMode.Decimal,
            FloatTransitionSeconds = 2.0,
            // Intentionally do NOT set FloatTransitionInSeconds / FloatTransitionOutSeconds.
        };
        var rule = SettingsStore.ToRule(persisted);
        Assert.Equal(2.0, rule.FloatTransitionInSeconds);
        Assert.Equal(2.0, rule.FloatTransitionOutSeconds);
    }

    [Fact]
    public void ToRule_NewFieldsAlreadySet_AreNotOverwrittenByMigration()
    {
        // If a newer save file already has the new fields populated, the
        // migration must not overwrite them with the old value.
        var persisted = new SettingsStore.PersistedTriggerRule
        {
            Id = Guid.NewGuid(),
            ParameterType = OscParameterType.Float,
            FloatTransitionSeconds = 2.0,
            FloatTransitionInSeconds = 0.5,
            FloatTransitionOutSeconds = 1.5,
        };
        var rule = SettingsStore.ToRule(persisted);
        Assert.Equal(0.5, rule.FloatTransitionInSeconds);
        Assert.Equal(1.5, rule.FloatTransitionOutSeconds);
    }

    [Fact]
    public void ToRule_LegacyJson_DeserializesAndMigrates()
    {
        // End-to-end: build a JSON literal with the old FloatTransitionSeconds
        // key, deserialize it through the real serializer, and confirm the
        // migration fires. This guards the WhenWritingDefault JsonIgnore pattern
        // and the production property name.
        const string legacyJson = @"{""Id"":""7d5c7c2e-7c70-4d0e-9e7c-1a2b3c4d5e6f"",""ParameterType"":1,""FloatValueMode"":1,""FloatTransitionSeconds"":2.0}";
        var persisted = JsonSerializer.Deserialize<SettingsStore.PersistedTriggerRule>(legacyJson);
        Assert.NotNull(persisted);
        Assert.Equal(2.0, persisted!.FloatTransitionSeconds);
        var rule = SettingsStore.ToRule(persisted);
        Assert.Equal(2.0, rule.FloatTransitionInSeconds);
        Assert.Equal(2.0, rule.FloatTransitionOutSeconds);
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
