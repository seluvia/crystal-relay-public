# Avatar Library Manager — Phase 2 Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Add group/tag management, custom icon picker, and group/tag filtering to the Avatar Picker.

**Architecture:** A new `AvatarLibraryManagerWindow` (modeless, opened from the picker) manages groups, tags, and per-avatar custom icons. The picker gets filter ComboBoxes and extended search. All data flows through the shared `Settings.AvatarLibrary` instance.

**Tech Stack:** C#, WPF, XAML, ThemeManager, ObservableObject, OpenFileDialog

---

### Task 1: AvatarLibraryManagerViewModel

**Files:**
- Create: `VrcTwitchOscBridge/ViewModels/AvatarLibraryManagerViewModel.cs`

- [ ] **Step 1: Create the ViewModel**

```csharp
using System.Collections.ObjectModel;
using System.Text.RegularExpressions;
using System.Windows;
using Microsoft.Win32;
using VrcTwitchOscBridge.Infrastructure;
using VrcTwitchOscBridge.Models;
using VrcTwitchOscBridge.Services;

namespace VrcTwitchOscBridge.ViewModels;

public sealed class AvatarLibraryManagerViewModel : ObservableObject
{
    private readonly AvatarLibrary library;
    private readonly AvatarImageService imageService;
    private readonly AvatarImageService imageService;
    private AvatarLibraryEntry? selectedEntry;
    private AvatarGroup? selectedGroup;
    private AvatarTag? selectedTag;

    public AvatarLibraryManagerViewModel(AvatarLibrary library, AvatarImageService imageService)
    {
        this.library = library;
        this.imageService = imageService;
    }

    public ObservableCollection<AvatarLibraryEntry> Entries => library.Entries;
    public ObservableCollection<AvatarGroup> Groups => library.Groups;
    public ObservableCollection<AvatarTag> Tags => library.Tags;

    public AvatarLibraryEntry? SelectedEntry
    {
        get => selectedEntry;
        set => SetProperty(ref selectedEntry, value);
    }

    public AvatarGroup? SelectedGroup
    {
        get => selectedGroup;
        set => SetProperty(ref selectedGroup, value);
    }

    public AvatarTag? SelectedTag
    {
        get => selectedTag;
        set => SetProperty(ref selectedTag, value);
    }

    public void AddGroup()
    {
        var group = new AvatarGroup
        {
            Name = $"Group {Groups.Count + 1}",
            SortOrder = Groups.Count
        };
        Groups.Add(group);
        SelectedGroup = group;
    }

    public void DeleteGroup()
    {
        if (SelectedGroup is null) return;
        var id = SelectedGroup.Id;
        Groups.Remove(SelectedGroup);
        foreach (var entry in Entries)
        {
            entry.GroupIds.Remove(id);
        }
        SelectedGroup = Groups.FirstOrDefault();
    }

    public void AddTag()
    {
        var tag = new AvatarTag
        {
            Name = $"Tag {Tags.Count + 1}",
            ColorHex = "#A855F7"
        };
        Tags.Add(tag);
        SelectedTag = tag;
    }

    public void DeleteTag()
    {
        if (SelectedTag is null) return;
        var id = SelectedTag.Id;
        Tags.Remove(SelectedTag);
        foreach (var entry in Entries)
        {
            entry.TagIds.Remove(id);
        }
        SelectedTag = Tags.FirstOrDefault();
    }

    private static readonly Regex HexColorRegex = new(@"^#[0-9A-Fa-f]{6}$", RegexOptions.Compiled);

    public bool IsValidHexColor(string hex) => HexColorRegex.IsMatch(hex);

    public void SetCustomIconForEntry(AvatarLibraryEntry entry, Window owner)
    {
        var dialog = new OpenFileDialog
        {
            Filter = "Image Files|*.png;*.jpg;*.jpeg;*.gif;*.bmp;*.webp",
            Title = "Choose Avatar Icon"
        };

        if (dialog.ShowDialog(owner) != true) return;

        var relativePath = imageService.SaveCustomIcon(entry.AvatarId, dialog.FileName);
        if (!string.IsNullOrWhiteSpace(relativePath))
        {
            entry.CustomIconPath = relativePath;
        }
    }

    public void ClearCustomIconForEntry(AvatarLibraryEntry entry)
    {
        entry.CustomIconPath = string.Empty;
    }
}
```

- [ ] **Step 2: Build to verify**

Run: `dotnet build "VrcTwitchOscBridge.csproj" --no-restore`
Expected: Build succeeded

- [ ] **Step 3: Commit**

```bash
git add VrcTwitchOscBridge/ViewModels/AvatarLibraryManagerViewModel.cs
git commit -m "feat: add AvatarLibraryManagerViewModel"
```

---

### Task 2: AvatarLibraryManagerWindow

**Files:**
- Create: `VrcTwitchOscBridge/AvatarLibraryManagerWindow.xaml`
- Create: `VrcTwitchOscBridge/AvatarLibraryManagerWindow.xaml.cs`

