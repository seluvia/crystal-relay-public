# Pick Avatar Window — Grouping & Tags Improvement Design

- **Date:** 2026-07-04
- **Topic:** Better grouping system and tags for the Avatar Picker Window
- **Status:** Approved (pending implementation)
- **Approach:** Approach 1 — minimal, reuse existing structures

## Context

The Avatar Picker (`AvatarPickerWindow`) lets users pick a single avatar or a multi-avatar roulette pool from their VRChat avatar list. The data model already supports groups and tags (`AvatarGroup`, `AvatarTag`, `AvatarLibraryEntry.GroupIds/TagIds`), and the picker has group/tag filter dropdowns — but two gaps make the system effectively non-functional:

1. **No assignment UI.** The `AvatarLibraryManagerWindow` can create/delete groups and tags but cannot assign avatars to them. `GroupIds`/`TagIds` stay empty in practice.
2. **Flat display only.** The picker shows a flat grid/list with no group indicator and no tag chips on cards, despite the model supporting both.

This design closes both gaps with the smallest surface area that fits the codebase's direct MVVM style.

## Decisions (from brainstorming)

| Decision | Choice |
|---|---|
| Group vs tag mental model | Groups = folders (one per avatar); Tags = colored labels (many per avatar) |
| Group membership | Strictly one group per avatar |
| Assignment interaction | Right-click context menu on cards + inline tag chips on cards |
| Group indicator on cards | No — groups are filter-only |
| Manager window role | Expanded with bulk avatar assignment |
| Stale assignments | Auto-prune library entries when avatars leave the VRChat list |
| Group colors | None — groups stay color-free |
| "Ungrouped" filter | Yes — pseudo-entry in the group filter dropdown |
| Collapsible group sections | No (picker stays flat) — `IsCollapsed` field is dead code, dropped |
| Implementation approach | Approach 1 — extend existing structures, no new services |

## Data Model Changes (`Models/AvatarLibrary.cs`)

### `AvatarLibraryEntry`
- Replace `List<string> GroupIds` → `string GroupId` (single, empty/null = ungrouped)
- `TagIds` stays `List<string>` (multi-assign, unchanged)

