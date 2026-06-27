using VrcTwitchOscBridge.Models;
using VrcTwitchOscBridge.Services;
using Xunit;

namespace VrcTwitchOscBridge.Tests;

public sealed class AvatarSwapRuntimeDispatchTests
{
    [Fact]
    public void FindAvatarSwapProfileForRule_ReturnsProfileForMigratedRule()
    {
        var profile = new AvatarSwapProfile { TargetAvatarId = "avtr_a" };
        var rule = new TriggerRule { ActionType = OscActionType.AvatarChange, AvatarChangeTargetId = "avtr_a" };
        profile.ChannelPointRules.Add(rule);
        var settings = new AppSettings();
        settings.AvatarSwapProfiles.Add(profile);

        var config = BridgeRuntimeConfiguration.FromSettings(settings, RuntimeConfig.CreateDefault(), null);
        var found = config.FindAvatarSwapProfileForRule(rule);

        Assert.NotNull(found);
        Assert.Equal("avtr_a", found.TargetAvatarId);
    }

    [Fact]
    public void FindAvatarSwapProfileForRule_ReturnsNullForUnmigratedRule()
    {
        var settings = new AppSettings();
        var config = BridgeRuntimeConfiguration.FromSettings(settings, RuntimeConfig.CreateDefault(), null);
        var rule = new TriggerRule { ActionType = OscActionType.AvatarChange };

        Assert.Null(config.FindAvatarSwapProfileForRule(rule));
    }

    [Fact]
    public void FromSettings_FlattensAvatarSwapChannelPointRuleIntoMatchableRules()
    {
        var settings = new AppSettings();
        var profile = new AvatarSwapProfile { TargetAvatarId = "avtr_target", TargetAvatarName = "Target" };
        var cpRule = new TriggerRule
        {
            TriggerType = TwitchTriggerType.ChannelPoints,
            ActionType = OscActionType.AvatarChange,
            AvatarChangeTargetId = "avtr_target",
            AvatarTargetName = "Target",
            ChannelPointRewardTitle = "Swap To Target",
            ChannelPointRewardCost = 100
        };
        profile.ChannelPointRules.Add(cpRule);
        settings.AvatarSwapProfiles.Add(profile);

        var config = BridgeRuntimeConfiguration.FromSettings(settings, RuntimeConfig.CreateDefault(), null);

        // The rule must be in the matchable Rules list so the runtime index can match it.
        Assert.Contains(config.Rules, r => ReferenceEquals(r.Rule, cpRule));
        // And it must still route back to the avatar swap profile.
        Assert.NotNull(config.FindAvatarSwapProfileForRule(cpRule));
    }

    [Fact]
    public void FromSettings_FlattensAvatarSwapBitsRuleIntoMatchableRules()
    {
        var settings = new AppSettings();
        var profile = new AvatarSwapProfile { TargetAvatarId = "avtr_target", TargetAvatarName = "Target" };
        var bitsRule = new TriggerRule
        {
            TriggerType = TwitchTriggerType.Bits,
            ActionType = OscActionType.AvatarChange,
            AvatarChangeTargetId = "avtr_target",
            AvatarTargetName = "Target",
            MinimumAmount = 100
        };
        profile.BitsRules.Add(bitsRule);
        settings.AvatarSwapProfiles.Add(profile);

        var config = BridgeRuntimeConfiguration.FromSettings(settings, RuntimeConfig.CreateDefault(), null);

        Assert.Contains(config.Rules, r => ReferenceEquals(r.Rule, bitsRule));
    }

    [Fact]
    public void FromSettings_RegistersAvatarSwapPaymentRuleWithCashPaymentMatcher()
    {
        var settings = new AppSettings();
        var profile = new AvatarSwapProfile { TargetAvatarId = "avtr_target", TargetAvatarName = "Target" };
        var payRule = new CashPaymentRule
        {
            Name = "Tip Swap",
            Provider = CashPaymentProvider.StreamElements,
            IsEnabled = true,
            ActionKind = CashPaymentActionKind.TriggerAction
        };
        payRule.TriggerAction.ActionType = OscActionType.AvatarChange;
        payRule.TriggerAction.AvatarChangeTargetId = "avtr_target";
        payRule.TriggerAction.AvatarTargetName = "Target";
        profile.PaymentRules.Add(payRule);
        settings.AvatarSwapProfiles.Add(profile);

        var config = BridgeRuntimeConfiguration.FromSettings(settings, RuntimeConfig.CreateDefault(), null);

        // The payment rule must be in the cash-payment matcher so it fires on cash events.
        Assert.Contains(config.CashPaymentRules, r => r.Id == payRule.Id);
        // And its trigger action routes back to the avatar swap profile.
        Assert.NotNull(config.FindAvatarSwapProfileForRule(payRule.TriggerAction));
    }

    // NOTE: The plan originally listed 6 additional placeholder tests for
    // ResolveAvatarSwapAction return modes and the cash/power-up/roulette
    // dispatch re-routing. Those behaviors live inside BridgeCoordinator as
    // private/internal logic that depends on the live OSC client, the shared
    // return avatar state, and the full coordinator state. They are not
    // practical to unit-test in isolation, so they were intentionally dropped
    // from this file. They will be covered by the manual smoke checks in
    // Task 31 of the Avatar Swap full migration plan.
}
