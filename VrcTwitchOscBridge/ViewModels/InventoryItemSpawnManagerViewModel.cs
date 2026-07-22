using System;
using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using System.Windows.Data;
using VrcTwitchOscBridge.Infrastructure;
using VrcTwitchOscBridge.Models;
using VrcTwitchOscBridge.Services;

namespace VrcTwitchOscBridge.ViewModels;

public sealed class InventoryItemSpawnManagerViewModel : ObservableObject, IDisposable
{
    private readonly MainWindowViewModel _mainVm;
    private readonly InventoryItemImageService _imageService;
    private readonly ObservableCollection<InventoryItemSpawnCardViewModel> _cardsBacking = [];
    private ICollectionView? _cardsView;
    private InventoryItemSpawnRule? _selectedRule;
    private InventoryItemSummary? _selectedInventoryItem;
    private string _searchText = string.Empty;
    private string _inventorySearchText = string.Empty;
    private bool _isEditing;
    private bool _isLoadingInventory;
    private bool _disposed;

    public InventoryItemSpawnManagerViewModel(MainWindowViewModel mainVm)
    {
        _mainVm = mainVm ?? throw new ArgumentNullException(nameof(mainVm));
        _imageService = new InventoryItemImageService();
        var authCookie = _mainVm.Settings.VrChat.AuthCookie;
        if (!string.IsNullOrWhiteSpace(authCookie))
        {
            _imageService.SetAuthCookie(authCookie);
        }

        _mainVm.Settings.InventoryItemSpawnRules.CollectionChanged += OnRulesCollectionChanged;

        _cardsView = CollectionViewSource.GetDefaultView(_cardsBacking);
        _cardsView.Filter = OnCardFilter;

        AvailableInventoryItems = [];
        FilteredInventoryItems = CollectionViewSource.GetDefaultView(AvailableInventoryItems);
        FilteredInventoryItems.Filter = OnInventoryItemFilter;

        RebuildCards();

        AddNewRuleCommand = new RelayCommand(AddNewRule);
        EditRuleCommand = new RelayCommand(p => EditRule((InventoryItemSpawnRule?)p));
        DeleteRuleCommand = new RelayCommand(p => DeleteRule((InventoryItemSpawnRule?)p));
        SaveRuleCommand = new RelayCommand(SaveRule);
        CancelEditCommand = new RelayCommand(CancelEdit);
        RefreshInventoryCommand = new AsyncRelayCommand(RefreshInventoryAsync);
    }

    public ObservableCollection<InventoryItemSpawnCardViewModel> Cards => _cardsBacking;

    public ICollectionView? CardsView
    {
        get => _cardsView;
        private set => SetProperty(ref _cardsView, value);
    }

    public InventoryItemSpawnRule? SelectedRule
    {
        get => _selectedRule;
        set => SetProperty(ref _selectedRule, value);
    }

    public InventoryItemSummary? SelectedInventoryItem
    {
        get => _selectedInventoryItem;
        set
        {
            if (SetProperty(ref _selectedInventoryItem, value) && value is not null && SelectedRule is not null)
            {
                SelectedRule.InventoryItemId = value.Id;
                SelectedRule.ItemName = value.Name;
                SelectedRule.ItemImageUrl = value.ImageUrl;
                SelectedRule.ItemType = value.ItemType;
                if (string.IsNullOrWhiteSpace(SelectedRule.RewardTitle))
                {
                    SelectedRule.RewardTitle = $"VRC: {value.Name}";
                }
            }
        }
    }

    public string SearchText
    {
        get => _searchText;
        set
        {
            if (SetProperty(ref _searchText, value))
            {
                CardsView?.Refresh();
            }
        }
    }

    public string InventorySearchText
    {
        get => _inventorySearchText;
        set
        {
            if (SetProperty(ref _inventorySearchText, value))
            {
                FilteredInventoryItems?.Refresh();
            }
        }
    }

    public bool IsEditing
    {
        get => _isEditing;
        set => SetProperty(ref _isEditing, value);
    }

    public bool IsLoadingInventory
    {
        get => _isLoadingInventory;
        set => SetProperty(ref _isLoadingInventory, value);
    }

