using VrcTwitchOscBridge.Models;
using VrcTwitchOscBridge.Services;
using Xunit;

namespace VrcTwitchOscBridge.Tests;

public sealed class AvatarSwapRuntimeDispatchTests
{
    [Fact]
    public void ChannelPointMatching_ConfiguredRewardIdDoesNotFallBackToMismatchedTitle()
    {
        var rule = new TriggerRule
        {
            TriggerType = TwitchTriggerType.ChannelPoints,
            RewardSyncMode = TwitchRewardSyncMode.LinkExisting,
            ChannelPointRewardId = "configured-reward-id",
            ChannelPointRewardTitle = "Stale Reward Title"
        };

        var matches = GetRuntimeRewardCandidates(
            rule,
            "different-reward-id",
            "Stale Reward Title",
            "GetChannelPointCandidates");

        Assert.Empty(matches);
    }

    [Fact]
    public void ChannelPointMatching_ConfiguredRewardIdStillMatchesWhenTwitchTitleChanges()
    {
        var rule = new TriggerRule
        {
            TriggerType = TwitchTriggerType.ChannelPoints,
            RewardSyncMode = TwitchRewardSyncMode.LinkExisting,
            ChannelPointRewardId = "configured-reward-id",
            ChannelPointRewardTitle = "Old Reward Title"
        };

        var match = Assert.Single(GetRuntimeRewardCandidates(
            rule,
            "configured-reward-id",
            "Renamed On Twitch",
            "GetChannelPointCandidates"));

        Assert.Equal(rule.Id, match.Id);
    }

    [Fact]
    public void ChannelPointMatching_CreateOrManageEmptyIdStillMatchesByTitle()
    {
        var rule = new TriggerRule
        {
            TriggerType = TwitchTriggerType.ChannelPoints,
            RewardSyncMode = TwitchRewardSyncMode.CreateOrManage,
            ChannelPointRewardTitle = "Legacy Reward Title"
        };

        var match = Assert.Single(GetRuntimeRewardCandidates(
            rule,
            "incoming-reward-id",
            "Legacy Reward Title",
            "GetChannelPointCandidates"));

        Assert.Equal(rule.Id, match.Id);
    }

    [Fact]
    public void ChannelPointMatching_LinkExistingEmptyIdDoesNotMatchByTitle()
    {
        var rule = new TriggerRule
        {
            TriggerType = TwitchTriggerType.ChannelPoints,
            RewardSyncMode = TwitchRewardSyncMode.LinkExisting,
            ChannelPointRewardTitle = "Unlinked Reward Title"
        };

        var matches = GetRuntimeRewardCandidates(
            rule,
            "incoming-reward-id",
            "Unlinked Reward Title",
            "GetChannelPointCandidates");

        Assert.Empty(matches);
    }

    [Fact]
    public void ActiveFloatBoostMatching_ConfiguredRewardIdDoesNotFallBackToMismatchedTitle()
    {
        var rule = new TriggerRule
        {
            TriggerType = TwitchTriggerType.ChannelPoints,
            ActionType = OscActionType.AvatarParameter,
            ParameterType = OscParameterType.Float,
            DurationSeconds = 10,
            ActiveFloatBoostRewardEnabled = true,
            ActiveFloatBoostRewardId = "configured-boost-id",
            ActiveFloatBoostRewardTitle = "Stale Boost Title"
        };

        var matches = GetRuntimeRewardCandidates(
            rule,
            "different-boost-id",
            "Stale Boost Title",
            "GetActiveFloatBoostCandidates");

        Assert.Empty(matches);
    }

