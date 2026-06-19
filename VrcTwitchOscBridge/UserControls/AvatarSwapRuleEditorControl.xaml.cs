using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using VrcTwitchOscBridge.Models;
using VrcTwitchOscBridge.Services;
using WinForms = System.Windows.Forms;

namespace VrcTwitchOscBridge.UserControls;

public partial class AvatarSwapRuleEditorControl : UserControl
{
    public static readonly DependencyProperty IsInAvatarSwapManagerProperty =
        DependencyProperty.Register(
            nameof(IsInAvatarSwapManager),
            typeof(bool),
            typeof(AvatarSwapRuleEditorControl),
            new PropertyMetadata(false));

    public bool IsInAvatarSwapManager
    {
        get => (bool)GetValue(IsInAvatarSwapManagerProperty);
        set => SetValue(IsInAvatarSwapManagerProperty, value);
    }

    public AvatarSwapRuleEditorControl()
    {
        InitializeComponent();
    }

    private void OnHelpButtonClicked(object sender, RoutedEventArgs e)
    {
        if (sender is not Button button)
        {
            return;
        }

        var title = button.CommandParameter as string;
        var message = button.Tag?.ToString();
        if (string.IsNullOrWhiteSpace(message))
        {
            return;
        }

        var translatedTitle = string.IsNullOrWhiteSpace(title)
            ? LocalizationService.Translate("Help")
            : LocalizationService.Translate(title);
        var translatedMessage = LocalizationService.Translate(message);

        var window = Window.GetWindow(this);
        var theme = window?.DataContext is ViewModels.MainWindowViewModel mainVm
            ? mainVm.SelectedTheme
            : ThemeManager.CurrentTheme;
        ThemedDialogWindow.ShowOk(window, theme, translatedTitle, translatedMessage);
    }

    private void OnPickManagedRewardColorClicked(object sender, RoutedEventArgs e)
    {
        if (sender is not Button button)
        {
            return;
        }

        var isCooldownColor = string.Equals(button.Tag?.ToString(), "Cooldown", StringComparison.OrdinalIgnoreCase);
        var fallbackColor = isCooldownColor
            ? ManagedRewardPresentation.InUseBackgroundColor
            : ManagedRewardPresentation.ReadyBackgroundColor;

        var rule = FindRuleFromButton(button);
        if (rule is null)
        {
            return;
        }

        var initialColor = isCooldownColor
            ? rule.ManagedRewardCooldownColor
            : rule.ManagedRewardReadyColor;

        using var dialog = new WinForms.ColorDialog
        {
            AllowFullOpen = true,
            FullOpen = true,
            AnyColor = true,
            SolidColorOnly = false,
            Color = ManagedRewardPresentation.ToDrawingColor(initialColor, fallbackColor)
        };

        var owner = Window.GetWindow(this);
        var ownerHandle = owner is not null
            ? new System.Windows.Interop.WindowInteropHelper(owner).Handle
            : IntPtr.Zero;
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
            rule.ManagedRewardCooldownColor = selectedColor;
        }
        else
        {
            rule.ManagedRewardReadyColor = selectedColor;
        }
    }

    private void OnAddSupporterFloatAddRangeClicked(object sender, RoutedEventArgs e)
    {
        if (DataContext is not TriggerRule rule)
        {
            return;
        }

        rule.SupporterFloatAddRanges.Add(new SupporterFloatAddRange());
    }

    private void OnRemoveSupporterFloatAddRangeClicked(object sender, RoutedEventArgs e)
    {
        if (sender is not FrameworkElement { DataContext: SupporterFloatAddRange range }
            || DataContext is not TriggerRule rule
            || rule.SupporterFloatAddRanges.Count <= 1)
        {
            return;
        }

        rule.SupporterFloatAddRanges.Remove(range);
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
            candidate = System.Windows.Media.VisualTreeHelper.GetParent(candidate);
        }
        return null;
    }

    private sealed class NativeWin32Window : WinForms.IWin32Window
    {
        public NativeWin32Window(IntPtr handle) => Handle = handle;
        public IntPtr Handle { get; }
    }
}
