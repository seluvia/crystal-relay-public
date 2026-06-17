using VrcTwitchOscBridge.Models;
using Xunit;

namespace VrcTwitchOscBridge.Tests;

public sealed class AvatarSwapMigrationServiceV4Tests
{
    [Fact]
    public void IsGiftSubscription_DefaultsToFalse()
    {
        var rule = new TriggerRule();
        Assert.False(rule.IsGiftSubscription);
    }

    [Fact]
    public void TwitchTriggerType_HasGiftSubscriptionValue()
    {
        Assert.True(Enum.IsDefined(typeof(TwitchTriggerType), "GiftSubscription"));
    }

    [Fact]
    public void TwitchTriggerType_HasChatCommandValue()
    {
        Assert.True(Enum.IsDefined(typeof(TwitchTriggerType), "ChatCommand"));
    }

    [Fact]
    public void TwitchTriggerType_HasFollowValue()
    {
        Assert.True(Enum.IsDefined(typeof(TwitchTriggerType), "Follow"));
    }

    [Fact]
    public void AvatarSwapProfile_HasFourRuleCollections()
    {
        var profile = new AvatarSwapProfile();
        Assert.NotNull(profile.ChannelPointRules);
        Assert.NotNull(profile.BitsRules);
        Assert.NotNull(profile.SubsRules);
        Assert.NotNull(profile.PaymentRules);
        Assert.Empty(profile.ChannelPointRules);
        Assert.Empty(profile.BitsRules);
        Assert.Empty(profile.SubsRules);
        Assert.Empty(profile.PaymentRules);
    }

    [Fact]
    public void AvatarSwapProfile_DropsBitsSubsAndRouletteCollections()
    {
        var profile = new AvatarSwapProfile();
        var type = typeof(AvatarSwapProfile);
        Assert.Null(type.GetProperty("BitsSubsRules"));
        Assert.Null(type.GetProperty("RouletteRules"));
        Assert.Null(type.GetProperty("ReturnAvatarMode"));
        Assert.Null(type.GetProperty("ReturnAvatarId"));
        Assert.Null(type.GetProperty("ReturnAvatarName"));
    }

    [Fact]
    public void AvatarSwapProfile_AvatarSubtitle_FormatIsFourCounts()
    {
        var profile = new AvatarSwapProfile { TargetAvatarName = "Test" };
        profile.ChannelPointRules.Add(new TriggerRule());
        profile.BitsRules.Add(new TriggerRule());
        profile.SubsRules.Add(new TriggerRule());
        profile.SubsRules.Add(new TriggerRule());
        profile.PaymentRules.Add(new TriggerRule());
        var subtitle = profile.AvatarSubtitle;
        Assert.Contains("1", subtitle);
        Assert.Contains("2", subtitle);
        Assert.Contains("cp", subtitle);
        Assert.Contains("bits", subtitle);
        Assert.Contains("subs", subtitle);
        Assert.Contains("pay", subtitle);
    }
}
