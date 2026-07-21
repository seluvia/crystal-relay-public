# Avatar Picker Redesign Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Redesign the Pick Avatar window with a VRChat-inspired sidebar layout, source-based browsing, collapsible filters (style/platform/content), larger cards, and favorite-group navigation.

**Architecture:** Expand the VRChat API client to fetch favorite groups and manage heart toggles; expand the avatar model to carry source flags, tags, platform, and author; rewrite AvatarPickerViewModel for sidebar navigation and combined filtering; replace the AvatarPickerWindow XAML and code-behind with a sidebar + filter bar + 170px card layout; redirect AvatarRouletPickerWindow to multi-select mode in the new picker.

**Tech Stack:** C#, WPF/XAML, .NET 10

## Global Constraints

- All VRChat API data structures, endpoints, and behavior must match `https://vrchat.community` (already confirmed during spec).
- Existing callers (`AvatarPickerService.OpenSingle()`, `OpenMulti()`) must keep their signatures unchanged.
- `AvatarRouletPickerWindow` is replaced by the new picker in multi-select mode — the old window file is deleted.
- All new UI elements must use the existing DynamicResource theme system, not hardcoded colors.

---

## File Map

| File | Responsibility | Change |
|------|---------------|--------|
| `Services/VrChatApiRoutes.cs` | Route constants for favorite endpoints | Add 3 new constants |
| `Services/VrChatApiClient.cs` | API client with JSON deserialization | Expand avatar record; add favorite group + toggle endpoints |
| `Models/VrChatAvatarSummary.cs` | Avatar data record consumed by picker | Add source flags, author, tags, platform |
| `Models/VrChatFavoriteGroup.cs` | Favorite group model (new file) | Create model for group display name + ID |
| `Models/AvatarLibrary.cs` | Persistent local org data | Add `RecentAvatarIds` list |
| `Services/AvatarPickerService.cs` | Static service that opens picker window | Pass source flags + favorite groups to ViewModel |
| `ViewModels/AvatarPickerViewModel.cs` | Picker logic: sidebar, filters, selection | Full rewrite |
| `AvatarPickerWindow.xaml` | Picker window layout | Full rewrite |
| `AvatarPickerWindow.xaml.cs` | Picker code-behind | Full rewrite |
| `AvatarRouletPickerWindow.xaml` | Old roulette window (delete) | Removed |
| `AvatarRouletPickerWindow.xaml.cs` | Old roulette code-behind (delete) | Removed |
| `ViewModels/MainWindowViewModel.cs` | Central orchestrator | Minor update for roulette redirect |

---

### Task 1: Add API Routes for Favorites + Expand Avatar Record

**Files:**
- Modify: `Services/VrChatApiRoutes.cs`
- Modify: `Services/VrChatApiClient.cs`

**Interfaces:**
- Consumes: Existing `VrChatApiRoutes` and `VrChatApiClient` structure
- Produces: `VrChatApiRoutes.FavoriteGroups`, `VrChatApiRoutes.AddFavorite`, `VrChatApiRoutes.RemoveFavorite`; expanded `VrChatAvatarRecord` with `AuthorName`, `Tags`, `UnityPackages`

- [ ] **Step 1: Add route constants to VrChatApiRoutes.cs**

After the existing routes, add:

```csharp
public const string FavoriteGroups = "favorite/groups";
public const string AddFavorite = "favorites";
public static string RemoveFavorite(string favoriteId) =>
    $"favorites/{Uri.EscapeDataString(favoriteId)}";
```

- [ ] **Step 2: Add UnityPackageRecord to VrChatApiClient.cs**

Add this record inside the `VrChatApiClient` class (near `VrChatAvatarRecord`):

```csharp
private sealed class UnityPackageRecord
{
    public string? platform { get; set; }
}
```

- [ ] **Step 3: Expand VrChatAvatarRecord**

Add fields to the existing `VrChatAvatarRecord`:

```csharp
private sealed class VrChatAvatarRecord
{
    public string? id { get; set; }
    public string? name { get; set; }
    public string? imageUrl { get; set; }
    public string? thumbnailImageUrl { get; set; }
    public string? authorName { get; set; }
    public string? authorId { get; set; }
    public List<string>? tags { get; set; }
    public List<UnityPackageRecord>? unityPackages { get; set; }
}
```

- [ ] **Step 4: Add FavoriteGroupRecord**

Add next to `VrChatAvatarRecord`:

```csharp
private sealed class FavoriteGroupRecord
{
    public string? id { get; set; }
    public string? displayName { get; set; }
    public string? name { get; set; }
    public string? type { get; set; }
}

private sealed class FavoriteEntryRecord
{
    public string? id { get; set; }
    public string? favoriteId { get; set; }
    public List<string>? tags { get; set; }
    public string? type { get; set; }
}
```

- [ ] **Step 5: Add favorite-related async methods to VrChatApiClient**

