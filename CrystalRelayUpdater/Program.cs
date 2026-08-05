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
    private static readonly TimeSpan StartupAcknowledgementTimeout = ProcessExitTimeout;
    private static readonly TimeSpan ProcessTerminationTimeout = TimeSpan.FromSeconds(5);
    private static readonly TimeSpan FileOperationRetryDelay = TimeSpan.FromMilliseconds(500);
    public static async Task<int> Main(string[] args)
    {
        if (!TryGetApplyManifestPath(args, out var manifestPath))
        {
            return 2;
        }

        return await ApplyUpdateAsync(manifestPath);
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

    private static async Task<int> ApplyUpdateAsync(string manifestPath)
    {
        ApplicationUpdateApplyManifest? manifest = null;
        UpdateInstallPlan? installPlan = null;
        try
        {
            ValidateApplyManifestPath(manifestPath);
            manifest = await ReadApplyManifestAsync(manifestPath);
            WriteUpdateLog($"Dedicated updater applying {manifest.Version} from '{manifest.PackageRoot}' to '{manifest.InstallDirectory}'.");

            ValidateApplyManifest(manifest);
            ApplicationUpdateRollback.ClearStaleStartupAcknowledgement(manifestPath);
            installPlan = CreateInstallPlan(manifest);
            ValidateInstallPlan(installPlan);
            await WaitForProcessExitAsync(manifest.SourceProcessId, ProcessExitTimeout);

            var installedEntryPath = Path.Combine(installPlan.TargetDirectory, manifest.EntryExecutableName);
            Process? launchedProcess = null;
            await ApplicationUpdateApplyTransaction.RunAsync(
                prepareBackupAsync: () =>
                {
                    PrepareRollbackBackup(manifest, installPlan);
                    return Task.CompletedTask;
                },
                replaceInstallAsync: () =>
                {
                    ReplaceInstallFiles(manifest, installPlan);
                    return Task.CompletedTask;
                },
                launchInstalledApplicationAsync: () =>
                {
                    launchedProcess = LaunchInstalledApplication(installedEntryPath, manifestPath);
                    return Task.CompletedTask;
                },
                waitForAcknowledgementAsync: () =>
                    ApplicationUpdateRollback.WaitForStartupAcknowledgementAsync(
                        manifestPath,
                        manifest.Version,
                        StartupAcknowledgementTimeout),
                terminateLaunchedApplicationAsync: () =>
                {
                    TryTerminateProcess(launchedProcess);
                    return Task.CompletedTask;
                },
                restoreBackupAsync: () =>
                {
                    RestoreBackup(manifest, installPlan);
                    return Task.CompletedTask;
                },
                relaunchRestoredApplicationAsync: () =>
                {
                    LaunchRestoredApplication(manifest, installPlan);
                    return Task.CompletedTask;
                },
                cleanupAfterAcknowledgementAsync: () =>
                {
                    if (!TryDeleteSuccessfulUpdateBackup(manifest.BackupDirectory)
                        || !TryDeletePreviousInstallDirectory(installPlan))
                    {
                        throw new UpdaterException(
                            "Crystal Relay applied the update, but could not finish post-acknowledgement cleanup.");
                    }

                    return Task.CompletedTask;
                });

            WriteUpdateLog(
                installPlan.RelocatesInstallFolder
                    ? $"Dedicated updater applied {manifest.Version} successfully to '{installPlan.TargetDirectory}'."
                    : $"Dedicated updater applied {manifest.Version} successfully.");
            return 0;
        }
        catch (Exception ex)
        {
            WriteUpdateLog($"Dedicated updater failed: {ex}");
            return 1;
        }
    }

    private static async Task<ApplicationUpdateApplyManifest> ReadApplyManifestAsync(string manifestPath)
    {
        var fullManifestPath = Path.GetFullPath(manifestPath);
        var json = await File.ReadAllTextAsync(fullManifestPath);
        return JsonSerializer.Deserialize(json, UpdaterJsonContext.Default.ApplicationUpdateApplyManifest)
            ?? throw new UpdaterException("The Crystal Relay update apply manifest could not be read.");
    }

    private static void ValidateApplyManifestPath(string manifestPath)
    {
        var fullManifestPath = Path.GetFullPath(manifestPath);
        ApplicationUpdatePathSafety.EnsureNoReparsePointInExistingAncestors(fullManifestPath);

        var updateRoot = NormalizeDirectoryPath(UpdatesFolder);
        var sessionRoot = Path.GetDirectoryName(fullManifestPath);
        if (string.IsNullOrWhiteSpace(sessionRoot)
            || AreSamePath(updateRoot, sessionRoot)
            || !IsPathInside(updateRoot, sessionRoot))
        {
            throw new UpdaterException("The Crystal Relay update apply manifest is outside updater storage.");
        }
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
            || Path.IsPathRooted(manifest.EntryExecutableName)
            || manifest.EntryExecutableName.Contains(Path.DirectorySeparatorChar)
            || manifest.EntryExecutableName.Contains(Path.AltDirectorySeparatorChar)
            || !ApplicationUpdatePackageRules.IsExpectedInstalledPackageEntryExecutableName(
                manifestChannel,
                manifest.Version,
                manifest.EntryExecutableName))
        {
            throw new UpdaterException("The Crystal Relay update apply manifest channel or entry executable is invalid.");
        }

        if (!string.IsNullOrWhiteSpace(manifest.PreviousEntryExecutableName)
            && (Path.IsPathRooted(manifest.PreviousEntryExecutableName)
                || manifest.PreviousEntryExecutableName.Contains(Path.DirectorySeparatorChar)
                || manifest.PreviousEntryExecutableName.Contains(Path.AltDirectorySeparatorChar)
                || !ApplicationUpdatePackageRules.IsApplicationExecutableName(manifest.PreviousEntryExecutableName)))
        {
            throw new UpdaterException("The Crystal Relay restored entry executable is invalid.");
        }

        var packageRoot = NormalizeDirectoryPath(manifest.PackageRoot);
        var installDirectory = NormalizeDirectoryPath(manifest.InstallDirectory);
        var backupDirectory = NormalizeDirectoryPath(manifest.BackupDirectory);
        ApplicationUpdatePathSafety.EnsureNoReparsePointInExistingAncestors(packageRoot);
        ApplicationUpdatePathSafety.EnsureNoReparsePointInExistingAncestors(installDirectory);
        ApplicationUpdatePathSafety.EnsureNoReparsePointInExistingAncestors(backupDirectory);
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
        ValidateStagedPackage(packageRoot, manifest, manifestChannel);

        ValidateInstallDirectory(installDirectory);

        var backupRoot = NormalizeDirectoryPath(UpdateBackupsFolder);
        if (AreSamePath(backupRoot, backupDirectory)
            || !IsPathInside(backupRoot, backupDirectory))
        {
            throw new UpdaterException("The update backup folder is outside Crystal Relay's updater storage.");
        }
    }

    private static void ValidateStagedPackage(
        string packageRoot,
        ApplicationUpdateApplyManifest applyManifest,
        ApplicationUpdateChannel applyChannel)
    {
        ApplicationUpdatePathSafety.EnsureNoReparsePointInExistingAncestors(packageRoot);
        ApplicationUpdatePathSafety.ValidateDirectoryTree(packageRoot);
        var entryExecutablePath = Path.Combine(packageRoot, applyManifest.EntryExecutableName);
        if (!File.Exists(entryExecutablePath))
        {
            throw new UpdaterException("The staged Crystal Relay update entry executable is missing.");
        }

        var packageManifestPath = Path.Combine(packageRoot, PackageManifestFileName);
        if (!File.Exists(packageManifestPath))
        {
            if (applyChannel == ApplicationUpdateChannel.BugFix)
            {
                throw new UpdaterException("The Bug Fix update package manifest is missing.");
            }

            return;
        }

        ApplicationUpdatePackageManifest? packageManifest;
        try
        {
            packageManifest = JsonSerializer.Deserialize(
                File.ReadAllText(packageManifestPath),
                UpdaterJsonContext.Default.ApplicationUpdatePackageManifest);
        }
        catch (Exception ex)
        {
            throw new UpdaterException(
                "The staged Crystal Relay package manifest could not be read.",
                ex);
        }

        if (packageManifest is null
            || !string.Equals(packageManifest.ProductName, applyManifest.ProductName, StringComparison.Ordinal)
            || string.IsNullOrWhiteSpace(packageManifest.Version)
            || !string.Equals(packageManifest.Runtime, applyManifest.Runtime, StringComparison.OrdinalIgnoreCase)
            || !string.Equals(packageManifest.Channel, applyManifest.Channel, StringComparison.OrdinalIgnoreCase)
            || !string.Equals(packageManifest.Version, applyManifest.Version, StringComparison.Ordinal)
            || !string.Equals(packageManifest.EntryExecutableName, applyManifest.EntryExecutableName, StringComparison.Ordinal))
        {
            throw new UpdaterException("The staged Crystal Relay package manifest does not match the apply manifest.");
        }

        if (!ApplicationUpdatePackageRules.TryParseManifestChannel(
                packageManifest.Channel,
                out var packageChannel)
            || packageChannel != applyChannel
            || Path.IsPathRooted(packageManifest.EntryExecutableName)
            || packageManifest.EntryExecutableName.Contains(Path.DirectorySeparatorChar)
            || packageManifest.EntryExecutableName.Contains(Path.AltDirectorySeparatorChar)
            || !ApplicationUpdatePackageRules.IsExpectedInstalledPackageEntryExecutableName(
                packageChannel,
                packageManifest.Version,
                packageManifest.EntryExecutableName))
        {
            throw new UpdaterException("The staged Crystal Relay package manifest is invalid.");
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
        ApplicationUpdatePathSafety.EnsureNoReparsePointInExistingAncestors(normalizedInstallDirectory);
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

        if (Directory.Exists(normalizedInstallDirectory))
        {
            ApplicationUpdatePathSafety.ValidateDirectoryTree(normalizedInstallDirectory);
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

    private static Process LaunchInstalledApplication(string entryExecutablePath, string applyManifestPath)
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
        return Process.Start(startInfo)
            ?? throw new UpdaterException("Crystal Relay updater could not launch the installed application.");
    }

    private static void TryTerminateProcess(Process? process)
    {
        if (process is null)
        {
            return;
        }

        try
        {
            ApplicationUpdateRollback.TerminateProcessTreeAndWait(process, ProcessTerminationTimeout);
        }
        catch (Exception ex)
        {
            WriteUpdateLog($"Could not terminate the failed Crystal Relay process: {ex.Message}");
            throw new UpdaterException(
                "Crystal Relay could not terminate the failed replacement process.",
                ex);
        }
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
            channel,
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
        ApplicationUpdateRollback.PrepareCompleteBackup(
            backupDirectory: manifest.BackupDirectory,
            sourceDirectory: installPlan.SourceDirectory,
            targetDirectory: installPlan.TargetDirectory,
            relocatesInstallFolder: installPlan.RelocatesInstallFolder,
            copyDirectoryContents: CopyDirectoryContents,
            validateBackupDirectory: directory =>
                ValidatePackageContents(directory),
            deleteDirectoryTree: DeleteDirectoryTree);
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

    private static Process LaunchRestoredApplication(
        ApplicationUpdateApplyManifest manifest,
        UpdateInstallPlan installPlan)
    {
        ApplicationUpdatePathSafety.ValidateDirectoryTree(installPlan.SourceDirectory);
        var restoredExecutable = !string.IsNullOrWhiteSpace(manifest.PreviousEntryExecutableName)
            ? Path.Combine(installPlan.SourceDirectory, manifest.PreviousEntryExecutableName)
            : ResolveSingleExecutable(installPlan.SourceDirectory);
        if (!IsPathInside(installPlan.SourceDirectory, restoredExecutable))
        {
            throw new UpdaterException("The restored Crystal Relay executable is outside the previous install folder.");
        }

        if (!File.Exists(restoredExecutable))
        {
            restoredExecutable = ResolveSingleExecutable(installPlan.SourceDirectory);
        }

        return Process.Start(new ProcessStartInfo
        {
            FileName = restoredExecutable,
            UseShellExecute = true,
            WorkingDirectory = Path.GetDirectoryName(restoredExecutable)
        }) ?? throw new UpdaterException("Crystal Relay updater could not relaunch the restored application.");
    }

    private static void RestoreBackup(ApplicationUpdateApplyManifest manifest, UpdateInstallPlan installPlan)
    {
        if (installPlan.RelocatesInstallFolder)
        {
            RestoreRelocatedTarget(manifest, installPlan);
            return;
        }

        if (!ApplicationUpdateRollback.IsCompleteBackup(
                manifest.BackupDirectory,
                relocatesInstallFolder: false,
                validateBackupDirectory: directory =>
                    IsValidPackageContents(directory)))
        {
            throw new UpdaterException(
                "The Crystal Relay rollback backup is incomplete or invalid; the current installation was left untouched.");
        }

        RestoreDirectoryBackup(installPlan.SourceBackupDirectory, installPlan.SourceDirectory);
        WriteUpdateLog("Restored previous Crystal Relay files from update backup.");
    }

    private static void RestoreRelocatedTarget(
        ApplicationUpdateApplyManifest manifest,
        UpdateInstallPlan installPlan)
    {
        if (!ApplicationUpdateRollback.IsCompleteBackup(
                manifest.BackupDirectory,
                relocatesInstallFolder: true,
                validateBackupDirectory: directory =>
                    IsValidPackageContents(directory)))
        {
            throw new UpdaterException(
                "The Crystal Relay rollback backup is incomplete or invalid; the current installation was left untouched.");
        }

        if (ApplicationUpdateRollback.GetRecordedTargetPresence(manifest.BackupDirectory))
        {
            if (ApplicationUpdateRollback.IsEntryMissing(installPlan.TargetBackupDirectory))
            {
                throw new UpdaterException(
                    "Missing target backup for a relocation target that was recorded as present.");
            }

            RestoreDirectoryBackup(installPlan.TargetBackupDirectory, installPlan.TargetDirectory);
            WriteUpdateLog("Restored previous Crystal Relay target files from update backup.");
        }
        else if (!ApplicationUpdateRollback.IsEntryMissing(installPlan.TargetBackupDirectory))
        {
            throw new UpdaterException(
                "The relocation target backup exists even though the target was recorded as absent.");
        }
        else
        {
            if (Directory.Exists(installPlan.TargetDirectory))
            {
                ApplicationUpdatePathSafety.EnsureNoReparsePointInExistingAncestors(installPlan.TargetDirectory);
                DeleteDirectoryTree(installPlan.TargetDirectory);
                if (!ApplicationUpdateRollback.IsEntryMissing(installPlan.TargetDirectory))
                {
                    throw new UpdaterException(
                        "Crystal Relay could not remove the newly-created relocation target folder.");
                }

                WriteUpdateLog("Removed partial Crystal Relay update target folder.");
            }
            else if (!ApplicationUpdateRollback.IsEntryMissing(installPlan.TargetDirectory))
            {
                throw new UpdaterException(
                    "The relocation target path is not a directory or a missing path.");
            }
        }

        ValidatePackageContents(installPlan.SourceDirectory);
    }

    private static void RestoreDirectoryBackup(string backupDirectory, string targetDirectory)
    {
        ApplicationUpdatePathSafety.EnsureNoReparsePointInExistingAncestors(backupDirectory);
        if (!Directory.Exists(backupDirectory))
        {
            throw new UpdaterException(
                "The rollback backup source is missing or is not a directory.");
        }

        ApplicationUpdatePathSafety.ValidateDirectoryTree(backupDirectory);
        ValidatePackageContents(backupDirectory);
        ApplicationUpdatePathSafety.EnsureNoReparsePointInExistingAncestors(targetDirectory);
        if (Directory.Exists(targetDirectory))
        {
            ApplicationUpdatePathSafety.ValidateDirectoryTree(targetDirectory);
            ClearDirectoryContents(targetDirectory);
        }
        else if (!ApplicationUpdateRollback.IsEntryMissing(targetDirectory))
        {
            throw new UpdaterException(
                "The rollback destination is not a directory or a missing path.");
        }

        ApplicationUpdatePathSafety.EnsureNoReparsePointInExistingAncestors(targetDirectory);
        Directory.CreateDirectory(targetDirectory);
        ApplicationUpdatePathSafety.ValidateDirectoryTree(targetDirectory);
        CopyDirectoryContents(backupDirectory, targetDirectory);
        ApplicationUpdatePathSafety.ValidateCopiedDirectoryTree(backupDirectory, targetDirectory);
        ValidatePackageContents(targetDirectory);
    }

    private static void CopyDirectoryContents(string sourceDirectory, string destinationDirectory)
    {
        ApplicationUpdatePathSafety.ValidateDirectoryTree(sourceDirectory);
        ApplicationUpdatePathSafety.EnsureNoReparsePointInExistingAncestors(destinationDirectory);
        Directory.CreateDirectory(destinationDirectory);
        ApplicationUpdatePathSafety.ValidateDirectoryTree(destinationDirectory);
        ApplicationUpdatePathSafety.EnsureNoReparsePointInExistingAncestors(sourceDirectory);
        foreach (var filePath in Directory.EnumerateFiles(sourceDirectory))
        {
            ApplicationUpdatePathSafety.EnsureNoReparsePointInExistingAncestors(filePath);
            CopyFileWithRetry(filePath, Path.Combine(destinationDirectory, Path.GetFileName(filePath)), overwrite: true);
        }

        ApplicationUpdatePathSafety.EnsureNoReparsePointInExistingAncestors(sourceDirectory);
        foreach (var sourceSubDirectory in Directory.EnumerateDirectories(sourceDirectory))
        {
            ApplicationUpdatePathSafety.EnsureNoReparsePointInExistingAncestors(sourceSubDirectory);
            var destinationSubDirectory = Path.Combine(destinationDirectory, Path.GetFileName(sourceSubDirectory));
            CopyDirectoryContents(sourceSubDirectory, destinationSubDirectory);
        }
    }

    private static void ClearDirectoryContents(string directoryPath)
    {
        ApplicationUpdatePathSafety.ValidateDirectoryTree(directoryPath);
        ApplicationUpdatePathSafety.EnsureNoReparsePointInExistingAncestors(directoryPath);
        foreach (var filePath in Directory.EnumerateFiles(directoryPath))
        {
            ApplicationUpdatePathSafety.EnsureNoReparsePointInExistingAncestors(filePath);
            DeleteFileWithRetry(filePath);
        }

        ApplicationUpdatePathSafety.EnsureNoReparsePointInExistingAncestors(directoryPath);
        foreach (var subDirectoryPath in Directory.EnumerateDirectories(directoryPath))
        {
            ApplicationUpdatePathSafety.EnsureNoReparsePointInExistingAncestors(subDirectoryPath);
            DeleteDirectoryTree(subDirectoryPath);
        }
    }

    private static void DeleteDirectoryTree(string directoryPath)
    {
        ApplicationUpdatePathSafety.EnsureNoReparsePointInExistingAncestors(directoryPath);
        if (!Directory.Exists(directoryPath))
        {
            return;
        }

        ApplicationUpdatePathSafety.ValidateDirectoryTree(directoryPath);
        ApplicationUpdatePathSafety.EnsureNoReparsePointInExistingAncestors(directoryPath);
        foreach (var filePath in Directory.EnumerateFiles(directoryPath))
        {
            DeleteFileWithRetry(filePath);
        }

        ApplicationUpdatePathSafety.EnsureNoReparsePointInExistingAncestors(directoryPath);
        foreach (var subDirectoryPath in Directory.EnumerateDirectories(directoryPath))
        {
            DeleteDirectoryTree(subDirectoryPath);
        }

        SetAttributesNormalWithRetry(directoryPath);
        DeleteDirectoryWithRetry(directoryPath);
    }

    private static void CopyFileWithRetry(string sourcePath, string destinationPath, bool overwrite)
    {
        ApplicationUpdatePathSafety.EnsureNoReparsePointInExistingAncestors(sourcePath);
        ApplicationUpdatePathSafety.EnsureNoReparsePointInExistingAncestors(destinationPath);
        ExecuteFileOperationWithRetry(
            () => File.Copy(sourcePath, destinationPath, overwrite),
            $"copy '{sourcePath}' to '{destinationPath}'");
    }

    private static void DeleteFileWithRetry(string filePath)
    {
        ApplicationUpdatePathSafety.EnsureNoReparsePointInExistingAncestors(filePath);
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
        ApplicationUpdatePathSafety.EnsureNoReparsePointInExistingAncestors(directoryPath);
        ExecuteFileOperationWithRetry(
            () => Directory.Delete(directoryPath, recursive: false),
            $"delete folder '{directoryPath}'");
    }

    private static void SetAttributesNormalWithRetry(string path)
    {
        ApplicationUpdatePathSafety.EnsureNoReparsePointInExistingAncestors(path);
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
        var executables = ApplicationUpdatePathSafety.GetRegularFiles(root)
            .Where(path => path.EndsWith(".exe", StringComparison.OrdinalIgnoreCase))
            .Where(path => ApplicationUpdatePackageRules.IsApplicationExecutableName(Path.GetFileName(path)))
            .ToArray();
        return executables.Length == 1
            ? executables[0]
            : throw new UpdaterException("The update package must contain exactly one Crystal Relay executable.");
    }

    private static bool TryDeleteSuccessfulUpdateBackup(string backupDirectory)
    {
        try
        {
            var backupRoot = NormalizeDirectoryPath(UpdateBackupsFolder);
            var fullBackupDirectory = NormalizeDirectoryPath(backupDirectory);
            ApplicationUpdatePathSafety.EnsureNoReparsePointInExistingAncestors(fullBackupDirectory);
            if (AreSamePath(backupRoot, fullBackupDirectory)
                || !IsPathInside(backupRoot, fullBackupDirectory))
            {
                WriteUpdateLog("Skipped completed update backup cleanup because the path is outside updater storage.");
                return false;
            }

            if (!Directory.Exists(fullBackupDirectory))
            {
                if (File.Exists(fullBackupDirectory))
                {
                    WriteUpdateLog("Could not remove completed update backup because a file occupies its path.");
                    return false;
                }

                return true;
            }

            ApplicationUpdatePathSafety.ValidateDirectoryTree(fullBackupDirectory);

            DeleteDirectoryTree(fullBackupDirectory);
            if (Directory.Exists(fullBackupDirectory))
            {
                WriteUpdateLog("Could not confirm removal of the completed Crystal Relay update backup.");
                return false;
            }

            WriteUpdateLog("Removed completed Crystal Relay update backup.");
            return true;
        }
        catch (Exception ex)
        {
            WriteUpdateLog($"Could not remove completed update backup: {ex.Message}");
            return false;
        }
    }

    private static bool TryDeletePreviousInstallDirectory(UpdateInstallPlan installPlan)
    {
        if (!installPlan.RelocatesInstallFolder)
        {
            return true;
        }

        if (!Directory.Exists(installPlan.SourceDirectory))
        {
            if (File.Exists(installPlan.SourceDirectory))
            {
                WriteUpdateLog("Could not remove the previous Crystal Relay install folder because a file occupies its path.");
                return false;
            }

            return true;
        }

        try
        {
            if (IsCurrentProcessInsideDirectory(installPlan.SourceDirectory))
            {
                WriteUpdateLog("Skipped old Crystal Relay install folder cleanup because it is still running from that folder.");
                return true;
            }

            if (!TryValidatePackageInstallDirectory(installPlan.SourceDirectory, out var validationError))
            {
                WriteUpdateLog($"Skipped old Crystal Relay install folder cleanup: {validationError}");
                return false;
            }

            DeleteDirectoryTree(installPlan.SourceDirectory);
            if (Directory.Exists(installPlan.SourceDirectory))
            {
                WriteUpdateLog("Could not confirm removal of the previous Crystal Relay install folder.");
                return false;
            }

            WriteUpdateLog($"Removed previous Crystal Relay install folder '{installPlan.SourceDirectory}'.");
            return true;
        }
        catch (Exception ex)
        {
            WriteUpdateLog($"Could not remove previous Crystal Relay install folder: {ex.Message}");
            return false;
        }
    }

    private static void ValidatePackageInstallDirectory(string directoryPath, string description)
    {
        if (!TryValidatePackageInstallDirectory(directoryPath, out var validationError))
        {
            throw new UpdaterException($"{description} is not a validated Crystal Relay install folder. {validationError}");
        }
    }

    private static void ValidatePackageContents(
        string directoryPath)
    {
        if (!TryValidatePackageContents(directoryPath, out var validationError))
        {
            throw new UpdaterException($"The rollback backup is not a validated Crystal Relay package. {validationError}");
        }
    }

    private static bool IsValidPackageContents(string directoryPath) =>
        TryValidatePackageContents(directoryPath, out _);

    private static bool TryValidatePackageInstallDirectory(string directoryPath, out string validationError)
    {
        validationError = string.Empty;
        var fullDirectoryPath = NormalizeDirectoryPath(directoryPath);
        ApplicationUpdatePathSafety.EnsureNoReparsePointInExistingAncestors(fullDirectoryPath);
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

        return TryValidatePackageContents(fullDirectoryPath, out validationError);
    }

    private static bool TryValidatePackageContents(string directoryPath, out string validationError)
    {
        validationError = string.Empty;
        var fullDirectoryPath = NormalizeDirectoryPath(directoryPath);
        ApplicationUpdatePathSafety.EnsureNoReparsePointInExistingAncestors(fullDirectoryPath);
        if (!Directory.Exists(fullDirectoryPath))
        {
            validationError = "The folder does not exist.";
            return false;
        }

        try
        {
            ApplicationUpdatePathSafety.ValidateDirectoryTree(fullDirectoryPath);
        }
        catch (Exception ex)
        {
            validationError = $"The folder contains an unsafe filesystem entry: {ex.Message}";
            return false;
        }

        var manifestPath = Path.Combine(fullDirectoryPath, PackageManifestFileName);
        ApplicationUpdatePathSafety.EnsureNoReparsePointInExistingAncestors(manifestPath);
        if (!File.Exists(manifestPath))
        {
            ApplicationUpdatePathSafety.EnsureNoReparsePointInExistingAncestors(fullDirectoryPath);
            var legacyExecutables = Directory.GetFiles(
                    fullDirectoryPath,
                    "*.exe",
                    SearchOption.TopDirectoryOnly)
                .Where(path =>
                {
                    var fileName = Path.GetFileName(path);
                    return !Path.IsPathRooted(fileName)
                        && !fileName.Contains(Path.DirectorySeparatorChar)
                        && !fileName.Contains(Path.AltDirectorySeparatorChar)
                        && ApplicationUpdatePackageRules.IsApplicationExecutableName(fileName)
                        && !fileName.Contains("-bugfix", StringComparison.OrdinalIgnoreCase);
                })
                .ToArray();
            if (legacyExecutables.Length == 1)
            {
                return true;
            }

            validationError = "The package manifest is missing and no safe Crystal Relay executable was found.";
            return false;
        }

        ApplicationUpdatePackageManifest? manifest;
        try
        {
            ApplicationUpdatePathSafety.EnsureNoReparsePointInExistingAncestors(manifestPath);
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
            || string.IsNullOrWhiteSpace(manifest.Version)
            || !string.Equals(manifest.Runtime, RuntimeName, StringComparison.OrdinalIgnoreCase)
            || !ApplicationUpdatePackageRules.TryParseManifestChannel(manifest.Channel, out var channel)
            || string.IsNullOrWhiteSpace(manifest.EntryExecutableName)
            || Path.IsPathRooted(manifest.EntryExecutableName)
            || manifest.EntryExecutableName.Contains(Path.DirectorySeparatorChar)
            || manifest.EntryExecutableName.Contains(Path.AltDirectorySeparatorChar)
            || !ApplicationUpdatePackageRules.IsExpectedInstalledPackageEntryExecutableName(
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
            ApplicationUpdatePathSafety.EnsureNoReparsePointInExistingAncestors(markerPath);
            var markerText = File.Exists(markerPath)
                ? ReadTextWithPathSafety(markerPath)
                : null;
            if (string.IsNullOrWhiteSpace(
                    ApplicationUpdatePackageRules.GetExpectedBuildMarker(channel, manifest.Version))
                || !ApplicationUpdatePackageRules.IsExpectedBuildMarker(channel, manifest.Version, markerText))
            {
                validationError = "The Bug Fix package marker is missing or invalid.";
                return false;
            }
        }

        var entryExecutablePath = Path.Combine(fullDirectoryPath, manifest.EntryExecutableName);
        ApplicationUpdatePathSafety.EnsureNoReparsePointInExistingAncestors(entryExecutablePath);
        if (!File.Exists(entryExecutablePath))
        {
            validationError = "The package entry executable is missing.";
            return false;
        }

        return true;
    }

    private static string ReadTextWithPathSafety(string path)
    {
        ApplicationUpdatePathSafety.EnsureNoReparsePointInExistingAncestors(path);
        return File.ReadAllText(path);
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
        ApplicationUpdateChannel Channel,
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
