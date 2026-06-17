using VrcTwitchOscBridge.Models;
using VrcTwitchOscBridge.Services;
using Xunit;

namespace VrcTwitchOscBridge.Tests;

public sealed class AvatarRouletteProfileDispatchTests
{
    [Fact]
    public void FromSettings_BuildsAvatarRouletteSnapshots()
    {
        var s = new AppSettings();
        var roulette = new AvatarRouletteProfile { Name = "Demo" };
        roulette.Pool.Add(new RouletteAvatarEntry { AvatarId = "a1", AvatarName = "One" });
        var trigger = new TriggerRule
        {
            TriggerType = TwitchTriggerType.ChannelPoints,
            ActionType = OscActionType.AvatarRoulet,
        };
        roulette.Triggers.Add(trigger);
        s.AvatarRouletteProfiles.Add(roulette);

        var config = BridgeRuntimeConfiguration.FromSettings(s, RuntimeConfig.CreateDefault(), null);

        Assert.Single(config.AvatarRouletteProfiles);
        var snap = config.AvatarRouletteProfiles[0];
        Assert.Equal("Demo", snap.Name);
        Assert.Single(snap.Pool);
        Assert.Equal("a1", snap.Pool[0].AvatarId);
        Assert.Single(snap.Triggers);
    }
}
