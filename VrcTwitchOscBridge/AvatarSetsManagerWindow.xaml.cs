using System;
using System.Windows;
using System.Windows.Input;
using VrcTwitchOscBridge.Services;
using VrcTwitchOscBridge.ViewModels;

namespace VrcTwitchOscBridge;

public partial class AvatarSetsManagerWindow : Window
{
    public AvatarSetsManagerWindow()
    {
        InitializeComponent();
        ThemeManager.ThemeChanged += OnThemeManagerThemeChanged;
        Closed += OnWindowClosed;
    }

    private AvatarSetsManagerViewModel? Vm => DataContext as AvatarSetsManagerViewModel;

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

    private void OnEditorBackdropClicked(object sender, MouseButtonEventArgs e)
    {
        if (Vm?.CloseEditorCommand.CanExecute(null) == true)
        {
            Vm.CloseEditorCommand.Execute(null);
        }
    }

    private void OnDeleteRuleClicked(object sender, RoutedEventArgs e)
    {
        if (sender is System.Windows.Controls.Button btn && btn.Tag is Models.TriggerRule rule && Vm != null)
        {
            Vm.DeleteChannelPointRuleCommand.Execute(rule);
        }
    }

    private void OnDeleteOutfitClicked(object sender, RoutedEventArgs e)
    {
        if (sender is System.Windows.Controls.Button btn && btn.Tag is Models.WardrobeOutfit outfit && Vm != null)
        {
            Vm.DeleteWardrobeOutfitCommand.Execute(outfit);
        }
    }

    private void OnRuleItemClicked(object sender, MouseButtonEventArgs e)
    {
        if (sender is System.Windows.Controls.Border border && border.Tag is Models.TriggerRule rule && Vm != null)
        {
            Vm.SelectedAvatarRule = rule;
            e.Handled = true;
        }
    }

    private void OnOutfitItemClicked(object sender, MouseButtonEventArgs e)
    {
        if (sender is System.Windows.Controls.Border border && border.Tag is Models.WardrobeOutfit outfit && Vm != null)
        {
            Vm.SelectedWardrobeOutfit = outfit;
            e.Handled = true;
        }
    }

    private void OnWardrobeParamListSelectionChanged(object sender, System.Windows.Controls.SelectionChangedEventArgs e)
    {
        if (sender is not System.Windows.Controls.ListBox lb || Vm is null) return;
        Vm.SyncWardrobeParamSelectionFromList(lb.SelectedItems.Cast<Models.WardrobeSnapshotParam>());
    }

    private void OnWardrobeParamListKeyDown(object sender, System.Windows.Input.KeyEventArgs e)
    {
        // Reserved for future custom shortcuts. Intentionally empty so the
        // ListBox.InputBindings (Ctrl+C/V/A, Delete, Esc) get first crack.
    }

    private void OnHideModeClicked(object sender, MouseButtonEventArgs e)
    {
        if (Vm?.SelectedAvatarRule is Models.TriggerRule rule)
        {
            rule.SpecialRulePairingMode = Models.SpecialRulePairingMode.HidePairedWhileActive;
            e.Handled = true;
        }
    }

    private void OnShowModeClicked(object sender, MouseButtonEventArgs e)
    {
        if (Vm?.SelectedAvatarRule is Models.TriggerRule rule)
        {
            rule.SpecialRulePairingMode = Models.SpecialRulePairingMode.ShowPairedWhileActive;
            e.Handled = true;
        }
    }

    private void OnParameterNameKeyUp(object sender, System.Windows.Input.KeyEventArgs e)
    {
        if (Vm == null) return;
        if (sender is System.Windows.Controls.ComboBox combo && combo.Text is string text)
        {
            Vm.ParameterNameFilter = text;
        }
    }

    private void OnParameterNameTextChanged(object sender, System.Windows.Controls.TextChangedEventArgs e)
    {
        if (Vm == null) return;
        if (sender is System.Windows.Controls.TextBox tb && tb.Text is string text)
        {
            Vm.ParameterNameFilter = text;
        }
    }

    private void OnWardrobeParameterNameTextChanged(object sender, System.Windows.Controls.TextChangedEventArgs e)
    {
        if (Vm == null) return;
        if (sender is System.Windows.Controls.TextBox tb && tb.Text is string text)
        {
            Vm.WardrobeParameterNameFilter = text;
        }
    }

