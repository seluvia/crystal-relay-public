using System.Collections.ObjectModel;
using System.Collections.Specialized;
using System.Windows.Media;
using VrcTwitchOscBridge.Infrastructure;
using VrcTwitchOscBridge.Services;
using Brush = System.Windows.Media.Brush;

namespace VrcTwitchOscBridge.Models;

public enum AvatarScaleTriggerType
{
    ChannelPointReward,
    ChatCommand,
    Bits,
    Subscription,
    GiftSubscription,
    Follow,
    SupporterGrowth
}

public enum AvatarScaleMode
{
    SetHeight,
    RandomHeight,
    RelativeHeight,
    Multiplier,
    Preset,
    GlitchyRandomHeight
}

public enum AvatarScalePreset
{
    Tiny,
    Small,
    Normal,
    Tall,
    Giant
}

public enum AvatarScaleRestoreMode
{
    None,
    PreviousHeight,
    ConfiguredHeight
}

public enum AvatarScaleMultiplierDirection
{
    Grow,
    Divide
}

public sealed class AvatarScaleBitGrowthRange : ObservableObject
{
    private int minimumBits = 1;
    private int maximumBits = 99;
    private double heightAddedMeters = 0.05;

    public int MinimumBits
    {
        get => minimumBits;
        set => SetProperty(ref minimumBits, Math.Max(1, value));
    }

    public int MaximumBits
    {
        get => maximumBits;
        set => SetProperty(ref maximumBits, Math.Max(0, value));
    }

    public double HeightAddedMeters
    {
        get => heightAddedMeters;
        set => SetProperty(ref heightAddedMeters, Math.Max(0, value));
    }
}

public sealed class AvatarScaleMasterRewardSettings : ObservableObject
{
    private bool isEnabled;
    private string rewardId = string.Empty;
    private string rewardTitle = "Avatar Scaling";
    private string rewardDescription = string.Empty;
    private int rewardCost = 100;
    private TwitchRewardSyncMode rewardSyncMode = TwitchRewardSyncMode.CreateOrManage;
    private int unlockDurationSeconds = 60;
    private int cooldownSeconds = 30;
    private string managedRewardReadyColor = ManagedRewardPresentation.ReadyBackgroundColor;
    private string managedRewardCooldownColor = ManagedRewardPresentation.InUseBackgroundColor;
    private bool deleteMasterRewardWhenInactive;
    private bool freeChildRewardSlotsWhenLocked = true;
    private bool preventAvatarChangesDuringActiveScaling;

    public bool IsEnabled
    {
        get => isEnabled;
        set => SetProperty(ref isEnabled, value);
    }

    public string RewardId
    {
        get => rewardId;
        set => SetProperty(ref rewardId, value?.Trim() ?? string.Empty);
    }

    public string RewardTitle
    {
        get => rewardTitle;
        set => SetProperty(ref rewardTitle, value ?? string.Empty);
    }

    public string RewardDescription
    {
        get => rewardDescription;
        set => SetProperty(ref rewardDescription, value ?? string.Empty);
    }

    public int RewardCost
    {
        get => rewardCost;
        set => SetProperty(ref rewardCost, Math.Max(1, value));
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
            }
        }
    }

    public bool UsesCreateOrManageReward => RewardSyncMode == TwitchRewardSyncMode.CreateOrManage;

    public bool UsesLinkedExistingReward => RewardSyncMode == TwitchRewardSyncMode.LinkExisting;

    public int UnlockDurationSeconds
    {
        get => unlockDurationSeconds;
        set => SetProperty(ref unlockDurationSeconds, Math.Max(1, value));
    }

    public int CooldownSeconds
    {
        get => cooldownSeconds;
        set => SetProperty(ref cooldownSeconds, Math.Max(0, value));
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

    public bool DeleteMasterRewardWhenInactive
    {
        get => deleteMasterRewardWhenInactive;
        set => SetProperty(ref deleteMasterRewardWhenInactive, value);
    }

    public bool FreeChildRewardSlotsWhenLocked
    {
        get => freeChildRewardSlotsWhenLocked;
        set => SetProperty(ref freeChildRewardSlotsWhenLocked, value);
    }

    public bool PreventAvatarChangesDuringActiveScaling
    {
        get => preventAvatarChangesDuringActiveScaling;
        set => SetProperty(ref preventAvatarChangesDuringActiveScaling, value);
    }

    public Brush ManagedRewardReadyColorBrush => CreateColorBrush(ManagedRewardReadyColor);

    public Brush ManagedRewardCooldownColorBrush => CreateColorBrush(ManagedRewardCooldownColor);

    private static Brush CreateColorBrush(string colorText)
    {
        try
        {
            var color = (Color)ColorConverter.ConvertFromString(colorText);
            var brush = new SolidColorBrush(color);
            brush.Freeze();
            return brush;
        }
        catch
        {
            var fallback = new SolidColorBrush(Colors.Transparent);
            fallback.Freeze();
            return fallback;
        }
    }
}

