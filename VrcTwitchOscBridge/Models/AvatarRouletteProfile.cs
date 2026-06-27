using System;
using System.Collections.ObjectModel;
using VrcTwitchOscBridge.Infrastructure;

namespace VrcTwitchOscBridge.Models;

public partial class AvatarRouletteProfile : ObservableObject
{
    public Guid Id { get; set; } = Guid.NewGuid();
    private string name = "New Roulette";
    public string Name
    {
        get => name;
        set => SetProperty(ref name, value);
    }
    private bool isEnabled = true;
    public bool IsEnabled
    {
        get => isEnabled;
        set => SetProperty(ref isEnabled, value);
    }
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
    public DateTime UpdatedAt { get; set; } = DateTime.UtcNow;

    public ObservableCollection<RouletteAvatarEntry> Pool { get; } = new();
    public string? ReturnAvatarId { get; set; }
    public string? ReturnAvatarName { get; set; }
    public ObservableCollection<TriggerRule> Triggers { get; } = new();

    public int PoolCount => Pool.Count;
    public int TriggerCount => Triggers.Count;
    public string Subtitle =>
        $"🎲 {PoolCount} pool · {TriggerCount} trigger{(TriggerCount == 1 ? "" : "s")}";

    public AvatarRouletteProfile()
    {
        Pool.CollectionChanged += (_, _) =>
        {
            UpdatedAt = DateTime.UtcNow;
            RaisePropertyChanged(nameof(PoolCount));
            RaisePropertyChanged(nameof(Subtitle));
        };
        Triggers.CollectionChanged += (_, _) =>
        {
            UpdatedAt = DateTime.UtcNow;
            RaisePropertyChanged(nameof(TriggerCount));
            RaisePropertyChanged(nameof(Subtitle));
        };
    }
}

public partial class RouletteAvatarEntry : ObservableObject
{
    public string AvatarId { get; set; } = string.Empty;
    public string AvatarName { get; set; } = string.Empty;
    public string? ThumbnailUrl { get; set; }
}
