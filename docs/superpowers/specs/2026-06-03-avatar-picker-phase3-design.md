# Phase 3 Design: Roulette Multi-Select & Polish

## Overview

Phase 3 adds multi-select checkboxes, drag-and-drop reordering, keyboard navigation, and virtualization to the Avatar Picker. The Avatar Roulette pool picker is unified into the same `AvatarPickerWindow` via `AvatarPickerService.OpenMulti`.

## Architecture

### Multi-Select Mode
- When opened via `OpenMulti`, `AvatarPickerWindow` enters multi-select mode
- Each avatar card/list item gets a `CheckBox` bound to `SelectedMultiAvatarIds`
- Bottom bar shows "X avatars in pool" count
- OK button enabled when at least one avatar is selected
- Clicking a card toggles selection; clicking the card body (not checkbox) also toggles

### Drag-and-Drop Reordering
- In multi-select mode, selected avatars can be reordered via drag-and-drop in the list view
- Grid view does not support drag-and-drop (wrap panel doesn't have natural ordering)
- A separate "Pool" list appears in list view when multi-select mode is active, showing selected avatars in order
- Drag items within the pool list to reorder
- Reorder updates `SelectedMultiAvatarIds` order (use `List<string>` instead of `HashSet` for ordered pool)

### Keyboard Navigation
- Arrow keys navigate focus through avatar cards/items
- Enter/Space toggles selection on focused item
- Escape closes picker (Cancel behavior)
- Ctrl+A selects all avatars in multi-select mode
- Ctrl+D deselects all

### Virtualization
- Replace `ItemsControl` in grid view with `ListBox` with `VirtualizingPanel`
- Use `VirtualizingStackPanel` for list view (already a `ListBox`)
- For grid view, use `WrapPanel` inside a `VirtualizingPanel` via custom panel or `UniformGrid` with virtualization
- Simplest approach: use `ListBox` with `WrapPanel` as `ItemsPanel` and enable `VirtualizingPanel.IsVirtualizing="True"`

### Roulette Pool Integration
- `MainWindowViewModel.OpenAvatarRouletPoolPicker` switches from `AvatarRouletPickerWindow` to `AvatarPickerService.OpenMulti`
- `AvatarRouletPickerWindow` files remain but are no longer used (can be cleaned up later)

## File Changes

### Modified Files
| File | Change |
|------|--------|
| `AvatarPickerWindow.xaml` | Add checkboxes to card/list templates, add pool reorder list, enable virtualization |
| `AvatarPickerWindow.xaml.cs` | Add keyboard navigation handlers, drag-and-drop handlers |
| `ViewModels/AvatarPickerViewModel.cs` | Change `SelectedMultiAvatarIds` from `HashSet` to `List` for ordering, add SelectAll/DeselectAll, add pool reorder methods |
| `ViewModels/MainWindowViewModel.cs` | Switch roulette pool picker to `AvatarPickerService.OpenMulti` |
| `VrcTwitchOscBridge.csproj` | No changes needed (no new files) |
| `Resources/Localization/en-US.extra.json` | Add new UI text keys |
| All `*.extra.json` files | Translate new keys |

### New Files
None — all changes are modifications to existing files.

## Implementation Order

1. ViewModel changes: ordered pool, SelectAll/DeselectAll, pool reorder
2. XAML changes: checkboxes, pool list, virtualization
3. Code-behind: keyboard navigation, drag-and-drop
4. Roulette pool integration in MainWindowViewModel
5. Localization
6. Build verification
