# Avatar Picker Refresh Icons Button Design

**Date:** 2026-06-03

## Problem

After opening the Pick Avatar window, some avatars may fail to download their thumbnail (network error, VRChat CDN hiccup, etc.) and remain as placeholders. There is currently no way to force a re-download without restarting the app.

## Solution

Add a ↻ Refresh Icons button to the title bar of the Pick Avatar window. Clicking it clears both the on-disk thumbnail cache and the in-memory image cache, resets all avatars to their placeholder images, then re-downloads every thumbnail fresh from the VRChat API using the stored auth cookie.

## UI Placement

- Title bar, to the left of the existing gear (⚙) button
- Styled with the existing `TitleBarManageButtonStyle`
- Icon: ↻ (U+21BB, same font size as the gear)
- Tooltip: `"Refresh Icons"`
- Disabled while a refresh is in progress (prevents double-click)
- Re-enables when `LoadImagesAsync` completes or is cancelled

## Behavior

1. User clicks ↻
2. Button disables
3. `viewModel.RefreshAllImagesAsync()` is called (fire-and-forget, `_ = ...`)
4. Inside `RefreshAllImagesAsync`:
   a. Cancel any in-progress `LoadImagesAsync` via existing `CancelImageLoading()`
   b. Call `imageService.ClearDiskCache()` — deletes all `.jpg` files from `cacheFolder`
   c. Call `imageService.ClearCache()` — clears in-memory `imageCache` dictionary
   d. Reset every item in `AllAvatars` to placeholder; update `FilteredAvatars` in sync
   e. Notify button to re-enable via a bool property `IsImageLoading`
   f. Call `LoadImagesAsync()` — re-downloads all thumbnails from VRChat API with auth cookie
   g. When loading completes, set `IsImageLoading = false`
5. Button re-enables

## Components

### AvatarImageService
- Add `ClearDiskCache()` — deletes all files in `cacheFolder`, swallows IO errors
- No signature changes to existing methods

### AvatarPickerViewModel
- Add `IsImageLoading` bool property (raises `PropertyChanged`) — drives button enabled state
- Add `RefreshAllImagesAsync()` — orchestrates cache clear + item reset + re-load
- `LoadImagesAsync` sets `IsImageLoading = true` at start, `false` at end/cancel

### AvatarPickerWindow.xaml
- Add ↻ button in title bar, left of gear button
- `IsEnabled` bound to `!IsImageLoading` via `InverseBooleanConverter` (or handled in code-behind)
- `Click` → `OnRefreshIconsClicked`

### AvatarPickerWindow.xaml.cs
- Add `OnRefreshIconsClicked` handler: `_ = viewModel.RefreshAllImagesAsync()`
- Button enable/disable driven by `IsImageLoading` binding

## Error Handling

- `ClearDiskCache` swallows individual file delete errors — a locked file just stays and gets skipped
- If `LoadImagesAsync` is cancelled mid-run, `IsImageLoading` is set to false and button re-enables
- Failed individual downloads stay as placeholders (existing behavior)

## Scope

**In scope:** ClearDiskCache, RefreshAllImagesAsync, IsImageLoading, title bar button

**Out of scope:** Per-avatar refresh, progress bar, download count display, retry logic