public sealed class AvatarScaleSet : ObservableObject
{
    private Guid id = Guid.NewGuid();
    private string name = "Default Scale Set";
    private ObservableCollection<AvatarScaleRule> scaleRules = [];

    public AvatarScaleSet()
    {
        scaleRules.CollectionChanged += OnScaleRulesChanged;
    }

    public Guid Id
    {
        get => id;
        set => SetProperty(ref id, value == Guid.Empty ? Guid.NewGuid() : value);
    }

    public string Name
    {
        get => name;
        set
        {
            if (SetProperty(ref name, value ?? string.Empty))
            {
                RaisePropertyChanged(nameof(DisplayTitle));
            }
        }
    }

    public ObservableCollection<AvatarScaleRule> ScaleRules
    {
        get => scaleRules;
        set
        {
            if (ReferenceEquals(scaleRules, value))
            {
                return;
            }

            scaleRules.CollectionChanged -= OnScaleRulesChanged;
            if (SetProperty(ref scaleRules, value ?? []))
            {
                scaleRules.CollectionChanged += OnScaleRulesChanged;
                RaisePropertyChanged(nameof(ScaleCountText));
            }
        }
    }

    public string DisplayTitle => string.IsNullOrWhiteSpace(Name) ? "Default Scale Set" : Name;

    public string ScaleCountText => ScaleRules.Count == 1
        ? "1 scale redeem"
        : $"{ScaleRules.Count} scale redeems";

    private void OnScaleRulesChanged(object? sender, NotifyCollectionChangedEventArgs e)
    {
        RaisePropertyChanged(nameof(ScaleCountText));
    }
}

public sealed class AvatarScaleRule : ObservableObject
{
    public const double SafeMinimumHeightMeters = 0.1;
    public const double SafeMaximumHeightMeters = 100;
    public const double AdvancedMinimumHeightMeters = 0.01;
    public const double AdvancedMaximumHeightMeters = 10_000;

    private Guid id = Guid.NewGuid();
    private bool isEnabled = true;
    private string name = "New Avatar Scale";
    private AvatarScaleTriggerType triggerType = AvatarScaleTriggerType.ChannelPointReward;
    private bool chatCommandEnabled;
    private string commandText = string.Empty;
    private ChatCommandPermission chatCommandPermission = ChatCommandPermission.Moderators;
    private string rewardId = string.Empty;
    private string rewardTitle = string.Empty;
    private string rewardDescription = string.Empty;
    private int rewardCost = 100;
    private TwitchRewardSyncMode rewardSyncMode = TwitchRewardSyncMode.CreateOrManage;
    private string managedRewardReadyColor = ManagedRewardPresentation.ReadyBackgroundColor;
    private string managedRewardCooldownColor = ManagedRewardPresentation.InUseBackgroundColor;
    private bool deleteManagedRewardWhenInactive;
    private int minimumBits = 1;
    private int maximumBits = 1_000_000;
    private string subscriptionTier = string.Empty;
    private int minimumMonths = -1;
    private int maximumMonths = -1;
    private int cooldownSeconds;
    private ObservableCollection<Guid> temporarilyDisabledScaleRuleIds = [];
    private AvatarScaleMode scaleMode = AvatarScaleMode.SetHeight;
    private double targetHeightMeters = 1.6;
    private double minimumHeightMeters = 0.5;
    private double maximumHeightMeters = 2.5;
    private double relativeHeightMeters = 0.25;
    private double relativeMinimumHeightMeters = SafeMinimumHeightMeters;
    private double relativeMaximumHeightMeters = SafeMaximumHeightMeters;
    private bool hideRewardWhenMinimumHeightReached = true;
    private bool hideRewardWhenMaximumHeightReached = true;
    private double heightMultiplier = 1.25;
    private AvatarScalePreset preset = AvatarScalePreset.Normal;
    private double activeTimeSeconds;
    private AvatarScaleRestoreMode restoreMode = AvatarScaleRestoreMode.ConfiguredHeight;
    private double restoreHeightMeters = 1.6;
    private int multiplierDirectionId;
    private double smoothTransitionSeconds;
    private double glitchyTransitionSeconds = 0.4;
    private bool advancedRangeEnabled;
    private bool bypassVrChatScaleLimits;
    private double supporterGrowthNormalHeightMeters = 1.6;
    private double supporterGrowthMaxAddedHeightMeters;
    private int supporterGrowthInactivityTimerSeconds = 60;
    private bool supporterGrowthAllowRewardScaleOverlay = true;
    private int supporterGrowthBitsTimerUnit = 100;
    private int supporterGrowthSecondsPerBitsUnit = 30;
    private int supporterGrowthTier1Seconds = 300;
    private int supporterGrowthTier2Seconds = 600;
    private int supporterGrowthTier3Seconds = 1500;
    private int supporterGrowthSoftCapSeconds = 1800;
    private int supporterGrowthSoftCapMultiplierPercent = 50;
    private int supporterGrowthMaxPaidTimeSeconds = 3600;
    private string supporterGrowthGrowKeyword = "grow";
    private string supporterGrowthShrinkKeyword = "shrink";
    private double supporterGrowthTier1HeightMeters = 0.10;
    private double supporterGrowthTier2HeightMeters = 0.20;
    private double supporterGrowthTier3HeightMeters = 0.30;
    private ObservableCollection<AvatarScaleBitGrowthRange> supporterGrowthBitRanges =
    [
        new AvatarScaleBitGrowthRange()
    ];

