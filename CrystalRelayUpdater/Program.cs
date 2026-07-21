using System.Diagnostics;
using System.Text.Json;
using System.Text.Json.Serialization;
using VrcTwitchOscBridge.Services;

namespace CrystalRelayUpdater;

internal static class Program
{
    private const string ApplyUpdateArgument = "--crystal-relay-apply-update";
    private const string CleanupUpdateArgument = "--crystal-relay-update-cleanup";
    private const string PackageManifestFileName = "crystal-relay-update.json";
    private const string ProductName = "Crystal Relay";
    private const string RuntimeName = "win-x64";
    private const string SourceBackupFolderName = "source";
    private const string TargetBackupFolderName = "target";
    private const int FileOperationRetryCount = 20;
    private static readonly TimeSpan ProcessExitTimeout = TimeSpan.FromSeconds(75);
    private static readonly TimeSpan FileOperationRetryDelay = TimeSpan.FromMilliseconds(500);
    public static async Task<int> Main(string[] args)
    {
        if (!TryGetApplyManifestPath(args, out var manifestPath))
        {
            return 2;
        }

        await ApplyUpdateAsync(manifestPath);
        return 0;
    }

    private static bool TryGetApplyManifestPath(string[] args, out string manifestPath)
    {
        manifestPath = string.Empty;
        for (var index = 0; index < args.Length; index++)
        {
            if (!string.Equals(args[index], ApplyUpdateArgument, StringComparison.OrdinalIgnoreCase))
            {
                continue;
            }

            if (index + 1 >= args.Length || string.IsNullOrWhiteSpace(args[index + 1]))
            {
                return false;
            }

            manifestPath = args[index + 1];
            return true;
        }

        return false;
    }

    private static async Task ApplyUpdateAsync(string manifestPath)
    {
        ApplicationUpdateApplyManifest? manifest = null;
        UpdateInstallPlan? installPlan = null;
        try
        {
            manifest = await ReadApplyManifestAsync(manifestPath);
            WriteUpdateLog($"Dedicated updater applying {manifest.Version} from '{manifest.PackageRoot}' to '{manifest.InstallDirectory}'.");

            ValidateApplyManifest(manifest);
            installPlan = CreateInstallPlan(manifest);
            ValidateInstallPlan(installPlan);
            await WaitForProcessExitAsync(manifest.SourceProcessId, ProcessExitTimeout);

            PrepareRollbackBackup(manifest, installPlan);
            ReplaceInstallFiles(manifest, installPlan);

            var installedEntryPath = Path.Combine(installPlan.TargetDirectory, manifest.EntryExecutableName);
            if (!File.Exists(installedEntryPath))
            {
                installedEntryPath = ResolveSingleExecutable(installPlan.TargetDirectory);
            }

            TryDeleteSuccessfulUpdateBackup(manifest.BackupDirectory);
            TryDeletePreviousInstallDirectory(installPlan);
            LaunchInstalledApplication(installedEntryPath, manifestPath);

            WriteUpdateLog(
                installPlan.RelocatesInstallFolder
                    ? $"Dedicated updater applied {manifest.Version} successfully to '{installPlan.TargetDirectory}'."
                    : $"Dedicated updater applied {manifest.Version} successfully.");
        }
        catch (Exception ex)
        {
            WriteUpdateLog($"Dedicated updater failed: {ex}");
            if (manifest is not null && installPlan is not null)
            {
                TryRestoreBackup(manifest, installPlan);
                TryLaunchRestoredApplication(manifest, installPlan);
            }
        }
    }

    private static async Task<ApplicationUpdateApplyManifest> ReadApplyManifestAsync(string manifestPath)
    {
        var fullManifestPath = Path.GetFullPath(manifestPath);
        var json = await File.ReadAllTextAsync(fullManifestPath);
        return JsonSerializer.Deserialize(json, UpdaterJsonContext.Default.ApplicationUpdateApplyManifest)
            ?? throw new UpdaterException("The Crystal Relay update apply manifest could not be read.");
    }

