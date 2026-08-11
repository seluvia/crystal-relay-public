using System;
using System.ComponentModel;
using System.Text;
using System.Windows.Input;
using VrcTwitchOscBridge.Infrastructure;
using VrcTwitchOscBridge.Models;

namespace VrcTwitchOscBridge.UserControls;

public sealed class InlineRouletteRuleRowViewModel : ObservableObject, IRuleRowViewModel, IDisposable
{
    private readonly TriggerRule _rule;
    private string _summary = string.Empty;
    private ICommand? _editCommand;
    private ICommand? _deleteCommand;

    public InlineRouletteRuleRowViewModel(TriggerRule rule)
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
        sb.Append("🎰 ").Append(name);

        switch (_rule.TriggerType)
        {
            case TwitchTriggerType.ChannelPoints:
                if (_rule.ChannelPointRewardCost > 0)
                    sb.Append(" — ").Append(_rule.ChannelPointRewardCost).Append(" pts");
                break;
            case TwitchTriggerType.Bits:
                if (_rule.MinimumAmount > 0)
                    sb.Append(" — ").Append(_rule.MinimumAmount).Append(" bits");
                break;
            case TwitchTriggerType.Subscriptions:
            case TwitchTriggerType.GiftSubscription:
                if (_rule.MinimumAmount > 0)
                    sb.Append(" — ").Append(_rule.MinimumAmount).Append(" subs");
                break;
            case TwitchTriggerType.Follow:
                sb.Append(" — follow");
                break;
            case TwitchTriggerType.ChatCommand:
                if (!string.IsNullOrWhiteSpace(_rule.ChatCommandText))
                    sb.Append(" — ").Append(_rule.ChatCommandText);
                break;
            case TwitchTriggerType.PowerUp:
                sb.Append(" — power-up");
                break;
        }

        Summary = sb.ToString();
    }

    public void Dispose() => _rule.PropertyChanged -= OnRulePropertyChanged;

    private void OnRulePropertyChanged(object? sender, PropertyChangedEventArgs e) => RefreshSummary();
}
