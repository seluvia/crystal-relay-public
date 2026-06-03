# Avatar Picker Redesign — Design Spec

**Date:** 2026-06-03
**Status:** Approved
**Author:** Crystal Dev

## Problem Statement

The current avatar selection system uses editable ComboBox dropdowns across multiple areas of Crystal Relay (Avatar Set Setup, Avatar Change rules, Power-up rules, Supporter rules, Avatar Roulette). These dropdowns are text-only, hard to browse, and provide no visual feedback about which avatar is which. Users with large avatar libraries struggle to find the right avatar quickly.

## Requirements

1. Replace all avatar selection ComboBoxes with a button that opens a dedicated picker window
2. Display avatar images (VRChat API thumbnails → custom local icons → placeholder)
3. Grid view and list view, with user's last choice remembered
4. Real-time search filtering as you type
5. Group system (named folders, collapsible, user-defined order)
6. Tag system (named tags with colors, multi-assign per avatar)
7. Custom local icon support per avatar
8. Avatar Roulette integration (multi-select with checkboxes)
9. Independent floating window (not modal, not docked)
10. All data stored in existing AppSettings (portable with profile)

## Architecture

### Components

| Component | Type | Purpose |
|-----------|------|---------|
| AvatarPickerWindow | WPF Window | Standalone picker window with grid/list view, search, groups, tags |
| AvatarPickerViewModel | ViewModel | Drives the picker UI — avatar list, filtering, selection, groups/tags management |
| AvatarPickerService | Service | Opens the picker window and returns selected avatar(s) |
| AvatarImageService | Service | Resolves avatar images (VRChat API → custom icon → placeholder) |
| AvatarLibrary | Model | Stores groups, tags, and custom icon paths per avatar |
| AvatarGroup | Model | Named group with list of avatar IDs, display order, collapsed state |
| AvatarTag | Model | Tag name, color, list of avatar IDs |

### Data Flow

1. User clicks \"Select Avatar\" button (replaces old ComboBox)
2. AvatarPickerService creates a new AvatarPickerWindow, passes the current avatar list and selection context
3. Window loads avatars with images from AvatarImageService
4. User searches, browses groups, selects an avatar
5. Window closes, returns AvatarPickerResult
6. Caller updates the bound AvatarId property

### Key Design Decisions

- Window is independent (Owner = null) so it floats freely
- View model per instance — each picker window gets its own ViewModel, no shared state issues
- Image caching — AvatarImageService caches VRChat thumbnails locally to avoid re-downloading
- Settings storage — groups, tags, and custom icon paths stored in AppSettings.AvatarLibrary
- Phased implementation — Phase 1 builds single-select mode; Phase 3 adds multi-select for roulette

## UI Layout

### Window Structure

\\\
+-------------------------------------------------------------+
|  Crystal Relay — Avatar Picker                         [-][x]|
+-------------------------------------------------------------+
|  [Search avatars...]                    [Grid] [List]       |
+-------------------------------------------------------------+
|  Groups: [All v]  Tags: [None v]          [Manage Groups+Tags]|
+-------------------------------------------------------------+
|                                                             |
|  [Card]  [Card]  [Card]  [Card]  [Card]  ... (grid view)   |
|  [Card]  [Card]  [Card]  [Card]  [Card]  ...               |
|                                                             |
+-------------------------------------------------------------+
|  Selected: \"My Cool Avatar\"                    [Cancel] [OK]|
+-------------------------------------------------------------+
\\\

### Grid View
- Cards with avatar image (120x120px), name below, \"Select\" button
- 4-5 cards per row depending on window width
- Wrapping flow layout
- Hover highlights card with theme border color
- Selected card gets a thick themed border + checkmark overlay

### List View
- Horizontal rows: small thumbnail (40x40px) | Avatar Name | Group/Tags | Select button
- Scrollable vertical list
- More compact, shows more avatars at once

### Search Bar
- Real-time filtering as you type
- Searches avatar name only (Phase 1), groups/tags in Phase 2
- Clear button (x) appears when text is entered
- Shows result count: \"Showing 12 of 84 avatars\"

### Group & Tag Filters
- Groups dropdown: Shows \"All\" + each group name. Selecting a group filters to only avatars in that group
- Tags dropdown: Shows \"None\" + each tag. Selecting a tag filters to avatars with that tag
- \"Manage Groups+Tags\" button: Opens a small sub-window for creating/editing groups and tags
- Both filters can be active simultaneously (AND logic)

### Bottom Bar
- Shows currently selected avatar name
- OK button: confirms selection and closes window
- Cancel button: closes without selection
- OK is disabled until an avatar is selected

