using System.Collections.ObjectModel;
using VrcTwitchOscBridge.Infrastructure;

namespace VrcTwitchOscBridge.Models;

public enum ChatTimestampFormat
{
    TwelveHour,
    TwentyFourHour
}

public sealed class AppSettings : ObservableObject
{
    private TwitchAccountSettings broadcaster = new();
    private TwitchAccountSettings bot = new();
    private VrChatAccountSettings vrChat = new();
    private CustomThemeSettings customTheme = new();
    private ObservableCollection<AvatarTriggerProfile> avatarProfiles = [];
    private ObservableCollection<MovementRedeemSet> movementRedeemSets = [];
    private ObservableCollection<TriggerRule> globalMovementRules = [];
    private ObservableCollection<TriggerRule> globalOverrideRules = [];
    private ObservableCollection<UniversalTriggerRule> universalTriggers = [];
    private ObservableCollection<AvatarScaleSet> avatarScaleSets = [];
    private ObservableCollection<AvatarScaleRule> avatarScaleRules = [];
    private AvatarScaleMasterRewardSettings avatarScaleMasterReward = new();
    private ObservableCollection<PowerUpRule> powerUpRules = [];
    private RewardFireSaleSettings rewardFireSale = new();
    private CashPaymentConnectionSettings cashPayments = new();
    private ObservableCollection<CashPaymentRule> cashPaymentRules = [];
    private ObservableCollection<TriggerRule> rules = [];
    private ObservableCollection<AvatarSwapProfile> avatarSwapProfiles = [];
    private string? masterAvatarSwapReturnId;
    private string? masterAvatarSwapReturnName;
    private int avatarChangeToAvatarSwapMigrationVersion;

    private int interfaceOpacityPercent = 88;
    private int chatTextSize = 20;
    private int chatOpacityPercent = 90;
    private bool chatShowTimestamps = true;
    private ChatTimestampFormat chatTimestampFormat = ChatTimestampFormat.TwentyFourHour;
    private string chatFontFamily = "Verdana";
    private bool chatboxAlwaysOnTop = true;
    private bool chatboxSettingsPanelOpen;
    private bool chatboxOverlayMode;
    private bool chatboxXsOverlayCompatibilityMode;
    private bool chatboxOscEnabled;
    private int chatboxOscDelaySeconds = 3;
    private bool chatboxViewerSoundEnabled;
    private bool useBroadcasterAsBotSender;
    private bool supporterOverrideInfoMessageEnabled;
    private bool triggerInfoAnnouncementsEnabled;
    private int triggerInfoAnnouncementIntervalMinutes = 10;
    private bool triggerInfoCommandEnabled = true;
    private string triggerInfoCommandText = "!rewards";
    private int triggerInfoCommandCooldownSeconds = 300;
    private ChatCommandPermission triggerInfoCommandPermission = ChatCommandPermission.Everyone;
    private bool useManagedRewardTitlePrefix = true;
    private bool worldCommandEnabled = true;
    private string worldCommandText = "!world";
    private int worldCommandCooldownSeconds = 30;
    private ChatCommandPermission worldCommandPermission = ChatCommandPermission.Everyone;
    private WorldCommandBlacklistSettings worldCommandBlacklist = new();
    private bool channelPointRewardTestModeEnabled;
    private bool avatarChangeCooldownOnlyModeEnabled;
    private bool emergencyRedeemStopEnabled;
    private bool desktopModeInputLockEnabled;
    private bool restartVrChatInDesktopMode;
    private bool liveFeedbackHeartbeatEnabled = true;
    private bool betaApplicationUpdatesEnabled;
    private bool easterEggsEnabled = true;
    private bool mainWindowTrayTipShown;
    private string ignoredUpdateVersion = string.Empty;
    private string ignoredBetaUpdateBaseVersion = string.Empty;
    private AppLanguage language = AppLanguage.SystemDefault;
    private AppTheme theme = AppTheme.VoidCrystal;
    private bool pauseCommandEnabled;
    private string pauseCommandText = "!pause";
    private bool redeemGroupCommandEnabled;
    private bool redeemControlCommandEnabled;
    private ObservableCollection<RedeemGroup> redeemGroups = [];

