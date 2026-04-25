using System;
using System.Collections.ObjectModel;
using System.Collections.Specialized;
using VrcTwitchOscBridge.Infrastructure;

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
    private ObservableCollection<TriggerRule> channelPointRules = [];

    public AvatarTriggerProfile()
    {
        channelPointRules.CollectionChanged += OnChannelPointRulesChanged;
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

    public string DisplayTitle => !string.IsNullOrWhiteSpace(Name)
        ? Name
        : !string.IsNullOrWhiteSpace(AvatarName)
            ? AvatarName
            : "New Avatar Set";

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

    private void OnChannelPointRulesChanged(object? sender, NotifyCollectionChangedEventArgs e)
    {
        RaisePropertyChanged(nameof(TriggerCountText));
    }
}
