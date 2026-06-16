using System;
using System.Collections.ObjectModel;
using System.Collections.Specialized;
using System.Windows.Media;
using VrcTwitchOscBridge.Infrastructure;

namespace VrcTwitchOscBridge.Models;

public sealed class AvatarSwapProfile : ObservableObject
{
    private Guid id = Guid.NewGuid();
    private string targetAvatarId = string.Empty;
    private string targetAvatarName = string.Empty;
    private string? targetThumbnailUrl;
    private ReturnAvatarMode returnAvatarMode = ReturnAvatarMode.UseGlobal;
    private string? returnAvatarId;
    private string? returnAvatarName;
    private bool isEnabled = true;
    private DateTime createdAt = DateTime.UtcNow;
    private DateTime updatedAt = DateTime.UtcNow;
    private ObservableCollection<TriggerRule> channelPointRules = [];
    private ObservableCollection<TriggerRule> bitsSubsRules = [];
    private ObservableCollection<TriggerRule> rouletteRules = [];

    public AvatarSwapProfile()
    {
        channelPointRules.CollectionChanged += OnCollectionChanged;
        bitsSubsRules.CollectionChanged += OnCollectionChanged;
        RouletteRules.CollectionChanged += OnCollectionChanged;
    }

    public Guid Id
    {
        get => id;
        set => SetProperty(ref id, value);
    }

    public string TargetAvatarId
    {
        get => targetAvatarId;
        set
        {
            if (SetProperty(ref targetAvatarId, value ?? string.Empty))
            {
                RaisePropertyChanged(nameof(DisplayTitle));
                RaisePropertyChanged(nameof(HasTarget));
            }
        }
    }

    public string TargetAvatarName
    {
        get => targetAvatarName;
        set
        {
            if (SetProperty(ref targetAvatarName, value ?? string.Empty))
            {
                RaisePropertyChanged(nameof(DisplayTitle));
            }
        }
    }

    public string? TargetThumbnailUrl
    {
        get => targetThumbnailUrl;
        set => SetProperty(ref targetThumbnailUrl, value);
    }

    public ReturnAvatarMode ReturnAvatarMode
    {
        get => returnAvatarMode;
        set
        {
            if (SetProperty(ref returnAvatarMode, value))
            {
                RaisePropertyChanged(nameof(ReturnAvatarDisplay));
            }
        }
    }

    public string? ReturnAvatarId
    {
        get => returnAvatarId;
        set => SetProperty(ref returnAvatarId, value);
    }

    public string? ReturnAvatarName
    {
        get => returnAvatarName;
        set
        {
            if (SetProperty(ref returnAvatarName, value))
            {
                RaisePropertyChanged(nameof(ReturnAvatarDisplay));
            }
        }
    }

    public bool IsEnabled
    {
        get => isEnabled;
        set
        {
            if (SetProperty(ref isEnabled, value))
            {
                RaisePropertyChanged(nameof(StatusText));
            }
        }
    }

    public DateTime CreatedAt
    {
        get => createdAt;
        set => SetProperty(ref createdAt, value);
    }

    public DateTime UpdatedAt
    {
        get => updatedAt;
        set => SetProperty(ref updatedAt, value);
    }

    public ObservableCollection<TriggerRule> ChannelPointRules
    {
        get => channelPointRules;
        set
        {
            if (channelPointRules is not null)
            {
                channelPointRules.CollectionChanged -= OnCollectionChanged;
            }
            SetProperty(ref channelPointRules, value ?? []);
            if (channelPointRules is not null)
            {
                channelPointRules.CollectionChanged += OnCollectionChanged;
            }
            RaisePropertyChanged(nameof(HasRules));
            RaisePropertyChanged(nameof(UsesChannelPointRules));
            RaisePropertyChanged(nameof(AvatarSubtitle));
        }
    }

    public ObservableCollection<TriggerRule> BitsSubsRules
    {
        get => bitsSubsRules;
        set
        {
            if (bitsSubsRules is not null)
            {
                bitsSubsRules.CollectionChanged -= OnCollectionChanged;
            }
            SetProperty(ref bitsSubsRules, value ?? []);
            if (bitsSubsRules is not null)
            {
                bitsSubsRules.CollectionChanged += OnCollectionChanged;
            }
            RaisePropertyChanged(nameof(HasRules));
            RaisePropertyChanged(nameof(UsesBitsSubsRules));
            RaisePropertyChanged(nameof(AvatarSubtitle));
        }
    }

    public ObservableCollection<TriggerRule> RouletteRules
    {
        get => rouletteRules;
        set
        {
            if (rouletteRules is not null)
            {
                rouletteRules.CollectionChanged -= OnCollectionChanged;
            }
            SetProperty(ref rouletteRules, value ?? []);
            if (rouletteRules is not null)
            {
                rouletteRules.CollectionChanged += OnCollectionChanged;
            }
            RaisePropertyChanged(nameof(HasRules));
            RaisePropertyChanged(nameof(UsesRouletteRules));
            RaisePropertyChanged(nameof(AvatarSubtitle));
        }
    }

    public string DisplayTitle => string.IsNullOrWhiteSpace(TargetAvatarName)
        ? (string.IsNullOrWhiteSpace(TargetAvatarId) ? "New Avatar Swap" : TargetAvatarId)
        : TargetAvatarName;

    public bool HasTarget => !string.IsNullOrWhiteSpace(TargetAvatarId);

    public string AvatarSubtitle =>
        $"{ChannelPointRules.Count} channel-point, {BitsSubsRules.Count} bits/subs, {RouletteRules.Count} roulette";

    public bool HasRules => ChannelPointRules.Count + BitsSubsRules.Count + RouletteRules.Count > 0;

    public bool UsesChannelPointRules => (ChannelPointRules?.Count ?? 0) > 0;

    public bool UsesBitsSubsRules => (BitsSubsRules?.Count ?? 0) > 0;

    public bool UsesRouletteRules => RouletteRules.Count > 0;

    public string ReturnAvatarDisplay => ReturnAvatarMode switch
    {
        ReturnAvatarMode.UseGlobal => "Global return",
        ReturnAvatarMode.UseCustom => string.IsNullOrWhiteSpace(ReturnAvatarName)
            ? "Custom return"
            : $"Returns to {ReturnAvatarName}",
        ReturnAvatarMode.SameAsTarget => "One-way swap",
        _ => string.Empty
    };

    public string StatusText => IsEnabled ? "Ready" : "Disabled";

    public SolidColorBrush StatusStripeReadyBrush { get; } = CreateFrozenBrush("#4ADE80");

    public SolidColorBrush StatusStripeWarnBrush { get; } = CreateFrozenBrush("#FBBF24");

    public SolidColorBrush StatusStripeOffBrush { get; } = CreateFrozenBrush("#6B7280");

    private static SolidColorBrush CreateFrozenBrush(string hex)
    {
        try
        {
            var color = (Color)ColorConverter.ConvertFromString(hex);
            var brush = new SolidColorBrush(color);
            brush.Freeze();
            return brush;
        }
        catch
        {
            return Brushes.Gray;
        }
    }

    private void OnCollectionChanged(object? sender, NotifyCollectionChangedEventArgs e)
    {
        RaisePropertyChanged(nameof(HasRules));
        RaisePropertyChanged(nameof(UsesChannelPointRules));
        RaisePropertyChanged(nameof(UsesBitsSubsRules));
        RaisePropertyChanged(nameof(UsesRouletteRules));
        RaisePropertyChanged(nameof(AvatarSubtitle));
        UpdatedAt = DateTime.UtcNow;
    }
}
