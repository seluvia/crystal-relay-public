using VrcTwitchOscBridge.Models;

namespace VrcTwitchOscBridge.Services.Support;

public static class SupportOverrideDurationMath
{
    public static double ComputePerEventAddSeconds(
        TriggerRuleSnapshot rule,
        int amount,
        string subscriptionTier)
    {
        var baseSeconds = Math.Max(0, rule.DurationSeconds);
        if (!rule.AmountScaledDurationEnabled)
        {
            return Math.Max(1, baseSeconds);
        }

        var scaled = ComputeScaledSeconds(rule, amount, subscriptionTier);
        return baseSeconds + scaled;
    }

    private static double ComputeScaledSeconds(
        TriggerRuleSnapshot rule,
        int amount,
        string subscriptionTier)
    {
        var safeAmount = Math.Max(1, amount);
        if (rule.TriggerType == TwitchTriggerType.Subscriptions)
        {
            var secondsPerSub = ResolveSubscriptionSecondsPerSub(rule, subscriptionTier);
            return safeAmount * secondsPerSub;
        }

        var unitsPerDuration = Math.Max(1, rule.BitsAmountUnitsPerDuration);
        var secondsPerUnit = Math.Max(1, rule.BitsSecondsPerAmountUnit);
        return (double)safeAmount / unitsPerDuration * secondsPerUnit;
    }

    private static int ResolveSubscriptionSecondsPerSub(TriggerRuleSnapshot rule, string subscriptionTier)
    {
        return subscriptionTier?.Trim() switch
        {
            "2000" => Math.Max(1, rule.SubscriptionTier2SecondsPerSub),
            "3000" => Math.Max(1, rule.SubscriptionTier3SecondsPerSub),
            _ => Math.Max(1, rule.SubscriptionTier1SecondsPerSub)
        };
    }
}
