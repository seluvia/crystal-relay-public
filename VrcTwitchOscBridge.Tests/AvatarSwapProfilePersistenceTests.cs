using System;
using System.Linq;
using System.Text.Json;
using VrcTwitchOscBridge.Models;
using VrcTwitchOscBridge.Services;
using Xunit;

namespace VrcTwitchOscBridge.Tests;

public sealed class AvatarSwapProfilePersistenceTests
{
    [Fact]
    public void PersistedAvatarSwapProfile_RoundTrips_AllRuleTypes()
    {
        var profile = new AvatarSwapProfile
        {
            Id = Guid.NewGuid(),
            TargetAvatarId = "avtr_test",
            TargetAvatarName = "Test Avatar",
            IsEnabled = true,
            BitsMaxSwapTimeEnabled = true,
            SubsMaxSwapTimeEnabled = false,
            MaxSwapTimeSeconds = 900
        };

        var channelPointRule = new TriggerRule
        {
            Id = Guid.NewGuid(),
            Name = "CP Swap",
            TriggerType = TwitchTriggerType.ChannelPoints,
            ActionType = OscActionType.AvatarChange,
            AvatarChangeTargetId = "avtr_test",
            AvatarTargetName = "Test Avatar"
        };
        var bitsRule = new TriggerRule
        {
            Id = Guid.NewGuid(),
            Name = "Bits Swap",
            TriggerType = TwitchTriggerType.Bits,
            ActionType = OscActionType.AvatarChange,
            AvatarChangeTargetId = "avtr_test",
            AvatarTargetName = "Test Avatar"
        };
        var subsRule = new TriggerRule
        {
            Id = Guid.NewGuid(),
            Name = "Subs Swap",
            TriggerType = TwitchTriggerType.Subscriptions,
            ActionType = OscActionType.AvatarChange,
            AvatarChangeTargetId = "avtr_test",
            AvatarTargetName = "Test Avatar"
        };
        var paymentRule = new CashPaymentRule
        {
            Id = Guid.NewGuid(),
            Name = "Tip Swap",
            Provider = CashPaymentProvider.KoFi,
            MinimumAmount = 5m,
            MaximumAmount = 100m,
            ActionKind = CashPaymentActionKind.TriggerAction,
            TriggerAction = new TriggerRule
            {
                Id = Guid.NewGuid(),
                Name = "Tip Swap Action",
                ActionType = OscActionType.AvatarChange,
                AvatarChangeTargetId = "avtr_test",
                AvatarTargetName = "Test Avatar"
            }
        };

        profile.ChannelPointRules.Add(channelPointRule);
        profile.BitsRules.Add(bitsRule);
        profile.SubsRules.Add(subsRule);
        profile.PaymentRules.Add(paymentRule);

        var persisted = SettingsStore.ToPersistedAvatarSwapProfile(profile);

        Assert.NotNull(persisted.ChannelPointRules);
        Assert.Single(persisted.ChannelPointRules);
        Assert.Equal(channelPointRule.Id, persisted.ChannelPointRules[0].Id);
        Assert.NotNull(persisted.BitsRules);
        Assert.Single(persisted.BitsRules);
        Assert.Equal(bitsRule.Id, persisted.BitsRules[0].Id);
        Assert.NotNull(persisted.SubsRules);
        Assert.Single(persisted.SubsRules);
        Assert.Equal(subsRule.Id, persisted.SubsRules[0].Id);
        Assert.NotNull(persisted.PaymentRules);
        Assert.Single(persisted.PaymentRules);
        Assert.Equal(paymentRule.Id, persisted.PaymentRules[0].Id);
        Assert.NotNull(persisted.PaymentRules[0].TriggerAction);
        Assert.Equal(paymentRule.TriggerAction.Id, persisted.PaymentRules[0].TriggerAction!.Id);

        var loaded = SettingsStore.ToAvatarSwapProfile(persisted);

        Assert.Equal(profile.Id, loaded.Id);
        Assert.Equal(profile.TargetAvatarId, loaded.TargetAvatarId);
        Assert.Equal(profile.TargetAvatarName, loaded.TargetAvatarName);
        Assert.True(loaded.BitsMaxSwapTimeEnabled);
        Assert.False(loaded.SubsMaxSwapTimeEnabled);
        Assert.Equal(900, loaded.MaxSwapTimeSeconds);

        Assert.Single(loaded.ChannelPointRules);
        Assert.Equal(channelPointRule.Id, loaded.ChannelPointRules[0].Id);
        Assert.Equal(channelPointRule.Name, loaded.ChannelPointRules[0].Name);

        Assert.Single(loaded.BitsRules);
        Assert.Equal(bitsRule.Id, loaded.BitsRules[0].Id);
        Assert.Equal(bitsRule.Name, loaded.BitsRules[0].Name);

        Assert.Single(loaded.SubsRules);
        Assert.Equal(subsRule.Id, loaded.SubsRules[0].Id);
        Assert.Equal(subsRule.Name, loaded.SubsRules[0].Name);

        Assert.Single(loaded.PaymentRules);
        Assert.Equal(paymentRule.Id, loaded.PaymentRules[0].Id);
        Assert.Equal(paymentRule.Name, loaded.PaymentRules[0].Name);
        Assert.Equal(paymentRule.TriggerAction.Id, loaded.PaymentRules[0].TriggerAction.Id);
    }

