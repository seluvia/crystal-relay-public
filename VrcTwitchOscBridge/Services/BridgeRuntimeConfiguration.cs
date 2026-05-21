using VrcTwitchOscBridge.Models;

namespace VrcTwitchOscBridge.Services;

public sealed record TwitchAccountSnapshot(
    string AccessToken,
    string RefreshToken,
    string UserId,
    string Login,
    string DisplayName,
    string ProfileImageUrl,
    DateTimeOffset? AccessTokenExpiresAt,
    DateTimeOffset? SessionRenewalDueAt,
    IReadOnlyList<string> Scopes);

public sealed record VrChatSessionSnapshot(
    string AuthCookie,
    string UserId,
    string DisplayName)
{
    public static VrChatSessionSnapshot Empty { get; } = new(string.Empty, string.Empty, string.Empty);

    public bool IsConnected => !string.IsNullOrWhiteSpace(AuthCookie) && !string.IsNullOrWhiteSpace(UserId);
}

public sealed record SetTriggerActionSnapshot(
    Guid Id,
    string ParameterName,
    OscParameterType ParameterType,
    string ParameterValue);

public sealed record TriggerRuleSnapshot(
    Guid Id,
    bool IsEnabled,
    string Name,
    bool IsGlobalOverride,
    Guid AvatarProfileId,
    string AvatarProfileName,
    string RequiredAvatarId,
    string RequiredAvatarName,
    Guid SupporterAvatarProfileId,
    string SupporterAvatarId,
    string SupporterAvatarName,
    bool BelongsToMasterAvatarProfile,
    TwitchTriggerType TriggerType,
    string ChannelPointRewardId,
    string ChannelPointRewardTitle,
    string ManagedRewardReadyColor,
    string ManagedRewardCooldownColor,
    bool ChatCommandEnabled,
    string ChatCommandText,
    ChatCommandPermission ChatCommandPermission,
    int MinimumAmount,
    bool AmountScaledDurationEnabled,
    int AmountUnitsPerDuration,
    int SecondsPerAmountUnit,
    int BitsAmountUnitsPerDuration,
    int BitsSecondsPerAmountUnit,
    int SubscriptionsAmountUnitsPerDuration,
    int SubscriptionsSecondsPerAmountUnit,
    int SubscriptionTier1SecondsPerSub,
    int SubscriptionTier2SecondsPerSub,
    int SubscriptionTier3SecondsPerSub,
    bool MaxAccumulatedDurationEnabled,
    int MaxAccumulatedDurationSeconds,
    OscActionType ActionType,
    PlayerMovementDirection MovementDirection,
    string ParameterName,
    OscParameterType ParameterType,
    IntZeroDurationMode IntZeroDurationMode,
    string ParameterValue,
    FloatValueMode FloatValueMode,
    double FloatTransitionSeconds,
    string ResetValue,
    bool ActiveFloatBoostRewardEnabled,
    string ActiveFloatBoostRewardId,
    string ActiveFloatBoostRewardTitle,
    string ActiveFloatBoostRewardDescription,
    int ActiveFloatBoostRewardCost,
    int ActiveFloatBoostRewardCooldownSeconds,
    string ActiveFloatBoostRewardReadyColor,
    string ActiveFloatBoostRewardCooldownColor,
    string ActiveFloatBoostAddValue,
    string ActiveFloatBoostMinimumValue,
    string ActiveFloatBoostMaximumValue,
    string AvatarChangeTargetId,
    string AvatarChangeResetId,
    string AvatarTargetName,
    string ResetAvatarName,
    IReadOnlyList<string> AvatarRouletAvatarIds,
    IReadOnlyList<string> AvatarRouletAvatarNames,
    int RangeMinimum,
    int RangeMaximum,
    int DurationSeconds,
    int CooldownSeconds,
    bool UsesLinkedChannelPointReward,
    int? BotMessageCooldownSeconds,
    bool SharedRewardChoiceEnabled,
    int SharedRewardChoiceNumber,
    string SharedRewardHelpText,
    string SupporterKeywordText,
    IReadOnlyList<SetTriggerActionSnapshot> SetTriggerActions,
    IReadOnlyList<Guid> TemporarilyDisabledRuleIds,
    string BotMessageTemplate);

public sealed record UniversalTriggerActionSnapshot(
    Guid Id,
    string OscAddress,
    UniversalTriggerValueKind ValueKind,
    string TargetValue,
    string DefaultValue,
    double DurationSeconds,
    bool AddToQueue,
    string ImportGroupKey);

public sealed record UniversalTriggerRuleSnapshot(
    Guid Id,
    bool IsEnabled,
    string Name,
    UniversalTriggerType TriggerType,
    bool ChatCommandEnabled,
    string CommandText,
    ChatCommandPermission ChatCommandPermission,
    string RewardId,
    string RewardTitle,
    int MinimumBits,
    int MaximumBits,
    string SubscriptionTier,
    int MinimumMonths,
    int MaximumMonths,
    int GlobalDelaySeconds,
    int UserDelaySeconds,
    bool ExecuteRandomAction,
    string ImportSource,
    IReadOnlyList<UniversalTriggerActionSnapshot> Actions);

public sealed record AvatarScaleBitGrowthRangeSnapshot(
    int MinimumBits,
    int MaximumBits,
    double HeightAddedMeters);

public sealed record AvatarScaleMasterRewardSnapshot(
    bool IsEnabled,
    string RewardId,
    string RewardTitle,
    int UnlockDurationSeconds,
    int CooldownSeconds,
    bool PreventAvatarChangesDuringActiveScaling);

