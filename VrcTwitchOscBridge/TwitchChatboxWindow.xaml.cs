using System.Collections;
using System.Collections.Specialized;
using System.ComponentModel;
using System.Globalization;
using System.IO;
using System.Net.Http;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Documents;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Threading;
using VrcTwitchOscBridge.Services;
using VrcTwitchOscBridge.Models;
using VrcTwitchOscBridge.ViewModels;

namespace VrcTwitchOscBridge;

public partial class TwitchChatboxWindow : Window
{
    private const string ViewerNotificationAudioRelativePath = "Assets\\twitch-chat-viewer-notification.wav";
    private const int MinimumChatTextSize = 12;
    private const int MaximumChatTextSize = 40;
    private readonly MainWindowViewModel viewModel;
    private MediaPlayer? viewerNotificationPlayer;
    private string? viewerNotificationAudioTempPath;

    public TwitchChatboxWindow(MainWindowViewModel viewModel, AppTheme initialTheme)
    {
        InitializeComponent();
        this.viewModel = viewModel;
        DataContext = viewModel;
        Topmost = viewModel.Settings.ChatboxAlwaysOnTop;
        ThemeManager.ApplyToResources(Resources, initialTheme);
        UpdateWindowStateGlyph();
        ApplyChatboxStateFromSettings();

        ThemeManager.ThemeChanged += OnThemeManagerThemeChanged;
        ChatMessageInlinePresenter.DiagnosticWritten += OnChatInlineDiagnosticWritten;
        viewModel.PropertyChanged += OnViewModelPropertyChanged;
        viewModel.Settings.PropertyChanged += OnAppSettingsPropertyChanged;
        viewModel.ChatMessages.CollectionChanged += OnChatMessagesCollectionChanged;
        Loaded += OnLoaded;
        Closed += OnClosed;
        StateChanged += OnStateChanged;
    }

    private void OnLoaded(object sender, RoutedEventArgs e)
    {
        ApplyChatboxStateFromSettings();
        ScrollChatToBottom();
    }

    private void OnClosed(object? sender, EventArgs e)
    {
        ThemeManager.ThemeChanged -= OnThemeManagerThemeChanged;
        ChatMessageInlinePresenter.DiagnosticWritten -= OnChatInlineDiagnosticWritten;
        viewModel.PropertyChanged -= OnViewModelPropertyChanged;
        viewModel.Settings.PropertyChanged -= OnAppSettingsPropertyChanged;
        viewModel.ChatMessages.CollectionChanged -= OnChatMessagesCollectionChanged;
        Loaded -= OnLoaded;
        Closed -= OnClosed;
        StateChanged -= OnStateChanged;
        CleanupViewerNotificationAudio();
    }

