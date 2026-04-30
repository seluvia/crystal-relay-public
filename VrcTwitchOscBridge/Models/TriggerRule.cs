using System.Collections.ObjectModel;
using System.Collections.Specialized;
using System.ComponentModel;
using System.Text.Json.Serialization;
using System.Windows.Media;
using VrcTwitchOscBridge.Infrastructure;
using VrcTwitchOscBridge.Services;
using Brush = System.Windows.Media.Brush;

namespace VrcTwitchOscBridge.Models;

public sealed class TriggerRule : ObservableObject
{
    private static string T(string sourceText) => LocalizationService.Translate(sourceText);

    private static string TF(string sourceFormat, params object[] args) => LocalizationService.Format(sourceFormat, args);

    private Guid id = Guid.NewGuid();
    private bool isEnabled = true;
    private string name = "New Twitch trigger";
    private TwitchTriggerType triggerType = TwitchTriggerType.ChannelPoints;
    private string channelPointRewardId = string.Empty;
    private string channelPointRewardTitle = string.Empty;
    private int channelPointRewardCost = 100;
    private string managedRewardReadyColor = ManagedRewardPresentation.ReadyBackgroundColor;
    private string managedRewardCooldownColor = ManagedRewardPresentation.InUseBackgroundColor;
    private bool deleteManagedRewardWhenInactive;
    private bool chatCommandEnabled;
    private string chatCommandText = string.Empty;
    private ChatCommandPermission chatCommandPermission = ChatCommandPermission.Moderators;
    private int minimumAmount = 1;
    private bool amountScaledDurationEnabled;
    private int amountUnitsPerDuration = 1;
    private int secondsPerAmountUnit = 1;
    private int bitsAmountUnitsPerDuration = 1;
    private int bitsSecondsPerAmountUnit = 1;
    private int subscriptionsAmountUnitsPerDuration = 1;
    private int subscriptionsSecondsPerAmountUnit = 1;
    private bool maxAccumulatedDurationEnabled;
    private int maxAccumulatedDurationSeconds = 1800;
    private OscActionType actionType = OscActionType.AvatarParameter;
    private PlayerMovementDirection movementDirection = PlayerMovementDirection.Forward;
    private string parameterName = "VRCEmote";
    private OscParameterType parameterType = OscParameterType.Int;
    private IntZeroDurationMode intZeroDurationMode = global::VrcTwitchOscBridge.Models.IntZeroDurationMode.Fixed;
    private string parameterValue = "1";
    private string avatarChangeTargetId = string.Empty;
    private string avatarTargetName = string.Empty;
    private string resetValue = "0";
    private string avatarChangeResetId = string.Empty;
    private string resetAvatarName = string.Empty;
    private ObservableCollection<string> avatarRouletAvatarIds = [];
    private ObservableCollection<string> avatarRouletAvatarNames = [];
    private int rangeMinimum;
    private int rangeMaximum = 5;
    private int durationSeconds = 10;
    private int cooldownSeconds = 30;
    private string botMessageTemplate = "{user} triggered {rule}. Active for {duration}. Cooldown {cooldown}.";
    private Guid supporterAvatarProfileId = Guid.Empty;
    private string supporterAvatarId = string.Empty;
    private string supporterAvatarName = string.Empty;
    private bool sharedRewardChoiceEnabled;
    private int sharedRewardChoiceNumber;
    private string sharedRewardHelpText = string.Empty;
    private ObservableCollection<SetTriggerAction> setTriggerActions = [];
    private ObservableCollection<Guid> temporarilyDisabledRuleIds = [];
    private string supporterAvatarScopeLabel = string.Empty;

    public TriggerRule()
    {
        temporarilyDisabledRuleIds.CollectionChanged += OnTemporarilyDisabledRuleIdsChanged;
        avatarRouletAvatarIds.CollectionChanged += OnAvatarRouletAvatarIdsChanged;
        avatarRouletAvatarNames.CollectionChanged += OnAvatarRouletAvatarNamesChanged;
        setTriggerActions.CollectionChanged += OnSetTriggerActionsChanged;
    }

    public Guid Id
    {
        get => id;
        set => SetProperty(ref id, value);
    }

    public bool IsEnabled
    {
        get => isEnabled;
        set => SetProperty(ref isEnabled, value);
    }

    public string Name
    {
        get => name;
        set
        {
            if (SetProperty(ref name, value))
            {
                RaisePropertyChanged(nameof(DisplayTitle));
                RaisePropertyChanged(nameof(TriggerSummary));
            }
        }
    }

    public TwitchTriggerType TriggerType
    {
        get => triggerType;
        set
        {
            if (SetProperty(ref triggerType, value))
            {
                RaisePropertyChanged(nameof(DisplayTitle));
                RaisePropertyChanged(nameof(UsesChannelPointReward));
                RaisePropertyChanged(nameof(UsesAmountThreshold));
                RaisePropertyChanged(nameof(UsesAmountScaledDuration));
                RaisePropertyChanged(nameof(UsesBitsOutfitSetTrigger));
                RaisePropertyChanged(nameof(UsesSupporterAmountTimerSettings));
                RaisePropertyChanged(nameof(DurationHelpText));
                RaisePropertyChanged(nameof(TriggerSummary));
            }
        }
    }

