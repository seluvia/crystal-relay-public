using System.ComponentModel;
using System.Diagnostics;
using System.IO;
using System.Runtime.ExceptionServices;
using System.Runtime.InteropServices;
using System.Security.Cryptography;

namespace VrcTwitchOscBridge.Services;

internal sealed class ApplicationUpdateRollbackException : Exception
{
    internal ApplicationUpdateRollbackException(
        Exception originalException,
        IReadOnlyList<Exception> recoveryExceptions)
        : base(
            "Crystal Relay could not complete update rollback because one or more recovery steps failed.",
            new AggregateException(
                "The update recovery operations failed.",
                new[] { originalException }.Concat(recoveryExceptions)))
    {
        OriginalException = originalException;
        RecoveryExceptions = recoveryExceptions;
    }

    internal Exception OriginalException { get; }

    internal IReadOnlyList<Exception> RecoveryExceptions { get; }
}

internal static class ApplicationUpdatePathSafety
{
    internal static void EnsureNoReparsePointInExistingAncestors(string path)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(path);

        var currentPath = Path.GetFullPath(path);
        while (!string.IsNullOrWhiteSpace(currentPath))
        {
            try
            {
                var attributes = File.GetAttributes(currentPath);
                if ((attributes & FileAttributes.ReparsePoint) != 0)
                {
                    throw new IOException($"The update path contains a reparse point: '{currentPath}'.");
                }
            }
            catch (FileNotFoundException)
            {
            }
            catch (DirectoryNotFoundException)
            {
            }

            var root = Path.GetPathRoot(currentPath);
            if (string.Equals(currentPath, root, StringComparison.OrdinalIgnoreCase))
            {
                return;
            }

            var parent = Path.GetDirectoryName(currentPath);
            if (string.IsNullOrWhiteSpace(parent)
                || string.Equals(parent, currentPath, StringComparison.OrdinalIgnoreCase))
            {
                return;
            }

            currentPath = parent;
        }
    }

    internal static void ValidateDirectoryTree(string directoryPath)
    {
        EnsureNoReparsePointInExistingAncestors(directoryPath);
        var fullDirectoryPath = Path.GetFullPath(directoryPath);
        if (!Directory.Exists(fullDirectoryPath)
            || IsReparsePoint(fullDirectoryPath))
        {
            throw new IOException($"The update directory is missing or unsafe: '{directoryPath}'.");
        }

        ValidateDirectoryTreeCore(fullDirectoryPath);
    }

    internal static IReadOnlyList<string> GetRegularFiles(string directoryPath)
    {
        ValidateDirectoryTree(directoryPath);
        var files = new List<string>();
        CollectRegularFiles(Path.GetFullPath(directoryPath), files);
        return files;
    }

    internal static void ValidateCopiedDirectoryTree(
        string sourceDirectory,
        string copiedDirectory)
    {
        ValidateDirectoryTree(sourceDirectory);
        ValidateDirectoryTree(copiedDirectory);

        var sourceFiles = GetRegularFileFingerprints(sourceDirectory);
        var copiedFiles = GetRegularFileFingerprints(copiedDirectory);
        if (sourceFiles.Count != copiedFiles.Count)
        {
            throw new IOException("The rollback backup did not contain the same regular files as its source.");
        }

        foreach (var sourceFile in sourceFiles)
        {
            if (!copiedFiles.TryGetValue(sourceFile.Key, out var copiedFingerprint)
                || copiedFingerprint.Length != sourceFile.Value.Length
                || !CryptographicOperations.FixedTimeEquals(
                    copiedFingerprint.Hash,
                    sourceFile.Value.Hash))
            {
                throw new IOException(
                    $"The rollback backup file set, size, or content did not match '{sourceFile.Key}'.");
            }
        }

        ValidateDirectoryTree(sourceDirectory);
        ValidateDirectoryTree(copiedDirectory);
    }

    internal static bool IsSafeDirectoryTree(string directoryPath)
    {
        try
        {
            ValidateDirectoryTree(directoryPath);
            return true;
        }
        catch
        {
            return false;
        }
    }

    private static void ValidateDirectoryTreeCore(string directoryPath)
    {
        EnsureNoReparsePointInExistingAncestors(directoryPath);
        foreach (var filePath in Directory.EnumerateFiles(directoryPath))
        {
            EnsureRegularFile(filePath);
        }

        EnsureNoReparsePointInExistingAncestors(directoryPath);
        foreach (var subDirectoryPath in Directory.EnumerateDirectories(directoryPath))
        {
            EnsureNoReparsePointInExistingAncestors(subDirectoryPath);
            if (IsReparsePoint(subDirectoryPath))
            {
                throw new IOException($"The update directory contains a reparse point: '{subDirectoryPath}'.");
            }

            ValidateDirectoryTreeCore(subDirectoryPath);
        }
    }

    private static void CollectRegularFiles(string directoryPath, ICollection<string> files)
    {
        EnsureNoReparsePointInExistingAncestors(directoryPath);
        foreach (var filePath in Directory.EnumerateFiles(directoryPath))
        {
            EnsureRegularFile(filePath);
            files.Add(filePath);
        }

        EnsureNoReparsePointInExistingAncestors(directoryPath);
        foreach (var subDirectoryPath in Directory.EnumerateDirectories(directoryPath))
        {
            EnsureNoReparsePointInExistingAncestors(subDirectoryPath);
            if (IsReparsePoint(subDirectoryPath))
            {
                throw new IOException($"The update directory contains a reparse point: '{subDirectoryPath}'.");
            }

            CollectRegularFiles(subDirectoryPath, files);
        }
    }

    internal static void TerminateProcessTreeAndWait(Process process, TimeSpan timeout)
    {
        ArgumentNullException.ThrowIfNull(process);
        if (timeout < TimeSpan.Zero && timeout != Timeout.InfiniteTimeSpan)
        {
            throw new ArgumentOutOfRangeException(nameof(timeout));
        }

        var rootProcessId = process.Id;
        var trackedProcessIds = new HashSet<int>(GetDescendantProcessIds(rootProcessId))
        {
            rootProcessId
        };

        if (!process.HasExited)
        {
            try
            {
                process.Kill(entireProcessTree: true);
            }
            catch (InvalidOperationException) when (process.HasExited)
            {
                // The replacement exited between the status check and Kill.
            }
            catch (ArgumentException) when (process.HasExited)
            {
                // The replacement exited between the status check and Kill.
            }
            catch (Win32Exception) when (process.HasExited)
            {
                // Windows reported that the replacement disappeared during termination.
            }
        }

        var stopwatch = Stopwatch.StartNew();
        while (true)
        {
            foreach (var descendantProcessId in GetDescendantProcessIds(rootProcessId))
            {
                trackedProcessIds.Add(descendantProcessId);
            }

            var liveDescendantExists = trackedProcessIds.Any(
                processId => processId != rootProcessId && IsProcessRunning(processId));
            if (process.HasExited && !liveDescendantExists)
            {
                return;
            }

            if (timeout != Timeout.InfiniteTimeSpan && stopwatch.Elapsed >= timeout)
            {
                throw new TimeoutException(
                    "Crystal Relay could not confirm termination of the failed replacement process tree.");
            }

            var waitMilliseconds = 50;
            if (timeout != Timeout.InfiniteTimeSpan)
            {
                var remaining = timeout - stopwatch.Elapsed;
                waitMilliseconds = Math.Max(
                    1,
                    Math.Min(waitMilliseconds, (int)Math.Ceiling(remaining.TotalMilliseconds)));
            }

            if (!process.HasExited)
            {
                process.WaitForExit(waitMilliseconds);
            }
            else
            {
                Thread.Sleep(waitMilliseconds);
            }
        }
    }

    private static Dictionary<string, FileFingerprint> GetRegularFileFingerprints(string directoryPath)
    {
        var fullDirectoryPath = Path.GetFullPath(directoryPath);
        var fingerprints = new Dictionary<string, FileFingerprint>(StringComparer.OrdinalIgnoreCase);
        foreach (var filePath in GetRegularFiles(fullDirectoryPath))
        {
            var relativePath = Path.GetRelativePath(fullDirectoryPath, filePath);
            if (!fingerprints.TryAdd(relativePath, GetFileFingerprint(filePath)))
            {
                throw new IOException($"The directory contains duplicate file paths: '{relativePath}'.");
            }
        }

        return fingerprints;
    }

    private static FileFingerprint GetFileFingerprint(string filePath)
    {
        EnsureRegularFile(filePath);
        using var stream = new FileStream(
            filePath,
            FileMode.Open,
            FileAccess.Read,
            FileShare.Read,
            bufferSize: 64 * 1024,
            options: FileOptions.SequentialScan);
        var fingerprint = new FileFingerprint(stream.Length, SHA256.HashData(stream));
        EnsureRegularFile(filePath);
        return fingerprint;
    }

    private static void EnsureRegularFile(string filePath)
    {
        EnsureNoReparsePointInExistingAncestors(filePath);
        var attributes = File.GetAttributes(filePath);
        if ((attributes & FileAttributes.ReparsePoint) != 0
            || (attributes & FileAttributes.Directory) != 0)
        {
            throw new IOException($"The update tree contains an unsafe file entry: '{filePath}'.");
        }
    }

    private static bool IsReparsePoint(string path)
    {
        var attributes = File.GetAttributes(path);
        return (attributes & FileAttributes.ReparsePoint) != 0;
    }

    private static bool IsProcessRunning(int processId)
    {
        try
        {
            using var process = Process.GetProcessById(processId);
            return !process.HasExited;
        }
        catch (ArgumentException)
        {
            return false;
        }
        catch (InvalidOperationException)
        {
            return false;
        }
    }

    private static IReadOnlyList<int> GetDescendantProcessIds(int rootProcessId)
    {
        var snapshot = CreateToolhelp32Snapshot(ToolhelpSnapshotProcess, 0);
        if (snapshot == InvalidHandleValue)
        {
            throw new Win32Exception(Marshal.GetLastWin32Error(), "Could not enumerate the Windows process tree.");
        }

        try
        {
            var childrenByParent = new Dictionary<int, List<int>>();
            var entry = new ProcessEntry32
            {
                Size = (uint)Marshal.SizeOf<ProcessEntry32>()
            };
            if (Process32First(snapshot, ref entry))
            {
                do
                {
                    var processId = checked((int)entry.ProcessId);
                    var parentProcessId = checked((int)entry.ParentProcessId);
                    if (!childrenByParent.TryGetValue(parentProcessId, out var children))
                    {
                        children = [];
                        childrenByParent[parentProcessId] = children;
                    }

                    children.Add(processId);
                }
                while (Process32Next(snapshot, ref entry));

                var error = Marshal.GetLastWin32Error();
                if (error != ErrorNoMoreFiles)
                {
                    throw new Win32Exception(error, "Could not enumerate the Windows process tree.");
                }
            }
            else
            {
                var error = Marshal.GetLastWin32Error();
                if (error != ErrorNoMoreFiles)
                {
                    throw new Win32Exception(error, "Could not enumerate the Windows process tree.");
                }
            }

            var descendants = new List<int>();
            var pendingParents = new Queue<int>([rootProcessId]);
            var visitedParents = new HashSet<int>();
            while (pendingParents.Count > 0)
            {
                var parentProcessId = pendingParents.Dequeue();
                if (!visitedParents.Add(parentProcessId)
                    || !childrenByParent.TryGetValue(parentProcessId, out var children))
                {
                    continue;
                }

                foreach (var childProcessId in children)
                {
                    if (childProcessId == rootProcessId || descendants.Contains(childProcessId))
                    {
                        continue;
                    }

                    descendants.Add(childProcessId);
                    pendingParents.Enqueue(childProcessId);
                }
            }

            return descendants;
        }
        finally
        {
            CloseHandle(snapshot);
        }
    }

    private const uint ToolhelpSnapshotProcess = 0x00000002;
    private const int ErrorNoMoreFiles = 18;
    private static readonly IntPtr InvalidHandleValue = new(-1);

    [DllImport("kernel32.dll", EntryPoint = "CreateToolhelp32Snapshot", SetLastError = true)]
    private static extern IntPtr CreateToolhelp32Snapshot(uint flags, uint processId);

    [DllImport("kernel32.dll", EntryPoint = "Process32FirstW", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool Process32First(IntPtr snapshot, ref ProcessEntry32 entry);

    [DllImport("kernel32.dll", EntryPoint = "Process32NextW", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool Process32Next(IntPtr snapshot, ref ProcessEntry32 entry);

    [DllImport("kernel32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool CloseHandle(IntPtr handle);

    [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
    private struct ProcessEntry32
    {
        internal uint Size;
        internal uint Usage;
        internal uint ProcessId;
        internal IntPtr DefaultHeapId;
        internal uint ModuleId;
        internal uint ThreadCount;
        internal uint ParentProcessId;
        internal int BasePriority;
        internal uint Flags;
        [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 260)]
        internal string? ExecutableFile;
    }

    private readonly record struct FileFingerprint(long Length, byte[] Hash);
}

internal static class ApplicationUpdateRollback
{
    internal const string CompleteMarkerFileName = "rollback-complete.flag";
    internal const string IncompleteDirectorySuffix = ".incomplete";
    internal const string StartupAcknowledgementFileName = "startup-acknowledged.flag";
    internal const string TargetPresenceFileName = "target-presence.flag";

    private const string CompleteMarkerText = "complete";
    private const string TargetPresentText = "present";
    private const string TargetAbsentText = "absent";
    private const string SourceDirectoryName = "source";
    private const string TargetDirectoryName = "target";
    private static readonly TimeSpan AcknowledgementPollInterval = TimeSpan.FromMilliseconds(25);

    internal static void TerminateProcessTreeAndWait(Process process, TimeSpan timeout) =>
        ApplicationUpdatePathSafety.TerminateProcessTreeAndWait(process, timeout);

    internal static string GetIncompleteBackupDirectory(string backupDirectory)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(backupDirectory);
        return $"{NormalizeDirectoryPath(backupDirectory)}{IncompleteDirectorySuffix}";
    }

    internal static string GetStartupAcknowledgementPath(string applyManifestPath)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(applyManifestPath);

        var fullManifestPath = Path.GetFullPath(applyManifestPath);
        ApplicationUpdatePathSafety.EnsureNoReparsePointInExistingAncestors(fullManifestPath);
        var manifestDirectory = Path.GetDirectoryName(fullManifestPath);
        if (string.IsNullOrWhiteSpace(manifestDirectory))
        {
            throw new ArgumentException("The apply manifest must be inside an update session.", nameof(applyManifestPath));
        }

        var sessionName = Path.GetFileName(manifestDirectory);
        var updatesDirectory = Path.GetDirectoryName(manifestDirectory);
        if (string.IsNullOrWhiteSpace(sessionName) || string.IsNullOrWhiteSpace(updatesDirectory))
        {
            throw new ArgumentException("The apply manifest must be inside an update session.", nameof(applyManifestPath));
        }

        var acknowledgementPath = Path.Combine(updatesDirectory, sessionName, StartupAcknowledgementFileName);
        ApplicationUpdatePathSafety.EnsureNoReparsePointInExistingAncestors(acknowledgementPath);
        return acknowledgementPath;
    }

    internal static void ClearStaleStartupAcknowledgement(string applyManifestPath)
    {
        var acknowledgementPath = GetStartupAcknowledgementPath(applyManifestPath);
        switch (GetEntryKind(acknowledgementPath))
        {
            case EntryKind.Missing:
                return;
            case EntryKind.File:
                ApplicationUpdatePathSafety.EnsureNoReparsePointInExistingAncestors(acknowledgementPath);
                File.Delete(acknowledgementPath);
                if (GetEntryKind(acknowledgementPath) != EntryKind.Missing)
                {
                    throw new IOException("The stale Crystal Relay startup acknowledgement could not be removed.");
                }

                return;
            case EntryKind.ReparsePoint:
                throw new IOException("The stale Crystal Relay startup acknowledgement is a reparse point.");
            default:
                throw new IOException("The stale Crystal Relay startup acknowledgement path is not a file.");
        }
    }

    internal static bool GetRecordedTargetPresence(string backupDirectory)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(backupDirectory);

        var finalBackupDirectory = NormalizeDirectoryPath(backupDirectory);
        ApplicationUpdatePathSafety.EnsureNoReparsePointInExistingAncestors(finalBackupDirectory);
        var targetPresencePath = Path.Combine(finalBackupDirectory, TargetPresenceFileName);
        ApplicationUpdatePathSafety.EnsureNoReparsePointInExistingAncestors(targetPresencePath);
        if (GetEntryKind(targetPresencePath) != EntryKind.File)
        {
            throw new IOException("The relocation target-presence marker is missing or invalid.");
        }

        ApplicationUpdatePathSafety.EnsureNoReparsePointInExistingAncestors(targetPresencePath);
        return File.ReadAllText(targetPresencePath) switch
        {
            TargetPresentText => true,
            TargetAbsentText => false,
            _ => throw new IOException("The relocation target-presence marker is missing or invalid.")
        };
    }

    internal static bool IsEntryMissing(string path)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(path);
        ApplicationUpdatePathSafety.EnsureNoReparsePointInExistingAncestors(path);
        return GetEntryKind(path) == EntryKind.Missing;
    }

    internal static void PrepareCompleteBackup(
        string backupDirectory,
        string sourceDirectory,
        string targetDirectory,
        bool relocatesInstallFolder,
        Action<string, string> copyDirectoryContents,
        Action<string> validateBackupDirectory,
        Action<string> deleteDirectoryTree)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(backupDirectory);
        ArgumentException.ThrowIfNullOrWhiteSpace(sourceDirectory);
        ArgumentException.ThrowIfNullOrWhiteSpace(targetDirectory);
        ArgumentNullException.ThrowIfNull(copyDirectoryContents);
        ArgumentNullException.ThrowIfNull(validateBackupDirectory);
        ArgumentNullException.ThrowIfNull(deleteDirectoryTree);

        var finalBackupDirectory = NormalizeDirectoryPath(backupDirectory);
        var incompleteBackupDirectory = GetIncompleteBackupDirectory(finalBackupDirectory);
        ApplicationUpdatePathSafety.EnsureNoReparsePointInExistingAncestors(finalBackupDirectory);
        ApplicationUpdatePathSafety.EnsureNoReparsePointInExistingAncestors(incompleteBackupDirectory);
        ApplicationUpdatePathSafety.EnsureNoReparsePointInExistingAncestors(targetDirectory);
        ApplicationUpdatePathSafety.ValidateDirectoryTree(sourceDirectory);
        EnsureEntryIsMissing(finalBackupDirectory, "The rollback backup");
        EnsureEntryIsMissing(incompleteBackupDirectory, "The incomplete rollback backup");

        try
        {
            ApplicationUpdatePathSafety.EnsureNoReparsePointInExistingAncestors(incompleteBackupDirectory);
            Directory.CreateDirectory(incompleteBackupDirectory);

            var targetWasPresent = false;
            if (relocatesInstallFolder)
            {
                switch (GetEntryKind(targetDirectory))
                {
                    case EntryKind.Missing:
                        break;
                    case EntryKind.Directory:
                    {
                        targetWasPresent = true;
                        ApplicationUpdatePathSafety.ValidateDirectoryTree(targetDirectory);
                        var targetBackupDirectory = Path.Combine(
                            incompleteBackupDirectory,
                            TargetDirectoryName);
                        copyDirectoryContents(targetDirectory, targetBackupDirectory);
                        ApplicationUpdatePathSafety.ValidateCopiedDirectoryTree(
                            targetDirectory,
                            targetBackupDirectory);
                        validateBackupDirectory(targetBackupDirectory);
                        break;
                    }
                    case EntryKind.File:
                        throw new IOException("The existing relocation target is a file, not a directory.");
                    case EntryKind.ReparsePoint:
                        throw new IOException("The existing relocation target is a reparse point.");
                    default:
                        throw new IOException("The relocation target has an unsupported filesystem entry type.");
                }
            }
            else
            {
                var sourceBackupDirectory = Path.Combine(
                    incompleteBackupDirectory,
                    SourceDirectoryName);
                copyDirectoryContents(sourceDirectory, sourceBackupDirectory);
                ApplicationUpdatePathSafety.ValidateCopiedDirectoryTree(
                    sourceDirectory,
                    sourceBackupDirectory);
                validateBackupDirectory(sourceBackupDirectory);
            }

            if (relocatesInstallFolder)
            {
                var targetPresencePath = Path.Combine(
                    incompleteBackupDirectory,
                    TargetPresenceFileName);
                ApplicationUpdatePathSafety.EnsureNoReparsePointInExistingAncestors(targetPresencePath);
                File.WriteAllText(
                    targetPresencePath,
                    targetWasPresent ? TargetPresentText : TargetAbsentText);
            }

            ApplicationUpdatePathSafety.ValidateDirectoryTree(incompleteBackupDirectory);
            var completeMarkerPath = Path.Combine(incompleteBackupDirectory, CompleteMarkerFileName);
            ApplicationUpdatePathSafety.EnsureNoReparsePointInExistingAncestors(completeMarkerPath);
            File.WriteAllText(completeMarkerPath, CompleteMarkerText);
            ApplicationUpdatePathSafety.EnsureNoReparsePointInExistingAncestors(incompleteBackupDirectory);
            ApplicationUpdatePathSafety.EnsureNoReparsePointInExistingAncestors(finalBackupDirectory);
            Directory.Move(incompleteBackupDirectory, finalBackupDirectory);
        }
        catch
        {
            try
            {
                deleteDirectoryTree(incompleteBackupDirectory);
            }
            catch
            {
            }

            throw;
        }
    }

    internal static bool IsCompleteBackup(
        string backupDirectory,
        bool relocatesInstallFolder,
        Func<string, bool> validateBackupDirectory)
    {
        try
        {
            if (string.IsNullOrWhiteSpace(backupDirectory)
                || validateBackupDirectory is null)
            {
                return false;
            }

            var finalBackupDirectory = NormalizeDirectoryPath(backupDirectory);
            ApplicationUpdatePathSafety.EnsureNoReparsePointInExistingAncestors(finalBackupDirectory);
            if (finalBackupDirectory.EndsWith(
                    IncompleteDirectorySuffix,
                    StringComparison.OrdinalIgnoreCase)
                || GetEntryKind(finalBackupDirectory) != EntryKind.Directory)
            {
                return false;
            }

            ApplicationUpdatePathSafety.ValidateDirectoryTree(finalBackupDirectory);

            var markerPath = Path.Combine(finalBackupDirectory, CompleteMarkerFileName);
            ApplicationUpdatePathSafety.EnsureNoReparsePointInExistingAncestors(markerPath);
            if (GetEntryKind(markerPath) != EntryKind.File)
            {
                return false;
            }

            ApplicationUpdatePathSafety.EnsureNoReparsePointInExistingAncestors(markerPath);
            if (!string.Equals(
                    File.ReadAllText(markerPath),
                    CompleteMarkerText,
                    StringComparison.Ordinal))
            {
                return false;
            }

            if (!relocatesInstallFolder)
            {
                var sourceBackupDirectory = Path.Combine(
                    finalBackupDirectory,
                    SourceDirectoryName);
                ApplicationUpdatePathSafety.EnsureNoReparsePointInExistingAncestors(sourceBackupDirectory);
                return GetEntryKind(sourceBackupDirectory) == EntryKind.Directory
                    && validateBackupDirectory(sourceBackupDirectory);
            }

            var targetBackupDirectory = Path.Combine(finalBackupDirectory, TargetDirectoryName);
            ApplicationUpdatePathSafety.EnsureNoReparsePointInExistingAncestors(targetBackupDirectory);
            var targetWasPresent = GetRecordedTargetPresence(finalBackupDirectory);
            var targetBackupKind = GetEntryKind(targetBackupDirectory);
            if (targetWasPresent)
            {
                return targetBackupKind == EntryKind.Directory
                    && validateBackupDirectory(targetBackupDirectory);
            }

            return targetBackupKind == EntryKind.Missing;
        }
        catch
        {
            return false;
        }
    }

    internal static void WriteStartupAcknowledgement(string applyManifestPath, string version)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(version);

        var acknowledgementPath = GetStartupAcknowledgementPath(applyManifestPath);
        var acknowledgementDirectory = Path.GetDirectoryName(acknowledgementPath)
            ?? throw new IOException("The startup acknowledgement path is invalid.");
        ApplicationUpdatePathSafety.EnsureNoReparsePointInExistingAncestors(acknowledgementDirectory);
        Directory.CreateDirectory(acknowledgementDirectory);
        ApplicationUpdatePathSafety.EnsureNoReparsePointInExistingAncestors(acknowledgementDirectory);

        var temporaryPath = Path.Combine(
            acknowledgementDirectory,
            $".{Path.GetFileName(acknowledgementPath)}.{Guid.NewGuid():N}.tmp");
        try
        {
            ApplicationUpdatePathSafety.EnsureNoReparsePointInExistingAncestors(temporaryPath);
            File.WriteAllText(temporaryPath, version);
            ApplicationUpdatePathSafety.EnsureNoReparsePointInExistingAncestors(acknowledgementPath);
            File.Move(temporaryPath, acknowledgementPath, overwrite: true);
        }
        finally
        {
            try
            {
                if (File.Exists(temporaryPath))
                {
                    ApplicationUpdatePathSafety.EnsureNoReparsePointInExistingAncestors(temporaryPath);
                    File.Delete(temporaryPath);
                }
            }
            catch
            {
            }
        }
    }

    internal static async Task WaitForStartupAcknowledgementAsync(
        string applyManifestPath,
        string version,
        TimeSpan timeout,
        CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(version);
        if (timeout < TimeSpan.Zero && timeout != Timeout.InfiniteTimeSpan)
        {
            throw new ArgumentOutOfRangeException(nameof(timeout));
        }

        var acknowledgementPath = GetStartupAcknowledgementPath(applyManifestPath);
        var stopwatch = Stopwatch.StartNew();
        var hasTimeout = timeout != Timeout.InfiniteTimeSpan;

        while (true)
        {
            cancellationToken.ThrowIfCancellationRequested();

            try
            {
                if (GetEntryKind(acknowledgementPath) == EntryKind.File)
                {
                    ApplicationUpdatePathSafety.EnsureNoReparsePointInExistingAncestors(acknowledgementPath);
                    if (string.Equals(
                            File.ReadAllText(acknowledgementPath),
                            version,
                            StringComparison.Ordinal))
                    {
                        return;
                    }
                }
            }
            catch (IOException)
            {
            }
            catch (UnauthorizedAccessException)
            {
            }

            if (hasTimeout && stopwatch.Elapsed >= timeout)
            {
                throw new TimeoutException("The Crystal Relay startup acknowledgement was not received.");
            }

            var delay = AcknowledgementPollInterval;
            if (hasTimeout)
            {
                var remaining = timeout - stopwatch.Elapsed;
                if (remaining <= TimeSpan.Zero)
                {
                    throw new TimeoutException("The Crystal Relay startup acknowledgement was not received.");
                }

                if (remaining < delay)
                {
                    delay = remaining;
                }
            }

            await Task.Delay(delay, cancellationToken);
        }
    }

    private static string NormalizeDirectoryPath(string path)
    {
        var fullPath = Path.GetFullPath(path);
        var root = Path.GetPathRoot(fullPath);
        return string.Equals(fullPath, root, StringComparison.OrdinalIgnoreCase)
            ? fullPath
            : fullPath.TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
    }

    private static void EnsureEntryIsMissing(string path, string description)
    {
        switch (GetEntryKind(path))
        {
            case EntryKind.Missing:
                return;
            case EntryKind.Directory:
                throw new IOException($"{description} already exists as a directory.");
            case EntryKind.File:
                throw new IOException($"{description} already exists as a file.");
            case EntryKind.ReparsePoint:
                throw new IOException($"{description} is a reparse point.");
            default:
                throw new IOException($"{description} has an unsupported filesystem entry type.");
        }
    }

    private static EntryKind GetEntryKind(string path)
    {
        try
        {
            var attributes = File.GetAttributes(path);
            if ((attributes & FileAttributes.ReparsePoint) != 0)
            {
                return EntryKind.ReparsePoint;
            }

            return (attributes & FileAttributes.Directory) != 0
                ? EntryKind.Directory
                : EntryKind.File;
        }
        catch (FileNotFoundException)
        {
            return EntryKind.Missing;
        }
        catch (DirectoryNotFoundException)
        {
            return EntryKind.Missing;
        }
    }

    private enum EntryKind
    {
        Missing,
        Directory,
        File,
        ReparsePoint
    }
}