    private void OnViewModelPropertyChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName == nameof(MainWindowViewModel.SelectedTheme))
        {
            ApplyTheme(viewModel.SelectedTheme);
            ApplyOverlayLayout(viewModel.Settings.ChatboxOverlayMode);
            RefreshVisibleChatNameColors();
        }
    }

    private void OnThemeManagerThemeChanged(object? sender, EventArgs e)
    {
        Dispatcher.BeginInvoke(() =>
        {
            ApplyTheme(ThemeManager.CurrentTheme);
            ApplyOverlayLayout(viewModel.Settings.ChatboxOverlayMode);
            RefreshVisibleChatNameColors();
        }, DispatcherPriority.Background);
    }

    private void OnAppSettingsPropertyChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName == nameof(AppSettings.Theme))
        {
            Dispatcher.BeginInvoke(() =>
            {
                ApplyTheme(viewModel.Settings.Theme);
                ApplyOverlayLayout(viewModel.Settings.ChatboxOverlayMode);
                RefreshVisibleChatNameColors();
            }, DispatcherPriority.Background);
            return;
        }

        if (e.PropertyName is nameof(AppSettings.ChatboxAlwaysOnTop)
            or nameof(AppSettings.ChatboxSettingsPanelOpen)
            or nameof(AppSettings.ChatboxOverlayMode))
        {
            Dispatcher.BeginInvoke(ApplyChatboxStateFromSettings, DispatcherPriority.Background);
        }
    }

    private void OnChatMessagesCollectionChanged(object? sender, NotifyCollectionChangedEventArgs e)
    {
        if (e.Action == NotifyCollectionChangedAction.Add)
        {
            Dispatcher.BeginInvoke(() =>
            {
                if (ShouldPlayViewerNotificationSound(e.NewItems))
                {
                    PlayViewerNotificationSound();
                }
            }, DispatcherPriority.Background);
        }

        if (e.Action is NotifyCollectionChangedAction.Add
            or NotifyCollectionChangedAction.Move
            or NotifyCollectionChangedAction.Replace
            or NotifyCollectionChangedAction.Reset)
        {
            Dispatcher.BeginInvoke(ScrollChatToBottom, DispatcherPriority.Background);
        }
    }

    private void ScrollChatToBottom()
    {
        if (ChatList.Items.Count == 0)
        {
            return;
        }

        ChatList.ScrollIntoView(ChatList.Items[ChatList.Items.Count - 1]);
    }

    private void OnToggleSettingsClicked(object sender, RoutedEventArgs e)
    {
        viewModel.Settings.ChatboxSettingsPanelOpen = !viewModel.Settings.ChatboxSettingsPanelOpen;
    }

    private void OnClearChatClicked(object sender, RoutedEventArgs e)
    {
        viewModel.ClearChatMessages();
    }

    private void OnChatTextSizePreviewTextInput(object sender, TextCompositionEventArgs e)
    {
        foreach (var character in e.Text)
        {
            if (!char.IsDigit(character))
            {
                e.Handled = true;
                return;
            }
        }
    }

    private void OnChatTextSizePasting(object sender, DataObjectPastingEventArgs e)
    {
        if (!e.DataObject.GetDataPresent(DataFormats.Text))
        {
            e.CancelCommand();
            return;
        }

        var pastedText = e.DataObject.GetData(DataFormats.Text) as string ?? string.Empty;
        foreach (var character in pastedText)
        {
            if (!char.IsDigit(character))
            {
                e.CancelCommand();
                return;
            }
        }
    }

    private void OnChatTextSizePreviewKeyDown(object sender, KeyEventArgs e)
    {
        if (e.Key == Key.Space)
        {
            e.Handled = true;
            return;
        }

        if (e.Key is not Key.Enter and not Key.Return)
        {
            return;
        }

        NormalizeChatTextSizeInput(sender as TextBox);
        MoveFocus(new TraversalRequest(FocusNavigationDirection.Next));
        e.Handled = true;
    }

    private void OnChatTextSizeLostFocus(object sender, RoutedEventArgs e)
    {
        NormalizeChatTextSizeInput(sender as TextBox);
    }

    private void OnStateChanged(object? sender, EventArgs e)
    {
        UpdateWindowStateGlyph();
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

        if (e.ClickCount == 2)
        {
            ToggleWindowState();
            return;
        }

        try
        {
            if (WindowState == WindowState.Maximized)
            {
                RestoreFromMaximizedForDrag(e);
            }

            DragMove();
        }
        catch
        {
        }
    }

    private void OnMinimizeButtonClick(object sender, RoutedEventArgs e)
    {
        WindowState = WindowState.Minimized;
    }

    private void OnToggleMaximizeButtonClick(object sender, RoutedEventArgs e)
    {
        ToggleWindowState();
    }

    private void OnCloseButtonClick(object sender, RoutedEventArgs e)
    {
        Close();
    }

    private void ToggleWindowState()
    {
        WindowState = WindowState == WindowState.Maximized
            ? WindowState.Normal
            : WindowState.Maximized;
    }

    private void UpdateWindowStateGlyph()
    {
        if (WindowStateGlyph is null)
        {
            return;
        }

        WindowStateGlyph.Text = WindowState == WindowState.Maximized ? "❐" : "☐";
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

    private void ApplyChatboxStateFromSettings()
    {
        Topmost = viewModel.Settings.ChatboxAlwaysOnTop;
        ApplyTheme(viewModel.Settings.Theme);

        var settingsPanelOpen = viewModel.Settings.ChatboxSettingsPanelOpen;
        SettingsPanel.Visibility = settingsPanelOpen ? Visibility.Visible : Visibility.Collapsed;
        SettingsToggleButton.Content = settingsPanelOpen
            ? LocalizationService.Translate("Done")
            : LocalizationService.Translate("Settings");

        ApplyOverlayLayout(viewModel.Settings.ChatboxOverlayMode);
    }

    private void RefreshVisibleChatNameColors()
    {
        for (var i = 0; i < viewModel.ChatMessages.Count; i++)
        {
            var entry = viewModel.ChatMessages[i];
            if (entry is not { } current)
            {
                continue;
            }

            // Recreate entry so name color follows active theme contrast rules.
            viewModel.ChatMessages[i] = new TwitchChatMessageEntry(
                current.UserDisplayName,
                current.MessageText,
                current.RawUserColor,
                current.BadgeImageUrls,
                current.InlineFragments,
                current.ShouldPlayViewerSound,
                current.ReceivedAt,
                viewModel.SelectedTheme,
                viewModel.Settings.ChatTimestampFormat);
        }
    }

    private void NormalizeChatTextSizeInput(TextBox? textBox)
    {
        if (textBox is null)
        {
            return;
        }

        var parsed = int.TryParse(textBox.Text, NumberStyles.Integer, CultureInfo.InvariantCulture, out var requestedValue)
            ? requestedValue
            : viewModel.Settings.ChatTextSize;
        var normalized = Math.Clamp(parsed, MinimumChatTextSize, MaximumChatTextSize);
        viewModel.Settings.ChatTextSize = normalized;
        textBox.Text = normalized.ToString(CultureInfo.InvariantCulture);
    }

    private bool ShouldPlayViewerNotificationSound(IList? newItems)
    {
        if (!viewModel.Settings.ChatboxViewerSoundEnabled
            || WindowState == WindowState.Minimized
            || !IsVisible
            || newItems is null
            || newItems.Count == 0)
        {
            return false;
        }

        foreach (var item in newItems)
        {
            if (item is TwitchChatMessageEntry entry && entry.ShouldPlayViewerSound)
            {
                return true;
            }
        }

        return false;
    }

    private void PlayViewerNotificationSound()
    {
        var audioPath = GetOrCreateViewerNotificationAudioPath();
        if (string.IsNullOrWhiteSpace(audioPath) || !File.Exists(audioPath))
        {
            return;
        }

        try
        {
            viewerNotificationPlayer?.Stop();
            viewerNotificationPlayer?.Close();

            var player = new MediaPlayer();
            viewerNotificationPlayer = player;
            player.MediaEnded += (_, _) =>
            {
                if (ReferenceEquals(viewerNotificationPlayer, player))
                {
                    player.Stop();
                }
            };
            player.MediaFailed += (_, _) =>
            {
                if (ReferenceEquals(viewerNotificationPlayer, player))
                {
                    player.Stop();
                }
            };
            player.Open(new Uri(audioPath, UriKind.Absolute));
            player.Volume = 0.72;
            player.Play();
        }
        catch
        {
            CleanupViewerNotificationAudio();
        }
    }

    private string? GetOrCreateViewerNotificationAudioPath()
    {
        if (!string.IsNullOrWhiteSpace(viewerNotificationAudioTempPath)
            && File.Exists(viewerNotificationAudioTempPath))
        {
            return viewerNotificationAudioTempPath;
        }

        viewerNotificationAudioTempPath = EmbeddedMediaCacheService.ExtractEmbeddedMediaToTempFile(ViewerNotificationAudioRelativePath);
        return viewerNotificationAudioTempPath;
    }

    private void CleanupViewerNotificationAudio()
    {
        if (viewerNotificationPlayer is not null)
        {
            viewerNotificationPlayer.Stop();
            viewerNotificationPlayer.Close();
        }

        viewerNotificationPlayer = null;

        EmbeddedMediaCacheService.DeleteTemporaryMediaFile(viewerNotificationAudioTempPath);
        viewerNotificationAudioTempPath = null;
    }

    private void OnChatInlineDiagnosticWritten(string message)
    {
        if (Dispatcher.CheckAccess())
        {
            viewModel.AppendDiagnosticLog(message);
            return;
        }

        Dispatcher.Invoke(() => viewModel.AppendDiagnosticLog(message));
    }

    private void ApplyOverlayLayout(bool overlayMode)
    {
        if (overlayMode)
        {
            TitleBarHost.Visibility = Visibility.Collapsed;
            ContentHostGrid.Margin = new Thickness(6);
            ChatShellBorder.Padding = new Thickness(10);
            ChatShellBorder.CornerRadius = new CornerRadius(12);
            return;
        }

        TitleBarHost.Visibility = Visibility.Visible;
        ContentHostGrid.Margin = new Thickness(14);
        ChatShellBorder.Padding = new Thickness(16);
        ChatShellBorder.CornerRadius = new CornerRadius(18);
    }

    private void ApplyTheme(AppTheme theme)
    {
        if (theme == AppTheme.Custom)
        {
            ThemeManager.ApplyToResources(Resources, theme);
            var palette = ThemeManager.CurrentPalette;
            SetBrushColor("MessageTextBrush", palette.GetColor("TextBrush"));
            SetBrushColor("MessageCardBrush", palette.GetColor("RuleCardBrush"));
            SetBrushColor("MessageBorderBrush", palette.GetColor("InputBorderBrush"));
            SetBrushColor("TimestampBrush", palette.GetColor("MutedBrush"));
            SetBrushColor("SecondaryButtonTextBrush", palette.GetColor("TextBrush"));
            return;
        }

        if (theme == AppTheme.TreetendersArm)
        {
            ThemeManager.ApplyToResources(Resources, theme);
            SetBrushColor("MessageTextBrush", "#F3F8E8");
            SetBrushColor("MessageCardBrush", "#AA15321D");
            SetBrushColor("MessageBorderBrush", "#5FA9F2");
            SetBrushColor("TimestampBrush", "#C2D9B5");
            SetBrushColor("SecondaryButtonTextBrush", "#F6FAED");
            return;
        }

        if (theme == AppTheme.CarrotPatch)
        {
            ThemeManager.ApplyToResources(Resources, theme);
            SetBrushColor("MessageTextBrush", "#FFF3DF");
            SetBrushColor("MessageCardBrush", "#A63A2117");
            SetBrushColor("MessageBorderBrush", "#C9894D");
            SetBrushColor("TimestampBrush", "#F0C99D");
            SetBrushColor("SecondaryButtonTextBrush", "#3E261F");
            return;
        }

        if (theme == AppTheme.Bratwurst)
        {
            ThemeManager.ApplyToResources(Resources, theme);
            SetBrushColor("MessageTextBrush", "#FFF3D2");
            SetBrushColor("MessageCardBrush", "#A8240E08");
            SetBrushColor("MessageBorderBrush", "#D6A11D");
            SetBrushColor("TimestampBrush", "#E0C48A");
            SetBrushColor("SecondaryButtonTextBrush", "#FFF3D2");
            return;
        }

        if (theme == AppTheme.Baked)
        {
            Resources["BodyFontFamily"] = new FontFamily("Cambria");
            Resources["HeadingFontFamily"] = new FontFamily("Georgia");
            SetBrushColor("WindowBackgroundBrush", "#1A120D");
            SetBrushColor("PanelBrush", "#D2261A13");
            SetBrushColor("BorderBrush", "#B89267");
            SetBrushColor("TextBrush", "#F3E7D7");
            SetBrushColor("MutedBrush", "#C9B39A");
            SetBrushColor("MessageTextBrush", "#F3E7D7");
            SetBrushColor("AccentBrush", "#A2472A");
            SetBrushColor("InputBrush", "#B433251B");
            SetBrushColor("InputBorderBrush", "#A97C56");
            SetBrushColor("MessageCardBrush", "#A12E2017");
            SetBrushColor("MessageBorderBrush", "#A97C56");
            SetBrushColor("TimestampBrush", "#E0CFBF");
            SetBrushColor("SecondaryButtonBrush", "#3A281C");
            SetBrushColor("SecondaryButtonBorderBrush", "#A47A56");
            SetBrushColor("SecondaryButtonTextBrush", "#F3E7D7");
            SetBrushColor("ComboSurfaceBrush", "#EFE1D0");
            SetBrushColor("ComboTextBrush", "#2C1B12");
            SetBrushColor("ComboHighlightBrush", "#D7BA9B");
            SetBrushColor("ComboDropButtonBrush", "#E3D0BC");
            SetBrushColor("ComboDropButtonHoverBrush", "#D9C0A5");
            SetBrushColor("ComboDropButtonPressedBrush", "#CEB08F");
            SetBrushColor("TitleBarBrush", "#221710");
            SetBrushColor("TitleBarTextBrush", "#F6EADB");
            SetBrushColor("TitleBarSubTextBrush", "#D6C0A9");
            SetBrushColor("TitleBarButtonBrush", "#00000000");
            SetBrushColor("TitleBarButtonHoverBrush", "#41291D");
            SetBrushColor("TitleBarButtonPressedBrush", "#5A3828");
            SetBrushColor("TitleBarCloseHoverBrush", "#8D3B29");
            SetBrushColor("TitleBarClosePressedBrush", "#6F2D1F");
            return;
        }

        if (theme == AppTheme.DreadNightBar)
        {
            Resources["BodyFontFamily"] = new FontFamily("Cambria");
            Resources["HeadingFontFamily"] = new FontFamily("Book Antiqua");
            SetBrushColor("WindowBackgroundBrush", "#090708");
            SetBrushColor("PanelBrush", "#D2141318");
            SetBrushColor("BorderBrush", "#8B2F3E");
            SetBrushColor("TextBrush", "#F2ECEC");
            SetBrushColor("MutedBrush", "#BDAFB2");
            SetBrushColor("MessageTextBrush", "#F2ECEC");
            SetBrushColor("AccentBrush", "#C54757");
            SetBrushColor("InputBrush", "#B41A171C");
            SetBrushColor("InputBorderBrush", "#7C3B45");
            SetBrushColor("MessageCardBrush", "#9F18161B");
            SetBrushColor("MessageBorderBrush", "#7C3B45");
            SetBrushColor("TimestampBrush", "#D9C7CB");
            SetBrushColor("SecondaryButtonBrush", "#2A2024");
            SetBrushColor("SecondaryButtonBorderBrush", "#7A3C46");
            SetBrushColor("SecondaryButtonTextBrush", "#F2ECEC");
            SetBrushColor("ComboSurfaceBrush", "#F1ECEE");
            SetBrushColor("ComboTextBrush", "#20181B");
            SetBrushColor("ComboHighlightBrush", "#E6C4CA");
            SetBrushColor("ComboDropButtonBrush", "#E7DADF");
            SetBrushColor("ComboDropButtonHoverBrush", "#DCC9D0");
            SetBrushColor("ComboDropButtonPressedBrush", "#D0B8C1");
            SetBrushColor("TitleBarBrush", "#1C1418");
            SetBrushColor("TitleBarTextBrush", "#F6EFF0");
            SetBrushColor("TitleBarSubTextBrush", "#CDBFC2");
            SetBrushColor("TitleBarButtonBrush", "#00000000");
            SetBrushColor("TitleBarButtonHoverBrush", "#3B2328");
            SetBrushColor("TitleBarButtonPressedBrush", "#5A3138");
            SetBrushColor("TitleBarCloseHoverBrush", "#A23543");
            SetBrushColor("TitleBarClosePressedBrush", "#7D2430");
            return;
        }

        if (theme == AppTheme.MoonBunnyWink)
        {
            Resources["BodyFontFamily"] = new FontFamily("Verdana");
            Resources["HeadingFontFamily"] = new FontFamily("Segoe Print");
            SetBrushColor("WindowBackgroundBrush", "#12091E");
            SetBrushColor("PanelBrush", "#D31C1230");
            SetBrushColor("BorderBrush", "#AA99D8");
            SetBrushColor("TextBrush", "#FFFDFB");
            SetBrushColor("MutedBrush", "#D8CFE7");
            SetBrushColor("MessageTextBrush", "#FFFDFB");
            SetBrushColor("AccentBrush", "#F2D24F");
            SetBrushColor("InputBrush", "#BE25183C");
            SetBrushColor("InputBorderBrush", "#B4A5E0");
            SetBrushColor("MessageCardBrush", "#BE24163A");
            SetBrushColor("MessageBorderBrush", "#AA99D8");
            SetBrushColor("TimestampBrush", "#EFE6FA");
            SetBrushColor("SecondaryButtonBrush", "#33204D");
            SetBrushColor("SecondaryButtonBorderBrush", "#8D7AC6");
            SetBrushColor("SecondaryButtonTextBrush", "#FFFDFB");
            SetBrushColor("ComboSurfaceBrush", "#FFFDF8");
            SetBrushColor("ComboTextBrush", "#241C30");
            SetBrushColor("ComboHighlightBrush", "#FBE992");
            SetBrushColor("ComboDropButtonBrush", "#F6E9FF");
            SetBrushColor("ComboDropButtonHoverBrush", "#ECD8FF");
            SetBrushColor("ComboDropButtonPressedBrush", "#DFC8F5");
            SetBrushColor("TitleBarBrush", "#26153A");
            SetBrushColor("TitleBarTextBrush", "#FFFDFB");
            SetBrushColor("TitleBarSubTextBrush", "#ECE4F9");
            SetBrushColor("TitleBarButtonBrush", "#00000000");
            SetBrushColor("TitleBarButtonHoverBrush", "#3E2758");
            SetBrushColor("TitleBarButtonPressedBrush", "#58377A");
            SetBrushColor("TitleBarCloseHoverBrush", "#B85B7D");
            SetBrushColor("TitleBarClosePressedBrush", "#954766");
            return;
        }

        if (theme == AppTheme.PeachesAndCream)
        {
            Resources["BodyFontFamily"] = new FontFamily("Calibri");
            Resources["HeadingFontFamily"] = new FontFamily("Georgia");
            SetBrushColor("WindowBackgroundBrush", "#FFF9EE");
            SetBrushColor("PanelBrush", "#DDFBF7ED");
            SetBrushColor("BorderBrush", "#4DB6BE");
            SetBrushColor("TextBrush", "#2C2A2B");
            SetBrushColor("MutedBrush", "#635B58");
            SetBrushColor("MessageTextBrush", "#FFFDF8");
            SetBrushColor("AccentBrush", "#F5826F");
            SetBrushColor("InputBrush", "#D7FFF8F1");
            SetBrushColor("InputBorderBrush", "#5BB8BF");
            SetBrushColor("MessageCardBrush", "#544C4B");
            SetBrushColor("MessageBorderBrush", "#4DB6BE");
            SetBrushColor("TimestampBrush", "#F6EDE7");
            SetBrushColor("SecondaryButtonBrush", "#4DB6BE");
            SetBrushColor("SecondaryButtonBorderBrush", "#379AA2");
            SetBrushColor("SecondaryButtonTextBrush", "#FFFDF8");
            SetBrushColor("ComboSurfaceBrush", "#FFFDF6");
            SetBrushColor("ComboTextBrush", "#262323");
            SetBrushColor("ComboHighlightBrush", "#FFE0D6");
            SetBrushColor("ComboDropButtonBrush", "#FFE7DE");
            SetBrushColor("ComboDropButtonHoverBrush", "#FFD8CC");
            SetBrushColor("ComboDropButtonPressedBrush", "#FFC8BA");
            SetBrushColor("TitleBarBrush", "#F5826F");
            SetBrushColor("TitleBarTextBrush", "#FFFDF8");
            SetBrushColor("TitleBarSubTextBrush", "#FFF0EA");
            SetBrushColor("TitleBarButtonBrush", "#00000000");
            SetBrushColor("TitleBarButtonHoverBrush", "#E67463");
            SetBrushColor("TitleBarButtonPressedBrush", "#D66658");
            SetBrushColor("TitleBarCloseHoverBrush", "#CB5A56");
            SetBrushColor("TitleBarClosePressedBrush", "#B84D4A");
            return;
        }

        if (theme == AppTheme.CosmicPuppyGirl)
        {
            Resources["BodyFontFamily"] = new FontFamily("Trebuchet MS");
            Resources["HeadingFontFamily"] = new FontFamily("Book Antiqua");
            SetBrushColor("WindowBackgroundBrush", "#120816");
            SetBrushColor("PanelBrush", "#CE1F132A");
            SetBrushColor("BorderBrush", "#6B3A89");
            SetBrushColor("TextBrush", "#F7F1FF");
            SetBrushColor("MutedBrush", "#D4C6E6");
            SetBrushColor("MessageTextBrush", "#F7F1FF");
            SetBrushColor("AccentBrush", "#93D944");
            SetBrushColor("InputBrush", "#BA26183A");
            SetBrushColor("InputBorderBrush", "#7A4BA2");
            SetBrushColor("MessageCardBrush", "#B629173A");
            SetBrushColor("MessageBorderBrush", "#7A4BA2");
            SetBrushColor("TimestampBrush", "#CFC2E0");
            SetBrushColor("SecondaryButtonBrush", "#351B4A");
            SetBrushColor("SecondaryButtonBorderBrush", "#6A4290");
            SetBrushColor("SecondaryButtonTextBrush", "#F7F1FF");
            SetBrushColor("ComboSurfaceBrush", "#F8F4FF");
            SetBrushColor("ComboTextBrush", "#1A1124");
            SetBrushColor("ComboHighlightBrush", "#D4F0AE");
            SetBrushColor("ComboDropButtonBrush", "#E9D9FF");
            SetBrushColor("ComboDropButtonHoverBrush", "#DBCBF8");
            SetBrushColor("ComboDropButtonPressedBrush", "#CCB8EC");
            SetBrushColor("TitleBarBrush", "#2A1436");
            SetBrushColor("TitleBarTextBrush", "#F8F2FF");
            SetBrushColor("TitleBarSubTextBrush", "#D8CAE8");
            SetBrushColor("TitleBarButtonBrush", "#00000000");
            SetBrushColor("TitleBarButtonHoverBrush", "#3E2056");
            SetBrushColor("TitleBarButtonPressedBrush", "#573176");
            SetBrushColor("TitleBarCloseHoverBrush", "#B54562");
            SetBrushColor("TitleBarClosePressedBrush", "#8E334B");
            return;
        }

        if (theme == AppTheme.Bubblegum)
        {
            Resources["BodyFontFamily"] = new FontFamily("Candara");
            Resources["HeadingFontFamily"] = new FontFamily("Ink Free");
            SetBrushColor("WindowBackgroundBrush", "#F2E9F7");
            SetBrushColor("PanelBrush", "#E2D1EE");
            SetBrushColor("BorderBrush", "#A78FCB");
            SetBrushColor("TextBrush", "#4B355F");
            SetBrushColor("MutedBrush", "#6A5A82");
            SetBrushColor("MessageTextBrush", "#F8F2FF");
            SetBrushColor("AccentBrush", "#E67DB8");
            SetBrushColor("InputBrush", "#D6C4E6");
            SetBrushColor("InputBorderBrush", "#A78FCB");
            SetBrushColor("MessageCardBrush", "#7D6B99");
            SetBrushColor("MessageBorderBrush", "#BCA5DE");
            SetBrushColor("TimestampBrush", "#E8DBF9");
            SetBrushColor("SecondaryButtonBrush", "#E8C9F5");
            SetBrushColor("SecondaryButtonBorderBrush", "#B88FD4");
            SetBrushColor("SecondaryButtonTextBrush", "#4B355F");
            SetBrushColor("ComboSurfaceBrush", "#EDE1F6");
            SetBrushColor("ComboTextBrush", "#3F2E54");
            SetBrushColor("ComboHighlightBrush", "#D5C1EA");
            SetBrushColor("ComboDropButtonBrush", "#E1C8EE");
            SetBrushColor("ComboDropButtonHoverBrush", "#D7B7E8");
            SetBrushColor("ComboDropButtonPressedBrush", "#CAA5E1");
            SetBrushColor("TitleBarBrush", "#A77CC8");
            SetBrushColor("TitleBarTextBrush", "#FDF7FF");
            SetBrushColor("TitleBarSubTextBrush", "#EFE2FB");
            SetBrushColor("TitleBarButtonBrush", "#00000000");
            SetBrushColor("TitleBarButtonHoverBrush", "#8D67B5");
            SetBrushColor("TitleBarButtonPressedBrush", "#7A56A4");
            SetBrushColor("TitleBarCloseHoverBrush", "#CD6A97");
            SetBrushColor("TitleBarClosePressedBrush", "#B45984");
            return;
        }

        if (theme == AppTheme.DreamScape)
        {
            Resources["BodyFontFamily"] = new FontFamily("Leelawadee UI");
            Resources["HeadingFontFamily"] = new FontFamily("Segoe Print");
            SetBrushColor("WindowBackgroundBrush", "#08132F");
            SetBrushColor("PanelBrush", "#CC1E1A56");
            SetBrushColor("BorderBrush", "#5F8BFF");
            SetBrushColor("TextBrush", "#FFF6FF");
            SetBrushColor("MutedBrush", "#E8D8FF");
            SetBrushColor("MessageTextBrush", "#FFF6FF");
            SetBrushColor("AccentBrush", "#FF58D8");
            SetBrushColor("InputBrush", "#BC222560");
            SetBrushColor("InputBorderBrush", "#7B92FF");
            SetBrushColor("MessageCardBrush", "#B0252B72");
            SetBrushColor("MessageBorderBrush", "#8AA1FF");
            SetBrushColor("TimestampBrush", "#D7C8FF");
            SetBrushColor("SecondaryButtonBrush", "#22347A");
            SetBrushColor("SecondaryButtonBorderBrush", "#6C8EFF");
            SetBrushColor("SecondaryButtonTextBrush", "#FFF7FF");
            SetBrushColor("ComboSurfaceBrush", "#FFF5FF");
            SetBrushColor("ComboTextBrush", "#140F2A");
            SetBrushColor("ComboHighlightBrush", "#E7DBFF");
            SetBrushColor("ComboDropButtonBrush", "#F9D7FF");
            SetBrushColor("ComboDropButtonHoverBrush", "#F4BFFF");
            SetBrushColor("ComboDropButtonPressedBrush", "#E7A4FF");
            SetBrushColor("TitleBarBrush", "#14245E");
            SetBrushColor("TitleBarTextBrush", "#FFF7FF");
            SetBrushColor("TitleBarSubTextBrush", "#F0DFFF");
            SetBrushColor("TitleBarButtonBrush", "#00000000");
            SetBrushColor("TitleBarButtonHoverBrush", "#3447A8");
            SetBrushColor("TitleBarButtonPressedBrush", "#5165DD");
            SetBrushColor("TitleBarCloseHoverBrush", "#D03B75");
            SetBrushColor("TitleBarClosePressedBrush", "#A82A5A");
            return;
        }

        if (theme == AppTheme.MainFrame)
        {
            Resources["BodyFontFamily"] = new FontFamily("Consolas");
            Resources["HeadingFontFamily"] = new FontFamily("Bahnschrift");
            SetBrushColor("WindowBackgroundBrush", "#05070D");
            SetBrushColor("PanelBrush", "#CC111524");
            SetBrushColor("BorderBrush", "#5B43A8");
            SetBrushColor("TextBrush", "#F2F7FF");
            SetBrushColor("MutedBrush", "#A4AFCA");
            SetBrushColor("MessageTextBrush", "#F2F7FF");
            SetBrushColor("AccentBrush", "#67F7A6");
            SetBrushColor("InputBrush", "#BF141B2C");
            SetBrushColor("InputBorderBrush", "#7058C4");
            SetBrushColor("MessageCardBrush", "#A2151C2D");
            SetBrushColor("MessageBorderBrush", "#7058C4");
            SetBrushColor("TimestampBrush", "#B6C4DE");
            SetBrushColor("SecondaryButtonBrush", "#1A2139");
            SetBrushColor("SecondaryButtonBorderBrush", "#5A47A1");
            SetBrushColor("SecondaryButtonTextBrush", "#F2F7FF");
            SetBrushColor("ComboSurfaceBrush", "#F3F8FF");
            SetBrushColor("ComboTextBrush", "#09111A");
            SetBrushColor("ComboHighlightBrush", "#CEF7DA");
            SetBrushColor("ComboDropButtonBrush", "#D9FBE5");
            SetBrushColor("ComboDropButtonHoverBrush", "#C6F4D6");
            SetBrushColor("ComboDropButtonPressedBrush", "#AFEBC4");
            SetBrushColor("TitleBarBrush", "#0F141F");
            SetBrushColor("TitleBarTextBrush", "#F2F7FF");
            SetBrushColor("TitleBarSubTextBrush", "#B4C0D8");
            SetBrushColor("TitleBarButtonBrush", "#00000000");
            SetBrushColor("TitleBarButtonHoverBrush", "#1E2940");
            SetBrushColor("TitleBarButtonPressedBrush", "#29385A");
            SetBrushColor("TitleBarCloseHoverBrush", "#8F2F57");
            SetBrushColor("TitleBarClosePressedBrush", "#712243");
            return;
        }

        if (theme == AppTheme.TrashKitty)
        {
            Resources["BodyFontFamily"] = new FontFamily("Trebuchet MS");
            Resources["HeadingFontFamily"] = new FontFamily("Bahnschrift");
            SetBrushColor("WindowBackgroundBrush", "#07090C");
            SetBrushColor("PanelBrush", "#D014171B");
            SetBrushColor("BorderBrush", "#62758F");
            SetBrushColor("TextBrush", "#F1F5F8");
            SetBrushColor("MutedBrush", "#B7C1CB");
            SetBrushColor("MessageTextBrush", "#F1F5F8");
            SetBrushColor("AccentBrush", "#9FC8FF");
            SetBrushColor("InputBrush", "#C51C2228");
            SetBrushColor("InputBorderBrush", "#7C92AF");
            SetBrushColor("MessageCardBrush", "#A91C2228");
            SetBrushColor("MessageBorderBrush", "#6D819C");
            SetBrushColor("TimestampBrush", "#BFD0E2");
            SetBrushColor("SecondaryButtonBrush", "#2E363F");
            SetBrushColor("SecondaryButtonBorderBrush", "#7C92AF");
            SetBrushColor("SecondaryButtonTextBrush", "#F1F5F8");
            SetBrushColor("ComboSurfaceBrush", "#EEF4FA");
            SetBrushColor("ComboTextBrush", "#101419");
            SetBrushColor("ComboHighlightBrush", "#C9DFFF");
            SetBrushColor("ComboDropButtonBrush", "#DDEBFA");
            SetBrushColor("ComboDropButtonHoverBrush", "#CCE0F7");
            SetBrushColor("ComboDropButtonPressedBrush", "#B8D1EF");
            SetBrushColor("TitleBarBrush", "#101317");
            SetBrushColor("TitleBarTextBrush", "#F1F5F8");
            SetBrushColor("TitleBarSubTextBrush", "#B9C5D1");
            SetBrushColor("TitleBarButtonBrush", "#00000000");
            SetBrushColor("TitleBarButtonHoverBrush", "#2B333C");
            SetBrushColor("TitleBarButtonPressedBrush", "#374454");
            SetBrushColor("TitleBarCloseHoverBrush", "#5E7085");
            SetBrushColor("TitleBarClosePressedBrush", "#435466");
            return;
        }

        Resources["BodyFontFamily"] = new FontFamily("Verdana");
        Resources["HeadingFontFamily"] = new FontFamily("Constantia");
        SetBrushColor("WindowBackgroundBrush", "#130B1E");
        SetBrushColor("PanelBrush", "#CC1C132B");
        SetBrushColor("BorderBrush", "#4B2B78");
        SetBrushColor("TextBrush", "#F5EEFF");
        SetBrushColor("MutedBrush", "#C9B8E3");
        SetBrushColor("MessageTextBrush", "#F5EEFF");
        SetBrushColor("AccentBrush", "#A855F7");
        SetBrushColor("InputBrush", "#B8271A3D");
        SetBrushColor("InputBorderBrush", "#5B3A8E");
        SetBrushColor("MessageCardBrush", "#AA28173C");
        SetBrushColor("MessageBorderBrush", "#5F3A93");
        SetBrushColor("TimestampBrush", "#A892CB");
        SetBrushColor("SecondaryButtonBrush", "#2C1C48");
        SetBrushColor("SecondaryButtonBorderBrush", "#6942A7");
        SetBrushColor("SecondaryButtonTextBrush", "#F5EEFF");
        SetBrushColor("ComboSurfaceBrush", "#F7F2FF");
        SetBrushColor("ComboTextBrush", "#140C20");
        SetBrushColor("ComboHighlightBrush", "#D8C2FF");
        SetBrushColor("ComboDropButtonBrush", "#E9DBFF");
        SetBrushColor("ComboDropButtonHoverBrush", "#D9C2FF");
        SetBrushColor("ComboDropButtonPressedBrush", "#C7A8FF");
        SetBrushColor("TitleBarBrush", "#20122F");
        SetBrushColor("TitleBarTextBrush", "#F5EEFF");
        SetBrushColor("TitleBarSubTextBrush", "#CBB9E5");
        SetBrushColor("TitleBarButtonBrush", "#00000000");
        SetBrushColor("TitleBarButtonHoverBrush", "#3B235B");
        SetBrushColor("TitleBarButtonPressedBrush", "#543183");
        SetBrushColor("TitleBarCloseHoverBrush", "#B43D62");
        SetBrushColor("TitleBarClosePressedBrush", "#8C2648");
    }

    private void RestoreFromMaximizedForDrag(MouseButtonEventArgs e)
    {
        var mousePosition = e.GetPosition(this);
        var screenPosition = PointToScreen(mousePosition);
        var horizontalRatio = ActualWidth <= 0 ? 0.5 : mousePosition.X / ActualWidth;
        var restoreBounds = RestoreBounds;
        var targetWidth = restoreBounds.Width > 0 ? restoreBounds.Width : Width;
        var targetHeight = restoreBounds.Height > 0 ? restoreBounds.Height : Height;

        WindowState = WindowState.Normal;
        Left = screenPosition.X - (targetWidth * horizontalRatio);
        Top = Math.Max(SystemParameters.WorkArea.Top, screenPosition.Y - Math.Min(32, targetHeight * 0.12));
    }

    private void SetBrushColor(string resourceKey, string colorText)
    {
        var color = (Color)ColorConverter.ConvertFromString(colorText);
        SetBrushColor(resourceKey, color);
    }

    private void SetBrushColor(string resourceKey, Color color)
    {
        if (Resources[resourceKey] is SolidColorBrush brush && !brush.IsFrozen)
        {
            brush.Color = color;
            return;
        }

        Resources[resourceKey] = new SolidColorBrush(color);
    }
}