    public string ChannelPointRewardId
    {
        get => channelPointRewardId;
        set
        {
            if (SetProperty(ref channelPointRewardId, value))
            {
                RaisePropertyChanged(nameof(TriggerSummary));
            }
        }
    }

    public string ChannelPointRewardTitle
    {
        get => channelPointRewardTitle;
        set
        {
            if (SetProperty(ref channelPointRewardTitle, value))
            {
                RaisePropertyChanged(nameof(DisplayTitle));
                RaisePropertyChanged(nameof(RewardDisplayTitle));
                RaisePropertyChanged(nameof(TriggerSummary));
            }
        }
    }

    public int ChannelPointRewardCost
    {
        get => channelPointRewardCost;
        set
        {
            var normalizedValue = Math.Max(0, value);
            if (SetProperty(ref channelPointRewardCost, normalizedValue))
            {
                RaisePropertyChanged(nameof(TriggerSummary));
            }
        }
    }

    public string ManagedRewardReadyColor
    {
        get => managedRewardReadyColor;
        set
        {
            var normalizedValue = ManagedRewardPresentation.NormalizeReadyBackgroundColor(value);
            if (SetProperty(ref managedRewardReadyColor, normalizedValue))
            {
                RaisePropertyChanged(nameof(ManagedRewardReadyColorBrush));
            }
        }
    }

    public string ManagedRewardCooldownColor
    {
        get => managedRewardCooldownColor;
        set
        {
            var normalizedValue = ManagedRewardPresentation.NormalizeCooldownBackgroundColor(value);
            if (SetProperty(ref managedRewardCooldownColor, normalizedValue))
            {
                RaisePropertyChanged(nameof(ManagedRewardCooldownColorBrush));
            }
        }
    }

    public bool DeleteManagedRewardWhenInactive
    {
        get => deleteManagedRewardWhenInactive;
        set
        {
            if (SetProperty(ref deleteManagedRewardWhenInactive, value))
            {
                RaisePropertyChanged(nameof(TriggerSummary));
            }
        }
    }

    public bool ChatCommandEnabled
    {
        get => chatCommandEnabled;
        set
        {
            if (SetProperty(ref chatCommandEnabled, value))
            {
                RaisePropertyChanged(nameof(RewardDisplayTitle));
                RaisePropertyChanged(nameof(TriggerSummary));
            }
        }
    }

    public string ChatCommandText
    {
        get => chatCommandText;
        set
        {
            var normalizedValue = ChatCommandUtility.Normalize(value);
            if (SetProperty(ref chatCommandText, normalizedValue))
            {
                RaisePropertyChanged(nameof(RewardDisplayTitle));
                RaisePropertyChanged(nameof(TriggerSummary));
            }
        }
    }

    public ChatCommandPermission ChatCommandPermission
    {
        get => chatCommandPermission;
        set
        {
            var normalizedValue = Enum.IsDefined(value) ? value : ChatCommandPermission.Moderators;
            if (SetProperty(ref chatCommandPermission, normalizedValue))
            {
                RaisePropertyChanged(nameof(TriggerSummary));
            }
        }
    }

    public int MinimumAmount
    {
        get => minimumAmount;
        set
        {
            if (SetProperty(ref minimumAmount, value))
            {
                RaisePropertyChanged(nameof(TriggerSummary));
            }
        }
    }

    public bool AmountScaledDurationEnabled
    {
        get => amountScaledDurationEnabled;
        set
        {
            if (SetProperty(ref amountScaledDurationEnabled, value))
            {
                RaisePropertyChanged(nameof(UsesAmountScaledDuration));
                RaisePropertyChanged(nameof(DurationHelpText));
                RaisePropertyChanged(nameof(TriggerSummary));
            }
        }
    }

    public int AmountUnitsPerDuration
    {
        get => amountUnitsPerDuration;
        set
        {
            var normalizedValue = Math.Max(1, value);
            if (SetProperty(ref amountUnitsPerDuration, normalizedValue))
            {
                RaisePropertyChanged(nameof(TriggerSummary));
            }
        }
    }

    public int SecondsPerAmountUnit
    {
        get => secondsPerAmountUnit;
        set
        {
            var normalizedValue = Math.Max(1, value);
            if (SetProperty(ref secondsPerAmountUnit, normalizedValue))
            {
                RaisePropertyChanged(nameof(TriggerSummary));
            }
        }
    }

    public int BitsAmountUnitsPerDuration
    {
        get => bitsAmountUnitsPerDuration;
        set
        {
            var normalizedValue = Math.Max(1, value);
            if (SetProperty(ref bitsAmountUnitsPerDuration, normalizedValue))
            {
                RaisePropertyChanged(nameof(TriggerSummary));
            }
        }
    }

    public int BitsSecondsPerAmountUnit
    {
        get => bitsSecondsPerAmountUnit;
        set
        {
            var normalizedValue = Math.Max(1, value);
            if (SetProperty(ref bitsSecondsPerAmountUnit, normalizedValue))
            {
                RaisePropertyChanged(nameof(TriggerSummary));
            }
        }
    }

    public int SubscriptionsAmountUnitsPerDuration
    {
        get => subscriptionsAmountUnitsPerDuration;
        set
        {
            var normalizedValue = Math.Max(1, value);
            if (SetProperty(ref subscriptionsAmountUnitsPerDuration, normalizedValue))
            {
                RaisePropertyChanged(nameof(TriggerSummary));
            }
        }
    }

