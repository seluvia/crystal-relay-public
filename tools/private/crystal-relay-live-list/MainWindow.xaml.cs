using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Diagnostics;
using System.Globalization;
using System.IO;
using System.Media;
using System.Net.Http;
using System.Runtime.CompilerServices;
using System.Text.Json;
using System.Text.Json.Serialization;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Interop;
using System.Windows.Media;
using System.Windows.Threading;

namespace CrystalRelayLiveList;

public partial class MainWindow : Window, INotifyPropertyChanged
{
    private static readonly TimeSpan RefreshInterval = TimeSpan.FromMinutes(5);
    private static readonly TimeSpan RequestTimeout = TimeSpan.FromSeconds(12);
    private static readonly TimeSpan HistoryWindow = TimeSpan.FromHours(24);
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

    private readonly HttpClient httpClient = new()
    {
        Timeout = RequestTimeout
    };
    private readonly DispatcherTimer refreshTimer = new()
    {
        Interval = RefreshInterval
    };
    private readonly List<MediaPlayer> activeAlertPlayers = [];
    private readonly Dictionary<string, LiveHistoryEntryRecord> liveHistory = new(StringComparer.OrdinalIgnoreCase);

    private string statusText = "Loading live users...";
    private string endpointText = "Endpoint not loaded yet.";
    private string lastUpdatedText = "Not refreshed yet.";
    private string historyStatusText = "History covers live users observed by this tool in the last 24 hours.";
    private string commandCopyStatus = "No command copied yet.";
    private bool canRefresh = true;
    private bool soundAlertsEnabled = true;
    private bool isShowingHistory;
    private bool hasLoadedLiveSnapshot;
    private readonly HashSet<string> knownLiveUserKeys = new(StringComparer.OrdinalIgnoreCase);
    private HwndSource? hwndSource;

    public MainWindow()
    {
        InitializeComponent();
        DataContext = this;
        SourceInitialized += OnSourceInitialized;
        refreshTimer.Tick += async (_, _) => await RefreshAsync();
        Loaded += async (_, _) =>
        {
            LoadLiveHistory();
            refreshTimer.Start();
            await RefreshAsync();
        };
        Closed += (_, _) =>
        {
            hwndSource?.RemoveHook(WndProc);
            hwndSource = null;
            refreshTimer.Stop();
            foreach (var player in activeAlertPlayers)
            {
                player.Close();
            }

            activeAlertPlayers.Clear();
            httpClient.Dispose();
        };
    }

    public event PropertyChangedEventHandler? PropertyChanged;

    public ObservableCollection<LiveUserViewModel> Users { get; } = [];

    public ObservableCollection<LiveHistoryEntryViewModel> HistoryEntries { get; } = [];

    public string StatusText
    {
        get => statusText;
        private set
        {
            if (SetProperty(ref statusText, value))
            {
                RaisePropertyChanged(nameof(ViewPrimaryStatusText));
            }
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
            {
                RaisePropertyChanged(nameof(ViewSecondaryStatusText));
            }
        }
    }

    public string HistoryStatusText
    {
        get => historyStatusText;
        private set
        {
            if (SetProperty(ref historyStatusText, value))
            {
                RaisePropertyChanged(nameof(ViewPrimaryStatusText));
            }
        }
    }

