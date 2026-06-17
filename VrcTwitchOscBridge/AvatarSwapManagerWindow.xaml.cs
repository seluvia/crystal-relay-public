using System.Windows;
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
}
