using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Text.Json.Serialization;
using VrcTwitchOscBridge.Infrastructure;

namespace VrcTwitchOscBridge.Models;

public enum PowerUpActionKind
{
    TriggerAction,
    AvatarScaling
}

public sealed class PowerUpRule : ObservableObject
{
    private Guid id = Guid.NewGuid();
    private bool isEnabled = true;
    private string name = "New Power Up";
    private TwitchRewardSyncMode sourceMode = TwitchRewardSyncMode.LinkExisting;
    private string powerUpId = string.Empty;
    private string powerUpTitle = string.Empty;
    private int bitsCost = 100;
    private string prompt = string.Empty;
    private bool avatarScoped;
    private string avatarId = string.Empty;
    private string avatarName = string.Empty;
    private int cooldownSeconds = 30;
    private bool fixedFloatAddEnabled;
    private string fixedFloatAddValue = "0.05";
    private string fixedFloatAddMinimumValue = "0";
    private string fixedFloatAddMaximumValue = "1";
    private PowerUpActionKind actionKind = PowerUpActionKind.TriggerAction;
    private TriggerRule actionRule = CreateDefaultTriggerAction();
    private AvatarScaleRule scaleAction = CreateDefaultScaleAction();

    public PowerUpRule()
    {
        WireNestedActions();
    }

    public Guid Id
    {
        get => id;
        set => SetProperty(ref id, value == Guid.Empty ? Guid.NewGuid() : value);
    }

    public bool IsEnabled
    {
        get => isEnabled;
        set
        {
            if (SetProperty(ref isEnabled, value))
            {
                RaisePropertyChanged(nameof(TriggerSummary));
            }
        }
    }

    public string Name
    {
        get => name;
        set
        {
            var normalizedValue = value ?? string.Empty;
            if (SetProperty(ref name, normalizedValue))
            {
                ActionRule.Name = normalizedValue;
                ScaleAction.Name = normalizedValue;
                RaisePropertyChanged(nameof(DisplayTitle));
                RaisePropertyChanged(nameof(TriggerSummary));
            }
        }
    }

    public TwitchRewardSyncMode SourceMode
    {
        get => sourceMode;
        set
        {
            var normalizedValue = Enum.IsDefined(value) ? value : TwitchRewardSyncMode.LinkExisting;
            if (SetProperty(ref sourceMode, normalizedValue))
            {
                RaisePropertyChanged(nameof(UsesLinkedExistingPowerUp));
                RaisePropertyChanged(nameof(UsesManagedPowerUpPlaceholder));
                RaisePropertyChanged(nameof(TriggerSummary));
            }
        }
    }

    public bool UsesLinkedExistingPowerUp => SourceMode == TwitchRewardSyncMode.LinkExisting;

    public bool UsesManagedPowerUpPlaceholder => SourceMode == TwitchRewardSyncMode.CreateOrManage;

    public string PowerUpId
    {
        get => powerUpId;
        set
        {
            if (SetProperty(ref powerUpId, value?.Trim() ?? string.Empty))
            {
                RaisePropertyChanged(nameof(TriggerSummary));
            }
        }
    }

    public string PowerUpTitle
    {
        get => powerUpTitle;
        set
        {
            if (SetProperty(ref powerUpTitle, string.IsNullOrWhiteSpace(value) ? string.Empty : value.Trim()))
            {
                RaisePropertyChanged(nameof(DisplayTitle));
                RaisePropertyChanged(nameof(TriggerSummary));
            }
        }
    }

    public int BitsCost
    {
        get => bitsCost;
        set
        {
            if (SetProperty(ref bitsCost, Math.Max(1, value)))
            {
                RaisePropertyChanged(nameof(TriggerSummary));
            }
        }
    }

    public string Prompt
    {
        get => prompt;
        set => SetProperty(ref prompt, value ?? string.Empty);
    }

    public bool AvatarScoped
    {
        get => avatarScoped;
        set
        {
            if (SetProperty(ref avatarScoped, value))
            {
                RaisePropertyChanged(nameof(TriggerSummary));
            }
        }
    }

    public string AvatarId
    {
        get => avatarId;
        set
        {
            if (SetProperty(ref avatarId, value?.Trim() ?? string.Empty))
            {
                RaisePropertyChanged(nameof(HasAvatarScope));
                RaisePropertyChanged(nameof(TriggerSummary));
            }
        }
    }

    public string AvatarName
    {
        get => avatarName;
        set
        {
            if (SetProperty(ref avatarName, value?.Trim() ?? string.Empty))
            {
                RaisePropertyChanged(nameof(AvatarScopeLabel));
                RaisePropertyChanged(nameof(TriggerSummary));
            }
        }
    }

    public bool HasAvatarScope => !string.IsNullOrWhiteSpace(AvatarId);

    public string AvatarScopeLabel => string.IsNullOrWhiteSpace(AvatarName)
        ? AvatarId
        : AvatarName;

