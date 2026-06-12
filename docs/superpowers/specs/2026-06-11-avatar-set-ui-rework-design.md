# Avatar Set UI Rework Design Spec

**Date:** 2026-06-11
**Scope:** Avatar Set management UI layer only - the runtime engine, models, persistence, Twitch reward sync, wardrobe logic, and master-profile selection are preserved unchanged.
**Goal:** Strip out the current inline Avatar Set list + editor in `MainWindow.xaml` and rebuild the management UI as a dedicated themed secondary window (`AvatarSetsManagerWindow.xaml`) with a card-grid + slide-in editor that mirrors the Universal Triggers pattern, but with the VRChat avatar profile icon as the card's hero image.


---

## Current State

### What works (do not touch)

- **Models**:
  - `Models/AvatarTriggerProfile.cs` (446 lines) - the Avatar Set itself: `Id`, `Name`, `AvatarId`, `AvatarName`, `IsCurrentAvatarActive`, `IsMasterProfile`, `IsEnabled`, `ChannelPointRules`, `WardrobeOutfits`, set-trigger master reward config, wardrobe mode config, computed display strings.
  - `Models/TriggerRule.cs` (1,846 lines) - the rules inside a set: Twitch trigger type, reward cost/cooldown/sync-mode, chat command, OSC action types (AvatarParameter / AvatarChange / AvatarRoulet / Movement), movement direction, parameter name/type/value, supporter float-add, etc.
  - `Models/WardrobeOutfit.cs` (193 lines) - wardrobe outfit inside a set.
  - `Models/WardrobeSnapshotParam.cs` - single parameter snapshot.
- **Persistence**: `AppSettings.AvatarProfiles` (`ObservableCollection<AvatarTriggerProfile>`) serialized by `SettingsStore.cs`. Save transfer / theme assets / crash logs unaffected.
- **Runtime engine** (`BridgeCoordinator.cs` and related): set trigger master reward handling, master profile selection, wardrobe snapshot application, set reward activation, current-avatar detection, Bits + Subs override priority, Avatar Scaling integration. All preserved.
- **Twitch API integration**: managed-reward sync for set-trigger master reward, cooldown color, hide/disable, delete-when-inactive (opt-in). Preserved.
- **Avatar image infrastructure** (`Services/AvatarImageService.cs`, 336 lines): VRChat thumbnail download with auth cookie, in-memory `ConcurrentDictionary` cache, disk cache at `%LOCALAPPDATA%\CrystalRelay\Crystal Relay Save Transfer\ThemeAssets\AvatarIcons\Cache\{avatarId}.jpg`, placeholder `DrawingImage` fallback, custom-icon layer. Fully reusable as-is.
- **Avatar picker** (`AvatarPickerWindow.xaml`/`.cs`, `AvatarPickerViewModel.cs`, `AvatarPickerService.cs`): the modal that lets the user pick a VRChat avatar. Opens via `MainWindowViewModel.OpenAvatarPickerCommand` with a `parameter` string. Preserved and reused.
- **VRChat API client** (`Services/VrChatApiClient.cs`, `Services/VrChatApiRoutes.cs`): `GetSelectableAvatarsAsync` returns `VrChatAvatarSummary` records with `ThumbnailUrl`. Preserved.

### What is being replaced

- The current inline Avatar Set UI in `MainWindow.xaml`:
  - The list pane: `ListBox` at line 4364 with `SelectedItem` bound to `MainWindowViewModel.SelectedAvatarProfile`, fed by `AvatarRuleProfiles`.
  - The card data template: `DataTemplate DataType="{x:Type models:AvatarTriggerProfile}"` at lines 1425-1537 (text-only card with toggle, title, subtitle, return-avatar chip, redeem count, live-status chip - no image).
  - The editor pane: second `DataTemplate DataType="{x:Type models:AvatarTriggerProfile}"` at lines 5419-5830 with the full editor (name, avatar picker, set-trigger master reward, channel point rules list, wardrobe editor).
  - The empty state at lines 4652-4707.
  - All `IsViewingAvatarTriggers` and section visibility flags.
- `MainWindowViewModel.OpenAvatarPickerCommand` (line 6380-6441) is preserved but only the `"Profile"`, `"PowerUp"`, `"Supporter"`, `"AvatarChange"` parameter paths are still wired. The new manager window opens the picker for the `"Profile"` path when the user picks an avatar in the editor.

### What is NOT being changed

