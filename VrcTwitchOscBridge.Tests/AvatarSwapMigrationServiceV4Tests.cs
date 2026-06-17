using VrcTwitchOscBridge.Models;
using VrcTwitchOscBridge.Services;
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

    [Fact]
    public async Task SettingsStore_RoundTripsAvatarRouletteProfiles()
    {
        var temp = Path.Combine(Path.GetTempPath(), $"cr-settings-{Guid.NewGuid():N}.json");
        try
        {
            var store = new SettingsStore(temp);
            var settings = new AppSettings();
            settings.AvatarRouletteProfiles.Add(new AvatarRouletteProfile { Name = "Test Roulette" });

            await store.SaveAsync(settings);
            var loaded = await store.LoadAsync();

            Assert.Single(loaded.AvatarRouletteProfiles);
            Assert.Equal("Test Roulette", loaded.AvatarRouletteProfiles[0].Name);
        }
        finally
        {
            if (File.Exists(temp)) File.Delete(temp);
        }
    }

    [Fact]
    public void MigrateV4_SplitsBitsSubsRulesIntoBitsAndSubs()
    {
        var temp = Path.Combine(Path.GetTempPath(), $"cr-v3-{Guid.NewGuid():N}.json");
        try
        {
            var json = "{\n" +
                       "  \"avatarSwapProfiles\": [\n" +
                       "    {\n" +
                       "      \"id\": \"00000000-0000-0000-0000-000000000001\",\n" +
                       "      \"targetAvatarId\": \"avtr_a\",\n" +
                       "      \"channelPointRules\": [],\n" +
                       "      \"bitsSubsRules\": [\n" +
                       "        { \"name\": \"Bits 500\", \"triggerType\": 1, \"minimumAmount\": 500 },\n" +
                       "        { \"name\": \"T1 Sub\", \"triggerType\": 2 },\n" +
                       "        { \"name\": \"Gift 5\", \"triggerType\": 2, \"isGiftSubscription\": true }\n" +
                       "      ]\n" +
                       "    }\n" +
                       "  ]\n" +
                       "}";
            File.WriteAllText(temp, json);
            var store = new SettingsStore(temp);
            var loaded = store.LoadAsync().GetAwaiter().GetResult();

            AvatarSwapMigrationService.Migrate(loaded);

            var p = loaded.AvatarSwapProfiles.Single();
            Assert.Single(p.BitsRules);
            Assert.Equal("Bits 500", p.BitsRules[0].Name);
            Assert.Equal(2, p.SubsRules.Count);
            Assert.Contains(p.SubsRules, r => r.Name == "T1 Sub" && r.TriggerType == TwitchTriggerType.Subscriptions);
            Assert.Contains(p.SubsRules, r => r.Name == "Gift 5" && r.TriggerType == TwitchTriggerType.GiftSubscription);
        }
        finally
        {
            if (File.Exists(temp)) File.Delete(temp);
        }
    }
}
