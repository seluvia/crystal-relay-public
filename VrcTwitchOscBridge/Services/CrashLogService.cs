using System.IO;
using System.Reflection;
using System.Text;

namespace VrcTwitchOscBridge.Services;

internal static class CrashLogService
{
    private const string AppFolderName = "CrystalRelay";
    private const string CrashLogFilePrefix = "crash-";
    private const string CrashLogFileExtension = ".txt";
    private const string LatestCrashFileName = "latest-crash.txt";

    public static CrashLogWriteResult TryWrite(string source, object? exceptionObject, bool isTerminating)
    {
        var crashReport = BuildCrashReport(source, exceptionObject, isTerminating);
        var candidateFolders = GetCandidateFolders();
        var failureMessages = new List<string>();

        foreach (var folderPath in candidateFolders)
        {
            try
            {
                Directory.CreateDirectory(folderPath);

                var timestamp = DateTime.Now;
                var uniqueSuffix = $"{Environment.ProcessId}-{Environment.CurrentManagedThreadId}";
                var crashLogPath = Path.Combine(
                    folderPath,
                    $"{CrashLogFilePrefix}{timestamp:yyyyMMdd-HHmmss-fff}-{uniqueSuffix}{CrashLogFileExtension}");
                var latestCrashPath = Path.Combine(folderPath, LatestCrashFileName);

                WriteTextFile(crashLogPath, crashReport);
                WriteTextFile(latestCrashPath, crashReport);

                return new CrashLogWriteResult(crashLogPath, latestCrashPath, folderPath, null);
            }
            catch (Exception ex)
            {
                failureMessages.Add($"{folderPath}: {ex.GetType().Name} - {ex.Message}");
            }
        }

        return new CrashLogWriteResult(
            null,
            null,
            null,
            failureMessages.Count == 0
                ? "Crystal Relay could not find a writable crash log folder."
                : string.Join(Environment.NewLine, failureMessages));
    }

    private static string BuildCrashReport(string source, object? exceptionObject, bool isTerminating)
    {
        var builder = new StringBuilder();
        var assembly = Assembly.GetExecutingAssembly().GetName();

        builder.AppendLine("Crystal Relay Crash Log");
        builder.AppendLine($"Timestamp: {DateTime.Now:yyyy-MM-dd HH:mm:ss}");
        builder.AppendLine($"TimestampUtc: {DateTime.UtcNow:yyyy-MM-dd HH:mm:ss}");
        builder.AppendLine($"App Version: {assembly.Version?.ToString() ?? "Unknown"}");
        builder.AppendLine($"Source: {source}");
        builder.AppendLine($"IsTerminating: {isTerminating}");
        builder.AppendLine($"OS: {Environment.OSVersion}");
        builder.AppendLine($"Runtime: {Environment.Version}");
        builder.AppendLine($"Process: {Path.GetFileName(Environment.ProcessPath) ?? "Unknown"}");
        builder.AppendLine($"Process Id: {Environment.ProcessId}");
        builder.AppendLine($"Managed Thread Id: {Environment.CurrentManagedThreadId}");
        builder.AppendLine(new string('-', 80));

        switch (exceptionObject)
        {
            case null:
                builder.AppendLine("No exception object was available.");
                break;

            case Exception exception:
                builder.AppendLine($"Exception Type: {exception.GetType().FullName}");
                builder.AppendLine($"Message: {exception.Message}");
                builder.AppendLine(new string('-', 80));
                builder.AppendLine(exception.ToString());
                break;

            default:
                builder.AppendLine($"Non-Exception crash object type: {exceptionObject.GetType().FullName}");
                builder.AppendLine(new string('-', 80));
                builder.AppendLine(exceptionObject.ToString());
                break;
        }

        return builder.ToString();
    }

    private static IEnumerable<string> GetCandidateFolders()
    {
        var localAppData = AppDataPaths.CrashLogFolder;
        var tempFolder = Path.Combine(Path.GetTempPath(), AppFolderName, "CrashLogs");

        return new[]
        {
            localAppData,
            tempFolder
        }.Where(path => !string.IsNullOrWhiteSpace(path))
         .Distinct(StringComparer.OrdinalIgnoreCase);
    }

    private static void WriteTextFile(string filePath, string contents)
    {
        using var stream = new FileStream(filePath, FileMode.Create, FileAccess.Write, FileShare.Read, 4096, FileOptions.SequentialScan);
        using var writer = new StreamWriter(stream, new UTF8Encoding(encoderShouldEmitUTF8Identifier: false));
        writer.Write(contents);
        writer.Flush();
        stream.Flush(flushToDisk: true);
    }
}

internal sealed record CrashLogWriteResult(
    string? CrashLogPath,
    string? LatestCrashPath,
    string? FolderPath,
    string? FailureReason);
