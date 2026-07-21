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
            owner: this);
        if (result is null) return;
        mainVm.ApplySharedReturnAvatarSelection(result.AvatarId, result.AvatarName, saveImmediately: true);
        _viewModel.SetGlobalReturnAvatar(result.AvatarId, result.AvatarName);
    }

    private void OnUseCurrentAvatarForGlobalReturnClicked(object sender, RoutedEventArgs e)
    {
        var mainVm = GetMainWindowViewModel();
        if (mainVm is null) return;
        var currentId = mainVm.Settings.VrChat.CurrentAvatarId;
        if (string.IsNullOrWhiteSpace(currentId)) return;
        var name = mainVm.ResolveVrChatAvatarName(currentId);
        mainVm.ApplySharedReturnAvatarSelection(currentId, name, saveImmediately: true);
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
            owner: this);
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

    private void OnPickRoulettePoolClicked(object sender, RoutedEventArgs e)
    {
        var mainVm = GetMainWindowViewModel();
        var roulette = _viewModel.SelectedRouletteCard?.Roulette;
        if (mainVm is null || roulette is null) return;

        var avatars = mainVm.GetAllVrChatAvatars();
        var currentPool = roulette.Pool
            .Select(entry => entry.AvatarId?.Trim() ?? string.Empty)
            .Where(avatarId => !string.IsNullOrWhiteSpace(avatarId))
            .Distinct(StringComparer.Ordinal)
            .ToArray();

        var selectedIds = AvatarPickerService.OpenMulti(
            ThemeManager.CurrentTheme,
            avatars,
            mainVm.Settings.AvatarLibrary,
            currentPool,
            owner: this);

        _viewModel.SetRoulettePoolSelection(selectedIds, avatars);
    }

    private void OnOpenPowerUpLibraryClicked(object sender, RoutedEventArgs e)
    {
        var mainVm = GetMainWindowViewModel();
        mainVm?.ShowPowerUpsCommand.Execute(null);
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