    private static void ValidateApplyManifest(ApplicationUpdateApplyManifest manifest)
    {
        if (!string.Equals(manifest.ProductName, ProductName, StringComparison.Ordinal)
            || string.IsNullOrWhiteSpace(manifest.Version)
            || string.IsNullOrWhiteSpace(manifest.EntryExecutableName)
            || string.IsNullOrWhiteSpace(manifest.PackageRoot)
            || string.IsNullOrWhiteSpace(manifest.InstallDirectory)
            || string.IsNullOrWhiteSpace(manifest.BackupDirectory))
        {
            throw new UpdaterException("The Crystal Relay update apply manifest is incomplete.");
        }

        if (!string.Equals(manifest.Runtime, RuntimeName, StringComparison.OrdinalIgnoreCase)
            || !ApplicationUpdatePackageRules.TryParseManifestChannel(manifest.Channel, out var manifestChannel)
            || !ApplicationUpdatePackageRules.IsExpectedEntryExecutableName(
                manifestChannel,
                manifest.Version,
                manifest.EntryExecutableName))
        {
            throw new UpdaterException("The Crystal Relay update apply manifest channel or entry executable is invalid.");
        }

        var packageRoot = NormalizeDirectoryPath(manifest.PackageRoot);
        var installDirectory = NormalizeDirectoryPath(manifest.InstallDirectory);
        var backupDirectory = NormalizeDirectoryPath(manifest.BackupDirectory);
        if (!Directory.Exists(packageRoot))
        {
            throw new UpdaterException("The staged Crystal Relay update folder is missing.");
        }

        var updateRoot = NormalizeDirectoryPath(UpdatesFolder);
        if (!IsPathInside(updateRoot, packageRoot))
        {
            throw new UpdaterException("The staged Crystal Relay update folder is outside updater storage.");
        }

        ValidateBuildMarker(packageRoot, manifestChannel, manifest.Version);

        ValidateInstallDirectory(installDirectory);

        var backupRoot = NormalizeDirectoryPath(UpdateBackupsFolder);
        if (!IsPathInside(backupRoot, backupDirectory))
        {
            throw new UpdaterException("The update backup folder is outside Crystal Relay's updater storage.");
        }
    }

    private static void ValidateBuildMarker(
        string packageRoot,
        ApplicationUpdateChannel channel,
        string version)
    {
        var expectedMarker = ApplicationUpdatePackageRules.GetExpectedBuildMarker(channel, version);
        if (channel != ApplicationUpdateChannel.BugFix)
        {
            return;
        }

        var markerPath = Path.Combine(packageRoot, "bugfix-build.flag");
        var markerText = File.Exists(markerPath) ? File.ReadAllText(markerPath) : null;
        if (string.IsNullOrWhiteSpace(expectedMarker)
            || !ApplicationUpdatePackageRules.IsExpectedBuildMarker(channel, version, markerText))
        {
            throw new UpdaterException("The Bug Fix update package marker is missing or invalid.");
        }
    }

    private static void ValidateInstallDirectory(string installDirectory)
    {
        ValidatePotentialInstallDirectory(installDirectory);

        if (!Directory.Exists(installDirectory))
        {
            throw new UpdaterException("The Crystal Relay install folder could not be found.");
        }
    }

    private static void ValidatePotentialInstallDirectory(string installDirectory)
    {
        var normalizedInstallDirectory = NormalizeDirectoryPath(installDirectory);
        var root = Path.GetPathRoot(normalizedInstallDirectory);
        if (string.IsNullOrWhiteSpace(root)
            || string.Equals(NormalizeDirectoryPath(root), normalizedInstallDirectory, StringComparison.OrdinalIgnoreCase))
        {
            throw new UpdaterException("Crystal Relay refused to update because the install folder path is unsafe.");
        }

        var runtimeDataRoot = NormalizeDirectoryPath(RootFolder);
        if (IsPathInside(runtimeDataRoot, normalizedInstallDirectory))
        {
            throw new UpdaterException("Crystal Relay refused to replace files inside its runtime data folder.");
        }

        var parentDirectory = Path.GetDirectoryName(normalizedInstallDirectory);
        if (string.IsNullOrWhiteSpace(parentDirectory) || !Directory.Exists(parentDirectory))
        {
            throw new UpdaterException("The Crystal Relay install parent folder could not be found.");
        }
    }

