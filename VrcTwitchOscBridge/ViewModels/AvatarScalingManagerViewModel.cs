using System.Collections.ObjectModel;
using System.Collections.Specialized;
using System.ComponentModel;
using System.Windows;
using VrcTwitchOscBridge.Infrastructure;
using VrcTwitchOscBridge.Models;
using VrcTwitchOscBridge.Services;

namespace VrcTwitchOscBridge.ViewModels;

public enum AvatarScalingManagerSourceView
{
    TwitchRewards,
    SupporterGrowth,
    CashPayments,
    PowerUps,
    AllSources
}

public sealed class AvatarScalingScaleSetGroupViewModel
{
    public AvatarScalingScaleSetGroupViewModel(
        AvatarScaleSet scaleSet,
        IEnumerable<AvatarScalingSourceCardViewModel> cards)
    {
        ScaleSet = scaleSet;
        Cards = new ObservableCollection<AvatarScalingSourceCardViewModel>(cards);
    }

    public AvatarScaleSet ScaleSet { get; }

    public ObservableCollection<AvatarScalingSourceCardViewModel> Cards { get; }

    public string Title => ScaleSet.DisplayTitle;

    public string CountText => Cards.Count == 1
        ? LocalizationService.Translate("1 reward")
        : LocalizationService.Format("{0} rewards", Cards.Count);
}

public sealed class AvatarScalingManagerViewModel : ObservableObject, IDisposable
{
    private readonly AppSettings settings;
    private readonly MainWindowViewModel? mainWindowViewModel;
    private readonly HashSet<AvatarScaleSet> observedScaleSets = [];
    private readonly Dictionary<AvatarScaleSet, ObservableCollection<AvatarScaleRule>> observedScaleRuleCollections = [];
    private readonly HashSet<AvatarScaleRule> observedScaleRules = [];
    private readonly HashSet<CashPaymentRule> observedCashPaymentRules = [];
    private readonly HashSet<PowerUpRule> observedPowerUpRules = [];
    private AvatarScalingManagerSourceView activeSourceView = AvatarScalingManagerSourceView.AllSources;
    private bool isEditorOpen;
    private bool isAdvancedSafetyOpen;
    private bool disposed;
    private AvatarScalingSourceCardViewModel? selectedCard;

    public AvatarScalingManagerViewModel(AppSettings settings, MainWindowViewModel? mainWindowViewModel)
    {
        this.settings = settings ?? throw new ArgumentNullException(nameof(settings));
        this.mainWindowViewModel = mainWindowViewModel;

        OpenEditorCommand = new RelayCommand(parameter => OpenEditor(parameter as AvatarScalingSourceCardViewModel));
        CloseEditorCommand = new RelayCommand(CloseEditor);
        OpenAdvancedSafetyCommand = new RelayCommand(ToggleAdvancedSafety);
        DeleteCardCommand = new RelayCommand(DeleteCard);
        TestCardCommand = new RelayCommand(TestCard);

        WireSourceCollections();
        Settings.AvatarScaleSafety.PropertyChanged += OnAvatarScaleSafetyPropertyChanged;
        if (mainWindowViewModel is not null)
        {
            mainWindowViewModel.PropertyChanged += OnMainWindowViewModelPropertyChanged;
        }

        RefreshCards();
    }

    public AppSettings Settings => settings;

    public ObservableCollection<AvatarScalingSourceCardViewModel> TwitchRewardCards { get; } = [];

    public ObservableCollection<AvatarScalingScaleSetGroupViewModel> TwitchScaleSetGroups { get; } = [];

    public ObservableCollection<AvatarScalingSourceCardViewModel> SupporterGrowthCards { get; } = [];

    public ObservableCollection<AvatarScalingSourceCardViewModel> CashPaymentCards { get; } = [];

    public ObservableCollection<AvatarScalingSourceCardViewModel> PowerUpCards { get; } = [];

    public AvatarScalingSourceCardViewModel MasterRewardCard { get; private set; } = null!;