    public AppSettings()
    {
        WireCustomTheme(customTheme);
    }

    public TwitchAccountSettings Broadcaster
    {
        get => broadcaster;
        set => SetProperty(ref broadcaster, value);
    }

    public TwitchAccountSettings Bot
    {
        get => bot;
        set => SetProperty(ref bot, value);
    }

    public VrChatAccountSettings VrChat
    {
        get => vrChat;
        set => SetProperty(ref vrChat, value);
    }

    public CustomThemeSettings CustomTheme
    {
        get => customTheme;
        set
        {
            var nextValue = value ?? new CustomThemeSettings();
            if (ReferenceEquals(customTheme, nextValue))
            {
                return;
            }

            UnwireCustomTheme(customTheme);
            customTheme = nextValue;
            WireCustomTheme(customTheme);
            RaisePropertyChanged();
        }
    }

    private AvatarLibrary avatarLibrary = new();

    public AvatarLibrary AvatarLibrary
    {
        get => avatarLibrary;
        set => SetProperty(ref avatarLibrary, value ?? new AvatarLibrary());
    }

    public ObservableCollection<AvatarTriggerProfile> AvatarProfiles
    {
        get => avatarProfiles;
        set => SetProperty(ref avatarProfiles, value ?? []);
    }

    public ObservableCollection<TriggerRule> GlobalOverrideRules
    {
        get => globalOverrideRules;
        set => SetProperty(ref globalOverrideRules, value ?? []);
    }

    public ObservableCollection<TriggerRule> GlobalMovementRules
    {
        get => globalMovementRules;
        set => SetProperty(ref globalMovementRules, value ?? []);
    }

    public ObservableCollection<MovementRedeemSet> MovementRedeemSets
    {
        get => movementRedeemSets;
        set => SetProperty(ref movementRedeemSets, value ?? []);
    }

    public ObservableCollection<UniversalTriggerRule> UniversalTriggers
    {
        get => universalTriggers;
        set => SetProperty(ref universalTriggers, value ?? []);
    }

    public bool UniversalTriggersChatCollapsed { get; set; }

    public bool UniversalTriggersRewardCollapsed { get; set; }

    public bool UniversalTriggersBitsCollapsed { get; set; }

    public bool UniversalTriggersSubsCollapsed { get; set; }

    public bool UniversalTriggersFollowsCollapsed { get; set; }

    public ObservableCollection<AvatarScaleSet> AvatarScaleSets
    {
        get => avatarScaleSets;
        set => SetProperty(ref avatarScaleSets, value ?? []);
    }

    public ObservableCollection<AvatarScaleRule> AvatarScaleRules
    {
        get => avatarScaleRules;
        set => SetProperty(ref avatarScaleRules, value ?? []);
    }

    public AvatarScaleMasterRewardSettings AvatarScaleMasterReward
    {
        get => avatarScaleMasterReward;
        set => SetProperty(ref avatarScaleMasterReward, value ?? new AvatarScaleMasterRewardSettings());
    }

    public ObservableCollection<PowerUpRule> PowerUpRules
    {
        get => powerUpRules;
        set => SetProperty(ref powerUpRules, value ?? []);
    }

    public RewardFireSaleSettings RewardFireSale
    {
        get => rewardFireSale;
        set => SetProperty(ref rewardFireSale, value ?? new RewardFireSaleSettings());
    }

    public CashPaymentConnectionSettings CashPayments
    {
        get => cashPayments;
        set => SetProperty(ref cashPayments, value ?? new CashPaymentConnectionSettings());
    }

