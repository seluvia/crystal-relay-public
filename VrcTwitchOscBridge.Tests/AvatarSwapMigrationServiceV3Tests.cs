using System;
using System.Collections.ObjectModel;
using VrcTwitchOscBridge.Models;
using VrcTwitchOscBridge.Services;
using Xunit;

namespace VrcTwitchOscBridge.Tests;

public sealed class AvatarSwapMigrationServiceV3Tests
{
    [Fact]
    public void MigrateV3_FoldsAndRemovesChannelPointRulesFromAvatarProfiles()
    {
        var settings = new AppSettings();
        var profile = new AvatarTriggerProfile { AvatarId = "avtr_a", AvatarName = "A" };
        var rule = new TriggerRule
        {
            ActionType = OscActionType.AvatarChange,
            AvatarChangeTargetId = "avtr_b",
            AvatarTargetName = "B"
        };
        profile.ChannelPointRules.Add(rule);
        settings.AvatarProfiles.Add(profile);

        AvatarSwapMigrationService.Migrate(settings);

        Assert.Single(settings.AvatarSwapProfiles);
        Assert.Single(settings.AvatarSwapProfiles[0].ChannelPointRules);
        Assert.Empty(profile.ChannelPointRules);
        Assert.Equal(TriggerRuleSource.AvatarSet, rule.Source);
        Assert.Equal(3, settings.AvatarChangeToAvatarSwapMigrationVersion);
    }

    [Fact]
    public void MigrateV3_FoldsAndRemovesFromGlobalOverrideRules()
    {
        var settings = new AppSettings();
        var rule = new TriggerRule
        {
            ActionType = OscActionType.AvatarChange,
            AvatarChangeTargetId = "avtr_a",
            AvatarTargetName = "A"
        };
        settings.GlobalOverrideRules.Add(rule);

        AvatarSwapMigrationService.Migrate(settings);

        Assert.Empty(settings.GlobalOverrideRules);
        Assert.Single(settings.AvatarSwapProfiles[0].BitsSubsRules);
        Assert.Equal(TriggerRuleSource.GlobalOverride, rule.Source);
    }

    [Fact]
    public void MigrateV3_FoldsAndStubsPowerUpActionRule()
    {
        var settings = new AppSettings();
        var powerUpId = Guid.NewGuid();
        var powerUp = new PowerUpRule
        {
            Id = powerUpId,
            ActionRule = new TriggerRule
            {
                ActionType = OscActionType.AvatarChange,
                AvatarChangeTargetId = "avtr_a"
            }
        };
        settings.PowerUpRules.Add(powerUp);

        AvatarSwapMigrationService.Migrate(settings);

        Assert.Equal(OscActionType.AvatarParameter, powerUp.ActionRule.ActionType);
        Assert.Single(settings.AvatarSwapProfiles[0].ChannelPointRules);
        Assert.Equal(powerUpId.ToString(), powerUp.ActionRule.PowerUpId);
    }

    [Fact]
    public void MigrateV3_FoldsAndStubsCashPaymentTriggerAction()
    {
        var settings = new AppSettings();
        var cashPaymentId = Guid.NewGuid();
        var rule = new CashPaymentRule { Id = cashPaymentId };
        rule.TriggerAction = new TriggerRule
        {
            ActionType = OscActionType.AvatarChange,
            AvatarChangeTargetId = "avtr_a"
        };
        settings.CashPaymentRules.Add(rule);

        AvatarSwapMigrationService.Migrate(settings);

        Assert.Equal(OscActionType.AvatarParameter, rule.TriggerAction.ActionType);
        Assert.Single(settings.AvatarSwapProfiles[0].ChannelPointRules);
        Assert.Equal(cashPaymentId.ToString(), rule.TriggerAction.CashPaymentRuleId);
    }

    [Fact]
    public void MigrateV3_FoldsAvatarRoulette()
    {
        var settings = new AppSettings();
        var rule = new TriggerRule
        {
            ActionType = OscActionType.AvatarRoulet,
            AvatarRouletAvatarIds = new ObservableCollection<string> { "avtr_a", "avtr_b" }
        };
        settings.GlobalOverrideRules.Add(rule);

        AvatarSwapMigrationService.Migrate(settings);

        Assert.Empty(settings.GlobalOverrideRules);
        Assert.Single(settings.AvatarSwapProfiles);
        Assert.Single(settings.AvatarSwapProfiles[0].RouletteRules);
        Assert.Equal("avtr_a", settings.AvatarSwapProfiles[0].TargetAvatarId);
    }

    [Fact]
    public void MigrateV3_BumpsVersionTo3()
    {
        var settings = new AppSettings();
        AvatarSwapMigrationService.Migrate(settings);
        Assert.Equal(3, settings.AvatarChangeToAvatarSwapMigrationVersion);
    }

    [Fact]
    public void MigrateV3_IsIdempotent()
    {
        var settings = new AppSettings();
        var rule = new TriggerRule { ActionType = OscActionType.AvatarChange, AvatarChangeTargetId = "avtr_a" };
        settings.GlobalOverrideRules.Add(rule);

        AvatarSwapMigrationService.Migrate(settings);
        var firstCount = settings.AvatarSwapProfiles.Count;
        var firstRuleCount = settings.AvatarSwapProfiles[0].ChannelPointRules.Count;

        AvatarSwapMigrationService.Migrate(settings);
        Assert.Equal(firstCount, settings.AvatarSwapProfiles.Count);
        Assert.Equal(firstRuleCount, settings.AvatarSwapProfiles[0].ChannelPointRules.Count);
    }
}
