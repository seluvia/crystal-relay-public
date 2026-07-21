using System.Collections.Concurrent;
using System.Collections.ObjectModel;
using System.Collections.Specialized;
using System.ComponentModel;
using System.Diagnostics;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Text;
using System.Windows;
using System.Windows.Data;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Threading;
using Microsoft.Win32;
using VrcTwitchOscBridge.Infrastructure;
using VrcTwitchOscBridge.Models;
using VrcTwitchOscBridge.Services;
using Brush = System.Windows.Media.Brush;
using Color = System.Windows.Media.Color;

namespace VrcTwitchOscBridge.ViewModels;

/// <summary>
/// Main UI view model for Crystal Relay.
/// This file ties the window to saved settings, Twitch and VRChat setup,
/// rule editing, managed rewards, chatbox state, and About-page data.
/// </summary>
public sealed class MainWindowViewModel : ObservableObject, IAsyncDisposable, ITwitchRewardSource
{
    // Internal result used by reward-sync callers so unsupported broadcaster accounts
    // can be handled as a normal limitation instead of a fatal error.
    private enum ManagedRewardSyncOutcome
    {
        Completed,
        BroadcasterCustomRewardsUnavailable,
        BroadcasterRewardManagementScopeMissing,
        BroadcasterTokenRefreshRequired
    }

    internal enum ManagedRewardSyncReason
    {
        SettingsEdit,
        Startup,
        AccountReconnect,
        AvatarChanged,
        RuntimeAvailability,
        AvatarScaleStatus,
        FloatLimitStatus,
        TestMode,
        EmergencyStop,
        StreamStateChanged,
        FireSaleChanged,
        ManualRefresh,
        ManualCleanup,
        Maintenance,
        AvatarScaleMasterRewardUnlocked
    }

    private sealed record BroadcasterRewardAccountSnapshot(
        bool IsConnected,
        string AccessToken,
        string RefreshToken,
        string UserId,
        string Login,
        string DisplayName,
        string ProfileImageUrl,
        DateTimeOffset? AccessTokenExpiresAt,
        DateTimeOffset? SessionRenewalDueAt,
        string[] Scopes,
        string TwitchClientId);

    private readonly record struct AvatarScaleRelativeLimitState(
        bool IsAtLimit,
        bool IsMinimumLimit,
        double CurrentHeightMeters,
        double EffectiveLimitMeters);

    private const string TwitchActivationUri = "https://www.twitch.tv/activate";
    private const string TwitchDeveloperConsoleUri = "https://dev.twitch.tv/console/apps";
    private const string KoFiSupportUri = "https://ko-fi.com/screminpal";
    private const string KoFiWebhookSettingsUri = "https://ko-fi.com/manage/webhooks";
    private const string DiscordInviteUri = "https://discord.gg/6DvWJXN6A2";
    private const string SettingsTestAudioRelativePath = "Assets\\engineer_no01.mp3";
    private const string TestBuildMarkerFileName = "test-build.flag";
    private const string BetaBuildMarkerFileName = "beta-build.flag";
    private const int MaxLogEntryCount = 200;
    private const int MaxChatMessageCount = 250;
    private const int MaxChatActivityEntryCount = 200;
    private const int TwitchCustomRewardPromptMaxLength = 200;
    private const int TwitchCustomRewardLimit = 50;
    private const int SavedLoginRecoveryPromptFailureThreshold = 2;
    private static readonly TimeSpan ManagedRewardCreateBackoffWindow = TimeSpan.FromSeconds(45);
    private static readonly TimeSpan ThrottledRewardSyncLogWindow = TimeSpan.FromSeconds(30);
    private static readonly TimeSpan ChatActivityDedupeWindow = TimeSpan.FromSeconds(12);
    private static readonly TimeSpan AvatarScaleLimitRewardSyncDebounce = TimeSpan.FromMilliseconds(750);
    private const double AvatarScaleLimitHeightToleranceMeters = 0.001;
    private const double AvatarScaleLimitHeightReleaseToleranceMeters = 0.003;
    private static readonly TimeSpan TwitchAccessTokenRefreshLeadTime = TimeSpan.FromMinutes(15);
    private static readonly TimeSpan TwitchCachedValidationGraceWindow = TimeSpan.FromMinutes(10);
    private static readonly TimeSpan TwitchPublicRefreshSessionWindow = TimeSpan.FromDays(30);
    private static readonly TimeSpan VrChatLocalStatePollInterval = TimeSpan.FromSeconds(10);
    private static readonly TimeSpan VrChatCurrentAvatarPollInterval = TimeSpan.FromSeconds(30);
    private static readonly TimeSpan ActiveAvatarScaleLocalRefreshInterval = TimeSpan.FromSeconds(5);
    private static readonly TimeSpan VrChatOscParameterAutoRefreshInitialDelay = TimeSpan.FromSeconds(2);
    private static readonly TimeSpan VrChatOscParameterAutoRefreshRetryDelay = TimeSpan.FromSeconds(3);
    private static readonly TimeSpan WorldCommandBlacklistRefreshInterval = TimeSpan.FromMinutes(10);
    private static readonly TimeSpan TwitchPublicSessionWindow = TimeSpan.FromDays(30);
    private static readonly TimeSpan AboutProfileRefreshInterval = TimeSpan.FromMinutes(5);
    private static readonly Guid AvatarScaleMasterRewardOwnerId = new("c69a2537-6c74-450f-9c5a-b6d9f04a7d95");
    private static readonly Guid RewardFireSaleFundingRewardOwnerId = new("f31cdb57-052f-4dd4-96d3-1c2b044e2fd9");
    private static readonly string[] IsoCurrencyCodeSeeds =
    [
        "USD", "EUR", "GBP", "CAD", "AUD", "NZD", "JPY", "CHF", "CNY", "HKD",
        "AED", "AFN", "ALL", "AMD", "ANG", "AOA", "ARS", "AWG", "AZN", "BAM",
        "BBD", "BDT", "BGN", "BHD", "BIF", "BMD", "BND", "BOB", "BOV", "BRL",
        "BSD", "BTN", "BWP", "BYN", "BZD", "CDF", "CHE", "CHW", "CLF", "CLP",
        "COP", "COU", "CRC", "CUC", "CUP", "CVE", "CZK", "DJF", "DKK", "DOP",
        "DZD", "EGP", "ERN", "ETB", "FJD", "FKP", "GEL", "GHS", "GIP", "GMD",
        "GNF", "GTQ", "GYD", "HNL", "HTG", "HUF", "IDR", "ILS", "INR", "IQD",
        "IRR", "ISK", "JMD", "JOD", "KES", "KGS", "KHR", "KMF", "KPW", "KRW",
        "KWD", "KYD", "KZT", "LAK", "LBP", "LKR", "LRD", "LSL", "LYD", "MAD",
        "MDL", "MGA", "MKD", "MMK", "MNT", "MOP", "MRU", "MUR", "MVR", "MWK",
        "MXN", "MXV", "MYR", "MZN", "NAD", "NGN", "NIO", "NOK", "NPR", "OMR",
        "PAB", "PEN", "PGK", "PHP", "PKR", "PLN", "PYG", "QAR", "RON", "RSD",
        "RUB", "RWF", "SAR", "SBD", "SCR", "SDG", "SEK", "SGD", "SHP", "SLE",
        "SLL", "SOS", "SRD", "SSP", "STN", "SVC", "SYP", "SZL", "THB", "TJS",
        "TMT", "TND", "TOP", "TRY", "TTD", "TWD", "TZS", "UAH", "UGX", "USN",
        "UYI", "UYU", "UYW", "UZS", "VED", "VES", "VND", "VUV", "WST", "XAF",
        "XAG", "XAU", "XBA", "XBB", "XBC", "XBD", "XCD", "XCG", "XDR", "XOF",
        "XPD", "XPF", "XPT", "XSU", "XTS", "XUA", "XXX", "YER", "ZAR", "ZMW",
        "ZWG"
    ];
    private static readonly AvatarScaleTriggerType[] PrimaryAvatarScaleTriggerTypes =
    [
        AvatarScaleTriggerType.ChannelPointReward,
        AvatarScaleTriggerType.ChatCommand,
        AvatarScaleTriggerType.Follow,
        AvatarScaleTriggerType.SupporterGrowth
    ];
    private static readonly HashSet<AvatarScaleTriggerType> LegacyPaidAvatarScaleTriggerTypes =
    [
        AvatarScaleTriggerType.Bits,
        AvatarScaleTriggerType.Subscription,
        AvatarScaleTriggerType.GiftSubscription
    ];
    private const int VrChatOscParameterAutoRefreshPassCount = 4;
    private static readonly string AppVersion = GetAppVersion();
    private static readonly ApplicationBuildIdentity AppBuildIdentity =
        ApplicationBuildIdentity.Detect(AppVersion, AppContext.BaseDirectory);
    private static readonly string BetaBuildLabel = AppBuildIdentity.HasBetaLabel
        ? AppBuildIdentity.DisplayLabel
        : string.Empty;
    private static readonly string AppUpdateVersion = AppBuildIdentity.UpdateVersion;
    private static readonly bool IsTestBuild = AppBuildIdentity.IsTestBuild;
    private static readonly string BuildChannel = AppBuildIdentity.BuildChannel;
    private static readonly HashSet<string> KnownViewerNotificationBotLogins = new(StringComparer.OrdinalIgnoreCase)
    {
        "nightbot",
        "streamelements",
        "streamlabs",
        "moobot",
        "fossabot",
        "sery_bot",
        "wizebot",
        "deepbot",
        "phantombot",
        "commanderroot",
        "soundalerts",
        "mixitupbot"
    };
    // These property sets decide which editor changes are large enough to justify
    // a bridge rebuild or managed Twitch reward sync.
    private static readonly HashSet<string> RulePropertiesRequiringBridgeRefresh = new(StringComparer.Ordinal)
    {
        nameof(TriggerRule.IsEnabled),
        nameof(TriggerRule.Name),
        nameof(TriggerRule.TriggerType),
        nameof(TriggerRule.ChannelPointRewardId),
        nameof(TriggerRule.ChannelPointRewardTitle),
        nameof(TriggerRule.RewardSyncMode),
        nameof(TriggerRule.ChatCommandEnabled),
        nameof(TriggerRule.ChatCommandText),
        nameof(TriggerRule.ChatCommandPermission),
        nameof(TriggerRule.MinimumAmount),
        nameof(TriggerRule.AmountScaledDurationEnabled),
        nameof(TriggerRule.AmountUnitsPerDuration),
        nameof(TriggerRule.SecondsPerAmountUnit),
        nameof(TriggerRule.BitsAmountUnitsPerDuration),
        nameof(TriggerRule.BitsSecondsPerAmountUnit),
        nameof(TriggerRule.SubscriptionsAmountUnitsPerDuration),
        nameof(TriggerRule.SubscriptionsSecondsPerAmountUnit),
        nameof(TriggerRule.SubscriptionTier1SecondsPerSub),
        nameof(TriggerRule.SubscriptionTier2SecondsPerSub),
        nameof(TriggerRule.SubscriptionTier3SecondsPerSub),
        nameof(TriggerRule.MaxAccumulatedDurationEnabled),
        nameof(TriggerRule.MaxAccumulatedDurationSeconds),
        nameof(TriggerRule.ActionType),
        nameof(TriggerRule.MovementDirection),
        nameof(TriggerRule.ParameterName),
        nameof(TriggerRule.ParameterType),
        nameof(TriggerRule.IntZeroDurationMode),
        nameof(TriggerRule.ParameterValue),
        nameof(TriggerRule.FloatValueMode),
        nameof(TriggerRule.FloatTransitionInSeconds),
        nameof(TriggerRule.FloatTransitionOutSeconds),
        nameof(TriggerRule.ResetValue),
        nameof(TriggerRule.AvatarChangeTargetId),
        nameof(TriggerRule.AvatarChangeResetId),
        nameof(TriggerRule.AvatarRouletAvatarIds),
        nameof(TriggerRule.AvatarRouletAvatarNames),
        nameof(TriggerRule.RangeMinimum),
        nameof(TriggerRule.RangeMaximum),
        nameof(TriggerRule.DurationSeconds),
        nameof(TriggerRule.CooldownSeconds),
        nameof(TriggerRule.SupporterAvatarProfileId),
        nameof(TriggerRule.SupporterAvatarId),
        nameof(TriggerRule.SupporterAvatarName),
        nameof(TriggerRule.SharedRewardChoiceEnabled),
        nameof(TriggerRule.SharedRewardChoiceNumber),
        nameof(TriggerRule.SharedRewardHelpText),
        nameof(TriggerRule.SupporterKeywordText),
        nameof(TriggerRule.SetTriggerRestoreMode),
        nameof(TriggerRule.ActiveFloatBoostRewardEnabled),
        nameof(TriggerRule.ActiveFloatBoostRewardId),
        nameof(TriggerRule.ActiveFloatBoostRewardTitle),
        nameof(TriggerRule.ActiveFloatBoostRewardDescription),
        nameof(TriggerRule.ActiveFloatBoostRewardCost),
        nameof(TriggerRule.ActiveFloatBoostRewardCooldownSeconds),
        nameof(TriggerRule.ActiveFloatBoostRewardReadyColor),
        nameof(TriggerRule.ActiveFloatBoostRewardCooldownColor),
        nameof(TriggerRule.ActiveFloatBoostAddValue),
        nameof(TriggerRule.ActiveFloatBoostMinimumValue),
        nameof(TriggerRule.ActiveFloatBoostMaximumValue),
        nameof(TriggerRule.SetTriggerActions),
        nameof(TriggerRule.SpecialRulePairingMode),
        nameof(TriggerRule.TemporarilyDisabledRuleIds),
        nameof(TriggerRule.BotMessageTemplate)
    };
    private static readonly HashSet<string> RulePropertiesRequiringManagedRewardSync = new(StringComparer.Ordinal)
    {
        nameof(TriggerRule.ActionType),
        nameof(TriggerRule.ChannelPointRewardId),
        nameof(TriggerRule.ChannelPointRewardTitle),
        nameof(TriggerRule.ChannelPointRewardCost),
        nameof(TriggerRule.ChannelPointRewardDescription),
        nameof(TriggerRule.RewardSyncMode),
        nameof(TriggerRule.ManagedRewardReadyColor),
        nameof(TriggerRule.ManagedRewardCooldownColor),
        nameof(TriggerRule.DeleteManagedRewardWhenInactive),
        nameof(TriggerRule.ActiveFloatBoostRewardEnabled),
        nameof(TriggerRule.ActiveFloatBoostRewardId),
        nameof(TriggerRule.ActiveFloatBoostRewardTitle),
        nameof(TriggerRule.ActiveFloatBoostRewardDescription),
        nameof(TriggerRule.ActiveFloatBoostRewardCost),
        nameof(TriggerRule.ActiveFloatBoostRewardCooldownSeconds),
        nameof(TriggerRule.ActiveFloatBoostRewardReadyColor),
        nameof(TriggerRule.ActiveFloatBoostRewardCooldownColor),
        nameof(TriggerRule.SharedRewardChoiceEnabled),
        nameof(TriggerRule.SharedRewardChoiceNumber),
        nameof(TriggerRule.SharedRewardHelpText),
        nameof(TriggerRule.IsEnabled),
        nameof(TriggerRule.CooldownSeconds),
        nameof(TriggerRule.SpecialRulePairingMode),
        nameof(TriggerRule.TemporarilyDisabledRuleIds)
    };
    private static readonly HashSet<string> AvatarProfilePropertiesRequiringBridgeRefresh = new(StringComparer.Ordinal)
    {
        nameof(AvatarTriggerProfile.IsEnabled),
        nameof(AvatarTriggerProfile.IsMasterProfile),
        nameof(AvatarTriggerProfile.AvatarId),
        nameof(AvatarTriggerProfile.SetTriggerMasterRewardId),
        nameof(AvatarTriggerProfile.SetTriggerMasterRewardTitle),
        nameof(AvatarTriggerProfile.SetTriggerMasterRewardSyncMode),
        nameof(AvatarTriggerProfile.SetTriggerMasterRewardCooldownSeconds),
        nameof(AvatarTriggerProfile.UseSharedNumberedOutfitReward),
        nameof(AvatarTriggerProfile.PostOutfitChoiceListToTwitchChat),
        nameof(AvatarTriggerProfile.UseWardrobeMode),
        nameof(AvatarTriggerProfile.WardrobeCooldownSeconds),
        nameof(AvatarTriggerProfile.WardrobeOutfits),
        nameof(AvatarTriggerProfile.UseWardrobeMasterReward),
        nameof(AvatarTriggerProfile.WardrobeMasterRewardId),
        nameof(AvatarTriggerProfile.WardrobeMasterRewardTitle),
        nameof(AvatarTriggerProfile.WardrobeMasterRewardCost),
        nameof(AvatarTriggerProfile.WardrobeMasterRewardSyncMode),
        nameof(AvatarTriggerProfile.WardrobeMasterRewardCooldownSeconds)
    };
    private static readonly HashSet<string> AvatarProfilePropertiesRequiringManagedRewardSync = new(StringComparer.Ordinal)
    {
        nameof(AvatarTriggerProfile.IsEnabled),
        nameof(AvatarTriggerProfile.IsMasterProfile),
        nameof(AvatarTriggerProfile.AvatarId),
        nameof(AvatarTriggerProfile.IsRewardTestOverrideEnabled),
        nameof(AvatarTriggerProfile.SetTriggerMasterRewardId),
        nameof(AvatarTriggerProfile.SetTriggerMasterRewardTitle),
        nameof(AvatarTriggerProfile.SetTriggerMasterRewardCost),
        nameof(AvatarTriggerProfile.SetTriggerMasterRewardDescription),
        nameof(AvatarTriggerProfile.SetTriggerMasterRewardSyncMode),
        nameof(AvatarTriggerProfile.SetTriggerMasterRewardCooldownSeconds),
        nameof(AvatarTriggerProfile.SetTriggerMasterRewardReadyColor),
        nameof(AvatarTriggerProfile.SetTriggerMasterRewardCooldownColor),
        nameof(AvatarTriggerProfile.DeleteSetTriggerMasterRewardWhenInactive),
        nameof(AvatarTriggerProfile.UseSharedNumberedOutfitReward),
        nameof(AvatarTriggerProfile.UseWardrobeMode),
        nameof(AvatarTriggerProfile.WardrobeOutfits),
        nameof(AvatarTriggerProfile.UseWardrobeMasterReward),
        nameof(AvatarTriggerProfile.WardrobeMasterRewardId),
        nameof(AvatarTriggerProfile.WardrobeMasterRewardTitle),
        nameof(AvatarTriggerProfile.WardrobeMasterRewardCost),
        nameof(AvatarTriggerProfile.WardrobeMasterRewardSyncMode),
        nameof(AvatarTriggerProfile.WardrobeMasterRewardCooldownSeconds),
        nameof(AvatarTriggerProfile.WardrobeMasterRewardReadyColor),
        nameof(AvatarTriggerProfile.WardrobeMasterRewardCooldownColor)
    };
    private static readonly HashSet<string> WardrobeOutfitPropertiesRequiringBridgeRefresh = new(StringComparer.Ordinal)
    {
        nameof(WardrobeOutfit.IsEnabled),
        nameof(WardrobeOutfit.Name),
        nameof(WardrobeOutfit.ActiveTimeSeconds),
        nameof(WardrobeOutfit.TwitchRewardId),
        nameof(WardrobeOutfit.ChatCommandText),
        nameof(WardrobeOutfit.SnapshotParams)
    };
    private static readonly HashSet<string> WardrobeOutfitPropertiesRequiringManagedRewardSync = new(StringComparer.Ordinal)
    {
        nameof(WardrobeOutfit.IsEnabled),
        nameof(WardrobeOutfit.Name),
        nameof(WardrobeOutfit.TwitchRewardId),
        nameof(WardrobeOutfit.TwitchRewardTitle),
        nameof(WardrobeOutfit.TwitchRewardCost),
        nameof(WardrobeOutfit.TwitchRewardDescription),
        nameof(WardrobeOutfit.TwitchRewardSyncMode),
        nameof(WardrobeOutfit.SnapshotParams)
    };
    private static readonly HashSet<string> UniversalTriggerPropertiesRequiringManagedRewardSync = new(StringComparer.Ordinal)
    {
        nameof(UniversalTriggerRule.IsEnabled),
        nameof(UniversalTriggerRule.TriggerType),
        nameof(UniversalTriggerRule.RewardId),
        nameof(UniversalTriggerRule.RewardTitle),
        nameof(UniversalTriggerRule.RewardCost),
        nameof(UniversalTriggerRule.RewardDescription),
        nameof(UniversalTriggerRule.RewardCooldownSeconds),
        nameof(UniversalTriggerRule.RewardSyncMode),
        nameof(UniversalTriggerRule.ManagedRewardReadyColor),
        nameof(UniversalTriggerRule.ManagedRewardCooldownColor),
        nameof(UniversalTriggerRule.DeleteManagedRewardWhenInactive)
    };
    private static readonly HashSet<string> UniversalTriggerActionPropertiesRequiringManagedRewardSync = new(StringComparer.Ordinal)
    {
        nameof(UniversalTriggerAction.OscAddress),
        nameof(UniversalTriggerAction.TargetValue),
        nameof(UniversalTriggerAction.DefaultValue),
        nameof(UniversalTriggerAction.DurationSeconds)
    };
    private static readonly HashSet<string> AvatarScaleRulePropertiesRequiringManagedRewardSync = new(StringComparer.Ordinal)
    {
        nameof(AvatarScaleRule.IsEnabled),
        nameof(AvatarScaleRule.TriggerType),
        nameof(AvatarScaleRule.RewardId),
        nameof(AvatarScaleRule.RewardTitle),
        nameof(AvatarScaleRule.RewardCost),
        nameof(AvatarScaleRule.RewardDescription),
        nameof(AvatarScaleRule.RewardSyncMode),
        nameof(AvatarScaleRule.ManagedRewardReadyColor),
        nameof(AvatarScaleRule.ManagedRewardCooldownColor),
        nameof(AvatarScaleRule.DeleteManagedRewardWhenInactive),
        nameof(AvatarScaleRule.CooldownSeconds),
        nameof(AvatarScaleRule.ScaleMode),
        nameof(AvatarScaleRule.RelativeHeightMeters),
        nameof(AvatarScaleRule.RelativeMinimumHeightMeters),
        nameof(AvatarScaleRule.RelativeMaximumHeightMeters),
        nameof(AvatarScaleRule.HideRewardWhenMinimumHeightReached),
        nameof(AvatarScaleRule.HideRewardWhenMaximumHeightReached),
        nameof(AvatarScaleRule.HeightMultiplier),
        nameof(AvatarScaleRule.TemporarilyDisabledScaleRuleIds)
    };
    private static readonly HashSet<string> AvatarScaleMasterRewardPropertiesRequiringManagedRewardSync = new(StringComparer.Ordinal)
    {
        nameof(AvatarScaleMasterRewardSettings.IsEnabled),
        nameof(AvatarScaleMasterRewardSettings.RewardId),
        nameof(AvatarScaleMasterRewardSettings.RewardTitle),
        nameof(AvatarScaleMasterRewardSettings.RewardCost),
        nameof(AvatarScaleMasterRewardSettings.RewardDescription),
        nameof(AvatarScaleMasterRewardSettings.RewardSyncMode),
        nameof(AvatarScaleMasterRewardSettings.UnlockDurationSeconds),
        nameof(AvatarScaleMasterRewardSettings.CooldownSeconds),
        nameof(AvatarScaleMasterRewardSettings.ManagedRewardReadyColor),
        nameof(AvatarScaleMasterRewardSettings.ManagedRewardCooldownColor),
        nameof(AvatarScaleMasterRewardSettings.DeleteMasterRewardWhenInactive),
        nameof(AvatarScaleMasterRewardSettings.FreeChildRewardSlotsWhenLocked),
        nameof(AvatarScaleMasterRewardSettings.PreventAvatarChangesDuringActiveScaling)
    };
    private static readonly HashSet<string> AvatarProfilePropertiesSkippingSave = new(StringComparer.Ordinal)
    {
        nameof(AvatarTriggerProfile.IsCurrentAvatarActive)
    };
    private static readonly HashSet<string> TwitchAccountPropertiesRequiringBridgeRefresh = new(StringComparer.Ordinal)
    {
        nameof(TwitchAccountSettings.AccessToken),
        nameof(TwitchAccountSettings.RefreshToken),
        nameof(TwitchAccountSettings.UserId),
        nameof(TwitchAccountSettings.Login),
        nameof(TwitchAccountSettings.Scopes)
    };
    private static MediaPlayer? settingsTestAudioPlayer;
    private static string? settingsTestAudioTempPath;
    // Service dependencies used by the window. The view model decides when to call them,
    // while the service classes keep the network, storage, and runtime logic separate.
    private readonly SettingsStore settingsStore = new();
    private readonly RuntimeConfigStore runtimeConfigStore = new();
    private readonly TwitchApiClient twitchApiClient = new();
    private readonly ApplicationUpdateService applicationUpdateService = new();
    private readonly BugReportService bugReportService = new();
    private readonly LiveFeedbackHeartbeatService liveFeedbackHeartbeatService = new();
    private readonly WorldCommandBlacklistService worldCommandBlacklistService = new();
    private readonly VrChatApiClient vrChatApiClient = new();
    private readonly VrChatLocalClientStateService vrChatLocalClientStateService = new();
    private readonly VrChatLocalOscCacheService vrChatLocalOscCacheService = new();
    private readonly Dispatcher dispatcher;
    private readonly DesktopInputLockService desktopInputLockService;
    private readonly BridgeCoordinator bridgeCoordinator;
    public BridgeCoordinator BridgeCoordinator => bridgeCoordinator;
    private readonly SemaphoreSlim bridgeRefreshGate = new(1, 1);
    private readonly SemaphoreSlim managedRewardSyncGate = new(1, 1);
    private readonly SemaphoreSlim vrChatLocalStateRefreshGate = new(1, 1);
    private readonly SemaphoreSlim vrChatCurrentAvatarRefreshGate = new(1, 1);
    private readonly DispatcherTimer sessionStatusTimer;
    private readonly DispatcherTimer vrChatLocalStateTimer;
    private readonly DispatcherTimer vrChatCurrentAvatarTimer;
    private readonly DispatcherTimer worldCommandBlacklistRefreshTimer;
    private readonly Dictionary<Guid, Guid> lastSelectedRuleIdsByAvatarProfileId = new();
    private readonly Dictionary<string, DateTimeOffset> recentChatActivityKeys = new(StringComparer.Ordinal);
    private readonly ObservableCollection<TriggerRule> emptyMasterAvatarRules = [];
    private readonly IActivityResumeService activityResumeService;
    private bool previousSessionWasClean;

    private AppSettings settings = new();
    private RuntimeConfig runtimeConfig = RuntimeConfig.CreateDefault();
    private TriggerRule? selectedRule;
    private MovementRedeemSet? selectedMovementRedeemSet;
    private AvatarScaleSet? selectedAvatarScaleSet;
    private AvatarScaleRule? selectedAvatarScaleRule;
    private PowerUpRule? selectedPowerUpRule;
    private TriggerRule? selectedAvatarRule;
    private AvatarTriggerProfile? selectedAvatarProfile;
    private VrChatOscParameterSummary? selectedAvatarParameterOption;
    private VrChatOscParameterSummary? selectedSetTriggerParameterOption;
    private SetTriggerAction? selectedSetTriggerAction;
    private string copiedAvatarParameterPath = string.Empty;
    private string bridgeStatus = "Waiting for broadcaster login.";
    private string broadcasterStatus = "Broadcaster account not connected.";
    private string botStatus = "Bot account not connected. This is optional.";
    private string runtimeConfigStatus = string.Empty;
    private string oscBridgeSummary = "OSC waiting for setup.";
    private string oscStatusDetail = "OSCQuery is waiting to start.";
    private bool isBroadcasterLive;
    private bool hasResolvedBroadcasterLiveState;
    private string broadcasterExpiryStatus = string.Empty;
    private string botExpiryStatus = string.Empty;
    private string broadcasterDeviceCode = string.Empty;
    private string broadcasterVerificationUri = string.Empty;
    private string botDeviceCode = string.Empty;
    private string botVerificationUri = string.Empty;
    private string vrChatStatus = "VRChat avatar access is not connected.";
    private string vrChatAvatarStatus = "Connect VRChat to load avatar choices.";
    private string vrChatOscParameterStatus = "Pick an avatar set to load its saved OSC parameters.";
    private string chatboxListenerStatus = "Connect broadcaster to start Twitch Chatbox.";
    private string chatboxModerationStatusText = "Select a chat card to moderate.";
    private bool isChatboxModerationDrawerOpen;
    private bool blockedWordsSectionOpen;
    private ObservableCollection<BlockedWordItem> blockedWordItems = [];
    private string newBlockedWordText = string.Empty;
    private TwitchChatMessageEntry? selectedChatMessage;
    private string activityAttentionMessage = string.Empty;
    private SectionView activeSection;
    private SettingsSectionView activeSettingsSection = SettingsSectionView.Twitch;
    private RuleListView activeRuleListView = RuleListView.AvatarTriggers;
    private TwitchChatboxWindow? twitchChatboxWindow;
    private TestModeWindow? testModeWindow;
    private bool isInitialized;
    private bool isRestoringAvatarParameterSelection;
    private bool isRestoringSetTriggerParameterSelection;
    private bool isApplyingMasterAvatarDefaults;
    private bool isRefreshingVrChatAvatarSelectionOptions;
    private bool isSynchronizingManagedRewards;
    private bool isSwitchingRuleView;
    private bool isShuttingDown;
    private bool isRefreshingAboutProfiles;
    private bool isNormalizingChatCommandRules;
    private bool runtimeConfigLoaded;
    private bool broadcasterManagedRewardsUnavailableForSession;
    private bool broadcasterReconnectRequired;
    private bool botReconnectRequired;
    private bool savedLoginRecoveryPromptShownThisRun;
    private bool isStartingSavedLoginRecovery;
    private bool hasActivityAttention;
    private string universalManagedRewardSyncStatusText = "Universal Twitch reward sync has not run yet.";
    private string worldCommandBlacklistStatusText = T("Protected world guard has not been checked yet.");
    private int savedLoginRecoveryFailureCount;
    private CancellationTokenSource? saveDebounceCancellation;
    private CancellationTokenSource? bridgeRefreshCancellation;
    private CancellationTokenSource? worldCommandBlacklistRefreshCancellation;
    private CancellationTokenSource? managedRewardSyncCancellation;
    private CancellationTokenSource? vrChatCurrentAvatarRefreshCancellation;
    private CancellationTokenSource? vrChatOscParameterRefreshCancellation;
    private CancellationTokenSource? vrChatLocalOscScanCancellation;
    private CancellationTokenSource? activeAvatarScaleLocalRefreshCancellation;
    private CancellationTokenSource? rewardFireSaleExpirationCancellation;
    private CancellationTokenSource? rewardFireSaleFundingCooldownCancellation;
    private DateTimeOffset? rewardFireSaleFundingRewardCooldownUntil;
    private int testModeBitsAmount = 100;
    private string testModeBitsMessage = string.Empty;
    private int testModeSubscriptionCount = 1;
    private string testModeSubscriptionTier = "1000";
    private bool testModeSubscriptionIsGift;
    private CashPaymentProvider testModeCashProvider = CashPaymentProvider.StreamElements;
    private decimal testModeCashAmount = 5m;
    private string testModeCashCurrencyCode = "USD";
    private string testModeCashMessage = string.Empty;
    private string testModeSimulationStatusText = T("Choose a simulated event and Crystal Relay will run it through the same matching path as a real stream event.");
    private FileSystemWatcher? vrChatLocalOscWatcher;
    private string? inferredLocalLowUserId;
    private readonly List<VrChatAvatarSummary> availableVrChatAvatars = [];
    private readonly Dictionary<string, string> availableVrChatAvatarNamesById = new(StringComparer.Ordinal);
    private readonly ConcurrentDictionary<string, List<VrChatOscParameterSummary>> cachedVrChatParametersByAvatarId = new(StringComparer.Ordinal);
    private readonly ConcurrentDictionary<string, DateTime> vrChatLocalOscAvatarWriteTimes = new(StringComparer.Ordinal);
    private readonly Dictionary<string, string> chatSuspiciousStatusesByUserId = new(StringComparer.Ordinal);
    private readonly Dictionary<string, string> chatSuspiciousStatusesByLogin = new(StringComparer.OrdinalIgnoreCase);
    private readonly HashSet<string> retiredManagedRewardIds = new(StringComparer.Ordinal);
    private readonly Dictionary<string, DateTimeOffset> managedRewardCreateBackoffByTitle = new(StringComparer.OrdinalIgnoreCase);
    private readonly object avatarScaleLimitStateGate = new();
    private readonly Dictionary<Guid, bool> avatarScaleLimitInactiveStateByRuleId = [];
    private readonly object floatLimitStateGate = new();
    private readonly Dictionary<Guid, (bool MaxReached, bool MinReached)> floatLimitStateByRuleId = [];
    private readonly Dictionary<string, DateTimeOffset> throttledLogExpiryByKey = new(StringComparer.Ordinal);
    private readonly List<ActionTypeOption> allActionTypes;
    private string lastSuccessfulManagedRewardDesiredFingerprint = string.Empty;
    private string lastObservedBroadcasterIdentityFingerprint = string.Empty;
    private string lastObservedBotIdentityFingerprint = string.Empty;
    private int pendingSkippedDeleteSuppressedCount;
    private DateTimeOffset? managedRewardApiBackoffUntil;
    private DateTimeOffset aboutProfilesLastRefreshedAt = DateTimeOffset.MinValue;
    private DateTime latestLocalVrChatAvatarWriteTimeUtc = DateTime.MinValue;
    private string vrChatOutputLogPath = string.Empty;
    private long vrChatOutputLogPosition;
    private string lastDetectedVrChatAvatarId = string.Empty;
    private Guid lastSelectedAvatarProfileId = Guid.Empty;
    private Guid selectedSupporterAvatarProfileId = Guid.Empty;
    private Guid lastSelectedMasterRuleId = Guid.Empty;
    private Guid lastSelectedSupporterRuleId = Guid.Empty;
    private Guid lastSelectedAvatarScaleSetId = Guid.Empty;
    private Guid lastSelectedAvatarScaleRuleId = Guid.Empty;
    private Guid lastSelectedPowerUpRuleId = Guid.Empty;
    private AppLanguage activeLanguageAtStartup = AppLanguage.SystemDefault;
    private ICollectionView? universalTriggersGroupedView;
    private bool isUniversalChatCommandsExpanded;
    private bool isUniversalChannelPointRewardsExpanded;
    private bool isUniversalBitsExpanded;
    private bool isUniversalSubscriptionsExpanded;
    private bool isUniversalGiftSubscriptionsExpanded;
    private bool isUniversalFollowsExpanded;
    private bool isUniversalUnconfiguredExpanded;
    private bool isAvatarBoolRedeemsExpanded;
    private bool isAvatarIntRedeemsExpanded;
    private bool isAvatarFloatRedeemsExpanded;
    private bool isAvatarMixRedeemsExpanded;
    private bool isAvatarOtherRedeemsExpanded;

    private static string T(string sourceText) => LocalizationService.Translate(sourceText);

    private static string TF(string sourceFormat, params object[] args) => LocalizationService.Format(sourceFormat, args);

    private static IReadOnlyList<string> BuildCashPaymentCurrencyCodeOptions()
    {
        var codes = new SortedSet<string>(StringComparer.Ordinal);

        foreach (var code in IsoCurrencyCodeSeeds)
        {
            AddCurrencyCode(code);
        }

        foreach (var culture in CultureInfo.GetCultures(CultureTypes.SpecificCultures))
        {
            try
            {
                AddCurrencyCode(new RegionInfo(culture.Name).ISOCurrencySymbol);
            }
            catch
            {
            }
        }

        var preferredCodes = new[] { "USD", "EUR", "GBP", "CAD", "AUD", "NZD", "JPY", "CHF", "CNY", "HKD" };
        var orderedCodes = new List<string>();
        foreach (var code in preferredCodes)
        {
            if (codes.Remove(code))
            {
                orderedCodes.Add(code);
            }
        }

        orderedCodes.AddRange(codes);
        return orderedCodes;

        void AddCurrencyCode(string? code)
        {
            var normalizedCode = (code ?? string.Empty).Trim().ToUpperInvariant();
            if (normalizedCode.Length == 3 && normalizedCode.All(char.IsLetter))
            {
                codes.Add(normalizedCode);
            }
        }
    }

    // Constructor setup for option lists, status defaults, commands, and About-page profiles.
    public MainWindowViewModel()
    {
        dispatcher = Dispatcher.CurrentDispatcher;
        desktopInputLockService = new DesktopInputLockService(dispatcher);
        activityResumeService = new ActivityResumeService();
        bridgeCoordinator = new BridgeCoordinator(desktopInputLockService, worldCommandBlacklistService, vrChatLocalOscCacheService, activityResumeService);
        LogEntries = [];
        ChatMessages = [];
        ChatActivityEntries = [];
        RewardOptions = [];
        PowerUpOptions = [];
        RewardSyncModeOptions =
        [
            new TwitchRewardSyncModeOption(TwitchRewardSyncMode.CreateOrManage, T("Create/manage reward")),
            new TwitchRewardSyncModeOption(TwitchRewardSyncMode.LinkExisting, T("Link existing Twitch reward"))
        ];
        AvatarRuleProfiles = [];
        AvatarRuleProfiles.CollectionChanged += (_, _) => RaisePropertyChanged(nameof(AvatarSetSummaryText));
        SupporterAvatarScopeOptions = [];
        SupporterRuleAvatarScopeOptions = [];
        ProfileAvatarOptions = [];
        VrChatAvatarOptions = [];
        VrChatResetAvatarOptions = [];
        AvatarParameterOptions = [];
        SetTriggerParameterOptions = [];
        AboutCreatorProfile = new AboutTwitchProfile("screminpal", "screminpal_", "https://www.twitch.tv/screminpal_");
        AboutTesterProfiles =
        [
            new AboutTwitchProfile("Riku Satori", "RikuSatori", "https://www.twitch.tv/RikuSatori"),
            new AboutTwitchProfile("Cuddleshy", "cuddleshyvr", "https://www.twitch.tv/cuddleshyvr"),
            new AboutTwitchProfile("SeidSaga", "njorunn_saga", "https://www.twitch.tv/njorunn_saga"),
            new AboutTwitchProfile("Hydie", "hydie", "https://www.twitch.tv/hydie"),
            new AboutTwitchProfile("SinUsagii", "sinusagii", "https://twitch.tv/sinusagii"),
            new AboutTwitchProfile("Xenstroke", "xenstroke", "https://www.twitch.tv/xenstroke"),
            new AboutTwitchProfile("EzekielTyr", "ezekieltyr", "https://www.twitch.tv/ezekieltyr"),
            new AboutTwitchProfile("TheRaccoonCat", "theraccooncat", "https://www.twitch.tv/theraccooncat")
        ];
        AboutCreatorProfile.PropertyChanged += OnAboutProfilePropertyChanged;
        foreach (var testerProfile in AboutTesterProfiles)
        {
            testerProfile.PropertyChanged += OnAboutProfilePropertyChanged;
        }
        bridgeStatus = T("Waiting for broadcaster login.");
        broadcasterStatus = T("Broadcaster account not connected.");
        botStatus = T("Bot account not connected. This is optional.");
        oscBridgeSummary = T("OSC waiting for setup.");
        oscStatusDetail = T("OSCQuery is waiting to start.");
        vrChatStatus = T("VRChat avatar access is not connected.");
        vrChatAvatarStatus = T("Connect VRChat to load avatar choices.");
        vrChatOscParameterStatus = T("Pick an avatar set to load its saved OSC parameters.");
        chatboxListenerStatus = T("Connect broadcaster to start Twitch Chatbox.");
        allActionTypes =
        [
            new ActionTypeOption(OscActionType.AvatarParameter, T("Avatar Parameter")),
            new ActionTypeOption(OscActionType.SetTrigger, T("Set Trigger")),
            new ActionTypeOption(OscActionType.AvatarChange, T("Avatar Swap")),
            new ActionTypeOption(OscActionType.AvatarRoulet, T("Avatar Roulette")),
            new ActionTypeOption(OscActionType.PlayerMovement, T("Player Movement"))
        ];
        TriggerTypes = Enum.GetValues<TwitchTriggerType>();
        OverrideTriggerTypes =
        [
            TwitchTriggerType.Bits,
            TwitchTriggerType.Subscriptions
        ];
        AvatarScaleTriggerTypes = Enum.GetValues<AvatarScaleTriggerType>();
        AvatarScaleModes =
        [
            new AvatarScaleModeOption(AvatarScaleMode.SetHeight, T("Set Height")),
            new AvatarScaleModeOption(AvatarScaleMode.RandomHeight, T("Random Height")),
            new AvatarScaleModeOption(AvatarScaleMode.GlitchyRandomHeight, T("Glitchy Random Height")),
            new AvatarScaleModeOption(AvatarScaleMode.RelativeHeight, T("Relative Height")),
            new AvatarScaleModeOption(AvatarScaleMode.Multiplier, T("Height Multiplier")),
            new AvatarScaleModeOption(AvatarScaleMode.Preset, T("Preset"))
        ];
        AvatarScalePresets = Enum.GetValues<AvatarScalePreset>();
        AvatarScaleRestoreModes = Enum.GetValues<AvatarScaleRestoreMode>();
        AvatarScaleSubscriptionTierOptions =
        [
            new AvatarScaleSubscriptionTierOption(string.Empty, T("Any tier")),
            new AvatarScaleSubscriptionTierOption("1000", T("Tier 1")),
            new AvatarScaleSubscriptionTierOption("2000", T("Tier 2")),
            new AvatarScaleSubscriptionTierOption("3000", T("Tier 3"))
        ];
        CashPaymentProviderOptions =
        [
            new CashPaymentProviderOption(CashPaymentProvider.StreamElements, "StreamElements"),
            new CashPaymentProviderOption(CashPaymentProvider.Streamlabs, "Streamlabs"),
            new CashPaymentProviderOption(CashPaymentProvider.KoFi, "Ko-fi")
        ];
        ActionTypes = [.. allActionTypes];
        ThemeOptions =
        [
            new ThemeOption(AppTheme.VoidCrystal, "Void Crystal"),
            new ThemeOption(AppTheme.Custom, "Custom"),
            new ThemeOption(AppTheme.TreetendersArm, "Treetender's Arm"),
            new ThemeOption(AppTheme.DreamScape, "Dream Scape"),
            new ThemeOption(AppTheme.MainFrame, "MainFrame"),
            new ThemeOption(AppTheme.TrashKitty, "Trash Kitty"),
            new ThemeOption(AppTheme.Bratwurst, "Bratwurst"),
            new ThemeOption(AppTheme.CarrotPatch, "Carrot Patch"),
            new ThemeOption(AppTheme.Bubblegum, "Bubblegum"),
            new ThemeOption(AppTheme.CosmicPuppyGirl, "Cosmic Puppy Girl"),
            new ThemeOption(AppTheme.PeachesAndCream, "Peaches & Cream"),
            new ThemeOption(AppTheme.MoonBunnyWink, "Moon Bunny Wink"),
            new ThemeOption(AppTheme.DreadNightBar, "Dread Night Bar"),
            new ThemeOption(AppTheme.Baked, "Baked"),
            new ThemeOption(AppTheme.NeonBorb, "Neon Borb"),
            new ThemeOption(AppTheme.StinkyOnline, "Stinky Online"),
            new ThemeOption(AppTheme.SquishyFoxPlush, "Squishy Fox Plush"),
            new ThemeOption(AppTheme.Puca, "Púca")
        ];
        LanguageOptions =
        [
            new AppLanguageOption(AppLanguage.SystemDefault, T("System Default")),
            new AppLanguageOption(AppLanguage.English, "English"),
            new AppLanguageOption(AppLanguage.Spanish, "Español"),
            new AppLanguageOption(AppLanguage.Japanese, "日本語"),
            new AppLanguageOption(AppLanguage.German, "Deutsch"),
            new AppLanguageOption(AppLanguage.French, "Français"),
            new AppLanguageOption(AppLanguage.PortugueseBrazil, "Português (Brasil)"),
            new AppLanguageOption(AppLanguage.Swedish, "Svenska"),
            new AppLanguageOption(AppLanguage.Italian, "Italiano"),
            new AppLanguageOption(AppLanguage.ChineseSimplified, "简体中文"),
            new AppLanguageOption(AppLanguage.ChineseTraditional, "繁體中文"),
            new AppLanguageOption(AppLanguage.Korean, "한국어"),
            new AppLanguageOption(AppLanguage.Russian, "Русский"),
            new AppLanguageOption(AppLanguage.Polish, "Polski"),
            new AppLanguageOption(AppLanguage.Thai, "ไทย")
        ];
        ChatFontOptions =
        [
            "Verdana",
            "Consolas",
            "Segoe UI",
            "Tahoma",
            "Calibri",
            "Trebuchet MS",
            "Cambria",
            "Georgia",
            "Lucida Sans Unicode",
            "Arial"
        ];
        CustomThemeFontOptions = []; // Deferred to InitializeAsync for faster startup.
        ChatTimestampFormatOptions =
        [
            new ChatTimestampFormatOption(ChatTimestampFormat.TwelveHour, T("12-hour")),
            new ChatTimestampFormatOption(ChatTimestampFormat.TwentyFourHour, T("24-hour"))
        ];
        ChatboxOscDelayOptions = [1, 2, 3, 4, 5, 6];
        ThemeManager.UpdateTheme(Settings.Theme, Settings.CustomTheme);
        MovementDirections =
        [
            new PlayerMovementOption(PlayerMovementDirection.Forward, T("Move Forward")),
            new PlayerMovementOption(PlayerMovementDirection.Backward, T("Move Backward")),
            new PlayerMovementOption(PlayerMovementDirection.Left, T("Move Left")),
            new PlayerMovementOption(PlayerMovementDirection.Right, T("Move Right")),
            new PlayerMovementOption(PlayerMovementDirection.Jump, T("Jump")),
            new PlayerMovementOption(PlayerMovementDirection.SpinLeft, T("Spin Left")),
            new PlayerMovementOption(PlayerMovementDirection.SpinRight, T("Spin Right")),
            new PlayerMovementOption(PlayerMovementDirection.RandomMovement, T("Random Movement")),
            new PlayerMovementOption(PlayerMovementDirection.GlitchyMovement, T("Glitchy Movement"))
        ];
        ChatCommandPermissionOptions =
        [
            new ChatCommandPermissionOption(ChatCommandPermission.Everyone, T("Everyone")),
            new ChatCommandPermissionOption(ChatCommandPermission.Moderators, T("Moderators + Broadcaster")),
            new ChatCommandPermissionOption(ChatCommandPermission.Broadcaster, T("Broadcaster Only"))
        ];
        FloatValueModes = Enum.GetValues<FloatValueMode>();
        IntZeroDurationModes = Enum.GetValues<IntZeroDurationMode>();
        sessionStatusTimer = new DispatcherTimer
        {
            Interval = TimeSpan.FromSeconds(30)
        };
        sessionStatusTimer.Tick += (_, _) =>
        {
            if (isInitialized)
            {
                UpdateAccountStatuses();
                if (DateTimeOffset.UtcNow - aboutProfilesLastRefreshedAt >= AboutProfileRefreshInterval)
                {
                    _ = RefreshAboutProfilesAsync();
                }
            }
        };
        sessionStatusTimer.Start();
        vrChatLocalStateTimer = new DispatcherTimer
        {
            Interval = VrChatLocalStatePollInterval
        };
        vrChatLocalStateTimer.Tick += (_, _) =>
        {
            if (isInitialized)
            {
                QueueCurrentVrChatLocalStateRefresh();
            }
        };
        vrChatLocalStateTimer.Start();
        vrChatCurrentAvatarTimer = new DispatcherTimer
        {
            Interval = VrChatCurrentAvatarPollInterval
        };
        vrChatCurrentAvatarTimer.Tick += (_, _) =>
        {
            if (isInitialized)
            {
                QueueCurrentVrChatAvatarRefresh();
            }
        };
        vrChatCurrentAvatarTimer.Start();
        worldCommandBlacklistRefreshTimer = new DispatcherTimer
        {
            Interval = WorldCommandBlacklistRefreshInterval
        };
        worldCommandBlacklistRefreshTimer.Tick += (_, _) =>
        {
            if (isInitialized)
            {
                QueueWorldCommandBlacklistRefresh();
            }
        };
        worldCommandBlacklistRefreshTimer.Start();

        ConnectBroadcasterCommand = new AsyncRelayCommand(ConnectBroadcasterAsync);
        DisconnectBroadcasterCommand = new AsyncRelayCommand(DisconnectBroadcasterAsync, () => HasRecoverableBroadcasterSession);
        OpenBroadcasterLoginCommand = new RelayCommand(() => OpenUri(BroadcasterVerificationUri), () => !string.IsNullOrWhiteSpace(BroadcasterVerificationUri));

        ConnectBotCommand = new AsyncRelayCommand(ConnectBotAsync);
        DisconnectBotCommand = new AsyncRelayCommand(DisconnectBotAsync, () => Settings.Bot.IsConnected);
        OpenBroadcasterAuthPageCommand = new AsyncRelayCommand(OpenOrAuthenticateBroadcasterAsync);
        OpenBotAuthPageCommand = new AsyncRelayCommand(OpenOrAuthenticateBotAsync);
        OpenBotLoginCommand = new RelayCommand(OpenBotAuthPage);
        ConnectVrChatCommand = new AsyncRelayCommand(ConnectVrChatAsync);
        DisconnectVrChatCommand = new AsyncRelayCommand(DisconnectVrChatAsync, () => Settings.VrChat.IsConnected);
        RefreshVrChatAvatarsCommand = new AsyncRelayCommand(RefreshVrChatAvatarsAsync, CanRefreshVrChatAvatars);
        ClearVrChatCacheCommand = new AsyncRelayCommand(ClearVrChatCacheAsync, () => availableVrChatAvatars.Count > 0 || HasPersistedVrChatCache());
        OpenRuntimeConfigCommand = new RelayCommand(OpenRuntimeConfigFile);
        OpenRuntimeConfigFolderCommand = new RelayCommand(OpenRuntimeConfigFolder);
        OpenTwitchDeveloperConsoleCommand = new RelayCommand(OpenTwitchDeveloperConsole);
        OpenSaveFolderCommand = new RelayCommand(OpenSaveFolder);
        RepairSavedLoginStateCommand = new AsyncRelayCommand(RepairSavedLoginStateAsync, () => !isStartingSavedLoginRecovery);
        OpenKoFiSupportCommand = new RelayCommand(OpenKoFiSupportPage);
        OpenKoFiWebhooksCommand = new RelayCommand(OpenKoFiWebhooksPage);
        OpenDiscordInviteCommand = new RelayCommand(OpenDiscordInvite);
        OpenBugReportCommand = new AsyncRelayCommand(() => OpenBugReportAsync());
        RefreshTwitchRewardsCommand = new AsyncRelayCommand(RefreshTwitchRewardsAsync);
        RefreshPowerUpsCommand = new AsyncRelayCommand(RefreshPowerUpsAsync);
        UnlinkTwitchRewardCommand = new RelayCommand(UnlinkTwitchReward);
        UnlinkWardrobeMasterRewardCommand = new RelayCommand(UnlinkWardrobeMasterReward);
        TestSelectedRuleCommand = new AsyncRelayCommand(TestSelectedRuleAsync, () => SelectedRule is not null);
        SimulateTestModeBitsCommand = new AsyncRelayCommand(SimulateTestModeBitsAsync);
        SimulateTestModeSubscriptionCommand = new AsyncRelayCommand(SimulateTestModeSubscriptionAsync);
        SimulateTestModeCashPaymentCommand = new AsyncRelayCommand(SimulateTestModeCashPaymentAsync);
        ShowSettingsTestCommand = new RelayCommand(ShowSettingsTestPopup);
        ShowHomeSectionCommand = new RelayCommand(() => SetActiveSection(SectionView.Home));
        ShowSettingsSectionCommand = new RelayCommand(() => SetActiveSection(SectionView.Settings));
        ShowActivitySectionCommand = new RelayCommand(ShowActivitySection);
        ShowAboutSectionCommand = new RelayCommand(() => SetActiveSection(SectionView.About));
        OpenTestModeWindowCommand = new RelayCommand(OpenTestModeWindow);
        OpenTwitchChatboxCommand = new RelayCommand(OpenTwitchChatbox);
        ToggleChatboxModerationDrawerCommand = new RelayCommand(ToggleChatboxModerationDrawer);
        AddBlockedWordCommand = new RelayCommand(AddBlockedWord, () => !string.IsNullOrWhiteSpace(NewBlockedWordText));
        RemoveBlockedWordCommand = new RelayCommand(p => RemoveBlockedWord(p as BlockedWordItem));
        RestoreBlockedWordCommand = new RelayCommand(p => RestoreBlockedWord(p as BlockedWordItem));
        TimeoutSelectedChatUser10SecondsCommand = new AsyncRelayCommand(() => TimeoutSelectedChatUserAsync(10), CanTimeoutSelectedChatUser);
        TimeoutSelectedChatUser1MinuteCommand = new AsyncRelayCommand(() => TimeoutSelectedChatUserAsync(60), CanTimeoutSelectedChatUser);
        TimeoutSelectedChatUser5MinutesCommand = new AsyncRelayCommand(() => TimeoutSelectedChatUserAsync(300), CanTimeoutSelectedChatUser);
        TimeoutSelectedChatUser10MinutesCommand = new AsyncRelayCommand(() => TimeoutSelectedChatUserAsync(600), CanTimeoutSelectedChatUser);
        TimeoutSelectedChatUser30MinutesCommand = new AsyncRelayCommand(() => TimeoutSelectedChatUserAsync(1800), CanTimeoutSelectedChatUser);
        TimeoutSelectedChatUser1HourCommand = new AsyncRelayCommand(() => TimeoutSelectedChatUserAsync(3600), CanTimeoutSelectedChatUser);
        BanSelectedChatUserCommand = new AsyncRelayCommand(BanSelectedChatUserAsync, CanTimeoutSelectedChatUser);
        PurgeSelectedChatUserCommand = new AsyncRelayCommand(PurgeSelectedChatUserAsync, CanTimeoutSelectedChatUser);
        DeleteSelectedChatMessageCommand = new AsyncRelayCommand(DeleteSelectedChatMessageAsync, CanDeleteSelectedChatMessage);
        MarkSelectedChatUserSuspiciousCommand = new AsyncRelayCommand(MarkSelectedChatUserSuspiciousAsync, CanManageSelectedChatUserSuspiciousStatus);
        RestrictSelectedChatUserCommand = new AsyncRelayCommand(RestrictSelectedChatUserAsync, CanManageSelectedChatUserSuspiciousStatus);
        ClearSelectedChatUserSuspiciousStatusCommand = new AsyncRelayCommand(ClearSelectedChatUserSuspiciousStatusAsync, CanManageSelectedChatUserSuspiciousStatus);
        OpenBuiltInCommandsCommand = new RelayCommand(OpenBuiltInCommands);
        RefreshWorldCommandBlacklistCommand = new AsyncRelayCommand(RefreshWorldCommandBlacklistManuallyAsync);
        DismissMigrationNoticeCommand = new RelayCommand(DismissMigrationNotice);
        DismissCashPaymentMigrationNoticeCommand = new RelayCommand(DismissCashPaymentMigrationNotice);
        DismissUiUpdateNoticeCommand = new RelayCommand(DismissUiUpdateNotice);
        ShowSettingsTwitchSectionCommand = new RelayCommand(() => SetActiveSettingsSection(SettingsSectionView.Twitch));
        ShowSettingsVrChatSectionCommand = new RelayCommand(() => SetActiveSettingsSection(SettingsSectionView.VrChat));
        ShowSettingsAppSectionCommand = new RelayCommand(() => SetActiveSettingsSection(SettingsSectionView.App));
        ShowSettingsVisualsSectionCommand = new RelayCommand(() => SetActiveSettingsSection(SettingsSectionView.Visuals));
        ShowSettingsSafetySectionCommand = new RelayCommand(() => SetActiveSettingsSection(SettingsSectionView.Safety));
        ShowAvatarTriggerRulesCommand = new RelayCommand(ShowAvatarTriggerRules);
        ShowMovementRedeemsCommand = new RelayCommand(OpenMovementRedeemsManager);
        ShowPowerUpsCommand = new RelayCommand(ShowPowerUps);
        OpenUniversalTriggersManagerCommand = new RelayCommand(OpenUniversalTriggersManager);
        OpenAvatarScalingManagerCommand = new RelayCommand(OpenAvatarScalingManager);
        OpenAvatarSetsManagerCommand = new RelayCommand(OpenAvatarSetsManager);
        OpenAvatarSwapManagerCommand = new RelayCommand(OpenAvatarSwapManager);
        OpenCashPaymentManagerCommand = new RelayCommand(OpenCashPaymentManager);
        OpenRewardFireSaleManagerCommand = new RelayCommand(OpenRewardFireSaleManager);
        PickReturnAvatarCommand = new RelayCommand(PickReturnAvatar);
        UseCurrentAvatarForReturnCommand = new RelayCommand(UseCurrentAvatarForReturn);
        ClearReturnAvatarCommand = new RelayCommand(ClearReturnAvatar);
        ShowAvatarScalingCommand = new RelayCommand(ShowAvatarScaling);

        AddAvatarProfileCommand = new RelayCommand(AddAvatarProfile);
        DeleteSelectedAvatarProfileCommand = new RelayCommand(DeleteSelectedAvatarProfile, () => SelectedAvatarProfile is not null);
        DeleteAllAvatarProfilesCommand = new RelayCommand(DeleteAllAvatarProfiles, () => AvatarRuleProfiles.Count > 0);
        SetSelectedAvatarProfileAsMasterCommand = new RelayCommand(SetSelectedAvatarProfileAsMaster, () => SelectedAvatarProfile is not null);
        ToggleSelectedAvatarRewardTestOverrideCommand = new RelayCommand(ToggleSelectedAvatarRewardTestOverride);
        ToggleEmergencyRedeemStopCommand = new RelayCommand(ToggleEmergencyRedeemStop);
        ToggleDesktopModeInputLockCommand = new RelayCommand(ToggleDesktopModeInputLock);
        UseCurrentVrChatAvatarForProfileCommand = new RelayCommand(
            UseCurrentVrChatAvatarForProfile,
            () => SelectedAvatarProfile is not null && !string.IsNullOrWhiteSpace(GetResolvedCurrentVrChatAvatarId()));
        OpenAvatarPickerCommand = new RelayCommand(OpenAvatarPicker);
        AddRuleCommand = new RelayCommand(AddRule);
        AddOutfitChoiceCommand = new RelayCommand(AddOutfitChoice, () => IsViewingAvatarTriggers && SelectedAvatarProfile is not null);
        RemoveSelectedOutfitChoiceCommand = new RelayCommand(
            RemoveSelectedOutfitChoice,
            () => IsViewingAvatarTriggers && SelectedAvatarProfile is not null && SelectedRule?.ActionType == OscActionType.SetTrigger);

        SelectRuleCommand = new RelayCommand(SelectRule, target => target is TriggerRule);
        AddAvatarSupporterTriggerCommand = new RelayCommand(AddAvatarSupporterTrigger);
        AddForceMovementOverrideCommand = new RelayCommand(AddForceMovementOverride);
        RemoveSelectedRuleCommand = new RelayCommand(RemoveSelectedRule, () => SelectedRule is not null);
        EnableAllRulesCommand = new RelayCommand(EnableAllRules, () => GetCurrentEditableRuleCollection().Count > 0);
        DisableAllRulesCommand = new RelayCommand(DisableAllRules, () => GetCurrentEditableRuleCollection().Count > 0);
        DeleteAllRulesCommand = new RelayCommand(DeleteAllRules, () => GetCurrentEditableRuleCollection().Count > 0);
        AddAvatarScaleSetCommand = new RelayCommand(AddAvatarScaleSet);
        RemoveSelectedAvatarScaleSetCommand = new RelayCommand(RemoveSelectedAvatarScaleSet, () => SelectedAvatarScaleSet is not null);
        AddAvatarScaleRuleCommand = new RelayCommand(AddAvatarScaleRule);
        AddRewardGrowthCommand = new RelayCommand(AddRewardGrowth);
        RemoveSelectedAvatarScaleRuleCommand = new RelayCommand(RemoveSelectedAvatarScaleRule, () => SelectedAvatarScaleRule is not null);
        EnableAllAvatarScaleRulesCommand = new RelayCommand(EnableAllAvatarScaleRules, () => GetAllAvatarScaleRules().Count > 0);
        DisableAllAvatarScaleRulesCommand = new RelayCommand(DisableAllAvatarScaleRules, () => GetAllAvatarScaleRules().Count > 0);
        DeleteAllAvatarScaleRulesCommand = new RelayCommand(DeleteAllAvatarScaleSets, () => Settings.AvatarScaleSets.Count > 0);
        TestSelectedAvatarScaleRuleCommand = new RelayCommand(StartSelectedAvatarScaleRuleTest, CanTestSelectedAvatarScaleRule);
        OpenAvatarScaleRuleLockoutPickerCommand = new RelayCommand(OpenAvatarScaleRuleLockoutPicker, CanOpenAvatarScaleRuleLockoutPicker);
        AddAvatarScalingCashPaymentRuleCommand = new RelayCommand(AddAvatarScalingCashPaymentRule);
        AddPowerUpRuleCommand = new RelayCommand(AddPowerUpRule);
        AddAvatarScalingPowerUpRuleCommand = new RelayCommand(AddAvatarScalingPowerUpRule);
        RemoveSelectedPowerUpRuleCommand = new RelayCommand(RemoveSelectedPowerUpRule, () => SelectedPowerUpRule is not null);
        EnableAllPowerUpRulesCommand = new RelayCommand(EnableAllPowerUpRules, () => Settings.PowerUpRules.Count > 0);
        DisableAllPowerUpRulesCommand = new RelayCommand(DisableAllPowerUpRules, () => Settings.PowerUpRules.Count > 0);
        DeleteAllPowerUpRulesCommand = new RelayCommand(DeleteAllPowerUpRules, () => Settings.PowerUpRules.Count > 0);
        TestSelectedPowerUpRuleCommand = new AsyncRelayCommand(TestSelectedPowerUpRuleAsync, () => SelectedPowerUpRule is not null);
        UnlinkPowerUpCommand = new RelayCommand(UnlinkPowerUp, target => target is PowerUpRule);
        UseCurrentAvatarForPowerUpRuleCommand = new RelayCommand(
            UseCurrentAvatarForPowerUpRule,
            () => SelectedPowerUpRule is not null && !string.IsNullOrWhiteSpace(GetResolvedCurrentVrChatAvatarId()));
        RegenerateKoFiRelayIdentityCommand = new RelayCommand(RegenerateKoFiRelayIdentity);
        OpenSpecialRuleLockoutPickerCommand = new RelayCommand(OpenSpecialRuleLockoutPicker, CanOpenSpecialRuleLockoutPicker);
        OpenAvatarRouletPoolPickerCommand = new RelayCommand(OpenAvatarRouletPoolPicker, CanOpenAvatarRouletPoolPicker);
        OpenActiveFloatBoostRewardCommand = new RelayCommand(OpenActiveFloatBoostReward, CanOpenActiveFloatBoostReward);
        AddSetTriggerActionCommand = new RelayCommand(AddSetTriggerAction, () => SelectedRule?.ActionType == OscActionType.SetTrigger);
        RemoveSelectedSetTriggerActionCommand = new RelayCommand(RemoveSelectedSetTriggerAction, () => SelectedRule?.ActionType == OscActionType.SetTrigger && SelectedSetTriggerAction is not null);
        CopySelectedAvatarParameterPathCommand = new RelayCommand(CopySelectedAvatarParameterPath, CanCopySelectedAvatarParameterPath);
        PasteSelectedAvatarParameterPathCommand = new RelayCommand(PasteSelectedAvatarParameterPath, CanPasteSelectedAvatarParameterPath);

        bridgeCoordinator.LogWritten += message => RunOnUi(() =>
        {
            UpdateOscStatusFromLog(message);
            AppendLog(message);
        });
        bridgeCoordinator.StatusChanged += status => RunOnUi(() =>
        {
            BridgeStatus = status;
            DebugLogService.Write($"Bridge status: {status}");
        });
        bridgeCoordinator.AccountUpdated += (role, snapshot) => RunOnUi(() =>
        {
            var previousIdentityFingerprint = BuildAccountIdentityFingerprint(role);
            ApplyAccountSnapshot(role, snapshot);
            UpdateAccountStatuses();
            QueueSave();
            if (HasAccountIdentityChanged(role, previousIdentityFingerprint))
            {
                _ = QueueRewardRefreshAsync();
                QueueManagedRewardSync(0, ManagedRewardSyncReason.AccountReconnect);
            }
            _ = RefreshAboutProfilesAsync();
        });
        bridgeCoordinator.ChatMessageReceived += message => RunOnUi(() => AppendChatMessage(message));
        bridgeCoordinator.ChatActivityReceived += activity => RunOnUi(() => AppendChatActivity(activity));
        bridgeCoordinator.VrChatAvatarChanged += avatarId => RunOnUi(() => HandleVrChatAvatarChangedByBridge(avatarId));
        bridgeCoordinator.SharedReturnAvatarChanged += (avatarId, avatarName) => RunOnUi(() => HandleSharedReturnAvatarChangedByBridge(avatarId, avatarName));
        bridgeCoordinator.StreamStateChanged += (isLive, streamEnded) => RunOnUi(() => HandleBroadcasterLiveStateChanged(isLive, streamEnded));
        bridgeCoordinator.ManagedRewardAvailabilityChanged += () => RunOnUi(() =>
        {
            RaisePropertyChanged(nameof(AvatarScaleMasterRewardStatusText));
            // Rule lockout state (disable pairing), avatar-scale active/inactive, and
            // supporter-override windows are Twitch-visible state flips that need to reach
            // the reward sync. Queue a passive sync so the fingerprint check can decide
            // whether a Twitch PATCH is actually needed.
            QueueManagedRewardSync(1100, ManagedRewardSyncReason.RuntimeAvailability);
        });
        bridgeCoordinator.RewardCooldownColorChanged += ruleId => _ = HandleRewardCooldownColorChangedAsync(ruleId);
        bridgeCoordinator.AvatarScaleMasterRewardUnlockStateChanged += () => RunOnUi(() =>
        {
            // The master reward's unlock window just opened or closed, which is a
            // Twitch-visible change for the child avatar-scale rewards: hidden while
            // locked, visible while unlocked. Queue a managed-reward sync so the
            // child rewards' desiredEnabled state actually reaches Twitch.
            QueueManagedRewardSync(0, ManagedRewardSyncReason.AvatarScaleMasterRewardUnlocked);
        });
        bridgeCoordinator.AvatarScaleStatusChanged += () => RunOnUi(HandleAvatarScaleStatusChanged);
        bridgeCoordinator.FloatLimitStatusChanged += () => RunOnUi(HandleFloatLimitStatusChanged);
        bridgeCoordinator.RewardFireSaleContributionReceived += contribution => RunOnUi(() => HandleRewardFireSaleContribution(contribution));
        bridgeCoordinator.DevFireSaleRequested += request => RunOnUi(() => HandleDevFireSaleRequest(request));
        bridgeCoordinator.PauseCommandRequested += () => RunOnUi(() => ToggleEmergencyRedeemStop());
        bridgeCoordinator.GroupToggleRequested += groupName => RunOnUi(() => ToggleRedeemGroupByName(groupName));
        bridgeCoordinator.RedeemControlRequested += (redeemName, enable) => RunOnUi(() => ToggleRedeemByName(redeemName, enable));
        bridgeCoordinator.VrChatOscAvatarChangeReceived += avatarId => RunOnUi(() => HandleIncomingOscAvatarChangeSync(avatarId));
        liveFeedbackHeartbeatService.DiagnosticLogged += message => RunOnUi(() => AppendLog(message));

        LoadingService.DefinePhases(
            ("settings", "Loading Settings"),
            ("vrchat", "Connecting to VRChat"),
            ("twitch", "Syncing Twitch Rewards"),
            ("bridge", "Starting OSC Bridge"),
            ("finalizing", "Finalizing")
        );
        RecomputeVrChatConnectionState();
    }

    public LoadingPhaseService LoadingService { get; } = new();

    public AppSettings Settings
    {
        get => settings;
        private set => SetProperty(ref settings, value);
    }

    internal bool IsApplicationSelfUpdateSupported => !IsTestBuild;

    public ObservableCollection<string> LogEntries { get; }

    public ObservableCollection<TwitchChatMessageEntry> ChatMessages { get; }

    public ObservableCollection<TwitchChatActivityEntry> ChatActivityEntries { get; }

    public ObservableCollection<TwitchRewardOption> RewardOptions { get; }

    public ObservableCollection<TwitchPowerUpOption> PowerUpOptions { get; }

    public IReadOnlyList<TwitchRewardSyncModeOption> PowerUpSourceModeOptions { get; } =
    [
        new TwitchRewardSyncModeOption(TwitchRewardSyncMode.LinkExisting, T("Link existing Power Up")),
        new TwitchRewardSyncModeOption(TwitchRewardSyncMode.CreateOrManage, T("Create/manage when Twitch supports it"))
    ];

    public IReadOnlyList<PowerUpActionKindOption> PowerUpActionKindOptions { get; } =
    [
        new PowerUpActionKindOption(PowerUpActionKind.TriggerAction, T("Trigger Action")),
        new PowerUpActionKindOption(PowerUpActionKind.AvatarScaling, T("Avatar Scaling"))
    ];

    public IReadOnlyList<TwitchRewardSyncModeOption> RewardSyncModeOptions { get; }

    public ObservableCollection<AvatarTriggerProfile> AvatarRuleProfiles { get; }

    public ObservableCollection<AvatarProfileScopeOption> SupporterAvatarScopeOptions { get; }

    public ObservableCollection<AvatarProfileScopeOption> SupporterRuleAvatarScopeOptions { get; }

    public ObservableCollection<TriggerRule> MasterAvatarRules => MasterAvatarProfile?.ChannelPointRules ?? emptyMasterAvatarRules;

    public ObservableCollection<VrChatAvatarOption> ProfileAvatarOptions { get; }

    public ObservableCollection<VrChatAvatarOption> VrChatAvatarOptions { get; }

    public ObservableCollection<VrChatAvatarOption> VrChatResetAvatarOptions { get; }

    public ObservableCollection<VrChatOscParameterSummary> AvatarParameterOptions { get; }

    public ObservableCollection<VrChatOscParameterSummary> SetTriggerParameterOptions { get; }

    public Guid SelectedSupporterAvatarProfileId
    {
        get => selectedSupporterAvatarProfileId;
        set
        {
            if (SetProperty(ref selectedSupporterAvatarProfileId, value))
            {
                RaiseSupporterRuleGroupProperties();
                AddAvatarSupporterTriggerCommand.NotifyCanExecuteChanged();
                AddForceMovementOverrideCommand.NotifyCanExecuteChanged();
            }
        }
    }

    public AboutTwitchProfile AboutCreatorProfile { get; }

    public ObservableCollection<AboutTwitchProfile> AboutTesterProfiles { get; }

    public AvatarTriggerProfile? SelectedAvatarProfile
    {
        get => selectedAvatarProfile;
        set
        {
            if (SetProperty(ref selectedAvatarProfile, value))
            {
                if (value is not null && !value.IsMasterProfile)
                {
                    lastSelectedAvatarProfileId = value.Id;
                }

                if (isSwitchingRuleView)
                {
                    return;
                }

                RaiseRuleSelectionStateProperties();
                AddOutfitChoiceCommand.NotifyCanExecuteChanged();
                RemoveSelectedOutfitChoiceCommand.NotifyCanExecuteChanged();
                RefreshSpecialRuleLockoutOptions();
                RefreshVrChatAvatarSelectionOptions();
                _ = EnsureSelectedAvatarParameterCacheLoadedAsync();
                SelectedRule = value is null ? null : GetRememberedRuleForProfile(value);

                if (Settings.ChannelPointRewardTestModeEnabled && !IsBroadcasterLive)
                {
                    QueueManagedRewardSync(0, ManagedRewardSyncReason.TestMode);
                }
            }
        }
    }

    public VrChatOscParameterSummary? SelectedAvatarParameterOption
    {
        get => selectedAvatarParameterOption;
        set
        {
            if (SetProperty(ref selectedAvatarParameterOption, value)
                && !isRestoringAvatarParameterSelection
                && SelectedRule?.ActionType == OscActionType.AvatarParameter
                && value is not null)
            {
                if (SelectedRule.ParameterType != value.ParameterType)
                {
                    SelectedRule.ParameterType = value.ParameterType;
                }

                SelectedRule.ParameterName = value.Address;
                RefreshAvatarParameterPathCommandStates();
            }
        }
    }

    public VrChatOscParameterSummary? SelectedSetTriggerParameterOption
    {
        get => selectedSetTriggerParameterOption;
        set
        {
            if (SetProperty(ref selectedSetTriggerParameterOption, value)
                && !isRestoringSetTriggerParameterSelection
                && SelectedRule?.ActionType == OscActionType.SetTrigger
                && SelectedSetTriggerAction is not null
                && value is not null)
            {
                SelectedSetTriggerAction.ParameterName = value.Address;
                RefreshAvatarParameterPathCommandStates();
            }
        }
    }

    public SetTriggerAction? SelectedSetTriggerAction
    {
        get => selectedSetTriggerAction;
        set
        {
            if (SetProperty(ref selectedSetTriggerAction, value))
            {
                RemoveSelectedSetTriggerActionCommand.NotifyCanExecuteChanged();
                RefreshSetTriggerParameterOptions();
                RefreshAvatarParameterPathCommandStates();
            }
        }
    }

    public IReadOnlyList<TwitchTriggerType> TriggerTypes { get; }

    public IReadOnlyList<TwitchTriggerType> OverrideTriggerTypes { get; }

    public IReadOnlyList<TwitchTriggerType> AvailableOverrideTriggerTypesForSelectedRule => OverrideTriggerTypes;

    public IReadOnlyList<AvatarScaleTriggerType> AvatarScaleTriggerTypes { get; }

    public IReadOnlyList<AvatarScaleTriggerType> AvailableAvatarScaleTriggerTypesForSelectedRule
    {
        get
        {
            var selectedTriggerType = SelectedAvatarScaleRule?.TriggerType;
            if (selectedTriggerType is { } triggerType
                && LegacyPaidAvatarScaleTriggerTypes.Contains(triggerType))
            {
                return [.. PrimaryAvatarScaleTriggerTypes, triggerType];
            }

            return PrimaryAvatarScaleTriggerTypes;
        }
    }

    public IReadOnlyList<AvatarScaleModeOption> AvatarScaleModes { get; }

    public IReadOnlyList<AvatarScalePreset> AvatarScalePresets { get; }

    public IReadOnlyList<AvatarScaleRestoreMode> AvatarScaleRestoreModes { get; }

    public IReadOnlyList<string> CashPaymentCurrencyCodeOptions { get; private set; } = [];

    public IReadOnlyList<CashPaymentProviderOption> CashPaymentProviderOptions { get; }

    public IReadOnlyList<AvatarScaleSubscriptionTierOption> AvatarScaleSubscriptionTierOptions { get; }

    public int TestModeBitsAmount
    {
        get => testModeBitsAmount;
        set => SetProperty(ref testModeBitsAmount, Math.Max(1, value));
    }

    public string TestModeBitsMessage
    {
        get => testModeBitsMessage;
        set => SetProperty(ref testModeBitsMessage, value ?? string.Empty);
    }

    public int TestModeSubscriptionCount
    {
        get => testModeSubscriptionCount;
        set => SetProperty(ref testModeSubscriptionCount, Math.Max(1, value));
    }

    public string TestModeSubscriptionTier
    {
        get => testModeSubscriptionTier;
        set => SetProperty(ref testModeSubscriptionTier, value ?? string.Empty);
    }

    public bool TestModeSubscriptionIsGift
    {
        get => testModeSubscriptionIsGift;
        set => SetProperty(ref testModeSubscriptionIsGift, value);
    }

    public CashPaymentProvider TestModeCashProvider
    {
        get => testModeCashProvider;
        set => SetProperty(ref testModeCashProvider, Enum.IsDefined(value) ? value : CashPaymentProvider.StreamElements);
    }

    public decimal TestModeCashAmount
    {
        get => testModeCashAmount;
        set => SetProperty(ref testModeCashAmount, Math.Max(0m, value));
    }

    public string TestModeCashCurrencyCode
    {
        get => testModeCashCurrencyCode;
        set => SetProperty(ref testModeCashCurrencyCode, string.IsNullOrWhiteSpace(value) ? "USD" : value.Trim().ToUpperInvariant());
    }

    public string TestModeCashMessage
    {
        get => testModeCashMessage;
        set => SetProperty(ref testModeCashMessage, value ?? string.Empty);
    }

    public string TestModeSimulationStatusText
    {
        get => testModeSimulationStatusText;
        private set => SetProperty(ref testModeSimulationStatusText, value);
    }

    public IReadOnlyList<ActionTypeOption> ActionTypes { get; }

    public IReadOnlyList<ThemeOption> ThemeOptions { get; }

    public IReadOnlyList<AppLanguageOption> LanguageOptions { get; }

    public IReadOnlyList<string> ChatFontOptions { get; }

    public IReadOnlyList<string> CustomThemeFontOptions { get; private set; } = [];

    public IReadOnlyList<ChatTimestampFormatOption> ChatTimestampFormatOptions { get; }

    public ChatTimestampFormatOption SelectedChatTimestampFormatOption
    {
        get => ChatTimestampFormatOptions.FirstOrDefault(option => option.Value == Settings.ChatTimestampFormat)
            ?? ChatTimestampFormatOptions.First(option => option.Value == ChatTimestampFormat.TwentyFourHour);
        set
        {
            if (value is null || Settings.ChatTimestampFormat == value.Value)
            {
                return;
            }

            Settings.ChatTimestampFormat = value.Value;
            RaisePropertyChanged();
        }
    }

    public IReadOnlyList<int> ChatboxOscDelayOptions { get; }

    public IReadOnlyList<PlayerMovementOption> MovementDirections { get; }

    public IReadOnlyList<ChatCommandPermissionOption> ChatCommandPermissionOptions { get; }

    public IReadOnlyList<OscParameterType>? ParameterTypes { get; }

    public IReadOnlyList<FloatValueMode> FloatValueModes { get; }

    public IReadOnlyList<IntZeroDurationMode> IntZeroDurationModes { get; }

    public IReadOnlyList<string>? BoolValueOptions { get; }

    public AppLanguageOption SelectedLanguageOption
    {
        get => LanguageOptions.FirstOrDefault(option => option.Value == Settings.Language)
            ?? LanguageOptions.First(option => option.Value == AppLanguage.SystemDefault);
        set
        {
            if (value is null || Settings.Language == value.Value)
            {
                return;
            }

            Settings.Language = value.Value;
            RaisePropertyChanged();
            RaisePropertyChanged(nameof(IsLanguageRestartNoticeVisible));
            RaisePropertyChanged(nameof(LanguageRestartNoticeText));
        }
    }

    public string WindowTitle => TF("Crystal Relay v{0} | Twitch to OSC", GetAppVersionDisplay());

    public string WindowHeaderSubtitle => TF("Twitch to OSC | v{0}", GetAppVersionDisplay());

    public string HomeVersionDisplay
    {
        get
        {
            var display = $"v{AppVersion}";
            if (!string.IsNullOrWhiteSpace(BetaBuildLabel))
            {
                display += $" {BetaBuildLabel}";
            }
            return display;
        }
    }

    public bool HasBetaBuildLabel => AppBuildIdentity.HasBetaLabel;
    public bool HasBugFixBuildLabel => AppBuildIdentity.HasBugFixLabel;
    public string BugFixBuildBadgeText => AppBuildIdentity.HasBugFixLabel
        ? TF("BUG FIX {0}", AppBuildIdentity.BugFixSequence)
        : string.Empty;

    public bool IsLanguageRestartNoticeVisible => Settings.Language != activeLanguageAtStartup;

    public string LanguageRestartNoticeText => T("Language changes apply after you restart Crystal Relay.");

    public bool ShowMigrationNotice =>
        !Settings.AvatarSwapMigrationNoticeShown
        && Settings.AvatarChangeToAvatarSwapMigrationVersion >= AvatarSwapMigrationService.CurrentMigrationVersion;

    public bool ShowCashPaymentMigrationNotice =>
        !Settings.CashPaymentMigrationNoticeShown;

    public bool ShowUiUpdateNotice =>
        !Settings.UiUpdateNoticeShown;

    public string UiOpacityStatusText => TF("UI Opacity: {0}%", Settings.InterfaceOpacityPercent);

    public AppTheme SelectedTheme
    {
        get => Settings.Theme;
        set
        {
            if (value == AppTheme.Custom && !Settings.CustomTheme.IsInitialized)
            {
                var sourceTheme = Settings.Theme == AppTheme.Custom
                    ? AppTheme.VoidCrystal
                    : Settings.Theme;
                Settings.CustomTheme = ThemeManager.CreateSeededCustomTheme(sourceTheme);
            }

            if (Settings.Theme == value)
            {
                return;
            }

            Settings.Theme = value;
        }
    }

    public string RuntimeConfigPath => runtimeConfigStore.ConfigPath;

    public string PortableSaveFolderPath => settingsStore.PortableProfileFolderPath;

    public string AppDataFolderPath => settingsStore.RootFolderPath;

    private bool BroadcasterCanManageRewards => HasScope(Settings.Broadcaster, TwitchScopes.RewardManagement);

    private bool HasRecoverableBroadcasterSession => HasRecoverableBroadcasterAccount(Settings.Broadcaster);

    private static bool HasRecoverableBroadcasterAccount(TwitchAccountSettings account)
    {
        return account.IsConnected || !string.IsNullOrWhiteSpace(account.RefreshToken);
    }

    private static bool HasRecoverableBroadcasterAccount(BroadcasterRewardAccountSnapshot account)
    {
        return account.IsConnected || !string.IsNullOrWhiteSpace(account.RefreshToken);
    }

    private bool BroadcasterRewardManagementScopeKnownMissing =>
        Settings.Broadcaster.IsConnected
        && Settings.Broadcaster.Scopes.Count > 0
        && !BroadcasterCanManageRewards;

    public bool IsViewingAvatarTriggers => activeRuleListView == RuleListView.AvatarTriggers;

    public bool IsViewingMasterAvatar => activeRuleListView == RuleListView.MasterAvatar;

    public bool IsViewingMovementRedeems => activeRuleListView == RuleListView.MovementRedeems;

    public bool IsViewingPowerUps => activeRuleListView == RuleListView.PowerUps;

    public bool IsViewingAvatarScaling => activeRuleListView == RuleListView.AvatarScaling;

    private string BuildRewardFireSaleFundingRewardPrompt()
    {
        var configuredDescription = Settings.RewardFireSale.FundingRewardDescription?.Trim() ?? string.Empty;
        return string.IsNullOrWhiteSpace(configuredDescription)
            ? TF(
                "Adds {0:N0} Fire Sale progress toward the next discount goal.",
                GetRewardFireSaleFundingProgressPerRedeem())
            : configuredDescription;
    }

    public IReadOnlyList<TriggerRule> SelectedAvatarSupporterRules => Settings.GlobalOverrideRules
        .Where(rule => !IsSupporterAvatarChangeOverride(rule) && !IsSupporterForceMovementOverride(rule))
        .ToArray();

    public IReadOnlyList<TriggerRule> ForceMovementOverrideRules => Settings.GlobalOverrideRules
        .Where(IsSupporterForceMovementOverride)
        .ToArray();

    public IReadOnlyList<TriggerRule> GlobalSupporterRules => [];

    public bool HasSelectedAvatarSupporterRules => SelectedAvatarSupporterRules.Count > 0;

    public bool HasForceMovementOverrideRules => ForceMovementOverrideRules.Count > 0;

    public bool HasGlobalSupporterRules => GlobalSupporterRules.Count > 0;

    public IReadOnlyList<MovementRedeemSet> MovementRedeemSets => Settings.MovementRedeemSets.ToArray();

    public IReadOnlyList<AvatarScaleSet> AvatarScaleSets => Settings.AvatarScaleSets.ToArray();

    public IReadOnlyList<AvatarScaleRule> AvatarScaleRules => SelectedAvatarScaleSet?.ScaleRules.ToArray() ?? [];

    public IReadOnlyList<UniversalTriggerRule> UniversalChatCommandTriggers => GetUniversalTriggersByType(UniversalTriggerType.ChatCommand);

    public IReadOnlyList<UniversalTriggerRule> UniversalChannelPointRewardTriggers => GetUniversalTriggersByType(UniversalTriggerType.ChannelPointReward);

    public IReadOnlyList<UniversalTriggerRule> UniversalBitsTriggers => GetUniversalTriggersByType(UniversalTriggerType.Bits);

    public IReadOnlyList<UniversalTriggerRule> UniversalSubscriptionTriggers => GetUniversalTriggersByType(UniversalTriggerType.Subscription);

    public IReadOnlyList<UniversalTriggerRule> UniversalGiftSubscriptionTriggers => GetUniversalTriggersByType(UniversalTriggerType.GiftSubscription);

    public IReadOnlyList<UniversalTriggerRule> UniversalFollowTriggers => GetUniversalTriggersByType(UniversalTriggerType.Follow);

    public IReadOnlyList<UniversalTriggerRule> UniversalUnconfiguredTriggers => Settings.UniversalTriggers
        .Where(trigger => !trigger.IsConfigured)
        .OrderBy(trigger => trigger.DisplayTitle, StringComparer.CurrentCultureIgnoreCase)
        .ToArray();

    public ICollectionView UniversalTriggersGroupedView => universalTriggersGroupedView ??= CreateUniversalTriggersGroupedView();

    public IReadOnlyList<TriggerRule> SelectedAvatarBoolRedeems => GetSelectedAvatarRedeemsByParameterType(OscParameterType.Bool);

    public IReadOnlyList<TriggerRule> SelectedAvatarIntRedeems => GetSelectedAvatarRedeemsByParameterType(OscParameterType.Int);

    public IReadOnlyList<TriggerRule> SelectedAvatarFloatRedeems => GetSelectedAvatarRedeemsByParameterType(OscParameterType.Float);

    public IReadOnlyList<TriggerRule> SelectedAvatarMixRedeems => GetSelectedAvatarMixRedeems();

    public IReadOnlyList<TriggerRule> SelectedAvatarOutfitChoices => GetSelectedAvatarMixRedeems();

    public IReadOnlyList<TriggerRule> SelectedAvatarOtherRedeems => GetSelectedAvatarOtherRedeems();

    public string AvatarBoolRedeemGroupTitle => "Bool Parameters";

    public string AvatarIntRedeemGroupTitle => "Int Parameters";

    public string AvatarFloatRedeemGroupTitle => "Float Parameters";

    public string AvatarMixRedeemGroupTitle => T("Mix Parameters");

    public string AvatarOtherRedeemGroupTitle => "Other Redeems";

    public bool HasAvatarMixRedeems => SelectedAvatarMixRedeems.Count > 0;

    public bool HasSelectedAvatarOutfitChoices => SelectedAvatarOutfitChoices.Count > 0;

    public bool HasAvatarOtherRedeems => SelectedAvatarOtherRedeems.Count > 0;

    public bool IsSetTriggerMasterRewardEditorVisible =>
        IsViewingAvatarTriggers
        && SelectedAvatarProfile?.UseSharedNumberedOutfitReward == true
        && SelectedAvatarProfile.ChannelPointRules.Any(rule => rule.ActionType == OscActionType.SetTrigger);

    public bool SelectedSetTriggerUsesSharedNumberedReward =>
        IsViewingAvatarTriggers
        && SelectedRule?.ActionType == OscActionType.SetTrigger
        && SelectedAvatarProfile?.UseSharedNumberedOutfitReward == true;

    public bool IsAvatarBoolRedeemsExpanded
    {
        get => isAvatarBoolRedeemsExpanded;
        set => SetProperty(ref isAvatarBoolRedeemsExpanded, value);
    }

    public bool IsAvatarIntRedeemsExpanded
    {
        get => isAvatarIntRedeemsExpanded;
        set => SetProperty(ref isAvatarIntRedeemsExpanded, value);
    }

    public bool IsAvatarFloatRedeemsExpanded
    {
        get => isAvatarFloatRedeemsExpanded;
        set => SetProperty(ref isAvatarFloatRedeemsExpanded, value);
    }

    public bool IsAvatarMixRedeemsExpanded
    {
        get => isAvatarMixRedeemsExpanded;
        set => SetProperty(ref isAvatarMixRedeemsExpanded, value);
    }

    public bool IsAvatarOtherRedeemsExpanded
    {
        get => isAvatarOtherRedeemsExpanded;
        set => SetProperty(ref isAvatarOtherRedeemsExpanded, value);
    }

    public string UniversalChatCommandGroupTitle => "Chat Commands";

    public string UniversalChannelPointRewardGroupTitle => "Channel Point Rewards";

    public string UniversalBitsGroupTitle => "Bits";

    public string UniversalSubscriptionGroupTitle => "Subscriptions";

    public string UniversalGiftSubscriptionGroupTitle => "Gift Subscriptions";

    public string UniversalFollowGroupTitle => "Follows";

    public string UniversalUnconfiguredGroupTitle => T("Unconfigured");

    public bool IsUniversalUnconfiguredExpanded
    {
        get => isUniversalUnconfiguredExpanded;
        set => SetProperty(ref isUniversalUnconfiguredExpanded, value);
    }

    public bool IsUniversalChatCommandsExpanded
    {
        get => isUniversalChatCommandsExpanded;
        set => SetProperty(ref isUniversalChatCommandsExpanded, value);
    }

    public bool IsUniversalChannelPointRewardsExpanded
    {
        get => isUniversalChannelPointRewardsExpanded;
        set => SetProperty(ref isUniversalChannelPointRewardsExpanded, value);
    }

    public bool IsUniversalBitsExpanded
    {
        get => isUniversalBitsExpanded;
        set => SetProperty(ref isUniversalBitsExpanded, value);
    }

    public bool IsUniversalSubscriptionsExpanded
    {
        get => isUniversalSubscriptionsExpanded;
        set => SetProperty(ref isUniversalSubscriptionsExpanded, value);
    }

    public bool IsUniversalGiftSubscriptionsExpanded
    {
        get => isUniversalGiftSubscriptionsExpanded;
        set => SetProperty(ref isUniversalGiftSubscriptionsExpanded, value);
    }

    public bool IsUniversalFollowsExpanded
    {
        get => isUniversalFollowsExpanded;
        set => SetProperty(ref isUniversalFollowsExpanded, value);
    }

    public AvatarTriggerProfile? MasterAvatarProfile =>
        Settings.AvatarProfiles.FirstOrDefault(profile => profile.IsMasterProfile)
        ?? Settings.AvatarProfiles.FirstOrDefault();

    public string SelectedRuleCollectionTitle => IsViewingPowerUps
        ? T("Power Up")
        : IsViewingAvatarScaling
        ? T("Avatar Scaling")
        : IsViewingMovementRedeems
            ? T("Movement Sets")
        : IsViewingMasterAvatar
            ? T("Avatar Change Redeems")
            : T("Avatar Redeems");

    public string SelectedRuleCollectionHelpText => IsViewingPowerUps
        ? T("Link Twitch Custom Power-ups paid with Bits, then choose the Crystal Relay action each Power Up should run. Linked Power Ups are listen-only in this beta build.")
        : IsViewingAvatarScaling
        ? T("Use Scale Sets to organize VRChat OSC avatar height scaling. Scale redeems send /avatar/eyeheight and stay separate from avatar sets, movement, universal triggers, and paid overrides.")
        : IsViewingMovementRedeems
            ? T("Use Movement Sets to organize global movement redeems. The sets are folders only; every movement redeem still works across every avatar and keeps its existing Twitch reward link.")
        : IsViewingMasterAvatar
            ? T("Use this list for avatar-switch rules that belong to Avatar Change Setup. Add Avatar Switch creates a direct Avatar Change rule or an Avatar Roulette rule, Delete Avatar Switch removes the selected one, and Enable All or Disable All controls the full avatar-switch list. These rules only turn on while you are on the shared return avatar unless a timed avatar switch is already active.")
        : SelectedAvatarProfile is null
            ? T("Use Avatar Sets to build per-avatar rule groups. Add Avatar Set creates another avatar group, Delete Avatar Set removes the selected one, and Delete All Avatar Sets clears the full set list. Each set becomes active only when Crystal Relay detects that exact avatar.")
            : T("This list holds the rules for one avatar set. Pick the avatar once, then add and manage the redeems that should only turn on while you are using that avatar.");

    public string RuleLibraryHelpText => IsViewingPowerUps
            ? T("This tab is for Twitch Custom Power-ups. Power Ups use Bits, stay separate from normal cheers, and can run OSC, avatar, movement, Set Trigger, or Avatar Scaling actions.")
        : IsViewingAvatarScaling
        ? T("This tab is for avatar height scale redeems using VRChat OSC Avatar Scaling. Use Scale Sets to keep different height reward ideas organized without changing how the triggers run.")
        : IsViewingMovementRedeems
            ? T("This tab is for organizing global movement redeems like forward, back, left, right, and spin. Movement Sets do not add avatar matching; they only keep the movement library easier to manage.")
            : IsViewingMasterAvatar
                ? T("This tab is for Avatar Change Setup. Pick the shared return avatar on the right, then build direct avatar swaps or Avatar Roulette rules here. Timed avatar-switch rules return to that shared return avatar when they finish.")
                : T("This tab is for Avatar Sets. Use it to group redeems by the avatar they belong to, then pick a set below to edit the rules inside it. Crystal Relay uses current-avatar detection so only the set for the avatar you are actually wearing turns on.");

    public string AddRuleButtonText => IsViewingPowerUps
        ? T("Add Power Up")
        : IsViewingAvatarScaling
        ? T("Add Scale Redeem")
        : IsViewingMovementRedeems
            ? T("Add Movement Redeem")
        : IsViewingMasterAvatar
            ? T("Add Avatar Switch")
            : T("Add Redeem");

    public string DeleteRuleButtonText => IsViewingPowerUps
        ? T("Delete Power Up")
        : IsViewingAvatarScaling
        ? T("Delete Scale Redeem")
        : IsViewingMovementRedeems
            ? T("Delete Movement Redeem")
        : IsViewingMasterAvatar
            ? T("Delete Avatar Switch")
            : T("Delete Redeem");

    public string DeleteAllRulesButtonText => IsViewingPowerUps
        ? T("Delete All Power Ups")
        : IsViewingAvatarScaling
        ? T("Delete All Scale Sets")
        : IsViewingMovementRedeems
            ? T("Delete All Movement Sets")
        : IsViewingMasterAvatar
            ? T("Delete All Avatar Switches")
            : T("Delete All Redeems");

    public string ManagedChannelPointRewardHelpText
    {
        get
        {
            if (!Settings.Broadcaster.IsConnected)
            {
                return T("Connect your broadcaster account if you want Crystal Relay to create, rename, enable, disable, and cooldown-sync this redeem on Twitch automatically. Without that broadcaster connection, the rule can still use Chat Command Fallback if you turn it on below, but Crystal Relay cannot manage a Twitch channel point redeem for you.");
            }

            if (BroadcasterRewardManagementScopeKnownMissing)
            {
                return T("Reconnect your broadcaster account once so Crystal Relay gets the Twitch permissions it needs to manage channel point redeems for you. That reconnect lets the app create the redeem, keep its enabled state in sync, and update its cooldown on Twitch.");
            }

            if (broadcasterManagedRewardsUnavailableForSession)
            {
                return T("Twitch only allows managed channel point redeems for affiliate or partner broadcasters. If your channel does not have that access, Crystal Relay cannot create this reward on Twitch for you, but the rule can still work through Chat Command Fallback if you enable it below.");
            }

            if (Settings.EmergencyRedeemStopEnabled)
            {
                return T("Redeems are paused right now. Crystal Relay is intentionally keeping managed Twitch redeems turned off so viewers cannot fire them until you resume.");
            }

            if (Settings.ChannelPointRewardTestModeEnabled)
            {
                return T("Test Mode is on. Crystal Relay still keeps managed redeems in sync for testing, but avatar-based rules continue following your current detected avatar instead of turning every avatar set on at once.");
            }

            if (IsViewingMovementRedeems)
            {
                return T("Crystal Relay will create this redeem on Twitch as a global movement rule. Movement redeems are not tied to one avatar set, so they stay available across every avatar unless you pause redeems.");
            }

            if (IsViewingMasterAvatar)
            {
                return T("Crystal Relay will create this redeem on Twitch and only turn it on while you are using the shared return avatar. That keeps avatar-switch redeems hidden while you are already on a temporary or different avatar. Timed Avatar Change and Avatar Roulette rules switch back to the shared return avatar when they finish.");
            }

            if (SelectedAvatarProfile is null || string.IsNullOrWhiteSpace(SelectedAvatarProfile.AvatarId))
            {
                return T("Pick the avatar for this set first. Crystal Relay only turns this redeem on while you are using that exact avatar, so it needs the set linked before it can manage the Twitch redeem correctly.");
            }

            return TF("Crystal Relay will create this redeem on Twitch and only turn it on while you are using {0}. When you switch away, Crystal Relay turns the managed Twitch redeem off again so viewers only see rules that belong to the avatar you are actually using.", SelectedAvatarProfile.AvatarDisplayName);
        }
    }

    public string UniversalManagedChannelPointRewardHelpText
    {
        get
        {
            if (!Settings.Broadcaster.IsConnected)
            {
                return "Connect your broadcaster account if you want Crystal Relay to create, adopt, enable, disable, and update this Universal Trigger reward on Twitch automatically.";
            }

            if (BroadcasterRewardManagementScopeKnownMissing)
            {
                return "Reconnect your broadcaster account once so Crystal Relay gets the Twitch permissions it needs to manage Universal Trigger channel point rewards.";
            }

            if (broadcasterManagedRewardsUnavailableForSession)
            {
                return "Twitch only allows managed channel point redeems for affiliate or partner broadcasters. The Universal Trigger can still be configured, but Crystal Relay cannot create this reward on Twitch for this account.";
            }

            if (Settings.EmergencyRedeemStopEnabled)
            {
                return "Redeems are paused right now. Crystal Relay is intentionally keeping managed Twitch redeems turned off until you resume.";
            }

            return T("Crystal Relay will create or link this Twitch redeem and keep it available in Test Mode or while live when the current avatar local OSC file has at least one matching avatar parameter action.");
        }
    }

    public string AvatarScaleRuntimeStatusText
    {
        get
        {
            var status = bridgeCoordinator.GetAvatarScaleRuntimeStatus();
            var current = status.CurrentHeightMeters is null ? "unknown" : $"{status.CurrentHeightMeters.Value:0.###}m";
            var minimum = status.MinimumHeightMeters is null ? "unknown" : $"{status.MinimumHeightMeters.Value:0.###}m";
            var maximum = status.MaximumHeightMeters is null ? "unknown" : $"{status.MaximumHeightMeters.Value:0.###}m";
            var allowed = status.ScalingAllowed is null ? "unknown" : status.ScalingAllowed.Value ? "allowed" : "blocked";
            var selected = SelectedAvatarScaleRule is null
                ? "Add or select a scale redeem to edit it."
                : SelectedAvatarScaleRule.ScaleRangeHelpText;
            return $"Current height: {current} | Min: {minimum} | Max: {maximum} | Scaling: {allowed}{Environment.NewLine}{selected}";
        }
    }

    public double CurrentAvatarHeightMeters
    {
        get
        {
            var status = bridgeCoordinator.GetAvatarScaleRuntimeStatus();
            return status.CurrentHeightMeters ?? 1.6;
        }
    }

    public string AvatarScaleMasterRewardStatusText
    {
        get
        {
            var master = Settings.AvatarScaleMasterReward;
            if (!master.IsEnabled)
            {
                return "Master Reward is off. Avatar Scaling channel-point rewards use their normal visibility behavior.";
            }

            if (string.IsNullOrWhiteSpace(master.RewardTitle))
            {
                return "Set a master reward name so Crystal Relay can create or link the Twitch redeem.";
            }

            if (!Settings.Broadcaster.IsConnected)
            {
                return "Connect the broadcaster account before Crystal Relay can manage the master reward on Twitch.";
            }

            if (BroadcasterRewardManagementScopeKnownMissing)
            {
                return "Reconnect the broadcaster account with channel-point reward management permission.";
            }

            if (!IsBroadcasterLive && !Settings.ChannelPointRewardTestModeEnabled)
            {
                return "Master Reward is linked but stays hidden until Test Mode is on or the stream is live.";
            }

            if (Settings.EmergencyRedeemStopEnabled)
            {
                return "Master Reward is paused while Pause Redeems is on.";
            }

            if (bridgeCoordinator.IsAvatarScaleMasterUnlockActive())
            {
                return "Avatar Scaling rewards are unlocked. Redeeming the master reward again extends the unlock timer.";
            }

            if (bridgeCoordinator.IsAvatarScaleMasterRewardOnCooldown())
            {
                return "Master Reward is cooling down. Child scale rewards stay locked unless the unlock timer is still active.";
            }

            return "Master Reward is ready. Child scale rewards stay hidden until this reward is redeemed.";
        }
    }

    public string PowerUpRuleStatusText
    {
        get
        {
            var rule = SelectedPowerUpRule;
            if (rule is null)
            {
                return T("Add or select a Power Up rule to link a Twitch Custom Power-up and choose its action.");
            }

            if (rule.UsesManagedPowerUpPlaceholder)
            {
                return T("Twitch has not documented Custom Power-up create/edit price APIs yet. This beta build keeps managed Power Up controls unavailable.");
            }

            if (string.IsNullOrWhiteSpace(rule.PowerUpId) && string.IsNullOrWhiteSpace(rule.PowerUpTitle))
            {
                return T("Link an existing Twitch Custom Power-up before this rule can match redemptions.");
            }

            return TF("{0} listens for {1:N0}-Bit Power Up redemptions. Linked Power Ups are never edited by Crystal Relay.", rule.DisplayTitle, Math.Max(1, rule.BitsCost));
        }
    }

    public string PowerUpActionEditorHelpText => SelectedPowerUpRule?.UsesAvatarScaling == true
        ? T("This Power Up runs an Avatar Scaling action from a Bits-paid Custom Power-up redemption.")
        : T("This Power Up runs the same OSC, Set Trigger, Avatar Change, Avatar Roulette, or movement actions as normal redeems, but it stays separate from Bits cheers.");

    private string WithUniversalManagedRewardSyncStatus(string status)
    {
        var syncStatus = universalManagedRewardSyncStatusText.Trim();
        return string.IsNullOrWhiteSpace(syncStatus)
            ? status
            : $"{status}{Environment.NewLine}{syncStatus}";
    }

    public string ChatCommandFallbackHelpText
    {
        get
        {
            if (!Settings.Broadcaster.IsConnected)
            {
                return T("Connect your broadcaster account first. Crystal Relay reads command fallback from the broadcaster chat listener, so it cannot watch for Twitch chat commands until that connection is active.");
            }

            if (!HasBroadcasterChatScope())
            {
                return T("Reconnect your broadcaster once so Crystal Relay gets Twitch chat read access. Command fallback uses the same live chat listener as the built-in chat features, and without that scope the rule cannot listen for commands.");
            }

            return T("Turn this on if you want this rule to also work from an exact Twitch chat command, even when channel points are unavailable. Crystal Relay matches the whole message, ignores letter case, adds ! automatically if you leave it out, and then checks the permission setting before firing the same rule.");
        }
    }

    public string ManagedRewardColorsDescriptionText => T("These colors control the Twitch reward background Crystal Relay applies while this redeem is ready and while it is on cooldown.");

    public bool IsSpecialRuleLockoutEditorVisible => IsViewingAvatarTriggers && SelectedRule is not null;

    public string SpecialRuleLockoutHelpText => T("Pairing can either hide sibling redeems while this redeem is on cooldown, or keep sibling redeems hidden until this redeem triggers.");

    public string SpecialRuleLockoutSummaryText
    {
        get
        {
            var configuredOptions = BuildConfiguredSpecialRuleLockoutOptions();
            if (configuredOptions.Count == 0)
            {
                return SelectedRule?.SpecialRulePairingMode == SpecialRulePairingMode.ShowPairedWhileActive
                    ? T("No reveal pairings set.")
                    : T("No disable pairings set.");
            }

            if (SelectedRule?.SpecialRulePairingMode == SpecialRulePairingMode.ShowPairedWhileActive)
            {
                return configuredOptions.Count == 1
                    ? TF("Reveal pairing: {0}", configuredOptions[0].Label)
                    : TF("Reveal pairings: {0}", configuredOptions.Count);
            }

            return configuredOptions.Count == 1
                ? TF("Disable pairing: {0}", configuredOptions[0].Label)
                : TF("Disable pairings: {0}", configuredOptions.Count);
        }
    }

    public string AvatarScaleRuleLockoutHelpText => T("Disable Pairing lets this scale redeem temporarily turn off other scale redeems in the same scale set while it is active. Use it when two height effects would fight each other or should behave like separate modes instead of stacking together.");

    public string AvatarScaleRuleLockoutSummaryText
    {
        get
        {
            var configuredOptions = BuildConfiguredAvatarScaleRuleLockoutOptions();
            if (configuredOptions.Count == 0)
            {
                return T("No disable pairings set.");
            }

            return configuredOptions.Count == 1
                ? TF("Disable pairing: {0}", configuredOptions[0].Label)
                : TF("Disable pairings: {0}", configuredOptions.Count);
        }
    }

    public IReadOnlyList<ActionTypeOption> AvailableActionTypesForSelectedContext
    {
        get
        {
            if (IsViewingMasterAvatar)
            {
                return GetActionTypeOptionsForSelectedContext(option => option.Value is OscActionType.AvatarChange or OscActionType.AvatarRoulet);
            }

            if (IsViewingMovementRedeems)
            {
                return GetActionTypeOptionsForSelectedContext(option => option.Value == OscActionType.PlayerMovement);
            }

            if (IsViewingPowerUps)
            {
                return GetActionTypeOptionsForSelectedContext(option =>
                    option.Value is OscActionType.AvatarParameter
                        or OscActionType.SetTrigger
                        or OscActionType.AvatarChange
                        or OscActionType.AvatarRoulet
                        or OscActionType.PlayerMovement);
            }

            if (IsViewingAvatarTriggers)
            {
                var allowSetTrigger = SelectedRule?.SharedRewardChoiceEnabled == true;
                return GetActionTypeOptionsForSelectedContext(option =>
                    option.Value == OscActionType.AvatarParameter
                    || (allowSetTrigger && option.Value == OscActionType.SetTrigger));
            }

            return GetActionTypeOptionsForSelectedContext(option => option.Value == OscActionType.AvatarParameter);
        }
    }

    public ActionTypeOption? SelectedActionTypeOption
    {
        get
        {
            var actionType = SelectedRule?.ActionType;
            if (actionType is null)
            {
                return null;
            }

            return AvailableActionTypesForSelectedContext.FirstOrDefault(option => option.Value == actionType.Value)
                ?? ActionTypes.FirstOrDefault(option => option.Value == actionType.Value);
        }
        set
        {
            if (SelectedRule is null || value is null)
            {
                return;
            }

            if (SelectedRule.ActionType != value.Value)
            {
                SelectedRule.ActionType = value.Value;
            }

            RaisePropertyChanged(nameof(SelectedActionTypeOption));
        }
    }

    private IReadOnlyList<ActionTypeOption> GetActionTypeOptionsForSelectedContext(Func<ActionTypeOption, bool> isAllowed)
    {
        var currentActionType = SelectedRule?.ActionType;
        return ActionTypes
            .Where(option => isAllowed(option) || option.Value == currentActionType)
            .ToArray();
    }

    private IReadOnlyList<ActionTypeOption> GetSupporterActionTypeOptionsForSelectedRule()
    {
        if (SelectedRule is null)
        {
            return GetActionTypeOptionsForSelectedContext(option => option.Value == OscActionType.AvatarParameter);
        }

        if (IsSupporterAvatarChangeOverride(SelectedRule))
        {
            return GetActionTypeOptionsForSelectedContext(option =>
                option.Value is OscActionType.AvatarChange or OscActionType.AvatarRoulet);
        }

        if (IsSupporterForceMovementOverride(SelectedRule))
        {
            return GetActionTypeOptionsForSelectedContext(option => option.Value == OscActionType.PlayerMovement);
        }

        if (!string.IsNullOrWhiteSpace(SelectedRule.SupporterAvatarId))
        {
            var allowBitsSetTrigger = SelectedRule.TriggerType == TwitchTriggerType.Bits
                || SelectedRule.ActionType == OscActionType.SetTrigger;
            return GetActionTypeOptionsForSelectedContext(option =>
                option.Value == OscActionType.AvatarParameter
                || (allowBitsSetTrigger && option.Value == OscActionType.SetTrigger));
        }

        return GetActionTypeOptionsForSelectedContext(option => option.Value == OscActionType.AvatarParameter);
    }

    public string SelectedAvatarProfileStatusText
    {
        get
        {
            if (SelectedAvatarProfile is null)
            {
                if (IsViewingAvatarScaling)
                {
                    return T("Avatar Scaling redeems send VRChat's /avatar/eyeheight OSC value and are not tied to one avatar set.");
                }

                if (IsViewingMovementRedeems)
                {
                    return T("Build global movement redeems here. They work the same on every avatar.");
                }

                return IsViewingMasterAvatar
                    ? T("Pick the avatar you want to return to after timed avatar-change redeems finish.")
                    : T("Create or select an avatar set to start building redeems for one avatar.");
            }

            if (IsViewingMasterAvatar)
            {
                if (Settings.AvatarChangeCooldownOnlyModeEnabled)
                {
                    return T("Cooldown-only Avatar Change mode is enabled. Direct Avatar Change redeems do not use the Return Avatar card; they hide the avatar you are already wearing and use cooldown to control when other avatar-change rewards return.");
                }

                if (string.IsNullOrWhiteSpace(SelectedAvatarProfile.AvatarId))
                {
                    return T("Pick the Return Avatar first. Timed Avatar Change and Avatar Roulette redeems switch back to this exact VRChat avatar ID when they finish.");
                }

                return SelectedAvatarProfile.IsCurrentAvatarActive
                    ? TF("Return Avatar is {0}, and you are using it right now. Timed avatar switches will come back here when they finish.", SelectedAvatarProfile.AvatarDisplayName)
                    : TF("Return Avatar is {0}. Avatar Change and Avatar Roulette redeems turn on while you are on this exact avatar, and timed switches return here when they finish.", SelectedAvatarProfile.AvatarDisplayName);
            }

            if (string.IsNullOrWhiteSpace(SelectedAvatarProfile.AvatarId))
            {
                return T("Pick the avatar these redeems belong to.");
            }

            if (Settings.EmergencyRedeemStopEnabled)
            {
                return T("Redeems are paused right now. Resume them when you are ready to let Twitch redeems run again.");
            }

            if (Settings.ChannelPointRewardTestModeEnabled)
            {
                return T("Test Mode is on. These redeems still follow their normal avatar checks instead of turning every set on at once.");
            }

            if (SelectedAvatarProfile.IsCurrentAvatarActive)
            {
                return TF("You are currently using {0}. These redeems are live.", SelectedAvatarProfile.AvatarDisplayName);
            }

            return TF("These redeems stay off until you switch to {0}.", SelectedAvatarProfile.AvatarDisplayName);
        }
    }

    public string CurrentVrChatAvatarDisplayName
    {
        get
        {
            var currentAvatarId = Settings.VrChat.CurrentAvatarId?.Trim() ?? string.Empty;
            if (string.IsNullOrWhiteSpace(currentAvatarId))
            {
                return T("Unknown");
            }

            var resolvedName = ResolveVrChatAvatarName(currentAvatarId);
            return string.IsNullOrWhiteSpace(resolvedName) ? T("Unknown") : resolvedName;
        }
    }

    public string CurrentVrChatAvatarStatusText => TF("Current avatar: {0}", CurrentVrChatAvatarDisplayName);

    public string MasterAvatarDisplayName => MasterAvatarProfile?.AvatarDisplayName ?? T("Pick return avatar");

    public string SelectedAvatarSetupTitle => IsViewingMasterAvatar ? T("Avatar Change Setup") : T("Avatar Set Setup");

    public string AvatarSetSummaryText => TF("Avatar Sets Summary Format", AvatarRuleProfiles?.Count ?? 0, AvatarRuleProfiles?.Count(p => p.IsEnabled) ?? 0);

    public string SelectedAvatarNameFieldLabel => T("Display Name");

    public string SelectedAvatarPickerLabel => IsViewingMasterAvatar
        ? T("Return Avatar")
        : T("This Avatar");

    public string UseCurrentAvatarButtonText => IsViewingMasterAvatar
        ? T("Use Current Avatar as Return Avatar")
        : T("Use Current VRChat Avatar");

    public string MasterAvatarReturnText => string.IsNullOrWhiteSpace(MasterAvatarProfile?.AvatarId)
        ? T("Pick the Return Avatar first so timed Avatar Change and Avatar Roulette redeems know the exact VRChat avatar ID to switch back to. If this is wrong, timed avatar switches cannot return correctly.")
        : TF("Timed Avatar Change and Avatar Roulette redeems switch back to {0} when they finish. In the normal return-avatar mode, direct Avatar Change redeems set to 0 seconds make their new avatar the Return Avatar instead.", MasterAvatarDisplayName);

    public string RewardTestOverrideButtonText => Settings.ChannelPointRewardTestModeEnabled
        ? T("Test Mode On")
        : T("Test Mode Off");

    public string RewardTestOverrideHelpText => Settings.EmergencyRedeemStopEnabled
        ? T("Redeems are paused right now. Resume them when you are ready to let Twitch redeems run again.")
        : Settings.ChannelPointRewardTestModeEnabled
            ? T("Test Mode is on. Crystal Relay is keeping redeems live for testing, but avatar-based redeems still follow your current avatar.")
            : T("Turn this on to test Twitch-managed redeems as if you were live.");

    public string EmergencyRedeemStopButtonText => Settings.EmergencyRedeemStopEnabled
        ? T("Resume Redeems")
        : T("Pause Redeems");

    public string EmergencyRedeemStopHelpText => Settings.EmergencyRedeemStopEnabled
        ? T("Redeems are paused. Crystal Relay is keeping managed Twitch redeems turned off until you resume them.")
        : T("Turn this on when you need a serious moment. Crystal Relay will turn off managed Twitch redeems and pause Twitch-triggered actions until you resume.");

    public bool IsEmergencyRedeemStopEnabled => Settings.EmergencyRedeemStopEnabled;

    public string DesktopModeInputLockButtonText => Settings.DesktopModeInputLockEnabled
        ? T("Desktop Hard Lock On")
        : T("Desktop Hard Lock Off");

    public string DesktopModeInputLockHelpText => Settings.DesktopModeInputLockEnabled
        ? TF("Desktop mode input lock is on. Stop Movement, Stop Turning, and Stop All will block local desktop movement input for their timer. VR mode still uses a softer VRChat-side lock. Emergency unlock: {0}.", DesktopInputLockService.EmergencyHotkeyDisplay)
        : TF("Desktop mode input lock is off. Stop Movement, Stop Turning, and Stop All will stay on the safer VRChat-side soft lock path. Turn this on only when you are streaming in VRChat desktop mode. Emergency unlock: {0}.", DesktopInputLockService.EmergencyHotkeyDisplay);

    public string DesktopModeInputLockStatusText => Settings.DesktopModeInputLockEnabled
        ? TF("Desktop hard lock is armed for stop-input redeems. Emergency unlock: {0}.", DesktopInputLockService.EmergencyHotkeyDisplay)
        : T("Desktop hard lock is off. Stop-input redeems will use the VRChat soft-lock fallback instead.");

    public bool IsDesktopModeInputLockEnabled => Settings.DesktopModeInputLockEnabled;

    public bool IsRewardTestOverrideAvailable => true;

    public bool IsBroadcasterLive
    {
        get => isBroadcasterLive;
        private set
        {
            if (SetProperty(ref isBroadcasterLive, value))
            {
                RefreshStreamingStatusCard();
                RaisePropertyChanged(nameof(IsStreamingTestButtonVisible));
            }
        }
    }

    public bool IsStreamingTestButtonVisible => !IsBroadcasterLive;

    public string VrChatOscParameterStatus
    {
        get => vrChatOscParameterStatus;
        private set => SetProperty(ref vrChatOscParameterStatus, value);
    }

    public TriggerRule? SelectedRule
    {
        get => selectedRule;
        set
        {
            if (SetProperty(ref selectedRule, value))
            {
                RemoveSelectedRuleCommand.NotifyCanExecuteChanged();
                RemoveSelectedOutfitChoiceCommand.NotifyCanExecuteChanged();
                TestSelectedRuleCommand.NotifyCanExecuteChanged();
                SelectedSetTriggerAction = value?.SetTriggerActions.FirstOrDefault();
                RaisePropertyChanged(nameof(SelectedSetTriggerUsesSharedNumberedReward));
                AddSetTriggerActionCommand.NotifyCanExecuteChanged();
                RemoveSelectedSetTriggerActionCommand.NotifyCanExecuteChanged();
                OpenActiveFloatBoostRewardCommand.NotifyCanExecuteChanged();
                RefreshAvatarParameterPathCommandStates();
                RememberSelectedRuleForCurrentView(value);
                RaisePropertyChanged(nameof(ChatCommandFallbackHelpText));

                if (isSwitchingRuleView)
                {
                    return;
                }

                RefreshSpecialRuleLockoutOptions();
                RefreshVrChatAvatarSelectionOptions();
                RefreshAvailableActionTypes();
                RefreshAvatarParameterOptions();
                _ = EnsureSelectedAvatarParameterCacheLoadedAsync();
            }
        }
    }

    public MovementRedeemSet? SelectedMovementRedeemSet
    {
        get => selectedMovementRedeemSet;
        set
        {
            if (SetProperty(ref selectedMovementRedeemSet, value))
            {
                AddRuleCommand.NotifyCanExecuteChanged();
                RaisePropertyChanged(nameof(SelectedMovementRedeemSet));
                RefreshRuleCommandStates();
            }
        }
    }

    public AvatarScaleSet? SelectedAvatarScaleSet
    {
        get => selectedAvatarScaleSet;
        set
        {
            if (SetProperty(ref selectedAvatarScaleSet, value))
            {
                lastSelectedAvatarScaleSetId = value?.Id ?? Guid.Empty;
                if (SelectedAvatarScaleRule is not null
                    && value?.ScaleRules.Contains(SelectedAvatarScaleRule) != true)
                {
                    SelectedAvatarScaleRule = GetRememberedAvatarScaleRule();
                }
                else if (SelectedAvatarScaleRule is null && value is not null)
                {
                    SelectedAvatarScaleRule = GetRememberedAvatarScaleRule();
                }

                RemoveSelectedAvatarScaleSetCommand.NotifyCanExecuteChanged();
                AddAvatarScaleRuleCommand.NotifyCanExecuteChanged();
                AddRewardGrowthCommand.NotifyCanExecuteChanged();
                RaisePropertyChanged(nameof(SelectedAvatarScaleSet));
                RaisePropertyChanged(nameof(AvatarScaleRules));
                RaisePropertyChanged(nameof(AvatarScaleRuntimeStatusText));
                RefreshRuleCommandStates();
            }
        }
    }

    public AvatarScaleRule? SelectedAvatarScaleRule
    {
        get => selectedAvatarScaleRule;
        set
        {
            if (SetProperty(ref selectedAvatarScaleRule, value))
            {
                lastSelectedAvatarScaleRuleId = value?.Id ?? Guid.Empty;
                RemoveSelectedAvatarScaleRuleCommand.NotifyCanExecuteChanged();
                TestSelectedAvatarScaleRuleCommand.NotifyCanExecuteChanged();
                OpenAvatarScaleRuleLockoutPickerCommand.NotifyCanExecuteChanged();
                RaisePropertyChanged(nameof(SelectedAvatarScaleRule));
                RaisePropertyChanged(nameof(AvailableAvatarScaleTriggerTypesForSelectedRule));
                RaisePropertyChanged(nameof(AvatarScaleRuntimeStatusText));
                RaisePropertyChanged(nameof(AvatarScaleRuleLockoutSummaryText));
            }
        }
    }

    public PowerUpRule? SelectedPowerUpRule
    {
        get => selectedPowerUpRule;
        set
        {
            if (SetProperty(ref selectedPowerUpRule, value))
            {
                lastSelectedPowerUpRuleId = value?.Id ?? Guid.Empty;
                if (IsViewingPowerUps)
                {
                    SelectedRule = value?.UsesTriggerAction == true ? value.ActionRule : null;
                    SelectedAvatarScaleRule = value?.UsesAvatarScaling == true ? value.ScaleAction : null;
                }

                RemoveSelectedPowerUpRuleCommand.NotifyCanExecuteChanged();
                TestSelectedPowerUpRuleCommand.NotifyCanExecuteChanged();
                UnlinkPowerUpCommand.NotifyCanExecuteChanged();
                UseCurrentAvatarForPowerUpRuleCommand.NotifyCanExecuteChanged();
                RaisePropertyChanged(nameof(SelectedPowerUpRule));
                RaisePropertyChanged(nameof(PowerUpRuleStatusText));
                RaisePropertyChanged(nameof(PowerUpActionEditorHelpText));
                RefreshAvailableActionTypes();
                RefreshAvatarParameterOptions();
                _ = EnsureSelectedAvatarParameterCacheLoadedAsync();
            }
        }
    }

    public TriggerRule? SelectedAvatarRule
    {
        get => selectedAvatarRule;
        set
        {
            SetProperty(ref selectedAvatarRule, value);
        }
    }

    public ObservableCollection<PowerUpRule> PowerUpRules => Settings.PowerUpRules;

    public string BridgeStatus
    {
        get => bridgeStatus;
        private set
        {
            if (SetProperty(ref bridgeStatus, value))
            {
                UpdateOscStatusSummary();
                RefreshStreamingStatusCard();
                UpdateChatboxListenerStatus();
                RaisePropertyChanged(nameof(ChatboxOscRelayStatusText));
            }
        }
    }

    public string BroadcasterStatus
    {
        get => broadcasterStatus;
        private set
        {
            if (SetProperty(ref broadcasterStatus, value))
            {
                RaisePropertyChanged(nameof(BroadcasterStatusDisplayText));
                RefreshStreamingStatusCard();
            }
        }
    }

    public string BroadcasterStatusDisplayText => LocalizeAccountStatusText(BroadcasterStatus);

    public string BotStatus
    {
        get => botStatus;
        private set
        {
            if (SetProperty(ref botStatus, value))
            {
                RaisePropertyChanged(nameof(BotStatusDisplayText));
            }
        }
    }

    public string BotStatusDisplayText => LocalizeAccountStatusText(BotStatus);

    public string RuntimeConfigStatus
    {
        get => runtimeConfigStatus;
        private set => SetProperty(ref runtimeConfigStatus, value);
    }

    public string OscBridgeSummary
    {
        get => oscBridgeSummary;
        private set => SetProperty(ref oscBridgeSummary, value);
    }

    public string OscStatusDetail
    {
        get => oscStatusDetail;
        private set => SetProperty(ref oscStatusDetail, value);
    }

    public string BroadcasterExpiryStatus
    {
        get => broadcasterExpiryStatus;
        private set => SetProperty(ref broadcasterExpiryStatus, value);
    }

    public string BotExpiryStatus
    {
        get => botExpiryStatus;
        private set => SetProperty(ref botExpiryStatus, value);
    }

    public string VrChatStatus
    {
        get => vrChatStatus;
        private set => SetProperty(ref vrChatStatus, value);
    }

    private VrChatConnectionState vrChatConnectionState = VrChatConnectionState.NoData;
    public VrChatConnectionState VrChatConnectionState
    {
        get => vrChatConnectionState;
        private set
        {
            if (vrChatConnectionState != value)
            {
                vrChatConnectionState = value;
                RaisePropertyChanged();
                RaisePropertyChanged(nameof(VrChatConnectionStateLabel));
                RaisePropertyChanged(nameof(VrChatConnectionStateTooltip));
                RaisePropertyChanged(nameof(VrChatConnectionStateBrush));
            }
        }
    }

    public string VrChatConnectionStateLabel => VrChatConnectionState switch
    {
        VrChatConnectionState.LoggedIn => T("VRChat: Logged in"),
        VrChatConnectionState.Cached => T("VRChat: Cached"),
        _ => T("VRChat: No data"),
    };

    public string VrChatConnectionStateTooltip => VrChatConnectionState switch
    {
        VrChatConnectionState.LoggedIn => T("Connected to VRChat. Avatar names and current avatar are fetched from the live API."),
        VrChatConnectionState.Cached => T("VRChat login is unavailable. Crystal Relay is using the cached avatar list and detecting the current avatar via OSC and LocalLow files."),
        _ => T("No avatar data is available. Log in to VRChat or visit an avatar in VRChat to build the cache."),
    };

    public Brush VrChatConnectionStateBrush => VrChatConnectionState switch
    {
        VrChatConnectionState.LoggedIn => Brushes.LimeGreen,
        VrChatConnectionState.Cached => Brushes.Goldenrod,
        _ => Brushes.Gray,
    };

    public string VrChatAvatarStatus
    {
        get => vrChatAvatarStatus;
        private set => SetProperty(ref vrChatAvatarStatus, value);
    }

    public bool IsBroadcasterConnected => HasRecoverableBroadcasterSession && !broadcasterReconnectRequired;

    public bool IsBroadcasterDisconnected => !IsBroadcasterConnected;

    public bool IsBotConnected => Settings.Bot.IsConnected;

    public bool IsBotDisconnected => !IsBotConnected;

    public bool IsOscConnected => bridgeCoordinator.IsOscActive;

    public bool IsOscDisconnected => !IsOscConnected;

    public string EffectiveBotSenderStatusText => BuildEffectiveBotSenderStatusText();

    public string BuiltInCommandsSummaryText => BuildBuiltInCommandsSummaryText();

    public string BuiltInCommandsWarningText => BuildBuiltInCommandsWarningText();

    public bool HasBuiltInCommandsWarning => !string.IsNullOrWhiteSpace(BuiltInCommandsWarningText);

    public string WorldCommandBlacklistStatusText
    {
        get => worldCommandBlacklistStatusText;
        private set => SetProperty(ref worldCommandBlacklistStatusText, value);
    }

    public string WorldCommandBlacklistFormatReminderText =>
        string.Join(
            Environment.NewLine,
            T("# [VRChat User ID] - [MM/DD/YYYY - MM/DD/YYYY] [Reason]"),
            T("usr_00000000-0000-0000-0000-000000000000 - [06/01/2026 - 06/07/2026] Special event reason"),
            T("# [World ID] - [Reason]"),
            T("wrld_00000000-0000-0000-0000-000000000000 - Patreon/private event reason"));

    public bool IsVrChatConnected => Settings.VrChat.IsConnected;

    public bool IsVrChatDisconnected => !IsVrChatConnected;

    public bool HasVrChatAvatarOptions =>
        ProfileAvatarOptions.Count > 0
        || VrChatAvatarOptions.Count > 0
        || VrChatResetAvatarOptions.Count > 0;

    public bool IsHomeSectionSelected => activeSection == SectionView.Home;

    public bool IsSettingsSectionSelected => activeSection == SectionView.Settings;

    public bool IsActivitySectionSelected => activeSection == SectionView.Activity;

    public bool IsAboutSectionSelected => activeSection == SectionView.About;

    public bool HasActivityAttention
    {
        get => hasActivityAttention;
        private set => SetProperty(ref hasActivityAttention, value);
    }

    public string ActivityAttentionMessage
    {
        get => activityAttentionMessage;
        private set
        {
            if (SetProperty(ref activityAttentionMessage, value))
            {
                RaisePropertyChanged(nameof(HasActivityWarning));
            }
        }
    }

    public bool HasActivityWarning => !string.IsNullOrWhiteSpace(ActivityAttentionMessage);

    public bool HasLiveAboutProfiles => EnumerateAboutProfiles().Any(profile => profile.IsLive);

    public bool IsSettingsTwitchSectionSelected => activeSettingsSection == SettingsSectionView.Twitch;

    public bool IsSettingsVrChatSectionSelected => activeSettingsSection == SettingsSectionView.VrChat;

    public bool IsSettingsAppSectionSelected => activeSettingsSection == SettingsSectionView.App;

    public bool IsSettingsVisualsSectionSelected => activeSettingsSection == SettingsSectionView.Visuals;

    public bool IsSettingsSafetySectionSelected => activeSettingsSection == SettingsSectionView.Safety;

    public bool IsVoidCrystalThemeSelected => SelectedTheme == AppTheme.VoidCrystal;

    public bool IsCustomThemeSelected => SelectedTheme == AppTheme.Custom;

    public bool IsTreetendersArmThemeSelected => SelectedTheme == AppTheme.TreetendersArm;

    public bool IsDreamScapeThemeSelected => SelectedTheme == AppTheme.DreamScape;

    public bool IsMainFrameThemeSelected => SelectedTheme == AppTheme.MainFrame;

    public bool IsTrashKittyThemeSelected => SelectedTheme == AppTheme.TrashKitty;

    public bool IsBratwurstThemeSelected => SelectedTheme == AppTheme.Bratwurst;

    public bool IsBubblegumThemeSelected => SelectedTheme == AppTheme.Bubblegum;

    public bool IsCosmicPuppyGirlThemeSelected => SelectedTheme == AppTheme.CosmicPuppyGirl;

    public bool IsPeachesAndCreamThemeSelected => SelectedTheme == AppTheme.PeachesAndCream;

    public bool IsMoonBunnyWinkThemeSelected => SelectedTheme == AppTheme.MoonBunnyWink;

    public bool IsDreadNightBarThemeSelected => SelectedTheme == AppTheme.DreadNightBar;

    public bool IsBakedThemeSelected => SelectedTheme == AppTheme.Baked;

    public bool IsNeonBorbThemeSelected => SelectedTheme == AppTheme.NeonBorb;

    public bool IsStinkyOnlineThemeSelected => SelectedTheme == AppTheme.StinkyOnline;

    public bool IsSquishyFoxPlushThemeSelected => SelectedTheme == AppTheme.SquishyFoxPlush;

    public bool IsPucaThemeSelected => SelectedTheme == AppTheme.Puca;

    public bool HasCustomThemeBackgroundImage => !string.IsNullOrWhiteSpace(Settings.CustomTheme.BackgroundImageRelativePath);

    public string CustomThemeBackgroundImageStatusText => HasCustomThemeBackgroundImage
        ? Path.GetFileName(Settings.CustomTheme.BackgroundImageRelativePath)
        : T("No background image selected.");

    public string ChatboxListenerStatus
    {
        get => chatboxListenerStatus;
        private set
        {
            if (SetProperty(ref chatboxListenerStatus, value))
            {
                RaisePropertyChanged(nameof(ChatboxEmptyStateText));
            }
        }
    }

    public string ChatboxEmptyStateText =>
        ChatboxListenerStatus.Contains("connected and listening", StringComparison.OrdinalIgnoreCase)
            ? T("Connected and listening. Waiting for new chat messages...")
            : ChatboxListenerStatus;

    public bool IsChatboxModerationDrawerOpen
    {
        get => isChatboxModerationDrawerOpen;
        private set
        {
            if (SetProperty(ref isChatboxModerationDrawerOpen, value))
            {
                RaisePropertyChanged(nameof(ChatboxModerationDrawerToggleText));
            }
        }
    }

    public string ChatboxModerationDrawerToggleText => IsChatboxModerationDrawerOpen
        ? T("Hide Activity + Mod")
        : T("Activity + Mod");

    public bool BlockedWordsSectionOpen
    {
        get => blockedWordsSectionOpen;
        set => SetProperty(ref blockedWordsSectionOpen, value);
    }

    public ObservableCollection<BlockedWordItem> BlockedWordItems
    {
        get => blockedWordItems;
        set => SetProperty(ref blockedWordItems, value);
    }

    public string NewBlockedWordText
    {
        get => newBlockedWordText;
        set
        {
            if (SetProperty(ref newBlockedWordText, value))
                AddBlockedWordCommand.NotifyCanExecuteChanged();
        }
    }

    public TwitchChatMessageEntry? SelectedChatMessage
    {
        get => selectedChatMessage;
        set
        {
            if (SetProperty(ref selectedChatMessage, value))
            {
                RaiseSelectedChatModerationProperties();
                RefreshChatModerationCommandStates();
            }
        }
    }

    public bool HasSelectedChatMessage => SelectedChatMessage is not null;

    public bool HasSelectedChatModerationTarget => GetSelectedChatModerationTarget() is not null;

    public string SelectedChatModerationTitle => SelectedChatMessage is { } entry
        ? TF("Moderate {0}", entry.UserDisplayName)
        : string.Empty;

    public string SelectedChatModerationDetailText
    {
        get
        {
            if (SelectedChatMessage is not { } entry)
            {
                return string.Empty;
            }

            var login = string.IsNullOrWhiteSpace(entry.UserLogin)
                ? T("unknown login")
                : $"@{entry.UserLogin}";
            var status = string.IsNullOrWhiteSpace(entry.SuspiciousStatusLabel)
                ? T("No suspicious-user status")
                : entry.SuspiciousStatusLabel;
            return string.IsNullOrWhiteSpace(entry.UserId)
                ? TF("{0} · {1}", login, status)
                : TF("{0} · Twitch ID {1} · {2}", login, entry.UserId, status);
        }
    }

    public string ChatboxModerationStatusText
    {
        get => chatboxModerationStatusText;
        private set => SetProperty(ref chatboxModerationStatusText, value);
    }

    public string ChatboxModerationScopeStatusText
    {
        get
        {
            if (!Settings.Broadcaster.IsConnected)
            {
                return T("Connect the broadcaster account to use Twitch moderation.");
            }

            var missingScopes = new List<string>();
            if (!HasScope(Settings.Broadcaster, TwitchScopes.ModerationBannedUsers))
            {
                missingScopes.Add(T("timeouts/bans"));
            }

            if (!HasScope(Settings.Broadcaster, TwitchScopes.ModerationChatMessages))
            {
                missingScopes.Add(T("message delete"));
            }

            if (!HasScope(Settings.Broadcaster, TwitchScopes.ModerationSuspiciousUsers)
                || !HasScope(Settings.Broadcaster, TwitchScopes.SuspiciousUsersRead))
            {
                missingScopes.Add(T("suspicious users"));
            }

            return missingScopes.Count == 0
                ? T("Twitch moderation access is ready.")
                : TF("Reconnect broadcaster to enable: {0}.", string.Join(", ", missingScopes));
        }
    }

    public string ChatboxOscRelayStatusText
    {
        get
        {
            if (!Settings.ChatboxOscEnabled)
            {
                return T("Turn this on to send Twitch chat into VRChat.");
            }

            if (!Settings.Broadcaster.IsConnected)
            {
                return T("Connect your broadcaster account first.");
            }

            if (!HasBroadcasterChatScope())
            {
                return T("Reconnect your broadcaster account to give Crystal Relay chat access.");
            }

            if (!bridgeCoordinator.IsOscActive || !bridgeCoordinator.HasDiscoveredVrChat)
            {
                return T("Waiting for OSC / VRChat connection.");
            }

            return T("Ready to send Twitch chat into VRChat.");
        }
    }

    public string BroadcasterDeviceCode
    {
        get => broadcasterDeviceCode;
        private set
        {
            if (SetProperty(ref broadcasterDeviceCode, value))
            {
                RaisePropertyChanged(nameof(BroadcasterDeviceCodeDisplayText));
                OpenBroadcasterLoginCommand.NotifyCanExecuteChanged();
            }
        }
    }

    public string BroadcasterDeviceCodeDisplayText => TF("Device code: {0}", BroadcasterDeviceCode);

    public string BroadcasterVerificationUri
    {
        get => broadcasterVerificationUri;
        private set
        {
            if (SetProperty(ref broadcasterVerificationUri, value))
            {
                OpenBroadcasterLoginCommand.NotifyCanExecuteChanged();
            }
        }
    }

    public string BotDeviceCode
    {
        get => botDeviceCode;
        private set
        {
            if (SetProperty(ref botDeviceCode, value))
            {
                RaisePropertyChanged(nameof(BotDeviceCodeDisplayText));
                OpenBotLoginCommand.NotifyCanExecuteChanged();
            }
        }
    }

    public string BotDeviceCodeDisplayText => TF("Device code: {0}", BotDeviceCode);

    public string BotVerificationUri
    {
        get => botVerificationUri;
        private set
        {
            if (SetProperty(ref botVerificationUri, value))
            {
                OpenBotLoginCommand.NotifyCanExecuteChanged();
            }
        }
    }

    public AsyncRelayCommand ConnectBroadcasterCommand { get; }

    public AsyncRelayCommand DisconnectBroadcasterCommand { get; }

    public RelayCommand OpenBroadcasterLoginCommand { get; }

    public AsyncRelayCommand OpenBroadcasterAuthPageCommand { get; }

    public AsyncRelayCommand ConnectBotCommand { get; }

    public AsyncRelayCommand DisconnectBotCommand { get; }

    public RelayCommand OpenBotLoginCommand { get; }

    public AsyncRelayCommand OpenBotAuthPageCommand { get; }

    public RelayCommand OpenRuntimeConfigCommand { get; }

    public RelayCommand OpenRuntimeConfigFolderCommand { get; }

    public AsyncRelayCommand ConnectVrChatCommand { get; }

    public AsyncRelayCommand DisconnectVrChatCommand { get; }

    public AsyncRelayCommand RefreshVrChatAvatarsCommand { get; }

    public AsyncRelayCommand ClearVrChatCacheCommand { get; }

    public RelayCommand OpenTwitchDeveloperConsoleCommand { get; }

    public RelayCommand OpenSaveFolderCommand { get; }

    public AsyncRelayCommand RepairSavedLoginStateCommand { get; }

    public RelayCommand OpenKoFiSupportCommand { get; }

    public RelayCommand OpenKoFiWebhooksCommand { get; }

    public RelayCommand OpenDiscordInviteCommand { get; }

    public AsyncRelayCommand OpenBugReportCommand { get; }

    public AsyncRelayCommand RefreshTwitchRewardsCommand { get; }

    public AsyncRelayCommand RefreshPowerUpsCommand { get; }

    ICommand ITwitchRewardSource.RefreshTwitchRewardsCommand => RefreshTwitchRewardsCommand;

    public RelayCommand UnlinkTwitchRewardCommand { get; }

    ICommand ITwitchRewardSource.UnlinkTwitchRewardCommand => UnlinkTwitchRewardCommand;

    ObservableCollection<TwitchPowerUpOption> ITwitchRewardSource.PowerUpOptions => PowerUpOptions;

    public RelayCommand UnlinkWardrobeMasterRewardCommand { get; }

    public AsyncRelayCommand TestSelectedRuleCommand { get; }

    public AsyncRelayCommand SimulateTestModeBitsCommand { get; }

    public AsyncRelayCommand SimulateTestModeSubscriptionCommand { get; }

    public AsyncRelayCommand SimulateTestModeCashPaymentCommand { get; }

    public RelayCommand ShowSettingsTestCommand { get; }

    public RelayCommand ShowHomeSectionCommand { get; }

    public RelayCommand ShowSettingsSectionCommand { get; }

    public RelayCommand ShowActivitySectionCommand { get; }

    public RelayCommand ShowAboutSectionCommand { get; }

    public RelayCommand OpenTestModeWindowCommand { get; }

    public RelayCommand OpenTwitchChatboxCommand { get; }

    public RelayCommand ToggleChatboxModerationDrawerCommand { get; }

    public RelayCommand AddBlockedWordCommand { get; }

    public RelayCommand RemoveBlockedWordCommand { get; }

    public RelayCommand RestoreBlockedWordCommand { get; }

    public AsyncRelayCommand TimeoutSelectedChatUser10SecondsCommand { get; }

    public AsyncRelayCommand TimeoutSelectedChatUser1MinuteCommand { get; }

    public AsyncRelayCommand TimeoutSelectedChatUser5MinutesCommand { get; }

    public AsyncRelayCommand TimeoutSelectedChatUser10MinutesCommand { get; }

    public AsyncRelayCommand TimeoutSelectedChatUser30MinutesCommand { get; }

    public AsyncRelayCommand TimeoutSelectedChatUser1HourCommand { get; }

    public AsyncRelayCommand BanSelectedChatUserCommand { get; }

    public AsyncRelayCommand PurgeSelectedChatUserCommand { get; }

    public AsyncRelayCommand DeleteSelectedChatMessageCommand { get; }

    public AsyncRelayCommand MarkSelectedChatUserSuspiciousCommand { get; }

    public AsyncRelayCommand RestrictSelectedChatUserCommand { get; }

    public AsyncRelayCommand ClearSelectedChatUserSuspiciousStatusCommand { get; }

    public RelayCommand OpenBuiltInCommandsCommand { get; }

    public AsyncRelayCommand RefreshWorldCommandBlacklistCommand { get; }

    public RelayCommand DismissMigrationNoticeCommand { get; }

    public RelayCommand DismissCashPaymentMigrationNoticeCommand { get; }

    public RelayCommand DismissUiUpdateNoticeCommand { get; }

    public RelayCommand ShowSettingsTwitchSectionCommand { get; }

    public RelayCommand ShowSettingsVrChatSectionCommand { get; }

    public RelayCommand ShowSettingsAppSectionCommand { get; }

    public RelayCommand ShowSettingsVisualsSectionCommand { get; }

    public RelayCommand ShowSettingsSafetySectionCommand { get; }

    public RelayCommand ShowAvatarTriggerRulesCommand { get; }

    public RelayCommand ShowMovementRedeemsCommand { get; }

    public RelayCommand ShowPowerUpsCommand { get; }

    public RelayCommand OpenUniversalTriggersManagerCommand { get; }

    public RelayCommand OpenAvatarScalingManagerCommand { get; }

    public RelayCommand OpenAvatarSetsManagerCommand { get; }

    public RelayCommand OpenCashPaymentManagerCommand { get; }

    public RelayCommand ShowAvatarScalingCommand { get; }

    public RelayCommand OpenRewardFireSaleManagerCommand { get; }

    public RelayCommand AddAvatarProfileCommand { get; }

    public RelayCommand DeleteSelectedAvatarProfileCommand { get; }

    public RelayCommand DeleteAllAvatarProfilesCommand { get; }

    public RelayCommand SetSelectedAvatarProfileAsMasterCommand { get; }

    public RelayCommand ToggleSelectedAvatarRewardTestOverrideCommand { get; }

    public RelayCommand ToggleEmergencyRedeemStopCommand { get; }

    public RelayCommand ToggleDesktopModeInputLockCommand { get; }

    public RelayCommand UseCurrentVrChatAvatarForProfileCommand { get; }

    public RelayCommand OpenAvatarPickerCommand { get; }

    public RelayCommand AddRuleCommand { get; }

    public RelayCommand AddOutfitChoiceCommand { get; }

    public RelayCommand RemoveSelectedOutfitChoiceCommand { get; }

    public RelayCommand SelectRuleCommand { get; }

    public RelayCommand AddAvatarSupporterTriggerCommand { get; }

    public RelayCommand AddForceMovementOverrideCommand { get; }

    public RelayCommand RemoveSelectedRuleCommand { get; }

    public RelayCommand EnableAllRulesCommand { get; }

    public RelayCommand DisableAllRulesCommand { get; }

    public RelayCommand DeleteAllRulesCommand { get; }

    public RelayCommand AddAvatarScaleSetCommand { get; }

    public RelayCommand RemoveSelectedAvatarScaleSetCommand { get; }

    public RelayCommand AddAvatarScaleRuleCommand { get; }

    public RelayCommand AddRewardGrowthCommand { get; }

    public RelayCommand RemoveSelectedAvatarScaleRuleCommand { get; }

    public RelayCommand EnableAllAvatarScaleRulesCommand { get; }

    public RelayCommand DisableAllAvatarScaleRulesCommand { get; }

    public RelayCommand DeleteAllAvatarScaleRulesCommand { get; }

    public RelayCommand TestSelectedAvatarScaleRuleCommand { get; }

    public RelayCommand OpenAvatarScaleRuleLockoutPickerCommand { get; }

    public RelayCommand AddAvatarScalingCashPaymentRuleCommand { get; }

    public RelayCommand AddPowerUpRuleCommand { get; }

    public RelayCommand AddAvatarScalingPowerUpRuleCommand { get; }

    public RelayCommand RemoveSelectedPowerUpRuleCommand { get; }

    public RelayCommand EnableAllPowerUpRulesCommand { get; }

    public RelayCommand DisableAllPowerUpRulesCommand { get; }

    public RelayCommand DeleteAllPowerUpRulesCommand { get; }

    public AsyncRelayCommand TestSelectedPowerUpRuleCommand { get; }

    public RelayCommand UnlinkPowerUpCommand { get; }

    public RelayCommand UseCurrentAvatarForPowerUpRuleCommand { get; }

    public RelayCommand RegenerateKoFiRelayIdentityCommand { get; }

    public RelayCommand OpenSpecialRuleLockoutPickerCommand { get; }

    public RelayCommand OpenAvatarRouletPoolPickerCommand { get; }

    public RelayCommand OpenActiveFloatBoostRewardCommand { get; }

    public RelayCommand AddSetTriggerActionCommand { get; }

    public RelayCommand RemoveSelectedSetTriggerActionCommand { get; }

    public RelayCommand CopySelectedAvatarParameterPathCommand { get; }

    public RelayCommand PasteSelectedAvatarParameterPathCommand { get; }

    public RelayCommand PickReturnAvatarCommand { get; }

    public RelayCommand UseCurrentAvatarForReturnCommand { get; }

    public RelayCommand ClearReturnAvatarCommand { get; }

    // Startup flow for the main window. This loads saved data, rebuilds editor state,
    // restores helper caches, and runs cleanup recovery if the previous launch ended badly.
    public async Task InitializeAsync()
    {
        var previousSessionNeedsRecovery = ShutdownRecoveryStateStore.BeginSession();
        previousSessionWasClean = !previousSessionNeedsRecovery;
        ReplaceSettings(await settingsStore.LoadAsync());
        RefreshBlockedWordItems();
        LoadingService.ReportProgress("settings", PhaseStatus.Completed);
        LoadingService.ReportProgress("vrchat", PhaseStatus.Active);
        var savedLoginRecoveryResult = SavedLoginStateRecoveryService.TryConsumeRecoveryResult();
        activeLanguageAtStartup = Settings.Language;
        ResetStartupSectionState();
        var resetStreamingTestModeOnLaunch = Settings.ChannelPointRewardTestModeEnabled;
        if (resetStreamingTestModeOnLaunch)
        {
            Settings.ChannelPointRewardTestModeEnabled = false;
        }
        await ReloadRuntimeConfigAsync();
        UpgradeLegacyRewardTestOverrides();
        RaiseThemeStateChanged();

        // Deferred heavy initialization: these were moved out of the constructor
        // to reduce the time before the window first appears.
        CashPaymentCurrencyCodeOptions = BuildCashPaymentCurrencyCodeOptions();
        RaisePropertyChanged(nameof(CashPaymentCurrencyCodeOptions));
        CustomThemeFontOptions = [.. Fonts.SystemFontFamilies
            .Select(fontFamily => fontFamily.Source)
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .OrderBy(name => name, StringComparer.OrdinalIgnoreCase)];
        RaisePropertyChanged(nameof(CustomThemeFontOptions));

        EnsureRuleCollectionsHaveStarterContent();
        NormalizeMasterAvatarProfiles();
        RefreshAvatarRuleProfilesList();
        RelocateMisplacedMovementRules();
        RemoveUnsupportedMovementRules();
        var normalizedChatCommandFallbacks = NormalizeChatCommandFallbackRules();
        var fusedUniversalCommandFallbacks = UniversalTriggerFusionService.FuseMatchingCommandFallbacks(Settings.UniversalTriggers);
        NormalizeAvatarProfileRules();
        var normalizedSupporterAvatarScopes = NormalizeSupporterAvatarScopes();
        UpdateAvatarProfileActivityStates();
        SelectedAvatarProfile = AvatarRuleProfiles.FirstOrDefault();
        SelectedRule = SelectedAvatarProfile?.ChannelPointRules.FirstOrDefault();
        RefreshAvailableActionTypes();
        RefreshVrChatAvatarSelectionOptions();
        _ = EnsureSelectedAvatarParameterCacheLoadedAsync();
        RefreshRuntimeSummary();
        UpdateAccountStatuses();
        isInitialized = true;
        LoadMasterAvatarReturnImage();
        if (normalizedChatCommandFallbacks || fusedUniversalCommandFallbacks > 0 || normalizedSupporterAvatarScopes)
        {
            QueueSave(0);
        }
        RefreshCommandStates();
        var aboutProfilesRefreshTask = RefreshAboutProfilesAsync();

        if (previousSessionNeedsRecovery)
        {
            AppendLog("Crystal Relay detected the previous session did not shut down cleanly. Running quick reward cleanup recovery.");
            var recoveryCleanupCompleted = await DisableManagedRewardsForRecoveryAsync();
            if (!recoveryCleanupCompleted)
            {
                AppendLog("Recovery cleanup could not finish yet. Crystal Relay will retry reward cleanup on the next launch.");
            }
        }

        if (resetStreamingTestModeOnLaunch)
        {
            AppendLog("Streaming test mode reset to off for this launch.");
        }

        if (fusedUniversalCommandFallbacks > 0)
        {
            AppendLog($"Fused {fusedUniversalCommandFallbacks} matching Universal chat command(s) into channel point rewards.");
        }

        AppendLog("Loaded saved settings.");
        ReportSavedLoginRecoveryResult(savedLoginRecoveryResult);
        if (previousSessionWasClean && !ApplicationRestartService.IsRestartSession)
        {
            await activityResumeService.DeleteStaleFileIfPresentAsync();
        }
        await activityResumeService.LoadPendingAsync();
        await RefreshWorldCommandBlacklistOnStartupAsync();
        LoadingService.ReportProgress("vrchat", PhaseStatus.Completed);
        LoadingService.ReportProgress("twitch", PhaseStatus.Active);
        await InitializeVrChatAsync();
        LoadingService.ReportProgress("twitch", PhaseStatus.Completed);
        LoadingService.ReportProgress("bridge", PhaseStatus.Active);
        await QueueRewardRefreshAsync();
        _ = QueuePowerUpRefreshAsync();
        QueueManagedRewardSync(reason: ManagedRewardSyncReason.Startup);
        await aboutProfilesRefreshTask;
        LoadingService.ReportProgress("bridge", PhaseStatus.Completed);
        LoadingService.ReportProgress("finalizing", PhaseStatus.Active);
        QueueBridgeRefresh();
        QueueLiveFeedbackHeartbeatEvaluation();
        LoadingService.CompleteAll();
    }

    public async ValueTask DisposeAsync()
    {
        isShuttingDown = true;
        var shutdownCleanupCompleted = true;

        if (twitchChatboxWindow is { IsLoaded: true })
        {
            twitchChatboxWindow.Closed -= OnTwitchChatboxClosed;
            twitchChatboxWindow.Close();
            twitchChatboxWindow = null;
        }

        if (testModeWindow is { IsLoaded: true })
        {
            testModeWindow.Closed -= OnTestModeWindowClosed;
            testModeWindow.Close();
            testModeWindow = null;
        }

        try
        {
            await StopLiveFeedbackHeartbeatAsync();
        }
        catch
        {
            // Shutdown should not wait on or fail because of live-feedback cleanup.
        }

        saveDebounceCancellation?.Cancel();
        saveDebounceCancellation?.Dispose();
        bridgeRefreshCancellation?.Cancel();
        bridgeRefreshCancellation?.Dispose();
        worldCommandBlacklistRefreshCancellation?.Cancel();
        worldCommandBlacklistRefreshCancellation?.Dispose();
        CancelAndDisposeQueuedCancellationSource(ref managedRewardSyncCancellation);
        CancelAndDisposeQueuedCancellationSource(ref vrChatCurrentAvatarRefreshCancellation);
        CancelAndDisposeQueuedCancellationSource(ref vrChatOscParameterRefreshCancellation);
        CancelAndDisposeQueuedCancellationSource(ref vrChatLocalOscScanCancellation);
        CancelAndDisposeQueuedCancellationSource(ref activeAvatarScaleLocalRefreshCancellation);
        CancelAndDisposeQueuedCancellationSource(ref rewardFireSaleExpirationCancellation);
        CancelAndDisposeQueuedCancellationSource(ref rewardFireSaleFundingCooldownCancellation);
        DisposeVrChatLocalOscWatcher();
        worldCommandBlacklistRefreshTimer.Stop();

        if (isInitialized)
        {
            if (Settings.ChannelPointRewardTestModeEnabled)
            {
                Settings.ChannelPointRewardTestModeEnabled = false;
            }

            try
            {
                shutdownCleanupCompleted = await DisableManagedRewardsForShutdownAsync();
            }
            catch
            {
                // Shutdown should continue even if Twitch cleanup cannot complete.
                shutdownCleanupCompleted = false;
            }

            try
            {
                var fireSale = Settings.RewardFireSale;
                fireSale.CurrentProgress = 0;
                fireSale.IsSaleActive = false;
                fireSale.ActiveDiscountPercent = 0;
                fireSale.ActiveTierGoalAmount = 0;
                fireSale.ActiveUntilUtc = null;
                await settingsStore.SaveAsync(Settings, CancellationToken.None);
            }
            catch
            {
                // Best-effort shutdown save so visual/theme changes are not lost
                // if the debounce window has not elapsed yet.
            }
        }

        UnwireSettings(Settings);
        AboutCreatorProfile.PropertyChanged -= OnAboutProfilePropertyChanged;
        foreach (var testerProfile in AboutTesterProfiles)
        {
            testerProfile.PropertyChanged -= OnAboutProfilePropertyChanged;
        }

        await bridgeCoordinator.DisposeAsync();
        twitchApiClient.Dispose();
        applicationUpdateService.Dispose();
        bugReportService.Dispose();
        liveFeedbackHeartbeatService.Dispose();
        worldCommandBlacklistService.Dispose();
        vrChatApiClient.Dispose();
        sessionStatusTimer.Stop();
        vrChatLocalStateTimer.Stop();
        vrChatCurrentAvatarTimer.Stop();
        worldCommandBlacklistRefreshTimer.Stop();
        bridgeRefreshGate.Dispose();
        managedRewardSyncGate.Dispose();
        vrChatLocalStateRefreshGate.Dispose();
        vrChatCurrentAvatarRefreshGate.Dispose();

        var isRestartShutdown = ApplicationRestartService.IsShuttingDownForRestart;
        AppendLog($"Shutdown: isRestartShutdown={isRestartShutdown}. Running cleanup...");
        ShutdownRecoveryStateStore.CompleteSession();
        if (!isRestartShutdown)
        {
            await activityResumeService.ClearAllAsync();
            AppendLog("Shutdown: activity resume file cleared.");
        }
        else
        {
            AppendLog("Shutdown: restart session detected — keeping activity resume file.");
        }
    }

    private void ResetStartupSectionState()
    {
        activeSection = SectionView.Home;
        activeSettingsSection = SettingsSectionView.Twitch;
        HasActivityAttention = false;
        RaisePropertyChanged(nameof(IsHomeSectionSelected));
        RaisePropertyChanged(nameof(IsSettingsSectionSelected));
        RaisePropertyChanged(nameof(IsActivitySectionSelected));
        RaisePropertyChanged(nameof(IsAboutSectionSelected));
        RaisePropertyChanged(nameof(IsSettingsTwitchSectionSelected));
        RaisePropertyChanged(nameof(IsSettingsVrChatSectionSelected));
        RaisePropertyChanged(nameof(IsSettingsAppSectionSelected));
        RaisePropertyChanged(nameof(IsSettingsVisualsSectionSelected));
        RaisePropertyChanged(nameof(IsSettingsSafetySectionSelected));
    }

    private void ShowActivitySection()
    {
        SetActiveSection(SectionView.Activity);
        ClearActivityAttentionPulse();
    }

    // Section switching is mostly visual, but About needs a refresh hook because
    // the creator/playtester cards can become stale while the app stays open.
    private void SetActiveSection(SectionView section)
    {
        if (!SetProperty(ref activeSection, section, nameof(IsHomeSectionSelected)))
        {
            return;
        }

        RaisePropertyChanged(nameof(IsHomeSectionSelected));
        RaisePropertyChanged(nameof(IsSettingsSectionSelected));
        RaisePropertyChanged(nameof(IsActivitySectionSelected));
        RaisePropertyChanged(nameof(IsAboutSectionSelected));

        if (section == SectionView.Activity)
        {
            ClearActivityAttentionPulse();
        }

        if (section == SectionView.About
            && (DateTimeOffset.UtcNow - aboutProfilesLastRefreshedAt >= AboutProfileRefreshInterval
                || EnumerateAboutProfiles().Any(profile => !profile.HasProfileImage)))
        {
            _ = RefreshAboutProfilesAsync();
        }
    }

    private void MarkActivityAttention(string message)
    {
        ActivityAttentionMessage = message;
        if (activeSection != SectionView.Activity)
        {
            HasActivityAttention = true;
        }
    }

    private void ClearActivityAttentionPulse()
    {
        HasActivityAttention = false;
    }

    // Whenever an About profile changes live state, the About button indicator may need
    // to light up or clear on the main window chrome.
    private void OnAboutProfilePropertyChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (string.IsNullOrWhiteSpace(e.PropertyName)
            || string.Equals(e.PropertyName, nameof(AboutTwitchProfile.IsLive), StringComparison.Ordinal))
        {
            RaisePropertyChanged(nameof(HasLiveAboutProfiles));
        }
    }

    private void SetActiveSettingsSection(SettingsSectionView section)
    {
        if (!SetProperty(ref activeSettingsSection, section, nameof(IsSettingsTwitchSectionSelected)))
        {
            return;
        }

        RaisePropertyChanged(nameof(IsSettingsTwitchSectionSelected));
        RaisePropertyChanged(nameof(IsSettingsVrChatSectionSelected));
        RaisePropertyChanged(nameof(IsSettingsAppSectionSelected));
        RaisePropertyChanged(nameof(IsSettingsVisualsSectionSelected));
        RaisePropertyChanged(nameof(IsSettingsSafetySectionSelected));
    }

    private void OpenTwitchChatbox()
    {
        if (twitchChatboxWindow is { IsLoaded: true })
        {
            if (twitchChatboxWindow.WindowState == WindowState.Minimized)
            {
                twitchChatboxWindow.WindowState = WindowState.Normal;
            }

            twitchChatboxWindow.Activate();
            twitchChatboxWindow.Focus();
            return;
        }

        twitchChatboxWindow = new TwitchChatboxWindow(this, SelectedTheme);
        twitchChatboxWindow.Closed += OnTwitchChatboxClosed;
        WindowPlacementStateStore.ApplyWindowPlacement(
            twitchChatboxWindow,
            WindowPlacementStateStore.TryLoadChatboxPlacement());
        twitchChatboxWindow.Show();
        UpdateChatboxListenerStatus();
    }

    private void ToggleChatboxModerationDrawer()
    {
        IsChatboxModerationDrawerOpen = !IsChatboxModerationDrawerOpen;
    }

    private void AddBlockedWord()
    {
        var word = NewBlockedWordText.Trim().ToLowerInvariant();
        if (string.IsNullOrWhiteSpace(word) || word.Length < 2)
            return;

        if (Settings.SuppressedBlockedWords.Remove(word))
        {
            NewBlockedWordText = string.Empty;
            RefreshBlockedWordItems();
            SyncFilterWithUserList();
            return;
        }

        if (Settings.CustomBlockedWords.Contains(word))
        {
            NewBlockedWordText = string.Empty;
            return;
        }

        if (ChatboxRelayModerationFilter.BlockedSlurTerms.Contains(word, StringComparer.OrdinalIgnoreCase))
        {
            NewBlockedWordText = string.Empty;
            return;
        }

        Settings.CustomBlockedWords.Add(word);
        NewBlockedWordText = string.Empty;
        RefreshBlockedWordItems();
        SyncFilterWithUserList();
    }

    private void RemoveBlockedWord(BlockedWordItem? item)
    {
        if (item is null)
            return;

        if (item.IsCustom)
        {
            Settings.CustomBlockedWords.Remove(item.Word);
        }
        else
        {
            Settings.SuppressedBlockedWords.Add(item.Word);
        }

        RefreshBlockedWordItems();
        SyncFilterWithUserList();
    }

    private void RestoreBlockedWord(BlockedWordItem? item)
    {
        if (item is null)
            return;

        Settings.SuppressedBlockedWords.Remove(item.Word);
        RefreshBlockedWordItems();
        SyncFilterWithUserList();
    }

    private void RefreshBlockedWordItems()
    {
        var items = new ObservableCollection<BlockedWordItem>();
        var suppressed = new HashSet<string>(Settings.SuppressedBlockedWords, StringComparer.OrdinalIgnoreCase);
        var custom = new HashSet<string>(Settings.CustomBlockedWords, StringComparer.OrdinalIgnoreCase);

        foreach (var word in ChatboxRelayModerationFilter.BlockedSlurTerms)
        {
            items.Add(new BlockedWordItem(word, IsCustom: false, IsSuppressed: suppressed.Contains(word)));
        }

        foreach (var word in Settings.CustomBlockedWords)
        {
            if (!string.IsNullOrWhiteSpace(word))
            {
                items.Add(new BlockedWordItem(word, IsCustom: true, IsSuppressed: false));
            }
        }

        BlockedWordItems = items;
    }

    private void SyncFilterWithUserList()
    {
        ChatboxRelayModerationFilter.SetUserBlockList(
            Settings.CustomBlockedWords,
            Settings.SuppressedBlockedWords);
    }

    private void OpenTestModeWindow()
    {
        if (testModeWindow is { IsLoaded: true })
        {
            if (testModeWindow.WindowState == WindowState.Minimized)
            {
                testModeWindow.WindowState = WindowState.Normal;
            }

            testModeWindow.Activate();
            testModeWindow.Focus();
            return;
        }

        testModeWindow = new TestModeWindow(this, SelectedTheme)
        {
            Owner = Application.Current?.MainWindow
        };
        testModeWindow.Closed += OnTestModeWindowClosed;
        testModeWindow.Show();
    }

    internal WindowPlacementSnapshot CaptureTwitchChatboxPlacement() =>
        WindowPlacementStateStore.CaptureWindow(
            twitchChatboxWindow,
            WindowPlacementStateStore.TwitchChatboxKey,
            twitchChatboxWindow is { IsVisible: true });

    internal WindowPlacementSnapshot CaptureTestModePlacement() =>
        WindowPlacementStateStore.CaptureWindow(
            testModeWindow,
            WindowPlacementStateStore.TestModeKey,
            testModeWindow is { IsVisible: true });

    internal void RestoreRestartSessionWindows(ApplicationRestartSessionState state)
    {
        var chatboxPlacement = state.Windows.FirstOrDefault(window =>
            string.Equals(window.WindowKey, WindowPlacementStateStore.TwitchChatboxKey, StringComparison.Ordinal));
        if (chatboxPlacement is { WasOpen: true })
        {
            OpenTwitchChatbox();
            WindowPlacementStateStore.ApplyWindowPlacement(twitchChatboxWindow!, chatboxPlacement);
        }

        var testModePlacement = state.Windows.FirstOrDefault(window =>
            string.Equals(window.WindowKey, WindowPlacementStateStore.TestModeKey, StringComparison.Ordinal));
        if (testModePlacement is { WasOpen: true })
        {
            OpenTestModeWindow();
            WindowPlacementStateStore.ApplyWindowPlacement(testModeWindow!, testModePlacement);
        }
    }

    internal async Task<VrChatCurrentLocationLookupResult> PrepareVrChatRestartRejoinAsync()
    {
        if (!Settings.VrChat.IsConnected)
        {
            return VrChatCurrentLocationLookupResult.Unavailable(T("Connect VRChat before using the VRChat restart action."));
        }

        try
        {
            var location = await vrChatApiClient.GetCurrentLocationAsync(Settings.VrChat.AuthCookie, CancellationToken.None);
            if (!location.IsAvailable)
            {
                AppendLog(location.FailureReason);
                location = await TryGetVrChatRestartLocationFromLocalLogAsync(location.FailureReason);
                if (!location.IsAvailable)
                {
                    return location;
                }
            }

            if (string.Equals(location.Source, "local-log", StringComparison.Ordinal))
            {
                AppendLog(T("VRChat restart will use the last real instance found in the local VRChat log because the VRChat API did not report a rejoinable instance."));
            }

            try
            {
                var invited = await vrChatApiClient.InviteMyselfToInstanceAsync(
                    Settings.VrChat.AuthCookie,
                    location.Location,
                    CancellationToken.None);
                AppendLog(invited
                    ? T("Prepared a VRChat self-invite for the restart rejoin path.")
                    : T("VRChat restart will continue without a self-invite because VRChat did not accept the invite request."));
            }
            catch (Exception ex)
            {
                AppendLog(TF("VRChat restart self-invite was skipped: {0}", GetFriendlyVrChatError(ex)));
            }

            return location;
        }
        catch (Exception ex)
        {
            var message = GetFriendlyVrChatError(ex);
            AppendLog(message);
            var fallbackLocation = await TryGetVrChatRestartLocationFromLocalLogAsync(message);
            if (!fallbackLocation.IsAvailable)
            {
                return fallbackLocation;
            }

            AppendLog(T("VRChat restart will use the last real instance found in the local VRChat log because the VRChat API did not report a rejoinable instance."));
            try
            {
                var invited = await vrChatApiClient.InviteMyselfToInstanceAsync(
                    Settings.VrChat.AuthCookie,
                    fallbackLocation.Location,
                    CancellationToken.None);
                AppendLog(invited
                    ? T("Prepared a VRChat self-invite for the restart rejoin path.")
                    : T("VRChat restart will continue without a self-invite because VRChat did not accept the invite request."));
            }
            catch (Exception inviteException)
            {
                AppendLog(TF("VRChat restart self-invite was skipped: {0}", GetFriendlyVrChatError(inviteException)));
            }

            return fallbackLocation;
        }
    }

    private async Task<VrChatCurrentLocationLookupResult> TryGetVrChatRestartLocationFromLocalLogAsync(string apiFailureReason)
    {
        try
        {
            var localLocation = await vrChatLocalClientStateService.ReadLatestJoinedInstanceAsync(CancellationToken.None);
            if (!localLocation.IsAvailable)
            {
                AppendLog(localLocation.FailureReason);
                var combinedFailure = string.IsNullOrWhiteSpace(apiFailureReason)
                    ? localLocation.FailureReason
                    : TF("{0} Local log fallback also failed: {1}", apiFailureReason, localLocation.FailureReason);
                return VrChatCurrentLocationLookupResult.Unavailable(combinedFailure);
            }

            return VrChatCurrentLocationLookupResult.Available(
                localLocation.WorldId,
                localLocation.InstanceId,
                localLocation.Location,
                localLocation.LaunchUri,
                "local-log");
        }
        catch (Exception ex)
        {
            var fallbackFailure = TF("Local VRChat log fallback failed: {0}", ex.Message);
            AppendLog(fallbackFailure);
            var combinedFailure = string.IsNullOrWhiteSpace(apiFailureReason)
                ? fallbackFailure
                : TF("{0} {1}", apiFailureReason, fallbackFailure);
            return VrChatCurrentLocationLookupResult.Unavailable(combinedFailure);
        }
    }

    private void OpenBuiltInCommands()
    {
        var dialog = new BuiltInCommandsWindow(SelectedTheme, this)
        {
            Owner = Application.Current?.MainWindow
        };
        dialog.ShowDialog();
    }

    private void DismissMigrationNotice()
    {
        if (Settings.AvatarSwapMigrationNoticeShown)
        {
            return;
        }

        Settings.AvatarSwapMigrationNoticeShown = true;
        _ = SaveSettingsAsync();
    }

    private void DismissCashPaymentMigrationNotice()
    {
        if (Settings.CashPaymentMigrationNoticeShown)
        {
            return;
        }

        Settings.CashPaymentMigrationNoticeShown = true;
        _ = SaveSettingsAsync();
    }

    private void DismissUiUpdateNotice()
    {
        if (Settings.UiUpdateNoticeShown)
        {
            return;
        }

        Settings.UiUpdateNoticeShown = true;
        _ = SaveSettingsAsync();
    }

    private void OnTwitchChatboxClosed(object? sender, EventArgs e)
    {
        if (twitchChatboxWindow is not null)
        {
            twitchChatboxWindow.Closed -= OnTwitchChatboxClosed;
        }

        twitchChatboxWindow = null;
    }

    private void OnTestModeWindowClosed(object? sender, EventArgs e)
    {
        if (testModeWindow is not null)
        {
            testModeWindow.Closed -= OnTestModeWindowClosed;
        }

        testModeWindow = null;
    }

    private void RaiseThemeStateChanged()
    {
        RaisePropertyChanged(nameof(SelectedTheme));
        RaisePropertyChanged(nameof(IsVoidCrystalThemeSelected));
        RaisePropertyChanged(nameof(IsCustomThemeSelected));
        RaisePropertyChanged(nameof(IsTreetendersArmThemeSelected));
        RaisePropertyChanged(nameof(IsDreamScapeThemeSelected));
        RaisePropertyChanged(nameof(IsMainFrameThemeSelected));
        RaisePropertyChanged(nameof(IsTrashKittyThemeSelected));
        RaisePropertyChanged(nameof(IsBratwurstThemeSelected));
        RaisePropertyChanged(nameof(IsBubblegumThemeSelected));
        RaisePropertyChanged(nameof(IsCosmicPuppyGirlThemeSelected));
        RaisePropertyChanged(nameof(IsPeachesAndCreamThemeSelected));
        RaisePropertyChanged(nameof(IsMoonBunnyWinkThemeSelected));
        RaisePropertyChanged(nameof(IsDreadNightBarThemeSelected));
        RaisePropertyChanged(nameof(IsBakedThemeSelected));
        RaisePropertyChanged(nameof(IsNeonBorbThemeSelected));
        RaisePropertyChanged(nameof(IsStinkyOnlineThemeSelected));
        RaisePropertyChanged(nameof(IsSquishyFoxPlushThemeSelected));
        RaisePropertyChanged(nameof(IsPucaThemeSelected));
        RaisePropertyChanged(nameof(HasCustomThemeBackgroundImage));
        RaisePropertyChanged(nameof(CustomThemeBackgroundImageStatusText));
    }

    private async Task ConnectBroadcasterAsync()
    {
        await AuthenticateAccountAsync(BridgeAccountRole.Broadcaster, TwitchScopes.Broadcaster);
    }

    private async Task ConnectBotAsync()
    {
        await AuthenticateAccountAsync(BridgeAccountRole.Bot, TwitchScopes.Bot);
    }

    private async Task ConnectVrChatAsync()
    {
        var loginWindow = new VrChatLoginWindow(SelectedTheme, vrChatApiClient)
        {
            Owner = Application.Current?.MainWindow
        };

        if (loginWindow.ShowDialog() != true)
        {
            return;
        }

        var account = loginWindow.AccountResult;
        if (account is null)
        {
            VrChatStatus = T("VRChat login completed, but no account details were returned.");
            VrChatAvatarStatus = T("Connect VRChat again when you want to load avatars.");
            RecomputeVrChatConnectionState();
            RefreshCommandStates();
            RefreshVrChatAvatarSelectionOptions();
            return;
        }

        VrChatStatus = T("Connecting to VRChat...");
        VrChatAvatarStatus = T("Waiting for VRChat login to finish.");

        try
        {
            Settings.VrChat.Apply(account);
            AvatarPickerService.SetVrChatAuthCookie(account.AuthCookie);
            RaiseVrChatConnectionStateProperties();
            SyncVrChatRuntimeState(queueManagedRewardSync: false);
            QueueSave();
            AppendLog(TF("Connected VRChat avatar access as {0}.", account.DisplayName));
            StartOrRefreshVrChatLocalOscWatcher();
            QueueLocalVrChatOscAvatarScan(0);
            QueueCurrentVrChatLocalStateRefresh(0);
            await RefreshVrChatAvatarsAsync(forceRemoteRefresh: true);
            RecomputeVrChatConnectionState();
        }
        catch (Exception ex)
        {
            ClearVrChatAccountPreservingCurrentAvatar();
            AvatarPickerService.SetVrChatAuthCookie(null);
            RaiseVrChatConnectionStateProperties();
            SyncVrChatRuntimeState(queueManagedRewardSync: false);
            VrChatStatus = GetFriendlyVrChatError(ex);
            VrChatAvatarStatus = T("VRChat avatar list is unavailable until login succeeds.");
            AppendLog(VrChatStatus);
            QueueSave();
            RecomputeVrChatConnectionState();
        }
        finally
        {
            RefreshCommandStates();
            RefreshVrChatAvatarSelectionOptions();
        }
    }

    // Broadcaster disconnect clears the session-only reward capability flag too,
    // so a later reconnect gets a fresh reward-management check.
    private async Task DisconnectBroadcasterAsync()
    {
        await StopLiveFeedbackHeartbeatAsync();
        ClearBroadcasterManagedRewardsUnavailableForSession();
        broadcasterReconnectRequired = false;
        Settings.Broadcaster.Clear();
        ClearBroadcasterDeviceFlow();
        UpdateAccountStatuses();
        RewardOptions.Clear();
        QueueSave();
        QueueBridgeRefresh();
        AppendLog("Disconnected the broadcaster Twitch account.");
        await Task.CompletedTask;
    }

    private async Task DisconnectBotAsync()
    {
        botReconnectRequired = false;
        Settings.Bot.Clear();
        ClearBotDeviceFlow();
        UpdateAccountStatuses();
        QueueSave();
        QueueBridgeRefresh();
        AppendLog("Disconnected the bot Twitch account.");
        await Task.CompletedTask;
    }

    private async Task DisconnectVrChatAsync()
    {
        await SafeVrChatLogoutAsync(Settings.VrChat.AuthCookie);
        ClearVrChatAccountPreservingCurrentAvatar();
        AvatarPickerService.SetVrChatAuthCookie(null);

        // Keep the on-disk avatar + OSC parameter caches. They are LocalLow
        // sourced and remain valid after a manual disconnect; deleting them
        // would empty the Avatar Swap / Avatar Sets / Wardrobe reward pickers
        // while the user is offline. Users who really want a fresh cache can
        // use the dedicated "Clear VRChat cache" button instead.
        var persistUserId = ResolveCurrentUserIdForCache();
        if (!string.IsNullOrEmpty(persistUserId) && availableVrChatAvatars.Count > 0)
        {
            try
            {
                await settingsStore.SaveVrChatAvatarCacheAsync(
                    persistUserId,
                    availableVrChatAvatars,
                    CancellationToken.None);
            }
            catch
            {
                // best-effort; do not block the disconnect flow
            }
        }

        RaiseVrChatConnectionStateProperties();
        VrChatStatus = T("VRChat avatar access is not connected.");
        VrChatAvatarStatus = T("Showing cached avatars. Connect VRChat to refresh.");
        SyncVrChatRuntimeState(queueManagedRewardSync: false);
        ResetVrChatLocalRuntimeTracking();
        // Restart the LocalLow watcher + scan so cached avatars keep
        // refreshing from VRChat's local files even while disconnected.
        StartOrRefreshVrChatLocalOscWatcher();
        QueueLocalVrChatOscAvatarScan(0);
        QueueSave();
        RefreshCommandStates();
        UpdateAvatarProfileActivityStates();
        RefreshVrChatAvatarSelectionOptions();
        RecomputeVrChatConnectionState();
        AppendLog(T("Disconnected VRChat avatar access. Cached avatars remain available."));
    }

    // Manually wipes the cached VRChat avatar + OSC parameter lists. This is
    // intentionally separate from the disconnect flow so a normal disconnect
    // keeps the cached avatars around for offline editing of Avatar Swap /
    // Avatar Sets / Wardrobe rewards.
    private async Task ClearVrChatCacheAsync()
    {
        try
        {
            await settingsStore.ClearVrChatAvatarCacheAsync(CancellationToken.None);
            await settingsStore.ClearVrChatOscParameterCacheAsync(CancellationToken.None);
        }
        catch (Exception ex)
        {
            AppendLog(TF("Could not clear VRChat cache: {0}", ex.Message));
            return;
        }

        ClearAvailableVrChatAvatars();
        cachedVrChatParametersByAvatarId.Clear();
        AvatarParameterOptions.Clear();
        SetTriggerParameterOptions.Clear();
        SelectedAvatarParameterOption = null;
        SelectedSetTriggerParameterOption = null;
        InvalidateInferredLocalLowUserId();
        VrChatAvatarStatus = T("VRChat avatar cache cleared. Connect VRChat to reload.");
        VrChatOscParameterStatus = T("VRChat OSC parameter cache cleared. Connect VRChat to reload.");
        StartOrRefreshVrChatLocalOscWatcher();
        QueueLocalVrChatOscAvatarScan(0);
        RefreshVrChatAvatarSelectionOptions();
        RefreshCommandStates();
        RecomputeVrChatConnectionState();
        AppendLog(T("Cleared the cached VRChat avatar and OSC parameter lists."));
    }

    private bool HasPersistedVrChatCache()
    {
        // We don't want to hit the secure store on every command evaluation.
        // Use the live in-memory avatar count as a fast proxy and let the
        // secure store checks happen on click if needed. The button stays
        // usable whenever the user has any in-memory cache, which is the
        // realistic case for this command.
        return availableVrChatAvatars.Count > 0
            || !string.IsNullOrEmpty(Settings.VrChat.UserId)
            || !string.IsNullOrEmpty(inferredLocalLowUserId);
    }

    private void SyncVrChatRuntimeState(bool queueManagedRewardSync = true)
    {
        var currentAvatarId = GetBestKnownCurrentAvatarId();
        if (!string.IsNullOrWhiteSpace(currentAvatarId)
            && string.IsNullOrWhiteSpace(Settings.VrChat.CurrentAvatarId))
        {
            Settings.VrChat.CurrentAvatarId = currentAvatarId;
        }

        bridgeCoordinator.UpdateCurrentVrChatAvatar(currentAvatarId);
        UpdateAvatarProfileActivityStates();
        QueueBridgeRefresh();
        if (queueManagedRewardSync)
        {
            QueueManagedRewardSync(0);
        }
    }

    private void ClearVrChatAccountPreservingCurrentAvatar()
    {
        var currentAvatarId = GetBestKnownCurrentAvatarId();
        var displayName = Settings.VrChat.DisplayName?.Trim() ?? string.Empty;
        Settings.VrChat.Clear();
        AvatarPickerService.SetVrChatAuthCookie(null);
        if (!string.IsNullOrWhiteSpace(currentAvatarId))
        {
            Settings.VrChat.CurrentAvatarId = currentAvatarId;
        }
        if (!string.IsNullOrWhiteSpace(displayName))
        {
            Settings.VrChat.DisplayName = displayName;
        }
    }

    private string GetBestKnownCurrentAvatarId()
    {
        var savedAvatarId = Settings.VrChat.CurrentAvatarId?.Trim() ?? string.Empty;
        if (!string.IsNullOrWhiteSpace(savedAvatarId))
        {
            return savedAvatarId;
        }

        return bridgeCoordinator.CurrentVrChatAvatarId?.Trim() ?? string.Empty;
    }

    // Shared Twitch device-auth flow used by both broadcaster and bot login buttons.
    // This keeps the desktop app out of the raw-password business.
    private async Task AuthenticateAccountAsync(BridgeAccountRole accountRole, IReadOnlyCollection<string> scopes)
    {
        await ReloadRuntimeConfigAsync();
        var twitchClientId = runtimeConfig.TwitchClientId.Trim();

        try
        {
            SetAccountStatus(accountRole, "Opening Twitch so you can sign in and approve Crystal Relay...");

            var deviceFlow = await twitchApiClient.StartDeviceAuthorizationAsync(twitchClientId, scopes);
            SetDeviceFlow(accountRole, deviceFlow.UserCode, deviceFlow.VerificationUri);
            SetAccountStatus(accountRole, $"Browser opened. Sign in to Twitch and approve access. If Twitch asks for a code, use {deviceFlow.UserCode}.");
            OpenAuthPage(accountRole);

            AppendLog($"Opened Twitch login for the {accountRole.ToString().ToLowerInvariant()} account. If Twitch asks for a code, use {deviceFlow.UserCode}.");

            var tokens = await WaitForDeviceTokenAsync(twitchClientId, scopes, deviceFlow);
            var validation = await twitchApiClient.ValidateTokenAsync(tokens.AccessToken)
                ?? throw new InvalidOperationException("Twitch returned a token that could not be validated.");

            var user = await twitchApiClient.GetUserAsync(tokens.AccessToken, twitchClientId, validation.UserId);
            var accountSettings = new TwitchAccountSettings
            {
                AccessToken = tokens.AccessToken,
                RefreshToken = tokens.RefreshToken,
                UserId = validation.UserId,
                Login = user?.Login ?? validation.Login,
                DisplayName = user?.DisplayName ?? validation.Login,
                ProfileImageUrl = user?.ProfileImageUrl ?? string.Empty,
                AccessTokenExpiresAt = DateTimeOffset.UtcNow.AddSeconds(tokens.ExpiresIn),
                SessionRenewalDueAt = DateTimeOffset.UtcNow.Add(TwitchPublicSessionWindow),
                Scopes = validation.Scopes
            };

            if (accountRole == BridgeAccountRole.Broadcaster)
            {
                ClearBroadcasterManagedRewardsUnavailableForSession();
                broadcasterReconnectRequired = false;
                Settings.Broadcaster.Apply(accountSettings);
                ClearBroadcasterDeviceFlow();
            }
            else
            {
                botReconnectRequired = false;
                Settings.Bot.Apply(accountSettings);
                ClearBotDeviceFlow();
            }

            UpdateAccountStatuses();
            QueueSave();
            await RefreshBridgeImmediatelyAfterTwitchLoginAsync(accountRole);
            await QueueRewardRefreshAsync();
            QueueManagedRewardSync(0);
            await RefreshAboutProfilesAsync();
            AppendLog($"Connected {accountRole.ToString().ToLowerInvariant()} account as {accountSettings.DisplayName}.");
        }
        catch (Exception ex)
        {
            var friendlyMessage = GetFriendlyAuthError(ex);
            SetAccountStatus(accountRole, friendlyMessage);
            AppendLog(friendlyMessage);
        }
        finally
        {
            RefreshCommandStates();
        }
    }

    private async Task RefreshBridgeImmediatelyAfterTwitchLoginAsync(BridgeAccountRole accountRole)
    {
        if (!isInitialized)
        {
            return;
        }

        bridgeRefreshCancellation?.Cancel();
        bridgeRefreshCancellation?.Dispose();
        bridgeRefreshCancellation = null;

        try
        {
            await bridgeRefreshGate.WaitAsync(CancellationToken.None);
            try
            {
                AppendLog($"Refreshing background bridge immediately after {accountRole.ToString().ToLowerInvariant()} Twitch login.");
                await EnsureBridgeStateAsync(CancellationToken.None, forceDiscoveryRefresh: true);
            }
            finally
            {
                bridgeRefreshGate.Release();
            }
        }
        catch (Exception ex)
        {
            AppendLog($"Twitch login succeeded, but Crystal Relay could not refresh the background bridge immediately: {ex.Message}");
            QueueBridgeRefresh();
        }
    }

    private async Task<TwitchApiClient.TokenExchangeResponse> WaitForDeviceTokenAsync(
        string clientId,
        IReadOnlyCollection<string> scopes,
        TwitchApiClient.DeviceCodeResponse deviceFlow)
    {
        var interval = TimeSpan.FromSeconds(Math.Max(2, deviceFlow.Interval));
        var expiresAt = DateTimeOffset.UtcNow.AddSeconds(deviceFlow.ExpiresIn);

        while (DateTimeOffset.UtcNow < expiresAt)
        {
            await Task.Delay(interval);

            try
            {
                return await twitchApiClient.ExchangeDeviceCodeAsync(clientId, scopes, deviceFlow.DeviceCode);
            }
            catch (TwitchApiException ex) when (string.Equals(ex.ApiMessage, "authorization_pending", StringComparison.OrdinalIgnoreCase))
            {
                continue;
            }
            catch (TwitchApiException ex) when (string.Equals(ex.ApiMessage, "slow_down", StringComparison.OrdinalIgnoreCase))
            {
                interval += TimeSpan.FromSeconds(5);
            }
            catch (TwitchApiException ex) when (string.Equals(ex.ApiMessage, "access_denied", StringComparison.OrdinalIgnoreCase))
            {
                throw new InvalidOperationException("Twitch authorization was denied.");
            }
            catch (TwitchApiException ex) when (ex.ApiMessage.Contains("expired", StringComparison.OrdinalIgnoreCase))
            {
                throw new InvalidOperationException("Twitch authorization expired before it completed.");
            }
        }

        throw new TimeoutException("Timed out waiting for Twitch authorization.");
    }

    private async Task InitializeVrChatAsync()
    {
        RaiseVrChatConnectionStateProperties();

        if (!Settings.VrChat.IsConnected)
        {
            AvatarPickerService.SetVrChatAuthCookie(null);
        }
        else
        {
            AvatarPickerService.SetVrChatAuthCookie(Settings.VrChat.AuthCookie);
        }

        var startupUserId = ResolveCurrentUserIdForCache();
        if (!string.IsNullOrEmpty(startupUserId))
        {
            var cachedAvatars = await settingsStore.LoadVrChatAvatarCacheAsync(startupUserId, CancellationToken.None);
            ReplaceAvailableVrChatAvatars(cachedAvatars);
        }

        StartOrRefreshVrChatLocalOscWatcher();
        QueueLocalVrChatOscAvatarScan(0);

        if (!Settings.VrChat.IsConnected)
        {
            VrChatStatus = T("VRChat avatar access is not connected.");
            VrChatOscParameterStatus = T("Connect VRChat to load avatar parameters.");
            ResetVrChatLocalRuntimeTracking();

            var inferredUserId = ResolveCurrentUserIdForCache();
            if (!string.IsNullOrEmpty(inferredUserId))
            {
                try
                {
                    var localAvatars = await vrChatLocalOscCacheService
                        .LoadKnownAvatarsAsync(inferredUserId, CancellationToken.None);
                    if (localAvatars.Count > 0)
                    {
                        RunOnUi(() => ApplyLocalVrChatOscAvatars(localAvatars));
                    }
                }
                catch
                {
                    // best-effort; the watcher and managed-reward sync handle the persistent case
                }
            }

            await ScanLocalVrChatOscAvatarCacheAsync(CancellationToken.None);
            QueueCurrentVrChatLocalStateRefresh(0);
            UpdateAvatarProfileActivityStates();
            VrChatAvatarStatus = availableVrChatAvatars.Count == 0
                ? T("No cached avatars yet. Connect VRChat once to build the cache.")
                : TF("Showing {0} cached avatars. Connect VRChat to refresh from the API.", availableVrChatAvatars.Count);
            RefreshVrChatAvatarSelectionOptions();
            RecomputeVrChatConnectionState();
            RefreshCommandStates();
            return;
        }

        if (NormalizeSupporterAvatarScopes())
        {
            QueueSave(0);
            QueueBridgeRefresh();
        }
        await ScanLocalVrChatOscAvatarCacheAsync(CancellationToken.None);
        QueueCurrentVrChatLocalStateRefresh(0);
        UpdateAvatarProfileActivityStates();

        if (availableVrChatAvatars.Count > 0)
        {
            VrChatStatus = TF("Connected to VRChat as {0}.", Settings.VrChat.DisplayName);
            VrChatAvatarStatus = TF("Loaded {0} saved avatars. Checking VRChat once for updates...", availableVrChatAvatars.Count);
            SyncVrChatAvatarRuleLabels();
            RefreshVrChatAvatarSelectionOptions();
        }

        if (availableVrChatAvatars.Count == 0)
        {
            VrChatStatus = TF("Connected to VRChat as {0}.", Settings.VrChat.DisplayName);
            VrChatAvatarStatus = T("Pulling your VRChat avatar list...");
            SyncVrChatAvatarRuleLabels();
            RefreshVrChatAvatarSelectionOptions();
        }

        await EnsureSelectedAvatarParameterCacheLoadedAsync();
        await RefreshVrChatAvatarsAsync(forceRemoteRefresh: true);
        RecomputeVrChatConnectionState();
    }

    private async Task RefreshVrChatAvatarsAsync()
    {
        await RefreshVrChatAvatarsAsync(forceRemoteRefresh: true);
    }

    private bool CanRefreshVrChatAvatars()
    {
        return Settings.VrChat.IsConnected
            || VrChatConnectionState == VrChatConnectionState.Cached
            || availableVrChatAvatars.Count > 0
            || !string.IsNullOrWhiteSpace(Settings.VrChat.UserId)
            || !string.IsNullOrWhiteSpace(inferredLocalLowUserId);
    }

    private async Task RefreshVrChatAvatarsAsync(bool forceRemoteRefresh)
    {
        if (!Settings.VrChat.IsConnected)
        {
            // Offline refresh: load the secure cache for the inferred userId,
            // then let the LocalLow scan merge in any fresh names. This keeps
            // the Avatar Swap / Avatar Sets / Wardrobe reward pickers usable
            // after a disconnect instead of silently emptying them.
            AvatarPickerService.SetVrChatAuthCookie(null);
            var offlineUserId = ResolveCurrentUserIdForCache();
            if (!string.IsNullOrEmpty(offlineUserId))
            {
                var cachedAvatars = await settingsStore.LoadVrChatAvatarCacheAsync(offlineUserId, CancellationToken.None);
                ReplaceAvailableVrChatAvatars(cachedAvatars);
            }
            StartOrRefreshVrChatLocalOscWatcher();
            await ScanLocalVrChatOscAvatarCacheAsync(CancellationToken.None);
            VrChatStatus = T("VRChat avatar access is not connected.");
            VrChatAvatarStatus = availableVrChatAvatars.Count == 0
                ? T("No cached avatars yet. Connect VRChat once to build the cache.")
                : TF("Showing {0} cached avatars. Connect VRChat to refresh from the API.", availableVrChatAvatars.Count);
            RefreshVrChatAvatarSelectionOptions();
            RefreshCommandStates();
            RecomputeVrChatConnectionState();
            return;
        }

        AvatarPickerService.SetVrChatAuthCookie(Settings.VrChat.AuthCookie);
        if (!forceRemoteRefresh)
        {
            var cachedAvatars = await settingsStore.LoadVrChatAvatarCacheAsync(Settings.VrChat.UserId, CancellationToken.None);
            ReplaceAvailableVrChatAvatars(cachedAvatars);
            if (NormalizeSupporterAvatarScopes())
            {
                QueueSave(0);
                QueueBridgeRefresh();
            }
            StartOrRefreshVrChatLocalOscWatcher();
            await ScanLocalVrChatOscAvatarCacheAsync(CancellationToken.None);
            QueueCurrentVrChatLocalStateRefresh(0);
            UpdateAvatarProfileActivityStates();
            VrChatAvatarStatus = cachedAvatars.Count == 0
                ? T("No saved avatar list yet. Use Refresh Avatar List to load your VRChat avatars.")
                : TF("Loaded {0} saved avatars.", cachedAvatars.Count);
            RefreshVrChatAvatarSelectionOptions();
            await EnsureSelectedAvatarParameterCacheLoadedAsync();
            RefreshCommandStates();
            RecomputeVrChatConnectionState();
            return;
        }

        VrChatAvatarStatus = T("Refreshing VRChat avatar list from VRChat...");

        try
        {
            var account = await vrChatApiClient.GetCurrentUserAsync(Settings.VrChat.AuthCookie, CancellationToken.None);
            var avatars = await vrChatApiClient.GetSelectableAvatarsAsync(
                account.AuthCookie,
                account.CurrentAvatarId,
                CancellationToken.None);

            Settings.VrChat.Apply(account);
            AvatarPickerService.SetVrChatAuthCookie(account.AuthCookie);
            ReplaceAvailableVrChatAvatars(avatars);
            SyncVrChatRuntimeState(queueManagedRewardSync: false);
            if (NormalizeSupporterAvatarScopes())
            {
                QueueSave(0);
                QueueBridgeRefresh();
            }
            await settingsStore.SaveVrChatAvatarCacheAsync(account.UserId, avatars, CancellationToken.None);
            StartOrRefreshVrChatLocalOscWatcher();
            await ScanLocalVrChatOscAvatarCacheAsync(CancellationToken.None);
            QueueCurrentVrChatLocalStateRefresh(0);
            SyncVrChatAvatarRuleLabels();
            UpdateAvatarProfileActivityStates();

            VrChatStatus = TF("Connected to VRChat as {0}.", account.DisplayName);
            VrChatAvatarStatus = avatars.Count == 0
                ? T("VRChat returned no avatar choices for this account yet.")
                : TF("Loaded {0} avatars and saved them securely. Use Refresh Avatar List when you upload new ones.", avatars.Count);

            QueueSave();
            AppendLog(avatars.Count == 0
                ? TF("Connected VRChat avatar access as {0}, but no avatars were returned.", account.DisplayName)
                : TF("Loaded {0} VRChat avatars for {1}.", avatars.Count, account.DisplayName));
            await RefreshVrChatOscParametersAsync(suppressErrors: true);
            RecomputeVrChatConnectionState();
        }
        catch (Exception ex)
        {
            if (ex is VrChatApiException apiException
                && apiException.StatusCode == System.Net.HttpStatusCode.Unauthorized)
            {
                await HandleVrChatUnauthorizedAsync(CancellationToken.None);
                RaiseVrChatConnectionStateProperties();
                SyncVrChatRuntimeState(queueManagedRewardSync: false);
                VrChatStatus = T("VRChat avatar access is not connected.");
                VrChatAvatarStatus = availableVrChatAvatars.Count == 0
                    ? T("No cached avatars yet. Connect VRChat once to build the cache.")
                    : TF("Showing {0} cached avatars. Connect VRChat to refresh from the API.", availableVrChatAvatars.Count);
                VrChatOscParameterStatus = T("Pick an avatar set to load its saved OSC parameters.");
                AppendLog(T("Disconnected VRChat avatar access. Cached avatars remain available."));
                RecordSavedLoginRecoverySignal();
                QueueSave();
            }
            else
            {
                VrChatStatus = GetFriendlyVrChatError(ex);
                VrChatAvatarStatus = availableVrChatAvatars.Count == 0
                    ? T("Crystal Relay could not refresh the VRChat avatar list.")
                    : TF("Crystal Relay could not refresh VRChat right now. Using {0} saved avatars.", availableVrChatAvatars.Count);
                AppendLog(VrChatStatus);
            }
        }
        finally
        {
            UpdateAvatarProfileActivityStates();
            RefreshVrChatAvatarSelectionOptions();
            RefreshAvatarParameterOptions();
            RefreshCommandStates();
            QueueManagedRewardSync();
        }
    }

    private void RefreshVrChatAvatarSelectionOptions()
    {
        RunOnUi(() =>
        {
            var isEditingPowerUp = IsViewingPowerUps && SelectedPowerUpRule is not null;
            var needsProfileAvatarOptions = !IsViewingPowerUps;
            var needsSupporterAvatarOptions = false;
            var needsAvatarChangeOptions = SelectedRule?.ActionType == OscActionType.AvatarChange;
            var needsPowerUpAvatarOptions = isEditingPowerUp;
            if (!needsProfileAvatarOptions && !needsSupporterAvatarOptions && !needsAvatarChangeOptions && !needsPowerUpAvatarOptions)
            {
                RaisePropertyChanged(nameof(HasVrChatAvatarOptions));
                return;
            }

            var selectedProfileAvatarId = SelectedAvatarProfile?.AvatarId;
            var selectedProfileAvatarName = SelectedAvatarProfile?.AvatarName;
            var selectedPowerUpAvatarId = SelectedPowerUpRule?.AvatarId ?? string.Empty;
            var selectedPowerUpAvatarName = SelectedPowerUpRule?.AvatarName ?? string.Empty;
            var selectedAvatarId = SelectedRule?.ActionType == OscActionType.AvatarChange
                ? SelectedRule.AvatarChangeTargetId
                : needsSupporterAvatarOptions
                    ? SelectedRule?.SupporterAvatarId ?? string.Empty
                    : needsPowerUpAvatarOptions
                        ? selectedPowerUpAvatarId
                        : string.Empty;
            var selectedResetAvatarId = SelectedRule?.ActionType == OscActionType.AvatarChange
                ? SelectedRule.AvatarChangeResetId
                : string.Empty;
            var selectedAvatarName = SelectedRule?.ActionType == OscActionType.AvatarChange
                ? SelectedRule.AvatarTargetName
                : needsSupporterAvatarOptions
                    ? SelectedRule?.SupporterAvatarName ?? string.Empty
                    : needsPowerUpAvatarOptions
                        ? selectedPowerUpAvatarName
                        : string.Empty;
            var selectedResetAvatarName = SelectedRule?.ActionType == OscActionType.AvatarChange
                ? SelectedRule.ResetAvatarName
                : string.Empty;
            isRefreshingVrChatAvatarSelectionOptions = true;
            try
            {
                if (needsProfileAvatarOptions)
                {
                    ReplaceCollectionIfChanged(
                        ProfileAvatarOptions,
                        BuildVrChatAvatarOptionSet(selectedProfileAvatarId, selectedProfileAvatarName, "Selected avatar"));
                }

                if (needsAvatarChangeOptions || needsSupporterAvatarOptions || needsPowerUpAvatarOptions)
                {
                    List<VrChatAvatarOption> avatarOptions = BuildVrChatAvatarOptionSet(
                        selectedAvatarId,
                        selectedAvatarName,
                        needsSupporterAvatarOptions
                            ? "Selected supporter avatar"
                            : needsPowerUpAvatarOptions && !needsAvatarChangeOptions
                                ? "Selected Power Up avatar"
                                : "Selected target avatar");

                    if (needsPowerUpAvatarOptions)
                    {
                        var existingIds = new HashSet<string>(avatarOptions.Select(option => option.Id), StringComparer.Ordinal);
                        EnsureCustomAvatarOption(
                            avatarOptions,
                            existingIds,
                            selectedPowerUpAvatarId,
                            selectedPowerUpAvatarName,
                            "Selected Power Up avatar");
                    }

                    ReplaceCollectionIfChanged(
                        VrChatAvatarOptions,
                        avatarOptions);
                }

                if (needsAvatarChangeOptions)
                {
                    var resetOptions = new List<VrChatAvatarOption>
                    {
                        new(string.Empty, string.Empty, "Do not switch back", "Do not switch back", string.Empty, false)
                    };

                    resetOptions.AddRange(BuildVrChatAvatarOptionSet(selectedResetAvatarId, selectedResetAvatarName, "Selected return avatar"));

                    ReplaceCollectionIfChanged(VrChatResetAvatarOptions, resetOptions);
                }

                if (needsProfileAvatarOptions && SelectedAvatarProfile is not null)
                {
                    if (!string.Equals(SelectedAvatarProfile.AvatarId, selectedProfileAvatarId, StringComparison.Ordinal))
                    {
                        SelectedAvatarProfile.AvatarId = selectedProfileAvatarId ?? string.Empty;
                    }

                    if (!string.Equals(SelectedAvatarProfile.AvatarName, selectedProfileAvatarName, StringComparison.Ordinal))
                    {
                        SelectedAvatarProfile.AvatarName = selectedProfileAvatarName ?? string.Empty;
                    }
                }

                if (needsAvatarChangeOptions && SelectedRule?.ActionType == OscActionType.AvatarChange)
                {
                    if (!string.Equals(SelectedRule.AvatarTargetName, selectedAvatarName, StringComparison.Ordinal))
                    {
                        SelectedRule.AvatarTargetName = selectedAvatarName;
                    }

                    if (!string.Equals(SelectedRule.AvatarChangeTargetId, selectedAvatarId, StringComparison.Ordinal))
                    {
                        SelectedRule.AvatarChangeTargetId = selectedAvatarId;
                    }

                    if (!string.Equals(SelectedRule.AvatarChangeResetId, selectedResetAvatarId, StringComparison.Ordinal))
                    {
                        SelectedRule.AvatarChangeResetId = selectedResetAvatarId;
                    }

                    if (!string.Equals(SelectedRule.ResetAvatarName, selectedResetAvatarName, StringComparison.Ordinal))
                    {
                        SelectedRule.ResetAvatarName = selectedResetAvatarName;
                    }
                }

                if (needsSupporterAvatarOptions && SelectedRule is not null)
                {
                    if (!string.Equals(SelectedRule.SupporterAvatarName, selectedAvatarName, StringComparison.Ordinal))
                    {
                        SelectedRule.SupporterAvatarName = selectedAvatarName;
                    }

                    if (!string.Equals(SelectedRule.SupporterAvatarId, selectedAvatarId, StringComparison.Ordinal))
                    {
                        SelectedRule.SupporterAvatarId = selectedAvatarId;
                    }
                }

                if (needsPowerUpAvatarOptions && SelectedPowerUpRule is not null)
                {
                    if (!string.Equals(SelectedPowerUpRule.AvatarName, selectedPowerUpAvatarName, StringComparison.Ordinal))
                    {
                        SelectedPowerUpRule.AvatarName = selectedPowerUpAvatarName;
                    }

                    if (!string.Equals(SelectedPowerUpRule.AvatarId, selectedPowerUpAvatarId, StringComparison.Ordinal))
                    {
                        SelectedPowerUpRule.AvatarId = selectedPowerUpAvatarId;
                    }
                }
            }
            finally
            {
                isRefreshingVrChatAvatarSelectionOptions = false;
            }

            RaisePropertyChanged(nameof(HasVrChatAvatarOptions));
        });
    }

    private void SyncVrChatAvatarRuleLabels()
    {
        foreach (var profile in Settings.AvatarProfiles)
        {
            if (!string.IsNullOrWhiteSpace(profile.AvatarId))
            {
                var resolvedProfileAvatarName = ResolveVrChatAvatarName(profile.AvatarId);
                if (!string.IsNullOrWhiteSpace(resolvedProfileAvatarName))
                {
                    profile.AvatarName = resolvedProfileAvatarName;
                }
            }
        }

        foreach (var rule in EnumerateAllRules())
        {
            if (rule.ActionType == OscActionType.AvatarChange)
            {
                SyncVrChatAvatarRuleLabel(rule, false);
                SyncVrChatAvatarRuleLabel(rule, true);
            }
            else if (rule.ActionType == OscActionType.AvatarRoulet)
            {
                SyncVrChatAvatarRouletPoolLabels(rule);
            }

            if (Settings.GlobalOverrideRules.Contains(rule)
                && !IsSupporterAvatarChangeOverride(rule)
                && !string.IsNullOrWhiteSpace(rule.SupporterAvatarId))
            {
                var supporterAvatarName = ResolveVrChatAvatarName(rule.SupporterAvatarId);
                if (!string.IsNullOrWhiteSpace(supporterAvatarName))
                {
                    rule.SupporterAvatarName = supporterAvatarName;
                }
            }
        }
    }

    private void SyncVrChatAvatarRuleLabel(TriggerRule rule, bool isResetAvatar)
    {
        var avatarId = isResetAvatar ? rule.AvatarChangeResetId : rule.AvatarChangeTargetId;
        var normalizedId = avatarId?.Trim() ?? string.Empty;
        if (string.IsNullOrWhiteSpace(normalizedId))
        {
            if (isResetAvatar)
            {
                rule.ResetAvatarName = string.Empty;
            }
            else
            {
                rule.AvatarTargetName = string.Empty;
            }

            return;
        }

        var avatarName = ResolveVrChatAvatarName(normalizedId);
        if (string.IsNullOrWhiteSpace(avatarName))
        {
            return;
        }

        if (isResetAvatar)
        {
            rule.ResetAvatarName = avatarName;
            return;
        }

        rule.AvatarTargetName = avatarName;
    }

    private void SyncVrChatAvatarRouletPoolLabels(TriggerRule rule)
    {
        var organizedEntries = BuildVrChatAvatarRouletPoolEntries(rule);
        var normalizedIds = organizedEntries
            .Select(entry => entry.AvatarId)
            .ToArray();
        var resolvedNames = organizedEntries
            .Select(entry => entry.AvatarName)
            .ToArray();

        if (!rule.AvatarRouletAvatarIds.SequenceEqual(normalizedIds, StringComparer.Ordinal))
        {
            rule.AvatarRouletAvatarIds = new ObservableCollection<string>(normalizedIds);
        }

        if (!rule.AvatarRouletAvatarNames.SequenceEqual(resolvedNames, StringComparer.Ordinal))
        {
            rule.AvatarRouletAvatarNames = new ObservableCollection<string>(resolvedNames);
        }
    }

    private IReadOnlyList<(string AvatarId, string AvatarName)> BuildVrChatAvatarRouletPoolEntries(TriggerRule rule)
    {
        var existingIds = rule.AvatarRouletAvatarIds
            .Select(avatarId => avatarId?.Trim() ?? string.Empty)
            .ToArray();
        var existingNames = rule.AvatarRouletAvatarNames
            .Select(avatarName => avatarName?.Trim() ?? string.Empty)
            .ToArray();
        var entries = new List<(string AvatarId, string AvatarName)>();
        var seenAvatarIds = new HashSet<string>(StringComparer.Ordinal);

        for (var index = 0; index < existingIds.Length; index++)
        {
            var avatarId = existingIds[index];
            if (string.IsNullOrWhiteSpace(avatarId) || !seenAvatarIds.Add(avatarId))
            {
                continue;
            }

            var resolvedName = ResolveVrChatAvatarName(avatarId);
            var fallbackName = index < existingNames.Length ? existingNames[index] : string.Empty;
            entries.Add((avatarId, string.IsNullOrWhiteSpace(resolvedName) ? fallbackName : resolvedName));
        }

        return entries;
    }

    public string ResolveVrChatAvatarName(string? avatarId)
    {
        var normalizedId = avatarId?.Trim() ?? string.Empty;
        if (string.IsNullOrWhiteSpace(normalizedId))
        {
            return string.Empty;
        }

        if (!availableVrChatAvatarNamesById.TryGetValue(normalizedId, out var avatarName))
        {
            return ResolveSavedVrChatAvatarName(normalizedId);
        }

        var normalizedName = avatarName?.Trim() ?? string.Empty;
        var resolvedName = string.IsNullOrWhiteSpace(normalizedName)
            || string.Equals(normalizedName, normalizedId, StringComparison.Ordinal)
            ? string.Empty
            : normalizedName;
        return string.IsNullOrWhiteSpace(resolvedName)
            ? ResolveSavedVrChatAvatarName(normalizedId)
            : resolvedName;
    }

    private string ResolveSavedVrChatAvatarName(string normalizedAvatarId)
    {
        if (string.IsNullOrWhiteSpace(normalizedAvatarId))
        {
            return string.Empty;
        }

        foreach (var profile in Settings.AvatarProfiles)
        {
            if (string.Equals(profile.AvatarId?.Trim() ?? string.Empty, normalizedAvatarId, StringComparison.Ordinal)
                && !string.IsNullOrWhiteSpace(profile.AvatarName))
            {
                return profile.AvatarName.Trim();
            }

            var matchingRuleName = ResolveSavedVrChatAvatarNameFromRules(profile.ChannelPointRules, normalizedAvatarId);
            if (!string.IsNullOrWhiteSpace(matchingRuleName))
            {
                return matchingRuleName;
            }
        }

        var supporterRuleName = ResolveSavedVrChatAvatarNameFromRules(Settings.GlobalOverrideRules, normalizedAvatarId);
        if (!string.IsNullOrWhiteSpace(supporterRuleName))
        {
            return supporterRuleName;
        }

        foreach (var rule in Settings.PowerUpRules)
        {
            if (string.Equals(rule.AvatarId?.Trim() ?? string.Empty, normalizedAvatarId, StringComparison.Ordinal)
                && !string.IsNullOrWhiteSpace(rule.AvatarName))
            {
                return rule.AvatarName.Trim();
            }

            var powerUpActionName = ResolveSavedVrChatAvatarNameFromRule(rule.ActionRule, normalizedAvatarId);
            if (!string.IsNullOrWhiteSpace(powerUpActionName))
            {
                return powerUpActionName;
            }
        }

        return string.Empty;
    }

    private static string ResolveSavedVrChatAvatarNameFromRules(IEnumerable<TriggerRule> rules, string normalizedAvatarId)
    {
        foreach (var rule in rules)
        {
            var matchingName = ResolveSavedVrChatAvatarNameFromRule(rule, normalizedAvatarId);
            if (!string.IsNullOrWhiteSpace(matchingName))
            {
                return matchingName;
            }
        }

        return string.Empty;
    }

    private static string ResolveSavedVrChatAvatarNameFromRule(TriggerRule rule, string normalizedAvatarId)
    {
        if (string.IsNullOrWhiteSpace(normalizedAvatarId))
        {
            return string.Empty;
        }

        if (string.Equals(rule.AvatarChangeTargetId?.Trim() ?? string.Empty, normalizedAvatarId, StringComparison.Ordinal)
            && !string.IsNullOrWhiteSpace(rule.AvatarTargetName))
        {
            return rule.AvatarTargetName.Trim();
        }

        if (string.Equals(rule.AvatarChangeResetId?.Trim() ?? string.Empty, normalizedAvatarId, StringComparison.Ordinal)
            && !string.IsNullOrWhiteSpace(rule.ResetAvatarName))
        {
            return rule.ResetAvatarName.Trim();
        }

        if (string.Equals(rule.SupporterAvatarId?.Trim() ?? string.Empty, normalizedAvatarId, StringComparison.Ordinal)
            && !string.IsNullOrWhiteSpace(rule.SupporterAvatarName))
        {
            return rule.SupporterAvatarName.Trim();
        }

        return string.Empty;
    }

    private string ResolveVrChatAvatarIdByName(string? avatarName)
    {
        var normalizedName = avatarName?.Trim() ?? string.Empty;
        if (string.IsNullOrWhiteSpace(normalizedName))
        {
            return string.Empty;
        }

        var matchingAvatarIds = availableVrChatAvatars
            .Where(avatar =>
                !string.IsNullOrWhiteSpace(avatar.Id)
                && string.Equals(avatar.Name?.Trim() ?? string.Empty, normalizedName, StringComparison.OrdinalIgnoreCase))
            .Select(avatar => avatar.Id)
            .Distinct(StringComparer.Ordinal)
            .ToArray();
        if (matchingAvatarIds.Length == 1)
        {
            return matchingAvatarIds[0];
        }

        return string.Empty;
    }

    private string GetResolvedCurrentVrChatAvatarId()
    {
        var currentAvatarId = Settings.VrChat.CurrentAvatarId?.Trim() ?? string.Empty;
        if (string.IsNullOrWhiteSpace(currentAvatarId))
        {
            return string.Empty;
        }

        return currentAvatarId;
    }

    public string CurrentVrChatAvatarId => GetResolvedCurrentVrChatAvatarId();

    public BridgeCoordinator Coordinator => bridgeCoordinator;

    public bool IsCurrentAvatarParameterAvailable(string parameterAddress)
    {
        var normalizedAddress = parameterAddress?.Trim() ?? string.Empty;
        if (string.IsNullOrWhiteSpace(normalizedAddress)) return false;
        var normalizedAvatarId = Settings.VrChat.CurrentAvatarId?.Trim() ?? string.Empty;
        if (string.IsNullOrWhiteSpace(normalizedAvatarId)) return false;
        if (!cachedVrChatParametersByAvatarId.TryGetValue(normalizedAvatarId, out var avatarParameters) || avatarParameters.Count == 0)
        {
            return false;
        }
        return avatarParameters.Any(p => string.Equals(p.Address?.Trim() ?? string.Empty, normalizedAddress, StringComparison.Ordinal));
    }

    public Task SaveSettingsAsync(CancellationToken cancellationToken = default)
        => settingsStore.SaveAsync(Settings, cancellationToken);

    public Task SynchronizeUniversalManagedRewardsAsync()
        => SynchronizeManagedChannelPointRewardsAsync(CancellationToken.None);

    public async Task ImportFoomaAndSyncAsync()
    {
        await ImportFoomaInteractionConfigAsync();
    }

    private static string GetSafeVrChatAvatarDisplayName(string? avatarName, string fallbackLabel = "Unknown avatar")
    {
        var normalizedName = avatarName?.Trim() ?? string.Empty;
        return string.IsNullOrWhiteSpace(normalizedName) ? fallbackLabel : normalizedName;
    }

    private static string GetAvatarDuplicateHint(string avatarId)
    {
        var normalizedId = avatarId?.Trim() ?? string.Empty;
        if (string.IsNullOrWhiteSpace(normalizedId))
        {
            return string.Empty;
        }

        const int hintLength = 6;
        var suffixLength = Math.Min(hintLength, normalizedId.Length);
        return $"ID ending {normalizedId[^suffixLength..]}";
    }

    private List<VrChatAvatarOption> BuildVrChatAvatarOptionSet(
        string? selectedAvatarId,
        string? selectedAvatarName,
        string fallbackLabel)
    {
        var allOptions = BuildAllSelectableVrChatAvatarOptions().ToList();

        var options = new List<VrChatAvatarOption>(allOptions);
        var existingIds = new HashSet<string>(options.Select(option => option.Id), StringComparer.Ordinal);
        var normalizedSelectedAvatarId = selectedAvatarId?.Trim() ?? string.Empty;
        if (!string.IsNullOrWhiteSpace(normalizedSelectedAvatarId))
        {
            var existingOption = allOptions.FirstOrDefault(option => string.Equals(option.Id, normalizedSelectedAvatarId, StringComparison.Ordinal));
            if (existingOption is not null && !existingIds.Contains(existingOption.Id))
            {
                options.Insert(0, existingOption);
                existingIds.Add(existingOption.Id);
            }
        }

        EnsureCustomAvatarOption(options, existingIds, normalizedSelectedAvatarId, selectedAvatarName, fallbackLabel);
        return options;
    }

    private IReadOnlyList<VrChatAvatarOption> BuildAllSelectableVrChatAvatarOptions()
    {
        var selectableAvatars = BuildKnownSelectableVrChatAvatars();
        var duplicateNameKeys = selectableAvatars
            .GroupBy(
                avatar => avatar.Name?.Trim() ?? string.Empty,
                StringComparer.OrdinalIgnoreCase)
            .Where(group => !string.IsNullOrWhiteSpace(group.Key) && group.Count() > 1)
            .Select(group => group.Key)
            .ToHashSet(StringComparer.OrdinalIgnoreCase);

        return selectableAvatars
            .Select(avatar => CreateVrChatAvatarOption(avatar, duplicateNameKeys))
            .OrderBy(option => option.DisplayLabel, StringComparer.OrdinalIgnoreCase)
            .ToArray();
    }

    private IReadOnlyList<VrChatAvatarSummary> BuildKnownSelectableVrChatAvatars()
    {
        var avatarsById = new Dictionary<string, VrChatAvatarSummary>(StringComparer.Ordinal);
        var currentAvatarId = Settings.VrChat.CurrentAvatarId?.Trim() ?? string.Empty;

        foreach (var avatar in availableVrChatAvatars)
        {
            AddKnownSelectableVrChatAvatar(
                avatarsById,
                avatar.Id,
                avatar.Name,
                avatar.IsCurrentAvatar || string.Equals(avatar.Id, currentAvatarId, StringComparison.Ordinal));
        }

        AddKnownSelectableVrChatAvatar(
            avatarsById,
            currentAvatarId,
            ResolveVrChatAvatarName(currentAvatarId),
            isCurrentAvatar: true);

        foreach (var profile in Settings.AvatarProfiles)
        {
            AddKnownSelectableVrChatAvatar(
                avatarsById,
                profile.AvatarId,
                profile.AvatarName,
                string.Equals(profile.AvatarId?.Trim() ?? string.Empty, currentAvatarId, StringComparison.Ordinal));

            foreach (var rule in profile.ChannelPointRules)
            {
                AddKnownSelectableAvatarTargets(avatarsById, rule, currentAvatarId);
            }
        }

        foreach (var rule in Settings.GlobalOverrideRules)
        {
            AddKnownSelectableVrChatAvatar(
                avatarsById,
                rule.SupporterAvatarId,
                rule.SupporterAvatarName,
                string.Equals(rule.SupporterAvatarId?.Trim() ?? string.Empty, currentAvatarId, StringComparison.Ordinal));
            AddKnownSelectableAvatarTargets(avatarsById, rule, currentAvatarId);
        }

        foreach (var rule in Settings.PowerUpRules)
        {
            AddKnownSelectableVrChatAvatar(
                avatarsById,
                rule.AvatarId,
                rule.AvatarName,
                string.Equals(rule.AvatarId?.Trim() ?? string.Empty, currentAvatarId, StringComparison.Ordinal));
            AddKnownSelectableAvatarTargets(avatarsById, rule.ActionRule, currentAvatarId);
        }

        return avatarsById.Values.ToArray();
    }

    private static void AddKnownSelectableAvatarTargets(
        IDictionary<string, VrChatAvatarSummary> avatarsById,
        TriggerRule rule,
        string currentAvatarId)
    {
        if (rule.ActionType != OscActionType.AvatarChange)
        {
            return;
        }

        AddKnownSelectableVrChatAvatar(
            avatarsById,
            rule.AvatarChangeTargetId,
            rule.AvatarTargetName,
            string.Equals(rule.AvatarChangeTargetId?.Trim() ?? string.Empty, currentAvatarId, StringComparison.Ordinal));
        AddKnownSelectableVrChatAvatar(
            avatarsById,
            rule.AvatarChangeResetId,
            rule.ResetAvatarName,
            string.Equals(rule.AvatarChangeResetId?.Trim() ?? string.Empty, currentAvatarId, StringComparison.Ordinal));
    }

    private static void AddKnownSelectableVrChatAvatar(
        IDictionary<string, VrChatAvatarSummary> avatarsById,
        string? avatarId,
        string? avatarName,
        bool isCurrentAvatar)
    {
        var normalizedId = avatarId?.Trim() ?? string.Empty;
        if (string.IsNullOrWhiteSpace(normalizedId))
        {
            return;
        }

        var normalizedName = avatarName?.Trim() ?? string.Empty;
        var displayName = string.IsNullOrWhiteSpace(normalizedName)
            || string.Equals(normalizedName, normalizedId, StringComparison.Ordinal)
            ? GetAvatarDuplicateHint(normalizedId)
            : normalizedName;

        if (avatarsById.TryGetValue(normalizedId, out var existingAvatar))
        {
            var existingName = existingAvatar.Name?.Trim() ?? string.Empty;
            var shouldKeepExistingName = !string.IsNullOrWhiteSpace(existingName)
                && !string.Equals(existingName, normalizedId, StringComparison.Ordinal)
                && !string.Equals(existingName, GetAvatarDuplicateHint(normalizedId), StringComparison.Ordinal);
            avatarsById[normalizedId] = existingAvatar with
            {
                Name = shouldKeepExistingName ? existingName : displayName,
                IsCurrentAvatar = existingAvatar.IsCurrentAvatar || isCurrentAvatar
            };
            return;
        }

        avatarsById[normalizedId] = new VrChatAvatarSummary(
            normalizedId,
            displayName,
            AuthorName: string.Empty,
            ThumbnailUrl: null,
            isCurrentAvatar,
            IsUploaded: false,
            IsFavorited: false,
            IsLicensed: false,
            Platform: string.Empty,
            StyleTags: Array.Empty<string>(),
            ContentTags: Array.Empty<string>(),
            FavoriteGroupName: null);
    }

    private static void ReplaceCollectionIfChanged<T>(
        ObservableCollection<T> destination,
        IReadOnlyList<T> source)
    {
        if (destination.Count == source.Count)
        {
            var isSame = true;
            for (var index = 0; index < source.Count; index++)
            {
                if (!EqualityComparer<T>.Default.Equals(destination[index], source[index]))
                {
                    isSame = false;
                    break;
                }
            }

            if (isSame)
            {
                return;
            }
        }

        destination.Clear();
        foreach (var item in source)
        {
            destination.Add(item);
        }
    }

    private void RefreshAvatarRuleProfilesList()
    {
        ReplaceCollectionIfChanged(
            AvatarRuleProfiles,
            Settings.AvatarProfiles
                .Where(profile => !profile.IsMasterProfile)
                .ToList());

        RefreshSupporterAvatarScopeOptions();
    }

    private void RefreshSupporterAvatarScopeOptions()
    {
        var avatarOptions = AvatarRuleProfiles
            .Select(profile => new AvatarProfileScopeOption(profile.Id, FormatSupporterAvatarScopeLabel(profile)))
            .ToArray();

        ReplaceCollectionIfChanged(SupporterAvatarScopeOptions, avatarOptions);

        var ruleOptions = new[]
            {
                new AvatarProfileScopeOption(Guid.Empty, T("Global / any avatar"))
            }
            .Concat(avatarOptions)
            .ToArray();
        ReplaceCollectionIfChanged(SupporterRuleAvatarScopeOptions, ruleOptions);
        RefreshSupporterRuleScopeLabels();

        if (SelectedSupporterAvatarProfileId == Guid.Empty
            || SupporterAvatarScopeOptions.All(option => option.Id != SelectedSupporterAvatarProfileId))
        {
            var currentAvatarId = Settings.VrChat.CurrentAvatarId?.Trim() ?? string.Empty;
            SelectedSupporterAvatarProfileId = AvatarRuleProfiles
                .FirstOrDefault(profile => !string.IsNullOrWhiteSpace(currentAvatarId)
                    && string.Equals(profile.AvatarId?.Trim() ?? string.Empty, currentAvatarId, StringComparison.Ordinal))
                ?.Id
                ?? SupporterAvatarScopeOptions.FirstOrDefault()?.Id
                ?? Guid.Empty;
        }
        else
        {
            RaiseSupporterRuleGroupProperties();
            AddAvatarSupporterTriggerCommand.NotifyCanExecuteChanged();
        }
    }

    private static string FormatSupporterAvatarScopeLabel(AvatarTriggerProfile profile)
    {
        var profileName = string.IsNullOrWhiteSpace(profile.DisplayTitle)
            ? "Avatar Set"
            : profile.DisplayTitle.Trim();
        var avatarName = string.IsNullOrWhiteSpace(profile.AvatarDisplayName)
            ? string.Empty
            : profile.AvatarDisplayName.Trim();

        return string.IsNullOrWhiteSpace(avatarName)
            || string.Equals(profileName, avatarName, StringComparison.OrdinalIgnoreCase)
            ? profileName
            : $"{profileName} - {avatarName}";
    }

    private void RefreshSupporterRuleScopeLabels()
    {
        foreach (var rule in Settings.GlobalOverrideRules)
        {
            if (IsSupporterAvatarChangeOverride(rule))
            {
                rule.SupporterAvatarScopeLabel = string.Empty;
                continue;
            }

            rule.SupporterAvatarScopeLabel = FormatSupporterAvatarScopeLabel(rule.SupporterAvatarId, rule.SupporterAvatarName);
        }
    }

    private string FormatSupporterAvatarScopeLabel(string avatarId, string avatarName)
    {
        var normalizedAvatarId = avatarId?.Trim() ?? string.Empty;
        if (string.IsNullOrWhiteSpace(normalizedAvatarId))
        {
            return T("Pick VRChat avatar");
        }

        var resolvedName = string.IsNullOrWhiteSpace(avatarName)
            ? ResolveVrChatAvatarName(normalizedAvatarId)
            : avatarName.Trim();
        return string.IsNullOrWhiteSpace(resolvedName)
            || string.Equals(resolvedName, normalizedAvatarId, StringComparison.Ordinal)
            ? GetSafeVrChatAvatarDisplayName(resolvedName, GetAvatarDuplicateHint(normalizedAvatarId))
            : resolvedName;
    }

    private static VrChatAvatarOption CreateVrChatAvatarOption(
        VrChatAvatarSummary avatar,
        ISet<string> duplicateNameKeys)
    {
        var normalizedAvatarId = avatar.Id?.Trim() ?? string.Empty;
        var cleanName = GetSafeVrChatAvatarDisplayName(
            string.Equals(avatar.Name?.Trim() ?? string.Empty, normalizedAvatarId, StringComparison.Ordinal)
                ? string.Empty
                : avatar.Name);
        var showDuplicateHint = duplicateNameKeys.Contains(cleanName);

        return new VrChatAvatarOption(
            normalizedAvatarId,
            cleanName,
            cleanName,
            cleanName,
            showDuplicateHint ? GetAvatarDuplicateHint(normalizedAvatarId) : string.Empty,
            false);
    }

    private static void EnsureCustomAvatarOption(
        ICollection<VrChatAvatarOption> options,
        ISet<string> existingIds,
        string? avatarId,
        string? avatarName,
        string label)
    {
        var normalizedId = avatarId?.Trim() ?? string.Empty;
        if (string.IsNullOrWhiteSpace(normalizedId) || existingIds.Contains(normalizedId))
        {
            return;
        }

        var normalizedName = avatarName?.Trim() ?? string.Empty;
        var displayName = GetSafeVrChatAvatarDisplayName(
            string.Equals(normalizedName, normalizedId, StringComparison.Ordinal) ? string.Empty : normalizedName,
            label);

        options.Add(new VrChatAvatarOption(normalizedId, displayName, displayName, displayName, string.Empty, true));
        existingIds.Add(normalizedId);
    }

    private async Task SafeVrChatLogoutAsync(string authCookie)
    {
        try
        {
            await vrChatApiClient.LogoutAsync(authCookie, CancellationToken.None);
        }
        catch
        {
        }
    }

    private void ShowAvatarTriggerRules()
    {
        var profile = GetRememberedAvatarRuleProfile();
        ApplyAvatarProfileDefaults(profile);
        var rule = profile is null ? null : GetRememberedRuleForProfile(profile);
        SwitchRuleView(RuleListView.AvatarTriggers, profile, rule);
    }

    private void ShowPowerUps()
    {
        SwitchRuleView(RuleListView.PowerUps, profile: null, rule: null);
        SelectedPowerUpRule = GetRememberedPowerUpRule();
        _ = QueuePowerUpRefreshAsync();
    }

    private UniversalTriggersManagerWindow? _universalTriggersManagerWindow;

    private AvatarScalingManagerWindow? _avatarScalingManagerWindow;

    private AvatarSetsManagerWindow? _avatarSetsManagerWindow;

    private AvatarSwapManagerWindow? _avatarSwapManagerWindow;

    private MovementRedeemsManagerWindow? _movementRedeemsManagerWindow;
    private CashPaymentManagerWindow? _cashPaymentManagerWindow;
    private RewardFireSaleManagerWindow? _rewardFireSaleManagerWindow;

    private readonly AvatarImageService _masterAvatarReturnImageService = new();
    private System.Windows.Media.ImageSource? _masterAvatarReturnImage;
    private System.Threading.CancellationTokenSource? _masterAvatarReturnImageCts;

    private void OpenUniversalTriggersManager()
    {
        if (_universalTriggersManagerWindow is { IsVisible: true })
        {
            _universalTriggersManagerWindow.Activate();
            return;
        }

        var managerVm = new UniversalTriggersManagerViewModel(Settings, this);
        _universalTriggersManagerWindow = new UniversalTriggersManagerWindow(managerVm)
        {
            Owner = System.Windows.Application.Current?.MainWindow,
        };
        _universalTriggersManagerWindow.Closed += (_, _) => _universalTriggersManagerWindow = null;
        _universalTriggersManagerWindow.Show();
    }

    private void OpenAvatarScalingManager()
    {
        if (_avatarScalingManagerWindow is { IsVisible: true })
        {
            _avatarScalingManagerWindow.Activate();
            return;
        }

        var managerVm = new AvatarScalingManagerViewModel(Settings, this);
        _avatarScalingManagerWindow = new AvatarScalingManagerWindow(managerVm)
        {
            Owner = Application.Current?.MainWindow,
        };
        _avatarScalingManagerWindow.Closed += (_, _) => _avatarScalingManagerWindow = null;
        _avatarScalingManagerWindow.Show();
    }

    private void OpenAvatarSetsManager()
    {
        if (_avatarSetsManagerWindow is { IsVisible: true })
        {
            _avatarSetsManagerWindow.Activate();
            return;
        }

        var managerVm = new AvatarSetsManagerViewModel(this);
        managerVm.SubscribeAllRulesAndOutfits();
        _avatarSetsManagerWindow = new AvatarSetsManagerWindow
        {
            Owner = System.Windows.Application.Current?.MainWindow,
            DataContext = managerVm
        };
        _avatarSetsManagerWindow.Closed += (_, _) =>
        {
            _avatarSetsManagerWindow = null;
        };
        _avatarSetsManagerWindow.Show();
    }

    public RelayCommand OpenAvatarSwapManagerCommand { get; }

    public void OpenAvatarSwapManager()
    {
        if (_avatarSwapManagerWindow is { IsVisible: true })
        {
            _avatarSwapManagerWindow.Activate();
            return;
        }

        var managerVm = new AvatarSwapManagerViewModel(
            Settings,
            this,
            TryGetVrChatAvatarThumbnailUrl,
            () =>
            {
                Coordinator?.ClearAllPermanentChangeCompleted();
                QueueSave(0);
                QueueBridgeRefresh();
                QueueManagedRewardSync(0, ManagedRewardSyncReason.SettingsEdit);
            });
        _ = QueuePowerUpRefreshAsync();
        _avatarSwapManagerWindow = new AvatarSwapManagerWindow(managerVm)
        {
            Owner = System.Windows.Application.Current?.MainWindow,
        };
        _avatarSwapManagerWindow.Closed += (_, _) => _avatarSwapManagerWindow = null;
        _avatarSwapManagerWindow.Show();
    }

    private void OpenMovementRedeemsManager()
    {
        if (_movementRedeemsManagerWindow is { IsVisible: true })
        {
            _movementRedeemsManagerWindow.Activate();
            return;
        }

        var managerVm = new MovementRedeemsManagerViewModel(Settings, this);
        _movementRedeemsManagerWindow = new MovementRedeemsManagerWindow(managerVm)
        {
            Owner = System.Windows.Application.Current?.MainWindow,
        };
        _movementRedeemsManagerWindow.Closed += (_, _) => _movementRedeemsManagerWindow = null;
        _movementRedeemsManagerWindow.Show();
    }

    private void OpenCashPaymentManager()
    {
        if (_cashPaymentManagerWindow is { IsVisible: true })
        {
            _cashPaymentManagerWindow.Activate();
            return;
        }

        var managerVm = new CashPaymentManagerViewModel(Settings, this);
        _cashPaymentManagerWindow = new CashPaymentManagerWindow(managerVm)
        {
            Owner = System.Windows.Application.Current?.MainWindow,
        };
        _cashPaymentManagerWindow.Closed += (_, _) => _cashPaymentManagerWindow = null;
        _cashPaymentManagerWindow.Show();
    }

    private void OpenRewardFireSaleManager()
    {
        if (_rewardFireSaleManagerWindow is { IsVisible: true })
        {
            _rewardFireSaleManagerWindow.Activate();
            return;
        }

        var managerVm = new RewardFireSaleManagerViewModel(Settings, this);
        _rewardFireSaleManagerWindow = new RewardFireSaleManagerWindow(managerVm)
        {
            Owner = System.Windows.Application.Current?.MainWindow,
        };
        _rewardFireSaleManagerWindow.Closed += (_, _) => _rewardFireSaleManagerWindow = null;
        _rewardFireSaleManagerWindow.Show();
    }

    public System.Windows.Media.ImageSource? MasterAvatarReturnImage
    {
        get => _masterAvatarReturnImage;
        private set
        {
            if (SetProperty(ref _masterAvatarReturnImage, value))
            {
                RaisePropertyChanged(nameof(HasMasterAvatarReturnImage));
            }
        }
    }

    public bool HasMasterAvatarReturnImage => _masterAvatarReturnImage is not null;

    private void PickReturnAvatar()
    {
        var avatars = GetAllVrChatAvatars();
        var result = AvatarPickerService.OpenSingle(
            ThemeManager.CurrentTheme,
            avatars,
            Settings.AvatarLibrary,
            Settings.MasterAvatarSwapReturnId,
            owner: Application.Current?.MainWindow);
        if (result is null) return;
        ApplySharedReturnAvatarSelection(result.AvatarId, result.AvatarName, saveImmediately: true);
        AppendLog($"Picked return avatar '{result.AvatarName}'.");
    }

    private void UseCurrentAvatarForReturn()
    {
        var currentId = Settings.VrChat.CurrentAvatarId;
        if (string.IsNullOrWhiteSpace(currentId)) return;
        var resolvedName = ResolveVrChatAvatarName(currentId);
        ApplySharedReturnAvatarSelection(currentId, resolvedName, saveImmediately: true);
        AppendLog($"Set return avatar to current avatar '{resolvedName}'.");
    }

    private void ClearReturnAvatar()
    {
        Settings.MasterAvatarSwapReturnId = null;
        Settings.MasterAvatarSwapReturnName = null;
        QueueSave();
        QueueBridgeRefresh();
        AppendLog("Cleared the return avatar.");
    }

    private void LoadMasterAvatarReturnImage()
    {
        _masterAvatarReturnImageCts?.Cancel();
        _masterAvatarReturnImageCts?.Dispose();
        _masterAvatarReturnImageCts = new System.Threading.CancellationTokenSource();
        var avatarId = Settings.MasterAvatarSwapReturnId;
        var thumbnailUrl = TryGetVrChatAvatarThumbnailUrl(avatarId);
        var ct = _masterAvatarReturnImageCts.Token;

        if (string.IsNullOrWhiteSpace(avatarId))
        {
            MasterAvatarReturnImage = null;
            return;
        }

        var syncImage = _masterAvatarReturnImageService.GetAvatarImage(avatarId, null, thumbnailUrl);
        if (syncImage is not null && !ct.IsCancellationRequested)
        {
            System.Windows.Application.Current?.Dispatcher.InvokeAsync(() => MasterAvatarReturnImage = syncImage);
            return;
        }

        _ = System.Threading.Tasks.Task.Run(async () =>
        {
            try
            {
                var asyncImage = await _masterAvatarReturnImageService.GetAvatarImageAsync(avatarId, null, thumbnailUrl, ct);
                if (asyncImage is not null && !ct.IsCancellationRequested)
                {
                    System.Windows.Application.Current?.Dispatcher.InvokeAsync(() => MasterAvatarReturnImage = asyncImage);
                }
            }
            catch (OperationCanceledException)
            {
            }
            catch
            {
                if (!ct.IsCancellationRequested)
                {
                    var placeholder = _masterAvatarReturnImageService.GetPlaceholderImage();
                    System.Windows.Application.Current?.Dispatcher.InvokeAsync(() => MasterAvatarReturnImage = placeholder);
                }
            }
        }, ct);
    }

    public void TestAvatarSet(AvatarTriggerProfile profile)
    {
        if (profile == null) return;
        if (!profile.UseWardrobeMode) return;
        var firstOutfit = profile.WardrobeOutfits?.FirstOrDefault(o => o.IsEnabled);
        if (firstOutfit == null) return;
        _ = TestWardrobeOutfitPublicAsync(firstOutfit, profile, CancellationToken.None);
    }

    private void ShowAvatarScaling()
    {
        SwitchRuleView(RuleListView.AvatarScaling, profile: null, rule: null);
        SelectedAvatarScaleSet = GetRememberedAvatarScaleSet();
        SelectedAvatarScaleRule = GetRememberedAvatarScaleRule();
        QueueManagedRewardSync(0);
    }

    private void EnsureRewardFireSaleTierExists()
    {
        if (Settings.RewardFireSale.Tiers.Count > 0)
        {
            return;
        }

        Settings.RewardFireSale.Tiers.Add(new RewardFireSaleTier());
    }

    private void StopRewardFireSale(bool expired)
    {
        var fireSale = Settings.RewardFireSale;
        if (!fireSale.IsSaleActive)
        {
            return;
        }

        CancelAndDisposeQueuedCancellationSource(ref rewardFireSaleExpirationCancellation);
        fireSale.IsSaleActive = false;
        fireSale.ActiveDiscountPercent = 0;
        fireSale.ActiveTierGoalAmount = 0;
        fireSale.ActiveUntilUtc = null;
        QueueSave(0);
        QueueManagedRewardSync(0, ManagedRewardSyncReason.FireSaleChanged);
        AppendLog(expired
            ? "Reward Fire Sale ended and Crystal Relay queued normal reward prices to restore."
            : "Reward Fire Sale stopped and Crystal Relay queued normal reward prices to restore.");
    }

    private bool ResetRewardFireSaleForStreamEnd()
    {
        var fireSale = Settings.RewardFireSale;
        if (!fireSale.IsSaleActive && fireSale.CurrentProgress <= 0)
        {
            return false;
        }

        CancelAndDisposeQueuedCancellationSource(ref rewardFireSaleExpirationCancellation);
        fireSale.IsSaleActive = false;
        fireSale.ActiveDiscountPercent = 0;
        fireSale.ActiveTierGoalAmount = 0;
        fireSale.ActiveUntilUtc = null;
        fireSale.CurrentProgress = 0;

        QueueSave(0);
        QueueManagedRewardSync(0, ManagedRewardSyncReason.FireSaleChanged);
        AppendLog("Stream ended, so Reward Fire Sale was reset and normal reward prices were queued to restore.");
        return true;
    }

    private bool HandleRewardFireSaleContribution(RewardFireSaleContribution contribution)
    {
        if (!isInitialized || isShuttingDown)
        {
            return false;
        }

        ExpireRewardFireSaleIfNeeded();
        var fireSale = Settings.RewardFireSale;
        var isFundingReward = contribution.Type == RewardFireSaleContributionType.ManagedReward
            && IsRewardFireSaleFundingReward(contribution.RewardId, contribution.RewardTitle);
        if (!fireSale.IsEnabled)
        {
            return isFundingReward;
        }

        if (IsRewardFireSaleActiveNow() && !CanRewardFireSaleAdvanceToLaterTier())
        {
        AppendThrottledLog("reward-fire-sale-active-progress-paused",
            "CR-DIAG: Reward Fire Sale is already active at its final available tier, so new contributions are not adding progress right now.",
            ThrottledRewardSyncLogWindow);
            return isFundingReward;
        }

        var contributionAmount = ResolveRewardFireSaleContributionAmount(contribution);
        if (contributionAmount <= 0)
        {
            return isFundingReward;
        }

        fireSale.CurrentProgress += contributionAmount;
        if (isFundingReward)
        {
            StartRewardFireSaleFundingRewardCooldown();
        }

        AppendLog($"Reward Fire Sale added {contributionAmount:N0} progress from {contribution.UserDisplayName}. Total: {fireSale.CurrentProgress:N0}.");
        ActivateRewardFireSaleIfGoalReached();
        QueueSave();
        return isFundingReward;
    }

    private bool HandleDevFireSaleRequest(DevFireSaleRequest request)
    {
        if (!isInitialized || isShuttingDown)
        {
            return false;
        }

        var discountPercent = Math.Clamp(request.DiscountPercent, 1, 100);
        var durationSeconds = Math.Max(1, request.DurationSeconds);
        var fireSale = Settings.RewardFireSale;

        CancelAndDisposeQueuedCancellationSource(ref rewardFireSaleExpirationCancellation);
        fireSale.IsEnabled = true;
        fireSale.SaleMode = RewardFireSaleMode.Temporary;
        fireSale.IsSaleActive = true;
        fireSale.ActiveDiscountPercent = discountPercent;
        fireSale.ActiveTierGoalAmount = 0;
        fireSale.ActiveUntilUtc = DateTimeOffset.UtcNow.AddSeconds(durationSeconds);

        ScheduleRewardFireSaleExpirationIfNeeded();
        QueueSave(0);
        QueueManagedRewardSync(0, ManagedRewardSyncReason.FireSaleChanged);
        AppendLog(
            $"Dev Fire Sale started by {request.UserDisplayName}: {discountPercent}% off for {durationSeconds:N0}s. Crystal Relay-owned reward prices will restore when it ends.");
        return true;
    }

    private int ResolveRewardFireSaleContributionAmount(RewardFireSaleContribution contribution)
    {
        var fireSale = Settings.RewardFireSale;
        if (contribution.Type == RewardFireSaleContributionType.Bits)
        {
            return fireSale.CountBits ? Math.Max(0, contribution.Amount) : 0;
        }

        if (contribution.Type == RewardFireSaleContributionType.CashPayment)
        {
            return fireSale.CountCashPayments ? Math.Max(0, contribution.Amount) * fireSale.CashPaymentProgressRatio : 0;
        }

        if (!fireSale.FundingRewardEnabled || !IsRewardFireSaleFundingReward(contribution.RewardId, contribution.RewardTitle))
        {
            return 0;
        }

        return GetRewardFireSaleFundingProgressPerRedeem();
    }

    private bool IsRewardFireSaleFundingReward(string? rewardId, string? rewardTitle)
    {
        var fireSale = Settings.RewardFireSale;
        var savedRewardId = fireSale.FundingRewardId?.Trim() ?? string.Empty;
        var incomingRewardId = rewardId?.Trim() ?? string.Empty;
        if (!string.IsNullOrWhiteSpace(savedRewardId)
            && string.Equals(savedRewardId, incomingRewardId, StringComparison.Ordinal))
        {
            return true;
        }

        return ManagedRewardPresentation.HasSameTitleIdentity(rewardTitle, fireSale.FundingRewardTitle);
    }

    private int GetRewardFireSaleFundingProgressPerRedeem()
    {
        var fireSale = Settings.RewardFireSale;
        return Math.Max(1, (int)Math.Floor(Math.Max(1, fireSale.FundingRewardCost) / (double)Math.Max(1, fireSale.RewardPointsPerProgressUnit)));
    }

    private void StartRewardFireSaleFundingRewardCooldown()
    {
        var cooldownSeconds = Math.Max(0, Settings.RewardFireSale.FundingRewardCooldownSeconds);
        if (cooldownSeconds <= 0)
        {
            ClearRewardFireSaleFundingRewardCooldown(queueSync: false);
            return;
        }

        rewardFireSaleFundingRewardCooldownUntil = DateTimeOffset.UtcNow.AddSeconds(cooldownSeconds);
        ScheduleRewardFireSaleFundingRewardCooldownEnd(cooldownSeconds);
        QueueManagedRewardSync(0, ManagedRewardSyncReason.FireSaleChanged);
    }

    private bool IsRewardFireSaleFundingRewardOnCooldown()
    {
        if (rewardFireSaleFundingRewardCooldownUntil is not { } cooldownUntil)
        {
            return false;
        }

        if (cooldownUntil > DateTimeOffset.UtcNow
            && Settings.RewardFireSale.FundingRewardCooldownSeconds > 0)
        {
            return true;
        }

        ClearRewardFireSaleFundingRewardCooldown(queueSync: false);
        return false;
    }

    private void ClearRewardFireSaleFundingRewardCooldown(bool queueSync)
    {
        rewardFireSaleFundingRewardCooldownUntil = null;
        CancelAndDisposeQueuedCancellationSource(ref rewardFireSaleFundingCooldownCancellation);
        if (queueSync)
        {
            QueueManagedRewardSync(0, ManagedRewardSyncReason.FireSaleChanged);
        }
    }

    private void ScheduleRewardFireSaleFundingRewardCooldownEnd(int cooldownSeconds)
    {
        var cooldownCancellation = ReplaceQueuedCancellationSource(ref rewardFireSaleFundingCooldownCancellation);
        _ = Task.Run(async () =>
        {
            try
            {
                await Task.Delay(TimeSpan.FromSeconds(Math.Max(1, cooldownSeconds)), cooldownCancellation.Token);
                RunOnUi(() =>
                {
                    rewardFireSaleFundingRewardCooldownUntil = null;
                    QueueManagedRewardSync(0, ManagedRewardSyncReason.FireSaleChanged);
                });
            }
            catch (OperationCanceledException)
            {
            }
            finally
            {
                DisposeCompletedQueuedCancellationSource(ref rewardFireSaleFundingCooldownCancellation, cooldownCancellation);
            }
        }, CancellationToken.None);
    }

    private void ActivateRewardFireSaleIfGoalReached()
    {
        var reachedTier = GetReachedRewardFireSaleTier();
        if (reachedTier is null)
        {
            return;
        }

        var fireSale = Settings.RewardFireSale;
        var saleWasActive = IsRewardFireSaleActiveNow();
        if (saleWasActive
            && reachedTier.GoalAmount <= fireSale.ActiveTierGoalAmount
            && reachedTier.DiscountPercent <= fireSale.ActiveDiscountPercent)
        {
            return;
        }

        fireSale.IsSaleActive = true;
        fireSale.ActiveDiscountPercent = reachedTier.DiscountPercent;
        fireSale.ActiveTierGoalAmount = reachedTier.GoalAmount;
        if (!saleWasActive)
        {
            fireSale.ActiveUntilUtc = fireSale.SaleMode == RewardFireSaleMode.Temporary
                ? DateTimeOffset.UtcNow.AddSeconds(Math.Max(1, fireSale.TemporaryDurationSeconds))
                : null;
        }

        if (!fireSale.MultiTierEnabled || IsFinalRewardFireSaleTier(reachedTier))
        {
            fireSale.CurrentProgress = 0;
        }

        if (!saleWasActive)
        {
            ScheduleRewardFireSaleExpirationIfNeeded();
        }

        QueueSave(0);
        QueueManagedRewardSync(0, ManagedRewardSyncReason.FireSaleChanged);
        AppendLog(saleWasActive
            ? $"Reward Fire Sale upgraded: {reachedTier.DiscountPercent}% off from the {reachedTier.GoalAmount:N0} goal tier."
            : $"Reward Fire Sale started: {reachedTier.DiscountPercent}% off Crystal Relay-owned VRC rewards.");
    }

    private RewardFireSaleTier? GetReachedRewardFireSaleTier()
    {
        var fireSale = Settings.RewardFireSale;
        var eligibleTiers = GetRewardFireSaleTiers()
            .Where(tier => fireSale.CurrentProgress >= tier.GoalAmount)
            .ToArray();
        if (eligibleTiers.Length == 0)
        {
            return null;
        }

        return fireSale.MultiTierEnabled
            ? eligibleTiers.OrderByDescending(tier => tier.GoalAmount).First()
            : eligibleTiers.OrderBy(tier => tier.GoalAmount).First();
    }

    private RewardFireSaleTier? GetNextRewardFireSaleTier()
    {
        return GetRewardFireSaleTiers()
            .Where(tier => Settings.RewardFireSale.CurrentProgress < tier.GoalAmount)
            .OrderBy(tier => tier.GoalAmount)
            .FirstOrDefault()
            ?? GetRewardFireSaleTiers().OrderByDescending(tier => tier.GoalAmount).FirstOrDefault();
    }

    private IReadOnlyList<RewardFireSaleTier> GetRewardFireSaleTiers()
    {
        EnsureRewardFireSaleTierExists();
        return Settings.RewardFireSale.Tiers
            .Where(tier => tier.GoalAmount > 0 && tier.DiscountPercent > 0)
            .OrderBy(tier => tier.GoalAmount)
            .ToArray();
    }

    private RewardFireSaleTier? GetFinalRewardFireSaleTier() =>
        GetRewardFireSaleTiers().OrderByDescending(tier => tier.GoalAmount).FirstOrDefault();

    private bool IsFinalRewardFireSaleTier(RewardFireSaleTier tier)
    {
        var finalTier = GetFinalRewardFireSaleTier();
        return finalTier is not null && finalTier.GoalAmount == tier.GoalAmount;
    }

    private bool CanRewardFireSaleAdvanceToLaterTier()
    {
        var fireSale = Settings.RewardFireSale;
        if (!fireSale.MultiTierEnabled || !IsRewardFireSaleActiveNow())
        {
            return false;
        }

        var finalTier = GetFinalRewardFireSaleTier();
        return finalTier is not null
            && fireSale.ActiveTierGoalAmount < finalTier.GoalAmount
            && fireSale.CurrentProgress < finalTier.GoalAmount;
    }

    private bool IsRewardFireSaleActiveNow()
    {
        var fireSale = Settings.RewardFireSale;
        if (!fireSale.IsEnabled || !fireSale.IsSaleActive || fireSale.ActiveDiscountPercent <= 0)
        {
            return false;
        }

        return fireSale.SaleMode != RewardFireSaleMode.Temporary
            || fireSale.ActiveUntilUtc is null
            || fireSale.ActiveUntilUtc > DateTimeOffset.UtcNow;
    }

    private void ExpireRewardFireSaleIfNeeded()
    {
        var fireSale = Settings.RewardFireSale;
        if (!fireSale.IsSaleActive
            || fireSale.SaleMode != RewardFireSaleMode.Temporary
            || fireSale.ActiveUntilUtc is not { } activeUntil
            || activeUntil > DateTimeOffset.UtcNow)
        {
            return;
        }

        StopRewardFireSale(expired: true);
    }

    private void ScheduleRewardFireSaleExpirationIfNeeded()
    {
        CancelAndDisposeQueuedCancellationSource(ref rewardFireSaleExpirationCancellation);
        var fireSale = Settings.RewardFireSale;
        if (!fireSale.IsSaleActive
            || fireSale.SaleMode != RewardFireSaleMode.Temporary
            || fireSale.ActiveUntilUtc is not { } activeUntil)
        {
            return;
        }

        var delay = activeUntil - DateTimeOffset.UtcNow;
        if (delay <= TimeSpan.Zero)
        {
            StopRewardFireSale(expired: true);
            return;
        }

        rewardFireSaleExpirationCancellation = new CancellationTokenSource();
        var cancellationToken = rewardFireSaleExpirationCancellation.Token;
        _ = Task.Run(async () =>
        {
            try
            {
                await Task.Delay(delay, cancellationToken);
                RunOnUi(() => StopRewardFireSale(expired: true));
            }
            catch (OperationCanceledException)
            {
            }
        }, CancellationToken.None);
    }

    private int ApplyRewardFireSaleDiscount(int normalCost, TwitchRewardSyncMode rewardSyncMode)
    {
        if (rewardSyncMode != TwitchRewardSyncMode.CreateOrManage || !IsRewardFireSaleActiveNow())
        {
            return Math.Max(1, normalCost);
        }

        var discountPercent = Math.Clamp(Settings.RewardFireSale.ActiveDiscountPercent, 0, 100);
        if (discountPercent <= 0)
        {
            return Math.Max(1, normalCost);
        }

        var discountedCost = (int)Math.Floor(Math.Max(1, normalCost) * (100 - discountPercent) / 100d);
        return Math.Max(1, discountedCost);
    }

    private void AddAvatarProfile()
    {
        var profile = CreateDefaultAvatarProfile();
        Settings.AvatarProfiles.Add(profile);
        SwitchRuleView(
            RuleListView.AvatarTriggers,
            profile,
            GetRememberedRuleForProfile(profile));
        AppendLog($"Added avatar set '{profile.DisplayTitle}'.");
    }

    private void DeleteSelectedAvatarProfile()
    {
        if (SelectedAvatarProfile is null)
        {
            return;
        }

        var removedName = SelectedAvatarProfile.DisplayTitle;
        var removedProfile = SelectedAvatarProfile;
        foreach (var removedRule in removedProfile.ChannelPointRules.ToArray())
        {
            RemoveSpecialRuleLockoutReferencesToRule(removedRule.Id);
        }
        RetireManagedRewards(removedProfile.ChannelPointRules);
        ForgetRememberedRules(removedProfile.ChannelPointRules);
        Settings.AvatarProfiles.Remove(removedProfile);

        if (Settings.AvatarProfiles.Count == 0)
        {
            SwitchRuleView(RuleListView.AvatarTriggers, profile: null, rule: null);
        }
        else
        {
            var nextProfile = AvatarRuleProfiles.FirstOrDefault();
            SwitchRuleView(
                RuleListView.AvatarTriggers,
                nextProfile,
                nextProfile is null ? null : GetRememberedRuleForProfile(nextProfile));
        }

        lastSelectedRuleIdsByAvatarProfileId.Remove(removedProfile.Id);
        if (lastSelectedAvatarProfileId == removedProfile.Id)
        {
            lastSelectedAvatarProfileId = Guid.Empty;
        }

        if (removedProfile.IsMasterProfile)
        {
            lastSelectedMasterRuleId = Guid.Empty;
        }

        AppendLog($"Removed avatar set '{removedName}'.");
    }

    private void DeleteAllAvatarProfiles()
    {
        var profilesToRemove = AvatarRuleProfiles.ToArray();
        if (profilesToRemove.Length == 0)
        {
            return;
        }

        if (!ConfirmDeleteAll(
            "Delete All Avatar Sets",
            "Are you sure you want to delete every avatar set and the redeems inside them? This cannot be undone."))
        {
            return;
        }

        foreach (var profile in profilesToRemove)
        {
            foreach (var removedRule in profile.ChannelPointRules.ToArray())
            {
                RemoveSpecialRuleLockoutReferencesToRule(removedRule.Id);
            }

            RetireManagedRewards(profile.ChannelPointRules);
            ForgetRememberedRules(profile.ChannelPointRules);
        }

        foreach (var profile in profilesToRemove)
        {
            Settings.AvatarProfiles.Remove(profile);
        }

        lastSelectedAvatarProfileId = Guid.Empty;
        SwitchRuleView(RuleListView.AvatarTriggers, profile: null, rule: null);
        QueueSave();
        QueueBridgeRefresh();
        AppendLog($"Deleted {profilesToRemove.Length} avatar set{(profilesToRemove.Length == 1 ? string.Empty : "s")}.");
    }

    private void SetSelectedAvatarProfileAsMaster()
    {
        if (SelectedAvatarProfile is null)
        {
            return;
        }

        foreach (var profile in Settings.AvatarProfiles)
        {
            profile.IsMasterProfile = ReferenceEquals(profile, SelectedAvatarProfile);
        }

        RefreshAvailableActionTypes();
        QueueSave();
        QueueBridgeRefresh();
        AppendLog($"Set '{SelectedAvatarProfile.DisplayTitle}' as the return avatar.");
    }

    private void ToggleSelectedAvatarRewardTestOverride()
    {
        Settings.ChannelPointRewardTestModeEnabled = !Settings.ChannelPointRewardTestModeEnabled;
        QueueSave();
        QueueManagedRewardSync(0, ManagedRewardSyncReason.TestMode);
        RaiseRuleSelectionStateProperties();
        RefreshRuleCommandStates();
        AppendLog(Settings.ChannelPointRewardTestModeEnabled
            ? "Universal reward test mode is on."
            : "Universal reward test mode is off.");
    }

    private void ToggleEmergencyRedeemStop()
    {
        Settings.EmergencyRedeemStopEnabled = !Settings.EmergencyRedeemStopEnabled;
        QueueSave(0);
        QueueManagedRewardSync(0, ManagedRewardSyncReason.EmergencyStop);
        QueueBridgeRefresh();
        RaisePropertyChanged(nameof(EmergencyRedeemStopButtonText));
        RaisePropertyChanged(nameof(EmergencyRedeemStopHelpText));
        RaisePropertyChanged(nameof(IsEmergencyRedeemStopEnabled));
        RaiseRuleSelectionStateProperties();
        AppendLog(Settings.EmergencyRedeemStopEnabled
            ? "Emergency redeem pause is on. Crystal Relay is turning Twitch redeems off until you resume them."
            : "Emergency redeem pause is off. Crystal Relay will bring Twitch redeems back when they are allowed again.");
    }

    private void ToggleRedeemGroupByName(string groupName)
    {
        var group = Settings.RedeemGroups.FirstOrDefault(g =>
            string.Equals(g.Name, groupName, StringComparison.OrdinalIgnoreCase));

        if (group is null)
        {
            AppendLog($"Redeem group '{groupName}' was not found.");
            return;
        }

        var rules = GetAllManagedRules().ToList();
        var assignedRules = rules.Where(r => group.AssignedRuleIds.Contains(r.Id)).ToList();

        if (assignedRules.Count == 0)
        {
            AppendLog($"Redeem group '{groupName}' has no assigned rules.");
            return;
        }

        var anyEnabled = assignedRules.Any(r => r.IsEnabled);
        foreach (var rule in assignedRules)
        {
            rule.IsEnabled = !anyEnabled;
        }

        QueueSave(0);
        QueueManagedRewardSync(0, ManagedRewardSyncReason.SettingsEdit);
        QueueBridgeRefresh();
        AppendLog(anyEnabled
            ? $"Disabled redeem group '{groupName}' ({assignedRules.Count} rule{(assignedRules.Count == 1 ? string.Empty : "s")})."
            : $"Enabled redeem group '{groupName}' ({assignedRules.Count} rule{(assignedRules.Count == 1 ? string.Empty : "s")}).");
    }

    private void ToggleRedeemByName(string redeemName, bool enable)
    {
        var rules = GetAllManagedRules().ToList();
        var matchedRules = rules.Where(r =>
            string.Equals(r.DisplayTitle, redeemName, StringComparison.OrdinalIgnoreCase)
            || string.Equals(r.Name, redeemName, StringComparison.OrdinalIgnoreCase)
            || string.Equals(r.ChannelPointRewardTitle, redeemName, StringComparison.OrdinalIgnoreCase)
            || string.Equals(r.RewardDisplayTitle, redeemName, StringComparison.OrdinalIgnoreCase)).ToList();

        if (matchedRules.Count == 0)
        {
            AppendLog($"No redeem matching '{redeemName}' was found.");
            return;
        }

        foreach (var rule in matchedRules)
        {
            rule.IsEnabled = enable;
        }

        QueueSave(0);
        QueueManagedRewardSync(0, ManagedRewardSyncReason.SettingsEdit);
        QueueBridgeRefresh();
        AppendLog(enable
            ? $"Enabled redeem '{redeemName}' ({matchedRules.Count} match{(matchedRules.Count == 1 ? string.Empty : "es")})."
            : $"Disabled redeem '{redeemName}' ({matchedRules.Count} match{(matchedRules.Count == 1 ? string.Empty : "es")}).");
    }

    private IEnumerable<TriggerRule> GetAllManagedRules()
    {
        foreach (var profile in Settings.AvatarProfiles)
        {
            foreach (var rule in profile.ChannelPointRules)
            {
                yield return rule;
            }
        }

        foreach (var rule in Settings.GlobalOverrideRules)
        {
            yield return rule;
        }

        foreach (var set in Settings.MovementRedeemSets)
        {
            foreach (var rule in set.MovementRules)
            {
                yield return rule;
            }
        }
    }

    public void AddRedeemGroup(RedeemGroup group)
    {
        if (string.IsNullOrWhiteSpace(group.Name) || string.IsNullOrWhiteSpace(group.CommandText))
        {
            return;
        }

        Settings.RedeemGroups.Add(group);
        QueueSave(0);
        RaisePropertyChanged(nameof(BuiltInCommandsSummaryText));
        AppendLog($"Added redeem group '{group.Name}' with command {group.CommandText}.");
    }

    public void UpdateRedeemGroup(RedeemGroup original, RedeemGroup updated)
    {
        if (original is null || updated is null)
        {
            return;
        }

        original.Name = updated.Name;
        original.CommandText = updated.CommandText;
        original.AssignedRuleIds = new ObservableCollection<Guid>(updated.AssignedRuleIds);
        QueueSave(0);
        RaisePropertyChanged(nameof(BuiltInCommandsSummaryText));
        AppendLog($"Updated redeem group '{updated.Name}'.");
    }

    public void RemoveRedeemGroup(RedeemGroup group)
    {
        if (group is null)
        {
            return;
        }

        var removed = Settings.RedeemGroups.Remove(group);
        if (removed)
        {
            QueueSave(0);
            RaisePropertyChanged(nameof(BuiltInCommandsSummaryText));
            AppendLog($"Removed redeem group '{group.Name}'.");
        }
    }

    public IReadOnlyList<TriggerRule> GetAllManagedRulesList() => GetAllManagedRules().ToList();

    private void ToggleDesktopModeInputLock()
    {
        Settings.DesktopModeInputLockEnabled = !Settings.DesktopModeInputLockEnabled;
        QueueSave(0);
        QueueBridgeRefresh();
        RaisePropertyChanged(nameof(DesktopModeInputLockButtonText));
        RaisePropertyChanged(nameof(DesktopModeInputLockHelpText));
        RaisePropertyChanged(nameof(DesktopModeInputLockStatusText));
        RaisePropertyChanged(nameof(IsDesktopModeInputLockEnabled));
        AppendLog(Settings.DesktopModeInputLockEnabled
            ? $"Desktop mode input lock is on. Stop-input redeems will use desktop hard locks, and {DesktopInputLockService.EmergencyHotkeyDisplay} will always release them."
            : "Desktop mode input lock is off. Stop-input redeems will fall back to the VRChat soft-lock path.");
    }

    private void UseCurrentVrChatAvatarForProfile()
    {
        if (SelectedAvatarProfile is null)
        {
            return;
        }

        var currentAvatarId = GetResolvedCurrentVrChatAvatarId();
        if (string.IsNullOrWhiteSpace(currentAvatarId))
        {
            VrChatAvatarStatus = T("Crystal Relay does not know the current avatar yet. Refresh avatars first.");
            return;
        }

        var resolvedName = ResolveVrChatAvatarName(currentAvatarId);
        if (SelectedAvatarProfile.IsMasterProfile)
        {
            ApplySharedReturnAvatarSelection(currentAvatarId, resolvedName, saveImmediately: true);
            AppendLog($"Set '{CurrentVrChatAvatarDisplayName}' as the return avatar.");
            return;
        }

        SelectedAvatarProfile.AvatarId = currentAvatarId;
        if (!string.IsNullOrWhiteSpace(resolvedName))
        {
            SelectedAvatarProfile.AvatarName = resolvedName;
        }

        UpdateAvatarProfileActivityStates();
        RefreshVrChatAvatarSelectionOptions();
        _ = EnsureSelectedAvatarParameterCacheLoadedAsync();
        QueueSave();
        AppendLog($"Assigned '{CurrentVrChatAvatarDisplayName}' to avatar set '{SelectedAvatarProfile.DisplayTitle}'.");
    }

    private async void OpenAvatarPicker(object? parameter)
    {
        var avatars = availableVrChatAvatars
            .Select(a => a with { })
            .ToList();

        var context = parameter as string ?? "Profile";
        var currentAvatarId = context switch
        {
            "Profile" => SelectedAvatarProfile?.AvatarId,
            "PowerUp" => SelectedPowerUpRule?.AvatarId,
            "Supporter" => SelectedRule?.SupporterAvatarId,
            _ => SelectedAvatarProfile?.AvatarId,
        };

        IReadOnlyList<VrChatFavoriteGroup>? favGroups = null;
        Dictionary<string, string>? avatarFavGroups = null;

        if (Settings.VrChat.IsConnected && !string.IsNullOrWhiteSpace(Settings.VrChat.AuthCookie))
        {
            try
            {
                var authCookie = Settings.VrChat.AuthCookie;
                var groups = await vrChatApiClient.GetFavoriteGroupsAsync(authCookie);
                var entries = await vrChatApiClient.GetFavoriteEntriesAsync(authCookie);
                favGroups = groups.Select(g => new VrChatFavoriteGroup(
                    g.Id ?? string.Empty,
                    g.DisplayName ?? g.Name ?? string.Empty,
                    g.Name ?? string.Empty,
                    entries.Count(e => e.Tags?.Contains(g.Name ?? string.Empty) == true))).ToList();

                avatarFavGroups = new Dictionary<string, string>();
                foreach (var entry in entries)
                {
                    if (entry.FavoriteId is not null && entry.Tags?.Count > 0)
                    {
                        var groupName = groups.FirstOrDefault(g => g.Name == entry.Tags[0])?.DisplayName ?? entry.Tags[0];
                        avatarFavGroups[entry.FavoriteId] = groupName;
                    }
                }
            }
            catch
            {
            }
        }

        var result = AvatarPickerService.OpenSingle(
            ThemeManager.CurrentTheme,
            avatars,
            Settings.AvatarLibrary,
            currentAvatarId,
            favGroups,
            avatarFavGroups,
            Application.Current.MainWindow);

        if (result is null)
        {
            return;
        }

        Settings.AvatarLibrary?.TrackRecentAvatar(result.AvatarId);

        switch (context)
        {
            case "Profile":
                if (SelectedAvatarProfile is not null)
                {
                    SelectedAvatarProfile.AvatarId = result.AvatarId;
                    SelectedAvatarProfile.AvatarName = result.AvatarName;
                }
                break;
            case "PowerUp":
                if (SelectedPowerUpRule is not null)
                {
                    SelectedPowerUpRule.AvatarId = result.AvatarId;
                    SelectedPowerUpRule.AvatarName = result.AvatarName;
                }
                break;
            case "Supporter":
                if (SelectedRule is not null)
                {
                    SelectedRule.SupporterAvatarId = result.AvatarId;
                    SelectedRule.SupporterAvatarName = result.AvatarName;
                }
                break;
        }

        RefreshVrChatAvatarSelectionOptions();
    }

    private void UseCurrentAvatarForSupporterRule()
    {
        if (SelectedRule is null)
        {
            return;
        }

        var currentAvatarId = Settings.VrChat.CurrentAvatarId?.Trim() ?? string.Empty;
        if (string.IsNullOrWhiteSpace(currentAvatarId))
        {
            VrChatAvatarStatus = T("Crystal Relay does not know the current avatar yet. Refresh avatars first.");
            return;
        }

        var resolvedName = ResolveVrChatAvatarName(currentAvatarId);
        SelectedRule.SupporterAvatarId = currentAvatarId;
        SelectedRule.SupporterAvatarName = resolvedName;
        SelectedRule.SupporterAvatarProfileId = Guid.Empty;
        RefreshVrChatAvatarSelectionOptions();
        RefreshAvatarParameterOptions();
        QueueSave();
        QueueBridgeRefresh();
        RaiseSupporterRuleGroupProperties();
        AppendLog($"Set supporter trigger '{SelectedRule.DisplayTitle}' to current avatar '{GetSafeVrChatAvatarDisplayName(resolvedName, currentAvatarId)}'.");
    }

    internal void ApplySharedReturnAvatarSelection(string avatarId, string? avatarName, bool saveImmediately)
    {
        var normalizedAvatarId = avatarId?.Trim() ?? string.Empty;
        if (string.IsNullOrWhiteSpace(normalizedAvatarId))
        {
            return;
        }

        EnsureMasterAvatarProfileExists();
        var masterProfile = MasterAvatarProfile;
        if (masterProfile is null)
        {
            return;
        }

        var resolvedName = string.IsNullOrWhiteSpace(avatarName)
            ? ResolveVrChatAvatarName(normalizedAvatarId)
            : avatarName.Trim();

        var changed = !string.Equals(masterProfile.AvatarId?.Trim() ?? string.Empty, normalizedAvatarId, StringComparison.Ordinal)
            || !string.Equals(masterProfile.AvatarName?.Trim() ?? string.Empty, resolvedName, StringComparison.Ordinal)
            || !string.Equals(Settings.MasterAvatarSwapReturnId?.Trim() ?? string.Empty, normalizedAvatarId, StringComparison.Ordinal)
            || !string.Equals(Settings.MasterAvatarSwapReturnName?.Trim() ?? string.Empty, resolvedName, StringComparison.Ordinal);

        masterProfile.AvatarId = normalizedAvatarId;
        masterProfile.AvatarName = resolvedName;
        Settings.MasterAvatarSwapReturnId = normalizedAvatarId;
        Settings.MasterAvatarSwapReturnName = resolvedName;

        ApplyMasterAvatarDefaults(masterProfile);
        UpdateAvatarProfileActivityStates();
        RefreshVrChatAvatarSelectionOptions();
        RaisePropertyChanged(nameof(MasterAvatarDisplayName));
        RaisePropertyChanged(nameof(MasterAvatarReturnText));
        RaisePropertyChanged(nameof(SelectedAvatarProfileStatusText));

        if (changed && saveImmediately)
        {
            QueueSave(0);
            QueueBridgeRefresh();
        }
    }

    private void AddRule()
    {
        var rule = IsViewingMovementRedeems
                ? CreateDefaultMovementRule()
            : IsViewingMasterAvatar
                ? CreateDefaultMasterAvatarRule()
                : CreateDefaultAvatarProfileRule();

        if (IsViewingMovementRedeems)
        {
            EnsureSelectedMovementRedeemSet();
            if (SelectedMovementRedeemSet is null)
            {
                return;
            }

            SelectedMovementRedeemSet.MovementRules.Add(rule);
        }
        else
        {
            if (IsViewingMasterAvatar)
            {
                EnsureMasterAvatarProfileExists();
                SelectedAvatarProfile = MasterAvatarProfile;
            }
            else if (SelectedAvatarProfile is null)
            {
                AddAvatarProfile();
            }

            if (SelectedAvatarProfile is null)
            {
                return;
            }

            SelectedAvatarProfile.ChannelPointRules.Add(rule);
            if (IsViewingMasterAvatar)
            {
                ApplyMasterAvatarDefaultsToRule(rule);
            }
        }

        SelectedRule = rule;
        QueueSave();
        QueueBridgeRefresh();
        AppendLog(IsViewingMovementRedeems
                ? $"Added movement redeem '{rule.DisplayTitle}'."
                : IsViewingMasterAvatar
                    ? $"Added master trigger '{rule.DisplayTitle}'."
            : $"Added trigger '{rule.DisplayTitle}' to '{SelectedAvatarProfile?.DisplayTitle}'.");
    }

    private void AddOutfitChoice()
    {
        if (!IsViewingAvatarTriggers)
        {
            return;
        }

        if (SelectedAvatarProfile is null)
        {
            AddAvatarProfile();
        }

        if (SelectedAvatarProfile is null)
        {
            return;
        }

        var choiceNumber = GetNextAvailableOutfitChoiceNumber(SelectedAvatarProfile);
        var rule = CreateDefaultOutfitChoiceRule(
            choiceNumber,
            SelectedAvatarProfile.UseSharedNumberedOutfitReward);
        SelectedAvatarProfile.ChannelPointRules.Add(rule);
        SelectedRule = rule;
        SelectedSetTriggerAction = rule.SetTriggerActions.FirstOrDefault();
        IsAvatarMixRedeemsExpanded = true;
        QueueSave();
        QueueBridgeRefresh();
        QueueManagedRewardSync(0);
        AppendLog($"Added outfit choice '{rule.DisplayTitle}' to '{SelectedAvatarProfile.DisplayTitle}'.");
    }

    private void RemoveSelectedOutfitChoice()
    {
        if (!IsViewingAvatarTriggers || SelectedRule?.ActionType != OscActionType.SetTrigger)
        {
            return;
        }

        RemoveSelectedRule();
    }

    private static int GetNextAvailableOutfitChoiceNumber(AvatarTriggerProfile profile)
    {
        var usedNumbers = profile.ChannelPointRules
            .Where(rule => rule.ActionType == OscActionType.SetTrigger)
            .Select(rule => Math.Max(0, rule.SharedRewardChoiceNumber))
            .Where(number => number > 0)
            .ToHashSet();

        var nextNumber = 1;
        while (usedNumbers.Contains(nextNumber))
        {
            nextNumber++;
        }

        return nextNumber;
    }

    public Task TestWardrobeOutfitPublicAsync(
        WardrobeOutfit outfit,
        AvatarTriggerProfile profile,
        CancellationToken cancellationToken)
    {
        return ExecuteTestWardrobeOutfitAsync(outfit, profile, cancellationToken);
    }

    private async Task ExecuteTestWardrobeOutfitAsync(
        WardrobeOutfit outfit,
        AvatarTriggerProfile profile,
        CancellationToken cancellationToken)
    {
        if (outfit is null || profile is null)
        {
            return;
        }

        await ReloadRuntimeConfigAsync();

        await bridgeRefreshGate.WaitAsync();
        try
        {
            await EnsureBridgeStateAsync(cancellationToken, allowOscOnly: true);

            if (!BridgeRuntimeConfiguration.TryToWardrobeSnapshot(outfit, profile, out var snapshot))
            {
                BridgeStatus = "Wardrobe outfit test did not run: outfit has no valid parameters.";
                AppendLog("Could not test wardrobe outfit: outfit is missing valid parameter snapshots.");
                return;
            }

            var applied = await bridgeCoordinator.ExecuteWardrobeOutfitAsync(snapshot, cancellationToken);
            if (applied)
            {
                BridgeStatus = $"Sent test for wardrobe outfit '{snapshot.Name}'.";
            }
            else
            {
                BridgeStatus = "Wardrobe outfit test did not run: VRChat may not be connected or avatar cache may not be available.";
                AppendLog("Could not test wardrobe outfit: VRChat may not be connected or avatar cache may not be available.");
            }
        }
        catch (Exception ex)
        {
            BridgeStatus = "Wardrobe outfit test did not run.";
            AppendLog($"Could not test wardrobe outfit: {ex.Message}");
        }
        finally
        {
            bridgeRefreshGate.Release();
        }
    }

    public async Task<IReadOnlyList<VrChatOscParameterSummary>> LoadAvatarParameterSummariesAsync(string? avatarId)
    {
        if (string.IsNullOrWhiteSpace(avatarId))
        {
            // Try fallback to current VRChat avatar ID if signed in
            var currentAvatarId = Settings?.VrChat?.CurrentAvatarId?.Trim() ?? string.Empty;
            if (!string.IsNullOrWhiteSpace(currentAvatarId))
            {
                avatarId = currentAvatarId;
            }
            else
            {
                return [];
            }
        }

        var userId = ResolveCurrentUserIdForCache();
        if (string.IsNullOrWhiteSpace(userId))
        {
            // Fall back to scanning OSC folder by avatar ID directly
            AppendLog("VRChat user ID is not set — signing in to VRChat via the main window's VRChat login enables parameter loading, OR Crystal Relay will scan the avatar OSC file directly.");
            try
            {
                var parameters = await vrChatLocalOscCacheService.LoadAvatarParametersByAvatarIdAsync(avatarId, CancellationToken.None);
                if (parameters.Count > 0)
                {
                    AppendLog($"Loaded {parameters.Count} parameters for avatar {avatarId} by scanning OSC folder.");
                }
                else
                {
                    AppendLog($"Could not find avatar OSC file for avatar ID '{avatarId}' in OSC folder. Make sure the avatar file exists in %LOCALAPPDATA%..\\..\\LocalLow\\VRChat\\VRChat\\OSC\\ or sign in to VRChat via the main window.");
                }
                return parameters.ToList();
            }
            catch (Exception ex)
            {
                AppendLog($"Could not load avatar parameters by avatar ID: {ex.Message}");
                return [];
            }
        }

        try
        {
            var parameters = await vrChatLocalOscCacheService.LoadAvatarParametersAsync(userId, avatarId, CancellationToken.None);
            return parameters.ToList();
        }
        catch (Exception ex)
        {
            AppendLog($"Could not load avatar parameters: {ex.Message}");
            return [];
        }
    }

    public async Task<IReadOnlyList<TwitchApiClient.CustomRewardResponse>> LoadTwitchCustomRewardsAsync(CancellationToken cancellationToken = default)
    {
        if (Settings?.Broadcaster?.AccessToken is not { Length: > 0 } accessToken
            || string.IsNullOrWhiteSpace(Settings.Broadcaster.UserId))
        {
            return [];
        }

        var clientId = runtimeConfig?.TwitchClientId ?? string.Empty;
        if (string.IsNullOrWhiteSpace(clientId))
        {
            return [];
        }

        try
        {
            return await twitchApiClient.GetCustomRewardsAsync(
                accessToken,
                clientId,
                Settings.Broadcaster.UserId,
                cancellationToken,
                onlyManageableRewards: false);
        }
        catch (Exception ex)
        {
            AppendLog($"Could not load Twitch custom rewards: {ex.Message}");
            return [];
        }
    }

    public IReadOnlyList<VrChatAvatarSummary> GetAllVrChatAvatars()
        => availableVrChatAvatars
            .Select(a => a with { })
            .ToList();

    public string? TryGetVrChatAvatarThumbnailUrl(string? avatarId)
    {
        if (string.IsNullOrWhiteSpace(avatarId)) return null;
        var match = availableVrChatAvatars.FirstOrDefault(a =>
            string.Equals(a.Id?.Trim(), avatarId.Trim(), StringComparison.OrdinalIgnoreCase));
        return match?.ThumbnailUrl;
    }

    public void RetireManagedRewardsPublic(IEnumerable<TriggerRule> rules) => RetireManagedRewards(rules);

    public void RetireWardrobeManagedReward(WardrobeOutfit outfit)
    {
        if (outfit is null) return;
        if (outfit.TwitchRewardSyncMode != TwitchRewardSyncMode.CreateOrManage) return;
        var rewardId = outfit.TwitchRewardId?.Trim();
        if (string.IsNullOrWhiteSpace(rewardId)) return;
        retiredManagedRewardIds.Add(rewardId);
        QueueManagedRewardSync();
    }

    public void QueueManagedRewardSyncPublic(ManagedRewardSyncReasonPublic reason = ManagedRewardSyncReasonPublic.SettingsEdit)
        => QueueManagedRewardSync(0, MapSyncReason(reason));

    public enum ManagedRewardSyncReasonPublic
    {
        SettingsEdit,
        ManualRefresh
    }

    private ManagedRewardSyncReason MapSyncReason(ManagedRewardSyncReasonPublic publicReason) => publicReason switch
    {
        ManagedRewardSyncReasonPublic.ManualRefresh => ManagedRewardSyncReason.ManualRefresh,
        _ => ManagedRewardSyncReason.SettingsEdit
    };

    public void DeleteAvatarProfilePublic(AvatarTriggerProfile profile)
    {
        if (profile is null) return;
        SelectedAvatarProfile = profile;
        DeleteSelectedAvatarProfile();
    }

    private void SelectRule(object? target)
    {
        if (target is TriggerRule rule)
        {
            SelectedRule = rule;
        }
    }

    private void AddAvatarSupporterTrigger()
    {
        TryResolveDefaultSupporterAvatar(out var avatarId, out var avatarName);

        var rule = CreateDefaultAvatarSupporterRule(avatarId, avatarName);
        Settings.GlobalOverrideRules.Add(rule);
        SelectedRule = rule;
        QueueSave();
        QueueBridgeRefresh();
        RaiseSupporterRuleGroupProperties();
        AppendLog($"Added avatar supporter trigger '{rule.DisplayTitle}' for '{FormatSupporterAvatarScopeLabel(avatarId, avatarName)}'.");
    }

    private void AddForceMovementOverride()
    {
        var rule = CreateDefaultForceMovementOverrideRule();
        Settings.GlobalOverrideRules.Add(rule);
        SelectedRule = rule;
        QueueSave();
        QueueBridgeRefresh();
        RaiseSupporterRuleGroupProperties();
        AppendLog($"Added force movement override '{rule.DisplayTitle}'.");
    }

    private void RemoveSelectedRule()
    {
        if (SelectedRule is null)
        {
            return;
        }

        var removedName = SelectedRule.DisplayTitle;
        var removedRule = SelectedRule;
        if (IsViewingMovementRedeems)
        {
            SelectedMovementRedeemSet?.MovementRules.Remove(SelectedRule);
            SelectedRule = SelectedMovementRedeemSet?.MovementRules.FirstOrDefault();
        }
        else if (IsViewingMasterAvatar)
        {
            MasterAvatarProfile?.ChannelPointRules.Remove(SelectedRule);
            SelectedRule = MasterAvatarProfile?.ChannelPointRules.FirstOrDefault();
        }
        else if (SelectedAvatarProfile is not null)
        {
            SelectedAvatarProfile.ChannelPointRules.Remove(SelectedRule);
            SelectedRule = SelectedAvatarProfile.ChannelPointRules.FirstOrDefault();
        }

        RemoveSpecialRuleLockoutReferencesToRule(removedRule.Id);
        RefreshSpecialRuleLockoutOptions();

        if (!IsViewingMovementRedeems)
        {
            RetireManagedRewards([removedRule]);
        }

        ForgetRememberedRule(removedRule);

        QueueSave();
        QueueBridgeRefresh();
        AppendLog($"Removed rule '{removedName}'.");
    }

    private void EnableAllRules()
    {
        foreach (var rule in GetCurrentEditableRuleCollection().Where(rule => !rule.IsEnabled))
        {
            rule.IsEnabled = true;
        }

        QueueSave();
        QueueBridgeRefresh();
        AppendLog("Enabled all rules.");
    }

    private void DisableAllRules()
    {
        foreach (var rule in GetCurrentEditableRuleCollection().Where(rule => rule.IsEnabled))
        {
            rule.IsEnabled = false;
        }

        QueueSave();
        QueueBridgeRefresh();
        AppendLog("Disabled all rules.");
    }

    private void DeleteAllRules()
    {
        if (!ConfirmDeleteAll(
            "Delete All Overrides",
            "Are you sure you want to delete every Bits + Subs override? This cannot be undone."))
        {
            return;
        }

        var currentRules = GetCurrentEditableRuleCollection();
        var removedCount = currentRules.Count;
        if (!IsViewingMovementRedeems)
        {
            RetireManagedRewards(currentRules.ToArray());
        }
        foreach (var rule in currentRules.ToArray())
        {
            RemoveSpecialRuleLockoutReferencesToRule(rule.Id);
        }
        ForgetRememberedRules(currentRules);
        currentRules.Clear();
        SelectedRule = null;
        RefreshSpecialRuleLockoutOptions();
        QueueSave();
        QueueBridgeRefresh();
        AppendLog($"Deleted {removedCount} override rule{(removedCount == 1 ? string.Empty : "s")}.");
    }

    private void AddAvatarScaleSet()
    {
        var set = CreateDefaultAvatarScaleSet();
        Settings.AvatarScaleSets.Add(set);
        SelectedAvatarScaleSet = set;
        SelectedAvatarScaleRule = null;
        QueueSave();
        AppendLog($"Added scale set '{set.DisplayTitle}'.");
    }

    private void RemoveSelectedAvatarScaleSet()
    {
        if (SelectedAvatarScaleSet is null)
        {
            return;
        }

        var removedName = SelectedAvatarScaleSet.DisplayTitle;
        foreach (var rule in SelectedAvatarScaleSet.ScaleRules)
        {
            RemoveAvatarScaleRuleLockoutReferencesToRule(rule.Id);
        }

        Settings.AvatarScaleSets.Remove(SelectedAvatarScaleSet);
        SelectedAvatarScaleSet = GetRememberedAvatarScaleSet();
        SelectedAvatarScaleRule = GetRememberedAvatarScaleRule();
        QueueSave();
        QueueBridgeRefresh();
        AppendLog($"Removed scale set '{removedName}'.");
    }

    private void AddAvatarScaleRule()
    {
        EnsureSelectedAvatarScaleSet();
        if (SelectedAvatarScaleSet is null)
        {
            return;
        }

        var rule = CreateDefaultAvatarScaleRule();
        SelectedAvatarScaleSet.ScaleRules.Add(rule);
        SelectedAvatarScaleRule = rule;
        QueueSave();
        QueueBridgeRefresh();
        AppendLog($"Added avatar scale redeem '{rule.DisplayTitle}' to '{SelectedAvatarScaleSet.DisplayTitle}'.");
    }

    private void AddRewardGrowth()
    {
        EnsureSelectedAvatarScaleSet();
        if (SelectedAvatarScaleSet is null)
        {
            return;
        }

        var rule = CreateDefaultAvatarScaleRule();
        rule.TriggerType = AvatarScaleTriggerType.SupporterGrowth;
        rule.Name = "New Supporter Growth";
        SelectedAvatarScaleSet.ScaleRules.Add(rule);
        SelectedAvatarScaleRule = rule;
        QueueSave();
        QueueBridgeRefresh();
        AppendLog($"Added supporter growth rule '{rule.DisplayTitle}' to '{SelectedAvatarScaleSet.DisplayTitle}'.");
    }

    private void RemoveSelectedAvatarScaleRule()
    {
        if (SelectedAvatarScaleRule is null)
        {
            return;
        }

        var removedName = SelectedAvatarScaleRule.DisplayTitle;
        RemoveAvatarScaleRuleLockoutReferencesToRule(SelectedAvatarScaleRule.Id);
        var ownerSet = GetOwningAvatarScaleSet(SelectedAvatarScaleRule);
        ownerSet?.ScaleRules.Remove(SelectedAvatarScaleRule);
        SelectedAvatarScaleRule = GetRememberedAvatarScaleRule();
        QueueSave();
        QueueBridgeRefresh();
        AppendLog($"Removed avatar scale redeem '{removedName}'.");
    }

    public void DeleteAvatarScaleRuleByCard(AvatarScaleRule rule)
    {
        RemoveAvatarScaleRuleLockoutReferencesToRule(rule.Id);
        var ownerSet = GetOwningAvatarScaleSet(rule);
        ownerSet?.ScaleRules.Remove(rule);
        if (ReferenceEquals(SelectedAvatarScaleRule, rule))
        {
            SelectedAvatarScaleRule = GetRememberedAvatarScaleRule();
        }
        QueueSave();
        QueueBridgeRefresh();
        AppendLog($"Removed avatar scale redeem '{rule.DisplayTitle}'.");
    }

    public void DeleteCashPaymentRuleByCard(CashPaymentRule rule)
    {
        Settings.CashPaymentRules.Remove(rule);
        QueueSave();
        QueueBridgeRefresh();
        AppendLog($"Removed cash payment rule '{rule.DisplayTitle}'.");
    }

    public void DeletePowerUpRuleByCard(PowerUpRule rule)
    {
        Settings.PowerUpRules.Remove(rule);
        if (ReferenceEquals(SelectedPowerUpRule, rule))
        {
            SelectedPowerUpRule = GetRememberedPowerUpRule();
        }
        QueueSave();
        QueueBridgeRefresh();
        AppendLog($"Removed Power Up rule '{rule.DisplayTitle}'.");
    }

    private void EnableAllAvatarScaleRules()
    {
        foreach (var rule in GetAllAvatarScaleRules().Where(rule => !rule.IsEnabled))
        {
            rule.IsEnabled = true;
        }

        QueueSave();
        QueueBridgeRefresh();
        AppendLog("Enabled all avatar scale redeems.");
    }

    private void DisableAllAvatarScaleRules()
    {
        foreach (var rule in GetAllAvatarScaleRules().Where(rule => rule.IsEnabled))
        {
            rule.IsEnabled = false;
        }

        QueueSave();
        QueueBridgeRefresh();
        AppendLog("Disabled all avatar scale redeems.");
    }

    private void DeleteAllAvatarScaleSets()
    {
        if (!ConfirmDeleteAll(
            "Delete Avatar Scale Sets",
            "Are you sure you want to delete every avatar scale set and scale redeem? This cannot be undone."))
        {
            return;
        }

        var removedRules = GetAllAvatarScaleRules().ToArray();
        var removedCount = Settings.AvatarScaleSets.Count;
        RetireManagedRewards(removedRules);
        Settings.AvatarScaleSets.Clear();
        SelectedAvatarScaleSet = null;
        SelectedAvatarScaleRule = null;
        QueueSave();
        QueueBridgeRefresh();
        AppendLog($"Deleted {removedCount} avatar scale set{(removedCount == 1 ? string.Empty : "s")}.");
    }

    private static CashPaymentRule CreateDefaultAvatarScalingCashPaymentRule()
    {
        var rule = new CashPaymentRule
        {
            Name = "New Cash Payment Scale",
            Provider = CashPaymentProvider.StreamElements,
            MinimumAmount = 1m,
            MaximumAmount = 0m,
            CurrencyCode = string.Empty,
            MessageContains = string.Empty,
            CooldownSeconds = 30,
            ActionKind = CashPaymentActionKind.AvatarScaling
        };
        rule.ScaleAction = CashPaymentRule.CreateDefaultScaleAction();
        rule.ScaleAction.Name = rule.Name;
        return rule;
    }

    private void AddAvatarScalingCashPaymentRule()
    {
        var rule = CreateDefaultAvatarScalingCashPaymentRule();
        Settings.CashPaymentRules.Add(rule);
        QueueSave();
        QueueBridgeRefresh();
        AppendLog($"Added cash payment scaling rule '{rule.DisplayTitle}'.");
    }

    private static PowerUpRule CreateDefaultPowerUpRule()
    {
        var rule = new PowerUpRule
        {
            Name = "New Power Up",
            SourceMode = TwitchRewardSyncMode.LinkExisting,
            BitsCost = 100,
            CooldownSeconds = 30,
            ActionKind = PowerUpActionKind.TriggerAction
        };
        rule.ActionRule = PowerUpRule.CreateDefaultTriggerAction();
        rule.ActionRule.Name = rule.Name;
        rule.ScaleAction = PowerUpRule.CreateDefaultScaleAction();
        rule.ScaleAction.Name = rule.Name;
        return rule;
    }

    private static PowerUpRule CreateDefaultAvatarScalingPowerUpRule()
    {
        var rule = new PowerUpRule
        {
            Name = "New Power Up Scale",
            SourceMode = TwitchRewardSyncMode.LinkExisting,
            BitsCost = 100,
            CooldownSeconds = 30,
            ActionKind = PowerUpActionKind.AvatarScaling
        };
        rule.ScaleAction = PowerUpRule.CreateDefaultScaleAction();
        rule.ScaleAction.Name = rule.Name;
        return rule;
    }

    private void AddPowerUpRule()
    {
        var rule = CreateDefaultPowerUpRule();
        Settings.PowerUpRules.Add(rule);
        SelectedPowerUpRule = rule;
        QueueSave();
        QueueBridgeRefresh();
        AppendLog($"Added Power Up rule '{rule.DisplayTitle}'.");
    }

    private void AddAvatarScalingPowerUpRule()
    {
        var rule = CreateDefaultAvatarScalingPowerUpRule();
        Settings.PowerUpRules.Add(rule);
        SelectedPowerUpRule = rule;
        QueueSave();
        QueueBridgeRefresh();
        AppendLog($"Added Power Up scaling rule '{rule.DisplayTitle}'.");
    }

    private void RemoveSelectedPowerUpRule()
    {
        if (SelectedPowerUpRule is null)
        {
            return;
        }

        var removedName = SelectedPowerUpRule.DisplayTitle;
        Settings.PowerUpRules.Remove(SelectedPowerUpRule);
        SelectedPowerUpRule = GetRememberedPowerUpRule();
        QueueSave();
        QueueBridgeRefresh();
        AppendLog($"Removed Power Up rule '{removedName}'.");
    }

    private void EnableAllPowerUpRules()
    {
        foreach (var rule in Settings.PowerUpRules.Where(rule => !rule.IsEnabled))
        {
            rule.IsEnabled = true;
        }

        QueueSave();
        QueueBridgeRefresh();
        AppendLog("Enabled all Power Up rules.");
    }

    private void DisableAllPowerUpRules()
    {
        foreach (var rule in Settings.PowerUpRules.Where(rule => rule.IsEnabled))
        {
            rule.IsEnabled = false;
        }

        QueueSave();
        QueueBridgeRefresh();
        AppendLog("Disabled all Power Up rules.");
    }

    private void DeleteAllPowerUpRules()
    {
        if (!ConfirmDeleteAll(
            "Delete Power Up Rules",
            "Are you sure you want to delete every Power Up rule? This cannot be undone. Linked Twitch Custom Power-ups are kept unchanged."))
        {
            return;
        }

        var removedCount = Settings.PowerUpRules.Count;
        Settings.PowerUpRules.Clear();
        SelectedPowerUpRule = null;
        QueueSave();
        QueueBridgeRefresh();
        AppendLog($"Deleted {removedCount} Power Up rule{(removedCount == 1 ? string.Empty : "s")}.");
    }

    private void UnlinkPowerUp(object? target)
    {
        if (target is not PowerUpRule rule)
        {
            return;
        }

        rule.PowerUpId = string.Empty;
        rule.PowerUpTitle = string.Empty;
        rule.Prompt = string.Empty;
        QueueSave();
        QueueBridgeRefresh();
        AppendLog($"Unlinked Power Up rule '{rule.DisplayTitle}'.");
    }

    private void UseCurrentAvatarForPowerUpRule()
    {
        if (SelectedPowerUpRule is null)
        {
            return;
        }

        var avatarId = GetResolvedCurrentVrChatAvatarId();
        if (string.IsNullOrWhiteSpace(avatarId))
        {
            return;
        }

        SelectedPowerUpRule.AvatarScoped = true;
        SelectedPowerUpRule.AvatarId = avatarId;
        SelectedPowerUpRule.AvatarName = GetSafeVrChatAvatarDisplayName(ResolveVrChatAvatarName(avatarId), avatarId);
        QueueSave();
        QueueBridgeRefresh();
        AppendLog($"Power Up rule '{SelectedPowerUpRule.DisplayTitle}' now belongs to '{SelectedPowerUpRule.AvatarScopeLabel}'.");
    }

    private void RegenerateKoFiRelayIdentity()
    {
        Settings.CashPayments.RegenerateKoFiRelayIdentity();
        QueueSave();
        QueueBridgeRefresh();
        AppendLog("Regenerated the Ko-fi hosted relay webhook URL.");
    }

    private async Task ImportFoomaInteractionConfigAsync()
    {
        var dialog = new OpenFileDialog
        {
            Title = T("Import Fooma Interaction Config"),
            Filter = "JSON files (*.json)|*.json|All files (*.*)|*.*",
            CheckFileExists = true,
            Multiselect = false
        };

        if (dialog.ShowDialog(Application.Current?.MainWindow) != true)
        {
            return;
        }

        try
        {
            var result = await FoomaInteractionConfigImporter.ImportAsync(dialog.FileName);
            var upsertResult = UpsertImportedUniversalTriggers(result.Triggers);

            var additionalFusedCount = UniversalTriggerFusionService.FuseMatchingCommandFallbacks(Settings.UniversalTriggers);
            QueueSave(0);
            QueueBridgeRefresh();
            QueueManagedRewardSync(0);
            RefreshRuleCommandStates();
            var fusedCount = result.FusedCommandCount + additionalFusedCount;
            var summary = fusedCount > 0
                ? TF("Imported {0} universal trigger(s): {1} added, {2} updated. Fused {3} matching chat command(s) into rewards. Skipped {4} invalid item(s).", result.ImportedCount, upsertResult.AddedCount, upsertResult.UpdatedCount, fusedCount, result.SkippedCount)
                : TF("Imported {0} universal trigger(s): {1} added, {2} updated. Skipped {3} invalid item(s).", result.ImportedCount, upsertResult.AddedCount, upsertResult.UpdatedCount, result.SkippedCount);
            AppendLog(summary);
            ThemedDialogWindow.ShowOk(
                Application.Current?.MainWindow,
                SelectedTheme,
                T("Fooma Import Complete"),
                summary,
                T("OK"));
        }
        catch (Exception ex)
        {
            AppendLog($"Fooma import failed: {ex.Message}");
            ThemedDialogWindow.ShowOk(
                Application.Current?.MainWindow,
                SelectedTheme,
                T("Fooma Import Failed"),
                ex.Message,
                T("OK"));
        }
    }

    private (int AddedCount, int UpdatedCount, UniversalTriggerRule? FirstTouchedTrigger) UpsertImportedUniversalTriggers(
        IReadOnlyList<UniversalTriggerRule> importedTriggers)
    {
        var addedCount = 0;
        var updatedCount = 0;
        UniversalTriggerRule? firstTouchedTrigger = null;

        foreach (var importedTrigger in importedTriggers)
        {
            var existingTrigger = FindExistingImportedUniversalTrigger(importedTrigger);
            if (existingTrigger is null)
            {
                Settings.UniversalTriggers.Add(importedTrigger);
                firstTouchedTrigger ??= importedTrigger;
                addedCount++;
                continue;
            }

            ApplyImportedUniversalTriggerUpdate(existingTrigger, importedTrigger);
            firstTouchedTrigger ??= existingTrigger;
            updatedCount++;
        }

        return (addedCount, updatedCount, firstTouchedTrigger);
    }

    private UniversalTriggerRule? FindExistingImportedUniversalTrigger(UniversalTriggerRule importedTrigger)
    {
        if (!FoomaInteractionConfigImporter.IsFoomaImport(importedTrigger))
        {
            return null;
        }

        var importIdentity = importedTrigger.ImportIdentity.Trim();
        var legacyImportIdentity = FoomaInteractionConfigImporter.BuildLegacyImportIdentity(importedTrigger);

        return Settings.UniversalTriggers.FirstOrDefault(existingTrigger =>
        {
            if (!FoomaInteractionConfigImporter.IsFoomaImport(existingTrigger))
            {
                return false;
            }

            if (!string.IsNullOrWhiteSpace(importIdentity)
                && string.Equals(existingTrigger.ImportIdentity?.Trim() ?? string.Empty, importIdentity, StringComparison.OrdinalIgnoreCase))
            {
                return true;
            }

            return string.IsNullOrWhiteSpace(existingTrigger.ImportIdentity)
                && !string.IsNullOrWhiteSpace(legacyImportIdentity)
                && string.Equals(
                    FoomaInteractionConfigImporter.BuildLegacyImportIdentity(existingTrigger),
                    legacyImportIdentity,
                    StringComparison.OrdinalIgnoreCase);
        });
    }

    private static void ApplyImportedUniversalTriggerUpdate(
        UniversalTriggerRule existingTrigger,
        UniversalTriggerRule importedTrigger)
    {
        var preservedRewardId = existingTrigger.RewardId;
        var preservedRewardDescription = existingTrigger.RewardDescription;
        var preservedRewardCost = existingTrigger.RewardCost;
        var preservedRewardCooldownSeconds = existingTrigger.RewardCooldownSeconds;
        var preservedRewardSyncMode = existingTrigger.RewardSyncMode;
        var preservedReadyColor = existingTrigger.ManagedRewardReadyColor;
        var preservedCooldownColor = existingTrigger.ManagedRewardCooldownColor;
        var preservedDeleteWhenInactive = existingTrigger.DeleteManagedRewardWhenInactive;

        existingTrigger.IsEnabled = importedTrigger.IsEnabled;
        existingTrigger.Name = importedTrigger.Name;
        existingTrigger.TriggerType = importedTrigger.TriggerType;
        existingTrigger.ChatCommandEnabled = importedTrigger.ChatCommandEnabled;
        existingTrigger.CommandText = importedTrigger.CommandText;
        existingTrigger.ChatCommandPermission = importedTrigger.ChatCommandPermission;
        existingTrigger.RewardTitle = importedTrigger.RewardTitle;
        existingTrigger.RewardId = string.IsNullOrWhiteSpace(preservedRewardId)
            ? importedTrigger.RewardId
            : preservedRewardId;
        existingTrigger.RewardDescription = string.IsNullOrWhiteSpace(preservedRewardDescription)
            ? importedTrigger.RewardDescription
            : preservedRewardDescription;
        existingTrigger.RewardCost = preservedRewardCost;
        existingTrigger.RewardCooldownSeconds = preservedRewardCooldownSeconds;
        existingTrigger.RewardSyncMode = preservedRewardSyncMode;
        existingTrigger.ManagedRewardReadyColor = preservedReadyColor;
        existingTrigger.ManagedRewardCooldownColor = preservedCooldownColor;
        existingTrigger.DeleteManagedRewardWhenInactive = preservedDeleteWhenInactive;
        existingTrigger.MinimumBits = importedTrigger.MinimumBits;
        existingTrigger.MaximumBits = importedTrigger.MaximumBits;
        existingTrigger.SubscriptionTier = importedTrigger.SubscriptionTier;
        existingTrigger.MinimumMonths = importedTrigger.MinimumMonths;
        existingTrigger.MaximumMonths = importedTrigger.MaximumMonths;
        existingTrigger.GlobalDelaySeconds = importedTrigger.GlobalDelaySeconds;
        existingTrigger.UserDelaySeconds = importedTrigger.UserDelaySeconds;
        existingTrigger.ExecuteRandomAction = importedTrigger.ExecuteRandomAction;
        existingTrigger.ImportSource = importedTrigger.ImportSource;
        existingTrigger.ImportIdentity = importedTrigger.ImportIdentity;
        existingTrigger.Actions = new ObservableCollection<UniversalTriggerAction>(
            importedTrigger.Actions.Select(CloneUniversalTriggerAction));
    }

    private static UniversalTriggerAction CloneUniversalTriggerAction(UniversalTriggerAction action)
    {
        return new UniversalTriggerAction
        {
            OscAddress = action.OscAddress,
            ValueKind = action.ValueKind,
            TargetValue = action.TargetValue,
            DefaultValue = action.DefaultValue,
            DurationSeconds = action.DurationSeconds,
            AddToQueue = action.AddToQueue,
            ImportGroupKey = action.ImportGroupKey
        };
    }

    private bool ConfirmDeleteAll(string title, string warningMessage)
    {
        return ThemedDialogWindow.ShowYesNo(
            Application.Current?.MainWindow,
            SelectedTheme,
            T(title),
            T(warningMessage),
            T("Delete All"),
            T("Cancel"));
    }

    private void RetireManagedRewards(IEnumerable<TriggerRule> rules)
    {
        var ruleArray = rules.ToArray();
        var retiredIds = ruleArray
            .Where(rule => rule.RewardSyncMode == TwitchRewardSyncMode.CreateOrManage)
            .Select(rule => rule.ChannelPointRewardId?.Trim())
            .Concat(ruleArray.Select(rule => rule.ActiveFloatBoostRewardId?.Trim()))
            .Where(rewardId => !string.IsNullOrWhiteSpace(rewardId))
            .ToArray();

        if (retiredIds.Length == 0)
        {
            return;
        }

        foreach (var rewardId in retiredIds)
        {
            retiredManagedRewardIds.Add(rewardId!);
        }

        QueueManagedRewardSync();
    }

    private void RetireManagedRewards(IEnumerable<UniversalTriggerRule> triggers)
    {
        var retiredIds = triggers
            .Where(trigger => trigger.RewardSyncMode == TwitchRewardSyncMode.CreateOrManage
                && (trigger.TriggerType == UniversalTriggerType.ChannelPointReward
                || !string.IsNullOrWhiteSpace(trigger.RewardId))
            )
            .Select(trigger => trigger.RewardId?.Trim())
            .Where(rewardId => !string.IsNullOrWhiteSpace(rewardId))
            .ToArray();

        if (retiredIds.Length == 0)
        {
            return;
        }

        foreach (var rewardId in retiredIds)
        {
            retiredManagedRewardIds.Add(rewardId!);
        }

        QueueManagedRewardSync();
    }

    private void RetireManagedRewards(IEnumerable<AvatarScaleRule> rules)
    {
        var retiredIds = rules
            .Where(rule => rule.RewardSyncMode == TwitchRewardSyncMode.CreateOrManage
                && (rule.TriggerType == AvatarScaleTriggerType.ChannelPointReward
                || !string.IsNullOrWhiteSpace(rule.RewardId))
            )
            .Select(rule => rule.RewardId?.Trim())
            .Where(rewardId => !string.IsNullOrWhiteSpace(rewardId))
            .ToArray();

        if (retiredIds.Length == 0)
        {
            return;
        }

        foreach (var rewardId in retiredIds)
        {
            retiredManagedRewardIds.Add(rewardId!);
        }

        QueueManagedRewardSync();
    }

    private TriggerRule? GetRememberedRuleForProfile(AvatarTriggerProfile profile)
    {
        if (lastSelectedRuleIdsByAvatarProfileId.TryGetValue(profile.Id, out var rememberedRuleId))
        {
            var rememberedRule = profile.ChannelPointRules.FirstOrDefault(rule => rule.Id == rememberedRuleId);
            if (rememberedRule is not null)
            {
                return rememberedRule;
            }
        }

        return profile.ChannelPointRules.FirstOrDefault();
    }

    private AvatarTriggerProfile? GetRememberedAvatarRuleProfile()
    {
        if (lastSelectedAvatarProfileId != Guid.Empty)
        {
            var rememberedProfile = AvatarRuleProfiles.FirstOrDefault(profile => profile.Id == lastSelectedAvatarProfileId);
            if (rememberedProfile is not null)
            {
                return rememberedProfile;
            }
        }

        return AvatarRuleProfiles.FirstOrDefault();
    }

    private TriggerRule? GetRememberedMasterRule()
    {
        var profile = MasterAvatarProfile;
        if (profile is null)
        {
            return null;
        }

        if (lastSelectedMasterRuleId != Guid.Empty)
        {
            var rememberedRule = profile.ChannelPointRules.FirstOrDefault(rule => rule.Id == lastSelectedMasterRuleId);
            if (rememberedRule is not null)
            {
                return rememberedRule;
            }
        }

        return profile.ChannelPointRules.FirstOrDefault();
    }

    private TriggerRule? GetRememberedSupporterRule()
    {
        if (lastSelectedSupporterRuleId != Guid.Empty)
        {
            var rememberedRule = Settings.GlobalOverrideRules.FirstOrDefault(rule => rule.Id == lastSelectedSupporterRuleId);
            if (rememberedRule is not null)
            {
                return rememberedRule;
            }
        }

        return Settings.GlobalOverrideRules.FirstOrDefault();
    }

    private AvatarScaleRule? GetRememberedAvatarScaleRule()
    {
        IEnumerable<AvatarScaleRule> candidateRules = (IEnumerable<AvatarScaleRule>?)SelectedAvatarScaleSet?.ScaleRules
            ?? GetAllAvatarScaleRules();
        if (lastSelectedAvatarScaleRuleId != Guid.Empty)
        {
            var rememberedRule = candidateRules.FirstOrDefault(rule => rule.Id == lastSelectedAvatarScaleRuleId);
            if (rememberedRule is not null)
            {
                return rememberedRule;
            }
        }

        return candidateRules.FirstOrDefault();
    }

    private AvatarScaleSet? GetRememberedAvatarScaleSet()
    {
        if (lastSelectedAvatarScaleSetId != Guid.Empty)
        {
            var rememberedSet = Settings.AvatarScaleSets.FirstOrDefault(set => set.Id == lastSelectedAvatarScaleSetId);
            if (rememberedSet is not null)
            {
                return rememberedSet;
            }
        }

        var rememberedRuleOwner = lastSelectedAvatarScaleRuleId == Guid.Empty
            ? null
            : Settings.AvatarScaleSets.FirstOrDefault(set => set.ScaleRules.Any(rule => rule.Id == lastSelectedAvatarScaleRuleId));
        return rememberedRuleOwner ?? Settings.AvatarScaleSets.FirstOrDefault();
    }

    private PowerUpRule? GetRememberedPowerUpRule()
    {
        if (lastSelectedPowerUpRuleId != Guid.Empty)
        {
            var rememberedRule = Settings.PowerUpRules.FirstOrDefault(rule => rule.Id == lastSelectedPowerUpRuleId);
            if (rememberedRule is not null)
            {
                return rememberedRule;
            }
        }

        return Settings.PowerUpRules.FirstOrDefault();
    }

    private void EnsureSelectedMovementRedeemSet()
    {
        if (SelectedMovementRedeemSet is not null)
            return;

        SelectedMovementRedeemSet = Settings.MovementRedeemSets.FirstOrDefault();
        if (SelectedMovementRedeemSet is not null)
            return;

        var set = CreateDefaultMovementRedeemSet();
        Settings.MovementRedeemSets.Add(set);
        SelectedMovementRedeemSet = set;
    }

    private MovementRedeemSet? GetOwningMovementRedeemSet(TriggerRule rule)
    {
        return Settings.MovementRedeemSets.FirstOrDefault(set => set.MovementRules.Contains(rule));
    }

    private List<TriggerRule> GetAllMovementRules()
    {
        return Settings.MovementRedeemSets.SelectMany(set => set.MovementRules).ToList();
    }

    private void SyncLegacyGlobalMovementRules()
    {
        var flattenedRules = GetAllMovementRules();
        if (Settings.GlobalMovementRules.SequenceEqual(flattenedRules))
        {
            return;
        }

        Settings.GlobalMovementRules = new ObservableCollection<TriggerRule>(flattenedRules);
    }

    private void EnsureSelectedAvatarScaleSet()
    {
        if (SelectedAvatarScaleSet is not null)
        {
            return;
        }

        SelectedAvatarScaleSet = GetRememberedAvatarScaleSet();
        if (SelectedAvatarScaleSet is not null)
        {
            return;
        }

        var set = CreateDefaultAvatarScaleSet();
        Settings.AvatarScaleSets.Add(set);
        SelectedAvatarScaleSet = set;
    }

    private AvatarScaleSet? GetOwningAvatarScaleSet(AvatarScaleRule rule)
    {
        return Settings.AvatarScaleSets.FirstOrDefault(set => set.ScaleRules.Contains(rule));
    }

    private List<AvatarScaleRule> GetAllAvatarScaleRules()
    {
        return Settings.AvatarScaleSets.SelectMany(set => set.ScaleRules).ToList();
    }

    private AvatarTriggerProfile? GetOwningAvatarProfile(TriggerRule rule)
    {
        return Settings.AvatarProfiles.FirstOrDefault(profile => profile.ChannelPointRules.Contains(rule));
    }

    private void RememberSelectedRuleForCurrentView(TriggerRule? rule)
    {
        if (rule is null)
        {
            return;
        }

        if (IsViewingPowerUps)
        {
            var owner = Settings.PowerUpRules.FirstOrDefault(powerUp => ReferenceEquals(powerUp.ActionRule, rule));
            if (owner is not null)
            {
                lastSelectedPowerUpRuleId = owner.Id;
            }

            return;
        }

        var ownerProfile = GetOwningAvatarProfile(rule);
        if (ownerProfile is null)
        {
            return;
        }

        lastSelectedRuleIdsByAvatarProfileId[ownerProfile.Id] = rule.Id;
        if (ownerProfile.IsMasterProfile)
        {
            lastSelectedMasterRuleId = rule.Id;
        }
    }

    private void ForgetRememberedRule(TriggerRule? rule)
    {
        if (rule is null)
        {
            return;
        }

        if (lastSelectedSupporterRuleId == rule.Id)
        {
            lastSelectedSupporterRuleId = Guid.Empty;
        }

        if (lastSelectedMasterRuleId == rule.Id)
        {
            lastSelectedMasterRuleId = Guid.Empty;
        }

        foreach (var profileId in lastSelectedRuleIdsByAvatarProfileId
                     .Where(pair => pair.Value == rule.Id)
                     .Select(pair => pair.Key)
                     .ToArray())
        {
            lastSelectedRuleIdsByAvatarProfileId.Remove(profileId);
        }
    }

    private void ForgetRememberedRules(IEnumerable<TriggerRule> rules)
    {
        foreach (var rule in rules.ToArray())
        {
            ForgetRememberedRule(rule);
        }
    }

    private void SwitchRuleView(RuleListView targetView, AvatarTriggerProfile? profile, TriggerRule? rule)
    {
        activeRuleListView = targetView;

        RaisePropertyChanged(nameof(IsViewingAvatarTriggers));
        RaisePropertyChanged(nameof(IsViewingMasterAvatar));
        RaisePropertyChanged(nameof(IsViewingMovementRedeems));
        RaisePropertyChanged(nameof(IsViewingPowerUps));
        RaisePropertyChanged(nameof(IsViewingAvatarScaling));

        isSwitchingRuleView = true;
        try
        {
            try
            {
                SelectedRule = rule;
            }
            catch (NullReferenceException)
            {
            }

            SelectedAvatarProfile = profile;
            if (targetView != RuleListView.MovementRedeems)
            {
                SelectedMovementRedeemSet = null;
            }

            if (targetView != RuleListView.AvatarScaling)
            {
                SelectedAvatarScaleSet = null;
                SelectedAvatarScaleRule = null;
            }

            if (targetView != RuleListView.PowerUps)
            {
                SelectedPowerUpRule = null;
            }
        }
        finally
        {
            isSwitchingRuleView = false;
        }

        RaiseRuleSelectionStateProperties();
        RefreshSpecialRuleLockoutOptions();
        RefreshAvailableActionTypes();
        RefreshVrChatAvatarSelectionOptions();
        if (SelectedRule?.ActionType == OscActionType.AvatarParameter)
        {
            RefreshAvatarParameterOptions();
            _ = EnsureSelectedAvatarParameterCacheLoadedAsync();
        }
        RefreshRuleCommandStates();
    }

    private void WireSettings(AppSettings appSettings)
    {
        appSettings.PropertyChanged += SettingsChanged;
        appSettings.Broadcaster.PropertyChanged += SettingsChanged;
        appSettings.Bot.PropertyChanged += SettingsChanged;
        appSettings.VrChat.PropertyChanged += SettingsChanged;
        appSettings.WorldCommandBlacklist.PropertyChanged += SettingsChanged;
        appSettings.AvatarProfiles.CollectionChanged += AvatarProfilesCollectionChanged;
        appSettings.MovementRedeemSets.CollectionChanged += MovementRedeemSetsCollectionChanged;
        appSettings.GlobalOverrideRules.CollectionChanged += GlobalOverrideRulesCollectionChanged;
        appSettings.UniversalTriggers.CollectionChanged += UniversalTriggersCollectionChanged;
        appSettings.AvatarScaleSets.CollectionChanged += AvatarScaleSetsCollectionChanged;
        appSettings.AvatarScaleMasterReward.PropertyChanged += AvatarScaleMasterRewardChanged;
        appSettings.PowerUpRules.CollectionChanged += PowerUpRulesCollectionChanged;
        appSettings.AvatarScaleSafety.PropertyChanged += AvatarScaleSafetyChanged;

        foreach (var profile in appSettings.AvatarProfiles)
        {
            WireAvatarProfile(profile);
        }

        foreach (var set in appSettings.MovementRedeemSets)
        {
            WireMovementRedeemSet(set);
        }

        foreach (var rule in appSettings.GlobalOverrideRules)
        {
            rule.PropertyChanged += RuleChanged;
        }

        foreach (var trigger in appSettings.UniversalTriggers)
        {
            WireUniversalTrigger(trigger);
        }

        foreach (var scaleSet in appSettings.AvatarScaleSets)
        {
            WireAvatarScaleSet(scaleSet);
        }

        foreach (var powerUpRule in appSettings.PowerUpRules)
        {
            WirePowerUpRule(powerUpRule);
        }
    }

    private void UnwireSettings(AppSettings appSettings)
    {
        appSettings.PropertyChanged -= SettingsChanged;
        appSettings.Broadcaster.PropertyChanged -= SettingsChanged;
        appSettings.Bot.PropertyChanged -= SettingsChanged;
        appSettings.VrChat.PropertyChanged -= SettingsChanged;
        appSettings.WorldCommandBlacklist.PropertyChanged -= SettingsChanged;
        appSettings.AvatarProfiles.CollectionChanged -= AvatarProfilesCollectionChanged;
        appSettings.MovementRedeemSets.CollectionChanged -= MovementRedeemSetsCollectionChanged;
        appSettings.GlobalOverrideRules.CollectionChanged -= GlobalOverrideRulesCollectionChanged;
        appSettings.UniversalTriggers.CollectionChanged -= UniversalTriggersCollectionChanged;
        appSettings.AvatarScaleSets.CollectionChanged -= AvatarScaleSetsCollectionChanged;
        appSettings.AvatarScaleMasterReward.PropertyChanged -= AvatarScaleMasterRewardChanged;
        appSettings.PowerUpRules.CollectionChanged -= PowerUpRulesCollectionChanged;
        appSettings.AvatarScaleSafety.PropertyChanged -= AvatarScaleSafetyChanged;

        foreach (var profile in appSettings.AvatarProfiles)
        {
            UnwireAvatarProfile(profile);
        }

        foreach (var set in appSettings.MovementRedeemSets)
        {
            UnwireMovementRedeemSet(set);
        }

        foreach (var rule in appSettings.GlobalOverrideRules)
        {
            rule.PropertyChanged -= RuleChanged;
        }

        foreach (var trigger in appSettings.UniversalTriggers)
        {
            UnwireUniversalTrigger(trigger);
        }

        foreach (var scaleSet in appSettings.AvatarScaleSets)
        {
            UnwireAvatarScaleSet(scaleSet);
        }

        foreach (var powerUpRule in appSettings.PowerUpRules)
        {
            UnwirePowerUpRule(powerUpRule);
        }
    }

    private void ReplaceSettings(AppSettings appSettings)
    {
        if (ReferenceEquals(Settings, appSettings))
        {
            return;
        }

        if (appSettings.Theme == AppTheme.Custom && !appSettings.CustomTheme.IsInitialized)
        {
            appSettings.CustomTheme = ThemeManager.CreateSeededCustomTheme(AppTheme.VoidCrystal);
        }

        UnwireSettings(Settings);
        Settings = appSettings;
        WireSettings(appSettings);
        ThemeManager.UpdateTheme(Settings.Theme, Settings.CustomTheme);
        universalTriggersGroupedView = null;
        RaisePropertyChanged(nameof(UniversalTriggersGroupedView));
        RaisePropertyChanged(nameof(MovementRedeemSets));
        RaisePropertyChanged(nameof(AvatarScaleSets));
        RaisePropertyChanged(nameof(AvatarScaleRules));
        RaisePropertyChanged(nameof(SelectedLanguageOption));
        RaisePropertyChanged(nameof(IsLanguageRestartNoticeVisible));
        RaisePropertyChanged(nameof(LanguageRestartNoticeText));
        RaiseUniversalTriggerGroupProperties();
        RaiseBuiltInCommandStateProperties();
    }

    private void AvatarProfilesCollectionChanged(object? sender, NotifyCollectionChangedEventArgs e)
    {
        if (e.NewItems is not null)
        {
            foreach (AvatarTriggerProfile profile in e.NewItems)
            {
                WireAvatarProfile(profile);
            }
        }

        if (e.OldItems is not null)
        {
            foreach (AvatarTriggerProfile profile in e.OldItems)
            {
                UnwireAvatarProfile(profile);
                lastSelectedRuleIdsByAvatarProfileId.Remove(profile.Id);
                if (lastSelectedAvatarProfileId == profile.Id)
                {
                    lastSelectedAvatarProfileId = Guid.Empty;
                }
            }
        }

        NormalizeMasterAvatarProfiles();
        RefreshAvatarRuleProfilesList();
        if (IsViewingMasterAvatar)
        {
            SelectedAvatarProfile = MasterAvatarProfile;
            ApplyMasterAvatarDefaults();
        }
        else if (IsViewingAvatarTriggers && SelectedAvatarProfile is null)
        {
            var rememberedProfile = GetRememberedAvatarRuleProfile();
            if (rememberedProfile is not null)
            {
                SwitchRuleView(
                    RuleListView.AvatarTriggers,
                    rememberedProfile,
                    GetRememberedRuleForProfile(rememberedProfile));
            }
        }
        else if (SelectedAvatarProfile is not null && SelectedAvatarProfile.IsMasterProfile)
        {
            var rememberedProfile = GetRememberedAvatarRuleProfile();
            SwitchRuleView(
                RuleListView.AvatarTriggers,
                rememberedProfile,
                rememberedProfile is null ? null : GetRememberedRuleForProfile(rememberedProfile));
        }

        QueueSave();
        QueueBridgeRefresh();
        UpdateAvatarProfileActivityStates();
        RaiseRuleSelectionStateProperties();
        RefreshRuleCommandStates();
        QueueManagedRewardSync();
    }

    private void GlobalOverrideRulesCollectionChanged(object? sender, NotifyCollectionChangedEventArgs e)
    {
        if (e.NewItems is not null)
        {
            foreach (TriggerRule rule in e.NewItems)
            {
                rule.PropertyChanged += RuleChanged;
            }
        }

        if (e.OldItems is not null)
        {
            foreach (TriggerRule rule in e.OldItems)
            {
                rule.PropertyChanged -= RuleChanged;
            }
        }

        QueueSave();
        QueueBridgeRefresh();
        RaiseSupporterRuleGroupProperties();
        RefreshRuleCommandStates();
        QueueManagedRewardSync();
    }

    private void MovementRedeemSetsCollectionChanged(object? sender, NotifyCollectionChangedEventArgs e)
    {
        if (e.NewItems is not null)
        {
            foreach (MovementRedeemSet set in e.NewItems)
            {
                WireMovementRedeemSet(set);
            }
        }

        if (e.OldItems is not null)
        {
            var removedRules = new List<TriggerRule>();
            foreach (MovementRedeemSet set in e.OldItems)
            {
                UnwireMovementRedeemSet(set);
                removedRules.AddRange(set.MovementRules);
            }

            RetireManagedRewards(removedRules);
        }

        SyncLegacyGlobalMovementRules();
        RaisePropertyChanged(nameof(MovementRedeemSets));
        QueueSave();
        QueueBridgeRefresh();
        RefreshRuleCommandStates();
        QueueManagedRewardSync();
    }

    private void WireMovementRedeemSet(MovementRedeemSet set)
    {
        set.PropertyChanged += MovementRedeemSetChanged;
        set.MovementRules.CollectionChanged += MovementRedeemSetRulesCollectionChanged;
        foreach (var rule in set.MovementRules)
        {
            ApplyMovementRuleDefaults(rule);
            rule.PropertyChanged += RuleChanged;
        }
    }

    private void UnwireMovementRedeemSet(MovementRedeemSet set)
    {
        set.PropertyChanged -= MovementRedeemSetChanged;
        set.MovementRules.CollectionChanged -= MovementRedeemSetRulesCollectionChanged;
        foreach (var rule in set.MovementRules)
        {
            rule.PropertyChanged -= RuleChanged;
        }
    }

    private void MovementRedeemSetChanged(object? sender, PropertyChangedEventArgs e)
    {
        QueueSave();
        RaisePropertyChanged(nameof(MovementRedeemSets));
    }

    private void MovementRedeemSetRulesCollectionChanged(object? sender, NotifyCollectionChangedEventArgs e)
    {
        if (e.NewItems is not null)
        {
            foreach (TriggerRule rule in e.NewItems)
            {
                ApplyMovementRuleDefaults(rule);
                rule.PropertyChanged += RuleChanged;
            }
        }

        if (e.OldItems is not null)
        {
            var removedRules = new List<TriggerRule>();
            foreach (TriggerRule rule in e.OldItems)
            {
                rule.PropertyChanged -= RuleChanged;
                removedRules.Add(rule);
            }

            RetireManagedRewards(removedRules);
        }

        SyncLegacyGlobalMovementRules();
        RaisePropertyChanged(nameof(MovementRedeemSets));
        QueueSave();
        QueueBridgeRefresh();
        RefreshRuleCommandStates();
        QueueManagedRewardSync();
    }

    private void UniversalTriggersCollectionChanged(object? sender, NotifyCollectionChangedEventArgs e)
    {
        if (e.NewItems is not null)
        {
            foreach (UniversalTriggerRule trigger in e.NewItems)
            {
                WireUniversalTrigger(trigger);
            }
        }

        if (e.OldItems is not null)
        {
            var removedTriggers = new List<UniversalTriggerRule>();
            foreach (UniversalTriggerRule trigger in e.OldItems)
            {
                UnwireUniversalTrigger(trigger);
                removedTriggers.Add(trigger);
            }

            RetireManagedRewards(removedTriggers);
        }

        RaiseUniversalTriggerGroupProperties();
        RaiseBuiltInCommandStateProperties();
        QueueSave();
        QueueBridgeRefresh();
        RefreshRuleCommandStates();
        QueueManagedRewardSync();
    }

    private void WireUniversalTrigger(UniversalTriggerRule trigger)
    {
        trigger.PropertyChanged += UniversalTriggerChanged;
        trigger.Actions.CollectionChanged += UniversalTriggerActionsCollectionChanged;
        foreach (var action in trigger.Actions)
        {
            action.PropertyChanged += UniversalTriggerActionChanged;
        }
    }

    private void UnwireUniversalTrigger(UniversalTriggerRule trigger)
    {
        trigger.PropertyChanged -= UniversalTriggerChanged;
        trigger.Actions.CollectionChanged -= UniversalTriggerActionsCollectionChanged;
        foreach (var action in trigger.Actions)
        {
            action.PropertyChanged -= UniversalTriggerActionChanged;
        }
    }

    private void UniversalTriggerActionsCollectionChanged(object? sender, NotifyCollectionChangedEventArgs e)
    {
        if (e.NewItems is not null)
        {
            foreach (UniversalTriggerAction action in e.NewItems)
            {
                action.PropertyChanged += UniversalTriggerActionChanged;
            }
        }

        if (e.OldItems is not null)
        {
            foreach (UniversalTriggerAction action in e.OldItems)
            {
                action.PropertyChanged -= UniversalTriggerActionChanged;
            }
        }

        if (sender is ObservableCollection<UniversalTriggerAction> actions)
        {
            FindUniversalTriggerForActions(actions)?.RefreshActionState();
        }

        QueueSave();
        QueueBridgeRefresh();
        QueueManagedRewardSync();
        RaiseUniversalTriggerGroupProperties();
    }

    private void UniversalTriggerChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (sender is UniversalTriggerRule trigger
            && e.PropertyName == nameof(UniversalTriggerRule.TriggerType)
            && trigger.TriggerType != UniversalTriggerType.ChannelPointReward
            && !string.IsNullOrWhiteSpace(trigger.RewardId))
        {
            RetireManagedRewards([trigger]);
            trigger.RewardId = string.Empty;
        }

        QueueSave();
        QueueBridgeRefresh();
        RaiseRuleSelectionStateProperties();
        RaiseUniversalTriggerGroupProperties();
        RaiseBuiltInCommandStateProperties();

        if (!isSynchronizingManagedRewards
            && ShouldSynchronizeManagedRewardsForUniversalTriggerChange(e.PropertyName))
        {
            QueueManagedRewardSync();
        }
    }

    private void UniversalTriggerActionChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (sender is UniversalTriggerAction action)
        {
            FindUniversalTriggerForAction(action)?.RefreshActionState();
        }

        QueueSave();
        QueueBridgeRefresh();
        RaiseUniversalTriggerGroupProperties();

        if (!isSynchronizingManagedRewards
            && ShouldSynchronizeManagedRewardsForUniversalTriggerActionChange(e.PropertyName))
        {
            QueueManagedRewardSync();
        }
    }

    private UniversalTriggerRule? FindUniversalTriggerForAction(UniversalTriggerAction action) =>
        Settings.UniversalTriggers.FirstOrDefault(trigger => trigger.Actions.Contains(action));

    private UniversalTriggerRule? FindUniversalTriggerForActions(ObservableCollection<UniversalTriggerAction> actions) =>
        Settings.UniversalTriggers.FirstOrDefault(trigger => ReferenceEquals(trigger.Actions, actions));

    private void AvatarScaleSetsCollectionChanged(object? sender, NotifyCollectionChangedEventArgs e)
    {
        if (e.NewItems is not null)
        {
            foreach (AvatarScaleSet set in e.NewItems)
            {
                WireAvatarScaleSet(set);
            }
        }

        if (e.OldItems is not null)
        {
            var removedRules = new List<AvatarScaleRule>();
            foreach (AvatarScaleSet set in e.OldItems)
            {
                UnwireAvatarScaleSet(set);
                removedRules.AddRange(set.ScaleRules);
                if (lastSelectedAvatarScaleSetId == set.Id)
                {
                    lastSelectedAvatarScaleSetId = Guid.Empty;
                }
            }

            RetireManagedRewards(removedRules);
        }

        if (IsViewingAvatarScaling && SelectedAvatarScaleSet is not null && !Settings.AvatarScaleSets.Contains(SelectedAvatarScaleSet))
        {
            SelectedAvatarScaleSet = GetRememberedAvatarScaleSet();
        }

        if (IsViewingAvatarScaling
            && SelectedAvatarScaleRule is not null
            && !GetAllAvatarScaleRules().Contains(SelectedAvatarScaleRule))
        {
            SelectedAvatarScaleRule = GetRememberedAvatarScaleRule();
        }

        RaisePropertyChanged(nameof(AvatarScaleSets));
        RaisePropertyChanged(nameof(AvatarScaleRules));
        QueueSave();
        QueueBridgeRefresh();
        RefreshRuleCommandStates();
        QueueManagedRewardSync();
    }

    private void WireAvatarScaleSet(AvatarScaleSet set)
    {
        set.PropertyChanged += AvatarScaleSetChanged;
        set.ScaleRules.CollectionChanged += AvatarScaleSetRulesCollectionChanged;
        foreach (var rule in set.ScaleRules)
        {
            rule.PropertyChanged += AvatarScaleRuleChanged;
        }
    }

    private void UnwireAvatarScaleSet(AvatarScaleSet set)
    {
        set.PropertyChanged -= AvatarScaleSetChanged;
        set.ScaleRules.CollectionChanged -= AvatarScaleSetRulesCollectionChanged;
        foreach (var rule in set.ScaleRules)
        {
            rule.PropertyChanged -= AvatarScaleRuleChanged;
        }
    }

    private void AvatarScaleSetChanged(object? sender, PropertyChangedEventArgs e)
    {
        QueueSave();
        RaisePropertyChanged(nameof(AvatarScaleSets));
        RaisePropertyChanged(nameof(AvatarScaleRules));
    }

    private void AvatarScaleSetRulesCollectionChanged(object? sender, NotifyCollectionChangedEventArgs e)
    {
        if (e.NewItems is not null)
        {
            foreach (AvatarScaleRule rule in e.NewItems)
            {
                rule.PropertyChanged += AvatarScaleRuleChanged;
            }
        }

        if (e.OldItems is not null)
        {
            var removedRules = new List<AvatarScaleRule>();
            foreach (AvatarScaleRule rule in e.OldItems)
            {
                rule.PropertyChanged -= AvatarScaleRuleChanged;
                removedRules.Add(rule);
                RemoveAvatarScaleRuleLockoutReferencesToRule(rule.Id);
                if (lastSelectedAvatarScaleRuleId == rule.Id)
                {
                    lastSelectedAvatarScaleRuleId = Guid.Empty;
                }
            }

            RetireManagedRewards(removedRules);
        }

        if (IsViewingAvatarScaling
            && SelectedAvatarScaleRule is not null
            && SelectedAvatarScaleSet?.ScaleRules.Contains(SelectedAvatarScaleRule) != true)
        {
            SelectedAvatarScaleRule = GetRememberedAvatarScaleRule();
        }

        RaisePropertyChanged(nameof(AvatarScaleSets));
        RaisePropertyChanged(nameof(AvatarScaleRules));
        RaisePropertyChanged(nameof(AvatarScaleRuleLockoutSummaryText));
        QueueSave();
        QueueBridgeRefresh();
        RefreshRuleCommandStates();
        QueueManagedRewardSync();
    }

    private void AvatarScaleRuleChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (sender is AvatarScaleRule rule
            && e.PropertyName == nameof(AvatarScaleRule.TriggerType)
            && rule.TriggerType != AvatarScaleTriggerType.ChannelPointReward
            && !string.IsNullOrWhiteSpace(rule.RewardId))
        {
            RetireManagedRewards([rule]);
            rule.RewardId = string.Empty;
        }

        QueueSave();
        QueueBridgeRefresh();
        RaisePropertyChanged(nameof(AvatarScaleRules));
        RaisePropertyChanged(nameof(AvatarScaleRuntimeStatusText));
        if (e.PropertyName == nameof(AvatarScaleRule.TriggerType))
        {
            RaisePropertyChanged(nameof(AvailableAvatarScaleTriggerTypesForSelectedRule));
        }

        if (e.PropertyName == nameof(AvatarScaleRule.TemporarilyDisabledScaleRuleIds)
            || e.PropertyName == nameof(AvatarScaleRule.HasScaleDisablePairings))
        {
            RaisePropertyChanged(nameof(AvatarScaleRuleLockoutSummaryText));
        }

        if (!isSynchronizingManagedRewards
            && ShouldSynchronizeManagedRewardsForAvatarScaleRuleChange(e.PropertyName))
        {
            QueueManagedRewardSync();
        }
    }

    private void PowerUpRulesCollectionChanged(object? sender, NotifyCollectionChangedEventArgs e)
    {
        if (e.NewItems is not null)
        {
            foreach (PowerUpRule rule in e.NewItems)
            {
                WirePowerUpRule(rule);
            }
        }

        if (e.OldItems is not null)
        {
            foreach (PowerUpRule rule in e.OldItems)
            {
                UnwirePowerUpRule(rule);
                if (lastSelectedPowerUpRuleId == rule.Id)
                {
                    lastSelectedPowerUpRuleId = Guid.Empty;
                }
            }
        }

        if (IsViewingPowerUps && SelectedPowerUpRule is not null && !Settings.PowerUpRules.Contains(SelectedPowerUpRule))
        {
            SelectedPowerUpRule = GetRememberedPowerUpRule();
        }

        RaisePropertyChanged(nameof(PowerUpRules));
        QueueSave();
        QueueBridgeRefresh();
        RefreshRuleCommandStates();
    }

    private void WirePowerUpRule(PowerUpRule rule)
    {
        rule.PropertyChanged += PowerUpRuleChanged;
        rule.ActionRule.PropertyChanged += PowerUpNestedTriggerActionChanged;
        rule.ScaleAction.PropertyChanged += PowerUpNestedScaleActionChanged;
    }

    private void UnwirePowerUpRule(PowerUpRule rule)
    {
        rule.PropertyChanged -= PowerUpRuleChanged;
        rule.ActionRule.PropertyChanged -= PowerUpNestedTriggerActionChanged;
        rule.ScaleAction.PropertyChanged -= PowerUpNestedScaleActionChanged;
    }

    private void PowerUpRuleChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (sender is PowerUpRule rule && ReferenceEquals(rule, SelectedPowerUpRule))
        {
            if (e.PropertyName == nameof(PowerUpRule.PowerUpId))
            {
                ApplySelectedPowerUpOption(rule);
            }

            if (e.PropertyName is nameof(PowerUpRule.AvatarScoped)
                or nameof(PowerUpRule.AvatarId)
                or nameof(PowerUpRule.AvatarName))
            {
                if (e.PropertyName == nameof(PowerUpRule.AvatarId))
                {
                    SyncPowerUpAvatarScopeLabel(rule);
                }

                RefreshVrChatAvatarSelectionOptions();
                RefreshAvatarParameterOptions();
                _ = EnsureSelectedAvatarParameterCacheLoadedAsync();
            }

            if (e.PropertyName == nameof(PowerUpRule.ActionKind)
                || e.PropertyName == nameof(PowerUpRule.ActionRule)
                || e.PropertyName == nameof(PowerUpRule.ScaleAction))
            {
                SelectedRule = rule.UsesTriggerAction ? rule.ActionRule : null;
                SelectedAvatarScaleRule = rule.UsesAvatarScaling ? rule.ScaleAction : null;
            }

            RaisePropertyChanged(nameof(PowerUpRuleStatusText));
            RaisePropertyChanged(nameof(PowerUpActionEditorHelpText));
        }

        RaisePropertyChanged(nameof(PowerUpRules));
        QueueSave();
        QueueBridgeRefresh();
        RefreshRuleCommandStates();
    }

    private void ApplySelectedPowerUpOption(PowerUpRule rule)
    {
        var powerUpId = rule.PowerUpId.Trim();
        if (string.IsNullOrWhiteSpace(powerUpId))
        {
            return;
        }

        var option = PowerUpOptions.FirstOrDefault(item =>
            string.Equals(item.Id, powerUpId, StringComparison.Ordinal));
        if (option is null || option.IsCatalogMissing || string.IsNullOrWhiteSpace(option.Id))
        {
            return;
        }

        rule.ApplyLinkedPowerUp(option.Id, option.Title, option.BitsCost, option.Prompt);
    }

    private void SyncPowerUpAvatarScopeLabel(PowerUpRule rule)
    {
        var avatarId = rule.AvatarId.Trim();
        if (string.IsNullOrWhiteSpace(avatarId))
        {
            if (!string.IsNullOrWhiteSpace(rule.AvatarName))
            {
                rule.AvatarName = string.Empty;
            }

            return;
        }

        var avatarName = ResolveVrChatAvatarName(avatarId);
        if (!string.IsNullOrWhiteSpace(avatarName)
            && !string.Equals(rule.AvatarName, avatarName, StringComparison.Ordinal))
        {
            rule.AvatarName = avatarName;
        }
    }

    private void PowerUpNestedTriggerActionChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (sender is TriggerRule actionRule)
        {
            var owner = Settings.PowerUpRules.FirstOrDefault(rule => ReferenceEquals(rule.ActionRule, actionRule));
            if (owner is not null)
            {
                owner.ActionRule.TriggerType = TwitchTriggerType.PowerUp;
                owner.ActionRule.RewardSyncMode = TwitchRewardSyncMode.LinkExisting;
                owner.ActionRule.ChannelPointRewardId = string.Empty;
                owner.ActionRule.ChannelPointRewardTitle = string.Empty;
                owner.ActionRule.ChatCommandEnabled = false;
                RaisePropertyChanged(nameof(PowerUpRules));
                RaisePropertyChanged(nameof(PowerUpRuleStatusText));

                if (ReferenceEquals(owner, SelectedPowerUpRule))
                {
                    RefreshSelectedPowerUpActionEditorState(actionRule, e.PropertyName);
                }
            }
        }

        QueueSave();
        QueueBridgeRefresh();
    }

    private void RefreshSelectedPowerUpActionEditorState(TriggerRule actionRule, string? propertyName)
    {
        if (!IsViewingPowerUps || !ReferenceEquals(actionRule, SelectedRule))
        {
            return;
        }

        if (actionRule.ActionType == OscActionType.AvatarChange)
        {
            if (propertyName == nameof(TriggerRule.AvatarChangeTargetId))
            {
                SyncVrChatAvatarRuleLabel(actionRule, false);
            }
            else if (propertyName == nameof(TriggerRule.AvatarChangeResetId))
            {
                SyncVrChatAvatarRuleLabel(actionRule, true);
            }
        }
        else if (actionRule.ActionType == OscActionType.AvatarRoulet
                 && propertyName == nameof(TriggerRule.AvatarRouletAvatarIds))
        {
            SyncVrChatAvatarRouletPoolLabels(actionRule);
        }

        if (actionRule.ActionType == OscActionType.SetTrigger
            && (SelectedSetTriggerAction is null || !actionRule.SetTriggerActions.Contains(SelectedSetTriggerAction)))
        {
            SelectedSetTriggerAction = actionRule.SetTriggerActions.FirstOrDefault();
        }

        RefreshVrChatAvatarSelectionOptions();
        RefreshAvailableActionTypes();
        RefreshAvatarParameterOptions();
        _ = EnsureSelectedAvatarParameterCacheLoadedAsync();
        OpenAvatarRouletPoolPickerCommand.NotifyCanExecuteChanged();
        OpenActiveFloatBoostRewardCommand.NotifyCanExecuteChanged();
        AddSetTriggerActionCommand.NotifyCanExecuteChanged();
        RemoveSelectedSetTriggerActionCommand.NotifyCanExecuteChanged();
        RefreshAvatarParameterPathCommandStates();
    }

    private void PowerUpNestedScaleActionChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (sender is AvatarScaleRule scaleRule)
        {
            scaleRule.TriggerType = AvatarScaleTriggerType.Bits;
            scaleRule.RewardId = string.Empty;
            scaleRule.RewardTitle = string.Empty;
            scaleRule.MinimumBits = 1;
            scaleRule.MaximumBits = int.MaxValue;
        }

        RaisePropertyChanged(nameof(PowerUpRules));
        QueueSave();
        QueueBridgeRefresh();
    }

    private void AvatarScaleMasterRewardChanged(object? sender, PropertyChangedEventArgs e)
    {
        QueueSave();
        QueueBridgeRefresh();
        RaisePropertyChanged(nameof(AvatarScaleMasterRewardStatusText));

        if (!isSynchronizingManagedRewards
            && AvatarScaleMasterRewardPropertiesRequiringManagedRewardSync.Contains(e.PropertyName ?? string.Empty))
        {
            QueueManagedRewardSync();
        }
    }

    private void AvatarScaleSafetyChanged(object? sender, PropertyChangedEventArgs e)
    {
        QueueSave();
        QueueBridgeRefresh();
        RaisePropertyChanged(nameof(AvatarScaleRuntimeStatusText));
        RaisePropertyChanged(nameof(AvatarScaleSets));
        RaisePropertyChanged(nameof(AvatarScaleRules));
        QueueManagedRewardSync();
    }

    private void HandleAvatarScaleStatusChanged()
    {
        RaisePropertyChanged(nameof(AvatarScaleRuntimeStatusText));
        RaisePropertyChanged(nameof(CurrentAvatarHeightMeters));

        if (!isInitialized || isShuttingDown)
        {
            return;
        }

        var status = bridgeCoordinator.GetAvatarScaleRuntimeStatus();
        RefreshCurrentAvatarMoreOftenDuringActiveScale(status);
        var shouldSync = false;
        var activeRuleIds = new HashSet<Guid>();
        foreach (var rule in GetAllAvatarScaleRules().Where(IsManagedAvatarScaleChannelPointRule))
        {
            activeRuleIds.Add(rule.Id);
            bool hadPreviousState;
            bool previousState;
            lock (avatarScaleLimitStateGate)
            {
                hadPreviousState = avatarScaleLimitInactiveStateByRuleId.TryGetValue(rule.Id, out previousState);
            }

            var hasLimitState = TryGetAvatarScaleRelativeLimitState(
                rule,
                status,
                hadPreviousState ? previousState : null,
                out var limitState);
            var isInactiveAtLimit = hasLimitState && limitState.IsAtLimit;
            if (!hadPreviousState
                || previousState != isInactiveAtLimit)
            {
                lock (avatarScaleLimitStateGate)
                {
                    avatarScaleLimitInactiveStateByRuleId[rule.Id] = isInactiveAtLimit;
                }

                if (hasLimitState)
                {
                    if (hadPreviousState || isInactiveAtLimit)
                    {
                        LogAvatarScaleLimitVisibilityChange(rule, limitState);
                    }

                    // First visible observations are still synced so a reward hidden by an earlier
                    // startup/settings pass can be shown again once the height leaves the limit.
                    shouldSync = true;
                }
            }
        }

        lock (avatarScaleLimitStateGate)
        {
            foreach (var removedRuleId in avatarScaleLimitInactiveStateByRuleId.Keys.Except(activeRuleIds).ToArray())
            {
                avatarScaleLimitInactiveStateByRuleId.Remove(removedRuleId);
            }
        }

        if (shouldSync)
        {
            QueueManagedRewardSync(
                (int)AvatarScaleLimitRewardSyncDebounce.TotalMilliseconds,
                ManagedRewardSyncReason.AvatarScaleStatus);
        }
    }

    private void LogAvatarScaleLimitVisibilityChange(AvatarScaleRule rule, AvatarScaleRelativeLimitState limitState)
    {
        var limitName = limitState.IsMinimumLimit ? "minimum" : "maximum";
        var movedDirection = limitState.IsMinimumLimit ? "above" : "below";
        var message = limitState.IsAtLimit
            ? $"Avatar scale reward '{rule.DisplayTitle}' is hidden on Twitch because current height {limitState.CurrentHeightMeters:0.###}m reached the effective {limitName} {limitState.EffectiveLimitMeters:0.###}m."
            : $"Avatar scale reward '{rule.DisplayTitle}' can show on Twitch again because current height {limitState.CurrentHeightMeters:0.###}m moved {movedDirection} the effective {limitName} {limitState.EffectiveLimitMeters:0.###}m.";

        AppendThrottledLog(
            $"avatar-scale-limit-visibility:{rule.Id}:{limitName}:{limitState.IsAtLimit}",
            message,
            ThrottledRewardSyncLogWindow);
    }

    private void HandleFloatLimitStatusChanged()
    {
        if (!isInitialized || isShuttingDown)
        {
            return;
        }

        var limitReachedRuleIds = bridgeCoordinator.GetActiveFloatLimitReachedRuleIds();
        var currentStates = limitReachedRuleIds
            .ToDictionary(id => id, id => (MaxReached: true, MinReached: true));

        var shouldSync = false;
        var activeRuleIds = new HashSet<Guid>();

        foreach (var rule in EnumerateAllRules().Where(r => r.UsesFloatHideOnLimit))
        {
            activeRuleIds.Add(rule.Id);
            var currentState = currentStates.TryGetValue(rule.Id, out var s)
                ? s
                : (MaxReached: false, MinReached: false);

            bool hadPreviousState;
            (bool PrevMax, bool PrevMin) previousState;
            lock (floatLimitStateGate)
            {
                hadPreviousState = floatLimitStateByRuleId.TryGetValue(rule.Id, out previousState);
            }

            if (!hadPreviousState
                || previousState.PrevMax != currentState.MaxReached
                || previousState.PrevMin != currentState.MinReached)
            {
                lock (floatLimitStateGate)
                {
                    floatLimitStateByRuleId[rule.Id] = currentState;
                }

                if (hadPreviousState || currentState.MaxReached || currentState.MinReached)
                {
                    LogFloatLimitVisibilityChange(rule, currentState);
                }

                shouldSync = true;
            }
        }

        lock (floatLimitStateGate)
        {
            foreach (var removedRuleId in floatLimitStateByRuleId.Keys.Except(activeRuleIds).ToArray())
            {
                floatLimitStateByRuleId.Remove(removedRuleId);
            }
        }

        if (shouldSync)
        {
            QueueManagedRewardSync(
                (int)AvatarScaleLimitRewardSyncDebounce.TotalMilliseconds,
                ManagedRewardSyncReason.FloatLimitStatus);
        }
    }

    private void LogFloatLimitVisibilityChange(TriggerRule rule, (bool MaxReached, bool MinReached) state)
    {
        var limitName = (rule.HideRewardWhenFloatMaxReached, rule.HideRewardWhenFloatMinReached) switch
        {
            (true, false) => "maximum",
            (false, true) => "minimum",
            _ => "configured limit",
        };
        var isHidden = state.MaxReached || state.MinReached;
        var message = isHidden
            ? $"Avatar set reward '{rule.DisplayTitle}' is hidden on Twitch because its float value reached the configured {limitName}."
            : $"Avatar set reward '{rule.DisplayTitle}' can show on Twitch again because its float value left the configured limit.";

        AppendThrottledLog(
            $"float-limit-visibility:{rule.Id}:{limitName}:{isHidden}",
            message,
            ThrottledRewardSyncLogWindow);
    }

    private void RefreshCurrentAvatarMoreOftenDuringActiveScale(AvatarScaleRuntimeStatus status)
    {
        if (!status.IsActive || !Settings.VrChat.IsConnected)
        {
            CancelAndDisposeQueuedCancellationSource(ref activeAvatarScaleLocalRefreshCancellation);
            return;
        }

        if (activeAvatarScaleLocalRefreshCancellation is not null)
        {
            return;
        }

        var refreshCancellation = new CancellationTokenSource();
        activeAvatarScaleLocalRefreshCancellation = refreshCancellation;
        var cancellationToken = refreshCancellation.Token;

        _ = Task.Run(async () =>
        {
            try
            {
                while (!cancellationToken.IsCancellationRequested)
                {
                    QueueCurrentVrChatLocalStateRefresh(0);
                    await Task.Delay(ActiveAvatarScaleLocalRefreshInterval, cancellationToken);

                    var currentStatus = bridgeCoordinator.GetAvatarScaleRuntimeStatus();
                    if (!currentStatus.IsActive)
                    {
                        break;
                    }
                }
            }
            catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
            {
            }
            finally
            {
                RunOnUi(() =>
                {
                    if (ReferenceEquals(activeAvatarScaleLocalRefreshCancellation, refreshCancellation))
                    {
                        activeAvatarScaleLocalRefreshCancellation = null;
                    }

                    refreshCancellation.Dispose();
                });
            }
        }, CancellationToken.None);
    }

    private void WireAvatarProfile(AvatarTriggerProfile profile)
    {
        profile.PropertyChanged += AvatarProfileChanged;
        profile.ChannelPointRules.CollectionChanged += AvatarProfileRulesCollectionChanged;
        profile.WardrobeOutfits.CollectionChanged += WardrobeOutfitsCollectionChanged;

        foreach (var rule in profile.ChannelPointRules)
        {
            rule.TriggerType = TwitchTriggerType.ChannelPoints;
            rule.PropertyChanged += RuleChanged;
        }

        foreach (var outfit in profile.WardrobeOutfits)
        {
            outfit.PropertyChanged += WardrobeOutfitPropertyChanged;
        }
    }

    private void UnwireAvatarProfile(AvatarTriggerProfile profile)
    {
        profile.PropertyChanged -= AvatarProfileChanged;
        profile.ChannelPointRules.CollectionChanged -= AvatarProfileRulesCollectionChanged;
        profile.WardrobeOutfits.CollectionChanged -= WardrobeOutfitsCollectionChanged;

        foreach (var rule in profile.ChannelPointRules)
        {
            rule.PropertyChanged -= RuleChanged;
        }

        foreach (var outfit in profile.WardrobeOutfits)
        {
            outfit.PropertyChanged -= WardrobeOutfitPropertyChanged;
        }
    }

    private void AvatarProfileRulesCollectionChanged(object? sender, NotifyCollectionChangedEventArgs e)
    {
        if (e.NewItems is not null)
        {
            foreach (TriggerRule rule in e.NewItems)
            {
                rule.TriggerType = TwitchTriggerType.ChannelPoints;
                rule.PropertyChanged += RuleChanged;
                if (MasterAvatarProfile is not null
                    && MasterAvatarProfile.ChannelPointRules.Contains(rule))
                {
                    ApplyMasterAvatarDefaultsToRule(rule);
                }
            }
        }

        if (e.OldItems is not null)
        {
            foreach (TriggerRule rule in e.OldItems)
            {
                rule.PropertyChanged -= RuleChanged;
            }
        }

        QueueSave();
        QueueBridgeRefresh();
        RefreshSpecialRuleLockoutOptions();
        RaiseAvatarRedeemGroupProperties();
        RefreshRuleCommandStates();
        QueueManagedRewardSync();
    }

    private void WardrobeOutfitsCollectionChanged(object? sender, NotifyCollectionChangedEventArgs e)
    {
        if (e.OldItems is not null)
        {
            foreach (WardrobeOutfit outfit in e.OldItems)
            {
                outfit.PropertyChanged -= WardrobeOutfitPropertyChanged;
            }
        }

        if (e.NewItems is not null)
        {
            foreach (WardrobeOutfit outfit in e.NewItems)
            {
                outfit.PropertyChanged += WardrobeOutfitPropertyChanged;
            }
        }

        QueueSave();
        QueueBridgeRefresh();
        QueueManagedRewardSync();
    }

    private void WardrobeOutfitPropertyChanged(object? sender, PropertyChangedEventArgs e)
    {
        QueueSave();

        if (ShouldRefreshBridgeForWardrobeOutfitChange(e.PropertyName))
        {
            QueueBridgeRefresh();
        }

        if (!isSynchronizingManagedRewards
            && ShouldSynchronizeManagedRewardsForWardrobeOutfitChange(e.PropertyName))
        {
            QueueManagedRewardSync();
        }
    }

    private void AvatarProfileChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (sender is not AvatarTriggerProfile profile)
        {
            return;
        }

        if (isRefreshingVrChatAvatarSelectionOptions
            && e.PropertyName == nameof(AvatarTriggerProfile.AvatarId))
        {
            return;
        }

        if (e.PropertyName == nameof(AvatarTriggerProfile.AvatarId))
        {
            var resolvedAvatarName = ResolveVrChatAvatarName(profile.AvatarId);
            if (!string.IsNullOrWhiteSpace(resolvedAvatarName))
            {
                profile.AvatarName = resolvedAvatarName;
            }

            if (profile.IsMasterProfile)
            {
                ApplyMasterAvatarDefaults(profile);
            }

            UpdateAvatarProfileActivityStates();
            RefreshVrChatAvatarSelectionOptions();
            _ = EnsureSelectedAvatarParameterCacheLoadedAsync();
        }
        else if (e.PropertyName == nameof(AvatarTriggerProfile.IsMasterProfile) && profile.IsMasterProfile)
        {
            foreach (var otherProfile in Settings.AvatarProfiles.Where(otherProfile => !ReferenceEquals(otherProfile, profile)))
            {
                otherProfile.IsMasterProfile = false;
            }

            RefreshAvatarRuleProfilesList();
            RefreshAvailableActionTypes();
            ApplyMasterAvatarDefaults(profile);

            if (IsViewingMasterAvatar)
            {
                SelectedAvatarProfile = MasterAvatarProfile;
                SelectedRule = GetRememberedMasterRule();
            }
            else if (IsViewingAvatarTriggers && SelectedAvatarProfile?.IsMasterProfile == true)
            {
                var rememberedProfile = GetRememberedAvatarRuleProfile();
                SelectedAvatarProfile = rememberedProfile;
                SelectedRule = rememberedProfile is null
                    ? null
                    : GetRememberedRuleForProfile(rememberedProfile);
            }
        }
        else if (e.PropertyName == nameof(AvatarTriggerProfile.IsMasterProfile))
        {
            RefreshAvatarRuleProfilesList();
        }
        else if (e.PropertyName == nameof(AvatarTriggerProfile.UseSharedNumberedOutfitReward))
        {
            if (!profile.UseSharedNumberedOutfitReward)
            {
                EnsureSeparateOutfitRewardTitles(profile);
            }

            RaiseAvatarRedeemGroupProperties();
        }

        if (ShouldSaveAvatarProfileChange(e.PropertyName))
        {
            QueueSave();
        }

        if (ShouldRefreshBridgeForAvatarProfileChange(e.PropertyName))
        {
            QueueBridgeRefresh();
        }

        RefreshSupporterAvatarScopeOptions();
        RaisePropertyChanged(nameof(AvatarRuleProfiles));
        RaisePropertyChanged(nameof(MasterAvatarProfile));
        RaisePropertyChanged(nameof(MasterAvatarRules));
        RaisePropertyChanged(nameof(MasterAvatarDisplayName));
        RaisePropertyChanged(nameof(UseCurrentAvatarButtonText));
        RaisePropertyChanged(nameof(SelectedAvatarSetupTitle));
        RaisePropertyChanged(nameof(SelectedAvatarNameFieldLabel));
        RaisePropertyChanged(nameof(SelectedAvatarPickerLabel));
        RaisePropertyChanged(nameof(MasterAvatarReturnText));
        RaisePropertyChanged(nameof(SelectedAvatarProfileStatusText));
        RaisePropertyChanged(nameof(ManagedChannelPointRewardHelpText));
        RaisePropertyChanged(nameof(IsSetTriggerMasterRewardEditorVisible));
        RaisePropertyChanged(nameof(SelectedSetTriggerUsesSharedNumberedReward));
        RaisePropertyChanged(nameof(RewardTestOverrideButtonText));
        RaisePropertyChanged(nameof(RewardTestOverrideHelpText));
        RaisePropertyChanged(nameof(IsRewardTestOverrideAvailable));

        if (!isSynchronizingManagedRewards
            && ShouldSynchronizeManagedRewardsForAvatarProfileChange(e.PropertyName))
        {
            QueueManagedRewardSync();
        }
    }

    private void RuleChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (isApplyingMasterAvatarDefaults)
        {
            if (sender is TriggerRule guardedRule
                && guardedRule.ActionType == OscActionType.AvatarChange
                && e.PropertyName is nameof(TriggerRule.AvatarChangeTargetId)
                    or nameof(TriggerRule.AvatarChangeResetId))
            {
                SyncVrChatAvatarRuleLabel(guardedRule, e.PropertyName == nameof(TriggerRule.AvatarChangeResetId));
            }

            return;
        }

        if (isRefreshingVrChatAvatarSelectionOptions
            && e.PropertyName is nameof(TriggerRule.AvatarChangeTargetId)
                or nameof(TriggerRule.AvatarChangeResetId))
        {
            return;
        }

        if (sender is TriggerRule rule)
        {
            if (e.PropertyName is nameof(TriggerRule.SupporterAvatarScopeLabel)
                or nameof(TriggerRule.HasSupporterAvatarScopeLabel))
            {
                return;
            }

            if (!isNormalizingChatCommandRules
                && e.PropertyName is nameof(TriggerRule.ChatCommandEnabled)
                    or nameof(TriggerRule.ChatCommandText))
            {
                NormalizeChatCommandFallbackRule(rule);
            }

            var owningAvatarProfile = GetOwningAvatarProfile(rule);

            if (owningAvatarProfile is not null && rule.TriggerType != TwitchTriggerType.ChannelPoints)
            {
                rule.TriggerType = TwitchTriggerType.ChannelPoints;
            }

            if (Settings.GlobalOverrideRules.Contains(rule)
                && rule.ActionType == OscActionType.PlayerMovement
                && rule.TriggerType != TwitchTriggerType.Bits)
            {
                rule.TriggerType = TwitchTriggerType.Bits;
            }

            if (Settings.GlobalOverrideRules.Contains(rule)
                && e.PropertyName == nameof(TriggerRule.ActionType)
                && rule.ActionType is OscActionType.AvatarChange or OscActionType.AvatarRoulet or OscActionType.PlayerMovement)
            {
                if (rule.SupporterAvatarProfileId != Guid.Empty)
                {
                    rule.SupporterAvatarProfileId = Guid.Empty;
                }

                if (!string.IsNullOrWhiteSpace(rule.SupporterAvatarId))
                {
                    rule.SupporterAvatarId = string.Empty;
                }

                if (!string.IsNullOrWhiteSpace(rule.SupporterAvatarName))
                {
                    rule.SupporterAvatarName = string.Empty;
                }
            }

            if (owningAvatarProfile is not null
                && e.PropertyName is nameof(TriggerRule.ActionType)
                    or nameof(TriggerRule.ParameterType)
                    or nameof(TriggerRule.SetTriggerActions))
            {
                RaiseAvatarRedeemGroupProperties();
            }

            if (owningAvatarProfile?.IsMasterProfile == true)
            {
                ApplyMasterAvatarDefaultsToRule(rule, owningAvatarProfile);
            }
            else if (owningAvatarProfile is not null)
            {
                ApplyAvatarProfileDefaultsToRule(rule);
                ApplySetTriggerMasterRewardDefaults(owningAvatarProfile);
            }

            if (rule.ActionType == OscActionType.AvatarChange)
            {
                if (e.PropertyName == nameof(TriggerRule.AvatarChangeTargetId))
                {
                    SyncVrChatAvatarRuleLabel(rule, false);
                }
                else if (e.PropertyName == nameof(TriggerRule.AvatarChangeResetId))
                {
                    SyncVrChatAvatarRuleLabel(rule, true);
                }
            }
            else if (rule.ActionType == OscActionType.AvatarRoulet
                     && e.PropertyName == nameof(TriggerRule.AvatarRouletAvatarIds))
            {
                SyncVrChatAvatarRouletPoolLabels(rule);
            }

            if (Settings.GlobalOverrideRules.Contains(rule)
                && !IsSupporterAvatarChangeOverride(rule)
                && e.PropertyName == nameof(TriggerRule.SupporterAvatarId)
                && !string.IsNullOrWhiteSpace(rule.SupporterAvatarId))
            {
                var supporterAvatarName = ResolveVrChatAvatarName(rule.SupporterAvatarId);
                if (!string.IsNullOrWhiteSpace(supporterAvatarName))
                {
                    rule.SupporterAvatarName = supporterAvatarName;
                }
            }

            if (GetOwningMovementRedeemSet(rule) is not null)
            {
                ApplyMovementRuleDefaults(rule);
            }
            else if (e.PropertyName == nameof(TriggerRule.ActionType)
                     && rule.ActionType == OscActionType.PlayerMovement
                     && !IsSupporterForceMovementOverride(rule))
            {
                RelocateRuleToGlobalMovementRules(rule);
                return;
            }

            if (ReferenceEquals(rule, SelectedRule)
                && e.PropertyName is nameof(TriggerRule.ActionType)
                    or nameof(TriggerRule.TriggerType)
                    or nameof(TriggerRule.AvatarChangeTargetId)
                    or nameof(TriggerRule.AvatarChangeResetId)
                    or nameof(TriggerRule.ParameterType)
                    or nameof(TriggerRule.ParameterName)
                    or nameof(TriggerRule.DurationSeconds)
                    or nameof(TriggerRule.SupporterAvatarId)
                    or nameof(TriggerRule.SupporterAvatarName)
                    or nameof(TriggerRule.SupporterAvatarProfileId)
                    or nameof(TriggerRule.SupporterKeywordText)
                    or nameof(TriggerRule.SharedRewardChoiceEnabled)
                    or nameof(TriggerRule.SetTriggerActions))
            {
                if (rule.ActionType == OscActionType.SetTrigger
                    && (SelectedSetTriggerAction is null || !rule.SetTriggerActions.Contains(SelectedSetTriggerAction)))
                {
                    SelectedSetTriggerAction = rule.SetTriggerActions.FirstOrDefault();
                }

                RefreshVrChatAvatarSelectionOptions();
                RefreshAvailableActionTypes();
                RefreshAvatarParameterOptions();
                RaisePropertyChanged(nameof(SelectedSetTriggerUsesSharedNumberedReward));
                OpenAvatarRouletPoolPickerCommand.NotifyCanExecuteChanged();
                OpenActiveFloatBoostRewardCommand.NotifyCanExecuteChanged();
                AddSetTriggerActionCommand.NotifyCanExecuteChanged();
                RemoveSelectedSetTriggerActionCommand.NotifyCanExecuteChanged();
                RefreshAvatarParameterPathCommandStates();
            }

            if (Settings.GlobalOverrideRules.Contains(rule)
                && e.PropertyName is nameof(TriggerRule.ActionType)
                    or nameof(TriggerRule.TriggerType)
                    or nameof(TriggerRule.SupporterAvatarId)
                    or nameof(TriggerRule.SupporterAvatarName)
                    or nameof(TriggerRule.SupporterAvatarProfileId)
                    or nameof(TriggerRule.SupporterKeywordText))
            {
                RaiseSupporterRuleGroupProperties();
            }

            if (ReferenceEquals(rule, SelectedRule)
                || e.PropertyName == nameof(TriggerRule.SpecialRulePairingMode)
                || e.PropertyName == nameof(TriggerRule.TemporarilyDisabledRuleIds)
                || RuleBelongsToAvatarProfile(rule))
            {
                RefreshSpecialRuleLockoutOptions();
            }
        }

        QueueSave();

        if (ShouldRefreshBridgeForRuleChange(e.PropertyName))
        {
            QueueBridgeRefresh();
        }

        RaisePropertyChanged(nameof(SelectedAvatarProfileStatusText));
        RaisePropertyChanged(nameof(ManagedChannelPointRewardHelpText));

        if (!isSynchronizingManagedRewards
            && sender is TriggerRule changedRule
            && (RuleBelongsToAvatarProfile(changedRule) || GetOwningMovementRedeemSet(changedRule) is not null)
            && ShouldSynchronizeManagedRewardsForRuleChange(e.PropertyName))
        {
            QueueManagedRewardSync();
        }
    }

    private void SettingsChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (isShuttingDown)
        {
            return;
        }

        UpdateAccountStatuses();
        RefreshCommandStates();
        RaisePropertyChanged(nameof(ChatboxOscRelayStatusText));
        RaisePropertyChanged(nameof(ChatCommandFallbackHelpText));
        if (sender is AppSettings && IsBuiltInCommandSettingsProperty(e.PropertyName))
        {
            RaiseBuiltInCommandStateProperties();
        }

        if (sender is WorldCommandBlacklistSettings)
        {
            worldCommandBlacklistService.Configure(Settings.WorldCommandBlacklist);
            WorldCommandBlacklistStatusText = T("Protected world guard settings changed. Checking the guard...");
            QueueWorldCommandBlacklistRefresh(force: true);
        }

        var saveDelayMilliseconds = 500;

        if (ReferenceEquals(sender, Settings.Broadcaster)
            && e.PropertyName == nameof(TwitchAccountSettings.Scopes)
            && BroadcasterCanManageRewards)
        {
            ClearBroadcasterManagedRewardsUnavailableForSession();
        }

        if (ReferenceEquals(sender, Settings.Broadcaster))
        {
            QueueLiveFeedbackHeartbeatEvaluation();
        }

        if (sender is AppSettings
            && e.PropertyName is nameof(AppSettings.Theme) or nameof(AppSettings.CustomTheme))
        {
            ThemeManager.UpdateTheme(Settings.Theme, Settings.CustomTheme);
            RaiseThemeStateChanged();
            if (e.PropertyName == nameof(AppSettings.Theme))
            {
                saveDelayMilliseconds = 0;
            }
        }

        if (sender is AppSettings
            && e.PropertyName == nameof(AppSettings.ChannelPointRewardTestModeEnabled))
        {
            RaiseRuleSelectionStateProperties();
            QueueManagedRewardSync(0, ManagedRewardSyncReason.TestMode);
        }

        if (sender is AppSettings
            && e.PropertyName == nameof(AppSettings.UseManagedRewardTitlePrefix))
        {
            QueueManagedRewardSync(0, ManagedRewardSyncReason.SettingsEdit);
        }

        if (sender is AppSettings
            && e.PropertyName == nameof(AppSettings.AvatarChangeCooldownOnlyModeEnabled))
        {
            RaiseRuleSelectionStateProperties();
            RaisePropertyChanged(nameof(MasterAvatarReturnText));
            RaisePropertyChanged(nameof(SelectedAvatarProfileStatusText));
            QueueBridgeRefresh();
            QueueManagedRewardSync(0);
        }

        if (sender is AppSettings
            && e.PropertyName is nameof(AppSettings.MasterAvatarSwapReturnId) or nameof(AppSettings.MasterAvatarSwapReturnName))
        {
            LoadMasterAvatarReturnImage();
            QueueBridgeRefresh();
            QueueManagedRewardSync(0);
        }

        if (sender is AppSettings
            && e.PropertyName == nameof(AppSettings.ChatTimestampFormat))
        {
            RefreshChatMessageTimestampDisplay();
            RaisePropertyChanged(nameof(SelectedChatTimestampFormatOption));
        }

        if (sender is AppSettings
            && e.PropertyName == nameof(AppSettings.EmergencyRedeemStopEnabled))
        {
            RaiseRuleSelectionStateProperties();
            RaisePropertyChanged(nameof(EmergencyRedeemStopButtonText));
            RaisePropertyChanged(nameof(EmergencyRedeemStopHelpText));
            RaisePropertyChanged(nameof(IsEmergencyRedeemStopEnabled));
            QueueManagedRewardSync(0, ManagedRewardSyncReason.EmergencyStop);
        }

        if (sender is AppSettings
            && e.PropertyName == nameof(AppSettings.DesktopModeInputLockEnabled))
        {
            RaisePropertyChanged(nameof(DesktopModeInputLockButtonText));
            RaisePropertyChanged(nameof(DesktopModeInputLockHelpText));
            RaisePropertyChanged(nameof(DesktopModeInputLockStatusText));
            RaisePropertyChanged(nameof(IsDesktopModeInputLockEnabled));
        }

        if (sender is AppSettings
            && e.PropertyName == nameof(AppSettings.LiveFeedbackHeartbeatEnabled))
        {
            saveDelayMilliseconds = 0;
            QueueLiveFeedbackHeartbeatEvaluation();
        }

        if (sender is AppSettings
            && e.PropertyName == nameof(AppSettings.BetaApplicationUpdatesEnabled))
        {
            saveDelayMilliseconds = 0;
        }

        if (sender is AppSettings
            && e.PropertyName == nameof(AppSettings.Language))
        {
            RaisePropertyChanged(nameof(SelectedLanguageOption));
            RaisePropertyChanged(nameof(IsLanguageRestartNoticeVisible));
            RaisePropertyChanged(nameof(LanguageRestartNoticeText));
            saveDelayMilliseconds = 0;
        }

        if (sender is AppSettings
            && e.PropertyName == nameof(AppSettings.EasterEggsEnabled))
        {
            saveDelayMilliseconds = 0;
        }

        QueueSave(saveDelayMilliseconds);

        if (sender is AppSettings
            && e.PropertyName is nameof(AppSettings.InterfaceOpacityPercent)
                or nameof(AppSettings.InterfaceOpacity)
                or nameof(AppSettings.ChatTextSize)
                or nameof(AppSettings.ChatOpacityPercent)
                or nameof(AppSettings.ChatOpacity)
                or nameof(AppSettings.ChatShowTimestamps)
                or nameof(AppSettings.ChatTimestampFormat)
                or nameof(AppSettings.ChatFontFamily)
                or nameof(AppSettings.ChatboxAlwaysOnTop)
                or nameof(AppSettings.ChatboxSettingsPanelOpen)
                or nameof(AppSettings.ChatboxOverlayMode)
                or nameof(AppSettings.ChatboxViewerSoundEnabled)
                or nameof(AppSettings.LiveFeedbackHeartbeatEnabled)
                or nameof(AppSettings.BetaApplicationUpdatesEnabled)
                or nameof(AppSettings.EasterEggsEnabled)
                or nameof(AppSettings.Language)
                or nameof(AppSettings.Theme)
                or nameof(AppSettings.CustomTheme))
        {
            if (e.PropertyName == nameof(AppSettings.InterfaceOpacityPercent))
            {
                RaisePropertyChanged(nameof(UiOpacityStatusText));
            }

            return;
        }

        if (sender is VrChatAccountSettings)
        {
            RaiseVrChatConnectionStateProperties();
            UpdateAvatarProfileActivityStates();
            RefreshVrChatAvatarSelectionOptions();
            RefreshAvatarParameterOptions();
            return;
        }

        if (ShouldRefreshBridgeForSettingsChange(sender, e.PropertyName))
        {
            QueueBridgeRefresh();
        }
    }

    internal void QueueSave(int delayMilliseconds = 500)
    {
        if (!isInitialized || isShuttingDown)
        {
            return;
        }

        saveDebounceCancellation?.Cancel();
        saveDebounceCancellation?.Dispose();
        saveDebounceCancellation = new CancellationTokenSource();
        var cancellationToken = saveDebounceCancellation.Token;

        _ = Task.Run(async () =>
        {
            try
            {
                if (delayMilliseconds > 0)
                {
                    await Task.Delay(delayMilliseconds, cancellationToken);
                }

                await settingsStore.SaveAsync(Settings, cancellationToken);
            }
            catch (OperationCanceledException)
            {
            }
            catch (Exception ex)
            {
                RunOnUi(() => AppendLog($"Could not save settings: {ex.Message}"));
            }
        }, CancellationToken.None);
    }

    private async Task RefreshWorldCommandBlacklistOnStartupAsync()
    {
        worldCommandBlacklistService.Configure(Settings.WorldCommandBlacklist);

        WorldCommandBlacklistStatusText = T("Checking protected world guard...");
        await RefreshWorldCommandBlacklistAsync(force: false, logResult: true, CancellationToken.None);
    }

    private async Task RefreshWorldCommandBlacklistManuallyAsync()
    {
        WorldCommandBlacklistStatusText = T("Checking protected world guard...");
        await RefreshWorldCommandBlacklistAsync(force: true, logResult: true, CancellationToken.None);
    }

    private void QueueWorldCommandBlacklistRefresh(int delayMilliseconds = 900, bool force = false)
    {
        if (!isInitialized || isShuttingDown)
        {
            return;
        }

        worldCommandBlacklistRefreshCancellation?.Cancel();
        worldCommandBlacklistRefreshCancellation?.Dispose();
        worldCommandBlacklistRefreshCancellation = new CancellationTokenSource();
        var cancellationToken = worldCommandBlacklistRefreshCancellation.Token;

        _ = Task.Run(async () =>
        {
            try
            {
                if (delayMilliseconds > 0)
                {
                    await Task.Delay(delayMilliseconds, cancellationToken);
                }

                await RefreshWorldCommandBlacklistAsync(force, logResult: false, cancellationToken);
            }
            catch (OperationCanceledException)
            {
            }
        }, CancellationToken.None);
    }

    private async Task RefreshWorldCommandBlacklistAsync(
        bool force,
        bool logResult,
        CancellationToken cancellationToken)
    {
        WorldCommandBlacklistRefreshResult result;
        try
        {
            result = await worldCommandBlacklistService.RefreshAsync(
                Settings.WorldCommandBlacklist,
                force,
                cancellationToken);
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            throw;
        }

        RunOnUi(() => ApplyWorldCommandBlacklistRefreshResult(result, logResult));
    }

    private void ApplyWorldCommandBlacklistRefreshResult(
        WorldCommandBlacklistRefreshResult result,
        bool logResult)
    {
        WorldCommandBlacklistStatusText = BuildWorldCommandBlacklistStatusText(result);
        if (logResult)
        {
            AppendLog(WorldCommandBlacklistStatusText);
        }
    }

    private static string BuildWorldCommandBlacklistStatusText(WorldCommandBlacklistRefreshResult result)
    {
        return result.Status switch
        {
            WorldCommandBlacklistRefreshStatus.Ready => TF("Protected world guard is ready: {0} world ID(s), {1} creator ID rule(s).", result.WorldEntryCount, result.CreatorEntryCount),
            WorldCommandBlacklistRefreshStatus.InvalidResponse => T("Protected world guard returned an invalid status. World sharing will stay protected."),
            WorldCommandBlacklistRefreshStatus.RequestFailed => T("Protected world guard could not be reached. World sharing will stay protected."),
            _ => T("Checking protected world guard...")
        };
    }

    internal async Task<ApplicationUpdateInfo?> GetPendingApplicationUpdateAsync(CancellationToken cancellationToken = default)
    {
        ApplicationUpdateCheckResult result;
        try
        {
            result = await applicationUpdateService.CheckForUpdateAsync(
                AppBuildIdentity,
                Settings.IgnoredUpdateVersion,
                Settings.IgnoredBetaUpdateBaseVersion,
                Settings.BetaApplicationUpdatesEnabled || AppBuildIdentity.Channel == ApplicationUpdateChannel.Beta,
                cancellationToken);
        }
        catch (OperationCanceledException)
        {
            return null;
        }

        return result.Status switch
        {
            ApplicationUpdateCheckStatus.UpdateAvailable => result.Update,
            ApplicationUpdateCheckStatus.ReleaseVersionUnreadable => LogAndReturnNoUpdate(T("Crystal Relay skipped the update check because the latest GitHub release version could not be read.")),
            ApplicationUpdateCheckStatus.RequestFailed => LogAndReturnNoUpdate(T("Crystal Relay could not check for updates right now.")),
            _ => null
        };
    }

    internal void IgnoreApplicationUpdate(string version)
    {
        var normalizedVersion = string.IsNullOrWhiteSpace(version) ? string.Empty : version.Trim();
        if (string.Equals(Settings.IgnoredUpdateVersion, normalizedVersion, StringComparison.Ordinal))
        {
            return;
        }

        Settings.IgnoredUpdateVersion = normalizedVersion;
        QueueSave(0);
    }

    internal void IgnoreBetaApplicationUpdatesUntilStable(string baseVersion)
    {
        var normalizedVersion = string.IsNullOrWhiteSpace(baseVersion) ? string.Empty : baseVersion.Trim();
        if (string.Equals(Settings.IgnoredBetaUpdateBaseVersion, normalizedVersion, StringComparison.Ordinal))
        {
            return;
        }

        Settings.IgnoredBetaUpdateBaseVersion = normalizedVersion;
        QueueSave(0);
    }

    private void QueueBridgeRefresh()
    {
        if (!isInitialized || isShuttingDown)
        {
            return;
        }

        bridgeRefreshCancellation?.Cancel();
        bridgeRefreshCancellation?.Dispose();
        bridgeRefreshCancellation = new CancellationTokenSource();
        var cancellationToken = bridgeRefreshCancellation.Token;

        _ = Task.Run(async () =>
        {
            try
            {
                await Task.Delay(900, cancellationToken);
                await bridgeRefreshGate.WaitAsync(cancellationToken);
                try
                {
                    await EnsureBridgeStateAsync(cancellationToken);
                }
                finally
                {
                    try
                    {
                        bridgeRefreshGate.Release();
                    }
                    catch (ObjectDisposedException)
                    {
                    }
                }
            }
            catch (OperationCanceledException)
            {
            }
            catch (Exception ex)
            {
                RunOnUi(() =>
                {
                    BridgeStatus = "Background bridge failed to refresh.";
                    AppendLog($"Could not refresh the background bridge: {ex.Message}");
                });
            }
        }, CancellationToken.None);
    }

    private async Task EnsureBridgeStateAsync(
        CancellationToken cancellationToken,
        bool allowOscOnly = false,
        bool forceDiscoveryRefresh = false)
    {
        await ReloadRuntimeConfigAsync();
        var configuration = BridgeRuntimeConfiguration.FromSettings(Settings, runtimeConfig, BuildLinkedRewardCooldownLookup());

        if (!HasRecoverableBroadcasterSession)
        {
            if (bridgeCoordinator.IsRunning)
            {
                await bridgeCoordinator.StopAsync();
            }

            var oscSessionWasActive = bridgeCoordinator.IsOscActive;
            var shouldForceDiscoveryRefresh = forceDiscoveryRefresh
                || !oscSessionWasActive
                || bridgeCoordinator.DiscoveryState == OscDiscoveryState.Lost;
            await bridgeCoordinator.StartOscOnlyAsync(configuration, cancellationToken);

            if (shouldForceDiscoveryRefresh)
            {
                await bridgeCoordinator.ForceOscRefreshAsync(cancellationToken);
            }

            RunOnUi(() =>
            {
                BridgeStatus = "OSCQuery is live. Twitch listener is waiting for broadcaster login.";
                OscStatusDetail = bridgeCoordinator.HasDiscoveredVrChat
                    ? T("VRChat is connected through OSCQuery.")
                    : T("OSCQuery receiver is online and searching for VRChat.");
            });
            return;
        }

        if (broadcasterReconnectRequired)
        {
            if (bridgeCoordinator.IsRunning)
            {
                await bridgeCoordinator.StopAsync();
            }

            var oscSessionWasActive = bridgeCoordinator.IsOscActive;
            var shouldForceDiscoveryRefresh = forceDiscoveryRefresh
                || !oscSessionWasActive
                || bridgeCoordinator.DiscoveryState == OscDiscoveryState.Lost;
            await bridgeCoordinator.StartOscOnlyAsync(configuration, cancellationToken);

            if (shouldForceDiscoveryRefresh)
            {
                await bridgeCoordinator.ForceOscRefreshAsync(cancellationToken);
            }

            RunOnUi(() =>
            {
                BridgeStatus = "Twitch listener needs reconnect.";
                OscStatusDetail = bridgeCoordinator.HasDiscoveredVrChat
                    ? T("OSCQuery is still connected to VRChat. Reconnect Twitch to restore the listener.")
                    : T("OSCQuery stayed online and is still searching for VRChat.");
                UpdateAccountStatuses();
            });
            return;
        }

        if (bridgeCoordinator.IsRunning)
        {
            if (bridgeCoordinator.CanApplyConfigurationWithoutRestart(configuration))
            {
                bridgeCoordinator.ApplyConfiguration(configuration);
                RunOnUi(() =>
                {
                    BridgeStatus = "Background bridge is live.";
                    OscStatusDetail = bridgeCoordinator.HasDiscoveredVrChat
                        ? T("VRChat is connected through OSCQuery.")
                        : T("OSCQuery receiver is online and searching for VRChat.");
                });
                return;
            }

            await bridgeCoordinator.StopAsync();
        }

        try
        {
            var oscSessionWasActive = bridgeCoordinator.IsOscActive;
            var shouldForceDiscoveryRefresh = forceDiscoveryRefresh
                || !oscSessionWasActive
                || bridgeCoordinator.DiscoveryState == OscDiscoveryState.Lost;
            await bridgeCoordinator.StartAsync(configuration);

            if (shouldForceDiscoveryRefresh)
            {
                await bridgeCoordinator.ForceOscRefreshAsync(cancellationToken);
            }

            RunOnUi(() =>
            {
                BridgeStatus = "Background bridge is live.";
                OscStatusDetail = bridgeCoordinator.HasDiscoveredVrChat
                    ? T("VRChat is connected through OSCQuery.")
                    : T("OSCQuery receiver is online and searching for VRChat.");
            });
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
        }
        catch (TwitchAccountReconnectRequiredException ex)
        {
            var oscFallbackStarted = false;
            string? oscFallbackError = null;

            if (ex.AccountRole == BridgeAccountRole.Broadcaster)
            {
                broadcasterReconnectRequired = true;
                QueueLiveFeedbackHeartbeatEvaluation();
            }
            else
            {
                botReconnectRequired = true;
            }

            if (!bridgeCoordinator.IsOscActive)
            {
                try
                {
                    await bridgeCoordinator.StartOscOnlyAsync(configuration, cancellationToken);
                    await bridgeCoordinator.ForceOscRefreshAsync(cancellationToken);
                    oscFallbackStarted = true;
                }
                catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
                {
                    return;
                }
                catch (Exception oscEx)
                {
                    oscFallbackError = oscEx.Message;
                }
            }

            RunOnUi(() =>
            {
                AppendLog($"{ex.AccountRole} Twitch login needs reconnecting. Crystal Relay did not reuse the rejected saved refresh token.");
                RecordSavedLoginRecoverySignal();
                UpdateAccountStatuses();

                if (oscFallbackStarted || bridgeCoordinator.IsOscActive)
                {
                    BridgeStatus = "Twitch listener needs reconnect.";
                    OscStatusDetail = bridgeCoordinator.HasDiscoveredVrChat
                        ? T("OSCQuery is still connected to VRChat. Reconnect Twitch to restore the listener.")
                        : T("OSCQuery stayed online and is still searching for VRChat.");

                    if (oscFallbackStarted)
                    {
                        AppendLog("Crystal Relay kept OSCQuery running so Test Rule can still work.");
                    }

                    return;
                }

                BridgeStatus = "Background bridge could not start.";
                OscStatusDetail = string.IsNullOrWhiteSpace(oscFallbackError)
                    ? T("OSCQuery could not start with the background bridge.")
                    : TF("OSCQuery fallback also failed: {0}", oscFallbackError);
                if (!string.IsNullOrWhiteSpace(oscFallbackError))
                {
                    AppendLog($"OSCQuery fallback failed: {oscFallbackError}");
                }
            });
        }
        catch (Exception ex)
        {
            var oscFallbackStarted = false;
            string? oscFallbackError = null;

            if (!bridgeCoordinator.IsOscActive)
            {
                try
                {
                    await bridgeCoordinator.StartOscOnlyAsync(configuration, cancellationToken);
                    await bridgeCoordinator.ForceOscRefreshAsync(cancellationToken);
                    oscFallbackStarted = true;
                }
                catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
                {
                    return;
                }
                catch (Exception oscEx)
                {
                    oscFallbackError = oscEx.Message;
                }
            }

            RunOnUi(() =>
            {
                AppendLog($"Could not start the Twitch bridge: {ex.Message}");

                if (oscFallbackStarted || bridgeCoordinator.IsOscActive)
                {
                    BridgeStatus = "Twitch listener needs attention.";
                    OscStatusDetail = bridgeCoordinator.HasDiscoveredVrChat
                        ? T("OSCQuery is still connected to VRChat. Twitch needs attention.")
                        : T("OSCQuery stayed online and is still searching for VRChat.");

                    if (oscFallbackStarted)
                    {
                        AppendLog("Crystal Relay kept OSCQuery running so Test Rule can still work.");
                    }

                    return;
                }

                BridgeStatus = "Background bridge could not start.";
                OscStatusDetail = string.IsNullOrWhiteSpace(oscFallbackError)
                    ? T("OSCQuery could not start with the background bridge.")
                    : TF("OSCQuery fallback also failed: {0}", oscFallbackError);

                if (!string.IsNullOrWhiteSpace(oscFallbackError))
                {
                    AppendLog($"OSCQuery fallback failed: {oscFallbackError}");
                }
            });
        }
    }

    private async Task RefreshOscConnectionAsync()
    {
        var shouldForceRefresh = ThemedDialogWindow.ShowYesNo(
            Application.Current?.MainWindow,
            SelectedTheme,
            T("Refresh OSCQuery"),
            T("Refreshing OSCQuery can make VRChat show its OSC reading popup again. Do you want Crystal Relay to force a new OSCQuery discovery right now?"),
            T("Refresh OSC"),
            T("Cancel"));

        if (!shouldForceRefresh)
        {
            return;
        }

        BridgeStatus = "Refreshing OSCQuery connection to VRChat...";
        OscBridgeSummary = T("Refreshing OSC connection...");
        OscStatusDetail = T("Refreshing the OSCQuery connection to VRChat.");

        await bridgeRefreshGate.WaitAsync();
        try
        {
            var oscWasActive = bridgeCoordinator.IsOscActive;
            await EnsureBridgeStateAsync(
                CancellationToken.None,
                allowOscOnly: true,
                forceDiscoveryRefresh: !oscWasActive);
            if (oscWasActive)
            {
                await bridgeCoordinator.ForceOscRefreshAsync(CancellationToken.None);
            }

            BridgeStatus = "OSCQuery refresh sent. Looking for VRChat if needed.";
            OscStatusDetail = bridgeCoordinator.HasDiscoveredVrChat
                ? T("VRChat is connected through OSCQuery.")
                : T("OSCQuery refresh sent. Crystal Relay is searching for VRChat.");
        }
        catch (Exception ex)
        {
            BridgeStatus = "OSC refresh failed.";
            OscStatusDetail = TF("OSCQuery refresh failed: {0}", ex.Message);
            AppendLog($"Could not refresh the OSC connection: {ex.Message}");
        }
        finally
        {
            bridgeRefreshGate.Release();
        }
    }

    private async Task TestSelectedRuleAsync()
    {
        if (SelectedRule is null)
        {
            return;
        }

        await TestRuleAsync(SelectedRule);
    }

    public void TestMovementRule(TriggerRule rule)
    {
        if (rule is null) return;
        bridgeCoordinator.QuickTestRule(rule);
    }

    public async Task TestRuleAsync(TriggerRule rule)
    {
        if (rule is null) return;

        await ReloadRuntimeConfigAsync();

        await bridgeRefreshGate.WaitAsync();
        try
        {
            await EnsureBridgeStateAsync(CancellationToken.None, allowOscOnly: true);

            var (isGlobalOverride, profile) = ResolveRuleRuntimeContext(rule);
            var rouletteProfile = Settings.AvatarRouletteProfiles.FirstOrDefault(candidate => candidate.Triggers.Contains(rule));
            var ruleSnapshot = BridgeRuntimeConfiguration.CreateManualTestSnapshot(rule, isGlobalOverride, profile, rouletteProfile);
            await bridgeCoordinator.SendTestRuleAsync(ruleSnapshot, CancellationToken.None);

            BridgeStatus = $"Sent test for '{ruleSnapshot.Name}'.";
        }
        catch (Exception ex)
        {
            BridgeStatus = "Rule test did not run.";
            AppendLog($"Could not test the selected rule: {ex.Message}");
        }
        finally
        {
            bridgeRefreshGate.Release();
        }
    }

    private async Task SimulateTestModeBitsAsync()
    {
        await RunTestModeSimulationAsync(
            () => bridgeCoordinator.SimulateBitsAsync(TestModeBitsAmount, TestModeBitsMessage, CancellationToken.None),
            TF("Simulated {0:N0} Bits.", Math.Max(1, TestModeBitsAmount)),
            T("Could not simulate Bits"));
    }

    private async Task SimulateTestModeSubscriptionAsync()
    {
        await RunTestModeSimulationAsync(
            () => bridgeCoordinator.SimulateSubscriptionAsync(
                TestModeSubscriptionCount,
                TestModeSubscriptionTier,
                TestModeSubscriptionIsGift,
                CancellationToken.None),
            TestModeSubscriptionIsGift
                ? TF("Simulated {0:N0} gift sub(s).", Math.Max(1, TestModeSubscriptionCount))
                : TF("Simulated {0:N0} subscription(s).", Math.Max(1, TestModeSubscriptionCount)),
            T("Could not simulate subscriptions"));
    }

    private async Task SimulateTestModeCashPaymentAsync()
    {
        await RunTestModeSimulationAsync(
            () => bridgeCoordinator.SimulateCashPaymentAsync(
                TestModeCashProvider,
                TestModeCashAmount,
                TestModeCashCurrencyCode,
                TestModeCashMessage,
                CancellationToken.None),
            TF("Simulated cash payment of {0:0.##} {1}.", Math.Max(0m, TestModeCashAmount), TestModeCashCurrencyCode),
            T("Could not simulate cash payment"));
    }

    private async Task RunTestModeSimulationAsync(
        Func<Task> simulateAsync,
        string successStatus,
        string failurePrefix)
    {
        await ReloadRuntimeConfigAsync();

        await bridgeRefreshGate.WaitAsync();
        try
        {
            await EnsureBridgeStateAsync(CancellationToken.None, allowOscOnly: true);
            await simulateAsync();

            BridgeStatus = successStatus;
            TestModeSimulationStatusText = successStatus;
        }
        catch (Exception ex)
        {
            BridgeStatus = T("Test simulation did not run.");
            TestModeSimulationStatusText = TF("{0}: {1}", failurePrefix, ex.Message);
            AppendLog($"{failurePrefix}: {ex.Message}");
        }
        finally
        {
            bridgeRefreshGate.Release();
        }
    }

    private bool CanTestSelectedAvatarScaleRule()
    {
        return SelectedAvatarScaleRule is not null
            || SelectedAvatarScaleSet?.ScaleRules.Count > 0;
    }

    private void StartSelectedAvatarScaleRuleTest()
    {
        _ = TestSelectedAvatarScaleRuleAsync();
    }

    public void SetSelectedAvatarScaleMode(AvatarScaleMode mode)
    {
        if (SelectedAvatarScaleRule is { } rule)
        {
            rule.ScaleMode = mode;
        }
    }

    private AvatarScaleRule? ResolveAvatarScaleRuleForTest()
    {
        if (SelectedAvatarScaleRule is not null
            && (SelectedAvatarScaleSet is null || SelectedAvatarScaleSet.ScaleRules.Contains(SelectedAvatarScaleRule)))
        {
            return SelectedAvatarScaleRule;
        }

        var fallbackRule = SelectedAvatarScaleSet?.ScaleRules.FirstOrDefault();
        if (fallbackRule is not null)
        {
            SelectedAvatarScaleRule = fallbackRule;
        }

        return fallbackRule;
    }

    private async Task TestSelectedAvatarScaleRuleAsync()
    {
        var ruleToTest = ResolveAvatarScaleRuleForTest();
        if (ruleToTest is null)
        {
            return;
        }

        try
        {
            await ReloadRuntimeConfigAsync();

            AvatarScaleRuleSnapshot ruleSnapshot;
            await bridgeRefreshGate.WaitAsync();
            try
            {
                await EnsureBridgeStateAsync(CancellationToken.None, allowOscOnly: true);
                ruleSnapshot = BridgeRuntimeConfiguration.CreateManualTestSnapshot(ruleToTest, Settings.AvatarScaleSafety);
            }
            finally
            {
                bridgeRefreshGate.Release();
            }

            await bridgeCoordinator.SendTestAvatarScaleRuleAsync(ruleSnapshot, CancellationToken.None);

            BridgeStatus = $"Sent avatar scale test for '{ruleSnapshot.Name}'.";
            RaisePropertyChanged(nameof(AvatarScaleRuntimeStatusText));
        }
        catch (Exception ex)
        {
            BridgeStatus = "Avatar scale test did not run.";
            AppendLog($"Could not test the selected avatar scale redeem: {ex.Message}");
        }
    }

    private async Task TestSelectedPowerUpRuleAsync()
    {
        if (SelectedPowerUpRule is null)
        {
            return;
        }

        await ReloadRuntimeConfigAsync();

        await bridgeRefreshGate.WaitAsync();
        try
        {
            await EnsureBridgeStateAsync(CancellationToken.None, allowOscOnly: true);

            var ruleSnapshot = BridgeRuntimeConfiguration.CreateManualTestSnapshot(SelectedPowerUpRule, MasterAvatarProfile, Settings.AvatarScaleSafety);
            await bridgeCoordinator.SendTestPowerUpRuleAsync(ruleSnapshot, CancellationToken.None);

            BridgeStatus = $"Sent Power Up test for '{ruleSnapshot.Name}'.";
            RaisePropertyChanged(nameof(AvatarScaleRuntimeStatusText));
        }
        catch (Exception ex)
        {
            BridgeStatus = "Power Up test did not run.";
            AppendLog($"Could not test the selected Power Up rule: {ex.Message}");
        }
        finally
        {
            bridgeRefreshGate.Release();
        }
    }

    public async Task TestAvatarScaleRuleByCardAsync(AvatarScaleRule rule)
    {
        if (rule is null) return;

        try
        {
            await ReloadRuntimeConfigAsync();

            AvatarScaleRuleSnapshot ruleSnapshot;
            await bridgeRefreshGate.WaitAsync();
            try
            {
                await EnsureBridgeStateAsync(CancellationToken.None, allowOscOnly: true);
                ruleSnapshot = BridgeRuntimeConfiguration.CreateManualTestSnapshot(rule, Settings.AvatarScaleSafety);
            }
            finally
            {
                bridgeRefreshGate.Release();
            }

            await bridgeCoordinator.SendTestAvatarScaleRuleAsync(ruleSnapshot, CancellationToken.None);

            BridgeStatus = $"Sent avatar scale test for '{ruleSnapshot.Name}'.";
            RaisePropertyChanged(nameof(AvatarScaleRuntimeStatusText));
        }
        catch (Exception ex)
        {
            BridgeStatus = "Avatar scale test did not run.";
            AppendLog($"Could not test the avatar scale redeem: {ex.Message}");
        }
    }

    public async Task TestCashPaymentRuleByCardAsync(CashPaymentRule rule)
    {
        if (rule is null) return;

        try
        {
            await ReloadRuntimeConfigAsync();

            await bridgeRefreshGate.WaitAsync();
            try
            {
                await EnsureBridgeStateAsync(CancellationToken.None, allowOscOnly: true);

                var ruleSnapshot = BridgeRuntimeConfiguration.CreateManualTestSnapshot(rule, Settings.AvatarScaleSafety);
                await bridgeCoordinator.SendTestCashPaymentRuleAsync(ruleSnapshot, CancellationToken.None);

                BridgeStatus = $"Sent cash payment test for '{ruleSnapshot.Name}'.";
                RaisePropertyChanged(nameof(AvatarScaleRuntimeStatusText));
            }
            finally
            {
                bridgeRefreshGate.Release();
            }
        }
        catch (Exception ex)
        {
            BridgeStatus = "Cash payment test did not run.";
            AppendLog($"Could not test the cash payment rule: {ex.Message}");
        }
    }

    public async Task TestPowerUpRuleByCardAsync(PowerUpRule rule)
    {
        if (rule is null) return;

        try
        {
            await ReloadRuntimeConfigAsync();

            await bridgeRefreshGate.WaitAsync();
            try
            {
                await EnsureBridgeStateAsync(CancellationToken.None, allowOscOnly: true);

                var ruleSnapshot = BridgeRuntimeConfiguration.CreateManualTestSnapshot(rule, MasterAvatarProfile, Settings.AvatarScaleSafety);
                await bridgeCoordinator.SendTestPowerUpRuleAsync(ruleSnapshot, CancellationToken.None);

                BridgeStatus = $"Sent Power Up test for '{ruleSnapshot.Name}'.";
                RaisePropertyChanged(nameof(AvatarScaleRuntimeStatusText));
            }
            finally
            {
                bridgeRefreshGate.Release();
            }
        }
        catch (Exception ex)
        {
            BridgeStatus = "Power Up test did not run.";
            AppendLog($"Could not test the Power Up rule: {ex.Message}");
        }
    }

    private (bool IsGlobalOverride, AvatarTriggerProfile? Profile) ResolveRuleRuntimeContext(TriggerRule rule)
    {
        if (Settings.GlobalOverrideRules.Contains(rule))
        {
            var supporterProfile = rule.SupporterAvatarProfileId == Guid.Empty
                ? null
                : Settings.AvatarProfiles.FirstOrDefault(profile => profile.Id == rule.SupporterAvatarProfileId);
            return (true, supporterProfile);
        }

        if (GetOwningMovementRedeemSet(rule) is not null)
        {
            return (true, null);
        }

        var profile = Settings.AvatarProfiles.FirstOrDefault(candidate => candidate.ChannelPointRules.Contains(rule));
        return (false, profile);
    }

    private async Task RefreshTwitchRewardsAsync()
    {
        await QueueRewardRefreshAsync();
        await QueuePowerUpRefreshAsync();
    }

    public async Task RefreshPowerUpsAsync()
    {
        await QueuePowerUpRefreshAsync();
    }

    private async Task<ManagedRewardSyncOutcome> EnsureBroadcasterRewardManagementReadyAsync(
        string status,
        string logKey,
        bool clearRewardOptions,
        CancellationToken cancellationToken)
    {
        var accountSnapshot = await GetBroadcasterRewardAccountSnapshotAsync(cancellationToken);
        if (!HasRecoverableBroadcasterAccount(accountSnapshot))
        {
            return ManagedRewardSyncOutcome.Completed;
        }

        if ((string.IsNullOrWhiteSpace(accountSnapshot.AccessToken)
                && string.IsNullOrWhiteSpace(accountSnapshot.RefreshToken))
            || string.IsNullOrWhiteSpace(accountSnapshot.TwitchClientId))
        {
            SetUniversalManagedRewardSyncStatus(status);
            RunOnUi(() => AppendThrottledLog(
                $"{logKey}:incomplete",
                "Twitch reward refresh skipped because the broadcaster login is incomplete. Reconnect the broadcaster account once.",
                ThrottledRewardSyncLogWindow));
            return ManagedRewardSyncOutcome.BroadcasterTokenRefreshRequired;
        }

        try
        {
            var refreshedAccount = await ValidateOrRefreshBroadcasterAccountAsync(accountSnapshot, cancellationToken);
            if (!HasScope(refreshedAccount, TwitchScopes.RewardManagement))
            {
                ReportBroadcasterRewardManagementScopeMissing(
                    status,
                    $"{logKey}:missing-scope",
                    clearRewardOptions);
                return ManagedRewardSyncOutcome.BroadcasterRewardManagementScopeMissing;
            }

            await ApplyBroadcasterAccountRefreshAsync(refreshedAccount, cancellationToken);
            return ManagedRewardSyncOutcome.Completed;
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            throw;
        }
        catch (TwitchAccountReconnectRequiredException)
        {
            broadcasterReconnectRequired = true;
            UpdateAccountStatuses();
            QueueLiveFeedbackHeartbeatEvaluation();
            RecordSavedLoginRecoverySignal();
            SetUniversalManagedRewardSyncStatus(status);
            RunOnUi(() => AppendThrottledLog(
                $"{logKey}:reconnect",
                "Twitch reward refresh skipped because the broadcaster login needs reconnecting.",
                ThrottledRewardSyncLogWindow));
            return ManagedRewardSyncOutcome.BroadcasterTokenRefreshRequired;
        }
        catch (Exception ex)
        {
            SetUniversalManagedRewardSyncStatus(status);
            RunOnUi(() => AppendThrottledLog(
                $"{logKey}:token",
                $"Twitch reward refresh skipped because Crystal Relay could not verify the broadcaster login yet. {ex.Message}",
                ThrottledRewardSyncLogWindow));
            return ManagedRewardSyncOutcome.BroadcasterTokenRefreshRequired;
        }
    }

    private Task<BroadcasterRewardAccountSnapshot> GetBroadcasterRewardAccountSnapshotAsync(CancellationToken cancellationToken)
    {
        if (dispatcher.CheckAccess())
        {
            return Task.FromResult(CreateBroadcasterRewardAccountSnapshot());
        }

        return dispatcher.InvokeAsync(
            CreateBroadcasterRewardAccountSnapshot,
            DispatcherPriority.Normal,
            cancellationToken).Task;
    }

    private BroadcasterRewardAccountSnapshot CreateBroadcasterRewardAccountSnapshot()
    {
        var account = Settings.Broadcaster;
        return new BroadcasterRewardAccountSnapshot(
            account.IsConnected,
            account.AccessToken,
            account.RefreshToken,
            account.UserId,
            account.Login,
            account.DisplayName,
            account.ProfileImageUrl,
            account.AccessTokenExpiresAt,
            account.SessionRenewalDueAt,
            account.Scopes?.ToArray() ?? [],
            runtimeConfig.TwitchClientId);
    }

    private async Task<TwitchAccountSettings> ValidateOrRefreshBroadcasterAccountAsync(
        BroadcasterRewardAccountSnapshot account,
        CancellationToken cancellationToken)
    {
        var accessToken = account.AccessToken;
        var refreshToken = account.RefreshToken;
        var scopes = account.Scopes.ToArray();
        var expiresAt = account.AccessTokenExpiresAt;
        var refreshedThisAttempt = false;

        if (!string.IsNullOrWhiteSpace(refreshToken)
            && (string.IsNullOrWhiteSpace(accessToken)
                || expiresAt is { } existingExpiresAt
                    && existingExpiresAt <= DateTimeOffset.UtcNow.Add(TwitchAccessTokenRefreshLeadTime)))
        {
            TwitchApiClient.TokenExchangeResponse refreshedToken;
            try
            {
                refreshedToken = await RefreshBroadcasterAccessTokenForUiAsync(
                    account.TwitchClientId,
                    refreshToken,
                    cancellationToken);
            }
            catch (TwitchApiException ex) when (CanUseCachedBroadcasterToken(account.UserId, accessToken, expiresAt, ex))
            {
                return CreateBroadcasterSettingsFromSnapshot(account, accessToken, refreshToken, scopes, expiresAt);
            }

            accessToken = refreshedToken.AccessToken;
            refreshToken = string.IsNullOrWhiteSpace(refreshedToken.RefreshToken)
                ? refreshToken
                : refreshedToken.RefreshToken;
            scopes = refreshedToken.Scope.Count > 0 ? [.. refreshedToken.Scope] : scopes;
            expiresAt = DateTimeOffset.UtcNow.AddSeconds(refreshedToken.ExpiresIn);
            await PersistBroadcasterRefreshedTokenAsync(
                CreateBroadcasterSettingsFromSnapshot(account, accessToken, refreshToken, scopes, expiresAt),
                cancellationToken);
            refreshedThisAttempt = true;
        }

        TwitchApiClient.TokenValidationResponse? validation;
        try
        {
            validation = await twitchApiClient.ValidateTokenAsync(accessToken, cancellationToken);
        }
        catch (TwitchApiException ex) when (CanUseCachedBroadcasterToken(account.UserId, accessToken, expiresAt, ex))
        {
            return CreateBroadcasterSettingsFromSnapshot(account, accessToken, refreshToken, scopes, expiresAt);
        }

        if (validation is null)
        {
            if (string.IsNullOrWhiteSpace(refreshToken))
            {
                throw new InvalidOperationException("The saved broadcaster token expired and has no refresh token.");
            }

            if (refreshedThisAttempt)
            {
                throw new InvalidOperationException("The refreshed broadcaster token could not be validated.");
            }

            var refreshedToken = await RefreshBroadcasterAccessTokenForUiAsync(
                account.TwitchClientId,
                refreshToken,
                cancellationToken);
            accessToken = refreshedToken.AccessToken;
            refreshToken = string.IsNullOrWhiteSpace(refreshedToken.RefreshToken)
                ? refreshToken
                : refreshedToken.RefreshToken;
            scopes = refreshedToken.Scope.Count > 0 ? [.. refreshedToken.Scope] : scopes;
            expiresAt = DateTimeOffset.UtcNow.AddSeconds(refreshedToken.ExpiresIn);
            await PersistBroadcasterRefreshedTokenAsync(
                CreateBroadcasterSettingsFromSnapshot(account, accessToken, refreshToken, scopes, expiresAt),
                cancellationToken);

            try
            {
                validation = await twitchApiClient.ValidateTokenAsync(accessToken, cancellationToken)
                    ?? throw new InvalidOperationException("The refreshed broadcaster token could not be validated.");
            }
            catch (TwitchApiException ex) when (CanUseCachedBroadcasterToken(account.UserId, accessToken, expiresAt, ex))
            {
                return CreateBroadcasterSettingsFromSnapshot(account, accessToken, refreshToken, scopes, expiresAt);
            }
        }

        if (!string.Equals(validation.ClientId, account.TwitchClientId, StringComparison.Ordinal))
        {
            throw new InvalidOperationException("The saved broadcaster token belongs to a different Twitch app.");
        }

        var user = await twitchApiClient.GetUserAsync(
            accessToken,
            account.TwitchClientId,
            validation.UserId,
            cancellationToken);
        var validatedScopes = validation.Scopes.Count > 0 ? validation.Scopes.ToArray() : scopes;

        return new TwitchAccountSettings
        {
            AccessToken = accessToken,
            RefreshToken = refreshToken,
            UserId = validation.UserId,
            Login = user?.Login ?? validation.Login,
            DisplayName = user?.DisplayName ?? account.DisplayName ?? validation.Login,
            ProfileImageUrl = user?.ProfileImageUrl ?? account.ProfileImageUrl,
            AccessTokenExpiresAt = DateTimeOffset.UtcNow.AddSeconds(validation.ExpiresIn),
            SessionRenewalDueAt = account.SessionRenewalDueAt ?? DateTimeOffset.UtcNow.Add(TwitchPublicRefreshSessionWindow),
            Scopes = [.. validatedScopes]
        };
    }

    private static TwitchAccountSettings CreateBroadcasterSettingsFromSnapshot(
        BroadcasterRewardAccountSnapshot account,
        string accessToken,
        string refreshToken,
        IReadOnlyCollection<string> scopes,
        DateTimeOffset? expiresAt)
    {
        return new TwitchAccountSettings
        {
            AccessToken = accessToken,
            RefreshToken = refreshToken,
            UserId = account.UserId,
            Login = account.Login,
            DisplayName = account.DisplayName,
            ProfileImageUrl = account.ProfileImageUrl,
            AccessTokenExpiresAt = expiresAt,
            SessionRenewalDueAt = account.SessionRenewalDueAt ?? DateTimeOffset.UtcNow.Add(TwitchPublicRefreshSessionWindow),
            Scopes = [.. scopes]
        };
    }

    private async Task<TwitchApiClient.TokenExchangeResponse> RefreshBroadcasterAccessTokenForUiAsync(
        string clientId,
        string refreshToken,
        CancellationToken cancellationToken)
    {
        try
        {
            return await twitchApiClient.RefreshBroadcasterAccessTokenAsync(clientId, refreshToken, cancellationToken);
        }
        catch (TwitchApiException ex) when (ex.StatusCode is System.Net.HttpStatusCode.BadRequest or System.Net.HttpStatusCode.Unauthorized)
        {
            throw new TwitchAccountReconnectRequiredException(BridgeAccountRole.Broadcaster, ex);
        }
    }

    private async Task PersistBroadcasterRefreshedTokenAsync(
        TwitchAccountSettings refreshedAccount,
        CancellationToken cancellationToken)
    {
        if (dispatcher.CheckAccess())
        {
            PersistBroadcasterRefreshedToken(refreshedAccount);
            return;
        }

        await dispatcher.InvokeAsync(
            () => PersistBroadcasterRefreshedToken(refreshedAccount),
            DispatcherPriority.Normal,
            cancellationToken).Task;
    }

    private void PersistBroadcasterRefreshedToken(TwitchAccountSettings refreshedAccount)
    {
        var bridgeSensitiveChange = HasBridgeSensitiveTwitchAccountChanges(Settings.Broadcaster, refreshedAccount);
        Settings.Broadcaster.AccessToken = refreshedAccount.AccessToken;
        Settings.Broadcaster.RefreshToken = refreshedAccount.RefreshToken;
        Settings.Broadcaster.AccessTokenExpiresAt = refreshedAccount.AccessTokenExpiresAt;
        Settings.Broadcaster.SessionRenewalDueAt = refreshedAccount.SessionRenewalDueAt;
        if (refreshedAccount.Scopes.Count > 0)
        {
            Settings.Broadcaster.Scopes = [.. refreshedAccount.Scopes];
        }

        broadcasterReconnectRequired = false;
        UpdateAccountStatuses();
        QueueLiveFeedbackHeartbeatEvaluation();
        QueueSave(0);
        if (bridgeSensitiveChange)
        {
            QueueBridgeRefresh();
        }
    }

    private static bool CanUseCachedBroadcasterToken(
        string userId,
        string accessToken,
        DateTimeOffset? expiresAt,
        TwitchApiException exception)
    {
        return IsTemporaryTokenValidationFailure(exception)
            && !string.IsNullOrWhiteSpace(userId)
            && !string.IsNullOrWhiteSpace(accessToken)
            && expiresAt is { } existingExpiresAt
            && existingExpiresAt > DateTimeOffset.UtcNow.Add(TwitchCachedValidationGraceWindow);
    }

    private static bool IsTemporaryTokenValidationFailure(TwitchApiException exception)
    {
        var statusCode = (int)exception.StatusCode;
        return exception.StatusCode == System.Net.HttpStatusCode.RequestTimeout
            || exception.StatusCode == System.Net.HttpStatusCode.TooManyRequests
            || statusCode >= 500;
    }

    private async Task ApplyBroadcasterAccountRefreshAsync(TwitchAccountSettings refreshedAccount, CancellationToken cancellationToken)
    {
        if (dispatcher.CheckAccess())
        {
            ApplyBroadcasterAccountRefresh(refreshedAccount);
            return;
        }

        await dispatcher.InvokeAsync(
            () => ApplyBroadcasterAccountRefresh(refreshedAccount),
            DispatcherPriority.Normal,
            cancellationToken).Task;
    }

    private void ApplyBroadcasterAccountRefresh(TwitchAccountSettings refreshedAccount)
    {
        var reconnectWasRequired = broadcasterReconnectRequired;
        var bridgeSensitiveChange = HasBridgeSensitiveTwitchAccountChanges(Settings.Broadcaster, refreshedAccount);
        var accountChanged = reconnectWasRequired
            || bridgeSensitiveChange
            || !string.Equals(Settings.Broadcaster.DisplayName, refreshedAccount.DisplayName, StringComparison.Ordinal)
            || !string.Equals(Settings.Broadcaster.ProfileImageUrl, refreshedAccount.ProfileImageUrl, StringComparison.Ordinal)
            || Settings.Broadcaster.AccessTokenExpiresAt != refreshedAccount.AccessTokenExpiresAt
            || Settings.Broadcaster.SessionRenewalDueAt != refreshedAccount.SessionRenewalDueAt;

        Settings.Broadcaster.Apply(refreshedAccount);
        broadcasterReconnectRequired = false;
        QueueLiveFeedbackHeartbeatEvaluation();
        if (BroadcasterCanManageRewards)
        {
            ClearBroadcasterManagedRewardsUnavailableForSession();
        }

        if (!accountChanged)
        {
            return;
        }

        UpdateAccountStatuses();
        QueueSave();
        if (bridgeSensitiveChange)
        {
            QueueBridgeRefresh();
        }
    }

    private void UnlinkTwitchReward(object? target)
    {
        switch (target)
        {
            case TriggerRule rule:
                rule.ChannelPointRewardId = string.Empty;
                break;
            case UniversalTriggerRule trigger:
                trigger.RewardId = string.Empty;
                break;
            case AvatarScaleRule scaleRule:
                scaleRule.RewardId = string.Empty;
                break;
            case AvatarScaleMasterRewardSettings masterReward:
                masterReward.RewardId = string.Empty;
                break;
            case AvatarTriggerProfile profile:
                profile.SetTriggerMasterRewardId = string.Empty;
                break;
            case WardrobeOutfit outfit:
                outfit.TwitchRewardId = string.Empty;
                break;
            default:
                return;
        }

        QueueManagedRewardSync(0);
        QueueSave(0);
    }

    private void UnlinkWardrobeMasterReward(object? target)
    {
        if (target is not AvatarTriggerProfile profile)
        {
            return;
        }

        profile.WardrobeMasterRewardId = string.Empty;

        QueueManagedRewardSync(0);
        QueueSave(0);
    }

    private async Task QueueRewardRefreshAsync()
    {
        await ReloadRuntimeConfigAsync();

        if (IsManagedRewardApiBackoffActive("Twitch reward refresh"))
        {
            return;
        }

        if (!HasRecoverableBroadcasterSession)
        {
            RunOnUi(() => AppendThrottledLog(
                "managed-rewards-refresh-no-broadcaster",
                "Twitch reward refresh skipped because the broadcaster account is not connected.",
                ThrottledRewardSyncLogWindow));
            return;
        }

        if (broadcasterManagedRewardsUnavailableForSession)
        {
            RunOnUi(() => AppendThrottledLog(
                "managed-rewards-refresh-unavailable",
                "Twitch reward refresh skipped because channel point reward management is unavailable for this broadcaster session.",
                ThrottledRewardSyncLogWindow));
            return;
        }

        var accountReadiness = await EnsureBroadcasterRewardManagementReadyAsync(
            "Twitch reward refresh skipped because the broadcaster login is not ready for channel-point reward management.",
            "managed-rewards-broadcaster-not-ready:catalog",
            clearRewardOptions: false,
            CancellationToken.None);
        if (accountReadiness != ManagedRewardSyncOutcome.Completed)
        {
            return;
        }

        try
        {
            var rewards = await twitchApiClient.GetCustomRewardsAsync(
                Settings.Broadcaster.AccessToken,
                runtimeConfig.TwitchClientId,
                Settings.Broadcaster.UserId);

            RunOnUi(() => ApplyRewardCatalog(rewards));
        }
        catch (Exception ex)
        {
            if (IsBroadcasterRewardEligibilityFailure(ex))
            {
                MarkBroadcasterManagedRewardsUnavailableForSession();
                RunOnUi(() => AppendThrottledLog(
                    "managed-rewards-unavailable:catalog",
                    "Crystal Relay cannot load Twitch redeems for this broadcaster because channel point reward management is only available for affiliate or partner accounts.",
                    ThrottledRewardSyncLogWindow));
                return;
            }

            if (IsBroadcasterRewardManagementScopeFailure(ex))
            {
                ReportBroadcasterRewardManagementScopeMissing(
                    "Twitch reward refresh skipped because the broadcaster login is missing channel-point reward management permission.",
                    "managed-rewards-missing-scope:catalog",
                    clearRewardOptions: false);
                return;
            }

            if (IsInvalidBroadcasterTokenFailure(ex))
            {
                RunOnUi(() => AppendThrottledLog(
                    "managed-rewards-token-refresh:catalog",
                    "Crystal Relay could not refresh Twitch redeems yet because the broadcaster login needs to refresh. The app will try again after the broadcaster session updates.",
                    ThrottledRewardSyncLogWindow));
                return;
            }

            if (TryApplyManagedRewardApiBackoff(ex, "Twitch reward refresh"))
            {
                return;
            }

            RunOnUi(() => AppendLog($"Could not refresh channel point rewards: {ex.Message}"));
        }
    }

    private async Task QueuePowerUpRefreshAsync()
    {
        await ReloadRuntimeConfigAsync();

        if (!HasRecoverableBroadcasterSession)
        {
            RunOnUi(() => AppendThrottledLog(
                "power-ups-refresh-no-broadcaster",
                "Power Up refresh skipped because the broadcaster account is not connected.",
                ThrottledRewardSyncLogWindow));
            return;
        }

        var accountSnapshot = await GetBroadcasterRewardAccountSnapshotAsync(CancellationToken.None);
        if ((string.IsNullOrWhiteSpace(accountSnapshot.AccessToken)
                && string.IsNullOrWhiteSpace(accountSnapshot.RefreshToken))
            || string.IsNullOrWhiteSpace(accountSnapshot.TwitchClientId))
        {
            RunOnUi(() => AppendThrottledLog(
                "power-ups-refresh-incomplete",
                "Power Up refresh skipped because the broadcaster login is incomplete. Reconnect the broadcaster account once.",
                ThrottledRewardSyncLogWindow));
            return;
        }

        try
        {
            var account = await ValidateOrRefreshBroadcasterAccountAsync(accountSnapshot, CancellationToken.None);
            var powerUps = await twitchApiClient.GetCustomPowerUpsAsync(
                account.AccessToken,
                accountSnapshot.TwitchClientId,
                account.UserId,
                CancellationToken.None);

            RunOnUi(() => ApplyPowerUpCatalog(powerUps));
        }
        catch (Exception ex)
        {
            if (IsInvalidBroadcasterTokenFailure(ex))
            {
                RunOnUi(() => AppendThrottledLog(
                    "power-ups-token-refresh",
                    "Crystal Relay could not refresh Power Ups yet because the broadcaster login needs to refresh.",
                    ThrottledRewardSyncLogWindow));
                return;
            }

            RunOnUi(() => AppendLog($"Could not refresh Twitch Power Ups: {ex.Message}"));
        }
    }

    // Debounced entry point for managed Twitch reward syncing.
    // Many editor changes land here, so older pending syncs get canceled in favor of the latest state.
    internal void QueueManagedRewardSync(
        int delayMilliseconds = 1100,
        ManagedRewardSyncReason reason = ManagedRewardSyncReason.SettingsEdit)
    {
        if (!isInitialized || isShuttingDown)
        {
            return;
        }

        if (IsManagedRewardApiBackoffActive($"Twitch reward sync ({DescribeManagedRewardSyncReason(reason)})"))
        {
            return;
        }

        if (broadcasterManagedRewardsUnavailableForSession)
        {
            SetUniversalManagedRewardSyncStatus("Universal Twitch reward sync skipped because Twitch channel-point reward management is unavailable for this broadcaster session.");
            return;
        }

        var syncCancellation = ReplaceQueuedCancellationSource(ref managedRewardSyncCancellation);
        var cancellationToken = syncCancellation.Token;

        _ = Task.Run(async () =>
        {
            try
            {
                if (delayMilliseconds > 0)
                {
                    await Task.Delay(delayMilliseconds, cancellationToken);
                }

                if (IsManagedRewardApiBackoffActive($"Twitch reward sync ({DescribeManagedRewardSyncReason(reason)})"))
                {
                    return;
                }

                if (broadcasterManagedRewardsUnavailableForSession)
                {
                    return;
                }

                var accountReadiness = await EnsureBroadcasterRewardManagementReadyAsync(
                    "Universal Twitch reward sync skipped because the broadcaster login is not ready for channel-point reward management.",
                    "managed-rewards-broadcaster-not-ready:sync",
                    clearRewardOptions: false,
                    cancellationToken);
                if (accountReadiness != ManagedRewardSyncOutcome.Completed)
                {
                    return;
                }

                await managedRewardSyncGate.WaitAsync(cancellationToken);
                try
                {
                    var syncOutcome = await SynchronizeManagedChannelPointRewardsAsync(cancellationToken, reason: reason);
                    if (syncOutcome == ManagedRewardSyncOutcome.BroadcasterCustomRewardsUnavailable)
                    {
                        RunOnUi(() => AppendThrottledLog(
                            "managed-rewards-unavailable:sync",
                            "Crystal Relay cannot manage Twitch redeems for this broadcaster because channel point reward management is only available for affiliate or partner accounts.",
                            ThrottledRewardSyncLogWindow));
                    }
                    else if (syncOutcome == ManagedRewardSyncOutcome.BroadcasterTokenRefreshRequired)
                    {
                        RunOnUi(() => AppendThrottledLog(
                            "managed-rewards-token-refresh:sync",
                            "Crystal Relay could not manage Twitch redeems yet because the broadcaster login needs to refresh. The app will try again after the broadcaster session updates.",
                            ThrottledRewardSyncLogWindow));
                    }
                    else if (syncOutcome == ManagedRewardSyncOutcome.BroadcasterRewardManagementScopeMissing)
                    {
                        ReportBroadcasterRewardManagementScopeMissing(
                            "Universal Twitch reward sync skipped because the broadcaster login is missing channel-point reward management permission.",
                            "managed-rewards-missing-scope:sync",
                            clearRewardOptions: false);
                    }
                }
                finally
                {
                    managedRewardSyncGate.Release();
                }
            }
            catch (OperationCanceledException)
            {
            }
            catch (Exception ex)
            {
                SetUniversalManagedRewardSyncStatus($"Universal Twitch reward sync failed: {ex.Message}");
                RunOnUi(() => AppendLog($"Could not sync managed channel point rewards: {ex.Message}"));
            }
            finally
            {
                DisposeCompletedQueuedCancellationSource(ref managedRewardSyncCancellation, syncCancellation);
            }
        }, CancellationToken.None);
    }

    private static bool IsPassiveManagedRewardSyncReason(ManagedRewardSyncReason reason) =>
        reason is ManagedRewardSyncReason.RuntimeAvailability
            or ManagedRewardSyncReason.AvatarScaleStatus
            or ManagedRewardSyncReason.FloatLimitStatus;

    private static bool ShouldSkipUnchangedManagedRewardSync(ManagedRewardSyncReason reason) =>
        IsPassiveManagedRewardSyncReason(reason)
        || reason is ManagedRewardSyncReason.AvatarChanged
            or ManagedRewardSyncReason.TestMode
            or ManagedRewardSyncReason.EmergencyStop
            or ManagedRewardSyncReason.StreamStateChanged
            or ManagedRewardSyncReason.FireSaleChanged;

    private static bool AllowsInactiveManagedRewardDeletion(ManagedRewardSyncReason reason) =>
        reason is ManagedRewardSyncReason.Maintenance or ManagedRewardSyncReason.ManualCleanup;

    private static bool ShouldSkipUninitializedPassiveManagedRewardSync(
        ManagedRewardSyncReason reason,
        bool allowManagedRewardActivation,
        string lastSuccessfulFingerprint)
    {
        return !allowManagedRewardActivation
            && string.IsNullOrWhiteSpace(lastSuccessfulFingerprint)
            && ShouldSkipUnchangedManagedRewardSync(reason);
    }

    private static bool ShouldAllowMissingManagedRewardMaterialization(
        ManagedRewardSyncReason reason,
        bool allowManagedRewardActivation)
    {
        return allowManagedRewardActivation
            || reason is ManagedRewardSyncReason.SettingsEdit
                or ManagedRewardSyncReason.ManualCleanup
                or ManagedRewardSyncReason.Maintenance;
    }

    private static string DescribeManagedRewardSyncReason(ManagedRewardSyncReason reason) => reason switch
    {
        ManagedRewardSyncReason.Startup => "startup",
        ManagedRewardSyncReason.AccountReconnect => "account reconnect",
        ManagedRewardSyncReason.AvatarChanged => "avatar changed",
        ManagedRewardSyncReason.RuntimeAvailability => "runtime availability",
        ManagedRewardSyncReason.AvatarScaleStatus => "avatar scale status",
        ManagedRewardSyncReason.FloatLimitStatus => "float limit status",
        ManagedRewardSyncReason.TestMode => "test mode",
        ManagedRewardSyncReason.EmergencyStop => "emergency stop",
        ManagedRewardSyncReason.StreamStateChanged => "stream state changed",
        ManagedRewardSyncReason.FireSaleChanged => "reward fire sale",
        ManagedRewardSyncReason.ManualRefresh => "manual refresh",
        ManagedRewardSyncReason.ManualCleanup => "manual cleanup",
            ManagedRewardSyncReason.Maintenance => "maintenance",
            ManagedRewardSyncReason.AvatarScaleMasterRewardUnlocked => "avatar scaling master reward unlock changed",
        _ => "settings edit"
    };

    private bool IsManagedRewardApiBackoffActive(string operation)
    {
        if (managedRewardApiBackoffUntil is not { } retryAfterUtc)
        {
            return false;
        }

        var now = DateTimeOffset.UtcNow;
        if (retryAfterUtc <= now)
        {
            managedRewardApiBackoffUntil = null;
            return false;
        }

        RunOnUi(() => AppendThrottledLog(
            "managed-rewards-api-backoff",
            $"{operation} skipped because Twitch asked Crystal Relay to slow reward API calls until {retryAfterUtc.LocalDateTime:t}.",
            ThrottledRewardSyncLogWindow));
        return true;
    }

    private bool TryApplyManagedRewardApiBackoff(Exception ex, string operation)
    {
        if (ex is not TwitchApiException { StatusCode: System.Net.HttpStatusCode.TooManyRequests } twitchException)
        {
            return false;
        }

        var retryAfterUtc = twitchException.RetryAfterUtc ?? DateTimeOffset.UtcNow.AddMinutes(1);
        if (managedRewardApiBackoffUntil is null || managedRewardApiBackoffUntil.Value < retryAfterUtc)
        {
            managedRewardApiBackoffUntil = retryAfterUtc;
        }

        RunOnUi(() => AppendThrottledLog(
            "managed-rewards-api-rate-limited",
            $"{operation} hit Twitch's reward API rate limit. Crystal Relay will pause reward API calls until {retryAfterUtc.LocalDateTime:t}.",
            ThrottledRewardSyncLogWindow));
        return true;
    }

    private void ReportBroadcasterRewardManagementScopeMissing(string status, string logKey, bool clearRewardOptions)
    {
        SetUniversalManagedRewardSyncStatus(status);
        RunOnUi(() =>
        {
            if (clearRewardOptions)
            {
                RewardOptions.Clear();
            }

            RaisePropertyChanged(nameof(ManagedChannelPointRewardHelpText));
            RaisePropertyChanged(nameof(UniversalManagedChannelPointRewardHelpText));
            AppendThrottledLog(
                logKey,
                "Reconnect the broadcaster account once so Crystal Relay can manage Twitch channel point rewards. The saved Twitch login is missing channel-point reward management permission.",
                ThrottledRewardSyncLogWindow);
        });
    }

    private static CancellationTokenSource ReplaceQueuedCancellationSource(ref CancellationTokenSource? field)
    {
        var next = new CancellationTokenSource();
        var previous = Interlocked.Exchange(ref field, next);
        TryCancelCancellationSource(previous);
        return next;
    }

    private static void CancelAndDisposeQueuedCancellationSource(ref CancellationTokenSource? field)
    {
        var previous = Interlocked.Exchange(ref field, null);
        TryCancelCancellationSource(previous);
        TryDisposeCancellationSource(previous);
    }

    private static void DisposeCompletedQueuedCancellationSource(
        ref CancellationTokenSource? field,
        CancellationTokenSource cancellationSource)
    {
        Interlocked.CompareExchange(ref field, null, cancellationSource);
        TryDisposeCancellationSource(cancellationSource);
    }

    private static void TryCancelCancellationSource(CancellationTokenSource? cancellationSource)
    {
        if (cancellationSource is null)
        {
            return;
        }

        try
        {
            cancellationSource.Cancel();
        }
        catch (ObjectDisposedException)
        {
        }
    }

    private static void TryDisposeCancellationSource(CancellationTokenSource? cancellationSource)
    {
        if (cancellationSource is null)
        {
            return;
        }

        try
        {
            cancellationSource.Dispose();
        }
        catch (ObjectDisposedException)
        {
        }
    }

    private void SetUniversalManagedRewardSyncStatus(string status)
    {
        if (!dispatcher.CheckAccess())
        {
            RunOnUi(() => SetUniversalManagedRewardSyncStatus(status));
            return;
        }

        var normalizedStatus = status?.Trim() ?? string.Empty;
        if (string.Equals(universalManagedRewardSyncStatusText, normalizedStatus, StringComparison.Ordinal))
        {
            return;
        }

        universalManagedRewardSyncStatusText = normalizedStatus;
    }

    private string GetUniversalRewardActivationReason(bool? forcedManagedRewardActivation, bool allowManagedRewardActivation)
    {
        if (allowManagedRewardActivation)
        {
            if (forcedManagedRewardActivation == true)
            {
                return "Reward visibility was forced on for this sync.";
            }

            return IsBroadcasterLive
                ? "Broadcaster is live, so eligible Universal rewards can show."
                : "Test Mode is on, so eligible Universal rewards can show.";
        }

        if (forcedManagedRewardActivation == false)
        {
            return "This sync intentionally kept rewards off for maintenance.";
        }

        if (Settings.EmergencyRedeemStopEnabled)
        {
            return "Redeems are paused, so Universal rewards are kept off on Twitch.";
        }

        return "Test Mode is off and the broadcaster is not live, so Universal rewards are kept off on Twitch.";
    }

    private async Task RefreshCurrentAvatarStateForManagedRewardSyncAsync(CancellationToken cancellationToken)
    {
        if (!Settings.VrChat.IsConnected)
        {
            try
            {
                await RefreshCurrentVrChatAvatarFromLocalFilesAsync(
                    cancellationToken,
                    queueManagedRewardSync: false);
            }
            catch (Exception ex) when (ex is not OperationCanceledException)
            {
                RunOnUi(() => AppendThrottledLog(
                    "managed-reward-current-avatar-local-cache-refresh",
                    $"Crystal Relay could not read the cached VRChat avatar state before reward sync: {ex.Message}",
                    ThrottledRewardSyncLogWindow));
            }

            await RecoverManagedRewardCurrentAvatarFromBridgeRuntimeAsync(cancellationToken);
            if (string.IsNullOrWhiteSpace(GetManagedRewardActivationAvatarId()))
            {
                RunOnUi(() => AppendThrottledLog(
                    "managed-reward-current-avatar-unknown",
                    "Crystal Relay does not know the current VRChat avatar yet. Avatar Set and Avatar Change rewards will stay hidden until the avatar refresh succeeds.",
                    ThrottledRewardSyncLogWindow));
            }

            return;
        }

        var apiRefreshSucceeded = false;
        try
        {
            await RefreshCurrentVrChatAvatarFromApiAsync(
                cancellationToken,
                queueManagedRewardSync: false,
                queueLocalStateRefresh: false);
            apiRefreshSucceeded = true;
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            RunOnUi(() => AppendThrottledLog(
                "managed-reward-current-avatar-api-refresh",
                $"Crystal Relay could not refresh the current avatar from VRChat before reward sync: {GetFriendlyVrChatError(ex)}",
                ThrottledRewardSyncLogWindow));
        }

        if (!apiRefreshSucceeded || string.IsNullOrWhiteSpace(Settings.VrChat.CurrentAvatarId))
        {
            try
            {
                await RefreshCurrentVrChatAvatarFromLocalFilesAsync(
                    cancellationToken,
                    queueManagedRewardSync: false);
            }
            catch (Exception ex) when (ex is not OperationCanceledException)
            {
                RunOnUi(() => AppendThrottledLog(
                    "managed-reward-current-avatar-local-refresh",
                    $"Crystal Relay could not read the local VRChat avatar state before reward sync: {ex.Message}",
                    ThrottledRewardSyncLogWindow));
            }
        }

        await RecoverManagedRewardCurrentAvatarFromBridgeRuntimeAsync(cancellationToken);

        var currentAvatarId = GetManagedRewardActivationAvatarId();
        if (!string.IsNullOrWhiteSpace(currentAvatarId))
        {
            RunOnUi(() =>
            {
                ReplaceCurrentAvatarInCache(currentAvatarId);
                UpdateAvatarProfileActivityStates();
                RefreshVrChatAvatarSelectionOptions();
                RefreshAvatarParameterOptions();
            });
        }
        else
        {
            RunOnUi(() => AppendThrottledLog(
                "managed-reward-current-avatar-unknown",
                "Crystal Relay does not know the current VRChat avatar yet. Avatar Set and Avatar Change rewards will stay hidden until the avatar refresh succeeds.",
                ThrottledRewardSyncLogWindow));
        }
    }

    private async Task RecoverManagedRewardCurrentAvatarFromBridgeRuntimeAsync(CancellationToken cancellationToken)
    {
        if (!string.IsNullOrWhiteSpace(Settings.VrChat.CurrentAvatarId))
        {
            return;
        }

        var runtimeAvatarId = bridgeCoordinator.CurrentVrChatAvatarId?.Trim() ?? string.Empty;
        if (string.IsNullOrWhiteSpace(runtimeAvatarId))
        {
            return;
        }

        await dispatcher.InvokeAsync(() =>
        {
            if (!string.IsNullOrWhiteSpace(Settings.VrChat.CurrentAvatarId))
            {
                return;
            }

            Settings.VrChat.CurrentAvatarId = runtimeAvatarId;
            ReplaceCurrentAvatarInCache(runtimeAvatarId);
            UpdateAvatarProfileActivityStates();
            RefreshVrChatAvatarSelectionOptions();
            RefreshAvatarParameterOptions();
            QueueSave(0);
            AppendThrottledLog(
                "managed-reward-current-avatar-runtime-recovered",
                $"Crystal Relay recovered the current VRChat avatar from the live bridge state before syncing Twitch rewards: {ResolveVrChatAvatarName(runtimeAvatarId)}.",
                ThrottledRewardSyncLogWindow);
        }, DispatcherPriority.Normal, cancellationToken).Task;
    }

    private void QueueCurrentVrChatAvatarRefresh(int delayMilliseconds = 0)
    {
        if (!isInitialized
            || isShuttingDown)
        {
            return;
        }

        if (!Settings.VrChat.IsConnected)
        {
            QueueCurrentVrChatLocalStateRefresh(delayMilliseconds);
            return;
        }

        var refreshCancellation = ReplaceQueuedCancellationSource(ref vrChatCurrentAvatarRefreshCancellation);
        var cancellationToken = refreshCancellation.Token;

        _ = Task.Run(async () =>
        {
            try
            {
                if (delayMilliseconds > 0)
                {
                    await Task.Delay(delayMilliseconds, cancellationToken);
                }

                await vrChatCurrentAvatarRefreshGate.WaitAsync(cancellationToken);
                try
                {
                    await RefreshCurrentVrChatAvatarFromApiAsync(cancellationToken);
                }
                finally
                {
                    vrChatCurrentAvatarRefreshGate.Release();
                }
            }
            catch (OperationCanceledException)
            {
            }
            catch (Exception ex)
            {
                RunOnUi(() => AppendLog($"Could not refresh the current VRChat avatar: {GetFriendlyVrChatError(ex)}"));
            }
            finally
            {
                DisposeCompletedQueuedCancellationSource(ref vrChatCurrentAvatarRefreshCancellation, refreshCancellation);
            }
        }, CancellationToken.None);
    }

    private void QueueCurrentVrChatLocalStateRefresh(int delayMilliseconds = 0)
    {
        if (!isInitialized || isShuttingDown)
        {
            return;
        }

        if (string.IsNullOrWhiteSpace(Settings.VrChat.DisplayName)
            && string.IsNullOrWhiteSpace(ResolveCurrentUserIdForCache()))
        {
            return;
        }

        _ = Task.Run(async () =>
        {
            try
            {
                if (delayMilliseconds > 0)
                {
                    await Task.Delay(delayMilliseconds, CancellationToken.None);
                }

                if (!await vrChatLocalStateRefreshGate.WaitAsync(0, CancellationToken.None))
                {
                    return;
                }

                try
                {
                    await RefreshCurrentVrChatAvatarFromLocalFilesAsync(CancellationToken.None);
                }
                finally
                {
                    vrChatLocalStateRefreshGate.Release();
                }
            }
            catch (OperationCanceledException)
            {
            }
            catch (Exception ex)
            {
                RunOnUi(() => AppendLog($"Could not read the local VRChat output log: {ex.Message}"));
            }
        }, CancellationToken.None);
    }

    private async Task RefreshCurrentVrChatAvatarFromLocalFilesAsync(
        CancellationToken cancellationToken,
        bool queueManagedRewardSync = true)
    {
        var resolvedAvatarId = string.Empty;
        var detectedAvatarName = string.Empty;
        var localDisplayName = Settings.VrChat.DisplayName?.Trim() ?? string.Empty;

        if (!string.IsNullOrWhiteSpace(localDisplayName))
        {
            var logState = await vrChatLocalClientStateService.ReadLatestLocalAvatarSwitchAsync(
                localDisplayName,
                vrChatOutputLogPath,
                vrChatOutputLogPosition,
                cancellationToken);

            vrChatOutputLogPath = logState.LogPath;
            vrChatOutputLogPosition = logState.NextPosition;

            detectedAvatarName = logState.AvatarName?.Trim() ?? string.Empty;
            if (!string.IsNullOrWhiteSpace(detectedAvatarName))
            {
                resolvedAvatarId = ResolveVrChatAvatarIdByName(detectedAvatarName);
            }
        }

        if (string.IsNullOrWhiteSpace(resolvedAvatarId))
        {
            var resolvedUserId = ResolveCurrentUserIdForCache();
            if (!string.IsNullOrWhiteSpace(resolvedUserId))
            {
                var latestLocalAvatar = await vrChatLocalOscCacheService
                    .LoadLatestKnownAvatarAsync(resolvedUserId, cancellationToken);
                if (latestLocalAvatar is not null)
                {
                    resolvedAvatarId = latestLocalAvatar.AvatarId?.Trim() ?? string.Empty;
                    detectedAvatarName = latestLocalAvatar.AvatarName?.Trim() ?? string.Empty;
                }
            }
        }

        if (string.IsNullOrWhiteSpace(resolvedAvatarId))
        {
            return;
        }

        var currentAvatarId = Settings.VrChat.CurrentAvatarId?.Trim() ?? string.Empty;
        if (string.Equals(lastDetectedVrChatAvatarId, resolvedAvatarId, StringComparison.Ordinal)
            && string.Equals(currentAvatarId, resolvedAvatarId, StringComparison.Ordinal))
        {
            return;
        }

        // When VRChat is connected, the VRChat API is the authoritative source for the
        // current avatar. The local output-log + OSC-cache fallback can return a stale
        // avatar (e.g. the previously worn avatar whose OSC JSON file has a more recent
        // write time). Letting the 2-second local state poll override the API-corrected
        // avatar creates a feedback loop: local detection sets avatar A → managed reward
        // sync's API refresh corrects to avatar B → 2s later local detection sets A again.
        // Skip the override when connected and the API has already set a current avatar.
        if (Settings.VrChat.IsConnected
            && !string.IsNullOrWhiteSpace(currentAvatarId)
            && !string.Equals(currentAvatarId, resolvedAvatarId, StringComparison.Ordinal))
        {
            lastDetectedVrChatAvatarId = resolvedAvatarId;
            return;
        }

        lastDetectedVrChatAvatarId = resolvedAvatarId;

        await dispatcher.InvokeAsync(() =>
        {
            if (!string.Equals(Settings.VrChat.CurrentAvatarId?.Trim() ?? string.Empty, resolvedAvatarId, StringComparison.Ordinal))
            {
                HandleVrChatAvatarChangedByBridge(resolvedAvatarId, queueManagedRewardSync);
                var detectedDisplayName = ResolveVrChatAvatarName(resolvedAvatarId);
                if (string.IsNullOrWhiteSpace(detectedDisplayName))
                {
                    detectedDisplayName = GetSafeVrChatAvatarDisplayName(
                        string.Equals(detectedAvatarName, resolvedAvatarId, StringComparison.Ordinal) ? string.Empty : detectedAvatarName,
                        "Unknown avatar");
                }

                AppendLog(TF("Detected your current VRChat avatar as {0}.", detectedDisplayName));
            }
            else
            {
                RefreshAvatarParameterOptions();
                _ = EnsureSelectedAvatarParameterCacheLoadedAsync();
            }
        });

        QueueLocalVrChatOscAvatarScan(0);
    }

    private async Task RefreshCurrentVrChatAvatarFromApiAsync(
        CancellationToken cancellationToken,
        bool queueManagedRewardSync = true,
        bool queueLocalStateRefresh = true)
    {
        if (!Settings.VrChat.IsConnected)
        {
            return;
        }

        try
        {
            var account = await vrChatApiClient.GetCurrentUserAsync(Settings.VrChat.AuthCookie, cancellationToken);
            var previousAvatarId = Settings.VrChat.CurrentAvatarId?.Trim() ?? string.Empty;
            var refreshedAvatarId = account.CurrentAvatarId?.Trim() ?? string.Empty;
            var avatarChanged = !string.Equals(previousAvatarId, refreshedAvatarId, StringComparison.Ordinal);

            await dispatcher.InvokeAsync(() =>
            {
                Settings.VrChat.DisplayName = account.DisplayName;
                StartOrRefreshVrChatLocalOscWatcher();
                QueueLocalVrChatOscAvatarScan(0);
                if (queueLocalStateRefresh)
                {
                    QueueCurrentVrChatLocalStateRefresh(0);
                }

                if (avatarChanged)
                {
                    HandleVrChatAvatarChangedByBridge(refreshedAvatarId, queueManagedRewardSync);
                    AppendLog(TF("Detected your current VRChat avatar as {0}.", CurrentVrChatAvatarDisplayName));
                }
                else
                {
                    ReplaceCurrentAvatarInCache(refreshedAvatarId);
                    UpdateAvatarProfileActivityStates();
                    RefreshVrChatAvatarSelectionOptions();
                    RefreshAvatarParameterOptions();
                }
            });
        }
        catch (VrChatApiException apiException) when (apiException.StatusCode == System.Net.HttpStatusCode.Unauthorized)
        {
            await HandleVrChatUnauthorizedAsync(cancellationToken);
            RaiseVrChatConnectionStateProperties();
            SyncVrChatRuntimeState(queueManagedRewardSync: false);
            VrChatStatus = T("VRChat avatar access is not connected.");
            VrChatAvatarStatus = availableVrChatAvatars.Count == 0
                ? T("No cached avatars yet. Connect VRChat once to build the cache.")
                : TF("Showing {0} cached avatars. Connect VRChat to refresh from the API.", availableVrChatAvatars.Count);
            VrChatOscParameterStatus = T("Pick an avatar set to load its saved OSC parameters.");
            AppendLog(T("Disconnected VRChat avatar access. Cached avatars remain available."));
            RecordSavedLoginRecoverySignal();
            QueueSave();
        }
    }

    private void StartOrRefreshVrChatLocalOscWatcher()
    {
        // Allow the LocalLow watcher to run even while VRChat is disconnected,
        // as long as we can resolve a userId from the live settings or the
        // LocalLow folder. This keeps the cached avatar list and parameter
        // cache fresh after a manual disconnect, so Avatar Swap / Avatar Sets /
        // Wardrobe rewards still have a populated avatar list to pick from.
        var resolvedUserId = ResolveCurrentUserIdForCache();
        if (string.IsNullOrWhiteSpace(resolvedUserId))
        {
            DisposeVrChatLocalOscWatcher();
            return;
        }

        var avatarFolderPath = VrChatLocalOscCacheService.GetAvatarOscFolderPath(resolvedUserId);
        if (string.IsNullOrWhiteSpace(avatarFolderPath) || !Directory.Exists(avatarFolderPath))
        {
            DisposeVrChatLocalOscWatcher();
            return;
        }

        if (vrChatLocalOscWatcher is not null
            && string.Equals(vrChatLocalOscWatcher.Path, avatarFolderPath, StringComparison.OrdinalIgnoreCase))
        {
            return;
        }

        DisposeVrChatLocalOscWatcher();

        vrChatLocalOscWatcher = new FileSystemWatcher(avatarFolderPath, "*.json")
        {
            NotifyFilter = NotifyFilters.FileName | NotifyFilters.LastWrite | NotifyFilters.CreationTime,
            IncludeSubdirectories = false,
            EnableRaisingEvents = true
        };

        vrChatLocalOscWatcher.Changed += OnVrChatLocalOscAvatarFilesChanged;
        vrChatLocalOscWatcher.Created += OnVrChatLocalOscAvatarFilesChanged;
        vrChatLocalOscWatcher.Renamed += OnVrChatLocalOscAvatarFilesChanged;
    }

    private void DisposeVrChatLocalOscWatcher()
    {
        if (vrChatLocalOscWatcher is null)
        {
            return;
        }

        vrChatLocalOscWatcher.EnableRaisingEvents = false;
        vrChatLocalOscWatcher.Changed -= OnVrChatLocalOscAvatarFilesChanged;
        vrChatLocalOscWatcher.Created -= OnVrChatLocalOscAvatarFilesChanged;
        vrChatLocalOscWatcher.Renamed -= OnVrChatLocalOscAvatarFilesChanged;
        vrChatLocalOscWatcher.Dispose();
        vrChatLocalOscWatcher = null;
    }

    private void ResetVrChatLocalRuntimeTracking()
    {
        latestLocalVrChatAvatarWriteTimeUtc = DateTime.MinValue;
        vrChatLocalOscAvatarWriteTimes.Clear();
        vrChatOutputLogPath = string.Empty;
        vrChatOutputLogPosition = 0;
        lastDetectedVrChatAvatarId = string.Empty;
    }

    private void OnVrChatLocalOscAvatarFilesChanged(object sender, FileSystemEventArgs e)
    {
        QueueLocalVrChatOscAvatarScan();
    }

    private void QueueLocalVrChatOscAvatarScan(int delayMilliseconds = 350)
    {
        if (!isInitialized || isShuttingDown || string.IsNullOrWhiteSpace(ResolveCurrentUserIdForCache()))
        {
            return;
        }

        var scanCancellation = ReplaceQueuedCancellationSource(ref vrChatLocalOscScanCancellation);
        var cancellationToken = scanCancellation.Token;

        _ = Task.Run(async () =>
        {
            try
            {
                if (delayMilliseconds > 0)
                {
                    await Task.Delay(delayMilliseconds, cancellationToken);
                }

                await ScanLocalVrChatOscAvatarCacheAsync(cancellationToken);
            }
            catch (OperationCanceledException)
            {
            }
            catch (Exception ex)
            {
                RunOnUi(() => AppendLog($"Could not read the local VRChat OSC avatar cache: {ex.Message}"));
            }
            finally
            {
                DisposeCompletedQueuedCancellationSource(ref vrChatLocalOscScanCancellation, scanCancellation);
            }
        }, CancellationToken.None);
    }

    private async Task ScanLocalVrChatOscAvatarCacheAsync(CancellationToken cancellationToken)
    {
        // Allow the LocalLow scan to run after a VRChat disconnect by resolving
        // the userId from Settings first, then from the cached LocalLow folder.
        // This is what keeps the in-memory avatar list and the OSC parameter
        // cache usable while the user is signed out (Cached state).
        var resolvedUserId = ResolveCurrentUserIdForCache();
        if (string.IsNullOrWhiteSpace(resolvedUserId))
        {
            return;
        }

        var localAvatars = await vrChatLocalOscCacheService.LoadKnownAvatarsAsync(resolvedUserId, cancellationToken);
        if (localAvatars.Count == 0)
        {
            return;
        }

        var refreshedParameterAvatarIds = new HashSet<string>(StringComparer.Ordinal);
        foreach (var localAvatar in localAvatars)
        {
            cancellationToken.ThrowIfCancellationRequested();

            var normalizedAvatarId = localAvatar.AvatarId?.Trim() ?? string.Empty;
            if (string.IsNullOrWhiteSpace(normalizedAvatarId))
            {
                continue;
            }

            var hasCachedParameters = cachedVrChatParametersByAvatarId.TryGetValue(normalizedAvatarId, out var existingParameters)
                && existingParameters.Count > 0;
            var hasImportedLocalWriteTime = vrChatLocalOscAvatarWriteTimes.TryGetValue(normalizedAvatarId, out var importedWriteTimeUtc);
            var shouldRefreshParameters = !hasImportedLocalWriteTime
                || localAvatar.LastWriteTimeUtc > importedWriteTimeUtc
                || !hasCachedParameters;

            if (!shouldRefreshParameters)
            {
                continue;
            }

            try
            {
                var localParameters = await vrChatLocalOscCacheService.LoadAvatarParametersAsync(
                    resolvedUserId,
                    normalizedAvatarId,
                    cancellationToken);

                await CacheVrChatOscParametersForAvatarAsync(normalizedAvatarId, localParameters, cancellationToken);
                vrChatLocalOscAvatarWriteTimes[normalizedAvatarId] = localAvatar.LastWriteTimeUtc;
                refreshedParameterAvatarIds.Add(normalizedAvatarId);
            }
            catch (OperationCanceledException)
            {
                throw;
            }
            catch (Exception)
            {
                // VRChat can still be writing the local OSC file when the watcher fires.
                // Skip this pass so the next file change or manual refresh can retry cleanly.
            }
        }

        RunOnUi(() =>
        {
            ApplyLocalVrChatOscAvatars(localAvatars);
            if (refreshedParameterAvatarIds.Count > 0)
            {
                RefreshAvatarParameterOptions();
            }
        });

        var persistUserId = ResolveCurrentUserIdForCache();
        if (!string.IsNullOrEmpty(persistUserId))
        {
            try
            {
                await settingsStore.SaveVrChatAvatarCacheAsync(
                    persistUserId,
                    availableVrChatAvatars,
                    cancellationToken);
            }
            catch
            {
                // best-effort; do not break the OSC flow
            }
        }

        RecomputeVrChatConnectionState();
    }

    private void ApplyLocalVrChatOscAvatars(IReadOnlyList<LocalVrChatOscAvatarSummary> localAvatars)
    {
        var avatarsChanged = MergeLocalVrChatAvatars(localAvatars);

        if (avatarsChanged)
        {
            SyncVrChatAvatarRuleLabels();
            RefreshVrChatAvatarSelectionOptions();
        }

        var currentAvatarId = Settings.VrChat.CurrentAvatarId?.Trim() ?? string.Empty;
        if (string.IsNullOrWhiteSpace(currentAvatarId))
        {
            return;
        }

        var currentAvatar = localAvatars.FirstOrDefault(avatar =>
            string.Equals(avatar.AvatarId, currentAvatarId, StringComparison.Ordinal));
        if (currentAvatar is null)
        {
            return;
        }

        if (currentAvatar.LastWriteTimeUtc > latestLocalVrChatAvatarWriteTimeUtc)
        {
            latestLocalVrChatAvatarWriteTimeUtc = currentAvatar.LastWriteTimeUtc;
            QueueCurrentVrChatOscParameterRefresh(currentAvatar.AvatarId);
            RefreshAvatarParameterOptions();
        }
    }

    private bool MergeLocalVrChatAvatars(IReadOnlyList<LocalVrChatOscAvatarSummary> localAvatars)
    {
        if (localAvatars.Count == 0)
        {
            return false;
        }

        var changed = false;
        var currentAvatarId = Settings.VrChat.CurrentAvatarId?.Trim() ?? string.Empty;
        var mergedAvatars = availableVrChatAvatars.ToDictionary(avatar => avatar.Id, StringComparer.Ordinal);

        foreach (var localAvatar in localAvatars)
        {
            var normalizedLocalAvatarId = localAvatar.AvatarId?.Trim() ?? string.Empty;
            if (string.IsNullOrWhiteSpace(normalizedLocalAvatarId))
            {
                continue;
            }

            var normalizedLocalAvatarName = localAvatar.AvatarName?.Trim() ?? string.Empty;
            if (!mergedAvatars.TryGetValue(normalizedLocalAvatarId, out var existingAvatar))
            {
                var fallbackName = string.IsNullOrWhiteSpace(normalizedLocalAvatarName)
                    || string.Equals(normalizedLocalAvatarName, normalizedLocalAvatarId, StringComparison.Ordinal)
                    ? GetAvatarDuplicateHint(normalizedLocalAvatarId)
                    : normalizedLocalAvatarName;
                mergedAvatars[normalizedLocalAvatarId] = new VrChatAvatarSummary(
                    normalizedLocalAvatarId,
                    fallbackName,
                    AuthorName: string.Empty,
                    ThumbnailUrl: null,
                    IsCurrentAvatar: string.Equals(normalizedLocalAvatarId, currentAvatarId, StringComparison.Ordinal),
                    IsUploaded: false,
                    IsFavorited: false,
                    IsLicensed: false,
                    Platform: string.Empty,
                    StyleTags: Array.Empty<string>(),
                    ContentTags: Array.Empty<string>(),
                    FavoriteGroupName: null);
                availableVrChatAvatarNamesById[normalizedLocalAvatarId] = fallbackName;
                changed = true;
                continue;
            }

            var existingAvatarName = existingAvatar.Name?.Trim() ?? string.Empty;
            var shouldUseLocalName = !string.IsNullOrWhiteSpace(normalizedLocalAvatarName)
                && !string.Equals(normalizedLocalAvatarName, normalizedLocalAvatarId, StringComparison.Ordinal)
                && (string.IsNullOrWhiteSpace(existingAvatarName)
                    || string.Equals(existingAvatarName, normalizedLocalAvatarId, StringComparison.Ordinal));

            if (!shouldUseLocalName)
            {
                continue;
            }

            mergedAvatars[normalizedLocalAvatarId] = existingAvatar with { Name = normalizedLocalAvatarName };
            availableVrChatAvatarNamesById[normalizedLocalAvatarId] = normalizedLocalAvatarName;
            changed = true;
        }

        if (!changed)
        {
            return false;
        }

        var updatedAvatars = availableVrChatAvatars
            .Select(avatar => mergedAvatars.TryGetValue(avatar.Id, out var mergedAvatar)
                ? mergedAvatar with { IsCurrentAvatar = string.Equals(mergedAvatar.Id, currentAvatarId, StringComparison.Ordinal) }
                : avatar)
            .ToList();
        var existingIds = updatedAvatars
            .Select(avatar => avatar.Id)
            .ToHashSet(StringComparer.Ordinal);
        updatedAvatars.AddRange(mergedAvatars
            .Where(pair => existingIds.Add(pair.Key))
            .Select(pair => pair.Value with
            {
                IsCurrentAvatar = string.Equals(pair.Key, currentAvatarId, StringComparison.Ordinal)
            }));

        availableVrChatAvatars.Clear();
        availableVrChatAvatars.AddRange(updatedAvatars);
        return true;
    }

    private IEnumerable<ManagedRewardOwnershipEntry> EnumerateManagedRewardOwnershipEntries(
        IReadOnlyCollection<TriggerRule> supportedMovementRules,
        IReadOnlyCollection<UniversalTriggerRule> managedUniversalTriggers,
        IReadOnlyCollection<AvatarScaleRule> managedAvatarScaleRules,
        AvatarScaleMasterRewardSettings? avatarScaleMasterReward)
    {
        foreach (var profile in Settings.AvatarProfiles)
        {
            var groupedRuleIds = new HashSet<Guid>();
            foreach (var group in EnumerateSharedAvatarSetRewardGroups(profile))
            {
                if (group.UsesSetTriggerMasterReward)
                {
                    yield return new ManagedRewardOwnershipEntry(
                        profile.Id,
                        GetSetTriggerMasterRewardId(profile, group),
                        GetSetTriggerMasterRewardTitle(profile, group.Rules),
                        profile.SetTriggerMasterRewardSyncMode);
                }

                foreach (var rule in group.Rules)
                {
                    groupedRuleIds.Add(rule.Id);
                    if (!group.UsesSetTriggerMasterReward)
                    {
                        yield return new ManagedRewardOwnershipEntry(
                            group.Owner.Id,
                            rule.ChannelPointRewardId,
                            group.RewardTitle,
                            group.RewardSyncMode);
                    }

                    if (IsActiveFloatBoostParentRule(rule))
                    {
                        yield return new ManagedRewardOwnershipEntry(
                            rule.ActiveFloatBoostRewardOwnerId,
                            rule.ActiveFloatBoostRewardId,
                            rule.ActiveFloatBoostRewardTitle,
                            TwitchRewardSyncMode.CreateOrManage);
                    }
                }
            }

            foreach (var rule in profile.ChannelPointRules)
            {
                if (groupedRuleIds.Contains(rule.Id))
                {
                    continue;
                }

                yield return new ManagedRewardOwnershipEntry(
                    rule.Id,
                    rule.ChannelPointRewardId,
                    rule.ChannelPointRewardTitle,
                    rule.RewardSyncMode);

                if (IsActiveFloatBoostParentRule(rule))
                {
                    yield return new ManagedRewardOwnershipEntry(
                        rule.ActiveFloatBoostRewardOwnerId,
                        rule.ActiveFloatBoostRewardId,
                        rule.ActiveFloatBoostRewardTitle,
                        TwitchRewardSyncMode.CreateOrManage);
                }
            }

            foreach (var outfit in profile.WardrobeOutfits)
            {
                var rewardTitle = GetWardrobeOutfitRewardTitle(outfit);
                if (string.IsNullOrWhiteSpace(rewardTitle)
                    && string.IsNullOrWhiteSpace(outfit.TwitchRewardId))
                {
                    continue;
                }

                yield return new ManagedRewardOwnershipEntry(
                    outfit.Id,
                    outfit.TwitchRewardId,
                    rewardTitle,
                    outfit.TwitchRewardSyncMode);
            }

            if ((profile.UseWardrobeMasterReward && profile.UseWardrobeMode)
                || !string.IsNullOrWhiteSpace(profile.WardrobeMasterRewardId))
            {
                var masterRewardTitle = profile.WardrobeMasterRewardTitle?.Trim() ?? string.Empty;
                if (!string.IsNullOrWhiteSpace(masterRewardTitle)
                    || !string.IsNullOrWhiteSpace(profile.WardrobeMasterRewardId))
                {
                    yield return new ManagedRewardOwnershipEntry(
                        profile.Id,
                        profile.WardrobeMasterRewardId,
                        masterRewardTitle,
                        profile.WardrobeMasterRewardSyncMode);
                }
            }
        }

        foreach (var rule in supportedMovementRules)
        {
            yield return new ManagedRewardOwnershipEntry(
                rule.Id,
                rule.ChannelPointRewardId,
                rule.ChannelPointRewardTitle,
                rule.RewardSyncMode);
        }

        foreach (var swapProfile in Settings.AvatarSwapProfiles)
        {
            foreach (var rule in swapProfile.ChannelPointRules)
            {
                yield return new ManagedRewardOwnershipEntry(
                    rule.Id,
                    rule.ChannelPointRewardId,
                    rule.ChannelPointRewardTitle,
                    rule.RewardSyncMode);
            }
        }

        foreach (var trigger in managedUniversalTriggers)
        {
            yield return new ManagedRewardOwnershipEntry(
                trigger.Id,
                trigger.RewardId,
                trigger.RewardTitle,
                trigger.RewardSyncMode);
        }

        if (avatarScaleMasterReward is not null)
        {
            yield return new ManagedRewardOwnershipEntry(
                AvatarScaleMasterRewardOwnerId,
                avatarScaleMasterReward.RewardId,
                avatarScaleMasterReward.RewardTitle,
                avatarScaleMasterReward.RewardSyncMode);
        }

        foreach (var rule in managedAvatarScaleRules)
        {
            yield return new ManagedRewardOwnershipEntry(
                rule.Id,
                rule.RewardId,
                GetAvatarScaleManagedRewardTitle(rule),
                rule.RewardSyncMode);
        }

        var fireSale = Settings.RewardFireSale;
        if (fireSale.FundingRewardEnabled || !string.IsNullOrWhiteSpace(fireSale.FundingRewardId))
        {
            yield return new ManagedRewardOwnershipEntry(
                RewardFireSaleFundingRewardOwnerId,
                fireSale.FundingRewardId,
                fireSale.FundingRewardTitle,
                TwitchRewardSyncMode.CreateOrManage);
        }
    }

    private string GetEffectiveRequiredAvatarIdForProfile(AvatarTriggerProfile? profile)
    {
        if (profile is null)
        {
            return string.Empty;
        }

        if (profile.IsMasterProfile && !string.IsNullOrWhiteSpace(Settings.MasterAvatarSwapReturnId))
        {
            return Settings.MasterAvatarSwapReturnId.Trim();
        }

        return profile.AvatarId?.Trim() ?? string.Empty;
    }

    private ManagedRewardSyncTarget CreateManagedRewardTargetForRule(
        AvatarTriggerProfile? profile,
        TriggerRule rule,
        string currentAvatarId,
        bool avatarChangeTransitionActive,
        bool allowManagedRewardActivation,
        IReadOnlyCollection<Guid> temporarilyDisabledRuleIds,
        IReadOnlyCollection<Guid> cooldownRuleIds,
        IReadOnlyCollection<Guid> activeTimedRuleIds,
        IReadOnlyCollection<Guid> activeFloatLimitReachedRuleIds)
    {
        var ruleHasRuntimeReadyAction = HasRuntimeReadyAction(rule);
        var isOnLocalCooldown = cooldownRuleIds.Contains(rule.Id);
        var normalizedCurrentAvatarId = currentAvatarId?.Trim() ?? string.Empty;
        var normalizedAvatarChangeTargetId = rule.AvatarChangeTargetId?.Trim() ?? string.Empty;
        var isCooldownOnlyDirectAvatarChange = Settings.AvatarChangeCooldownOnlyModeEnabled
            && profile?.IsMasterProfile == true
            && rule.ActionType is OscActionType.AvatarChange or OscActionType.AvatarRoulet;
        var isCurrentAvatarChangeTarget = isCooldownOnlyDirectAvatarChange
            && rule.ActionType == OscActionType.AvatarChange
            && !string.IsNullOrWhiteSpace(normalizedCurrentAvatarId)
            && !string.IsNullOrWhiteSpace(normalizedAvatarChangeTargetId)
            && string.Equals(normalizedAvatarChangeTargetId, normalizedCurrentAvatarId, StringComparison.Ordinal);
        var anyCooldownOnlyAvatarChangeOnCooldown = isCooldownOnlyDirectAvatarChange
            && profile!.ChannelPointRules.Any(candidate =>
                candidate.ActionType is OscActionType.AvatarChange or OscActionType.AvatarRoulet
                && cooldownRuleIds.Contains(candidate.Id));
        var cooldownOnlyAvatarChangeVisible = isCooldownOnlyDirectAvatarChange
            && !string.IsNullOrWhiteSpace(normalizedCurrentAvatarId)
            && (isOnLocalCooldown || (!anyCooldownOnlyAvatarChangeOnCooldown && !isCurrentAvatarChangeTarget));
        var profileIsEffectivelyActive = AvatarRuleActivationPolicy.IsRuleActiveForCurrentAvatar(
            isGlobalOverride: profile is null,
            belongsToMasterAvatarProfile: profile?.IsMasterProfile ?? false,
            actionType: rule.ActionType,
            avatarChangeTargetId: rule.AvatarChangeTargetId,
            requiredAvatarId: GetEffectiveRequiredAvatarIdForProfile(profile),
            currentAvatarId: currentAvatarId,
            avatarChangeTransitionActive: avatarChangeTransitionActive,
            avatarChangeCooldownOnlyModeEnabled: Settings.AvatarChangeCooldownOnlyModeEnabled,
            permanentAvatarChange: rule.PermanentAvatarChange,
            permanentChangeCompleted: bridgeCoordinator.IsPermanentChangeCompleted(rule.Id));
        var isActiveFloatBoostParent = IsActiveFloatBoostParentRule(rule) && activeTimedRuleIds.Contains(rule.Id);
        var floatLimitReached = activeFloatLimitReachedRuleIds.Contains(rule.Id)
            && rule.UsesFloatHideOnLimit
            && (rule.HideRewardWhenFloatMaxReached || rule.HideRewardWhenFloatMinReached);
        var ruleIsVisibleForCurrentAvatar = isCooldownOnlyDirectAvatarChange
            ? cooldownOnlyAvatarChangeVisible
            : profileIsEffectivelyActive;
        var desiredEnabled = allowManagedRewardActivation
            && ruleHasRuntimeReadyAction
            && (profile?.IsEnabled ?? true)
            && rule.IsEnabled
            && !temporarilyDisabledRuleIds.Contains(rule.Id)
            && ruleIsVisibleForCurrentAvatar
            && !isActiveFloatBoostParent
            && !floatLimitReached;
        var backgroundColor = ManagedRewardPresentation.NormalizeReadyBackgroundColor(
            isOnLocalCooldown ? rule.ManagedRewardCooldownColor : rule.ManagedRewardReadyColor);

        return new ManagedRewardSyncTarget(
            rule.Id,
            rule.DisplayTitle,
            rule.ChannelPointRewardId,
            rule.ChannelPointRewardTitle,
            ApplyRewardFireSaleDiscount(rule.ChannelPointRewardCost, rule.RewardSyncMode),
            rule.RewardSyncMode,
            rule.CooldownSeconds,
            backgroundColor,
            prompt: BuildManagedRewardPrompt(rule.ChannelPointRewardDescription),
            requireUserInput: false,
            desiredEnabled: desiredEnabled,
            isCooldownActive: isOnLocalCooldown,
            deleteWhenInactive: rule.DeleteManagedRewardWhenInactive && !isCooldownOnlyDirectAvatarChange && !temporarilyDisabledRuleIds.Contains(rule.Id) && !isActiveFloatBoostParent && !floatLimitReached,
            protectFromCapReclaim: desiredEnabled || isOnLocalCooldown || temporarilyDisabledRuleIds.Contains(rule.Id) || isActiveFloatBoostParent || isCooldownOnlyDirectAvatarChange || floatLimitReached,
            applyRewardId: rewardId => rule.ChannelPointRewardId = rewardId);
    }

    private ManagedRewardSyncTarget CreateManagedRewardTargetForAvatarSwapRule(
        AvatarSwapProfile swapProfile,
        TriggerRule rule,
        string currentAvatarId,
        string returnAvatarId,
        bool avatarChangeTransitionActive,
        bool allowManagedRewardActivation,
        IReadOnlyCollection<Guid> temporarilyDisabledRuleIds,
        IReadOnlyCollection<Guid> cooldownRuleIds,
        IReadOnlyCollection<Guid> activeTimedRuleIds,
        IReadOnlyCollection<Guid> activeFloatLimitReachedRuleIds)
    {
        var ruleHasRuntimeReadyAction = HasRuntimeReadyAction(rule);
        var isOnLocalCooldown = cooldownRuleIds.Contains(rule.Id);
        var normalizedCurrentAvatarId = currentAvatarId?.Trim() ?? string.Empty;
        var normalizedAvatarChangeTargetId = rule.AvatarChangeTargetId?.Trim() ?? string.Empty;
        var isCooldownOnlyDirectAvatarChange = (Settings.AvatarChangeCooldownOnlyModeEnabled || Settings.PermanentSwapModeEnabled)
            && rule.ActionType is OscActionType.AvatarChange or OscActionType.AvatarRoulet;
        var isCurrentAvatarChangeTarget = isCooldownOnlyDirectAvatarChange
            && rule.ActionType == OscActionType.AvatarChange
            && !string.IsNullOrWhiteSpace(normalizedCurrentAvatarId)
            && !string.IsNullOrWhiteSpace(normalizedAvatarChangeTargetId)
            && string.Equals(normalizedAvatarChangeTargetId, normalizedCurrentAvatarId, StringComparison.Ordinal);
        var anyCooldownOnlyAvatarChangeOnCooldown = isCooldownOnlyDirectAvatarChange
            && swapProfile.ChannelPointRules.Any(candidate =>
                candidate.ActionType is OscActionType.AvatarChange or OscActionType.AvatarRoulet
                && cooldownRuleIds.Contains(candidate.Id));
        var cooldownOnlyAvatarChangeVisible = isCooldownOnlyDirectAvatarChange
            && !string.IsNullOrWhiteSpace(normalizedCurrentAvatarId)
            && (isOnLocalCooldown || (!anyCooldownOnlyAvatarChangeOnCooldown && !isCurrentAvatarChangeTarget));
        var swapRequiredAvatarId = rule.ReturnToPreviousAvatar && !string.IsNullOrWhiteSpace(swapProfile.TargetAvatarId)
            ? swapProfile.TargetAvatarId
            : returnAvatarId;
        var profileIsEffectivelyActive = AvatarRuleActivationPolicy.IsRuleActiveForCurrentAvatar(
            isGlobalOverride: false,
            belongsToMasterAvatarProfile: true,
            actionType: rule.ActionType,
            avatarChangeTargetId: rule.AvatarChangeTargetId,
            requiredAvatarId: swapRequiredAvatarId,
            currentAvatarId: currentAvatarId,
            avatarChangeTransitionActive: avatarChangeTransitionActive,
            avatarChangeCooldownOnlyModeEnabled: Settings.AvatarChangeCooldownOnlyModeEnabled,
            permanentAvatarChange: rule.PermanentAvatarChange,
            permanentChangeCompleted: bridgeCoordinator.IsPermanentChangeCompleted(rule.Id));
        var isActiveFloatBoostParent = IsActiveFloatBoostParentRule(rule) && activeTimedRuleIds.Contains(rule.Id);
        var floatLimitReached = activeFloatLimitReachedRuleIds.Contains(rule.Id)
            && rule.UsesFloatHideOnLimit
            && (rule.HideRewardWhenFloatMaxReached || rule.HideRewardWhenFloatMinReached);
        var ruleIsVisibleForCurrentAvatar = isCooldownOnlyDirectAvatarChange
            ? cooldownOnlyAvatarChangeVisible
            : profileIsEffectivelyActive;
        var permanentSwapBlocked = Settings.PermanentSwapModeEnabled
            && bridgeCoordinator.GetPermanentSwapModeBlockedUntil() is DateTimeOffset blockUntil
            && blockUntil > DateTimeOffset.UtcNow;
        var desiredEnabled = allowManagedRewardActivation
            && ruleHasRuntimeReadyAction
            && swapProfile.IsEnabled
            && rule.IsEnabled
            && !temporarilyDisabledRuleIds.Contains(rule.Id)
            && ruleIsVisibleForCurrentAvatar
            && !isActiveFloatBoostParent
            && !floatLimitReached
            && !permanentSwapBlocked;
        var backgroundColor = ManagedRewardPresentation.NormalizeReadyBackgroundColor(
            isOnLocalCooldown ? rule.ManagedRewardCooldownColor : rule.ManagedRewardReadyColor);

        return new ManagedRewardSyncTarget(
            rule.Id,
            rule.DisplayTitle,
            rule.ChannelPointRewardId,
            rule.ChannelPointRewardTitle,
            ApplyRewardFireSaleDiscount(rule.ChannelPointRewardCost, rule.RewardSyncMode),
            rule.RewardSyncMode,
            rule.CooldownSeconds,
            backgroundColor,
            prompt: BuildManagedRewardPrompt(rule.ChannelPointRewardDescription),
            requireUserInput: false,
            desiredEnabled: desiredEnabled,
            isCooldownActive: isOnLocalCooldown,
            deleteWhenInactive: rule.DeleteManagedRewardWhenInactive && !isCooldownOnlyDirectAvatarChange && !temporarilyDisabledRuleIds.Contains(rule.Id) && !isActiveFloatBoostParent && !floatLimitReached && !permanentSwapBlocked,
            protectFromCapReclaim: desiredEnabled || isOnLocalCooldown || temporarilyDisabledRuleIds.Contains(rule.Id) || isActiveFloatBoostParent || isCooldownOnlyDirectAvatarChange || floatLimitReached || permanentSwapBlocked,
            applyRewardId: rewardId => rule.ChannelPointRewardId = rewardId);
    }

    private ManagedRewardSyncTarget CreateManagedRewardTargetForRouletteRule(
        AvatarRouletteProfile rouletteProfile,
        TriggerRule rule,
        string currentAvatarId,
        bool avatarChangeTransitionActive,
        bool allowManagedRewardActivation,
        IReadOnlyCollection<Guid> temporarilyDisabledRuleIds,
        IReadOnlyCollection<Guid> cooldownRuleIds,
        IReadOnlyCollection<Guid> activeTimedRuleIds,
        IReadOnlyCollection<Guid> activeFloatLimitReachedRuleIds)
    {
        var ruleHasRuntimeReadyAction = rule.ActionType == OscActionType.AvatarRoulet
            ? rouletteProfile.Pool.Any(entry => !string.IsNullOrWhiteSpace(entry.AvatarId))
            : HasRuntimeReadyAction(rule);
        var isOnLocalCooldown = cooldownRuleIds.Contains(rule.Id);
        var returnAvatarId = !string.IsNullOrWhiteSpace(rouletteProfile.ReturnAvatarId)
            ? rouletteProfile.ReturnAvatarId.Trim()
            : !string.IsNullOrWhiteSpace(Settings.MasterAvatarSwapReturnId)
                ? Settings.MasterAvatarSwapReturnId.Trim()
                : string.Empty;
        var isActiveFloatBoostParent = IsActiveFloatBoostParentRule(rule) && activeTimedRuleIds.Contains(rule.Id);
        var floatLimitReached = activeFloatLimitReachedRuleIds.Contains(rule.Id)
            && rule.UsesFloatHideOnLimit
            && (rule.HideRewardWhenFloatMaxReached || rule.HideRewardWhenFloatMinReached);
        var ruleIsVisibleForCurrentAvatar = string.IsNullOrWhiteSpace(returnAvatarId)
            || AvatarRuleActivationPolicy.IsRuleActiveForCurrentAvatar(
                isGlobalOverride: false,
                belongsToMasterAvatarProfile: true,
                actionType: rule.ActionType,
                avatarChangeTargetId: string.Empty,
                requiredAvatarId: returnAvatarId,
                currentAvatarId: currentAvatarId,
                avatarChangeTransitionActive: avatarChangeTransitionActive,
                avatarChangeCooldownOnlyModeEnabled: false,
                permanentAvatarChange: false,
                permanentChangeCompleted: false);
        var desiredEnabled = allowManagedRewardActivation
            && ruleHasRuntimeReadyAction
            && rouletteProfile.Pool.Count > 0
            && rouletteProfile.IsEnabled
            && rule.IsEnabled
            && !temporarilyDisabledRuleIds.Contains(rule.Id)
            && ruleIsVisibleForCurrentAvatar
            && !isActiveFloatBoostParent
            && !floatLimitReached;
        var backgroundColor = ManagedRewardPresentation.NormalizeReadyBackgroundColor(
            isOnLocalCooldown ? rule.ManagedRewardCooldownColor : rule.ManagedRewardReadyColor);

        return new ManagedRewardSyncTarget(
            rule.Id,
            rule.DisplayTitle,
            rule.ChannelPointRewardId,
            rule.ChannelPointRewardTitle,
            ApplyRewardFireSaleDiscount(rule.ChannelPointRewardCost, rule.RewardSyncMode),
            rule.RewardSyncMode,
            rule.CooldownSeconds,
            backgroundColor,
            prompt: BuildManagedRewardPrompt(rule.ChannelPointRewardDescription),
            requireUserInput: false,
            desiredEnabled: desiredEnabled,
            isCooldownActive: isOnLocalCooldown,
            deleteWhenInactive: rule.DeleteManagedRewardWhenInactive && !temporarilyDisabledRuleIds.Contains(rule.Id) && !isActiveFloatBoostParent && !floatLimitReached,
            protectFromCapReclaim: desiredEnabled || isOnLocalCooldown || temporarilyDisabledRuleIds.Contains(rule.Id) || isActiveFloatBoostParent || floatLimitReached,
            applyRewardId: rewardId => rule.ChannelPointRewardId = rewardId);
    }

    private ManagedRewardSyncTarget? CreateManagedRewardTargetForWardrobeOutfit(
        AvatarTriggerProfile profile,
        WardrobeOutfit outfit,
        bool allowManagedRewardActivation)
    {
        if (string.IsNullOrWhiteSpace(profile.AvatarId))
        {
            return null;
        }

        var rewardTitle = GetWardrobeOutfitRewardTitle(outfit);
        if (string.IsNullOrWhiteSpace(rewardTitle))
        {
            return null;
        }

        if (!int.TryParse(outfit.TwitchRewardCost, out var parsedCost) || parsedCost < 1)
        {
            parsedCost = 100;
        }

        var desiredEnabled = allowManagedRewardActivation
            && profile.IsEnabled
            && profile.UseWardrobeMode
            && outfit.IsEnabled;

        return new ManagedRewardSyncTarget(
            outfit.Id,
            outfit.DisplayTitle,
            outfit.TwitchRewardId,
            rewardTitle,
            parsedCost,
            outfit.TwitchRewardSyncMode,
            cooldownSeconds: 0,
            backgroundColor: ManagedRewardPresentation.NormalizeReadyBackgroundColor(outfit.ManagedRewardReadyColor),
            prompt: outfit.TwitchRewardDescription,
            requireUserInput: false,
            desiredEnabled: desiredEnabled,
            isCooldownActive: false,
            deleteWhenInactive: outfit.DeleteManagedRewardWhenInactive,
            protectFromCapReclaim: desiredEnabled,
            applyRewardId: rewardId => outfit.TwitchRewardId = rewardId);
    }

    private static string GetWardrobeOutfitRewardTitle(WardrobeOutfit outfit)
    {
        var explicitTitle = outfit.TwitchRewardTitle?.Trim() ?? string.Empty;
        if (!string.IsNullOrWhiteSpace(explicitTitle))
        {
            return explicitTitle;
        }

        return outfit.DisplayTitle?.Trim() ?? string.Empty;
    }

    private ManagedRewardSyncTarget? CreateManagedRewardTargetForWardrobeMasterReward(
        AvatarTriggerProfile profile,
        bool allowManagedRewardActivation)
    {
        if (string.IsNullOrWhiteSpace(profile.AvatarId)
            || string.IsNullOrWhiteSpace(profile.WardrobeMasterRewardTitle))
        {
            return null;
        }

        var desiredEnabled = allowManagedRewardActivation
            && profile.IsEnabled
            && profile.UseWardrobeMode
            && profile.UseWardrobeMasterReward;

        return new ManagedRewardSyncTarget(
            profile.Id,
            profile.WardrobeMasterRewardTitle,
            profile.WardrobeMasterRewardId,
            profile.WardrobeMasterRewardTitle,
            profile.WardrobeMasterRewardCost,
            profile.WardrobeMasterRewardSyncMode,
            profile.WardrobeMasterRewardCooldownSeconds,
            ManagedRewardPresentation.NormalizeReadyBackgroundColor(profile.WardrobeMasterRewardReadyColor),
            prompt: string.Empty,
            requireUserInput: true,
            desiredEnabled: desiredEnabled,
            isCooldownActive: false,
            deleteWhenInactive: false,
            protectFromCapReclaim: desiredEnabled,
            applyRewardId: rewardId => profile.WardrobeMasterRewardId = rewardId);
    }

    private ManagedRewardSyncTarget? CreateManagedRewardTargetForActiveFloatBoostReward(
        AvatarTriggerProfile profile,
        TriggerRule rule,
        string currentAvatarId,
        bool avatarChangeTransitionActive,
        bool allowManagedRewardActivation,
        IReadOnlyCollection<Guid> temporarilyDisabledRuleIds,
        IReadOnlyCollection<Guid> activeTimedRuleIds,
        IReadOnlyCollection<Guid> activeFloatBoostMaximumReachedRuleIds)
    {
        if (!IsActiveFloatBoostParentRule(rule))
        {
            return null;
        }

        var profileIsEffectivelyActive = AvatarRuleActivationPolicy.IsRuleActiveForCurrentAvatar(
            isGlobalOverride: false,
            belongsToMasterAvatarProfile: profile.IsMasterProfile,
            actionType: rule.ActionType,
            avatarChangeTargetId: rule.AvatarChangeTargetId,
            requiredAvatarId: GetEffectiveRequiredAvatarIdForProfile(profile),
            currentAvatarId: currentAvatarId,
            avatarChangeTransitionActive: avatarChangeTransitionActive,
            permanentAvatarChange: rule.PermanentAvatarChange,
            permanentChangeCompleted: bridgeCoordinator.IsPermanentChangeCompleted(rule.Id));
        var parentIsActive = activeTimedRuleIds.Contains(rule.Id);
        var boostMaximumReached = activeFloatBoostMaximumReachedRuleIds.Contains(rule.Id);
        var parentCanBeManaged = rule.RewardSyncMode == TwitchRewardSyncMode.CreateOrManage;
        var desiredEnabled = allowManagedRewardActivation
            && parentCanBeManaged
            && parentIsActive
            && !boostMaximumReached
            && profile.IsEnabled
            && profileIsEffectivelyActive
            && rule.IsEnabled
            && !temporarilyDisabledRuleIds.Contains(rule.Id)
            && HasRuntimeReadyAction(rule);
        var rewardTitle = string.IsNullOrWhiteSpace(rule.ActiveFloatBoostRewardTitle)
            ? T("Active Boost Reward")
            : rule.ActiveFloatBoostRewardTitle;

        return new ManagedRewardSyncTarget(
            rule.ActiveFloatBoostRewardOwnerId,
            TF("Active boost for {0}", rule.DisplayTitle),
            rule.ActiveFloatBoostRewardId,
            rewardTitle,
            ApplyRewardFireSaleDiscount(rule.ActiveFloatBoostRewardCost, TwitchRewardSyncMode.CreateOrManage),
            TwitchRewardSyncMode.CreateOrManage,
            rule.ActiveFloatBoostRewardCooldownSeconds,
            ManagedRewardPresentation.NormalizeReadyBackgroundColor(rule.ActiveFloatBoostRewardReadyColor),
            prompt: BuildManagedRewardPrompt(rule.ActiveFloatBoostRewardDescription),
            requireUserInput: false,
            desiredEnabled: desiredEnabled,
            isCooldownActive: false,
            deleteWhenInactive: false,
            protectFromCapReclaim: parentIsActive || desiredEnabled,
            applyRewardId: rewardId => rule.ActiveFloatBoostRewardId = rewardId);
    }

    private ManagedRewardSyncTarget CreateManagedRewardTargetForSharedAvatarSetRewardGroup(
        AvatarTriggerProfile profile,
        SharedAvatarSetRewardGroup group,
        string currentAvatarId,
        bool avatarChangeTransitionActive,
        bool allowManagedRewardActivation,
        IReadOnlyCollection<Guid> temporarilyDisabledRuleIds,
        IReadOnlyCollection<Guid> cooldownRuleIds,
        IReadOnlyCollection<Guid> activeTimedRuleIds)
    {
        var owner = group.Owner;
        var profileIsEffectivelyActive = AvatarRuleActivationPolicy.IsRuleActiveForCurrentAvatar(
            isGlobalOverride: false,
            belongsToMasterAvatarProfile: profile.IsMasterProfile,
            actionType: owner.ActionType,
            avatarChangeTargetId: owner.AvatarChangeTargetId,
            requiredAvatarId: GetEffectiveRequiredAvatarIdForProfile(profile),
            currentAvatarId: currentAvatarId,
            avatarChangeTransitionActive: avatarChangeTransitionActive,
            permanentAvatarChange: owner.PermanentAvatarChange,
            permanentChangeCompleted: bridgeCoordinator.IsPermanentChangeCompleted(owner.Id));
        var activeChoices = group.Rules
            .Where(rule => rule.IsEnabled
                && HasRuntimeReadyAction(rule)
                && !temporarilyDisabledRuleIds.Contains(rule.Id))
            .ToArray();
        var desiredEnabled = allowManagedRewardActivation
            && profile.IsEnabled
            && profileIsEffectivelyActive
            && activeChoices.Length > 0;
        var anyChoiceInCooldown = group.Rules.Any(rule => cooldownRuleIds.Contains(rule.Id));
        var anyChoiceTemporarilyDisabled = group.Rules.Any(rule => temporarilyDisabledRuleIds.Contains(rule.Id));
        var anyActiveFloatBoostChoice = group.Rules.Any(rule => IsActiveFloatBoostParentRule(rule) && activeTimedRuleIds.Contains(rule.Id));
        desiredEnabled = desiredEnabled && !anyActiveFloatBoostChoice;
        var readyColor = group.UsesSetTriggerMasterReward
            ? profile.SetTriggerMasterRewardReadyColor
            : owner.ManagedRewardReadyColor;
        var cooldownColor = group.UsesSetTriggerMasterReward
            ? profile.SetTriggerMasterRewardCooldownColor
            : owner.ManagedRewardCooldownColor;
        var backgroundColor = ManagedRewardPresentation.NormalizeReadyBackgroundColor(
            anyChoiceInCooldown ? cooldownColor : readyColor);
        var rewardId = group.UsesSetTriggerMasterReward
            ? GetSetTriggerMasterRewardId(profile, group)
            : GetSharedAvatarSetRewardGroupRewardId(group);
        var rewardTitle = group.UsesSetTriggerMasterReward
            ? GetSetTriggerMasterRewardTitle(profile, group.Rules)
            : group.RewardTitle;
        var rewardCost = group.UsesSetTriggerMasterReward
            ? profile.SetTriggerMasterRewardCost
            : owner.ChannelPointRewardCost;
        var rewardSyncMode = group.UsesSetTriggerMasterReward
            ? profile.SetTriggerMasterRewardSyncMode
            : group.RewardSyncMode;
        var cooldownSeconds = group.UsesSetTriggerMasterReward
            ? profile.SetTriggerMasterRewardCooldownSeconds
            : owner.CooldownSeconds;
        var deleteWhenInactive = group.UsesSetTriggerMasterReward
            ? profile.DeleteSetTriggerMasterRewardWhenInactive
            : owner.DeleteManagedRewardWhenInactive;

        return new ManagedRewardSyncTarget(
            group.UsesSetTriggerMasterReward ? profile.Id : owner.Id,
            group.UsesSetTriggerMasterReward ? "Set Trigger Master Reward" : owner.DisplayTitle,
            rewardId,
            rewardTitle,
            ApplyRewardFireSaleDiscount(rewardCost, rewardSyncMode),
            rewardSyncMode,
            cooldownSeconds,
            backgroundColor,
            prompt: BuildSharedAvatarSetRewardPrompt(
                activeChoices.Length > 0 ? activeChoices : group.Rules,
                group.UsesSetTriggerMasterReward
                    ? profile.SetTriggerMasterRewardDescription
                    : owner.ChannelPointRewardDescription),
            requireUserInput: true,
            desiredEnabled: desiredEnabled,
            isCooldownActive: anyChoiceInCooldown,
            deleteWhenInactive: deleteWhenInactive && !anyChoiceInCooldown && !anyChoiceTemporarilyDisabled && !anyActiveFloatBoostChoice,
            protectFromCapReclaim: desiredEnabled || anyChoiceInCooldown || anyChoiceTemporarilyDisabled || anyActiveFloatBoostChoice,
            applyRewardId: rewardId =>
            {
                if (group.UsesSetTriggerMasterReward)
                {
                    profile.SetTriggerMasterRewardId = rewardId;
                }

                foreach (var rule in group.Rules)
                {
                    rule.ChannelPointRewardId = rewardId;
                }
            });
    }

    private static IEnumerable<SharedAvatarSetRewardGroup> EnumerateSharedAvatarSetRewardGroups(AvatarTriggerProfile profile)
    {
        return profile.ChannelPointRules
            .Where(rule => IsSharedAvatarSetRewardChoiceRule(profile, rule))
            .Select(rule => new
            {
                Rule = rule,
                GroupKey = GetSharedAvatarSetRewardGroupKey(rule)
            })
            .Where(entry => entry.Rule.ActionType == OscActionType.SetTrigger || !string.IsNullOrWhiteSpace(entry.GroupKey))
            .GroupBy(entry => entry.GroupKey, StringComparer.OrdinalIgnoreCase)
            .Select(group =>
            {
                var rules = group
                    .Select(entry => entry.Rule)
                    .OrderBy(rule => Math.Max(1, rule.SharedRewardChoiceNumber))
                    .ThenBy(rule => rule.DisplayTitle, StringComparer.CurrentCultureIgnoreCase)
                    .ToArray();
                var owner = rules.FirstOrDefault(rule =>
                        rule.RewardSyncMode == TwitchRewardSyncMode.LinkExisting
                        && !string.IsNullOrWhiteSpace(rule.ChannelPointRewardId))
                    ?? rules.FirstOrDefault(rule => rule.RewardSyncMode == TwitchRewardSyncMode.LinkExisting)
                    ?? rules[0];
                var usesSetTriggerMasterReward = profile.UseSharedNumberedOutfitReward
                    && rules.Any(rule => rule.ActionType == OscActionType.SetTrigger);
                var rewardTitle = rules
                    .Select(rule => rule.ChannelPointRewardTitle?.Trim() ?? string.Empty)
                    .FirstOrDefault(title => !string.IsNullOrWhiteSpace(title)) ?? string.Empty;
                return new SharedAvatarSetRewardGroup(
                    owner,
                    rules,
                    rewardTitle,
                    usesSetTriggerMasterReward,
                    usesSetTriggerMasterReward
                        ? profile.SetTriggerMasterRewardSyncMode
                        : owner.RewardSyncMode);
            });
    }

    private static bool IsSharedAvatarSetRewardChoiceRule(TriggerRule rule) =>
        rule.TriggerType == TwitchTriggerType.ChannelPoints
        && rule.SharedRewardChoiceEnabled
        && rule.SharedRewardChoiceNumber > 0;

    private static bool IsSharedAvatarSetRewardChoiceRule(AvatarTriggerProfile profile, TriggerRule rule) =>
        IsSharedAvatarSetRewardChoiceRule(rule)
        && (rule.ActionType != OscActionType.SetTrigger || profile.UseSharedNumberedOutfitReward);

    private static string GetSharedAvatarSetRewardGroupKey(TriggerRule rule)
    {
        if (rule.ActionType == OscActionType.SetTrigger)
        {
            return "set-trigger-master";
        }

        var titleKey = ManagedRewardPresentation.NormalizeTitleIdentityKey(rule.ChannelPointRewardTitle);
        if (!string.IsNullOrWhiteSpace(titleKey))
        {
            return $"title:{titleKey}";
        }

        var rewardId = rule.ChannelPointRewardId?.Trim() ?? string.Empty;
        return string.IsNullOrWhiteSpace(rewardId) ? string.Empty : $"id:{rewardId}";
    }

    private static string GetSharedAvatarSetRewardGroupRewardId(SharedAvatarSetRewardGroup group)
    {
        var ownerRewardId = group.Owner.ChannelPointRewardId?.Trim() ?? string.Empty;
        if (!string.IsNullOrWhiteSpace(ownerRewardId))
        {
            return ownerRewardId;
        }

        return group.Rules
            .Select(rule => rule.ChannelPointRewardId?.Trim() ?? string.Empty)
            .FirstOrDefault(rewardId => !string.IsNullOrWhiteSpace(rewardId)) ?? string.Empty;
    }

    private static string GetSetTriggerMasterRewardId(AvatarTriggerProfile profile, SharedAvatarSetRewardGroup group)
    {
        var rewardId = profile.SetTriggerMasterRewardId?.Trim() ?? string.Empty;
        if (!string.IsNullOrWhiteSpace(rewardId))
        {
            return rewardId;
        }

        return group.Rules
            .Select(rule => rule.ChannelPointRewardId?.Trim() ?? string.Empty)
            .FirstOrDefault(candidate => !string.IsNullOrWhiteSpace(candidate)) ?? string.Empty;
    }

    private static string GetSetTriggerMasterRewardTitle(
        AvatarTriggerProfile profile,
        IReadOnlyCollection<TriggerRule> rules)
    {
        var rewardTitle = profile.SetTriggerMasterRewardTitle?.Trim() ?? string.Empty;
        if (!string.IsNullOrWhiteSpace(rewardTitle))
        {
            return rewardTitle;
        }

        return rules
            .Select(rule => rule.ChannelPointRewardTitle?.Trim() ?? string.Empty)
            .FirstOrDefault(candidate => !string.IsNullOrWhiteSpace(candidate)) ?? string.Empty;
    }

    private static string BuildManagedRewardPrompt(string? description)
    {
        var configuredDescription = description?.Trim() ?? string.Empty;
        return string.IsNullOrWhiteSpace(configuredDescription)
            ? T("Managed by Crystal Relay.")
            : configuredDescription;
    }

    private static string BuildSharedAvatarSetRewardPrompt(IReadOnlyList<TriggerRule> rules, string? description = null)
    {
        var options = rules
            .Where(IsSharedAvatarSetRewardChoiceRule)
            .OrderBy(rule => Math.Max(1, rule.SharedRewardChoiceNumber))
            .ThenBy(rule => rule.DisplayTitle, StringComparer.CurrentCultureIgnoreCase)
            .Select(DescribeSharedAvatarSetRewardChoiceOption)
            .Where(option => !string.IsNullOrWhiteSpace(option))
            .ToArray();
        if (options.Length == 0)
        {
            return T("Type a number to choose this avatar set redeem.");
        }

        var prompt = $"{T("Use number")}: {string.Join(", ", options)}";
        var choicePrompt = prompt.Length <= TwitchCustomRewardPromptMaxLength
            ? prompt
            : T("Type a number to choose this avatar set redeem.");
        var configuredDescription = description?.Trim() ?? string.Empty;
        if (string.IsNullOrWhiteSpace(configuredDescription))
        {
            return choicePrompt;
        }

        var combinedPrompt = $"{configuredDescription} {choicePrompt}";
        return combinedPrompt.Length <= TwitchCustomRewardPromptMaxLength
            ? combinedPrompt
            : choicePrompt;
    }

    private static string DescribeSharedAvatarSetRewardChoiceOption(TriggerRule rule)
    {
        var label = !string.IsNullOrWhiteSpace(rule.SharedRewardHelpText)
            ? rule.SharedRewardHelpText.Trim()
            : !string.IsNullOrWhiteSpace(rule.Name)
                ? rule.Name.Trim()
                : rule.DisplayTitle.Trim();
        if (rule.ActionType == OscActionType.AvatarParameter
            && !string.IsNullOrWhiteSpace(rule.ParameterName))
        {
            label = $"{label} ({GetAvatarParameterDisplayName(rule.ParameterName)} -> {rule.ParameterValue})";
        }
        else if (rule.ActionType == OscActionType.SetTrigger
                 && string.IsNullOrWhiteSpace(rule.SharedRewardHelpText))
        {
            label = TF("Set Trigger ({0} params)", rule.SetTriggerActions.Count);
        }

        return $"{Math.Max(1, rule.SharedRewardChoiceNumber)} = {label}";
    }

    private ManagedRewardSyncTarget? CreateManagedRewardTargetForUniversalTrigger(
        UniversalTriggerRule trigger,
        bool allowManagedRewardActivation,
        string currentAvatarId)
    {
        var triggerIsConfigured = trigger.IsConfigured;
        if (!triggerIsConfigured && string.IsNullOrWhiteSpace(trigger.RewardId))
        {
            return null;
        }

        var desiredEnabled = allowManagedRewardActivation
            && trigger.IsEnabled
            && triggerIsConfigured
            && HasRuntimeReadyUniversalTriggerAction(trigger)
            && IsUniversalTriggerReadyForCurrentAvatarJson(trigger, currentAvatarId);
        var rewardTitle = triggerIsConfigured ? trigger.RewardTitle : string.Empty;
        var shouldDeleteWhenInactive = trigger.DeleteManagedRewardWhenInactive
            || (!triggerIsConfigured && !string.IsNullOrWhiteSpace(trigger.RewardId));

        return new ManagedRewardSyncTarget(
            trigger.Id,
            trigger.DisplayTitle,
            trigger.RewardId,
            rewardTitle,
            ApplyRewardFireSaleDiscount(trigger.RewardCost, trigger.RewardSyncMode),
            trigger.RewardSyncMode,
            cooldownSeconds: trigger.UsesCreateOrManageReward ? trigger.RewardCooldownSeconds : 0,
            backgroundColor: ManagedRewardPresentation.NormalizeReadyBackgroundColor(trigger.ManagedRewardReadyColor),
            prompt: BuildManagedRewardPrompt(trigger.RewardDescription),
            requireUserInput: false,
            desiredEnabled: desiredEnabled,
            isCooldownActive: false,
            deleteWhenInactive: shouldDeleteWhenInactive,
            protectFromCapReclaim: desiredEnabled,
            applyRewardId: rewardId => trigger.RewardId = rewardId);
    }

    private ManagedRewardSyncTarget CreateManagedRewardTargetForAvatarScaleMasterReward(
        AvatarScaleMasterRewardSettings masterReward,
        bool allowManagedRewardActivation,
        bool useCooldownPresentation,
        bool isCooldownActive)
    {
        var hasRewardIdentity = masterReward.RewardSyncMode == TwitchRewardSyncMode.LinkExisting
            ? !string.IsNullOrWhiteSpace(masterReward.RewardId)
            : !string.IsNullOrWhiteSpace(masterReward.RewardTitle);
        var desiredEnabled = allowManagedRewardActivation
            && masterReward.IsEnabled
            && hasRewardIdentity;
        // The master reward is shown in its cooldown color while the unlock window is active
        // OR while the redemption cooldown is active, so a full sync that runs mid-unlock does
        // not reset the color back to the ready color and undo the per-reward PATCH.
        var backgroundColor = isCooldownActive
            ? ManagedRewardPresentation.NormalizeCooldownBackgroundColor(masterReward.ManagedRewardCooldownColor)
            : ManagedRewardPresentation.NormalizeReadyBackgroundColor(masterReward.ManagedRewardReadyColor);

        return new ManagedRewardSyncTarget(
            AvatarScaleMasterRewardOwnerId,
            "Avatar Scaling Master Reward",
            masterReward.RewardId,
            masterReward.RewardTitle,
            ApplyRewardFireSaleDiscount(masterReward.RewardCost, masterReward.RewardSyncMode),
            masterReward.RewardSyncMode,
            masterReward.CooldownSeconds,
            backgroundColor,
            prompt: BuildManagedRewardPrompt(masterReward.RewardDescription),
            requireUserInput: false,
            desiredEnabled: desiredEnabled,
            isCooldownActive: isCooldownActive,
            deleteWhenInactive: masterReward.DeleteMasterRewardWhenInactive,
            protectFromCapReclaim: desiredEnabled || useCooldownPresentation || isCooldownActive,
            applyRewardId: rewardId => masterReward.RewardId = rewardId);
    }

    private ManagedRewardSyncTarget CreateManagedRewardTargetForAvatarScaleRule(
        AvatarScaleRule rule,
        bool allowManagedRewardActivation,
        IReadOnlyCollection<Guid> cooldownRuleIds,
        IReadOnlyCollection<Guid> temporarilyDisabledRuleIds,
        IReadOnlyCollection<Guid> activeAvatarScaleEffectRuleIds,
        IReadOnlyCollection<Guid> queuedAvatarScaleRuleIds,
        bool masterRewardEnabled,
        bool masterRewardUnlocked,
        bool freeChildRewardSlotsWhenLocked)
    {
        var scaleStatus = bridgeCoordinator.GetAvatarScaleRuntimeStatus();
        var isHiddenAtRelativeLimit = IsAvatarScaleRuleInactiveAtRelativeLimit(
            rule,
            scaleStatus,
            GetAvatarScaleLimitInactiveState(rule.Id));
        var isOnLocalCooldown = cooldownRuleIds.Contains(rule.Id);
        var isTemporarilyDisabledByPairing = temporarilyDisabledRuleIds.Contains(rule.Id);
        var isActiveScaleEffect = activeAvatarScaleEffectRuleIds.Contains(rule.Id);
        var isQueuedScaleRedeem = queuedAvatarScaleRuleIds.Contains(rule.Id);
        var isHiddenByMasterLock = masterRewardEnabled
            && !masterRewardUnlocked;
        var masterGateAllowsReward = !masterRewardEnabled
            || masterRewardUnlocked;
        var desiredEnabled = allowManagedRewardActivation
            && rule.IsEnabled
            && IsRuntimeReadyAvatarScaleRule(rule)
            && !isHiddenAtRelativeLimit
            && !isTemporarilyDisabledByPairing
            && masterGateAllowsReward;
        var backgroundColor = ManagedRewardPresentation.NormalizeReadyBackgroundColor(
            isOnLocalCooldown ? rule.ManagedRewardCooldownColor : rule.ManagedRewardReadyColor);
        var shouldDeleteWhenInactive = isHiddenByMasterLock
            ? freeChildRewardSlotsWhenLocked
            : rule.DeleteManagedRewardWhenInactive;

        return new ManagedRewardSyncTarget(
            rule.Id,
            rule.DisplayTitle,
            rule.RewardId,
            GetAvatarScaleManagedRewardTitle(rule),
            ApplyRewardFireSaleDiscount(rule.RewardCost, rule.RewardSyncMode),
            rule.RewardSyncMode,
            GetAvatarScaleEffectiveManagedRewardCooldownSeconds(rule),
            backgroundColor,
            prompt: BuildManagedRewardPrompt(rule.RewardDescription),
            requireUserInput: false,
            desiredEnabled: desiredEnabled,
            isCooldownActive: isOnLocalCooldown,
            deleteWhenInactive: shouldDeleteWhenInactive && !isHiddenAtRelativeLimit && !isOnLocalCooldown && !isActiveScaleEffect && !isQueuedScaleRedeem && !isTemporarilyDisabledByPairing,
            protectFromCapReclaim: desiredEnabled || isHiddenAtRelativeLimit || isOnLocalCooldown || isActiveScaleEffect || isQueuedScaleRedeem || isTemporarilyDisabledByPairing,
            applyRewardId: rewardId => rule.RewardId = rewardId);
    }

    private bool? GetAvatarScaleLimitInactiveState(Guid ruleId)
    {
        lock (avatarScaleLimitStateGate)
        {
            return avatarScaleLimitInactiveStateByRuleId.TryGetValue(ruleId, out var isInactiveAtLimit)
                ? isInactiveAtLimit
                : null;
        }
    }

    private ManagedRewardSyncTarget? CreateManagedRewardTargetForRewardFireSaleFundingReward(
        bool allowManagedRewardActivation)
    {
        var fireSale = Settings.RewardFireSale;
        if (!fireSale.FundingRewardEnabled && string.IsNullOrWhiteSpace(fireSale.FundingRewardId))
        {
            return null;
        }

        var desiredEnabled = allowManagedRewardActivation
            && fireSale.IsEnabled
            && fireSale.FundingRewardEnabled
            && (!IsRewardFireSaleActiveNow() || CanRewardFireSaleAdvanceToLaterTier());
        var isFundingRewardOnCooldown = IsRewardFireSaleFundingRewardOnCooldown();
        var backgroundColor = ManagedRewardPresentation.NormalizeReadyBackgroundColor(fireSale.FundingRewardReadyColor);

        return new ManagedRewardSyncTarget(
            RewardFireSaleFundingRewardOwnerId,
            T("Fire Sale Funding Reward"),
            fireSale.FundingRewardId,
            fireSale.FundingRewardTitle,
            fireSale.FundingRewardCost,
            TwitchRewardSyncMode.CreateOrManage,
            cooldownSeconds: fireSale.FundingRewardCooldownSeconds,
            backgroundColor: backgroundColor,
            prompt: BuildRewardFireSaleFundingRewardPrompt(),
            requireUserInput: false,
            desiredEnabled: desiredEnabled,
            isCooldownActive: isFundingRewardOnCooldown,
            deleteWhenInactive: false,
            protectFromCapReclaim: desiredEnabled || fireSale.IsSaleActive || isFundingRewardOnCooldown,
            applyRewardId: rewardId => fireSale.FundingRewardId = rewardId);
    }

    private static string GetAvatarScaleManagedRewardTitle(AvatarScaleRule rule)
    {
        var rewardTitle = rule.RewardTitle?.Trim() ?? string.Empty;
        if (!string.IsNullOrWhiteSpace(rewardTitle))
        {
            return rewardTitle;
        }

        var displayTitle = rule.DisplayTitle?.Trim() ?? string.Empty;
        return string.IsNullOrWhiteSpace(displayTitle) ? "Avatar Scale" : displayTitle;
    }

    private static int GetAvatarScaleEffectiveManagedRewardCooldownSeconds(AvatarScaleRule rule)
    {
        return rule.RewardSyncMode == TwitchRewardSyncMode.CreateOrManage
            ? Math.Max(0, rule.CooldownSeconds)
            : 0;
    }

    private static int GetAvatarScaleEffectDurationSeconds(AvatarScaleRule rule)
    {
        var transitionSeconds = rule.ScaleMode == AvatarScaleMode.GlitchyRandomHeight
            ? Math.Clamp(rule.GlitchyRandomHeightTransitionSeconds, 0, 30)
            : Math.Max(0, rule.SmoothTransitionSeconds);
        var activeSeconds = Math.Max(0, rule.ActiveTimeSeconds);
        var restoreTransitionSeconds = activeSeconds > 0 && rule.RestoreMode != AvatarScaleRestoreMode.None
            ? Math.Max(0, rule.SmoothTransitionSeconds)
            : 0;
        return (int)Math.Ceiling(transitionSeconds + activeSeconds + restoreTransitionSeconds);
    }

    private async Task EnsureCurrentAvatarParametersReadyForUniversalRewardSyncAsync(
        IReadOnlyCollection<UniversalTriggerRule> managedUniversalTriggers,
        string currentAvatarId,
        CancellationToken cancellationToken)
    {
        if (managedUniversalTriggers.Count == 0
            || !managedUniversalTriggers.Any(HasUniversalTriggerAvatarParameterGate))
        {
            return;
        }

        var normalizedAvatarId = currentAvatarId?.Trim() ?? string.Empty;
        var resolvedUserId = ResolveCurrentUserIdForCache();
        if (string.IsNullOrWhiteSpace(resolvedUserId)
            || string.IsNullOrWhiteSpace(normalizedAvatarId))
        {
            return;
        }

        var avatarFolderPath = VrChatLocalOscCacheService.GetAvatarOscFolderPath(resolvedUserId);
        var avatarFilePath = VrChatLocalOscCacheService.GetAvatarOscFilePath(resolvedUserId, normalizedAvatarId);
        var avatarFolderExists = !string.IsNullOrWhiteSpace(avatarFolderPath) && Directory.Exists(avatarFolderPath);
        var avatarFileExists = avatarFolderExists
            && !string.IsNullOrWhiteSpace(avatarFilePath)
            && File.Exists(avatarFilePath);

        if (avatarFileExists)
        {
            var localWriteTimeUtc = File.GetLastWriteTimeUtc(avatarFilePath);
            var hasCachedParameters = cachedVrChatParametersByAvatarId.TryGetValue(normalizedAvatarId, out var cachedParameters);
            var hasImportedLocalWriteTime = vrChatLocalOscAvatarWriteTimes.TryGetValue(normalizedAvatarId, out var importedWriteTimeUtc);
            var shouldRefreshFromLocal = !hasImportedLocalWriteTime
                || localWriteTimeUtc > importedWriteTimeUtc
                || !hasCachedParameters
                || cachedParameters is null
                || cachedParameters.Count == 0;

            if (!shouldRefreshFromLocal)
            {
                return;
            }

            try
            {
                var localParameters = await vrChatLocalOscCacheService.LoadAvatarParametersAsync(
                    resolvedUserId,
                    normalizedAvatarId,
                    cancellationToken);

                await CacheVrChatOscParametersForAvatarAsync(
                    normalizedAvatarId,
                    localParameters,
                    cancellationToken,
                    queueManagedRewardSync: false);
                vrChatLocalOscAvatarWriteTimes[normalizedAvatarId] = localWriteTimeUtc;
                RunOnUi(RefreshAvatarParameterOptions);
            }
            catch (Exception ex)
            {
                if (ex is OperationCanceledException && cancellationToken.IsCancellationRequested)
                {
                    throw;
                }

                RunOnUi(() => AppendThrottledLog(
                    $"universal-avatar-parameter-preflight:{normalizedAvatarId}",
                    $"Could not read the current avatar's local OSC parameter file yet. Universal rewards that need avatar parameters will stay hidden until VRChat writes a readable file: {ex.Message}",
                    ThrottledRewardSyncLogWindow));
            }

            return;
        }

        if (avatarFolderExists)
        {
            var hadParameters = cachedVrChatParametersByAvatarId.TryGetValue(normalizedAvatarId, out var cachedParameters)
                && cachedParameters.Count > 0;
            vrChatLocalOscAvatarWriteTimes.TryRemove(normalizedAvatarId, out _);

            if (hadParameters || !cachedVrChatParametersByAvatarId.ContainsKey(normalizedAvatarId))
            {
                await CacheVrChatOscParametersForAvatarAsync(
                    normalizedAvatarId,
                    [],
                    cancellationToken,
                    queueManagedRewardSync: false);
                RunOnUi(RefreshAvatarParameterOptions);
            }

            RunOnUi(() => AppendThrottledLog(
                $"universal-avatar-osc-file-missing:{normalizedAvatarId}",
                "Crystal Relay could not find the current avatar's local OSC parameter file. Universal Twitch rewards that need avatar parameters will stay hidden until VRChat writes that file.",
                ThrottledRewardSyncLogWindow));
            return;
        }

        vrChatLocalOscAvatarWriteTimes.TryRemove(normalizedAvatarId, out _);
        if (!cachedVrChatParametersByAvatarId.TryGetValue(normalizedAvatarId, out var existingParameters)
            || existingParameters.Count > 0)
        {
            await CacheVrChatOscParametersForAvatarAsync(
                normalizedAvatarId,
                [],
                cancellationToken,
                queueManagedRewardSync: false);
            RunOnUi(RefreshAvatarParameterOptions);
        }

        RunOnUi(() => AppendThrottledLog(
            $"universal-avatar-osc-folder-missing:{normalizedAvatarId}",
            "Crystal Relay could not find VRChat's local OSC avatar folder. Universal Twitch rewards that need avatar parameters will stay hidden until VRChat writes the current avatar JSON.",
            ThrottledRewardSyncLogWindow));
    }

    // Full managed-reward reconciliation pass.
    // Crystal Relay compares local rules against Twitch custom rewards and then creates,
    // updates, disables, or removes rewards so Twitch matches the current rule state.
    private async Task<ManagedRewardSyncOutcome> SynchronizeManagedChannelPointRewardsAsync(
        CancellationToken cancellationToken,
        bool? forcedManagedRewardActivation = null,
        ManagedRewardSyncReason reason = ManagedRewardSyncReason.SettingsEdit)
    {
        await ReloadRuntimeConfigAsync();
        RunOnUi(ExpireRewardFireSaleIfNeeded);

        var managedUniversalTriggers = Settings.UniversalTriggers
            .Where(IsManagedUniversalChannelPointTrigger)
            .Where(trigger => trigger.IsConfigured || !string.IsNullOrWhiteSpace(trigger.RewardId))
            .ToArray();
        var managedAvatarScaleRules = GetAllAvatarScaleRules()
            .Where(IsManagedAvatarScaleChannelPointRule)
            .ToArray();
        var managedAvatarScaleMasterReward = IsManagedAvatarScaleMasterReward(Settings.AvatarScaleMasterReward)
            ? Settings.AvatarScaleMasterReward
            : null;
        await RefreshCurrentAvatarStateForManagedRewardSyncAsync(cancellationToken);

        if (managedUniversalTriggers.Length == 0)
        {
            SetUniversalManagedRewardSyncStatus("No Universal channel-point rewards are configured.");
        }

        if (!HasRecoverableBroadcasterSession)
        {
            SetUniversalManagedRewardSyncStatus("Universal Twitch reward sync skipped because the broadcaster account is disconnected.");
            return ManagedRewardSyncOutcome.Completed;
        }

        if ((string.IsNullOrWhiteSpace(Settings.Broadcaster.AccessToken)
                && string.IsNullOrWhiteSpace(Settings.Broadcaster.RefreshToken))
            || string.IsNullOrWhiteSpace(runtimeConfig.TwitchClientId))
        {
            SetUniversalManagedRewardSyncStatus("Universal Twitch reward sync skipped because the broadcaster login is incomplete.");
            return ManagedRewardSyncOutcome.Completed;
        }

        if (BroadcasterRewardManagementScopeKnownMissing)
        {
            ReportBroadcasterRewardManagementScopeMissing(
                "Universal Twitch reward sync skipped because the broadcaster login is missing channel-point reward management permission.",
                "managed-rewards-missing-scope:sync",
                clearRewardOptions: false);
            return ManagedRewardSyncOutcome.BroadcasterRewardManagementScopeMissing;
        }

        if (broadcasterManagedRewardsUnavailableForSession)
        {
            SetUniversalManagedRewardSyncStatus("Universal Twitch reward sync skipped because Twitch channel-point reward management is unavailable for this broadcaster session.");
            return ManagedRewardSyncOutcome.BroadcasterCustomRewardsUnavailable;
        }

        ManagedRewardSyncCatalog? rewardCatalog = null;
        isSynchronizingManagedRewards = true;

        try
        {
            var apiCalls = new ManagedRewardApiCallCounter();
            var temporarilyDisabledRuleIds = bridgeCoordinator.GetTemporarilyDisabledRuleIds();
            var cooldownRuleIds = bridgeCoordinator.GetRulesOnCooldownIds();
            var activeTimedRuleIds = bridgeCoordinator.GetActiveTimedRuleIds();
            var activeFloatBoostMaximumReachedRuleIds = bridgeCoordinator.GetActiveFloatBoostMaximumReachedRuleIds();
            var activeFloatLimitReachedRuleIds = bridgeCoordinator.GetActiveFloatLimitReachedRuleIds();
            var activeAvatarScaleEffectRuleIds = bridgeCoordinator.GetActiveAvatarScaleEffectRuleIds();
            var queuedAvatarScaleRuleIds = bridgeCoordinator.GetQueuedAvatarScaleRuleIds();
            var avatarChangeTransitionActive = bridgeCoordinator.IsAvatarChangeTransitionActive();
            var masterRewardUnlocked = bridgeCoordinator.IsAvatarScaleMasterUnlockActive();
            var masterRewardInCooldown = bridgeCoordinator.IsAvatarScaleMasterRewardOnCooldown();
            var allowManagedRewardActivation = forcedManagedRewardActivation
                ?? ((IsBroadcasterLive || Settings.ChannelPointRewardTestModeEnabled)
                    && !Settings.EmergencyRedeemStopEnabled);
            var supportedMovementRules = GetAllMovementRules()
                .Where(IsSupportedMovementRule)
                .ToArray();
            var currentAvatarId = GetManagedRewardActivationAvatarId();
            await EnsureCurrentAvatarParametersReadyForUniversalRewardSyncAsync(
                managedUniversalTriggers,
                currentAvatarId,
                cancellationToken);
            if (managedUniversalTriggers.Length > 0)
            {
                var unlinkedUniversalCount = managedUniversalTriggers.Count(trigger => string.IsNullOrWhiteSpace(trigger.RewardId));
                SetUniversalManagedRewardSyncStatus(unlinkedUniversalCount > 0
                    ? $"Universal Twitch reward sync is checking {managedUniversalTriggers.Length} reward(s); {unlinkedUniversalCount} unlinked reward(s) will be created, adopted, or wait for an existing reward link based on their reward source. {GetUniversalRewardActivationReason(forcedManagedRewardActivation, allowManagedRewardActivation)}"
                    : $"Universal Twitch reward sync is checking {managedUniversalTriggers.Length} linked reward(s). {GetUniversalRewardActivationReason(forcedManagedRewardActivation, allowManagedRewardActivation)}");
            }

            // Build once-per-sync ownership indexes up front. The reconciliation pass uses
            // them to adopt, recycle, and retire rewards without repeatedly rescanning the
            // full rule list and reward list inside each per-rule operation.
            var ownershipEntries = EnumerateManagedRewardOwnershipEntries(
                    supportedMovementRules,
                    managedUniversalTriggers,
                    managedAvatarScaleRules,
                    managedAvatarScaleMasterReward)
                .ToArray();
            var claimedRewardIds = ownershipEntries
                .Select(entry => entry.RewardId?.Trim())
                .Where(rewardId => !string.IsNullOrWhiteSpace(rewardId))
                .Cast<string>()
                .ToHashSet(StringComparer.Ordinal);
            var desiredManagedRewardTitleKeys = ownershipEntries
                .Where(entry => entry.RewardSyncMode == TwitchRewardSyncMode.CreateOrManage)
                .Select(entry => ManagedRewardPresentation.NormalizeTitleIdentityKey(entry.RewardTitle))
                .Where(titleKey => !string.IsNullOrWhiteSpace(titleKey))
                .ToHashSet(StringComparer.OrdinalIgnoreCase);
            var ownershipIndex = new ManagedRewardRuleOwnershipIndex(ownershipEntries);

            var avatarScaleMasterTarget = managedAvatarScaleMasterReward is null
                ? null
                : CreateManagedRewardTargetForAvatarScaleMasterReward(
                    managedAvatarScaleMasterReward,
                    allowManagedRewardActivation,
                    masterRewardUnlocked || masterRewardInCooldown,
                    masterRewardInCooldown);
            var avatarProfileTargets = new List<ManagedRewardSyncTarget>();
            foreach (var profile in Settings.AvatarProfiles)
            {
                var synchronizedSharedRuleIds = new HashSet<Guid>();
                foreach (var group in EnumerateSharedAvatarSetRewardGroups(profile))
                {
                    foreach (var rule in group.Rules)
                    {
                        synchronizedSharedRuleIds.Add(rule.Id);
                    }

                    avatarProfileTargets.Add(CreateManagedRewardTargetForSharedAvatarSetRewardGroup(
                        profile,
                        group,
                        currentAvatarId,
                        avatarChangeTransitionActive,
                        allowManagedRewardActivation,
                        temporarilyDisabledRuleIds,
                        cooldownRuleIds,
                        activeTimedRuleIds));
                    foreach (var rule in group.Rules)
                    {
                        if (CreateManagedRewardTargetForActiveFloatBoostReward(
                                profile,
                                rule,
                                currentAvatarId,
                                avatarChangeTransitionActive,
                                allowManagedRewardActivation,
                                temporarilyDisabledRuleIds,
                                activeTimedRuleIds,
                                activeFloatBoostMaximumReachedRuleIds) is { } boostTarget)
                        {
                            avatarProfileTargets.Add(boostTarget);
                        }
                    }
                }

                foreach (var rule in profile.ChannelPointRules)
                {
                    if (synchronizedSharedRuleIds.Contains(rule.Id))
                    {
                        continue;
                    }

                    avatarProfileTargets.Add(CreateManagedRewardTargetForRule(
                        profile,
                        rule,
                        currentAvatarId,
                        avatarChangeTransitionActive,
                        allowManagedRewardActivation,
                        temporarilyDisabledRuleIds,
                        cooldownRuleIds,
                        activeTimedRuleIds,
                        activeFloatLimitReachedRuleIds));
                    if (CreateManagedRewardTargetForActiveFloatBoostReward(
                            profile,
                            rule,
                            currentAvatarId,
                            avatarChangeTransitionActive,
                            allowManagedRewardActivation,
                            temporarilyDisabledRuleIds,
                            activeTimedRuleIds,
                            activeFloatBoostMaximumReachedRuleIds) is { } boostTarget)
                    {
                        avatarProfileTargets.Add(boostTarget);
                    }
                }

                foreach (var outfit in profile.WardrobeOutfits)
                {
                    if (CreateManagedRewardTargetForWardrobeOutfit(
                            profile,
                            outfit,
                            allowManagedRewardActivation) is { } outfitTarget)
                    {
                        avatarProfileTargets.Add(outfitTarget);
                    }
                }

                if (CreateManagedRewardTargetForWardrobeMasterReward(
                        profile,
                        allowManagedRewardActivation) is { } masterTarget)
                {
                    avatarProfileTargets.Add(masterTarget);
                }
            }

            var avatarSwapReturnAvatarId = !string.IsNullOrWhiteSpace(Settings.MasterAvatarSwapReturnId)
                ? Settings.MasterAvatarSwapReturnId.Trim()
                : (MasterAvatarProfile?.AvatarId?.Trim() ?? string.Empty);
            var avatarSwapTargets = new List<ManagedRewardSyncTarget>();
            foreach (var swapProfile in Settings.AvatarSwapProfiles)
            {
                foreach (var rule in swapProfile.ChannelPointRules)
                {
                    avatarSwapTargets.Add(CreateManagedRewardTargetForAvatarSwapRule(
                        swapProfile,
                        rule,
                        currentAvatarId,
                        avatarSwapReturnAvatarId,
                        avatarChangeTransitionActive,
                        allowManagedRewardActivation,
                        temporarilyDisabledRuleIds,
                        cooldownRuleIds,
                        activeTimedRuleIds,
                        activeFloatLimitReachedRuleIds));
                }
            }

            var rouletteTargets = new List<ManagedRewardSyncTarget>();
            foreach (var rouletteProfile in Settings.AvatarRouletteProfiles)
            {
                foreach (var rule in rouletteProfile.Triggers.Where(t => t.TriggerType == TwitchTriggerType.ChannelPoints && t.RewardSyncMode != TwitchRewardSyncMode.LinkExisting))
                {
                    rouletteTargets.Add(CreateManagedRewardTargetForRouletteRule(
                        rouletteProfile,
                        rule,
                        currentAvatarId,
                        avatarChangeTransitionActive,
                        allowManagedRewardActivation,
                        temporarilyDisabledRuleIds,
                        cooldownRuleIds,
                        activeTimedRuleIds,
                        activeFloatLimitReachedRuleIds));
                }
            }

            var movementTargets = supportedMovementRules
                .Select(rule => CreateManagedRewardTargetForRule(
                    profile: null,
                    rule,
                    currentAvatarId,
                    avatarChangeTransitionActive,
                    allowManagedRewardActivation,
                    temporarilyDisabledRuleIds,
                    cooldownRuleIds,
                    activeTimedRuleIds,
                    activeFloatLimitReachedRuleIds))
                .ToArray();
            var universalTargets = managedUniversalTriggers
                .Select(trigger => CreateManagedRewardTargetForUniversalTrigger(
                    trigger,
                    allowManagedRewardActivation,
                    currentAvatarId))
                .Where(target => target is not null)
                .Cast<ManagedRewardSyncTarget>()
                .ToArray();
            var avatarScaleTargets = managedAvatarScaleRules
                .Select(rule => CreateManagedRewardTargetForAvatarScaleRule(
                    rule,
                    allowManagedRewardActivation,
                    cooldownRuleIds,
                    temporarilyDisabledRuleIds,
                    activeAvatarScaleEffectRuleIds,
                    queuedAvatarScaleRuleIds,
                    Settings.AvatarScaleMasterReward.IsEnabled,
                    masterRewardUnlocked,
                    Settings.AvatarScaleMasterReward.FreeChildRewardSlotsWhenLocked))
                .ToArray();
            var rewardFireSaleFundingTarget = CreateManagedRewardTargetForRewardFireSaleFundingReward(allowManagedRewardActivation);
            var allSyncTargets = new List<ManagedRewardSyncTarget>();
            if (avatarScaleMasterTarget is not null)
            {
                allSyncTargets.Add(avatarScaleMasterTarget);
            }

            allSyncTargets.AddRange(avatarProfileTargets);
            allSyncTargets.AddRange(avatarSwapTargets);
            allSyncTargets.AddRange(rouletteTargets);
            allSyncTargets.AddRange(movementTargets);
            allSyncTargets.AddRange(universalTargets);
            allSyncTargets.AddRange(avatarScaleTargets);
            if (rewardFireSaleFundingTarget is not null)
            {
                allSyncTargets.Add(rewardFireSaleFundingTarget);
            }

            var capReclaimProtectedRewardIds = BuildManagedRewardCapReclaimProtectedRewardIds(allSyncTargets);
            var capReclaimProtectedTitleKeys = BuildManagedRewardCapReclaimProtectedTitleKeys(allSyncTargets);
            var hasCreateOrManageSyncWork = allSyncTargets.Any(target => !target.UsesLinkedExistingReward)
                || retiredManagedRewardIds.Count > 0;
            var desiredFingerprint = BuildManagedRewardDesiredFingerprint(
                allSyncTargets,
                retiredManagedRewardIds,
                currentAvatarId,
                allowManagedRewardActivation,
                forcedManagedRewardActivation,
                Settings.UseManagedRewardTitlePrefix);
            if (ShouldSkipUninitializedPassiveManagedRewardSync(
                    reason,
                    allowManagedRewardActivation,
                    lastSuccessfulManagedRewardDesiredFingerprint))
            {
                RunOnUi(() => AppendThrottledLog(
                    $"managed-rewards-skip-uninitialized-passive:{reason}",
                    $"Skipped Twitch reward API sync for {DescribeManagedRewardSyncReason(reason)} because rewards are not visible and Crystal Relay has not completed a deliberate reward sync baseline yet.",
                    ThrottledRewardSyncLogWindow));
                return ManagedRewardSyncOutcome.Completed;
            }

            if (!hasCreateOrManageSyncWork)
            {
                RunOnUi(() => AppendThrottledLog(
                    $"managed-rewards-skip-linked-only:{reason}",
                    $"Skipped Twitch reward API sync for {DescribeManagedRewardSyncReason(reason)} because only linked/listen-only rewards are configured.",
                    ThrottledRewardSyncLogWindow));
                return ManagedRewardSyncOutcome.Completed;
            }

            if (ShouldSkipUnchangedManagedRewardSync(reason)
                && string.Equals(lastSuccessfulManagedRewardDesiredFingerprint, desiredFingerprint, StringComparison.Ordinal))
            {
                RunOnUi(() => AppendThrottledLog(
                    $"managed-rewards-skip-unchanged:{reason}",
                    $"Skipped Twitch reward API sync for {DescribeManagedRewardSyncReason(reason)} because desired reward state has not changed.",
                    ThrottledRewardSyncLogWindow));
                return ManagedRewardSyncOutcome.Completed;
            }

            apiCalls.CatalogReads++;
            rewardCatalog = new ManagedRewardSyncCatalog(await twitchApiClient.GetCustomRewardsAsync(
                Settings.Broadcaster.AccessToken,
                runtimeConfig.TwitchClientId,
                Settings.Broadcaster.UserId,
                cancellationToken));
            ManagedRewardSyncCatalog? manageableRewardCatalog = null;
            async Task<ManagedRewardSyncCatalog> GetManageableRewardCatalogAsync(CancellationToken requestCancellationToken)
            {
                if (manageableRewardCatalog is not null)
                {
                    return manageableRewardCatalog;
                }

                apiCalls.ManageableCatalogReads++;
                manageableRewardCatalog = new ManagedRewardSyncCatalog(await twitchApiClient.GetCustomRewardsAsync(
                    Settings.Broadcaster.AccessToken,
                    runtimeConfig.TwitchClientId,
                    Settings.Broadcaster.UserId,
                    requestCancellationToken,
                    onlyManageableRewards: true));
                return manageableRewardCatalog;
            }

            var changesMade = false;
            if (retiredManagedRewardIds.Count > 0)
            {
                foreach (var retiredRewardId in retiredManagedRewardIds.ToArray())
                {
                    if (!rewardCatalog.TryGetById(retiredRewardId, out var existingRetiredReward))
                    {
                        retiredManagedRewardIds.Remove(retiredRewardId);
                        continue;
                    }

                    try
                    {
                        apiCalls.Deletes++;
                        await twitchApiClient.DeleteCustomRewardAsync(
                            Settings.Broadcaster.AccessToken,
                            runtimeConfig.TwitchClientId,
                            Settings.Broadcaster.UserId,
                            existingRetiredReward.Id,
                            cancellationToken);
                        rewardCatalog.Remove(existingRetiredReward.Id);
                        changesMade = true;
                    }
                    catch (TwitchApiException ex) when (ex.StatusCode == System.Net.HttpStatusCode.NotFound)
                    {
                        rewardCatalog.Remove(existingRetiredReward.Id);
                    }

                    retiredManagedRewardIds.Remove(retiredRewardId);
                }
            }

            var allowInactiveRewardDeletion = AllowsInactiveManagedRewardDeletion(reason);
            var allowMissingManagedRewardMaterialization = ShouldAllowMissingManagedRewardMaterialization(
                reason,
                allowManagedRewardActivation);
            changesMade |= await CleanupStaleManagedRewardsAsync(
                rewardCatalog,
                claimedRewardIds,
                desiredManagedRewardTitleKeys,
                apiCalls,
                allowInactiveRewardDeletion,
                reason,
                cancellationToken);

            pendingSkippedDeleteSuppressedCount = 0;
            foreach (var target in allSyncTargets)
            {
                changesMade |= await SynchronizeManagedRewardForTargetAsync(
                    target,
                    allSyncTargets,
                    rewardCatalog,
                    claimedRewardIds,
                    desiredManagedRewardTitleKeys,
                    capReclaimProtectedRewardIds,
                    capReclaimProtectedTitleKeys,
                    GetManageableRewardCatalogAsync,
                    ownershipIndex,
                    apiCalls,
                    allowInactiveRewardDeletion,
                    allowMissingManagedRewardMaterialization,
                    reason,
                    cancellationToken);
            }

            if (pendingSkippedDeleteSuppressedCount > 0)
            {
                var suppressedCount = pendingSkippedDeleteSuppressedCount;
                pendingSkippedDeleteSuppressedCount = 0;
                RunOnUi(() => AppendThrottledLog(
                    $"managed-reward-delete-suppressed-summary:{reason}",
                    $"Skipped deleting {suppressedCount} inactive Twitch reward(s) during {DescribeManagedRewardSyncReason(reason)} to avoid reward API churn. Crystal Relay only deletes opted-in inactive rewards during explicit cleanup/maintenance or direct rule removal.",
                    ThrottledRewardSyncLogWindow));
            }

            RunOnUi(() => ApplyRewardCatalog(rewardCatalog.Rewards));
            lastSuccessfulManagedRewardDesiredFingerprint = BuildManagedRewardDesiredFingerprint(
                allSyncTargets,
                retiredManagedRewardIds,
                currentAvatarId,
                allowManagedRewardActivation,
                forcedManagedRewardActivation,
                Settings.UseManagedRewardTitlePrefix);
            RunOnUi(() => AppendThrottledLog(
                $"managed-rewards-api-count:{reason}",
                $"Twitch reward sync for {DescribeManagedRewardSyncReason(reason)} used {apiCalls.Describe()} API call(s).",
                ThrottledRewardSyncLogWindow));
            if (managedUniversalTriggers.Length > 0)
            {
                var enabledUniversalCount = universalTargets.Count(target => target.DesiredEnabled);
                var avatarHiddenUniversalCount = managedUniversalTriggers.Count(trigger =>
                    trigger.IsEnabled
                    && HasRuntimeReadyUniversalTriggerAction(trigger)
                    && !IsUniversalTriggerReadyForCurrentAvatarJson(trigger, currentAvatarId));
                var stillUnlinkedUniversalCount = managedUniversalTriggers.Count(trigger => string.IsNullOrWhiteSpace(trigger.RewardId));
                var avatarHiddenText = avatarHiddenUniversalCount > 0
                    ? $" {avatarHiddenUniversalCount} hidden by current-avatar parameter check."
                    : string.Empty;
                SetUniversalManagedRewardSyncStatus(stillUnlinkedUniversalCount > 0
                    ? $"Last Twitch sync checked {managedUniversalTriggers.Length} Universal reward(s); {enabledUniversalCount} set visible.{avatarHiddenText} {stillUnlinkedUniversalCount} still need a Twitch link. {GetUniversalRewardActivationReason(forcedManagedRewardActivation, allowManagedRewardActivation)}"
                    : $"Last Twitch sync checked {managedUniversalTriggers.Length} Universal reward(s); {enabledUniversalCount} set visible.{avatarHiddenText} {GetUniversalRewardActivationReason(forcedManagedRewardActivation, allowManagedRewardActivation)}");
            }

            if (changesMade && !isShuttingDown)
            {
                QueueSave(0);
            }

            return ManagedRewardSyncOutcome.Completed;
        }
        catch (Exception ex)
        {
            if (ex is OperationCanceledException && cancellationToken.IsCancellationRequested)
            {
                throw;
            }

            if (IsBroadcasterRewardEligibilityFailure(ex))
            {
                MarkBroadcasterManagedRewardsUnavailableForSession();
                return ManagedRewardSyncOutcome.BroadcasterCustomRewardsUnavailable;
            }

            if (IsBroadcasterRewardManagementScopeFailure(ex))
            {
                ReportBroadcasterRewardManagementScopeMissing(
                    "Universal Twitch reward sync skipped because the broadcaster login is missing channel-point reward management permission.",
                    "managed-rewards-missing-scope:sync",
                    clearRewardOptions: false);
                return ManagedRewardSyncOutcome.BroadcasterRewardManagementScopeMissing;
            }

            if (TryApplyManagedRewardApiBackoff(ex, $"Twitch reward sync ({DescribeManagedRewardSyncReason(reason)})"))
            {
                return ManagedRewardSyncOutcome.Completed;
            }

            if (IsInvalidBroadcasterTokenFailure(ex))
            {
                return ManagedRewardSyncOutcome.BroadcasterTokenRefreshRequired;
            }

            throw;
        }
        finally
        {
            isSynchronizingManagedRewards = false;
        }
    }

    private async Task<bool> DisableManagedRewardsForShutdownAsync()
    {
        return await DisableManagedRewardsForMaintenanceAsync(
            timeout: TimeSpan.FromSeconds(6),
            successMessage: "Disabled managed channel point rewards during shutdown.",
            timeoutMessage: "Timed out while disabling managed rewards during shutdown. Crystal Relay will retry on next launch.",
            unsupportedLogKey: "managed-rewards-unavailable:shutdown",
            unsupportedMessage: "Crystal Relay skipped shutdown reward cleanup because this broadcaster account cannot use Twitch channel point rewards.");
    }

    private async Task<bool> DisableManagedRewardsForRecoveryAsync()
    {
        return await DisableManagedRewardsForMaintenanceAsync(
            timeout: TimeSpan.FromSeconds(5),
            successMessage: "Recovery cleanup disabled managed channel point rewards from the previous session.",
            timeoutMessage: "Recovery reward cleanup timed out.",
            unsupportedLogKey: "managed-rewards-unavailable:recovery",
            unsupportedMessage: "Crystal Relay could not run reward cleanup because this broadcaster account cannot use Twitch channel point rewards.");
    }

    private async Task<bool> DisableManagedRewardsForMaintenanceAsync(
        TimeSpan timeout,
        string successMessage,
        string timeoutMessage,
        string unsupportedLogKey,
        string unsupportedMessage)
    {
        await ReloadRuntimeConfigAsync();

        if (!Settings.Broadcaster.IsConnected
            || string.IsNullOrWhiteSpace(Settings.Broadcaster.AccessToken)
            || string.IsNullOrWhiteSpace(Settings.Broadcaster.UserId)
            || string.IsNullOrWhiteSpace(runtimeConfig.TwitchClientId)
            || BroadcasterRewardManagementScopeKnownMissing)
        {
            return true;
        }

        if (broadcasterManagedRewardsUnavailableForSession)
        {
            AppendThrottledLog(unsupportedLogKey, unsupportedMessage, ThrottledRewardSyncLogWindow);
            return true;
        }

        var lockAcquired = false;
        try
        {
            using var rewardDisableCancellation = new CancellationTokenSource(timeout);
            await managedRewardSyncGate.WaitAsync(rewardDisableCancellation.Token);
            lockAcquired = true;
            var syncOutcome = await SynchronizeManagedChannelPointRewardsAsync(
                rewardDisableCancellation.Token,
                forcedManagedRewardActivation: false,
                reason: ManagedRewardSyncReason.Maintenance);
            if (syncOutcome == ManagedRewardSyncOutcome.BroadcasterCustomRewardsUnavailable)
            {
                AppendThrottledLog(unsupportedLogKey, unsupportedMessage, ThrottledRewardSyncLogWindow);
                return true;
            }
            if (syncOutcome == ManagedRewardSyncOutcome.BroadcasterRewardManagementScopeMissing)
            {
                AppendThrottledLog(
                    $"{unsupportedLogKey}:missing-scope",
                    "Crystal Relay skipped reward cleanup because the broadcaster login is missing channel-point reward management permission. Reconnect the broadcaster account once.",
                    ThrottledRewardSyncLogWindow);
                return true;
            }
            if (syncOutcome == ManagedRewardSyncOutcome.BroadcasterTokenRefreshRequired)
            {
                AppendThrottledLog(
                    $"{unsupportedLogKey}:token-refresh",
                    "Crystal Relay skipped reward cleanup because the broadcaster login needs to refresh first. The app will keep launching normally and try again after the broadcaster session updates.",
                    ThrottledRewardSyncLogWindow);
                return true;
            }

            AppendLog(successMessage);
            return true;
        }
        catch (OperationCanceledException)
        {
            AppendLog(timeoutMessage);
            return false;
        }
        finally
        {
            if (lockAcquired)
            {
                managedRewardSyncGate.Release();
            }
        }
    }

    private async Task<bool> SynchronizeManagedRewardForTargetAsync(
        ManagedRewardSyncTarget target,
        IReadOnlyCollection<ManagedRewardSyncTarget> allSyncTargets,
        ManagedRewardSyncCatalog rewardCatalog,
        HashSet<string> claimedRewardIds,
        IReadOnlyCollection<string> desiredManagedRewardTitleKeys,
        IReadOnlyCollection<string> capReclaimProtectedRewardIds,
        IReadOnlyCollection<string> capReclaimProtectedTitleKeys,
        Func<CancellationToken, Task<ManagedRewardSyncCatalog>> getManageableRewardCatalogAsync,
        ManagedRewardRuleOwnershipIndex ownershipIndex,
        ManagedRewardApiCallCounter apiCalls,
        bool allowInactiveRewardDeletion,
        bool allowMissingManagedRewardMaterialization,
        ManagedRewardSyncReason reason,
        CancellationToken cancellationToken)
    {
        var rewardTitle = ManagedRewardPresentation.StripPrefix(target.RewardTitle);
        var managedRewardTitle = ManagedRewardPresentation.BuildTitle(rewardTitle, Settings.UseManagedRewardTitlePrefix);
        var rewardCost = Math.Max(1, target.RewardCost);
        var rewardCooldownSeconds = Math.Max(0, target.CooldownSeconds);
        var rewardBackgroundColor = target.BackgroundColor;
        var rewardPrompt = NormalizeManagedRewardPrompt(target.Prompt);
        var requireUserInput = target.RequireUserInput;
        var desiredEnabled = target.DesiredEnabled;
        var rewardId = target.RewardId?.Trim() ?? string.Empty;
        var existingReward = !string.IsNullOrWhiteSpace(rewardId) && rewardCatalog.TryGetById(rewardId, out var matchedReward)
            ? matchedReward
            : null;
        var changed = false;
        if (existingReward is not null)
        {
            RunOnUi(() => AppendThrottledLog(
                $"managed-reward-existing-id:{target.Id}:{existingReward.Id}",
                $"Reward sync matched existing Twitch reward '{existingReward.Title}' for '{target.DisplayTitle}' by stable reward ID.",
                ThrottledRewardSyncLogWindow));
        }
        else if (!string.IsNullOrWhiteSpace(rewardId))
        {
            RunOnUi(() => AppendThrottledLog(
                $"managed-reward-id-missing:{target.Id}:{rewardId}",
                target.UsesLinkedExistingReward
                    ? $"Linked Twitch reward for '{target.DisplayTitle}' no longer exists or could not be loaded. Crystal Relay will not create a replacement; choose a Twitch reward again."
                    : $"Reward sync could not find saved Twitch reward ID for '{target.DisplayTitle}', so it will try normalized title matching before creating anything.",
                ThrottledRewardSyncLogWindow));
        }

        if (target.UsesLinkedExistingReward)
        {
            return await SynchronizeLinkedExistingRewardForTargetAsync(
                target,
                existingReward,
                rewardCatalog,
                claimedRewardIds,
                cancellationToken);
        }

        var shouldDeleteInactiveReward = target.DeleteWhenInactive
            && (!desiredEnabled || string.IsNullOrWhiteSpace(rewardTitle));
        if (shouldDeleteInactiveReward && !allowInactiveRewardDeletion)
        {
            pendingSkippedDeleteSuppressedCount++;
            DebugLogService.Write($"Skipped deleting inactive Twitch reward for '{target.DisplayTitle}' (ID: {target.Id}) during {DescribeManagedRewardSyncReason(reason)} to avoid reward API churn. Crystal Relay only deletes opted-in inactive rewards during explicit cleanup/maintenance or direct rule removal.");
            if (existingReward is null || string.IsNullOrWhiteSpace(rewardTitle))
            {
                return changed;
            }

            shouldDeleteInactiveReward = false;
        }

        if (shouldDeleteInactiveReward)
        {
            if (existingReward is not null)
            {
                return await DeleteInactiveManagedRewardAsync(
                    target,
                    existingReward,
                    rewardCatalog,
                    ownershipIndex,
                    apiCalls,
                    cancellationToken);
            }

            if (!string.IsNullOrWhiteSpace(target.RewardId))
            {
                changed |= SetManagedRewardTargetId(target, string.Empty, ownershipIndex);
                RunOnUi(() => AppendThrottledLog(
                    $"managed-reward-clear-inactive-id:{target.Id}",
                    $"Reward sync cleared an inactive saved Twitch reward link for '{target.DisplayTitle}' because there was no active reward to delete.",
                    ThrottledRewardSyncLogWindow));
            }

            return changed;
        }

        if (!allowMissingManagedRewardMaterialization
            && existingReward is null
            && !string.IsNullOrWhiteSpace(rewardTitle))
        {
            RunOnUi(() => AppendThrottledLog(
                $"managed-reward-materialize-suppressed:{reason}:{target.Id}",
                $"Skipped creating or adopting Twitch reward for '{target.DisplayTitle}' during {DescribeManagedRewardSyncReason(reason)} because rewards are currently hidden. Crystal Relay will create or adopt it during an intentional settings sync, Test Mode, or live stream sync.",
                ThrottledRewardSyncLogWindow));
            return changed;
        }

        if (reason == ManagedRewardSyncReason.FireSaleChanged
            && existingReward is null
            && !desiredEnabled
            && !string.IsNullOrWhiteSpace(rewardTitle))
        {
            RunOnUi(() => AppendThrottledLog(
                $"managed-reward-fire-sale-materialize-suppressed:{target.Id}",
                $"Reward Fire Sale skipped creating inactive Twitch reward '{target.DisplayTitle}'. It will be discounted when that reward is needed by normal sync.",
                ThrottledRewardSyncLogWindow));
            return changed;
        }

        if (existingReward is null && !string.IsNullOrWhiteSpace(rewardTitle))
        {
            var adoptedReward = await FindAdoptableExistingManagedRewardAsync(
                target.Id,
                rewardTitle,
                rewardCatalog,
                ownershipIndex,
                refreshCatalogIfNeeded: false,
                apiCalls,
                cancellationToken);

            if (adoptedReward is not null)
            {
                changed |= SetManagedRewardTargetId(target, adoptedReward.Id, ownershipIndex);
                claimedRewardIds.Add(adoptedReward.Id);
                existingReward = adoptedReward;
                RunOnUi(() => AppendThrottledLog(
                    $"managed-reward-adopted-title:{target.Id}:{adoptedReward.Id}",
                    $"Reward sync linked '{target.DisplayTitle}' to existing Twitch reward '{adoptedReward.Title}' by normalized title.",
                    ThrottledRewardSyncLogWindow));
            }
        }

        if (existingReward is null && string.IsNullOrWhiteSpace(rewardTitle))
        {
            if (!string.IsNullOrWhiteSpace(target.RewardId))
            {
                changed |= SetManagedRewardTargetId(target, string.Empty, ownershipIndex);
                changed = true;
            }

            RunOnUi(() => AppendThrottledLog(
                $"managed-reward-skip-blank:{target.Id}",
                $"Reward sync skipped '{target.DisplayTitle}' because it has no reward name configured.",
                ThrottledRewardSyncLogWindow));
            return changed;
        }

        if (existingReward is not null && string.IsNullOrWhiteSpace(rewardTitle))
        {
            if (existingReward.IsEnabled)
            {
                apiCalls.Updates++;
                var disabledReward = await twitchApiClient.UpdateCustomRewardAsync(
                    Settings.Broadcaster.AccessToken,
                    runtimeConfig.TwitchClientId,
                        Settings.Broadcaster.UserId,
                        existingReward.Id,
                        existingReward.Title,
                        existingReward.Cost,
                        false,
                        existingReward.IsGlobalCooldownEnabled ? existingReward.GlobalCooldownSeconds ?? 0 : 0,
                        string.IsNullOrWhiteSpace(existingReward.BackgroundColor)
                            ? ManagedRewardPresentation.ReadyBackgroundColor
                            : existingReward.BackgroundColor,
                        cancellationToken,
                        NormalizeManagedRewardPrompt(existingReward.Prompt),
                        existingReward.IsUserInputRequired);
                rewardCatalog.Replace(disabledReward);
                changed = true;
                RunOnUi(() => AppendThrottledLog(
                    $"managed-reward-disabled-blank:{existingReward.Id}",
                    $"Reward sync disabled existing Twitch reward '{existingReward.Title}' for '{target.DisplayTitle}' because its local reward name is blank.",
                    ThrottledRewardSyncLogWindow));
            }

            return changed;
        }

        try
        {
            if (existingReward is null)
            {
                var refreshedAdoptedReward = await FindAdoptableExistingManagedRewardAsync(
                    target.Id,
                    rewardTitle,
                    rewardCatalog,
                    ownershipIndex,
                    refreshCatalogIfNeeded: false,
                    apiCalls,
                    cancellationToken);
                if (refreshedAdoptedReward is not null)
                {
                    changed |= SetManagedRewardTargetId(target, refreshedAdoptedReward.Id, ownershipIndex);
                    claimedRewardIds.Add(refreshedAdoptedReward.Id);
                    existingReward = refreshedAdoptedReward;
                    changed = true;
                    ClearManagedRewardCreateBackoff(managedRewardTitle);
                    RunOnUi(() => AppendThrottledLog(
                        $"managed-reward-adopted:{managedRewardTitle}",
                        $"Crystal Relay found the existing Twitch redeem '{rewardTitle}' and linked '{target.DisplayTitle}' to it.",
                        ThrottledRewardSyncLogWindow));
                }
            }

            if (existingReward is null)
            {
                if (ShouldBackOffManagedRewardCreate(managedRewardTitle))
                {
                    return changed;
                }

                if (rewardCatalog.Rewards.Count >= TwitchCustomRewardLimit)
                {
                    var capRecovery = await TryRecycleManagedRewardForCapacityAsync(
                        target,
                        allSyncTargets,
                        rewardCatalog,
                        claimedRewardIds,
                        capReclaimProtectedRewardIds,
                        capReclaimProtectedTitleKeys,
                        getManageableRewardCatalogAsync,
                        ownershipIndex,
                        apiCalls,
                        managedRewardTitle,
                        rewardCost,
                        desiredEnabled,
                        rewardCooldownSeconds,
                        rewardBackgroundColor,
                        rewardPrompt,
                        requireUserInput,
                        cancellationToken);
                    if (capRecovery.handled)
                    {
                        return changed || capRecovery.changed;
                    }
                }

                TwitchApiClient.CustomRewardResponse createdReward;
                try
                {
                    apiCalls.Creates++;
                    createdReward = await twitchApiClient.CreateCustomRewardAsync(
                        Settings.Broadcaster.AccessToken,
                        runtimeConfig.TwitchClientId,
                        Settings.Broadcaster.UserId,
                        managedRewardTitle,
                        rewardCost,
                        desiredEnabled,
                        rewardCooldownSeconds,
                        rewardBackgroundColor,
                        cancellationToken,
                        rewardPrompt,
                        requireUserInput);
                }
                catch (Exception ex) when (IsManagedRewardCapacityFailure(ex))
                {
                    var capRecovery = await TryRecycleManagedRewardForCapacityAsync(
                        target,
                        allSyncTargets,
                        rewardCatalog,
                        claimedRewardIds,
                        capReclaimProtectedRewardIds,
                        capReclaimProtectedTitleKeys,
                        getManageableRewardCatalogAsync,
                        ownershipIndex,
                        apiCalls,
                        managedRewardTitle,
                        rewardCost,
                        desiredEnabled,
                        rewardCooldownSeconds,
                        rewardBackgroundColor,
                        rewardPrompt,
                        requireUserInput,
                        cancellationToken);
                    if (capRecovery.handled)
                    {
                        return changed || capRecovery.changed;
                    }

                    throw;
                }

                changed |= SetManagedRewardTargetId(target, createdReward.Id, ownershipIndex);
                rewardCatalog.Replace(createdReward);
                claimedRewardIds.Add(createdReward.Id);
                ClearManagedRewardCreateBackoff(managedRewardTitle);
                RunOnUi(() => AppendLog($"Created Twitch reward '{managedRewardTitle}' for '{target.DisplayTitle}'."));                
                return true;
            }

            claimedRewardIds.Add(existingReward.Id);
            ClearManagedRewardCreateBackoff(managedRewardTitle);
            if (!ManagedRewardPresentation.HasSameTitlePresentation(existingReward.Title, managedRewardTitle)
                || existingReward.Cost != rewardCost
                || existingReward.IsEnabled != desiredEnabled
                || existingReward.IsGlobalCooldownEnabled != (rewardCooldownSeconds > 0)
                || (existingReward.IsGlobalCooldownEnabled && (existingReward.GlobalCooldownSeconds ?? 0) != rewardCooldownSeconds)
                || !string.Equals(existingReward.BackgroundColor, rewardBackgroundColor, StringComparison.OrdinalIgnoreCase)
                || !string.Equals(NormalizeManagedRewardPrompt(existingReward.Prompt), rewardPrompt, StringComparison.Ordinal)
                || existingReward.IsUserInputRequired != requireUserInput)
            {
                apiCalls.Updates++;
                var updatedReward = await twitchApiClient.UpdateCustomRewardAsync(
                    Settings.Broadcaster.AccessToken,
                    runtimeConfig.TwitchClientId,
                    Settings.Broadcaster.UserId,
                    existingReward.Id,
                    managedRewardTitle,
                    rewardCost,
                    desiredEnabled,
                    rewardCooldownSeconds,
                    rewardBackgroundColor,
                    cancellationToken,
                    rewardPrompt,
                    requireUserInput);
                rewardCatalog.Replace(updatedReward);
                changed = true;
                RunOnUi(() => AppendThrottledLog(
                    $"managed-reward-updated:{existingReward.Id}",
                    $"Reward sync updated existing Twitch reward '{existingReward.Title}' for '{target.DisplayTitle}' instead of creating a new reward.",
                    ThrottledRewardSyncLogWindow));
            }
            else
            {
                RunOnUi(() => AppendThrottledLog(
                    $"managed-reward-skipped-current:{existingReward.Id}",
                    $"Reward sync kept existing Twitch reward '{existingReward.Title}' for '{target.DisplayTitle}' because the saved reward is already current.",
                    ThrottledRewardSyncLogWindow));
            }
        }
        catch (Exception ex)
        {
            if (ex is OperationCanceledException && cancellationToken.IsCancellationRequested)
            {
                throw;
            }

            if (IsBroadcasterRewardEligibilityFailure(ex))
            {
                MarkBroadcasterManagedRewardsUnavailableForSession();
                throw;
            }

            if (TryApplyManagedRewardApiBackoff(ex, $"Twitch reward sync for '{target.DisplayTitle}'"))
            {
                throw;
            }

            var duplicateRecovery = await TryRecoverDuplicateManagedRewardAsync(
                ex,
                target,
                rewardTitle,
                managedRewardTitle,
                rewardCatalog,
                claimedRewardIds,
                ownershipIndex,
                apiCalls,
                cancellationToken);
            if (duplicateRecovery.handled)
            {
                return changed || duplicateRecovery.changed;
            }

            if (TryHandleManagedRewardSyncFailure(ex, managedRewardTitle, rewardTitle, target.DisplayTitle))
            {
                return changed;
            }

            RunOnUi(() => AppendLog($"Could not sync Twitch reward '{rewardTitle}' for '{target.DisplayTitle}': {ex.Message}"));
        }

        return changed;
    }

    private Task<bool> SynchronizeLinkedExistingRewardForTargetAsync(
        ManagedRewardSyncTarget target,
        TwitchApiClient.CustomRewardResponse? existingReward,
        ManagedRewardSyncCatalog rewardCatalog,
        HashSet<string> claimedRewardIds,
        CancellationToken cancellationToken)
    {
        var rewardId = target.RewardId?.Trim() ?? string.Empty;
        if (string.IsNullOrWhiteSpace(rewardId))
        {
            RunOnUi(() => AppendThrottledLog(
                $"linked-reward-missing-selection:{target.Id}",
                $"Linked reward sync skipped '{target.DisplayTitle}' because no existing Twitch reward is selected.",
                ThrottledRewardSyncLogWindow));
            return Task.FromResult(false);
        }

        if (existingReward is null)
        {
            RunOnUi(() => AppendThrottledLog(
                $"linked-reward-not-found:{target.Id}:{rewardId}",
                $"Linked Twitch reward for '{target.DisplayTitle}' was not found. Crystal Relay will not create, rename, or replace it automatically.",
                ThrottledRewardSyncLogWindow));
            return Task.FromResult(false);
        }

        claimedRewardIds.Add(existingReward.Id);

        RunOnUi(() => AppendThrottledLog(
            $"linked-reward-listen-only:{existingReward.Id}:{target.Id}",
            $"Linked Twitch reward '{existingReward.Title}' is listen-only for '{target.DisplayTitle}'. Crystal Relay will run its action when redeemed, but will not edit Twitch visibility, name, cost, prompt, color, input setting, deletion, or cooldown.",
            ThrottledRewardSyncLogWindow));
        return Task.FromResult(false);
    }

    private async Task<bool> DeleteInactiveManagedRewardAsync(
        ManagedRewardSyncTarget target,
        TwitchApiClient.CustomRewardResponse existingReward,
        ManagedRewardSyncCatalog rewardCatalog,
        ManagedRewardRuleOwnershipIndex ownershipIndex,
        ManagedRewardApiCallCounter apiCalls,
        CancellationToken cancellationToken)
    {
        var rewardTitle = ManagedRewardPresentation.StripPrefix(existingReward.Title);

        try
        {
            apiCalls.Deletes++;
            await twitchApiClient.DeleteCustomRewardAsync(
                Settings.Broadcaster.AccessToken,
                runtimeConfig.TwitchClientId,
                Settings.Broadcaster.UserId,
                existingReward.Id,
                cancellationToken);
            rewardCatalog.Remove(existingReward.Id);
            SetManagedRewardTargetId(target, string.Empty, ownershipIndex);
            RunOnUi(() => AppendThrottledLog(
                $"managed-reward-delete-inactive:{existingReward.Id}",
                $"Deleted inactive Twitch reward '{existingReward.Title}' for '{target.DisplayTitle}' to free a reward slot.",
                ThrottledRewardSyncLogWindow));
            return true;
        }
        catch (TwitchApiException ex) when (ex.StatusCode == System.Net.HttpStatusCode.NotFound)
        {
            rewardCatalog.Remove(existingReward.Id);
            SetManagedRewardTargetId(target, string.Empty, ownershipIndex);
            return true;
        }
        catch (Exception ex)
        {
            if (ex is OperationCanceledException && cancellationToken.IsCancellationRequested)
            {
                throw;
            }

            if (IsBroadcasterRewardEligibilityFailure(ex))
            {
                MarkBroadcasterManagedRewardsUnavailableForSession();
                throw;
            }

            if (IsInvalidBroadcasterTokenFailure(ex))
            {
                throw;
            }

            if (TryApplyManagedRewardApiBackoff(ex, $"Twitch reward delete for '{target.DisplayTitle}'"))
            {
                throw;
            }

            RunOnUi(() => AppendThrottledLog(
                $"managed-reward-delete-inactive-failed:{existingReward.Id}",
                $"Could not delete inactive Twitch reward '{rewardTitle}' for '{target.DisplayTitle}': {ex.Message}",
                ThrottledRewardSyncLogWindow));
            return false;
        }
    }

    private static bool HasRuntimeReadyAction(TriggerRule rule) => rule.ActionType switch
    {
        OscActionType.AvatarParameter => !string.IsNullOrWhiteSpace(rule.ParameterName),
        OscActionType.SetTrigger => rule.SharedRewardChoiceEnabled
            && rule.SharedRewardChoiceNumber > 0
            && rule.SetTriggerActions.Any(IsRuntimeReadySetTriggerAction),
        OscActionType.AvatarChange => !string.IsNullOrWhiteSpace(rule.AvatarChangeTargetId),
        OscActionType.AvatarRoulet => rule.AvatarRouletAvatarIds.Any(avatarId => !string.IsNullOrWhiteSpace(avatarId)),
        OscActionType.PlayerMovement => IsSupportedMovementRule(rule),
        _ => false
    };

    private static bool IsManagedUniversalChannelPointTrigger(UniversalTriggerRule trigger) =>
        trigger.TriggerType == UniversalTriggerType.ChannelPointReward;

    private static bool IsManagedAvatarScaleChannelPointRule(AvatarScaleRule rule) =>
        rule.TriggerType == AvatarScaleTriggerType.ChannelPointReward;

    private static bool IsManagedAvatarScaleMasterReward(AvatarScaleMasterRewardSettings masterReward) =>
        masterReward.IsEnabled || !string.IsNullOrWhiteSpace(masterReward.RewardId);

    private static bool IsActiveFloatBoostParentRule(TriggerRule rule) =>
        rule.TriggerType == TwitchTriggerType.ChannelPoints
        && rule.ActionType == OscActionType.AvatarParameter
        && rule.ParameterType == OscParameterType.Float
        && rule.DurationSeconds > 0
        && rule.ActiveFloatBoostRewardEnabled
        && (!string.IsNullOrWhiteSpace(rule.ActiveFloatBoostRewardTitle)
            || !string.IsNullOrWhiteSpace(rule.ActiveFloatBoostRewardId));

    private static bool IsAvatarScaleRuleInactiveAtRelativeLimit(
        AvatarScaleRule rule,
        AvatarScaleRuntimeStatus status,
        bool? previousIsAtLimit = null)
    {
        return TryGetAvatarScaleRelativeLimitState(rule, status, previousIsAtLimit, out var limitState)
            && limitState.IsAtLimit;
    }

    private static bool TryGetAvatarScaleRelativeLimitState(
        AvatarScaleRule rule,
        AvatarScaleRuntimeStatus status,
        bool? previousIsAtLimit,
        out AvatarScaleRelativeLimitState limitState)
    {
        limitState = default;
        if (rule.ScaleMode != AvatarScaleMode.RelativeHeight
            || rule.RewardSyncMode != TwitchRewardSyncMode.CreateOrManage
            || status.CurrentHeightMeters is null
            || rule.RelativeHeightMeters == 0)
        {
            return false;
        }

        if (rule.IsSubtractRelativeHeight)
        {
            if (!rule.HideRewardWhenMinimumHeightReached)
            {
                return false;
            }

            var effectiveMinimum = rule.RelativeMinimumHeightMeters;
            if (!rule.BypassVrChatScaleLimits && status.MinimumHeightMeters is > 0)
            {
                effectiveMinimum = Math.Max(effectiveMinimum, status.MinimumHeightMeters.Value);
            }

            var tolerance = previousIsAtLimit == true
                ? AvatarScaleLimitHeightReleaseToleranceMeters
                : AvatarScaleLimitHeightToleranceMeters;
            limitState = new AvatarScaleRelativeLimitState(
                status.CurrentHeightMeters.Value <= effectiveMinimum + tolerance,
                IsMinimumLimit: true,
                status.CurrentHeightMeters.Value,
                effectiveMinimum);
            return true;
        }

        if (!rule.HideRewardWhenMaximumHeightReached)
        {
            return false;
        }

        var effectiveMaximum = rule.RelativeMaximumHeightMeters;
        if (!rule.BypassVrChatScaleLimits && status.MaximumHeightMeters is > 0)
        {
            effectiveMaximum = Math.Min(effectiveMaximum, status.MaximumHeightMeters.Value);
        }

        var maximumTolerance = previousIsAtLimit == true
            ? AvatarScaleLimitHeightReleaseToleranceMeters
            : AvatarScaleLimitHeightToleranceMeters;
        limitState = new AvatarScaleRelativeLimitState(
            status.CurrentHeightMeters.Value >= effectiveMaximum - maximumTolerance,
            IsMinimumLimit: false,
            status.CurrentHeightMeters.Value,
            effectiveMaximum);
        return true;
    }

    public static bool HasUniversalTriggerAvatarParameterGate(UniversalTriggerRule trigger) =>
        GetUniversalTriggerRequiredAvatarParameterAddresses(trigger).Count > 0;

    private static bool HasRuntimeReadyUniversalTriggerAction(UniversalTriggerRule trigger) =>
        trigger.Actions.Any(IsRuntimeReadyUniversalTriggerAction);

    private static bool IsRuntimeReadyAvatarScaleRule(AvatarScaleRule rule) =>
        rule.ScaleMode switch
        {
            AvatarScaleMode.GlitchyRandomHeight => rule.ActiveTimeSeconds > 0
                && Math.Max(rule.MinimumHeightMeters, rule.MaximumHeightMeters) > Math.Min(rule.MinimumHeightMeters, rule.MaximumHeightMeters),
            AvatarScaleMode.Multiplier => rule.HeightMultiplier > 0,
            _ => true
        };

    private static bool IsRuntimeReadyUniversalTriggerAction(UniversalTriggerAction action) =>
        !string.IsNullOrWhiteSpace(action.OscAddress)
        && !string.IsNullOrWhiteSpace(action.TargetValue)
        && (action.DurationSeconds <= 0 || !string.IsNullOrWhiteSpace(action.DefaultValue));

    private static bool IsRuntimeReadySetTriggerAction(SetTriggerAction action) =>
        action.ParameterType is OscParameterType.Bool or OscParameterType.Int or OscParameterType.Float
        && !string.IsNullOrWhiteSpace(action.ParameterName)
        && !string.IsNullOrWhiteSpace(action.ParameterValue);

    private static IReadOnlyList<string> GetUniversalTriggerRequiredAvatarParameterAddresses(UniversalTriggerRule trigger)
    {
        return trigger.Actions
            .Where(IsRuntimeReadyUniversalTriggerAction)
            .Select(action => TryNormalizeUniversalAvatarParameterAddress(action.OscAddress))
            .Where(address => !string.IsNullOrWhiteSpace(address))
            .Cast<string>()
            .Distinct(StringComparer.Ordinal)
            .ToArray();
    }

    private IReadOnlyList<string> GetMissingCurrentAvatarParameters(IReadOnlyList<string> requiredParameters)
    {
        return GetMissingCurrentAvatarParameters(requiredParameters, Settings.VrChat.CurrentAvatarId?.Trim() ?? string.Empty);
    }

    private bool CurrentAvatarLocalOscJsonExists(string currentAvatarId)
    {
        var normalizedAvatarId = currentAvatarId?.Trim() ?? string.Empty;
        var resolvedUserId = ResolveCurrentUserIdForCache();
        if (string.IsNullOrWhiteSpace(resolvedUserId)
            || string.IsNullOrWhiteSpace(normalizedAvatarId))
        {
            return false;
        }

        var avatarFilePath = VrChatLocalOscCacheService.GetAvatarOscFilePath(resolvedUserId, normalizedAvatarId);
        return !string.IsNullOrWhiteSpace(avatarFilePath) && File.Exists(avatarFilePath);
    }

    public bool IsUniversalTriggerReadyForCurrentAvatarJson(
        UniversalTriggerRule trigger,
        string currentAvatarId)
    {
        var requiredParameters = GetUniversalTriggerRequiredAvatarParameterAddresses(trigger);
        if (requiredParameters.Count == 0)
        {
            return true;
        }

        return GetFoundCurrentAvatarParameterCount(requiredParameters, currentAvatarId) > 0;
    }

    private int GetFoundCurrentAvatarParameterCount(
        IReadOnlyList<string> requiredParameters,
        string currentAvatarId)
    {
        if (requiredParameters.Count == 0)
        {
            return 0;
        }

        var normalizedAvatarId = currentAvatarId?.Trim() ?? string.Empty;
        if (string.IsNullOrWhiteSpace(normalizedAvatarId)
            || !cachedVrChatParametersByAvatarId.TryGetValue(normalizedAvatarId, out var avatarParameters)
            || avatarParameters.Count == 0)
        {
            return 0;
        }

        var availableParameters = avatarParameters
            .Select(parameter => parameter.Address?.Trim() ?? string.Empty)
            .Where(address => !string.IsNullOrWhiteSpace(address))
            .ToHashSet(StringComparer.Ordinal);

        return requiredParameters.Count(availableParameters.Contains);
    }

    private IReadOnlyList<string> GetMissingCurrentAvatarParameters(
        IReadOnlyList<string> requiredParameters,
        string currentAvatarId)
    {
        if (requiredParameters.Count == 0)
        {
            return [];
        }

        var normalizedAvatarId = currentAvatarId?.Trim() ?? string.Empty;
        if (string.IsNullOrWhiteSpace(normalizedAvatarId)
            || !cachedVrChatParametersByAvatarId.TryGetValue(normalizedAvatarId, out var avatarParameters)
            || avatarParameters.Count == 0)
        {
            return requiredParameters.Select(GetAvatarParameterDisplayName).ToArray();
        }

        var availableParameters = avatarParameters
            .Select(parameter => parameter.Address?.Trim() ?? string.Empty)
            .Where(address => !string.IsNullOrWhiteSpace(address))
            .ToHashSet(StringComparer.Ordinal);

        return requiredParameters
            .Where(parameter => !availableParameters.Contains(parameter))
            .Select(GetAvatarParameterDisplayName)
            .ToArray();
    }

    private static string? TryNormalizeUniversalAvatarParameterAddress(string oscAddress)
    {
        var normalizedAddress = oscAddress?.Trim() ?? string.Empty;
        if (string.IsNullOrWhiteSpace(normalizedAddress))
        {
            return null;
        }

        const string avatarParameterPrefix = "/avatar/parameters/";
        const string avatarParameterPrefixWithoutSlash = "avatar/parameters/";
        if (normalizedAddress.StartsWith("/", StringComparison.Ordinal)
            && !normalizedAddress.StartsWith(avatarParameterPrefix, StringComparison.Ordinal))
        {
            return null;
        }

        if (normalizedAddress.StartsWith(avatarParameterPrefixWithoutSlash, StringComparison.Ordinal))
        {
            normalizedAddress = $"/{normalizedAddress}";
        }

        try
        {
            return VrChatOscClient.NormalizeAvatarParameterAddress(normalizedAddress);
        }
        catch (InvalidOperationException)
        {
            return normalizedAddress;
        }
    }

    private static string GetAvatarParameterDisplayName(string address)
    {
        var normalizedAddress = address?.Trim() ?? string.Empty;
        return normalizedAddress.Split('/', StringSplitOptions.RemoveEmptyEntries).LastOrDefault() ?? normalizedAddress;
    }

    private async Task<(bool handled, bool changed)> TryRecoverDuplicateManagedRewardAsync(
        Exception ex,
        ManagedRewardSyncTarget target,
        string rewardTitle,
        string managedRewardTitle,
        ManagedRewardSyncCatalog rewardCatalog,
        HashSet<string> claimedRewardIds,
        ManagedRewardRuleOwnershipIndex ownershipIndex,
        ManagedRewardApiCallCounter apiCalls,
        CancellationToken cancellationToken)
    {
        if (!IsDuplicateManagedRewardFailure(ex))
        {
            return (false, false);
        }

        var matchingReward = await FindAdoptableExistingManagedRewardAsync(
            target.Id,
            rewardTitle,
            rewardCatalog,
            ownershipIndex,
            refreshCatalogIfNeeded: true,
            apiCalls,
            cancellationToken);

        if (matchingReward is not null)
        {
            SetManagedRewardTargetId(target, matchingReward.Id, ownershipIndex);
            claimedRewardIds.Add(matchingReward.Id);
            ClearManagedRewardCreateBackoff(managedRewardTitle);
            RunOnUi(() =>
            {
                ApplyRewardCatalog(rewardCatalog.Rewards);
                AppendThrottledLog(
                    $"managed-reward-duplicate-adopted:{managedRewardTitle}",
                    $"Crystal Relay found the existing Twitch redeem '{rewardTitle}' and linked '{target.DisplayTitle}' to it instead of creating a duplicate.",
                    ThrottledRewardSyncLogWindow);
            });
            return (true, true);
        }

        MarkManagedRewardCreateBackoff(managedRewardTitle);
        RunOnUi(() => AppendThrottledLog(
            $"managed-reward-duplicate:{managedRewardTitle}",
            $"Twitch already has a redeem named '{rewardTitle}', so Crystal Relay will not create another one. Use a different reward name or connect this rule to the existing redeem.",
            ThrottledRewardSyncLogWindow));
        return (true, false);
    }

    private async Task<TwitchApiClient.CustomRewardResponse?> FindAdoptableExistingManagedRewardAsync(
        Guid currentOwnerId,
        string rewardTitle,
        ManagedRewardSyncCatalog rewardCatalog,
        ManagedRewardRuleOwnershipIndex ownershipIndex,
        bool refreshCatalogIfNeeded,
        ManagedRewardApiCallCounter apiCalls,
        CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(rewardTitle))
        {
            return null;
        }

        var matchingReward = FindAdoptableExistingManagedReward(currentOwnerId, rewardTitle, rewardCatalog, ownershipIndex);
        if (matchingReward is not null || !refreshCatalogIfNeeded)
        {
            return matchingReward;
        }

        apiCalls.CatalogReads++;
        rewardCatalog.Reset(await twitchApiClient.GetCustomRewardsAsync(
            Settings.Broadcaster.AccessToken,
            runtimeConfig.TwitchClientId,
            Settings.Broadcaster.UserId,
            cancellationToken));

        return FindAdoptableExistingManagedReward(currentOwnerId, rewardTitle, rewardCatalog, ownershipIndex);
    }

    private TwitchApiClient.CustomRewardResponse? FindAdoptableExistingManagedReward(
        Guid currentOwnerId,
        string rewardTitle,
        ManagedRewardSyncCatalog rewardCatalog,
        ManagedRewardRuleOwnershipIndex ownershipIndex)
    {
        foreach (var reward in rewardCatalog.FindByTitleVariants(rewardTitle))
        {
            if (!ownershipIndex.IsOwnedByAnotherRule(currentOwnerId, reward))
            {
                return reward;
            }
        }

        return null;
    }

    private async Task<(bool handled, bool changed)> TryRecycleManagedRewardForCapacityAsync(
        ManagedRewardSyncTarget target,
        IReadOnlyCollection<ManagedRewardSyncTarget> allSyncTargets,
        ManagedRewardSyncCatalog rewardCatalog,
        HashSet<string> claimedRewardIds,
        IReadOnlyCollection<string> capReclaimProtectedRewardIds,
        IReadOnlyCollection<string> capReclaimProtectedTitleKeys,
        Func<CancellationToken, Task<ManagedRewardSyncCatalog>> getManageableRewardCatalogAsync,
        ManagedRewardRuleOwnershipIndex ownershipIndex,
        ManagedRewardApiCallCounter apiCalls,
        string managedRewardTitle,
        int rewardCost,
        bool desiredEnabled,
        int rewardCooldownSeconds,
        string rewardBackgroundColor,
        string rewardPrompt,
        bool requireUserInput,
        CancellationToken cancellationToken)
    {
        if (!desiredEnabled)
        {
            MarkManagedRewardCreateBackoff(managedRewardTitle);
            RunOnUi(() =>
            {
                var message = $"Twitch is full on custom rewards, so Crystal Relay skipped creating inactive off-avatar reward '{ManagedRewardPresentation.StripPrefix(managedRewardTitle)}'. It will make room only for rewards needed by the current avatar.";
                AppendThrottledLog(
                    $"managed-reward-capacity-inactive:{managedRewardTitle}",
                    message,
                    ThrottledRewardSyncLogWindow);
                MarkActivityAttention(message);
            });
            return (true, false);
        }

        var manageableCatalog = await getManageableRewardCatalogAsync(cancellationToken);
        var manageableRewardIds = manageableCatalog.Rewards
            .Select(reward => reward.Id?.Trim())
            .Where(rewardId => !string.IsNullOrWhiteSpace(rewardId))
            .Cast<string>()
            .ToHashSet(StringComparer.Ordinal);
        var reclaimCandidate = rewardCatalog.FindFirstCapReclaimCandidate(
            manageableRewardIds,
            capReclaimProtectedRewardIds,
            capReclaimProtectedTitleKeys);

        if (reclaimCandidate is null)
        {
            MarkManagedRewardCreateBackoff(managedRewardTitle);
            RunOnUi(() =>
            {
                var message = $"Twitch is full on custom rewards, and Crystal Relay found no disabled app-owned off-avatar VRC reward it can safely recycle for '{target.DisplayTitle}'. Linked and user-created rewards were left untouched.";
                AppendThrottledLog(
                    $"managed-reward-capacity-no-reclaim:{managedRewardTitle}",
                    message,
                    ThrottledRewardSyncLogWindow);
                MarkActivityAttention(message);
            });
            return (true, false);
        }

        var previousRewardTitle = reclaimCandidate.Title;
        var clearedOwnerCount = ClearManagedRewardIdFromCreateOrManageOwners(
            reclaimCandidate.Id,
            target.Id,
            ownershipIndex);
        var clearedSyncTargetCount = ClearManagedRewardIdFromSyncTargets(
            reclaimCandidate.Id,
            target.Id,
            allSyncTargets);
        claimedRewardIds.Remove(reclaimCandidate.Id);

        apiCalls.Updates++;
        var recycledReward = await twitchApiClient.UpdateCustomRewardAsync(
            Settings.Broadcaster.AccessToken,
            runtimeConfig.TwitchClientId,
            Settings.Broadcaster.UserId,
            reclaimCandidate.Id,
            managedRewardTitle,
            rewardCost,
            desiredEnabled,
            rewardCooldownSeconds,
            rewardBackgroundColor,
            cancellationToken,
            rewardPrompt,
            requireUserInput);

        SetManagedRewardTargetId(target, recycledReward.Id, ownershipIndex);
        rewardCatalog.Replace(recycledReward);
        claimedRewardIds.Add(recycledReward.Id);
        ClearManagedRewardCreateBackoff(managedRewardTitle);
        RunOnUi(() => AppendThrottledLog(
            $"managed-reward-capacity-recycled:{reclaimCandidate.Id}:{target.Id}",
            $"Twitch was full, so Crystal Relay recycled disabled app-owned reward '{previousRewardTitle}' into '{managedRewardTitle}' for '{target.DisplayTitle}'. Cleared {clearedOwnerCount} old off-avatar saved link(s) and {clearedSyncTargetCount} in-pass sync target(s).",
            ThrottledRewardSyncLogWindow));
        return (true, true);
    }

    private static int ClearManagedRewardIdFromSyncTargets(
        string rewardId,
        Guid newOwnerId,
        IEnumerable<ManagedRewardSyncTarget> syncTargets)
    {
        var normalizedRewardId = rewardId?.Trim() ?? string.Empty;
        if (string.IsNullOrWhiteSpace(normalizedRewardId))
        {
            return 0;
        }

        var clearedCount = 0;
        foreach (var syncTarget in syncTargets)
        {
            if (syncTarget.Id == newOwnerId
                || syncTarget.UsesLinkedExistingReward
                || !string.Equals(syncTarget.RewardId?.Trim(), normalizedRewardId, StringComparison.Ordinal))
            {
                continue;
            }

            syncTarget.RewardId = string.Empty;
            clearedCount++;
        }

        return clearedCount;
    }

    private int ClearManagedRewardIdFromCreateOrManageOwners(
        string rewardId,
        Guid newOwnerId,
        ManagedRewardRuleOwnershipIndex ownershipIndex)
    {
        var normalizedRewardId = rewardId?.Trim() ?? string.Empty;
        if (string.IsNullOrWhiteSpace(normalizedRewardId))
        {
            return 0;
        }

        var clearedCount = 0;
        ownershipIndex.RemoveRewardId(normalizedRewardId);

        void ClearTriggerRule(TriggerRule rule)
        {
            if (rule.Id != newOwnerId
                && rule.RewardSyncMode == TwitchRewardSyncMode.CreateOrManage
                && string.Equals(rule.ChannelPointRewardId?.Trim(), normalizedRewardId, StringComparison.Ordinal))
            {
                rule.ChannelPointRewardId = string.Empty;
                clearedCount++;
            }

            if (rule.ActiveFloatBoostRewardOwnerId != newOwnerId
                && string.Equals(rule.ActiveFloatBoostRewardId?.Trim(), normalizedRewardId, StringComparison.Ordinal))
            {
                rule.ActiveFloatBoostRewardId = string.Empty;
                clearedCount++;
            }
        }

        foreach (var profile in Settings.AvatarProfiles)
        {
            if (profile.Id != newOwnerId
                && profile.SetTriggerMasterRewardSyncMode == TwitchRewardSyncMode.CreateOrManage
                && string.Equals(profile.SetTriggerMasterRewardId?.Trim(), normalizedRewardId, StringComparison.Ordinal))
            {
                profile.SetTriggerMasterRewardId = string.Empty;
                clearedCount++;
            }

            if (profile.Id != newOwnerId
                && profile.WardrobeMasterRewardSyncMode == TwitchRewardSyncMode.CreateOrManage
                && string.Equals(profile.WardrobeMasterRewardId?.Trim(), normalizedRewardId, StringComparison.Ordinal))
            {
                profile.WardrobeMasterRewardId = string.Empty;
                clearedCount++;
            }

            foreach (var outfit in profile.WardrobeOutfits)
            {
                if (outfit.Id != newOwnerId
                    && outfit.TwitchRewardSyncMode == TwitchRewardSyncMode.CreateOrManage
                    && string.Equals(outfit.TwitchRewardId?.Trim(), normalizedRewardId, StringComparison.Ordinal))
                {
                    outfit.TwitchRewardId = string.Empty;
                    clearedCount++;
                }
            }

            foreach (var rule in profile.ChannelPointRules)
            {
                ClearTriggerRule(rule);
            }
        }

        foreach (var rule in GetAllMovementRules())
        {
            ClearTriggerRule(rule);
        }

        foreach (var trigger in Settings.UniversalTriggers)
        {
            if (trigger.Id == newOwnerId
                || trigger.RewardSyncMode != TwitchRewardSyncMode.CreateOrManage
                || !string.Equals(trigger.RewardId?.Trim(), normalizedRewardId, StringComparison.Ordinal))
            {
                continue;
            }

            trigger.RewardId = string.Empty;
            clearedCount++;
        }

        if (newOwnerId != AvatarScaleMasterRewardOwnerId
            && Settings.AvatarScaleMasterReward.RewardSyncMode == TwitchRewardSyncMode.CreateOrManage
            && string.Equals(Settings.AvatarScaleMasterReward.RewardId?.Trim(), normalizedRewardId, StringComparison.Ordinal))
        {
            Settings.AvatarScaleMasterReward.RewardId = string.Empty;
            clearedCount++;
        }

        foreach (var rule in GetAllAvatarScaleRules())
        {
            if (rule.Id == newOwnerId
                || rule.RewardSyncMode != TwitchRewardSyncMode.CreateOrManage
                || !string.Equals(rule.RewardId?.Trim(), normalizedRewardId, StringComparison.Ordinal))
            {
                continue;
            }

            rule.RewardId = string.Empty;
            clearedCount++;
        }

        if (newOwnerId != RewardFireSaleFundingRewardOwnerId
            && string.Equals(Settings.RewardFireSale.FundingRewardId?.Trim(), normalizedRewardId, StringComparison.Ordinal))
        {
            Settings.RewardFireSale.FundingRewardId = string.Empty;
            clearedCount++;
        }

        return clearedCount;
    }

    private async Task<bool> CleanupStaleManagedRewardsAsync(
        ManagedRewardSyncCatalog rewardCatalog,
        IReadOnlyCollection<string> claimedRewardIds,
        IReadOnlyCollection<string> desiredManagedRewardTitleKeys,
        ManagedRewardApiCallCounter apiCalls,
        bool allowInactiveRewardDeletion,
        ManagedRewardSyncReason reason,
        CancellationToken cancellationToken)
    {
        var staleRewards = rewardCatalog.GetStaleRewards(claimedRewardIds, desiredManagedRewardTitleKeys);
        if (!allowInactiveRewardDeletion)
        {
            var staleRewardCount = staleRewards.Count;
            if (staleRewardCount > 0)
            {
                RunOnUi(() => AppendThrottledLog(
                    $"managed-reward-stale-cleanup-suppressed:{reason}",
                    $"Skipped deleting {staleRewardCount} stale managed Twitch reward(s) during {DescribeManagedRewardSyncReason(reason)} to avoid reward API churn. Stale reward cleanup only runs during explicit cleanup/maintenance.",
                    ThrottledRewardSyncLogWindow));
            }

            return false;
        }

        var changed = false;
        foreach (var staleReward in staleRewards)
        {
            try
            {
                apiCalls.Deletes++;
                await twitchApiClient.DeleteCustomRewardAsync(
                    Settings.Broadcaster.AccessToken,
                    runtimeConfig.TwitchClientId,
                    Settings.Broadcaster.UserId,
                    staleReward.Id,
                    cancellationToken);
                rewardCatalog.Remove(staleReward.Id);
                changed = true;
                RunOnUi(() => AppendThrottledLog(
                    $"managed-reward-stale-deleted:{staleReward.Id}",
                    $"Reward sync cleaned up stale disabled managed Twitch reward '{staleReward.Title}'.",
                    ThrottledRewardSyncLogWindow));
            }
            catch (TwitchApiException ex) when (ex.StatusCode == System.Net.HttpStatusCode.NotFound)
            {
                rewardCatalog.Remove(staleReward.Id);
                changed = true;
                RunOnUi(() => AppendThrottledLog(
                    $"managed-reward-stale-missing:{staleReward.Id}",
                    $"Reward sync removed missing stale Twitch reward '{staleReward.Title}' from the local sync catalog.",
                    ThrottledRewardSyncLogWindow));
            }
            catch (Exception ex)
            {
                if (ex is OperationCanceledException && cancellationToken.IsCancellationRequested)
                {
                    throw;
                }

                if (IsBroadcasterRewardEligibilityFailure(ex))
                {
                    MarkBroadcasterManagedRewardsUnavailableForSession();
                    throw;
                }

                if (TryApplyManagedRewardApiBackoff(ex, "Twitch stale reward cleanup"))
                {
                    throw;
                }

                var cleanupTitle = ManagedRewardPresentation.StripPrefix(staleReward.Title);
                RunOnUi(() => AppendThrottledLog(
                    $"managed-reward-cleanup:{staleReward.Id}",
                    $"Could not clean up an old Twitch reward '{cleanupTitle}': {ex.Message}",
                    ThrottledRewardSyncLogWindow));
            }
        }

        return changed;
    }

    private bool TryHandleManagedRewardSyncFailure(
        Exception ex,
        string managedRewardTitle,
        string rewardTitle,
        string displayTitle)
    {
        if (!IsManagedRewardCapacityFailure(ex))
        {
            return false;
        }

        MarkManagedRewardCreateBackoff(managedRewardTitle);
        RunOnUi(() =>
        {
            var message = $"Twitch is full on custom rewards, so Crystal Relay could not add '{rewardTitle}' yet. Remove old rewards or wait for cleanup, then it will try again.";
            AppendThrottledLog(
                $"managed-reward-capacity:{managedRewardTitle}",
                message,
                ThrottledRewardSyncLogWindow);
            MarkActivityAttention(message);
        });
        return true;
    }

    private static bool IsManagedRewardCapacityFailure(Exception ex)
    {
        return ex is TwitchApiException apiException
            && apiException.ApiMessage.Contains("CREATE_CUSTOM_REWARD_TOO_MANY_REWARDS", StringComparison.OrdinalIgnoreCase);
    }

    private static string NormalizeManagedRewardPrompt(string prompt)
    {
        var normalizedPrompt = (prompt ?? string.Empty)
            .Replace(Environment.NewLine, " ", StringComparison.Ordinal)
            .Replace('\r', ' ')
            .Replace('\n', ' ')
            .Trim();
        return normalizedPrompt.Length <= TwitchCustomRewardPromptMaxLength
            ? normalizedPrompt
            : normalizedPrompt[..TwitchCustomRewardPromptMaxLength];
    }

    private static bool IsBroadcasterRewardEligibilityFailure(Exception ex)
    {
        return ex is TwitchApiException apiException
            && apiException.ApiMessage.Contains("partner or affiliate status", StringComparison.OrdinalIgnoreCase);
    }

    private static bool IsDuplicateManagedRewardFailure(Exception ex)
    {
        return ex is TwitchApiException apiException
            && apiException.ApiMessage.Contains("CREATE_CUSTOM_REWARD_DUPLICATE_REWARD", StringComparison.OrdinalIgnoreCase);
    }

    private static bool IsBroadcasterRewardManagementScopeFailure(Exception ex)
    {
        return ex is TwitchApiException apiException
            && (apiException.StatusCode == System.Net.HttpStatusCode.Unauthorized
                || apiException.StatusCode == System.Net.HttpStatusCode.Forbidden)
            && apiException.ApiMessage.Contains("scope", StringComparison.OrdinalIgnoreCase);
    }

    private static bool IsInvalidBroadcasterTokenFailure(Exception ex)
    {
        return ex is TwitchApiException apiException
            && (apiException.StatusCode == System.Net.HttpStatusCode.Unauthorized
                || apiException.ApiMessage.Contains("Invalid OAuth token", StringComparison.OrdinalIgnoreCase));
    }

    private void MarkBroadcasterManagedRewardsUnavailableForSession()
    {
        if (broadcasterManagedRewardsUnavailableForSession)
        {
            return;
        }

        broadcasterManagedRewardsUnavailableForSession = true;
        RunOnUi(() =>
        {
            RaisePropertyChanged(nameof(ManagedChannelPointRewardHelpText));
            RaisePropertyChanged(nameof(UniversalManagedChannelPointRewardHelpText));
            SetUniversalManagedRewardSyncStatus("Universal Twitch reward sync skipped because Twitch channel-point reward management is unavailable for this broadcaster session.");
        });
    }

    private void ClearBroadcasterManagedRewardsUnavailableForSession()
    {
        if (!broadcasterManagedRewardsUnavailableForSession)
        {
            return;
        }

        broadcasterManagedRewardsUnavailableForSession = false;
        RunOnUi(() =>
        {
            RaisePropertyChanged(nameof(ManagedChannelPointRewardHelpText));
            RaisePropertyChanged(nameof(UniversalManagedChannelPointRewardHelpText));
        });
    }

    private bool ShouldBackOffManagedRewardCreate(string managedRewardTitle)
    {
        PruneExpiredTimestampEntries(managedRewardCreateBackoffByTitle, DateTimeOffset.UtcNow);
        if (!managedRewardCreateBackoffByTitle.TryGetValue(managedRewardTitle, out var retryAfter))
        {
            return false;
        }

        if (retryAfter <= DateTimeOffset.UtcNow)
        {
            managedRewardCreateBackoffByTitle.Remove(managedRewardTitle);
            return false;
        }

        return true;
    }

    private void MarkManagedRewardCreateBackoff(string managedRewardTitle)
    {
        if (string.IsNullOrWhiteSpace(managedRewardTitle))
        {
            return;
        }

        PruneExpiredTimestampEntries(managedRewardCreateBackoffByTitle, DateTimeOffset.UtcNow);
        managedRewardCreateBackoffByTitle[managedRewardTitle] = DateTimeOffset.UtcNow.Add(ManagedRewardCreateBackoffWindow);
    }

    private void ClearManagedRewardCreateBackoff(string managedRewardTitle)
    {
        if (string.IsNullOrWhiteSpace(managedRewardTitle))
        {
            return;
        }

        managedRewardCreateBackoffByTitle.Remove(managedRewardTitle);
    }

    private string GetManagedRewardActivationAvatarId()
    {
        return GetBestKnownCurrentAvatarId();
    }

    private static HashSet<string> BuildManagedRewardCapReclaimProtectedRewardIds(
        IEnumerable<ManagedRewardSyncTarget> targets)
    {
        return targets
            .Where(target => target.UsesLinkedExistingReward || target.ProtectFromCapReclaim)
            .Select(target => target.RewardId?.Trim())
            .Where(rewardId => !string.IsNullOrWhiteSpace(rewardId))
            .Cast<string>()
            .ToHashSet(StringComparer.Ordinal);
    }

    private static HashSet<string> BuildManagedRewardCapReclaimProtectedTitleKeys(
        IEnumerable<ManagedRewardSyncTarget> targets)
    {
        return targets
            .Where(target => target.UsesLinkedExistingReward || target.ProtectFromCapReclaim)
            .Select(target => ManagedRewardPresentation.NormalizeTitleIdentityKey(target.RewardTitle))
            .Where(titleKey => !string.IsNullOrWhiteSpace(titleKey))
            .ToHashSet(StringComparer.OrdinalIgnoreCase);
    }

    private static string BuildManagedRewardDesiredFingerprint(
        IReadOnlyCollection<ManagedRewardSyncTarget> targets,
        IEnumerable<string> retiredRewardIds,
        string currentAvatarId,
        bool allowManagedRewardActivation,
        bool? forcedManagedRewardActivation,
        bool useManagedRewardTitlePrefix)
    {
        var builder = new StringBuilder();
        builder.Append("avatar=");
        AppendFingerprintValue(builder, currentAvatarId);
        builder.Append("|activation=").Append(allowManagedRewardActivation ? "1" : "0");
        builder.Append("|forced=").Append(forcedManagedRewardActivation?.ToString() ?? "auto");
        builder.Append("|prefix=").Append(useManagedRewardTitlePrefix ? "1" : "0");

        foreach (var target in targets
                     .Where(target => !target.UsesLinkedExistingReward)
                     .OrderBy(target => target.Id))
        {
            builder.Append("|target=");
            builder.Append(target.Id);
            AppendFingerprintValue(builder, target.RewardId);
            AppendFingerprintValue(builder, target.RewardTitle);
            builder.Append(':').Append(target.RewardCost);
            builder.Append(':').Append(target.CooldownSeconds);
            AppendFingerprintValue(builder, target.BackgroundColor);
            AppendFingerprintValue(builder, target.Prompt);
            builder.Append(':').Append(target.RequireUserInput ? "1" : "0");
            builder.Append(':').Append(target.DesiredEnabled ? "1" : "0");
            builder.Append(':').Append(target.DeleteWhenInactive ? "1" : "0");
        }

        foreach (var retiredRewardId in retiredRewardIds
                     .Select(rewardId => rewardId?.Trim() ?? string.Empty)
                     .Where(rewardId => !string.IsNullOrWhiteSpace(rewardId))
                     .OrderBy(rewardId => rewardId, StringComparer.Ordinal))
        {
            builder.Append("|retired=");
            AppendFingerprintValue(builder, retiredRewardId);
        }

        return builder.ToString();
    }

    private static void AppendFingerprintValue(StringBuilder builder, string? value)
    {
        var normalizedValue = value?.Trim() ?? string.Empty;
        builder.Append(':').Append(normalizedValue.Length).Append(':').Append(normalizedValue);
    }

    private static bool SetManagedRewardTargetId(
        ManagedRewardSyncTarget target,
        string rewardId,
        ManagedRewardRuleOwnershipIndex ownershipIndex)
    {
        var previousRewardId = target.RewardId?.Trim() ?? string.Empty;
        var normalizedRewardId = rewardId?.Trim() ?? string.Empty;
        if (string.Equals(previousRewardId, normalizedRewardId, StringComparison.Ordinal))
        {
            return false;
        }

        target.RewardId = normalizedRewardId;
        target.ApplyRewardId(normalizedRewardId);
        ownershipIndex.UpdateRewardId(target.Id, previousRewardId, normalizedRewardId);
        return true;
    }

    private void ApplyRewardCatalog(IEnumerable<TwitchApiClient.CustomRewardResponse> rewards)
    {
        var orderedRewards = rewards
            .OrderBy(reward => reward.Title, StringComparer.OrdinalIgnoreCase)
            .ToArray();

        ApplyRewardOptions(orderedRewards);
        QueueBridgeRefresh();

        foreach (var rule in EnumerateAllRules().Where(rule =>
                     rule.RewardSyncMode == TwitchRewardSyncMode.CreateOrManage
                     && !string.IsNullOrWhiteSpace(rule.ChannelPointRewardId)))
        {
            var reward = orderedRewards.FirstOrDefault(option =>
                string.Equals(option.Id, rule.ChannelPointRewardId, StringComparison.Ordinal));
            if (reward is null)
            {
                continue;
            }

            var normalizedRewardTitle = ManagedRewardPresentation.StripPrefix(reward.Title);
            if (string.IsNullOrWhiteSpace(rule.ChannelPointRewardTitle))
            {
                rule.ChannelPointRewardTitle = normalizedRewardTitle;
            }

            if (rule.ChannelPointRewardCost <= 0)
            {
                rule.ChannelPointRewardCost = reward.Cost;
            }
        }

        foreach (var profile in Settings.AvatarProfiles.Where(profile =>
                     profile.SetTriggerMasterRewardSyncMode == TwitchRewardSyncMode.CreateOrManage
                     && !string.IsNullOrWhiteSpace(profile.SetTriggerMasterRewardId)))
        {
            var reward = orderedRewards.FirstOrDefault(option =>
                string.Equals(option.Id, profile.SetTriggerMasterRewardId, StringComparison.Ordinal));
            if (reward is null)
            {
                continue;
            }

            var normalizedRewardTitle = ManagedRewardPresentation.StripPrefix(reward.Title);
            if (string.IsNullOrWhiteSpace(profile.SetTriggerMasterRewardTitle))
            {
                profile.SetTriggerMasterRewardTitle = normalizedRewardTitle;
            }

            if (profile.SetTriggerMasterRewardCost <= 0)
            {
                profile.SetTriggerMasterRewardCost = reward.Cost;
            }
        }

        foreach (var trigger in Settings.UniversalTriggers.Where(trigger =>
                     trigger.TriggerType == UniversalTriggerType.ChannelPointReward
                     && trigger.RewardSyncMode == TwitchRewardSyncMode.CreateOrManage
                     && !string.IsNullOrWhiteSpace(trigger.RewardId)))
        {
            var reward = orderedRewards.FirstOrDefault(option =>
                string.Equals(option.Id, trigger.RewardId, StringComparison.Ordinal));
            if (reward is null)
            {
                continue;
            }

            var normalizedRewardTitle = ManagedRewardPresentation.StripPrefix(reward.Title);
            if (string.IsNullOrWhiteSpace(trigger.RewardTitle))
            {
                trigger.RewardTitle = normalizedRewardTitle;
            }

            if (trigger.RewardCost <= 0)
            {
                trigger.RewardCost = reward.Cost;
            }
        }

        foreach (var rule in GetAllAvatarScaleRules().Where(rule =>
                     rule.TriggerType == AvatarScaleTriggerType.ChannelPointReward
                     && rule.RewardSyncMode == TwitchRewardSyncMode.CreateOrManage
                     && !string.IsNullOrWhiteSpace(rule.RewardId)))
        {
            var reward = orderedRewards.FirstOrDefault(option =>
                string.Equals(option.Id, rule.RewardId, StringComparison.Ordinal));
            if (reward is null)
            {
                continue;
            }

            var normalizedRewardTitle = ManagedRewardPresentation.StripPrefix(reward.Title);
            if (string.IsNullOrWhiteSpace(rule.RewardTitle))
            {
                rule.RewardTitle = normalizedRewardTitle;
            }

            if (rule.RewardCost <= 0)
            {
                rule.RewardCost = reward.Cost;
            }
        }
    }

    private void ApplyRewardOptions(IReadOnlyList<TwitchApiClient.CustomRewardResponse> orderedRewards)
    {
        var nextOptions = BuildRewardOptions(orderedRewards);

        foreach (var option in nextOptions)
        {
            if (FindRewardOptionIndexById(RewardOptions, option.Id) < 0)
            {
                RewardOptions.Add(option);
            }
        }

        for (var desiredIndex = 0; desiredIndex < nextOptions.Count; desiredIndex++)
        {
            var desiredOption = nextOptions[desiredIndex];
            var currentIndex = FindRewardOptionIndexById(RewardOptions, desiredOption.Id);
            if (currentIndex < 0)
            {
                RewardOptions.Insert(desiredIndex, desiredOption);
                continue;
            }

            if (currentIndex != desiredIndex)
            {
                RewardOptions.Move(currentIndex, desiredIndex);
            }

            if (!EqualityComparer<TwitchRewardOption>.Default.Equals(RewardOptions[desiredIndex], desiredOption))
            {
                RewardOptions[desiredIndex] = desiredOption;
            }
        }

        for (var index = RewardOptions.Count - 1; index >= nextOptions.Count; index--)
        {
            RewardOptions.RemoveAt(index);
        }
    }

    private IReadOnlyDictionary<string, int> BuildLinkedRewardCooldownLookup()
    {
        var lookup = new Dictionary<string, int>(StringComparer.Ordinal);
        foreach (var option in RewardOptions)
        {
            var rewardId = option.Id?.Trim() ?? string.Empty;
            if (string.IsNullOrWhiteSpace(rewardId) || option.IsCatalogMissing)
            {
                continue;
            }

            lookup[rewardId] = Math.Max(0, option.CooldownSeconds);
        }

        return lookup;
    }

    private void ApplyPowerUpCatalog(IEnumerable<TwitchApiClient.CustomPowerUpResponse> powerUps)
    {
        var orderedPowerUps = powerUps
            .OrderBy(powerUp => powerUp.Title, StringComparer.OrdinalIgnoreCase)
            .ToArray();

        ApplyPowerUpOptions(orderedPowerUps);
        foreach (var rule in Settings.PowerUpRules.Where(rule =>
                     rule.SourceMode == TwitchRewardSyncMode.LinkExisting
                     && !string.IsNullOrWhiteSpace(rule.PowerUpId)))
        {
            var powerUp = orderedPowerUps.FirstOrDefault(option =>
                string.Equals(option.Id, rule.PowerUpId, StringComparison.Ordinal));
            if (powerUp is null)
            {
                continue;
            }

            rule.PowerUpTitle = powerUp.Title;
            rule.BitsCost = powerUp.EffectiveBitsCost <= 0 ? rule.BitsCost : powerUp.EffectiveBitsCost;
            rule.Prompt = powerUp.Prompt;
        }

        QueueSave();
        QueueBridgeRefresh();
    }

    private void ApplyPowerUpOptions(IReadOnlyList<TwitchApiClient.CustomPowerUpResponse> orderedPowerUps)
    {
        var nextOptions = BuildPowerUpOptions(orderedPowerUps);
        foreach (var option in nextOptions)
        {
            if (FindPowerUpOptionIndexById(PowerUpOptions, option.Id) < 0)
            {
                PowerUpOptions.Add(option);
            }
        }

        for (var desiredIndex = 0; desiredIndex < nextOptions.Count; desiredIndex++)
        {
            var desiredOption = nextOptions[desiredIndex];
            var currentIndex = FindPowerUpOptionIndexById(PowerUpOptions, desiredOption.Id);
            if (currentIndex < 0)
            {
                PowerUpOptions.Insert(desiredIndex, desiredOption);
                continue;
            }

            if (currentIndex != desiredIndex)
            {
                PowerUpOptions.Move(currentIndex, desiredIndex);
            }

            if (!EqualityComparer<TwitchPowerUpOption>.Default.Equals(PowerUpOptions[desiredIndex], desiredOption))
            {
                PowerUpOptions[desiredIndex] = desiredOption;
            }
        }

        for (var index = PowerUpOptions.Count - 1; index >= nextOptions.Count; index--)
        {
            PowerUpOptions.RemoveAt(index);
        }
    }

    private IReadOnlyList<TwitchPowerUpOption> BuildPowerUpOptions(IReadOnlyList<TwitchApiClient.CustomPowerUpResponse> orderedPowerUps)
    {
        var options = new List<TwitchPowerUpOption>
        {
            TwitchPowerUpOption.Placeholder(T("Select Twitch Power Up"))
        };

        var loadedPowerUpIds = new HashSet<string>(StringComparer.Ordinal);
        foreach (var powerUp in orderedPowerUps)
        {
            if (!string.IsNullOrWhiteSpace(powerUp.Id))
            {
                loadedPowerUpIds.Add(powerUp.Id);
            }

            options.Add(TwitchPowerUpOption.FromPowerUp(powerUp));
        }

        var missingPowerUpIds = new HashSet<string>(loadedPowerUpIds, StringComparer.Ordinal);
        foreach (var reference in EnumerateLinkedPowerUpReferences())
        {
            if (missingPowerUpIds.Add(reference.PowerUpId))
            {
                options.Add(TwitchPowerUpOption.MissingLinked(reference.PowerUpId, reference.DisplayTitle));
            }
        }

        return options;
    }

    private IEnumerable<LinkedPowerUpReference> EnumerateLinkedPowerUpReferences()
    {
        foreach (var rule in Settings.PowerUpRules.Where(rule =>
                     rule.SourceMode == TwitchRewardSyncMode.LinkExisting
                     && !string.IsNullOrWhiteSpace(rule.PowerUpId)))
        {
            yield return new LinkedPowerUpReference(
                rule.PowerUpId.Trim(),
                string.IsNullOrWhiteSpace(rule.PowerUpTitle) ? rule.DisplayTitle : rule.PowerUpTitle.Trim());
        }
    }

    private IReadOnlyList<TwitchRewardOption> BuildRewardOptions(IReadOnlyList<TwitchApiClient.CustomRewardResponse> orderedRewards)
    {
        var options = new List<TwitchRewardOption>
        {
            TwitchRewardOption.Placeholder(T("Select Twitch reward"))
        };

        var loadedRewardIds = new HashSet<string>(StringComparer.Ordinal);
        foreach (var reward in orderedRewards)
        {
            if (!string.IsNullOrWhiteSpace(reward.Id))
            {
                loadedRewardIds.Add(reward.Id);
            }

            options.Add(TwitchRewardOption.FromReward(reward));
        }

        var missingLinkedRewardIds = new HashSet<string>(loadedRewardIds, StringComparer.Ordinal);
        foreach (var reference in EnumerateLinkedTwitchRewardReferences())
        {
            if (missingLinkedRewardIds.Add(reference.RewardId))
            {
                options.Add(TwitchRewardOption.MissingLinked(reference.RewardId, reference.DisplayTitle));
            }
        }

        return options;
    }

    private IEnumerable<LinkedTwitchRewardReference> EnumerateLinkedTwitchRewardReferences()
    {
        foreach (var rule in EnumerateAllRules().Where(rule =>
                     rule.RewardSyncMode == TwitchRewardSyncMode.LinkExisting
                     && !string.IsNullOrWhiteSpace(rule.ChannelPointRewardId)))
        {
            yield return new LinkedTwitchRewardReference(
                rule.ChannelPointRewardId.Trim(),
                GetLinkedRewardFallbackTitle(rule.ChannelPointRewardTitle, rule.DisplayTitle));
        }

        foreach (var profile in Settings.AvatarProfiles.Where(profile =>
                     profile.SetTriggerMasterRewardSyncMode == TwitchRewardSyncMode.LinkExisting
                     && !string.IsNullOrWhiteSpace(profile.SetTriggerMasterRewardId)))
        {
            yield return new LinkedTwitchRewardReference(
                profile.SetTriggerMasterRewardId.Trim(),
                GetLinkedRewardFallbackTitle(profile.SetTriggerMasterRewardTitle, profile.SetTriggerMasterRewardDisplayTitle));
        }

        foreach (var trigger in Settings.UniversalTriggers.Where(trigger =>
                     trigger.TriggerType == UniversalTriggerType.ChannelPointReward
                     && trigger.RewardSyncMode == TwitchRewardSyncMode.LinkExisting
                     && !string.IsNullOrWhiteSpace(trigger.RewardId)))
        {
            yield return new LinkedTwitchRewardReference(
                trigger.RewardId.Trim(),
                GetLinkedRewardFallbackTitle(trigger.RewardTitle, trigger.DisplayTitle));
        }

        var masterReward = Settings.AvatarScaleMasterReward;
        if (masterReward.RewardSyncMode == TwitchRewardSyncMode.LinkExisting
            && !string.IsNullOrWhiteSpace(masterReward.RewardId))
        {
            yield return new LinkedTwitchRewardReference(
                masterReward.RewardId.Trim(),
                GetLinkedRewardFallbackTitle(masterReward.RewardTitle, T("Avatar Scaling Master Reward")));
        }

        foreach (var rule in GetAllAvatarScaleRules().Where(rule =>
                     rule.TriggerType == AvatarScaleTriggerType.ChannelPointReward
                     && rule.RewardSyncMode == TwitchRewardSyncMode.LinkExisting
                     && !string.IsNullOrWhiteSpace(rule.RewardId)))
        {
            yield return new LinkedTwitchRewardReference(
                rule.RewardId.Trim(),
                GetLinkedRewardFallbackTitle(rule.RewardTitle, rule.DisplayTitle));
        }

        foreach (var profile in Settings.AvatarProfiles)
        {
            foreach (var outfit in profile.WardrobeOutfits.Where(outfit =>
                         outfit.TwitchRewardSyncMode == TwitchRewardSyncMode.LinkExisting
                         && !string.IsNullOrWhiteSpace(outfit.TwitchRewardId)))
            {
                yield return new LinkedTwitchRewardReference(
                    outfit.TwitchRewardId.Trim(),
                    GetLinkedRewardFallbackTitle(outfit.TwitchRewardTitle, outfit.DisplayTitle));
            }

            if (profile.WardrobeMasterRewardSyncMode == TwitchRewardSyncMode.LinkExisting
                && !string.IsNullOrWhiteSpace(profile.WardrobeMasterRewardId))
            {
                yield return new LinkedTwitchRewardReference(
                    profile.WardrobeMasterRewardId.Trim(),
                    GetLinkedRewardFallbackTitle(profile.WardrobeMasterRewardTitle, T("Wardrobe Master Reward")));
            }
        }
    }

    private static string GetLinkedRewardFallbackTitle(string configuredTitle, string ownerTitle)
    {
        var normalizedTitle = ManagedRewardPresentation.StripPrefix(configuredTitle ?? string.Empty);
        if (!string.IsNullOrWhiteSpace(normalizedTitle))
        {
            return normalizedTitle.Trim();
        }

        return ownerTitle?.Trim() ?? string.Empty;
    }

    private static int FindRewardOptionIndexById(IReadOnlyList<TwitchRewardOption> options, string rewardId)
    {
        var normalizedRewardId = rewardId?.Trim() ?? string.Empty;
        for (var index = 0; index < options.Count; index++)
        {
            if (string.Equals(options[index].Id, normalizedRewardId, StringComparison.Ordinal))
            {
                return index;
            }
        }

        return -1;
    }

    private static int FindPowerUpOptionIndexById(IReadOnlyList<TwitchPowerUpOption> options, string powerUpId)
    {
        var normalizedPowerUpId = powerUpId?.Trim() ?? string.Empty;
        for (var index = 0; index < options.Count; index++)
        {
            if (string.Equals(options[index].Id, normalizedPowerUpId, StringComparison.Ordinal))
            {
                return index;
            }
        }

        return -1;
    }

    // About refresh prefers normal authenticated Twitch API calls when possible.
    // If the app does not have a usable token, it falls back to optional configured profile data.
    private async Task RefreshAboutProfilesAsync()
    {
        if (isRefreshingAboutProfiles)
        {
            return;
        }

        isRefreshingAboutProfiles = true;

        await ReloadRuntimeConfigAsync();
        var profiles = EnumerateAboutProfiles().ToArray();

        try
        {
            if (!HasAboutProfileLookupAccess())
            {
                await RefreshAboutProfilesWithoutAuthAsync(profiles);
                return;
            }

            var accessToken = GetAboutProfileLookupAccessToken();
            if (string.IsNullOrWhiteSpace(accessToken))
            {
                await RefreshAboutProfilesWithoutAuthAsync(profiles);
                return;
            }

            var users = await twitchApiClient.GetUsersByLoginsAsync(
                accessToken,
                runtimeConfig.TwitchClientId,
                profiles.Select(profile => profile.TwitchLogin));
            var liveUserIds = await twitchApiClient.GetLiveStreamUserIdsAsync(
                accessToken,
                runtimeConfig.TwitchClientId,
                users.Select(user => user.Id));

            var usersByLogin = users
                .Where(user => !string.IsNullOrWhiteSpace(user.Login))
                .GroupBy(user => user.Login, StringComparer.OrdinalIgnoreCase)
                .ToDictionary(group => group.Key, group => group.First(), StringComparer.OrdinalIgnoreCase);

            RunOnUi(() =>
            {
                foreach (var profile in profiles)
                {
                    if (usersByLogin.TryGetValue(profile.TwitchLogin, out var user))
                    {
                        profile.DisplayName = string.IsNullOrWhiteSpace(user.DisplayName)
                            ? profile.DisplayName
                            : user.DisplayName;
                        profile.ProfileImageUrl = user.ProfileImageUrl ?? string.Empty;
                        profile.IsLive = liveUserIds.Contains(user.Id);
                    }
                    else
                    {
                        profile.IsLive = false;
                    }
                }
            });

            aboutProfilesLastRefreshedAt = DateTimeOffset.UtcNow;
        }
        catch
        {
            try
            {
                await RefreshAboutProfilesWithoutAuthAsync(profiles);
            }
            catch
            {
                RunOnUi(ClearAboutProfileLiveStates);
            }
        }
        finally
        {
            isRefreshingAboutProfiles = false;
        }
    }

    // No-login About refresh path. This keeps creator/playtester cards useful on
    // fresh installs by using configured supplemental data before image-only fallbacks.
    private async Task RefreshAboutProfilesWithoutAuthAsync(IReadOnlyList<AboutTwitchProfile> profiles)
    {
        if (!string.IsNullOrWhiteSpace(runtimeConfig.SupplementalAboutProfilesEndpoint))
        {
            try
            {
                var supplementalProfiles = await twitchApiClient.GetSupplementalAboutProfilesAsync(
                    runtimeConfig.SupplementalAboutProfilesEndpoint,
                    runtimeConfig.SupplementalAboutProfilesHeaderName,
                    runtimeConfig.SupplementalAboutProfilesHeaderValue);
                RunOnUi(() => ApplySupplementalAboutProfiles(profiles, supplementalProfiles));
            }
            catch
            {
                RunOnUi(() => ApplyAboutProfileLiveStates(
                    profiles,
                    new Dictionary<string, bool>(StringComparer.OrdinalIgnoreCase)));
            }
        }
        else
        {
            RunOnUi(() => ApplyAboutProfileLiveStates(
                profiles,
                new Dictionary<string, bool>(StringComparer.OrdinalIgnoreCase)));
        }

        await RefreshAboutProfileImagesWithoutAuthAsync(profiles);

        aboutProfilesLastRefreshedAt = DateTimeOffset.UtcNow;
    }

    private async Task RefreshAboutProfileImagesWithoutAuthAsync(IReadOnlyList<AboutTwitchProfile> profiles)
    {
        var profilesNeedingImages = profiles
            .Where(profile => !profile.HasProfileImage)
            .ToArray();
        if (profilesNeedingImages.Length == 0)
        {
            return;
        }

        await Task.WhenAll(profilesNeedingImages.Select(async profile =>
        {
            try
            {
                var publicImageUrl = await twitchApiClient.GetPublicChannelProfileImageUrlAsync(profile.TwitchLogin);
                if (string.IsNullOrWhiteSpace(publicImageUrl))
                {
                    return;
                }

                RunOnUi(() => profile.ProfileImageUrl = publicImageUrl);
            }
            catch
            {
                // Public About profile lookups are best effort only.
            }
        }));
    }

    private void ClearAboutProfileLiveStates()
    {
        foreach (var profile in EnumerateAboutProfiles())
        {
            profile.IsLive = false;
        }
    }

    private static void ApplyAboutProfileLiveStates(
        IEnumerable<AboutTwitchProfile> profiles,
        IReadOnlyDictionary<string, bool> liveStates)
    {
        foreach (var profile in profiles)
        {
            profile.IsLive = liveStates.TryGetValue(profile.TwitchLogin, out var isLive) && isLive;
        }
    }

    private static void ApplySupplementalAboutProfiles(
        IEnumerable<AboutTwitchProfile> profiles,
        IReadOnlyDictionary<string, TwitchApiClient.SupplementalAboutProfileData> supplementalProfiles)
    {
        foreach (var profile in profiles)
        {
            if (!supplementalProfiles.TryGetValue(profile.TwitchLogin, out var supplementalProfile))
            {
                profile.IsLive = false;
                continue;
            }

            if (!string.IsNullOrWhiteSpace(supplementalProfile.DisplayName))
            {
                profile.DisplayName = supplementalProfile.DisplayName;
            }

            if (!string.IsNullOrWhiteSpace(supplementalProfile.ProfileImageUrl))
            {
                profile.ProfileImageUrl = supplementalProfile.ProfileImageUrl;
            }

            profile.IsLive = supplementalProfile.IsLive;
        }
    }

    private bool HasAboutProfileLookupAccess()
    {
        return !string.IsNullOrWhiteSpace(GetAboutProfileLookupAccessToken())
            && !string.IsNullOrWhiteSpace(runtimeConfig.TwitchClientId);
    }

    private string GetAboutProfileLookupAccessToken()
    {
        if (!string.IsNullOrWhiteSpace(Settings.Broadcaster.AccessToken))
        {
            return Settings.Broadcaster.AccessToken;
        }

        if (!string.IsNullOrWhiteSpace(Settings.Bot.AccessToken))
        {
            return Settings.Bot.AccessToken;
        }

        return string.Empty;
    }

    private IEnumerable<AboutTwitchProfile> EnumerateAboutProfiles()
    {
        yield return AboutCreatorProfile;

        foreach (var tester in AboutTesterProfiles)
        {
            yield return tester;
        }
    }

    private void RefreshRuntimeSummary()
    {
        RuntimeConfigStatus = T("Built-in Twitch app ready.");

        UpdateOscStatusSummary();
    }

    private bool ShouldSaveAvatarProfileChange(string? propertyName) =>
        string.IsNullOrWhiteSpace(propertyName) || !AvatarProfilePropertiesSkippingSave.Contains(propertyName);

    private static bool ShouldRefreshBridgeForAvatarProfileChange(string? propertyName) =>
        string.IsNullOrWhiteSpace(propertyName) || AvatarProfilePropertiesRequiringBridgeRefresh.Contains(propertyName);

    private static bool ShouldSynchronizeManagedRewardsForAvatarProfileChange(string? propertyName) =>
        string.IsNullOrWhiteSpace(propertyName) || AvatarProfilePropertiesRequiringManagedRewardSync.Contains(propertyName);

    private static bool ShouldRefreshBridgeForRuleChange(string? propertyName) =>
        string.IsNullOrWhiteSpace(propertyName) || RulePropertiesRequiringBridgeRefresh.Contains(propertyName);

    private static bool ShouldSynchronizeManagedRewardsForRuleChange(string? propertyName) =>
        string.IsNullOrWhiteSpace(propertyName) || RulePropertiesRequiringManagedRewardSync.Contains(propertyName);

    private static bool ShouldRefreshBridgeForWardrobeOutfitChange(string? propertyName) =>
        string.IsNullOrWhiteSpace(propertyName) || WardrobeOutfitPropertiesRequiringBridgeRefresh.Contains(propertyName);

    private static bool ShouldSynchronizeManagedRewardsForWardrobeOutfitChange(string? propertyName) =>
        string.IsNullOrWhiteSpace(propertyName) || WardrobeOutfitPropertiesRequiringManagedRewardSync.Contains(propertyName);

    private static bool ShouldSynchronizeManagedRewardsForUniversalTriggerChange(string? propertyName) =>
        string.IsNullOrWhiteSpace(propertyName) || UniversalTriggerPropertiesRequiringManagedRewardSync.Contains(propertyName);

    private static bool ShouldSynchronizeManagedRewardsForUniversalTriggerActionChange(string? propertyName) =>
        string.IsNullOrWhiteSpace(propertyName) || UniversalTriggerActionPropertiesRequiringManagedRewardSync.Contains(propertyName);

    private static bool ShouldSynchronizeManagedRewardsForAvatarScaleRuleChange(string? propertyName) =>
        string.IsNullOrWhiteSpace(propertyName) || AvatarScaleRulePropertiesRequiringManagedRewardSync.Contains(propertyName);

    private static bool ShouldRefreshBridgeForSettingsChange(object? sender, string? propertyName)
    {
        if (string.IsNullOrWhiteSpace(propertyName))
        {
            return true;
        }

        return sender switch
        {
            TwitchAccountSettings => TwitchAccountPropertiesRequiringBridgeRefresh.Contains(propertyName),
            _ => true
        };
    }

    private async Task ReloadRuntimeConfigAsync(bool force = false)
    {
        if (runtimeConfigLoaded && !force)
        {
            return;
        }

        runtimeConfig = await runtimeConfigStore.LoadAsync();
        runtimeConfigLoaded = true;
        RunOnUi(RefreshRuntimeSummary);
        RunOnUi(RefreshCommandStates);
        RunOnUi(QueueLiveFeedbackHeartbeatEvaluation);
    }

    private void UpdateAccountStatuses()
    {
        if (Settings.Broadcaster.IsConnected)
        {
            var broadcasterDisplayName = string.IsNullOrWhiteSpace(Settings.Broadcaster.DisplayName)
                ? Settings.Broadcaster.Login
                : Settings.Broadcaster.DisplayName;
            broadcasterDisplayName = string.IsNullOrWhiteSpace(broadcasterDisplayName)
                ? "broadcaster"
                : broadcasterDisplayName;
            BroadcasterStatus = BroadcasterRewardManagementScopeKnownMissing
                ? $"Connected as {broadcasterDisplayName}, but reconnect once to restore Twitch reward management."
                : broadcasterReconnectRequired
                    ? $"Connected as {broadcasterDisplayName}, but reconnect Twitch to refresh the background listener."
                : $"Connected as {broadcasterDisplayName}.";
        }
        else if (HasRecoverableBroadcasterSession)
        {
            BroadcasterStatus = "Checking saved broadcaster login.";
        }
        else
        {
            BroadcasterStatus = "Broadcaster account not connected.";
        }

        BotStatus = Settings.Bot.IsConnected
            ? botReconnectRequired
                ? $"Connected as {Settings.Bot.DisplayName}, but reconnect Twitch to restore bot announcements."
                : $"Connected as {Settings.Bot.DisplayName}. Bot announcements are ready."
            : Settings.UseBroadcasterAsBotSender
                ? "Bot account not connected. Broadcaster-as-bot is enabled."
                : "Bot account not connected. This is optional.";

        BroadcasterExpiryStatus = BuildExpiryStatus(Settings.Broadcaster);
        BotExpiryStatus = BuildExpiryStatus(Settings.Bot);
        RaisePropertyChanged(nameof(EffectiveBotSenderStatusText));
        UpdateOscStatusSummary();
        RefreshStreamingStatusCard();
        UpdateChatboxListenerStatus();
        RaiseConnectionStateProperties();
        RaisePropertyChanged(nameof(ManagedChannelPointRewardHelpText));
    }

    private string LocalizeAccountStatusText(string status)
    {
        if (string.IsNullOrWhiteSpace(status))
        {
            return status;
        }

        const string connectedPrefix = "Connected as ";
        const string botConnectedSuffix = ". Bot announcements are ready.";

        if (status.StartsWith(connectedPrefix, StringComparison.Ordinal)
            && status.EndsWith(botConnectedSuffix, StringComparison.Ordinal))
        {
            var displayName = status.Substring(connectedPrefix.Length, status.Length - connectedPrefix.Length - botConnectedSuffix.Length);
            return TF("Connected as {0}. Bot announcements are ready.", displayName);
        }

        const string broadcasterReconnectSuffix = ", but reconnect Twitch to refresh the background listener.";
        if (status.StartsWith(connectedPrefix, StringComparison.Ordinal)
            && status.EndsWith(broadcasterReconnectSuffix, StringComparison.Ordinal))
        {
            var displayName = status.Substring(connectedPrefix.Length, status.Length - connectedPrefix.Length - broadcasterReconnectSuffix.Length);
            return TF("Connected as {0}, but reconnect Twitch to refresh the background listener.", displayName);
        }

        const string botReconnectSuffix = ", but reconnect Twitch to restore bot announcements.";
        if (status.StartsWith(connectedPrefix, StringComparison.Ordinal)
            && status.EndsWith(botReconnectSuffix, StringComparison.Ordinal))
        {
            var displayName = status.Substring(connectedPrefix.Length, status.Length - connectedPrefix.Length - botReconnectSuffix.Length);
            return TF("Connected as {0}, but reconnect Twitch to restore bot announcements.", displayName);
        }

        if (status.StartsWith(connectedPrefix, StringComparison.Ordinal)
            && status.EndsWith(".", StringComparison.Ordinal))
        {
            var displayName = status.Substring(connectedPrefix.Length, status.Length - connectedPrefix.Length - 1);
            return TF("Connected as {0}.", displayName);
        }

        return T(status);
    }

    private void RefreshStreamingStatusCard()
    {
    }

    private sealed record ManagedRewardOwnershipEntry(
        Guid Id,
        string RewardId,
        string RewardTitle,
        TwitchRewardSyncMode RewardSyncMode);

    private sealed record SharedAvatarSetRewardGroup(
        TriggerRule Owner,
        IReadOnlyList<TriggerRule> Rules,
        string RewardTitle,
        bool UsesSetTriggerMasterReward,
        TwitchRewardSyncMode RewardSyncMode);

    private sealed class ManagedRewardApiCallCounter
    {
        public int CatalogReads { get; set; }

        public int ManageableCatalogReads { get; set; }

        public int Creates { get; set; }

        public int Updates { get; set; }

        public int Deletes { get; set; }

        public int Total => CatalogReads + ManageableCatalogReads + Creates + Updates + Deletes;

        public string Describe()
        {
            var manageableText = ManageableCatalogReads > 0
                ? $", {ManageableCatalogReads} manageable GET"
                : string.Empty;
            return $"{Total} total ({CatalogReads} GET{manageableText}, {Creates} create, {Updates} update, {Deletes} delete)";
        }
    }

    private sealed class ManagedRewardSyncTarget
    {
        public ManagedRewardSyncTarget(
            Guid id,
            string displayTitle,
            string rewardId,
            string rewardTitle,
            int rewardCost,
            TwitchRewardSyncMode rewardSyncMode,
            int cooldownSeconds,
            string backgroundColor,
            string prompt,
            bool requireUserInput,
            bool desiredEnabled,
            bool isCooldownActive,
            bool deleteWhenInactive,
            bool protectFromCapReclaim,
            Action<string> applyRewardId)
        {
            Id = id;
            DisplayTitle = displayTitle;
            RewardId = rewardId?.Trim() ?? string.Empty;
            RewardTitle = rewardTitle?.Trim() ?? string.Empty;
            RewardCost = Math.Max(1, rewardCost);
            RewardSyncMode = Enum.IsDefined(rewardSyncMode)
                ? rewardSyncMode
                : TwitchRewardSyncMode.CreateOrManage;
            CooldownSeconds = Math.Max(0, cooldownSeconds);
            BackgroundColor = backgroundColor;
            Prompt = prompt?.Trim() ?? string.Empty;
            RequireUserInput = requireUserInput;
            DesiredEnabled = desiredEnabled;
            IsCooldownActive = isCooldownActive;
            DeleteWhenInactive = deleteWhenInactive;
            ProtectFromCapReclaim = protectFromCapReclaim;
            ApplyRewardId = applyRewardId;
        }

        public Guid Id { get; }

        public string DisplayTitle { get; }

        public string RewardId { get; set; }

        public string RewardTitle { get; }

        public int RewardCost { get; }

        public TwitchRewardSyncMode RewardSyncMode { get; }

        public bool UsesLinkedExistingReward => RewardSyncMode == TwitchRewardSyncMode.LinkExisting;

        public int CooldownSeconds { get; }

        public string BackgroundColor { get; }

        public string Prompt { get; }

        public bool RequireUserInput { get; }

        public bool DesiredEnabled { get; }

        public bool IsCooldownActive { get; }

        public bool DeleteWhenInactive { get; }

        public bool ProtectFromCapReclaim { get; }

        public Action<string> ApplyRewardId { get; }
    }

    private sealed class ManagedRewardSyncCatalog
    {
        private readonly List<TwitchApiClient.CustomRewardResponse> rewards = [];
        private readonly Dictionary<string, TwitchApiClient.CustomRewardResponse> rewardsById = new(StringComparer.Ordinal);
        private readonly Dictionary<string, List<TwitchApiClient.CustomRewardResponse>> rewardsByTitle = new(StringComparer.OrdinalIgnoreCase);

        public ManagedRewardSyncCatalog(IEnumerable<TwitchApiClient.CustomRewardResponse> rewards)
        {
            Reset(rewards);
        }

        public IReadOnlyList<TwitchApiClient.CustomRewardResponse> Rewards => rewards;

        public void Reset(IEnumerable<TwitchApiClient.CustomRewardResponse> refreshedRewards)
        {
            rewards.Clear();
            rewardsById.Clear();
            rewardsByTitle.Clear();

            foreach (var reward in refreshedRewards)
            {
                rewards.Add(reward);
                rewardsById[reward.Id] = reward;
                AddTitleLookup(reward);
            }
        }

        public bool TryGetById(string rewardId, out TwitchApiClient.CustomRewardResponse reward)
        {
            return rewardsById.TryGetValue(rewardId.Trim(), out reward!);
        }

        public void Replace(TwitchApiClient.CustomRewardResponse reward)
        {
            if (rewardsById.TryGetValue(reward.Id, out var existingReward))
            {
                RemoveTitleLookup(existingReward);
                var existingIndex = rewards.FindIndex(current => string.Equals(current.Id, reward.Id, StringComparison.Ordinal));
                if (existingIndex >= 0)
                {
                    rewards[existingIndex] = reward;
                }
            }
            else
            {
                rewards.Add(reward);
            }

            rewardsById[reward.Id] = reward;
            AddTitleLookup(reward);
        }

        public bool Remove(string rewardId)
        {
            if (!rewardsById.Remove(rewardId.Trim(), out var existingReward))
            {
                return false;
            }

            RemoveTitleLookup(existingReward);
            var existingIndex = rewards.FindIndex(reward => string.Equals(reward.Id, existingReward.Id, StringComparison.Ordinal));
            if (existingIndex >= 0)
            {
                rewards.RemoveAt(existingIndex);
            }

            return true;
        }

        public TwitchApiClient.CustomRewardResponse? FindFirstRecyclableReward(
            IReadOnlyCollection<string> claimedRewardIds,
            IReadOnlyCollection<string> desiredManagedRewardTitleKeys)
        {
            foreach (var reward in rewards)
            {
                var rewardTitleKey = ManagedRewardPresentation.NormalizeTitleIdentityKey(reward.Title);
                if (!claimedRewardIds.Contains(reward.Id)
                    && !reward.IsEnabled
                    && ManagedRewardPresentation.IsManagedTitle(reward.Title)
                    && !desiredManagedRewardTitleKeys.Contains(rewardTitleKey))
                {
                    return reward;
                }
            }

            return null;
        }

        public TwitchApiClient.CustomRewardResponse? FindFirstCapReclaimCandidate(
            IReadOnlyCollection<string> manageableRewardIds,
            IReadOnlyCollection<string> protectedRewardIds,
            IReadOnlyCollection<string> protectedRewardTitleKeys)
        {
            foreach (var reward in rewards
                         .OrderBy(reward => reward.Title, StringComparer.OrdinalIgnoreCase)
                         .ThenBy(reward => reward.Id, StringComparer.Ordinal))
            {
                var rewardTitleKey = ManagedRewardPresentation.NormalizeTitleIdentityKey(reward.Title);
                if (manageableRewardIds.Contains(reward.Id)
                    && !protectedRewardIds.Contains(reward.Id)
                    && !reward.IsEnabled
                    && ManagedRewardPresentation.IsManagedTitle(reward.Title)
                    && !protectedRewardTitleKeys.Contains(rewardTitleKey))
                {
                    return reward;
                }
            }

            return null;
        }

        public IReadOnlyList<TwitchApiClient.CustomRewardResponse> GetStaleRewards(
            IReadOnlyCollection<string> claimedRewardIds,
            IReadOnlyCollection<string> desiredManagedRewardTitleKeys)
        {
            var staleRewards = new List<TwitchApiClient.CustomRewardResponse>();
            foreach (var reward in rewards)
            {
                var rewardTitleKey = ManagedRewardPresentation.NormalizeTitleIdentityKey(reward.Title);
                if (!claimedRewardIds.Contains(reward.Id)
                    && !reward.IsEnabled
                    && ManagedRewardPresentation.IsManagedTitle(reward.Title)
                    && !desiredManagedRewardTitleKeys.Contains(rewardTitleKey))
                {
                    staleRewards.Add(reward);
                }
            }

            return staleRewards;
        }

        public IEnumerable<TwitchApiClient.CustomRewardResponse> FindByTitleVariants(string rewardTitle)
        {
            var seenRewardIds = new HashSet<string>(StringComparer.Ordinal);
            var titleKey = ManagedRewardPresentation.NormalizeTitleIdentityKey(rewardTitle);
            if (string.IsNullOrWhiteSpace(titleKey))
            {
                yield break;
            }

            if (!rewardsByTitle.TryGetValue(titleKey, out var matches))
            {
                yield break;
            }

            foreach (var reward in matches)
            {
                if (seenRewardIds.Add(reward.Id))
                {
                    yield return reward;
                }
            }
        }

        private void AddTitleLookup(TwitchApiClient.CustomRewardResponse reward)
        {
            var titleKey = ManagedRewardPresentation.NormalizeTitleIdentityKey(reward.Title);
            if (string.IsNullOrWhiteSpace(titleKey))
            {
                return;
            }

            if (!rewardsByTitle.TryGetValue(titleKey, out var matches))
            {
                matches = [];
                rewardsByTitle[titleKey] = matches;
            }

            matches.Add(reward);
        }

        private void RemoveTitleLookup(TwitchApiClient.CustomRewardResponse reward)
        {
            var titleKey = ManagedRewardPresentation.NormalizeTitleIdentityKey(reward.Title);
            if (string.IsNullOrWhiteSpace(titleKey)
                || !rewardsByTitle.TryGetValue(titleKey, out var matches))
            {
                return;
            }

            for (var index = matches.Count - 1; index >= 0; index--)
            {
                if (string.Equals(matches[index].Id, reward.Id, StringComparison.Ordinal))
                {
                    matches.RemoveAt(index);
                }
            }

            if (matches.Count == 0)
            {
                rewardsByTitle.Remove(titleKey);
            }
        }
    }

    private sealed class ManagedRewardRuleOwnershipIndex
    {
        private readonly Dictionary<string, HashSet<Guid>> ruleIdsByRewardId = new(StringComparer.Ordinal);
        private readonly Dictionary<string, HashSet<Guid>> ruleIdsByRewardTitle = new(StringComparer.OrdinalIgnoreCase);

        public ManagedRewardRuleOwnershipIndex(IEnumerable<ManagedRewardOwnershipEntry> entries)
        {
            foreach (var entry in entries)
            {
                RegisterRule(entry);
            }
        }

        public bool IsOwnedByAnotherRule(Guid currentOwnerId, TwitchApiClient.CustomRewardResponse reward)
        {
            var rewardId = reward.Id?.Trim() ?? string.Empty;
            if (!string.IsNullOrWhiteSpace(rewardId)
                && TryHasOtherOwner(ruleIdsByRewardId, rewardId, currentOwnerId))
            {
                return true;
            }

            var rewardTitleKey = ManagedRewardPresentation.NormalizeTitleIdentityKey(reward.Title);
            return !string.IsNullOrWhiteSpace(rewardTitleKey)
                && TryHasOtherOwner(ruleIdsByRewardTitle, rewardTitleKey, currentOwnerId);
        }

        public void UpdateRewardId(Guid ownerId, string previousRewardId, string currentRewardId)
        {
            var normalizedPreviousRewardId = previousRewardId?.Trim() ?? string.Empty;
            var normalizedCurrentRewardId = currentRewardId?.Trim() ?? string.Empty;

            if (!string.IsNullOrWhiteSpace(normalizedPreviousRewardId))
            {
                RemoveOwner(ruleIdsByRewardId, normalizedPreviousRewardId, ownerId);
            }

            if (!string.IsNullOrWhiteSpace(normalizedCurrentRewardId))
            {
                AddOwner(ruleIdsByRewardId, normalizedCurrentRewardId, ownerId);
            }
        }

        public void RemoveRewardId(string rewardId)
        {
            var normalizedRewardId = rewardId?.Trim() ?? string.Empty;
            if (!string.IsNullOrWhiteSpace(normalizedRewardId))
            {
                ruleIdsByRewardId.Remove(normalizedRewardId);
            }
        }

        private void RegisterRule(ManagedRewardOwnershipEntry entry)
        {
            var rewardId = entry.RewardId?.Trim() ?? string.Empty;
            if (!string.IsNullOrWhiteSpace(rewardId))
            {
                AddOwner(ruleIdsByRewardId, rewardId, entry.Id);
            }

            if (entry.RewardSyncMode != TwitchRewardSyncMode.CreateOrManage)
            {
                return;
            }

            var rewardTitleKey = ManagedRewardPresentation.NormalizeTitleIdentityKey(entry.RewardTitle);
            if (string.IsNullOrWhiteSpace(rewardTitleKey))
            {
                return;
            }

            AddOwner(ruleIdsByRewardTitle, rewardTitleKey, entry.Id);
        }

        private static bool TryHasOtherOwner(
            Dictionary<string, HashSet<Guid>> lookup,
            string key,
            Guid currentRuleId)
        {
            if (!lookup.TryGetValue(key, out var owners))
            {
                return false;
            }

            foreach (var ownerId in owners)
            {
                if (ownerId != currentRuleId)
                {
                    return true;
                }
            }

            return false;
        }

        private static void AddOwner(
            Dictionary<string, HashSet<Guid>> lookup,
            string key,
            Guid ruleId)
        {
            if (!lookup.TryGetValue(key, out var owners))
            {
                owners = [];
                lookup[key] = owners;
            }

            owners.Add(ruleId);
        }

        private static void RemoveOwner(
            Dictionary<string, HashSet<Guid>> lookup,
            string key,
            Guid ruleId)
        {
            if (!lookup.TryGetValue(key, out var owners))
            {
                return;
            }

            owners.Remove(ruleId);
            if (owners.Count == 0)
            {
                lookup.Remove(key);
            }
        }
    }

    private void UpdateOscStatusSummary()
    {
        if (bridgeCoordinator.IsOscActive)
        {
            OscBridgeSummary = bridgeCoordinator.HasDiscoveredVrChat
                ? T("OSC is transmitting and working.")
                : T("OSC is waiting for VRChat.");
        }
        else if (!Settings.Broadcaster.IsConnected)
        {
            OscBridgeSummary = T("OSC waiting for broadcaster login.");
        }
        else if (BridgeStatus.Contains("could not", StringComparison.OrdinalIgnoreCase)
            || BridgeStatus.Contains("failed", StringComparison.OrdinalIgnoreCase)
            || BridgeStatus.Contains("error", StringComparison.OrdinalIgnoreCase))
        {
            OscBridgeSummary = T("OSC needs attention.");
        }
        else if (BridgeStatus.Contains("stopped", StringComparison.OrdinalIgnoreCase))
        {
            OscBridgeSummary = T("OSC is offline.");
        }
        else if (BridgeStatus.Contains("refresh", StringComparison.OrdinalIgnoreCase))
        {
            OscBridgeSummary = T("OSC is refreshing.");
        }
        else if (BridgeStatus.Contains("VRChat", StringComparison.OrdinalIgnoreCase)
            && (BridgeStatus.Contains("waiting", StringComparison.OrdinalIgnoreCase)
                || BridgeStatus.Contains("looking", StringComparison.OrdinalIgnoreCase)))
        {
            OscBridgeSummary = T("OSC is waiting for VRChat.");
        }
        else if (BridgeStatus.Contains("reconnect", StringComparison.OrdinalIgnoreCase)
            || BridgeStatus.Contains("connect", StringComparison.OrdinalIgnoreCase)
            || BridgeStatus.Contains("live", StringComparison.OrdinalIgnoreCase)
            || BridgeStatus.Contains("listening", StringComparison.OrdinalIgnoreCase))
        {
            if (!OscBridgeSummary.Contains("VRChat", StringComparison.OrdinalIgnoreCase)
                && !OscBridgeSummary.Contains("transmitting", StringComparison.OrdinalIgnoreCase))
            {
                OscBridgeSummary = T("OSC is starting up.");
            }
        }
        else
        {
            OscBridgeSummary = T("OSC standing by.");
        }

        RaisePropertyChanged(nameof(IsOscConnected));
        RaisePropertyChanged(nameof(IsOscDisconnected));
    }

    private void UpdateChatboxListenerStatus()
    {
        if (!Settings.Broadcaster.IsConnected)
        {
            ChatboxListenerStatus = T("Connect broadcaster to start Twitch Chatbox.");
            return;
        }

        if (!HasBroadcasterChatScope())
        {
            ChatboxListenerStatus = T("Reconnect broadcaster once to enable Twitch chat read access.");
            return;
        }

        if (BridgeStatus.Contains("error", StringComparison.OrdinalIgnoreCase)
            || BridgeStatus.Contains("failed", StringComparison.OrdinalIgnoreCase)
            || BridgeStatus.Contains("could not", StringComparison.OrdinalIgnoreCase))
        {
            ChatboxListenerStatus = T("Chatbox is waiting for the bridge to recover.");
            return;
        }

        if (BridgeStatus.Contains("listening", StringComparison.OrdinalIgnoreCase)
            || BridgeStatus.Contains("live", StringComparison.OrdinalIgnoreCase)
            || BridgeStatus.Contains("working", StringComparison.OrdinalIgnoreCase))
        {
            ChatboxListenerStatus = T("Chatbox is connected and listening.");
            return;
        }

        if (BridgeStatus.Contains("connect", StringComparison.OrdinalIgnoreCase)
            || BridgeStatus.Contains("refresh", StringComparison.OrdinalIgnoreCase)
            || BridgeStatus.Contains("starting", StringComparison.OrdinalIgnoreCase))
        {
            ChatboxListenerStatus = T("Chatbox is connecting...");
            return;
        }

        ChatboxListenerStatus = T("Chatbox is standing by.");
    }

    private bool HasBroadcasterChatScope()
    {
        return Settings.Broadcaster.Scopes.Any(scope =>
            string.Equals(scope, "user:read:chat", StringComparison.OrdinalIgnoreCase));
    }

    private bool HasBroadcasterChatWriteScope() => HasScope(Settings.Broadcaster, TwitchScopes.ChatWrite);

    private string BuildEffectiveBotSenderStatusText()
    {
        if (Settings.UseBroadcasterAsBotSender)
        {
            if (!Settings.Broadcaster.IsConnected)
            {
                return T("Connect your broadcaster account before using the broadcaster as the chat sender.");
            }

            if (!HasBroadcasterChatWriteScope())
            {
                return T("Reconnect your broadcaster once so Crystal Relay can send Twitch chat messages from the broadcaster account.");
            }

            var displayName = string.IsNullOrWhiteSpace(Settings.Broadcaster.DisplayName)
                ? Settings.Broadcaster.Login
                : Settings.Broadcaster.DisplayName;
            return TF("Chat messages will send as broadcaster {0}.", displayName);
        }

        if (Settings.Bot.IsConnected)
        {
            var displayName = string.IsNullOrWhiteSpace(Settings.Bot.DisplayName)
                ? Settings.Bot.Login
                : Settings.Bot.DisplayName;
            return TF("Chat messages will send as bot {0}.", displayName);
        }

        return T("Connect a bot account or enable broadcaster-as-bot to send Twitch chat messages.");
    }

    private string BuildBuiltInCommandsSummaryText()
    {
        var enabledCount = 0;
        if (Settings.WorldCommandEnabled && ChatCommandUtility.IsConfigured(Settings.WorldCommandText))
        {
            enabledCount++;
        }

        if (Settings.TriggerInfoCommandEnabled && ChatCommandUtility.IsConfigured(Settings.TriggerInfoCommandText))
        {
            enabledCount++;
        }

        if (Settings.PauseCommandEnabled && ChatCommandUtility.IsConfigured(Settings.PauseCommandText))
        {
            enabledCount++;
        }

        if (Settings.RedeemGroupCommandEnabled)
        {
            enabledCount += Settings.RedeemGroups.Count(g => ChatCommandUtility.IsConfigured(g.CommandText));
        }

        if (Settings.RedeemControlCommandEnabled)
        {
            enabledCount++;
        }

        return enabledCount switch
        {
            0 => T("No built-in bot commands are enabled."),
            1 => T("1 built-in bot command is enabled."),
            _ => TF("{0} built-in bot commands are enabled.", enabledCount)
        };
    }

    private string BuildBuiltInCommandsWarningText()
    {
        var worldCommand = Settings.WorldCommandEnabled
            ? ChatCommandUtility.Normalize(Settings.WorldCommandText)
            : string.Empty;
        var triggerInfoCommand = Settings.TriggerInfoCommandEnabled
            ? ChatCommandUtility.Normalize(Settings.TriggerInfoCommandText)
            : string.Empty;

        if (ChatCommandUtility.IsConfigured(worldCommand)
            && ChatCommandUtility.MessageMatches(worldCommand, triggerInfoCommand))
        {
            return T("Two built-in commands use the same chat command. The first matching built-in command runs first, so rename or disable one of them.");
        }

        var builtInCommands = new[] { worldCommand, triggerInfoCommand }
            .Where(ChatCommandUtility.IsConfigured)
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToArray();

        if (builtInCommands.Length > 0
            && Settings.UniversalTriggers.Any(trigger =>
                trigger.IsEnabled
                && trigger.TriggerType == UniversalTriggerType.ChatCommand
                && builtInCommands.Any(command => ChatCommandUtility.MessageMatches(command, trigger.CommandText))))
        {
            return T("A Universal Trigger uses the same command as an enabled built-in command. Built-in commands run first, so the Universal Trigger will only run if the built-in command is disabled or renamed.");
        }

        return string.Empty;
    }

    private void RaiseBuiltInCommandStateProperties()
    {
        RaisePropertyChanged(nameof(BuiltInCommandsSummaryText));
        RaisePropertyChanged(nameof(BuiltInCommandsWarningText));
        RaisePropertyChanged(nameof(HasBuiltInCommandsWarning));
    }

    private static bool IsBuiltInCommandSettingsProperty(string? propertyName) =>
        propertyName is nameof(AppSettings.WorldCommandEnabled)
            or nameof(AppSettings.WorldCommandText)
            or nameof(AppSettings.WorldCommandCooldownSeconds)
            or nameof(AppSettings.WorldCommandPermission)
            or nameof(AppSettings.TriggerInfoCommandEnabled)
            or nameof(AppSettings.TriggerInfoCommandText)
            or nameof(AppSettings.TriggerInfoCommandCooldownSeconds)
            or nameof(AppSettings.TriggerInfoCommandPermission);

    private void UpdateOscStatusFromLog(string message)
    {
        if (string.IsNullOrWhiteSpace(message))
        {
            return;
        }

        if (message.Contains("Discovered VRChat through OSCQuery", StringComparison.OrdinalIgnoreCase))
        {
            OscStatusDetail = T("VRChat is connected through OSCQuery.");
            OscBridgeSummary = T("OSC is transmitting and working.");
            RaisePropertyChanged(nameof(ChatboxOscRelayStatusText));
            QueueCurrentVrChatAvatarRefresh(0);
            return;
        }

        if (message.Contains("Searching for VRChat through OSCQuery", StringComparison.OrdinalIgnoreCase))
        {
            OscStatusDetail = T("OSCQuery receiver is online and searching for VRChat.");
            OscBridgeSummary = T("OSC is waiting for VRChat.");
            RaisePropertyChanged(nameof(ChatboxOscRelayStatusText));
            return;
        }

        if (message.Contains("OSCQuery service", StringComparison.OrdinalIgnoreCase))
        {
            OscStatusDetail = T("OSCQuery receiver is online.");
            OscBridgeSummary = T("OSCQuery is online.");
            RaisePropertyChanged(nameof(ChatboxOscRelayStatusText));
            return;
        }

        if (message.Contains("Forcing an OSCQuery refresh", StringComparison.OrdinalIgnoreCase))
        {
            OscStatusDetail = T("Refreshing the OSCQuery connection to VRChat.");
            OscBridgeSummary = T("OSC is refreshing.");
            RaisePropertyChanged(nameof(ChatboxOscRelayStatusText));
            return;
        }

        if (message.Contains("lost the OSCQuery connection to VRChat", StringComparison.OrdinalIgnoreCase))
        {
            OscStatusDetail = T("Crystal Relay lost the OSCQuery connection to VRChat and is waiting for it to come back.");
            OscBridgeSummary = T("OSC is waiting for VRChat.");
            RaisePropertyChanged(nameof(ChatboxOscRelayStatusText));
            return;
        }

        if (message.Contains("OSCQuery discovery refresh failed", StringComparison.OrdinalIgnoreCase)
            || message.Contains("OSC receive error", StringComparison.OrdinalIgnoreCase)
            || message.Contains("could not find VRChat through OSCQuery", StringComparison.OrdinalIgnoreCase)
            || message.Contains("OSCQuery is not running yet", StringComparison.OrdinalIgnoreCase)
            || message.Contains("did not expose any avatar parameters through OSCQuery", StringComparison.OrdinalIgnoreCase))
        {
            OscStatusDetail = message;
            OscBridgeSummary = T("OSC needs attention.");
            RaisePropertyChanged(nameof(ChatboxOscRelayStatusText));
            return;
        }
    }

    private void SetAccountStatus(BridgeAccountRole accountRole, string status)
    {
        if (accountRole == BridgeAccountRole.Broadcaster)
        {
            BroadcasterStatus = status;
        }
        else
        {
            BotStatus = status;
        }
    }

    private void SetDeviceFlow(BridgeAccountRole accountRole, string code, string verificationUri)
    {
        if (accountRole == BridgeAccountRole.Broadcaster)
        {
            BroadcasterDeviceCode = code;
            BroadcasterVerificationUri = verificationUri;
        }
        else
        {
            BotDeviceCode = code;
            BotVerificationUri = verificationUri;
        }
    }

    private void ClearBroadcasterDeviceFlow()
    {
        BroadcasterDeviceCode = string.Empty;
        BroadcasterVerificationUri = string.Empty;
    }

    private void ClearBotDeviceFlow()
    {
        BotDeviceCode = string.Empty;
        BotVerificationUri = string.Empty;
    }

    private void ApplyAccountSnapshot(BridgeAccountRole accountRole, TwitchAccountSnapshot snapshot)
    {
        var accountSettings = BridgeRuntimeConfiguration.ToSettings(snapshot);

        if (accountRole == BridgeAccountRole.Broadcaster)
        {
            broadcasterReconnectRequired = false;
            Settings.Broadcaster.Apply(accountSettings);
            if (BroadcasterCanManageRewards)
            {
                ClearBroadcasterManagedRewardsUnavailableForSession();
            }

            QueueLiveFeedbackHeartbeatEvaluation();
        }
        else
        {
            botReconnectRequired = false;
            Settings.Bot.Apply(accountSettings);
        }
    }

    internal void AppendLog(string message)
    {
        DebugLogService.Write(message);
        var timestampedMessage = $"[{DateTime.Now:HH:mm:ss}] {message}";
        if (LogEntries.Count >= MaxLogEntryCount)
        {
            LogEntries.RemoveAt(LogEntries.Count - 1);
        }

        LogEntries.Insert(0, timestampedMessage);
    }

    internal void AppendDiagnosticLog(string message) => AppendLog(message);

    private ApplicationUpdateInfo? LogAndReturnNoUpdate(string message)
    {
        AppendLog(message);
        return null;
    }

    internal void AppendThrottledLog(string key, string message, TimeSpan throttleWindow)
    {
        var now = DateTimeOffset.UtcNow;
        PruneExpiredTimestampEntries(throttledLogExpiryByKey, now);
        if (throttledLogExpiryByKey.TryGetValue(key, out var expiry)
            && expiry > now)
        {
            return;
        }

        throttledLogExpiryByKey[key] = now.Add(throttleWindow);
        AppendLog(message);
    }

    private static void PruneExpiredTimestampEntries(
        IDictionary<string, DateTimeOffset> entries,
        DateTimeOffset now)
    {
        if (entries.Count == 0)
        {
            return;
        }

        List<string>? expiredKeys = null;
        foreach (var pair in entries)
        {
            if (pair.Value <= now)
            {
                expiredKeys ??= [];
                expiredKeys.Add(pair.Key);
            }
        }

        if (expiredKeys is null)
        {
            return;
        }

        foreach (var expiredKey in expiredKeys)
        {
            entries.Remove(expiredKey);
        }
    }

    private void AppendChatMessage(BridgeChatMessage message)
    {
        if (string.IsNullOrWhiteSpace(message.MessageText))
        {
            return;
        }

        if (ChatMessages.Count >= MaxChatMessageCount)
        {
            ChatMessages.RemoveAt(0);
        }

        var entry = new TwitchChatMessageEntry(
            message.UserDisplayName,
            message.UserLogin,
            message.UserId,
            message.MessageText,
            message.UserColor,
            [.. message.BadgeImageUrls],
            [.. message.BadgeSetIds],
            BuildChatMessageFragments(message),
            message.Kind == BridgeChatMessageKind.Chat && ShouldPlayViewerChatSound(message),
            message.ReceivedAt,
            SelectedTheme,
            Settings.ChatTimestampFormat,
            MapChatMessageEntryKind(message.Kind),
            message.RewardTitle,
            message.RewardCost,
            message.RewardUserInput,
            message.SupportAmount,
            message.SupportTier,
            message.SupportMonths,
            message.SupportMessage,
            message.MessageId,
            message.MessageType,
            message.SourceBroadcasterUserId,
            message.SourceBroadcasterUserLogin,
            message.SourceBroadcasterUserName,
            message.SourceMessageId,
            message.IsSourceOnly);
        ApplyKnownSuspiciousStatus(entry);
        ChatMessages.Add(entry);
        var activity = BuildChatMessageActivity(message);
        if (activity is not null)
        {
            AppendChatActivity(activity);
        }
    }

    private BridgeChatActivity? BuildChatMessageActivity(BridgeChatMessage message)
    {
        var kind = message.Kind switch
        {
            BridgeChatMessageKind.ChannelPointRedemption => BridgeChatActivityKind.ChannelPointRedemption,
            BridgeChatMessageKind.BitsCheer
                or BridgeChatMessageKind.Subscription
                or BridgeChatMessageKind.Resubscription
                or BridgeChatMessageKind.GiftSubscription
                or BridgeChatMessageKind.Raid => BridgeChatActivityKind.SupportEvent,
            _ => (BridgeChatActivityKind?)null
        };
        if (kind is null)
        {
            return null;
        }

        var text = message.Kind switch
        {
            BridgeChatMessageKind.ChannelPointRedemption => TF("{0} redeemed {1}.", message.UserDisplayName, message.RewardTitle),
            BridgeChatMessageKind.BitsCheer => TF("{0} cheered {1:N0} Bits.", message.UserDisplayName, message.SupportAmount),
            BridgeChatMessageKind.Subscription => TF("{0} subscribed.", message.UserDisplayName),
            BridgeChatMessageKind.Resubscription => TF("{0} resubbed.", message.UserDisplayName),
            BridgeChatMessageKind.GiftSubscription => TF("{0} gifted {1:N0} subs.", message.UserDisplayName, message.SupportAmount),
            BridgeChatMessageKind.Raid => TF("{0} raided with {1:N0} viewers.", message.UserDisplayName, message.SupportAmount),
            _ => string.Empty
        };

        return new BridgeChatActivity(kind.Value, text, message.ReceivedAt)
        {
            TargetUserDisplayName = message.UserDisplayName,
            TargetUserLogin = message.UserLogin,
            TargetUserId = message.UserId,
            MessageId = message.MessageId
        };
    }

    private static TwitchChatMessageEntryKind MapChatMessageEntryKind(BridgeChatMessageKind kind) => kind switch
    {
        BridgeChatMessageKind.ChannelPointRedemption => TwitchChatMessageEntryKind.ChannelPointRedemption,
        BridgeChatMessageKind.BitsCheer => TwitchChatMessageEntryKind.BitsCheer,
        BridgeChatMessageKind.Subscription => TwitchChatMessageEntryKind.Subscription,
        BridgeChatMessageKind.Resubscription => TwitchChatMessageEntryKind.Resubscription,
        BridgeChatMessageKind.GiftSubscription => TwitchChatMessageEntryKind.GiftSubscription,
        BridgeChatMessageKind.Raid => TwitchChatMessageEntryKind.Raid,
        _ => TwitchChatMessageEntryKind.Chat
    };

    private IReadOnlyList<TwitchChatInlineFragment> BuildChatMessageFragments(BridgeChatMessage message)
    {
        if (message.Fragments.Count > 0)
        {
            return [.. message.Fragments.Select(fragment => new TwitchChatInlineFragment(
                fragment.Kind == BridgeChatFragmentKind.Emote ? TwitchChatInlineFragmentKind.Emote : TwitchChatInlineFragmentKind.Text,
                fragment.Text,
                fragment.ImageUrl))];
        }

        return [new TwitchChatInlineFragment(TwitchChatInlineFragmentKind.Text, message.MessageText, string.Empty)];
    }

    private bool ShouldPlayViewerChatSound(BridgeChatMessage message)
    {
        var normalizedLogin = message.UserLogin?.Trim() ?? string.Empty;
        if (string.IsNullOrWhiteSpace(normalizedLogin))
        {
            return false;
        }

        if (string.Equals(normalizedLogin, Settings.Broadcaster.Login?.Trim() ?? string.Empty, StringComparison.OrdinalIgnoreCase))
        {
            return false;
        }

        if (Settings.Bot.IsConnected
            && string.Equals(normalizedLogin, Settings.Bot.Login?.Trim() ?? string.Empty, StringComparison.OrdinalIgnoreCase))
        {
            return false;
        }

        return !KnownViewerNotificationBotLogins.Contains(normalizedLogin);
    }

    private void RefreshChatMessageTimestampDisplay()
    {
        foreach (var chatMessage in ChatMessages)
        {
            chatMessage.ApplyTimestampFormat(Settings.ChatTimestampFormat);
        }
    }

    public void ClearChatMessages()
    {
        ChatMessages.Clear();
        SelectedChatMessage = null;
    }

    private void AppendChatActivity(BridgeChatActivity activity)
    {
        ApplyChatActivitySideEffects(activity);
        if (!ShouldDisplayChatActivity(activity)
            || ShouldSuppressDuplicateChatActivity(activity))
        {
            return;
        }

        if (string.IsNullOrWhiteSpace(activity.MessageText))
        {
            return;
        }

        if (ChatActivityEntries.Count >= MaxChatActivityEntryCount)
        {
            ChatActivityEntries.RemoveAt(ChatActivityEntries.Count - 1);
        }

        ChatActivityEntries.Insert(0, new TwitchChatActivityEntry(activity));
    }

    private static bool ShouldDisplayChatActivity(BridgeChatActivity activity) =>
        activity.Kind != BridgeChatActivityKind.SuspiciousUserMessage;

    private bool ShouldSuppressDuplicateChatActivity(BridgeChatActivity activity)
    {
        var key = BuildChatActivityDedupeKey(activity);
        if (string.IsNullOrWhiteSpace(key))
        {
            return false;
        }

        var now = DateTimeOffset.UtcNow;
        PruneRecentChatActivityKeys(now);
        if (recentChatActivityKeys.TryGetValue(key, out var seenAt)
            && now - seenAt <= ChatActivityDedupeWindow)
        {
            recentChatActivityKeys[key] = now;
            return true;
        }

        recentChatActivityKeys[key] = now;
        return false;
    }

    private void PruneRecentChatActivityKeys(DateTimeOffset now)
    {
        foreach (var expiredKey in recentChatActivityKeys
                     .Where(pair => now - pair.Value > ChatActivityDedupeWindow)
                     .Select(pair => pair.Key)
                     .ToArray())
        {
            recentChatActivityKeys.Remove(expiredKey);
        }
    }

    private static string BuildChatActivityDedupeKey(BridgeChatActivity activity)
    {
        var targetKey = !string.IsNullOrWhiteSpace(activity.TargetUserId)
            ? activity.TargetUserId.Trim()
            : activity.TargetUserLogin.Trim().ToUpperInvariant();
        return activity.Kind switch
        {
            BridgeChatActivityKind.MessageDeleted when !string.IsNullOrWhiteSpace(activity.MessageId) =>
                $"delete:{activity.MessageId.Trim()}",
            BridgeChatActivityKind.UserMessagesCleared or BridgeChatActivityKind.MessagePurged when !string.IsNullOrWhiteSpace(targetKey) =>
                $"purge:{targetKey}",
            BridgeChatActivityKind.SuspiciousUserUpdated when !string.IsNullOrWhiteSpace(targetKey) =>
                $"suspicious:{targetKey}:{activity.SuspiciousStatus.Trim().ToUpperInvariant()}",
            BridgeChatActivityKind.ChatCleared => "chat-cleared",
            _ => string.Empty
        };
    }

    private void ApplyChatActivitySideEffects(BridgeChatActivity activity)
    {
        switch (activity.Kind)
        {
            case BridgeChatActivityKind.MessageDeleted:
                RemoveChatMessages(message => !string.IsNullOrWhiteSpace(activity.MessageId)
                    && string.Equals(message.MessageId, activity.MessageId, StringComparison.Ordinal));
                break;
            case BridgeChatActivityKind.UserMessagesCleared:
                RemoveChatMessages(message => ChatMessageMatchesUser(message, activity.TargetUserId, activity.TargetUserLogin));
                break;
            case BridgeChatActivityKind.ChatCleared:
                ChatMessages.Clear();
                SelectedChatMessage = null;
                break;
            case BridgeChatActivityKind.SuspiciousUserUpdated:
            case BridgeChatActivityKind.SuspiciousUserMessage:
                UpdateSuspiciousStatusForUser(activity.TargetUserId, activity.TargetUserLogin, activity.SuspiciousStatus);
                break;
        }
    }

    private void RemoveChatMessages(Predicate<TwitchChatMessageEntry> shouldRemove)
    {
        for (var index = ChatMessages.Count - 1; index >= 0; index--)
        {
            var message = ChatMessages[index];
            if (!shouldRemove(message))
            {
                continue;
            }

            if (ReferenceEquals(SelectedChatMessage, message))
            {
                SelectedChatMessage = null;
            }

            ChatMessages.RemoveAt(index);
        }
    }

    private static bool ChatMessageMatchesUser(TwitchChatMessageEntry message, string userId, string userLogin)
    {
        return (!string.IsNullOrWhiteSpace(userId)
                && string.Equals(message.UserId, userId, StringComparison.Ordinal))
            || (!string.IsNullOrWhiteSpace(userLogin)
                && string.Equals(message.UserLogin, userLogin, StringComparison.OrdinalIgnoreCase));
    }

    private void ApplyKnownSuspiciousStatus(TwitchChatMessageEntry entry)
    {
        if (!string.IsNullOrWhiteSpace(entry.UserId)
            && chatSuspiciousStatusesByUserId.TryGetValue(entry.UserId, out var statusById))
        {
            entry.SuspiciousStatus = statusById;
            return;
        }

        if (!string.IsNullOrWhiteSpace(entry.UserLogin)
            && chatSuspiciousStatusesByLogin.TryGetValue(entry.UserLogin, out var statusByLogin))
        {
            entry.SuspiciousStatus = statusByLogin;
        }
    }

    private void UpdateSuspiciousStatusForUser(string userId, string userLogin, string suspiciousStatus)
    {
        var status = NormalizeChatSuspiciousStatus(suspiciousStatus);
        var shouldClear = string.IsNullOrWhiteSpace(status)
            || string.Equals(status, "NO_TREATMENT", StringComparison.OrdinalIgnoreCase);

        if (!string.IsNullOrWhiteSpace(userId))
        {
            if (shouldClear)
            {
                chatSuspiciousStatusesByUserId.Remove(userId);
            }
            else
            {
                chatSuspiciousStatusesByUserId[userId] = status;
            }
        }

        if (!string.IsNullOrWhiteSpace(userLogin))
        {
            if (shouldClear)
            {
                chatSuspiciousStatusesByLogin.Remove(userLogin);
            }
            else
            {
                chatSuspiciousStatusesByLogin[userLogin] = status;
            }
        }

        foreach (var message in ChatMessages)
        {
            if (ChatMessageMatchesUser(message, userId, userLogin))
            {
                message.SuspiciousStatus = shouldClear ? string.Empty : status;
            }
        }

        if (SelectedChatMessage is not null && ChatMessageMatchesUser(SelectedChatMessage, userId, userLogin))
        {
            RaiseSelectedChatModerationProperties();
        }
    }

    private static string NormalizeChatSuspiciousStatus(string? suspiciousStatus)
    {
        var normalized = suspiciousStatus?.Trim() ?? string.Empty;
        return normalized switch
        {
            _ when normalized.Equals("ACTIVE_MONITORING", StringComparison.OrdinalIgnoreCase)
                || normalized.Equals("active_monitoring", StringComparison.OrdinalIgnoreCase) => "ACTIVE_MONITORING",
            _ when normalized.Equals("RESTRICTED", StringComparison.OrdinalIgnoreCase)
                || normalized.Equals("restricted", StringComparison.OrdinalIgnoreCase) => "RESTRICTED",
            _ when normalized.Equals("NO_TREATMENT", StringComparison.OrdinalIgnoreCase)
                || normalized.Equals("no_treatment", StringComparison.OrdinalIgnoreCase) => "NO_TREATMENT",
            _ => normalized.ToUpperInvariant()
        };
    }

    private async Task TimeoutSelectedChatUserAsync(int durationSeconds)
    {
        var target = GetSelectedChatModerationTarget();
        if (target is null)
        {
            return;
        }

        try
        {
            ChatboxModerationStatusText = TF("Timing out {0}...", target.DisplayName);
            var account = await GetBroadcasterModerationAccountAsync([TwitchScopes.ModerationBannedUsers], CancellationToken.None);
            await twitchApiClient.BanOrTimeoutUserAsync(
                account.AccessToken,
                runtimeConfig.TwitchClientId,
                account.UserId,
                account.UserId,
                target.UserId,
                durationSeconds,
                "Crystal Relay quick timeout",
                CancellationToken.None);
            var message = TF("Timed out {0} for {1}.", target.DisplayName, FormatModerationDuration(durationSeconds));
            ChatboxModerationStatusText = message;
            AppendLocalChatActivity(BridgeChatActivityKind.Timeout, message, target);
        }
        catch (Exception ex)
        {
            ReportChatModerationFailure(target, ex);
        }
    }

    private async Task BanSelectedChatUserAsync()
    {
        var target = GetSelectedChatModerationTarget();
        if (target is null)
        {
            return;
        }

        try
        {
            ChatboxModerationStatusText = TF("Banning {0}...", target.DisplayName);
            var account = await GetBroadcasterModerationAccountAsync([TwitchScopes.ModerationBannedUsers], CancellationToken.None);
            await twitchApiClient.BanOrTimeoutUserAsync(
                account.AccessToken,
                runtimeConfig.TwitchClientId,
                account.UserId,
                account.UserId,
                target.UserId,
                null,
                "Crystal Relay quick ban",
                CancellationToken.None);
            var message = TF("Banned {0}.", target.DisplayName);
            ChatboxModerationStatusText = message;
            AppendLocalChatActivity(BridgeChatActivityKind.Ban, message, target);
        }
        catch (Exception ex)
        {
            ReportChatModerationFailure(target, ex);
        }
    }

    private async Task PurgeSelectedChatUserAsync()
    {
        var target = GetSelectedChatModerationTarget();
        if (target is null)
        {
            return;
        }

        try
        {
            ChatboxModerationStatusText = TF("Purging recent chat from {0}...", target.DisplayName);
            var account = await GetBroadcasterModerationAccountAsync([TwitchScopes.ModerationBannedUsers], CancellationToken.None);
            await twitchApiClient.BanOrTimeoutUserAsync(
                account.AccessToken,
                runtimeConfig.TwitchClientId,
                account.UserId,
                account.UserId,
                target.UserId,
                1,
                "Crystal Relay purge",
                CancellationToken.None);
            var message = TF("Purged recent chat from {0}.", target.DisplayName);
            ChatboxModerationStatusText = message;
            AppendLocalChatActivity(BridgeChatActivityKind.MessagePurged, message, target);
            RemoveChatMessages(messageEntry => ChatMessageMatchesUser(messageEntry, target.UserId, target.Login));
        }
        catch (Exception ex)
        {
            ReportChatModerationFailure(target, ex);
        }
    }

    private async Task DeleteSelectedChatMessageAsync()
    {
        var target = GetSelectedChatModerationTarget();
        if (target is null || SelectedChatMessage is not { } selectedMessage || string.IsNullOrWhiteSpace(selectedMessage.MessageId))
        {
            return;
        }

        try
        {
            ChatboxModerationStatusText = TF("Deleting a message from {0}...", target.DisplayName);
            var account = await GetBroadcasterModerationAccountAsync([TwitchScopes.ModerationChatMessages], CancellationToken.None);
            await twitchApiClient.DeleteChatMessageAsync(
                account.AccessToken,
                runtimeConfig.TwitchClientId,
                account.UserId,
                account.UserId,
                selectedMessage.MessageId,
                CancellationToken.None);
            var message = TF("Deleted a message from {0}.", target.DisplayName);
            ChatboxModerationStatusText = message;
            AppendLocalChatActivity(BridgeChatActivityKind.MessageDeleted, message, target, selectedMessage.MessageId);
        }
        catch (Exception ex)
        {
            ReportChatModerationFailure(target, ex);
        }
    }

    private Task MarkSelectedChatUserSuspiciousAsync() =>
        SetSelectedChatUserSuspiciousStatusAsync("ACTIVE_MONITORING");

    private Task RestrictSelectedChatUserAsync() =>
        SetSelectedChatUserSuspiciousStatusAsync("RESTRICTED");

    private async Task ClearSelectedChatUserSuspiciousStatusAsync()
    {
        var target = GetSelectedChatModerationTarget();
        if (target is null)
        {
            return;
        }

        try
        {
            ChatboxModerationStatusText = TF("Clearing suspicious-user status for {0}...", target.DisplayName);
            var account = await GetBroadcasterModerationAccountAsync([TwitchScopes.ModerationSuspiciousUsers], CancellationToken.None);
            await twitchApiClient.ClearSuspiciousUserStatusAsync(
                account.AccessToken,
                runtimeConfig.TwitchClientId,
                account.UserId,
                account.UserId,
                target.UserId,
                CancellationToken.None);
            UpdateSuspiciousStatusForUser(target.UserId, target.Login, "NO_TREATMENT");
            var message = TF("Cleared suspicious-user status for {0}.", target.DisplayName);
            ChatboxModerationStatusText = message;
            AppendLocalChatActivity(BridgeChatActivityKind.SuspiciousUserUpdated, message, target, suspiciousStatus: "NO_TREATMENT");
        }
        catch (Exception ex)
        {
            ReportChatModerationFailure(target, ex);
        }
    }

    private async Task SetSelectedChatUserSuspiciousStatusAsync(string suspiciousStatus)
    {
        var target = GetSelectedChatModerationTarget();
        if (target is null)
        {
            return;
        }

        try
        {
            var statusLabel = FormatSuspiciousStatusLabel(suspiciousStatus);
            ChatboxModerationStatusText = TF("Updating {0} to {1}...", target.DisplayName, statusLabel);
            var account = await GetBroadcasterModerationAccountAsync([TwitchScopes.ModerationSuspiciousUsers], CancellationToken.None);
            await twitchApiClient.SetSuspiciousUserStatusAsync(
                account.AccessToken,
                runtimeConfig.TwitchClientId,
                account.UserId,
                account.UserId,
                target.UserId,
                suspiciousStatus,
                CancellationToken.None);
            UpdateSuspiciousStatusForUser(target.UserId, target.Login, suspiciousStatus);
            var message = string.Equals(suspiciousStatus, "RESTRICTED", StringComparison.Ordinal)
                ? TF("Restricted {0} through Twitch suspicious-user tools.", target.DisplayName)
                : TF("Marked {0} for suspicious-user monitoring.", target.DisplayName);
            ChatboxModerationStatusText = message;
            AppendLocalChatActivity(BridgeChatActivityKind.SuspiciousUserUpdated, message, target, suspiciousStatus: suspiciousStatus);
        }
        catch (Exception ex)
        {
            ReportChatModerationFailure(target, ex);
        }
    }

    private async Task<TwitchAccountSettings> GetBroadcasterModerationAccountAsync(
        IReadOnlyCollection<string> requiredScopes,
        CancellationToken cancellationToken)
    {
        await ReloadRuntimeConfigAsync();
        var snapshot = CreateBroadcasterRewardAccountSnapshot();
        if (!snapshot.IsConnected)
        {
            throw new InvalidOperationException(T("Connect the broadcaster account to use Twitch moderation."));
        }

        var account = await ValidateOrRefreshBroadcasterAccountAsync(snapshot, cancellationToken);
        var missingScopes = requiredScopes
            .Where(scope => !HasScope(account, scope))
            .ToArray();
        if (missingScopes.Length > 0)
        {
            throw new InvalidOperationException(TF("Reconnect broadcaster to enable: {0}.", string.Join(", ", missingScopes)));
        }

        return account;
    }

    private void AppendLocalChatActivity(
        BridgeChatActivityKind kind,
        string message,
        ChatModerationTarget target,
        string messageId = "",
        string suspiciousStatus = "")
    {
        AppendChatActivity(new BridgeChatActivity(kind, message, DateTimeOffset.Now)
        {
            TargetUserDisplayName = target.DisplayName,
            TargetUserLogin = target.Login,
            TargetUserId = target.UserId,
            MessageId = messageId,
            SuspiciousStatus = suspiciousStatus
        });
    }

    private void ReportChatModerationFailure(ChatModerationTarget target, Exception ex)
    {
        var message = TF("Moderation action for {0} failed: {1}", target.DisplayName, SensitiveTextSanitizer.Sanitize(ex.Message));
        ChatboxModerationStatusText = message;
        AppendLocalChatActivity(BridgeChatActivityKind.ModerationFailure, message, target);
    }

    private ChatModerationTarget? GetSelectedChatModerationTarget()
    {
        if (SelectedChatMessage is not { } entry
            || string.IsNullOrWhiteSpace(entry.UserId)
            || string.Equals(entry.UserId, Settings.Broadcaster.UserId, StringComparison.Ordinal))
        {
            return null;
        }

        return new ChatModerationTarget(entry.UserDisplayName, entry.UserLogin, entry.UserId);
    }

    private bool CanTimeoutSelectedChatUser() =>
        GetSelectedChatModerationTarget() is not null
        && HasScope(Settings.Broadcaster, TwitchScopes.ModerationBannedUsers);

    private bool CanDeleteSelectedChatMessage() =>
        GetSelectedChatModerationTarget() is not null
        && SelectedChatMessage is { } entry
        && !string.IsNullOrWhiteSpace(entry.MessageId)
        && HasScope(Settings.Broadcaster, TwitchScopes.ModerationChatMessages);

    private bool CanManageSelectedChatUserSuspiciousStatus() =>
        GetSelectedChatModerationTarget() is not null
        && HasScope(Settings.Broadcaster, TwitchScopes.ModerationSuspiciousUsers);

    private void RaiseSelectedChatModerationProperties()
    {
        RaisePropertyChanged(nameof(HasSelectedChatMessage));
        RaisePropertyChanged(nameof(HasSelectedChatModerationTarget));
        RaisePropertyChanged(nameof(SelectedChatModerationTitle));
        RaisePropertyChanged(nameof(SelectedChatModerationDetailText));
    }

    private void RefreshChatModerationCommandStates()
    {
        TimeoutSelectedChatUser10SecondsCommand.NotifyCanExecuteChanged();
        TimeoutSelectedChatUser1MinuteCommand.NotifyCanExecuteChanged();
        TimeoutSelectedChatUser5MinutesCommand.NotifyCanExecuteChanged();
        TimeoutSelectedChatUser10MinutesCommand.NotifyCanExecuteChanged();
        TimeoutSelectedChatUser30MinutesCommand.NotifyCanExecuteChanged();
        TimeoutSelectedChatUser1HourCommand.NotifyCanExecuteChanged();
        BanSelectedChatUserCommand.NotifyCanExecuteChanged();
        PurgeSelectedChatUserCommand.NotifyCanExecuteChanged();
        DeleteSelectedChatMessageCommand.NotifyCanExecuteChanged();
        MarkSelectedChatUserSuspiciousCommand.NotifyCanExecuteChanged();
        RestrictSelectedChatUserCommand.NotifyCanExecuteChanged();
        ClearSelectedChatUserSuspiciousStatusCommand.NotifyCanExecuteChanged();
        RaisePropertyChanged(nameof(ChatboxModerationScopeStatusText));
    }

    private static string FormatModerationDuration(int seconds)
    {
        return seconds switch
        {
            < 60 => string.Format(CultureInfo.CurrentCulture, LocalizationService.Translate("{0:N0}s"), seconds),
            < 3600 => string.Format(CultureInfo.CurrentCulture, LocalizationService.Translate("{0:N0}m"), seconds / 60),
            _ => string.Format(CultureInfo.CurrentCulture, LocalizationService.Translate("{0:N0}h"), seconds / 3600)
        };
    }

    private static string FormatSuspiciousStatusLabel(string suspiciousStatus) => suspiciousStatus switch
    {
        "ACTIVE_MONITORING" => LocalizationService.Translate("Suspicious"),
        "RESTRICTED" => LocalizationService.Translate("Restricted"),
        "NO_TREATMENT" => LocalizationService.Translate("Cleared"),
        _ => suspiciousStatus
    };

    private void RefreshCommandStates()
    {
        ConnectBroadcasterCommand.NotifyCanExecuteChanged();
        DisconnectBroadcasterCommand.NotifyCanExecuteChanged();
        OpenBroadcasterAuthPageCommand.NotifyCanExecuteChanged();
        ConnectBotCommand.NotifyCanExecuteChanged();
        DisconnectBotCommand.NotifyCanExecuteChanged();
        OpenBotAuthPageCommand.NotifyCanExecuteChanged();
        ConnectVrChatCommand.NotifyCanExecuteChanged();
        DisconnectVrChatCommand.NotifyCanExecuteChanged();
        RefreshVrChatAvatarsCommand.NotifyCanExecuteChanged();
        ClearVrChatCacheCommand.NotifyCanExecuteChanged();
        RemoveSelectedRuleCommand.NotifyCanExecuteChanged();
        TestSelectedRuleCommand.NotifyCanExecuteChanged();
        RemoveSelectedAvatarScaleSetCommand.NotifyCanExecuteChanged();
        RemoveSelectedAvatarScaleRuleCommand.NotifyCanExecuteChanged();
        TestSelectedAvatarScaleRuleCommand.NotifyCanExecuteChanged();
        OpenBroadcasterLoginCommand.NotifyCanExecuteChanged();
        OpenBotLoginCommand.NotifyCanExecuteChanged();
        DeleteSelectedAvatarProfileCommand.NotifyCanExecuteChanged();
        DeleteAllAvatarProfilesCommand.NotifyCanExecuteChanged();
        SetSelectedAvatarProfileAsMasterCommand.NotifyCanExecuteChanged();
        ToggleSelectedAvatarRewardTestOverrideCommand.NotifyCanExecuteChanged();
        UseCurrentVrChatAvatarForProfileCommand.NotifyCanExecuteChanged();
        RefreshChatModerationCommandStates();
        RefreshRuleCommandStates();
    }

    private void RefreshRuleCommandStates()
    {
        RaiseRuleSelectionStateProperties();
        RefreshAvailableActionTypes();
        AddRuleCommand.NotifyCanExecuteChanged();
        AddOutfitChoiceCommand.NotifyCanExecuteChanged();
        RemoveSelectedOutfitChoiceCommand.NotifyCanExecuteChanged();
        AddAvatarSupporterTriggerCommand.NotifyCanExecuteChanged();
        AddForceMovementOverrideCommand.NotifyCanExecuteChanged();
        AddAvatarProfileCommand.NotifyCanExecuteChanged();
        RemoveSelectedRuleCommand.NotifyCanExecuteChanged();
        OpenSpecialRuleLockoutPickerCommand.NotifyCanExecuteChanged();
        OpenAvatarRouletPoolPickerCommand.NotifyCanExecuteChanged();
        OpenActiveFloatBoostRewardCommand.NotifyCanExecuteChanged();
        EnableAllRulesCommand.NotifyCanExecuteChanged();
        DisableAllRulesCommand.NotifyCanExecuteChanged();
        DeleteAllRulesCommand.NotifyCanExecuteChanged();
        TestSelectedRuleCommand.NotifyCanExecuteChanged();
        AddAvatarScaleSetCommand.NotifyCanExecuteChanged();
        RemoveSelectedAvatarScaleSetCommand.NotifyCanExecuteChanged();
        AddAvatarScaleRuleCommand.NotifyCanExecuteChanged();
        AddRewardGrowthCommand.NotifyCanExecuteChanged();
        RemoveSelectedAvatarScaleRuleCommand.NotifyCanExecuteChanged();
        EnableAllAvatarScaleRulesCommand.NotifyCanExecuteChanged();
        DisableAllAvatarScaleRulesCommand.NotifyCanExecuteChanged();
        DeleteAllAvatarScaleRulesCommand.NotifyCanExecuteChanged();
        TestSelectedAvatarScaleRuleCommand.NotifyCanExecuteChanged();
        OpenAvatarScaleRuleLockoutPickerCommand.NotifyCanExecuteChanged();
        AddAvatarScalingCashPaymentRuleCommand.NotifyCanExecuteChanged();
        AddPowerUpRuleCommand.NotifyCanExecuteChanged();
        AddAvatarScalingPowerUpRuleCommand.NotifyCanExecuteChanged();
        RemoveSelectedPowerUpRuleCommand.NotifyCanExecuteChanged();
        EnableAllPowerUpRulesCommand.NotifyCanExecuteChanged();
        DisableAllPowerUpRulesCommand.NotifyCanExecuteChanged();
        DeleteAllPowerUpRulesCommand.NotifyCanExecuteChanged();
        TestSelectedPowerUpRuleCommand.NotifyCanExecuteChanged();
        UnlinkPowerUpCommand.NotifyCanExecuteChanged();
        UseCurrentAvatarForPowerUpRuleCommand.NotifyCanExecuteChanged();
        DeleteSelectedAvatarProfileCommand.NotifyCanExecuteChanged();
        DeleteAllAvatarProfilesCommand.NotifyCanExecuteChanged();
        SetSelectedAvatarProfileAsMasterCommand.NotifyCanExecuteChanged();
        ToggleSelectedAvatarRewardTestOverrideCommand.NotifyCanExecuteChanged();
        UseCurrentVrChatAvatarForProfileCommand.NotifyCanExecuteChanged();
    }

    private void UpgradeLegacyRewardTestOverrides()
    {
        if (Settings.ChannelPointRewardTestModeEnabled)
        {
            foreach (var profile in Settings.AvatarProfiles.Where(profile => profile.IsRewardTestOverrideEnabled))
            {
                profile.IsRewardTestOverrideEnabled = false;
            }

            return;
        }

        var hadLegacyOverride = Settings.AvatarProfiles.Any(profile => profile.IsRewardTestOverrideEnabled);
        if (!hadLegacyOverride)
        {
            return;
        }

        Settings.ChannelPointRewardTestModeEnabled = true;
        foreach (var profile in Settings.AvatarProfiles.Where(profile => profile.IsRewardTestOverrideEnabled))
        {
            profile.IsRewardTestOverrideEnabled = false;
        }
    }

    private void HandleBroadcasterLiveStateChanged(bool isLive, bool streamEnded)
    {
        var stateChanged = !hasResolvedBroadcasterLiveState || IsBroadcasterLive != isLive;
        hasResolvedBroadcasterLiveState = true;
        IsBroadcasterLive = isLive;
        RefreshStreamingStatusCard();
        QueueLiveFeedbackHeartbeatEvaluation();

        if (isLive && Settings.ChannelPointRewardTestModeEnabled)
        {
            Settings.ChannelPointRewardTestModeEnabled = false;
            AppendLog("Streaming test mode turned off automatically because the broadcaster is live.");
        }

        var rewardFireSaleResetQueued = streamEnded && ResetRewardFireSaleForStreamEnd();
        if (stateChanged)
        {
            if (!rewardFireSaleResetQueued)
            {
                QueueManagedRewardSync(0, ManagedRewardSyncReason.StreamStateChanged);
            }
        }
    }

    private void QueueLiveFeedbackHeartbeatEvaluation()
    {
        if (!isInitialized || isShuttingDown)
        {
            return;
        }

        liveFeedbackHeartbeatService.UpdateState(
            Settings.LiveFeedbackHeartbeatEnabled,
            HasRecoverableBroadcasterSession && !broadcasterReconnectRequired,
            IsBroadcasterLive,
            string.IsNullOrWhiteSpace(Settings.Broadcaster.DisplayName)
                ? Settings.Broadcaster.Login
                : Settings.Broadcaster.DisplayName,
            Settings.Broadcaster.Login,
            runtimeConfig.LiveFeedbackHeartbeatEndpoint,
            AppUpdateVersion,
            BuildChannel);
    }

    private Task StopLiveFeedbackHeartbeatAsync()
    {
        return liveFeedbackHeartbeatService.StopAsync();
    }

    private void RunOnUi(Action action)
    {
        if (dispatcher.CheckAccess())
        {
            action();
            return;
        }

        _ = dispatcher.InvokeAsync(action);
    }

    private bool RunOnUi(Func<bool> action)
    {
        if (dispatcher.CheckAccess())
        {
            return action();
        }

        return dispatcher.Invoke(action);
    }

    private static void OpenUri(string? uri)
    {
        if (string.IsNullOrWhiteSpace(uri))
        {
            return;
        }

        Process.Start(new ProcessStartInfo(uri)
        {
            UseShellExecute = true
        });
    }

    private void OpenRuntimeConfigFile()
    {
        OpenUri(RuntimeConfigPath);
    }

    private void OpenRuntimeConfigFolder()
    {
        var folder = Path.GetDirectoryName(RuntimeConfigPath);
        if (!string.IsNullOrWhiteSpace(folder))
        {
            OpenUri(folder);
        }
    }

    private void OpenSaveFolder()
    {
        OpenUri(AppDataFolderPath);
    }

    private async Task RepairSavedLoginStateAsync()
    {
        await StartSavedLoginRecoveryAsync(requireConfirmation: true);
    }

    private async Task StartSavedLoginRecoveryAsync(bool requireConfirmation)
    {
        if (isStartingSavedLoginRecovery)
        {
            return;
        }

        if (requireConfirmation)
        {
            var confirmed = ThemedDialogWindow.ShowYesNo(
                Application.Current?.MainWindow,
                SelectedTheme,
                T("Repair Saved Login State"),
                T("Crystal Relay will back up your redeem setup and custom theme assets, clear saved login/session files and stored tokens, restore the redeems, then restart. You will need to reconnect Twitch, VRChat, and payment providers."),
                T("Start Repair"),
                T("Cancel"));
            if (!confirmed)
            {
                return;
            }
        }

        isStartingSavedLoginRecovery = true;
        RepairSavedLoginStateCommand.NotifyCanExecuteChanged();

        try
        {
            await settingsStore.SaveAsync(Settings, CancellationToken.None);
            var preparation = await SavedLoginStateRecoveryService.PrepareRecoveryBackupAsync(CancellationToken.None);
            SavedLoginStateRecoveryService.StartRecoveryHelper(preparation);
            AppendLog(T("Starting saved login repair. Crystal Relay will restart after the backup is restored."));
            Application.Current?.MainWindow?.Close();
        }
        catch (Exception ex)
        {
            isStartingSavedLoginRecovery = false;
            RepairSavedLoginStateCommand.NotifyCanExecuteChanged();
            AppendLog(TF("Saved login repair could not start: {0}", ex.Message));
            ThemedDialogWindow.ShowOk(
                Application.Current?.MainWindow,
                SelectedTheme,
                T("Saved Login Repair"),
                TF("Saved login repair could not start.\n\n{0}", ex.Message));
        }
    }

    private void RecordSavedLoginRecoverySignal()
    {
        if (isStartingSavedLoginRecovery || savedLoginRecoveryPromptShownThisRun)
        {
            return;
        }

        savedLoginRecoveryFailureCount++;
        if (savedLoginRecoveryFailureCount < SavedLoginRecoveryPromptFailureThreshold)
        {
            return;
        }

        savedLoginRecoveryPromptShownThisRun = true;
        RunOnUi(() => _ = ShowSavedLoginRecoverySuggestionAsync());
    }

    private async Task ShowSavedLoginRecoverySuggestionAsync()
    {
        if (isStartingSavedLoginRecovery)
        {
            return;
        }

        var choice = ThemedDialogWindow.ShowThreeChoice(
            Application.Current?.MainWindow,
            SelectedTheme,
            T("Repair Saved Login State"),
            T("Crystal Relay noticed saved login/session failures more than once this run. The repair flow can back up your redeems, clear saved login state, restore your redeems, and restart Crystal Relay."),
            T("Repair and Restart"),
            T("Open Settings"),
            T("Not Now"));

        if (choice == ThemedDialogChoice.Primary)
        {
            await StartSavedLoginRecoveryAsync(requireConfirmation: false);
            return;
        }

        if (choice == ThemedDialogChoice.Secondary)
        {
            SetActiveSection(SectionView.Settings);
            SetActiveSettingsSection(SettingsSectionView.Safety);
        }
    }

    private void ReportSavedLoginRecoveryResult(SavedLoginRecoveryResult? result)
    {
        if (result is null)
        {
            return;
        }

        if (result.Succeeded)
        {
            AppendLog(T("Saved login repair restored your redeem setup and theme assets. Reconnect Twitch, VRChat, and payment providers before going live."));
            _ = dispatcher.BeginInvoke(() =>
                ThemedDialogWindow.ShowOk(
                    Application.Current?.MainWindow,
                    SelectedTheme,
                    T("Saved Login Repair"),
                    T("Saved login repair finished. Your redeem setup and custom theme assets were restored. Reconnect Twitch, VRChat, and payment providers before going live."),
                    T("OK"),
                    string.IsNullOrWhiteSpace(result.QuarantineFolderPath)
                        ? null
                        : TF("Safety backup: {0}", result.QuarantineFolderPath)),
                DispatcherPriority.ContextIdle);
            return;
        }

        var errorMessage = string.IsNullOrWhiteSpace(result.ErrorMessage)
            ? T("Unknown error.")
            : result.ErrorMessage;
        AppendLog(TF("Saved login repair could not finish: {0}", errorMessage));
        _ = dispatcher.BeginInvoke(() =>
            ThemedDialogWindow.ShowOk(
                Application.Current?.MainWindow,
                SelectedTheme,
                T("Saved Login Repair"),
                TF("Saved login repair could not finish. Your safety backup is still available at:\n{0}", result.BackupFolderPath),
                T("OK"),
                errorMessage),
            DispatcherPriority.ContextIdle);
    }

    private void OpenTwitchDeveloperConsole()
    {
        OpenUri(TwitchDeveloperConsoleUri);
    }

    private void OpenKoFiSupportPage()
    {
        var shouldOpenKoFi = ThemedDialogWindow.ShowYesNo(
            Application.Current?.MainWindow,
            SelectedTheme,
            T("Support Crystal Relay on Ko-fi"),
            T("Crystal Relay is completely free for everyone. If it helps your stream and you want to support development, you can leave a tip on Ko-fi. Every contribution helps keep the program free and growing."),
            T("Open Ko-fi"),
            T("Close"));

        if (shouldOpenKoFi)
        {
            OpenUri(KoFiSupportUri);
        }
    }

    private void OpenKoFiWebhooksPage()
    {
        OpenUri(KoFiWebhookSettingsUri);
    }

    private void OpenDiscordInvite()
    {
        var shouldOpenDiscord = ThemedDialogWindow.ShowYesNo(
            Application.Current?.MainWindow,
            SelectedTheme,
            T("Join the Crystal Relay Discord"),
            T("Get live update pings, sneak peeks, dev-related information, and meet other Crystal Relay users."),
            T("Open Discord"),
            T("Close"));

        if (shouldOpenDiscord)
        {
            OpenUri(DiscordInviteUri);
        }
    }

    private async Task OpenBugReportAsync(
        string? presetCategory = null,
        string? presetTitle = null)
    {
        var latestCrashPath = System.IO.Path.Combine(AppDataPaths.CrashLogFolder, "latest-crash.txt");
        var hasCrashLog = System.IO.File.Exists(latestCrashPath);

        var snapshot = BugReportSnapshotService.Build(new BugReportSnapshotData(
            IsBroadcasterConnected,
            IsBotConnected,
            IsVrChatConnected,
            OscStatusDetail,
            ResolveVrChatAvatarName(CurrentVrChatAvatarId),
            CurrentVrChatAvatarId,
            CurrentAvatarHeightMeters,
            SelectedTheme,
            GetThemeDisplayName(),
            GetAppVersionDisplay()));

        var activityLogSection = bugReportService.BuildActivityLogSection(LogEntries.ToArray());
        var debugLogSection = bugReportService.BuildDebugLogSection();
        var crashLogSection = hasCrashLog ? bugReportService.BuildCrashLogSection() : null;

        var dialog = new VrcTwitchOscBridge.BugReportWindow(
            SelectedTheme,
            hasCrashLog,
            presetCategory,
            presetTitle,
            snapshot,
            activityLogSection,
            debugLogSection,
            crashLogSection,
            GetAppVersionDisplay())
        {
            Owner = Application.Current?.MainWindow
        };

        if (dialog.ShowDialog() != true)
        {
            return;
        }

        string? activityLog = dialog.IncludeActivityLog ? activityLogSection : null;
        string? debugLog = dialog.IncludeDebugLog ? debugLogSection : null;
        string? crashLog = dialog.IncludeCrashLog ? crashLogSection : null;

        var submission = new BugReportSubmission(
            dialog.BugTitle,
            dialog.WhatHappened,
            dialog.ExpectedBehavior,
            dialog.StepsToReproduce,
            dialog.ContactName,
            GetAppVersionDisplay(),
            dialog.Category,
            dialog.Severity,
            snapshot,
            activityLog,
            debugLog,
            crashLog);

        AppendLog("Sending bug report to Crystal Relay's bug report service.");
        var result = await bugReportService.SubmitAsync(submission);
        if (result.Succeeded && !string.IsNullOrWhiteSpace(result.IssueUrl))
        {
            AppendLog($"Bug report submitted: {result.IssueUrl}");
            var shouldOpenIssue = ThemedDialogWindow.ShowYesNo(
                Application.Current?.MainWindow,
                SelectedTheme,
                T("Bug report sent"),
                $"{T("Crystal Relay created a GitHub issue for this report.")}{Environment.NewLine}{Environment.NewLine}{result.IssueUrl}",
                T("Open Issue"),
                T("Close"));
            if (shouldOpenIssue)
            {
                OpenUri(result.IssueUrl);
            }

            if (dialog.IncludeCrashLog)
            {
                MarkCrashReportSeen();
            }

            return;
        }

        var errorMessage = string.IsNullOrWhiteSpace(result.ErrorMessage)
            ? T("The bug report service did not accept the report.")
            : result.ErrorMessage;
        AppendLog($"Bug report could not be sent: {errorMessage}");
        var shouldOpenFallback = ThemedDialogWindow.ShowYesNo(
            Application.Current?.MainWindow,
            SelectedTheme,
            T("Bug report could not be sent"),
            $"{errorMessage}{Environment.NewLine}{Environment.NewLine}{T("Open the GitHub Issues page instead?")}",
            T("Open GitHub Issues"),
            T("Close"));
        if (shouldOpenFallback)
        {
            OpenUri(BugReportService.GitHubIssuesUrl);
        }
    }

    private string GetThemeDisplayName()
    {
        foreach (var option in ThemeOptions)
        {
            if (option.Value == SelectedTheme)
            {
                return option.Label;
            }
        }

        return Enum.GetName(SelectedTheme) ?? "Unknown";
    }

    internal async Task CheckForPendingCrashReportAsync()
    {
        var latestCrashPath = Path.Combine(AppDataPaths.CrashLogFolder, "latest-crash.txt");
        var seenMarkerPath = Path.Combine(AppDataPaths.CrashLogFolder, "crash-report-seen.marker");

        if (!File.Exists(latestCrashPath))
        {
            return;
        }

        DateTime crashTime;
        try
        {
            crashTime = File.GetLastWriteTimeUtc(latestCrashPath);
        }
        catch
        {
            return;
        }

        DateTime seenTime = DateTime.MinValue;
        if (File.Exists(seenMarkerPath))
        {
            try
            {
                seenTime = File.GetLastWriteTimeUtc(seenMarkerPath);
            }
            catch
            {
            }
        }

        if (crashTime <= seenTime)
        {
            return;
        }

        var shouldReport = ThemedDialogWindow.ShowYesNo(
            Application.Current?.MainWindow,
            SelectedTheme,
            T("Crystal Relay crashed last time"),
            T("Crystal Relay closed unexpectedly during your last session. Send a bug report with the crash log attached?"),
            T("Send crash report"),
            T("Not Now"));

        if (!shouldReport)
        {
            MarkCrashReportSeen(seenMarkerPath);
            return;
        }

        await OpenBugReportAsync(
            presetCategory: "crash",
            presetTitle: TF("Crash on {0}", crashTime.ToLocalTime().ToString("g")));
    }

    private void MarkCrashReportSeen(string? seenMarkerPath = null)
    {
        seenMarkerPath ??= Path.Combine(AppDataPaths.CrashLogFolder, "crash-report-seen.marker");
        try
        {
            File.WriteAllText(seenMarkerPath, DateTimeOffset.UtcNow.ToString("O"));
        }
        catch
        {
        }
    }

    private void OpenBroadcasterAuthPage()
    {
        OpenAuthPage(BridgeAccountRole.Broadcaster);
    }

    private void OpenBotAuthPage()
    {
        OpenAuthPage(BridgeAccountRole.Bot);
    }

    private void OpenAuthPage(BridgeAccountRole accountRole)
    {
        var verificationUri = accountRole == BridgeAccountRole.Broadcaster
            ? BroadcasterVerificationUri
            : BotVerificationUri;

        var targetUri = string.IsNullOrWhiteSpace(verificationUri) ? TwitchActivationUri : verificationUri;
        if (accountRole == BridgeAccountRole.Bot)
        {
            OpenUriInEdge(targetUri);
            return;
        }

        OpenUri(targetUri);
    }

    private async Task OpenOrAuthenticateBroadcasterAsync()
    {
        await OpenOrAuthenticateAsync(BridgeAccountRole.Broadcaster, TwitchScopes.Broadcaster);
    }

    private async Task OpenOrAuthenticateBotAsync()
    {
        await OpenOrAuthenticateAsync(BridgeAccountRole.Bot, TwitchScopes.Bot);
    }

    private async Task OpenOrAuthenticateAsync(BridgeAccountRole accountRole, IReadOnlyCollection<string> scopes)
    {
        var hasExistingCode = accountRole == BridgeAccountRole.Broadcaster
            ? !string.IsNullOrWhiteSpace(BroadcasterDeviceCode)
            : !string.IsNullOrWhiteSpace(BotDeviceCode);

        var isConnected = accountRole == BridgeAccountRole.Broadcaster
            ? HasRecoverableBroadcasterSession
            : Settings.Bot.IsConnected;

        var reconnectRequired = accountRole == BridgeAccountRole.Broadcaster
            ? broadcasterReconnectRequired
            : botReconnectRequired;

        if (hasExistingCode || (isConnected && !reconnectRequired))
        {
            OpenAuthPage(accountRole);
            return;
        }

        await AuthenticateAccountAsync(accountRole, scopes);
    }

    private static void OpenUriInEdge(string? uri)
    {
        if (string.IsNullOrWhiteSpace(uri))
        {
            return;
        }

        try
        {
            Process.Start(new ProcessStartInfo("msedge.exe", uri)
            {
                UseShellExecute = true
            });
        }
        catch
        {
            OpenUri(uri);
        }
    }

    private static string GetFriendlyAuthError(Exception ex)
    {
        if (ex is TwitchApiException apiException)
        {
            if (apiException.ApiMessage.Contains("invalid client", StringComparison.OrdinalIgnoreCase))
            {
                return "Twitch rejected Crystal Relay's built-in Twitch app ID. Make sure you're on the latest Crystal Relay release and try again.";
            }

            if (apiException.ApiMessage.Contains("invalid scope", StringComparison.OrdinalIgnoreCase))
            {
                return "Twitch rejected one of the requested scopes. Double-check that your Twitch app is registered correctly and try again.";
            }
        }

        return ex.Message;
    }

    private static bool HasScope(TwitchAccountSettings account, string scope)
    {
        return account.Scopes.Any(existingScope =>
            string.Equals(existingScope, scope, StringComparison.OrdinalIgnoreCase));
    }

    private static bool HasBridgeSensitiveTwitchAccountChanges(TwitchAccountSettings current, TwitchAccountSettings next)
    {
        return !string.Equals(current.AccessToken, next.AccessToken, StringComparison.Ordinal)
            || !string.Equals(current.UserId, next.UserId, StringComparison.Ordinal)
            || !string.Equals(current.Login, next.Login, StringComparison.OrdinalIgnoreCase)
            || !ScopeSetsEqual(current.Scopes, next.Scopes);
    }

    private static bool ScopeSetsEqual(IEnumerable<string> currentScopes, IEnumerable<string> nextScopes)
    {
        var currentSet = currentScopes
            .Where(scope => !string.IsNullOrWhiteSpace(scope))
            .ToHashSet(StringComparer.OrdinalIgnoreCase);
        var nextSet = nextScopes
            .Where(scope => !string.IsNullOrWhiteSpace(scope))
            .ToHashSet(StringComparer.OrdinalIgnoreCase);
        return currentSet.SetEquals(nextSet);
    }

    private string BuildExpiryStatus(TwitchAccountSettings account)
    {
        if (!account.IsConnected || account.AccessTokenExpiresAt is null)
        {
            return string.Empty;
        }

        var remaining = account.AccessTokenExpiresAt.Value - DateTimeOffset.UtcNow;
        var renewalDueAt = account.SessionRenewalDueAt;
        var renewalText = renewalDueAt is null
            ? string.Empty
            : BuildRenewalText(renewalDueAt.Value - DateTimeOffset.UtcNow);

        if (remaining <= TimeSpan.Zero)
        {
            return bridgeCoordinator.IsRunning
                ? string.IsNullOrWhiteSpace(renewalText)
                    ? T("Refreshing Twitch session...")
                    : TF("Refreshing Twitch session... {0}", renewalText)
                : string.IsNullOrWhiteSpace(renewalText)
                    ? T("Twitch session needs to refresh the next time Crystal Relay starts listening.")
                    : TF("Twitch session needs to refresh the next time Crystal Relay starts listening. {0}", renewalText);
        }

        var remainingText = DescribeRemainingTime(remaining);
        return bridgeCoordinator.IsRunning
            ? string.IsNullOrWhiteSpace(renewalText)
                ? TF("Current Twitch token expires in about {0}. Crystal Relay refreshes it automatically while running.", remainingText)
                : TF("Current Twitch token expires in about {0}. Crystal Relay refreshes it automatically while running. {1}", remainingText, renewalText)
            : string.IsNullOrWhiteSpace(renewalText)
                ? TF("Current Twitch token expires in about {0}.", remainingText)
                : TF("Current Twitch token expires in about {0}. {1}", remainingText, renewalText);
    }

    private static string DescribeRemainingTime(TimeSpan remaining)
    {
        if (remaining <= TimeSpan.Zero)
        {
            return T("less than 1 minute");
        }

        var rounded = TimeSpan.FromMinutes(Math.Ceiling(remaining.TotalMinutes));
        var days = Math.Max(0, rounded.Days);
        var hours = Math.Max(0, rounded.Hours);
        var minutes = Math.Max(0, rounded.Minutes);

        if (days > 0)
        {
            return TF(
                "{0} {1}, {2} {3}, {4} {5}",
                days,
                days == 1 ? T("day") : T("days"),
                hours,
                hours == 1 ? T("hour") : T("hours"),
                minutes,
                minutes == 1 ? T("minute") : T("minutes"));
        }

        if (rounded.TotalHours >= 1)
        {
            var wholeHours = Math.Max(1, (int)rounded.TotalHours);
            return TF(
                "{0} {1}, {2} {3}",
                wholeHours,
                wholeHours == 1 ? T("hour") : T("hours"),
                minutes,
                minutes == 1 ? T("minute") : T("minutes"));
        }

        var wholeMinutes = Math.Max(1, (int)rounded.TotalMinutes);
        return TF("{0} {1}", wholeMinutes, wholeMinutes == 1 ? T("minute") : T("minutes"));
    }

    private static string BuildRenewalText(TimeSpan remaining)
    {
        if (remaining <= TimeSpan.Zero)
        {
            return T("Re-login now to renew the 30-day Twitch session window.");
        }

        return TF("Manual re-login window in about {0}.", DescribeRemainingTime(remaining));
    }

    private void RaiseConnectionStateProperties()
    {
        RaisePropertyChanged(nameof(IsBroadcasterConnected));
        RaisePropertyChanged(nameof(IsBroadcasterDisconnected));
        RaisePropertyChanged(nameof(IsBotConnected));
        RaisePropertyChanged(nameof(IsBotDisconnected));
    }

    private void RaiseVrChatConnectionStateProperties()
    {
        RaisePropertyChanged(nameof(IsVrChatConnected));
        RaisePropertyChanged(nameof(IsVrChatDisconnected));
    }

    private void RecomputeVrChatConnectionState()
    {
        VrChatConnectionState newState;
        if (!string.IsNullOrWhiteSpace(Settings.VrChat.AuthCookie))
        {
            newState = VrChatConnectionState.LoggedIn;
        }
        else if (availableVrChatAvatars.Count > 0)
        {
            newState = VrChatConnectionState.Cached;
        }
        else
        {
            newState = VrChatConnectionState.NoData;
        }
        VrChatConnectionState = newState;
    }

    private string? ResolveCurrentUserIdForCache()
    {
        var resolved = VrChatLocalOscCacheService.ResolveCacheUserId(
            Settings.VrChat.UserId,
            inferredLocalLowUserId);
        if (!string.IsNullOrWhiteSpace(Settings.VrChat.UserId))
        {
            return resolved;
        }
        if (string.IsNullOrWhiteSpace(inferredLocalLowUserId))
        {
            inferredLocalLowUserId = resolved;
        }
        return resolved;
    }

    private void InvalidateInferredLocalLowUserId()
    {
        inferredLocalLowUserId = null;
    }

    private async Task HandleVrChatUnauthorizedAsync(CancellationToken ct)
    {
        var cachedAvatars = SelectVrChatAvatarsForCachedModeAfterAuthFailure(availableVrChatAvatars);
        ReplaceAvailableVrChatAvatars(cachedAvatars);

        var userId = ResolveCurrentUserIdForCache();
        if (!string.IsNullOrEmpty(userId))
        {
            try
            {
                await settingsStore.SaveVrChatAvatarCacheAsync(
                    userId,
                    cachedAvatars,
                    ct);
            }
            catch
            {
                // best-effort; do not break the cached-mode transition
            }
        }

        Settings.VrChat.Clear();
        AvatarPickerService.SetVrChatAuthCookie(null);
        StartOrRefreshVrChatLocalOscWatcher();
        await ScanLocalVrChatOscAvatarCacheAsync(ct);
        RecomputeVrChatConnectionState();
        QueueCurrentVrChatLocalStateRefresh(0);
    }

    internal static IReadOnlyList<VrChatAvatarSummary> SelectVrChatAvatarsForCachedModeAfterAuthFailure(
        IEnumerable<VrChatAvatarSummary> avatars)
    {
        return avatars.ToList();
    }

    private void HandleIncomingOscAvatarChangeSync(string avatarId)
    {
        _ = HandleIncomingOscAvatarChangeAsync(avatarId, CancellationToken.None);
    }

    private async Task HandleIncomingOscAvatarChangeAsync(string avatarId, CancellationToken ct)
    {
        if (!avatarId.StartsWith("avtr_", StringComparison.Ordinal)) return;

        string? resolvedName = null;
        if (availableVrChatAvatarNamesById.TryGetValue(avatarId, out var existingName) &&
            !string.IsNullOrWhiteSpace(existingName) &&
            !string.Equals(existingName, avatarId, StringComparison.Ordinal))
        {
            resolvedName = existingName;
        }

        if (resolvedName is null)
        {
            var inferredUserId = await Task.Run(
                () => VrChatLocalOscCacheService.TryInferUserIdFromLocalLowInRoot(
                    VrChatLocalClientStateService.GetVrChatRootPath()),
                ct);
            if (!string.IsNullOrEmpty(inferredUserId))
            {
                var known = await vrChatLocalOscCacheService
                    .LoadKnownAvatarsAsync(inferredUserId, ct);
                var match = known.FirstOrDefault(a =>
                    string.Equals(a.AvatarId, avatarId, StringComparison.Ordinal));
                if (match is not null &&
                    !string.IsNullOrWhiteSpace(match.AvatarName) &&
                    !string.Equals(match.AvatarName, avatarId, StringComparison.Ordinal))
                {
                    resolvedName = match.AvatarName;
                }
            }
        }

        var merged = OscAvatarChangeMerger.MergeIntoList(
            availableVrChatAvatars,
            avatarId,
            resolvedName ?? string.Empty,
            "Local OSC");
        ReplaceAvailableVrChatAvatars(merged);

        var currentUserId = ResolveCurrentUserIdForCache();
        if (!string.IsNullOrEmpty(currentUserId))
        {
            try
            {
                await settingsStore.SaveVrChatAvatarCacheAsync(
                    currentUserId,
                    availableVrChatAvatars,
                    ct);
            }
            catch
            {
                // best-effort; do not break the OSC flow
            }
        }

        // Synchronous: HandleVrChatAvatarChangedByBridge is void and mutates UI-bound
        // state (Settings.VrChat.CurrentAvatarId, in-memory list, managed reward sync)
        // that the following RecomputeVrChatConnectionState reads. Must run before
        // RecomputeVrChatConnectionState so the connection state reflects the change.
        HandleVrChatAvatarChangedByBridge(avatarId, queueManagedRewardSync: true);

        // When permanent swap mode is enabled, update the global return avatar to match
        // manual avatar changes so that future "Return to Previous" swaps can return
        // the viewer back to the streamer's manually-chosen avatar.
        if (Settings.PermanentSwapModeEnabled && !string.IsNullOrWhiteSpace(avatarId))
        {
            ApplySharedReturnAvatarSelection(avatarId, resolvedName ?? string.Empty, saveImmediately: true);
        }

        RecomputeVrChatConnectionState();
    }

    private void EnsureRuleCollectionsHaveStarterContent()
    {
        if (Settings.AvatarProfiles.Count > 0 || Settings.GlobalOverrideRules.Count > 0)
        {
            return;
        }

        Settings.AvatarProfiles.Add(CreateDefaultAvatarProfile());
    }

    private void EnsureMasterAvatarProfileExists()
    {
        NormalizeMasterAvatarProfiles();
        if (Settings.AvatarProfiles.Count == 0)
        {
            Settings.AvatarProfiles.Add(CreateDefaultAvatarProfile());
            return;
        }

        if (Settings.AvatarProfiles.Any(profile => profile.IsMasterProfile))
        {
            return;
        }

        Settings.AvatarProfiles[0].IsMasterProfile = true;
    }

    private void NormalizeMasterAvatarProfiles()
    {
        AvatarTriggerProfile? firstMasterProfile = null;
        foreach (var profile in Settings.AvatarProfiles)
        {
            if (!profile.IsMasterProfile)
            {
                continue;
            }

            if (firstMasterProfile is null)
            {
                firstMasterProfile = profile;
                continue;
            }

            profile.IsMasterProfile = false;
        }

        if (firstMasterProfile is null && Settings.AvatarProfiles.Count > 0)
        {
            Settings.AvatarProfiles[0].IsMasterProfile = true;
        }
    }

    private IEnumerable<TriggerRule> EnumerateAllRules()
    {
        return Settings.AvatarProfiles.SelectMany(profile => profile.ChannelPointRules)
            .Concat(Settings.AvatarSwapProfiles.SelectMany(p => p.ChannelPointRules))
            .Concat(Settings.AvatarSwapProfiles.SelectMany(p => p.BitsRules))
            .Concat(Settings.AvatarSwapProfiles.SelectMany(p => p.SubsRules))
            .Concat(Settings.AvatarSwapProfiles.SelectMany(p => p.PowerUpRules))
            .Concat(Settings.AvatarRouletteProfiles.SelectMany(r => r.Triggers))
            .Concat(GetAllMovementRules())
            .Concat(Settings.GlobalOverrideRules);
    }

    private ObservableCollection<TriggerRule> GetCurrentEditableRuleCollection()
    {
        if (IsViewingAvatarScaling)
        {
            return new ObservableCollection<TriggerRule>();
        }

        if (IsViewingMovementRedeems)
        {
            return SelectedMovementRedeemSet?.MovementRules ?? new ObservableCollection<TriggerRule>();
        }

        if (IsViewingMasterAvatar)
        {
            return MasterAvatarProfile?.ChannelPointRules ?? new ObservableCollection<TriggerRule>();
        }

        return SelectedAvatarProfile?.ChannelPointRules ?? new ObservableCollection<TriggerRule>();
    }

    private IReadOnlyList<TriggerRuleReferenceOption> BuildAvailableSpecialRuleLockoutOptions()
    {
        if (!IsViewingAvatarTriggers || SelectedAvatarProfile is null || SelectedRule is null)
        {
            return [];
        }

        var selectedRuleId = SelectedRule.Id;
        var existingLockouts = SelectedRule.TemporarilyDisabledRuleIds.ToHashSet();

        return SelectedAvatarProfile.ChannelPointRules
            .Where(rule => rule.Id != selectedRuleId && !existingLockouts.Contains(rule.Id))
            .OrderBy(rule => GetSpecialRuleLockoutDisplayLabel(rule), StringComparer.OrdinalIgnoreCase)
            .Select(rule => new TriggerRuleReferenceOption(rule.Id, GetSpecialRuleLockoutDisplayLabel(rule)))
            .ToArray();
    }

    private IReadOnlyList<TriggerRuleReferenceOption> BuildConfiguredSpecialRuleLockoutOptions()
    {
        if (!IsViewingAvatarTriggers || SelectedAvatarProfile is null || SelectedRule is null)
        {
            return [];
        }

        var profileRulesById = SelectedAvatarProfile.ChannelPointRules.ToDictionary(rule => rule.Id);
        var configuredOptions = new List<TriggerRuleReferenceOption>();

        foreach (var blockedRuleId in SelectedRule.TemporarilyDisabledRuleIds)
        {
            if (blockedRuleId == SelectedRule.Id)
            {
                continue;
            }

            if (profileRulesById.TryGetValue(blockedRuleId, out var blockedRule))
            {
                configuredOptions.Add(new TriggerRuleReferenceOption(blockedRule.Id, GetSpecialRuleLockoutDisplayLabel(blockedRule)));
            }
        }

        return configuredOptions;
    }

    private void RefreshSpecialRuleLockoutOptions()
    {
        RaisePropertyChanged(nameof(SpecialRuleLockoutSummaryText));
    }

    private bool CanOpenSpecialRuleLockoutPicker()
    {
        return IsViewingAvatarTriggers
            && SelectedAvatarProfile is not null
            && SelectedRule is not null;
    }

    private void OpenSpecialRuleLockoutPicker()
    {
        if (!CanOpenSpecialRuleLockoutPicker() || SelectedAvatarProfile is null || SelectedRule is null)
        {
            return;
        }

        var dialog = new RuleLockoutPickerWindow(
            SelectedTheme,
            SelectedAvatarProfile.DisplayTitle,
            SelectedRule.RewardDisplayTitle,
            BuildAvailableSpecialRuleLockoutOptions(),
            BuildConfiguredSpecialRuleLockoutOptions(),
            allowPairingModeSelection: true,
            selectedPairingMode: SelectedRule.SpecialRulePairingMode)
        {
            Owner = Application.Current?.MainWindow
        };

        if (dialog.ShowDialog() != true)
        {
            return;
        }

        var updatedRuleIds = dialog.SelectedRuleIds
            .Where(ruleId => ruleId != Guid.Empty && ruleId != SelectedRule.Id)
            .Distinct()
            .ToArray();
        var currentRuleIds = SelectedRule.TemporarilyDisabledRuleIds
            .Where(ruleId => ruleId != Guid.Empty && ruleId != SelectedRule.Id)
            .Distinct()
            .ToArray();

        var updatedPairingMode = dialog.SelectedPairingMode;
        var currentPairingMode = SelectedRule.SpecialRulePairingMode;

        if (updatedRuleIds.SequenceEqual(currentRuleIds)
            && updatedPairingMode == currentPairingMode)
        {
            return;
        }

        SelectedRule.SpecialRulePairingMode = updatedPairingMode;
        SelectedRule.TemporarilyDisabledRuleIds = new ObservableCollection<Guid>(updatedRuleIds);
        RefreshSpecialRuleLockoutOptions();
        QueueSave();
        QueueBridgeRefresh();
        QueueManagedRewardSync(0);
        AppendLog(updatedRuleIds.Length == 0
            ? $"Cleared disable pairings for '{SelectedRule.DisplayTitle}'."
            : updatedPairingMode == SpecialRulePairingMode.ShowPairedWhileActive
                ? $"Updated reveal pairings for '{SelectedRule.DisplayTitle}'."
                : $"Updated disable pairings for '{SelectedRule.DisplayTitle}'.");
    }

    private IReadOnlyList<TriggerRuleReferenceOption> BuildAvailableAvatarScaleRuleLockoutOptions()
    {
        if (SelectedAvatarScaleRule is not { } selectedRule || GetSelectedAvatarScaleRuleLockoutSet() is not { } selectedSet)
        {
            return [];
        }

        var selectedRuleId = selectedRule.Id;
        var existingLockouts = selectedRule.TemporarilyDisabledScaleRuleIds.ToHashSet();

        return selectedSet.ScaleRules
            .Where(rule => rule.Id != selectedRuleId && !existingLockouts.Contains(rule.Id))
            .OrderBy(rule => GetAvatarScaleRuleLockoutDisplayLabel(rule), StringComparer.OrdinalIgnoreCase)
            .Select(rule => new TriggerRuleReferenceOption(rule.Id, GetAvatarScaleRuleLockoutDisplayLabel(rule)))
            .ToArray();
    }

    private IReadOnlyList<TriggerRuleReferenceOption> BuildConfiguredAvatarScaleRuleLockoutOptions()
    {
        if (SelectedAvatarScaleRule is not { } selectedRule || GetSelectedAvatarScaleRuleLockoutSet() is not { } selectedSet)
        {
            return [];
        }

        var scaleRulesById = selectedSet.ScaleRules.ToDictionary(rule => rule.Id);
        var configuredOptions = new List<TriggerRuleReferenceOption>();

        foreach (var blockedRuleId in selectedRule.TemporarilyDisabledScaleRuleIds)
        {
            if (blockedRuleId == selectedRule.Id)
            {
                continue;
            }

            if (scaleRulesById.TryGetValue(blockedRuleId, out var blockedRule))
            {
                configuredOptions.Add(new TriggerRuleReferenceOption(blockedRule.Id, GetAvatarScaleRuleLockoutDisplayLabel(blockedRule)));
            }
        }

        return configuredOptions;
    }

    private bool CanOpenAvatarScaleRuleLockoutPicker()
    {
        return SelectedAvatarScaleRule is not null
            && (BuildAvailableAvatarScaleRuleLockoutOptions().Count > 0
                || BuildConfiguredAvatarScaleRuleLockoutOptions().Count > 0);
    }

    private AvatarScaleSet? GetSelectedAvatarScaleRuleLockoutSet()
    {
        if (SelectedAvatarScaleRule is not { } selectedRule)
        {
            return null;
        }

        return SelectedAvatarScaleSet?.ScaleRules.Contains(selectedRule) == true
            ? SelectedAvatarScaleSet
            : GetOwningAvatarScaleSet(selectedRule);
    }

    private void OpenAvatarScaleRuleLockoutPicker()
    {
        if (!CanOpenAvatarScaleRuleLockoutPicker()
            || SelectedAvatarScaleRule is not { } selectedRule
            || GetSelectedAvatarScaleRuleLockoutSet() is not { } selectedSet)
        {
            return;
        }

        var dialog = new RuleLockoutPickerWindow(
            SelectedTheme,
            $"Scale Set: {selectedSet.DisplayTitle}",
            selectedRule.DisplayTitle,
            BuildAvailableAvatarScaleRuleLockoutOptions(),
            BuildConfiguredAvatarScaleRuleLockoutOptions())
        {
            Owner = Application.Current?.MainWindow
        };

        if (dialog.ShowDialog() != true)
        {
            return;
        }

        var updatedRuleIds = dialog.SelectedRuleIds
            .Where(ruleId => ruleId != Guid.Empty && ruleId != selectedRule.Id)
            .Distinct()
            .ToArray();
        var currentRuleIds = selectedRule.TemporarilyDisabledScaleRuleIds
            .Where(ruleId => ruleId != Guid.Empty && ruleId != selectedRule.Id)
            .Distinct()
            .ToArray();

        if (updatedRuleIds.SequenceEqual(currentRuleIds))
        {
            return;
        }

        selectedRule.TemporarilyDisabledScaleRuleIds = new ObservableCollection<Guid>(updatedRuleIds);
        RaisePropertyChanged(nameof(AvatarScaleRuleLockoutSummaryText));
        QueueSave();
        QueueBridgeRefresh();
        QueueManagedRewardSync(0);
        AppendLog(updatedRuleIds.Length == 0
            ? $"Cleared scale disable pairings for '{selectedRule.DisplayTitle}'."
            : $"Updated scale disable pairings for '{selectedRule.DisplayTitle}'.");
    }

    private bool CanOpenAvatarRouletPoolPicker()
    {
        return IsViewingMasterAvatar
            && SelectedRule?.ActionType == OscActionType.AvatarRoulet
            && Settings.VrChat.IsConnected;
    }

    private void OpenAvatarRouletPoolPicker()
    {
        if (!CanOpenAvatarRouletPoolPicker() || SelectedRule is null)
        {
            return;
        }

        var configuredIds = SelectedRule.AvatarRouletAvatarIds
            .Where(id => !string.IsNullOrWhiteSpace(id))
            .ToList();

        var avatars = availableVrChatAvatars
            .Select(a => a with { })
            .ToList();

        var result = AvatarPickerService.OpenMulti(
            ThemeManager.CurrentTheme,
            avatars,
            Settings.AvatarLibrary,
            configuredIds,
            owner: Application.Current.MainWindow);

        if (result is null)
        {
            return;
        }

        var updatedAvatarIds = result
            .Where(id => !string.IsNullOrWhiteSpace(id))
            .Distinct(StringComparer.Ordinal)
            .ToList();
        var updatedAvatarNames = updatedAvatarIds
            .Select(id => ResolveVrChatAvatarName(id))
            .ToList();

        var currentAvatarIds = SelectedRule.AvatarRouletAvatarIds
            .Where(id => !string.IsNullOrWhiteSpace(id))
            .Select(id => id.Trim())
            .Distinct(StringComparer.Ordinal)
            .ToList();
        var currentAvatarNames = SelectedRule.AvatarRouletAvatarNames
            .Select(n => n?.Trim() ?? string.Empty)
            .ToList();

        if (updatedAvatarIds.SequenceEqual(currentAvatarIds, StringComparer.Ordinal)
            && updatedAvatarNames.SequenceEqual(currentAvatarNames, StringComparer.Ordinal))
        {
            return;
        }

        SelectedRule.AvatarRouletAvatarIds = new ObservableCollection<string>(updatedAvatarIds);
        SelectedRule.AvatarRouletAvatarNames = new ObservableCollection<string>(updatedAvatarNames);
        SyncVrChatAvatarRouletPoolLabels(SelectedRule);
        QueueSave();
        QueueBridgeRefresh();
        QueueManagedRewardSync(0);
        AppendLog(updatedAvatarIds.Count == 0
            ? $"Cleared the Avatar Roulette pool for '{SelectedRule.DisplayTitle}'."
            : $"Updated the Avatar Roulette pool for '{SelectedRule.DisplayTitle}'.");
    }

    private bool CanOpenActiveFloatBoostReward(object? parameter = null)
    {
        var rule = parameter as TriggerRule ?? SelectedRule;
        return rule is not null
            && rule.ActionType == OscActionType.AvatarParameter
            && rule.ParameterType == OscParameterType.Float
            && rule.DurationSeconds > 0;
    }

    private void OpenActiveFloatBoostReward(object? parameter)
    {
        var rule = parameter as TriggerRule ?? SelectedRule;
        if (rule is null || !CanOpenActiveFloatBoostReward(rule))
        {
            return;
        }

        if (string.IsNullOrWhiteSpace(rule.ActiveFloatBoostRewardTitle))
        {
            var parentTitle = string.IsNullOrWhiteSpace(rule.ChannelPointRewardTitle)
                ? rule.DisplayTitle
                : rule.ChannelPointRewardTitle;
            rule.ActiveFloatBoostRewardTitle = string.IsNullOrWhiteSpace(parentTitle)
                ? T("Boost Float")
                : TF("Keep {0}", parentTitle);
        }

        var dialog = new ActiveFloatBoostRewardWindow(SelectedTheme, rule)
        {
            Owner = Application.Current?.MainWindow
        };
        dialog.ShowDialog();
    }

    private void OpenSupporterOverrideTimeSettings(object? parameter)
    {
        var rule = parameter as TriggerRule ?? SelectedRule;
        if (rule is null || !rule.UsesSupporterAmountTimerSettings)
        {
            return;
        }

        var dialog = new SupporterOverrideTimeSettingsWindow(SelectedTheme, rule)
        {
            Owner = Application.Current?.MainWindow
        };
        dialog.ShowDialog();
    }

    private void RemoveSpecialRuleLockoutReferencesToRule(Guid ruleId)
    {
        foreach (var rule in Settings.AvatarProfiles
                     .SelectMany(profile => profile.ChannelPointRules)
                     .Where(rule => rule.TemporarilyDisabledRuleIds.Contains(ruleId)))
        {
            rule.TemporarilyDisabledRuleIds.Remove(ruleId);
        }
    }

    private void RemoveAvatarScaleRuleLockoutReferencesToRule(Guid ruleId)
    {
        foreach (var rule in GetAllAvatarScaleRules()
                     .Where(rule => rule.TemporarilyDisabledScaleRuleIds.Contains(ruleId)))
        {
            rule.TemporarilyDisabledScaleRuleIds.Remove(ruleId);
        }
    }

    private static string GetSpecialRuleLockoutDisplayLabel(TriggerRule rule)
    {
        return rule.RewardDisplayTitle;
    }

    private static string GetAvatarScaleRuleLockoutDisplayLabel(AvatarScaleRule rule)
    {
        return rule.DisplayTitle;
    }

    private IReadOnlyList<VrChatAvatarOption> BuildConfiguredAvatarRouletPoolOptions(TriggerRule rule)
    {
        var allOptionsById = BuildAllSelectableVrChatAvatarOptions()
            .ToDictionary(option => option.Id, StringComparer.Ordinal);
        var configuredOptions = new List<VrChatAvatarOption>();
        var configuredAvatarNames = rule.AvatarRouletAvatarNames
            .Select(avatarName => avatarName?.Trim() ?? string.Empty)
            .ToArray();

        foreach (var (avatarId, index) in rule.AvatarRouletAvatarIds
                     .Where(avatarId => !string.IsNullOrWhiteSpace(avatarId))
                     .Select((avatarId, index) => (avatarId: avatarId.Trim(), index))
                     .DistinctBy(pair => pair.avatarId, StringComparer.Ordinal))
        {
            if (allOptionsById.TryGetValue(avatarId, out var existingOption))
            {
                configuredOptions.Add(existingOption);
                continue;
            }

            var configuredName = index < configuredAvatarNames.Length
                ? configuredAvatarNames[index]
                : string.Empty;
            var resolvedName = ResolveVrChatAvatarName(avatarId);
            var displayName = GetSafeVrChatAvatarDisplayName(
                string.IsNullOrWhiteSpace(resolvedName) ? configuredName : resolvedName,
                "Selected avatar");
            configuredOptions.Add(new VrChatAvatarOption(
                avatarId,
                displayName,
                displayName,
                displayName,
                GetAvatarDuplicateHint(avatarId),
                true));
        }

        return configuredOptions;
    }

    private bool RuleBelongsToAvatarProfile(TriggerRule rule)
    {
        return GetOwningAvatarProfile(rule) is not null;
    }

    private void RemoveUnsupportedMovementRules()
    {
        var unsupportedRules = GetAllMovementRules()
            .Where(rule => !IsSupportedMovementDirection(rule.MovementDirection))
            .ToArray();
        if (unsupportedRules.Length == 0)
        {
            return;
        }

        foreach (var rule in unsupportedRules)
        {
            ForgetRememberedRules([rule]);
            GetOwningMovementRedeemSet(rule)?.MovementRules.Remove(rule);
        }

        AppendLog($"Removed {unsupportedRules.Length} old stop-input movement redeem{(unsupportedRules.Length == 1 ? string.Empty : "s")} because Crystal Relay now supports directional movement redeems only.");
        QueueSave(0);
    }

    private bool NormalizeChatCommandFallbackRules()
    {
        var changesMade = false;
        var claimedCommands = new Dictionary<string, TriggerRule>(StringComparer.OrdinalIgnoreCase);

        isNormalizingChatCommandRules = true;
        try
        {
            foreach (var rule in EnumerateAllRules())
            {
                var normalizedCommand = ChatCommandUtility.Normalize(rule.ChatCommandText);
                if (!string.Equals(rule.ChatCommandText, normalizedCommand, StringComparison.Ordinal))
                {
                    rule.ChatCommandText = normalizedCommand;
                    changesMade = true;
                }

                if (rule.ChatCommandEnabled && !ChatCommandUtility.IsConfigured(rule.ChatCommandText))
                {
                    rule.ChatCommandEnabled = false;
                    changesMade = true;
                }

                if (!ChatCommandUtility.IsConfigured(rule.ChatCommandText))
                {
                    continue;
                }

                if (claimedCommands.TryGetValue(rule.ChatCommandText, out var existingRule))
                {
                    var duplicateCommand = rule.ChatCommandText;
                    rule.ChatCommandEnabled = false;
                    rule.ChatCommandText = string.Empty;
                    changesMade = true;
                    AppendLog($"Removed duplicate chat command fallback '{duplicateCommand}' from '{rule.DisplayTitle}' because it is already used by '{existingRule.DisplayTitle}'.");
                    continue;
                }

                claimedCommands[rule.ChatCommandText] = rule;
            }
        }
        finally
        {
            isNormalizingChatCommandRules = false;
        }

        if (changesMade && isInitialized)
        {
            QueueSave(0);
        }

        return changesMade;
    }

    private void NormalizeChatCommandFallbackRule(TriggerRule rule)
    {
        if (isNormalizingChatCommandRules)
        {
            return;
        }

        isNormalizingChatCommandRules = true;
        try
        {
            var normalizedCommand = ChatCommandUtility.Normalize(rule.ChatCommandText);
            if (!string.Equals(rule.ChatCommandText, normalizedCommand, StringComparison.Ordinal))
            {
                rule.ChatCommandText = normalizedCommand;
            }

            if (!ChatCommandUtility.IsConfigured(rule.ChatCommandText))
            {
                return;
            }

            var duplicateRule = EnumerateAllRules().FirstOrDefault(existingRule =>
                existingRule.Id != rule.Id
                && ChatCommandUtility.IsConfigured(existingRule.ChatCommandText)
                && string.Equals(existingRule.ChatCommandText, rule.ChatCommandText, StringComparison.OrdinalIgnoreCase));
            if (duplicateRule is null)
            {
                return;
            }

            var duplicateCommand = rule.ChatCommandText;
            rule.ChatCommandEnabled = false;
            rule.ChatCommandText = string.Empty;
            AppendLog($"Chat command fallback '{duplicateCommand}' is already used by '{duplicateRule.DisplayTitle}'. Pick a different command for '{rule.DisplayTitle}'.");
        }
        finally
        {
            isNormalizingChatCommandRules = false;
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

    private static bool IsSupportedMovementRule(TriggerRule rule) =>
        rule.ActionType != OscActionType.PlayerMovement || IsSupportedMovementDirection(rule.MovementDirection);

    private static bool IsSupporterAvatarChangeOverride(TriggerRule rule) =>
        rule.ActionType is OscActionType.AvatarChange or OscActionType.AvatarRoulet;

    private static bool IsSupporterForceMovementOverride(TriggerRule rule) =>
        rule.TriggerType == TwitchTriggerType.Bits
        && rule.ActionType == OscActionType.PlayerMovement;

    private void RaiseRuleSelectionStateProperties()
    {
        RaisePropertyChanged(nameof(IsViewingAvatarTriggers));
        RaisePropertyChanged(nameof(IsViewingMasterAvatar));
        RaisePropertyChanged(nameof(IsViewingMovementRedeems));
        RaisePropertyChanged(nameof(IsViewingPowerUps));
        RaisePropertyChanged(nameof(IsViewingAvatarScaling));
        RaisePropertyChanged(nameof(MovementRedeemSets));
        RaisePropertyChanged(nameof(AvatarScaleSets));
        RaisePropertyChanged(nameof(AvatarScaleRules));
        RaisePropertyChanged(nameof(PowerUpRules));
        RaisePropertyChanged(nameof(MasterAvatarDisplayName));
        RaisePropertyChanged(nameof(MasterAvatarRules));
        RaisePropertyChanged(nameof(SelectedRuleCollectionTitle));
        RaisePropertyChanged(nameof(SelectedRuleCollectionHelpText));
        RaisePropertyChanged(nameof(RuleLibraryHelpText));
        RaisePropertyChanged(nameof(AddRuleButtonText));
        RaisePropertyChanged(nameof(DeleteRuleButtonText));
        RaisePropertyChanged(nameof(DeleteAllRulesButtonText));
        RaisePropertyChanged(nameof(SelectedAvatarProfileStatusText));
        RaisePropertyChanged(nameof(SelectedAvatarSetupTitle));
        RaisePropertyChanged(nameof(SelectedAvatarNameFieldLabel));
        RaisePropertyChanged(nameof(SelectedAvatarPickerLabel));
        RaisePropertyChanged(nameof(UseCurrentAvatarButtonText));
        RaisePropertyChanged(nameof(MasterAvatarReturnText));
        RaisePropertyChanged(nameof(ManagedChannelPointRewardHelpText));
        RaisePropertyChanged(nameof(UniversalManagedChannelPointRewardHelpText));
        RaisePropertyChanged(nameof(AvatarScaleRuntimeStatusText));
        RaisePropertyChanged(nameof(PowerUpRuleStatusText));
        RaisePropertyChanged(nameof(PowerUpActionEditorHelpText));
        RaisePropertyChanged(nameof(IsSetTriggerMasterRewardEditorVisible));
        RaisePropertyChanged(nameof(SelectedSetTriggerUsesSharedNumberedReward));
        RaisePropertyChanged(nameof(SelectedActionTypeOption));
        RaiseAvatarRedeemGroupProperties();
        RaiseSupporterRuleGroupProperties();
        RaiseUniversalTriggerGroupProperties();
        RaisePropertyChanged(nameof(IsSpecialRuleLockoutEditorVisible));
        RaisePropertyChanged(nameof(SpecialRuleLockoutHelpText));
        RaisePropertyChanged(nameof(SpecialRuleLockoutSummaryText));
        RaisePropertyChanged(nameof(AvatarScaleRuleLockoutHelpText));
        RaisePropertyChanged(nameof(AvatarScaleRuleLockoutSummaryText));
        RaisePropertyChanged(nameof(RewardTestOverrideButtonText));
        RaisePropertyChanged(nameof(RewardTestOverrideHelpText));
        RaisePropertyChanged(nameof(IsRewardTestOverrideAvailable));
        RaisePropertyChanged(nameof(IsStreamingTestButtonVisible));
        RaisePropertyChanged(nameof(EmergencyRedeemStopButtonText));
        RaisePropertyChanged(nameof(EmergencyRedeemStopHelpText));
        RaisePropertyChanged(nameof(IsEmergencyRedeemStopEnabled));
        RaisePropertyChanged(nameof(DesktopModeInputLockButtonText));
        RaisePropertyChanged(nameof(DesktopModeInputLockHelpText));
        RaisePropertyChanged(nameof(DesktopModeInputLockStatusText));
        RaisePropertyChanged(nameof(IsDesktopModeInputLockEnabled));
        RaisePropertyChanged(nameof(AvailableOverrideTriggerTypesForSelectedRule));
    }

    private void RefreshAvailableActionTypes()
    {
        RaisePropertyChanged(nameof(AvailableActionTypesForSelectedContext));
        RaisePropertyChanged(nameof(AvailableOverrideTriggerTypesForSelectedRule));
        RaisePropertyChanged(nameof(SelectedActionTypeOption));
    }

    private void AddSetTriggerAction()
    {
        if (SelectedRule is null || SelectedRule.ActionType != OscActionType.SetTrigger)
        {
            return;
        }

        var parameterType = SelectedSetTriggerAction?.ParameterType
            ?? (SelectedRule.ParameterType is OscParameterType.Bool or OscParameterType.Int or OscParameterType.Float
            ? SelectedRule.ParameterType
            : OscParameterType.Int);
        var action = new SetTriggerAction
        {
            ParameterName = SelectedSetTriggerParameterOption?.Address
                ?? SelectedSetTriggerAction?.ParameterName
                ?? (string.IsNullOrWhiteSpace(SelectedRule.ParameterName) ? "VRCEmote" : SelectedRule.ParameterName.Trim()),
            ParameterType = parameterType,
            ParameterValue = parameterType switch
            {
                OscParameterType.Bool => "True",
                OscParameterType.Float => "0.0",
                _ => "1"
            }
        };

        SelectedRule.SetTriggerActions.Add(action);
        SelectedSetTriggerAction = action;
        QueueSave();
        QueueBridgeRefresh();
        QueueManagedRewardSync(0);
    }

    private void CopySelectedAvatarParameterPath()
    {
        var path = GetSelectedAvatarParameterPathForCopy();
        if (string.IsNullOrWhiteSpace(path))
        {
            return;
        }

        copiedAvatarParameterPath = path.Trim();
        RefreshAvatarParameterPathCommandStates();
        AppendLog(TF("Copied avatar parameter path: {0}", copiedAvatarParameterPath));
    }

    private bool CanCopySelectedAvatarParameterPath()
    {
        return !string.IsNullOrWhiteSpace(GetSelectedAvatarParameterPathForCopy());
    }

    private void PasteSelectedAvatarParameterPath()
    {
        if (string.IsNullOrWhiteSpace(copiedAvatarParameterPath))
        {
            return;
        }

        var path = copiedAvatarParameterPath.Trim();
        if (SelectedRule?.ActionType == OscActionType.AvatarParameter)
        {
            if (string.Equals(SelectedRule.ParameterName?.Trim(), path, StringComparison.Ordinal))
            {
                return;
            }

            SelectedRule.ParameterName = path;
            RefreshAvatarParameterOptions();
            AppendLog(TF("Pasted avatar parameter path: {0}", path));
            return;
        }

        if (SelectedRule?.ActionType == OscActionType.SetTrigger && SelectedSetTriggerAction is not null)
        {
            if (string.Equals(SelectedSetTriggerAction.ParameterName?.Trim(), path, StringComparison.Ordinal))
            {
                return;
            }

            SelectedSetTriggerAction.ParameterName = path;
            RefreshSetTriggerParameterOptions();
            AppendLog(TF("Pasted avatar parameter path: {0}", path));
        }
    }

    private bool CanPasteSelectedAvatarParameterPath()
    {
        if (string.IsNullOrWhiteSpace(copiedAvatarParameterPath))
        {
            return false;
        }

        return SelectedRule?.ActionType == OscActionType.AvatarParameter
            || (SelectedRule?.ActionType == OscActionType.SetTrigger && SelectedSetTriggerAction is not null);
    }

    private string GetSelectedAvatarParameterPathForCopy()
    {
        if (SelectedRule?.ActionType == OscActionType.AvatarParameter)
        {
            return SelectedAvatarParameterOption?.Address
                ?? SelectedRule.ParameterName?.Trim()
                ?? string.Empty;
        }

        if (SelectedRule?.ActionType == OscActionType.SetTrigger && SelectedSetTriggerAction is not null)
        {
            return SelectedSetTriggerParameterOption?.Address
                ?? SelectedSetTriggerAction.ParameterName?.Trim()
                ?? string.Empty;
        }

        return string.Empty;
    }

    private void RefreshAvatarParameterPathCommandStates()
    {
        CopySelectedAvatarParameterPathCommand.NotifyCanExecuteChanged();
        PasteSelectedAvatarParameterPathCommand.NotifyCanExecuteChanged();
    }

    private void RemoveSelectedSetTriggerAction()
    {
        if (SelectedRule is null
            || SelectedRule.ActionType != OscActionType.SetTrigger
            || SelectedSetTriggerAction is null)
        {
            return;
        }

        var removedIndex = SelectedRule.SetTriggerActions.IndexOf(SelectedSetTriggerAction);
        if (removedIndex < 0)
        {
            SelectedSetTriggerAction = SelectedRule.SetTriggerActions.FirstOrDefault();
            return;
        }

        SelectedRule.SetTriggerActions.RemoveAt(removedIndex);
        SelectedSetTriggerAction = SelectedRule.SetTriggerActions.Count == 0
            ? null
            : SelectedRule.SetTriggerActions[Math.Min(removedIndex, SelectedRule.SetTriggerActions.Count - 1)];
        QueueSave();
        QueueBridgeRefresh();
        QueueManagedRewardSync(0);
    }

    private IReadOnlyList<TriggerRule> GetSelectedAvatarRedeemsByParameterType(OscParameterType parameterType)
    {
        return SelectedAvatarProfile?.ChannelPointRules
            .Where(rule => rule.ActionType == OscActionType.AvatarParameter && rule.ParameterType == parameterType)
            .ToArray()
            ?? [];
    }

    private IReadOnlyList<TriggerRule> GetSelectedAvatarOtherRedeems()
    {
        return SelectedAvatarProfile?.ChannelPointRules
            .Where(rule => rule.ActionType != OscActionType.SetTrigger
                && (rule.ActionType != OscActionType.AvatarParameter
                    || rule.ParameterType is not (OscParameterType.Bool or OscParameterType.Int or OscParameterType.Float)))
            .ToArray()
            ?? [];
    }

    private IReadOnlyList<TriggerRule> GetSelectedAvatarMixRedeems()
    {
        return SelectedAvatarProfile?.ChannelPointRules
            .Where(rule => rule.ActionType == OscActionType.SetTrigger)
            .ToArray()
            ?? [];
    }

    private void RaiseAvatarRedeemGroupProperties()
    {
        RaisePropertyChanged(nameof(SelectedAvatarBoolRedeems));
        RaisePropertyChanged(nameof(SelectedAvatarIntRedeems));
        RaisePropertyChanged(nameof(SelectedAvatarFloatRedeems));
        RaisePropertyChanged(nameof(SelectedAvatarMixRedeems));
        RaisePropertyChanged(nameof(SelectedAvatarOutfitChoices));
        RaisePropertyChanged(nameof(HasAvatarMixRedeems));
        RaisePropertyChanged(nameof(HasSelectedAvatarOutfitChoices));
        RaisePropertyChanged(nameof(SelectedAvatarOtherRedeems));
        RaisePropertyChanged(nameof(HasAvatarOtherRedeems));
        RaisePropertyChanged(nameof(IsSetTriggerMasterRewardEditorVisible));
    }

    private void RaiseSupporterRuleGroupProperties()
    {
        RefreshSupporterRuleScopeLabels();
        RaisePropertyChanged(nameof(SelectedAvatarSupporterRules));
        RaisePropertyChanged(nameof(ForceMovementOverrideRules));
        RaisePropertyChanged(nameof(GlobalSupporterRules));
        RaisePropertyChanged(nameof(HasSelectedAvatarSupporterRules));
        RaisePropertyChanged(nameof(HasForceMovementOverrideRules));
        RaisePropertyChanged(nameof(HasGlobalSupporterRules));
    }

    private IReadOnlyList<UniversalTriggerRule> GetUniversalTriggersByType(UniversalTriggerType triggerType)
    {
        return Settings.UniversalTriggers
            .Where(trigger => trigger.IsConfigured && trigger.TriggerType == triggerType)
            .OrderBy(trigger => trigger.DisplayTitle, StringComparer.CurrentCultureIgnoreCase)
            .ToArray();
    }

    private ICollectionView CreateUniversalTriggersGroupedView()
    {
        var view = CollectionViewSource.GetDefaultView(Settings.UniversalTriggers);
        view.GroupDescriptions.Clear();
        view.GroupDescriptions.Add(new PropertyGroupDescription(nameof(UniversalTriggerRule.TriggerGroupTitle)));
        view.SortDescriptions.Clear();
        view.SortDescriptions.Add(new SortDescription(nameof(UniversalTriggerRule.TriggerGroupSortOrder), ListSortDirection.Ascending));
        view.SortDescriptions.Add(new SortDescription(nameof(UniversalTriggerRule.DisplayTitle), ListSortDirection.Ascending));
        return view;
    }

    private void RaiseUniversalTriggerGroupProperties()
    {
        universalTriggersGroupedView?.Refresh();
        RaisePropertyChanged(nameof(UniversalTriggersGroupedView));
        RaisePropertyChanged(nameof(UniversalUnconfiguredTriggers));
        RaisePropertyChanged(nameof(UniversalChatCommandTriggers));
        RaisePropertyChanged(nameof(UniversalChannelPointRewardTriggers));
        RaisePropertyChanged(nameof(UniversalBitsTriggers));
        RaisePropertyChanged(nameof(UniversalSubscriptionTriggers));
        RaisePropertyChanged(nameof(UniversalGiftSubscriptionTriggers));
        RaisePropertyChanged(nameof(UniversalFollowTriggers));
        RaisePropertyChanged(nameof(UniversalUnconfiguredGroupTitle));
    }

    private void UpdateAvatarProfileActivityStates()
    {
        var currentAvatarId = GetBestKnownCurrentAvatarId();
        foreach (var profile in Settings.AvatarProfiles)
        {
            profile.IsCurrentAvatarActive = !string.IsNullOrWhiteSpace(currentAvatarId)
                && string.Equals(profile.AvatarId, currentAvatarId, StringComparison.Ordinal);
        }

        RaisePropertyChanged(nameof(CurrentVrChatAvatarDisplayName));
        RaisePropertyChanged(nameof(CurrentVrChatAvatarStatusText));
        RaisePropertyChanged(nameof(MasterAvatarDisplayName));
        RaisePropertyChanged(nameof(MasterAvatarReturnText));
        RaisePropertyChanged(nameof(SelectedAvatarProfileStatusText));
        UseCurrentVrChatAvatarForProfileCommand.NotifyCanExecuteChanged();
    }

    private AvatarTriggerProfile CreateDefaultAvatarProfile()
    {
        var isFirstProfile = Settings.AvatarProfiles.Count == 0;
        var nextAvatarSetNumber = Settings.AvatarProfiles.Count(profile => !profile.IsMasterProfile) + 1;
        var currentAvatarId = Settings.VrChat.CurrentAvatarId?.Trim() ?? string.Empty;
        var currentAvatarName = ResolveVrChatAvatarName(currentAvatarId);

        return new AvatarTriggerProfile
        {
            Name = isFirstProfile ? "Return Avatar" : $"Avatar Set {nextAvatarSetNumber}",
            IsMasterProfile = isFirstProfile,
            AvatarId = isFirstProfile ? currentAvatarId : string.Empty,
            AvatarName = isFirstProfile ? currentAvatarName : string.Empty,
            ChannelPointRules = new ObservableCollection<TriggerRule>
            {
                CreateDefaultAvatarProfileRule()
            }
        };
    }

    private static TriggerRule CreateDefaultAvatarProfileRule()
    {
        return CreateBaseRule("New Channel Point Trigger", TwitchTriggerType.ChannelPoints);
    }

    private static TriggerRule CreateDefaultOutfitChoiceRule(int choiceNumber, bool useSharedNumberedReward)
    {
        var normalizedChoiceNumber = Math.Max(1, choiceNumber);
        var label = $"Outfit {normalizedChoiceNumber}";
        var rule = CreateBaseRule(label, TwitchTriggerType.ChannelPoints);
        rule.ActionType = OscActionType.SetTrigger;
        rule.SharedRewardChoiceEnabled = true;
        rule.SharedRewardChoiceNumber = normalizedChoiceNumber;
        rule.SharedRewardHelpText = label;
        rule.ChannelPointRewardTitle = useSharedNumberedReward ? string.Empty : label;
        rule.ChannelPointRewardDescription = string.Empty;
        rule.DurationSeconds = 70;
        rule.CooldownSeconds = 30;
        rule.SetTriggerRestoreMode = SetTriggerRestoreMode.ConfiguredAndRelated;
        return rule;
    }

    private static UniversalTriggerRule CreateDefaultUniversalTrigger()
    {
        return new UniversalTriggerRule
        {
            Name = "New Universal Trigger",
            TriggerType = UniversalTriggerType.ChatCommand,
            ChatCommandPermission = ChatCommandPermission.Moderators,
            Actions = new ObservableCollection<UniversalTriggerAction>
            {
                new()
            }
        };
    }

    private AvatarScaleSet CreateDefaultAvatarScaleSet()
    {
        var nextNumber = Settings.AvatarScaleSets.Count + 1;
        return new AvatarScaleSet
        {
            Name = Settings.AvatarScaleSets.Count == 0 ? "Default Scale Set" : $"Scale Set {nextNumber}",
            ScaleRules = []
        };
    }

    private static AvatarScaleRule CreateDefaultAvatarScaleRule()
    {
        return new AvatarScaleRule
        {
            Name = "New Avatar Scale",
            TriggerType = AvatarScaleTriggerType.ChannelPointReward,
            RewardTitle = "Avatar Scale",
            RewardCost = 100,
            ChatCommandEnabled = false,
            CommandText = "!scale",
            ChatCommandPermission = ChatCommandPermission.Moderators,
            ScaleMode = AvatarScaleMode.SetHeight,
            TargetHeightMeters = 1.6,
            MinimumHeightMeters = 0.5,
            MaximumHeightMeters = 2.5,
            RelativeHeightMeters = 0.25,
            HideRewardWhenMinimumHeightReached = true,
            HideRewardWhenMaximumHeightReached = true,
            HeightMultiplier = 1.25,
            Preset = AvatarScalePreset.Normal,
            ActiveTimeSeconds = 0,
            CooldownSeconds = 30,
            RestoreMode = AvatarScaleRestoreMode.ConfiguredHeight,
            RestoreHeightMeters = 1.6,
            SetHeightTransitionSeconds = 0,
            RandomHeightTransitionSeconds = 0,
            RelativeHeightTransitionSeconds = 0,
            MultiplierTransitionSeconds = 0,
            PresetTransitionSeconds = 0,
            GlitchyRandomHeightTransitionSeconds = 0,
            SupporterGrowthTransitionSeconds = 0
        };
    }

    private MovementRedeemSet CreateDefaultMovementRedeemSet()
    {
        var nextNumber = Settings.MovementRedeemSets.Count + 1;
        return new MovementRedeemSet
        {
            Name = Settings.MovementRedeemSets.Count == 0 ? "Default Movement Set" : $"Movement Set {nextNumber}",
            MovementRules = []
        };
    }

    private static TriggerRule CreateDefaultMasterAvatarRule()
    {
        var rule = CreateBaseRule("New Avatar Change Trigger", TwitchTriggerType.ChannelPoints);
        rule.ActionType = OscActionType.AvatarChange;
        rule.DurationSeconds = 20;
        rule.CooldownSeconds = 30;
        return rule;
    }

    private static TriggerRule CreateDefaultMovementRule()
    {
        var rule = CreateBaseRule("New Movement Redeem", TwitchTriggerType.ChannelPoints);
        rule.ActionType = OscActionType.PlayerMovement;
        rule.DurationSeconds = 3;
        rule.CooldownSeconds = 0;
        return rule;
    }

    private static TriggerRule CreateDefaultOverrideRule()
    {
        var rule = CreateBaseRule("New Supporter Override", TwitchTriggerType.Bits);
        rule.MinimumAmount = 100;
        return rule;
    }

    private static TriggerRule CreateDefaultAvatarSupporterRule(string avatarId, string avatarName)
    {
        var rule = CreateBaseRule("New Avatar Supporter Trigger", TwitchTriggerType.Bits);
        rule.SupporterAvatarProfileId = Guid.Empty;
        rule.SupporterAvatarId = avatarId?.Trim() ?? string.Empty;
        rule.SupporterAvatarName = avatarName?.Trim() ?? string.Empty;
        rule.ActionType = OscActionType.AvatarParameter;
        rule.MinimumAmount = 100;
        return rule;
    }

    private static TriggerRule CreateDefaultForceMovementOverrideRule()
    {
        var rule = CreateBaseRule("New Force Movement", TwitchTriggerType.Bits);
        rule.ActionType = OscActionType.PlayerMovement;
        rule.MovementDirection = PlayerMovementDirection.Forward;
        rule.SupporterAvatarProfileId = Guid.Empty;
        rule.SupporterAvatarId = string.Empty;
        rule.SupporterAvatarName = string.Empty;
        rule.SupporterKeywordText = "forward";
        rule.MinimumAmount = 100;
        rule.DurationSeconds = 3;
        rule.CooldownSeconds = 10;
        return rule;
    }

    private static TriggerRule CreateBaseRule(string name, TwitchTriggerType triggerType)
    {
        return new TriggerRule
        {
            Name = name,
            TriggerType = triggerType,
            ChannelPointRewardTitle = string.Empty,
            ChannelPointRewardCost = 100,
            ChatCommandEnabled = false,
            ChatCommandText = string.Empty,
            ChatCommandPermission = ChatCommandPermission.Moderators,
            MinimumAmount = 1,
            AmountScaledDurationEnabled = false,
            AmountUnitsPerDuration = 1,
            SecondsPerAmountUnit = 1,
            BitsAmountUnitsPerDuration = 1,
            BitsSecondsPerAmountUnit = 1,
            SubscriptionsAmountUnitsPerDuration = 1,
            SubscriptionsSecondsPerAmountUnit = 1,
            MaxAccumulatedDurationEnabled = false,
            MaxAccumulatedDurationSeconds = 1800,
            ActionType = OscActionType.AvatarParameter,
            MovementDirection = PlayerMovementDirection.Forward,
            ParameterName = "VRCEmote",
            ParameterType = OscParameterType.Int,
            IntZeroDurationMode = IntZeroDurationMode.Fixed,
            ParameterValue = "1",
            ResetValue = "0",
            RangeMinimum = 0,
            RangeMaximum = 5,
            DurationSeconds = 10,
            CooldownSeconds = 30,
            BotMessageTemplate = "{user} triggered {rule}. Active for {duration}. Cooldown {cooldown}."
        };
    }

    private async Task EnsureSelectedAvatarParameterCacheLoadedAsync()
    {
        var avatarId = GetSelectedParameterCacheAvatarId();
        if (string.IsNullOrWhiteSpace(avatarId)
            || string.IsNullOrWhiteSpace(ResolveCurrentUserIdForCache()))
        {
            RefreshAvatarParameterOptions();
            return;
        }

        if (!cachedVrChatParametersByAvatarId.TryGetValue(avatarId, out var existingParameters)
            || existingParameters.Count == 0)
        {
            var loadedParameters = await LoadVrChatOscParametersForAvatarAsync(avatarId, CancellationToken.None);
            cachedVrChatParametersByAvatarId[avatarId] = [.. loadedParameters];
        }

        RefreshAvatarParameterOptions();
    }

    private async Task RefreshVrChatOscParametersAsync()
    {
        await RefreshVrChatOscParametersAsync(suppressErrors: false);
    }

    private async Task RefreshVrChatOscParametersAsync(bool suppressErrors)
    {
        if (string.IsNullOrWhiteSpace(ResolveCurrentUserIdForCache()))
        {
            VrChatOscParameterStatus = T("Connect VRChat to load avatar parameters.");
            return;
        }

        await RefreshCurrentVrChatAvatarFromLocalFilesAsync(CancellationToken.None);

        if (!SelectedParameterAvatarMatchesCurrentAvatar()
            && IsViewingAvatarTriggers)
        {
            TrySelectMatchingAvatarProfile(Settings.VrChat.CurrentAvatarId?.Trim() ?? string.Empty);
        }

        var avatarId = GetSelectedParameterCacheAvatarId();
        if (string.IsNullOrWhiteSpace(avatarId))
        {
            VrChatOscParameterStatus = T("Pick the avatar first, then refresh its OSC parameters.");
            return;
        }

        VrChatOscParameterStatus = T("Refreshing OSC parameters for this avatar...");

        await bridgeRefreshGate.WaitAsync();
        try
        {
            var refreshedParameters = await LoadVrChatOscParametersForAvatarAsync(avatarId, CancellationToken.None);
            await CacheVrChatOscParametersForAvatarAsync(avatarId, refreshedParameters, CancellationToken.None);
            RefreshAvatarParameterOptions();

            var avatarDisplayName = GetSafeVrChatAvatarDisplayName(ResolveVrChatAvatarName(avatarId));
            VrChatOscParameterStatus = refreshedParameters.Count == 0
                ? T("No OSC parameters were found for this avatar yet. Wear it once in VRChat with OSC on, then try again.")
                : TF("Loaded {0} saved OSC parameters for {1}.", refreshedParameters.Count, avatarDisplayName);

            if (!suppressErrors)
            {
                AppendLog(refreshedParameters.Count == 0
                    ? TF("No OSC parameters were found yet for {0}.", avatarDisplayName)
                    : TF("Loaded {0} OSC parameters for {1}.", refreshedParameters.Count, avatarDisplayName));
            }
        }
        catch (Exception ex)
        {
            if (!suppressErrors)
            {
                AppendLog(TF("Could not refresh OSC parameters: {0}", ex.Message));
                VrChatOscParameterStatus = TF("Could not refresh OSC parameters: {0}", ex.Message);
            }
        }
        finally
        {
            bridgeRefreshGate.Release();
            await EnsureSelectedAvatarParameterCacheLoadedAsync();
        }
    }

    private void RefreshAvatarParameterOptions()
    {
        RunOnUi(() =>
        {
            var selectedAvatarId = GetSelectedParameterCacheAvatarId();
            RefreshAvatarParameterOptionsCore(selectedAvatarId);
            RefreshSetTriggerParameterOptionsCore(selectedAvatarId);
            UpdateVrChatOscParameterStatus(selectedAvatarId);
        });
    }

    private void RefreshSetTriggerParameterOptions()
    {
        RunOnUi(() =>
        {
            var selectedAvatarId = GetSelectedParameterCacheAvatarId();
            RefreshSetTriggerParameterOptionsCore(selectedAvatarId);
            UpdateVrChatOscParameterStatus(selectedAvatarId);
        });
    }

    private void RefreshAvatarParameterOptionsCore(string selectedAvatarId)
    {
        var isAvatarParameterRule = SelectedRule?.ActionType == OscActionType.AvatarParameter;
        isRestoringAvatarParameterSelection = true;
        try
        {
            if (!isAvatarParameterRule || SelectedRule is null)
            {
                SelectedAvatarParameterOption = null;
                return;
            }

            var selectedParameterName = SelectedRule.ParameterName ?? string.Empty;
            var selectedParameterAddress = NormalizeAvatarParameterAddressOrEmpty(selectedParameterName);
            ReplaceCollectionIfChanged(
                AvatarParameterOptions,
                BuildAvatarParameterOptionsForType(selectedAvatarId, SelectedRule.ParameterType, selectedParameterName));

            SelectedAvatarParameterOption = AvatarParameterOptions.FirstOrDefault(option =>
                string.Equals(option.Address, selectedParameterAddress, StringComparison.Ordinal));
        }
        finally
        {
            isRestoringAvatarParameterSelection = false;
        }
    }

    private void RefreshSetTriggerParameterOptionsCore(string selectedAvatarId)
    {
        var selectedSetTriggerAction = SelectedRule?.ActionType == OscActionType.SetTrigger
            ? SelectedSetTriggerAction
            : null;

        isRestoringSetTriggerParameterSelection = true;
        try
        {
            if (selectedSetTriggerAction is null)
            {
                ReplaceCollectionIfChanged(SetTriggerParameterOptions, []);
                SelectedSetTriggerParameterOption = null;
                return;
            }

            var selectedParameterName = selectedSetTriggerAction.ParameterName ?? string.Empty;
            var selectedParameterAddress = NormalizeAvatarParameterAddressOrEmpty(selectedParameterName);
            ReplaceCollectionIfChanged(
                SetTriggerParameterOptions,
                BuildAvatarParameterOptionsForType(selectedAvatarId, selectedSetTriggerAction.ParameterType, selectedParameterName));

            SelectedSetTriggerParameterOption = SetTriggerParameterOptions.FirstOrDefault(option =>
                string.Equals(option.Address, selectedParameterAddress, StringComparison.Ordinal));
        }
        finally
        {
            isRestoringSetTriggerParameterSelection = false;
        }
    }

    private List<VrChatOscParameterSummary> BuildAvatarParameterOptionsForType(
        string selectedAvatarId,
        OscParameterType parameterType,
        string selectedParameterName)
    {
        var cachedParameters = !string.IsNullOrWhiteSpace(selectedAvatarId)
            && cachedVrChatParametersByAvatarId.TryGetValue(selectedAvatarId, out var loadedParameters)
            ? loadedParameters
            : BuildFallbackAvatarParameterOptions();

        var nextOptions = cachedParameters
            .Where(parameter => parameter.ParameterType == parameterType)
            .OrderBy(parameter => parameter.Name, StringComparer.OrdinalIgnoreCase)
            .ToList();

        var selectedParameterAddress = NormalizeAvatarParameterAddressOrEmpty(selectedParameterName);
        if (!string.IsNullOrWhiteSpace(selectedParameterAddress)
            && !nextOptions.Any(option => string.Equals(option.Address, selectedParameterAddress, StringComparison.Ordinal)))
        {
            nextOptions.Insert(0, CreateCustomAvatarParameterOption(selectedParameterAddress, parameterType));
        }

        return nextOptions;
    }

    private void UpdateVrChatOscParameterStatus(string selectedAvatarId)
    {
        if (string.IsNullOrWhiteSpace(ResolveCurrentUserIdForCache()))
        {
            VrChatOscParameterStatus = T("Connect VRChat to load avatar parameters.");
        }
        else if (string.IsNullOrWhiteSpace(selectedAvatarId))
        {
            VrChatOscParameterStatus = T("Pick the avatar first, then Crystal Relay can use its saved OSC parameters.");
        }
        else if (cachedVrChatParametersByAvatarId.TryGetValue(selectedAvatarId, out var avatarParameters) && avatarParameters.Count > 0)
        {
            VrChatOscParameterStatus = SelectedParameterAvatarMatchesCurrentAvatar()
                ? TF("Showing saved OSC parameters for your current avatar: {0}.", CurrentVrChatAvatarDisplayName)
                : T("Showing the saved OSC parameters from the last time you used this avatar.");
        }
        else
        {
            VrChatOscParameterStatus = SelectedParameterAvatarMatchesCurrentAvatar()
                ? T("No saved OSC parameters yet. Wear this avatar in VRChat to generate them.")
                : T("No saved OSC parameters for this avatar yet. Switch to it in VRChat to generate them.");
        }
    }

    private static string NormalizeAvatarParameterAddressOrEmpty(string parameterName)
    {
        return string.IsNullOrWhiteSpace(parameterName)
            ? string.Empty
            : VrChatOscClient.NormalizeAvatarParameterAddress(parameterName);
    }

    private string GetSelectedParameterCacheAvatarId()
    {
        if (IsViewingPowerUps)
        {
            var powerUpAvatarId = SelectedPowerUpRule?.AvatarScoped == true
                ? SelectedPowerUpRule.AvatarId?.Trim() ?? string.Empty
                : string.Empty;
            return !string.IsNullOrWhiteSpace(powerUpAvatarId)
                ? powerUpAvatarId
                : Settings.VrChat.CurrentAvatarId?.Trim() ?? string.Empty;
        }

        return SelectedAvatarProfile?.AvatarId?.Trim() ?? string.Empty;
    }

    private bool SelectedParameterAvatarMatchesCurrentAvatar()
    {
        var selectedAvatarId = GetSelectedParameterCacheAvatarId();
        var currentAvatarId = Settings.VrChat.CurrentAvatarId?.Trim() ?? string.Empty;
        return !string.IsNullOrWhiteSpace(selectedAvatarId)
            && !string.IsNullOrWhiteSpace(currentAvatarId)
            && string.Equals(selectedAvatarId, currentAvatarId, StringComparison.Ordinal);
    }

    private void HandleVrChatAvatarChangedByBridge(string avatarId, bool queueManagedRewardSync = true)
    {
        var normalizedAvatarId = avatarId?.Trim() ?? string.Empty;
        if (string.IsNullOrWhiteSpace(normalizedAvatarId))
        {
            return;
        }

        var previousAvatarId = Settings.VrChat.CurrentAvatarId?.Trim() ?? string.Empty;
        bridgeCoordinator.UpdateCurrentVrChatAvatar(normalizedAvatarId);
        Settings.VrChat.CurrentAvatarId = normalizedAvatarId;
        lastDetectedVrChatAvatarId = normalizedAvatarId;
        ReplaceCurrentAvatarInCache(normalizedAvatarId);
        UpdateAvatarProfileActivityStates();
        if (IsViewingAvatarTriggers
            && !string.IsNullOrWhiteSpace(previousAvatarId)
            && SelectedAvatarProfile is not null
            && string.Equals(SelectedAvatarProfile.AvatarId?.Trim() ?? string.Empty, previousAvatarId, StringComparison.Ordinal))
        {
            TrySelectMatchingAvatarProfile(normalizedAvatarId);
        }

        RefreshVrChatAvatarSelectionOptions();
        RefreshAvatarParameterOptions();
        QueueSave();
        if (queueManagedRewardSync)
        {
            // Delay the avatar-changed sync so the OSC parameter refresh (which starts
            // after VrChatOscParameterAutoRefreshInitialDelay) has a chance to load
            // parameters for the new avatar before the sync fires. When parameters
            // finish loading, the RuntimeAvailability sync cancels this pending
            // AvatarChanged sync and fires with the correct parameter-availability
            // state — coalescing two syncs into one and halving the Twitch API calls.
            // If parameters are not available after the refresh, this sync fires with
            // rewards disabled (parameter-dependent rewards stay hidden), which is the
            // correct fallback behavior.
            QueueManagedRewardSync((int)VrChatOscParameterAutoRefreshInitialDelay.TotalMilliseconds, ManagedRewardSyncReason.AvatarChanged);
        }

        _ = EnsureSelectedAvatarParameterCacheLoadedAsync();
        QueueCurrentVrChatOscParameterRefresh(normalizedAvatarId, queueManagedRewardSync);
    }

    private void HandleSharedReturnAvatarChangedByBridge(string avatarId, string avatarName)
    {
        ApplySharedReturnAvatarSelection(avatarId, avatarName, saveImmediately: true);
    }

    // Per-rule PATCH path. When a single rule's cooldown state flips (start or expire)
    // the bridge fires RewardCooldownColorChanged(ruleId). We look up the rule's
    // reward id, find it in the cached Twitch catalog, and PATCH just that reward's
    // background color. Skips when the cached color already matches, so no PATCH
    // and no "modified" timestamp bump when there's nothing to change. No catalog
    // GET, no per-target loop, no mass sync.
    private async Task HandleRewardCooldownColorChangedAsync(Guid ruleId)
    {
        try
        {
            if (!isInitialized || isShuttingDown)
            {
                return;
            }
            if (!HasRecoverableBroadcasterSession)
            {
                return;
            }
            if (broadcasterManagedRewardsUnavailableForSession
                || BroadcasterRewardManagementScopeKnownMissing)
            {
                return;
            }

            var rewardId = ResolveManagedRewardIdForRule(ruleId);
            if (string.IsNullOrWhiteSpace(rewardId))
            {
                return;
            }

            // The avatar scale master reward tracks its cooldown in dedicated unlock/cooldown
            // fields (not the shared per-rule cooldown dictionary) because the master reward is
            // identified by a fixed Guid rather than a TriggerRule. Without this special case the
            // per-reward PATCH would always see "ready" and never apply the cooldown color while
            // the master reward is unlocked or cooling down.
            var isOnCooldown = ruleId == AvatarScaleMasterRewardOwnerId
                ? bridgeCoordinator.IsAvatarScaleMasterRewardOnCooldown()
                : bridgeCoordinator.GetRulesOnCooldownIds().Contains(ruleId);
            var configuredColor = ResolveConfiguredRewardColor(ruleId, isOnCooldown);
            if (string.IsNullOrWhiteSpace(configuredColor))
            {
                return;
            }

            var cachedOption = RewardOptions.FirstOrDefault(option =>
                !string.IsNullOrEmpty(option.Id) && string.Equals(option.Id, rewardId, StringComparison.Ordinal));
            if (cachedOption is null || cachedOption.IsCatalogMissing)
            {
                return;
            }

            if (string.Equals(cachedOption.BackgroundColor, configuredColor, StringComparison.OrdinalIgnoreCase))
            {
                return;
            }

            await ReloadRuntimeConfigAsync();
            var accessToken = Settings.Broadcaster.AccessToken;
            var clientId = runtimeConfig.TwitchClientId;
            var broadcasterId = Settings.Broadcaster.UserId;
            if (string.IsNullOrWhiteSpace(accessToken)
                || string.IsNullOrWhiteSpace(clientId)
                || string.IsNullOrWhiteSpace(broadcasterId))
            {
                return;
            }

            var updatedReward = await twitchApiClient.UpdateCustomRewardAsync(
                accessToken,
                clientId,
                broadcasterId,
                cachedOption.Id,
                cachedOption.Title,
                cachedOption.Cost,
                cachedOption.IsEnabled,
                cachedOption.CooldownSeconds,
                configuredColor,
                CancellationToken.None,
                cachedOption.Prompt,
                cachedOption.IsUserInputRequired);

            ApplySingleRewardCatalogUpdate(updatedReward);
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            DebugLogService.Write($"Per-reward color PATCH for rule {ruleId} failed: {ex.Message}");
        }
    }

    private string? ResolveManagedRewardIdForRule(Guid ruleId)
    {
        foreach (var profile in Settings.AvatarProfiles)
        {
            foreach (var rule in profile.ChannelPointRules)
            {
                if (rule.Id == ruleId && !string.IsNullOrWhiteSpace(rule.ChannelPointRewardId))
                {
                    return rule.ChannelPointRewardId.Trim();
                }
            }
        }
        foreach (var set in Settings.MovementRedeemSets)
        {
            foreach (var rule in set.MovementRules)
            {
                if (rule.Id == ruleId && !string.IsNullOrWhiteSpace(rule.ChannelPointRewardId))
                {
                    return rule.ChannelPointRewardId.Trim();
                }
            }
        }
        foreach (var rule in Settings.Rules)
        {
            if (rule.Id == ruleId && !string.IsNullOrWhiteSpace(rule.ChannelPointRewardId))
            {
                return rule.ChannelPointRewardId.Trim();
            }
        }
        foreach (var rule in Settings.GlobalOverrideRules)
        {
            if (rule.Id == ruleId && !string.IsNullOrWhiteSpace(rule.ChannelPointRewardId))
            {
                return rule.ChannelPointRewardId.Trim();
            }
        }
        foreach (var rule in Settings.GlobalMovementRules)
        {
            if (rule.Id == ruleId && !string.IsNullOrWhiteSpace(rule.ChannelPointRewardId))
            {
                return rule.ChannelPointRewardId.Trim();
            }
        }
        foreach (var swapProfile in Settings.AvatarSwapProfiles)
        {
            foreach (var rule in swapProfile.ChannelPointRules)
            {
                if (rule.Id == ruleId && !string.IsNullOrWhiteSpace(rule.ChannelPointRewardId))
                {
                    return rule.ChannelPointRewardId.Trim();
                }
            }
        }
        foreach (var trigger in Settings.UniversalTriggers)
        {
            if (trigger.Id == ruleId && !string.IsNullOrWhiteSpace(trigger.RewardId))
            {
                return trigger.RewardId.Trim();
            }
        }
        foreach (var set in Settings.AvatarScaleSets)
        {
            foreach (var rule in set.ScaleRules)
            {
                if (rule.Id == ruleId && !string.IsNullOrWhiteSpace(rule.RewardId))
                {
                    return rule.RewardId.Trim();
                }
            }
        }
        if (ruleId == AvatarScaleMasterRewardOwnerId)
        {
            var masterReward = Settings.AvatarScaleMasterReward;
            if (masterReward is not null
                && !string.IsNullOrWhiteSpace(masterReward.RewardId)
                && !string.IsNullOrWhiteSpace(masterReward.RewardTitle))
            {
                return masterReward.RewardId.Trim();
            }
        }
        return null;
    }

    private string? ResolveConfiguredRewardColor(Guid ruleId, bool isOnCooldown)
    {
        foreach (var profile in Settings.AvatarProfiles)
        {
            foreach (var rule in profile.ChannelPointRules)
            {
                if (rule.Id == ruleId)
                {
                    return ManagedRewardPresentation.NormalizeReadyBackgroundColor(
                        isOnCooldown ? rule.ManagedRewardCooldownColor : rule.ManagedRewardReadyColor);
                }
            }
        }
        foreach (var set in Settings.MovementRedeemSets)
        {
            foreach (var rule in set.MovementRules)
            {
                if (rule.Id == ruleId)
                {
                    return ManagedRewardPresentation.NormalizeReadyBackgroundColor(
                        isOnCooldown ? rule.ManagedRewardCooldownColor : rule.ManagedRewardReadyColor);
                }
            }
        }
        foreach (var rule in Settings.Rules)
        {
            if (rule.Id == ruleId)
            {
                return ManagedRewardPresentation.NormalizeReadyBackgroundColor(
                    isOnCooldown ? rule.ManagedRewardCooldownColor : rule.ManagedRewardReadyColor);
            }
        }
        foreach (var rule in Settings.GlobalOverrideRules)
        {
            if (rule.Id == ruleId)
            {
                return ManagedRewardPresentation.NormalizeReadyBackgroundColor(
                    isOnCooldown ? rule.ManagedRewardCooldownColor : rule.ManagedRewardReadyColor);
            }
        }
        foreach (var rule in Settings.GlobalMovementRules)
        {
            if (rule.Id == ruleId)
            {
                return ManagedRewardPresentation.NormalizeReadyBackgroundColor(
                    isOnCooldown ? rule.ManagedRewardCooldownColor : rule.ManagedRewardReadyColor);
            }
        }
        foreach (var swapProfile in Settings.AvatarSwapProfiles)
        {
            foreach (var rule in swapProfile.ChannelPointRules)
            {
                if (rule.Id == ruleId)
                {
                    return ManagedRewardPresentation.NormalizeReadyBackgroundColor(
                        isOnCooldown ? rule.ManagedRewardCooldownColor : rule.ManagedRewardReadyColor);
                }
            }
        }
        foreach (var set in Settings.AvatarScaleSets)
        {
            foreach (var rule in set.ScaleRules)
            {
                if (rule.Id == ruleId)
                {
                    return ManagedRewardPresentation.NormalizeReadyBackgroundColor(
                        isOnCooldown ? rule.ManagedRewardCooldownColor : rule.ManagedRewardReadyColor);
                }
            }
        }
        if (ruleId == AvatarScaleMasterRewardOwnerId)
        {
            var masterReward = Settings.AvatarScaleMasterReward;
            if (masterReward is not null && !string.IsNullOrWhiteSpace(masterReward.RewardTitle))
            {
                return ManagedRewardPresentation.NormalizeReadyBackgroundColor(
                    isOnCooldown ? masterReward.ManagedRewardCooldownColor : masterReward.ManagedRewardReadyColor);
            }
        }
        return null;
    }

    private void ApplySingleRewardCatalogUpdate(TwitchApiClient.CustomRewardResponse updatedReward)
    {
        RunOnUi(() =>
        {
            for (var i = 0; i < RewardOptions.Count; i++)
            {
                if (string.Equals(RewardOptions[i].Id, updatedReward.Id, StringComparison.Ordinal))
                {
                    RewardOptions[i] = TwitchRewardOption.FromReward(updatedReward);
                    return;
                }
            }
        });
    }

    // The ManagedRewardAvailabilityChanged event fires for local-only state flips
    // (per-rule cooldowns starting/ending, timed effect windows opening/closing,
    // reset notifications, avatar-change transitions) plus the rare Twitch-visible
    // changes like a manual toggle. Twitch does not see local cooldowns, timed
    // effects, or reset notifications, and any "change" the per-target check would
    // detect from this path (e.g. avatar detection flicker in test mode flipping
    // DesiredEnabled) is a churn PATCH that just bumps the per-reward "modified"
    // timestamp on Twitch for nothing. Real Twitch-visible changes have their own
    // dedicated sync triggers (settings-edit, stream-state-changed, avatar-changed,
    // fire-sale-changed, account-reconnect, emergency-stop, test-mode, manual-refresh),
    // so this handler intentionally does NOT queue a sync.
    private void HandleManagedRewardAvailabilityChanged()
    {
    }

    // BuildAccountIdentityFingerprint captures the Twitch-visible identity fields
    // (UserId, Login, Scopes) of an account. Access tokens, refresh tokens, and
    // expiry timestamps are intentionally excluded: a bare token refresh on the
    // same broadcaster login does not change the Twitch-side reward ownership,
    // and firing a full reward sync on every refresh causes a mass PATCH storm
    // that bumps every reward's "modified" timestamp.
    private string BuildAccountIdentityFingerprint(BridgeAccountRole role)
    {
        var account = role == BridgeAccountRole.Broadcaster
            ? Settings.Broadcaster
            : Settings.Bot;
        if (account is null)
        {
            return string.Empty;
        }

        var builder = new StringBuilder();
        AppendFingerprintValue(builder, account.UserId);
        AppendFingerprintValue(builder, account.Login);
        AppendFingerprintValue(builder, account.DisplayName);
        AppendFingerprintValue(builder, account.ProfileImageUrl);
        if (account.Scopes is not null)
        {
            foreach (var scope in account.Scopes.OrderBy(s => s, StringComparer.Ordinal))
            {
                builder.Append('|').Append(scope).Append(',');
            }
        }
        return builder.ToString();
    }

    private bool HasAccountIdentityChanged(BridgeAccountRole role, string previousIdentityFingerprint)
    {
        var currentFingerprint = BuildAccountIdentityFingerprint(role);
        var lastObservedField = role == BridgeAccountRole.Broadcaster
            ? ref lastObservedBroadcasterIdentityFingerprint
            : ref lastObservedBotIdentityFingerprint;

        if (string.Equals(currentFingerprint, lastObservedField, StringComparison.Ordinal))
        {
            return false;
        }
        if (string.Equals(currentFingerprint, previousIdentityFingerprint, StringComparison.Ordinal))
        {
            return false;
        }

        lastObservedField = currentFingerprint;
        return true;
    }

    private async Task<List<VrChatOscParameterSummary>> LoadLiveVrChatOscParametersAsync(CancellationToken cancellationToken)
    {
        await EnsureBridgeStateAsync(cancellationToken, allowOscOnly: true);

        return (await bridgeCoordinator.GetCurrentAvatarParametersAsync(cancellationToken))
            .Where(parameter => parameter.ParameterType is OscParameterType.Bool or OscParameterType.Int or OscParameterType.Float)
            .ToList();
    }

    private async Task<List<VrChatOscParameterSummary>> TryLoadVrChatOscParametersFromLocalCacheAsync(
        string avatarId,
        CancellationToken cancellationToken)
    {
        var resolvedUserId = ResolveCurrentUserIdForCache();
        if (string.IsNullOrWhiteSpace(resolvedUserId))
        {
            return [];
        }

        var localParameters = await vrChatLocalOscCacheService.LoadAvatarParametersAsync(
            resolvedUserId,
            avatarId,
            cancellationToken);

        return [.. localParameters];
    }

    private async Task<List<VrChatOscParameterSummary>> LoadVrChatOscParametersForAvatarAsync(
        string avatarId,
        CancellationToken cancellationToken)
    {
        var normalizedAvatarId = avatarId?.Trim() ?? string.Empty;
        if (TryGetLocalAvatarOscFileWriteTime(normalizedAvatarId, out var localWriteTimeUtc))
        {
            var localParameters = await TryLoadVrChatOscParametersFromLocalCacheAsync(normalizedAvatarId, cancellationToken);
            vrChatLocalOscAvatarWriteTimes[normalizedAvatarId] = localWriteTimeUtc;
            return localParameters;
        }

        var cachedParameters = await settingsStore.LoadVrChatOscParameterCacheAsync(
            ResolveCurrentUserIdForCache() ?? string.Empty,
            normalizedAvatarId,
            cancellationToken);
        if (cachedParameters.Count > 0)
        {
            return [.. cachedParameters];
        }

        if (string.Equals(Settings.VrChat.CurrentAvatarId?.Trim() ?? string.Empty, normalizedAvatarId, StringComparison.Ordinal))
        {
            return await LoadLiveVrChatOscParametersAsync(cancellationToken);
        }

        return [];
    }

    private bool TryGetLocalAvatarOscFileWriteTime(string avatarId, out DateTime lastWriteTimeUtc)
    {
        lastWriteTimeUtc = DateTime.MinValue;

        var resolvedUserId = ResolveCurrentUserIdForCache();
        if (string.IsNullOrWhiteSpace(resolvedUserId)
            || string.IsNullOrWhiteSpace(avatarId))
        {
            return false;
        }

        var filePath = VrChatLocalOscCacheService.GetAvatarOscFilePath(resolvedUserId, avatarId);
        if (string.IsNullOrWhiteSpace(filePath) || !File.Exists(filePath))
        {
            return false;
        }

        lastWriteTimeUtc = File.GetLastWriteTimeUtc(filePath);
        return true;
    }

    private async Task CacheVrChatOscParametersForAvatarAsync(
        string avatarId,
        IReadOnlyList<VrChatOscParameterSummary> parameters,
        CancellationToken cancellationToken,
        bool queueManagedRewardSync = true)
    {
        var normalizedAvatarId = avatarId?.Trim() ?? string.Empty;
        var resolvedUserId = ResolveCurrentUserIdForCache();
        if (string.IsNullOrWhiteSpace(resolvedUserId)
            || string.IsNullOrWhiteSpace(normalizedAvatarId))
        {
            return;
        }

        cachedVrChatParametersByAvatarId[normalizedAvatarId] = [.. parameters];
        await settingsStore.SaveVrChatOscParameterCacheAsync(
            resolvedUserId,
            normalizedAvatarId,
            parameters,
            cancellationToken);

        if (string.Equals(Settings.VrChat.CurrentAvatarId?.Trim() ?? string.Empty, normalizedAvatarId, StringComparison.Ordinal))
        {
            if (queueManagedRewardSync)
            {
                QueueManagedRewardSync(0, ManagedRewardSyncReason.RuntimeAvailability);
            }
        }
    }

    private void QueueCurrentVrChatOscParameterRefresh(string avatarId, bool queueManagedRewardSync = true)
    {
        if (!isInitialized || isShuttingDown || string.IsNullOrWhiteSpace(ResolveCurrentUserIdForCache()))
        {
            return;
        }

        var normalizedAvatarId = avatarId?.Trim() ?? string.Empty;
        if (string.IsNullOrWhiteSpace(normalizedAvatarId))
        {
            return;
        }

        var parameterRefreshCancellation = ReplaceQueuedCancellationSource(ref vrChatOscParameterRefreshCancellation);
        var cancellationToken = parameterRefreshCancellation.Token;

        _ = Task.Run(async () =>
        {
            Exception? lastError = null;

            try
            {
                await Task.Delay(VrChatOscParameterAutoRefreshInitialDelay, cancellationToken);

                for (var pass = 0; pass < VrChatOscParameterAutoRefreshPassCount; pass++)
                {
                    cancellationToken.ThrowIfCancellationRequested();

                    if (!string.Equals(Settings.VrChat.CurrentAvatarId?.Trim() ?? string.Empty, normalizedAvatarId, StringComparison.Ordinal))
                    {
                        return;
                    }

                    await bridgeRefreshGate.WaitAsync(cancellationToken);
                    try
                    {
                        var refreshedParameters = await LoadVrChatOscParametersForAvatarAsync(normalizedAvatarId, cancellationToken);

                        if (!string.Equals(Settings.VrChat.CurrentAvatarId?.Trim() ?? string.Empty, normalizedAvatarId, StringComparison.Ordinal))
                        {
                            return;
                        }

                        await CacheVrChatOscParametersForAvatarAsync(
                            normalizedAvatarId,
                            refreshedParameters,
                            cancellationToken,
                            queueManagedRewardSync);
                        RefreshAvatarParameterOptions();
                        lastError = null;

                        if (refreshedParameters.Count > 0)
                        {
                            return;
                        }
                    }
                    catch (OperationCanceledException)
                    {
                        throw;
                    }
                    catch (Exception ex)
                    {
                        lastError = ex;
                    }
                    finally
                    {
                        bridgeRefreshGate.Release();
                    }

                    if (pass < VrChatOscParameterAutoRefreshPassCount - 1)
                    {
                        await Task.Delay(VrChatOscParameterAutoRefreshRetryDelay, cancellationToken);
                    }
                }

                if (lastError is not null
                    && string.Equals(Settings.VrChat.CurrentAvatarId?.Trim() ?? string.Empty, normalizedAvatarId, StringComparison.Ordinal))
                {
                    RunOnUi(() => AppendLog($"Could not refresh OSC parameters for {GetSafeVrChatAvatarDisplayName(ResolveVrChatAvatarName(normalizedAvatarId))} after the avatar swap: {lastError.Message}"));
                }
            }
            catch (OperationCanceledException)
            {
            }
            finally
            {
                DisposeCompletedQueuedCancellationSource(ref vrChatOscParameterRefreshCancellation, parameterRefreshCancellation);
            }
        }, CancellationToken.None);
    }

    private void ReplaceCurrentAvatarInCache(string currentAvatarId)
    {
        if (availableVrChatAvatars.Count == 0)
        {
            return;
        }

        // Only flip the old/new current-avatar markers in place. Rebuilding the whole list
        // creates avoidable UI churn because VRChat can report avatar changes frequently.
        var previousCurrentIndex = -1;
        var nextCurrentIndex = -1;
        for (var index = 0; index < availableVrChatAvatars.Count; index++)
        {
            var avatar = availableVrChatAvatars[index];
            if (avatar.IsCurrentAvatar)
            {
                previousCurrentIndex = index;
            }

            if (string.Equals(avatar.Id, currentAvatarId, StringComparison.Ordinal))
            {
                nextCurrentIndex = index;
            }

            if (previousCurrentIndex >= 0 && nextCurrentIndex >= 0)
            {
                break;
            }
        }

        if (previousCurrentIndex < 0 && nextCurrentIndex < 0)
        {
            return;
        }

        if (previousCurrentIndex >= 0
            && previousCurrentIndex != nextCurrentIndex
            && availableVrChatAvatars[previousCurrentIndex].IsCurrentAvatar)
        {
            availableVrChatAvatars[previousCurrentIndex] =
                availableVrChatAvatars[previousCurrentIndex] with { IsCurrentAvatar = false };
        }

        if (nextCurrentIndex >= 0 && !availableVrChatAvatars[nextCurrentIndex].IsCurrentAvatar)
        {
            availableVrChatAvatars[nextCurrentIndex] =
                availableVrChatAvatars[nextCurrentIndex] with { IsCurrentAvatar = true };
        }
    }

    private bool TrySelectMatchingAvatarProfile(string avatarId)
    {
        var normalizedAvatarId = avatarId?.Trim() ?? string.Empty;
        if (string.IsNullOrWhiteSpace(normalizedAvatarId))
        {
            return false;
        }

        var matchingProfile = AvatarRuleProfiles.FirstOrDefault(profile =>
            string.Equals(profile.AvatarId?.Trim() ?? string.Empty, normalizedAvatarId, StringComparison.Ordinal));

        if (matchingProfile is null || ReferenceEquals(matchingProfile, SelectedAvatarProfile))
        {
            return false;
        }

        SelectedAvatarProfile = matchingProfile;
        return true;
    }

    private void ReplaceAvailableVrChatAvatars(IEnumerable<VrChatAvatarSummary> avatars)
    {
        availableVrChatAvatars.Clear();
        availableVrChatAvatarNamesById.Clear();

        foreach (var avatar in avatars)
        {
            availableVrChatAvatars.Add(avatar);

            if (string.IsNullOrWhiteSpace(avatar.Id))
            {
                continue;
            }

            availableVrChatAvatarNamesById[avatar.Id] = avatar.Name?.Trim() ?? string.Empty;
        }
    }

    private void ClearAvailableVrChatAvatars()
    {
        availableVrChatAvatars.Clear();
        availableVrChatAvatarNamesById.Clear();
    }

    private static List<VrChatOscParameterSummary> BuildFallbackAvatarParameterOptions()
    {
        return
        [
            new VrChatOscParameterSummary("/avatar/parameters/AFK", "AFK", OscParameterType.Bool),
            new VrChatOscParameterSummary("/avatar/parameters/MuteSelf", "MuteSelf", OscParameterType.Bool),
            new VrChatOscParameterSummary("/avatar/parameters/Seated", "Seated", OscParameterType.Bool),
            new VrChatOscParameterSummary("/avatar/parameters/Toggle", "Toggle", OscParameterType.Bool),
            new VrChatOscParameterSummary("/avatar/parameters/GestureLeft", "GestureLeft", OscParameterType.Int),
            new VrChatOscParameterSummary("/avatar/parameters/GestureRight", "GestureRight", OscParameterType.Int),
            new VrChatOscParameterSummary("/avatar/parameters/VRCEmote", "VRCEmote", OscParameterType.Int),
            new VrChatOscParameterSummary("/avatar/parameters/VRCFaceBlendH", "VRCFaceBlendH", OscParameterType.Float),
            new VrChatOscParameterSummary("/avatar/parameters/VRCFaceBlendV", "VRCFaceBlendV", OscParameterType.Float),
            new VrChatOscParameterSummary("/avatar/parameters/VelocityX", "VelocityX", OscParameterType.Float),
            new VrChatOscParameterSummary("/avatar/parameters/VelocityY", "VelocityY", OscParameterType.Float),
            new VrChatOscParameterSummary("/avatar/parameters/VelocityZ", "VelocityZ", OscParameterType.Float)
        ];
    }

    private static VrChatOscParameterSummary CreateCustomAvatarParameterOption(string parameterName, OscParameterType parameterType)
    {
        var normalizedAddress = VrChatOscClient.NormalizeAvatarParameterAddress(parameterName);
        var displayName = normalizedAddress.Split('/', StringSplitOptions.RemoveEmptyEntries).LastOrDefault() ?? normalizedAddress;
        return new VrChatOscParameterSummary(normalizedAddress, displayName, parameterType);
    }

    private bool NormalizeSupporterAvatarScopes()
    {
        var changed = false;
        foreach (var rule in Settings.GlobalOverrideRules)
        {
            if (IsSupporterAvatarChangeOverride(rule))
            {
                if (!string.IsNullOrWhiteSpace(rule.SupporterAvatarId))
                {
                    rule.SupporterAvatarId = string.Empty;
                    changed = true;
                }

                if (!string.IsNullOrWhiteSpace(rule.SupporterAvatarName))
                {
                    rule.SupporterAvatarName = string.Empty;
                    changed = true;
                }

                if (rule.SupporterAvatarProfileId != Guid.Empty)
                {
                    rule.SupporterAvatarProfileId = Guid.Empty;
                    changed = true;
                }

                continue;
            }

            if (rule.SupporterAvatarProfileId != Guid.Empty)
            {
                var profile = Settings.AvatarProfiles.FirstOrDefault(candidate => candidate.Id == rule.SupporterAvatarProfileId);
                if (profile is not null && !string.IsNullOrWhiteSpace(profile.AvatarId))
                {
                    var profileAvatarId = profile.AvatarId.Trim();
                    var profileAvatarName = string.IsNullOrWhiteSpace(profile.AvatarName)
                        ? ResolveVrChatAvatarName(profileAvatarId)
                        : profile.AvatarName.Trim();
                    if (!string.Equals(rule.SupporterAvatarId, profileAvatarId, StringComparison.Ordinal))
                    {
                        rule.SupporterAvatarId = profileAvatarId;
                        changed = true;
                    }

                    if (!string.Equals(rule.SupporterAvatarName, profileAvatarName, StringComparison.Ordinal))
                    {
                        rule.SupporterAvatarName = profileAvatarName;
                        changed = true;
                    }
                }

                rule.SupporterAvatarProfileId = Guid.Empty;
                changed = true;
            }

            if (string.IsNullOrWhiteSpace(rule.SupporterAvatarId)
                && TryResolveDefaultSupporterAvatar(out var defaultAvatarId, out var defaultAvatarName))
            {
                rule.SupporterAvatarId = defaultAvatarId;
                rule.SupporterAvatarName = defaultAvatarName;
                changed = true;
            }

            if (!string.IsNullOrWhiteSpace(rule.SupporterAvatarId)
                && string.IsNullOrWhiteSpace(rule.SupporterAvatarName))
            {
                var resolvedName = ResolveVrChatAvatarName(rule.SupporterAvatarId);
                if (!string.IsNullOrWhiteSpace(resolvedName))
                {
                    rule.SupporterAvatarName = resolvedName;
                    changed = true;
                }
            }
        }

        RefreshSupporterRuleScopeLabels();
        return changed;
    }

    private bool TryResolveDefaultSupporterAvatar(out string avatarId, out string avatarName)
    {
        avatarId = Settings.VrChat.CurrentAvatarId?.Trim() ?? string.Empty;
        avatarName = string.IsNullOrWhiteSpace(avatarId) ? string.Empty : ResolveVrChatAvatarName(avatarId);
        if (!string.IsNullOrWhiteSpace(avatarId))
        {
            return true;
        }

        var firstAvatar = BuildAllSelectableVrChatAvatarOptions().FirstOrDefault(option => !string.IsNullOrWhiteSpace(option.Id));
        if (firstAvatar is null)
        {
            return false;
        }

        avatarId = firstAvatar.Id.Trim();
        avatarName = firstAvatar.Name?.Trim() ?? string.Empty;
        return true;
    }

    private void NormalizeAvatarProfileRules()
    {
        foreach (var profile in AvatarRuleProfiles)
        {
            ApplyAvatarProfileDefaults(profile);
        }
    }

    private static void ApplyAvatarProfileDefaults(AvatarTriggerProfile? profile)
    {
        if (profile is null || profile.IsMasterProfile)
        {
            return;
        }

        foreach (var rule in profile.ChannelPointRules)
        {
            ApplyAvatarProfileDefaultsToRule(rule);
        }

        ApplySetTriggerMasterRewardDefaults(profile);
        if (!profile.UseSharedNumberedOutfitReward)
        {
            EnsureSeparateOutfitRewardTitles(profile);
        }
    }

    private static void ApplyAvatarProfileDefaultsToRule(TriggerRule rule)
    {
        if (rule.TriggerType != TwitchTriggerType.ChannelPoints)
        {
            rule.TriggerType = TwitchTriggerType.ChannelPoints;
        }

        if (rule.ActionType is not (OscActionType.AvatarParameter or OscActionType.SetTrigger))
        {
            rule.ActionType = OscActionType.AvatarParameter;
        }

        if (rule.ActionType == OscActionType.SetTrigger)
        {
            rule.SharedRewardChoiceEnabled = true;
            if (rule.SharedRewardChoiceNumber <= 0)
            {
                rule.SharedRewardChoiceNumber = 1;
            }

            if (rule.DurationSeconds <= 0)
            {
                rule.DurationSeconds = 3;
            }
        }
    }

    private static void ApplySetTriggerMasterRewardDefaults(AvatarTriggerProfile profile)
    {
        var migratedRule = profile.ChannelPointRules
            .Where(rule => rule.ActionType == OscActionType.SetTrigger)
            .OrderBy(rule => Math.Max(1, rule.SharedRewardChoiceNumber))
            .ThenBy(rule => rule.DisplayTitle, StringComparer.CurrentCultureIgnoreCase)
            .FirstOrDefault(rule =>
                !string.IsNullOrWhiteSpace(rule.ChannelPointRewardId)
                || !string.IsNullOrWhiteSpace(rule.ChannelPointRewardTitle));
        if (migratedRule is null)
        {
            return;
        }

        if (string.IsNullOrWhiteSpace(profile.SetTriggerMasterRewardId)
            && !string.IsNullOrWhiteSpace(migratedRule.ChannelPointRewardId))
        {
            profile.SetTriggerMasterRewardId = migratedRule.ChannelPointRewardId.Trim();
        }

        if (string.IsNullOrWhiteSpace(profile.SetTriggerMasterRewardTitle)
            && !string.IsNullOrWhiteSpace(migratedRule.ChannelPointRewardTitle))
        {
            profile.SetTriggerMasterRewardTitle = migratedRule.ChannelPointRewardTitle.Trim();
            profile.SetTriggerMasterRewardDescription = migratedRule.ChannelPointRewardDescription;
            profile.SetTriggerMasterRewardCost = migratedRule.ChannelPointRewardCost;
            profile.SetTriggerMasterRewardCooldownSeconds = migratedRule.CooldownSeconds;
            profile.SetTriggerMasterRewardReadyColor = migratedRule.ManagedRewardReadyColor;
            profile.SetTriggerMasterRewardCooldownColor = migratedRule.ManagedRewardCooldownColor;
            profile.DeleteSetTriggerMasterRewardWhenInactive = migratedRule.DeleteManagedRewardWhenInactive;
        }
    }

    private static void EnsureSeparateOutfitRewardTitles(AvatarTriggerProfile profile)
    {
        foreach (var rule in profile.ChannelPointRules.Where(rule => rule.ActionType == OscActionType.SetTrigger))
        {
            if (!string.IsNullOrWhiteSpace(rule.ChannelPointRewardTitle))
            {
                continue;
            }

            var label = !string.IsNullOrWhiteSpace(rule.SharedRewardHelpText)
                ? rule.SharedRewardHelpText.Trim()
                : !string.IsNullOrWhiteSpace(rule.Name)
                    ? rule.Name.Trim()
                    : $"Outfit {Math.Max(1, rule.SharedRewardChoiceNumber)}";
            rule.ChannelPointRewardTitle = label;
        }
    }

    private void ApplyMasterAvatarDefaults(AvatarTriggerProfile? masterProfile = null)
    {
        var profile = masterProfile ?? MasterAvatarProfile;
        if (profile is null || !profile.IsMasterProfile)
        {
            return;
        }

        foreach (var rule in profile.ChannelPointRules)
        {
            ApplyMasterAvatarDefaultsToRule(rule, profile);
        }
    }

    private void ApplyMasterAvatarDefaultsToRule(TriggerRule rule, AvatarTriggerProfile? masterProfile = null)
    {
        var profile = masterProfile ?? MasterAvatarProfile;
        if (profile is null || !profile.IsMasterProfile)
        {
            return;
        }

        var wasApplyingDefaults = isApplyingMasterAvatarDefaults;
        if (!wasApplyingDefaults)
        {
            isApplyingMasterAvatarDefaults = true;
        }

        try
        {
            if (rule.TriggerType != TwitchTriggerType.ChannelPoints)
            {
                rule.TriggerType = TwitchTriggerType.ChannelPoints;
            }

            if (rule.ActionType is not OscActionType.AvatarChange and not OscActionType.AvatarRoulet)
            {
                rule.ActionType = OscActionType.AvatarChange;
            }

            var masterAvatarId = profile.AvatarId?.Trim() ?? string.Empty;
            var masterAvatarName = string.IsNullOrWhiteSpace(profile.AvatarName)
                ? ResolveVrChatAvatarName(masterAvatarId)
                : profile.AvatarName;

            rule.AvatarChangeResetId = masterAvatarId;
            rule.ResetAvatarName = masterAvatarName;
        }
        finally
        {
            if (!wasApplyingDefaults)
            {
                isApplyingMasterAvatarDefaults = false;
            }
        }
    }

    private static void ApplyMovementRuleDefaults(TriggerRule rule)
    {
        if (rule.TriggerType != TwitchTriggerType.ChannelPoints)
        {
            rule.TriggerType = TwitchTriggerType.ChannelPoints;
        }

        if (rule.ActionType != OscActionType.PlayerMovement)
        {
            rule.ActionType = OscActionType.PlayerMovement;
        }

        if (rule.DurationSeconds <= 0)
        {
            rule.DurationSeconds = 3;
        }
    }

    private void RelocateMisplacedMovementRules()
    {
        var movedRuleNames = new List<string>();

        foreach (var profile in Settings.AvatarProfiles)
        {
            var misplacedRules = profile.ChannelPointRules
                .Where(rule => rule.ActionType == OscActionType.PlayerMovement)
                .ToArray();

            foreach (var rule in misplacedRules)
            {
                profile.ChannelPointRules.Remove(rule);
                AddRuleToSelectedOrDefaultMovementSet(rule);

                movedRuleNames.Add(rule.DisplayTitle);
            }
        }

        var misplacedOverrideRules = Settings.GlobalOverrideRules
            .Where(rule => rule.ActionType == OscActionType.PlayerMovement)
            .Where(rule => !IsSupporterForceMovementOverride(rule))
            .ToArray();

        foreach (var rule in misplacedOverrideRules)
        {
            Settings.GlobalOverrideRules.Remove(rule);
            AddRuleToSelectedOrDefaultMovementSet(rule);

            movedRuleNames.Add(rule.DisplayTitle);
        }

        if (movedRuleNames.Count == 0)
        {
            return;
        }

        if (SelectedRule is not null && GetOwningMovementRedeemSet(SelectedRule) is not null)
        {
            SwitchRuleView(RuleListView.MovementRedeems, profile: null, SelectedRule);
        }

        if (isInitialized)
        {
            QueueSave(0);
            QueueBridgeRefresh();
            QueueManagedRewardSync(0);
            AppendLog(TF("Moved {0} movement redeem(s) into Movement Redeems so avatar and override rules stay cleaner.", movedRuleNames.Count));
        }
    }

    private void RelocateRuleToGlobalMovementRules(TriggerRule rule)
    {
        if (GetOwningMovementRedeemSet(rule) is not null)
        {
            ApplyMovementRuleDefaults(rule);
            return;
        }

        var owningProfile = Settings.AvatarProfiles.FirstOrDefault(profile => profile.ChannelPointRules.Contains(rule));
        if (owningProfile is not null)
        {
            ForgetRememberedRule(rule);
            owningProfile.ChannelPointRules.Remove(rule);
        }
        else if (Settings.GlobalOverrideRules.Contains(rule))
        {
            ForgetRememberedRule(rule);
            Settings.GlobalOverrideRules.Remove(rule);
        }
        else
        {
            return;
        }

        AddRuleToSelectedOrDefaultMovementSet(rule);

        ApplyMovementRuleDefaults(rule);
        SwitchRuleView(RuleListView.MovementRedeems, profile: null, rule);
        AppendLog(TF("Moved '{0}' into Movement Redeems because player movement is global.", rule.DisplayTitle));
    }

    private void AddRuleToSelectedOrDefaultMovementSet(TriggerRule rule)
    {
        if (GetOwningMovementRedeemSet(rule) is not null)
        {
            return;
        }

        EnsureSelectedMovementRedeemSet();
        var targetSet = SelectedMovementRedeemSet ?? Settings.MovementRedeemSets.FirstOrDefault();
        if (targetSet is null)
        {
            targetSet = CreateDefaultMovementRedeemSet();
            Settings.MovementRedeemSets.Add(targetSet);
        }

        targetSet.MovementRules.Add(rule);
    }

    private static string GetFriendlyVrChatError(Exception ex)
    {
        if (ex is VrChatApiException apiException)
        {
            if (apiException.StatusCode == System.Net.HttpStatusCode.Unauthorized)
            {
                return T("VRChat login was not accepted. Double-check the username, password, and 2FA code.");
            }

            if (apiException.ApiMessage.Contains("Missing Credentials", StringComparison.OrdinalIgnoreCase))
            {
                return T("VRChat login was missing credentials. Try connecting again.");
            }
        }

        if (ex is InvalidOperationException invalidOperationException)
        {
            return invalidOperationException.Message;
        }

        return TF("VRChat avatar access failed: {0}", ex.Message);
    }

    private void ShowSettingsTestPopup()
    {
        PlaySettingsTestAudio();
        ThemedDialogWindow.ShowOk(
            Application.Current?.MainWindow,
            SelectedTheme,
            T("Crystal Relay"),
            T("What do you think that button is for? free food? NOPE!"),
            T("OK"));
    }

    private static void PlaySettingsTestAudio()
    {
        var audioPath = EmbeddedMediaCacheService.ExtractEmbeddedMediaToTempFile(SettingsTestAudioRelativePath);
        if (string.IsNullOrWhiteSpace(audioPath) || !File.Exists(audioPath))
        {
            return;
        }

        try
        {
            settingsTestAudioPlayer?.Stop();
            settingsTestAudioPlayer?.Close();
            EmbeddedMediaCacheService.DeleteTemporaryMediaFile(settingsTestAudioTempPath);

            var player = new MediaPlayer();
            settingsTestAudioPlayer = player;
            settingsTestAudioTempPath = audioPath;
            player.MediaEnded += (_, _) => CleanupSettingsTestAudioPlayer(player);
            player.MediaFailed += (_, _) => CleanupSettingsTestAudioPlayer(player);
            player.Open(new Uri(audioPath, UriKind.Absolute));
            player.Volume = 1.0;
            player.Play();
        }
        catch
        {
            EmbeddedMediaCacheService.DeleteTemporaryMediaFile(audioPath);
            settingsTestAudioTempPath = null;
            settingsTestAudioPlayer = null;
        }
    }

    private static void CleanupSettingsTestAudioPlayer(MediaPlayer player)
    {
        if (!ReferenceEquals(settingsTestAudioPlayer, player))
        {
            player.Close();
            return;
        }

        player.Stop();
        player.Close();
        EmbeddedMediaCacheService.DeleteTemporaryMediaFile(settingsTestAudioTempPath);
        settingsTestAudioTempPath = null;
        settingsTestAudioPlayer = null;
    }

    private static string GetAppVersion()
    {
        var entryAssembly = Assembly.GetEntryAssembly();
        var informationalVersion = entryAssembly?
            .GetCustomAttribute<AssemblyInformationalVersionAttribute>()?
            .InformationalVersion;

        if (!string.IsNullOrWhiteSpace(informationalVersion))
        {
            var plusIndex = informationalVersion.IndexOf('+');
            return plusIndex >= 0
                ? informationalVersion[..plusIndex]
                : informationalVersion;
        }

        var version = entryAssembly?.GetName().Version;
        if (version is not null)
        {
            return $"{version.Major}.{version.Minor}.{version.Build}";
        }

        return "0.0.0";
    }

    private static string GetAppVersionDisplay()
    {
        var builder = new StringBuilder(
            AppBuildIdentity.HasBugFixLabel
                ? AppBuildIdentity.UpdateVersion
                : AppVersion);
        if (AppBuildIdentity.HasBetaLabel)
        {
            builder.Append(" - ");
            builder.Append(AppBuildIdentity.DisplayLabel);
        }

        if (IsTestBuild)
        {
            builder.Append(LocalizationService.Translate(" - Test Build"));
        }

#if DEBUG
        builder.Append(" - DEBUG");
#endif

        return builder.ToString();
    }

    private enum SectionView
    {
        Home,
        Settings,
        Activity,
        About
    }

    private enum SettingsSectionView
    {
        Twitch,
        VrChat,
        App,
        Visuals,
        Safety
    }

    private enum RuleListView
    {
        AvatarTriggers,
        MasterAvatar,
        MovementRedeems,
        PowerUps,
        UniversalTriggers,
        AvatarScaling,
        Wardrobe
    }

    private sealed record ChatModerationTarget(string DisplayName, string Login, string UserId);

    private sealed record LinkedTwitchRewardReference(string RewardId, string DisplayTitle);

    private sealed record LinkedPowerUpReference(string PowerUpId, string DisplayTitle);
}

public sealed record TwitchRewardOption(
    string Id,
    string Title,
    int Cost,
    bool IsEnabled,
    string Prompt,
    string BackgroundColor,
    int CooldownSeconds,
    bool IsUserInputRequired,
    bool IsCatalogMissing = false)
{
    public static TwitchRewardOption Placeholder(string title) =>
        new(string.Empty, title, 0, false, string.Empty, string.Empty, 0, false);

    public static TwitchRewardOption MissingLinked(string id, string title)
    {
        var displayTitle = string.IsNullOrWhiteSpace(title)
            ? LocalizationService.Translate("Linked reward not loaded")
            : LocalizationService.Format("{0} (not loaded)", title.Trim());
        return new TwitchRewardOption(id, displayTitle, 0, false, string.Empty, string.Empty, 0, false, true);
    }

    public static TwitchRewardOption FromReward(TwitchApiClient.CustomRewardResponse reward)
    {
        var cooldownSeconds = reward.IsGlobalCooldownEnabled
            ? Math.Max(0, reward.GlobalCooldownSeconds ?? 0)
            : 0;
        return new TwitchRewardOption(
            reward.Id,
            reward.Title,
            reward.Cost,
            reward.IsEnabled,
            reward.Prompt,
            reward.BackgroundColor,
            cooldownSeconds,
            reward.IsUserInputRequired);
    }

    public string DisplayLabel => string.IsNullOrWhiteSpace(Id)
        ? Title
        : IsCatalogMissing
            ? Title
        : $"{Title} ({Cost} pts, {StatusText})";

    public string DetailText
    {
        get
        {
            if (string.IsNullOrWhiteSpace(Id))
            {
                return string.Empty;
            }

            if (IsCatalogMissing)
            {
                return LocalizationService.Format(
                    "Saved Twitch reward ID: {0}. Refresh rewards or reconnect the broadcaster account.",
                    Id);
            }

            var cooldownText = CooldownSeconds > 0
                ? LocalizationService.Format("{0}s cooldown", CooldownSeconds)
                : LocalizationService.Translate("No cooldown");
            var inputText = IsUserInputRequired
                ? LocalizationService.Translate("Input required")
                : LocalizationService.Translate("No input required");
            return $"{StatusText} | {Cost} pts | {cooldownText} | {inputText}";
        }
    }

    public string StatusText => IsEnabled
        ? LocalizationService.Translate("Enabled")
        : LocalizationService.Translate("Disabled");

    public bool HasDetailText => !string.IsNullOrWhiteSpace(DetailText);

    public override string ToString() => DisplayLabel;
}

public sealed record TwitchPowerUpOption(
    string Id,
    string Title,
    int BitsCost,
    bool IsEnabled,
    string Prompt,
    bool IsCatalogMissing = false)
{
    public static TwitchPowerUpOption Placeholder(string title) =>
        new(string.Empty, title, 0, false, string.Empty);

    public static TwitchPowerUpOption MissingLinked(string id, string title)
    {
        var displayTitle = string.IsNullOrWhiteSpace(title)
            ? LocalizationService.Translate("Linked Power Up not loaded")
            : LocalizationService.Format("{0} (not loaded)", title.Trim());
        return new TwitchPowerUpOption(id, displayTitle, 0, false, string.Empty, true);
    }

    public static TwitchPowerUpOption FromPowerUp(TwitchApiClient.CustomPowerUpResponse powerUp)
    {
        return new TwitchPowerUpOption(
            powerUp.Id,
            powerUp.Title,
            powerUp.EffectiveBitsCost,
            powerUp.IsEnabled,
            powerUp.Prompt);
    }

    public string DisplayLabel => string.IsNullOrWhiteSpace(Id)
        ? Title
        : IsCatalogMissing
            ? Title
            : $"{Title} ({BitsCost} Bits, {StatusText})";

    public string DetailText
    {
        get
        {
            if (string.IsNullOrWhiteSpace(Id))
            {
                return string.Empty;
            }

            if (IsCatalogMissing)
            {
                return LocalizationService.Format(
                    "Saved Twitch Custom Power-up ID: {0}. Refresh Power Ups or reconnect the broadcaster account.",
                    Id);
            }

            var promptText = string.IsNullOrWhiteSpace(Prompt)
                ? LocalizationService.Translate("No prompt")
                : Prompt.Trim();
            return $"{StatusText} | {BitsCost} Bits | {promptText}";
        }
    }

    public string StatusText => IsEnabled
        ? LocalizationService.Translate("Enabled")
        : LocalizationService.Translate("Disabled");

    public bool HasDetailText => !string.IsNullOrWhiteSpace(DetailText);

    public override string ToString() => DisplayLabel;
}

public sealed record TwitchRewardSyncModeOption(TwitchRewardSyncMode Value, string Label)
{
    public override string ToString() => Label;
}

public sealed record PowerUpActionKindOption(PowerUpActionKind Value, string Label)
{
    public override string ToString() => Label;
}

public sealed class TwitchChatActivityEntry
{
    public TwitchChatActivityEntry(BridgeChatActivity activity)
    {
        Kind = activity.Kind;
        MessageText = activity.MessageText?.Trim() ?? string.Empty;
        TargetUserDisplayName = activity.TargetUserDisplayName?.Trim() ?? string.Empty;
        TargetUserLogin = activity.TargetUserLogin?.Trim() ?? string.Empty;
        TargetUserId = activity.TargetUserId?.Trim() ?? string.Empty;
        MessageId = activity.MessageId?.Trim() ?? string.Empty;
        SuspiciousStatus = activity.SuspiciousStatus?.Trim() ?? string.Empty;
        ReceivedAt = activity.ReceivedAt;
    }

    public BridgeChatActivityKind Kind { get; }

    public string MessageText { get; }

    public string TargetUserDisplayName { get; }

    public string TargetUserLogin { get; }

    public string TargetUserId { get; }

    public string MessageId { get; }

    public string SuspiciousStatus { get; }

    public DateTimeOffset ReceivedAt { get; }

    public string TimestampText => ReceivedAt.ToLocalTime().ToString("HH:mm:ss", CultureInfo.CurrentCulture);

    public string KindLabel => Kind switch
    {
        BridgeChatActivityKind.ChannelPointRedemption => LocalizationService.Translate("Reward"),
        BridgeChatActivityKind.SupportEvent => LocalizationService.Translate("Support"),
        BridgeChatActivityKind.Follow => LocalizationService.Translate("Follow"),
        BridgeChatActivityKind.MessageDeleted => LocalizationService.Translate("Deleted"),
        BridgeChatActivityKind.UserMessagesCleared => LocalizationService.Translate("Purged"),
        BridgeChatActivityKind.ChatCleared => LocalizationService.Translate("Cleared"),
        BridgeChatActivityKind.Timeout => LocalizationService.Translate("Timeout"),
        BridgeChatActivityKind.Ban => LocalizationService.Translate("Ban"),
        BridgeChatActivityKind.MessagePurged => LocalizationService.Translate("Purge"),
        BridgeChatActivityKind.SuspiciousUserUpdated => LocalizationService.Translate("Suspicious"),
        BridgeChatActivityKind.SuspiciousUserMessage => LocalizationService.Translate("Suspicious"),
        BridgeChatActivityKind.ModerationFailure => LocalizationService.Translate("Failed"),
        _ => LocalizationService.Translate("Activity")
    };

    public bool HasTargetUser => !string.IsNullOrWhiteSpace(TargetUserDisplayName)
        || !string.IsNullOrWhiteSpace(TargetUserLogin);

    public string TargetUserText => string.IsNullOrWhiteSpace(TargetUserLogin)
        ? TargetUserDisplayName
        : string.Format(CultureInfo.CurrentCulture, LocalizationService.Translate("@{0}"), TargetUserLogin);
}

public sealed record RewardFireSaleModeOption(RewardFireSaleMode Value, string Label)
{
    public override string ToString() => Label;
}

public sealed class AboutTwitchProfile : ObservableObject
{
    private static readonly string[] TrustedProfileImageHostSuffixes =
    [
        ".jtvnw.net",
        ".twitchcdn.net"
    ];
    private string displayName;
    private string profileImageUrl = string.Empty;
    private bool isLive;

    public AboutTwitchProfile(string displayName, string twitchLogin, string twitchUrl)
    {
        this.displayName = displayName;
        TwitchLogin = twitchLogin;
        TwitchUrl = twitchUrl;
    }

    public string TwitchLogin { get; }

    public string TwitchUrl { get; }

    public Uri TwitchUri => new(TwitchUrl);

    public Uri? ProfileImageUri => TryCreateTrustedProfileImageUri(ProfileImageUrl);

    public string DisplayName
    {
        get => displayName;
        set
        {
            if (SetProperty(ref displayName, value))
            {
                RaisePropertyChanged(nameof(Initial));
            }
        }
    }

    public string ProfileImageUrl
    {
        get => profileImageUrl;
        set
        {
            var normalizedValue = NormalizeTrustedProfileImageUrl(value);
            if (SetProperty(ref profileImageUrl, normalizedValue))
            {
                RaisePropertyChanged(nameof(HasProfileImage));
                RaisePropertyChanged(nameof(ProfileImageUri));
            }
        }
    }

    public bool HasProfileImage => ProfileImageUri is not null;

    public bool IsLive
    {
        get => isLive;
        set => SetProperty(ref isLive, value);
    }

    public string Initial => string.IsNullOrWhiteSpace(DisplayName)
        ? "?"
        : DisplayName.Trim()[0].ToString().ToUpperInvariant();

    private static string NormalizeTrustedProfileImageUrl(string? value)
    {
        var uri = TryCreateTrustedProfileImageUri(value);
        return uri?.AbsoluteUri ?? string.Empty;
    }

    private static Uri? TryCreateTrustedProfileImageUri(string? value)
    {
        if (string.IsNullOrWhiteSpace(value)
            || !Uri.TryCreate(value.Trim(), UriKind.Absolute, out var uri))
        {
            return null;
        }

        if (!string.Equals(uri.Scheme, Uri.UriSchemeHttps, StringComparison.OrdinalIgnoreCase)
            || !string.IsNullOrWhiteSpace(uri.UserInfo))
        {
            return null;
        }

        var host = uri.Host.Trim();
        if (string.IsNullOrWhiteSpace(host))
        {
            return null;
        }

        var trustedHost = TrustedProfileImageHostSuffixes.Any(suffix =>
            host.EndsWith(suffix, StringComparison.OrdinalIgnoreCase));
        if (!trustedHost)
        {
            return null;
        }

        return uri;
    }
}

public sealed record VrChatAvatarOption(
    string Id,
    string Name,
    string DisplayLabel,
    string SearchText,
    string SecondaryHint,
    bool IsFallback)
{
    public bool HasSecondaryHint => !string.IsNullOrWhiteSpace(SecondaryHint);

    public override string ToString() => DisplayLabel;
}

public enum TwitchChatMessageEntryKind
{
    Chat,
    ChannelPointRedemption,
    BitsCheer,
    Subscription,
    Resubscription,
    GiftSubscription,
    Raid
}

public enum TwitchChatRoleCardKind
{
    None,
    KaiBloodwolf,
    Hypercraftiing,
    KyouZakira,
    Phil13938,
    Falx,
    Staff,
    LeadModerator,
    Moderator,
    Vip,
    Artist,
    TierThree,
    TierTwo,
    TierOne,
    Subscriber
}

public sealed class TwitchChatMessageEntry : ObservableObject
{
    private const string CrystalRelayDeveloperLogin = "Screminpal_";
    private const string KaiBloodwolfLogin = "kai_bloodwolf";
    private const string HypercraftiingLogin = "hypercraftiing";
    private const string KyouZakiraLogin = "kyou_zakira";
    private const string Phil13938Login = "phil13938";
    private const string FalxPlaysLogin = "falxplays";
    private static readonly SolidColorBrush DefaultNameBrush = CreateFrozenBrush("#F5EEFF");
    private static readonly SolidColorBrush BubblegumNameBrush = CreateFrozenBrush("#5A426B");
    private static readonly LinearGradientBrush CrystalRelayDeveloperNameBrush = CreateFrozenDeveloperNameBrush();
    private static readonly LinearGradientBrush KaiBloodwolfNameBrush = CreateFrozenKaiBloodwolfNameBrush();
    private static readonly LinearGradientBrush HypercraftiingNameBrush = CreateFrozenHypercraftiingNameBrush();
    private static readonly LinearGradientBrush KyouZakiraNameBrush = CreateFrozenKyouZakiraNameBrush();
    private static readonly LinearGradientBrush Phil13938NameBrush = CreateFrozenPhil13938NameBrush();
    private static readonly LinearGradientBrush FalxPlaysNameBrush = CreateFrozenFalxPlaysNameBrush();
    private static readonly Color DarkCardReferenceColor = Color.FromRgb(40, 23, 60);
    private ChatTimestampFormat timestampFormat;
    private string suspiciousStatus = string.Empty;

    public TwitchChatMessageEntry(
        string userDisplayName,
        string userLogin,
        string userId,
        string messageText,
        string userColor,
        IReadOnlyList<string> badgeImageUrls,
        IReadOnlyList<string> badgeSetIds,
        IReadOnlyList<TwitchChatInlineFragment> inlineFragments,
        bool shouldPlayViewerSound,
        DateTimeOffset receivedAt,
        AppTheme theme,
        ChatTimestampFormat timestampFormat,
        TwitchChatMessageEntryKind kind = TwitchChatMessageEntryKind.Chat,
        string rewardTitle = "",
        int rewardCost = 0,
        string rewardUserInput = "",
        int supportAmount = 0,
        string supportTier = "",
        int supportMonths = 0,
        string supportMessage = "",
        string messageId = "",
        string messageType = "",
        string sourceBroadcasterUserId = "",
        string sourceBroadcasterUserLogin = "",
        string sourceBroadcasterUserName = "",
        string sourceMessageId = "",
        bool isSourceOnly = false)
    {
        var normalizedSupportTier = supportTier?.Trim() ?? string.Empty;

        Kind = Enum.IsDefined(kind) ? kind : TwitchChatMessageEntryKind.Chat;
        UserDisplayName = string.IsNullOrWhiteSpace(userDisplayName) ? "Viewer" : userDisplayName.Trim();
        UserLogin = userLogin?.Trim() ?? string.Empty;
        UserId = userId?.Trim() ?? string.Empty;
        IsCrystalRelayDeveloper = IsCrystalRelayDeveloperAccount(UserDisplayName, UserLogin);
        IsKaiBloodwolf = IsKaiBloodwolfAccount(UserDisplayName, UserLogin);
        IsHypercraftiing = IsHypercraftiingAccount(UserDisplayName, UserLogin);
        IsKyouZakira = IsKyouZakiraAccount(UserDisplayName, UserLogin);
        IsPhil13938 = IsPhil13938Account(UserDisplayName, UserLogin);
        IsFalxPlays = IsFalxPlaysAccount(UserDisplayName, UserLogin);
        MessageText = messageText;
        BadgeImageUrls = badgeImageUrls;
        BadgeSetIds = badgeSetIds;
        RoleCardKind = IsCrystalRelayDeveloper
            ? TwitchChatRoleCardKind.None
            : IsKaiBloodwolf
            ? TwitchChatRoleCardKind.KaiBloodwolf
            : IsHypercraftiing
            ? TwitchChatRoleCardKind.Hypercraftiing
            : IsKyouZakira
            ? TwitchChatRoleCardKind.KyouZakira
            : IsPhil13938
            ? TwitchChatRoleCardKind.Phil13938
            : IsFalxPlays
            ? TwitchChatRoleCardKind.Falx
            : ResolveRoleCardKind(Kind, normalizedSupportTier, BadgeSetIds);
        InlineFragments = inlineFragments.Count == 0
            ? [new TwitchChatInlineFragment(TwitchChatInlineFragmentKind.Text, messageText, string.Empty)]
            : inlineFragments;
        ShouldPlayViewerSound = shouldPlayViewerSound;
        ReceivedAt = receivedAt;
        RawUserColor = userColor;
        NameBrush = IsCrystalRelayDeveloper
            ? CrystalRelayDeveloperNameBrush
            : IsKaiBloodwolf
            ? KaiBloodwolfNameBrush
            : IsHypercraftiing
            ? HypercraftiingNameBrush
            : IsKyouZakira
            ? KyouZakiraNameBrush
            : IsPhil13938
            ? Phil13938NameBrush
            : IsFalxPlays
            ? FalxPlaysNameBrush
            : ParseNameBrush(userColor, theme);
        RewardTitle = string.IsNullOrWhiteSpace(rewardTitle) ? MessageText.Trim() : rewardTitle.Trim();
        RewardCost = Math.Max(0, rewardCost);
        RewardUserInput = rewardUserInput?.Trim() ?? string.Empty;
        SupportAmount = Math.Max(0, supportAmount);
        SupportTier = normalizedSupportTier;
        SupportMonths = Math.Max(0, supportMonths);
        SupportMessage = supportMessage?.Trim() ?? string.Empty;
        MessageId = messageId?.Trim() ?? string.Empty;
        MessageType = messageType?.Trim() ?? string.Empty;
        SourceBroadcasterUserId = sourceBroadcasterUserId?.Trim() ?? string.Empty;
        SourceBroadcasterUserLogin = sourceBroadcasterUserLogin?.Trim() ?? string.Empty;
        SourceBroadcasterUserName = sourceBroadcasterUserName?.Trim() ?? string.Empty;
        SourceMessageId = sourceMessageId?.Trim() ?? string.Empty;
        IsSourceOnly = isSourceOnly;
        this.timestampFormat = NormalizeTimestampFormat(timestampFormat);
    }

    public TwitchChatMessageEntryKind Kind { get; }

    public bool IsChatMessage => Kind == TwitchChatMessageEntryKind.Chat;

    public bool IsChannelPointRedemption => Kind == TwitchChatMessageEntryKind.ChannelPointRedemption;

    public bool IsSupportEvent => Kind is TwitchChatMessageEntryKind.BitsCheer
        or TwitchChatMessageEntryKind.Subscription
        or TwitchChatMessageEntryKind.Resubscription
        or TwitchChatMessageEntryKind.GiftSubscription
        or TwitchChatMessageEntryKind.Raid;

    public string UserDisplayName { get; }

    public string UserLogin { get; }

    public string UserId { get; }

    public bool IsCrystalRelayDeveloper { get; }

    public bool IsKaiBloodwolf { get; }

    public bool IsHypercraftiing { get; }

    public bool IsKyouZakira { get; }

    public bool IsPhil13938 { get; }

    public bool IsFalxPlays { get; }

    public string MessageText { get; }

    public IReadOnlyList<string> BadgeImageUrls { get; }

    public IReadOnlyList<string> BadgeSetIds { get; }

    public TwitchChatRoleCardKind RoleCardKind { get; }

    public bool HasBadgeRoleCard => RoleCardKind != TwitchChatRoleCardKind.None;

    public bool IsKaiBloodwolfRoleCard => RoleCardKind == TwitchChatRoleCardKind.KaiBloodwolf;

    public bool IsHypercraftiingRoleCard => RoleCardKind == TwitchChatRoleCardKind.Hypercraftiing;

    public bool IsKyouZakiraRoleCard => RoleCardKind == TwitchChatRoleCardKind.KyouZakira;

    public bool IsPhil13938RoleCard => RoleCardKind == TwitchChatRoleCardKind.Phil13938;

    public bool IsFalxPlaysRoleCard => RoleCardKind == TwitchChatRoleCardKind.Falx;

    public bool IsTwitchStaffRoleCard => RoleCardKind == TwitchChatRoleCardKind.Staff;

    public bool IsLeadModeratorRoleCard => RoleCardKind == TwitchChatRoleCardKind.LeadModerator;

    public bool IsModeratorRoleCard => RoleCardKind == TwitchChatRoleCardKind.Moderator;

    public bool IsVipRoleCard => RoleCardKind == TwitchChatRoleCardKind.Vip;

    public bool IsArtistRoleCard => RoleCardKind == TwitchChatRoleCardKind.Artist;

    public bool IsTierThreeRoleCard => RoleCardKind == TwitchChatRoleCardKind.TierThree;

    public bool IsTierTwoRoleCard => RoleCardKind == TwitchChatRoleCardKind.TierTwo;

    public bool IsTierOneRoleCard => RoleCardKind == TwitchChatRoleCardKind.TierOne;

    public bool IsSubscriberRoleCard => RoleCardKind == TwitchChatRoleCardKind.Subscriber;

    public string RoleCardLabel => RoleCardKind switch
    {
        TwitchChatRoleCardKind.KaiBloodwolf => "KFC/popeyes chugger",
        TwitchChatRoleCardKind.Hypercraftiing => "The Great Cuddly Synth",
        TwitchChatRoleCardKind.KyouZakira => "Chatoic Umbreon",
        TwitchChatRoleCardKind.Phil13938 => "The Canadian Bnuy",
        TwitchChatRoleCardKind.Falx => "Awooey",
        TwitchChatRoleCardKind.Staff => "TWITCH STAFF",
        TwitchChatRoleCardKind.LeadModerator => "LEAD MOD",
        TwitchChatRoleCardKind.Moderator => "MOD",
        TwitchChatRoleCardKind.Vip => "VIP",
        TwitchChatRoleCardKind.Artist => "ARTIST",
        TwitchChatRoleCardKind.TierThree => "TIER 3",
        TwitchChatRoleCardKind.TierTwo => "TIER 2",
        TwitchChatRoleCardKind.TierOne => "TIER 1",
        TwitchChatRoleCardKind.Subscriber => "SUBSCRIBER",
        _ => string.Empty
    };

    public IReadOnlyList<TwitchChatInlineFragment> InlineFragments { get; }

    public bool ShouldPlayViewerSound { get; }

    public DateTimeOffset ReceivedAt { get; }

    public string RawUserColor { get; }

    public string TimestampText => FormatTimestamp(ReceivedAt, timestampFormat);

    public Brush NameBrush { get; }

    public string RewardTitle { get; }

    public int RewardCost { get; }

    public string RewardUserInput { get; }

    public bool HasRewardCost => RewardCost > 0;

    public bool HasRewardUserInput => !string.IsNullOrWhiteSpace(RewardUserInput);

    public int SupportAmount { get; }

    public string SupportTier { get; }

    public int SupportMonths { get; }

    public string SupportMessage { get; }

    public string MessageId { get; }

    public string MessageType { get; }

    public string SourceBroadcasterUserId { get; }

    public string SourceBroadcasterUserLogin { get; }

    public string SourceBroadcasterUserName { get; }

    public string SourceMessageId { get; }

    public bool IsSourceOnly { get; }

    public string SuspiciousStatus
    {
        get => suspiciousStatus;
        set
        {
            var normalized = NormalizeSuspiciousStatus(value);
            if (SetProperty(ref suspiciousStatus, normalized))
            {
                RaisePropertyChanged(nameof(HasSuspiciousStatus));
                RaisePropertyChanged(nameof(IsSuspiciousMonitored));
                RaisePropertyChanged(nameof(IsRestrictedChatter));
                RaisePropertyChanged(nameof(SuspiciousStatusLabel));
            }
        }
    }

    public bool HasSuspiciousStatus => !string.IsNullOrWhiteSpace(SuspiciousStatus)
        && !string.Equals(SuspiciousStatus, "NO_TREATMENT", StringComparison.OrdinalIgnoreCase);

    public bool IsSuspiciousMonitored => string.Equals(SuspiciousStatus, "ACTIVE_MONITORING", StringComparison.OrdinalIgnoreCase);

    public bool IsRestrictedChatter => string.Equals(SuspiciousStatus, "RESTRICTED", StringComparison.OrdinalIgnoreCase);

    public string SuspiciousStatusLabel => SuspiciousStatus switch
    {
        "ACTIVE_MONITORING" => LocalizationService.Translate("SUSPICIOUS"),
        "RESTRICTED" => LocalizationService.Translate("RESTRICTED"),
        _ => string.Empty
    };

    public bool HasSupportDetailText => !string.IsNullOrWhiteSpace(SupportDetailText);

    public bool HasSupportMessage => !string.IsNullOrWhiteSpace(SupportMessage);

    public string SupportEventLabel => Kind switch
    {
        TwitchChatMessageEntryKind.BitsCheer => LocalizationService.Translate("Bits Cheer"),
        TwitchChatMessageEntryKind.Subscription => LocalizationService.Translate("New Sub"),
        TwitchChatMessageEntryKind.Resubscription => LocalizationService.Translate("Resub"),
        TwitchChatMessageEntryKind.GiftSubscription => LocalizationService.Translate("Gift Subs"),
        TwitchChatMessageEntryKind.Raid => LocalizationService.Translate("Raid"),
        _ => string.Empty
    };

    public string SupportHeadlineText => Kind switch
    {
        TwitchChatMessageEntryKind.BitsCheer => string.Format(
            CultureInfo.CurrentCulture,
            LocalizationService.Translate("{0} cheered {1:N0} Bits"),
            UserDisplayName,
            SupportAmount),
        TwitchChatMessageEntryKind.Subscription => string.Format(
            CultureInfo.CurrentCulture,
            LocalizationService.Translate("{0} subscribed"),
            UserDisplayName),
        TwitchChatMessageEntryKind.Resubscription => string.Format(
            CultureInfo.CurrentCulture,
            LocalizationService.Translate("{0} resubbed"),
            UserDisplayName),
        TwitchChatMessageEntryKind.GiftSubscription => string.Format(
            CultureInfo.CurrentCulture,
            LocalizationService.Translate("{0} gifted {1:N0} subs"),
            UserDisplayName,
            SupportAmount),
        TwitchChatMessageEntryKind.Raid => string.Format(
            CultureInfo.CurrentCulture,
            LocalizationService.Translate("{0} raided with {1:N0} viewers"),
            UserDisplayName,
            SupportAmount),
        _ => string.Empty
    };

    public string SupportDetailText => Kind switch
    {
        TwitchChatMessageEntryKind.Subscription => FormatSupportTierLabel(SupportTier),
        TwitchChatMessageEntryKind.Resubscription => FormatSupportTierAndMonths(SupportTier, SupportMonths),
        TwitchChatMessageEntryKind.GiftSubscription => FormatSupportTierLabel(SupportTier),
        _ => string.Empty
    };

    public string RedemptionViewerText => string.Format(
        CultureInfo.CurrentCulture,
        LocalizationService.Translate("Redeemed by {0}"),
        UserDisplayName);

    public string RedemptionCostText => RewardCost > 0
        ? string.Format(
            CultureInfo.CurrentCulture,
            LocalizationService.Translate("{0:N0} points"),
            RewardCost)
        : string.Empty;

    private static string NormalizeSuspiciousStatus(string? value)
    {
        var normalized = value?.Trim() ?? string.Empty;
        return normalized switch
        {
            _ when normalized.Equals("ACTIVE_MONITORING", StringComparison.OrdinalIgnoreCase)
                || normalized.Equals("active_monitoring", StringComparison.OrdinalIgnoreCase) => "ACTIVE_MONITORING",
            _ when normalized.Equals("RESTRICTED", StringComparison.OrdinalIgnoreCase)
                || normalized.Equals("restricted", StringComparison.OrdinalIgnoreCase) => "RESTRICTED",
            _ when normalized.Equals("NO_TREATMENT", StringComparison.OrdinalIgnoreCase)
                || normalized.Equals("no_treatment", StringComparison.OrdinalIgnoreCase) => "NO_TREATMENT",
            _ => normalized.ToUpperInvariant()
        };
    }

    private static string FormatSupportTierAndMonths(string tier, int months)
    {
        var tierLabel = FormatSupportTierLabel(tier);
        if (months <= 0)
        {
            return tierLabel;
        }

        var monthLabel = string.Format(
            CultureInfo.CurrentCulture,
            LocalizationService.Translate("{0:N0} months"),
            months);

        return string.IsNullOrWhiteSpace(tierLabel)
            ? monthLabel
            : string.Format(CultureInfo.CurrentCulture, LocalizationService.Translate("{0} ({1})"), tierLabel, monthLabel);
    }

    private static string FormatSupportTierLabel(string tier)
    {
        return tier.Trim() switch
        {
            "1000" => LocalizationService.Translate("Tier 1"),
            "2000" => LocalizationService.Translate("Tier 2"),
            "3000" => LocalizationService.Translate("Tier 3"),
            _ => string.Empty
        };
    }

    private static TwitchChatRoleCardKind ResolveRoleCardKind(
        TwitchChatMessageEntryKind kind,
        string supportTier,
        IReadOnlyList<string> badgeSetIds)
    {
        if (kind is TwitchChatMessageEntryKind.Subscription
            or TwitchChatMessageEntryKind.Resubscription
            or TwitchChatMessageEntryKind.GiftSubscription)
        {
            var tierRole = ResolveSubscriptionTierRole(supportTier);
            if (tierRole != TwitchChatRoleCardKind.None)
            {
                return tierRole;
            }
        }

        if (ContainsBadgeSetId(badgeSetIds, "staff"))
        {
            return TwitchChatRoleCardKind.Staff;
        }

        if (ContainsBadgeSetId(badgeSetIds, "lead_moderator"))
        {
            return TwitchChatRoleCardKind.LeadModerator;
        }

        if (ContainsBadgeSetId(badgeSetIds, "moderator"))
        {
            return TwitchChatRoleCardKind.Moderator;
        }

        if (ContainsBadgeSetId(badgeSetIds, "vip"))
        {
            return TwitchChatRoleCardKind.Vip;
        }

        if (ContainsBadgeSetId(badgeSetIds, "artist-badge") || ContainsBadgeSetId(badgeSetIds, "artist"))
        {
            return TwitchChatRoleCardKind.Artist;
        }

        if (ContainsBadgeSetId(badgeSetIds, "subscriber") || ContainsBadgeSetId(badgeSetIds, "founder"))
        {
            return TwitchChatRoleCardKind.Subscriber;
        }

        return TwitchChatRoleCardKind.None;
    }

    private static TwitchChatRoleCardKind ResolveSubscriptionTierRole(string tier)
    {
        return tier.Trim() switch
        {
            "3000" => TwitchChatRoleCardKind.TierThree,
            "2000" => TwitchChatRoleCardKind.TierTwo,
            "1000" => TwitchChatRoleCardKind.TierOne,
            _ => TwitchChatRoleCardKind.None
        };
    }

    private static bool ContainsBadgeSetId(IReadOnlyList<string> badgeSetIds, string setId)
    {
        foreach (var badgeSetId in badgeSetIds)
        {
            if (string.Equals(badgeSetId?.Trim(), setId, StringComparison.OrdinalIgnoreCase))
            {
                return true;
            }
        }

        return false;
    }

    public static Brush ResolveNameBrush(string userColor, AppTheme theme) => ParseNameBrush(userColor, theme);

    public void ApplyTimestampFormat(ChatTimestampFormat value)
    {
        var normalized = NormalizeTimestampFormat(value);
        if (timestampFormat == normalized)
        {
            return;
        }

        timestampFormat = normalized;
        RaisePropertyChanged(nameof(TimestampText));
    }

    private static Brush ParseNameBrush(string userColor, AppTheme theme)
    {
        if (string.IsNullOrWhiteSpace(userColor))
        {
            return theme == AppTheme.Bubblegum ? BubblegumNameBrush : DefaultNameBrush;
        }

        try
        {
            var parsedColor = (Color)ColorConverter.ConvertFromString(userColor);
            var readableColor = EnsureReadableNameColor(parsedColor, theme);

            var brush = new SolidColorBrush(readableColor);
            brush.Freeze();
            return brush;
        }
        catch
        {
            return theme == AppTheme.Bubblegum ? BubblegumNameBrush : DefaultNameBrush;
        }
    }

    private static SolidColorBrush CreateFrozenBrush(string colorText)
    {
        var color = (Color)ColorConverter.ConvertFromString(colorText);
        var brush = new SolidColorBrush(color);
        brush.Freeze();
        return brush;
    }

    private static LinearGradientBrush CreateFrozenDeveloperNameBrush()
    {
        var brush = new LinearGradientBrush
        {
            StartPoint = new Point(0, 0.5),
            EndPoint = new Point(1, 0.5)
        };
        brush.GradientStops.Add(new GradientStop(Color.FromRgb(255, 246, 255), 0d));
        brush.GradientStops.Add(new GradientStop(Color.FromRgb(125, 249, 255), 0.45d));
        brush.GradientStops.Add(new GradientStop(Color.FromRgb(255, 127, 229), 1d));
        brush.Freeze();
        return brush;
    }

    private static LinearGradientBrush CreateFrozenKaiBloodwolfNameBrush()
    {
        var brush = new LinearGradientBrush
        {
            StartPoint = new Point(0, 0.5),
            EndPoint = new Point(1, 0.5)
        };
        brush.GradientStops.Add(new GradientStop(Color.FromRgb(255, 255, 255), 0d));
        brush.GradientStops.Add(new GradientStop(Color.FromRgb(255, 94, 94), 0.52d));
        brush.GradientStops.Add(new GradientStop(Color.FromRgb(245, 245, 245), 1d));
        brush.Freeze();
        return brush;
    }

    private static LinearGradientBrush CreateFrozenHypercraftiingNameBrush()
    {
        var brush = new LinearGradientBrush
        {
            StartPoint = new Point(0, 0.5),
            EndPoint = new Point(1, 0.5)
        };
        brush.GradientStops.Add(new GradientStop(Color.FromRgb(244, 214, 255), 0d));
        brush.GradientStops.Add(new GradientStop(Color.FromRgb(255, 94, 120), 0.48d));
        brush.GradientStops.Add(new GradientStop(Color.FromRgb(180, 112, 255), 1d));
        brush.Freeze();
        return brush;
    }

    private static LinearGradientBrush CreateFrozenKyouZakiraNameBrush()
    {
        var brush = new LinearGradientBrush
        {
            StartPoint = new Point(0, 0.5),
            EndPoint = new Point(1, 0.5)
        };
        brush.GradientStops.Add(new GradientStop(Color.FromRgb(246, 224, 255), 0d));
        brush.GradientStops.Add(new GradientStop(Color.FromRgb(192, 96, 255), 0.35d));
        brush.GradientStops.Add(new GradientStop(Color.FromRgb(255, 82, 110), 0.7d));
        brush.GradientStops.Add(new GradientStop(Color.FromRgb(255, 238, 246), 1d));
        brush.Freeze();
        return brush;
    }

    private static LinearGradientBrush CreateFrozenPhil13938NameBrush()
    {
        var brush = new LinearGradientBrush
        {
            StartPoint = new Point(0, 0.5),
            EndPoint = new Point(1, 0.5)
        };
        brush.GradientStops.Add(new GradientStop(Color.FromRgb(214, 0, 0), 0d));
        brush.GradientStops.Add(new GradientStop(Color.FromRgb(233, 97, 0), 0.5d));
        brush.GradientStops.Add(new GradientStop(Color.FromRgb(68, 0, 0), 1d));
        brush.Freeze();
        return brush;
    }

    private static LinearGradientBrush CreateFrozenFalxPlaysNameBrush()
    {
        var brush = new LinearGradientBrush
        {
            StartPoint = new Point(0, 0.5),
            EndPoint = new Point(1, 0.5)
        };
        brush.GradientStops.Add(new GradientStop(Color.FromRgb(0, 191, 165), 0d));
        brush.GradientStops.Add(new GradientStop(Color.FromRgb(41, 121, 255), 1d));
        brush.Freeze();
        return brush;
    }

    private static bool IsCrystalRelayDeveloperAccount(string displayName, string login) =>
        IsCrystalRelayDeveloperName(displayName) || IsCrystalRelayDeveloperName(login);

    private static bool IsCrystalRelayDeveloperName(string value)
    {
        var normalized = NormalizeTwitchName(value);
        return string.Equals(normalized, CrystalRelayDeveloperLogin, StringComparison.OrdinalIgnoreCase);
    }

    private static bool IsKaiBloodwolfAccount(string displayName, string login) =>
        IsKaiBloodwolfName(displayName) || IsKaiBloodwolfName(login);

    private static bool IsKaiBloodwolfName(string value)
    {
        var normalized = NormalizeTwitchName(value);
        return string.Equals(normalized, KaiBloodwolfLogin, StringComparison.OrdinalIgnoreCase);
    }

    private static bool IsHypercraftiingAccount(string displayName, string login) =>
        IsHypercraftiingName(displayName) || IsHypercraftiingName(login);

    private static bool IsHypercraftiingName(string value)
    {
        var normalized = NormalizeTwitchName(value);
        return string.Equals(normalized, HypercraftiingLogin, StringComparison.OrdinalIgnoreCase);
    }

    private static bool IsKyouZakiraAccount(string displayName, string login) =>
        IsKyouZakiraName(displayName) || IsKyouZakiraName(login);

    private static bool IsKyouZakiraName(string value)
    {
        var normalized = NormalizeTwitchName(value);
        return string.Equals(normalized, KyouZakiraLogin, StringComparison.OrdinalIgnoreCase);
    }

    private static bool IsPhil13938Account(string displayName, string login) =>
        IsPhil13938Name(displayName) || IsPhil13938Name(login);

    private static bool IsPhil13938Name(string value)
    {
        var normalized = NormalizeTwitchName(value);
        return string.Equals(normalized, Phil13938Login, StringComparison.OrdinalIgnoreCase);
    }

    private static bool IsFalxPlaysAccount(string displayName, string login) =>
        IsFalxPlaysName(displayName) || IsFalxPlaysName(login);

    private static bool IsFalxPlaysName(string value)
    {
        var normalized = NormalizeTwitchName(value);
        return string.Equals(normalized, FalxPlaysLogin, StringComparison.OrdinalIgnoreCase);
    }

    private static string NormalizeTwitchName(string value) =>
        (value ?? string.Empty).Trim().TrimStart('@').Trim();

    private static ChatTimestampFormat NormalizeTimestampFormat(ChatTimestampFormat value) =>
        Enum.IsDefined(value) ? value : ChatTimestampFormat.TwentyFourHour;

    private static string FormatTimestamp(DateTimeOffset receivedAt, ChatTimestampFormat format) =>
        NormalizeTimestampFormat(format) == ChatTimestampFormat.TwelveHour
            ? receivedAt.ToLocalTime().ToString("hh:mm:ss tt")
            : receivedAt.ToLocalTime().ToString("HH:mm:ss");

    private static double GetRelativeLuminance(Color color)
    {
        static double ConvertChannel(byte channel)
        {
            var value = channel / 255d;
            return value <= 0.03928
                ? value / 12.92
                : Math.Pow((value + 0.055) / 1.055, 2.4);
        }

        var red = ConvertChannel(color.R);
        var green = ConvertChannel(color.G);
        var blue = ConvertChannel(color.B);
        return (0.2126 * red) + (0.7152 * green) + (0.0722 * blue);
    }

    private static Color EnsureReadableNameColor(Color color, AppTheme theme)
    {
        if (theme == AppTheme.Bubblegum)
        {
            var contrast = GetContrastRatio(color, Color.FromRgb(236, 230, 240));
            if (contrast >= 3.8d)
            {
                return color;
            }

            // In Bubblegum, push low-contrast colors toward deeper readable pastel-dark.
            return Color.FromRgb(
                DarkenChannel(color.R),
                DarkenChannel(color.G),
                DarkenChannel(color.B));
        }

        var referenceColor = theme == AppTheme.Custom
            ? ThemeManager.CurrentPalette.GetColor("RuleCardBrush")
            : DarkCardReferenceColor;
        var referenceIsLight = GetRelativeLuminance(referenceColor) > 0.5d;

        if (!referenceIsLight && color.R <= 72 && color.G <= 72 && color.B <= 72)
        {
            return ((SolidColorBrush)DefaultNameBrush).Color;
        }

        var referenceContrast = GetContrastRatio(color, referenceColor);
        if (referenceContrast >= 4.5d)
        {
            return color;
        }

        var adjusted = color;
        for (var attempt = 0; attempt < 7 && GetContrastRatio(adjusted, referenceColor) < 4.5d; attempt++)
        {
            adjusted = referenceIsLight
                ? Color.FromRgb(
                    DarkenChannel(adjusted.R),
                    DarkenChannel(adjusted.G),
                    DarkenChannel(adjusted.B))
                : Color.FromRgb(
                    LiftChannel(adjusted.R),
                    LiftChannel(adjusted.G),
                    LiftChannel(adjusted.B));
        }

        if (GetContrastRatio(adjusted, referenceColor) >= 4.5d)
        {
            return adjusted;
        }

        return referenceIsLight
            ? Colors.Black
            : ((SolidColorBrush)DefaultNameBrush).Color;
    }

    private static byte LiftChannel(byte channel)
    {
        var remaining = 255 - channel;
        return (byte)Math.Clamp(channel + (remaining * 0.32), 0, 255);
    }

    private static byte DarkenChannel(byte channel)
    {
        return (byte)Math.Clamp(channel * 0.58, 0, 255);
    }

    private static double GetContrastRatio(Color foreground, Color background)
    {
        var foregroundLuminance = GetRelativeLuminance(foreground);
        var backgroundLuminance = GetRelativeLuminance(background);
        var lighter = Math.Max(foregroundLuminance, backgroundLuminance);
        var darker = Math.Min(foregroundLuminance, backgroundLuminance);
        return (lighter + 0.05d) / (darker + 0.05d);
    }
}

public enum TwitchChatInlineFragmentKind
{
    Text,
    Emote
}

public sealed record TwitchChatInlineFragment(
    TwitchChatInlineFragmentKind Kind,
    string Text,
    string ImageUrl)
{
    public Uri? ImageUri =>
        Uri.TryCreate(ImageUrl, UriKind.Absolute, out var imageUri)
        && string.Equals(imageUri.Scheme, Uri.UriSchemeHttps, StringComparison.OrdinalIgnoreCase)
            ? imageUri
            : null;
}

public sealed record ActionTypeOption(OscActionType Value, string Label)
{
    public override string ToString() => Label;
}

public sealed record ThemeOption(AppTheme Value, string Label);

public sealed record AppLanguageOption(AppLanguage Value, string Label)
{
    public override string ToString() => Label;
}

public sealed record AvatarProfileScopeOption(Guid Id, string Label)
{
    public override string ToString() => Label;
}

public sealed record ChatTimestampFormatOption(ChatTimestampFormat Value, string Label)
{
    public override string ToString() => Label;
}

public sealed record ChatCommandPermissionOption(ChatCommandPermission Value, string Label)
{
    public override string ToString() => Label;
}

public sealed record AvatarScaleModeOption(AvatarScaleMode Value, string Label);

public sealed record PlayerMovementOption(PlayerMovementDirection Value, string Label);

public sealed record AvatarScaleSubscriptionTierOption(string Value, string Label)
{
    public override string ToString() => Label;
}

public sealed record CashPaymentProviderOption(CashPaymentProvider Value, string Label)
{
    public override string ToString() => Label;
}

public sealed record CashPaymentActionKindOption(CashPaymentActionKind Value, string Label)
{
    public override string ToString() => Label;
}

public sealed record TriggerRuleReferenceOption(Guid RuleId, string Label);