    private static async Task WaitForProcessExitAsync(int processId, TimeSpan timeout)
    {
        if (processId <= 0 || processId == Environment.ProcessId)
        {
            return;
        }

        try
        {
            using var process = Process.GetProcessById(processId);
            using var timeoutCancellation = new CancellationTokenSource(timeout);
            await process.WaitForExitAsync(timeoutCancellation.Token);
        }
        catch (ArgumentException)
        {
        }
        catch (OperationCanceledException)
        {
            throw new UpdaterException("Crystal Relay could not close in time to apply the update.");
        }
    }

    private static void LaunchInstalledApplication(string entryExecutablePath, string applyManifestPath)
    {
        var startInfo = new ProcessStartInfo
        {
            FileName = entryExecutablePath,
            UseShellExecute = true,
            WorkingDirectory = Path.GetDirectoryName(entryExecutablePath)
        };
        startInfo.ArgumentList.Add(CleanupUpdateArgument);
        startInfo.ArgumentList.Add(applyManifestPath);
        startInfo.ArgumentList.Add(Environment.ProcessId.ToString());
        Process.Start(startInfo);
    }

    private static UpdateInstallPlan CreateInstallPlan(ApplicationUpdateApplyManifest manifest)
    {
        var sourceDirectory = NormalizeDirectoryPath(manifest.InstallDirectory);
        var packageRoot = NormalizeDirectoryPath(manifest.PackageRoot);

        if (!ApplicationUpdatePackageRules.TryParseManifestChannel(manifest.Channel, out var channel))
        {
            throw new UpdaterException("The Crystal Relay update apply manifest channel is invalid.");
        }

        var targetDirectory = NormalizeDirectoryPath(
            ApplicationUpdatePackageRules.GetInstallTargetDirectory(
                channel,
                sourceDirectory,
                packageRoot));

        return new UpdateInstallPlan(
            sourceDirectory,
            targetDirectory,
            Path.Combine(NormalizeDirectoryPath(manifest.BackupDirectory), SourceBackupFolderName),
            Path.Combine(NormalizeDirectoryPath(manifest.BackupDirectory), TargetBackupFolderName),
            !AreSamePath(sourceDirectory, targetDirectory));
    }

    private static void ValidateInstallPlan(UpdateInstallPlan installPlan)
    {
        ValidatePotentialInstallDirectory(installPlan.TargetDirectory);

        if (!installPlan.RelocatesInstallFolder)
        {
            return;
        }

        var sourceParent = Path.GetDirectoryName(installPlan.SourceDirectory)
            ?? throw new UpdaterException("The Crystal Relay install folder path is invalid.");
        var targetParent = Path.GetDirectoryName(installPlan.TargetDirectory)
            ?? throw new UpdaterException("The Crystal Relay install target path is invalid.");
        if (!AreSamePath(sourceParent, targetParent))
        {
            throw new UpdaterException("Crystal Relay refused to move the install folder outside its current parent folder.");
        }

        if (!IsPackageInstallFolderName(Path.GetFileName(installPlan.SourceDirectory))
            || !IsPackageInstallFolderName(Path.GetFileName(installPlan.TargetDirectory)))
        {
            throw new UpdaterException("Crystal Relay refused to rename a folder that is not a Crystal Relay package folder.");
        }

        if (Directory.Exists(installPlan.TargetDirectory))
        {
            ValidatePackageInstallDirectory(installPlan.TargetDirectory, "The existing update target folder");
        }
    }

