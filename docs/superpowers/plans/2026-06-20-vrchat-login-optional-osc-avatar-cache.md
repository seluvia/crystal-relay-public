# VRChat-Optional OSC Avatar Cache Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Allow Crystal Relay to detect the current VRChat avatar and look up its name without a working VRChat login, by persisting the LocalLow-derived avatar data and subscribing to OSC `/avatar/change`. Expose the connection state in the UI with a status pill.

**Architecture:** Five new behaviors are added on top of the existing LocalLow watcher and avatar cache: (1) a new `TryInferUserIdFromLocalLowAsync` helper, (2) a new `VrChatOscAvatarChangeReceived` event raised by `BridgeCoordinator` when `/avatar/change` is observed, (3) a new `HandleIncomingOscAvatarChangeAsync` flow that mirrors the existing API-driven path, (4) a `ScanLocalVrChatOscAvatarCacheAsync` step that now persists the merged in-memory list, and (5) a refactored 401 cleanup that keeps LocalLow-sourced entries and marks the app as `Cached`. A new `VrChatConnectionState` enum and a small UI pill surface the state.

**Tech Stack:** C# / .NET 10, WPF, xUnit, `ProtectedData` for cache encryption, existing OSCQuery and OSC packet plumbing.

---

## File Structure

This change is mostly additive and touches these files:

| File | Change |
|---|---|
| `VrcTwitchOscBridge/Services/VrChatLocalOscCacheService.cs` | Add `TryInferUserIdFromLocalLowAsync` public static helper. |
| `VrcTwitchOscBridge/Services/BridgeCoordinator.cs` | Add `VrChatOscAvatarChangeReceived` event; add `/avatar/change` branch to `ObserveOscValue`. |
| `VrcTwitchOscBridge/Models/VrChatConnectionState.cs` | New enum: `LoggedIn | Cached | NoData`. |
| `VrcTwitchOscBridge/ViewModels/MainWindowViewModel.cs` | Add `HandleIncomingOscAvatarChangeAsync`, `HandleVrChatUnauthorizedAsync`, `ResolveCurrentUserIdForCache`, `VrChatConnectionState` property, and `VrChatConnectionStateLabel/Brush/Tooltip` properties. Persist on every `ScanLocalVrChatOscAvatarCacheAsync`. Reload cache on startup without a saved session. Re-audit 401 cleanup. |
| `VrcTwitchOscBridge/MainWindow.xaml` | Add a small `TextBlock` next to the existing `VrChatStatus` text that shows the `VrChatConnectionState` label. |
| `VrcTwitchOscBridge/Resources/Localization/en-US.json` | Add 6 new keys (3 labels + 3 tooltips). |
| `VrcTwitchOscBridge/Resources/Localization/*.json` (12 other locales) | Mirror the 6 new keys per the AGENTS.md translation rules. |
| `VrcTwitchOscBridge.Tests/VrChatLocalOscCacheServiceUserIdInferenceTests.cs` | New test file. |
| `VrcTwitchOscBridge.Tests/VrChatConnectionStateTests.cs` | New test file. |
| `VrcTwitchOscBridge.Tests/SettingsStoreVrChatAvatarCacheTests.cs` | New test file (extends existing SettingsStore tests if any). |

No file is split or restructured; the change stays within the existing patterns.

---

## Task 1: Add the `TryInferUserIdFromLocalLowAsync` helper (with TDD)

**Files:**
- Modify: `VrcTwitchOscBridge/Services/VrChatLocalOscCacheService.cs`
- Create: `VrcTwitchOscBridge.Tests/VrChatLocalOscCacheServiceUserIdInferenceTests.cs`

- [ ] **Step 1: Write the failing tests**

Create `VrcTwitchOscBridge.Tests/VrChatLocalOscCacheServiceUserIdInferenceTests.cs`:

```csharp
using System;
using System.IO;
using VrcTwitchOscBridge.Services;
using Xunit;

namespace VrcTwitchOscBridge.Tests;

public sealed class VrChatLocalOscCacheServiceUserIdInferenceTests : IDisposable
{
    private readonly string tempRoot;

    public VrChatLocalOscCacheServiceUserIdInferenceTests()
    {
        tempRoot = Path.Combine(Path.GetTempPath(), "cr-userid-test-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(tempRoot);
    }

    public void Dispose()
    {
        if (Directory.Exists(tempRoot))
        {
            Directory.Delete(tempRoot, recursive: true);
        }
    }

    private string CreateOscUser(string userId, DateTime lastWriteTimeUtc)
    {
        var dir = Path.Combine(tempRoot, "OSC", userId, "Avatars");
        Directory.CreateDirectory(dir);
        // Touch the user folder so Directory.GetLastWriteTimeUtc returns a known value.
        File.WriteAllText(Path.Combine(dir, ".touch"), "x");
        Directory.SetLastWriteTimeUtc(dir, lastWriteTimeUtc);
        return userId;
    }

    [Fact]
    public void TryInferUserIdFromLocalLowAsync_WhenNoOscFolder_ReturnsNull()
    {
        // No OSC folder at all under the temp root.
        var result = VrChatLocalOscCacheService.TryInferUserIdFromLocalLowInRootAsync(tempRoot);
        Assert.Null(result);
    }

    [Fact]
    public void TryInferUserIdFromLocalLowAsync_WhenSingleUserFolder_ReturnsThatUserId()
    {
        var expected = CreateOscUser("usr_single", DateTime.UtcNow);
        var result = VrChatLocalOscCacheService.TryInferUserIdFromLocalLowInRootAsync(tempRoot);
        Assert.Equal(expected, result);
    }

    [Fact]
    public void TryInferUserIdFromLocalLowAsync_WhenMultipleUserFolders_ReturnsMostRecentlyModified()
    {
        var older = DateTime.UtcNow.AddDays(-2);
        var newer = DateTime.UtcNow.AddDays(-1);
        CreateOscUser("usr_older", older);
        var expected = CreateOscUser("usr_newer", newer);
        var result = VrChatLocalOscCacheService.TryInferUserIdFromLocalLowInRootAsync(tempRoot);
        Assert.Equal(expected, result);
    }
}
```

- [ ] **Step 2: Run tests to verify they fail**

Run from the repo root:
```bash
dotnet test "E:\!!!Program to work on\Proper Crystal Relay\VrcTwitchOscBridge.Tests\VrcTwitchOscBridge.Tests.csproj" --filter "FullyQualifiedName~VrChatLocalOscCacheServiceUserIdInferenceTests"
```
Expected: BUILD error or test failures because `TryInferUserIdFromLocalLowInRootAsync` does not exist.

- [ ] **Step 3: Add the helper to `VrChatLocalOscCacheService`**

In `VrcTwitchOscBridge/Services/VrChatLocalOscCacheService.cs`, add the following public static method (place it next to the other `GetAvatarOscFolderPath` helpers around line 153):

```csharp
// Resolves the most likely VRChat user id by looking at the OSC user folders
// under a given LocalLow-style root. Returns the single folder name when
// there is one, the most-recently-modified folder when there are several,
// or null when there are none (or the root is unreadable). The test-friendly
// overload accepts an explicit root path; the public no-arg overload uses
// %USERPROFILE%\AppData\LocalLow\VRChat\VRChat.
public static string? TryInferUserIdFromLocalLowInRootAsync(string rootPath)
{
    if (string.IsNullOrWhiteSpace(rootPath) || !Directory.Exists(rootPath))
    {
        return null;
    }

    var oscRoot = Path.Combine(rootPath, "OSC");
    if (!Directory.Exists(oscRoot))
    {
        return null;
    }

    string[] userDirs;
    try
    {
        userDirs = Directory.GetDirectories(oscRoot, "usr_*");
    }
    catch
    {
        return null;
    }

    if (userDirs.Length == 0) return null;
    if (userDirs.Length == 1) return Path.GetFileName(userDirs[0]);

    // multiple — pick the most recently modified and log a soft warning
    var newest = userDirs
        .OrderByDescending(d =>
        {
            try { return Directory.GetLastWriteTimeUtc(d); }
            catch { return DateTime.MinValue; }
        })
        .First();
    return Path.GetFileName(newest);
}

public static Task<string?> TryInferUserIdFromLocalLowAsync(CancellationToken ct = default)
{
    ct.ThrowIfCancellationRequested();
    var root = VrChatLocalClientStateService.GetVrChatRootPath();
    return Task.FromResult(TryInferUserIdFromLocalLowInRootAsync(root));
}
```

