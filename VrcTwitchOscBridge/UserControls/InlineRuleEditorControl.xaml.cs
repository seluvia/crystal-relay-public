using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using VrcTwitchOscBridge.Models;
using VrcTwitchOscBridge.Services;
using WinForms = System.Windows.Forms;

namespace VrcTwitchOscBridge.UserControls;

public partial class InlineRuleEditorControl : UserControl
{
    public InlineRuleEditorControl()
    {
        InitializeComponent();
    }

    private void OnPickReadyColorClicked(object sender, RoutedEventArgs e)
    {
        PickColorForRule(sender, isCooldown: false);
    }

    private void OnPickCooldownColorClicked(object sender, RoutedEventArgs e)
    {
        PickColorForRule(sender, isCooldown: true);
    }

    private void PickColorForRule(object sender, bool isCooldown)
    {
        if (sender is not Button button) return;

        var rule = FindRule(button);
        if (rule is null) return;

        var initialHex = isCooldown ? rule.ManagedRewardCooldownColor : rule.ManagedRewardReadyColor;

        var fallback = isCooldown
            ? ManagedRewardPresentation.InUseBackgroundColor
            : ManagedRewardPresentation.ReadyBackgroundColor;
        var initialColor = ManagedRewardPresentation.ToDrawingColor(initialHex, fallback);

        using var dialog = new WinForms.ColorDialog
        {
            AllowFullOpen = true,
            FullOpen = true,
            AnyColor = true,
            SolidColorOnly = false,
            Color = initialColor
        };

        var owner = Window.GetWindow(this);
        var ownerHandle = owner is not null
            ? new System.Windows.Interop.WindowInteropHelper(owner).Handle
            : IntPtr.Zero;
        var result = ownerHandle != IntPtr.Zero
            ? dialog.ShowDialog(new NativeWin32Window(ownerHandle))
            : dialog.ShowDialog();
        if (result != WinForms.DialogResult.OK) return;

        var selectedHex = ManagedRewardPresentation.ToHex(dialog.Color);
        if (isCooldown)
            rule.ManagedRewardCooldownColor = selectedHex;
        else
            rule.ManagedRewardReadyColor = selectedHex;
    }

    private TriggerRule? FindRule(DependencyObject source)
    {
        DependencyObject? current = source;
        while (current is not null)
        {
            if (current is FrameworkElement { DataContext: IRuleRowViewModel rowViewModel }
                && rowViewModel.Rule is TriggerRule rule)
            {
                return rule;
            }
            current = VisualTreeHelper.GetParent(current);
        }
        return null;
    }

    private sealed class NativeWin32Window : WinForms.IWin32Window
    {
        public NativeWin32Window(IntPtr handle) => Handle = handle;
        public IntPtr Handle { get; }
    }
}