- Any Twitch API / EventSub behavior.
- Any reward sync behavior.
- Avatar Change, Avatar Roulette, Power Up, Supporter Growth, Supporter Override, Avatar Scaling, Movement Redeems, Bits + Subs overrides, Universal Triggers, Cash Payments, Twitch Chatbox, About page.
- `BridgeCoordinator`, `SettingsStore`, `AppSettings.AvatarProfiles` shape.
- `AvatarImageService` - reused as-is.
- `AvatarPickerWindow` - reused as-is.


---

## Goals

1. Remove the current inline Avatar Set list + editor from `MainWindow.xaml` cleanly without disturbing the runtime engine or persistence.
2. Ship a friendly themed secondary window (`AvatarSetsManagerWindow.xaml`) modeled after `UniversalTriggersManagerWindow.xaml` with:
   - empty-state landing with a big "Create your first Avatar Set" card
   - populated state with title bar + toolbar (New + Search + Filter chips + Sort + Enable/Disable All) + single-grid card layout
   - 280x320 cards with the VRChat avatar profile icon as the hero image
   - left status stripe (green/amber/gray) matching Universal Triggers card pattern
   - mode pill (`STANDARD` / `WARDROBE`) + count pill + status pill + optional `★ MASTER` badge
   - pick-avatar dashed placeholder for sets without an avatar
   - 480px slide-in editor overlay (right edge)
   - footer with red `Delete All` button
3. Preserve every saved Avatar Set across the rework (`AppSettings.AvatarProfiles` is untouched).
4. Preserve every existing Avatar Set feature: set-trigger master reward config, channel point rules, wardrobe outfits, wardrobe mode, master return-avatar selection, Bits + Subs override priority, Avatar Scaling integration, Fire Sale discount, managed-reward sync.
5. Other Crystal Relay systems must keep working through the rebuild.


---

## Out of Scope

- Any change to `BridgeCoordinator` runtime hot path.
- Any change to `AvatarTriggerProfile`, `TriggerRule`, `WardrobeOutfit`, `WardrobeSnapshotParam` model shapes.
- Any change to `SettingsStore` persistence shape (no schema migration).
- Any change to `AppSettings.AvatarProfiles` collection wiring.
- Any change to `AvatarImageService` (reused as-is).
- Any change to `AvatarPickerWindow` (reused as-is).
- Any change to `VrChatApiClient` or `VrChatApiRoutes`.
- Any change to other Crystal Relay sections (Avatar Change, Power Up, Universal Triggers, etc.).
- No new VRChat / Twitch API endpoints.
- No redesign of the Avatar Set editor content (the existing editor controls are moved as-is into the slide-in overlay, not redesigned).
- No multi-select / bulk edit on cards.
- No drag-to-reorder for cards.
- No wardrobe outfit previews on the card.
- No new "Duplicate Set" action.
- No new "Export Set" / "Import Set" action.


---

## Architecture

### Entry point in the main window

`MainWindow.xaml` keeps a single sidebar button labeled "Avatar Sets" inside the Redeem Library / Avatar Triggers section. Clicking it raises `MainWindowViewModel.OpenAvatarSetsManagerCommand`, which constructs and shows the secondary window as a non-modal dialog parented to the main window (matching `UniversalTriggersManagerWindow` and `AvatarLibraryManagerWindow` lifecycle).

The current inline list pane + editor pane are removed. In their place, a small status line ("X sets • Y active") sits above the button so the user can see their set count at a glance without opening the window.

`MainWindowViewModel.AvatarRuleProfiles`, `SelectedAvatarProfile`, `AddAvatarProfileCommand`, `DeleteSelectedAvatarProfileCommand`, `DeleteAllAvatarProfilesCommand`, `SetSelectedAvatarProfileAsMasterCommand`, `UseCurrentVrChatAvatarForProfileCommand`, `RefreshVrChatAvatarsCommand`, `OpenAvatarPickerCommand`, `AddWardrobeOutfitCommand`, `RemoveWardrobeOutfitCommand`, `AddWardrobeSnapshotParamCommand`, `TestWardrobeOutfitCommand`, `CreateDefaultAvatarProfile`, and `RefreshAvatarRuleProfilesList` are all preserved. They are now driven from the new manager window's editor instead of the inline editor.

### Secondary window

`AvatarSetsManagerWindow.xaml` (+ `.xaml.cs`):

