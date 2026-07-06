using System;
using System.Windows;
using System.Windows.Input;
using VrcTwitchOscBridge.Services;
using VrcTwitchOscBridge.ViewModels;

namespace VrcTwitchOscBridge;

public sealed partial class MovementRedeemsManagerWindow : Window
{
    public MovementRedeemsManagerWindow(MovementRedeemsManagerViewModel viewModel)
    {
        DataContext = viewModel ?? throw new ArgumentNullException(nameof(viewModel));
        InitializeComponent();
        ThemeManager.ApplyToResources(Resources, ThemeManager.CurrentTheme);
        ThemeManager.ThemeChanged += OnThemeManagerThemeChanged;
        Closed += OnWindowClosed;
    }

    private MovementRedeemsManagerViewModel Vm => (MovementRedeemsManagerViewModel)DataContext;

    private void OnTitleBarMouseDown(object sender, MouseButtonEventArgs e)
    {
        if (e.ChangedButton != MouseButton.Left || e.ButtonState != MouseButtonState.Pressed)
            return;

        try
        {
            DragMove();
        }
        catch (InvalidOperationException)
        {
        }
    }

    private void OnCloseClicked(object sender, RoutedEventArgs e) => Close();

    private void OnEditorBackdropClicked(object sender, MouseButtonEventArgs e) => Vm.IsEditorOpen = false;

    private void OnThemeManagerThemeChanged(object? sender, EventArgs e)
    {
        Dispatcher.BeginInvoke(() => ThemeManager.ApplyToResources(Resources));
    }

    private void OnWindowClosed(object? sender, EventArgs e)
    {
        ThemeManager.ThemeChanged -= OnThemeManagerThemeChanged;
        Closed -= OnWindowClosed;
        if (DataContext is IDisposable disposableDataContext)
        {
            disposableDataContext.Dispose();
        }
    }
}
