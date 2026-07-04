using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using VrcTwitchOscBridge.Models;
using VrcTwitchOscBridge.Services;
using VrcTwitchOscBridge.ViewModels;

namespace VrcTwitchOscBridge;

public partial class AvatarPickerWindow : Window
{
    private readonly AvatarPickerViewModel viewModel;
    private readonly AvatarImageService imageService;
    private AvatarLibraryManagerWindow? managerWindow;

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

        DataContext = viewModel;

        InitializeComponent();
        ThemeManager.ApplyToResources(Resources, theme);
        ThemeManager.ThemeChanged += OnThemeManagerThemeChanged;
        Closed += OnWindowClosed;

        UpdateSelectionDisplay();
        UpdateFilteredCountText();

        Loaded += OnLoaded;
        PreviewKeyDown += OnPreviewKeyDown;
    }

    private void OnLoaded(object sender, RoutedEventArgs e)
    {
        SearchTextBox.Focus();
        _ = viewModel.LoadImagesAsync();
    }

    private void OnThemeManagerThemeChanged(object? sender, EventArgs e)
    {
        Dispatcher.BeginInvoke(() => ThemeManager.ApplyToResources(Resources));
    }

    private void OnWindowClosed(object? sender, EventArgs e)
    {
        viewModel.CancelImageLoading();
        ThemeManager.ThemeChanged -= OnThemeManagerThemeChanged;
        Closed -= OnWindowClosed;
        PreviewKeyDown -= OnPreviewKeyDown;
    }

    private void OnRefreshIconsClicked(object sender, RoutedEventArgs e)
    {
        _ = viewModel.RefreshAllImagesAsync();
    }

    private void OnCloseButtonClicked(object sender, RoutedEventArgs e)
    {
        DialogResult = false;
        Close();
    }

    private void OnManageButtonClicked(object sender, RoutedEventArgs e)
    {
        if (managerWindow is not null)
        {
            managerWindow.Activate();
            return;
        }

        managerWindow = new AvatarLibraryManagerWindow(
            ThemeManager.CurrentTheme,
            viewModel.Library ?? new AvatarLibrary(),
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
            try
            {
                DragMove();
            }
            catch
            {
            }
        }
    }

    private void OnGridViewToggled(object sender, RoutedEventArgs e)
    {
        viewModel.ViewMode = AvatarPickerViewMode.Grid;
    }

    private void OnListViewToggled(object sender, RoutedEventArgs e)
    {
        viewModel.ViewMode = AvatarPickerViewMode.List;
    }

    private void OnAvatarSelectClicked(object sender, RoutedEventArgs e)
    {
        if (sender is Button button && button.Tag is AvatarPickerItem item)
        {
            SelectAvatarItem(item);
            UpdateSelectionDisplay();
        }
    }

    private void SelectAvatarItem(AvatarPickerItem item)
    {
        if (viewModel.IsMultiSelectMode)
        {
            viewModel.ToggleMultiSelect(item);
        }
        else
        {
            viewModel.SelectedItem = item;
        }
    }

    public IReadOnlyList<string> GetSelectedAvatarIds() => viewModel.GetSelectedAvatarIds();

    private void UpdateSelectionDisplay()
    {
        if (viewModel.IsMultiSelectMode)
        {
            SelectionLabel.Text = viewModel.MultiSelectCountText;
            SelectionName.Text = string.Empty;
        }
        else
        {
            SelectionLabel.Text = LocalizationService.Translate("Selected avatar:");
            SelectionName.Text = viewModel.SelectedAvatarDisplayName;
        }
    }

    private void UpdateFilteredCountText()
    {
        FilteredCountText.Text = viewModel.FilteredCountText;
    }

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

    private void OnSetGroupSubmenuOpened(object sender, RoutedEventArgs e)
    {
        if (sender is not MenuItem groupMenu) return;
        var item = (groupMenu.Parent as ContextMenu)?.DataContext as AvatarPickerItem;
        if (item is null) return;

        groupMenu.Items.Clear();
        var library = viewModel.Library;
        if (library is null) return;

        var entry = library.GetEntry(item.Id);
        var currentGroupId = entry?.GroupId ?? string.Empty;

        foreach (var group in library.Groups.OrderBy(g => g.SortOrder).ThenBy(g => g.Name))
        {
            var menuItem = new MenuItem
            {
                Header = group.Name,
                IsCheckable = true,
                IsChecked = string.Equals(group.Id, currentGroupId, StringComparison.Ordinal),
                Tag = new Tuple<string, AvatarPickerItem, string>(group.Id, item, "set")
            };
            menuItem.Click += OnGroupMenuItemClicked;
            groupMenu.Items.Add(menuItem);
        }

        groupMenu.Items.Add(new Separator());

        var removeItem = new MenuItem
        {
            Header = LocalizationService.Translate("Remove from group"),
            IsCheckable = true,
            IsChecked = string.IsNullOrWhiteSpace(currentGroupId),
            Tag = new Tuple<string, AvatarPickerItem, string>(string.Empty, item, "remove")
        };
        removeItem.Click += OnGroupMenuItemClicked;
        groupMenu.Items.Add(removeItem);

        groupMenu.Items.Add(new Separator());

        var newItem = new MenuItem
        {
            Header = LocalizationService.Translate("New Group..."),
            Tag = item
        };
        newItem.Click += OnNewGroupFromMenuClicked;
        groupMenu.Items.Add(newItem);
    }

    private void OnGroupMenuItemClicked(object sender, RoutedEventArgs e)
    {
        if (sender is not MenuItem menuItem) return;
        if (menuItem.Tag is not Tuple<string, AvatarPickerItem, string> tag) return;
        var (groupId, item, _) = tag;

        var library = viewModel.Library;
        if (library is null) return;

        library.EnsureEntry(item.Id);
        var entry = library.GetEntry(item.Id);
        if (entry is null) return;

        entry.GroupId = groupId;
        viewModel.RebuildItem(item);
        viewModel.RebuildFilterOptions();
    }

    private void OnNewGroupFromMenuClicked(object sender, RoutedEventArgs e)
    {
        if (sender is not MenuItem menuItem) return;
        if (menuItem.Tag is not AvatarPickerItem item) return;

        var library = viewModel.Library;
        if (library is null) return;

        var name = ThemedInputDialog.ShowPrompt(
            this,
            ThemeManager.CurrentTheme,
            LocalizationService.Translate("New Group..."),
            LocalizationService.Translate("New group name:"),
            LocalizationService.Translate("Create"));
        if (string.IsNullOrWhiteSpace(name)) return;

        var group = new AvatarGroup
        {
            Name = name.Trim(),
            SortOrder = library.Groups.Count
        };
        library.Groups.Add(group);

        library.EnsureEntry(item.Id);
        var entry = library.GetEntry(item.Id);
        if (entry is not null)
        {
            entry.GroupId = group.Id;
        }

        viewModel.RebuildItem(item);
        viewModel.RebuildFilterOptions();
    }

    private void OnTagsSubmenuOpened(object sender, RoutedEventArgs e)
    {
        if (sender is not MenuItem tagsMenu) return;
        var item = (tagsMenu.Parent as ContextMenu)?.DataContext as AvatarPickerItem;
        if (item is null) return;

        tagsMenu.Items.Clear();
        var library = viewModel.Library;
        if (library is null) return;

        var entry = library.GetEntry(item.Id);
        var currentTagIds = entry?.TagIds ?? new List<string>();

        foreach (var tag in library.Tags.OrderBy(t => t.Name))
        {
            var menuItem = new MenuItem
            {
                Header = tag.Name,
                IsCheckable = true,
                IsChecked = currentTagIds.Contains(tag.Id),
                Tag = new Tuple<AvatarTag, AvatarPickerItem>(tag, item)
            };
            menuItem.Click += OnTagMenuItemClicked;
            tagsMenu.Items.Add(menuItem);
        }

        if (library.Tags.Count > 0)
        {
            tagsMenu.Items.Add(new Separator());
        }

        var newItem = new MenuItem
        {
            Header = LocalizationService.Translate("New Tag..."),
            Tag = item
        };
        newItem.Click += OnNewTagFromMenuClicked;
        tagsMenu.Items.Add(newItem);
    }

    private void OnTagMenuItemClicked(object sender, RoutedEventArgs e)
    {
        if (sender is not MenuItem menuItem) return;
        if (menuItem.Tag is not Tuple<AvatarTag, AvatarPickerItem> tagTuple) return;
        var (tag, item) = tagTuple;

        var library = viewModel.Library;
        if (library is null) return;

        library.EnsureEntry(item.Id);
        var entry = library.GetEntry(item.Id);
        if (entry is null) return;

        if (entry.TagIds.Contains(tag.Id))
        {
            entry.TagIds.Remove(tag.Id);
        }
        else
        {
            entry.TagIds.Add(tag.Id);
        }

        viewModel.RebuildItem(item);
    }

    private void OnNewTagFromMenuClicked(object sender, RoutedEventArgs e)
    {
        if (sender is not MenuItem menuItem) return;
        if (menuItem.Tag is not AvatarPickerItem item) return;

        var library = viewModel.Library;
        if (library is null) return;

        var (name, color) = ThemedInputDialog.ShowPromptWithColor(
            this,
            ThemeManager.CurrentTheme,
            LocalizationService.Translate("New Tag..."),
            LocalizationService.Translate("New tag name:"),
            LocalizationService.Translate("Tag color:"),
            LocalizationService.Translate("Create"));
        if (string.IsNullOrWhiteSpace(name)) return;

        var tag = new AvatarTag
        {
            Name = name.Trim(),
            ColorHex = string.IsNullOrWhiteSpace(color) ? "#A855F7" : color
        };
        library.Tags.Add(tag);

        library.EnsureEntry(item.Id);
        var entry = library.GetEntry(item.Id);
        if (entry is not null && !entry.TagIds.Contains(tag.Id))
        {
            entry.TagIds.Add(tag.Id);
        }

        viewModel.RebuildItem(item);
        viewModel.RebuildFilterOptions();
    }

    private void OnTagChipRemoveClicked(object sender, MouseButtonEventArgs e)
    {
        if (sender is not FrameworkElement element) return;
        if (element.DataContext is not AvatarTagDisplay tag) return;

        var parent = element;
        AvatarPickerItem? item = null;
        while (parent is not null)
        {
            if (parent.DataContext is AvatarPickerItem found)
            {
                item = found;
                break;
            }
            parent = VisualTreeHelper.GetParent(parent) as FrameworkElement;
        }
        if (item is null) return;

        var library = viewModel.Library;
        if (library is null) return;

        var entry = library.GetEntry(item.Id);
        if (entry is null) return;

        entry.TagIds.Remove(tag.Id);
        viewModel.RebuildItem(item);
        e.Handled = true;
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
            var updated = new AvatarPickerItem(item.Id, item.Name, item.SourceLabel, newImage, item.ThumbnailUrl, item.IsSelected, item.Tags);
            allAvatars[index] = updated;
            viewModel.RefreshFilter();
        }
    }

    private void OnCardCheckBoxClicked(object sender, RoutedEventArgs e)
    {
        if (sender is CheckBox cb && cb.DataContext is AvatarPickerItem item)
        {
            viewModel.ToggleMultiSelect(item);
            UpdateSelectionDisplay();
            e.Handled = true;
        }
    }

    private void OnPreviewKeyDown(object sender, KeyEventArgs e)
    {
        if (viewModel.IsMultiSelectMode)
        {
            if (e.Key == Key.A && Keyboard.Modifiers == ModifierKeys.Control)
            {
                viewModel.SelectAll();
                UpdateSelectionDisplay();
                e.Handled = true;
                return;
            }
            if (e.Key == Key.D && Keyboard.Modifiers == ModifierKeys.Control)
            {
                viewModel.DeselectAll();
                UpdateSelectionDisplay();
                e.Handled = true;
                return;
            }
        }

        if (e.Key == Key.Enter || e.Key == Key.Space)
        {
            var focused = FocusManager.GetFocusedElement(this) as FrameworkElement;
            if (focused?.DataContext is AvatarPickerItem item)
            {
                if (viewModel.IsMultiSelectMode)
                {
                    viewModel.ToggleMultiSelect(item);
                }
                else
                {
                    viewModel.SelectedItem = item;
                }
                UpdateSelectionDisplay();
                e.Handled = true;
                return;
            }
        }

        // Arrow key navigation for grid view
        if (viewModel.ViewMode == AvatarPickerViewMode.Grid)
        {
            NavigateGridItems(e);
        }
    }

    private void NavigateGridItems(KeyEventArgs e)
    {
        var focused = FocusManager.GetFocusedElement(this) as FrameworkElement;
        if (focused?.DataContext is not AvatarPickerItem currentItem) return;

        var items = viewModel.FilteredAvatars;
        var currentIndex = items.IndexOf(currentItem);
        if (currentIndex < 0) return;

        // Estimate columns based on actual GridViewControl width
        var actualWidth = GridViewControl.ActualWidth;
        var itemWidth = 152.0; // 140 width + 12 margin
        var columns = Math.Max(1, (int)(actualWidth / itemWidth));

        int nextIndex = currentIndex;
        switch (e.Key)
        {
            case Key.Left:
                nextIndex = Math.Max(0, currentIndex - 1);
                break;
            case Key.Right:
                nextIndex = Math.Min(items.Count - 1, currentIndex + 1);
                break;
            case Key.Up:
                nextIndex = Math.Max(0, currentIndex - columns);
                break;
            case Key.Down:
                nextIndex = Math.Min(items.Count - 1, currentIndex + columns);
                break;
            default:
                return;
        }

        if (nextIndex != currentIndex)
        {
            e.Handled = true;
            var nextItem = items[nextIndex];
            var container = GridViewControl.ItemContainerGenerator.ContainerFromItem(nextItem) as FrameworkElement;
            container?.Focus();
        }
    }

    // Drag-and-drop for list view reordering
    private AvatarPickerItem? dragDropItem;
    private Point dragDropStartPoint;

    private void OnListViewPreviewMouseLeftButtonDown(object sender, MouseButtonEventArgs e)
    {
        var point = e.GetPosition(ListViewControl);
        var hitTest = VisualTreeHelper.HitTest(ListViewControl, point);
        if (hitTest is null) return;

        var item = FindListBoxItem(hitTest.VisualHit);
        if (item?.DataContext is AvatarPickerItem avatarItem)
        {
            dragDropItem = avatarItem;
            dragDropStartPoint = point;
        }
    }

    private void OnListViewPreviewMouseMove(object sender, MouseEventArgs e)
    {
        if (dragDropItem is null || e.LeftButton != MouseButtonState.Pressed) return;

        var point = e.GetPosition(ListViewControl);
        var diff = dragDropStartPoint - point;

        if (Math.Abs(diff.X) > SystemParameters.MinimumHorizontalDragDistance ||
            Math.Abs(diff.Y) > SystemParameters.MinimumVerticalDragDistance)
        {
            DragDrop.DoDragDrop(ListViewControl, dragDropItem, DragDropEffects.Move);
            dragDropItem = null;
        }
    }

    private void OnListViewDrop(object sender, DragEventArgs e)
    {
        if (e.Data.GetData(typeof(AvatarPickerItem)) is not AvatarPickerItem droppedItem) return;

        var targetPoint = e.GetPosition(ListViewControl);
        var targetItem = FindListBoxItemAtPoint(targetPoint);
        if (targetItem is null || ReferenceEquals(targetItem, droppedItem)) return;

        // Reorder in SelectedMultiAvatarIds if in multi-select mode
        if (viewModel.IsMultiSelectMode)
        {
            var pool = viewModel.SelectedMultiAvatarIds;
            var droppedId = droppedItem.Id;
            var targetId = targetItem.Id;

            var droppedIndex = pool.IndexOf(droppedId);
            var targetIndex = pool.IndexOf(targetId);

            if (droppedIndex >= 0 && targetIndex >= 0 && droppedIndex != targetIndex)
            {
                pool.RemoveAt(droppedIndex);
                pool.Insert(targetIndex, droppedId);
            }
        }
    }

    private ListBoxItem? FindListBoxItem(DependencyObject? visual)
    {
        while (visual is not null and not ListBoxItem)
        {
            visual = VisualTreeHelper.GetParent(visual);
        }
        return visual as ListBoxItem;
    }

    private AvatarPickerItem? FindListBoxItemAtPoint(Point point)
    {
        var hitTest = VisualTreeHelper.HitTest(ListViewControl, point);
        var item = FindListBoxItem(hitTest?.VisualHit);
        return item?.DataContext as AvatarPickerItem;
    }
}