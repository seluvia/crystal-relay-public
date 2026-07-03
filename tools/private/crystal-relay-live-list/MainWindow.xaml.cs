using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Diagnostics;
using System.Globalization;
using System.IO;
using System.Media;
using System.Net.Http;
using System.Runtime.CompilerServices;
using System.Text.Json;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Data;
using System.Windows.Input;
using System.Windows.Interop;
using System.Windows.Media;
using System.Windows.Shell;
using System.Windows.Threading;
using CrystalRelayLiveList.Services;
using CrystalRelayLiveList.ViewModels;

namespace CrystalRelayLiveList;

public partial class MainWindow : Window, INotifyPropertyChanged
{
    private static readonly TimeSpan SlowPollInterval = TimeSpan.FromMinutes(5);
    private static readonly TimeSpan FastPollInterval = TimeSpan.FromSeconds(30);
    private static readonly TimeSpan RequestTimeout = TimeSpan.FromSeconds(12);
    private const int WmNcHitTest = 0x0084;
    private const int HtLeft = 10;
    private const int HtRight = 11;
    private const int HtTop = 12;
    private const int HtTopLeft = 13;
    private const int HtTopRight = 14;
    private const int HtBottom = 15;
    private const int HtBottomLeft = 16;
    private const int HtBottomRight = 17;
    private const double ResizeHitTestThickness = 16d;
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web)
    {
        PropertyNameCaseInsensitive = true
    };

    private readonly HttpClient httpClient = new() { Timeout = RequestTimeout };
    private readonly DispatcherTimer refreshTimer = new();
    private readonly DispatcherTimer retryTimer = new();
    private readonly List<MediaPlayer> activeAlertPlayers = [];
    private readonly LiveHistoryStore historyStore = new();
    private readonly LiveStatsTracker stats = new();
    private readonly RetryPolicy retryPolicy = new(TimeSpan.FromSeconds(5), TimeSpan.FromMinutes(2));
    private readonly ObservableCollection<string> presetNames = [];
    private readonly ObservableCollection<string> copyHistory = [];

    private LiveListConfigCache? configCache;
    private TrayService? tray;
    private StreamWatcherService? streamWatcher;
    private DevCommandService? devCommands;
    private FavoritesStore? favorites;
    private DislikedStore? disliked;

    private string statusText = "Loading live users...";
    private string endpointText = "Endpoint not loaded yet.";
    private string lastUpdatedText = "Not refreshed yet.";
    private string historyStatusText = "History covers live users observed by this tool in the last 24 hours.";
    private string dislikedStatusText = "Streamers you've marked disliked. They won't trigger notifications.";
    private string commandCopyStatus = "No command copied yet.";
    private string streamViewerTitleText = "Stream Viewer";
    private string streamViewerStatusText = "Choose a live user to view their stream here.";
    private string currentStreamTwitchUrl = string.Empty;
    private string currentStreamChannelSlug = string.Empty;
    private string streamViewerVersionBadgeText = string.Empty;
    private string streamViewerChannelBadgeText = string.Empty;
    private string searchText = string.Empty;
    private string statsText = string.Empty;
    private string selectedPresetName = string.Empty;
    private string newPresetName = string.Empty;
    private bool streamViewerHasVersionBadge;
    private bool streamViewerHasChannelBadge;
    private bool canRefresh = true;
    private bool soundAlertsEnabled = true;
    private bool fastPollEnabled;
    private bool isShowingHistory;
    private bool isShowingStream;
    private bool hasLoadedLiveSnapshot;
    private bool favoritesOnly;
    private bool isShowingDisliked;
    private HashSet<string> knownLiveUserKeys = new(StringComparer.OrdinalIgnoreCase);
    private int unreadLiveCount;
    private HwndSource? hwndSource;

    public MainWindow()
    {
        InitializeComponent();
        DataContext = this;
        SourceInitialized += OnSourceInitialized;
        refreshTimer.Tick += async (_, _) => await RefreshAsync();
        retryTimer.Tick += async (_, _) =>
        {
            retryTimer.Stop();
            await RefreshAsync();
        };
        Loaded += async (_, _) =>
        {
            InitializeServices();
            LoadLiveHistory();
            UpdateTimerInterval();
            refreshTimer.Start();
            await RefreshAsync();
        };
        Closed += (_, _) =>
        {
            hwndSource?.RemoveHook(WndProc);
            hwndSource = null;
            refreshTimer.Stop();
            retryTimer.Stop();
            foreach (var player in activeAlertPlayers)
            {
                player.Close();
            }
            activeAlertPlayers.Clear();
            streamWatcher?.Dispose();
            tray?.Dispose();
            httpClient.Dispose();
        };
        Activated += (_, _) => ClearUnreadBadge();
    }

    public event PropertyChangedEventHandler? PropertyChanged;

    public ObservableCollection<LiveUserViewModel> Users { get; } = [];

    public ObservableCollection<LiveHistoryEntryViewModel> HistoryEntries { get; } = [];

    public ObservableCollection<string> PresetNames => presetNames;

    public ObservableCollection<string> CopyHistory => copyHistory;

    public string StatusText
    {
        get => statusText;
        private set
        {
            if (SetProperty(ref statusText, value))
                RaisePropertyChanged(nameof(ViewPrimaryStatusText));
        }
    }

    public string EndpointText
    {
        get => endpointText;
        private set => SetProperty(ref endpointText, value);
    }

    public string LastUpdatedText
    {
        get => lastUpdatedText;
        private set
        {
            if (SetProperty(ref lastUpdatedText, value))
                RaisePropertyChanged(nameof(ViewSecondaryStatusText));
        }
    }

    public string HistoryStatusText
    {
        get => historyStatusText;
        private set
        {
            if (SetProperty(ref historyStatusText, value))
                RaisePropertyChanged(nameof(ViewPrimaryStatusText));
        }
    }

    public string DislikedStatusText
    {
        get => dislikedStatusText;
        private set
        {
            if (SetProperty(ref dislikedStatusText, value))
                RaisePropertyChanged(nameof(ViewPrimaryStatusText));
        }
    }

    public string CommandCopyStatus
    {
        get => commandCopyStatus;
        private set => SetProperty(ref commandCopyStatus, value);
    }

    public string StreamViewerTitleText
    {
        get => streamViewerTitleText;
        private set
        {
            if (SetProperty(ref streamViewerTitleText, value))
                RaisePropertyChanged(nameof(ViewTitleText));
        }
    }

    public string StreamViewerStatusText
    {
        get => streamViewerStatusText;
        private set
        {
            if (SetProperty(ref streamViewerStatusText, value))
                RaisePropertyChanged(nameof(ViewPrimaryStatusText));
        }
    }

    public string CurrentStreamTwitchUrl
    {
        get => currentStreamTwitchUrl;
        private set
        {
            if (SetProperty(ref currentStreamTwitchUrl, value))
                RaisePropertyChanged(nameof(ViewSecondaryStatusText));
        }
    }

    public string StreamViewerVersionBadgeText
    {
        get => streamViewerVersionBadgeText;
        private set => SetProperty(ref streamViewerVersionBadgeText, value);
    }

    public string StreamViewerChannelBadgeText
    {
        get => streamViewerChannelBadgeText;
        private set => SetProperty(ref streamViewerChannelBadgeText, value);
    }

    public bool StreamViewerHasVersionBadge
    {
        get => streamViewerHasVersionBadge;
        private set => SetProperty(ref streamViewerHasVersionBadge, value);
    }

    public bool StreamViewerHasChannelBadge
    {
        get => streamViewerHasChannelBadge;
        private set => SetProperty(ref streamViewerHasChannelBadge, value);
    }

    public bool CanRefresh
    {
        get => canRefresh;
        private set => SetProperty(ref canRefresh, value);
    }

    public bool SoundAlertsEnabled
    {
        get => soundAlertsEnabled;
        set => SetProperty(ref soundAlertsEnabled, value);
    }

    public bool FastPollEnabled
    {
        get => fastPollEnabled;
        set
        {
            if (SetProperty(ref fastPollEnabled, value))
            {
                UpdateTimerInterval();
            }
        }
    }

    public bool FavoritesOnly
    {
        get => favoritesOnly;
        set
        {
            if (SetProperty(ref favoritesOnly, value))
            {
                ApplySearchFilter();
            }
        }
    }

    public string SearchText
    {
        get => searchText;
        set
        {
            if (SetProperty(ref searchText, value))
            {
                ApplySearchFilter();
            }
        }
    }

    public string StatsText
    {
        get => statsText;
        private set => SetProperty(ref statsText, value);
    }

    public string SelectedPresetName
    {
        get => selectedPresetName;
        set => SetProperty(ref selectedPresetName, value);
    }

    public string NewPresetName
    {
        get => newPresetName;
        set => SetProperty(ref newPresetName, value);
    }

    public bool IsEmpty => Users.Count == 0;

    public bool IsLiveViewVisible => !isShowingHistory && !isShowingStream && !isShowingDisliked;

    public bool IsHistoryViewVisible => isShowingHistory && !isShowingStream;

    public bool IsDislikedViewVisible => isShowingDisliked && !isShowingStream;

    public bool IsStreamViewVisible => isShowingStream;

    public bool IsDecorativeBackdropVisible => !isShowingStream;

    public bool IsLiveEmptyVisible => IsLiveViewVisible && Users.Count == 0;

    public bool IsHistoryEmptyVisible => IsHistoryViewVisible && HistoryEntries.Count == 0;

    public bool IsDislikedEmptyVisible => IsDislikedViewVisible && Users.All(u => !u.IsDisliked);

    public string ViewTitleText => isShowingStream
        ? StreamViewerTitleText
        : isShowingHistory ? "24h Live History"
        : isShowingDisliked ? "Disliked Crystal Relay Users" : "Live Crystal Relay Users";

    public string ViewPrimaryStatusText => isShowingStream
        ? StreamViewerStatusText
        : isShowingHistory ? HistoryStatusText
        : isShowingDisliked ? DislikedStatusText : StatusText;

    public string ViewSecondaryStatusText => isShowingStream
        ? CurrentStreamTwitchUrl
        : isShowingHistory ? "Saved locally in AppData." : LastUpdatedText;

    private void InitializeServices()
    {
        var basePath = AppContext.BaseDirectory;
        var candidates = new[]
        {
            Path.Combine(basePath, "live-list.local.json"),
            Path.Combine(Environment.CurrentDirectory, "live-list.local.json"),
            Path.GetFullPath(Path.Combine(basePath, "..", "..", "..", "live-list.local.json")),
            Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
                "CrystalRelay",
                "bridge.runtime.json")
        };
        configCache = new LiveListConfigCache(candidates);

        var dataRoot = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
            "CrystalRelay",
            "DevTools",
            "LiveList");

        favorites = new FavoritesStore(Path.Combine(dataRoot, "favorites.json"));
        disliked = new DislikedStore(Path.Combine(dataRoot, "disliked.json"));
        devCommands = new DevCommandService(Path.Combine(dataRoot, "command-presets.json"));
        streamWatcher = new StreamWatcherService(StreamWebView);
        tray = new TrayService(this, ShowFromTray, () => _ = RefreshAsync());

        RefreshPresetNames();
        RefreshCopyHistory();
    }

    private void ShowFromTray()
    {
        Show();
        WindowState = WindowState.Normal;
        Activate();
    }

    private void UpdateTimerInterval()
    {
        refreshTimer.Interval = fastPollEnabled ? FastPollInterval : SlowPollInterval;
    }

    private async Task RefreshAsync()
    {
        if (!CanRefresh)
            return;

        CanRefresh = false;
        StatusText = "Refreshing live list...";
        try
        {
            var resolved = configCache?.Resolve() ?? new LiveListResolvedConfig(null, string.Empty, string.Empty);
            var endpoint = resolved.Endpoint;
            if (endpoint is null)
            {
                Users.Clear();
                knownLiveUserKeys = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
                hasLoadedLiveSnapshot = false;
                RaiseLiveViewPropertiesChanged();
                EndpointText = "Endpoint not configured. Create live-list.local.json beside this app, or set liveFeedbackHeartbeatEndpoint in Crystal Relay runtime config.";
                StatusText = "Waiting for endpoint configuration.";
                LastUpdatedText = "Not refreshed yet.";
                return;
            }

            EndpointText = $"Endpoint: {endpoint}";
            using var response = await httpClient.GetAsync(endpoint);
            response.EnsureSuccessStatusCode();
            await using var stream = await response.Content.ReadAsStreamAsync();
            var payload = await JsonSerializer.DeserializeAsync<LiveListResponse>(stream, JsonOptions);

            var incoming = BuildIncomingUsers(payload);
            var incomingKeys = incoming
                .Select(u => LiveUserKey.Normalize(u.TwitchUrl, u.DisplayName))
                .Where(k => !string.IsNullOrWhiteSpace(k))
                .ToHashSet(StringComparer.OrdinalIgnoreCase);

            var dislikedKeys = disliked is null
                ? new HashSet<string>(StringComparer.OrdinalIgnoreCase)
                : disliked.Keys.ToHashSet(StringComparer.OrdinalIgnoreCase);
            var alertableIncomingKeys = incomingKeys.Except(dislikedKeys).ToHashSet(StringComparer.OrdinalIgnoreCase);
            var shouldAlert = hasLoadedLiveSnapshot && alertableIncomingKeys.Any(k => !knownLiveUserKeys.Contains(k));
            var newFavoriteNames = hasLoadedLiveSnapshot
                ? incoming.Where(u => favorites is not null
                    && favorites.IsFavorite(LiveUserKey.Normalize(u.TwitchUrl, u.DisplayName))
                    && !dislikedKeys.Contains(LiveUserKey.Normalize(u.TwitchUrl, u.DisplayName))
                    && !knownLiveUserKeys.Contains(LiveUserKey.Normalize(u.TwitchUrl, u.DisplayName)))
                    .Select(u => u.DisplayName)
                    .ToList()
                : new List<string>();

            // #1: Diff the live list (no Clear/re-add churn).
            var diff = LiveListDiffer.Diff(Users, incoming,
                u => LiveUserKey.Normalize(u.TwitchUrl, u.DisplayName),
                (a, b) => string.Equals(a.RelayVersion, b.RelayVersion, StringComparison.Ordinal)
                         && string.Equals(a.BuildChannel, b.BuildChannel, StringComparison.Ordinal)
                         && a.LastPingAt == b.LastPingAt);
            LiveListDiffer.Apply(Users, diff, u => LiveUserKey.Normalize(u.TwitchUrl, u.DisplayName));

            // #2,#3,#7: History — upsert + dirty-flag save + single sort.
            historyStore.Upsert(incoming, DateTimeOffset.UtcNow);
            historyStore.Prune(DateTimeOffset.UtcNow);
            var sorted = historyStore.SortedSnapshot();
            var histIncoming = sorted.Select(r => new LiveHistoryEntryViewModel(r)).ToList();
            var histDiff = LiveListDiffer.Diff(HistoryEntries, histIncoming,
                h => LiveUserKey.Normalize(h.TwitchUrl, h.DisplayName),
                (a, b) => a.LastSeenLiveAt == b.LastSeenLiveAt
                         && string.Equals(a.DetailText, b.DetailText, StringComparison.Ordinal));
            LiveListDiffer.Apply(HistoryEntries, histDiff, h => LiveUserKey.Normalize(h.TwitchUrl, h.DisplayName));
            HistoryStatusText = HistoryEntries.Count == 1
                ? "1 streamer observed live in the last 24 hours."
                : $"{HistoryEntries.Count} streamers observed live in the last 24 hours.";
            if (historyStore.IsDirty)
            {
                SaveLiveHistory(sorted);
                historyStore.MarkClean();
            }

            // #17: Stats.
            stats.RecordSnapshot(Users.Count, incomingKeys);
            UpdateStatsText();

            // #8: key-set swap.
            knownLiveUserKeys = incomingKeys;

            // Alerts + badges + tray.
            if (shouldAlert)
            {
                var newlyLiveCount = alertableIncomingKeys.Count(k => !knownLiveUserKeys.Contains(k));
                unreadLiveCount += newlyLiveCount;
                UpdateUnreadBadge();
                if (SoundAlertsEnabled)
                    PlayLiveSoundAlert();
                var alertableUsers = Users.Where(u => !dislikedKeys.Contains(LiveUserKey.Normalize(u.TwitchUrl, u.DisplayName))).ToList();
                tray?.ShowBalloon("Crystal Relay live", alertableUsers.Count == 1
                    ? $"{alertableUsers[0].DisplayName} is live."
                    : $"{alertableUsers.Count} Crystal Relay users are live.");
                foreach (var name in newFavoriteNames)
                {
                    tray?.ShowBalloon("Favorite live", $"{name} just went live.");
                }
            }

            retryPolicy.Reset();
            hasLoadedLiveSnapshot = true;
            var updatedAt = payload?.UpdatedAt is { } value
                ? value.ToLocalTime().ToString("g")
                : DateTimeOffset.Now.ToString("g");
            StatusText = Users.Count == 1
                ? "1 Crystal Relay user is live."
                : $"{Users.Count} Crystal Relay users are live.";
            LastUpdatedText = $"Last updated: {updatedAt}.";
            ApplySearchFilter();
            RaiseLiveViewPropertiesChanged();
        }
        catch (Exception ex)
        {
            WriteCrashLogSafe("RefreshAsync", ex);
            StatusText = $"Could not load the live list: {ex.Message}";
            LastUpdatedText = $"Last attempt: {DateTimeOffset.Now:g}";
            // #19: retry with backoff.
            var delay = retryPolicy.NextDelay();
            retryTimer.Interval = delay;
            retryTimer.Start();
        }
        finally
        {
            CanRefresh = true;
        }
    }

    private List<LiveUserViewModel> BuildIncomingUsers(LiveListResponse? payload)
    {
        var result = new List<LiveUserViewModel>();
        foreach (var user in payload?.Users ?? [])
        {
            if (string.IsNullOrWhiteSpace(user.DisplayName) || string.IsNullOrWhiteSpace(user.TwitchUrl))
            {
                continue;
            }
            var key = LiveUserKey.Normalize(user.TwitchUrl, user.DisplayName);
            var isFavorite = favorites is not null && favorites.IsFavorite(key);
            var isDisliked = disliked is not null && disliked.IsDisliked(key);
            result.Add(new LiveUserViewModel(
                user.DisplayName,
                user.TwitchUrl,
                user.RelayVersion,
                user.BuildChannel,
                user.LastPingAt,
                isFavorite,
                isDisliked));
        }
        return result;
    }

    private void UpdateStatsText()
    {
        StatsText = $"Peak: {stats.PeakLive}  |  Unique seen: {stats.UniqueStreamersSeen}  |  Current: {stats.CurrentLive}  |  Session: {(int)stats.SessionDuration.TotalMinutes}m";
    }

    private void UpdateUnreadBadge()
    {
        if (TaskbarBadge is null) return;
        if (unreadLiveCount > 0)
        {
            TaskbarBadge.ProgressState = TaskbarItemProgressState.Normal;
            TaskbarBadge.ProgressValue = Math.Min(unreadLiveCount / 10.0, 1.0);
        }
    }

    private void ClearUnreadBadge()
    {
        unreadLiveCount = 0;
        if (TaskbarBadge is null) return;
        TaskbarBadge.ProgressState = TaskbarItemProgressState.None;
        TaskbarBadge.ProgressValue = 0;
    }

    private void ApplySearchFilter()
    {
        var liveView = CollectionViewSource.GetDefaultView(Users);
        liveView.Filter = isShowingDisliked ? FilterDislikedUser : FilterUser;
        liveView.Refresh();
        var historyView = CollectionViewSource.GetDefaultView(HistoryEntries);
        historyView.Filter = FilterHistoryEntry;
        historyView.Refresh();
    }

    private bool FilterUser(object item)
    {
        if (item is not LiveUserViewModel user) return false;
        var key = LiveUserKey.Normalize(user.TwitchUrl, user.DisplayName);
        if (disliked is not null && disliked.IsDisliked(key))
        {
            return false;
        }
        if (favoritesOnly && favorites is not null
            && !favorites.IsFavorite(key))
        {
            return false;
        }
        if (string.IsNullOrWhiteSpace(searchText)) return true;
        return user.DisplayName.Contains(searchText, StringComparison.OrdinalIgnoreCase)
            || user.TwitchUrl.Contains(searchText, StringComparison.OrdinalIgnoreCase);
    }

    private bool FilterDislikedUser(object item)
    {
        if (item is not LiveUserViewModel user) return false;
        if (disliked is null) return false;
        if (!disliked.IsDisliked(LiveUserKey.Normalize(user.TwitchUrl, user.DisplayName)))
        {
            return false;
        }
        if (string.IsNullOrWhiteSpace(searchText)) return true;
        return user.DisplayName.Contains(searchText, StringComparison.OrdinalIgnoreCase)
            || user.TwitchUrl.Contains(searchText, StringComparison.OrdinalIgnoreCase);
    }

    private bool FilterHistoryEntry(object item)
    {
        if (item is not LiveHistoryEntryViewModel entry) return false;
        if (string.IsNullOrWhiteSpace(searchText)) return true;
        return entry.DisplayName.Contains(searchText, StringComparison.OrdinalIgnoreCase)
            || entry.TwitchUrl.Contains(searchText, StringComparison.OrdinalIgnoreCase);
    }

    private void LoadLiveHistory()
    {
        var historyPath = GetLiveHistoryPath();
        if (File.Exists(historyPath))
        {
            try
            {
                var json = File.ReadAllText(historyPath);
                var store = JsonSerializer.Deserialize<LiveHistoryStorePayload>(json, JsonOptions);
                historyStore.Load(store?.Entries ?? []);
            }
            catch (Exception ex)
            {
                HistoryStatusText = "Could not load 24h history; starting fresh.";
                WriteCrashLogSafe("LoadLiveHistory", ex);
            }
        }
        historyStore.Prune(DateTimeOffset.UtcNow);
        RefreshLiveHistoryView();
        if (historyStore.IsDirty)
        {
            SaveLiveHistory(historyStore.SortedSnapshot());
            historyStore.MarkClean();
        }
    }

    private void RefreshLiveHistoryView()
    {
        var sorted = historyStore.SortedSnapshot();
        var histIncoming = sorted.Select(r => new LiveHistoryEntryViewModel(r)).ToList();
        var histDiff = LiveListDiffer.Diff(HistoryEntries, histIncoming,
            h => LiveUserKey.Normalize(h.TwitchUrl, h.DisplayName),
            (a, b) => a.LastSeenLiveAt == b.LastSeenLiveAt
                     && string.Equals(a.DetailText, b.DetailText, StringComparison.Ordinal));
        LiveListDiffer.Apply(HistoryEntries, histDiff, h => LiveUserKey.Normalize(h.TwitchUrl, h.DisplayName));
        HistoryStatusText = HistoryEntries.Count == 1
            ? "1 streamer observed live in the last 24 hours."
            : $"{HistoryEntries.Count} streamers observed live in the last 24 hours.";
        RaiseHistoryViewPropertiesChanged();
    }

    private void SaveLiveHistory(IReadOnlyList<LiveHistoryEntryRecord> sorted)
    {
        var historyPath = GetLiveHistoryPath();
        var tempPath = $"{historyPath}.tmp";
        try
        {
            var directory = Path.GetDirectoryName(historyPath);
            if (!string.IsNullOrWhiteSpace(directory))
            {
                Directory.CreateDirectory(directory);
            }

            var store = new LiveHistoryStorePayload
            {
                UpdatedAt = DateTimeOffset.UtcNow,
                Entries = sorted.ToList()
            };
            var writeOptions = new JsonSerializerOptions(JsonOptions) { WriteIndented = true };
            File.WriteAllText(tempPath, JsonSerializer.Serialize(store, writeOptions));
            if (File.Exists(historyPath))
            {
                File.Replace(tempPath, historyPath, null);
            }
            else
            {
                File.Move(tempPath, historyPath);
            }
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or NotSupportedException)
        {
            HistoryStatusText = $"Could not save 24h history: {ex.Message}";
            try
            {
                if (File.Exists(tempPath))
                    File.Delete(tempPath);
            }
            catch
            {
            }
        }
    }

    private static string GetLiveHistoryPath()
    {
        return Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
            "CrystalRelay",
            "DevTools",
            "LiveList",
            "live-history.json");
    }

    private void PlayLiveSoundAlert()
    {
        var resolved = configCache?.Resolve();
        var alertSoundPath = resolved?.AlertSoundPath ?? string.Empty;
        if (!string.IsNullOrWhiteSpace(alertSoundPath)
            && File.Exists(alertSoundPath)
            && TryPlayConfiguredLiveSoundAlert(alertSoundPath))
        {
            return;
        }
        PlayFallbackLiveSoundAlert();
    }

    private bool TryPlayConfiguredLiveSoundAlert(string alertSoundPath)
    {
        MediaPlayer? player = null;
        try
        {
            player = new MediaPlayer();
            activeAlertPlayers.Add(player);

            void Cleanup()
            {
                if (player is null) return;
                player.MediaEnded -= OnMediaEnded;
                player.MediaFailed -= OnMediaFailed;
                player.Close();
                activeAlertPlayers.Remove(player);
                player = null;
            }

            void OnMediaEnded(object? sender, EventArgs e) => Cleanup();
            void OnMediaFailed(object? sender, ExceptionEventArgs e)
            {
                Cleanup();
                PlayFallbackLiveSoundAlert();
            }

            player.MediaEnded += OnMediaEnded;
            player.MediaFailed += OnMediaFailed;
            player.Open(new Uri(alertSoundPath, UriKind.Absolute));
            player.Play();
            return true;
        }
        catch
        {
            if (player is not null)
            {
                player.Close();
                activeAlertPlayers.Remove(player);
            }
            return false;
        }
    }

    private static void PlayFallbackLiveSoundAlert()
    {
        try
        {
            SystemSounds.Asterisk.Play();
        }
        catch
        {
        }
    }

    private void OnRefreshClicked(object sender, RoutedEventArgs e)
    {
        configCache?.Invalidate();
        _ = RefreshAsync();
    }

    private void OnShowLiveNowClicked(object sender, RoutedEventArgs e)
    {
        SetLiveListView();
    }

    private void OnShowHistoryClicked(object sender, RoutedEventArgs e)
    {
        SetHistoryView(true);
    }

    private void SetLiveListView()
    {
        if (!isShowingHistory && !isShowingStream && !isShowingDisliked)
            return;
        if (isShowingStream)
            StopStreamViewer();
        isShowingHistory = false;
        isShowingStream = false;
        isShowingDisliked = false;
        RaiseViewModePropertiesChanged();
        UpdateStoryboardState();
        ApplySearchFilter();
    }

    private void SetHistoryView(bool showHistory)
    {
        if (isShowingHistory == showHistory && !isShowingStream && !isShowingDisliked)
            return;
        if (isShowingStream)
            StopStreamViewer();
        isShowingHistory = showHistory;
        isShowingStream = false;
        isShowingDisliked = false;
        RaiseViewModePropertiesChanged();
        UpdateStoryboardState();
    }

    private void SetDislikedView()
    {
        if (isShowingDisliked && !isShowingStream)
            return;
        if (isShowingStream)
            StopStreamViewer();
        isShowingHistory = false;
        isShowingStream = false;
        isShowingDisliked = true;
        RaiseViewModePropertiesChanged();
        UpdateStoryboardState();
        ApplySearchFilter();
    }

    private void OnShowDislikedClicked(object sender, RoutedEventArgs e)
    {
        SetDislikedView();
    }

    private void OnViewStreamClicked(object sender, RoutedEventArgs e)
    {
        if (sender is not Button { Tag: string twitchUrl } || string.IsNullOrWhiteSpace(twitchUrl))
            return;
        _ = ViewStreamAsync(twitchUrl);
    }

    private void OnOpenStreamClicked(object sender, RoutedEventArgs e)
    {
        if (sender is not Button { Tag: string twitchUrl } || string.IsNullOrWhiteSpace(twitchUrl))
            return;
        OpenTwitchUrl(twitchUrl, "Could not open the Twitch stream link.");
    }

    private void OnOpenVodsClicked(object sender, RoutedEventArgs e)
    {
        if (sender is not Button { Tag: string twitchUrl } || string.IsNullOrWhiteSpace(twitchUrl))
            return;
        OpenTwitchUrl(twitchUrl, "Could not open the Twitch VOD link.");
    }

    private void OnToggleFavoriteClicked(object sender, RoutedEventArgs e)
    {
        if (sender is not Button { Tag: string twitchUrl } || favorites is null)
            return;
        var key = LiveUserKey.Normalize(twitchUrl, null);
        var nowFavorite = favorites.Toggle(key);
        if (nowFavorite && disliked is not null && disliked.IsDisliked(key))
        {
            disliked.Toggle(key);
        }
        RefreshUserClassification(key);
        ApplySearchFilter();
        RaiseViewModePropertiesChanged();
    }

    private void OnToggleDislikedClicked(object sender, RoutedEventArgs e)
    {
        if (sender is not Button { Tag: string twitchUrl } || disliked is null)
            return;
        var key = LiveUserKey.Normalize(twitchUrl, null);
        var nowDisliked = disliked.Toggle(key);
        if (nowDisliked && favorites is not null && favorites.IsFavorite(key))
        {
            favorites.Toggle(key);
        }
        RefreshUserClassification(key);
        ApplySearchFilter();
        RaiseViewModePropertiesChanged();
    }

    private void RefreshUserClassification(string key)
    {
        if (string.IsNullOrWhiteSpace(key)) return;
        var isFavorite = favorites is not null && favorites.IsFavorite(key);
        var isDisliked = disliked is not null && disliked.IsDisliked(key);
        foreach (var u in Users)
        {
            if (string.Equals(LiveUserKey.Normalize(u.TwitchUrl, u.DisplayName), key, StringComparison.OrdinalIgnoreCase))
            {
                u.RefreshClassification(isFavorite, isDisliked);
            }
        }
    }

    private async Task ViewStreamAsync(string twitchUrl)
    {
        if (!LiveUserKey.TryGetChannelSlug(twitchUrl, out var channelSlug))
        {
            StatusText = "Could not read the Twitch channel from that live user.";
            return;
        }

        CurrentStreamTwitchUrl = twitchUrl.Trim();
        currentStreamChannelSlug = channelSlug;
        StreamViewerTitleText = $"Viewing {channelSlug}";
        StreamViewerStatusText = "Loading Twitch video and chat...";

        var user = Users.FirstOrDefault(u =>
            string.Equals(u.TwitchUrl, twitchUrl, StringComparison.OrdinalIgnoreCase));
        if (user is not null)
        {
            StreamViewerVersionBadgeText = user.VersionBadgeText;
            StreamViewerChannelBadgeText = user.ChannelBadgeText;
            StreamViewerHasVersionBadge = user.HasVersionBadge;
            StreamViewerHasChannelBadge = user.HasChannelBadge;
        }
        else
        {
            StreamViewerVersionBadgeText = string.Empty;
            StreamViewerChannelBadgeText = string.Empty;
            StreamViewerHasVersionBadge = false;
            StreamViewerHasChannelBadge = false;
        }

        isShowingHistory = false;
        isShowingStream = true;
        RaiseViewModePropertiesChanged();
        UpdateStoryboardState();

        try
        {
            if (streamWatcher is null) return;
            await streamWatcher.EnsureReadyAsync();
            streamWatcher.Navigate(channelSlug);
            StreamViewerStatusText = "Twitch video and chat are loading inside Crystal Relay Live Feedback.";
        }
        catch (Exception ex)
        {
            StreamViewerStatusText = $"Could not open the in-app viewer: {ex.Message}";
        }
    }

    private void OnBackToLiveListClicked(object sender, RoutedEventArgs e)
    {
        if (LiveNowModeButton is not null)
            LiveNowModeButton.IsChecked = true;
        SetLiveListView();
    }

    private async void OnClearTwitchLoginClicked(object sender, RoutedEventArgs e)
    {
        try
        {
            if (streamWatcher is null) return;
            await streamWatcher.EnsureReadyAsync();
            await streamWatcher.ClearLoginAsync(currentStreamChannelSlug);
            StreamViewerStatusText = "Cleared the Twitch login data for this dev tool viewer.";
        }
        catch (Exception ex)
        {
            StreamViewerStatusText = $"Could not clear the Twitch login data: {ex.Message}";
        }
    }

    private void StopStreamViewer()
    {
        streamWatcher?.Stop();
        CurrentStreamTwitchUrl = string.Empty;
        currentStreamChannelSlug = string.Empty;
        StreamViewerStatusText = "Choose a live user to view their stream here.";
        StreamViewerVersionBadgeText = string.Empty;
        StreamViewerChannelBadgeText = string.Empty;
        StreamViewerHasVersionBadge = false;
        StreamViewerHasChannelBadge = false;
    }

    private void OpenTwitchUrl(string twitchUrl, string failureStatusText)
    {
        try
        {
            Process.Start(new ProcessStartInfo(twitchUrl) { UseShellExecute = true });
        }
        catch
        {
            StatusText = failureStatusText;
        }
    }

    private void OnCopyDevCommandClicked(object sender, RoutedEventArgs e)
    {
        if (devCommands is null || sender is not Button { Tag: string commandKind })
            return;

        var command = commandKind switch
        {
            "grow" => devCommands.BuildGrow(
                ClampReadDouble(GrowMetersBox, 0.001, 5.0, 0.25),
                ClampReadInt(GrowSecondsBox, 1, 300, 30),
                ClampReadDouble(GrowTransitionBox, 0, 30, 1)),
            "shrink" => devCommands.BuildShrink(
                ClampReadDouble(GrowMetersBox, 0.001, 5.0, 0.25),
                ClampReadInt(GrowSecondsBox, 1, 300, 30),
                ClampReadDouble(GrowTransitionBox, 0, 30, 1)),
            "scalerandom" => devCommands.BuildScaleRandom(
                ClampReadDouble(ScaleRandomMinBox, 0.1, 5.0, 0.8),
                ClampReadDouble(ScaleRandomMaxBox, 0.1, 5.0, 2.0),
                ClampReadInt(ScaleRandomSecondsBox, 1, 300, 20)),
            "move" => devCommands.BuildMove(
                GetSelectedMoveDirection(),
                ClampReadInt(MoveSecondsBox, 1, 60, 5)),
            "moverandom" => devCommands.BuildMoveRandom(
                ClampReadInt(MoveRandomSecondsBox, 1, 120, 12)),
            "firesale" => devCommands.BuildFireSale(
                ClampReadInt(FireSalePercentBox, 1, 100, 25),
                ClampReadInt(FireSaleSecondsBox, 1, 3600, 120)),
            _ => string.Empty
        };

        if (string.IsNullOrWhiteSpace(command))
            return;

        CopyCommand(command);
    }

    private void CopyCommand(string command)
    {
        try
        {
            Clipboard.SetText(command);
            CommandCopyStatus = $"Copied: {command}";
            devCommands?.RecordCopy(command);
            RefreshCopyHistory();
        }
        catch (Exception ex)
        {
            CommandCopyStatus = $"Could not copy command: {ex.Message}";
        }
    }

    private void OnCopyHistoryItemClicked(object sender, RoutedEventArgs e)
    {
        if (sender is not Button { Tag: string command } || string.IsNullOrWhiteSpace(command))
            return;
        CopyCommand(command);
    }

    private void OnSavePresetClicked(object sender, RoutedEventArgs e)
    {
        if (devCommands is null || string.IsNullOrWhiteSpace(NewPresetName))
            return;
        var command = CommandCopyStatus.StartsWith("Copied: ")
            ? CommandCopyStatus["Copied: ".Length..]
            : string.Empty;
        if (string.IsNullOrWhiteSpace(command))
        {
            CommandCopyStatus = "Copy a command first, then save it as a preset.";
            return;
        }
        devCommands.SavePreset(NewPresetName, command);
        CommandCopyStatus = $"Saved preset '{NewPresetName}'.";
        NewPresetName = string.Empty;
        RefreshPresetNames();
    }

    private void OnDeletePresetClicked(object sender, RoutedEventArgs e)
    {
        if (devCommands is null || string.IsNullOrWhiteSpace(SelectedPresetName))
            return;
        if (devCommands.DeletePreset(SelectedPresetName))
        {
            CommandCopyStatus = $"Deleted preset '{SelectedPresetName}'.";
            SelectedPresetName = string.Empty;
        }
        RefreshPresetNames();
    }

    private void OnLoadPresetClicked(object sender, RoutedEventArgs e)
    {
        if (devCommands is null || string.IsNullOrWhiteSpace(SelectedPresetName))
            return;
        var presets = devCommands.LoadPresets();
        if (presets.TryGetValue(SelectedPresetName, out var command))
        {
            CopyCommand(command);
        }
    }

    private void RefreshPresetNames()
    {
        presetNames.Clear();
        if (devCommands is null) return;
        foreach (var name in devCommands.LoadPresets().Keys.OrderBy(n => n, StringComparer.OrdinalIgnoreCase))
        {
            presetNames.Add(name);
        }
    }

    private void RefreshCopyHistory()
    {
        copyHistory.Clear();
        if (devCommands is null) return;
        foreach (var cmd in devCommands.CopyHistory())
        {
            copyHistory.Add(cmd);
        }
    }

    private void OnRandomizeCommandsClicked(object sender, RoutedEventArgs e)
    {
        GrowMetersBox.Text = RandomDoubleText(0.1, 0.5);
        GrowSecondsBox.Text = RandomIntText(10, 60);
        GrowTransitionBox.Text = RandomDoubleText(0, 3);
        ScaleRandomMinBox.Text = RandomDoubleText(0.5, 1.4);
        var randomMin = ClampReadDouble(ScaleRandomMinBox, 0.1, 5.0, 0.8);
        ScaleRandomMaxBox.Text = RandomDoubleText(Math.Min(4.8, randomMin + 0.2), 3.0);
        ScaleRandomSecondsBox.Text = RandomIntText(10, 60);
        MoveDirectionBox.SelectedIndex = Random.Shared.Next(0, Math.Max(1, MoveDirectionBox.Items.Count));
        MoveSecondsBox.Text = RandomIntText(2, 12);
        MoveRandomSecondsBox.Text = RandomIntText(5, 20);
        FireSalePercentBox.Text = RandomIntText(10, 75);
        FireSaleSecondsBox.Text = RandomIntText(60, 300);
        CommandCopyStatus = "Randomized command values.";
    }

    private string GetSelectedMoveDirection()
    {
        return MoveDirectionBox.SelectedItem is ComboBoxItem item
            && item.Content is string direction
            && !string.IsNullOrWhiteSpace(direction)
                ? direction.Trim()
                : "forward";
    }

    private static double ClampReadDouble(TextBox textBox, double minimum, double maximum, double fallback)
    {
        if (!double.TryParse(textBox.Text, NumberStyles.Float, CultureInfo.InvariantCulture, out var value)
            || double.IsNaN(value)
            || double.IsInfinity(value))
        {
            value = fallback;
        }
        value = Math.Clamp(value, minimum, maximum);
        textBox.Text = value.ToString("0.###", CultureInfo.InvariantCulture);
        return value;
    }

    private static int ClampReadInt(TextBox textBox, int minimum, int maximum, int fallback)
    {
        if (!int.TryParse(textBox.Text, NumberStyles.Integer, CultureInfo.InvariantCulture, out var value))
        {
            value = fallback;
        }
        value = Math.Clamp(value, minimum, maximum);
        textBox.Text = value.ToString(CultureInfo.InvariantCulture);
        return value;
    }

    private static string RandomDoubleText(double minimum, double maximum)
    {
        var low = Math.Min(minimum, maximum);
        var high = Math.Max(minimum, maximum);
        var value = low + Random.Shared.NextDouble() * (high - low);
        return value.ToString("0.###", CultureInfo.InvariantCulture);
    }

    private static string RandomIntText(int minimum, int maximum)
    {
        var low = Math.Min(minimum, maximum);
        var high = Math.Max(minimum, maximum);
        return Random.Shared.Next(low, high + 1).ToString(CultureInfo.InvariantCulture);
    }

    private void UpdateStoryboardState()
    {
        // #4: pause the decorative storyboard when hidden or minimized.
        try
        {
            if (DecorativeStoryboard is null) return;
            if (IsDecorativeBackdropVisible && WindowState != WindowState.Minimized)
            {
                DecorativeStoryboard.Begin(this, isControllable: true);
            }
            else
            {
                DecorativeStoryboard.Pause(this);
            }
        }
        catch
        {
        }
    }

    protected override void OnStateChanged(EventArgs e)
    {
        base.OnStateChanged(e);
        UpdateStoryboardState();
    }

    private void OnTitleBarMouseDown(object sender, MouseButtonEventArgs e)
    {
        if (e.ChangedButton != MouseButton.Left)
            return;
        if (e.ClickCount == 2)
        {
            ToggleMaximizeRestore();
            return;
        }
        DragMove();
    }

    private void ToggleMaximizeRestore()
    {
        WindowState = WindowState == WindowState.Maximized ? WindowState.Normal : WindowState.Maximized;
    }

    private void OnMinimizeClicked(object sender, RoutedEventArgs e)
    {
        WindowState = WindowState.Minimized;
    }

    private void OnMaximizeRestoreClicked(object sender, RoutedEventArgs e)
    {
        ToggleMaximizeRestore();
    }

    private void OnCloseClicked(object sender, RoutedEventArgs e)
    {
        Close();
    }

    private void OnSourceInitialized(object? sender, EventArgs e)
    {
        hwndSource = HwndSource.FromHwnd(new WindowInteropHelper(this).Handle);
        hwndSource?.AddHook(WndProc);
    }

    private IntPtr WndProc(IntPtr hwnd, int msg, IntPtr wParam, IntPtr lParam, ref bool handled)
    {
        if (msg != WmNcHitTest || WindowState == WindowState.Maximized)
            return IntPtr.Zero;
        var screenPoint = GetScreenPoint(lParam);
        var windowPoint = PointFromScreen(screenPoint);
        var hitTest = GetResizeHitTest(windowPoint);
        if (hitTest == 0)
            return IntPtr.Zero;
        handled = true;
        return new IntPtr(hitTest);
    }

    private int GetResizeHitTest(Point point)
    {
        if (ActualWidth <= 0 || ActualHeight <= 0)
            return 0;
        var nearLeft = point.X >= 0 && point.X <= ResizeHitTestThickness;
        var nearRight = point.X >= ActualWidth - ResizeHitTestThickness && point.X <= ActualWidth;
        var nearTop = point.Y >= 0 && point.Y <= ResizeHitTestThickness;
        var nearBottom = point.Y >= ActualHeight - ResizeHitTestThickness && point.Y <= ActualHeight;
        if (nearTop && nearLeft) return HtTopLeft;
        if (nearTop && nearRight) return HtTopRight;
        if (nearBottom && nearLeft) return HtBottomLeft;
        if (nearBottom && nearRight) return HtBottomRight;
        if (nearLeft) return HtLeft;
        if (nearRight) return HtRight;
        if (nearTop) return HtTop;
        return nearBottom ? HtBottom : 0;
    }

    private static Point GetScreenPoint(IntPtr lParam)
    {
        var value = lParam.ToInt64();
        var x = unchecked((short)(value & 0xFFFF));
        var y = unchecked((short)((value >> 16) & 0xFFFF));
        return new Point(x, y);
    }

    private bool SetProperty<T>(ref T field, T value, [CallerMemberName] string? propertyName = null)
    {
        if (EqualityComparer<T>.Default.Equals(field, value))
            return false;
        field = value;
        RaisePropertyChanged(propertyName);
        return true;
    }

    private void RaisePropertyChanged([CallerMemberName] string? propertyName = null)
    {
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
    }

    private void RaiseLiveViewPropertiesChanged()
    {
        RaisePropertyChanged(nameof(IsEmpty));
        RaisePropertyChanged(nameof(IsLiveEmptyVisible));
    }

    private void RaiseHistoryViewPropertiesChanged()
    {
        RaisePropertyChanged(nameof(IsHistoryEmptyVisible));
    }

    private void RaiseViewModePropertiesChanged()
    {
        RaisePropertyChanged(nameof(IsLiveViewVisible));
        RaisePropertyChanged(nameof(IsHistoryViewVisible));
        RaisePropertyChanged(nameof(IsDislikedViewVisible));
        RaisePropertyChanged(nameof(IsStreamViewVisible));
        RaisePropertyChanged(nameof(IsDecorativeBackdropVisible));
        RaisePropertyChanged(nameof(IsLiveEmptyVisible));
        RaisePropertyChanged(nameof(IsHistoryEmptyVisible));
        RaisePropertyChanged(nameof(IsDislikedEmptyVisible));
        RaisePropertyChanged(nameof(ViewTitleText));
        RaisePropertyChanged(nameof(ViewPrimaryStatusText));
        RaisePropertyChanged(nameof(ViewSecondaryStatusText));
    }

    private static void WriteCrashLogSafe(string source, Exception ex)
    {
        try
        {
            var folder = Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
                "CrystalRelay",
                "DevTools",
                "LiveList",
                "CrashLogs");
            Directory.CreateDirectory(folder);
            var stamp = DateTime.Now.ToString("yyyyMMdd-HHmmss");
            var path = Path.Combine(folder, $"livelist-{stamp}-{Guid.NewGuid():N}.txt");
            File.WriteAllText(path, $"{source}:{Environment.NewLine}{ex}{Environment.NewLine}");
        }
        catch
        {
        }
    }

    private sealed class LiveListResponse
    {
        public DateTimeOffset? UpdatedAt { get; set; }
        public List<LiveUserResponse> Users { get; set; } = [];
    }

    private sealed class LiveUserResponse
    {
        public string DisplayName { get; set; } = string.Empty;
        public string TwitchUrl { get; set; } = string.Empty;
        public string RelayVersion { get; set; } = string.Empty;
        public string BuildChannel { get; set; } = string.Empty;
        public DateTimeOffset? LastPingAt { get; set; }
    }

    private sealed class LiveHistoryStorePayload
    {
        public DateTimeOffset UpdatedAt { get; set; }
        public List<LiveHistoryEntryRecord> Entries { get; set; } = [];
    }
}
