using System;
using System.Collections.ObjectModel;
using System.Collections.Specialized;
using System.Windows.Media;
using VrcTwitchOscBridge.Infrastructure;
using VrcTwitchOscBridge.Services;
using Brush = System.Windows.Media.Brush;

namespace VrcTwitchOscBridge.Models;

public sealed class AvatarTriggerProfile : ObservableObject
{
    private Guid id = Guid.NewGuid();
    private bool isEnabled = true;
    private bool isMasterProfile;
    private string name = "New Avatar Set";
    private string avatarId = string.Empty;
    private string avatarName = string.Empty;
    private bool isCurrentAvatarActive;
    private bool isRewardTestOverrideEnabled;
    private string setTriggerMasterRewardId = string.Empty;
    private string setTriggerMasterRewardTitle = string.Empty;
    private string setTriggerMasterRewardDescription = string.Empty;
    private int setTriggerMasterRewardCost = 100;
    private TwitchRewardSyncMode setTriggerMasterRewardSyncMode = TwitchRewardSyncMode.CreateOrManage;
    private int setTriggerMasterRewardCooldownSeconds;
    private string setTriggerMasterRewardReadyColor = ManagedRewardPresentation.ReadyBackgroundColor;
    private string setTriggerMasterRewardCooldownColor = ManagedRewardPresentation.InUseBackgroundColor;
    private bool deleteSetTriggerMasterRewardWhenInactive;
    private bool useSharedNumberedOutfitReward = true;
    private bool postOutfitChoiceListToTwitchChat;
private ObservableCollection<TriggerRule> channelPointRules = [];
    private bool useWardrobeMode;
    private int wardrobeCooldownSeconds;
    private ObservableCollection<WardrobeOutfit> wardrobeOutfits = [];
    private bool useWardrobeMasterReward;
    private string wardrobeMasterRewardId = string.Empty;
    private string wardrobeMasterRewardTitle = string.Empty;
    private int wardrobeMasterRewardCost = 100;
    private TwitchRewardSyncMode wardrobeMasterRewardSyncMode = TwitchRewardSyncMode.CreateOrManage;
    private int wardrobeMasterRewardCooldownSeconds;
    private string wardrobeMasterRewardReadyColor = ManagedRewardPresentation.ReadyBackgroundColor;
    private string wardrobeMasterRewardCooldownColor = ManagedRewardPresentation.InUseBackgroundColor;

