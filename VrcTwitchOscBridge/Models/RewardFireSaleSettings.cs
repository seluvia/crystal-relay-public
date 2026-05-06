using System.Collections.ObjectModel;
using System.Windows.Media;
using VrcTwitchOscBridge.Infrastructure;
using VrcTwitchOscBridge.Services;
using Brush = System.Windows.Media.Brush;

namespace VrcTwitchOscBridge.Models;

public enum RewardFireSaleMode
{
    Temporary,
    Permanent
}

public sealed class RewardFireSaleTier : ObservableObject
{
    private Guid id = Guid.NewGuid();
    private int goalAmount = 5000;
    private int discountPercent = 25;

    public Guid Id
    {
        get => id;
        set => SetProperty(ref id, value == Guid.Empty ? Guid.NewGuid() : value);
    }

    public int GoalAmount
    {
        get => goalAmount;
        set
        {
            if (SetProperty(ref goalAmount, Math.Max(1, value)))
            {
                RaisePropertyChanged(nameof(DisplaySummary));
            }
        }
    }

    public int DiscountPercent
    {
        get => discountPercent;
        set
        {
            if (SetProperty(ref discountPercent, Math.Clamp(value, 1, 100)))
            {
                RaisePropertyChanged(nameof(DisplaySummary));
            }
        }
    }

    public string DisplaySummary => $"{GoalAmount:N0} goal = {DiscountPercent}% off";
}

public sealed class RewardFireSaleSettings : ObservableObject
{
    private bool isEnabled;
    private bool countBits = true;
    private bool countManagedRewards = true;
    private bool fundingRewardEnabled;
    private string fundingRewardId = string.Empty;
    private string fundingRewardTitle = "Fire Sale Fund";
    private string fundingRewardDescription = string.Empty;
    private int fundingRewardCost = 100;
    private int fundingRewardCooldownSeconds;
    private string fundingRewardReadyColor = ManagedRewardPresentation.ReadyBackgroundColor;
    private string fundingRewardCooldownColor = ManagedRewardPresentation.InUseBackgroundColor;
    private int rewardPointsPerProgressUnit = 10;
    private bool multiTierEnabled = true;
    private ObservableCollection<RewardFireSaleTier> tiers =
    [
        new RewardFireSaleTier()
    ];
    private RewardFireSaleMode saleMode = RewardFireSaleMode.Temporary;
    private int temporaryDurationSeconds = 300;
    private long currentProgress;
    private bool isSaleActive;
    private int activeDiscountPercent;
    private int activeTierGoalAmount;
    private DateTimeOffset? activeUntilUtc;

    public bool IsEnabled
    {
        get => isEnabled;
        set => SetProperty(ref isEnabled, value);
    }

    public bool CountBits
    {
        get => countBits;
        set => SetProperty(ref countBits, value);
    }

    public bool CountManagedRewards
    {
        get => countManagedRewards;
        set => SetProperty(ref countManagedRewards, value);
    }

    public bool FundingRewardEnabled
    {
        get => fundingRewardEnabled;
        set => SetProperty(ref fundingRewardEnabled, value);
    }

    public string FundingRewardId
    {
        get => fundingRewardId;
        set => SetProperty(ref fundingRewardId, value?.Trim() ?? string.Empty);
    }

    public string FundingRewardTitle
    {
        get => fundingRewardTitle;
        set => SetProperty(ref fundingRewardTitle, string.IsNullOrWhiteSpace(value) ? "Fire Sale Fund" : value.Trim());
    }

    public string FundingRewardDescription
    {
        get => fundingRewardDescription;
        set => SetProperty(ref fundingRewardDescription, value ?? string.Empty);
    }

    public int FundingRewardCost
    {
        get => fundingRewardCost;
        set => SetProperty(ref fundingRewardCost, Math.Max(1, value));
    }

    public int FundingRewardCooldownSeconds
    {
        get => fundingRewardCooldownSeconds;
        set => SetProperty(ref fundingRewardCooldownSeconds, Math.Max(0, value));
    }

    public string FundingRewardReadyColor
    {
        get => fundingRewardReadyColor;
        set
        {
            var normalizedValue = ManagedRewardPresentation.NormalizeReadyBackgroundColor(value);
            if (SetProperty(ref fundingRewardReadyColor, normalizedValue))
            {
                RaisePropertyChanged(nameof(FundingRewardReadyColorBrush));
            }
        }
    }

    public string FundingRewardCooldownColor
    {
        get => fundingRewardCooldownColor;
        set
        {
            var normalizedValue = ManagedRewardPresentation.NormalizeCooldownBackgroundColor(value);
            if (SetProperty(ref fundingRewardCooldownColor, normalizedValue))
            {
                RaisePropertyChanged(nameof(FundingRewardCooldownColorBrush));
            }
        }
    }

    public int RewardPointsPerProgressUnit
    {
        get => rewardPointsPerProgressUnit;
        set => SetProperty(ref rewardPointsPerProgressUnit, Math.Max(1, value));
    }

    public bool MultiTierEnabled
    {
        get => multiTierEnabled;
        set => SetProperty(ref multiTierEnabled, value);
    }

    public ObservableCollection<RewardFireSaleTier> Tiers
    {
        get => tiers;
        set => SetProperty(ref tiers, value ?? []);
    }

    public RewardFireSaleMode SaleMode
    {
        get => saleMode;
        set => SetProperty(ref saleMode, Enum.IsDefined(value) ? value : RewardFireSaleMode.Temporary);
    }

    public int TemporaryDurationSeconds
    {
        get => temporaryDurationSeconds;
        set => SetProperty(ref temporaryDurationSeconds, Math.Max(1, value));
    }

    public long CurrentProgress
    {
        get => currentProgress;
        set
        {
            if (SetProperty(ref currentProgress, Math.Max(0, value)))
            {
                RaisePropertyChanged(nameof(CurrentProgressText));
            }
        }
    }

    public bool IsSaleActive
    {
        get => isSaleActive;
        set => SetProperty(ref isSaleActive, value);
    }

    public int ActiveDiscountPercent
    {
        get => activeDiscountPercent;
        set => SetProperty(ref activeDiscountPercent, Math.Clamp(value, 0, 100));
    }

    public int ActiveTierGoalAmount
    {
        get => activeTierGoalAmount;
        set => SetProperty(ref activeTierGoalAmount, Math.Max(0, value));
    }

    public DateTimeOffset? ActiveUntilUtc
    {
        get => activeUntilUtc;
        set => SetProperty(ref activeUntilUtc, value);
    }

    public string CurrentProgressText => $"{CurrentProgress:N0}";

    public Brush FundingRewardReadyColorBrush => CreateColorBrush(FundingRewardReadyColor);

    public Brush FundingRewardCooldownColorBrush => CreateColorBrush(FundingRewardCooldownColor);

    private static Brush CreateColorBrush(string colorText)
    {
        var brush = new SolidColorBrush((Color)ColorConverter.ConvertFromString(colorText));
        brush.Freeze();
        return brush;
    }
}
