using System.IO;
using System.Net;
using System.Net.Http;
using System.Net.Http.Json;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace VrcTwitchOscBridge.Services;

internal sealed class BugReportService : IDisposable
{
    public const string GitHubIssuesUrl = "https://github.com/seluvia/crystal-relay-public/issues";

    private const string DesktopClientHeaderName = "X-Crystal-Relay-Client";
    private const string DesktopClientHeaderValue = "CrystalRelayDesktop";
    private const string AppVersionHeaderName = "X-Crystal-Relay-Version";
    private const int MaxDiagnosticLogLength = 40 * 1024;
    private const int MaxPayloadLength = 56 * 1024;
    private const int MaxActivityLogLines = 200;
    private const int MaxDebugLogLines = 400;
    private const string TrimmedMarker = "[trimmed]";
    private static readonly TimeSpan RequestTimeout = TimeSpan.FromSeconds(15);
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web);

    private static readonly string[] BugReportEndpointSegments =
    [
        "https://crystal",
        "-relay",
        "-bug",
        "-report",
        ".screminpal",
        "-animation",
        ".workers",
        ".dev",
        "/report"
    ];

    private readonly HttpClient httpClient = new()
    {
        Timeout = RequestTimeout
    };

    public static Uri BugReportEndpoint => new(string.Concat(BugReportEndpointSegments));

    public async Task<BugReportSubmitResult> SubmitAsync(
        BugReportSubmission submission,
        CancellationToken cancellationToken = default)
    {
        var snapshotTrimmed = TrimToUtf8Length(submission.Snapshot, 2 * 1024);
        var activityTrimmed = submission.ActivityLog is null
            ? null
            : TrimToUtf8Length(submission.ActivityLog, 16 * 1024);
        var debugTrimmed = submission.DebugLog is null
            ? null
            : TrimToUtf8Length(submission.DebugLog, 16 * 1024);
        var crashTrimmed = submission.CrashLog is null
            ? null
            : TrimToUtf8Length(submission.CrashLog, 12 * 1024);

        var payload = new BugReportPayload(
            TrimForTransport(submission.Title, 120),
            TrimForTransport(submission.WhatHappened, 5000),
            TrimForTransport(submission.ExpectedBehavior, 5000),
            TrimForTransport(submission.StepsToReproduce, 5000),
            TrimForTransport(submission.ContactName, 120),
            TrimForTransport(submission.AppVersion, 80),
            TrimForTransport(submission.Category, 40),
            TrimForTransport(submission.Severity, 40),
            snapshotTrimmed,
            activityTrimmed,
            debugTrimmed,
            crashTrimmed,
            DateTimeOffset.UtcNow);

        var json = JsonSerializer.Serialize(payload, JsonOptions);
        if (Encoding.UTF8.GetByteCount(json) > MaxPayloadLength)
        {
            payload = payload with { DebugLog = null };
            json = JsonSerializer.Serialize(payload, JsonOptions);
        }

        if (Encoding.UTF8.GetByteCount(json) > MaxPayloadLength)
        {
            payload = payload with { ActivityLog = null };
            json = JsonSerializer.Serialize(payload, JsonOptions);
        }

        if (Encoding.UTF8.GetByteCount(json) > MaxPayloadLength)
        {
            payload = payload with { CrashLog = null };
            json = JsonSerializer.Serialize(payload, JsonOptions);
        }

        if (Encoding.UTF8.GetByteCount(json) > MaxPayloadLength)
        {
            payload = payload with { Snapshot = TrimToUtf8Length(payload.Snapshot, 1 * 1024) };
        }

        try
        {
            using var request = new HttpRequestMessage(HttpMethod.Post, BugReportEndpoint)
            {
                Content = JsonContent.Create(payload, options: JsonOptions)
            };
            request.Headers.TryAddWithoutValidation(DesktopClientHeaderName, DesktopClientHeaderValue);
            request.Headers.TryAddWithoutValidation(AppVersionHeaderName, payload.AppVersion);

            using var response = await httpClient.SendAsync(request, cancellationToken);
            var responsePayload = await ReadResponsePayloadAsync(response, cancellationToken);
            if (response.IsSuccessStatusCode && !string.IsNullOrWhiteSpace(responsePayload?.IssueUrl))
            {
                return BugReportSubmitResult.Success(responsePayload.IssueUrl);
            }

            var message = responsePayload?.Message;
            if (string.IsNullOrWhiteSpace(message))
            {
                message = $"The bug report service returned HTTP {(int)response.StatusCode}.";
            }

            if (response.StatusCode == HttpStatusCode.TooManyRequests)
            {
                var retryAfterSeconds = GetRetryAfterSeconds(response, responsePayload);
                if (retryAfterSeconds is > 0)
                {
                    message = $"{message} {LocalizationService.Format("Try again in {0}.", DescribeRetryDelay(retryAfterSeconds.Value))}";
                }
            }

            return BugReportSubmitResult.Failure(message);
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            return BugReportSubmitResult.Failure(LocalizationService.Translate("Bug report sending was canceled."));
        }
        catch (Exception ex) when (ex is HttpRequestException or TaskCanceledException or JsonException)
        {
            return BugReportSubmitResult.Failure(
                LocalizationService.Format("Crystal Relay could not reach the bug report service: {0}", ex.Message));
        }
    }

    public string BuildActivityLogSection(IEnumerable<string> activityLogEntries)
    {
        var recentEntries = activityLogEntries
            .Where(entry => !string.IsNullOrWhiteSpace(entry))
            .Take(MaxActivityLogLines)
            .Reverse()
            .ToArray();
        if (recentEntries.Length == 0)
        {
            return string.Empty;
        }

        var builder = new StringBuilder();
        builder.AppendLine("Recent Activity Log");
        builder.AppendLine(new string('-', 40));
        foreach (var entry in recentEntries)
        {
            builder.AppendLine(entry);
        }

        return TrimToUtf8Length(SensitiveTextSanitizer.Sanitize(builder.ToString()), 16 * 1024);
    }

    public string BuildDebugLogSection()
    {
        var recentDebugLogLines = DebugLogService.ReadRecentLines(MaxDebugLogLines);
        if (recentDebugLogLines.Count == 0)
        {
            return string.Empty;
        }

        var builder = new StringBuilder();
        builder.AppendLine("Recent Debug Logs");
        builder.AppendLine(new string('-', 40));
        foreach (var line in recentDebugLogLines)
        {
            builder.AppendLine(line);
        }

        return TrimToUtf8Length(SensitiveTextSanitizer.Sanitize(builder.ToString()), 16 * 1024);
    }

    public string BuildCrashLogSection()
    {
        var latestCrashLog = TryReadLatestCrashLog();
        if (string.IsNullOrWhiteSpace(latestCrashLog))
        {
            return string.Empty;
        }

        var builder = new StringBuilder();
        builder.AppendLine("Latest Crash Log");
        builder.AppendLine(new string('-', 40));
        builder.AppendLine(latestCrashLog);

        return TrimToUtf8Length(SensitiveTextSanitizer.Sanitize(builder.ToString()), 12 * 1024);
    }

    public void Dispose() => httpClient.Dispose();

    private static async Task<BugReportResponse?> ReadResponsePayloadAsync(
        HttpResponseMessage response,
        CancellationToken cancellationToken)
    {
        try
        {
            return await response.Content.ReadFromJsonAsync<BugReportResponse>(JsonOptions, cancellationToken);
        }
        catch (JsonException)
        {
            return null;
        }
    }

    private static int? GetRetryAfterSeconds(HttpResponseMessage response, BugReportResponse? responsePayload)
    {
        if (responsePayload?.RetryAfterSeconds is > 0)
        {
            return responsePayload.RetryAfterSeconds.Value;
        }

        var retryAfter = response.Headers.RetryAfter;
        if (retryAfter?.Delta is { } delta)
        {
            return Math.Max(1, (int)Math.Ceiling(delta.TotalSeconds));
        }

        if (retryAfter?.Date is { } date)
        {
            var seconds = (date - DateTimeOffset.UtcNow).TotalSeconds;
            return seconds > 0 ? Math.Max(1, (int)Math.Ceiling(seconds)) : null;
        }

        return null;
    }

    private static string DescribeRetryDelay(int seconds)
    {
        var delay = TimeSpan.FromSeconds(Math.Max(1, seconds));
        if (delay.TotalHours >= 1)
        {
            return $"{Math.Ceiling(delay.TotalHours):N0} hour(s)";
        }

        return $"{Math.Max(1, Math.Ceiling(delay.TotalMinutes)):N0} minute(s)";
    }

    private static string TrimForTransport(string? value, int maxCharacters)
    {
        var trimmed = value?.Trim() ?? string.Empty;
        return trimmed.Length <= maxCharacters
            ? trimmed
            : trimmed[..maxCharacters];
    }

    private static string TrimToUtf8Length(string value, int maxBytes)
    {
        if (maxBytes <= 0 || string.IsNullOrEmpty(value))
        {
            return string.Empty;
        }

        if (Encoding.UTF8.GetByteCount(value) <= maxBytes)
        {
            return value;
        }

        var marker = $"{Environment.NewLine}{TrimmedMarker}";
        var markerBytes = Encoding.UTF8.GetByteCount(marker);
        if (markerBytes >= maxBytes)
        {
            return TrimMarkerToUtf8Length(marker, maxBytes);
        }

        var contentBudget = maxBytes - markerBytes;
        var builder = new StringBuilder(value.Length);
        var currentBytes = 0;
        foreach (var rune in value.EnumerateRunes())
        {
            var runeText = rune.ToString();
            var runeBytes = Encoding.UTF8.GetByteCount(runeText);
            if (currentBytes + runeBytes > contentBudget)
            {
                break;
            }

            builder.Append(runeText);
            currentBytes += runeBytes;
        }

        return $"{builder.ToString().TrimEnd()}{marker}";
    }

    private static string TrimMarkerToUtf8Length(string marker, int maxBytes)
    {
        var builder = new StringBuilder(marker.Length);
        var currentBytes = 0;
        foreach (var rune in marker.EnumerateRunes())
        {
            var runeText = rune.ToString();
            var runeBytes = Encoding.UTF8.GetByteCount(runeText);
            if (currentBytes + runeBytes > maxBytes)
            {
                break;
            }

            builder.Append(runeText);
            currentBytes += runeBytes;
        }

        return builder.ToString();
    }

    private static string TryReadLatestCrashLog()
    {
        try
        {
            var latestCrashPath = Path.Combine(AppDataPaths.CrashLogFolder, "latest-crash.txt");
            if (!File.Exists(latestCrashPath))
            {
                return string.Empty;
            }

            return File.ReadAllText(latestCrashPath);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            return $"Could not read latest crash log: {ex.Message}";
        }
    }

    private sealed record BugReportPayload(
        string Title,
        string WhatHappened,
        string ExpectedBehavior,
        string StepsToReproduce,
        string ContactName,
        string AppVersion,
        string Category,
        string Severity,
        string Snapshot,
        string? ActivityLog,
        string? DebugLog,
        string? CrashLog,
        DateTimeOffset SubmittedAtUtc);

    private sealed class BugReportResponse
    {
        [JsonPropertyName("issueUrl")]
        public string? IssueUrl { get; set; }

        [JsonPropertyName("message")]
        public string? Message { get; set; }

        [JsonPropertyName("retryAfterSeconds")]
        public int? RetryAfterSeconds { get; set; }
    }
}

internal sealed record BugReportSubmission(
    string Title,
    string WhatHappened,
    string ExpectedBehavior,
    string StepsToReproduce,
    string ContactName,
    string AppVersion,
    string Category,
    string Severity,
    string Snapshot,
    string? ActivityLog,
    string? DebugLog,
    string? CrashLog);

internal sealed record BugReportSubmitResult(bool Succeeded, string? IssueUrl, string? ErrorMessage)
{
    public static BugReportSubmitResult Success(string issueUrl) => new(true, issueUrl, null);

    public static BugReportSubmitResult Failure(string errorMessage) => new(false, null, errorMessage);
}
