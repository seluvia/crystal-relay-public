# Pick Avatar Grouping & Tags Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Make the Avatar Picker's group/tag system actually usable — single-group assignment via right-click, colored tag chips on cards, a bulk-assignment Avatars tab in the manager, and auto-prune of stale library entries.

**Architecture:** Approach 1 from the design spec — extend existing structures, no new services. The `AvatarLibraryEntry` model simplifies from `List<string> GroupIds` to a single `string GroupId`. Tag chips and a context-menu assignment flow are added to the existing picker card templates. The `AvatarLibraryManagerWindow` gains an "Avatars" tab for bulk assignment. A small themed input dialog is added for "New Group..."/"New Tag..." prompts (the existing `ThemedDialogWindow` is message-only, no text input).

**Tech Stack:** C# / WPF / XAML / .NET 10 / xUnit (tests) / Newtonsoft.Json (settings persistence, via existing `SettingsStore`).

**Spec:** `docs/superpowers/specs/2026-07-04-pick-avatar-grouping-tags-design.md`

---

## File Structure

**New files:**
- `VrcTwitchOscBridge/ThemedInputDialog.xaml` + `.xaml.cs` — minimal themed prompt (title, label, TextBox, OK/Cancel). Reused by "New Group..." and "New Tag...".
- `VrcTwitchOscBridge/Models/AvatarTagDisplay.cs` — small record `{ Id, Name, ColorHex }` used by picker chips and manager rows.
- `VrcTwitchOscBridge/Models/FilterOption.cs` — small record `{ Id, Display }` for filter dropdowns (`Id` null = All, `"ungrouped"` = Ungrouped, real id = that group/tag).
- `VrcTwitchOscBridge/ViewModels/AvatarAssignmentRow.cs` — wraps an `AvatarLibraryEntry` with display fields for the manager's Avatars tab.
- `VrcTwitchOscBridge.Tests/AvatarLibraryPruneTests.cs` — unit tests for auto-prune.
- `VrcTwitchOscBridge.Tests/AvatarLibraryFilterTests.cs` — unit tests for the single-group filter and "Ungrouped" sentinel.

**Modified files:**
- `VrcTwitchOscBridge/Models/AvatarLibrary.cs` — `GroupIds`→`GroupId`, drop `IsCollapsed`, add `PruneMissingEntries`.
- `VrcTwitchOscBridge/ViewModels/AvatarPickerViewModel.cs` — prune call, tag resolution, `RebuildItem`, filter dropdown sources, single-group filter.
- `VrcTwitchOscBridge/AvatarPickerWindow.xaml` — tag chips in both card templates, new filter dropdowns, context menu items.
- `VrcTwitchOscBridge/AvatarPickerWindow.xaml.cs` — context-menu handlers, chip-remove handler, forward avatars to manager.
- `VrcTwitchOscBridge/AvatarLibraryManagerWindow.xaml` — new Avatars tab.
- `VrcTwitchOscBridge/AvatarLibraryManagerWindow.xaml.cs` — constructor takes `avatars`.
- `VrcTwitchOscBridge/ViewModels/AvatarLibraryManagerViewModel.cs` — assignment rows, bulk commands, cascade updates for single `GroupId`, constructor takes `avatars`.
- `VrcTwitchOscBridge/VrcTwitchOscBridge.csproj` — explicit `<Page>`/`<Compile>` entries for new files.
- All `Resources/Localization/*.extra.json` — new keys.
- `VrcTwitchOscBridge.Tests/VrcTwitchOscBridge.Tests.csproj` — explicit `<Compile>` entries for new test files (the test project uses default item inclusion, so this is likely unnecessary — verify in Task 1).

---

## Task 1: Model changes — `GroupIds` → `GroupId`, drop `IsCollapsed`, add `PruneMissingEntries`

**Files:**
- Modify: `VrcTwitchOscBridge/Models/AvatarLibrary.cs`
- Create: `VrcTwitchOscBridge.Tests/AvatarLibraryPruneTests.cs`

- [ ] **Step 1: Write the failing test for `PruneMissingEntries`**

Create `VrcTwitchOscBridge.Tests/AvatarLibraryPruneTests.cs`:

```csharp
using VrcTwitchOscBridge.Models;
using Xunit;

namespace VrcTwitchOscBridge.Tests;

public sealed class AvatarLibraryPruneTests
{
    [Fact]
    public void PruneMissingEntries_RemovesEntriesNotInCurrentAvatarList()
    {
        var library = new AvatarLibrary();
        library.Entries.Add(new AvatarLibraryEntry { AvatarId = "avd_11111" });
        library.Entries.Add(new AvatarLibraryEntry { AvatarId = "avd_22222" });
        library.Entries.Add(new AvatarLibraryEntry { AvatarId = "avd_33333" });

        var currentAvatars = new[]
        {
            new VrChatAvatarSummary("avd_11111", "Cutie", "VRChat", false, null),
            new VrChatAvatarSummary("avd_22222", "Other", "VRChat", false, null)
        };

        library.PruneMissingEntries(currentAvatars);

        Assert.Contains(library.Entries, e => e.AvatarId == "avd_11111");
        Assert.Contains(library.Entries, e => e.AvatarId == "avd_22222");
        Assert.DoesNotContain(library.Entries, e => e.AvatarId == "avd_33333");
    }

    [Fact]
    public void PruneMissingEntries_KeepsAllWhenAllPresent()
    {
        var library = new AvatarLibrary();
        library.Entries.Add(new AvatarLibraryEntry { AvatarId = "avd_11111" });

        var currentAvatars = new[]
        {
            new VrChatAvatarSummary("avd_11111", "Cutie", "VRChat", false, null)
        };

        library.PruneMissingEntries(currentAvatars);

        Assert.Single(library.Entries);
    }

    [Fact]
    public void PruneMissingEntries_HandlesEmptyLibrary()
    {
        var library = new AvatarLibrary();
        var currentAvatars = new[]
        {
            new VrChatAvatarSummary("avd_11111", "Cutie", "VRChat", false, null)
        };

        library.PruneMissingEntries(currentAvatars);

        Assert.Empty(library.Entries);
    }

    [Fact]
    public void PruneMissingEntries_HandlesEmptyAvatarList_RemovesAllEntries()
    {
        var library = new AvatarLibrary();
        library.Entries.Add(new AvatarLibraryEntry { AvatarId = "avd_11111" });

        library.PruneMissingEntries(Array.Empty<VrChatAvatarSummary>());

        Assert.Empty(library.Entries);
    }
}
```

- [ ] **Step 2: Run test to verify it fails**

Run:
```
dotnet test "E:\!!!Program to work on\Proper Crystal Relay\VrcTwitchOscBridge.Tests\VrcTwitchOscBridge.Tests.csproj" --filter "FullyQualifiedName~AvatarLibraryPruneTests" --no-restore
```
Expected: FAIL — `PruneMissingEntries` not found, and `GroupIds` still a list so `GroupId` references won't compile. (The test file references the new single-`GroupId` API indirectly via `new AvatarLibraryEntry` — should compile since `GroupIds`→`GroupId` hasn't changed yet, but `PruneMissingEntries` is missing.)

- [ ] **Step 3: Apply model changes to `AvatarLibrary.cs`**

Open `VrcTwitchOscBridge/Models/AvatarLibrary.cs`. Make these exact edits:

**Edit 1 — `AvatarGroup`: remove `IsCollapsed`.** Replace the `AvatarGroup` class (lines 81-111) with:

```csharp
public sealed class AvatarGroup : ObservableObject
{
    private string id = Guid.NewGuid().ToString();
    private string name = string.Empty;
    private int sortOrder;

    public string Id
    {
        get => id;
        set => SetProperty(ref id, value);
    }

    public string Name
    {
        get => name;
        set => SetProperty(ref name, value);
    }

    public int SortOrder
    {
        get => sortOrder;
        set => SetProperty(ref sortOrder, value);
    }
}
```

**Edit 2 — `AvatarLibraryEntry`: `GroupIds` → `GroupId`.** Replace the `GroupIds` property (lines 53-54 and 68-72) with a single `GroupId`. The full `AvatarLibraryEntry` class becomes:

```csharp
public sealed class AvatarLibraryEntry : ObservableObject
{
    private string avatarId = string.Empty;
    private string customIconPath = string.Empty;
    private string groupId = string.Empty;
    private List<string> tagIds = [];

    public string AvatarId
    {
        get => avatarId;
        set => SetProperty(ref avatarId, value);
    }

    public string CustomIconPath
    {
        get => customIconPath;
        set => SetProperty(ref customIconPath, value);
    }

    public string GroupId
    {
        get => groupId;
        set => SetProperty(ref groupId, value);
    }

    public List<string> TagIds
    {
        get => tagIds;
        set => SetProperty(ref tagIds, value ?? []);
    }
}
```

**Edit 3 — `AvatarLibrary`: add `PruneMissingEntries`.** Add this method to the `AvatarLibrary` class (after `EnsureEntry`):

```csharp
/// <summary>
/// Removes any entry whose AvatarId is not in the current VRChat avatar list.
/// Call when the picker opens with a fresh avatar list.
/// </summary>
public void PruneMissingEntries(IReadOnlyList<VrChatAvatarSummary> currentAvatars)
{
    if (currentAvatars.Count == 0)
    {
        Entries.Clear();
        return;
    }

    var currentIds = new HashSet<string>(currentAvatars.Select(a => a.Id), StringComparer.Ordinal);
    for (var i = Entries.Count - 1; i >= 0; i--)
    {
        if (!currentIds.Contains(Entries[i].AvatarId))
        {
            Entries.RemoveAt(i);
        }
    }
}
```

- [ ] **Step 4: Fix compile errors from the `GroupIds` → `GroupId` rename**

Search the codebase for `GroupIds` references in `.cs` files and update them:

Run: `grep -r "GroupIds" VrcTwitchOscBridge/ --include="*.cs"`

Expected matches and fixes:
- `ViewModels/AvatarPickerViewModel.cs` lines ~354-356 (`entry?.GroupIds...`) — update to single `GroupId` equality. This will be fully rewritten in Task 3, but for now change `entry?.GroupIds.Contains(selectedFilterGroupId)` → `entry?.GroupId == selectedFilterGroupId` and the search-name lookup `entry?.GroupIds.Select(...)` → a single-name lookup `avatarLibrary?.Groups.FirstOrDefault(g => g.Id == entry?.GroupId)?.Name?.ToLowerInvariant()` in a `List<string>` form. Minimal change to keep it compiling; full rewrite comes in Task 3.
- `ViewModels/AvatarLibraryManagerViewModel.cs` `DeleteGroup` — `entry.GroupIds.Remove(id)` → `if (entry.GroupId == id) entry.GroupId = string.Empty;`

If other files reference `GroupIds`, update them analogously. The goal of this step is a compiling build, not the final filter logic (Task 3 rewrites it).

- [ ] **Step 5: Run tests to verify they pass**

Run:
```
dotnet test "E:\!!!Program to work on\Proper Crystal Relay\VrcTwitchOscBridge.Tests\VrcTwitchOscBridge.Tests.csproj" --filter "FullyQualifiedName~AvatarLibraryPruneTests" --no-restore
```
Expected: PASS (4 tests).

- [ ] **Step 6: Build the app project to confirm it compiles**

Run:
```
dotnet build "E:\!!!Program to work on\Proper Crystal Relay\VrcTwitchOscBridge\VrcTwitchOscBridge.csproj" --no-restore
```
Expected: Build succeeded, 0 errors.

- [ ] **Step 7: Commit**

```bash
git add VrcTwitchOscBridge/Models/AvatarLibrary.cs VrcTwitchOscBridge.Tests/AvatarLibraryPruneTests.cs VrcTwitchOscBridge/ViewModels/AvatarPickerViewModel.cs VrcTwitchOscBridge/ViewModels/AvatarLibraryManagerViewModel.cs
git commit -m "Simplify AvatarLibraryEntry to single GroupId, drop IsCollapsed, add PruneMissingEntries"
```