    public int SubscriptionsSecondsPerAmountUnit
    {
        get => subscriptionsSecondsPerAmountUnit;
        set
        {
            var normalizedValue = Math.Max(1, value);
            if (SetProperty(ref subscriptionsSecondsPerAmountUnit, normalizedValue))
            {
                RaisePropertyChanged(nameof(TriggerSummary));
            }
        }
    }

    public bool MaxAccumulatedDurationEnabled
    {
        get => maxAccumulatedDurationEnabled;
        set => SetProperty(ref maxAccumulatedDurationEnabled, value);
    }

    public int MaxAccumulatedDurationSeconds
    {
        get => maxAccumulatedDurationSeconds;
        set
        {
            var normalizedValue = Math.Max(1, value);
            SetProperty(ref maxAccumulatedDurationSeconds, normalizedValue);
        }
    }

    public OscActionType ActionType
    {
        get => actionType;
        set
        {
            if (SetProperty(ref actionType, value))
            {
                if (value == OscActionType.SetTrigger)
                {
                    SharedRewardChoiceEnabled = true;
                    if (DurationSeconds <= 0)
                    {
                        DurationSeconds = 3;
                    }

                    EnsureSetTriggerAction();
                }

                if (value == OscActionType.PlayerMovement && DurationSeconds <= 0)
                {
                    DurationSeconds = 1;
                }
                else if (value == OscActionType.AvatarRoulet && DurationSeconds <= 0)
                {
                    DurationSeconds = 20;
                }

                RaisePropertyChanged(nameof(UsesAvatarParameter));
                RaisePropertyChanged(nameof(UsesAvatarChange));
                RaisePropertyChanged(nameof(UsesAvatarRoulet));
                RaisePropertyChanged(nameof(UsesPlayerMovement));
                RaisePropertyChanged(nameof(UsesSetTrigger));
                RaisePropertyChanged(nameof(UsesCooldown));
                RaisePropertyChanged(nameof(UsesBitsOutfitSetTrigger));
                RaisePropertyChanged(nameof(UsesSupporterAmountTimerSettings));
                RaisePropertyChanged(nameof(AvatarRedeemListSummary));
                RaiseActionVisibilityProperties();
                RaisePropertyChanged(nameof(TriggerSummary));
            }
        }
    }

    public PlayerMovementDirection MovementDirection
    {
        get => movementDirection;
        set
        {
            if (SetProperty(ref movementDirection, value))
            {
                RaisePropertyChanged(nameof(TriggerSummary));
            }
        }
    }

    public string ParameterName
    {
        get => parameterName;
        set
        {
            if (SetProperty(ref parameterName, value))
            {
                RaisePropertyChanged(nameof(AvatarRedeemListSummary));
                RaisePropertyChanged(nameof(TriggerSummary));
            }
        }
    }

    public OscParameterType ParameterType
    {
        get => parameterType;
        set
        {
            if (SetProperty(ref parameterType, value))
            {
                // Type switches reset incompatible editor values on purpose so a bool/int/float
                // rule never carries stale text like True into a numeric field or vice versa.
                if (value == OscParameterType.Bool)
                {
                    ParameterValue = "True";
                    ResetValue = "False";
                }
                else if (value == OscParameterType.Int)
                {
                    ParameterValue = "0";
                    ResetValue = "0";
                    RangeMinimum = 0;
                    RangeMaximum = RangeMaximum >= 0 ? RangeMaximum : 5;
                    if (RangeMaximum <= RangeMinimum)
                    {
                        RangeMaximum = 5;
                    }
                }
                else if (value == OscParameterType.Float)
                {
                    ParameterValue = "0.0";
                    ResetValue = "0.0";
                }

                RaiseActionVisibilityProperties();
                RaisePropertyChanged(nameof(TriggerSummary));
            }
        }
    }

    public IntZeroDurationMode IntZeroDurationMode
    {
        get => intZeroDurationMode;
        set
        {
            if (SetProperty(ref intZeroDurationMode, value))
            {
                RaisePropertyChanged(nameof(UsesIntFixedInstantValue));
                RaisePropertyChanged(nameof(UsesIntRangeInputs));
                RaisePropertyChanged(nameof(IntModeHelpText));
                RaisePropertyChanged(nameof(TriggerSummary));
            }
        }
    }

    public string ParameterValue
    {
        get => parameterValue;
        set
        {
            if (SetProperty(ref parameterValue, value))
            {
                RaisePropertyChanged(nameof(TriggerSummary));
            }
        }
    }

    public string AvatarTargetName
    {
        get => avatarTargetName;
        set
        {
            if (SetProperty(ref avatarTargetName, value))
            {
                RaisePropertyChanged(nameof(TriggerSummary));
            }
        }
    }

    public string AvatarChangeTargetId
    {
        get => avatarChangeTargetId;
        set
        {
            if (SetProperty(ref avatarChangeTargetId, value))
            {
                RaisePropertyChanged(nameof(TriggerSummary));
            }
        }
    }

    public string ResetValue
    {
        get => resetValue;
        set
        {
            if (SetProperty(ref resetValue, value))
            {
                RaisePropertyChanged(nameof(TriggerSummary));
            }
        }
    }

