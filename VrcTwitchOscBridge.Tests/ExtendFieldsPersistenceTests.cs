using VrcTwitchOscBridge.Models;
using VrcTwitchOscBridge.Services;
using Xunit;

namespace VrcTwitchOscBridge.Tests;

public class ExtendFieldsPersistenceTests
{
    [Fact]
    public void TriggerRule_ExtendFields_RoundTripThroughPersistence()
    {
        var rule = new TriggerRule
        {
            Name = "Test Bits",
            TriggerType = TwitchTriggerType.Bits,
            ExtendCurrentActivity = true,
            ExtendSeconds = 15,
        };

        var persisted = SettingsStore.ToPersistedRule(rule);
        var restored = SettingsStore.ToRule(persisted);

        Assert.True(restored.ExtendCurrentActivity);
        Assert.Equal(15, restored.ExtendSeconds);
    }

    [Fact]
    public void TriggerRule_ExtendFields_DefaultWhenMissing()
    {
        var persisted = new SettingsStore.PersistedTriggerRule
        {
            Name = "Old Save",
            TriggerType = TwitchTriggerType.Bits,
        };

        var restored = SettingsStore.ToRule(persisted);

        Assert.False(restored.ExtendCurrentActivity);
        Assert.Equal(0, restored.ExtendSeconds);
    }

    [Fact]
    public void AvatarScaleRule_ExtendFields_RoundTripThroughPersistence()
    {
        var rule = new AvatarScaleRule
        {
            Name = "Test Scale",
            ExtendCurrentActivity = true,
            ExtendSeconds = 20,
        };

        var persisted = SettingsStore.ToPersistedAvatarScaleRule(rule);
        var restored = SettingsStore.ToAvatarScaleRule(persisted);

        Assert.True(restored.ExtendCurrentActivity);
        Assert.Equal(20, restored.ExtendSeconds);
    }
}
