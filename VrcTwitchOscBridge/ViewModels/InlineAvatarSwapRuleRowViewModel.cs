using System;
using VrcTwitchOscBridge.Infrastructure;
using VrcTwitchOscBridge.Models;

namespace VrcTwitchOscBridge.ViewModels;

public sealed class InlineAvatarSwapRuleRowViewModel : ObservableObject
{
    private bool _isExpanded;
    private string _summary = string.Empty;

    public InlineAvatarSwapRuleRowViewModel(TriggerRule rule)
    {
        Rule = rule ?? throw new ArgumentNullException(nameof(rule));
        UpdateSummary();
    }

    public TriggerRule Rule { get; }

    public bool IsExpanded
    {
        get => _isExpanded;
        set => SetProperty(ref _isExpanded, value);
    }

    public string Summary
    {
        get => _summary;
        private set => SetProperty(ref _summary, value);
    }

    public void UpdateSummary()
    {
        Summary = Rule.DisplayTitle ?? Rule.Name ?? "Rule";
    }
}