    [Fact]
    public void ActiveFloatBoostMatching_ManagedEmptyIdStillMatchesByTitle()
    {
        var rule = new TriggerRule
        {
            TriggerType = TwitchTriggerType.ChannelPoints,
            ActionType = OscActionType.AvatarParameter,
            ParameterType = OscParameterType.Float,
            DurationSeconds = 10,
            ActiveFloatBoostRewardEnabled = true,
            ActiveFloatBoostRewardTitle = "Managed Boost Title"
        };

        var match = Assert.Single(GetRuntimeRewardCandidates(
            rule,
            "incoming-boost-id",
            "Managed Boost Title",
            "GetActiveFloatBoostCandidates"));

        Assert.Equal(rule.Id, match.Id);
    }

    [Fact]
    public void UniversalRewardMatching_ConfiguredRewardIdDoesNotFallBackToMismatchedTitle()
    {
        var snapshot = CreateUniversalRewardSnapshot(
            "configured-id",
            "Stale Title",
            TwitchRewardSyncMode.LinkExisting);

        var matches = InvokeRewardMatcher(
            "UniversalRewardMatches",
            snapshot,
            "different-id",
            "Stale Title");

        Assert.False(matches);
    }

    [Fact]
    public void AvatarScaleRewardMatching_ConfiguredRewardIdDoesNotFallBackToMismatchedTitle()
    {
        var snapshot = CreateAvatarScaleRewardSnapshot(
            "configured-id",
            "Stale Title",
            TwitchRewardSyncMode.LinkExisting);

        var matches = InvokeRewardMatcher(
            "AvatarScaleRewardMatches",
            snapshot,
            "different-id",
            "Stale Title");

        Assert.False(matches);
    }

    [Fact]
    public void AvatarScaleMasterRewardMatching_ConfiguredRewardIdDoesNotFallBackToMismatchedTitle()
    {
        var snapshot = CreateAvatarScaleMasterRewardSnapshot(
            "configured-id",
            "Stale Title",
            TwitchRewardSyncMode.LinkExisting);

        var matches = InvokeRewardMatcher(
            "AvatarScaleMasterRewardMatches",
            snapshot,
            "different-id",
            "Stale Title");

        Assert.False(matches);
    }

    [Fact]
    public void UniversalAndAvatarScaleRewardMatching_CreateOrManageEmptyIdsStillMatchByTitle()
    {
        Assert.True(InvokeRewardMatcher(
            "UniversalRewardMatches",
            CreateUniversalRewardSnapshot(
                string.Empty,
                "Legacy Title",
                TwitchRewardSyncMode.CreateOrManage),
            "incoming-id",
            "Legacy Title"));
        Assert.True(InvokeRewardMatcher(
            "AvatarScaleRewardMatches",
            CreateAvatarScaleRewardSnapshot(
                string.Empty,
                "Legacy Title",
                TwitchRewardSyncMode.CreateOrManage),
            "incoming-id",
            "Legacy Title"));
        Assert.True(InvokeRewardMatcher(
            "AvatarScaleMasterRewardMatches",
            CreateAvatarScaleMasterRewardSnapshot(
                string.Empty,
                "Legacy Title",
                TwitchRewardSyncMode.CreateOrManage),
            "incoming-id",
            "Legacy Title"));
    }

    [Fact]
    public void UniversalAndAvatarScaleRewardMatching_LinkExistingEmptyIdsDoNotMatchByTitle()
    {
        Assert.False(InvokeRewardMatcher(
            "UniversalRewardMatches",
            CreateUniversalRewardSnapshot(
                string.Empty,
                "Unlinked Title",
                TwitchRewardSyncMode.LinkExisting),
            "incoming-id",
            "Unlinked Title"));
        Assert.False(InvokeRewardMatcher(
            "AvatarScaleRewardMatches",
            CreateAvatarScaleRewardSnapshot(
                string.Empty,
                "Unlinked Title",
                TwitchRewardSyncMode.LinkExisting),
            "incoming-id",
            "Unlinked Title"));
        Assert.False(InvokeRewardMatcher(
            "AvatarScaleMasterRewardMatches",
            CreateAvatarScaleMasterRewardSnapshot(
                string.Empty,
                "Unlinked Title",
                TwitchRewardSyncMode.LinkExisting),
            "incoming-id",
            "Unlinked Title"));
    }

