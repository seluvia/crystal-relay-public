# Inventory Item Spawn Reward System Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Allow streamers to create Twitch channel point rewards that spawn VRChat inventory props in-world via the VRChat Inventory API.

**Architecture:** Follows the exact same patterns as the existing Avatar Sets system — manager window, card grid, slide-in editor, Twitch reward sync (CreateOrManage / LinkExisting). New API methods in `VrChatApiClient` for fetching inventory and spawning items. Dispatch in `BridgeCoordinator` between Wardrobe and Avatar Set rules.

**Tech Stack:** C# / WPF / .NET 10 (`net10.0-windows`)

## Global Constraints

- All new files go under `E:\!!!Program to work on\Proper Crystal Relay\VrcTwitchOscBridge\`
- Models go in `Models/`; Services go in `Services/`; ViewModels go in `ViewModels/`; Windows at root of app project
- All manager windows use `WindowStyle="None"` custom chrome with `WindowChrome`
- All manager ViewModels extend `ObservableObject` and implement `IDisposable`
- All ViewModels take `MainWindowViewModel` as a constructor dependency
- VRChat API calls use the auth cookie pattern from `VrChatApiClient`
- Test files go in `VrcTwitchOscBridge.Tests\` following `ManagerWindowXamlTests.cs` pattern
- Three `csproj` includes per manager window: `<Page>`, `<Compile>.xaml.cs`, `<Compile>ViewModel.cs`
- `EnableDefaultItems=false` in the csproj — all new files must be explicitly included

---

### Task 1: Create Models

**Files:**
- Create: `E:\!!!Program to work on\Proper Crystal Relay\VrcTwitchOscBridge\Models\InventoryItemSpawnRule.cs`
- Create: `E:\!!!Program to work on\Proper Crystal Relay\VrcTwitchOscBridge\Models\InventoryItemSummary.cs`

**Interfaces:**
- Consumes: Nothing (standalone models)
- Produces: `InventoryItemSpawnRule` (serializable rule with Twitch reward config), `InventoryItemSummary` (lightweight API display model)

- [ ] **Step 1: Create `InventoryItemSummary.cs`**

```csharp
using System.Collections.Generic;

namespace VrcTwitchOscBridge.Models;

public sealed record InventoryItemSummary(
    string Id,
    string Name,
    string ImageUrl,
    string Description,
    string ItemType,
    IReadOnlyList<string> Tags,
    IReadOnlyList<string> Flags
);
```

- [ ] **Step 2: Create `InventoryItemSpawnRule.cs`**

```csharp
using System;
using System.Text.Json.Serialization;
using VrcTwitchOscBridge.Infrastructure;

namespace VrcTwitchOscBridge.Models;

public sealed class InventoryItemSpawnRule : ObservableObject, IJsonOnDeserialized
{
    private string id = Guid.NewGuid().ToString();
    private string inventoryItemId = string.Empty;
    private string itemName = string.Empty;
    private string itemImageUrl = string.Empty;
    private string itemType = string.Empty;
    private string? rewardId;
    private string rewardTitle = string.Empty;
    private int rewardCost = 100;
    private TwitchRewardSyncMode syncMode = TwitchRewardSyncMode.CreateOrManage;
    private bool isEnabled = true;
    private int cooldownSeconds;
    private string rewardVersionFingerprint = string.Empty;

    public string Id
    {
        get => id;
        set => SetProperty(ref id, value ?? Guid.NewGuid().ToString());
    }

    public string InventoryItemId
    {
        get => inventoryItemId;
        set => SetProperty(ref inventoryItemId, value ?? string.Empty);
    }

    public string ItemName
    {
        get => itemName;
        set => SetProperty(ref itemName, value ?? string.Empty);
    }

    public string ItemImageUrl
    {
        get => itemImageUrl;
        set => SetProperty(ref itemImageUrl, value ?? string.Empty);
    }

    public string ItemType
    {
        get => itemType;
        set => SetProperty(ref itemType, value ?? string.Empty);
    }

    public string? RewardId
    {
        get => rewardId;
        set => SetProperty(ref rewardId, value);
    }

    public string RewardTitle
    {
        get => rewardTitle;
        set => SetProperty(ref rewardTitle, value ?? string.Empty);
    }

    public int RewardCost
    {
        get => rewardCost;
        set => SetProperty(ref rewardCost, Math.Max(1, value));
    }

    public TwitchRewardSyncMode SyncMode
    {
        get => syncMode;
        set => SetProperty(ref syncMode, value);
    }

    public bool IsEnabled
    {
        get => isEnabled;
        set => SetProperty(ref isEnabled, value);
    }

    public int CooldownSeconds
    {
        get => cooldownSeconds;
        set => SetProperty(ref cooldownSeconds, Math.Max(0, value));
    }

    public string RewardVersionFingerprint
    {
        get => rewardVersionFingerprint;
        set => SetProperty(ref rewardVersionFingerprint, value ?? string.Empty);
    }

    public string DisplayTitle => !string.IsNullOrWhiteSpace(rewardTitle) ? rewardTitle : itemName;

    public string? SyncStatusBadge => syncMode switch
    {
        TwitchRewardSyncMode.CreateOrManage => string.IsNullOrWhiteSpace(rewardId) ? "Not Created" : "Created",
        TwitchRewardSyncMode.LinkExisting => string.IsNullOrWhiteSpace(rewardId) ? "Not Linked" : "Linked",
        _ => null
    };

    public void OnDeserialized() { }
}
```

---

### Task 2: Add Collection to AppSettings

**Files:**
- Modify: `E:\!!!Program to work on\Proper Crystal Relay\VrcTwitchOscBridge\Models\AppSettings.cs`

**Interfaces:**
- Consumes: `InventoryItemSpawnRule` model
- Produces: `Settings.InventoryItemSpawnRules` property (used by all later tasks)

- [ ] **Step 1: Add backing field near line 33** (after `cashPaymentRules`)

```csharp
private ObservableCollection<InventoryItemSpawnRule> inventoryItemSpawnRules = [];
```

- [ ] **Step 2: Add property near the AvatarProfiles block (around line 140)**

```csharp
public ObservableCollection<InventoryItemSpawnRule> InventoryItemSpawnRules
{
    get => inventoryItemSpawnRules;
    set => SetProperty(ref inventoryItemSpawnRules, value ?? []);
}
```

---

### Task 3: Add VRChat Inventory API Methods

**Files:**
- Modify: `E:\!!!Program to work on\Proper Crystal Relay\VrcTwitchOscBridge\Services\VrChatApiRoutes.cs`
- Modify: `E:\!!!Program to work on\Proper Crystal Relay\VrcTwitchOscBridge\Services\VrChatApiClient.cs`

**Interfaces:**
- Consumes: `InventoryItemSummary` model
- Produces: `VrChatApiClient.GetInventoryPropsAsync(authCookie, ct)` and `VrChatApiClient.SpawnInventoryItemAsync(authCookie, itemId, ct)`

- [ ] **Step 1: Add route constants to `VrChatApiRoutes.cs`**

```csharp
public const string Inventory = "inventory";

