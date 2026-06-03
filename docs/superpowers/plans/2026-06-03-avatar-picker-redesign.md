# Avatar Picker Redesign — Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Replace all avatar selection ComboBox dropdowns with a dedicated AvatarPickerWindow that shows avatars in a grid/list with images, search, groups, tags, and custom icons.

**Architecture:** A standalone WPF window (AvatarPickerWindow) with its own ViewModel (AvatarPickerViewModel), opened via a shared service (AvatarPickerService). New models (AvatarLibrary, AvatarGroup, AvatarTag, AvatarLibraryEntry) store organization data in AppSettings. AvatarImageService resolves images from VRChat API, custom local files, or placeholder.

**Tech Stack:** C#, WPF, XAML, .NET 10, existing Crystal Relay infrastructure (ObservableObject, RelayCommand, AsyncRelayCommand, SettingsStore, VrChatApiClient, ThemeManager)

---

## File Structure

### New Files

| File | Responsibility |
|------|---------------|
| Models/AvatarLibrary.cs | AvatarLibrary, AvatarGroup, AvatarTag, AvatarLibraryEntry models |
| Models/AvatarPickerViewMode.cs | Enum for Grid/List view mode |
| Services/AvatarImageService.cs | Resolves avatar images (VRChat API -> custom -> placeholder) |
| Services/AvatarPickerService.cs | Opens picker window, returns selection result |
| ViewModels/AvatarPickerViewModel.cs | Drives picker UI logic |
| AvatarPickerWindow.xaml | Picker window UI |
| AvatarPickerWindow.xaml.cs | Picker window code-behind |

### Modified Files

| File | Change |
|------|--------|
| Models/AppSettings.cs | Add AvatarLibrary property |
| ViewModels/MainWindowViewModel.cs | Add OpenAvatarPickerCommand, remove ComboBox bindings |
| MainWindow.xaml | Replace avatar ComboBoxes with Browse buttons |
| VrcTwitchOscBridge.csproj | Add all new files |
| Resources/Localization/en-US.extra.json | Add localization keys |

---

## Phase 1: Core Picker Window

### Task 1: Avatar Picker Models

**Files:**
- Create: `VrcTwitchOscBridge/Models/AvatarLibrary.cs`
- Create: `VrcTwitchOscBridge/Models/AvatarPickerViewMode.cs`

- [ ] **Step 1: Create AvatarPickerViewMode enum**

```csharp
// VrcTwitchOscBridge/Models/AvatarPickerViewMode.cs
namespace VrcTwitchOscBridge.Models;

public enum AvatarPickerViewMode
{
    Grid,
    List
}
```

- [ ] **Step 2: Create AvatarLibrary models**

```csharp
// VrcTwitchOscBridge/Models/AvatarLibrary.cs
using System.Collections.ObjectModel;
using VrcTwitchOscBridge.Infrastructure;

namespace VrcTwitchOscBridge.Models;

public sealed class AvatarLibrary : ObservableObject
{
    private AvatarPickerViewMode lastViewMode = AvatarPickerViewMode.Grid;
    private ObservableCollection<AvatarLibraryEntry> entries = [];
    private ObservableCollection<AvatarGroup> groups = [];
    private ObservableCollection<AvatarTag> tags = [];

    public AvatarPickerViewMode LastViewMode
    {
        get => lastViewMode;
        set => SetProperty(ref lastViewMode, value);
    }

    public ObservableCollection<AvatarLibraryEntry> Entries
    {
        get => entries;
        set => SetProperty(ref entries, value ?? []);
    }

    public ObservableCollection<AvatarGroup> Groups
    {
        get => groups;
        set => SetProperty(ref groups, value ?? []);
    }

    public ObservableCollection<AvatarTag> Tags
    {
        get => tags;
        set => SetProperty(ref tags, value ?? []);
    }

    public AvatarLibraryEntry? GetEntry(string avatarId) =>
        Entries.FirstOrDefault(e => string.Equals(e.AvatarId, avatarId, StringComparison.Ordinal));

    public void EnsureEntry(string avatarId)
    {
        if (GetEntry(avatarId) is null)
        {
            Entries.Add(new AvatarLibraryEntry { AvatarId = avatarId });
        }
    }
}

public sealed class AvatarLibraryEntry : ObservableObject
{
    private string avatarId = string.Empty;
    private string customIconPath = string.Empty;
    private List<string> groupIds = [];
    private List<string> tagIds = [];

    public string AvatarId
    {
        get => avatarId;
        set => SetProperty(ref avatarId, value);
    }

    public string CustomIconPath
    {
        get => customIconPath;
        set => SetProperty(ref customIconPath, value);
    }

    public List<string> GroupIds
    {
        get => groupIds;
        set => SetProperty(ref groupIds, value ?? []);
    }

    public List<string> TagIds
    {
        get => tagIds;
        set => SetProperty(ref tagIds, value ?? []);
    }
}

public sealed class AvatarGroup : ObservableObject
{
    private string id = Guid.NewGuid().ToString();
    private string name = string.Empty;
    private bool isCollapsed;
    private int sortOrder;

    public string Id
    {
        get => id;
        set => SetProperty(ref id, value);
    }

    public string Name
    {
        get => name;
        set => SetProperty(ref name, value);
    }

    public bool IsCollapsed
    {
        get => isCollapsed;
        set => SetProperty(ref isCollapsed, value);
    }

    public int SortOrder
    {
        get => sortOrder;
        set => SetProperty(ref sortOrder, value);
    }
}

public sealed class AvatarTag : ObservableObject
{
    private string id = Guid.NewGuid().ToString();
    private string name = string.Empty;
    private string colorHex = "#A855F7";

    public string Id
    {
        get => id;
        set => SetProperty(ref id, value);
    }

    public string Name
    {
        get => name;
        set => SetProperty(ref name, value);
    }

    public string ColorHex
    {
        get => colorHex;
        set => SetProperty(ref colorHex, value);
    }
}
```

