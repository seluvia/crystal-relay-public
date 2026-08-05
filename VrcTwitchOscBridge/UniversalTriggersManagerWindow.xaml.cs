using System;
using System.Windows;
using System.Windows.Input;
using System.Windows.Interop;
using System.Windows.Media;
using VrcTwitchOscBridge.Models;
using VrcTwitchOscBridge.Services;
using VrcTwitchOscBridge.ViewModels;

namespace VrcTwitchOscBridge;

public partial class UniversalTriggersManagerWindow : Window
{
    public UniversalTriggersManagerWindow(UniversalTriggersManagerViewModel viewModel)
    {
        DataContext = viewModel ?? throw new ArgumentNullException(nameof(viewModel));
        InitializeComponent();
        ApplyUniversalTriggerThemeResources();
        ThemeManager.ThemeChanged += OnThemeManagerThemeChanged;
        Closed += OnWindowClosed;
    }

    private UniversalTriggersManagerViewModel Vm => (UniversalTriggersManagerViewModel)DataContext;

    private void OnTitleBarMouseDown(object sender, MouseButtonEventArgs e)
    {
        if (e.ChangedButton == MouseButton.Left && e.ButtonState == MouseButtonState.Pressed)
        {
            try
            {
                DragMove();
            }
            catch (InvalidOperationException)
            {
            }
        }
    }

    private void OnCloseClicked(object sender, RoutedEventArgs e) => Close();

    private void OnToggleChatSection(object sender, MouseButtonEventArgs e) => Vm.IsChatSectionCollapsed = !Vm.IsChatSectionCollapsed;

    private void OnToggleRewardSection(object sender, MouseButtonEventArgs e) => Vm.IsRewardSectionCollapsed = !Vm.IsRewardSectionCollapsed;

    private void OnToggleBitsSection(object sender, MouseButtonEventArgs e) => Vm.IsBitsSectionCollapsed = !Vm.IsBitsSectionCollapsed;

    private void OnToggleSubsSection(object sender, MouseButtonEventArgs e) => Vm.IsSubsSectionCollapsed = !Vm.IsSubsSectionCollapsed;

    private void OnToggleFollowsSection(object sender, MouseButtonEventArgs e) => Vm.IsFollowsSectionCollapsed = !Vm.IsFollowsSectionCollapsed;

    private void OnEditorBackdropClicked(object sender, MouseButtonEventArgs e)
    {
        if (Vm.CloseEditorCommand.CanExecute(null))
        {
            Vm.CloseEditorCommand.Execute(null);
        }
    }

    private void OnPickManagedRewardColorClicked(object sender, RoutedEventArgs e)
    {
        if (sender is not System.Windows.Controls.Button button) return;
        if (Vm.SelectedTrigger is null) return;

        var isCooldownColor = string.Equals(button.Tag as string, "Cooldown", StringComparison.OrdinalIgnoreCase);
        var initialColor = isCooldownColor
            ? Vm.SelectedTrigger.ManagedRewardCooldownColor
            : Vm.SelectedTrigger.ManagedRewardReadyColor;

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
            Vm.SelectedTrigger.ManagedRewardCooldownColor = selectedColor;
        else
            Vm.SelectedTrigger.ManagedRewardReadyColor = selectedColor;
    }

    private void OnThemeManagerThemeChanged(object? sender, EventArgs e)
    {
        Dispatcher.BeginInvoke(ApplyUniversalTriggerThemeResources);
    }

    private void ApplyUniversalTriggerThemeResources()
    {
        ThemeManager.ApplyToResources(Resources, ThemeManager.CurrentTheme);
        if (ThemeManager.CurrentTheme != AppTheme.VoidCrystal)
        {
            CopyUniversalTriggerBrushColor("ComboSurfaceBrush", "UniversalTriggerComboSurfaceBrush");
            CopyUniversalTriggerBrushColor("ComboTextBrush", "UniversalTriggerComboTextBrush");
            CopyUniversalTriggerBrushColor("ComboHighlightBrush", "UniversalTriggerComboHighlightBrush");
            CopyUniversalTriggerBrushColor("ComboDropButtonBrush", "UniversalTriggerComboDropButtonBrush");
            CopyUniversalTriggerBrushColor("ComboDropButtonHoverBrush", "UniversalTriggerComboDropButtonHoverBrush");
            CopyUniversalTriggerBrushColor("ComboDropButtonPressedBrush", "UniversalTriggerComboDropButtonPressedBrush");
            return;
        }

        SetUniversalTriggerBrushColor("UniversalTriggerComboSurfaceBrush", "#2F1A4A");
        SetUniversalTriggerBrushColor("UniversalTriggerComboTextBrush", "#F8F1FF");
        SetUniversalTriggerBrushColor("UniversalTriggerComboHighlightBrush", "#5A2E8A");
        SetUniversalTriggerBrushColor("UniversalTriggerComboDropButtonBrush", "#3A2160");
        SetUniversalTriggerBrushColor("UniversalTriggerComboDropButtonHoverBrush", "#6F3FB0");
        SetUniversalTriggerBrushColor("UniversalTriggerComboDropButtonPressedBrush", "#824DCF");
    }

    private void CopyUniversalTriggerBrushColor(string sourceKey, string targetKey)
    {
        if (Resources[sourceKey] is SolidColorBrush sourceBrush)
        {
            Resources[targetKey] = new SolidColorBrush(sourceBrush.Color);
        }
    }

    private void SetUniversalTriggerBrushColor(string resourceKey, string hexColor)
    {
        var color = (Color)ColorConverter.ConvertFromString(hexColor)!;
        if (Resources[resourceKey] is SolidColorBrush brush && !brush.IsFrozen)
        {
            brush.Color = color;
            return;
        }

        Resources[resourceKey] = new SolidColorBrush(color);
    }

    private void OnWindowClosed(object? sender, EventArgs e)
    {
        ThemeManager.ThemeChanged -= OnThemeManagerThemeChanged;
        Closed -= OnWindowClosed;
        if (DataContext is UniversalTriggersManagerViewModel viewModel
            && viewModel.CloseEditorCommand.CanExecute(null))
        {
            viewModel.CloseEditorCommand.Execute(null);
        }
        if (DataContext is IDisposable disposableDataContext)
        {
            disposableDataContext.Dispose();
        }
    }

    private sealed class NativeWin32Window(IntPtr handle) : System.Windows.Forms.IWin32Window
    {
        public IntPtr Handle => handle;
    }
}
