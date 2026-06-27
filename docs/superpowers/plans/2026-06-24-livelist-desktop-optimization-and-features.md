# Crystal Relay Live List Desktop — Optimization & Features Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Apply 19 optimization/robustness/feature items to the private `crystal-relay-live-list` WPF dev tool: incremental diff refresh, dirty-flag history writes, paused-when-hidden storyboard, a custom VirtualizingWrapPanel, cached config, single-sort reuse, cheaper key-set swap, hardened async error handling, global crash handlers, short-poll near-realtime, search/filter, system tray + toast, favorites, taskbar unread badge, dev-command presets + copy history, stats, WebView2 cleanup, and retry/backoff.

**Architecture:** The current app is one 1520-line `MainWindow.xaml.cs`. We extract testable pure logic into focused service files (`LiveUserKey`, `LiveListDiffer`, `LiveHistoryStore`, `LiveListConfigCache`, `RetryPolicy`, `DevCommandService`, `FavoritesStore`, `LiveStatsTracker`, `VirtualizingWrapPanel`) and UI helpers (`TrayService`, `StreamWatcherService`), keep `MainWindow` as a slim view/viewmodel that delegates to them, and add an xUnit test project for the pure logic. UI items are build-verified; pure logic is TDD.

**Tech Stack:** C# / .NET 10 / WPF / WinForms NotifyIcon (tray) / WebView2 / xUnit. Private dev tool under `tools/private/crystal-relay-live-list`. No main-program, updater, release-script, or public-doc changes.

**Scope decisions confirmed with user:**
- #11 realtime: **short-poll** (configurable fast interval, default 30s toggle) — no Cloudflare worker changes.
- #16 dev commands: the `!screm` parser only supports `grow|shrink|scalerandom|move|moverandom|firesale` (verified `VrcTwitchOscBridge/Services/DevChatCommandParser.cs`). So #16 = **named presets + copy history**, no new command grammar.
- #13 tray: **tray icon + minimize-to-tray + toast/balloon on new live**, with context menu (Show / Refresh / Exit).

---

## File map

