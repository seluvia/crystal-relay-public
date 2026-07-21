using System.Net.Http;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json.Serialization;

namespace VrcTwitchOscBridge.Services;

public enum ApplicationUpdateCheckStatus
{
    NoUpdate,
    UpdateAvailable,
    ReleaseVersionUnreadable,
    RequestFailed
}

public sealed record ApplicationUpdateInfo(
    string CurrentVersion,
    string LatestVersion,
    string LatestBaseVersion,
    ApplicationUpdateChannel Channel,
    int BugFixSequence,
    string ReleaseTitle,
    string ReleaseBody,
    string ReleasePageUrl,
    string AssetName,
    string AssetDownloadUrl,
    long AssetSizeBytes,
    string Sha256Digest)
{
    public bool IsBeta => Channel == ApplicationUpdateChannel.Beta;
    public bool IsBugFix => Channel == ApplicationUpdateChannel.BugFix;
}

public sealed record ApplicationUpdateCheckResult(
    ApplicationUpdateCheckStatus Status,
    ApplicationUpdateInfo? Update = null);

public sealed class ApplicationUpdateService : IDisposable
{
    private static readonly Uri ReleasesEndpoint = new(
        "https://api.github.com/repos/seluvia/crystal-relay-public/releases?per_page=100");
    private static readonly TimeSpan DefaultRequestTimeout = TimeSpan.FromSeconds(6);

    private readonly HttpClient httpClient;

    public ApplicationUpdateService()
        : this(new HttpClient { Timeout = DefaultRequestTimeout })
    {
    }

    internal ApplicationUpdateService(HttpClient httpClient)
    {
        this.httpClient = httpClient ?? throw new ArgumentNullException(nameof(httpClient));
        this.httpClient.DefaultRequestHeaders.Accept.Add(
            new MediaTypeWithQualityHeaderValue("application/vnd.github+json"));
        this.httpClient.DefaultRequestHeaders.UserAgent.ParseAdd("CrystalRelay-DesktopApp");
    }