- [ ] **Step 3: Add models to csproj**

In `VrcTwitchOscBridge/VrcTwitchOscBridge.csproj`, add after the existing `Models/VrChatAvatarSummary.cs` line:

```xml
    <Compile Include="Models\AvatarLibrary.cs" />
    <Compile Include="Models\AvatarPickerViewMode.cs" />
```

- [ ] **Step 4: Build and verify**

Run: `dotnet build "VrcTwitchOscBridge.csproj" --no-restore`
Expected: Build succeeded, 0 warnings, 0 errors

- [ ] **Step 5: Commit**

```bash
git add VrcTwitchOscBridge/Models/AvatarLibrary.cs VrcTwitchOscBridge/Models/AvatarPickerViewMode.cs VrcTwitchOscBridge/VrcTwitchOscBridge.csproj
git commit -m "feat: add AvatarLibrary models for avatar picker"
```

---

### Task 2: AvatarImageService

**Files:**
- Create: `VrcTwitchOscBridge/Services/AvatarImageService.cs`

- [ ] **Step 1: Create AvatarImageService**

```csharp
// VrcTwitchOscBridge/Services/AvatarImageService.cs
using System.IO;
using System.Net.Http;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using VrcTwitchOscBridge.Models;

namespace VrcTwitchOscBridge.Services;

/// <summary>
/// Resolves avatar images from custom local icons, VRChat API thumbnails, or a built-in placeholder.
/// Images are cached locally to avoid repeated downloads.
/// </summary>
public sealed class AvatarImageService
{
    private static readonly HttpClient HttpClient = new()
    {
        Timeout = TimeSpan.FromSeconds(10)
    };

    private readonly string iconFolder;
    private readonly string cacheFolder;
    private readonly Dictionary<string, ImageSource> imageCache = new(StringComparer.Ordinal);

    public AvatarImageService()
    {
        var themeAssets = AppDataPaths.Instance?.ThemeAssetsFolder ?? string.Empty;
        iconFolder = string.IsNullOrWhiteSpace(themeAssets)
            ? string.Empty
            : Path.Combine(themeAssets, "AvatarIcons");
        cacheFolder = string.IsNullOrWhiteSpace(iconFolder)
            ? string.Empty
            : Path.Combine(iconFolder, "Cache");

        if (!string.IsNullOrWhiteSpace(iconFolder) && !Directory.Exists(iconFolder))
        {
            Directory.CreateDirectory(iconFolder);
        }
        if (!string.IsNullOrWhiteSpace(cacheFolder) && !Directory.Exists(cacheFolder))
        {
            Directory.CreateDirectory(cacheFolder);
        }
    }

    /// <summary>
    /// Gets the image source for an avatar. Tries custom icon first, then VRChat thumbnail, then placeholder.
    /// </summary>
    public ImageSource? GetAvatarImage(
        string avatarId,
        string? customIconPath,
        string? vrchatThumbnailUrl)
    {
        var cacheKey = avatarId;
        if (imageCache.TryGetValue(cacheKey, out var cached))
        {
            return cached;
        }

        var image = LoadCustomIcon(customIconPath)
            ?? LoadVrChatThumbnail(avatarId, vrchatThumbnailUrl)
            ?? GetPlaceholderImage();

        imageCache[cacheKey] = image;
        return image;
    }

    /// <summary>
    /// Clears the in-memory image cache. Called when avatar list is refreshed.
    /// </summary>
    public void ClearCache()
    {
        imageCache.Clear();
    }

    /// <summary>
    /// Saves a custom icon file for an avatar and returns the relative path.
    /// </summary>
    public string? SaveCustomIcon(string avatarId, string sourceFilePath)
    {
        if (string.IsNullOrWhiteSpace(iconFolder) || !File.Exists(sourceFilePath))
        {
            return null;
        }

        var extension = Path.GetExtension(sourceFilePath);
        var fileName = $"{avatarId}{extension}";
        var destPath = Path.Combine(iconFolder, fileName);

        File.Copy(sourceFilePath, destPath, overwrite: true);
        return Path.Combine("AvatarIcons", fileName);
    }

    /// <summary>
    /// Gets the full path to a custom icon from its relative path.
    /// </summary>
    public string? ResolveCustomIconPath(string? relativePath)
    {
        if (string.IsNullOrWhiteSpace(relativePath) || string.IsNullOrWhiteSpace(iconFolder))
        {
            return null;
        }

        var fullPath = Path.Combine(iconFolder, Path.GetFileName(relativePath));
        return File.Exists(fullPath) ? fullPath : null;
    }

    private ImageSource? LoadCustomIcon(string? customIconPath)
    {
        var fullPath = ResolveCustomIconPath(customIconPath);
        if (string.IsNullOrWhiteSpace(fullPath))
        {
            return null;
        }

        try
        {
            var bitmap = new BitmapImage();
            bitmap.BeginInit();
            bitmap.CacheOption = BitmapCacheOption.OnLoad;
            bitmap.UriSource = new Uri(fullPath);
            bitmap.EndInit();
            bitmap.Freeze();
            return bitmap;
        }
        catch
        {
            return null;
        }
    }

    private ImageSource? LoadVrChatThumbnail(string avatarId, string? thumbnailUrl)
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
            var bytes = HttpClient.GetByteArrayAsync(thumbnailUrl).GetAwaiter().GetResult();
            File.WriteAllBytes(cachePath, bytes);
            return LoadImageFromFile(cachePath);
        }
        catch
        {
            return null;
        }
    }

    private static ImageSource? LoadImageFromFile(string path)
    {
        try
        {
            var bitmap = new BitmapImage();
            bitmap.BeginInit();
            bitmap.CacheOption = BitmapCacheOption.OnLoad;
            bitmap.UriSource = new Uri(path);
            bitmap.EndInit();
            bitmap.Freeze();
            return bitmap;
        }
        catch
        {
            return null;
        }
    }

    private static ImageSource GetPlaceholderImage()
    {
        var drawing = new DrawingGroup();
        using (var context = drawing.Open())
        {
            context.DrawRectangle(
                new SolidColorBrush(Color.FromRgb(40, 25, 60)),
                new Pen(new SolidColorBrush(Color.FromRgb(80, 50, 120)), 1),
                new Rect(0, 0, 120, 120));

            context.DrawEllipse(
                new SolidColorBrush(Color.FromRgb(120, 90, 160)),
                null,
                new Point(60, 45), 20, 20);
            context.DrawEllipse(
                new SolidColorBrush(Color.FromRgb(120, 90, 160)),
                null,
                new Point(60, 95), 35, 25);
        }

        drawing.Freeze();
        var visual = new DrawingImage(drawing);
        visual.Freeze();
        return visual;
    }
}
```

