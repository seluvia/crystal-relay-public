using System.Diagnostics;
using System.IO;

namespace VrcTwitchOscBridge.Services;

internal static class DebugLogService
{
    private const string LogFilePrefix = "debug-";
    private const string LogFileExtension = ".log";
    private const int RetentionDays = 7;
    private static readonly object SyncRoot = new();
    private static DateOnly lastRetentionPruneDate = DateOnly.MinValue;

    public static event Action<string>? EntryWritten;

    public static void Write(string? message)
    {
        var sanitizedMessage = SensitiveTextSanitizer.Sanitize(message);
        if (string.IsNullOrWhiteSpace(sanitizedMessage))
        {
            return;
        }

        var timestamp = DateTimeOffset.Now;
        var line = $"[{timestamp:yyyy-MM-dd HH:mm:ss.fff zzz}] {sanitizedMessage.ReplaceLineEndings(" ")}";

        lock (SyncRoot)
        {
            try
            {
                Directory.CreateDirectory(AppDataPaths.DebugLogFolder);
                PruneExpiredLogsLocked(timestamp);
                File.AppendAllText(GetLogPath(timestamp), line + Environment.NewLine);
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
            {
                Debug.WriteLine($"Crystal Relay debug logging failed: {ex.Message}");
            }
        }

        EntryWritten?.Invoke(line);
    }

    public static IReadOnlyList<string> ReadRecentLines(int maxLines)
    {
        if (maxLines <= 0)
        {
            return [];
        }

        lock (SyncRoot)
        {
            try
            {
                Directory.CreateDirectory(AppDataPaths.DebugLogFolder);
                PruneExpiredLogsLocked(DateTimeOffset.Now);
                var lines = new List<string>(maxLines);
                foreach (var path in Directory.EnumerateFiles(AppDataPaths.DebugLogFolder, $"{LogFilePrefix}*{LogFileExtension}", SearchOption.TopDirectoryOnly)
                             .OrderByDescending(File.GetLastWriteTimeUtc))
                {
                    var fileLines = File.ReadLines(path)
                        .Where(line => !string.IsNullOrWhiteSpace(line))
                        .Reverse();
                    foreach (var line in fileLines)
                    {
                        lines.Add(SensitiveTextSanitizer.Sanitize(line));
                        if (lines.Count >= maxLines)
                        {
                            lines.Reverse();
                            return lines;
                        }
                    }
                }

                lines.Reverse();
                return lines;
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
            {
                return [$"Could not read debug logs: {SensitiveTextSanitizer.Sanitize(ex.Message)}"];
            }
        }
    }

    private static string GetLogPath(DateTimeOffset timestamp) =>
        Path.Combine(AppDataPaths.DebugLogFolder, $"{LogFilePrefix}{timestamp:yyyyMMdd}{LogFileExtension}");

    private static void PruneExpiredLogsLocked(DateTimeOffset now)
    {
        var today = DateOnly.FromDateTime(now.LocalDateTime);
        if (today == lastRetentionPruneDate)
        {
            return;
        }

        lastRetentionPruneDate = today;
        var cutoff = now.LocalDateTime.Date.AddDays(-(RetentionDays - 1));
        foreach (var path in Directory.EnumerateFiles(AppDataPaths.DebugLogFolder, $"{LogFilePrefix}*{LogFileExtension}", SearchOption.TopDirectoryOnly))
        {
            try
            {
                if (File.GetLastWriteTime(path) < cutoff)
                {
                    File.Delete(path);
                }
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
            {
                Debug.WriteLine($"Crystal Relay debug log cleanup skipped '{path}': {ex.Message}");
            }
        }
    }
}