public static string SpawnInventoryItem(string itemId) =>
    $"inventory/spawn?id={Uri.EscapeDataString(itemId)}";
```

- [ ] **Step 2: Add deserialization records and methods to `VrChatApiClient.cs`**

Add inside the class, after `VrChatWorldRecord`:

```csharp
private sealed record InventoryRecord(
    string Id,
    string Name,
    string? Description,
    string? ImageUrl,
    string? ItemType,
    IReadOnlyList<string>? Tags,
    IReadOnlyList<string>? Flags);

private sealed record InventoryListResponse(
    List<InventoryRecord>? Data,
    int TotalCount);

private sealed record InventorySpawnResponse(
    string? Token,
    int Version);
```

- [ ] **Step 3: Add `GetInventoryPropsAsync` method to `VrChatApiClient`**

After `GetSelectableAvatarsAsync`:

```csharp
public async Task<List<InventoryItemSummary>> GetInventoryPropsAsync(
    string authCookie, CancellationToken ct = default)
{
    var result = new List<InventoryItemSummary>();
    const int pageSize = 100;
    var offset = 0;

    while (true)
    {
        var request = CreateRequest(HttpMethod.Get,
            $"inventory?types=prop&flags=instantiatable&n={pageSize}&offset={offset}",
            authCookie);

        using var response = await httpClient.SendAsync(request,
            HttpCompletionOption.ResponseHeadersRead, ct);

        if (!response.IsSuccessStatusCode)
        {
            throw new VrChatApiException(response.StatusCode,
                $"Failed to fetch inventory: {response.ReasonPhrase}");
        }

        var body = await response.Content.ReadFromJsonAsync<InventoryListResponse>(JsonOptions, ct);
        if (body?.Data is null || body.Data.Count == 0)
            break;

        foreach (var item in body.Data)
        {
            result.Add(new InventoryItemSummary(
                item.Id,
                item.Name ?? "Unknown",
                item.ImageUrl ?? string.Empty,
                item.Description ?? string.Empty,
                item.ItemType ?? "prop",
                item.Tags ?? [],
                item.Flags ?? []));
        }

        offset += pageSize;
        if (offset >= body.TotalCount)
            break;
    }

    return result;
}
```

- [ ] **Step 4: Add `SpawnInventoryItemAsync` method**

After `GetInventoryPropsAsync`:

```csharp
public async Task SpawnInventoryItemAsync(
    string authCookie, string inventoryItemId, CancellationToken ct = default)
{
    if (string.IsNullOrWhiteSpace(inventoryItemId))
        throw new ArgumentException("Inventory item ID is required.", nameof(inventoryItemId));

    var request = CreateRequest(HttpMethod.Get,
        VrChatApiRoutes.SpawnInventoryItem(inventoryItemId), authCookie);

    using var response = await httpClient.SendAsync(request,
        HttpCompletionOption.ResponseHeadersRead, ct);

    if (!response.IsSuccessStatusCode)
    {
        throw new VrChatApiException(response.StatusCode,
            $"Failed to spawn inventory item: {response.ReasonPhrase}");
    }

    // Response body contains { "token": "...", "version": 0 } — no further processing needed
    await response.Content.ReadFromJsonAsync<InventorySpawnResponse>(JsonOptions, ct);
}
```

- [ ] **Step 5: Add `using VrcTwitchOscBridge.Models;` if not already present** (it should already be at the top of `VrChatApiClient.cs`)

---

### Task 4: Create Inventory Item Image Service

**Files:**
- Create: `E:\!!!Program to work on\Proper Crystal Relay\VrcTwitchOscBridge\Services\InventoryItemImageService.cs`

**Interfaces:**
- Consumes: auth cookie (set externally)
- Produces: `InventoryItemImageService` — loads and caches item thumbnail images via auth-cookie-authenticated HTTP requests

- [ ] **Step 1: Create the service file**

```csharp
using System;
using System.Collections.Concurrent;
using System.Net.Http;
using System.Threading;
using System.Threading.Tasks;
using System.Windows.Media.Imaging;

namespace VrcTwitchOscBridge.Services;

public sealed class InventoryItemImageService : IDisposable
{
    private readonly HttpClient _httpClient = new();
    private readonly ConcurrentDictionary<string, BitmapImage?> _cache = new();
    private string? _authCookie;
    private bool _disposed;

    public void SetAuthCookie(string authCookie)
    {
        _authCookie = authCookie;
    }

    public async Task<BitmapImage?> LoadImageAsync(string imageUrl, CancellationToken ct = default)
    {
        if (string.IsNullOrWhiteSpace(imageUrl))
            return null;

        if (_cache.TryGetValue(imageUrl, out var cached))
            return cached;

        try
        {
            using var request = new HttpRequestMessage(HttpMethod.Get, imageUrl);
            if (!string.IsNullOrWhiteSpace(_authCookie))
            {
                request.Headers.TryAddWithoutValidation("Cookie", $"auth={_authCookie.Trim()}");
            }

            using var response = await _httpClient.SendAsync(request,
                HttpCompletionOption.ResponseHeadersRead, ct);

            if (!response.IsSuccessStatusCode)
                return null;

            using var stream = await response.Content.ReadAsStreamAsync(ct);
            var bitmap = new BitmapImage();
            bitmap.BeginInit();
            bitmap.CacheOption = BitmapCacheOption.OnLoad;
            bitmap.StreamSource = stream;
            bitmap.EndInit();
            bitmap.Freeze();

            _cache[imageUrl] = bitmap;
            return bitmap;
        }
        catch
        {
            _cache[imageUrl] = null;
            return null;
        }
    }

    public void ClearCache() => _cache.Clear();

    public void Dispose()
    {
        if (!_disposed)
        {
            _disposed = true;
            _httpClient.Dispose();
            _cache.Clear();
        }
    }
}
```

---

### Task 5: Add Spawn Rules to Bridge Runtime Configuration

**Files:**
- Modify: `E:\!!!Program to work on\Proper Crystal Relay\VrcTwitchOscBridge\Services\BridgeRuntimeConfiguration.cs`

**Interfaces:**
- Consumes: `BridgeRuntimeConfiguration` record, `InventoryItemSpawnRule` model
- Produces: `BridgeRuntimeConfiguration.InventoryItemSpawnRules` property

- [ ] **Step 1: Add inventory spawn rules to the `BridgeRuntimeConfiguration` record**

Add a new parameter after `AvatarRouletteProfiles`:

```csharp
IReadOnlyList<InventoryItemSpawnRule> InventoryItemSpawnRules
```

- [ ] **Step 2: Add to `FromSettings`**

In `FromSettings`, after the roulette profiles line, add:

```csharp
var inventoryItemSpawnRules = settings.InventoryItemSpawnRules
    .Where(r => r.IsEnabled)
    .ToArray() as IReadOnlyList<InventoryItemSpawnRule>;