- [ ] **Step 2: Add to csproj**

Add after existing service entries:

```xml
    <Compile Include="Services\AvatarImageService.cs" />
```

- [ ] **Step 3: Build and verify**

Run: `dotnet build "VrcTwitchOscBridge.csproj" --no-restore`
Expected: Build succeeded

- [ ] **Step 4: Commit**

```bash
git add VrcTwitchOscBridge/Services/AvatarImageService.cs VrcTwitchOscBridge/VrcTwitchOscBridge.csproj
git commit -m "feat: add AvatarImageService for avatar image resolution"
```

---

### Task 3: AvatarPickerViewModel

**Files:**
- Create: `VrcTwitchOscBridge/ViewModels/AvatarPickerViewModel.cs`

- [ ] **Step 1: Create AvatarPickerViewModel**

```csharp
// VrcTwitchOscBridge/ViewModels/AvatarPickerViewModel.cs
using System.Collections.ObjectModel;
using System.Windows.Media;
using VrcTwitchOscBridge.Infrastructure;
using VrcTwitchOscBridge.Models;
using VrcTwitchOscBridge.Services;

namespace VrcTwitchOscBridge.ViewModels;

public sealed class AvatarPickerViewModel : ObservableObject
{
    private readonly AvatarImageService imageService;
    private readonly AvatarLibrary? avatarLibrary;
    private string searchText = string.Empty;
    private AvatarPickerViewMode viewMode = AvatarPickerViewMode.Grid;
    private string? selectedAvatarId;
    private string? selectedAvatarName;
    private bool isMultiSelectMode;

    public AvatarPickerViewModel(
        IReadOnlyList<VrChatAvatarSummary> avatars,
        AvatarImageService imageService,
        AvatarLibrary? avatarLibrary = null,
        string? currentAvatarId = null,
        IReadOnlyList<string>? multiSelectCurrentIds = null)
    {
        this.imageService = imageService;
        this.avatarLibrary = avatarLibrary;

        if (multiSelectCurrentIds is { Count: > 0 })
        {
            isMultiSelectMode = true;
            SelectedMultiAvatarIds = new HashSet<string>(multiSelectCurrentIds, StringComparer.Ordinal);
        }

        AllAvatars = new ObservableCollection<AvatarPickerItem>(
            avatars.Select(a => CreatePickerItem(a)));

        if (!string.IsNullOrWhiteSpace(currentAvatarId))
        {
            selectedAvatarId = currentAvatarId;
            var current = AllAvatars.FirstOrDefault(a => string.Equals(a.Id, currentAvatarId, StringComparison.Ordinal));
            if (current is not null)
            {
                selectedAvatarName = current.Name;
            }
        }

        viewMode = avatarLibrary?.LastViewMode ?? AvatarPickerViewMode.Grid;

        ApplyFilter();
    }

    public ObservableCollection<AvatarPickerItem> AllAvatars { get; }
    public ObservableCollection<AvatarPickerItem> FilteredAvatars { get; } = [];

    public AvatarPickerItem? SelectedItem
    {
        get => FilteredAvatars.FirstOrDefault(a => string.Equals(a.Id, selectedAvatarId, StringComparison.Ordinal));
        set
        {
            if (value is not null)
            {
                selectedAvatarId = value.Id;
                selectedAvatarName = value.Name;
                RaisePropertyChanged(nameof(SelectedAvatarDisplayName));
                RaisePropertyChanged(nameof(CanConfirm));
            }
        }
    }

    public HashSet<string> SelectedMultiAvatarIds { get; } = [];

    public string SearchText
    {
        get => searchText;
        set
        {
            if (SetProperty(ref searchText, value))
            {
                ApplyFilter();
            }
        }
    }

    public AvatarPickerViewMode ViewMode
    {
        get => viewMode;
        set
        {
            if (SetProperty(ref viewMode, value))
            {
                avatarLibrary.LastViewMode = value;
            }
        }
    }

    public bool IsMultiSelectMode => isMultiSelectMode;
    public bool CanConfirm => isMultiSelectMode ? SelectedMultiAvatarIds.Count > 0 : !string.IsNullOrWhiteSpace(selectedAvatarId);
    public string SelectedAvatarDisplayName => selectedAvatarName ?? "No avatar selected";
    public string FilteredCountText => $"Showing {FilteredAvatars.Count} of {AllAvatars.Count} avatars";

    public void ToggleMultiSelect(AvatarPickerItem item)
    {
        if (SelectedMultiAvatarIds.Contains(item.Id))
        {
            SelectedMultiAvatarIds.Remove(item.Id);
        }
        else
        {
            SelectedMultiAvatarIds.Add(item.Id);
        }
        RaisePropertyChanged(nameof(CanConfirm));
        RaisePropertyChanged(nameof(MultiSelectCountText));
    }

    public string MultiSelectCountText => $"{SelectedMultiAvatarIds.Count} avatar{(SelectedMultiAvatarIds.Count == 1 ? string.Empty : "s")} in pool";

    public IReadOnlyList<string> GetSelectedAvatarIds() =>
        isMultiSelectMode
            ? SelectedMultiAvatarIds.ToList()
            : (string.IsNullOrWhiteSpace(selectedAvatarId) ? [] : [selectedAvatarId]);

    private void ApplyFilter()
    {
        FilteredAvatars.Clear();
        var search = searchText.Trim().ToLowerInvariant();

        foreach (var avatar in AllAvatars)
        {
            if (string.IsNullOrWhiteSpace(search) || avatar.SearchText.Contains(search, StringComparison.OrdinalIgnoreCase))
            {
                FilteredAvatars.Add(avatar);
            }
        }

        RaisePropertyChanged(nameof(FilteredCountText));
    }

    private AvatarPickerItem CreatePickerItem(VrChatAvatarSummary summary)
    {
        var entry = avatarLibrary?.GetEntry(summary.Id);
        var customIconPath = entry?.CustomIconPath;

        var image = imageService.GetAvatarImage(summary.Id, customIconPath, vrchatThumbnailUrl: null);

        return new AvatarPickerItem(
            summary.Id,
            summary.Name,
            summary.SourceLabel,
            image);
    }
}

public sealed record AvatarPickerItem(
    string Id,
    string Name,
    string SourceLabel,
    ImageSource? Image)
{
    public string SearchText => $"{Id} {Name} {SourceLabel}";
    public string DisplayName => !string.IsNullOrWhiteSpace(Name) && !string.Equals(Name, Id, StringComparison.Ordinal)
        ? Name
        : "Unknown Avatar";
}
```

