# Universal Triggers Rebuild Design Spec

**Date:** 2026-06-10
**Scope:** Universal Triggers UI layer only - the runtime engine, models, persistence, Fooma importer, EventSub routing, and managed-reward sync are preserved unchanged.
**Goal:** Strip out the broken legacy inline UI in `MainWindow.xaml` and the orphaned half-built new-UI files, then rebuild the management UI as a dedicated themed secondary window (`UniversalTriggersManagerWindow.xaml`) with a card-grid + slide-out editor that matches the friendliness level the user signed off on during brainstorming.


---

## Current State

### What works (do not touch)

- **Runtime engine** (`BridgeCoordinator.cs`): `ParseUniversalEvent`, `ExecuteMatchingUniversalTriggersAsync`, `ExecuteUniversalTriggerAsync`, per-trigger `SemaphoreSlim` queue, global + per-user delay maps, `BuildUniversalOscPacket`, `SendUniversalActionResetAsync`. All seven EventSub subscription types route through here.
- **Models**: `UniversalTriggerRule.cs`, `UniversalTriggerAction.cs`, `UniversalTriggerType.cs`, `UniversalTriggerValueKind.cs`.
- **Fooma importer + fusion**: `Services/FoomaInteractionConfigImporter.cs` and `Services/UniversalTriggerFusionService.cs`.
- **Persistence**: `AppSettings.UniversalTriggers` collection serialized by `SettingsStore.cs` (`PersistedUniversalTriggerRule` DTO).
- **Runtime snapshot**: `BridgeRuntimeConfiguration.UniversalTriggers` is populated from the settings collection via `TryToUniversalSnapshot`.
- **Twitch managed-reward sync**: `MainWindowViewModel.CreateManagedRewardTargetForUniversalTrigger`, `EnsureCurrentAvatarParametersReadyForUniversalRewardSyncAsync`, `HasUniversalTriggerAvatarParameterGate`, `GetUniversalTriggerRequiredAvatarParameterAddresses`, `IsUniversalTriggerReadyForCurrentAvatarJson`, `SynchronizeManagedChannelPointRewardsAsync`, `UniversalManagedRewardStatusText`.
- **Reward Fire Sale**: `ApplyRewardFireSaleDiscount(trigger.RewardCost, trigger.RewardSyncMode)` discount path for managed Universal rewards is intact.
- **Coupling note**: `UniversalIncomingEvent` shape is reused by Cash Payments (`HandleCashPaymentEventAsync`) and Power-ups (`ToUniversalPowerUpEvent`). The DTO and `UniversalTriggerType.Bits` enum value must not be renamed or removed.

### What is broken

- **Legacy inline UI** in `MainWindow.xaml` (~760 lines spanning ~lines 4355-9241 and DataTemplates at 1584/1615/8753/9170) still renders, but commit `7c30744` deleted the pass-through commands it bound to (`AddUniversalTriggerCommand`, `EnableAllUniversalTriggersCommand`, `DisableAllUniversalTriggersCommand`, `DeleteAllUniversalTriggersCommand`, `RemoveSelectedUniversalTriggerCommand`, `TestSelectedUniversalTriggerCommand`, `AddUniversalTriggerActionCommand`, `RemoveSelectedUniversalTriggerActionCommand`, `ImportFoomaInteractionConfigCommand`). WPF binding failures are runtime-only, so the build is GREEN but every button silently does nothing in the running app.
- **Orphaned new-UI files** never embedded in `MainWindow.xaml`:
  - `UniversalTriggersView.xaml` (+ `.xaml.cs`)
  - `UniversalTriggerCreateWizardWindow.xaml` (+ `.xaml.cs`)
  - `UniversalTriggerImportPreviewWindow.xaml` (+ `.xaml.cs`)
  - `ViewModels/UniversalTriggersViewModel.cs`
  - `ViewModels/UniversalTriggerCardViewModel.cs`
  - `ViewModels/UniversalTriggerCreateWizardViewModel.cs`
  - `ViewModels/UniversalTriggerImportPreviewViewModel.cs`

  `MainWindowViewModel.UniversalTriggersViewModel` (lazy property at line 2527) constructs the orphaned VM but no XAML embeds the corresponding view.
- **In-flight rework spec** `docs/superpowers/specs/2026-06-10-universal-triggers-rework-design.md` is the half-finished plan that produced the orphaned files. It is superseded by this rebuild spec.

### Why not just rewire the orphaned files

The user reviewed the orphaned design and asked for a different shape: a dedicated themed secondary window (like `AvatarLibraryManagerWindow`) instead of an embedded UserControl, an empty-state landing page that flips to a populated management view, fixed-dimension cards, collapsible event-type sections, and global Enable/Disable All controls. Salvaging the orphaned files would require gutting most of them; building fresh against the agreed design is cleaner and less risky.


---

## Goals

1. Remove the broken legacy UI cleanly without disturbing the runtime engine.
2. Remove the orphaned new-UI files cleanly so they do not confuse future development.
3. Ship a friendly themed secondary window (`UniversalTriggersManagerWindow.xaml`) the user explicitly designed with:
   - empty-state landing with two big cards (Import Fooma / Create New)
   - populated state with toolbar + filter chips + collapsible event-type sections + uniform 240x168 cards + slide-out editor panel
   - Fooma cat pixel-art icon at five sizes across the UI (12px badge, 14px chip, 18px toolbar button, 22px title bar, 80px landing card)
   - global Enable All / Disable All controls in the toolbar
   - Delete All in the bottom action bar (destructive stays out of the way)
