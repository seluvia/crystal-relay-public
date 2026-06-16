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
        Assert.Equal(2, settings.AvatarChangeToAvatarSwapMigrationVersion);
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
            AvatarChangeToAvatarSwapMigrationVersion = 2
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

    [Fact]
    public void Migrate_FoldsNonMasterProfileAvatarChangeRules()
    {
        var settings = new AppSettings();
        var avatarSet = new AvatarTriggerProfile
        {
            Name = "My Set",
            AvatarId = "avtr_set",
            AvatarName = "My Set Avatar"
        };
        avatarSet.ChannelPointRules.Add(new TriggerRule
        {
            ActionType = OscActionType.AvatarChange,
            AvatarChangeTargetId = "avtr_target",
            AvatarTargetName = "Target Avatar"
        });
        avatarSet.ChannelPointRules.Add(new TriggerRule
        {
            ActionType = OscActionType.AvatarParameter,
            ParameterName = "SomeParam"
        });
        settings.AvatarProfiles.Add(avatarSet);

        AvatarSwapMigrationService.Migrate(settings);

        var profile = Assert.Single(settings.AvatarSwapProfiles);
        Assert.Equal("avtr_target", profile.TargetAvatarId);
        var rule = Assert.Single(profile.ChannelPointRules);
        Assert.Equal(TriggerRuleSource.AvatarSet, rule.Source);
        Assert.Equal(OscActionType.AvatarChange, rule.ActionType);
    }

    [Fact]
    public void Migrate_FoldsPowerUpAvatarChangeActionRules()
    {
        var settings = new AppSettings();
        var powerUp = new PowerUpRule
        {
            Name = "Test Power-up",
            PowerUpId = "pu_123"
        };
        powerUp.ActionRule.ActionType = OscActionType.AvatarChange;
        powerUp.ActionRule.AvatarChangeTargetId = "avtr_target";
        powerUp.ActionRule.AvatarTargetName = "Target Avatar";
        settings.PowerUpRules.Add(powerUp);

        AvatarSwapMigrationService.Migrate(settings);

        var profile = Assert.Single(settings.AvatarSwapProfiles);
        var rule = Assert.Single(profile.ChannelPointRules);
        Assert.Equal(TriggerRuleSource.PowerUp, rule.Source);
        Assert.Same(powerUp.ActionRule, rule);
    }

    [Fact]
    public void Migrate_FoldsCashPaymentAvatarChangeTriggerActions()
    {
        var settings = new AppSettings();
        var cashRule = new CashPaymentRule
        {
            Name = "Test Cash Rule",
            Provider = CashPaymentProvider.StreamElements
        };
        cashRule.TriggerAction.ActionType = OscActionType.AvatarChange;
        cashRule.TriggerAction.AvatarChangeTargetId = "avtr_target";
        cashRule.TriggerAction.AvatarTargetName = "Target Avatar";
        settings.CashPaymentRules.Add(cashRule);

        AvatarSwapMigrationService.Migrate(settings);

        var profile = Assert.Single(settings.AvatarSwapProfiles);
        var rule = Assert.Single(profile.ChannelPointRules);
        Assert.Equal(TriggerRuleSource.CashPayment, rule.Source);
        Assert.Same(cashRule.TriggerAction, rule);
    }

    [Fact]
    public void Migrate_IsIdempotent_DoesNotDuplicateWhenRunTwice()
    {
        var settings = new AppSettings();
        var master = new AvatarTriggerProfile
        {
            IsMasterProfile = true,
            AvatarId = "avtr_return"
        };
        master.ChannelPointRules.Add(new TriggerRule
        {
            ActionType = OscActionType.AvatarChange,
            AvatarChangeTargetId = "avtr_a",
            AvatarTargetName = "Avatar A"
        });
        settings.AvatarProfiles.Add(master);

        AvatarSwapMigrationService.Migrate(settings);
        var firstCount = settings.AvatarSwapProfiles.Single().ChannelPointRules.Count;

        AvatarSwapMigrationService.Migrate(settings);
        var secondCount = settings.AvatarSwapProfiles.Single().ChannelPointRules.Count;

        Assert.Equal(firstCount, secondCount);
    }

    [Fact]
    public void Migrate_BumpsMigrationVersionTo2()
    {
        var settings = new AppSettings();
        AvatarSwapMigrationService.Migrate(settings);
        Assert.Equal(2, settings.AvatarChangeToAvatarSwapMigrationVersion);
    }
}
