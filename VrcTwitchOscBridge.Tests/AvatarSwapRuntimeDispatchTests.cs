using VrcTwitchOscBridge.Models;
using VrcTwitchOscBridge.Services;
using Xunit;

namespace VrcTwitchOscBridge.Tests;

public sealed class AvatarSwapRuntimeDispatchTests
{
    [Fact]
    public void FindAvatarSwapProfileForRule_ReturnsProfileForMigratedRule()
    {
        var profile = new AvatarSwapProfile { TargetAvatarId = "avtr_a" };
        var rule = new TriggerRule { ActionType = OscActionType.AvatarChange, AvatarChangeTargetId = "avtr_a" };
        profile.ChannelPointRules.Add(rule);
        var settings = new AppSettings();
        settings.AvatarSwapProfiles.Add(profile);

        var config = BridgeRuntimeConfiguration.FromSettings(settings, RuntimeConfig.CreateDefault(), null);
        var found = config.FindAvatarSwapProfileForRule(rule);

        Assert.NotNull(found);
        Assert.Equal("avtr_a", found.TargetAvatarId);
    }

    [Fact]
    public void FindAvatarSwapProfileForRule_ReturnsNullForUnmigratedRule()
    {
        var settings = new AppSettings();
        var config = BridgeRuntimeConfiguration.FromSettings(settings, RuntimeConfig.CreateDefault(), null);
        var rule = new TriggerRule { ActionType = OscActionType.AvatarChange };

        Assert.Null(config.FindAvatarSwapProfileForRule(rule));
    }

    // NOTE: The plan originally listed 6 additional placeholder tests for
    // ResolveAvatarSwapAction return modes and the cash/power-up/roulette
    // dispatch re-routing. Those behaviors live inside BridgeCoordinator as
    // private/internal logic that depends on the live OSC client, the shared
    // return avatar state, and the full coordinator state. They are not
    // practical to unit-test in isolation, so they were intentionally dropped
    // from this file. They will be covered by the manual smoke checks in
    // Task 31 of the Avatar Swap full migration plan.
}