- [ ] **Step 2: Add to csproj**

```xml
    <Compile Include="ViewModels\AvatarPickerViewModel.cs" />
```

- [ ] **Step 3: Build and verify**

Run: `dotnet build "VrcTwitchOscBridge.csproj" --no-restore`
Expected: Build succeeded

- [ ] **Step 4: Commit**

```bash
git add VrcTwitchOscBridge/ViewModels/AvatarPickerViewModel.cs VrcTwitchOscBridge/VrcTwitchOscBridge.csproj
git commit -m "feat: add AvatarPickerViewModel"
```

---

### Task 4: AvatarPickerWindow (XAML + Code-Behind)

**Files:**
- Create: `VrcTwitchOscBridge/AvatarPickerWindow.xaml`
- Create: `VrcTwitchOscBridge/AvatarPickerWindow.xaml.cs`

- [ ] **Step 1: Create AvatarPickerWindow.xaml**

The XAML creates a themed window with:
- Title bar with close button (matching existing themed window pattern like AvatarRouletPickerWindow.xaml)
- Search bar with real-time filtering (x:Name="SearchTextBox")
- Grid/List view toggle buttons
- Avatar grid (WrapPanel with cards) or list (ListBox)
- Bottom bar with selected avatar name and OK/Cancel buttons