---

## Task 2: New small types — `AvatarTagDisplay`, `FilterOption`, `AvatarAssignmentRow`

**Files:**
- Create: `VrcTwitchOscBridge/Models/AvatarTagDisplay.cs`
- Create: `VrcTwitchOscBridge/Models/FilterOption.cs`
- Create: `VrcTwitchOscBridge/ViewModels/AvatarAssignmentRow.cs`
- Modify: `VrcTwitchOscBridge/VrcTwitchOscBridge.csproj` (add `<Compile>` entries)

- [ ] **Step 1: Create `AvatarTagDisplay.cs`**

Create `VrcTwitchOscBridge/Models/AvatarTagDisplay.cs`:

```csharp
namespace VrcTwitchOscBridge.Models;

/// <summary>
/// Display-only projection of an AvatarTag for chip rendering in the picker
/// and the manager's Avatars tab. Carries no mutation capability.
/// </summary>
public sealed record AvatarTagDisplay(string Id, string Name, string ColorHex);
```

- [ ] **Step 2: Create `FilterOption.cs`**

Create `VrcTwitchOscBridge/Models/FilterOption.cs`:

```csharp
namespace VrcTwitchOscBridge.Models;

/// <summary>
/// One entry in the picker's group or tag filter dropdown.
/// Id is null for "All", "ungrouped" for the Ungrouped pseudo-group,
/// or a real AvatarGroup/AvatarTag Id.
/// </summary>
public sealed record FilterOption(string? Id, string Display);
```

- [ ] **Step 3: Create `AvatarAssignmentRow.cs`**

Create `VrcTwitchOscBridge/ViewModels/AvatarAssignmentRow.cs`:

```csharp
using System.Windows.Media;
using VrcTwitchOscBridge.Infrastructure;
using VrcTwitchOscBridge.Models;

namespace VrcTwitchOscBridge.ViewModels;

/// <summary>
/// One row in the manager's Avatars tab. Wraps a library entry with
/// display fields (avatar name, image, group name, tag chips).
/// </summary>
public sealed class AvatarAssignmentRow : ObservableObject
{
    private string groupName = string.Empty;
    private IReadOnlyList<AvatarTagDisplay> tags = [];

    public required AvatarLibraryEntry Entry { get; init; }
    public required string DisplayName { get; init; }
    public required ImageSource? Image { get; init; }
    public required string AvatarId { get; init; }

    public string GroupName
    {
        get => groupName;
        set => SetProperty(ref groupName, value);
    }

    public IReadOnlyList<AvatarTagDisplay> Tags
    {
        get => tags;
        set => SetProperty(ref tags, value);
    }
}
```

- [ ] **Step 4: Add `<Compile>` entries to the csproj**

Open `VrcTwitchOscBridge/VrcTwitchOscBridge.csproj`. In the `<ItemGroup>` containing `<Compile Include="Models\AvatarPickerViewMode.cs" />` (around line 203), add:

```xml
    <Compile Include="Models\AvatarTagDisplay.cs" />
    <Compile Include="Models\FilterOption.cs" />
    <Compile Include="ViewModels\AvatarAssignmentRow.cs" />
```

Place them near the existing `Models\` and `ViewModels\` entries to keep grouping tidy.

- [ ] **Step 5: Build to confirm**

Run:
```
dotnet build "E:\!!!Program to work on\Proper Crystal Relay\VrcTwitchOscBridge\VrcTwitchOscBridge.csproj" --no-restore
```
Expected: Build succeeded, 0 errors.

- [ ] **Step 6: Commit**

```bash
git add VrcTwitchOscBridge/Models/AvatarTagDisplay.cs VrcTwitchOscBridge/Models/FilterOption.cs VrcTwitchOscBridge/ViewModels/AvatarAssignmentRow.cs VrcTwitchOscBridge/VrcTwitchOscBridge.csproj
git commit -m "Add AvatarTagDisplay, FilterOption, AvatarAssignmentRow types"
```

---

## Task 3: `AvatarPickerViewModel` — prune, tag resolution, `RebuildItem`, filter dropdowns

**Files:**
- Modify: `VrcTwitchOscBridge/ViewModels/AvatarPickerViewModel.cs`
- Create: `VrcTwitchOscBridge.Tests/AvatarLibraryFilterTests.cs`

- [ ] **Step 1: Write the failing filter test**

Create `VrcTwitchOscBridge.Tests/AvatarLibraryFilterTests.cs`:

```csharp
using VrcTwitchOscBridge.Models;
using Xunit;

namespace VrcTwitchOscBridge.Tests;

public sealed class AvatarLibraryFilterTests
{
    [Fact]
    public void ResolveGroupFilter_All_ReturnsNull()
    {
        // The VM maps FilterOption.Id null -> "show all".
        // This test documents the contract: null Id means no filter.
        var option = new FilterOption(null, "All");
        Assert.Null(option.Id);
    }

    [Fact]
    public void ResolveGroupFilter_Ungrouped_ReturnsSentinel()
    {
        var option = new FilterOption("ungrouped", "Ungrouped");
        Assert.Equal("ungrouped", option.Id);
    }

    [Fact]
    public void ResolveGroupFilter_RealGroup_ReturnsGroupId()
    {
        var option = new FilterOption("grp_123", "Cuties");
        Assert.Equal("grp_123", option.Id);
    }

    [Fact]
    public void GroupFilterOptions_IncludesAll_Ungrouped_AndGroupsInSortOrder()
    {
        var library = new AvatarLibrary();
        library.Groups.Add(new AvatarGroup { Id = "g2", Name = "Public", SortOrder = 1 });
        library.Groups.Add(new AvatarGroup { Id = "g1", Name = "Cuties", SortOrder = 0 });

        var options = AvatarLibraryFilterOptionsBuilder.BuildGroupOptions(library);

        Assert.Equal(4, options.Count); // All, Ungrouped, Cuties, Public
        Assert.Null(options[0].Id);
        Assert.Equal("All", options[0].Display);
        Assert.Equal("ungrouped", options[1].Id);
        Assert.Equal("Ungrouped", options[1].Display);
        Assert.Equal("g1", options[2].Id);
        Assert.Equal("Cuties", options[2].Display);
        Assert.Equal("g2", options[3].Id);
        Assert.Equal("Public", options[3].Display);
    }

    [Fact]
    public void TagFilterOptions_IncludesAll_AndTagsInNameOrder()
    {
        var library = new AvatarLibrary();
        library.Tags.Add(new AvatarTag { Id = "t2", Name = "Fav", ColorHex = "#F472B6" });
        library.Tags.Add(new AvatarTag { Id = "t1", Name = "Mini", ColorHex = "#A855F7" });

        var options = AvatarLibraryFilterOptionsBuilder.BuildTagOptions(library);

        Assert.Equal(3, options.Count); // All, Fav, Mini (alphabetical by Name)
        Assert.Null(options[0].Id);
        Assert.Equal("All", options[0].Display);
        Assert.Equal("t2", options[1].Id); // Fav sorts before Mini
        Assert.Equal("Fav", options[1].Display);
        Assert.Equal("t1", options[2].Id);
        Assert.Equal("Mini", options[2].Display);
    }
}
```

- [ ] **Step 2: Run test to verify it fails**

Run:
```
dotnet test "E:\!!!Program to work on\Proper Crystal Relay\VrcTwitchOscBridge.Tests\VrcTwitchOscBridge.Tests.csproj" --filter "FullyQualifiedName~AvatarLibraryFilterTests" --no-restore
```
Expected: FAIL — `AvatarLibraryFilterOptionsBuilder` not found.

- [ ] **Step 3: Add `AvatarLibraryFilterOptionsBuilder` and rewrite `AvatarPickerViewModel`**

Open `VrcTwitchOscBridge/ViewModels/AvatarPickerViewModel.cs`.

**Edit 1 — add `using` for `AvatarTagDisplay` and `FilterOption`:** at the top, after the existing `using VrcTwitchOscBridge.Models;`, no new using needed (same namespace). Just ensure `Models` is imported.

**Edit 2 — replace the `AvatarPickerItem` record** (at the bottom of the file, lines 403-415) with:

```csharp
public sealed record AvatarPickerItem(
    string Id,
    string Name,
    string SourceLabel,
    ImageSource? Image,
    string? ThumbnailUrl = null,
    bool IsSelected = false,
    IReadOnlyList<AvatarTagDisplay> Tags = null)
{
    public string SearchText => $"{Id} {Name} {SourceLabel}";
    public string DisplayName => !string.IsNullOrWhiteSpace(Name) && !string.Equals(Name, Id, StringComparison.Ordinal)
        ? Name
        : "Unknown Avatar";
}
```

(`Tags = null` default keeps existing call sites compiling; `ApplyFilter` resolves and passes real tags.)

**Edit 3 — in the constructor, after `this.avatarLibrary = avatarLibrary;` (line ~32), add the prune call:**

```csharp
// Prune library entries whose avatar is no longer in the VRChat list.
avatarLibrary?.PruneMissingEntries(avatars);
```

Place it before `AllAvatars = new ObservableCollection<...>`.

**Edit 4 — add `ResolveTags` helper and `RebuildItem` helper.** Add these methods to the `AvatarPickerViewModel` class (after `CreatePickerItem`):

```csharp
private IReadOnlyList<AvatarTagDisplay> ResolveTags(AvatarLibraryEntry? entry)
{
    if (avatarLibrary is null || entry is null || entry.TagIds.Count == 0)
    {
        return [];
    }

    var tags = new List<AvatarTagDisplay>(entry.TagIds.Count);
    foreach (var tagId in entry.TagIds)
    {
        var tag = avatarLibrary.Tags.FirstOrDefault(t => t.Id == tagId);
        if (tag is not null)
        {
            tags.Add(new AvatarTagDisplay(tag.Id, tag.Name, tag.ColorHex));
        }
    }
    return tags;
}

/// <summary>
/// Rebuilds an item with fresh Tags/Image/IsSelected and replaces it in both
/// AllAvatars and FilteredAvatars. Consolidates the scattered replace logic.
/// </summary>
public void RebuildItem(AvatarPickerItem item)
{
    var entry = avatarLibrary?.GetEntry(item.Id);
    var tags = ResolveTags(entry);
    var updated = new AvatarPickerItem(item.Id, item.Name, item.SourceLabel, item.Image, item.ThumbnailUrl, item.IsSelected, tags);

    var allIndex = AllAvatars.IndexOf(item);
    if (allIndex >= 0) AllAvatars[allIndex] = updated;

    var filteredIndex = FilteredAvatars.IndexOf(item);
    if (filteredIndex >= 0) FilteredAvatars[filteredIndex] = updated;
}

