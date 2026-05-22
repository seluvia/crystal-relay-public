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
using System.Windows.Threading;

namespace CrystalRelayLiveList;

public partial class MainWindow : Window, INotifyPropertyChanged
{
    private static readonly TimeSpan RefreshInterval = TimeSpan.FromMinutes(5);
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

    private readonly HttpClient httpClient = new()
    {
        Timeout = RequestTimeout
    };
    private readonly DispatcherTimer refreshTimer = new()
    {
        Interval = RefreshInterval
    };

    private string statusText = "Loading live users...";
    private string endpointText = "Endpoint not loaded yet.";
    private string lastUpdatedText = "Not refreshed yet.";
    private string commandCopyStatus = "No command copied yet.";
    private bool canRefresh = true;
    private bool soundAlertsEnabled = true;
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
            refreshTimer.Start();
            await RefreshAsync();
        };
        Closed += (_, _) =>
        {
            hwndSource?.RemoveHook(WndProc);
            hwndSource = null;
            refreshTimer.Stop();
            httpClient.Dispose();
        };
    }

    public event PropertyChangedEventHandler? PropertyChanged;

    public ObservableCollection<LiveUserViewModel> Users { get; } = [];

    public string StatusText
    {
        get => statusText;
        private set => SetProperty(ref statusText, value);
    }

    public string EndpointText
    {
        get => endpointText;
        private set => SetProperty(ref endpointText, value);
    }

    public string LastUpdatedText
    {
        get => lastUpdatedText;
        private set => SetProperty(ref lastUpdatedText, value);
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
                RaisePropertyChanged(nameof(IsEmpty));
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

            RaisePropertyChanged(nameof(IsEmpty));
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
        return !string.IsNullOrWhiteSpace(user.TwitchUrl)
            ? user.TwitchUrl.Trim()
            : user.DisplayName.Trim();
    }

    private static void PlayLiveSoundAlert()
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

    private Uri? ResolveLiveApiEndpoint()
    {
        foreach (var path in GetConfigCandidates())
        {
            var endpoint = TryReadEndpoint(path);
            if (BuildLiveApiUri(endpoint) is { } uri)
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

    private static string TryReadEndpoint(string path)
    {
        if (!File.Exists(path))
        {
            return string.Empty;
        }

        try
        {
            var json = File.ReadAllText(path);
            var config = JsonSerializer.Deserialize<LiveListConfig>(json, JsonOptions);
            return !string.IsNullOrWhiteSpace(config?.LiveApiEndpoint)
                ? config.LiveApiEndpoint
                : config?.LiveFeedbackHeartbeatEndpoint ?? string.Empty;
        }
        catch
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

    private void OnOpenStreamClicked(object sender, RoutedEventArgs e)
    {
        if (sender is not Button { Tag: string twitchUrl } || string.IsNullOrWhiteSpace(twitchUrl))
        {
            return;
        }

        try
        {
            Process.Start(new ProcessStartInfo(twitchUrl)
            {
                UseShellExecute = true
            });
        }
        catch
        {
            StatusText = "Could not open the Twitch stream link.";
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

    private sealed class LiveListConfig
    {
        public string LiveFeedbackHeartbeatEndpoint { get; set; } = string.Empty;

        public string LiveApiEndpoint { get; set; } = string.Empty;
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

            var details = new List<string>();
            if (!string.IsNullOrWhiteSpace(relayVersion))
            {
                details.Add($"Crystal Relay {relayVersion.Trim()}");
            }

            if (!string.IsNullOrWhiteSpace(buildChannel))
            {
                details.Add(buildChannel.Trim());
            }

            if (lastPingAt is { } lastPing)
            {
                details.Add($"Last heartbeat {lastPing.ToLocalTime():g}");
            }

            DetailText = details.Count > 0 ? string.Join(" | ", details) : "Live heartbeat active.";
        }

        public string DisplayName { get; }

        public string TwitchUrl { get; }

        public string DetailText { get; }
    }
}