4. Preserve every saved Universal Trigger across the rebuild (`AppSettings.UniversalTriggers` is untouched).
5. Preserve every feature in the runtime: chat command, channel-point reward (managed + linked-existing), bits, sub, gift sub, follow, Fooma import, test/simulate, managed reward sync, fire-sale discount, EventSub routing, queue + delay gates.
6. Other Crystal Relay systems must keep working through the demolition: Avatar Sets, Avatar Change, Avatar Roulette, Movement Redeems, Avatar Scaling (incl. Power-up + Cash Payment scale paths that reuse `UniversalIncomingEvent`), Bits + Subs overrides, Reward Fire Sale, Cash Payments, Twitch Chatbox, About page status.

## Out of Scope

- Any change to `BridgeCoordinator` runtime hot path beyond removing dead helper references.
- Any change to `UniversalTriggerRule` / `UniversalTriggerAction` / `UniversalTriggerType` / `UniversalTriggerValueKind` models.
- Any change to `FoomaInteractionConfigImporter` or `UniversalTriggerFusionService`.
- Any change to `SettingsStore` persistence shape (no schema migration).
- Any change to `BridgeRuntimeConfiguration` snapshot shape.
- Any change to `UniversalIncomingEvent` DTO or its consumers in Cash Payments / Power-ups.
- Any change to other Crystal Relay sections.
- No new VRChat / Twitch API endpoints, no new EventSub subscription types.
- No reintroduction of a create wizard (the orphaned 4-step wizard is dropped; the slide-out editor opens directly on a new blank trigger when "Create New" is clicked, prefilled by trigger type).
- The legacy text-based "what is the !world command" warning and similar dense help blocks are intentionally not ported - the new cards and slide-out editor self-document via plain English summaries and field hints.


---

## Architecture

### Entry point in the main window

`MainWindow.xaml` keeps a single sidebar button labeled "Universal Triggers". Clicking it raises `MainWindowViewModel.OpenUniversalTriggersManagerCommand`, which constructs and shows the secondary window as a non-modal dialog parented to the main window (matching `AvatarLibraryManagerWindow` lifecycle).

There is no longer a "Universal Triggers" tab inside the main workspace. The main workspace no longer scopes filter / search / selection state to Universal Triggers. `IsViewingUniversalTriggers` and related sidebar state on `MainWindowViewModel` are removed.

### Secondary window

`UniversalTriggersManagerWindow.xaml` (+ `.xaml.cs`):

- Custom-themed chrome (`WindowStyle="None"`, `WindowChrome` with `CaptionHeight="0"`), matches `AvatarLibraryManagerWindow`.
- `Width="980"`, `Height="640"`, `MinWidth="720"`, `MinHeight="480"`, `WindowStartupLocation="CenterOwner"`.
- Single window for both Phase A (empty state) and Phase B (populated) - switched via a `DataTrigger` on `Settings.UniversalTriggers.Count == 0`.
- Constructed by `MainWindowViewModel.OpenUniversalTriggersManagerCommand` with the `UniversalTriggersManagerViewModel` as its DataContext.

### View model

`ViewModels/UniversalTriggersManagerViewModel.cs`:

- Holds a reference to `AppSettings` (via constructor injection from `MainWindowViewModel`) so it can read/write `Settings.UniversalTriggers`.
- Exposes filtered `CollectionView`s for each section group: 💬 Chat / 🎁 Reward / 💎 Bits / ⭐ Subs & Gift Subs (combined) / ❤️ Follow. Five sections total - Subs and Gift Subs share a section because they belong to the same "viewer support" concept and a streamer's mental model rarely separates them. Each card inside the combined section still shows a `SUB` or `GIFT SUB` type pill.
- Exposes filter chip state (`ShowAll` / `ShowActive` / `ShowDisabled` / `ShowNeedsFix` / `ShowFooma`) - mutually exclusive (clicking one clears others).
- Exposes `SearchText` (filters by name, command text, reward title, OSC address - case-insensitive substring).
- Exposes per-section collapsed state (`IsChatSectionCollapsed`, etc.) persisted via `AppSettings` so collapse state survives app restarts.
- Exposes commands:
  - `AddNewTriggerCommand` (CommandParameter = `UniversalTriggerType` enum) - creates a blank trigger with that type, opens the slide-out editor immediately
  - `ImportFoomaCommand` - opens file picker, runs `FoomaInteractionConfigImporter.ImportAsync`, appends results
  - `EnableAllCommand`, `DisableAllCommand`, `EnableSectionCommand`, `DisableSectionCommand`
  - `DeleteAllCommand` - shows `ThemedDialogWindow.ShowYesNo` confirmation
  - `OpenEditorCommand` (CommandParameter = `UniversalTriggerRule`) - opens the slide-out editor for that trigger
  - `CloseEditorCommand`
  - `TestTriggerCommand`, `DeleteTriggerCommand` (operate on `SelectedTrigger` in the slide-out editor)
  - `SortModeCommand` (CommandParameter = enum: ByType / ByStatus / ByName / RecentlyEdited)
  - `CollapseAllCommand`, `ExpandAllCommand`
- The view model surfaces a derived `CardViewModel` per `UniversalTriggerRule`: type chip text, status pill (`Ready` / `Warn` / `Disabled`), description string, "From Fooma" flag, icon glyph. Derivation logic ports from the orphaned `UniversalTriggerCardViewModel` but lives in the new manager VM.
- The view model is created once per window open and disposed when the window closes (re-creating clears any transient filter state). Saved triggers always come from `Settings.UniversalTriggers`.

### Editor panel