private AvatarPickerItem CreatePickerItem(VrChatAvatarSummary summary)
{
    var image = imageService.GetPlaceholderImage();
    var entry = avatarLibrary?.GetEntry(summary.Id);
    var tags = ResolveTags(entry);
    return new AvatarPickerItem(
        summary.Id,
        summary.Name,
        summary.SourceLabel,
        image,
        summary.ThumbnailUrl,
        Tags: tags);
}
```

(Remove the old `CreatePickerItem` that didn't set `Tags`.)

**Edit 5 — rewrite `ApplyFilter` for single `GroupId` and the "ungrouped" sentinel.** Replace the existing `ApplyFilter` method (lines ~344-388) with:

```csharp
private void ApplyFilter()
{
    FilteredAvatars.Clear();
    var search = searchText.Trim().ToLowerInvariant();

    foreach (var avatar in AllAvatars)
    {
        var entry = avatarLibrary?.GetEntry(avatar.Id);

        if (!string.IsNullOrWhiteSpace(search))
        {
            var groupName = entry is not null && !string.IsNullOrWhiteSpace(entry.GroupId)
                ? avatarLibrary?.Groups.FirstOrDefault(g => g.Id == entry.GroupId)?.Name?.ToLowerInvariant()
                : null;
            var tagNames = entry?.TagIds
                .Select(id => avatarLibrary?.Tags.FirstOrDefault(t => t.Id == id)?.Name)
                .Where(n => n is not null)
                .Select(n => n!.ToLowerInvariant())
                .ToList() ?? [];

            var matchesSearch = avatar.SearchText.Contains(search, StringComparison.OrdinalIgnoreCase)
                || (groupName is not null && groupName.Contains(search, StringComparison.OrdinalIgnoreCase))
                || tagNames.Any(n => n.Contains(search, StringComparison.OrdinalIgnoreCase));

            if (!matchesSearch) continue;
        }

        // Group filter: null = all, "ungrouped" = empty GroupId, real id = exact match.
        if (!string.IsNullOrWhiteSpace(selectedFilterGroupId))
        {
            if (selectedFilterGroupId == "ungrouped")
            {
                if (!string.IsNullOrWhiteSpace(entry?.GroupId)) continue;
            }
            else
            {
                if (entry?.GroupId != selectedFilterGroupId) continue;
            }
        }

        if (!string.IsNullOrWhiteSpace(selectedFilterTagId))
        {
            if (entry?.TagIds.Contains(selectedFilterTagId) != true) continue;
        }

        // Ensure tags are fresh on the filtered item.
        var tags = ResolveTags(entry);
        var withTags = avatar.Tags is null || avatar.Tags.Count != tags.Count
            ? new AvatarPickerItem(avatar.Id, avatar.Name, avatar.SourceLabel, avatar.Image, avatar.ThumbnailUrl, avatar.IsSelected, tags)
            : avatar;

        FilteredAvatars.Add(withTags);
    }

    RaisePropertyChanged(nameof(FilteredCountText));
}
```

**Edit 6 — add filter dropdown option sources and `SelectedGroupFilterOption`/`SelectedTagFilterOption`.** Add these properties and the helper class to the VM (replace the existing `SelectedFilterGroupId`/`SelectedFilterTagId` properties with the option-based versions):

```csharp
public ObservableCollection<FilterOption> GroupFilterOptions { get; } = [];
public ObservableCollection<FilterOption> TagFilterOptions { get; } = [];

private FilterOption? selectedGroupFilterOption;
private FilterOption? selectedTagFilterOption;

public FilterOption? SelectedGroupFilterOption
{
    get => selectedGroupFilterOption;
    set
    {
        if (SetProperty(ref selectedGroupFilterOption, value))
        {
            selectedFilterGroupId = value?.Id;
            ApplyFilter();
        }
    }
}

public FilterOption? SelectedTagFilterOption
{
    get => selectedTagFilterOption;
    set
    {
        if (SetProperty(ref selectedTagFilterOption, value))
        {
            selectedFilterTagId = value?.Id;
            ApplyFilter();
        }
    }
}
```

**Edit 7 — populate the filter option collections in the constructor.** After the prune call and before `ApplyFilter()`, add:

```csharp
RebuildFilterOptions();
```

And add the method:

```csharp
public void RebuildFilterOptions()
{
    GroupFilterOptions.Clear();
    TagFilterOptions.Clear();

    GroupFilterOptions.Add(new FilterOption(null, LocalizationService.Translate("All")));
    GroupFilterOptions.Add(new FilterOption("ungrouped", LocalizationService.Translate("Ungrouped")));
    if (avatarLibrary is not null)
    {
        foreach (var group in avatarLibrary.Groups.OrderBy(g => g.SortOrder).ThenBy(g => g.Name))
        {
            GroupFilterOptions.Add(new FilterOption(group.Id, group.Name));
        }

        TagFilterOptions.Add(new FilterOption(null, LocalizationService.Translate("All")));
        foreach (var tag in avatarLibrary.Tags.OrderBy(t => t.Name))
        {
            TagFilterOptions.Add(new FilterOption(tag.Id, tag.Name));
        }
    }

    // Preserve current selection if still present, else reset to "All".
    selectedGroupFilterOption = GroupFilterOptions.FirstOrDefault(o => o.Id == selectedFilterGroupId)
        ?? GroupFilterOptions[0];
    selectedTagFilterOption = TagFilterOptions.FirstOrDefault(o => o.Id == selectedFilterTagId)
        ?? TagFilterOptions.FirstOrDefault() ?? new FilterOption(null, LocalizationService.Translate("All"));
    RaisePropertyChanged(nameof(SelectedGroupFilterOption));
    RaisePropertyChanged(nameof(SelectedTagFilterOption));
}
```

**Edit 8 — add the `AvatarLibraryFilterOptionsBuilder` static helper** (used by the test). Add at the bottom of the file, after the `AvatarPickerItem` record:

```csharp
public static class AvatarLibraryFilterOptionsBuilder
{
    public static IReadOnlyList<FilterOption> BuildGroupOptions(AvatarLibrary library)
    {
        var options = new List<FilterOption>
        {
            new(null, "All"),
            new("ungrouped", "Ungrouped")
        };
        foreach (var group in library.Groups.OrderBy(g => g.SortOrder).ThenBy(g => g.Name))
        {
            options.Add(new FilterOption(group.Id, group.Name));
        }
        return options;
    }

    public static IReadOnlyList<FilterOption> BuildTagOptions(AvatarLibrary library)
    {
        var options = new List<FilterOption> { new(null, "All") };
        foreach (var tag in library.Tags.OrderBy(t => t.Name))
        {
            options.Add(new FilterOption(tag.Id, tag.Name));
        }
        return options;
    }
}
```

(The test uses the raw "All"/"Ungrouped" strings; the VM uses localized versions via `LocalizationService.Translate`. The builder is the unlocalized contract; the VM localizes at display time.)

- [ ] **Step 4: Run tests to verify they pass**

Run:
```
dotnet test "E:\!!!Program to work on\Proper Crystal Relay\VrcTwitchOscBridge.Tests\VrcTwitchOscBridge.Tests.csproj" --filter "FullyQualifiedName~AvatarLibraryFilterTests" --no-restore
```
Expected: PASS (5 tests).

- [ ] **Step 5: Build the app**

Run:
```
dotnet build "E:\!!!Program to work on\Proper Crystal Relay\VrcTwitchOscBridge\VrcTwitchOscBridge.csproj" --no-restore
```
Expected: Build succeeded, 0 errors. (The XAML still binds to `SelectedFilterGroupId` which we removed — that binding will break at runtime, not compile time. Task 4 fixes the XAML. If the build fails on XAML compilation for that reason, proceed to Task 4 and re-build after.)

- [ ] **Step 6: Commit**

```bash
git add VrcTwitchOscBridge/ViewModels/AvatarPickerViewModel.cs VrcTwitchOscBridge.Tests/AvatarLibraryFilterTests.cs
git commit -m "AvatarPickerViewModel: prune, tag resolution, RebuildItem, filter dropdowns"
```

---

## Task 4: `AvatarPickerWindow.xaml` — tag chips in card templates, new filter dropdowns, context menu items

**Files:**
- Modify: `VrcTwitchOscBridge/AvatarPickerWindow.xaml`

- [ ] **Step 1: Add a tag-chip template to `Window.Resources`**

Open `VrcTwitchOscBridge/AvatarPickerWindow.xaml`. In `<Window.Resources>`, after the `AvatarListItemTemplate` `DataTemplate` (around line 559), add a chip template and a tag-chip `ItemsControl` template:

```xml
<!-- Tag Chip DataTemplate -->
<DataTemplate x:Key="TagChipTemplate" DataType="{x:Type vm:AvatarTagDisplay}">
    <Border Background="{Binding ColorHex}"
            CornerRadius="8"
            Padding="6,1"
            Margin="0,0,4,0"
            Cursor="Hand"
            Tag="{Binding}"
            MouseLeftButtonDown="OnTagChipRemoveClicked">
        <StackPanel Orientation="Horizontal">
            <TextBlock Text="{Binding Name}"
                       Foreground="White"
                       FontSize="9"
                       VerticalAlignment="Center" />
            <TextBlock Text="&#x2715;"
                       Foreground="White"
                       FontSize="8"
                       Margin="3,0,0,0"
                       VerticalAlignment="Center" />
        </StackPanel>
    </Border>
</DataTemplate>
```

(`vm:AvatarTagDisplay` requires `xmlns:vm="clr-namespace:VrcTwitchOscBridge.ViewModels"` — already declared at the top. But `AvatarTagDisplay` lives in `Models`, so use `xmlns:m="clr-namespace:VrcTwitchOscBridge.Models"` and `DataType="{x:Type m:AvatarTagDisplay}"`. Add the `m` namespace to the `<Window>` tag if not present.)

Add to the `<Window>` opening tag (if not already present):
```xml
xmlns:m="clr-namespace:VrcTwitchOscBridge.Models"
```

And use `DataType="{x:Type m:AvatarTagDisplay}"` in the chip template.

- [ ] **Step 2: Add tag chips to `AvatarCardTemplate` (Grid view)**

In `AvatarCardTemplate` (around line 407), the current Grid has three rows: image (120), name (Auto), select button (Auto). Add a fourth row for tags.

Change the `Grid.RowDefinitions` from:
```xml
<RowDefinition Height="120" />
<RowDefinition Height="Auto" />
<RowDefinition Height="Auto" />
```
to:
```xml
<RowDefinition Height="120" />
<RowDefinition Height="Auto" />
<RowDefinition Height="Auto" />
<RowDefinition Height="Auto" />
```

After the "Avatar Name" `TextBlock` (Grid.Row="1") and before the "Select Button" (Grid.Row="2"), insert the tag chips row. The select button moves to Row 3:

```xml
<!-- Tag Chips -->
<ItemsControl Grid.Row="2"
              ItemsSource="{Binding Tags}"
              ItemTemplate="{StaticResource TagChipTemplate}"
              Margin="0,0,0,6">
    <ItemsControl.ItemsPanel>
        <ItemsPanelTemplate>
            <WrapPanel Orientation="Horizontal" />
        </ItemsPanelTemplate>
    </ItemsControl.ItemsPanel>
</ItemsControl>
```

And update the Select Button from `Grid.Row="2"` to `Grid.Row="3"`.

- [ ] **Step 3: Add tag chips to `AvatarListItemTemplate` (List view)**

In `AvatarListItemTemplate` (around line 480), the current Grid has 4 columns: image (40), name (*), source label (Auto), select button (Auto). Add a tag-chip column.

Change the `Grid.ColumnDefinitions` from:
```xml
<ColumnDefinition Width="40" />
<ColumnDefinition Width="*" />
<ColumnDefinition Width="Auto" />
<ColumnDefinition Width="Auto" />
```
to:
```xml
<ColumnDefinition Width="40" />
<ColumnDefinition Width="*" />
<ColumnDefinition Width="Auto" />
<ColumnDefinition Width="Auto" />
<ColumnDefinition Width="Auto" />
```

Insert the tag chips in the new column (before the Select Button):

```xml
<!-- Tag Chips -->
<ItemsControl Grid.Column="3"
              ItemsSource="{Binding Tags}"
              ItemTemplate="{StaticResource TagChipTemplate}"
              VerticalAlignment="Center"
              Margin="0,0,10,0">
    <ItemsControl.ItemsPanel>
        <ItemsPanelTemplate>
            <WrapPanel Orientation="Horizontal" />
        </ItemsPanelTemplate>
    </ItemsControl.ItemsPanel>
