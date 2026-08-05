using System.Collections.ObjectModel;
using System.Collections.Specialized;
using System.Windows.Media;
using VrcTwitchOscBridge.Infrastructure;
using VrcTwitchOscBridge.Services;

namespace VrcTwitchOscBridge.Models;

public sealed class UniversalTriggerRule : ObservableObject
{
    private Guid id = Guid.NewGuid();
    private bool isEnabled = true;
    private string name = "New Universal Trigger";
    private UniversalTriggerType triggerType = UniversalTriggerType.ChatCommand;
    private bool chatCommandEnabled;
    private string commandText = string.Empty;
    private ChatCommandPermission chatCommandPermission = ChatCommandPermission.Moderators;
    private string rewardId = string.Empty;
    private string rewardTitle = string.Empty;
    private string rewardDescription = string.Empty;
    private int rewardCost = 100;
    private int rewardCooldownSeconds;
    private TwitchRewardSyncMode rewardSyncMode = TwitchRewardSyncMode.CreateOrManage;
    private string managedRewardReadyColor = ManagedRewardPresentation.ReadyBackgroundColor;
    private string managedRewardCooldownColor = ManagedRewardPresentation.InUseBackgroundColor;
    private bool deleteManagedRewardWhenInactive;
    private int minimumBits = 1;
    private int maximumBits = 1_000_000;
    private string subscriptionTier = string.Empty;
    private int minimumMonths = -1;
    private int maximumMonths = -1;
    private int globalDelaySeconds;
    private int userDelaySeconds;
    private bool executeRandomAction;
    private string importSource = string.Empty;
    private string importIdentity = string.Empty;
    private ObservableCollection<UniversalTriggerAction> actions = [];

    public UniversalTriggerRule()
    {
        actions.CollectionChanged += OnActionsChanged;
    }

