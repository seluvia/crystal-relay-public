using VrcTwitchOscBridge.Models;
using VrcTwitchOscBridge.UserControls;
using Xunit;

namespace VrcTwitchOscBridge.Tests;

public sealed class InlineChannelPointRuleRowViewModelTests
{
    [Fact]
    public void Summary_FormatsNameAndCost()
    {
        var rule = new TriggerRule
        {
            Name = "My Reward",
            TriggerType = TwitchTriggerType.ChannelPoints,
            ChannelPointRewardCost = 500
        };
        var vm = new InlineChannelPointRuleRowViewModel(rule);

        Assert.Contains("My Reward", vm.Summary);
        Assert.Contains("500", vm.Summary);
    }

    [Fact]
    public void Summary_OmitsCostWhenZero()
    {
        var rule = new TriggerRule
        {
            Name = "Free",
            TriggerType = TwitchTriggerType.ChannelPoints,
            ChannelPointRewardCost = 0
        };
        var vm = new InlineChannelPointRuleRowViewModel(rule);

        Assert.DoesNotContain("pts", vm.Summary);
    }

    [Fact]
    public void IsEnabled_ReflectsRuleProperty()
    {
        var rule = new TriggerRule { IsEnabled = false };
        var vm = new InlineChannelPointRuleRowViewModel(rule);

        Assert.False(vm.IsEnabled);

        rule.IsEnabled = true;
        Assert.True(vm.IsEnabled);
    }
}

public sealed class InlineBitsRuleRowViewModelTests
{
    [Fact]
    public void Summary_IncludesMinAmount()
    {
        var rule = new TriggerRule
        {
            Name = "Cheer",
            TriggerType = TwitchTriggerType.Bits,
            MinimumAmount = 100
        };
        var vm = new InlineBitsRuleRowViewModel(rule);

        Assert.Contains("Cheer", vm.Summary);
        Assert.Contains("100", vm.Summary);
        Assert.Contains("bits", vm.Summary);
    }

    [Fact]
    public void Summary_IncludesScaledDuration()
    {
        var rule = new TriggerRule
        {
            Name = "Cheer",
            TriggerType = TwitchTriggerType.Bits,
            BitsAmountUnitsPerDuration = 50,
            BitsSecondsPerAmountUnit = 1
        };
        var vm = new InlineBitsRuleRowViewModel(rule);

        Assert.Contains("1s per 50 bits", vm.Summary);
    }

    [Fact]
    public void Summary_IncludesMaxAccumulated()
    {
        var rule = new TriggerRule
        {
            Name = "Cheer",
            TriggerType = TwitchTriggerType.Bits,
            MaxAccumulatedDurationEnabled = true,
            MaxAccumulatedDurationSeconds = 600
        };
        var vm = new InlineBitsRuleRowViewModel(rule);

        Assert.Contains("cap 600s", vm.Summary);
    }

    [Fact]
    public void Summary_IncludesKeyword()
    {
        var rule = new TriggerRule
        {
            Name = "Cheer",
            TriggerType = TwitchTriggerType.Bits,
            SupporterKeywordText = "!boost"
        };
        var vm = new InlineBitsRuleRowViewModel(rule);

        Assert.Contains("keyword: !boost", vm.Summary);
    }
}

public sealed class InlineSubsRuleRowViewModelTests
{
    [Fact]
    public void Summary_IncludesTierMultipliers()
    {
        var rule = new TriggerRule
        {
            Name = "Sub Boost",
            TriggerType = TwitchTriggerType.Subscriptions,
            SubscriptionTier1SecondsPerSub = 10,
            SubscriptionTier2SecondsPerSub = 25,
            SubscriptionTier3SecondsPerSub = 60
        };
        var vm = new InlineSubsRuleRowViewModel(rule);

        Assert.Contains("T1:10s", vm.Summary);
        Assert.Contains("T2:25s", vm.Summary);
        Assert.Contains("T3:60s", vm.Summary);
    }

    [Fact]
    public void Summary_IncludesAllTiersAtMinimumOne()
    {
        // TriggerRule normalizes tier values to a minimum of 1, so the summary always
        // shows all three tiers. This test verifies the values reflect the user setting
        // (T1=10) and the minimum default (T2=1, T3=1).
        var rule = new TriggerRule
        {
            Name = "Sub",
            TriggerType = TwitchTriggerType.Subscriptions,
            SubscriptionTier1SecondsPerSub = 10
        };
        var vm = new InlineSubsRuleRowViewModel(rule);

        Assert.Contains("T1:10s", vm.Summary);
        Assert.Contains("T2:1s", vm.Summary);
        Assert.Contains("T3:1s", vm.Summary);
    }

