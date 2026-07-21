# Avatar Picker Redesign — Design Spec

## Overview

Redesign the Pick Avatar window (`AvatarPickerWindow`) from a flat card grid with dropdown filters to a sidebar-browse layout inspired by VRChat's own avatar browsing experience. The new picker separates avatars by source (Favorites, Uploaded, Purchased, Local OSC), adds VRChat API-based filtering (style tags, content warnings, platform), and preserves favorite-group browsing.

## Architecture

The redesign touches five layers:

1. **API client** — expand `VrChatApiClient` and `VrChatApiRoutes` to fetch favorite groups and manage toggling
2. **Model** — add fields to `VrChatAvatarSummary`/`VrChatAvatarRecord`, add favorite-group mapping
3. **Service** — update `AvatarPickerService` to pass grouped data; add favorite-group resolution
4. **View-Model** — rewrite `AvatarPickerViewModel` for sidebar navigation and combined filtering
5. **View** — new `AvatarPickerWindow.xaml` with sidebar + collapsible filter bar + larger cards

## API Changes (Confirmed via vrchat.community)

### Endpoints to add

| Endpoint | Method | Purpose |
|----------|--------|---------|
| `GET /favorite/groups?type=avatar` | Read | List favorite groups (avatars1–avatars4) with display names |
| `GET /favorites?type=avatar` | Read | List favorite entries with their group tags |
| `POST /favorites` | Write | Add avatar to a favorite group |
| `DELETE /favorites/{favoriteId}` | Write | Remove avatar from favorites |

### Fields to add to avatar deserialization

The existing `VrChatAvatarRecord` in `VrChatApiClient.cs` only captures `id`, `name`, `imageUrl`, `thumbnailImageUrl`. Add:

| JSON field | C# field | Purpose |
|------------|----------|---------|
| `authorName` | `string?` | "by Seluvia" on cards |
| `authorId` | `string?` | (future use) |
| `tags` | `List<string>` | `avatar_furry`, `content_adult`, etc. |
| `unityPackages` | `List<UnityPackageRecord>` | Platform detection via `[].platform` ("standalonewindows", "android") |

### Favorite-group mapping

Fetch `GET /favorites?type=avatar` which returns entries like:
```json
{"id":"fvrt_...","favoriteId":"avtr_...","tags":["avatars1"],"type":"avatar"}
```

Fetch `GET /favorite/groups?type=avatar` for display names:
```json
{"id":"fvgrp_...","displayName":"Avatars 1","name":"avatars1","type":"avatar"}
```

Build a map: avatar ID → list of favorite group names. This is stored in the ViewModel only (not persisted; re-fetched on each picker open).

## Sidebar Layout

```
┌──────────────────────────────────────────────────┐
│ [icon] Pick Avatar         Crystal Relay    ↻ ⚙ ✕ │
├────────┬─────────────────────────────────────────┤
│ BROWSE │  Search: [___________________________]  │
│ 📁 All │  View: [☰] [☷]                          │
│ 🕐 Rec │  ⬆️ Uploaded  · 12 avatars              │
│ ────── │  ┌──────────────────────────────────┐   │
│ ❤️ Fav▶│  │ Furry v PC v                      │   │
│ ⬆️ Upl │  │ ▼ Filters (2 active)              │   │
│ 🛍️ Pur │  │ Style: [All][Furry][Humans]...    │   │
│ 💻 OSC │  │ Plat:  [All][PC ][Quest][Both]    │   │
│ ────── │  │ Cont:  [All][Adult][Gore]...       │   │
│ █ Group│  ├──────────────────────────────────┤   │
│ █ Group│  │ [img]     [img]     [img]        │   │
│ 📂 Ung │  │ Kitsune   Moon B    Neon B       │   │
│ ────── │  │ by you    by you    by you        │   │
│ ✏️ Man │  │ PC·Furry  Both·Kemo PC·Cute      │   │
└────────┴─────────────────────────────────────────┘
```

### Sidebar sections

**BROWSE** (always visible):
- 📁 All Avatars — shows everything from all sources combined
- 🕐 Recent — last 10 picked avatars (tracked in AvatarLibrary)

**SOURCES** (VRChat API-driven):
- ❤️ Favorites — expandable, shows sub-groups avatars1–avatars4 + "All Favorites"
- ⬆️ Uploaded — avatars from `avatars?user=me&releaseStatus=all`
- 🛍️ Purchased — avatars from `avatars/licensed` (renamed from "Licensed")
- 💻 Local OSC — avatars from LocalLow JSON files

**MY GROUPS** (from AvatarLibrary):
- User-created groups with colored dots
- 📂 Ungrouped
- ✏️ Manage Groups... opens AvatarLibraryManagerWindow

### Source label resolution

Currently `VrChatApiClient.GetSelectableAvatarsAsync` merges all three sources (uploaded/favorites/licensed) into one deduplicated list with combined `SourceLabel` strings. We replace this with:

- Preserve distinct source flags per avatar (`IsUploaded`, `IsFavorited`, `IsLicensed`)
- `SourceLabel` becomes a calculated display string for the list view
- Favorites sidebar section shows avatars where `IsFavorited == true`
- Favorites sub-groups resolved from the favorite-group map