### `AvatarGroup`
- Remove `IsCollapsed` field (dead code — was for collapsible sections we're not building)
- Keep `Id`, `Name`, `SortOrder`

### `AvatarTag`
- Unchanged: `Id`, `Name`, `ColorHex`

### Migration of existing data
Existing saves may have `GroupIds` populated. Since the manager never exposed assignment UI, these lists are almost certainly empty in practice. The load path ignores the old `GroupIds` field (JSON deserialization drops it) and treats the avatar as ungrouped. No explicit migration code — old `GroupIds` data is silently dropped on load.

### Auto-prune
When the picker opens with a fresh avatar list, it prunes any `AvatarLibraryEntry` whose `AvatarId` is not in the current VRChat avatar list. Runs in `AvatarPickerViewModel`'s constructor (after the library is handed in, before building `AllAvatars`). Implemented as a `HashSet<string>` of current avatar IDs for O(1) lookup, so overall O(entries). Mutates `library.Entries` directly; persistence happens via the existing `AppSettings` save flow.

### "Ungrouped" representation
`IsNullOrWhiteSpace(GroupId)` means ungrouped. "Ungrouped" is a pseudo-filter, not a real group — no fake group ID pollutes the `Groups` collection.

## Picker UX (`AvatarPickerWindow.xaml` + `.xaml.cs`)

### Context menu on avatar cards
Extends the existing `ContextMenu` (which already has Set/Clear Custom Icon). New themed items:

```
Set Custom Icon...
Clear Custom Icon
─────────────────
Set Group ▸          [Cuties ✓] [Public] [— Remove from group]
Tags ▸               [✓ Mini] [✓ Fav] [+ New Tag...]
```

- **Set Group ▸** submenu: one `MenuItem` per existing group (radio-check on the current group), plus a final `— Remove from group` (clears `GroupId`) and `+ New Group...` (prompts for a name, creates the group, assigns it). Selecting a different group moves the avatar (single-group rule).
- **Tags ▸** submenu: one checkable `MenuItem` per existing tag (checkmark if the avatar has it), plus `+ New Tag...` (prompts for name + color, creates it, adds it). Toggling a tag adds/removes the `TagId` from the entry.
- Both submenus rebuild on each open (`ContextMenuOpening` event) so newly-created groups/tags appear without reopening the menu.

### Tag chips on cards
- Horizontal `ItemsControl` of small colored chips bound to a computed `Tags` collection on `AvatarPickerItem`.
- Each chip: colored background (from `AvatarTag.ColorHex`), white text, rounded, small font.
- Clicking a chip's inline `×` removes that tag from the avatar.
- Avatars with no tags show no chip row (no empty space).
- Added to **both** `AvatarCardTemplate` (Grid) and `AvatarListItemTemplate` (List) for consistency.

### No group indicator on cards
Confirmed — group membership is visible only through the filter dropdown.

### Filter dropdowns
- **Group filter**: `ItemsSource` is a computed `GroupFilterOptions` list: `[All]`, `[Ungrouped]`, then real groups sorted by `SortOrder` then `Name`. Selecting "Ungrouped" filters to `IsNullOrWhiteSpace(GroupId)`. `FilterOption.Id` semantics: `null` = All, `"ungrouped"` = Ungrouped, real-id = that group.
- **Tag filter**: same wrapper treatment so "All" shows for tags too. `null` = all tags, real-id = that tag.
- `SelectedGroupFilterOption`/`SelectedTagFilterOption` bound to the ComboBoxes; mapped internally to the existing `SelectedFilterGroupId`/`SelectedFilterTagId` strings.

### Multi-select picker mode (Avatar Roulette pool)
Context menu and chips apply to the card regardless of multi-select mode. Assigning a group/tag does not toggle pool membership. Chips' `×` remove button sits at the bottom of the card; the multi-select checkbox stays top-right — no overlap.

### No new windows, no new services
All assignment mutations go through the existing `AvatarLibrary` / `AvatarLibraryEntry` directly from the picker's context-menu handlers in `AvatarPickerWindow.xaml.cs`, mirroring how custom-icon assignment already works there.

## Manager Window Expansion (`AvatarLibraryManagerWindow.xaml` + `.xaml.cs` + VM)

### New "Avatars" tab
Added to the existing `TabControl` alongside Groups and Tags tabs.

### Layout
```
┌─────────────────────────────────────────────────┐
│ [search box]                          [Refresh] │
├──────────────┬──────────────────────────────────┤
│ Avatar list  │  Group: [Cuties      ▾]          │
│ (left)       │  Tags:  [✓ Mini] [Fav] [+ Add]   │
│  • Cutie     │                                 │
│  • Public    │  Custom Icon: [Set...] [Clear]   │
│  • Avatar 3  │                                 │
│  • ...       │  [Apply to selection]            │
│  (scroll)    │  [Clear group for selection]     │
│              │  [Clear all tags for selection]  │
├──────────────┴──────────────────────────────────┤
│ [Select All] [Select None]    (n selected)      │
└─────────────────────────────────────────────────┘
```

### Left pane — avatar list
- `ListBox` of all `AvatarLibraryEntry` items in the library, **plus** entries synthesized on-the-fly for any avatar in the VRChat list that has no library entry yet (so you can assign before opening the picker).
- Each row: avatar image (40×40, via `AvatarImageService`), avatar name, group name (muted), and tag chips (mini).
- Multi-select enabled for bulk operations.
- List is read from a new `IReadOnlyList<VrChatAvatarSummary>` passed into the manager window (constructor gains an `avatars` parameter).

### Right pane — assignment controls
- **Group**: `ComboBox` listing `[— No group —]` + all groups. Changing it sets `GroupId` on every selected entry.
- **Tags**: list of all tags as checkable chips. Toggling a tag adds/removes it on every selected entry. `+ Add` opens a small inline "new tag" prompt (name + color) — reuses the same creation logic as the Tags tab.
- **Custom Icon**: `Set...`/`Clear` buttons that apply to the single selected entry (disabled when more than one is selected, since icon assignment is inherently per-avatar).
- **Bulk buttons**: "Apply to selection" commits the right-pane group/tag state to all selected entries; "Clear group for selection" and "Clear all tags for selection" are shortcuts.

### Right-pane reflects selection live
- Single selection: group dropdown and tag checkboxes show that entry's current state.
- Multiple selection: group shows `[mixed]`, tags show indeterminate state until the user explicitly sets them and clicks Apply.

### Constructor change
`AvatarLibraryManagerWindow` constructor gains `IReadOnlyList<VrChatAvatarSummary> avatars`. When opened from the picker's gear icon (`AvatarPickerWindow.OnManageButtonClicked`), the picker forwards the original `avatars` it received.

### Manager VM changes (`AvatarLibraryManagerViewModel`)
- Constructor gains `IReadOnlyList<VrChatAvatarSummary> avatars` (it already takes `AvatarImageService`, unchanged).
- New `ObservableCollection<AvatarAssignmentRow>` where `AvatarAssignmentRow` wraps `{ AvatarLibraryEntry Entry, string DisplayName, ImageSource? Image, string GroupName, IReadOnlyList<AvatarTagDisplay> Tags }`.
- New commands: `ApplyGroupCommand`, `ApplyTagsCommand`, `ClearGroupCommand`, `ClearTagsCommand`, `SelectAllCommand`, `SelectNoneCommand`, `SetCustomIconForSelectionCommand`, `ClearCustomIconForSelectionCommand`.
- Existing `AddGroup`/`DeleteGroup`/`AddTag`/`DeleteTag` cascade across assignment rows. `DeleteGroup` clears `GroupId` on entries that had it (updated from the current `GroupIds` removal). `DeleteTag` removes its id from `TagIds` on entries — unchanged.

### Auto-prune vs manager view
The manager shows every entry in the library, including ones for avatars no longer in the VRChat list. Since the picker auto-prunes on open, the manager gets a pre-pruned library when opened from the picker. The Avatars tab may show a "stale" muted badge on entries whose avatar ID isn't in the current list; since we auto-prune on picker open, this is a minor nicety, not required.

## ViewModel Wiring (`AvatarPickerViewModel.cs`)

- Constructor: after receiving `avatarLibrary`, run `PruneMissingEntries(avatars)` against `library.Entries` (removes any entry whose `AvatarId` is not in the `avatars` list). Then build `AllAvatars` as today.
- `AvatarPickerItem` gains `IReadOnlyList<AvatarTagDisplay> Tags`. Computed in `CreatePickerItem` and recomputed whenever an item is rebuilt (selection, image load, tag change). A small `ResolveTags(entry)` helper maps `entry.TagIds` → `AvatarTagDisplay` list via `library.Tags`.
- New helper `RebuildItem(AvatarPickerItem)` creates a fresh item with updated `Image`/`IsSelected`/`Tags` and replaces it in both `AllAvatars` and `FilteredAvatars`. Consolidates the current scattered "replace in both collections" logic now used by selection, image load, and custom-icon refresh. The custom-icon refresh in `AvatarPickerWindow.xaml.cs` calls this instead of duplicating logic.
- `ApplyFilter` updates:
  - Group filter: resolve `selectedFilterGroupId` — `null` = all, `"ungrouped"` sentinel = entries with `IsNullOrWhiteSpace(GroupId)`, real id = entries whose `GroupId` equals it.
  - Tag filter: unchanged (already checks `entry.TagIds.Contains(selectedFilterTagId)`).
  - Search: unchanged (already searches group/tag names) but adjusted for `GroupId` (single) instead of `GroupIds` (list).
- Filter dropdown sources:
  - `GroupFilterOptions` (new computed `ObservableCollection<FilterOption>`): `[All]`, `[Ungrouped]`, then groups sorted by `SortOrder` then `Name`.
  - `TagFilterOptions`: similar wrapper so "All" shows for tags too.

## New Small Types

- **`AvatarTagDisplay`** record: `{ string Id, string Name, string ColorHex }` — lives in `Models/` or `ViewModels/`. Used by both picker item chips and manager assignment rows.
- **`FilterOption`** record: `{ string? Id, string Display }` — small, in `ViewModels/`. `Id` is `null`/`"ungrouped"`/real-id for groups; `null`/real-id for tags.
- **`AvatarAssignmentRow`** (manager VM): wraps an entry with display fields for the Avatars tab.

## ThemedInputDialog (new, small, only if needed)

The "New Group..." and "New Tag..." prompts need a themed input dialog. Crystal Relay doesn't appear to have a reusable themed input prompt. If no existing themed prompt is found during implementation, add a minimal `ThemedInputDialog` window (name + optional color picker) reused by both "New Group" and "New Tag". Keeps the prompt on-theme. This is the one small new window this feature needs.

## Localization

New UI text keys added to `en-US` source and translated into all non-English files per the localization rules. Keys include:

- "Set Group", "Remove from group", "New Group...", "Tags", "New Tag...", "New tag name:", "Tag color:", "New group name:"
- "Ungrouped", "All"
- "Apply to selection", "Clear group for selection", "Clear all tags for selection"
- "Select All", "Select None", "n selected", "mixed"
- "Avatars" (tab header)
- "Custom Icon:", "Set...", "Clear", "Search avatars..."
- "— No group —"

Run the localization audit after adding/changing UI text.

## Project File (`VrcTwitchOscBridge.csproj`)

Default item inclusion is disabled. New files must be explicitly added:
- `ThemedInputDialog.xaml` under `<Page>` (if added)
- `ThemedInputDialog.xaml.cs` under `<Compile>`
- Any new `.cs` files (e.g. for `AvatarTagDisplay`, `FilterOption`, `AvatarAssignmentRow`) under `<Compile>`

## Verification

Per AGENTS.md, verification = build + localization audit (no existing picker tests).

- Build: `dotnet build "E:\!!!Program to work on\Proper Crystal Relay\VrcTwitchOscBridge\VrcTwitchOscBridge.csproj" --no-restore`
- Localization audit: run the audit project per the existing workflow.
- If unit tests for `PruneMissingEntries` and the single-group filter are desired, the user should request them explicitly.

## Edge Cases & Error Handling

1. **Group deleted while an avatar has it as `GroupId`**: `DeleteGroup` cascades — clears `GroupId` on any entry referencing the deleted group's id. Picker filter dropdown reads from `library.Groups` live, so a deleted group disappears from the dropdown.

2. **Tag deleted while an avatar has it**: `DeleteTag` removes the id from every entry's `TagIds` — unchanged. Card chips re-resolve on next rebuild; if the picker is open and the tag was deleted from the manager (separate window), the picker's chips go stale until a rebuild. Acceptable since the picker is short-lived.

3. **New group/tag name collision**: names are not unique by design (only `Id` is). Creating a group named "Cuties" when one already exists makes a second one. Acceptable — matches existing manager behavior. No dedup enforcement.

4. **Empty group/tag name from the inline prompt**: the prompt dialog validates non-empty before creating; a blank name cancels.

5. **Auto-prune with large avatar lists**: O(entries) via `HashSet<string>` of current avatar IDs. Typical user has tens to low-hundreds of entries; fine.

6. **Avatar with an entry but no tags and no group**: renders normally, no chips, no group indicator, filter shows it under "Ungrouped" / "All". No special handling.

7. **Multi-select picker mode**: context menu and chips apply regardless of mode. Chips' `×` button (bottom of card) does not collide with the multi-select checkbox (top-right).

8. **List view vs grid view**: chips render in both. The list-item template gets a chip `ItemsControl` in the name/source-label row area, wrapping if needed. Group filter dropdown is the same control for both views.

**Error handling:**
- All library mutations are in-memory on the `AvatarLibrary` instance. Persistence is the existing `AppSettings` save path — failures surface through the existing settings-save error handling, not new code.
- Image loading in the manager's Avatars tab reuses `AvatarImageService`; failures fall back to the placeholder. No new error paths.
- Context-menu handlers guard against null `entry` (call `EnsureEntry` first, as the custom-icon handler already does).

## Out of Scope (explicitly not doing)

- Collapsible group sections in the picker (the model's `IsCollapsed` is being removed).
- Drag-and-drop group reassignment.
- Live sync between picker and manager windows.
- Group colors.
- Nested groups / sub-groups.
- Export/import of group/tag assignments.
- A dedicated `AvatarLibraryAssignmentService` (Approach 1 — direct mutation).
- Unit tests for prune/filter unless the user asks.

## Files Touched

| File | Change |
|---|---|
| `Models/AvatarLibrary.cs` | `GroupIds`→`GroupId`, drop `IsCollapsed` |
| `ViewModels/AvatarPickerViewModel.cs` | prune, filter dropdowns, tag resolution, `RebuildItem` helper |
| `AvatarPickerWindow.xaml` | tag chips in both card templates, filter dropdowns, context menu items |
| `AvatarPickerWindow.xaml.cs` | context-menu handlers, chip-remove handler |
| `AvatarLibraryManagerWindow.xaml` | new Avatars tab |
| `AvatarLibraryManagerWindow.xaml.cs` | constructor takes `avatars` |
| `ViewModels/AvatarLibraryManagerViewModel.cs` | assignment rows, bulk commands, cascade updates for single `GroupId` |
| `ThemedInputDialog.xaml` / `.cs` | new, small, only if no existing themed prompt found |
| `AvatarTagDisplay` record | new, small |
| `FilterOption` record | new, small |
| `AvatarAssignmentRow` | new, in manager VM |
| Localization files (all languages) | new keys |
| `VrcTwitchOscBridge.csproj` | explicit `<Page>`/`<Compile>` entries for new files |
