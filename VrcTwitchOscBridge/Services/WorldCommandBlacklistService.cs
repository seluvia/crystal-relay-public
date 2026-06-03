using System.Globalization;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
using System.Text.Json.Serialization;
using VrcTwitchOscBridge.Models;

namespace VrcTwitchOscBridge.Services;

internal enum WorldCommandBlacklistRefreshStatus
{
    Disabled,
    Ready,
    RequestFailed,
    InvalidResponse
}

internal sealed record WorldCommandBlacklistRefreshResult(
    WorldCommandBlacklistRefreshStatus Status,
    int WorldEntryCount = 0,
    int CreatorEntryCount = 0,
    DateTimeOffset? UpdatedAtUtc = null);

internal sealed record WorldCommandBlacklistDecision(bool IsBlocked, bool IsFailClosed, string Reason = "")
{
    public static WorldCommandBlacklistDecision Allow { get; } = new(false, false);

    public static WorldCommandBlacklistDecision Block(bool isFailClosed = false, string reason = "") =>
        new(true, isFailClosed, reason.Trim());
}

internal sealed class WorldCommandBlacklistService : IDisposable
{
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNameCaseInsensitive = true
    };

    private static readonly Uri CheckEndpoint = new(WorldCommandBlacklistSettings.DefaultWorkerCheckEndpoint);
    private static readonly Uri StatusEndpoint = new(WorldCommandBlacklistSettings.DefaultWorkerStatusEndpoint);
    private static readonly TimeSpan DefaultRequestTimeout = TimeSpan.FromSeconds(6);

    private readonly object stateGate = new();
    private readonly HttpClient httpClient = new()
    {
        Timeout = DefaultRequestTimeout
    };

    private bool isEnabled;

    public WorldCommandBlacklistService()
    {
        httpClient.DefaultRequestHeaders.Accept.Add(new MediaTypeWithQualityHeaderValue("application/json"));
        httpClient.DefaultRequestHeaders.UserAgent.ParseAdd("CrystalRelay-DesktopApp");
    }

    public void Configure(WorldCommandBlacklistSettings settings)
    {
        lock (stateGate)
        {
            isEnabled = settings.IsEnabled;
        }
    }

    public void InvalidateCurrentSessionList()
    {
        // The Cloudflare guard is checked server-side for each world command.
        // There is no local list to invalidate.
    }

    public async Task<WorldCommandBlacklistDecision> EvaluateAsync(
        string worldId,
        string authorId,
        CancellationToken cancellationToken = default)
    {
        lock (stateGate)
        {
            if (!isEnabled)
            {
                return WorldCommandBlacklistDecision.Allow;
            }
        }

        if (string.IsNullOrWhiteSpace(worldId))
        {
            return WorldCommandBlacklistDecision.Allow;
        }

        try
        {
            using var response = await httpClient.PostAsJsonAsync(
                CheckEndpoint,
                new WorldGuardCheckRequest(
                    worldId.Trim(),
                    (authorId ?? string.Empty).Trim(),
                    DateOnly.FromDateTime(DateTime.Now).ToString("MM/dd/yyyy", CultureInfo.InvariantCulture)),
                JsonOptions,
                cancellationToken);

            if (!response.IsSuccessStatusCode)
            {
                return WorldCommandBlacklistDecision.Block(isFailClosed: true);
            }

            var payload = await response.Content.ReadFromJsonAsync<WorldGuardCheckResponse>(JsonOptions, cancellationToken);
            return payload is null
                ? WorldCommandBlacklistDecision.Block(isFailClosed: true)
                : payload.Blocked
                    ? WorldCommandBlacklistDecision.Block(reason: payload.Reason ?? string.Empty)
                    : WorldCommandBlacklistDecision.Allow;
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            throw;
        }
        catch
        {
            return WorldCommandBlacklistDecision.Block(isFailClosed: true);
        }
    }

    public async Task<WorldCommandBlacklistRefreshResult> RefreshAsync(
        WorldCommandBlacklistSettings settings,
        bool force,
        CancellationToken cancellationToken = default)
    {
        Configure(settings);

        try
        {
            using var request = new HttpRequestMessage(HttpMethod.Get, StatusEndpoint);
            using var response = await httpClient.SendAsync(request, cancellationToken);
            if (!response.IsSuccessStatusCode)
            {
                return new WorldCommandBlacklistRefreshResult(WorldCommandBlacklistRefreshStatus.RequestFailed);
            }

            var payload = await response.Content.ReadFromJsonAsync<WorldGuardStatusResponse>(JsonOptions, cancellationToken);
            if (payload is null || !payload.Ok)
            {
                return new WorldCommandBlacklistRefreshResult(WorldCommandBlacklistRefreshStatus.InvalidResponse);
            }

            return new WorldCommandBlacklistRefreshResult(
                WorldCommandBlacklistRefreshStatus.Ready,
                Math.Max(0, payload.WorldEntryCount),
                Math.Max(0, payload.CreatorEntryCount),
                ParseUpdatedAt(payload.UpdatedAt));
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            throw;
        }
        catch
        {
            return new WorldCommandBlacklistRefreshResult(WorldCommandBlacklistRefreshStatus.RequestFailed);
        }
    }

    public void Dispose() => httpClient.Dispose();

    private static DateTimeOffset? ParseUpdatedAt(string? value)
    {
        return DateTimeOffset.TryParse(
            value,
            CultureInfo.InvariantCulture,
            DateTimeStyles.AssumeUniversal | DateTimeStyles.AdjustToUniversal,
            out var updatedAt)
            ? updatedAt
            : null;
    }

    private sealed record WorldGuardCheckRequest(
        [property: JsonPropertyName("worldId")] string WorldId,
        [property: JsonPropertyName("authorId")] string AuthorId,
        [property: JsonPropertyName("localDate")] string LocalDate);

    private sealed class WorldGuardCheckResponse
    {
        [JsonPropertyName("blocked")]
        public bool Blocked { get; set; }

        [JsonPropertyName("reason")]
        public string? Reason { get; set; }
    }

    private sealed class WorldGuardStatusResponse
    {
        [JsonPropertyName("ok")]
        public bool Ok { get; set; }

        [JsonPropertyName("worldEntryCount")]
        public int WorldEntryCount { get; set; }

        [JsonPropertyName("creatorEntryCount")]
        public int CreatorEntryCount { get; set; }

        [JsonPropertyName("updatedAt")]
        public string? UpdatedAt { get; set; }
    }
}