The slide-out is a `Border` overlay inside `UniversalTriggersManagerWindow.xaml` (not a separate `Window`). When `IsEditorOpen == true`, the overlay slides in from the right at `Width="480"` with a semi-transparent backdrop (`#80000000`) covering the grid behind it. Saving the editor calls `SettingsStore.SaveAsync` and marks the trigger dirty for managed-reward sync.

The editor body is built from cards that mirror the orphaned design but with the simpler set of fields the user signed off on:

1. **Trigger settings card** - Name, Enabled toggle, Type, Command (chat), Permission (chat), Reward Sync Mode (reward), Reward Title (reward), Min/Max Bits (bits), Tier + Min/Max Months (subs), Global Delay, User Delay, Run Random Action.
2. **Twitch reward card** (only when `UsesChannelPointReward`) - Reward Sync Mode dropdown (`CreateOrManage` / `LinkExisting`), Reward Title (managed only), Cost, Cooldown, Ready Color, Cooldown Color, Delete-when-inactive checkbox (managed only, defaults off per AGENTS.md). A read-only status line shows whether the reward is currently visible on Twitch and why ("Visible", "Hidden - current avatar missing param `{name}`", "Pending sync - reconnect Twitch broadcaster", etc.). There is no manual "Hide" toggle - visibility is derived automatically from `HasUniversalTriggerAvatarParameterGate` + `IsUniversalTriggerReadyForCurrentAvatarJson`, as the runtime already does.
3. **Avatar Readiness card** (always visible) - lists required avatar parameters from this trigger's actions, marks each as Found / Missing for the current avatar.
4. **OSC Actions card** - editable list of `UniversalTriggerAction` entries (Address, ValueKind, TargetValue, DefaultValue, DurationSeconds, AddToQueue). Add / Remove buttons.

Footer: Delete (red, left) + Test now + Save (right).


---

## UI Design

### Phase A - Empty state (`Settings.UniversalTriggers.Count == 0`)

Centered vertically and horizontally inside the window body (below the custom title bar). Layout:

