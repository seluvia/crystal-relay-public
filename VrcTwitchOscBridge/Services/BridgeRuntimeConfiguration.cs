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

public sealed record WardrobeOutfitSnapshot(
    Guid Id,
    bool IsEnabled,
    string Name,
    Guid AvatarProfileId,
    string AvatarId,
    int ActiveTimeSeconds,
    int CooldownSeconds,
    IReadOnlyList<WardrobeParamSnapshot> Params,
    bool UsesMasterReward);

public sealed record WardrobeParamSnapshot(
    string ParameterName,
    OscParameterType ParameterType,
    string SetValue);

public sealed record SupporterFloatAddRangeSnapshot(
    int MinimumAmount,
    int MaximumAmount,
    string AddValue);

public sealed record RedeemGroupSnapshot(
    string Name,
    string CommandText,
    IReadOnlyList<Guid> AssignedRuleIds);

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
    bool SupporterFloatAddEnabled,
    string SupporterFloatAddMinimumValue,
    string SupporterFloatAddMaximumValue,
    IReadOnlyList<SupporterFloatAddRangeSnapshot> SupporterFloatAddRanges,
    string AvatarChangeTargetId,
    string AvatarChangeResetId,
    string AvatarTargetName,
    string ResetAvatarName,
    IReadOnlyList<string> AvatarRouletAvatarIds,
    IReadOnlyList<string> AvatarRouletAvatarNames,
    int RangeMinimum,
    int RangeMaximum,
    double DurationSeconds,
    int CooldownSeconds,
    bool UsesLinkedChannelPointReward,
    int? BotMessageCooldownSeconds,
    bool SharedRewardChoiceEnabled,
    int SharedRewardChoiceNumber,
    string SharedRewardHelpText,
    bool UsesSharedNumberedOutfitReward,
    bool PostOutfitChoiceListToTwitchChat,
    SetTriggerRestoreMode SetTriggerRestoreMode,
    string SupporterKeywordText,
    IReadOnlyList<SetTriggerActionSnapshot> SetTriggerActions,
    SpecialRulePairingMode SpecialRulePairingMode,
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
    int MultiplierDirectionId,
    int RelativeHeightDirectionId,
    double SetHeightTransitionSeconds,
    double RandomHeightTransitionSeconds,
    double RelativeHeightTransitionSeconds,
    double MultiplierTransitionSeconds,
    double PresetTransitionSeconds,
    double GlitchyRandomHeightTransitionSeconds,
    double SupporterGrowthTransitionSeconds,
    double SmoothTransitionSeconds,
    AvatarScalePreset Preset,
    double ActiveTimeSeconds,
    AvatarScaleRestoreMode RestoreMode,
    double RestoreHeightMeters,
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

public sealed record PowerUpRuleSnapshot(
    Guid Id,
    bool IsEnabled,
    string Name,
    TwitchRewardSyncMode SourceMode,
    string PowerUpId,
    string PowerUpTitle,
    int BitsCost,
    string Prompt,
    bool AvatarScoped,
    string AvatarId,
    string AvatarName,
    int CooldownSeconds,
    bool FixedFloatAddEnabled,
    string FixedFloatAddValue,
    string FixedFloatAddMinimumValue,
    string FixedFloatAddMaximumValue,
    PowerUpActionKind ActionKind,
    TriggerRuleSnapshot? TriggerAction,
    AvatarScaleRuleSnapshot? ScaleAction);