- Custom-themed chrome (`WindowStyle="None"`, `WindowChrome` with `CaptionHeight="0"`), matches `UniversalTriggersManagerWindow` and `AvatarLibraryManagerWindow`.
- `Width="1100"`, `Height="700"`, `MinWidth="800"`, `MinHeight="500"`, `WindowStartupLocation="CenterOwner"`.
- Single window for both empty state and populated state - switched via a `DataTrigger` on `AvatarRuleProfiles.Count == 0` (mirrors `UniversalTriggersManagerWindow` empty-state pattern at lines 586-665).
- Constructed by `MainWindowViewModel.OpenAvatarSetsManagerCommand` with the new `AvatarSetsManagerViewModel` as its DataContext. The VM receives a reference to `MainWindowViewModel` and the shared `AvatarImageService` instance.

### View model

`ViewModels/AvatarSetsManagerViewModel.cs`:

- Holds a reference to `MainWindowViewModel` (constructor-injected) so it can read/write `AvatarRuleProfiles`, call `AddAvatarProfileCommand`, `OpenAvatarPickerCommand`, etc., and observe the `IsCurrentAvatarActive` property of each profile.
- Exposes a single `ICollectionView` of `AvatarSetCardViewModel` instances - one per `AvatarTriggerProfile`. (Single grid, no section grouping by trigger type - that grouping only exists in Universal Triggers because it has five trigger types. Avatar Set has one logical "type": an avatar-bound set.)
- Exposes:
  - `string SubtitleSummary` = "X total • Y active • Z need avatar"
  - `string SearchText` (filters by Avatar Set name and avatar name - case-insensitive substring)
  - `AvatarSetsFilterMode FilterMode` enum: `All`, `Active`, `Disabled`, `LiveNow`, `Master`
  - `AvatarSetsSortMode SortMode` enum: `ByName`, `ByStatus`, `RecentlyEdited`
  - `int CountAll`, `CountActive`, `CountDisabled`, `CountLiveNow`, `CountMaster` + matching `AllFilterText`, etc., for the filter chip badges
  - `AvatarTriggerProfile? SelectedProfile` (the profile being edited in the slide-in overlay)
  - `bool IsEditorOpen` (toggles the slide-in overlay visibility)
  - `ObservableCollection<AvatarSetCardViewModel> Cards` (the backing collection - the view wraps it)
- Commands:
  - `AddNewSetCommand` - calls `MainWindowViewModel.AddAvatarProfileCommand`, then opens the editor on the new profile
  - `OpenEditorCommand(AvatarTriggerProfile profile)` - sets `SelectedProfile` + `IsEditorOpen = true`
  - `CloseEditorCommand`
  - `TestSetCommand(AvatarTriggerProfile profile)` - **Wardrobe mode**: calls `MainWindowViewModel.TestWardrobeOutfitCommand` on the first enabled outfit in `profile.WardrobeOutfits`. **Standard mode**: the current inline UI has no "test the whole set" command for standard channel-point rules; the Test button on standard cards is therefore disabled with a tooltip "Test is only available for wardrobe sets". This matches existing behavior - no new test path is invented for standard sets.
  - `EnableAllCommand`, `DisableAllCommand` - iterate cards, set `IsEnabled`
  - `DeleteAllCommand` - calls `MainWindowViewModel.DeleteAllAvatarProfilesCommand` (which already shows its own confirmation)
  - `ShowAllCommand`, `ShowActiveCommand`, `ShowDisabledCommand`, `ShowLiveNowCommand`, `ShowMasterCommand`
  - `SortByNameCommand`, `SortByStatusCommand`, `SortByRecentCommand`
- The view model subscribes to `MainWindowViewModel.AvatarRuleProfiles.CollectionChanged` and rebuilds the `Cards` collection when profiles are added/removed. It also subscribes to each profile's `PropertyChanged` to refresh the card VM's derived state (status, live text, count) without rebuilding the whole collection.

### Card view model

`ViewModels/AvatarSetCardViewModel.cs` (per-card wrapper, ~120 lines):