public static class ChatMessageInlinePresenter
{
    private const int MaxCachedEmoteImages = 512;
    private const int MaxLoggedFailedEmoteImages = 256;

    // Busy chats reuse the same emotes constantly, so cache decoded images by final URI and
    // reuse frozen ImageSource instances instead of decoding the same bitmap every message.
    private static readonly HttpClient emoteImageHttpClient = CreateEmoteImageHttpClient();
    private static readonly object emoteImageCacheGate = new();
    private static readonly Dictionary<string, LinkedListNode<CachedEmoteImage>> emoteImagesByUri = new(StringComparer.Ordinal);
    private static readonly LinkedList<CachedEmoteImage> emoteImageRecency = new();
    private static readonly HashSet<string> emoteImageLoadsInFlight = new(StringComparer.Ordinal);
    private static readonly HashSet<string> failedEmoteImageUrls = new(StringComparer.Ordinal);
    private static readonly Queue<string> failedEmoteImageLogOrder = [];

    public static event Action<string>? DiagnosticWritten;

    private static HttpClient CreateEmoteImageHttpClient()
    {
        var client = new HttpClient
        {
            Timeout = TimeSpan.FromSeconds(4)
        };
        client.DefaultRequestHeaders.UserAgent.ParseAdd("CrystalRelay/2.8.9 (+https://github.com/seluvia/crystal-relay-public)");
        return client;
    }