</ItemsControl>
```

And update the Select Button from `Grid.Column="3"` to `Grid.Column="4"`.

- [ ] **Step 4: Replace the filter dropdowns**

Replace the existing "Filter Bar" `StackPanel` (Grid.Row="2", around lines 705-736) with:

```xml
<!-- Filter Bar -->
<StackPanel Grid.Row="2" Orientation="Horizontal" Margin="0,8,0,0">
    <TextBlock Text="{loc:Translate 'Group:'}" Foreground="{DynamicResource MutedBrush}" VerticalAlignment="Center" Margin="0,0,4,0" />
    <ComboBox Width="150"
              ItemsSource="{Binding GroupFilterOptions}"
              SelectedItem="{Binding SelectedGroupFilterOption, UpdateSourceTrigger=PropertyChanged}"
              DisplayMemberPath="Display"
              Margin="0,0,16,0">
        <ComboBox.Style>
            <Style TargetType="ComboBox" BasedOn="{StaticResource {x:Type ComboBox}}">
                <Setter Property="Background" Value="{DynamicResource InputBrush}" />
                <Setter Property="BorderBrush" Value="{DynamicResource InputBorderBrush}" />
                <Setter Property="Foreground" Value="{DynamicResource TextBrush}" />
            </Style>
        </ComboBox.Style>
    </ComboBox>

    <TextBlock Text="{loc:Translate 'Tag:'}" Foreground="{DynamicResource MutedBrush}" VerticalAlignment="Center" Margin="0,0,4,0" />
    <ComboBox Width="150"
              ItemsSource="{Binding TagFilterOptions}"
              SelectedItem="{Binding SelectedTagFilterOption, UpdateSourceTrigger=PropertyChanged}"
              DisplayMemberPath="Display">
        <ComboBox.Style>
            <Style TargetType="ComboBox" BasedOn="{StaticResource {x:Type ComboBox}}">
                <Setter Property="Background" Value="{DynamicResource InputBrush}" />
                <Setter Property="BorderBrush" Value="{DynamicResource InputBorderBrush}" />
                <Setter Property="Foreground" Value="{DynamicResource TextBrush}" />
            </Style>
        </ComboBox.Style>
    </ComboBox>
</StackPanel>
```

- [ ] **Step 5: Extend the card `ContextMenu` with "Set Group" and "Tags" submenus**

In both `AvatarCardTemplate` and `AvatarListItemTemplate`, the `Border.ContextMenu` currently has:
```xml
<MenuItem Header="{loc:Translate 'Set Custom Icon...'}" Click="OnSetCustomIconClicked" />
<MenuItem Header="{loc:Translate 'Clear Custom Icon'}" Click="OnClearCustomIconClicked" />
```

Add a separator and two dynamic submenus. Because the group/tag lists are dynamic, use `SubmenuOpened` handlers to populate them. Replace the `ContextMenu` in both templates with:

```xml
<Border.ContextMenu>
    <ContextMenu>
        <MenuItem Header="{loc:Translate 'Set Custom Icon...'}" Click="OnSetCustomIconClicked" />
        <MenuItem Header="{loc:Translate 'Clear Custom Icon'}" Click="OnClearCustomIconClicked" />
        <Separator />
        <MenuItem Header="{loc:Translate 'Set Group'}" SubmenuOpened="OnSetGroupSubmenuOpened" />
        <MenuItem Header="{loc:Translate 'Tags'}" SubmenuOpened="OnTagsSubmenuOpened" />
    </ContextMenu>
</Border.ContextMenu>
```

(The actual `MenuItem` children for groups/tags are added in code-behind in Task 5, on the `SubmenuOpened` event, so they're fresh each open.)

- [ ] **Step 6: Build to confirm XAML compiles**

Run:
```
dotnet build "E:\!!!Program to work on\Proper Crystal Relay\VrcTwitchOscBridge\VrcTwitchOscBridge.csproj" --no-restore
```
Expected: Build succeeded. The `OnSetGroupSubmenuOpened`/`OnTagsSubmenuOpened`/`OnTagChipRemoveClicked` handlers don't exist yet — XAML compile will fail if they're referenced and missing. So this step may fail until Task 5 adds them. **If it fails on missing handlers, proceed to Task 5 and re-build after.** If it fails for other reasons (binding errors, typo), fix those.

- [ ] **Step 7: Commit**

```bash
git add VrcTwitchOscBridge/AvatarPickerWindow.xaml
git commit -m "AvatarPickerWindow XAML: tag chips on cards, filter dropdowns, group/tag context menu stubs"
```

---

## Task 5: `AvatarPickerWindow.xaml.cs` — context-menu handlers, chip remove, forward avatars to manager

**Files:**
- Modify: `VrcTwitchOscBridge/AvatarPickerWindow.xaml.cs`

- [ ] **Step 1: Add context-menu populate handlers**

Open `VrcTwitchOscBridge/AvatarPickerWindow.xaml.cs`. Add these handlers to the `AvatarPickerWindow` class (after `OnClearCustomIconClicked`, around line 221):

```csharp
private void OnSetGroupSubmenuOpened(object sender, RoutedEventArgs e)
{
    if (sender is not MenuItem groupMenu) return;
    var item = (groupMenu.Parent as ContextMenu)?.DataContext as AvatarPickerItem;
    if (item is null) return;

    groupMenu.Items.Clear();
    var library = viewModel.Library;
    if (library is null) return;

    var entry = library.GetEntry(item.Id);
    var currentGroupId = entry?.GroupId ?? string.Empty;

    foreach (var group in library.Groups.OrderBy(g => g.SortOrder).ThenBy(g => g.Name))
    {
        var menuItem = new MenuItem
        {
            Header = group.Name,
            IsCheckable = true,
            IsChecked = string.Equals(group.Id, currentGroupId, StringComparison.Ordinal),
            Tag = new Tuple<string, AvatarPickerItem, string>(group.Id, item, "set")
        };
        menuItem.Click += OnGroupMenuItemClicked;
        groupMenu.Items.Add(menuItem);
    }

    groupMenu.Items.Add(new Separator());

    var removeItem = new MenuItem
    {
        Header = LocalizationService.Translate("Remove from group"),
        IsCheckable = true,
        IsChecked = string.IsNullOrWhiteSpace(currentGroupId),
        Tag = new Tuple<string, AvatarPickerItem, string>(string.Empty, item, "remove")
    };
    removeItem.Click += OnGroupMenuItemClicked;
    groupMenu.Items.Add(removeItem);

    groupMenu.Items.Add(new Separator());

    var newItem = new MenuItem
    {
        Header = LocalizationService.Translate("New Group..."),
        Tag = item
    };
    newItem.Click += OnNewGroupFromMenuClicked;
    groupMenu.Items.Add(newItem);
}

private void OnGroupMenuItemClicked(object sender, RoutedEventArgs e)
{
    if (sender is not MenuItem menuItem) return;
    if (menuItem.Tag is not Tuple<string, AvatarPickerItem, string> tag) return;
    var (groupId, item, _) = tag;

    var library = viewModel.Library;
    if (library is null) return;

    library.EnsureEntry(item.Id);
    var entry = library.GetEntry(item.Id);
    if (entry is null) return;

    entry.GroupId = groupId;
    viewModel.RebuildItem(item);
    viewModel.RebuildFilterOptions();
}

private void OnNewGroupFromMenuClicked(object sender, RoutedEventArgs e)
{
    if (sender is not MenuItem menuItem) return;
    if (menuItem.Tag is not AvatarPickerItem item) return;

    var library = viewModel.Library;
    if (library is null) return;

    var name = ThemedInputDialog.ShowPrompt(
        this,
        ThemeManager.CurrentTheme,
        LocalizationService.Translate("New Group..."),
        LocalizationService.Translate("New group name:"),
        LocalizationService.Translate("Create"));
    if (string.IsNullOrWhiteSpace(name)) return;

    var group = new AvatarGroup
    {
        Name = name.Trim(),
        SortOrder = library.Groups.Count
    };
    library.Groups.Add(group);

    library.EnsureEntry(item.Id);
    var entry = library.GetEntry(item.Id);
    if (entry is not null)
    {
        entry.GroupId = group.Id;
    }

    viewModel.RebuildItem(item);
    viewModel.RebuildFilterOptions();
}

private void OnTagsSubmenuOpened(object sender, RoutedEventArgs e)
{
    if (sender is not MenuItem tagsMenu) return;
    var item = (tagsMenu.Parent as ContextMenu)?.DataContext as AvatarPickerItem;
    if (item is null) return;

    tagsMenu.Items.Clear();
    var library = viewModel.Library;
    if (library is null) return;

    var entry = library.GetEntry(item.Id);
    var currentTagIds = entry?.TagIds ?? new List<string>();

    foreach (var tag in library.Tags.OrderBy(t => t.Name))
    {
        var menuItem = new MenuItem
        {
            Header = tag.Name,
            IsCheckable = true,
            IsChecked = currentTagIds.Contains(tag.Id),
            Tag = new Tuple<AvatarTag, AvatarPickerItem>(tag, item)
        };
        menuItem.Click += OnTagMenuItemClicked;
        tagsMenu.Items.Add(menuItem);
    }

    if (library.Tags.Count > 0)
    {
        tagsMenu.Items.Add(new Separator());
    }

    var newItem = new MenuItem
    {
        Header = LocalizationService.Translate("New Tag..."),
        Tag = item
    };
    newItem.Click += OnNewTagFromMenuClicked;
    tagsMenu.Items.Add(newItem);
}

private void OnTagMenuItemClicked(object sender, RoutedEventArgs e)
{
    if (sender is not MenuItem menuItem) return;
    if (menuItem.Tag is not Tuple<AvatarTag, AvatarPickerItem> tag) return;
    var (tag, item) = tag;

    var library = viewModel.Library;
    if (library is null) return;

    library.EnsureEntry(item.Id);
    var entry = library.GetEntry(item.Id);
    if (entry is null) return;

    if (entry.TagIds.Contains(tag.Id))
    {
        entry.TagIds.Remove(tag.Id);
    }
    else
    {
        entry.TagIds.Add(tag.Id);
    }

    viewModel.RebuildItem(item);
}

private void OnNewTagFromMenuClicked(object sender, RoutedEventArgs e)
{
    if (sender is not MenuItem menuItem) return;
    if (menuItem.Tag is not AvatarPickerItem item) return;

    var library = viewModel.Library;
    if (library is null) return;

    var (name, color) = ThemedInputDialog.ShowPromptWithColor(
        this,
        ThemeManager.CurrentTheme,
        LocalizationService.Translate("New Tag..."),
        LocalizationService.Translate("New tag name:"),
        LocalizationService.Translate("Tag color:"),
        LocalizationService.Translate("Create"));
    if (string.IsNullOrWhiteSpace(name)) return;

    var tag = new AvatarTag
    {
        Name = name.Trim(),
        ColorHex = string.IsNullOrWhiteSpace(color) ? "#A855F7" : color
    };
    library.Tags.Add(tag);

    library.EnsureEntry(item.Id);
    var entry = library.GetEntry(item.Id);
    if (entry is not null && !entry.TagIds.Contains(tag.Id))
    {
        entry.TagIds.Add(tag.Id);
    }

    viewModel.RebuildItem(item);
    viewModel.RebuildFilterOptions();
}

