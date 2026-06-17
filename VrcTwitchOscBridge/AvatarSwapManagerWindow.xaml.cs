using System.Windows;
using System.Windows.Input;
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
    }

    public AvatarSwapManagerViewModel ViewModel => _viewModel;

    private void OnTitleBarMouseDown(object sender, MouseButtonEventArgs e)
    {
        if (e.ChangedButton == MouseButton.Left)
        {
            if (e.ClickCount == 2)
            {
                WindowState = WindowState == WindowState.Maximized ? WindowState.Normal : WindowState.Maximized;
            }
            else
            {
                DragMove();
            }
        }
    }

    private void OnCloseClicked(object sender, RoutedEventArgs e)
    {
        Close();
    }
}
