using System.Collections.ObjectModel;
using System.Collections.Specialized;
using System.ComponentModel;
using System.Text.Json.Serialization;
using System.Windows.Media;
using VrcTwitchOscBridge.Infrastructure;
using VrcTwitchOscBridge.Services;
using Brush = System.Windows.Media.Brush;

namespace VrcTwitchOscBridge.Models;

public enum SpecialRulePairingMode
{
    HidePairedWhileActive,
    ShowPairedWhileActive
}

public enum SetTriggerRestoreMode
{
    FullSafeDiff,
    ConfiguredAndRelated,
    ConfiguredOnly
}

public sealed class SupporterFloatAddRange : ObservableObject
{
    private int minimumAmount = 1;
    private int maximumAmount;
    private string addValue = "0.05";

    public int MinimumAmount
    {
        get => minimumAmount;
        set => SetProperty(ref minimumAmount, Math.Max(1, value));
    }

    public int MaximumAmount
    {
        get => maximumAmount;
        set => SetProperty(ref maximumAmount, Math.Max(0, value));
    }

    public string AddValue
    {
        get => addValue;
        set => SetProperty(ref addValue, value?.Trim() ?? string.Empty);
    }
}

public sealed class TriggerRule : ObservableObject
{
    private static string T(string sourceText) => LocalizationService.Translate(sourceText);

    private static string TF(string sourceFormat, params object[] args) => LocalizationService.Format(sourceFormat, args);

    private Guid id = Guid.NewGuid();
    private bool isEnabled = true;
    private string name = "New Twitch trigger";
    private TwitchTriggerType triggerType = TwitchTriggerType.ChannelPoints;
    private TriggerRuleSource source = TriggerRuleSource.None;
    private string channelPointRewardId = string.Empty;
    private string channelPointRewardTitle = string.Empty;
    private string channelPointRewardDescription = string.Empty;
    private int channelPointRewardCost = 100;
    private TwitchRewardSyncMode rewardSyncMode = TwitchRewardSyncMode.CreateOrManage;
    private string managedRewardReadyColor = ManagedRewardPresentation.ReadyBackgroundColor;
    private string managedRewardCooldownColor = ManagedRewardPresentation.InUseBackgroundColor;
    private bool deleteManagedRewardWhenInactive;
    private bool chatCommandEnabled;
    private string chatCommandText = string.Empty;
    private ChatCommandPermission chatCommandPermission = ChatCommandPermission.Moderators;
    private int minimumAmount = 1;
    private bool amountScaledDurationEnabled;
    private bool addBitsToSwapTime;
    private int amountUnitsPerDuration = 1;
    private int secondsPerAmountUnit = 1;
    private int bitsAmountUnitsPerDuration = 1;
    private int bitsSecondsPerAmountUnit = 1;
    private int subscriptionsAmountUnitsPerDuration = 1;
    private int subscriptionsSecondsPerAmountUnit = 1;
    private int subscriptionTier1SecondsPerSub = 1;
    private int subscriptionTier2SecondsPerSub = 1;
    private int subscriptionTier3SecondsPerSub = 1;
    private bool subscriptionTier1Enabled = true;
    private bool subscriptionTier2Enabled = true;
    private bool subscriptionTier3Enabled = true;
    private bool maxAccumulatedDurationEnabled;
    private int maxAccumulatedDurationSeconds = 1800;
    private OscActionType actionType = OscActionType.AvatarParameter;
    private PlayerMovementDirection movementDirection = PlayerMovementDirection.Forward;
    private string parameterName = "VRCEmote";
    private OscParameterType parameterType = OscParameterType.Int;
    private IntZeroDurationMode intZeroDurationMode = global::VrcTwitchOscBridge.Models.IntZeroDurationMode.Fixed;
    private string parameterValue = "1";
    private FloatValueMode floatValueMode = global::VrcTwitchOscBridge.Models.FloatValueMode.Decimal;
    private double floatTransitionInSeconds;
    private double floatTransitionOutSeconds;
    private FloatActionMode floatActionMode = FloatActionMode.Set;
    private double floatRangeMin = 0.0;
    private double floatRangeMax = 1.0;
    private double floatCycleStep = 0.1;
    private double floatAddAmount = 0.1;
    private double floatSubtractAmount = 0.1;
    private double floatAddSubtractAmount = 0.1;
    private double floatMultiplyFactor = 1.5;
    private double floatToggleOnValue = 1.0;
    private double floatToggleOffValue = 0.0;
    private int floatGlitchyIntervalMs = 200;
    private double floatPulseSeconds = 0.5;
    private FloatClampMode floatClampMode = FloatClampMode.ZeroToOne;
    private bool hideRewardWhenFloatMaxReached;
    private bool hideRewardWhenFloatMinReached;
    private string avatarChangeTargetId = string.Empty;
    private string avatarTargetName = string.Empty;
    private string resetValue = "0";
    private string avatarChangeResetId = string.Empty;
    private string resetAvatarName = string.Empty;
    private ObservableCollection<string> avatarRouletAvatarIds = [];
    private ObservableCollection<string> avatarRouletAvatarNames = [];
    private int rangeMinimum;
    private int rangeMaximum = 5;
    private double durationSeconds = 0;
    private int cooldownSeconds = 0;
    private string botMessageTemplate = "{user} triggered {rule}. Active for {duration}. Cooldown {cooldown}.";
    private Guid supporterAvatarProfileId = Guid.Empty;
    private string supporterAvatarId = string.Empty;
    private string supporterAvatarName = string.Empty;
    private bool sharedRewardChoiceEnabled;
    private int sharedRewardChoiceNumber;
    private string sharedRewardHelpText = string.Empty;
    private string supporterKeywordText = string.Empty;
    private bool bitsKeywordEnabled;
    private Guid activeFloatBoostRewardOwnerId = Guid.NewGuid();
    private bool activeFloatBoostRewardEnabled;
    private string activeFloatBoostRewardId = string.Empty;
    private string activeFloatBoostRewardTitle = string.Empty;
    private string activeFloatBoostRewardDescription = string.Empty;
    private int activeFloatBoostRewardCost = 100;
    private int activeFloatBoostRewardCooldownSeconds;
    private string activeFloatBoostRewardReadyColor = ManagedRewardPresentation.ReadyBackgroundColor;
    private string activeFloatBoostRewardCooldownColor = ManagedRewardPresentation.InUseBackgroundColor;
    private string activeFloatBoostAddValue = "0.05";
    private string activeFloatBoostMinimumValue = "0";
    private string activeFloatBoostMaximumValue = "1";
    private bool supporterFloatAddEnabled;
    private string supporterFloatAddMinimumValue = "0";
    private string supporterFloatAddMaximumValue = "1";
    private ObservableCollection<SupporterFloatAddRange> supporterFloatAddRanges = [new()];
    private ObservableCollection<SetTriggerAction> setTriggerActions = [];
    private SetTriggerRestoreMode setTriggerRestoreMode = SetTriggerRestoreMode.ConfiguredAndRelated;
    private SpecialRulePairingMode specialRulePairingMode = SpecialRulePairingMode.HidePairedWhileActive;
    private ObservableCollection<Guid> temporarilyDisabledRuleIds = [];
    private string supporterAvatarScopeLabel = string.Empty;

