using System;
using System.ComponentModel;
using System.Text;
using System.Windows.Input;
using VrcTwitchOscBridge.Infrastructure;
using VrcTwitchOscBridge.Models;

namespace VrcTwitchOscBridge.UserControls;

public sealed class InlinePowerUpRuleRowViewModel : ObservableObject, IRuleRowViewModel
{
    private readonly TriggerRule _rule;
    private string _summary = string.Empty;
    private ICommand? _editCommand;
    private ICommand? _deleteCommand;

    public InlinePowerUpRuleRowViewModel(TriggerRule rule, AvatarSwapProfile? profile = null)
    {
        _rule = rule ?? throw new ArgumentNullException(nameof(rule));
        Profile = profile;
        _rule.PropertyChanged += OnRulePropertyChanged;
        RefreshSummary();
    }

    public object Rule => _rule;
    public AvatarSwapProfile? Profile { get; }
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
        sb.Append("⚡ ").Append(name);
        if (!string.IsNullOrWhiteSpace(_rule.PowerUpId))
        {
            var title = string.IsNullOrWhiteSpace(_rule.ChannelPointRewardTitle)
                ? "linked"
                : _rule.ChannelPointRewardTitle;
            sb.Append(" — ").Append(title);
        }
        Summary = sb.ToString();
    }

    private void OnRulePropertyChanged(object? sender, PropertyChangedEventArgs e) => RefreshSummary();
}
