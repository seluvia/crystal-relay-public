using System;
using System.Linq;
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
}
