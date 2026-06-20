using System;
using System.ComponentModel;
using System.Text;
using System.Windows.Input;
using VrcTwitchOscBridge.Infrastructure;
using VrcTwitchOscBridge.Models;

namespace VrcTwitchOscBridge.UserControls;

public sealed class InlineBitsRuleRowViewModel : ObservableObject, IRuleRowViewModel
{
    private readonly TriggerRule _rule;
    private string _summary = string.Empty;
    private ICommand? _editCommand;
    private ICommand? _deleteCommand;

    public InlineBitsRuleRowViewModel(TriggerRule rule)
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
        sb.Append("💎 ").Append(name);
        if (_rule.MinimumAmount > 0)
        {
            sb.Append(" — Min ").Append(_rule.MinimumAmount).Append(" bits");
        }
        if (_rule.BitsAmountUnitsPerDuration > 0 && _rule.BitsSecondsPerAmountUnit > 0)
        {
            sb.Append(", ").Append(_rule.BitsSecondsPerAmountUnit)
              .Append("s per ").Append(_rule.BitsAmountUnitsPerDuration).Append(" bits");
        }
        if (_rule.MaxAccumulatedDurationEnabled && _rule.MaxAccumulatedDurationSeconds > 0)
        {
            sb.Append(", cap ").Append(_rule.MaxAccumulatedDurationSeconds).Append("s");
        }
        if (!string.IsNullOrWhiteSpace(_rule.SupporterKeywordText))
        {
            sb.Append(", keyword: ").Append(_rule.SupporterKeywordText);
        }
        if (_rule.AddBitsToSwapTime)
        {
            sb.Append(", swap time");
        }
        Summary = sb.ToString();
    }

    private void OnRulePropertyChanged(object? sender, PropertyChangedEventArgs e) => RefreshSummary();
}