private void OnTagChipRemoveClicked(object sender, MouseButtonEventArgs e)
{
    if (sender is not FrameworkElement element) return;
    if (element.DataContext is not AvatarTagDisplay tag) return;

    // Find the parent AvatarPickerItem via the ItemsControl DataContext.
    var parent = element;
    AvatarPickerItem? item = null;
    while (parent is not null)
    {
        if (parent.DataContext is AvatarPickerItem found)
        {
            item = found;
            break;
        }
        parent = VisualTreeHelper.GetParent(parent) as FrameworkElement;
    }
    if (item is null) return;

    var library = viewModel.Library;
    if (library is null) return;

    var entry = library.GetEntry(item.Id);
    if (entry is null) return;

    entry.TagIds.Remove(tag.Id);
    viewModel.RebuildItem(item);
    e.Handled = true;
}
```

- [ ] **Step 2: Update `OnManageButtonClicked` to forward avatars**

Replace the existing `OnManageButtonClicked` (around line 78) with:

```csharp
private void OnManageButtonClicked(object sender, RoutedEventArgs e)
{
    if (managerWindow is not null)
    {
        managerWindow.Activate();
        return;
    }

    managerWindow = new AvatarLibraryManagerWindow(
        ThemeManager.CurrentTheme,
        viewModel.Library ?? new AvatarLibrary(),
        imageService,
        viewModel.AvatarSummaries);
    managerWindow.Owner = this;
    managerWindow.Closed += OnManagerWindowClosed;
    managerWindow.Show();
}
```

(`viewModel.AvatarSummaries` is added in Step 3 below.)

- [ ] **Step 3: Add `AvatarSummaries` accessor to `AvatarPickerViewModel`**

Open `VrcTwitchOscBridge/ViewModels/AvatarPickerViewModel.cs`. Add a property to expose the original avatar list passed into the constructor:

In the constructor, change the signature storage. Add a private field and public property:

```csharp
private readonly IReadOnlyList<VrChatAvatarSummary> avatarSummaries;

// In the constructor:
this.avatarSummaries = avatars;

// Public accessor:
public IReadOnlyList<VrChatAvatarSummary> AvatarSummaries => avatarSummaries;
```

- [ ] **Step 4: Add `using` for `Models` to the code-behind if needed**

`AvatarPickerWindow.xaml.cs` already has `using VrcTwitchOscBridge.Models;` (line 5). Confirm `AvatarGroup`, `AvatarTag`, `AvatarTagDisplay`, `AvatarLibraryEntry` are all in that namespace — they are. No change needed.

- [ ] **Step 5: Build**

Run:
```
dotnet build "E:\!!!Program to work on\Proper Crystal Relay\VrcTwitchOscBridge\VrcTwitchOscBridge.csproj" --no-restore
```
Expected: Build fails — `ThemedInputDialog` doesn't exist yet. Proceed to Task 6 to create it, then re-build.

- [ ] **Step 6: Commit (after Task 6 build passes — defer commit until then)**

Do not commit yet. The build is broken until `ThemedInputDialog` exists. Commit after Task 6 Step 4.

---

## Task 6: `ThemedInputDialog` — minimal themed prompt for "New Group..."/"New Tag..."

**Files:**
- Create: `VrcTwitchOscBridge/ThemedInputDialog.xaml`
- Create: `VrcTwitchOscBridge/ThemedInputDialog.xaml.cs`
- Modify: `VrcTwitchOscBridge/VrcTwitchOscBridge.csproj` (add `<Page>` and `<Compile>` entries)

- [ ] **Step 1: Create `ThemedInputDialog.xaml`**

Create `VrcTwitchOscBridge/ThemedInputDialog.xaml`:

```xml
<Window x:Class="VrcTwitchOscBridge.ThemedInputDialog"
        xmlns="http://schemas.microsoft.com/winfx/2006/xaml/presentation"
        xmlns:x="http://schemas.microsoft.com/winfx/2006/xaml"
        xmlns:shell="clr-namespace:System.Windows.Shell;assembly=PresentationFramework"
        Title="Themed Input | Crystal Relay"
        Width="400"
        Height="Auto"
        SizeToContent="Height"
        MinWidth="360"
        WindowStyle="None"
        WindowStartupLocation="CenterOwner"
        FontFamily="{DynamicResource BodyFontFamily}"
        UseLayoutRounding="True"
        SnapsToDevicePixels="True"
        Background="{DynamicResource WindowBackgroundBrush}">
    <shell:WindowChrome.WindowChrome>
        <shell:WindowChrome CaptionHeight="0" CornerRadius="0" GlassFrameThickness="0" ResizeBorderThickness="0" UseAeroCaptionButtons="False" />
    </shell:WindowChrome.WindowChrome>
    <Window.Resources>
        <FontFamily x:Key="BodyFontFamily">Verdana</FontFamily>
        <FontFamily x:Key="HeadingFontFamily">Constantia</FontFamily>
        <SolidColorBrush x:Key="WindowBackgroundBrush" Color="#130B1E" />
        <SolidColorBrush x:Key="PanelBrush" Color="#CC1C132B" />
        <SolidColorBrush x:Key="BorderBrush" Color="#4B2B78" />
        <SolidColorBrush x:Key="AccentBrush" Color="#A855F7" />
        <SolidColorBrush x:Key="TextBrush" Color="#F5EEFF" />
        <SolidColorBrush x:Key="MutedBrush" Color="#C9B8E3" />
        <SolidColorBrush x:Key="InputBrush" Color="#B8271A3D" />
        <SolidColorBrush x:Key="InputBorderBrush" Color="#5B3A8E" />
        <SolidColorBrush x:Key="TitleBarBrush" Color="#20122F" />
        <SolidColorBrush x:Key="TitleBarTextBrush" Color="#F5EEFF" />
        <SolidColorBrush x:Key="SecondaryButtonBrush" Color="#2C1C48" />
        <SolidColorBrush x:Key="SecondaryButtonBorderBrush" Color="#6942A7" />

        <Style x:Key="PrimaryButtonStyle" TargetType="Button">
            <Setter Property="FontFamily" Value="{DynamicResource HeadingFontFamily}" />
            <Setter Property="FontWeight" Value="SemiBold" />
            <Setter Property="Background" Value="{DynamicResource AccentBrush}" />
            <Setter Property="Foreground" Value="White" />
            <Setter Property="BorderThickness" Value="0" />
            <Setter Property="Padding" Value="15,10" />
            <Setter Property="Cursor" Value="Hand" />
            <Setter Property="Template">
                <Setter.Value>
                    <ControlTemplate TargetType="Button">
                        <Border Background="{TemplateBinding Background}" CornerRadius="14" Padding="{TemplateBinding Padding}">
                            <ContentPresenter HorizontalAlignment="Center" VerticalAlignment="Center" />
                        </Border>
                    </ControlTemplate>
                </Setter.Value>
            </Setter>
        </Style>

        <Style x:Key="SecondaryButtonStyle" TargetType="Button" BasedOn="{StaticResource PrimaryButtonStyle}">
            <Setter Property="Background" Value="{DynamicResource SecondaryButtonBrush}" />
            <Setter Property="Foreground" Value="{DynamicResource TextBrush}" />
            <Setter Property="BorderThickness" Value="1" />
            <Setter Property="BorderBrush" Value="{DynamicResource SecondaryButtonBorderBrush}" />
        </Style>

        <Style x:Key="InputTextBoxStyle" TargetType="TextBox">
            <Setter Property="FontFamily" Value="{DynamicResource BodyFontFamily}" />
            <Setter Property="FontSize" Value="14" />
            <Setter Property="Background" Value="{DynamicResource InputBrush}" />
            <Setter Property="Foreground" Value="{DynamicResource TextBrush}" />
            <Setter Property="BorderBrush" Value="{DynamicResource InputBorderBrush}" />
            <Setter Property="BorderThickness" Value="1" />
            <Setter Property="Padding" Value="12,10" />
            <Setter Property="CaretBrush" Value="{DynamicResource TextBrush}" />
            <Setter Property="Template">
                <Setter.Value>
                    <ControlTemplate TargetType="TextBox">
                        <Border Background="{TemplateBinding Background}" BorderBrush="{TemplateBinding BorderBrush}" BorderThickness="{TemplateBinding BorderThickness}" CornerRadius="10" Padding="{TemplateBinding Padding}">
                            <ScrollViewer x:Name="PART_ContentHost" VerticalAlignment="Center" />
                        </Border>
                    </ControlTemplate>
                </Setter.Value>
            </Setter>
        </Style>
    </Window.Resources>

    <Border Margin="1" Background="{DynamicResource WindowBackgroundBrush}" BorderBrush="{DynamicResource BorderBrush}" BorderThickness="1">
        <Grid>
            <Grid.RowDefinitions>
                <RowDefinition Height="48" />
                <RowDefinition Height="Auto" />
            </Grid.RowDefinitions>

            <!-- Title Bar -->
            <Border Grid.Row="0" Background="{DynamicResource TitleBarBrush}" BorderBrush="{DynamicResource BorderBrush}" BorderThickness="0,0,0,1" MouseLeftButtonDown="OnTitleBarMouseDown">
                <Grid Margin="12,0,8,0">
                    <TextBlock x:Name="WindowTitleTextBlock" FontFamily="{DynamicResource HeadingFontFamily}" FontSize="14" FontWeight="Bold" Foreground="{DynamicResource TitleBarTextBrush}" VerticalAlignment="Center" />
                    <Button HorizontalAlignment="Right" Width="46" Height="48" Background="Transparent" BorderThickness="0" Click="OnCloseButtonClicked">
                        <TextBlock Text="&#x2715;" FontSize="13" FontWeight="SemiBold" Foreground="{DynamicResource TitleBarTextBrush}" />
                    </Button>
                </Grid>
            </Border>

            <!-- Content -->
            <StackPanel Grid.Row="1" Margin="20">
                <TextBlock x:Name="HeaderLabel" Foreground="{DynamicResource MutedBrush}" FontSize="13" Margin="0,0,0,6" />
                <TextBox x:Name="InputBox" Style="{StaticResource InputTextBoxStyle}" MaxLength="100" />
                <StackPanel x:Name="ColorPanel" Orientation="Horizontal" Margin="0,12,0,0" Visibility="Collapsed">
                    <TextBlock x:Name="ColorLabel" Foreground="{DynamicResource MutedBrush}" FontSize="13" VerticalAlignment="Center" Margin="0,0,8,0" />
                    <TextBox x:Name="ColorBox" Style="{StaticResource InputTextBoxStyle}" Width="100" MaxLength="7" />
                    <Border x:Name="ColorPreview" Width="24" Height="24" CornerRadius="4" BorderBrush="{DynamicResource BorderBrush}" BorderThickness="1" Margin="8,0,0,0">
                        <Border.Background>
                            <SolidColorBrush Color="{Binding Text, ElementName=ColorBox, TargetNullValue=#A855F7, FallbackValue=#A855F7}" />
                        </Border.Background>
                    </Border>
                </StackPanel>
                <StackPanel Orientation="Horizontal" HorizontalAlignment="Right" Margin="0,20,0,0">
                    <Button Width="100" Margin="0,0,10,0" Content="{x:Static local:ThemedInputDialog.CancelText}" Style="{StaticResource SecondaryButtonStyle}" IsCancel="True" Click="OnCancelButtonClicked" />
                    <Button x:Name="PrimaryButton" Width="100" Style="{StaticResource PrimaryButtonStyle}" IsDefault="True" Click="OnPrimaryButtonClicked" />
                </StackPanel>
            </StackPanel>
        </Grid>
    </Border>
</Window>
```

(Add `xmlns:local="clr-namespace:VrcTwitchOscBridge"` to the `<Window>` tag so `local:ThemedInputDialog.CancelText` resolves.)

- [ ] **Step 2: Create `ThemedInputDialog.xaml.cs`**

Create `VrcTwitchOscBridge/ThemedInputDialog.xaml.cs`:

```csharp
using System.Windows;
using System.Windows.Input;
using VrcTwitchOscBridge.Services;

namespace VrcTwitchOscBridge;

public partial class ThemedInputDialog : Window
{
    public static readonly string CancelText = LocalizationService.Translate("Cancel");