public sealed record AvatarScaleRuleSnapshot(
    Guid Id,
    bool IsEnabled,
    string Name,
    AvatarScaleTriggerType TriggerType,
    bool ChatCommandEnabled,
    string CommandText,
    ChatCommandPermission ChatCommandPermission,
    string RewardId,
    string RewardTitle,
    int MinimumBits,
    int MaximumBits,
    string SubscriptionTier,
    int MinimumMonths,
    int MaximumMonths,
    int CooldownSeconds,
    IReadOnlyList<Guid> TemporarilyDisabledRuleIds,
    AvatarScaleMode ScaleMode,
    double TargetHeightMeters,
    double MinimumHeightMeters,
    double MaximumHeightMeters,
    double RelativeHeightMeters,
    double RelativeMinimumHeightMeters,
    double RelativeMaximumHeightMeters,
    double HeightMultiplier,
    AvatarScalePreset Preset,
    double ActiveTimeSeconds,
    AvatarScaleRestoreMode RestoreMode,
    double RestoreHeightMeters,
    double SmoothTransitionSeconds,
    bool AdvancedRangeEnabled,
    bool BypassVrChatScaleLimits,
    double SupporterGrowthNormalHeightMeters,
    double SupporterGrowthMaxAddedHeightMeters,
    int SupporterGrowthInactivityTimerSeconds,
    bool SupporterGrowthAllowRewardScaleOverlay,
    int SupporterGrowthBitsTimerUnit,
    int SupporterGrowthSecondsPerBitsUnit,
    int SupporterGrowthTier1Seconds,
    int SupporterGrowthTier2Seconds,
    int SupporterGrowthTier3Seconds,
    int SupporterGrowthSoftCapSeconds,
    int SupporterGrowthSoftCapMultiplierPercent,
    int SupporterGrowthMaxPaidTimeSeconds,
    string SupporterGrowthGrowKeyword,
    string SupporterGrowthShrinkKeyword,
    double SupporterGrowthTier1HeightMeters,
    double SupporterGrowthTier2HeightMeters,
    double SupporterGrowthTier3HeightMeters,
    IReadOnlyList<AvatarScaleBitGrowthRangeSnapshot> SupporterGrowthBitRanges);

public sealed record CashPaymentConnectionSnapshot(
    bool StreamElementsEnabled,
    string StreamElementsAccountId,
    string StreamElementsJwtToken,
    bool StreamlabsEnabled,
    string StreamlabsAccessToken,
    bool KoFiEnabled,
    KoFiConnectionMode KoFiConnectionMode,
    string KoFiRelayBaseUrl,
    string KoFiRelayChannelId,
    string KoFiRelayClientSecret,
    int KoFiLocalPort,
    string KoFiWebhookPath,
    string KoFiVerificationToken);

public sealed record CashPaymentRuleSnapshot(
    Guid Id,
    bool IsEnabled,
    string Name,
    CashPaymentProvider Provider,
    decimal MinimumAmount,
    decimal MaximumAmount,
    string CurrencyCode,
    string MessageContains,
    int CooldownSeconds,
    CashPaymentActionKind ActionKind,
    TriggerRuleSnapshot? TriggerAction,
    AvatarScaleRuleSnapshot? ScaleAction);

