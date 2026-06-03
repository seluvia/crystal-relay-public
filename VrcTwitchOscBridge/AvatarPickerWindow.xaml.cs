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

        UpdateSelectionDisplay();
        UpdateFilteredCountText();

        viewModel.PropertyChanged += (sender, e) =>
        {
            if (e.PropertyName == nameof(AvatarPickerViewModel.SelectedAvatarDisplayName) ||
                e.PropertyName == nameof(AvatarPickerViewModel.MultiSelectCountText) ||
                e.PropertyName == nameof(AvatarPickerViewModel.IsMultiSelectMode) ||
                e.PropertyName == nameof(AvatarPickerViewModel.FilteredCountText))
            {
                Dispatcher.BeginInvoke(() =>
                {
                    UpdateSelectionDisplay();
                    UpdateFilteredCountText();
                });
            }
        };

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

    private void OnGridViewClicked(object sender, RoutedEventArgs e)
    {
        viewModel.ViewMode = AvatarPickerViewMode.Grid;
    }

    private void OnListViewClicked(object sender, RoutedEventArgs e)
    {
        viewModel.ViewMode = AvatarPickerViewMode.List;
    }

    private void OnAvatarCardSelectClicked(object sender, RoutedEventArgs e)
    {
        if (sender is Button button && button.Tag is AvatarPickerItem item)
        {
            SelectAvatarItem(item);
        }
    }

    private void OnAvatarListSelectClicked(object sender, RoutedEventArgs e)
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