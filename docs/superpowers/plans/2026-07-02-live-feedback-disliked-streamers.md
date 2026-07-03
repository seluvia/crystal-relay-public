# Live Feedback Disliked Streamers + Favorite Star Visual State Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Add a "Disliked" classification + tab to the Crystal Relay Live Feedback dev tool so disliked streamers are routed to a separate section and excluded from notifications, and fix the favorite-star button so it visually reflects favorite state.

**Architecture:** A new `DislikedStore` mirrors the existing tested `FavoritesStore` (parallel JSON-persisted key set). `LiveUserViewModel` gains `INotifyPropertyChanged` with `IsFavorite`/`IsDisliked` props refreshed in-place after toggles. The card template gets `DataTrigger`-driven visual state for the star and a new dislike button. A third "Disliked" radio tab shows disliked live users only. `RefreshAsync` excludes disliked users from alerts/balloons/badge.

**Tech Stack:** C# (.NET 10), WPF + XAML, xUnit. Private dev tool under `tools/private/crystal-relay-live-list`.

**Spec:** `docs/superpowers/specs/2026-07-02-live-feedback-disliked-streamers-design.md`

---

## File Structure

New:
- `tools/private/crystal-relay-live-list/Services/DislikedStore.cs` — JSON-persisted disliked-key set, mirrors `FavoritesStore`.
- `tools/private/crystal-relay-live-list-tests/DislikedStoreTests.cs` — xUnit tests mirroring `FavoritesStoreTests`.

Edited:
- `tools/private/crystal-relay-live-list/ViewModels/LiveUserViewModel.cs` — add `INotifyPropertyChanged`, `IsFavorite`, `IsDisliked`, `RefreshClassification`.
- `tools/private/crystal-relay-live-list/MainWindow.xaml.cs` — wire `DislikedStore`, instance `BuildIncomingUsers`, new toggle/show handlers, mutual exclusion, disliked-aware filter + alerts, `isShowingDisliked` view state.
- `tools/private/crystal-relay-live-list/MainWindow.xaml` — favorite star `DataTrigger`s, new dislike button + 5th header column, third "Disliked" radio tab, disliked-view `ScrollViewer`+`ItemsControl`.

---

### Task 1: Create DislikedStore with failing tests

**Files:**
- Create: `tools/private/crystal-relay-live-list/Services/DislikedStore.cs`
- Create: `tools/private/crystal-relay-live-list-tests/DislikedStoreTests.cs`

- [ ] **Step 1: Write the failing tests**

Create `tools/private/crystal-relay-live-list-tests/DislikedStoreTests.cs` with this exact content:

```csharp
using System.IO;
using CrystalRelayLiveList.Services;
using Xunit;

namespace CrystalRelayLiveList.Tests;

public sealed class DislikedStoreTests
{
    [Fact]
    public void Toggle_AddsAndRemovesDisliked()
    {
        var dir = Path.Combine(Path.GetTempPath(), "crl-tests-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(dir);
        try
        {
            var path = Path.Combine(dir, "disliked.json");
            var store = new DislikedStore(path);

            Assert.True(store.Toggle("https://www.twitch.tv/a"));
            Assert.True(store.IsDisliked("https://www.twitch.tv/a"));
            Assert.False(store.Toggle("https://www.twitch.tv/a"));
            Assert.False(store.IsDisliked("https://www.twitch.tv/a"));
        }
        finally
        {
            Directory.Delete(dir, true);
        }
    }

    [Fact]
    public void IsDisliked_CaseInsensitive()
    {
        var dir = Path.Combine(Path.GetTempPath(), "crl-tests-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(dir);
        try
        {
            var path = Path.Combine(dir, "disliked.json");
            var store = new DislikedStore(path);
            store.Toggle("https://www.twitch.tv/casey");

            Assert.True(store.IsDisliked("https://www.twitch.tv/Casey"));
        }
        finally
        {
            Directory.Delete(dir, true);
        }
    }

    [Fact]
    public void Persisted_AcrossInstances()
    {
        var dir = Path.Combine(Path.GetTempPath(), "crl-tests-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(dir);
        try
        {
            var path = Path.Combine(dir, "disliked.json");
            new DislikedStore(path).Toggle("https://www.twitch.tv/a");

            Assert.True(new DislikedStore(path).IsDisliked("https://www.twitch.tv/a"));
        }
        finally
        {
            Directory.Delete(dir, true);
        }
    }
}
```

- [ ] **Step 2: Run tests to verify they fail**

Run:
```
dotnet test "E:\!!!Program to work on\Proper Crystal Relay\tools\private\crystal-relay-live-list-tests\CrystalRelayLiveList.Tests.csproj"
```
Expected: FAIL with "type or namespace 'DislikedStore' could not be found" (compile error).

- [ ] **Step 3: Implement DislikedStore**

Create `tools/private/crystal-relay-live-list/Services/DislikedStore.cs` with this exact content (mirrors `FavoritesStore.cs` with `IsDisliked` naming):

```csharp
using System.Collections.Generic;
using System.IO;
using System.Text.Json;

namespace CrystalRelayLiveList.Services;

public sealed class DislikedStore
{
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web)
    {
        PropertyNameCaseInsensitive = true
    };

    private readonly string path;
    private readonly HashSet<string> keys = new(StringComparer.OrdinalIgnoreCase);

    public DislikedStore(string path)
    {
        this.path = path;
        Load();
    }

    public IReadOnlyCollection<string> Keys => keys;

    public bool IsDisliked(string key) => keys.Contains(key);

    public bool Toggle(string key)
    {
        if (string.IsNullOrWhiteSpace(key))
        {
            return false;
        }
        if (!keys.Add(key))
        {
            keys.Remove(key);
            Save();
            return false;
        }
        Save();
        return true;
    }

    private void Load()
    {
        if (!File.Exists(path))
        {
            return;
        }
        try
        {
            var json = File.ReadAllText(path);
            var payload = JsonSerializer.Deserialize<DislikedPayload>(json, JsonOptions);
            keys.Clear();
            if (payload?.Keys is not null)
            {
                foreach (var k in payload.Keys)
                {
                    if (!string.IsNullOrWhiteSpace(k))
                    {
                        keys.Add(k);
                    }
                }
            }
        }
        catch
        {
            // ignore corrupt file
        }
    }

    private void Save()
    {
        try
        {
            var dir = Path.GetDirectoryName(path);
            if (!string.IsNullOrWhiteSpace(dir))
            {
                Directory.CreateDirectory(dir);
            }
            var payload = new DislikedPayload { Keys = keys.ToList() };
            var temp = path + ".tmp";
            File.WriteAllText(temp, JsonSerializer.Serialize(payload, new JsonSerializerOptions(JsonOptions) { WriteIndented = true }));
            if (File.Exists(path))
            {
                File.Replace(temp, path, null);
            }
            else
            {
                File.Move(temp, path);
            }
        }
        catch
        {
            // disliked list is a convenience; never throw
        }
    }

    private sealed class DislikedPayload
    {
        public List<string> Keys { get; set; } = new();
    }
}
```

