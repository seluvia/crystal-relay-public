using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Collections.Specialized;
using System.ComponentModel;
using System.Linq;
using System.Windows.Data;
using VrcTwitchOscBridge.Infrastructure;
using VrcTwitchOscBridge.Models;
using VrcTwitchOscBridge.Services;

namespace VrcTwitchOscBridge.ViewModels;

public sealed class UniversalTriggersViewModel : ObservableObject
{
    private readonly AppSettings settings;
    private readonly BridgeCoordinator coordinator;
    private readonly Action<Action> uiInvoke;
    private readonly List<UniversalTriggerRule> _subscribedRules = new();
    private bool _isUpdatingFilter;

    public ObservableCollection<UniversalTriggerRule> UniversalTriggers => settings.UniversalTriggers;
    public ICollectionView UniversalTriggersView { get; }

    public ObservableCollection<UniversalTriggerCardViewModel> Cards { get; } = new();
    public ICollectionView CardsView { get; }

    private UniversalTriggerRule? selectedTrigger;
    public UniversalTriggerRule? SelectedTrigger
    {
        get => selectedTrigger;
        set => SetProperty(ref selectedTrigger, value);
    }

    private bool isEditorOpen;
    public bool IsEditorOpen
    {
        get => isEditorOpen;
        set => SetProperty(ref isEditorOpen, value);
    }

    private string searchText = string.Empty;
    public string SearchText
    {
        get => searchText;
        set
        {
            if (SetProperty(ref searchText, value ?? string.Empty))
            {
                UniversalTriggersView.Refresh();
                RaiseCountsChanged();
            }
        }
    }

    private bool showAll = true;
    public bool ShowAll
    {
        get => showAll;
        set
        {
            if (SetProperty(ref showAll, value))
            {
                if (value)
                {
                    if (_isUpdatingFilter) return;
                    _isUpdatingFilter = true;
                    try { ShowReady = false; ShowWarnings = false; ShowFooma = false; }
                    finally { _isUpdatingFilter = false; }
                }
                if (!_isUpdatingFilter) { ApplyFilter(); RaiseCountsChanged(); }
            }
        }
    }
    private bool showReady;
    public bool ShowReady
    {
        get => showReady;
        set
        {
            if (SetProperty(ref showReady, value))
            {
                if (value)
                {
                    if (_isUpdatingFilter) return;
                    _isUpdatingFilter = true;
                    try { ShowAll = false; ShowWarnings = false; ShowFooma = false; }
                    finally { _isUpdatingFilter = false; }
                }
                if (!_isUpdatingFilter) { ApplyFilter(); RaiseCountsChanged(); }
            }
        }
    }
    private bool showWarnings;
    public bool ShowWarnings
    {
        get => showWarnings;
        set
        {
            if (SetProperty(ref showWarnings, value))
            {
                if (value)
                {
                    if (_isUpdatingFilter) return;
                    _isUpdatingFilter = true;
                    try { ShowAll = false; ShowReady = false; ShowFooma = false; }
                    finally { _isUpdatingFilter = false; }
                }
                if (!_isUpdatingFilter) { ApplyFilter(); RaiseCountsChanged(); }
            }
        }
    }
    private bool showFooma;
    public bool ShowFooma
    {
        get => showFooma;
        set
        {
            if (SetProperty(ref showFooma, value))
            {
                if (value)
                {
                    if (_isUpdatingFilter) return;
                    _isUpdatingFilter = true;
                    try { ShowAll = false; ShowReady = false; ShowWarnings = false; }
                    finally { _isUpdatingFilter = false; }
                }
                if (!_isUpdatingFilter) { ApplyFilter(); RaiseCountsChanged(); }
            }
        }
    }

    public int CountAll => settings.UniversalTriggers.Count;
    public int CountReady => settings.UniversalTriggers.Count(IsCardReady);
    public int CountWarnings => settings.UniversalTriggers.Count(r => IsCardWarn(r) || IsCardDanger(r));
    public int CountFooma => settings.UniversalTriggers.Count(r => string.Equals(r.ImportSource, "Fooma Twitch Interaction", StringComparison.OrdinalIgnoreCase));

    public AsyncRelayCommand AddTriggerCommand { get; }
    public AsyncRelayCommand ImportFoomaCommand { get; }
    public AsyncRelayCommand DeleteAllCommand { get; }
    public AsyncRelayCommand EnableAllCommand { get; }
    public AsyncRelayCommand DisableAllCommand { get; }
    public RelayCommand OpenTriggerEditorCommand { get; }
    public RelayCommand CloseEditorCommand { get; }
    public AsyncRelayCommand TestSelectedTriggerCommand { get; }
    public AsyncRelayCommand DeleteSelectedTriggerCommand { get; }
    public RelayCommand ShowAllCommand { get; }
    public RelayCommand ShowReadyCommand { get; }
    public RelayCommand ShowWarningsCommand { get; }
    public RelayCommand ShowFoomaCommand { get; }

