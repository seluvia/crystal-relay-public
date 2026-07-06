using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Linq;
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

    public MovementRedeemsManagerViewModel(AppSettings settings, MainWindowViewModel? mainWindowViewModel)
    {
        this.settings = settings ?? throw new ArgumentNullException(nameof(settings));
        this.mainWindowViewModel = mainWindowViewModel;

        FilterCategoryCommand = new RelayCommand(p => ActiveCategory = p as MovementCategory?);
        OpenEditorCommand = new RelayCommand(p => { SelectedCard = p as MovementRedeemCardViewModel; IsEditorOpen = SelectedCard is not null; });
        CloseEditorCommand = new RelayCommand(() => IsEditorOpen = false);
        AddNewRuleCommand = new RelayCommand(AddNewRule);
        DeleteCardCommand = new RelayCommand(p => DeleteCard(p as MovementRedeemCardViewModel));
        TestCardCommand = new RelayCommand(p => TestCard(p as MovementRedeemCardViewModel));

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

    public MovementRedeemCardViewModel? SelectedCard { get; set; }

    public RelayCommand FilterCategoryCommand { get; }
    public RelayCommand OpenEditorCommand { get; }
    public RelayCommand CloseEditorCommand { get; }
    public RelayCommand AddNewRuleCommand { get; }
    public RelayCommand DeleteCardCommand { get; }
    public RelayCommand TestCardCommand { get; }

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
        var rule = new TriggerRule { Name = "New Movement Rule", MovementDirection = PlayerMovementDirection.Forward };
        firstSet.MovementRules.Add(rule);
        RefreshCards();
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
            SelectedCard = null;
        RefreshCards();
    }

    private void TestCard(MovementRedeemCardViewModel? card)
    {
        if (card is null) return;
        OnTestCard(card);
    }

    public void Dispose()
    {
        if (disposed) return;
        disposed = true;
    }
}