- [ ] **Step 4: Run tests to verify they pass**

Run:
```
dotnet test "E:\!!!Program to work on\Proper Crystal Relay\tools\private\crystal-relay-live-list-tests\CrystalRelayLiveList.Tests.csproj"
```
Expected: PASS — 3 `DislikedStoreTests` tests pass, and the 3 existing `FavoritesStoreTests` still pass (6 total).

- [ ] **Step 5: Commit**

```
git add "tools/private/crystal-relay-live-list/Services/DislikedStore.cs" "tools/private/crystal-relay-live-list-tests/DislikedStoreTests.cs"
git commit -m "Add DislikedStore with JSON persistence and tests"
```

---

### Task 2: Add IsFavorite/IsDisliked to LiveUserViewModel

**Files:**
- Modify: `tools/private/crystal-relay-live-list/ViewModels/LiveUserViewModel.cs` (entire file — currently 46 lines)

- [ ] **Step 1: Replace LiveUserViewModel.cs with the INotifyPropertyChanged version**

Replace the entire contents of `tools/private/crystal-relay-live-list/ViewModels/LiveUserViewModel.cs` with:

```csharp
using System.ComponentModel;
using System.Runtime.CompilerServices;

namespace CrystalRelayLiveList.ViewModels;

public sealed class LiveUserViewModel : INotifyPropertyChanged
{
    private bool isFavorite;
    private bool isDisliked;

    public LiveUserViewModel(
        string displayName,
        string twitchUrl,
        string relayVersion,
        string buildChannel,
        DateTimeOffset? lastPingAt,
        bool isFavorite,
        bool isDisliked)
    {
        DisplayName = displayName.Trim();
        TwitchUrl = twitchUrl.Trim();
        RelayVersion = relayVersion.Trim();
        BuildChannel = buildChannel.Trim();
        LastPingAt = lastPingAt?.ToUniversalTime();
        VersionBadgeText = RelayVersion;
        ChannelBadgeText = BuildChannel;
        HasVersionBadge = !string.IsNullOrWhiteSpace(RelayVersion);
        HasChannelBadge = !string.IsNullOrWhiteSpace(BuildChannel);
        this.isFavorite = isFavorite;
        this.isDisliked = isDisliked;

        DetailText = LastPingAt is { } lastPing
            ? $"Last heartbeat {lastPing.ToLocalTime():g}"
            : "Live heartbeat active.";
    }

    public event PropertyChangedEventHandler? PropertyChanged;

    public string DisplayName { get; }

    public string TwitchUrl { get; }

    public string RelayVersion { get; }

    public string BuildChannel { get; }

    public DateTimeOffset? LastPingAt { get; }

    public string DetailText { get; }

    public string VersionBadgeText { get; }

    public string ChannelBadgeText { get; }

    public bool HasVersionBadge { get; }

    public bool HasChannelBadge { get; }

    public bool IsFavorite
    {
        get => isFavorite;
        private set
        {
            if (isFavorite != value)
            {
                isFavorite = value;
                OnPropertyChanged();
            }
        }
    }

    public bool IsDisliked
    {
        get => isDisliked;
        private set
        {
            if (isDisliked != value)
            {
                isDisliked = value;
                OnPropertyChanged();
            }
        }
    }

    public void RefreshClassification(bool favorite, bool disliked)
    {
        IsFavorite = favorite;
        IsDisliked = disliked;
    }

    private void OnPropertyChanged([CallerMemberName] string? propertyName = null)
    {
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
    }
}
```

- [ ] **Step 2: Verify the test project still compiles**