    [Fact]
    public void Summary_ShowsSubTypeRegularPlusGift_WhenIsGiftSubscription()
    {
        var rule = new TriggerRule
        {
            Name = "Gift",
            TriggerType = TwitchTriggerType.Subscriptions,
            IsGiftSubscription = true
        };
        var vm = new InlineSubsRuleRowViewModel(rule);

        Assert.Contains("sub-type: regular+gift", vm.Summary);
    }

    [Fact]
    public void Summary_ShowsSubTypeRegular_WhenNotGift()
    {
        var rule = new TriggerRule
        {
            Name = "Regular",
            TriggerType = TwitchTriggerType.Subscriptions
        };
        var vm = new InlineSubsRuleRowViewModel(rule);

        Assert.Contains("sub-type: regular", vm.Summary);
    }

    [Fact]
    public void Summary_IncludesKeyword()
    {
        var rule = new TriggerRule
        {
            Name = "Sub",
            TriggerType = TwitchTriggerType.Subscriptions,
            SupporterKeywordText = "!thanks"
        };
        var vm = new InlineSubsRuleRowViewModel(rule);

        Assert.Contains("keyword: !thanks", vm.Summary);
    }
}

public sealed class InlinePaymentRuleRowViewModelTests
{
    [Fact]
    public void Summary_IncludesProvider()
    {
        var rule = new CashPaymentRule
        {
            Name = "Tip Swap",
            Provider = CashPaymentProvider.KoFi
        };
        var vm = new InlinePaymentRuleRowViewModel(rule);

        Assert.Contains("Tip Swap", vm.Summary);
        Assert.Contains("Ko-fi", vm.Summary);
    }

    [Fact]
    public void Summary_IncludesMinMaxAndCurrency()
    {
        var rule = new CashPaymentRule
        {
            Name = "Tip",
            Provider = CashPaymentProvider.StreamElements,
            MinimumAmount = 5m,
            MaximumAmount = 50m,
            CurrencyCode = "USD"
        };
        var vm = new InlinePaymentRuleRowViewModel(rule);

        Assert.Contains("USD", vm.Summary);
        Assert.Contains("5-50", vm.Summary);
    }

    [Fact]
    public void Summary_OmitsRangeWhenBothZero()
    {
        var rule = new CashPaymentRule
        {
            Name = "Tip",
            Provider = CashPaymentProvider.StreamElements,
            MinimumAmount = 0m,
            MaximumAmount = 0m
        };
        var vm = new InlinePaymentRuleRowViewModel(rule);

        Assert.DoesNotContain("-0", vm.Summary);
    }

    [Fact]
    public void Summary_IncludesMessageContains()
    {
        var rule = new CashPaymentRule
        {
            Name = "Cheer Tip",
            Provider = CashPaymentProvider.Streamlabs,
            MessageContains = "cheer"
        };
        var vm = new InlinePaymentRuleRowViewModel(rule);

        Assert.Contains("match: 'cheer'", vm.Summary);
    }

    [Fact]
    public void Summary_ProviderName_UsesDisplayLabels()
    {
        Assert.Contains("StreamElements", new InlinePaymentRuleRowViewModel(new CashPaymentRule { Provider = CashPaymentProvider.StreamElements }).Summary);
        Assert.Contains("Streamlabs", new InlinePaymentRuleRowViewModel(new CashPaymentRule { Provider = CashPaymentProvider.Streamlabs }).Summary);
        Assert.Contains("Ko-fi", new InlinePaymentRuleRowViewModel(new CashPaymentRule { Provider = CashPaymentProvider.KoFi }).Summary);
    }
}

public sealed class InlineRouletteRuleRowViewModelTests
{
    [Fact]
    public void Summary_FormatsNameAndCost()
    {
        var rule = new TriggerRule
        {
            Name = "Roulette Trigger",
            TriggerType = TwitchTriggerType.ChannelPoints,
            ChannelPointRewardCost = 250
        };
        var vm = new InlineRouletteRuleRowViewModel(rule);

        Assert.Contains("Roulette Trigger", vm.Summary);
        Assert.Contains("250", vm.Summary);
    }

    [Fact]
    public void Summary_HandlesFreeReward()
    {
        var rule = new TriggerRule
        {
            Name = "Free Spin",
            TriggerType = TwitchTriggerType.ChannelPoints
        };
        var vm = new InlineRouletteRuleRowViewModel(rule);

        Assert.Contains("Free Spin", vm.Summary);
    }
}
