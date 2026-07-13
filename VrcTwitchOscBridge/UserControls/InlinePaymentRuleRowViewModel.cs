using System;
using System.ComponentModel;
using System.Text;
using System.Windows.Input;
using VrcTwitchOscBridge.Infrastructure;
using VrcTwitchOscBridge.Models;

namespace VrcTwitchOscBridge.UserControls;

public sealed class InlinePaymentRuleRowViewModel : ObservableObject, IRuleRowViewModel
{
    private readonly CashPaymentRule _rule;
    private string _summary = string.Empty;
    private ICommand? _editCommand;
    private ICommand? _deleteCommand;

    public InlinePaymentRuleRowViewModel(CashPaymentRule rule, AvatarSwapProfile? profile = null)
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
        var provider = ProviderDisplayName(_rule.Provider);
        var sb = new StringBuilder();
        sb.Append("💵 ").Append(name).Append(" — ").Append(provider);
        if (_rule.MinimumAmount > 0 || _rule.MaximumAmount > 0)
        {
            sb.Append(' ').Append(_rule.CurrencyCode ?? string.Empty).Append(' ')
              .Append(_rule.MinimumAmount).Append('-').Append(_rule.MaximumAmount);
        }
        if (_rule.RequireMessageKeyword && !string.IsNullOrWhiteSpace(_rule.MessageContains))
        {
            sb.Append(" match: '").Append(_rule.MessageContains).Append('\'');
        }
        Summary = sb.ToString();
    }

    private static string ProviderDisplayName(CashPaymentProvider provider) => provider switch
    {
        CashPaymentProvider.StreamElements => "StreamElements",
        CashPaymentProvider.Streamlabs => "Streamlabs",
        CashPaymentProvider.KoFi => "Ko-fi",
        _ => provider.ToString()
    };

    private void OnRulePropertyChanged(object? sender, PropertyChangedEventArgs e) => RefreshSummary();
}