    [Fact]
    public void UniversalAndAvatarScaleRewardMatching_ConfiguredIdsStillMatchAfterTwitchRename()
    {
        Assert.True(InvokeRewardMatcher(
            "UniversalRewardMatches",
            CreateUniversalRewardSnapshot(
                "configured-id",
                "Old Title",
                TwitchRewardSyncMode.LinkExisting),
            "configured-id",
            "Renamed On Twitch"));
        Assert.True(InvokeRewardMatcher(
            "AvatarScaleRewardMatches",
            CreateAvatarScaleRewardSnapshot(
                "configured-id",
                "Old Title",
                TwitchRewardSyncMode.LinkExisting),
            "configured-id",
            "Renamed On Twitch"));
        Assert.True(InvokeRewardMatcher(
            "AvatarScaleMasterRewardMatches",
            CreateAvatarScaleMasterRewardSnapshot(
                "configured-id",
                "Old Title",
                TwitchRewardSyncMode.LinkExisting),
            "configured-id",
            "Renamed On Twitch"));
    }

    [Fact]
    public void SetTriggerMasterMatching_LinkExistingEmptyIdDoesNotMatchByTitle()
    {
        var snapshot = CreateSetTriggerMasterRewardSnapshot(
            TwitchRewardSyncMode.LinkExisting,
            string.Empty,
            "Unlinked Set Trigger Master");

        var matches = GetRuntimeRewardCandidates(
            [snapshot],
            "incoming-id",
            "Unlinked Set Trigger Master",
            "GetChannelPointCandidates");

        Assert.Empty(matches);
    }

    [Fact]
    public void SetTriggerMasterMatching_CreateOrManageEmptyIdStillMatchesByTitle()
    {
        var snapshot = CreateSetTriggerMasterRewardSnapshot(
            TwitchRewardSyncMode.CreateOrManage,
            string.Empty,
            "Managed Set Trigger Master");

        var match = Assert.Single(GetRuntimeRewardCandidates(
            [snapshot],
            "incoming-id",
            "Managed Set Trigger Master",
            "GetChannelPointCandidates"));

        Assert.Equal(snapshot.Id, match.Id);
    }

    [Fact]
    public void FromSettings_EmptyLinkedRewardIdsAreNotRuntimeReadyButManagedTitlesRemainReady()
    {
        var settings = new AppSettings();
        var unlinkedUniversal = CreateUniversalRewardRule(
            string.Empty,
            "Retained Universal Title",
            TwitchRewardSyncMode.LinkExisting);
        var managedUniversal = CreateUniversalRewardRule(
            string.Empty,
            "Managed Universal Title",
            TwitchRewardSyncMode.CreateOrManage);
        var unlinkedScale = new AvatarScaleRule
        {
            TriggerType = AvatarScaleTriggerType.ChannelPointReward,
            RewardId = string.Empty,
            RewardTitle = "Retained Scale Title",
            RewardSyncMode = TwitchRewardSyncMode.LinkExisting
        };
        var managedScale = new AvatarScaleRule
        {
            TriggerType = AvatarScaleTriggerType.ChannelPointReward,
            RewardId = string.Empty,
            RewardTitle = "Managed Scale Title",
            RewardSyncMode = TwitchRewardSyncMode.CreateOrManage
        };
        settings.UniversalTriggers.Add(unlinkedUniversal);
        settings.UniversalTriggers.Add(managedUniversal);
        settings.AvatarScaleRules.Add(unlinkedScale);
        settings.AvatarScaleRules.Add(managedScale);
        settings.AvatarScaleMasterReward.IsEnabled = true;
        settings.AvatarScaleMasterReward.RewardSyncMode = TwitchRewardSyncMode.LinkExisting;
        settings.AvatarScaleMasterReward.RewardId = string.Empty;
        settings.AvatarScaleMasterReward.RewardTitle = "Retained Master Title";

        var configuration = BridgeRuntimeConfiguration.FromSettings(
            settings,
            RuntimeConfig.CreateDefault(),
            null);

        Assert.DoesNotContain(configuration.UniversalTriggers, rule => rule.Id == unlinkedUniversal.Id);
        Assert.Contains(configuration.UniversalTriggers, rule => rule.Id == managedUniversal.Id);
        Assert.DoesNotContain(configuration.AvatarScaleRules, rule => rule.Id == unlinkedScale.Id);
        Assert.Contains(configuration.AvatarScaleRules, rule => rule.Id == managedScale.Id);
        Assert.False(configuration.AvatarScaleMasterReward.IsEnabled);
    }