    public static readonly DependencyProperty FragmentsProperty =
        DependencyProperty.RegisterAttached(
            "Fragments",
            typeof(IReadOnlyList<TwitchChatInlineFragment>),
            typeof(ChatMessageInlinePresenter),
            new PropertyMetadata(null, OnFragmentsChanged));

    public static readonly DependencyProperty FontScaleKeyProperty =
        DependencyProperty.RegisterAttached(
            "FontScaleKey",
            typeof(double),
            typeof(ChatMessageInlinePresenter),
            new PropertyMetadata(0d, OnFragmentsChanged));

    public static void SetFragments(DependencyObject element, IReadOnlyList<TwitchChatInlineFragment>? value) =>
        element.SetValue(FragmentsProperty, value);

    public static IReadOnlyList<TwitchChatInlineFragment>? GetFragments(DependencyObject element) =>
        (IReadOnlyList<TwitchChatInlineFragment>?)element.GetValue(FragmentsProperty);

    public static void SetFontScaleKey(DependencyObject element, double value) =>
        element.SetValue(FontScaleKeyProperty, value);

    public static double GetFontScaleKey(DependencyObject element) =>
        (double)element.GetValue(FontScaleKeyProperty);

    private static void OnFragmentsChanged(DependencyObject dependencyObject, DependencyPropertyChangedEventArgs _)
    {
        if (dependencyObject is not TextBlock textBlock)
        {
            return;
        }

        RebuildInlines(textBlock);
    }