    public string AvatarChangeResetId
    {
        get => avatarChangeResetId;
        set
        {
            if (SetProperty(ref avatarChangeResetId, value))
            {
                RaisePropertyChanged(nameof(TriggerSummary));
            }
        }
    }

    public string ResetAvatarName
    {
        get => resetAvatarName;
        set
        {
            if (SetProperty(ref resetAvatarName, value))
            {
                RaisePropertyChanged(nameof(TriggerSummary));
            }
        }
    }

    public ObservableCollection<string> AvatarRouletAvatarIds
    {
        get => avatarRouletAvatarIds;
        set
        {
            if (ReferenceEquals(avatarRouletAvatarIds, value))
            {
                return;
            }

            avatarRouletAvatarIds.CollectionChanged -= OnAvatarRouletAvatarIdsChanged;
            if (SetProperty(ref avatarRouletAvatarIds, value ?? []))
            {
                avatarRouletAvatarIds.CollectionChanged += OnAvatarRouletAvatarIdsChanged;
                RaisePropertyChanged(nameof(HasAvatarRouletPool));
                RaisePropertyChanged(nameof(AvatarRouletPoolSummary));
                RaisePropertyChanged(nameof(TriggerSummary));
            }
        }
    }

    public ObservableCollection<string> AvatarRouletAvatarNames
    {
        get => avatarRouletAvatarNames;
        set
        {
            if (ReferenceEquals(avatarRouletAvatarNames, value))
            {
                return;
            }

            avatarRouletAvatarNames.CollectionChanged -= OnAvatarRouletAvatarNamesChanged;
            if (SetProperty(ref avatarRouletAvatarNames, value ?? []))
            {
                avatarRouletAvatarNames.CollectionChanged += OnAvatarRouletAvatarNamesChanged;
                RaisePropertyChanged(nameof(AvatarRouletPoolSummary));
                RaisePropertyChanged(nameof(TriggerSummary));
            }
        }
    }

    public int RangeMinimum
    {
        get => rangeMinimum;
        set
        {
            if (SetProperty(ref rangeMinimum, value))
            {
                RaisePropertyChanged(nameof(IntModeHelpText));
                RaisePropertyChanged(nameof(TriggerSummary));
            }
        }
    }

    public int RangeMaximum
    {
        get => rangeMaximum;
        set
        {
            if (SetProperty(ref rangeMaximum, value))
            {
                RaisePropertyChanged(nameof(IntModeHelpText));
                RaisePropertyChanged(nameof(TriggerSummary));
            }
        }
    }

    public int DurationSeconds
    {
        get => durationSeconds;
        set
        {
            var normalizedValue = ActionType switch
            {
                OscActionType.PlayerMovement => Math.Max(1, value),
                OscActionType.SetTrigger => value <= 0 ? 3 : value,
                _ => value
            };
            if (SetProperty(ref durationSeconds, normalizedValue))
            {
                RaiseActionVisibilityProperties();
                RaisePropertyChanged(nameof(TriggerSummary));
            }
        }
    }

    public int CooldownSeconds
    {
        get => cooldownSeconds;
        set => SetProperty(ref cooldownSeconds, value);
    }

    public string BotMessageTemplate
    {
        get => botMessageTemplate;
        set => SetProperty(ref botMessageTemplate, value);
    }

    public Guid SupporterAvatarProfileId
    {
        get => supporterAvatarProfileId;
        set
        {
            if (SetProperty(ref supporterAvatarProfileId, value))
            {
                RaisePropertyChanged(nameof(HasSupporterAvatarScope));
                RaisePropertyChanged(nameof(TriggerSummary));
            }
        }
    }

    public string SupporterAvatarId
    {
        get => supporterAvatarId;
        set
        {
            if (SetProperty(ref supporterAvatarId, value ?? string.Empty))
            {
                RaisePropertyChanged(nameof(HasSupporterAvatarScope));
                RaisePropertyChanged(nameof(TriggerSummary));
            }
        }
    }

    public string SupporterAvatarName
    {
        get => supporterAvatarName;
        set
        {
            if (SetProperty(ref supporterAvatarName, value ?? string.Empty))
            {
                RaisePropertyChanged(nameof(TriggerSummary));
            }
        }
    }

    public bool SharedRewardChoiceEnabled
    {
        get => sharedRewardChoiceEnabled;
        set
        {
            if (SetProperty(ref sharedRewardChoiceEnabled, value))
            {
                if (value && sharedRewardChoiceNumber <= 0)
                {
                    SharedRewardChoiceNumber = 1;
                }
                else if (!value && ActionType == OscActionType.SetTrigger)
                {
                    ActionType = OscActionType.AvatarParameter;
                }

                RaisePropertyChanged(nameof(UsesSharedRewardChoice));
                RaisePropertyChanged(nameof(SharedRewardChoiceSummary));
                RaisePropertyChanged(nameof(AvatarRedeemListSummary));
                RaisePropertyChanged(nameof(TriggerSummary));
            }
        }
    }