    [Fact]
    public void FromSettings_EmptyLinkedRewardWithChatFallbackMatchesOnlyCommand()
    {
        var settings = new AppSettings();
        var universal = CreateUniversalRewardRule(
            string.Empty,
            "Retained Universal Title",
            TwitchRewardSyncMode.LinkExisting);
        universal.ChatCommandEnabled = true;
        universal.CommandText = "!fused";
        universal.ChatCommandPermission = ChatCommandPermission.Everyone;
        var scale = new AvatarScaleRule
        {
            TriggerType = AvatarScaleTriggerType.ChannelPointReward,
            RewardSyncMode = TwitchRewardSyncMode.LinkExisting,
            RewardTitle = "Retained Scale Title",
            ChatCommandEnabled = true,
            CommandText = "!scale",
            ChatCommandPermission = ChatCommandPermission.Everyone
        };
        settings.UniversalTriggers.Add(universal);
        settings.AvatarScaleRules.Add(scale);

        var configuration = BridgeRuntimeConfiguration.FromSettings(
            settings,
            RuntimeConfig.CreateDefault(),
            null);
        var universalSnapshot = Assert.Single(configuration.UniversalTriggers, rule => rule.Id == universal.Id);
        var scaleSnapshot = Assert.Single(configuration.AvatarScaleRules, rule => rule.Id == scale.Id);

        Assert.False(InvokeIncomingMatcher(
            "UniversalTriggerMatches",
            universalSnapshot,
            UniversalTriggerType.ChannelPointReward,
            incomingRewardId: "incoming-id",
            incomingRewardTitle: universal.RewardTitle));
        Assert.True(InvokeIncomingMatcher(
            "UniversalTriggerMatches",
            universalSnapshot,
            UniversalTriggerType.ChatCommand,
            chatMessageText: "!fused"));
        Assert.False(InvokeIncomingMatcher(
            "AvatarScaleRuleMatches",
            scaleSnapshot,
            UniversalTriggerType.ChannelPointReward,
            incomingRewardId: "incoming-id",
            incomingRewardTitle: scale.RewardTitle));
        Assert.True(InvokeIncomingMatcher(
            "AvatarScaleRuleMatches",
            scaleSnapshot,
            UniversalTriggerType.ChatCommand,
            chatMessageText: "!scale"));
    }

