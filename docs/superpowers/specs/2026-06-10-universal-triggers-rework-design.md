# Universal Triggers UI Rework — Design Spec

**Date:** 2026-06-10
**Lane:** v3.1.10 beta
**Scope:** Universal Triggers tab in the main editor and the new guided create + import flows. No other sections affected. No data-model or runtime-behavior changes to the Universal Triggers engine.

---

## 1. Problem

The current Universal Triggers screen at `MainWindow.xaml:8680-9429` is dense and hard to use, especially for new users and for the Fooma-imported set:

- The screen is a wall of fields. The first thing a user sees is every field for every trigger type, even when nothing is selected.
- There is no clear "start here" affordance. New users have to figure out the layout from the existing structure.
- The "Universal Trigger Setup Warning" is a static block of text that lives inside the OSC Actions card, so it can be missed and isn't contextual to what's wrong.
- The trigger list groups by event type (Chat Commands, Channel Point Rewards, Bits, Subscriptions, Gift Subscriptions, Follows). This is correct but the grouping lives in seven `Expander`s with no filter or search.
- The per-trigger readiness signal is a single line of muted text under the Twitch reward block (`UniversalManagedRewardStatusText`). It is easy to miss.
- Direct OSC paths (`/input/*`, `/tracking/*`) work but the editor does not warn that they will not gate the Twitch reward on avatar params.
- Fooma import drops triggers into the same wall-of-fields layout with no preview, no count of what was created, and no clear "this is a Fooma import" indicator.
- The "Imported 19 triggers" dialog and the per-trigger delete confirmation are the only acknowledgement of imports; the rest of the import flow is just a file picker.

The user has confirmed the goal: make Universal Triggers modern, refined, easy to understand, and centered on the "param path is the contract with the avatar" idea, while preserving Fooma Config import and the warning system.

## 2. Goals

- Make the **entry** into Universal Triggers obvious. Two clear paths: "Import Fooma Config" (the fast lane) and "Create from scratch" (a guided wizard).
- Make the **list** scannable. Cards, not grouped `Expander`s. Each card shows the trigger type, name, action count, the avatar params it touches, and a clear ready/warning state for the current avatar.
- Make the **warning system** live and contextual. Cards show color-coded chips for `Ready`, `Direct OSC paths`, `Missing param`, `Needs setup`, etc. The current static warning text becomes a contextual chip on the card plus a tooltip on hover.
- Keep the **Twitch reward sync, Fusion, and runtime behavior** exactly as they are today. No engine changes.
- Keep the **data model** exactly as it is today. No new persisted properties, no migration.
- **Theme integration**: every new panel, card, and chip must use `DynamicResource` bindings to the real theme brushes so the whole screen recolors with the user's current theme.
- **Isolate** the change to Universal Triggers. Nothing outside the new View, the new ViewModel, the two new dialogs, and the localization files for Universal Triggers strings is touched.

## 3. Non-Goals

- No change to `UniversalTriggerRule`, `UniversalTriggerAction`, `UniversalTriggerType`, `UniversalTriggerValueKind`, or any other model.
- No change to `FoomaInteractionConfigImporter`, `UniversalTriggerFusionService`, or the runtime paths in `BridgeCoordinator.cs`.
- No change to Twitch reward sync, Fire Sale, Avatar Sets, Avatar Scaling, Movement, Power-Ups, Cash Payments, Bits + Subs overrides, or Chatbox.
- No change to the `PersistedProfileSettings` DTO, the persistence format, or the migrator chain.
- No new top-level tab or navigation. Universal Triggers is still one tab.
- No new converter, no new theme style, no new brush beyond the soft-warn triplet described in §10. We reuse the existing theme brushes for everything else.

## 4. Architecture

The new Universal Triggers surface is split into four new components, replacing the inline XAML in `MainWindow.xaml`. All new XAML lives at the project root (matching the existing pattern: `MainWindow.xaml`, `BugReportWindow.xaml`, `ThemedDialogWindow.xaml`, etc. all live next to `Models/`, `Services/`, `ViewModels/`, with no `Views/` folder).