- [ ] **Step 1: Create the code-behind**

```csharp
using System.Windows;
using VrcTwitchOscBridge.Models;
using VrcTwitchOscBridge.Services;
using VrcTwitchOscBridge.ViewModels;

namespace VrcTwitchOscBridge;

public partial class AvatarLibraryManagerWindow : Window
{
    private readonly AvatarLibraryManagerViewModel viewModel;

    public AvatarLibraryManagerWindow(
        AppTheme theme,
        AvatarLibrary library,
        AvatarImageService imageService)
    {
        viewModel = new AvatarLibraryManagerViewModel(library, imageService);
        DataContext = viewModel;

        InitializeComponent();
        ThemeManager.ApplyToResources(Resources, theme);
        ThemeManager.ThemeChanged += OnThemeManagerThemeChanged;
        Closed += OnWindowClosed;
    }

    private void OnThemeManagerThemeChanged(object? sender, EventArgs e)
    {
        Dispatcher.BeginInvoke(() => ThemeManager.ApplyToResources(Resources));
    }

    private void OnWindowClosed(object? sender, EventArgs e)
    {
        ThemeManager.ThemeChanged -= OnThemeManagerThemeChanged;
        Closed -= OnWindowClosed;
    }

    private void OnCloseButtonClicked(object sender, RoutedEventArgs e)
    {
        Close();
    }

    private void OnTitleBarMouseDown(object sender, System.Windows.Input.MouseButtonEventArgs e)
    {
        if (e.ChangedButton == System.Windows.Input.MouseButton.Left)
        {
            try { DragMove(); } catch { }
        }
    }
}
```

- [ ] **Step 2: Create the XAML**

