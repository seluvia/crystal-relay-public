# Avatar Picker Refresh Icons Button Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Add a ↻ Refresh Icons button to the Pick Avatar title bar that clears the thumbnail cache and re-downloads all avatar images from the VRChat API.

**Architecture:** `AvatarImageService` gains `ClearDiskCache()`. `AvatarPickerViewModel` gains `IsImageLoading` bool property and `RefreshAllImagesAsync()` which clears both caches, resets all items to placeholder, then calls `LoadImagesAsync()`. The title bar gets a ↻ button bound to `IsImageLoading` for enabled/disabled state. `LoadImagesAsync` sets `IsImageLoading` true at start and false at end.

**Tech Stack:** C#, WPF, .NET 10, data binding, `INotifyPropertyChanged` (via existing `ObservableObject` base)

---

## File Structure

| File | Change |
|------|--------|
| `VrcTwitchOscBridge/Services/AvatarImageService.cs` | Add `ClearDiskCache()` |
| `VrcTwitchOscBridge/ViewModels/AvatarPickerViewModel.cs` | Add `IsImageLoading` property + `RefreshAllImagesAsync()`, update `LoadImagesAsync` to set it |
| `VrcTwitchOscBridge/AvatarPickerWindow.xaml` | Add ↻ button in title bar (new column, left of gear) |
| `VrcTwitchOscBridge/AvatarPickerWindow.xaml.cs` | Add `OnRefreshIconsClicked` handler |

---

### Task 1: Add ClearDiskCache to AvatarImageService

**Files:**
- Modify: `VrcTwitchOscBridge/Services/AvatarImageService.cs`

- [ ] **Step 1: Add `ClearDiskCache` method**

Add after the existing `ClearCache()` method (around line 103):

```csharp
/// <summary>
/// Deletes all cached thumbnail files from disk so they are re-downloaded on next load.
/// </summary>
public void ClearDiskCache()
{
    if (string.IsNullOrWhiteSpace(cacheFolder) || !Directory.Exists(cacheFolder))
    {
        return;
    }

    foreach (var file in Directory.GetFiles(cacheFolder))
    {
        try
        {
            File.Delete(file);
        }
        catch
        {
            // File may be locked; skip and continue
        }
    }
}
```

- [ ] **Step 2: Build to verify**

Run: `dotnet build "E:\!!!Program to work on\Proper Crystal Relay\VrcTwitchOscBridge\VrcTwitchOscBridge.csproj" --no-restore`
Expected: 0 errors, 0 warnings

---

### Task 2: Add IsImageLoading and RefreshAllImagesAsync to AvatarPickerViewModel

**Files:**
- Modify: `VrcTwitchOscBridge/ViewModels/AvatarPickerViewModel.cs`

The current `LoadImagesAsync` does NOT set any loading state. The current `CancelImageLoading` disposes the token. The `imageService` field is `AvatarImageService`. The `AllAvatars` and `FilteredAvatars` are `ObservableCollection<AvatarPickerItem>`. The base class is `ObservableObject` which has `RaisePropertyChanged`.

- [ ] **Step 1: Add `IsImageLoading` property**

Add this field and property after the `imageLoadCancellation` field (around line 57):

```csharp
private bool isImageLoading;

public bool IsImageLoading
{
    get => isImageLoading;
    private set
    {
        if (isImageLoading != value)
        {
            isImageLoading = value;
            RaisePropertyChanged(nameof(IsImageLoading));
        }
    }
}
```

- [ ] **Step 2: Update `LoadImagesAsync` to set `IsImageLoading`**

Find `LoadImagesAsync` (around line 62). Add `IsImageLoading = true;` right after the new `CancellationTokenSource` is created, and `IsImageLoading = false;` at the very end. The method goes from:

```csharp
public async Task LoadImagesAsync()
{
    imageLoadCancellation?.Cancel();
    imageLoadCancellation?.Dispose();
    imageLoadCancellation = new CancellationTokenSource();
    var cancellationToken = imageLoadCancellation.Token;

    int loaded = 0, noUrl = 0, failed = 0;

    foreach (var avatar in AllAvatars)
    {
        // ... loop body unchanged ...
    }

    Debug.WriteLine($"[AvatarPicker] Image loading complete: {loaded} loaded, {noUrl} no URL, {failed} failed");
}
```

To:

```csharp
public async Task LoadImagesAsync()
{
    imageLoadCancellation?.Cancel();
    imageLoadCancellation?.Dispose();
    imageLoadCancellation = new CancellationTokenSource();
    var cancellationToken = imageLoadCancellation.Token;

    IsImageLoading = true;
    int loaded = 0, noUrl = 0, failed = 0;

    foreach (var avatar in AllAvatars)
    {
        if (cancellationToken.IsCancellationRequested)
        {
            break;
        }

        var thumbnailUrl = avatar.ThumbnailUrl;

        if (string.IsNullOrWhiteSpace(thumbnailUrl))
        {
            noUrl++;
            Debug.WriteLine($"[AvatarPicker] No thumbnail URL for '{avatar.Name}' ({avatar.Id})");
            continue;
        }

        var newImage = await imageService.GetAvatarImageAsync(avatar.Id, customIconPath: null, thumbnailUrl, cancellationToken);

        if (cancellationToken.IsCancellationRequested)
        {
            break;
        }

        if (newImage is not null && !IsPlaceholderImage(newImage))
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
            loaded++;
        }
        else
        {
            failed++;
            Debug.WriteLine($"[AvatarPicker] Failed to load thumbnail for '{avatar.Name}' ({avatar.Id}) from {thumbnailUrl}");
        }
    }

    IsImageLoading = false;
    Debug.WriteLine($"[AvatarPicker] Image loading complete: {loaded} loaded, {noUrl} no URL, {failed} failed");
}
```

- [ ] **Step 3: Add `RefreshAllImagesAsync` method**

Add after `CancelImageLoading()` (around line 143):

```csharp
/// <summary>
/// Clears all cached thumbnails and re-downloads them from the VRChat API.
/// </summary>
public async Task RefreshAllImagesAsync()
{
    CancelImageLoading();
    imageService.ClearDiskCache();
    imageService.ClearCache();

    var placeholder = imageService.GetPlaceholderImage();
    for (var i = 0; i < AllAvatars.Count; i++)
    {
        var avatar = AllAvatars[i];
        var reset = new AvatarPickerItem(avatar.Id, avatar.Name, avatar.SourceLabel, placeholder, avatar.ThumbnailUrl);
        AllAvatars[i] = reset;
    }

    // Rebuild FilteredAvatars to reflect the reset items
    ApplyFilter();

    await LoadImagesAsync();
}
```

- [ ] **Step 4: Build to verify**

Run: `dotnet build "E:\!!!Program to work on\Proper Crystal Relay\VrcTwitchOscBridge\VrcTwitchOscBridge.csproj" --no-restore`
Expected: 0 errors, 0 warnings

---

### Task 3: Add ↻ button to the title bar XAML

**Files:**
- Modify: `VrcTwitchOscBridge/AvatarPickerWindow.xaml`

The current title bar Grid has 3 columns: `*` (title), `Auto` (gear button, Grid.Column="1"), `Auto` (close button, Grid.Column="2"). We need to add a new `Auto` column for the refresh button between the title and the gear.

- [ ] **Step 1: Add a fourth column and the refresh button**

Find the title bar Grid column definitions and buttons (around line 571). Change from:

```xml
<Grid Margin="12,0,8,0">
    <Grid.ColumnDefinitions>
        <ColumnDefinition Width="*" />
        <ColumnDefinition Width="Auto" />
        <ColumnDefinition Width="Auto" />
    </Grid.ColumnDefinitions>

    <DockPanel VerticalAlignment="Center">
        <!-- title content unchanged -->
    </DockPanel>

    <Button Grid.Column="1"
            Style="{StaticResource TitleBarManageButtonStyle}"
            Click="OnManageButtonClicked">
        <TextBlock Text="&#x2699;"
                   FontSize="14"
                   FontWeight="SemiBold"
                   Foreground="{DynamicResource TitleBarTextBrush}" />
    </Button>

    <Button Grid.Column="2"
            Style="{StaticResource TitleBarCloseButtonStyle}"
            Click="OnCloseButtonClicked">
        <TextBlock Text="&#x2715;"
                   FontSize="13"
                   FontWeight="SemiBold"
                   Foreground="{DynamicResource TitleBarTextBrush}" />
    </Button>
```

To:

```xml
<Grid Margin="12,0,8,0">
    <Grid.ColumnDefinitions>
        <ColumnDefinition Width="*" />
        <ColumnDefinition Width="Auto" />
        <ColumnDefinition Width="Auto" />
        <ColumnDefinition Width="Auto" />
    </Grid.ColumnDefinitions>

    <DockPanel VerticalAlignment="Center">
        <!-- title content unchanged -->
    </DockPanel>

    <Button Grid.Column="1"
            x:Name="RefreshIconsButton"
            Style="{StaticResource TitleBarManageButtonStyle}"
            IsEnabled="{Binding IsImageLoading, Converter={StaticResource InverseBooleanConverter}}"
            Click="OnRefreshIconsClicked"
            ToolTip="{loc:Translate 'Refresh Icons'}">
        <TextBlock Text="&#x21BB;"
                   FontSize="14"
                   FontWeight="SemiBold"
                   Foreground="{DynamicResource TitleBarTextBrush}" />
    </Button>

    <Button Grid.Column="2"
            Style="{StaticResource TitleBarManageButtonStyle}"
            Click="OnManageButtonClicked">
        <TextBlock Text="&#x2699;"
                   FontSize="14"
                   FontWeight="SemiBold"
                   Foreground="{DynamicResource TitleBarTextBrush}" />
    </Button>

    <Button Grid.Column="3"
            Style="{StaticResource TitleBarCloseButtonStyle}"
            Click="OnCloseButtonClicked">
        <TextBlock Text="&#x2715;"
                   FontSize="13"
                   FontWeight="SemiBold"
                   Foreground="{DynamicResource TitleBarTextBrush}" />
    </Button>
```