```csharp
public async Task<IReadOnlyList<FavoriteGroupRecord>> GetFavoriteGroupsAsync(
    string authCookie, CancellationToken cancellationToken = default)
{
    using var request = new HttpRequestMessage(HttpMethod.Get,
        $"{BaseUrl}{VrChatApiRoutes.FavoriteGroups}?type=avatar");
    request.Headers.Add("Cookie", $"auth={authCookie}");

    using var response = await httpClient.SendAsync(request, cancellationToken);
    response.EnsureSuccessStatusCode();

    var json = await response.Content.ReadAsStringAsync(cancellationToken);
    return JsonSerializer.Deserialize<List<FavoriteGroupRecord>>(json) ?? [];
}

public async Task<List<FavoriteEntryRecord>> GetFavoriteEntriesAsync(
    string authCookie, CancellationToken cancellationToken = default)
{
    using var request = new HttpRequestMessage(HttpMethod.Get,
        $"{BaseUrl}{VrChatApiRoutes.ListFavorites}?type=avatar");
    request.Headers.Add("Cookie", $"auth={authCookie}");

    using var response = await httpClient.SendAsync(request, cancellationToken);
    response.EnsureSuccessStatusCode();

    var json = await response.Content.ReadAsStringAsync(cancellationToken);
    return JsonSerializer.Deserialize<List<FavoriteEntryRecord>>(json) ?? [];
}

public async Task<string?> AddFavoriteAsync(
    string authCookie, string avatarId, string groupTag,
    CancellationToken cancellationToken = default)
{
    var body = new { type = "avatar", favoriteId = avatarId, tags = new[] { groupTag } };
    var content = new StringContent(JsonSerializer.Serialize(body), Encoding.UTF8, "application/json");

    using var request = new HttpRequestMessage(HttpMethod.Post,
        $"{BaseUrl}{VrChatApiRoutes.AddFavorite}")
    {
        Content = content
    };
    request.Headers.Add("Cookie", $"auth={authCookie}");

    using var response = await httpClient.SendAsync(request, cancellationToken);
    if (!response.IsSuccessStatusCode) return null;

    var json = await response.Content.ReadAsStringAsync(cancellationToken);
    var result = JsonSerializer.Deserialize<FavoriteEntryRecord>(json);
    return result?.id; // returns fvrt_ ID
}

public async Task<bool> RemoveFavoriteAsync(
    string authCookie, string favoriteId,
    CancellationToken cancellationToken = default)
{
    using var request = new HttpRequestMessage(HttpMethod.Delete,
        $"{BaseUrl}{VrChatApiRoutes.RemoveFavorite(favoriteId)}");
    request.Headers.Add("Cookie", $"auth={authCookie}");

    using var response = await httpClient.SendAsync(request, cancellationToken);
    return response.IsSuccessStatusCode;
}
```

- [ ] **Step 6: Add ListFavorites route**

In `VrChatApiRoutes.cs`, add a constant for the base favorites list:

```csharp
public const string ListFavorites = "favorites";
```

- [ ] **Step 7: Build to verify**

Run: `dotnet build "E:\!!!Program to work on\Proper Crystal Relay\VrcTwitchOscBridge\VrcTwitchOscBridge.csproj" --no-restore`
Expected: Build succeeds with no errors.

- [ ] **Step 8: Commit**

```bash
git add Services/VrChatApiRoutes.cs Services/VrChatApiClient.cs
git commit -m "feat: add favorite group API routes, expand avatar record with tags/author/platform"
```

---

### Task 2: Expand VrChatAvatarSummary + Create Favorite Group Model

**Files:**
- Modify: `Models/VrChatAvatarSummary.cs`
- Create: `Models/VrChatFavoriteGroup.cs`

**Interfaces:**
- Consumes: Avatar data from API (authorName, tags, unityPackages)
- Produces: Expanded `VrChatAvatarSummary` with source/platform/tags; `VrChatFavoriteGroup` record

- [ ] **Step 1: Update VrChatAvatarSummary model**

Replace the existing record with:

```csharp
namespace VrcTwitchOscBridge.Models;

public sealed record VrChatAvatarSummary(
    string Id,
    string Name,
    string AuthorName,
    string? ThumbnailUrl,
    bool IsCurrentAvatar,
    bool IsUploaded,
    bool IsFavorited,
    bool IsLicensed,
    string Platform,           // "PC", "Quest", "Both", ""
    IReadOnlyList<string> StyleTags,    // "Furry", "Cute" (avatar_* prefix stripped)
    IReadOnlyList<string> ContentTags,  // "adult", "gore", etc. (content_* prefix stripped)
    string? FavoriteGroupName // "Avatars 1" when browsing within a favorite group
);
```

- [ ] **Step 2: Create VrChatFavoriteGroup model**

```csharp
namespace VrcTwitchOscBridge.Models;

public sealed record VrChatFavoriteGroup(
    string Id,
    string DisplayName,  // "Avatars 1"
    string Name,          // "avatars1"
    int Count             // number of avatars in this group
);
```

- [ ] **Step 3: Update all references to old VrChatAvatarSummary constructor**