public sealed record BridgeRuntimeConfiguration(
    string TwitchClientId,
    TwitchAccountSnapshot Broadcaster,
    TwitchAccountSnapshot? Bot,
    string CurrentVrChatAvatarId,
    string SharedReturnAvatarId,
    string SharedReturnAvatarName,
    VrChatSessionSnapshot VrChatSession,
    bool ChatboxOscEnabled,
    int ChatboxOscDelaySeconds,
    bool UseBroadcasterAsBotSender,
    bool SupporterOverrideInfoMessageEnabled,
    bool TriggerInfoAnnouncementsEnabled,
    int TriggerInfoAnnouncementIntervalMinutes,
    bool TriggerInfoCommandEnabled,
    string TriggerInfoCommandText,
    int TriggerInfoCommandCooldownSeconds,
    ChatCommandPermission TriggerInfoCommandPermission,
    bool WorldCommandEnabled,
    string WorldCommandText,
    int WorldCommandCooldownSeconds,
    ChatCommandPermission WorldCommandPermission,
    bool ChannelPointRewardTestModeEnabled,
    bool AvatarChangeCooldownOnlyModeEnabled,
    bool EmergencyRedeemStopEnabled,
    bool DesktopModeInputLockEnabled,
    AvatarScaleMasterRewardSnapshot AvatarScaleMasterReward,
    CashPaymentConnectionSnapshot CashPayments,
    IReadOnlyList<TriggerRuleSnapshot> Rules,
    IReadOnlyList<UniversalTriggerRuleSnapshot> UniversalTriggers,
    IReadOnlyList<AvatarScaleRuleSnapshot> AvatarScaleRules,
    IReadOnlyList<CashPaymentRuleSnapshot> CashPaymentRules)
{
    public static BridgeRuntimeConfiguration FromSettings(
        AppSettings settings,
        RuntimeConfig runtimeConfig,
        IReadOnlyDictionary<string, int>? linkedRewardCooldownSecondsById = null)
    {
        var rules = new List<TriggerRuleSnapshot>();
        var universalTriggers = new List<UniversalTriggerRuleSnapshot>();
        var avatarScaleRules = new List<AvatarScaleRuleSnapshot>();
        var cashPaymentRules = new List<CashPaymentRuleSnapshot>();
        var masterProfile = settings.AvatarProfiles.FirstOrDefault(profile => profile.IsMasterProfile);

        foreach (var profile in settings.AvatarProfiles)
        {
            foreach (var rule in profile.ChannelPointRules)
            {
                if (TryToSnapshot(rule, isGlobalOverride: false, profile, linkedRewardCooldownSecondsById, out var snapshot))
                {
                    rules.Add(snapshot);
                }
            }
        }

        foreach (var rule in settings.GlobalOverrideRules)
        {
            var supporterProfile = rule.SupporterAvatarProfileId == Guid.Empty
                ? null
                : settings.AvatarProfiles.FirstOrDefault(profile => profile.Id == rule.SupporterAvatarProfileId);
            if (TryToSnapshot(rule, isGlobalOverride: true, supporterProfile, linkedRewardCooldownSecondsById, out var snapshot))
            {
                rules.Add(snapshot);
            }
        }

        var movementRules = settings.MovementRedeemSets.Count > 0
            ? settings.MovementRedeemSets.SelectMany(set => set.MovementRules)
            : settings.GlobalMovementRules;
        foreach (var rule in movementRules)
        {
            if (TryToSnapshot(rule, isGlobalOverride: true, profile: null, linkedRewardCooldownSecondsById, out var snapshot))
            {
                rules.Add(snapshot);
            }
        }

        foreach (var trigger in settings.UniversalTriggers)
        {
            if (TryToUniversalSnapshot(trigger, requireTriggerFilter: true, out var snapshot))
            {
                universalTriggers.Add(snapshot);
            }
        }

        var configuredScaleRules = settings.AvatarScaleSets.Count > 0
            ? settings.AvatarScaleSets.SelectMany(set => set.ScaleRules)
            : settings.AvatarScaleRules;
        foreach (var scaleRule in configuredScaleRules)
        {
            if (TryToAvatarScaleSnapshot(scaleRule, requireTriggerFilter: true, out var snapshot))
            {
                avatarScaleRules.Add(snapshot);
            }
        }

        foreach (var cashRule in settings.CashPaymentRules)
        {
            if (TryToCashPaymentSnapshot(cashRule, out var snapshot))
            {
                cashPaymentRules.Add(snapshot);
            }
        }

        return new BridgeRuntimeConfiguration(
            runtimeConfig.TwitchClientId.Trim(),
            ToSnapshot(settings.Broadcaster),
            settings.Bot.IsConnected ? ToSnapshot(settings.Bot) : null,
            settings.VrChat.CurrentAvatarId.Trim(),
            masterProfile?.AvatarId.Trim() ?? string.Empty,
            masterProfile?.AvatarName.Trim() ?? string.Empty,
            settings.VrChat.IsConnected
                ? new VrChatSessionSnapshot(
                    settings.VrChat.AuthCookie,
                    settings.VrChat.UserId,
                    settings.VrChat.DisplayName)
                : VrChatSessionSnapshot.Empty,
            settings.ChatboxOscEnabled,
            settings.ChatboxOscDelaySeconds,
            settings.UseBroadcasterAsBotSender,
            settings.SupporterOverrideInfoMessageEnabled,
            settings.TriggerInfoAnnouncementsEnabled,
            Math.Max(1, settings.TriggerInfoAnnouncementIntervalMinutes),
            settings.TriggerInfoCommandEnabled,
            ChatCommandUtility.Normalize(settings.TriggerInfoCommandText),
            Math.Max(0, settings.TriggerInfoCommandCooldownSeconds),
            Enum.IsDefined(settings.TriggerInfoCommandPermission)
                ? settings.TriggerInfoCommandPermission
                : ChatCommandPermission.Everyone,
            settings.WorldCommandEnabled,
            ChatCommandUtility.Normalize(settings.WorldCommandText),
            Math.Max(0, settings.WorldCommandCooldownSeconds),
            Enum.IsDefined(settings.WorldCommandPermission)
                ? settings.WorldCommandPermission
                : ChatCommandPermission.Everyone,
            settings.ChannelPointRewardTestModeEnabled,
            settings.AvatarChangeCooldownOnlyModeEnabled,
            settings.EmergencyRedeemStopEnabled,
            settings.DesktopModeInputLockEnabled,
            ToAvatarScaleMasterRewardSnapshot(settings.AvatarScaleMasterReward),
            ToCashPaymentConnectionSnapshot(settings.CashPayments),
            rules.ToArray(),
            universalTriggers.ToArray(),
            avatarScaleRules.ToArray(),
            cashPaymentRules.ToArray());
    }

    public static TwitchAccountSnapshot ToSnapshot(TwitchAccountSettings settings)
    {
        return new TwitchAccountSnapshot(
            settings.AccessToken,
            settings.RefreshToken,
            settings.UserId,
            settings.Login,
            settings.DisplayName,
            settings.ProfileImageUrl,
            settings.AccessTokenExpiresAt,
            settings.SessionRenewalDueAt,
            [.. settings.Scopes]);
    }

    public static TwitchAccountSettings ToSettings(TwitchAccountSnapshot snapshot)
    {
        return new TwitchAccountSettings
        {
            AccessToken = snapshot.AccessToken,
            RefreshToken = snapshot.RefreshToken,
            UserId = snapshot.UserId,
            Login = snapshot.Login,
            DisplayName = snapshot.DisplayName,
            ProfileImageUrl = snapshot.ProfileImageUrl,
            AccessTokenExpiresAt = snapshot.AccessTokenExpiresAt,
            SessionRenewalDueAt = snapshot.SessionRenewalDueAt,
            Scopes = [.. snapshot.Scopes]
        };
    }

    public static TriggerRuleSnapshot CreateManualTestSnapshot(
        TriggerRule rule,
        bool isGlobalOverride,
        AvatarTriggerProfile? profile)
    {
        if (!IsManualTestReady(rule))
        {
            throw new InvalidOperationException(GetManualTestReadinessError(rule));
        }

        return CreateSnapshot(rule, isGlobalOverride, profile, linkedRewardCooldownSecondsById: null);
    }

    public static UniversalTriggerRuleSnapshot CreateManualTestSnapshot(UniversalTriggerRule rule)
    {
        if (!TryToUniversalSnapshot(rule, requireTriggerFilter: false, out var snapshot))
        {
            throw new InvalidOperationException("Add at least one complete OSC action before testing this universal trigger.");
        }

        return snapshot;
    }

    public static AvatarScaleRuleSnapshot CreateManualTestSnapshot(AvatarScaleRule rule)
    {
        if (!TryToAvatarScaleSnapshot(rule, requireTriggerFilter: false, out var snapshot))
        {
            throw new InvalidOperationException("Finish the avatar scale setup before testing this scale redeem.");
        }

        return snapshot;
    }

    public static CashPaymentRuleSnapshot CreateManualTestSnapshot(CashPaymentRule rule)
    {
        if (!TryToCashPaymentSnapshot(rule, out var snapshot))
        {
            throw new InvalidOperationException("Finish the cash payment rule before testing it.");
        }

        return snapshot;
    }

    private static bool TryToCashPaymentSnapshot(CashPaymentRule rule, out CashPaymentRuleSnapshot snapshot)
    {
        snapshot = default!;
        TriggerRuleSnapshot? triggerAction = null;
        AvatarScaleRuleSnapshot? scaleAction = null;

        if (rule.ActionKind == CashPaymentActionKind.AvatarScaling)
        {
            if (!TryToAvatarScaleSnapshot(rule.ScaleAction, requireTriggerFilter: false, out var scaleSnapshot))
            {
                return false;
            }

            scaleAction = scaleSnapshot with
            {
                Id = rule.Id,
                IsEnabled = rule.IsEnabled && scaleSnapshot.IsEnabled,
                Name = rule.DisplayTitle,
                TriggerType = AvatarScaleTriggerType.Bits,
                RewardId = string.Empty,
                RewardTitle = string.Empty,
                CooldownSeconds = Math.Max(0, rule.CooldownSeconds)
            };
        }
        else
        {
            if (!IsManualTestReady(rule.TriggerAction))
            {
                return false;
            }

            var triggerSnapshot = CreateSnapshot(
                rule.TriggerAction,
                isGlobalOverride: true,
                profile: null,
                linkedRewardCooldownSecondsById: null);
            triggerAction = triggerSnapshot with
            {
                Id = rule.Id,
                IsEnabled = rule.IsEnabled && triggerSnapshot.IsEnabled,
                Name = rule.DisplayTitle,
                TriggerType = TwitchTriggerType.Bits,
                ChannelPointRewardId = string.Empty,
                ChannelPointRewardTitle = string.Empty,
                ChatCommandEnabled = false,
                ChatCommandText = string.Empty,
                MinimumAmount = 1,
                CooldownSeconds = Math.Max(0, rule.CooldownSeconds),
                UsesLinkedChannelPointReward = false,
                BotMessageCooldownSeconds = Math.Max(0, rule.CooldownSeconds)
            };
        }

        snapshot = new CashPaymentRuleSnapshot(
            rule.Id,
            rule.IsEnabled,
            rule.DisplayTitle,
            rule.Provider,
            Math.Max(0m, rule.MinimumAmount),
            Math.Max(0m, rule.MaximumAmount),
            rule.CurrencyCode.Trim().ToUpperInvariant(),
            rule.MessageContains.Trim(),
            Math.Max(0, rule.CooldownSeconds),
            rule.ActionKind,
            triggerAction,
            scaleAction);
        return true;
    }

    private static bool TryToSnapshot(
        TriggerRule rule,
        bool isGlobalOverride,
        AvatarTriggerProfile? profile,
        IReadOnlyDictionary<string, int>? linkedRewardCooldownSecondsById,
        out TriggerRuleSnapshot snapshot)
    {
        snapshot = default!;

        if (!IsLiveRuntimeReady(rule, profile, isGlobalOverride))
        {
            return false;
        }

        snapshot = CreateSnapshot(rule, isGlobalOverride, profile, linkedRewardCooldownSecondsById);
        return true;
    }

    private static TriggerRuleSnapshot CreateSnapshot(
        TriggerRule rule,
        bool isGlobalOverride,
        AvatarTriggerProfile? profile,
        IReadOnlyDictionary<string, int>? linkedRewardCooldownSecondsById)
    {
        var usesSetTriggerMasterReward = !isGlobalOverride
            && rule.ActionType == OscActionType.SetTrigger
            && profile is not null;
        var channelPointRewardId = usesSetTriggerMasterReward
            ? profile!.SetTriggerMasterRewardId.Trim()
            : rule.ChannelPointRewardId.Trim();
        var channelPointRewardTitle = usesSetTriggerMasterReward
            ? profile!.SetTriggerMasterRewardTitle.Trim()
            : rule.ChannelPointRewardTitle.Trim();
        var rewardSyncMode = usesSetTriggerMasterReward
            ? profile!.SetTriggerMasterRewardSyncMode
            : rule.RewardSyncMode;
        var readyColor = usesSetTriggerMasterReward
            ? profile!.SetTriggerMasterRewardReadyColor
            : rule.ManagedRewardReadyColor;
        var cooldownColor = usesSetTriggerMasterReward
            ? profile!.SetTriggerMasterRewardCooldownColor
            : rule.ManagedRewardCooldownColor;
        var configuredCooldownSeconds = usesSetTriggerMasterReward
            ? profile!.SetTriggerMasterRewardCooldownSeconds
            : rule.CooldownSeconds;
        var usesLinkedChannelPointReward = rule.TriggerType == TwitchTriggerType.ChannelPoints
            && rewardSyncMode == TwitchRewardSyncMode.LinkExisting;
        var cooldownSeconds = usesLinkedChannelPointReward
            ? 0
            : Math.Max(0, configuredCooldownSeconds);
        var botMessageCooldownSeconds = usesLinkedChannelPointReward
            ? GetLinkedRewardBotCooldownSeconds(linkedRewardCooldownSecondsById, channelPointRewardId)
            : cooldownSeconds;
        var supporterAvatarId = isGlobalOverride ? rule.SupporterAvatarId.Trim() : string.Empty;
        var supporterAvatarName = isGlobalOverride ? rule.SupporterAvatarName.Trim() : string.Empty;
        if (isGlobalOverride
            && string.IsNullOrWhiteSpace(supporterAvatarId)
            && profile is not null
            && !string.IsNullOrWhiteSpace(profile.AvatarId))
        {
            supporterAvatarId = profile.AvatarId.Trim();
            supporterAvatarName = profile.AvatarName.Trim();
        }

        var avatarScopedSupporterRule = isGlobalOverride
            && rule.TriggerType is TwitchTriggerType.Bits or TwitchTriggerType.Subscriptions
            && rule.ActionType is not (OscActionType.AvatarChange or OscActionType.AvatarRoulet);
        var requiredAvatarId = avatarScopedSupporterRule
            ? supporterAvatarId
            : profile?.AvatarId.Trim() ?? string.Empty;
        var requiredAvatarName = avatarScopedSupporterRule
            ? supporterAvatarName
            : profile?.AvatarName.Trim() ?? string.Empty;

        return new TriggerRuleSnapshot(
            rule.Id,
            (profile?.IsEnabled ?? true) && rule.IsEnabled,
            rule.DisplayTitle,
            isGlobalOverride,
            profile?.Id ?? Guid.Empty,
            profile?.Name.Trim() ?? string.Empty,
            requiredAvatarId,
            requiredAvatarName,
            isGlobalOverride ? rule.SupporterAvatarProfileId : Guid.Empty,
            supporterAvatarId,
            supporterAvatarName,
            profile?.IsMasterProfile ?? false,
            rule.TriggerType,
            channelPointRewardId,
            channelPointRewardTitle,
            ManagedRewardPresentation.NormalizeReadyBackgroundColor(readyColor),
            ManagedRewardPresentation.NormalizeCooldownBackgroundColor(cooldownColor),
            rule.ChatCommandEnabled,
            ChatCommandUtility.Normalize(rule.ChatCommandText),
            rule.ChatCommandPermission,
            rule.MinimumAmount,
            rule.AmountScaledDurationEnabled,
            rule.AmountUnitsPerDuration,
            rule.SecondsPerAmountUnit,
            rule.BitsAmountUnitsPerDuration,
            rule.BitsSecondsPerAmountUnit,
            rule.SubscriptionsAmountUnitsPerDuration,
            rule.SubscriptionsSecondsPerAmountUnit,
            rule.SubscriptionTier1SecondsPerSub,
            rule.SubscriptionTier2SecondsPerSub,
            rule.SubscriptionTier3SecondsPerSub,
            rule.MaxAccumulatedDurationEnabled,
            rule.MaxAccumulatedDurationSeconds,
            rule.ActionType,
            rule.MovementDirection,
            rule.ParameterName.Trim(),
            rule.ParameterType,
            rule.IntZeroDurationMode,
            rule.ParameterValue.Trim(),
            Enum.IsDefined(rule.FloatValueMode) ? rule.FloatValueMode : FloatValueMode.Decimal,
            Math.Clamp(rule.FloatTransitionSeconds, 0, 30),
            rule.ResetValue.Trim(),
            rule.ActiveFloatBoostRewardEnabled,
            rule.ActiveFloatBoostRewardId.Trim(),
            rule.ActiveFloatBoostRewardTitle.Trim(),
            rule.ActiveFloatBoostRewardDescription.Trim(),
            Math.Max(1, rule.ActiveFloatBoostRewardCost),
            Math.Max(0, rule.ActiveFloatBoostRewardCooldownSeconds),
            ManagedRewardPresentation.NormalizeReadyBackgroundColor(rule.ActiveFloatBoostRewardReadyColor),
            ManagedRewardPresentation.NormalizeCooldownBackgroundColor(rule.ActiveFloatBoostRewardCooldownColor),
            rule.ActiveFloatBoostAddValue.Trim(),
            rule.ActiveFloatBoostMinimumValue.Trim(),
            rule.ActiveFloatBoostMaximumValue.Trim(),
            rule.AvatarChangeTargetId.Trim(),
            rule.AvatarChangeResetId.Trim(),
            rule.AvatarTargetName.Trim(),
            rule.ResetAvatarName.Trim(),
            [.. rule.AvatarRouletAvatarIds.Select(avatarId => avatarId?.Trim() ?? string.Empty).Where(avatarId => !string.IsNullOrWhiteSpace(avatarId)).Distinct(StringComparer.Ordinal)],
            [.. rule.AvatarRouletAvatarNames.Select(avatarName => avatarName?.Trim() ?? string.Empty)],
            rule.RangeMinimum,
            rule.RangeMaximum,
            rule.DurationSeconds,
            cooldownSeconds,
            usesLinkedChannelPointReward,
            botMessageCooldownSeconds,
            rule.SharedRewardChoiceEnabled,
            Math.Max(0, rule.SharedRewardChoiceNumber),
            rule.SharedRewardHelpText.Trim(),
            rule.SupporterKeywordText.Trim(),
            [.. rule.SetTriggerActions.Select(ToSetTriggerActionSnapshot).Where(action => HasAvatarParameterPath(action.ParameterName) && !string.IsNullOrWhiteSpace(action.ParameterValue))],
            [.. rule.TemporarilyDisabledRuleIds.Where(ruleId => ruleId != Guid.Empty).Distinct()],
            rule.BotMessageTemplate.Trim());
    }

    private static SetTriggerActionSnapshot ToSetTriggerActionSnapshot(SetTriggerAction action)
    {
        var normalizedType = action.ParameterType is OscParameterType.Bool or OscParameterType.Int or OscParameterType.Float
            ? action.ParameterType
            : OscParameterType.Int;
        return new SetTriggerActionSnapshot(
            action.Id == Guid.Empty ? Guid.NewGuid() : action.Id,
            action.ParameterName.Trim(),
            normalizedType,
            action.ParameterValue.Trim());
    }

    private static int? GetLinkedRewardBotCooldownSeconds(
        IReadOnlyDictionary<string, int>? linkedRewardCooldownSecondsById,
        string rewardId)
    {
        var normalizedRewardId = rewardId?.Trim() ?? string.Empty;
        return !string.IsNullOrWhiteSpace(normalizedRewardId)
            && linkedRewardCooldownSecondsById is not null
            && linkedRewardCooldownSecondsById.TryGetValue(normalizedRewardId, out var cooldownSeconds)
                ? Math.Max(0, cooldownSeconds)
                : null;
    }

    private static bool TryToUniversalSnapshot(
        UniversalTriggerRule rule,
        bool requireTriggerFilter,
        out UniversalTriggerRuleSnapshot snapshot)
    {
        snapshot = default!;
        if (requireTriggerFilter && !IsUniversalTriggerFilterReady(rule))
        {
            return false;
        }

        var actions = rule.Actions
            .Select(ToUniversalActionSnapshot)
            .Where(action => !string.IsNullOrWhiteSpace(action.OscAddress)
                && !string.IsNullOrWhiteSpace(action.TargetValue)
                && (action.DurationSeconds <= 0 || !string.IsNullOrWhiteSpace(action.DefaultValue)))
            .ToArray();
        if (actions.Length == 0)
        {
            return false;
        }

        snapshot = new UniversalTriggerRuleSnapshot(
            rule.Id == Guid.Empty ? Guid.NewGuid() : rule.Id,
            rule.IsEnabled,
            string.IsNullOrWhiteSpace(rule.DisplayTitle) ? "Universal Trigger" : rule.DisplayTitle.Trim(),
            rule.TriggerType,
            rule.ChatCommandEnabled,
            ChatCommandUtility.Normalize(rule.CommandText),
            rule.ChatCommandPermission,
            rule.RewardId.Trim(),
            rule.RewardTitle.Trim(),
            Math.Max(1, rule.MinimumBits),
            Math.Max(1, rule.MaximumBits),
            rule.SubscriptionTier.Trim(),
            rule.MinimumMonths,
            rule.MaximumMonths,
            Math.Max(0, rule.GlobalDelaySeconds),
            Math.Max(0, rule.UserDelaySeconds),
            rule.ExecuteRandomAction,
            rule.ImportSource.Trim(),
            actions);
        return true;
    }

    private static UniversalTriggerActionSnapshot ToUniversalActionSnapshot(UniversalTriggerAction action)
    {
        return new UniversalTriggerActionSnapshot(
            action.Id == Guid.Empty ? Guid.NewGuid() : action.Id,
            action.OscAddress.Trim(),
            Enum.IsDefined(action.ValueKind) ? action.ValueKind : UniversalTriggerValueKind.Int,
            action.TargetValue.Trim(),
            action.DefaultValue.Trim(),
            Math.Max(0, action.DurationSeconds),
            action.AddToQueue,
            action.ImportGroupKey.Trim());
    }

    private static bool TryToAvatarScaleSnapshot(
        AvatarScaleRule rule,
        bool requireTriggerFilter,
        out AvatarScaleRuleSnapshot snapshot)
    {
        snapshot = default!;
        if (requireTriggerFilter && !IsAvatarScaleTriggerFilterReady(rule))
        {
            return false;
        }

        if (!IsAvatarScaleActionReady(rule))
        {
            return false;
        }

        var cooldownSeconds =
            rule.TriggerType == AvatarScaleTriggerType.ChannelPointReward
            && rule.RewardSyncMode == TwitchRewardSyncMode.CreateOrManage
                ? Math.Max(0, rule.CooldownSeconds)
                : 0;

        snapshot = new AvatarScaleRuleSnapshot(
            rule.Id == Guid.Empty ? Guid.NewGuid() : rule.Id,
            rule.IsEnabled,
            string.IsNullOrWhiteSpace(rule.DisplayTitle) ? "Avatar Scale" : rule.DisplayTitle.Trim(),
            rule.TriggerType,
            rule.ChatCommandEnabled,
            ChatCommandUtility.Normalize(rule.CommandText),
            rule.ChatCommandPermission,
            rule.RewardId.Trim(),
            rule.RewardTitle.Trim(),
            Math.Max(1, rule.MinimumBits),
            Math.Max(1, rule.MaximumBits),
            rule.SubscriptionTier.Trim(),
            rule.MinimumMonths,
            rule.MaximumMonths,
            cooldownSeconds,
            [.. rule.TemporarilyDisabledScaleRuleIds.Where(ruleId => ruleId != Guid.Empty).Distinct()],
            rule.ScaleMode,
            ClampScaleHeight(rule.TargetHeightMeters, rule.AdvancedRangeEnabled),
            ClampScaleHeight(rule.MinimumHeightMeters, rule.AdvancedRangeEnabled),
            ClampScaleHeight(rule.MaximumHeightMeters, rule.AdvancedRangeEnabled),
            ClampRelativeScaleHeight(rule.RelativeHeightMeters, rule.AdvancedRangeEnabled),
            ClampScaleHeight(rule.RelativeMinimumHeightMeters, rule.AdvancedRangeEnabled),
            ClampScaleHeight(rule.RelativeMaximumHeightMeters, rule.AdvancedRangeEnabled),
            Math.Clamp(rule.HeightMultiplier, 0.01, AvatarScaleRule.AdvancedMaximumHeightMeters),
            rule.Preset,
            Math.Max(0, rule.ActiveTimeSeconds),
            AvatarScaleRestoreMode.ConfiguredHeight,
            ClampScaleHeight(rule.RestoreHeightMeters, rule.AdvancedRangeEnabled),
            Math.Clamp(rule.SmoothTransitionSeconds, 0, 30),
            rule.AdvancedRangeEnabled,
            rule.BypassVrChatScaleLimits,
            ClampScaleHeight(rule.SupporterGrowthNormalHeightMeters, rule.AdvancedRangeEnabled),
            ClampRelativeScaleHeight(rule.SupporterGrowthMaxAddedHeightMeters, rule.AdvancedRangeEnabled),
            Math.Max(1, rule.SupporterGrowthInactivityTimerSeconds),
            rule.SupporterGrowthAllowRewardScaleOverlay,
            Math.Max(1, rule.SupporterGrowthBitsTimerUnit),
            Math.Max(0, rule.SupporterGrowthSecondsPerBitsUnit),
            Math.Max(0, rule.SupporterGrowthTier1Seconds),
            Math.Max(0, rule.SupporterGrowthTier2Seconds),
            Math.Max(0, rule.SupporterGrowthTier3Seconds),
            Math.Max(0, rule.SupporterGrowthSoftCapSeconds),
            Math.Clamp(rule.SupporterGrowthSoftCapMultiplierPercent, 0, 100),
            Math.Max(1, rule.SupporterGrowthMaxPaidTimeSeconds),
            string.IsNullOrWhiteSpace(rule.SupporterGrowthGrowKeyword) ? "grow" : rule.SupporterGrowthGrowKeyword.Trim(),
            string.IsNullOrWhiteSpace(rule.SupporterGrowthShrinkKeyword) ? "shrink" : rule.SupporterGrowthShrinkKeyword.Trim(),
            Math.Max(0, rule.SupporterGrowthTier1HeightMeters),
            Math.Max(0, rule.SupporterGrowthTier2HeightMeters),
            Math.Max(0, rule.SupporterGrowthTier3HeightMeters),
            [.. rule.SupporterGrowthBitRanges.Select(ToAvatarScaleBitGrowthRangeSnapshot)]);
        return true;
    }

    private static AvatarScaleMasterRewardSnapshot ToAvatarScaleMasterRewardSnapshot(
        AvatarScaleMasterRewardSettings settings)
    {
        return new AvatarScaleMasterRewardSnapshot(
            settings.IsEnabled,
            settings.RewardId.Trim(),
            settings.RewardTitle.Trim(),
            Math.Max(1, settings.UnlockDurationSeconds),
            Math.Max(0, settings.CooldownSeconds),
            settings.PreventAvatarChangesDuringActiveScaling);
    }

    private static CashPaymentConnectionSnapshot ToCashPaymentConnectionSnapshot(
        CashPaymentConnectionSettings settings)
    {
        return new CashPaymentConnectionSnapshot(
            settings.StreamElementsEnabled,
            settings.StreamElementsAccountId.Trim(),
            settings.StreamElementsJwtToken,
            settings.StreamlabsEnabled,
            settings.StreamlabsAccessToken,
            settings.KoFiEnabled,
            Enum.IsDefined(settings.KoFiConnectionMode) ? settings.KoFiConnectionMode : KoFiConnectionMode.HostedRelay,
            string.IsNullOrWhiteSpace(settings.KoFiRelayBaseUrl)
                ? CashPaymentConnectionSettings.DefaultKoFiRelayBaseUrl
                : settings.KoFiRelayBaseUrl.Trim().TrimEnd('/'),
            settings.KoFiRelayChannelId.Trim(),
            settings.KoFiRelayClientSecret,
            Math.Clamp(settings.KoFiLocalPort, 1, 65535),
            string.IsNullOrWhiteSpace(settings.KoFiWebhookPath) ? "/kofi" : settings.KoFiWebhookPath.Trim(),
            settings.KoFiVerificationToken);
    }

    private static AvatarScaleBitGrowthRangeSnapshot ToAvatarScaleBitGrowthRangeSnapshot(AvatarScaleBitGrowthRange range)
    {
        return new AvatarScaleBitGrowthRangeSnapshot(
            Math.Max(1, range.MinimumBits),
            Math.Max(0, range.MaximumBits),
            Math.Max(0, range.HeightAddedMeters));
    }

    private static bool IsLiveRuntimeReady(TriggerRule rule, AvatarTriggerProfile? profile, bool isGlobalOverride)
    {
        var hasChatCommandFallback = rule.ChatCommandEnabled && ChatCommandUtility.IsConfigured(rule.ChatCommandText);
        var hasChannelPointRewardIdentity = rule.ActionType == OscActionType.SetTrigger && profile is not null
            ? !string.IsNullOrWhiteSpace(profile.SetTriggerMasterRewardId)
                || !string.IsNullOrWhiteSpace(profile.SetTriggerMasterRewardTitle)
            : !string.IsNullOrWhiteSpace(rule.ChannelPointRewardId)
                || !string.IsNullOrWhiteSpace(rule.ChannelPointRewardTitle);

        if (rule.TriggerType == TwitchTriggerType.ChannelPoints
            && !hasChatCommandFallback
            && !hasChannelPointRewardIdentity)
        {
            return false;
        }

        var isForceMovementSupporterRule = isGlobalOverride
            && rule.TriggerType == TwitchTriggerType.Bits
            && rule.ActionType == OscActionType.PlayerMovement;

        if (isForceMovementSupporterRule && string.IsNullOrWhiteSpace(rule.SupporterKeywordText))
        {
            return false;
        }

        if (isGlobalOverride
            && rule.TriggerType is TwitchTriggerType.Bits or TwitchTriggerType.Subscriptions
            && rule.ActionType is not (OscActionType.AvatarChange or OscActionType.AvatarRoulet)
            && !isForceMovementSupporterRule
            && string.IsNullOrWhiteSpace(rule.SupporterAvatarId)
            && string.IsNullOrWhiteSpace(profile?.AvatarId))
        {
            return false;
        }

        return IsManualTestReady(rule);
    }

    private static bool IsUniversalTriggerFilterReady(UniversalTriggerRule rule)
    {
        return rule.TriggerType switch
        {
            UniversalTriggerType.ChatCommand => ChatCommandUtility.IsConfigured(rule.CommandText),
            UniversalTriggerType.ChannelPointReward => !string.IsNullOrWhiteSpace(rule.RewardId)
                || !string.IsNullOrWhiteSpace(rule.RewardTitle),
            UniversalTriggerType.Bits => Math.Max(1, rule.MaximumBits) >= Math.Max(1, rule.MinimumBits),
            UniversalTriggerType.Subscription or UniversalTriggerType.GiftSubscription or UniversalTriggerType.Follow => true,
            _ => false
        };
    }

    private static bool IsAvatarScaleTriggerFilterReady(AvatarScaleRule rule)
    {
        return rule.TriggerType switch
        {
            AvatarScaleTriggerType.ChatCommand => ChatCommandUtility.IsConfigured(rule.CommandText),
            AvatarScaleTriggerType.ChannelPointReward => !string.IsNullOrWhiteSpace(rule.RewardId)
                || !string.IsNullOrWhiteSpace(rule.RewardTitle),
            AvatarScaleTriggerType.Bits => Math.Max(1, rule.MaximumBits) >= Math.Max(1, rule.MinimumBits),
            AvatarScaleTriggerType.SupporterGrowth => true,
            AvatarScaleTriggerType.Subscription or AvatarScaleTriggerType.GiftSubscription or AvatarScaleTriggerType.Follow => true,
            _ => false
        };
    }

    private static bool IsAvatarScaleActionReady(AvatarScaleRule rule)
    {
        return rule.ScaleMode switch
        {
            AvatarScaleMode.RandomHeight => Math.Max(rule.MinimumHeightMeters, rule.MaximumHeightMeters) >= Math.Min(rule.MinimumHeightMeters, rule.MaximumHeightMeters),
            AvatarScaleMode.GlitchyRandomHeight => rule.ActiveTimeSeconds > 0
                && Math.Max(rule.MinimumHeightMeters, rule.MaximumHeightMeters) > Math.Min(rule.MinimumHeightMeters, rule.MaximumHeightMeters),
            AvatarScaleMode.Multiplier => rule.HeightMultiplier > 0,
            _ => true
        };
    }

    private static double ClampScaleHeight(double value, bool advancedRangeEnabled)
    {
        if (double.IsNaN(value) || double.IsInfinity(value))
        {
            return 1.6;
        }

        return Math.Clamp(
            value,
            advancedRangeEnabled ? AvatarScaleRule.AdvancedMinimumHeightMeters : AvatarScaleRule.SafeMinimumHeightMeters,
            advancedRangeEnabled ? AvatarScaleRule.AdvancedMaximumHeightMeters : AvatarScaleRule.SafeMaximumHeightMeters);
    }

    private static double ClampRelativeScaleHeight(double value, bool advancedRangeEnabled)
    {
        if (double.IsNaN(value) || double.IsInfinity(value))
        {
            return 0;
        }

        var limit = advancedRangeEnabled ? AvatarScaleRule.AdvancedMaximumHeightMeters : AvatarScaleRule.SafeMaximumHeightMeters;
        return Math.Clamp(value, -limit, limit);
    }

    private static bool IsManualTestReady(TriggerRule rule)
    {
        return rule.ActionType switch
        {
            OscActionType.AvatarParameter => IsAvatarParameterRuleReady(rule),
            OscActionType.SetTrigger => IsSetTriggerRuleReady(rule),
            OscActionType.AvatarChange => !string.IsNullOrWhiteSpace(rule.AvatarChangeTargetId),
            OscActionType.AvatarRoulet => rule.AvatarRouletAvatarIds.Any(avatarId => !string.IsNullOrWhiteSpace(avatarId)),
            OscActionType.PlayerMovement => IsSupportedMovementDirection(rule.MovementDirection),
            _ => false
        };
    }

    private static string GetManualTestReadinessError(TriggerRule rule) => rule.ActionType switch
    {
        OscActionType.AvatarParameter => "Pick a valid VRChat parameter and trigger/reset value before testing this rule.",
        OscActionType.SetTrigger => "Add at least one complete Set Trigger parameter before testing this rule.",
        OscActionType.AvatarChange => "Pick the avatar target first before testing this rule.",
        OscActionType.AvatarRoulet => "Pick at least one avatar for the roulette pool before testing this rule.",
        OscActionType.PlayerMovement => "Pick a supported movement action first before testing this rule.",
        _ => "Finish the rule action setup before testing this rule."
    };

    private static bool IsAvatarParameterRuleReady(TriggerRule rule)
    {
        if (!HasAvatarParameterPath(rule.ParameterName)
            || string.IsNullOrWhiteSpace(rule.ParameterValue))
        {
            return false;
        }

        try
        {
            var oscClient = new VrChatOscClient();
            var parameterValue = rule.ParameterType == OscParameterType.Float
                ? FloatValueModeConverter.TryParseNormalized(rule.FloatValueMode, rule.ParameterValue, out var normalizedValue)
                    ? FloatValueModeConverter.ToOscText(normalizedValue)
                    : string.Empty
                : rule.ParameterValue;
            if (string.IsNullOrWhiteSpace(parameterValue))
            {
                return false;
            }

            _ = oscClient.BuildAvatarParameterPacket(rule.ParameterName, rule.ParameterType, parameterValue);
            if (rule.DurationSeconds > 0)
            {
                if (string.IsNullOrWhiteSpace(rule.ResetValue))
                {
                    return false;
                }

                var resetValue = rule.ParameterType == OscParameterType.Float
                    ? FloatValueModeConverter.TryParseNormalized(rule.FloatValueMode, rule.ResetValue, out var normalizedResetValue)
                        ? FloatValueModeConverter.ToOscText(normalizedResetValue)
                        : string.Empty
                    : rule.ResetValue;
                if (string.IsNullOrWhiteSpace(resetValue))
                {
                    return false;
                }

                _ = oscClient.BuildAvatarParameterPacket(rule.ParameterName, rule.ParameterType, resetValue);
            }

            return true;
        }
        catch
        {
            return false;
        }
    }

    private static bool IsSetTriggerRuleReady(TriggerRule rule)
    {
        if (!rule.SetTriggerActions.Any(IsSetTriggerActionReady))
        {
            return false;
        }

        return rule.TriggerType switch
        {
            TwitchTriggerType.ChannelPoints => rule.SharedRewardChoiceEnabled && rule.SharedRewardChoiceNumber > 0,
            TwitchTriggerType.Bits => !string.IsNullOrWhiteSpace(rule.SharedRewardHelpText),
            _ => false
        };
    }

    private static bool IsSetTriggerActionReady(SetTriggerAction action)
    {
        if (action.ParameterType is not (OscParameterType.Bool or OscParameterType.Int or OscParameterType.Float)
            || !HasAvatarParameterPath(action.ParameterName)
            || string.IsNullOrWhiteSpace(action.ParameterValue))
        {
            return false;
        }

        try
        {
            _ = new VrChatOscClient().BuildAvatarParameterPacket(action.ParameterName, action.ParameterType, action.ParameterValue);
            return true;
        }
        catch
        {
            return false;
        }
    }

    private static bool IsSupportedMovementDirection(PlayerMovementDirection direction) => direction is
        PlayerMovementDirection.Forward
        or PlayerMovementDirection.Backward
        or PlayerMovementDirection.Left
        or PlayerMovementDirection.Right
        or PlayerMovementDirection.Jump
        or PlayerMovementDirection.SpinLeft
        or PlayerMovementDirection.SpinRight
        or PlayerMovementDirection.RandomMovement
        or PlayerMovementDirection.GlitchyMovement;

    private static bool HasAvatarParameterPath(string parameterName)
    {
        try
        {
            _ = VrChatOscClient.NormalizeAvatarParameterAddress(parameterName);
            return true;
        }
        catch (InvalidOperationException)
        {
            return false;
        }
    }
}

