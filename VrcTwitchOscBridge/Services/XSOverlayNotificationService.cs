using System;
using System.Diagnostics;
using System.Net.WebSockets;
using System.Text;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using VrcTwitchOscBridge.Models;

namespace VrcTwitchOscBridge.Services;

public sealed class XSOverlayNotificationService : IDisposable
{
    private const string WebSocketUrl = "ws://localhost:42070/?client=CrystalRelay";
    private const float DefaultVolume = 0.7f;
    private const float DefaultOpacity = 1f;
    private const float DefaultHeight = 175f;
    private const string DefaultAudio = "default";
    private const string DefaultIcon = "default";
    private const string SourceApp = "Crystal Relay";

    private readonly XSOverlaySettings _settings;
    private ClientWebSocket? _webSocket;
    private CancellationTokenSource? _cts;
    private readonly DebounceContext _followDebounce;
    private readonly DebounceContext _raidDebounce;
    private readonly DebounceContext _giftSubDebounce;
    private bool _disposed;

    private sealed class DebounceContext
    {
        private readonly XSOverlayNotificationService _owner;
        private readonly Func<XSOverlaySettings, bool> _enabledGetter;
        private readonly Func<XSOverlaySettings, float> _windowGetter;
        private readonly Func<XSOverlaySettings, float> _timeoutGetter;
        private readonly Func<DebounceAccumulator, string> _messageBuilder;
        private Timer? _timer;
        private DebounceAccumulator? _accumulator;
        private readonly object _lock = new();

        public DebounceContext(
            XSOverlayNotificationService owner,
            Func<XSOverlaySettings, bool> enabledGetter,
            Func<XSOverlaySettings, float> windowGetter,
            Func<XSOverlaySettings, float> timeoutGetter,
            Func<DebounceAccumulator, string> messageBuilder)
        {
            _owner = owner;
            _enabledGetter = enabledGetter;
            _windowGetter = windowGetter;
            _timeoutGetter = timeoutGetter;
            _messageBuilder = messageBuilder;
        }

        public void Enqueue(Action<DebounceAccumulator> accumulate)
        {
            if (!_enabledGetter(_owner._settings))
                return;

            lock (_lock)
            {
                _accumulator ??= new DebounceAccumulator();
                accumulate(_accumulator);
                ResetTimer();
            }
        }

        private void ResetTimer()
        {
            _timer?.Dispose();
            var windowMs = (int)(_windowGetter(_owner._settings) * 1000);
            _timer = new Timer(OnTimerFired, null, windowMs, Timeout.Infinite);
        }

        private void OnTimerFired(object? state)
        {
            DebounceAccumulator? snapshot;
            lock (_lock)
            {
                snapshot = _accumulator;
                _accumulator = null;
                _timer?.Dispose();
                _timer = null;
            }

            if (snapshot is null)
                return;

            var timeout = _timeoutGetter(_owner._settings);
            var message = _messageBuilder(snapshot);
            _ = _owner.SendNotificationAsync(message, timeout);
        }
    }

    private sealed class DebounceAccumulator
    {
        public int TotalCount { get; set; }
        public string? FirstUserName { get; set; }
        public int UniqueUserCount { get; set; }
        public string? LatestUserName { get; set; }
        public int LatestViewerCount { get; set; }
        public string? LatestGifterName { get; set; }
        public int GiftSubTotal { get; set; }
        public string? Tier { get; set; }
    }

    public XSOverlayNotificationService(XSOverlaySettings settings)
    {
        _settings = settings;
        _followDebounce = new DebounceContext(this,
            s => s.EnableFollowNotifications,
            s => s.FollowDebounceWindow,
            s => s.FollowTimeout,
            acc => acc.TotalCount > 1
                ? $"{acc.FirstUserName} +{acc.TotalCount - 1} others followed"
                : $"{acc.FirstUserName} followed");

        _raidDebounce = new DebounceContext(this,
            s => s.EnableRaidNotifications,
            s => s.RaidDebounceWindow,
            s => s.RaidTimeout,
            acc => $"{acc.LatestUserName} raided with {acc.LatestViewerCount:N0} viewers");

        _giftSubDebounce = new DebounceContext(this,
            s => s.EnableGiftSubNotifications,
            s => s.GiftSubDebounceWindow,
            s => s.GiftSubTimeout,
            acc => $"{acc.LatestGifterName} gifted {acc.GiftSubTotal:N0} subs");
    }