    public int SharedRewardChoiceNumber
    {
        get => sharedRewardChoiceNumber;
        set
        {
            var normalizedValue = Math.Max(0, value);
            if (SetProperty(ref sharedRewardChoiceNumber, normalizedValue))
            {
                RaisePropertyChanged(nameof(UsesSharedRewardChoice));
                RaisePropertyChanged(nameof(SharedRewardChoiceSummary));
                RaisePropertyChanged(nameof(AvatarRedeemListSummary));
                RaisePropertyChanged(nameof(TriggerSummary));
            }
        }
    }

    public string SharedRewardHelpText
    {
        get => sharedRewardHelpText;
        set
        {
            if (SetProperty(ref sharedRewardHelpText, value ?? string.Empty))
            {
                RaisePropertyChanged(nameof(SharedRewardChoiceSummary));
                RaisePropertyChanged(nameof(AvatarRedeemListSummary));
                RaisePropertyChanged(nameof(TriggerSummary));
            }
        }
    }

    public ObservableCollection<SetTriggerAction> SetTriggerActions
    {
        get => setTriggerActions;
        set
        {
            if (ReferenceEquals(setTriggerActions, value))
            {
                return;
            }

            setTriggerActions.CollectionChanged -= OnSetTriggerActionsChanged;
            foreach (var action in setTriggerActions)
            {
                action.PropertyChanged -= OnSetTriggerActionPropertyChanged;
            }

            if (SetProperty(ref setTriggerActions, value ?? []))
            {
                setTriggerActions.CollectionChanged += OnSetTriggerActionsChanged;
                foreach (var action in setTriggerActions)
                {
                    action.PropertyChanged += OnSetTriggerActionPropertyChanged;
                }

                RaiseSetTriggerProperties();
            }
        }
    }

    public ObservableCollection<Guid> TemporarilyDisabledRuleIds
    {
        get => temporarilyDisabledRuleIds;
        set
        {
            if (ReferenceEquals(temporarilyDisabledRuleIds, value))
            {
                return;
            }

            temporarilyDisabledRuleIds.CollectionChanged -= OnTemporarilyDisabledRuleIdsChanged;
            if (SetProperty(ref temporarilyDisabledRuleIds, value ?? []))
            {
                temporarilyDisabledRuleIds.CollectionChanged += OnTemporarilyDisabledRuleIdsChanged;
                RaisePropertyChanged(nameof(HasSpecialRuleLockouts));
            }
        }
    }

    public bool UsesChannelPointReward => TriggerType == TwitchTriggerType.ChannelPoints;

    public bool UsesAmountThreshold => TriggerType != TwitchTriggerType.ChannelPoints;

    public bool UsesAmountScaledDuration => UsesAmountThreshold && AmountScaledDurationEnabled;

    public bool UsesBitsOutfitSetTrigger => TriggerType == TwitchTriggerType.Bits && UsesSetTrigger;

    public bool UsesSupporterAmountTimerSettings => UsesAmountThreshold && !UsesSetTrigger;

    public bool HasSupporterAvatarScope => !string.IsNullOrWhiteSpace(SupporterAvatarId);

    [JsonIgnore]
    public string SupporterAvatarScopeLabel
    {
        get => supporterAvatarScopeLabel;
        set
        {
            if (SetProperty(ref supporterAvatarScopeLabel, value ?? string.Empty))
            {
                RaisePropertyChanged(nameof(HasSupporterAvatarScopeLabel));
            }
        }
    }

    [JsonIgnore]
    public bool HasSupporterAvatarScopeLabel => !string.IsNullOrWhiteSpace(SupporterAvatarScopeLabel);

    public bool UsesAvatarParameter => ActionType == OscActionType.AvatarParameter;

    public bool UsesAvatarChange => ActionType == OscActionType.AvatarChange;

    public bool UsesAvatarRoulet => ActionType == OscActionType.AvatarRoulet;

    public bool UsesPlayerMovement => ActionType == OscActionType.PlayerMovement;

    public bool UsesSetTrigger => ActionType == OscActionType.SetTrigger;

    public bool UsesCooldown => ActionType != OscActionType.PlayerMovement;

    public bool HasSpecialRuleLockouts => TemporarilyDisabledRuleIds.Count > 0;

    public bool HasConfiguredChatCommand => ChatCommandUtility.IsConfigured(ChatCommandText);

    public bool UsesSharedRewardChoice => UsesChannelPointReward && SharedRewardChoiceEnabled && SharedRewardChoiceNumber > 0;

    public bool HasSetTriggerActions => SetTriggerActions.Count > 0;

    public int SetTriggerActionCount => SetTriggerActions.Count;

    public Brush ManagedRewardReadyColorBrush => CreateColorBrush(ManagedRewardReadyColor);

    public Brush ManagedRewardCooldownColorBrush => CreateColorBrush(ManagedRewardCooldownColor);

    public string DisplayTitle
    {
        get
        {
            if (TriggerType == TwitchTriggerType.ChannelPoints)
            {
                return string.IsNullOrWhiteSpace(ChannelPointRewardTitle)
                    ? HasConfiguredChatCommand
                        ? ChatCommandText.Trim()
                        : (string.IsNullOrWhiteSpace(Name) ? "New Redeem" : Name.Trim())
                    : ChannelPointRewardTitle.Trim();
            }

            return string.IsNullOrWhiteSpace(Name)
                ? "New Rule"
                : Name.Trim();
        }
    }