    public AvatarScalingSourceCardViewModel? SelectedCard
    {
        get => selectedCard;
        private set
        {
            if (SetProperty(ref selectedCard, value))
            {
                RaisePropertyChanged(nameof(SelectedAvatarScaleRule));
                RaisePropertyChanged(nameof(SelectedCashPaymentRule));
                RaisePropertyChanged(nameof(SelectedPowerUpRule));
                RaisePropertyChanged(nameof(SelectedCardUsesScaleSetCommands));
                RaisePropertyChanged(nameof(HasSelectedAvatarScaleSet));
                RaisePropertyChanged(nameof(HasSelectedCard));
                RaisePropertyChanged(nameof(HasNoSelectedCard));
                RaisePropertyChanged(nameof(AvailableAvatarScaleTriggerTypesForSelectedRule));
                RaisePropertyChanged(nameof(AvatarScaleRuleLockoutSummaryText));
                RaisePropertyChanged(nameof(ActiveScaleAction));
            }
        }
    }

    public AvatarScaleRule? SelectedAvatarScaleRule => SelectedCard?.ScaleRule;

    public CashPaymentRule? SelectedCashPaymentRule => SelectedCard?.CashPaymentRule;

    public PowerUpRule? SelectedPowerUpRule => SelectedCard?.PowerUpRule;

    public AvatarScaleRule? ActiveScaleAction =>
        SelectedCard?.ScaleRule
        ?? SelectedCard?.CashPaymentRule?.ScaleAction
        ?? SelectedCard?.PowerUpRule?.ScaleAction;

    public bool SelectedCardUsesScaleSetCommands => GetSelectedCardScaleSetOwner() is not null;

    public bool HasSelectedAvatarScaleSet => mainWindowViewModel?.SelectedAvatarScaleSet is not null
        || GetSelectedCardScaleSetOwner() is not null;

    public bool HasSelectedCard => SelectedCard is not null;

    public bool HasNoSelectedCard => SelectedCard is null;

    public AvatarScalingManagerSourceView ActiveSourceView
    {
        get => activeSourceView;
        set
        {
            if (SetProperty(ref activeSourceView, value))
            {
                RaisePropertyChanged(nameof(IsChannelPointViewActive));
                RaisePropertyChanged(nameof(IsPaySystemViewActive));
                RaisePropertyChanged(nameof(ChannelPointColumnWidth));
                RaisePropertyChanged(nameof(PaySystemSpacerWidth));
                RaisePropertyChanged(nameof(PaySystemColumnWidth));
            }
        }
    }

    public bool IsChannelPointViewActive =>
        ActiveSourceView == AvatarScalingManagerSourceView.TwitchRewards
        || ActiveSourceView == AvatarScalingManagerSourceView.AllSources;

    public bool IsPaySystemViewActive =>
        ActiveSourceView == AvatarScalingManagerSourceView.SupporterGrowth
        || ActiveSourceView == AvatarScalingManagerSourceView.CashPayments
        || ActiveSourceView == AvatarScalingManagerSourceView.PowerUps
        || ActiveSourceView == AvatarScalingManagerSourceView.AllSources;

    private static readonly GridLength ZeroGrid = new GridLength(0);
    private static readonly GridLength StarGrid = new GridLength(1, GridUnitType.Star);
    private static readonly GridLength SpacerGrid = new GridLength(12);

    public GridLength ChannelPointColumnWidth => IsChannelPointViewActive ? StarGrid : ZeroGrid;
    public GridLength PaySystemSpacerWidth =>
        (IsChannelPointViewActive && IsPaySystemViewActive) ? SpacerGrid : ZeroGrid;
    public GridLength PaySystemColumnWidth => IsPaySystemViewActive ? StarGrid : ZeroGrid;

    public bool IsEditorOpen
    {
        get => isEditorOpen;
        private set => SetProperty(ref isEditorOpen, value);
    }

    public bool IsAdvancedSafetyOpen
    {
        get => isAdvancedSafetyOpen;
        private set => SetProperty(ref isAdvancedSafetyOpen, value);
    }

    public string CurrentMaxHeightAllowedText => Settings.AvatarScaleSafety.CurrentMaxHeightAllowedText;

    public string CurrentMinHeightAllowedText => Settings.AvatarScaleSafety.CurrentMinHeightAllowedText;

    public RelayCommand? AddAvatarScaleSetCommand => mainWindowViewModel?.AddAvatarScaleSetCommand;

    public RelayCommand? RemoveSelectedAvatarScaleSetCommand => mainWindowViewModel?.RemoveSelectedAvatarScaleSetCommand;

    public RelayCommand? AddAvatarScaleRuleCommand => mainWindowViewModel?.AddAvatarScaleRuleCommand;

