using System;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using VrcTwitchOscBridge.Models;
using VrcTwitchOscBridge.Services;
using VrcTwitchOscBridge.ViewModels;
using WinForms = System.Windows.Forms;

namespace VrcTwitchOscBridge.UserControls;

public partial class InlineAvatarSwapRuleRowControl : UserControl
{
    public static readonly DependencyProperty RowProperty = DependencyProperty.Register(
        nameof(Row), typeof(InlineAvatarSwapRuleRowViewModel), typeof(InlineAvatarSwapRuleRowControl),
        new PropertyMetadata(null));

    public InlineAvatarSwapRuleRowViewModel? Row
    {
        get => (InlineAvatarSwapRuleRowViewModel?)GetValue(RowProperty);
        set => SetValue(RowProperty, value);
    }

    public InlineAvatarSwapRuleRowControl() => InitializeComponent();

    private void OnPickManagedRewardColorClicked(object sender, RoutedEventArgs e)
    {
        if (sender is not Button button) return;

        var isCooldown = string.Equals(button.Tag?.ToString(), "Cooldown", StringComparison.OrdinalIgnoreCase);
        var fallback = isCooldown
            ? ManagedRewardPresentation.InUseBackgroundColor
            : ManagedRewardPresentation.ReadyBackgroundColor;

        var rule = FindRuleFromButton(button);
        if (rule is null) return;

        var initial = isCooldown ? rule.ManagedRewardCooldownColor : rule.ManagedRewardReadyColor;
        if (string.IsNullOrWhiteSpace(initial)) return;

        using var dialog = new WinForms.ColorDialog
        {
            AllowFullOpen = true,
            FullOpen = true,
            AnyColor = true,
            SolidColorOnly = false,
            Color = ManagedRewardPresentation.ToDrawingColor(initial, fallback)
        };

        var owner = Window.GetWindow(this);
        var ownerHandle = owner is not null
            ? new System.Windows.Interop.WindowInteropHelper(owner).Handle
            : IntPtr.Zero;
        var result = ownerHandle != IntPtr.Zero
            ? dialog.ShowDialog(new NativeWin32Window(ownerHandle))
            : dialog.ShowDialog();
        if (result != WinForms.DialogResult.OK) return;

        var hex = ManagedRewardPresentation.ToHex(dialog.Color);
        if (isCooldown) rule.ManagedRewardCooldownColor = hex;
        else rule.ManagedRewardReadyColor = hex;
    }

    private void OnResetManagedRewardColorClicked(object sender, RoutedEventArgs e)
    {
        if (sender is not Button button) return;
        var isCooldown = string.Equals(button.Tag?.ToString(), "Cooldown", StringComparison.OrdinalIgnoreCase);

        var rule = FindRuleFromButton(button);
        if (rule is null) return;

        if (isCooldown) rule.ManagedRewardCooldownColor = ManagedRewardPresentation.InUseBackgroundColor;
        else rule.ManagedRewardReadyColor = ManagedRewardPresentation.ReadyBackgroundColor;
    }

    private static TriggerRule? FindRuleFromButton(Button button)
    {
        DependencyObject? candidate = button;
        while (candidate is not null)
        {
            if (candidate is FrameworkElement { DataContext: TriggerRule rule })
            {
                return rule;
            }
            candidate = VisualTreeHelper.GetParent(candidate);
        }
        return null;
    }

    private sealed class NativeWin32Window : WinForms.IWin32Window
    {
        public NativeWin32Window(IntPtr handle) => Handle = handle;
        public IntPtr Handle { get; }
    }
}