```xml
<Window x:Class="VrcTwitchOscBridge.AvatarLibraryManagerWindow"
        xmlns="http://schemas.microsoft.com/winfx/2006/xaml/presentation"
        xmlns:x="http://schemas.microsoft.com/winfx/2006/xaml"
        xmlns:loc="clr-namespace:VrcTwitchOscBridge.Services"
        xmlns:shell="clr-namespace:System.Windows.Shell;assembly=PresentationFramework"
        xmlns:vm="clr-namespace:VrcTwitchOscBridge.ViewModels"
        Title="{loc:Translate 'Manage Avatar Library | Crystal Relay'}"
        Icon="Assets/crystal-relay-icon.ico"
        Width="500"
        Height="450"
        MinWidth="400"
        MinHeight="350"
        WindowStyle="None"
        WindowStartupLocation="CenterOwner"
        FontFamily="{DynamicResource BodyFontFamily}"
        UseLayoutRounding="True"
        SnapsToDevicePixels="True"
        Background="{DynamicResource WindowBackgroundBrush}">
    <shell:WindowChrome.WindowChrome>
        <shell:WindowChrome CaptionHeight="0" CornerRadius="0" GlassFrameThickness="0" ResizeBorderThickness="0" UseAeroCaptionButtons="False" />
    </shell:WindowChrome.WindowChrome>
    <Window.Resources>
        <FontFamily x:Key="BodyFontFamily">Verdana</FontFamily>
        <FontFamily x:Key="HeadingFontFamily">Constantia</FontFamily>
        <SolidColorBrush x:Key="WindowBackgroundBrush" Color="#130B1E" />
        <SolidColorBrush x:Key="PanelBrush" Color="#CC1C132B" />
        <SolidColorBrush x:Key="BorderBrush" Color="#4B2B78" />
        <SolidColorBrush x:Key="AccentBrush" Color="#A855F7" />
        <SolidColorBrush x:Key="TextBrush" Color="#F5EEFF" />
        <SolidColorBrush x:Key="MutedBrush" Color="#C9B8E3" />
        <SolidColorBrush x:Key="InputBrush" Color="#B8271A3D" />
        <SolidColorBrush x:Key="InputBorderBrush" Color="#5B3A8E" />
        <SolidColorBrush x:Key="TitleBarBrush" Color="#20122F" />
        <SolidColorBrush x:Key="TitleBarTextBrush" Color="#F5EEFF" />
        <SolidColorBrush x:Key="TitleBarCloseHoverBrush" Color="#B43D62" />
        <SolidColorBrush x:Key="TitleBarClosePressedBrush" Color="#8C2648" />
        <SolidColorBrush x:Key="SecondaryButtonBrush" Color="#2C1C48" />
        <SolidColorBrush x:Key="SecondaryButtonBorderBrush" Color="#6942A7" />

        <Style x:Key="TitleBarCloseButtonStyle" TargetType="Button">
            <Setter Property="Width" Value="46" />
            <Setter Property="Height" Value="48" />
            <Setter Property="Background" Value="Transparent" />
            <Setter Property="BorderThickness" Value="0" />
            <Setter Property="Template">
                <Setter.Value>
                    <ControlTemplate TargetType="Button">
                        <Border Background="{TemplateBinding Background}">
                            <ContentPresenter HorizontalAlignment="Center" VerticalAlignment="Center" />
                        </Border>
                        <ControlTemplate.Triggers>
                            <Trigger Property="IsMouseOver" Value="True">
                                <Setter Property="Background" Value="{DynamicResource TitleBarCloseHoverBrush}" />
                            </Trigger>
                            <Trigger Property="IsPressed" Value="True">
                                <Setter Property="Background" Value="{DynamicResource TitleBarClosePressedBrush}" />
                            </Trigger>
                        </ControlTemplate.Triggers>
                    </ControlTemplate>
                </Setter.Value>
            </Setter>
        </Style>

        <Style x:Key="SecondaryButtonStyle" TargetType="Button">
            <Setter Property="Padding" Value="12,6" />
            <Setter Property="Background" Value="{DynamicResource SecondaryButtonBrush}" />
            <Setter Property="BorderBrush" Value="{DynamicResource SecondaryButtonBorderBrush}" />
            <Setter Property="BorderThickness" Value="1" />
            <Setter Property="Foreground" Value="{DynamicResource TextBrush}" />
            <Setter Property="Cursor" Value="Hand" />
            <Setter Property="Template">
                <Setter.Value>
                    <ControlTemplate TargetType="Button">
                        <Border Background="{TemplateBinding Background}" BorderBrush="{TemplateBinding BorderBrush}" BorderThickness="{TemplateBinding BorderThickness}" CornerRadius="8">
                            <ContentPresenter HorizontalAlignment="Center" VerticalAlignment="Center" Margin="{TemplateBinding Padding}" />
                        </Border>
                    </ControlTemplate>
                </Setter.Value>
            </Setter>
        </Style>

        <Style x:Key="ListBoxItemStyle" TargetType="ListBoxItem">
            <Setter Property="Padding" Value="8,6" />
            <Setter Property="Background" Value="Transparent" />
            <Setter Property="BorderThickness" Value="0" />
            <Setter Property="Template">
                <Setter.Value>
                    <ControlTemplate TargetType="ListBoxItem">
                        <Border Background="{TemplateBinding Background}" Padding="{TemplateBinding Padding}" CornerRadius="6">
                            <ContentPresenter />
                        </Border>
                        <ControlTemplate.Triggers>
                            <Trigger Property="IsSelected" Value="True">
                                <Setter Property="Background" Value="{DynamicResource AccentBrush}" />
                            </Trigger>
                            <Trigger Property="IsMouseOver" Value="True">
                                <Setter Property="Background" Value="{DynamicResource InputBrush}" />
                            </Trigger>
                        </ControlTemplate.Triggers>
                    </ControlTemplate>
                </Setter.Value>
            </Setter>
        </Style>
    </Window.Resources>

    <Border BorderBrush="{DynamicResource BorderBrush}" BorderThickness="1">
        <Grid>
            <Grid.RowDefinitions>
                <RowDefinition Height="48" />
                <RowDefinition Height="*" />
            </Grid.RowDefinitions>

            <!-- Title Bar -->
            <Border Grid.Row="0" Background="{DynamicResource TitleBarBrush}" BorderBrush="{DynamicResource BorderBrush}" BorderThickness="0,0,0,1" MouseLeftButtonDown="OnTitleBarMouseDown">
                <Grid Margin="12,0,8,0">
                    <TextBlock Text="{loc:Translate 'Manage Avatar Library'}" FontFamily="{DynamicResource HeadingFontFamily}" FontSize="14" FontWeight="Bold" Foreground="{DynamicResource TitleBarTextBrush}" VerticalAlignment="Center" />
                    <Button HorizontalAlignment="Right" Style="{StaticResource TitleBarCloseButtonStyle}" Click="OnCloseButtonClicked">
                        <TextBlock Text="&#x2715;" FontSize="13" FontWeight="SemiBold" Foreground="{DynamicResource TitleBarTextBrush}" />
                    </Button>
                </Grid>
            </Border>

            <!-- Main Content -->
            <Grid Grid.Row="1" Margin="16">
                <Border Background="{DynamicResource PanelBrush}" BorderBrush="{DynamicResource BorderBrush}" BorderThickness="1" CornerRadius="16" Padding="16">
                    <TabControl Background="Transparent" BorderThickness="0">
                        <!-- Groups Tab -->
                        <TabItem Header="{loc:Translate 'Groups'}">
                            <Grid>
                                <Grid.RowDefinitions>
                                    <RowDefinition Height="*" />
                                    <RowDefinition Height="Auto" />
                                </Grid.RowDefinitions>
                                <ListBox Grid.Row="0" ItemsSource="{Binding Groups}" SelectedItem="{Binding SelectedGroup}" Style="{x:Null}" ItemContainerStyle="{StaticResource ListBoxItemStyle}" Background="Transparent" BorderThickness="0">
                                    <ListBox.ItemTemplate>
                                        <DataTemplate>
                                            <Grid>
                                                <Grid.ColumnDefinitions>
                                                    <ColumnDefinition Width="*" />
                                                    <ColumnDefinition Width="60" />
                                                    <ColumnDefinition Width="Auto" />
                                                </Grid.ColumnDefinitions>
                                                <TextBox Grid.Column="0" Text="{Binding Name, UpdateSourceTrigger=LostFocus}" Background="Transparent" BorderThickness="0" Foreground="{DynamicResource TextBrush}" Margin="0,0,8,0" />
                                                <TextBox Grid.Column="1" Text="{Binding SortOrder}" Background="{DynamicResource InputBrush}" BorderBrush="{DynamicResource InputBorderBrush}" BorderThickness="1" Foreground="{DynamicResource TextBrush}" TextAlignment="Center" Padding="2" />
                                                <CheckBox Grid.Column="2" IsChecked="{Binding IsCollapsed}" Content="{loc:Translate 'Hide'}" Foreground="{DynamicResource MutedBrush}" Margin="8,0,0,0" />
                                            </Grid>
                                        </DataTemplate>
                                    </ListBox.ItemTemplate>
                                </ListBox>
                                <StackPanel Grid.Row="1" Orientation="Horizontal" Margin="0,12,0,0">
                                    <Button Content="{loc:Translate 'Add Group'}" Style="{StaticResource SecondaryButtonStyle}" Command="{Binding AddGroupCommand}" />
                                    <Button Content="{loc:Translate 'Delete Group'}" Style="{StaticResource SecondaryButtonStyle}" Margin="8,0,0,0" Command="{Binding DeleteGroupCommand}" />
                                </StackPanel>
                            </Grid>
                        </TabItem>

                        <!-- Tags Tab -->
                        <TabItem Header="{loc:Translate 'Tags'}">
                            <Grid>
                                <Grid.RowDefinitions>
                                    <RowDefinition Height="*" />
                                    <RowDefinition Height="Auto" />
                                </Grid.RowDefinitions>
                                <ListBox Grid.Row="0" ItemsSource="{Binding Tags}" SelectedItem="{Binding SelectedTag}" Style="{x:Null}" ItemContainerStyle="{StaticResource ListBoxItemStyle}" Background="Transparent" BorderThickness="0">
                                    <ListBox.ItemTemplate>
                                        <DataTemplate>
                                            <Grid>
                                                <Grid.ColumnDefinitions>
                                                    <ColumnDefinition Width="*" />
                                                    <ColumnDefinition Width="70" />
                                                    <ColumnDefinition Width="30" />
                                                </Grid.ColumnDefinitions>
                                                <TextBox Grid.Column="0" Text="{Binding Name, UpdateSourceTrigger=LostFocus}" Background="Transparent" BorderThickness="0" Foreground="{DynamicResource TextBrush}" Margin="0,0,8,0" />
                                                <TextBox Grid.Column="1" Text="{Binding ColorHex, UpdateSourceTrigger=PropertyChanged}" Background="{DynamicResource InputBrush}" BorderBrush="{DynamicResource InputBorderBrush}" BorderThickness="1" Foreground="{DynamicResource TextBrush}" TextAlignment="Center" Padding="2" />
                                                <Border Grid.Column="2" Width="20" Height="20" CornerRadius="4" BorderBrush="{DynamicResource BorderBrush}" BorderThickness="1">
                                                    <Border.Background>
                                                        <SolidColorBrush Color="{Binding ColorHex, TargetNullValue=#A855F7, FallbackValue=#A855F7}" />
                                                    </Border.Background>
                                                </Border>
                                            </Grid>
                                        </DataTemplate>
                                    </ListBox.ItemTemplate>
                                </ListBox>
                                <StackPanel Grid.Row="1" Orientation="Horizontal" Margin="0,12,0,0">
                                    <Button Content="{loc:Translate 'Add Tag'}" Style="{StaticResource SecondaryButtonStyle}" Command="{Binding AddTagCommand}" />
                                    <Button Content="{loc:Translate 'Delete Tag'}" Style="{StaticResource SecondaryButtonStyle}" Margin="8,0,0,0" Command="{Binding DeleteTagCommand}" />
                                </StackPanel>
                            </Grid>
                        </TabItem>
                    </TabControl>
                </Border>
            </Grid>
        </Grid>
    </Border>
</Window>
```

