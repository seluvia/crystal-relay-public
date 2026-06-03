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

    public AvatarPickerWindow(
        AppTheme theme,
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
}