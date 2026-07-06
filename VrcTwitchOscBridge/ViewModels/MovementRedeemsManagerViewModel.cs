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
    private MovementCategory? activeCategory;
    private bool isEditorOpen;
    private bool disposed;
    private TriggerRule? selectedRule;
    private bool isNewRule;

    public MovementRedeemsManagerViewModel(AppSettings settings, MainWindowViewModel? mainWindowViewModel)
    {
        this.settings = settings ?? throw new ArgumentNullException(nameof(settings));
        this.mainWindowViewModel = mainWindowViewModel;

        FilterCategoryCommand = new RelayCommand(p => ActiveCategory = p as MovementCategory?);
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

    public MovementCategory? ActiveCategory
    {
        get => activeCategory;
        set
        {
            if (SetProperty(ref activeCategory, value))
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
                RaisePropertyChanged(nameof(UsesBits));
                RaisePropertyChanged(nameof(UsesSubscription));
                RaisePropertyChanged(nameof(UsesFollow));
                RaisePropertyChanged(nameof(UsesGiftSub));
                RaisePropertyChanged(nameof(IsAxisType));
                RaisePropertyChanged(nameof(IsVrOnly));
                RaisePropertyChanged(nameof(EditorTitle));
            }
        }
    }

    public bool IsNewRule
    {
        get => isNewRule;
        set => SetProperty(ref isNewRule, value);
    }

    public string EditorTitle => IsNewRule ? "Add Movement Rule" : "Edit Movement Rule";

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

    public bool UsesChannelPointReward => selectedRule?.TriggerType == TwitchTriggerType.ChannelPoints;
    public bool UsesChatCommand => selectedRule?.TriggerType == TwitchTriggerType.ChatCommand;
    public bool UsesBits => selectedRule?.TriggerType == TwitchTriggerType.Bits;
    public bool UsesSubscription => selectedRule?.TriggerType == TwitchTriggerType.Subscriptions;
    public bool UsesGiftSub => selectedRule?.TriggerType == TwitchTriggerType.GiftSubscription;
    public bool UsesFollow => selectedRule?.TriggerType == TwitchTriggerType.Follow;
    public bool IsAxisType => selectedRule is not null && MovementTypeClassifier.IsAxisType(selectedRule.MovementDirection);
    public bool IsVrOnly => selectedRule is not null && MovementTypeClassifier.IsVrOnly(selectedRule.MovementDirection);

    public MovementRedeemCardViewModel? SelectedCard { get; set; }

    public ListCollectionView MovementDirections { get; }

    public RelayCommand FilterCategoryCommand { get; }
    public RelayCommand OpenEditorCommand { get; }
    public RelayCommand CloseEditorCommand { get; }
    public RelayCommand AddNewRuleCommand { get; }
    public RelayCommand DeleteCardCommand { get; }
    public RelayCommand TestCardCommand { get; }
    public RelayCommand SaveEditorCommand { get; }
    public RelayCommand DeleteRuleCommand { get; }
    public RelayCommand TestRuleCommand { get; }

    public int MovementCount => GetCategoryCount(MovementCategory.Movement);
    public int TurningCount => GetCategoryCount(MovementCategory.Turning);
    public int HandCount => GetCategoryCount(MovementCategory.HandInteractions);
    public int HeldObjectCount => GetCategoryCount(MovementCategory.HeldObject);
    public int UiTogglesCount => GetCategoryCount(MovementCategory.UiToggles);

    private int GetCategoryCount(MovementCategory category) =>
        allRules.Count(r => MovementTypeClassifier.GetCategory(r.MovementDirection) == category);

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

        if (activeCategory.HasValue)
        {
            var cat = activeCategory.Value;
            filtered = filtered.Where(r => MovementTypeClassifier.GetCategory(r.MovementDirection) == cat);
        }

        Cards.Clear();
        foreach (var rule in filtered)
        {
            Cards.Add(new MovementRedeemCardViewModel(rule, OnTestCard));
        }

        RaisePropertyChanged(nameof(MovementCount));
        RaisePropertyChanged(nameof(TurningCount));
        RaisePropertyChanged(nameof(HandCount));
        RaisePropertyChanged(nameof(HeldObjectCount));
        RaisePropertyChanged(nameof(UiTogglesCount));
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

    private void SaveEditor()
    {
        if (SelectedRule is null) return;
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