    public TriggerRule()
    {
        temporarilyDisabledRuleIds.CollectionChanged += OnTemporarilyDisabledRuleIdsChanged;
        avatarRouletAvatarIds.CollectionChanged += OnAvatarRouletAvatarIdsChanged;
        avatarRouletAvatarNames.CollectionChanged += OnAvatarRouletAvatarNamesChanged;
        supporterFloatAddRanges.CollectionChanged += OnSupporterFloatAddRangesChanged;
        WireSupporterFloatAddRanges(supporterFloatAddRanges);
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
                RaisePropertyChanged(nameof(UsesForceMovementBitsTrigger));
                RaisePropertyChanged(nameof(UsesSupporterAmountTimerSettings));
                RaisePropertyChanged(nameof(UsesSupporterFloatAdd));
                RaisePropertyChanged(nameof(UsesActiveSupporterFloatAdd));
                RaisePropertyChanged(nameof(SupporterFloatAddSummary));
                RaisePropertyChanged(nameof(DurationHelpText));
                RaisePropertyChanged(nameof(SupporterTimeSettingsSummary));
                RaisePropertyChanged(nameof(TriggerSummary));
            }
        }
    }

    public TriggerRuleSource Source
    {
        get => source;
        set
        {
            if (SetProperty(ref source, value))
            {
                RaisePropertyChanged(nameof(SourceDisplayName));
            }
        }
    }

    public string SourceDisplayName => Source switch
    {
        TriggerRuleSource.Native => "Native",
        TriggerRuleSource.AvatarSet => "From Avatar Set",
        TriggerRuleSource.GlobalOverride => "From Supporter Override",
        TriggerRuleSource.PowerUp => "From Power-up",
        TriggerRuleSource.CashPayment => "From Cash Payment",
        _ => string.Empty
    };

    public bool HasSourceBadge => Source != TriggerRuleSource.None && Source != TriggerRuleSource.Native;

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

    public string ChannelPointRewardDescription
    {
        get => channelPointRewardDescription;
        set => SetProperty(ref channelPointRewardDescription, value ?? string.Empty);
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

    public TwitchRewardSyncMode RewardSyncMode
    {
        get => rewardSyncMode;
        set
        {
            var normalizedValue = Enum.IsDefined(value)
                ? value
                : TwitchRewardSyncMode.CreateOrManage;
            if (SetProperty(ref rewardSyncMode, normalizedValue))
            {
                RaisePropertyChanged(nameof(UsesCreateOrManageReward));
                RaisePropertyChanged(nameof(UsesLinkedExistingReward));
                RaisePropertyChanged(nameof(UsesFloatHideOnLimit));
                RaisePropertyChanged(nameof(TriggerSummary));
            }
        }
    }

    public bool UsesCreateOrManageReward => RewardSyncMode == TwitchRewardSyncMode.CreateOrManage;

    public bool UsesLinkedExistingReward => RewardSyncMode == TwitchRewardSyncMode.LinkExisting;

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

    public System.Windows.Media.Brush ManagedRewardReadyBrush => HexToBrush(ManagedRewardReadyColor);

    public System.Windows.Media.Brush ManagedRewardCooldownBrush => HexToBrush(ManagedRewardCooldownColor);

    private static System.Windows.Media.Brush HexToBrush(string? hex)
    {
        if (string.IsNullOrWhiteSpace(hex)) return System.Windows.Media.Brushes.Transparent;
        try
        {
            var converter = new System.Windows.Media.BrushConverter();
            return (System.Windows.Media.Brush?)converter.ConvertFromString(hex) ?? System.Windows.Media.Brushes.Transparent;
        }
        catch
        {
            return System.Windows.Media.Brushes.Transparent;
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
                RaisePropertyChanged(nameof(SupporterTimeSettingsSummary));
                RaisePropertyChanged(nameof(TriggerSummary));
            }
        }
    }

    public bool AddBitsToSwapTime
    {
        get => addBitsToSwapTime;
        set => SetProperty(ref addBitsToSwapTime, value);
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
                RaisePropertyChanged(nameof(SupporterTimeSettingsSummary));
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
                RaisePropertyChanged(nameof(SupporterTimeSettingsSummary));
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
                RaisePropertyChanged(nameof(SupporterTimeSettingsSummary));
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
                RaisePropertyChanged(nameof(SupporterTimeSettingsSummary));
            }
        }
    }

    public int SubscriptionTier1SecondsPerSub
    {
        get => subscriptionTier1SecondsPerSub;
        set
        {
            var normalizedValue = Math.Max(1, value);
            if (SetProperty(ref subscriptionTier1SecondsPerSub, normalizedValue))
            {
                RaisePropertyChanged(nameof(TriggerSummary));
                RaisePropertyChanged(nameof(SupporterTimeSettingsSummary));
            }
        }
    }

    public int SubscriptionTier2SecondsPerSub
    {
        get => subscriptionTier2SecondsPerSub;
        set
        {
            var normalizedValue = Math.Max(1, value);
            if (SetProperty(ref subscriptionTier2SecondsPerSub, normalizedValue))
            {
                RaisePropertyChanged(nameof(TriggerSummary));
                RaisePropertyChanged(nameof(SupporterTimeSettingsSummary));
            }
        }
    }

    public int SubscriptionTier3SecondsPerSub
    {
        get => subscriptionTier3SecondsPerSub;
        set
        {
            var normalizedValue = Math.Max(1, value);
            if (SetProperty(ref subscriptionTier3SecondsPerSub, normalizedValue))
            {
                RaisePropertyChanged(nameof(TriggerSummary));
                RaisePropertyChanged(nameof(SupporterTimeSettingsSummary));
            }
        }
    }

    public bool SubscriptionTier1Enabled
    {
        get => subscriptionTier1Enabled;
        set
        {
            if (SetProperty(ref subscriptionTier1Enabled, value))
            {
                RaisePropertyChanged(nameof(TriggerSummary));
            }
        }
    }

    public bool SubscriptionTier2Enabled
    {
        get => subscriptionTier2Enabled;
        set
        {
            if (SetProperty(ref subscriptionTier2Enabled, value))
            {
                RaisePropertyChanged(nameof(TriggerSummary));
            }
        }
    }

    public bool SubscriptionTier3Enabled
    {
        get => subscriptionTier3Enabled;
        set
        {
            if (SetProperty(ref subscriptionTier3Enabled, value))
            {
                RaisePropertyChanged(nameof(TriggerSummary));
            }
        }
    }

    public bool MaxAccumulatedDurationEnabled
    {
        get => maxAccumulatedDurationEnabled;
        set
        {
            if (SetProperty(ref maxAccumulatedDurationEnabled, value))
            {
                RaisePropertyChanged(nameof(SupporterTimeSettingsSummary));
            }
        }
    }

    public int MaxAccumulatedDurationSeconds
    {
        get => maxAccumulatedDurationSeconds;
        set
        {
            var normalizedValue = Math.Max(1, value);
            if (SetProperty(ref maxAccumulatedDurationSeconds, normalizedValue))
            {
                RaisePropertyChanged(nameof(SupporterTimeSettingsSummary));
            }
        }
    }

    public OscActionType ActionType
    {
        get => actionType;
        set
        {
            var previousActionType = actionType;
            if (SetProperty(ref actionType, value))
            {
                if (value == OscActionType.SetTrigger)
                {
                    SharedRewardChoiceEnabled = true;
                    if (previousActionType != OscActionType.SetTrigger
                        && setTriggerRestoreMode == SetTriggerRestoreMode.FullSafeDiff)
                    {
                        SetTriggerRestoreMode = SetTriggerRestoreMode.ConfiguredAndRelated;
                    }

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
                RaisePropertyChanged(nameof(UsesForceMovementBitsTrigger));
                RaisePropertyChanged(nameof(UsesSupporterAmountTimerSettings));
                RaisePropertyChanged(nameof(UsesSupporterFloatAdd));
                RaisePropertyChanged(nameof(UsesActiveSupporterFloatAdd));
                RaisePropertyChanged(nameof(SupporterFloatAddSummary));
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
                    ResetValue = string.Empty;
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

    public FloatValueMode FloatValueMode
    {
        get => floatValueMode;
        set
        {
            var normalizedValue = Enum.IsDefined(value) ? value : global::VrcTwitchOscBridge.Models.FloatValueMode.Decimal;
            var previousValue = floatValueMode;
            if (SetProperty(ref floatValueMode, normalizedValue))
            {
                ParameterValue = FloatValueModeConverter.ConvertDisplayText(ParameterValue, previousValue, normalizedValue);
                ResetValue = FloatValueModeConverter.ConvertDisplayText(ResetValue, previousValue, normalizedValue);
                ActiveFloatBoostAddValue = FloatValueModeConverter.ConvertDisplayText(ActiveFloatBoostAddValue, previousValue, normalizedValue);
                ActiveFloatBoostMinimumValue = FloatValueModeConverter.ConvertDisplayText(ActiveFloatBoostMinimumValue, previousValue, normalizedValue);
                ActiveFloatBoostMaximumValue = FloatValueModeConverter.ConvertDisplayText(ActiveFloatBoostMaximumValue, previousValue, normalizedValue);
                SupporterFloatAddMinimumValue = FloatValueModeConverter.ConvertDisplayText(SupporterFloatAddMinimumValue, previousValue, normalizedValue);
                SupporterFloatAddMaximumValue = FloatValueModeConverter.ConvertDisplayText(SupporterFloatAddMaximumValue, previousValue, normalizedValue);
                foreach (var range in SupporterFloatAddRanges)
                {
                    range.AddValue = FloatValueModeConverter.ConvertDisplayText(range.AddValue, previousValue, normalizedValue);
                }

                RaisePropertyChanged(nameof(FloatValueModeHelpText));
                RaisePropertyChanged(nameof(SupporterFloatAddSummary));
                RaisePropertyChanged(nameof(TriggerSummary));
            }
        }
    }

    public double FloatTransitionInSeconds
    {
        get => floatTransitionInSeconds;
        set
        {
            var normalizedValue = Math.Clamp(value, 0, 30);
            if (SetProperty(ref floatTransitionInSeconds, normalizedValue))
            {
                RaisePropertyChanged(nameof(UsesFloatInTransition));
                RaisePropertyChanged(nameof(TriggerSummary));
            }
        }
    }

    public double FloatTransitionOutSeconds
    {
        get => floatTransitionOutSeconds;
        set
        {
            var normalizedValue = Math.Clamp(value, 0, 30);
            if (SetProperty(ref floatTransitionOutSeconds, normalizedValue))
            {
                RaisePropertyChanged(nameof(UsesFloatOutTransition));
                RaisePropertyChanged(nameof(TriggerSummary));
            }
        }
    }

    public FloatActionMode FloatActionMode
    {
        get => floatActionMode;
        set
        {
            var normalizedValue = Enum.IsDefined(value) ? value : FloatActionMode.Set;
            if (SetProperty(ref floatActionMode, normalizedValue))
            {
                RaiseFloatActionModeVisibilityProperties();
                RaisePropertyChanged(nameof(TriggerSummary));
            }
        }
    }

    public double FloatRangeMin
    {
        get => floatRangeMin;
        set
        {
            var clamped = Math.Clamp(value, 0.0, 1.0);
            if (clamped > floatRangeMax - 0.0001) clamped = floatRangeMax - 0.0001;
            if (clamped < 0) clamped = 0;
            if (SetProperty(ref floatRangeMin, clamped))
            {
                RaisePropertyChanged(nameof(TriggerSummary));
            }
        }
    }

    public double FloatRangeMax
    {
        get => floatRangeMax;
        set
        {
            var clamped = Math.Clamp(value, 0.0, 1.0);
            if (clamped < floatRangeMin + 0.0001) clamped = floatRangeMin + 0.0001;
            if (clamped > 1) clamped = 1;
            if (SetProperty(ref floatRangeMax, clamped))
            {
                RaisePropertyChanged(nameof(TriggerSummary));
            }
        }
    }

    public double FloatCycleStep
    {
        get => floatCycleStep;
        set
        {
            var normalizedValue = Math.Max(0.0, value);
            if (SetProperty(ref floatCycleStep, normalizedValue))
            {
                RaisePropertyChanged(nameof(TriggerSummary));
            }
        }
    }

    public double FloatAddAmount
    {
        get => floatAddAmount;
        set
        {
            var normalizedValue = Math.Max(0.0, value);
            if (SetProperty(ref floatAddAmount, normalizedValue))
            {
                RaisePropertyChanged(nameof(TriggerSummary));
            }
        }
    }

    public double FloatSubtractAmount
    {
        get => floatSubtractAmount;
        set
        {
            var normalizedValue = Math.Max(0.0, value);
            if (SetProperty(ref floatSubtractAmount, normalizedValue))
            {
                RaisePropertyChanged(nameof(TriggerSummary));
            }
        }
    }

    public double FloatAddSubtractAmount
    {
        get => floatAddSubtractAmount;
        set
        {
            if (SetProperty(ref floatAddSubtractAmount, value))
            {
                RaisePropertyChanged(nameof(TriggerSummary));
            }
        }
    }

    public double FloatMultiplyFactor
    {
        get => floatMultiplyFactor;
        set
        {
            if (SetProperty(ref floatMultiplyFactor, value))
            {
                RaisePropertyChanged(nameof(TriggerSummary));
            }
        }
    }

    public double FloatToggleOnValue
    {
        get => floatToggleOnValue;
        set
        {
            var normalizedValue = Math.Clamp(value, 0.0, 1.0);
            if (SetProperty(ref floatToggleOnValue, normalizedValue))
            {
                RaisePropertyChanged(nameof(TriggerSummary));
            }
        }
    }

    public double FloatToggleOffValue
    {
        get => floatToggleOffValue;
        set
        {
            var normalizedValue = Math.Clamp(value, 0.0, 1.0);
            if (SetProperty(ref floatToggleOffValue, normalizedValue))
            {
                RaisePropertyChanged(nameof(TriggerSummary));
            }
        }
    }

    public int FloatGlitchyIntervalMs
    {
        get => floatGlitchyIntervalMs;
        set
        {
            var normalizedValue = Math.Max(1, value);
            if (SetProperty(ref floatGlitchyIntervalMs, normalizedValue))
            {
                RaisePropertyChanged(nameof(TriggerSummary));
            }
        }
    }

    public double FloatPulseSeconds
    {
        get => floatPulseSeconds;
        set
        {
            var normalizedValue = Math.Max(0.0, value);
            if (SetProperty(ref floatPulseSeconds, normalizedValue))
            {
                RaisePropertyChanged(nameof(TriggerSummary));
            }
        }
    }

    public FloatClampMode FloatClampMode
    {
        get => floatClampMode;
        set
        {
            var normalizedValue = Enum.IsDefined(value) ? value : FloatClampMode.ZeroToOne;
            if (SetProperty(ref floatClampMode, normalizedValue))
            {
                RaisePropertyChanged(nameof(TriggerSummary));
            }
        }
    }

    public bool HideRewardWhenFloatMaxReached
    {
        get => hideRewardWhenFloatMaxReached;
        set => SetProperty(ref hideRewardWhenFloatMaxReached, value);
    }

    public bool HideRewardWhenFloatMinReached
    {
        get => hideRewardWhenFloatMinReached;
        set => SetProperty(ref hideRewardWhenFloatMinReached, value);
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

    [JsonIgnore]
    public string AvatarChangeDisplayName => !string.IsNullOrWhiteSpace(AvatarTargetName)
        ? AvatarTargetName
        : !string.IsNullOrWhiteSpace(AvatarChangeTargetId) ? AvatarChangeTargetId : "(Not set)";

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

    public string? PowerUpId { get; set; }
    public string? CashPaymentRuleId { get; set; }
    public bool IsGiftSubscription { get; set; }
    public bool PermanentAvatarChange { get; set; }
    public bool CooldownOnlyAvatarChange { get; set; }

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

    public double DurationSeconds
    {
        get => durationSeconds;
        set
        {
            var normalizedValue = ActionType switch
            {
                OscActionType.PlayerMovement => Math.Max(1d, value),
                OscActionType.SetTrigger => value <= 0 ? 3d : value,
                _ => value
            };
            if (SetProperty(ref durationSeconds, normalizedValue))
            {
                RaiseActionVisibilityProperties();
                RaisePropertyChanged(nameof(UsesFloatTimedValues));
                RaisePropertyChanged(nameof(UsesFloatInTransition));
                RaisePropertyChanged(nameof(UsesFloatOutTransition));
                RaisePropertyChanged(nameof(UsesActiveFloatBoostReward));
                RaisePropertyChanged(nameof(ActiveFloatBoostRewardStatusText));
                RaisePropertyChanged(nameof(UsesSupporterFloatAdd));
                RaisePropertyChanged(nameof(UsesActiveSupporterFloatAdd));
                RaisePropertyChanged(nameof(SupporterFloatAddSummary));
                RaisePropertyChanged(nameof(SupporterTimeSettingsSummary));
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

    public string SupporterKeywordText
    {
        get => supporterKeywordText;
        set
        {
            var normalizedValue = string.IsNullOrWhiteSpace(value) ? string.Empty : value.Trim();
            BitsKeywordEnabled = !string.IsNullOrEmpty(normalizedValue);
            if (SetProperty(ref supporterKeywordText, normalizedValue))
            {
                RaisePropertyChanged(nameof(UsesForceMovementBitsTrigger));
                RaisePropertyChanged(nameof(TriggerSummary));
            }
        }
    }

    public bool BitsKeywordEnabled
    {
        get => bitsKeywordEnabled;
        set
        {
            if (SetProperty(ref bitsKeywordEnabled, value))
            {
                RaisePropertyChanged(nameof(UsesBitsKeyword));
                RaisePropertyChanged(nameof(TriggerSummary));
            }
        }
    }

    [JsonIgnore]
    public bool UsesBitsKeyword
        => BitsKeywordEnabled && !string.IsNullOrWhiteSpace(SupporterKeywordText);

    public Guid ActiveFloatBoostRewardOwnerId
    {
        get => activeFloatBoostRewardOwnerId;
        set => SetProperty(ref activeFloatBoostRewardOwnerId, value == Guid.Empty ? Guid.NewGuid() : value);
    }

    public bool ActiveFloatBoostRewardEnabled
    {
        get => activeFloatBoostRewardEnabled;
        set
        {
            if (SetProperty(ref activeFloatBoostRewardEnabled, value))
            {
                RaisePropertyChanged(nameof(UsesActiveFloatBoostReward));
                RaisePropertyChanged(nameof(ActiveFloatBoostRewardStatusText));
                RaisePropertyChanged(nameof(TriggerSummary));
            }
        }
    }

    public string ActiveFloatBoostRewardId
    {
        get => activeFloatBoostRewardId;
        set => SetProperty(ref activeFloatBoostRewardId, value ?? string.Empty);
    }

    public string ActiveFloatBoostRewardTitle
    {
        get => activeFloatBoostRewardTitle;
        set
        {
            if (SetProperty(ref activeFloatBoostRewardTitle, value ?? string.Empty))
            {
                RaisePropertyChanged(nameof(ActiveFloatBoostRewardStatusText));
            }
        }
    }

    public string ActiveFloatBoostRewardDescription
    {
        get => activeFloatBoostRewardDescription;
        set => SetProperty(ref activeFloatBoostRewardDescription, value ?? string.Empty);
    }

    public int ActiveFloatBoostRewardCost
    {
        get => activeFloatBoostRewardCost;
        set => SetProperty(ref activeFloatBoostRewardCost, Math.Max(1, value));
    }

    public int ActiveFloatBoostRewardCooldownSeconds
    {
        get => activeFloatBoostRewardCooldownSeconds;
        set => SetProperty(ref activeFloatBoostRewardCooldownSeconds, Math.Max(0, value));
    }

    public string ActiveFloatBoostRewardReadyColor
    {
        get => activeFloatBoostRewardReadyColor;
        set
        {
            var normalizedValue = ManagedRewardPresentation.NormalizeReadyBackgroundColor(value);
            if (SetProperty(ref activeFloatBoostRewardReadyColor, normalizedValue))
            {
                RaisePropertyChanged(nameof(ActiveFloatBoostRewardReadyColorBrush));
            }
        }
    }

    public string ActiveFloatBoostRewardCooldownColor
    {
        get => activeFloatBoostRewardCooldownColor;
        set
        {
            var normalizedValue = ManagedRewardPresentation.NormalizeCooldownBackgroundColor(value);
            if (SetProperty(ref activeFloatBoostRewardCooldownColor, normalizedValue))
            {
                RaisePropertyChanged(nameof(ActiveFloatBoostRewardCooldownColorBrush));
            }
        }
    }

    public string ActiveFloatBoostAddValue
    {
        get => activeFloatBoostAddValue;
        set
        {
            if (SetProperty(ref activeFloatBoostAddValue, value ?? string.Empty))
            {
                RaisePropertyChanged(nameof(ActiveFloatBoostRewardStatusText));
            }
        }
    }

    public string ActiveFloatBoostMinimumValue
    {
        get => activeFloatBoostMinimumValue;
        set => SetProperty(ref activeFloatBoostMinimumValue, value ?? string.Empty);
    }

    public string ActiveFloatBoostMaximumValue
    {
        get => activeFloatBoostMaximumValue;
        set => SetProperty(ref activeFloatBoostMaximumValue, value ?? string.Empty);
    }

    public bool SupporterFloatAddEnabled
    {
        get => supporterFloatAddEnabled;
        set
        {
            if (SetProperty(ref supporterFloatAddEnabled, value))
            {
                RaisePropertyChanged(nameof(UsesActiveSupporterFloatAdd));
                RaisePropertyChanged(nameof(SupporterFloatAddSummary));
                RaisePropertyChanged(nameof(TriggerSummary));
            }
        }
    }

    public string SupporterFloatAddMinimumValue
    {
        get => supporterFloatAddMinimumValue;
        set
        {
            if (SetProperty(ref supporterFloatAddMinimumValue, value ?? string.Empty))
            {
                RaisePropertyChanged(nameof(SupporterFloatAddSummary));
                RaisePropertyChanged(nameof(TriggerSummary));
            }
        }
    }

    public string SupporterFloatAddMaximumValue
    {
        get => supporterFloatAddMaximumValue;
        set
        {
            if (SetProperty(ref supporterFloatAddMaximumValue, value ?? string.Empty))
            {
                RaisePropertyChanged(nameof(SupporterFloatAddSummary));
                RaisePropertyChanged(nameof(TriggerSummary));
            }
        }
    }

    public ObservableCollection<SupporterFloatAddRange> SupporterFloatAddRanges
    {
        get => supporterFloatAddRanges;
        set
        {
            var normalizedValue = value ?? [];
            if (ReferenceEquals(supporterFloatAddRanges, normalizedValue))
            {
                return;
            }

            supporterFloatAddRanges.CollectionChanged -= OnSupporterFloatAddRangesChanged;
            UnwireSupporterFloatAddRanges(supporterFloatAddRanges);
            supporterFloatAddRanges = normalizedValue;
            supporterFloatAddRanges.CollectionChanged += OnSupporterFloatAddRangesChanged;
            WireSupporterFloatAddRanges(supporterFloatAddRanges);
            RaiseSupporterFloatAddProperties();
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
                RaisePropertyChanged(nameof(SpecialRulePairingBadgeText));
            }
        }
    }

    public SetTriggerRestoreMode SetTriggerRestoreMode
    {
        get => setTriggerRestoreMode;
        set
        {
            var normalizedValue = Enum.IsDefined(value)
                ? value
                : SetTriggerRestoreMode.ConfiguredAndRelated;
            if (SetProperty(ref setTriggerRestoreMode, normalizedValue))
            {
                RaiseSetTriggerRestoreModeProperties();
            }
        }
    }

    [JsonIgnore]
    public bool SetTriggerAutoRestoreRelatedOutfitParameters
    {
        get => SetTriggerRestoreMode == SetTriggerRestoreMode.ConfiguredAndRelated;
        set => SetTriggerRestoreMode = value
            ? SetTriggerRestoreMode.ConfiguredAndRelated
            : SetTriggerRestoreMode.ConfiguredOnly;
    }

    public SpecialRulePairingMode SpecialRulePairingMode
    {
        get => specialRulePairingMode;
        set
        {
            var normalizedValue = Enum.IsDefined(value)
                ? value
                : SpecialRulePairingMode.HidePairedWhileActive;
            if (SetProperty(ref specialRulePairingMode, normalizedValue))
            {
                RaisePropertyChanged(nameof(HasSpecialRuleLockouts));
                RaisePropertyChanged(nameof(SpecialRulePairingBadgeText));
            }
        }
    }

    public bool UsesChannelPointReward => TriggerType == TwitchTriggerType.ChannelPoints;

    public bool UsesAmountThreshold => TriggerType is TwitchTriggerType.Bits or TwitchTriggerType.Subscriptions;

    public bool UsesAmountScaledDuration => UsesAmountThreshold && AmountScaledDurationEnabled;

    public bool UsesAddBitsToSwapTime => UsesAmountThreshold && AddBitsToSwapTime;

    public string SupporterTimeSettingsSummary
    {
        get
        {
            if (!UsesAmountScaledDuration)
            {
                return T("Amount scaling is off. This override uses fixed Active Time.");
            }

            var bitsText = TF(
                "Bits: {0}s per {1} bits",
                Math.Max(1, BitsSecondsPerAmountUnit),
                Math.Max(1, BitsAmountUnitsPerDuration));
            var subsText = TF(
                "Subs: T1 {0}s, T2 {1}s, T3 {2}s",
                Math.Max(1, SubscriptionTier1SecondsPerSub),
                Math.Max(1, SubscriptionTier2SecondsPerSub),
                Math.Max(1, SubscriptionTier3SecondsPerSub));
            var capText = MaxAccumulatedDurationEnabled
                ? TF("Cap: {0}s max", Math.Max(1, MaxAccumulatedDurationSeconds))
                : T("Cap: off");
            var startText = TF("Start: {0}s", Math.Max(0, DurationSeconds));
            return $"{startText} | {bitsText} | {subsText} | {capText}";
        }
    }

    public bool UsesBitsOutfitSetTrigger => TriggerType == TwitchTriggerType.Bits && UsesSetTrigger;

    public bool UsesForceMovementBitsTrigger => TriggerType == TwitchTriggerType.Bits && UsesPlayerMovement;

    public bool UsesSupporterAmountTimerSettings => UsesAmountThreshold && !UsesSetTrigger && !UsesForceMovementBitsTrigger;

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

    [JsonIgnore]
    public string SupporterAvatarDisplayName => !string.IsNullOrWhiteSpace(SupporterAvatarName)
        ? SupporterAvatarName
        : !string.IsNullOrWhiteSpace(SupporterAvatarId) ? SupporterAvatarId : "(Not set)";

    public bool UsesAvatarParameter => ActionType == OscActionType.AvatarParameter;

    public bool UsesAvatarChange => ActionType == OscActionType.AvatarChange;

    public bool UsesAvatarRoulet => ActionType == OscActionType.AvatarRoulet;

    public bool UsesPlayerMovement => ActionType == OscActionType.PlayerMovement;

    public bool UsesSetTrigger => ActionType == OscActionType.SetTrigger;

    public bool UsesCooldown => ActionType != OscActionType.PlayerMovement || UsesForceMovementBitsTrigger;

    public bool HasSpecialRuleLockouts => TemporarilyDisabledRuleIds.Count > 0;

    public string SpecialRulePairingBadgeText => SpecialRulePairingMode == SpecialRulePairingMode.ShowPairedWhileActive
        ? T("Reveal pairing set")
        : T("Disable pairing set");

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

            if (TriggerType == TwitchTriggerType.PowerUp)
            {
                return string.IsNullOrWhiteSpace(Name)
                    ? "New Power Up Action"
                    : Name.Trim();
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

    public bool UsesLegacySetTriggerFullDiffRestore => SetTriggerRestoreMode == SetTriggerRestoreMode.FullSafeDiff;

    public string SetTriggerRestoreModeSummary => SetTriggerRestoreMode switch
    {
        SetTriggerRestoreMode.FullSafeDiff => T("Legacy restore: restore the full safe changed outfit diff."),
        SetTriggerRestoreMode.ConfiguredAndRelated => T("Restore listed parameters first, then related outfit toggles learned from VRChat."),
        SetTriggerRestoreMode.ConfiguredOnly => T("Restore only the listed outfit parameters."),
        _ => T("Restore listed parameters first, then related outfit toggles learned from VRChat.")
    };

    public bool UsesTimedAction => DurationSeconds > 0;

    public bool UsesInstantAction => DurationSeconds <= 0;

    public bool UsesBoolParameter => UsesAvatarParameter && ParameterType == OscParameterType.Bool;

    public bool UsesIntParameter => UsesAvatarParameter && ParameterType == OscParameterType.Int;

    public bool UsesTextOrFloatParameter => UsesAvatarParameter && (ParameterType == OscParameterType.Float || ParameterType == OscParameterType.String);

    public bool UsesFloatParameter => UsesAvatarParameter && ParameterType == OscParameterType.Float;

    public bool UsesFloatTimedValues => UsesFloatParameter && UsesTimedAction;

    public bool UsesFloatInTransition => UsesFloatParameter && FloatTransitionInSeconds > 0;
    public bool UsesFloatOutTransition => UsesFloatParameter && FloatTransitionOutSeconds > 0;

    public bool UsesActiveFloatBoostReward => UsesFloatTimedValues && ActiveFloatBoostRewardEnabled;

    public bool UsesSupporterFloatAdd => UsesAmountThreshold
        && UsesFloatTimedValues
        && !UsesSetTrigger
        && !UsesForceMovementBitsTrigger;

    public bool UsesActiveSupporterFloatAdd => UsesSupporterFloatAdd && SupporterFloatAddEnabled;

    public bool HasSupporterFloatAddRanges => SupporterFloatAddRanges.Count > 0;

    public bool UsesBoolTimedValues => UsesBoolParameter && UsesTimedAction;

    public bool UsesBoolToggleHint => UsesBoolParameter && UsesInstantAction;

    public bool UsesIntInstantModeOptions => UsesIntParameter && UsesInstantAction;

    public bool UsesIntFixedInstantValue => UsesIntParameter && UsesInstantAction && IntZeroDurationMode == global::VrcTwitchOscBridge.Models.IntZeroDurationMode.Fixed;

    public bool UsesIntRangeInputs => UsesIntParameter && UsesInstantAction && IntZeroDurationMode != global::VrcTwitchOscBridge.Models.IntZeroDurationMode.Fixed;

    public bool UsesFloatActionMode => UsesAvatarParameter && ParameterType == OscParameterType.Float;

    public bool UsesFloatSetMode => UsesFloatActionMode && FloatActionMode == FloatActionMode.Set;
    public bool UsesFloatRandomMode => UsesFloatActionMode && FloatActionMode == FloatActionMode.Random;
    public bool UsesFloatAddMode => UsesFloatActionMode && FloatActionMode == FloatActionMode.Add;
    public bool UsesFloatSubtractMode => UsesFloatActionMode && FloatActionMode == FloatActionMode.Subtract;
    public bool UsesFloatAddSubtractMode => UsesFloatActionMode && FloatActionMode == FloatActionMode.AddSubtract;
    public bool UsesFloatMultiplyMode => UsesFloatActionMode && FloatActionMode == FloatActionMode.Multiply;
    public bool UsesFloatToggleMode => UsesFloatActionMode && FloatActionMode == FloatActionMode.Toggle;
    public bool UsesFloatCycleMode => UsesFloatActionMode && FloatActionMode == FloatActionMode.Cycle;
    public bool UsesFloatGlitchyMode => UsesFloatActionMode && FloatActionMode == FloatActionMode.Glitchy;
    public bool UsesFloatPulseMode => UsesFloatActionMode && FloatActionMode == FloatActionMode.Pulse;

    public bool UsesFloatRangeInputs => UsesFloatActionMode &&
        (FloatActionMode == FloatActionMode.Random
         || FloatActionMode == FloatActionMode.Cycle
         || FloatActionMode == FloatActionMode.Glitchy);

    public bool UsesFloatCycleStep => UsesFloatActionMode && FloatActionMode == FloatActionMode.Cycle;

    public bool UsesFloatToggleValues => UsesFloatActionMode && FloatActionMode == FloatActionMode.Toggle;

    public bool UsesFloatGlitchyInterval => UsesFloatActionMode && FloatActionMode == FloatActionMode.Glitchy;

    public bool UsesFloatPulseSeconds => UsesFloatActionMode && FloatActionMode == FloatActionMode.Pulse;

    public bool UsesFloatClampMode => UsesFloatActionMode &&
        (FloatActionMode == FloatActionMode.Add
         || FloatActionMode == FloatActionMode.Subtract
         || FloatActionMode == FloatActionMode.AddSubtract
         || FloatActionMode == FloatActionMode.Multiply);

    public bool UsesFloatHideOnLimit => UsesFloatActionMode
        && RewardSyncMode == TwitchRewardSyncMode.CreateOrManage
        && DurationSeconds > 0
        && (FloatActionMode == FloatActionMode.Add
            || FloatActionMode == FloatActionMode.Subtract
            || FloatActionMode == FloatActionMode.AddSubtract
            || FloatActionMode == FloatActionMode.Multiply);

    public bool UsesIntTimedValues => UsesIntParameter && UsesTimedAction;

    public bool UsesDirectInstantValue => UsesTextOrFloatParameter && UsesInstantAction;

    public bool UsesDirectTimedValues => UsesTextOrFloatParameter && UsesTimedAction;

    public bool UsesAvatarChangeTimedReset => UsesAvatarChange && UsesTimedAction;

    public string FloatValueModeHelpText => FloatValueMode == global::VrcTwitchOscBridge.Models.FloatValueMode.Percent
        ? T("Percent mode accepts 0 to 100 and sends the converted 0.00 to 1.00 OSC float value.")
        : T("Decimal mode sends the value directly as a 0.00 to 1.00 OSC float.");

    public string ActiveFloatBoostRewardStatusText
    {
        get
        {
            if (!UsesActiveFloatBoostReward)
            {
                return UsesLinkedExistingReward
                    ? T("Active boost rewards need a Crystal Relay-managed parent reward.")
                    : T("Enable this only on timed float Avatar Parameter redeems.");
            }

            if (UsesLinkedExistingReward)
            {
                return T("Active boost reward is configured, but parent hide/show requires a Crystal Relay-managed parent reward.");
            }

            var title = string.IsNullOrWhiteSpace(ActiveFloatBoostRewardTitle)
                ? T("Active Boost Reward")
                : ActiveFloatBoostRewardTitle.Trim();
            return TF("{0} adds {1} while this redeem is active.", title, ActiveFloatBoostAddValue);
        }
    }

    public string SupporterFloatAddSummary
    {
        get
        {
            if (!UsesActiveSupporterFloatAdd)
            {
                return T("Bits/Subs add is off.");
            }

            var range = SupporterFloatAddRanges.FirstOrDefault(IsSupporterFloatAddRangeConfigured);
            if (range is null)
            {
                return T("Add one Bits/Subs rule.");
            }

            return FormatSupporterFloatAddRangeSummary(range);
        }
    }

    public Brush ActiveFloatBoostRewardReadyColorBrush => CreateColorBrush(ActiveFloatBoostRewardReadyColor);

    public Brush ActiveFloatBoostRewardCooldownColorBrush => CreateColorBrush(ActiveFloatBoostRewardCooldownColor);

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

    public string DurationHelpText => UsesForceMovementBitsTrigger
        ? T("Use whole seconds. Crystal Relay holds the selected VRChat movement input for the full Active Time after a matching Bits cheer word, then releases it automatically.")
        : UsesPlayerMovement
        ? T("Use whole seconds. Crystal Relay holds the selected VRChat movement input for the full Active Time, then releases it automatically. Movement redeems do not use 0-second instant mode.")
        : UsesAvatarRoulet
            ? T("Use whole seconds. Avatar Roulette is always a timed temporary switch, so 0 is not used here. Crystal Relay stays on the rolled avatar for this long, then returns to the shared return avatar.")
            : UsesAmountScaledDuration
                ? T("Active time and amount both add on top. Each matching trigger extends the current timer by Active Time + the scaled amount.")
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
                TwitchTriggerType.Bits when UsesForceMovementBitsTrigger => string.IsNullOrWhiteSpace(SupporterKeywordText)
                    ? TF("Bits >= {0} + movement word needed", Math.Max(1, MinimumAmount))
                    : TF("Bits >= {0} + Word: {1}", Math.Max(1, MinimumAmount), SupporterKeywordText.Trim()),
                TwitchTriggerType.Bits when UsesActiveSupporterFloatAdd => SupporterFloatAddSummary,
                TwitchTriggerType.Bits => (AmountScaledDurationEnabled || AddBitsToSwapTime)
                    ? TF("Bits >= {0} ({1}s per {2} bits)", Math.Max(1, MinimumAmount), Math.Max(1, BitsSecondsPerAmountUnit), Math.Max(1, BitsAmountUnitsPerDuration))
                    : TF("Bits >= {0}", Math.Max(1, MinimumAmount)),
                TwitchTriggerType.Subscriptions when UsesActiveSupporterFloatAdd => SupporterFloatAddSummary,
                TwitchTriggerType.Subscriptions => (AmountScaledDurationEnabled || AddBitsToSwapTime)
                    ? TF("Subs >= {0} (T1 {1}s, T2 {2}s, T3 {3}s)", Math.Max(1, MinimumAmount), Math.Max(1, SubscriptionTier1SecondsPerSub), Math.Max(1, SubscriptionTier2SecondsPerSub), Math.Max(1, SubscriptionTier3SecondsPerSub))
                    : TF("Subs >= {0}", Math.Max(1, MinimumAmount)),
                TwitchTriggerType.PowerUp => T("Power Up"),
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
                OscActionType.AvatarParameter when UsesActiveSupporterFloatAdd => TF("Add to {0}", ParameterName),
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
        RaisePropertyChanged(nameof(UsesForceMovementBitsTrigger));
        RaisePropertyChanged(nameof(UsesBoolParameter));
        RaisePropertyChanged(nameof(UsesIntParameter));
        RaisePropertyChanged(nameof(UsesTextOrFloatParameter));
        RaisePropertyChanged(nameof(UsesFloatParameter));
        RaisePropertyChanged(nameof(UsesFloatTimedValues));
        RaisePropertyChanged(nameof(UsesFloatInTransition));
        RaisePropertyChanged(nameof(UsesFloatOutTransition));
        RaisePropertyChanged(nameof(UsesActiveFloatBoostReward));
        RaisePropertyChanged(nameof(UsesSupporterFloatAdd));
        RaisePropertyChanged(nameof(UsesActiveSupporterFloatAdd));
        RaiseFloatActionModeVisibilityProperties();
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
        RaisePropertyChanged(nameof(FloatValueModeHelpText));
        RaisePropertyChanged(nameof(ActiveFloatBoostRewardStatusText));
        RaisePropertyChanged(nameof(SupporterFloatAddSummary));
        RaisePropertyChanged(nameof(HasSetTriggerActions));
        RaisePropertyChanged(nameof(SetTriggerActionCount));
        RaisePropertyChanged(nameof(SetTriggerSummary));
        RaiseSetTriggerRestoreModeProperties();
        RaisePropertyChanged(nameof(DurationHelpText));
        RaisePropertyChanged(nameof(IntModeHelpText));
    }

    private void RaiseFloatActionModeVisibilityProperties()
    {
        RaisePropertyChanged(nameof(UsesFloatActionMode));
        RaisePropertyChanged(nameof(UsesFloatSetMode));
        RaisePropertyChanged(nameof(UsesFloatRandomMode));
        RaisePropertyChanged(nameof(UsesFloatAddMode));
        RaisePropertyChanged(nameof(UsesFloatSubtractMode));
        RaisePropertyChanged(nameof(UsesFloatAddSubtractMode));
        RaisePropertyChanged(nameof(UsesFloatMultiplyMode));
        RaisePropertyChanged(nameof(UsesFloatToggleMode));
        RaisePropertyChanged(nameof(UsesFloatCycleMode));
        RaisePropertyChanged(nameof(UsesFloatGlitchyMode));
        RaisePropertyChanged(nameof(UsesFloatPulseMode));
        RaisePropertyChanged(nameof(UsesFloatRangeInputs));
        RaisePropertyChanged(nameof(UsesFloatCycleStep));
        RaisePropertyChanged(nameof(UsesFloatToggleValues));
        RaisePropertyChanged(nameof(UsesFloatGlitchyInterval));
        RaisePropertyChanged(nameof(UsesFloatPulseSeconds));
        RaisePropertyChanged(nameof(UsesFloatClampMode));
        RaisePropertyChanged(nameof(UsesFloatHideOnLimit));
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
        PlayerMovementDirection.RandomMovement => T("Random Movement"),
        PlayerMovementDirection.GlitchyMovement => T("Glitchy Movement"),
        _ => T("Move")
    };

    private static string DescribeDuration(double seconds) => $"{Math.Max(1d, seconds):0.##}s";

    private static bool IsSupporterFloatAddRangeConfigured(SupporterFloatAddRange range) =>
        !string.IsNullOrWhiteSpace(range.AddValue);

    private string FormatSupporterFloatAddRangeSummary(SupporterFloatAddRange range)
    {
        var triggerLabel = TriggerType == TwitchTriggerType.Bits ? "Bits" : "Subs";
        var minimumAmount = Math.Max(1, range.MinimumAmount);
        var maximumAmount = Math.Max(0, range.MaximumAmount);
        var addValue = FormatFloatDisplayValue(range.AddValue);
        var maximumValue = FormatFloatDisplayValue(SupporterFloatAddMaximumValue);
        return maximumAmount == 0
            ? TF("{0} {1}+: +{2} (max {3})", triggerLabel, minimumAmount, addValue, maximumValue)
            : TF("{0} {1}-{2}: +{3} (max {4})", triggerLabel, minimumAmount, maximumAmount, addValue, maximumValue);
    }

    private string FormatFloatDisplayValue(string value)
    {
        var normalizedValue = value?.Trim() ?? string.Empty;
        return FloatValueMode == global::VrcTwitchOscBridge.Models.FloatValueMode.Percent
            && !string.IsNullOrWhiteSpace(normalizedValue)
            && !normalizedValue.EndsWith('%')
                ? $"{normalizedValue}%"
                : normalizedValue;
    }

    private void OnTemporarilyDisabledRuleIdsChanged(object? sender, NotifyCollectionChangedEventArgs e)
    {
        RaisePropertyChanged(nameof(TemporarilyDisabledRuleIds));
        RaisePropertyChanged(nameof(HasSpecialRuleLockouts));
        RaisePropertyChanged(nameof(SpecialRulePairingBadgeText));
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

    private void OnSupporterFloatAddRangesChanged(object? sender, NotifyCollectionChangedEventArgs e)
    {
        if (e.OldItems is not null)
        {
            foreach (SupporterFloatAddRange range in e.OldItems)
            {
                range.PropertyChanged -= OnSupporterFloatAddRangePropertyChanged;
            }
        }

        if (e.NewItems is not null)
        {
            foreach (SupporterFloatAddRange range in e.NewItems)
            {
                range.PropertyChanged += OnSupporterFloatAddRangePropertyChanged;
            }
        }

        RaiseSupporterFloatAddProperties();
    }

    private void OnSupporterFloatAddRangePropertyChanged(object? sender, PropertyChangedEventArgs e)
    {
        RaiseSupporterFloatAddProperties();
    }

    private void WireSupporterFloatAddRanges(IEnumerable<SupporterFloatAddRange> ranges)
    {
        foreach (var range in ranges)
        {
            range.PropertyChanged += OnSupporterFloatAddRangePropertyChanged;
        }
    }

    private void UnwireSupporterFloatAddRanges(IEnumerable<SupporterFloatAddRange> ranges)
    {
        foreach (var range in ranges)
        {
            range.PropertyChanged -= OnSupporterFloatAddRangePropertyChanged;
        }
    }

    private void RaiseSupporterFloatAddProperties()
    {
        RaisePropertyChanged(nameof(SupporterFloatAddRanges));
        RaisePropertyChanged(nameof(HasSupporterFloatAddRanges));
        RaisePropertyChanged(nameof(SupporterFloatAddSummary));
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

    private void RaiseSetTriggerRestoreModeProperties()
    {
        RaisePropertyChanged(nameof(SetTriggerRestoreMode));
        RaisePropertyChanged(nameof(SetTriggerAutoRestoreRelatedOutfitParameters));
        RaisePropertyChanged(nameof(UsesLegacySetTriggerFullDiffRestore));
        RaisePropertyChanged(nameof(SetTriggerRestoreModeSummary));
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