```

- [ ] **Step 3: Pass to record constructor**

Add `inventoryItemSpawnRules` (or the backing variable) as the last argument to the record constructor call.

---

### Task 6: Create Card ViewModel

**Files:**
- Create: `E:\!!!Program to work on\Proper Crystal Relay\VrcTwitchOscBridge\ViewModels\InventoryItemSpawnCardViewModel.cs`

**Interfaces:**
- Consumes: `InventoryItemSpawnRule`, `InventoryItemImageService`
- Produces: Card VM with display properties consumed by the manager window card grid

- [ ] **Step 1: Create the card ViewModel**

```csharp
using System;
using System.ComponentModel;
using System.Threading;
using System.Threading.Tasks;
using System.Windows.Media.Imaging;
using VrcTwitchOscBridge.Infrastructure;
using VrcTwitchOscBridge.Models;
using VrcTwitchOscBridge.Services;

namespace VrcTwitchOscBridge.ViewModels;

public sealed class InventoryItemSpawnCardViewModel : ObservableObject, IDisposable
{
    private readonly InventoryItemImageService _imageService;
    private CancellationTokenSource? _imageCts;
    private BitmapImage? _thumbnail;
    private bool _disposed;

    public InventoryItemSpawnCardViewModel(
        InventoryItemSpawnRule rule,
        InventoryItemImageService imageService)
    {
        Rule = rule ?? throw new ArgumentNullException(nameof(rule));
        _imageService = imageService ?? throw new ArgumentNullException(nameof(imageService));
        Rule.PropertyChanged += OnRulePropertyChanged;
        _ = LoadThumbnailAsync();
    }

    public InventoryItemSpawnRule Rule { get; }

    public string DisplayTitle => Rule.DisplayTitle;

    public string ItemName => Rule.ItemName;

    public string ItemType => Rule.ItemType;

    public bool IsEnabled => Rule.IsEnabled;

    public int RewardCost => Rule.RewardCost;

    public int CooldownSeconds => Rule.CooldownSeconds;

    public string? SyncStatusBadge => Rule.SyncStatusBadge;

    public string SyncModeLabel => Rule.SyncMode switch
    {
        TwitchRewardSyncMode.CreateOrManage => "Managed",
        TwitchRewardSyncMode.LinkExisting => "Linked",
        _ => "Unknown"
    };

    public BitmapImage? Thumbnail
    {
        get => _thumbnail;
        private set => SetProperty(ref _thumbnail, value);
    }

    private async Task LoadThumbnailAsync()
    {
        _imageCts?.Cancel();
        _imageCts?.Dispose();
        _imageCts = new CancellationTokenSource();

        try
        {
            Thumbnail = await _imageService.LoadImageAsync(Rule.ItemImageUrl, _imageCts.Token);
        }
        catch (OperationCanceledException)
        {
        }
    }

    private void OnRulePropertyChanged(object? sender, PropertyChangedEventArgs e)
    {
        switch (e.PropertyName)
        {
            case nameof(RewardTitle):
            case nameof(ItemName):
                RaisePropertyChanged(nameof(DisplayTitle));
                break;
            case nameof(IsEnabled):
                RaisePropertyChanged(nameof(IsEnabled));
                break;
            case nameof(RewardCost):
                RaisePropertyChanged(nameof(RewardCost));
                break;
            case nameof(CooldownSeconds):
                RaisePropertyChanged(nameof(CooldownSeconds));
                break;
            case nameof(SyncMode):
                RaisePropertyChanged(nameof(SyncModeLabel));
                RaisePropertyChanged(nameof(SyncStatusBadge));
                break;
            case nameof(RewardId):
                RaisePropertyChanged(nameof(SyncStatusBadge));
                break;
            case nameof(ItemImageUrl):
                _ = LoadThumbnailAsync();
                break;
        }
    }