Add `using System.Linq;` and `using System.Threading;` and `using System.Threading.Tasks;` at the top of the file if not already present.

- [ ] **Step 4: Run tests to verify they pass**

```bash
dotnet test "E:\!!!Program to work on\Proper Crystal Relay\VrcTwitchOscBridge.Tests\VrcTwitchOscBridge.Tests.csproj" --filter "FullyQualifiedName~VrChatLocalOscCacheServiceUserIdInferenceTests"
```
Expected: 3 tests pass.

- [ ] **Step 5: Commit**

```bash
git add VrcTwitchOscBridge/Services/VrChatLocalOscCacheService.cs VrcTwitchOscBridge.Tests/VrChatLocalOscCacheServiceUserIdInferenceTests.cs
git commit -m "feat(vrchat): add TryInferUserIdFromLocalLowAsync helper"
```

---

## Task 2: Add the `VrChatOscAvatarChangeReceived` event in `BridgeCoordinator`

**Files:**
- Modify: `VrcTwitchOscBridge/Services/BridgeCoordinator.cs`

This task adds the event and a small parser method. The wiring of the event to `MainWindowViewModel` happens in Task 4.

- [ ] **Step 1: Locate the event declaration area**

Open `VrcTwitchOscBridge/Services/BridgeCoordinator.cs`. Find the area near line 256 where `ObservedValueReceived` is wired (`oscRouterService.ObservedValueReceived += observedValue => ObserveOscValue(observedValue);`). Just above the `ObserveOscValue` method (around line 17114), there should be an events section or the events are declared near the top of the class. Find an existing event like `public event Action<...>? AvatarScaleStatusChanged;` and add the new event next to it.

- [ ] **Step 2: Add the new event declaration**

Add the following public event next to the other events (e.g., right after `AvatarScaleStatusChanged`):

```csharp
public event Action<string>? VrChatOscAvatarChangeReceived;
```

- [ ] **Step 3: Add the `/avatar/change` branch to `ObserveOscValue`**

In `ObserveOscValue` (around line 17114), add the following branch at the very top of the method, before any other checks:

```csharp
if (string.Equals(observedValue.Address, "/avatar/change", StringComparison.Ordinal))
{
    var avatarId = observedValue.StringValue?.Trim();
    if (!string.IsNullOrEmpty(avatarId) && avatarId.StartsWith("avtr_", StringComparison.Ordinal))
    {
        try
        {
            VrChatOscAvatarChangeReceived?.Invoke(avatarId);
        }
        catch
        {
            // subscriber exceptions must not break the OSC receive loop
        }
    }
    return;
}
```

The `using System;` should already be present at the top of the file.

- [ ] **Step 4: Build to verify compilation**

```bash
dotnet build "E:\!!!Program to work on\Proper Crystal Relay\VrcTwitchOscBridge\VrcTwitchOscBridge.csproj" --no-restore
```
Expected: build succeeds with no errors.

- [ ] **Step 5: Commit**

```bash
git add VrcTwitchOscBridge/Services/BridgeCoordinator.cs
git commit -m "feat(vrchat): add /avatar/change observation branch in BridgeCoordinator"
```

---

## Task 3: Add the `VrChatConnectionState` enum

**Files:**
- Create: `VrcTwitchOscBridge/Models/VrChatConnectionState.cs`

- [ ] **Step 1: Create the enum file**

Create `VrcTwitchOscBridge/Models/VrChatConnectionState.cs`:

```csharp
namespace VrcTwitchOscBridge.Models;

public enum VrChatConnectionState
{
    NoData,
    Cached,
    LoggedIn,
}
```

- [ ] **Step 2: Register the file in the project**

Open `VrcTwitchOscBridge/VrcTwitchOscBridge.csproj` and add the new file to the appropriate `<ItemGroup>`. The project has `EnableDefaultCompileItems=false` and lists files explicitly, so the new file must be added under the existing pattern. Find a sibling model like `<Compile Include="Models\VrChatAvatarSummary.cs" />` and add right next to it:

```xml
<Compile Include="Models\VrChatConnectionState.cs" />
```

- [ ] **Step 3: Build to verify**

```bash
dotnet build "E:\!!!Program to work on\Proper Crystal Relay\VrcTwitchOscBridge\VrcTwitchOscBridge.csproj" --no-restore
```
Expected: build succeeds.

- [ ] **Step 4: Commit**

```bash
git add VrcTwitchOscBridge/Models/VrChatConnectionState.cs VrcTwitchOscBridge/VrcTwitchOscBridge.csproj
git commit -m "feat(vrchat): add VrChatConnectionState enum"
```

---

## Task 4: Add `ResolveCurrentUserIdForCache` and the wiring in `MainWindowViewModel`

**Files:**
- Modify: `VrcTwitchOscBridge/ViewModels/MainWindowViewModel.cs`

This task adds the helper that resolves the user id used as the cache scope, the `VrChatOscAvatarChangeReceived` subscription, and the `VrChatConnectionState` property.

- [ ] **Step 1: Find the constructor and add a subscription**

Open `ViewModels/MainWindowViewModel.cs` and find the constructor. The existing wiring of `oscRouterService.ObservedValueReceived` lives in `BridgeCoordinator` (line 256). Inside `MainWindowViewModel`'s constructor, find the place where other `bridgeCoordinator.X += ...` subscriptions are added (search for `bridgeCoordinator.`). Right after them, add:

```csharp
bridgeCoordinator.VrChatOscAvatarChangeReceived += HandleIncomingOscAvatarChangeSync;
```

The `HandleIncomingOscAvatarChangeSync` method will be added in Task 5. For now, add a stub:

```csharp
private void HandleIncomingOscAvatarChangeSync(string avatarId)
{
    // Implemented in Task 5: HandleIncomingOscAvatarChangeAsync.
    _ = HandleIncomingOscAvatarChangeAsync(avatarId, CancellationToken.None);
}
```

Add the same using statements already in the file: `using VrcTwitchOscBridge.Services;` and `using VrcTwitchOscBridge.Models;` (if not already present).

- [ ] **Step 2: Add the `ResolveCurrentUserIdForCache` helper**

Place it next to the other private helpers near the top of the class:

```csharp
private string? inferredLocalLowUserId;

private string? ResolveCurrentUserIdForCache()
{
    if (!string.IsNullOrWhiteSpace(Settings.VrChat.UserId))
    {
        return Settings.VrChat.UserId;
    }
    if (!string.IsNullOrWhiteSpace(inferredLocalLowUserId))
    {
        return inferredLocalLowUserId;
    }
    var inferred = VrChatLocalOscCacheService.TryInferUserIdFromLocalLowInRootAsync(
        VrChatLocalClientStateService.GetVrChatRootPath());
    inferredLocalLowUserId = inferred;
    return inferred;
}

private void InvalidateInferredLocalLowUserId()
{
    inferredLocalLowUserId = null;
}
```

Add `InvalidateInferredLocalLowUserId()` to the existing 401 cleanup so the next call re-infers.

- [ ] **Step 3: Add the `VrChatConnectionState` computed property**

Find where the other `[ObservableProperty]` or public properties live in `MainWindowViewModel`. The simplest form is a regular property with `OnPropertyChanged`. Place it near the existing `VrChatStatus` property (around line 2646):

