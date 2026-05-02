using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using VrcTwitchOscBridge.Models;
using VrcTwitchOscBridge.Services;

namespace VrcTwitchOscBridge;

public partial class BugReportWindow : Window
{
    public BugReportWindow(AppTheme theme)
    {
        InitializeComponent();
        ThemeManager.ApplyToResources(Resources, theme);
        ThemeManager.ThemeChanged += OnThemeManagerThemeChanged;
        Closed += OnWindowClosed;
        Loaded += (_, _) =>
        {
            TitleTextBox.Focus();
            TitleTextBox.SelectAll();
        };
    }

    public string BugTitle => TitleTextBox.Text.Trim();

    public string WhatHappened => WhatHappenedTextBox.Text.Trim();

    public string ExpectedBehavior => ExpectedTextBox.Text.Trim();

    public string StepsToReproduce => StepsTextBox.Text.Trim();

    public string ContactName => ContactTextBox.Text.Trim();

    public bool IncludeSanitizedLogs => IncludeLogsCheckBox.IsChecked == true;

    private void OnWindowClosed(object? sender, EventArgs e)
    {
        ThemeManager.ThemeChanged -= OnThemeManagerThemeChanged;
        Closed -= OnWindowClosed;
    }

    private void OnThemeManagerThemeChanged(object? sender, EventArgs e)
    {
        Dispatcher.BeginInvoke(() => ThemeManager.ApplyToResources(Resources));
    }

    private void OnSendClicked(object sender, RoutedEventArgs e)
    {
        if (!ValidateForm(out var validationMessage))
        {
            ValidationTextBlock.Text = validationMessage;
            ValidationTextBlock.Visibility = Visibility.Visible;
            return;
        }

        DialogResult = true;
    }

    private void OnCancelClicked(object sender, RoutedEventArgs e) => DialogResult = false;

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

    private bool ValidateForm(out string validationMessage)
    {
        if (!IsWithinRequiredRange(BugTitle, 8, 120))
        {
            validationMessage = LocalizationService.Translate("Bug title must be 8 to 120 characters.");
            return false;
        }

        if (!IsWithinRequiredRange(WhatHappened, 20, 5000))
        {
            validationMessage = LocalizationService.Translate("What happened must be 20 to 5000 characters.");
            return false;
        }

        if (!IsWithinRequiredRange(ExpectedBehavior, 20, 5000))
        {
            validationMessage = LocalizationService.Translate("Expected behavior must be 20 to 5000 characters.");
            return false;
        }

        if (!IsWithinRequiredRange(StepsToReproduce, 20, 5000))
        {
            validationMessage = LocalizationService.Translate("Steps to reproduce must be 20 to 5000 characters.");
            return false;
        }

        validationMessage = string.Empty;
        return true;
    }

    private static bool IsWithinRequiredRange(string value, int minLength, int maxLength) =>
        value.Length >= minLength && value.Length <= maxLength;

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