    private static void RebuildInlines(TextBlock textBlock)
    {
        textBlock.Inlines.Clear();

        var fragments = GetFragments(textBlock);
        if (fragments is null || fragments.Count == 0)
        {
            return;
        }

        foreach (var fragment in fragments)
        {
            if (fragment.Kind == TwitchChatInlineFragmentKind.Emote && fragment.ImageUri is not null)
            {
                var cachedEmoteImage = TryGetCachedEmoteImage(fragment.ImageUri);
                if (cachedEmoteImage is null)
                {
                    QueueMissingEmoteImageLoad(textBlock, fragment.ImageUri);
                    textBlock.Inlines.Add(new Run(fragment.Text));
                    continue;
                }

                var image = new Image
                {
                    Height = Math.Max(20, Math.Round(textBlock.FontSize * 1.35)),
                    Stretch = Stretch.Uniform,
                    SnapsToDevicePixels = true,
                    Margin = new Thickness(0, -2, 1, -2)
                };
                ApplyCachedEmoteImage(image, cachedEmoteImage);

                textBlock.Inlines.Add(new InlineUIContainer(image)
                {
                    BaselineAlignment = BaselineAlignment.Center
                });
                continue;
            }

            textBlock.Inlines.Add(new Run(fragment.Text));
        }
    }

