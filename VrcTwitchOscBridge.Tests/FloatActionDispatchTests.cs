using VrcTwitchOscBridge.Models;
using VrcTwitchOscBridge.Services;
using Xunit;

namespace VrcTwitchOscBridge.Tests;

public sealed class FloatActionDispatchTests
{
    private static TriggerRule Rule(FloatActionMode mode, double parameterValue = 0.5, string? resetValue = "0")
    {
        return new TriggerRule
        {
            ParameterType = OscParameterType.Float,
            FloatActionMode = mode,
            ParameterValue = parameterValue.ToString(System.Globalization.CultureInfo.InvariantCulture),
            ResetValue = resetValue ?? string.Empty,
        };
    }

    [Fact]
    public void Set_ReturnsParameterValue()
    {
        var (next, _) = FloatActionDispatch.ComputeNext(
            Rule(FloatActionMode.Set, 0.42), currentValue: 0.0);
        Assert.Equal(0.42, next);
    }

    [Fact]
    public void Random_ReturnsValueInRange()
    {
        var rule = Rule(FloatActionMode.Random);
        rule.FloatRangeMin = 0.2;
        rule.FloatRangeMax = 0.8;
        for (int i = 0; i < 50; i++)
        {
            var (next, _) = FloatActionDispatch.ComputeNext(rule, currentValue: 0.0);
            Assert.InRange(next, 0.2, 0.8);
        }
    }

    [Fact]
    public void Add_AddsToCurrentAndClampsToZeroOne()
    {
        var rule = Rule(FloatActionMode.Add, 0);
        rule.FloatAddAmount = 0.3;
        rule.FloatClampMode = FloatClampMode.ZeroToOne;
        var (next, _) = FloatActionDispatch.ComputeNext(rule, currentValue: 0.6);
        Assert.Equal(0.9, next);
    }

    [Fact]
    public void Add_ClampsToOneWhenOverflowing()
    {
        var rule = Rule(FloatActionMode.Add);
        rule.FloatAddAmount = 0.5;
        rule.FloatClampMode = FloatClampMode.ZeroToOne;
        var (next, _) = FloatActionDispatch.ComputeNext(rule, currentValue: 0.9);
        Assert.Equal(1.0, next);
    }

    [Fact]
    public void Subtract_SubtractsFromCurrentAndClampsToZero()
    {
        var rule = Rule(FloatActionMode.Subtract);
        rule.FloatSubtractAmount = 0.4;
        rule.FloatClampMode = FloatClampMode.ZeroToOne;
        var (next, _) = FloatActionDispatch.ComputeNext(rule, currentValue: 0.3);
        Assert.Equal(0.0, next);
    }

    [Fact]
    public void AddSubtract_NegativeAmountSubtracts()
    {
        var rule = Rule(FloatActionMode.AddSubtract);
        rule.FloatAddSubtractAmount = -0.2;
        rule.FloatClampMode = FloatClampMode.ZeroToOne;
        var (next, _) = FloatActionDispatch.ComputeNext(rule, currentValue: 0.5);
        Assert.Equal(0.3, next);
    }

    [Fact]
    public void Multiply_MultipliesAndClampsToZeroOne()
    {
        var rule = Rule(FloatActionMode.Multiply);
        rule.FloatMultiplyFactor = 1.5;
        rule.FloatClampMode = FloatClampMode.ZeroToOne;
        var (next, _) = FloatActionDispatch.ComputeNext(rule, currentValue: 0.8);
        Assert.Equal(1.0, next);
    }

    [Fact]
    public void Multiply_NoClamp_AllowsOverOne()
    {
        var rule = Rule(FloatActionMode.Multiply);
        rule.FloatMultiplyFactor = 1.5;
        rule.FloatClampMode = FloatClampMode.None;
        var (next, _) = FloatActionDispatch.ComputeNext(rule, currentValue: 0.8);
        Assert.Equal(1.2, next);
    }

    [Fact]
    public void Toggle_CurrentNearOn_SendsOff()
    {
        var rule = Rule(FloatActionMode.Toggle);
        rule.FloatToggleOnValue = 1.0;
        rule.FloatToggleOffValue = 0.0;
        var (next, _) = FloatActionDispatch.ComputeNext(rule, currentValue: 1.0);
        Assert.Equal(0.0, next);
    }

    [Fact]
    public void Toggle_CurrentNearOff_SendsOn()
    {
        var rule = Rule(FloatActionMode.Toggle);
        rule.FloatToggleOnValue = 1.0;
        rule.FloatToggleOffValue = 0.0;
        var (next, _) = FloatActionDispatch.ComputeNext(rule, currentValue: 0.0);
        Assert.Equal(1.0, next);
    }

    [Fact]
    public void Cycle_IncrementsAndWrapsAtMax()
    {
        var rule = Rule(FloatActionMode.Cycle);
        rule.FloatRangeMin = 0.0;
        rule.FloatRangeMax = 1.0;
        rule.FloatCycleStep = 0.4;
        var (n1, _) = FloatActionDispatch.ComputeNext(rule, currentValue: 0.0);
        var (n2, _) = FloatActionDispatch.ComputeNext(rule, currentValue: 0.4);
        var (n3, _) = FloatActionDispatch.ComputeNext(rule, currentValue: 0.8);
        Assert.Equal(0.4, n1);
        Assert.Equal(0.8, n2);
        Assert.True(n3 < 0.4, $"expected wrap to land below 0.4, got {n3}");
    }

    [Fact]
    public void Pulse_ReturnsParameterValue()
    {
        var rule = Rule(FloatActionMode.Pulse, 0.7);
        var (next, _) = FloatActionDispatch.ComputeNext(rule, currentValue: 0.0);
        Assert.Equal(0.7, next);
    }

    [Fact]
    public void ComputeNext_PassThroughResetValue()
    {
        var rule = Rule(FloatActionMode.Random, resetValue: "0.25");
        var (_, reset) = FloatActionDispatch.ComputeNext(rule, currentValue: 0.0);
        Assert.Equal(0.25, reset);
    }

    [Fact]
    public void ComputeNext_EmptyResetValue_ReturnsNullReset()
    {
        var rule = Rule(FloatActionMode.Set, resetValue: "");
        var (_, reset) = FloatActionDispatch.ComputeNext(rule, currentValue: 0.0);
        Assert.Null(reset);
    }

    [Fact]
    public void ComputeNext_UnknownMode_FallsBackToParameterValue()
    {
        var rule = new TriggerRule
        {
            ParameterType = OscParameterType.Float,
            FloatActionMode = (FloatActionMode)999,
            ParameterValue = "0.33",
        };
        var (next, _) = FloatActionDispatch.ComputeNext(rule, currentValue: 0.0);
        Assert.Equal(0.33, next);
    }
}
