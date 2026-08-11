using System.IO;
using System.Net;
using System.Net.Sockets;
using System.Reflection;
using System.Runtime.CompilerServices;
using System.Text.Json;
using System.Windows.Threading;
using VrcTwitchOscBridge.Models;
using VrcTwitchOscBridge.Services;
using Xunit;

namespace VrcTwitchOscBridge.Tests;

[CollectionDefinition("BridgeCoordinator integration", DisableParallelization = true)]
public sealed class BridgeCoordinatorIntegrationCollectionDefinition
{
}

[Collection("BridgeCoordinator integration")]
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
    public void FromSettings_IncludesAdvancedAvatarSwapCommandAndFindsOwner()
    {
        var settings = new AppSettings { VrChat = new VrChatAccountSettings() };
        settings.MasterAvatarSwapReturnId = "avtr_return";
        var profile = new AvatarSwapProfile
        {
            TargetAvatarId = "avtr_target",
            TargetAvatarName = "Target"
        };
        var rule = new TriggerRule
        {
            TriggerType = TwitchTriggerType.ChatCommand,
            ActionType = OscActionType.AvatarChange,
            AvatarChangeTargetId = "avtr_target",
            AvatarTargetName = "Target",
            ChatCommandEnabled = true,
            ChatCommandText = "!swap",
            ChatCommandPermission = ChatCommandPermission.Everyone
        };
        profile.AdvancedRules.Add(rule);
        settings.AvatarSwapProfiles.Add(profile);

        var configuration = BridgeRuntimeConfiguration.FromSettings(
            settings,
            RuntimeConfig.CreateDefault(),
            null);

        var snapshot = Assert.Single(configuration.Rules, candidate => candidate.Id == rule.Id);
        Assert.Equal(TwitchTriggerType.ChatCommand, snapshot.TriggerType);
        Assert.True(snapshot.ChatCommandEnabled);
        Assert.Equal("!swap", snapshot.ChatCommandText);
        Assert.Equal("avtr_target", snapshot.AvatarChangeTargetId);
        var owner = Assert.Single(configuration.AvatarSwapProfiles, candidate =>
            candidate.AdvancedRules.Any(candidateRule => candidateRule.Id == rule.Id));
        Assert.Equal("avtr_target", owner.TargetAvatarId);
        Assert.Same(owner, configuration.FindAvatarSwapProfileForRule(rule));
    }

    [Fact]
    public void AdvancedAvatarSwapChatCommand_UsesPermissionAndWholeMessageMatching()
    {
        var settings = new AppSettings { VrChat = new VrChatAccountSettings() };
        settings.MasterAvatarSwapReturnId = "avtr_return";
        var profile = new AvatarSwapProfile
        {
            TargetAvatarId = "avtr_target",
            TargetAvatarName = "Target"
        };
        var rule = new TriggerRule
        {
            TriggerType = TwitchTriggerType.ChatCommand,
            ActionType = OscActionType.AvatarChange,
            AvatarChangeTargetId = "avtr_target",
            AvatarTargetName = "Target",
            ChatCommandEnabled = true,
            ChatCommandText = "!swap"
        };
        profile.AdvancedRules.Add(rule);
        settings.AvatarSwapProfiles.Add(profile);

        var configuration = BridgeRuntimeConfiguration.FromSettings(
            settings,
            RuntimeConfig.CreateDefault(),
            null);
        var snapshot = Assert.Single(configuration.Rules, candidate => candidate.Id == rule.Id);

        var everyone = snapshot with { ChatCommandPermission = ChatCommandPermission.Everyone };
        Assert.True(InvokeChatCommandMatcher(configuration, everyone, "!swap", false, false));
        Assert.True(InvokeChatCommandMatcher(configuration, everyone, "!swap", true, false));
        Assert.True(InvokeChatCommandMatcher(configuration, everyone, "!swap", false, true));
        Assert.False(InvokeChatCommandMatcher(configuration, everyone, "!swap extra", true, true));

        var moderators = snapshot with { ChatCommandPermission = ChatCommandPermission.Moderators };
        Assert.False(InvokeChatCommandMatcher(configuration, moderators, "!swap", false, false));
        Assert.True(InvokeChatCommandMatcher(configuration, moderators, "!swap", true, false));
        Assert.True(InvokeChatCommandMatcher(configuration, moderators, "!swap", false, true));

        var broadcaster = snapshot with { ChatCommandPermission = ChatCommandPermission.Broadcaster };
        Assert.False(InvokeChatCommandMatcher(configuration, broadcaster, "!swap", false, false));
        Assert.False(InvokeChatCommandMatcher(configuration, broadcaster, "!swap", true, false));
        Assert.True(InvokeChatCommandMatcher(configuration, broadcaster, "!swap", false, true));
    }

    [Fact]
    public async Task FollowNotificationAndSimulation_ExecuteAdvancedAvatarSwapRule()
    {
        var settings = new AppSettings
        {
            MasterAvatarSwapReturnId = "avtr_return",
            MasterAvatarSwapReturnName = "Return"
        };
        var profile = new AvatarSwapProfile
        {
            TargetAvatarId = "avtr_target",
            TargetAvatarName = "Target"
        };
        var rule = new TriggerRule
        {
            Name = "Follow swap",
            TriggerType = TwitchTriggerType.Follow,
            ActionType = OscActionType.AvatarChange,
            AvatarChangeTargetId = "avtr_target",
            AvatarTargetName = "Target"
        };
        profile.AdvancedRules.Add(rule);
        settings.AvatarSwapProfiles.Add(profile);

        var configuration = BridgeRuntimeConfiguration.FromSettings(
            settings,
            RuntimeConfig.CreateDefault(),
            null);
        await using var coordinator = CreateCoordinator(new NoOpActivityResumeService());
        using var sendClient = ConfigureCoordinatorForAvatarSwapExecution(coordinator, configuration, "avtr_return");

        var logs = new List<string>();
        coordinator.LogWritten += logs.Add;

        using var eventDocument = JsonDocument.Parse(
            "{\"user_name\":\"Follower\",\"user_id\":\"user-1\",\"user_login\":\"follower\"}");
        await InvokePrivateAsync(
            coordinator,
            "HandleNotificationAsync",
            new EventSubNotification("follow-live", "channel.follow", eventDocument.RootElement.Clone()),
            CancellationToken.None);

        Assert.Equal("avtr_target", coordinator.CurrentVrChatAvatarId);
        Assert.Contains(logs, message => message.Contains("triggered 'Follow swap'", StringComparison.Ordinal));

        SetPrivateField(coordinator, "currentVrChatAvatarId", "avtr_return");
        var simulatedFollow = CreateUniversalIncomingEvent(
            UniversalTriggerType.Follow,
            "Follower",
            "user-1",
            "follower");
        await InvokePrivateAsync(
            coordinator,
            "HandleSimulatedTwitchEventAsync",
            null,
            simulatedFollow,
            CancellationToken.None);

        Assert.Equal("avtr_target", coordinator.CurrentVrChatAvatarId);
        Assert.Equal(2, logs.Count(message => message.Contains("triggered 'Follow swap'", StringComparison.Ordinal)));
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

    [Fact]
    public void SubscriptionMatching_GiftSubscriptionRuleUsesAtLeastConfiguredThreshold()
    {
        var rule = new TriggerRule
        {
            TriggerType = TwitchTriggerType.GiftSubscription,
            ActionType = OscActionType.AvatarChange,
            AvatarChangeTargetId = "avtr_target",
            AvatarTargetName = "Target",
            SubsTriggerCount = 7
        };
        var snapshot = BridgeRuntimeConfiguration.CreateManualTestSnapshot(
            rule,
            isGlobalOverride: true,
            profile: null);
        var settings = new AppSettings();
        var configuration = BridgeRuntimeConfiguration.FromSettings(
            settings,
            RuntimeConfig.CreateDefault(),
            null);
        var runtimeRuleIndexType = typeof(BridgeCoordinator).GetNestedType(
            "RuntimeRuleIndex",
            BindingFlags.NonPublic);
        Assert.NotNull(runtimeRuleIndexType);
        var createMethod = runtimeRuleIndexType!.GetMethod(
            "Create",
            BindingFlags.Public | BindingFlags.Static);
        Assert.NotNull(createMethod);
        var runtimeRuleIndex = createMethod!.Invoke(null, new object[] { new[] { snapshot } });
        Assert.NotNull(runtimeRuleIndex);

        var bridgeEventType = typeof(BridgeCoordinator).GetNestedType(
            "BridgeIncomingEvent",
            BindingFlags.NonPublic);
        Assert.NotNull(bridgeEventType);
        var bridgeEventConstructor = Assert.Single(
            bridgeEventType!.GetConstructors(
                BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic),
            candidate => candidate.GetParameters().Length == 13);
        var bridgeEvent = bridgeEventConstructor.Invoke(new object?[]
        {
            TwitchTriggerType.Subscriptions,
            "Gifter",
            10,
            null,
            null,
            "Gift Sub",
            false,
            string.Empty,
            string.Empty,
            string.Empty,
            Array.Empty<string>(),
            false,
            false
        });

        var coordinator = (BridgeCoordinator)RuntimeHelpers.GetUninitializedObject(typeof(BridgeCoordinator));
        SetPrivateField(coordinator, "subsAccumulator", new Dictionary<Guid, int>());
        var selectMethod = typeof(BridgeCoordinator).GetMethod(
            "SelectMatchingRules",
            BindingFlags.NonPublic | BindingFlags.Instance);
        Assert.NotNull(selectMethod);

        var matches = selectMethod!.Invoke(
            coordinator,
            new[]
            {
                configuration,
                runtimeRuleIndex,
                bridgeEvent,
                string.Empty,
                false,
                Array.Empty<Guid>()
            });

        var matchedRules = Assert.IsAssignableFrom<IEnumerable<TriggerRuleSnapshot>>(matches).ToArray();
        var matchedRule = Assert.Single(matchedRules);
        Assert.Equal(snapshot.Id, matchedRule.Id);
    }

    [Fact]
    public void ZeroDurationRouletteAction_DoesNotBuildReturnPacket()
    {
        var settings = new AppSettings
        {
            MasterAvatarSwapReturnId = "avtr_return",
            MasterAvatarSwapReturnName = "Return"
        };
        var roulette = new AvatarRouletteProfile
        {
            Name = "Demo Roulette",
            ReturnAvatarId = "avtr_return",
            ReturnAvatarName = "Return"
        };
        roulette.Pool.Add(new RouletteAvatarEntry { AvatarId = "avtr_target", AvatarName = "Target" });
        var rule = new TriggerRule
        {
            TriggerType = TwitchTriggerType.ChannelPoints,
            ActionType = OscActionType.AvatarRoulet,
            DurationSeconds = 10,
            ChannelPointRewardId = "rew_roulette",
            ChannelPointRewardTitle = "Roulette Reward"
        };
        roulette.Triggers.Add(rule);
        settings.AvatarRouletteProfiles.Add(roulette);

        var configuration = BridgeRuntimeConfiguration.FromSettings(
            settings,
            RuntimeConfig.CreateDefault(),
            null);
        var rouletteSnapshot = Assert.Single(configuration.AvatarRouletteProfiles);
        var triggerSnapshot = Assert.Single(rouletteSnapshot.Triggers) with { DurationSeconds = 0 };
        var coordinator = (BridgeCoordinator)RuntimeHelpers.GetUninitializedObject(typeof(BridgeCoordinator));
        SetPrivateField(coordinator, "stateGate", new object());
        SetPrivateField(coordinator, "vrChatOscClient", new VrChatOscClient());
        var resolveMethod = typeof(BridgeCoordinator).GetMethod(
            "ResolveRouletteProfileAction",
            BindingFlags.NonPublic | BindingFlags.Instance);
        Assert.NotNull(resolveMethod);

        var action = resolveMethod!.Invoke(coordinator, new object[] { rouletteSnapshot, triggerSnapshot });
        Assert.NotNull(action);
        var hasResetPackets = (bool)action!.GetType().GetProperty("HasResetPackets")!.GetValue(action)!;

        Assert.False(hasResetPackets);
    }

    [Theory]
    [InlineData(OscActionType.AvatarChange)]
    [InlineData(OscActionType.AvatarRoulet)]
    public void TimedSupporterOverrideRepeat_RestartsSameAvatarAction(OscActionType actionType)
    {
        var rule = TestTriggerRuleSnapshotBuilder.Build(
            triggerType: TwitchTriggerType.Subscriptions,
            durationSeconds: 300) with
        {
            ActionType = actionType
        };
        var method = typeof(BridgeCoordinator).GetMethod(
            "ShouldRestartActiveSupporterOverride",
            BindingFlags.NonPublic | BindingFlags.Static);
        Assert.NotNull(method);

        var shouldRestart = (bool)method!.Invoke(null, new object[] { rule, rule })!;

        Assert.True(shouldRestart);

        var parameterRule = rule with { ActionType = OscActionType.AvatarParameter };
        var shouldExtend = (bool)method.Invoke(null, new object[] { parameterRule, parameterRule })!;

        Assert.False(shouldExtend);

        var source = File.ReadAllText(FindSourceFile(
            "VrcTwitchOscBridge",
            "Services",
            "BridgeCoordinator.cs"));
        var handlerBody = GetMethodBody(
            source,
            "private async Task HandleTimedSupporterOverrideTriggerCoreAsync");
        var restartIndex = handlerBody.IndexOf(
            "RestartActiveAvatarSupporterOverrideAsync",
            StringComparison.Ordinal);
        var extendIndex = handlerBody.IndexOf(
            "ExtendActiveSupporterOverrideAsync",
            StringComparison.Ordinal);

        Assert.True(restartIndex >= 0, "The same-rule avatar branch must restart the active avatar override.");
        Assert.True(extendIndex > restartIndex, "The float/parameter extension branch must remain after the restart branch.");
    }

    [Theory]
    [InlineData(TwitchTriggerType.Subscriptions)]
    [InlineData(TwitchTriggerType.GiftSubscription)]
    public void TimedSupporterOverride_TierEnablementCoversBothSubscriptionTriggerTypes(
        TwitchTriggerType triggerType)
    {
        var source = File.ReadAllText(FindSourceFile(
            "VrcTwitchOscBridge",
            "Services",
            "BridgeCoordinator.cs"));
        var body = GetMethodBody(
            source,
            "private async Task HandleTimedSupporterOverrideTriggerCoreAsync");
        var tierCheckIndex = body.IndexOf(
            "IsSubscriptionTierEnabled(rule, bridgeEvent.SubscriptionTier)",
            StringComparison.Ordinal);

        Assert.True(tierCheckIndex >= 0, "The timed supporter handler must apply subscription tier gating.");
        Assert.Contains(
            $"TwitchTriggerType.{triggerType}",
            body[..tierCheckIndex],
            StringComparison.Ordinal);
    }

    [Theory]
    [InlineData(TwitchTriggerType.Subscriptions)]
    [InlineData(TwitchTriggerType.GiftSubscription)]
    public void SupporterOverridePriority_UsesSubscriptionTriggerCount(
        TwitchTriggerType triggerType)
    {
        var higherSubscriptionThreshold = TestTriggerRuleSnapshotBuilder.Build(triggerType) with
        {
            MinimumAmount = 1,
            SubsTriggerCount = 7
        };
        var lowerSubscriptionThreshold = TestTriggerRuleSnapshotBuilder.Build(triggerType) with
        {
            MinimumAmount = 100,
            SubsTriggerCount = 3
        };
        var method = typeof(BridgeCoordinator).GetMethod(
            "CompareSupporterOverridePriority",
            BindingFlags.NonPublic | BindingFlags.Static);
        Assert.NotNull(method);

        var comparison = (int)method!.Invoke(
            null,
            new object[] { higherSubscriptionThreshold, lowerSubscriptionThreshold })!;

        Assert.True(comparison > 0);
    }

    [Fact]
    public void SupporterOverridePriority_UsesMinimumAmountForBits()
    {
        var higherBitsThreshold = TestTriggerRuleSnapshotBuilder.Build(TwitchTriggerType.Bits) with
        {
            MinimumAmount = 100,
            SubsTriggerCount = 1
        };
        var lowerBitsThreshold = TestTriggerRuleSnapshotBuilder.Build(TwitchTriggerType.Bits) with
        {
            MinimumAmount = 3,
            SubsTriggerCount = 7
        };
        var method = typeof(BridgeCoordinator).GetMethod(
            "CompareSupporterOverridePriority",
            BindingFlags.NonPublic | BindingFlags.Static);
        Assert.NotNull(method);

        var comparison = (int)method!.Invoke(
            null,
            new object[] { higherBitsThreshold, lowerBitsThreshold })!;

        Assert.True(comparison > 0);
    }

    [Fact]
    public void SupporterOverrideAnnouncements_DescribeGiftSubscriptionRules()
    {
        var giftRule = TestTriggerRuleSnapshotBuilder.Build(
            triggerType: TwitchTriggerType.GiftSubscription,
            durationSeconds: 60) with
        {
            Name = "Gift avatar time",
            ActionType = OscActionType.AvatarParameter,
            SubsTriggerCount = 7,
            SubscriptionTier1SecondsPerSub = 30,
            SubscriptionTier2SecondsPerSub = 60,
            SubscriptionTier3SecondsPerSub = 90
        };
        var descriptions = InvokePrivateStringList(
            "BuildSupporterOverrideOptionDescriptions",
            new object[] { new[] { giftRule } });

        var description = Assert.Single(descriptions);
        Assert.Contains("Subs 7+", description, StringComparison.Ordinal);

        var scopedGiftRule = giftRule with { SupporterAvatarId = "avtr_current" };
        var announcementOptions = InvokePrivateStringList(
            "BuildCurrentAvatarSupporterAnnouncementOptions",
            new object[] { new[] { scopedGiftRule }, "avtr_current" });

        Assert.Contains(
            announcementOptions,
            option => option.Contains("Gift avatar time", StringComparison.Ordinal));
    }

    [Fact]
    public void TimedSupporterOverrideTransitions_SerializeCompletionAndTriggerStateChanges()
    {
        var source = File.ReadAllText(FindSourceFile(
            "VrcTwitchOscBridge",
            "Services",
            "BridgeCoordinator.cs"));
        var triggerBody = GetMethodBody(
            source,
            "private async Task HandleTimedSupporterOverrideTriggerAsync");
        var completionBody = GetMethodBody(
            source,
            "private async Task CompleteTimedSupporterOverrideAsync");
        var enterBody = GetMethodBody(
            source,
            "private async Task<bool> TryEnterTimedSupporterOverrideTransitionAsync");
        var leaveBody = GetMethodBody(
            source,
            "private void LeaveTimedSupporterOverrideTransitionUser");
        var resetBody = GetMethodBody(source, "private async Task ResetPendingRulesAsync");
        var scheduleBody = GetMethodBody(
            source,
            "private void ScheduleTimedSupporterOverrideCompletion(");
        var graceScheduleBody = GetMethodBody(
            source,
            "private void ScheduleTimedSupporterOverrideCompletionAfterGracePeriod(");
        var startBody = GetMethodBody(source, "private async Task StartCoreAsync");
        var oscOnlyStartBody = GetMethodBody(source, "private async Task StartOscOnlyCoreAsync");
        var stopBody = GetMethodBody(source, "private async Task StopCoreAsync");
        var disposeBody = GetMethodBody(source, "public async ValueTask DisposeAsync");

        Assert.Contains("TryEnterTimedSupporterOverrideTransitionAsync", triggerBody, StringComparison.Ordinal);
        Assert.Contains("LeaveTimedSupporterOverrideTransitionUser", triggerBody, StringComparison.Ordinal);
        Assert.Contains("TryEnterTimedSupporterOverrideTransitionAsync", completionBody, StringComparison.Ordinal);
        Assert.Contains("LeaveTimedSupporterOverrideTransitionUser", completionBody, StringComparison.Ordinal);
        Assert.Contains("timedSupporterOverrideTransitionGate.WaitAsync", enterBody, StringComparison.Ordinal);
        Assert.Contains("timedSupporterOverrideTransitionGate.Release", leaveBody, StringComparison.Ordinal);
        Assert.Contains("timedSupporterOverrideTransitionGate.WaitAsync", resetBody, StringComparison.Ordinal);
        Assert.Contains("timedSupporterOverrideTransitionGate.Release", resetBody, StringComparison.Ordinal);
        Assert.Contains("var completionToken = completionCancellation.Token;", scheduleBody, StringComparison.Ordinal);
        Assert.Contains("var completionToken = completionCancellation.Token;", graceScheduleBody, StringComparison.Ordinal);
        Assert.Contains("ReopenTimedSupporterOverrideTransitionAdmission", startBody, StringComparison.Ordinal);
        Assert.Contains("ReopenTimedSupporterOverrideTransitionAdmission", oscOnlyStartBody, StringComparison.Ordinal);
        Assert.Contains("MarkTimedSupporterOverrideTransitionStopping", stopBody, StringComparison.Ordinal);
        Assert.True(
            stopBody.IndexOf("MarkTimedSupporterOverrideTransitionStopping", StringComparison.Ordinal)
                < stopBody.IndexOf("isStopping = true", StringComparison.Ordinal));
        Assert.Contains("WaitForTimedSupporterOverrideTransitionUsersAsync", stopBody, StringComparison.Ordinal);
        Assert.Contains("MarkTimedSupporterOverrideTransitionDisposalStarted", disposeBody, StringComparison.Ordinal);
        Assert.Contains("WaitForTimedSupporterOverrideTransitionUsersAsync", disposeBody, StringComparison.Ordinal);
        Assert.Contains("timedSupporterOverrideTransitionGate.Dispose();", disposeBody, StringComparison.Ordinal);
        Assert.True(
            disposeBody.IndexOf("await StopCoreAsync()", StringComparison.Ordinal)
                < disposeBody.IndexOf("WaitForTimedSupporterOverrideTransitionUsersAsync", StringComparison.Ordinal));
        Assert.True(
            disposeBody.IndexOf("WaitForTimedSupporterOverrideTransitionUsersAsync", StringComparison.Ordinal)
                < disposeBody.IndexOf("timedSupporterOverrideTransitionGate.Dispose();", StringComparison.Ordinal));
    }

    [Fact]
    public async Task TimedSupporterOverrideTransition_RejectsNewAdmissionAfterStopBegins()
    {
        var coordinator = CreateCoordinator();
        Task<bool>? secondAdmission = null;
        Task? stopTask = null;
        var firstAdmission = false;

        try
        {
            var firstAdmissionTask = InvokePrivateTask<bool>(
                coordinator,
                "TryEnterTimedSupporterOverrideTransitionAsync",
                CancellationToken.None);
            firstAdmission = await firstAdmissionTask;
            Assert.True(firstAdmission);

            stopTask = coordinator.StopAsync();
            Assert.True(GetPrivateField<bool>(coordinator, "isStopping"));

            secondAdmission = InvokePrivateTask<bool>(
                coordinator,
                "TryEnterTimedSupporterOverrideTransitionAsync",
                CancellationToken.None);

            var completed = await Task.WhenAny(
                secondAdmission,
                Task.Delay(TimeSpan.FromSeconds(1)));
            Assert.Same(secondAdmission, completed);
            Assert.False(await secondAdmission);
        }
        finally
        {
            if (firstAdmission)
            {
                InvokePrivate(
                    coordinator,
                    "LeaveTimedSupporterOverrideTransitionUser",
                    true);
            }

            if (secondAdmission is not null)
            {
                var entered = await secondAdmission;
                if (entered)
                {
                    InvokePrivate(
                        coordinator,
                        "LeaveTimedSupporterOverrideTransitionUser",
                        true);
                }
            }

            if (stopTask is not null)
            {
                await stopTask.WaitAsync(TimeSpan.FromSeconds(5));
            }
        }
    }

    [Fact]
    public async Task TimedSupporterOverrideTransition_RejectsAdmissionQueuedBeforeStopBegins()
    {
        var coordinator = CreateCoordinator();
        Task<bool>? secondAdmission = null;
        var firstAdmission = false;

        try
        {
            firstAdmission = await InvokePrivateTask<bool>(
                coordinator,
                "TryEnterTimedSupporterOverrideTransitionAsync",
                CancellationToken.None);
            Assert.True(firstAdmission);

            secondAdmission = InvokePrivateTask<bool>(
                coordinator,
                "TryEnterTimedSupporterOverrideTransitionAsync",
                CancellationToken.None);
            Assert.False(secondAdmission.IsCompleted);

            InvokePrivate(coordinator, "MarkTimedSupporterOverrideTransitionStopping");
            InvokePrivate(
                coordinator,
                "LeaveTimedSupporterOverrideTransitionUser",
                true);
            firstAdmission = false;

            Assert.False(await secondAdmission);
        }
        finally
        {
            if (firstAdmission)
            {
                InvokePrivate(
                    coordinator,
                    "LeaveTimedSupporterOverrideTransitionUser",
                    true);
            }

            if (secondAdmission is not null)
            {
                var entered = await secondAdmission;
                if (entered)
                {
                    InvokePrivate(
                        coordinator,
                        "LeaveTimedSupporterOverrideTransitionUser",
                        true);
                }
            }
        }
    }

    [Fact]
    public void QueuedAvatarSwitchDrain_SerializesHandoffAndReleasesBeforeDelay()
    {
        var source = File.ReadAllText(FindSourceFile(
            "VrcTwitchOscBridge",
            "Services",
            "BridgeCoordinator.cs"));
        var drainBody = GetMethodBody(source, "private void EnsureQueuedAvatarSwitchDrain");
        var transitionEntryIndex = drainBody.IndexOf(
            "TryEnterTimedSupporterOverrideTransitionAsync",
            StringComparison.Ordinal);
        var supporterStateCheckIndex = drainBody.IndexOf(
            "IsSupporterOverrideSequenceActiveLocked",
            StringComparison.Ordinal);
        var dequeueIndex = drainBody.LastIndexOf(
            "queuedAvatarSwitches.Dequeue()",
            StringComparison.Ordinal);
        var executionIndex = drainBody.IndexOf(
            "ExecuteQueuedAvatarSwitchAsync",
            StringComparison.Ordinal);
        var transitionLeaveIndex = drainBody.IndexOf(
            "LeaveTimedSupporterOverrideTransitionUser",
            StringComparison.Ordinal);
        var delayIndex = drainBody.IndexOf(
            "await Task.Delay(delay, cancellationToken)",
            StringComparison.Ordinal);

        Assert.True(transitionEntryIndex >= 0, "The queued avatar-switch drain must enter the timed transition gate.");
        Assert.True(transitionEntryIndex < supporterStateCheckIndex);
        Assert.True(supporterStateCheckIndex < dequeueIndex);
        Assert.True(dequeueIndex < executionIndex);
        Assert.True(executionIndex < transitionLeaveIndex);
        Assert.True(
            transitionLeaveIndex < delayIndex,
            "The timed transition gate must be released before the queued switch duration delay.");
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

    private static bool InvokeChatCommandMatcher(
        BridgeRuntimeConfiguration configuration,
        TriggerRuleSnapshot snapshot,
        string messageText,
        bool userIsModerator,
        bool userIsBroadcaster)
    {
        var runtimeRuleIndexType = typeof(BridgeCoordinator).GetNestedType(
            "RuntimeRuleIndex",
            BindingFlags.NonPublic);
        Assert.NotNull(runtimeRuleIndexType);
        var createMethod = runtimeRuleIndexType.GetMethod(
            "Create",
            BindingFlags.Public | BindingFlags.Static);
        Assert.NotNull(createMethod);
        var runtimeRuleIndex = createMethod.Invoke(
            null,
            new object[] { new[] { snapshot } });
        Assert.NotNull(runtimeRuleIndex);

        var bridgeEventType = typeof(BridgeCoordinator).GetNestedType(
            "BridgeIncomingEvent",
            BindingFlags.NonPublic);
        Assert.NotNull(bridgeEventType);
        var bridgeEventConstructor = Assert.Single(
            bridgeEventType.GetConstructors(
                BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic),
            candidate => candidate.GetParameters().Length == 13);
        var bridgeEvent = bridgeEventConstructor.Invoke(new object?[]
        {
            TwitchTriggerType.ChatCommand,
            "Viewer",
            0,
            null,
            null,
            "Chat command",
            true,
            messageText,
            "viewer-id",
            "viewer",
            Array.Empty<string>(),
            userIsModerator,
            userIsBroadcaster
        });

        var matchMethod = typeof(BridgeCoordinator).GetMethod(
            "SelectMatchingChatCommandRules",
            BindingFlags.NonPublic | BindingFlags.Static);
        Assert.NotNull(matchMethod);
        var matches = matchMethod.Invoke(null, new object?[]
        {
            configuration,
            runtimeRuleIndex,
            bridgeEvent,
            "avtr_return",
            false,
            Array.Empty<Guid>(),
            null
        });
        return Assert.IsAssignableFrom<IEnumerable<TriggerRuleSnapshot>>(matches)
            .Any(candidate => candidate.Id == snapshot.Id);
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

    private static void SetPrivateField(object target, string fieldName, object value)
    {
        var field = typeof(BridgeCoordinator).GetField(fieldName, BindingFlags.NonPublic | BindingFlags.Instance);
        Assert.NotNull(field);
        field!.SetValue(target, value);
    }

    private static object? InvokePrivate(
        object target,
        string methodName,
        params object?[] arguments)
    {
        var method = target.GetType().GetMethod(
            methodName,
            BindingFlags.NonPublic | BindingFlags.Instance);
        Assert.NotNull(method);
        return method!.Invoke(target, arguments);
    }

    private static async Task<T> InvokePrivateTask<T>(
        object target,
        string methodName,
        params object?[] arguments)
    {
        var invocation = InvokePrivate(target, methodName, arguments);
        var task = Assert.IsAssignableFrom<Task<T>>(invocation);
        return await task;
    }

    private static T GetPrivateField<T>(object target, string fieldName)
    {
        var field = target.GetType().GetField(fieldName, BindingFlags.NonPublic | BindingFlags.Instance);
        Assert.NotNull(field);
        return Assert.IsType<T>(field!.GetValue(target));
    }

    private static BridgeCoordinator CreateCoordinator(IActivityResumeService? activityResumeService = null) => new(
        new DesktopInputLockService(Dispatcher.CurrentDispatcher),
        new WorldCommandBlacklistService(),
        new VrChatLocalOscCacheService(),
        activityResumeService);

    private static UdpClient ConfigureCoordinatorForAvatarSwapExecution(
        BridgeCoordinator coordinator,
        BridgeRuntimeConfiguration configuration,
        string currentAvatarId)
    {
        InvokePrivate(coordinator, "SetActiveConfiguration", configuration);
        SetPrivateField(coordinator, "currentVrChatAvatarId", currentAvatarId);

        var routerField = typeof(BridgeCoordinator).GetField(
            "oscRouterService",
            BindingFlags.NonPublic | BindingFlags.Instance);
        Assert.NotNull(routerField);
        var router = routerField!.GetValue(coordinator);
        Assert.NotNull(router);

        var routerType = router!.GetType();
        var targetType = routerType.GetNestedType("DiscoveredOscTarget", BindingFlags.NonPublic);
        Assert.NotNull(targetType);
        var target = Activator.CreateInstance(
            targetType!,
            BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic,
            binder: null,
            args: ["Test VRChat", IPAddress.Loopback, 9001, 9002],
            culture: null);
        Assert.NotNull(target);

        routerType.GetField("activeVrChatTarget", BindingFlags.NonPublic | BindingFlags.Instance)!
            .SetValue(router, target);
        var sendClient = new UdpClient();
        routerType.GetField("sendClient", BindingFlags.NonPublic | BindingFlags.Instance)!
            .SetValue(router, sendClient);
        return sendClient;
    }

    private static object CreateUniversalIncomingEvent(
        UniversalTriggerType triggerType,
        string userDisplayName,
        string userId,
        string userLogin)
    {
        var incomingEventType = typeof(BridgeCoordinator).GetNestedType(
            "UniversalIncomingEvent",
            BindingFlags.NonPublic);
        Assert.NotNull(incomingEventType);
        var constructor = Assert.Single(
            incomingEventType!.GetConstructors(
                BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic),
            candidate => candidate.GetParameters().Length == 13);

        return constructor.Invoke(new object?[]
        {
            triggerType,
            userDisplayName,
            userId,
            userLogin,
            1,
            null,
            null,
            string.Empty,
            string.Empty,
            0,
            Array.Empty<string>(),
            false,
            false
        });
    }

    private static async Task InvokePrivateAsync(
        object target,
        string methodName,
        params object?[] arguments)
    {
        var invocation = InvokePrivate(target, methodName, arguments);
        var task = Assert.IsAssignableFrom<Task>(invocation);
        await task;
    }

    private static IReadOnlyList<string> InvokePrivateStringList(
        string methodName,
        params object[] arguments)
    {
        var method = typeof(BridgeCoordinator).GetMethod(
            methodName,
            BindingFlags.NonPublic | BindingFlags.Static);
        Assert.NotNull(method);

        var result = method!.Invoke(null, arguments);
        return Assert.IsAssignableFrom<IEnumerable<string>>(result).ToArray();
    }

    private static string GetMethodBody(string source, string methodSignatureStart)
    {
        var methodStart = source.IndexOf(methodSignatureStart, StringComparison.Ordinal);
        Assert.True(methodStart >= 0, $"Could not find method signature starting with '{methodSignatureStart}'.");

        var bodyStart = source.IndexOf('{', methodStart);
        Assert.True(bodyStart >= 0, $"Could not find method body for '{methodSignatureStart}'.");

        var depth = 0;
        for (var index = bodyStart; index < source.Length; index++)
        {
            if (source[index] == '{')
            {
                depth++;
            }
            else if (source[index] == '}')
            {
                depth--;
                if (depth == 0)
                {
                    return source.Substring(bodyStart, index - bodyStart + 1);
                }
            }
        }

        throw new InvalidOperationException($"Could not find method body end for '{methodSignatureStart}'.");
    }

    private static string FindSourceFile(params string[] relativeParts)
    {
        var directory = new DirectoryInfo(AppContext.BaseDirectory);
        while (directory is not null)
        {
            var candidate = Path.Combine(new[] { directory.FullName }.Concat(relativeParts).ToArray());
            if (File.Exists(candidate))
            {
                return candidate;
            }

            directory = directory.Parent;
        }

        throw new FileNotFoundException("Could not find source file.", Path.Combine(relativeParts));
    }

    private sealed class NoOpActivityResumeService : IActivityResumeService
    {
        public Task LoadPendingAsync() => Task.CompletedTask;

        public bool HasPendingResume => false;

        public bool IsPendingForAvatar(string avatarId) => false;

        public IReadOnlyList<ResumeActivity> GetPendingActivities() => [];

        public Task RemoveExpiredActivitiesAsync() => Task.CompletedTask;

        public Task RecordActivityStartedAsync(ResumeActivity activity, string avatarId) => Task.CompletedTask;

        public Task RecordActivityEndedAsync(Guid ruleId, ResumeActivity? expectedActivity = null) => Task.CompletedTask;

        public Task RemoveActivityAsync(ResumeActivity activity) => Task.CompletedTask;

        public Task ClearAllAsync() => Task.CompletedTask;

        public Task CommitAsync() => Task.CompletedTask;

        public Task DeleteStaleFileIfPresentAsync() => Task.CompletedTask;
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
