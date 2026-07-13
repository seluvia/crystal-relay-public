using System.ComponentModel;
using VrcTwitchOscBridge.Infrastructure;
using VrcTwitchOscBridge.Models;
using VrcTwitchOscBridge.Services;

namespace VrcTwitchOscBridge.ViewModels;

public enum AvatarScalingSourceKind
{
    MasterReward,
    TwitchReward,
    TwitchEvent,
    SupporterGrowth,
    CashPayment,
    PowerUp
}

public enum AvatarScalingCardStatus
{
    Ready,
    NeedsSetup,
    Disabled
}

public sealed class AvatarScalingSourceCardViewModel : ObservableObject, IDisposable
{
    private readonly AvatarScaleSafetySettings safety;
    private AvatarScaleRule? cashPaymentScaleAction;
    private AvatarScaleRule? powerUpScaleAction;
    private bool disposed;

    public AvatarScalingSourceCardViewModel(
        AvatarScalingSourceKind kind,
        AvatarScaleSafetySettings safety,
        AvatarScaleRule? scaleRule = null,
        AvatarScaleMasterRewardSettings? masterReward = null,
        CashPaymentRule? cashPaymentRule = null,
        PowerUpRule? powerUpRule = null)
    {
        Kind = kind;
        this.safety = safety ?? throw new ArgumentNullException(nameof(safety));
        ScaleRule = scaleRule;
        MasterReward = masterReward;
        CashPaymentRule = cashPaymentRule;
        PowerUpRule = powerUpRule;

        this.safety.PropertyChanged += OnSafetyPropertyChanged;
        if (ScaleRule is not null)
        {
            ScaleRule.PropertyChanged += OnSourcePropertyChanged;
        }

        if (MasterReward is not null)
        {
            MasterReward.PropertyChanged += OnSourcePropertyChanged;
        }

        if (CashPaymentRule is not null)
        {
            CashPaymentRule.PropertyChanged += OnCashPaymentRulePropertyChanged;
            WireCashPaymentScaleAction(CashPaymentRule.ScaleAction);
        }

        if (PowerUpRule is not null)
        {
            PowerUpRule.PropertyChanged += OnPowerUpRulePropertyChanged;
            WirePowerUpScaleAction(PowerUpRule.ScaleAction);
        }
    }

    public AvatarScalingSourceKind Kind { get; }

    public AvatarScaleRule? ScaleRule { get; }

    public AvatarScaleMasterRewardSettings? MasterReward { get; }

    public CashPaymentRule? CashPaymentRule { get; }

    public PowerUpRule? PowerUpRule { get; }

    public string Title => Kind switch
    {
        AvatarScalingSourceKind.MasterReward => string.IsNullOrWhiteSpace(MasterReward?.RewardTitle)
            ? LocalizationService.Translate("Master Unlock Reward")
            : MasterReward!.RewardTitle,
        AvatarScalingSourceKind.CashPayment => CashPaymentRule?.DisplayTitle ?? LocalizationService.Translate("Cash Payment Scaling"),
        AvatarScalingSourceKind.PowerUp => PowerUpRule?.DisplayTitle ?? LocalizationService.Translate("Power Up Scaling"),
        _ => ScaleRule?.DisplayTitle ?? LocalizationService.Translate("Avatar Scale")
    };

    public string SourcePill => Kind switch
    {
        AvatarScalingSourceKind.MasterReward => LocalizationService.Translate("Master"),
        AvatarScalingSourceKind.TwitchReward => LocalizationService.Translate("Reward"),
        AvatarScalingSourceKind.TwitchEvent => LocalizationService.Translate("Twitch Event"),
        AvatarScalingSourceKind.SupporterGrowth => LocalizationService.Translate("Supporter Growth"),
        AvatarScalingSourceKind.CashPayment => LocalizationService.Translate("Cash"),
        AvatarScalingSourceKind.PowerUp => LocalizationService.Translate("Power Up"),
        _ => string.Empty
    };

    public AvatarScalingCardStatus Status
    {
        get
        {
            if (ScaleRule is { IsEnabled: false }
                || MasterReward is { IsEnabled: false }
                || CashPaymentRule is { IsEnabled: false }
                || CashPaymentRule?.ScaleAction.IsEnabled == false
                || PowerUpRule is { IsEnabled: false }
                || PowerUpRule?.ScaleAction.IsEnabled == false)
            {
                return AvatarScalingCardStatus.Disabled;
            }

            if (Kind == AvatarScalingSourceKind.TwitchReward
                && ScaleRule is { UsesChannelPointReward: true }
                && string.IsNullOrWhiteSpace(ScaleRule.RewardId)
                && string.IsNullOrWhiteSpace(ScaleRule.RewardTitle))
            {
                return AvatarScalingCardStatus.NeedsSetup;
            }

            if (ScaleRule is { UsesChatCommand: true } && string.IsNullOrWhiteSpace(ScaleRule.CommandText))
            {
                return AvatarScalingCardStatus.NeedsSetup;
            }

            return AvatarScalingCardStatus.Ready;
        }
    }