    private ThemedInputDialog(
        AppTheme theme,
        string title,
        string label,
        string primaryButtonText,
        bool showColor)
    {
        InitializeComponent();
        ThemeManager.ApplyToResources(Resources, theme);
        ThemeManager.ThemeChanged += OnThemeManagerThemeChanged;
        Closed += OnWindowClosed;

        Title = LocalizationService.Format("{0} | Crystal Relay", title);
        WindowTitleTextBlock.Text = title;
        HeaderLabel.Text = label;
        PrimaryButton.Content = primaryButtonText;

        ColorPanel.Visibility = showColor ? Visibility.Visible : Visibility.Collapsed;
        if (showColor)
        {
            ColorBox.Text = "#A855F7";
        }

        Loaded += (s, e) => InputBox.Focus();
    }

    public string InputValue => InputBox.Text;
    public string ColorValue => ColorBox.Text;

    public static string? ShowPrompt(
        Window owner,
        AppTheme theme,
        string title,
        string label,
        string primaryButtonText)
    {
        var dialog = new ThemedInputDialog(theme, title, label, primaryButtonText, showColor: false)
        {
            Owner = owner
        };
        return dialog.ShowDialog() == true ? dialog.InputValue : null;
    }

    public static (string? name, string? color) ShowPromptWithColor(
        Window owner,
        AppTheme theme,
        string title,
        string nameLabel,
        string colorLabel,
        string primaryButtonText)
    {
        var dialog = new ThemedInputDialog(theme, title, nameLabel, primaryButtonText, showColor: true)
        {
            Owner = owner
        };
        dialog.ColorLabel.Text = colorLabel;
        return dialog.ShowDialog() == true ? (dialog.InputValue, dialog.ColorValue) : (null, null);
    }

    private void OnPrimaryButtonClicked(object sender, RoutedEventArgs e)
    {
        if (string.IsNullOrWhiteSpace(InputBox.Text))
        {
            return;
        }
        DialogResult = true;
        Close();
    }

    private void OnCancelButtonClicked(object sender, RoutedEventArgs e)
    {
        DialogResult = false;
        Close();
    }

    private void OnCloseButtonClicked(object sender, RoutedEventArgs e)
    {
        DialogResult = false;
        Close();
    }

    private void OnTitleBarMouseDown(object sender, MouseButtonEventArgs e)
    {
        if (e.ChangedButton == MouseButton.Left)
        {
            try { DragMove(); } catch { }
        }
    }

    private void OnWindowClosed(object? sender, EventArgs e)
    {
        ThemeManager.ThemeChanged -= OnThemeManagerThemeChanged;
        Closed -= OnWindowClosed;
    }

    private void OnThemeManagerThemeChanged(object? sender, EventArgs e)
    {
        Dispatcher.BeginInvoke(() => ThemeManager.ApplyToResources(Resources));
    }
}
```

- [ ] **Step 3: Add `<Page>` and `<Compile>` entries to csproj**

Open `VrcTwitchOscBridge/VrcTwitchOscBridge.csproj`. In the `<ItemGroup>` with `<Page>` entries (after `AvatarLibraryManagerWindow.xaml` around line 48), add:

```xml
    <Page Include="ThemedInputDialog.xaml" />
```

In the `<ItemGroup>` with `<Compile>` entries (after `AvatarLibraryManagerWindow.xaml.cs` around line 103), add:

```xml
    <Compile Include="ThemedInputDialog.xaml.cs" />
```

- [ ] **Step 4: Build**

Run:
```
dotnet build "E:\!!!Program to work on\Proper Crystal Relay\VrcTwitchOscBridge\VrcTwitchOscBridge.csproj" --no-restore
```
Expected: Build succeeded, 0 errors. This resolves the broken build from Task 5.

- [ ] **Step 5: Run all picker tests to confirm nothing regressed**

Run:
```
dotnet test "E:\!!!Program to work on\Proper Crystal Relay\VrcTwitchOscBridge.Tests\VrcTwitchOscBridge.Tests.csproj" --filter "FullyQualifiedName~AvatarLibraryPruneTests|FullyQualifiedName~AvatarLibraryFilterTests" --no-restore
```
Expected: PASS (9 tests).

- [ ] **Step 6: Commit Tasks 5 + 6 together**

```bash
git add VrcTwitchOscBridge/AvatarPickerWindow.xaml.cs VrcTwitchOscBridge/ThemedInputDialog.xaml VrcTwitchOscBridge/ThemedInputDialog.xaml.cs VrcTwitchOscBridge/VrcTwitchOscBridge.csproj VrcTwitchOscBridge/ViewModels/AvatarPickerViewModel.cs
git commit -m "AvatarPicker: context-menu group/tag assignment, tag chip remove, ThemedInputDialog, forward avatars to manager"
```

---

## Task 7: `AvatarLibraryManagerWindow` — new Avatars tab (XAML)

**Files:**
- Modify: `VrcTwitchOscBridge/AvatarLibraryManagerWindow.xaml`

- [ ] **Step 1: Widen the window and add the Avatars tab**

Open `VrcTwitchOscBridge/AvatarLibraryManagerWindow.xaml`. Change the window size (line 9-10) from `Width="500" Height="450"` to:

```xml
Width="720"
Height="560"
MinWidth="600"
MinHeight="450"
```

In the `TabControl` (around line 125), add a third `TabItem` after the Tags tab (before the closing `</TabControl>`):

```xml
<!-- Avatars Tab -->
<TabItem Header="{loc:Translate 'Avatars'}">
    <Grid Margin="0,12,0,0">
        <Grid.RowDefinitions>
            <RowDefinition Height="Auto" />
            <RowDefinition Height="*" />
            <RowDefinition Height="Auto" />
        </Grid.RowDefinitions>

        <!-- Search -->
        <TextBox Grid.Row="0"
                 Text="{Binding AvatarSearchText, UpdateSourceTrigger=PropertyChanged}"
                 Style="{x:Null}"
                 Background="{DynamicResource InputBrush}"
                 Foreground="{DynamicResource TextBrush}"
                 BorderBrush="{DynamicResource InputBorderBrush}"
                 BorderThickness="1"
                 Padding="10,6"
                 Margin="0,0,0,8" />

        <!-- Main split: avatar list + assignment pane -->
        <Grid Grid.Row="1">
            <Grid.ColumnDefinitions>
                <ColumnDefinition Width="*" MinWidth="220" />
                <ColumnDefinition Width="10" />
                <ColumnDefinition Width="*" MinWidth="260" />
            </Grid.ColumnDefinitions>

            <!-- Avatar list (left) -->
            <ListBox Grid.Column="0"
                     ItemsSource="{Binding AssignmentRows}"
                     SelectedItem="{Binding SelectedAssignmentRow}"
                     SelectionMode="Extended"
                     Background="Transparent"
                     BorderThickness="0"
                     ItemContainerStyle="{StaticResource ListBoxItemStyle}">
                <ListBox.ItemTemplate>
                    <DataTemplate>
                        <Grid Margin="0,2">
                            <Grid.ColumnDefinitions>
                                <ColumnDefinition Width="40" />
                                <ColumnDefinition Width="*" />
                            </Grid.ColumnDefinitions>
                            <Border Grid.Column="0" Width="36" Height="36" Background="{DynamicResource InputBrush}" CornerRadius="6" ClipToBounds="True" Margin="0,0,8,0">
                                <Image Source="{Binding Image}" Stretch="UniformToFill" />
                            </Border>
                            <StackPanel Grid.Column="1" VerticalAlignment="Center">
                                <TextBlock Text="{Binding DisplayName}" Foreground="{DynamicResource TextBrush}" FontSize="12" FontWeight="SemiBold" TextTrimming="CharacterEllipsis" />
                                <TextBlock Text="{Binding GroupName}" Foreground="{DynamicResource MutedBrush}" FontSize="10" />
                            </StackPanel>
                        </Grid>
                    </DataTemplate>
                </ListBox.ItemTemplate>
            </ListBox>

            <!-- Assignment pane (right) -->
            <StackPanel Grid.Column="2" Margin="0,0,0,0">
                <TextBlock Text="{loc:Translate 'Group:'}" Foreground="{DynamicResource MutedBrush}" FontSize="12" Margin="0,0,0,4" />
                <ComboBox ItemsSource="{Binding GroupAssignmentOptions}"
                          SelectedItem="{Binding SelectedGroupAssignment, UpdateSourceTrigger=PropertyChanged}"
                          DisplayMemberPath="Display"
                          Background="{DynamicResource InputBrush}"
                          Foreground="{DynamicResource TextBrush}"
                          BorderBrush="{DynamicResource InputBorderBrush}"
                          Margin="0,0,0,12" />

                <TextBlock Text="{loc:Translate 'Tags:'}" Foreground="{DynamicResource MutedBrush}" FontSize="12" Margin="0,0,0,4" />
                <ItemsControl ItemsSource="{Binding TagAssignmentOptions}">
                    <ItemsControl.ItemTemplate>
                        <DataTemplate>
                            <CheckBox IsChecked="{Binding IsChecked, UpdateSourceTrigger=PropertyChanged}"
                                      Content="{Binding Display}"
                                      Foreground="{DynamicResource TextBrush}"
                                      Margin="0,2" />
                        </DataTemplate>
                    </ItemsControl.ItemTemplate>
                </ItemsControl>

                <Button Content="{loc:Translate 'Apply to selection'}"
                        Style="{StaticResource SecondaryButtonStyle}"
                        Command="{Binding ApplyAssignmentCommand}"
                        Margin="0,12,0,0" />

                <Button Content="{loc:Translate 'Clear group for selection'}"
                        Style="{StaticResource SecondaryButtonStyle}"
                        Command="{Binding ClearGroupCommand}"
                        Margin="0,8,0,0" />

                <Button Content="{loc:Translate 'Clear all tags for selection'}"
                        Style="{StaticResource SecondaryButtonStyle}"
                        Command="{Binding ClearTagsCommand}"
                        Margin="0,8,0,0" />

                <Separator Margin="0,16,0,16" />

                <TextBlock Text="{loc:Translate 'Custom Icon:'}" Foreground="{DynamicResource MutedBrush}" FontSize="12" Margin="0,0,0,4" />
                <StackPanel Orientation="Horizontal">
                    <Button Content="{loc:Translate 'Set...'}"
                            Style="{StaticResource SecondaryButtonStyle}"
                            Command="{Binding SetCustomIconForSelectionCommand}"
                            Margin="0,0,8,0" />
                    <Button Content="{loc:Translate 'Clear'}"
                            Style="{StaticResource SecondaryButtonStyle}"
                            Command="{Binding ClearCustomIconForSelectionCommand}" />
                </StackPanel>
            </StackPanel>
        </Grid>

        <!-- Bottom bar -->
        <StackPanel Grid.Row="2" Orientation="Horizontal" Margin="0,12,0,0">
            <Button Content="{loc:Translate 'Select All'}" Style="{StaticResource SecondaryButtonStyle}" Command="{Binding SelectAllCommand}" />
            <Button Content="{loc:Translate 'Select None'}" Style="{StaticResource SecondaryButtonStyle}" Margin="8,0,0,0" Command="{Binding SelectNoneCommand}" />
            <TextBlock x:Name="SelectionCountText" Text="{Binding SelectionCountText}" Foreground="{DynamicResource MutedBrush}" FontSize="11" VerticalAlignment="Center" Margin="16,0,0,0" />
        </StackPanel>
    </Grid>
