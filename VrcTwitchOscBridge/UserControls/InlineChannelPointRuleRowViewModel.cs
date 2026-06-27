using System;
using System.ComponentModel;
using System.Windows.Input;
using System.Windows.Media;
using VrcTwitchOscBridge.Infrastructure;
using VrcTwitchOscBridge.Models;

namespace VrcTwitchOscBridge.UserControls;

public sealed class InlineChannelPointRuleRowViewModel : ObservableObject, IRuleRowViewModel
{
    private readonly TriggerRule _rule;
    private string _summary = string.Empty;
    private ICommand? _editCommand;
    private ICommand? _deleteCommand;

    public InlineChannelPointRuleRowViewModel(TriggerRule rule, AvatarSwapProfile? profile = null)
    {
        _rule = rule ?? throw new ArgumentNullException(nameof(rule));
        Profile = profile;
        _rule.PropertyChanged += OnRulePropertyChanged;
        RefreshSummary();
    }

    public object Rule => _rule;
    public AvatarSwapProfile? Profile { get; }

    public string Summary
    {
        get => _summary;
        private set => SetProperty(ref _summary, value);
    }

    public bool IsEnabled => _rule.IsEnabled;

    public Brush ReadyBrush => _rule.ManagedRewardReadyBrush;

    public Brush CooldownBrush => _rule.ManagedRewardCooldownBrush;

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
        Summary = _rule.ChannelPointRewardCost > 0
            ? $"🏆 {name} — {_rule.ChannelPointRewardCost} pts"
            : $"🏆 {name}";
    }

    private void OnRulePropertyChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName is nameof(TriggerRule.Name)
            or nameof(TriggerRule.ChannelPointRewardCost)
            or nameof(TriggerRule.IsEnabled)
            or nameof(TriggerRule.ManagedRewardReadyColor)
            or nameof(TriggerRule.ManagedRewardCooldownColor))
        {
            RefreshSummary();
            RaisePropertyChanged(nameof(IsEnabled));
            RaisePropertyChanged(nameof(ReadyBrush));
            RaisePropertyChanged(nameof(CooldownBrush));
        }
    }
}