    public string StatusText => Status switch
    {
        AvatarScalingCardStatus.Ready => LocalizationService.Translate("Ready"),
        AvatarScalingCardStatus.NeedsSetup => LocalizationService.Translate("Needs setup"),
        AvatarScalingCardStatus.Disabled => LocalizationService.Translate("Disabled"),
        _ => string.Empty
    };

    public string ActionSummary => DescribeScaleAction(ScaleRule)
        ?? DescribeScaleAction(CashPaymentRule?.ScaleAction)
        ?? DescribeScaleAction(PowerUpRule?.ScaleAction)
        ?? LocalizationService.Translate("Unlocks child scale rewards");

    public string SafetySummary => LocalizationService.Format("Current max height allowed: {0}", safety.CurrentMaxHeightAllowedText);

    public bool CanTest => Kind == AvatarScalingSourceKind.TwitchReward
        && ScaleRule is { UsesChannelPointReward: true };

    public void Dispose()
    {
        if (disposed)
        {
            return;
        }

        disposed = true;
        safety.PropertyChanged -= OnSafetyPropertyChanged;
        if (ScaleRule is not null)
        {
            ScaleRule.PropertyChanged -= OnSourcePropertyChanged;
        }

        if (MasterReward is not null)
        {
            MasterReward.PropertyChanged -= OnSourcePropertyChanged;
        }

        if (CashPaymentRule is not null)
        {
            CashPaymentRule.PropertyChanged -= OnCashPaymentRulePropertyChanged;
        }

        if (cashPaymentScaleAction is not null)
        {
            cashPaymentScaleAction.PropertyChanged -= OnSourcePropertyChanged;
            cashPaymentScaleAction = null;
        }

        if (PowerUpRule is not null)
        {
            PowerUpRule.PropertyChanged -= OnPowerUpRulePropertyChanged;
        }

        if (powerUpScaleAction is not null)
        {
            powerUpScaleAction.PropertyChanged -= OnSourcePropertyChanged;
            powerUpScaleAction = null;
        }

        GC.SuppressFinalize(this);
    }

    private static string? DescribeScaleAction(AvatarScaleRule? rule)
    {
        if (rule is null)
        {
            return null;
        }

        return rule.TriggerType == AvatarScaleTriggerType.SupporterGrowth
            ? rule.SupporterGrowthSummary
            : rule.ScaleSummary;
    }

    private void OnSafetyPropertyChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (string.IsNullOrWhiteSpace(e.PropertyName)
            || e.PropertyName == nameof(AvatarScaleSafetySettings.CurrentMaxHeightAllowedText)
            || e.PropertyName == nameof(AvatarScaleSafetySettings.CurrentMaximumHeightMeters))
        {
            RaisePropertyChanged(nameof(SafetySummary));
        }
    }

    private void OnSourcePropertyChanged(object? sender, PropertyChangedEventArgs e) => RaiseComputedSourceProperties();

    private void OnCashPaymentRulePropertyChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (string.IsNullOrWhiteSpace(e.PropertyName) || e.PropertyName == nameof(CashPaymentRule.ScaleAction))
        {
            WireCashPaymentScaleAction(CashPaymentRule!.ScaleAction);
        }

        RaiseComputedSourceProperties();
    }

    private void OnPowerUpRulePropertyChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (string.IsNullOrWhiteSpace(e.PropertyName) || e.PropertyName == nameof(PowerUpRule.ScaleAction))
        {
            WirePowerUpScaleAction(PowerUpRule!.ScaleAction);
        }

        RaiseComputedSourceProperties();
    }

    private void WireCashPaymentScaleAction(AvatarScaleRule rule)
    {
        if (ReferenceEquals(cashPaymentScaleAction, rule))
        {
            return;
        }

        if (cashPaymentScaleAction is not null)
        {
            cashPaymentScaleAction.PropertyChanged -= OnSourcePropertyChanged;
        }

        cashPaymentScaleAction = rule;
        cashPaymentScaleAction.PropertyChanged += OnSourcePropertyChanged;
    }

    private void WirePowerUpScaleAction(AvatarScaleRule rule)
    {
        if (ReferenceEquals(powerUpScaleAction, rule))
        {
            return;
        }

        if (powerUpScaleAction is not null)
        {
            powerUpScaleAction.PropertyChanged -= OnSourcePropertyChanged;
        }

        powerUpScaleAction = rule;
        powerUpScaleAction.PropertyChanged += OnSourcePropertyChanged;
    }

    private void RaiseComputedSourceProperties()
    {
        RaisePropertyChanged(nameof(Title));
        RaisePropertyChanged(nameof(Status));
        RaisePropertyChanged(nameof(StatusText));
        RaisePropertyChanged(nameof(ActionSummary));
        RaisePropertyChanged(nameof(CanTest));
    }
}