    public RelayCommand? AddRewardGrowthCommand => mainWindowViewModel?.AddRewardGrowthCommand;

    public RelayCommand? AddAvatarScalingCashPaymentRuleCommand => mainWindowViewModel?.AddAvatarScalingCashPaymentRuleCommand;

    public RelayCommand? AddAvatarScalingPowerUpRuleCommand => mainWindowViewModel?.AddAvatarScalingPowerUpRuleCommand;

    public RelayCommand? RemoveSelectedAvatarScaleRuleCommand => mainWindowViewModel?.RemoveSelectedAvatarScaleRuleCommand;

    public RelayCommand? TestSelectedAvatarScaleRuleCommand => mainWindowViewModel?.TestSelectedAvatarScaleRuleCommand;

    public RelayCommand? OpenAvatarScaleRuleLockoutPickerCommand => mainWindowViewModel?.OpenAvatarScaleRuleLockoutPickerCommand;

    public AsyncRelayCommand? RefreshTwitchRewardsCommand => mainWindowViewModel?.RefreshTwitchRewardsCommand;

    public RelayCommand? UnlinkTwitchRewardCommand => mainWindowViewModel?.UnlinkTwitchRewardCommand;

    public RelayCommand DeleteCardCommand { get; }

    public RelayCommand TestCardCommand { get; }

    public IReadOnlyList<AvatarScaleTriggerType> AvailableAvatarScaleTriggerTypesForSelectedRule =>
        mainWindowViewModel?.AvailableAvatarScaleTriggerTypesForSelectedRule ?? [];

    public IReadOnlyList<AvatarScaleModeOption> AvatarScaleModes => mainWindowViewModel?.AvatarScaleModes ?? [];

    public IReadOnlyList<AvatarScalePreset> AvatarScalePresets => mainWindowViewModel?.AvatarScalePresets ?? [];

    public IReadOnlyList<AvatarScaleRestoreMode> AvatarScaleRestoreModes => mainWindowViewModel?.AvatarScaleRestoreModes ?? [];

    public IReadOnlyList<AvatarScaleSubscriptionTierOption> AvatarScaleSubscriptionTierOptions =>
        mainWindowViewModel?.AvatarScaleSubscriptionTierOptions ?? [];

    public IReadOnlyList<ChatCommandPermissionOption> ChatCommandPermissionOptions =>
        mainWindowViewModel?.ChatCommandPermissionOptions ?? [];

    public IReadOnlyList<TwitchRewardSyncModeOption> RewardSyncModeOptions => mainWindowViewModel?.RewardSyncModeOptions ?? [];

    public ObservableCollection<TwitchRewardOption> RewardOptions => mainWindowViewModel?.RewardOptions ?? [];

    public string AvatarScaleRuleLockoutSummaryText => mainWindowViewModel?.AvatarScaleRuleLockoutSummaryText ?? string.Empty;

    public RelayCommand OpenEditorCommand { get; }

    public RelayCommand CloseEditorCommand { get; }

    public RelayCommand OpenAdvancedSafetyCommand { get; }

    public void RefreshCards()
    {
        var previousSelectedCard = SelectedCard;

        DisposeCards();
        TwitchRewardCards.Clear();
        TwitchScaleSetGroups.Clear();
        SupporterGrowthCards.Clear();
        CashPaymentCards.Clear();
        PowerUpCards.Clear();

        MasterRewardCard = new AvatarScalingSourceCardViewModel(
            AvatarScalingSourceKind.MasterReward,
            Settings.AvatarScaleSafety,
            masterReward: Settings.AvatarScaleMasterReward);
        RaisePropertyChanged(nameof(MasterRewardCard));

        foreach (var set in Settings.AvatarScaleSets)
        {
            var groupCards = new List<AvatarScalingSourceCardViewModel>();
            foreach (var rule in set.ScaleRules)
            {
                var kind = GetScaleRuleKind(rule);
                var card = new AvatarScalingSourceCardViewModel(kind, Settings.AvatarScaleSafety, scaleRule: rule);

                if (kind == AvatarScalingSourceKind.SupporterGrowth)
                {
                    SupporterGrowthCards.Add(card);
                }
                else
                {
                    TwitchRewardCards.Add(card);
                    groupCards.Add(card);
                }
            }

            if (groupCards.Count > 0)
            {
                TwitchScaleSetGroups.Add(new AvatarScalingScaleSetGroupViewModel(set, groupCards));
            }
        }

        foreach (var rule in Settings.CashPaymentRules.Where(rule => rule.UsesAvatarScaling))
        {
            CashPaymentCards.Add(new AvatarScalingSourceCardViewModel(
                AvatarScalingSourceKind.CashPayment,
                Settings.AvatarScaleSafety,
                cashPaymentRule: rule));
        }

        foreach (var rule in Settings.PowerUpRules.Where(rule => rule.UsesAvatarScaling))
        {
            PowerUpCards.Add(new AvatarScalingSourceCardViewModel(
                AvatarScalingSourceKind.PowerUp,
                Settings.AvatarScaleSafety,
                powerUpRule: rule));
        }

        ReconcileSelectedCard(previousSelectedCard);
    }

