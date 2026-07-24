using System;
using System.Collections;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Linq;
using System.Windows.Data;
using VrcTwitchOscBridge.Infrastructure;
using VrcTwitchOscBridge.Models;

namespace VrcTwitchOscBridge.ViewModels;

public sealed class MovementRedeemsManagerViewModel : ObservableObject, IDisposable
{
    private readonly AppSettings settings;
    private readonly MainWindowViewModel? mainWindowViewModel;
    private string searchText = string.Empty;
    private bool isEditorOpen;
    private bool disposed;
    private TriggerRule? selectedRule;
    private bool isNewRule;
    private bool isCashPaymentEnabled;
    private CashPaymentProvider cashPaymentProvider = CashPaymentProvider.StreamElements;
    private decimal cashPaymentMinimumAmount = 1m;
    private string cashPaymentCurrencyCode = string.Empty;
    private int cashPaymentCooldown;

    public MovementRedeemsManagerViewModel(AppSettings settings, MainWindowViewModel? mainWindowViewModel)
    {
        this.settings = settings ?? throw new ArgumentNullException(nameof(settings));
        this.mainWindowViewModel = mainWindowViewModel;

        OpenEditorCommand = new RelayCommand(p =>
        {
            SelectedCard = p as MovementRedeemCardViewModel;
            if (SelectedCard is not null)
            {
                SelectedRule = SelectedCard.GetRule();
                IsNewRule = false;
                IsEditorOpen = true;
            }
        });
        CloseEditorCommand = new RelayCommand(() => IsEditorOpen = false);
        AddNewRuleCommand = new RelayCommand(AddNewRule);
        EnableAllCommand = new RelayCommand(EnableAll);
        DisableAllCommand = new RelayCommand(DisableAll);
        DeleteCardCommand = new RelayCommand(p => DeleteCard(p as MovementRedeemCardViewModel));
        TestCardCommand = new RelayCommand(p => TestCard(p as MovementRedeemCardViewModel));

        SaveEditorCommand = new RelayCommand(SaveEditor);
        DeleteRuleCommand = new RelayCommand(() => DeleteCard(SelectedCard));
        TestRuleCommand = new RelayCommand(() => { if (SelectedCard is not null) TestCard(SelectedCard); });

        var items = new List<MovementDirectionItem>();
        foreach (PlayerMovementDirection dir in Enum.GetValues(typeof(PlayerMovementDirection)))
        {
            items.Add(new MovementDirectionItem(dir));
        }
        MovementDirections = (ListCollectionView)CollectionViewSource.GetDefaultView(items);

        WireCollectionChanges();
        RefreshCards();
    }

    public ObservableCollection<MovementRedeemCardViewModel> Cards { get; } = [];

    public string SearchText
    {
        get => searchText;
        set
        {
            if (SetProperty(ref searchText, value))
                RefreshCards();
        }
    }

    public bool IsEditorOpen
    {
        get => isEditorOpen;
        set => SetProperty(ref isEditorOpen, value);
    }

    public TriggerRule? SelectedRule
    {
        get => selectedRule;
        set
        {
            if (SetProperty(ref selectedRule, value))
            {
                RaisePropertyChanged(nameof(UsesChannelPointReward));
                RaisePropertyChanged(nameof(UsesChatCommand));
                RaisePropertyChanged(nameof(UsesChatCommandFallback));
                RaisePropertyChanged(nameof(UsesBits));
                RaisePropertyChanged(nameof(UsesSubscription));
                RaisePropertyChanged(nameof(UsesFollow));
                RaisePropertyChanged(nameof(UsesGiftSub));
                RaisePropertyChanged(nameof(UsesCashPayment));
                RaisePropertyChanged(nameof(IsAxisType));
                RaisePropertyChanged(nameof(IsVrOnly));
                RaisePropertyChanged(nameof(EditorTitle));
            }
            LoadCashPaymentState();
        }
    }