    [JsonIgnore]
    public string AvatarDisplayName => string.IsNullOrWhiteSpace(AvatarName)
        ? string.IsNullOrWhiteSpace(AvatarId) ? "(Not set)" : AvatarId
        : AvatarName;

    public int CooldownSeconds
    {
        get => cooldownSeconds;
        set
        {
            if (SetProperty(ref cooldownSeconds, Math.Max(0, value)))
            {
                ActionRule.CooldownSeconds = cooldownSeconds;
                ScaleAction.CooldownSeconds = cooldownSeconds;
                RaisePropertyChanged(nameof(TriggerSummary));
            }
        }
    }

    public bool FixedFloatAddEnabled
    {
        get => fixedFloatAddEnabled;
        set
        {
            if (SetProperty(ref fixedFloatAddEnabled, value))
            {
                ApplyFixedFloatAddDefaults(ActionRule);
                RaisePropertyChanged(nameof(TriggerSummary));
            }
        }
    }

    public string FixedFloatAddValue
    {
        get => fixedFloatAddValue;
        set
        {
            if (SetProperty(ref fixedFloatAddValue, string.IsNullOrWhiteSpace(value) ? "0.05" : value.Trim()))
            {
                ApplyFixedFloatAddDefaults(ActionRule);
            }
        }
    }

    public string FixedFloatAddMinimumValue
    {
        get => fixedFloatAddMinimumValue;
        set
        {
            if (SetProperty(ref fixedFloatAddMinimumValue, string.IsNullOrWhiteSpace(value) ? "0" : value.Trim()))
            {
                ApplyFixedFloatAddDefaults(ActionRule);
            }
        }
    }

    public string FixedFloatAddMaximumValue
    {
        get => fixedFloatAddMaximumValue;
        set
        {
            if (SetProperty(ref fixedFloatAddMaximumValue, string.IsNullOrWhiteSpace(value) ? "1" : value.Trim()))
            {
                ApplyFixedFloatAddDefaults(ActionRule);
            }
        }
    }

    public PowerUpActionKind ActionKind
    {
        get => actionKind;
        set
        {
            var normalizedValue = Enum.IsDefined(value) ? value : PowerUpActionKind.TriggerAction;
            if (SetProperty(ref actionKind, normalizedValue))
            {
                RaisePropertyChanged(nameof(UsesTriggerAction));
                RaisePropertyChanged(nameof(UsesAvatarScaling));
                RaisePropertyChanged(nameof(TriggerSummary));
            }
        }
    }

    public bool UsesTriggerAction => ActionKind == PowerUpActionKind.TriggerAction;

    public bool UsesAvatarScaling => ActionKind == PowerUpActionKind.AvatarScaling;

    public TriggerRule ActionRule
    {
        get => actionRule;
        set
        {
            var nextValue = value ?? CreateDefaultTriggerAction();
            if (ReferenceEquals(actionRule, nextValue))
            {
                return;
            }

            UnwireTriggerAction(actionRule);
            actionRule = nextValue;
            ApplyTriggerActionDefaults(actionRule, Name);
            WireTriggerAction(actionRule);
            RaisePropertyChanged();
            RaisePropertyChanged(nameof(TriggerSummary));
        }
    }

    public AvatarScaleRule ScaleAction
    {
        get => scaleAction;
        set
        {
            var nextValue = value ?? CreateDefaultScaleAction();
            if (ReferenceEquals(scaleAction, nextValue))
            {
                return;
            }

            UnwireScaleAction(scaleAction);
            scaleAction = nextValue;
            ApplyScaleActionDefaults(scaleAction, Name);
            WireScaleAction(scaleAction);
            RaisePropertyChanged();
            RaisePropertyChanged(nameof(TriggerSummary));
        }
    }

    public string DisplayTitle
    {
        get
        {
            if (!string.IsNullOrWhiteSpace(PowerUpTitle))
            {
                return PowerUpTitle.Trim();
            }

            return string.IsNullOrWhiteSpace(Name) ? "Power Up" : Name.Trim();
        }
    }

    public string TriggerSummary
    {
        get
        {
            var enabledText = IsEnabled ? "Enabled" : "Disabled";
            var sourceText = UsesLinkedExistingPowerUp ? "linked" : "managed placeholder";
            var title = string.IsNullOrWhiteSpace(PowerUpTitle) ? "Power Up not linked" : PowerUpTitle.Trim();
            var scopeText = AvatarScoped
                ? string.IsNullOrWhiteSpace(AvatarScopeLabel) ? "avatar-scoped" : $"avatar: {AvatarScopeLabel}"
                : "global";
            var actionText = UsesAvatarScaling ? "Avatar Scaling" : ActionRule.ActionType.ToString();
            return $"{enabledText} | {sourceText} | {title} | {BitsCost:N0} Bits | {scopeText} | {actionText}";
        }
    }

    public void ApplyLinkedPowerUp(string id, string title, int bits, string prompt)
    {
        PowerUpId = id;
        PowerUpTitle = title;
        BitsCost = bits <= 0 ? BitsCost : bits;
        Prompt = prompt;
        SourceMode = TwitchRewardSyncMode.LinkExisting;
    }