## Card Design (170px)

```
┌────────────────────┐
│ ┌──────────────────┐│
│ │      image       ││  ❤️/🤍 (if Favorites section)
│ │   120px x 150px  ││
│ └──────────────────┘│
│ Kitsune Dream [cur] │  ← name + "current" badge
│ by you              │  ← author
│ PC · Furry          │  ← platform + style tag (outlined)
└────────────────────┘
```

- **Size**: 170px wide (up from 140px), accommodates the new content
- **Platform badge**: solid pill (PC=purple, Quest=orange, Both=green)
- **Style tag**: outlined pill with matching border color (from `avatar_*`, prefix stripped)
- **No content warning on card** — kept only in the filter bar
- **Favorite heart**: only visible in Favorites / All / Recent sections (not in Uploaded or Purchased where favoriting doesn't apply)
- **"current" badge**: green pill when avatar matches the currently worn avatar
- **Click anywhere on card**: selects it (removes separate "Select" button from current design)

## Collapsible Filter Bar

Appears below the section title when a section with avatars is active.

**Collapsed state**: one line showing "▼ Filters (N active)" with removable active chips

**Expanded state**: three compact rows:

| Row | Label | Options | Source |
|-----|-------|---------|--------|
| Style | All + chips | Furry, Humans, Kemono, Cute, Robots, Beans, Anthro... | `avatar_*` tags, prefix stripped |
| Platform | All + chips | PC, Quest, Both | `unityPackages[].platform` |
| Content | All + chips | 🔞 Adult, 💀 Gore, 👻 Horror, 🔥 Suggestive, ⚔️ Violence | `content_*` tags, friendly names |

- Multi-select within each row
- Each selection creates an active chip at the top of the filter bar
- Clicking ✕ on a chip removes that filter
- Search box scopes to the current section + active filters

## Window Dimensions

- **Default size**: 1000 x 750 (up from 900 x 700)
- **Min size**: 800 x 550 (up from 700 x 500)
- **Sidebar**: 210px fixed width
- **Grid wraps**: ~4 cards per row at default width

## Files to Modify

| File | Changes |
|------|---------|
| `Services/VrChatApiRoutes.cs` | Add `FavoriteGroups`, `AddFavorite`, `RemoveFavorite` route helpers |
| `Services/VrChatApiClient.cs` | Expand `VrChatAvatarRecord`; add `GetFavoriteGroupsAsync`, `AddFavoriteAsync`, `RemoveFavoriteAsync`; update `GetSelectableAvatarsAsync` to preserve source flags |
| `Models/VrChatAvatarSummary.cs` | Add `IsUploaded`, `IsFavorited`, `IsLicensed`, `AuthorName`, `VrChatTags`, `Platform` fields; rename `SourceLabel` → computed |
| `Services/AvatarPickerService.cs` | Update to pass source flags and favorite-group data |
| `ViewModels/AvatarPickerViewModel.cs` | Rewrite for sidebar navigation, grouped source sections, filter state |
| `AvatarPickerWindow.xaml` | New sidebar layout, collapsible filter bar, 170px cards |
| `AvatarPickerWindow.xaml.cs` | Rewrite drag-drop, selection, keyboard nav for new layout |
| `Models/AvatarLibrary.cs` | Add `RecentAvatarIds` list (last 10) for Recent section |
| `AGENTS.md` | Updated API source (already done) |

## Files to Remove/Replace

- `AvatarRouletPickerWindow.xaml` and code-behind — the new picker handles multi-select natively, making the separate roulette window redundant. Replace with `AvatarPickerService.OpenMulti()` using the new window.
- `Models/AvatarPickerViewMode.cs` — keep, it's fine

## AvatarPickerItem Record (updated)

```csharp
public sealed record AvatarPickerItem(
    string Id,
    string Name,
    string AuthorName,
    ImageSource? Image,
    string? ThumbnailUrl,
    bool IsSelected,
    bool IsCurrentAvatar,
    bool IsFavorited,          // new: from VRChat favorites
    string? FavoriteGroupName, // new: "Avatars 1" etc.
    string Platform,            // new: "PC", "Quest", "Both"
    IReadOnlyList<string> StyleTags,     // new: ["Furry", "Cute"] from avatar_* tags
    IReadOnlyList<string> ContentTags,   // new: ["Adult"] from content_* tags
    IReadOnlyList<AvatarTagDisplay>? UserTags, // existing AvatarLibrary tags
    string SourceSection        // new: "Favorites", "Uploaded", "Purchased", "LocalOsc"
);
```

## Backward Compatibility

- `AvatarPickerService.OpenSingle()` and `OpenMulti()` signatures stay the same
- All existing callers (Avatar Sets, Avatar Swap, Power Ups, Supporters, MainWindow) continue to work without changes
- The separate `AvatarRouletPickerWindow` is replaced by `AvatarPickerService.OpenMulti()` using the new picker in multi-select mode
- `AvatarSetsManagerWindow` and `AvatarSwapManagerWindow` use the picker through the existing service layer — no UI changes needed in those windows
