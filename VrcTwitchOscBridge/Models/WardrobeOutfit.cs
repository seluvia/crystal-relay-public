using System.Collections.ObjectModel;
using System.Collections.Specialized;
using VrcTwitchOscBridge.Infrastructure;
using VrcTwitchOscBridge.Services;

namespace VrcTwitchOscBridge.Models;

public sealed class WardrobeOutfit : ObservableObject
{
    private Guid id = Guid.NewGuid();
    private bool isEnabled = true;
    private string name = "New Outfit";
    private int activeTimeSeconds = 30;
    private string twitchRewardId = string.Empty;
    private string twitchRewardTitle = string.Empty;
    private TwitchRewardSyncMode twitchRewardSyncMode = TwitchRewardSyncMode.CreateOrManage;
    private string chatCommandText = string.Empty;
    private ObservableCollection<WardrobeSnapshotParam> snapshotParams = [];

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
        set => SetProperty(ref activeTimeSeconds, Math.Max(1, value));
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

    public ObservableCollection<WardrobeSnapshotParam> SnapshotParams
    {
        get => snapshotParams;
        set
        {
            if (ReferenceEquals(snapshotParams, value)) return;
            snapshotParams.CollectionChanged -= OnSnapshotParamsChanged;
            if (SetProperty(ref snapshotParams, value ?? []))
            {
                snapshotParams.CollectionChanged += OnSnapshotParamsChanged;
                RaisePropertyChanged(nameof(ParamCountText));
                RaisePropertyChanged(nameof(DisplaySummary));
            }
        }
    }

    public string DisplayTitle => !string.IsNullOrWhiteSpace(Name) ? Name : "New Outfit";
    public string DisplaySummary => TF("{0} ({1} param{2})", DisplayTitle, SnapshotParams.Count, SnapshotParams.Count == 1 ? string.Empty : "s");
    public string ParamCountText => SnapshotParams.Count == 1 ? "1 param" : $"{SnapshotParams.Count} params";

    private void OnSnapshotParamsChanged(object? sender, NotifyCollectionChangedEventArgs e)
    {
        RaisePropertyChanged(nameof(ParamCountText));
        RaisePropertyChanged(nameof(DisplaySummary));
    }
}