Run:
```
dotnet build "E:\!!!Program to work on\Proper Crystal Relay\tools\private\crystal-relay-live-list-tests\CrystalRelayLiveList.Tests.csproj"
```
Expected: The test project compiles (tests don't construct `LiveUserViewModel` directly, so no test breakage). The main app project (`CrystalRelayLiveList.csproj`) will NOT compile yet because `BuildIncomingUsers` still uses the old 5-arg constructor — that is fixed in Task 3. Do not run the main app build yet.

- [ ] **Step 3: Commit**

```
git add "tools/private/crystal-relay-live-list/ViewModels/LiveUserViewModel.cs"
git commit -m "Add IsFavorite/IsDisliked with change notification to LiveUserViewModel"
```

---

### Task 3: Wire DislikedStore into MainWindow code-behind

**Files:**
- Modify: `tools/private/crystal-relay-live-list/MainWindow.xaml.cs`

This task adds the `disliked` field, initializes it, makes `BuildIncomingUsers` an instance method that passes classification flags, updates the toggle handler for mutual exclusion, and adds `OnToggleDislikedClicked`. The app must compile after this task.

- [ ] **Step 1: Add the disliked field**

In `tools/private/crystal-relay-live-list/MainWindow.xaml.cs`, find line 57:

```csharp
    private FavoritesStore? favorites;
```

Replace with:

```csharp
    private FavoritesStore? favorites;
    private DislikedStore? disliked;
```

- [ ] **Step 2: Initialize disliked in InitializeServices**

Find lines 343-344 (the `favorites = new FavoritesStore(...)` and `devCommands = ...` lines):

```csharp
        favorites = new FavoritesStore(Path.Combine(dataRoot, "favorites.json"));
        devCommands = new DevCommandService(Path.Combine(dataRoot, "command-presets.json"));
```

Replace with:

```csharp
        favorites = new FavoritesStore(Path.Combine(dataRoot, "favorites.json"));
        disliked = new DislikedStore(Path.Combine(dataRoot, "disliked.json"));
        devCommands = new DevCommandService(Path.Combine(dataRoot, "command-presets.json"));
```

- [ ] **Step 3: Make BuildIncomingUsers an instance method that sets classification**

Find line 487 — the static method signature:

```csharp
    private static List<LiveUserViewModel> BuildIncomingUsers(LiveListResponse? payload)
```

Replace the entire method (lines 487-504) with:

```csharp
    private List<LiveUserViewModel> BuildIncomingUsers(LiveListResponse? payload)
    {
        var result = new List<LiveUserViewModel>();
        foreach (var user in payload?.Users ?? [])
        {
            if (string.IsNullOrWhiteSpace(user.DisplayName) || string.IsNullOrWhiteSpace(user.TwitchUrl))
            {
                continue;
            }
            var key = LiveUserKey.Normalize(user.TwitchUrl, user.DisplayName);
            var isFavorite = favorites is not null && favorites.IsFavorite(key);
            var isDisliked = disliked is not null && disliked.IsDisliked(key);
            result.Add(new LiveUserViewModel(
                user.DisplayName,
                user.TwitchUrl,
                user.RelayVersion,
                user.BuildChannel,
                user.LastPingAt,
                isFavorite,
                isDisliked));
        }
        return result;
    }
```

- [ ] **Step 4: Add OnToggleDislikedClicked and update OnToggleFavoriteClicked with mutual exclusion + in-place VM refresh**

Find lines 780-787 — the existing `OnToggleFavoriteClicked`:

```csharp
    private void OnToggleFavoriteClicked(object sender, RoutedEventArgs e)
    {
        if (sender is not Button { Tag: string twitchUrl } || favorites is null)
            return;
        var key = LiveUserKey.Normalize(twitchUrl, null);
        favorites.Toggle(key);
        ApplySearchFilter();
    }
```

Replace with:

```csharp
    private void OnToggleFavoriteClicked(object sender, RoutedEventArgs e)
    {
        if (sender is not Button { Tag: string twitchUrl } || favorites is null)
            return;
        var key = LiveUserKey.Normalize(twitchUrl, null);
        var nowFavorite = favorites.Toggle(key);
        if (nowFavorite && disliked is not null && disliked.IsDisliked(key))
        {
            disliked.Toggle(key);
        }
        RefreshUserClassification(key);
        ApplySearchFilter();
    }

    private void OnToggleDislikedClicked(object sender, RoutedEventArgs e)
    {
        if (sender is not Button { Tag: string twitchUrl } || disliked is null)
            return;
        var key = LiveUserKey.Normalize(twitchUrl, null);
        var nowDisliked = disliked.Toggle(key);
        if (nowDisliked && favorites is not null && favorites.IsFavorite(key))
        {
            favorites.Toggle(key);
        }
        RefreshUserClassification(key);
        ApplySearchFilter();
    }

    private void RefreshUserClassification(string key)
    {
        if (string.IsNullOrWhiteSpace(key)) return;
        var isFavorite = favorites is not null && favorites.IsFavorite(key);
        var isDisliked = disliked is not null && disliked.IsDisliked(key);
        foreach (var u in Users)
        {
            if (string.Equals(LiveUserKey.Normalize(u.TwitchUrl, u.DisplayName), key, StringComparison.OrdinalIgnoreCase))
            {
                u.RefreshClassification(isFavorite, isDisliked);
            }
        }
    }
```

- [ ] **Step 5: Build the app to verify it compiles**

Run:
```
dotnet build "E:\!!!Program to work on\Proper Crystal Relay\tools\private\crystal-relay-live-list\CrystalRelayLiveList.csproj" --no-restore
```
Expected: Build succeeds (the `OnToggleDislikedClicked` handler is referenced by XAML which doesn't exist yet, but XAML handlers are resolved at XAML compile time — since we haven't added the XAML button yet, the handler is simply unused code and the build passes). If the build fails because of an unresolved XAML reference to `OnToggleDislikedClicked`, that means XAML was edited out of order; proceed to Task 5 to add the XAML button.

- [ ] **Step 6: Run tests to confirm no regression**

Run:
```
dotnet test "E:\!!!Program to work on\Proper Crystal Relay\tools\private\crystal-relay-live-list-tests\CrystalRelayLiveList.Tests.csproj"
```
Expected: PASS — all 6 tests still pass.

- [ ] **Step 7: Commit**

```
git add "tools/private/crystal-relay-live-list/MainWindow.xaml.cs"
git commit -m "Wire DislikedStore, instance BuildIncomingUsers, dislike toggle with mutual exclusion"
```

---

### Task 4: Add disliked-aware filtering and notification exclusion

**Files:**
- Modify: `tools/private/crystal-relay-live-list/MainWindow.xaml.cs`

- [ ] **Step 1: Exclude disliked users from the main live filter**

Find lines 539-550 — the `FilterUser` method:

```csharp
    private bool FilterUser(object item)
    {
        if (item is not LiveUserViewModel user) return false;
        if (favoritesOnly && favorites is not null
            && !favorites.IsFavorite(LiveUserKey.Normalize(user.TwitchUrl, user.DisplayName)))
        {
            return false;
        }
        if (string.IsNullOrWhiteSpace(searchText)) return true;
        return user.DisplayName.Contains(searchText, StringComparison.OrdinalIgnoreCase)
            || user.TwitchUrl.Contains(searchText, StringComparison.OrdinalIgnoreCase);
    }
```

Replace with:

```csharp
    private bool FilterUser(object item)
    {
        if (item is not LiveUserViewModel user) return false;
        var key = LiveUserKey.Normalize(user.TwitchUrl, user.DisplayName);
        if (disliked is not null && disliked.IsDisliked(key))
        {
            return false;
        }
        if (favoritesOnly && favorites is not null
            && !favorites.IsFavorite(key))
        {
            return false;
        }
        if (string.IsNullOrWhiteSpace(searchText)) return true;
        return user.DisplayName.Contains(searchText, StringComparison.OrdinalIgnoreCase)
            || user.TwitchUrl.Contains(searchText, StringComparison.OrdinalIgnoreCase);
    }

    private bool FilterDislikedUser(object item)
    {
        if (item is not LiveUserViewModel user) return false;
        if (disliked is null) return false;
        if (!disliked.IsDisliked(LiveUserKey.Normalize(user.TwitchUrl, user.DisplayName)))
        {
            return false;
        }
        if (string.IsNullOrWhiteSpace(searchText)) return true;
        return user.DisplayName.Contains(searchText, StringComparison.OrdinalIgnoreCase)
            || user.TwitchUrl.Contains(searchText, StringComparison.OrdinalIgnoreCase);
    }
```

- [ ] **Step 2: Apply the disliked filter in ApplySearchFilter**

Find lines 529-537 — `ApplySearchFilter`:

```csharp
    private void ApplySearchFilter()
    {
        var liveView = CollectionViewSource.GetDefaultView(Users);
        liveView.Filter = FilterUser;
        liveView.Refresh();
        var historyView = CollectionViewSource.GetDefaultView(HistoryEntries);
        historyView.Filter = FilterHistoryEntry;
        historyView.Refresh();
    }
```

Replace with:

```csharp
    private void ApplySearchFilter()
    {
        var liveView = CollectionViewSource.GetDefaultView(Users);
        liveView.Filter = isShowingDisliked ? FilterDislikedUser : FilterUser;
        liveView.Refresh();
        var historyView = CollectionViewSource.GetDefaultView(HistoryEntries);
        historyView.Filter = FilterHistoryEntry;
        historyView.Refresh();
    }
```

- [ ] **Step 3: Exclude disliked users from alerts and tray balloons in RefreshAsync**

Find lines 399-406 — the `shouldAlert` and `newFavoriteNames` block:

```csharp
            var shouldAlert = hasLoadedLiveSnapshot && incomingKeys.Any(k => !knownLiveUserKeys.Contains(k));
            var newFavoriteNames = hasLoadedLiveSnapshot
                ? incoming.Where(u => favorites is not null
                    && favorites.IsFavorite(LiveUserKey.Normalize(u.TwitchUrl, u.DisplayName))
                    && !knownLiveUserKeys.Contains(LiveUserKey.Normalize(u.TwitchUrl, u.DisplayName)))
                    .Select(u => u.DisplayName)
                    .ToList()
                : new List<string>();
```

Replace with:

```csharp
            var dislikedKeys = disliked is null
                ? new HashSet<string>(StringComparer.OrdinalIgnoreCase)
                : disliked.Keys.ToHashSet(StringComparer.OrdinalIgnoreCase);
            var alertableIncomingKeys = incomingKeys.Except(dislikedKeys).ToHashSet(StringComparer.OrdinalIgnoreCase);
            var shouldAlert = hasLoadedLiveSnapshot && alertableIncomingKeys.Any(k => !knownLiveUserKeys.Contains(k));
            var newFavoriteNames = hasLoadedLiveSnapshot
                ? incoming.Where(u => favorites is not null
                    && favorites.IsFavorite(LiveUserKey.Normalize(u.TwitchUrl, u.DisplayName))
                    && !dislikedKeys.Contains(LiveUserKey.Normalize(u.TwitchUrl, u.DisplayName))
                    && !knownLiveUserKeys.Contains(LiveUserKey.Normalize(u.TwitchUrl, u.DisplayName)))
                    .Select(u => u.DisplayName)
                    .ToList()
                : new List<string>();
```

- [ ] **Step 4: Use alertable count for the unread badge and tray balloon**

Find lines 443-457 — the `if (shouldAlert)` block:

```csharp
            if (shouldAlert)
            {
                var newlyLiveCount = incomingKeys.Count(k => !knownLiveUserKeys.Contains(k));
                unreadLiveCount += newlyLiveCount;
                UpdateUnreadBadge();
                if (SoundAlertsEnabled)
                    PlayLiveSoundAlert();
                tray?.ShowBalloon("Crystal Relay live", Users.Count == 1
                    ? $"{Users[0].DisplayName} is live."
                    : $"{Users.Count} Crystal Relay users are live.");
                foreach (var name in newFavoriteNames)
                {
                    tray?.ShowBalloon("Favorite live", $"{name} just went live.");
                }
            }
```

Replace with:

```csharp
            if (shouldAlert)
            {
                var newlyLiveCount = alertableIncomingKeys.Count(k => !knownLiveUserKeys.Contains(k));
                unreadLiveCount += newlyLiveCount;
                UpdateUnreadBadge();
                if (SoundAlertsEnabled)
                    PlayLiveSoundAlert();
                var alertableUsers = Users.Where(u => !dislikedKeys.Contains(LiveUserKey.Normalize(u.TwitchUrl, u.DisplayName))).ToList();
                tray?.ShowBalloon("Crystal Relay live", alertableUsers.Count == 1
                    ? $"{alertableUsers[0].DisplayName} is live."
                    : $"{alertableUsers.Count} Crystal Relay users are live.");
                foreach (var name in newFavoriteNames)
                {
                    tray?.ShowBalloon("Favorite live", $"{name} just went live.");
                }
            }
```

- [ ] **Step 5: Build to verify it compiles**

Run:
```
dotnet build "E:\!!!Program to work on\Proper Crystal Relay\tools\private\crystal-relay-live-list\CrystalRelayLiveList.csproj" --no-restore
```
Expected: Build succeeds. (`isShowingDisliked` is referenced in `ApplySearchFilter` but not yet declared — it will be added in Task 5 along with the view-state plumbing. If the build fails on `isShowingDisliked`, add the field now: see Task 5 Step 1 for the field declaration, then return here. To keep tasks independent, declare the field here:)

Before building, add the `isShowingDisliked` field. Find line 82:

```csharp
    private bool favoritesOnly;
```

Add after it:

```csharp
    private bool favoritesOnly;
    private bool isShowingDisliked;
```

Now build.

- [ ] **Step 6: Run tests**

Run:
```
dotnet test "E:\!!!Program to work on\Proper Crystal Relay\tools\private\crystal-relay-live-list-tests\CrystalRelayLiveList.Tests.csproj"
```
Expected: PASS — all 6 tests.

- [ ] **Step 7: Commit**

```
git add "tools/private/crystal-relay-live-list/MainWindow.xaml.cs"
git commit -m "Exclude disliked users from main list, alerts, tray balloons, and unread badge"
```

---

### Task 5: Add Disliked view state and navigation handlers

**Files:**
- Modify: `tools/private/crystal-relay-live-list/MainWindow.xaml.cs`

- [ ] **Step 1: Add the isShowingDisliked field (if not already added in Task 4)**

If Task 4 Step 5 already added `private bool isShowingDisliked;` after `private bool favoritesOnly;`, skip this step. Otherwise, find line 82:

```csharp
    private bool favoritesOnly;
```

Add after it:

```csharp
    private bool favoritesOnly;
    private bool isShowingDisliked;
```

- [ ] **Step 2: Add IsDislikedViewVisible and IsDislikedEmptyVisible computed properties**

Find lines 298-308 — the view-mode computed properties block:

```csharp
    public bool IsLiveViewVisible => !isShowingHistory && !isShowingStream;

    public bool IsHistoryViewVisible => isShowingHistory && !isShowingStream;

    public bool IsStreamViewVisible => isShowingStream;

    public bool IsDecorativeBackdropVisible => !isShowingStream;

    public bool IsLiveEmptyVisible => IsLiveViewVisible && Users.Count == 0;

    public bool IsHistoryEmptyVisible => IsHistoryViewVisible && HistoryEntries.Count == 0;
```

Replace with:

```csharp
    public bool IsLiveViewVisible => !isShowingHistory && !isShowingStream && !isShowingDisliked;

    public bool IsHistoryViewVisible => isShowingHistory && !isShowingStream;

    public bool IsDislikedViewVisible => isShowingDisliked && !isShowingStream;

    public bool IsStreamViewVisible => isShowingStream;

    public bool IsDecorativeBackdropVisible => !isShowingStream;

    public bool IsLiveEmptyVisible => IsLiveViewVisible && Users.Count == 0;

    public bool IsHistoryEmptyVisible => IsHistoryViewVisible && HistoryEntries.Count == 0;

    public bool IsDislikedEmptyVisible => IsDislikedViewVisible && Users.All(u => !u.IsDisliked);
```

- [ ] **Step 3: Raise IsDislikedViewVisible in RaiseViewModePropertiesChanged**

Find lines 1195-1206 — `RaiseViewModePropertiesChanged`:

```csharp
    private void RaiseViewModePropertiesChanged()
    {
        RaisePropertyChanged(nameof(IsLiveViewVisible));
        RaisePropertyChanged(nameof(IsHistoryViewVisible));
        RaisePropertyChanged(nameof(IsStreamViewVisible));
        RaisePropertyChanged(nameof(IsDecorativeBackdropVisible));
        RaisePropertyChanged(nameof(IsLiveEmptyVisible));
        RaisePropertyChanged(nameof(IsHistoryEmptyVisible));
        RaisePropertyChanged(nameof(ViewTitleText));
        RaisePropertyChanged(nameof(ViewPrimaryStatusText));
        RaisePropertyChanged(nameof(ViewSecondaryStatusText));
    }
```

Replace with:

```csharp
    private void RaiseViewModePropertiesChanged()
    {
        RaisePropertyChanged(nameof(IsLiveViewVisible));
        RaisePropertyChanged(nameof(IsHistoryViewVisible));
        RaisePropertyChanged(nameof(IsDislikedViewVisible));
        RaisePropertyChanged(nameof(IsStreamViewVisible));
        RaisePropertyChanged(nameof(IsDecorativeBackdropVisible));
        RaisePropertyChanged(nameof(IsLiveEmptyVisible));
        RaisePropertyChanged(nameof(IsHistoryEmptyVisible));
        RaisePropertyChanged(nameof(IsDislikedEmptyVisible));
        RaisePropertyChanged(nameof(ViewTitleText));
        RaisePropertyChanged(nameof(ViewPrimaryStatusText));
        RaisePropertyChanged(nameof(ViewSecondaryStatusText));
    }
```

- [ ] **Step 4: Update ViewTitleText/ViewPrimaryStatusText for the disliked view**

Find lines 310-320:

```csharp
    public string ViewTitleText => isShowingStream
        ? StreamViewerTitleText
        : isShowingHistory ? "24h Live History" : "Live Crystal Relay Users";

    public string ViewPrimaryStatusText => isShowingStream
        ? StreamViewerStatusText
        : isShowingHistory ? HistoryStatusText : StatusText;

    public string ViewSecondaryStatusText => isShowingStream
        ? CurrentStreamTwitchUrl
        : isShowingHistory ? "Saved locally in AppData." : LastUpdatedText;
```

Replace with:

```csharp
    public string ViewTitleText => isShowingStream
        ? StreamViewerTitleText
        : isShowingHistory ? "24h Live History"
        : isShowingDisliked ? "Disliked Crystal Relay Users" : "Live Crystal Relay Users";

    public string ViewPrimaryStatusText => isShowingStream
        ? StreamViewerStatusText
        : isShowingHistory ? HistoryStatusText
        : isShowingDisliked ? DislikedStatusText : StatusText;

    public string ViewSecondaryStatusText => isShowingStream
        ? CurrentStreamTwitchUrl
        : isShowingHistory ? "Saved locally in AppData." : LastUpdatedText;
```

- [ ] **Step 5: Add the DislikedStatusText property**

Find lines 62-63 — the `historyStatusText` field and its property:

```csharp
    private string historyStatusText = "History covers live users observed by this tool in the last 24 hours.";
```

Add a new field after it:

```csharp
    private string historyStatusText = "History covers live users observed by this tool in the last 24 hours.";
    private string dislikedStatusText = "Streamers you've marked disliked. They won't trigger notifications.";
```

Then find the `HistoryStatusText` property (lines 160-168):

```csharp
    public string HistoryStatusText
    {
        get => historyStatusText;
        private set
        {
            if (SetProperty(ref historyStatusText, value))
                RaisePropertyChanged(nameof(ViewPrimaryStatusText));
        }
    }
```

Add a new property after it:

```csharp
    public string HistoryStatusText
    {
        get => historyStatusText;
        private set
        {
            if (SetProperty(ref historyStatusText, value))
                RaisePropertyChanged(nameof(ViewPrimaryStatusText));
        }
    }

    public string DislikedStatusText
    {
        get => dislikedStatusText;
        private set
        {
            if (SetProperty(ref dislikedStatusText, value))
                RaisePropertyChanged(nameof(ViewPrimaryStatusText));
        }
    }
```

- [ ] **Step 6: Update SetLiveListView and SetHistoryView to reset isShowingDisliked**

Find lines 735-757 — `SetLiveListView` and `SetHistoryView`:

```csharp
    private void SetLiveListView()
    {
        if (!isShowingHistory && !isShowingStream)
            return;
        if (isShowingStream)
            StopStreamViewer();
        isShowingHistory = false;
        isShowingStream = false;
        RaiseViewModePropertiesChanged();
        UpdateStoryboardState();
    }

    private void SetHistoryView(bool showHistory)
    {
        if (isShowingHistory == showHistory && !isShowingStream)
            return;
        if (isShowingStream)
            StopStreamViewer();
        isShowingHistory = showHistory;
        isShowingStream = false;
        RaiseViewModePropertiesChanged();
        UpdateStoryboardState();
    }
```

Replace with:

```csharp
    private void SetLiveListView()
    {
        if (!isShowingHistory && !isShowingStream && !isShowingDisliked)
            return;
        if (isShowingStream)
            StopStreamViewer();
        isShowingHistory = false;
        isShowingStream = false;
        isShowingDisliked = false;
        RaiseViewModePropertiesChanged();
        UpdateStoryboardState();
        ApplySearchFilter();
    }

    private void SetHistoryView(bool showHistory)
    {
        if (isShowingHistory == showHistory && !isShowingStream && !isShowingDisliked)
            return;
        if (isShowingStream)
            StopStreamViewer();
        isShowingHistory = showHistory;
        isShowingStream = false;
        isShowingDisliked = false;
        RaiseViewModePropertiesChanged();
        UpdateStoryboardState();
    }

    private void SetDislikedView()
    {
        if (isShowingDisliked && !isShowingStream)
            return;
        if (isShowingStream)
            StopStreamViewer();
        isShowingHistory = false;
        isShowingStream = false;
        isShowingDisliked = true;
        RaiseViewModePropertiesChanged();
        UpdateStoryboardState();
        ApplySearchFilter();
    }

    private void OnShowDislikedClicked(object sender, RoutedEventArgs e)
    {
        SetDislikedView();
    }
```

- [ ] **Step 7: Build to verify it compiles**

Run:
```
dotnet build "E:\!!!Program to work on\Proper Crystal Relay\tools\private\crystal-relay-live-list\CrystalRelayLiveList.csproj" --no-restore
```
Expected: Build succeeds.

- [ ] **Step 8: Run tests**

Run:
```
dotnet test "E:\!!!Program to work on\Proper Crystal Relay\tools\private\crystal-relay-live-list-tests\CrystalRelayLiveList.Tests.csproj"
```
Expected: PASS — all 6 tests.

- [ ] **Step 9: Commit**

```
git add "tools/private/crystal-relay-live-list\MainWindow.xaml.cs"
git commit -m "Add Disliked view state, navigation handler, status text, and tab reset"
```

---

### Task 6: Add favorite-star visual state + dislike button to card XAML

**Files:**
- Modify: `tools/private/crystal-relay-live-list/MainWindow.xaml` (card template, lines 1161-1221)

- [ ] **Step 1: Add a 5th column to the card header grid**

Find lines 1161-1167 — the header Grid column definitions:

```xml
                                            <Grid Grid.Row="0">
                                                <Grid.ColumnDefinitions>
                                                    <ColumnDefinition Width="*" />
                                                    <ColumnDefinition Width="Auto" />
                                                    <ColumnDefinition Width="Auto" />
                                                    <ColumnDefinition Width="Auto" />
                                                </Grid.ColumnDefinitions>
```

Replace with:

```xml
                                            <Grid Grid.Row="0">
                                                <Grid.ColumnDefinitions>
                                                    <ColumnDefinition Width="*" />
                                                    <ColumnDefinition Width="Auto" />
                                                    <ColumnDefinition Width="Auto" />
                                                    <ColumnDefinition Width="Auto" />
                                                    <ColumnDefinition Width="Auto" />
                                                </Grid.ColumnDefinitions>
```

- [ ] **Step 2: Replace the favorite star button with a DataTrigger-styled version and add a dislike button**

Find lines 1208-1220 — the existing favorite star button:

```xml
                                                <Button Grid.Column="3"
                                                        Margin="6,0,0,0"
                                                        Padding="4,2"
                                                        Background="Transparent"
                                                        BorderBrush="Transparent"
                                                        Cursor="Hand"
                                                        ToolTip="Toggle favorite"
                                                        Tag="{Binding TwitchUrl}"
                                                        Click="OnToggleFavoriteClicked">
                                                    <TextBlock Text="&#x2605;"
                                                               FontSize="16"
                                                               Foreground="{StaticResource PinkBrush}" />
                                                </Button>
```

Replace with:

```xml
                                                <Button Grid.Column="3"
                                                        Margin="6,0,0,0"
                                                        Padding="4,2"
                                                        Background="Transparent"
                                                        BorderBrush="Transparent"
                                                        Cursor="Hand"
                                                        Tag="{Binding TwitchUrl}"
                                                        Click="OnToggleFavoriteClicked">
                                                    <TextBlock Text="&#x2605;"
                                                               FontSize="16"
                                                               Foreground="{StaticResource MutedBrush}"
                                                               Opacity="0.4">
                                                        <TextBlock.Style>
                                                            <Style TargetType="TextBlock">
                                                                <Setter Property="ToolTip" Value="Toggle favorite" />
                                                                <Style.Triggers>
                                                                    <DataTrigger Binding="{Binding IsFavorite}" Value="True">
                                                                        <Setter Property="Foreground" Value="{StaticResource PinkBrush}" />
                                                                        <Setter Property="Opacity" Value="1.0" />
                                                                        <Setter Property="ToolTip" Value="Unfavorite" />
                                                                    </DataTrigger>
                                                                </Style.Triggers>
                                                            </Style>
                                                        </TextBlock.Style>
                                                    </TextBlock>
                                                </Button>
                                                <Button Grid.Column="4"
                                                        Margin="6,0,0,0"
                                                        Padding="4,2"
                                                        Background="Transparent"
                                                        BorderBrush="Transparent"
                                                        Cursor="Hand"
                                                        Tag="{Binding TwitchUrl}"
                                                        Click="OnToggleDislikedClicked">
                                                    <TextBlock Text="&#x1F44E;"
                                                               FontSize="14"
                                                               Foreground="{StaticResource MutedBrush}"
                                                               Opacity="0.4">
                                                        <TextBlock.Style>
                                                            <Style TargetType="TextBlock">
                                                                <Setter Property="ToolTip" Value="Toggle disliked" />
                                                                <Style.Triggers>
                                                                    <DataTrigger Binding="{Binding IsDisliked}" Value="True">
                                                                        <Setter Property="Foreground" Value="{StaticResource PinkBrush}" />
                                                                        <Setter Property="Opacity" Value="1.0" />
                                                                        <Setter Property="ToolTip" Value="Remove from disliked" />
                                                                    </DataTrigger>
                                                                </Style.Triggers>
                                                            </Style>
                                                        </TextBlock.Style>
                                                    </TextBlock>
                                                </Button>
```

Note: the thumbs-down glyph `&#x1F44E;` renders as an emoji. If it doesn't render cleanly in Segoe UI, swap to `&#x26D4;` (no-entry sign) at `FontSize=16`. The `Opacity`/`Foreground`/`ToolTip` all flip via `DataTrigger` on `IsFavorite`/`IsDisliked` — no new converters needed.

- [ ] **Step 3: Build to verify XAML compiles**

Run:
```
dotnet build "E:\!!!Program to work on\Proper Crystal Relay\tools\private\crystal-relay-live-list\CrystalRelayLiveList.csproj" --no-restore
```
Expected: Build succeeds. The `OnToggleDislikedClicked` handler now exists (from Task 3) and is wired to the new button.

- [ ] **Step 4: Commit**

```
git add "tools/private/crystal-relay-live-list\MainWindow.xaml"
git commit -m "Add favorite-star visual state and dislike button with DataTrigger styling"
```

---

### Task 7: Add the Disliked radio tab and disliked-view panel

**Files:**
- Modify: `tools/private/crystal-relay-live-list/MainWindow.xaml` (radio tab section ~line 1042, and new disliked-view section after the history view)

- [ ] **Step 1: Add the third Disliked radio tab**

Find lines 1042-1046 — the `HistoryModeButton`:

```xml
                                <RadioButton x:Name="HistoryModeButton"
                                             GroupName="LiveListMode"
                                             Style="{StaticResource ViewModeRadioStyle}"
                                             Content="24h History"
                                             Checked="OnShowHistoryClicked" />
```

Add a new radio button after it:

```xml
                                <RadioButton x:Name="HistoryModeButton"
                                             GroupName="LiveListMode"
                                             Style="{StaticResource ViewModeRadioStyle}"
                                             Content="24h History"
                                             Checked="OnShowHistoryClicked" />
                                <RadioButton x:Name="DislikedModeButton"
                                             GroupName="LiveListMode"
                                             Style="{StaticResource ViewModeRadioStyle}"
                                             Content="Disliked"
                                             Checked="OnShowDislikedClicked" />
```

- [ ] **Step 2: Add the disliked-view ScrollViewer+ItemsControl after the history view**

Find line 1403 — the closing `</ScrollViewer>` of the history view (the second `</ScrollViewer>` in the file, around line 1403). To locate it precisely, find the history-view `</ItemsControl>` + `</StackPanel>` + `</ScrollViewer>` block ending around line 1403-1404.

Insert this new disliked-view block immediately after that `</ScrollViewer>` (and before the stream-view `Border` that starts around line 1405):

```xml
                    <ScrollViewer VerticalScrollBarVisibility="Auto"
                                  HorizontalScrollBarVisibility="Disabled"
                                  Style="{StaticResource ThemedScrollViewerStyle}"
                                  Visibility="{Binding IsDislikedViewVisible, Converter={StaticResource BoolToVisibilityConverter}}">
                        <StackPanel>
                            <TextBlock Margin="0,0,0,12"
                                       Text="{Binding DislikedStatusText}"
                                       Foreground="{StaticResource MutedBrush}"
                                       FontWeight="SemiBold"
                                       TextWrapping="Wrap" />
                            <ItemsControl ItemsSource="{Binding Users}">
                                <ItemsControl.ItemsPanel>
                                    <ItemsPanelTemplate>
                                        <WrapPanel />
                                    </ItemsPanelTemplate>
                                </ItemsControl.ItemsPanel>
                                <ItemsControl.ItemTemplate>
                                    <DataTemplate>
                                        <Border Width="284"
                                                MinHeight="178"
                                                Margin="0,0,14,14"
                                                Padding="16"
                                                Background="{StaticResource PanelStrongBrush}"
                                                BorderBrush="{StaticResource CrystalBorderBrush}"
                                                BorderThickness="1"
                                                CornerRadius="14">
                                            <Grid>
                                                <Grid.RowDefinitions>
                                                    <RowDefinition Height="Auto" />
                                                    <RowDefinition Height="Auto" />
                                                    <RowDefinition Height="*" />
                                                    <RowDefinition Height="Auto" />
                                                </Grid.RowDefinitions>

                                                <Grid Grid.Row="0">
                                                    <Grid.ColumnDefinitions>
                                                        <ColumnDefinition Width="*" />
                                                        <ColumnDefinition Width="Auto" />
                                                        <ColumnDefinition Width="Auto" />
                                                        <ColumnDefinition Width="Auto" />
                                                    </Grid.ColumnDefinitions>
                                                    <TextBlock Grid.Column="0"
                                                               Text="{Binding DisplayName}"
                                                               Foreground="{StaticResource TextBrush}"
                                                               FontSize="19"
                                                               FontWeight="Black"
                                                               TextWrapping="Wrap" />
                                                    <Border Grid.Column="2"
                                                            Margin="10,0,0,0"
                                                            Padding="8,4"
                                                            VerticalAlignment="Top"
                                                            Background="{StaticResource LivePillBrush}"
                                                            CornerRadius="999">
                                                        <TextBlock Text="LIVE"
                                                                   Foreground="{StaticResource ButtonTextBrush}"
                                                                   FontSize="10"
                                                                   FontWeight="Bold" />
                                                    </Border>
                                                    <Button Grid.Column="3"
                                                            Margin="6,0,0,0"
                                                            Padding="4,2"
                                                            Background="Transparent"
                                                            BorderBrush="Transparent"
                                                            Cursor="Hand"
                                                            Tag="{Binding TwitchUrl}"
                                                            Click="OnToggleDislikedClicked">
                                                        <TextBlock Text="&#x1F44E;"
                                                                   FontSize="14"
                                                                   Foreground="{StaticResource MutedBrush}"
                                                                   Opacity="0.4">
                                                            <TextBlock.Style>
                                                                <Style TargetType="TextBlock">
                                                                    <Setter Property="ToolTip" Value="Toggle disliked" />
                                                                    <Style.Triggers>
                                                                        <DataTrigger Binding="{Binding IsDisliked}" Value="True">
                                                                            <Setter Property="Foreground" Value="{StaticResource PinkBrush}" />
                                                                            <Setter Property="Opacity" Value="1.0" />
                                                                            <Setter Property="ToolTip" Value="Remove from disliked" />
                                                                        </DataTrigger>
                                                                    </Style.Triggers>
                                                                </Style>
                                                            </TextBlock.Style>
                                                        </TextBlock>
                                                    </Button>
                                                </Grid>

                                                <Border Grid.Row="1"
                                                        Margin="0,12,0,0"
                                                        Padding="10,7"
                                                        Background="{StaticResource PanelInsetBrush}"
                                                        BorderBrush="{StaticResource SubtleBorderBrush}"
                                                        BorderThickness="1"
                                                        CornerRadius="10">
                                                    <TextBlock Text="{Binding TwitchUrl}"
                                                               Foreground="{StaticResource CyanBrush}"
                                                               FontWeight="SemiBold"
                                                               TextWrapping="Wrap" />
                                                </Border>

                                                <TextBlock Grid.Row="2"
                                                           Margin="0,12,0,14"
                                                           Text="{Binding DetailText}"
                                                           Foreground="{StaticResource MutedBrush}"
                                                           TextWrapping="Wrap" />

                                                <WrapPanel Grid.Row="3">
                                                    <Button Width="88"
                                                            Margin="0,0,8,0"
                                                            Style="{StaticResource ActionButtonStyle}"
                                                            Content="View"
                                                            Tag="{Binding TwitchUrl}"
                                                            Click="OnViewStreamClicked" />
                                                    <Button Width="126"
                                                            Style="{StaticResource GhostButtonStyle}"
                                                            Content="Open Stream"
                                                            Tag="{Binding TwitchUrl}"
                                                            Click="OnOpenStreamClicked" />
                                                </WrapPanel>
                                            </Grid>
                                        </Border>
                                    </DataTemplate>
                                </ItemsControl.ItemTemplate>
                            </ItemsControl>
                        </StackPanel>
                    </ScrollViewer>
```

This disliked-view card omits the version/channel badges and the favorite star (a disliked user can't be favorited due to mutual exclusion), keeping just the LIVE pill, the dislike button (so you can un-dislike from inside the Disliked tab), the Twitch URL, detail text, and the View/Open Stream buttons. The dislike button's `DataTrigger` mirrors the main card.

- [ ] **Step 3: Build to verify XAML compiles**

Run:
```
dotnet build "E:\!!!Program to work on\Proper Crystal Relay\tools\private\crystal-relay-live-list\CrystalRelayLiveList.csproj" --no-restore
```
Expected: Build succeeds.

- [ ] **Step 4: Run tests**

Run:
```
dotnet test "E:\!!!Program to work on\Proper Crystal Relay\tools\private\crystal-relay-live-list-tests\CrystalRelayLiveList.Tests.csproj"
```
Expected: PASS — all 6 tests.

- [ ] **Step 5: Commit**

```
git add "tools/private/crystal-relay-live-list\MainWindow.xaml"
git commit -m "Add Disliked radio tab and disliked-view panel with dislike button"
```

---

### Task 8: Final build, test run, and manual verification

**Files:** None (verification only)

- [ ] **Step 1: Clean build of the app project**

Run:
```
dotnet build "E:\!!!Program to work on\Proper Crystal Relay\tools\private\crystal-relay-live-list\CrystalRelayLiveList.csproj"
```
Expected: Build succeeds with no errors.

- [ ] **Step 2: Full test run**

Run:
```
dotnet test "E:\!!!Program to work on\Proper Crystal Relay\tools\private\crystal-relay-live-list-tests\CrystalRelayLiveList.Tests.csproj"
```
Expected: PASS — 3 `FavoritesStoreTests` + 3 `DislikedStoreTests` = 6 tests pass.

- [ ] **Step 3: Manual smoke test (optional, user-run)**

Launch the built `CrystalRelayLiveList.exe` from `tools/private/crystal-relay-live-list/bin/Debug/net10.0-windows/`. Verify:
1. Favorite star on a live user card dims when not favorite, brightens to pink when toggled on, dims again when toggled off. ToolTip flips "Toggle favorite" / "Unfavorite".
2. Dislike button (thumbs-down) on the same card: dim when not disliked, pink when disliked. ToolTip flips "Toggle disliked" / "Remove from disliked".
3. Toggling favorite on a disliked user removes the dislike (mutual exclusion), and vice versa.
4. A disliked user disappears from the main Live Now list immediately.
5. The "Disliked" radio tab shows disliked live users only.
6. Disliking a currently-live user does not produce a sound alert, tray balloon, or unread badge when they next go live (verifiable by watching a known disliked user's state transition, or by trusting the unit-tested exclusion logic).
7. The "Favorites only" checkbox still works on the main list.
8. Switching between Live Now / 24h History / Disliked tabs shows the correct view and resets the others.

- [ ] **Step 4: No commit needed (verification only)**

If all checks pass, the feature is complete. If any check fails, file the specific failure as a follow-up task with reproduction steps.
