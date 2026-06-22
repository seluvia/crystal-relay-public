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
