using System.Collections.ObjectModel;
using System.Collections.Specialized;
using System.ComponentModel;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Reflection;
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
        BroadcasterTokenRefreshRequired
    }

    private const string TwitchActivationUri = "https://www.twitch.tv/activate";
    private const string TwitchDeveloperConsoleUri = "https://dev.twitch.tv/console/apps";
    private const string SettingsTestAudioRelativePath = "Assets\\engineer_no01.mp3";
    private const string TestBuildMarkerFileName = "test-build.flag";
    private const int MaxLogEntryCount = 200;
    private const int MaxChatMessageCount = 250;
    private static readonly TimeSpan ManagedRewardCreateBackoffWindow = TimeSpan.FromSeconds(45);
    private static readonly TimeSpan ThrottledRewardSyncLogWindow = TimeSpan.FromSeconds(30);
    private static readonly TimeSpan VrChatLocalStatePollInterval = TimeSpan.FromSeconds(2);
    private static readonly TimeSpan VrChatCurrentAvatarPollInterval = TimeSpan.FromSeconds(15);
    private static readonly TimeSpan VrChatOscParameterAutoRefreshInitialDelay = TimeSpan.FromSeconds(2);
    private static readonly TimeSpan VrChatOscParameterAutoRefreshRetryDelay = TimeSpan.FromSeconds(3);
    private static readonly TimeSpan TwitchPublicSessionWindow = TimeSpan.FromDays(30);
    private static readonly TimeSpan AboutProfileRefreshInterval = TimeSpan.FromMinutes(5);
    private const int VrChatOscParameterAutoRefreshPassCount = 4;
    private static readonly string AppVersion = GetAppVersion();
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
        nameof(TriggerRule.TemporarilyDisabledRuleIds),
        nameof(TriggerRule.BotMessageTemplate)
    };
    private static readonly HashSet<string> RulePropertiesRequiringManagedRewardSync = new(StringComparer.Ordinal)
    {
        nameof(TriggerRule.ActionType),
        nameof(TriggerRule.ChannelPointRewardId),
        nameof(TriggerRule.ChannelPointRewardTitle),
        nameof(TriggerRule.ChannelPointRewardCost),
        nameof(TriggerRule.ManagedRewardReadyColor),
        nameof(TriggerRule.ManagedRewardCooldownColor),
        nameof(TriggerRule.ParameterName),
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
        nameof(AvatarTriggerProfile.AvatarId)
    };
    private static readonly HashSet<string> AvatarProfilePropertiesRequiringManagedRewardSync = new(StringComparer.Ordinal)
    {
        nameof(AvatarTriggerProfile.IsEnabled),
        nameof(AvatarTriggerProfile.IsMasterProfile),
        nameof(AvatarTriggerProfile.AvatarId),
        nameof(AvatarTriggerProfile.IsRewardTestOverrideEnabled)
    };
    private static readonly HashSet<string> UniversalTriggerPropertiesRequiringManagedRewardSync = new(StringComparer.Ordinal)
    {
        nameof(UniversalTriggerRule.IsEnabled),
        nameof(UniversalTriggerRule.TriggerType),
        nameof(UniversalTriggerRule.RewardId),
        nameof(UniversalTriggerRule.RewardTitle),
        nameof(UniversalTriggerRule.RewardCost),
        nameof(UniversalTriggerRule.ManagedRewardReadyColor),
        nameof(UniversalTriggerRule.ManagedRewardCooldownColor)
    };
    private static readonly HashSet<string> UniversalTriggerActionPropertiesRequiringManagedRewardSync = new(StringComparer.Ordinal)
    {
        nameof(UniversalTriggerAction.OscAddress),
        nameof(UniversalTriggerAction.TargetValue),
        nameof(UniversalTriggerAction.DefaultValue),
        nameof(UniversalTriggerAction.DurationSeconds)
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
    private AvatarTriggerProfile? selectedAvatarProfile;
    private VrChatOscParameterSummary? selectedAvatarParameterOption;
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
    private bool isApplyingMasterAvatarDefaults;
    private bool isRefreshingVrChatAvatarSelectionOptions;
    private bool isSynchronizingManagedRewards;
    private bool isSwitchingRuleView;
    private bool isShuttingDown;
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
    private FileSystemWatcher? vrChatLocalOscWatcher;
    private readonly List<VrChatAvatarSummary> availableVrChatAvatars = [];
    private readonly Dictionary<string, string> availableVrChatAvatarNamesById = new(StringComparer.Ordinal);
    private readonly Dictionary<string, List<VrChatOscParameterSummary>> cachedVrChatParametersByAvatarId = new(StringComparer.Ordinal);
    private readonly Dictionary<string, DateTime> vrChatLocalOscAvatarWriteTimes = new(StringComparer.Ordinal);
    private readonly HashSet<string> retiredManagedRewardIds = new(StringComparer.Ordinal);
    private readonly Dictionary<string, DateTimeOffset> managedRewardCreateBackoffByTitle = new(StringComparer.OrdinalIgnoreCase);
    private readonly Dictionary<string, DateTimeOffset> throttledLogExpiryByKey = new(StringComparer.Ordinal);
    private readonly List<ActionTypeOption> allActionTypes;
    private DateTimeOffset aboutProfilesLastRefreshedAt = DateTimeOffset.MinValue;
    private DateTime latestLocalVrChatAvatarWriteTimeUtc = DateTime.MinValue;
    private string vrChatOutputLogPath = string.Empty;
    private long vrChatOutputLogPosition;
    private string lastDetectedVrChatAvatarId = string.Empty;
    private Guid lastSelectedAvatarProfileId = Guid.Empty;
    private Guid lastSelectedMasterRuleId = Guid.Empty;
    private Guid lastSelectedMovementRuleId = Guid.Empty;
    private Guid lastSelectedSupporterRuleId = Guid.Empty;
    private Guid lastSelectedUniversalTriggerId = Guid.Empty;
    private AppLanguage activeLanguageAtStartup = AppLanguage.SystemDefault;
    private ICollectionView? universalTriggersGroupedView;
    private bool isUniversalChatCommandsExpanded = true;
    private bool isUniversalChannelPointRewardsExpanded = true;
    private bool isUniversalBitsExpanded = true;
    private bool isUniversalSubscriptionsExpanded = true;
    private bool isUniversalGiftSubscriptionsExpanded = true;
    private bool isUniversalFollowsExpanded = true;

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
        AvatarRuleProfiles = [];
        ProfileAvatarOptions = [];
        VrChatAvatarOptions = [];
        VrChatResetAvatarOptions = [];
        AvatarParameterOptions = [];
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
        ActionTypes = [.. allActionTypes];
        ThemeOptions =
        [
            new ThemeOption(AppTheme.VoidCrystal, "Void Crystal"),
            new ThemeOption(AppTheme.Custom, "Custom"),
            new ThemeOption(AppTheme.TreetendersArm, "Treetender's Arm"),
            new ThemeOption(AppTheme.DreamScape, "Dream Scape"),
            new ThemeOption(AppTheme.MainFrame, "MainFrame"),
            new ThemeOption(AppTheme.TrashKitty, "Trash Kitty"),
            new ThemeOption(AppTheme.CarrotPatch, "Carrot Patch"),
            new ThemeOption(AppTheme.Bubblegum, "Bubblegum"),
            new ThemeOption(AppTheme.CosmicPuppyGirl, "Cosmic Puppy Girl"),
            new ThemeOption(AppTheme.PeachesAndCream, "Peaches & Cream"),
            new ThemeOption(AppTheme.MoonBunnyWink, "Moon Bunny Wink"),
            new ThemeOption(AppTheme.DreadNightBar, "Dread Night Bar"),
            new ThemeOption(AppTheme.Baked, "Baked")
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
        RefreshOscConnectionCommand = new AsyncRelayCommand(RefreshOscConnectionAsync);
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
        RefreshVrChatOscParametersCommand = new AsyncRelayCommand(RefreshVrChatOscParametersAsync);

        AddRuleCommand = new RelayCommand(AddRule);
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
        OpenSpecialRuleLockoutPickerCommand = new RelayCommand(OpenSpecialRuleLockoutPicker, CanOpenSpecialRuleLockoutPicker);
        OpenAvatarRouletPoolPickerCommand = new RelayCommand(OpenAvatarRouletPoolPicker, CanOpenAvatarRouletPoolPicker);

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
            QueueManagedRewardSync(0);
            _ = RefreshAboutProfilesAsync();
        });
        bridgeCoordinator.ChatMessageReceived += message => RunOnUi(() => AppendChatMessage(message));
        bridgeCoordinator.VrChatAvatarChanged += avatarId => RunOnUi(() => HandleVrChatAvatarChangedByBridge(avatarId));
        bridgeCoordinator.SharedReturnAvatarChanged += (avatarId, avatarName) => RunOnUi(() => HandleSharedReturnAvatarChangedByBridge(avatarId, avatarName));
        bridgeCoordinator.StreamStateChanged += isLive => RunOnUi(() => HandleBroadcasterLiveStateChanged(isLive));
        bridgeCoordinator.ManagedRewardAvailabilityChanged += () => RunOnUi(() => QueueManagedRewardSync(0));

    }

    public AppSettings Settings
    {
        get => settings;
        private set => SetProperty(ref settings, value);
    }

    public ObservableCollection<string> LogEntries { get; }

    public ObservableCollection<TwitchChatMessageEntry> ChatMessages { get; }

    public ObservableCollection<TwitchRewardOption> RewardOptions { get; }

    public ObservableCollection<AvatarTriggerProfile> AvatarRuleProfiles { get; }

    public ObservableCollection<TriggerRule> MasterAvatarRules => MasterAvatarProfile?.ChannelPointRules ?? emptyMasterAvatarRules;

    public ObservableCollection<VrChatAvatarOption> ProfileAvatarOptions { get; }

    public ObservableCollection<VrChatAvatarOption> VrChatAvatarOptions { get; }

    public ObservableCollection<VrChatAvatarOption> VrChatResetAvatarOptions { get; }

    public ObservableCollection<VrChatOscParameterSummary> AvatarParameterOptions { get; }

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
                    QueueManagedRewardSync(0);
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
                SelectedRule.ParameterName = value.Address;
            }
        }
    }

    public IReadOnlyList<TwitchTriggerType> TriggerTypes { get; }

    public IReadOnlyList<TwitchTriggerType> OverrideTriggerTypes { get; }

    public IReadOnlyList<UniversalTriggerType> UniversalTriggerTypes { get; }

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

    public string WindowTitle => TF("Crystal Relay v{0}{1} | Twitch to OSC", AppVersion, GetTestBuildTitleSuffix());

    public string WindowHeaderSubtitle => TF("Twitch to OSC | v{0}{1}", AppVersion, GetTestBuildTitleSuffix());

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

    public bool IsViewingAvatarTriggers => activeRuleListView == RuleListView.AvatarTriggers;

    public bool IsViewingMasterAvatar => activeRuleListView == RuleListView.MasterAvatar;

    public bool IsViewingMovementRedeems => activeRuleListView == RuleListView.MovementRedeems;

    public bool IsViewingSupporterOverrides => activeRuleListView == RuleListView.SupporterOverrides;

    public bool IsViewingUniversalTriggers => activeRuleListView == RuleListView.UniversalTriggers;

    public IReadOnlyList<UniversalTriggerRule> UniversalChatCommandTriggers => GetUniversalTriggersByType(UniversalTriggerType.ChatCommand);

    public IReadOnlyList<UniversalTriggerRule> UniversalChannelPointRewardTriggers => GetUniversalTriggersByType(UniversalTriggerType.ChannelPointReward);

    public IReadOnlyList<UniversalTriggerRule> UniversalBitsTriggers => GetUniversalTriggersByType(UniversalTriggerType.Bits);

    public IReadOnlyList<UniversalTriggerRule> UniversalSubscriptionTriggers => GetUniversalTriggersByType(UniversalTriggerType.Subscription);

    public IReadOnlyList<UniversalTriggerRule> UniversalGiftSubscriptionTriggers => GetUniversalTriggersByType(UniversalTriggerType.GiftSubscription);

    public IReadOnlyList<UniversalTriggerRule> UniversalFollowTriggers => GetUniversalTriggersByType(UniversalTriggerType.Follow);

    public ICollectionView UniversalTriggersGroupedView => universalTriggersGroupedView ??= CreateUniversalTriggersGroupedView();

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

    public string SelectedRuleCollectionTitle => IsViewingUniversalTriggers
        ? T("Universal Triggers")
        : IsViewingSupporterOverrides
        ? T("Bits + Subs Overrides")
        : IsViewingMovementRedeems
            ? T("Movement Redeems")
        : IsViewingMasterAvatar
            ? T("Avatar Change Redeems")
            : T("Avatar Redeems");

    public string SelectedRuleCollectionHelpText => IsViewingUniversalTriggers
        ? T("Use this list for imported or universal Twitch interactions. These triggers can listen for chat commands, channel point rewards, bits, subscriptions, gift subs, and follows without mixing into avatar sets or paid override rules.")
        : IsViewingSupporterOverrides
        ? T("Use this list for paid Twitch triggers like bits and subscriptions. Add Override creates a paid rule, Delete Override removes the selected one, and Enable All or Disable All controls the full override list at once. Bits and subs ignore avatar matching, so these rules stay global instead of waiting for one specific avatar.")
        : IsViewingMovementRedeems
            ? T("Use this list for global movement rules. Add Movement Redeem creates another movement trigger, Delete Movement Redeem removes the selected one, and Enable All or Disable All controls the whole movement library at once. Movement rules are global, so they are not tied to one avatar set.")
        : IsViewingMasterAvatar
            ? T("Use this list for avatar-switch rules that belong to Avatar Change Setup. Add Avatar Switch creates a direct Avatar Change rule or an Avatar Roulette rule, Delete Avatar Switch removes the selected one, and Enable All or Disable All controls the full avatar-switch list. These rules only turn on while you are on the shared return avatar unless a timed avatar switch is already active.")
        : SelectedAvatarProfile is null
            ? T("Use Avatar Sets to build per-avatar rule groups. Add Avatar Set creates another avatar group, Delete Avatar Set removes the selected one, and Delete All Avatar Sets clears the full set list. Each set becomes active only when Crystal Relay detects that exact avatar.")
            : T("This list holds the rules for one avatar set. Pick the avatar once, then add and manage the redeems that should only turn on while you are using that avatar.");

    public string RuleLibraryHelpText => IsViewingUniversalTriggers
        ? T("This tab is for universal Twitch interaction triggers. Import a Fooma Twitch Interaction config here, then test and adjust the OSC actions without changing the existing Avatar Sets, Movement Redeems, Avatar Change, or Bits + Subs override sections.")
        : IsViewingSupporterOverrides
        ? T("This tab is for paid Twitch overrides. Use it when you want bits or subscriptions to trigger a rule regardless of which avatar you are using. The controls here manage those paid global rules separately from avatar-based redeems.")
        : IsViewingMovementRedeems
            ? T("This tab is for global movement redeems like forward, back, left, right, and spin. These rules work across every avatar instead of belonging to one avatar set, so viewers can still trigger movement actions after you swap avatars.")
            : IsViewingMasterAvatar
                ? T("This tab is for Avatar Change Setup. Pick the shared return avatar on the right, then build direct avatar swaps or Avatar Roulette rules here. Timed avatar-switch rules return to that shared return avatar when they finish.")
                : T("This tab is for Avatar Sets. Use it to group redeems by the avatar they belong to, then pick a set below to edit the rules inside it. Crystal Relay uses current-avatar detection so only the set for the avatar you are actually wearing turns on.");

    public string AddRuleButtonText => IsViewingUniversalTriggers
        ? T("Add Universal Trigger")
        : IsViewingSupporterOverrides
        ? T("Add Override")
        : IsViewingMovementRedeems
            ? T("Add Movement Redeem")
        : IsViewingMasterAvatar
            ? T("Add Avatar Switch")
            : T("Add Redeem");

    public string DeleteRuleButtonText => IsViewingUniversalTriggers
        ? T("Delete Universal Trigger")
        : IsViewingSupporterOverrides
        ? T("Delete Override")
        : IsViewingMovementRedeems
            ? T("Delete Movement Redeem")
        : IsViewingMasterAvatar
            ? T("Delete Avatar Switch")
            : T("Delete Redeem");

    public string DeleteAllRulesButtonText => IsViewingUniversalTriggers
        ? T("Delete All Universal Triggers")
        : IsViewingSupporterOverrides
        ? T("Delete All Overrides")
        : IsViewingMovementRedeems
            ? T("Delete All Movement Redeems")
        : IsViewingMasterAvatar
            ? T("Delete All Avatar Switches")
            : T("Delete All Redeems");

    public string SelectedRuleEmptyStateText => IsViewingUniversalTriggers
        ? T("Import a Fooma config or add a universal trigger to edit it.")
        : IsViewingSupporterOverrides
        ? T("Add a bits or subs redeem to edit it.")
        : IsViewingMovementRedeems
            ? T("Add a movement redeem to edit it.")
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

            if (!BroadcasterCanManageRewards)
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

            if (!BroadcasterCanManageRewards)
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

    public IReadOnlyList<ActionTypeOption> AvailableActionTypesForSelectedContext
    {
        get
        {
            if (IsViewingMasterAvatar)
            {
                return ActionTypes.Where(option => option.Value is OscActionType.AvatarChange or OscActionType.AvatarRoulet).ToArray();
            }

            if (IsViewingMovementRedeems)
            {
                return ActionTypes.Where(option => option.Value == OscActionType.PlayerMovement).ToArray();
            }

            if (IsViewingSupporterOverrides)
            {
                return ActionTypes.Where(option => option.Value is OscActionType.AvatarParameter or OscActionType.AvatarChange).ToArray();
            }

            return ActionTypes.Where(option => option.Value == OscActionType.AvatarParameter).ToArray();
        }
    }

    public string SelectedAvatarProfileStatusText
    {
        get
        {
            if (SelectedAvatarProfile is null)
            {
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

    public bool IsBubblegumThemeSelected => SelectedTheme == AppTheme.Bubblegum;

    public bool IsCosmicPuppyGirlThemeSelected => SelectedTheme == AppTheme.CosmicPuppyGirl;

    public bool IsPeachesAndCreamThemeSelected => SelectedTheme == AppTheme.PeachesAndCream;

    public bool IsMoonBunnyWinkThemeSelected => SelectedTheme == AppTheme.MoonBunnyWink;

    public bool IsDreadNightBarThemeSelected => SelectedTheme == AppTheme.DreadNightBar;

    public bool IsBakedThemeSelected => SelectedTheme == AppTheme.Baked;

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

    public AsyncRelayCommand RefreshOscConnectionCommand { get; }

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

    public RelayCommand AddAvatarProfileCommand { get; }

    public RelayCommand DeleteSelectedAvatarProfileCommand { get; }

    public RelayCommand DeleteAllAvatarProfilesCommand { get; }

    public RelayCommand SetSelectedAvatarProfileAsMasterCommand { get; }

    public RelayCommand ToggleSelectedAvatarRewardTestOverrideCommand { get; }

    public RelayCommand ToggleEmergencyRedeemStopCommand { get; }

    public RelayCommand ToggleDesktopModeInputLockCommand { get; }

    public RelayCommand UseCurrentVrChatAvatarForProfileCommand { get; }

    public AsyncRelayCommand RefreshVrChatOscParametersCommand { get; }

    public RelayCommand AddRuleCommand { get; }

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

    public RelayCommand OpenSpecialRuleLockoutPickerCommand { get; }

    public RelayCommand OpenAvatarRouletPoolPickerCommand { get; }

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
        UpdateAvatarProfileActivityStates();
        SelectedAvatarProfile = AvatarRuleProfiles.FirstOrDefault();
        SelectedRule = SelectedAvatarProfile?.ChannelPointRules.FirstOrDefault();
        RefreshAvailableActionTypes();
        RefreshVrChatAvatarSelectionOptions();
        _ = EnsureSelectedAvatarParameterCacheLoadedAsync();
        RefreshRuntimeSummary();
        UpdateAccountStatuses();
        isInitialized = true;
        if (normalizedChatCommandFallbacks || fusedUniversalCommandFallbacks > 0)
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
        QueueManagedRewardSync();
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
        managedRewardSyncCancellation?.Cancel();
        managedRewardSyncCancellation?.Dispose();
        vrChatCurrentAvatarRefreshCancellation?.Cancel();
        vrChatCurrentAvatarRefreshCancellation?.Dispose();
        vrChatOscParameterRefreshCancellation?.Cancel();
        vrChatOscParameterRefreshCancellation?.Dispose();
        vrChatLocalOscScanCancellation?.Cancel();
        vrChatLocalOscScanCancellation?.Dispose();
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
        RaisePropertyChanged(nameof(IsBubblegumThemeSelected));
        RaisePropertyChanged(nameof(IsCosmicPuppyGirlThemeSelected));
        RaisePropertyChanged(nameof(IsPeachesAndCreamThemeSelected));
        RaisePropertyChanged(nameof(IsMoonBunnyWinkThemeSelected));
        RaisePropertyChanged(nameof(IsDreadNightBarThemeSelected));
        RaisePropertyChanged(nameof(IsBakedThemeSelected));
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
            QueueSave();
            AppendLog(TF("Connected VRChat avatar access as {0}.", account.DisplayName));
            StartOrRefreshVrChatLocalOscWatcher();
            QueueLocalVrChatOscAvatarScan(0);
            QueueCurrentVrChatLocalStateRefresh(0);
            await RefreshVrChatAvatarsAsync(forceRemoteRefresh: true);
        }
        catch (Exception ex)
        {
            Settings.VrChat.Clear();
            RaiseVrChatConnectionStateProperties();
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
        Settings.VrChat.Clear();
        ClearAvailableVrChatAvatars();
        cachedVrChatParametersByAvatarId.Clear();
        AvatarParameterOptions.Clear();
        SelectedAvatarParameterOption = null;
        VrChatOscParameterStatus = T("Connect VRChat to load avatar parameters.");
        RaiseVrChatConnectionStateProperties();
        VrChatStatus = T("VRChat avatar access is not connected.");
        VrChatAvatarStatus = T("Connect VRChat to load avatar choices.");
        ResetVrChatLocalRuntimeTracking();
        DisposeVrChatLocalOscWatcher();
        QueueSave();
        RefreshCommandStates();
        UpdateAvatarProfileActivityStates();
        RefreshVrChatAvatarSelectionOptions();
        AppendLog(T("Disconnected VRChat avatar access."));
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
                Settings.VrChat.Clear();
                ClearAvailableVrChatAvatars();
                await settingsStore.ClearVrChatAvatarCacheAsync(CancellationToken.None);
                await settingsStore.ClearVrChatOscParameterCacheAsync(CancellationToken.None);
                cachedVrChatParametersByAvatarId.Clear();
                AvatarParameterOptions.Clear();
                SelectedAvatarParameterOption = null;
                RaiseVrChatConnectionStateProperties();
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
            var needsAvatarChangeOptions = SelectedRule?.ActionType == OscActionType.AvatarChange;
            if (!needsProfileAvatarOptions && !needsAvatarChangeOptions)
            {
                RaisePropertyChanged(nameof(HasVrChatAvatarOptions));
                return;
            }

            var selectedProfileAvatarId = SelectedAvatarProfile?.AvatarId;
            var selectedProfileAvatarName = SelectedAvatarProfile?.AvatarName;
            var selectedAvatarId = SelectedRule?.ActionType == OscActionType.AvatarChange
                ? SelectedRule.AvatarChangeTargetId
                : string.Empty;
            var selectedResetAvatarId = SelectedRule?.ActionType == OscActionType.AvatarChange
                ? SelectedRule.AvatarChangeResetId
                : string.Empty;
            var selectedAvatarName = SelectedRule?.ActionType == OscActionType.AvatarChange
                ? SelectedRule.AvatarTargetName
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

                if (needsAvatarChangeOptions)
                {
                    ReplaceCollectionIfChanged(
                        VrChatAvatarOptions,
                        Settings.VrChat.IsConnected
                            ? BuildVrChatAvatarOptionSet(selectedAvatarId, selectedAvatarName, "Selected target avatar")
                            : []);

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
        var normalizedIds = rule.AvatarRouletAvatarIds
            .Where(avatarId => !string.IsNullOrWhiteSpace(avatarId))
            .Select(avatarId => avatarId.Trim())
            .Distinct(StringComparer.Ordinal)
            .ToArray();
        var existingNames = rule.AvatarRouletAvatarNames
            .Select(avatarName => avatarName?.Trim() ?? string.Empty)
            .ToArray();
        var resolvedNames = normalizedIds
            .Select((avatarId, index) =>
            {
                var resolvedName = ResolveVrChatAvatarName(avatarId);
                if (!string.IsNullOrWhiteSpace(resolvedName))
                {
                    return resolvedName;
                }

                return index < existingNames.Length ? existingNames[index] : string.Empty;
            })
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
        QueueManagedRewardSync(0);
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
        QueueManagedRewardSync(0);
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
            Settings.GlobalMovementRules.Add(rule);
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
            Settings.GlobalMovementRules.Remove(SelectedRule);
            SelectedRule = Settings.GlobalMovementRules.FirstOrDefault();
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

        if (!IsViewingSupporterOverrides)
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
        if (!IsViewingSupporterOverrides)
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
            .Where(trigger => trigger.TriggerType == UniversalTriggerType.ChannelPointReward
                || !string.IsNullOrWhiteSpace(trigger.RewardId))
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
        if (lastSelectedMovementRuleId != Guid.Empty)
        {
            var rememberedRule = Settings.GlobalMovementRules.FirstOrDefault(rule => rule.Id == lastSelectedMovementRuleId);
            if (rememberedRule is not null)
            {
                return rememberedRule;
            }
        }

        return Settings.GlobalMovementRules.FirstOrDefault();
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
        appSettings.GlobalMovementRules.CollectionChanged += GlobalMovementRulesCollectionChanged;
        appSettings.GlobalOverrideRules.CollectionChanged += GlobalOverrideRulesCollectionChanged;
        appSettings.UniversalTriggers.CollectionChanged += UniversalTriggersCollectionChanged;

        foreach (var profile in appSettings.AvatarProfiles)
        {
            WireAvatarProfile(profile);
        }

        foreach (var rule in appSettings.GlobalMovementRules)
        {
            ApplyMovementRuleDefaults(rule);
            rule.PropertyChanged += RuleChanged;
        }

        foreach (var rule in appSettings.GlobalOverrideRules)
        {
            rule.PropertyChanged += RuleChanged;
        }

        foreach (var trigger in appSettings.UniversalTriggers)
        {
            WireUniversalTrigger(trigger);
        }
    }

    private void UnwireSettings(AppSettings appSettings)
    {
        appSettings.PropertyChanged -= SettingsChanged;
        appSettings.Broadcaster.PropertyChanged -= SettingsChanged;
        appSettings.Bot.PropertyChanged -= SettingsChanged;
        appSettings.VrChat.PropertyChanged -= SettingsChanged;
        appSettings.AvatarProfiles.CollectionChanged -= AvatarProfilesCollectionChanged;
        appSettings.GlobalMovementRules.CollectionChanged -= GlobalMovementRulesCollectionChanged;
        appSettings.GlobalOverrideRules.CollectionChanged -= GlobalOverrideRulesCollectionChanged;
        appSettings.UniversalTriggers.CollectionChanged -= UniversalTriggersCollectionChanged;

        foreach (var profile in appSettings.AvatarProfiles)
        {
            UnwireAvatarProfile(profile);
        }

        foreach (var rule in appSettings.GlobalMovementRules)
        {
            rule.PropertyChanged -= RuleChanged;
        }

        foreach (var rule in appSettings.GlobalOverrideRules)
        {
            rule.PropertyChanged -= RuleChanged;
        }

        foreach (var trigger in appSettings.UniversalTriggers)
        {
            UnwireUniversalTrigger(trigger);
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
        RefreshRuleCommandStates();
        QueueManagedRewardSync();
    }

    private void GlobalMovementRulesCollectionChanged(object? sender, NotifyCollectionChangedEventArgs e)
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
            foreach (TriggerRule rule in e.OldItems)
            {
                rule.PropertyChanged -= RuleChanged;
            }
        }

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

        if (ShouldSynchronizeManagedRewardsForAvatarProfileChange(e.PropertyName))
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

            if (owningAvatarProfile?.IsMasterProfile == true)
            {
                ApplyMasterAvatarDefaultsToRule(rule, owningAvatarProfile);
            }
            else if (owningAvatarProfile is not null)
            {
                ApplyAvatarProfileDefaultsToRule(rule);
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

            if (Settings.GlobalMovementRules.Contains(rule))
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
                    or nameof(TriggerRule.AvatarChangeTargetId)
                    or nameof(TriggerRule.AvatarChangeResetId)
                    or nameof(TriggerRule.ParameterType)
                    or nameof(TriggerRule.ParameterName))
            {
                RefreshVrChatAvatarSelectionOptions();
                RefreshAvailableActionTypes();
                RefreshAvatarParameterOptions();
                OpenAvatarRouletPoolPickerCommand.NotifyCanExecuteChanged();
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
            && (RuleBelongsToAvatarProfile(changedRule) || Settings.GlobalMovementRules.Contains(changedRule))
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
            QueueManagedRewardSync(0);
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
        if (!isInitialized)
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
        var configuration = BridgeRuntimeConfiguration.FromSettings(Settings, runtimeConfig);

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
            BridgeStatus = "Rule test failed.";
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
            BridgeStatus = "Universal trigger test failed.";
            AppendLog($"Could not test the selected universal trigger: {ex.Message}");
        }
        finally
        {
            bridgeRefreshGate.Release();
        }
    }

    private (bool IsGlobalOverride, AvatarTriggerProfile? Profile) ResolveRuleRuntimeContext(TriggerRule rule)
    {
        if (Settings.GlobalOverrideRules.Contains(rule) || Settings.GlobalMovementRules.Contains(rule))
        {
            return (true, null);
        }

        var profile = Settings.AvatarProfiles.FirstOrDefault(candidate => candidate.ChannelPointRules.Contains(rule));
        return (false, profile);
    }

    private async Task QueueRewardRefreshAsync()
    {
        await ReloadRuntimeConfigAsync();

        if (!Settings.Broadcaster.IsConnected)
        {
            RunOnUi(() => RewardOptions.Clear());
            return;
        }

        if (broadcasterManagedRewardsUnavailableForSession)
        {
            RunOnUi(() => RewardOptions.Clear());
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

            if (IsInvalidBroadcasterTokenFailure(ex))
            {
                RunOnUi(() => AppendThrottledLog(
                    "managed-rewards-token-refresh:catalog",
                    "Crystal Relay could not refresh Twitch redeems yet because the broadcaster login needs to refresh. The app will try again after the broadcaster session updates.",
                    ThrottledRewardSyncLogWindow));
                return;
            }

            RunOnUi(() => AppendLog($"Could not refresh channel point rewards: {ex.Message}"));
        }
    }

    // Debounced entry point for managed Twitch reward syncing.
    // Many editor changes land here, so older pending syncs get canceled in favor of the latest state.
    private void QueueManagedRewardSync(int delayMilliseconds = 1100)
    {
        if (!isInitialized)
        {
            return;
        }

        if (broadcasterManagedRewardsUnavailableForSession)
        {
            SetUniversalManagedRewardSyncStatus("Universal Twitch reward sync skipped because Twitch channel-point reward management is unavailable for this broadcaster session.");
            return;
        }

        managedRewardSyncCancellation?.Cancel();
        managedRewardSyncCancellation?.Dispose();
        managedRewardSyncCancellation = new CancellationTokenSource();
        var cancellationToken = managedRewardSyncCancellation.Token;

        _ = Task.Run(async () =>
        {
            try
            {
                if (delayMilliseconds > 0)
                {
                    await Task.Delay(delayMilliseconds, cancellationToken);
                }

                if (broadcasterManagedRewardsUnavailableForSession)
                {
                    return;
                }

                await managedRewardSyncGate.WaitAsync(cancellationToken);
                try
                {
                    var syncOutcome = await SynchronizeManagedChannelPointRewardsAsync(cancellationToken);
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
        }, CancellationToken.None);
    }

    private void SetUniversalManagedRewardSyncStatus(string status)
    {
        var normalizedStatus = status?.Trim() ?? string.Empty;
        if (string.Equals(universalManagedRewardSyncStatusText, normalizedStatus, StringComparison.Ordinal))
        {
            return;
        }

        universalManagedRewardSyncStatusText = normalizedStatus;
        RunOnUi(() => RaisePropertyChanged(nameof(UniversalManagedRewardStatusText)));
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

    private async Task RefreshCurrentAvatarStateForUniversalRewardSyncAsync(
        IReadOnlyCollection<UniversalTriggerRule> managedUniversalTriggers,
        CancellationToken cancellationToken)
    {
        if (managedUniversalTriggers.Count == 0 || !Settings.VrChat.IsConnected)
        {
            RunOnUi(() => RaisePropertyChanged(nameof(UniversalManagedRewardStatusText)));
            return;
        }

        try
        {
            await RefreshCurrentVrChatAvatarFromApiAsync(
                cancellationToken,
                queueManagedRewardSync: false,
                queueLocalStateRefresh: false);
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            RunOnUi(() => AppendThrottledLog(
                "universal-current-avatar-api-refresh",
                $"Crystal Relay could not refresh the current avatar from VRChat before Universal reward sync: {GetFriendlyVrChatError(ex)}",
                ThrottledRewardSyncLogWindow));
        }

        var currentAvatarId = Settings.VrChat.CurrentAvatarId?.Trim() ?? string.Empty;
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

        RunOnUi(() => RaisePropertyChanged(nameof(UniversalManagedRewardStatusText)));
    }

    private void QueueCurrentVrChatAvatarRefresh(int delayMilliseconds = 0)
    {
        if (!isInitialized
            || !Settings.VrChat.IsConnected)
        {
            return;
        }

        vrChatCurrentAvatarRefreshCancellation?.Cancel();
        vrChatCurrentAvatarRefreshCancellation?.Dispose();
        vrChatCurrentAvatarRefreshCancellation = new CancellationTokenSource();
        var cancellationToken = vrChatCurrentAvatarRefreshCancellation.Token;

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
        }, CancellationToken.None);
    }

    private void QueueCurrentVrChatLocalStateRefresh(int delayMilliseconds = 0)
    {
        if (!isInitialized || !Settings.VrChat.IsConnected)
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
            Settings.VrChat.Clear();
            ClearAvailableVrChatAvatars();
            await settingsStore.ClearVrChatAvatarCacheAsync(CancellationToken.None);
            await settingsStore.ClearVrChatOscParameterCacheAsync(CancellationToken.None);
            cachedVrChatParametersByAvatarId.Clear();
            AvatarParameterOptions.Clear();
            SelectedAvatarParameterOption = null;
            RaiseVrChatConnectionStateProperties();
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
        if (!isInitialized || !Settings.VrChat.IsConnected || string.IsNullOrWhiteSpace(Settings.VrChat.UserId))
        {
            return;
        }

        vrChatLocalOscScanCancellation?.Cancel();
        vrChatLocalOscScanCancellation?.Dispose();
        vrChatLocalOscScanCancellation = new CancellationTokenSource();
        var cancellationToken = vrChatLocalOscScanCancellation.Token;

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
        IReadOnlyCollection<UniversalTriggerRule> managedUniversalTriggers)
    {
        foreach (var rule in Settings.AvatarProfiles.SelectMany(profile => profile.ChannelPointRules))
        {
            yield return new ManagedRewardOwnershipEntry(rule.Id, rule.ChannelPointRewardId, rule.ChannelPointRewardTitle);
        }

        foreach (var rule in supportedMovementRules)
        {
            yield return new ManagedRewardOwnershipEntry(rule.Id, rule.ChannelPointRewardId, rule.ChannelPointRewardTitle);
        }

        foreach (var trigger in managedUniversalTriggers)
        {
            yield return new ManagedRewardOwnershipEntry(trigger.Id, trigger.RewardId, trigger.RewardTitle);
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
        var backgroundColor = cooldownRuleIds.Contains(rule.Id)
            ? ManagedRewardPresentation.NormalizeCooldownBackgroundColor(rule.ManagedRewardCooldownColor)
            : ManagedRewardPresentation.NormalizeReadyBackgroundColor(rule.ManagedRewardReadyColor);

        return new ManagedRewardSyncTarget(
            rule.Id,
            rule.DisplayTitle,
            rule.ChannelPointRewardId,
            rule.ChannelPointRewardTitle,
            rule.ChannelPointRewardCost,
            rule.CooldownSeconds,
            backgroundColor,
            desiredEnabled,
            rewardId => rule.ChannelPointRewardId = rewardId);
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
            trigger.RewardCost,
            cooldownSeconds: 0,
            ManagedRewardPresentation.NormalizeReadyBackgroundColor(trigger.ManagedRewardReadyColor),
            desiredEnabled,
            rewardId => trigger.RewardId = rewardId);
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
        bool? forcedManagedRewardActivation = null)
    {
        await ReloadRuntimeConfigAsync();

        var managedUniversalTriggers = Settings.UniversalTriggers
            .Where(IsManagedUniversalChannelPointTrigger)
            .ToArray();
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

        if (!BroadcasterCanManageRewards)
        {
            SetUniversalManagedRewardSyncStatus("Universal Twitch reward sync skipped because the broadcaster login is missing channel-point reward management permission. Reconnect the broadcaster account once.");
            return ManagedRewardSyncOutcome.Completed;
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
            rewardCatalog = new ManagedRewardSyncCatalog(await twitchApiClient.GetCustomRewardsAsync(
                Settings.Broadcaster.AccessToken,
                runtimeConfig.TwitchClientId,
                Settings.Broadcaster.UserId,
                cancellationToken));
            var temporarilyDisabledRuleIds = bridgeCoordinator.GetTemporarilyDisabledRuleIds();
            var cooldownRuleIds = bridgeCoordinator.GetRulesOnCooldownIds();
            var avatarChangeTransitionActive = bridgeCoordinator.IsAvatarChangeTransitionActive();
            var allowManagedRewardActivation = forcedManagedRewardActivation
                ?? ((IsBroadcasterLive || Settings.ChannelPointRewardTestModeEnabled)
                    && !Settings.EmergencyRedeemStopEnabled);
            var supportedMovementRules = Settings.GlobalMovementRules
                .Where(IsSupportedMovementRule)
                .ToArray();
            await RefreshCurrentAvatarStateForUniversalRewardSyncAsync(managedUniversalTriggers, cancellationToken);
            var currentAvatarId = GetManagedRewardActivationAvatarId();
            await EnsureCurrentAvatarParametersReadyForUniversalRewardSyncAsync(
                managedUniversalTriggers,
                currentAvatarId,
                cancellationToken);
            if (managedUniversalTriggers.Length > 0)
            {
                var unlinkedUniversalCount = managedUniversalTriggers.Count(trigger => string.IsNullOrWhiteSpace(trigger.RewardId));
                SetUniversalManagedRewardSyncStatus(unlinkedUniversalCount > 0
                    ? $"Universal Twitch reward sync is checking {managedUniversalTriggers.Length} reward(s); {unlinkedUniversalCount} unlinked reward(s) will be created or adopted if Twitch allows it. {GetUniversalRewardActivationReason(forcedManagedRewardActivation, allowManagedRewardActivation)}"
                    : $"Universal Twitch reward sync is checking {managedUniversalTriggers.Length} linked reward(s). {GetUniversalRewardActivationReason(forcedManagedRewardActivation, allowManagedRewardActivation)}");
            }

            // Build once-per-sync ownership indexes up front. The reconciliation pass uses
            // them to adopt, recycle, and retire rewards without repeatedly rescanning the
            // full rule list and reward list inside each per-rule operation.
            var ownershipEntries = EnumerateManagedRewardOwnershipEntries(supportedMovementRules, managedUniversalTriggers)
                .ToArray();
            var claimedRewardIds = ownershipEntries
                .Select(entry => entry.RewardId?.Trim())
                .Where(rewardId => !string.IsNullOrWhiteSpace(rewardId))
                .Cast<string>()
                .ToHashSet(StringComparer.Ordinal);
            var desiredManagedRewardTitles = ownershipEntries
                .Select(entry => ManagedRewardPresentation.BuildTitle(entry.RewardTitle))
                .Where(title => !string.IsNullOrWhiteSpace(title))
                .ToHashSet(StringComparer.OrdinalIgnoreCase);
            var ownershipIndex = new ManagedRewardRuleOwnershipIndex(ownershipEntries);

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

            changesMade |= await CleanupStaleManagedRewardsAsync(
                rewardCatalog,
                claimedRewardIds,
                desiredManagedRewardTitles,
                cancellationToken);

            foreach (var profile in Settings.AvatarProfiles)
            {
                foreach (var rule in profile.ChannelPointRules)
                {
                    var target = CreateManagedRewardTargetForRule(
                        profile,
                        rule,
                        currentAvatarId,
                        avatarChangeTransitionActive,
                        allowManagedRewardActivation,
                        temporarilyDisabledRuleIds,
                        cooldownRuleIds);
                    changesMade |= await SynchronizeManagedRewardForTargetAsync(
                        target,
                        rewardCatalog,
                        claimedRewardIds,
                        desiredManagedRewardTitles,
                        ownershipIndex,
                        cancellationToken);
                }
            }

            foreach (var rule in supportedMovementRules)
            {
                var target = CreateManagedRewardTargetForRule(
                    profile: null,
                    rule,
                    currentAvatarId,
                    avatarChangeTransitionActive,
                    allowManagedRewardActivation,
                    temporarilyDisabledRuleIds,
                    cooldownRuleIds);
                changesMade |= await SynchronizeManagedRewardForTargetAsync(
                    target,
                    rewardCatalog,
                    claimedRewardIds,
                    desiredManagedRewardTitles,
                    ownershipIndex,
                    cancellationToken);
            }

            var universalTargets = managedUniversalTriggers
                .Select(trigger => CreateManagedRewardTargetForUniversalTrigger(
                    trigger,
                    allowManagedRewardActivation,
                    currentAvatarId))
                .ToArray();

            foreach (var target in universalTargets)
            {
                changesMade |= await SynchronizeManagedRewardForTargetAsync(
                    target,
                    rewardCatalog,
                    claimedRewardIds,
                    desiredManagedRewardTitles,
                    ownershipIndex,
                    cancellationToken);
            }

            RunOnUi(() => ApplyRewardCatalog(rewardCatalog.Rewards));
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
            || !BroadcasterCanManageRewards)
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
                forcedManagedRewardActivation: false);
            if (syncOutcome == ManagedRewardSyncOutcome.BroadcasterCustomRewardsUnavailable)
            {
                AppendThrottledLog(unsupportedLogKey, unsupportedMessage, ThrottledRewardSyncLogWindow);
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
        ManagedRewardSyncCatalog rewardCatalog,
        HashSet<string> claimedRewardIds,
        IReadOnlyCollection<string> desiredManagedRewardTitles,
        ManagedRewardRuleOwnershipIndex ownershipIndex,
        CancellationToken cancellationToken)
    {
        var rewardTitle = ManagedRewardPresentation.StripPrefix(target.RewardTitle);
        var managedRewardTitle = ManagedRewardPresentation.BuildTitle(rewardTitle);
        var rewardCost = Math.Max(1, target.RewardCost);
        var rewardCooldownSeconds = Math.Max(0, target.CooldownSeconds);
        var rewardBackgroundColor = target.BackgroundColor;
        var desiredEnabled = target.DesiredEnabled;
        var rewardId = target.RewardId?.Trim() ?? string.Empty;
        var existingReward = !string.IsNullOrWhiteSpace(rewardId) && rewardCatalog.TryGetById(rewardId, out var matchedReward)
            ? matchedReward
            : null;
        var changed = false;

        if (existingReward is null && !string.IsNullOrWhiteSpace(rewardTitle))
        {
            var adoptedReward = await FindAdoptableExistingManagedRewardAsync(
                target.Id,
                rewardTitle,
                rewardCatalog,
                ownershipIndex,
                refreshCatalogIfNeeded: false,
                cancellationToken);

            if (adoptedReward is not null)
            {
                changed |= SetManagedRewardTargetId(target, adoptedReward.Id, ownershipIndex);
                claimedRewardIds.Add(adoptedReward.Id);
                existingReward = adoptedReward;
            }
        }

        if (existingReward is null && string.IsNullOrWhiteSpace(rewardTitle))
        {
            if (!string.IsNullOrWhiteSpace(target.RewardId))
            {
                changed |= SetManagedRewardTargetId(target, string.Empty, ownershipIndex);
                changed = true;
            }

            return changed;
        }

        if (existingReward is not null && string.IsNullOrWhiteSpace(rewardTitle))
        {
            if (existingReward.IsEnabled)
            {
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
                        cancellationToken);
                rewardCatalog.Replace(disabledReward);
                changed = true;
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
                    refreshCatalogIfNeeded: true,
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

                var recyclableReward = rewardCatalog.FindFirstRecyclableReward(claimedRewardIds, desiredManagedRewardTitles);

                if (recyclableReward is not null)
                {
                    var recycledReward = await twitchApiClient.UpdateCustomRewardAsync(
                        Settings.Broadcaster.AccessToken,
                        runtimeConfig.TwitchClientId,
                        Settings.Broadcaster.UserId,
                        recyclableReward.Id,
                        managedRewardTitle,
                        rewardCost,
                        desiredEnabled,
                        rewardCooldownSeconds,
                        rewardBackgroundColor,
                        cancellationToken);
                    changed |= SetManagedRewardTargetId(target, recycledReward.Id, ownershipIndex);
                    rewardCatalog.Replace(recycledReward);
                    claimedRewardIds.Add(recycledReward.Id);
                    ClearManagedRewardCreateBackoff(managedRewardTitle);
                    RunOnUi(() => AppendLog($"Reused a saved Twitch reward for '{target.DisplayTitle}'."));                    
                    return true;
                }

                var createdReward = await twitchApiClient.CreateCustomRewardAsync(
                    Settings.Broadcaster.AccessToken,
                    runtimeConfig.TwitchClientId,
                    Settings.Broadcaster.UserId,
                    managedRewardTitle,
                    rewardCost,
                    desiredEnabled,
                    rewardCooldownSeconds,
                    rewardBackgroundColor,
                    cancellationToken);
                changed |= SetManagedRewardTargetId(target, createdReward.Id, ownershipIndex);
                rewardCatalog.Replace(createdReward);
                claimedRewardIds.Add(createdReward.Id);
                ClearManagedRewardCreateBackoff(managedRewardTitle);
                RunOnUi(() => AppendLog($"Created Twitch reward '{managedRewardTitle}' for '{target.DisplayTitle}'."));                
                return true;
            }

            claimedRewardIds.Add(existingReward.Id);
            ClearManagedRewardCreateBackoff(managedRewardTitle);
            if (!string.Equals(existingReward.Title, managedRewardTitle, StringComparison.Ordinal)
                || existingReward.Cost != rewardCost
                || existingReward.IsEnabled != desiredEnabled
                || existingReward.IsGlobalCooldownEnabled != (rewardCooldownSeconds > 0)
                || (existingReward.IsGlobalCooldownEnabled && (existingReward.GlobalCooldownSeconds ?? 0) != rewardCooldownSeconds)
                || !string.Equals(existingReward.BackgroundColor, rewardBackgroundColor, StringComparison.OrdinalIgnoreCase))
            {
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
                    cancellationToken);
                rewardCatalog.Replace(updatedReward);
                changed = true;
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

            var duplicateRecovery = await TryRecoverDuplicateManagedRewardAsync(
                ex,
                target,
                rewardTitle,
                managedRewardTitle,
                rewardCatalog,
                claimedRewardIds,
                ownershipIndex,
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

    private static bool HasRuntimeReadyAction(TriggerRule rule) => rule.ActionType switch
    {
        OscActionType.AvatarParameter => !string.IsNullOrWhiteSpace(rule.ParameterName),
        OscActionType.AvatarChange => !string.IsNullOrWhiteSpace(rule.AvatarChangeTargetId),
        OscActionType.AvatarRoulet => rule.AvatarRouletAvatarIds.Any(avatarId => !string.IsNullOrWhiteSpace(avatarId)),
        OscActionType.PlayerMovement => IsSupportedMovementRule(rule),
        _ => false
    };

    private static bool IsManagedUniversalChannelPointTrigger(UniversalTriggerRule trigger) =>
        trigger.TriggerType == UniversalTriggerType.ChannelPointReward;

    private static bool HasUniversalTriggerAvatarParameterGate(UniversalTriggerRule trigger) =>
        GetUniversalTriggerRequiredAvatarParameterAddresses(trigger).Count > 0;

    private static bool HasRuntimeReadyUniversalTriggerAction(UniversalTriggerRule trigger) =>
        trigger.Actions.Any(IsRuntimeReadyUniversalTriggerAction);

    private static bool IsRuntimeReadyUniversalTriggerAction(UniversalTriggerAction action) =>
        !string.IsNullOrWhiteSpace(action.OscAddress)
        && !string.IsNullOrWhiteSpace(action.TargetValue)
        && (action.DurationSeconds <= 0 || !string.IsNullOrWhiteSpace(action.DefaultValue));

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

    private async Task<bool> CleanupStaleManagedRewardsAsync(
        ManagedRewardSyncCatalog rewardCatalog,
        IReadOnlyCollection<string> claimedRewardIds,
        IReadOnlyCollection<string> desiredManagedRewardTitles,
        CancellationToken cancellationToken)
    {
        var staleRewards = rewardCatalog.GetStaleRewards(claimedRewardIds, desiredManagedRewardTitles);

        var changed = false;
        foreach (var staleReward in staleRewards)
        {
            try
            {
                await twitchApiClient.DeleteCustomRewardAsync(
                    Settings.Broadcaster.AccessToken,
                    runtimeConfig.TwitchClientId,
                    Settings.Broadcaster.UserId,
                    staleReward.Id,
                    cancellationToken);
                rewardCatalog.Remove(staleReward.Id);
                changed = true;
            }
            catch (TwitchApiException ex) when (ex.StatusCode == System.Net.HttpStatusCode.NotFound)
            {
                rewardCatalog.Remove(staleReward.Id);
                changed = true;
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
        if (ex is not TwitchApiException apiException
            || !apiException.ApiMessage.Contains("CREATE_CUSTOM_REWARD_TOO_MANY_REWARDS", StringComparison.OrdinalIgnoreCase))
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
            RewardOptions.Clear();
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
        return Settings.VrChat.CurrentAvatarId?.Trim() ?? string.Empty;
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

        RewardOptions.Clear();
        RewardOptions.Add(new TwitchRewardOption(string.Empty, "Any reward", 0));
        foreach (var reward in orderedRewards)
        {
            RewardOptions.Add(new TwitchRewardOption(
                reward.Id,
                ManagedRewardPresentation.StripPrefix(reward.Title),
                reward.Cost));
        }

        foreach (var rule in EnumerateAllRules().Where(rule => !string.IsNullOrWhiteSpace(rule.ChannelPointRewardId)))
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

        foreach (var trigger in Settings.UniversalTriggers.Where(trigger =>
                     trigger.TriggerType == UniversalTriggerType.ChannelPointReward
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
    }

    // About refresh prefers normal authenticated Twitch API calls when possible.
    // If the app does not have a usable token, it falls back to the supplemental relay path.
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
    // fresh installs by pulling fallback live/profile data before trying image-only fallbacks.
    private async Task RefreshAboutProfilesWithoutAuthAsync(IReadOnlyList<AboutTwitchProfile> profiles)
    {
        try
        {
            var supplementalProfiles = await twitchApiClient.GetSupplementalAboutProfilesAsync();
            RunOnUi(() => ApplySupplementalAboutProfiles(profiles, supplementalProfiles));
        }
        catch
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

    private static bool ShouldSynchronizeManagedRewardsForUniversalTriggerChange(string? propertyName) =>
        string.IsNullOrWhiteSpace(propertyName) || UniversalTriggerPropertiesRequiringManagedRewardSync.Contains(propertyName);

    private static bool ShouldSynchronizeManagedRewardsForUniversalTriggerActionChange(string? propertyName) =>
        string.IsNullOrWhiteSpace(propertyName) || UniversalTriggerActionPropertiesRequiringManagedRewardSync.Contains(propertyName);

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
        BroadcasterStatus = Settings.Broadcaster.IsConnected
            ? $"Connected as {Settings.Broadcaster.DisplayName}."
            : "Broadcaster account not connected.";

        BotStatus = Settings.Bot.IsConnected
            ? $"Connected as {Settings.Bot.DisplayName}. Bot announcements are ready."
            : "Bot account not connected. This is optional.";

        BroadcasterExpiryStatus = BuildExpiryStatus(Settings.Broadcaster);
        BotExpiryStatus = BuildExpiryStatus(Settings.Bot);
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
                SetStreamingStatusCard(
                    "You are live.",
                    "Twitch listener is connected and streaming status is updating normally.",
                    "Live",
                    "Live",
                    "Live",
                    "Connected",
                    "Healthy");
                return;
            }

            SetStreamingStatusCard(
                "You are offline.",
                "Twitch listener is connected and waiting for you to go live.",
                "Healthy",
                "Offline",
                "Healthy",
                "Connected",
                "Healthy");
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

        return bridgeStatus.Contains("needs attention", StringComparison.OrdinalIgnoreCase)
            || bridgeStatus.Contains("could not", StringComparison.OrdinalIgnoreCase)
            || bridgeStatus.Contains("failed", StringComparison.OrdinalIgnoreCase)
            || bridgeStatus.Contains("error", StringComparison.OrdinalIgnoreCase)
            || bridgeStatus.Contains("issue", StringComparison.OrdinalIgnoreCase)
            || bridgeStatus.Contains("expired", StringComparison.OrdinalIgnoreCase)
            || bridgeStatus.Contains("disconnected", StringComparison.OrdinalIgnoreCase)
            || broadcasterStatus.Contains("reconnect", StringComparison.OrdinalIgnoreCase)
            || broadcasterStatus.Contains("expired", StringComparison.OrdinalIgnoreCase);
    }

    private sealed record ManagedRewardOwnershipEntry(
        Guid Id,
        string RewardId,
        string RewardTitle);

    private sealed class ManagedRewardSyncTarget
    {
        public ManagedRewardSyncTarget(
            Guid id,
            string displayTitle,
            string rewardId,
            string rewardTitle,
            int rewardCost,
            int cooldownSeconds,
            string backgroundColor,
            bool desiredEnabled,
            Action<string> applyRewardId)
        {
            Id = id;
            DisplayTitle = displayTitle;
            RewardId = rewardId?.Trim() ?? string.Empty;
            RewardTitle = rewardTitle?.Trim() ?? string.Empty;
            RewardCost = Math.Max(1, rewardCost);
            CooldownSeconds = Math.Max(0, cooldownSeconds);
            BackgroundColor = backgroundColor;
            DesiredEnabled = desiredEnabled;
            ApplyRewardId = applyRewardId;
        }

        public Guid Id { get; }

        public string DisplayTitle { get; }

        public string RewardId { get; set; }

        public string RewardTitle { get; }

        public int RewardCost { get; }

        public int CooldownSeconds { get; }

        public string BackgroundColor { get; }

        public bool DesiredEnabled { get; }

        public Action<string> ApplyRewardId { get; }
    }

    private sealed class ManagedRewardSyncCatalog
    {
        private readonly List<TwitchApiClient.CustomRewardResponse> rewards = [];
        private readonly Dictionary<string, TwitchApiClient.CustomRewardResponse> rewardsById = new(StringComparer.Ordinal);
        private readonly Dictionary<string, List<TwitchApiClient.CustomRewardResponse>> rewardsByTitle = new(StringComparer.Ordinal);

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
            IReadOnlyCollection<string> desiredManagedRewardTitles)
        {
            foreach (var reward in rewards)
            {
                if (!claimedRewardIds.Contains(reward.Id)
                    && !reward.IsEnabled
                    && ManagedRewardPresentation.IsManagedTitle(reward.Title)
                    && !desiredManagedRewardTitles.Contains(reward.Title))
                {
                    return reward;
                }
            }

            return null;
        }

        public IReadOnlyList<TwitchApiClient.CustomRewardResponse> GetStaleRewards(
            IReadOnlyCollection<string> claimedRewardIds,
            IReadOnlyCollection<string> desiredManagedRewardTitles)
        {
            var staleRewards = new List<TwitchApiClient.CustomRewardResponse>();
            foreach (var reward in rewards)
            {
                if (!claimedRewardIds.Contains(reward.Id)
                    && !reward.IsEnabled
                    && ManagedRewardPresentation.IsManagedTitle(reward.Title)
                    && !desiredManagedRewardTitles.Contains(reward.Title))
                {
                    staleRewards.Add(reward);
                }
            }

            return staleRewards;
        }

        public IEnumerable<TwitchApiClient.CustomRewardResponse> FindByTitleVariants(string rewardTitle)
        {
            var seenRewardIds = new HashSet<string>(StringComparer.Ordinal);
            var baseTitle = ManagedRewardPresentation.StripPrefix(rewardTitle);
            foreach (var titleKey in EnumerateTitleKeys(baseTitle))
            {
                if (!rewardsByTitle.TryGetValue(titleKey, out var matches))
                {
                    continue;
                }

                foreach (var reward in matches)
                {
                    if (seenRewardIds.Add(reward.Id))
                    {
                        yield return reward;
                    }
                }
            }
        }

        private static IEnumerable<string> EnumerateTitleKeys(string rewardTitle)
        {
            if (string.IsNullOrWhiteSpace(rewardTitle))
            {
                yield break;
            }

            var strippedTitle = ManagedRewardPresentation.StripPrefix(rewardTitle);
            if (string.IsNullOrWhiteSpace(strippedTitle))
            {
                yield break;
            }

            yield return strippedTitle;

            var managedTitle = ManagedRewardPresentation.BuildTitle(strippedTitle);
            if (!string.Equals(managedTitle, strippedTitle, StringComparison.Ordinal))
            {
                yield return managedTitle;
            }
        }

        private void AddTitleLookup(TwitchApiClient.CustomRewardResponse reward)
        {
            var title = reward.Title?.Trim() ?? string.Empty;
            if (string.IsNullOrWhiteSpace(title))
            {
                return;
            }

            if (!rewardsByTitle.TryGetValue(title, out var matches))
            {
                matches = [];
                rewardsByTitle[title] = matches;
            }

            matches.Add(reward);
        }

        private void RemoveTitleLookup(TwitchApiClient.CustomRewardResponse reward)
        {
            var title = reward.Title?.Trim() ?? string.Empty;
            if (string.IsNullOrWhiteSpace(title)
                || !rewardsByTitle.TryGetValue(title, out var matches))
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
                rewardsByTitle.Remove(title);
            }
        }
    }

    private sealed class ManagedRewardRuleOwnershipIndex
    {
        private readonly Dictionary<string, HashSet<Guid>> ruleIdsByRewardId = new(StringComparer.Ordinal);
        private readonly Dictionary<string, HashSet<Guid>> ruleIdsByRewardTitle = new(StringComparer.Ordinal);

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

            var rewardTitle = reward.Title?.Trim() ?? string.Empty;
            return !string.IsNullOrWhiteSpace(rewardTitle)
                && TryHasOtherOwner(ruleIdsByRewardTitle, rewardTitle, currentOwnerId);
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

        private void RegisterRule(ManagedRewardOwnershipEntry entry)
        {
            var rewardId = entry.RewardId?.Trim() ?? string.Empty;
            if (!string.IsNullOrWhiteSpace(rewardId))
            {
                AddOwner(ruleIdsByRewardId, rewardId, entry.Id);
            }

            var strippedTitle = ManagedRewardPresentation.StripPrefix(entry.RewardTitle);
            if (string.IsNullOrWhiteSpace(strippedTitle))
            {
                return;
            }

            AddOwner(ruleIdsByRewardTitle, strippedTitle, entry.Id);
            var managedTitle = ManagedRewardPresentation.BuildTitle(strippedTitle);
            if (!string.Equals(managedTitle, strippedTitle, StringComparison.Ordinal))
            {
                AddOwner(ruleIdsByRewardTitle, managedTitle, entry.Id);
            }
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

        return bridgeStatus.Contains("connecting", StringComparison.OrdinalIgnoreCase)
            || bridgeStatus.Contains("reconnecting", StringComparison.OrdinalIgnoreCase)
            || bridgeStatus.Contains("starting", StringComparison.OrdinalIgnoreCase)
            || bridgeStatus.Contains("refresh", StringComparison.OrdinalIgnoreCase)
            || bridgeStatus.Contains("retrying", StringComparison.OrdinalIgnoreCase)
            || bridgeStatus.Contains("checking", StringComparison.OrdinalIgnoreCase);
    }

    private string BuildStreamingErrorDetail(string bridgeStatus, string broadcasterStatus)
    {
        if (bridgeStatus.Contains("expired", StringComparison.OrdinalIgnoreCase)
            || broadcasterStatus.Contains("reconnect", StringComparison.OrdinalIgnoreCase)
            || broadcasterStatus.Contains("expired", StringComparison.OrdinalIgnoreCase))
        {
            return T("Reconnect Twitch to restore the background listener.");
        }

        if (bridgeStatus.Contains("issue", StringComparison.OrdinalIgnoreCase)
            || bridgeStatus.Contains("retry", StringComparison.OrdinalIgnoreCase)
            || bridgeStatus.Contains("disconnected", StringComparison.OrdinalIgnoreCase))
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
            ShouldPlayViewerChatSound(message),
            message.ReceivedAt,
            SelectedTheme,
            Settings.ChatTimestampFormat));
    }

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

    private void HandleBroadcasterLiveStateChanged(bool isLive)
    {
        IsBroadcasterLive = isLive;
        if (isLive && Settings.ChannelPointRewardTestModeEnabled)
        {
            Settings.ChannelPointRewardTestModeEnabled = false;
            AppendLog("Streaming test mode turned off automatically because the broadcaster is live.");
        }

        QueueManagedRewardSync(0);
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
            .Concat(Settings.GlobalMovementRules)
            .Concat(Settings.GlobalOverrideRules);
    }

    private ObservableCollection<TriggerRule> GetCurrentEditableRuleCollection()
    {
        if (IsViewingUniversalTriggers)
        {
            return new ObservableCollection<TriggerRule>();
        }

        if (IsViewingSupporterOverrides)
        {
            return Settings.GlobalOverrideRules;
        }

        if (IsViewingMovementRedeems)
        {
            return Settings.GlobalMovementRules;
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

    private static string GetSpecialRuleLockoutDisplayLabel(TriggerRule rule)
    {
        return rule.RewardDisplayTitle;
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
        var unsupportedRules = Settings.GlobalMovementRules
            .Where(rule => !IsSupportedMovementDirection(rule.MovementDirection))
            .ToArray();
        if (unsupportedRules.Length == 0)
        {
            return;
        }

        foreach (var rule in unsupportedRules)
        {
            ForgetRememberedRules([rule]);
            Settings.GlobalMovementRules.Remove(rule);
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
        or PlayerMovementDirection.SpinLeft
        or PlayerMovementDirection.SpinRight;

    private static bool IsSupportedMovementRule(TriggerRule rule) =>
        rule.ActionType != OscActionType.PlayerMovement || IsSupportedMovementDirection(rule.MovementDirection);

    private void RaiseRuleSelectionStateProperties()
    {
        RaisePropertyChanged(nameof(IsViewingAvatarTriggers));
        RaisePropertyChanged(nameof(IsViewingMasterAvatar));
        RaisePropertyChanged(nameof(IsViewingMovementRedeems));
        RaisePropertyChanged(nameof(IsViewingSupporterOverrides));
        RaisePropertyChanged(nameof(IsViewingUniversalTriggers));
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
        RaiseUniversalTriggerGroupProperties();
        RaisePropertyChanged(nameof(IsSpecialRuleLockoutEditorVisible));
        RaisePropertyChanged(nameof(SpecialRuleLockoutHelpText));
        RaisePropertyChanged(nameof(SpecialRuleLockoutSummaryText));
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
        var currentAvatarId = Settings.VrChat.CurrentAvatarId?.Trim() ?? string.Empty;
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
            if (SelectedRule?.ActionType != OscActionType.AvatarParameter)
            {
                SelectedAvatarParameterOption = null;
                RaisePropertyChanged(nameof(UniversalManagedRewardStatusText));
                return;
            }

            isRestoringAvatarParameterSelection = true;
            try
            {
                var selectedAvatarId = GetSelectedParameterCacheAvatarId();
                var currentType = SelectedRule?.ParameterType ?? OscParameterType.Int;
                var cachedParameters = !string.IsNullOrWhiteSpace(selectedAvatarId)
                    && cachedVrChatParametersByAvatarId.TryGetValue(selectedAvatarId, out var loadedParameters)
                    ? loadedParameters
                    : BuildFallbackAvatarParameterOptions();

                var nextOptions = cachedParameters
                    .Where(parameter => parameter.ParameterType == currentType)
                    .OrderBy(parameter => parameter.Name, StringComparer.OrdinalIgnoreCase)
                    .ToList();

                if (SelectedRule is not null
                    && !string.IsNullOrWhiteSpace(SelectedRule.ParameterName)
                    && !nextOptions.Any(option => string.Equals(option.Address, SelectedRule.ParameterName, StringComparison.Ordinal)))
                {
                    nextOptions.Insert(0, CreateCustomAvatarParameterOption(SelectedRule.ParameterName, currentType));
                }

                ReplaceCollectionIfChanged(AvatarParameterOptions, nextOptions);

                SelectedAvatarParameterOption = SelectedRule is null
                    ? null
                    : AvatarParameterOptions.FirstOrDefault(option => string.Equals(option.Address, SelectedRule.ParameterName, StringComparison.Ordinal));

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
            finally
            {
                isRestoringAvatarParameterSelection = false;
                RaisePropertyChanged(nameof(UniversalManagedRewardStatusText));
            }
        });
    }

    private string GetSelectedParameterCacheAvatarId()
    {
        if (IsViewingSupporterOverrides || IsViewingUniversalTriggers)
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
            QueueManagedRewardSync(0);
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
                QueueManagedRewardSync(0);
            }
        }
    }

    private void QueueCurrentVrChatOscParameterRefresh(string avatarId, bool queueManagedRewardSync = true)
    {
        if (!isInitialized || !Settings.VrChat.IsConnected)
        {
            return;
        }

        var normalizedAvatarId = avatarId?.Trim() ?? string.Empty;
        if (string.IsNullOrWhiteSpace(normalizedAvatarId))
        {
            return;
        }

        vrChatOscParameterRefreshCancellation?.Cancel();
        vrChatOscParameterRefreshCancellation?.Dispose();
        vrChatOscParameterRefreshCancellation = new CancellationTokenSource();
        var cancellationToken = vrChatOscParameterRefreshCancellation.Token;

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
    }

    private static void ApplyAvatarProfileDefaultsToRule(TriggerRule rule)
    {
        if (rule.TriggerType != TwitchTriggerType.ChannelPoints)
        {
            rule.TriggerType = TwitchTriggerType.ChannelPoints;
        }

        if (rule.ActionType != OscActionType.AvatarParameter)
        {
            rule.ActionType = OscActionType.AvatarParameter;
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

        if (rule.CooldownSeconds != 0)
        {
            rule.CooldownSeconds = 0;
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
                if (!Settings.GlobalMovementRules.Contains(rule))
                {
                    Settings.GlobalMovementRules.Add(rule);
                }

                movedRuleNames.Add(rule.DisplayTitle);
            }
        }

        var misplacedOverrideRules = Settings.GlobalOverrideRules
            .Where(rule => rule.ActionType == OscActionType.PlayerMovement)
            .ToArray();

        foreach (var rule in misplacedOverrideRules)
        {
            Settings.GlobalOverrideRules.Remove(rule);
            if (!Settings.GlobalMovementRules.Contains(rule))
            {
                Settings.GlobalMovementRules.Add(rule);
            }

            movedRuleNames.Add(rule.DisplayTitle);
        }

        if (movedRuleNames.Count == 0)
        {
            return;
        }

        if (SelectedRule is not null && Settings.GlobalMovementRules.Contains(SelectedRule))
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
        if (Settings.GlobalMovementRules.Contains(rule))
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

        if (!Settings.GlobalMovementRules.Contains(rule))
        {
            Settings.GlobalMovementRules.Add(rule);
        }

        ApplyMovementRuleDefaults(rule);
        SwitchRuleView(RuleListView.MovementRedeems, profile: null, rule);
        AppendLog(TF("Moved '{0}' into Movement Redeems because player movement is global.", rule.DisplayTitle));
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

    private static string GetTestBuildTitleSuffix() => IsTestBuild ? LocalizationService.Translate(" - Test Build") : string.Empty;

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
        UniversalTriggers
    }
}

public sealed record TwitchRewardOption(string Id, string Title, int Cost)
{
    public string DisplayLabel => Cost > 0 ? $"{Title} ({Cost} pts)" : Title;
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
        ChatTimestampFormat timestampFormat)
    {
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
        this.timestampFormat = NormalizeTimestampFormat(timestampFormat);
    }

    public string UserDisplayName { get; }

    public string MessageText { get; }

    public IReadOnlyList<string> BadgeImageUrls { get; }

    public IReadOnlyList<TwitchChatInlineFragment> InlineFragments { get; }

    public bool ShouldPlayViewerSound { get; }

    public DateTimeOffset ReceivedAt { get; }

    public string RawUserColor { get; }

    public string TimestampText => FormatTimestamp(ReceivedAt, timestampFormat);

    public Brush NameBrush { get; }

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

public sealed record ActionTypeOption(OscActionType Value, string Label);

public sealed record ThemeOption(AppTheme Value, string Label);

public sealed record AppLanguageOption(AppLanguage Value, string Label)
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

public sealed record TriggerRuleReferenceOption(Guid RuleId, string Label);