    private void LoadCashPaymentState()
    {
        if (selectedRule?.CashPaymentRuleId is not null)
        {
            var cashRule = settings.CashPaymentRules.FirstOrDefault(r => r.Id.ToString() == selectedRule.CashPaymentRuleId);
            if (cashRule is not null)
            {
                isCashPaymentEnabled = true;
                cashPaymentProvider = cashRule.Provider;
                cashPaymentMinimumAmount = cashRule.MinimumAmount;
                cashPaymentCurrencyCode = cashRule.CurrencyCode;
                cashPaymentCooldown = cashRule.CooldownSeconds;
                RaisePropertyChanged(nameof(IsCashPaymentEnabled));
                RaisePropertyChanged(nameof(CashPaymentProvider));
                RaisePropertyChanged(nameof(CashPaymentMinimumAmount));
                RaisePropertyChanged(nameof(CashPaymentCurrencyCode));
                RaisePropertyChanged(nameof(CashPaymentCooldown));
                return;
            }
        }
        isCashPaymentEnabled = false;
        cashPaymentProvider = Models.CashPaymentProvider.StreamElements;
        cashPaymentMinimumAmount = 1m;
        cashPaymentCurrencyCode = string.Empty;
        cashPaymentCooldown = 0;
        RaisePropertyChanged(nameof(IsCashPaymentEnabled));
        RaisePropertyChanged(nameof(CashPaymentProvider));
        RaisePropertyChanged(nameof(CashPaymentMinimumAmount));
        RaisePropertyChanged(nameof(CashPaymentCurrencyCode));
        RaisePropertyChanged(nameof(CashPaymentCooldown));
    }

    public bool IsNewRule
    {
        get => isNewRule;
        set => SetProperty(ref isNewRule, value);
    }

    public string EditorTitle => IsNewRule ? "Add Movement Rule" : "Edit Movement Rule";

    public bool IsCashPaymentEnabled
    {
        get => isCashPaymentEnabled;
        set => SetProperty(ref isCashPaymentEnabled, value);
    }

    public CashPaymentProvider CashPaymentProvider
    {
        get => cashPaymentProvider;
        set
        {
            var normalizedValue = Enum.IsDefined(value) ? value : Models.CashPaymentProvider.StreamElements;
            SetProperty(ref cashPaymentProvider, normalizedValue);
        }
    }

    public decimal CashPaymentMinimumAmount
    {
        get => cashPaymentMinimumAmount;
        set => SetProperty(ref cashPaymentMinimumAmount, Math.Max(0m, value));
    }

    public string CashPaymentCurrencyCode
    {
        get => cashPaymentCurrencyCode;
        set => SetProperty(ref cashPaymentCurrencyCode, value?.Trim() ?? string.Empty);
    }

    public int CashPaymentCooldown
    {
        get => cashPaymentCooldown;
        set => SetProperty(ref cashPaymentCooldown, Math.Max(0, value));
    }

    public bool UsesCashPayment => selectedRule?.Source == TriggerRuleSource.CashPayment || isCashPaymentEnabled;

    public IList ChatCommandPermissionValues => Enum.GetValues(typeof(ChatCommandPermission));

    public IReadOnlyList<TwitchTriggerTypeOption> TriggerTypeOptions { get; } =
    [
        new(TwitchTriggerType.ChannelPoints, "Channel Points"),
        new(TwitchTriggerType.Bits, "Bits"),
        new(TwitchTriggerType.Subscriptions, "Subs"),
        new(TwitchTriggerType.GiftSubscription, "Gift Sub"),
        new(TwitchTriggerType.Follow, "Follow"),
        new(TwitchTriggerType.ChatCommand, "Chat Command"),
    ];

    public IReadOnlyList<TwitchRewardSyncModeOption> RewardSyncModeOptions { get; } =
    [
        new(TwitchRewardSyncMode.CreateOrManage, "Create or Manage"),
        new(TwitchRewardSyncMode.LinkExisting, "Link Existing"),
    ];

    public IReadOnlyList<CashPaymentProviderOption> CashPaymentProviderOptions =>
        mainWindowViewModel?.CashPaymentProviderOptions ?? [];

    public bool UsesChannelPointReward => selectedRule?.TriggerType == TwitchTriggerType.ChannelPoints;
    public bool UsesChatCommand => selectedRule?.TriggerType == TwitchTriggerType.ChatCommand;
    public bool UsesChatCommandFallback => selectedRule?.ChatCommandEnabled == true && selectedRule?.TriggerType != TwitchTriggerType.ChatCommand;
    public bool UsesBits => selectedRule?.TriggerType == TwitchTriggerType.Bits;
    public bool UsesSubscription => selectedRule?.TriggerType == TwitchTriggerType.Subscriptions;
    public bool UsesGiftSub => selectedRule?.TriggerType == TwitchTriggerType.GiftSubscription;
    public bool UsesFollow => selectedRule?.TriggerType == TwitchTriggerType.Follow;
    public bool IsAxisType => selectedRule is not null && MovementTypeClassifier.IsAxisType(selectedRule.MovementDirection);
    public bool IsVrOnly => selectedRule is not null && MovementTypeClassifier.IsVrOnly(selectedRule.MovementDirection);

