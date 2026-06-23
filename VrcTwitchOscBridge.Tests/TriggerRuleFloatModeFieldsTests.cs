using VrcTwitchOscBridge.Models;
using Xunit;

namespace VrcTwitchOscBridge.Tests;

public sealed class TriggerRuleFloatModeFieldsTests
{
    [Fact]
    public void FloatActionMode_DefaultsToSet()
    {
        var rule = new TriggerRule();
        Assert.Equal(FloatActionMode.Set, rule.FloatActionMode);
    }

    [Fact]
    public void FloatRange_DefaultsToZeroOne()
    {
        var rule = new TriggerRule();
        Assert.Equal(0.0, rule.FloatRangeMin);
        Assert.Equal(1.0, rule.FloatRangeMax);
    }

    [Fact]
    public void FloatCycleStep_DefaultsToPointOne()
    {
        var rule = new TriggerRule();
        Assert.Equal(0.1, rule.FloatCycleStep);
    }

    [Fact]
    public void FloatAmounts_DefaultToExpectedValues()
    {
        var rule = new TriggerRule();
        Assert.Equal(0.1, rule.FloatAddAmount);
        Assert.Equal(0.1, rule.FloatSubtractAmount);
        Assert.Equal(0.1, rule.FloatAddSubtractAmount);
        Assert.Equal(1.5, rule.FloatMultiplyFactor);
    }

    [Fact]
    public void FloatToggleValues_DefaultToOneAndZero()
    {
        var rule = new TriggerRule();
        Assert.Equal(1.0, rule.FloatToggleOnValue);
        Assert.Equal(0.0, rule.FloatToggleOffValue);
    }

    [Fact]
    public void FloatGlitchyInterval_DefaultsTo200()
    {
        var rule = new TriggerRule();
        Assert.Equal(200, rule.FloatGlitchyIntervalMs);
    }

    [Fact]
    public void FloatPulseSeconds_DefaultsToPointFive()
    {
        var rule = new TriggerRule();
        Assert.Equal(0.5, rule.FloatPulseSeconds);
    }

    [Fact]
    public void FloatClampMode_DefaultsToZeroToOne()
    {
        var rule = new TriggerRule();
        Assert.Equal(FloatClampMode.ZeroToOne, rule.FloatClampMode);
    }

    [Fact]
    public void FloatRangeMax_SetBelowMin_ClampsToMinPlusEpsilon()
    {
        var rule = new TriggerRule { FloatRangeMin = 0.4 };
        rule.FloatRangeMax = 0.1;
        Assert.True(rule.FloatRangeMax >= rule.FloatRangeMin + 0.0001);
    }

    [Fact]
    public void FloatPulseSeconds_SetToNegative_ClampsToZero()
    {
        var rule = new TriggerRule { FloatPulseSeconds = -1.0 };
        Assert.Equal(0.0, rule.FloatPulseSeconds);
    }

    [Fact]
    public void FloatGlitchyInterval_SetToZero_ClampsToOne()
    {
        var rule = new TriggerRule { FloatGlitchyIntervalMs = 0 };
        Assert.Equal(1, rule.FloatGlitchyIntervalMs);
    }

    [Fact]
    public void RoundTripsAllFieldsThroughPublicProperties()
    {
        var rule = new TriggerRule
        {
            FloatActionMode = FloatActionMode.Glitchy,
            FloatRangeMin = 0.2,
            FloatRangeMax = 0.8,
            FloatCycleStep = 0.05,
            FloatAddAmount = 0.2,
            FloatSubtractAmount = 0.3,
            FloatAddSubtractAmount = -0.4,
            FloatMultiplyFactor = 2.0,
            FloatToggleOnValue = 1.0,
            FloatToggleOffValue = 0.0,
            FloatGlitchyIntervalMs = 150,
            FloatPulseSeconds = 0.75,
            FloatClampMode = FloatClampMode.MinToMax,
        };
        Assert.Equal(FloatActionMode.Glitchy, rule.FloatActionMode);
        Assert.Equal(0.2, rule.FloatRangeMin);
        Assert.Equal(0.8, rule.FloatRangeMax);
        Assert.Equal(0.05, rule.FloatCycleStep);
        Assert.Equal(0.2, rule.FloatAddAmount);
        Assert.Equal(0.3, rule.FloatSubtractAmount);
        Assert.Equal(-0.4, rule.FloatAddSubtractAmount);
        Assert.Equal(2.0, rule.FloatMultiplyFactor);
        Assert.Equal(1.0, rule.FloatToggleOnValue);
        Assert.Equal(0.0, rule.FloatToggleOffValue);
        Assert.Equal(150, rule.FloatGlitchyIntervalMs);
        Assert.Equal(0.75, rule.FloatPulseSeconds);
        Assert.Equal(FloatClampMode.MinToMax, rule.FloatClampMode);
    }
}

public sealed class TriggerRuleFloatModeUsesTests
{
    [Fact]
    public void UsesFloatActionMode_TrueOnlyWhenParameterTypeIsFloat()
    {
        var rule = new TriggerRule { ParameterType = OscParameterType.Float };
        Assert.True(rule.UsesFloatActionMode);
        rule.ParameterType = OscParameterType.Bool;
        Assert.False(rule.UsesFloatActionMode);
        rule.ParameterType = OscParameterType.Int;
        Assert.False(rule.UsesFloatActionMode);
        rule.ParameterType = OscParameterType.String;
        Assert.False(rule.UsesFloatActionMode);
    }

