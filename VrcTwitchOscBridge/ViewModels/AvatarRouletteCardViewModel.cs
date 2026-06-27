using System;
using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Linq;
using VrcTwitchOscBridge.Infrastructure;
using VrcTwitchOscBridge.Models;
using VrcTwitchOscBridge.Services;

namespace VrcTwitchOscBridge.ViewModels;

public sealed class AvatarRouletteCardViewModel : ObservableObject, IDisposable
{
    private readonly AvatarImageService _imageService;

    public AvatarRouletteCardViewModel(AvatarRouletteProfile roulette, AvatarImageService? imageService = null)
    {
        Roulette = roulette ?? throw new ArgumentNullException(nameof(roulette));
        _imageService = imageService ?? new AvatarImageService();
        Roulette.PropertyChanged += OnRoulettePropertyChanged;
        RebuildPreviewRows();
    }

    public AvatarRouletteProfile Roulette { get; }

    public string Name => Roulette.Name;

    public string Subtitle => Roulette.Subtitle;

    public int PoolCount => Roulette.PoolCount;

    public int TriggerCount => Roulette.TriggerCount;

    public ObservableCollection<RoulettePoolEntryRowViewModel> PreviewRows { get; } = new();

    public void Dispose()
    {
        Roulette.PropertyChanged -= OnRoulettePropertyChanged;
    }

    private void OnRoulettePropertyChanged(object? sender, PropertyChangedEventArgs e)
    {
        switch (e.PropertyName)
        {
            case nameof(AvatarRouletteProfile.Name):
                RaisePropertyChanged(nameof(Name));
                break;
            case nameof(AvatarRouletteProfile.IsEnabled):
                break;
            case nameof(AvatarRouletteProfile.PoolCount):
                RebuildPreviewRows();
                RaisePropertyChanged(nameof(PoolCount));
                RaisePropertyChanged(nameof(Subtitle));
                break;
            case nameof(AvatarRouletteProfile.TriggerCount):
            case nameof(AvatarRouletteProfile.Subtitle):
                RaisePropertyChanged(nameof(Subtitle));
                RaisePropertyChanged(nameof(TriggerCount));
                break;
        }
    }

    private void RebuildPreviewRows()
    {
        PreviewRows.Clear();
        foreach (var entry in Roulette.Pool.Take(4))
        {
            PreviewRows.Add(new RoulettePoolEntryRowViewModel(entry, _imageService));
        }
    }
}