    private void OpenEditor(AvatarScalingSourceCardViewModel? card)
    {
        if (card is null)
        {
            return;
        }

        SelectedCard = card;
        var selectedRule = SelectedAvatarScaleRule;
        var owningSet = GetSelectedCardScaleSetOwner();
        if (mainWindowViewModel is not null)
        {
            mainWindowViewModel.SelectedAvatarScaleSet = owningSet;
            mainWindowViewModel.SelectedAvatarScaleRule = selectedRule;
        }

        RaisePropertyChanged(nameof(SelectedCardUsesScaleSetCommands));
        RaisePropertyChanged(nameof(HasSelectedAvatarScaleSet));
        RaisePropertyChanged(nameof(AvailableAvatarScaleTriggerTypesForSelectedRule));
        RaisePropertyChanged(nameof(AvatarScaleRuleLockoutSummaryText));

        IsEditorOpen = true;
    }

    private void CloseEditor()
    {
        IsEditorOpen = false;
        SelectedCard = null;
    }

    private void DeleteCard(object? parameter)
    {
        if (parameter is not AvatarScalingSourceCardViewModel card)
        {
            return;
        }

        switch (card.Kind)
        {
            case AvatarScalingSourceKind.MasterReward:
                return;
            case AvatarScalingSourceKind.TwitchReward:
            case AvatarScalingSourceKind.TwitchEvent:
            case AvatarScalingSourceKind.SupporterGrowth:
                if (card.ScaleRule is { } scaleRule)
                {
                    mainWindowViewModel?.DeleteAvatarScaleRuleByCard(scaleRule);
                }
                break;
            case AvatarScalingSourceKind.CashPayment:
                if (card.CashPaymentRule is { } cashRule)
                {
                    mainWindowViewModel?.DeleteCashPaymentRuleByCard(cashRule);
                }
                break;
            case AvatarScalingSourceKind.PowerUp:
                if (card.PowerUpRule is { } powerUpRule)
                {
                    mainWindowViewModel?.DeletePowerUpRuleByCard(powerUpRule);
                }
                break;
        }
    }

    private void TestCard(object? parameter)
    {
        if (parameter is not AvatarScalingSourceCardViewModel card)
        {
            return;
        }

        if (!card.CanTest)
        {
            return;
        }

        if (card.ScaleRule is { } scaleRule)
        {
            _ = mainWindowViewModel?.TestAvatarScaleRuleByCardAsync(scaleRule);
        }
    }

    private void ToggleAdvancedSafety()
    {
        IsAdvancedSafetyOpen = !IsAdvancedSafetyOpen;
    }

    private AvatarScaleSet? GetSelectedCardScaleSetOwner()
    {
        if (SelectedCard?.ScaleRule is not { } scaleRule)
        {
            return null;
        }

        return Settings.AvatarScaleSets.FirstOrDefault(set => set.ScaleRules.Contains(scaleRule));
    }

    private static AvatarScalingSourceKind GetScaleRuleKind(AvatarScaleRule rule) => rule.TriggerType switch
    {
        AvatarScaleTriggerType.SupporterGrowth => AvatarScalingSourceKind.SupporterGrowth,
        AvatarScaleTriggerType.ChannelPointReward or AvatarScaleTriggerType.ChatCommand => AvatarScalingSourceKind.TwitchReward,
        _ => AvatarScalingSourceKind.TwitchEvent
    };