    public ObservableCollection<CashPaymentRule> CashPaymentRules
    {
        get => cashPaymentRules;
        set => SetProperty(ref cashPaymentRules, value ?? []);
    }

    public ObservableCollection<TriggerRule> Rules
    {
        get => rules;
        set => SetProperty(ref rules, value ?? []);
    }

    public ObservableCollection<AvatarSwapProfile> AvatarSwapProfiles
    {
        get => avatarSwapProfiles;
        set => SetProperty(ref avatarSwapProfiles, value ?? []);
    }

    public string? MasterAvatarSwapReturnId
    {
        get => masterAvatarSwapReturnId;
        set
        {
            if (SetProperty(ref masterAvatarSwapReturnId, value))
            {
                RaisePropertyChanged(nameof(HasMasterAvatarSwapReturn));
                RaisePropertyChanged(nameof(MasterAvatarSwapReturnDisplayName));
            }
        }
    }

    public string? MasterAvatarSwapReturnName
    {
        get => masterAvatarSwapReturnName;
        set
        {
            if (SetProperty(ref masterAvatarSwapReturnName, value))
            {
                RaisePropertyChanged(nameof(MasterAvatarSwapReturnDisplayName));
            }
        }
    }

    public int AvatarChangeToAvatarSwapMigrationVersion
    {
        get => avatarChangeToAvatarSwapMigrationVersion;
        set => SetProperty(ref avatarChangeToAvatarSwapMigrationVersion, value);
    }

    public bool HasMasterAvatarSwapReturn => !string.IsNullOrWhiteSpace(MasterAvatarSwapReturnId);

    public string MasterAvatarSwapReturnDisplayName => string.IsNullOrWhiteSpace(MasterAvatarSwapReturnName)
        ? (string.IsNullOrWhiteSpace(MasterAvatarSwapReturnId) ? "(no return avatar picked)" : MasterAvatarSwapReturnId)
        : MasterAvatarSwapReturnName;

    public AppTheme Theme
    {
        get => theme;
        set => SetProperty(ref theme, Enum.IsDefined(value) ? value : AppTheme.VoidCrystal);
    }

    public AppLanguage Language
    {
        get => language;
        set => SetProperty(ref language, Enum.IsDefined(value) ? value : AppLanguage.SystemDefault);
    }

    public int InterfaceOpacityPercent
    {
        get => interfaceOpacityPercent;
        set
        {
            var clamped = Math.Clamp(value, 10, 100);
            if (SetProperty(ref interfaceOpacityPercent, clamped))
            {
                RaisePropertyChanged(nameof(InterfaceOpacity));
            }
        }
    }

    public double InterfaceOpacity => InterfaceOpacityPercent / 100d;

    public int ChatTextSize
    {
        get => chatTextSize;
        set => SetProperty(ref chatTextSize, Math.Clamp(value, 12, 40));
    }

    public int ChatOpacityPercent
    {
        get => chatOpacityPercent;
        set
        {
            var clamped = Math.Clamp(value, 25, 100);
            if (SetProperty(ref chatOpacityPercent, clamped))
            {
                RaisePropertyChanged(nameof(ChatOpacity));
            }
        }
    }

    public double ChatOpacity => ChatOpacityPercent / 100d;

    public bool ChatShowTimestamps
    {
        get => chatShowTimestamps;
        set => SetProperty(ref chatShowTimestamps, value);
    }

    public ChatTimestampFormat ChatTimestampFormat
    {
        get => chatTimestampFormat;
        set => SetProperty(
            ref chatTimestampFormat,
            Enum.IsDefined(value) ? value : ChatTimestampFormat.TwentyFourHour);
    }

    public string ChatFontFamily
    {
        get => chatFontFamily;
        set
        {
            var normalized = string.IsNullOrWhiteSpace(value)
                ? "Verdana"
                : value.Trim();
            SetProperty(ref chatFontFamily, normalized);
        }
    }