    private void OnParameterItemClicked(object sender, RoutedEventArgs e)
    {
        if (Vm?.SelectedAvatarRule is Models.TriggerRule rule &&
            sender is System.Windows.Controls.Button btn &&
            btn.Tag is Models.VrChatOscParameterSummary p)
        {
            rule.ParameterName = p.Name;
            // Also auto-set the parameter type to match
            rule.ParameterType = p.ParameterType;
            // Clear the search filter so the user sees the change
            Vm.ParameterNameFilter = string.Empty;
        }
    }

    private void OnWardrobeParameterItemClicked(object sender, RoutedEventArgs e)
    {
        if (Vm?.SelectedWardrobeSnapshotParam is not { } param ||
            sender is not System.Windows.Controls.Button btn ||
            btn.Tag is not Models.VrChatOscParameterSummary p)
        {
            return;
        }

        param.ParameterName = p.Name;
        param.ParameterType = p.ParameterType;
        Vm.WardrobeParameterNameFilter = string.Empty;
    }

    private void OnParameterValueTrueClicked(object sender, RoutedEventArgs e)
    {
        if (sender is System.Windows.Controls.Button btn && btn.Tag is Models.TriggerRule rule)
        {
            rule.ParameterValue = "True";
            e.Handled = true;
        }
    }

    private void OnParameterValueFalseClicked(object sender, RoutedEventArgs e)
    {
        if (sender is System.Windows.Controls.Button btn && btn.Tag is Models.TriggerRule rule)
        {
            rule.ParameterValue = "False";
            e.Handled = true;
        }
    }

    private void OnResetValueTrueClicked(object sender, RoutedEventArgs e)
    {
        if (sender is System.Windows.Controls.Button btn && btn.Tag is Models.TriggerRule rule)
        {
            rule.ResetValue = "True";
            e.Handled = true;
        }
    }

    private void OnResetValueFalseClicked(object sender, RoutedEventArgs e)
    {
        if (sender is System.Windows.Controls.Button btn && btn.Tag is Models.TriggerRule rule)
        {
            rule.ResetValue = "False";
            e.Handled = true;
        }
    }

    private void OnPickReadyColorClicked(object sender, RoutedEventArgs e)
    {
        if (sender is System.Windows.Controls.Button btn && btn.Tag is Models.TriggerRule rule)
        {
            PickColorAndApply(rule.ManagedRewardReadyColor, color => rule.ManagedRewardReadyColor = color);
            e.Handled = true;
        }
    }

    private void OnResetReadyColorClicked(object sender, RoutedEventArgs e)
    {
        if (sender is System.Windows.Controls.Button btn && btn.Tag is Models.TriggerRule rule)
        {
            rule.ManagedRewardReadyColor = Services.ManagedRewardPresentation.ReadyBackgroundColor;
            e.Handled = true;
        }
    }

    private void OnPickCooldownColorClicked(object sender, RoutedEventArgs e)
    {
        if (sender is System.Windows.Controls.Button btn && btn.Tag is Models.TriggerRule rule)
        {
            PickColorAndApply(rule.ManagedRewardCooldownColor, color => rule.ManagedRewardCooldownColor = color);
            e.Handled = true;
        }
    }

    private void OnResetCooldownColorClicked(object sender, RoutedEventArgs e)
    {
        if (sender is System.Windows.Controls.Button btn && btn.Tag is Models.TriggerRule rule)
        {
            rule.ManagedRewardCooldownColor = Services.ManagedRewardPresentation.InUseBackgroundColor;
            e.Handled = true;
        }
    }

    private void OnPickWardrobeReadyColorClicked(object sender, RoutedEventArgs e)
    {
        if (sender is System.Windows.Controls.Button btn && btn.Tag is Models.WardrobeOutfit outfit)
        {
            PickColorAndApply(outfit.ManagedRewardReadyColor, color => outfit.ManagedRewardReadyColor = color);
            e.Handled = true;
        }
    }

    private void OnResetWardrobeReadyColorClicked(object sender, RoutedEventArgs e)
    {
        if (sender is System.Windows.Controls.Button btn && btn.Tag is Models.WardrobeOutfit outfit)
        {
            outfit.ManagedRewardReadyColor = Services.ManagedRewardPresentation.ReadyBackgroundColor;
            e.Handled = true;
        }
    }