public sealed record AvatarSwapProfileSnapshot(
    Guid Id,
    string TargetAvatarId,
    string TargetAvatarName,
    ReturnAvatarMode ReturnAvatarMode,
    string? ReturnAvatarId,
    string? ReturnAvatarName,
    bool IsEnabled,
    string? ThumbnailUrl,
    IReadOnlyList<TriggerRuleSnapshot> ChannelPointRules,
    IReadOnlyList<TriggerRuleSnapshot> BitsSubsRules);

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
    bool PauseCommandEnabled,
    string PauseCommandText,
    bool RedeemGroupCommandEnabled,
    bool RedeemControlCommandEnabled,
    IReadOnlyList<RedeemGroupSnapshot> RedeemGroups,
    AvatarScaleMasterRewardSnapshot AvatarScaleMasterReward,
    CashPaymentConnectionSnapshot CashPayments,
    IReadOnlyList<TriggerRuleSnapshot> Rules,
    IReadOnlyList<PowerUpRuleSnapshot> PowerUpRules,
    IReadOnlyList<UniversalTriggerRuleSnapshot> UniversalTriggers,
    IReadOnlyList<AvatarScaleRuleSnapshot> AvatarScaleRules,
    IReadOnlyList<CashPaymentRuleSnapshot> CashPaymentRules,
    IReadOnlyList<AvatarTriggerProfile> AvatarProfiles,
    IReadOnlyList<AvatarSwapProfileSnapshot> AvatarSwapProfiles,
    string MasterAvatarSwapReturnId,
    string MasterAvatarSwapReturnName)
{
    public static BridgeRuntimeConfiguration FromSettings(
        AppSettings settings,
        RuntimeConfig runtimeConfig,
        IReadOnlyDictionary<string, int>? linkedRewardCooldownSecondsById = null)
    {
        var rules = new List<TriggerRuleSnapshot>();
        var powerUpRules = new List<PowerUpRuleSnapshot>();
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

        foreach (var powerUpRule in settings.PowerUpRules)
        {
            if (TryToPowerUpSnapshot(powerUpRule, masterProfile, out var snapshot))
            {
                powerUpRules.Add(snapshot);
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

        var avatarSwapProfiles = new List<AvatarSwapProfileSnapshot>();
        foreach (var swapProfile in settings.AvatarSwapProfiles)
        {
            var channelPointSnapshots = new List<TriggerRuleSnapshot>();
            foreach (var rule in swapProfile.ChannelPointRules)
            {
                if (TryToSnapshot(rule, isGlobalOverride: false, profile: null, linkedRewardCooldownSecondsById, out var snapshot))
                {
                    channelPointSnapshots.Add(snapshot);
                }
            }

            var bitsSubsSnapshots = new List<TriggerRuleSnapshot>();
            foreach (var rule in swapProfile.BitsSubsRules)
            {
                if (TryToSnapshot(rule, isGlobalOverride: true, profile: null, linkedRewardCooldownSecondsById, out var snapshot))
                {
                    bitsSubsSnapshots.Add(snapshot);
                }
            }

            avatarSwapProfiles.Add(new AvatarSwapProfileSnapshot(
                swapProfile.Id,
                swapProfile.TargetAvatarId,
                swapProfile.TargetAvatarName,
                swapProfile.ReturnAvatarMode,
                swapProfile.ReturnAvatarId,
                swapProfile.ReturnAvatarName,
                swapProfile.IsEnabled,
                swapProfile.TargetThumbnailUrl,
                channelPointSnapshots.ToArray(),
                bitsSubsSnapshots.ToArray()));
        }

        return new BridgeRuntimeConfiguration(
            runtimeConfig.TwitchClientId.Trim(),
            ToSnapshot(settings.Broadcaster),
            settings.Bot.IsConnected ? ToSnapshot(settings.Bot) : null,
            settings.VrChat.CurrentAvatarId.Trim(),
            !string.IsNullOrWhiteSpace(settings.MasterAvatarSwapReturnId)
                ? settings.MasterAvatarSwapReturnId.Trim()
                : (masterProfile?.AvatarId.Trim() ?? string.Empty),
            !string.IsNullOrWhiteSpace(settings.MasterAvatarSwapReturnName)
                ? settings.MasterAvatarSwapReturnName.Trim()
                : (masterProfile?.AvatarName.Trim() ?? string.Empty),
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
            settings.PauseCommandEnabled,
            ChatCommandUtility.Normalize(settings.PauseCommandText),
            settings.RedeemGroupCommandEnabled,
            settings.RedeemControlCommandEnabled,
            [.. settings.RedeemGroups.Select(g => new RedeemGroupSnapshot(
                g.Name,
                ChatCommandUtility.Normalize(g.CommandText),
                [.. g.AssignedRuleIds]))],
            ToAvatarScaleMasterRewardSnapshot(settings.AvatarScaleMasterReward),
            ToCashPaymentConnectionSnapshot(settings.CashPayments),
            rules.ToArray(),
            powerUpRules.ToArray(),
            universalTriggers.ToArray(),
            avatarScaleRules.ToArray(),
            cashPaymentRules.ToArray(),
            settings.AvatarProfiles.ToArray(),
            avatarSwapProfiles.ToArray(),
            settings.MasterAvatarSwapReturnId?.Trim() ?? string.Empty,
            settings.MasterAvatarSwapReturnName?.Trim() ?? string.Empty);
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

    public static PowerUpRuleSnapshot CreateManualTestSnapshot(PowerUpRule rule, AvatarTriggerProfile? masterProfile)
    {
        if (!TryToPowerUpSnapshot(rule, masterProfile, out var snapshot))
        {
            throw new InvalidOperationException("Finish the Power Up rule setup before testing it.");
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

    internal static bool TryToWardrobeSnapshot(
        WardrobeOutfit outfit,
        AvatarTriggerProfile profile,
        out WardrobeOutfitSnapshot snapshot)
    {
        snapshot = default!;

        if (!outfit.IsEnabled || string.IsNullOrWhiteSpace(profile.AvatarId))
        {
            return false;
        }

        var validParams = outfit.SnapshotParams
            .Where(p => !string.IsNullOrWhiteSpace(p.ParameterName)
                     && !string.IsNullOrWhiteSpace(p.SetValue)
                     && p.ParameterType is OscParameterType.Bool or OscParameterType.Int or OscParameterType.Float)
            .Select(p => new WardrobeParamSnapshot(
                VrChatOscClient.NormalizeAvatarParameterAddress(p.ParameterName),
                p.ParameterType,
                p.SetValue))
            .ToList();

        if (validParams.Count == 0)
        {
            return false;
        }

        snapshot = new WardrobeOutfitSnapshot(
            outfit.Id,
            outfit.IsEnabled,
            outfit.DisplayTitle,
            profile.Id,
            profile.AvatarId,
            Math.Max(1, outfit.ActiveTimeSeconds),
            Math.Max(0, profile.WardrobeCooldownSeconds),
            validParams,
            profile.UseWardrobeMasterReward);
        return true;
    }

    private static TriggerRuleSnapshot CreateSnapshot(
        TriggerRule rule,
        bool isGlobalOverride,
        AvatarTriggerProfile? profile,
        IReadOnlyDictionary<string, int>? linkedRewardCooldownSecondsById)
    {
        var usesSharedNumberedOutfitReward = !isGlobalOverride
            && rule.ActionType == OscActionType.SetTrigger
            && profile?.UseSharedNumberedOutfitReward == true;
        var usesSetTriggerMasterReward = usesSharedNumberedOutfitReward;
        var postOutfitChoiceListToTwitchChat = !isGlobalOverride
            && rule.ActionType == OscActionType.SetTrigger
            && profile?.PostOutfitChoiceListToTwitchChat == true;
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
            rule.SupporterFloatAddEnabled,
            rule.SupporterFloatAddMinimumValue.Trim(),
            rule.SupporterFloatAddMaximumValue.Trim(),
            [.. rule.SupporterFloatAddRanges.Select(ToSupporterFloatAddRangeSnapshot)],
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
            usesSharedNumberedOutfitReward,
            postOutfitChoiceListToTwitchChat,
            Enum.IsDefined(rule.SetTriggerRestoreMode)
                ? rule.SetTriggerRestoreMode
                : SetTriggerRestoreMode.ConfiguredAndRelated,
            rule.SupporterKeywordText.Trim(),
            [.. rule.SetTriggerActions.Select(ToSetTriggerActionSnapshot).Where(action => HasAvatarParameterPath(action.ParameterName) && !string.IsNullOrWhiteSpace(action.ParameterValue))],
            rule.SpecialRulePairingMode,
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

    private static SupporterFloatAddRangeSnapshot ToSupporterFloatAddRangeSnapshot(SupporterFloatAddRange range)
    {
        return new SupporterFloatAddRangeSnapshot(
            Math.Max(1, range.MinimumAmount),
            Math.Max(0, range.MaximumAmount),
            range.AddValue.Trim());
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

    private static bool TryToPowerUpSnapshot(
        PowerUpRule rule,
        AvatarTriggerProfile? masterProfile,
        out PowerUpRuleSnapshot snapshot)
    {
        snapshot = default!;
        if (!rule.IsEnabled)
        {
            return false;
        }

        TriggerRuleSnapshot? triggerAction = null;
        AvatarScaleRuleSnapshot? scaleAction = null;
        if (rule.ActionKind == PowerUpActionKind.AvatarScaling)
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
                MinimumBits = 1,
                MaximumBits = int.MaxValue,
                CooldownSeconds = Math.Max(0, rule.CooldownSeconds)
            };
        }
        else
        {
            if (!IsManualTestReady(rule.ActionRule))
            {
                return false;
            }

            var actionSnapshot = CreateSnapshot(
                rule.ActionRule,
                isGlobalOverride: true,
                profile: null,
                linkedRewardCooldownSecondsById: null);

            var isAvatarSwitch = rule.ActionRule.ActionType is OscActionType.AvatarChange or OscActionType.AvatarRoulet;
            var requiredAvatarId = string.Empty;
            var requiredAvatarName = string.Empty;
            var belongsToMasterAvatarProfile = false;
            if (isAvatarSwitch)
            {
                requiredAvatarId = masterProfile?.AvatarId.Trim() ?? string.Empty;
                requiredAvatarName = masterProfile?.AvatarName.Trim() ?? string.Empty;
                belongsToMasterAvatarProfile = true;
            }
            else if (rule.AvatarScoped)
            {
                requiredAvatarId = rule.AvatarId.Trim();
                requiredAvatarName = rule.AvatarName.Trim();
            }

            var hasRequiredAvatar = !string.IsNullOrWhiteSpace(requiredAvatarId);
            triggerAction = actionSnapshot with
            {
                Id = rule.Id,
                IsEnabled = rule.IsEnabled && actionSnapshot.IsEnabled,
                Name = rule.DisplayTitle,
                IsGlobalOverride = !hasRequiredAvatar,
                AvatarProfileId = Guid.Empty,
                AvatarProfileName = string.Empty,
                RequiredAvatarId = requiredAvatarId,
                RequiredAvatarName = requiredAvatarName,
                SupporterAvatarProfileId = Guid.Empty,
                SupporterAvatarId = rule.AvatarScoped ? rule.AvatarId.Trim() : string.Empty,
                SupporterAvatarName = rule.AvatarScoped ? rule.AvatarName.Trim() : string.Empty,
                BelongsToMasterAvatarProfile = belongsToMasterAvatarProfile,
                TriggerType = TwitchTriggerType.PowerUp,
                ChannelPointRewardId = string.Empty,
                ChannelPointRewardTitle = rule.PowerUpTitle.Trim(),
                ChatCommandEnabled = false,
                ChatCommandText = string.Empty,
                MinimumAmount = 1,
                AmountScaledDurationEnabled = false,
                CooldownSeconds = Math.Max(0, rule.CooldownSeconds),
                UsesLinkedChannelPointReward = false,
                BotMessageCooldownSeconds = Math.Max(0, rule.CooldownSeconds),
                SupporterFloatAddEnabled = rule.FixedFloatAddEnabled,
                SupporterFloatAddMinimumValue = rule.FixedFloatAddMinimumValue.Trim(),
                SupporterFloatAddMaximumValue = rule.FixedFloatAddMaximumValue.Trim(),
                SupporterFloatAddRanges =
                [
                    new SupporterFloatAddRangeSnapshot(
                        1,
                        0,
                        string.IsNullOrWhiteSpace(rule.FixedFloatAddValue) ? "0.05" : rule.FixedFloatAddValue.Trim())
                ]
            };
        }

        snapshot = new PowerUpRuleSnapshot(
            rule.Id,
            rule.IsEnabled,
            rule.DisplayTitle,
            Enum.IsDefined(rule.SourceMode) ? rule.SourceMode : TwitchRewardSyncMode.LinkExisting,
            rule.PowerUpId.Trim(),
            rule.PowerUpTitle.Trim(),
            Math.Max(1, rule.BitsCost),
            rule.Prompt.Trim(),
            rule.AvatarScoped,
            rule.AvatarId.Trim(),
            rule.AvatarName.Trim(),
            Math.Max(0, rule.CooldownSeconds),
            rule.FixedFloatAddEnabled,
            string.IsNullOrWhiteSpace(rule.FixedFloatAddValue) ? "0.05" : rule.FixedFloatAddValue.Trim(),
            string.IsNullOrWhiteSpace(rule.FixedFloatAddMinimumValue) ? "0" : rule.FixedFloatAddMinimumValue.Trim(),
            string.IsNullOrWhiteSpace(rule.FixedFloatAddMaximumValue) ? "1" : rule.FixedFloatAddMaximumValue.Trim(),
            Enum.IsDefined(rule.ActionKind) ? rule.ActionKind : PowerUpActionKind.TriggerAction,
            triggerAction,
            scaleAction);
        return true;
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
            (int)rule.MultiplierDirection,
            (int)rule.RelativeHeightDirection,
            Math.Clamp(rule.SetHeightTransitionSeconds, 0, 30),
            Math.Clamp(rule.RandomHeightTransitionSeconds, 0, 30),
            Math.Clamp(rule.RelativeHeightTransitionSeconds, 0, 30),
            Math.Clamp(rule.MultiplierTransitionSeconds, 0, 30),
            Math.Clamp(rule.PresetTransitionSeconds, 0, 30),
            Math.Clamp(rule.GlitchyRandomHeightTransitionSeconds, 0, 30),
            Math.Clamp(rule.SupporterGrowthTransitionSeconds, 0, 30),
            Math.Clamp(rule.SmoothTransitionSeconds, 0, 30),
            rule.Preset,
            Math.Max(0, rule.ActiveTimeSeconds),
            AvatarScaleRestoreMode.ConfiguredHeight,
            ClampScaleHeight(rule.RestoreHeightMeters, rule.AdvancedRangeEnabled),
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
        var usesSharedSetTriggerReward = !isGlobalOverride
            && rule.ActionType == OscActionType.SetTrigger
            && profile?.UseSharedNumberedOutfitReward == true;
        var hasChannelPointRewardIdentity = usesSharedSetTriggerReward
            ? !string.IsNullOrWhiteSpace(profile!.SetTriggerMasterRewardId)
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
            TwitchTriggerType.PowerUp => true,
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
            && actionType is OscActionType.AvatarChange or OscActionType.AvatarRoulet)
        {
            if (avatarChangeTransitionActive || string.IsNullOrWhiteSpace(normalizedCurrentAvatarId))
            {
                return false;
            }

            if (actionType == OscActionType.AvatarRoulet)
            {
                return true;
            }

            var normalizedAvatarChangeTargetId = avatarChangeTargetId?.Trim() ?? string.Empty;
            return !string.IsNullOrWhiteSpace(normalizedAvatarChangeTargetId)
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