    public AvatarScaleRule()
    {
        temporarilyDisabledScaleRuleIds.CollectionChanged += OnTemporarilyDisabledScaleRuleIdsChanged;
        supporterGrowthBitRanges.CollectionChanged += OnSupporterGrowthBitRangesChanged;
        WireSupporterGrowthBitRanges(supporterGrowthBitRanges);
    }

    public Guid Id
    {
        get => id;
        set => SetProperty(ref id, value == Guid.Empty ? Guid.NewGuid() : value);
    }

    public bool IsEnabled
    {
        get => isEnabled;
        set => SetAndRaiseSummary(ref isEnabled, value);
    }

    public string Name
    {
        get => name;
        set => SetAndRaiseSummary(ref name, value ?? string.Empty);
    }

    public AvatarScaleTriggerType TriggerType
    {
        get => triggerType;
        set
        {
            var normalizedValue = Enum.IsDefined(value) ? value : AvatarScaleTriggerType.ChannelPointReward;
            if (SetProperty(ref triggerType, normalizedValue))
            {
                RaiseTriggerProperties();
            }
        }
    }

    public bool ChatCommandEnabled
    {
        get => chatCommandEnabled;
        set => SetAndRaiseSummary(ref chatCommandEnabled, value);
    }

    public string CommandText
    {
        get => commandText;
        set => SetAndRaiseSummary(ref commandText, ChatCommandUtility.Normalize(value));
    }

    public ChatCommandPermission ChatCommandPermission
    {
        get => chatCommandPermission;
        set => SetProperty(ref chatCommandPermission, Enum.IsDefined(value) ? value : ChatCommandPermission.Moderators);
    }

    public string RewardId
    {
        get => rewardId;
        set => SetAndRaiseSummary(ref rewardId, value?.Trim() ?? string.Empty);
    }

    public string RewardTitle
    {
        get => rewardTitle;
        set => SetAndRaiseSummary(ref rewardTitle, value?.Trim() ?? string.Empty);
    }

    public string RewardDescription
    {
        get => rewardDescription;
        set => SetProperty(ref rewardDescription, value ?? string.Empty);
    }

    public int RewardCost
    {
        get => rewardCost;
        set => SetAndRaiseSummary(ref rewardCost, Math.Max(1, value));
    }