Search for `new VrChatAvatarSummary(` across the codebase and update each call site to pass the new required parameters. The main call sites are:
- `VrChatApiClient.cs` (where records are created from API data)
- `MainWindowViewModel.cs` (where `VrChatAvatarSummary` is constructed)
- `SettingsStore.cs` (persistence)

For now, use default/empty values for new fields at call sites that aren't part of the picker:
```csharp
new VrChatAvatarSummary(
    record.id!, record.name!, record.authorName ?? "", record.thumbnailImageUrl,
    isCurrent, isUploaded, isFavorited, isLicensed,
    platform, styleTags, contentTags, favoriteGroupName)
```

- [ ] **Step 4: Build to verify**

Run: `dotnet build "E:\!!!Program to work on\Proper Crystal Relay\VrcTwitchOscBridge\VrcTwitchOscBridge.csproj" --no-restore`
Expected: Build succeeds. Fix any constructor mismatches by adding empty defaults.

- [ ] **Step 5: Commit**

```bash
git add Models/VrChatAvatarSummary.cs Models/VrChatFavoriteGroup.cs
git commit -m "feat: expand avatar summary model, add favorite group model"
```

---

### Task 3: Add Recent Avatar Tracking to AvatarLibrary

**Files:**
- Modify: `Models/AvatarLibrary.cs`

**Interfaces:**
- Consumes: `AvatarLibrary` existing structure
- Produces: `AvatarLibrary.RecentAvatarIds` list + `TrackRecentAvatar()` helper

- [ ] **Step 1: Add RecentAvatarIds to AvatarLibrary**

Add to the `AvatarLibrary` class body:

```csharp
private const int MaxRecentAvatars = 10;

public List<string> RecentAvatarIds { get; set; } = [];

public void TrackRecentAvatar(string avatarId)
{
    RecentAvatarIds.Remove(avatarId);
    RecentAvatarIds.Insert(0, avatarId);
    if (RecentAvatarIds.Count > MaxRecentAvatars)
        RecentAvatarIds.RemoveRange(MaxRecentAvatars, RecentAvatarIds.Count - MaxRecentAvatars);
}
```

- [ ] **Step 2: Build to verify**

Run: `dotnet build "E:\!!!Program to work on\Proper Crystal Relay\VrcTwitchOscBridge\VrcTwitchOscBridge.csproj" --no-restore`
Expected: Build succeeds.

- [ ] **Step 3: Commit**

```bash
git add Models/AvatarLibrary.cs
git commit -m "feat: add recent avatar tracking to avatar library"
```

---

### Task 4: Update AvatarPickerService to Pass New Data

**Files:**
- Modify: `Services/AvatarPickerService.cs`

**Interfaces:**
- Consumes: New `VrChatAvatarSummary` fields, `VrChatFavoriteGroup`
- Produces: Updated `OpenSingle`/`OpenMulti` that pass source flags + groups to ViewModel

- [ ] **Step 1: Update OpenSingle to accept and pass favorite groups**

```csharp
public static AvatarPickerResult? OpenSingle(
    AppTheme theme,
    IReadOnlyList<VrChatAvatarSummary> avatars,
    AvatarLibrary? avatarLibrary = null,
    string? currentAvatarId = null,
    IReadOnlyList<VrChatFavoriteGroup>? favoriteGroups = null,
    Dictionary<string, string>? avatarFavoriteGroups = null,
    Window? owner = null)
{
    var window = new AvatarPickerWindow(
        theme, avatars, Instance, avatarLibrary,
        favoriteGroups: favoriteGroups,
        avatarFavoriteGroups: avatarFavoriteGroups,
        currentAvatarId: currentAvatarId);

    if (owner is not null)
    {
        window.Owner = owner;
        window.WindowStartupLocation = WindowStartupLocation.CenterOwner;
    }
    else
    {
        window.WindowStartupLocation = WindowStartupLocation.CenterScreen;
    }

    var result = window.ShowDialog();
    if (result != true) return null;

    var selectedIds = window.GetSelectedAvatarIds();
    if (selectedIds.Count == 0) return null;

    var selectedId = selectedIds[0];
    var selectedAvatar = avatars.FirstOrDefault(a => string.Equals(a.Id, selectedId, StringComparison.Ordinal));
    return new AvatarPickerResult(selectedId, selectedAvatar?.Name ?? selectedId);
}
```

- [ ] **Step 2: Update OpenMulti similarly**

```csharp
public static IReadOnlyList<string> OpenMulti(
    AppTheme theme,
    IReadOnlyList<VrChatAvatarSummary> avatars,
    AvatarLibrary? avatarLibrary = null,
    IReadOnlyList<string>? currentPool = null,
    IReadOnlyList<VrChatFavoriteGroup>? favoriteGroups = null,
    Dictionary<string, string>? avatarFavoriteGroups = null,
    Window? owner = null)
{
    var window = new AvatarPickerWindow(
        theme, avatars, Instance, avatarLibrary,
        multiSelectCurrentIds: currentPool,
        favoriteGroups: favoriteGroups,
        avatarFavoriteGroups: avatarFavoriteGroups);

    // ... owner setup same as before ...

    var result = window.ShowDialog();
    return result == true ? window.GetSelectedAvatarIds() : (currentPool ?? []);
}
```