Key patterns to follow from existing windows:
- Use `WindowStyle="None"` with `WindowChrome` for custom chrome
- Use theme brushes: `{DynamicResource WindowBackgroundBrush}`, `{DynamicResource TextBrush}`, etc.
- Use `{loc:Translate '...'}` for all user-facing text
- Window size: Width=900, Height=700, MinWidth=700, MinHeight=500

Grid card template:
- Border with rounded corners, themed background
- Image (120x120) with placeholder fallback
- TextBlock for avatar name
- Button "Select" that sets SelectedItem

List item template:
- Horizontal StackPanel: Image (40x40) + Name + SourceLabel + Select button

- [ ] **Step 2: Create AvatarPickerWindow.xaml.cs**

```csharp
// VrcTwitchOscBridge/AvatarPickerWindow.xaml.cs
using System.Windows;
using System.Windows.Input;
using VrcTwitchOscBridge.Models;
using VrcTwitchOscBridge.Services;
using VrcTwitchOscBridge.ViewModels;

namespace VrcTwitchOscBridge;

public partial class AvatarPickerWindow : Window
{
    private readonly AvatarPickerViewModel viewModel;

    public AvatarPickerWindow(
        IReadOnlyList<VrChatAvatarSummary> avatars,
        AvatarImageService imageService,
        AvatarLibrary? avatarLibrary = null,
        string? currentAvatarId = null,
        IReadOnlyList<string>? multiSelectCurrentIds = null)
    {
        viewModel = new AvatarPickerViewModel(
            avatars,
            imageService,
            avatarLibrary,
            currentAvatarId,
            multiSelectCurrentIds);

        DataContext = viewModel;

        InitializeComponent();
        Loaded += OnLoaded;
    }

    private void OnLoaded(object sender, RoutedEventArgs e)
    {
        if (FindName("SearchTextBox") is System.Windows.Controls.TextBox searchBox)
        {
            searchBox.Focus();
        }
    }

    private void OnCloseButtonClicked(object sender, RoutedEventArgs e)
    {
        Close();
    }

    private void OnConfirmButtonClicked(object sender, RoutedEventArgs e)
    {
        DialogResult = true;
        Close();
    }

    private void OnCancelButtonClicked(object sender, RoutedEventArgs e)
    {
        DialogResult = false;
        Close();
    }

    private void OnTitleBarMouseDown(object sender, MouseButtonEventArgs e)
    {
        if (e.ChangedButton == MouseButton.Left)
        {
            DragMove();
        }
    }

    public IReadOnlyList<string> GetSelectedAvatarIds() => viewModel.GetSelectedAvatarIds();
}
```

