# Phase 2 Design: Avatar Library Manager — Groups, Tags & Custom Icons

## Overview

Phase 2 adds user-facing organization tools to the Avatar Picker: named groups, colored tags, and per-avatar custom icon images. Users manage these through a compact `AvatarLibraryManagerWindow` opened from the picker, and filter avatars in the picker by group and tag.

## Architecture

### Shared Data
- `Settings.AvatarLibrary` is the single source of truth
- Both `AvatarPickerWindow` and `AvatarLibraryManagerWindow` bind to the same instance
- Changes in the manager propagate live to the picker via `ObservableCollection` bindings

### New Window: `AvatarLibraryManagerWindow`
- Compact themed window (~500x400), modeless (picker stays open underneath)
- Two tabs: **Groups** and **Tags**
- Constructed with `(AppTheme theme, AvatarLibrary library)`
- Registered with `ThemeManager` for live theme changes

### Groups Tab
- `ListBox` showing all groups from `AvatarLibrary.Groups`
- Each item shows: name (editable TextBox), sort order (numeric up/down), collapsed checkbox
- **Add Group** button: creates new `AvatarGroup` with auto-generated GUID and default name
- **Delete Group** button: removes selected group and clears its ID from all `AvatarLibraryEntry.GroupIds`
- No reordering UI — sort order is controlled by the numeric field

### Tags Tab
- `ListBox` showing all tags from `AvatarLibrary.Tags`
- Each item shows: name (editable TextBox), hex color input + preview swatch
- **Add Tag** button: creates new `AvatarTag` with auto-generated GUID, default name, default color `#A855F7`
- **Delete Tag** button: removes selected tag and clears its ID from all `AvatarLibraryEntry.TagIds`
- Color picker: hex `TextBox` (validates `#RRGGBB` format) + small `Border` preview swatch

### Custom Icon Picker
- Custom icons are set from within the `AvatarPickerWindow` itself via right-click context menu on an avatar card
- Context menu items: **"Set Custom Icon..."** and **"Clear Custom Icon"**
- "Set Custom Icon..." opens `OpenFileDialog` filtered to `*.png;*.jpg;*.jpeg;*.gif;*.bmp;*.webp`
- Selected file is copied to `AppDataPaths.ThemeAssetsFolder` with a unique name (`avatar_<id>_custom.<ext>`)
- Path stored in `AvatarLibraryEntry.CustomIconPath`
- `AvatarImageService` already reads `CustomIconPath` as its first fallback tier
- "Clear Custom Icon..." sets `CustomIconPath` to empty string, falling back to VRChat API image

### Picker Changes
- **Filter bar**: Two `ComboBox` dropdowns added between search bar and avatar list
  - Group filter: "All Groups" + each group name
  - Tag filter: "All Tags" + each tag name
- Filters are AND'd: avatar must match both if both are set
- **Extended search**: `AvatarPickerViewModel` search matches avatar name AND group names AND tag names (via `AvatarLibrary` lookup)
- Filter dropdowns populate from `AvatarLibrary.Groups` and `AvatarLibrary.Tags`

### Title Bar Change
- `AvatarPickerWindow` title bar gets a **"Manage" button** to the left of the close button
- Opens `AvatarLibraryManagerWindow` modeless with `Owner = this`
- Prevents multiple manager instances (single-instance check)

## File Changes

### New Files
| File | Purpose |
|------|---------|
| `AvatarLibraryManagerWindow.xaml` | Manager window UI with Groups/Tags tabs |
| `AvatarLibraryManagerWindow.xaml.cs` | Code-behind with ThemeManager integration |
| `ViewModels/AvatarLibraryManagerViewModel.cs` | Manager UI logic (add/edit/delete groups and tags) |

### Modified Files
| File | Change |
|------|--------|
| `AvatarPickerWindow.xaml` | Add Manage button to title bar, add filter ComboBoxes |
| `AvatarPickerWindow.xaml.cs` | Wire Manage button click, add context menu handlers, pass library to filters |
| `ViewModels/AvatarPickerViewModel.cs` | Add group/tag filter properties, extend search |
| `Services/AvatarImageService.cs` | Add method to copy custom icon to ThemeAssetsFolder |
| `VrcTwitchOscBridge.csproj` | Add new files |
| `Resources/Localization/en-US.extra.json` | Add new UI text keys |
| All `*.extra.json` files | Translate new keys |

## Implementation Order

1. `AvatarLibraryManagerViewModel` — add/edit/delete groups and tags, custom icon path
2. `AvatarLibraryManagerWindow` — XAML + code-behind, tab layout
3. `AvatarPickerWindow` changes — Manage button, filter ComboBoxes, extended search
4. `AvatarImageService` — custom icon copy helper
5. Localization keys + translations
6. Build verification + csproj updates

## Constraints

- No external NuGet dependencies (color picker is hex input + swatch)
- Modeless manager — picker stays interactive
- Custom icon files copied to ThemeAssetsFolder, not stored inline
- All UI text localized
- ThemeManager integration for both windows
- csproj entries required for all new files
