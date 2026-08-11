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
        Assert.Equal(AvatarSwapMigrationService.CurrentMigrationVersion, settings.AvatarChangeToAvatarSwapMigrationVersion);
    }

    [Fact]
    public void MigrateV3_FoldsLegacyProfileChatCommandIntoAdvancedRules()
    {
        var settings = new AppSettings();
        var profile = new AvatarTriggerProfile { AvatarId = "avtr_a", AvatarName = "A" };
        var rule = new TriggerRule
        {
            TriggerType = TwitchTriggerType.ChatCommand,
            ActionType = OscActionType.AvatarChange,
            AvatarChangeTargetId = "avtr_b",
            AvatarTargetName = "B",
            ChatCommandEnabled = true,
            ChatCommandText = "!swap"
        };
        profile.ChannelPointRules.Add(rule);
        settings.AvatarProfiles.Add(profile);

        AvatarSwapMigrationService.Migrate(settings);

        var swapProfile = Assert.Single(settings.AvatarSwapProfiles);
        Assert.Empty(profile.ChannelPointRules);
        Assert.Empty(swapProfile.ChannelPointRules);
        var migrated = Assert.Single(swapProfile.AdvancedRules);
        Assert.Same(rule, migrated);
        Assert.Equal(rule.Id, migrated.Id);
        Assert.Equal(TwitchTriggerType.ChatCommand, migrated.TriggerType);
        Assert.Equal(TriggerRuleSource.AvatarSet, migrated.Source);
    }

    [Fact]
    public void MigrateV3_FoldsAndRemovesFromGlobalOverrideRules()
    {
        var settings = new AppSettings();
        var rule = new TriggerRule
        {
            TriggerType = TwitchTriggerType.Bits,
            ActionType = OscActionType.AvatarChange,
            AvatarChangeTargetId = "avtr_a",
            AvatarTargetName = "A"
        };
        settings.GlobalOverrideRules.Add(rule);

        AvatarSwapMigrationService.Migrate(settings);

        Assert.Empty(settings.GlobalOverrideRules);
        Assert.Single(settings.AvatarSwapProfiles[0].BitsRules);
        Assert.Equal(TriggerRuleSource.GlobalOverride, rule.Source);
    }

    [Theory]
    [InlineData(TwitchTriggerType.ChatCommand)]
    [InlineData(TwitchTriggerType.Follow)]
    public void MigrateV3_FoldsLegacyGlobalNonChannelPointRulesIntoAdvancedRules(
        TwitchTriggerType triggerType)
    {
        var settings = new AppSettings();
        var rule = new TriggerRule
        {
            TriggerType = triggerType,
            ActionType = OscActionType.AvatarChange,
            AvatarChangeTargetId = "avtr_a",
            AvatarTargetName = "A"
        };
        settings.GlobalOverrideRules.Add(rule);

        AvatarSwapMigrationService.Migrate(settings);

        var swapProfile = Assert.Single(settings.AvatarSwapProfiles);
        Assert.Empty(settings.GlobalOverrideRules);
        Assert.Empty(swapProfile.ChannelPointRules);
        var migrated = Assert.Single(swapProfile.AdvancedRules);
        Assert.Same(rule, migrated);
        Assert.Equal(rule.Id, migrated.Id);
        Assert.Equal(triggerType, migrated.TriggerType);
        Assert.Equal(TriggerRuleSource.GlobalOverride, migrated.Source);
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
        Assert.Single(settings.AvatarSwapProfiles[0].PaymentRules);
        Assert.Equal(cashPaymentId.ToString(), rule.TriggerAction.CashPaymentRuleId);
    }

    [Fact(Skip = "v3->v4 split: v3 step no longer creates RouletteRules; AvatarRouletteProfile is now created by the v3->v4 step. End-to-end migration is covered by the manual smoke test.")]
    public void MigrateV3_FoldsAvatarRoulette()
    {
        // The v3 migration step now puts all rules into ChannelPointRules since the
        // BitsSubsRules/RouletteRules collections no longer exist on the live model.
        // The roulette rules are then converted to AvatarRouletteProfile by the v3->v4 step.
        // This test is skipped because the v3->v3 transition in isolation is no longer
        // meaningful; the full migration (which produces AvatarRouletteProfile) is covered
        // by the manual smoke test.
        Assert.True(true);
    }

    [Fact]
    public void MigrateV3_BumpsVersionTo3()
    {
        var settings = new AppSettings();
        AvatarSwapMigrationService.Migrate(settings);
        Assert.Equal(AvatarSwapMigrationService.CurrentMigrationVersion, settings.AvatarChangeToAvatarSwapMigrationVersion);
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