    public AvatarTriggerProfile()
    {
        channelPointRules.CollectionChanged += OnChannelPointRulesChanged;
        wardrobeOutfits.CollectionChanged += OnWardrobeOutfitsChanged;
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

    public bool IsMasterProfile
    {
        get => isMasterProfile;
        set
        {
            if (SetProperty(ref isMasterProfile, value))
            {
                RaisePropertyChanged(nameof(DisplayTitle));
                RaisePropertyChanged(nameof(MasterStatusText));
            }
        }
    }

    public string Name
    {
        get => name;
        set
        {
            if (SetProperty(ref name, value))
            {
                RaisePropertyChanged(nameof(DisplayTitle));
            }
        }
    }

    public string AvatarId
    {
        get => avatarId;
        set
        {
            if (SetProperty(ref avatarId, value))
            {
                RaisePropertyChanged(nameof(HasAvatarSelected));
                RaisePropertyChanged(nameof(DisplayTitle));
                RaisePropertyChanged(nameof(AvatarDisplayName));
            }
        }
    }

    public string AvatarName
    {
        get => avatarName;
        set
        {
            if (SetProperty(ref avatarName, value))
            {
                RaisePropertyChanged(nameof(DisplayTitle));
                RaisePropertyChanged(nameof(AvatarDisplayName));
            }
        }
    }

    public bool IsCurrentAvatarActive
    {
        get => isCurrentAvatarActive;
        set
        {
            if (SetProperty(ref isCurrentAvatarActive, value))
            {
                RaisePropertyChanged(nameof(CurrentAvatarStatusText));
            }
        }
    }

    public bool IsRewardTestOverrideEnabled
    {
        get => isRewardTestOverrideEnabled;
        set
        {
            if (SetProperty(ref isRewardTestOverrideEnabled, value))
            {
                RaisePropertyChanged(nameof(CurrentAvatarStatusText));
            }
        }
    }

    public string SetTriggerMasterRewardId
    {
        get => setTriggerMasterRewardId;
        set => SetProperty(ref setTriggerMasterRewardId, value ?? string.Empty);
    }

    public string SetTriggerMasterRewardTitle
    {
        get => setTriggerMasterRewardTitle;
        set
        {
            if (SetProperty(ref setTriggerMasterRewardTitle, value ?? string.Empty))
            {
                RaisePropertyChanged(nameof(SetTriggerMasterRewardDisplayTitle));
            }
        }
    }

    public string SetTriggerMasterRewardDescription
    {
        get => setTriggerMasterRewardDescription;
        set => SetProperty(ref setTriggerMasterRewardDescription, value ?? string.Empty);
    }

    public int SetTriggerMasterRewardCost
    {
        get => setTriggerMasterRewardCost;
        set => SetProperty(ref setTriggerMasterRewardCost, Math.Max(1, value));
    }

    public TwitchRewardSyncMode SetTriggerMasterRewardSyncMode
    {
        get => setTriggerMasterRewardSyncMode;
        set
        {
            var normalizedValue = Enum.IsDefined(value)
                ? value
                : TwitchRewardSyncMode.CreateOrManage;
            if (SetProperty(ref setTriggerMasterRewardSyncMode, normalizedValue))
            {
                RaisePropertyChanged(nameof(UsesCreateOrManageSetTriggerMasterReward));
                RaisePropertyChanged(nameof(UsesLinkedExistingSetTriggerMasterReward));
                RaisePropertyChanged(nameof(SetTriggerMasterRewardDisplayTitle));
            }
        }
    }

    public bool UsesCreateOrManageSetTriggerMasterReward =>
        SetTriggerMasterRewardSyncMode == TwitchRewardSyncMode.CreateOrManage;

    public bool UsesLinkedExistingSetTriggerMasterReward =>
        SetTriggerMasterRewardSyncMode == TwitchRewardSyncMode.LinkExisting;

    public int SetTriggerMasterRewardCooldownSeconds
    {
        get => setTriggerMasterRewardCooldownSeconds;
        set => SetProperty(ref setTriggerMasterRewardCooldownSeconds, Math.Max(0, value));
    }

    public string SetTriggerMasterRewardReadyColor
    {
        get => setTriggerMasterRewardReadyColor;
        set
        {
            var normalizedValue = ManagedRewardPresentation.NormalizeReadyBackgroundColor(value);
            if (SetProperty(ref setTriggerMasterRewardReadyColor, normalizedValue))
            {
                RaisePropertyChanged(nameof(SetTriggerMasterRewardReadyColorBrush));
            }
        }
    }

    public string SetTriggerMasterRewardCooldownColor
    {
        get => setTriggerMasterRewardCooldownColor;
        set
        {
            var normalizedValue = ManagedRewardPresentation.NormalizeCooldownBackgroundColor(value);
            if (SetProperty(ref setTriggerMasterRewardCooldownColor, normalizedValue))
            {
                RaisePropertyChanged(nameof(SetTriggerMasterRewardCooldownColorBrush));
            }
        }
    }

    public bool DeleteSetTriggerMasterRewardWhenInactive
    {
        get => deleteSetTriggerMasterRewardWhenInactive;
        set => SetProperty(ref deleteSetTriggerMasterRewardWhenInactive, value);
    }

    public bool UseSharedNumberedOutfitReward
    {
        get => useSharedNumberedOutfitReward;
        set
        {
            if (SetProperty(ref useSharedNumberedOutfitReward, value))
            {
                RaisePropertyChanged(nameof(OutfitRewardModeSummary));
            }
        }
    }

public bool PostOutfitChoiceListToTwitchChat
    {
        get => postOutfitChoiceListToTwitchChat;
        set => SetProperty(ref postOutfitChoiceListToTwitchChat, value);
    }

    public bool UseWardrobeMode
    {
        get => useWardrobeMode;
        set
        {
            if (SetProperty(ref useWardrobeMode, value))
            {
                RaisePropertyChanged(nameof(WardrobeModeSummary));
            }
        }
    }

    public int WardrobeCooldownSeconds
    {
        get => wardrobeCooldownSeconds;
        set => SetProperty(ref wardrobeCooldownSeconds, Math.Max(0, value));
    }

    public ObservableCollection<WardrobeOutfit> WardrobeOutfits
    {
        get => wardrobeOutfits;
        set
        {
            if (ReferenceEquals(wardrobeOutfits, value)) return;
            wardrobeOutfits.CollectionChanged -= OnWardrobeOutfitsChanged;
            if (SetProperty(ref wardrobeOutfits, value ?? []))
            {
                wardrobeOutfits.CollectionChanged += OnWardrobeOutfitsChanged;
            }
        }
    }

    public bool UseWardrobeMasterReward
    {
        get => useWardrobeMasterReward;
        set => SetProperty(ref useWardrobeMasterReward, value);
    }

    public string WardrobeMasterRewardId
    {
        get => wardrobeMasterRewardId;
        set => SetProperty(ref wardrobeMasterRewardId, value ?? string.Empty);
    }

    public string WardrobeMasterRewardTitle
    {
        get => wardrobeMasterRewardTitle;
        set => SetProperty(ref wardrobeMasterRewardTitle, value ?? string.Empty);
    }

    public int WardrobeMasterRewardCost
    {
        get => wardrobeMasterRewardCost;
        set => SetProperty(ref wardrobeMasterRewardCost, Math.Max(1, value));
    }

    public TwitchRewardSyncMode WardrobeMasterRewardSyncMode
    {
        get => wardrobeMasterRewardSyncMode;
        set
        {
            var normalizedValue = Enum.IsDefined(value)
                ? value
                : TwitchRewardSyncMode.CreateOrManage;
            if (SetProperty(ref wardrobeMasterRewardSyncMode, normalizedValue))
            {
                RaisePropertyChanged(nameof(UsesCreateOrManageWardrobeMasterReward));
                RaisePropertyChanged(nameof(UsesLinkedExistingWardrobeMasterReward));
            }
        }
    }

    public bool UsesCreateOrManageWardrobeMasterReward =>
        WardrobeMasterRewardSyncMode == TwitchRewardSyncMode.CreateOrManage;

    public bool UsesLinkedExistingWardrobeMasterReward =>
        WardrobeMasterRewardSyncMode == TwitchRewardSyncMode.LinkExisting;

    public int WardrobeMasterRewardCooldownSeconds
    {
        get => wardrobeMasterRewardCooldownSeconds;
        set => SetProperty(ref wardrobeMasterRewardCooldownSeconds, Math.Max(0, value));
    }

    public string WardrobeMasterRewardReadyColor
    {
        get => wardrobeMasterRewardReadyColor;
        set
        {
            var normalizedValue = ManagedRewardPresentation.NormalizeReadyBackgroundColor(value);
            if (SetProperty(ref wardrobeMasterRewardReadyColor, normalizedValue))
            {
                RaisePropertyChanged(nameof(WardrobeMasterRewardReadyColorBrush));
            }
        }
    }

    public string WardrobeMasterRewardCooldownColor
    {
        get => wardrobeMasterRewardCooldownColor;
        set
        {
            var normalizedValue = ManagedRewardPresentation.NormalizeCooldownBackgroundColor(value);
            if (SetProperty(ref wardrobeMasterRewardCooldownColor, normalizedValue))
            {
                RaisePropertyChanged(nameof(WardrobeMasterRewardCooldownColorBrush));
            }
        }
    }

    public Brush WardrobeMasterRewardReadyColorBrush => CreateColorBrush(WardrobeMasterRewardReadyColor);
    public Brush WardrobeMasterRewardCooldownColorBrush => CreateColorBrush(WardrobeMasterRewardCooldownColor);

    public string WardrobeModeSummary => UseWardrobeMode
        ? "Named outfits with mixed Bool/Int/Float snapshots and auto-capture restore."
        : "Numbered outfit choices using Set Trigger. Toggle on to switch to Wardrobe mode.";

    public ObservableCollection<TriggerRule> ChannelPointRules
    {
        get => channelPointRules;
        set
        {
            if (ReferenceEquals(channelPointRules, value))
            {
                return;
            }

            channelPointRules.CollectionChanged -= OnChannelPointRulesChanged;
            if (SetProperty(ref channelPointRules, value ?? []))
            {
                channelPointRules.CollectionChanged += OnChannelPointRulesChanged;
                RaisePropertyChanged(nameof(TriggerCountText));
            }
        }
    }

    public string DisplayTitle
    {
        get
        {
            if (IsMasterProfile)
            {
                return HasAvatarSelected ? AvatarDisplayName : "Return Avatar";
            }

            return !string.IsNullOrWhiteSpace(Name)
                ? Name
                : !string.IsNullOrWhiteSpace(AvatarName)
                    ? AvatarName
                    : "New Avatar Set";
        }
    }

    public bool HasAvatarSelected => !string.IsNullOrWhiteSpace(AvatarId);

    public string AvatarDisplayName => !string.IsNullOrWhiteSpace(AvatarName)
        && !string.Equals(AvatarName.Trim(), AvatarId?.Trim() ?? string.Empty, StringComparison.Ordinal)
        ? AvatarName
        : !string.IsNullOrWhiteSpace(AvatarId)
            ? "Selected avatar"
            : "Pick avatar";

    public string TriggerCountText => ChannelPointRules.Count == 1
        ? "1 redeem"
        : $"{ChannelPointRules.Count} redeems";

    public string MasterStatusText => IsMasterProfile ? "Return Avatar" : string.Empty;

    public string CurrentAvatarStatusText => IsCurrentAvatarActive
        ? "Live now"
        : "Waiting for this avatar";

    public string SetTriggerMasterRewardDisplayTitle => !string.IsNullOrWhiteSpace(SetTriggerMasterRewardTitle)
        ? SetTriggerMasterRewardTitle.Trim()
        : "Set Trigger Master Reward";

    public Brush SetTriggerMasterRewardReadyColorBrush => CreateColorBrush(SetTriggerMasterRewardReadyColor);

    public Brush SetTriggerMasterRewardCooldownColorBrush => CreateColorBrush(SetTriggerMasterRewardCooldownColor);

    public string OutfitRewardModeSummary => UseSharedNumberedOutfitReward
        ? "One Twitch reward asks viewers to type an outfit number."
        : "Each outfit gets its own Twitch reward and does not need viewer input.";

    private static Brush CreateColorBrush(string colorText)
    {
        var brush = new SolidColorBrush((Color)ColorConverter.ConvertFromString(colorText));
        brush.Freeze();
        return brush;
    }

    private void OnChannelPointRulesChanged(object? sender, NotifyCollectionChangedEventArgs e)
    {
        RaisePropertyChanged(nameof(TriggerCountText));
    }

    private void OnWardrobeOutfitsChanged(object? sender, NotifyCollectionChangedEventArgs e)
    {
        // Future: raise aggregate properties if needed
    }
}
