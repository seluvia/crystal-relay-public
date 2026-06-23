# VRChat-Optional OSC Avatar Cache: Read OSC Without Login

Date: 2026-06-20
Lane: in-progress 3.1.9 beta 4
Status: design approved, pending user spec review

## Problem

Crystal Relay currently requires a working VRChat login to look up avatar names and resolve the user's currently-worn avatar. The login is used for two things:

1. **Avatar list** — `GET /avatars?user=me&releaseStatus=all`, `avatars/favorites`, `avatars/licensed`. Returned entries have an `id` and a human-readable `name`.
2. **Current avatar** — `GET /auth/user` returns the `currentAvatar` field on the authenticated user.

When the auth cookie is missing or has expired (HTTP 401), the app clears the encrypted avatar cache and the in-memory avatar list (`MainWindowViewModel.cs:4223-4224, 11383-11404`). The result: every avatar-aware feature stops working until the user re-authenticates.

Meanwhile, two local sources of avatar data already exist and are used only for OSC parameter lookup, not for name resolution:

- **LocalLow OSC JSON** at `…\AppData\LocalLow\VRChat\VRChat\OSC\<usr_…>\Avatars\<avtr_…>.json`. Each file carries the avatar's `id`, `name`, and typed `parameters[]` array. Already parsed by `VrChatLocalOscCacheService`.
- **OSC `/avatar/change` broadcast** — VRChat's local client emits the current avatar id on the OSC bus when the user changes avatars. The app's `OscRouterService` advertises this address and the bridge sends to it, but the receive loop currently filters it out (`BridgeCoordinator.ObserveOscValue`, `VrcTwitchOscBridge/Services/BridgeCoordinator.cs:17114-17161`).

The user has asked: when VRChat login is unavailable (expired cookie, network down, fresh install before first login), the app should still know which avatars the user wears, what their names are, and what parameters to read. It should also be able to cache any avatar it observes via OSC, even if the API is down.

## Goal

1. Allow the app to run without a VRChat login while keeping the avatar-aware features (avatar sets, avatar change/roulette, OSC parameter actions, avatar scaling) working against local data.
2. **Persist** the LocalLow-derived `(id, name)` pairs so they survive app restarts and don't have to be re-walked from disk on every launch.
3. **Subscribe** to OSC `/avatar/change` and use it to update `Settings.VrChat.CurrentAvatarId` in real time, the same way the API call is used today.
4. **Gracefully degrade** on VRChat 401: keep LocalLow-sourced entries, drop API-sourced entries, mark the app as `Cached` instead of `NoData`.
5. **Surface** the current login/cache state in the UI with a small status pill so the user knows whether they are running with full API access, cached data, or no avatar data at all.

## Non-goals

- No anonymous avatar name lookup. The LocalLow JSON `name` field is only present when VRChat itself wrote the file (which requires the user to have visited the avatar while logged in at least once).
- No new cache file format. We extend the existing `vrchat-avatar-cache.secure` (`Services/SettingsStore.cs:153-180`) with a new `SourceLabel = "Local OSC"` value, reusing the existing load/save helpers.
- No change to the existing login flow. Logging in still works exactly as today.
- No change to the OSC parameter cache schema. The existing `vrchat-osc-parameter-cache.secure` is already populated from LocalLow; this design does not modify it.
- No change to the OSC send path. We still send to `/avatar/change` via `VrChatOscClient.BuildAvatarChangePacket` (`Services/VrChatOscClient.cs:28-31`).
- No new dependency on a third-party package.
- No public-facing branding or naming change. The status pill is internal UI.

## Current architecture (ground truth from code reading)

### VRChat API surface
- `Services/VrChatApiClient.cs:14-15` — base URL `https://api.vrchat.cloud/api/1/`, 18 s timeout.
- `Services/VrChatApiRoutes.cs:1-15` — endpoints: `CurrentUser = "auth/user"`, `UploadedAvatars = "avatars?user=me&releaseStatus=all"`, `FavoriteAvatars = "avatars/favorites"`, `LicensedAvatars = "avatars/licensed"`.
- `VrChatApiClient.GetCurrentUserAsync` (`Services/VrChatApiClient.cs:83-98`) returns `VrChatAccountSettings` with `CurrentAvatarId` mapped from the `currentAvatar` field of `auth/user` (lines 476-485).
- `VrChatApiClient.GetSelectableAvatarsAsync` (lines 112-138) merges uploaded + favorites + licensed into a single `IReadOnlyList<VrChatAvatarSummary>`.