    public Guid Id
    {
        get => id;
        set => SetProperty(ref id, value == Guid.Empty ? Guid.NewGuid() : value);
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
            if (SetProperty(ref name, value ?? string.Empty))
            {
                RaiseTitleProperties();
            }
        }
    }

    public UniversalTriggerType TriggerType
    {
        get => triggerType;
        set
        {
            if (SetProperty(ref triggerType, Enum.IsDefined(value) ? value : UniversalTriggerType.ChatCommand))
            {
                RaisePropertyChanged(nameof(UsesChatCommand));
                RaisePropertyChanged(nameof(UsesChannelPointReward));
                RaisePropertyChanged(nameof(UsesBits));
                RaisePropertyChanged(nameof(UsesSubscription));
                RaisePropertyChanged(nameof(UsesFollow));
                RaisePropertyChanged(nameof(UsesPermission));
                RaiseTitleProperties();
            }
        }
    }

    public string CommandText
    {
        get => commandText;
        set
        {
            if (SetProperty(ref commandText, ChatCommandUtility.Normalize(value)))
            {
                RaiseTitleProperties();
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
                RaiseTitleProperties();
            }
        }
    }

    public ChatCommandPermission ChatCommandPermission
    {
        get => chatCommandPermission;
        set => SetProperty(ref chatCommandPermission, Enum.IsDefined(value) ? value : ChatCommandPermission.Moderators);
    }

    public string RewardId
    {
        get => rewardId;
        set
        {
            if (SetProperty(ref rewardId, value?.Trim() ?? string.Empty))
            {
                RaiseTitleProperties();
            }
        }
    }

    public string RewardTitle
    {
        get => rewardTitle;
        set
        {
            if (SetProperty(ref rewardTitle, value?.Trim() ?? string.Empty))
            {
                RaiseTitleProperties();
            }
        }
    }

    public string RewardDescription
    {
        get => rewardDescription;
        set => SetProperty(ref rewardDescription, value ?? string.Empty);
    }

    public int RewardCost
    {
        get => rewardCost;
        set
        {
            if (SetProperty(ref rewardCost, Math.Max(1, value)))
            {
                RaiseTitleProperties();
            }
        }
    }

    public int RewardCooldownSeconds
    {
        get => rewardCooldownSeconds;
        set => SetProperty(ref rewardCooldownSeconds, Math.Max(0, value));
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
                RaiseTitleProperties();
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
        set
        {
            if (SetProperty(ref deleteManagedRewardWhenInactive, value))
            {
                RaiseTitleProperties();
            }
        }
    }

    public int MinimumBits
    {
        get => minimumBits;
        set
        {
            if (SetProperty(ref minimumBits, Math.Max(1, value)))
            {
                RaiseTitleProperties();
            }
        }
    }

    public int MaximumBits
    {
        get => maximumBits;
        set
        {
            if (SetProperty(ref maximumBits, Math.Max(1, value)))
            {
                RaiseTitleProperties();
            }
        }
    }

    public string SubscriptionTier
    {
        get => subscriptionTier;
        set
        {
            if (SetProperty(ref subscriptionTier, value?.Trim() ?? string.Empty))
            {
                RaiseTitleProperties();
            }
        }
    }

    public int MinimumMonths
    {
        get => minimumMonths;
        set
        {
            if (SetProperty(ref minimumMonths, value))
            {
                RaiseTitleProperties();
            }
        }
    }

    public int MaximumMonths
    {
        get => maximumMonths;
        set
        {
            if (SetProperty(ref maximumMonths, value))
            {
                RaiseTitleProperties();
            }
        }
    }

    public int GlobalDelaySeconds
    {
        get => globalDelaySeconds;
        set => SetProperty(ref globalDelaySeconds, Math.Max(0, value));
    }

    public int UserDelaySeconds
    {
        get => userDelaySeconds;
        set => SetProperty(ref userDelaySeconds, Math.Max(0, value));
    }

    public bool ExecuteRandomAction
    {
        get => executeRandomAction;
        set
        {
            if (SetProperty(ref executeRandomAction, value))
            {
                RaiseTitleProperties();
            }
        }
    }

    public string ImportSource
    {
        get => importSource;
        set => SetProperty(ref importSource, value?.Trim() ?? string.Empty);
    }

    public string ImportIdentity
    {
        get => importIdentity;
        set => SetProperty(ref importIdentity, value?.Trim() ?? string.Empty);
    }

    public ObservableCollection<UniversalTriggerAction> Actions
    {
        get => actions;
        set
        {
            if (ReferenceEquals(actions, value))
            {
                return;
            }

            actions.CollectionChanged -= OnActionsChanged;
            if (SetProperty(ref actions, value ?? []))
            {
                actions.CollectionChanged += OnActionsChanged;
                RaisePropertyChanged(nameof(HasActions));
                RaiseTitleProperties();
            }
        }
    }

    public bool UsesChatCommand => TriggerType == UniversalTriggerType.ChatCommand;

    public bool UsesChannelPointReward => TriggerType == UniversalTriggerType.ChannelPointReward;

    public bool UsesBits => TriggerType == UniversalTriggerType.Bits;

    public bool UsesSubscription => TriggerType is UniversalTriggerType.Subscription or UniversalTriggerType.GiftSubscription;

    public bool UsesFollow => TriggerType == UniversalTriggerType.Follow;

    public bool UsesPermission => UsesChatCommand;

    public bool HasActions => Actions.Count > 0;

    public bool HasConfiguredChatCommandFallback => UsesChannelPointReward
        && ChatCommandEnabled
        && ChatCommandUtility.IsConfigured(CommandText);

    public bool IsConfigured => IsTriggerFilterConfigured && HasCompleteAction;

    public bool IsTriggerFilterConfigured => TriggerType switch
    {
        UniversalTriggerType.ChatCommand => ChatCommandUtility.IsConfigured(CommandText),
        UniversalTriggerType.ChannelPointReward => ManagedRewardPresentation.HasConfiguredRewardIdentity(
                RewardSyncMode,
                RewardId,
                RewardTitle)
            || HasConfiguredChatCommandFallback,
        UniversalTriggerType.Bits => Math.Max(1, MaximumBits) >= Math.Max(1, MinimumBits),
        UniversalTriggerType.Subscription or UniversalTriggerType.GiftSubscription or UniversalTriggerType.Follow => true,
        _ => false
    };

    public bool HasCompleteAction => Actions.Any(IsCompleteAction);

    public Brush ManagedRewardReadyColorBrush => CreateColorBrush(ManagedRewardReadyColor);

    public Brush ManagedRewardCooldownColorBrush => CreateColorBrush(ManagedRewardCooldownColor);

    public int TriggerGroupSortOrder => !IsConfigured ? 0 : TriggerType switch
    {
        UniversalTriggerType.ChatCommand => 1,
        UniversalTriggerType.ChannelPointReward => 2,
        UniversalTriggerType.Bits => 3,
        UniversalTriggerType.Subscription => 4,
        UniversalTriggerType.GiftSubscription => 5,
        UniversalTriggerType.Follow => 6,
        _ => 99
    };

    public string TriggerGroupTitle => !IsConfigured ? "Unconfigured" : TriggerType switch
    {
        UniversalTriggerType.ChatCommand => "Chat Commands",
        UniversalTriggerType.ChannelPointReward => "Channel Point Rewards",
        UniversalTriggerType.Bits => "Bits",
        UniversalTriggerType.Subscription => "Subscriptions",
        UniversalTriggerType.GiftSubscription => "Gift Subscriptions",
        UniversalTriggerType.Follow => "Follows",
        _ => "Other"
    };

    public string DisplayTitle => TriggerType switch
    {
        UniversalTriggerType.ChatCommand when !string.IsNullOrWhiteSpace(CommandText) => CommandText,
        UniversalTriggerType.ChannelPointReward when !string.IsNullOrWhiteSpace(RewardTitle) => RewardTitle,
        UniversalTriggerType.Bits => $"Bits {Math.Min(MinimumBits, MaximumBits)}-{Math.Max(MinimumBits, MaximumBits)}",
        UniversalTriggerType.Subscription => string.IsNullOrWhiteSpace(SubscriptionTier) ? "Subscription" : $"Tier {SubscriptionTier} Subscription",
        UniversalTriggerType.GiftSubscription => string.IsNullOrWhiteSpace(SubscriptionTier) ? "Gift Subscription" : $"Tier {SubscriptionTier} Gift Sub",
        UniversalTriggerType.Follow => "Follow",
        _ => string.IsNullOrWhiteSpace(Name) ? "Universal Trigger" : Name
    };

    public string TriggerSummary
    {
        get
        {
            var filter = TriggerType switch
            {
                UniversalTriggerType.ChatCommand => string.IsNullOrWhiteSpace(CommandText) ? "Set command" : $"Command {CommandText}",
                UniversalTriggerType.ChannelPointReward => DescribeRewardFilter(),
                UniversalTriggerType.Bits => $"Bits {Math.Min(MinimumBits, MaximumBits)}-{Math.Max(MinimumBits, MaximumBits)}",
                UniversalTriggerType.Subscription => DescribeSubscriptionFilter("Sub"),
                UniversalTriggerType.GiftSubscription => DescribeSubscriptionFilter("Gift sub"),
                UniversalTriggerType.Follow => "Follow event",
                _ => TriggerType.ToString()
            };
            var actionMode = ExecuteRandomAction ? "random action" : "all actions";
            var setupText = IsConfigured ? filter : $"{filter} | Needs setup";
            return $"{setupText} | {Actions.Count} action(s), {actionMode}";
        }
    }

    private string DescribeRewardFilter()
    {
        var rewardText = string.IsNullOrWhiteSpace(RewardTitle) ? "Set reward" : $"Reward {RewardTitle}";
        return ChatCommandEnabled && ChatCommandUtility.IsConfigured(CommandText)
            ? $"{rewardText} | Command {CommandText}"
            : rewardText;
    }

    private string DescribeSubscriptionFilter(string label)
    {
        var tierText = string.IsNullOrWhiteSpace(SubscriptionTier) ? "any tier" : $"tier {SubscriptionTier}";
        var min = Math.Min(MinimumMonths, MaximumMonths);
        var max = Math.Max(MinimumMonths, MaximumMonths);
        var monthText = MinimumMonths < 0 && MaximumMonths < 0 ? "any months" : $"{min}-{max} months";
        return $"{label} {tierText}, {monthText}";
    }

    private void OnActionsChanged(object? sender, NotifyCollectionChangedEventArgs e)
    {
        RefreshActionState();
    }

    public void RefreshActionState()
    {
        RaisePropertyChanged(nameof(Actions));
        RaisePropertyChanged(nameof(HasActions));
        RaisePropertyChanged(nameof(HasCompleteAction));
        RaiseTitleProperties();
    }

    private void RaiseTitleProperties()
    {
        RaisePropertyChanged(nameof(IsTriggerFilterConfigured));
        RaisePropertyChanged(nameof(IsConfigured));
        RaisePropertyChanged(nameof(HasConfiguredChatCommandFallback));
        RaisePropertyChanged(nameof(DisplayTitle));
        RaisePropertyChanged(nameof(TriggerSummary));
        RaisePropertyChanged(nameof(TriggerGroupSortOrder));
        RaisePropertyChanged(nameof(TriggerGroupTitle));
    }

    private static bool IsCompleteAction(UniversalTriggerAction action) =>
        !string.IsNullOrWhiteSpace(action.OscAddress)
        && !string.IsNullOrWhiteSpace(action.TargetValue)
        && (action.DurationSeconds <= 0 || !string.IsNullOrWhiteSpace(action.DefaultValue));

    private static Brush CreateColorBrush(string colorText)
    {
        var brush = new SolidColorBrush((Color)ColorConverter.ConvertFromString(colorText));
        brush.Freeze();
        return brush;
    }
}