- [ ] **Step 3: Update AvatarPickerWindow constructor overloads**

These will be updated in Task 6/7. For now, ensure the build works by adding the optional parameters to the constructor in `AvatarPickerWindow.xaml.cs`.

- [ ] **Step 4: Build to verify**

Run: `dotnet build "E:\!!!Program to work on\Proper Crystal Relay\VrcTwitchOscBridge\VrcTwitchOscBridge.csproj" --no-restore`
Expected: Build succeeds.

- [ ] **Step 5: Commit**

```bash
git add Services/AvatarPickerService.cs
git commit -m "feat: update picker service to pass favorite groups and expanded data"
```

---

### Task 5: Rewrite AvatarPickerViewModel

**Files:**
- Modify: `ViewModels/AvatarPickerViewModel.cs`

**Interfaces:**
- Consumes: Expanded `VrChatAvatarSummary`, `VrChatFavoriteGroup`, `AvatarLibrary`
- Produces: Sidebar navigation state, filter state, filtered avatar list

- [ ] **Step 1: Define sidebar navigation model**

Add at the top (inside namespace, before class):

```csharp
public enum BrowseSection
{
    AllAvatars,
    Recent,
    Favorites,
    FavoritesGroup1,
    FavoritesGroup2,
    FavoritesGroup3,
    FavoritesGroup4,
    Uploaded,
    Purchased,
    LocalOsc,
    UserGroup,
    Ungrouped
}

public sealed record SidebarItem(
    string Label,
    BrowseSection Section,
    string Icon,
    int Count,
    bool IsExpandable = false,
    bool IsExpanded = false,
    IReadOnlyList<SidebarItem>? Children = null,
    string? ColorHex = null // for user groups
);
```

- [ ] **Step 2: Define the new ViewModel class skeleton**

```csharp
public sealed class AvatarPickerViewModel : ObservableObject
{
    private readonly AvatarImageService imageService;
    private readonly IReadOnlyList<VrChatAvatarSummary> allAvatars;
    private readonly AvatarLibrary? avatarLibrary;
    private readonly Dictionary<string, string>? avatarFavoriteGroups; // avatarId -> groupDisplayName
    private readonly IReadOnlyList<VrChatFavoriteGroup>? favoriteGroups;
    private string searchText = string.Empty;
    private string? selectedAvatarId;
    private string? selectedAvatarName;
    private bool isMultiSelectMode;
    private bool filtersExpanded;

    // Sidebar
    public ObservableCollection<SidebarItem> SidebarItems { get; }
    private SidebarItem? selectedSidebarItem;

    public SidebarItem? SelectedSidebarItem
    {
        get => selectedSidebarItem;
        set
        {
            if (SetProperty(ref selectedSidebarItem, value))
            {
                ApplyFilter();
                RaisePropertyChanged(nameof(SectionTitle));
                RaisePropertyChanged(nameof(SectionDescription));
            }
        }
    }

    // Filters
    private bool HasActiveFilters =>
        SelectedStyleTags.Count > 0 || SelectedPlatform != null || SelectedContentTags.Count > 0;

    public ObservableCollection<string> AllStyleTags { get; }
    public ObservableCollection<string> SelectedStyleTags { get; } = [];

    public string? SelectedPlatform { get; set; } // null=All, "PC", "Quest", "Both"

    public ObservableCollection<string> AllContentTags { get; }
    public ObservableCollection<string> SelectedContentTags { get; } = [];

    public bool FiltersExpanded
    {
        get => filtersExpanded;
        set => SetProperty(ref filtersExpanded, value);
    }

    // Avatar lists
    public ObservableCollection<AvatarPickerItem> AllAvatars { get; }
    public ObservableCollection<AvatarPickerItem> FilteredAvatars { get; } = [];

    // Search
    public string SearchText
    {
        get => searchText;
        set
        {
            if (SetProperty(ref searchText, value))
                ApplyFilter();
        }
    }

    // Selection
    public AvatarPickerItem? SelectedItem { get; set; }
    public List<string> SelectedMultiAvatarIds { get; } = [];
    public bool IsMultiSelectMode => isMultiSelectMode;
    public bool CanConfirm => isMultiSelectMode ? SelectedMultiAvatarIds.Count > 0 : selectedAvatarId != null;
    public string SelectedAvatarDisplayName => selectedAvatarName ?? "No avatar selected";
    public string SectionTitle => SelectedSidebarItem?.Label ?? "All Avatars";
    public string SectionDescription => GetSectionDescription();

    // View mode
    public AvatarPickerViewMode ViewMode { get; set; }
```

- [ ] **Step 3: Implement constructor**

