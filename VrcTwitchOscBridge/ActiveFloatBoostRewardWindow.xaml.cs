using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Interop;
using VrcTwitchOscBridge.Models;
using VrcTwitchOscBridge.Services;
using WinForms = System.Windows.Forms;

namespace VrcTwitchOscBridge;

public partial class ActiveFloatBoostRewardWindow : Window
{
    public ActiveFloatBoostRewardWindow(AppTheme theme, TriggerRule rule)
    {
        InitializeComponent();
        ThemeManager.ApplyToResources(Resources, theme);
        ThemeManager.ThemeChanged += OnThemeManagerThemeChanged;
        Closed += OnWindowClosed;
        DataContext = rule;
        RuleNameTextBlock.Text = LocalizationService.Format(
            "Boost reward for {0}",
            rule.DisplayTitle);
    }

    private void OnCloseButtonClicked(object sender, RoutedEventArgs e) => DialogResult = true;

    private void OnPickActiveBoostRewardColorClicked(object sender, RoutedEventArgs e)
    {
        if (DataContext is not TriggerRule rule || sender is not Button button)
        {
            return;
        }

        var isCooldownColor = string.Equals(button.Tag?.ToString(), "Cooldown", StringComparison.OrdinalIgnoreCase);
        var fallbackColor = isCooldownColor
            ? ManagedRewardPresentation.InUseBackgroundColor
            : ManagedRewardPresentation.ReadyBackgroundColor;
        var initialColor = isCooldownColor
            ? rule.ActiveFloatBoostRewardCooldownColor
            : rule.ActiveFloatBoostRewardReadyColor;

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
            rule.ActiveFloatBoostRewardCooldownColor = selectedColor;
        }
        else
        {
            rule.ActiveFloatBoostRewardReadyColor = selectedColor;
        }
    }

    private void OnWindowClosed(object? sender, EventArgs e)
    {
        ThemeManager.ThemeChanged -= OnThemeManagerThemeChanged;
        Closed -= OnWindowClosed;
    }

    private void OnThemeManagerThemeChanged(object? sender, EventArgs e)
    {
        Dispatcher.BeginInvoke(() => ThemeManager.ApplyToResources(Resources));
    }

    private void OnTitleBarMouseLeftButtonDown(object sender, MouseButtonEventArgs e)
    {
        if (e.ChangedButton != MouseButton.Left)
        {
            return;
        }

        if (FindAncestor<Button>(e.OriginalSource as DependencyObject) is not null)
        {
            return;
        }

        try
        {
            DragMove();
        }
        catch
        {
        }
    }

    private static T? FindAncestor<T>(DependencyObject? current)
        where T : DependencyObject
    {
        while (current is not null)
        {
            if (current is T match)
            {
                return match;
            }

            current = System.Windows.Media.VisualTreeHelper.GetParent(current);
        }

        return null;
    }

    private sealed class NativeWin32Window(IntPtr handle) : WinForms.IWin32Window
    {
        public IntPtr Handle { get; } = handle;
    }
}