| File | Role |
|---|---|
| `VrcTwitchOscBridge/UniversalTriggersView.xaml` (+ `.xaml.cs`) | The main library view: slim toolbar, filter strip, card grid, empty-state onboarding, and the slide-out editor (the editor is inlined as a sibling `Grid` cell inside the same UserControl, with `Visibility` bound to `IsEditorOpen` on the view-model). Embedded into `MainWindow.xaml` as a single `UserControl` where the current inline block lives. |
| `VrcTwitchOscBridge/ViewModels/UniversalTriggersViewModel.cs` | The new view-model. Holds `UniversalTriggers` (still the same `Settings.UniversalTriggers` collection — no model change), `SelectedTrigger`, the card-filter properties (`ShowAll`, `ShowReady`, `ShowWarnings`, `ShowFooma`, `SearchText`), `IsEditorOpen`, and the commands (`AddTriggerCommand`, `ImportFoomaCommand`, `OpenTriggerEditorCommand`, `DeleteAllCommand`, etc.). Exposed by `MainWindowViewModel` as a property `UniversalTriggersViewModel` so the existing bindings to the same data continue to work. |
| `VrcTwitchOscBridge/UniversalTriggerCreateWizardWindow.xaml` (+ `.xaml.cs`) + `ViewModels/UniversalTriggerCreateWizardViewModel.cs` | Modal window used for the guided 4-step create flow. Closes with `DialogResult = true` and the constructed `UniversalTriggerRule` on save. |
| `VrcTwitchOscBridge/UniversalTriggerImportPreviewWindow.xaml` (+ `.xaml.cs`) + `ViewModels/UniversalTriggerImportPreviewViewModel.cs` | Modal window used for the 3-step Fooma import preview. Returns the preview payload (file path + parsed result) on confirm. |

The existing `MainWindow.xaml.cs` keeps its existing click handlers and the `OnFoomaHelpButtonClicked` flow unchanged. The `OnHelpButtonClicked` for "Universal Trigger Editor" remains wired and uses the new help text from §9.

The `MainWindowViewModel` no longer carries the Universal Triggers commands. The new `UniversalTriggersViewModel` owns the full command surface for the section (`AddTriggerCommand`, `ImportFoomaCommand`, `OpenTriggerEditorCommand`, `DeleteAllCommand`, `EnableAllCommand`, `DisableAllCommand`, `TestTriggerCommand`, etc.). The old pass-through commands on `MainWindowViewModel` (e.g. `AddUniversalTriggerCommand`, `ImportFoomaInteractionConfigCommand`, `RemoveSelectedUniversalTriggerCommand`, `EnableAllUniversalTriggersCommand`, `DisableAllUniversalTriggersCommand`, `DeleteAllUniversalTriggersCommand`, `TestSelectedUniversalTriggerCommand`, `AddUniversalTriggerActionCommand`, `RemoveSelectedUniversalTriggerActionCommand`) are removed because their only consumer was the inline XAML that is being replaced. Removing them is safe because no other view, no test, and no other code path references them.

### Why a separate ViewModel

The Universal Triggers surface is the only place in `MainWindowViewModel.cs` that owns this much state, has this many commands, and has its own grouping logic. Moving it into its own file makes the new code reviewable on its own, keeps `MainWindowViewModel` from growing further, and is consistent with the other recent extractions (Avatar Picker, Chatbox, etc.).

## 5. Library View Layout

The post-setup library view (the default state once any trigger exists) has three vertical regions:

### 5.1 Slim toolbar (top)

Single row, `TitleBarBrush` background:
- **Left:** `Universal Triggers` (heading) + one-line subtitle ("Run direct OSC actions from Twitch events, across any avatar that has the params.")
- **Right:** three buttons in this order: `+ New trigger` (AccentBrush), `Import Fooma` (SecondaryButtonBrush), `Delete all` (SecondaryButtonBrush).

### 5.2 Filter strip

Single row, `NestedPanelBrush` background, below the toolbar:
- **Filter chips:** `All (12)`, `Ready (7)`, `Warnings (2)`, `From Fooma (2)`. Active chip uses `AccentDim` background + `Accent` border. Counts come from a computed property on the view-model.
- **Search box** (right-aligned, max width 280px): placeholder "Search triggers or params…", binds to `SearchText`. Filters by trigger name, command text, reward title, and any param path substring.

### 5.3 Card grid

A two-column grid of cards that wraps to one column when the parent is narrow. Implemented as an `ItemsControl` with a `WrapPanel` (orientation horizontal) as `ItemsPanel`, inside a `ScrollViewer`. The card `DataTemplate` is a fixed-width `Border` (390px min, 12px radius, `PanelBrush` background, 1px border in `InputBorderBrush`, 14px padding) and a fixed minimum height so cards in a row align.
- Each card is a `Border` (12px radius, `PanelBrush` background, 1px border in `InputBorderBrush`, 14px padding).
- Card border switches to `DangerBorderBrush` if the card has any red-status chip.
- Card opacity is `0.75` for the `Unconfigured` state, full opacity otherwise.

#### Card content (top to bottom)

1. **Type chip** (left) and **secondary chip** (right):
   - `CHAT COMMAND` → `AccentDim` + `Accent` border
   - `CHANNEL POINT` → `DangerBrush` + `DangerBorderBrush` border (because rewards touch Twitch)
   - `BITS` / `SUBSCRIPTION` / `GIFT SUBSCRIPTION` / `FOLLOW` → `AccentDim` + `Accent` border
   - `UNCONFIGURED` → `StatusChipBrush` + `BorderBrush`
   - Secondary chip on the right shows context: e.g. `100 pts` (cost), `100-999 bits`, `from Fooma`.