```csharp
public AvatarPickerViewModel(
    IReadOnlyList<VrChatAvatarSummary> avatars,
    AvatarImageService imageService,
    AvatarLibrary? avatarLibrary = null,
    string? currentAvatarId = null,
    IReadOnlyList<string>? multiSelectCurrentIds = null,
    IReadOnlyList<VrChatFavoriteGroup>? favoriteGroups = null,
    Dictionary<string, string>? avatarFavoriteGroups = null)
{
    this.imageService = imageService;
    this.avatarLibrary = avatarLibrary;
    this.allAvatars = avatars;
    this.favoriteGroups = favoriteGroups;
    this.avatarFavoriteGroups = avatarFavoriteGroups;

    avatarLibrary?.PruneMissingEntries(avatars);

    if (multiSelectCurrentIds is { Count: > 0 })
    {
        isMultiSelectMode = true;
        SelectedMultiAvatarIds = new List<string>(multiSelectCurrentIds);
    }

    // Build avatar items
    AllAvatars = new ObservableCollection<AvatarPickerItem>(
        avatars.Select(a => CreatePickerItem(a)));

    // Current selection
    if (!string.IsNullOrWhiteSpace(currentAvatarId))
    {
        selectedAvatarId = currentAvatarId;
        var current = AllAvatars.FirstOrDefault(a => a.Id == currentAvatarId);
        if (current is not null)
        {
            selectedAvatarName = current.Name;
            SetItemSelected(current, true);
        }
    }

    // Collect all unique style/content tags
    AllStyleTags = new ObservableCollection<string>(
        avatars.SelectMany(a => a.StyleTags).Distinct().OrderBy(t => t));

    AllContentTags = new ObservableCollection<string>(
        avatars.SelectMany(a => a.ContentTags).Distinct().OrderBy(t => t));

    // Build sidebar
    SidebarItems = new ObservableCollection<SidebarItem>(BuildSidebarItems());

    // Default selection: All Avatars
    selectedSidebarItem = SidebarItems[0];
    viewMode = avatarLibrary?.LastViewMode ?? AvatarPickerViewMode.Grid;

    ApplyFilter();
}
```

- [ ] **Step 4: Implement BuildSidebarItems**

```csharp
private List<SidebarItem> BuildSidebarItems()
{
    var items = new List<SidebarItem>();

    // BROWSE section
    items.Add(new SidebarItem("All Avatars", BrowseSection.AllAvatars, "\U0001F4C1", allAvatars.Count));
    items.Add(new SidebarItem("Recent", BrowseSection.Recent, "\U0001F550", GetRecentCount()));

    // SOURCES
    if (favoriteGroups is { Count: > 0 })
    {
        var totalFav = allAvatars.Count(a => a.IsFavorited);
        var children = new List<SidebarItem>
        {
            new("All Favorites", BrowseSection.Favorites, "\u25CF", totalFav)
        };
        foreach (var g in favoriteGroups)
        {
            int cnt = avatarFavoriteGroups?.Count(kvp => kvp.Value == g.DisplayName) ?? 0;
            children.Add(new SidebarItem(g.DisplayName,
                BrowseSection.FavoritesGroup1 + favoriteGroups.IndexOf(g),
                "\u25CF", cnt, ColorHex: "#f472b6"));
        }
        items.Add(new SidebarItem("Favorites", BrowseSection.Favorites, "\u2764\uFE0F",
            totalFav, IsExpandable: true, IsExpanded: false, Children: children));
    }

    items.Add(new SidebarItem("Uploaded", BrowseSection.Uploaded, "\u2B06\uFE0F",
        allAvatars.Count(a => a.IsUploaded)));
    items.Add(new SidebarItem("Purchased", BrowseSection.Purchased, "\U0001F6CD\uFE0F",
        allAvatars.Count(a => a.IsLicensed)));
    items.Add(new SidebarItem("Local OSC", BrowseSection.LocalOsc, "\U0001F4BB",
        allAvatars.Count(a => string.IsNullOrEmpty(a.FavoriteGroupName) && !a.IsUploaded && !a.IsFavorited && !a.IsLicensed)));

    // MY GROUPS
    if (avatarLibrary?.Groups is { Count: > 0 })
    {
        foreach (var group in avatarLibrary.Groups.OrderBy(g => g.SortOrder).ThenBy(g => g.Name))
        {
            var count = allAvatars.Count(a =>
                avatarLibrary.GetEntry(a.Id)?.GroupId == group.Id);
            items.Add(new SidebarItem(group.Name, BrowseSection.UserGroup,
                "\u25CF", count, ColorHex: group.ColorHex));
        }
    }

    items.Add(new SidebarItem("Ungrouped", BrowseSection.Ungrouped, "\U0001F4C2",
        allAvatars.Count(a => string.IsNullOrWhiteSpace(avatarLibrary?.GetEntry(a.Id)?.GroupId))));

    return items;
}

private int GetRecentCount()
{
    if (avatarLibrary?.RecentAvatarIds is null) return 0;
    return avatarLibrary.RecentAvatarIds.Count(id => allAvatars.Any(a => a.Id == id));
}
```

