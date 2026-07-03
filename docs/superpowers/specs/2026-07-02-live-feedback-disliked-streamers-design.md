# Live Feedback Dev Tool — Disliked Streamers + Favorite Star Visual State

Date: 2026-07-02
Target: `tools/private/crystal-relay-live-list` (private dev tool, not the main app)

## Summary

Add a way to mark live streamers as "disliked" so they are routed to a separate
Disliked section and excluded from live notifications (sound, tray balloon,
unread badge). Also fix the existing favorite-star button so it visually reflects
the favorite state.

## Motivation

The live feedback dev tool currently lets you favorite streamers and filter to
favorites only, but there is no way to say "I do not want to be notified about
this streamer." The favorite star also never shows whether a streamer is already
favorited, which reads as a bug.

## Scope

In scope:
- New disliked-streamer classification with separate storage.
- Third "Disliked" navigation tab showing disliked live users only.
- Disliked users excluded from the main live list and from all notifications.
- Favorite-star visual state fix (reflect `IsFavorite` on the card).
- New dislike button on each card with its own visual state.
- Mutual exclusion: favoriting removes dislike and vice versa.
- Unit tests for the new disliked store.

Out of scope:
- Offline disliked streamers (the Disliked tab shows only currently-live disliked
  users, matching how the main list works).
- Main app / release / changelog / website changes (this is a private dev tool).
- Localization audit (this private dev tool has no localization flow).

## Approach

Approach A: parallel `DislikedStore` mirroring the existing tested
`FavoritesStore`. Minimal targeted edit; existing favorites tests stay green.

## Design

### 1. Data & Storage

- New `Services/DislikedStore.cs` mirrors `Services/FavoritesStore.cs`:
  - `HashSet<string>` of normalized keys, case-insensitive
    (`StringComparer.OrdinalIgnoreCase`).
  - JSON-persisted to `disliked.json` in the same
    `AppData/Local/CrystalRelay/DevTools/LiveList` folder.
  - Same atomic save pattern (write `.tmp`, then `File.Replace`/`File.Move`).
  - Same swallow-errors policy (disliked list is a convenience, never throws).
  - Public API: `IReadOnlyCollection<string> Keys`, `bool IsDisliked(string key)`
    (named `IsDisliked` for readability at call sites even though the internals
    mirror `FavoritesStore`), `bool Toggle(string key)`.
- `FavoritesStore` is untouched; its tests stay green.
- In `MainWindow.xaml.cs`, a new `private DislikedStore? disliked;` field is
  initialized in `InitializeServices()` right after `favorites`:
  `disliked = new DislikedStore(Path.Combine(dataRoot, "disliked.json"));`
- Mutual exclusion is enforced at the toggle handlers in the code-behind, not in
  the stores themselves (stores stay single-responsibility):
  - `OnToggleFavoriteClicked`: after `favorites.Toggle(key)`, if the user is now
    a favorite and `disliked.IsDisliked(key)` is true, call `disliked.Toggle(key)`
    to remove it.
  - `OnToggleDislikedClicked`: after `disliked.Toggle(key)`, if the user is now
    disliked and `favorites.IsFavorite(key)` is true, call `favorites.Toggle(key)`
    to remove it.

### 2. ViewModel & Visual State

- `LiveUserViewModel` gains two properties with change notification:
  - `bool IsFavorite`
  - `bool IsDisliked`
- `LiveUserViewModel` implements `INotifyPropertyChanged` (small impl; only these
  two props raise change notifications). A `RefreshClassification(bool
  isFavorite, bool isDisliked)` method updates both and raises
  `PropertyChanged` for each, so an in-place toggle reflects immediately without
  a full list refresh.
- `BuildIncomingUsers` becomes an instance method (it is currently static) so it
  can read `favorites` and `disliked` when constructing each
  `LiveUserViewModel`. Each VM is built with its initial `IsFavorite` /
  `IsDisliked` values.
- Favorite star button (existing, `MainWindow.xaml` Grid.Column 3): give the
  star `TextBlock` a small inline `Style` with `DataTrigger`s on `IsFavorite`:
  - `Opacity`: 1.0 when favorite, 0.4 when not (via `Setter` on the trigger).
  - `Foreground`: `PinkBrush` when favorite, `MutedBrush` when not.
  - `ToolTip`: "Unfavorite" when favorite, "Toggle favorite" when not (set via
    `ToolTipService.ToolTip` setters on each trigger).
  - No new converters needed — pure `DataTrigger` setters, matching the existing
    no-converter XAML patterns in this window.
- New dislike button on each card, placed in a new Grid.Column 4 (the header
  Grid needs a 5th `ColumnDefinition Width="Auto"` added — the grid currently
  has 4 columns 0..3):
  - Same chrome as the favorite star (transparent background/border, `Cursor=Hand`,
    `Tag={Binding TwitchUrl}`, `Click="OnToggleDislikedClicked"`).
  - Glyph: `&#x1F44E;` (thumbs-down) at `FontSize=16`.
  - Visual state mirrors the favorite star via `DataTrigger`s on `IsDisliked`:
    `Opacity` 1.0 disliked / 0.4 not, `Foreground` `PinkBrush` (disliked) /
    `MutedBrush` (not), `ToolTip` "Remove from disliked" / "Toggle disliked".
- After toggling in `OnToggleFavoriteClicked` / `OnToggleDislikedClicked`, find
  the affected `LiveUserViewModel` in `Users` and call `RefreshClassification`
  with the fresh `favorites.IsFavorite(key)` / `disliked.IsDisliked(key)` values,
  then `ApplySearchFilter()` so the disliked user leaves the main list immediately
  and appears in the Disliked view.