    public TwitchRewardSyncMode RewardSyncMode
    {
        get => rewardSyncMode;
        set
        {
            var normalizedValue = Enum.IsDefined(value)
                ? value
                : TwitchRewardSyncMode.CreateOrManage;
            if (SetAndRaiseSummary(ref rewardSyncMode, normalizedValue))
            {
                RaisePropertyChanged(nameof(UsesCreateOrManageReward));
                RaisePropertyChanged(nameof(UsesLinkedExistingReward));
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

    public bool DeleteManagedRewardWhenInactive
    {
        get => deleteManagedRewardWhenInactive;
        set => SetAndRaiseSummary(ref deleteManagedRewardWhenInactive, value);
    }

    public int MinimumBits
    {
        get => minimumBits;
        set => SetAndRaiseSummary(ref minimumBits, Math.Max(1, value));
    }

    public int MaximumBits
    {
        get => maximumBits;
        set => SetAndRaiseSummary(ref maximumBits, Math.Max(1, value));
    }

    public string SubscriptionTier
    {
        get => subscriptionTier;
        set => SetAndRaiseSummary(ref subscriptionTier, value?.Trim() ?? string.Empty);
    }

    public int MinimumMonths
    {
        get => minimumMonths;
        set => SetAndRaiseSummary(ref minimumMonths, value);
    }

    public int MaximumMonths
    {
        get => maximumMonths;
        set => SetAndRaiseSummary(ref maximumMonths, value);
    }

    public int CooldownSeconds
    {
        get => cooldownSeconds;
        set => SetProperty(ref cooldownSeconds, Math.Max(0, value));
    }

    public ObservableCollection<Guid> TemporarilyDisabledScaleRuleIds
    {
        get => temporarilyDisabledScaleRuleIds;
        set
        {
            if (ReferenceEquals(temporarilyDisabledScaleRuleIds, value))
            {
                return;
            }

            temporarilyDisabledScaleRuleIds.CollectionChanged -= OnTemporarilyDisabledScaleRuleIdsChanged;
            if (SetProperty(ref temporarilyDisabledScaleRuleIds, value ?? []))
            {
                temporarilyDisabledScaleRuleIds.CollectionChanged += OnTemporarilyDisabledScaleRuleIdsChanged;
                RaisePropertyChanged(nameof(HasScaleDisablePairings));
            }
        }
    }

    public AvatarScaleMode ScaleMode
    {
        get => scaleMode;
        set
        {
            var normalizedValue = Enum.IsDefined(value) ? value : AvatarScaleMode.SetHeight;
            if (SetProperty(ref scaleMode, normalizedValue))
            {
                if (normalizedValue == AvatarScaleMode.GlitchyRandomHeight && ActiveTimeSeconds <= 0)
                {
                    ActiveTimeSeconds = 10;
                }

                RaiseScaleProperties();
            }
        }
    }

    public double TargetHeightMeters
    {
        get => targetHeightMeters;
        set => SetAndRaiseScale(ref targetHeightMeters, ClampHeight(value));
    }

    public double MinimumHeightMeters
    {
        get => minimumHeightMeters;
        set => SetAndRaiseScale(ref minimumHeightMeters, ClampHeight(value));
    }

    public double MaximumHeightMeters
    {
        get => maximumHeightMeters;
        set => SetAndRaiseScale(ref maximumHeightMeters, ClampHeight(value));
    }

    public double RelativeHeightMeters
    {
        get => relativeHeightMeters;
        set => SetAndRaiseScale(ref relativeHeightMeters, ClampRelativeHeight(value));
    }

    public double RelativeMinimumHeightMeters
    {
        get => relativeMinimumHeightMeters;
        set => SetAndRaiseScale(ref relativeMinimumHeightMeters, ClampHeight(value));
    }

    public double RelativeMaximumHeightMeters
    {
        get => relativeMaximumHeightMeters;
        set => SetAndRaiseScale(ref relativeMaximumHeightMeters, ClampHeight(value));
    }

    public bool HideRewardWhenMinimumHeightReached
    {
        get => hideRewardWhenMinimumHeightReached;
        set => SetProperty(ref hideRewardWhenMinimumHeightReached, value);
    }

    public bool HideRewardWhenMaximumHeightReached
    {
        get => hideRewardWhenMaximumHeightReached;
        set => SetProperty(ref hideRewardWhenMaximumHeightReached, value);
    }

    public double HeightMultiplier
    {
        get => heightMultiplier;
        set => SetAndRaiseScale(ref heightMultiplier, Math.Clamp(value, 0.01, AdvancedMaximumHeightMeters));
    }

    public AvatarScalePreset Preset
    {
        get => preset;
        set
        {
            var normalizedValue = Enum.IsDefined(value) ? value : AvatarScalePreset.Normal;
            if (SetProperty(ref preset, normalizedValue))
            {
                RaiseScaleProperties();
            }
        }
    }

    public double ActiveTimeSeconds
    {
        get => activeTimeSeconds;
        set
        {
            if (SetAndRaiseScale(ref activeTimeSeconds, Math.Max(0, value)))
            {
                RaisePropertyChanged(nameof(HasActiveTime));
            }
        }
    }

    public AvatarScaleRestoreMode RestoreMode
    {
        get => restoreMode;
        set
        {
            var normalizedValue = Enum.IsDefined(value) ? value : AvatarScaleRestoreMode.PreviousHeight;
            if (SetProperty(ref restoreMode, normalizedValue))
            {
                RaiseScaleProperties();
            }
        }
    }

    public double RestoreHeightMeters
    {
        get => restoreHeightMeters;
        set => SetAndRaiseScale(ref restoreHeightMeters, ClampHeight(value));
    }

    public int MultiplierDirectionId
    {
        get => multiplierDirectionId;
        set
        {
            var normalized = Enum.IsDefined((AvatarScaleMultiplierDirection)value)
                ? value
                : (int)AvatarScaleMultiplierDirection.Grow;
            if (SetAndRaiseScale(ref multiplierDirectionId, normalized))
            {
                RaisePropertyChanged(nameof(MultiplierDirection));
            }
        }
    }

    public AvatarScaleMultiplierDirection MultiplierDirection =>
        Enum.IsDefined((AvatarScaleMultiplierDirection)multiplierDirectionId)
            ? (AvatarScaleMultiplierDirection)multiplierDirectionId
            : AvatarScaleMultiplierDirection.Grow;

    public double SmoothTransitionSeconds
    {
        get => smoothTransitionSeconds;
        set => SetAndRaiseScale(ref smoothTransitionSeconds, Math.Clamp(value, 0, 30));
    }

    public double GlitchyTransitionSeconds
    {
        get => glitchyTransitionSeconds;
        set => SetAndRaiseScale(ref glitchyTransitionSeconds, Math.Clamp(value, 0, 5));
    }

    public bool AdvancedRangeEnabled
    {
        get => advancedRangeEnabled;
        set
        {
            if (!SetProperty(ref advancedRangeEnabled, value))
            {
                return;
            }

            TargetHeightMeters = ClampHeight(TargetHeightMeters);
            MinimumHeightMeters = ClampHeight(MinimumHeightMeters);
            MaximumHeightMeters = ClampHeight(MaximumHeightMeters);
            RestoreHeightMeters = ClampHeight(RestoreHeightMeters);
            RelativeHeightMeters = ClampRelativeHeight(RelativeHeightMeters);
            RelativeMinimumHeightMeters = ClampHeight(RelativeMinimumHeightMeters);
            RelativeMaximumHeightMeters = ClampHeight(RelativeMaximumHeightMeters);
            RaiseScaleProperties();
            RaisePropertyChanged(nameof(ScaleRangeHelpText));
        }
    }

    public bool BypassVrChatScaleLimits
    {
        get => bypassVrChatScaleLimits;
        set
        {
            if (SetProperty(ref bypassVrChatScaleLimits, value))
            {
                RaisePropertyChanged(nameof(ScaleRangeHelpText));
            }
        }
    }

    public double SupporterGrowthNormalHeightMeters
    {
        get => supporterGrowthNormalHeightMeters;
        set => SetAndRaiseSupporterGrowth(ref supporterGrowthNormalHeightMeters, ClampHeight(value));
    }

    public double SupporterGrowthMaxAddedHeightMeters
    {
        get => supporterGrowthMaxAddedHeightMeters;
        set => SetAndRaiseSupporterGrowth(ref supporterGrowthMaxAddedHeightMeters, Math.Max(0, ClampRelativeHeight(value)));
    }

    public int SupporterGrowthInactivityTimerSeconds
    {
        get => supporterGrowthInactivityTimerSeconds;
        set => SetAndRaiseSupporterGrowth(ref supporterGrowthInactivityTimerSeconds, Math.Max(1, value));
    }

    public bool SupporterGrowthAllowRewardScaleOverlay
    {
        get => supporterGrowthAllowRewardScaleOverlay;
        set => SetAndRaiseSupporterGrowth(ref supporterGrowthAllowRewardScaleOverlay, value);
    }

    public int SupporterGrowthBitsTimerUnit
    {
        get => supporterGrowthBitsTimerUnit;
        set => SetAndRaiseSupporterGrowth(ref supporterGrowthBitsTimerUnit, Math.Max(1, value));
    }

    public int SupporterGrowthSecondsPerBitsUnit
    {
        get => supporterGrowthSecondsPerBitsUnit;
        set => SetAndRaiseSupporterGrowth(ref supporterGrowthSecondsPerBitsUnit, Math.Max(0, value));
    }

    public int SupporterGrowthTier1Seconds
    {
        get => supporterGrowthTier1Seconds;
        set => SetAndRaiseSupporterGrowth(ref supporterGrowthTier1Seconds, Math.Max(0, value));
    }

    public int SupporterGrowthTier2Seconds
    {
        get => supporterGrowthTier2Seconds;
        set => SetAndRaiseSupporterGrowth(ref supporterGrowthTier2Seconds, Math.Max(0, value));
    }

    public int SupporterGrowthTier3Seconds
    {
        get => supporterGrowthTier3Seconds;
        set => SetAndRaiseSupporterGrowth(ref supporterGrowthTier3Seconds, Math.Max(0, value));
    }

    public int SupporterGrowthSoftCapSeconds
    {
        get => supporterGrowthSoftCapSeconds;
        set => SetAndRaiseSupporterGrowth(ref supporterGrowthSoftCapSeconds, Math.Max(0, value));
    }

    public int SupporterGrowthSoftCapMultiplierPercent
    {
        get => supporterGrowthSoftCapMultiplierPercent;
        set => SetAndRaiseSupporterGrowth(ref supporterGrowthSoftCapMultiplierPercent, Math.Clamp(value, 0, 100));
    }

    public int SupporterGrowthMaxPaidTimeSeconds
    {
        get => supporterGrowthMaxPaidTimeSeconds;
        set => SetAndRaiseSupporterGrowth(ref supporterGrowthMaxPaidTimeSeconds, Math.Max(1, value));
    }

    public string SupporterGrowthGrowKeyword
    {
        get => supporterGrowthGrowKeyword;
        set => SetAndRaiseSupporterGrowth(ref supporterGrowthGrowKeyword, NormalizeSupporterGrowthKeyword(value, "grow"));
    }

    public string SupporterGrowthShrinkKeyword
    {
        get => supporterGrowthShrinkKeyword;
        set => SetAndRaiseSupporterGrowth(ref supporterGrowthShrinkKeyword, NormalizeSupporterGrowthKeyword(value, "shrink"));
    }

    public double SupporterGrowthTier1HeightMeters
    {
        get => supporterGrowthTier1HeightMeters;
        set => SetAndRaiseSupporterGrowth(ref supporterGrowthTier1HeightMeters, Math.Max(0, value));
    }

    public double SupporterGrowthTier2HeightMeters
    {
        get => supporterGrowthTier2HeightMeters;
        set => SetAndRaiseSupporterGrowth(ref supporterGrowthTier2HeightMeters, Math.Max(0, value));
    }

    public double SupporterGrowthTier3HeightMeters
    {
        get => supporterGrowthTier3HeightMeters;
        set => SetAndRaiseSupporterGrowth(ref supporterGrowthTier3HeightMeters, Math.Max(0, value));
    }

    public ObservableCollection<AvatarScaleBitGrowthRange> SupporterGrowthBitRanges
    {
        get => supporterGrowthBitRanges;
        set
        {
            if (ReferenceEquals(supporterGrowthBitRanges, value))
            {
                return;
            }

            supporterGrowthBitRanges.CollectionChanged -= OnSupporterGrowthBitRangesChanged;
            UnwireSupporterGrowthBitRanges(supporterGrowthBitRanges);
            if (SetProperty(ref supporterGrowthBitRanges, value ?? []))
            {
                supporterGrowthBitRanges.CollectionChanged += OnSupporterGrowthBitRangesChanged;
                WireSupporterGrowthBitRanges(supporterGrowthBitRanges);
                RaiseSupporterGrowthProperties();
            }
        }
    }

    public bool UsesChannelPointReward => TriggerType == AvatarScaleTriggerType.ChannelPointReward;

    public bool UsesChatCommand => TriggerType == AvatarScaleTriggerType.ChatCommand;

    public bool UsesBits => TriggerType == AvatarScaleTriggerType.Bits;

    public bool UsesSubscription => TriggerType is AvatarScaleTriggerType.Subscription or AvatarScaleTriggerType.GiftSubscription;

    public bool UsesFollow => TriggerType == AvatarScaleTriggerType.Follow;

    public bool UsesSupporterGrowth => TriggerType == AvatarScaleTriggerType.SupporterGrowth;

    public bool UsesStandardScaleAction => !UsesSupporterGrowth;

    public bool UsesChatCommandFallback => UsesChannelPointReward;

    public bool UsesTargetHeight => ScaleMode == AvatarScaleMode.SetHeight;

    public bool UsesRandomHeight => ScaleMode == AvatarScaleMode.RandomHeight;

    public bool UsesGlitchyRandomHeight => ScaleMode == AvatarScaleMode.GlitchyRandomHeight;

    public bool UsesRandomHeightRange => UsesRandomHeight || UsesGlitchyRandomHeight;

    public bool UsesRelativeHeight => ScaleMode == AvatarScaleMode.RelativeHeight;

    public bool UsesRelativeMinimumHeight => UsesRelativeHeight && RelativeHeightMeters < 0;

    public bool UsesRelativeMaximumHeight => UsesRelativeHeight && RelativeHeightMeters > 0;

    public bool UsesMultiplier => ScaleMode == AvatarScaleMode.Multiplier;

    public bool UsesPreset => ScaleMode == AvatarScaleMode.Preset;

    public bool HasActiveTime => ActiveTimeSeconds > 0;

    public bool UsesConfiguredRestoreHeight => HasActiveTime;

    public bool HasScaleDisablePairings => TemporarilyDisabledScaleRuleIds.Count > 0;

    public Brush ManagedRewardReadyColorBrush => CreateColorBrush(ManagedRewardReadyColor);

    public Brush ManagedRewardCooldownColorBrush => CreateColorBrush(ManagedRewardCooldownColor);

    public string DisplayTitle => TriggerType switch
    {
        AvatarScaleTriggerType.ChannelPointReward when !string.IsNullOrWhiteSpace(RewardTitle) => RewardTitle,
        AvatarScaleTriggerType.ChatCommand when !string.IsNullOrWhiteSpace(CommandText) => CommandText,
        AvatarScaleTriggerType.Bits => $"Bits {Math.Min(MinimumBits, MaximumBits)}-{Math.Max(MinimumBits, MaximumBits)}",
        AvatarScaleTriggerType.Subscription => string.IsNullOrWhiteSpace(SubscriptionTier) ? "Subscription Scale" : $"Tier {SubscriptionTier} Sub Scale",
        AvatarScaleTriggerType.GiftSubscription => string.IsNullOrWhiteSpace(SubscriptionTier) ? "Gift Sub Scale" : $"Tier {SubscriptionTier} Gift Scale",
        AvatarScaleTriggerType.Follow => "Follow Scale",
        AvatarScaleTriggerType.SupporterGrowth => string.IsNullOrWhiteSpace(Name) ? "Supporter Growth" : Name,
        _ => string.IsNullOrWhiteSpace(Name) ? "Avatar Scale" : Name
    };

    public string TriggerSummary
    {
        get
        {
            var trigger = TriggerType switch
            {
                AvatarScaleTriggerType.ChannelPointReward => string.IsNullOrWhiteSpace(RewardTitle) ? "Set reward" : $"Reward {RewardTitle}",
                AvatarScaleTriggerType.ChatCommand => string.IsNullOrWhiteSpace(CommandText) ? "Set command" : $"Command {CommandText}",
                AvatarScaleTriggerType.Bits => $"Bits {Math.Min(MinimumBits, MaximumBits)}-{Math.Max(MinimumBits, MaximumBits)}",
                AvatarScaleTriggerType.Subscription => DescribeSubscription("Sub"),
                AvatarScaleTriggerType.GiftSubscription => DescribeSubscription("Gift sub"),
                AvatarScaleTriggerType.Follow => "Follow event",
                AvatarScaleTriggerType.SupporterGrowth => "Supporter Growth",
                _ => TriggerType.ToString()
            };

            return $"{trigger} | {ScaleSummary}";
        }
    }

    public string ScaleSummary => ScaleMode switch
    {
        AvatarScaleMode.SetHeight => $"Set {TargetHeightMeters:0.##}m",
        AvatarScaleMode.RandomHeight => $"Random {Math.Min(MinimumHeightMeters, MaximumHeightMeters):0.##}-{Math.Max(MinimumHeightMeters, MaximumHeightMeters):0.##}m",
        AvatarScaleMode.GlitchyRandomHeight => $"Glitchy {Math.Min(MinimumHeightMeters, MaximumHeightMeters):0.##}-{Math.Max(MinimumHeightMeters, MaximumHeightMeters):0.##}m",
        AvatarScaleMode.RelativeHeight => $"{RelativeHeightMeters:+0.##;-0.##;0}m relative",
        AvatarScaleMode.Multiplier => $"x{HeightMultiplier:0.##}",
        AvatarScaleMode.Preset => Preset.ToString(),
        _ => "Scale"
    };

    public string SupporterGrowthSummary =>
        $"Supporter growth +{SupporterGrowthTier1HeightMeters:0.##}/+{SupporterGrowthTier2HeightMeters:0.##}/+{SupporterGrowthTier3HeightMeters:0.##}m";

    public string ScaleRangeHelpText
    {
        get
        {
            var rangeText = AdvancedRangeEnabled
                ? "Advanced range is on. Crystal Relay accepts 0.01m to 10000m technically; extreme values can be uncomfortable or world-blocked."
                : "Safe range is 0.1m to 100m.";

            return BypassVrChatScaleLimits
                ? $"{rangeText} VRChat world min/max will be bypassed for this redeem."
                : rangeText;
        }
    }

    public static double GetPresetHeight(AvatarScalePreset presetValue) => presetValue switch
    {
        AvatarScalePreset.Tiny => 0.5,
        AvatarScalePreset.Small => 1.0,
        AvatarScalePreset.Tall => 2.2,
        AvatarScalePreset.Giant => 4.0,
        _ => 1.6
    };

    private string DescribeSubscription(string label)
    {
        var tierText = string.IsNullOrWhiteSpace(SubscriptionTier) ? "any tier" : $"tier {SubscriptionTier}";
        var min = Math.Min(MinimumMonths, MaximumMonths);
        var max = Math.Max(MinimumMonths, MaximumMonths);
        var monthText = MinimumMonths < 0 && MaximumMonths < 0 ? "any months" : $"{min}-{max} months";
        return $"{label} {tierText}, {monthText}";
    }

    private double ClampHeight(double value)
    {
        var minimum = AdvancedRangeEnabled ? AdvancedMinimumHeightMeters : SafeMinimumHeightMeters;
        var maximum = AdvancedRangeEnabled ? AdvancedMaximumHeightMeters : SafeMaximumHeightMeters;
        if (double.IsNaN(value) || double.IsInfinity(value))
        {
            return 1.6;
        }

        return Math.Clamp(value, minimum, maximum);
    }

    private double ClampRelativeHeight(double value)
    {
        var limit = AdvancedRangeEnabled ? AdvancedMaximumHeightMeters : SafeMaximumHeightMeters;
        if (double.IsNaN(value) || double.IsInfinity(value))
        {
            return 0;
        }

        return Math.Clamp(value, -limit, limit);
    }

    private bool SetAndRaiseSummary<T>(ref T storage, T value)
    {
        if (!SetProperty(ref storage, value))
        {
            return false;
        }

        RaiseTitleProperties();
        return true;
    }

    private bool SetAndRaiseScale(ref double storage, double value)
    {
        if (!SetProperty(ref storage, value))
        {
            return false;
        }

        RaiseScaleProperties();
        return true;
    }

    private bool SetAndRaiseScale(ref int storage, int value)
    {
        if (!SetProperty(ref storage, value))
        {
            return false;
        }

        RaiseScaleProperties();
        return true;
    }

    private bool SetAndRaiseSupporterGrowth(ref double storage, double value)
    {
        if (!SetProperty(ref storage, value))
        {
            return false;
        }

        RaiseSupporterGrowthProperties();
        return true;
    }

    private bool SetAndRaiseSupporterGrowth(ref int storage, int value)
    {
        if (!SetProperty(ref storage, value))
        {
            return false;
        }

        RaiseSupporterGrowthProperties();
        return true;
    }

    private bool SetAndRaiseSupporterGrowth(ref bool storage, bool value)
    {
        if (!SetProperty(ref storage, value))
        {
            return false;
        }

        RaiseSupporterGrowthProperties();
        return true;
    }

    private bool SetAndRaiseSupporterGrowth(ref string storage, string value)
    {
        if (!SetProperty(ref storage, value))
        {
            return false;
        }

        RaiseSupporterGrowthProperties();
        return true;
    }

    private static string NormalizeSupporterGrowthKeyword(string? value, string fallback)
    {
        var normalizedValue = value?.Trim() ?? string.Empty;
        return string.IsNullOrWhiteSpace(normalizedValue) ? fallback : normalizedValue;
    }

    private void RaiseTriggerProperties()
    {
        RaisePropertyChanged(nameof(UsesChannelPointReward));
        RaisePropertyChanged(nameof(UsesChatCommand));
        RaisePropertyChanged(nameof(UsesBits));
        RaisePropertyChanged(nameof(UsesSubscription));
        RaisePropertyChanged(nameof(UsesFollow));
        RaisePropertyChanged(nameof(UsesSupporterGrowth));
        RaisePropertyChanged(nameof(UsesStandardScaleAction));
        RaisePropertyChanged(nameof(UsesChatCommandFallback));
        RaiseTitleProperties();
    }

    private void RaiseScaleProperties()
    {
        RaisePropertyChanged(nameof(UsesTargetHeight));
        RaisePropertyChanged(nameof(UsesRandomHeight));
        RaisePropertyChanged(nameof(UsesGlitchyRandomHeight));
        RaisePropertyChanged(nameof(UsesRandomHeightRange));
        RaisePropertyChanged(nameof(UsesRelativeHeight));
        RaisePropertyChanged(nameof(UsesRelativeMinimumHeight));
        RaisePropertyChanged(nameof(UsesRelativeMaximumHeight));
        RaisePropertyChanged(nameof(UsesMultiplier));
        RaisePropertyChanged(nameof(UsesPreset));
        RaisePropertyChanged(nameof(HasActiveTime));
        RaisePropertyChanged(nameof(UsesConfiguredRestoreHeight));
        RaisePropertyChanged(nameof(ScaleSummary));
        RaisePropertyChanged(nameof(TriggerSummary));
    }

    private void RaiseSupporterGrowthProperties()
    {
        RaisePropertyChanged(nameof(SupporterGrowthBitRanges));
        RaisePropertyChanged(nameof(SupporterGrowthSummary));
        RaisePropertyChanged(nameof(TriggerSummary));
    }

    private void RaiseTitleProperties()
    {
        RaisePropertyChanged(nameof(DisplayTitle));
        RaisePropertyChanged(nameof(TriggerSummary));
    }

    private void OnTemporarilyDisabledScaleRuleIdsChanged(object? sender, NotifyCollectionChangedEventArgs e)
    {
        RaisePropertyChanged(nameof(TemporarilyDisabledScaleRuleIds));
        RaisePropertyChanged(nameof(HasScaleDisablePairings));
    }

    private void OnSupporterGrowthBitRangesChanged(object? sender, NotifyCollectionChangedEventArgs e)
    {
        if (e.OldItems is not null)
        {
            foreach (AvatarScaleBitGrowthRange range in e.OldItems)
            {
                range.PropertyChanged -= OnSupporterGrowthBitRangeChanged;
            }
        }

        if (e.NewItems is not null)
        {
            foreach (AvatarScaleBitGrowthRange range in e.NewItems)
            {
                range.PropertyChanged += OnSupporterGrowthBitRangeChanged;
            }
        }

        RaiseSupporterGrowthProperties();
    }

    private void OnSupporterGrowthBitRangeChanged(object? sender, System.ComponentModel.PropertyChangedEventArgs e)
    {
        RaiseSupporterGrowthProperties();
    }

    private void WireSupporterGrowthBitRanges(IEnumerable<AvatarScaleBitGrowthRange> ranges)
    {
        foreach (var range in ranges)
        {
            range.PropertyChanged += OnSupporterGrowthBitRangeChanged;
        }
    }

    private void UnwireSupporterGrowthBitRanges(IEnumerable<AvatarScaleBitGrowthRange> ranges)
    {
        foreach (var range in ranges)
        {
            range.PropertyChanged -= OnSupporterGrowthBitRangeChanged;
        }
    }

    private static Brush CreateColorBrush(string colorText)
    {
        var brush = new SolidColorBrush((Color)ColorConverter.ConvertFromString(colorText));
        brush.Freeze();
        return brush;
    }
}