```csharp
private VrChatConnectionState vrChatConnectionState = VrChatConnectionState.NoData;
public VrChatConnectionState VrChatConnectionState
{
    get => vrChatConnectionState;
    private set
    {
        if (vrChatConnectionState != value)
        {
            vrChatConnectionState = value;
            OnPropertyChanged();
            OnPropertyChanged(nameof(VrChatConnectionStateLabel));
            OnPropertyChanged(nameof(VrChatConnectionStateTooltip));
            OnPropertyChanged(nameof(VrChatConnectionStateBrush));
        }
    }
}
```

- [ ] **Step 4: Add the supporting label / tooltip / brush properties**

```csharp
public string VrChatConnectionStateLabel => VrChatConnectionState switch
{
    VrChatConnectionState.LoggedIn => T("VRChat: Logged in"),
    VrChatConnectionState.Cached => T("VRChat: Cached"),
    _ => T("VRChat: No data"),
};

public string VrChatConnectionStateTooltip => VrChatConnectionState switch
{
    VrChatConnectionState.LoggedIn => T("Connected to VRChat. Avatar names and current avatar are fetched from the live API."),
    VrChatConnectionState.Cached => T("VRChat login is unavailable. Crystal Relay is using the cached avatar list and detecting the current avatar via OSC and LocalLow files."),
    _ => T("No avatar data is available. Log in to VRChat or visit an avatar in VRChat to build the cache."),
};

public System.Windows.Media.Brush VrChatConnectionStateBrush => VrChatConnectionState switch
{
    VrChatConnectionState.LoggedIn => System.Windows.Media.Brushes.LimeGreen,
    VrChatConnectionState.Cached => System.Windows.Media.Brushes.Goldenrod,
    _ => System.Windows.Media.Brushes.Gray,
};
```

Note: the project already has a `T(string)` and `TF(string, args)` helper for localization. If `T` is a different name in this file, use the existing helper.

- [ ] **Step 5: Add the state recomputation helper**

```csharp
private void RecomputeVrChatConnectionState()
{
    VrChatConnectionState newState;
    if (!string.IsNullOrWhiteSpace(Settings.VrChat.AuthCookie))
    {
        newState = VrChatConnectionState.LoggedIn;
    }
    else if (availableVrChatAvatars.Count > 0)
    {
        newState = VrChatConnectionState.Cached;
    }
    else
    {
        newState = VrChatConnectionState.NoData;
    }
    VrChatConnectionState = newState;
}
```

- [ ] **Step 6: Build to verify**

