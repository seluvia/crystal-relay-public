using VrcTwitchOscBridge.Models;
using VrcTwitchOscBridge.Services;
using Xunit;

namespace VrcTwitchOscBridge.Tests;

public sealed class FloatLimitDetectionTests
{
    private static TriggerRule Rule(FloatActionMode mode, FloatClampMode clamp,
        double min = 0.0, double max = 1.0) => new()
    {
        ParameterType = OscParameterType.Float,
        FloatActionMode = mode,
        FloatClampMode = clamp,
        FloatRangeMin = min,
        FloatRangeMax = max,
        HideRewardWhenFloatMaxReached = true,
        HideRewardWhenFloatMinReached = true,
    };

    [Fact]
    public void ZeroToOne_AtMax_ReportsMaxReached()
    {
        var rule = Rule(FloatActionMode.Add, FloatClampMode.ZeroToOne);
        var (max, min) = FloatLimitDetection.ComputeLimitState(rule, 1.0, previousMaxReached: false);
        Assert.True(max);
        Assert.False(min);
    }

    [Fact]
    public void ZeroToOne_AtMin_ReportsMinReached()
    {
        var rule = Rule(FloatActionMode.Add, FloatClampMode.ZeroToOne);
        var (max, min) = FloatLimitDetection.ComputeLimitState(rule, 0.0, previousMaxReached: false);
        Assert.False(max);
        Assert.True(min);
    }

    [Fact]
    public void ZeroToOne_AtMidpoint_ReportsNeither()
    {
        var rule = Rule(FloatActionMode.Add, FloatClampMode.ZeroToOne);
        var (max, min) = FloatLimitDetection.ComputeLimitState(rule, 0.5, previousMaxReached: false);
        Assert.False(max);
        Assert.False(min);
    }

    [Fact]
    public void MinToMax_AtMax_ReportsMaxReached()
    {
        var rule = Rule(FloatActionMode.Add, FloatClampMode.MinToMax, min: 0.2, max: 0.8);
        var (max, min) = FloatLimitDetection.ComputeLimitState(rule, 0.8, previousMaxReached: false);
        Assert.True(max);
        Assert.False(min);
    }

    [Fact]
    public void MinToMax_AtMin_ReportsMinReached()
    {
        var rule = Rule(FloatActionMode.Add, FloatClampMode.MinToMax, min: 0.2, max: 0.8);
        var (max, min) = FloatLimitDetection.ComputeLimitState(rule, 0.2, previousMaxReached: false);
        Assert.False(max);
        Assert.True(min);
    }

    [Fact]
    public void None_NeverReportsLimitReached()
    {
        var rule = Rule(FloatActionMode.Add, FloatClampMode.None);
        var (max, min) = FloatLimitDetection.ComputeLimitState(rule, 1.0, previousMaxReached: false);
        Assert.False(max);
        Assert.False(min);
    }

    [Fact]
    public void Hysteresis_StaysAtMaxUntilBelowReleaseTolerance()
    {
        var rule = Rule(FloatActionMode.Add, FloatClampMode.ZeroToOne);
        // At max
        var (max1, _) = FloatLimitDetection.ComputeLimitState(rule, 1.0, previousMaxReached: false);
        Assert.True(max1);
        // Slightly below max — hysteresis keeps it true
        var (max2, _) = FloatLimitDetection.ComputeLimitState(rule, 0.99999, previousMaxReached: true);
        Assert.True(max2);
        // Well below release tolerance — clears
        var (max3, _) = FloatLimitDetection.ComputeLimitState(rule, 0.5, previousMaxReached: true);
        Assert.False(max3);
    }

    [Fact]
    public void Hysteresis_StaysAtMinUntilAboveReleaseTolerance()
    {
        var rule = Rule(FloatActionMode.Subtract, FloatClampMode.ZeroToOne);
        // At min
        var (_, min1) = FloatLimitDetection.ComputeLimitState(rule, 0.0, previousMinReached: false);
        Assert.True(min1);
        // Slightly above min — hysteresis keeps it true
        var (_, min2) = FloatLimitDetection.ComputeLimitState(rule, 0.00001, previousMinReached: true);
        Assert.True(min2);
        // Well above release tolerance — clears
        var (_, min3) = FloatLimitDetection.ComputeLimitState(rule, 0.5, previousMinReached: true);
        Assert.False(min3);
    }

    [Fact]
    public void NonCumulativeMode_AlwaysReturnsFalse()
    {
        foreach (var mode in new[] { FloatActionMode.Set, FloatActionMode.Random,
                                     FloatActionMode.Toggle, FloatActionMode.Cycle,
                                     FloatActionMode.Glitchy, FloatActionMode.Pulse })
        {
            var rule = Rule(mode, FloatClampMode.ZeroToOne);
            var (max, min) = FloatLimitDetection.ComputeLimitState(rule, 1.0, previousMaxReached: false);
            Assert.False(max, $"{mode} should not report max reached.");
            Assert.False(min, $"{mode} should not report min reached.");
        }
    }

    [Fact]
    public void FeatureDisabled_WhenBothFlagsOff_ReturnsFalse()
    {
        var rule = Rule(FloatActionMode.Add, FloatClampMode.ZeroToOne);
        rule.HideRewardWhenFloatMaxReached = false;
        rule.HideRewardWhenFloatMinReached = false;
        var (max, min) = FloatLimitDetection.ComputeLimitState(
            rule, 1.0, previousMaxReached: false, previousMinReached: false,
            featureEnabled: false);
        Assert.False(max);
        Assert.False(min);
    }

    [Fact]
    public void OnlyMaxEnabled_ReportsMaxOnly()
    {
        var rule = Rule(FloatActionMode.Add, FloatClampMode.ZeroToOne);
        rule.HideRewardWhenFloatMaxReached = true;
        rule.HideRewardWhenFloatMinReached = false;
        var (max, min) = FloatLimitDetection.ComputeLimitState(
            rule, 0.0, previousMaxReached: false, previousMinReached: false,
            featureEnabled: true);
        Assert.False(min);  // min checkbox is off, so min never reports
    }
}