    public async Task<ApplicationUpdateCheckResult> CheckForUpdateAsync(
        ApplicationBuildIdentity currentBuild,
        string ignoredVersionText,
        string ignoredBetaBaseVersionText,
        bool includeBetaUpdates,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(currentBuild);
        if (!TryParseReleaseVersion(currentBuild.UpdateVersion, out var currentVersion))
        {
            return new ApplicationUpdateCheckResult(ApplicationUpdateCheckStatus.NoUpdate);
        }

        List<GitHubReleaseResponse>? releases;
        try
        {
            using var response = await httpClient.GetAsync(ReleasesEndpoint, cancellationToken);
            if (!response.IsSuccessStatusCode)
            {
                return new ApplicationUpdateCheckResult(ApplicationUpdateCheckStatus.RequestFailed);
            }

            releases = await response.Content.ReadFromJsonAsync<List<GitHubReleaseResponse>>(
                cancellationToken: cancellationToken);
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch
        {
            return new ApplicationUpdateCheckResult(ApplicationUpdateCheckStatus.RequestFailed);
        }

        if (releases is null || releases.Count == 0)
        {
            return new ApplicationUpdateCheckResult(ApplicationUpdateCheckStatus.NoUpdate);
        }

        var candidates = releases
            .Where(release => !release.Draft && !string.IsNullOrWhiteSpace(release.HtmlUrl))
            .Select(release => TryCreateCandidate(release, out var candidate) ? candidate : null)
            .OfType<ReleaseCandidate>()
            .ToArray();
        if (candidates.Length == 0)
        {
            return new ApplicationUpdateCheckResult(ApplicationUpdateCheckStatus.ReleaseVersionUnreadable);
        }

        var betaUpdatesAllowed = includeBetaUpdates
            || currentBuild.Channel == ApplicationUpdateChannel.Beta;
        var bestStable = candidates
            .Where(candidate => candidate.Channel == ApplicationUpdateChannel.Stable)
            .Where(candidate => IsNewerStableCandidate(candidate.Version, currentVersion, currentBuild.Channel))
            .Where(candidate => !IsIgnoredUpdate(ignoredVersionText, candidate.Version, betaCandidate: false))
            .OrderByDescending(candidate => candidate.Version)
            .ThenByDescending(candidate => candidate.PublishedAt)
            .FirstOrDefault();

        var bestBugFix = !currentBuild.IsTestBuild
            && currentBuild.Channel is ApplicationUpdateChannel.Stable or ApplicationUpdateChannel.BugFix
                ? candidates
                    .Where(candidate => candidate.Channel == ApplicationUpdateChannel.BugFix)
                    .Where(candidate => candidate.Version.CompareBaseTo(currentVersion) == 0)
                    .Where(candidate => candidate.BugFixSequence > currentBuild.BugFixSequence)
                    .OrderByDescending(candidate => candidate.BugFixSequence)
                    .ThenByDescending(candidate => candidate.PublishedAt)
                    .FirstOrDefault()
                : null;

        var bestBeta = betaUpdatesAllowed
            ? candidates
                .Where(candidate => candidate.Channel == ApplicationUpdateChannel.Beta)
                .Where(candidate => IsNewerBetaCandidate(candidate.Version, currentVersion))
                .Where(candidate => !IsIgnoredUpdate(ignoredVersionText, candidate.Version, betaCandidate: true))
                .Where(candidate => !IsIgnoredBetaBaseVersion(ignoredBetaBaseVersionText, candidate.Version))
                .OrderByDescending(candidate => candidate.Version)
                .ThenByDescending(candidate => candidate.PublishedAt)
                .FirstOrDefault()
            : null;

        var update = bestStable ?? bestBugFix ?? bestBeta;
        if (update is null)
        {
            return new ApplicationUpdateCheckResult(ApplicationUpdateCheckStatus.NoUpdate);
        }

        return new ApplicationUpdateCheckResult(
            ApplicationUpdateCheckStatus.UpdateAvailable,
            new ApplicationUpdateInfo(
                currentBuild.UpdateVersion,
                update.Version.ToDisplayString(),
                update.Version.ToBaseDisplayString(),
                update.Channel,
                update.BugFixSequence,
                update.ReleaseTitle,
                update.ReleaseBody,
                update.ReleasePageUrl,
                update.AssetName,
                update.AssetDownloadUrl,
                update.AssetSizeBytes,
                update.Sha256Digest));
    }

    public void Dispose() => httpClient.Dispose();

    private static bool TryCreateCandidate(GitHubReleaseResponse release, out ReleaseCandidate candidate)
    {
        candidate = default!;
        ApplicationUpdateChannel channel;
        AppReleaseVersion version;
        var bugFixSequence = 0;

        var tagText = release.TagName ?? string.Empty;
        if (tagText.StartsWith("v", StringComparison.Ordinal)
            && ApplicationUpdatePackageRules.TryParseBugFixVersion(
                tagText[1..],
                out var bugFixBaseVersion,
                out bugFixSequence))
        {
            if (release.Prerelease
                || !TryParseReleaseVersion(bugFixBaseVersion, out var baseVersion))
            {
                return false;
            }

            channel = ApplicationUpdateChannel.BugFix;
            version = baseVersion with { Prerelease = $"bugfix{bugFixSequence}" };
        }
        else
        {
            if (!TryParseReleaseVersion(release.TagName, out version))
            {
                return false;
            }

            var isBeta = IsBetaRelease(release, version);
            if (!isBeta && (release.Prerelease || version.IsPrerelease))
            {
                return false;
            }

            channel = isBeta ? ApplicationUpdateChannel.Beta : ApplicationUpdateChannel.Stable;
        }

        if (!TryFindReleaseAsset(release, version, channel, out var asset))
        {
            return false;
        }

        candidate = new ReleaseCandidate(
            version,
            channel,
            bugFixSequence,
            string.IsNullOrWhiteSpace(release.Name) ? version.ToDisplayString() : release.Name.Trim(),
            release.Body ?? string.Empty,
            release.HtmlUrl ?? string.Empty,
            asset.Name ?? string.Empty,
            asset.BrowserDownloadUrl ?? string.Empty,
            Math.Max(0, asset.Size),
            asset.Digest ?? string.Empty,
            release.PublishedAt ?? DateTimeOffset.MinValue);
        return true;
    }

    private static bool TryFindReleaseAsset(
        GitHubReleaseResponse release,
        AppReleaseVersion version,
        ApplicationUpdateChannel channel,
        out GitHubReleaseAssetResponse asset)
    {
        asset = default!;
        var expectedAssetName = ApplicationUpdatePackageRules.GetExpectedAssetName(
            channel,
            version.ToDisplayString());
        var nameComparison = channel == ApplicationUpdateChannel.BugFix
            ? StringComparison.Ordinal
            : StringComparison.OrdinalIgnoreCase;
        var match = (release.Assets ?? [])
            .Where(candidate => !string.IsNullOrWhiteSpace(candidate.Name))
            .FirstOrDefault(candidate => string.Equals(
                candidate.Name,
                expectedAssetName,
                nameComparison));
        if (match is null || string.IsNullOrWhiteSpace(match.BrowserDownloadUrl))
        {
            return false;
        }

        asset = match;
        return true;
    }

    private static bool IsNewerStableCandidate(
        AppReleaseVersion stableVersion,
        AppReleaseVersion currentVersion,
        ApplicationUpdateChannel currentChannel) =>
        currentChannel == ApplicationUpdateChannel.BugFix
            ? stableVersion.CompareBaseTo(currentVersion) > 0
            : stableVersion.CompareTo(currentVersion) > 0;

    private static bool IsNewerBetaCandidate(AppReleaseVersion betaVersion, AppReleaseVersion currentVersion)
    {
        var baseComparison = betaVersion.CompareBaseTo(currentVersion);
        if (baseComparison > 0)
        {
            return true;
        }

        if (baseComparison < 0)
        {
            return false;
        }

        if (currentVersion.IsPrerelease)
        {
            return betaVersion.CompareTo(currentVersion) > 0;
        }

        return false;
    }

    private static bool IsIgnoredUpdate(string ignoredVersionText, AppReleaseVersion candidateVersion, bool betaCandidate)
    {
        if (!TryParseReleaseVersion(ignoredVersionText, out var ignoredVersion))
        {
            return false;
        }

        if (betaCandidate || candidateVersion.IsPrerelease || ignoredVersion.IsPrerelease)
        {
            return candidateVersion.HasSameIdentity(ignoredVersion);
        }

        return candidateVersion.CompareTo(ignoredVersion) <= 0;
    }

    private static bool IsIgnoredBetaBaseVersion(string ignoredBetaBaseVersionText, AppReleaseVersion candidateVersion)
    {
        if (string.IsNullOrWhiteSpace(ignoredBetaBaseVersionText))
        {
            return false;
        }

        return string.Equals(
            candidateVersion.ToBaseDisplayString(),
            ignoredBetaBaseVersionText.Trim(),
            StringComparison.OrdinalIgnoreCase);
    }

    private static bool IsBetaRelease(GitHubReleaseResponse release, AppReleaseVersion version)
    {
        if (ContainsBetaMarker(version.Prerelease))
        {
            return true;
        }

        return release.Prerelease
            && (ContainsBetaMarker(release.TagName) || ContainsBetaMarker(release.Name));
    }

    private static bool ContainsBetaMarker(string? value) =>
        !string.IsNullOrWhiteSpace(value)
        && value.Contains("beta", StringComparison.OrdinalIgnoreCase);

    private static bool TryParseReleaseVersion(string? value, out AppReleaseVersion version)
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

        var prerelease = string.Empty;
        var hyphenIndex = normalized.IndexOf('-');
        if (hyphenIndex >= 0)
        {
            prerelease = normalized[(hyphenIndex + 1)..].Trim();
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

        version = new AppReleaseVersion(major, minor, patch, prerelease);
        return true;
    }

    private sealed record ReleaseCandidate(
        AppReleaseVersion Version,
        ApplicationUpdateChannel Channel,
        int BugFixSequence,
        string ReleaseTitle,
        string ReleaseBody,
        string ReleasePageUrl,
        string AssetName,
        string AssetDownloadUrl,
        long AssetSizeBytes,
        string Sha256Digest,
        DateTimeOffset PublishedAt);

    private readonly record struct AppReleaseVersion(int Major, int Minor, int Patch, string Prerelease)
        : IComparable<AppReleaseVersion>
    {
        public bool IsPrerelease => !string.IsNullOrWhiteSpace(Prerelease);

        public int CompareBaseTo(AppReleaseVersion other)
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

        public int CompareTo(AppReleaseVersion other)
        {
            var baseComparison = CompareBaseTo(other);
            if (baseComparison != 0)
            {
                return baseComparison;
            }

            if (!IsPrerelease && !other.IsPrerelease)
            {
                return 0;
            }

            if (!IsPrerelease)
            {
                return 1;
            }

            if (!other.IsPrerelease)
            {
                return -1;
            }

            return ComparePrerelease(Prerelease, other.Prerelease);
        }

        public bool HasSameIdentity(AppReleaseVersion other) => CompareTo(other) == 0;

        public string ToDisplayString() =>
            IsPrerelease
                ? $"{Major}.{Minor}.{Patch}-{Prerelease}"
                : $"{Major}.{Minor}.{Patch}";

        public string ToBaseDisplayString() => $"{Major}.{Minor}.{Patch}";
    }

    private static int ComparePrerelease(string left, string right)
    {
        var leftParts = left.Split(['.', '-', '_'], StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
        var rightParts = right.Split(['.', '-', '_'], StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
        var count = Math.Max(leftParts.Length, rightParts.Length);
        for (var index = 0; index < count; index++)
        {
            if (index >= leftParts.Length)
            {
                return -1;
            }

            if (index >= rightParts.Length)
            {
                return 1;
            }

            var comparison = ComparePrereleasePart(leftParts[index], rightParts[index]);
            if (comparison != 0)
            {
                return comparison;
            }
        }

        return 0;
    }

    private static int ComparePrereleasePart(string left, string right)
    {
        var leftSplit = SplitAlphaNumericSuffix(left);
        var rightSplit = SplitAlphaNumericSuffix(right);
        var prefixComparison = string.Compare(leftSplit.Prefix, rightSplit.Prefix, StringComparison.OrdinalIgnoreCase);
        if (prefixComparison != 0)
        {
            return prefixComparison;
        }

        if (leftSplit.Number is { } leftNumber && rightSplit.Number is { } rightNumber)
        {
            return leftNumber.CompareTo(rightNumber);
        }

        return string.Compare(left, right, StringComparison.OrdinalIgnoreCase);
    }

    private static (string Prefix, int? Number) SplitAlphaNumericSuffix(string value)
    {
        var splitIndex = value.Length;
        while (splitIndex > 0 && char.IsDigit(value[splitIndex - 1]))
        {
            splitIndex--;
        }

        if (splitIndex == value.Length || !int.TryParse(value[splitIndex..], out var number))
        {
            return (value, null);
        }

        return (value[..splitIndex], number);
    }

    private sealed class GitHubReleaseResponse
    {
        [JsonPropertyName("tag_name")]
        public string? TagName { get; set; }

        [JsonPropertyName("name")]
        public string? Name { get; set; }

        [JsonPropertyName("body")]
        public string? Body { get; set; }

        [JsonPropertyName("html_url")]
        public string? HtmlUrl { get; set; }

        [JsonPropertyName("draft")]
        public bool Draft { get; set; }

        [JsonPropertyName("prerelease")]
        public bool Prerelease { get; set; }

        [JsonPropertyName("published_at")]
        public DateTimeOffset? PublishedAt { get; set; }

        [JsonPropertyName("assets")]
        public List<GitHubReleaseAssetResponse>? Assets { get; set; }
    }

    private sealed class GitHubReleaseAssetResponse
    {
        [JsonPropertyName("name")]
        public string? Name { get; set; }

        [JsonPropertyName("browser_download_url")]
        public string? BrowserDownloadUrl { get; set; }

        [JsonPropertyName("size")]
        public long Size { get; set; }

        [JsonPropertyName("digest")]
        public string? Digest { get; set; }
    }
}