    public bool ChatboxAlwaysOnTop
    {
        get => chatboxAlwaysOnTop;
        set => SetProperty(ref chatboxAlwaysOnTop, value);
    }

    public bool ChatboxSettingsPanelOpen
    {
        get => chatboxSettingsPanelOpen;
        set => SetProperty(ref chatboxSettingsPanelOpen, value);
    }

    public bool ChatboxOverlayMode
    {
        get => chatboxOverlayMode;
        set => SetProperty(ref chatboxOverlayMode, value);
    }

    public bool ChatboxXsOverlayCompatibilityMode
    {
        get => chatboxXsOverlayCompatibilityMode;
        set => SetProperty(ref chatboxXsOverlayCompatibilityMode, value);
    }

    public bool ChatboxOscEnabled
    {
        get => chatboxOscEnabled;
        set => SetProperty(ref chatboxOscEnabled, value);
    }

    public int ChatboxOscDelaySeconds
    {
        get => chatboxOscDelaySeconds;
        set => SetProperty(ref chatboxOscDelaySeconds, Math.Clamp(value, 1, 6));
    }

    public bool ChatboxViewerSoundEnabled
    {
        get => chatboxViewerSoundEnabled;
        set => SetProperty(ref chatboxViewerSoundEnabled, value);
    }

    public bool UseBroadcasterAsBotSender
    {
        get => useBroadcasterAsBotSender;
        set => SetProperty(ref useBroadcasterAsBotSender, value);
    }

    public bool SupporterOverrideInfoMessageEnabled
    {
        get => supporterOverrideInfoMessageEnabled;
        set => SetProperty(ref supporterOverrideInfoMessageEnabled, value);
    }

    public bool TriggerInfoAnnouncementsEnabled
    {
        get => triggerInfoAnnouncementsEnabled;
        set => SetProperty(ref triggerInfoAnnouncementsEnabled, value);
    }

    public int TriggerInfoAnnouncementIntervalMinutes
    {
        get => triggerInfoAnnouncementIntervalMinutes;
        set => SetProperty(ref triggerInfoAnnouncementIntervalMinutes, Math.Max(1, value));
    }

    public bool TriggerInfoCommandEnabled
    {
        get => triggerInfoCommandEnabled;
        set => SetProperty(ref triggerInfoCommandEnabled, value);
    }

    public string TriggerInfoCommandText
    {
        get => triggerInfoCommandText;
        set => SetProperty(ref triggerInfoCommandText, ChatCommandUtility.Normalize(value));
    }

    public int TriggerInfoCommandCooldownSeconds
    {
        get => triggerInfoCommandCooldownSeconds;
        set => SetProperty(ref triggerInfoCommandCooldownSeconds, Math.Max(0, value));
    }

    public ChatCommandPermission TriggerInfoCommandPermission
    {
        get => triggerInfoCommandPermission;
        set => SetProperty(
            ref triggerInfoCommandPermission,
            Enum.IsDefined(value) ? value : ChatCommandPermission.Everyone);
    }

    public bool UseManagedRewardTitlePrefix
    {
        get => useManagedRewardTitlePrefix;
        set => SetProperty(ref useManagedRewardTitlePrefix, value);
    }

    public bool WorldCommandEnabled
    {
        get => worldCommandEnabled;
        set => SetProperty(ref worldCommandEnabled, value);
    }

    public string WorldCommandText
    {
        get => worldCommandText;
        set => SetProperty(ref worldCommandText, ChatCommandUtility.Normalize(value));
    }

    public int WorldCommandCooldownSeconds
    {
        get => worldCommandCooldownSeconds;
        set => SetProperty(ref worldCommandCooldownSeconds, Math.Max(0, value));
    }

    public ChatCommandPermission WorldCommandPermission
    {
        get => worldCommandPermission;
        set => SetProperty(
            ref worldCommandPermission,
            Enum.IsDefined(value) ? value : ChatCommandPermission.Everyone);
    }

