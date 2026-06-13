using System.Collections.ObjectModel;
using System.Collections.Specialized;
using System.ComponentModel;
using VrcTwitchOscBridge.Infrastructure;
using VrcTwitchOscBridge.Services;
using System.Windows.Media;
using Brush = System.Windows.Media.Brush;

namespace VrcTwitchOscBridge.Models;

public sealed class WardrobeOutfit : ObservableObject
{
    public const int SafeObservationSeconds = 70;

    private Guid id = Guid.NewGuid();
    private bool isEnabled = true;
    private string name = "New Outfit";
    private int activeTimeSeconds = SafeObservationSeconds;
    private string twitchRewardId = string.Empty;
    private string twitchRewardTitle = string.Empty;
    private string twitchRewardCost = "100";
    private string twitchRewardDescription = string.Empty;
    private TwitchRewardSyncMode twitchRewardSyncMode = TwitchRewardSyncMode.CreateOrManage;
    private string chatCommandText = string.Empty;
    private ObservableCollection<WardrobeSnapshotParam> snapshotParams = [];
    private string managedRewardReadyColor = ManagedRewardPresentation.ReadyBackgroundColor;
    private string managedRewardCooldownColor = ManagedRewardPresentation.InUseBackgroundColor;

    private static string T(string sourceText) => LocalizationService.Translate(sourceText);
    private static string TF(string sourceFormat, params object[] args) => LocalizationService.Format(sourceFormat, args);

    public WardrobeOutfit()
    {
        snapshotParams.CollectionChanged += OnSnapshotParamsChanged;
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
                RaisePropertyChanged(nameof(DisplayTitle));
                RaisePropertyChanged(nameof(DisplaySummary));
            }
        }
    }

    public int ActiveTimeSeconds
    {
        get => activeTimeSeconds;
        set
        {
            if (SetProperty(ref activeTimeSeconds, Math.Max(1, value)))
            {
                RaisePropertyChanged(nameof(UsesShortActiveTime));
            }
        }
    }

    public string TwitchRewardId
    {
        get => twitchRewardId;
        set => SetProperty(ref twitchRewardId, value ?? string.Empty);
    }

    public string TwitchRewardTitle
    {
        get => twitchRewardTitle;
        set
        {
            if (SetProperty(ref twitchRewardTitle, value ?? string.Empty))
            {
                RaisePropertyChanged(nameof(DisplayTitle));
            }
        }
    }

    public string TwitchRewardCost
    {
        get => twitchRewardCost;
        set => SetProperty(ref twitchRewardCost, value ?? string.Empty);
    }

    public string TwitchRewardDescription
    {
        get => twitchRewardDescription;
        set => SetProperty(ref twitchRewardDescription, value ?? string.Empty);
    }

    public TwitchRewardSyncMode TwitchRewardSyncMode
    {
        get => twitchRewardSyncMode;
        set
        {
            var normalizedValue = Enum.IsDefined(value)
                ? value
                : TwitchRewardSyncMode.CreateOrManage;
            if (SetProperty(ref twitchRewardSyncMode, normalizedValue))
            {
                RaisePropertyChanged(nameof(UsesCreateOrManageReward));
                RaisePropertyChanged(nameof(UsesLinkedExistingReward));
            }
        }
    }

    public bool UsesCreateOrManageReward => TwitchRewardSyncMode == TwitchRewardSyncMode.CreateOrManage;
    public bool UsesLinkedExistingReward => TwitchRewardSyncMode == TwitchRewardSyncMode.LinkExisting;

    public string ChatCommandText
    {
        get => chatCommandText;
        set => SetProperty(ref chatCommandText, value ?? string.Empty);
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

    public Brush ManagedRewardReadyColorBrush => CreateColorBrush(ManagedRewardReadyColor);
    public Brush ManagedRewardCooldownColorBrush => CreateColorBrush(ManagedRewardCooldownColor);

    public ObservableCollection<WardrobeSnapshotParam> SnapshotParams
    {
        get => snapshotParams;
        set
        {
            if (ReferenceEquals(snapshotParams, value)) return;
            snapshotParams.CollectionChanged -= OnSnapshotParamsChanged;
            UnwireSnapshotParams(snapshotParams);
            var normalizedValue = value ?? [];
            if (SetProperty(ref snapshotParams, normalizedValue))
            {
                WireSnapshotParams(snapshotParams);
                snapshotParams.CollectionChanged += OnSnapshotParamsChanged;
                RaisePropertyChanged(nameof(SnapshotParams));
                RaisePropertyChanged(nameof(ParamCountText));
                RaisePropertyChanged(nameof(DisplaySummary));
            }
        }
    }

    public string DisplayTitle => !string.IsNullOrWhiteSpace(Name) ? Name : "New Outfit";
    public string DisplaySummary => TF("{0} ({1} param{2})", DisplayTitle, SnapshotParams.Count, SnapshotParams.Count == 1 ? string.Empty : "s");
    public string ParamCountText => SnapshotParams.Count == 1 ? "1 param" : $"{SnapshotParams.Count} params";
    public bool UsesShortActiveTime => ActiveTimeSeconds < SafeObservationSeconds;

    private void OnSnapshotParamsChanged(object? sender, NotifyCollectionChangedEventArgs e)
    {
        if (e.OldItems is not null)
        {
            foreach (WardrobeSnapshotParam param in e.OldItems)
            {
                param.PropertyChanged -= SnapshotParamChanged;
            }
        }

        if (e.NewItems is not null)
        {
            foreach (WardrobeSnapshotParam param in e.NewItems)
            {
                param.PropertyChanged += SnapshotParamChanged;
            }
        }

        RaisePropertyChanged(nameof(SnapshotParams));
        RaisePropertyChanged(nameof(ParamCountText));
        RaisePropertyChanged(nameof(DisplaySummary));
    }

    private void SnapshotParamChanged(object? sender, PropertyChangedEventArgs e)
    {
        RaisePropertyChanged(nameof(SnapshotParams));
    }

    private void WireSnapshotParams(IEnumerable<WardrobeSnapshotParam> parameters)
    {
        foreach (var parameter in parameters)
        {
            parameter.PropertyChanged += SnapshotParamChanged;
        }
    }

    private void UnwireSnapshotParams(IEnumerable<WardrobeSnapshotParam> parameters)
    {
        foreach (var parameter in parameters)
        {
            parameter.PropertyChanged -= SnapshotParamChanged;
        }
    }

    private static Brush CreateColorBrush(string colorText)
    {
        var brush = new SolidColorBrush((Color)ColorConverter.ConvertFromString(colorText));
        brush.Freeze();
        return brush;
    }
}