    [Fact]
    public void FromSettings_CreateOrManageIdOnlyRewardsRemainRuntimeReady()
    {
        var settings = new AppSettings();
        var universal = CreateUniversalRewardRule(
            "managed-universal-id",
            string.Empty,
            TwitchRewardSyncMode.CreateOrManage);
        var scale = new AvatarScaleRule
        {
            TriggerType = AvatarScaleTriggerType.ChannelPointReward,
            RewardSyncMode = TwitchRewardSyncMode.CreateOrManage,
            RewardId = "managed-scale-id",
            RewardTitle = string.Empty
        };
        settings.UniversalTriggers.Add(universal);
        settings.AvatarScaleRules.Add(scale);
        settings.AvatarScaleMasterReward.IsEnabled = true;
        settings.AvatarScaleMasterReward.RewardSyncMode = TwitchRewardSyncMode.CreateOrManage;
        settings.AvatarScaleMasterReward.RewardId = "managed-master-id";
        settings.AvatarScaleMasterReward.RewardTitle = string.Empty;

        var configuration = BridgeRuntimeConfiguration.FromSettings(
            settings,
            RuntimeConfig.CreateDefault(),
            null);
        var universalSnapshot = Assert.Single(configuration.UniversalTriggers, rule => rule.Id == universal.Id);
        var scaleSnapshot = Assert.Single(configuration.AvatarScaleRules, rule => rule.Id == scale.Id);

        Assert.True(configuration.AvatarScaleMasterReward.IsEnabled);
        Assert.True(InvokeRewardMatcher(
            "UniversalRewardMatches",
            universalSnapshot,
            universal.RewardId,
            "Renamed Universal Reward"));
        Assert.True(InvokeRewardMatcher(
            "AvatarScaleRewardMatches",
            scaleSnapshot,
            scale.RewardId,
            "Renamed Scale Reward"));
        Assert.True(InvokeRewardMatcher(
            "AvatarScaleMasterRewardMatches",
            configuration.AvatarScaleMasterReward,
            settings.AvatarScaleMasterReward.RewardId,
            "Renamed Master Reward"));
    }

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

    [Fact]
    public void FromSettings_FlattensAvatarSwapChannelPointRuleIntoMatchableRules()
    {
        var settings = new AppSettings();
        var profile = new AvatarSwapProfile { TargetAvatarId = "avtr_target", TargetAvatarName = "Target" };
        var cpRule = new TriggerRule
        {
            TriggerType = TwitchTriggerType.ChannelPoints,
            ActionType = OscActionType.AvatarChange,
            AvatarChangeTargetId = "avtr_target",
            AvatarTargetName = "Target",
            ChannelPointRewardTitle = "Swap To Target",
            ChannelPointRewardCost = 100
        };
        profile.ChannelPointRules.Add(cpRule);
        settings.AvatarSwapProfiles.Add(profile);

        var config = BridgeRuntimeConfiguration.FromSettings(settings, RuntimeConfig.CreateDefault(), null);

        // The rule must be in the matchable Rules list so the runtime index can match it.
        Assert.Contains(config.Rules, r => ReferenceEquals(r.Rule, cpRule));
        // And it must still route back to the avatar swap profile.
        Assert.NotNull(config.FindAvatarSwapProfileForRule(cpRule));
    }

    [Fact]
    public void FromSettings_FlattensAvatarSwapBitsRuleIntoMatchableRules()
    {
        var settings = new AppSettings();
        var profile = new AvatarSwapProfile { TargetAvatarId = "avtr_target", TargetAvatarName = "Target" };
        var bitsRule = new TriggerRule
        {
            TriggerType = TwitchTriggerType.Bits,
            ActionType = OscActionType.AvatarChange,
            AvatarChangeTargetId = "avtr_target",
            AvatarTargetName = "Target",
            MinimumAmount = 100
        };
        profile.BitsRules.Add(bitsRule);
        settings.AvatarSwapProfiles.Add(profile);

        var config = BridgeRuntimeConfiguration.FromSettings(settings, RuntimeConfig.CreateDefault(), null);

        Assert.Contains(config.Rules, r => ReferenceEquals(r.Rule, bitsRule));
    }