- [ ] **Step 5: Implement ApplyFilter**

```csharp
private void ApplyFilter()
{
    FilteredAvatars.Clear();

    var search = searchText.Trim().ToLowerInvariant();
    var section = SelectedSidebarItem?.Section ?? BrowseSection.AllAvatars;
    var favGroupName = SelectedSidebarItem?.Label;

    foreach (var avatar in AllAvatars)
    {
        // Sidebar section filter
        if (!MatchesSection(avatar, section, favGroupName)) continue;

        // Search text
        if (!string.IsNullOrWhiteSpace(search))
        {
            var entry = avatarLibrary?.GetEntry(avatar.Id);
            var groupName = entry is not null && !string.IsNullOrWhiteSpace(entry.GroupId)
                ? avatarLibrary?.Groups.FirstOrDefault(g => g.Id == entry.GroupId)?.Name?.ToLowerInvariant()
                : null;
            var tagNames = entry?.TagIds
                .Select(id => avatarLibrary?.Tags.FirstOrDefault(t => t.Id == id)?.Name?.ToLowerInvariant())
                .Where(n => n is not null)
                .ToList() ?? [];

            var matchesSearch = avatar.Name.Contains(search, StringComparison.OrdinalIgnoreCase)
                || avatar.Id.Contains(search, StringComparison.OrdinalIgnoreCase)
                || (groupName is not null && groupName.Contains(search))
                || tagNames.Any(n => n!.Contains(search));

            if (!matchesSearch) continue;
        }

        // Style tag filter
        if (SelectedStyleTags.Count > 0)
        {
            if (!avatar.StyleTags.Any(t => SelectedStyleTags.Contains(t)))
                continue;
        }

        // Content tag filter
        if (SelectedContentTags.Count > 0)
        {
            if (!avatar.ContentTags.Any(t => SelectedContentTags.Contains(t)))
                continue;
        }

        // Platform filter
        if (!string.IsNullOrWhiteSpace(SelectedPlatform) && SelectedPlatform != "All")
        {
            if (avatar.Platform != SelectedPlatform)
                continue;
        }

        // User group filter (sidebar handles this, but check MY GROUPS section)
        if (section == BrowseSection.UserGroup)
        {
            var entry = avatarLibrary?.GetEntry(avatar.Id);
            var groupId = avatarLibrary?.Groups
                .FirstOrDefault(g => g.Name == SelectedSidebarItem?.Label)?.Id;
            if (entry?.GroupId != groupId) continue;
        }
        else if (section == BrowseSection.Ungrouped)
        {
            if (!string.IsNullOrWhiteSpace(avatarLibrary?.GetEntry(avatar.Id)?.GroupId))
                continue;
        }

        FilteredAvatars.Add(avatar);
    }

    RaisePropertyChanged(nameof(FilteredCountText));
}

private bool MatchesSection(VrChatAvatarSummary avatar, BrowseSection section, string? favGroupName)
{
    return section switch
    {
        BrowseSection.AllAvatars => true,
        BrowseSection.Recent => avatarLibrary?.RecentAvatarIds.Contains(avatar.Id) == true,
        BrowseSection.Favorites => avatar.IsFavorited,
        BrowseSection.FavoritesGroup1 or BrowseSection.FavoritesGroup2
            or BrowseSection.FavoritesGroup3 or BrowseSection.FavoritesGroup4
            => avatar.IsFavorited && avatar.FavoriteGroupName == favGroupName,
        BrowseSection.Uploaded => avatar.IsUploaded,
        BrowseSection.Purchased => avatar.IsLicensed,
        BrowseSection.LocalOsc => !avatar.IsUploaded && !avatar.IsFavorited && !avatar.IsLicensed,
        _ => true
    };
}
```

- [ ] **Step 6: Implement helper methods**

```csharp
public IReadOnlyList<string> GetSelectedAvatarIds() =>
    isMultiSelectMode
        ? SelectedMultiAvatarIds.ToList()
        : (string.IsNullOrWhiteSpace(selectedAvatarId) ? [] : [selectedAvatarId]);

public string FilteredCountText =>
    $"Showing {FilteredAvatars.Count} of {AllAvatars.Count} avatars";

private string GetSectionDescription()
{
    var total = allAvatars.Count;
    return SelectedSidebarItem?.Section switch
    {
        BrowseSection.Favorites => $"{SelectedSidebarItem.Count} avatars you've favorited",
        BrowseSection.Uploaded => $"{SelectedSidebarItem.Count} avatars you've uploaded",
        BrowseSection.Purchased => $"{SelectedSidebarItem.Count} avatars you've purchased",
        BrowseSection.LocalOsc => $"{SelectedSidebarItem.Count} avatars from LocalLow OSC",
        BrowseSection.Recent => $"Recently used avatars",
        _ => $"{total} total avatars"
    };
}
```

- [ ] **Step 7: Build to verify**