    private static void PrepareRollbackBackup(ApplicationUpdateApplyManifest manifest, UpdateInstallPlan installPlan)
    {
        Directory.CreateDirectory(manifest.BackupDirectory);

        if (installPlan.RelocatesInstallFolder)
        {
            if (Directory.Exists(installPlan.TargetDirectory))
            {
                ValidatePackageInstallDirectory(installPlan.TargetDirectory, "The existing update target folder");
                CopyDirectoryContents(installPlan.TargetDirectory, installPlan.TargetBackupDirectory);
            }

            return;
        }

        CopyDirectoryContents(installPlan.SourceDirectory, installPlan.SourceBackupDirectory);
    }

    private static void ReplaceInstallFiles(ApplicationUpdateApplyManifest manifest, UpdateInstallPlan installPlan)
    {
        if (!installPlan.RelocatesInstallFolder)
        {
            ClearDirectoryContents(installPlan.SourceDirectory);
            CopyDirectoryContents(manifest.PackageRoot, installPlan.TargetDirectory);
            return;
        }

        if (Directory.Exists(installPlan.TargetDirectory))
        {
            ClearDirectoryContents(installPlan.TargetDirectory);
        }
        else
        {
            Directory.CreateDirectory(installPlan.TargetDirectory);
        }

        CopyDirectoryContents(manifest.PackageRoot, installPlan.TargetDirectory);
    }

    private static void TryLaunchRestoredApplication(ApplicationUpdateApplyManifest manifest, UpdateInstallPlan installPlan)
    {
        try
        {
            var restoredExecutable = !string.IsNullOrWhiteSpace(manifest.PreviousEntryExecutableName)
                ? Path.Combine(installPlan.SourceDirectory, manifest.PreviousEntryExecutableName)
                : ResolveSingleExecutable(installPlan.SourceDirectory);
            if (!File.Exists(restoredExecutable))
            {
                restoredExecutable = ResolveSingleExecutable(installPlan.SourceDirectory);
            }

            Process.Start(new ProcessStartInfo
            {
                FileName = restoredExecutable,
                UseShellExecute = true,
                WorkingDirectory = Path.GetDirectoryName(restoredExecutable)
            });
        }
        catch (Exception ex)
        {
            WriteUpdateLog($"Could not relaunch restored Crystal Relay: {ex.Message}");
        }
    }

    private static void TryRestoreBackup(ApplicationUpdateApplyManifest manifest, UpdateInstallPlan installPlan)
    {
        try
        {
            if (!Directory.Exists(manifest.BackupDirectory))
            {
                return;
            }

            if (installPlan.RelocatesInstallFolder)
            {
                RestoreRelocatedTarget(installPlan);
                return;
            }

            RestoreDirectoryBackup(installPlan.SourceBackupDirectory, installPlan.SourceDirectory);
            WriteUpdateLog("Restored previous Crystal Relay files from update backup.");
        }
        catch (Exception ex)
        {
            WriteUpdateLog($"Could not restore update backup: {ex}");
        }
    }

    private static void RestoreRelocatedTarget(UpdateInstallPlan installPlan)
    {
        if (Directory.Exists(installPlan.TargetBackupDirectory))
        {
            RestoreDirectoryBackup(installPlan.TargetBackupDirectory, installPlan.TargetDirectory);
            WriteUpdateLog("Restored previous Crystal Relay target files from update backup.");
            return;
        }

        if (Directory.Exists(installPlan.TargetDirectory))
        {
            DeleteDirectoryTree(installPlan.TargetDirectory);
            WriteUpdateLog("Removed partial Crystal Relay update target folder.");
        }
    }

    private static void RestoreDirectoryBackup(string backupDirectory, string targetDirectory)
    {
        if (!Directory.Exists(backupDirectory))
        {
            return;
        }

        Directory.CreateDirectory(targetDirectory);
        ClearDirectoryContents(targetDirectory);
        CopyDirectoryContents(backupDirectory, targetDirectory);
    }