    private static CachedEmoteImage? TryGetCachedEmoteImage(Uri imageUri)
    {
        var imageKey = imageUri.ToString();
        lock (emoteImageCacheGate)
        {
            if (emoteImagesByUri.TryGetValue(imageKey, out var cachedImageNode))
            {
                emoteImageRecency.Remove(cachedImageNode);
                emoteImageRecency.AddFirst(cachedImageNode);
                return cachedImageNode.Value;
            }
        }

        return null;
    }

    private static void QueueMissingEmoteImageLoad(TextBlock textBlock, Uri imageUri)
    {
        var imageKey = imageUri.ToString();
        lock (emoteImageCacheGate)
        {
            if (emoteImagesByUri.ContainsKey(imageKey)
                || failedEmoteImageUrls.Contains(imageKey)
                || !emoteImageLoadsInFlight.Add(imageKey))
            {
                return;
            }
        }

        _ = LoadEmoteImageAndRebuildAsync(textBlock, imageUri, imageKey);
    }

    private static async Task LoadEmoteImageAndRebuildAsync(TextBlock textBlock, Uri imageUri, string imageKey)
    {
        var loaded = false;
        try
        {
            using var response = await emoteImageHttpClient.GetAsync(
                imageUri,
                HttpCompletionOption.ResponseHeadersRead);
            response.EnsureSuccessStatusCode();
            var imageBytes = await response.Content.ReadAsByteArrayAsync();
            var cachedImage = DecodeEmoteImage(imageKey, imageBytes, response.Content.Headers.ContentType?.MediaType);

            lock (emoteImageCacheGate)
            {
                if (!emoteImagesByUri.ContainsKey(imageKey))
                {
                    var node = new LinkedListNode<CachedEmoteImage>(cachedImage);
                    emoteImageRecency.AddFirst(node);
                    emoteImagesByUri[imageKey] = node;
                    while (emoteImagesByUri.Count > MaxCachedEmoteImages && emoteImageRecency.Last is not null)
                    {
                        var removedNode = emoteImageRecency.Last;
                        emoteImageRecency.RemoveLast();
                        emoteImagesByUri.Remove(removedNode.Value.Uri);
                    }
                }

                loaded = true;
            }
        }
        catch (Exception ex)
        {
            RememberFailedEmoteImage(imageKey, ex);
        }
        finally
        {
            lock (emoteImageCacheGate)
            {
                emoteImageLoadsInFlight.Remove(imageKey);
            }
        }

        if (loaded && !textBlock.Dispatcher.HasShutdownStarted)
        {
            await textBlock.Dispatcher.InvokeAsync(
                () => RebuildInlines(textBlock),
                DispatcherPriority.Background);
        }
    }

