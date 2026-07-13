using System.Windows;
using System.Windows.Input;
using VrcTwitchOscBridge.Models;
using VrcTwitchOscBridge.Services;

namespace VrcTwitchOscBridge;

public partial class ThemedInputDialog : Window
{
    public static readonly string CancelText = LocalizationService.Translate("Cancel");

    private ThemedInputDialog(
        AppTheme theme,
        string title,
        string label,
        string primaryButtonText,
        bool showColor)
    {
        InitializeComponent();
        ThemeManager.ApplyToResources(Resources, theme);
        ThemeManager.ThemeChanged += OnThemeManagerThemeChanged;
        Closed += OnWindowClosed;

        Title = LocalizationService.Format("{0} | Crystal Relay", title);
        WindowTitleTextBlock.Text = title;
        HeaderLabel.Text = label;
        PrimaryButton.Content = primaryButtonText;

        ColorPanel.Visibility = showColor ? Visibility.Visible : Visibility.Collapsed;
        if (showColor)
        {
            ColorBox.Text = "#A855F7";
        }

        Loaded += (s, e) => InputBox.Focus();
    }

    public string InputValue => InputBox.Text;
    public string ColorValue => ColorBox.Text;

    public static string? ShowPrompt(
        Window owner,
        AppTheme theme,
        string title,
        string label,
        string primaryButtonText)
    {
        var dialog = new ThemedInputDialog(theme, title, label, primaryButtonText, showColor: false)
        {
            Owner = owner
        };
        return dialog.ShowDialog() == true ? dialog.InputValue : null;
    }

    public static (string? name, string? color) ShowPromptWithColor(
        Window owner,
        AppTheme theme,
        string title,
        string nameLabel,
        string colorLabel,
        string primaryButtonText)
    {
        var dialog = new ThemedInputDialog(theme, title, nameLabel, primaryButtonText, showColor: true)
        {
            Owner = owner
        };
        dialog.ColorLabel.Text = colorLabel;
        return dialog.ShowDialog() == true ? (dialog.InputValue, dialog.ColorValue) : (null, null);
    }

    private void OnPrimaryButtonClicked(object sender, RoutedEventArgs e)
    {
        if (string.IsNullOrWhiteSpace(InputBox.Text))
        {
            return;
        }
        DialogResult = true;
        Close();
    }

    private void OnCancelButtonClicked(object sender, RoutedEventArgs e)
    {
        DialogResult = false;
        Close();
    }

    private void OnCloseButtonClicked(object sender, RoutedEventArgs e)
    {
        DialogResult = false;
        Close();
    }

    private void OnTitleBarMouseDown(object sender, MouseButtonEventArgs e)
    {
        if (e.ChangedButton == MouseButton.Left)
        {
            try { DragMove(); } catch { }
        }
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
}
