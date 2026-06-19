using System.Reflection;
using VrcTwitchOscBridge.Models;
using VrcTwitchOscBridge.Services;
using Xunit;

namespace VrcTwitchOscBridge.Tests;

public sealed class TriggerRuleRoundTripTests
{
    [Fact]
    public void SpecialRulePairingMode_NormalizesOutOfRangeValue()
    {
        var rule = new TriggerRule();
        rule.SpecialRulePairingMode = (SpecialRulePairingMode)999;
        Assert.Equal(SpecialRulePairingMode.HidePairedWhileActive, rule.SpecialRulePairingMode);
    }

    [Fact]
    public void SharedRewardChoiceFields_RoundTripThroughPublicProperties()
    {
        var rule = new TriggerRule
        {
            SharedRewardChoiceEnabled = true,
            SharedRewardChoiceNumber = 3,
            SharedRewardHelpText = "Third option"
        };
        Assert.True(rule.SharedRewardChoiceEnabled);
        Assert.Equal(3, rule.SharedRewardChoiceNumber);
        Assert.Equal("Third option", rule.SharedRewardHelpText);
    }

    [Fact]
    public void DeleteManagedRewardWhenInactive_RoundTrips()
    {
        var rule = new TriggerRule { DeleteManagedRewardWhenInactive = true };
        Assert.True(rule.DeleteManagedRewardWhenInactive);
    }

    [Fact]
    public void BotMessageTemplate_RoundTrips()
    {
        var rule = new TriggerRule { BotMessageTemplate = "{user} did the thing" };
        Assert.Equal("{user} did the thing", rule.BotMessageTemplate);
    }

    [Fact]
    public void ChannelPointRewardDescription_RoundTrips()
    {
        var rule = new TriggerRule { ChannelPointRewardDescription = "Long description" };
        Assert.Equal("Long description", rule.ChannelPointRewardDescription);
    }

    [Fact]
    public void ManagedRewardColors_NormalizeAndRoundTrip()
    {
        var rule = new TriggerRule();
        rule.ManagedRewardReadyColor = "#22C55E";
        Assert.Equal("#22C55E", rule.ManagedRewardReadyColor, ignoreCase: true);
        rule.ManagedRewardCooldownColor = "#EF4444";
        Assert.Equal("#EF4444", rule.ManagedRewardCooldownColor, ignoreCase: true);
    }

    [Fact]
    public void ChatCommandPermission_RoundTrips()
    {
        var rule = new TriggerRule { ChatCommandPermission = ChatCommandPermission.Broadcaster };
        Assert.Equal(ChatCommandPermission.Broadcaster, rule.ChatCommandPermission);
    }

    [Fact]
    public void ManagedRewardReadyBrush_ReturnsTransparentForEmptyColor()
    {
        var rule = new TriggerRule();
        var backingField = typeof(TriggerRule).GetField(
            "managedRewardReadyColor",
            BindingFlags.Instance | BindingFlags.NonPublic);
        Assert.NotNull(backingField);
        backingField!.SetValue(rule, string.Empty);
        Assert.Equal(System.Windows.Media.Brushes.Transparent, rule.ManagedRewardReadyBrush);
    }

    [Fact]
    public void BitsKeywordEnabled_DefaultsToFalse()
    {
        var rule = new TriggerRule();
        Assert.False(rule.BitsKeywordEnabled);
    }

    [Fact]
    public void BitsKeywordEnabled_RoundTrips()
    {
        var rule = new TriggerRule { BitsKeywordEnabled = true };
        Assert.True(rule.BitsKeywordEnabled);
    }
}
