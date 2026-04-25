using System.Collections;
using System.Collections.Specialized;
using System.ComponentModel;
using System.IO;
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
    private readonly MainWindowViewModel viewModel;
    private MediaPlayer? viewerNotificationPlayer;
    private string? viewerNotificationAudioTempPath;

    public TwitchChatboxWindow(MainWindowViewModel viewModel, AppTheme initialTheme)
    {
        InitializeComponent();
        this.viewModel = viewModel;
        DataContext = viewModel;
        Topmost = viewModel.Settings.ChatboxAlwaysOnTop;
        ApplyTheme(initialTheme);
        UpdateWindowStateGlyph();
        ApplyChatboxStateFromSettings();

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
            viewerNotificationPlayer = null;
        }

        EmbeddedMediaCacheService.DeleteTemporaryMediaFile(viewerNotificationAudioTempPath);
        viewerNotificationAudioTempPath = null;
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
    private static readonly object emoteImageCacheGate = new();
    private static readonly Dictionary<string, ImageSource> emoteImagesByUri = new(StringComparer.Ordinal);

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
                var cachedEmoteImage = GetCachedEmoteImage(fragment.ImageUri);
                if (cachedEmoteImage is null)
                {
                    textBlock.Inlines.Add(new Run(fragment.Text));
                    continue;
                }

                var image = new Image
                {
                    Source = cachedEmoteImage,
                    Height = Math.Max(20, Math.Round(textBlock.FontSize * 1.35)),
                    Stretch = Stretch.Uniform,
                    SnapsToDevicePixels = true,
                    Margin = new Thickness(0, -2, 1, -2)
                };

                textBlock.Inlines.Add(new InlineUIContainer(image)
                {
                    BaselineAlignment = BaselineAlignment.Center
                });
                continue;
            }

            textBlock.Inlines.Add(new Run(fragment.Text));
        }
    }

    private static ImageSource? GetCachedEmoteImage(Uri imageUri)
    {
        var imageKey = imageUri.ToString();
        lock (emoteImageCacheGate)
        {
            if (emoteImagesByUri.TryGetValue(imageKey, out var cachedImage))
            {
                return cachedImage;
            }
        }

        try
        {
            var bitmap = new BitmapImage();
            bitmap.BeginInit();
            bitmap.CacheOption = BitmapCacheOption.OnLoad;
            bitmap.UriSource = imageUri;
            bitmap.EndInit();
            bitmap.Freeze();

            lock (emoteImageCacheGate)
            {
                emoteImagesByUri[imageKey] = bitmap;
            }

            return bitmap;
        }
        catch
        {
            return null;
        }
    }
}