- [ ] **Step 3: Add RelayCommand properties to the ViewModel**

Add to `AvatarLibraryManagerViewModel.cs` after the constructor:

```csharp
    public RelayCommand AddGroupCommand { get; }
    public RelayCommand DeleteGroupCommand { get; }
    public RelayCommand AddTagCommand { get; }
    public RelayCommand DeleteTagCommand { get; }

    // In constructor, after this.imageService = imageService;
    AddGroupCommand = new RelayCommand(AddGroup);
    DeleteGroupCommand = new RelayCommand(DeleteGroup, () => SelectedGroup is not null);
    AddTagCommand = new RelayCommand(AddTag);
    DeleteTagCommand = new RelayCommand(DeleteTag, () => SelectedTag is not null);

    // After SelectedGroup setter, add:
    // In the setter, after SetProperty:
    // DeleteGroupCommand.NotifyCanExecuteChanged();

    // After SelectedTag setter, add:
    // DeleteTagCommand.NotifyCanExecuteChanged();
```

Update the `SelectedGroup` setter:
```csharp
    public AvatarGroup? SelectedGroup
    {
        get => selectedGroup;
        set
        {
            if (SetProperty(ref selectedGroup, value))
            {
                DeleteGroupCommand.NotifyCanExecuteChanged();
            }
        }
    }
```

