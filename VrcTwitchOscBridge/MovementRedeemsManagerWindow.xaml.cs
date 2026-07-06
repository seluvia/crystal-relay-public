using System;
using System.Windows;
using System.Windows.Input;
using System.Windows.Interop;
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

    private void OnPickManagedRewardColorClicked(object sender, RoutedEventArgs e)
    {
        if (sender is not System.Windows.Controls.Button button) return;
        if (Vm.SelectedRule is null) return;

        var isCooldownColor = string.Equals(button.Tag as string, "Cooldown", StringComparison.OrdinalIgnoreCase);
        var initialColor = isCooldownColor
            ? Vm.SelectedRule.ManagedRewardCooldownColor
            : Vm.SelectedRule.ManagedRewardReadyColor;

        using var dialog = new System.Windows.Forms.ColorDialog
        {
            AllowFullOpen = true,
            FullOpen = true,
            AnyColor = true,
            SolidColorOnly = false,
            Color = ManagedRewardPresentation.ToDrawingColor(initialColor, ManagedRewardPresentation.ReadyBackgroundColor),
        };

        var ownerHandle = new WindowInteropHelper(this).Handle;
        var result = ownerHandle != IntPtr.Zero
            ? dialog.ShowDialog(new NativeWin32Window(ownerHandle))
            : dialog.ShowDialog();

        if (result != System.Windows.Forms.DialogResult.OK) return;

        var selectedColor = ManagedRewardPresentation.ToHex(dialog.Color);
        if (isCooldownColor)
            Vm.SelectedRule.ManagedRewardCooldownColor = selectedColor;
        else
            Vm.SelectedRule.ManagedRewardReadyColor = selectedColor;
    }

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

    private sealed class NativeWin32Window(IntPtr handle) : System.Windows.Forms.IWin32Window
    {
        public IntPtr Handle { get; } = handle;
    }
}