- Constructor takes `(AvatarTriggerProfile profile, AvatarImageService imageService, Func<AvatarTriggerProfile, MainWindowViewModel> mainVmAccessor)`.
- Subscribes to `profile.PropertyChanged` and `imageService` cache change events to refresh derived state.
- Exposes:
  - `AvatarTriggerProfile Profile` (the underlying model)
  - `string DisplayTitle` = `Profile.Name` (or "New Set" if empty)
  - `string AvatarSubtitle` = `Profile.AvatarName` if set, otherwise `(no avatar picked)` or the live `Profile.AvatarId`
  - `string CountPillText` = "N redeems" (standard) or "N outfits" (wardrobe) or "0 redeems" (empty)
  - `string ModePillText` = "STANDARD" or "WARDROBE" or empty when zero
  - `string StatusPillText` = "READY" / "SETUP NEEDED" / "DISABLED"
  - `string LiveText` = "● Live now" (green) / "○ Waiting for this avatar" (muted) / "○ Pick an avatar to enable" (amber) / "○ Off" (gray)
  - `Brush StatusStripeBrush` - looks up `StatusStripeReadyBrush` / `StatusStripeWarnBrush` / `StatusStripeOffBrush` from app resources (same brushes Universal Triggers uses), falls back to Green / Goldenrod / Gray
  - `Brush ModePillBrush` - Indigo for standard, Pink for wardrobe
  - `Brush StatusPillBrush` - matches stripe color
  - `bool IsMaster` = `Profile.IsMasterProfile`
  - `bool IsLive` = `Profile.IsCurrentAvatarActive`
  - `bool IsEnabled` = `Profile.IsEnabled` (drives the toggle and the 0.55 opacity dim)
  - `bool HasAvatar` = !string.IsNullOrWhiteSpace(`Profile.AvatarId`)
  - `ImageSource? Image` - bound to the card's hero image area
  - `bool IsTestDisabled` = `!HasAvatar` (Test button disabled when no avatar)
  - `ICommand OpenEditorCommand` - delegates to the parent VM's `OpenEditorCommand` with `Profile`
  - `ICommand TestCommand` - delegates to the parent VM's `TestSetCommand` with `Profile`
- **Image loading**:
  - On construction, if `HasAvatar`, calls `imageService.GetAvatarImageAsync(Profile.AvatarId, customIconPath: null, vrchatThumbnailUrl: ??, ct)`.
  - The thumbnail URL is looked up from a fresh `VrChatApiClient.GetSelectableAvatarsAsync` snapshot cached on the parent VM, or `null` if VRChat is offline (falls back to placeholder).
  - The `Image` property is set on the UI thread via `Application.Current.Dispatcher.InvokeAsync`.
  - When the profile's `AvatarId` changes, the card VM re-fetches the image and updates `Image`.
  - When the window closes, all card VMs are disposed and the image refs are released; the on-disk cache stays.

### Avatar Picker integration

- The editor overlay (see below) hosts a "Browse..." button that calls `MainWindowViewModel.OpenAvatarPickerCommand` with `parameter: "Profile"` (same string the inline UI uses).
- The picker opens modally, returns the chosen `VrChatAvatarSummary` (id + name + thumbnail URL).
- The picked values are written into `SelectedProfile.AvatarId` and `SelectedProfile.AvatarName` by the existing `OpenAvatarPicker` handler.
- The card VM observes `Profile.AvatarId` change and re-fetches the image, so the card updates immediately when the editor saves.

### Editor overlay (slide-in)

- 480px-wide right-edge panel with `PanelBrush` background, slides in via a `DoubleAnimation` on `RenderTransform` when `IsEditorOpen == true`. Matches `UniversalTriggersManagerWindow.xaml` slide-in pattern at lines 1144-1800+.
- Hosts the existing editor content from `MainWindow.xaml` lines 5419-5830, relocated as a `DataTemplate DataType="{x:Type models:AvatarTriggerProfile}"` in the new window's `Window.Resources`.
- Editor includes (preserved as-is from the current inline UI):
  - Name textbox
  - Avatar picker: `Browse` button (opens picker), `Use Current Avatar` button, refresh button
  - Set-trigger master reward: ID, title, description, cost, sync mode, cooldown seconds, ready color, cooldown color
  - Channel point rules list
  - Wardrobe editor (when wardrobe mode is on)
  - At the bottom: a red `Delete Set` button (calls `MainWindowViewModel.DeleteSelectedAvatarProfileCommand` after a confirmation dialog), and `Save & Close` / `Cancel` buttons