    public string RewardDisplayTitle => string.IsNullOrWhiteSpace(ChannelPointRewardTitle)
        ? HasConfiguredChatCommand
            ? ChatCommandText.Trim()
            : "Set reward name"
        : ChannelPointRewardTitle.Trim();

    public string AvatarRedeemListSummary
    {
        get
        {
            var parameterSummary = UsesSetTrigger
                ? SetTriggerSummary
                : string.IsNullOrWhiteSpace(ParameterName)
                    ? T("Pick the avatar parameter below.")
                    : TF("Parameter: {0}", ParameterName.Trim());

            return UsesSharedRewardChoice
                ? TF("{0} | {1}", SharedRewardChoiceSummary, parameterSummary)
                : parameterSummary;
        }
    }

    public string SharedRewardChoiceSummary => UsesSharedRewardChoice
        ? TF("Shared reward choice #{0}", SharedRewardChoiceNumber)
        : T("Shared reward choice not enabled");

    public string SetTriggerSummary => TF("Set Trigger ({0} params)", SetTriggerActions.Count);

    public bool UsesTimedAction => DurationSeconds > 0;

    public bool UsesInstantAction => DurationSeconds <= 0;

    public bool UsesBoolParameter => UsesAvatarParameter && ParameterType == OscParameterType.Bool;

    public bool UsesIntParameter => UsesAvatarParameter && ParameterType == OscParameterType.Int;

    public bool UsesTextOrFloatParameter => UsesAvatarParameter && (ParameterType == OscParameterType.Float || ParameterType == OscParameterType.String);

    public bool UsesBoolTimedValues => UsesBoolParameter && UsesTimedAction;

    public bool UsesBoolToggleHint => UsesBoolParameter && UsesInstantAction;

    public bool UsesIntInstantModeOptions => UsesIntParameter && UsesInstantAction;

    public bool UsesIntFixedInstantValue => UsesIntParameter && UsesInstantAction && IntZeroDurationMode == global::VrcTwitchOscBridge.Models.IntZeroDurationMode.Fixed;

    public bool UsesIntRangeInputs => UsesIntParameter && UsesInstantAction && IntZeroDurationMode != global::VrcTwitchOscBridge.Models.IntZeroDurationMode.Fixed;

    public bool UsesIntTimedValues => UsesIntParameter && UsesTimedAction;

    public bool UsesDirectInstantValue => UsesTextOrFloatParameter && UsesInstantAction;

    public bool UsesDirectTimedValues => UsesTextOrFloatParameter && UsesTimedAction;

    public bool UsesAvatarChangeTimedReset => UsesAvatarChange && UsesTimedAction;

    public bool HasAvatarRouletPool => AvatarRouletAvatarIds.Count > 0;

    public string AvatarRouletPoolSummary
    {
        get
        {
            var cleanNames = AvatarRouletAvatarNames
                .Select(name => name?.Trim() ?? string.Empty)
                .Where(name => !string.IsNullOrWhiteSpace(name))
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .ToArray();
            if (cleanNames.Length == 1)
            {
                return cleanNames[0];
            }

            if (cleanNames.Length == 2)
            {
                return $"{cleanNames[0]} and {cleanNames[1]}";
            }

            if (cleanNames.Length >= 3)
            {
                return $"{cleanNames[0]}, {cleanNames[1]}, and {cleanNames.Length - 2} more";
            }

            return HasAvatarRouletPool
                ? AvatarRouletAvatarIds.Count == 1
                    ? "1 avatar selected"
                    : $"{AvatarRouletAvatarIds.Count} avatars selected"
                : "Pick the roulette avatar pool.";
        }
    }

    public string DurationHelpText => UsesPlayerMovement
        ? T("Use whole seconds. Crystal Relay holds the selected VRChat movement input for the full Active Time, then releases it automatically. Movement redeems do not use 0-second instant mode.")
        : UsesAvatarRoulet
            ? T("Use whole seconds. Avatar Roulette is always a timed temporary switch, so 0 is not used here. Crystal Relay stays on the rolled avatar for this long, then returns to the shared return avatar.")
            : UsesAmountScaledDuration
                ? T("Amount-scaled timer is enabled, so Active Time is calculated from the incoming bits/sub amount.")
                : UsesAvatarChange
                    ? T("Use whole seconds. Set this to 0 if you want the avatar change to stay active and become the new shared return avatar. Any value above 0 makes it a temporary switch that returns to the shared return avatar when the timer ends.")
                    : T("Use whole seconds. Set this to 0 for an instant one-shot action, or use a higher value when you want Crystal Relay to hold the parameter active for a timed redeem.");

    public string IntModeHelpText => IntZeroDurationMode switch
    {
        global::VrcTwitchOscBridge.Models.IntZeroDurationMode.Random => T("Chooses a random whole number between the minimum and maximum each time the redeem fires, then sends that value immediately."),
        global::VrcTwitchOscBridge.Models.IntZeroDurationMode.Cycle => T("Moves to the next whole number in the range each time the redeem fires. When it reaches the maximum, it wraps back to the minimum and keeps cycling."),
        _ => T("Sends the exact whole number you set below as a one-shot value right away.")
    };