- [ ] **Step 3: Add to csproj**

```xml
    <Page Include="AvatarPickerWindow.xaml" />
    <Compile Include="AvatarPickerWindow.xaml.cs" />
```

- [ ] **Step 4: Build and verify**

Run: `dotnet build "VrcTwitchOscBridge.csproj" --no-restore`
Expected: Build succeeded

- [ ] **Step 5: Commit**

```bash
git add VrcTwitchOscBridge/AvatarPickerWindow.xaml VrcTwitchOscBridge/AvatarPickerWindow.xaml.cs VrcTwitchOscBridge/VrcTwitchOscBridge.csproj
git commit -m "feat: add AvatarPickerWindow"
```

---

### Task 5: AvatarPickerService

**Files:**
- Create: `VrcTwitchOscBridge/Services/AvatarPickerService.cs`

- [ ] **Step 1: Create AvatarPickerService**

```csharp
// VrcTwitchOscBridge/Services/AvatarPickerService.cs
using System.Windows;
using VrcTwitchOscBridge.Models;

namespace VrcTwitchOscBridge.Services;

/// <summary>
/// Opens the AvatarPickerWindow and returns the selected avatar(s).
/// </summary>
public static class AvatarPickerService
{
    private static AvatarImageService? _instance;

    private static AvatarImageService Instance =>
        _instance ??= new AvatarImageService();

    /// <summary>
    /// Opens the avatar picker for single selection.
    /// </summary>
    public static AvatarPickerResult? OpenSingle(
        IReadOnlyList<VrChatAvatarSummary> avatars,
        AvatarLibrary? avatarLibrary = null,
        string? currentAvatarId = null,
        Window? owner = null)
    {
        var window = new AvatarPickerWindow(
            avatars,
            Instance,
            avatarLibrary,
            currentAvatarId);

        if (owner is not null)
        {
            window.Owner = owner;
            window.WindowStartupLocation = WindowStartupLocation.CenterOwner;
        }
        else
        {
            window.WindowStartupLocation = WindowStartupLocation.CenterScreen;
        }

        var result = window.ShowDialog();
        if (result != true)
        {
            return null;
        }

        var selectedIds = window.GetSelectedAvatarIds();
        if (selectedIds.Count == 0)
        {
            return null;
        }

        var selectedId = selectedIds[0];
        var selectedAvatar = avatars.FirstOrDefault(a => string.Equals(a.Id, selectedId, StringComparison.Ordinal));
        return new AvatarPickerResult(selectedId, selectedAvatar?.Name ?? selectedId);
    }

    /// <summary>
    /// Opens the avatar picker for multi-selection (Avatar Roulette pool).
    /// </summary>
    public static IReadOnlyList<string> OpenMulti(
        IReadOnlyList<VrChatAvatarSummary> avatars,
        AvatarLibrary? avatarLibrary = null,
        IReadOnlyList<string>? currentPool = null,
        Window? owner = null)
    {
        var window = new AvatarPickerWindow(
            avatars,
            Instance,
            avatarLibrary,
            multiSelectCurrentIds: currentPool);

        if (owner is not null)
        {
            window.Owner = owner;
            window.WindowStartupLocation = WindowStartupLocation.CenterOwner;
        }
        else
        {
            window.WindowStartupLocation = WindowStartupLocation.CenterScreen;
        }

        var result = window.ShowDialog();
        return result == true ? window.GetSelectedAvatarIds() : (currentPool ?? []);
    }

    /// <summary>
    /// Clears the image cache. Call when avatar list is refreshed.
    /// </summary>
    public static void ClearImageCache()
    {
        Instance.ClearCache();
    }
}

public sealed record AvatarPickerResult(string AvatarId, string AvatarName);
```

- [ ] **Step 2: Add to csproj**

```xml
    <Compile Include="Services\AvatarPickerService.cs" />
```