### Encrypted secure caches
- `Services/AppDataPaths.cs:20` — `SecureFolder => Path.Combine(RootFolder, "Secure")`.
- `Services/SettingsStore.cs:82-90` — declares the three secure files. Avatar cache at `vrchat-avatar-cache.secure` (+ `.bak`).
- `SettingsStore.cs:124-180` — `LoadVrChatAvatarCacheAsync(userId, ct)` / `SaveVrChatAvatarCacheAsync(userId, avatars, ct)`. Caches are scoped to a single `UserId`.
- `SettingsStore.cs:2983-3008` — `PersistedVrChatAvatarCache` / `PersistedVrChatAvatar` schema. `PersistedVrChatAvatar` has `Id`, `Name`, `SourceLabel` (already a string field, today set to `"Uploaded"`, `"Favorites"`, `"Licensed"`), `IsCurrentAvatar`, `ThumbnailUrl`.
- `SettingsStore.cs:3010-3021` — `PersistedVrChatAvatar` is mapped to `VrChatAvatarSummary(Id, Name, SourceLabel, IsCurrentAvatar, ThumbnailUrl)`.
- `SettingsStore.cs:222-273` — `SaveVrChatOscParameterCacheAsync(userId, avatarId, parameters, ct)` upserts a per-avatar parameter list.
- Encryption: `ProtectedData.Protect` with `DataProtectionScope.CurrentUser` (`SettingsStore.cs:2328-2374`). Atomic write with `.bak` swap, hidden file attribute.

