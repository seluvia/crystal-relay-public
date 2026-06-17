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
}
