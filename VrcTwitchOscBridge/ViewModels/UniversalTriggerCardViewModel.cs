using System;
using System.ComponentModel;
using System.Linq;
using System.Windows.Media;
using VrcTwitchOscBridge.Infrastructure;
using VrcTwitchOscBridge.Models;
using VrcTwitchOscBridge.Services;

namespace VrcTwitchOscBridge.ViewModels;

public enum UniversalTriggerCardStatus
{
    Ready,
    Warn,
    Disabled,
}

public sealed class UniversalTriggerCardViewModel : ObservableObject, IDisposable
{
    private readonly Func<UniversalTriggerRule, bool> _isWarnFn;
    private bool disposed;

    public UniversalTriggerCardViewModel(UniversalTriggerRule rule, Func<UniversalTriggerRule, bool> isWarnFn)
    {
        Rule = rule ?? throw new ArgumentNullException(nameof(rule));
        _isWarnFn = isWarnFn ?? throw new ArgumentNullException(nameof(isWarnFn));
        rule.PropertyChanged += OnRulePropertyChanged;
    }

    public UniversalTriggerRule Rule { get; }

    public UniversalTriggerCardStatus Status =>
        !Rule.IsEnabled ? UniversalTriggerCardStatus.Disabled
        : _isWarnFn(Rule) ? UniversalTriggerCardStatus.Warn
        : UniversalTriggerCardStatus.Ready;

    public string TypePill => Rule.TriggerType switch
    {
        UniversalTriggerType.ChatCommand => LocalizationService.Translate("Universal Triggers Type Pill Chat"),
        UniversalTriggerType.ChannelPointReward => LocalizationService.Translate("Universal Triggers Type Pill Reward"),
        UniversalTriggerType.Bits => LocalizationService.Translate("Universal Triggers Type Pill Bits"),
        UniversalTriggerType.Subscription => LocalizationService.Translate("Universal Triggers Type Pill Sub"),
        UniversalTriggerType.GiftSubscription => LocalizationService.Translate("Universal Triggers Type Pill Gift Sub"),
        UniversalTriggerType.Follow => LocalizationService.Translate("Universal Triggers Type Pill Follow"),
        _ => string.Empty,
    };

    public string EmojiIcon => Rule.TriggerType switch
    {
        UniversalTriggerType.ChatCommand => "💬",
        UniversalTriggerType.ChannelPointReward => "🎁",
        UniversalTriggerType.Bits => "💎",
        UniversalTriggerType.Subscription => "⭐",
        UniversalTriggerType.GiftSubscription => "🎀",
        UniversalTriggerType.Follow => "❤️",
        _ => "❓",
    };

    public string StatusPill => Status switch
    {
        UniversalTriggerCardStatus.Ready => LocalizationService.Translate("Universal Triggers Status Ready"),
        UniversalTriggerCardStatus.Warn => LocalizationService.Translate("Needs setup"),
        UniversalTriggerCardStatus.Disabled => LocalizationService.Translate("Universal Triggers Status Disabled"),
        _ => string.Empty,
    };

    public Brush StatusStripeBrush
    {
        get
        {
            var app = System.Windows.Application.Current;
            if (app is null)
            {
                return Status == UniversalTriggerCardStatus.Warn ? Brushes.Goldenrod
                    : Status == UniversalTriggerCardStatus.Disabled ? Brushes.Gray
                    : Brushes.Green;
            }
            var key = Status switch
            {
                UniversalTriggerCardStatus.Ready => "StatusStripeReadyBrush",
                UniversalTriggerCardStatus.Warn => "StatusStripeWarnBrush",
                _ => "StatusStripeOffBrush",
            };
            return app.TryFindResource(key) as Brush
                ?? (Status == UniversalTriggerCardStatus.Warn ? Brushes.Goldenrod
                : Status == UniversalTriggerCardStatus.Disabled ? Brushes.Gray
                : Brushes.Green);
        }
    }

    public bool IsFromFooma => FoomaInteractionConfigImporter.IsFoomaImport(Rule);

    public string Description
    {
        get
        {
            var actionSummary = BuildActionSummary();
            return Rule.TriggerType switch
            {
                UniversalTriggerType.ChatCommand => LocalizationService.Format("Universal Triggers Description Chat", Rule.CommandText ?? string.Empty, actionSummary),
                UniversalTriggerType.ChannelPointReward when Rule.RewardSyncMode == TwitchRewardSyncMode.CreateOrManage
                    => LocalizationService.Format("Universal Triggers Description Reward Managed", Rule.RewardTitle ?? string.Empty, Rule.RewardCost, Rule.RewardCooldownSeconds, actionSummary),
                UniversalTriggerType.ChannelPointReward
                    => LocalizationService.Format("Universal Triggers Description Reward Linked", Rule.RewardTitle ?? string.Empty),
                UniversalTriggerType.Bits when Rule.MaximumBits > 0
                    => LocalizationService.Format("Universal Triggers Description Bits Range", Rule.MinimumBits, Rule.MaximumBits, actionSummary),
                UniversalTriggerType.Bits
                    => LocalizationService.Format("Universal Triggers Description Bits Open", Rule.MinimumBits, actionSummary),
                UniversalTriggerType.Subscription => LocalizationService.Format("Universal Triggers Description Subs", Rule.SubscriptionTier.ToString(), actionSummary),
                UniversalTriggerType.GiftSubscription => LocalizationService.Format("Universal Triggers Description Gift Subs", actionSummary),
                UniversalTriggerType.Follow => LocalizationService.Format("Universal Triggers Description Follow", actionSummary),
                _ => string.Empty,
            };
        }
    }

    private string BuildActionSummary()
    {
        var actions = Rule.Actions;
        if (actions.Count == 0)
        {
            return "(no actions)";
        }
        if (actions.Count == 1)
        {
            var a = actions[0];
            return $"{a.OscAddress} {a.TargetValue} for {a.DurationSeconds}s";
        }
        var key = Rule.ExecuteRandomAction ? "Universal Triggers Action Summary Random" : "Universal Triggers Action Summary All";
        return LocalizationService.Format(key, actions.Count);
    }

    private void OnRulePropertyChanged(object? sender, PropertyChangedEventArgs e)
    {
        RaisePropertyChanged(nameof(Status));
        RaisePropertyChanged(nameof(StatusPill));
        RaisePropertyChanged(nameof(StatusStripeBrush));
        RaisePropertyChanged(nameof(TypePill));
        RaisePropertyChanged(nameof(EmojiIcon));
        RaisePropertyChanged(nameof(Description));
        RaisePropertyChanged(nameof(IsFromFooma));
    }

    public void Dispose()
    {
        if (disposed)
        {
            return;
        }

        disposed = true;
        Rule.PropertyChanged -= OnRulePropertyChanged;
        GC.SuppressFinalize(this);
    }
}
