using System.Diagnostics;
using System.Net.Http;
using System.Net.Http.Json;
using System.Text.Json;
using System.Text.RegularExpressions;

namespace VrcTwitchOscBridge.Services;

internal sealed class LiveFeedbackHeartbeatService : IDisposable
{
    private static readonly TimeSpan OnlineRefreshInterval = TimeSpan.FromHours(1);
    private static readonly TimeSpan FirstRetryDelay = TimeSpan.FromMinutes(5);
    private static readonly TimeSpan FailedRefreshRetryDelay = TimeSpan.FromHours(1);
    private static readonly TimeSpan TimerInterval = TimeSpan.FromMinutes(1);
    private static readonly TimeSpan RequestTimeout = TimeSpan.FromSeconds(10);
    private static readonly TimeSpan ShutdownRequestTimeout = TimeSpan.FromSeconds(3);
    private static readonly TimeSpan FailureLogThrottle = TimeSpan.FromMinutes(15);
    private static readonly Regex TwitchLoginPattern = new("^[A-Za-z0-9_]{3,30}$", RegexOptions.Compiled | RegexOptions.CultureInvariant);
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web);

    private readonly object stateGate = new();
    private readonly SemaphoreSlim evaluationGate = new(1, 1);
    private readonly HttpClient httpClient = new()
    {
        Timeout = RequestTimeout
    };
    private readonly Timer refreshTimer;

    private HeartbeatState currentState = HeartbeatState.Empty;
    private HeartbeatState lastActiveState = HeartbeatState.Empty;
    private DateTimeOffset lastSuccessfulOnlineHeartbeatAt = DateTimeOffset.MinValue;
    private DateTimeOffset nextOnlineAttemptAt = DateTimeOffset.MinValue;
    private DateTimeOffset lastFailureLogAt = DateTimeOffset.MinValue;
    private bool heartbeatActive;
    private int consecutiveOnlineFailures;
    private bool disposed;

    public LiveFeedbackHeartbeatService()
    {
        refreshTimer = new Timer(_ => _ = EvaluateAsync(), null, TimerInterval, TimerInterval);
    }

    public event Action<string>? DiagnosticLogged;

    public void UpdateState(
        bool enabled,
        bool hasBroadcaster,
        bool isLive,
        string displayName,
        string twitchLogin,
        string endpoint,
        string relayVersion,
        string buildChannel)
    {
        lock (stateGate)
        {
            currentState = new HeartbeatState(
                enabled,
                hasBroadcaster,
                isLive,
                displayName?.Trim() ?? string.Empty,
                twitchLogin?.Trim() ?? string.Empty,
                endpoint?.Trim() ?? string.Empty,
                relayVersion?.Trim() ?? string.Empty,
                buildChannel?.Trim() ?? string.Empty);
        }

        _ = EvaluateAsync();
    }

    public async Task StopAsync()
    {
        await evaluationGate.WaitAsync().ConfigureAwait(false);
        try
        {
            var offlineState = lastActiveState.HasTwitchIdentity ? lastActiveState : GetCurrentState();
            if (heartbeatActive)
            {
                await SendOfflineAsync(offlineState, ShutdownRequestTimeout).ConfigureAwait(false);
            }

            MarkInactive();
        }
        finally
        {
            evaluationGate.Release();
        }
    }

    public void Dispose()
    {
        disposed = true;
        refreshTimer.Dispose();
        httpClient.Dispose();
        evaluationGate.Dispose();
    }

    private async Task EvaluateAsync()
    {
        if (disposed)
        {
            return;
        }

        await evaluationGate.WaitAsync().ConfigureAwait(false);
        try
        {
            if (disposed)
            {
                return;
            }

            var state = GetCurrentState();
            if (!CanSendOnline(state))
            {
                if (heartbeatActive)
                {
                    var offlineState = lastActiveState.HasTwitchIdentity ? lastActiveState : state;
                    await SendOfflineAsync(offlineState, RequestTimeout).ConfigureAwait(false);
                    MarkInactive();
                }

                return;
            }

            await EnsureOnlineAsync(state).ConfigureAwait(false);
        }
        finally
        {
            evaluationGate.Release();
        }
    }

    private async Task EnsureOnlineAsync(HeartbeatState state)
    {
        var now = DateTimeOffset.UtcNow;
        if (now < nextOnlineAttemptAt)
        {
            return;
        }

        if (heartbeatActive && now - lastSuccessfulOnlineHeartbeatAt < OnlineRefreshInterval)
        {
            return;
        }

        var sent = await SendHeartbeatAsync(state, isLive: true, RequestTimeout).ConfigureAwait(false);
        if (sent)
        {
            heartbeatActive = true;
            lastActiveState = state;
            lastSuccessfulOnlineHeartbeatAt = now;
            nextOnlineAttemptAt = now.Add(OnlineRefreshInterval);
            consecutiveOnlineFailures = 0;
            return;
        }

        consecutiveOnlineFailures += 1;
        nextOnlineAttemptAt = now.Add(!heartbeatActive && consecutiveOnlineFailures == 1
            ? FirstRetryDelay
            : FailedRefreshRetryDelay);
    }

    private async Task SendOfflineAsync(HeartbeatState state, TimeSpan timeout)
    {
        _ = await SendHeartbeatAsync(state, isLive: false, timeout).ConfigureAwait(false);
    }

    private async Task<bool> SendHeartbeatAsync(HeartbeatState state, bool isLive, TimeSpan timeout)
    {
        var pingUri = BuildPingUri(state.Endpoint);
        var twitchUrl = BuildTwitchUrl(state.TwitchLogin);
        if (pingUri is null || string.IsNullOrWhiteSpace(twitchUrl))
        {
            return false;
        }

        var payload = new LiveFeedbackHeartbeatPayload(
            TrimForPayload(string.IsNullOrWhiteSpace(state.DisplayName) ? state.TwitchLogin : state.DisplayName, 80),
            twitchUrl,
            isLive,
            TrimForPayload(state.RelayVersion, 40),
            TrimForPayload(state.BuildChannel, 40));

        try
        {
            using var timeoutCancellation = new CancellationTokenSource(timeout);
            using var request = new HttpRequestMessage(HttpMethod.Post, pingUri)
            {
                Content = JsonContent.Create(payload, options: JsonOptions)
            };
            using var response = await httpClient.SendAsync(request, timeoutCancellation.Token).ConfigureAwait(false);
            if (response.IsSuccessStatusCode)
            {
                return true;
            }

            LogFailure($"Live Feedback Heartbeat returned HTTP {(int)response.StatusCode}.");
        }
        catch (Exception ex) when (ex is HttpRequestException or TaskCanceledException or JsonException or InvalidOperationException)
        {
            LogFailure($"Live Feedback Heartbeat could not send a {(isLive ? "live" : "offline")} ping: {ex.Message}");
        }

        return false;
    }

    private void LogFailure(string message)
    {
        Debug.WriteLine(message);
        var now = DateTimeOffset.UtcNow;
        if (now - lastFailureLogAt < FailureLogThrottle)
        {
            return;
        }

        lastFailureLogAt = now;
        DiagnosticLogged?.Invoke(message);
    }

    private HeartbeatState GetCurrentState()
    {
        lock (stateGate)
        {
            return currentState;
        }
    }

    private static bool CanSendOnline(HeartbeatState state)
    {
        return state.Enabled
            && state.HasBroadcaster
            && state.IsLive
            && BuildPingUri(state.Endpoint) is not null
            && !string.IsNullOrWhiteSpace(BuildTwitchUrl(state.TwitchLogin));
    }

    private void MarkInactive()
    {
        heartbeatActive = false;
        consecutiveOnlineFailures = 0;
        nextOnlineAttemptAt = DateTimeOffset.MinValue;
    }

    private static Uri? BuildPingUri(string endpoint)
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
        if (path.EndsWith("/api/ping", StringComparison.OrdinalIgnoreCase))
        {
            builder.Path = path;
        }
        else if (path.EndsWith("/api/live", StringComparison.OrdinalIgnoreCase))
        {
            builder.Path = string.Concat(path.AsSpan(0, path.Length - "/api/live".Length), "/api/ping");
        }
        else
        {
            builder.Path = string.IsNullOrWhiteSpace(path) || path == "/"
                ? "/api/ping"
                : $"{path}/api/ping";
        }

        return builder.Uri;
    }

    private static string BuildTwitchUrl(string twitchLogin)
    {
        var normalized = twitchLogin.Trim().TrimStart('@');
        return TwitchLoginPattern.IsMatch(normalized)
            ? $"https://www.twitch.tv/{normalized.ToLowerInvariant()}"
            : string.Empty;
    }

    private static string TrimForPayload(string value, int maxLength)
    {
        var normalized = value.Trim();
        return normalized.Length <= maxLength ? normalized : normalized[..maxLength];
    }

    private readonly record struct HeartbeatState(
        bool Enabled,
        bool HasBroadcaster,
        bool IsLive,
        string DisplayName,
        string TwitchLogin,
        string Endpoint,
        string RelayVersion,
        string BuildChannel)
    {
        public static HeartbeatState Empty { get; } = new(false, false, false, string.Empty, string.Empty, string.Empty, string.Empty, string.Empty);

        public bool HasTwitchIdentity => !string.IsNullOrWhiteSpace(TwitchLogin);
    }

    private sealed record LiveFeedbackHeartbeatPayload(
        string DisplayName,
        string TwitchUrl,
        bool IsLive,
        string RelayVersion,
        string BuildChannel);
}