2. **Title** — 15pt bold, `TextBrush`.
3. **Action summary** — 11pt muted text. "2 actions, 2s total · /avatar/parameters/Ragdoll.Menu, /avatar/parameters/Ragdoll". Up to 2 param paths shown inline, then "…".
4. **Status chips** (row of wrap):
   - **Green (Ready):** `AccentDim` bg + `Accent` border, `RuleCardHoverBrush` text. Text: `✓ Ready for current avatar`.
   - **Yellow (Warn):** new soft-warn brushes, see §8. Text: `⚠ Direct OSC paths`, `⚠ Not avatar-bound`.
   - **Red (Danger):** `DangerBrush` bg + `DangerBorderBrush` border, `TextBrush` text. Text: `✗ {param} param missing`, `✗ No complete actions`.
   - **Muted (Info):** `StatusChipBrush` bg + `BorderBrush` border, `MutedBrush` text. Text: `Moderators+`, `+ !throw command`, `from Fooma`, `Needs setup`.

Clicking a card opens the editor slide-out for that trigger (§6).

## 6. Editor Slide-Out

The editor is a `520px` wide `UserControl` that hosts as a slide-out panel from the right edge of the library view, covering the right half of the card grid. The list behind it dims to `Opacity=0.4` while the editor is open.

### 6.1 Editor structure

1. **Title bar** (`TitleBarBrush`):
   - Trigger name (a `TextBlock` showing the name in 14pt bold; double-clicking the name (or pressing `F2`) swaps it for a `TextBox` bound to the same property, and `Enter` / focus loss commits the change). Type chip, Ready/Warning status chip, `✕` close button.
2. **Avatar Readiness panel** (FIRST section, always visible):
   - `StatusChipBrush` background, `InputBorderBrush` border, 12px radius, 12px padding.
   - Header: "Avatar Readiness".
   - Subheader: "For the current avatar (Example Avatar):" with the actual avatar name bound.
   - For each unique avatar param path used by the trigger's actions: a row with `✓` (green) or `✗` (red), the path in a `code` chip, and a one-line description: "- param found in current avatar" or "- missing from current avatar OSC JSON, OSC send will no-op".
   - Final summary line: "N actions target this param · reward shows on Twitch" or "reward hidden on Twitch until avatar has the param".
3. **Trigger Settings card** (`NestedPanelBrush` + `InputBorderBrush`):
   - Enabled checkbox.
   - 2-col: Display Name, Trigger Type.
   - 2-col: Global Delay, User Delay.
   - Random mode checkbox.
4. **Twitch Reward card** (only when `TriggerType == ChannelPointReward`):
   - 2-col: Reward Source (Create or manage / Link existing), Cost.
   - 2-col: Reward Name, Cooldown.
   - Description textbox (multi-line, optional).
   - Chat Command Fallback panel (collapsible): enable toggle, command text, permission.
   - Delete when inactive checkbox + 1-line explanation.
5. **OSC Actions card**:
   - Header: "OSC actions (N actions, random one per trigger)" with a `+ Add action` button.
   - Compact list of action rows: path (in a `code` chip), `Int = N`, `↩ 0`, `⏱ 1s`.
   - "Edit selected" button opens a small inline editor below the row.
6. **Footer bar** (`TitleBarBrush`):
   - `Test now` (secondary), `Duplicate` (secondary), `Delete` (secondary, right-aligned), `Save` (accent, right-most).

### 6.2 Visual cue: editor opens on the right of the list

The library view and the editor share the same `Grid` cell, but the editor is in a `Grid` that animates its `Margin` from `0,0,-520,0` to `0` over 200ms. The list sits in a sibling `Grid` cell that goes to `Opacity=0.4` while the editor is open.

## 7. Guided Create Wizard

The create wizard is a 4-step modal `Window` (`UniversalTriggerCreateWizardWindow`). It uses the same themed dialog chrome as the rest of the app (`ThemedDialogWindow` chrome) and includes a step indicator at the top.

### 7.1 Step indicator

A horizontal bar at the top of the window:
- 4 segments, each ~25% width and 4px tall.
- Completed segment: `AccentBrush`.
- Pending segment: `SecondaryButtonBorderBrush`.
- A label between segments: `Step N of 4` in `TitleBarSubTextBrush`.
- A `Cancel` button on the right closes the window with `DialogResult = false`.

### 7.2 Step 1 — Pick the Twitch event

Title: "What should trigger this?"
6-card grid (3 cols × 2 rows):

| Card | Icon | Subtitle |
|---|---|---|
| Chat Command | 💬 | "e.g. !wave" |
| Channel Point Reward | ⭐ | "Twitch redeem" |
| Bits | 💎 | "Cheering" |
| Subscription | 🎁 | "New sub" |
| Gift Subscription | 🎀 | "Gift sub event" |
| Follow | 👤 | "New follower" |

