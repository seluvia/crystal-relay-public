using System.Globalization;
using VrcTwitchOscBridge.Models;

namespace VrcTwitchOscBridge.Services;

public static class FloatActionDispatch
{
    public const double ToggleTolerance = 0.0001;

    public static (double nextValue, double? resetValue) ComputeNext(TriggerRule rule, double currentValue)
    {
        var reset = ParseReset(rule.ResetValue);
        var next = rule.FloatActionMode switch
        {
            FloatActionMode.Random       => RollRandom(rule),
            FloatActionMode.Add          => ApplyClamp(rule, currentValue + rule.FloatAddAmount),
            FloatActionMode.Subtract     => ApplyClamp(rule, currentValue - rule.FloatSubtractAmount),
            FloatActionMode.AddSubtract  => ApplyClamp(rule, currentValue + rule.FloatAddSubtractAmount),
            FloatActionMode.Multiply     => ApplyClamp(rule, currentValue * rule.FloatMultiplyFactor),
            FloatActionMode.Toggle       => ComputeToggle(rule, currentValue),
            FloatActionMode.Cycle        => ComputeCycle(rule, currentValue),
            FloatActionMode.Pulse        => ParseParameter(rule),
            _                            => ParseParameter(rule),
        };
        return (NormalizeToOscPrecision(next), reset);
    }

    private static double RollRandom(TriggerRule rule)
    {
        var min = rule.FloatRangeMin;
        var max = rule.FloatRangeMax;
        if (max <= min) return min;
        return Random.Shared.NextDouble() * (max - min) + min;
    }

    private static double ComputeToggle(TriggerRule rule, double currentValue)
    {
        if (Math.Abs(currentValue - rule.FloatToggleOnValue) < ToggleTolerance)
            return rule.FloatToggleOffValue;
        return rule.FloatToggleOnValue;
    }

    private static double ComputeCycle(TriggerRule rule, double currentValue)
    {
        var min = rule.FloatRangeMin;
        var max = rule.FloatRangeMax;
        var range = max - min;
        if (range <= 0) return min;
        var step = rule.FloatCycleStep;
        var next = currentValue + step;
        if (next > max)
        {
            var overflow = next - max;
            next = min + (overflow % range);
        }
        return next;
    }

    private static double ApplyClamp(TriggerRule rule, double value)
    {
        return rule.FloatClampMode switch
        {
            FloatClampMode.None     => value,
            FloatClampMode.ZeroToOne => Math.Clamp(value, 0.0, 1.0),
            FloatClampMode.MinToMax => Math.Clamp(value, rule.FloatRangeMin, rule.FloatRangeMax),
            _                       => Math.Clamp(value, 0.0, 1.0),
        };
    }

    private static double ParseParameter(TriggerRule rule)
    {
        if (double.TryParse(rule.ParameterValue, NumberStyles.Float, CultureInfo.InvariantCulture, out var v))
            return Math.Clamp(v, 0.0, 1.0);
        return 0.0;
    }

    private static double? ParseReset(string raw)
    {
        if (string.IsNullOrWhiteSpace(raw)) return null;
        if (double.TryParse(raw, NumberStyles.Float, CultureInfo.InvariantCulture, out var v))
            return Math.Clamp(v, 0.0, 1.0);
        return null;
    }

    private static double NormalizeToOscPrecision(double value)
    {
        if (double.IsNaN(value) || double.IsInfinity(value)) return 0.0;
        var text = value.ToString("0.###", CultureInfo.InvariantCulture);
        return double.Parse(text, NumberStyles.Float, CultureInfo.InvariantCulture);
    }
}