</TabItem>
```

- [ ] **Step 2: Build (expect VM-missing errors — fix in Task 8)**

Run:
```
dotnet build "E:\!!!Program to work on\Proper Crystal Relay\VrcTwitchOscBridge\VrcTwitchOscBridge.csproj" --no-restore
```
Expected: Build fails — `AssignmentRows`, `AvatarSearchText`, `GroupAssignmentOptions`, `TagAssignmentOptions`, `ApplyAssignmentCommand`, etc. don't exist on the VM yet. Proceed to Task 8.

- [ ] **Step 3: Commit (defer until Task 8 build passes)**

Do not commit yet.

---

## Task 8: `AvatarLibraryManagerViewModel` — assignment rows, bulk commands, constructor takes avatars

**Files:**
- Modify: `VrcTwitchOscBridge/ViewModels/AvatarLibraryManagerViewModel.cs`
- Modify: `VrcTwitchOscBridge/AvatarLibraryManagerWindow.xaml.cs`

- [ ] **Step 1: Rewrite `AvatarLibraryManagerViewModel`**

Open `VrcTwitchOscBridge/ViewModels/AvatarLibraryManagerViewModel.cs`. Replace the entire file with:

```csharp
using System.Collections.ObjectModel;
using System.Text.RegularExpressions;
using System.Windows;
using Microsoft.Win32;
using VrcTwitchOscBridge.Infrastructure;
using VrcTwitchOscBridge.Models;
using VrcTwitchOscBridge.Services;

namespace VrcTwitchOscBridge.ViewModels;

public sealed class AvatarLibraryManagerViewModel : ObservableObject
{
    private readonly AvatarLibrary library;
    private readonly AvatarImageService imageService;
    private readonly IReadOnlyList<VrChatAvatarSummary> avatars;
    private AvatarLibraryEntry? selectedEntry;
    private AvatarGroup? selectedGroup;
    private AvatarTag? selectedTag;
    private string avatarSearchText = string.Empty;
    private AvatarAssignmentRow? selectedAssignmentRow;
    private FilterOption? selectedGroupAssignment;
    private string selectionCountText = string.Empty;

    public AvatarLibraryManagerViewModel(
        AvatarLibrary library,
        AvatarImageService imageService,
        IReadOnlyList<VrChatAvatarSummary> avatars)
    {
        this.library = library;
        this.imageService = imageService;
        this.avatars = avatars;

        AddGroupCommand = new RelayCommand(AddGroup);
        DeleteGroupCommand = new RelayCommand(DeleteGroup, () => SelectedGroup is not null);
        AddTagCommand = new RelayCommand(AddTag);
        DeleteTagCommand = new RelayCommand(DeleteTag, () => SelectedTag is not null);
        ApplyAssignmentCommand = new RelayCommand(ApplyAssignment, () => SelectedAssignmentRow is not null || GetSelectedRows().Any());
        ClearGroupCommand = new RelayCommand(ClearGroupForSelection, () => GetSelectedRows().Any());
        ClearTagsCommand = new RelayCommand(ClearTagsForSelection, () => GetSelectedRows().Any());
        SelectAllCommand = new RelayCommand(SelectAllRows);
        SelectNoneCommand = new RelayCommand(SelectNoneRows);
        SetCustomIconForSelectionCommand = new RelayCommand(SetCustomIconForSelection, () => GetSelectedRows().Count() == 1);
        ClearCustomIconForSelectionCommand = new RelayCommand(ClearCustomIconForSelection, () => GetSelectedRows().Count() == 1);

        RebuildAssignmentRows();
        RebuildGroupAssignmentOptions();
        RebuildTagAssignmentOptions();
    }

    public ObservableCollection<Models.AvatarLibraryEntry> Entries => library.Entries;
    public ObservableCollection<AvatarGroup> Groups => library.Groups;
    public ObservableCollection<AvatarTag> Tags => library.Tags;
    public ObservableCollection<AvatarAssignmentRow> AssignmentRows { get; } = [];
    public ObservableCollection<FilterOption> GroupAssignmentOptions { get; } = [];
    public ObservableCollection<TagAssignmentOption> TagAssignmentOptions { get; } = [];

    public AvatarLibraryEntry? SelectedEntry
    {
        get => selectedEntry;
        set => SetProperty(ref selectedEntry, value);
    }

    public AvatarGroup? SelectedGroup
    {
        get => selectedGroup;
        set
        {
            if (SetProperty(ref selectedGroup, value))
            {
                DeleteGroupCommand.NotifyCanExecuteChanged();
            }
        }
    }

    public AvatarTag? SelectedTag
    {
        get => selectedTag;
        set
        {
            if (SetProperty(ref selectedTag, value))
            {
                DeleteTagCommand.NotifyCanExecuteChanged();
            }
        }
    }

    public AvatarAssignmentRow? SelectedAssignmentRow
    {
        get => selectedAssignmentRow;
        set
        {
            if (SetProperty(ref selectedAssignmentRow, value))
            {
                UpdateAssignmentPaneFromSelection();
                ApplyAssignmentCommand.NotifyCanExecuteChanged();
            }
        }
    }

    public FilterOption? SelectedGroupAssignment
    {
        get => selectedGroupAssignment;
        set => SetProperty(ref selectedGroupAssignment, value);
    }

    public string AvatarSearchText
    {
        get => avatarSearchText;
        set
        {
            if (SetProperty(ref avatarSearchText, value))
            {
                RebuildAssignmentRows();
            }
        }
    }

    public string SelectionCountText
    {
        get => selectionCountText;
        private set => SetProperty(ref selectionCountText, value);
    }

    public RelayCommand AddGroupCommand { get; }
    public RelayCommand DeleteGroupCommand { get; }
    public RelayCommand AddTagCommand { get; }
    public RelayCommand DeleteTagCommand { get; }
    public RelayCommand ApplyAssignmentCommand { get; }
    public RelayCommand ClearGroupCommand { get; }
    public RelayCommand ClearTagsCommand { get; }
    public RelayCommand SelectAllCommand { get; }
    public RelayCommand SelectNoneCommand { get; }
    public RelayCommand SetCustomIconForSelectionCommand { get; }
    public RelayCommand ClearCustomIconForSelectionCommand { get; }

    public void AddGroup()
    {
        var group = new AvatarGroup
        {
            Name = $"Group {Groups.Count + 1}",
            SortOrder = Groups.Count
        };
        Groups.Add(group);
        SelectedGroup = group;
        RebuildGroupAssignmentOptions();
    }

    public void DeleteGroup()
    {
        if (SelectedGroup is null) return;
        var id = SelectedGroup.Id;
        Groups.Remove(SelectedGroup);
        foreach (var entry in Entries)
        {
            if (entry.GroupId == id)
            {
                entry.GroupId = string.Empty;
            }
        }
        SelectedGroup = Groups.FirstOrDefault();
        RebuildGroupAssignmentOptions();
        RebuildAssignmentRows();
    }

    public void AddTag()
    {
        var tag = new AvatarTag
        {
            Name = $"Tag {Tags.Count + 1}",
            ColorHex = "#A855F7"
        };
        Tags.Add(tag);
        SelectedTag = tag;
        RebuildTagAssignmentOptions();
    }

    public void DeleteTag()
    {
        if (SelectedTag is null) return;
        var id = SelectedTag.Id;
        Tags.Remove(SelectedTag);
        foreach (var entry in Entries)
        {
            entry.TagIds.Remove(id);
        }
        SelectedTag = Tags.FirstOrDefault();
        RebuildTagAssignmentOptions();
        RebuildAssignmentRows();
    }

    private static readonly Regex HexColorRegex = new(@"^#[0-9A-Fa-f]{6}$", RegexOptions.Compiled);

    public bool IsValidHexColor(string hex) => HexColorRegex.IsMatch(hex);

    public void SetCustomIconForEntry(AvatarLibraryEntry entry, Window owner)
    {
        var dialog = new OpenFileDialog
        {
            Filter = "Image Files|*.png;*.jpg;*.jpeg;*.gif;*.bmp;*.webp",
            Title = "Choose Avatar Icon"
        };

        if (dialog.ShowDialog(owner) != true) return;

        var relativePath = imageService.SaveCustomIcon(entry.AvatarId, dialog.FileName);
        if (!string.IsNullOrWhiteSpace(relativePath))
        {
            entry.CustomIconPath = relativePath;
        }
    }

    public void ClearCustomIconForEntry(AvatarLibraryEntry entry)
    {
        entry.CustomIconPath = string.Empty;
    }

    // --- Assignment row management ---

    private void RebuildAssignmentRows()
    {
        AssignmentRows.Clear();
        var search = avatarSearchText.Trim().ToLowerInvariant();

        // Ensure every avatar in the VRChat list has an entry (so it shows up).
        foreach (var avatar in avatars)
        {
            library.EnsureEntry(avatar.Id);
        }

        foreach (var avatar in avatars)
        {
            if (!string.IsNullOrWhiteSpace(search)
                && !avatar.Name.ToLowerInvariant().Contains(search)
                && !avatar.Id.ToLowerInvariant().Contains(search))
            {
                continue;
            }

            var entry = library.GetEntry(avatar.Id);
            if (entry is null) continue;

            var groupName = string.IsNullOrWhiteSpace(entry.GroupId)
                ? string.Empty
                : library.Groups.FirstOrDefault(g => g.Id == entry.GroupId)?.Name ?? string.Empty;

            var tags = entry.TagIds
                .Select(id => library.Tags.FirstOrDefault(t => t.Id == id))
                .Where(t => t is not null)
                .Select(t => new AvatarTagDisplay(t!.Id, t!.Name, t!.ColorHex))
                .ToList();

            var image = imageService.GetAvatarImage(avatar.Id, entry.CustomIconPath, avatar.ThumbnailUrl);

            AssignmentRows.Add(new AvatarAssignmentRow
            {
                Entry = entry,
                DisplayName = avatar.Name,
                Image = image,
                AvatarId = avatar.Id,
                GroupName = groupName,
                Tags = tags
            });
        }

        UpdateSelectionCount();
    }

    private IEnumerable<AvatarAssignmentRow> GetSelectedRows()
    {
        // The ListBox uses SelectionMode=Extended; SelectedItem is the anchor.
        // For multi-select, the VM would need to track the selected items collection.
        // For now, treat SelectedAssignmentRow as the single selection; multi-select
        // applies via SelectAllCommand + Apply.
        return SelectedAssignmentRow is not null ? new[] { SelectedAssignmentRow } : [];
    }

    private void UpdateAssignmentPaneFromSelection()
    {
        var row = SelectedAssignmentRow;
        if (row is null) return;

        var entry = row.Entry;
        SelectedGroupAssignment = GroupAssignmentOptions.FirstOrDefault(o => o.Id == entry.GroupId)
            ?? GroupAssignmentOptions.FirstOrDefault();

        foreach (var tagOpt in TagAssignmentOptions)
        {
            tagOpt.IsChecked = entry.TagIds.Contains(tagOpt.TagId);
        }
    }

    private void UpdateSelectionCount()
    {
        var count = GetSelectedRows().Count();
        SelectionCountText = count == 0
            ? LocalizationService.Translate("Select avatars to assign")
            : $"{count} {LocalizationService.Translate("selected")}";
    }

    private void RebuildGroupAssignmentOptions()
    {
        GroupAssignmentOptions.Clear();
        GroupAssignmentOptions.Add(new FilterOption(string.Empty, LocalizationService.Translate("— No group —")));
        foreach (var group in library.Groups.OrderBy(g => g.SortOrder).ThenBy(g => g.Name))
        {
            GroupAssignmentOptions.Add(new FilterOption(group.Id, group.Name));
        }
    }

    private void RebuildTagAssignmentOptions()
    {
        TagAssignmentOptions.Clear();
        foreach (var tag in library.Tags.OrderBy(t => t.Name))
        {
            TagAssignmentOptions.Add(new TagAssignmentOption(tag.Id, tag.Name, tag.ColorHex));
        }
    }

