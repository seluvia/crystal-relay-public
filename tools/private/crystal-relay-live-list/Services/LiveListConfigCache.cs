using System.IO;
using System.Text.Json;

namespace CrystalRelayLiveList.Services;

public sealed record LiveListResolvedConfig(Uri? Endpoint, string AlertSoundPath, string SourcePath);

public sealed class LiveListConfigCache
{
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web)
    {
        PropertyNameCaseInsensitive = true
    };

    private readonly IReadOnlyList<string> candidatePaths;
    private LiveListResolvedConfig? cached;
    private long lastSignature;

    public LiveListConfigCache(IReadOnlyList<string> candidatePaths)
    {
        this.candidatePaths = candidatePaths;
    }

    public void Invalidate()
    {
        cached = null;
        lastSignature = 0;
    }

    public LiveListResolvedConfig Resolve()
    {
        if (cached is not null && !SignatureChanged())
        {
            return cached;
        }

        foreach (var path in candidatePaths)
        {
            if (!File.Exists(path))
            {
                continue;
            }
            try
            {
                var json = File.ReadAllText(path);
                var config = JsonSerializer.Deserialize<LiveListConfigPayload>(json, JsonOptions);
                if (config is null)
                {
                    continue;
                }

                var endpointText = !string.IsNullOrWhiteSpace(config.LiveApiEndpoint)
                    ? config.LiveApiEndpoint
                    : config.LiveFeedbackHeartbeatEndpoint;
                var endpoint = BuildLiveApiUri(endpointText ?? string.Empty);
                var sound = config.LiveAlertSoundPath ?? string.Empty;
                cached = new LiveListResolvedConfig(endpoint, sound, path);
                lastSignature = ComputeSignature();
                return cached;
            }
            catch
            {
                // ignore unreadable candidate
            }
        }

        cached = new LiveListResolvedConfig(null, string.Empty, string.Empty);
        lastSignature = ComputeSignature();
        return cached;
    }

    private bool SignatureChanged() => ComputeSignature() != lastSignature;

    private long ComputeSignature()
    {
        long sig = 0;
        foreach (var path in candidatePaths)
        {
            try
            {
                if (File.Exists(path))
                {
                    var info = new FileInfo(path);
                    sig = unchecked(sig * 31 + info.LastWriteTimeUtc.Ticks);
                }
            }
            catch
            {
                // ignore
            }
        }
        return sig;
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

        var builder = new UriBuilder(uri) { Query = string.Empty, Fragment = string.Empty };
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
            builder.Path = string.IsNullOrWhiteSpace(path) || path == "/" ? "/api/live" : $"{path}/api/live";
        }
        return builder.Uri;
    }

    private sealed class LiveListConfigPayload
    {
        public string LiveFeedbackHeartbeatEndpoint { get; set; } = string.Empty;
        public string LiveApiEndpoint { get; set; } = string.Empty;
        public string LiveAlertSoundPath { get; set; } = string.Empty;
    }
}