Update the `SelectedTag` setter:
```csharp
    public AvatarTag? SelectedTag
    {
        get => selectedTag;
        set
        {
            if (SetProperty(ref selectedTag, value))
            {
                DeleteTagCommand.NotifyCanExecuteChanged();
            }
        }
    }
```

- [ ] **Step 4: Add new files to VrcTwitchOscBridge.csproj**

Add after existing `<Page Include="AvatarPickerWindow.xaml" />`:
```xml
    <Page Include="AvatarLibraryManagerWindow.xaml" />
```

Add after existing `<Compile Include="AvatarPickerWindow.xaml.cs" />`:
```xml
    <Compile Include="AvatarLibraryManagerWindow.xaml.cs" />
```

Add after existing `<Compile Include="ViewModels\AvatarPickerViewModel.cs" />`:
```xml
    <Compile Include="ViewModels\AvatarLibraryManagerViewModel.cs" />
```

- [ ] **Step 5: Build and verify**

Run: `dotnet build "VrcTwitchOscBridge.csproj" --no-restore`
Expected: Build succeeded

- [ ] **Step 6: Commit**

```bash
git add VrcTwitchOscBridge/AvatarLibraryManagerWindow.xaml VrcTwitchOscBridge/AvatarLibraryManagerWindow.xaml.cs VrcTwitchOscBridge/ViewModels/AvatarLibraryManagerViewModel.cs VrcTwitchOscBridge/VrcTwitchOscBridge.csproj
git commit -m "feat: add AvatarLibraryManagerWindow with Groups and Tags tabs"
```

---

### Task 3: Picker Changes — Manage Button, Filters, Extended Search

**Files:**
- Modify: `VrcTwitchOscBridge/AvatarPickerWindow.xaml`
- Modify: `VrcTwitchOscBridge/AvatarPickerWindow.xaml.cs`
- Modify: `VrcTwitchOscBridge/ViewModels/AvatarPickerViewModel.cs`

- [ ] **Step 1: Store imageService as a field in code-behind**

In `AvatarPickerWindow.xaml.cs`, change the constructor parameter `imageService` to `imageSvc` and add a field:

```csharp
public partial class AvatarPickerWindow : Window
{
    private readonly AvatarPickerViewModel viewModel;
    private readonly AvatarImageService imageService;

    public AvatarPickerWindow(
        AppTheme theme,
        IReadOnlyList<VrChatAvatarSummary> avatars,
        AvatarImageService imageSvc,
        AvatarLibrary? avatarLibrary = null,
        string? currentAvatarId = null,
        IReadOnlyList<string>? multiSelectCurrentIds = null)
    {
        this.imageService = imageSvc;
        viewModel = new AvatarPickerViewModel(
            avatars,
            imageSvc,
            avatarLibrary,
            currentAvatarId,
            multiSelectCurrentIds);
```

- [ ] **Step 2: Add Manage button to picker title bar**

In `AvatarPickerWindow.xaml`, find the title bar Grid (inside the Border with `MouseLeftButtonDown="OnTitleBarMouseDown"`). The current Grid has:
```xml
<Grid Margin="12,0,8,0">
    <Grid.ColumnDefinitions>
        <ColumnDefinition Width="*" />
        <ColumnDefinition Width="Auto" />
    </Grid.ColumnDefinitions>
    ...
    <Button Grid.Column="1" ...>
```

Change the column definitions to add a middle column for the Manage button:
```xml
<Grid Margin="12,0,8,0">
    <Grid.ColumnDefinitions>
        <ColumnDefinition Width="*" />
        <ColumnDefinition Width="Auto" />
        <ColumnDefinition Width="Auto" />
    </Grid.ColumnDefinitions>
```

Add the Manage button between the title DockPanel and the close button:
```xml
                    <Button Grid.Column="1"
                            Width="46"
                            Height="48"
                            Background="Transparent"
                            BorderThickness="0"
                            Click="OnManageButtonClicked">
                        <TextBlock Text="&#x2699;" FontSize="14" Foreground="{DynamicResource TitleBarTextBrush}" />
                        <Button.Style>
                            <Style TargetType="Button">
                                <Setter Property="Template">
                                    <Setter.Value>
                                        <ControlTemplate TargetType="Button">
                                            <Border Background="{TemplateBinding Background}">
                                                <ContentPresenter HorizontalAlignment="Center" VerticalAlignment="Center" />
                                            </Border>
                                            <ControlTemplate.Triggers>
                                                <Trigger Property="IsMouseOver" Value="True">
                                                    <Setter Property="Background" Value="{DynamicResource TitleBarCloseHoverBrush}" />
                                                </Trigger>
                                            </ControlTemplate.Triggers>
                                        </ControlTemplate>
                                    </Setter.Value>
                                </Setter>
                            </Style>
                        </Button.Style>
                    </Button>
```