internal static class ApplicationUpdateApplyTransaction
{
    internal static async Task RunAsync(
        Func<Task> prepareBackupAsync,
        Func<Task> replaceInstallAsync,
        Func<Task> launchInstalledApplicationAsync,
        Func<Task> waitForAcknowledgementAsync,
        Func<Task> terminateLaunchedApplicationAsync,
        Func<Task> restoreBackupAsync,
        Func<Task> relaunchRestoredApplicationAsync,
        Func<Task> cleanupAfterAcknowledgementAsync,
        Func<Task>? validateBeforeBackupAsync = null)
    {
        ArgumentNullException.ThrowIfNull(prepareBackupAsync);
        ArgumentNullException.ThrowIfNull(replaceInstallAsync);
        ArgumentNullException.ThrowIfNull(launchInstalledApplicationAsync);
        ArgumentNullException.ThrowIfNull(waitForAcknowledgementAsync);
        ArgumentNullException.ThrowIfNull(terminateLaunchedApplicationAsync);
        ArgumentNullException.ThrowIfNull(restoreBackupAsync);
        ArgumentNullException.ThrowIfNull(relaunchRestoredApplicationAsync);
        ArgumentNullException.ThrowIfNull(cleanupAfterAcknowledgementAsync);

        if (validateBeforeBackupAsync is not null)
        {
            await validateBeforeBackupAsync();
        }

        await prepareBackupAsync();

        var launchAttempted = false;
        try
        {
            await replaceInstallAsync();
            launchAttempted = true;
            await launchInstalledApplicationAsync();
            await waitForAcknowledgementAsync();
        }
        catch (Exception originalException)
        {
            var recoveryExceptions = new List<Exception>();
            if (launchAttempted)
            {
                if (!await TryRunRecoveryPhaseAsync(terminateLaunchedApplicationAsync, recoveryExceptions))
                {
                    throw new ApplicationUpdateRollbackException(originalException, recoveryExceptions);
                }
            }

            if (!await TryRunRecoveryPhaseAsync(restoreBackupAsync, recoveryExceptions))
            {
                throw new ApplicationUpdateRollbackException(originalException, recoveryExceptions);
            }

            await TryRunRecoveryPhaseAsync(relaunchRestoredApplicationAsync, recoveryExceptions);

            if (recoveryExceptions.Count > 0)
            {
                throw new ApplicationUpdateRollbackException(originalException, recoveryExceptions);
            }

            ExceptionDispatchInfo.Capture(originalException).Throw();
        }

        await cleanupAfterAcknowledgementAsync();
    }

    private static async Task<bool> TryRunRecoveryPhaseAsync(
        Func<Task> operation,
        ICollection<Exception> recoveryExceptions)
    {
        try
        {
            await operation();
            return true;
        }
        catch (Exception exception)
        {
            recoveryExceptions.Add(exception);
            return false;
        }
    }
}
