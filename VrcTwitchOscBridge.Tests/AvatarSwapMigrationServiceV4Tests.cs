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

    [Fact]
    public void AvatarRouletteProfile_DefaultsAreEmpty()
    {
        var p = new AvatarRouletteProfile();
        Assert.NotNull(p.Pool);
        Assert.Empty(p.Pool);
        Assert.NotNull(p.Triggers);
        Assert.Empty(p.Triggers);
        Assert.True(p.IsEnabled);
        Assert.Null(p.ReturnAvatarId);
        Assert.Null(p.ReturnAvatarName);
        Assert.Equal(0, p.PoolCount);
        Assert.Equal(0, p.TriggerCount);
    }

    [Fact]
    public void AvatarRouletteProfile_Subtitle_FormatsPoolAndTriggerCount()
    {
        var p = new AvatarRouletteProfile { Name = "Demo" };
        p.Pool.Add(new RouletteAvatarEntry { AvatarId = "a1", AvatarName = "One" });
        p.Pool.Add(new RouletteAvatarEntry { AvatarId = "a2", AvatarName = "Two" });
        p.Triggers.Add(new TriggerRule());
        Assert.Contains("2", p.Subtitle);
        Assert.Contains("1", p.Subtitle);
        Assert.Contains("🎲", p.Subtitle);
        Assert.Contains("pool", p.Subtitle);
    }

    [Fact]
    public void AppSettings_AvatarRouletteProfiles_DefaultsToEmpty()
    {
        var s = new AppSettings();
        Assert.NotNull(s.AvatarRouletteProfiles);
        Assert.Empty(s.AvatarRouletteProfiles);
    }
}