    public MovementRedeemCardViewModel? SelectedCard { get; set; }

    public ListCollectionView MovementDirections { get; }

    public RelayCommand OpenEditorCommand { get; }
    public RelayCommand CloseEditorCommand { get; }
    public RelayCommand AddNewRuleCommand { get; }
    public RelayCommand EnableAllCommand { get; }
    public RelayCommand DisableAllCommand { get; }
    public RelayCommand DeleteCardCommand { get; }
    public RelayCommand TestCardCommand { get; }
    public RelayCommand SaveEditorCommand { get; }
    public RelayCommand DeleteRuleCommand { get; }
    public RelayCommand TestRuleCommand { get; }

    private readonly List<TriggerRule> allRules = [];

    private void WireCollectionChanges()
    {
        foreach (var set in settings.MovementRedeemSets)
        {
            WireSet(set);
        }
        settings.MovementRedeemSets.CollectionChanged += (_, e) =>
        {
            if (e.NewItems is not null)
            {
                foreach (MovementRedeemSet set in e.NewItems)
                    WireSet(set);
            }
            if (e.OldItems is not null)
            {
                foreach (MovementRedeemSet set in e.OldItems)
                    UnwireSet(set);
            }
            RefreshCards();
        };
    }

    private void WireSet(MovementRedeemSet set)
    {
        allRules.AddRange(set.MovementRules);
        set.MovementRules.CollectionChanged += (_, _) => RefreshCards();
    }

    private void UnwireSet(MovementRedeemSet set)
    {
        foreach (var rule in set.MovementRules)
            allRules.Remove(rule);
    }

    private void RefreshCards()
    {
        allRules.Clear();
        foreach (var set in settings.MovementRedeemSets)
        {
            allRules.AddRange(set.MovementRules);
        }

        var filtered = allRules.AsEnumerable();

        if (!string.IsNullOrWhiteSpace(searchText))
        {
            var lower = searchText.ToLowerInvariant();
            filtered = filtered.Where(r =>
                r.Name?.ToLowerInvariant().Contains(lower) == true ||
                r.MovementDirection.ToString().ToLowerInvariant().Contains(lower));
        }

        Cards.Clear();
        foreach (var rule in filtered)
        {
            Cards.Add(new MovementRedeemCardViewModel(rule, OnTestCard));
        }

    }

    private void OnTestCard(MovementRedeemCardViewModel card)
    {
        mainWindowViewModel?.TestMovementRule(card.GetRule());
    }

    private void AddNewRule()
    {
        var firstSet = settings.MovementRedeemSets.FirstOrDefault();
        if (firstSet is null)
        {
            firstSet = new MovementRedeemSet { Name = "Default" };
            settings.MovementRedeemSets.Add(firstSet);
        }
        var rule = new TriggerRule
        {
            Name = "New Movement Rule",
            MovementDirection = PlayerMovementDirection.Forward,
            ActionType = OscActionType.PlayerMovement,
            DurationSeconds = 5,
            CooldownSeconds = 60
        };
        firstSet.MovementRules.Add(rule);
        RefreshCards();
        var card = Cards.FirstOrDefault(c => c.Id == rule.Id);
        if (card is not null)
        {
            SelectedCard = card;
            SelectedRule = rule;
            IsNewRule = true;
            IsEditorOpen = true;
        }
    }

    private void DeleteCard(MovementRedeemCardViewModel? card)
    {
        if (card is null) return;
        var rule = card.GetRule();
        foreach (var set in settings.MovementRedeemSets)
        {
            if (set.MovementRules.Remove(rule))
                break;
        }
        if (rule.CashPaymentRuleId is not null)
        {
            var cashRule = settings.CashPaymentRules.FirstOrDefault(r => r.Id.ToString() == rule.CashPaymentRuleId);
            if (cashRule is not null)
            {
                settings.CashPaymentRules.Remove(cashRule);
            }
            rule.CashPaymentRuleId = null;
            rule.Source = TriggerRuleSource.None;
        }
        if (SelectedCard == card)
        {
            SelectedCard = null;
            SelectedRule = null;
            IsEditorOpen = false;
        }
        RefreshCards();
    }

