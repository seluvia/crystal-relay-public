using System;
using System.ComponentModel;
using System.Threading;
using System.Threading.Tasks;
using System.Windows.Media.Imaging;
using VrcTwitchOscBridge.Infrastructure;
using VrcTwitchOscBridge.Models;
using VrcTwitchOscBridge.Services;

namespace VrcTwitchOscBridge.ViewModels;

public sealed class InventoryItemSpawnCardViewModel : ObservableObject, IDisposable
{
    private readonly InventoryItemImageService _imageService;
    private CancellationTokenSource? _imageCts;
    private BitmapImage? _thumbnail;
    private bool _disposed;

    public InventoryItemSpawnCardViewModel(
        InventoryItemSpawnRule rule,
        InventoryItemImageService imageService)
    {
        Rule = rule ?? throw new ArgumentNullException(nameof(rule));
        _imageService = imageService ?? throw new ArgumentNullException(nameof(imageService));
        Rule.PropertyChanged += OnRulePropertyChanged;
        _ = LoadThumbnailAsync();
    }

    public InventoryItemSpawnRule Rule { get; }

    public string DisplayTitle => Rule.DisplayTitle;

    public string ItemName => Rule.ItemName;

    public string ItemType => Rule.ItemType;

    public bool IsEnabled => Rule.IsEnabled;

    public int RewardCost => Rule.RewardCost;

    public int CooldownSeconds => Rule.CooldownSeconds;

    public string? SyncStatusBadge => Rule.SyncStatusBadge;

    public string SyncModeLabel => Rule.SyncMode switch
    {
        TwitchRewardSyncMode.CreateOrManage => "Managed",
        TwitchRewardSyncMode.LinkExisting => "Linked",
        _ => "Unknown"
    };

    public BitmapImage? Thumbnail
    {
        get => _thumbnail;
        private set => SetProperty(ref _thumbnail, value);
    }

    private async Task LoadThumbnailAsync()
    {
        _imageCts?.Cancel();
        _imageCts?.Dispose();
        _imageCts = new CancellationTokenSource();

        try
        {
            Thumbnail = await _imageService.LoadImageAsync(Rule.ItemImageUrl, _imageCts.Token);
        }
        catch (OperationCanceledException)
        {
        }
    }

    private void OnRulePropertyChanged(object? sender, PropertyChangedEventArgs e)
    {
        switch (e.PropertyName)
        {
            case nameof(InventoryItemSpawnRule.RewardTitle):
            case nameof(InventoryItemSpawnRule.ItemName):
                RaisePropertyChanged(nameof(DisplayTitle));
                break;
            case nameof(InventoryItemSpawnRule.IsEnabled):
                RaisePropertyChanged(nameof(IsEnabled));
                break;
            case nameof(InventoryItemSpawnRule.RewardCost):
                RaisePropertyChanged(nameof(RewardCost));
                break;
            case nameof(InventoryItemSpawnRule.CooldownSeconds):
                RaisePropertyChanged(nameof(CooldownSeconds));
                break;
            case nameof(InventoryItemSpawnRule.SyncMode):
                RaisePropertyChanged(nameof(SyncModeLabel));
                RaisePropertyChanged(nameof(SyncStatusBadge));
                break;
            case nameof(InventoryItemSpawnRule.RewardId):
                RaisePropertyChanged(nameof(SyncStatusBadge));
                break;
            case nameof(InventoryItemSpawnRule.ItemType):
                RaisePropertyChanged(nameof(ItemType));
                break;
            case nameof(InventoryItemSpawnRule.ItemImageUrl):
                _ = LoadThumbnailAsync();
                break;
        }
    }

    public void Dispose()
    {
        if (!_disposed)
        {
            _disposed = true;
            _imageCts?.Cancel();
            _imageCts?.Dispose();
            Rule.PropertyChanged -= OnRulePropertyChanged;
        }
    }
}