Run: `dotnet build "E:\!!!Program to work on\Proper Crystal Relay\VrcTwitchOscBridge\VrcTwitchOscBridge.csproj" --no-restore`
Expected: Build succeeds.

- [ ] **Step 8: Commit**

```bash
git add ViewModels/AvatarPickerViewModel.cs
git commit -m "feat: rewrite avatar picker view model with sidebar and filters"
```

---

### Task 6: Rewrite AvatarPickerWindow.xaml

**Files:**
- Rewrite: `AvatarPickerWindow.xaml`

**Interfaces:**
- Consumes: New `AvatarPickerViewModel` with sidebar/filter properties
- Produces: Full window layout with sidebar, filter bar, 170px cards, bottom bar

Due to the size of XAML changes, outline the key structural elements:

- Window: 1000x750 default, min 800x550, custom chrome
- Title bar: icon + "Pick Avatar" + "Crystal Relay" + refresh/manage/close buttons
- Content: Grid with two columns — 210px sidebar | * content
- Sidebar: ItemsControl with template per item (icon + label + count, expandable items with children)
- Content panel:
  - Search + view toggle row
  - Section title + description
  - Collapsible filter bar (border with header + three expandable rows)
  - ScrollViewer with WrapPanel containing 170px cards
  - Bottom bar showing count + selection + Cancel/OK

- [ ] **Step 1: Write the complete AvatarPickerWindow.xaml**

Write the file. Key sections:
- Line ~40: Window.Resources with all styles (reuse existing theme patterns but remove hardcoded color overrides — use DynamicResource throughout)
- Sidebar item template (icon + label + count, expandable with indented children)
- Filter chip templates (pill-shaped borders)
- 170px card template (image 150x120, name, author, platform + style tags)
- Bottom bar layout

- [ ] **Step 2: Build to verify**

Run: `dotnet build "E:\!!!Program to work on\Proper Crystal Relay\VrcTwitchOscBridge\VrcTwitchOscBridge.csproj" --no-restore`
Expected: Build succeeds.

- [ ] **Step 3: Commit**

```bash
git add AvatarPickerWindow.xaml
git commit -m "feat: redesign avatar picker window with sidebar layout"
```

---

### Task 7: Rewrite AvatarPickerWindow.xaml.cs

**Files:**
- Rewrite: `AvatarPickerWindow.xaml.cs`

**Interfaces:**
- Consumes: New ViewModel, new XAML
- Produces: Code-behind with sidebar click handlers, filter bar toggle, selection management

- [ ] **Step 1: Write the complete code-behind**

Key handlers:
- Constructor taking new parameters (favoriteGroups, avatarFavoriteGroups)
- Sidebar click → `viewModel.SelectedSidebarItem = ...`
- Filter chip click → toggle in `SelectedStyleTags` / `SelectedContentTags` / `SelectedPlatform`
- Expand/collapse filter bar → `viewModel.FiltersExpanded`
- Card click → `SelectAvatarItem`
- Heart toggle → call API + refresh
- Keyboard navigation (arrow keys in grid)
- Drag-drop for multi-select reordering

- [ ] **Step 2: Build to verify**

Run: `dotnet build "E:\!!!Program to work on\Proper Crystal Relay\VrcTwitchOscBridge\VrcTwitchOscBridge.csproj" --no-restore`
Expected: Build succeeds.

- [ ] **Step 3: Commit**

```bash
git add AvatarPickerWindow.xaml.cs
git commit -m "feat: rewrite avatar picker code-behind for new layout"
```

---

### Task 8: Replace AvatarRouletPickerWindow with Multi-Select Picker

**Files:**
- Delete: `AvatarRouletPickerWindow.xaml`
- Delete: `AvatarRouletPickerWindow.xaml.cs`
- Modify: `ViewModels/MainWindowViewModel.cs`
- (Possibly) Remove from `.csproj` if listed explicitly

- [ ] **Step 1: Find all references to AvatarRouletPickerWindow**

Search for `AvatarRouletPickerWindow` creation across the codebase.

- [ ] **Step 2: Redirect to AvatarPickerService.OpenMulti**

Replace code like:
```csharp
var window = new AvatarRouletPickerWindow(...);
window.ShowDialog();
```

With:
```csharp
var result = AvatarPickerService.OpenMulti(
    ThemeManager.CurrentTheme, avatars, Settings.AvatarLibrary,
    currentPool: currentPool, owner: Application.Current.MainWindow);
```

The MainWindowViewModel calls are in:
- Method handling roulette pool editing (search for `new AvatarRouletPickerWindow`)
- The roulette pool open/return flows

- [ ] **Step 3: Remove AvatarRouletPickerWindow files**

Delete both `.xaml` and `.xaml.cs`.

- [ ] **Step 4: Remove from .csproj if listed**

Check `VrcTwitchOscBridge.csproj` for `<Page Include="AvatarRouletPickerWindow.xaml" />` or similar and remove it.

- [ ] **Step 5: Build to verify**