Change the close button from `Grid.Column="1"` to `Grid.Column="2"`.

- [ ] **Step 3: Add Manage button handler in code-behind**

In `AvatarPickerWindow.xaml.cs`, add:

```csharp
    private AvatarLibraryManagerWindow? managerWindow;

    private void OnManageButtonClicked(object sender, RoutedEventArgs e)
    {
        if (managerWindow is not null)
        {
            managerWindow.Activate();
            return;
        }

        managerWindow = new AvatarLibraryManagerWindow(
            ThemeManager.CurrentTheme,
            viewModel.Library,
            imageService);
        managerWindow.Owner = this;
        managerWindow.Closed += OnManagerWindowClosed;
        managerWindow.Show();
    }

    private void OnManagerWindowClosed(object? sender, EventArgs e)
    {
        if (managerWindow is not null)
        {
            managerWindow.Closed -= OnManagerWindowClosed;
            managerWindow = null;
        }
    }
```

- [ ] **Step 4: Add filter properties to AvatarPickerViewModel**

In `AvatarPickerViewModel.cs`, add after the `viewMode` field:

```csharp
    private string? selectedFilterGroupId;
    private string? selectedFilterTagId;
```

Add properties:
```csharp
    public string? SelectedFilterGroupId
    {
        get => selectedFilterGroupId;
        set
        {
            if (SetProperty(ref selectedFilterGroupId, value))
            {
                ApplyFilter();
            }
        }
    }

    public string? SelectedFilterTagId
    {
        get => selectedFilterTagId;
        set
        {
            if (SetProperty(ref selectedFilterTagId, value))
            {
                ApplyFilter();
            }
        }
    }

    public AvatarLibrary? Library => avatarLibrary;

    public void RefreshFilter() => ApplyFilter();
```

- [ ] **Step 5: Extend ApplyFilter to support group/tag filters and extended search**

Replace the `ApplyFilter` method:

```csharp
    private void ApplyFilter()
    {
        FilteredAvatars.Clear();
        var search = searchText.Trim().ToLowerInvariant();

        foreach (var avatar in AllAvatars)
        {
            if (!string.IsNullOrWhiteSpace(search))
            {
                var entry = avatarLibrary?.GetEntry(avatar.Id);
                var groupNames = entry?.GroupIds
                    .Select(id => avatarLibrary?.Groups.FirstOrDefault(g => g.Id == id)?.Name)
                    .Where(n => n is not null)
                    .Select(n => n!.ToLowerInvariant())
                    .ToList() ?? [];
                var tagNames = entry?.TagIds
                    .Select(id => avatarLibrary?.Tags.FirstOrDefault(t => t.Id == id)?.Name)
                    .Where(n => n is not null)
                    .Select(n => n!.ToLowerInvariant())
                    .ToList() ?? [];

                var matchesSearch = avatar.SearchText.Contains(search, StringComparison.OrdinalIgnoreCase)
                    || groupNames.Any(n => n.Contains(search, StringComparison.OrdinalIgnoreCase))
                    || tagNames.Any(n => n.Contains(search, StringComparison.OrdinalIgnoreCase));

                if (!matchesSearch) continue;
            }

            if (!string.IsNullOrWhiteSpace(selectedFilterGroupId))
            {
                var entry = avatarLibrary?.GetEntry(avatar.Id);
                if (entry?.GroupIds.Contains(selectedFilterGroupId) != true) continue;
            }

            if (!string.IsNullOrWhiteSpace(selectedFilterTagId))
            {
                var entry = avatarLibrary?.GetEntry(avatar.Id);
                if (entry?.TagIds.Contains(selectedFilterTagId) != true) continue;
            }

            FilteredAvatars.Add(avatar);
        }

        RaisePropertyChanged(nameof(FilteredCountText));
    }
```

- [ ] **Step 6: Add filter ComboBoxes to picker XAML**

In `AvatarPickerWindow.xaml`, find the "Search and View Toggle Bar" Grid (Grid.Row="1" inside the main content Grid). After the closing `</Grid>` of the search bar, add a new filter bar:

```xml
                        <!-- Filter Bar -->
                        <StackPanel Grid.Row="2" Orientation="Horizontal" Margin="0,8,0,0">
                            <TextBlock Text="{loc:Translate 'Group:'}" Foreground="{DynamicResource MutedBrush}" VerticalAlignment="Center" Margin="0,0,4,0" />
                            <ComboBox Width="150"
                                      ItemsSource="{Binding Library.Groups}"
                                      SelectedValue="{Binding SelectedFilterGroupId, UpdateSourceTrigger=PropertyChanged}"
                                      SelectedValuePath="Id"
                                      DisplayMemberPath="Name"
                                      Margin="0,0,16,0">
                                <ComboBox.Style>
                                    <Style TargetType="ComboBox" BasedOn="{StaticResource {x:Type ComboBox}}">
                                        <Setter Property="Background" Value="{DynamicResource InputBrush}" />
                                        <Setter Property="BorderBrush" Value="{DynamicResource InputBorderBrush}" />
                                        <Setter Property="Foreground" Value="{DynamicResource TextBrush}" />
                                    </Style>
                                </ComboBox.Style>
                            </ComboBox>

                            <TextBlock Text="{loc:Translate 'Tag:'}" Foreground="{DynamicResource MutedBrush}" VerticalAlignment="Center" Margin="0,0,4,0" />
                            <ComboBox Width="150"
                                      ItemsSource="{Binding Library.Tags}"
                                      SelectedValue="{Binding SelectedFilterTagId, UpdateSourceTrigger=PropertyChanged}"
                                      SelectedValuePath="Id"
                                      DisplayMemberPath="Name">
                                <ComboBox.Style>
                                    <Style TargetType="ComboBox" BasedOn="{StaticResource {x:Type ComboBox}}">
                                        <Setter Property="Background" Value="{DynamicResource InputBrush}" />
                                        <Setter Property="BorderBrush" Value="{DynamicResource InputBorderBrush}" />
                                        <Setter Property="Foreground" Value="{DynamicResource TextBrush}" />
                                    </Style>
                                </ComboBox.Style>
                            </ComboBox>
                        </StackPanel>
```

Note: This goes in the Grid that currently has the avatar display area. The Grid.RowDefinitions need to shift. Change the main content Grid row definitions from:
```xml
<Grid.RowDefinitions>
    <RowDefinition Height="Auto" />
    <RowDefinition Height="Auto" />
    <RowDefinition Height="*" />
    <RowDefinition Height="Auto" />
</Grid.RowDefinitions>
```
to:
```xml
<Grid.RowDefinitions>
    <RowDefinition Height="Auto" />
    <RowDefinition Height="Auto" />
    <RowDefinition Height="Auto" />
    <RowDefinition Height="*" />
    <RowDefinition Height="Auto" />
</Grid.RowDefinitions>
```

And update the row assignments:
- Header (Choose an Avatar): stays `Grid.Row="0"`
- Search bar: stays `Grid.Row="1"`
- Filter bar: new `Grid.Row="2"`
- Avatar display (ScrollViewer): change from `Grid.Row="2"` to `Grid.Row="3"`
- Bottom bar: change from `Grid.Row="3"` to `Grid.Row="4"`

- [ ] **Step 7: Build and verify**

Run: `dotnet build "VrcTwitchOscBridge.csproj" --no-restore`
Expected: Build succeeded

- [ ] **Step 8: Commit**

```bash
git add VrcTwitchOscBridge/AvatarPickerWindow.xaml VrcTwitchOscBridge/AvatarPickerWindow.xaml.cs VrcTwitchOscBridge/ViewModels/AvatarPickerViewModel.cs
git commit -m "feat: add Manage button, group/tag filters, and extended search to AvatarPickerWindow"
```

---

### Task 4: Context Menu for Custom Icons

**Files:**
- Modify: `VrcTwitchOscBridge/AvatarPickerWindow.xaml`
- Modify: `VrcTwitchOscBridge/AvatarPickerWindow.xaml.cs`

- [ ] **Step 1: Add context menu to avatar card template**

In `AvatarPickerWindow.xaml`, find the `AvatarCardTemplate` DataTemplate. Add a `ContextMenu` to the outer Border of the card:

```xml
<Border.ContextMenu>
    <ContextMenu>
        <MenuItem Header="{loc:Translate 'Set Custom Icon...'}" Click="OnSetCustomIconClicked" />
        <MenuItem Header="{loc:Translate 'Clear Custom Icon'}" Click="OnClearCustomIconClicked" />
    </ContextMenu>
</Border.ContextMenu>
```

Do the same for the `AvatarListItemTemplate` ListBoxItem.

- [ ] **Step 2: Add context menu handlers in code-behind**

In `AvatarPickerWindow.xaml.cs`, add:

```csharp
    private void OnSetCustomIconClicked(object sender, RoutedEventArgs e)
    {
        var item = (sender as MenuItem)?.DataContext as AvatarPickerItem;
        if (item is null) return;
        var entry = viewModel.Library?.GetEntry(item.Id);
        if (entry is null)
        {
            viewModel.Library?.EnsureEntry(item.Id);
            entry = viewModel.Library?.GetEntry(item.Id);
        }
        if (entry is not null)
        {
            // We need access to imageService - store it as a field
            // Already stored as imageService field
            var dialog = new Microsoft.Win32.OpenFileDialog
            {
                Filter = "Image Files|*.png;*.jpg;*.jpeg;*.gif;*.bmp;*.webp",
                Title = "Choose Avatar Icon"
            };
            if (dialog.ShowDialog(this) == true)
            {
                var relativePath = imageService.SaveCustomIcon(entry.AvatarId, dialog.FileName);
                if (!string.IsNullOrWhiteSpace(relativePath))
                {
                    entry.CustomIconPath = relativePath;
                    RefreshAvatarImage(item);
                }
            }
        }
    }

    private void OnClearCustomIconClicked(object sender, RoutedEventArgs e)
    {
        var item = (sender as MenuItem)?.DataContext as AvatarPickerItem;
        if (item is null) return;
        var entry = viewModel.Library?.GetEntry(item.Id);
        if (entry is not null)
        {
            entry.CustomIconPath = string.Empty;
            RefreshAvatarImage(item);
        }
    }

    private void RefreshAvatarImage(AvatarPickerItem item)
    {
        imageService.ClearCache();
        var entry = viewModel.Library?.GetEntry(item.Id);
        var newImage = imageService.GetAvatarImage(item.Id, entry?.CustomIconPath, vrchatThumbnailUrl: null);
        var allAvatars = viewModel.AllAvatars;
        var index = allAvatars.IndexOf(item);
        if (index >= 0)
        {
            var updated = new AvatarPickerItem(item.Id, item.Name, item.SourceLabel, newImage);
            allAvatars[index] = updated;
            viewModel.RefreshFilter();
        }
    }
```

- [ ] **Step 3: Build and verify**

Run: `dotnet build "VrcTwitchOscBridge.csproj" --no-restore`
Expected: Build succeeded

- [ ] **Step 4: Commit**

```bash
git add VrcTwitchOscBridge/AvatarPickerWindow.xaml VrcTwitchOscBridge/AvatarPickerWindow.xaml.cs
git commit -m "feat: add context menu for custom icon picker on avatar cards"
```

---

### Task 5: Localization

**Files:**
- Modify: `VrcTwitchOscBridge/Resources/Localization/en-US.extra.json`
- Modify: All other `*.extra.json` files

- [ ] **Step 1: Add English source keys**

Add to `en-US.extra.json` before the closing `}`:
```json
  "Manage Avatar Library | Crystal Relay": "Manage Avatar Library | Crystal Relay",
  "Manage Avatar Library": "Manage Avatar Library",
  "Groups": "Groups",
  "Tags": "Tags",
  "Add Group": "Add Group",
  "Delete Group": "Delete Group",
  "Add Tag": "Add Tag",
  "Delete Tag": "Delete Tag",
  "Hide": "Hide",
  "Set Custom Icon...": "Set Custom Icon...",
  "Clear Custom Icon": "Clear Custom Icon",
  "Group:": "Group:",
  "Tag:": "Tag:",
  "All Groups": "All Groups",
  "All Tags": "All Tags",
  "Choose Avatar Icon": "Choose Avatar Icon"
```

- [ ] **Step 2: Add translations to all non-English files**

Add to each `*.extra.json` file (es-ES, de-DE, fr-FR, ja-JP, pt-BR, sv-SE, ru-RU, it-IT, zh-CN, zh-TW, ko-KR, pl-PL, th-TH) before the closing `}`:
```json
  "Manage Avatar Library | Crystal Relay": "...",
  "Manage Avatar Library": "...",
  "Groups": "...",
  "Tags": "...",
  "Add Group": "...",
  "Delete Group": "...",
  "Add Tag": "...",
  "Delete Tag": "...",
  "Hide": "...",
  "Set Custom Icon...": "...",
  "Clear Custom Icon": "...",
  "Group:": "...",
  "Tag:": "...",
  "All Groups": "...",
  "All Tags": "...",
  "Choose Avatar Icon": "..."
```

- [ ] **Step 3: Run localization audit**

Run: `dotnet run --project LocalizationAudit --no-restore`
Expected: No missing keys for the new strings

- [ ] **Step 4: Commit**

```bash
git add VrcTwitchOscBridge/Resources/Localization/*.extra.json
git commit -m "feat: add localization keys for avatar library manager"
```

---

### Task 6: Final Verification

- [ ] **Step 1: Full build**

Run: `dotnet build "VrcTwitchOscBridge.csproj" --no-restore`
Expected: Build succeeded, 0 warnings, 0 errors

- [ ] **Step 2: Verify csproj has all new files**

Check that these are in `VrcTwitchOscBridge.csproj`:
- `<Page Include="AvatarLibraryManagerWindow.xaml" />`
- `<Compile Include="AvatarLibraryManagerWindow.xaml.cs" />`
- `<Compile Include="ViewModels\AvatarLibraryManagerViewModel.cs" />`

- [ ] **Step 3: Git review**

Run: `git diff --stat HEAD~6..HEAD`
Verify all expected files are changed, no stray files

- [ ] **Step 4: Final commit if needed**
