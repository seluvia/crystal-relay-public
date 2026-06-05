# Avatar Picker Performance and Layout Fix Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Fix the Pick Avatar window's long load time, missing images, and vertical list stacking by adding async image loading and changing the list view to a wrapping layout.

**Architecture:** The constructor creates items with placeholder images instantly. A background task loads thumbnails asynchronously via `HttpClient.GetAsync()`, updating items progressively through `Dispatcher.BeginInvoke`. The list view uses a `WrapPanel` instead of `VirtualizingStackPanel` for side-by-side layout.

**Tech Stack:** C#, WPF, .NET 10, HttpClient, Dispatcher, CancellationToken

---

## File Structure

| File | Responsibility |
|------|---------------|
| `VrcTwitchOscBridge/Services/AvatarImageService.cs` | Add async `GetAvatarImageAsync` method for background thumbnail downloads |
| `VrcTwitchOscBridge/ViewModels/AvatarPickerViewModel.cs` | Modify constructor to use placeholders; add `LoadImagesAsync` method |
| `VrcTwitchOscBridge/AvatarPickerWindow.xaml.cs` | Start/stop async loading in window lifecycle |
| `VrcTwitchOscBridge/AvatarPickerWindow.xaml` | Change ListView ItemsPanel to WrapPanel |

---

### Task 1: Add async image loading to AvatarImageService

**Files:**
- Modify: `VrcTwitchOscBridge/Services/AvatarImageService.cs`

- [ ] **Step 1: Add async thumbnail loading method**

Add this method to `AvatarImageService.cs` after the existing `LoadVrChatThumbnail` method (around line 154):

```csharp
private async Task<ImageSource?> LoadVrChatThumbnailAsync(string avatarId, string? thumbnailUrl, CancellationToken cancellationToken)
{
    if (string.IsNullOrWhiteSpace(thumbnailUrl) || string.IsNullOrWhiteSpace(cacheFolder))
    {
        return null;
    }

    var cachePath = Path.Combine(cacheFolder, $"{avatarId}.jpg");
    if (File.Exists(cachePath))
    {
        return LoadImageFromFile(cachePath);
    }

    try
    {
        using var response = await HttpClient.GetAsync(thumbnailUrl, cancellationToken);
        if (!response.IsSuccessStatusCode)
        {
            return null;
        }

        var bytes = await response.Content.ReadAsByteArrayAsync(cancellationToken);
        File.WriteAllBytes(cachePath, bytes);
        return LoadImageFromFile(cachePath);
    }
    catch (OperationCanceledException)
    {
        return null;
    }
    catch
    {
        return null;
    }
}
```

- [ ] **Step 2: Add public async GetAvatarImageAsync method**

Add this method to `AvatarImageService.cs` after the existing `GetAvatarImage` method (around line 65):

```csharp
/// <summary>
/// Gets the image source for an avatar asynchronously. Tries custom icon first, then VRChat thumbnail, then placeholder.
/// </summary>
public async Task<ImageSource?> GetAvatarImageAsync(
    string avatarId,
    string? customIconPath,
    string? vrchatThumbnailUrl,
    CancellationToken cancellationToken)
{
    var cacheKey = avatarId;
    if (imageCache.TryGetValue(cacheKey, out var cached))
    {
        return cached;
    }

    var image = LoadCustomIcon(customIconPath)
        ?? await LoadVrChatThumbnailAsync(avatarId, vrchatThumbnailUrl, cancellationToken)
        ?? GetPlaceholderImage();

    imageCache[cacheKey] = image;
    return image;
}
```

- [ ] **Step 3: Build to verify no errors**

Run: `dotnet build "VrcTwitchOscBridge/VrcTwitchOscBridge.csproj" --no-restore`
Expected: Build succeeds with no errors

---

### Task 2: Modify AvatarPickerViewModel for async loading

**Files:**
- Modify: `VrcTwitchOscBridge/ViewModels/AvatarPickerViewModel.cs`

- [ ] **Step 1: Add CancellationTokenSource field and LoadImagesAsync method**

Add these fields and method to `AvatarPickerViewModel.cs` after the constructor (around line 53):