- Top: small sparkle glyph (text "Universal Triggers ✨" or `loc:Translate 'Universal Triggers'`) - reuses the standard themed title bar at the top of the window.
- Welcome text: `loc:Translate 'Universal Triggers Welcome Title'` + `loc:Translate 'Universal Triggers Welcome Body'`.
- Two cards side by side, 240px wide each, both 18px padded:

  **Card 1 - Import Fooma Config**:
  - Background: `rgba(168,85,247,.15)`, border: 1px solid `{DynamicResource AccentBrush}`, border-radius: 14.
  - Icon: 80x80 `Image` of `pack://application:,,,/Assets/fooma-icon.png` with `RenderOptions.BitmapScalingMode="NearestNeighbor"`.
  - Title: `loc:Translate 'Universal Triggers Welcome Import Title'` (bold).
  - Body: `loc:Translate 'Universal Triggers Welcome Import Body'`.
  - Button: `loc:Translate 'Universal Triggers Welcome Import Action'` (accent background, white text) - bound to `ImportFoomaCommand`.

  **Card 2 - Create New Trigger**:
  - Background: `{DynamicResource PanelBrush}`, border: 1px solid `{DynamicResource BorderBrush}`, border-radius: 14.
  - Icon: 🛠️ hammer-and-wrench emoji, font-size 48, centered in an 80px box (matches the Fooma icon's vertical footprint).
  - Title: `loc:Translate 'Universal Triggers Welcome Create Title'`.
  - Body: `loc:Translate 'Universal Triggers Welcome Create Body'`.
  - Button: `loc:Translate 'Universal Triggers Welcome Create Action'` (transparent background, accent border) - bound to `AddNewTriggerCommand` with default parameter `UniversalTriggerType.ChatCommand`.

### Phase B - Populated state (`Settings.UniversalTriggers.Count >= 1`)

Three-row layout below the custom title bar.

**Row 1: Title bar**
- Left: "✨ Universal Triggers" + subtitle showing live counts ("12 saved | 9 active | 2 need a quick fix"). Counts bind to derived properties on the VM that recompute when the underlying collection changes.
- Right (right-to-left): close button (✕), Import Fooma button (with 18x18 Fooma icon), `+ New` accent button (opens the editor on a blank Chat Command trigger), search box (`Width="200"`, placeholder `loc:Translate 'Universal Triggers Search Placeholder'`).

**Row 2: Filter / global controls bar**
- Filter chips (mutually exclusive): All | Active | Disabled | Needs Fix | From Fooma. Each chip shows live count. The "From Fooma" chip has a 14x14 Fooma icon on its left edge.
- Vertical divider.
- Global Enable All button (green-tinted), Disable All button (amber-tinted).
- Spacer.
- Sort dropdown: By Type (default) | By Status | By Name | Recently Edited.
- Collapse all / Expand all buttons.

**Row 3: Body (scrolling)**

Each event-type group is a `Border` with `loc:Translate` header text, a count chip, a section "Disable All" mini button, and a chevron (▴ when expanded, ▾ when collapsed). Clicking anywhere on the header toggles `IsXxxSectionCollapsed`. Below the header, when expanded, is a `UniformGrid` of cards (or `ItemsControl` with a `WrapPanel` ItemsPanel for variable column count).

Order of sections (collapsing Subs and Gift Subs into one shared section to match the mockups the user signed off on):

1. 💬 Chat Commands
2. 🎁 Channel Point Rewards
3. 💎 Bits
4. ⭐ Subs & Gift Subs (combined - each card's pill shows `SUB` or `GIFT SUB`)
5. ❤️ Follows

A section is hidden entirely when its filtered list is empty for the current filter + search combination.

**Section header count suffix selection rule:**

The header text after the section name is one of these patterns based on the current section's card states:

- All cards active, none hidden, none disabled: `({active} active)` - uses `Universal Triggers Section Active Suffix`.
- Some cards hidden (warn-stripe), no disabled cards: `({active} active, {hidden} hidden)` - uses `Universal Triggers Section Active Hidden Suffix`.
- Some cards disabled (grey-stripe), no hidden cards: `({active} active, {off} off)` - uses `Universal Triggers Section Off Suffix`.
- Mix of all three states: `({active} active, {hidden} need fix, {off} off)` - uses `Universal Triggers Section Mixed Suffix`.
- Section has zero cards in current filter view: section is hidden entirely (no header rendered).

### Card template

Fixed `Width="240"`, `Height="168"`, `Padding="10,12,10,12"`, `BorderThickness="1,3,1,1"` so the top border edge becomes the status stripe (3px green / amber / grey based on status). `Background={DynamicResource PanelBrush}`.

Card content (top to bottom):

- **Top row (height 42)**: 36x36 emoji icon box, name+pills column, 32x18 toggle. Pills row uses a `WrapPanel` capped to one line via clipping; pills are: type pill (e.g. `CHAT`), status pill (e.g. `Ready` / `Avatar missing` / `Hidden` / `Disabled`), and source pill (`Fooma` with the 12x12 Fooma icon) if imported.
- **Description (flex, 3-line clamp)**: plain-English summary built by the card VM. WPF `TextBlock` with two `Run` children where needed - one plain text Run, one `FontFamily="{DynamicResource MonoFontFamily}"` Run for the command / address tokens (use a small monospace `Run` instead of trying to render backticks):
  - Chat: `Types ` + `{Command}` + ` -> {ActionSummary}`
  - Reward (managed): `VRC: {Title}  ·  {Cost} pts  ·  {Cooldown}s cooldown  ·  {ActionSummary}`
  - Reward (linked): `Listens to your reward {Title}. Crystal Relay never modifies the reward.`
  - Bits: `Cheer {MinBits}-{MaxBits} bits -> {ActionSummary}` (omit `-{MaxBits}` if `MaxBits == 0` / unlimited)
  - Subs: `New sub at {Tier}+ -> {ActionSummary}`
  - Gift Subs: `Gift sub -> {ActionSummary}`
  - Follow: `New follower -> {ActionSummary}`
  `{ActionSummary}` formats: single action -> the verb form ("avatar waves 1s", "spins right 3s"); multiple actions -> count + "(random)" if `ExecuteRandomAction`, else "(all in order)".
- **Buttons row (height 26)**: `⚡ Test` (half width) + `⚙ Edit` (half width). Both styled as secondary buttons. Clicking the card body (not the buttons or toggle) also fires `OpenEditorCommand`.

Long names truncate with ellipsis. Full name shows in a `ToolTip` on the name `TextBlock` and in the editor when opened. The card uses `Cursor="Hand"` when hovered.

Disabled cards get `Opacity="0.65"` and a grey status stripe.

### Slide-out editor panel

Implemented as an overlay `Grid` spanning all rows of the window, visible only when `IsEditorOpen == true`:

```
<Grid Visibility="{Binding IsEditorOpen, Converter={StaticResource BoolToVisibilityConverter}}">
  <Border Background="#80000000"/>                       <!-- backdrop -->
  <Border Width="480" HorizontalAlignment="Right" .../>   <!-- panel -->
</Grid>
```

Panel rows:
- Title bar (height auto): `{Binding SelectedTrigger.Name}` + close button.
- Body (`ScrollViewer` filling middle): four cards described in Architecture > Editor panel.
- Footer (height auto): Delete (left, red) + spacer + Test now (secondary) + Save (accent).

Backdrop click closes the editor (calls `CloseEditorCommand`).

Save behavior: the editor binds two-way against `SelectedTrigger` properties (so changes are live), but the Save button is what triggers `SettingsStore.SaveAsync` + reward sync. Cancel discards by reloading from a snapshot taken on open (implemented as a small backup/restore in the VM).


---

## Removal Plan

Delete these files entirely:

- `VrcTwitchOscBridge/UniversalTriggersView.xaml`
- `VrcTwitchOscBridge/UniversalTriggersView.xaml.cs`
- `VrcTwitchOscBridge/UniversalTriggerCreateWizardWindow.xaml`
- `VrcTwitchOscBridge/UniversalTriggerCreateWizardWindow.xaml.cs`
- `VrcTwitchOscBridge/UniversalTriggerImportPreviewWindow.xaml`
- `VrcTwitchOscBridge/UniversalTriggerImportPreviewWindow.xaml.cs`
- `VrcTwitchOscBridge/ViewModels/UniversalTriggersViewModel.cs`
- `VrcTwitchOscBridge/ViewModels/UniversalTriggerCardViewModel.cs`
- `VrcTwitchOscBridge/ViewModels/UniversalTriggerCreateWizardViewModel.cs`
- `VrcTwitchOscBridge/ViewModels/UniversalTriggerImportPreviewViewModel.cs`

Remove corresponding `<Page>` and `<Compile>` entries from `VrcTwitchOscBridge/VrcTwitchOscBridge.csproj`.

Delete from `VrcTwitchOscBridge/MainWindow.xaml`:

- `DataTemplate DataType="{x:Type models:UniversalTriggerRule}"` at ~line 1584
- `DataTemplate DataType="{x:Type models:UniversalTriggerAction}"` at ~line 1615
- "Universal Triggers" sidebar button at ~line 3770 is REPLACED by a button that fires `OpenUniversalTriggersManagerCommand` instead of `ShowUniversalTriggersCommand`. The button keeps its label, icon styling, and position. The `IsViewingUniversalTriggers` `DataTrigger` is removed.
- All `DataTrigger Binding="{Binding IsViewingUniversalTriggers}"` blocks (~lines 3776, 3910, 4311, 4495, 4675, 5533, 5711, 8477, 9241, 10776) - these scoped main-workspace UI to Universal Triggers and are no longer needed.
- The header / filter strip at ~lines 4336-4365 (Add / Enable All / Disable All / Delete All buttons that were scoped to Universal Triggers).
- The `ListBox ItemsSource="{Binding Settings.UniversalTriggers}"` at ~line 4666 and the empty-state `MultiDataTrigger` at ~lines 4811-4812.
- The inline editor block at ~lines 8477-9241 (Universal Trigger editor card with its embedded `DataTemplate` blocks at 8753, 9170).
- The `MultiDataTrigger` at ~lines 10804-10805 (`SelectedUniversalTrigger == null` empty-state).

Delete from `VrcTwitchOscBridge/MainWindow.xaml.cs`:

- The `UniversalTriggerRule` cases in the cooldown color switch at lines 378, 427, 430 (the new editor handles per-rule cooldown colors itself).
- `OnFoomaHelpButtonClicked` at line 344 + the `FoomaTwitchInteractionUrl` constant at line 38 ARE PRESERVED but become unreferenced after removal. Move both into `UniversalTriggersManagerWindow.xaml.cs`. Add a small "?" help button next to the empty-state landing's "Import Fooma Config" card title and next to the toolbar's "Import Fooma" button in the populated view; both bind to a new `OpenFoomaHelpCommand` that calls into the same dialog + URL flow. Add localization keys `Universal Triggers Fooma Help Title` and `Universal Triggers Fooma Help Body` (translations needed in every `*.extra.json`).

Delete or repurpose in `VrcTwitchOscBridge/ViewModels/MainWindowViewModel.cs`:

- `IsViewingUniversalTriggers` property and its `RaisePropertyChanged` callers.
- `SelectedUniversalTrigger`, `SelectedUniversalTriggerAction`.
- `UniversalTriggerTypes`, `UniversalTriggerValueKinds` collection properties (move to the new manager VM).
- `ShowUniversalTriggersCommand` is REPLACED by `OpenUniversalTriggersManagerCommand`.
- The lazy `UniversalTriggersViewModel` property at line 2527 - DELETED (orphan VM is gone).
- `ImportFoomaInteractionConfigAsync` at line 7615 - PRESERVED but called from the new manager VM. The method stays on `MainWindowViewModel` only if other systems also need it; otherwise it migrates into the new manager VM. Audit before moving.
- `UniversalManagedRewardStatusText` at line 1930 - PRESERVED (still needed by managed-reward sync), but the new manager window may also surface it inline.

Keep entirely unchanged:

- All `CreateManagedRewardTargetForUniversalTrigger`, `EnsureCurrentAvatarParametersReadyForUniversalRewardSyncAsync`, `HasUniversalTriggerAvatarParameterGate`, `GetUniversalTriggerRequiredAvatarParameterAddresses`, `IsUniversalTriggerReadyForCurrentAvatarJson`, `SynchronizeManagedChannelPointRewardsAsync`, `ApplyRewardFireSaleDiscount` logic. These are the bridge between the UI and the managed-reward sync pipeline; they only need the model objects to exist, which they still do.
- `BridgeRuntimeConfiguration.UniversalTriggers` / `UniversalTriggerRuleSnapshot` / `UniversalTriggerActionSnapshot` / `TryToUniversalSnapshot` / `ToUniversalActionSnapshot` / `IsUniversalTriggerFilterReady` / `CreateManualTestSnapshot`.
- `BridgeCoordinator` Universal Trigger code (all sites).
- `FoomaInteractionConfigImporter`, `UniversalTriggerFusionService`.
- `SettingsStore` `PersistedUniversalTriggerRule` DTO + load/save paths.
- `AppSettings.UniversalTriggers` collection.
- All Universal Trigger model classes (`UniversalTriggerRule`, `UniversalTriggerAction`, `UniversalTriggerType`, `UniversalTriggerValueKind`).


---

## Implementation Order

Each step has a verify gate. Do not move to the next step until the gate passes.

### Step 0 - Backup

Run `Backup-Crystal-Relay-Project.ps1` to capture a raw-source snapshot under `Backups\v3.1.9\` before any deletion. AGENTS.md mandates this for major UI changes.

**Gate:** new backup ZIP exists under `Backups\v3.1.9\` with today's timestamp.

### Step 1 - Add Fooma asset

Copy `C:\Users\screm\Downloads\pp_background_removed.png` to `VrcTwitchOscBridge/Assets/fooma-icon.png`. Add a `<Resource Include="Assets\fooma-icon.png" />` entry to `VrcTwitchOscBridge.csproj`.

**Gate:** `dotnet build` succeeds. File present in `Assets`.

### Step 2 - Demolition

Delete the orphaned files listed in Removal Plan. Remove their csproj entries. Rip out the old inline Universal Triggers UI from `MainWindow.xaml`. Rip out the `IsViewingUniversalTriggers` plumbing from `MainWindowViewModel`. Replace the sidebar "Universal Triggers" button with one that fires `OpenUniversalTriggersManagerCommand` (the command itself can be a stub that does nothing for this step).

**Gate:**
- `dotnet build "VrcTwitchOscBridge\VrcTwitchOscBridge.csproj"` succeeds.
- Manual launch via `Launch-Crystal-Relay-Debug.bat`. Confirm:
  - Main window loads.
  - Avatar Sets, Avatar Change, Avatar Roulette, Movement Redeems, Avatar Scaling, Bits + Subs overrides, Cash Payments, Reward Fire Sale, Twitch Chatbox, About page all still functional.
  - Clicking the Universal Triggers sidebar button does nothing yet (or shows a debug placeholder window).
  - No XAML binding errors in the debug output window for the sections we did not touch.
- Power-up scale and Cash Payment scale paths that share `UniversalIncomingEvent` still trigger their actions.

### Step 3 - Window shell + empty-state landing

Create `UniversalTriggersManagerWindow.xaml` (+ `.xaml.cs`) with custom themed chrome (modeled on `AvatarLibraryManagerWindow`). Create `ViewModels/UniversalTriggersManagerViewModel.cs` with constructor + `Settings` reference + computed empty-state flag. Wire `OpenUniversalTriggersManagerCommand` to construct + show the window. Implement Phase A landing (sparkle title, welcome text, two cards with the Fooma asset and the hammer emoji). Wire `ImportFoomaCommand` to reuse the existing `FoomaInteractionConfigImporter`. Wire `AddNewTriggerCommand` to create a blank `UniversalTriggerRule`, append to `Settings.UniversalTriggers`, save, and open the editor (stub for now).

**Gate:**
- Build succeeds.
- Launching the app and clicking the Universal Triggers sidebar button opens the new window.
- With zero triggers saved, the window shows the empty-state landing with both cards visible.
- Clicking "Choose file..." opens the file picker; selecting a valid Fooma config appends triggers and the window flips to Phase B (currently a placeholder).
- Clicking "Start blank" creates a new trigger and flips to Phase B.

### Step 4 - Populated state shell

Implement Phase B layout: title bar + filter chips + global Enable/Disable All + sort dropdown + collapse/expand-all + empty body placeholder. Wire all chip commands, search text filtering, and the Enable All / Disable All commands.

**Gate:**
- Build succeeds.
- After importing a Fooma config, the window flips to Phase B and shows the toolbar.
- Filter chips update visibly (counts + selected highlight). Search text shrinks the visible group counts.
- Enable All / Disable All toggle `IsEnabled` on every rule and persist.

### Step 5 - Section grids + cards

Implement five collapsible event-type sections (Chat / Reward / Bits / Subs & Gift Subs combined / Follows) with the uniform 240x168 card template. Card content uses the plain-English summary logic. Toggle persists immediately. Test button fires `coordinator.SendTestUniversalTriggerAsync(CreateManualTestSnapshot(rule), default)`. Edit button opens the slide-out editor (stub for now).

**Gate:**
- Build succeeds.
- Cards appear correctly grouped, status stripe shows the right color, toggle persists, Test button fires an OSC packet visible in Crystal Relay's debug log.
- Section headers collapse/expand. Collapse state persists across app restart.
- Filter chips correctly hide non-matching cards and empty sections.

### Step 6 - Slide-out editor panel

Implement the 480px overlay with backdrop. Build the four editor cards (Trigger settings, Twitch reward conditional, Avatar Readiness, OSC Actions). Wire Save / Test now / Delete / Close. Implement snapshot/restore for Cancel-on-backdrop.

**Gate:**
- Build succeeds.
- Clicking Edit on a card opens the panel; saving persists to disk; backdrop click closes without saving.
- Trigger settings card hides irrelevant fields by trigger type (Command field only for Chat, Reward fields only for Reward, etc.).
- Avatar Readiness card lists required params and marks each Found/Missing for the current avatar.
- OSC Actions card lets you add/remove actions with all six fields editable.
- Managed-reward sync runs after Save (verify via existing `UniversalManagedRewardStatusText`).

### Step 7 - Localization + audit

Add new keys to `Localization/en-US.extra.json`. Translate into all non-English `*.extra.json` files per AGENTS.md "Localization Translation Quality Rules". Run the localization audit.

**Gate:**
- `dotnet run --project LocalizationAudit` passes (no missing keys, no empty values, all placeholders preserved).

### Step 8 - End-to-end simulation

Walk through the full streamer journey as a test:
1. Launch app with `Settings.UniversalTriggers` empty -> open window -> see Phase A.
2. Click Import Fooma -> pick a real Fooma config -> see Phase B with all triggers grouped.
3. Filter / search / sort / collapse / expand / Enable All / Disable All.
4. Click Edit on a chat trigger -> change name -> Save -> card updates.
5. Click Edit on a managed reward trigger -> change cost -> Save -> verify Twitch reward updates (visible in Twitch Creator Dashboard).
6. Test now on each card type -> verify OSC packet fires (via debug log).
7. Switch VRChat avatar -> reward visibility re-syncs based on current avatar params (status stripe / status pill updates).
8. Delete a trigger from the editor -> confirm dialog -> trigger gone.
9. Delete All -> confirm -> Phase A returns.
10. Close + reopen window -> collapse state and saved triggers persist.

**Gate:** all ten scenarios pass without UI hangs, build errors, or runtime exceptions.

### Step 9 - Build + final smoke

`dotnet build` clean. Run `Check-Crystal-Relay-Dependencies.ps1` (no new vulnerable packages should appear since we did not add NuGet refs). Update `CHANGELOG.txt` + `RELEASE-CHANGE-RECORD.txt` under the active dev version per AGENTS.md changelog workflow.

**Gate:** build green, no new vulnerabilities, changelog updated.


---

## Localization Keys

New keys to add to `Localization/en-US.extra.json` (and translated into every other `*.extra.json` per AGENTS.md):

- `Universal Triggers Welcome Title` -> "Welcome to Universal Triggers"
- `Universal Triggers Welcome Body` -> "Fire VRChat OSC actions from Twitch chat commands, channel-point rewards, bits, subs, gift subs, and follows. Start fast by importing a Fooma Twitch Interaction config, or build your first trigger from scratch."
- `Universal Triggers Welcome Import Title` -> "Import Fooma Config"
- `Universal Triggers Welcome Import Body` -> "Pick a .json file from Fooma. Crystal Relay parses commands, rewards, bits, subs, follows and fuses pairs automatically."
- `Universal Triggers Welcome Import Action` -> "Choose file..."
- `Universal Triggers Welcome Create Title` -> "Create New Trigger"
- `Universal Triggers Welcome Create Body` -> "Start from a blank trigger. Pick an event type (chat, reward, bits, sub, follow), point it at OSC actions."
- `Universal Triggers Welcome Create Action` -> "Start blank"
- `Universal Triggers Subtitle Summary` -> "{0} saved | {1} active | {2} need a quick fix"
- `Universal Triggers Search Placeholder` -> "Search by name, command, or reward..."
- `Universal Triggers Filter All` -> "All"
- `Universal Triggers Filter Active` -> "Active"
- `Universal Triggers Filter Disabled` -> "Disabled"
- `Universal Triggers Filter Needs Fix` -> "Needs Fix"
- `Universal Triggers Filter From Fooma` -> "From Fooma"
- `Universal Triggers Enable All` -> "Enable All"
- `Universal Triggers Disable All` -> "Disable All"
- `Universal Triggers Delete All` -> "Delete All"
- `Universal Triggers Sort By Type` -> "By Type"
- `Universal Triggers Sort By Status` -> "By Status"
- `Universal Triggers Sort By Name` -> "By Name"
- `Universal Triggers Sort Recently Edited` -> "Recently Edited"
- `Universal Triggers Collapse All` -> "Collapse all"
- `Universal Triggers Expand All` -> "Expand all"
- `Universal Triggers Section Chat` -> "Chat Commands"
- `Universal Triggers Section Reward` -> "Channel Point Rewards"
- `Universal Triggers Section Bits` -> "Bits"
- `Universal Triggers Section Subs Combined` -> "Subs & Gift Subs"
- `Universal Triggers Section Follows` -> "Follows"
- `Universal Triggers Type Pill Sub` -> "SUB"
- `Universal Triggers Type Pill Gift Sub` -> "GIFT SUB"
- `Universal Triggers Type Pill Chat` -> "CHAT"
- `Universal Triggers Type Pill Reward` -> "REWARD"
- `Universal Triggers Type Pill Bits` -> "BITS"
- `Universal Triggers Type Pill Follow` -> "FOLLOW"
- `Universal Triggers Section Active Suffix` -> "({0} active)"
- `Universal Triggers Section Active Hidden Suffix` -> "({0} active, {1} hidden)"
- `Universal Triggers Section Off Suffix` -> "({0} active, {1} off)"
- `Universal Triggers Section Mixed Suffix` -> "({0} active, {1} need fix, {2} off)"
- `Universal Triggers Section Disable All Mini` -> "Disable section"
- `Universal Triggers Status Ready` -> "Ready"
- `Universal Triggers Status Avatar Missing` -> "Avatar missing"
- `Universal Triggers Status Hidden` -> "Hidden"
- `Universal Triggers Status Disabled` -> "Off"
- `Universal Triggers Status Listening` -> "Listening"
- `Universal Triggers Source Fooma` -> "Fooma"
- `Universal Triggers Source Managed` -> "Managed"
- `Universal Triggers Source Linked` -> "Linked"
- `Universal Triggers Card Test` -> "Test"
- `Universal Triggers Card Edit` -> "Edit"
- `Universal Triggers Description Chat` -> "Types {0} -> {1}"  (placeholder {0} is the command, rendered as a monospace `Run`)
- `Universal Triggers Description Reward Managed` -> "VRC: {0} · {1} pts · {2}s cooldown · {3}"
- `Universal Triggers Description Reward Linked` -> "Listens to your reward {0}. Crystal Relay never modifies the reward."
- `Universal Triggers Description Bits Range` -> "Cheer {0}-{1} bits -> {2}"
- `Universal Triggers Description Bits Open` -> "Cheer {0}+ bits -> {1}"
- `Universal Triggers Description Subs` -> "New sub at {0}+ -> {1}"
- `Universal Triggers Description Gift Subs` -> "Gift sub -> {0}"
- `Universal Triggers Description Follow` -> "New follower -> {0}"
- `Universal Triggers Action Summary Single` -> "{0}"  (verb form e.g. "avatar waves 1s")
- `Universal Triggers Action Summary Random` -> "{0} actions (random)"
- `Universal Triggers Action Summary All` -> "{0} actions (all)"
- `Universal Triggers Editor Trigger Settings` -> "Trigger settings"
- `Universal Triggers Editor Twitch Reward` -> "Twitch reward"
- `Universal Triggers Editor Avatar Readiness` -> "Avatar readiness"
- `Universal Triggers Editor OSC Actions` -> "OSC actions"
- `Universal Triggers Editor Add Action` -> "+ Add action"
- `Universal Triggers Editor Delete` -> "Delete"
- `Universal Triggers Editor Test Now` -> "Test now"
- `Universal Triggers Editor Save` -> "Save"
- `Universal Triggers Editor Close` -> "Close"
- `Universal Triggers Editor Avatar Param Found` -> "Found"
- `Universal Triggers Editor Avatar Param Missing` -> "Missing"
- `Universal Triggers Editor No Avatar Params` -> "This trigger has no avatar parameter actions, so it always runs."
- `Universal Triggers Delete Confirm Title` -> "Delete trigger?"
- `Universal Triggers Delete Confirm Body` -> "Delete this Universal Trigger? The action cannot be undone."
- `Universal Triggers Delete All Confirm Title` -> "Delete all Universal Triggers?"
- `Universal Triggers Delete All Confirm Body` -> "Delete every Universal Trigger? This removes {0} triggers and any Crystal Relay-owned channel-point rewards they manage. The action cannot be undone."
- `Universal Triggers Fooma Help Title` -> "About Fooma Twitch Interaction"
- `Universal Triggers Fooma Help Body` -> "Crystal Relay can import Fooma Twitch Interaction JSON configs to create Universal Triggers in one click. Open the Fooma project page to learn more or to grab the config tool?"

The existing key `Universal Triggers` (the section / window title) stays. The orphaned wizard keys (`Universal Triggers Wizard *`) and orphaned import preview keys are unused and should be removed from `en-US.extra.json` and all translated `*.extra.json` files.

## Testing Plan

### Per-step unit smoke

After each Step gate, run:
- `dotnet build "VrcTwitchOscBridge\VrcTwitchOscBridge.csproj" --no-restore` -> green required.
- Run the app via `Launch-Crystal-Relay-Debug.bat`.
- Visit at least one other Crystal Relay section to confirm no regression (rotate between Avatar Sets, Cash Payments, Avatar Scaling, Twitch Chatbox).

### End-to-end smoke (Step 8 in implementation order)

The 10 scenarios in Step 8 form the acceptance test. Use a real Twitch dev account + a VRChat account on a known avatar. The Power-up + Cash Payment scale paths (which reuse `UniversalIncomingEvent`) must still trigger their scale actions when their own rules fire.

### Coupling regression checklist

- Power-up scale rule with a bits threshold -> simulate or wait for a real Power-up -> avatar height changes per the scale rule -> confirm the Universal Trigger rebuild did not break the `ToUniversalPowerUpEvent` path.
- Cash Payment rule with a tip-amount threshold -> simulate a hosted Ko-fi payload -> avatar scale rule fires -> confirm `HandleCashPaymentEventAsync` building `UniversalIncomingEvent` still works.
- Reward Fire Sale start -> verify discounted price on Crystal Relay-managed Universal rewards -> Fire Sale end -> verify prices restore.
- Avatar change -> verify Universal rewards with avatar param gates hide/show on Twitch as the current avatar changes.

## Risks

1. **WPF binding errors after demolition.** The legacy XAML has 100+ Universal Trigger binding sites scattered through `MainWindow.xaml`. Missing one leaves dangling bindings that throw at runtime even though the build passes. Mitigation: search after each XAML edit for `Universal` / `IsViewingUniversalTriggers` / `SelectedUniversalTrigger`; run the app with debug binding traces enabled.
2. **Managed-reward sync rerun.** Removing the legacy UI must not trigger a full reward delete/recreate. Mitigation: the runtime sync gates on `RewardSyncMode` + `desiredEnabled`; preserving the IDs means linked rewards stay listen-only and managed rewards keep their existing Twitch IDs. We do not touch any sync code in this rebuild.
3. **Fooma asset rendering.** WPF needs `Resource` build action + `pack://` URI + `NearestNeighbor` scaling for crisp pixel art. Mitigation: explicit `Resource` entry in csproj, `RenderOptions.BitmapScalingMode="NearestNeighbor"` on every `Image` element.
4. **Window lifecycle leaks.** Open the manager twice -> two windows. Mitigation: `MainWindowViewModel` holds a single instance reference; second open brings the existing window to front instead of constructing a new one.
5. **Localization audit failure.** New keys not added to non-English files trip the audit. Mitigation: follow AGENTS.md translation quality rules; run the audit before declaring Step 7 done.
6. **AppSettings persisted collapse state.** Adding `IsChatSectionCollapsed` etc. to `AppSettings` requires `SettingsStore` serialization changes. Mitigation: add nullable properties (default expanded) so old saves load with no migration.
7. **Snapshot/restore for editor Cancel.** Naive snapshot misses nested `Actions` collection mutations. Mitigation: deep-copy via JSON round-trip on editor open; restore by replacing rule + actions on Cancel.
8. **Pass-throughs the user did not remove.** Audit `MainWindowViewModel` for any remaining `Universal*Command` callers in XAML before declaring Step 2 done.

## Future / Out of Scope

- The orphaned design's 4-step Create Wizard is not ported. If the empty-state landing + slide-out editor turns out to confuse first-time users, a wizard can be re-added later.
- Per-trigger action ordering UI (drag to reorder) is not in this spec - actions list shows in insertion order.
- Duplicate-trigger quick action is not in this spec. If demanded, add as a context-menu item on each card later.
- A trigger-type pixel-art icon set (like the Fooma cat for Fooma but also one for chat, reward, etc.) is not in this spec - emoji glyphs are used for the type icons.
- Bulk multi-select on cards is not in this spec.
- Per-section sort overrides are not in this spec; sort applies window-wide.

---

## Summary

Strip the broken legacy UI and the orphaned new-UI files, keep the runtime engine and persistence completely intact, build a fresh themed secondary window with the design the user signed off on (empty-state landing with Fooma cat + hammer wand cards, populated state with toolbar + filter chips + collapsible event-type sections + uniform 240x168 cards + slide-out editor panel from the right). All saved triggers and the runtime behavior survive unchanged.