    [Fact]
    public void FromSettings_RegistersAvatarSwapPaymentRuleWithCashPaymentMatcher()
    {
        var settings = new AppSettings();
        var profile = new AvatarSwapProfile { TargetAvatarId = "avtr_target", TargetAvatarName = "Target" };
        var payRule = new CashPaymentRule
        {
            Name = "Tip Swap",
            Provider = CashPaymentProvider.StreamElements,
            IsEnabled = true,
            ActionKind = CashPaymentActionKind.TriggerAction
        };
        payRule.TriggerAction.ActionType = OscActionType.AvatarChange;
        payRule.TriggerAction.AvatarChangeTargetId = "avtr_target";
        payRule.TriggerAction.AvatarTargetName = "Target";
        profile.PaymentRules.Add(payRule);
        settings.AvatarSwapProfiles.Add(profile);

        var config = BridgeRuntimeConfiguration.FromSettings(settings, RuntimeConfig.CreateDefault(), null);

        // The payment rule must be in the cash-payment matcher so it fires on cash events.
        Assert.Contains(config.CashPaymentRules, r => r.Id == payRule.Id);
        // And its trigger action routes back to the avatar swap profile.
        Assert.NotNull(config.FindAvatarSwapProfileForRule(payRule.TriggerAction));
    }

    private static TriggerRuleSnapshot[] GetRuntimeRewardCandidates(
        TriggerRule rule,
        string incomingRewardId,
        string incomingRewardTitle,
        string methodName) =>
        GetRuntimeRewardCandidates(
            [TriggerRuleSnapshot.FromRule(rule)],
            incomingRewardId,
            incomingRewardTitle,
            methodName);

    private static TriggerRuleSnapshot[] GetRuntimeRewardCandidates(
        IReadOnlyList<TriggerRuleSnapshot> rules,
        string incomingRewardId,
        string incomingRewardTitle,
        string methodName)
    {
        var runtimeRuleIndexType = typeof(BridgeCoordinator).GetNestedType(
            "RuntimeRuleIndex",
            System.Reflection.BindingFlags.NonPublic);
        Assert.NotNull(runtimeRuleIndexType);
        var createMethod = runtimeRuleIndexType.GetMethod(
            "Create",
            System.Reflection.BindingFlags.Public | System.Reflection.BindingFlags.Static);
        Assert.NotNull(createMethod);
        var runtimeRuleIndex = createMethod.Invoke(
            null,
            new object[] { rules });
        Assert.NotNull(runtimeRuleIndex);
        var matchMethod = runtimeRuleIndexType.GetMethod(
            methodName,
            System.Reflection.BindingFlags.Public | System.Reflection.BindingFlags.Instance);
        Assert.NotNull(matchMethod);

        var matches = matchMethod.Invoke(
            runtimeRuleIndex,
            new object[] { incomingRewardId, incomingRewardTitle });
        return Assert.IsAssignableFrom<IEnumerable<TriggerRuleSnapshot>>(matches).ToArray();
    }

    private static UniversalTriggerRuleSnapshot CreateUniversalRewardSnapshot(
        string rewardId,
        string rewardTitle,
        TwitchRewardSyncMode rewardSyncMode)
    {
        return BridgeRuntimeConfiguration.CreateManualTestSnapshot(
            CreateUniversalRewardRule(rewardId, rewardTitle, rewardSyncMode));
    }

    private static UniversalTriggerRule CreateUniversalRewardRule(
        string rewardId,
        string rewardTitle,
        TwitchRewardSyncMode rewardSyncMode)
    {
        var rule = new UniversalTriggerRule
        {
            TriggerType = UniversalTriggerType.ChannelPointReward,
            RewardId = rewardId,
            RewardTitle = rewardTitle,
            RewardSyncMode = rewardSyncMode
        };
        rule.Actions.Add(new UniversalTriggerAction
        {
            OscAddress = "/avatar/parameters/Test",
            TargetValue = "1"
        });
        return rule;
    }

    private static AvatarScaleRuleSnapshot CreateAvatarScaleRewardSnapshot(
        string rewardId,
        string rewardTitle,
        TwitchRewardSyncMode rewardSyncMode)
    {
        return BridgeRuntimeConfiguration.CreateManualTestSnapshot(new AvatarScaleRule
        {
            TriggerType = AvatarScaleTriggerType.ChannelPointReward,
            RewardId = rewardId,
            RewardTitle = rewardTitle,
            RewardSyncMode = rewardSyncMode
        });
    }

