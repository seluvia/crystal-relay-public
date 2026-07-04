using VrcTwitchOscBridge.Infrastructure;

namespace VrcTwitchOscBridge.Models;

public sealed class AvatarScaleSafetySettings : ObservableObject
{
    private double currentMinimumHeightMeters = AvatarScaleRule.SafeMinimumHeightMeters;
    private double currentMaximumHeightMeters = AvatarScaleRule.SafeMaximumHeightMeters;

    public double CurrentMinimumHeightMeters
    {
        get => currentMinimumHeightMeters;
        set
        {
            var nextValue = NormalizeHeight(value, AvatarScaleRule.SafeMinimumHeightMeters);
            nextValue = Math.Clamp(nextValue, AvatarScaleRule.AdvancedMinimumHeightMeters, AvatarScaleRule.AdvancedMaximumHeightMeters);
            if (SetProperty(ref currentMinimumHeightMeters, nextValue))
            {
                if (currentMaximumHeightMeters < currentMinimumHeightMeters)
                {
                    CurrentMaximumHeightMeters = currentMinimumHeightMeters;
                }

                RaisePropertyChanged(nameof(CurrentMaxHeightAllowedText));
            }
        }
    }

    public double CurrentMaximumHeightMeters
    {
        get => currentMaximumHeightMeters;
        set
        {
            var nextValue = NormalizeHeight(value, AvatarScaleRule.SafeMaximumHeightMeters);
            nextValue = Math.Clamp(nextValue, CurrentMinimumHeightMeters, AvatarScaleRule.AdvancedMaximumHeightMeters);
            if (SetProperty(ref currentMaximumHeightMeters, nextValue))
            {
                RaisePropertyChanged(nameof(CurrentMaxHeightAllowedText));
            }
        }
    }

    public string CurrentMaxHeightAllowedText => $"{CurrentMaximumHeightMeters:0.###}m";

    public double ClampHeight(double value)
    {
        if (double.IsNaN(value) || double.IsInfinity(value))
        {
            return Math.Clamp(1.6, CurrentMinimumHeightMeters, CurrentMaximumHeightMeters);
        }

        return Math.Clamp(value, CurrentMinimumHeightMeters, CurrentMaximumHeightMeters);
    }

    public double ClampRelativeHeight(double value)
    {
        if (double.IsNaN(value) || double.IsInfinity(value))
        {
            return 0;
        }

        var limit = Math.Max(Math.Abs(CurrentMinimumHeightMeters), Math.Abs(CurrentMaximumHeightMeters));
        return Math.Clamp(value, -limit, limit);
    }

    public static AvatarScaleSafetySettings FromExistingRules(IEnumerable<AvatarScaleRule> rules)
    {
        var settings = new AvatarScaleSafetySettings();
        var advancedValues = rules
            .Where(rule => rule.AdvancedRangeEnabled)
            .SelectMany(GetConfiguredHeightValues)
            .Where(value => !double.IsNaN(value) && !double.IsInfinity(value))
            .Where(value => value > 0)
            .ToArray();
        var smallestAdvancedValue = advancedValues
            .Where(value => value < AvatarScaleRule.SafeMinimumHeightMeters)
            .DefaultIfEmpty(AvatarScaleRule.SafeMinimumHeightMeters)
            .Min();
        var largestAdvancedValue = advancedValues
            .Where(value => value > AvatarScaleRule.SafeMaximumHeightMeters)
            .DefaultIfEmpty(AvatarScaleRule.SafeMaximumHeightMeters)
            .Max();

        settings.CurrentMinimumHeightMeters = smallestAdvancedValue;
        settings.CurrentMaximumHeightMeters = largestAdvancedValue;
        return settings;
    }

    private static IEnumerable<double> GetConfiguredHeightValues(AvatarScaleRule rule)
    {
        yield return rule.TargetHeightMeters;
        yield return rule.MinimumHeightMeters;
        yield return rule.MaximumHeightMeters;
        yield return rule.RelativeHeightMeters;
        yield return rule.RelativeMinimumHeightMeters;
        yield return rule.RelativeMaximumHeightMeters;
        yield return rule.RestoreHeightMeters;
    }

    private static double NormalizeHeight(double value, double fallback)
    {
        return double.IsNaN(value) || double.IsInfinity(value) || value <= 0
            ? fallback
            : value;
    }
}
