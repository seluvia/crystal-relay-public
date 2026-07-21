using System;
using System.Windows;
using System.Windows.Input;
using System.Windows.Interop;
using VrcTwitchOscBridge.Models;
using VrcTwitchOscBridge.Services;
using VrcTwitchOscBridge.ViewModels;
using WinForms = System.Windows.Forms;

namespace VrcTwitchOscBridge;

public partial class RewardFireSaleManagerWindow : Window
{
    public RewardFireSaleManagerWindow(RewardFireSaleManagerViewModel viewModel)
    {
        DataContext = viewModel ?? throw new ArgumentNullException(nameof(viewModel));
        InitializeComponent();
        ThemeManager.ApplyToResources(Resources, ThemeManager.CurrentTheme);
        ThemeManager.ThemeChanged += OnThemeManagerThemeChanged;
        Closed += OnWindowClosed;
    }

    private void OnTitleBarMouseDown(object sender, MouseButtonEventArgs e)
    {
        if (e.ChangedButton != MouseButton.Left || e.ButtonState != MouseButtonState.Pressed)
            return;
        try { DragMove(); }
        catch (InvalidOperationException) { }
    }

    private void OnCloseClicked(object sender, RoutedEventArgs e) => Close();

    private void OnMinimizeClicked(object sender, RoutedEventArgs e) =>
        WindowState = WindowState.Minimized;

    private void OnPickManagedRewardColorClicked(object sender, RoutedEventArgs e)
    {
        if (sender is not FrameworkElement { Tag: string tagName })
        {
            return;
        }

        var fireSale = Settings;
        if (fireSale is null)
        {
            return;
        }

        var isCooldownColor = string.Equals(tagName, "Cooldown", StringComparison.OrdinalIgnoreCase);
        var fallbackColor = isCooldownColor
            ? ManagedRewardPresentation.InUseBackgroundColor
            : ManagedRewardPresentation.ReadyBackgroundColor;
        var initialColor = isCooldownColor
            ? fireSale.FundingRewardCooldownColor
            : fireSale.FundingRewardReadyColor;

        using var dialog = new WinForms.ColorDialog
        {
            AllowFullOpen = true,
            FullOpen = true,
            AnyColor = true,
            SolidColorOnly = false,
            Color = ManagedRewardPresentation.ToDrawingColor(initialColor, fallbackColor)
        };

        var ownerHandle = new WindowInteropHelper(this).Handle;
        var result = ownerHandle != IntPtr.Zero
            ? dialog.ShowDialog(new NativeWin32Window(ownerHandle))
            : dialog.ShowDialog();
        if (result != WinForms.DialogResult.OK)
        {
            return;
        }

        var selectedColor = ManagedRewardPresentation.ToHex(dialog.Color);
        if (isCooldownColor)
        {
            fireSale.FundingRewardCooldownColor = selectedColor;
        }
        else
        {
            fireSale.FundingRewardReadyColor = selectedColor;
        }
    }

    private RewardFireSaleSettings? Settings =>
        (DataContext as RewardFireSaleManagerViewModel)?.Settings;

    private sealed class NativeWin32Window(IntPtr handle) : WinForms.IWin32Window
    {
        public IntPtr Handle { get; } = handle;
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
            disposableDataContext.Dispose();
    }
}
