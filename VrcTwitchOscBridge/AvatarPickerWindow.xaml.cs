using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
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
    }

    private void OnLoaded(object sender, RoutedEventArgs e)
    {
        SearchTextBox.Focus();
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
}