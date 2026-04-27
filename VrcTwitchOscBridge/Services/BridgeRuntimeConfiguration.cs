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

public sealed record TriggerRuleSnapshot(
    Guid Id,
    bool IsEnabled,
    string Name,
    bool IsGlobalOverride,
    Guid AvatarProfileId,
    string AvatarProfileName,
    string RequiredAvatarId,
    string RequiredAvatarName,
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
    bool MaxAccumulatedDurationEnabled,
    int MaxAccumulatedDurationSeconds,
    OscActionType ActionType,
    PlayerMovementDirection MovementDirection,
    string ParameterName,
    OscParameterType ParameterType,
    IntZeroDurationMode IntZeroDurationMode,
    string ParameterValue,
    string ResetValue,
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

public sealed record BridgeRuntimeConfiguration(
    string TwitchClientId,
    TwitchAccountSnapshot Broadcaster,
    TwitchAccountSnapshot? Bot,
    string CurrentVrChatAvatarId,
    string SharedReturnAvatarId,
    string SharedReturnAvatarName,
    bool ChatboxOscEnabled,
    int ChatboxOscDelaySeconds,
    bool SupporterOverrideInfoMessageEnabled,
    bool ChannelPointRewardTestModeEnabled,
    bool EmergencyRedeemStopEnabled,
    bool DesktopModeInputLockEnabled,
    IReadOnlyList<TriggerRuleSnapshot> Rules,
    IReadOnlyList<UniversalTriggerRuleSnapshot> UniversalTriggers)
{
    public static BridgeRuntimeConfiguration FromSettings(AppSettings settings, RuntimeConfig runtimeConfig)
    {
        var rules = new List<TriggerRuleSnapshot>();
        var universalTriggers = new List<UniversalTriggerRuleSnapshot>();
        var masterProfile = settings.AvatarProfiles.FirstOrDefault(profile => profile.IsMasterProfile);

        foreach (var profile in settings.AvatarProfiles)
        {
            foreach (var rule in profile.ChannelPointRules)
            {
                if (TryToSnapshot(rule, isGlobalOverride: false, profile, out var snapshot))
                {
                    rules.Add(snapshot);
                }
            }
        }

        foreach (var rule in settings.GlobalOverrideRules)
        {
            if (TryToSnapshot(rule, isGlobalOverride: true, profile: null, out var snapshot))
            {
                rules.Add(snapshot);
            }
        }

        foreach (var rule in settings.GlobalMovementRules)
        {
            if (TryToSnapshot(rule, isGlobalOverride: true, profile: null, out var snapshot))
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

        return new BridgeRuntimeConfiguration(
            runtimeConfig.TwitchClientId.Trim(),
            ToSnapshot(settings.Broadcaster),
            settings.Bot.IsConnected ? ToSnapshot(settings.Bot) : null,
            settings.VrChat.CurrentAvatarId.Trim(),
            masterProfile?.AvatarId.Trim() ?? string.Empty,
            masterProfile?.AvatarName.Trim() ?? string.Empty,
            settings.ChatboxOscEnabled,
            settings.ChatboxOscDelaySeconds,
            settings.SupporterOverrideInfoMessageEnabled,
            settings.ChannelPointRewardTestModeEnabled,
            settings.EmergencyRedeemStopEnabled,
            settings.DesktopModeInputLockEnabled,
            rules.ToArray(),
            universalTriggers.ToArray());
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

        return CreateSnapshot(rule, isGlobalOverride, profile);
    }

    public static UniversalTriggerRuleSnapshot CreateManualTestSnapshot(UniversalTriggerRule rule)
    {
        if (!TryToUniversalSnapshot(rule, requireTriggerFilter: false, out var snapshot))
        {
            throw new InvalidOperationException("Add at least one complete OSC action before testing this universal trigger.");
        }

        return snapshot;
    }

    private static bool TryToSnapshot(
        TriggerRule rule,
        bool isGlobalOverride,
        AvatarTriggerProfile? profile,
        out TriggerRuleSnapshot snapshot)
    {
        snapshot = default!;

        if (!IsLiveRuntimeReady(rule))
        {
            return false;
        }

        snapshot = CreateSnapshot(rule, isGlobalOverride, profile);
        return true;
    }

    private static TriggerRuleSnapshot CreateSnapshot(
        TriggerRule rule,
        bool isGlobalOverride,
        AvatarTriggerProfile? profile)
    {
        return new TriggerRuleSnapshot(
            rule.Id,
            (profile?.IsEnabled ?? true) && rule.IsEnabled,
            rule.DisplayTitle,
            isGlobalOverride,
            profile?.Id ?? Guid.Empty,
            profile?.Name.Trim() ?? string.Empty,
            profile?.AvatarId.Trim() ?? string.Empty,
            profile?.AvatarName.Trim() ?? string.Empty,
            profile?.IsMasterProfile ?? false,
            rule.TriggerType,
            rule.ChannelPointRewardId.Trim(),
            rule.ChannelPointRewardTitle.Trim(),
            ManagedRewardPresentation.NormalizeReadyBackgroundColor(rule.ManagedRewardReadyColor),
            ManagedRewardPresentation.NormalizeCooldownBackgroundColor(rule.ManagedRewardCooldownColor),
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
            rule.MaxAccumulatedDurationEnabled,
            rule.MaxAccumulatedDurationSeconds,
            rule.ActionType,
            rule.MovementDirection,
            rule.ParameterName.Trim(),
            rule.ParameterType,
            rule.IntZeroDurationMode,
            rule.ParameterValue.Trim(),
            rule.ResetValue.Trim(),
            rule.AvatarChangeTargetId.Trim(),
            rule.AvatarChangeResetId.Trim(),
            rule.AvatarTargetName.Trim(),
            rule.ResetAvatarName.Trim(),
            [.. rule.AvatarRouletAvatarIds.Select(avatarId => avatarId?.Trim() ?? string.Empty).Where(avatarId => !string.IsNullOrWhiteSpace(avatarId)).Distinct(StringComparer.Ordinal)],
            [.. rule.AvatarRouletAvatarNames.Select(avatarName => avatarName?.Trim() ?? string.Empty)],
            rule.RangeMinimum,
            rule.RangeMaximum,
            rule.DurationSeconds,
            rule.CooldownSeconds,
            [.. rule.TemporarilyDisabledRuleIds.Where(ruleId => ruleId != Guid.Empty).Distinct()],
            rule.BotMessageTemplate.Trim());
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

    private static bool IsLiveRuntimeReady(TriggerRule rule)
    {
        var hasChatCommandFallback = rule.ChatCommandEnabled && ChatCommandUtility.IsConfigured(rule.ChatCommandText);
        if (rule.TriggerType == TwitchTriggerType.ChannelPoints
            && !hasChatCommandFallback
            && string.IsNullOrWhiteSpace(rule.ChannelPointRewardId)
            && string.IsNullOrWhiteSpace(rule.ChannelPointRewardTitle))
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

    private static bool IsManualTestReady(TriggerRule rule)
    {
        return rule.ActionType switch
        {
            OscActionType.AvatarParameter => HasAvatarParameterPath(rule.ParameterName),
            OscActionType.AvatarChange => !string.IsNullOrWhiteSpace(rule.AvatarChangeTargetId),
            OscActionType.AvatarRoulet => rule.AvatarRouletAvatarIds.Any(avatarId => !string.IsNullOrWhiteSpace(avatarId)),
            OscActionType.PlayerMovement => IsSupportedMovementDirection(rule.MovementDirection),
            _ => false
        };
    }

    private static string GetManualTestReadinessError(TriggerRule rule) => rule.ActionType switch
    {
        OscActionType.AvatarParameter => "Pick a valid VRChat parameter first before testing this rule.",
        OscActionType.AvatarChange => "Pick the avatar target first before testing this rule.",
        OscActionType.AvatarRoulet => "Pick at least one avatar for the roulette pool before testing this rule.",
        OscActionType.PlayerMovement => "Pick a supported movement action first before testing this rule.",
        _ => "Finish the rule action setup before testing this rule."
    };

    private static bool IsSupportedMovementDirection(PlayerMovementDirection direction) => direction is
        PlayerMovementDirection.Forward
        or PlayerMovementDirection.Backward
        or PlayerMovementDirection.Left
        or PlayerMovementDirection.Right
        or PlayerMovementDirection.SpinLeft
        or PlayerMovementDirection.SpinRight;

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
        bool avatarChangeTransitionActive)
    {
        if (isGlobalOverride)
        {
            return true;
        }

        var normalizedRequiredAvatarId = requiredAvatarId?.Trim() ?? string.Empty;
        var normalizedCurrentAvatarId = currentAvatarId?.Trim() ?? string.Empty;
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