Selected card: 2px `Accent` border, `AccentDim` background. Unselected: 1px `InputBorderBrush` border, `PanelBrush` background. Hover: 1px `HighlightBorderBrush` border.

`Next` button enables when one card is selected.

### 7.3 Step 2 — Configure the event

The fields shown depend on the event picked in Step 1:
- **Chat Command:** Command text, Who can use it (Everyone / Subs / Mods / Streamer).
- **Channel Point Reward:** Reward Name, Cost, Description, Cooldown seconds, "Delete the Twitch reward when no avatar has the required param" checkbox.
- **Bits:** Min bits, Max bits.
- **Subscription:** Sub tier, Min months, Max months.
- **Gift Subscription:** Sub tier, Min months, Max months.
- **Follow:** Just a one-line "Fires on every new follower" confirmation.

### 7.4 Step 3 — Add OSC actions

- Header: "What should this trigger send? Pick params from the current avatar or type a path."
- Action rows in a `DataGrid`-style table (path / type / target / default / duration).
- `+ Add another action` button adds a new row.
- A `StatusChip`-colored hint banner at the bottom: `✨ Params found in current avatar: Ragdoll, Ragdoll.Menu, twitch · 2 of 3 used`. The hint reads the same avatar-param cache used by the runtime (`VrChatLocalOscCacheService`) and shows the current avatar's available params. If no avatar is loaded, the hint is muted and reads "Load a VRChat avatar to see available params".
- `Run a random one of these per trigger` checkbox.

### 7.5 Step 4 — Review and save

- Summary card with the trigger name, type chip, ready/warning chip, action count, and the list of param paths it targets.
- `« Back` (secondary), `Test now` (secondary, runs the actions immediately), `Save trigger` (accent, closes the window with `DialogResult = true` and the constructed `UniversalTriggerRule`).

## 8. Import Preview

The import preview is a 3-step modal `Window` (`UniversalTriggerImportPreviewWindow`).

### 8.1 Step 1 — Pick file

A single screen with a file picker button, a one-line description ("Pick a Fooma Twitch Interaction JSON file"), and a small "What is Fooma?" help link that opens the existing `OnFoomaHelpButtonClicked` Gumroad dialog.

### 8.2 Step 2 — Preview

A single screen showing what the import will create, before committing:

- A `StatusChipBrush` banner at the top: "FOOMA CONFIG DETECTED" with the file name, file size, and a summary line ("3 commands · 5 channel rewards · 4 sub rules · 6 bits rules · 1 follow rule · (8 will be fused into rewards)").
- A list of the triggers that will be created. Each row shows the trigger name, type chip, and action summary. The list is scrollable, capped at 5 visible rows with a "+ N more (Bits, Subscriptions, Follow) — click Expand to see all" link to expand.
- A `DangerBrush` warning panel IF the import contains any triggers whose actions are all direct OSC paths (i.e. none of the actions target `avatar/parameters/...`). The warning text: "The `!movement` command uses built-in /input/* paths. That won't gate reward visibility on avatar params. It'll still work, but the warning system will mark it as not avatar-bound."
- A small muted note at the bottom: "Crystal Relay will create the rewards on Twitch (if 'Create or manage' is on), tag them with the VRC: prefix, and link any matching commands to their reward. You can re-import the same file later to update."

The buttons: `« Back` (secondary), `Import N triggers` (accent).

### 8.3 Step 3 — Done

A one-screen confirmation with a summary: "Imported N triggers (M command+reward pairs fused)". The new triggers are highlighted in the library view behind a faded modal. Close button.

## 9. Empty-State Onboarding

When the library is empty (zero `UniversalTriggerRule` instances), the slim toolbar collapses and the entire body becomes the onboarding card. The onboarding card is a centered `Border` with `PanelHighlightBrush` background, `InputBorderBrush` border, 18px radius, max width 620px, centered with 36px vertical padding.

Content:
- Big emoji icon (✨) at the top.
- "Welcome to Universal Triggers" (20pt bold).
- A two-sentence description: "Universal Triggers run direct OSC actions from Twitch events. They listen to **avatar params** (not avatar sets), so a reward can fire on *any* avatar that has the param — private or public."
- Two large side-by-side CTA cards:
  - `📥 Import Fooma Config` (accent border, accent-dim background) — primary CTA, with the description "Have a Fooma Twitch Interaction JSON? Import it and you're done."
  - `🪄 Create from scratch` (input-border, panel background) — secondary CTA, with the description "A short guided setup. Pick a Twitch event, point to a param, done."
- An expandable "How does the avatar param thing work?" details/summary at the bottom with the explanation from `AGENTS.md` ("Crystal Relay reads each avatar's local OSC JSON when it loads. If your trigger targets /avatar/parameters/twitch and the current avatar declares twitch as a param, the reward shows on Twitch. Switch to an avatar without twitch and the reward hides (or is deleted if you turned that on). Direct OSC paths like /input/Jump always run regardless of avatar.").

## 10. Updated Warning System