    private void WireSourceCollections()
    {
        Settings.AvatarScaleSets.CollectionChanged += OnAvatarScaleSetsChanged;
        Settings.CashPaymentRules.CollectionChanged += OnCashPaymentRulesChanged;
        Settings.PowerUpRules.CollectionChanged += OnPowerUpRulesChanged;
        foreach (var set in Settings.AvatarScaleSets)
        {
            WireScaleSet(set);
        }

        foreach (var rule in Settings.CashPaymentRules)
        {
            WireCashPaymentRule(rule);
        }

        foreach (var rule in Settings.PowerUpRules)
        {
            WirePowerUpRule(rule);
        }
    }

    private void WireScaleSet(AvatarScaleSet set)
    {
        if (observedScaleSets.Add(set))
        {
            set.PropertyChanged += OnScaleSetPropertyChanged;
            WireScaleRulesCollection(set, set.ScaleRules);
        }
    }

    private void UnwireScaleSet(AvatarScaleSet set)
    {
        if (observedScaleSets.Remove(set))
        {
            set.PropertyChanged -= OnScaleSetPropertyChanged;
            if (observedScaleRuleCollections.TryGetValue(set, out var scaleRules))
            {
                UnwireScaleRulesCollection(scaleRules);
                observedScaleRuleCollections.Remove(set);
            }
        }
    }

    private void WireScaleRulesCollection(AvatarScaleSet set, ObservableCollection<AvatarScaleRule> scaleRules)
    {
        observedScaleRuleCollections[set] = scaleRules;
        scaleRules.CollectionChanged += OnScaleRulesChanged;
        foreach (var rule in scaleRules)
        {
            WireScaleRule(rule);
        }
    }

    private void UnwireScaleRulesCollection(ObservableCollection<AvatarScaleRule> scaleRules)
    {
        scaleRules.CollectionChanged -= OnScaleRulesChanged;
        foreach (var rule in scaleRules)
        {
            UnwireScaleRule(rule);
        }
    }

    private void WireScaleRule(AvatarScaleRule rule)
    {
        if (observedScaleRules.Add(rule))
        {
            rule.PropertyChanged += OnScaleRulePropertyChanged;
        }
    }

    private void UnwireScaleRule(AvatarScaleRule rule)
    {
        if (observedScaleRules.Remove(rule))
        {
            rule.PropertyChanged -= OnScaleRulePropertyChanged;
        }
    }

    private void WireCashPaymentRule(CashPaymentRule rule)
    {
        if (observedCashPaymentRules.Add(rule))
        {
            rule.PropertyChanged += OnCashPaymentRulePropertyChanged;
        }
    }

    private void UnwireCashPaymentRule(CashPaymentRule rule)
    {
        if (observedCashPaymentRules.Remove(rule))
        {
            rule.PropertyChanged -= OnCashPaymentRulePropertyChanged;
        }
    }

    private void WirePowerUpRule(PowerUpRule rule)
    {
        if (observedPowerUpRules.Add(rule))
        {
            rule.PropertyChanged += OnPowerUpRulePropertyChanged;
        }
    }

    private void UnwirePowerUpRule(PowerUpRule rule)
    {
        if (observedPowerUpRules.Remove(rule))
        {
            rule.PropertyChanged -= OnPowerUpRulePropertyChanged;
        }
    }

    private void OnAvatarScaleSetsChanged(object? sender, NotifyCollectionChangedEventArgs e)
    {
        if (e.Action == NotifyCollectionChangedAction.Reset)
        {
            foreach (var set in observedScaleSets.ToArray())
            {
                UnwireScaleSet(set);
            }

            foreach (var set in Settings.AvatarScaleSets)
            {
                WireScaleSet(set);
            }
        }
        else if (e.OldItems is not null)
        {
            foreach (var item in e.OldItems.OfType<AvatarScaleSet>())
            {
                UnwireScaleSet(item);
            }
        }

        if (e.NewItems is not null)
        {
            foreach (var item in e.NewItems.OfType<AvatarScaleSet>())
            {
                WireScaleSet(item);
            }
        }

        RefreshCards();
    }

    private void OnScaleSetPropertyChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (sender is not AvatarScaleSet set
            || (!string.IsNullOrWhiteSpace(e.PropertyName) && e.PropertyName != nameof(AvatarScaleSet.ScaleRules)))
        {
            return;
        }

        if (observedScaleRuleCollections.TryGetValue(set, out var oldScaleRules))
        {
            if (ReferenceEquals(oldScaleRules, set.ScaleRules))
            {
                return;
            }

            UnwireScaleRulesCollection(oldScaleRules);
        }

