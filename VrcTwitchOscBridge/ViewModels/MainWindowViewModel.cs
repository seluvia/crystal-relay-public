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
public sealed class MainWindowViewModel : ObservableObject, IAsyncDisposable
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

    private enum ManagedRewardSyncReason
    {
        SettingsEdit,
        Startup,
        AccountReconnect,
        AvatarChanged,
        RuntimeAvailability,
        AvatarScaleStatus,
        TestMode,
        EmergencyStop,
        StreamStateChanged,
        FireSaleChanged,
        ManualRefresh,
        ManualCleanup,
        Maintenance
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

    private const string TwitchActivationUri = "https://www.twitch.tv/activate";
    private const string TwitchDeveloperConsoleUri = "https://dev.twitch.tv/console/apps";
    private const string KoFiSupportUri = "https://ko-fi.com/screminpal";
    private const string SettingsTestAudioRelativePath = "Assets\\engineer_no01.mp3";
    private const string TestBuildMarkerFileName = "test-build.flag";
    private const string BetaBuildMarkerFileName = "beta-build.flag";
    private const int MaxLogEntryCount = 200;
    private const int MaxChatMessageCount = 250;
    private const int TwitchCustomRewardPromptMaxLength = 200;
    private const int TwitchCustomRewardLimit = 50;
    private static readonly TimeSpan ManagedRewardCreateBackoffWindow = TimeSpan.FromSeconds(45);
    private static readonly TimeSpan ThrottledRewardSyncLogWindow = TimeSpan.FromSeconds(30);
    private static readonly TimeSpan AvatarScaleLimitRewardSyncDebounce = TimeSpan.FromMilliseconds(750);
    private static readonly TimeSpan TwitchAccessTokenRefreshLeadTime = TimeSpan.FromMinutes(15);
    private static readonly TimeSpan TwitchPublicRefreshSessionWindow = TimeSpan.FromDays(30);
    private static readonly TimeSpan VrChatLocalStatePollInterval = TimeSpan.FromSeconds(2);
    private static readonly TimeSpan VrChatCurrentAvatarPollInterval = TimeSpan.FromSeconds(15);
    private static readonly TimeSpan ActiveAvatarScaleLocalRefreshInterval = TimeSpan.FromSeconds(2);
    private static readonly TimeSpan VrChatOscParameterAutoRefreshInitialDelay = TimeSpan.FromSeconds(2);
    private static readonly TimeSpan VrChatOscParameterAutoRefreshRetryDelay = TimeSpan.FromSeconds(3);
    private static readonly TimeSpan TwitchPublicSessionWindow = TimeSpan.FromDays(30);
    private static readonly TimeSpan AboutProfileRefreshInterval = TimeSpan.FromMinutes(5);
    private static readonly Guid AvatarScaleMasterRewardOwnerId = new("c69a2537-6c74-450f-9c5a-b6d9f04a7d95");
    private static readonly Guid RewardFireSaleFundingRewardOwnerId = new("f31cdb57-052f-4dd4-96d3-1c2b044e2fd9");
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
    private static readonly string BetaBuildLabel = DetectBetaBuildLabel();
    private static readonly bool IsTestBuild = DetectTestBuild();
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
        nameof(TriggerRule.MaxAccumulatedDurationEnabled),
        nameof(TriggerRule.MaxAccumulatedDurationSeconds),
        nameof(TriggerRule.ActionType),
        nameof(TriggerRule.MovementDirection),
        nameof(TriggerRule.ParameterName),
        nameof(TriggerRule.ParameterType),
        nameof(TriggerRule.IntZeroDurationMode),
        nameof(TriggerRule.ParameterValue),
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
        nameof(TriggerRule.SetTriggerActions),
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
        nameof(TriggerRule.ParameterName),
        nameof(TriggerRule.SharedRewardChoiceEnabled),
        nameof(TriggerRule.SharedRewardChoiceNumber),
        nameof(TriggerRule.SharedRewardHelpText),
        nameof(TriggerRule.SetTriggerActions),
        nameof(TriggerRule.AvatarChangeTargetId),
        nameof(TriggerRule.AvatarRouletAvatarIds),
        nameof(TriggerRule.IsEnabled),
        nameof(TriggerRule.CooldownSeconds),
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
        nameof(AvatarTriggerProfile.SetTriggerMasterRewardCooldownSeconds)
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
        nameof(AvatarTriggerProfile.DeleteSetTriggerMasterRewardWhenInactive)
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
    private readonly VrChatApiClient vrChatApiClient = new();
    private readonly VrChatLocalClientStateService vrChatLocalClientStateService = new();
    private readonly VrChatLocalOscCacheService vrChatLocalOscCacheService = new();
    private readonly Dispatcher dispatcher;
    private readonly DesktopInputLockService desktopInputLockService;
    private readonly BridgeCoordinator bridgeCoordinator;
    private readonly SemaphoreSlim bridgeRefreshGate = new(1, 1);
    private readonly SemaphoreSlim managedRewardSyncGate = new(1, 1);
    private readonly SemaphoreSlim vrChatLocalStateRefreshGate = new(1, 1);
    private readonly SemaphoreSlim vrChatCurrentAvatarRefreshGate = new(1, 1);
    private readonly DispatcherTimer sessionStatusTimer;
    private readonly DispatcherTimer vrChatLocalStateTimer;
    private readonly DispatcherTimer vrChatCurrentAvatarTimer;
    private readonly Dictionary<Guid, Guid> lastSelectedRuleIdsByAvatarProfileId = new();
    private readonly ObservableCollection<TriggerRule> emptyMasterAvatarRules = [];

    private AppSettings settings = new();
    private RuntimeConfig runtimeConfig = RuntimeConfig.CreateDefault();
    private TriggerRule? selectedRule;
    private UniversalTriggerRule? selectedUniversalTrigger;
    private UniversalTriggerAction? selectedUniversalTriggerAction;
    private MovementRedeemSet? selectedMovementRedeemSet;
    private AvatarScaleSet? selectedAvatarScaleSet;
    private AvatarScaleRule? selectedAvatarScaleRule;
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
    private string streamingStatusSummary = "Broadcaster not connected.";
    private string streamingStatusDetail = "Connect Twitch to monitor stream and listener status.";
    private string streamingStatusVisualState = "Disconnected";
    private string streamingStreamStateText = "Unavailable";
    private string streamingStreamStateVisual = "Disconnected";
    private string streamingListenerStateText = "Offline";
    private string streamingListenerStateVisual = "Disconnected";
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
    private SectionView activeSection;
    private SettingsSectionView activeSettingsSection = SettingsSectionView.Accounts;
    private RuleListView activeRuleListView = RuleListView.AvatarTriggers;
    private TwitchChatboxWindow? twitchChatboxWindow;
    private bool isInitialized;
    private bool isRestoringAvatarParameterSelection;
    private bool isRestoringSetTriggerParameterSelection;
    private bool isApplyingMasterAvatarDefaults;
    private bool isRefreshingVrChatAvatarSelectionOptions;
    private bool isSynchronizingManagedRewards;
    private bool isSwitchingRuleView;
    private bool isShuttingDown;
    private bool suppressRewardFireSaleChangeSideEffects;
    private bool isRefreshingAboutProfiles;
    private bool isNormalizingChatCommandRules;
    private bool runtimeConfigLoaded;
    private bool broadcasterManagedRewardsUnavailableForSession;
    private string universalManagedRewardSyncStatusText = "Universal Twitch reward sync has not run yet.";
    private CancellationTokenSource? saveDebounceCancellation;
    private CancellationTokenSource? bridgeRefreshCancellation;
    private CancellationTokenSource? managedRewardSyncCancellation;
    private CancellationTokenSource? vrChatCurrentAvatarRefreshCancellation;
    private CancellationTokenSource? vrChatOscParameterRefreshCancellation;
    private CancellationTokenSource? vrChatLocalOscScanCancellation;
    private CancellationTokenSource? activeAvatarScaleLocalRefreshCancellation;
    private CancellationTokenSource? rewardFireSaleExpirationCancellation;
    private CancellationTokenSource? rewardFireSaleFundingCooldownCancellation;
    private DateTimeOffset? rewardFireSaleFundingRewardCooldownUntil;
    private FileSystemWatcher? vrChatLocalOscWatcher;
    private readonly List<VrChatAvatarSummary> availableVrChatAvatars = [];
    private readonly Dictionary<string, string> availableVrChatAvatarNamesById = new(StringComparer.Ordinal);
    private readonly Dictionary<string, List<VrChatOscParameterSummary>> cachedVrChatParametersByAvatarId = new(StringComparer.Ordinal);
    private readonly Dictionary<string, DateTime> vrChatLocalOscAvatarWriteTimes = new(StringComparer.Ordinal);
    private readonly HashSet<string> retiredManagedRewardIds = new(StringComparer.Ordinal);
    private readonly Dictionary<string, DateTimeOffset> managedRewardCreateBackoffByTitle = new(StringComparer.OrdinalIgnoreCase);
    private readonly Dictionary<Guid, bool> avatarScaleLimitInactiveStateByRuleId = [];
    private readonly Dictionary<string, DateTimeOffset> throttledLogExpiryByKey = new(StringComparer.Ordinal);
    private readonly List<ActionTypeOption> allActionTypes;
    private string lastSuccessfulManagedRewardDesiredFingerprint = string.Empty;
    private DateTimeOffset? managedRewardApiBackoffUntil;
    private DateTimeOffset aboutProfilesLastRefreshedAt = DateTimeOffset.MinValue;
    private DateTime latestLocalVrChatAvatarWriteTimeUtc = DateTime.MinValue;
    private string vrChatOutputLogPath = string.Empty;
    private long vrChatOutputLogPosition;
    private string lastDetectedVrChatAvatarId = string.Empty;
    private Guid lastSelectedAvatarProfileId = Guid.Empty;
    private Guid selectedSupporterAvatarProfileId = Guid.Empty;
    private Guid lastSelectedMasterRuleId = Guid.Empty;
    private Guid lastSelectedMovementSetId = Guid.Empty;
    private Guid lastSelectedMovementRuleId = Guid.Empty;
    private Guid lastSelectedSupporterRuleId = Guid.Empty;
    private Guid lastSelectedUniversalTriggerId = Guid.Empty;
    private Guid lastSelectedAvatarScaleSetId = Guid.Empty;
    private Guid lastSelectedAvatarScaleRuleId = Guid.Empty;
    private AppLanguage activeLanguageAtStartup = AppLanguage.SystemDefault;
    private ICollectionView? universalTriggersGroupedView;
    private bool isUniversalChatCommandsExpanded = true;
    private bool isUniversalChannelPointRewardsExpanded = true;
    private bool isUniversalBitsExpanded = true;
    private bool isUniversalSubscriptionsExpanded = true;
    private bool isUniversalGiftSubscriptionsExpanded = true;
    private bool isUniversalFollowsExpanded = true;
    private bool isAvatarBoolRedeemsExpanded;
    private bool isAvatarIntRedeemsExpanded;
    private bool isAvatarFloatRedeemsExpanded;
    private bool isAvatarMixRedeemsExpanded;
    private bool isAvatarOtherRedeemsExpanded;

    private static string T(string sourceText) => LocalizationService.Translate(sourceText);

    private static string TF(string sourceFormat, params object[] args) => LocalizationService.Format(sourceFormat, args);

    // Constructor setup for option lists, status defaults, commands, and About-page profiles.
    public MainWindowViewModel()
    {
        dispatcher = Dispatcher.CurrentDispatcher;
        desktopInputLockService = new DesktopInputLockService(dispatcher);
        bridgeCoordinator = new BridgeCoordinator(desktopInputLockService);
        LogEntries = [];
        ChatMessages = [];
        RewardOptions = [];
        RewardSyncModeOptions =
        [
            new TwitchRewardSyncModeOption(TwitchRewardSyncMode.CreateOrManage, T("Create/manage reward")),
            new TwitchRewardSyncModeOption(TwitchRewardSyncMode.LinkExisting, T("Link existing Twitch reward"))
        ];
        AvatarRuleProfiles = [];
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
        streamingStatusSummary = T("Broadcaster not connected.");
        streamingStatusDetail = T("Connect Twitch to monitor stream and listener status.");
        streamingStatusVisualState = "Disconnected";
        streamingStreamStateText = T("Unavailable");
        streamingStreamStateVisual = "Disconnected";
        streamingListenerStateText = T("Offline");
        streamingListenerStateVisual = "Disconnected";
        vrChatStatus = T("VRChat avatar access is not connected.");
        vrChatAvatarStatus = T("Connect VRChat to load avatar choices.");
        vrChatOscParameterStatus = T("Pick an avatar set to load its saved OSC parameters.");
        chatboxListenerStatus = T("Connect broadcaster to start Twitch Chatbox.");
        allActionTypes =
        [
            new ActionTypeOption(OscActionType.AvatarParameter, T("Avatar Parameter")),
            new ActionTypeOption(OscActionType.SetTrigger, T("Set Trigger")),
            new ActionTypeOption(OscActionType.AvatarChange, T("Avatar Change")),
            new ActionTypeOption(OscActionType.AvatarRoulet, T("Avatar Roulette")),
            new ActionTypeOption(OscActionType.PlayerMovement, T("Player Movement"))
        ];
        TriggerTypes = Enum.GetValues<TwitchTriggerType>();
        OverrideTriggerTypes =
        [
            TwitchTriggerType.Bits,
            TwitchTriggerType.Subscriptions
        ];
        UniversalTriggerTypes = Enum.GetValues<UniversalTriggerType>();
        AvatarScaleTriggerTypes = Enum.GetValues<AvatarScaleTriggerType>();
        AvatarScaleModes = Enum.GetValues<AvatarScaleMode>();
        AvatarScalePresets = Enum.GetValues<AvatarScalePreset>();
        AvatarScaleRestoreModes = Enum.GetValues<AvatarScaleRestoreMode>();
        AvatarScaleSubscriptionTierOptions =
        [
            new AvatarScaleSubscriptionTierOption(string.Empty, T("Any tier")),
            new AvatarScaleSubscriptionTierOption("1000", T("Tier 1")),
            new AvatarScaleSubscriptionTierOption("2000", T("Tier 2")),
            new AvatarScaleSubscriptionTierOption("3000", T("Tier 3"))
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
            new ThemeOption(AppTheme.StinkyOnline, "Stinky Online")
        ];
        LanguageOptions =
        [
            new AppLanguageOption(AppLanguage.SystemDefault, T("System Default")),
            new AppLanguageOption(AppLanguage.English, "English"),
            new AppLanguageOption(AppLanguage.Spanish, "EspaÃ±ol"),
            new AppLanguageOption(AppLanguage.Japanese, "æ—¥æœ¬èªž"),
            new AppLanguageOption(AppLanguage.German, "Deutsch"),
            new AppLanguageOption(AppLanguage.French, "FranÃ§ais"),
            new AppLanguageOption(AppLanguage.PortugueseBrazil, "PortuguÃªs (Brasil)"),
            new AppLanguageOption(AppLanguage.Swedish, "Svenska"),
            new AppLanguageOption(AppLanguage.Italian, "Italiano"),
            new AppLanguageOption(AppLanguage.ChineseSimplified, "ç®€ä½“ä¸­æ–‡"),
            new AppLanguageOption(AppLanguage.ChineseTraditional, "ç¹é«”ä¸­æ–‡"),
            new AppLanguageOption(AppLanguage.Korean, "í•œêµ­ì–´"),
            new AppLanguageOption(AppLanguage.Russian, "Ð ÑƒÑÑÐºÐ¸Ð¹"),
            new AppLanguageOption(AppLanguage.Polish, "Polski"),
            new AppLanguageOption(AppLanguage.Thai, "à¹„à¸—à¸¢")
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
        CustomThemeFontOptions = [.. Fonts.SystemFontFamilies
            .Select(fontFamily => fontFamily.Source)
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .OrderBy(name => name, StringComparer.OrdinalIgnoreCase)];
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
            new PlayerMovementOption(PlayerMovementDirection.SpinRight, T("Spin Right"))
        ];
        ChatCommandPermissionOptions =
        [
            new ChatCommandPermissionOption(ChatCommandPermission.Everyone, T("Everyone")),
            new ChatCommandPermissionOption(ChatCommandPermission.Moderators, T("Moderators + Broadcaster")),
            new ChatCommandPermissionOption(ChatCommandPermission.Broadcaster, T("Broadcaster Only"))
        ];
        ParameterTypes = [OscParameterType.Bool, OscParameterType.Int, OscParameterType.Float];
        UniversalTriggerValueKinds = Enum.GetValues<UniversalTriggerValueKind>();
        IntZeroDurationModes = Enum.GetValues<IntZeroDurationMode>();
        BoolValueOptions = ["True", "False"];
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

        ConnectBroadcasterCommand = new AsyncRelayCommand(ConnectBroadcasterAsync);
        DisconnectBroadcasterCommand = new AsyncRelayCommand(DisconnectBroadcasterAsync, () => Settings.Broadcaster.IsConnected);
        OpenBroadcasterLoginCommand = new RelayCommand(() => OpenUri(BroadcasterVerificationUri), () => !string.IsNullOrWhiteSpace(BroadcasterVerificationUri));

        ConnectBotCommand = new AsyncRelayCommand(ConnectBotAsync);
        DisconnectBotCommand = new AsyncRelayCommand(DisconnectBotAsync, () => Settings.Bot.IsConnected);
        OpenBroadcasterAuthPageCommand = new AsyncRelayCommand(OpenOrAuthenticateBroadcasterAsync);
        OpenBotAuthPageCommand = new AsyncRelayCommand(OpenOrAuthenticateBotAsync);
        OpenBotLoginCommand = new RelayCommand(OpenBotAuthPage);
        ConnectVrChatCommand = new AsyncRelayCommand(ConnectVrChatAsync);
        DisconnectVrChatCommand = new AsyncRelayCommand(DisconnectVrChatAsync, () => Settings.VrChat.IsConnected);
        RefreshVrChatAvatarsCommand = new AsyncRelayCommand(RefreshVrChatAvatarsAsync, () => Settings.VrChat.IsConnected);
        OpenRuntimeConfigCommand = new RelayCommand(OpenRuntimeConfigFile);
        OpenRuntimeConfigFolderCommand = new RelayCommand(OpenRuntimeConfigFolder);
        OpenTwitchDeveloperConsoleCommand = new RelayCommand(OpenTwitchDeveloperConsole);
        OpenSaveFolderCommand = new RelayCommand(OpenSaveFolder);
        OpenKoFiSupportCommand = new RelayCommand(OpenKoFiSupportPage);
        OpenBugReportCommand = new AsyncRelayCommand(OpenBugReportAsync);
        RefreshOscConnectionCommand = new AsyncRelayCommand(RefreshOscConnectionAsync);
        RefreshTwitchRewardsCommand = new AsyncRelayCommand(RefreshTwitchRewardsAsync);
        UnlinkTwitchRewardCommand = new RelayCommand(UnlinkTwitchReward);
        TestSelectedRuleCommand = new AsyncRelayCommand(TestSelectedRuleAsync, () => SelectedRule is not null);
        ShowSettingsTestCommand = new RelayCommand(ShowSettingsTestPopup);
        ShowHomeSectionCommand = new RelayCommand(() => SetActiveSection(SectionView.Home));
        ShowSettingsSectionCommand = new RelayCommand(() => SetActiveSection(SectionView.Settings));
        ShowActivitySectionCommand = new RelayCommand(() => SetActiveSection(SectionView.Activity));
        ShowAboutSectionCommand = new RelayCommand(() => SetActiveSection(SectionView.About));
        OpenTwitchChatboxCommand = new RelayCommand(OpenTwitchChatbox);
        ShowSettingsAccountsSectionCommand = new RelayCommand(() => SetActiveSettingsSection(SettingsSectionView.Accounts));
        ShowSettingsVisualsSectionCommand = new RelayCommand(() => SetActiveSettingsSection(SettingsSectionView.Visuals));
        ShowAvatarTriggerRulesCommand = new RelayCommand(ShowAvatarTriggerRules);
        ShowMasterAvatarTabCommand = new RelayCommand(ShowMasterAvatarTab);
        ShowMovementRedeemsCommand = new RelayCommand(ShowMovementRedeems);
        ShowSupporterOverridesCommand = new RelayCommand(ShowSupporterOverrides);
        ShowUniversalTriggersCommand = new RelayCommand(ShowUniversalTriggers);
        ShowAvatarScalingCommand = new RelayCommand(ShowAvatarScaling);
        ShowRewardFireSaleCommand = new RelayCommand(ShowRewardFireSale);
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
        UseCurrentAvatarForSupporterRuleCommand = new RelayCommand(
            UseCurrentAvatarForSupporterRule,
            () => CanUseCurrentAvatarForSupporterRule());
        UseCurrentAvatarForAvatarChangeRuleCommand = new RelayCommand(
            UseCurrentAvatarForAvatarChangeRule,
            () => CanUseCurrentAvatarForAvatarChangeRule());
        RefreshVrChatOscParametersCommand = new AsyncRelayCommand(RefreshVrChatOscParametersAsync);

        AddRuleCommand = new RelayCommand(AddRule);
        SelectRuleCommand = new RelayCommand(SelectRule, target => target is TriggerRule);
        AddAvatarSupporterTriggerCommand = new RelayCommand(AddAvatarSupporterTrigger);
        AddAvatarChangeOverrideCommand = new RelayCommand(AddAvatarChangeOverride);
        RemoveSelectedRuleCommand = new RelayCommand(RemoveSelectedRule, () => SelectedRule is not null);
        EnableAllRulesCommand = new RelayCommand(EnableAllRules, () => GetCurrentEditableRuleCollection().Count > 0);
        DisableAllRulesCommand = new RelayCommand(DisableAllRules, () => GetCurrentEditableRuleCollection().Count > 0);
        DeleteAllRulesCommand = new RelayCommand(DeleteAllRules, () => GetCurrentEditableRuleCollection().Count > 0);
        AddUniversalTriggerCommand = new RelayCommand(AddUniversalTrigger);
        RemoveSelectedUniversalTriggerCommand = new RelayCommand(RemoveSelectedUniversalTrigger, () => SelectedUniversalTrigger is not null);
        EnableAllUniversalTriggersCommand = new RelayCommand(EnableAllUniversalTriggers, () => Settings.UniversalTriggers.Count > 0);
        DisableAllUniversalTriggersCommand = new RelayCommand(DisableAllUniversalTriggers, () => Settings.UniversalTriggers.Count > 0);
        DeleteAllUniversalTriggersCommand = new RelayCommand(DeleteAllUniversalTriggers, () => Settings.UniversalTriggers.Count > 0);
        TestSelectedUniversalTriggerCommand = new AsyncRelayCommand(TestSelectedUniversalTriggerAsync, () => SelectedUniversalTrigger is not null);
        ImportFoomaInteractionConfigCommand = new AsyncRelayCommand(ImportFoomaInteractionConfigAsync);
        AddUniversalTriggerActionCommand = new RelayCommand(AddUniversalTriggerAction, () => SelectedUniversalTrigger is not null);
        RemoveSelectedUniversalTriggerActionCommand = new RelayCommand(RemoveSelectedUniversalTriggerAction, () => SelectedUniversalTriggerAction is not null);
        AddMovementRedeemSetCommand = new RelayCommand(AddMovementRedeemSet);
        RemoveSelectedMovementRedeemSetCommand = new RelayCommand(RemoveSelectedMovementRedeemSet, () => SelectedMovementRedeemSet is not null);
        DeleteAllMovementRedeemSetsCommand = new RelayCommand(DeleteAllMovementRedeemSets, () => Settings.MovementRedeemSets.Count > 0);
        AddAvatarScaleSetCommand = new RelayCommand(AddAvatarScaleSet);
        RemoveSelectedAvatarScaleSetCommand = new RelayCommand(RemoveSelectedAvatarScaleSet, () => SelectedAvatarScaleSet is not null);
        AddAvatarScaleRuleCommand = new RelayCommand(AddAvatarScaleRule);
        RemoveSelectedAvatarScaleRuleCommand = new RelayCommand(RemoveSelectedAvatarScaleRule, () => SelectedAvatarScaleRule is not null);
        EnableAllAvatarScaleRulesCommand = new RelayCommand(EnableAllAvatarScaleRules, () => GetAllAvatarScaleRules().Count > 0);
        DisableAllAvatarScaleRulesCommand = new RelayCommand(DisableAllAvatarScaleRules, () => GetAllAvatarScaleRules().Count > 0);
        DeleteAllAvatarScaleRulesCommand = new RelayCommand(DeleteAllAvatarScaleSets, () => Settings.AvatarScaleSets.Count > 0);
        TestSelectedAvatarScaleRuleCommand = new RelayCommand(StartSelectedAvatarScaleRuleTest, CanTestSelectedAvatarScaleRule);
        OpenAvatarScaleRuleLockoutPickerCommand = new RelayCommand(OpenAvatarScaleRuleLockoutPicker, CanOpenAvatarScaleRuleLockoutPicker);
        OpenSpecialRuleLockoutPickerCommand = new RelayCommand(OpenSpecialRuleLockoutPicker, CanOpenSpecialRuleLockoutPicker);
        OpenAvatarRouletPoolPickerCommand = new RelayCommand(OpenAvatarRouletPoolPicker, CanOpenAvatarRouletPoolPicker);
        AddSetTriggerActionCommand = new RelayCommand(AddSetTriggerAction, () => SelectedRule?.ActionType == OscActionType.SetTrigger);
        RemoveSelectedSetTriggerActionCommand = new RelayCommand(RemoveSelectedSetTriggerAction, () => SelectedRule?.ActionType == OscActionType.SetTrigger && SelectedSetTriggerAction is not null);
        CopySelectedAvatarParameterPathCommand = new RelayCommand(CopySelectedAvatarParameterPath, CanCopySelectedAvatarParameterPath);
        PasteSelectedAvatarParameterPathCommand = new RelayCommand(PasteSelectedAvatarParameterPath, CanPasteSelectedAvatarParameterPath);
        AddRewardFireSaleTierCommand = new RelayCommand(AddRewardFireSaleTier);
        RemoveRewardFireSaleTierCommand = new RelayCommand(
            RemoveRewardFireSaleTier,
            target => target is RewardFireSaleTier && Settings.RewardFireSale.Tiers.Count > 1);
        StopRewardFireSaleCommand = new RelayCommand(StopRewardFireSale, () => Settings.RewardFireSale.IsSaleActive);
        ResetRewardFireSaleProgressCommand = new RelayCommand(ResetRewardFireSaleProgress, () => Settings.RewardFireSale.CurrentProgress > 0);

        bridgeCoordinator.LogWritten += message => RunOnUi(() =>
        {
            UpdateOscStatusFromLog(message);
            AppendLog(message);
        });
        bridgeCoordinator.StatusChanged += status => RunOnUi(() =>
        {
            BridgeStatus = status;
        });
        bridgeCoordinator.AccountUpdated += (role, snapshot) => RunOnUi(() =>
        {
            ApplyAccountSnapshot(role, snapshot);
            UpdateAccountStatuses();
            QueueSave();
            _ = QueueRewardRefreshAsync();
            QueueManagedRewardSync(0, ManagedRewardSyncReason.AccountReconnect);
            _ = RefreshAboutProfilesAsync();
        });
        bridgeCoordinator.ChatMessageReceived += message => RunOnUi(() => AppendChatMessage(message));
        bridgeCoordinator.VrChatAvatarChanged += avatarId => RunOnUi(() => HandleVrChatAvatarChangedByBridge(avatarId));
        bridgeCoordinator.SharedReturnAvatarChanged += (avatarId, avatarName) => RunOnUi(() => HandleSharedReturnAvatarChangedByBridge(avatarId, avatarName));
        bridgeCoordinator.StreamStateChanged += (isLive, streamEnded) => RunOnUi(() => HandleBroadcasterLiveStateChanged(isLive, streamEnded));
        bridgeCoordinator.ManagedRewardAvailabilityChanged += () => RunOnUi(() =>
        {
            RaisePropertyChanged(nameof(AvatarScaleMasterRewardStatusText));
            QueueManagedRewardSync(
                (int)AvatarScaleLimitRewardSyncDebounce.TotalMilliseconds,
                ManagedRewardSyncReason.RuntimeAvailability);
        });
        bridgeCoordinator.AvatarScaleStatusChanged += () => RunOnUi(HandleAvatarScaleStatusChanged);
        bridgeCoordinator.RewardFireSaleContributionReceived += contribution => RunOnUi(() => HandleRewardFireSaleContribution(contribution));

    }

    public AppSettings Settings
    {
        get => settings;
        private set => SetProperty(ref settings, value);
    }

    public ObservableCollection<string> LogEntries { get; }

    public ObservableCollection<TwitchChatMessageEntry> ChatMessages { get; }

    public ObservableCollection<TwitchRewardOption> RewardOptions { get; }

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

    public IReadOnlyList<UniversalTriggerType> UniversalTriggerTypes { get; }

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

    public IReadOnlyList<AvatarScaleMode> AvatarScaleModes { get; }

    public IReadOnlyList<AvatarScalePreset> AvatarScalePresets { get; }

    public IReadOnlyList<AvatarScaleRestoreMode> AvatarScaleRestoreModes { get; }

    public IReadOnlyList<AvatarScaleSubscriptionTierOption> AvatarScaleSubscriptionTierOptions { get; }

    public IReadOnlyList<ActionTypeOption> ActionTypes { get; }

    public IReadOnlyList<ThemeOption> ThemeOptions { get; }

    public IReadOnlyList<AppLanguageOption> LanguageOptions { get; }

    public IReadOnlyList<string> ChatFontOptions { get; }

    public IReadOnlyList<string> CustomThemeFontOptions { get; }

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

    public IReadOnlyList<OscParameterType> ParameterTypes { get; }

    public IReadOnlyList<UniversalTriggerValueKind> UniversalTriggerValueKinds { get; }

    public IReadOnlyList<IntZeroDurationMode> IntZeroDurationModes { get; }

    public IReadOnlyList<string> BoolValueOptions { get; }

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

    public bool IsLanguageRestartNoticeVisible => Settings.Language != activeLanguageAtStartup;

    public string LanguageRestartNoticeText => T("Language changes apply after you restart Crystal Relay.");

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

    private bool BroadcasterRewardManagementScopeKnownMissing =>
        Settings.Broadcaster.IsConnected
        && Settings.Broadcaster.Scopes.Count > 0
        && !BroadcasterCanManageRewards;

    public bool IsViewingAvatarTriggers => activeRuleListView == RuleListView.AvatarTriggers;

    public bool IsViewingMasterAvatar => activeRuleListView == RuleListView.MasterAvatar;

    public bool IsViewingMovementRedeems => activeRuleListView == RuleListView.MovementRedeems;

    public bool IsViewingSupporterOverrides => activeRuleListView == RuleListView.SupporterOverrides;

    public bool IsViewingUniversalTriggers => activeRuleListView == RuleListView.UniversalTriggers;

    public bool IsViewingAvatarScaling => activeRuleListView == RuleListView.AvatarScaling;

    public bool IsViewingRewardFireSale => activeRuleListView == RuleListView.RewardFireSale;

    public bool IsRewardFireSaleTemporary => Settings.RewardFireSale.SaleMode == RewardFireSaleMode.Temporary;

    public IReadOnlyList<RewardFireSaleModeOption> RewardFireSaleModeOptions { get; } =
    [
        new RewardFireSaleModeOption(RewardFireSaleMode.Temporary, T("Temporary")),
        new RewardFireSaleModeOption(RewardFireSaleMode.Permanent, T("Permanent"))
    ];

    public string RewardFireSaleStatusText
    {
        get
        {
            var fireSale = Settings.RewardFireSale;
            if (!fireSale.IsEnabled)
            {
                return T("Reward Fire Sale is off.");
            }

            if (IsRewardFireSaleActiveNow())
            {
                var untilText = fireSale.SaleMode == RewardFireSaleMode.Temporary && fireSale.ActiveUntilUtc is { } activeUntil
                    ? TF(" Ends {0}.", activeUntil.ToLocalTime().ToString("g"))
                    : T(" Stays active until stopped.");
                return TF(
                    "Fire Sale active: {0}% off from the {1:N0} goal tier.{2}",
                    fireSale.ActiveDiscountPercent,
                    fireSale.ActiveTierGoalAmount,
                    untilText);
            }

            var nextTier = GetNextRewardFireSaleTier();
            if (nextTier is null)
            {
                return T("Add a Fire Sale tier to start tracking progress.");
            }

            var remaining = Math.Max(0, nextTier.GoalAmount - fireSale.CurrentProgress);
            return TF(
                "{0:N0} / {1:N0} progress. {2:N0} more to start {3}% off.",
                fireSale.CurrentProgress,
                nextTier.GoalAmount,
                remaining,
                nextTier.DiscountPercent);
        }
    }

    public double RewardFireSaleProgressPercent
    {
        get
        {
            var nextTier = GetNextRewardFireSaleTier();
            if (nextTier is null)
            {
                return 0;
            }

            return Math.Clamp(Settings.RewardFireSale.CurrentProgress / (double)Math.Max(1, nextTier.GoalAmount) * 100d, 0d, 100d);
        }
    }

    public string RewardFireSaleActiveWarningText => T("Fire Sale Test Mode warning: starting or stopping a Fire Sale changes Crystal Relay-owned Twitch reward costs. Linked rewards stay listen-only. Stop the sale or let the timer expire to restore normal prices.");

    public string RewardFireSaleFundingRewardConversionText
    {
        get
        {
            var fireSale = Settings.RewardFireSale;
            return TF(
                "At {0:N0} points and {1:N0}:1 conversion, each redeem adds {2:N0} Fire Sale progress.",
                Math.Max(1, fireSale.FundingRewardCost),
                Math.Max(1, fireSale.RewardPointsPerProgressUnit),
                GetRewardFireSaleFundingProgressPerRedeem());
        }
    }

    public string RewardFireSaleFundingRewardPrompt => BuildRewardFireSaleFundingRewardPrompt();

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
        .Where(rule => !IsSupporterAvatarChangeOverride(rule))
        .ToArray();

    public IReadOnlyList<TriggerRule> AvatarChangeOverrideRules => Settings.GlobalOverrideRules
        .Where(IsSupporterAvatarChangeOverride)
        .ToArray();

    public IReadOnlyList<TriggerRule> GlobalSupporterRules => [];

    public bool HasSelectedAvatarSupporterRules => SelectedAvatarSupporterRules.Count > 0;

    public bool HasAvatarChangeOverrideRules => AvatarChangeOverrideRules.Count > 0;

    public bool HasGlobalSupporterRules => GlobalSupporterRules.Count > 0;

    public IReadOnlyList<MovementRedeemSet> MovementRedeemSets => Settings.MovementRedeemSets.ToArray();

    public IReadOnlyList<TriggerRule> MovementRedeemRules => SelectedMovementRedeemSet?.MovementRules.ToArray() ?? [];

    public IReadOnlyList<AvatarScaleSet> AvatarScaleSets => Settings.AvatarScaleSets.ToArray();

    public IReadOnlyList<AvatarScaleRule> AvatarScaleRules => SelectedAvatarScaleSet?.ScaleRules.ToArray() ?? [];

    public IReadOnlyList<UniversalTriggerRule> UniversalChatCommandTriggers => GetUniversalTriggersByType(UniversalTriggerType.ChatCommand);

    public IReadOnlyList<UniversalTriggerRule> UniversalChannelPointRewardTriggers => GetUniversalTriggersByType(UniversalTriggerType.ChannelPointReward);

    public IReadOnlyList<UniversalTriggerRule> UniversalBitsTriggers => GetUniversalTriggersByType(UniversalTriggerType.Bits);

    public IReadOnlyList<UniversalTriggerRule> UniversalSubscriptionTriggers => GetUniversalTriggersByType(UniversalTriggerType.Subscription);

    public IReadOnlyList<UniversalTriggerRule> UniversalGiftSubscriptionTriggers => GetUniversalTriggersByType(UniversalTriggerType.GiftSubscription);

    public IReadOnlyList<UniversalTriggerRule> UniversalFollowTriggers => GetUniversalTriggersByType(UniversalTriggerType.Follow);

    public ICollectionView UniversalTriggersGroupedView => universalTriggersGroupedView ??= CreateUniversalTriggersGroupedView();

    public IReadOnlyList<TriggerRule> SelectedAvatarBoolRedeems => GetSelectedAvatarRedeemsByParameterType(OscParameterType.Bool);

    public IReadOnlyList<TriggerRule> SelectedAvatarIntRedeems => GetSelectedAvatarRedeemsByParameterType(OscParameterType.Int);

    public IReadOnlyList<TriggerRule> SelectedAvatarFloatRedeems => GetSelectedAvatarRedeemsByParameterType(OscParameterType.Float);

    public IReadOnlyList<TriggerRule> SelectedAvatarMixRedeems => GetSelectedAvatarMixRedeems();

    public IReadOnlyList<TriggerRule> SelectedAvatarOtherRedeems => GetSelectedAvatarOtherRedeems();

    public string AvatarBoolRedeemGroupTitle => "Bool Parameters";

    public string AvatarIntRedeemGroupTitle => "Int Parameters";

    public string AvatarFloatRedeemGroupTitle => "Float Parameters";

    public string AvatarMixRedeemGroupTitle => T("Mix Parameters");

    public string AvatarOtherRedeemGroupTitle => "Other Redeems";

    public bool HasAvatarMixRedeems => SelectedAvatarMixRedeems.Count > 0;

    public bool HasAvatarOtherRedeems => SelectedAvatarOtherRedeems.Count > 0;

    public bool IsSetTriggerMasterRewardEditorVisible =>
        IsViewingAvatarTriggers
        && SelectedAvatarProfile?.ChannelPointRules.Any(rule => rule.ActionType == OscActionType.SetTrigger) == true;

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

    public string SelectedRuleCollectionTitle => IsViewingRewardFireSale
        ? T("Reward Fire Sale")
        : IsViewingAvatarScaling
        ? T("Avatar Scaling")
        : IsViewingUniversalTriggers
        ? T("Universal Triggers")
        : IsViewingSupporterOverrides
        ? T("Bits + Subs Overrides")
        : IsViewingMovementRedeems
            ? T("Movement Sets")
        : IsViewingMasterAvatar
            ? T("Avatar Change Redeems")
            : T("Avatar Redeems");

    public string SelectedRuleCollectionHelpText => IsViewingRewardFireSale
        ? T("Build a shared Bits and funding reward goal that discounts Crystal Relay-owned channel point redeems. Linked Twitch rewards stay listen-only and are never repriced.")
        : IsViewingAvatarScaling
        ? T("Use Scale Sets to organize VRChat OSC avatar height scaling. Scale redeems send /avatar/eyeheight and stay separate from avatar sets, movement, universal triggers, and paid overrides.")
        : IsViewingUniversalTriggers
        ? T("Use this list for imported or universal Twitch interactions. These triggers can listen for chat commands, channel point rewards, bits, subscriptions, gift subs, and follows without mixing into avatar sets or paid override rules.")
        : IsViewingSupporterOverrides
        ? T("Use this list for paid Twitch triggers like bits and subscriptions. Avatar Supporter Triggers are tied directly to one VRChat avatar, while Avatar Change Overrides stay global so outfit-name Bits triggers do not fight avatar swaps.")
        : IsViewingMovementRedeems
            ? T("Use Movement Sets to organize global movement redeems. The sets are folders only; every movement redeem still works across every avatar and keeps its existing Twitch reward link.")
        : IsViewingMasterAvatar
            ? T("Use this list for avatar-switch rules that belong to Avatar Change Setup. Add Avatar Switch creates a direct Avatar Change rule or an Avatar Roulette rule, Delete Avatar Switch removes the selected one, and Enable All or Disable All controls the full avatar-switch list. These rules only turn on while you are on the shared return avatar unless a timed avatar switch is already active.")
        : SelectedAvatarProfile is null
            ? T("Use Avatar Sets to build per-avatar rule groups. Add Avatar Set creates another avatar group, Delete Avatar Set removes the selected one, and Delete All Avatar Sets clears the full set list. Each set becomes active only when Crystal Relay detects that exact avatar.")
            : T("This list holds the rules for one avatar set. Pick the avatar once, then add and manage the redeems that should only turn on while you are using that avatar.");

    public string RuleLibraryHelpText => IsViewingRewardFireSale
        ? T("Reward Fire Sale tracks Bits and the optional Fire Sale funding reward toward a discount goal. The sale changes only Crystal Relay-created reward prices when the goal starts or ends.")
        : IsViewingAvatarScaling
        ? T("This tab is for avatar height scale redeems using VRChat OSC Avatar Scaling. Use Scale Sets to keep different height reward ideas organized without changing how the triggers run.")
        : IsViewingUniversalTriggers
        ? T("This tab is for universal Twitch interaction triggers. Import a Fooma Twitch Interaction config here, then test and adjust the OSC actions without changing the existing Avatar Sets, Movement Redeems, Avatar Change, or Bits + Subs override sections.")
        : IsViewingSupporterOverrides
        ? T("This tab is for paid Twitch triggers. Use Avatar Supporter Triggers for current-avatar bits/subs actions and Bits outfit Set Triggers, and keep avatar-change paid overrides in their own group.")
        : IsViewingMovementRedeems
            ? T("This tab is for organizing global movement redeems like forward, back, left, right, and spin. Movement Sets do not add avatar matching; they only keep the movement library easier to manage.")
            : IsViewingMasterAvatar
                ? T("This tab is for Avatar Change Setup. Pick the shared return avatar on the right, then build direct avatar swaps or Avatar Roulette rules here. Timed avatar-switch rules return to that shared return avatar when they finish.")
                : T("This tab is for Avatar Sets. Use it to group redeems by the avatar they belong to, then pick a set below to edit the rules inside it. Crystal Relay uses current-avatar detection so only the set for the avatar you are actually wearing turns on.");

    public string AddRuleButtonText => IsViewingRewardFireSale
        ? T("Add Fire Sale Tier")
        : IsViewingAvatarScaling
        ? T("Add Scale Redeem")
        : IsViewingUniversalTriggers
        ? T("Add Universal Trigger")
        : IsViewingSupporterOverrides
        ? T("Add Avatar Supporter Trigger")
        : IsViewingMovementRedeems
            ? T("Add Movement Redeem")
        : IsViewingMasterAvatar
            ? T("Add Avatar Switch")
            : T("Add Redeem");

    public string DeleteRuleButtonText => IsViewingRewardFireSale
        ? T("Delete Fire Sale Tier")
        : IsViewingAvatarScaling
        ? T("Delete Scale Redeem")
        : IsViewingUniversalTriggers
        ? T("Delete Universal Trigger")
        : IsViewingSupporterOverrides
        ? T("Delete Override")
        : IsViewingMovementRedeems
            ? T("Delete Movement Redeem")
        : IsViewingMasterAvatar
            ? T("Delete Avatar Switch")
            : T("Delete Redeem");

    public string DeleteAllRulesButtonText => IsViewingRewardFireSale
        ? T("Reset Fire Sale Progress")
        : IsViewingAvatarScaling
        ? T("Delete All Scale Sets")
        : IsViewingUniversalTriggers
        ? T("Delete All Universal Triggers")
        : IsViewingSupporterOverrides
        ? T("Delete All Overrides")
        : IsViewingMovementRedeems
            ? T("Delete All Movement Sets")
        : IsViewingMasterAvatar
            ? T("Delete All Avatar Switches")
            : T("Delete All Redeems");

    public string SelectedRuleEmptyStateText => IsViewingRewardFireSale
        ? T("Use the Reward Fire Sale setup to edit sale sources, tiers, and duration.")
        : IsViewingAvatarScaling
        ? T("Select or add a scale set, then add a scale redeem to edit it.")
        : IsViewingUniversalTriggers
        ? T("Import a Fooma config or add a universal trigger to edit it.")
        : IsViewingSupporterOverrides
        ? T("Add or select a bits/subs trigger to edit it.")
        : IsViewingMovementRedeems
            ? T("Select a movement set, then add a movement redeem to edit it.")
        : IsViewingMasterAvatar
            ? T("Add an avatar-switch redeem to edit it.")
        : SelectedAvatarProfile is null
            ? T("Select or create an avatar set first.")
            : T("Add a redeem in this avatar set to edit it.");

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

            return "Crystal Relay will create or link this Twitch redeem and keep it available in Test Mode or while live when the current avatar's OSC file supports this trigger.";
        }
    }

    public string UniversalManagedRewardStatusText
    {
        get
        {
            var trigger = SelectedUniversalTrigger;
            if (trigger is null || trigger.TriggerType != UniversalTriggerType.ChannelPointReward)
            {
                return string.Empty;
            }

            if (string.IsNullOrWhiteSpace(trigger.RewardTitle))
            {
                return WithUniversalManagedRewardSyncStatus("Set a reward name so Crystal Relay can create or link the Twitch redeem.");
            }

            if (!HasRuntimeReadyUniversalTriggerAction(trigger))
            {
                return WithUniversalManagedRewardSyncStatus("Add at least one complete OSC action before this reward can be managed on Twitch.");
            }

            var requiredParameters = GetUniversalTriggerRequiredAvatarParameterAddresses(trigger);
            if (requiredParameters.Count == 0)
            {
                return WithUniversalManagedRewardSyncStatus("No avatar-parameter gate is needed. This reward can show whenever Twitch redeems are active.");
            }

            var currentAvatarId = Settings.VrChat.CurrentAvatarId?.Trim() ?? string.Empty;
            if (string.IsNullOrWhiteSpace(currentAvatarId))
            {
                return WithUniversalManagedRewardSyncStatus("Current avatar unknown. Universal rewards that need avatar parameters stay hidden until Crystal Relay detects the current avatar.");
            }

            if (!CurrentAvatarLocalOscJsonExists(currentAvatarId)
                || !cachedVrChatParametersByAvatarId.ContainsKey(currentAvatarId))
            {
                return WithUniversalManagedRewardSyncStatus("Waiting for current avatar JSON. Universal rewards that need avatar parameters stay hidden until VRChat writes the current avatar's OSC file.");
            }

            var missingParameters = GetMissingCurrentAvatarParameters(requiredParameters);
            var foundParameterCount = requiredParameters.Count - missingParameters.Count;
            if (missingParameters.Count == requiredParameters.Count)
            {
                return WithUniversalManagedRewardSyncStatus($"Hidden on Twitch: current avatar is missing required parameters. Missing: {string.Join(", ", missingParameters.Take(4))}{(missingParameters.Count > 4 ? ", ..." : string.Empty)}.");
            }

            if (missingParameters.Count > 0)
            {
                return WithUniversalManagedRewardSyncStatus($"Partially ready for current avatar. Found {foundParameterCount} of {requiredParameters.Count} required OSC parameter(s). Missing: {string.Join(", ", missingParameters.Take(4))}{(missingParameters.Count > 4 ? ", ..." : string.Empty)}.");
            }

            return WithUniversalManagedRewardSyncStatus($"Ready for current avatar. Found {requiredParameters.Count} required OSC parameter(s).");
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

    public string SpecialRuleLockoutHelpText => T("Disable Pairing lets this redeem temporarily turn off other redeems in the same avatar set while it is active. Use it when two redeems would fight each other, overlap visually, or should behave like separate modes instead of stacking together.");

    public string SpecialRuleLockoutSummaryText
    {
        get
        {
            var configuredOptions = BuildConfiguredSpecialRuleLockoutOptions();
            if (configuredOptions.Count == 0)
            {
                return T("No disable pairings set.");
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

            if (IsViewingSupporterOverrides)
            {
                return GetSupporterActionTypeOptionsForSelectedRule();
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

                if (IsViewingUniversalTriggers)
                {
                    return T("Universal triggers run globally from Twitch events and send direct OSC actions.");
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
                if (string.IsNullOrWhiteSpace(SelectedAvatarProfile.AvatarId))
                {
                    return T("Pick the return avatar first. Timed avatar-change redeems will switch back to it when they finish.");
                }

                return SelectedAvatarProfile.IsCurrentAvatarActive
                    ? TF("Return avatar is {0}, and you are using it right now.", SelectedAvatarProfile.AvatarDisplayName)
                    : TF("Return avatar is {0}. Avatar Change and Avatar Roulette redeems turn on while you are on this avatar, and timed switches return here when they finish.", SelectedAvatarProfile.AvatarDisplayName);
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

    public string SelectedAvatarNameFieldLabel => T("Display Name");

    public string SelectedAvatarPickerLabel => IsViewingMasterAvatar
        ? T("Return Avatar")
        : T("This Avatar");

    public string UseCurrentAvatarButtonText => IsViewingMasterAvatar
        ? T("Use Current Avatar as Return Avatar")
        : T("Use Current VRChat Avatar");

    public string MasterAvatarReturnText => string.IsNullOrWhiteSpace(MasterAvatarProfile?.AvatarId)
        ? T("Pick the return avatar first so timed avatar-change redeems know where to switch back. Avatar-change redeems set to 0 seconds will become the new return avatar.")
        : TF("Timed avatar-change redeems will switch back to {0} when they finish. Avatar-change redeems set to 0 seconds will make their new avatar the return avatar instead.", MasterAvatarDisplayName);

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
                TestSelectedRuleCommand.NotifyCanExecuteChanged();
                SelectedSetTriggerAction = value?.SetTriggerActions.FirstOrDefault();
                AddSetTriggerActionCommand.NotifyCanExecuteChanged();
                RemoveSelectedSetTriggerActionCommand.NotifyCanExecuteChanged();
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

    public UniversalTriggerRule? SelectedUniversalTrigger
    {
        get => selectedUniversalTrigger;
        set
        {
            if (SetProperty(ref selectedUniversalTrigger, value))
            {
                lastSelectedUniversalTriggerId = value?.Id ?? Guid.Empty;
                SelectedUniversalTriggerAction = value?.Actions.FirstOrDefault();
                RemoveSelectedUniversalTriggerCommand.NotifyCanExecuteChanged();
                AddUniversalTriggerActionCommand.NotifyCanExecuteChanged();
                TestSelectedUniversalTriggerCommand.NotifyCanExecuteChanged();
                RaisePropertyChanged(nameof(SelectedUniversalTrigger));
                RaisePropertyChanged(nameof(UniversalManagedRewardStatusText));

                if (IsViewingUniversalTriggers)
                {
                    _ = EnsureSelectedAvatarParameterCacheLoadedAsync();
                }
            }
        }
    }

    public UniversalTriggerAction? SelectedUniversalTriggerAction
    {
        get => selectedUniversalTriggerAction;
        set
        {
            if (SetProperty(ref selectedUniversalTriggerAction, value))
            {
                RemoveSelectedUniversalTriggerActionCommand.NotifyCanExecuteChanged();
                RaisePropertyChanged(nameof(UniversalManagedRewardStatusText));
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
                lastSelectedMovementSetId = value?.Id ?? Guid.Empty;
                if (SelectedRule is not null
                    && value?.MovementRules.Contains(SelectedRule) != true
                    && IsViewingMovementRedeems)
                {
                    SelectedRule = GetRememberedMovementRule();
                }
                else if (SelectedRule is null && value is not null && IsViewingMovementRedeems)
                {
                    SelectedRule = GetRememberedMovementRule();
                }

                RemoveSelectedMovementRedeemSetCommand.NotifyCanExecuteChanged();
                AddRuleCommand.NotifyCanExecuteChanged();
                RaisePropertyChanged(nameof(SelectedMovementRedeemSet));
                RaisePropertyChanged(nameof(MovementRedeemRules));
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

    public string StreamingStatusSummary
    {
        get => streamingStatusSummary;
        private set => SetProperty(ref streamingStatusSummary, value);
    }

    public string StreamingStatusDetail
    {
        get => streamingStatusDetail;
        private set => SetProperty(ref streamingStatusDetail, value);
    }

    public string StreamingStatusVisualState
    {
        get => streamingStatusVisualState;
        private set => SetProperty(ref streamingStatusVisualState, value);
    }

    public string StreamingStreamStateText
    {
        get => streamingStreamStateText;
        private set => SetProperty(ref streamingStreamStateText, value);
    }

    public string StreamingStreamStateVisual
    {
        get => streamingStreamStateVisual;
        private set => SetProperty(ref streamingStreamStateVisual, value);
    }

    public string StreamingListenerStateText
    {
        get => streamingListenerStateText;
        private set => SetProperty(ref streamingListenerStateText, value);
    }

    public string StreamingListenerStateVisual
    {
        get => streamingListenerStateVisual;
        private set => SetProperty(ref streamingListenerStateVisual, value);
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

    public string VrChatAvatarStatus
    {
        get => vrChatAvatarStatus;
        private set => SetProperty(ref vrChatAvatarStatus, value);
    }

    public bool IsBroadcasterConnected => Settings.Broadcaster.IsConnected;

    public bool IsBroadcasterDisconnected => !IsBroadcasterConnected;

    public bool IsBotConnected => Settings.Bot.IsConnected;

    public bool IsBotDisconnected => !IsBotConnected;

    public string EffectiveBotSenderStatusText => BuildEffectiveBotSenderStatusText();

    public bool IsVrChatConnected => Settings.VrChat.IsConnected;

    public bool IsVrChatDisconnected => !IsVrChatConnected;

    public bool HasVrChatAvatarOptions => ProfileAvatarOptions.Count > 0;

    public bool IsHomeSectionSelected => activeSection == SectionView.Home;

    public bool IsSettingsSectionSelected => activeSection == SectionView.Settings;

    public bool IsActivitySectionSelected => activeSection == SectionView.Activity;

    public bool IsAboutSectionSelected => activeSection == SectionView.About;

    public bool HasLiveAboutProfiles => EnumerateAboutProfiles().Any(profile => profile.IsLive);

    public bool IsSettingsAccountsSectionSelected => activeSettingsSection == SettingsSectionView.Accounts;

    public bool IsSettingsVisualsSectionSelected => activeSettingsSection == SettingsSectionView.Visuals;

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

    public bool IsStinkyOnlineThemeSelected => SelectedTheme == AppTheme.StinkyOnline;

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

    public RelayCommand OpenTwitchDeveloperConsoleCommand { get; }

    public RelayCommand OpenSaveFolderCommand { get; }

    public RelayCommand OpenKoFiSupportCommand { get; }

    public AsyncRelayCommand OpenBugReportCommand { get; }

    public AsyncRelayCommand RefreshOscConnectionCommand { get; }

    public AsyncRelayCommand RefreshTwitchRewardsCommand { get; }

    public RelayCommand UnlinkTwitchRewardCommand { get; }

    public AsyncRelayCommand TestSelectedRuleCommand { get; }

    public RelayCommand ShowSettingsTestCommand { get; }

    public RelayCommand ShowHomeSectionCommand { get; }

    public RelayCommand ShowSettingsSectionCommand { get; }

    public RelayCommand ShowActivitySectionCommand { get; }

    public RelayCommand ShowAboutSectionCommand { get; }

    public RelayCommand OpenTwitchChatboxCommand { get; }

    public RelayCommand ShowSettingsAccountsSectionCommand { get; }

    public RelayCommand ShowSettingsVisualsSectionCommand { get; }

    public RelayCommand ShowAvatarTriggerRulesCommand { get; }

    public RelayCommand ShowMasterAvatarTabCommand { get; }

    public RelayCommand ShowMovementRedeemsCommand { get; }

    public RelayCommand ShowSupporterOverridesCommand { get; }

    public RelayCommand ShowUniversalTriggersCommand { get; }

    public RelayCommand ShowAvatarScalingCommand { get; }

    public RelayCommand ShowRewardFireSaleCommand { get; }

    public RelayCommand AddAvatarProfileCommand { get; }

    public RelayCommand DeleteSelectedAvatarProfileCommand { get; }

    public RelayCommand DeleteAllAvatarProfilesCommand { get; }

    public RelayCommand SetSelectedAvatarProfileAsMasterCommand { get; }

    public RelayCommand ToggleSelectedAvatarRewardTestOverrideCommand { get; }

    public RelayCommand ToggleEmergencyRedeemStopCommand { get; }

    public RelayCommand ToggleDesktopModeInputLockCommand { get; }

    public RelayCommand UseCurrentVrChatAvatarForProfileCommand { get; }

    public RelayCommand UseCurrentAvatarForSupporterRuleCommand { get; }

    public RelayCommand UseCurrentAvatarForAvatarChangeRuleCommand { get; }

    public AsyncRelayCommand RefreshVrChatOscParametersCommand { get; }

    public RelayCommand AddRuleCommand { get; }

    public RelayCommand SelectRuleCommand { get; }

    public RelayCommand AddAvatarSupporterTriggerCommand { get; }

    public RelayCommand AddAvatarChangeOverrideCommand { get; }

    public RelayCommand RemoveSelectedRuleCommand { get; }

    public RelayCommand EnableAllRulesCommand { get; }

    public RelayCommand DisableAllRulesCommand { get; }

    public RelayCommand DeleteAllRulesCommand { get; }

    public RelayCommand AddUniversalTriggerCommand { get; }

    public RelayCommand RemoveSelectedUniversalTriggerCommand { get; }

    public RelayCommand EnableAllUniversalTriggersCommand { get; }

    public RelayCommand DisableAllUniversalTriggersCommand { get; }

    public RelayCommand DeleteAllUniversalTriggersCommand { get; }

    public AsyncRelayCommand TestSelectedUniversalTriggerCommand { get; }

    public AsyncRelayCommand ImportFoomaInteractionConfigCommand { get; }

    public RelayCommand AddUniversalTriggerActionCommand { get; }

    public RelayCommand RemoveSelectedUniversalTriggerActionCommand { get; }

    public RelayCommand AddMovementRedeemSetCommand { get; }

    public RelayCommand RemoveSelectedMovementRedeemSetCommand { get; }

    public RelayCommand DeleteAllMovementRedeemSetsCommand { get; }

    public RelayCommand AddAvatarScaleSetCommand { get; }

    public RelayCommand RemoveSelectedAvatarScaleSetCommand { get; }

    public RelayCommand AddAvatarScaleRuleCommand { get; }

    public RelayCommand RemoveSelectedAvatarScaleRuleCommand { get; }

    public RelayCommand EnableAllAvatarScaleRulesCommand { get; }

    public RelayCommand DisableAllAvatarScaleRulesCommand { get; }

    public RelayCommand DeleteAllAvatarScaleRulesCommand { get; }

    public RelayCommand TestSelectedAvatarScaleRuleCommand { get; }

    public RelayCommand OpenAvatarScaleRuleLockoutPickerCommand { get; }

    public RelayCommand OpenSpecialRuleLockoutPickerCommand { get; }

    public RelayCommand OpenAvatarRouletPoolPickerCommand { get; }

    public RelayCommand AddSetTriggerActionCommand { get; }

    public RelayCommand RemoveSelectedSetTriggerActionCommand { get; }

    public RelayCommand CopySelectedAvatarParameterPathCommand { get; }

    public RelayCommand PasteSelectedAvatarParameterPathCommand { get; }

    public RelayCommand AddRewardFireSaleTierCommand { get; }

    public RelayCommand RemoveRewardFireSaleTierCommand { get; }

    public RelayCommand StopRewardFireSaleCommand { get; }

    public RelayCommand ResetRewardFireSaleProgressCommand { get; }

    // Startup flow for the main window. This loads saved data, rebuilds editor state,
    // restores helper caches, and runs cleanup recovery if the previous launch ended badly.
    public async Task InitializeAsync()
    {
        var previousSessionNeedsRecovery = ShutdownRecoveryStateStore.BeginSession();
        ReplaceSettings(await settingsStore.LoadAsync());
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

        EnsureRuleCollectionsHaveStarterContent();
        NormalizeMasterAvatarProfiles();
        RefreshAvatarRuleProfilesList();
        RelocateMisplacedMovementRules();
        RemoveUnsupportedMovementRules();
        var normalizedChatCommandFallbacks = NormalizeChatCommandFallbackRules();
        var fusedUniversalCommandFallbacks = UniversalTriggerFusionService.FuseMatchingCommandFallbacks(Settings.UniversalTriggers);
        NormalizeAvatarProfileRules();
        var normalizedSupporterAvatarScopes = NormalizeSupporterAvatarScopes();
        var normalizedRewardFireSale = NormalizeRewardFireSaleSettings();
        RestoreRewardFireSaleStartupState();
        UpdateAvatarProfileActivityStates();
        SelectedAvatarProfile = AvatarRuleProfiles.FirstOrDefault();
        SelectedRule = SelectedAvatarProfile?.ChannelPointRules.FirstOrDefault();
        RefreshAvailableActionTypes();
        RefreshVrChatAvatarSelectionOptions();
        _ = EnsureSelectedAvatarParameterCacheLoadedAsync();
        RefreshRuntimeSummary();
        UpdateAccountStatuses();
        isInitialized = true;
        ScheduleRewardFireSaleExpirationIfNeeded();
        if (normalizedChatCommandFallbacks || fusedUniversalCommandFallbacks > 0 || normalizedSupporterAvatarScopes || normalizedRewardFireSale)
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
        await InitializeVrChatAsync();
        await QueueRewardRefreshAsync();
        QueueManagedRewardSync(reason: ManagedRewardSyncReason.Startup);
        await aboutProfilesRefreshTask;
        QueueBridgeRefresh();
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

        saveDebounceCancellation?.Cancel();
        saveDebounceCancellation?.Dispose();
        bridgeRefreshCancellation?.Cancel();
        bridgeRefreshCancellation?.Dispose();
        CancelAndDisposeQueuedCancellationSource(ref managedRewardSyncCancellation);
        CancelAndDisposeQueuedCancellationSource(ref vrChatCurrentAvatarRefreshCancellation);
        CancelAndDisposeQueuedCancellationSource(ref vrChatOscParameterRefreshCancellation);
        CancelAndDisposeQueuedCancellationSource(ref vrChatLocalOscScanCancellation);
        CancelAndDisposeQueuedCancellationSource(ref activeAvatarScaleLocalRefreshCancellation);
        CancelAndDisposeQueuedCancellationSource(ref rewardFireSaleExpirationCancellation);
        CancelAndDisposeQueuedCancellationSource(ref rewardFireSaleFundingCooldownCancellation);
        DisposeVrChatLocalOscWatcher();

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
        vrChatApiClient.Dispose();
        sessionStatusTimer.Stop();
        vrChatLocalStateTimer.Stop();
        vrChatCurrentAvatarTimer.Stop();
        bridgeRefreshGate.Dispose();
        managedRewardSyncGate.Dispose();
        vrChatLocalStateRefreshGate.Dispose();
        vrChatCurrentAvatarRefreshGate.Dispose();

        if (!isInitialized || shutdownCleanupCompleted)
        {
            ShutdownRecoveryStateStore.CompleteSession();
        }
    }

    private void ResetStartupSectionState()
    {
        activeSection = SectionView.Home;
        activeSettingsSection = SettingsSectionView.Accounts;
        RaisePropertyChanged(nameof(IsHomeSectionSelected));
        RaisePropertyChanged(nameof(IsSettingsSectionSelected));
        RaisePropertyChanged(nameof(IsActivitySectionSelected));
        RaisePropertyChanged(nameof(IsAboutSectionSelected));
        RaisePropertyChanged(nameof(IsSettingsAccountsSectionSelected));
        RaisePropertyChanged(nameof(IsSettingsVisualsSectionSelected));
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

        if (section == SectionView.About
            && (DateTimeOffset.UtcNow - aboutProfilesLastRefreshedAt >= AboutProfileRefreshInterval
                || EnumerateAboutProfiles().Any(profile => !profile.HasProfileImage)))
        {
            _ = RefreshAboutProfilesAsync();
        }
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
        if (!SetProperty(ref activeSettingsSection, section, nameof(IsSettingsAccountsSectionSelected)))
        {
            return;
        }

        RaisePropertyChanged(nameof(IsSettingsAccountsSectionSelected));
        RaisePropertyChanged(nameof(IsSettingsVisualsSectionSelected));
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
        twitchChatboxWindow.Show();
        UpdateChatboxListenerStatus();
    }

    private void OnTwitchChatboxClosed(object? sender, EventArgs e)
    {
        if (twitchChatboxWindow is not null)
        {
            twitchChatboxWindow.Closed -= OnTwitchChatboxClosed;
        }

        twitchChatboxWindow = null;
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
        RaisePropertyChanged(nameof(IsStinkyOnlineThemeSelected));
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
        var loginWindow = new VrChatLoginWindow(SelectedTheme)
        {
            Owner = Application.Current?.MainWindow
        };

        if (loginWindow.ShowDialog() != true)
        {
            return;
        }

        VrChatStatus = T("Connecting to VRChat...");
        VrChatAvatarStatus = T("Waiting for VRChat login to finish.");

        try
        {
            var loginResponse = await vrChatApiClient.LoginWithCredentialsAsync(
                loginWindow.VrChatUsername,
                loginWindow.VrChatPassword,
                CancellationToken.None);

            var account = loginResponse.Account;
            if (loginResponse.RequiredTwoFactorMethods.Count > 0)
            {
                var twoFactorWindow = new VrChatTwoFactorWindow(SelectedTheme, loginResponse.RequiredTwoFactorMethods)
                {
                    Owner = Application.Current?.MainWindow
                };

                if (twoFactorWindow.ShowDialog() != true)
                {
                    VrChatStatus = T("VRChat login cancelled before 2FA completed.");
                    VrChatAvatarStatus = T("Connect VRChat again when you want to load avatars.");
                    await SafeVrChatLogoutAsync(loginResponse.AuthCookie);
                    return;
                }

                account = await vrChatApiClient.CompleteTwoFactorAsync(
                    loginResponse.AuthCookie,
                    twoFactorWindow.SelectedMethod,
                    twoFactorWindow.VerificationCode,
                    CancellationToken.None);
            }

            if (account is null)
            {
                throw new InvalidOperationException("VRChat login completed, but no account details were returned.");
            }

            Settings.VrChat.Apply(account);
            RaiseVrChatConnectionStateProperties();
            SyncVrChatRuntimeState(queueManagedRewardSync: false);
            QueueSave();
            AppendLog(TF("Connected VRChat avatar access as {0}.", account.DisplayName));
            StartOrRefreshVrChatLocalOscWatcher();
            QueueLocalVrChatOscAvatarScan(0);
            QueueCurrentVrChatLocalStateRefresh(0);
            await RefreshVrChatAvatarsAsync(forceRemoteRefresh: true);
        }
        catch (Exception ex)
        {
            ClearVrChatAccountPreservingCurrentAvatar();
            RaiseVrChatConnectionStateProperties();
            SyncVrChatRuntimeState(queueManagedRewardSync: false);
            VrChatStatus = GetFriendlyVrChatError(ex);
            VrChatAvatarStatus = T("VRChat avatar list is unavailable until login succeeds.");
            AppendLog(VrChatStatus);
            QueueSave();
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
        ClearBroadcasterManagedRewardsUnavailableForSession();
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
        await settingsStore.ClearVrChatAvatarCacheAsync(CancellationToken.None);
        await settingsStore.ClearVrChatOscParameterCacheAsync(CancellationToken.None);
        ClearVrChatAccountPreservingCurrentAvatar();
        ClearAvailableVrChatAvatars();
        cachedVrChatParametersByAvatarId.Clear();
        AvatarParameterOptions.Clear();
        SetTriggerParameterOptions.Clear();
        SelectedAvatarParameterOption = null;
        SelectedSetTriggerParameterOption = null;
        VrChatOscParameterStatus = T("Connect VRChat to load avatar parameters.");
        RaiseVrChatConnectionStateProperties();
        VrChatStatus = T("VRChat avatar access is not connected.");
        VrChatAvatarStatus = T("Connect VRChat to load avatar choices.");
        SyncVrChatRuntimeState();
        ResetVrChatLocalRuntimeTracking();
        DisposeVrChatLocalOscWatcher();
        QueueSave();
        RefreshCommandStates();
        UpdateAvatarProfileActivityStates();
        RefreshVrChatAvatarSelectionOptions();
        AppendLog(T("Disconnected VRChat avatar access."));
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
        RaisePropertyChanged(nameof(UniversalManagedRewardStatusText));
        QueueBridgeRefresh();
        if (queueManagedRewardSync)
        {
            QueueManagedRewardSync(0);
        }
    }

    private void ClearVrChatAccountPreservingCurrentAvatar()
    {
        var currentAvatarId = GetBestKnownCurrentAvatarId();
        Settings.VrChat.Clear();
        if (!string.IsNullOrWhiteSpace(currentAvatarId))
        {
            Settings.VrChat.CurrentAvatarId = currentAvatarId;
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
                Settings.Broadcaster.Apply(accountSettings);
                ClearBroadcasterDeviceFlow();
            }
            else
            {
                Settings.Bot.Apply(accountSettings);
                ClearBotDeviceFlow();
            }

            UpdateAccountStatuses();
            QueueSave();
            QueueBridgeRefresh();
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
            VrChatStatus = T("VRChat avatar access is not connected.");
            VrChatAvatarStatus = T("Connect VRChat to load avatar choices.");
            VrChatOscParameterStatus = T("Connect VRChat to load avatar parameters.");
            ResetVrChatLocalRuntimeTracking();
            DisposeVrChatLocalOscWatcher();
            RefreshVrChatAvatarSelectionOptions();
            return;
        }

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

        if (cachedAvatars.Count > 0)
        {
            VrChatStatus = TF("Connected to VRChat as {0}.", Settings.VrChat.DisplayName);
            VrChatAvatarStatus = TF("Loaded {0} saved avatars. Checking VRChat once for updates...", cachedAvatars.Count);
            SyncVrChatAvatarRuleLabels();
            RefreshVrChatAvatarSelectionOptions();
        }

        if (cachedAvatars.Count == 0)
        {
            VrChatStatus = TF("Connected to VRChat as {0}.", Settings.VrChat.DisplayName);
            VrChatAvatarStatus = T("Pulling your VRChat avatar list...");
            SyncVrChatAvatarRuleLabels();
            RefreshVrChatAvatarSelectionOptions();
        }

        await EnsureSelectedAvatarParameterCacheLoadedAsync();
        await RefreshVrChatAvatarsAsync(forceRemoteRefresh: true);
    }

    private async Task RefreshVrChatAvatarsAsync()
    {
        await RefreshVrChatAvatarsAsync(forceRemoteRefresh: true);
    }

    private async Task RefreshVrChatAvatarsAsync(bool forceRemoteRefresh)
    {
        if (!Settings.VrChat.IsConnected)
        {
            VrChatStatus = T("VRChat avatar access is not connected.");
            VrChatAvatarStatus = T("Connect VRChat to load avatar choices.");
            ResetVrChatLocalRuntimeTracking();
            DisposeVrChatLocalOscWatcher();
            RefreshVrChatAvatarSelectionOptions();
            RefreshCommandStates();
            return;
        }

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
        }
        catch (Exception ex)
        {
            if (ex is VrChatApiException apiException
                && apiException.StatusCode == System.Net.HttpStatusCode.Unauthorized)
            {
                ClearVrChatAccountPreservingCurrentAvatar();
                ClearAvailableVrChatAvatars();
                await settingsStore.ClearVrChatAvatarCacheAsync(CancellationToken.None);
                await settingsStore.ClearVrChatOscParameterCacheAsync(CancellationToken.None);
                cachedVrChatParametersByAvatarId.Clear();
                AvatarParameterOptions.Clear();
                SetTriggerParameterOptions.Clear();
                SelectedAvatarParameterOption = null;
                SelectedSetTriggerParameterOption = null;
                RaiseVrChatConnectionStateProperties();
                SyncVrChatRuntimeState(queueManagedRewardSync: false);
                VrChatStatus = T("Saved VRChat session expired. Connect again to reload avatars.");
                VrChatAvatarStatus = T("VRChat avatar list is unavailable until you reconnect.");
                VrChatOscParameterStatus = T("Reconnect VRChat to load avatar parameters again.");
                ResetVrChatLocalRuntimeTracking();
                DisposeVrChatLocalOscWatcher();
                AppendLog(T("Saved VRChat avatar session expired and was cleared."));
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
            var needsProfileAvatarOptions = !IsViewingSupporterOverrides;
            var needsSupporterAvatarOptions = IsViewingSupporterOverrides
                && SelectedRule is not null
                && !IsSupporterAvatarChangeOverride(SelectedRule);
            var needsAvatarChangeOptions = SelectedRule?.ActionType == OscActionType.AvatarChange;
            if (!needsProfileAvatarOptions && !needsSupporterAvatarOptions && !needsAvatarChangeOptions)
            {
                RaisePropertyChanged(nameof(HasVrChatAvatarOptions));
                return;
            }

            var selectedProfileAvatarId = SelectedAvatarProfile?.AvatarId;
            var selectedProfileAvatarName = SelectedAvatarProfile?.AvatarName;
            var selectedAvatarId = SelectedRule?.ActionType == OscActionType.AvatarChange
                ? SelectedRule.AvatarChangeTargetId
                : needsSupporterAvatarOptions
                    ? SelectedRule?.SupporterAvatarId ?? string.Empty
                    : string.Empty;
            var selectedResetAvatarId = SelectedRule?.ActionType == OscActionType.AvatarChange
                ? SelectedRule.AvatarChangeResetId
                : string.Empty;
            var selectedAvatarName = SelectedRule?.ActionType == OscActionType.AvatarChange
                ? SelectedRule.AvatarTargetName
                : needsSupporterAvatarOptions
                    ? SelectedRule?.SupporterAvatarName ?? string.Empty
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
                        Settings.VrChat.IsConnected
                            ? BuildVrChatAvatarOptionSet(selectedProfileAvatarId, selectedProfileAvatarName, "Selected avatar")
                            : []);
                }

                if (needsAvatarChangeOptions || needsSupporterAvatarOptions)
                {
                    ReplaceCollectionIfChanged(
                        VrChatAvatarOptions,
                        Settings.VrChat.IsConnected
                            ? BuildVrChatAvatarOptionSet(
                                selectedAvatarId,
                                selectedAvatarName,
                                needsSupporterAvatarOptions ? "Selected supporter avatar" : "Selected target avatar")
                            : []);
                }

                if (needsAvatarChangeOptions)
                {
                    var resetOptions = new List<VrChatAvatarOption>
                    {
                        new(string.Empty, string.Empty, "Do not switch back", "Do not switch back", string.Empty, false)
                    };

                    if (Settings.VrChat.IsConnected)
                    {
                        resetOptions.AddRange(BuildVrChatAvatarOptionSet(selectedResetAvatarId, selectedResetAvatarName, "Selected return avatar"));
                    }

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

    private string ResolveVrChatAvatarName(string? avatarId)
    {
        var normalizedId = avatarId?.Trim() ?? string.Empty;
        if (string.IsNullOrWhiteSpace(normalizedId))
        {
            return string.Empty;
        }

        if (!availableVrChatAvatarNamesById.TryGetValue(normalizedId, out var avatarName))
        {
            return string.Empty;
        }

        var normalizedName = avatarName?.Trim() ?? string.Empty;
        return string.IsNullOrWhiteSpace(normalizedName)
            || string.Equals(normalizedName, normalizedId, StringComparison.Ordinal)
            ? string.Empty
            : normalizedName;
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

        return string.IsNullOrWhiteSpace(ResolveVrChatAvatarName(currentAvatarId))
            ? string.Empty
            : currentAvatarId;
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
        var duplicateNameKeys = availableVrChatAvatars
            .GroupBy(
                avatar => avatar.Name?.Trim() ?? string.Empty,
                StringComparer.OrdinalIgnoreCase)
            .Where(group => !string.IsNullOrWhiteSpace(group.Key) && group.Count() > 1)
            .Select(group => group.Key)
            .ToHashSet(StringComparer.OrdinalIgnoreCase);

        return availableVrChatAvatars
            .Select(avatar => CreateVrChatAvatarOption(avatar, duplicateNameKeys))
            .OrderBy(option => option.DisplayLabel, StringComparer.OrdinalIgnoreCase)
            .ToArray();
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

    private void ShowMasterAvatarTab()
    {
        EnsureMasterAvatarProfileExists();
        var profile = MasterAvatarProfile;
        ApplyMasterAvatarDefaults(profile);
        var rule = GetRememberedMasterRule();
        SwitchRuleView(RuleListView.MasterAvatar, profile, rule);
    }

    private void ShowMovementRedeems()
    {
        EnsureSelectedMovementRedeemSet();
        SelectedMovementRedeemSet = GetRememberedMovementRedeemSet();
        var rule = GetRememberedMovementRule();
        SwitchRuleView(RuleListView.MovementRedeems, profile: null, rule);
    }

    private void ShowSupporterOverrides()
    {
        SwitchRuleView(RuleListView.SupporterOverrides, profile: null, GetRememberedSupporterRule());
    }

    private void ShowUniversalTriggers()
    {
        SwitchRuleView(RuleListView.UniversalTriggers, profile: null, rule: null);
        SelectedUniversalTrigger = GetRememberedUniversalTrigger();
        _ = EnsureSelectedAvatarParameterCacheLoadedAsync();
        QueueManagedRewardSync(0);
    }

    private void ShowAvatarScaling()
    {
        SwitchRuleView(RuleListView.AvatarScaling, profile: null, rule: null);
        SelectedAvatarScaleSet = GetRememberedAvatarScaleSet();
        SelectedAvatarScaleRule = GetRememberedAvatarScaleRule();
        QueueManagedRewardSync(0);
    }

    private void ShowRewardFireSale()
    {
        SwitchRuleView(RuleListView.RewardFireSale, profile: null, rule: null);
        SelectedUniversalTrigger = null;
        SelectedAvatarScaleSet = null;
        SelectedAvatarScaleRule = null;
        EnsureRewardFireSaleTierExists();
        RefreshRewardFireSaleStateProperties();
    }

    private bool NormalizeRewardFireSaleSettings()
    {
        var changed = false;
        var fireSale = Settings.RewardFireSale;
        if (fireSale.Tiers.Count == 0)
        {
            fireSale.Tiers.Add(new RewardFireSaleTier());
            changed = true;
        }

        foreach (var tier in fireSale.Tiers)
        {
            var goal = tier.GoalAmount;
            var discount = tier.DiscountPercent;
            tier.GoalAmount = Math.Max(1, goal);
            tier.DiscountPercent = Math.Clamp(discount, 1, 100);
            changed |= goal != tier.GoalAmount || discount != tier.DiscountPercent;
        }

        if (fireSale.TemporaryDurationSeconds <= 0)
        {
            fireSale.TemporaryDurationSeconds = 300;
            changed = true;
        }

        var fundingTitle = fireSale.FundingRewardTitle;
        fireSale.FundingRewardTitle = string.IsNullOrWhiteSpace(fundingTitle) ? "Fire Sale Fund" : fundingTitle.Trim();
        changed |= !string.Equals(fundingTitle, fireSale.FundingRewardTitle, StringComparison.Ordinal);

        fireSale.FundingRewardDescription ??= string.Empty;

        var fundingCost = fireSale.FundingRewardCost;
        fireSale.FundingRewardCost = Math.Max(1, fundingCost <= 0 ? 100 : fundingCost);
        changed |= fundingCost != fireSale.FundingRewardCost;

        var fundingCooldown = fireSale.FundingRewardCooldownSeconds;
        fireSale.FundingRewardCooldownSeconds = Math.Max(0, fundingCooldown);
        changed |= fundingCooldown != fireSale.FundingRewardCooldownSeconds;

        var fundingReadyColor = fireSale.FundingRewardReadyColor;
        fireSale.FundingRewardReadyColor = ManagedRewardPresentation.NormalizeReadyBackgroundColor(fundingReadyColor);
        changed |= !string.Equals(fundingReadyColor, fireSale.FundingRewardReadyColor, StringComparison.OrdinalIgnoreCase);

        var fundingCooldownColor = fireSale.FundingRewardCooldownColor;
        fireSale.FundingRewardCooldownColor = ManagedRewardPresentation.NormalizeCooldownBackgroundColor(fundingCooldownColor);
        changed |= !string.Equals(fundingCooldownColor, fireSale.FundingRewardCooldownColor, StringComparison.OrdinalIgnoreCase);

        var conversion = fireSale.RewardPointsPerProgressUnit;
        fireSale.RewardPointsPerProgressUnit = Math.Max(1, conversion <= 0 ? 10 : conversion);
        changed |= conversion != fireSale.RewardPointsPerProgressUnit;

        return changed;
    }

    private void RestoreRewardFireSaleStartupState()
    {
        var fireSale = Settings.RewardFireSale;
        if (!fireSale.IsSaleActive)
        {
            return;
        }

        if (fireSale.SaleMode == RewardFireSaleMode.Temporary
            && fireSale.ActiveUntilUtc is { } activeUntil
            && activeUntil <= DateTimeOffset.UtcNow)
        {
            fireSale.IsSaleActive = false;
            fireSale.ActiveDiscountPercent = 0;
            fireSale.ActiveTierGoalAmount = 0;
            fireSale.ActiveUntilUtc = null;
            AppendLog("Reward Fire Sale expired while Crystal Relay was closed. Normal reward prices will be restored on the next reward sync.");
        }
    }

    private void EnsureRewardFireSaleTierExists()
    {
        if (Settings.RewardFireSale.Tiers.Count > 0)
        {
            return;
        }

        Settings.RewardFireSale.Tiers.Add(new RewardFireSaleTier());
    }

    private void AddRewardFireSaleTier()
    {
        var lastTier = Settings.RewardFireSale.Tiers
            .OrderBy(tier => tier.GoalAmount)
            .LastOrDefault();
        var nextGoal = lastTier is null ? 5000 : Math.Max(1, lastTier.GoalAmount + 5000);
        var nextDiscount = lastTier is null ? 25 : Math.Clamp(lastTier.DiscountPercent + 10, 1, 100);
        Settings.RewardFireSale.Tiers.Add(new RewardFireSaleTier
        {
            GoalAmount = nextGoal,
            DiscountPercent = nextDiscount
        });
        AppendLog($"Added Reward Fire Sale tier {nextGoal:N0} = {nextDiscount}% off.");
    }

    private void RemoveRewardFireSaleTier(object? target)
    {
        if (target is not RewardFireSaleTier tier || Settings.RewardFireSale.Tiers.Count <= 1)
        {
            return;
        }

        Settings.RewardFireSale.Tiers.Remove(tier);
        AppendLog($"Removed Reward Fire Sale tier {tier.GoalAmount:N0} = {tier.DiscountPercent}% off.");
    }

    private void ResetRewardFireSaleProgress()
    {
        Settings.RewardFireSale.CurrentProgress = 0;
        RefreshRewardFireSaleStateProperties();
        QueueSave();
        AppendLog("Reward Fire Sale progress reset.");
    }

    private void StopRewardFireSale()
    {
        StopRewardFireSale(expired: false);
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
        RefreshRewardFireSaleStateProperties();
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

        suppressRewardFireSaleChangeSideEffects = true;
        try
        {
            CancelAndDisposeQueuedCancellationSource(ref rewardFireSaleExpirationCancellation);
            fireSale.IsSaleActive = false;
            fireSale.ActiveDiscountPercent = 0;
            fireSale.ActiveTierGoalAmount = 0;
            fireSale.ActiveUntilUtc = null;
            fireSale.CurrentProgress = 0;
        }
        finally
        {
            suppressRewardFireSaleChangeSideEffects = false;
        }

        RefreshRewardFireSaleStateProperties();
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
            AppendThrottledLog(
                "reward-fire-sale-active-progress-paused",
                "Reward Fire Sale is already active at its final available tier, so new Bits and funding reward redeems are not adding progress right now.",
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
        RefreshRewardFireSaleStateProperties();
        QueueSave();
        return isFundingReward;
    }

    private int ResolveRewardFireSaleContributionAmount(RewardFireSaleContribution contribution)
    {
        var fireSale = Settings.RewardFireSale;
        if (contribution.Type == RewardFireSaleContributionType.Bits)
        {
            return fireSale.CountBits ? Math.Max(0, contribution.Amount) : 0;
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

        suppressRewardFireSaleChangeSideEffects = true;
        try
        {
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
        }
        finally
        {
            suppressRewardFireSaleChangeSideEffects = false;
        }

        if (!saleWasActive)
        {
            ScheduleRewardFireSaleExpirationIfNeeded();
        }

        RefreshRewardFireSaleStateProperties();
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

    private void RefreshRewardFireSaleStateProperties()
    {
        RaisePropertyChanged(nameof(RewardFireSaleStatusText));
        RaisePropertyChanged(nameof(RewardFireSaleProgressPercent));
        RaisePropertyChanged(nameof(IsRewardFireSaleTemporary));
        RaisePropertyChanged(nameof(RewardFireSaleFundingRewardConversionText));
        RaisePropertyChanged(nameof(RewardFireSaleFundingRewardPrompt));
        StopRewardFireSaleCommand.NotifyCanExecuteChanged();
        ResetRewardFireSaleProgressCommand.NotifyCanExecuteChanged();
        RemoveRewardFireSaleTierCommand.NotifyCanExecuteChanged();
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

        var (destinationName, warningMessage) = GetDeleteAllWarningContent();
        var warningResult = ThemedDialogWindow.ShowYesNo(
            Application.Current?.MainWindow,
            SelectedTheme,
            "Delete All Avatar Sets",
            warningMessage);

        if (!warningResult)
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
        AppendLog($"Yeeted {profilesToRemove.Length} avatar set{(profilesToRemove.Length == 1 ? string.Empty : "s")} to the {destinationName}.");
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

    private bool CanUseCurrentAvatarForSupporterRule()
    {
        return IsViewingSupporterOverrides
            && SelectedRule is not null
            && !IsSupporterAvatarChangeOverride(SelectedRule)
            && !string.IsNullOrWhiteSpace(Settings.VrChat.CurrentAvatarId);
    }

    private void UseCurrentAvatarForSupporterRule()
    {
        if (!CanUseCurrentAvatarForSupporterRule() || SelectedRule is null)
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

    private bool CanUseCurrentAvatarForAvatarChangeRule()
    {
        return IsViewingSupporterOverrides
            && SelectedRule?.ActionType == OscActionType.AvatarChange
            && !string.IsNullOrWhiteSpace(Settings.VrChat.CurrentAvatarId);
    }

    private void UseCurrentAvatarForAvatarChangeRule()
    {
        if (!CanUseCurrentAvatarForAvatarChangeRule() || SelectedRule is null)
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
        SelectedRule.AvatarChangeTargetId = currentAvatarId;
        SelectedRule.AvatarTargetName = resolvedName;
        RefreshVrChatAvatarSelectionOptions();
        QueueSave();
        QueueBridgeRefresh();
        AppendLog($"Set avatar change override '{SelectedRule.DisplayTitle}' target to current avatar '{GetSafeVrChatAvatarDisplayName(resolvedName, currentAvatarId)}'.");
    }

    private void ApplySharedReturnAvatarSelection(string avatarId, string? avatarName, bool saveImmediately)
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
            || !string.Equals(masterProfile.AvatarName?.Trim() ?? string.Empty, resolvedName, StringComparison.Ordinal);

        masterProfile.AvatarId = normalizedAvatarId;
        masterProfile.AvatarName = resolvedName;

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
        var rule = IsViewingSupporterOverrides
            ? CreateDefaultOverrideRule()
            : IsViewingMovementRedeems
                ? CreateDefaultMovementRule()
            : IsViewingMasterAvatar
                ? CreateDefaultMasterAvatarRule()
                : CreateDefaultAvatarProfileRule();

        if (IsViewingSupporterOverrides)
        {
            Settings.GlobalOverrideRules.Add(rule);
        }
        else if (IsViewingMovementRedeems)
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
        AppendLog(IsViewingSupporterOverrides
            ? $"Added override '{rule.DisplayTitle}'."
            : IsViewingMovementRedeems
                ? $"Added movement redeem '{rule.DisplayTitle}'."
                : IsViewingMasterAvatar
                    ? $"Added master trigger '{rule.DisplayTitle}'."
            : $"Added trigger '{rule.DisplayTitle}' to '{SelectedAvatarProfile?.DisplayTitle}'.");
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
        if (!TryResolveDefaultSupporterAvatar(out var avatarId, out var avatarName))
        {
            AppendLog("Connect VRChat or refresh your avatar list before adding an Avatar Supporter Trigger.");
            return;
        }

        var rule = CreateDefaultAvatarSupporterRule(avatarId, avatarName);
        Settings.GlobalOverrideRules.Add(rule);
        SelectedRule = rule;
        QueueSave();
        QueueBridgeRefresh();
        RaiseSupporterRuleGroupProperties();
        AppendLog($"Added avatar supporter trigger '{rule.DisplayTitle}' for '{FormatSupporterAvatarScopeLabel(avatarId, avatarName)}'.");
    }

    private void AddAvatarChangeOverride()
    {
        var rule = CreateDefaultAvatarChangeOverrideRule();
        Settings.GlobalOverrideRules.Add(rule);
        SelectedRule = rule;
        QueueSave();
        QueueBridgeRefresh();
        RaiseSupporterRuleGroupProperties();
        AppendLog($"Added avatar change override '{rule.DisplayTitle}'.");
    }

    private void RemoveSelectedRule()
    {
        if (SelectedRule is null)
        {
            return;
        }

        var removedName = SelectedRule.DisplayTitle;
        var removedRule = SelectedRule;
        if (IsViewingSupporterOverrides)
        {
            Settings.GlobalOverrideRules.Remove(SelectedRule);
            SelectedRule = Settings.GlobalOverrideRules.FirstOrDefault();
        }
        else if (IsViewingMovementRedeems)
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

        if (!IsViewingSupporterOverrides && !IsViewingMovementRedeems)
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
        var (destinationName, warningMessage) = GetDeleteAllWarningContent();
        var warningResult = ThemedDialogWindow.ShowYesNo(
            Application.Current?.MainWindow,
            SelectedTheme,
            "Delete All Rules",
            warningMessage);

        if (!warningResult)
        {
            return;
        }

        var currentRules = GetCurrentEditableRuleCollection();
        var removedCount = currentRules.Count;
        if (!IsViewingSupporterOverrides && !IsViewingMovementRedeems)
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
        AppendLog($"Yeeted {removedCount} rules to the {destinationName}.");
    }

    private void AddUniversalTrigger()
    {
        var trigger = CreateDefaultUniversalTrigger();
        Settings.UniversalTriggers.Add(trigger);
        SelectedUniversalTrigger = trigger;
        SelectedUniversalTriggerAction = trigger.Actions.FirstOrDefault();
        QueueSave();
        QueueBridgeRefresh();
        AppendLog($"Added universal trigger '{trigger.DisplayTitle}'.");
    }

    private void AddMovementRedeemSet()
    {
        var set = CreateDefaultMovementRedeemSet();
        Settings.MovementRedeemSets.Add(set);
        SelectedMovementRedeemSet = set;
        SelectedRule = set.MovementRules.FirstOrDefault();
        AppendLog($"Added movement set '{set.DisplayTitle}'.");
    }

    private void RemoveSelectedMovementRedeemSet()
    {
        if (SelectedMovementRedeemSet is null)
        {
            return;
        }

        var removedSet = SelectedMovementRedeemSet;
        var removedName = removedSet.DisplayTitle;
        var removedRules = removedSet.MovementRules.ToArray();
        ForgetRememberedRules(removedRules);
        Settings.MovementRedeemSets.Remove(removedSet);

        if (lastSelectedMovementSetId == removedSet.Id)
        {
            lastSelectedMovementSetId = Guid.Empty;
        }

        SelectedMovementRedeemSet = GetRememberedMovementRedeemSet();
        SelectedRule = GetRememberedMovementRule();
        QueueSave();
        QueueBridgeRefresh();
        AppendLog($"Removed movement set '{removedName}'.");
    }

    private void DeleteAllMovementRedeemSets()
    {
        var setsToRemove = Settings.MovementRedeemSets.ToArray();
        if (setsToRemove.Length == 0)
        {
            return;
        }

        var (destinationName, warningMessage) = GetDeleteAllWarningContent();
        var warningResult = ThemedDialogWindow.ShowYesNo(
            Application.Current?.MainWindow,
            SelectedTheme,
            "Delete All Movement Sets",
            warningMessage);

        if (!warningResult)
        {
            return;
        }

        foreach (var set in setsToRemove)
        {
            var removedRules = set.MovementRules.ToArray();
            ForgetRememberedRules(removedRules);
            Settings.MovementRedeemSets.Remove(set);
        }

        lastSelectedMovementSetId = Guid.Empty;
        SelectedMovementRedeemSet = null;
        SelectedRule = null;
        QueueSave();
        QueueBridgeRefresh();
        AppendLog($"Yeeted {setsToRemove.Length} movement set{(setsToRemove.Length == 1 ? string.Empty : "s")} to the {destinationName}.");
    }

    private void RemoveSelectedUniversalTrigger()
    {
        if (SelectedUniversalTrigger is null)
        {
            return;
        }

        var removedName = SelectedUniversalTrigger.DisplayTitle;
        Settings.UniversalTriggers.Remove(SelectedUniversalTrigger);
        SelectedUniversalTrigger = GetRememberedUniversalTrigger();
        QueueSave();
        QueueBridgeRefresh();
        AppendLog($"Removed universal trigger '{removedName}'.");
    }

    private void EnableAllUniversalTriggers()
    {
        foreach (var trigger in Settings.UniversalTriggers.Where(trigger => !trigger.IsEnabled))
        {
            trigger.IsEnabled = true;
        }

        QueueSave();
        QueueBridgeRefresh();
        AppendLog("Enabled all universal triggers.");
    }

    private void DisableAllUniversalTriggers()
    {
        foreach (var trigger in Settings.UniversalTriggers.Where(trigger => trigger.IsEnabled))
        {
            trigger.IsEnabled = false;
        }

        QueueSave();
        QueueBridgeRefresh();
        AppendLog("Disabled all universal triggers.");
    }

    private void DeleteAllUniversalTriggers()
    {
        var warningResult = ThemedDialogWindow.ShowYesNo(
            Application.Current?.MainWindow,
            SelectedTheme,
            T("Delete Universal Triggers"),
            T("Delete every universal trigger? This does not delete Avatar Sets, Movement Redeems, Avatar Change, or Bits + Subs overrides."));

        if (!warningResult)
        {
            return;
        }

        var removedCount = Settings.UniversalTriggers.Count;
        Settings.UniversalTriggers.Clear();
        SelectedUniversalTrigger = null;
        QueueSave();
        QueueBridgeRefresh();
        AppendLog($"Deleted {removedCount} universal triggers.");
    }

    private void AddUniversalTriggerAction()
    {
        if (SelectedUniversalTrigger is null)
        {
            return;
        }

        var action = new UniversalTriggerAction();
        SelectedUniversalTrigger.Actions.Add(action);
        SelectedUniversalTriggerAction = action;
        QueueSave();
        QueueBridgeRefresh();
    }

    private void RemoveSelectedUniversalTriggerAction()
    {
        if (SelectedUniversalTrigger is null || SelectedUniversalTriggerAction is null)
        {
            return;
        }

        SelectedUniversalTrigger.Actions.Remove(SelectedUniversalTriggerAction);
        SelectedUniversalTriggerAction = SelectedUniversalTrigger.Actions.FirstOrDefault();
        QueueSave();
        QueueBridgeRefresh();
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
        var warningResult = ThemedDialogWindow.ShowYesNo(
            Application.Current?.MainWindow,
            SelectedTheme,
            T("Delete Avatar Scale Sets"),
            T("Delete every avatar scale set and scale redeem? This only clears the Avatar Scaling section."));

        if (!warningResult)
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
            foreach (var trigger in result.Triggers)
            {
                Settings.UniversalTriggers.Add(trigger);
            }

            var additionalFusedCount = UniversalTriggerFusionService.FuseMatchingCommandFallbacks(Settings.UniversalTriggers);
            SelectedUniversalTrigger = result.Triggers.FirstOrDefault(Settings.UniversalTriggers.Contains) ?? SelectedUniversalTrigger;
            QueueSave(0);
            QueueBridgeRefresh();
            QueueManagedRewardSync(0);
            RefreshRuleCommandStates();
            var fusedCount = result.FusedCommandCount + additionalFusedCount;
            var summary = fusedCount > 0
                ? TF("Imported {0} universal trigger(s). Fused {1} matching chat command(s) into rewards. Skipped {2} invalid item(s).", result.ImportedCount, fusedCount, result.SkippedCount)
                : TF("Imported {0} universal trigger(s). Skipped {1} invalid item(s).", result.ImportedCount, result.SkippedCount);
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

    private (string DestinationName, string WarningMessage) GetDeleteAllWarningContent()
    {
        var destinationName = SelectedTheme == AppTheme.DreamScape
            ? "nightmare realm"
            : "void";
        var warningMessage = $"are you really sure you want to yeet all of these rule to the {destinationName}?";
        return (destinationName, warningMessage);
    }

    private void RetireManagedRewards(IEnumerable<TriggerRule> rules)
    {
        var retiredIds = rules
            .Where(rule => rule.RewardSyncMode == TwitchRewardSyncMode.CreateOrManage)
            .Select(rule => rule.ChannelPointRewardId?.Trim())
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

    private TriggerRule? GetRememberedMovementRule()
    {
        IEnumerable<TriggerRule> candidateRules = (IEnumerable<TriggerRule>?)SelectedMovementRedeemSet?.MovementRules
            ?? GetAllMovementRules();
        if (lastSelectedMovementRuleId != Guid.Empty)
        {
            var rememberedRule = candidateRules.FirstOrDefault(rule => rule.Id == lastSelectedMovementRuleId);
            if (rememberedRule is not null)
            {
                return rememberedRule;
            }
        }

        return candidateRules.FirstOrDefault();
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

    private UniversalTriggerRule? GetRememberedUniversalTrigger()
    {
        if (lastSelectedUniversalTriggerId != Guid.Empty)
        {
            var rememberedTrigger = Settings.UniversalTriggers.FirstOrDefault(trigger => trigger.Id == lastSelectedUniversalTriggerId);
            if (rememberedTrigger is not null)
            {
                return rememberedTrigger;
            }
        }

        return Settings.UniversalTriggers.FirstOrDefault();
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

    private MovementRedeemSet? GetRememberedMovementRedeemSet()
    {
        if (lastSelectedMovementSetId != Guid.Empty)
        {
            var rememberedSet = Settings.MovementRedeemSets.FirstOrDefault(set => set.Id == lastSelectedMovementSetId);
            if (rememberedSet is not null)
            {
                return rememberedSet;
            }
        }

        var rememberedRuleOwner = lastSelectedMovementRuleId == Guid.Empty
            ? null
            : Settings.MovementRedeemSets.FirstOrDefault(set => set.MovementRules.Any(rule => rule.Id == lastSelectedMovementRuleId));
        return rememberedRuleOwner ?? Settings.MovementRedeemSets.FirstOrDefault();
    }

    private void EnsureSelectedMovementRedeemSet()
    {
        if (SelectedMovementRedeemSet is not null)
        {
            return;
        }

        SelectedMovementRedeemSet = GetRememberedMovementRedeemSet();
        if (SelectedMovementRedeemSet is not null)
        {
            return;
        }

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

        if (IsViewingSupporterOverrides)
        {
            lastSelectedSupporterRuleId = rule.Id;
            return;
        }

        if (IsViewingMovementRedeems)
        {
            lastSelectedMovementRuleId = rule.Id;
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

        if (lastSelectedMovementRuleId == rule.Id)
        {
            lastSelectedMovementRuleId = Guid.Empty;
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
        isSwitchingRuleView = true;
        try
        {
            activeRuleListView = targetView;
            SelectedAvatarProfile = profile;
            SelectedRule = rule;
            if (targetView != RuleListView.UniversalTriggers)
            {
                SelectedUniversalTrigger = null;
            }

            if (targetView != RuleListView.MovementRedeems)
            {
                SelectedMovementRedeemSet = null;
            }

            if (targetView != RuleListView.AvatarScaling)
            {
                SelectedAvatarScaleSet = null;
                SelectedAvatarScaleRule = null;
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
        if (IsViewingUniversalTriggers)
        {
            RefreshAvatarParameterOptions();
            _ = EnsureSelectedAvatarParameterCacheLoadedAsync();
        }
        else if (SelectedRule?.ActionType == OscActionType.AvatarParameter)
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
        appSettings.AvatarProfiles.CollectionChanged += AvatarProfilesCollectionChanged;
        appSettings.MovementRedeemSets.CollectionChanged += MovementRedeemSetsCollectionChanged;
        appSettings.GlobalOverrideRules.CollectionChanged += GlobalOverrideRulesCollectionChanged;
        appSettings.UniversalTriggers.CollectionChanged += UniversalTriggersCollectionChanged;
        appSettings.AvatarScaleSets.CollectionChanged += AvatarScaleSetsCollectionChanged;
        appSettings.AvatarScaleMasterReward.PropertyChanged += AvatarScaleMasterRewardChanged;
        WireRewardFireSale(appSettings.RewardFireSale);

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
    }

    private void UnwireSettings(AppSettings appSettings)
    {
        appSettings.PropertyChanged -= SettingsChanged;
        appSettings.Broadcaster.PropertyChanged -= SettingsChanged;
        appSettings.Bot.PropertyChanged -= SettingsChanged;
        appSettings.VrChat.PropertyChanged -= SettingsChanged;
        appSettings.AvatarProfiles.CollectionChanged -= AvatarProfilesCollectionChanged;
        appSettings.MovementRedeemSets.CollectionChanged -= MovementRedeemSetsCollectionChanged;
        appSettings.GlobalOverrideRules.CollectionChanged -= GlobalOverrideRulesCollectionChanged;
        appSettings.UniversalTriggers.CollectionChanged -= UniversalTriggersCollectionChanged;
        appSettings.AvatarScaleSets.CollectionChanged -= AvatarScaleSetsCollectionChanged;
        appSettings.AvatarScaleMasterReward.PropertyChanged -= AvatarScaleMasterRewardChanged;
        UnwireRewardFireSale(appSettings.RewardFireSale);

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
    }

    private void WireRewardFireSale(RewardFireSaleSettings fireSale)
    {
        fireSale.PropertyChanged += RewardFireSaleChanged;
        fireSale.Tiers.CollectionChanged += RewardFireSaleTiersCollectionChanged;
        foreach (var tier in fireSale.Tiers)
        {
            tier.PropertyChanged += RewardFireSaleTierChanged;
        }
    }

    private void UnwireRewardFireSale(RewardFireSaleSettings fireSale)
    {
        fireSale.PropertyChanged -= RewardFireSaleChanged;
        fireSale.Tiers.CollectionChanged -= RewardFireSaleTiersCollectionChanged;
        foreach (var tier in fireSale.Tiers)
        {
            tier.PropertyChanged -= RewardFireSaleTierChanged;
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
        RaisePropertyChanged(nameof(MovementRedeemRules));
        RaisePropertyChanged(nameof(AvatarScaleSets));
        RaisePropertyChanged(nameof(AvatarScaleRules));
        RaisePropertyChanged(nameof(SelectedLanguageOption));
        RaisePropertyChanged(nameof(IsLanguageRestartNoticeVisible));
        RaisePropertyChanged(nameof(LanguageRestartNoticeText));
        RaiseUniversalTriggerGroupProperties();
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
                if (lastSelectedMovementSetId == set.Id)
                {
                    lastSelectedMovementSetId = Guid.Empty;
                }
            }

            RetireManagedRewards(removedRules);
        }

        if (IsViewingMovementRedeems && SelectedMovementRedeemSet is not null && !Settings.MovementRedeemSets.Contains(SelectedMovementRedeemSet))
        {
            SelectedMovementRedeemSet = GetRememberedMovementRedeemSet();
        }

        if (IsViewingMovementRedeems
            && SelectedRule is not null
            && !GetAllMovementRules().Contains(SelectedRule))
        {
            SelectedRule = GetRememberedMovementRule();
        }

        SyncLegacyGlobalMovementRules();
        RaisePropertyChanged(nameof(MovementRedeemSets));
        RaisePropertyChanged(nameof(MovementRedeemRules));
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
                if (lastSelectedMovementRuleId == rule.Id)
                {
                    lastSelectedMovementRuleId = Guid.Empty;
                }
            }

            RetireManagedRewards(removedRules);
        }

        if (IsViewingMovementRedeems
            && SelectedRule is not null
            && SelectedMovementRedeemSet?.MovementRules.Contains(SelectedRule) != true)
        {
            SelectedRule = GetRememberedMovementRule();
        }

        SyncLegacyGlobalMovementRules();
        RaisePropertyChanged(nameof(MovementRedeemSets));
        RaisePropertyChanged(nameof(MovementRedeemRules));
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
                if (lastSelectedUniversalTriggerId == trigger.Id)
                {
                    lastSelectedUniversalTriggerId = Guid.Empty;
                }
            }

            RetireManagedRewards(removedTriggers);
        }

        if (IsViewingUniversalTriggers && SelectedUniversalTrigger is not null && !Settings.UniversalTriggers.Contains(SelectedUniversalTrigger))
        {
            SelectedUniversalTrigger = GetRememberedUniversalTrigger();
        }

        RaiseUniversalTriggerGroupProperties();
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

        AddUniversalTriggerActionCommand.NotifyCanExecuteChanged();
        RemoveSelectedUniversalTriggerActionCommand.NotifyCanExecuteChanged();
        QueueSave();
        QueueBridgeRefresh();
        QueueManagedRewardSync();
        RaisePropertyChanged(nameof(UniversalManagedRewardStatusText));
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
        RaisePropertyChanged(nameof(UniversalManagedRewardStatusText));

        if (!isSynchronizingManagedRewards
            && ShouldSynchronizeManagedRewardsForUniversalTriggerChange(e.PropertyName))
        {
            QueueManagedRewardSync();
        }
    }

    private void UniversalTriggerActionChanged(object? sender, PropertyChangedEventArgs e)
    {
        QueueSave();
        QueueBridgeRefresh();
        RaisePropertyChanged(nameof(UniversalManagedRewardStatusText));

        if (!isSynchronizingManagedRewards
            && ShouldSynchronizeManagedRewardsForUniversalTriggerActionChange(e.PropertyName))
        {
            QueueManagedRewardSync();
        }
    }

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

    private void RewardFireSaleChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (sender is not RewardFireSaleSettings fireSale)
        {
            return;
        }

        if (suppressRewardFireSaleChangeSideEffects)
        {
            return;
        }

        if (e.PropertyName == nameof(RewardFireSaleSettings.Tiers))
        {
            UnwireRewardFireSale(fireSale);
            WireRewardFireSale(fireSale);
        }

        if (e.PropertyName == nameof(RewardFireSaleSettings.IsEnabled)
            && !fireSale.IsEnabled
            && fireSale.IsSaleActive)
        {
            StopRewardFireSale(expired: false);
        }

        if (e.PropertyName == nameof(RewardFireSaleSettings.SaleMode))
        {
            RaisePropertyChanged(nameof(IsRewardFireSaleTemporary));
            if (fireSale.IsSaleActive)
            {
                if (fireSale.SaleMode == RewardFireSaleMode.Temporary
                    && (fireSale.ActiveUntilUtc is null || fireSale.ActiveUntilUtc <= DateTimeOffset.UtcNow))
                {
                    fireSale.ActiveUntilUtc = DateTimeOffset.UtcNow.AddSeconds(Math.Max(1, fireSale.TemporaryDurationSeconds));
                }

                ScheduleRewardFireSaleExpirationIfNeeded();
            }
        }

        if (e.PropertyName == nameof(RewardFireSaleSettings.FundingRewardCooldownSeconds)
            && fireSale.FundingRewardCooldownSeconds <= 0)
        {
            ClearRewardFireSaleFundingRewardCooldown(queueSync: true);
        }

        if (e.PropertyName == nameof(RewardFireSaleSettings.IsSaleActive)
            || e.PropertyName == nameof(RewardFireSaleSettings.ActiveDiscountPercent)
            || e.PropertyName == nameof(RewardFireSaleSettings.ActiveTierGoalAmount)
            || e.PropertyName == nameof(RewardFireSaleSettings.ActiveUntilUtc)
            || e.PropertyName == nameof(RewardFireSaleSettings.CurrentProgress)
            || e.PropertyName == nameof(RewardFireSaleSettings.IsEnabled)
            || e.PropertyName == nameof(RewardFireSaleSettings.FundingRewardCost)
            || e.PropertyName == nameof(RewardFireSaleSettings.FundingRewardCooldownSeconds)
            || e.PropertyName == nameof(RewardFireSaleSettings.FundingRewardDescription)
            || e.PropertyName == nameof(RewardFireSaleSettings.RewardPointsPerProgressUnit))
        {
            RefreshRewardFireSaleStateProperties();
        }

        if (e.PropertyName == nameof(RewardFireSaleSettings.IsSaleActive)
            || e.PropertyName == nameof(RewardFireSaleSettings.ActiveDiscountPercent)
            || e.PropertyName == nameof(RewardFireSaleSettings.IsEnabled)
            || e.PropertyName == nameof(RewardFireSaleSettings.FundingRewardEnabled)
            || e.PropertyName == nameof(RewardFireSaleSettings.FundingRewardTitle)
            || e.PropertyName == nameof(RewardFireSaleSettings.FundingRewardCost)
            || e.PropertyName == nameof(RewardFireSaleSettings.FundingRewardCooldownSeconds)
            || e.PropertyName == nameof(RewardFireSaleSettings.FundingRewardDescription)
            || e.PropertyName == nameof(RewardFireSaleSettings.FundingRewardReadyColor)
            || e.PropertyName == nameof(RewardFireSaleSettings.FundingRewardCooldownColor)
            || e.PropertyName == nameof(RewardFireSaleSettings.RewardPointsPerProgressUnit))
        {
            QueueManagedRewardSync(0, ManagedRewardSyncReason.FireSaleChanged);
        }

        QueueSave();
    }

    private void RewardFireSaleTiersCollectionChanged(object? sender, NotifyCollectionChangedEventArgs e)
    {
        if (e.NewItems is not null)
        {
            foreach (RewardFireSaleTier tier in e.NewItems)
            {
                tier.PropertyChanged += RewardFireSaleTierChanged;
            }
        }

        if (e.OldItems is not null)
        {
            foreach (RewardFireSaleTier tier in e.OldItems)
            {
                tier.PropertyChanged -= RewardFireSaleTierChanged;
            }
        }

        RefreshRewardFireSaleStateProperties();
        QueueSave();
    }

    private void RewardFireSaleTierChanged(object? sender, PropertyChangedEventArgs e)
    {
        RefreshRewardFireSaleStateProperties();
        QueueSave();
    }

    private void HandleAvatarScaleStatusChanged()
    {
        RaisePropertyChanged(nameof(AvatarScaleRuntimeStatusText));

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
            var isInactiveAtLimit = IsAvatarScaleRuleInactiveAtRelativeLimit(rule, status.CurrentHeightMeters);
            if (!avatarScaleLimitInactiveStateByRuleId.TryGetValue(rule.Id, out var previousState)
                || previousState != isInactiveAtLimit)
            {
                avatarScaleLimitInactiveStateByRuleId[rule.Id] = isInactiveAtLimit;
                shouldSync = shouldSync || previousState != isInactiveAtLimit;
            }
        }

        foreach (var removedRuleId in avatarScaleLimitInactiveStateByRuleId.Keys.Except(activeRuleIds).ToArray())
        {
            avatarScaleLimitInactiveStateByRuleId.Remove(removedRuleId);
        }

        if (shouldSync)
        {
            QueueManagedRewardSync(
                (int)AvatarScaleLimitRewardSyncDebounce.TotalMilliseconds,
                ManagedRewardSyncReason.AvatarScaleStatus);
        }
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

        foreach (var rule in profile.ChannelPointRules)
        {
            rule.TriggerType = TwitchTriggerType.ChannelPoints;
            rule.PropertyChanged += RuleChanged;
        }
    }

    private void UnwireAvatarProfile(AvatarTriggerProfile profile)
    {
        profile.PropertyChanged -= AvatarProfileChanged;
        profile.ChannelPointRules.CollectionChanged -= AvatarProfileRulesCollectionChanged;

        foreach (var rule in profile.ChannelPointRules)
        {
            rule.PropertyChanged -= RuleChanged;
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
                && e.PropertyName == nameof(TriggerRule.ActionType)
                && rule.ActionType is OscActionType.AvatarChange or OscActionType.AvatarRoulet)
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
                     && rule.ActionType == OscActionType.PlayerMovement)
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
                    or nameof(TriggerRule.SupporterAvatarId)
                    or nameof(TriggerRule.SupporterAvatarName)
                    or nameof(TriggerRule.SupporterAvatarProfileId)
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
                OpenAvatarRouletPoolPickerCommand.NotifyCanExecuteChanged();
                AddSetTriggerActionCommand.NotifyCanExecuteChanged();
                RemoveSelectedSetTriggerActionCommand.NotifyCanExecuteChanged();
                RefreshAvatarParameterPathCommandStates();
            }

            if (Settings.GlobalOverrideRules.Contains(rule)
                && e.PropertyName is nameof(TriggerRule.ActionType)
                    or nameof(TriggerRule.TriggerType)
                    or nameof(TriggerRule.SupporterAvatarId)
                    or nameof(TriggerRule.SupporterAvatarName)
                    or nameof(TriggerRule.SupporterAvatarProfileId))
            {
                RaiseSupporterRuleGroupProperties();
            }

            if (ReferenceEquals(rule, SelectedRule)
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
        var saveDelayMilliseconds = 500;

        if (ReferenceEquals(sender, Settings.Broadcaster)
            && e.PropertyName == nameof(TwitchAccountSettings.Scopes)
            && BroadcasterCanManageRewards)
        {
            ClearBroadcasterManagedRewardsUnavailableForSession();
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

    private void QueueSave(int delayMilliseconds = 500)
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

    internal async Task<ApplicationUpdateInfo?> GetPendingApplicationUpdateAsync(CancellationToken cancellationToken = default)
    {
        ApplicationUpdateCheckResult result;
        try
        {
            result = await applicationUpdateService.CheckForUpdateAsync(AppVersion, Settings.IgnoredUpdateVersion, cancellationToken);
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

    private void QueueBridgeRefresh()
    {
        if (!isInitialized)
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
                    bridgeRefreshGate.Release();
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

        if (!Settings.Broadcaster.IsConnected)
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
                        AppendLog("Crystal Relay kept OSCQuery running so Force Refresh and Test Rule can still work.");
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

        await ReloadRuntimeConfigAsync();

        await bridgeRefreshGate.WaitAsync();
        try
        {
            await EnsureBridgeStateAsync(CancellationToken.None, allowOscOnly: true);

            var selectedRule = SelectedRule;
            var (isGlobalOverride, profile) = ResolveRuleRuntimeContext(selectedRule);
            var ruleSnapshot = BridgeRuntimeConfiguration.CreateManualTestSnapshot(selectedRule, isGlobalOverride, profile);
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

    private async Task TestSelectedUniversalTriggerAsync()
    {
        if (SelectedUniversalTrigger is null)
        {
            return;
        }

        await ReloadRuntimeConfigAsync();

        await bridgeRefreshGate.WaitAsync();
        try
        {
            await EnsureBridgeStateAsync(CancellationToken.None, allowOscOnly: true);

            var triggerSnapshot = BridgeRuntimeConfiguration.CreateManualTestSnapshot(SelectedUniversalTrigger);
            await bridgeCoordinator.SendTestUniversalTriggerAsync(triggerSnapshot, CancellationToken.None);

            BridgeStatus = $"Sent universal test for '{triggerSnapshot.Name}'.";
        }
        catch (Exception ex)
        {
            BridgeStatus = "Universal trigger test did not run.";
            AppendLog($"Could not test the selected universal trigger: {ex.Message}");
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
                ruleSnapshot = BridgeRuntimeConfiguration.CreateManualTestSnapshot(ruleToTest);
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
    }

    private async Task<ManagedRewardSyncOutcome> EnsureBroadcasterRewardManagementReadyAsync(
        string status,
        string logKey,
        bool clearRewardOptions,
        CancellationToken cancellationToken)
    {
        var accountSnapshot = await GetBroadcasterRewardAccountSnapshotAsync(cancellationToken);
        if (!accountSnapshot.IsConnected)
        {
            return ManagedRewardSyncOutcome.Completed;
        }

        if (string.IsNullOrWhiteSpace(accountSnapshot.AccessToken)
            || string.IsNullOrWhiteSpace(accountSnapshot.UserId)
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

        if (!string.IsNullOrWhiteSpace(refreshToken)
            && expiresAt is { } existingExpiresAt
            && existingExpiresAt <= DateTimeOffset.UtcNow.Add(TwitchAccessTokenRefreshLeadTime))
        {
            var refreshedToken = await twitchApiClient.RefreshAccessTokenAsync(
                account.TwitchClientId,
                refreshToken,
                cancellationToken);
            accessToken = refreshedToken.AccessToken;
            refreshToken = refreshedToken.RefreshToken;
            scopes = [.. refreshedToken.Scope];
            expiresAt = DateTimeOffset.UtcNow.AddSeconds(refreshedToken.ExpiresIn);
        }

        var validation = await twitchApiClient.ValidateTokenAsync(accessToken, cancellationToken);
        if (validation is null)
        {
            if (string.IsNullOrWhiteSpace(refreshToken))
            {
                throw new InvalidOperationException("The saved broadcaster token expired and has no refresh token.");
            }

            var refreshedToken = await twitchApiClient.RefreshAccessTokenAsync(
                account.TwitchClientId,
                refreshToken,
                cancellationToken);
            accessToken = refreshedToken.AccessToken;
            refreshToken = refreshedToken.RefreshToken;
            scopes = [.. refreshedToken.Scope];
            expiresAt = DateTimeOffset.UtcNow.AddSeconds(refreshedToken.ExpiresIn);
            validation = await twitchApiClient.ValidateTokenAsync(accessToken, cancellationToken)
                ?? throw new InvalidOperationException("The refreshed broadcaster token could not be validated.");
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
        var bridgeSensitiveChange = HasBridgeSensitiveTwitchAccountChanges(Settings.Broadcaster, refreshedAccount);
        var accountChanged = bridgeSensitiveChange
            || !string.Equals(Settings.Broadcaster.DisplayName, refreshedAccount.DisplayName, StringComparison.Ordinal)
            || !string.Equals(Settings.Broadcaster.ProfileImageUrl, refreshedAccount.ProfileImageUrl, StringComparison.Ordinal)
            || Settings.Broadcaster.AccessTokenExpiresAt != refreshedAccount.AccessTokenExpiresAt
            || Settings.Broadcaster.SessionRenewalDueAt != refreshedAccount.SessionRenewalDueAt;

        Settings.Broadcaster.Apply(refreshedAccount);
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
            default:
                return;
        }

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

        if (!Settings.Broadcaster.IsConnected)
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

    // Debounced entry point for managed Twitch reward syncing.
    // Many editor changes land here, so older pending syncs get canceled in favor of the latest state.
    private void QueueManagedRewardSync(
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
        reason is ManagedRewardSyncReason.RuntimeAvailability or ManagedRewardSyncReason.AvatarScaleStatus;

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
        ManagedRewardSyncReason.TestMode => "test mode",
        ManagedRewardSyncReason.EmergencyStop => "emergency stop",
        ManagedRewardSyncReason.StreamStateChanged => "stream state changed",
        ManagedRewardSyncReason.FireSaleChanged => "reward fire sale",
        ManagedRewardSyncReason.ManualRefresh => "manual refresh",
        ManagedRewardSyncReason.ManualCleanup => "manual cleanup",
        ManagedRewardSyncReason.Maintenance => "maintenance",
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
            RaisePropertyChanged(nameof(UniversalManagedRewardStatusText));
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
        RaisePropertyChanged(nameof(UniversalManagedRewardStatusText));
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
            await RecoverManagedRewardCurrentAvatarFromBridgeRuntimeAsync(cancellationToken);
            if (string.IsNullOrWhiteSpace(GetManagedRewardActivationAvatarId()))
            {
                RunOnUi(() => AppendThrottledLog(
                    "managed-reward-current-avatar-unknown",
                    "Crystal Relay does not know the current VRChat avatar yet. Avatar Set and Avatar Change rewards will stay hidden until the avatar refresh succeeds.",
                    ThrottledRewardSyncLogWindow));
            }

            RunOnUi(() => RaisePropertyChanged(nameof(UniversalManagedRewardStatusText)));
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

        RunOnUi(() => RaisePropertyChanged(nameof(UniversalManagedRewardStatusText)));
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
            || isShuttingDown
            || !Settings.VrChat.IsConnected)
        {
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
        if (!isInitialized || isShuttingDown || !Settings.VrChat.IsConnected)
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
        if (!Settings.VrChat.IsConnected)
        {
            return;
        }

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
            return;
        }

        var currentAvatarId = Settings.VrChat.CurrentAvatarId?.Trim() ?? string.Empty;
        if (string.Equals(lastDetectedVrChatAvatarId, resolvedAvatarId, StringComparison.Ordinal)
            && string.Equals(currentAvatarId, resolvedAvatarId, StringComparison.Ordinal))
        {
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
            ClearVrChatAccountPreservingCurrentAvatar();
            ClearAvailableVrChatAvatars();
            await settingsStore.ClearVrChatAvatarCacheAsync(CancellationToken.None);
            await settingsStore.ClearVrChatOscParameterCacheAsync(CancellationToken.None);
            cachedVrChatParametersByAvatarId.Clear();
            AvatarParameterOptions.Clear();
            SetTriggerParameterOptions.Clear();
            SelectedAvatarParameterOption = null;
            SelectedSetTriggerParameterOption = null;
            RaiseVrChatConnectionStateProperties();
            SyncVrChatRuntimeState(queueManagedRewardSync: false);
            VrChatStatus = T("Saved VRChat session expired. Connect again to keep tracking your current avatar.");
            VrChatAvatarStatus = T("VRChat avatar list is unavailable until you reconnect.");
            VrChatOscParameterStatus = T("Reconnect VRChat to load avatar parameters again.");
            ResetVrChatLocalRuntimeTracking();
            DisposeVrChatLocalOscWatcher();
            AppendLog(T("Saved VRChat avatar session expired and was cleared."));
            QueueSave();
        }
    }

    private void StartOrRefreshVrChatLocalOscWatcher()
    {
        if (!Settings.VrChat.IsConnected || string.IsNullOrWhiteSpace(Settings.VrChat.UserId))
        {
            DisposeVrChatLocalOscWatcher();
            return;
        }

        var avatarFolderPath = VrChatLocalOscCacheService.GetAvatarOscFolderPath(Settings.VrChat.UserId);
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
        if (!isInitialized || isShuttingDown || !Settings.VrChat.IsConnected || string.IsNullOrWhiteSpace(Settings.VrChat.UserId))
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
        if (!Settings.VrChat.IsConnected || string.IsNullOrWhiteSpace(Settings.VrChat.UserId))
        {
            return;
        }

        var localAvatars = await vrChatLocalOscCacheService.LoadKnownAvatarsAsync(Settings.VrChat.UserId, cancellationToken);
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
                    Settings.VrChat.UserId,
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
            if (string.IsNullOrWhiteSpace(normalizedLocalAvatarId)
                || !mergedAvatars.TryGetValue(normalizedLocalAvatarId, out var existingAvatar))
            {
                continue;
            }

            var normalizedLocalAvatarName = localAvatar.AvatarName?.Trim() ?? string.Empty;
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

    private ManagedRewardSyncTarget CreateManagedRewardTargetForRule(
        AvatarTriggerProfile? profile,
        TriggerRule rule,
        string currentAvatarId,
        bool avatarChangeTransitionActive,
        bool allowManagedRewardActivation,
        IReadOnlyCollection<Guid> temporarilyDisabledRuleIds,
        IReadOnlyCollection<Guid> cooldownRuleIds)
    {
        var ruleHasRuntimeReadyAction = HasRuntimeReadyAction(rule);
        var profileIsEffectivelyActive = AvatarRuleActivationPolicy.IsRuleActiveForCurrentAvatar(
            isGlobalOverride: profile is null,
            belongsToMasterAvatarProfile: profile?.IsMasterProfile ?? false,
            actionType: rule.ActionType,
            avatarChangeTargetId: rule.AvatarChangeTargetId,
            requiredAvatarId: profile?.AvatarId,
            currentAvatarId: currentAvatarId,
            avatarChangeTransitionActive: avatarChangeTransitionActive);
        var desiredEnabled = allowManagedRewardActivation
            && ruleHasRuntimeReadyAction
            && (profile?.IsEnabled ?? true)
            && rule.IsEnabled
            && !temporarilyDisabledRuleIds.Contains(rule.Id)
            && profileIsEffectivelyActive;
        var isOnLocalCooldown = cooldownRuleIds.Contains(rule.Id);
        var backgroundColor = isOnLocalCooldown
            ? ManagedRewardPresentation.NormalizeCooldownBackgroundColor(rule.ManagedRewardCooldownColor)
            : ManagedRewardPresentation.NormalizeReadyBackgroundColor(rule.ManagedRewardReadyColor);

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
            deleteWhenInactive: rule.DeleteManagedRewardWhenInactive && !temporarilyDisabledRuleIds.Contains(rule.Id),
            protectFromCapReclaim: desiredEnabled || isOnLocalCooldown || temporarilyDisabledRuleIds.Contains(rule.Id),
            applyRewardId: rewardId => rule.ChannelPointRewardId = rewardId);
    }

    private ManagedRewardSyncTarget CreateManagedRewardTargetForSharedAvatarSetRewardGroup(
        AvatarTriggerProfile profile,
        SharedAvatarSetRewardGroup group,
        string currentAvatarId,
        bool avatarChangeTransitionActive,
        bool allowManagedRewardActivation,
        IReadOnlyCollection<Guid> temporarilyDisabledRuleIds,
        IReadOnlyCollection<Guid> cooldownRuleIds)
    {
        var owner = group.Owner;
        var profileIsEffectivelyActive = AvatarRuleActivationPolicy.IsRuleActiveForCurrentAvatar(
            isGlobalOverride: false,
            belongsToMasterAvatarProfile: profile.IsMasterProfile,
            actionType: owner.ActionType,
            avatarChangeTargetId: owner.AvatarChangeTargetId,
            requiredAvatarId: profile.AvatarId,
            currentAvatarId: currentAvatarId,
            avatarChangeTransitionActive: avatarChangeTransitionActive);
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
        var readyColor = group.UsesSetTriggerMasterReward
            ? profile.SetTriggerMasterRewardReadyColor
            : owner.ManagedRewardReadyColor;
        var cooldownColor = group.UsesSetTriggerMasterReward
            ? profile.SetTriggerMasterRewardCooldownColor
            : owner.ManagedRewardCooldownColor;
        var backgroundColor = anyChoiceInCooldown
            ? ManagedRewardPresentation.NormalizeCooldownBackgroundColor(cooldownColor)
            : ManagedRewardPresentation.NormalizeReadyBackgroundColor(readyColor);
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
            deleteWhenInactive: deleteWhenInactive && !anyChoiceInCooldown && !anyChoiceTemporarilyDisabled,
            protectFromCapReclaim: desiredEnabled || anyChoiceInCooldown || anyChoiceTemporarilyDisabled,
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
            .Where(IsSharedAvatarSetRewardChoiceRule)
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
                var usesSetTriggerMasterReward = rules.Any(rule => rule.ActionType == OscActionType.SetTrigger);
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

    private ManagedRewardSyncTarget CreateManagedRewardTargetForUniversalTrigger(
        UniversalTriggerRule trigger,
        bool allowManagedRewardActivation,
        string currentAvatarId)
    {
        var desiredEnabled = allowManagedRewardActivation
            && trigger.IsEnabled
            && HasRuntimeReadyUniversalTriggerAction(trigger)
            && IsUniversalTriggerReadyForCurrentAvatarJson(trigger, currentAvatarId);

        return new ManagedRewardSyncTarget(
            trigger.Id,
            trigger.DisplayTitle,
            trigger.RewardId,
            trigger.RewardTitle,
            ApplyRewardFireSaleDiscount(trigger.RewardCost, trigger.RewardSyncMode),
            trigger.RewardSyncMode,
            cooldownSeconds: trigger.UsesCreateOrManageReward ? trigger.RewardCooldownSeconds : 0,
            backgroundColor: ManagedRewardPresentation.NormalizeReadyBackgroundColor(trigger.ManagedRewardReadyColor),
            prompt: BuildManagedRewardPrompt(trigger.RewardDescription),
            requireUserInput: false,
            desiredEnabled: desiredEnabled,
            isCooldownActive: false,
            deleteWhenInactive: trigger.DeleteManagedRewardWhenInactive,
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
        var backgroundColor = useCooldownPresentation
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
        var currentHeight = bridgeCoordinator.GetAvatarScaleRuntimeStatus().CurrentHeightMeters;
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
            && !IsAvatarScaleRuleInactiveAtRelativeLimit(rule, currentHeight)
            && !isTemporarilyDisabledByPairing
            && masterGateAllowsReward;
        var backgroundColor = isOnLocalCooldown
            ? ManagedRewardPresentation.NormalizeCooldownBackgroundColor(rule.ManagedRewardCooldownColor)
            : ManagedRewardPresentation.NormalizeReadyBackgroundColor(rule.ManagedRewardReadyColor);
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
            deleteWhenInactive: shouldDeleteWhenInactive && !isOnLocalCooldown && !isActiveScaleEffect && !isQueuedScaleRedeem && !isTemporarilyDisabledByPairing,
            protectFromCapReclaim: desiredEnabled || isOnLocalCooldown || isActiveScaleEffect || isQueuedScaleRedeem || isTemporarilyDisabledByPairing,
            applyRewardId: rewardId => rule.RewardId = rewardId);
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
        var backgroundColor = isFundingRewardOnCooldown
            ? ManagedRewardPresentation.NormalizeCooldownBackgroundColor(fireSale.FundingRewardCooldownColor)
            : ManagedRewardPresentation.NormalizeReadyBackgroundColor(fireSale.FundingRewardReadyColor);

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
        var transitionSeconds = Math.Max(0, rule.SmoothTransitionSeconds);
        var activeSeconds = Math.Max(0, rule.ActiveTimeSeconds);
        var restoreTransitionSeconds = activeSeconds > 0 && rule.RestoreMode != AvatarScaleRestoreMode.None
            ? transitionSeconds
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
        if (!Settings.VrChat.IsConnected
            || string.IsNullOrWhiteSpace(Settings.VrChat.UserId)
            || string.IsNullOrWhiteSpace(normalizedAvatarId))
        {
            RunOnUi(() => RaisePropertyChanged(nameof(UniversalManagedRewardStatusText)));
            return;
        }

        var avatarFolderPath = VrChatLocalOscCacheService.GetAvatarOscFolderPath(Settings.VrChat.UserId);
        var avatarFilePath = VrChatLocalOscCacheService.GetAvatarOscFilePath(Settings.VrChat.UserId, normalizedAvatarId);
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
                    Settings.VrChat.UserId,
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
            vrChatLocalOscAvatarWriteTimes.Remove(normalizedAvatarId);

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

        vrChatLocalOscAvatarWriteTimes.Remove(normalizedAvatarId);
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

        if (!Settings.Broadcaster.IsConnected)
        {
            SetUniversalManagedRewardSyncStatus("Universal Twitch reward sync skipped because the broadcaster account is disconnected.");
            return ManagedRewardSyncOutcome.Completed;
        }

        if (string.IsNullOrWhiteSpace(Settings.Broadcaster.AccessToken)
            || string.IsNullOrWhiteSpace(Settings.Broadcaster.UserId)
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
                        cooldownRuleIds));
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
                        cooldownRuleIds));
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
                    cooldownRuleIds))
                .ToArray();
            var universalTargets = managedUniversalTriggers
                .Select(trigger => CreateManagedRewardTargetForUniversalTrigger(
                    trigger,
                    allowManagedRewardActivation,
                    currentAvatarId))
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
                forcedManagedRewardActivation);
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

            RunOnUi(() => ApplyRewardCatalog(rewardCatalog.Rewards));
            lastSuccessfulManagedRewardDesiredFingerprint = BuildManagedRewardDesiredFingerprint(
                allSyncTargets,
                retiredManagedRewardIds,
                currentAvatarId,
                allowManagedRewardActivation,
                forcedManagedRewardActivation);
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
        var managedRewardTitle = ManagedRewardPresentation.BuildTitle(rewardTitle);
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
            RunOnUi(() => AppendThrottledLog(
                $"managed-reward-delete-suppressed:{reason}:{target.Id}",
                $"Skipped deleting inactive Twitch reward for '{target.DisplayTitle}' during {DescribeManagedRewardSyncReason(reason)} to avoid reward API churn. Crystal Relay only deletes opted-in inactive rewards during explicit cleanup/maintenance or direct rule removal.",
                ThrottledRewardSyncLogWindow));
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
            if (!ManagedRewardPresentation.HasSameTitleIdentity(existingReward.Title, managedRewardTitle)
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

    private static bool IsAvatarScaleRuleInactiveAtRelativeLimit(AvatarScaleRule rule, double? currentHeight)
    {
        if (rule.ScaleMode != AvatarScaleMode.RelativeHeight
            || currentHeight is null
            || rule.RelativeHeightMeters == 0)
        {
            return false;
        }

        return rule.RelativeHeightMeters < 0
            ? currentHeight.Value <= rule.RelativeMinimumHeightMeters
            : currentHeight.Value >= rule.RelativeMaximumHeightMeters;
    }

    private static bool HasUniversalTriggerAvatarParameterGate(UniversalTriggerRule trigger) =>
        GetUniversalTriggerRequiredAvatarParameterAddresses(trigger).Count > 0;

    private static bool HasRuntimeReadyUniversalTriggerAction(UniversalTriggerRule trigger) =>
        trigger.Actions.Any(IsRuntimeReadyUniversalTriggerAction);

    private static bool IsRuntimeReadyAvatarScaleRule(AvatarScaleRule rule) =>
        rule.ScaleMode != AvatarScaleMode.Multiplier || rule.HeightMultiplier > 0;

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
        if (string.IsNullOrWhiteSpace(Settings.VrChat.UserId)
            || string.IsNullOrWhiteSpace(normalizedAvatarId))
        {
            return false;
        }

        var avatarFilePath = VrChatLocalOscCacheService.GetAvatarOscFilePath(Settings.VrChat.UserId, normalizedAvatarId);
        return !string.IsNullOrWhiteSpace(avatarFilePath) && File.Exists(avatarFilePath);
    }

    private bool IsUniversalTriggerReadyForCurrentAvatarJson(
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
        if (string.IsNullOrWhiteSpace(normalizedAddress)
            || !normalizedAddress.StartsWith("/avatar/parameters/", StringComparison.Ordinal))
        {
            return null;
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
            RunOnUi(() => AppendThrottledLog(
                $"managed-reward-capacity-inactive:{managedRewardTitle}",
                $"Twitch is full on custom rewards, so Crystal Relay skipped creating inactive off-avatar reward '{ManagedRewardPresentation.StripPrefix(managedRewardTitle)}'. It will make room only for rewards needed by the current avatar.",
                ThrottledRewardSyncLogWindow));
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
            RunOnUi(() => AppendThrottledLog(
                $"managed-reward-capacity-no-reclaim:{managedRewardTitle}",
                $"Twitch is full on custom rewards, and Crystal Relay found no disabled app-owned off-avatar VRC reward it can safely recycle for '{target.DisplayTitle}'. Linked and user-created rewards were left untouched.",
                ThrottledRewardSyncLogWindow));
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
            if (rule.Id == newOwnerId
                || rule.RewardSyncMode != TwitchRewardSyncMode.CreateOrManage
                || !string.Equals(rule.ChannelPointRewardId?.Trim(), normalizedRewardId, StringComparison.Ordinal))
            {
                return;
            }

            rule.ChannelPointRewardId = string.Empty;
            clearedCount++;
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
        RunOnUi(() => AppendThrottledLog(
            $"managed-reward-capacity:{managedRewardTitle}",
            $"Twitch is full on custom rewards, so Crystal Relay could not add '{rewardTitle}' yet. Remove old rewards or wait for cleanup, then it will try again.",
            ThrottledRewardSyncLogWindow));
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
            RaisePropertyChanged(nameof(UniversalManagedRewardStatusText));
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
        bool? forcedManagedRewardActivation)
    {
        var builder = new StringBuilder();
        builder.Append("avatar=");
        AppendFingerprintValue(builder, currentAvatarId);
        builder.Append("|activation=").Append(allowManagedRewardActivation ? "1" : "0");
        builder.Append("|forced=").Append(forcedManagedRewardActivation?.ToString() ?? "auto");

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
            builder.Append(':').Append(target.IsCooldownActive ? "1" : "0");
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

    // About refresh prefers normal authenticated Twitch API calls when possible.
    // If the app does not have a usable token, it falls back to public image-only lookups.
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
    // fresh installs by clearing live state before trying public image-only fallbacks.
    private async Task RefreshAboutProfilesWithoutAuthAsync(IReadOnlyList<AboutTwitchProfile> profiles)
    {
        RunOnUi(() => ApplyAboutProfileLiveStates(
            profiles,
            new Dictionary<string, bool>(StringComparer.OrdinalIgnoreCase)));
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
                : $"Connected as {broadcasterDisplayName}.";
        }
        else
        {
            BroadcasterStatus = "Broadcaster account not connected.";
        }

        BotStatus = Settings.Bot.IsConnected
            ? $"Connected as {Settings.Bot.DisplayName}. Bot announcements are ready."
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
        var broadcasterConnected = Settings.Broadcaster.IsConnected;
        var normalizedBridgeStatus = BridgeStatus?.Trim() ?? string.Empty;
        var normalizedBroadcasterStatus = BroadcasterStatus?.Trim() ?? string.Empty;

        if (!broadcasterConnected)
        {
            hasResolvedBroadcasterLiveState = false;
            SetStreamingStatusCard(
                "Broadcaster not connected.",
                "Connect Twitch to monitor stream and listener status.",
                "Disconnected",
                "Unavailable",
                "Disconnected",
                "Offline",
                "Disconnected");
            return;
        }

        if (IsStreamingListenerInErrorState(normalizedBridgeStatus, normalizedBroadcasterStatus))
        {
            SetStreamingStatusCard(
                "Streaming needs attention.",
                BuildStreamingErrorDetail(normalizedBridgeStatus, normalizedBroadcasterStatus),
                "Error",
                "Unknown",
                "Error",
                "Down",
                "Error");
            return;
        }

        if (IsStreamingListenerConnectingState(normalizedBridgeStatus))
        {
            if (bridgeCoordinator.IsRunning && hasResolvedBroadcasterLiveState)
            {
                SetResolvedStreamingStatusCard(listenerIsReconnecting: true);
                return;
            }

            var isReconnect = normalizedBridgeStatus.Contains("reconnect", StringComparison.OrdinalIgnoreCase);
            SetStreamingStatusCard(
                "Checking Twitch status...",
                isReconnect
                    ? "Crystal Relay is reconnecting the Twitch listener and checking your stream status."
                    : "Crystal Relay is connecting the Twitch listener and checking your stream status.",
                "Checking",
                "Checking",
                "Checking",
                "Connecting",
                "Checking");
            return;
        }

        if (bridgeCoordinator.IsRunning)
        {
            if (IsBroadcasterLive)
            {
                SetResolvedStreamingStatusCard(listenerIsReconnecting: false);
                return;
            }

            SetResolvedStreamingStatusCard(listenerIsReconnecting: false);
            return;
        }

        SetStreamingStatusCard(
            "Checking Twitch status...",
            "Crystal Relay is connecting the Twitch listener and checking your stream status.",
            "Checking",
            "Checking",
            "Checking",
            "Connecting",
            "Checking");
    }

    private void SetResolvedStreamingStatusCard(bool listenerIsReconnecting)
    {
        var listenerText = listenerIsReconnecting ? "Reconnecting" : "Connected";
        var listenerVisual = listenerIsReconnecting ? "Checking" : "Healthy";

        if (IsBroadcasterLive)
        {
            SetStreamingStatusCard(
                "You are live.",
                listenerIsReconnecting
                    ? "Twitch listener is reconnecting in the background. Your last checked stream state is live."
                    : "Twitch listener is connected and streaming status is updating normally.",
                "Live",
                "Live",
                "Live",
                listenerText,
                listenerVisual);
            return;
        }

        SetStreamingStatusCard(
            "You are offline.",
            listenerIsReconnecting
                ? "Twitch listener is reconnecting in the background. Your last checked stream state is offline."
                : "Twitch listener is connected and waiting for you to go live.",
            "Healthy",
            "Offline",
            "Healthy",
            listenerText,
            listenerVisual);
    }

    private void SetStreamingStatusCard(
        string summary,
        string detail,
        string cardVisualState,
        string streamStateText,
        string streamVisualState,
        string listenerStateText,
        string listenerVisualState)
    {
        StreamingStatusSummary = T(summary);
        StreamingStatusDetail = T(detail);
        StreamingStatusVisualState = cardVisualState;
        StreamingStreamStateText = T(streamStateText);
        StreamingStreamStateVisual = streamVisualState;
        StreamingListenerStateText = T(listenerStateText);
        StreamingListenerStateVisual = listenerVisualState;
    }

    private static bool IsStreamingListenerInErrorState(string bridgeStatus, string broadcasterStatus)
    {
        if (string.IsNullOrWhiteSpace(bridgeStatus) && string.IsNullOrWhiteSpace(broadcasterStatus))
        {
            return false;
        }

        return bridgeStatus.Contains("Twitch listener needs attention", StringComparison.OrdinalIgnoreCase)
            || bridgeStatus.Contains("Bridge error", StringComparison.OrdinalIgnoreCase)
            || bridgeStatus.Contains("OAuth session expired", StringComparison.OrdinalIgnoreCase)
            || bridgeStatus.Contains("Listener disconnected", StringComparison.OrdinalIgnoreCase)
            || bridgeStatus.Contains("Twitch connection issue", StringComparison.OrdinalIgnoreCase)
            || bridgeStatus.Contains("Background bridge could not start", StringComparison.OrdinalIgnoreCase)
            || broadcasterStatus.Contains("reconnect", StringComparison.OrdinalIgnoreCase)
            || broadcasterStatus.Contains("expired", StringComparison.OrdinalIgnoreCase);
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

    private bool IsStreamingListenerConnectingState(string bridgeStatus)
    {
        if (!bridgeCoordinator.IsRunning)
        {
            return true;
        }

        return bridgeStatus.Contains("Connecting background listener", StringComparison.OrdinalIgnoreCase)
            || bridgeStatus.Contains("Reconnecting background listener", StringComparison.OrdinalIgnoreCase)
            || bridgeStatus.Contains("starting", StringComparison.OrdinalIgnoreCase)
            || bridgeStatus.Contains("Listener disconnected", StringComparison.OrdinalIgnoreCase)
            || bridgeStatus.Contains("Twitch connection issue", StringComparison.OrdinalIgnoreCase);
    }

    private string BuildStreamingErrorDetail(string bridgeStatus, string broadcasterStatus)
    {
        if (bridgeStatus.Contains("OAuth session expired", StringComparison.OrdinalIgnoreCase)
            || bridgeStatus.Contains("Bridge error", StringComparison.OrdinalIgnoreCase)
            || broadcasterStatus.Contains("reconnect", StringComparison.OrdinalIgnoreCase)
            || broadcasterStatus.Contains("expired", StringComparison.OrdinalIgnoreCase))
        {
            return T("Reconnect Twitch to restore the background listener.");
        }

        if (bridgeStatus.Contains("Twitch listener needs attention", StringComparison.OrdinalIgnoreCase)
            || bridgeStatus.Contains("Listener disconnected", StringComparison.OrdinalIgnoreCase)
            || bridgeStatus.Contains("Twitch connection issue", StringComparison.OrdinalIgnoreCase)
            || bridgeStatus.Contains("Background bridge could not start", StringComparison.OrdinalIgnoreCase))
        {
            return T("Twitch listener is having trouble and may need a moment or a reconnect.");
        }

        return T("Twitch listener is down and needs attention before streaming status can update normally.");
    }

    private void UpdateOscStatusSummary()
    {
        if (bridgeCoordinator.IsOscActive)
        {
            OscBridgeSummary = bridgeCoordinator.HasDiscoveredVrChat
                ? T("OSC is transmitting and working.")
                : T("OSC is waiting for VRChat.");
            return;
        }

        if (!Settings.Broadcaster.IsConnected)
        {
            OscBridgeSummary = T("OSC waiting for broadcaster login.");
            return;
        }

        if (BridgeStatus.Contains("could not", StringComparison.OrdinalIgnoreCase)
            || BridgeStatus.Contains("failed", StringComparison.OrdinalIgnoreCase)
            || BridgeStatus.Contains("error", StringComparison.OrdinalIgnoreCase))
        {
            OscBridgeSummary = T("OSC needs attention.");
            return;
        }

        if (BridgeStatus.Contains("stopped", StringComparison.OrdinalIgnoreCase))
        {
            OscBridgeSummary = T("OSC is offline.");
            return;
        }

        if (BridgeStatus.Contains("refresh", StringComparison.OrdinalIgnoreCase))
        {
            OscBridgeSummary = T("OSC is refreshing.");
            return;
        }

        if (BridgeStatus.Contains("VRChat", StringComparison.OrdinalIgnoreCase)
            && (BridgeStatus.Contains("waiting", StringComparison.OrdinalIgnoreCase)
                || BridgeStatus.Contains("looking", StringComparison.OrdinalIgnoreCase)))
        {
            OscBridgeSummary = T("OSC is waiting for VRChat.");
            return;
        }

        if (BridgeStatus.Contains("reconnect", StringComparison.OrdinalIgnoreCase)
            || BridgeStatus.Contains("connect", StringComparison.OrdinalIgnoreCase)
            || BridgeStatus.Contains("live", StringComparison.OrdinalIgnoreCase)
            || BridgeStatus.Contains("listening", StringComparison.OrdinalIgnoreCase))
        {
            if (!OscBridgeSummary.Contains("VRChat", StringComparison.OrdinalIgnoreCase)
                && !OscBridgeSummary.Contains("transmitting", StringComparison.OrdinalIgnoreCase))
            {
                OscBridgeSummary = T("OSC is starting up.");
            }

            return;
        }

        OscBridgeSummary = T("OSC standing by.");
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
            Settings.Broadcaster.Apply(accountSettings);
            if (BroadcasterCanManageRewards)
            {
                ClearBroadcasterManagedRewardsUnavailableForSession();
            }
        }
        else
        {
            Settings.Bot.Apply(accountSettings);
        }
    }

    private void AppendLog(string message)
    {
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

    private void AppendThrottledLog(string key, string message, TimeSpan throttleWindow)
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

        ChatMessages.Add(new TwitchChatMessageEntry(
            message.UserDisplayName,
            message.MessageText,
            message.UserColor,
            [.. message.BadgeImageUrls],
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
            message.SupportMessage));
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
    }

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
        RemoveSelectedRuleCommand.NotifyCanExecuteChanged();
        TestSelectedRuleCommand.NotifyCanExecuteChanged();
        RemoveSelectedUniversalTriggerCommand.NotifyCanExecuteChanged();
        TestSelectedUniversalTriggerCommand.NotifyCanExecuteChanged();
        AddUniversalTriggerActionCommand.NotifyCanExecuteChanged();
        RemoveSelectedUniversalTriggerActionCommand.NotifyCanExecuteChanged();
        RemoveSelectedMovementRedeemSetCommand.NotifyCanExecuteChanged();
        DeleteAllMovementRedeemSetsCommand.NotifyCanExecuteChanged();
        RemoveSelectedAvatarScaleSetCommand.NotifyCanExecuteChanged();
        RemoveSelectedAvatarScaleRuleCommand.NotifyCanExecuteChanged();
        TestSelectedAvatarScaleRuleCommand.NotifyCanExecuteChanged();
        OpenBroadcasterLoginCommand.NotifyCanExecuteChanged();
        OpenBotLoginCommand.NotifyCanExecuteChanged();
        RefreshOscConnectionCommand.NotifyCanExecuteChanged();
        DeleteSelectedAvatarProfileCommand.NotifyCanExecuteChanged();
        DeleteAllAvatarProfilesCommand.NotifyCanExecuteChanged();
        SetSelectedAvatarProfileAsMasterCommand.NotifyCanExecuteChanged();
        ToggleSelectedAvatarRewardTestOverrideCommand.NotifyCanExecuteChanged();
        UseCurrentVrChatAvatarForProfileCommand.NotifyCanExecuteChanged();
        RefreshVrChatOscParametersCommand.NotifyCanExecuteChanged();
        RefreshRuleCommandStates();
    }

    private void RefreshRuleCommandStates()
    {
        RaiseRuleSelectionStateProperties();
        RefreshAvailableActionTypes();
        AddRuleCommand.NotifyCanExecuteChanged();
        AddAvatarSupporterTriggerCommand.NotifyCanExecuteChanged();
        AddAvatarChangeOverrideCommand.NotifyCanExecuteChanged();
        AddAvatarProfileCommand.NotifyCanExecuteChanged();
        RemoveSelectedRuleCommand.NotifyCanExecuteChanged();
        OpenSpecialRuleLockoutPickerCommand.NotifyCanExecuteChanged();
        OpenAvatarRouletPoolPickerCommand.NotifyCanExecuteChanged();
        EnableAllRulesCommand.NotifyCanExecuteChanged();
        DisableAllRulesCommand.NotifyCanExecuteChanged();
        DeleteAllRulesCommand.NotifyCanExecuteChanged();
        TestSelectedRuleCommand.NotifyCanExecuteChanged();
        AddUniversalTriggerCommand.NotifyCanExecuteChanged();
        RemoveSelectedUniversalTriggerCommand.NotifyCanExecuteChanged();
        EnableAllUniversalTriggersCommand.NotifyCanExecuteChanged();
        DisableAllUniversalTriggersCommand.NotifyCanExecuteChanged();
        DeleteAllUniversalTriggersCommand.NotifyCanExecuteChanged();
        TestSelectedUniversalTriggerCommand.NotifyCanExecuteChanged();
        ImportFoomaInteractionConfigCommand.NotifyCanExecuteChanged();
        AddUniversalTriggerActionCommand.NotifyCanExecuteChanged();
        RemoveSelectedUniversalTriggerActionCommand.NotifyCanExecuteChanged();
        AddMovementRedeemSetCommand.NotifyCanExecuteChanged();
        RemoveSelectedMovementRedeemSetCommand.NotifyCanExecuteChanged();
        DeleteAllMovementRedeemSetsCommand.NotifyCanExecuteChanged();
        AddAvatarScaleSetCommand.NotifyCanExecuteChanged();
        RemoveSelectedAvatarScaleSetCommand.NotifyCanExecuteChanged();
        AddAvatarScaleRuleCommand.NotifyCanExecuteChanged();
        RemoveSelectedAvatarScaleRuleCommand.NotifyCanExecuteChanged();
        EnableAllAvatarScaleRulesCommand.NotifyCanExecuteChanged();
        DisableAllAvatarScaleRulesCommand.NotifyCanExecuteChanged();
        DeleteAllAvatarScaleRulesCommand.NotifyCanExecuteChanged();
        TestSelectedAvatarScaleRuleCommand.NotifyCanExecuteChanged();
        OpenAvatarScaleRuleLockoutPickerCommand.NotifyCanExecuteChanged();
        DeleteSelectedAvatarProfileCommand.NotifyCanExecuteChanged();
        DeleteAllAvatarProfilesCommand.NotifyCanExecuteChanged();
        SetSelectedAvatarProfileAsMasterCommand.NotifyCanExecuteChanged();
        ToggleSelectedAvatarRewardTestOverrideCommand.NotifyCanExecuteChanged();
        UseCurrentVrChatAvatarForProfileCommand.NotifyCanExecuteChanged();
        UseCurrentAvatarForSupporterRuleCommand.NotifyCanExecuteChanged();
        UseCurrentAvatarForAvatarChangeRuleCommand.NotifyCanExecuteChanged();
        StopRewardFireSaleCommand.NotifyCanExecuteChanged();
        ResetRewardFireSaleProgressCommand.NotifyCanExecuteChanged();
        RemoveRewardFireSaleTierCommand.NotifyCanExecuteChanged();
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

    private void OpenTwitchDeveloperConsole()
    {
        OpenUri(TwitchDeveloperConsoleUri);
    }

    private void OpenKoFiSupportPage()
    {
        OpenUri(KoFiSupportUri);
    }

    private async Task OpenBugReportAsync()
    {
        var dialog = new VrcTwitchOscBridge.BugReportWindow(SelectedTheme)
        {
            Owner = Application.Current?.MainWindow
        };

        if (dialog.ShowDialog() != true)
        {
            return;
        }

        var diagnostics = dialog.IncludeSanitizedLogs
            ? bugReportService.BuildSanitizedDiagnostics(AppVersion, LogEntries.ToArray())
            : string.Empty;

        var submission = new BugReportSubmission(
            dialog.BugTitle,
            dialog.WhatHappened,
            dialog.ExpectedBehavior,
            dialog.StepsToReproduce,
            dialog.ContactName,
            GetAppVersionDisplay(),
            diagnostics);

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
            ? Settings.Broadcaster.IsConnected
            : Settings.Bot.IsConnected;

        if (hasExistingCode || isConnected)
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
            .Concat(GetAllMovementRules())
            .Concat(Settings.GlobalOverrideRules);
    }

    private ObservableCollection<TriggerRule> GetCurrentEditableRuleCollection()
    {
        if (IsViewingUniversalTriggers || IsViewingAvatarScaling)
        {
            return new ObservableCollection<TriggerRule>();
        }

        if (IsViewingSupporterOverrides)
        {
            return Settings.GlobalOverrideRules;
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
            BuildConfiguredSpecialRuleLockoutOptions())
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

        if (updatedRuleIds.SequenceEqual(currentRuleIds))
        {
            return;
        }

        SelectedRule.TemporarilyDisabledRuleIds = new ObservableCollection<Guid>(updatedRuleIds);
        RefreshSpecialRuleLockoutOptions();
        QueueSave();
        QueueBridgeRefresh();
        QueueManagedRewardSync(0);
        AppendLog(updatedRuleIds.Length == 0
            ? $"Cleared disable pairings for '{SelectedRule.DisplayTitle}'."
            : $"Updated disable pairings for '{SelectedRule.DisplayTitle}'.");
    }

    private IReadOnlyList<TriggerRuleReferenceOption> BuildAvailableAvatarScaleRuleLockoutOptions()
    {
        if (!IsViewingAvatarScaling || SelectedAvatarScaleSet is null || SelectedAvatarScaleRule is null)
        {
            return [];
        }

        var selectedRuleId = SelectedAvatarScaleRule.Id;
        var existingLockouts = SelectedAvatarScaleRule.TemporarilyDisabledScaleRuleIds.ToHashSet();

        return SelectedAvatarScaleSet.ScaleRules
            .Where(rule => rule.Id != selectedRuleId && !existingLockouts.Contains(rule.Id))
            .OrderBy(rule => GetAvatarScaleRuleLockoutDisplayLabel(rule), StringComparer.OrdinalIgnoreCase)
            .Select(rule => new TriggerRuleReferenceOption(rule.Id, GetAvatarScaleRuleLockoutDisplayLabel(rule)))
            .ToArray();
    }

    private IReadOnlyList<TriggerRuleReferenceOption> BuildConfiguredAvatarScaleRuleLockoutOptions()
    {
        if (!IsViewingAvatarScaling || SelectedAvatarScaleSet is null || SelectedAvatarScaleRule is null)
        {
            return [];
        }

        var scaleRulesById = SelectedAvatarScaleSet.ScaleRules.ToDictionary(rule => rule.Id);
        var configuredOptions = new List<TriggerRuleReferenceOption>();

        foreach (var blockedRuleId in SelectedAvatarScaleRule.TemporarilyDisabledScaleRuleIds)
        {
            if (blockedRuleId == SelectedAvatarScaleRule.Id)
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
        return IsViewingAvatarScaling
            && SelectedAvatarScaleSet is not null
            && SelectedAvatarScaleRule is not null;
    }

    private void OpenAvatarScaleRuleLockoutPicker()
    {
        if (!CanOpenAvatarScaleRuleLockoutPicker() || SelectedAvatarScaleSet is null || SelectedAvatarScaleRule is null)
        {
            return;
        }

        var dialog = new RuleLockoutPickerWindow(
            SelectedTheme,
            $"Scale Set: {SelectedAvatarScaleSet.DisplayTitle}",
            SelectedAvatarScaleRule.DisplayTitle,
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
            .Where(ruleId => ruleId != Guid.Empty && ruleId != SelectedAvatarScaleRule.Id)
            .Distinct()
            .ToArray();
        var currentRuleIds = SelectedAvatarScaleRule.TemporarilyDisabledScaleRuleIds
            .Where(ruleId => ruleId != Guid.Empty && ruleId != SelectedAvatarScaleRule.Id)
            .Distinct()
            .ToArray();

        if (updatedRuleIds.SequenceEqual(currentRuleIds))
        {
            return;
        }

        SelectedAvatarScaleRule.TemporarilyDisabledScaleRuleIds = new ObservableCollection<Guid>(updatedRuleIds);
        RaisePropertyChanged(nameof(AvatarScaleRuleLockoutSummaryText));
        QueueSave();
        QueueBridgeRefresh();
        QueueManagedRewardSync(0);
        AppendLog(updatedRuleIds.Length == 0
            ? $"Cleared scale disable pairings for '{SelectedAvatarScaleRule.DisplayTitle}'."
            : $"Updated scale disable pairings for '{SelectedAvatarScaleRule.DisplayTitle}'.");
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

        var configuredOptions = BuildConfiguredAvatarRouletPoolOptions(SelectedRule);
        var configuredIds = configuredOptions
            .Select(option => option.Id)
            .Where(avatarId => !string.IsNullOrWhiteSpace(avatarId))
            .ToHashSet(StringComparer.Ordinal);
        var availableOptions = BuildAllSelectableVrChatAvatarOptions()
            .Where(option => !configuredIds.Contains(option.Id))
            .ToArray();
        var dialog = new AvatarRouletPickerWindow(
            SelectedTheme,
            MasterAvatarProfile?.DisplayTitle ?? "Return Avatar",
            SelectedRule.RewardDisplayTitle,
            availableOptions,
            configuredOptions)
        {
            Owner = Application.Current?.MainWindow
        };

        if (dialog.ShowDialog() != true)
        {
            return;
        }

        var selectedOptions = dialog.SelectedAvatars
            .Where(option => !string.IsNullOrWhiteSpace(option.Id))
            .ToArray();
        var updatedAvatarIds = selectedOptions
            .Select(option => option.Id.Trim())
            .Distinct(StringComparer.Ordinal)
            .ToArray();
        var updatedAvatarNames = selectedOptions
            .Select(option => option.Name?.Trim() ?? string.Empty)
            .ToArray();
        var currentAvatarIds = SelectedRule.AvatarRouletAvatarIds
            .Where(avatarId => !string.IsNullOrWhiteSpace(avatarId))
            .Select(avatarId => avatarId.Trim())
            .Distinct(StringComparer.Ordinal)
            .ToArray();
        var currentAvatarNames = SelectedRule.AvatarRouletAvatarNames
            .Select(avatarName => avatarName?.Trim() ?? string.Empty)
            .ToArray();

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
        AppendLog(updatedAvatarIds.Length == 0
            ? $"Cleared the Avatar Roulette pool for '{SelectedRule.DisplayTitle}'."
            : $"Updated the Avatar Roulette pool for '{SelectedRule.DisplayTitle}'.");
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
        or PlayerMovementDirection.SpinRight;

    private static bool IsSupportedMovementRule(TriggerRule rule) =>
        rule.ActionType != OscActionType.PlayerMovement || IsSupportedMovementDirection(rule.MovementDirection);

    private static bool IsSupporterAvatarChangeOverride(TriggerRule rule) =>
        rule.ActionType is OscActionType.AvatarChange or OscActionType.AvatarRoulet;

    private void RaiseRuleSelectionStateProperties()
    {
        RaisePropertyChanged(nameof(IsViewingAvatarTriggers));
        RaisePropertyChanged(nameof(IsViewingMasterAvatar));
        RaisePropertyChanged(nameof(IsViewingMovementRedeems));
        RaisePropertyChanged(nameof(IsViewingSupporterOverrides));
        RaisePropertyChanged(nameof(IsViewingUniversalTriggers));
        RaisePropertyChanged(nameof(IsViewingAvatarScaling));
        RaisePropertyChanged(nameof(IsViewingRewardFireSale));
        RaisePropertyChanged(nameof(MovementRedeemSets));
        RaisePropertyChanged(nameof(MovementRedeemRules));
        RaisePropertyChanged(nameof(AvatarScaleSets));
        RaisePropertyChanged(nameof(AvatarScaleRules));
        RaisePropertyChanged(nameof(MasterAvatarDisplayName));
        RaisePropertyChanged(nameof(MasterAvatarRules));
        RaisePropertyChanged(nameof(SelectedRuleCollectionTitle));
        RaisePropertyChanged(nameof(SelectedRuleCollectionHelpText));
        RaisePropertyChanged(nameof(RuleLibraryHelpText));
        RaisePropertyChanged(nameof(AddRuleButtonText));
        RaisePropertyChanged(nameof(DeleteRuleButtonText));
        RaisePropertyChanged(nameof(DeleteAllRulesButtonText));
        RaisePropertyChanged(nameof(SelectedRuleEmptyStateText));
        RaisePropertyChanged(nameof(SelectedAvatarProfileStatusText));
        RaisePropertyChanged(nameof(SelectedAvatarSetupTitle));
        RaisePropertyChanged(nameof(SelectedAvatarNameFieldLabel));
        RaisePropertyChanged(nameof(SelectedAvatarPickerLabel));
        RaisePropertyChanged(nameof(UseCurrentAvatarButtonText));
        RaisePropertyChanged(nameof(MasterAvatarReturnText));
        RaisePropertyChanged(nameof(ManagedChannelPointRewardHelpText));
        RaisePropertyChanged(nameof(UniversalManagedChannelPointRewardHelpText));
        RaisePropertyChanged(nameof(UniversalManagedRewardStatusText));
        RaisePropertyChanged(nameof(AvatarScaleRuntimeStatusText));
        RefreshRewardFireSaleStateProperties();
        RaisePropertyChanged(nameof(IsSetTriggerMasterRewardEditorVisible));
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
    }

    private void RefreshAvailableActionTypes()
    {
        RaisePropertyChanged(nameof(AvailableActionTypesForSelectedContext));
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
        RaisePropertyChanged(nameof(HasAvatarMixRedeems));
        RaisePropertyChanged(nameof(SelectedAvatarOtherRedeems));
        RaisePropertyChanged(nameof(HasAvatarOtherRedeems));
        RaisePropertyChanged(nameof(IsSetTriggerMasterRewardEditorVisible));
    }

    private void RaiseSupporterRuleGroupProperties()
    {
        RefreshSupporterRuleScopeLabels();
        RaisePropertyChanged(nameof(SelectedAvatarSupporterRules));
        RaisePropertyChanged(nameof(AvatarChangeOverrideRules));
        RaisePropertyChanged(nameof(GlobalSupporterRules));
        RaisePropertyChanged(nameof(HasSelectedAvatarSupporterRules));
        RaisePropertyChanged(nameof(HasAvatarChangeOverrideRules));
        RaisePropertyChanged(nameof(HasGlobalSupporterRules));
    }

    private IReadOnlyList<UniversalTriggerRule> GetUniversalTriggersByType(UniversalTriggerType triggerType)
    {
        return Settings.UniversalTriggers
            .Where(trigger => trigger.TriggerType == triggerType)
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
        RaisePropertyChanged(nameof(UniversalChatCommandTriggers));
        RaisePropertyChanged(nameof(UniversalChannelPointRewardTriggers));
        RaisePropertyChanged(nameof(UniversalBitsTriggers));
        RaisePropertyChanged(nameof(UniversalSubscriptionTriggers));
        RaisePropertyChanged(nameof(UniversalGiftSubscriptionTriggers));
        RaisePropertyChanged(nameof(UniversalFollowTriggers));
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
        RaisePropertyChanged(nameof(UniversalManagedRewardStatusText));
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

    private static UniversalTriggerRule CreateDefaultUniversalTrigger()
    {
        return new UniversalTriggerRule
        {
            Name = "New Universal Trigger",
            TriggerType = UniversalTriggerType.ChatCommand,
            CommandText = "!trigger",
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
            HeightMultiplier = 1.25,
            Preset = AvatarScalePreset.Normal,
            ActiveTimeSeconds = 0,
            RestoreMode = AvatarScaleRestoreMode.ConfiguredHeight,
            RestoreHeightMeters = 1.6,
            SmoothTransitionSeconds = 0
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

    private static TriggerRule CreateDefaultAvatarChangeOverrideRule()
    {
        var rule = CreateBaseRule("New Avatar Change Override", TwitchTriggerType.Bits);
        rule.ActionType = OscActionType.AvatarChange;
        rule.SupporterAvatarProfileId = Guid.Empty;
        rule.SupporterAvatarId = string.Empty;
        rule.SupporterAvatarName = string.Empty;
        rule.MinimumAmount = 100;
        rule.DurationSeconds = 20;
        rule.CooldownSeconds = 30;
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
        if (!Settings.VrChat.IsConnected || string.IsNullOrWhiteSpace(avatarId))
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
        if (!Settings.VrChat.IsConnected)
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
            VrChatOscParameterStatus = IsViewingSupporterOverrides
                ? T("Refresh avatars first so Crystal Relay knows which avatar you are using.")
                : T("Pick the avatar first, then refresh its OSC parameters.");
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
            RaisePropertyChanged(nameof(UniversalManagedRewardStatusText));
        });
    }

    private void RefreshSetTriggerParameterOptions()
    {
        RunOnUi(() =>
        {
            var selectedAvatarId = GetSelectedParameterCacheAvatarId();
            RefreshSetTriggerParameterOptionsCore(selectedAvatarId);
            UpdateVrChatOscParameterStatus(selectedAvatarId);
            RaisePropertyChanged(nameof(UniversalManagedRewardStatusText));
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
        if (!Settings.VrChat.IsConnected)
        {
            VrChatOscParameterStatus = T("Connect VRChat to load avatar parameters.");
        }
        else if (string.IsNullOrWhiteSpace(selectedAvatarId))
        {
            VrChatOscParameterStatus = IsViewingSupporterOverrides
                ? T("Refresh avatars once so Crystal Relay can match supporter overrides to your current avatar.")
                : T("Pick the avatar first, then Crystal Relay can use its saved OSC parameters.");
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
                ? T("No saved OSC parameters yet. Wear this avatar in VRChat, then press Refresh OSC Parameters.")
                : T("No saved OSC parameters for this avatar yet. Switch to it in VRChat, then press Refresh OSC Parameters.");
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
        if (IsViewingSupporterOverrides)
        {
            var supporterAvatarId = SelectedRule?.SupporterAvatarId?.Trim() ?? string.Empty;
            if (!string.IsNullOrWhiteSpace(supporterAvatarId))
            {
                return supporterAvatarId;
            }

            return Settings.VrChat.CurrentAvatarId?.Trim() ?? string.Empty;
        }

        if (IsViewingUniversalTriggers)
        {
            return Settings.VrChat.CurrentAvatarId?.Trim() ?? string.Empty;
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
            QueueManagedRewardSync(0, ManagedRewardSyncReason.AvatarChanged);
        }

        _ = EnsureSelectedAvatarParameterCacheLoadedAsync();
        QueueCurrentVrChatOscParameterRefresh(normalizedAvatarId, queueManagedRewardSync);
    }

    private void HandleSharedReturnAvatarChangedByBridge(string avatarId, string avatarName)
    {
        ApplySharedReturnAvatarSelection(avatarId, avatarName, saveImmediately: true);
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
        if (!Settings.VrChat.IsConnected || string.IsNullOrWhiteSpace(Settings.VrChat.UserId))
        {
            return [];
        }

        var localParameters = await vrChatLocalOscCacheService.LoadAvatarParametersAsync(
            Settings.VrChat.UserId,
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
            Settings.VrChat.UserId,
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

        if (!Settings.VrChat.IsConnected
            || string.IsNullOrWhiteSpace(Settings.VrChat.UserId)
            || string.IsNullOrWhiteSpace(avatarId))
        {
            return false;
        }

        var filePath = VrChatLocalOscCacheService.GetAvatarOscFilePath(Settings.VrChat.UserId, avatarId);
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
        if (!Settings.VrChat.IsConnected
            || string.IsNullOrWhiteSpace(Settings.VrChat.UserId)
            || string.IsNullOrWhiteSpace(normalizedAvatarId))
        {
            return;
        }

        cachedVrChatParametersByAvatarId[normalizedAvatarId] = [.. parameters];
        await settingsStore.SaveVrChatOscParameterCacheAsync(
            Settings.VrChat.UserId,
            normalizedAvatarId,
            parameters,
            cancellationToken);

        if (string.Equals(Settings.VrChat.CurrentAvatarId?.Trim() ?? string.Empty, normalizedAvatarId, StringComparison.Ordinal))
        {
            RunOnUi(() => RaisePropertyChanged(nameof(UniversalManagedRewardStatusText)));
            if (queueManagedRewardSync)
            {
                QueueManagedRewardSync(0, ManagedRewardSyncReason.RuntimeAvailability);
            }
        }
    }

    private void QueueCurrentVrChatOscParameterRefresh(string avatarId, bool queueManagedRewardSync = true)
    {
        if (!isInitialized || isShuttingDown || !Settings.VrChat.IsConnected)
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

    private static string DetectBetaBuildLabel()
    {
        try
        {
            var markerPath = Path.Combine(AppContext.BaseDirectory, BetaBuildMarkerFileName);
            if (!File.Exists(markerPath))
            {
                return string.Empty;
            }

            var label = File.ReadAllText(markerPath).Trim();
            if (string.IsNullOrWhiteSpace(label))
            {
                return "Beta";
            }

            if (label.StartsWith("beta", StringComparison.OrdinalIgnoreCase))
            {
                var betaNumber = label[4..].Trim(' ', '-', '_');
                if (int.TryParse(betaNumber, NumberStyles.Integer, CultureInfo.InvariantCulture, out var parsedBetaNumber) &&
                    parsedBetaNumber > 0)
                {
                    return $"Beta {parsedBetaNumber.ToString(CultureInfo.InvariantCulture)}";
                }
            }

            return label.Length <= 40 ? label : label[..40];
        }
        catch
        {
            return string.Empty;
        }
    }

    private static bool DetectTestBuild()
    {
        try
        {
            return File.Exists(Path.Combine(AppContext.BaseDirectory, TestBuildMarkerFileName));
        }
        catch
        {
            return false;
        }
    }

    private static string GetAppVersionDisplay()
    {
        var builder = new StringBuilder(AppVersion);
        if (!string.IsNullOrWhiteSpace(BetaBuildLabel))
        {
            builder.Append(" - ");
            builder.Append(BetaBuildLabel);
        }

        if (IsTestBuild)
        {
            builder.Append(LocalizationService.Translate(" - Test Build"));
        }

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
        Accounts,
        Visuals
    }

    private enum RuleListView
    {
        AvatarTriggers,
        MasterAvatar,
        MovementRedeems,
        SupporterOverrides,
        UniversalTriggers,
        AvatarScaling,
        RewardFireSale
    }

    private sealed record LinkedTwitchRewardReference(string RewardId, string DisplayTitle);
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

public sealed record TwitchRewardSyncModeOption(TwitchRewardSyncMode Value, string Label)
{
    public override string ToString() => Label;
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

public sealed class TwitchChatMessageEntry : ObservableObject
{
    private static readonly SolidColorBrush DefaultNameBrush = CreateFrozenBrush("#F5EEFF");
    private static readonly SolidColorBrush BubblegumNameBrush = CreateFrozenBrush("#5A426B");
    private static readonly Color DarkCardReferenceColor = Color.FromRgb(40, 23, 60);
    private ChatTimestampFormat timestampFormat;

    public TwitchChatMessageEntry(
        string userDisplayName,
        string messageText,
        string userColor,
        IReadOnlyList<string> badgeImageUrls,
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
        string supportMessage = "")
    {
        Kind = Enum.IsDefined(kind) ? kind : TwitchChatMessageEntryKind.Chat;
        UserDisplayName = string.IsNullOrWhiteSpace(userDisplayName) ? "Viewer" : userDisplayName.Trim();
        MessageText = messageText;
        BadgeImageUrls = badgeImageUrls;
        InlineFragments = inlineFragments.Count == 0
            ? [new TwitchChatInlineFragment(TwitchChatInlineFragmentKind.Text, messageText, string.Empty)]
            : inlineFragments;
        ShouldPlayViewerSound = shouldPlayViewerSound;
        ReceivedAt = receivedAt;
        RawUserColor = userColor;
        NameBrush = ParseNameBrush(userColor, theme);
        RewardTitle = string.IsNullOrWhiteSpace(rewardTitle) ? MessageText.Trim() : rewardTitle.Trim();
        RewardCost = Math.Max(0, rewardCost);
        RewardUserInput = rewardUserInput?.Trim() ?? string.Empty;
        SupportAmount = Math.Max(0, supportAmount);
        SupportTier = supportTier?.Trim() ?? string.Empty;
        SupportMonths = Math.Max(0, supportMonths);
        SupportMessage = supportMessage?.Trim() ?? string.Empty;
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

    public string MessageText { get; }

    public IReadOnlyList<string> BadgeImageUrls { get; }

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

public sealed record PlayerMovementOption(PlayerMovementDirection Value, string Label);

public sealed record AvatarScaleSubscriptionTierOption(string Value, string Label)
{
    public override string ToString() => Label;
}

public sealed record TriggerRuleReferenceOption(Guid RuleId, string Label);
