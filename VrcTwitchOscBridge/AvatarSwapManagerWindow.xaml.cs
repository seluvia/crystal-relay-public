using System.Windows;
using System.Windows.Input;
using VrcTwitchOscBridge.Models;
using VrcTwitchOscBridge.Services;
using VrcTwitchOscBridge.ViewModels;

namespace VrcTwitchOscBridge;

public partial class AvatarSwapManagerWindow : Window
{
    private readonly AvatarSwapManagerViewModel _viewModel;

    public AvatarSwapManagerWindow(AvatarSwapManagerViewModel viewModel)
    {
        InitializeComponent();
        ThemeManager.ApplyToResources(Resources, ThemeManager.CurrentTheme);
        ThemeManager.ThemeChanged += OnThemeChanged;
        _viewModel = viewModel ?? throw new System.ArgumentNullException(nameof(viewModel));
        DataContext = _viewModel;
    }

    public AvatarSwapManagerViewModel ViewModel => _viewModel;

    private void OnThemeChanged(object? sender, EventArgs e)
    {
        Dispatcher.BeginInvoke(() => ThemeManager.ApplyToResources(Resources));
    }

    protected override void OnClosed(EventArgs e)
    {
        ThemeManager.ThemeChanged -= OnThemeChanged;
        base.OnClosed(e);
    }

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

    private void OnPickGlobalReturnClicked(object sender, RoutedEventArgs e)
    {
        var mainVm = GetMainWindowViewModel();
        if (mainVm is null) return;
        var avatars = mainVm.GetAllVrChatAvatars();
        var result = AvatarPickerService.OpenSingle(
            ThemeManager.CurrentTheme,
            avatars,
            mainVm.Settings.AvatarLibrary,
            mainVm.Settings.MasterAvatarSwapReturnId,
            this);
        if (result is null) return;
        _viewModel.SetGlobalReturnAvatar(result.AvatarId, result.AvatarName);
    }

    private void OnUseCurrentAvatarForGlobalReturnClicked(object sender, RoutedEventArgs e)
    {
        var mainVm = GetMainWindowViewModel();
        if (mainVm is null) return;
        var currentId = mainVm.Settings.VrChat.CurrentAvatarId;
        if (string.IsNullOrWhiteSpace(currentId)) return;
        var name = mainVm.ResolveVrChatAvatarName(currentId);
        _viewModel.SetGlobalReturnAvatar(currentId, name);
    }

    private void OnPickTargetAvatarClicked(object sender, RoutedEventArgs e)
    {
        var mainVm = GetMainWindowViewModel();
        if (mainVm is null) return;
        var avatars = mainVm.GetAllVrChatAvatars();
        var currentTargetId = _viewModel.SelectedSwapCard?.Profile.TargetAvatarId;
        var result = AvatarPickerService.OpenSingle(
            ThemeManager.CurrentTheme,
            avatars,
            mainVm.Settings.AvatarLibrary,
            currentTargetId,
            this);
        if (result is null) return;
        _viewModel.SetTargetAvatar(result.AvatarId, result.AvatarName);
    }

    private void OnUseCurrentAvatarForTargetClicked(object sender, RoutedEventArgs e)
    {
        var mainVm = GetMainWindowViewModel();
        if (mainVm is null) return;
        var currentId = mainVm.Settings.VrChat.CurrentAvatarId;
        if (string.IsNullOrWhiteSpace(currentId)) return;
        var name = mainVm.ResolveVrChatAvatarName(currentId);
        _viewModel.SetTargetAvatar(currentId, name);
    }

    private static MainWindowViewModel? GetMainWindowViewModel()
    {
        if (Application.Current?.MainWindow?.DataContext is MainWindowViewModel mainVm)
        {
            return mainVm;
        }
        return null;
    }
}