- The `Save & Close` button commits any pending changes to `SelectedProfile` and closes the overlay. The `Cancel` button reverts uncommitted changes (where possible - the current inline UI commits on edit, so "cancel" just closes without persisting anything that wasn't already saved by the picker handler).

### MainWindow.xaml changes

- Remove the inline Avatar Set list pane (`ListBox` at line 4364-4399, data template at lines 1425-1537, empty state at lines 4652-4707).
- Remove the inline editor pane (`DataTemplate` at lines 5419-5830, `ContentControl` at line 5384).
- Remove the `IsViewingAvatarTriggers`, `IsViewingMasterAvatar`, and any related visibility flags from `MainWindowViewModel.cs` that only existed to drive the inline UI.
- Replace with a small inline summary block:
  ```xaml
  <StackPanel>
      <TextBlock Text="{Binding AvatarSetSummaryText}" Foreground="MutedBrush" />
      <Button Content="Manage Avatar Sets" Command="{Binding OpenAvatarSetsManagerCommand}" Style="..." />
  </StackPanel>
  ```
- Add `OpenAvatarSetsManagerCommand` to `MainWindowViewModel.cs` (lazy property pattern matching `OpenUniversalTriggersManagerCommand`):
  ```csharp
  public ICommand OpenAvatarSetsManagerCommand => _openAvatarSetsManagerCommand ??= new RelayCommand(_ =>
  {
      var window = new AvatarSetsManagerWindow
      {
          Owner = Application.Current.MainWindow,
          DataContext = new AvatarSetsManagerViewModel(this, App.AvatarImageService)
      };
      window.ShowDialog();
  });
  ```
- The `AvatarPicker` parameter `"Profile"` path in `OpenAvatarPicker` is preserved - it now writes back into the editor's `SelectedProfile` (which is the same model property the inline UI used).

### `MainWindowViewModel.cs` changes

- Add `OpenAvatarSetsManagerCommand` (above).
- Add `string AvatarSetSummaryText` = "$"{AvatarRuleProfiles.Count} sets • {AvatarRuleProfiles.Count(p => p.IsEnabled)} active"`.
- Remove `IsViewingAvatarTriggers` and `IsViewingMasterAvatar` properties (no longer drive any XAML).
- Keep all other Avatar Set-related properties and commands unchanged (`AvatarRuleProfiles`, `SelectedAvatarProfile`, `AddAvatarProfileCommand`, `DeleteSelectedAvatarProfileCommand`, etc.).
- The `OpenAvatarPickerCommand` "Profile" branch is preserved as-is.

### Localization

New `en-US` keys (and matching translations in every `.extra.json` file):

| Key | Value |
|---|---|
| `Avatar Sets Manager Title` | `Avatar Sets` |
| `Avatar Sets Subtitle Format` | `{0} total • {1} active • {2} need avatar` |
| `Avatar Sets Empty Title` | `Create your first Avatar Set` |
| `Avatar Sets Empty Body` | `Avatar Sets bundle a VRChat avatar with multiple channel-point redeems or wardrobe outfits that activate when you switch to that avatar.` |
| `Avatar Sets Toolbar New` | `New Set` |
| `Avatar Sets Toolbar Search` | `Search by name...` |
| `Avatar Sets Filter All` | `All` |
| `Avatar Sets Filter Active` | `Active` |
| `Avatar Sets Filter Disabled` | `Disabled` |
| `Avatar Sets Filter Live` | `Live Now` |
| `Avatar Sets Filter Master` | `Master` |
| `Avatar Sets Sort Name` | `Sort: By Name` |
| `Avatar Sets Sort Status` | `Sort: By Status` |
| `Avatar Sets Sort Recent` | `Sort: Recently Edited` |
| `Avatar Sets Enable All` | `Enable All` |
| `Avatar Sets Disable All` | `Disable All` |
| `Avatar Sets Delete All` | `Delete All` |
| `Avatar Sets Delete All Confirm` | `Delete all avatar sets?` |
| `Avatar Sets Delete Set` | `Delete Set` |
| `Avatar Sets Delete Set Confirm` | `Delete this avatar set?` |
| `Avatar Sets Card Test` | `Test` |
| `Avatar Sets Card Edit` | `Edit` |
| `Avatar Sets Card Pick Avatar` | `Pick Avatar` |
| `Avatar Sets Card Setup Needed` | `Setup Needed` |
| `Avatar Sets Card Disabled` | `Disabled` |
| `Avatar Sets Card Ready` | `Ready` |
| `Avatar Sets Card Live` | `● Live now` |
| `Avatar Sets Card Waiting` | `○ Waiting for this avatar` |
| `Avatar Sets Card Pick Avatar Hint` | `○ Pick an avatar to enable` |
| `Avatar Sets Card Off` | `○ Off` |
| `Avatar Sets Card Count Redeems Format` | `{0} redeems` |
| `Avatar Sets Card Count Outfits Format` | `{0} outfits` |
| `Avatar Sets Card Count Zero` | `0 redeems` |
| `Avatar Sets Mode Standard` | `Standard` |
| `Avatar Sets Mode Wardrobe` | `Wardrobe` |
| `Avatar Sets Master Badge` | `★ Master` |
| `Avatar Sets Editor Save Close` | `Save & Close` |
| `Avatar Sets Editor Cancel` | `Cancel` |
| `Avatar Sets Manage Button` | `Manage Avatar Sets` |
| `Avatar Sets Summary Format` | `{0} sets • {1} active` |

All non-English `.extra.json` files get matching translations following the project's localization translation quality rules (informal register, brand terms in English, no empty values, placeholder integrity preserved). The localization audit script runs after translation.


---

## Card state matrix

| State | Stripe | Status pill | Live text | Card opacity | Test button |
|---|---|---|---|---|---|
| Standard + Enabled + Live + Has avatar | Green | `READY` | `● Live now` (green) | 100% | Disabled (standard sets have no test path) |
| Standard + Enabled + Waiting + Has avatar | Green | `READY` | `○ Waiting for this avatar` (muted) | 100% | Disabled |
| Wardrobe + Enabled + Live + Has avatar | Green | `READY` | `● Live now` (green) | 100% | Enabled (fires first outfit test) |
| Wardrobe + Enabled + Waiting + Has avatar | Green | `READY` | `○ Waiting for this avatar` (muted) | 100% | Enabled |
| No avatar (any mode, any enabled state) | Amber | `SETUP NEEDED` | `○ Pick an avatar to enable` (amber) | 100% | Disabled |
| Standard + Disabled (any avatar state) | Gray | `DISABLED` | `○ Off` (gray) | 55% | Disabled |
| Wardrobe + Disabled (any avatar state) | Gray | `DISABLED` | `○ Off` (gray) | 55% | Enabled (fires first outfit test) |
| Disabled + No avatar | Amber | `SETUP NEEDED` + `DISABLED` (both shown) | `○ Off` (gray) | 55% | Disabled |


---

## Components summary

| File | Action | Purpose |
|---|---|---|
| `VrcTwitchOscBridge/AvatarSetsManagerWindow.xaml` | Create | The new themed manager window. |
| `VrcTwitchOscBridge/AvatarSetsManagerWindow.xaml.cs` | Create | Code-behind for window lifecycle, close button, title bar drag. |
| `VrcTwitchOscBridge/ViewModels/AvatarSetsManagerViewModel.cs` | Create | Window-level VM: filter, sort, search, editor state, commands. |
| `VrcTwitchOscBridge/ViewModels/AvatarSetCardViewModel.cs` | Create | Per-card wrapper with image load, derived pills, click commands. |
| `VrcTwitchOscBridge/Models/AvatarSetsFilterMode.cs` | Create | Enum: `All`, `Active`, `Disabled`, `LiveNow`, `Master`. |
| `VrcTwitchOscBridge/Models/AvatarSetsSortMode.cs` | Create | Enum: `ByName`, `ByStatus`, `RecentlyEdited`. |
| `VrcTwitchOscBridge/MainWindow.xaml` | Modify | Remove inline list + editor; add Manage button + summary text. |
| `VrcTwitchOscBridge/MainWindowViewModel.cs` | Modify | Add `OpenAvatarSetsManagerCommand`, `AvatarSetSummaryText`; remove `IsViewingAvatarTriggers` / `IsViewingMasterAvatar` if no other XAML binds to them. |
| `VrcTwitchOscBridge/VrcTwitchOscBridge.csproj` | Modify | Add the new `.xaml` and `.xaml.cs` files to the `<Page>` and `<Compile>` item groups (the project uses `EnableDefaultItems=false`). |
| `VrcTwitchOscBridge/Localization/*.json` | Modify | Add all new `en-US` source keys and matching entries in every `.extra.json` translation file. |


---

## Data flow

### Window open

1. User clicks "Manage Avatar Sets" button in the main window.
2. `OpenAvatarSetsManagerCommand` constructs `AvatarSetsManagerWindow` and `AvatarSetsManagerViewModel(this, App.AvatarImageService)`.
3. The VM wraps each `AvatarTriggerProfile` in an `AvatarSetCardViewModel` and adds it to its `Cards` collection.
4. Each card VM kicks off an async image load via `AvatarImageService.GetAvatarImageAsync`.
5. The window shows in populated state (cards visible) or empty state (welcome card) based on `AvatarRuleProfiles.Count`.

### Editing a set

1. User clicks a card or the `Edit` button.
2. `OpenEditorCommand` sets `SelectedProfile` and `IsEditorOpen = true`.
3. The slide-in overlay animates in (right edge).
4. User edits fields in the editor; changes flow into `SelectedProfile` via standard two-way bindings (same pattern the inline editor uses).
5. User clicks "Save & Close" → overlay closes. Card VM observes `Profile` property changes and refreshes its derived state (count, status, etc.) automatically.
6. User clicks "Delete Set" → confirmation dialog → `MainWindowViewModel.DeleteSelectedAvatarProfileCommand` removes the profile → the VM's `CollectionChanged` handler removes the card VM from `Cards` → overlay closes.

### Image loading

1. Card VM holds a `CancellationTokenSource` for its image load.
2. On `Profile.AvatarId` change, the old load is cancelled and a new one starts.
3. `AvatarImageService.GetAvatarImageAsync(avatarId, customIconPath: null, vrchatThumbnailUrl: null, ct)`:
   - In-memory cache hit → return immediately
   - Disk cache hit → load from `%LOCALAPPDATA%\CrystalRelay\Crystal Relay Save Transfer\ThemeAssets\AvatarIcons\Cache\{avatarId}.jpg` → return
   - Otherwise → download from VRChat CDN with auth cookie → save to disk → return `BitmapImage`
4. Result is set on `Image` property via UI dispatcher.
5. If download fails or `avatarId` is empty → `Image` stays as the placeholder `DrawingImage` from `AvatarImageService.GetPlaceholderImage()`.

### Profile added

1. User clicks "New Set" in the toolbar.
2. `AddNewSetCommand` calls `MainWindowViewModel.AddAvatarProfileCommand` (which calls `CreateDefaultAvatarProfile` and adds to `AvatarRuleProfiles`).
3. The VM's `CollectionChanged` handler creates a new `AvatarSetCardViewModel` and adds it to `Cards`.
4. `OpenEditorCommand` is called on the new card to open the editor immediately so the user can pick an avatar.

### Profile removed

1. `MainWindowViewModel.DeleteSelectedAvatarProfileCommand` (or `DeleteAllAvatarProfilesCommand`) removes from `AvatarRuleProfiles`.
2. The VM's `CollectionChanged` handler removes the corresponding card VM from `Cards`.
3. If the deleted profile was the master, `MainWindowViewModel` already clears the master flag (existing behavior - no new logic).

### Filter / sort / search

- `SearchText` change → view is refreshed; cards where `DisplayTitle` or `AvatarSubtitle` contains the search string (case-insensitive) are kept.
- `FilterMode` change → view is refreshed; cards matching the filter are kept.
- `SortMode` change → view is sorted.
- The five filter chips are mutually exclusive (clicking one clears the others), matching `UniversalTriggersManagerViewModel.ShowAll/Active/Disabled/NeedsFix/Fooma` pattern.

### Avatar Picker from the editor

1. User clicks "Browse..." in the editor.
2. The button calls `MainWindowViewModel.OpenAvatarPickerCommand` with `parameter: "Profile"`.
3. The existing `AvatarPickerWindow` opens modally (reused as-is).
4. On selection, the existing handler writes the picked `Id` and `Name` into `SelectedProfile.AvatarId` and `SelectedProfile.AvatarName`.
5. The card VM observes `Profile.AvatarId` change and re-fetches the image. The card updates from "SETUP NEEDED" dashed placeholder to the real thumbnail.


---

## Error handling & edge cases

- **No avatar picked** → card shows the dashed `+ PICK AVATAR` placeholder, Test button disabled, status pill is `SETUP NEEDED`, stripe is amber. Card is still editable. The editor's "Browse..." button is the only way out.
- **Avatar image download fails** → card falls back to the existing `AvatarImageService.GetPlaceholderImage()` (purple head/body silhouette). No error toasts, no retry button. Same behavior as the Avatar Picker.
- **No VRChat login** → `thumbnailUrl` is null, all cards show the placeholder. No special error state. Same as the Avatar Picker.
- **Master profile deletion** → if the user deletes the set that is `IsMasterProfile`, the existing `DeleteSelectedAvatarProfileCommand` already clears the master flag. No new logic.
- **Deletion confirmation** → the existing `MessageBox.Show("Delete this avatar set?")` pattern from the inline UI is reused. "Delete All" uses `MessageBox.Show("Delete all avatar sets?")`.
- **Empty state** → if `AvatarRuleProfiles.Count == 0`, show a welcome card with a big "Create your first Avatar Set" button (matches `UniversalTriggersManagerWindow` empty-state pattern at lines 586-665).
- **Save transfer** → `AvatarProfiles` is already in `AppSettings` and flows through the existing save/transfer pipeline. No new save logic.
- **Image cache cleanup** → when a profile is deleted, its on-disk image cache file (`Cache/{avatarId}.jpg`) is NOT deleted (intentional - same avatar may be picked again, and the file is small). The in-memory cache entry is evicted when the card VM is disposed.
- **Cancellation** → when the user closes the window mid-load, all card VM `CancellationTokenSource`s are cancelled. In-flight image loads abort gracefully (the `AvatarImageService` already handles `OperationCanceledException`).
- **Window closed mid-edit** → any unsaved changes in the editor are persisted via the same two-way bindings the inline UI used. The editor doesn't have an explicit "dirty" flag; this matches the existing inline UI behavior.
- **Multiple manager windows** → `OpenAvatarSetsManagerCommand` checks if a manager window is already open and brings it to front instead of creating a duplicate (matches `AvatarLibraryManagerWindow` lifecycle).
- **Theme switching** → the manager window re-evaluates theme brushes when `ThemeManager.CurrentTheme` changes (same pattern `UniversalTriggersManagerWindow` uses for its `PanelBrush`, `AccentBrush`, etc.).


---

## Testing approach

No new test framework needed (this is a UI feature). Verification will be:

### Build verification

After every code change:
```bash
dotnet build "E:\!!!Program to work on\Proper Crystal Relay\VrcTwitchOscBridge\VrcTwitchOscBridge.csproj" --no-restore
```

### Manual smoke test

Launch via `Launch-Crystal-Relay-Debug.bat`:
1. Open the main window → click "Manage Avatar Sets" → new window opens.
2. Verify all four default card states render correctly (Ready+Live, Ready+Waiting, No Avatar, Disabled).
3. Click "+ New Set" → new card appears with dashed `+ PICK AVATAR` placeholder, editor opens.
4. Click "Browse..." in the editor → Avatar Picker opens → pick an avatar → picker closes → card thumbnail appears.
5. Edit the set name → close editor → card title updates.
6. Toggle the set on/off in the card → status pill, stripe color, and opacity update.
7. Click "Edit" on a wardrobe mode set → wardrobe editor appears in the overlay.
8. Click "Delete Set" in the editor → confirmation dialog → confirm → card removed.
9. Type in the search box → only matching cards remain.
10. Click filter chips (All / Active / Disabled / Live Now / Master) → only matching cards remain.
11. Change sort → cards reorder.
12. Click "Disable All" → all cards dim, all stripes turn gray, all toggles off.
13. Click "Enable All" → all cards restore.
14. Click "Delete All" in the footer → confirmation dialog → confirm → all cards removed, empty state shows.
15. Close the window → reopen → all changes persisted.

### Regression test

Confirm these still work unchanged:
1. Avatar Change section (any avatar) - pickers, current-avatar detection, profile icon display.
2. Power Up Redeem Library section - rules, Bits counting, avatar scope.
3. Supporter Growth / Supporter Override.
4. Universal Triggers manager window.
5. Avatar Library manager window.
6. Twitch reward sync (set-trigger master reward activation, hide/disable, cooldown color).
7. Fire Sale discount on set-trigger master reward.
8. Avatar Scaling integration with Avatar Set.

### Theme test

Switch between at least 2 themes (`Void Crystal` + one other) and confirm:
1. Card `PanelBrush` background matches theme.
2. Status stripe brushes adapt.
3. Pill colors are readable on the new background.
4. Theme-specific fonts render correctly.

### Localization audit

Run the localization audit script after all `en-US` keys are added and translated. Confirm:
1. Every `en-US` key has a match in every `.extra.json` file (or is intentionally English-only per the project rules).
2. No empty values.
3. No placeholder name mismatches.
4. Brand terms (Crystal Relay, Bits, Subs, OSC, VRChat, Twitch) preserved across translations.

### Save transfer round-trip

1. Add 3 avatar sets with different states (one Ready, one No Avatar, one Disabled).
2. Use the existing save transfer export.
3. Clear app data.
4. Import the save.
5. Confirm all 3 sets appear with their avatars, names, and states intact.
