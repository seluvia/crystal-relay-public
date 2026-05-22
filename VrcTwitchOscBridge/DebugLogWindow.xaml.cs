using System.Text;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using VrcTwitchOscBridge.Models;
using VrcTwitchOscBridge.Services;

namespace VrcTwitchOscBridge;

public partial class DebugLogWindow : Window
{
    private const int MaxDisplayedLines = 500;
    private readonly List<string> visibleLines = [];

    public DebugLogWindow(AppTheme theme)
    {
        InitializeComponent();
        ThemeManager.ApplyToResources(Resources, theme);
        ThemeManager.ThemeChanged += OnThemeManagerThemeChanged;
        DebugLogService.EntryWritten += OnDebugLogEntryWritten;
        Closed += OnWindowClosed;
        Reload();
    }

    private void Reload()
    {
        visibleLines.Clear();
        visibleLines.AddRange(DebugLogService.ReadRecentLines(MaxDisplayedLines));
        RefreshLogText();
    }

    private void OnDebugLogEntryWritten(string entry)
    {
        Dispatcher.BeginInvoke(() =>
        {
            visibleLines.Add(entry);
            while (visibleLines.Count > MaxDisplayedLines)
            {
                visibleLines.RemoveAt(0);
            }

            RefreshLogText();
        });
    }

    private void RefreshLogText()
    {
        var builder = new StringBuilder();
        foreach (var line in visibleLines)
        {
            builder.AppendLine(line);
        }

        LogTextBox.Text = builder.ToString();
        LogTextBox.CaretIndex = LogTextBox.Text.Length;
        LogTextBox.ScrollToEnd();
    }

    private void OnRefreshClicked(object sender, RoutedEventArgs e) => Reload();

    private void OnCloseClicked(object sender, RoutedEventArgs e) => Close();

    private void OnWindowClosed(object? sender, EventArgs e)
    {
        DebugLogService.EntryWritten -= OnDebugLogEntryWritten;
        ThemeManager.ThemeChanged -= OnThemeManagerThemeChanged;
        Closed -= OnWindowClosed;
    }

    private void OnThemeManagerThemeChanged(object? sender, EventArgs e)
    {
        Dispatcher.BeginInvoke(() => ThemeManager.ApplyToResources(Resources));
    }

    private void OnTitleBarMouseLeftButtonDown(object sender, MouseButtonEventArgs e)
    {
        if (e.ChangedButton != MouseButton.Left || FindAncestor<Button>(e.OriginalSource as DependencyObject) is not null)
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
