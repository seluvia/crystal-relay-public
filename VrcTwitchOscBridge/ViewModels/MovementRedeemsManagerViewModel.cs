using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Linq;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
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

    public IRelayCommand<MovementCategory?> FilterCategoryCommand { get; }
    public IRelayCommand OpenEditorCommand { get; }
    public IRelayCommand CloseEditorCommand { get; }
    public IRelayCommand AddNewRuleCommand { get; }
    public IRelayCommand DeleteCardCommand { get; }
    public IRelayCommand TestCardCommand { get; }

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

        OnPropertyChanged(nameof(MovementCount));
        OnPropertyChanged(nameof(TurningCount));
        OnPropertyChanged(nameof(HandCount));
        OnPropertyChanged(nameof(HeldObjectCount));
        OnPropertyChanged(nameof(UiTogglesCount));
    }

    private void OnTestCard(MovementRedeemCardViewModel card)
    {
        mainWindowViewModel?.TestMovementRule(card.GetRule());
    }

    public void Dispose()
    {
        if (disposed) return;
        disposed = true;
    }
}