- [ ] **Step 3: Build and verify**

Run: `dotnet build "VrcTwitchOscBridge.csproj" --no-restore`
Expected: Build succeeded

- [ ] **Step 4: Commit**

```bash
git add VrcTwitchOscBridge/Services/AvatarPickerService.cs VrcTwitchOscBridge/VrcTwitchOscBridge.csproj
git commit -m "feat: add AvatarPickerService"
```

---

### Task 6: Add AvatarLibrary to AppSettings

**Files:**
- Modify: `VrcTwitchOscBridge/Models/AppSettings.cs`

- [ ] **Step 1: Add AvatarLibrary property to AppSettings**

In `AppSettings.cs`, add a field and property after the `CustomTheme` property:

```csharp
    private AvatarLibrary avatarLibrary = new();

    public AvatarLibrary AvatarLibrary
    {
        get => avatarLibrary;
        set => SetProperty(ref avatarLibrary, value ?? new AvatarLibrary());
    }
```

- [ ] **Step 2: Build and verify**

Run: `dotnet build "VrcTwitchOscBridge.csproj" --no-restore`
Expected: Build succeeded

- [ ] **Step 3: Commit**

```bash
git add VrcTwitchOscBridge/Models/AppSettings.cs
git commit -m "feat: add AvatarLibrary property to AppSettings"
```

---

### Task 7: Add OpenAvatarPickerCommand to MainWindowViewModel

**Files:**
- Modify: `VrcTwitchOscBridge/ViewModels/MainWindowViewModel.cs`

- [ ] **Step 1: Add the command and helper method**

In `MainWindowViewModel.cs`:

1. Add a new command property:
```csharp
    public RelayCommand OpenAvatarPickerCommand { get; }
```

2. In the constructor, initialize it:
```csharp
        OpenAvatarPickerCommand = new RelayCommand(OpenAvatarPicker);
```

3. Add the method:
```csharp
    private void OpenAvatarPicker(object? parameter)
    {
        var avatars = availableVrChatAvatars
            .Select(a => new VrChatAvatarSummary(a.Id, a.Name, a.SourceLabel, a.IsCurrentAvatar))
            .ToList();

        var result = AvatarPickerService.OpenSingle(
            avatars,
            Settings.AvatarLibrary,
            SelectedAvatarProfile?.AvatarId,
            this);

        if (result is not null && SelectedAvatarProfile is not null)
        {
            SelectedAvatarProfile.AvatarId = result.AvatarId;
            SelectedAvatarProfile.AvatarName = result.AvatarName;
            RefreshVrChatAvatarSelectionOptions();
        }
    }
```

- [ ] **Step 2: Build and verify**

Run: `dotnet build "VrcTwitchOscBridge.csproj" --no-restore`
Expected: Build succeeded

- [ ] **Step 3: Commit**

```bash
git add VrcTwitchOscBridge/ViewModels/MainWindowViewModel.cs
git commit -m "feat: add OpenAvatarPickerCommand to MainWindowViewModel"
```

---

### Task 8: Replace First Dropdown (Proof of Concept)

**Files:**
- Modify: `VrcTwitchOscBridge/MainWindow.xaml`

- [ ] **Step 1: Replace the Avatar Set Setup return avatar ComboBox**

Find the ComboBox at line ~5287 (ProfileAvatarOptions in Avatar Set Setup):

```xml
<ComboBox ItemsSource="{Binding DataContext.ProfileAvatarOptions, RelativeSource={RelativeSource AncestorType=Window}}" ... />
```

Replace with:

```xml
<DockPanel>
    <Button DockPanel.Dock="Right"
            Content="{loc:Translate 'Browse...'}"
            Style="{StaticResource SecondaryButtonStyle}"
            Command="{Binding DataContext.OpenAvatarPickerCommand, RelativeSource={RelativeSource AncestorType=Window}}"
            Margin="0,8,0,0" />
    <TextBlock Text="{Binding AvatarDisplayName}"
               Foreground="{DynamicResource TextBrush}"
               VerticalAlignment="Center"
               Margin="0,8,8,0"
               TextWrapping="Wrap" />
</DockPanel>
```

- [ ] **Step 2: Build and verify**

Run: `dotnet build "VrcTwitchOscBridge.csproj" --no-restore`
Expected: Build succeeded

- [ ] **Step 3: Commit**

```bash
git add VrcTwitchOscBridge/MainWindow.xaml
git commit -m "feat: replace first avatar ComboBox with Browse button (proof of concept)"
```

---

