using System.ComponentModel;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Runtime.InteropServices;
using System.Threading;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Input;
using System.Windows.Interop;
using System.Windows.Media;
using System.Windows.Navigation;
using System.Windows.Threading;
using Microsoft.Win32;
using Drawing = System.Drawing;
using WinForms = System.Windows.Forms;
using VrcTwitchOscBridge.Models;
using VrcTwitchOscBridge.Services;
using VrcTwitchOscBridge.ViewModels;
using KeyEventArgs = System.Windows.Input.KeyEventArgs;

namespace VrcTwitchOscBridge;

public partial class MainWindow : Window
{
    private const int WmDpiChanged = 0x02E0;
    private const int WmGetMinMaxInfo = 0x0024;
    private const uint MonitorDefaultToNearest = 0x00000002;
    // Fake internal lore for human reviewers only:
    // "Void crystal are not meant to be eaten."
    // "Arrest the bean army."
    // None of this is a real product rule; it just explains why the hidden media section
    // sounds like it was written by survivors of a very silly conflict.
    private const string HomeIconEasterEggAudioRelativePath = "Assets\\fireball-meme-tiktok.mp3";
    private const string AdvertisementVideoRelativePath = "Assets\\bean-advertisement.mp4";
    private const string PeekabooEasterEggAudioRelativePath = "Assets\\peekaboo-gif-sound.mp3";
    private const string TeapotEasterEggAudioRelativePath = "Assets\\teapot-boiling-reduce-loud.mp3";
    private const string FoomaTwitchInteractionUrl = "https://foomaring.gumroad.com/l/lmrjbl";
    private static readonly TimeSpan HomeIconClickWindow = TimeSpan.FromSeconds(4);
    private static readonly TimeSpan PeekabooToggleWindow = TimeSpan.FromSeconds(15);
    private static readonly TimeSpan PeekabooEasterEggCooldown = TimeSpan.FromSeconds(30);
    private static readonly TimeSpan TeapotShakeSampleWindow = TimeSpan.FromMilliseconds(1250);
    private static readonly TimeSpan TeapotShakeCooldown = TimeSpan.FromSeconds(30);
    private static readonly string[] LoadingStoryboardKeys =
    [
        "HologramIdleStoryboard",
        "ScanLineStoryboard",
        "PhasePulseStoryboard",
        "HudEntranceStoryboard"
    ];
    private const int PeekabooRequiredToggleCount = 6;
    private const int TeapotShakeRequiredDirectionChanges = 5;
    private const double TeapotShakeRequiredTravelDistance = 320d;
    private static readonly Key[] KonamiSequence =
    [
        Key.Up,
        Key.Up,
        Key.Down,
        Key.Down,
        Key.Left,
        Key.Right,
        Key.Left,
        Key.Right,
        Key.B,
        Key.A
    ];

    private readonly MainWindowViewModel viewModel = new();
    private readonly Queue<DateTimeOffset> homeIconClickTimes = [];
    private readonly Queue<DateTimeOffset> peekabooToggleTimes = [];
    private readonly Queue<WindowDragSample> titleBarDragSamples = [];
    private AppSettings? observedSettings;
    private HwndSource? windowSource;
    private bool isGracefulShutdownInProgress;
    private bool isGracefulShutdownCompleted;
    private bool wasWindowMinimized;
    private int konamiSequenceIndex;
    private bool isAdvertisementPlaying;
    private bool isTrackingTitleBarShake;
    private bool shouldShowTeapotDialogAfterDrag;
    private bool isTeapotDialogOpen;
    private bool isTrayHideInProgress;
    private bool isMainWindowHiddenToTray;
    private CancellationTokenSource? applicationUpdateCheckCancellation;
    private WindowState lastNonMinimizedWindowState = WindowState.Normal;
    private MediaPlayer? homeIconEasterEggPlayer;
    private string? homeIconEasterEggAudioTempPath;
    private MediaPlayer? peekabooEasterEggPlayer;
    private string? peekabooEasterEggAudioTempPath;
    private MediaPlayer? teapotEasterEggPlayer;
    private string? teapotEasterEggAudioTempPath;
    private DebugLogWindow? debugLogWindow;
    private string? advertisementVideoTempPath;
    private AppTheme? loadedThemeBackground;
    private WinForms.NotifyIcon? trayNotifyIcon;
    private Drawing.Icon? trayIconImage;
    private readonly ApplicationRestartSessionState? restartRestoreState;
    private DateTimeOffset peekabooLastTriggeredAt = DateTimeOffset.MinValue;
    private DateTimeOffset teapotLastShownAt = DateTimeOffset.MinValue;

    public MainWindow()
        : this(null)
    {
    }

    internal MainWindow(ApplicationRestartSessionState? restartRestoreState)
    {
        this.restartRestoreState = restartRestoreState;
        InitializeComponent();
        DataContext = viewModel;
        WindowPlacementStateStore.ApplyWindowPlacement(
            this,
            restartRestoreState?.Windows.FirstOrDefault(window =>
                string.Equals(window.WindowKey, WindowPlacementStateStore.MainWindowKey, StringComparison.Ordinal)));
        SourceInitialized += OnSourceInitialized;
        Loaded += OnLoaded;
        Closing += OnClosing;
        StateChanged += OnStateChanged;
        LocationChanged += OnLocationChanged;
        PreviewKeyDown += OnPreviewKeyDown;
        viewModel.PropertyChanged += OnViewModelPropertyChanged;
        ThemeManager.ThemeChanged += OnThemeManagerThemeChanged;
        AttachSettingsHandlers(viewModel.Settings);
        ApplyTheme(viewModel.SelectedTheme);
        InitializeTrayIcon();
        UpdateWindowStateGlyph();
        wasWindowMinimized = WindowState == WindowState.Minimized;
        lastNonMinimizedWindowState = WindowState == WindowState.Minimized ? WindowState.Normal : WindowState;
    }

    private async void OnLoaded(object sender, RoutedEventArgs e)
    {
        LoadingOverlay.Visibility = Visibility.Visible;
        StartLoadingAnimations();
        CreateStarField();
        await Dispatcher.InvokeAsync(() => { }, DispatcherPriority.ApplicationIdle);

        await viewModel.InitializeAsync();
        ApplyTheme(viewModel.SelectedTheme);

        // Reveal transition
        await RunRevealTransitionAsync();

        StopLoadingAnimations();
        LoadingOverlay.Visibility = Visibility.Collapsed;
        RestoreRestartSessionWindows();
        QueueApplicationUpdateCheck();
        ShowAvatarSwapMigrationNoticeIfNeeded();
        _ = viewModel.CheckForPendingCrashReportAsync();
    }

    private void ShowAvatarSwapMigrationNoticeIfNeeded()
    {
        if (!viewModel.ShowMigrationNotice)
        {
            return;
        }

        MessageBox.Show(
            this,
            LocalizationService.Translate(
                "Avatar Swap has been reworked! Avatar Roulette is now its own card type, and Bits / Subs / Payment each have their own section. The per-avatar 'Return Avatar' is gone — all swaps now return to the global Return Avatar at the top of the window. This notice will not appear again."),
            LocalizationService.Translate("Avatar Swap Manager"),
            MessageBoxButton.OK,
            MessageBoxImage.Information);
        viewModel.DismissMigrationNoticeCommand.Execute(null);
    }

    private async void OnClosing(object? sender, CancelEventArgs e)
    {
        if (isAdvertisementPlaying)
        {
            e.Cancel = true;
            return;
        }

        if (isGracefulShutdownCompleted)
        {
            return;
        }

        if (isGracefulShutdownInProgress)
        {
            e.Cancel = true;
            return;
        }

        e.Cancel = true;
        isGracefulShutdownInProgress = true;
        IsEnabled = false;
        applicationUpdateCheckCancellation?.Cancel();
        applicationUpdateCheckCancellation?.Dispose();
        applicationUpdateCheckCancellation = null;

        SourceInitialized -= OnSourceInitialized;
        Loaded -= OnLoaded;
        Closing -= OnClosing;
        StateChanged -= OnStateChanged;
        LocationChanged -= OnLocationChanged;
        PreviewKeyDown -= OnPreviewKeyDown;
        viewModel.PropertyChanged -= OnViewModelPropertyChanged;
        ThemeManager.ThemeChanged -= OnThemeManagerThemeChanged;
        DetachSettingsHandlers();
        windowSource?.RemoveHook(WindowMessageHook);
        try
        {
            await viewModel.DisposeAsync();
        }
        catch
        {
            // Let shutdown continue. The app-level crash logger still captures hard failures.
        }
        finally
        {
            CleanupHomeIconEasterEggAudio();
            CleanupPeekabooEasterEggAudio();
            CleanupTeapotEasterEggAudio();
            CloseAdvertisementOverlay();
            CleanupTrayIcon();
            isGracefulShutdownCompleted = true;
            isGracefulShutdownInProgress = false;
            IsEnabled = true;
            Close();
        }
    }

    private void OnSourceInitialized(object? sender, EventArgs e)
    {
        windowSource = (HwndSource?)PresentationSource.FromVisual(this);
        windowSource?.AddHook(WindowMessageHook);
    }