    public UniversalTriggersViewModel(AppSettings settings, BridgeCoordinator coordinator, Action<Action> uiInvoke)
    {
        this.settings = settings;
        this.coordinator = coordinator;
        this.uiInvoke = uiInvoke;

        UniversalTriggersView = CollectionViewSource.GetDefaultView(UniversalTriggers);
        UniversalTriggersView.Filter = FilterTrigger;

        CardsView = CollectionViewSource.GetDefaultView(Cards);
        CardsView.Filter = FilterCard;
        SyncCards();

        UniversalTriggers.CollectionChanged += OnUniversalTriggersCollectionChanged;
        foreach (var rule in UniversalTriggers)
            SubscribeRule(rule);

        AddTriggerCommand = new AsyncRelayCommand(async () => await OpenCreateWizardAsync());
        ImportFoomaCommand = new AsyncRelayCommand(async () => await OpenImportPreviewAsync());
        DeleteAllCommand = new AsyncRelayCommand(async () => await DeleteAllAsync());
        EnableAllCommand = new AsyncRelayCommand(async () => { foreach (var t in UniversalTriggers) t.IsEnabled = true; });
        DisableAllCommand = new AsyncRelayCommand(async () => { foreach (var t in UniversalTriggers) t.IsEnabled = false; });
        OpenTriggerEditorCommand = new RelayCommand(p =>
        {
            UniversalTriggerRule? rule = p switch
            {
                UniversalTriggerRule r => r,
                UniversalTriggerCardViewModel c => c.Rule,
                _ => null
            };
            if (rule is not null)
            {
                SelectedTrigger = rule;
                IsEditorOpen = true;
            }
        });
        CloseEditorCommand = new RelayCommand(_ => IsEditorOpen = false);
        TestSelectedTriggerCommand = new AsyncRelayCommand(async () => await TestSelectedAsync());
        DeleteSelectedTriggerCommand = new AsyncRelayCommand(async () => await DeleteSelectedAsync());
        ShowAllCommand = new RelayCommand(_ => ShowAll = true);
        ShowReadyCommand = new RelayCommand(_ => ShowReady = true);
        ShowWarningsCommand = new RelayCommand(_ => ShowWarnings = true);
        ShowFoomaCommand = new RelayCommand(_ => ShowFooma = true);
    }

    private bool FilterTrigger(object obj)
    {
        if (obj is not UniversalTriggerRule rule) return false;
        if (!string.IsNullOrWhiteSpace(SearchText))
        {
            var q = SearchText.Trim();
            var matchesText = (rule.Name?.Contains(q, StringComparison.OrdinalIgnoreCase) ?? false)
                              || (rule.CommandText?.Contains(q, StringComparison.OrdinalIgnoreCase) ?? false)
                              || (rule.RewardTitle?.Contains(q, StringComparison.OrdinalIgnoreCase) ?? false)
                              || rule.Actions.Any(a => a.OscAddress?.Contains(q, StringComparison.OrdinalIgnoreCase) ?? false);
            if (!matchesText) return false;
        }
        if (ShowReady) return IsCardReady(rule);
        if (ShowWarnings) return IsCardWarn(rule) || IsCardDanger(rule);
        if (ShowFooma) return string.Equals(rule.ImportSource, "Fooma Twitch Interaction", StringComparison.OrdinalIgnoreCase);
        return true;
    }

    private void ApplyFilter() => UniversalTriggersView.Refresh();

    private void SyncCards()
    {
        Cards.Clear();
        foreach (var rule in UniversalTriggers) Cards.Add(new UniversalTriggerCardViewModel(rule));
        RaiseCountsChanged();
    }

    private bool FilterCard(object obj)
    {
        if (obj is not UniversalTriggerCardViewModel card) return false;
        return FilterTrigger(card.Rule);
    }

    private void RaiseCountsChanged()
    {
        RaisePropertyChanged(nameof(CountAll));
        RaisePropertyChanged(nameof(CountReady));
        RaisePropertyChanged(nameof(CountWarnings));
        RaisePropertyChanged(nameof(CountFooma));
    }

    private static bool IsCardReady(UniversalTriggerRule r) =>
        r.IsConfigured && r.HasCompleteAction && new UniversalTriggerCardViewModel(r).PrimaryStatus == UniversalTriggerCardStatus.Ready;

    private static bool IsCardWarn(UniversalTriggerRule r) =>
        new UniversalTriggerCardViewModel(r).IsWarn;

    private static bool IsCardDanger(UniversalTriggerRule r) =>
        new UniversalTriggerCardViewModel(r).IsDanger;

    private async Task OpenCreateWizardAsync()
    {
        // Wired in Task 7 (wizard).
        await Task.CompletedTask;
    }

    private async Task OpenImportPreviewAsync()
    {
        // Wired in Task 8 (import preview).
        await Task.CompletedTask;
    }

    private async Task DeleteAllAsync()
    {
        // Wired in Task 11 (wiring).
        await Task.CompletedTask;
    }

    private async Task TestSelectedAsync()
    {
        if (selectedTrigger is null) return;
        await coordinator.SendTestUniversalTriggerAsync(
            BridgeRuntimeConfiguration.CreateManualTestSnapshot(selectedTrigger),
            default);
    }

    private async Task DeleteSelectedAsync()
    {
        if (selectedTrigger is null) return;
        var snapshot = selectedTrigger;
        IsEditorOpen = false;
        UniversalTriggers.Remove(snapshot);
        await Task.CompletedTask;
    }

    private void OnUniversalTriggersCollectionChanged(object? sender, NotifyCollectionChangedEventArgs e)
    {
        if (e.OldItems != null)
            foreach (UniversalTriggerRule r in e.OldItems) UnsubscribeRule(r);
        if (e.NewItems != null)
            foreach (UniversalTriggerRule r in e.NewItems) SubscribeRule(r);
        SyncCards();
    }

    private void SubscribeRule(UniversalTriggerRule rule)
    {
        if (_subscribedRules.Contains(rule)) return;
        rule.PropertyChanged += OnRulePropertyChanged;
        _subscribedRules.Add(rule);
    }

    private void UnsubscribeRule(UniversalTriggerRule rule)
    {
        rule.PropertyChanged -= OnRulePropertyChanged;
        _subscribedRules.Remove(rule);
    }

    private void OnRulePropertyChanged(object? sender, PropertyChangedEventArgs e)
    {
        // Refresh counts whenever a status-relevant property changes on any rule
        RaiseCountsChanged();
    }
}