    private static void CopyDirectoryContents(string sourceDirectory, string destinationDirectory)
    {
        Directory.CreateDirectory(destinationDirectory);
        foreach (var filePath in Directory.EnumerateFiles(sourceDirectory))
        {
            CopyFileWithRetry(filePath, Path.Combine(destinationDirectory, Path.GetFileName(filePath)), overwrite: true);
        }

        foreach (var sourceSubDirectory in Directory.EnumerateDirectories(sourceDirectory))
        {
            var destinationSubDirectory = Path.Combine(destinationDirectory, Path.GetFileName(sourceSubDirectory));
            CopyDirectoryContents(sourceSubDirectory, destinationSubDirectory);
        }
    }

    private static void ClearDirectoryContents(string directoryPath)
    {
        foreach (var filePath in Directory.EnumerateFiles(directoryPath))
        {
            DeleteFileWithRetry(filePath);
        }

        foreach (var subDirectoryPath in Directory.EnumerateDirectories(directoryPath))
        {
            DeleteDirectoryTree(subDirectoryPath);
        }
    }

    private static void DeleteDirectoryTree(string directoryPath)
    {
        if (!Directory.Exists(directoryPath))
        {
            return;
        }

        foreach (var filePath in Directory.EnumerateFiles(directoryPath, "*", SearchOption.AllDirectories))
        {
            SetAttributesNormalWithRetry(filePath);
        }

        foreach (var subDirectoryPath in Directory.EnumerateDirectories(directoryPath, "*", SearchOption.AllDirectories))
        {
            SetAttributesNormalWithRetry(subDirectoryPath);
        }

        SetAttributesNormalWithRetry(directoryPath);
        DeleteDirectoryWithRetry(directoryPath);
    }

    private static void CopyFileWithRetry(string sourcePath, string destinationPath, bool overwrite)
    {
        ExecuteFileOperationWithRetry(
            () => File.Copy(sourcePath, destinationPath, overwrite),
            $"copy '{sourcePath}' to '{destinationPath}'");
    }

    private static void DeleteFileWithRetry(string filePath)
    {
        ExecuteFileOperationWithRetry(
            () =>
            {
                File.SetAttributes(filePath, FileAttributes.Normal);
                File.Delete(filePath);
            },
            $"delete '{filePath}'");
    }

    private static void DeleteDirectoryWithRetry(string directoryPath)
    {
        ExecuteFileOperationWithRetry(
            () => Directory.Delete(directoryPath, recursive: true),
            $"delete folder '{directoryPath}'");
    }

    private static void SetAttributesNormalWithRetry(string path)
    {
        ExecuteFileOperationWithRetry(
            () => File.SetAttributes(path, FileAttributes.Normal),
            $"prepare '{path}' for cleanup");
    }

    private static void ExecuteFileOperationWithRetry(Action action, string description)
    {
        for (var attempt = 1; attempt <= FileOperationRetryCount; attempt++)
        {
            try
            {
                action();
                return;
            }
            catch (Exception ex) when (IsRetriableFileOperationException(ex) && attempt < FileOperationRetryCount)
            {
                Thread.Sleep(FileOperationRetryDelay);
            }
            catch (Exception ex) when (IsRetriableFileOperationException(ex))
            {
                throw new UpdaterException(
                    $"Crystal Relay could not {description} because the file stayed busy.",
                    ex);
            }
        }
    }

    private static bool IsRetriableFileOperationException(Exception ex) =>
        ex is IOException or UnauthorizedAccessException;

    private static string ResolveSingleExecutable(string root)
    {
        var executables = Directory.GetFiles(root, "*.exe", SearchOption.AllDirectories)
            .Where(path => ApplicationUpdatePackageRules.IsApplicationExecutableName(Path.GetFileName(path)))
            .ToArray();
        return executables.Length == 1
            ? executables[0]
            : throw new UpdaterException("The update package must contain exactly one Crystal Relay executable.");
    }

