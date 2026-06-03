using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using VrcTwitchOscBridge.Models;
using VrcTwitchOscBridge.Services;
using VrcTwitchOscBridge.ViewModels;

namespace VrcTwitchOscBridge;

public partial class BuiltInCommandsWindow : Window
{
    private readonly MainWindowViewModel viewModel;

    public BuiltInCommandsWindow(AppTheme theme, MainWindowViewModel viewModel)
    {
        InitializeComponent();
        ThemeManager.ApplyToResources(Resources, theme);
        ThemeManager.ThemeChanged += OnThemeManagerThemeChanged;
        Closed += OnWindowClosed;
        DataContext = viewModel;
        this.viewModel = viewModel;
        SelectWorldCommand();
    }

    private void OnWorldCommandButtonClicked(object sender, RoutedEventArgs e) => SelectWorldCommand();

    private void OnTriggerInfoCommandButtonClicked(object sender, RoutedEventArgs e) => SelectTriggerInfoCommand();

    private void OnChatControlCommandButtonClicked(object sender, RoutedEventArgs e) => SelectChatControl();

    private void SelectWorldCommand()
    {
        WorldCommandPanel.Visibility = Visibility.Visible;
        TriggerInfoCommandPanel.Visibility = Visibility.Collapsed;
        ChatControlCommandPanel.Visibility = Visibility.Collapsed;
        WorldCommandButton.Opacity = 1;
        TriggerInfoCommandButton.Opacity = 0.72;
        ChatControlCommandButton.Opacity = 0.72;
    }

    private void SelectTriggerInfoCommand()
    {
        WorldCommandPanel.Visibility = Visibility.Collapsed;
        TriggerInfoCommandPanel.Visibility = Visibility.Visible;
        ChatControlCommandPanel.Visibility = Visibility.Collapsed;
        WorldCommandButton.Opacity = 0.72;
        TriggerInfoCommandButton.Opacity = 1;
        ChatControlCommandButton.Opacity = 0.72;
    }

    private void SelectChatControl()
    {
        WorldCommandPanel.Visibility = Visibility.Collapsed;
        TriggerInfoCommandPanel.Visibility = Visibility.Collapsed;
        ChatControlCommandPanel.Visibility = Visibility.Visible;
        WorldCommandButton.Opacity = 0.72;
        TriggerInfoCommandButton.Opacity = 0.72;
        ChatControlCommandButton.Opacity = 1;
    }

    private void OnAddRedeemGroupClicked(object sender, RoutedEventArgs e)
    {
        var dialog = new RedeemGroupEditorDialog(ThemeManager.CurrentTheme, viewModel, null);
        dialog.ShowDialog();
    }

    private void OnEditRedeemGroupClicked(object sender, RoutedEventArgs e)
    {
        if (sender is Button button && button.Tag is RedeemGroup group)
        {
            var dialog = new RedeemGroupEditorDialog(ThemeManager.CurrentTheme, viewModel, group);
            dialog.ShowDialog();
        }
    }

    private void OnRemoveRedeemGroupClicked(object sender, RoutedEventArgs e)
    {
        if (sender is Button button && button.Tag is RedeemGroup group)
        {
            viewModel.RemoveRedeemGroup(group);
        }
    }

    private void OnCloseButtonClicked(object sender, RoutedEventArgs e) => DialogResult = true;

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
}