    public string CommandCopyStatus
    {
        get => commandCopyStatus;
        private set => SetProperty(ref commandCopyStatus, value);
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

    public bool IsEmpty => Users.Count == 0;

    public bool IsLiveViewVisible => !isShowingHistory;

    public bool IsHistoryViewVisible => isShowingHistory;

    public bool IsLiveEmptyVisible => IsLiveViewVisible && Users.Count == 0;

    public bool IsHistoryEmptyVisible => IsHistoryViewVisible && HistoryEntries.Count == 0;

    public string ViewTitleText => isShowingHistory ? "24h Live History" : "Live Crystal Relay Users";

    public string ViewPrimaryStatusText => isShowingHistory ? HistoryStatusText : StatusText;

    public string ViewSecondaryStatusText => isShowingHistory ? "Saved locally in AppData." : LastUpdatedText;

    private async Task RefreshAsync()
    {
        if (!CanRefresh)
        {
            return;
        }

        CanRefresh = false;
        StatusText = "Refreshing live list...";
        try
        {
            var endpoint = ResolveLiveApiEndpoint();
            if (endpoint is null)
            {
                Users.Clear();
                knownLiveUserKeys.Clear();
                hasLoadedLiveSnapshot = false;
                RaiseLiveViewPropertiesChanged();
                EndpointText = "Endpoint not configured. Create live-list.local.json beside this app, or set liveFeedbackHeartbeatEndpoint in Crystal Relay runtime config.";
                StatusText = "Waiting for endpoint configuration.";
                LastUpdatedText = "Not refreshed yet.";
                return;
            }

            EndpointText = $"Endpoint: {endpoint}";
            using var response = await httpClient.GetAsync(endpoint);
            if (!response.IsSuccessStatusCode)
            {
                StatusText = $"Live list returned HTTP {(int)response.StatusCode}.";
                LastUpdatedText = $"Last attempt: {DateTimeOffset.Now:g}";
                return;
            }

            await using var stream = await response.Content.ReadAsStreamAsync();
            var payload = await JsonSerializer.DeserializeAsync<LiveListResponse>(stream, JsonOptions);
            var liveUsers = new List<LiveUserViewModel>();
            var currentLiveUserKeys = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            Users.Clear();
            foreach (var user in payload?.Users ?? [])
            {
                if (string.IsNullOrWhiteSpace(user.DisplayName) || string.IsNullOrWhiteSpace(user.TwitchUrl))
                {
                    continue;
                }

                var liveUser = new LiveUserViewModel(
                    user.DisplayName,
                    user.TwitchUrl,
                    user.RelayVersion,
                    user.BuildChannel,
                    user.LastPingAt);
                liveUsers.Add(liveUser);
                currentLiveUserKeys.Add(CreateLiveUserKey(liveUser));
            }

            var shouldPlayLiveAlert = false;
            if (hasLoadedLiveSnapshot)
            {
                foreach (var userKey in currentLiveUserKeys)
                {
                    if (!knownLiveUserKeys.Contains(userKey))
                    {
                        shouldPlayLiveAlert = true;
                        break;
                    }
                }
            }

            foreach (var liveUser in liveUsers)
            {
                Users.Add(liveUser);
            }

            UpdateLiveHistory(liveUsers, DateTimeOffset.UtcNow);
            RaiseLiveViewPropertiesChanged();
            if (shouldPlayLiveAlert && SoundAlertsEnabled)
            {
                PlayLiveSoundAlert();
            }

            knownLiveUserKeys.Clear();
            foreach (var userKey in currentLiveUserKeys)
            {
                knownLiveUserKeys.Add(userKey);
            }

            hasLoadedLiveSnapshot = true;
            var updatedAt = payload?.UpdatedAt is { } value
                ? value.ToLocalTime().ToString("g")
                : DateTimeOffset.Now.ToString("g");
            StatusText = Users.Count == 1
                ? "1 Crystal Relay user is live."
                : $"{Users.Count} Crystal Relay users are live.";
            LastUpdatedText = $"Last updated: {updatedAt}.";
        }
        catch (Exception ex) when (ex is HttpRequestException or TaskCanceledException or JsonException or IOException or InvalidOperationException)
        {
            StatusText = $"Could not load the live list: {ex.Message}";
            LastUpdatedText = $"Last attempt: {DateTimeOffset.Now:g}";
        }
        finally
        {
            CanRefresh = true;
        }
    }

    private static string CreateLiveUserKey(LiveUserViewModel user)
    {
        return NormalizeLiveUserKey(user.TwitchUrl, user.DisplayName);
    }

    private void LoadLiveHistory()
    {
        liveHistory.Clear();
        var historyPath = GetLiveHistoryPath();
        if (File.Exists(historyPath))
        {
            try
            {
                var json = File.ReadAllText(historyPath);
                var store = JsonSerializer.Deserialize<LiveHistoryStore>(json, JsonOptions);
                foreach (var entry in store?.Entries ?? [])
                {
                    UpsertLoadedHistoryEntry(entry);
                }
            }
            catch (Exception ex) when (ex is JsonException or IOException or UnauthorizedAccessException or NotSupportedException)
            {
                HistoryStatusText = "Could not load 24h history; starting fresh.";
            }
        }

        PruneLiveHistory(DateTimeOffset.UtcNow);
        RefreshLiveHistoryView();
        SaveLiveHistory();
    }

    private void UpdateLiveHistory(IEnumerable<LiveUserViewModel> liveUsers, DateTimeOffset observedAt)
    {
        foreach (var user in liveUsers)
        {
            var key = NormalizeLiveUserKey(user.TwitchUrl, user.DisplayName);
            if (string.IsNullOrWhiteSpace(key))
            {
                continue;
            }

            var lastSeenAt = (user.LastPingAt ?? observedAt).ToUniversalTime();
            if (liveHistory.TryGetValue(key, out var existing))
            {
                existing.DisplayName = user.DisplayName;
                existing.TwitchUrl = user.TwitchUrl;
                existing.RelayVersion = user.RelayVersion;
                existing.BuildChannel = user.BuildChannel;
                existing.LastSeenLiveAt = lastSeenAt > existing.LastSeenLiveAt
                    ? lastSeenAt
                    : existing.LastSeenLiveAt;
                if (existing.LastSeenLiveAt < existing.FirstSeenLiveAt)
                {
                    existing.LastSeenLiveAt = observedAt.ToUniversalTime();
                }

                continue;
            }

            var firstSeenAt = observedAt.ToUniversalTime();
            liveHistory[key] = new LiveHistoryEntryRecord
            {
                Key = key,
                DisplayName = user.DisplayName,
                TwitchUrl = user.TwitchUrl,
                RelayVersion = user.RelayVersion,
                BuildChannel = user.BuildChannel,
                FirstSeenLiveAt = firstSeenAt,
                LastSeenLiveAt = lastSeenAt < firstSeenAt ? firstSeenAt : lastSeenAt
            };
        }

        PruneLiveHistory(observedAt);
        RefreshLiveHistoryView();
        SaveLiveHistory();
    }

    private void UpsertLoadedHistoryEntry(LiveHistoryEntryRecord entry)
    {
        var key = NormalizeLiveUserKey(entry.TwitchUrl, entry.DisplayName);
        if (string.IsNullOrWhiteSpace(key))
        {
            key = entry.Key?.Trim() ?? string.Empty;
        }

        if (string.IsNullOrWhiteSpace(key))
        {
            return;
        }

        var cleanEntry = new LiveHistoryEntryRecord
        {
            Key = key,
            DisplayName = entry.DisplayName?.Trim() ?? string.Empty,
            TwitchUrl = entry.TwitchUrl?.Trim() ?? string.Empty,
            RelayVersion = entry.RelayVersion?.Trim() ?? string.Empty,
            BuildChannel = entry.BuildChannel?.Trim() ?? string.Empty,
            FirstSeenLiveAt = entry.FirstSeenLiveAt.ToUniversalTime(),
            LastSeenLiveAt = entry.LastSeenLiveAt.ToUniversalTime()
        };
        if (cleanEntry.FirstSeenLiveAt == default)
        {
            cleanEntry.FirstSeenLiveAt = cleanEntry.LastSeenLiveAt == default
                ? DateTimeOffset.UtcNow
                : cleanEntry.LastSeenLiveAt;
        }

        if (cleanEntry.LastSeenLiveAt == default || cleanEntry.LastSeenLiveAt < cleanEntry.FirstSeenLiveAt)
        {
            cleanEntry.LastSeenLiveAt = cleanEntry.FirstSeenLiveAt;
        }

        if (liveHistory.TryGetValue(key, out var existing))
        {
            existing.DisplayName = string.IsNullOrWhiteSpace(cleanEntry.DisplayName) ? existing.DisplayName : cleanEntry.DisplayName;
            existing.TwitchUrl = string.IsNullOrWhiteSpace(cleanEntry.TwitchUrl) ? existing.TwitchUrl : cleanEntry.TwitchUrl;
            existing.RelayVersion = string.IsNullOrWhiteSpace(cleanEntry.RelayVersion) ? existing.RelayVersion : cleanEntry.RelayVersion;
            existing.BuildChannel = string.IsNullOrWhiteSpace(cleanEntry.BuildChannel) ? existing.BuildChannel : cleanEntry.BuildChannel;
            existing.FirstSeenLiveAt = cleanEntry.FirstSeenLiveAt < existing.FirstSeenLiveAt
                ? cleanEntry.FirstSeenLiveAt
                : existing.FirstSeenLiveAt;
            existing.LastSeenLiveAt = cleanEntry.LastSeenLiveAt > existing.LastSeenLiveAt
                ? cleanEntry.LastSeenLiveAt
                : existing.LastSeenLiveAt;
            return;
        }

        liveHistory[key] = cleanEntry;
    }

    private void PruneLiveHistory(DateTimeOffset now)
    {
        var cutoff = now.ToUniversalTime() - HistoryWindow;
        var staleKeys = liveHistory
            .Where(pair => pair.Value.LastSeenLiveAt.ToUniversalTime() < cutoff)
            .Select(pair => pair.Key)
            .ToList();
        foreach (var key in staleKeys)
        {
            liveHistory.Remove(key);
        }
    }

    private void RefreshLiveHistoryView()
    {
        HistoryEntries.Clear();
        foreach (var entry in liveHistory.Values
            .OrderByDescending(entry => entry.LastSeenLiveAt)
            .ThenBy(entry => entry.DisplayName, StringComparer.OrdinalIgnoreCase))
        {
            HistoryEntries.Add(new LiveHistoryEntryViewModel(entry));
        }

        HistoryStatusText = HistoryEntries.Count == 1
            ? "1 streamer observed live in the last 24 hours."
            : $"{HistoryEntries.Count} streamers observed live in the last 24 hours.";
        RaiseHistoryViewPropertiesChanged();
    }

    private void SaveLiveHistory()
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

            var store = new LiveHistoryStore
            {
                UpdatedAt = DateTimeOffset.UtcNow,
                Entries = liveHistory.Values
                    .OrderByDescending(entry => entry.LastSeenLiveAt)
                    .ThenBy(entry => entry.DisplayName, StringComparer.OrdinalIgnoreCase)
                    .ToList()
            };
            var writeOptions = new JsonSerializerOptions(JsonOptions)
            {
                WriteIndented = true
            };
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
                {
                    File.Delete(tempPath);
                }
            }
            catch
            {
                // A failed cleanup should not interrupt live-list refreshes.
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

    private static string NormalizeLiveUserKey(string? twitchUrl, string? displayName)
    {
        if (TryGetTwitchChannelSlug(twitchUrl, out var channelSlug))
        {
            return $"https://www.twitch.tv/{channelSlug.ToLowerInvariant()}";
        }

        if (!string.IsNullOrWhiteSpace(twitchUrl))
        {
            return twitchUrl.Trim();
        }

        return displayName?.Trim() ?? string.Empty;
    }

    private static string BuildTwitchVodUrl(string? twitchUrl)
    {
        return TryGetTwitchChannelSlug(twitchUrl, out var channelSlug)
            ? $"https://www.twitch.tv/{channelSlug}/videos?filter=archives&sort=time"
            : twitchUrl ?? string.Empty;
    }

    private static bool TryGetTwitchChannelSlug(string? twitchUrl, out string channelSlug)
    {
        channelSlug = string.Empty;
        if (string.IsNullOrWhiteSpace(twitchUrl)
            || !Uri.TryCreate(twitchUrl.Trim(), UriKind.Absolute, out var uri)
            || (uri.Scheme != Uri.UriSchemeHttps && uri.Scheme != Uri.UriSchemeHttp)
            || !IsTwitchHost(uri.Host))
        {
            return false;
        }

        var slug = uri.AbsolutePath
            .Split('/', StringSplitOptions.RemoveEmptyEntries)
            .FirstOrDefault();
        if (string.IsNullOrWhiteSpace(slug) || !IsTwitchChannelSlug(slug))
        {
            return false;
        }

        channelSlug = slug;
        return true;
    }

    private static bool IsTwitchHost(string host)
    {
        return host.Equals("twitch.tv", StringComparison.OrdinalIgnoreCase)
            || host.EndsWith(".twitch.tv", StringComparison.OrdinalIgnoreCase);
    }

    private static bool IsTwitchChannelSlug(string slug)
    {
        return slug.All(character => char.IsLetterOrDigit(character) || character == '_');
    }

    private void PlayLiveSoundAlert()
    {
        var alertSoundPath = ResolveLiveAlertSoundPath();
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
                if (player is null)
                {
                    return;
                }

                player.MediaEnded -= OnMediaEnded;
                player.MediaFailed -= OnMediaFailed;
                player.Close();
                activeAlertPlayers.Remove(player);
                player = null;
            }

            void OnMediaEnded(object? sender, EventArgs e)
            {
                Cleanup();
            }

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
            // System sounds can be unavailable on some Windows audio setups.
        }
    }

    private string ResolveLiveAlertSoundPath()
    {
        foreach (var (path, config) in ReadConfigCandidates())
        {
            var baseDirectory = Path.GetDirectoryName(path) ?? AppContext.BaseDirectory;
            var alertSoundPath = ResolveConfiguredPath(config.LiveAlertSoundPath, baseDirectory);
            if (!string.IsNullOrWhiteSpace(alertSoundPath))
            {
                return alertSoundPath;
            }
        }

        return string.Empty;
    }

    private Uri? ResolveLiveApiEndpoint()
    {
        foreach (var (_, config) in ReadConfigCandidates())
        {
            var endpoint = !string.IsNullOrWhiteSpace(config.LiveApiEndpoint)
                ? config.LiveApiEndpoint
                : config.LiveFeedbackHeartbeatEndpoint;
            if (BuildLiveApiUri(endpoint ?? string.Empty) is { } uri)
            {
                return uri;
            }
        }

        return null;
    }

    private static IEnumerable<string> GetConfigCandidates()
    {
        yield return Path.Combine(AppContext.BaseDirectory, "live-list.local.json");
        yield return Path.Combine(Environment.CurrentDirectory, "live-list.local.json");
        yield return Path.GetFullPath(Path.Combine(AppContext.BaseDirectory, "..", "..", "..", "live-list.local.json"));
        yield return Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
            "CrystalRelay",
            "bridge.runtime.json");
    }

    private static IEnumerable<(string Path, LiveListConfig Config)> ReadConfigCandidates()
    {
        foreach (var path in GetConfigCandidates())
        {
            if (TryReadConfig(path) is { } config)
            {
                yield return (path, config);
            }
        }
    }

    private static LiveListConfig? TryReadConfig(string path)
    {
        if (!File.Exists(path))
        {
            return null;
        }

        try
        {
            var json = File.ReadAllText(path);
            return JsonSerializer.Deserialize<LiveListConfig>(json, JsonOptions);
        }
        catch
        {
            return null;
        }
    }

    private static string ResolveConfiguredPath(string configuredPath, string baseDirectory)
    {
        if (string.IsNullOrWhiteSpace(configuredPath))
        {
            return string.Empty;
        }

        try
        {
            var expandedPath = Environment.ExpandEnvironmentVariables(configuredPath.Trim());
            return Path.IsPathRooted(expandedPath)
                ? expandedPath
                : Path.GetFullPath(Path.Combine(baseDirectory, expandedPath));
        }
        catch (Exception ex) when (ex is ArgumentException or IOException or NotSupportedException)
        {
            return string.Empty;
        }
    }

    private static Uri? BuildLiveApiUri(string endpoint)
    {
        if (string.IsNullOrWhiteSpace(endpoint)
            || !Uri.TryCreate(endpoint.Trim(), UriKind.Absolute, out var uri)
            || uri.UserInfo.Length > 0
            || (uri.Scheme != Uri.UriSchemeHttps && uri.Scheme != Uri.UriSchemeHttp))
        {
            return null;
        }

        var builder = new UriBuilder(uri)
        {
            Query = string.Empty,
            Fragment = string.Empty
        };

        var path = builder.Path.TrimEnd('/');
        if (path.EndsWith("/api/live", StringComparison.OrdinalIgnoreCase))
        {
            builder.Path = path;
        }
        else if (path.EndsWith("/api/ping", StringComparison.OrdinalIgnoreCase))
        {
            builder.Path = string.Concat(path.AsSpan(0, path.Length - "/api/ping".Length), "/api/live");
        }
        else
        {
            builder.Path = string.IsNullOrWhiteSpace(path) || path == "/"
                ? "/api/live"
                : $"{path}/api/live";
        }

        return builder.Uri;
    }

    private void OnRefreshClicked(object sender, RoutedEventArgs e)
    {
        _ = RefreshAsync();
    }

    private void OnShowLiveNowClicked(object sender, RoutedEventArgs e)
    {
        SetHistoryView(false);
    }

    private void OnShowHistoryClicked(object sender, RoutedEventArgs e)
    {
        SetHistoryView(true);
    }

    private void SetHistoryView(bool showHistory)
    {
        if (isShowingHistory == showHistory)
        {
            return;
        }

        isShowingHistory = showHistory;
        RaiseViewModePropertiesChanged();
    }

    private void OnOpenStreamClicked(object sender, RoutedEventArgs e)
    {
        if (sender is not Button { Tag: string twitchUrl } || string.IsNullOrWhiteSpace(twitchUrl))
        {
            return;
        }

        OpenTwitchUrl(twitchUrl, "Could not open the Twitch stream link.");
    }

    private void OnOpenVodsClicked(object sender, RoutedEventArgs e)
    {
        if (sender is not Button { Tag: string twitchUrl } || string.IsNullOrWhiteSpace(twitchUrl))
        {
            return;
        }

        OpenTwitchUrl(twitchUrl, "Could not open the Twitch VOD link.");
    }

    private void OpenTwitchUrl(string twitchUrl, string failureStatusText)
    {
        try
        {
            Process.Start(new ProcessStartInfo(twitchUrl)
            {
                UseShellExecute = true
            });
        }
        catch
        {
            StatusText = failureStatusText;
        }
    }

    private void OnCopyDevCommandClicked(object sender, RoutedEventArgs e)
    {
        if (sender is not Button { Tag: string commandKind })
        {
            return;
        }

        var command = commandKind switch
        {
            "grow" => BuildGrowCommand("grow"),
            "shrink" => BuildGrowCommand("shrink"),
            "scalerandom" => string.Format(
                CultureInfo.InvariantCulture,
                "!screm scalerandom {0:0.###}-{1:0.###} {2}",
                ClampReadDouble(ScaleRandomMinBox, 0.1, 5.0, 0.8),
                ClampReadDouble(ScaleRandomMaxBox, 0.1, 5.0, 2.0),
                ClampReadInt(ScaleRandomSecondsBox, 1, 300, 20)),
            "move" => string.Format(
                CultureInfo.InvariantCulture,
                "!screm move {0} {1}",
                GetSelectedMoveDirection(),
                ClampReadInt(MoveSecondsBox, 1, 60, 5)),
            "moverandom" => string.Format(
                CultureInfo.InvariantCulture,
                "!screm moverandom {0}",
                ClampReadInt(MoveRandomSecondsBox, 1, 120, 12)),
            "firesale" => string.Format(
                CultureInfo.InvariantCulture,
                "!screm firesale {0} {1}",
                ClampReadInt(FireSalePercentBox, 1, 100, 25),
                ClampReadInt(FireSaleSecondsBox, 1, 3600, 120)),
            _ => string.Empty
        };

        if (string.IsNullOrWhiteSpace(command))
        {
            return;
        }

        try
        {
            Clipboard.SetText(command);
            CommandCopyStatus = $"Copied: {command}";
        }
        catch (Exception ex)
        {
            CommandCopyStatus = $"Could not copy command: {ex.Message}";
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

    private string BuildGrowCommand(string commandName)
    {
        return string.Format(
            CultureInfo.InvariantCulture,
            "!screm {0} {1:0.###} {2} {3:0.###}",
            commandName,
            ClampReadDouble(GrowMetersBox, 0.001, 5.0, 0.25),
            ClampReadInt(GrowSecondsBox, 1, 300, 30),
            ClampReadDouble(GrowTransitionBox, 0, 30, 1));
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

    private string GetSelectedMoveDirection()
    {
        return MoveDirectionBox.SelectedItem is ComboBoxItem item
            && item.Content is string direction
            && !string.IsNullOrWhiteSpace(direction)
                ? direction.Trim()
                : "forward";
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

    private void OnTitleBarMouseDown(object sender, MouseButtonEventArgs e)
    {
        if (e.ChangedButton != MouseButton.Left)
        {
            return;
        }

        if (e.ClickCount == 2)
        {
            WindowState = WindowState == WindowState.Maximized ? WindowState.Normal : WindowState.Maximized;
            return;
        }

        DragMove();
    }

    private void OnMinimizeClicked(object sender, RoutedEventArgs e)
    {
        WindowState = WindowState.Minimized;
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
        {
            return IntPtr.Zero;
        }

        var screenPoint = GetScreenPoint(lParam);
        var windowPoint = PointFromScreen(screenPoint);
        var hitTest = GetResizeHitTest(windowPoint);
        if (hitTest == 0)
        {
            return IntPtr.Zero;
        }

        handled = true;
        return new IntPtr(hitTest);
    }

    private int GetResizeHitTest(Point point)
    {
        if (ActualWidth <= 0 || ActualHeight <= 0)
        {
            return 0;
        }

        var nearLeft = point.X >= 0 && point.X <= ResizeHitTestThickness;
        var nearRight = point.X >= ActualWidth - ResizeHitTestThickness && point.X <= ActualWidth;
        var nearTop = point.Y >= 0 && point.Y <= ResizeHitTestThickness;
        var nearBottom = point.Y >= ActualHeight - ResizeHitTestThickness && point.Y <= ActualHeight;

        if (nearTop && nearLeft)
        {
            return HtTopLeft;
        }

        if (nearTop && nearRight)
        {
            return HtTopRight;
        }

        if (nearBottom && nearLeft)
        {
            return HtBottomLeft;
        }

        if (nearBottom && nearRight)
        {
            return HtBottomRight;
        }

        if (nearLeft)
        {
            return HtLeft;
        }

        if (nearRight)
        {
            return HtRight;
        }

        if (nearTop)
        {
            return HtTop;
        }

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
        {
            return false;
        }

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
        RaisePropertyChanged(nameof(IsLiveEmptyVisible));
        RaisePropertyChanged(nameof(IsHistoryEmptyVisible));
        RaisePropertyChanged(nameof(ViewTitleText));
        RaisePropertyChanged(nameof(ViewPrimaryStatusText));
        RaisePropertyChanged(nameof(ViewSecondaryStatusText));
    }

    private sealed class LiveListConfig
    {
        public string LiveFeedbackHeartbeatEndpoint { get; set; } = string.Empty;

        public string LiveApiEndpoint { get; set; } = string.Empty;

        public string LiveAlertSoundPath { get; set; } = string.Empty;
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

    private sealed class LiveHistoryStore
    {
        public DateTimeOffset UpdatedAt { get; set; }

        public List<LiveHistoryEntryRecord> Entries { get; set; } = [];
    }

    public sealed class LiveHistoryEntryRecord
    {
        public string Key { get; set; } = string.Empty;

        public string DisplayName { get; set; } = string.Empty;

        public string TwitchUrl { get; set; } = string.Empty;

        public string RelayVersion { get; set; } = string.Empty;

        public string BuildChannel { get; set; } = string.Empty;

        public DateTimeOffset FirstSeenLiveAt { get; set; }

        public DateTimeOffset LastSeenLiveAt { get; set; }
    }

    public sealed class LiveUserViewModel
    {
        public LiveUserViewModel(
            string displayName,
            string twitchUrl,
            string relayVersion,
            string buildChannel,
            DateTimeOffset? lastPingAt)
        {
            DisplayName = displayName.Trim();
            TwitchUrl = twitchUrl.Trim();
            RelayVersion = relayVersion.Trim();
            BuildChannel = buildChannel.Trim();
            LastPingAt = lastPingAt?.ToUniversalTime();

            var details = new List<string>();
            if (!string.IsNullOrWhiteSpace(RelayVersion))
            {
                details.Add($"Crystal Relay {RelayVersion}");
            }

            if (!string.IsNullOrWhiteSpace(BuildChannel))
            {
                details.Add(BuildChannel);
            }

            if (LastPingAt is { } lastPing)
            {
                details.Add($"Last heartbeat {lastPing.ToLocalTime():g}");
            }

            DetailText = details.Count > 0 ? string.Join(" | ", details) : "Live heartbeat active.";
        }

        public string DisplayName { get; }

        public string TwitchUrl { get; }

        public string RelayVersion { get; }

        public string BuildChannel { get; }

        public DateTimeOffset? LastPingAt { get; }

        public string DetailText { get; }
    }

    public sealed class LiveHistoryEntryViewModel
    {
        internal LiveHistoryEntryViewModel(LiveHistoryEntryRecord entry)
        {
            DisplayName = string.IsNullOrWhiteSpace(entry.DisplayName)
                ? "Unknown streamer"
                : entry.DisplayName.Trim();
            TwitchUrl = entry.TwitchUrl?.Trim() ?? string.Empty;
            RelayVersion = entry.RelayVersion?.Trim() ?? string.Empty;
            BuildChannel = entry.BuildChannel?.Trim() ?? string.Empty;
            FirstSeenLiveAt = entry.FirstSeenLiveAt.ToLocalTime();
            LastSeenLiveAt = entry.LastSeenLiveAt.ToLocalTime();
            VodUrl = BuildTwitchVodUrl(TwitchUrl);

            var details = new List<string>();
            if (!string.IsNullOrWhiteSpace(RelayVersion))
            {
                details.Add($"Crystal Relay {RelayVersion}");
            }

            if (!string.IsNullOrWhiteSpace(BuildChannel))
            {
                details.Add(BuildChannel);
            }

            details.Add($"First seen live {FirstSeenLiveAt:g}");
            details.Add($"Last heartbeat {LastSeenLiveAt:g}");
            DetailText = string.Join(Environment.NewLine, details);
        }

        public string DisplayName { get; }

        public string TwitchUrl { get; }

        public string VodUrl { get; }

        public string RelayVersion { get; }

        public string BuildChannel { get; }

        public DateTimeOffset FirstSeenLiveAt { get; }

        public DateTimeOffset LastSeenLiveAt { get; }

        public string DetailText { get; }
    }
}
