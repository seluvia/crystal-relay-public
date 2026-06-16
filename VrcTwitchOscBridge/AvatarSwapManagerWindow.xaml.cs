using System.Windows;
using System.Windows.Input;
using VrcTwitchOscBridge.Services;
using VrcTwitchOscBridge.ViewModels;

namespace VrcTwitchOscBridge;

public partial class AvatarSwapManagerWindow : Window
{
    private readonly AvatarSwapManagerViewModel _viewModel;

    public AvatarSwapManagerWindow(AvatarSwapManagerViewModel viewModel)
    {
        InitializeComponent();
        _viewModel = viewModel ?? throw new System.ArgumentNullException(nameof(viewModel));
        DataContext = _viewModel;
        ThemeManager.ThemeChanged += OnThemeChanged;
        ThemeManager.ApplyToResources(Resources);
        Closed += OnClosed;
    }

    public AvatarSwapManagerViewModel ViewModel => _viewModel;

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

    private void OnCloseClicked(object sender, RoutedEventArgs e)
    {
        Close();
    }

    private void OnChannelPointSectionHeaderClick(object sender, MouseButtonEventArgs e)
    {
        _viewModel.ToggleChannelPointSectionCommand.Execute(null);
    }

    private void OnBitsSubsSectionHeaderClick(object sender, MouseButtonEventArgs e)
    {
        _viewModel.ToggleBitsSubsSectionCommand.Execute(null);
    }

    private void OnThemeChanged(object? sender, System.EventArgs e)
    {
        Dispatcher.InvokeAsync(() => ThemeManager.ApplyToResources(Resources));
    }

    private void OnClosed(object? sender, System.EventArgs e)
    {
        ThemeManager.ThemeChanged -= OnThemeChanged;
        _viewModel.OnWindowClosed();
    }
}
