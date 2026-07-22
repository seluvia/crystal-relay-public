# Inventory Item Spawn Reward System

## Overview

A new reward system that lets streamers set up Twitch channel point rewards to
spawn VRChat inventory props in-world via the VRChat Inventory API. Follows the
same patterns as the existing Avatar Sets system: manager window, card grid,
slide-in editor, Twitch reward sync (CreateOrManage / LinkExisting).

## Models

### InventoryItemSpawnRule (new file: `Models/InventoryItemSpawnRule.cs`)

```csharp
public sealed class InventoryItemSpawnRule
{
    // Identity
    public string Id { get; set; } = Guid.NewGuid().ToString();

    // Inventory item reference (from VRChat API)
    public string InventoryItemId { get; set; } = string.Empty;
    public string ItemName { get; set; } = string.Empty;
    public string ItemImageUrl { get; set; } = string.Empty;
    public string ItemType { get; set; } = string.Empty; // "prop" etc.

    // Twitch reward config (mirrors AvatarSet pattern)
    public TwitchRewardSyncMode SyncMode { get; set; } = TwitchRewardSyncMode.CreateOrManage;
    public string? RewardId { get; set; }
    public string RewardTitle { get; set; } = string.Empty;
    public int RewardCost { get; set; } = 100;
    public bool IsEnabled { get; set; } = true;

    // Cooldown
    public int CooldownSeconds { get; set; } = 0;

    // Twitch-managed fields
    public string? RewardVersionFingerprint { get; set; }
}
```

Uses same `TwitchRewardSyncMode` enum as Avatar Sets.

### InventoryItemSummary (new file: `Models/InventoryItemSummary.cs`)

```csharp
public sealed record InventoryItemSummary(
    string Id,
    string Name,
    string ImageUrl,
    string ItemType,
    IReadOnlyList<string> Tags,
    IReadOnlyList<string> Flags
);
```

Lightweight display model for the inventory picker UI.

### AppSettings changes

Add to `AppSettings`:

```csharp
public ObservableCollection<InventoryItemSpawnRule> InventoryItemSpawnRules { get; set; } = new();
```

## VRChat API Layer

### VrChatApiRoutes additions

```csharp
public const string Inventory = "inventory";
public static string SpawnInventoryItem(string itemId) =>
    $"inventory/spawn?id={Uri.EscapeDataString(itemId)}";
```

### VrChatApiClient additions

```csharp
public async Task<List<InventoryItemSummary>> GetInventoryPropsAsync(
    string authCookie, CancellationToken ct = default)

public async Task SpawnInventoryItemAsync(
    string authCookie, string inventoryItemId, CancellationToken ct = default)
```

`GetInventoryPropsAsync`: Calls `GET /inventory` with `types=prop&flags=instantiatable&n=100`, returns items that have the `instantiatable` flag. Paginates if needed (same offset pattern as avatars).

`SpawnInventoryItemAsync`: Calls `GET /inventory/spawn?id={itemId}`. Response is `{ "token": "...", "version": 0 }` — no content to return, success means spawned.

### Internal JSON records

New deserialization records inside `VrChatApiClient`:

```csharp
private sealed record InventoryRecord(
    string Id,
    string Name,
    string Description,
    string ImageUrl,
    string ItemType,
    IReadOnlyList<string> Tags,
    IReadOnlyList<string> Flags,
    // other fields ignored
);

private sealed record InventoryListResponse(
    List<InventoryRecord> Data,
    int TotalCount
);

private sealed record InventorySpawnResponse(
    string Token,
    int Version
);
```

## Manager Window

### Window: `InventoryItemSpawnManagerWindow.xaml` + `.xaml.cs`

New file pair, non-modal themed window. Same custom chrome as other managers.

Layout:
- **Title bar**: "Inventory Item Spawns"
- **Toolbar**: Refresh inventory (re-fetches from VRChat API), search/filter by item name
- **Card grid**: WrapPanel of `InventoryItemSpawnCardViewModel` cards (~240x300)
- **Editor panel**: Slide-in on edit/add with two sections:
  - **Item picker**: Searchable list of inventory items loaded from VRChat, showing thumbnail + name + type badge. Selecting one fills the rule's item reference.
  - **Reward config**: Title override, cost, cooldown, Twitch sync mode dropdown, enable/disable toggle.
- **Empty state**: "No item spawn rules yet" with a "New Spawn" button.

### ViewModel: `InventoryItemSpawnManagerViewModel.cs`