    private static void RememberFailedEmoteImage(string imageKey, Exception ex)
    {
        var shouldLog = false;
        lock (emoteImageCacheGate)
        {
            shouldLog = failedEmoteImageUrls.Add(imageKey);
            if (shouldLog)
            {
                failedEmoteImageLogOrder.Enqueue(imageKey);
                while (failedEmoteImageUrls.Count > MaxLoggedFailedEmoteImages && failedEmoteImageLogOrder.Count > 0)
                {
                    failedEmoteImageUrls.Remove(failedEmoteImageLogOrder.Dequeue());
                }
            }
        }

        if (shouldLog)
        {
            DiagnosticWritten?.Invoke($"Twitch Chatbox could not load emote image {ShortenImageUrlForLog(imageKey)}: {ex.Message}");
        }
    }

    private static CachedEmoteImage DecodeEmoteImage(string imageKey, byte[] imageBytes, string? mediaType)
    {
        if (IsGifPayload(imageBytes, mediaType))
        {
            using var imageStream = new MemoryStream(imageBytes);
            var decoder = new GifBitmapDecoder(
                imageStream,
                BitmapCreateOptions.PreservePixelFormat,
                BitmapCacheOption.OnLoad);
            var frames = decoder.Frames
                .Select(frame =>
                {
                    if (frame.CanFreeze)
                    {
                        frame.Freeze();
                    }

                    return (ImageSource)frame;
                })
                .ToArray();

            if (frames.Length > 1)
            {
                var delays = decoder.Frames
                    .Select(GetGifFrameDelay)
                    .ToArray();
                return new CachedEmoteImage(imageKey, frames[0], frames, delays);
            }
        }

        using var bitmapStream = new MemoryStream(imageBytes);
        var bitmap = new BitmapImage();
        bitmap.BeginInit();
        bitmap.CacheOption = BitmapCacheOption.OnLoad;
        bitmap.StreamSource = bitmapStream;
        bitmap.EndInit();
        bitmap.Freeze();
        return new CachedEmoteImage(imageKey, bitmap, [], []);
    }