    public string TriggerSummary
    {
        get
        {
            // This summary is shown on cards, lists, and selectors, so keep it compact while
            // still exposing the trigger shape reviewers need to recognize at a glance.
            var trigger = TriggerType switch
            {
                TwitchTriggerType.ChannelPoints => string.IsNullOrWhiteSpace(ChannelPointRewardTitle)
                    ? HasConfiguredChatCommand
                        ? TF("Command: {0}", ChatCommandText)
                        : T("Set redeem name")
                    : TF("Redeem: {0} ({1} pts)", ChannelPointRewardTitle, Math.Max(1, ChannelPointRewardCost)),
                TwitchTriggerType.Bits when UsesBitsOutfitSetTrigger => string.IsNullOrWhiteSpace(SharedRewardHelpText)
                    ? TF("Bits >= {0} + Outfit name needed", Math.Max(1, MinimumAmount))
                    : TF("Bits >= {0} + Outfit: {1}", Math.Max(1, MinimumAmount), SharedRewardHelpText.Trim()),
                TwitchTriggerType.Bits => AmountScaledDurationEnabled
                    ? TF("Bits >= {0} ({1}s per {2} bits)", Math.Max(1, MinimumAmount), Math.Max(1, BitsSecondsPerAmountUnit), Math.Max(1, BitsAmountUnitsPerDuration))
                    : TF("Bits >= {0}", Math.Max(1, MinimumAmount)),
                TwitchTriggerType.Subscriptions => AmountScaledDurationEnabled
                    ? TF("Subs >= {0} ({1}s per {2} subs)", Math.Max(1, MinimumAmount), Math.Max(1, SubscriptionsSecondsPerAmountUnit), Math.Max(1, SubscriptionsAmountUnitsPerDuration))
                    : TF("Subs >= {0}", Math.Max(1, MinimumAmount)),
                _ => Name
            };

            if (HasConfiguredChatCommand
                && (TriggerType != TwitchTriggerType.ChannelPoints || !string.IsNullOrWhiteSpace(ChannelPointRewardTitle)))
            {
                trigger = TF("{0} + Command: {1}", trigger, ChatCommandText);
            }

            if (UsesSharedRewardChoice)
            {
                trigger = TF("{0} + Choice #{1}", trigger, SharedRewardChoiceNumber);
            }

            var action = ActionType switch
            {
                OscActionType.AvatarChange => string.IsNullOrWhiteSpace(AvatarChangeTargetId)
                    ? T("Pick avatar")
                    : DurationSeconds <= 0
                        ? TF("Switch to {0} permanently", GetAvatarDisplayName(AvatarTargetName, AvatarChangeTargetId))
                        : TF("Switch to {0}", GetAvatarDisplayName(AvatarTargetName, AvatarChangeTargetId)),
                OscActionType.AvatarRoulet => HasAvatarRouletPool
                    ? TF("Roll from {0}", AvatarRouletPoolSummary)
                    : T("Pick avatar pool"),
                OscActionType.PlayerMovement => TF("{0} for {1}", DescribeMovementDirection(MovementDirection), DescribeDuration(Math.Max(1, DurationSeconds))),
                OscActionType.AvatarParameter when UsesBoolToggleHint => TF("Toggle {0}", ParameterName),
                OscActionType.AvatarParameter when UsesIntParameter && UsesInstantAction && IntZeroDurationMode == global::VrcTwitchOscBridge.Models.IntZeroDurationMode.Random =>
                    TF("Random {0} ({1}-{2})", ParameterName, Math.Min(RangeMinimum, RangeMaximum), Math.Max(RangeMinimum, RangeMaximum)),
                OscActionType.AvatarParameter when UsesIntParameter && UsesInstantAction && IntZeroDurationMode == global::VrcTwitchOscBridge.Models.IntZeroDurationMode.Cycle =>
                    TF("Cycle {0} ({1}-{2})", ParameterName, Math.Min(RangeMinimum, RangeMaximum), Math.Max(RangeMinimum, RangeMaximum)),
                OscActionType.SetTrigger => SetTriggerSummary,
                _ => $"{ParameterName} -> {ParameterValue}"
            };

            return TF("{0} | {1}", trigger, action);
        }
    }

    private static string GetAvatarDisplayName(string avatarName, string avatarId)
    {
        if (!string.IsNullOrWhiteSpace(avatarName)
            && !string.Equals(avatarName.Trim(), avatarId?.Trim() ?? string.Empty, StringComparison.Ordinal))
        {
            return avatarName;
        }

        return T("selected avatar");
    }

    private static Brush CreateColorBrush(string colorText)
    {
        var brush = new SolidColorBrush((Color)ColorConverter.ConvertFromString(colorText));
        brush.Freeze();
        return brush;
    }

