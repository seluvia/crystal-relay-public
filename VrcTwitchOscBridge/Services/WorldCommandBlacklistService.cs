using System.Globalization;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
using System.Text.Json.Serialization;
using System.Text.RegularExpressions;
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

internal sealed record WorldCommandBlacklistDecision(bool IsBlocked, bool IsFailClosed)
{
    public static WorldCommandBlacklistDecision Allow { get; } = new(false, false);

    public static WorldCommandBlacklistDecision Block(bool isFailClosed = false) =>
        new(true, isFailClosed);
}

internal sealed class WorldCommandBlacklistService : IDisposable
{
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNameCaseInsensitive = false,
        AllowDuplicateProperties = false
    };

    private static readonly Uri CheckEndpoint = new(WorldCommandBlacklistSettings.DefaultWorkerCheckEndpoint);
    private static readonly Uri StatusEndpoint = new(WorldCommandBlacklistSettings.DefaultWorkerStatusEndpoint);
    private static readonly TimeSpan DefaultRequestTimeout = TimeSpan.FromSeconds(6);
    private static readonly Regex WorldIdPattern = new(
        @"\Awrld_[0-9a-f]{8}-[0-9a-f]{4}-[0-9a-f]{4}-[0-9a-f]{4}-[0-9a-f]{12}\z",
        RegexOptions.Compiled | RegexOptions.CultureInvariant | RegexOptions.IgnoreCase);
    private static readonly Regex AuthorIdPattern = new(
        @"\Ausr_[0-9a-f]{8}-[0-9a-f]{4}-[0-9a-f]{4}-[0-9a-f]{4}-[0-9a-f]{12}\z",
        RegexOptions.Compiled | RegexOptions.CultureInvariant | RegexOptions.IgnoreCase);
    private static readonly Regex UtcTimestampPattern = new(
        @"\A[0-9]{4}-[0-9]{2}-[0-9]{2}T[0-9]{2}:[0-9]{2}:[0-9]{2}(?:\.[0-9]{1,7})?(?:Z|\+00:00)\z",
        RegexOptions.Compiled | RegexOptions.CultureInvariant);
    private static readonly string[] UtcTimestampFormats =
    [
        "yyyy-MM-dd'T'HH:mm:ss'Z'",
        "yyyy-MM-dd'T'HH:mm:ss.FFFFFFF'Z'",
        "yyyy-MM-dd'T'HH:mm:sszzz",
        "yyyy-MM-dd'T'HH:mm:ss.FFFFFFFzzz"
    ];

    private readonly HttpClient httpClient;
    private readonly bool ownsHttpClient;

    public WorldCommandBlacklistService() : this(new HttpClient(), ownsHttpClient: true)
    {
    }

    internal WorldCommandBlacklistService(HttpClient httpClient) : this(httpClient, ownsHttpClient: false)
    {
    }

    private WorldCommandBlacklistService(HttpClient httpClient, bool ownsHttpClient)
    {
        this.httpClient = httpClient ?? throw new ArgumentNullException(nameof(httpClient));
        this.ownsHttpClient = ownsHttpClient;
        httpClient.Timeout = DefaultRequestTimeout;

        if (!httpClient.DefaultRequestHeaders.Accept.Any(value =>
                string.Equals(value.MediaType, "application/json", StringComparison.OrdinalIgnoreCase)))
        {
            httpClient.DefaultRequestHeaders.Accept.Add(
                new MediaTypeWithQualityHeaderValue("application/json"));
        }

        if (!httpClient.DefaultRequestHeaders.UserAgent.Any(value =>
                string.Equals(value.Product?.Name, "CrystalRelay-DesktopApp", StringComparison.OrdinalIgnoreCase)))
        {
            httpClient.DefaultRequestHeaders.UserAgent.ParseAdd("CrystalRelay-DesktopApp");
        }
    }

    public void Configure(WorldCommandBlacklistSettings settings) => _ = settings;

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
        cancellationToken.ThrowIfCancellationRequested();

        if (!WorldIdPattern.IsMatch(worldId ?? string.Empty)
            || !AuthorIdPattern.IsMatch(authorId ?? string.Empty))
        {
            return WorldCommandBlacklistDecision.Block(isFailClosed: true);
        }

        try
        {
            using var response = await httpClient.PostAsJsonAsync(
                CheckEndpoint,
                new WorldGuardCheckRequest(
                    worldId!.ToLowerInvariant(),
                    authorId!.ToLowerInvariant()),
                JsonOptions,
                cancellationToken);

            if (!response.IsSuccessStatusCode)
            {
                return ReturnUnlessCallerCancelled(
                    cancellationToken,
                    WorldCommandBlacklistDecision.Block(isFailClosed: true));
            }

            var payload = await response.Content.ReadFromJsonAsync<WorldGuardCheckResponse>(JsonOptions, cancellationToken);
            if (payload?.SchemaVersion != 2
                || payload.Blocked is not bool blocked
                || payload.AdditionalProperties is { Count: > 0 })
            {
                return ReturnUnlessCallerCancelled(
                    cancellationToken,
                    WorldCommandBlacklistDecision.Block(isFailClosed: true));
            }

            return ReturnUnlessCallerCancelled(
                cancellationToken,
                blocked
                    ? WorldCommandBlacklistDecision.Block()
                    : WorldCommandBlacklistDecision.Allow);
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            throw;
        }
        catch
        {
            return ReturnUnlessCallerCancelled(
                cancellationToken,
                WorldCommandBlacklistDecision.Block(isFailClosed: true));
        }
    }

    public async Task<WorldCommandBlacklistRefreshResult> RefreshAsync(
        WorldCommandBlacklistSettings settings,
        bool force,
        CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        Configure(settings);

        try
        {
            using var request = new HttpRequestMessage(HttpMethod.Get, StatusEndpoint);
            using var response = await httpClient.SendAsync(request, cancellationToken);
            if (!response.IsSuccessStatusCode)
            {
                return ReturnUnlessCallerCancelled(
                    cancellationToken,
                    new WorldCommandBlacklistRefreshResult(WorldCommandBlacklistRefreshStatus.RequestFailed));
            }

            WorldGuardStatusResponse? payload;
            try
            {
                payload = await response.Content.ReadFromJsonAsync<WorldGuardStatusResponse>(
                    JsonOptions,
                    cancellationToken);
            }
            catch (JsonException)
            {
                return ReturnUnlessCallerCancelled(
                    cancellationToken,
                    new WorldCommandBlacklistRefreshResult(WorldCommandBlacklistRefreshStatus.InvalidResponse));
            }
            catch (NotSupportedException)
            {
                return ReturnUnlessCallerCancelled(
                    cancellationToken,
                    new WorldCommandBlacklistRefreshResult(WorldCommandBlacklistRefreshStatus.InvalidResponse));
            }

            if (!TryValidateStatus(payload, out var updatedAtUtc))
            {
                return ReturnUnlessCallerCancelled(
                    cancellationToken,
                    new WorldCommandBlacklistRefreshResult(WorldCommandBlacklistRefreshStatus.InvalidResponse));
            }

            return ReturnUnlessCallerCancelled(
                cancellationToken,
                new WorldCommandBlacklistRefreshResult(
                    WorldCommandBlacklistRefreshStatus.Ready,
                    payload!.WorldEntryCount!.Value,
                    payload.CreatorEntryCount!.Value,
                    updatedAtUtc));
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            throw;
        }
        catch
        {
            return ReturnUnlessCallerCancelled(
                cancellationToken,
                new WorldCommandBlacklistRefreshResult(WorldCommandBlacklistRefreshStatus.RequestFailed));
        }
    }

    public void Dispose()
    {
        if (ownsHttpClient)
        {
            httpClient.Dispose();
        }
    }

    private static bool TryValidateStatus(
        WorldGuardStatusResponse? payload,
        out DateTimeOffset? updatedAtUtc)
    {
        updatedAtUtc = null;
        if (payload?.Ok != true
            || payload.AdditionalProperties is { Count: > 0 }
            || !payload.WorldEntryCountPresent
            || payload.WorldEntryCount is not int worldEntryCount
            || worldEntryCount < 0
            || !payload.CreatorEntryCountPresent
            || payload.CreatorEntryCount is not int creatorEntryCount
            || creatorEntryCount < 0)
        {
            return false;
        }

        var hasAnyNewField = payload.SchemaVersionPresent
            || payload.RevisionPresent
            || payload.StorageStatePresent
            || payload.TotalEntriesPresent;
        if (hasAnyNewField)
        {
            if (!payload.SchemaVersionPresent
                || payload.SchemaVersion != 2
                || !payload.RevisionPresent
                || payload.Revision is not int revision
                || revision < 0
                || !payload.StorageStatePresent
                || payload.StorageState is not string storageState
                || !payload.TotalEntriesPresent
                || payload.TotalEntries is not int totalEntries
                || totalEntries < 0
                || (long)totalEntries < (long)worldEntryCount + creatorEntryCount
                || !IsValidModeRevision(storageState, revision))
            {
                return false;
            }
        }

        if (!payload.UpdatedAtPresent)
        {
            return true;
        }

        return TryParseUtcTimestamp(payload.UpdatedAt, out updatedAtUtc);
    }

    private static bool IsValidModeRevision(string storageState, int revision) =>
        storageState switch
        {
            "bootstrap-legacy" or "legacy-freeze" => revision == 0,
            "verification-pending" or "active" => revision >= 1,
            _ => false
        };

    private static bool TryParseUtcTimestamp(string? value, out DateTimeOffset? updatedAtUtc)
    {
        updatedAtUtc = null;
        if (string.IsNullOrEmpty(value)
            || !UtcTimestampPattern.IsMatch(value)
            || !DateTimeOffset.TryParseExact(
                value,
                UtcTimestampFormats,
                CultureInfo.InvariantCulture,
                DateTimeStyles.AssumeUniversal | DateTimeStyles.AdjustToUniversal,
                out var updatedAt)
            || updatedAt.Offset != TimeSpan.Zero)
        {
            return false;
        }

        updatedAtUtc = updatedAt.ToUniversalTime();
        return true;
    }

    private static T ReturnUnlessCallerCancelled<T>(CancellationToken cancellationToken, T result)
    {
        cancellationToken.ThrowIfCancellationRequested();
        return result;
    }

    private sealed record WorldGuardCheckRequest(
        [property: JsonPropertyName("worldId")] string WorldId,
        [property: JsonPropertyName("authorId")] string AuthorId);

    private sealed class WorldGuardCheckResponse
    {
        [JsonPropertyName("schemaVersion")]
        public int? SchemaVersion { get; set; }

        [JsonPropertyName("blocked")]
        public bool? Blocked { get; set; }

        [JsonExtensionData]
        public Dictionary<string, JsonElement>? AdditionalProperties { get; set; }
    }

    private sealed class WorldGuardStatusResponse
    {
        private int? worldEntryCount;
        private int? creatorEntryCount;
        private string? updatedAt;
        private int? schemaVersion;
        private int? revision;
        private string? storageState;
        private int? totalEntries;

        [JsonPropertyName("ok")]
        public bool? Ok { get; set; }

        [JsonPropertyName("worldEntryCount")]
        public int? WorldEntryCount
        {
            get => worldEntryCount;
            set
            {
                WorldEntryCountPresent = true;
                worldEntryCount = value;
            }
        }

        [JsonIgnore]
        public bool WorldEntryCountPresent { get; private set; }

        [JsonPropertyName("creatorEntryCount")]
        public int? CreatorEntryCount
        {
            get => creatorEntryCount;
            set
            {
                CreatorEntryCountPresent = true;
                creatorEntryCount = value;
            }
        }

        [JsonIgnore]
        public bool CreatorEntryCountPresent { get; private set; }

        [JsonPropertyName("updatedAt")]
        public string? UpdatedAt
        {
            get => updatedAt;
            set
            {
                UpdatedAtPresent = true;
                updatedAt = value;
            }
        }

        [JsonIgnore]
        public bool UpdatedAtPresent { get; private set; }

        [JsonPropertyName("schemaVersion")]
        public int? SchemaVersion
        {
            get => schemaVersion;
            set
            {
                SchemaVersionPresent = true;
                schemaVersion = value;
            }
        }

        [JsonIgnore]
        public bool SchemaVersionPresent { get; private set; }

        [JsonPropertyName("revision")]
        public int? Revision
        {
            get => revision;
            set
            {
                RevisionPresent = true;
                revision = value;
            }
        }

        [JsonIgnore]
        public bool RevisionPresent { get; private set; }

        [JsonPropertyName("storageState")]
        public string? StorageState
        {
            get => storageState;
            set
            {
                StorageStatePresent = true;
                storageState = value;
            }
        }

        [JsonIgnore]
        public bool StorageStatePresent { get; private set; }

        [JsonPropertyName("totalEntries")]
        public int? TotalEntries
        {
            get => totalEntries;
            set
            {
                TotalEntriesPresent = true;
                totalEntries = value;
            }
        }

        [JsonIgnore]
        public bool TotalEntriesPresent { get; private set; }

        [JsonExtensionData]
        public Dictionary<string, JsonElement>? AdditionalProperties { get; set; }
    }
}