        WireScaleRulesCollection(set, set.ScaleRules);
        RefreshCards();
    }

    private void OnScaleRulesChanged(object? sender, NotifyCollectionChangedEventArgs e)
    {
        if (e.Action == NotifyCollectionChangedAction.Reset)
        {
            ReconcileObservedScaleRules();
        }
        else if (e.OldItems is not null)
        {
            foreach (var item in e.OldItems.OfType<AvatarScaleRule>())
            {
                UnwireScaleRule(item);
            }
        }

        if (e.NewItems is not null)
        {
            foreach (var item in e.NewItems.OfType<AvatarScaleRule>())
            {
                WireScaleRule(item);
            }
        }

        RefreshCards();
    }

    private void OnCashPaymentRulesChanged(object? sender, NotifyCollectionChangedEventArgs e)
    {
        if (e.Action == NotifyCollectionChangedAction.Reset)
        {
            ReconcileObservedCashPaymentRules();
        }
        else if (e.OldItems is not null)
        {
            foreach (var item in e.OldItems.OfType<CashPaymentRule>())
            {
                UnwireCashPaymentRule(item);
            }
        }

        if (e.NewItems is not null)
        {
            foreach (var item in e.NewItems.OfType<CashPaymentRule>())
            {
                WireCashPaymentRule(item);
            }
        }

        RefreshCards();
    }

    private void OnPowerUpRulesChanged(object? sender, NotifyCollectionChangedEventArgs e)
    {
        if (e.Action == NotifyCollectionChangedAction.Reset)
        {
            ReconcileObservedPowerUpRules();
        }
        else if (e.OldItems is not null)
        {
            foreach (var item in e.OldItems.OfType<PowerUpRule>())
            {
                UnwirePowerUpRule(item);
            }
        }

        if (e.NewItems is not null)
        {
            foreach (var item in e.NewItems.OfType<PowerUpRule>())
            {
                WirePowerUpRule(item);
            }
        }

        RefreshCards();
    }

    private void ReconcileObservedScaleRules()
    {
        var activeRules = Settings.AvatarScaleSets.SelectMany(set => set.ScaleRules).ToHashSet();
        foreach (var rule in observedScaleRules.Where(rule => !activeRules.Contains(rule)).ToArray())
        {
            UnwireScaleRule(rule);
        }

        foreach (var rule in activeRules)
        {
            WireScaleRule(rule);
        }
    }

    private void ReconcileObservedCashPaymentRules()
    {
        var activeRules = Settings.CashPaymentRules.ToHashSet();
        foreach (var rule in observedCashPaymentRules.Where(rule => !activeRules.Contains(rule)).ToArray())
        {
            UnwireCashPaymentRule(rule);
        }

        foreach (var rule in activeRules)
        {
            WireCashPaymentRule(rule);
        }
    }

    private void ReconcileObservedPowerUpRules()
    {
        var activeRules = Settings.PowerUpRules.ToHashSet();
        foreach (var rule in observedPowerUpRules.Where(rule => !activeRules.Contains(rule)).ToArray())
        {
            UnwirePowerUpRule(rule);
        }

        foreach (var rule in activeRules)
        {
            WirePowerUpRule(rule);
        }
    }

    private void OnScaleRulePropertyChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (ReferenceEquals(sender, SelectedCard?.ScaleRule)
            && (string.IsNullOrWhiteSpace(e.PropertyName)
                || e.PropertyName == nameof(AvatarScaleRule.TemporarilyDisabledScaleRuleIds)
                || e.PropertyName == nameof(AvatarScaleRule.HasScaleDisablePairings)))
        {
            RaisePropertyChanged(nameof(AvatarScaleRuleLockoutSummaryText));
        }

        if (string.IsNullOrWhiteSpace(e.PropertyName) || e.PropertyName == nameof(AvatarScaleRule.TriggerType))
        {
            RefreshCards();
        }
    }

    private void OnCashPaymentRulePropertyChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (string.IsNullOrWhiteSpace(e.PropertyName) || e.PropertyName == nameof(CashPaymentRule.ActionKind))
        {
            RefreshCards();
        }
    }

    private void OnMainWindowViewModelPropertyChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (string.IsNullOrWhiteSpace(e.PropertyName)
            || e.PropertyName == nameof(MainWindowViewModel.AvatarScaleRuleLockoutSummaryText))
        {
            RaisePropertyChanged(nameof(AvatarScaleRuleLockoutSummaryText));
        }

        if (string.IsNullOrWhiteSpace(e.PropertyName)
            || e.PropertyName == nameof(MainWindowViewModel.SelectedAvatarScaleSet))
        {
            RaisePropertyChanged(nameof(HasSelectedAvatarScaleSet));
        }
    }

    private void OnPowerUpRulePropertyChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (string.IsNullOrWhiteSpace(e.PropertyName) || e.PropertyName == nameof(PowerUpRule.ActionKind))
        {
            RefreshCards();
        }
    }

    private void OnAvatarScaleSafetyPropertyChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (string.IsNullOrWhiteSpace(e.PropertyName)
            || e.PropertyName == nameof(AvatarScaleSafetySettings.CurrentMaxHeightAllowedText)
            || e.PropertyName == nameof(AvatarScaleSafetySettings.CurrentMaximumHeightMeters)
            || e.PropertyName == nameof(AvatarScaleSafetySettings.CurrentMinHeightAllowedText)
            || e.PropertyName == nameof(AvatarScaleSafetySettings.CurrentMinimumHeightMeters))
        {
            RaisePropertyChanged(nameof(CurrentMaxHeightAllowedText));
            RaisePropertyChanged(nameof(CurrentMinHeightAllowedText));
        }
    }

    private void ReconcileSelectedCard(AvatarScalingSourceCardViewModel? previousSelectedCard)
    {
        if (previousSelectedCard is null)
        {
            return;
        }

        var replacement = GetCurrentCards().FirstOrDefault(card => HasSameSource(card, previousSelectedCard));
        if (replacement is not null)
        {
            SelectedCard = replacement;
            return;
        }

        IsEditorOpen = false;
        SelectedCard = null;
    }

    private IEnumerable<AvatarScalingSourceCardViewModel> GetCurrentCards()
    {
        if (MasterRewardCard is not null)
        {
            yield return MasterRewardCard;
        }

        foreach (var card in TwitchRewardCards)
        {
            yield return card;
        }

        foreach (var card in SupporterGrowthCards)
        {
            yield return card;
        }

        foreach (var card in CashPaymentCards)
        {
            yield return card;
        }

        foreach (var card in PowerUpCards)
        {
            yield return card;
        }
    }

    private static bool HasSameSource(AvatarScalingSourceCardViewModel card, AvatarScalingSourceCardViewModel previousCard) =>
        previousCard.ScaleRule is not null && ReferenceEquals(card.ScaleRule, previousCard.ScaleRule)
        || previousCard.MasterReward is not null && ReferenceEquals(card.MasterReward, previousCard.MasterReward)
        || previousCard.CashPaymentRule is not null && ReferenceEquals(card.CashPaymentRule, previousCard.CashPaymentRule)
        || previousCard.PowerUpRule is not null && ReferenceEquals(card.PowerUpRule, previousCard.PowerUpRule);

    private void DisposeCards()
    {
        foreach (var card in GetCurrentCards())
        {
            card.Dispose();
        }
    }

    public void Dispose()
    {
        if (disposed)
        {
            return;
        }

        disposed = true;
        if (mainWindowViewModel is not null)
        {
            mainWindowViewModel.PropertyChanged -= OnMainWindowViewModelPropertyChanged;
        }

        Settings.AvatarScaleSafety.PropertyChanged -= OnAvatarScaleSafetyPropertyChanged;
        Settings.AvatarScaleSets.CollectionChanged -= OnAvatarScaleSetsChanged;
        Settings.CashPaymentRules.CollectionChanged -= OnCashPaymentRulesChanged;
        Settings.PowerUpRules.CollectionChanged -= OnPowerUpRulesChanged;
        foreach (var set in observedScaleSets.ToArray())
        {
            UnwireScaleSet(set);
        }

        foreach (var rule in observedScaleRules.ToArray())
        {
            UnwireScaleRule(rule);
        }

        foreach (var rule in observedCashPaymentRules.ToArray())
        {
            UnwireCashPaymentRule(rule);
        }

        foreach (var rule in observedPowerUpRules.ToArray())
        {
            UnwirePowerUpRule(rule);
        }

        DisposeCards();
        GC.SuppressFinalize(this);
    }
}