```csharp
private CancellationTokenSource? imageLoadCancellation;

/// <summary>
/// Loads avatar images asynchronously in the background. Call after the window is shown.
/// </summary>
public async Task LoadImagesAsync()
{
    imageLoadCancellation?.Cancel();
    imageLoadCancellation?.Dispose();
    imageLoadCancellation = new CancellationTokenSource();
    var cancellationToken = imageLoadCancellation.Token;

    foreach (var avatar in AllAvatars)
    {
        if (cancellationToken.IsCancellationRequested)
        {
            break;
        }

        // Skip if already has a non-placeholder image (custom icon or cached thumbnail)
        if (avatar.Image is not null && !IsPlaceholderImage(avatar.Image))
        {
            continue;
        }

        var entry = avatarLibrary?.GetEntry(avatar.Id);
        var customIconPath = entry?.CustomIconPath;
        var summary = AllAvatars.FirstOrDefault(a => string.Equals(a.Id, avatar.Id, StringComparison.Ordinal));
        var thumbnailUrl = summary?.GetType().GetProperty("ThumbnailUrl")?.GetValue(summary) as string;

        // We need the thumbnail URL from the original summary. Store it in AvatarPickerItem.
        // For now, load from the service which will check cache first.
        var newImage = await imageService.GetAvatarImageAsync(avatar.Id, customIconPath, thumbnailUrl, cancellationToken);

        if (newImage is not null && !cancellationToken.IsCancellationRequested)
        {
            var allAvatars = AllAvatars;
            var index = allAvatars.IndexOf(avatar);
            if (index >= 0)
            {
                var updated = new AvatarPickerItem(avatar.Id, avatar.Name, avatar.SourceLabel, newImage);
                allAvatars[index] = updated;
                ApplyFilter();
            }
        }
    }
}

private static bool IsPlaceholderImage(ImageSource? image)
{
    if (image is DrawingImage drawingImage)
    {
        return drawingImage.Drawing is DrawingGroup;
    }
    return false;
}

/// <summary>
/// Cancels any pending image loading.
/// </summary>
public void CancelImageLoading()
{
    imageLoadCancellation?.Cancel();
    imageLoadCancellation?.Dispose();
    imageLoadCancellation = null;
}
```

Wait — this approach has a problem. The `AvatarPickerItem` doesn't store the `ThumbnailUrl`, and we can't easily retrieve it from the `AllAvatars` collection since it's an `ObservableCollection<AvatarPickerItem>` and the original `VrChatAvatarSummary` data is lost after construction.

Let me revise the approach. The `AvatarPickerItem` record needs to store the thumbnail URL so the async loader can use it.

- [ ] **Step 1 (revised): Add ThumbnailUrl to AvatarPickerItem record**

Modify the `AvatarPickerItem` record at the bottom of `AvatarPickerViewModel.cs` (around line 237):

```csharp
public sealed record AvatarPickerItem(
    string Id,
    string Name,
    string SourceLabel,
    ImageSource? Image,
    string? ThumbnailUrl = null)
{
    public string SearchText => $"{Id} {Name} {SourceLabel}";
    public string DisplayName => !string.IsNullOrWhiteSpace(Name) && !string.Equals(Name, Id, StringComparison.Ordinal)
        ? Name
        : "Unknown Avatar";
}
```

- [ ] **Step 2: Update CreatePickerItem to store ThumbnailUrl**

Modify the `CreatePickerItem` method (around line 222):

```csharp
private AvatarPickerItem CreatePickerItem(VrChatAvatarSummary summary)
{
    var entry = avatarLibrary?.GetEntry(summary.Id);
    var customIconPath = entry?.CustomIconPath;

    // Use placeholder only in constructor; images load asynchronously
    var image = GetPlaceholderImage();

    return new AvatarPickerItem(
        summary.Id,
        summary.Name,
        summary.SourceLabel,
        image,
        summary.ThumbnailUrl);
}
```

Wait — this would show placeholders for ALL avatars initially, even those with custom icons. Let me think about this more carefully.