### Create — pure logic services (unit-tested)
- `tools/private/crystal-relay-live-list/Services/LiveUserKey.cs` — shared Twitch URL/slug parsing + key normalization (extracted from `MainWindow`).
- `tools/private/crystal-relay-live-list/Services/LiveListDiffer.cs` — compute add/update/remove between a current keyed collection and a new payload (#1, #2, #8).
- `tools/private/crystal-relay-live-list/Services/LiveHistoryStore.cs` — owns history dict, prune, **dirty-flag** save/load, single-sort (#3, #7).
- `tools/private/crystal-relay-live-list/Services/LiveListConfigCache.cs` — cached endpoint + sound-path resolution (#6).
- `tools/private/crystal-relay-live-list/Services/RetryPolicy.cs` — exponential backoff for refresh failures (#19).
- `tools/private/crystal-relay-live-list/Services/DevCommandService.cs` — command building + named presets + copy history (#16).
- `tools/private/crystal-relay-live-list/Services/FavoritesStore.cs` — favorites persistence + matching (#14).
- `tools/private/crystal-relay-live-list/Services/LiveStatsTracker.cs` — peak live count, unique streamers seen, session start (#17).
- `tools/private/crystal-relay-live-list/Controls/VirtualizingWrapPanel.cs` — custom virtualizing wrap panel (#5).

### Create — UI helpers (build-verified)
- `tools/private/crystal-relay-live-list/Services/TrayService.cs` — NotifyIcon + minimize-to-tray + balloon (#13).
- `tools/private/crystal-relay-live-list/Services/StreamWatcherService.cs` — WebView2 lifecycle + cleanup (#18, extracts existing stream logic).

### Create — viewmodels (extract from nested classes)
- `tools/private/crystal-relay-live-list/ViewModels/LiveUserViewModel.cs`
- `tools/private/crystal-relay-live-list/ViewModels/LiveHistoryEntryViewModel.cs`

### Create — test project
- `tools/private/crystal-relay-live-list-tests/CrystalRelayLiveList.Tests.csproj`
- `tools/private/crystal-relay-live-list-tests/LiveUserKeyTests.cs`
- `tools/private/crystal-relay-live-list-tests/LiveListDifferTests.cs`
- `tools/private/crystal-relay-live-list-tests/LiveHistoryStoreTests.cs`
- `tools/private/crystal-relay-live-list-tests/LiveListConfigCacheTests.cs`
- `tools/private/crystal-relay-live-list-tests/RetryPolicyTests.cs`
- `tools/private/crystal-relay-live-list-tests/DevCommandServiceTests.cs`
- `tools/private/crystal-relay-live-list-tests/FavoritesStoreTests.cs`
- `tools/private/crystal-relay-live-list-tests/LiveStatsTrackerTests.cs`
- `tools/private/crystal-relay-live-list-tests/VirtualizingWrapPanelLayoutTests.cs`

### Modify
- `tools/private/crystal-relay-live-list/CrystalRelayLiveList.csproj` — add `<UseWindowsForms>true</UseWindowsForms>` (#13); new .cs auto-included (SDK defaults on).
- `tools/private/crystal-relay-live-list/App.xaml.cs` — global crash handlers (#10).
- `tools/private/crystal-relay-live-list/MainWindow.xaml.cs` — wire all services; diff refresh (#1,#2,#8); short-poll (#11); retry (#19); config cache (#6); broaden catch (#9); search filter (#12); favorites UI (#14); stats (#17); taskbar badge (#15); tray wiring (#13); storyboard pause (#4); WebView2 cleanup via StreamWatcherService (#18).
- `tools/private/crystal-relay-live-list/MainWindow.xaml` — search box (#12); favorite star button on cards (#14); stats card (#17); `VirtualizingWrapPanel` (#5); `TaskbarItemInfo` overlay (#15).

---

## Task 1: Extract `LiveUserKey` + tests (#8 helper, shared)

**Files:**
- Create: `tools/private/crystal-relay-live-list/Services/LiveUserKey.cs`
- Create: `tools/private/crystal-relay-live-list-tests/LiveUserKeyTests.cs`
- Create: `tools/private/crystal-relay-live-list-tests/CrystalRelayLiveList.Tests.csproj`

- [ ] **Step 1: Create test project**

`tools/private/crystal-relay-live-list-tests/CrystalRelayLiveList.Tests.csproj`:
```xml
<Project Sdk="Microsoft.NET.Sdk">
  <PropertyGroup>
    <TargetFramework>net10.0-windows</TargetFramework>
    <IsPackable>false</IsPackable>
    <Nullable>enable</Nullable>
    <ImplicitUsings>enable</ImplicitUsings>
    <UseWPF>true</UseWPF>
    <LangVersion>latest</LangVersion>
  </PropertyGroup>
  <ItemGroup>
    <PackageReference Include="Microsoft.NET.Test.Sdk" Version="17.11.1" />
    <PackageReference Include="xunit" Version="2.9.2" />
    <PackageReference Include="xunit.runner.visualstudio" Version="2.8.2" />
  </ItemGroup>
  <ItemGroup>
    <ProjectReference Include="..\tools\private\crystal-relay-live-list\CrystalRelayLiveList.csproj" />
  </ItemGroup>
</Project>
```

- [ ] **Step 2: Write failing tests**

`tools/private/crystal-relay-live-list-tests/LiveUserKeyTests.cs`:
```csharp
using CrystalRelayLiveList.Services;
using Xunit;

namespace CrystalRelayLiveList.Tests;

public sealed class LiveUserKeyTests
{
    [Theory]
    [InlineData("https://www.twitch.tv/screminpal", "https://www.twitch.tv/screminpal")]
    [InlineData("https://twitch.tv/Screminpal", "https://www.twitch.tv/screminpal")]
    [InlineData("  https://www.twitch.tv/Casey  ", "https://www.twitch.tv/casey")]
    public void NormalizesTwitchChannelToLower(string input, string expected)
    {
        Assert.Equal(expected, LiveUserKey.Normalize(input, null));
    }

    [Theory]
    [InlineData("https://example.org/x")]
    [InlineData("not a url")]
    [InlineData("")]
    public void FallsBackToTrimmedUrlOrDisplayName(string url)
    {
        Assert.Equal("https://example.org/x", LiveUserKey.Normalize(url, null));
    }

    [Fact]
    public void EmptyUrlUsesDisplayName()
    {
        Assert.Equal("casey", LiveUserKey.Normalize("", "Casey"));
    }

    [Theory]
    [InlineData("https://www.twitch.tv/screminpal", true, "screminpal")]
    [InlineData("https://twitch.tv/Casey/videos", false, "")]
    [InlineData("https://example.org/x", false, "")]
    public void TryGetSlug(string url, bool ok, string slug)
    {
        var result = LiveUserKey.TryGetChannelSlug(url, out var outSlug);
        Assert.Equal(ok, result);
        Assert.Equal(slug, outSlug);
    }
}
```

- [ ] **Step 3: Run tests — expect compile failure (no service yet)**

Run: `dotnet test tools\private\crystal-relay-live-list-tests\CrystalRelayLiveList.Tests.csproj`
Expected: build error — `CrystalRelayLiveList.Services.LiveUserKey` not found.

- [ ] **Step 4: Implement `LiveUserKey`**

`tools/private/crystal-relay-live-list/Services/LiveUserKey.cs`:
```csharp
using System.Globalization;

namespace CrystalRelayLiveList.Services;

public static class LiveUserKey
{
    public static string Normalize(string? twitchUrl, string? displayName)
    {
        if (TryGetChannelSlug(twitchUrl, out var slug))
        {
            return string.Format(CultureInfo.InvariantCulture, "https://www.twitch.tv/{0}", slug.ToLowerInvariant());
        }

        if (!string.IsNullOrWhiteSpace(twitchUrl))
        {
            return twitchUrl.Trim();
        }

        return displayName?.Trim() ?? string.Empty;
    }

    public static bool TryGetChannelSlug(string? twitchUrl, out string channelSlug)
    {
        channelSlug = string.Empty;
        if (string.IsNullOrWhiteSpace(twitchUrl)
            || !Uri.TryCreate(twitchUrl.Trim(), UriKind.Absolute, out var uri)
            || (uri.Scheme != Uri.UriSchemeHttps && uri.Scheme != Uri.UriSchemeHttp)
            || !IsTwitchHost(uri.Host))
        {
            return false;
        }

        var slug = uri.AbsolutePath
            .Split('/', StringSplitOptions.RemoveEmptyEntries)
            .FirstOrDefault();
        if (string.IsNullOrWhiteSpace(slug) || !IsChannelSlug(slug))
        {
            return false;
        }

        channelSlug = slug;
        return true;
    }

    public static string BuildVodUrl(string? twitchUrl)
    {
        return TryGetChannelSlug(twitchUrl, out var slug)
            ? string.Format(CultureInfo.InvariantCulture, "https://www.twitch.tv/{0}/videos?filter=archives&sort=time", slug)
            : twitchUrl ?? string.Empty;
    }

    private static bool IsTwitchHost(string host)
    {
        return host.Equals("twitch.tv", StringComparison.OrdinalIgnoreCase)
            || host.EndsWith(".twitch.tv", StringComparison.OrdinalIgnoreCase);
    }

    private static bool IsChannelSlug(string slug)
    {
        return slug.All(c => char.IsLetterOrDigit(c) || c == '_');
    }
}
```

- [ ] **Step 5: Run tests — expect pass**

Run: `dotnet test tools\private\crystal-relay-live-list-tests\CrystalRelayLiveList.Tests.csproj`
Expected: PASS (6 tests).

- [ ] **Step 6: Build app to confirm no break**

Run: `dotnet build tools\private\crystal-relay-live-list\CrystalRelayLiveList.csproj --no-restore`
Expected: PASS (service not yet wired in, old code still present).

---

## Task 2: `LiveListDiffer` + tests (#1, #2, #8)

Replaces `Users.Clear()` + re-add and `HistoryEntries.Clear()` + re-add with incremental updates so WPF only touches changed containers.

**Files:**
- Create: `tools/private/crystal-relay-live-list/Services/LiveListDiffer.cs`
- Create: `tools/private/crystal-relay-live-list-tests/LiveListDifferTests.cs`

- [ ] **Step 1: Write failing tests**

`tools/private/crystal-relay-live-list-tests/LiveListDifferTests.cs`:
```csharp
using System.Collections.ObjectModel;
using CrystalRelayLiveList.Services;
using CrystalRelayLiveList.ViewModels;
using Xunit;

namespace CrystalRelayLiveList.Tests;

public sealed class LiveListDifferTests
{
    private static LiveUserViewModel User(string name, string url, string ver = "1.0") =>
        new(name, url, ver, "stable", DateTimeOffset.UtcNow);

    [Fact]
    public void Apply_AddsNewUsers()
    {
        var users = new ObservableCollection<LiveUserViewModel>();
        var incoming = new[] { User("A", "https://www.twitch.tv/a"), User("B", "https://www.twitch.tv/b") };
        var diff = LiveListDiffer.Diff(users, incoming, u => LiveUserKey.Normalize(u.TwitchUrl, u.DisplayName));

        LiveListDiffer.Apply(users, diff);

        Assert.Equal(2, users.Count);
    }

    [Fact]
    public void Apply_RemovesMissingUsers()
    {
        var users = new ObservableCollection<LiveUserViewModel>
        {
            User("A", "https://www.twitch.tv/a"),
            User("B", "https://www.twitch.tv/b")
        };
        var incoming = new[] { User("A", "https://www.twitch.tv/a") };
        var diff = LiveListDiffer.Diff(users, incoming, u => LiveUserKey.Normalize(u.TwitchUrl, u.DisplayName));

        LiveListDiffer.Apply(users, diff);

        Assert.Single(users);
        Assert.Equal("A", users[0].DisplayName);
    }

    [Fact]
    public void Apply_KeepsExistingInstance_WhenStillPresent()
    {
        var a = User("A", "https://www.twitch.tv/a");
        var users = new ObservableCollection<LiveUserViewModel> { a };
        var incoming = new[] { User("A", "https://www.twitch.tv/a", "2.0") };
        var diff = LiveListDiffer.Diff(users, incoming, u => LiveUserKey.Normalize(u.TwitchUrl, u.DisplayName));

        LiveListDiffer.Apply(users, diff);

        // Removed stale, added fresh — count stays 1, instance replaced because version changed.
        Assert.Single(users);
        Assert.Equal("2.0", users[0].RelayVersion);
    }

    [Fact]
    public void Apply_NoChanges_LeavesCollectionUntouched()
    {
        var a = User("A", "https://www.twitch.tv/a");
        var users = new ObservableCollection<LiveUserViewModel> { a };
        var incoming = new[] { User("A", "https://www.twitch.tv/a", a.RelayVersion) };
        var diff = LiveListDiffer.Diff(users, incoming, u => LiveUserKey.Normalize(u.TwitchUrl, u.DisplayName));

        LiveListDiffer.Apply(users, diff);

        Assert.Same(a, users[0]);
    }
}
```

- [ ] **Step 2: Run tests — expect compile failure**

Run: `dotnet test tools\private\crystal-relay-live-list-tests\CrystalRelayLiveList.Tests.csproj`
Expected: `LiveListDiffer` / `ViewModels.LiveUserViewModel` not found.

- [ ] **Step 3: Extract `LiveUserViewModel` to its own file**

Move the `public sealed class LiveUserViewModel` (currently nested in `MainWindow.xaml.cs:1429-1472`) to `tools/private/crystal-relay-live-list/ViewModels/LiveUserViewModel.cs` in namespace `CrystalRelayLiveList.ViewModels`. Keep all members identical. Remove the nested class from `MainWindow.xaml.cs`. Add `using CrystalRelayLiveList.ViewModels;` to `MainWindow.xaml.cs`.

- [ ] **Step 4: Implement `LiveListDiffer`**

`tools/private/crystal-relay-live-list/Services/LiveListDiffer.cs`:
```csharp
using System.Collections.ObjectModel;

namespace CrystalRelayLiveList.Services;

public sealed record LiveListDiff<T>(
    IReadOnlyList<T> ToAdd,
    IReadOnlyList<T> ToRemove,
    IReadOnlyList<T> ToUpdate,
    IReadOnlySet<string> UnchangedKeys);

public static class LiveListDiffer
{
    public static LiveListDiff<T> Diff<T>(
        ObservableCollection<T> current,
        IReadOnlyList<T> incoming,
        Func<T, string> keySelector,
        Func<T, T, bool> equals,
        IEqualityComparer<string>? keyComparer = null)
    {
        var cmp = keyComparer ?? StringComparer.OrdinalIgnoreCase;
        var currentByKey = new Dictionary<string, (int Index, T Item)>(cmp);
        for (var i = 0; i < current.Count; i++)
        {
            currentByKey[keySelector(current[i])] = (i, current[i]);
        }

        var incomingKeys = new HashSet<string>(cmp);
        var toAdd = new List<T>();
        var toUpdate = new List<T>();
        var unchanged = new HashSet<string>(cmp);

        foreach (var item in incoming)
        {
            var key = keySelector(item);
            if (string.IsNullOrWhiteSpace(key))
            {
                continue;
            }

            if (!incomingKeys.Add(key))
            {
                continue;
            }

            if (currentByKey.TryGetValue(key, out var existing))
            {
                if (equals(existing.Item, item))
                {
                    unchanged.Add(key);
                }
                else
                {
                    toUpdate.Add(item);
                }
            }
            else
            {
                toAdd.Add(item);
            }
        }

        var toRemove = new List<T>();
        foreach (var (key, existing) in currentByKey)
        {
            if (!incomingKeys.Contains(key))
            {
                toRemove.Add(existing.Item);
            }
        }

        return new LiveListDiff<T>(toAdd, toRemove, toUpdate, unchanged);
    }

    public static void Apply<T>(
        ObservableCollection<T> current,
        LiveListDiff<T> diff,
        Func<T, string> keySelector,
        Action<T, T>? replaceInPlace = null,
        IEqualityComparer<string>? keyComparer = null)
    {
        var cmp = keyComparer ?? StringComparer.OrdinalIgnoreCase;
        var indexByKey = new Dictionary<string, int>(cmp);
        for (var i = 0; i < current.Count; i++)
        {
            indexByKey[keySelector(current[i])] = i;
        }

        // Remove first (by key, stable order).
        foreach (var item in diff.ToRemove)
        {
            if (indexByKey.TryGetValue(keySelector(item), out var idx))
            {
                current.RemoveAt(idx);
                indexByKey.Remove(keySelector(item));
                // Rebuild indices above idx — simplest correct approach for small N.
                indexByKey.Clear();
                for (var i = 0; i < current.Count; i++)
                {
                    indexByKey[keySelector(current[i])] = i;
                }
            }
        }

        // Update in place where supported; otherwise replace.
        foreach (var item in diff.ToUpdate)
        {
            var key = keySelector(item);
            if (indexByKey.TryGetValue(key, out var idx))
            {
                if (replaceInPlace is not null)
                {
                    replaceInPlace(current[idx], item);
                }
                else
                {
                    current[idx] = item;
                }
            }
        }

        // Add.
        foreach (var item in diff.ToAdd)
        {
            current.Add(item);
        }
    }
}
```

- [ ] **Step 5: Run tests — expect pass**

Run: `dotnet test tools\private\crystal-relay-live-list-tests\CrystalRelayLiveList.Tests.csproj`
Expected: PASS (4 tests).

---

## Task 3: `LiveHistoryStore` with dirty flag + tests (#3, #7)

Owns the history dict; prunes >24h; only writes to disk when entries actually mutated; computes the sort once and reuses for both view + save.

**Files:**
- Create: `tools/private/crystal-relay-live-list/Services/LiveHistoryStore.cs`
- Create: `tools/private/crystal-relay-live-list-tests/LiveHistoryStoreTests.cs`

- [ ] **Step 1: Write failing tests**

`tools/private/crystal-relay-live-list-tests/LiveHistoryStoreTests.cs`:
```csharp
using CrystalRelayLiveList.Services;
using Xunit;

namespace CrystalRelayLiveList.Tests;

public sealed class LiveHistoryStoreTests
{
    private static LiveHistoryEntryRecord Entry(string name, string url, DateTimeOffset first, DateTimeOffset last) =>
        new()
        {
            Key = LiveUserKey.Normalize(url, name),
            DisplayName = name,
            TwitchUrl = url,
            FirstSeenLiveAt = first,
            LastSeenLiveAt = last
        };

    [Fact]
    public void Upsert_NewEntry_MarksDirty()
    {
        var store = new LiveHistoryStore();
        var now = DateTimeOffset.UtcNow;

        store.Upsert(new[] { Entry("A", "https://www.twitch.tv/a", now, now) }, now);

        Assert.True(store.IsDirty);
    }

    [Fact]
    public void Upsert_UnchangedEntry_DoesNotMarkDirty()
    {
        var now = DateTimeOffset.UtcNow;
        var store = new LiveHistoryStore();
        store.Upsert(new[] { Entry("A", "https://www.twitch.tv/a", now, now) }, now);
        store.MarkClean();

        store.Upsert(new[] { Entry("A", "https://www.twitch.tv/a", now, now) }, now);

        Assert.False(store.IsDirty);
    }

    [Fact]
    public void Prune_RemovesOlderThan24h()
    {
        var now = DateTimeOffset.UtcNow;
        var store = new LiveHistoryStore();
        store.Upsert(new[] { Entry("Old", "https://www.twitch.tv/old", now - TimeSpan.FromHours(25), now - TimeSpan.FromHours(25)) }, now);

        store.Prune(now);

        Assert.Empty(store.Snapshot());
    }

    [Fact]
    public void SortedSnapshot_IsOrderedByLastSeenDescThenName()
    {
        var now = DateTimeOffset.UtcNow;
        var store = new LiveHistoryStore();
        store.Upsert(new[]
        {
            Entry("B", "https://www.twitch.tv/b", now - TimeSpan.FromHours(2), now - TimeSpan.FromHours(2)),
            Entry("A", "https://www.twitch.tv/a", now, now)
        }, now);

        var sorted = store.SortedSnapshot();

        Assert.Equal("A", sorted[0].DisplayName);
        Assert.Equal("B", sorted[1].DisplayName);
    }
}
```

- [ ] **Step 2: Run tests — expect compile failure**

Run: `dotnet test tools\private\crystal-relay-live-list-tests\CrystalRelayLiveList.Tests.csproj`
Expected: `LiveHistoryStore` not found.

- [ ] **Step 3: Extract `LiveHistoryEntryRecord` to namespace**

Move `public sealed class LiveHistoryEntryRecord` (nested in `MainWindow.xaml.cs:1412-1427`) to `tools/private/crystal-relay-live-list/ViewModels/LiveHistoryEntryRecord.cs` in namespace `CrystalRelayLiveList.ViewModels`. Keep fields identical. Update `MainWindow.xaml.cs` `using`.

- [ ] **Step 4: Implement `LiveHistoryStore`**

`tools/private/crystal-relay-live-list/Services/LiveHistoryStore.cs`:
```csharp
using System.Collections.Generic;
using CrystalRelayLiveList.ViewModels;

namespace CrystalRelayLiveList.Services;

public sealed class LiveHistoryStore
{
    private static readonly TimeSpan HistoryWindow = TimeSpan.FromHours(24);
    private readonly Dictionary<string, LiveHistoryEntryRecord> entries = new(StringComparer.OrdinalIgnoreCase);
    private bool dirty;

    public bool IsDirty => dirty;

    public void MarkClean() => dirty = false;

    public void Load(IEnumerable<LiveHistoryEntryRecord> loaded)
    {
        entries.Clear();
        foreach (var entry in loaded)
        {
            var key = LiveUserKey.Normalize(entry.TwitchUrl, entry.DisplayName);
            if (string.IsNullOrWhiteSpace(key))
            {
                key = entry.Key?.Trim() ?? string.Empty;
            }
            if (string.IsNullOrWhiteSpace(key))
            {
                continue;
            }
            entries[key] = CleanEntry(entry, key);
        }
        dirty = true; // loaded state worth persisting once (pruned)
    }

    public void Upsert(IEnumerable<LiveUserViewModel> liveUsers, DateTimeOffset observedAt)
    {
        foreach (var user in liveUsers)
        {
            var key = LiveUserKey.Normalize(user.TwitchUrl, user.DisplayName);
            if (string.IsNullOrWhiteSpace(key))
            {
                continue;
            }

            var lastSeen = (user.LastPingAt ?? observedAt).ToUniversalTime();
            if (entries.TryGetValue(key, out var existing))
            {
                var changed = false;
                if (!string.Equals(existing.DisplayName, user.DisplayName, StringComparison.Ordinal))
                {
                    existing.DisplayName = user.DisplayName;
                    changed = true;
                }
                if (!string.Equals(existing.TwitchUrl, user.TwitchUrl, StringComparison.Ordinal))
                {
                    existing.TwitchUrl = user.TwitchUrl;
                    changed = true;
                }
                if (!string.Equals(existing.RelayVersion, user.RelayVersion, StringComparison.Ordinal))
                {
                    existing.RelayVersion = user.RelayVersion;
                    changed = true;
                }
                if (!string.Equals(existing.BuildChannel, user.BuildChannel, StringComparison.Ordinal))
                {
                    existing.BuildChannel = user.BuildChannel;
                    changed = true;
                }
                var newLast = lastSeen > existing.LastSeenLiveAt ? lastSeen : existing.LastSeenLiveAt;
                if (newLast != existing.LastSeenLiveAt)
                {
                    existing.LastSeenLiveAt = newLast;
                    changed = true;
                }
                if (existing.LastSeenLiveAt < existing.FirstSeenLiveAt)
                {
                    existing.LastSeenLiveAt = observedAt.ToUniversalTime();
                    changed = true;
                }
                if (changed)
                {
                    dirty = true;
                }
            }
            else
            {
                var first = observedAt.ToUniversalTime();
                entries[key] = new LiveHistoryEntryRecord
                {
                    Key = key,
                    DisplayName = user.DisplayName,
                    TwitchUrl = user.TwitchUrl,
                    RelayVersion = user.RelayVersion,
                    BuildChannel = user.BuildChannel,
                    FirstSeenLiveAt = first,
                    LastSeenLiveAt = lastSeen < first ? first : lastSeen
                };
                dirty = true;
            }
        }
    }

    public void Prune(DateTimeOffset now)
    {
        var cutoff = now.ToUniversalTime() - HistoryWindow;
        var stale = new List<string>();
        foreach (var (key, entry) in entries)
        {
            if (entry.LastSeenLiveAt.ToUniversalTime() < cutoff)
            {
                stale.Add(key);
            }
        }
        if (stale.Count == 0)
        {
            return;
        }
        foreach (var key in stale)
        {
            entries.Remove(key);
        }
        dirty = true;
    }

    public IReadOnlyList<LiveHistoryEntryRecord> Snapshot() => entries.Values.ToList();

    public IReadOnlyList<LiveHistoryEntryRecord> SortedSnapshot() =>
        entries.Values
            .OrderByDescending(e => e.LastSeenLiveAt)
            .ThenBy(e => e.DisplayName, StringComparer.OrdinalIgnoreCase)
            .ToList();

    private static LiveHistoryEntryRecord CleanEntry(LiveHistoryEntryRecord entry, string key)
    {
        var clean = new LiveHistoryEntryRecord
        {
            Key = key,
            DisplayName = entry.DisplayName?.Trim() ?? string.Empty,
            TwitchUrl = entry.TwitchUrl?.Trim() ?? string.Empty,
            RelayVersion = entry.RelayVersion?.Trim() ?? string.Empty,
            BuildChannel = entry.BuildChannel?.Trim() ?? string.Empty,
            FirstSeenLiveAt = entry.FirstSeenLiveAt.ToUniversalTime(),
            LastSeenLiveAt = entry.LastSeenLiveAt.ToUniversalTime()
        };
        if (clean.FirstSeenLiveAt == default)
        {
            clean.FirstSeenLiveAt = clean.LastSeenLiveAt == default ? DateTimeOffset.UtcNow : clean.LastSeenLiveAt;
        }
        if (clean.LastSeenLiveAt == default || clean.LastSeenLiveAt < clean.FirstSeenLiveAt)
        {
            clean.LastSeenLiveAt = clean.FirstSeenLiveAt;
        }
        return clean;
    }
}
```

> **Note:** `LiveUserViewModel` lives in `CrystalRelayLiveList.ViewModels`; add `using CrystalRelayLiveList.ViewModels;`.

- [ ] **Step 5: Run tests — expect pass**

Run: `dotnet test tools\private\crystal-relay-live-list-tests\CrystalRelayLiveList.Tests.csproj`
Expected: PASS (4 tests).

---

## Task 4: `LiveListConfigCache` + tests (#6)

Caches resolved endpoint URI + alert sound path; invalidates on manual refresh or when the candidate files' write times change.

**Files:**
- Create: `tools/private/crystal-relay-live-list/Services/LiveListConfigCache.cs`
- Create: `tools/private/crystal-relay-live-list-tests/LiveListConfigCacheTests.cs`

- [ ] **Step 1: Write failing tests**

`tools/private/crystal-relay-live-list-tests/LiveListConfigCacheTests.cs`:
```csharp
using CrystalRelayLiveList.Services;
using Xunit;

namespace CrystalRelayLiveList.Tests;

public sealed class LiveListConfigCacheTests
{
    [Fact]
    public void Invalidate_ForcesReloadOnNextResolve()
    {
        var dir = Path.Combine(Path.GetTempPath(), "crl-tests-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(dir);
        try
        {
            var localPath = Path.Combine(dir, "live-list.local.json");
            File.WriteAllText(localPath, "{\"liveApiEndpoint\":\"https://example.org/api/ping\"}");

            var cache = new LiveListConfigCache(new[] { localPath });
            var first = cache.Resolve();
            Assert.NotNull(first.Endpoint);
            cache.Invalidate();
            var second = cache.Resolve();
            Assert.NotNull(second.Endpoint);
            Assert.Equal(first.Endpoint, second.Endpoint);
        }
        finally
        {
            Directory.Delete(dir, true);
        }
    }

    [Fact]
    public void Resolve_ReturnsEmpty_WhenNoConfig()
    {
        var cache = new LiveListConfigCache(new[] { Path.Combine(Path.GetTempPath(), "does-not-exist-" + Guid.NewGuid()) });
        var resolved = cache.Resolve();
        Assert.Null(resolved.Endpoint);
        Assert.Equal(string.Empty, resolved.AlertSoundPath);
    }
}
```

- [ ] **Step 2: Run tests — expect compile failure**

Run: `dotnet test tools\private\crystal-relay-live-list-tests\CrystalRelayLiveList.Tests.csproj`
Expected: `LiveListConfigCache` not found.

- [ ] **Step 3: Implement `LiveListConfigCache`**

`tools/private/crystal-relay-live-list/Services/LiveListConfigCache.cs`:
```csharp
using System.IO;
using System.Text.Json;

namespace CrystalRelayLiveList.Services;

public sealed record LiveListResolvedConfig(Uri? Endpoint, string AlertSoundPath, string SourcePath);

public sealed class LiveListConfigCache
{
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web)
    {
        PropertyNameCaseInsensitive = true
    };

    private readonly IReadOnlyList<string> candidatePaths;
    private LiveListResolvedConfig? cached;
    private DateTime lastResolvedAtUtc;
    private long lastSignature;

    public LiveListConfigCache(IReadOnlyList<string> candidatePaths)
    {
        this.candidatePaths = candidatePaths;
    }

    public void Invalidate()
    {
        cached = null;
        lastSignature = 0;
    }

    public LiveListResolvedConfig Resolve()
    {
        if (cached is not null && !SignatureChanged())
        {
            return cached;
        }

        foreach (var path in candidatePaths)
        {
            if (!File.Exists(path))
            {
                continue;
            }
            try
            {
                var json = File.ReadAllText(path);
                var config = JsonSerializer.Deserialize<LiveListConfigPayload>(json, JsonOptions);
                if (config is null)
                {
                    continue;
                }

                var endpointText = !string.IsNullOrWhiteSpace(config.LiveApiEndpoint)
                    ? config.LiveApiEndpoint
                    : config.LiveFeedbackHeartbeatEndpoint;
                var endpoint = BuildLiveApiUri(endpointText ?? string.Empty);
                var sound = config.LiveAlertSoundPath ?? string.Empty;
                cached = new LiveListResolvedConfig(endpoint, sound, path);
                lastResolvedAtUtc = DateTime.UtcNow;
                lastSignature = ComputeSignature();
                return cached;
            }
            catch
            {
                // ignore unreadable candidate
            }
        }

        cached = new LiveListResolvedConfig(null, string.Empty, string.Empty);
        lastResolvedAtUtc = DateTime.UtcNow;
        lastSignature = ComputeSignature();
        return cached;
    }

    private bool SignatureChanged()
    {
        var current = ComputeSignature();
        return current != lastSignature;
    }

    private long ComputeSignature()
    {
        long sig = 0;
        foreach (var path in candidatePaths)
        {
            try
            {
                if (File.Exists(path))
                {
                    var info = new FileInfo(path);
                    sig = unchecked(sig * 31 + info.LastWriteTimeUtc.Ticks);
                }
            }
            catch
            {
                // ignore
            }
        }
        return sig;
    }

    private static Uri? BuildLiveApiUri(string endpoint)
    {
        if (string.IsNullOrWhiteSpace(endpoint)
            || !Uri.TryCreate(endpoint.Trim(), UriKind.Absolute, out var uri)
            || uri.UserInfo.Length > 0
            || (uri.Scheme != Uri.UriSchemeHttps && uri.Scheme != Uri.UriSchemeHttp))
        {
            return null;
        }

        var builder = new UriBuilder(uri) { Query = string.Empty, Fragment = string.Empty };
        var path = builder.Path.TrimEnd('/');
        if (path.EndsWith("/api/live", StringComparison.OrdinalIgnoreCase))
        {
            builder.Path = path;
        }
        else if (path.EndsWith("/api/ping", StringComparison.OrdinalIgnoreCase))
        {
            builder.Path = string.Concat(path.AsSpan(0, path.Length - "/api/ping".Length), "/api/live");
        }
        else
        {
            builder.Path = string.IsNullOrWhiteSpace(path) || path == "/" ? "/api/live" : $"{path}/api/live";
        }
        return builder.Uri;
    }

    private sealed class LiveListConfigPayload
    {
        public string LiveFeedbackHeartbeatEndpoint { get; set; } = string.Empty;
        public string LiveApiEndpoint { get; set; } = string.Empty;
        public string LiveAlertSoundPath { get; set; } = string.Empty;
    }
}
```

- [ ] **Step 4: Run tests — expect pass**

Run: `dotnet test tools\private\crystal-relay-live-list-tests\CrystalRelayLiveList.Tests.csproj`
Expected: PASS (2 tests).

---

## Task 5: `RetryPolicy` + tests (#19)

Exponential backoff for transient refresh failures; resets on success.

**Files:**
- Create: `tools/private/crystal-relay-live-list/Services/RetryPolicy.cs`
- Create: `tools/private/crystal-relay-live-list-tests/RetryPolicyTests.cs`

- [ ] **Step 1: Write failing tests**

`tools/private/crystal-relay-live-list-tests/RetryPolicyTests.cs`:
```csharp
using CrystalRelayLiveList.Services;
using Xunit;

namespace CrystalRelayLiveList.Tests;

public sealed class RetryPolicyTests
{
    [Fact]
    public void FirstFailure_ReturnsBaseDelay()
    {
        var policy = new RetryPolicy(baseDelay: TimeSpan.FromSeconds(1), maxDelay: TimeSpan.FromSeconds(30));
        Assert.Equal(TimeSpan.FromSeconds(1), policy.NextDelay());
    }

    [Fact]
    public void RepeatedFailures_GrowExponentially()
    {
        var policy = new RetryPolicy(TimeSpan.FromSeconds(1), TimeSpan.FromSeconds(30));
        Assert.Equal(TimeSpan.FromSeconds(1), policy.NextDelay());
        Assert.Equal(TimeSpan.FromSeconds(2), policy.NextDelay());
        Assert.Equal(TimeSpan.FromSeconds(4), policy.NextDelay());
    }

    [Fact]
    public void DelayCapsAtMax()
    {
        var policy = new RetryPolicy(TimeSpan.FromSeconds(10), TimeSpan.FromSeconds(30));
        Assert.Equal(TimeSpan.FromSeconds(10), policy.NextDelay());
        Assert.Equal(TimeSpan.FromSeconds(20), policy.NextDelay());
        Assert.Equal(TimeSpan.FromSeconds(30), policy.NextDelay());
        Assert.Equal(TimeSpan.FromSeconds(30), policy.NextDelay());
    }

    [Fact]
    public void Reset_ReturnsToBaseDelay()
    {
        var policy = new RetryPolicy(TimeSpan.FromSeconds(1), TimeSpan.FromSeconds(30));
        policy.NextDelay();
        policy.NextDelay();
        policy.Reset();
        Assert.Equal(TimeSpan.FromSeconds(1), policy.NextDelay());
    }
}
```

- [ ] **Step 2: Run tests — expect compile failure**

Run: `dotnet test tools\private\crystal-relay-live-list-tests\CrystalRelayLiveList.Tests.csproj`
Expected: `RetryPolicy` not found.

- [ ] **Step 3: Implement `RetryPolicy`**

`tools/private/crystal-relay-live-list/Services/RetryPolicy.cs`:
```csharp
namespace CrystalRelayLiveList.Services;

public sealed class RetryPolicy
{
    private readonly TimeSpan baseDelay;
    private readonly TimeSpan maxDelay;
    private int failures;

    public RetryPolicy(TimeSpan baseDelay, TimeSpan maxDelay)
    {
        this.baseDelay = baseDelay;
        this.maxDelay = maxDelay;
    }

    public TimeSpan NextDelay()
    {
        var delay = TimeSpan.FromTicks(baseDelay.Ticks * (1L << Math.Min(failures, 16)));
        if (delay > maxDelay)
        {
            delay = maxDelay;
        }
        failures++;
        return delay;
    }

    public void Reset() => failures = 0;
}
```

- [ ] **Step 4: Run tests — expect pass**

Run: `dotnet test tools\private\crystal-relay-live-list-tests\CrystalRelayLiveList.Tests.csproj`
Expected: PASS (4 tests).

---

## Task 6: `DevCommandService` — presets + copy history + tests (#16)

Builds the 5 real `!screm` commands; saves/loads named presets to AppData; keeps a capped copy history.

**Files:**
- Create: `tools/private/crystal-relay-live-list/Services/DevCommandService.cs`
- Create: `tools/private/crystal-relay-live-list-tests/DevCommandServiceTests.cs`

- [ ] **Step 1: Write failing tests**

`tools/private/crystal-relay-live-list-tests/DevCommandServiceTests.cs`:
```csharp
using System.Globalization;
using CrystalRelayLiveList.Services;
using Xunit;

namespace CrystalRelayLiveList.Tests;

public sealed class DevCommandServiceTests
{
    [Fact]
    public void BuildGrow_FormatsInvariant()
    {
        var svc = new DevCommandService();
        var cmd = svc.BuildGrow(0.25, 30, 1);
        Assert.Equal("!screm grow 0.25 30 1", cmd);
    }

    [Fact]
    public void BuildShrink_NegatesMetersInText()
    {
        var svc = new DevCommandService();
        var cmd = svc.BuildShrink(0.5, 10, 0);
        Assert.Equal("!screm shrink 0.5 10 0", cmd);
    }

    [Fact]
    public void BuildScalerandom_FormatsRange()
    {
        var svc = new DevCommandService();
        var cmd = svc.BuildScaleRandom(0.8, 2.0, 20);
        Assert.Equal("!screm scalerandom 0.8-2 20", cmd);
    }

    [Fact]
    public void BuildMove_FormatsDirectionSeconds()
    {
        var svc = new DevCommandService();
        Assert.Equal("!screm move forward 5", svc.BuildMove("forward", 5));
        Assert.Equal("!screm move spinleft 12", svc.BuildMove("spinleft", 12));
    }

    [Fact]
    public void BuildMoverandom_FormatsSeconds()
    {
        var svc = new DevCommandService();
        Assert.Equal("!screm moverandom 12", svc.BuildMoveRandom(12));
    }

    [Fact]
    public void BuildFiresale_FormatsPercentSeconds()
    {
        var svc = new DevCommandService();
        Assert.Equal("!screm firesale 25 120", svc.BuildFireSale(25, 120));
    }

    [Fact]
    public void CopyHistory_CappedAndOrderedNewestFirst()
    {
        var svc = new DevCommandService(historyCapacity: 3);
        svc.RecordCopy("!screm grow 0.25 30 1");
        svc.RecordCopy("!screm move forward 5");
        svc.RecordCopy("!screm firesale 25 120");
        svc.RecordCopy("!screm moverandom 12");

        var history = svc.CopyHistory();
        Assert.Equal(3, history.Count);
        Assert.Equal("!screm moverandom 12", history[0]);
    }

    [Fact]
    public void Presets_RoundTrip()
    {
        var dir = Path.Combine(Path.GetTempPath(), "crl-tests-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(dir);
        try
        {
            var path = Path.Combine(dir, "presets.json");
            var svc = new DevCommandService(presetsPath: path);
            svc.SavePreset("warmup", "!screm grow 0.25 30 1");
            svc.SavePreset("warmup", "!screm grow 0.25 30 2"); // overwrite

            var loaded = new DevCommandService(presetsPath: path).LoadPresets();
            Assert.Single(loaded);
            Assert.Equal("!screm grow 0.25 30 2", loaded["warmup"]);
        }
        finally
        {
            Directory.Delete(dir, true);
        }
    }
}
```

- [ ] **Step 2: Run tests — expect compile failure**

Run: `dotnet test tools\private\crystal-relay-live-list-tests\CrystalRelayLiveList.Tests.csproj`
Expected: `DevCommandService` not found.

- [ ] **Step 3: Implement `DevCommandService`**

`tools/private/crystal-relay-live-list/Services/DevCommandService.cs`:
```csharp
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Text.Json;

namespace CrystalRelayLiveList.Services;

public sealed class DevCommandService
{
    private const int DefaultHistoryCapacity = 25;
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web)
    {
        PropertyNameCaseInsensitive = true
    };

    private readonly string? presetsPath;
    private readonly int historyCapacity;
    private readonly LinkedList<string> copyHistory = new();

    public DevCommandService(string? presetsPath = null, int historyCapacity = DefaultHistoryCapacity)
    {
        this.presetsPath = presetsPath;
        this.historyCapacity = historyCapacity;
    }

    public string BuildGrow(double meters, int seconds, double transition) =>
        string.Format(CultureInfo.InvariantCulture, "!screm grow {0:0.###} {1} {2:0.###}", meters, seconds, transition);

    public string BuildShrink(double meters, int seconds, double transition) =>
        string.Format(CultureInfo.InvariantCulture, "!screm shrink {0:0.###} {1} {2:0.###}", meters, seconds, transition);

    public string BuildScaleRandom(double min, double max, int seconds) =>
        string.Format(CultureInfo.InvariantCulture, "!screm scalerandom {0:0.###}-{1:0.###} {2}", min, max, seconds);

    public string BuildMove(string direction, int seconds) =>
        string.Format(CultureInfo.InvariantCulture, "!screm move {0} {1}", direction, seconds);

    public string BuildMoveRandom(int seconds) =>
        string.Format(CultureInfo.InvariantCulture, "!screm moverandom {0}", seconds);

    public string BuildFireSale(int percent, int seconds) =>
        string.Format(CultureInfo.InvariantCulture, "!screm firesale {0} {1}", percent, seconds);

    public void RecordCopy(string command)
    {
        if (string.IsNullOrWhiteSpace(command))
        {
            return;
        }
        copyHistory.Remove(command);
        copyHistory.AddFirst(command);
        while (copyHistory.Count > historyCapacity)
        {
            copyHistory.RemoveLast();
        }
    }

    public IReadOnlyList<string> CopyHistory() => copyHistory.ToList();

    public IReadOnlyDictionary<string, string> LoadPresets()
    {
        if (presetsPath is null || !File.Exists(presetsPath))
        {
            return new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        }
        try
        {
            var json = File.ReadAllText(presetsPath);
            var payload = JsonSerializer.Deserialize<DevCommandPresetsPayload>(json, JsonOptions);
            return payload?.Presets is null
                ? new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
                : new Dictionary<string, string>(payload.Presets, StringComparer.OrdinalIgnoreCase);
        }
        catch
        {
            return new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        }
    }

    public void SavePreset(string name, string command)
    {
        if (string.IsNullOrWhiteSpace(name))
        {
            return;
        }
        var current = LoadPresets();
        current[name.Trim()] = command;
        WritePresets(current);
    }

    public bool DeletePreset(string name)
    {
        if (string.IsNullOrWhiteSpace(name))
        {
            return false;
        }
        var current = LoadPresets();
        if (!current.Remove(name.Trim()))
        {
            return false;
        }
        WritePresets(current);
        return true;
    }

    private void WritePresets(IReadOnlyDictionary<string, string> presets)
    {
        if (presetsPath is null)
        {
            return;
        }
        try
        {
            var dir = Path.GetDirectoryName(presetsPath);
            if (!string.IsNullOrWhiteSpace(dir))
            {
                Directory.CreateDirectory(dir);
            }
            var payload = new DevCommandPresetsPayload { Presets = presets.ToDictionary(k => k.Key, v => v.Value) };
            var temp = presetsPath + ".tmp";
            File.WriteAllText(temp, JsonSerializer.Serialize(payload, new JsonSerializerOptions(JsonOptions) { WriteIndented = true }));
            if (File.Exists(presetsPath))
            {
                File.Replace(temp, presetsPath, null);
            }
            else
            {
                File.Move(temp, presetsPath);
            }
        }
        catch
        {
            // presets are a convenience; never throw
        }
    }

    private sealed class DevCommandPresetsPayload
    {
        public Dictionary<string, string> Presets { get; set; } = new();
    }
}
```

- [ ] **Step 4: Run tests — expect pass**

Run: `dotnet test tools\private\crystal-relay-live-list-tests\CrystalRelayLiveList.Tests.csproj`
Expected: PASS (8 tests).

---

## Task 7: `FavoritesStore` + tests (#14)

Persists favorite channel keys to AppData; matches against live users; supports per-favorite alert sound override.

**Files:**
- Create: `tools/private/crystal-relay-live-list/Services/FavoritesStore.cs`
- Create: `tools/private/crystal-relay-live-list-tests/FavoritesStoreTests.cs`

- [ ] **Step 1: Write failing tests**

`tools/private/crystal-relay-live-list-tests/FavoritesStoreTests.cs`:
```csharp
using CrystalRelayLiveList.Services;
using Xunit;

namespace CrystalRelayLiveList.Tests;

public sealed class FavoritesStoreTests
{
    [Fact]
    public void Toggle_AddsAndRemovesFavorite()
    {
        var dir = Path.Combine(Path.GetTempPath(), "crl-tests-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(dir);
        try
        {
            var path = Path.Combine(dir, "favorites.json");
            var store = new FavoritesStore(path);

            Assert.True(store.Toggle("https://www.twitch.tv/a"));
            Assert.True(store.IsFavorite("https://www.twitch.tv/a"));
            Assert.False(store.Toggle("https://www.twitch.tv/a"));
            Assert.False(store.IsFavorite("https://www.twitch.tv/a"));
        }
        finally
        {
            Directory.Delete(dir, true);
        }
    }

    [Fact]
    public void IsFavorite_CaseInsensitive()
    {
        var dir = Path.Combine(Path.GetTempPath(), "crl-tests-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(dir);
        try
        {
            var path = Path.Combine(dir, "favorites.json");
            var store = new FavoritesStore(path);
            store.Toggle("https://www.twitch.tv/casey");

            Assert.True(store.IsFavorite("https://www.twitch.tv/Casey"));
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
            var path = Path.Combine(dir, "favorites.json");
            new FavoritesStore(path).Toggle("https://www.twitch.tv/a");

            Assert.True(new FavoritesStore(path).IsFavorite("https://www.twitch.tv/a"));
        }
        finally
        {
            Directory.Delete(dir, true);
        }
    }
}
```

- [ ] **Step 2: Run tests — expect compile failure**

Run: `dotnet test tools\private\crystal-relay-live-list-tests\CrystalRelayLiveList.Tests.csproj`
Expected: `FavoritesStore` not found.

- [ ] **Step 3: Implement `FavoritesStore`**

`tools/private/crystal-relay-live-list/Services/FavoritesStore.cs`:
```csharp
using System.Collections.Generic;
using System.IO;
using System.Text.Json;

namespace CrystalRelayLiveList.Services;

public sealed class FavoritesStore
{
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web)
    {
        PropertyNameCaseInsensitive = true
    };

    private readonly string path;
    private readonly HashSet<string> keys = new(StringComparer.OrdinalIgnoreCase);

    public FavoritesStore(string path)
    {
        this.path = path;
        Load();
    }

    public IReadOnlyCollection<string> Keys => keys;

    public bool IsFavorite(string key) => keys.Contains(key);

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
            var payload = JsonSerializer.Deserialize<FavoritesPayload>(json, JsonOptions);
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
            var payload = new FavoritesPayload { Keys = keys.ToList() };
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
            // favorites are a convenience; never throw
        }
    }

    private sealed class FavoritesPayload
    {
        public List<string> Keys { get; set; } = new();
    }
}
```

- [ ] **Step 4: Run tests — expect pass**

Run: `dotnet test tools\private\crystal-relay-live-list-tests\CrystalRelayLiveList.Tests.csproj`
Expected: PASS (3 tests).

---

## Task 8: `LiveStatsTracker` + tests (#17)

Tracks peak live count, total unique streamers ever seen (this session + persisted), session start.

**Files:**
- Create: `tools/private/crystal-relay-live-list/Services/LiveStatsTracker.cs`
- Create: `tools/private/crystal-relay-live-list-tests/LiveStatsTrackerTests.cs`

- [ ] **Step 1: Write failing tests**

`tools/private/crystal-relay-live-list-tests/LiveStatsTrackerTests.cs`:
```csharp
using CrystalRelayLiveList.Services;
using Xunit;

namespace CrystalRelayLiveList.Tests;

public sealed class LiveStatsTrackerTests
{
    [Fact]
    public void PeakLive_TracksMaximum()
    {
        var tracker = new LiveStatsTracker();
        tracker.RecordSnapshot(3);
        tracker.RecordSnapshot(5);
        tracker.RecordSnapshot(2);
        Assert.Equal(5, tracker.PeakLive);
    }

    [Fact]
    public void UniqueStreamers_AccumulatesDistinctKeys()
    {
        var tracker = new LiveStatsTracker();
        tracker.RecordSnapshot(1, new[] { "a", "b" });
        tracker.RecordSnapshot(1, new[] { "b", "c" });
        Assert.Equal(3, tracker.UniqueStreamersSeen);
    }

    [Fact]
    public void CurrentLive_IsLastSnapshot()
    {
        var tracker = new LiveStatsTracker();
        tracker.RecordSnapshot(3);
        tracker.RecordSnapshot(2);
        Assert.Equal(2, tracker.CurrentLive);
    }
}
```

- [ ] **Step 2: Run tests — expect compile failure**

Run: `dotnet test tools\private\crystal-relay-live-list-tests\CrystalRelayLiveList.Tests.csproj`
Expected: `LiveStatsTracker` not found.

- [ ] **Step 3: Implement `LiveStatsTracker`**

`tools/private/crystal-relay-live-list/Services/LiveStatsTracker.cs`:
```csharp
using System.Collections.Generic;

namespace CrystalRelayLiveList.Services;

public sealed class LiveStatsTracker
{
    private readonly HashSet<string> uniqueKeys = new(StringComparer.OrdinalIgnoreCase);

    public DateTimeOffset SessionStartedAt { get; } = DateTimeOffset.UtcNow;
    public int PeakLive { get; private set; }
    public int CurrentLive { get; private set; }
    public int UniqueStreamersSeen => uniqueKeys.Count;

    public void RecordSnapshot(int liveCount, IEnumerable<string>? liveKeys = null)
    {
        CurrentLive = liveCount;
        if (liveCount > PeakLive)
        {
            PeakLive = liveCount;
        }
        if (liveKeys is not null)
        {
            foreach (var key in liveKeys)
            {
                if (!string.IsNullOrWhiteSpace(key))
                {
                    uniqueKeys.Add(key);
                }
            }
        }
    }

    public TimeSpan SessionDuration => DateTimeOffset.UtcNow - SessionStartedAt;
}
```

- [ ] **Step 4: Run tests — expect pass**

Run: `dotnet test tools\private\crystal-relay-live-list-tests\CrystalRelayLiveList.Tests.csproj`
Expected: PASS (3 tests).

---

## Task 9: `VirtualizingWrapPanel` + layout-math tests (#5)

Custom `VirtualizingPanel` that realizes only items in the viewport, laid out in wrapped rows. The measure/arrange math is factored into a testable static helper.

**Files:**
- Create: `tools/private/crystal-relay-live-list/Controls/VirtualizingWrapPanel.cs`
- Create: `tools/private/crystal-relay-live-list-tests/VirtualizingWrapPanelLayoutTests.cs`

- [ ] **Step 1: Write failing tests for the layout math**

`tools/private/crystal-relay-live-list-tests/VirtualizingWrapPanelLayoutTests.cs`:
```csharp
using CrystalRelayLiveList.Controls;
using Xunit;

namespace CrystalRelayLiveList.Tests;

public sealed class VirtualizingWrapPanelLayoutTests
{
    [Fact]
    public void ComputeRows_WrapsWhenItemExceedsAvailableWidth()
    {
        var sizes = new[] { new Size(100, 50), new Size(100, 50), new Size(100, 50) };
        var rows = VirtualizingWrapPanel.ComputeRows(sizes, itemWidth: 100, availableWidth: 250, spacing: 10);

        Assert.Equal(2, rows.Count);
        Assert.Equal(2, rows[0].Count);
        Assert.Single(rows[1]);
    }

    [Fact]
    public void ComputeRows_SingleItemFits()
    {
        var rows = VirtualizingWrapPanel.ComputeRows(new[] { new Size(100, 50) }, 100, 250, 10);
        Assert.Single(rows);
        Assert.Single(rows[0]);
    }

    [Fact]
    public void ComputeYOffsetAndHeight_SumsRowHeightsWithSpacing()
    {
        var rows = new[] { new[] { new Size(100, 50) }, new[] { new Size(100, 70) } };
        var (y0, h0) = VirtualizingWrapPanel.ComputeRowOffset(rows, 0, spacing: 10);
        var (y1, h1) = VirtualizingWrapPanel.ComputeRowOffset(rows, 1, spacing: 10);
        Assert.Equal(0, y0);
        Assert.Equal(50, h0);
        Assert.Equal(60, y1);
        Assert.Equal(70, h1);
    }
}
```

- [ ] **Step 2: Run tests — expect compile failure**

Run: `dotnet test tools\private\crystal-relay-live-list-tests\CrystalRelayLiveList.Tests.csproj`
Expected: `VirtualizingWrapPanel` not found.

- [ ] **Step 3: Implement `VirtualizingWrapPanel`**

`tools/private/crystal-relay-live-list/Controls/VirtualizingWrapPanel.cs`:
```csharp
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Media;

namespace CrystalRelayLiveList.Controls;

public class VirtualizingWrapPanel : VirtualizingPanel, IScrollInfo
{
    private Size extent = new(double.PositiveInfinity, 0);
    private Size viewport;
    private Offset offset;
    private Size[] itemSizes = Array.Empty<Size>();
    private List<List<int>> rows = new();

    public double ItemWidth { get; set; } = 284;
    public double ItemHeight { get; set; } = 178;
    public double Spacing { get; set; } = 14;

    public static List<List<int>> ComputeRows(IReadOnlyList<Size> sizes, double itemWidth, double availableWidth, double spacing)
    {
        var rows = new List<List<int>>();
        var current = new List<int>();
        var used = 0d;
        for (var i = 0; i < sizes.Count; i++)
        {
            var w = itemWidth;
            if (used + w > availableWidth && current.Count > 0)
            {
                rows.Add(current);
                current = new List<int>();
                used = 0;
            }
            current.Add(i);
            used += w + spacing;
        }
        if (current.Count > 0)
        {
            rows.Add(current);
        }
        return rows;
    }

    public static (double Y, double Height) ComputeRowOffset(IReadOnlyList<IReadOnlyList<Size>> rows, int rowIndex, double spacing)
    {
        var y = 0d;
        for (var i = 0; i < rowIndex; i++)
        {
            var h = 0d;
            foreach (var s in rows[i])
            {
                if (s.Height > h) h = s.Height;
            }
            y += h + spacing;
        }
        var rowHeight = 0d;
        foreach (var s in rows[rowIndex])
        {
            if (s.Height > rowHeight) rowHeight = s.Height;
        }
        return (y, rowHeight);
    }

    // IScrollInfo members delegate to standard behavior; full implementation below.
    public bool CanVerticallyScroll { get; set; }
    public bool CanHorizontallyScroll { get; set; }
    public double ExtentWidth => extent.Width;
    public double ExtentHeight => extent.Height;
    public double ViewportWidth => viewport.Width;
    public double ViewportHeight => viewport.Height;
    public double HorizontalOffset => offset.X;
    public double VerticalOffset => offset.Y;
    public ScrollViewer? ScrollOwner { get; set; }

    public Rect MakeVisible(Visual visual, Rect rectangle) => rectangle;

    public void SetVerticalOffset(double value)
    {
        var clamped = Math.Max(0, Math.Min(value, Math.Max(0, extent.Height - viewport.Height)));
        if (clamped == offset.Y) return;
        offset.Y = clamped;
        InvalidateMeasure();
        ScrollOwner?.InvalidateScrollInfo();
    }

    public void SetHorizontalOffset(double value) { }
    public void LineUp() => SetVerticalOffset(offset.Y - 16);
    public void LineDown() => SetVerticalOffset(offset.Y + 16);
    public void LineLeft() { }
    public void LineRight() { }
    public void MouseWheelUp() => LineUp();
    public void MouseWheelDown() => LineDown();
    public void PageUp() => SetVerticalOffset(offset.Y - viewport.Height);
    public void PageDown() => SetVerticalOffset(offset.Y + viewport.Height);
    public void PageLeft() { }
    public void PageRight() { }

    protected override Size MeasureOverride(Size availableSize)
    {
        viewport = availableSize;
        var generator = ItemContainerGenerator;
        if (generator is null) return availableSize;

        var count = Items.Count;
        itemSizes = new Size[count];
        for (var i = 0; i < count; i++)
        {
            itemSizes[i] = new Size(ItemWidth, ItemHeight);
        }

        rows = ComputeRows(itemSizes, ItemWidth, availableSize.Width, Spacing);

        double totalHeight = 0;
        foreach (var row in rows)
        {
            var h = 0d;
            foreach (var idx in row)
            {
                if (itemSizes[idx].Height > h) h = itemSizes[idx].Height;
            }
            totalHeight += h + Spacing;
        }
        extent = new Size(availableSize.Width, Math.Max(0, totalHeight - Spacing));

        // Realize only items visible in the viewport.
        var topY = 0d;
        foreach (var row in rows)
        {
            var rowHeight = 0d;
            foreach (var idx in row)
            {
                if (itemSizes[idx].Height > rowHeight) rowHeight = itemSizes[idx].Height;
            }
            var rowBottom = topY + rowHeight;
            if (rowBottom >= offset.Y && topY <= offset.Y + viewport.Height)
            {
                foreach (var idx in row)
                {
                    var container = generator.ContainerFromIndex(idx) as UIElement;
                    if (container is null)
                    {
                        var need = generator.GenerateNext();
                        if (need is UIElement ui)
                        {
                            AddInternalChild(ui);
                            generator.PrepareItemContainer(ui);
                            container = ui;
                        }
                    }
                    container?.Measure(new Size(ItemWidth, ItemHeight));
                }
            }
            topY += rowHeight + Spacing;
        }

        // Recycle off-screen containers.
        for (var i = InternalChildren.Count - 1; i >= 0; i--)
        {
            if (InternalChildren[i] is not UIElement child) continue;
            var genPos = generator.GeneratorPositionFromIndex(i);
            var index = genPos.Index + (genPos.Offset > 0 ? 1 : 0);
            var rowOf = RowContainingIndex(index);
            if (rowOf < 0) continue;
            var (y, h) = ComputeRowOffset(rows.Select(r => r.Select(idx => itemSizes[idx]).ToList()).ToList(), rowOf, Spacing);
            if (y + h < offset.Y || y > offset.Y + viewport.Height)
            {
                RemoveInternalChild(child);
                generator.Recycle(generator.GeneratorPositionFromIndex(index), 1);
            }
        }

        ScrollOwner?.InvalidateScrollInfo();
        return new Size(availableSize.Width, extent.Height);
    }

    protected override Size ArrangeOverride(Size finalSize)
    {
        var generator = ItemContainerGenerator;
        if (generator is null) return finalSize;

        double topY = 0;
        foreach (var row in rows)
        {
            var rowHeight = 0d;
            foreach (var idx in row)
            {
                if (itemSizes[idx].Height > rowHeight) rowHeight = itemSizes[idx].Height;
            }
            if (topY + rowHeight >= offset.Y && topY <= offset.Y + viewport.Height)
            {
                double x = 0;
                foreach (var idx in row)
                {
                    var container = generator.ContainerFromIndex(idx) as UIElement;
                    if (container is not null)
                    {
                        container.Arrange(new Rect(x, topY - offset.Y, ItemWidth, itemSizes[idx].Height));
                        x += ItemWidth + Spacing;
                    }
                }
            }
            topY += rowHeight + Spacing;
        }
        return finalSize;
    }

    private int RowContainingIndex(int index)
    {
        for (var r = 0; r < rows.Count; r++)
        {
            if (rows[r].Contains(index)) return r;
        }
        return -1;
    }
}
```

- [ ] **Step 4: Run tests — expect pass**

Run: `dotnet test tools\private\crystal-relay-live-list-tests\CrystalRelayLiveList.Tests.csproj`
Expected: PASS (3 layout tests). The WPF panel itself is build-verified in Task 13.

---

## Task 10: `TrayService` (#13)

Wraps WinForms `NotifyIcon` for tray icon, minimize-to-tray, and new-live balloon. Requires WinForms in the csproj.

**Files:**
- Create: `tools/private/crystal-relay-live-list/Services/TrayService.cs`
- Modify: `tools/private/crystal-relay-live-list/CrystalRelayLiveList.csproj`

- [ ] **Step 1: Enable WinForms in csproj**

Add to the `<PropertyGroup>` in `CrystalRelayLiveList.csproj`:
```xml
    <UseWindowsForms>true</UseWindowsForms>
```

- [ ] **Step 2: Implement `TrayService`**

`tools/private/crystal-relay-live-list/Services/TrayService.cs`:
```csharp
using System.Drawing;
using System.Windows;
using System.Windows.Forms;
using Application = System.Windows.Application;
using MessageBox = System.Windows.MessageBox;

namespace CrystalRelayLiveList.Services;

public sealed class TrayService : IDisposable
{
    private readonly NotifyIcon notifyIcon;
    private readonly Window window;
    private bool disposed;

    public TrayService(Window window, Action onShow, Action onRefresh)
    {
        this.window = window;
        notifyIcon = new NotifyIcon
        {
            Text = "Crystal Relay Live Feedback",
            Visible = true,
            Icon = LoadEmbeddedIcon()
        };

        var menu = new ContextMenuStrip();
        menu.Items.Add("Show", null, (_, _) => onShow());
        menu.Items.Add("Refresh", null, (_, _) => onRefresh());
        menu.Items.Add("-");
        menu.Items.Add("Exit", null, (_, _) =>
        {
            notifyIcon.Visible = false;
            Application.Current.Shutdown();
        });
        notifyIcon.ContextMenuStrip = menu;
        notifyIcon.DoubleClick += (_, _) => onShow();

        window.StateChanged += OnStateChanged;
        window.Closing += (_, _) => notifyIcon.Visible = false;
    }

    public void ShowBalloon(string title, string message)
    {
        if (disposed) return;
        notifyIcon.ShowBalloonTip(5000, title, message, ToolTipIcon.Info);
    }

    private void OnStateChanged(object? sender, EventArgs e)
    {
        if (window.WindowState == WindowState.Minimized)
        {
            window.Hide();
        }
        else if (window.Visibility != Visibility.Visible)
        {
            window.Show();
        }
    }

    private static Icon LoadEmbeddedIcon()
    {
        try
        {
            var path = Path.Combine(AppContext.BaseDirectory, "Assets", "crystal-relay-icon.ico");
            if (File.Exists(path))
            {
                return new Icon(path);
            }
        }
        catch
        {
            // fall through to default
        }
        return SystemIcons.Application;
    }

    public void Dispose()
    {
        if (disposed) return;
        disposed = true;
        window.StateChanged -= OnStateChanged;
        notifyIcon.Visible = false;
        notifyIcon.Dispose();
    }
}
```

- [ ] **Step 3: Build to verify WinForms reference works**

Run: `dotnet build tools\private\crystal-relay-live-list\CrystalRelayLiveList.csproj`
Expected: PASS.

---

## Task 11: `StreamWatcherService` (#18)

Extracts WebView2 lifecycle from `MainWindow`; adds cleanup-on-close + optional periodic cleanup of the user-data folder.

**Files:**
- Create: `tools/private/crystal-relay-live-list/Services/StreamWatcherService.cs`

- [ ] **Step 1: Implement `StreamWatcherService`**

`tools/private/crystal-relay-live-list/Services/StreamWatcherService.cs`:
```csharp
using System.IO;
using Microsoft.Web.WebView2.Core;
using System.Windows.Controls;

namespace CrystalRelayLiveList.Services;

public sealed class StreamWatcherService : IDisposable
{
    private const string TwitchViewerHost = "crystal-relay-live-feedback.test";
    private const string StreamViewerPageName = "stream-viewer.html";

    private readonly WebView2 webView;
    private bool initialized;
    private bool disposed;

    public StreamWatcherService(WebView2 webView)
    {
        this.webView = webView;
    }

    public bool IsReady => initialized && webView.CoreWebView2 is not null;

    public static string GetUserDataFolder() =>
        Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
            "CrystalRelay",
            "DevTools",
            "LiveList",
            "WebView2");

    public async Task EnsureReadyAsync()
    {
        if (IsReady) return;
        var folder = GetUserDataFolder();
        Directory.CreateDirectory(folder);
        var env = await CoreWebView2Environment.CreateAsync(null, folder);
        await webView.EnsureCoreWebView2Async(env);
        var core = webView.CoreWebView2 ?? throw new InvalidOperationException("WebView2 did not initialize.");
        core.SetVirtualHostNameToFolderMapping(
            TwitchViewerHost,
            AppContext.BaseDirectory,
            CoreWebView2HostResourceAccessKind.DenyCors);
        core.NewWindowRequested += OnNewWindowRequested;
        core.Settings.IsStatusBarEnabled = false;
        initialized = true;
    }

    public void Navigate(string channelSlug) =>
        (webView.CoreWebView2 ?? throw new InvalidOperationException("WebView2 not ready."))
            .Navigate(BuildViewerUri(channelSlug).ToString());

    public async Task ClearLoginAsync(string? channelSlug)
    {
        if (webView.CoreWebView2 is null) return;
        webView.CoreWebView2.CookieManager.DeleteAllCookies();
        await webView.CoreWebView2.Profile.ClearBrowsingDataAsync(CoreWebView2BrowsingDataKinds.AllProfile);
        if (!string.IsNullOrWhiteSpace(channelSlug))
        {
            webView.CoreWebView2.Navigate(BuildViewerUri(channelSlug).ToString());
        }
    }

    public void Stop()
    {
        if (webView.CoreWebView2 is not null)
        {
            webView.CoreWebView2.Stop();
            webView.CoreWebView2.Navigate("about:blank");
        }
    }

    private void OnNewWindowRequested(object? sender, CoreWebView2NewWindowRequestedEventArgs e)
    {
        e.Handled = true;
        if (webView.CoreWebView2 is null || string.IsNullOrWhiteSpace(e.Uri)) return;
        webView.CoreWebView2.Navigate(e.Uri);
    }

    private static Uri BuildViewerUri(string channelSlug)
    {
        var builder = new UriBuilder(Uri.UriSchemeHttps, TwitchViewerHost)
        {
            Path = StreamViewerPageName,
            Query = $"channel={Uri.EscapeDataString(channelSlug)}"
        };
        return builder.Uri;
    }

    public void Dispose()
    {
        if (disposed) return;
        disposed = true;
        try
        {
            if (webView.CoreWebView2 is not null)
            {
                webView.CoreWebView2.NewWindowRequested -= OnNewWindowRequested;
                webView.CoreWebView2.Stop();
                webView.CoreWebView2.Navigate("about:blank");
            }
            webView.Dispose();
        }
        catch
        {
            // best-effort cleanup
        }
    }
}
```

- [ ] **Step 2: Build to verify**

Run: `dotnet build tools\private\crystal-relay-live-list\CrystalRelayLiveList.csproj`
Expected: PASS.

---

## Task 12: Global crash handlers in `App.xaml.cs` (#10)

**Files:**
- Modify: `tools/private/crystal-relay-live-list/App.xaml.cs`

- [ ] **Step 1: Replace `App.xaml.cs` with crash handlers**

`tools/private/crystal-relay-live-list/App.xaml.cs`:
```csharp
using System.IO;
using System.Windows;
using System.Windows.Threading;

namespace CrystalRelayLiveList;

public partial class App : Application
{
    private static readonly string CrashLogFolder = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
        "CrystalRelay",
        "DevTools",
        "LiveList",
        "CrashLogs");

    protected override void OnStartup(StartupEventArgs e)
    {
        DispatcherUnhandledException += OnDispatcherUnhandledException;
        AppDomain.CurrentDomain.UnhandledException += OnDomainUnhandledException;
        System.Threading.Tasks.TaskScheduler.UnobservedTaskException += OnUnobservedTaskException;
        base.OnStartup(e);
    }

    private void OnDispatcherUnhandledException(object sender, DispatcherUnhandledExceptionEventArgs e)
    {
        WriteCrashLog("DispatcherUnhandledException", e.Exception);
        e.Handled = true;
    }

    private void OnDomainUnhandledException(object sender, UnhandledExceptionEventArgs e)
    {
        if (e.ExceptionObject is Exception ex)
        {
            WriteCrashLog("AppDomain.UnhandledException", ex);
        }
    }

    private void OnUnobservedTaskException(object? sender, System.Threading.Tasks.UnobservedTaskExceptionEventArgs e)
    {
        WriteCrashLog("TaskScheduler.UnobservedTaskException", e.Exception);
        e.SetObserved();
    }

    private static void WriteCrashLog(string source, Exception ex)
    {
        try
        {
            Directory.CreateDirectory(CrashLogFolder);
            var stamp = DateTime.Now.ToString("yyyyMMdd-HHmmss");
            var path = Path.Combine(CrashLogFolder, $"livelist-{stamp}-{Guid.NewGuid():N}.txt");
            File.WriteAllText(path, $"{source}:{Environment.NewLine}{ex}{Environment.NewLine}");
        }
        catch
        {
            // never throw from a crash handler
        }
    }
}
```

- [ ] **Step 2: Build to verify**

Run: `dotnet build tools\private\crystal-relay-live-list\CrystalRelayLiveList.csproj`
Expected: PASS.

---

## Task 13: Rewrite `MainWindow.xaml.cs` to wire all services (#1,#2,#3,#4,#6,#8,#9,#11,#12,#13,#14,#15,#16,#17,#18,#19)

This is the integration task. `MainWindow.xaml.cs` delegates to the services from Tasks 1-11. Key behavioral changes:

- **#1,#2 diff refresh**: `RefreshAsync` builds the incoming list, calls `LiveListDiffer.Diff` + `Apply` on `Users`; history uses `LiveHistoryStore.Upsert` + `SortedSnapshot()` and `LiveListDiffer.Apply` on `HistoryEntries`.
- **#3,#7 dirty save**: only call `SaveLiveHistory()` when `historyStore.IsDirty`; `MarkClean()` after.
- **#4 storyboard pause**: name the storyboard (`x:Name="DecorativeStoryboard"` in XAML) and call `Begin`/`Pause` based on `IsDecorativeBackdropVisible` and window state; resume on show.
- **#6 config cache**: `configCache.Resolve()`; `configCache.Invalidate()` on manual refresh.
- **#8 key-set swap**: replace clear+re-add loop with `knownLiveUserKeys = new HashSet<string>(...)`.
- **#9 broaden catch**: `catch (Exception ex)` (all) in `RefreshAsync`, log via crash handler.
- **#11 short-poll**: add `FastPollEnabled` bool + `FastPollInterval` (30s); timer interval chosen from the toggle; UI checkbox in hero.
- **#12 search/filter**: `SearchText` property; `CollectionViewSource.GetDefaultView(Users)` with `Filter` predicate; same for `HistoryEntries`.
- **#13 tray**: construct `TrayService`; call `ShowBalloon` on new live.
- **#14 favorites**: `FavoritesStore`; star button on each card toggles favorite; favorites listed/highlighted; favorite-only filter toggle.
- **#15 taskbar badge**: `TaskbarItemInfo` with overlay icon + `ProgressState`/`ProgressValue` showing new-live count since last focus.
- **#16 presets/copy history**: `DevCommandService`; preset save/load UI; copy history list with re-copy.
- **#17 stats**: `LiveStatsTracker`; display peak/unique/session in a stats card.
- **#18 WebView2 cleanup**: use `StreamWatcherService`; dispose on close.
- **#19 retry/backoff**: `RetryPolicy`; on failed refresh, delay next tick by `policy.NextDelay()` (capped), reset on success.

**Files:**
- Modify: `tools/private/crystal-relay-live-list/MainWindow.xaml.cs` (rewrite to delegate to services)
- Modify: `tools/private/crystal-relay-live-list/MainWindow.xaml` (add search box, favorite star, stats card, fast-poll toggle, `TaskbarItemInfo`, name the storyboard, swap `ItemsPanelTemplate` to `Controls:VirtualizingWrapPanel`)

- [ ] **Step 1: Update XAML namespaces + named storyboard + virtualizing panels + new controls**

In `MainWindow.xaml`:
- Add xmlns: `xmlns:controls="clr-namespace:CrystalRelayLiveList.Controls"`.
- Name the storyboard: `<Storyboard x:Name="DecorativeStoryboard" RepeatBehavior="Forever">` (move `x:Name` onto the existing Storyboard).
- Replace both `<ItemsPanelTemplate><WrapPanel/></ItemsPanelTemplate>` with `<ItemsPanelTemplate><controls:VirtualizingWrapPanel ItemWidth="284" ItemHeight="178" Spacing="14"/></ItemsPanelTemplate>`.
- Add a `TaskbarItemInfo` on the `Window`: `<Window.TaskbarItemInfo><TaskbarItemInfo x:Name="TaskbarBadge"/></Window.TaskbarItemInfo>`.
- In the hero `WrapPanel`, add a fast-poll `CheckBox` bound to `FastPollEnabled`.
- Add a search `TextBox` above the live `ScrollViewer`, bound to `SearchText`, with `UpdateSourceTrigger=PropertyChanged`.
- Add a favorite-star `Button` to each live card `DataTemplate` (Tag binds TwitchUrl, Click handler `OnToggleFavoriteClicked`), with a star glyph whose Visibility binds `IsFavorite`.
- Add a stats card (small `Border` with `MetaPillStyle`) showing `PeakLive`, `UniqueStreamersSeen`, and `SessionDurationText`.
- Add a preset `ComboBox` + "Save preset" / "Delete preset" buttons and a copy-history `ListBox` in the right command panel, bound to `PresetNames`, `SelectedPresetName`, `CopyHistory`.

- [ ] **Step 2: Rewrite `MainWindow.xaml.cs` to delegate to services**

Replace the inline history/config/key/sound/stream/command logic with service calls. The class keeps: view-mode state, refresh timer (now choosing interval from `FastPollEnabled`), `Users`/`HistoryEntries` collections, `SearchText`/filter wiring, `TrayService`, `StreamWatcherService`, `DevCommandService`, `FavoritesStore`, `LiveStatsTracker`, `RetryPolicy`, `LiveListConfigCache`, `LiveHistoryStore`. The `RefreshAsync` body becomes:
```csharp
private async Task RefreshAsync()
{
    if (!CanRefresh) return;
    CanRefresh = false;
    StatusText = "Refreshing live list...";
    try
    {
        var resolved = configCache.Resolve();
        var endpoint = resolved.Endpoint;
        if (endpoint is null)
        {
            EndpointText = "Endpoint not configured. Create live-list.local.json beside this app, or set liveFeedbackHeartbeatEndpoint in Crystal Relay runtime config.";
            StatusText = "Waiting for endpoint configuration.";
            LastUpdatedText = "Not refreshed yet.";
            return;
        }

        EndpointText = $"Endpoint: {endpoint}";
        using var response = await httpClient.GetAsync(endpoint);
        response.EnsureSuccessStatusCode();
        await using var stream = await response.Content.ReadAsStreamAsync();
        var payload = await JsonSerializer.DeserializeAsync<LiveListResponse>(stream, JsonOptions);
        var incoming = BuildIncomingUsers(payload);
        var incomingKeys = incoming.Select(u => LiveUserKey.Normalize(u.TwitchUrl, u.DisplayName)).ToList();
        var currentKeys = incomingKeys.ToHashSet(StringComparer.OrdinalIgnoreCase);

        var shouldAlert = hasLoadedLiveSnapshot && currentKeys.Any(k => !knownLiveUserKeys.Contains(k));
        var newFavorites = hasLoadedLiveSnapshot
            ? incoming.Where(u => favorites.IsFavorite(LiveUserKey.Normalize(u.TwitchUrl, u.DisplayName)) && !knownLiveUserKeys.Contains(LiveUserKey.Normalize(u.TwitchUrl, u.DisplayName))).ToList()
            : new List<LiveUserViewModel>();

        // Diff the live list (no Clear/re-add churn).
        var diff = LiveListDiffer.Diff(Users, incoming,
            u => LiveUserKey.Normalize(u.TwitchUrl, u.DisplayName),
            (a, b) => string.Equals(a.RelayVersion, b.RelayVersion, StringComparison.Ordinal)
                     && string.Equals(a.BuildChannel, b.BuildChannel, StringComparison.Ordinal)
                     && a.LastPingAt == b.LastPingAt);
        LiveListDiffer.Apply(Users, diff, u => LiveUserKey.Normalize(u.TwitchUrl, u.DisplayName));

        // History: upsert + dirty-flag save + single sort.
        historyStore.Upsert(incoming, DateTimeOffset.UtcNow);
        historyStore.Prune(DateTimeOffset.UtcNow);
        var sorted = historyStore.SortedSnapshot();
        var histDiff = LiveListDiffer.Diff(HistoryEntries, sorted.Select(r => new LiveHistoryEntryViewModel(r)).ToList(),
            h => LiveUserKey.Normalize(h.TwitchUrl, h.DisplayName),
            (a, b) => a.LastSeenLiveAt == b.LastSeenLiveAt && string.Equals(a.DetailText, b.DetailText, StringComparison.Ordinal));
        LiveListDiffer.Apply(HistoryEntries, histDiff, h => LiveUserKey.Normalize(h.TwitchUrl, h.DisplayName));
        if (historyStore.IsDirty)
        {
            SaveLiveHistory(sorted);
            historyStore.MarkClean();
        }

        // Stats.
        stats.RecordSnapshot(Users.Count, currentKeys);

        // Keys swap (#8).
        knownLiveUserKeys = currentKeys;

        // Alerts + badges + tray.
        if (shouldAlert)
        {
            UpdateUnreadBadge(Users.Count);
            if (SoundAlertsEnabled) PlayLiveSoundAlert();
            tray?.ShowBalloon("Crystal Relay live", $"{Users.Count} users live now.");
            foreach (var fav in newFavorites)
            {
                tray?.ShowBalloon("Favorite live", $"{fav.DisplayName} just went live.");
            }
        }

        retryPolicy.Reset();
        hasLoadedLiveSnapshot = true;
        StatusText = Users.Count == 1 ? "1 Crystal Relay user is live." : $"{Users.Count} Crystal Relay users are live.";
        LastUpdatedText = $"Last updated: {(payload?.UpdatedAt is { } v ? v.ToLocalTime().ToString("g") : DateTimeOffset.Now.ToString("g"))}.";
        ApplySearchFilter();
        RaiseLiveViewPropertiesChanged();
    }
    catch (Exception ex)
    {
        WriteCrashLogSafe("RefreshAsync", ex);
        StatusText = $"Could not load the live list: {ex.Message}";
        LastUpdatedText = $"Last attempt: {DateTimeOffset.Now:g}";
        var delay = retryPolicy.NextDelay();
        ScheduleRetry(delay);
    }
    finally
    {
        CanRefresh = true;
    }
}
```

`ScheduleRetry` sets a one-shot timer that calls `RefreshAsync` after `delay` and then re-arms the regular interval. `UpdateUnreadBadge` sets `TaskbarBadge.ProgressState=Normal` and `ProgressValue` to `min(newCount/10,1)` and a small overlay until the window is activated (clear on `Activated`).

`OnToggleFavoriteClicked` reads `Tag` as TwitchUrl, calls `favorites.Toggle(LiveUserKey.Normalize(url, displayName))`, refreshes the `IsFavorite` flags on view models, and reapplies the filter.

Storyboard pause: in the view-mode setters and `OnStateChanged`, call `DecorativeStoryboard.Begin(this)` or `DecorativeStoryboard.Pause(this)` based on `IsDecorativeBackdropVisible && WindowState != Minimized`.

- [ ] **Step 3: Build the app**

Run: `dotnet build tools\private\crystal-relay-live-list\CrystalRelayLiveList.csproj`
Expected: PASS.

- [ ] **Step 4: Run the full test suite**

Run: `dotnet test tools\private\crystal-relay-live-list-tests\CrystalRelayLiveList.Tests.csproj`
Expected: all tests PASS.

---

## Task 14: Localization audit (#18-adjacent / dev-tool only)

This is a private dev tool with no localized user-facing strings (all text is hardcoded English in XAML). Per AGENTS.md the localization audit applies to the main app, not private dev tools. **No localization changes required.** Skip.

---

## Task 15: Final verification

- [ ] **Step 1: Build the app clean**

Run: `dotnet build tools\private\crystal-relay-live-list\CrystalRelayLiveList.csproj`
Expected: PASS, no warnings/errors.

- [ ] **Step 2: Run all dev-tool tests**

Run: `dotnet test tools\private\crystal-relay-live-list-tests\CrystalRelayLiveList.Tests.csproj`
Expected: all PASS (≈35 tests across Tasks 1-9).

- [ ] **Step 3: Smoke-run the app**

Run the debug launcher is for the main app, not this tool. Launch the built exe directly:
`tools\private\crystal-relay-live-list\bin\Debug\net10.0-windows\CrystalRelayLiveList.exe`
Expected: window opens, decorative animation runs, live list loads (or shows endpoint-not-configured if no local config), tray icon appears, minimizing hides to tray, no unhandled exceptions.

- [ ] **Step 4: No main-program / public-doc / release-script changes**

Confirm `git status` only touches files under `tools/private/crystal-relay-live-list*` and `docs/superpowers/plans/`. No `VrcTwitchOscBridge`, `cloudflare`, release scripts, README, CHANGELOG, or AGENTS.md changes.

---

## Self-review notes

- **Spec coverage:** items 1-19 all map to tasks (1-2→T2/T13; 3,7→T3/T13; 4→T13; 5→T9/T13; 6→T4/T13; 8→T1/T13; 9,10→T12/T13; 11→T13; 12→T13; 13→T10/T13; 14→T7/T13; 15→T13; 16→T6/T13; 17→T8/T13; 18→T11/T13; 19→T5/T13).
- **Placeholder scan:** no TBD/TODO; integration code in T13 shows the actual `RefreshAsync` body and exact XAML changes.
- **Type consistency:** `LiveUserKey.Normalize`, `LiveListDiffer.Diff/Apply`, `LiveHistoryStore.Upsert/Prune/SortedSnapshot/IsDirty/MarkClean`, `LiveListConfigCache.Resolve/Invalidate`, `RetryPolicy.NextDelay/Reset`, `DevCommandService.Build*/RecordCopy/CopyHistory/LoadPresets/SavePreset/DeletePreset`, `FavoritesStore.Toggle/IsFavorite/Keys`, `LiveStatsTracker.RecordSnapshot/PeakLive/CurrentLive/UniqueStreamersSeen`, `StreamWatcherService.EnsureReadyAsync/Navigate/ClearLoginAsync/Stop/Dispose`, `TrayService.ShowBalloon/Dispose` — names match across tasks.
