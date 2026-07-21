using System.Linq;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using VrcTwitchOscBridge.Models;
using VrcTwitchOscBridge.Services;
using WinForms = System.Windows.Forms;

namespace VrcTwitchOscBridge.UserControls;

public partial class InlineRuleEditorControl : UserControl
{
    public static readonly DependencyProperty ProfileProperty = DependencyProperty.Register(
        nameof(Profile),
        typeof(AvatarSwapProfile),
        typeof(InlineRuleEditorControl),
        new PropertyMetadata(null));

    public AvatarSwapProfile? Profile
    {
        get => (AvatarSwapProfile?)GetValue(ProfileProperty);
        set => SetValue(ProfileProperty, value);
    }

    public InlineRuleEditorControl()
    {
        InitializeComponent();
        Loaded += OnLoaded;
    }

    private void OnLoaded(object sender, RoutedEventArgs e)
    {
        if (DataContext is IRuleRowViewModel rowVm && rowVm.Rule is TriggerRule rule)
        {
            UpdateRadioButtons(rule);
        }
    }

    private void UpdateRadioButtons(TriggerRule rule)
    {
        var global = FindName("ReturnToGlobalRadio") as RadioButton;
        var previous = FindName("ReturnToPreviousRadio") as RadioButton;
        if (global is null || previous is null) return;

        if (rule.ReturnToPreviousAvatar)
            previous.IsChecked = true;
        else
            global.IsChecked = true;
    }

    private void OnReturnToGlobalChecked(object sender, RoutedEventArgs e)
    {
        if (DataContext is IRuleRowViewModel rowVm && rowVm.Rule is TriggerRule rule)
        {
            rule.ReturnToPreviousAvatar = false;
        }
    }

    private void OnReturnToPreviousChecked(object sender, RoutedEventArgs e)
    {
        if (DataContext is IRuleRowViewModel rowVm && rowVm.Rule is TriggerRule rule)
        {
            rule.ReturnToPreviousAvatar = true;
        }
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