internal static class AvatarRuleActivationPolicy
{
    public static bool IsRuleActiveForCurrentAvatar(
        bool isGlobalOverride,
        bool belongsToMasterAvatarProfile,
        OscActionType actionType,
        string? avatarChangeTargetId,
        string? requiredAvatarId,
        string? currentAvatarId,
        bool avatarChangeTransitionActive,
        bool avatarChangeCooldownOnlyModeEnabled = false)
    {
        if (isGlobalOverride)
        {
            return true;
        }

        var normalizedCurrentAvatarId = currentAvatarId?.Trim() ?? string.Empty;
        if (avatarChangeCooldownOnlyModeEnabled
            && belongsToMasterAvatarProfile
            && actionType == OscActionType.AvatarChange)
        {
            var normalizedAvatarChangeTargetId = avatarChangeTargetId?.Trim() ?? string.Empty;
            return !string.IsNullOrWhiteSpace(normalizedCurrentAvatarId)
                && !string.IsNullOrWhiteSpace(normalizedAvatarChangeTargetId)
                && !string.Equals(normalizedAvatarChangeTargetId, normalizedCurrentAvatarId, StringComparison.Ordinal);
        }

        var normalizedRequiredAvatarId = requiredAvatarId?.Trim() ?? string.Empty;
        if (string.IsNullOrWhiteSpace(normalizedRequiredAvatarId)
            || string.IsNullOrWhiteSpace(normalizedCurrentAvatarId)
            || !string.Equals(normalizedRequiredAvatarId, normalizedCurrentAvatarId, StringComparison.Ordinal))
        {
            return false;
        }

        if (actionType is OscActionType.AvatarChange or OscActionType.AvatarRoulet)
        {
            if (!belongsToMasterAvatarProfile || avatarChangeTransitionActive)
            {
                return false;
            }

            // Hide direct avatar switches that would "change" to the avatar already in use.
            if (actionType == OscActionType.AvatarChange)
            {
                var normalizedAvatarChangeTargetId = avatarChangeTargetId?.Trim() ?? string.Empty;
                if (!string.IsNullOrWhiteSpace(normalizedAvatarChangeTargetId)
                    && string.Equals(normalizedAvatarChangeTargetId, normalizedCurrentAvatarId, StringComparison.Ordinal))
                {
                    return false;
                }
            }

            return true;
        }

        return true;
    }
}