    private void RaiseActionVisibilityProperties()
    {
        RaisePropertyChanged(nameof(UsesTimedAction));
        RaisePropertyChanged(nameof(UsesInstantAction));
        RaisePropertyChanged(nameof(UsesPlayerMovement));
        RaisePropertyChanged(nameof(UsesSetTrigger));
        RaisePropertyChanged(nameof(UsesCooldown));
        RaisePropertyChanged(nameof(UsesBoolParameter));
        RaisePropertyChanged(nameof(UsesIntParameter));
        RaisePropertyChanged(nameof(UsesTextOrFloatParameter));
        RaisePropertyChanged(nameof(UsesBoolTimedValues));
        RaisePropertyChanged(nameof(UsesBoolToggleHint));
        RaisePropertyChanged(nameof(UsesIntInstantModeOptions));
        RaisePropertyChanged(nameof(UsesIntFixedInstantValue));
        RaisePropertyChanged(nameof(UsesIntRangeInputs));
        RaisePropertyChanged(nameof(UsesIntTimedValues));
        RaisePropertyChanged(nameof(UsesDirectInstantValue));
        RaisePropertyChanged(nameof(UsesDirectTimedValues));
        RaisePropertyChanged(nameof(UsesAvatarChangeTimedReset));
        RaisePropertyChanged(nameof(UsesAvatarRoulet));
        RaisePropertyChanged(nameof(HasAvatarRouletPool));
        RaisePropertyChanged(nameof(AvatarRouletPoolSummary));
        RaisePropertyChanged(nameof(HasSetTriggerActions));
        RaisePropertyChanged(nameof(SetTriggerActionCount));
        RaisePropertyChanged(nameof(SetTriggerSummary));
        RaisePropertyChanged(nameof(DurationHelpText));
        RaisePropertyChanged(nameof(IntModeHelpText));
    }

    private static string DescribeMovementDirection(PlayerMovementDirection direction) => direction switch
    {
        PlayerMovementDirection.Forward => T("Move Forward"),
        PlayerMovementDirection.Backward => T("Move Backward"),
        PlayerMovementDirection.Left => T("Move Left"),
        PlayerMovementDirection.Right => T("Move Right"),
        PlayerMovementDirection.Jump => T("Jump"),
        PlayerMovementDirection.SpinLeft => T("Spin Left"),
        PlayerMovementDirection.SpinRight => T("Spin Right"),
        PlayerMovementDirection.StopMovement => T("Stop Movement"),
        PlayerMovementDirection.StopTurning => T("Stop Turning"),
        PlayerMovementDirection.StopAll => T("Stop All"),
        _ => T("Move")
    };

    private static string DescribeDuration(int seconds) => $"{Math.Max(1, seconds)}s";

    private void OnTemporarilyDisabledRuleIdsChanged(object? sender, NotifyCollectionChangedEventArgs e)
    {
        RaisePropertyChanged(nameof(TemporarilyDisabledRuleIds));
        RaisePropertyChanged(nameof(HasSpecialRuleLockouts));
    }

    private void OnAvatarRouletAvatarIdsChanged(object? sender, NotifyCollectionChangedEventArgs e)
    {
        RaisePropertyChanged(nameof(AvatarRouletAvatarIds));
        RaisePropertyChanged(nameof(HasAvatarRouletPool));
        RaisePropertyChanged(nameof(AvatarRouletPoolSummary));
        RaisePropertyChanged(nameof(TriggerSummary));
    }

    private void OnAvatarRouletAvatarNamesChanged(object? sender, NotifyCollectionChangedEventArgs e)
    {
        RaisePropertyChanged(nameof(AvatarRouletAvatarNames));
        RaisePropertyChanged(nameof(AvatarRouletPoolSummary));
        RaisePropertyChanged(nameof(TriggerSummary));
    }

    private void OnSetTriggerActionsChanged(object? sender, NotifyCollectionChangedEventArgs e)
    {
        if (e.OldItems is not null)
        {
            foreach (SetTriggerAction action in e.OldItems)
            {
                action.PropertyChanged -= OnSetTriggerActionPropertyChanged;
            }
        }

        if (e.NewItems is not null)
        {
            foreach (SetTriggerAction action in e.NewItems)
            {
                action.PropertyChanged += OnSetTriggerActionPropertyChanged;
            }
        }

        RaiseSetTriggerProperties();
    }

    private void OnSetTriggerActionPropertyChanged(object? sender, PropertyChangedEventArgs e)
    {
        RaiseSetTriggerProperties();
    }

    private void RaiseSetTriggerProperties()
    {
        RaisePropertyChanged(nameof(SetTriggerActions));
        RaisePropertyChanged(nameof(HasSetTriggerActions));
        RaisePropertyChanged(nameof(SetTriggerActionCount));
        RaisePropertyChanged(nameof(SetTriggerSummary));
        RaisePropertyChanged(nameof(AvatarRedeemListSummary));
        RaisePropertyChanged(nameof(TriggerSummary));
    }

    private void EnsureSetTriggerAction()
    {
        if (SetTriggerActions.Count > 0)
        {
            return;
        }

        SetTriggerActions.Add(CreateSetTriggerActionFromCurrentParameter());
    }

    private SetTriggerAction CreateSetTriggerActionFromCurrentParameter()
    {
        var normalizedType = ParameterType is OscParameterType.Bool or OscParameterType.Int or OscParameterType.Float
            ? ParameterType
            : OscParameterType.Int;
        return new SetTriggerAction
        {
            ParameterName = string.IsNullOrWhiteSpace(ParameterName) ? "VRCEmote" : ParameterName.Trim(),
            ParameterType = normalizedType,
            ParameterValue = string.IsNullOrWhiteSpace(ParameterValue)
                ? normalizedType switch
                {
                    OscParameterType.Bool => "True",
                    OscParameterType.Float => "0.0",
                    _ => "1"
                }
                : ParameterValue.Trim()
        };
    }
}
