using System.Net.Http;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json.Serialization;

namespace VrcTwitchOscBridge.Services;

internal enum ApplicationUpdateCheckStatus
{
    NoUpdate,
    UpdateAvailable,
    ReleaseVersionUnreadable,
    RequestFailed
}

internal sealed record ApplicationUpdateInfo(
    string CurrentVersion,
    string LatestVersion,
    string ReleasePageUrl);

internal sealed record ApplicationUpdateCheckResult(
    ApplicationUpdateCheckStatus Status,
    ApplicationUpdateInfo? Update = null);

/// <summary>
/// Checks the latest public Crystal Relay GitHub release so the app can prompt
/// users when a newer published version is available.
/// </summary>
internal sealed class ApplicationUpdateService : IDisposable
{
    private static readonly Uri LatestReleaseEndpoint = new("https://api.github.com/repos/seluvia/crystal-relay-public/releases/latest");
    private static readonly TimeSpan DefaultRequestTimeout = TimeSpan.FromSeconds(6);

    private readonly HttpClient httpClient = new()
    {
        Timeout = DefaultRequestTimeout
    };

    public ApplicationUpdateService()
    {
        httpClient.DefaultRequestHeaders.Accept.Add(new MediaTypeWithQualityHeaderValue("application/vnd.github+json"));
        httpClient.DefaultRequestHeaders.UserAgent.ParseAdd("CrystalRelay-DesktopApp");
    }

    public async Task<ApplicationUpdateCheckResult> CheckForUpdateAsync(
        string currentVersionText,
        string ignoredVersionText,
        CancellationToken cancellationToken = default)
    {
        if (!TryParseVersion(currentVersionText, out var currentVersion))
        {
            return new ApplicationUpdateCheckResult(ApplicationUpdateCheckStatus.NoUpdate);
        }

        GitHubReleaseResponse? release;
        try
        {
            using var response = await httpClient.GetAsync(LatestReleaseEndpoint, cancellationToken);
            if (!response.IsSuccessStatusCode)
            {
                return new ApplicationUpdateCheckResult(ApplicationUpdateCheckStatus.RequestFailed);
            }

            release = await response.Content.ReadFromJsonAsync<GitHubReleaseResponse>(cancellationToken: cancellationToken);
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch
        {
            return new ApplicationUpdateCheckResult(ApplicationUpdateCheckStatus.RequestFailed);
        }

        if (release is null
            || release.Draft
            || release.Prerelease
            || string.IsNullOrWhiteSpace(release.HtmlUrl))
        {
            return new ApplicationUpdateCheckResult(ApplicationUpdateCheckStatus.NoUpdate);
        }

        if (!TryParseVersion(release.TagName, out var latestVersion))
        {
            return new ApplicationUpdateCheckResult(ApplicationUpdateCheckStatus.ReleaseVersionUnreadable);
        }

        if (latestVersion.CompareTo(currentVersion) <= 0)
        {
            return new ApplicationUpdateCheckResult(ApplicationUpdateCheckStatus.NoUpdate);
        }

        if (TryParseVersion(ignoredVersionText, out var ignoredVersion)
            && latestVersion.CompareTo(ignoredVersion) <= 0)
        {
            return new ApplicationUpdateCheckResult(ApplicationUpdateCheckStatus.NoUpdate);
        }

        return new ApplicationUpdateCheckResult(
            ApplicationUpdateCheckStatus.UpdateAvailable,
            new ApplicationUpdateInfo(
                currentVersion.ToDisplayString(),
                latestVersion.ToDisplayString(),
                release.HtmlUrl));
    }

    public void Dispose() => httpClient.Dispose();

    private static bool TryParseVersion(string? value, out AppVersionTriple version)
    {
        version = default;
        if (string.IsNullOrWhiteSpace(value))
        {
            return false;
        }

        var normalized = value.Trim();
        if (normalized.StartsWith('v') || normalized.StartsWith('V'))
        {
            normalized = normalized[1..];
        }

        var plusIndex = normalized.IndexOf('+');
        if (plusIndex >= 0)
        {
            normalized = normalized[..plusIndex];
        }

        var hyphenIndex = normalized.IndexOf('-');
        if (hyphenIndex >= 0)
        {
            normalized = normalized[..hyphenIndex];
        }

        var parts = normalized.Split('.', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
        if (parts.Length is < 3 or > 4)
        {
            return false;
        }

        if (!int.TryParse(parts[0], out var major)
            || !int.TryParse(parts[1], out var minor)
            || !int.TryParse(parts[2], out var patch))
        {
            return false;
        }

        version = new AppVersionTriple(major, minor, patch);
        return true;
    }

    private readonly record struct AppVersionTriple(int Major, int Minor, int Patch)
    {
        public int CompareTo(AppVersionTriple other)
        {
            var majorComparison = Major.CompareTo(other.Major);
            if (majorComparison != 0)
            {
                return majorComparison;
            }

            var minorComparison = Minor.CompareTo(other.Minor);
            if (minorComparison != 0)
            {
                return minorComparison;
            }

            return Patch.CompareTo(other.Patch);
        }

        public string ToDisplayString() => $"{Major}.{Minor}.{Patch}";
    }

    private sealed class GitHubReleaseResponse
    {
        [JsonPropertyName("tag_name")]
        public string? TagName { get; set; }

        [JsonPropertyName("html_url")]
        public string? HtmlUrl { get; set; }

        [JsonPropertyName("draft")]
        public bool Draft { get; set; }

        [JsonPropertyName("prerelease")]
        public bool Prerelease { get; set; }
    }
}