    public WorldCommandBlacklistSettings WorldCommandBlacklist
    {
        get => worldCommandBlacklist;
        set => SetProperty(ref worldCommandBlacklist, value ?? new WorldCommandBlacklistSettings());
    }

    public bool ChannelPointRewardTestModeEnabled
    {
        get => channelPointRewardTestModeEnabled;
        set => SetProperty(ref channelPointRewardTestModeEnabled, value);
    }

    public bool AvatarChangeCooldownOnlyModeEnabled
    {
        get => avatarChangeCooldownOnlyModeEnabled;
        set => SetProperty(ref avatarChangeCooldownOnlyModeEnabled, value);
    }

    public bool AvatarSwapManagerUseFullRuleEditor { get; set; } = true;
    public bool AvatarSwapMigrationNoticeShown { get; set; }

    public bool EmergencyRedeemStopEnabled
    {
        get => emergencyRedeemStopEnabled;
        set => SetProperty(ref emergencyRedeemStopEnabled, value);
    }

    public bool DesktopModeInputLockEnabled
    {
        get => desktopModeInputLockEnabled;
        set => SetProperty(ref desktopModeInputLockEnabled, value);
    }

    public bool RestartVrChatInDesktopMode
    {
        get => restartVrChatInDesktopMode;
        set => SetProperty(ref restartVrChatInDesktopMode, value);
    }

    public bool LiveFeedbackHeartbeatEnabled
    {
        get => liveFeedbackHeartbeatEnabled;
        set => SetProperty(ref liveFeedbackHeartbeatEnabled, value);
    }

    public bool BetaApplicationUpdatesEnabled
    {
        get => betaApplicationUpdatesEnabled;
        set => SetProperty(ref betaApplicationUpdatesEnabled, value);
    }

    public bool EasterEggsEnabled
    {
        get => easterEggsEnabled;
        set => SetProperty(ref easterEggsEnabled, value);
    }

    public bool MainWindowTrayTipShown
    {
        get => mainWindowTrayTipShown;
        set => SetProperty(ref mainWindowTrayTipShown, value);
    }

    public string IgnoredUpdateVersion
    {
        get => ignoredUpdateVersion;
        set => SetProperty(ref ignoredUpdateVersion, string.IsNullOrWhiteSpace(value) ? string.Empty : value.Trim());
    }

    public string IgnoredBetaUpdateBaseVersion
    {
        get => ignoredBetaUpdateBaseVersion;
        set => SetProperty(ref ignoredBetaUpdateBaseVersion, string.IsNullOrWhiteSpace(value) ? string.Empty : value.Trim());
    }

    public bool PauseCommandEnabled
    {
        get => pauseCommandEnabled;
        set => SetProperty(ref pauseCommandEnabled, value);
    }

    public string PauseCommandText
    {
        get => pauseCommandText;
        set => SetProperty(ref pauseCommandText, ChatCommandUtility.Normalize(value));
    }

    public bool RedeemGroupCommandEnabled
    {
        get => redeemGroupCommandEnabled;
        set => SetProperty(ref redeemGroupCommandEnabled, value);
    }

    public bool RedeemControlCommandEnabled
    {
        get => redeemControlCommandEnabled;
        set => SetProperty(ref redeemControlCommandEnabled, value);
    }

    public ObservableCollection<RedeemGroup> RedeemGroups
    {
        get => redeemGroups;
        set => SetProperty(ref redeemGroups, value ?? []);
    }

    private void WireCustomTheme(CustomThemeSettings settings)
    {
        settings.PropertyChanged += OnCustomThemePropertyChanged;
    }

    private void UnwireCustomTheme(CustomThemeSettings settings)
    {
        settings.PropertyChanged -= OnCustomThemePropertyChanged;
    }

    private void OnCustomThemePropertyChanged(object? sender, System.ComponentModel.PropertyChangedEventArgs e)
    {
        RaisePropertyChanged(nameof(CustomTheme));
    }
}