The current `Universal Trigger Setup Warning` static text block is replaced with a four-level, contextual warning system. The mapping table is:

| State | Color | Border | Background | Where it shows |
|---|---|---|---|---|
| `Ready` (all required avatar params exist in current avatar) | `RuleCardHoverBrush` text | `Accent` | `AccentDim` | Card chip + editor header chip |
| `Warn` (trigger runs but reward is not avatar-bound) | new `--warn-text` (warm yellow) | new `--warn-border` | new `--warn-bg` | Card chip + tooltip on editor's direct-OSC actions |
| `Danger` (trigger can't fire or reward is hidden) | `TextBrush` | `DangerBorderBrush` | `DangerBrush` | Card chip + editor header chip + editor's Avatar Readiness panel |
| `Info` (neutral context) | `MutedBrush` | `BorderBrush` | `StatusChipBrush` | Card chip only |

The new warn brushes are added to `ThemeManager.cs` as soft-warning tokens for every existing palette (Void Crystal, Baked, Bubblegum, etc.) — same shape as the existing `DangerBrush` / `DangerBorderBrush` pair, just a warm yellow tone per palette. This is the only theme system change in the rework.

The static "Universal Trigger Setup Warning" text inside the OSC Actions card is removed. Its content is folded into the editor's Avatar Readiness panel (`✗ Missing param` rows) and the onboarding details panel.

The `ThemedDialogWindow.ShowYesNo` confirmation for "Delete All Universal Triggers" stays. It's the right pattern for a destructive bulk action. No change.

The `OnFoomaHelpButtonClicked` help dialog stays. Its "What is Fooma?" content is moved into the onboarding details panel as the new home for the explanation, with the Gumroad link still reachable.

## 11. Data Model Changes

**None.** The `UniversalTriggerRule` and `UniversalTriggerAction` models are untouched. The only property the editor view-model needs that does not already exist on the model is a per-card `AvatarReadiness` summary, which is computed in the view-model from the existing `IsUniversalTriggerReadyForCurrentAvatarJson` and `HasUniversalTriggerAvatarParameterGate` checks (currently used for the `UniversalManagedRewardStatusText` text).

## 12. File Changes

### 12.1 New files

| File | Purpose |
|---|---|
| `VrcTwitchOscBridge/UniversalTriggersView.xaml` | Library view, filter strip, card grid, empty-state onboarding, slide-out editor host |
| `VrcTwitchOscBridge/UniversalTriggersView.xaml.cs` | Code-behind (code-behind is empty or limited to button click plumbing; logic lives in the VM) |
| `VrcTwitchOscBridge/ViewModels/UniversalTriggersViewModel.cs` | New view-model: filter state, search, card readiness, command surface for the library view |
| `VrcTwitchOscBridge/UniversalTriggerCreateWizardWindow.xaml` | 4-step create wizard |
| `VrcTwitchOscBridge/UniversalTriggerCreateWizardWindow.xaml.cs` | Wizard code-behind |
| `VrcTwitchOscBridge/ViewModels/UniversalTriggerCreateWizardViewModel.cs` | Wizard view-model: step state, draft `UniversalTriggerRule`, validation |
| `VrcTwitchOscBridge/UniversalTriggerImportPreviewWindow.xaml` | 3-step import preview |
| `VrcTwitchOscBridge/UniversalTriggerImportPreviewWindow.xaml.cs` | Preview code-behind |
| `VrcTwitchOscBridge/ViewModels/UniversalTriggerImportPreviewViewModel.cs` | Preview view-model: parsed preview data, the "Import N triggers" command |

### 12.2 Edited files

| File | Changes |
|---|---|
| `VrcTwitchOscBridge/MainWindow.xaml` | Replace the inline Universal Triggers block (current `MainWindow.xaml:8680-9429`) with a single `<local:UniversalTriggersView DataContext="{Binding UniversalTriggersViewModel}" />` element. Remove the inline `DataTemplate` entries for `UniversalTriggerRule` (lines 1584 and 8966) and `UniversalTriggerAction` (lines 1615 and 9383) — they move to the new View. Keep the `Universal Triggers` nav button at line 3770 unchanged. |
| `VrcTwitchOscBridge/MainWindow.xaml.cs` | Keep `OnFoomaHelpButtonClicked` and the two `OnPickManagedRewardColorClicked` handlers. The pickers remain in the editor code-behind because they use the `ColorDialog` WinForms component. |
| `VrcTwitchOscBridge/ViewModels/MainWindowViewModel.cs` | Expose a new property `UniversalTriggersViewModel UniversalTriggersViewModel { get; }` initialized in the constructor. Existing pass-through commands stay for backwards compat but route to the new VM. |
| `VrcTwitchOscBridge/Services/ThemeManager.cs` | Add `WarnBrush`, `WarnBorderBrush`, and `WarnTextBrush` to every existing palette (16 themes), following the same pattern as the existing `DangerBrush` / `DangerBorderBrush` / `DangerTextBrush` triplet. Warm yellow tones per palette (Void Crystal: bg `#4a3a1a`, border `#a08a3a`, text `#f0d878`). |
| `VrcTwitchOscBridge/VrcTwitchOscBridge.csproj` | Add explicit `<Compile Include>` and `<Page Include>` entries for all the new `.cs` and `.xaml` files (the project has `EnableDefaultItems=false`). |
| `VrcTwitchOscBridge/Resources/Localization/en-US.extra.json` | Add the new keys listed in §13. |
| `VrcTwitchOscBridge/Resources/Localization/*.extra.json` (every other language) | Add matching placeholder translations for the new keys. |

### 12.3 Files NOT touched

- `Models/UniversalTriggerRule.cs`, `Models/UniversalTriggerAction.cs`, `Models/UniversalTriggerType.cs`, `Models/UniversalTriggerValueKind.cs`.
- `Services/UniversalTriggerFusionService.cs`, `Services/FoomaInteractionConfigImporter.cs`, `Services/SettingsStore.cs` (the Universal Triggers persistence block).
- `Services/BridgeCoordinator.cs` (the runtime universal trigger engine).
- `Services/BridgeRuntimeConfiguration.cs` (the immutable snapshot builder).
- Any other redeem library, the Twitch reward sync code, the chatbox, the about page, or any non-Universal-Triggers view or view-model.

## 13. Localization

The `en-US.extra.json` file gains the following keys. All other `*.extra.json` files must be updated to match. The same localization rules in `AGENTS.md` apply: informal register, placeholders preserved exactly, brand/technical terms (`Bits`, `Subs`, `OSC`, `OSCQuery`, `VRChat`, `Twitch`, `Crystal Relay`, `VRC:`, `Fooma`) stay in English.

### 13.1 New keys

- `Universal Triggers Subtitle` → `Run direct OSC actions from Twitch events, across any avatar that has the params.`
- `Universal Triggers New Trigger` → `+ New trigger`
- `Universal Triggers Import Fooma` → `Import Fooma`
- `Universal Triggers Delete All` → `Delete all`
- `Universal Triggers Filter All` → `All ({0})`
- `Universal Triggers Filter Ready` → `Ready ({0})`
- `Universal Triggers Filter Warnings` → `Warnings ({0})`
- `Universal Triggers Filter Fooma` → `From Fooma ({0})`
- `Universal Triggers Search Placeholder` → `Search triggers or params…`
- `Universal Triggers Card Action Summary` → `{0} action(s), {1}s total · {2}` (last slot is the joined path list, truncated)
- `Universal Triggers Ready` → `✓ Ready for current avatar`
- `Universal Triggers Warn Direct Osc` → `⚠ Direct OSC paths`
- `Universal Triggers Warn Not Avatar Bound` → `⚠ Not avatar-bound`
- `Universal Triggers Danger Missing Param` → `✗ {0} param missing`
- `Universal Triggers Danger No Actions` → `✗ No complete actions`
- `Universal Triggers Info Needs Setup` → `Needs setup`
- `Universal Triggers Info Moderators` → `Moderators+`
- `Universal Triggers Info Fused With Command` → `+ !{0} command`
- `Universal Triggers Info From Fooma` → `from Fooma`
- `Universal Triggers Editor Avatar Readiness` → `Avatar Readiness`
- `Universal Triggers Editor Readiness For Avatar` → `For the current avatar ({0}):`
- `Universal Triggers Editor Readiness Param Found` → `✓ param found in current avatar`
- `Universal Triggers Editor Readiness Param Missing` → `✗ missing from current avatar OSC JSON, OSC send will no-op`
- `Universal Triggers Editor Readiness Reward Shown` → `N actions target this param · reward shows on Twitch`
- `Universal Triggers Editor Readiness Reward Hidden` → `reward hidden on Twitch until avatar has the param`
- `Universal Triggers Editor Section Trigger Settings` → `Trigger settings`
- `Universal Triggers Editor Section Twitch Reward` → `Twitch reward`
- `Universal Triggers Editor Section Osc Actions` → `OSC actions`
- `Universal Triggers Editor Footer Test` → `Test now`
- `Universal Triggers Editor Footer Duplicate` → `Duplicate`
- `Universal Triggers Editor Footer Delete` → `Delete`
- `Universal Triggers Editor Footer Save` → `Save`
- `Universal Triggers Wizard Title` → `Create a new trigger`
- `Universal Triggers Wizard Step N of 4` → `Step {0} of 4`
- `Universal Triggers Wizard Cancel` → `Cancel`
- `Universal Triggers Wizard Back` → `« Back`
- `Universal Triggers Wizard Next` → `Next »`
- `Universal Triggers Wizard Step 1 Title` → `What should trigger this?`
- `Universal Triggers Wizard Step 1 Hint` → `Pick one of the six Twitch events to drive this trigger.`
- `Universal Triggers Wizard Event Chat Command` → `Chat Command`
- `Universal Triggers Wizard Event Chat Command Hint` → `e.g. !wave`
- `Universal Triggers Wizard Event Channel Point` → `Channel Point Reward`
- `Universal Triggers Wizard Event Channel Point Hint` → `Twitch redeem`
- `Universal Triggers Wizard Event Bits` → `Bits`
- `Universal Triggers Wizard Event Bits Hint` → `Cheering`
- `Universal Triggers Wizard Event Subscription` → `Subscription`
- `Universal Triggers Wizard Event Subscription Hint` → `New sub`
- `Universal Triggers Wizard Event Gift Sub` → `Gift Subscription`
- `Universal Triggers Wizard Event Gift Sub Hint` → `Gift sub event`
- `Universal Triggers Wizard Event Follow` → `Follow`
- `Universal Triggers Wizard Event Follow Hint` → `New follower`
- `Universal Triggers Wizard Step 2 Title` → `Configure the event`
- `Universal Triggers Wizard Step 2 Channel Point` → `Tell Crystal Relay about the reward.`
- `Universal Triggers Wizard Step 2 Chat Command` → `Tell Crystal Relay which chat command and who can use it.`
- `Universal Triggers Wizard Step 2 Bits` → `Set the bits range that fires this trigger.`
- `Universal Triggers Wizard Step 2 Subscription` → `Set the subscription tier and month range.`
- `Universal Triggers Wizard Step 2 Follow` → `Fires on every new follower.`
- `Universal Triggers Wizard Step 3 Title` → `Add OSC actions`
- `Universal Triggers Wizard Step 3 Hint` → `What should this trigger send? Pick params from the current avatar or type a path.`
- `Universal Triggers Wizard Step 3 Add Action` → `+ Add another action`
- `Universal Triggers Wizard Step 3 Random` → `Run a random one of these per trigger`
- `Universal Triggers Wizard Step 3 Params Available` → `✨ Params found in current avatar: {0} · {1} of {2} used`
- `Universal Triggers Wizard Step 3 Params No Avatar` → `Load a VRChat avatar to see available params`
- `Universal Triggers Wizard Step 4 Title` → `Review and save`
- `Universal Triggers Wizard Step 4 Hint` → `Looks good? Test it now or save it.`
- `Universal Triggers Wizard Step 4 Test` → `Test now`
- `Universal Triggers Wizard Step 4 Save` → `Save trigger`
- `Universal Triggers Import Title` → `Import Fooma Config`
- `Universal Triggers Import Step N of 3` → `Step {0} of 3 - {1}`
- `Universal Triggers Import Step Preview` → `Preview`
- `Universal Triggers Import Step Done` → `Done`
- `Universal Triggers Import File Summary` → `{0} commands · {1} channel rewards · {2} sub rules · {3} bits rules · {4} follow rule(s) · ({5} will be fused into rewards)`
- `Universal Triggers Import Will Create` → `This will create the following triggers:`
- `Universal Triggers Import More Truncated` → `+ {0} more ({1}) - click «Expand» to see all`
- `Universal Triggers Import Warn Direct Osc` → `The `!{0}` command uses built-in /input/* paths. That won't gate reward visibility on avatar params. It'll still work, but the warning system will mark it as not avatar-bound.`
- `Universal Triggers Import After Note` → `Crystal Relay will create the rewards on Twitch (if 'Create or manage' is on), tag them with the VRC: prefix, and link any matching commands to their reward. You can re-import the same file later to update.`
- `Universal Triggers Import Confirm` → `Import {0} triggers`
- `Universal Triggers Import Done Summary` → `Imported {0} triggers ({1} command+reward pairs fused)`
- `Universal Triggers Onboarding Title` → `Welcome to Universal Triggers`
- `Universal Triggers Onboarding Body` → `Universal Triggers run direct OSC actions from Twitch events. They listen to **avatar params** (not avatar sets), so a reward can fire on *any* avatar that has the param — private or public.`
- `Universal Triggers Onboarding Import Title` → `📥 Import Fooma Config`
- `Universal Triggers Onboarding Import Body` → `Have a Fooma Twitch Interaction JSON? Import it and you're done.`
- `Universal Triggers Onboarding Import Action` → `Choose .json file »`
- `Universal Triggers Onboarding Create Title` → `🪄 Create from scratch`
- `Universal Triggers Onboarding Create Body` → `A short guided setup. Pick a Twitch event, point to a param, done.`
- `Universal Triggers Onboarding Create Action` → `Start wizard »`
- `Universal Triggers Onboarding Help Question` → `How does the avatar param thing work?`
- `Universal Triggers Onboarding Help Body` → `Crystal Relay reads each avatar's local OSC JSON when it loads. If your trigger targets /avatar/parameters/twitch and the current avatar declares twitch as a param, the reward shows on Twitch. Switch to an avatar without twitch and the reward hides (or is deleted if you turned that on). Direct OSC paths like /input/Jump always run regardless of avatar.`

### 13.2 Removed keys

- `Universal Trigger Setup Warning` and its long body text — replaced by the contextual chips and the onboarding help body.
- `Import a Fooma config or add a universal trigger.` and `Import a Fooma config or add a universal trigger to edit it.` — replaced by the onboarding card.

### 13.3 Unchanged keys

- `Delete Universal Trigger`, `Delete All Universal Triggers`, `Universal Trigger Editor`, `Universal Triggers`, `Fooma Import Complete`, `Fooma Import Failed`, `Import Fooma Config`, `Import Fooma Interaction Config`, `Delete Twitch reward when inactive`, all the existing trigger field labels, all the existing action field labels, and the `OnHelpButtonClicked` for "Universal Trigger Editor" help text. The Editor help text is updated to a one-sentence version that matches the new "param path is the contract" framing (the longer explanation lives in the onboarding help body).

### 13.4 Retired key (replaced by the new key)

- `Add Universal Trigger` is retired. The new `Universal Triggers New Trigger` key replaces it because the new toolbar button is the entry point for the wizard, not for the old "add a blank trigger and edit it" flow. The new key is the only key the new View binds to. The retired key can stay in `en-US.extra.json` (no other binding uses it) for legacy export/import tooling; the localization audit can ignore retired keys.

## 14. Acceptance Criteria

- A new user with no triggers sees the onboarding card. They can pick Import Fooma or Create from scratch, and either flow lands them in the library view with at least one trigger.
- Importing a Fooma config (the `Config.json` from `C:\Users\screm\Downloads\`) opens the 3-step preview, lists all 19 expected triggers, fuses the 8 command+reward pairs, and creates the rewards on Twitch (if `Create or manage` is on). The library view shows the imported cards with `from Fooma` chips.
- The create wizard walks through 4 steps, builds a valid `UniversalTriggerRule` on save, and the new card appears in the library view.
- A card whose actions all use direct OSC paths (`/input/*`, `/tracking/*`) shows a yellow `⚠ Direct OSC paths` chip and a yellow `Not avatar-bound` chip. Its border is `InputBorderBrush` (not danger). The reward is visible on Twitch regardless of the current avatar.
- A card whose actions target `avatar/parameters/Ragdoll` shows a red `✗ Ragdoll param missing` chip when the current avatar's local OSC JSON does not declare `Ragdoll`. Its border switches to `DangerBorderBrush`. The reward is hidden on Twitch.
- A card whose actions target `avatar/parameters/twitch` on an avatar that has `twitch` shows a green `✓ Ready for current avatar` chip and a normal blue border. The reward is visible on Twitch.
- The filter chips correctly count and filter. Searching for `twitch` filters to cards whose action paths contain that string. The card count chips update live.
- Switching the app's theme (Void Crystal → Baked → Bubblegum, etc.) recolors every new panel, card, chip, and input immediately. No new colors are introduced that are not in the theme palette (except the soft-warn triplet added per palette in §10).
- The 4 existing universal-trigger warnings (no-help-text block, no-warning-dialog, runtime warning missing, status text missing) are replaced by the new chip system without losing the "Direct OSC paths won't gate reward" message — it is now on the card chip, in the tooltip, and in the onboarding help body.
- `dotnet build "E:\!!!Program to work on\Proper Crystal Relay\VrcTwitchOscBridge\VrcTwitchOscBridge.csproj" --no-restore` succeeds.
- `dotnet run --project "E:\!!!Program to work on\Proper Crystal Relay\LocalizationAudit"` (or the equivalent invocation) reports zero untranslated keys for the new strings across all `*.extra.json` files.
- The Fooma help Gumroad dialog still works. The chat command help (`!world`, etc.) still works. The Twitch reward sync, Fire Sale, and all other redeem libraries are unchanged in behavior.
- No secrets, tokens, runtime state, or user-local paths are added to the repo. The Config.json sample is not copied into the repo.

## 15. Out of Scope

- Any change to the data model (`UniversalTriggerRule`, `UniversalTriggerAction`, related enums).
- Any change to the Fooma importer, fusion service, settings persistence, or runtime execution path.
- Any change to the Twitch reward sync code (`SynchronizeManagedChannelPointRewardsAsync` and friends).
- Any change to the chatbox, Avatar Sets, Avatar Scaling, Movement, Power-Ups, Cash Payments, Bits + Subs overrides, or the about page.
- Any new top-level tab or nav change.
- Any new converter, theme style, or brush beyond the soft-warn triplet added to every existing palette.
- Any new persisted property or migration.
- Re-importing the user's actual `Config.json` file from `C:\Users\screm\Downloads\` into the repo (per `AGENTS.md` "Do not copy user-local files… into the repo unless the user explicitly asks"). The file is only used as a live test during development.
