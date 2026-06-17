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
}