Run: `dotnet build "E:\!!!Program to work on\Proper Crystal Relay\VrcTwitchOscBridge\VrcTwitchOscBridge.csproj" --no-restore`
Expected: Build succeeds with no references to the deleted files.

- [ ] **Step 6: Commit**

```bash
git add -A
git commit -m "feat: replace avatar roulette picker with multi-select in new picker"
```

---

### Task 9: Update MainWindowViewModel Callers for New Picker Data

**Files:**
- Modify: `ViewModels/MainWindowViewModel.cs`

- [ ] **Step 1: Update OpenAvatarPicker to pass favorite groups**

In the `OpenAvatarPicker` method, after building the avatar list, add favorite group data:

```csharp
private async void OpenAvatarPicker(object? parameter)
{
    // ... existing code to build avatars list ...

    IReadOnlyList<VrChatFavoriteGroup>? favGroups = null;
    Dictionary<string, string>? avatarFavGroups = null;

    if (vrChatApiClient?.IsConnected == true && authCookie is not null)
    {
        try
        {
            var groups = await vrChatApiClient.GetFavoriteGroupsAsync(authCookie);
            var entries = await vrChatApiClient.GetFavoriteEntriesAsync(authCookie);
            favGroups = groups.Select(g => new VrChatFavoriteGroup(g.id!, g.displayName ?? g.name!, g.name!, 
                entries.Count(e => e.tags?.Contains(g.name!) == true))).ToList();

            avatarFavGroups = new Dictionary<string, string>();
            foreach (var entry in entries)
            {
                if (entry.favoriteId is not null && entry.tags?.Count > 0)
                {
                    var groupName = groups.FirstOrDefault(g => g.name == entry.tags[0])?.displayName ?? entry.tags[0];
                    avatarFavGroups[entry.favoriteId] = groupName;
                }
            }
        }
        catch { /* offline — proceed without favorite data */ }
    }

    var context = parameter as string ?? "Profile";
    // ... rest of existing context logic ...

    var result = AvatarPickerService.OpenSingle(
        ThemeManager.CurrentTheme, avatars, Settings.AvatarLibrary,
        currentAvatarId, favGroups, avatarFavGroups, Application.Current.MainWindow);

    // ... existing result handling ...
}
```

- [ ] **Step 2: Update other callers as needed**

Search for `AvatarPickerService.OpenMulti` callers (roulette pool, swap roulette) and ensure they pass favorite groups.

- [ ] **Step 3: Add TrackRecentAvatar calls**

After any successful avatar picker result (where the user confirmed), call:
```csharp
Settings.AvatarLibrary?.TrackRecentAvatar(result.AvatarId);
```

- [ ] **Step 4: Build to verify**

Run: `dotnet build "E:\!!!Program to work on\Proper Crystal Relay\VrcTwitchOscBridge\VrcTwitchOscBridge.csproj" --no-restore`
Expected: Build succeeds.

- [ ] **Step 5: Commit**

```bash
git add ViewModels/MainWindowViewModel.cs
git commit -m "feat: wire up favorite group data and recent tracking in main VM"
```

---

### Task 10: Final Build and Integration Test

**Files:**
- No file changes — verification only

- [ ] **Step 1: Full build**

```bash
dotnet build "E:\!!!Program to work on\Proper Crystal Relay\VrcTwitchOscBridge\VrcTwitchOscBridge.csproj" --no-restore
```
Expected: Build succeeds with no errors or warnings.

- [ ] **Step 2: Run localization audit**

```bash
# Assuming there's a localizaton audit script
# Check if any new UI strings need localization entries
```

- [ ] **Step 3: Verify backward compatibility**

Checklist:
- Avatar Sets "Browse..." button opens picker correctly
- Avatar Swap "Browse" and "Browse Return" open picker correctly
- Power-Up avatar scope opens picker correctly
- Supporter avatar picker opens correctly
- Roulette pool opens in multi-select mode (not old window)

- [ ] **Step 4: Final commit if any fixes needed**

```bash
git commit -m "fix: build fixes and integration adjustments"
```

---

## Spec Coverage Check

| Spec Requirement | Task |
|-----------------|------|
| API: favorite group endpoints | Task 1 |
| API: expand avatar record (author, tags, platform) | Task 1 |
| Model: expand VrChatAvatarSummary | Task 2 |
| Model: VrChatFavoriteGroup | Task 2 |
| AvatarLibrary: recent tracking | Task 3 |
| Service: pass new data | Task 4 |
| ViewModel: sidebar navigation | Task 5 |
| ViewModel: filter state (style, platform, content) | Task 5 |
| ViewModel: favorite group resolution | Task 5 |
| View: sidebar layout | Task 6 |
| View: collapsible filter bar | Task 6 |
| View: 170px cards with platform + style tags | Task 6 |
| View: heart toggle on favorites | Task 7 |
| Code-behind: sidebar click, filter interaction | Task 7 |
| Replace AvatarRouletPickerWindow | Task 8 |
| Update MainWindowViewModel | Task 9 |
| Final integration | Task 10 |
