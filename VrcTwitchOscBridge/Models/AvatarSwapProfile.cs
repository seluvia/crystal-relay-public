using System.Collections.ObjectModel;
using System;
using VrcTwitchOscBridge.Infrastructure;

namespace VrcTwitchOscBridge.Models;

public partial class AvatarSwapProfile : ObservableObject
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public string TargetAvatarId { get; set; } = string.Empty;
    public string TargetAvatarName { get; set; } = string.Empty;
    public string? TargetThumbnailUrl { get; set; }
    public bool IsEnabled { get; set; } = true;
    private bool bitsMaxSwapTimeEnabled;
    private bool subsMaxSwapTimeEnabled;
    private int maxSwapTimeSeconds = 1800;

    public bool BitsMaxSwapTimeEnabled
    {
        get => bitsMaxSwapTimeEnabled;
        set
        {
            if (SetProperty(ref bitsMaxSwapTimeEnabled, value))
            {
                RaisePropertyChanged(nameof(AnySwapTimeCapEnabled));
            }
        }
    }

    public bool SubsMaxSwapTimeEnabled
    {
        get => subsMaxSwapTimeEnabled;
        set
        {
            if (SetProperty(ref subsMaxSwapTimeEnabled, value))
            {
                RaisePropertyChanged(nameof(AnySwapTimeCapEnabled));
            }
        }
    }

    public int MaxSwapTimeSeconds
    {
        get => maxSwapTimeSeconds;
        set => SetProperty(ref maxSwapTimeSeconds, value);
    }

    public bool AnySwapTimeCapEnabled => BitsMaxSwapTimeEnabled || SubsMaxSwapTimeEnabled;

    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
    public DateTime UpdatedAt { get; set; } = DateTime.UtcNow;

    public ObservableCollection<TriggerRule> ChannelPointRules { get; } = new();
    public ObservableCollection<TriggerRule> BitsRules { get; } = new();
    public ObservableCollection<TriggerRule> SubsRules { get; } = new();
    public ObservableCollection<CashPaymentRule> PaymentRules { get; } = new();

    public bool HasRules =>
        ChannelPointRules.Count + BitsRules.Count + SubsRules.Count + PaymentRules.Count > 0;
    public bool UsesChannelPointRules => ChannelPointRules.Count > 0;
    public bool UsesBitsRules => BitsRules.Count > 0;
    public bool UsesSubsRules => SubsRules.Count > 0;
    public bool UsesPaymentRules => PaymentRules.Count > 0;

    public string AvatarSubtitle =>
        $"{ChannelPointRules.Count} cp · {BitsRules.Count} bits · {SubsRules.Count} subs · {PaymentRules.Count} pay";

    public AvatarSwapProfile()
    {
        ChannelPointRules.CollectionChanged += (_, _) => Bump();
        BitsRules.CollectionChanged += (_, _) => Bump();
        SubsRules.CollectionChanged += (_, _) => Bump();
        PaymentRules.CollectionChanged += (_, _) => Bump();
    }

    private void Bump()
    {
        UpdatedAt = DateTime.UtcNow;
        RaisePropertyChanged(nameof(HasRules));
        RaisePropertyChanged(nameof(UsesChannelPointRules));
        RaisePropertyChanged(nameof(UsesBitsRules));
        RaisePropertyChanged(nameof(UsesSubsRules));
        RaisePropertyChanged(nameof(UsesPaymentRules));
        RaisePropertyChanged(nameof(AvatarSubtitle));
    }
}