```bash
dotnet build "E:\!!!Program to work on\Proper Crystal Relay\VrcTwitchOscBridge\VrcTwitchOscBridge.csproj" --no-restore
```
Expected: build succeeds with no errors. (The `HandleIncomingOscAvatarChangeAsync` method referenced in the stub does not exist yet — that's OK because the stub only assigns the result of an async call to `_`. The C# compiler will warn but the build will succeed. Task 5 adds the real method.)

If the build fails because `HandleIncomingOscAvatarChangeAsync` is undefined, add the following empty method temporarily and replace it in Task 5:

```csharp
private Task HandleIncomingOscAvatarChangeAsync(string avatarId, CancellationToken ct) => Task.CompletedTask;
```

- [ ] **Step 7: Commit**

```bash
git add VrcTwitchOscBridge/ViewModels/MainWindowViewModel.cs
git commit -m "feat(vrchat): add VrChatConnectionState property and cache user id resolver"
```

---

## Task 5: Implement `HandleIncomingOscAvatarChangeAsync` (with TDD)

**Files:**
- Create: `VrcTwitchOscBridge.Tests/VrChatConnectionStateTests.cs`
- Modify: `VrcTwitchOscBridge/ViewModels/MainWindowViewModel.cs`

The full `MainWindowViewModel` is heavily wired; for the new test file we will only test the parts that can run in isolation. For this task we will test the parsing / merge logic by extracting it into a small static helper that the viewmodel calls. This keeps the test independent of WPF dispatcher and Twitch services.

- [ ] **Step 1: Add a small static merge helper**

Create a new file `VrcTwitchOscBridge/Services/OscAvatarChangeMerger.cs`:

```csharp
using System;
using System.Collections.Generic;
using VrcTwitchOscBridge.Models;

namespace VrcTwitchOscBridge.Services;

internal static class OscAvatarChangeMerger
{
    public static IReadOnlyList<VrChatAvatarSummary> MergeIntoList(
        IReadOnlyList<VrChatAvatarSummary> existing,
        string avatarId,
        string resolvedName,
        string newSourceLabel)
    {
        if (string.IsNullOrWhiteSpace(avatarId)) return existing;
        if (!avatarId.StartsWith("avtr_", StringComparison.Ordinal)) return existing;

        var result = new List<VrChatAvatarSummary>(existing);
        var idx = result.FindIndex(a => string.Equals(a.Id, avatarId, StringComparison.Ordinal));
        var finalName = string.IsNullOrWhiteSpace(resolvedName) ? avatarId : resolvedName;

        if (idx < 0)
        {
            result.Add(new VrChatAvatarSummary(
                Id: avatarId,
                Name: finalName,
                SourceLabel: newSourceLabel,
                IsCurrentAvatar: false,
                ThumbnailUrl: null));
        }
        else
        {
            var current = result[idx];
            // Adopt the new name if the existing one is empty or just the avatar id
            // (i.e., the better-known name wins).
            var shouldAdopt = string.IsNullOrWhiteSpace(current.Name)
                || string.Equals(current.Name, current.Id, StringComparison.Ordinal);
            if (shouldAdopt || !string.Equals(current.Name, finalName, StringComparison.Ordinal))
            {
                result[idx] = current with
                {
                    Name = shouldAdopt ? finalName : current.Name,
                    SourceLabel = shouldAdopt ? newSourceLabel : current.SourceLabel,
                };
            }
        }
        return result;
    }
}
```

Add the file to the project (`VrcTwitchOscBridge.csproj`):

```xml
<Compile Include="Services\OscAvatarChangeMerger.cs" />
```

- [ ] **Step 2: Write the failing tests for the merger**

Create `VrcTwitchOscBridge.Tests/VrChatConnectionStateTests.cs`:

```csharp
using System.Collections.Generic;
using VrcTwitchOscBridge.Models;
using VrcTwitchOscBridge.Services;
using Xunit;

namespace VrcTwitchOscBridge.Tests;

public sealed class VrChatConnectionStateTests
{
    [Fact]
    public void MergeIntoList_EmptyList_AddsNewEntry()
    {
        var result = OscAvatarChangeMerger.MergeIntoList(
            existing: new List<VrChatAvatarSummary>(),
            avatarId: "avtr_abc",
            resolvedName: "My Avatar",
            newSourceLabel: "Local OSC");
        Assert.Single(result);
        Assert.Equal("avtr_abc", result[0].Id);
        Assert.Equal("My Avatar", result[0].Name);
        Assert.Equal("Local OSC", result[0].SourceLabel);
    }

    [Fact]
    public void MergeIntoList_NewIdWithBlankName_FallsBackToId()
    {
        var result = OscAvatarChangeMerger.MergeIntoList(
            existing: new List<VrChatAvatarSummary>(),
            avatarId: "avtr_abc",
            resolvedName: "",
            newSourceLabel: "Local OSC");
        Assert.Equal("avtr_abc", result[0].Name);
    }

    [Fact]
    public void MergeIntoList_ExistingEntryWithIdAsName_AdoptsBetterName()
    {
        var existing = new List<VrChatAvatarSummary>
        {
            new("avtr_abc", "avtr_abc", "Local OSC", false, null),
        };
        var result = OscAvatarChangeMerger.MergeIntoList(
            existing, "avtr_abc", "Real Name", "Local OSC");
        Assert.Single(result);
        Assert.Equal("Real Name", result[0].Name);
    }

    [Fact]
    public void MergeIntoList_ExistingEntryWithBetterName_PreservesIt()
    {
        var existing = new List<VrChatAvatarSummary>
        {
            new("avtr_abc", "Real Name", "Uploaded", false, null),
        };
        var result = OscAvatarChangeMerger.MergeIntoList(
            existing, "avtr_abc", "Other Name", "Local OSC");
        Assert.Single(result);
        Assert.Equal("Real Name", result[0].Name);
        Assert.Equal("Uploaded", result[0].SourceLabel);
    }

    [Fact]
    public void MergeIntoList_EmptyAvatarId_ReturnsUnchanged()
    {
        var existing = new List<VrChatAvatarSummary>
        {
            new("avtr_abc", "Real Name", "Uploaded", false, null),
        };
        var result = OscAvatarChangeMerger.MergeIntoList(
            existing, "", "X", "Local OSC");
        Assert.Same(existing, result);
    }

    [Fact]
    public void MergeIntoList_MalformedId_ReturnsUnchanged()
    {
        var existing = new List<VrChatAvatarSummary>
        {
            new("avtr_abc", "Real Name", "Uploaded", false, null),
        };
        var result = OscAvatarChangeMerger.MergeIntoList(
            existing, "not_an_avatar_id", "X", "Local OSC");
        Assert.Same(existing, result);
    }
}
```

- [ ] **Step 3: Run the tests to verify they fail**

```bash
dotnet test "E:\!!!Program to work on\Proper Crystal Relay\VrcTwitchOscBridge.Tests\VrcTwitchOscBridge.Tests.csproj" --filter "FullyQualifiedName~VrChatConnectionStateTests"
```
Expected: build error or test failures because `OscAvatarChangeMerger` does not exist.

- [ ] **Step 4: Implement the merger (already done in Step 1)**

Re-run:

```bash
dotnet test "E:\!!!Program to work on\Proper Crystal Relay\VrcTwitchOscBridge.Tests\VrcTwitchOscBridge.Tests.csproj" --filter "FullyQualifiedName~VrChatConnectionStateTests"
```
Expected: 6 tests pass.

- [ ] **Step 5: Replace the stub method in `MainWindowViewModel`**

Find the `HandleIncomingOscAvatarChangeAsync` stub added in Task 4 and replace it with the real implementation:

```csharp
private async Task HandleIncomingOscAvatarChangeAsync(string avatarId, CancellationToken ct)
{
    if (!avatarId.StartsWith("avtr_", StringComparison.Ordinal)) return;

    // 1. Resolve the name from the in-memory map; if found, skip the file lookup.
    string? resolvedName = null;
    if (availableVrChatAvatarNamesById.TryGetValue(avatarId, out var existingName) &&
        !string.IsNullOrWhiteSpace(existingName) &&
        !string.Equals(existingName, avatarId, StringComparison.Ordinal))
    {
        resolvedName = existingName;
    }

    // 2. If not in memory, walk LocalLow for the JSON.
    if (resolvedName is null)
    {
        var inferredUserId = await Task.Run(
            () => VrChatLocalOscCacheService.TryInferUserIdFromLocalLowInRootAsync(
                VrChatLocalClientStateService.GetVrChatRootPath()),
            ct).ConfigureAwait(false);
        if (!string.IsNullOrEmpty(inferredUserId))
        {
            var known = await vrChatLocalOscCacheService
                .LoadKnownAvatarsAsync(inferredUserId, ct)
                .ConfigureAwait(false);
            var match = known.FirstOrDefault(a =>
                string.Equals(a.AvatarId, avatarId, StringComparison.Ordinal));
            if (match is not null &&
                !string.IsNullOrWhiteSpace(match.AvatarName) &&
                !string.Equals(match.AvatarName, avatarId, StringComparison.Ordinal))
            {
                resolvedName = match.AvatarName;
            }
        }
    }

    // 3. Merge into the in-memory list using the static helper.
    var merged = OscAvatarChangeMerger.MergeIntoList(
        availableVrChatAvatars,
        avatarId,
        resolvedName ?? string.Empty,
        "Local OSC");
    ReplaceAvailableVrChatAvatars(merged);

    // 4. Persist (best-effort).
    var currentUserId = ResolveCurrentUserIdForCache();
    if (!string.IsNullOrEmpty(currentUserId))
    {
        try
        {
            await settingsStore
                .SaveVrChatAvatarCacheAsync(currentUserId, availableVrChatAvatars, ct)
                .ConfigureAwait(false);
        }
        catch
        {
            // best-effort; do not break the OSC flow
        }
    }

    // 5. Drive the existing downstream flow.
    await HandleVrChatAvatarChangedByBridge(avatarId, queueManagedRewardSync: true)
        .ConfigureAwait(false);

    // 6. Re-evaluate the connection state.
    RecomputeVrChatConnectionState();
}
```

Add `using System.Linq;` if not already present.

- [ ] **Step 6: Build and re-run all tests**

```bash
dotnet build "E:\!!!Program to work on\Proper Crystal Relay\VrcTwitchOscBridge\VrcTwitchOscBridge.csproj" --no-restore
dotnet test "E:\!!!Program to work on\Proper Crystal Relay\VrcTwitchOscBridge.Tests\VrcTwitchOscBridge.Tests.csproj"
```
Expected: build succeeds, all tests pass.

- [ ] **Step 7: Commit**

```bash
git add VrcTwitchOscBridge/Services/OscAvatarChangeMerger.cs VrcTwitchOscBridge/ViewModels/MainWindowViewModel.cs VrcTwitchOscBridge.Tests/VrChatConnectionStateTests.cs VrcTwitchOscBridge/VrcTwitchOscBridge.csproj
git commit -m "feat(vrchat): implement HandleIncomingOscAvatarChangeAsync with merge helper"
```

---

## Task 6: Persist on every LocalLow watcher event

**Files:**
- Modify: `VrcTwitchOscBridge/ViewModels/MainWindowViewModel.cs`

- [ ] **Step 1: Locate `ScanLocalVrChatOscAvatarCacheAsync`**

In `MainWindowViewModel.cs`, find the method around line 11506-11572. The method is `async Task ScanLocalVrChatOscAvatarCacheAsync(CancellationToken ct)`. Inside, after the call to `ApplyLocalVrChatOscAvatars` (or `MergeLocalVrChatAvatars` — the line is around 11566), add the persist call.

- [ ] **Step 2: Add the persist call**

At the end of the method, just before the final `return` (or at the end of the try block), add:

```csharp
// Persist the merged in-memory list so LocalLow-sourced names survive restarts.
var persistUserId = ResolveCurrentUserIdForCache();
if (!string.IsNullOrEmpty(persistUserId))
{
    try
    {
        await settingsStore
            .SaveVrChatAvatarCacheAsync(persistUserId, availableVrChatAvatars, CancellationToken.None)
            .ConfigureAwait(false);
    }
    catch
    {
        // best-effort
    }
}

RecomputeVrChatConnectionState();
```

If the method body uses `ct` instead of `CancellationToken.None` for the save, use `ct`. The watcher already debounces; the save runs at most once per debounced scan.

- [ ] **Step 3: Build and run tests**

```bash
dotnet build "E:\!!!Program to work on\Proper Crystal Relay\VrcTwitchOscBridge\VrcTwitchOscBridge.csproj" --no-restore
dotnet test "E:\!!!Program to work on\Proper Crystal Relay\VrcTwitchOscBridge.Tests\VrcTwitchOscBridge.Tests.csproj"
```
Expected: build succeeds, all tests pass.

- [ ] **Step 4: Commit**

```bash
git add VrcTwitchOscBridge/ViewModels/MainWindowViewModel.cs
git commit -m "feat(vrchat): persist avatar cache after every LocalLow scan"
```

---

## Task 7: Refactor 401 cleanup to keep LocalLow entries

**Files:**
- Modify: `VrcTwitchOscBridge/ViewModels/MainWindowViewModel.cs`

- [ ] **Step 1: Locate both 401 cleanup sites**

Find the two places where `VrChatApiException` with `StatusCode == Unauthorized` is handled:
- `MainWindowViewModel.cs:4223-4224` (in `RefreshVrChatAvatarsAsync`)
- `MainWindowViewModel.cs:11383-11404` (in `RefreshCurrentVrChatAvatarFromApiAsync`)

- [ ] **Step 2: Extract a new `HandleVrChatUnauthorizedAsync` method**

Add the following private async method near the other 401-related methods in the class:

```csharp
private async Task HandleVrChatUnauthorizedAsync(CancellationToken ct)
{
    // 1. Filter the in-memory list to LocalLow-sourced entries only.
    var localOnly = availableVrChatAvatars
        .Where(a => string.Equals(a.SourceLabel, "Local OSC", StringComparison.Ordinal))
        .ToList();

    // 2. Replace the in-memory list.
    ReplaceAvailableVrChatAvatars(localOnly);

    // 3. Persist the filtered list (keeps LocalLow, drops API).
    var userId = ResolveCurrentUserIdForCache();
    if (!string.IsNullOrEmpty(userId))
    {
        try
        {
            await settingsStore
                .SaveVrChatAvatarCacheAsync(userId, localOnly, ct)
                .ConfigureAwait(false);
        }
        catch
        {
            // best-effort
        }
    }

    // 4. Clear auth state. (Do NOT clear CurrentAvatarId — the bridge keeps
    //    tracking the last known avatar until the next /avatar/change.)
    Settings.VrChat.AuthCookie = string.Empty;
    Settings.VrChat.UserId = string.Empty;
    Settings.VrChat.DisplayName = string.Empty;

    // 5. OSC parameter cache and in-memory parameter dict stay as-is —
    //    they are LocalLow-sourced and remain valid.

    // 6. Re-evaluate the connection state.
    RecomputeVrChatConnectionState();
    InvalidateInferredLocalLowUserId();
}
```

- [ ] **Step 3: Replace the two 401 cleanup blocks with a call to the new method**

In `RefreshVrChatAvatarsAsync` (around line 4223-4224), replace the three cleanup lines with:

```csharp
await HandleVrChatUnauthorizedAsync(ct).ConfigureAwait(false);
```

Do the same in `RefreshCurrentVrChatAvatarFromApiAsync` (around line 11383-11404), but keep the surrounding `VrChatStatus = ...` log message. The new code should be:

```csharp
AppendLog(T("Saved VRChat session expired. Connect again to reload avatars."));
await HandleVrChatUnauthorizedAsync(ct).ConfigureAwait(false);
```

If the `VrChatStatus` is set on a different line, leave that line untouched — only replace the three `Clear...Async` calls and the `cachedVrChatParametersByAvatarId.Clear()` with the single new method call.

- [ ] **Step 4: Build to verify**

```bash
dotnet build "E:\!!!Program to work on\Proper Crystal Relay\VrcTwitchOscBridge\VrcTwitchOscBridge.csproj" --no-restore
```
Expected: build succeeds.

- [ ] **Step 5: Commit**

```bash
git add VrcTwitchOscBridge/ViewModels/MainWindowViewModel.cs
git commit -m "feat(vrchat): refactor 401 cleanup to keep LocalLow entries"
```

---

## Task 8: Add a round-trip test for the SettingsStore avatar cache

**Files:**
- Create: `VrcTwitchOscBridge.Tests/SettingsStoreVrChatAvatarCacheTests.cs`

- [ ] **Step 1: Write the failing test**

```csharp
using System;
using System.Collections.Generic;
using System.IO;
using System.Threading;
using System.Threading.Tasks;
using VrcTwitchOscBridge.Models;
using VrcTwitchOscBridge.Services;
using Xunit;

namespace VrcTwitchOscBridge.Tests;

public sealed class SettingsStoreVrChatAvatarCacheTests : IDisposable
{
    private readonly string tempRoot;

    public SettingsStoreVrChatAvatarCacheTests()
    {
        tempRoot = Path.Combine(Path.GetTempPath(), "cr-settings-test-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(tempRoot);
    }

    public void Dispose()
    {
        if (Directory.Exists(tempRoot))
        {
            Directory.Delete(tempRoot, recursive: true);
        }
    }

    [Fact]
    public async Task SaveAndLoadAvatarCache_PreservesLocalLowEntries()
    {
        var store = new SettingsStore(tempRoot);
        var avatars = new List<VrChatAvatarSummary>
        {
            new("avtr_1", "API Name 1", "Uploaded", true, null),
            new("avtr_2", "Local Name 2", "Local OSC", false, null),
            new("avtr_3", "Fav Name 3", "Favorites", false, null),
        };

        await store.SaveVrChatAvatarCacheAsync("usr_test", avatars, CancellationToken.None);
        var loaded = await store.LoadVrChatAvatarCacheAsync("usr_test", CancellationToken.None);

        Assert.Equal(3, loaded.Count);
        var byId = loaded.ToDictionary(a => a.Id, StringComparer.Ordinal);
        Assert.Equal("API Name 1", byId["avtr_1"].Name);
        Assert.Equal("Uploaded", byId["avtr_1"].SourceLabel);
        Assert.Equal("Local Name 2", byId["avtr_2"].Name);
        Assert.Equal("Local OSC", byId["avtr_2"].SourceLabel);
        Assert.Equal("Fav Name 3", byId["avtr_3"].Name);
        Assert.Equal("Favorites", byId["avtr_3"].SourceLabel);
    }

    [Fact]
    public async Task LoadAvatarCache_WithMismatchedUserId_ReturnsEmpty()
    {
        var store = new SettingsStore(tempRoot);
        var avatars = new List<VrChatAvatarSummary>
        {
            new("avtr_1", "Name", "Local OSC", false, null),
        };
        await store.SaveVrChatAvatarCacheAsync("usr_one", avatars, CancellationToken.None);
        var loaded = await store.LoadVrChatAvatarCacheAsync("usr_two", CancellationToken.None);
        Assert.Empty(loaded);
    }
}
```

Note: the test assumes `SettingsStore` has a constructor that accepts the app data root path. If it does not, look at how existing tests in the project construct `SettingsStore` (the existing project may not have any SettingsStore tests — that's fine). If the constructor signature is different, adjust the test to use the real signature. The `using System.Linq;` for `.ToDictionary(...)` is needed.

- [ ] **Step 2: Run the test**

```bash
dotnet test "E:\!!!Program to work on\Proper Crystal Relay\VrcTwitchOscBridge.Tests\VrcTwitchOscBridge.Tests.csproj" --filter "FullyQualifiedName~SettingsStoreVrChatAvatarCacheTests"
```
Expected: 2 tests pass. (If the test cannot construct `SettingsStore` due to Windows-specific `ProtectedData` calls on a non-Windows test environment, the test must be marked with `[Trait("Category","WindowsOnly")]` or `[Fact(Skip="...")]`. On Windows it will pass; on non-Windows CI it will be skipped. This is acceptable — the file-level tests for the helper logic in Task 1 already cover the cross-platform logic.)

- [ ] **Step 3: Commit**

```bash
git add VrcTwitchOscBridge.Tests/SettingsStoreVrChatAvatarCacheTests.cs
git commit -m "test: add SettingsStore avatar cache round-trip tests"
```

---

## Task 9: Update the startup path to load cache without login

**Files:**
- Modify: `VrcTwitchOscBridge/ViewModels/MainWindowViewModel.cs`

- [ ] **Step 1: Locate the startup path**

Find the startup block around `MainWindowViewModel.cs:4104-4113` that loads the cached avatar list and starts the LocalLow watcher. The current code is gated on `Settings.VrChat.HasSavedSession`.

- [ ] **Step 2: Replace the condition with a user-id-aware load**

Change:

```csharp
if (Settings.VrChat.HasSavedSession)
{
    var cached = await settingsStore.LoadVrChatAvatarCacheAsync(Settings.VrChat.UserId, ...);
    ReplaceAvailableVrChatAvatars(cached);
}
StartOrRefreshVrChatLocalOscWatcher();
QueueLocalVrChatOscAvatarScan(0);
```

to:

```csharp
var startupUserId = ResolveCurrentUserIdForCache();
if (!string.IsNullOrEmpty(startupUserId))
{
    var cached = await settingsStore.LoadVrChatAvatarCacheAsync(startupUserId, ct);
    ReplaceAvailableVrChatAvatars(cached);
}
StartOrRefreshVrChatLocalOscWatcher();
QueueLocalVrChatOscAvatarScan(0); // one-shot LocalLow scan reads all <userId>/Avatars/<id>.json
RecomputeVrChatConnectionState();
```

If the existing code uses a different cancellation token name, use the same name. The key change is the relaxed condition and the explicit `RecomputeVrChatConnectionState()` at the end.

- [ ] **Step 3: Also call `RecomputeVrChatConnectionState` after login completes**

Find `ConnectVrChatAsync` (around line 3775-3852) and `RefreshVrChatAvatarsAsync` (around line 4142-4258). At the end of each successful path (after `ReplaceAvailableVrChatAvatars` or equivalent), add:

```csharp
RecomputeVrChatConnectionState();
```

Do not add it on the 401 path — `HandleVrChatUnauthorizedAsync` already calls it.

- [ ] **Step 4: Build to verify**

```bash
dotnet build "E:\!!!Program to work on\Proper Crystal Relay\VrcTwitchOscBridge\VrcTwitchOscBridge.csproj" --no-restore
```
Expected: build succeeds.

- [ ] **Step 5: Commit**

```bash
git add VrcTwitchOscBridge/ViewModels/MainWindowViewModel.cs
git commit -m "feat(vrchat): load avatar cache on startup without requiring a saved session"
```

---

## Task 10: Add the status pill to `MainWindow.xaml`

**Files:**
- Modify: `VrcTwitchOscBridge/MainWindow.xaml`

- [ ] **Step 1: Find the existing `VrChatStatus` text block**

Open `MainWindow.xaml` and go to line 3077 where the `VrChatStatus` text block is. The surrounding `StackPanel` is the "VRChat Avatar Access" section. Add the new pill inside the same `StackPanel`, right after the `VrChatStatus` text block.

- [ ] **Step 2: Add the new `TextBlock`**

After the existing `VrChatStatus` text block, add:

```xaml
<TextBlock Margin="0,4,0,0"
           Text="{Binding VrChatConnectionStateLabel}"
           ToolTip="{Binding VrChatConnectionStateTooltip}"
           Foreground="{Binding VrChatConnectionStateBrush}"
           FontSize="11"
           FontWeight="Normal" />
```

This adds a small status text under the existing `VrChatStatus` line, showing "VRChat: Logged in" / "VRChat: Cached" / "VRChat: No data" in a smaller font with a colored brush and tooltip.

- [ ] **Step 3: Build to verify**

```bash
dotnet build "E:\!!!Program to work on\Proper Crystal Relay\VrcTwitchOscBridge\VrcTwitchOscBridge.csproj" --no-restore
```
Expected: build succeeds.

- [ ] **Step 4: Commit**

```bash
git add VrcTwitchOscBridge/MainWindow.xaml
git commit -m "feat(ui): add VRChat connection state pill to main window"
```

---

## Task 11: Add en-US localization keys

**Files:**
- Modify: `VrcTwitchOscBridge/Resources/Localization/en-US.json`

- [ ] **Step 1: Open `en-US.json` and find a good insertion point**

Look for the existing string `"VRChat Avatar Access"` (around line 260) and add the 6 new keys right after it. The keys are flat string → string entries.

- [ ] **Step 2: Add the 6 new keys**

```json
"VRChat: Logged in": "VRChat: Logged in",
"VRChat: Cached": "VRChat: Cached",
"VRChat: No data": "VRChat: No data",
"Connected to VRChat. Avatar names and current avatar are fetched from the live API.": "Connected to VRChat. Avatar names and current avatar are fetched from the live API.",
"VRChat login is unavailable. Crystal Relay is using the cached avatar list and detecting the current avatar via OSC and LocalLow files.": "VRChat login is unavailable. Crystal Relay is using the cached avatar list and detecting the current avatar via OSC and LocalLow files.",
"No avatar data is available. Log in to VRChat or visit an avatar in VRChat to build the cache.": "No avatar data is available. Log in to VRChat or visit an avatar in VRChat to build the cache.",
```

Place these as their own block, separated by a blank line for readability.

- [ ] **Step 3: Commit**

```bash
git add VrcTwitchOscBridge/Resources/Localization/en-US.json
git commit -m "feat(localization): add en-US keys for VRChat connection state"
```

---

## Task 12: Mirror keys into other locales

**Files:**
- Modify: all 14 other `*.json` and `*.extra.json` localization files

- [ ] **Step 1: Open each non-English file**

The non-English locales are:
- `de-DE.json`, `de-DE.extra.json`
- `es-ES.json`, `es-ES.extra.json`
- `fr-FR.json`, `fr-FR.extra.json`
- `it-IT.json`, `it-IT.extra.json`
- `ja-JP.json`, `ja-JP.extra.json`
- `ko-KR.json`, `ko-KR.extra.json`
- `pl-PL.json`, `pl-PL.extra.json`
- `pt-BR.json`, `pt-BR.extra.json`
- `ru-RU.json`, `ru-RU.extra.json`
- `sv-SE.json`, `sv-SE.extra.json`
- `th-TH.json`, `th-TH.extra.json`
- `zh-CN.json`, `zh-CN.extra.json`
- `zh-TW.json`, `zh-TW.extra.json`

For each file, add the 6 new keys. Per the AGENTS.md translation rules:
- Use informal register (`du` for de-DE, `tú` for es-ES, `tu` for fr-FR, etc.)
- Keep brand and technical terms in English: `VRChat`, `OSC`, `Twitch`, `Crystal Relay`
- Preserve placeholders exactly (there are none in these strings, but stay aware)
- Do not translate product names or feature brand terms
- For gaming/streaming vocabulary, use the natural terms native speakers use

- [ ] **Step 2: Suggested translations (copy these into each file)**

`de-DE.json` and `de-DE.extra.json`:
```json
"VRChat: Logged in": "VRChat: Angemeldet",
"VRChat: Cached": "VRChat: Zwischengespeichert",
"VRChat: No data": "VRChat: Keine Daten",
"Connected to VRChat. Avatar names and current avatar are fetched from the live API.": "Mit VRChat verbunden. Avatar-Namen und aktueller Avatar werden live von der API geladen.",
"VRChat login is unavailable. Crystal Relay is using the cached avatar list and detecting the current avatar via OSC and LocalLow files.": "VRChat-Login ist nicht verfügbar. Crystal Relay nutzt die zwischengespeicherte Avatar-Liste und erkennt den aktuellen Avatar über OSC und LocalLow-Dateien.",
"No avatar data is available. Log in to VRChat or visit an avatar in VRChat to build the cache.": "Keine Avatar-Daten verfügbar. Melde dich bei VRChat an oder besuche einen Avatar in VRChat, um den Cache aufzubauen.",
```

`es-ES.json` and `es-ES.extra.json`:
```json
"VRChat: Logged in": "VRChat: Conectado",
"VRChat: Cached": "VRChat: En caché",
"VRChat: No data": "VRChat: Sin datos",
"Connected to VRChat. Avatar names and current avatar are fetched from the live API.": "Conectado a VRChat. Los nombres de los avatares y el avatar actual se obtienen directamente de la API en vivo.",
"VRChat login is unavailable. Crystal Relay is using the cached avatar list and detecting the current avatar via OSC and LocalLow files.": "El inicio de sesión de VRChat no está disponible. Crystal Relay está usando la lista de avatares en caché y detecta el avatar actual mediante OSC y los archivos de LocalLow.",
"No avatar data is available. Log in to VRChat or visit an avatar in VRChat to build the cache.": "No hay datos de avatares disponibles. Inicia sesión en VRChat o visita un avatar en VRChat para crear la caché.",
```

`fr-FR.json` and `fr-FR.extra.json`:
```json
"VRChat: Logged in": "VRChat : Connecté",
"VRChat: Cached": "VRChat : En cache",
"VRChat: No data": "VRChat : Aucune donnée",
"Connected to VRChat. Avatar names and current avatar are fetched from the live API.": "Connecté à VRChat. Les noms d'avatar et l'avatar actuel sont récupérés en direct depuis l'API.",
"VRChat login is unavailable. Crystal Relay is using the cached avatar list and detecting the current avatar via OSC and LocalLow files.": "La connexion VRChat n'est pas disponible. Crystal Relay utilise la liste d'avatars en cache et détecte l'avatar actuel via OSC et les fichiers LocalLow.",
"No avatar data is available. Log in to VRChat or visit an avatar in VRChat to build the cache.": "Aucune donnée d'avatar disponible. Connecte-toi à VRChat ou visite un avatar dans VRChat pour construire le cache.",
```

`it-IT.json` and `it-IT.extra.json`:
```json
"VRChat: Logged in": "VRChat: Connesso",
"VRChat: Cached": "VRChat: In cache",
"VRChat: No data": "VRChat: Nessun dato",
"Connected to VRChat. Avatar names and current avatar are fetched from the live API.": "Connesso a VRChat. I nomi degli avatar e l'avatar attuale vengono recuperati dall'API live.",
"VRChat login is unavailable. Crystal Relay is using the cached avatar list and detecting the current avatar via OSC and LocalLow files.": "L'accesso a VRChat non è disponibile. Crystal Relay sta usando la lista avatar in cache e rileva l'avatar attuale tramite OSC e i file LocalLow.",
"No avatar data is available. Log in to VRChat or visit an avatar in VRChat to build the cache.": "Nessun dato avatar disponibile. Accedi a VRChat o visita un avatar in VRChat per creare la cache.",
```

`ja-JP.json` and `ja-JP.extra.json`:
```json
"VRChat: Logged in": "VRChat: ログイン中",
"VRChat: Cached": "VRChat: キャッシュ",
"VRChat: No data": "VRChat: データなし",
"Connected to VRChat. Avatar names and current avatar are fetched from the live API.": "VRChat に接続済み。アバター名と現在のアバターはライブ API から取得しています。",
"VRChat login is unavailable. Crystal Relay is using the cached avatar list and detecting the current avatar via OSC and LocalLow files.": "VRChat ログインが利用できません。Crystal Relay はキャッシュされたアバターリストを使用し、OSC と LocalLow ファイルで現在のアバターを検出しています。",
"No avatar data is available. Log in to VRChat or visit an avatar in VRChat to build the cache.": "アバターデータがありません。VRChat にログインするか、VRChat 内でアバターを訪問してキャッシュを構築してください。",
```

`ko-KR.json` and `ko-KR.extra.json`:
```json
"VRChat: Logged in": "VRChat: 로그인됨",
"VRChat: Cached": "VRChat: 캐시됨",
"VRChat: No data": "VRChat: 데이터 없음",
"Connected to VRChat. Avatar names and current avatar are fetched from the live API.": "VRChat에 연결되었습니다. 아바타 이름과 현재 아바타를 실시간 API에서 가져옵니다.",
"VRChat login is unavailable. Crystal Relay is using the cached avatar list and detecting the current avatar via OSC and LocalLow files.": "VRChat 로그인을 사용할 수 없습니다. Crystal Relay는 캐시된 아바타 목록을 사용하고 OSC 및 LocalLow 파일을 통해 현재 아바타를 감지합니다.",
"No avatar data is available. Log in to VRChat or visit an avatar in VRChat to build the cache.": "사용 가능한 아바타 데이터가 없습니다. VRChat에 로그인하거나 VRChat에서 아바타를 방문하여 캐시를 만드세요.",
```

`pl-PL.json` and `pl-PL.extra.json`:
```json
"VRChat: Logged in": "VRChat: Zalogowano",
"VRChat: Cached": "VRChat: Z pamięci podręcznej",
"VRChat: No data": "VRChat: Brak danych",
"Connected to VRChat. Avatar names and current avatar are fetched from the live API.": "Połączono z VRChat. Nazwy awatarów i bieżący awatar są pobierane na żywo z API.",
"VRChat login is unavailable. Crystal Relay is using the cached avatar list and detecting the current avatar via OSC and LocalLow files.": "Logowanie do VRChat jest niedostępne. Crystal Relay korzysta z listy awatarów z pamięci podręcznej i wykrywa bieżący awatar przez OSC oraz pliki LocalLow.",
"No avatar data is available. Log in to VRChat or visit an avatar in VRChat to build the cache.": "Brak danych awatarów. Zaloguj się do VRChat lub odwiedź awatar w VRChat, aby zbudować pamięć podręczną.",
```

`pt-BR.json` and `pt-BR.extra.json`:
```json
"VRChat: Logged in": "VRChat: Conectado",
"VRChat: Cached": "VRChat: Em cache",
"VRChat: No data": "VRChat: Sem dados",
"Connected to VRChat. Avatar names and current avatar are fetched from the live API.": "Conectado ao VRChat. Os nomes dos avatares e o avatar atual são buscados diretamente da API ao vivo.",
"VRChat login is unavailable. Crystal Relay is using the cached avatar list and detecting the current avatar via OSC and LocalLow files.": "O login do VRChat não está disponível. O Crystal Relay está usando a lista de avatares em cache e detectando o avatar atual por OSC e arquivos LocalLow.",
"No avatar data is available. Log in to VRChat or visit an avatar in VRChat to build the cache.": "Não há dados de avatar disponíveis. Faça login no VRChat ou visite um avatar no VRChat para criar o cache.",
```

`ru-RU.json` and `ru-RU.extra.json`:
```json
"VRChat: Logged in": "VRChat: Вход выполнен",
"VRChat: Cached": "VRChat: Из кэша",
"VRChat: No data": "VRChat: Нет данных",
"Connected to VRChat. Avatar names and current avatar are fetched from the live API.": "Подключено к VRChat. Имена аватаров и текущий аватар загружаются из живого API.",
"VRChat login is unavailable. Crystal Relay is using the cached avatar list and detecting the current avatar via OSC and LocalLow files.": "Вход в VRChat недоступен. Crystal Relay использует кэшированный список аватаров и определяет текущий аватар через OSC и файлы LocalLow.",
"No avatar data is available. Log in to VRChat or visit an avatar in VRChat to build the cache.": "Нет данных об аватарах. Войдите в VRChat или посетите аватар в VRChat, чтобы создать кэш.",
```

`sv-SE.json` and `sv-SE.extra.json`:
```json
"VRChat: Logged in": "VRChat: Inloggad",
"VRChat: Cached": "VRChat: Cachad",
"VRChat: No data": "VRChat: Ingen data",
"Connected to VRChat. Avatar names and current avatar are fetched from the live API.": "Ansluten till VRChat. Avatar-namn och aktuell avatar hämtas direkt från live-API:et.",
"VRChat login is unavailable. Crystal Relay is using the cached avatar list and detecting the current avatar via OSC and LocalLow files.": "VRChat-inloggning är inte tillgänglig. Crystal Relay använder den cachade avatar-listan och upptäcker aktuell avatar via OSC och LocalLow-filer.",
"No avatar data is available. Log in to VRChat or visit an avatar in VRChat to build the cache.": "Ingen avatar-data tillgänglig. Logga in på VRChat eller besök en avatar i VRChat för att bygga cachen.",
```

`th-TH.json` and `th-TH.extra.json`:
```json
"VRChat: Logged in": "VRChat: เข้าสู่ระบบแล้ว",
"VRChat: Cached": "VRChat: แคชไว้",
"VRChat: No data": "VRChat: ไม่มีข้อมูล",
"Connected to VRChat. Avatar names and current avatar are fetched from the live API.": "เชื่อมต่อกับ VRChat แล้ว ชื่ออวาตาร์และอวาตาร์ปัจจุบันถูกดึงจาก API แบบสด",
"VRChat login is unavailable. Crystal Relay is using the cached avatar list and detecting the current avatar via OSC and LocalLow files.": "ไม่สามารถเข้าสู่ระบบ VRChat ได้ Crystal Relay กำลังใช้รายการอวาตาร์ที่แคชไว้และตรวจจับอวาตาร์ปัจจุบันผ่าน OSC และไฟล์ LocalLow",
"No avatar data is available. Log in to VRChat or visit an avatar in VRChat to build the cache.": "ไม่มีข้อมูลอวาตาร์ที่ใช้งานได้ โปรดเข้าสู่ระบบ VRChat หรือเข้าไปยังอวาตาร์ใน VRChat เพื่อสร้างแคช",
```

`zh-CN.json` and `zh-CN.extra.json`:
```json
"VRChat: Logged in": "VRChat：已登录",
"VRChat: Cached": "VRChat：已缓存",
"VRChat: No data": "VRChat：无数据",
"Connected to VRChat. Avatar names and current avatar are fetched from the live API.": "已连接到 VRChat。头像名称和当前头像通过实时 API 获取。",
"VRChat login is unavailable. Crystal Relay is using the cached avatar list and detecting the current avatar via OSC and LocalLow files.": "无法登录 VRChat。Crystal Relay 正在使用缓存的头像列表，并通过 OSC 和 LocalLow 文件检测当前头像。",
"No avatar data is available. Log in to VRChat or visit an avatar in VRChat to build the cache.": "没有可用的头像数据。请登录 VRChat 或在 VRChat 中访问一个头像以建立缓存。",
```

`zh-TW.json` and `zh-TW.extra.json`:
```json
"VRChat: Logged in": "VRChat：已登入",
"VRChat: Cached": "VRChat：已快取",
"VRChat: No data": "VRChat：無資料",
"Connected to VRChat. Avatar names and current avatar are fetched from the live API.": "已連線至 VRChat。頭像名稱與目前頭像透過即時 API 取得。",
"VRChat login is unavailable. Crystal Relay is using the cached avatar list and detecting the current avatar via OSC and LocalLow files.": "無法登入 VRChat。Crystal Relay 正在使用快取的頭像清單，並透過 OSC 與 LocalLow 檔案偵測目前頭像。",
"No avatar data is available. Log in to VRChat or visit an avatar in VRChat to build the cache.": "沒有可用的頭像資料。請登入 VRChat 或在 VRChat 中造訪一個頭像以建立快取。",
```

- [ ] **Step 3: Build to verify**

```bash
dotnet build "E:\!!!Program to work on\Proper Crystal Relay\VrcTwitchOscBridge\VrcTwitchOscBridge.csproj" --no-restore
```
Expected: build succeeds.

- [ ] **Step 4: Run the localization audit**

The localization audit is run automatically by the build scripts. For a manual check, run:
```bash
powershell -ExecutionPolicy Bypass -File "E:\!!!Program to work on\Proper Crystal Relay\tools\github\Export-Crystal-Relay-Public.ps1" -SkipExport -RunLocalizationAudit
```
(If the audit script has a different flag, use the one documented in `AGENTS.md` under "Localization Rules".)

Expected: no missing-key warnings for the 6 new strings in any locale.

- [ ] **Step 5: Commit**

```bash
git add VrcTwitchOscBridge/Resources/Localization/
git commit -m "feat(localization): mirror VRChat connection state keys to all locales"
```

---

## Task 13: Build, test, and smoke-test the full change

**Files:** none modified in this task — verification only.

- [ ] **Step 1: Run the full build**

```bash
dotnet build "E:\!!!Program to work on\Proper Crystal Relay\VrcTwitchOscBridge\VrcTwitchOscBridge.csproj" --no-restore
```
Expected: build succeeds with no errors.

- [ ] **Step 2: Run the full test suite**

```bash
dotnet test "E:\!!!Program to work on\Proper Crystal Relay\VrcTwitchOscBridge.Tests\VrcTwitchOscBridge.Tests.csproj"
```
Expected: all tests pass, including the 3 new user-id-inference tests, the 6 merger tests, and the 2 SettingsStore tests.

- [ ] **Step 3: Manual smoke test plan**

Run the launch-debug script and verify each of the 9 scenarios from the spec's testing section:

1. Fresh install, no login, no LocalLow data → pill = `NoData`. Open the app, confirm only Twitch features are usable.
2. Add a single LocalLow avatar JSON manually → restart app, pill = `Cached`, the avatar id resolves to the JSON's `name`.
3. Log in normally → pill = `LoggedIn`, full list available.
4. Log out from the app → pill = `Cached`, last-known avatar still resolves.
5. Log in, then force a 401 by revoking the cookie → pill = `Cached`, LocalLow entries survive, API entries gone.
6. With VRChat running and the app in `Cached` state, switch avatars in VRChat → pill stays `Cached`, `Settings.VrChat.CurrentAvatarId` updates, the new avatar's name shows up.
7. Multiple accounts on the same Windows user → log in as A, log out, log in as B. The cache for A is preserved on disk but B sees an empty cache initially.
8. Stop VRChat mid-session → existing OSC recovery kicks in (no change).
9. Output log fallback still works → confirm `MainWindowViewModel.RefreshCurrentVrChatAvatarFromLocalFilesAsync` still fires when only the log has the new avatar.

For each scenario, record the result in a comment or in your final report. If any scenario fails, fix the regression before continuing.

- [ ] **Step 4: Final commit (if any smoke-test fixes were needed)**

```bash
git add -A
git commit -m "fix: address smoke-test findings"
```

---

## Self-Review Notes

**Spec coverage:**
- Goal 1 (run without login) → Tasks 4, 5, 6, 7, 9.
- Goal 2 (persist LocalLow names) → Task 6.
- Goal 3 (subscribe to /avatar/change) → Tasks 2, 5.
- Goal 4 (graceful 401) → Task 7.
- Goal 5 (UI status pill) → Tasks 3, 4, 10, 11, 12.

**Placeholder scan:** No TBDs. All steps include exact file paths, code blocks, and commands.

**Type consistency:**
- `VrChatConnectionState` enum: declared in Task 3 (`LoggedIn | Cached | NoData`), used consistently in Tasks 4, 5, 9, 10.
- `VrChatAvatarSummary` record: constructor `(Id, Name, SourceLabel, IsCurrentAvatar, ThumbnailUrl)` used consistently in Tasks 1, 5, 6, 8.
- `OscAvatarChangeMerger.MergeIntoList(existing, avatarId, resolvedName, newSourceLabel)` signature is used identically in the tests (Task 5) and the implementation (Task 5).
- `VrChatOscAvatarChangeReceived` event: `Action<string>` in Task 2, subscribed with `Action<string>` handler in Task 4.
- `Settings.VrChat.AuthCookie / UserId / DisplayName / CurrentAvatarId`: all standard fields, used consistently across Tasks 7 and 9.
- `ReplaceAvailableVrChatAvatars(IReadOnlyList<VrChatAvatarSummary>)` is the existing public method (line 19692-19714) used in Tasks 5, 6, 7.
- `SaveVrChatAvatarCacheAsync(userId, avatars, ct)` and `LoadVrChatAvatarCacheAsync(userId, ct)` are the existing public methods used in Tasks 6, 7, 8, 9.

No type mismatches detected.
