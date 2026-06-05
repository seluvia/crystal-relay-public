# Avatar Picker Performance and Layout Fix

**Date:** 2026-06-03  
**Author:** Crystal Dev

## Problem Statement

The Pick Avatar window has three issues:

1. **Long pause on open** - The window takes a long time to appear because the constructor loads all 449 avatar images synchronously on the UI thread. Each image triggers a blocking HTTP call (`GetAwaiter().GetResult()`) if the thumbnail isn't cached locally.

2. **Images not displaying** - The synchronous HTTP calls block and may timeout/fail silently. The `LoadVrChatThumbnail` method has a 10-second timeout per image, and failures fall back to placeholders without indication.

3. **List view stacking vertically** - The `ListViewControl` uses a `ListBox` with the default `VirtualizingStackPanel` (vertical orientation), which stacks one item per row instead of flowing items side-by-side.

## Architecture

### Current Flow

```
User clicks Browse
  -> MainWindowViewModel.OpenAvatarPicker()
    -> AvatarPickerService.OpenSingle()
      -> new AvatarPickerWindow(...)
        -> new AvatarPickerViewModel(...)
          -> For each of 449 avatars:
            -> CreatePickerItem()
              -> imageService.GetAvatarImage()
                -> LoadCustomIcon() OR LoadVrChatThumbnail() OR GetPlaceholderImage()
                -> LoadVrChatThumbnail does: HttpClient.GetByteArrayAsync().GetAwaiter().GetResult()
          -> ApplyFilter()
        -> InitializeComponent()
        -> ShowDialog()
```

The entire image loading happens in the constructor before `ShowDialog()` is called, blocking the UI thread.

### Proposed Flow

```
User clicks Browse
  -> MainWindowViewModel.OpenAvatarPicker()
    -> AvatarPickerService.OpenSingle()
      -> new AvatarPickerWindow(...)
        -> new AvatarPickerViewModel(...)
          -> For each of 449 avatars:
            -> CreatePickerItem() with placeholder image only (instant)
          -> ApplyFilter()
        -> InitializeComponent()
        -> ShowDialog() (window appears instantly)
        -> OnLoaded fires
          -> Start LoadImagesAsync() on background thread
            -> For each avatar without a cached image:
              -> Load thumbnail asynchronously
              -> Update item image via Dispatcher.Invoke
              -> Continue to next avatar
```

## Components

### AvatarPickerViewModel

**What it does:** Manages avatar list state, filtering, and image loading.

**Changes:**
- Constructor creates items with placeholder images only (no HTTP calls)
- New `LoadImagesAsync(CancellationToken)` method loads thumbnails in background
- Items update progressively as images load
- Cancellation token allows stopping on window close

**Dependencies:** `AvatarImageService`, `Dispatcher` for UI thread updates

**Interface:**
```csharp
public AvatarPickerViewModel(...) // Constructor unchanged signature, internal behavior changes
public Task LoadImagesAsync(CancellationToken cancellationToken) // New
```

### AvatarImageService

**What it does:** Resolves and caches avatar images.

**Changes:**
- Add `GetAvatarImageAsync(string avatarId, string? customIconPath, string? vrchatThumbnailUrl, CancellationToken)` method
- Uses `HttpClient.GetAsync()` instead of blocking `GetByteArrayAsync().GetAwaiter().GetResult()`
- Returns `ImageSource?` after loading and caching
- Keeps existing sync `GetAvatarImage()` for compatibility

**Dependencies:** `HttpClient`, file system for caching

**Interface:**
```csharp
public async Task<ImageSource?> GetAvatarImageAsync(string avatarId, string? customIconPath, string? vrchatThumbnailUrl, CancellationToken cancellationToken) // New
public ImageSource? GetAvatarImage(string avatarId, string? customIconPath, string? vrchatThumbnailUrl) // Existing, unchanged
```

### AvatarPickerWindow

**What it does:** UI window for avatar selection.

**Changes:**
- `OnLoaded` starts `viewModel.LoadImagesAsync()` with a `CancellationTokenSource`
- `OnWindowClosed` cancels the token to stop background loading
- XAML: Change `ListViewControl` ItemsPanel from default to `WrapPanel`

**Dependencies:** `AvatarPickerViewModel`, `CancellationTokenSource`

### XAML Changes

**AvatarPickerWindow.xaml:**
- Add `ItemsPanel` to `ListViewControl` with `WrapPanel`
- Keep existing `AvatarListItemTemplate` unchanged

## Data Flow

1. Window opens with all items showing placeholder images
2. Background task iterates through avatars
3. For each avatar:
   - Check if custom icon exists (sync, fast)
   - Check if cached thumbnail exists (sync, fast)
   - If neither, download thumbnail asynchronously
   - Update the item's `Image` property via `Dispatcher.Invoke`
   - Continue to next avatar
4. If user closes window, cancellation stops remaining downloads
5. Already-loaded images remain visible

## Error Handling

- Failed downloads: Log warning, keep placeholder, continue to next avatar
- Custom icon load failure: Keep placeholder, continue
- Cache write failure: Keep placeholder in memory, continue
- Cancellation: Stop loading, no error shown to user
- All errors are non-fatal; the window remains usable with placeholders

## Testing

**Manual testing:**
1. Open Pick Avatar window - should appear instantly
2. Watch images populate progressively
3. Switch to list view - items should flow left-to-right and wrap
4. Close window while images are loading - no errors
5. Reopen window - previously cached images should appear faster

**Build check:**
- `dotnet build "VrcTwitchOscBridge.csproj" --no-restore` must pass

## Risks

- **Low risk:** The async loading is additive; existing sync behavior is preserved
- **Low risk:** WrapPanel for list view is a standard WPF pattern
- **Medium risk:** If `Dispatcher.Invoke` is called too frequently, it could cause UI jank. Mitigation: use `Dispatcher.BeginInvoke` with `DispatcherPriority.Background` to yield to UI rendering between updates.
- **Low risk:** Cancellation token handling must be correct to avoid `OperationCanceledException` crashes

## Scope

**In scope:**
- AvatarPickerViewModel async image loading
- AvatarImageService async method
- AvatarPickerWindow lifecycle management for loading
- ListView ItemsPanel change to WrapPanel

**Out of scope:**
- Virtualization improvements (keep as-is for now)
- Image download retry logic (keep simple for now)
- Progress indicator UI (can add later if needed)
- List view template redesign (keep existing horizontal layout)