### Task 9: Add Localization Keys

**Files:**
- Modify: `VrcTwitchOscBridge/Resources/Localization/en-US.extra.json`

- [ ] **Step 1: Add localization keys**

Add to `en-US.extra.json`:

```json
  "Browse...": "Browse...",
  "Avatar Picker": "Avatar Picker",
  "Search avatars...": "Search avatars...",
  "Grid": "Grid",
  "List": "List",
  "Select": "Select",
  "Selected:": "Selected:",
  "No avatar selected": "No avatar selected",
  "Unknown Avatar": "Unknown Avatar",
  "Showing {0} of {1} avatars": "Showing {0} of {1} avatars",
  "avatar in pool": "avatar in pool",
  "avatars in pool": "avatars in pool"
```

- [ ] **Step 2: Build and verify**

Run: `dotnet build "VrcTwitchOscBridge.csproj" --no-restore`
Expected: Build succeeded

- [ ] **Step 3: Commit**

```bash
git add VrcTwitchOscBridge/Resources/Localization/en-US.extra.json
git commit -m "feat: add localization keys for avatar picker"
```

---

### Task 10: Replace Remaining Dropdowns

**Files:**
- Modify: `VrcTwitchOscBridge/MainWindow.xaml`
- Modify: `VrcTwitchOscBridge/ViewModels/MainWindowViewModel.cs`

- [ ] **Step 1: Replace all remaining avatar ComboBoxes**

Replace the following ComboBoxes with the Browse button pattern from Task 8:

1. **Avatar Change Rule — Target avatar** (line ~9762): `VrChatAvatarOptions` bound to `AvatarChangeTargetId`
2. **Avatar Change Rule — Reset avatar**: Add a second Browse button for `AvatarChangeResetId`
3. **Power-up Rule — Avatar scope** (line ~5063): `VrChatAvatarOptions` bound to `AvatarId`
4. **Supporter Rule — Avatar scope** (line ~8607): `VrChatAvatarOptions` bound to `SupporterAvatarId`

Each replacement needs a tailored command that knows which property to update. Create specific commands in MainWindowViewModel:

```csharp
    public RelayCommand OpenAvatarPickerForAvatarChangeTargetCommand { get; }
    public RelayCommand OpenAvatarPickerForAvatarChangeResetCommand { get; }
    public RelayCommand OpenAvatarPickerForPowerUpCommand { get; }
    public RelayCommand OpenAvatarPickerForSupporterCommand { get; }
```

Each command follows the same pattern as `OpenAvatarPicker` but targets the correct property on the SelectedRule or relevant object.

- [ ] **Step 2: Build and verify**

Run: `dotnet build "VrcTwitchOscBridge.csproj" --no-restore`
Expected: Build succeeded

- [ ] **Step 3: Commit**

```bash
git add VrcTwitchOscBridge/MainWindow.xaml VrcTwitchOscBridge/ViewModels/MainWindowViewModel.cs
git commit -m "feat: replace all avatar ComboBoxes with Browse buttons"
```

---

### Task 11: Final Verification

- [ ] **Step 1: Full build**

Run: `dotnet build "VrcTwitchOscBridge.csproj" --no-restore`
Expected: Build succeeded, 0 warnings, 0 errors

- [ ] **Step 2: Verify csproj has all new files**

Check that these entries exist in `VrcTwitchOscBridge.csproj`:
- `<Compile Include="Models\AvatarLibrary.cs" />`
- `<Compile Include="Models\AvatarPickerViewMode.cs" />`
- `<Compile Include="Services\AvatarImageService.cs" />`
- `<Compile Include="Services\AvatarPickerService.cs" />`
- `<Compile Include="ViewModels\AvatarPickerViewModel.cs" />`
- `<Page Include="AvatarPickerWindow.xaml" />`
- `<Compile Include="AvatarPickerWindow.xaml.cs" />`

- [ ] **Step 3: Commit final state**

```bash
git status
git add -A
git commit -m "feat: complete Phase 1 of avatar picker redesign"
```

---

## Phase 2: Groups, Tags & Custom Icons (Future)

- AvatarGroup/AvatarTag management sub-window
- Group/tag filter dropdowns in picker
- Custom icon file picker per avatar card
- Search extended to groups/tags
- VRChat API thumbnail URL integration

## Phase 3: Roulette Multi-Select & Polish (Future)

- Multi-select mode with checkboxes
- "X avatars in pool" bottom bar
- Keyboard navigation
- Virtualization for large libraries
- Full replacement of all remaining dropdowns