    private static void TryDeleteSuccessfulUpdateBackup(string backupDirectory)
    {
        try
        {
            var backupRoot = NormalizeDirectoryPath(UpdateBackupsFolder);
            var fullBackupDirectory = NormalizeDirectoryPath(backupDirectory);
            if (IsPathInside(backupRoot, fullBackupDirectory) && Directory.Exists(fullBackupDirectory))
            {
                DeleteDirectoryTree(fullBackupDirectory);
                WriteUpdateLog("Removed completed Crystal Relay update backup.");
            }
        }
        catch (Exception ex)
        {
            WriteUpdateLog($"Could not remove completed update backup: {ex.Message}");
        }
    }

    private static void TryDeletePreviousInstallDirectory(UpdateInstallPlan installPlan)
    {
        if (!installPlan.RelocatesInstallFolder || !Directory.Exists(installPlan.SourceDirectory))
        {
            return;
        }

        try
        {
            if (IsCurrentProcessInsideDirectory(installPlan.SourceDirectory))
            {
                WriteUpdateLog("Skipped old Crystal Relay install folder cleanup because it is still running from that folder.");
                return;
            }

            if (!TryValidatePackageInstallDirectory(installPlan.SourceDirectory, out var validationError))
            {
                WriteUpdateLog($"Skipped old Crystal Relay install folder cleanup: {validationError}");
                return;
            }

            DeleteDirectoryTree(installPlan.SourceDirectory);
            WriteUpdateLog($"Removed previous Crystal Relay install folder '{installPlan.SourceDirectory}'.");
        }
        catch (Exception ex)
        {
            WriteUpdateLog($"Could not remove previous Crystal Relay install folder: {ex.Message}");
        }
    }

    private static void ValidatePackageInstallDirectory(string directoryPath, string description)
    {
        if (!TryValidatePackageInstallDirectory(directoryPath, out var validationError))
        {
            throw new UpdaterException($"{description} is not a validated Crystal Relay install folder. {validationError}");
        }
    }

    private static bool TryValidatePackageInstallDirectory(string directoryPath, out string validationError)
    {
        validationError = string.Empty;
        var fullDirectoryPath = NormalizeDirectoryPath(directoryPath);
        if (!Directory.Exists(fullDirectoryPath))
        {
            validationError = "The folder does not exist.";
            return false;
        }

        if (!IsPackageInstallFolderName(Path.GetFileName(fullDirectoryPath)))
        {
            validationError = "The folder name does not match Crystal Relay's package format.";
            return false;
        }

        var manifestPath = Path.Combine(fullDirectoryPath, PackageManifestFileName);
        if (!File.Exists(manifestPath))
        {
            validationError = "The package manifest is missing.";
            return false;
        }

        ApplicationUpdatePackageManifest? manifest;
        try
        {
            manifest = JsonSerializer.Deserialize(
                File.ReadAllText(manifestPath),
                UpdaterJsonContext.Default.ApplicationUpdatePackageManifest);
        }
        catch (Exception ex)
        {
            validationError = $"The package manifest could not be read: {ex.Message}";
            return false;
        }

        if (manifest is null
            || !string.Equals(manifest.ProductName, ProductName, StringComparison.Ordinal)
            || !string.Equals(manifest.Runtime, RuntimeName, StringComparison.OrdinalIgnoreCase)
            || !ApplicationUpdatePackageRules.TryParseManifestChannel(manifest.Channel, out var channel)
            || string.IsNullOrWhiteSpace(manifest.EntryExecutableName)
            || Path.IsPathRooted(manifest.EntryExecutableName)
            || manifest.EntryExecutableName.Contains(Path.DirectorySeparatorChar)
            || manifest.EntryExecutableName.Contains(Path.AltDirectorySeparatorChar)
            || !ApplicationUpdatePackageRules.IsExpectedEntryExecutableName(
                channel,
                manifest.Version,
                manifest.EntryExecutableName))
        {
            validationError = "The package manifest is not a Crystal Relay install manifest.";
            return false;
        }

        if (channel == ApplicationUpdateChannel.BugFix)
        {
            var markerPath = Path.Combine(fullDirectoryPath, "bugfix-build.flag");
            var markerText = File.Exists(markerPath) ? File.ReadAllText(markerPath) : null;
            if (string.IsNullOrWhiteSpace(
                    ApplicationUpdatePackageRules.GetExpectedBuildMarker(channel, manifest.Version))
                || !ApplicationUpdatePackageRules.IsExpectedBuildMarker(channel, manifest.Version, markerText))
            {
                validationError = "The Bug Fix package marker is missing or invalid.";
                return false;
            }
        }

        var entryExecutablePath = Path.Combine(fullDirectoryPath, manifest.EntryExecutableName);
        if (!File.Exists(entryExecutablePath))
        {
            validationError = "The package entry executable is missing.";
            return false;
        }

        return true;
    }