Mirrors `AvatarSetsManagerViewModel` pattern:

```csharp
public sealed class InventoryItemSpawnManagerViewModel : ObservableObject, IDisposable
```

Public:
- `ObservableCollection<InventoryItemSpawnCardViewModel> Cards`
- `InventoryItemSpawnRule? SelectedRule` — the rule being edited
- `InventoryItemSummary? SelectedInventoryItem` — picked item in editor
- `ObservableCollection<InventoryItemSummary> AvailableInventoryItems` — from VRChat API
- `ICollectionView FilteredInventoryItems` — searchable/filtered
- Search text, filter/sort for cards
- Commands: `AddNew`, `Edit`, `Delete`, `RefreshInventory`, `Save`, `Cancel`
- Constructor takes `MainWindowViewModel` (for accessing VrChatApiClient, settings, Twitch reward sync)

### Card: `InventoryItemSpawnCardViewModel.cs`

Wraps `InventoryItemSpawnRule`. Properties:
- `DisplayTitle` — item name or custom title
- `ThumbnailUrl` — `ItemImageUrl` from the rule
- `IsEnabled` — status pill
- `IsLive` — matches current active Twitch reward state
- `RewardCost` display
- `CooldownText` — formatted cooldown
- `SyncModeBadge` — "Created" / "Linked" / "Not Set"

## Home UI Placement

The Redeem Library 2x2 card grid in `MainWindow.xaml` becomes a 3-column top row:

| Column | Content |
|--------|---------|
| Col 0  | **Avatar Sets** (existing, moves here if needed) |
| Col 1  | **Inventory Item Spawns** (new) |
| Col 2  | **Avatar Actions** (existing, Avatar Swap + Avatar Scaling) |

Row 2 stays the same: Trigger Systems (Universal Triggers + Movement) | Viewer Support (Cash Payments + Reward Fire Sale).

New card description text: "Spawn props in-world" with subtitle "Let viewers spawn inventory items".

## Twitch Reward Sync

Uses the exact same managed-reward sync infrastructure:

- `QueueManagedRewardSync()` called when rules are added/removed/changed
- Reward fingerprinting (compare `RewardTitle`, `RewardCost`, `IsEnabled` state against Twitch)
- `CreateOrManage` creates/updates rewards, `LinkExisting` listens only
- Opt-in `Delete Twitch reward when inactive` (reuses the existing setting)
- Cooldown colors synced to Twitch reward (reuses existing cooldown color logic)

Reward title defaults to `VRC: {ItemName}` when creating (matches `VRC:` prefix convention).

## Runtime Execution

### BridgeCoordinator changes

A new dispatch step in `HandleNotificationAsync` between the Wardrobe check and Avatar Set rules:

1. Match incoming channel point redemption against `InventoryItemSpawnRule` by reward ID or title
2. Check rule is enabled and not on cooldown
3. Call `VrChatApiClient.SpawnInventoryItemAsync(authCookie, rule.InventoryItemId)`
4. On success: apply cooldown, log activity
5. On failure: log error (item might be consumed or no longer owned)

This is a simple pipeline addition — no OSC involvement, no queue management needed since spawn is fire-and-forget.

The `RuntimeRuleIndex` can optionally include an item-spawn lookup for performance, but with a relatively small number of rules a simple `List<InventoryItemSpawnRule>` scan is fine.

### Cooldown tracking

Reuses the existing cooldown infrastructure (`CooldownTracker` / `lastExecutionTimes` pattern in `BridgeCoordinator`).

## Thumbnail Images

Item `imageUrl` values from the VRChat API require auth cookie authentication. A new lightweight `InventoryItemImageService` (or extended `AvatarImageService`) handles downloading and caching item thumbnails using the same cookie-based auth pattern.

Since this is a smaller need (a few dozen item images at most), in-memory caching with a `ConcurrentDictionary<string, BitmapImage>` is sufficient — no disk cache needed unless the user has many props.

## Items Excluded

- Bits, subs, gift subs, follows, chat commands as triggers (future)
- Stickers, emojis, equippable items (future)
- Avatar-bound rules (items are global, not per-avatar)
- Movement, OSC, Set Trigger actions (not applicable)
- Universal Triggers integration (future)
- Wardrobe integration (not applicable)
- Fire Sale pricing (future)

## Out of Scope (v1)

- Multiple props per reward (one reward = one item)
- Consumable item tracking
- Item quantity display
- Bulk import from VRChat inventory
- Drag-reorder in card grid