    public static TriggerRule CreateDefaultTriggerAction()
    {
        return new TriggerRule
        {
            Name = "New Power Up",
            TriggerType = TwitchTriggerType.PowerUp,
            RewardSyncMode = TwitchRewardSyncMode.LinkExisting,
            ChannelPointRewardId = string.Empty,
            ChannelPointRewardTitle = string.Empty,
            ChannelPointRewardDescription = string.Empty,
            ChatCommandEnabled = false,
            ChatCommandText = string.Empty,
            MinimumAmount = 1,
            AmountScaledDurationEnabled = false,
            ActionType = OscActionType.AvatarParameter,
            ParameterName = "VRCEmote",
            ParameterType = OscParameterType.Int,
            ParameterValue = "1",
            ResetValue = "0",
            RangeMinimum = 0,
            RangeMaximum = 5,
            DurationSeconds = 10,
            CooldownSeconds = 30,
            SharedRewardHelpText = "Power Up Set Trigger",
            BotMessageTemplate = "{user} triggered {rule}. Active for {duration}. Cooldown {cooldown}.",
            SetTriggerActions = new ObservableCollection<SetTriggerAction>()
        };
    }

    public static AvatarScaleRule CreateDefaultScaleAction()
    {
        return new AvatarScaleRule
        {
            Name = "New Power Up Scale",
            TriggerType = AvatarScaleTriggerType.Bits,
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
            SetHeightTransitionSeconds = 0,
            RandomHeightTransitionSeconds = 0,
            RelativeHeightTransitionSeconds = 0,
            MultiplierTransitionSeconds = 0,
            PresetTransitionSeconds = 0,
            GlitchyRandomHeightTransitionSeconds = 0,
            SupporterGrowthTransitionSeconds = 0,
            MinimumBits = 1,
            MaximumBits = int.MaxValue
        };
    }

    private void WireNestedActions()
    {
        ApplyTriggerActionDefaults(actionRule, Name);
        ApplyScaleActionDefaults(scaleAction, Name);
        WireTriggerAction(actionRule);
        WireScaleAction(scaleAction);
    }

    private void WireTriggerAction(TriggerRule rule) => rule.PropertyChanged += NestedActionChanged;

    private void UnwireTriggerAction(TriggerRule rule) => rule.PropertyChanged -= NestedActionChanged;

    private void WireScaleAction(AvatarScaleRule rule) => rule.PropertyChanged += NestedActionChanged;

    private void UnwireScaleAction(AvatarScaleRule rule) => rule.PropertyChanged -= NestedActionChanged;

    private void NestedActionChanged(object? sender, PropertyChangedEventArgs e)
    {
        RaisePropertyChanged(nameof(TriggerSummary));
    }

    private void ApplyTriggerActionDefaults(TriggerRule rule, string ownerName)
    {
        rule.TriggerType = TwitchTriggerType.PowerUp;
        rule.RewardSyncMode = TwitchRewardSyncMode.LinkExisting;
        rule.ChatCommandEnabled = false;
        rule.ChatCommandText = string.Empty;
        rule.ChannelPointRewardId = string.Empty;
        rule.ChannelPointRewardTitle = string.Empty;
        rule.ChannelPointRewardDescription = string.Empty;
        rule.MinimumAmount = 1;
        rule.CooldownSeconds = CooldownSeconds;
        rule.SharedRewardHelpText = string.IsNullOrWhiteSpace(rule.SharedRewardHelpText)
            ? "Power Up Set Trigger"
            : rule.SharedRewardHelpText;
        if (string.IsNullOrWhiteSpace(rule.Name))
        {
            rule.Name = ownerName;
        }

        ApplyFixedFloatAddDefaults(rule);
    }

    private void ApplyScaleActionDefaults(AvatarScaleRule rule, string ownerName)
    {
        rule.TriggerType = AvatarScaleTriggerType.Bits;
        rule.RewardId = string.Empty;
        rule.RewardTitle = string.Empty;
        rule.CommandText = string.Empty;
        rule.MinimumBits = 1;
        rule.MaximumBits = int.MaxValue;
        rule.CooldownSeconds = CooldownSeconds;
        if (string.IsNullOrWhiteSpace(rule.Name))
        {
            rule.Name = ownerName;
        }
    }

    private void ApplyFixedFloatAddDefaults(TriggerRule rule)
    {
        rule.SupporterFloatAddEnabled = FixedFloatAddEnabled;
        rule.SupporterFloatAddMinimumValue = FixedFloatAddMinimumValue;
        rule.SupporterFloatAddMaximumValue = FixedFloatAddMaximumValue;
        rule.SupporterFloatAddRanges =
        [
            new SupporterFloatAddRange
            {
                MinimumAmount = 1,
                MaximumAmount = 0,
                AddValue = FixedFloatAddValue
            }
        ];
    }
}