    private void TestCard(MovementRedeemCardViewModel? card)
    {
        if (card is null) return;
        OnTestCard(card);
    }

    private void EnableAll()
    {
        foreach (var set in settings.MovementRedeemSets)
        {
            foreach (var rule in set.MovementRules)
                rule.IsEnabled = true;
        }
    }

    private void DisableAll()
    {
        foreach (var set in settings.MovementRedeemSets)
        {
            foreach (var rule in set.MovementRules)
                rule.IsEnabled = false;
        }
    }

    private void SaveEditor()
    {
        var rule = selectedRule;
        if (rule is null) return;

        if (isCashPaymentEnabled)
        {
            var existing = rule.CashPaymentRuleId is not null
                ? settings.CashPaymentRules.FirstOrDefault(r => r.Id.ToString() == rule.CashPaymentRuleId)
                : null;
            CashPaymentRule cashRule;
            if (existing is not null)
            {
                cashRule = existing;
            }
            else
            {
                cashRule = new CashPaymentRule();
                var savedName = rule.Name;
                var savedTriggerType = rule.TriggerType;
                var savedRewardSyncMode = rule.RewardSyncMode;
                var savedChatCommandEnabled = rule.ChatCommandEnabled;
                var savedChatCommandText = rule.ChatCommandText;
                var savedChannelPointRewardId = rule.ChannelPointRewardId;
                var savedChannelPointRewardTitle = rule.ChannelPointRewardTitle;
                var savedMinimumAmount = rule.MinimumAmount;
                cashRule.TriggerAction = rule;
                rule.TriggerType = savedTriggerType;
                rule.RewardSyncMode = savedRewardSyncMode;
                rule.ChatCommandEnabled = savedChatCommandEnabled;
                rule.ChatCommandText = savedChatCommandText;
                rule.ChannelPointRewardId = savedChannelPointRewardId;
                rule.ChannelPointRewardTitle = savedChannelPointRewardTitle;
                rule.MinimumAmount = savedMinimumAmount;
                if (!string.IsNullOrWhiteSpace(savedName))
                    rule.Name = savedName;
                rule.CashPaymentRuleId = cashRule.Id.ToString();
                rule.Source = TriggerRuleSource.CashPayment;
                settings.CashPaymentRules.Add(cashRule);
            }
            cashRule.Name = rule.Name;
            cashRule.Provider = cashPaymentProvider;
            cashRule.MinimumAmount = cashPaymentMinimumAmount;
            cashRule.CurrencyCode = cashPaymentCurrencyCode;
            cashRule.CooldownSeconds = cashPaymentCooldown;
        }
        else if (rule.CashPaymentRuleId is not null)
        {
            var existing = settings.CashPaymentRules.FirstOrDefault(r => r.Id.ToString() == rule.CashPaymentRuleId);
            if (existing is not null)
            {
                settings.CashPaymentRules.Remove(existing);
            }
            rule.CashPaymentRuleId = null;
            rule.Source = TriggerRuleSource.None;
        }

        RefreshCards();
        IsEditorOpen = false;
    }

    public void OnTriggerTypeChanged()
    {
        RaisePropertyChanged(nameof(UsesChannelPointReward));
        RaisePropertyChanged(nameof(UsesChatCommand));
        RaisePropertyChanged(nameof(UsesBits));
        RaisePropertyChanged(nameof(UsesSubscription));
        RaisePropertyChanged(nameof(UsesGiftSub));
        RaisePropertyChanged(nameof(UsesFollow));
        RaisePropertyChanged(nameof(UsesCashPayment));
    }

    public void Dispose()
    {
        if (disposed) return;
        disposed = true;
    }
}

public sealed record MovementDirectionItem(PlayerMovementDirection Value)
{
    public string Display => MovementRedeemCardViewModel.GetDisplayName(Value);
}

public sealed record TwitchTriggerTypeOption(TwitchTriggerType Value, string Label)
{
    public override string ToString() => Label;
}