### LocalLow OSC cache service
- `Services/VrChatLocalOscCacheService.cs:154-190` — `GetAvatarOscFolderPath(userId)` and `GetAvatarOscFilePath(userId, avatarId)`.
- `Services/VrChatLocalOscCacheService.cs:194-237` — `FindAvatarOscFilePathByAvatarId(avatarId)` walks every `OSC\<userId>\Avatars\` folder and returns the first match. Used when the user id is not known.
- `Services/VrChatLocalOscCacheService.cs:22-67` — `LoadKnownAvatarsAsync(userId, ct)`. Reads every `<avatarId>.json` in the user's avatar folder, dedupes by id, returns `IReadOnlyList<LocalVrChatOscAvatarSummary>` with `(AvatarId, AvatarName, FilePath, LastWriteTimeUtc)`.
- `Services/VrChatLocalOscCacheService.cs:79-106` — `LoadAvatarParametersAsync(userId, avatarId, ct)` and `LoadAvatarParametersByAvatarIdAsync(avatarId, ct)` (the latter uses the file-by-id fallback).
- `Services/VrChatLocalOscCacheService.cs:312-339` — file-level parse. Reads `id` and `name` from the JSON; falls back to the id if the `name` is blank.
- `Services/VrChatLocalOscCacheService.cs:341-364` — `LocalOscAvatarFile`, `LocalOscParameter`, `LocalOscParameterEndpoint` schema.

### LocalLow watcher (in-memory only today)
- `ViewModels/MainWindowViewModel.cs:11407-11440` — `StartOrRefreshVrChatLocalOscWatcher` sets up a `FileSystemWatcher` on `OSC\<userId>\Avatars\*.json`.
- `ViewModels/MainWindowViewModel.cs:11471-11504` — `QueueLocalVrChatOscAvatarScan(delayMs)` debounces watcher events.
- `ViewModels/MainWindowViewModel.cs:11506-11572` — `ScanLocalVrChatOscAvatarCacheAsync`. Calls `LoadKnownAvatarsAsync`, then `ApplyLocalVrChatOscAvatars` on the UI thread.
- `ViewModels/MainWindowViewModel.cs:11574-11603` — `ApplyLocalVrChatOscAvatars` updates `availableVrChatAvatars`.
- `ViewModels/MainWindowViewModel.cs:11605-11680` — `MergeLocalVrChatAvatars`. The merge rule at lines 11642-11654 is: only adopt the LocalLow name if the existing name is empty or just the avatar id (i.e., does not clobber a better name already known from the API).
- **Persistence gap**: this path never calls `SaveVrChatAvatarCacheAsync`. The LocalLow names are in-memory only and lost on restart.

### Current-avatar detection (two paths)
- `ViewModels/MainWindowViewModel.cs:11342-11405` — `RefreshCurrentVrChatAvatarFromApiAsync`. Reads `auth/user`, updates `Settings.VrChat.CurrentAvatarId`, calls `HandleVrChatAvatarChangedByBridge`.
- `ViewModels/MainWindowViewModel.cs:11272-11340` — `RefreshCurrentVrChatAvatarFromLocalFilesAsync`. Tail-reads `output_log_*.txt` for `Switching <player> to avatar <name>` lines, resolves the name back to an id via `availableVrChatAvatars`.
- `ViewModels/MainWindowViewModel.cs:19079-19111` — `HandleVrChatAvatarChangedByBridge(avatarId, queueManagedRewardSync)`. The single downstream consumer of "we know the avatar changed" — updates the bridge's current id, replaces the in-memory cache entry, refreshes UI, queues managed-reward sync, and triggers the per-avatar parameter cache load.
- `Services/BridgeCoordinator.cs:13906-13952` — `SetCurrentVrChatAvatar` (called via `UpdateCurrentVrChatAvatar` at lines 489-492). Holds the in-bridge current id and fires the `VrChatAvatarChanged` event.
- `Services/BridgeCoordinator.cs:17114-17161` — `ObserveOscValue`. Currently filters incoming OSC to:
  - `/avatar/eyeheight*` (stored in `avatarScaleValues`)
  - `/avatar/parameters/*` (stored in `avatarParameterValues`)
  - everything else is dropped.
  - **This is the place where we add the `/avatar/change` branch.**

### OSC send/receive
- `Services/OscRouterService.cs:379-411` — `RunReceiveLoopAsync`. Parses every received OSC packet via `TryReadObservedValue` (lines 782-812) and raises `ObservedValueReceived` for any value with a type tag.
- `Services/OscRouterService.cs:586-595` — `BuildDesiredEndpoints`. Already advertises `/avatar/change` as `OscParameterType.String`. This is unchanged by the new design.
- `Services/VrChatOscClient.cs:28-31` — `BuildAvatarChangePacket(avatarId)`. Builds the OSC packet to send to `/avatar/change`. Unchanged.
- `Services/OscRouterService.cs:534-553` — `LooksLikeVrChatAsync`. Checks for `/avatar/change` (or `/avatar/eyeheight*`/`/avatar/parameters`/`/input`) in the OSCQuery tree of a discovered service to confirm the target is VRChat. Unchanged.

### 401 cleanup (today)
- `ViewModels/MainWindowViewModel.cs:4223-4224` — in `RefreshVrChatAvatarsAsync`, on `Unauthorized`:
  - `settingsStore.ClearVrChatAvatarCacheAsync(ct)`
  - `settingsStore.ClearVrChatOscParameterCacheAsync(ct)`
  - `cachedVrChatParametersByAvatarId.Clear()`
- `ViewModels/MainWindowViewModel.cs:11383-11404` — in `RefreshCurrentVrChatAvatarFromApiAsync`, the same cleanup runs.

## Design

### Three-state machine

Add a new enum `VrChatConnectionState` with three values:

```csharp
public enum VrChatConnectionState
{
    NoData,    // no auth, no cache, no LocalLow
    Cached,    // no auth, but cache or LocalLow has at least one avatar
    LoggedIn,  // auth cookie is set and not expired
}
```

The state is computed:

- `LoggedIn` when `Settings.VrChat.AuthCookie` is non-empty and the last API refresh was not a 401 within the last N seconds (N = 30 s, to absorb transient errors).
- `Cached` when no auth, but `availableVrChatAvatars` is non-empty OR the persisted cache on disk has at least one entry OR `TryInferUserIdFromLocalLowAsync` returns a non-null user id.
- `NoData` otherwise.

The state is exposed as a new read-only `VrChatConnectionState` property on `MainWindowViewModel` and is the data source for the top-bar status pill.

### New component: `/avatar/change` subscription

In `Services/BridgeCoordinator.cs:17114-17161`, add a new branch at the top of `ObserveOscValue` (before the existing `IsAvatarScaleAddress` / `StartsWith("/avatar/parameters/")` checks):

```csharp
if (string.Equals(observedValue.Address, "/avatar/change", StringComparison.Ordinal))
{
    var avatarId = observedValue.StringValue?.Trim();
    if (!string.IsNullOrEmpty(avatarId) && avatarId.StartsWith("avtr_", StringComparison.Ordinal))
    {
        VrChatOscAvatarChangeReceived?.Invoke(avatarId);
    }
    return;
}
```

The new event `VrChatOscAvatarChangeReceived` is declared on `BridgeCoordinator` (next to the existing `ObservedValueReceived` and `AvatarScaleStatusChanged` events at the top of the class). The wiring is in the `MainWindowViewModel` constructor.

### New method: `MainWindowViewModel.HandleIncomingOscAvatarChange(avatarId, ct)`

This is the OSC-driven equivalent of `RefreshCurrentVrChatAvatarFromApiAsync`. Pseudocode:

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

    // 2. If not in memory, walk LocalLow for the JSON. The path helper is
    //    synchronous; the file parse is via LoadKnownAvatarsAsync(userId, ct)
    //    using the inferred user id (cheaper than reading the file a second time).
    if (resolvedName is null)
    {
        var inferredUserId = await VrChatLocalOscCacheService
            .TryInferUserIdFromLocalLowAsync(ct)
            .ConfigureAwait(false);
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

    // 3. Merge into the in-memory list.
    var finalName = resolvedName ?? avatarId;
    var isNew = !availableVrChatAvatarNamesById.ContainsKey(avatarId);
    if (isNew)
    {
        availableVrChatAvatars.Add(new VrChatAvatarSummary(
            Id: avatarId,
            Name: finalName,
            SourceLabel: "Local OSC",
            IsCurrentAvatar: false,
            ThumbnailUrl: null));
        availableVrChatAvatarNamesById[avatarId] = finalName;
    }
    else if (!string.Equals(availableVrChatAvatarNamesById[avatarId], finalName, StringComparison.Ordinal))
    {
        // Adopt the better name if the existing one is the id.
        var idx = availableVrChatAvatars.FindIndex(a => a.Id == avatarId);
        if (idx >= 0)
        {
            availableVrChatAvatars[idx] = availableVrChatAvatars[idx] with
            {
                Name = finalName,
                SourceLabel = "Local OSC",
            };
            availableVrChatAvatarNamesById[avatarId] = finalName;
        }
    }

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
        catch (Exception ex)
        {
            // log warning, do not throw
        }
    }

    // 5. Drive the existing downstream flow.
    await HandleVrChatAvatarChangedByBridge(avatarId, queueManagedRewardSync: true)
        .ConfigureAwait(false);
}
```

### New helper: `VrChatLocalOscCacheService.TryInferUserIdFromLocalLowAsync(ct)`

Walks `%USERPROFILE%\AppData\LocalLow\VRChat\VRChat\OSC\` and returns the first `usr_*` subfolder name, or null if there are zero. If there are multiple, returns the name of the most recently modified one. Logs a warning when multiple are found.

```csharp
public static Task<string?> TryInferUserIdFromLocalLowAsync(CancellationToken ct = default)
{
    ct.ThrowIfCancellationRequested();
    var oscRoot = Path.Combine(GetVrChatRootPath(), "OSC");
    if (!Directory.Exists(oscRoot)) return Task.FromResult<string?>(null);
    string[] userDirs;
    try
    {
        userDirs = Directory.GetDirectories(oscRoot, "usr_*");
    }
    catch
    {
        return Task.FromResult<string?>(null);
    }
    if (userDirs.Length == 0) return Task.FromResult<string?>(null);
    if (userDirs.Length == 1) return Task.FromResult<string?>(Path.GetFileName(userDirs[0]));
    // multiple — pick the most recently modified
    var newest = userDirs
        .OrderByDescending(d => Directory.GetLastWriteTimeUtc(d))
        .First();
    return Task.FromResult<string?>(Path.GetFileName(newest));
}
```

### New helper: `MainWindowViewModel.ResolveCurrentUserIdForCache()`

Returns the user id to use as the cache scope. Preference order:

1. `Settings.VrChat.UserId` if non-empty.
2. The result of `VrChatLocalOscCacheService.TryInferUserIdFromLocalLowAsync()` cached as a field `inferredLocalLowUserId`.

If both are empty, the persist step is skipped but the in-memory update still happens.

### Persist on every LocalLow watcher event

In `ViewModels/MainWindowViewModel.cs:11506-11572` `ScanLocalVrChatOscAvatarCacheAsync`, after the existing `ApplyLocalVrChatOscAvatars` call (line 11566), add:

```csharp
var userId = ResolveCurrentUserIdForCache();
if (!string.IsNullOrEmpty(userId))
{
    try
    {
        await settingsStore
            .SaveVrChatAvatarCacheAsync(userId, availableVrChatAvatars, CancellationToken.None)
            .ConfigureAwait(false);
    }
    catch (Exception ex)
    {
        // log warning
    }
}
```

This means the cache is persisted any time a LocalLow file is added, changed, or removed.

### 401 cleanup: keep LocalLow, drop API

Replace `MainWindowViewModel.cs:4223-4224` and `11383-11404` with the new behavior. The new flow:

```csharp
private async Task HandleVrChatUnauthorizedAsync(CancellationToken ct)
{
    // 1. Filter the in-memory list to LocalLow-only entries.
    var localOnly = availableVrChatAvatars
        .Where(a => string.Equals(a.SourceLabel, "Local OSC", StringComparison.Ordinal))
        .ToList();

    // 2. Replace the in-memory list.
    ReplaceAvailableVrChatAvatars(localOnly);

    // 3. Persist the filtered list (keeps LocalLow entries; drops API entries).
    var userId = ResolveCurrentUserIdForCache();
    if (!string.IsNullOrEmpty(userId))
    {
        try
        {
            await settingsStore
                .SaveVrChatAvatarCacheAsync(userId, localOnly, ct)
                .ConfigureAwait(false);
        }
        catch (Exception ex)
        {
            // log warning
        }
    }

    // 4. Clear auth state.
    Settings.VrChat.AuthCookie = string.Empty;
    Settings.VrChat.UserId = string.Empty;
    Settings.VrChat.DisplayName = string.Empty;

    // 5. The OSC parameter cache and the in-memory parameter dictionary
    //    are NOT cleared — they are LocalLow-sourced and still valid.

    // 6. The bridge keeps its current avatar id until the next OSC /avatar/change
    //    or LocalLow watcher event updates it. Do not clear BridgeCoordinator.currentVrChatAvatarId.

    // 7. Notify the UI to flip the status pill.
    OnPropertyChanged(nameof(VrChatConnectionState));
}
```

**Re-audit of 401 callers**: any code that assumed `availableVrChatAvatars` was empty after a 401 needs to be checked. Known callers: managed reward sync (`RefreshCurrentAvatarStateForManagedRewardSyncAsync` at line 11101), avatar selection options builder (`RefreshVrChatAvatarSelectionOptions`), parameter options builder (`RefreshAvatarParameterOptions`). The contract change is "the list is non-empty and contains LocalLow-sourced entries after 401". All three of these consumers iterate the list and treat empty as "no avatars"; a non-empty list with LocalLow entries will continue to render correctly.

### Startup path: load cache without login

At `ViewModels/MainWindowViewModel.cs:4104-4113`, the existing startup path already calls `LoadVrChatAvatarCacheAsync` and then `ScanLocalVrChatOscAvatarCacheAsync` (which does a one-time read of all `<userId>/Avatars/<avatarId>.json` files). The change is to make the cache load work without a saved session, and to also run the LocalLow scan when no saved session is present.

**Today:**
```csharp
if (Settings.VrChat.HasSavedSession)
{
    var cached = await settingsStore.LoadVrChatAvatarCacheAsync(Settings.VrChat.UserId, ...);
    ReplaceAvailableVrChatAvatars(cached);
}
StartOrRefreshVrChatLocalOscWatcher();
QueueLocalVrChatOscAvatarScan(0);
```

**New:**
```csharp
var userId = ResolveCurrentUserIdForCache();
if (!string.IsNullOrEmpty(userId))
{
    var cached = await settingsStore.LoadVrChatAvatarCacheAsync(userId, ...);
    ReplaceAvailableVrChatAvatars(cached);
}
StartOrRefreshVrChatLocalOscWatcher();
QueueLocalVrChatOscAvatarScan(0); // existing one-shot LocalLow scan reads all
                                  // <userId>/Avatars/<avatarId>.json files and
                                  // merges their names into the in-memory list
                                  // (then persists via the new persist step)
```

`ResolveCurrentUserIdForCache` returns `Settings.VrChat.UserId` if set, else the result of `TryInferUserIdFromLocalLowAsync`. The same one-shot scan that runs today (and merges LocalLow names) is what populates the in-memory list for fresh installs; the only change is that the persist step is now appended to it (see "Persist on every LocalLow watcher event" above).

After the load, always:

- Start the LocalLow watcher (`StartOrRefreshVrChatLocalOscWatcher`).
- Queue an initial scan (`QueueLocalVrChatOscAvatarScan(0)`).
- Subscribe to `BridgeCoordinator.VrChatOscAvatarChangeReceived`.
- Recompute the `VrChatConnectionState` property.

### UI: status pill

In `MainWindow.xaml`, find the existing top-bar status area (where the Twitch/VRChat status text lives today) and add a small `TextBlock` bound to `VrChatConnectionState`:

```xaml
<TextBlock Text="{Binding VrChatConnectionStateLabel}"
           ToolTip="{Binding VrChatConnectionStateTooltip}"
           Foreground="{Binding VrChatConnectionStateBrush}" />
```

Three new `MainWindowViewModel` properties drive it:

- `VrChatConnectionStateLabel` — `string` (`"VRChat: Logged in"` / `"VRChat: Cached"` / `"VRChat: No data"`).
- `VrChatConnectionStateTooltip` — `string` with a one-line explanation per state.
- `VrChatConnectionStateBrush` — `Brush` (green / yellow / gray).

The state is recomputed on:

- Login completion.
- Logout.
- 401.
- First successful `HandleIncomingOscAvatarChange` (transition from `NoData` to `Cached`).
- Settings changes that affect auth (cookie rotation, manual logout).

### Localization

Add en-US source keys for:

- `VRChat_Label_LoggedIn` — `"VRChat: Logged in"`
- `VRChat_Label_Cached` — `"VRChat: Cached"`
- `VRChat_Label_NoData` — `"VRChat: No data"`
- `VRChat_Tooltip_LoggedIn` — `"Connected to VRChat. Avatar names and current avatar are fetched from the live API."`
- `VRChat_Tooltip_Cached` — `"VRChat login is unavailable. Crystal Relay is using the cached avatar list and detecting current avatar via OSC and LocalLow files."`
- `VRChat_Tooltip_NoData` — `"No avatar data is available. Log in to VRChat or visit an avatar in VRChat to build the cache."`

Mirror to all other languages per `AGENTS.md` localization rules (informal register, brand terms in English, preserve placeholders, etc.). Run the localization audit after the change.

## Data flow

### Avatar change while logged out (happy path)

```
User in VRChat switches avatars
  -> VRChat client writes new <avatarId>.json to LocalLow\OSC\usr_xxx\Avatars\
  -> VRChat client emits /avatar/change with the new avatar id (String)
  -> OscRouterService.RunReceiveLoopAsync receives the packet
  -> TryReadObservedValue parses it as String
  -> ObservedValueReceived fires
  -> BridgeCoordinator.ObserveOscValue sees /avatar/change
  -> Fires VrChatOscAvatarChangeReceived(avatarId)
  -> MainWindowViewModel.HandleIncomingOscAvatarChangeAsync(avatarId, ct)
       -> FindAvatarOscFilePathByAvatarId(avatarId) returns path
       -> Parse name from JSON
       -> Merge into availableVrChatAvatars with SourceLabel="Local OSC"
       -> SaveVrChatAvatarCacheAsync(userId, list, ct) -- persists
       -> HandleVrChatAvatarChangedByBridge(avatarId, ...) -- downstream
            -> Settings.VrChat.CurrentAvatarId = avatarId
            -> ReplaceCurrentAvatarInCache(avatarId)
            -> RefreshVrChatAvatarSelectionOptions, RefreshAvatarParameterOptions
            -> QueueManagedRewardSync(0, ManagedRewardSyncReason.AvatarChanged)
            -> EnsureSelectedAvatarParameterCacheLoadedAsync + QueueCurrentVrChatOscParameterRefresh
```

### App startup, no login, persisted cache exists

```
MainWindowViewModel startup
  -> ResolveCurrentUserIdForCache()
       -> Settings.VrChat.UserId (prior session) OR TryInferUserIdFromLocalLowAsync()
  -> LoadVrChatAvatarCacheAsync(userId, ct) -- returns cached list
  -> (if empty) TryLoadAvatarsFromLocalLowAsync -- bulk LocalLow import
  -> ReplaceAvailableVrChatAvatars(list)
  -> StartOrRefreshVrChatLocalOscWatcher
  -> QueueLocalVrChatOscAvatarScan(0)
       -> ScanLocalVrChatOscAvatarCacheAsync
            -> LoadKnownAvatarsAsync
            -> MergeLocalVrChatAvatars
            -> NEW: SaveVrChatAvatarCacheAsync(userId, list, ct)
  -> Subscribe to BridgeCoordinator.VrChatOscAvatarChangeReceived
  -> VrChatConnectionState = Cached
```

## Error handling

| Scenario | Behavior |
|---|---|
| `/avatar/change` fires with empty string | Ignored. |
| `/avatar/change` fires with id not starting with `avtr_` | Ignored, log a debug line. |
| `/avatar/change` fires with id, but no JSON on disk | Cache id as name with `SourceLabel = "Local OSC"`, persist. UI shows id as name. |
| `/avatar/change` fires but no user id can be resolved | Defer persist; in-memory update only. Next scan will write the cache. |
| `FindAvatarOscFilePathByAvatarId` finds no file | Treat id as the name. |
| `SaveVrChatAvatarCacheAsync` throws | Log warning, continue. In-memory state is still valid. |
| `LoadVrChatAvatarCacheAsync` throws on startup | Start with empty list, state = `NoData`, log warning. |
| LocalLow scan throws | Existing per-file try/catch in `VrChatLocalOscCacheService`. No change. |
| 401 during API refresh | New behavior: filter in-memory list to `SourceLabel == "Local OSC"`, persist, clear auth/cookie/userid. State → `Cached`. OSC observe stays active. |
| User logs in for first time after using cache | API list overwrites LocalLow entries. LocalLow watcher continues to update the persisted cache as files change. |
| User logs in with a different account than the cache was built for | `LoadVrChatAvatarCacheAsync` checks the persisted `UserId`; mismatch returns empty. New login scopes a new cache. Old cache preserved on disk. |
| `TryInferUserIdFromLocalLowAsync` finds zero or multiple OSC user folders | Zero → `NoData`. Multiple → newest by mtime, log a warning. |
| Same avatar id seen twice in a row via OSC | No-op. `HandleVrChatAvatarChangedByBridge` checks for id change (19086). |
| File system watcher fires repeatedly | Existing debounce via `QueueLocalVrChatOscAvatarScan`. No change. |
| Output log shows an avatar name not in cache | Existing fallback (11272-11340): id resolution fails, no id update, log line emitted. No change. |

## Concurrency

- The OSC receive loop is single-threaded; it raises `ObservedValueReceived` on the receive thread.
- The bridge dispatches to the UI thread via the existing `ObserveOscValue` pattern (`BridgeCoordinator.cs:17114-17161`).
- `HandleIncomingOscAvatarChangeAsync` is invoked from the bridge; it is awaited on the UI thread, so it can directly mutate `availableVrChatAvatars`.
- The cache save is async (`SaveVrChatAvatarCacheAsync`) and atomic on disk via `SaveProtectedJsonAsync` + `.bak` swap. If two save calls race, the last writer wins. Both contain the same in-memory list snapshot, so the race is benign.
- The LocalLow file watcher debounce is unchanged.

## Testing

### Unit tests (in `VrcTwitchOscBridge.Tests`)

**`VrChatLocalOscCacheService` tests**
- `TryInferUserIdFromLocalLowAsync_WhenFolderExists_ReturnsUserId` — set up a temp `OSC\usr_abc123\Avatars\` directory, assert the helper returns `usr_abc123`.
- `TryInferUserIdFromLocalLowAsync_WhenMultipleUsersExist_ReturnsMostRecentlyModified` — set up two OSC user folders with different write times, assert the newer one wins.
- `TryInferUserIdFromLocalLowAsync_WhenNoFolderExists_ReturnsNull` — assert null on a fresh temp root.

**`SettingsStore` tests** (extend existing tests)
- `SaveVrChatAvatarCacheAsync_ThenLoadVrChatAvatarCacheAsync_PreservesLocalLowEntries` — round-trip a list containing both API-sourced and LocalLow-sourced entries; assert the load returns them with `SourceLabel` intact.
- `LoadVrChatAvatarCacheAsync_WithMismatchedUserId_ReturnsEmpty` — existing behavior, keep the test.

**`MainWindowViewModel` tests** (new test class if needed)
- `HandleIncomingOscAvatarChange_KnownIdInLocalLow_MergesAndPersists` — pre-populate the in-memory list and the persisted cache; fire the handler with a known id; assert the name shows up, the cache file was rewritten, and the downstream handler was called.
- `HandleIncomingOscAvatarChange_UnknownId_StoresIdAsName` — fire with a brand-new id; assert the list now has an entry with `Name = id` and `SourceLabel = "Local OSC"`.
- `HandleIncomingOscAvatarChange_EmptyString_Ignored` — assert no state change.
- `HandleIncomingOscAvatarChange_MalformedId_Ignored` — assert no state change for a non-`avtr_` value.
- `HandleIncomingOscAvatarChange_AlreadyCurrentAvatar_NoOp` — assert the downstream handler is NOT called when the id matches the current avatar.
- `HandleIncomingOscAvatarChange_NoUserId_DefersPersist` — assert the in-memory list is updated but the cache file is NOT written.

**401 cleanup tests** (extend existing tests)
- `HandleVrChatUnauthorized_KeepsLocalLowEntriesAndDropsApiEntries` — set up the in-memory list with mixed source labels; trigger the 401 cleanup; assert only `SourceLabel == "Local OSC"` entries remain, the cache was rewritten with the filtered list, and the OSC parameter cache is NOT cleared.
- `HandleVrChatUnauthorized_DoesNotClearOscParameterCache` — assert the parameter cache file and the in-memory `cachedVrChatParametersByAvatarId` dict are untouched.

**Startup path tests**
- `Startup_NoLoginNoCache_NoDataState` — assert `VrChatConnectionState == NoData` and `availableVrChatAvatars` is empty.
- `Startup_NoLoginWithPersistedCache_CachedState` — write a cache file, run the startup path, assert `VrChatConnectionState == Cached` and the list is populated.
- `Startup_NoLoginWithLocalLowFolderButNoPersistedCache_CachedState` — write a LocalLow user folder with one avatar JSON, run the startup path, assert `VrChatConnectionState == Cached` and the LocalLow user id was inferred.

### Manual smoke test plan

1. Fresh install, no login, no LocalLow data → pill = `NoData`. Open the app, confirm only Twitch features are usable.
2. Add a single LocalLow avatar JSON manually → restart app, pill = `Cached`, the avatar id resolves to the JSON's `name`.
3. Log in normally → pill = `LoggedIn`, full list available.
4. Log out from the app → pill = `Cached`, last-known avatar still resolves.
5. Log in, then force a 401 by revoking the cookie → pill = `Cached`, LocalLow entries survive, API entries gone.
6. With VRChat running and the app in `Cached` state, switch avatars in VRChat → pill stays `Cached`, `Settings.VrChat.CurrentAvatarId` updates, the new avatar's name shows up.
7. Multiple accounts on the same Windows user → log in as A, log out, log in as B. The cache for A is preserved on disk but B sees an empty cache initially.
8. Stop VRChat mid-session → existing OSC recovery kicks in (no change).
9. Output log fallback still works → confirm `MainWindowViewModel.RefreshCurrentVrChatAvatarFromLocalFilesAsync` still fires when only the log has the new avatar.

### What we will NOT test

- Real VRChat client behavior (can't run live in CI). The OSC receive path is tested with synthetic packets.
- DPAPI encryption (Windows-specific, tested by existing `SettingsStore` tests).
- WPF UI binding for the status pill (manual smoke test only).

### Build / lint gate

- `dotnet build "E:\!!!Program to work on\Proper Crystal Relay\VrcTwitchOscBridge\VrcTwitchOscBridge.csproj" --no-restore` after the code change.
- `dotnet test` for the test project.
- Localization audit (run by the build scripts) after adding new strings.

## Risks and mitigations

| Risk | Severity | Mitigation |
|---|---|---|
| `/avatar/change` may not fire reliably from VRChat's client | Medium | The LocalLow file watcher and the `output_log_*.txt` reader are independent fallbacks. If OSC `/avatar/change` doesn't fire, the watcher still detects the new JSON and the log reader still sees the name. |
| Cache save races with LocalLow watcher events | Low | Save is atomic via `SaveProtectedJsonAsync` + `.bak` swap. Race is benign. |
| Existing 401 callers that expect a fully-cleared cache | Medium | Re-audit all call sites of the 401 cleanup path; verify nothing relies on the cache being empty. |
| Multiple Windows accounts sharing a LocalLow root | Low | Out of scope. `TryInferUserIdFromLocalLowAsync` picks the first/newest folder. |
| `UserId` mismatch on first login after a long offline period | Low | The startup path will fail to load the cache with a stale user id; the new login will overwrite with the correct user id. |
| UI status pill flicker during the login → first API refresh window | Low | Pill shows `LoggedIn` as soon as the cookie is set. Sticky for 30 s to absorb transient 401s. |
| Increased disk I/O from persisting on every LocalLow watcher event | Low | The persisted cache is small and the writes are debounced by the existing watcher debounce. |
| A maliciously crafted `<avatarId>.json` could be parsed | Low | The parser is read-only and bounded. The `name` field is the only user-facing data and is rendered in the existing UI. |
| Localization gaps for the new status pill strings | Low | The localization audit will catch missing keys. |
| `/avatar/change` packet arrives with a different type tag (e.g., int) | Low | `TryReadObservedValue` parses standard OSC types; the new handler validates the value is a non-empty `avtr_` id. |

## Rollout

1. Implement on the `beta4` build lane (current `Active build lane: beta4` per `AGENTS.md`).
2. Build a test package via `Build-Crystal-Relay-Test.ps1` and run the manual smoke test plan.
3. After the test build is good, build a beta via `Build-Crystal-Relay-Beta.ps1`, update the `CHANGELOG.txt` `v<version> beta <N>` entry per the workflow, and let the user test it on stream.
4. Promote to stable on user confirmation per `AGENTS.md` release workflow.

## Rollback

- The change is mostly additive: one new event hook, one new method, one new helper, two modified methods, one new enum + UI binding.
- The only behavioral change is the 401 cleanup: instead of clearing the cache, we filter it. If the user reports problems, the 401 path can be reverted to the old behavior in a single edit (the new persist call is a no-op if the in-memory list is already empty).
- A version-bumped beta with the rollback in place is the standard recovery.

## Open questions

None at this time. The user has confirmed all the major design decisions (subscribe to OSC `/avatar/change`, cache only `id`+`name`, silent fallback with a status pill, keep LocalLow on 401, Approach A).