### 3. Navigation, Filtering & Notifications

- Third radio tab "Disliked" added next to Live Now / 24h History, using the
  existing `ViewModeRadioStyle`. New `GroupName="LiveListMode"`,
  `Checked="OnShowDislikedClicked"`.
- New `private bool isShowingDisliked;` field and a new
  `bool IsDislikedViewVisible` computed property (`isShowingDisliked &&
  !isShowingStream`). Wired into `RaiseViewModePropertiesChanged` alongside the
  existing view-mode properties.
- `IsDecorativeBackdropVisible` extended:
  `!isShowingStream` stays, but the backdrop can remain visible in the disliked
  view (it's a list view, not a stream view). No change needed — disliked view is
  a list view and should keep the backdrop, matching Live Now / History.
- New `OnShowDislikedClicked` handler sets `isShowingHistory=false`,
  `isShowingStream=false`, `isShowingDisliked=true`, stops any stream viewer, and
  calls `RaiseViewModePropertiesChanged` + `UpdateStoryboardState`.
- `SetLiveListView` / `SetHistoryView` reset `isShowingDisliked=false` so
  switching tabs clears the disliked view state.
- Main live list filter (`FilterUser`): exclude users where
  `disliked.IsDisliked(LiveUserKey.Normalize(...))`. Disliked live users never
  appear in the main list.
- Disliked view: a new `ScrollViewer` + `ItemsControl` section matching the main
  list's exact layout, bound to `Users` with a filter that shows *only*
  disliked users. Reuses the existing card `DataTemplate` so favorite + dislike
  buttons are present and un-dislike works from inside the Disliked tab. The
  disliked view is wrapped in the same `Visibility="{Binding
  IsDislikedViewVisible, ...}"` pattern as the other views.
- Notifications in `RefreshAsync`:
  - `shouldAlert` (newly-live check) excludes disliked users: compute
    `incomingKeys` as before, but the newly-live set used for alerting is
    `incomingKeys.Except(dislikedKeys)`. Disliked users going live do not
    trigger the sound alert, the "Crystal Relay live" tray balloon, or the
    unread badge increment.
  - `newFavoriteNames` already cannot include disliked users (mutual exclusion),
    but the favorite-live balloon loop stays guarded by `favorites.IsFavorite`
    which is sufficient.
  - Aggregate `stats.RecordSnapshot` keeps using total `Users.Count` /
    `incomingKeys` for peak/unique/current telemetry (that's aggregate, not
    per-user noise).

### 4. Tests, Build & Verification

- New `DislikedStoreTests.cs` mirrors `FavoritesStoreTests`:
  - `Toggle_AddsAndRemovesDisliked`
  - `IsDisliked_CaseInsensitive`
  - `Persisted_AcrossInstances`
- No new tests for the ViewModel or notification exclusion (UI-layer; verified by
  manual run of the live-list dev tool directly from its build output).
- Build verification:
  - `dotnet build` on `CrystalRelayLiveList.csproj` directly (AGENTS.md notes the
    slnx isn't a reliable validation target).
  - `dotnet build` + `dotnet test` on `CrystalRelayLiveList.Tests.csproj` to
    confirm both the existing `FavoritesStoreTests` and the new
    `DislikedStoreTests` pass.
- No localization audit (private dev tool, no `.extra.json` flow).
- No `AGENTS.md` version / build-lane / changelog / website changes (this is a
  `tools/private` dev-tool edit, not a main-program or release change).

## Files Touched

New:
- `tools/private/crystal-relay-live-list/Services/DislikedStore.cs`
- `tools/private/crystal-relay-live-list-tests/DislikedStoreTests.cs`

Edited:
- `tools/private/crystal-relay-live-list/ViewModels/LiveUserViewModel.cs`
  (add `INotifyPropertyChanged`, `IsFavorite`, `IsDisliked`,
  `RefreshClassification`)
- `tools/private/crystal-relay-live-list/MainWindow.xaml.cs`
  (new `disliked` field, `BuildIncomingUsers` becomes instance method, new
  `OnToggleDislikedClicked` / `OnShowDislikedClicked` handlers, mutual-exclusion
  in both toggle handlers, disliked-aware `FilterUser`, disliked-aware alert
  exclusion in `RefreshAsync`, `isShowingDisliked` + `IsDislikedViewVisible` +
  view-mode wiring, reset `isShowingDisliked` in `SetLiveListView` /
  `SetHistoryView`)
- `tools/private/crystal-relay-live-list/MainWindow.xaml`
  (favorite star visual-state `DataTrigger`s, new dislike button in card header
  + 5th column definition, third "Disliked" radio tab, new disliked-view
  `ScrollViewer`+`ItemsControl` section)

## Risks & Mitigations

- **Stale `IsFavorite`/`IsDisliked` after toggle**: mitigated by calling
  `RefreshClassification` on the affected VM in-place right after each toggle,
  before `ApplySearchFilter`.
- **Disliked user still alerts on first load**: mitigated by excluding disliked
  keys from the `shouldAlert` newly-live set in `RefreshAsync`.
- **Mutual exclusion races**: stores are single-threaded on the UI thread; the
  toggle handlers do both swaps synchronously before refresh, so no race.
- **Existing favorites tests break**: `FavoritesStore` is untouched; only new
  code is added. `FavoritesStoreTests` stay green.