    private void OnViewModelPropertyChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName == nameof(MainWindowViewModel.SelectedTheme))
        {
            ApplyTheme(viewModel.SelectedTheme);
            return;
        }

        if (e.PropertyName == nameof(MainWindowViewModel.Settings))
        {
            AttachSettingsHandlers(viewModel.Settings);
        }
    }

    private void OnThemeManagerThemeChanged(object? sender, EventArgs e)
    {
        Dispatcher.BeginInvoke(() => ApplyTheme(ThemeManager.CurrentTheme));
    }

    private bool AreEasterEggsEnabled => viewModel.Settings.EasterEggsEnabled;

    private void AttachSettingsHandlers(AppSettings settings)
    {
        if (ReferenceEquals(observedSettings, settings))
        {
            return;
        }

        DetachSettingsHandlers();
        observedSettings = settings;
        observedSettings.PropertyChanged += OnAppSettingsPropertyChanged;
    }

    private void DetachSettingsHandlers()
    {
        if (observedSettings is null)
        {
            return;
        }

        observedSettings.PropertyChanged -= OnAppSettingsPropertyChanged;
        observedSettings = null;
    }

    private void OnAppSettingsPropertyChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName != nameof(AppSettings.EasterEggsEnabled))
        {
            return;
        }

        ResetEasterEggState(stopActiveMedia: !AreEasterEggsEnabled);
    }

    private void OnChooseCustomThemeBackgroundImageClicked(object sender, RoutedEventArgs e)
    {
        var fileDialog = new OpenFileDialog
        {
            Title = LocalizationService.Translate("Choose Image"),
            Filter = "Image Files|*.png;*.jpg;*.jpeg;*.bmp;*.gif;*.webp",
            CheckFileExists = true,
            Multiselect = false
        };

        if (fileDialog.ShowDialog(this) != true)
        {
            return;
        }

        try
        {
            viewModel.Settings.CustomTheme.BackgroundImageRelativePath =
                ThemeAssetStore.ImportBackgroundImage(fileDialog.FileName);
        }
        catch (Exception ex)
        {
            ThemedDialogWindow.ShowOk(
                this,
                viewModel.SelectedTheme,
                LocalizationService.Translate("Custom Theme"),
                LocalizationService.Format("Crystal Relay could not import that background image.\n\n{0}", ex.Message),
                LocalizationService.Translate("OK"));
        }
    }

    private void OnClearCustomThemeBackgroundImageClicked(object sender, RoutedEventArgs e)
    {
        ThemeAssetStore.ClearBackgroundImage(viewModel.Settings.CustomTheme.BackgroundImageRelativePath);
        viewModel.Settings.CustomTheme.BackgroundImageRelativePath = string.Empty;
    }

    private void ResetEasterEggState(bool stopActiveMedia)
    {
        homeIconClickTimes.Clear();
        peekabooToggleTimes.Clear();
        titleBarDragSamples.Clear();
        konamiSequenceIndex = 0;
        isTrackingTitleBarShake = false;
        shouldShowTeapotDialogAfterDrag = false;

        if (!stopActiveMedia)
        {
            return;
        }

        CleanupHomeIconEasterEggAudio();
        CleanupPeekabooEasterEggAudio();
        CleanupTeapotEasterEggAudio();
        CloseAdvertisementOverlay();
    }

    private void OnHelpButtonClicked(object sender, RoutedEventArgs e)
    {
        if (sender is not Button button)
        {
            return;
        }

        var title = button.CommandParameter as string;
        var message = button.Tag?.ToString();
        if (string.IsNullOrWhiteSpace(message))
        {
            return;
        }

        var translatedTitle = string.IsNullOrWhiteSpace(title)
            ? LocalizationService.Translate("Help")
            : LocalizationService.Translate(title);
        var translatedMessage = LocalizationService.Translate(message);

        ThemedDialogWindow.ShowOk(this, viewModel.SelectedTheme, translatedTitle, translatedMessage);
    }

    private void OnFoomaHelpButtonClicked(object sender, RoutedEventArgs e)
    {
        var shouldOpenFoomaPage = ThemedDialogWindow.ShowYesNo(
            this,
            viewModel.SelectedTheme,
            "Fooma Twitch Interaction",
            "Universal Triggers can import Fooma Twitch Interaction JSON configs for assist-style OSC actions. If you do not have that system yet, Crystal Relay can open its Gumroad page for you.",
            "Open Fooma Page",
            "Close");

        if (!shouldOpenFoomaPage)
        {
            return;
        }

        OpenExternalUri(FoomaTwitchInteractionUrl);
    }

    private void OnPickManagedRewardColorClicked(object sender, RoutedEventArgs e)
    {
        if (sender is not Button button)
        {
            return;
        }

        var isCooldownColor = string.Equals(button.Tag?.ToString(), "Cooldown", StringComparison.OrdinalIgnoreCase);
        var fallbackColor = isCooldownColor
            ? ManagedRewardPresentation.InUseBackgroundColor
            : ManagedRewardPresentation.ReadyBackgroundColor;
        var initialColor = button.DataContext switch
        {
            TriggerRule rule => isCooldownColor
                ? rule.ManagedRewardCooldownColor
                : rule.ManagedRewardReadyColor,
            UniversalTriggerRule trigger => isCooldownColor
                ? trigger.ManagedRewardCooldownColor
                : trigger.ManagedRewardReadyColor,
            AvatarScaleRule scaleRule => isCooldownColor
                ? scaleRule.ManagedRewardCooldownColor
                : scaleRule.ManagedRewardReadyColor,
            AvatarScaleMasterRewardSettings masterReward => isCooldownColor
                ? masterReward.ManagedRewardCooldownColor
                : masterReward.ManagedRewardReadyColor,
            AvatarTriggerProfile profile => isCooldownColor
                ? profile.SetTriggerMasterRewardCooldownColor
                : profile.SetTriggerMasterRewardReadyColor,
            RewardFireSaleSettings fireSale => isCooldownColor
                ? fireSale.FundingRewardCooldownColor
                : fireSale.FundingRewardReadyColor,
            _ => string.Empty
        };

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
        switch (button.DataContext)
        {
            case TriggerRule rule when isCooldownColor:
                rule.ManagedRewardCooldownColor = selectedColor;
                break;
            case TriggerRule rule:
                rule.ManagedRewardReadyColor = selectedColor;
                break;
            case UniversalTriggerRule trigger when isCooldownColor:
                trigger.ManagedRewardCooldownColor = selectedColor;
                break;
            case UniversalTriggerRule trigger:
                trigger.ManagedRewardReadyColor = selectedColor;
                break;
            case AvatarScaleRule scaleRule when isCooldownColor:
                scaleRule.ManagedRewardCooldownColor = selectedColor;
                break;
            case AvatarScaleRule scaleRule:
                scaleRule.ManagedRewardReadyColor = selectedColor;
                break;
            case AvatarScaleMasterRewardSettings masterReward when isCooldownColor:
                masterReward.ManagedRewardCooldownColor = selectedColor;
                break;
            case AvatarScaleMasterRewardSettings masterReward:
                masterReward.ManagedRewardReadyColor = selectedColor;
                break;
            case AvatarTriggerProfile profile when isCooldownColor:
                profile.SetTriggerMasterRewardCooldownColor = selectedColor;
                break;
            case AvatarTriggerProfile profile:
                profile.SetTriggerMasterRewardReadyColor = selectedColor;
                break;
            case RewardFireSaleSettings fireSale when isCooldownColor:
                fireSale.FundingRewardCooldownColor = selectedColor;
                break;
            case RewardFireSaleSettings fireSale:
                fireSale.FundingRewardReadyColor = selectedColor;
                break;
        }
    }

    private void OnAddSupporterGrowthBitRangeClicked(object sender, RoutedEventArgs e)
    {
        if (DataContext is not MainWindowViewModel { SelectedAvatarScaleRule: { } rule })
        {
            return;
        }

        rule.SupporterGrowthBitRanges.Add(new AvatarScaleBitGrowthRange());
    }

    private void OnRemoveSupporterGrowthBitRangeClicked(object sender, RoutedEventArgs e)
    {
        if (sender is not FrameworkElement { DataContext: AvatarScaleBitGrowthRange range }
            || DataContext is not MainWindowViewModel { SelectedAvatarScaleRule: { } rule }
            || rule.SupporterGrowthBitRanges.Count <= 1)
        {
            return;
        }

        rule.SupporterGrowthBitRanges.Remove(range);
    }

    private void OnAddSupporterFloatAddRangeClicked(object sender, RoutedEventArgs e)
    {
        if (DataContext is not MainWindowViewModel { SelectedRule: { } rule })
        {
            return;
        }

        rule.SupporterFloatAddRanges.Add(new SupporterFloatAddRange());
    }

    private void OnRemoveSupporterFloatAddRangeClicked(object sender, RoutedEventArgs e)
    {
        if (sender is not FrameworkElement { DataContext: SupporterFloatAddRange range }
            || DataContext is not MainWindowViewModel { SelectedRule: { } rule }
            || rule.SupporterFloatAddRanges.Count <= 1)
        {
            return;
        }

        rule.SupporterFloatAddRanges.Remove(range);
    }

    private void ScaleActionModeButton_Click(object sender, RoutedEventArgs e)
    {
        if (sender is not FrameworkElement fe || fe.Tag is not string tagName) return;
        if (!Enum.TryParse<AvatarScaleMode>(tagName, out var mode)) return;
        if (DataContext is MainWindowViewModel vm) vm.SetSelectedAvatarScaleMode(mode);
    }

    private void ScaleActionMultOpButton_Click(object sender, RoutedEventArgs e)
    {
        if (DataContext is not MainWindowViewModel vm) return;
        if (vm.SelectedAvatarScaleRule is not { } rule) return;
        rule.MultiplierDirectionId = rule.MultiplierDirection == AvatarScaleMultiplierDirection.Grow
            ? (int)AvatarScaleMultiplierDirection.Divide
            : (int)AvatarScaleMultiplierDirection.Grow;
    }

    private void ScaleActionRelHeightOpButton_Click(object sender, RoutedEventArgs e)
    {
        if (DataContext is not MainWindowViewModel vm) return;
        if (vm.SelectedAvatarScaleRule is not { } rule) return;
        rule.RelativeHeightDirectionId = rule.IsSubtractRelativeHeight
            ? (int)AvatarScaleRelativeHeightDirection.Add
            : (int)AvatarScaleRelativeHeightDirection.Subtract;
    }

    private void ApplyTheme(AppTheme theme)
    {
        ApplyThemeBackground(theme);
        ThemeManager.ApplyToResources(Resources, theme);
    }

    private void ApplyThemeBackground(AppTheme theme)
    {
        if (theme == AppTheme.Custom)
        {
            ThemeBackgroundHost.Content = ThemeManager.CreateMainWindowBackgroundElement();
            loadedThemeBackground = AppTheme.Custom;
            return;
        }

        if (loadedThemeBackground == theme && ThemeBackgroundHost.Content is not null)
        {
            return;
        }

        ThemeBackgroundHost.Content = LoadThemeBackground(theme);
        loadedThemeBackground = theme;
    }

    private static FrameworkElement LoadThemeBackground(AppTheme theme)
    {
        var componentPath = theme switch
        {
            AppTheme.VoidCrystal => "ThemeBackgrounds/VoidCrystalThemeBackground.xaml",
            AppTheme.TreetendersArm => "ThemeBackgrounds/TreetendersArmThemeBackground.xaml",
            AppTheme.DreamScape => "ThemeBackgrounds/DreamScapeThemeBackground.xaml",
            AppTheme.MainFrame => "ThemeBackgrounds/MainFrameThemeBackground.xaml",
            AppTheme.TrashKitty => "ThemeBackgrounds/TrashKittyThemeBackground.xaml",
            AppTheme.Bratwurst => "ThemeBackgrounds/BratwurstThemeBackground.xaml",
            AppTheme.CarrotPatch => "ThemeBackgrounds/CarrotPatchThemeBackground.xaml",
            AppTheme.Bubblegum => "ThemeBackgrounds/BubblegumThemeBackground.xaml",
            AppTheme.CosmicPuppyGirl => "ThemeBackgrounds/CosmicPuppyGirlThemeBackground.xaml",
            AppTheme.PeachesAndCream => "ThemeBackgrounds/PeachesAndCreamThemeBackground.xaml",
            AppTheme.MoonBunnyWink => "ThemeBackgrounds/MoonBunnyWinkThemeBackground.xaml",
            AppTheme.DreadNightBar => "ThemeBackgrounds/DreadNightBarThemeBackground.xaml",
            AppTheme.Baked => "ThemeBackgrounds/BakedThemeBackground.xaml",
            AppTheme.NeonBorb => "ThemeBackgrounds/NeonBorbThemeBackground.xaml",
            AppTheme.StinkyOnline => "ThemeBackgrounds/StinkyOnlineThemeBackground.xaml",
            AppTheme.SquishyFoxPlush => "ThemeBackgrounds/SquishyFoxPlushThemeBackground.xaml",
            AppTheme.Puca => "ThemeBackgrounds/PucaThemeBackground.xaml",
            _ => "ThemeBackgrounds/VoidCrystalThemeBackground.xaml"
        };

        return Application.LoadComponent(new Uri(componentPath, UriKind.Relative)) as FrameworkElement
            ?? throw new InvalidOperationException($"Could not load the theme background view '{componentPath}'.");
    }

    private void ApplyVoidCrystalTheme()
    {
        SetFontFamilyResource("BodyFontFamily", "Verdana");
        SetFontFamilyResource("HeadingFontFamily", "Constantia");
        Resources["HomeTitleFontSize"] = 24d;
        SetBrushColor("WindowBackgroundBrush", "#130B1E");
        SetBrushColor("PanelBrush", "#CC1C132B");
        SetBrushColor("PanelSecondaryBrush", "#C8241739");
        SetBrushColor("PanelHighlightBrush", "#C72E1A4B");
        SetBrushColor("BorderBrush", "#5A3D92");
        SetBrushColor("AccentBrush", "#B16BFF");
        SetBrushColor("TextBrush", "#F5EEFF");
        SetBrushColor("MutedBrush", "#BFAFD8");
        SetBrushColor("InputBrush", "#B8271A3D");
        SetBrushColor("InputBorderBrush", "#7552BC");
        SetBrushColor("ComboSurfaceBrush", "#F7F2FF");
        SetBrushColor("ComboTextBrush", "#140C20");
        SetBrushColor("ComboHighlightBrush", "#D8C2FF");
        SetBrushColor("SecondaryButtonBrush", "#2C1C48");
        SetBrushColor("SecondaryButtonBorderBrush", "#7F5FD0");
        SetBrushColor("SectionActiveBrush", "#8E54FF");
        SetBrushColor("RuleCardBrush", "#B8241739");
        SetBrushColor("RuleCardSelectedBrush", "#351C57");
        SetBrushColor("RuleCardHoverBrush", "#A978FF");
        SetBrushColor("StatusChipBrush", "#8E241841");
        SetBrushColor("NestedPanelBrush", "#B8241739");
        SetBrushColor("DangerBrush", "#4A1729");
        SetBrushColor("DangerBorderBrush", "#B45A84");
        SetBrushColor("HighlightBorderBrush", "#A978FF");
        SetBrushColor("PopupBorderBrush", "#8C6EE8");
        SetBrushColor("ComboDropButtonBrush", "#E9DBFF");
        SetBrushColor("ComboDropButtonHoverBrush", "#D9C2FF");
        SetBrushColor("ComboDropButtonPressedBrush", "#C7A8FF");
        SetBrushColor("ScrollTrackBrush", "#25183D");
        SetBrushColor("ScrollThumbBrush", "#7B57D0");
        SetBrushColor("ScrollThumbHoverBrush", "#966AF0");
        SetBrushColor("ScrollThumbPressedBrush", "#B388FF");
        SetBrushColor("TitleBarBrush", "#20122F");
        SetBrushColor("TitleBarTextBrush", "#F5EEFF");
        SetBrushColor("TitleBarSubTextBrush", "#CBB9E5");
        SetBrushColor("TitleBarButtonBrush", "#00000000");
        SetBrushColor("TitleBarButtonHoverBrush", "#3B235B");
        SetBrushColor("TitleBarButtonPressedBrush", "#543183");
        SetBrushColor("TitleBarCloseHoverBrush", "#B43D62");
        SetBrushColor("TitleBarClosePressedBrush", "#8C2648");
    }

    private void ApplyPeachesAndCreamTheme()
    {
        SetFontFamilyResource("BodyFontFamily", "Calibri");
        SetFontFamilyResource("HeadingFontFamily", "Georgia");
        Resources["HomeTitleFontSize"] = 24d;
        SetBrushColor("WindowBackgroundBrush", "#FFF9EE");
        SetBrushColor("PanelBrush", "#DDFBF7ED");
        SetBrushColor("PanelSecondaryBrush", "#D8FFF5E7");
        SetBrushColor("PanelHighlightBrush", "#DDFDEFD8");
        SetBrushColor("BorderBrush", "#4DB6BE");
        SetBrushColor("AccentBrush", "#F5826F");
        SetBrushColor("TextBrush", "#2C2A2B");
        SetBrushColor("MutedBrush", "#635B58");
        SetBrushColor("InputBrush", "#D7FFF8F1");
        SetBrushColor("InputBorderBrush", "#5BB8BF");
        SetBrushColor("ComboSurfaceBrush", "#FFFDF6");
        SetBrushColor("ComboTextBrush", "#262323");
        SetBrushColor("ComboHighlightBrush", "#FFE0D6");
        SetBrushColor("SecondaryButtonBrush", "#4DB6BE");
        SetBrushColor("SecondaryButtonBorderBrush", "#379AA2");
        SetBrushColor("SectionActiveBrush", "#F5826F");
        SetBrushColor("RuleCardBrush", "#DDEFE8DB");
        SetBrushColor("RuleCardSelectedBrush", "#FFF2E8");
        SetBrushColor("RuleCardHoverBrush", "#F6A592");
        SetBrushColor("StatusChipBrush", "#E051B2B9");
        SetBrushColor("NestedPanelBrush", "#D8FFF6EC");
        SetBrushColor("DangerBrush", "#E49786");
        SetBrushColor("DangerBorderBrush", "#CE7260");
        SetBrushColor("HighlightBorderBrush", "#F2A08E");
        SetBrushColor("PopupBorderBrush", "#4DB6BE");
        SetBrushColor("ComboDropButtonBrush", "#FFE7DE");
        SetBrushColor("ComboDropButtonHoverBrush", "#FFD8CC");
        SetBrushColor("ComboDropButtonPressedBrush", "#FFC8BA");
        SetBrushColor("ScrollTrackBrush", "#EEE6D8");
        SetBrushColor("ScrollThumbBrush", "#53B5BD");
        SetBrushColor("ScrollThumbHoverBrush", "#65C0C8");
        SetBrushColor("ScrollThumbPressedBrush", "#79CDD4");
        SetBrushColor("TitleBarBrush", "#F5826F");
        SetBrushColor("TitleBarTextBrush", "#FFFDF8");
        SetBrushColor("TitleBarSubTextBrush", "#FFF0EA");
        SetBrushColor("TitleBarButtonBrush", "#00000000");
        SetBrushColor("TitleBarButtonHoverBrush", "#E67463");
        SetBrushColor("TitleBarButtonPressedBrush", "#D66658");
        SetBrushColor("TitleBarCloseHoverBrush", "#CB5A56");
        SetBrushColor("TitleBarClosePressedBrush", "#B84D4A");
    }

    private void ApplyDreamScapeTheme()
    {
        SetFontFamilyResource("BodyFontFamily", "Leelawadee UI");
        SetFontFamilyResource("HeadingFontFamily", "Segoe Print");
        Resources["HomeTitleFontSize"] = 22d;
        SetBrushColor("WindowBackgroundBrush", "#08132F");
        SetBrushColor("PanelBrush", "#CC1E1A56");
        SetBrushColor("PanelSecondaryBrush", "#CB20276A");
        SetBrushColor("PanelHighlightBrush", "#CC2C2D7E");
        SetBrushColor("BorderBrush", "#5F8BFF");
        SetBrushColor("AccentBrush", "#FF58D8");
        SetBrushColor("TextBrush", "#FFF6FF");
        SetBrushColor("MutedBrush", "#E8D8FF");
        SetBrushColor("InputBrush", "#BC222560");
        SetBrushColor("InputBorderBrush", "#7B92FF");
        SetBrushColor("ComboSurfaceBrush", "#FFF5FF");
        SetBrushColor("ComboTextBrush", "#140F2A");
        SetBrushColor("ComboHighlightBrush", "#E7DBFF");
        SetBrushColor("SecondaryButtonBrush", "#22347A");
        SetBrushColor("SecondaryButtonBorderBrush", "#6C8EFF");
        SetBrushColor("SectionActiveBrush", "#7C62FF");
        SetBrushColor("RuleCardBrush", "#BE211D5A");
        SetBrushColor("RuleCardSelectedBrush", "#3E3AA0");
        SetBrushColor("RuleCardHoverBrush", "#FF73D9");
        SetBrushColor("StatusChipBrush", "#A82F276D");
        SetBrushColor("NestedPanelBrush", "#BD232462");
        SetBrushColor("DangerBrush", "#A92955");
        SetBrushColor("DangerBorderBrush", "#FFA1CD");
        SetBrushColor("HighlightBorderBrush", "#FF6ECF");
        SetBrushColor("PopupBorderBrush", "#7B92FF");
        SetBrushColor("ComboDropButtonBrush", "#F9D7FF");
        SetBrushColor("ComboDropButtonHoverBrush", "#F4BFFF");
        SetBrushColor("ComboDropButtonPressedBrush", "#E7A4FF");
        SetBrushColor("ScrollTrackBrush", "#1A2459");
        SetBrushColor("ScrollThumbBrush", "#FF68D8");
        SetBrushColor("ScrollThumbHoverBrush", "#FF88E2");
        SetBrushColor("ScrollThumbPressedBrush", "#FFA6ED");
        SetBrushColor("TitleBarBrush", "#14245E");
        SetBrushColor("TitleBarTextBrush", "#FFF7FF");
        SetBrushColor("TitleBarSubTextBrush", "#F0DFFF");
        SetBrushColor("TitleBarButtonBrush", "#00000000");
        SetBrushColor("TitleBarButtonHoverBrush", "#3447A8");
        SetBrushColor("TitleBarButtonPressedBrush", "#5165DD");
        SetBrushColor("TitleBarCloseHoverBrush", "#D03B75");
        SetBrushColor("TitleBarClosePressedBrush", "#A82A5A");
    }

    private void ApplyMainFrameTheme()
    {
        SetFontFamilyResource("BodyFontFamily", "Consolas");
        SetFontFamilyResource("HeadingFontFamily", "Bahnschrift");
        Resources["HomeTitleFontSize"] = 24d;
        SetBrushColor("WindowBackgroundBrush", "#05070D");
        SetBrushColor("PanelBrush", "#CC111524");
        SetBrushColor("PanelSecondaryBrush", "#CC151A2B");
        SetBrushColor("PanelHighlightBrush", "#CC172036");
        SetBrushColor("BorderBrush", "#5B43A8");
        SetBrushColor("AccentBrush", "#67F7A6");
        SetBrushColor("TextBrush", "#F2F7FF");
        SetBrushColor("MutedBrush", "#A4AFCA");
        SetBrushColor("InputBrush", "#BF141B2C");
        SetBrushColor("InputBorderBrush", "#7058C4");
        SetBrushColor("ComboSurfaceBrush", "#F3F8FF");
        SetBrushColor("ComboTextBrush", "#09111A");
        SetBrushColor("ComboHighlightBrush", "#CEF7DA");
        SetBrushColor("SecondaryButtonBrush", "#1A2139");
        SetBrushColor("SecondaryButtonBorderBrush", "#5A47A1");
        SetBrushColor("SectionActiveBrush", "#4E3BA4");
        SetBrushColor("RuleCardBrush", "#BC151C2D");
        SetBrushColor("RuleCardSelectedBrush", "#1C2745");
        SetBrushColor("RuleCardHoverBrush", "#72FFB3");
        SetBrushColor("StatusChipBrush", "#A01B2440");
        SetBrushColor("NestedPanelBrush", "#BC161C2E");
        SetBrushColor("DangerBrush", "#4B1724");
        SetBrushColor("DangerBorderBrush", "#B95D79");
        SetBrushColor("HighlightBorderBrush", "#72FFB3");
        SetBrushColor("PopupBorderBrush", "#6954BA");
        SetBrushColor("ComboDropButtonBrush", "#D9FBE5");
        SetBrushColor("ComboDropButtonHoverBrush", "#C6F4D6");
        SetBrushColor("ComboDropButtonPressedBrush", "#AFEBC4");
        SetBrushColor("ScrollTrackBrush", "#0E1320");
        SetBrushColor("ScrollThumbBrush", "#5744A4");
        SetBrushColor("ScrollThumbHoverBrush", "#6C56C7");
        SetBrushColor("ScrollThumbPressedBrush", "#8570E3");
        SetBrushColor("TitleBarBrush", "#0F141F");
        SetBrushColor("TitleBarTextBrush", "#F2F7FF");
        SetBrushColor("TitleBarSubTextBrush", "#B4C0D8");
        SetBrushColor("TitleBarButtonBrush", "#00000000");
        SetBrushColor("TitleBarButtonHoverBrush", "#1E2940");
        SetBrushColor("TitleBarButtonPressedBrush", "#29385A");
        SetBrushColor("TitleBarCloseHoverBrush", "#8F2F57");
        SetBrushColor("TitleBarClosePressedBrush", "#712243");
    }

    private void ApplyTrashKittyTheme()
    {
        SetFontFamilyResource("BodyFontFamily", "Trebuchet MS");
        SetFontFamilyResource("HeadingFontFamily", "Bahnschrift");
        Resources["HomeTitleFontSize"] = 24d;
        SetBrushColor("WindowBackgroundBrush", "#07090C");
        SetBrushColor("PanelBrush", "#D014171B");
        SetBrushColor("PanelSecondaryBrush", "#D01F242B");
        SetBrushColor("PanelHighlightBrush", "#D02C333B");
        SetBrushColor("BorderBrush", "#62758F");
        SetBrushColor("AccentBrush", "#9FC8FF");
        SetBrushColor("TextBrush", "#F1F5F8");
        SetBrushColor("MutedBrush", "#B7C1CB");
        SetBrushColor("InputBrush", "#C51C2228");
        SetBrushColor("InputBorderBrush", "#7C92AF");
        SetBrushColor("ComboSurfaceBrush", "#EEF4FA");
        SetBrushColor("ComboTextBrush", "#101419");
        SetBrushColor("ComboHighlightBrush", "#C9DFFF");
        SetBrushColor("SecondaryButtonBrush", "#2E363F");
        SetBrushColor("SecondaryButtonBorderBrush", "#7C92AF");
        SetBrushColor("SectionActiveBrush", "#5F78A2");
        SetBrushColor("RuleCardBrush", "#C91B2026");
        SetBrushColor("RuleCardSelectedBrush", "#2D3846");
        SetBrushColor("RuleCardHoverBrush", "#9FC8FF");
        SetBrushColor("StatusChipBrush", "#A62A323A");
        SetBrushColor("NestedPanelBrush", "#C620262D");
        SetBrushColor("DangerBrush", "#4A2127");
        SetBrushColor("DangerBorderBrush", "#B46A7A");
        SetBrushColor("HighlightBorderBrush", "#9FC8FF");
        SetBrushColor("PopupBorderBrush", "#7C92AF");
        SetBrushColor("ComboDropButtonBrush", "#DDEBFA");
        SetBrushColor("ComboDropButtonHoverBrush", "#CCE0F7");
        SetBrushColor("ComboDropButtonPressedBrush", "#B8D1EF");
        SetBrushColor("ScrollTrackBrush", "#14191F");
        SetBrushColor("ScrollThumbBrush", "#596A7F");
        SetBrushColor("ScrollThumbHoverBrush", "#7189A5");
        SetBrushColor("ScrollThumbPressedBrush", "#94B8E8");
        SetBrushColor("TitleBarBrush", "#101317");
        SetBrushColor("TitleBarTextBrush", "#F1F5F8");
        SetBrushColor("TitleBarSubTextBrush", "#B9C5D1");
        SetBrushColor("TitleBarButtonBrush", "#00000000");
        SetBrushColor("TitleBarButtonHoverBrush", "#2B333C");
        SetBrushColor("TitleBarButtonPressedBrush", "#374454");
        SetBrushColor("TitleBarCloseHoverBrush", "#5E7085");
        SetBrushColor("TitleBarClosePressedBrush", "#435466");
    }

    private void ApplyBubblegumTheme()
    {
        SetFontFamilyResource("BodyFontFamily", "Candara");
        SetFontFamilyResource("HeadingFontFamily", "Ink Free");
        Resources["HomeTitleFontSize"] = 23d;
        SetBrushColor("WindowBackgroundBrush", "#FFF5FD");
        SetBrushColor("PanelBrush", "#D9FFF7FB");
        SetBrushColor("PanelSecondaryBrush", "#D2F7FBFF");
        SetBrushColor("PanelHighlightBrush", "#D9FFF8D9");
        SetBrushColor("BorderBrush", "#8BCBFF");
        SetBrushColor("AccentBrush", "#FF94C8");
        SetBrushColor("TextBrush", "#6A4A7B");
        SetBrushColor("MutedBrush", "#8A719A");
        SetBrushColor("InputBrush", "#C8FFF9FF");
        SetBrushColor("InputBorderBrush", "#99CFFD");
        SetBrushColor("ComboSurfaceBrush", "#FFFFFCFF");
        SetBrushColor("ComboTextBrush", "#5B4872");
        SetBrushColor("ComboHighlightBrush", "#FFF7D3");
        SetBrushColor("SecondaryButtonBrush", "#B9D8FF");
        SetBrushColor("SecondaryButtonBorderBrush", "#86B8F4");
        SetBrushColor("SectionActiveBrush", "#FF9AD0");
        SetBrushColor("RuleCardBrush", "#D7FFF2F9");
        SetBrushColor("RuleCardSelectedBrush", "#FFF9E0");
        SetBrushColor("RuleCardHoverBrush", "#FFC2E6");
        SetBrushColor("StatusChipBrush", "#D8FFF4D1");
        SetBrushColor("NestedPanelBrush", "#D5FFF8FC");
        SetBrushColor("DangerBrush", "#F6A7C3");
        SetBrushColor("DangerBorderBrush", "#F39ABA");
        SetBrushColor("HighlightBorderBrush", "#F5D77C");
        SetBrushColor("PopupBorderBrush", "#A7CFFF");
        SetBrushColor("ComboDropButtonBrush", "#FFE2F4");
        SetBrushColor("ComboDropButtonHoverBrush", "#FFD1ED");
        SetBrushColor("ComboDropButtonPressedBrush", "#FFC0E5");
        SetBrushColor("ScrollTrackBrush", "#E7F5FF");
        SetBrushColor("ScrollThumbBrush", "#FFAFD7");
        SetBrushColor("ScrollThumbHoverBrush", "#FF9ACA");
        SetBrushColor("ScrollThumbPressedBrush", "#F889BD");
        SetBrushColor("TitleBarBrush", "#B7D9FF");
        SetBrushColor("TitleBarTextBrush", "#5A426B");
        SetBrushColor("TitleBarSubTextBrush", "#715B82");
        SetBrushColor("TitleBarButtonBrush", "#00000000");
        SetBrushColor("TitleBarButtonHoverBrush", "#FFD3EA");
        SetBrushColor("TitleBarButtonPressedBrush", "#FFC0DF");
        SetBrushColor("TitleBarCloseHoverBrush", "#FF9CBF");
        SetBrushColor("TitleBarClosePressedBrush", "#F57FA8");
    }

    private void ApplyCosmicPuppyGirlTheme()
    {
        SetFontFamilyResource("BodyFontFamily", "Trebuchet MS");
        SetFontFamilyResource("HeadingFontFamily", "Book Antiqua");
        Resources["HomeTitleFontSize"] = 23d;
        SetBrushColor("WindowBackgroundBrush", "#120816");
        SetBrushColor("PanelBrush", "#CE1F132A");
        SetBrushColor("PanelSecondaryBrush", "#C7231433");
        SetBrushColor("PanelHighlightBrush", "#CF2A173A");
        SetBrushColor("BorderBrush", "#6B3A89");
        SetBrushColor("AccentBrush", "#93D944");
        SetBrushColor("TextBrush", "#F7F1FF");
        SetBrushColor("MutedBrush", "#D4C6E6");
        SetBrushColor("InputBrush", "#BA26183A");
        SetBrushColor("InputBorderBrush", "#7A4BA2");
        SetBrushColor("ComboSurfaceBrush", "#F8F4FF");
        SetBrushColor("ComboTextBrush", "#1A1124");
        SetBrushColor("ComboHighlightBrush", "#D4F0AE");
        SetBrushColor("SecondaryButtonBrush", "#351B4A");
        SetBrushColor("SecondaryButtonBorderBrush", "#6A4290");
        SetBrushColor("SectionActiveBrush", "#96D947");
        SetBrushColor("RuleCardBrush", "#BE2A173C");
        SetBrushColor("RuleCardSelectedBrush", "#4B244E");
        SetBrushColor("RuleCardHoverBrush", "#B14C62");
        SetBrushColor("StatusChipBrush", "#9D2A1C3E");
        SetBrushColor("NestedPanelBrush", "#BC251736");
        SetBrushColor("DangerBrush", "#632031");
        SetBrushColor("DangerBorderBrush", "#C65E79");
        SetBrushColor("HighlightBorderBrush", "#8FD33E");
        SetBrushColor("PopupBorderBrush", "#7A4BA2");
        SetBrushColor("ComboDropButtonBrush", "#E9D9FF");
        SetBrushColor("ComboDropButtonHoverBrush", "#DBCBF8");
        SetBrushColor("ComboDropButtonPressedBrush", "#CCB8EC");
        SetBrushColor("ScrollTrackBrush", "#261338");
        SetBrushColor("ScrollThumbBrush", "#9BDB4A");
        SetBrushColor("ScrollThumbHoverBrush", "#B6EC6F");
        SetBrushColor("ScrollThumbPressedBrush", "#C8F591");
        SetBrushColor("TitleBarBrush", "#2A1436");
        SetBrushColor("TitleBarTextBrush", "#F8F2FF");
        SetBrushColor("TitleBarSubTextBrush", "#D8CAE8");
        SetBrushColor("TitleBarButtonBrush", "#00000000");
        SetBrushColor("TitleBarButtonHoverBrush", "#3E2056");
        SetBrushColor("TitleBarButtonPressedBrush", "#573176");
        SetBrushColor("TitleBarCloseHoverBrush", "#B54562");
        SetBrushColor("TitleBarClosePressedBrush", "#8E334B");
    }

    private void ApplyDreadNightBarTheme()
    {
        SetFontFamilyResource("BodyFontFamily", "Cambria");
        SetFontFamilyResource("HeadingFontFamily", "Book Antiqua");
        Resources["HomeTitleFontSize"] = 24d;
        SetBrushColor("WindowBackgroundBrush", "#090708");
        SetBrushColor("PanelBrush", "#D2141318");
        SetBrushColor("PanelSecondaryBrush", "#CC18161B");
        SetBrushColor("PanelHighlightBrush", "#D01E181E");
        SetBrushColor("BorderBrush", "#8B2F3E");
        SetBrushColor("AccentBrush", "#C54757");
        SetBrushColor("TextBrush", "#F2ECEC");
        SetBrushColor("MutedBrush", "#BDAFB2");
        SetBrushColor("InputBrush", "#B41A171C");
        SetBrushColor("InputBorderBrush", "#7C3B45");
        SetBrushColor("ComboSurfaceBrush", "#F1ECEE");
        SetBrushColor("ComboTextBrush", "#20181B");
        SetBrushColor("ComboHighlightBrush", "#E6C4CA");
        SetBrushColor("SecondaryButtonBrush", "#2A2024");
        SetBrushColor("SecondaryButtonBorderBrush", "#7A3C46");
        SetBrushColor("SectionActiveBrush", "#C54757");
        SetBrushColor("RuleCardBrush", "#B31A171D");
        SetBrushColor("RuleCardSelectedBrush", "#402328");
        SetBrushColor("RuleCardHoverBrush", "#9A4A58");
        SetBrushColor("StatusChipBrush", "#9D291E24");
        SetBrushColor("NestedPanelBrush", "#B219171C");
        SetBrushColor("DangerBrush", "#4D151C");
        SetBrushColor("DangerBorderBrush", "#A24A57");
        SetBrushColor("HighlightBorderBrush", "#C75C69");
        SetBrushColor("PopupBorderBrush", "#8B2F3E");
        SetBrushColor("ComboDropButtonBrush", "#E7DADF");
        SetBrushColor("ComboDropButtonHoverBrush", "#DCC9D0");
        SetBrushColor("ComboDropButtonPressedBrush", "#D0B8C1");
        SetBrushColor("ScrollTrackBrush", "#221A1D");
        SetBrushColor("ScrollThumbBrush", "#8C3947");
        SetBrushColor("ScrollThumbHoverBrush", "#A84A59");
        SetBrushColor("ScrollThumbPressedBrush", "#C45E6E");
        SetBrushColor("TitleBarBrush", "#1C1418");
        SetBrushColor("TitleBarTextBrush", "#F6EFF0");
        SetBrushColor("TitleBarSubTextBrush", "#CDBFC2");
        SetBrushColor("TitleBarButtonBrush", "#00000000");
        SetBrushColor("TitleBarButtonHoverBrush", "#3B2328");
        SetBrushColor("TitleBarButtonPressedBrush", "#5A3138");
        SetBrushColor("TitleBarCloseHoverBrush", "#A23543");
        SetBrushColor("TitleBarClosePressedBrush", "#7D2430");
    }

    private void ApplyBakedTheme()
    {
        SetFontFamilyResource("BodyFontFamily", "Cambria");
        SetFontFamilyResource("HeadingFontFamily", "Georgia");
        Resources["HomeTitleFontSize"] = 24d;
        SetBrushColor("WindowBackgroundBrush", "#1A120D");
        SetBrushColor("PanelBrush", "#D2261A13");
        SetBrushColor("PanelSecondaryBrush", "#CC2D1F17");
        SetBrushColor("PanelHighlightBrush", "#D3332319");
        SetBrushColor("BorderBrush", "#B89267");
        SetBrushColor("AccentBrush", "#A2472A");
        SetBrushColor("TextBrush", "#F3E7D7");
        SetBrushColor("MutedBrush", "#C9B39A");
        SetBrushColor("InputBrush", "#B433251B");
        SetBrushColor("InputBorderBrush", "#A97C56");
        SetBrushColor("ComboSurfaceBrush", "#EFE1D0");
        SetBrushColor("ComboTextBrush", "#2C1B12");
        SetBrushColor("ComboHighlightBrush", "#D7BA9B");
        SetBrushColor("SecondaryButtonBrush", "#3A281C");
        SetBrushColor("SecondaryButtonBorderBrush", "#A47A56");
        SetBrushColor("SectionActiveBrush", "#A2472A");
        SetBrushColor("RuleCardBrush", "#BC2E2017");
        SetBrushColor("RuleCardSelectedBrush", "#4A3122");
        SetBrushColor("RuleCardHoverBrush", "#B97B58");
        SetBrushColor("StatusChipBrush", "#A939251A");
        SetBrushColor("NestedPanelBrush", "#B22F2016");
        SetBrushColor("DangerBrush", "#5A2318");
        SetBrushColor("DangerBorderBrush", "#B16652");
        SetBrushColor("HighlightBorderBrush", "#D3A173");
        SetBrushColor("PopupBorderBrush", "#B89267");
        SetBrushColor("ComboDropButtonBrush", "#E3D0BC");
        SetBrushColor("ComboDropButtonHoverBrush", "#D9C0A5");
        SetBrushColor("ComboDropButtonPressedBrush", "#CEB08F");
        SetBrushColor("ScrollTrackBrush", "#271A13");
        SetBrushColor("ScrollThumbBrush", "#9B6545");
        SetBrushColor("ScrollThumbHoverBrush", "#B57957");
        SetBrushColor("ScrollThumbPressedBrush", "#CE8F68");
        SetBrushColor("TitleBarBrush", "#221710");
        SetBrushColor("TitleBarTextBrush", "#F6EADB");
        SetBrushColor("TitleBarSubTextBrush", "#D6C0A9");
        SetBrushColor("TitleBarButtonBrush", "#00000000");
        SetBrushColor("TitleBarButtonHoverBrush", "#41291D");
        SetBrushColor("TitleBarButtonPressedBrush", "#5A3828");
        SetBrushColor("TitleBarCloseHoverBrush", "#8D3B29");
        SetBrushColor("TitleBarClosePressedBrush", "#6F2D1F");
    }

    private void ApplyMoonBunnyWinkTheme()
    {
        SetFontFamilyResource("BodyFontFamily", "Verdana");
        SetFontFamilyResource("HeadingFontFamily", "Segoe Print");
        Resources["HomeTitleFontSize"] = 23d;
        SetBrushColor("WindowBackgroundBrush", "#12091E");
        SetBrushColor("PanelBrush", "#D31C1230");
        SetBrushColor("PanelSecondaryBrush", "#CC24163A");
        SetBrushColor("PanelHighlightBrush", "#D226173F");
        SetBrushColor("BorderBrush", "#AA99D8");
        SetBrushColor("AccentBrush", "#F2D24F");
        SetBrushColor("TextBrush", "#FFFDFB");
        SetBrushColor("MutedBrush", "#D8CFE7");
        SetBrushColor("InputBrush", "#BE25183C");
        SetBrushColor("InputBorderBrush", "#B4A5E0");
        SetBrushColor("ComboSurfaceBrush", "#FFFDF8");
        SetBrushColor("ComboTextBrush", "#241C30");
        SetBrushColor("ComboHighlightBrush", "#FBE992");
        SetBrushColor("SecondaryButtonBrush", "#33204D");
        SetBrushColor("SecondaryButtonBorderBrush", "#8D7AC6");
        SetBrushColor("SectionActiveBrush", "#F2D24F");
        SetBrushColor("RuleCardBrush", "#C225163A");
        SetBrushColor("RuleCardSelectedBrush", "#40265A");
        SetBrushColor("RuleCardHoverBrush", "#C2A9FF");
        SetBrushColor("StatusChipBrush", "#A72B1C43");
        SetBrushColor("NestedPanelBrush", "#C2241638");
        SetBrushColor("DangerBrush", "#5A2440");
        SetBrushColor("DangerBorderBrush", "#C67AA2");
        SetBrushColor("HighlightBorderBrush", "#F2D24F");
        SetBrushColor("PopupBorderBrush", "#AA99D8");
        SetBrushColor("ComboDropButtonBrush", "#F6E9FF");
        SetBrushColor("ComboDropButtonHoverBrush", "#ECD8FF");
        SetBrushColor("ComboDropButtonPressedBrush", "#DFC8F5");
        SetBrushColor("ScrollTrackBrush", "#241633");
        SetBrushColor("ScrollThumbBrush", "#F0D34F");
        SetBrushColor("ScrollThumbHoverBrush", "#F6E07B");
        SetBrushColor("ScrollThumbPressedBrush", "#FFEA9D");
        SetBrushColor("TitleBarBrush", "#26153A");
        SetBrushColor("TitleBarTextBrush", "#FFFDFB");
        SetBrushColor("TitleBarSubTextBrush", "#ECE4F9");
        SetBrushColor("TitleBarButtonBrush", "#00000000");
        SetBrushColor("TitleBarButtonHoverBrush", "#3E2758");
        SetBrushColor("TitleBarButtonPressedBrush", "#58377A");
        SetBrushColor("TitleBarCloseHoverBrush", "#B85B7D");
        SetBrushColor("TitleBarClosePressedBrush", "#954766");
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

    private void SetFontFamilyResource(string resourceKey, string fontFamilyText)
    {
        var fontFamily = new FontFamily(fontFamilyText);
        Resources[resourceKey] = fontFamily;
    }

    // Startup update checks stay in the window layer because the final step is a themed
    // yes/no dialog tied to the current window chrome and browser-launch path.
    private void QueueApplicationUpdateCheck()
    {
        applicationUpdateCheckCancellation?.Cancel();
        applicationUpdateCheckCancellation?.Dispose();
        applicationUpdateCheckCancellation = new CancellationTokenSource();
        var cancellationToken = applicationUpdateCheckCancellation.Token;
        _ = CheckForApplicationUpdateAsync(cancellationToken);
    }

    private async Task CheckForApplicationUpdateAsync(CancellationToken cancellationToken)
    {
        try
        {
            var availableUpdate = await viewModel.GetPendingApplicationUpdateAsync(cancellationToken);
            if (availableUpdate is null
                || cancellationToken.IsCancellationRequested
                || isGracefulShutdownInProgress
                || !IsLoaded)
            {
                return;
            }

            if (!viewModel.IsApplicationSelfUpdateSupported)
            {
                var fallbackMessage = LocalizationService.Format(
                    "A newer Crystal Relay update is available.\n\nCurrent version: {0}\nLatest version: {1}\n\nIf you continue, Crystal Relay will open the GitHub release page in your browser.",
                    availableUpdate.CurrentVersion,
                    availableUpdate.LatestVersion);
                var shouldOpenReleasePage = ThemedDialogWindow.ShowYesNo(
                    this,
                    viewModel.SelectedTheme,
                    LocalizationService.Translate("Update Available"),
                    fallbackMessage,
                    LocalizationService.Translate("Open Update Page"),
                    LocalizationService.Translate("Not Now"));
                if (shouldOpenReleasePage)
                {
                    OpenExternalUri(availableUpdate.ReleasePageUrl);
                }

                return;
            }

            var updateMessage = LocalizationService.Format(
                "A newer Crystal Relay update is available.\n\nCurrent version: {0}\nLatest version: {1}\n\nIf you continue, Crystal Relay will download the update, close, replace the app files, and reopen.",
                availableUpdate.CurrentVersion,
                availableUpdate.LatestVersion);

            if (availableUpdate.IsBeta)
            {
                var betaChoice = ThemedDialogWindow.ShowThreeChoice(
                    this,
                    viewModel.SelectedTheme,
                    LocalizationService.Translate("Update Available"),
                    updateMessage,
                    LocalizationService.Translate("Download & Restart"),
                    LocalizationService.Translate("Remind Me Later"),
                    LocalizationService.Translate("Hide Betas Until Stable"));

                if (betaChoice == ThemedDialogChoice.Primary)
                {
                    await StartApplicationSelfUpdateAsync(availableUpdate, cancellationToken);
                    return;
                }

                if (betaChoice == ThemedDialogChoice.Tertiary)
                {
                    viewModel.IgnoreBetaApplicationUpdatesUntilStable(availableUpdate.LatestBaseVersion);
                    return;
                }

                return;
            }

            var updateChoice = ThemedDialogWindow.ShowThreeChoice(
                this,
                viewModel.SelectedTheme,
                LocalizationService.Translate("Update Available"),
                updateMessage,
                LocalizationService.Translate("Download & Restart"),
                LocalizationService.Translate("Remind Me Later"),
                LocalizationService.Translate("Skip This Version"));

            if (updateChoice == ThemedDialogChoice.Primary)
            {
                await StartApplicationSelfUpdateAsync(availableUpdate, cancellationToken);
                return;
            }

            if (updateChoice == ThemedDialogChoice.Tertiary)
            {
                viewModel.IgnoreApplicationUpdate(availableUpdate.LatestVersion);
            }
        }
        catch (OperationCanceledException)
        {
        }
    }

    private async Task StartApplicationSelfUpdateAsync(
        ApplicationUpdateInfo availableUpdate,
        CancellationToken cancellationToken)
    {
        var updateLaunched = false;
        try
        {
            IsEnabled = false;
            Mouse.OverrideCursor = Cursors.Wait;
            using var updater = new ApplicationSelfUpdateService();
            var progress = new Progress<ApplicationSelfUpdateProgress>(updateProgress =>
            {
                if (!string.IsNullOrWhiteSpace(updateProgress.Message))
                {
                    viewModel.AppendDiagnosticLog(updateProgress.Message);
                }
            });

            await updater.PrepareAndLaunchUpdateAsync(availableUpdate, progress, cancellationToken);
            updateLaunched = true;
            Close();
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch (Exception ex)
        {
            viewModel.AppendDiagnosticLog(
                LocalizationService.Format(
                    "Crystal Relay could not install the update automatically: {0}",
                    ex.Message));
            var shouldOpenReleasePage = ThemedDialogWindow.ShowYesNo(
                this,
                viewModel.SelectedTheme,
                LocalizationService.Translate("Update Failed"),
                LocalizationService.Format(
                    "Crystal Relay could not install the update automatically.\n\n{0}\n\nOpen the GitHub release page instead?",
                    ex.Message),
                LocalizationService.Translate("Open Update Page"),
                LocalizationService.Translate("Close"));
            if (shouldOpenReleasePage)
            {
                OpenExternalUri(availableUpdate.ReleasePageUrl);
            }
        }
        finally
        {
            if (!updateLaunched)
            {
                Mouse.OverrideCursor = null;
                IsEnabled = true;
            }
        }
    }

    private void OnHyperlinkRequestNavigate(object sender, RequestNavigateEventArgs e)
    {
        try
        {
            OpenExternalUri(e.Uri.AbsoluteUri);
            e.Handled = true;
        }
        catch
        {
        }
    }

    private static void OpenExternalUri(string uri)
    {
        Process.Start(new ProcessStartInfo
        {
            FileName = uri,
            UseShellExecute = true
        });
    }

    private void OnStateChanged(object? sender, EventArgs e)
    {
        if (WindowState != WindowState.Minimized)
        {
            lastNonMinimizedWindowState = WindowState;
        }

        UpdateWindowStateGlyph();

        TrackPeekabooWindowToggle();
    }

    private void OnLocationChanged(object? sender, EventArgs e)
    {
        if (!isTrackingTitleBarShake || isTeapotDialogOpen || isGracefulShutdownInProgress)
        {
            return;
        }

        var now = DateTimeOffset.UtcNow;
        titleBarDragSamples.Enqueue(new WindowDragSample(now, new System.Windows.Point(Left, Top)));

        while (titleBarDragSamples.Count > 0 && now - titleBarDragSamples.Peek().Timestamp > TeapotShakeSampleWindow)
        {
            titleBarDragSamples.Dequeue();
        }

        EvaluateTeapotShakePattern();
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

        BeginTitleBarShakeTracking();
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
        finally
        {
            EndTitleBarShakeTracking();
        }
    }

    private void OnMinimizeButtonClick(object sender, RoutedEventArgs e)
    {
        if (isGracefulShutdownInProgress || isGracefulShutdownCompleted)
        {
            return;
        }

        ShowInTaskbar = true;
        WindowState = WindowState.Minimized;
    }

    private void OnHideToTrayButtonClick(object sender, RoutedEventArgs e)
    {
        HideMainWindowToTray(setMinimizedState: true);
    }

    private void OnToggleMaximizeButtonClick(object sender, RoutedEventArgs e)
    {
        ToggleWindowState();
    }

    private void OnPowerButtonClick(object sender, RoutedEventArgs e)
    {
        if (!ConfirmQuitApplication())
        {
            return;
        }

        Close();
    }

    private void OnRestartCrystalRelayClicked(object sender, RoutedEventArgs e)
    {
        if (isGracefulShutdownInProgress || isGracefulShutdownCompleted)
        {
            return;
        }

        if (!ThemedDialogWindow.ShowYesNo(
                this,
                viewModel.SelectedTheme,
                LocalizationService.Translate("Restart Crystal Relay?"),
                LocalizationService.Translate("Crystal Relay will close and reopen. Open Crystal Relay windows will be restored.")))
        {
            return;
        }

        StartRestart(ApplicationRestartMode.CrystalRelayOnly);
    }

    private async void OnRestartVrChatAndCrystalRelayClicked(object sender, RoutedEventArgs e)
    {
        if (isGracefulShutdownInProgress || isGracefulShutdownCompleted)
        {
            return;
        }

        if (!ThemedDialogWindow.ShowYesNo(
                this,
                viewModel.SelectedTheme,
                LocalizationService.Translate("Restart VRChat and Crystal Relay?"),
                LocalizationService.Translate("Crystal Relay will read your current VRChat instance, close VRChat, force-close it if needed, reopen that instance, and restart Crystal Relay.")))
        {
            return;
        }

        IsEnabled = false;
        try
        {
            var location = await viewModel.PrepareVrChatRestartRejoinAsync();
            if (!location.IsAvailable)
            {
                ThemedDialogWindow.ShowOk(
                    this,
                    viewModel.SelectedTheme,
                    LocalizationService.Translate("VRChat restart unavailable"),
                    string.IsNullOrWhiteSpace(location.FailureReason)
                        ? LocalizationService.Translate("Crystal Relay could not find a current VRChat instance to rejoin. VRChat was not restarted.")
                        : location.FailureReason);
                return;
            }

            StartRestart(
                ApplicationRestartMode.VrChatAndCrystalRelay,
                location.LaunchUri,
                viewModel.Settings.RestartVrChatInDesktopMode);
        }
        finally
        {
            if (!isGracefulShutdownInProgress && !isGracefulShutdownCompleted)
            {
                IsEnabled = true;
            }
        }
    }

    private void StartRestart(
        ApplicationRestartMode restartMode,
        string? vrChatLaunchUri = null,
        bool restartVrChatInDesktopMode = false)
    {
        var state = CaptureRestartSessionState(restartMode, vrChatLaunchUri, restartVrChatInDesktopMode);
        ApplicationRestartService.StartRestartHelper(state);
        Close();
    }

    private void OnHomeBrandIconClicked(object sender, RoutedEventArgs e)
    {
        if (!AreEasterEggsEnabled)
        {
            homeIconClickTimes.Clear();
            return;
        }

        var now = DateTimeOffset.UtcNow;
        homeIconClickTimes.Enqueue(now);
        while (homeIconClickTimes.Count > 0 && now - homeIconClickTimes.Peek() > HomeIconClickWindow)
        {
            homeIconClickTimes.Dequeue();
        }

        if (homeIconClickTimes.Count < 5)
        {
            return;
        }

        homeIconClickTimes.Clear();
        PlayHomeIconEasterEggAudio();
    }

    private void TrackPeekabooWindowToggle()
    {
        var isMinimized = WindowState == WindowState.Minimized;
        if (isMinimized == wasWindowMinimized)
        {
            return;
        }

        wasWindowMinimized = isMinimized;
        if (!AreEasterEggsEnabled || isGracefulShutdownInProgress || isAdvertisementPlaying)
        {
            peekabooToggleTimes.Clear();
            return;
        }

        var now = DateTimeOffset.UtcNow;
        if (now - peekabooLastTriggeredAt < PeekabooEasterEggCooldown)
        {
            peekabooToggleTimes.Clear();
            return;
        }

        peekabooToggleTimes.Enqueue(now);
        while (peekabooToggleTimes.Count > 0 && now - peekabooToggleTimes.Peek() > PeekabooToggleWindow)
        {
            peekabooToggleTimes.Dequeue();
        }

        if (peekabooToggleTimes.Count < PeekabooRequiredToggleCount)
        {
            return;
        }

        peekabooToggleTimes.Clear();
        peekabooLastTriggeredAt = now;
        PlayPeekabooEasterEggAudio();
    }

    private void BeginTitleBarShakeTracking()
    {
        if (!AreEasterEggsEnabled || isAdvertisementPlaying || isTeapotDialogOpen)
        {
            titleBarDragSamples.Clear();
            isTrackingTitleBarShake = false;
            shouldShowTeapotDialogAfterDrag = false;
            return;
        }

        titleBarDragSamples.Clear();
        titleBarDragSamples.Enqueue(new WindowDragSample(DateTimeOffset.UtcNow, new System.Windows.Point(Left, Top)));
        isTrackingTitleBarShake = true;
        shouldShowTeapotDialogAfterDrag = false;
    }

    private void EndTitleBarShakeTracking()
    {
        isTrackingTitleBarShake = false;
        titleBarDragSamples.Clear();

        if (!shouldShowTeapotDialogAfterDrag)
        {
            return;
        }

        shouldShowTeapotDialogAfterDrag = false;
        ShowTeapotEasterEggDialog();
    }

    private void EvaluateTeapotShakePattern()
    {
        if (titleBarDragSamples.Count < 4)
        {
            return;
        }

        var samples = titleBarDragSamples.ToArray();
        var totalTravelDistance = 0d;
        var directionChanges = 0;
        var previousDirection = WindowShakeDirection.None;

        for (var index = 1; index < samples.Length; index++)
        {
            var deltaX = samples[index].Position.X - samples[index - 1].Position.X;
            var deltaY = samples[index].Position.Y - samples[index - 1].Position.Y;
            var stepDistance = Math.Abs(deltaX) + Math.Abs(deltaY);
            if (stepDistance < 6d)
            {
                continue;
            }

            totalTravelDistance += stepDistance;
            var currentDirection = ResolveShakeDirection(deltaX, deltaY);
            if (currentDirection == WindowShakeDirection.None)
            {
                continue;
            }

            if (previousDirection != WindowShakeDirection.None && currentDirection != previousDirection)
            {
                directionChanges++;
            }

            previousDirection = currentDirection;
        }

        if (totalTravelDistance < TeapotShakeRequiredTravelDistance
            || directionChanges < TeapotShakeRequiredDirectionChanges)
        {
            return;
        }

        var now = DateTimeOffset.UtcNow;
        if (now - teapotLastShownAt < TeapotShakeCooldown)
        {
            return;
        }

        shouldShowTeapotDialogAfterDrag = true;
    }

    private static WindowShakeDirection ResolveShakeDirection(double deltaX, double deltaY)
    {
        if (Math.Abs(deltaX) >= Math.Abs(deltaY))
        {
            if (deltaX > 0)
            {
                return WindowShakeDirection.Right;
            }

            if (deltaX < 0)
            {
                return WindowShakeDirection.Left;
            }
        }
        else
        {
            if (deltaY > 0)
            {
                return WindowShakeDirection.Down;
            }

            if (deltaY < 0)
            {
                return WindowShakeDirection.Up;
            }
        }

        return WindowShakeDirection.None;
    }

    private void ShowTeapotEasterEggDialog()
    {
        if (!AreEasterEggsEnabled || isTeapotDialogOpen || isGracefulShutdownInProgress)
        {
            return;
        }

        isTeapotDialogOpen = true;
        teapotLastShownAt = DateTimeOffset.UtcNow;

        try
        {
            PlayTeapotEasterEggAudio();
            ThemedDialogWindow.ShowOk(
                this,
                viewModel.SelectedTheme,
                "Error: 418 - I am a teapot",
                "You shook Crystal Relay a little too hard.",
                finePrint: "Fine print: this is just an easter egg, not a real error.");
        }
        finally
        {
            isTeapotDialogOpen = false;
        }
    }

    private void PlayTeapotEasterEggAudio()
    {
        var audioPath = EmbeddedMediaCacheService.ExtractEmbeddedMediaToTempFile(TeapotEasterEggAudioRelativePath);
        if (string.IsNullOrWhiteSpace(audioPath) || !File.Exists(audioPath))
        {
            return;
        }

        try
        {
            CleanupTeapotEasterEggAudio();

            var player = new MediaPlayer();
            teapotEasterEggPlayer = player;
            teapotEasterEggAudioTempPath = audioPath;
            player.MediaEnded += (_, _) => CleanupTeapotEasterEggAudio();
            player.MediaFailed += (_, _) => CleanupTeapotEasterEggAudio();
            player.Open(new Uri(audioPath, UriKind.Absolute));
            player.Volume = 1.0;
            player.Play();
        }
        catch
        {
            EmbeddedMediaCacheService.DeleteTemporaryMediaFile(audioPath);
            teapotEasterEggAudioTempPath = null;
            teapotEasterEggPlayer = null;
        }
    }

    private void OnPreviewKeyDown(object sender, KeyEventArgs e)
    {
        if (isAdvertisementPlaying)
        {
            e.Handled = true;
            return;
        }

        if (TryOpenDebugLogWindowFromChord(e))
        {
            return;
        }

        if (!AreEasterEggsEnabled)
        {
            konamiSequenceIndex = 0;
            return;
        }

        if (IsTextInputFocused(e.OriginalSource as DependencyObject))
        {
            konamiSequenceIndex = 0;
            return;
        }

        var pressedKey = e.Key == Key.System ? e.SystemKey : e.Key;
        if (pressedKey == Key.None)
        {
            return;
        }

        if (pressedKey == KonamiSequence[konamiSequenceIndex])
        {
            konamiSequenceIndex++;
            if (konamiSequenceIndex >= KonamiSequence.Length)
            {
                konamiSequenceIndex = 0;
                ShowAdvertisementOverlay();
            }

            return;
        }

        konamiSequenceIndex = pressedKey == KonamiSequence[0] ? 1 : 0;
    }

    private bool TryOpenDebugLogWindowFromChord(KeyEventArgs e)
    {
        if ((Keyboard.Modifiers & ModifierKeys.Control) != 0
            || IsTextInputFocused(e.OriginalSource as DependencyObject)
            || !Keyboard.IsKeyDown(Key.C)
            || !Keyboard.IsKeyDown(Key.T)
            || !Keyboard.IsKeyDown(Key.R)
            || !Keyboard.IsKeyDown(Key.L)
            || !Keyboard.IsKeyDown(Key.D))
        {
            return false;
        }

        e.Handled = true;
        if (debugLogWindow is { IsVisible: true } existingWindow)
        {
            existingWindow.Activate();
            return true;
        }

        OpenDebugLogWindow();
        return true;
    }

    private void OpenDebugLogWindow(WindowPlacementSnapshot? placement = null)
    {
        if (debugLogWindow is { IsVisible: true } existingWindow)
        {
            existingWindow.Activate();
            return;
        }

        debugLogWindow = new DebugLogWindow(viewModel.SelectedTheme)
        {
            Owner = this
        };
        debugLogWindow.Closed += (_, _) => debugLogWindow = null;
        WindowPlacementStateStore.ApplyWindowPlacement(debugLogWindow, placement);
        debugLogWindow.Show();
        debugLogWindow.Activate();
    }

    private void RestoreRestartSessionWindows()
    {
        if (restartRestoreState is null)
        {
            return;
        }

        viewModel.RestoreRestartSessionWindows(restartRestoreState);
        var debugPlacement = restartRestoreState.Windows.FirstOrDefault(window =>
            string.Equals(window.WindowKey, WindowPlacementStateStore.DebugLogKey, StringComparison.Ordinal));
        if (debugPlacement is { WasOpen: true })
        {
            OpenDebugLogWindow(debugPlacement);
        }
    }

    private ApplicationRestartSessionState CaptureRestartSessionState(
        ApplicationRestartMode restartMode,
        string? vrChatLaunchUri = null,
        bool restartVrChatInDesktopMode = false)
    {
        return new ApplicationRestartSessionState
        {
            Mode = restartMode,
            VrChatLaunchUri = vrChatLaunchUri,
            RestartVrChatInDesktopMode = restartVrChatInDesktopMode,
            Windows =
            [
                WindowPlacementStateStore.CaptureWindow(this, WindowPlacementStateStore.MainWindowKey),
                viewModel.CaptureTwitchChatboxPlacement(),
                viewModel.CaptureTestModePlacement(),
                WindowPlacementStateStore.CaptureWindow(
                    debugLogWindow,
                    WindowPlacementStateStore.DebugLogKey,
                    debugLogWindow is { IsVisible: true })
            ]
        };
    }

    private void PlayHomeIconEasterEggAudio()
    {
        var audioPath = EmbeddedMediaCacheService.ExtractEmbeddedMediaToTempFile(HomeIconEasterEggAudioRelativePath);
        if (string.IsNullOrWhiteSpace(audioPath) || !File.Exists(audioPath))
        {
            return;
        }

        try
        {
            CleanupHomeIconEasterEggAudio();

            var player = new MediaPlayer();
            homeIconEasterEggPlayer = player;
            homeIconEasterEggAudioTempPath = audioPath;
            player.MediaEnded += (_, _) => CleanupHomeIconEasterEggAudio();
            player.MediaFailed += (_, _) => CleanupHomeIconEasterEggAudio();
            player.Open(new Uri(audioPath, UriKind.Absolute));
            player.Volume = 1.0;
            player.Play();
        }
        catch
        {
            EmbeddedMediaCacheService.DeleteTemporaryMediaFile(audioPath);
            homeIconEasterEggAudioTempPath = null;
            homeIconEasterEggPlayer = null;
        }
    }

    private void PlayPeekabooEasterEggAudio()
    {
        var audioPath = EmbeddedMediaCacheService.ExtractEmbeddedMediaToTempFile(PeekabooEasterEggAudioRelativePath);
        if (string.IsNullOrWhiteSpace(audioPath) || !File.Exists(audioPath))
        {
            return;
        }

        try
        {
            CleanupPeekabooEasterEggAudio();

            var player = new MediaPlayer();
            peekabooEasterEggPlayer = player;
            peekabooEasterEggAudioTempPath = audioPath;
            player.MediaEnded += (_, _) => CleanupPeekabooEasterEggAudio();
            player.MediaFailed += (_, _) => CleanupPeekabooEasterEggAudio();
            player.Open(new Uri(audioPath, UriKind.Absolute));
            player.Volume = 1.0;
            player.Play();
        }
        catch
        {
            EmbeddedMediaCacheService.DeleteTemporaryMediaFile(audioPath);
            peekabooEasterEggAudioTempPath = null;
            peekabooEasterEggPlayer = null;
        }
    }

    private readonly record struct WindowDragSample(DateTimeOffset Timestamp, System.Windows.Point Position);

    private enum WindowShakeDirection
    {
        None,
        Left,
        Right,
        Up,
        Down
    }

    private void CleanupHomeIconEasterEggAudio()
    {
        if (homeIconEasterEggPlayer is not null)
        {
            homeIconEasterEggPlayer.Stop();
            homeIconEasterEggPlayer.Close();
            homeIconEasterEggPlayer = null;
        }

        EmbeddedMediaCacheService.DeleteTemporaryMediaFile(homeIconEasterEggAudioTempPath);
        homeIconEasterEggAudioTempPath = null;
    }

    private void CleanupPeekabooEasterEggAudio()
    {
        if (peekabooEasterEggPlayer is not null)
        {
            peekabooEasterEggPlayer.Stop();
            peekabooEasterEggPlayer.Close();
            peekabooEasterEggPlayer = null;
        }

        EmbeddedMediaCacheService.DeleteTemporaryMediaFile(peekabooEasterEggAudioTempPath);
        peekabooEasterEggAudioTempPath = null;
    }

    private void CleanupTeapotEasterEggAudio()
    {
        if (teapotEasterEggPlayer is not null)
        {
            teapotEasterEggPlayer.Stop();
            teapotEasterEggPlayer.Close();
            teapotEasterEggPlayer = null;
        }

        EmbeddedMediaCacheService.DeleteTemporaryMediaFile(teapotEasterEggAudioTempPath);
        teapotEasterEggAudioTempPath = null;
    }

    private void ShowAdvertisementOverlay()
    {
        if (!AreEasterEggsEnabled || isAdvertisementPlaying)
        {
            return;
        }

        var videoPath = EmbeddedMediaCacheService.ExtractEmbeddedMediaToTempFile(AdvertisementVideoRelativePath);
        if (string.IsNullOrWhiteSpace(videoPath) || !File.Exists(videoPath))
        {
            return;
        }

        try
        {
            isAdvertisementPlaying = true;
            advertisementVideoTempPath = videoPath;
            AdvertisementOverlay.Visibility = Visibility.Visible;
            AdvertisementMediaElement.Source = new Uri(videoPath, UriKind.Absolute);
            AdvertisementMediaElement.Position = TimeSpan.Zero;
            AdvertisementMediaElement.Volume = 1.0;
            AdvertisementMediaElement.Play();
        }
        catch
        {
            EmbeddedMediaCacheService.DeleteTemporaryMediaFile(videoPath);
            advertisementVideoTempPath = null;
            isAdvertisementPlaying = false;
            AdvertisementOverlay.Visibility = Visibility.Collapsed;
        }
    }

    private void CloseAdvertisementOverlay()
    {
        AdvertisementMediaElement.Stop();
        AdvertisementMediaElement.Source = null;
        AdvertisementOverlay.Visibility = Visibility.Collapsed;
        isAdvertisementPlaying = false;
        EmbeddedMediaCacheService.DeleteTemporaryMediaFile(advertisementVideoTempPath);
        advertisementVideoTempPath = null;
    }

    private void OnAdvertisementMediaEnded(object sender, RoutedEventArgs e)
    {
        CloseAdvertisementOverlay();
    }

    private void OnAdvertisementMediaFailed(object sender, ExceptionRoutedEventArgs e)
    {
        CloseAdvertisementOverlay();
    }

    private static bool IsTextInputFocused(DependencyObject? originalSource)
    {
        return IsTextInputElement(originalSource)
            || IsTextInputElement(Keyboard.FocusedElement as DependencyObject);
    }

    private static bool IsTextInputElement(DependencyObject? source)
    {
        if (source is TextBoxBase or PasswordBox
            || FindAncestor<TextBoxBase>(source) is not null
            || FindAncestor<PasswordBox>(source) is not null)
        {
            return true;
        }

        if (source is ComboBox { IsEditable: true }
            || FindAncestor<ComboBox>(source) is ComboBox { IsEditable: true })
        {
            return true;
        }

        return false;
    }

    private void OnRuleTogglePreviewMouseWheel(object sender, MouseWheelEventArgs e)
    {
        if (sender is ListBox listBox)
        {
            var internalScroll = FindDescendant<ScrollViewer>(listBox);
            if (internalScroll is not null && internalScroll.ScrollableHeight > 0)
            {
                e.Handled = true;
                ScrollViewerByMouseWheel(internalScroll, e.Delta);
                return;
            }
        }

        var senderDo = sender as DependencyObject;
        var originalDo = e.OriginalSource as DependencyObject;

        var scrollViewer = FindScrollableScrollViewer(senderDo)
                           ?? FindScrollableScrollViewer(originalDo)
                           ?? FindAncestor<ScrollViewer>(senderDo)
                           ?? FindAncestor<ScrollViewer>(originalDo);

        if (scrollViewer is null)
        {
            return;
        }

        e.Handled = true;
        ScrollViewerByMouseWheel(scrollViewer, e.Delta);
    }

    private static void ScrollViewerByMouseWheel(ScrollViewer scrollViewer, int delta)
    {
        if (delta == 0)
        {
            return;
        }

        var notchCount = Math.Max(1, Math.Abs(delta) / Mouse.MouseWheelDeltaForOneLine);
        var wheelScrollLines = SystemParameters.WheelScrollLines;

        if (wheelScrollLines < 0)
        {
            for (var i = 0; i < notchCount; i++)
            {
                if (delta < 0)
                {
                    scrollViewer.PageDown();
                }
                else
                {
                    scrollViewer.PageUp();
                }
            }

            return;
        }

        var lineCount = Math.Max(1, wheelScrollLines) * notchCount;
        for (var i = 0; i < lineCount; i++)
        {
            if (delta < 0)
            {
                scrollViewer.LineDown();
            }
            else
            {
                scrollViewer.LineUp();
            }
        }
    }

    private static ScrollViewer? FindScrollableScrollViewer(DependencyObject? current)
    {
        while (current is not null)
        {
            if (current is ScrollViewer scrollViewer)
            {
                if (scrollViewer.ScrollableHeight > 0
                    || scrollViewer.ComputedVerticalScrollBarVisibility == Visibility.Visible)
                {
                    return scrollViewer;
                }
            }

            current = VisualTreeHelper.GetParent(current);
        }

        return null;
    }

    private static T? FindDescendant<T>(DependencyObject? current) where T : DependencyObject
    {
        if (current is null)
        {
            return null;
        }

        var childCount = VisualTreeHelper.GetChildrenCount(current);
        for (var i = 0; i < childCount; i++)
        {
            var child = VisualTreeHelper.GetChild(current, i);
            if (child is T match)
            {
                return match;
            }

            var descendant = FindDescendant<T>(child);
            if (descendant is not null)
            {
                return descendant;
            }
        }

        return null;
    }

    private void ToggleWindowState()
    {
        WindowState = WindowState == WindowState.Maximized
            ? WindowState.Normal
            : WindowState.Maximized;
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

    private void UpdateWindowStateGlyph()
    {
        if (WindowStateGlyph is null)
        {
            return;
        }

        WindowStateGlyph.Text = WindowState == WindowState.Maximized ? "❐" : "☐";
    }

    private void InitializeTrayIcon()
    {
        if (trayNotifyIcon is not null)
        {
            return;
        }

        trayIconImage = CreateTrayIconImage();

        var contextMenu = new WinForms.ContextMenuStrip();
        var openMenuItem = new WinForms.ToolStripMenuItem(LocalizationService.Translate("Open Crystal Relay"));
        openMenuItem.Click += OnTrayOpenMenuItemClicked;

        var quitMenuItem = new WinForms.ToolStripMenuItem(LocalizationService.Translate("Quit Crystal Relay"));
        quitMenuItem.Click += OnTrayQuitMenuItemClicked;

        contextMenu.Items.Add(openMenuItem);
        contextMenu.Items.Add(new WinForms.ToolStripSeparator());
        contextMenu.Items.Add(quitMenuItem);

        trayNotifyIcon = new WinForms.NotifyIcon
        {
            Text = "Crystal Relay",
            Icon = trayIconImage,
            Visible = false,
            ContextMenuStrip = contextMenu
        };
        trayNotifyIcon.MouseDoubleClick += OnTrayIconMouseDoubleClick;
    }

    private void CleanupTrayIcon()
    {
        if (trayNotifyIcon is null)
        {
            return;
        }

        trayNotifyIcon.Visible = false;
        trayNotifyIcon.MouseDoubleClick -= OnTrayIconMouseDoubleClick;

        if (trayNotifyIcon.ContextMenuStrip is { } contextMenu)
        {
            foreach (var item in contextMenu.Items.OfType<WinForms.ToolStripMenuItem>())
            {
                item.Click -= OnTrayOpenMenuItemClicked;
                item.Click -= OnTrayQuitMenuItemClicked;
            }

            contextMenu.Dispose();
        }

        trayNotifyIcon.Dispose();
        trayNotifyIcon = null;

        trayIconImage?.Dispose();
        trayIconImage = null;
    }

    private static Drawing.Icon CreateTrayIconImage()
    {
        try
        {
            var processPath = Environment.ProcessPath;
            if (!string.IsNullOrWhiteSpace(processPath) && File.Exists(processPath))
            {
                using var extractedIcon = Drawing.Icon.ExtractAssociatedIcon(processPath);
                if (extractedIcon is not null)
                {
                    return (Drawing.Icon)extractedIcon.Clone();
                }
            }
        }
        catch
        {
        }

        return (Drawing.Icon)Drawing.SystemIcons.Application.Clone();
    }

    private void HideMainWindowToTray(bool setMinimizedState)
    {
        if (isTrayHideInProgress || isMainWindowHiddenToTray || isGracefulShutdownInProgress || isGracefulShutdownCompleted)
        {
            return;
        }

        InitializeTrayIcon();

        try
        {
            isTrayHideInProgress = true;

            if (setMinimizedState && WindowState != WindowState.Minimized)
            {
                WindowState = WindowState.Minimized;
            }

            if (trayNotifyIcon is not null)
            {
                trayNotifyIcon.Visible = true;
            }

            isMainWindowHiddenToTray = true;
            ShowInTaskbar = false;
            Hide();
            ShowTrayTipIfNeeded();
        }
        finally
        {
            isTrayHideInProgress = false;
        }
    }

    private void RestoreMainWindowFromTray()
    {
        if (isGracefulShutdownInProgress || isGracefulShutdownCompleted)
        {
            return;
        }

        isMainWindowHiddenToTray = false;
        ShowInTaskbar = true;

        if (trayNotifyIcon is not null)
        {
            trayNotifyIcon.Visible = false;
        }

        var restoreState = lastNonMinimizedWindowState == WindowState.Minimized
            ? WindowState.Normal
            : lastNonMinimizedWindowState;

        Show();
        WindowState = restoreState;
        ActivateMainWindow();
    }

    private void ActivateMainWindow()
    {
        Activate();
        Topmost = true;
        Topmost = false;
        Focus();
    }

    private void ShowTrayTipIfNeeded()
    {
        if (trayNotifyIcon is null || viewModel.Settings.MainWindowTrayTipShown)
        {
            return;
        }

        viewModel.Settings.MainWindowTrayTipShown = true;
        trayNotifyIcon.ShowBalloonTip(
            5000,
            "Crystal Relay",
            LocalizationService.Translate("Crystal Relay is still running in the system tray. Double-click the tray icon to open it again."),
            WinForms.ToolTipIcon.Info);
    }

    private bool ConfirmQuitApplication()
    {
        if (isGracefulShutdownInProgress || isGracefulShutdownCompleted)
        {
            return false;
        }

        var owner = IsVisible ? this : null;
        return ThemedDialogWindow.ShowYesNo(
            owner,
            viewModel.SelectedTheme,
            LocalizationService.Translate("Quit Crystal Relay?"),
            LocalizationService.Translate("Do you really want to quit this program for good?"));
    }

    private void OnTrayIconMouseDoubleClick(object? sender, WinForms.MouseEventArgs e)
    {
        if (e.Button != WinForms.MouseButtons.Left)
        {
            return;
        }

        Dispatcher.BeginInvoke(RestoreMainWindowFromTray);
    }

    private void OnTrayOpenMenuItemClicked(object? sender, EventArgs e)
    {
        Dispatcher.BeginInvoke(RestoreMainWindowFromTray);
    }

    private void OnTrayQuitMenuItemClicked(object? sender, EventArgs e)
    {
        Dispatcher.BeginInvoke(() =>
        {
            if (!ConfirmQuitApplication())
            {
                return;
            }

            Close();
        });
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

    private IntPtr WindowMessageHook(IntPtr hwnd, int msg, IntPtr wParam, IntPtr lParam, ref bool handled)
    {
        if (msg == App.ActivateExistingWindowMessageId)
        {
            BringWindowToFront();
            handled = true;
            return IntPtr.Zero;
        }

        if (msg == WmDpiChanged)
        {
            InvalidateVisual();
            return IntPtr.Zero;
        }

        if (msg == WmGetMinMaxInfo)
        {
            ConstrainToWorkArea(hwnd, lParam);
            handled = true;
        }

        return IntPtr.Zero;
    }

    private static void ConstrainToWorkArea(IntPtr hwnd, IntPtr lParam)
    {
        var minMaxInfo = Marshal.PtrToStructure<MinMaxInfo>(lParam);
        var monitor = MonitorFromWindow(hwnd, MonitorDefaultToNearest);
        if (monitor == IntPtr.Zero)
        {
            return;
        }

        var monitorInfo = new MonitorInfo();
        monitorInfo.Size = Marshal.SizeOf<MonitorInfo>();

        if (!GetMonitorInfo(monitor, ref monitorInfo))
        {
            return;
        }

        var workArea = monitorInfo.WorkArea;
        var monitorArea = monitorInfo.MonitorArea;

        var workWidth = Math.Abs(workArea.Right - workArea.Left);
        var workHeight = Math.Abs(workArea.Bottom - workArea.Top);

        // Always constrain maximized size to working area
        minMaxInfo.MaxPosition.X = Math.Abs(workArea.Left - monitorArea.Left);
        minMaxInfo.MaxPosition.Y = Math.Abs(workArea.Top - monitorArea.Top);
        minMaxInfo.MaxSize.X = workWidth;
        minMaxInfo.MaxSize.Y = workHeight;

        // Always constrain max tracking size (resize boundary) to working area
        // Windows already initializes MaxTrackSize to the virtual screen size;
        // we just clamp it down to the current monitor's working area.
        if (minMaxInfo.MaxTrackSize.X > workWidth)
            minMaxInfo.MaxTrackSize.X = workWidth;
        if (minMaxInfo.MaxTrackSize.Y > workHeight)
            minMaxInfo.MaxTrackSize.Y = workHeight;

        // If minimum size exceeds working area, clamp minimum down to fit
        if (minMaxInfo.MinTrackSize.X > minMaxInfo.MaxTrackSize.X)
            minMaxInfo.MinTrackSize.X = minMaxInfo.MaxTrackSize.X;
        if (minMaxInfo.MinTrackSize.Y > minMaxInfo.MaxTrackSize.Y)
            minMaxInfo.MinTrackSize.Y = minMaxInfo.MaxTrackSize.Y;

        Marshal.StructureToPtr(minMaxInfo, lParam, true);
    }

    private void BringWindowToFront()
    {
        Dispatcher.BeginInvoke(() =>
        {
            if (isMainWindowHiddenToTray)
            {
                RestoreMainWindowFromTray();
                return;
            }

            if (WindowState == WindowState.Minimized)
            {
                WindowState = lastNonMinimizedWindowState == WindowState.Minimized
                    ? WindowState.Normal
                    : lastNonMinimizedWindowState;
            }

            ShowInTaskbar = true;
            Show();
            ActivateMainWindow();
        });
    }

    private void StartLoadingAnimations()
    {
        foreach (var storyboardKey in LoadingStoryboardKeys)
        {
            if (TryGetLoadingStoryboard(storyboardKey, out var storyboard))
            {
                storyboard.Begin(this, true);
            }
        }
    }

    private void StopLoadingAnimations()
    {
        foreach (var storyboardKey in LoadingStoryboardKeys)
        {
            if (TryGetLoadingStoryboard(storyboardKey, out var storyboard))
            {
                storyboard.Stop(this);
            }
        }
    }

    private void CreateStarField()
    {
        var random = new Random();
        var canvas = StarFieldCanvas;

        // Nebula clouds: large blurred ellipses behind the stars
        var nebulaColors = new[]
        {
            System.Windows.Media.Color.FromArgb(18, 74, 158, 255),
            System.Windows.Media.Color.FromArgb(12, 124, 92, 255),
            System.Windows.Media.Color.FromArgb(10, 255, 92, 135),
            System.Windows.Media.Color.FromArgb(8, 60, 180, 220)
        };

        for (var n = 0; n < 4; n++)
        {
            var nebula = new System.Windows.Shapes.Ellipse
            {
                Width = random.Next(200, 400),
                Height = random.Next(150, 300),
                Fill = new System.Windows.Media.SolidColorBrush(nebulaColors[n]),
                Opacity = 0.8
            };
            nebula.Effect = new System.Windows.Media.Effects.BlurEffect { Radius = 60 };
            Canvas.SetLeft(nebula, random.Next(-100, 700));
            Canvas.SetTop(nebula, random.Next(-100, 500));
            canvas.Children.Add(nebula);

            // Slow drift for nebula
            var driftX = new System.Windows.Media.Animation.DoubleAnimation
            {
                From = Canvas.GetLeft(nebula),
                To = Canvas.GetLeft(nebula) + random.Next(-30, 30),
                Duration = new Duration(TimeSpan.FromSeconds(random.Next(15, 25))),
                AutoReverse = true,
                RepeatBehavior = System.Windows.Media.Animation.RepeatBehavior.Forever
            };
            var driftY = new System.Windows.Media.Animation.DoubleAnimation
            {
                From = Canvas.GetTop(nebula),
                To = Canvas.GetTop(nebula) + random.Next(-20, 20),
                Duration = new Duration(TimeSpan.FromSeconds(random.Next(20, 30))),
                AutoReverse = true,
                RepeatBehavior = System.Windows.Media.Animation.RepeatBehavior.Forever
            };

            System.Windows.Media.Animation.Storyboard.SetTarget(driftX, nebula);
            System.Windows.Media.Animation.Storyboard.SetTargetProperty(driftX, new PropertyPath("(Canvas.Left)"));
            System.Windows.Media.Animation.Storyboard.SetTarget(driftY, nebula);
            System.Windows.Media.Animation.Storyboard.SetTargetProperty(driftY, new PropertyPath("(Canvas.Top)"));

            var nebulaStoryboard = new System.Windows.Media.Animation.Storyboard();
            nebulaStoryboard.Children.Add(driftX);
            nebulaStoryboard.Children.Add(driftY);
            nebulaStoryboard.Begin(this, true);
        }

        // Star colors for variety
        var starBrushes = new[]
        {
            System.Windows.Media.Brushes.White,
            System.Windows.Media.Brushes.AliceBlue,
            System.Windows.Media.Brushes.LightSteelBlue,
            System.Windows.Media.Brushes.LightYellow,
            System.Windows.Media.Brushes.MistyRose,
            System.Windows.Media.Brushes.PaleTurquoise
        };

        for (var i = 0; i < 100; i++)
        {
            var isBeacon = i % 20 == 0;
            var star = new System.Windows.Shapes.Ellipse
            {
                Width = isBeacon ? random.Next(2, 4) : random.Next(1, 3),
                Height = isBeacon ? random.Next(2, 4) : random.Next(1, 3),
                Fill = starBrushes[random.Next(starBrushes.Length)],
                Opacity = isBeacon
                    ? random.NextDouble() * 0.3 + 0.5
                    : random.NextDouble() * 0.35 + 0.1
            };

            Canvas.SetLeft(star, random.Next(0, 900));
            Canvas.SetTop(star, random.Next(0, 700));
            canvas.Children.Add(star);

            // Twinkle animation
            var twinkleAnimation = new System.Windows.Media.Animation.DoubleAnimation
            {
                From = star.Opacity * 0.15,
                To = star.Opacity,
                Duration = new Duration(TimeSpan.FromSeconds(isBeacon ? random.Next(3, 7) : random.Next(2, 5))),
                AutoReverse = true,
                RepeatBehavior = System.Windows.Media.Animation.RepeatBehavior.Forever,
                BeginTime = TimeSpan.FromSeconds(random.NextDouble() * 5)
            };

            System.Windows.Media.Animation.Storyboard.SetTarget(twinkleAnimation, star);
            System.Windows.Media.Animation.Storyboard.SetTargetProperty(twinkleAnimation, new PropertyPath("Opacity"));
            var starStoryboard = new System.Windows.Media.Animation.Storyboard();
            starStoryboard.Children.Add(twinkleAnimation);

            // Slow drift for some stars
            if (i % 3 == 0)
            {
                var driftX = new System.Windows.Media.Animation.DoubleAnimation
                {
                    From = Canvas.GetLeft(star),
                    To = Canvas.GetLeft(star) + random.Next(-15, 15),
                    Duration = new Duration(TimeSpan.FromSeconds(random.Next(30, 50))),
                    AutoReverse = true,
                    RepeatBehavior = System.Windows.Media.Animation.RepeatBehavior.Forever
                };
                System.Windows.Media.Animation.Storyboard.SetTarget(driftX, star);
                System.Windows.Media.Animation.Storyboard.SetTargetProperty(driftX, new PropertyPath("(Canvas.Left)"));
                starStoryboard.Children.Add(driftX);
            }

            starStoryboard.Begin(this, true);
        }
    }

    private async Task RunRevealTransitionAsync()
    {
        // Step 1: Fade in "All systems operational" message
        if (TryGetLoadingStoryboard("RevealTransitionStoryboard", out _))
        {
            SignOffMessage.Opacity = 0.85;
        }
        await Task.Delay(2500);
        SignOffMessage.Opacity = 0;

        // Step 2: Fade out overlay with 2s timeout to prevent hang
        if (TryGetLoadingStoryboard("RevealTransitionStoryboard", out var revealStoryboard))
        {
            var tcs = new TaskCompletionSource();
            revealStoryboard.Completed += (_, _) => tcs.TrySetResult();
            revealStoryboard.Begin(this);
            await Task.WhenAny(tcs.Task, Task.Delay(2000));
        }
    }

    private bool TryGetLoadingStoryboard(
        string storyboardKey,
        out System.Windows.Media.Animation.Storyboard storyboard)
    {
        if (LoadingOverlay.Resources.Contains(storyboardKey)
            && LoadingOverlay.Resources[storyboardKey] is System.Windows.Media.Animation.Storyboard foundStoryboard)
        {
            storyboard = foundStoryboard;
            return true;
        }

        storyboard = null!;
        return false;
    }

    [DllImport("user32.dll")]
    private static extern IntPtr MonitorFromWindow(IntPtr hwnd, uint dwFlags);

    [DllImport("user32.dll", CharSet = CharSet.Auto)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool GetMonitorInfo(IntPtr hMonitor, ref MonitorInfo lpmi);

    [StructLayout(LayoutKind.Sequential)]
    private struct Point
    {
        public int X;
        public int Y;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct MinMaxInfo
    {
        public Point Reserved;
        public Point MaxSize;
        public Point MaxPosition;
        public Point MinTrackSize;
        public Point MaxTrackSize;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct Rect
    {
        public int Left;
        public int Top;
        public int Right;
        public int Bottom;
    }

    [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Auto)]
    private struct MonitorInfo
    {
        public int Size;
        public Rect MonitorArea;
        public Rect WorkArea;
        public int Flags;
    }

    private sealed class NativeWin32Window(IntPtr handle) : WinForms.IWin32Window
    {
        public IntPtr Handle { get; } = handle;
    }
}