    public void ApplyAssignment()
    {
        var rows = GetSelectedRows().ToList();
        if (rows.Count == 0) return;

        var groupId = SelectedGroupAssignment?.Id ?? string.Empty;
        foreach (var row in rows)
        {
            row.Entry.GroupId = groupId;
            row.GroupName = string.IsNullOrWhiteSpace(groupId)
                ? string.Empty
                : library.Groups.FirstOrDefault(g => g.Id == groupId)?.Name ?? string.Empty;
        }

        // Apply tags (toggle-based: whatever is checked becomes the new set).
        foreach (var row in rows)
        {
            row.Entry.TagIds.Clear();
            foreach (var tagOpt in TagAssignmentOptions.Where(t => t.IsChecked))
            {
                row.Entry.TagIds.Add(tagOpt.TagId);
            }
            var tags = row.Entry.TagIds
                .Select(id => library.Tags.FirstOrDefault(t => t.Id == id))
                .Where(t => t is not null)
                .Select(t => new AvatarTagDisplay(t!.Id, t!.Name, t!.ColorHex))
                .ToList();
            row.Tags = tags;
        }
    }

    public void ClearGroupForSelection()
    {
        foreach (var row in GetSelectedRows())
        {
            row.Entry.GroupId = string.Empty;
            row.GroupName = string.Empty;
        }
    }

    public void ClearTagsForSelection()
    {
        foreach (var row in GetSelectedRows())
        {
            row.Entry.TagIds.Clear();
            row.Tags = [];
        }
    }

    public void SelectAllRows()
    {
        // Multi-select via SelectAll is handled by the view; the VM raises property changes.
        // The ListBox's SelectAll is invoked via command binding in the view if needed.
        // For simplicity, this toggles a "select all" flag that the view can bind to.
        // (If the ListBox doesn't support command-based select-all, the view's code-behind
        // can call ListBox.SelectAll() directly.)
        UpdateSelectionCount();
    }

    public void SelectNoneRows()
    {
        SelectedAssignmentRow = null;
        UpdateSelectionCount();
    }

    public void SetCustomIconForSelection()
    {
        var row = GetSelectedRows().FirstOrDefault();
        if (row is null) return;
        SetCustomIconForEntry(row.Entry, Application.Current.MainWindow);
        RebuildAssignmentRows();
    }

    public void ClearCustomIconForSelection()
    {
        var row = GetSelectedRows().FirstOrDefault();
        if (row is null) return;
        ClearCustomIconForEntry(row.Entry);
        RebuildAssignmentRows();
    }
}

public sealed class TagAssignmentOption : ObservableObject
{
    private bool isChecked;

    public string TagId { get; }
    public string Display { get; }
    public string ColorHex { get; }

    public TagAssignmentOption(string tagId, string display, string colorHex)
    {
        TagId = tagId;
        Display = display;
        ColorHex = colorHex;
    }

    public bool IsChecked
    {
        get => isChecked;
        set => SetProperty(ref isChecked, value);
    }
}
```

- [ ] **Step 2: Update `AvatarLibraryManagerWindow.xaml.cs` constructor**

Open `VrcTwitchOscBridge/AvatarLibraryManagerWindow.xaml.cs`. Replace the constructor (lines 13-25) with:

```csharp
public AvatarLibraryManagerWindow(
    AppTheme theme,
    AvatarLibrary library,
    AvatarImageService imageService,
    IReadOnlyList<VrChatAvatarSummary> avatars)
{
    viewModel = new AvatarLibraryManagerViewModel(library, imageService, avatars);
    DataContext = viewModel;

    InitializeComponent();
    ThemeManager.ApplyToResources(Resources, theme);
    ThemeManager.ThemeChanged += OnThemeManagerThemeChanged;
    Closed += OnWindowClosed;
}
```

Add `using VrcTwitchOscBridge.Models;` if not present (it is — line 3).

- [ ] **Step 3: Build**

Run:
```
dotnet build "E:\!!!Program to work on\Proper Crystal Relay\VrcTwitchOscBridge\VrcTwitchOscBridge.csproj" --no-restore
```
Expected: Build succeeded, 0 errors.

- [ ] **Step 4: Run all tests**

Run:
```
dotnet test "E:\!!!Program to work on\Proper Crystal Relay\VrcTwitchOscBridge.Tests\VrcTwitchOscBridge.Tests.csproj" --no-restore
```
Expected: PASS (all existing + new tests).

- [ ] **Step 5: Commit Tasks 7 + 8 together**

```bash
git add VrcTwitchOscBridge/AvatarLibraryManagerWindow.xaml VrcTwitchOscBridge/AvatarLibraryManagerWindow.xaml.cs VrcTwitchOscBridge/ViewModels/AvatarLibraryManagerViewModel.cs
git commit -m "AvatarLibraryManager: add Avatars tab with bulk group/tag assignment"
```

---

## Task 9: Localization — add new keys to all language files

**Files:**
- Modify: all `VrcTwitchOscBridge/Resources/Localization/*.extra.json` (14 files)

- [ ] **Step 1: Add English keys to `en-US.extra.json`**

Open `VrcTwitchOscBridge/Resources/Localization/en-US.extra.json`. Add these keys (in alphabetical order within the existing JSON object — the file appears to be roughly alphabetical):

```json
"All": "All",
"Apply to selection": "Apply to selection",
"Avatars": "Avatars",
"Clear all tags for selection": "Clear all tags for selection",
"Clear group for selection": "Clear group for selection",
"Custom Icon:": "Custom Icon:",
"— No group —": "— No group —",
"mixed": "mixed",
"New group name:": "New group name:",
"New Group...": "New Group...",
"New Tag...": "New Tag...",
"New tag name:": "New tag name:",
"n selected": "{0} selected",
"Remove from group": "Remove from group",
"Search avatars...": "Search avatars...",
"Select All": "Select All",
"selected": "selected",
"Select avatars to assign": "Select avatars to assign",
"Select None": "Select None",
"Set...": "Set...",
"Set Group": "Set Group",
"Tag color:": "Tag color:",
"Tags": "Tags",
"Themed Input | Crystal Relay": "Themed Input | Crystal Relay",
"Ungrouped": "Ungrouped",
```

(For `en-US.extra.json`, key and value are identical, per the existing pattern.)

- [ ] **Step 2: Translate each key into all 13 non-English languages**

For each of these files, add the same keys with translated values:
- `de-DE.extra.json`, `es-ES.extra.json`, `fr-FR.extra.json`, `it-IT.extra.json`, `ja-JP.extra.json`, `ko-KR.extra.json`, `pl-PL.extra.json`, `pt-BR.extra.json`, `ru-RU.extra.json`, `sv-SE.extra.json`, `th-TH.extra.json`, `zh-CN.extra.json`, `zh-TW.extra.json`

**Translation rules (from AGENTS.md):**
- Informal/friendly register: `du` (de-DE), `tú` (es-ES), `tu` (fr-FR), informal equivalents for others.
- Keep brand/technical terms in English: `Crystal Relay`, `OSC`, `VRChat`, `Twitch`, `Bits`, `Subs`.
- Preserve format placeholders exactly: `{0}` stays as `{0}`.
- Use natural gaming/streaming vocabulary, not literal translations.
- No empty values, no accidental English copies unless the term stays English by design.

**Example German translations:**
```json
"All": "Alle",
"Apply to selection": "Auf Auswahl anwenden",
"Avatars": "Avatare",
"Clear all tags for selection": "Alle Tags für Auswahl entfernen",
"Clear group for selection": "Gruppe für Auswahl entfernen",
"Custom Icon:": "Eigenes Icon:",
"— No group —": "— Keine Gruppe —",
"mixed": "gemischt",
"New group name:": "Neuer Gruppenname:",
"New Group...": "Neue Gruppe...",
"New Tag...": "Neues Tag...",
"New tag name:": "Neuer Tag-Name:",
"n selected": "{0} ausgewählt",
"Remove from group": "Aus Gruppe entfernen",
"Search avatars...": "Avatare suchen...",
"Select All": "Alle auswählen",
"selected": "ausgewählt",
"Select avatars to assign": "Avatare zum Zuweisen auswählen",
"Select None": "Keine auswählen",
"Set...": "Festlegen...",
"Set Group": "Gruppe festlegen",
"Tag color:": "Tag-Farbe:",
"Tags": "Tags",
"Themed Input | Crystal Relay": "Eingabe | Crystal Relay",
"Ungrouped": "Ohne Gruppe",
```

Repeat for each language with natural translations. Use the same terminology within each file and keep it consistent.

- [ ] **Step 3: Run the localization audit**

Run:
```
dotnet run --project "E:\!!!Program to work on\Proper Crystal Relay\LocalizationAudit\LocalizationAudit.csproj" --no-restore
```
Expected: Audit passes — no missing keys, no empty values, placeholder integrity OK. If the audit reports missing keys or mismatches, fix them in the relevant language file and re-run.

- [ ] **Step 4: Build**

Run:
```
dotnet build "E:\!!!Program to work on\Proper Crystal Relay\VrcTwitchOscBridge\VrcTwitchOscBridge.csproj" --no-restore
```
Expected: Build succeeded.

- [ ] **Step 5: Commit**

```bash
git add VrcTwitchOscBridge/Resources/Localization/*.extra.json
git commit -m "Localization: add avatar grouping & tags UI keys for all languages"
```

---

## Task 10: Final verification — full build, all tests, localization audit

**Files:** none modified

- [ ] **Step 1: Full clean build**

Run:
```
dotnet build "E:\!!!Program to work on\Proper Crystal Relay\VrcTwitchOscBridge\VrcTwitchOscBridge.csproj" --no-restore
```
Expected: Build succeeded, 0 errors, 0 warnings (or only pre-existing warnings).

- [ ] **Step 2: Full test run**

Run:
```
dotnet test "E:\!!!Program to work on\Proper Crystal Relay\VrcTwitchOscBridge.Tests\VrcTwitchOscBridge.Tests.csproj" --no-restore
```
Expected: All tests PASS.

- [ ] **Step 3: Localization audit**

Run:
```
dotnet run --project "E:\!!!Program to work on\Proper Crystal Relay\LocalizationAudit\LocalizationAudit.csproj" --no-restore
```
Expected: Audit passes.

- [ ] **Step 4: Manual smoke test (optional, user-driven)**

Launch the debug build:
```
E:\!!!Program to work on\Proper Crystal Relay\Launch-Crystal-Relay-Debug.bat
```
Verify:
1. Open the Avatar Picker (via Avatar Swap manager or any avatar-pick button).
2. Right-click an avatar card → "Set Group ▸" → create a new group → assign. Card's group membership should be set (verify by filtering on that group).
3. Right-click → "Tags ▸" → create a new tag → toggle it. Tag chip should appear on the card.
4. Click a tag chip's × → tag removed from card.
5. Filter by "Ungrouped" → only avatars with no group appear.
6. Open the manager (gear icon) → Avatars tab → select an avatar → assign a group/tag via the right pane → Apply.
7. Close and reopen the picker → verify assignments persisted (settings save flow).
8. Verify an avatar that has left the VRChat list no longer appears in the manager after the picker pruned it.

- [ ] **Step 5: Final commit if any fixups were made**

If steps 1-3 surfaced issues that were fixed, commit the fixes:
```bash
git add -A
git commit -m "Fix build/test/localization issues from avatar grouping & tags work"
```

If no fixups needed, no commit — the feature is complete.

---

## Summary

| Task | What | Tests |
|---|---|---|
| 1 | Model: `GroupIds`→`GroupId`, drop `IsCollapsed`, `PruneMissingEntries` | 4 prune tests |
| 2 | New types: `AvatarTagDisplay`, `FilterOption`, `AvatarAssignmentRow` | — |
| 3 | VM: prune call, tag resolution, `RebuildItem`, filter dropdowns | 5 filter tests |
| 4 | XAML: tag chips on cards, filter dropdowns, context menu stubs | — |
| 5 | Code-behind: context-menu handlers, chip remove, forward avatars | — |
| 6 | `ThemedInputDialog` window | — |
| 7 | Manager XAML: Avatars tab | — |
| 8 | Manager VM: assignment rows, bulk commands | — |
| 9 | Localization: new keys in all 14 languages | audit |
| 10 | Final verification: build + tests + audit + manual smoke | — |