    public void Start()
    {
        if (_cts is not null)
            return;
        _cts = new CancellationTokenSource();
        _ = ConnectAndRunAsync(_cts.Token);
    }

    public void Stop()
    {
        _cts?.Cancel();
        _cts?.Dispose();
        _cts = null;
    }

    private async Task ConnectAndRunAsync(CancellationToken ct)
    {
        var retryDelay = TimeSpan.FromSeconds(1);
        const int maxRetryDelaySeconds = 30;

        while (!ct.IsCancellationRequested)
        {
            try
            {
                _webSocket?.Dispose();
                _webSocket = new ClientWebSocket();
                await _webSocket.ConnectAsync(new Uri(WebSocketUrl), ct);
                retryDelay = TimeSpan.FromSeconds(1);

                var buffer = new byte[4096];
                while (_webSocket.State == WebSocketState.Open && !ct.IsCancellationRequested)
                {
                    var result = await _webSocket.ReceiveAsync(new ArraySegment<byte>(buffer), ct);
                    if (result.MessageType == WebSocketMessageType.Close)
                        break;
                }
            }
            catch (OperationCanceledException)
            {
                break;
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"[XSOverlay] Connection failed: {ex.Message}");
            }

            if (!ct.IsCancellationRequested)
            {
                try
                {
                    await Task.Delay(retryDelay, ct);
                    retryDelay = TimeSpan.FromSeconds(Math.Min(retryDelay.TotalSeconds * 2, maxRetryDelaySeconds));
                }
                catch (OperationCanceledException)
                {
                    break;
                }
            }
        }
    }

    private async Task SendNotificationAsync(string messageText, float timeout)
    {
        if (_webSocket?.State != WebSocketState.Open)
            return;

        try
        {
            var notification = new XSONotificationObject
            {
                messageType = 1,
                title = "Crystal Relay",
                content = messageText,
                timeout = timeout,
                height = DefaultHeight,
                opacity = DefaultOpacity,
                volume = DefaultVolume,
                audioPath = DefaultAudio,
                icon = DefaultIcon,
                useBase64Icon = false,
                sourceApp = SourceApp
            };

            var apiObject = new XSOApiObject
            {
                sender = "crystal_relay",
                target = "xsoverlay",
                command = "SendNotification",
                jsonData = JsonSerializer.Serialize(notification),
                rawData = null
            };

            var json = JsonSerializer.Serialize(apiObject);
            var bytes = Encoding.UTF8.GetBytes(json);
            await _webSocket.SendAsync(new ArraySegment<byte>(bytes), WebSocketMessageType.Text, true, CancellationToken.None);
        }
        catch (Exception ex)
        {
            Debug.WriteLine($"[XSOverlay] Send failed: {ex.Message}");
        }
    }

    public void QueueFollowEvent(string userName)
    {
        _followDebounce.Enqueue(acc =>
        {
            acc.TotalCount++;
            acc.FirstUserName ??= userName;
            acc.UniqueUserCount = acc.TotalCount;
        });
    }

    public void QueueRaidEvent(string broadcasterName, int viewerCount)
    {
        _raidDebounce.Enqueue(acc =>
        {
            acc.LatestUserName = broadcasterName;
            acc.LatestViewerCount = viewerCount;
        });
    }

    public void QueueGiftSubEvent(string gifterName, int amount, string tier)
    {
        _giftSubDebounce.Enqueue(acc =>
        {
            acc.LatestGifterName = gifterName;
            acc.GiftSubTotal += amount;
            acc.Tier = tier;
        });
    }

    public void Dispose()
    {
        if (_disposed)
            return;
        _disposed = true;
        Stop();
        _webSocket?.Dispose();
    }

    private struct XSONotificationObject
    {
        public int messageType { get; set; }
        public string title { get; set; }
        public string content { get; set; }
        public float timeout { get; set; }
        public float height { get; set; }
        public float opacity { get; set; }
        public float volume { get; set; }
        public string audioPath { get; set; }
        public string icon { get; set; }
        public bool useBase64Icon { get; set; }
        public string sourceApp { get; set; }
    }

    private struct XSOApiObject
    {
        public string sender { get; set; }
        public string target { get; set; }
        public string command { get; set; }
        public string? jsonData { get; set; }
        public string? rawData { get; set; }
    }
}