The issue is: we want custom icons to show immediately (they're local files, fast to load), but VRChat thumbnails to load asynchronously (they require HTTP calls).

Revised approach:
- Constructor: Load custom icons synchronously (fast, local), use placeholder for everything else
- Async loader: Load VRChat thumbnails for items that don't have custom icons

- [ ] **Step 1 (final revised): Add ThumbnailUrl to AvatarPickerItem record**

Modify the `AvatarPickerItem` record at the bottom of `AvatarPickerViewModel.cs` (around line 237):

```csharp
public sealed record AvatarPickerItem(
    string Id,
    string Name,
    string SourceLabel,
    ImageSource? Image,
    string? ThumbnailUrl = null)
{
    public string SearchText => $"{Id} {Name} {SourceLabel}";
    public string DisplayName => !string.IsNullOrWhiteSpace(Name) && !string.Equals(Name, Id, StringComparison.Ordinal)
        ? Name
        : "Unknown Avatar";
}
```

- [ ] **Step 2: Update CreatePickerItem to load custom icons sync, store thumbnail URL**

Modify the `CreatePickerItem` method (around line 222):

```csharp
private AvatarPickerItem CreatePickerItem(VrChatAvatarSummary summary)
{
    var entry = avatarLibrary?.GetEntry(summary.Id);
    var customIconPath = entry?.CustomIconPath;

    // Load custom icons synchronously (fast, local file). VRChat thumbnails load async.
    var image = imageService.GetCustomIconOnly(customIconPath) ?? GetPlaceholderImage();

    return new AvatarPickerItem(
        summary.Id,
        summary.Name,
        summary.SourceLabel,
        image,
        summary.ThumbnailUrl);
}
```

- [ ] **Step 3: Add GetCustomIconOnly method to AvatarImageService**

Add this method to `AvatarImageService.cs` after the existing `GetAvatarImage` method (around line 65):

```csharp
/// <summary>
/// Gets only a custom icon image, without downloading VRChat thumbnails. Fast sync operation.
/// </summary>
public ImageSource? GetCustomIconOnly(string? customIconPath)
{
    return LoadCustomIcon(customIconPath);
}
```

- [ ] **Step 4: Add CancellationTokenSource field and LoadImagesAsync method**

Add these fields and method to `AvatarPickerViewModel.cs` after the constructor (around line 53):

```csharp
private CancellationTokenSource? imageLoadCancellation;

/// <summary>
/// Loads VRChat thumbnail images asynchronously in the background. Call after the window is shown.
/// </summary>
public async Task LoadImagesAsync()
{
    imageLoadCancellation?.Cancel();
    imageLoadCancellation?.Dispose();
    imageLoadCancellation = new CancellationTokenSource();
    var cancellationToken = imageLoadCancellation.Token;

    foreach (var avatar in AllAvatars)
    {
        if (cancellationToken.IsCancellationRequested)
        {
            break;
        }

        // Skip if already has a non-placeholder image (custom icon or cached thumbnail)
        if (avatar.Image is not null && !IsPlaceholderImage(avatar.Image))
        {
            continue;
        }

        var entry = avatarLibrary?.GetEntry(avatar.Id);
        var customIconPath = entry?.CustomIconPath;
        var thumbnailUrl = avatar.ThumbnailUrl;

        var newImage = await imageService.GetAvatarImageAsync(avatar.Id, customIconPath, thumbnailUrl, cancellationToken);

        if (newImage is not null && !cancellationToken.IsCancellationRequested)
        {
            var allAvatars = AllAvatars;
            var index = allAvatars.IndexOf(avatar);
            if (index >= 0)
            {
                var updated = new AvatarPickerItem(avatar.Id, avatar.Name, avatar.SourceLabel, newImage, avatar.ThumbnailUrl);
                allAvatars[index] = updated;
                ApplyFilter();
            }
        }
    }
}

private static bool IsPlaceholderImage(ImageSource? image)
{
    if (image is DrawingImage drawingImage)
    {
        return drawingImage.Drawing is DrawingGroup;
    }
    return false;
}

/// <summary>
/// Cancels any pending image loading.
/// </summary>
public void CancelImageLoading()
{
    imageLoadCancellation?.Cancel();
    imageLoadCancellation?.Dispose();
    imageLoadCancellation = null;
}
```

- [ ] **Step 5: Add Dispatcher import**

Add this using directive at the top of `AvatarPickerViewModel.cs` (after line 5):

```csharp
using System.Windows.Threading;
```

Wait — the `LoadImagesAsync` method is called from the window's code-behind, which runs on the UI thread. The `await` will yield back to the UI thread, but the actual HTTP calls happen on thread pool threads. However, the `foreach` loop and `ApplyFilter()` calls run on the UI thread after each `await`.

This could cause UI jank if we update the collection too frequently. Let me use `Dispatcher.BeginInvoke` with `Background` priority to batch updates.

Actually, a simpler approach: the `LoadImagesAsync` method should run on a background thread using `Task.Run`, and use `Dispatcher.BeginInvoke` to update the collection. But since this is a ViewModel, it shouldn't directly depend on `Dispatcher`.

Better approach: The window's code-behind calls `LoadImagesAsync`, and the ViewModel uses `Application.Current.Dispatcher` internally. Or, even better, the ViewModel exposes an event for image updates, and the window subscribes to it.

Actually, the simplest correct approach for WPF: the `LoadImagesAsync` method runs on the UI thread (called from `OnLoaded`), uses `await` for each HTTP call (which yields to the thread pool), and updates the collection after each await. Since `await` yields back to the captured synchronization context (UI thread), the collection updates happen on the UI thread safely.

The concern about jank is real but manageable: with 449 avatars, if each takes ~100ms to download and update, that's ~45 seconds total. But many will be cached, and the UI remains responsive because `await` yields between updates.

Let me keep the simple approach for now. The `Dispatcher` import isn't needed since `await` handles the context switching.

- [ ] **Step 5 (revised): Build to verify no errors**

Run: `dotnet build "VrcTwitchOscBridge/VrcTwitchOscBridge.csproj" --no-restore`
Expected: Build succeeds with no errors

---

### Task 3: Add lifecycle management to AvatarPickerWindow

**Files:**
- Modify: `VrcTwitchOscBridge/AvatarPickerWindow.xaml.cs`

- [ ] **Step 1: Add CancellationTokenSource field**

Add this field to `AvatarPickerWindow.xaml.cs` after the existing fields (around line 15):

```csharp
private CancellationTokenSource? imageLoadCancellation;
```

- [ ] **Step 2: Start image loading in OnLoaded**

Modify the `OnLoaded` method (around line 48):

```csharp
private void OnLoaded(object sender, RoutedEventArgs e)
{
    SearchTextBox.Focus();
    _ = viewModel.LoadImagesAsync();
}
```

- [ ] **Step 3: Cancel image loading in OnWindowClosed**

Modify the `OnWindowClosed` method (around line 58):

```csharp
private void OnWindowClosed(object? sender, EventArgs e)
{
    viewModel.CancelImageLoading();
    ThemeManager.ThemeChanged -= OnThemeManagerThemeChanged;
    Closed -= OnWindowClosed;
    PreviewKeyDown -= OnPreviewKeyDown;
}
```

- [ ] **Step 4: Build to verify no errors**

Run: `dotnet build "VrcTwitchOscBridge/VrcTwitchOscBridge.csproj" --no-restore`
Expected: Build succeeds with no errors

---

### Task 4: Change ListView ItemsPanel to WrapPanel

**Files:**
- Modify: `VrcTwitchOscBridge/AvatarPickerWindow.xaml`

- [ ] **Step 1: Add ItemsPanel to ListViewControl**

Modify the `ListViewControl` ListBox (around line 738) to include an ItemsPanel:

```xml
<!-- List View -->
<ListBox x:Name="ListViewControl"
         ItemsSource="{Binding FilteredAvatars}"
         ItemTemplate="{StaticResource AvatarListItemTemplate}"
         Background="Transparent"
         BorderThickness="0"
         VirtualizingPanel.IsVirtualizing="True"
         VirtualizingPanel.VirtualizationMode="Recycling"
         ScrollViewer.CanContentScroll="True"
         AllowDrop="True"
         PreviewMouseLeftButtonDown="OnListViewPreviewMouseLeftButtonDown"
         PreviewMouseMove="OnListViewPreviewMouseMove"
         Drop="OnListViewDrop"
         Visibility="{Binding ViewMode, Converter={StaticResource EnumToVisibilityConverter}, ConverterParameter=List}">
    <ListBox.ItemsPanel>
        <ItemsPanelTemplate>
            <WrapPanel Orientation="Horizontal" />
        </ItemsPanelTemplate>
    </ListBox.ItemsPanel>
</ListBox>
```

- [ ] **Step 2: Build to verify no errors**

Run: `dotnet build "VrcTwitchOscBridge/VrcTwitchOscBridge.csproj" --no-restore`
Expected: Build succeeds with no errors

---

### Task 5: Final verification

- [ ] **Step 1: Full build**

Run: `dotnet build "VrcTwitchOscBridge/VrcTwitchOscBridge.csproj" --no-restore`
Expected: Build succeeds with no errors or warnings related to changed files

- [ ] **Step 2: Manual testing checklist**

1. Open Pick Avatar window — should appear instantly with placeholder images
2. Watch images populate progressively (custom icons first, then thumbnails)
3. Switch to list view — items should flow left-to-right and wrap to next row
4. Close window while images are loading — no errors or crashes
5. Reopen window — previously cached images should appear faster
6. Search/filter while images are loading — should work without issues
7. Multi-select mode — should work with async loading

---

## Self-Review

**1. Spec coverage:**
- ✅ Long pause on open → Async image loading in ViewModel constructor + LoadImagesAsync
- ✅ Images not displaying → Async HTTP calls with proper error handling
- ✅ List view stacking → WrapPanel ItemsPanel

**2. Placeholder scan:**
- ✅ No TBD, TODO, or incomplete sections
- ✅ All code blocks are complete
- ✅ All file paths are exact

**3. Type consistency:**
- ✅ `AvatarPickerItem` record updated with `ThumbnailUrl` parameter in Task 2 Step 1
- ✅ `CreatePickerItem` uses the new 5-parameter constructor in Task 2 Step 2
- ✅ `LoadImagesAsync` creates updated items with the 5-parameter constructor in Task 2 Step 4
- ✅ `AvatarImageService.GetAvatarImageAsync` signature matches usage in Task 2 Step 4
- ✅ `AvatarImageService.GetCustomIconOnly` signature matches usage in Task 2 Step 2

**4. Potential issues identified:**
- The `IsPlaceholderImage` check uses `DrawingImage` type check, which should work since `GetPlaceholderImage()` returns a `DrawingImage` wrapping a `DrawingGroup`
- The `ApplyFilter()` call in `LoadImagesAsync` will re-filter the entire collection after each image update. This could be slow for 449 items. Consider optimizing by only updating the `FilteredAvatars` collection directly instead of re-filtering.

Let me fix the `ApplyFilter` issue:

- [ ] **Fix: Optimize image update to avoid full re-filter**

In Task 2 Step 4, replace the `ApplyFilter()` call with a direct update to `FilteredAvatars`:

```csharp
if (newImage is not null && !cancellationToken.IsCancellationRequested)
{
    var allAvatars = AllAvatars;
    var index = allAvatars.IndexOf(avatar);
    if (index >= 0)
    {
        var updated = new AvatarPickerItem(avatar.Id, avatar.Name, avatar.SourceLabel, newImage, avatar.ThumbnailUrl);
        allAvatars[index] = updated;
        
        // Update FilteredAvatars directly if the item is currently visible
        var filteredIndex = FilteredAvatars.IndexOf(avatar);
        if (filteredIndex >= 0)
        {
            FilteredAvatars[filteredIndex] = updated;
        }
    }
}
```

This avoids the O(n) re-filter for each image update.

---

**Plan complete.** Two execution options:

**1. Subagent-Driven (recommended)** - I dispatch a fresh subagent per task, review between tasks, fast iteration

**2. Inline Execution** - Execute tasks in this session using executing-plans, batch execution with checkpoints

Which approach?