    [Fact]
    public void PersistedAvatarSwapProfile_RoundTripsAdvancedChatCommandRule()
    {
        var profile = new AvatarSwapProfile { TargetAvatarId = "avtr_target" };
        var rule = new TriggerRule
        {
            Name = "Chat swap",
            TriggerType = TwitchTriggerType.ChatCommand,
            ActionType = OscActionType.AvatarChange,
            AvatarChangeTargetId = "avtr_target",
            ChatCommandEnabled = true,
            ChatCommandText = "swap",
            ChatCommandPermission = ChatCommandPermission.Broadcaster
        };
        profile.AdvancedRules.Add(rule);

        var persisted = SettingsStore.ToPersistedAvatarSwapProfile(profile);
        var loaded = SettingsStore.ToAvatarSwapProfile(persisted);

        var loadedRule = Assert.Single(loaded.AdvancedRules);
        Assert.Equal(rule.Id, loadedRule.Id);
        Assert.Equal(TwitchTriggerType.ChatCommand, loadedRule.TriggerType);
        Assert.Equal("!swap", loadedRule.ChatCommandText);
        Assert.True(loadedRule.ChatCommandEnabled);
        Assert.Equal(ChatCommandPermission.Broadcaster, loadedRule.ChatCommandPermission);
        Assert.Empty(loaded.ChannelPointRules);
    }

    [Fact]
    public void ToAvatarSwapProfile_MovesLegacyNonChannelPointRulesOutOfChannelPointRules()
    {
        var legacyCommand = new TriggerRule
        {
            TriggerType = TwitchTriggerType.ChatCommand,
            ActionType = OscActionType.AvatarChange,
            AvatarChangeTargetId = "avtr_target",
            ChatCommandEnabled = true,
            ChatCommandText = "!legacy-swap"
        };
        var legacyReward = new TriggerRule
        {
            TriggerType = TwitchTriggerType.ChannelPoints,
            ActionType = OscActionType.AvatarChange,
            AvatarChangeTargetId = "avtr_target",
            ChannelPointRewardTitle = "Swap Reward"
        };
        var persisted = new SettingsStore.PersistedAvatarSwapProfile
        {
            TargetAvatarId = "avtr_target",
            ChannelPointRules = [
                SettingsStore.ToPersistedRule(legacyCommand),
                SettingsStore.ToPersistedRule(legacyReward)
            ]
        };

        var loaded = SettingsStore.ToAvatarSwapProfile(persisted);

        Assert.Single(loaded.ChannelPointRules);
        Assert.Equal(TwitchTriggerType.ChannelPoints, loaded.ChannelPointRules[0].TriggerType);
        var migrated = Assert.Single(loaded.AdvancedRules);
        Assert.Equal(legacyCommand.Id, migrated.Id);
        Assert.Equal(TwitchTriggerType.ChatCommand, migrated.TriggerType);
    }

    [Theory]
    [InlineData(TwitchTriggerType.Subscriptions)]
    [InlineData(TwitchTriggerType.GiftSubscription)]
    public void PersistedAvatarSwapProfile_RoundTripsSubscriptionTriggerSettings(TwitchTriggerType triggerType)
    {
        var profile = new AvatarSwapProfile();
        profile.SubsRules.Add(new TriggerRule
        {
            TriggerType = triggerType,
            ActionType = OscActionType.AvatarChange,
            SubsTriggerCount = 7,
            SubsAccumulationEnabled = true,
            SubsCarryOverEnabled = true
        });

        var persisted = SettingsStore.ToPersistedAvatarSwapProfile(profile);
        var loaded = SettingsStore.ToAvatarSwapProfile(persisted);
        var loadedRule = Assert.Single(loaded.SubsRules);

        Assert.Equal(7, loadedRule.SubsTriggerCount);
        Assert.True(loadedRule.SubsAccumulationEnabled);
        Assert.True(loadedRule.SubsCarryOverEnabled);
    }

    [Theory]
    [InlineData(TwitchTriggerType.Subscriptions)]
    [InlineData(TwitchTriggerType.GiftSubscription)]
    public void PersistedSubscriptionRule_AbsentTriggerCountMigratesLegacyMinimumAmount(
        TwitchTriggerType triggerType)
    {
        var persisted = JsonSerializer.Deserialize<SettingsStore.PersistedTriggerRule>(
            $"{{\"TriggerType\":{(int)triggerType},\"MinimumAmount\":7}}");
        Assert.NotNull(persisted);

        var loaded = SettingsStore.ToRule(persisted!);

        Assert.Equal(7, loaded.SubsTriggerCount);
    }

    [Theory]
    [InlineData(TwitchTriggerType.Subscriptions)]
    [InlineData(TwitchTriggerType.GiftSubscription)]
    public void PersistedSubscriptionRule_ExplicitTriggerCountOneWinsOverStaleMinimumAmount(
        TwitchTriggerType triggerType)
    {
        var persisted = JsonSerializer.Deserialize<SettingsStore.PersistedTriggerRule>(
            $"{{\"TriggerType\":{(int)triggerType},\"MinimumAmount\":7,\"SubsTriggerCount\":1}}");
        Assert.NotNull(persisted);

        var loaded = SettingsStore.ToRule(persisted!);

        Assert.Equal(1, loaded.SubsTriggerCount);
    }
}
