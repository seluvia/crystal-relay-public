using VrcTwitchOscBridge.Models;

namespace VrcTwitchOscBridge.Services;

public static class FloatLimitDetection
{
    private const double Tolerance = 0.000001d;
    private const double ReleaseTolerance = 0.0001d;

    public static (bool MaxReached, bool MinReached) ComputeLimitState(
        TriggerRule rule,
        double currentValue,
        bool previousMaxReached = false,
        bool previousMinReached = false,
        bool featureEnabled = true)
    {
        if (!featureEnabled)
        {
            return (false, false);
        }

        if (!IsCumulativeMode(rule.FloatActionMode))
        {
            return (false, false);
        }

        var (lower, upper) = ResolveLimits(rule);

        bool maxReached;
        if (previousMaxReached)
        {
            maxReached = currentValue >= upper - ReleaseTolerance;
        }
        else
        {
            maxReached = currentValue >= upper - Tolerance;
        }

        bool minReached;
        if (previousMinReached)
        {
            minReached = currentValue <= lower + ReleaseTolerance;
        }
        else
        {
            minReached = currentValue <= lower + Tolerance;
        }

        if (!rule.HideRewardWhenFloatMaxReached)
        {
            maxReached = false;
        }

        if (!rule.HideRewardWhenFloatMinReached)
        {
            minReached = false;
        }

        return (maxReached, minReached);
    }

    private static bool IsCumulativeMode(FloatActionMode mode) =>
        mode == FloatActionMode.Add
        || mode == FloatActionMode.Subtract
        || mode == FloatActionMode.AddSubtract
        || mode == FloatActionMode.Multiply;

    private static (double Lower, double Upper) ResolveLimits(TriggerRule rule)
    {
        return rule.FloatClampMode switch
        {
            FloatClampMode.None => (double.MinValue, double.MaxValue),
            FloatClampMode.ZeroToOne => (0.0, 1.0),
            FloatClampMode.MinToMax => (rule.FloatRangeMin, rule.FloatRangeMax),
            _ => (0.0, 1.0),
        };
    }
}