- [ ] **Step 2: Add `InverseBooleanConverter` to Window.Resources**

Find the `<!-- Converters -->` comment in `Window.Resources` (around line 57). Add the converter:

```xml
<!-- Converters -->
<BooleanToVisibilityConverter x:Key="BoolToVisibilityConverter" />
<local:EnumBooleanConverter x:Key="EnumBooleanConverter" />
<local:EnumToVisibilityConverter x:Key="EnumToVisibilityConverter" />
<local:InverseBooleanConverter x:Key="InverseBooleanConverter" />
```

- [ ] **Step 3: Verify InverseBooleanConverter exists**

Check whether `InverseBooleanConverter` already exists in the codebase:

Run: `rg "InverseBooleanConverter" "E:\!!!Program to work on\Proper Crystal Relay\VrcTwitchOscBridge" --include="*.cs" -l`

If it exists, note the namespace. If it does NOT exist, add it to a new file or an existing converter file. A minimal implementation:

```csharp
using System.Globalization;
using System.Windows.Data;

namespace VrcTwitchOscBridge;

[ValueConversion(typeof(bool), typeof(bool))]
public sealed class InverseBooleanConverter : IValueConverter
{
    public object Convert(object value, Type targetType, object parameter, CultureInfo culture)
        => value is bool b && !b;

    public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture)
        => value is bool b && !b;
}
```

If the converter already exists in a different namespace, use that namespace in XAML instead of `local:`.

- [ ] **Step 4: Build to verify**

Run: `dotnet build "E:\!!!Program to work on\Proper Crystal Relay\VrcTwitchOscBridge\VrcTwitchOscBridge.csproj" --no-restore`
Expected: 0 errors, 0 warnings

---

### Task 4: Add OnRefreshIconsClicked handler to code-behind

**Files:**
- Modify: `VrcTwitchOscBridge/AvatarPickerWindow.xaml.cs`

- [ ] **Step 1: Add handler**

Add after the existing `OnManageButtonClicked` method (around line 71):

```csharp
private void OnRefreshIconsClicked(object sender, RoutedEventArgs e)
{
    _ = viewModel.RefreshAllImagesAsync();
}
```

- [ ] **Step 2: Build to verify**

Run: `dotnet build "E:\!!!Program to work on\Proper Crystal Relay\VrcTwitchOscBridge\VrcTwitchOscBridge.csproj" --no-restore`
Expected: 0 errors, 0 warnings

---

### Task 5: Final build

- [ ] **Step 1: Full clean build**

Run: `dotnet build "E:\!!!Program to work on\Proper Crystal Relay\VrcTwitchOscBridge\VrcTwitchOscBridge.csproj" --no-restore`
Expected: 0 errors, 0 warnings

---

## Self-Review

**Spec coverage:**
- ✅ `ClearDiskCache()` → Task 1
- ✅ `IsImageLoading` property → Task 2 Step 1
- ✅ `LoadImagesAsync` sets `IsImageLoading` → Task 2 Step 2
- ✅ `RefreshAllImagesAsync` clears caches + resets placeholders + calls LoadImagesAsync → Task 2 Step 3
- ✅ ↻ button in title bar, left of gear → Task 3 Step 1
- ✅ Button disabled while loading via `InverseBooleanConverter` → Task 3 Steps 1-3
- ✅ `OnRefreshIconsClicked` handler → Task 4

**Placeholder scan:** No TBD, TODO, or vague items. All code complete.

**Type consistency:**
- `IsImageLoading` defined in Task 2 Step 1, used in Task 3 Step 1 binding — matches
- `RefreshAllImagesAsync()` defined in Task 2 Step 3, called in Task 4 Step 1 — matches
- `ClearDiskCache()` defined in Task 1, called in Task 2 Step 3 — matches
- `GetPlaceholderImage()` already public on `AvatarImageService` (added in earlier session) — matches
- `ApplyFilter()` is private on `AvatarPickerViewModel` and called from `RefreshAllImagesAsync` which is in the same class — matches
- `InverseBooleanConverter` defined in Task 3 Step 3 if absent, referenced as `local:InverseBooleanConverter` in XAML — `local:` namespace is already declared in the XAML file