    public void Dispose()
    {
        if (!_disposed)
        {
            _disposed = true;
            _imageCts?.Cancel();
            _imageCts?.Dispose();
            Rule.PropertyChanged -= OnRulePropertyChanged;
        }
    }
}
```

---

### Task 7: Create Manager ViewModel

**Files:**
- Create: `E:\!!!Program to work on\Proper Crystal Relay\VrcTwitchOscBridge\ViewModels\InventoryItemSpawnManagerViewModel.cs`

**Interfaces:**
- Consumes: `InventoryItemSpawnRule`, `InventoryItemSummary`, `InventoryItemImageService`, `VrChatApiClient`, `MainWindowViewModel`
- Produces: `InventoryItemSpawnManagerViewModel` as DataContext for the manager window

- [ ] **Step 1: Create the manager ViewModel**

```csharp
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
    private bool _isEditing;
    private bool _isLoadingInventory;
    private bool _disposed;

    public InventoryItemSpawnManagerViewModel(MainWindowViewModel mainVm)
    {
        _mainVm = mainVm ?? throw new ArgumentNullException(nameof(mainVm));
        _imageService = new InventoryItemImageService();

        _mainVm.Settings.InventoryItemSpawnRules.CollectionChanged += OnRulesCollectionChanged;

        _cardsView = CollectionViewSource.GetDefaultView(_cardsBacking);
        _cardsView.Filter = OnCardFilter;

        AvailableInventoryItems = [];
        FilteredInventoryItems = CollectionViewSource.GetDefaultView(AvailableInventoryItems);
        FilteredInventoryItems.Filter = OnInventoryItemFilter;

        RebuildCards();

        // Commands
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
                    SelectedRule.RewardTitle = value.Name;
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

    // Commands
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

        if (string.IsNullOrWhiteSpace(SearchText))
            return true;

        return item.Name.Contains(SearchText, StringComparison.OrdinalIgnoreCase);
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
    }

    private void EditRule(InventoryItemSpawnRule? rule)
    {
        if (rule is null) return;
        SelectedRule = rule;
        SelectedInventoryItem = null;
        IsEditing = true;
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
        _mainVm.QueueManagedRewardSync(0);
    }

    private void SaveRule()
    {
        IsEditing = false;
        _mainVm.QueueSave(0);
        _mainVm.QueueBridgeRefresh();
        _mainVm.QueueManagedRewardSync(0);
    }

    private void CancelEdit()
    {
        IsEditing = false;
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
            var items = await _mainVm.VrChatApi.GetInventoryPropsAsync(authCookie);

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
```

---

### Task 8: Create Manager Window (XAML + Code-behind)

**Files:**
- Create: `E:\!!!Program to work on\Proper Crystal Relay\VrcTwitchOscBridge\InventoryItemSpawnManagerWindow.xaml`
- Create: `E:\!!!Program to work on\Proper Crystal Relay\VrcTwitchOscBridge\InventoryItemSpawnManagerWindow.xaml.cs`

**Interfaces:**
- Consumes: `InventoryItemSpawnManagerViewModel` (set as DataContext)
- Produces: Themed manager window

- [ ] **Step 1: Create the XAML file**

```xml
<Window x:Class="VrcTwitchOscBridge.InventoryItemSpawnManagerWindow"
        xmlns="http://schemas.microsoft.com/winfx/2006/xaml/presentation"
        xmlns:x="http://schemas.microsoft.com/winfx/2006/xaml"
        xmlns:loc="clr-namespace:VrcTwitchOscBridge.Services"
        xmlns:vm="clr-namespace:VrcTwitchOscBridge.ViewModels"
        xmlns:models="clr-namespace:VrcTwitchOscBridge.Models"
        xmlns:shell="clr-namespace:System.Windows.Shell;assembly=PresentationFramework"
        xmlns:d="http://schemas.microsoft.com/expression/blend/2008"
        xmlns:mc="http://schemas.openxmlformats.org/markup-compatibility/2006"
        mc:Ignorable="d"
        d:DataContext="{d:DesignInstance Type=vm:InventoryItemSpawnManagerViewModel}"
        Title="Inventory Item Spawns"
        Icon="Assets/crystal-relay-icon.ico"
        Width="900" Height="650" MinWidth="700" MinHeight="450"
        WindowStyle="None" WindowStartupLocation="CenterOwner"
        FontFamily="{DynamicResource BodyFontFamily}"
        UseLayoutRounding="True" SnapsToDevicePixels="True"
        Background="{DynamicResource WindowBackgroundBrush}">
    <shell:WindowChrome.WindowChrome>
        <shell:WindowChrome CaptionHeight="0" CornerRadius="0" GlassFrameThickness="0" ResizeBorderThickness="6" UseAeroCaptionButtons="False" />
    </shell:WindowChrome.WindowChrome>
    <Window.Resources>
        <ResourceDictionary>
            <ResourceDictionary.MergedDictionaries>
                <ResourceDictionary Source="/Resources/ThemeStyles.xaml" />
            </ResourceDictionary.MergedDictionaries>
            <!-- Copy the same brush/font resources pattern from AvatarSetsManagerWindow.xaml -->
            <SolidColorBrush x:Key="CardBackgroundBrush" Color="#1E1E1E" />
            <SolidColorBrush x:Key="CardBorderBrush" Color="#333333" />
            <SolidColorBrush x:Key="CardHoverBrush" Color="#2A2A2A" />
            <SolidColorBrush x:Key="PillCreatedBrush" Color="#4CAF50" />
            <SolidColorBrush x:Key="PillLinkedBrush" Color="#2196F3" />
            <SolidColorBrush x:Key="PillUnlinkedBrush" Color="#FF9800" />
            <SolidColorBrush x:Key="ToolbarBackgroundBrush" Color="#1A1A1A" />
            <SolidColorBrush x:Key="EditorPanelBackgroundBrush" Color="#222222" />
            <SolidColorBrush x:Key="EditorBorderBrush" Color="#3A3A3A" />
            <!-- Fonts -->
            <FontFamily x:Key="BodyFontFamily">Segoe UI</FontFamily>
            <FontFamily x:Key="HeadingFontFamily">Segoe UI Semibold</FontFamily>
            <!-- Converters from Converters namespace if used -->
        </ResourceDictionary>
    </Window.Resources>
    <Grid>
        <Grid.RowDefinitions>
            <RowDefinition Height="42" />
            <RowDefinition Height="*" />
        </Grid.RowDefinitions>

        <!-- Title Bar -->
        <Border Grid.Row="0" Background="{DynamicResource WindowBackgroundBrush}" MouseLeftButtonDown="OnTitleBarMouseDown">
            <Grid>
                <Grid.ColumnDefinitions>
                    <ColumnDefinition Width="Auto" />
                    <ColumnDefinition Width="*" />
                    <ColumnDefinition Width="Auto" />
                </Grid.ColumnDefinitions>
                <TextBlock Grid.Column="0" Text="Inventory Item Spawns" FontFamily="{DynamicResource HeadingFontFamily}" FontSize="14" FontWeight="SemiBold" Foreground="{DynamicResource TextBrush}" Margin="14,0,0,0" VerticalAlignment="Center" />
                <StackPanel Grid.Column="2" Orientation="Horizontal" Margin="0,0,4,0">
                    <Button x:Name="MinimizeButton" Content="─" Style="{DynamicResource WindowButtonStyle}" Width="34" Height="28" Click="OnMinimizeClicked" />
                    <Button x:Name="CloseButton" Content="✕" Style="{DynamicResource WindowButtonStyle}" Width="34" Height="28" Click="OnCloseClicked" />
                </StackPanel>
            </Grid>
        </Border>

        <!-- Content -->
        <Grid Grid.Row="1" Margin="0,4,0,0">
            <Grid.ColumnDefinitions>
                <ColumnDefinition Width="1.5*" MinWidth="400" />
                <ColumnDefinition Width="Auto" />
                <ColumnDefinition Width="*" MinWidth="260" />
            </Grid.ColumnDefinitions>

            <!-- Left: Card Grid -->
            <Grid Grid.Column="0" Margin="12,8,4,12">
                <Grid.RowDefinitions>
                    <RowDefinition Height="Auto" />
                    <RowDefinition Height="*" />
                </Grid.RowDefinitions>

                <!-- Toolbar -->
                <Border Grid.Row="0" Background="{StaticResource ToolbarBackgroundBrush}" CornerRadius="6" Padding="8" Margin="0,0,0,8">
                    <Grid>
                        <Grid.ColumnDefinitions>
                            <ColumnDefinition Width="*" />
                            <ColumnDefinition Width="Auto" />
                            <ColumnDefinition Width="Auto" />
                        </Grid.ColumnDefinitions>
                        <TextBox Grid.Column="0" Text="{Binding SearchText, UpdateSourceTrigger=PropertyChanged}" 
                                 Style="{DynamicResource SearchTextBoxStyle}" 
                                 PlaceholderText="Search items..." Margin="0,0,8,0" />
                        <Button Grid.Column="1" Content="Refresh" Command="{Binding RefreshInventoryCommand}" 
                                Style="{DynamicResource NavCardActionButtonStyle}" Margin="0,0,8,0" />
                        <Button Grid.Column="2" Content="+ New Spawn" Command="{Binding AddNewRuleCommand}" 
                                Style="{DynamicResource AccentButtonStyle}" />
                    </Grid>
                </Border>

                <!-- Card Grid -->
                <ScrollViewer Grid.Row="1" VerticalScrollBarVisibility="Auto" HorizontalScrollBarVisibility="Disabled" Padding="0,0,4,0">
                    <ItemsControl ItemsSource="{Binding CardsView}">
                        <ItemsControl.ItemsPanel>
                            <ItemsPanelTemplate>
                                <WrapPanel Orientation="Horizontal" ItemWidth="220" ItemHeight="260" />
                            </ItemsPanelTemplate>
                        </ItemsControl.ItemsPanel>
                        <ItemsControl.ItemTemplate>
                            <DataTemplate DataType="{x:Type vm:InventoryItemSpawnCardViewModel}">
                                <Border Background="{StaticResource CardBackgroundBrush}" BorderBrush="{StaticResource CardBorderBrush}" 
                                        BorderThickness="1" CornerRadius="8" Margin="4" Padding="10">
                                    <Grid>
                                        <Grid.RowDefinitions>
                                            <RowDefinition Height="100" />
                                            <RowDefinition Height="Auto" />
                                            <RowDefinition Height="*" />
                                            <RowDefinition Height="Auto" />
                                        </Grid.RowDefinitions>

                                        <!-- Thumbnail -->
                                        <Border Grid.Row="0" Background="#2A2A2A" CornerRadius="6" Margin="0,0,0,6">
                                            <Image Source="{Binding Thumbnail}" Width="160" Height="90" Stretch="Uniform" />
                                        </Border>

                                        <!-- Title -->
                                        <TextBlock Grid.Row="1" Text="{Binding DisplayTitle}" FontFamily="{DynamicResource HeadingFontFamily}" 
                                                   FontSize="13" FontWeight="SemiBold" Foreground="{DynamicResource TextBrush}" 
                                                   TextTrimming="CharacterEllipsis" Margin="0,0,0,2" />

                                        <!-- Info -->
                                        <StackPanel Grid.Row="2" VerticalAlignment="Top" Margin="0,0,0,4">
                                            <TextBlock Text="{Binding SyncModeLabel}" FontSize="10" Foreground="{DynamicResource MutedBrush}" />
                                            <TextBlock Text="{Binding SyncStatusBadge}" FontSize="10" Foreground="{DynamicResource MutedBrush}" />
                                            <TextBlock Text="{Binding RewardCost, StringFormat='{}{0} pts'}" FontSize="10" Foreground="{DynamicResource MutedBrush}" />
                                        </StackPanel>

                                        <!-- Edit/Delete Buttons -->
                                        <StackPanel Grid.Row="3" Orientation="Horizontal" HorizontalAlignment="Right">
                                            <Button Content="Edit" Command="{Binding DataContext.EditRuleCommand, RelativeSource={RelativeSource AncestorType=Window}}" 
                                                    CommandParameter="{Binding Rule}" Style="{DynamicResource NavCardActionButtonStyle}" Margin="0,0,4,0" />
                                            <Button Content="Del" Command="{Binding DataContext.DeleteRuleCommand, RelativeSource={RelativeSource AncestorType=Window}}" 
                                                    CommandParameter="{Binding Rule}" Style="{DynamicResource NavCardActionButtonStyle}" />
                                        </StackPanel>
                                    </Grid>
                                </Border>
                            </DataTemplate>
                        </ItemsControl.ItemTemplate>
                    </ItemsControl>
                </ScrollViewer>
            </Grid>

            <!-- Divider -->
            <GridSplitter Grid.Column="1" Width="4" HorizontalAlignment="Center" VerticalAlignment="Stretch" 
                          Background="{DynamicResource ControlDefaultBorderBrush}" ResizeBehavior="PreviousAndNext" />

            <!-- Right: Editor Panel -->
            <Border Grid.Column="2" Background="{StaticResource EditorPanelBackgroundBrush}" CornerRadius="8" Margin="4,8,12,12" 
                    BorderBrush="{StaticResource EditorBorderBrush}" BorderThickness="1"
                    Visibility="{Binding IsEditing, Converter={StaticResource BoolToVisibilityConverter}}">
                <ScrollViewer VerticalScrollBarVisibility="Auto" Padding="14">
                    <StackPanel>
                        <TextBlock Text="Edit Spawn Rule" FontFamily="{DynamicResource HeadingFontFamily}" FontSize="14" 
                                   FontWeight="SemiBold" Foreground="{DynamicResource TextBrush}" Margin="0,0,0,12" />

                        <!-- Item Picker -->
                        <TextBlock Text="Select Item" FontWeight="SemiBold" Foreground="{DynamicResource TextBrush}" Margin="0,0,0,4" />
                        <TextBox Text="{Binding SearchText, UpdateSourceTrigger=PropertyChanged}" 
                                 PlaceholderText="Search inventory..." Margin="0,0,0,6" />
                        <ListBox ItemsSource="{Binding FilteredInventoryItems}" 
                                 SelectedItem="{Binding SelectedInventoryItem}" 
                                 Height="160" Margin="0,0,0,12">
                            <ListBox.ItemTemplate>
                                <DataTemplate DataType="{x:Type models:InventoryItemSummary}">
                                    <StackPanel Orientation="Horizontal">
                                        <TextBlock Text="{Binding Name}" Foreground="{DynamicResource TextBrush}" />
                                        <TextBlock Text=" (" FontSize="10" Foreground="{DynamicResource MutedBrush}" />
                                        <TextBlock Text="{Binding ItemType}" FontSize="10" Foreground="{DynamicResource MutedBrush}" />
                                        <TextBlock Text=")" FontSize="10" Foreground="{DynamicResource MutedBrush}" />
                                    </StackPanel>
                                </DataTemplate>
                            </ListBox.ItemTemplate>
                        </ListBox>

                        <!-- Reward Config -->
                        <TextBlock Text="Reward Title" FontWeight="SemiBold" Foreground="{DynamicResource TextBrush}" Margin="0,0,0,4" />
                        <TextBox Text="{Binding SelectedRule.RewardTitle, UpdateSourceTrigger=PropertyChanged}" Margin="0,0,0,8" />

                        <TextBlock Text="Reward Cost (points)" FontWeight="SemiBold" Foreground="{DynamicResource TextBrush}" Margin="0,0,0,4" />
                        <TextBox Text="{Binding SelectedRule.RewardCost, UpdateSourceTrigger=PropertyChanged}" Margin="0,0,0,8" />

                        <TextBlock Text="Sync Mode" FontWeight="SemiBold" Foreground="{DynamicResource TextBrush}" Margin="0,0,0,4" />
                        <ComboBox SelectedItem="{Binding SelectedRule.SyncMode}" Margin="0,0,0,8">
                            <ComboBox.Items>
                                <ComboBoxItem Content="Create &amp; Manage" />
                                <ComboBoxItem Content="Link Existing" />
                            </ComboBox.Items>
                        </ComboBox>

                        <TextBlock Text="Cooldown (seconds)" FontWeight="SemiBold" Foreground="{DynamicResource TextBrush}" Margin="0,0,0,4" />
                        <TextBox Text="{Binding SelectedRule.CooldownSeconds, UpdateSourceTrigger=PropertyChanged}" Margin="0,0,0,12" />

                        <CheckBox IsChecked="{Binding SelectedRule.IsEnabled}" Content="Enabled" 
                                  Foreground="{DynamicResource TextBrush}" Margin="0,0,0,16" />

                        <!-- Action Buttons -->
                        <StackPanel Orientation="Horizontal" HorizontalAlignment="Right">
                            <Button Content="Save" Command="{Binding SaveRuleCommand}" 
                                    Style="{DynamicResource AccentButtonStyle}" Margin="0,0,8,0" Width="80" />
                            <Button Content="Cancel" Command="{Binding CancelEditCommand}" 
                                    Style="{DynamicResource NavCardActionButtonStyle}" Width="80" />
                        </StackPanel>
                    </StackPanel>
                </ScrollViewer>
            </Border>
        </Grid>
    </Grid>
</Window>
```

- [ ] **Step 2: Create the code-behind**

```csharp
using System;
using System.ComponentModel;
using System.Windows;
using System.Windows.Input;
using VrcTwitchOscBridge.Services;
using VrcTwitchOscBridge.ViewModels;

namespace VrcTwitchOscBridge;

public sealed partial class InventoryItemSpawnManagerWindow : Window
{
    private InventoryItemSpawnManagerViewModel? Vm => DataContext as InventoryItemSpawnManagerViewModel;

    public InventoryItemSpawnManagerWindow()
    {
        InitializeComponent();
        ThemeManager.ApplyToResources(Resources, ThemeManager.CurrentTheme);
        ThemeManager.ThemeChanged += OnThemeManagerThemeChanged;
        Closed += OnWindowClosed;
    }

    private void OnTitleBarMouseDown(object sender, MouseButtonEventArgs e)
    {
        if (e.ChangedButton == MouseButton.Left)
            DragMove();
    }

    private void OnMinimizeClicked(object sender, RoutedEventArgs e)
    {
        WindowState = WindowState.Minimized;
    }

    private void OnCloseClicked(object sender, RoutedEventArgs e)
    {
        Close();
    }

    private void OnThemeManagerThemeChanged(object? sender, EventArgs e)
    {
        if (Dispatcher.CheckAccess())
            ThemeManager.ApplyToResources(Resources, ThemeManager.CurrentTheme);
        else
            Dispatcher.Invoke(() => ThemeManager.ApplyToResources(Resources, ThemeManager.CurrentTheme));
    }

    private void OnWindowClosed(object? sender, EventArgs e)
    {
        ThemeManager.ThemeChanged -= OnThemeManagerThemeChanged;
        if (Vm is IDisposable disposable)
            disposable.Dispose();
    }
}
```

---

### Task 9: Add Home UI Card to MainWindow.xaml

**Files:**
- Modify: `E:\!!!Program to work on\Proper Crystal Relay\VrcTwitchOscBridge\MainWindow.xaml`

**Interfaces:**
- Consumes: `OpenInventoryItemSpawnManagerCommand` from `MainWindowViewModel`
- Produces: New card in the Redeem Library section

- [ ] **Step 1: Expand the Redeem Library top row to 3 columns**

Change the initial 2x2 card grid to have 3 columns in the top row. Find the existing grid structure around line 2978 and add a new column definition:

```xml
<!-- Change from 2 columns to 3 in the top row -->
<Grid.ColumnDefinitions>
    <ColumnDefinition Width="*" />
    <ColumnDefinition Width="*" />
    <ColumnDefinition Width="*" />
</Grid.ColumnDefinitions>
```

Move Avatar Sets to `Grid.Row="0" Grid.Column="0"` and add the new card at `Grid.Column="1"`. Shift Avatar Actions to `Grid.Column="2"`.

- [ ] **Step 2: Add the new card (insert between Avatar Sets and Avatar Actions)**

```xml
<!-- Card 2: Inventory Item Spawns -->
<Border Grid.Row="0" Grid.Column="1" Style="{StaticResource NavCardStyle}">
    <StackPanel>
        <TextBlock Text="Inventory Item Spawns"
                   FontFamily="{DynamicResource HeadingFontFamily}"
                   FontSize="10" FontWeight="SemiBold"
                   Foreground="{DynamicResource MutedBrush}" />
        <TextBlock Text="Spawn props in-world"
                   FontWeight="Bold" FontSize="15"
                   Foreground="{DynamicResource TextBrush}" />
        <TextBlock Text="Let viewers spawn inventory items"
                   Foreground="{DynamicResource MutedBrush}"
                   FontSize="11" TextWrapping="Wrap" />
        <Button Content="Manage →"
                Style="{StaticResource NavCardActionButtonStyle}"
                Command="{Binding OpenInventoryItemSpawnManagerCommand}"
                HorizontalAlignment="Left" />
    </StackPanel>
</Border>
```

- [ ] **Step 3: Shift Avatar Actions to Column 2**

Change the existing Avatar Actions card from `Grid.Column="1"` to `Grid.Column="2"`.

---

### Task 10: Add Command, Field, and Open Method to MainWindowViewModel

**Files:**
- Modify: `E:\!!!Program to work on\Proper Crystal Relay\VrcTwitchOscBridge\ViewModels\MainWindowViewModel.cs`

**Interfaces:**
- Consumes: `InventoryItemSpawnManagerViewModel`, `InventoryItemSpawnManagerWindow`
- Produces: `OpenInventoryItemSpawnManagerCommand`, window management, reward sync trigger

- [ ] **Step 1: Add nullable window field near line 5141** (with the other manager window fields)

```csharp
private InventoryItemSpawnManagerWindow? _inventoryItemSpawnManagerWindow;
```

- [ ] **Step 2: Add command declaration near line 2938** (with other RelayCommand properties)

```csharp
public RelayCommand OpenInventoryItemSpawnManagerCommand { get; }
```

- [ ] **Step 3: Add command assignment near line 911** (with other command assignments)

```csharp
OpenInventoryItemSpawnManagerCommand = new RelayCommand(OpenInventoryItemSpawnManager);
```

- [ ] **Step 4: Add open method after the existing manager open methods (~line 5288)**

```csharp
private void OpenInventoryItemSpawnManager()
{
    if (_inventoryItemSpawnManagerWindow is { IsVisible: true })
    {
        _inventoryItemSpawnManagerWindow.Activate();
        return;
    }

    var managerVm = new InventoryItemSpawnManagerViewModel(this);
    _inventoryItemSpawnManagerWindow = new InventoryItemSpawnManagerWindow
    {
        Owner = System.Windows.Application.Current?.MainWindow,
        DataContext = managerVm
    };
    _inventoryItemSpawnManagerWindow.Closed += (_, _) =>
    {
        _inventoryItemSpawnManagerWindow = null;
    };
    _inventoryItemSpawnManagerWindow.Show();
}
```

---

### Task 11: Add Reward Sync for Inventory Spawn Rules

**Files:**
- Modify: `E:\!!!Program to work on\Proper Crystal Relay\VrcTwitchOscBridge\ViewModels\MainWindowViewModel.cs`

**Interfaces:**
- Consumes: `InventoryItemSpawnRule`, `ManagedRewardSyncTarget`
- Produces: Managed reward sync targets for inventory spawn rules in the sync pipeline

- [ ] **Step 1: Add inventory spawn rules to `EnumerateManagedRewardOwnershipEntries`**

Add a parameter `IReadOnlyCollection<InventoryItemSpawnRule> inventorySpawnRules` to the method signature. Inside the method, add after the avatar roulette profiles loop:

```csharp
foreach (var rule in Settings.InventoryItemSpawnRules)
{
    if (!string.IsNullOrWhiteSpace(rule.RewardId) || !string.IsNullOrWhiteSpace(rule.RewardTitle))
    {
        yield return new ManagedRewardOwnershipEntry(
            Guid.Parse(rule.Id),
            rule.RewardId,
            rule.RewardTitle,
            rule.SyncMode);
    }
}
```

Also update the call site at line 12756 to pass `Settings.InventoryItemSpawnRules`.

- [ ] **Step 2: Add sync target creation in `SynchronizeManagedChannelPointRewardsAsync`**

After the roulette targets block (around line 12906), add:

```csharp
var inventorySpawnTargets = new List<ManagedRewardSyncTarget>();
foreach (var rule in Settings.InventoryItemSpawnRules)
{
    inventorySpawnTargets.Add(CreateManagedRewardTargetForInventorySpawnRule(
        rule,
        allowManagedRewardActivation,
        temporarilyDisabledRuleIds,
        cooldownRuleIds));
}
```

- [ ] **Step 3: Add the sync target creation method**

```csharp
private ManagedRewardSyncTarget CreateManagedRewardTargetForInventorySpawnRule(
    InventoryItemSpawnRule rule,
    bool allowManagedRewardActivation,
    IReadOnlyCollection<Guid> temporarilyDisabledRuleIds,
    IReadOnlyCollection<Guid> cooldownRuleIds)
{
    var ruleId = Guid.Parse(rule.Id);
    var isOnCooldown = cooldownRuleIds.Contains(ruleId);
    var desiredEnabled = allowManagedRewardActivation
        && rule.IsEnabled
        && !temporarilyDisabledRuleIds.Contains(ruleId);

    return new ManagedRewardSyncTarget(
        ruleId,
        rule.DisplayTitle,
        rule.RewardId ?? string.Empty,
        rule.RewardTitle,
        rule.RewardCost,
        rule.SyncMode,
        rule.CooldownSeconds,
        ManagedRewardPresentation.ReadyBackgroundColor,
        prompt: string.Empty,
        requireUserInput: false,
        desiredEnabled: desiredEnabled,
        isCooldownActive: isOnCooldown,
        deleteWhenInactive: false,
        protectFromCapReclaim: desiredEnabled || isOnCooldown,
        applyRewardId: rewardId => rule.RewardId = rewardId);
}
```

- [ ] **Step 4: Merge targets into the main sync flow**

Near the end of `SynchronizeManagedChannelPointRewardsAsync` where targets are combined (around line 13000+), add `inventorySpawnTargets` to the combined target list that gets passed to the sync reconciliation method.

Search for where `powerUpTargets`, `cashPaymentTargets`, etc. are collected, and add `inventorySpawnTargets` into the same combined enumerable.

---

### Task 12: Add Runtime Dispatch in BridgeCoordinator

**Files:**
- Modify: `E:\!!!Program to work on\Proper Crystal Relay\VrcTwitchOscBridge\Services\BridgeCoordinator.cs`

**Interfaces:**
- Consumes: `VrChatApiClient`, `BridgeRuntimeConfiguration.InventoryItemSpawnRules`, cooldown tracking
- Produces: Dispatch for inventory item spawn when a matching channel point redemption arrives

- [ ] **Step 1: Add dispatch method**

Add to `BridgeCoordinator`:

```csharp
private async Task<bool> TryExecuteInventoryItemSpawnFromRedemptionAsync(
    BridgeRuntimeConfiguration configuration,
    string rewardId,
    string rewardTitle,
    string userId,
    CancellationToken cancellationToken)
{
    var matchedRule = configuration.InventoryItemSpawnRules
        .FirstOrDefault(rule =>
            rule.IsEnabled &&
            (string.Equals(rule.RewardId?.Trim(), rewardId, StringComparison.Ordinal) ||
             string.Equals(rule.RewardTitle?.Trim(), rewardTitle, StringComparison.Ordinal)));

    if (matchedRule is null)
        return false;

    if (string.IsNullOrWhiteSpace(matchedRule.InventoryItemId))
        return false;

    if (IsRuleOnCooldown(matchedRule.Id, matchedRule.CooldownSeconds))
    {
        WriteLog($"Inventory item spawn '{matchedRule.ItemName}' is on cooldown.");
        return true; // Claimed but didn't execute
    }

    try
    {
        var authCookie = GetCurrentVrChatAuthCookie();
        if (string.IsNullOrWhiteSpace(authCookie))
        {
            WriteLog("Cannot spawn inventory item: VRChat auth cookie not available.");
            return false;
        }

        await _vrChatApiClient.SpawnInventoryItemAsync(
            authCookie, matchedRule.InventoryItemId, cancellationToken);

        ApplyCooldown(matchedRule.Id, matchedRule.CooldownSeconds);
        WriteLog($"Spawned inventory item '{matchedRule.ItemName}' for user '{userId}'.");
        return true;
    }
    catch (Exception ex)
    {
        WriteLog($"Failed to spawn inventory item '{matchedRule.ItemName}': {SensitiveTextSanitizer.Sanitize(ex.Message)}");
        return false;
    }
}
```

- [ ] **Step 2: Add cooldown helpers**

If the existing cooldown infrastructure uses a `ConcurrentDictionary<string, DateTime>` pattern, add:

```csharp
private readonly ConcurrentDictionary<string, DateTime> _inventorySpawnCooldowns = new();

private bool IsRuleOnCooldown(string ruleId, int cooldownSeconds)
{
    if (cooldownSeconds <= 0) return false;
    if (_inventorySpawnCooldowns.TryGetValue(ruleId, out var until))
    {
        if (DateTime.UtcNow < until) return true;
        _inventorySpawnCooldowns.TryRemove(ruleId, out _);
    }
    return false;
}

private void ApplyCooldown(string ruleId, int cooldownSeconds)
{
    if (cooldownSeconds > 0)
        _inventorySpawnCooldowns[ruleId] = DateTime.UtcNow.AddSeconds(cooldownSeconds);
}
```

(If the existing `BridgeCoordinator` already has a general cooldown tracker, reuse it instead. Update these helper signatures to match the existing pattern.)

- [ ] **Step 3: Wire into `HandleNotificationAsync`**

In `HandleNotificationAsync`, after the Wardrobe check and before the Avatar Set rules check (around line 3456), add:

```csharp
// Try Inventory Item Spawn (props)
if (!bridgeEvent.IsChatCommandTrigger
    && bridgeEvent.TriggerType == TwitchTriggerType.ChannelPoints
    && bridgeEvent.RewardId is not null
    && await TryExecuteInventoryItemSpawnFromRedemptionAsync(
        configuration,
        bridgeEvent.RewardId,
        bridgeEvent.RewardTitle ?? string.Empty,
        bridgeEvent.UserId,
        cancellationToken))
{
    return;
}
```

- [ ] **Step 4: Add `using System.Collections.Concurrent;` at the top** if not already present.

- [ ] **Step 5: Ensure `_vrChatApiClient` is accessible** — if `BridgeCoordinator` doesn't already have a field for `VrChatApiClient`, add one:

```csharp
private readonly VrChatApiClient _vrChatApiClient;
```

And initialize it in the constructor.

Also add a `GetCurrentVrChatAuthCookie()` method that reads the auth cookie from the current session state (check the existing pattern — `Settings.VrChat.AuthCookie` or similar).

---

### Task 13: Add csproj Includes

**Files:**
- Modify: `E:\!!!Program to work on\Proper Crystal Relay\VrcTwitchOscBridge\VrcTwitchOscBridge.csproj`

**Interfaces:**
- Consumes: All new files
- Produces: Buildable project

- [ ] **Step 1: Add Page include** after the AvatarSetsManagerWindow line (~line 49)

```xml
<Page Include="InventoryItemSpawnManagerWindow.xaml" />
```

- [ ] **Step 2: Add Compile include** after the AvatarSetsManagerWindow code-behind line (~line 108)

```xml
<Compile Include="InventoryItemSpawnManagerWindow.xaml.cs">
  <DependentUpon>InventoryItemSpawnManagerWindow.xaml</DependentUpon>
</Compile>
```

- [ ] **Step 3: Add ViewModel Compile includes** after the AvatarSetsManagerViewModel line (~line 132)

```xml
<Compile Include="ViewModels\InventoryItemSpawnManagerViewModel.cs" />
<Compile Include="ViewModels\InventoryItemSpawnCardViewModel.cs" />
```

- [ ] **Step 4: Add Service Compile includes** after the existing Services list

```xml
<Compile Include="Services\InventoryItemImageService.cs" />
```

---

### Task 14: Write XAML Tests

**Files:**
- Create: `E:\!!!Program to work on\Proper Crystal Relay\VrcTwitchOscBridge.Tests\InventoryItemSpawnManagerWindowXamlTests.cs`

**Interfaces:**
- Consumes: XAML file content
- Produces: Test verification of XAML bindings

- [ ] **Step 1: Create test file**

```csharp
using System;
using System.IO;
using System.Linq;
using Xunit;

namespace VrcTwitchOscBridge.Tests;

public sealed class InventoryItemSpawnManagerWindowXamlTests
{
    private static string FindSourceFile(string projectName, string fileName)
    {
        var dir = AppContext.BaseDirectory;
        while (dir != null)
        {
            var candidate = Path.Combine(dir, projectName, fileName);
            if (File.Exists(candidate))
                return candidate;
            var parent = Directory.GetParent(dir);
            dir = parent?.FullName;
        }
        throw new FileNotFoundException($"Could not find {fileName} in any parent of {AppContext.BaseDirectory}");
    }

    [Fact]
    public void Window_HasCustomChrome()
    {
        var xaml = File.ReadAllText(FindSourceFile("VrcTwitchOscBridge", "InventoryItemSpawnManagerWindow.xaml"));
        Assert.Contains("WindowStyle=\"None\"", xaml);
        Assert.Contains("shell:WindowChrome", xaml);
    }

    [Fact]
    public void Window_HasInventoryItemSpawnManagerViewModelDataContext()
    {
        var xaml = File.ReadAllText(FindSourceFile("VrcTwitchOscBridge", "InventoryItemSpawnManagerWindow.xaml"));
        Assert.Contains("d:DataContext=\"{d:DesignInstance Type=vm:InventoryItemSpawnManagerViewModel", xaml);
    }

    [Fact]
    public void CardGrid_BindsToCardsView()
    {
        var xaml = File.ReadAllText(FindSourceFile("VrcTwitchOscBridge", "InventoryItemSpawnManagerWindow.xaml"));
        Assert.Contains("ItemsSource=\"{Binding CardsView}\"", xaml);
    }

    [Fact]
    public void EditorPanel_BindsToSelectedRule()
    {
        var xaml = File.ReadAllText(FindSourceFile("VrcTwitchOscBridge", "InventoryItemSpawnManagerWindow.xaml"));
        Assert.Contains("{Binding SelectedRule.", xaml);
    }

    [Fact]
    public void Toolbar_HasSearchRefreshAndAddNew()
    {
        var xaml = File.ReadAllText(FindSourceFile("VrcTwitchOscBridge", "InventoryItemSpawnManagerWindow.xaml"));
        Assert.Contains("{Binding SearchText,", xaml);
        Assert.Contains("{Binding RefreshInventoryCommand", xaml);
        Assert.Contains("{Binding AddNewRuleCommand", xaml);
    }

    [Fact]
    public void ItemPicker_BindsToFilteredInventoryItems()
    {
        var xaml = File.ReadAllText(FindSourceFile("VrcTwitchOscBridge", "InventoryItemSpawnManagerWindow.xaml"));
        Assert.Contains("{Binding FilteredInventoryItems", xaml);
        Assert.Contains("{Binding SelectedInventoryItem", xaml);
    }

    [Fact]
    public void SyncModeComboBox_HasCreateAndLinkOptions()
    {
        var xaml = File.ReadAllText(FindSourceFile("VrcTwitchOscBridge", "InventoryItemSpawnManagerWindow.xaml"));
        Assert.Contains("Create &amp; Manage", xaml);
        Assert.Contains("Link Existing", xaml);
    }
}
```

---

### Task 15: Build and Verify

**Files:** None (build verification)

- [ ] **Step 1: Build the project**

Run: `dotnet build "E:\!!!Program to work on\Proper Crystal Relay\VrcTwitchOscBridge\VrcTwitchOscBridge.csproj"`

Expected: Build succeeds with no errors or warnings.

- [ ] **Step 2: Run the tests**

Run: `dotnet test "E:\!!!Program to work on\Proper Crystal Relay\VrcTwitchOscBridge.Tests\VrcTwitchOscBridge.Tests.csproj" --no-restore --filter "InventoryItemSpawnManagerWindowXamlTests"`

Expected: All tests pass.

- [ ] **Step 3: Fix any build/test failures** — address any missing `using` directives, type mismatches, or binding errors found during build.

---

## Task Dependency Graph

```
Task 1 (Models)
  └─> Task 2 (AppSettings)
       └─> Task 5 (Runtime Config)
       └─> Task 6 (Card VM)
  └─> Task 3 (API Methods)
       └─> Task 4 (Image Service)
            └─> Task 6 (Card VM) — uses ImageService
                 └─> Task 7 (Manager VM) — uses Card VM
                      └─> Task 8 (Window XAML) — uses Manager VM
  └─> Task 10 (MainVM command) ——> Task 9 (Home UI)
  └─> Task 11 (Reward Sync)
  └─> Task 12 (Bridge Dispatch)
All └─> Task 13 (csproj)
     └─> Task 14 (Tests)
     └─> Task 15 (Build)
```

Tasks 4, 9, 12, 13, 14, 15 have no dependency on each other and can be parallelized after their upstream dependencies are met.