    public ObservableCollection<InventoryItemSummary> AvailableInventoryItems { get; }

    public ICollectionView FilteredInventoryItems { get; }

    public RelayCommand AddNewRuleCommand { get; }
    public RelayCommand EditRuleCommand { get; }
    public RelayCommand DeleteRuleCommand { get; }
    public RelayCommand SaveRuleCommand { get; }
    public RelayCommand CancelEditCommand { get; }
    public AsyncRelayCommand RefreshInventoryCommand { get; }

    private bool OnCardFilter(object obj)
    {
        if (obj is not InventoryItemSpawnCardViewModel card)
            return false;

        if (string.IsNullOrWhiteSpace(SearchText))
            return true;

        return card.ItemName.Contains(SearchText, StringComparison.OrdinalIgnoreCase)
            || card.DisplayTitle.Contains(SearchText, StringComparison.OrdinalIgnoreCase);
    }

    private bool OnInventoryItemFilter(object obj)
    {
        if (obj is not InventoryItemSummary item)
            return false;

        if (string.IsNullOrWhiteSpace(InventorySearchText))
            return true;

        return item.Name.Contains(InventorySearchText, StringComparison.OrdinalIgnoreCase);
    }

    private void RebuildCards()
    {
        foreach (var card in _cardsBacking)
            card.Dispose();
        _cardsBacking.Clear();

        foreach (var rule in _mainVm.Settings.InventoryItemSpawnRules)
        {
            _cardsBacking.Add(new InventoryItemSpawnCardViewModel(rule, _imageService));
        }

        CardsView?.Refresh();
    }

    private void OnRulesCollectionChanged(object? sender, System.Collections.Specialized.NotifyCollectionChangedEventArgs e)
    {
        RebuildCards();
    }

    private void AddNewRule()
    {
        var rule = new InventoryItemSpawnRule();
        _mainVm.Settings.InventoryItemSpawnRules.Add(rule);
        SelectedRule = rule;
        SelectedInventoryItem = null;
        IsEditing = true;
        _ = TryAutoRefreshInventoryAsync();
    }

    private void EditRule(InventoryItemSpawnRule? rule)
    {
        if (rule is null) return;
        SelectedRule = rule;
        SelectedInventoryItem = null;
        IsEditing = true;
        _ = TryAutoRefreshInventoryAsync();
    }

    private void DeleteRule(InventoryItemSpawnRule? rule)
    {
        if (rule is null) return;
        _mainVm.Settings.InventoryItemSpawnRules.Remove(rule);
        if (SelectedRule == rule)
        {
            SelectedRule = null;
            IsEditing = false;
        }
        _mainVm.QueueSave(0);
        _mainVm.QueueManagedRewardSyncPublic();
    }

    private void SaveRule()
    {
        IsEditing = false;
        _mainVm.QueueSave(0);
        _mainVm.QueueManagedRewardSyncPublic();
    }

    private void CancelEdit()
    {
        IsEditing = false;
    }

    private async Task TryAutoRefreshInventoryAsync()
    {
        if (AvailableInventoryItems.Count > 0)
            return;
        await RefreshInventoryAsync();
    }

    private async Task RefreshInventoryAsync()
    {
        IsLoadingInventory = true;
        AvailableInventoryItems.Clear();

        try
        {
            var authCookie = _mainVm.Settings.VrChat.AuthCookie;
            if (string.IsNullOrWhiteSpace(authCookie))
                return;

            _imageService.SetAuthCookie(authCookie);
            _imageService.ClearCache();
            RebuildCards();
            var items = await _mainVm.VrChatApiClient.GetInventoryPropsAsync(authCookie);

            foreach (var item in items)
                AvailableInventoryItems.Add(item);
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"Failed to refresh inventory: {ex.Message}");
        }
        finally
        {
            IsLoadingInventory = false;
        }
    }

    public void Dispose()
    {
        if (!_disposed)
        {
            _disposed = true;
            _mainVm.Settings.InventoryItemSpawnRules.CollectionChanged -= OnRulesCollectionChanged;
            foreach (var card in _cardsBacking)
                card.Dispose();
            _cardsBacking.Clear();
            _imageService.Dispose();
        }
    }
}