    private void OnPickWardrobeCooldownColorClicked(object sender, RoutedEventArgs e)
    {
        if (sender is System.Windows.Controls.Button btn && btn.Tag is Models.WardrobeOutfit outfit)
        {
            PickColorAndApply(outfit.ManagedRewardCooldownColor, color => outfit.ManagedRewardCooldownColor = color);
            e.Handled = true;
        }
    }

    private void OnResetWardrobeCooldownColorClicked(object sender, RoutedEventArgs e)
    {
        if (sender is System.Windows.Controls.Button btn && btn.Tag is Models.WardrobeOutfit outfit)
        {
            outfit.ManagedRewardCooldownColor = Services.ManagedRewardPresentation.InUseBackgroundColor;
            e.Handled = true;
        }
    }

    private void OnWardrobeIntValuePreviewTextInput(object sender, System.Windows.Input.TextCompositionEventArgs e)
    {
        e.Handled = !IsValidIntegerText(e.Text);
    }

    private void OnWardrobeIntValueLostFocus(object sender, RoutedEventArgs e)
    {
        if (sender is not System.Windows.Controls.TextBox textBox) return;
        if (string.IsNullOrWhiteSpace(textBox.Text) || !int.TryParse(textBox.Text, out _))
        {
            textBox.Text = "0";
        }
    }

    private void OnWardrobeFloatValuePreviewTextInput(object sender, System.Windows.Input.TextCompositionEventArgs e)
    {
        e.Handled = !IsValidFloatText(e.Text);
    }

    private void OnWardrobeFloatValueLostFocus(object sender, RoutedEventArgs e)
    {
        if (sender is not System.Windows.Controls.TextBox textBox) return;
        if (string.IsNullOrWhiteSpace(textBox.Text) || !double.TryParse(textBox.Text, out _))
        {
            textBox.Text = "0.0";
        }
    }

    private void OnWardrobeIntValuePasting(object sender, DataObjectPastingEventArgs e)
    {
        if (e.DataObject.GetDataPresent(typeof(string)) &&
            e.DataObject.GetData(typeof(string)) is string text &&
            !IsValidIntegerText(text))
        {
            e.CancelCommand();
        }
    }

    private void OnWardrobeFloatValuePasting(object sender, DataObjectPastingEventArgs e)
    {
        if (e.DataObject.GetDataPresent(typeof(string)) &&
            e.DataObject.GetData(typeof(string)) is string text &&
            !IsValidFloatText(text))
        {
            e.CancelCommand();
        }
    }

    private static bool IsValidIntegerText(string text)
    {
        foreach (var c in text)
        {
            if (c is >= '0' and <= '9' or '-' or '+') continue;
            return false;
        }
        return true;
    }

    private static bool IsValidFloatText(string text)
    {
        foreach (var c in text)
        {
            if (c is >= '0' and <= '9' or '-' or '+' or '.') continue;
            return false;
        }
        return true;
    }

    private void PickColorAndApply(string currentHex, Action<string> apply)
    {
        using var dialog = new System.Windows.Forms.ColorDialog
        {
            AllowFullOpen = true,
            AnyColor = true,
            FullOpen = true,
            Color = Services.ManagedRewardPresentation.ToDrawingColor(
                currentHex,
                Services.ManagedRewardPresentation.ReadyBackgroundColor),
            CustomColors = new[]
            {
                0x22C55E, 0xEF4444, 0x3B82F6, 0xF59E0B, 0xA855F7, 0xEC4899, 0x14B8A6, 0x6B7280
            }
        };

        if (dialog.ShowDialog() == System.Windows.Forms.DialogResult.OK)
        {
            apply(Services.ManagedRewardPresentation.ToHex(dialog.Color));
        }
    }

    private void OnThemeManagerThemeChanged(object? sender, EventArgs e)
    {
        Dispatcher.BeginInvoke(() => ThemeManager.ApplyToResources(Resources));
    }

    private void OnWindowClosed(object? sender, EventArgs e)
    {
        ThemeManager.ThemeChanged -= OnThemeManagerThemeChanged;
        Closed -= OnWindowClosed;
    }
}