    private static AvatarScaleMasterRewardSnapshot CreateAvatarScaleMasterRewardSnapshot(
        string rewardId,
        string rewardTitle,
        TwitchRewardSyncMode rewardSyncMode)
    {
        var settings = new AppSettings();
        settings.AvatarScaleMasterReward.IsEnabled = true;
        settings.AvatarScaleMasterReward.RewardId = rewardId;
        settings.AvatarScaleMasterReward.RewardTitle = rewardTitle;
        settings.AvatarScaleMasterReward.RewardSyncMode = rewardSyncMode;
        return BridgeRuntimeConfiguration.FromSettings(
            settings,
            RuntimeConfig.CreateDefault(),
            null).AvatarScaleMasterReward;
    }

    private static TriggerRuleSnapshot CreateSetTriggerMasterRewardSnapshot(
        TwitchRewardSyncMode rewardSyncMode,
        string rewardId,
        string rewardTitle)
    {
        var settings = new AppSettings();
        var profile = new AvatarTriggerProfile
        {
            AvatarId = "avtr_test",
            UseSharedNumberedOutfitReward = true,
            SetTriggerMasterRewardSyncMode = rewardSyncMode,
            SetTriggerMasterRewardId = rewardId,
            SetTriggerMasterRewardTitle = rewardTitle
        };
        var rule = new TriggerRule
        {
            TriggerType = TwitchTriggerType.ChannelPoints,
            ActionType = OscActionType.SetTrigger,
            SharedRewardChoiceEnabled = true,
            SharedRewardChoiceNumber = 1,
            ChannelPointRewardId = rewardId,
            ChannelPointRewardTitle = rewardTitle
        };
        rule.SetTriggerActions.Add(new SetTriggerAction
        {
            ParameterName = "/avatar/parameters/Test",
            ParameterType = OscParameterType.Bool,
            ParameterValue = "true"
        });
        profile.ChannelPointRules.Add(rule);
        settings.AvatarProfiles.Add(profile);

        return Assert.Single(
            BridgeRuntimeConfiguration.FromSettings(settings, RuntimeConfig.CreateDefault(), null).Rules,
            snapshot => snapshot.Id == rule.Id);
    }

    private static bool InvokeRewardMatcher(
        string methodName,
        object configuredReward,
        string incomingRewardId,
        string incomingRewardTitle) =>
        InvokeIncomingMatcher(
            methodName,
            configuredReward,
            UniversalTriggerType.ChannelPointReward,
            incomingRewardId,
            incomingRewardTitle);

    private static bool InvokeIncomingMatcher(
        string methodName,
        object configuredReward,
        UniversalTriggerType triggerType,
        string incomingRewardId = "",
        string incomingRewardTitle = "",
        string chatMessageText = "")
    {
        var incomingEventType = typeof(BridgeCoordinator).GetNestedType(
            "UniversalIncomingEvent",
            System.Reflection.BindingFlags.NonPublic);
        Assert.NotNull(incomingEventType);
        var constructor = Assert.Single(
            incomingEventType.GetConstructors(
                System.Reflection.BindingFlags.Instance
                | System.Reflection.BindingFlags.Public
                | System.Reflection.BindingFlags.NonPublic),
            candidate => candidate.GetParameters().Length == 13);
        var incomingEvent = constructor.Invoke(new object?[]
        {
            triggerType,
            "Viewer",
            string.Empty,
            string.Empty,
            0,
            incomingRewardId,
            incomingRewardTitle,
            chatMessageText,
            string.Empty,
            0,
            Array.Empty<string>(),
            false,
            false
        });
        var method = typeof(BridgeCoordinator).GetMethod(
            methodName,
            System.Reflection.BindingFlags.Static | System.Reflection.BindingFlags.NonPublic);
        Assert.NotNull(method);
        return Assert.IsType<bool>(method.Invoke(null, new[] { configuredReward, incomingEvent }));
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