    private static bool IsPackageInstallFolderName(string? folderName) =>
        ApplicationUpdatePackageRules.IsPackageInstallFolderName(folderName);

    private static string NormalizeDirectoryPath(string path)
    {
        var fullPath = Path.GetFullPath(path);
        var root = Path.GetPathRoot(fullPath);
        return string.Equals(fullPath, root, StringComparison.OrdinalIgnoreCase)
            ? fullPath
            : fullPath.TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
    }

    private static bool IsPathInside(string rootPath, string candidatePath)
    {
        var normalizedRoot = NormalizeDirectoryPath(rootPath);
        var normalizedCandidate = Path.GetFullPath(candidatePath);
        return string.Equals(normalizedRoot, normalizedCandidate, StringComparison.OrdinalIgnoreCase)
            || normalizedCandidate.StartsWith(
                normalizedRoot + Path.DirectorySeparatorChar,
                StringComparison.OrdinalIgnoreCase);
    }

    private static bool AreSamePath(string left, string right) =>
        string.Equals(NormalizeDirectoryPath(left), NormalizeDirectoryPath(right), StringComparison.OrdinalIgnoreCase);

    private static bool IsCurrentProcessInsideDirectory(string directoryPath)
    {
        var currentBaseDirectory = NormalizeDirectoryPath(AppContext.BaseDirectory);
        return IsPathInside(directoryPath, currentBaseDirectory);
    }

    private static void WriteUpdateLog(string message)
    {
        try
        {
            Directory.CreateDirectory(UpdatesFolder);
            File.AppendAllText(
                Path.Combine(UpdatesFolder, "update.log"),
                $"[{DateTimeOffset.Now:O}] {message}{Environment.NewLine}");
        }
        catch
        {
        }
    }

    private static string LocalAppDataFolder =>
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData);

    private static string RootFolder => Path.Combine(LocalAppDataFolder, "CrystalRelay");

    private static string UpdatesFolder => Path.Combine(RootFolder, "Updates");

    private static string UpdateBackupsFolder => Path.Combine(RootFolder, "UpdateBackups");

    private sealed record UpdateInstallPlan(
        string SourceDirectory,
        string TargetDirectory,
        string SourceBackupDirectory,
        string TargetBackupDirectory,
        bool RelocatesInstallFolder);

    private sealed class UpdaterException(string message, Exception? innerException = null)
        : Exception(message, innerException);
}

internal sealed record ApplicationUpdatePackageManifest(
    string ProductName,
    string Version,
    string Channel,
    string Runtime,
    string EntryExecutableName);

internal sealed record ApplicationUpdateApplyManifest(
    string ProductName,
    string Version,
    string Channel,
    string Runtime,
    string EntryExecutableName,
    string PackageRoot,
    string InstallDirectory,
    string BackupDirectory,
    string ReleasePageUrl,
    int SourceProcessId,
    string PreviousEntryExecutableName);

[JsonSourceGenerationOptions(PropertyNameCaseInsensitive = true, WriteIndented = true)]
[JsonSerializable(typeof(ApplicationUpdatePackageManifest))]
[JsonSerializable(typeof(ApplicationUpdateApplyManifest))]
internal sealed partial class UpdaterJsonContext : JsonSerializerContext;
