using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Text;
using System.Windows.Input;
using VrcTwitchOscBridge.Infrastructure;
using VrcTwitchOscBridge.Models;

namespace VrcTwitchOscBridge.UserControls;

public sealed class InlineSubsRuleRowViewModel : ObservableObject, IRuleRowViewModel
{
    private readonly TriggerRule _rule;
    private string _summary = string.Empty;
    private ICommand? _editCommand;
    private ICommand? _deleteCommand;

    public InlineSubsRuleRowViewModel(TriggerRule rule)
    {
        _rule = rule ?? throw new ArgumentNullException(nameof(rule));
        _rule.PropertyChanged += OnRulePropertyChanged;
        RefreshSummary();
    }

    public object Rule => _rule;
    public string Summary { get => _summary; private set => SetProperty(ref _summary, value); }
    public bool IsEnabled => _rule.IsEnabled;

    public ICommand EditCommand
    {
        get => _editCommand ??= new RelayCommand(_ => { });
        set => _editCommand = value;
    }

    public ICommand DeleteCommand
    {
        get => _deleteCommand ??= new RelayCommand(_ => { });
        set => _deleteCommand = value;
    }

    public void RefreshSummary()
    {
        var name = string.IsNullOrWhiteSpace(_rule.Name) ? "Untitled" : _rule.Name;
        var sb = new StringBuilder();
        sb.Append("⭐ ").Append(name);
        var parts = new List<string>();
        if (_rule.SubscriptionTier1SecondsPerSub > 0) parts.Add($"T1:{_rule.SubscriptionTier1SecondsPerSub}s");
        if (_rule.SubscriptionTier2SecondsPerSub > 0) parts.Add($"T2:{_rule.SubscriptionTier2SecondsPerSub}s");
        if (_rule.SubscriptionTier3SecondsPerSub > 0) parts.Add($"T3:{_rule.SubscriptionTier3SecondsPerSub}s");
        if (parts.Count > 0) sb.Append(" — ").Append(string.Join(" ", parts));
        if (_rule.SubscriptionsAmountUnitsPerDuration > 0 && _rule.SubscriptionsSecondsPerAmountUnit > 0)
        {
            sb.Append(", ").Append(_rule.SubscriptionsSecondsPerAmountUnit)
              .Append("s per ").Append(_rule.SubscriptionsAmountUnitsPerDuration).Append(" subs");
        }
        if (_rule.MaxAccumulatedDurationEnabled && _rule.MaxAccumulatedDurationSeconds > 0)
        {
            sb.Append(", cap ").Append(_rule.MaxAccumulatedDurationSeconds).Append("s");
        }
        var subType = _rule.IsGiftSubscription ? "regular+gift" : "regular";
        sb.Append(", sub-type: ").Append(subType);
        if (!string.IsNullOrWhiteSpace(_rule.SupporterKeywordText))
        {
            sb.Append(", keyword: ").Append(_rule.SupporterKeywordText);
        }
        Summary = sb.ToString();
    }

    private void OnRulePropertyChanged(object? sender, PropertyChangedEventArgs e) => RefreshSummary();
}
