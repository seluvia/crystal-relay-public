using System;
using System.Windows;
using System.Windows.Input;
using System.Windows.Interop;
using VrcTwitchOscBridge.Models;
using VrcTwitchOscBridge.Services;
using VrcTwitchOscBridge.ViewModels;
using WinForms = System.Windows.Forms;

namespace VrcTwitchOscBridge;

public partial class AvatarScalingManagerWindow : Window
{
    public AvatarScalingManagerWindow(AvatarScalingManagerViewModel viewModel)
    {
        DataContext = viewModel ?? throw new ArgumentNullException(nameof(viewModel));
        InitializeComponent();
        ThemeManager.ApplyToResources(Resources, ThemeManager.CurrentTheme);
        ThemeManager.ThemeChanged += OnThemeManagerThemeChanged;
        Closed += OnWindowClosed;
        viewModel.PropertyChanged += OnViewModelPropertyChanged;
        UpdateColumnLayout();
    }

    private static readonly GridLength ZeroGrid = new GridLength(0);
    private static readonly GridLength StarGrid = new GridLength(1, GridUnitType.Star);
    private static readonly GridLength SpacerGrid = new GridLength(12);

    private void OnViewModelPropertyChanged(object? sender, System.ComponentModel.PropertyChangedEventArgs e)
    {
        if (e.PropertyName == nameof(AvatarScalingManagerViewModel.ActiveSourceView))
        {
            UpdateColumnLayout();
        }
    }

    private void UpdateColumnLayout()
    {
        var vm = Vm;
        ChannelPointColumn.Width = vm.IsChannelPointViewActive ? StarGrid : ZeroGrid;
        PaySystemSpacerColumn.Width = (vm.IsChannelPointViewActive && vm.IsPaySystemViewActive) ? SpacerGrid : ZeroGrid;
        PaySystemColumn.Width = vm.IsPaySystemViewActive ? StarGrid : ZeroGrid;
    }

    private AvatarScalingManagerViewModel Vm => (AvatarScalingManagerViewModel)DataContext;

    private void OnTitleBarMouseDown(object sender, MouseButtonEventArgs e)
    {
        if (e.ChangedButton != MouseButton.Left || e.ButtonState != MouseButtonState.Pressed)
        {
            return;
        }

        try
        {
            DragMove();
        }
        catch (InvalidOperationException)
        {
        }
    }

    private void OnCloseClicked(object sender, RoutedEventArgs e) => Close();

    private void ScaleActionModeButton_Click(object sender, RoutedEventArgs e)
    {
        if (sender is not FrameworkElement { Tag: string tagName })
        {
            return;
        }

        if (!Enum.TryParse<AvatarScaleMode>(tagName, out var mode))
        {
            return;
        }

        if (Vm.ActiveScaleAction is { } rule)
        {
            rule.ScaleMode = mode;
        }
    }

    private void ScaleActionMultOpButton_Click(object sender, RoutedEventArgs e)
    {
        if (Vm.ActiveScaleAction is not { } rule)
        {
            return;
        }

        rule.MultiplierDirectionId = rule.MultiplierDirection == AvatarScaleMultiplierDirection.Grow
            ? (int)AvatarScaleMultiplierDirection.Divide
            : (int)AvatarScaleMultiplierDirection.Grow;
    }

    private void ScaleActionRelHeightOpButton_Click(object sender, RoutedEventArgs e)
    {
        if (Vm.ActiveScaleAction is not { } rule)
        {
            return;
        }

        rule.RelativeHeightDirectionId = rule.IsSubtractRelativeHeight
            ? (int)AvatarScaleRelativeHeightDirection.Add
            : (int)AvatarScaleRelativeHeightDirection.Subtract;
    }

    private void OnPickManagedRewardColorClicked(object sender, RoutedEventArgs e)
    {
        if (sender is not FrameworkElement { Tag: string tagName }
            || (Vm.SelectedAvatarScaleRule is null && Vm.SelectedCard?.MasterReward is null))
        {
            return;
        }

        var isCooldownColor = string.Equals(tagName, "Cooldown", StringComparison.OrdinalIgnoreCase);
        var fallbackColor = isCooldownColor
            ? ManagedRewardPresentation.InUseBackgroundColor
            : ManagedRewardPresentation.ReadyBackgroundColor;
        var childRule = Vm.SelectedAvatarScaleRule;
        var masterReward = Vm.SelectedCard?.MasterReward;
        var initialColor = childRule is not null
            ? isCooldownColor ? childRule.ManagedRewardCooldownColor : childRule.ManagedRewardReadyColor
            : isCooldownColor ? masterReward!.ManagedRewardCooldownColor : masterReward!.ManagedRewardReadyColor;

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
        if (childRule is not null)
        {
            if (isCooldownColor)
            {
                childRule.ManagedRewardCooldownColor = selectedColor;
            }
            else
            {
                childRule.ManagedRewardReadyColor = selectedColor;
            }
        }
        else if (masterReward is not null && isCooldownColor)
        {
            masterReward.ManagedRewardCooldownColor = selectedColor;
        }
        else if (masterReward is not null)
        {
            masterReward.ManagedRewardReadyColor = selectedColor;
        }
    }

    private void OnAddSupporterGrowthBitRangeClicked(object sender, RoutedEventArgs e)
    {
        if (Vm.SelectedAvatarScaleRule is { } rule)
        {
            rule.SupporterGrowthBitRanges.Add(new AvatarScaleBitGrowthRange());
        }
    }

    private void OnRemoveSupporterGrowthBitRangeClicked(object sender, RoutedEventArgs e)
    {
        if (sender is not FrameworkElement { DataContext: AvatarScaleBitGrowthRange range }
            || Vm.SelectedAvatarScaleRule is not { } rule
            || rule.SupporterGrowthBitRanges.Count <= 1)
        {
            return;
        }

        rule.SupporterGrowthBitRanges.Remove(range);
    }

    private void OnThemeManagerThemeChanged(object? sender, EventArgs e)
    {
        Dispatcher.BeginInvoke(() => ThemeManager.ApplyToResources(Resources));
    }

    private void OnWindowClosed(object? sender, EventArgs e)
    {
        ThemeManager.ThemeChanged -= OnThemeManagerThemeChanged;
        Closed -= OnWindowClosed;
        if (DataContext is AvatarScalingManagerViewModel vm)
        {
            vm.PropertyChanged -= OnViewModelPropertyChanged;
        }
        if (DataContext is IDisposable disposableDataContext)
        {
            disposableDataContext.Dispose();
        }
    }

    private sealed class NativeWin32Window(IntPtr handle) : WinForms.IWin32Window
    {
        public IntPtr Handle { get; } = handle;
    }
}
