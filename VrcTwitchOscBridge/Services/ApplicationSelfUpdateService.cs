using System.Diagnostics;
using System.IO;
using System.IO.Compression;
using System.Net.Http;
using System.Security.Cryptography;
using System.Text.Json;

namespace VrcTwitchOscBridge.Services;

internal sealed record ApplicationSelfUpdateProgress(string Message, double? Percent = null);

internal sealed record ApplicationUpdatePackageManifest(
    string ProductName,
    string Version,
    string Channel,
    string Runtime,
    string EntryExecutableName);

internal sealed class ApplicationSelfUpdateException(string message, Exception? innerException = null)
    : Exception(message, innerException);

internal sealed class ApplicationSelfUpdateService : IDisposable
{
    public const string PackageManifestFileName = "crystal-relay-update.json";
    public const string ApplyUpdateArgument = "--crystal-relay-apply-update";
    private const string CleanupUpdateArgument = "--crystal-relay-update-cleanup";
    private const string ApplyManifestFileName = "crystal-relay-apply-update.json";
    private const string ProductName = "Crystal Relay";
    private const string RuntimeName = "win-x64";
    private const string PackageFolderPrefix = "CrystalRelay-v";
    private const string DedicatedUpdaterExecutableName = "CrystalRelayUpdater.exe";
    private const string SourceBackupFolderName = "source";
    private const string TargetBackupFolderName = "target";
    private const string ExecutableSearchPattern = "Crystal Relay.exe";
    private const int FileOperationRetryCount = 20;
    private static readonly TimeSpan DownloadTimeout = TimeSpan.FromMinutes(10);
    private static readonly TimeSpan ProcessExitTimeout = TimeSpan.FromSeconds(75);
    private static readonly TimeSpan CleanupProcessExitTimeout = TimeSpan.FromSeconds(20);
    private static readonly TimeSpan StaleUpdateSessionRetention = TimeSpan.FromDays(1);
    private static readonly TimeSpan FailedUpdateBackupRetention = TimeSpan.FromDays(1);
    private static readonly TimeSpan FileOperationRetryDelay = TimeSpan.FromMilliseconds(500);
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNameCaseInsensitive = true,
        WriteIndented = true
    };

    private readonly HttpClient httpClient = new()
    {
        Timeout = DownloadTimeout
    };

    public ApplicationSelfUpdateService()
    {
        httpClient.DefaultRequestHeaders.UserAgent.ParseAdd("CrystalRelay-DesktopApp");
    }

    public static bool TryGetApplyManifestPath(string[] args, out string manifestPath)
    {
        manifestPath = string.Empty;
        if (args.Length == 0)
        {
            return false;
        }

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

    public static bool TryGetCleanupRequest(string[] args, out ApplicationSelfUpdateCleanupRequest request)
    {
        request = default;
        if (args.Length == 0)
        {
            return false;
        }

        for (var index = 0; index < args.Length; index++)
        {
            if (!string.Equals(args[index], CleanupUpdateArgument, StringComparison.OrdinalIgnoreCase))
            {
                continue;
            }

            if (index + 2 >= args.Length
                || string.IsNullOrWhiteSpace(args[index + 1])
                || !int.TryParse(args[index + 2], out var processId))
            {
                return false;
            }

            request = new ApplicationSelfUpdateCleanupRequest(args[index + 1], processId);
            return true;
        }

        return false;
    }

    public async Task PrepareAndLaunchUpdateAsync(
        ApplicationUpdateInfo update,
        IProgress<ApplicationSelfUpdateProgress>? progress = null,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(update);
        ValidateReleaseAsset(update);

        AppDataPaths.EnsureCoreFolders();
        Directory.CreateDirectory(AppDataPaths.UpdatesFolder);
        Directory.CreateDirectory(AppDataPaths.UpdateBackupsFolder);
        PruneStaleUpdateArtifacts();

        var sessionRoot = Path.Combine(
            AppDataPaths.UpdatesFolder,
            $"{SanitizePathSegment(update.LatestVersion)}-{DateTimeOffset.UtcNow:yyyyMMddHHmmssfff}");
        var stagingRoot = Path.Combine(sessionRoot, "staged");
        var downloadPath = Path.Combine(sessionRoot, update.AssetName);
        Directory.CreateDirectory(sessionRoot);
        Directory.CreateDirectory(stagingRoot);

        try
        {
            Report(progress, "Preparing Crystal Relay update.");
            await DownloadUpdateAsync(update, downloadPath, progress, cancellationToken);
            ValidateDownloadedFile(update, downloadPath);

            Report(progress, "Extracting Crystal Relay update.");
            ExtractZipSafely(downloadPath, stagingRoot);

            var package = ResolvePackage(stagingRoot, update);
            var backupDirectory = Path.Combine(
                AppDataPaths.UpdateBackupsFolder,
                $"{SanitizePathSegment(update.LatestVersion)}-{DateTimeOffset.UtcNow:yyyyMMddHHmmssfff}");
            var currentExecutableName = Path.GetFileName(Environment.ProcessPath ?? string.Empty);
            var applyManifest = new ApplicationUpdateApplyManifest(
                ProductName,
                package.Manifest.Version,
                package.Manifest.Channel,
                package.Manifest.Runtime,
                package.Manifest.EntryExecutableName,
                package.PackageRoot,
                NormalizeDirectoryPath(AppContext.BaseDirectory),
                backupDirectory,
                update.ReleasePageUrl,
                Environment.ProcessId,
                currentExecutableName);
            var applyManifestPath = Path.Combine(sessionRoot, ApplyManifestFileName);
            await File.WriteAllTextAsync(
                applyManifestPath,
                JsonSerializer.Serialize(applyManifest, JsonOptions),
                cancellationToken);

            Report(progress, "Restarting Crystal Relay to apply the update.");
            var updaterExecutablePath = PrepareDedicatedUpdaterExecutable(sessionRoot);
            LaunchDedicatedUpdater(updaterExecutablePath, applyManifestPath);
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch (ApplicationSelfUpdateException)
        {
            throw;
        }
        catch (Exception ex)
        {
            throw new ApplicationSelfUpdateException("Crystal Relay could not prepare the update.", ex);
        }
    }

    public static async Task ApplyUpdateAsync(string manifestPath, CancellationToken cancellationToken = default)
    {
        var releasePageUrl = string.Empty;
        try
        {
            var manifest = await ReadApplyManifestAsync(manifestPath, cancellationToken);
            releasePageUrl = manifest.ReleasePageUrl;
            WriteUpdateLog($"Applying update {manifest.Version} from '{manifest.PackageRoot}' to '{manifest.InstallDirectory}'.");

            ValidateApplyManifest(manifest);
            var installPlan = CreateInstallPlan(manifest);
            ValidateInstallPlan(installPlan);
            await WaitForProcessExitAsync(manifest.SourceProcessId, ProcessExitTimeout, cancellationToken);

            PrepareRollbackBackup(manifest, installPlan);

            try
            {
                ReplaceInstallFiles(manifest, installPlan);

                var installedEntryPath = Path.Combine(installPlan.TargetDirectory, manifest.EntryExecutableName);
                if (!File.Exists(installedEntryPath))
                {
                    installedEntryPath = ResolveSingleExecutable(installPlan.TargetDirectory);
                }

                LaunchInstalledApplication(installedEntryPath, manifestPath);
                WriteUpdateLog(
                    installPlan.RelocatesInstallFolder
                        ? $"Update {manifest.Version} applied successfully to '{installPlan.TargetDirectory}'."
                        : $"Update {manifest.Version} applied successfully.");
            }
            catch (Exception ex)
            {
                WriteUpdateLog($"Update replacement failed: {ex}");
                TryRestoreBackup(manifest, installPlan);
                TryLaunchRestoredApplication(manifest, installPlan);
                throw new ApplicationSelfUpdateException("Crystal Relay restored the previous version because the update could not be applied.", ex);
            }
        }
        catch (Exception ex)
        {
            WriteUpdateLog($"Update apply failed: {ex}");
            ShowUpdateFailure(ex, releasePageUrl);
        }
    }

    public static async Task CleanupCompletedUpdateAsync(
        ApplicationSelfUpdateCleanupRequest request,
        CancellationToken cancellationToken = default)
    {
        try
        {
            await WaitForProcessExitAsync(request.ProcessId, CleanupProcessExitTimeout, cancellationToken);

            var fullManifestPath = Path.GetFullPath(request.ManifestPath);
            var updateRoot = Path.GetFullPath(AppDataPaths.UpdatesFolder);
            if (!IsPathInside(updateRoot, fullManifestPath) || !File.Exists(fullManifestPath))
            {
                return;
            }

            var sessionRoot = Path.GetDirectoryName(fullManifestPath);
            if (string.IsNullOrWhiteSpace(sessionRoot) || !IsPathInside(updateRoot, sessionRoot))
            {
                return;
            }

            var manifest = await ReadApplyManifestAsync(fullManifestPath, cancellationToken);
            ValidateApplyManifest(manifest);
            var installPlan = CreateInstallPlan(manifest);

            DeleteDirectoryTree(sessionRoot);
            TryDeleteSuccessfulUpdateBackup(manifest.BackupDirectory);
            TryDeletePreviousInstallDirectory(installPlan);
            PruneStaleUpdateArtifacts();
        }
        catch (Exception ex)
        {
            WriteUpdateLog($"Update cleanup skipped: {ex.Message}");
        }
    }

    public void Dispose() => httpClient.Dispose();

    private static void ValidateReleaseAsset(ApplicationUpdateInfo update)
    {
        if (string.IsNullOrWhiteSpace(update.AssetName)
            || !update.AssetName.EndsWith(".zip", StringComparison.OrdinalIgnoreCase))
        {
            throw new ApplicationSelfUpdateException("The GitHub release does not include a usable Crystal Relay ZIP asset.");
        }

        var expectedAssetName = update.IsBeta
            ? $"CrystalRelay-v{update.LatestVersion}-win-x64.zip"
            : $"CrystalRelay-v{update.LatestVersion}-win-x64.zip";
        if (!string.Equals(update.AssetName, expectedAssetName, StringComparison.OrdinalIgnoreCase))
        {
            throw new ApplicationSelfUpdateException("The GitHub release asset name does not match Crystal Relay's update package format.");
        }

        if (!Uri.TryCreate(update.AssetDownloadUrl, UriKind.Absolute, out var uri)
            || uri.Scheme != Uri.UriSchemeHttps
            || !string.Equals(uri.Host, "github.com", StringComparison.OrdinalIgnoreCase))
        {
            throw new ApplicationSelfUpdateException("The update download URL is not a trusted GitHub HTTPS asset URL.");
        }
    }

    private async Task DownloadUpdateAsync(
        ApplicationUpdateInfo update,
        string downloadPath,
        IProgress<ApplicationSelfUpdateProgress>? progress,
        CancellationToken cancellationToken)
    {
        Report(progress, "Downloading Crystal Relay update.", 0);
        using var response = await httpClient.GetAsync(
            update.AssetDownloadUrl,
            HttpCompletionOption.ResponseHeadersRead,
            cancellationToken);
        response.EnsureSuccessStatusCode();

        await using var remoteStream = await response.Content.ReadAsStreamAsync(cancellationToken);
        await using var fileStream = new FileStream(downloadPath, FileMode.Create, FileAccess.Write, FileShare.None);

        var totalBytes = response.Content.Headers.ContentLength ?? update.AssetSizeBytes;
        var buffer = new byte[1024 * 128];
        long downloadedBytes = 0;
        var nextProgressReport = 0d;
        while (true)
        {
            var bytesRead = await remoteStream.ReadAsync(buffer.AsMemory(0, buffer.Length), cancellationToken);
            if (bytesRead == 0)
            {
                break;
            }

            await fileStream.WriteAsync(buffer.AsMemory(0, bytesRead), cancellationToken);
            downloadedBytes += bytesRead;
            if (totalBytes <= 0)
            {
                continue;
            }

            var percent = Math.Round(downloadedBytes * 100d / totalBytes, 1);
            if (percent >= nextProgressReport)
            {
                Report(progress, $"Downloading Crystal Relay update ({percent:0.#}%).", percent);
                nextProgressReport += 10;
            }
        }

        Report(progress, "Crystal Relay update download finished.", 100);
    }

    private static void ValidateDownloadedFile(ApplicationUpdateInfo update, string downloadPath)
    {
        var fileInfo = new FileInfo(downloadPath);
        if (!fileInfo.Exists || fileInfo.Length == 0)
        {
            throw new ApplicationSelfUpdateException("The update download was empty.");
        }

        if (update.AssetSizeBytes > 0 && fileInfo.Length != update.AssetSizeBytes)
        {
            throw new ApplicationSelfUpdateException("The update download size did not match the GitHub release asset.");
        }

        var expectedDigest = NormalizeSha256Digest(update.Sha256Digest);
        if (string.IsNullOrWhiteSpace(expectedDigest))
        {
            throw new ApplicationSelfUpdateException("The GitHub release asset does not include a SHA-256 digest.");
        }

        using var fileStream = File.OpenRead(downloadPath);
        var actualDigest = Convert.ToHexString(SHA256.HashData(fileStream)).ToLowerInvariant();
        if (!string.Equals(actualDigest, expectedDigest, StringComparison.OrdinalIgnoreCase))
        {
            throw new ApplicationSelfUpdateException("The update download failed SHA-256 validation.");
        }
    }

    private static UpdatePackage ResolvePackage(string stagingRoot, ApplicationUpdateInfo update)
    {
        var manifestPaths = Directory.GetFiles(stagingRoot, PackageManifestFileName, SearchOption.AllDirectories);
        if (manifestPaths.Length > 1)
        {
            throw new ApplicationSelfUpdateException("The update package contains more than one Crystal Relay manifest.");
        }

        if (manifestPaths.Length == 1)
        {
            var manifestPath = manifestPaths[0];
            var packageRoot = Path.GetDirectoryName(manifestPath)
                ?? throw new ApplicationSelfUpdateException("The update package manifest path is invalid.");
            var manifest = JsonSerializer.Deserialize<ApplicationUpdatePackageManifest>(
                    File.ReadAllText(manifestPath),
                    JsonOptions)
                ?? throw new ApplicationSelfUpdateException("The update package manifest could not be read.");
            ValidatePackageManifest(manifest, update);

            var entryPath = Path.Combine(packageRoot, manifest.EntryExecutableName);
            if (!File.Exists(entryPath))
            {
                throw new ApplicationSelfUpdateException("The update package entry executable is missing.");
            }

            return new UpdatePackage(packageRoot, manifest, entryPath);
        }

        var executablePath = ResolveSingleExecutable(stagingRoot);
        var fallbackManifest = new ApplicationUpdatePackageManifest(
            ProductName,
            update.LatestVersion,
            update.IsBeta ? "beta" : "stable",
            RuntimeName,
            Path.GetFileName(executablePath));
        return new UpdatePackage(Path.GetDirectoryName(executablePath)!, fallbackManifest, executablePath);
    }

    private static void ValidatePackageManifest(ApplicationUpdatePackageManifest manifest, ApplicationUpdateInfo update)
    {
        if (!string.Equals(manifest.ProductName, ProductName, StringComparison.Ordinal))
        {
            throw new ApplicationSelfUpdateException("The update package is not a Crystal Relay package.");
        }

        if (!string.Equals(manifest.Runtime, RuntimeName, StringComparison.OrdinalIgnoreCase))
        {
            throw new ApplicationSelfUpdateException("The update package runtime does not match this Crystal Relay build.");
        }

        var expectedChannel = update.IsBeta ? "beta" : "stable";
        if (!string.Equals(manifest.Channel, expectedChannel, StringComparison.OrdinalIgnoreCase))
        {
            throw new ApplicationSelfUpdateException("The update package channel does not match the selected release.");
        }

        if (!string.Equals(manifest.Version, update.LatestVersion, StringComparison.OrdinalIgnoreCase))
        {
            throw new ApplicationSelfUpdateException("The update package version does not match the selected release.");
        }

        if (string.IsNullOrWhiteSpace(manifest.EntryExecutableName)
            || Path.IsPathRooted(manifest.EntryExecutableName)
            || manifest.EntryExecutableName.Contains(Path.DirectorySeparatorChar)
            || manifest.EntryExecutableName.Contains(Path.AltDirectorySeparatorChar)
            || !manifest.EntryExecutableName.EndsWith(".exe", StringComparison.OrdinalIgnoreCase))
        {
            throw new ApplicationSelfUpdateException("The update package entry executable name is invalid.");
        }
    }

    private static void ExtractZipSafely(string zipPath, string destinationRoot)
    {
        var fullDestinationRoot = NormalizeDirectoryPath(destinationRoot);
        using var archive = ZipFile.OpenRead(zipPath);
        if (archive.Entries.Count == 0)
        {
            throw new ApplicationSelfUpdateException("The update ZIP is empty.");
        }

        foreach (var entry in archive.Entries)
        {
            if (string.IsNullOrWhiteSpace(entry.FullName) || Path.IsPathRooted(entry.FullName))
            {
                throw new ApplicationSelfUpdateException("The update ZIP contains an invalid path.");
            }

            var destinationPath = Path.GetFullPath(Path.Combine(fullDestinationRoot, entry.FullName));
            if (!IsPathInside(fullDestinationRoot, destinationPath))
            {
                throw new ApplicationSelfUpdateException("The update ZIP contains a path outside the staging folder.");
            }

            if (string.IsNullOrWhiteSpace(entry.Name))
            {
                Directory.CreateDirectory(destinationPath);
                continue;
            }

            var destinationDirectory = Path.GetDirectoryName(destinationPath)
                ?? throw new ApplicationSelfUpdateException("The update ZIP contains an invalid file path.");
            Directory.CreateDirectory(destinationDirectory);
            entry.ExtractToFile(destinationPath, overwrite: true);
        }
    }

    private static async Task<ApplicationUpdateApplyManifest> ReadApplyManifestAsync(
        string manifestPath,
        CancellationToken cancellationToken)
    {
        var fullManifestPath = Path.GetFullPath(manifestPath);
        var json = await File.ReadAllTextAsync(fullManifestPath, cancellationToken);
        return JsonSerializer.Deserialize<ApplicationUpdateApplyManifest>(json, JsonOptions)
            ?? throw new ApplicationSelfUpdateException("The Crystal Relay update apply manifest could not be read.");
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
            throw new ApplicationSelfUpdateException("The Crystal Relay update apply manifest is incomplete.");
        }

        var packageRoot = NormalizeDirectoryPath(manifest.PackageRoot);
        var installDirectory = NormalizeDirectoryPath(manifest.InstallDirectory);
        var backupDirectory = NormalizeDirectoryPath(manifest.BackupDirectory);
        if (!Directory.Exists(packageRoot))
        {
            throw new ApplicationSelfUpdateException("The staged Crystal Relay update folder is missing.");
        }

        var updateRoot = NormalizeDirectoryPath(AppDataPaths.UpdatesFolder);
        if (!IsPathInside(updateRoot, packageRoot))
        {
            throw new ApplicationSelfUpdateException("The staged Crystal Relay update folder is outside updater storage.");
        }

        ValidateInstallDirectory(installDirectory);

        var backupRoot = NormalizeDirectoryPath(AppDataPaths.UpdateBackupsFolder);
        if (!IsPathInside(backupRoot, backupDirectory))
        {
            throw new ApplicationSelfUpdateException("The update backup folder is outside Crystal Relay's updater storage.");
        }
    }

    private static void ValidateInstallDirectory(string installDirectory)
    {
        ValidatePotentialInstallDirectory(installDirectory);

        if (!Directory.Exists(installDirectory))
        {
            throw new ApplicationSelfUpdateException("The Crystal Relay install folder could not be found.");
        }
    }

    private static void ValidatePotentialInstallDirectory(string installDirectory)
    {
        var normalizedInstallDirectory = NormalizeDirectoryPath(installDirectory);

        var root = Path.GetPathRoot(normalizedInstallDirectory);
        if (string.IsNullOrWhiteSpace(root)
            || string.Equals(NormalizeDirectoryPath(root), normalizedInstallDirectory, StringComparison.OrdinalIgnoreCase))
        {
            throw new ApplicationSelfUpdateException("Crystal Relay refused to update because the install folder path is unsafe.");
        }

        var runtimeDataRoot = NormalizeDirectoryPath(AppDataPaths.RootFolder);
        if (IsPathInside(runtimeDataRoot, normalizedInstallDirectory))
        {
            throw new ApplicationSelfUpdateException("Crystal Relay refused to replace files inside its runtime data folder.");
        }

        var parentDirectory = Path.GetDirectoryName(normalizedInstallDirectory);
        if (string.IsNullOrWhiteSpace(parentDirectory) || !Directory.Exists(parentDirectory))
        {
            throw new ApplicationSelfUpdateException("The Crystal Relay install parent folder could not be found.");
        }
    }

    private static async Task WaitForProcessExitAsync(
        int processId,
        TimeSpan timeout,
        CancellationToken cancellationToken)
    {
        if (processId <= 0 || processId == Environment.ProcessId)
        {
            return;
        }

        try
        {
            using var process = Process.GetProcessById(processId);
            using var timeoutCancellation = new CancellationTokenSource(timeout);
            using var linkedCancellation = CancellationTokenSource.CreateLinkedTokenSource(
                cancellationToken,
                timeoutCancellation.Token);
            await process.WaitForExitAsync(linkedCancellation.Token);
        }
        catch (ArgumentException)
        {
        }
        catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested)
        {
            throw new ApplicationSelfUpdateException("Crystal Relay could not close in time to apply the update.");
        }
    }

    private static string PrepareDedicatedUpdaterExecutable(string sessionRoot)
    {
        var installedUpdaterPath = Path.Combine(AppContext.BaseDirectory, DedicatedUpdaterExecutableName);
        if (!File.Exists(installedUpdaterPath))
        {
            throw new ApplicationSelfUpdateException("Crystal Relay could not find its dedicated updater executable.");
        }

        var updaterExecutablePath = Path.Combine(sessionRoot, DedicatedUpdaterExecutableName);
        CopyFileWithRetry(installedUpdaterPath, updaterExecutablePath, overwrite: true);
        return updaterExecutablePath;
    }

    private static void LaunchDedicatedUpdater(string updaterExecutablePath, string applyManifestPath)
    {
        var startInfo = new ProcessStartInfo
        {
            FileName = updaterExecutablePath,
            UseShellExecute = false,
            CreateNoWindow = true,
            WindowStyle = ProcessWindowStyle.Hidden,
            WorkingDirectory = Path.GetDirectoryName(updaterExecutablePath)
        };
        startInfo.ArgumentList.Add(ApplyUpdateArgument);
        startInfo.ArgumentList.Add(applyManifestPath);
        Process.Start(startInfo);
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
        var packageFolderName = Path.GetFileName(packageRoot);
        var sourceFolderName = Path.GetFileName(sourceDirectory);
        var targetDirectory = sourceDirectory;

        if (IsPackageInstallFolderName(sourceFolderName) && IsPackageInstallFolderName(packageFolderName))
        {
            var sourceParent = Path.GetDirectoryName(sourceDirectory)
                ?? throw new ApplicationSelfUpdateException("The Crystal Relay install folder path is invalid.");
            targetDirectory = NormalizeDirectoryPath(Path.Combine(sourceParent, packageFolderName));
        }

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
            ?? throw new ApplicationSelfUpdateException("The Crystal Relay install folder path is invalid.");
        var targetParent = Path.GetDirectoryName(installPlan.TargetDirectory)
            ?? throw new ApplicationSelfUpdateException("The Crystal Relay install target path is invalid.");
        if (!AreSamePath(sourceParent, targetParent))
        {
            throw new ApplicationSelfUpdateException("Crystal Relay refused to move the install folder outside its current parent folder.");
        }

        if (!IsPackageInstallFolderName(Path.GetFileName(installPlan.SourceDirectory))
            || !IsPackageInstallFolderName(Path.GetFileName(installPlan.TargetDirectory)))
        {
            throw new ApplicationSelfUpdateException("Crystal Relay refused to rename a folder that is not a Crystal Relay package folder.");
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
                throw new ApplicationSelfUpdateException(
                    $"Crystal Relay could not {description} because the file stayed busy.",
                    ex);
            }
        }
    }

    private static bool IsRetriableFileOperationException(Exception ex) =>
        ex is IOException or UnauthorizedAccessException;

    private static string ResolveSingleExecutable(string root)
    {
        var executables = Directory.GetFiles(root, ExecutableSearchPattern, SearchOption.AllDirectories)
            .Where(path => Path.GetFileName(path).EndsWith(".exe", StringComparison.OrdinalIgnoreCase))
            .ToArray();
        return executables.Length == 1
            ? executables[0]
            : throw new ApplicationSelfUpdateException("The update package must contain exactly one Crystal Relay executable.");
    }

    private static void PruneStaleUpdateArtifacts()
    {
        DeleteOldUpdateSessions(AppDataPaths.UpdatesFolder);
        DeleteOldUpdateBackups(AppDataPaths.UpdateBackupsFolder);
    }

    private static void DeleteOldUpdateSessions(string updateRoot)
    {
        var cutoff = DateTimeOffset.UtcNow.Subtract(StaleUpdateSessionRetention);
        foreach (var directory in Directory.EnumerateDirectories(updateRoot))
        {
            try
            {
                var info = new DirectoryInfo(directory);
                if (info.LastWriteTimeUtc < cutoff.UtcDateTime)
                {
                    DeleteDirectoryTree(info.FullName);
                }
            }
            catch
            {
            }
        }
    }

    private static void DeleteOldUpdateBackups(string backupRoot)
    {
        var cutoff = DateTimeOffset.UtcNow.Subtract(FailedUpdateBackupRetention);
        foreach (var directory in Directory.EnumerateDirectories(backupRoot))
        {
            try
            {
                var info = new DirectoryInfo(directory);
                if (info.LastWriteTimeUtc < cutoff.UtcDateTime)
                {
                    DeleteDirectoryTree(info.FullName);
                }
            }
            catch
            {
            }
        }
    }

    private static void TryDeleteSuccessfulUpdateBackup(string backupDirectory)
    {
        try
        {
            var backupRoot = NormalizeDirectoryPath(AppDataPaths.UpdateBackupsFolder);
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
            throw new ApplicationSelfUpdateException($"{description} is not a validated Crystal Relay install folder. {validationError}");
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
            manifest = JsonSerializer.Deserialize<ApplicationUpdatePackageManifest>(
                File.ReadAllText(manifestPath),
                JsonOptions);
        }
        catch (Exception ex)
        {
            validationError = $"The package manifest could not be read: {ex.Message}";
            return false;
        }

        if (manifest is null
            || !string.Equals(manifest.ProductName, ProductName, StringComparison.Ordinal)
            || !string.Equals(manifest.Runtime, RuntimeName, StringComparison.OrdinalIgnoreCase)
            || string.IsNullOrWhiteSpace(manifest.EntryExecutableName)
            || Path.IsPathRooted(manifest.EntryExecutableName)
            || manifest.EntryExecutableName.Contains(Path.DirectorySeparatorChar)
            || manifest.EntryExecutableName.Contains(Path.AltDirectorySeparatorChar)
            || !manifest.EntryExecutableName.EndsWith(".exe", StringComparison.OrdinalIgnoreCase))
        {
            validationError = "The package manifest is not a Crystal Relay install manifest.";
            return false;
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
        !string.IsNullOrWhiteSpace(folderName)
        && !folderName.Contains(Path.DirectorySeparatorChar)
        && !folderName.Contains(Path.AltDirectorySeparatorChar)
        && (string.Equals(folderName, "Crystal Relay", StringComparison.OrdinalIgnoreCase)
            || (folderName.StartsWith(PackageFolderPrefix, StringComparison.OrdinalIgnoreCase)
                && folderName.EndsWith($"-{RuntimeName}", StringComparison.OrdinalIgnoreCase)));

    private static string NormalizeSha256Digest(string? digest)
    {
        if (string.IsNullOrWhiteSpace(digest))
        {
            return string.Empty;
        }

        var normalized = digest.Trim();
        const string prefix = "sha256:";
        if (normalized.StartsWith(prefix, StringComparison.OrdinalIgnoreCase))
        {
            normalized = normalized[prefix.Length..];
        }

        return normalized.Length == 64 && normalized.All(IsHexDigit)
            ? normalized.ToLowerInvariant()
            : string.Empty;
    }

    private static bool IsHexDigit(char value) =>
        value is >= '0' and <= '9'
            or >= 'a' and <= 'f'
            or >= 'A' and <= 'F';

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

    private static string SanitizePathSegment(string value)
    {
        var invalid = Path.GetInvalidFileNameChars();
        var builder = new string(value.Select(character => invalid.Contains(character) ? '-' : character).ToArray());
        return string.IsNullOrWhiteSpace(builder) ? "update" : builder;
    }

    private static void Report(
        IProgress<ApplicationSelfUpdateProgress>? progress,
        string message,
        double? percent = null) =>
        progress?.Report(new ApplicationSelfUpdateProgress(message, percent));

    private static void ShowUpdateFailure(Exception exception, string releasePageUrl)
    {
        try
        {
            var message = string.IsNullOrWhiteSpace(releasePageUrl)
                ? $"Crystal Relay could not finish the update.\n\n{exception.Message}"
                : $"Crystal Relay could not finish the update.\n\n{exception.Message}\n\nOpen the GitHub release page instead?";
            var result = System.Windows.MessageBox.Show(
                message,
                "Crystal Relay Update",
                string.IsNullOrWhiteSpace(releasePageUrl)
                    ? System.Windows.MessageBoxButton.OK
                    : System.Windows.MessageBoxButton.YesNo,
                System.Windows.MessageBoxImage.Error);
            if (result == System.Windows.MessageBoxResult.Yes
                && Uri.TryCreate(releasePageUrl, UriKind.Absolute, out var uri)
                && uri.Scheme == Uri.UriSchemeHttps)
            {
                Process.Start(new ProcessStartInfo
                {
                    FileName = uri.AbsoluteUri,
                    UseShellExecute = true
                });
            }
        }
        catch
        {
        }
    }

    private static void WriteUpdateLog(string message)
    {
        try
        {
            Directory.CreateDirectory(AppDataPaths.UpdatesFolder);
            File.AppendAllText(
                Path.Combine(AppDataPaths.UpdatesFolder, "update.log"),
                $"[{DateTimeOffset.Now:O}] {message}{Environment.NewLine}");
        }
        catch
        {
        }
    }

    private sealed record UpdatePackage(
        string PackageRoot,
        ApplicationUpdatePackageManifest Manifest,
        string EntryExecutablePath);

    private sealed record UpdateInstallPlan(
        string SourceDirectory,
        string TargetDirectory,
        string SourceBackupDirectory,
        string TargetBackupDirectory,
        bool RelocatesInstallFolder);

    private sealed record ApplicationUpdateApplyManifest(
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
}

internal readonly record struct ApplicationSelfUpdateCleanupRequest(string ManifestPath, int ProcessId);
