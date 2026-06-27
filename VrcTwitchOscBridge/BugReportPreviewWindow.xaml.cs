using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using VrcTwitchOscBridge.Models;
using VrcTwitchOscBridge.Services;

namespace VrcTwitchOscBridge;

public partial class BugReportPreviewWindow : Window
{
    public BugReportPreviewWindow(string previewText, AppTheme theme)
    {
        InitializeComponent();
        ThemeManager.ApplyToResources(Resources, theme);
        ThemeManager.ThemeChanged += OnThemeManagerThemeChanged;
        Closed += OnWindowClosed;
        PreviewTextBox.Text = previewText;
        Loaded += (_, _) => PreviewTextBox.ScrollToHome();
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

    private void OnCloseButtonClicked(object sender, RoutedEventArgs e) => DialogResult = false;

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

    private static T? FindAncestor<T>(DependencyObject? current) where T : DependencyObject
    {
        while (current is not null)
        {
            if (current is T match)
            {
                return match;
            }

            current = VisualTreeHelper.GetParent(current);
        }

        return null;
    }
}
