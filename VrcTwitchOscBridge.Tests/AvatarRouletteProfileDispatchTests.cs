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
            ChannelPointRewardId = "rew_1",
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

    [Fact]
    public void CreateManualTestSnapshot_AllowsRouletteProfilePool()
    {
        var roulette = new AvatarRouletteProfile { Name = "Demo" };
        roulette.Pool.Add(new RouletteAvatarEntry { AvatarId = "a1", AvatarName = "One" });
        var trigger = new TriggerRule
        {
            TriggerType = TwitchTriggerType.ChannelPoints,
            ActionType = OscActionType.AvatarRoulet,
            ChannelPointRewardId = "rew_1",
        };
        roulette.Triggers.Add(trigger);

        var snapshot = BridgeRuntimeConfiguration.CreateManualTestSnapshot(
            trigger,
            isGlobalOverride: true,
            profile: null,
            rouletteProfile: roulette);

        Assert.Equal(OscActionType.AvatarRoulet, snapshot.ActionType);
        Assert.Same(trigger, snapshot.Rule);
    }

    [Fact]
    public void FindRouletteProfileForRule_ReturnsProfileForTrigger()
    {
        var s = new AppSettings();
        var roulette = new AvatarRouletteProfile { Name = "Demo" };
        var trigger = new TriggerRule
        {
            ActionType = OscActionType.AvatarRoulet,
            ChannelPointRewardId = "rew_1",
        };
        trigger.AvatarRouletAvatarIds.Add("avtr_x");
        roulette.Triggers.Add(trigger);
        s.AvatarRouletteProfiles.Add(roulette);

        var config = BridgeRuntimeConfiguration.FromSettings(s, RuntimeConfig.CreateDefault(), null);

        var found = config.FindRouletteProfileForRule(trigger);
        Assert.NotNull(found);
        Assert.Equal("Demo", found.Name);
    }

    [Fact]
    public void FindRouletteProfileForRule_ReturnsNullForUnknownRule()
    {
        var s = new AppSettings();
        var config = BridgeRuntimeConfiguration.FromSettings(s, RuntimeConfig.CreateDefault(), null);
        var stray = new TriggerRule { ActionType = OscActionType.AvatarRoulet };
        Assert.Null(config.FindRouletteProfileForRule(stray));
    }

    [Fact]
    public void FindAvatarSwapProfileForRule_LocatesRuleInBitsRules()
    {
        var s = new AppSettings();
        var p = new AvatarSwapProfile { TargetAvatarId = "avtr_a" };
        var bits = new TriggerRule { TriggerType = TwitchTriggerType.Bits, ActionType = OscActionType.AvatarChange, AvatarChangeTargetId = "avtr_b", AvatarTargetName = "B" };
        p.BitsRules.Add(bits);
        s.AvatarSwapProfiles.Add(p);

        var config = BridgeRuntimeConfiguration.FromSettings(s, RuntimeConfig.CreateDefault(), null);

        var found = config.FindAvatarSwapProfileForRule(bits);
        Assert.NotNull(found);
        Assert.Equal("avtr_a", found.TargetAvatarId);
    }

    [Fact]
    public void ResolveAvatarSwapAction_UsesGlobalReturnAvatar()
    {
        var s = new AppSettings
        {
            MasterAvatarSwapReturnId = "avtr_return",
            MasterAvatarSwapReturnName = "Return",
        };
        var p = new AvatarSwapProfile { TargetAvatarId = "avtr_target" };
        s.AvatarSwapProfiles.Add(p);

        var config = BridgeRuntimeConfiguration.FromSettings(s, RuntimeConfig.CreateDefault(), null);
        Assert.Equal("avtr_return", config.MasterAvatarSwapReturnId);
    }
}
