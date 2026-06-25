using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Input;
using System.Windows.Media;
using VrcTwitchOscBridge.Models;
using VrcTwitchOscBridge.Services;

namespace VrcTwitchOscBridge;

public partial class BugReportWindow : Window
{
    private static readonly string[] CategoryKeys =
        ["connection", "rewards", "scaling", "movement", "ui-theme", "crash", "other"];

    private readonly string snapshot;
    private readonly string? activityLogSection;
    private readonly string? debugLogSection;
    private readonly string? crashLogSection;
    private readonly string appVersion;
    private readonly AppTheme currentTheme;

    public BugReportWindow(
        AppTheme theme,
        bool hasCrashLog = true,
        string? presetCategory = null,
        string? presetTitle = null,
        string snapshot = "",
        string? activityLogSection = null,
        string? debugLogSection = null,
        string? crashLogSection = null,
        string? appVersion = null)
    {
        this.snapshot = snapshot;
        this.activityLogSection = activityLogSection;
        this.debugLogSection = debugLogSection;
        this.crashLogSection = crashLogSection;
        this.appVersion = appVersion ?? string.Empty;
        currentTheme = theme;

        InitializeComponent();
        ThemeManager.ApplyToResources(Resources, theme);
        ThemeManager.ThemeChanged += OnThemeManagerThemeChanged;
        Closed += OnWindowClosed;

        SnapshotTextBox.Text = snapshot;

        if (!hasCrashLog)
        {
            CrashLogCheckBox.Visibility = Visibility.Collapsed;
        }

        if (!string.IsNullOrEmpty(presetCategory))
        {
            var index = Array.IndexOf(CategoryKeys, presetCategory);
            if (index >= 0)
            {
                CategoryComboBox.SelectedIndex = index;
            }
        }

        if (!string.IsNullOrEmpty(presetTitle))
        {
            TitleTextBox.Text = presetTitle;
        }

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

    public string Category => (CategoryComboBox.SelectedItem is ComboBoxItem item ? item.Tag?.ToString() : null) ?? "other";

    public string Severity => (SeverityComboBox.SelectedItem is ComboBoxItem item ? item.Tag?.ToString() : null) ?? "normal";

    public bool IncludeActivityLog => ActivityLogCheckBox.IsChecked == true;

    public bool IncludeDebugLog => DebugLogCheckBox.IsChecked == true;

    public bool IncludeCrashLog => CrashLogCheckBox.IsChecked == true && CrashLogCheckBox.Visibility == Visibility.Visible;

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

    private void OnPreviewClicked(object sender, RoutedEventArgs e)
    {
        var preview = BugReportPreviewBuilder.Build(
            BugTitle,
            Category,
            Severity,
            WhatHappened,
            ExpectedBehavior,
            StepsToReproduce,
            ContactName,
            appVersion,
            snapshot,
            IncludeActivityLog ? activityLogSection : null,
            IncludeDebugLog ? debugLogSection : null,
            IncludeCrashLog ? crashLogSection : null);

        var previewWindow = new BugReportPreviewWindow(preview, currentTheme)
        {
            Owner = this
        };
        previewWindow.ShowDialog();
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
