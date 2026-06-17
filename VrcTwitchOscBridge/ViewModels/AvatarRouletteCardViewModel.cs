using System;
using VrcTwitchOscBridge.Infrastructure;
using VrcTwitchOscBridge.Models;

namespace VrcTwitchOscBridge.ViewModels;

public sealed class AvatarRouletteCardViewModel : ObservableObject
{
    public AvatarRouletteCardViewModel(AvatarRouletteProfile roulette)
    {
        Roulette = roulette ?? throw new ArgumentNullException(nameof(roulette));
    }

    public AvatarRouletteProfile Roulette { get; }

    public string Name => Roulette.Name;

    public string Subtitle => Roulette.Subtitle;

    public int PoolCount => Roulette.PoolCount;

    public int TriggerCount => Roulette.TriggerCount;
}
