using VrcTwitchOscBridge.Models;
using VrcTwitchOscBridge.Services;
using Xunit;

namespace VrcTwitchOscBridge.Tests;

public sealed class AvatarSwapMigrationServiceTests
{
    [Fact]
    public void Migrate_FoldsMasterProfileRulesIntoAvatarSwapProfiles()
    {
        var settings = new AppSettings();
        var master = new AvatarTriggerProfile
        {
            IsMasterProfile = true,
            AvatarId = "avtr_return",
            AvatarName = "Return Avatar"
        };
        master.ChannelPointRules.Add(new TriggerRule
        {
            ActionType = OscActionType.AvatarChange,
            AvatarChangeTargetId = "avtr_a",
            AvatarTargetName = "Avatar A"
        });
        master.ChannelPointRules.Add(new TriggerRule
        {
            ActionType = OscActionType.AvatarChange,
            AvatarChangeTargetId = "avtr_a",
            AvatarTargetName = "Avatar A"
        });
        master.ChannelPointRules.Add(new TriggerRule
        {
            ActionType = OscActionType.AvatarChange,
            AvatarChangeTargetId = "avtr_b",
            AvatarTargetName = "Avatar B"
        });
        settings.AvatarProfiles.Add(master);

        AvatarSwapMigrationService.Migrate(settings);

        Assert.Equal(2, settings.AvatarSwapProfiles.Count);
        var a = settings.AvatarSwapProfiles.Single(p => p.TargetAvatarId == "avtr_a");
        var b = settings.AvatarSwapProfiles.Single(p => p.TargetAvatarId == "avtr_b");
        Assert.Equal(2, a.ChannelPointRules.Count);
        Assert.Single(b.ChannelPointRules);
        Assert.Equal("avtr_return", settings.MasterAvatarSwapReturnId);
        Assert.Equal(1, settings.AvatarChangeToAvatarSwapMigrationVersion);
    }

    [Fact]
    public void Migrate_FoldsGlobalOverrideRulesIntoBitsSubsRules()
    {
        var settings = new AppSettings();
        settings.GlobalOverrideRules.Add(new TriggerRule
        {
            ActionType = OscActionType.AvatarChange,
            AvatarChangeTargetId = "avtr_a",
            AvatarTargetName = "Avatar A",
            MinimumAmount = 100
        });

        AvatarSwapMigrationService.Migrate(settings);

        var a = Assert.Single(settings.AvatarSwapProfiles);
        Assert.Single(a.BitsSubsRules);
        Assert.Equal(100, a.BitsSubsRules[0].MinimumAmount);
    }

    [Fact]
    public void Migrate_SkipsWhenAlreadyMigrated()
    {
        var settings = new AppSettings
        {
            AvatarChangeToAvatarSwapMigrationVersion = 1
        };
        settings.AvatarProfiles.Add(new AvatarTriggerProfile
        {
            IsMasterProfile = true,
            AvatarId = "avtr_return"
        });
        settings.AvatarProfiles.First().ChannelPointRules.Add(new TriggerRule
        {
            ActionType = OscActionType.AvatarChange,
            AvatarChangeTargetId = "avtr_a"
        });

        AvatarSwapMigrationService.Migrate(settings);

        Assert.Empty(settings.AvatarSwapProfiles);
    }
}