    private static bool IsGifPayload(byte[] imageBytes, string? mediaType)
    {
        if (string.Equals(mediaType, "image/gif", StringComparison.OrdinalIgnoreCase))
        {
            return true;
        }

        return imageBytes.Length >= 6
            && imageBytes[0] == 'G'
            && imageBytes[1] == 'I'
            && imageBytes[2] == 'F'
            && imageBytes[3] == '8'
            && (imageBytes[4] == '7' || imageBytes[4] == '9')
            && imageBytes[5] == 'a';
    }

    private static TimeSpan GetGifFrameDelay(BitmapFrame frame)
    {
        const int minimumFrameDelayMilliseconds = 20;
        const int defaultFrameDelayMilliseconds = 100;

        try
        {
            if (frame.Metadata is BitmapMetadata metadata
                && metadata.ContainsQuery("/grctlext/Delay"))
            {
                var rawDelay = metadata.GetQuery("/grctlext/Delay");
                var hundredths = rawDelay switch
                {
                    byte value => value,
                    ushort value => value,
                    short value => value,
                    int value => value,
                    uint value => value > int.MaxValue ? int.MaxValue : (int)value,
                    _ => 0
                };

                if (hundredths > 0)
                {
                    return TimeSpan.FromMilliseconds(Math.Max(minimumFrameDelayMilliseconds, hundredths * 10));
                }
            }
        }
        catch
        {
            // Bad GIF metadata should not stop chat text from rendering.
        }

        return TimeSpan.FromMilliseconds(defaultFrameDelayMilliseconds);
    }

    private static void ApplyCachedEmoteImage(Image image, CachedEmoteImage cachedEmoteImage)
    {
        if (!cachedEmoteImage.IsAnimated)
        {
            image.Source = cachedEmoteImage.Image;
            return;
        }

        var frameIndex = 0;
        image.Source = cachedEmoteImage.AnimationFrames[frameIndex];
        var timer = new DispatcherTimer(DispatcherPriority.Render, image.Dispatcher)
        {
            Interval = cachedEmoteImage.AnimationDelays[frameIndex]
        };
        timer.Tick += (_, _) =>
        {
            if (cachedEmoteImage.AnimationFrames.Count == 0)
            {
                timer.Stop();
                return;
            }

            frameIndex = (frameIndex + 1) % cachedEmoteImage.AnimationFrames.Count;
            image.Source = cachedEmoteImage.AnimationFrames[frameIndex];
            timer.Interval = cachedEmoteImage.AnimationDelays.Count > frameIndex
                ? cachedEmoteImage.AnimationDelays[frameIndex]
                : TimeSpan.FromMilliseconds(100);
        };
        image.Unloaded += (_, _) => timer.Stop();
        timer.Start();
    }

    private static string ShortenImageUrlForLog(string imageUrl) =>
        imageUrl.Length <= 96
            ? imageUrl
            : $"{imageUrl[..96]}...";

    private sealed record CachedEmoteImage(
        string Uri,
        ImageSource Image,
        IReadOnlyList<ImageSource> AnimationFrames,
        IReadOnlyList<TimeSpan> AnimationDelays)
    {
        public bool IsAnimated => AnimationFrames.Count > 1;
    }
}