### Roulette Mode (Phase 3)
- Same window, but \"Select\" buttons become checkboxes
- Bottom bar shows \"X avatars in pool\" instead of single selection
- OK button returns list of selected avatar IDs

## Data Models

### AvatarLibrary (stored in AppSettings)

\\\csharp
public class AvatarLibrary : ObservableObject
{
    public ObservableCollection<AvatarLibraryEntry> Entries { get; }
    public ObservableCollection<AvatarGroup> Groups { get; }
    public ObservableCollection<AvatarTag> Tags { get; }
    public AvatarPickerViewMode LastViewMode { get; set; }
}

public class AvatarLibraryEntry
{
    public string AvatarId { get; set; }
    public string CustomIconPath { get; set; }
    public List<string> GroupIds { get; set; }
    public List<string> TagIds { get; set; }
}

public class AvatarGroup
{
    public string Id { get; set; }
    public string Name { get; set; }
    public bool IsCollapsed { get; set; }
    public int SortOrder { get; set; }
}

public class AvatarTag
{
    public string Id { get; set; }
    public string Name { get; set; }
    public string ColorHex { get; set; }
}
\\\

### Storage Location

All stored inside AppSettings.AvatarLibrary. Custom icon images stored in AppDataPaths.ThemeAssetsFolder\\AvatarIcons\\.

### Image Resolution Pipeline

1. Check if AvatarLibraryEntry has CustomIconPath AND file exists -> Load local image
2. Check if VRChat API thumbnail URL is available -> Download, cache, load
3. Show built-in placeholder

### Caching Strategy

- VRChat thumbnails cached in AppDataPaths.ThemeAssetsFolder\\AvatarIcons\\Cache\\
- Cache keyed by avatar ID
- Cache invalidated when user clicks \"Refresh Avatar List\"
- Custom icons never cached (loaded directly from file)

## Integration

### Current Dropdown Locations (all replaced)

| Location | Current Control | Bound Property |
|----------|----------------|----------------|
| Avatar Set Setup — Return Avatar picker | ComboBox -> ProfileAvatarOptions | AvatarTriggerProfile.AvatarId |
| Avatar Change Rule — Target avatar | ComboBox -> VrChatAvatarOptions | TriggerRule.AvatarChangeTargetId |
| Avatar Change Rule — Reset avatar | ComboBox -> VrChatResetAvatarOptions | TriggerRule.AvatarChangeResetId |
| Power-up Rule — Avatar scope | ComboBox -> VrChatAvatarOptions | TriggerRule.AvatarId |
| Supporter Rule — Avatar scope | ComboBox -> VrChatAvatarOptions | TriggerRule.SupporterAvatarId |
| Avatar Roulette — Pool selection | ComboBox -> VrChatAvatarOptions | TriggerRule.AvatarRoulettePool |

### Replacement Pattern

Each ComboBox replaced with a DockPanel containing a TextBlock (showing selected avatar name) and a \"Browse...\" button that opens the picker.

### Service API

\\\csharp
public static class AvatarPickerService
{
    public static Task<AvatarPickerResult?> OpenSingleAsync(
        IReadOnlyList<VrChatAvatarSummary> avatars,
        string? currentAvatarId = null,
        Window? owner = null);

    public static Task<IReadOnlyList<string>> OpenMultiAsync(
        IReadOnlyList<VrChatAvatarSummary> avatars,
        IReadOnlyList<string> currentPool,
        Window? owner = null);
}
\\\

### Backward Compatibility

- Existing saved avatar IDs continue to work
- VrChatAvatarOptions collection still populated for status text and \"Use Current Avatar\" button
- Settings serialization is additive (new AvatarLibrary section, old fields untouched)
- No breaking changes to existing model properties

## Phased Implementation

### Phase 1: Core Picker Window
- AvatarPickerWindow + AvatarPickerViewModel
- Grid/List view toggle with remembered preference
- Real-time search filtering
- VRChat API image loading + placeholder fallback
- Single-select mode with OK/Cancel
- AvatarPickerService with OpenSingleAsync
- Replace 2-3 key dropdowns as proof of concept

### Phase 2: Groups, Tags & Custom Icons
- AvatarLibrary model + settings integration
- Group creation/editing (sub-window)
- Tag creation/editing with color picker
- Custom icon file picker per avatar
- Group/tag filter dropdowns in picker
- Search extended to groups/tags
- Replace remaining dropdowns

### Phase 3: Roulette Multi-Select & Polish
- Multi-select mode with checkboxes
- \"X avatars in pool\" bottom bar
- Drag-and-drop reordering within pool
- Keyboard navigation (arrow keys, Enter to select)
- Performance optimization for large avatar libraries (virtualization)
- Full replacement of all remaining dropdowns