    [Fact]
    public void UsesFloatSetMode_TrueWhenActionModeIsSet()
    {
        var rule = new TriggerRule
        {
            ParameterType = OscParameterType.Float,
            FloatActionMode = FloatActionMode.Set
        };
        Assert.True(rule.UsesFloatSetMode);
        rule.FloatActionMode = FloatActionMode.Random;
        Assert.False(rule.UsesFloatSetMode);
    }

    [Fact]
    public void UsesFloatRangeInputs_TrueForRandomCycleGlitchy()
    {
        var rule = new TriggerRule { ParameterType = OscParameterType.Float };
        foreach (var mode in new[] { FloatActionMode.Random, FloatActionMode.Cycle, FloatActionMode.Glitchy })
        {
            rule.FloatActionMode = mode;
            Assert.True(rule.UsesFloatRangeInputs, $"expected true for {mode}");
        }
        rule.FloatActionMode = FloatActionMode.Add;
        Assert.False(rule.UsesFloatRangeInputs);
    }

    [Fact]
    public void UsesFloatCycleStep_TrueOnlyForCycle()
    {
        var rule = new TriggerRule
        {
            ParameterType = OscParameterType.Float,
            FloatActionMode = FloatActionMode.Cycle
        };
        Assert.True(rule.UsesFloatCycleStep);
        rule.FloatActionMode = FloatActionMode.Random;
        Assert.False(rule.UsesFloatCycleStep);
    }

    [Fact]
    public void UsesFloatToggleValues_TrueOnlyForToggle()
    {
        var rule = new TriggerRule
        {
            ParameterType = OscParameterType.Float,
            FloatActionMode = FloatActionMode.Toggle
        };
        Assert.True(rule.UsesFloatToggleValues);
        rule.FloatActionMode = FloatActionMode.Add;
        Assert.False(rule.UsesFloatToggleValues);
    }

    [Fact]
    public void UsesFloatGlitchyInterval_TrueOnlyForGlitchy()
    {
        var rule = new TriggerRule
        {
            ParameterType = OscParameterType.Float,
            FloatActionMode = FloatActionMode.Glitchy
        };
        Assert.True(rule.UsesFloatGlitchyInterval);
        rule.FloatActionMode = FloatActionMode.Random;
        Assert.False(rule.UsesFloatGlitchyInterval);
    }

    [Fact]
    public void UsesFloatPulseSeconds_TrueOnlyForPulse()
    {
        var rule = new TriggerRule
        {
            ParameterType = OscParameterType.Float,
            FloatActionMode = FloatActionMode.Pulse
        };
        Assert.True(rule.UsesFloatPulseSeconds);
        rule.FloatActionMode = FloatActionMode.Add;
        Assert.False(rule.UsesFloatPulseSeconds);
    }

    [Fact]
    public void UsesFloatClampMode_TrueForRelativeModes()
    {
        var rule = new TriggerRule { ParameterType = OscParameterType.Float };
        foreach (var mode in new[]
                 {
                     FloatActionMode.Add, FloatActionMode.Subtract,
                     FloatActionMode.AddSubtract, FloatActionMode.Multiply
                 })
        {
            rule.FloatActionMode = mode;
            Assert.True(rule.UsesFloatClampMode, $"expected true for {mode}");
        }
        rule.FloatActionMode = FloatActionMode.Random;
        Assert.False(rule.UsesFloatClampMode);
    }

    [Fact]
    public void UsesFloatActionMode_FalseWhenActionTypeIsNotAvatarParameter()
    {
        var rule = new TriggerRule
        {
            ParameterType = OscParameterType.Float,
            ActionType = OscActionType.SetTrigger
        };
        Assert.False(rule.UsesFloatActionMode);

        rule.ActionType = OscActionType.PlayerMovement;
        Assert.False(rule.UsesFloatActionMode);

        rule.ActionType = OscActionType.AvatarChange;
        Assert.False(rule.UsesFloatActionMode);

        rule.ActionType = OscActionType.AvatarRoulet;
        Assert.False(rule.UsesFloatActionMode);

        rule.ActionType = OscActionType.AvatarParameter;
        Assert.True(rule.UsesFloatActionMode);
    }

    [Fact]
    public void FloatActionMode_RaisesVisibilityPropertyChanges()
    {
        var rule = new TriggerRule
        {
            ActionType = OscActionType.AvatarParameter,
            ParameterType = OscParameterType.Float,
            FloatActionMode = FloatActionMode.Set
        };
        var changed = new List<string>();
        rule.PropertyChanged += (_, e) => changed.Add(e.PropertyName ?? string.Empty);

        rule.FloatActionMode = FloatActionMode.Add;

        Assert.Contains(nameof(TriggerRule.UsesFloatSetMode), changed);
        Assert.Contains(nameof(TriggerRule.UsesFloatAddMode), changed);
        Assert.Contains(nameof(TriggerRule.UsesFloatClampMode), changed);
        Assert.Contains(nameof(TriggerRule.UsesFloatRangeInputs), changed);
        Assert.Contains(nameof(TriggerRule.UsesFloatToggleValues), changed);
        Assert.Contains(nameof(TriggerRule.UsesFloatPulseSeconds), changed);
    }
}
