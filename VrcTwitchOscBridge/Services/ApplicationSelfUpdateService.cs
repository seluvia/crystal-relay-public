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
    private const string DedicatedUpdaterExecutableName = "CrystalRelayUpdater.exe";
    private const string SourceBackupFolderName = "source";
    private const string TargetBackupFolderName = "target";
    private const int FileOperationRetryCount = 20;
    private static readonly TimeSpan DownloadTimeout = TimeSpan.FromMinutes(10);
    private static readonly TimeSpan ProcessExitTimeout = TimeSpan.FromSeconds(75);
    private static readonly TimeSpan StartupAcknowledgementTimeout = ProcessExitTimeout;
    private static readonly TimeSpan ProcessTerminationTimeout = TimeSpan.FromSeconds(5);
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

        ApplicationUpdatePathSafety.EnsureNoReparsePointInExistingAncestors(AppDataPaths.RootFolder);
        ApplicationUpdatePathSafety.EnsureNoReparsePointInExistingAncestors(AppDataPaths.UpdatesFolder);
        ApplicationUpdatePathSafety.EnsureNoReparsePointInExistingAncestors(AppDataPaths.UpdateBackupsFolder);
        AppDataPaths.EnsureCoreFolders();
        ApplicationUpdatePathSafety.ValidateDirectoryTree(AppDataPaths.UpdatesFolder);
        ApplicationUpdatePathSafety.ValidateDirectoryTree(AppDataPaths.UpdateBackupsFolder);
        Directory.CreateDirectory(AppDataPaths.UpdatesFolder);
        Directory.CreateDirectory(AppDataPaths.UpdateBackupsFolder);
        PruneStaleUpdateArtifacts();

        var sessionRoot = Path.Combine(
            AppDataPaths.UpdatesFolder,
            $"{SanitizePathSegment(update.LatestVersion)}-{DateTimeOffset.UtcNow:yyyyMMddHHmmssfff}-{Guid.NewGuid():N}");
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
                $"{SanitizePathSegment(update.LatestVersion)}-{DateTimeOffset.UtcNow:yyyyMMddHHmmssfff}-{Guid.NewGuid():N}");
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
            ValidateApplyManifestPath(manifestPath);
            var manifest = await ReadApplyManifestAsync(manifestPath, cancellationToken);
            releasePageUrl = manifest.ReleasePageUrl;
            WriteUpdateLog($"Applying update {manifest.Version} from '{manifest.PackageRoot}' to '{manifest.InstallDirectory}'.");

            ValidateApplyManifest(manifest);
            ApplicationUpdateRollback.ClearStaleStartupAcknowledgement(manifestPath);
            var installPlan = CreateInstallPlan(manifest);
            ValidateInstallPlan(installPlan);
            await WaitForProcessExitAsync(manifest.SourceProcessId, ProcessExitTimeout, cancellationToken);

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
                    var installedEntryPath = Path.Combine(installPlan.TargetDirectory, manifest.EntryExecutableName);
                    if (!File.Exists(installedEntryPath))
                    {
                        installedEntryPath = ResolveSingleExecutable(installPlan.TargetDirectory);
                    }

                    launchedProcess = LaunchInstalledApplication(installedEntryPath, manifestPath);
                    return Task.CompletedTask;
                },
                waitForAcknowledgementAsync: () =>
                    ApplicationUpdateRollback.WaitForStartupAcknowledgementAsync(
                        manifestPath,
                        manifest.Version,
                        StartupAcknowledgementTimeout,
                        cancellationToken),
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
                        throw new ApplicationSelfUpdateException(
                            "Crystal Relay applied the update, but could not finish post-acknowledgement cleanup.");
                    }

                    return Task.CompletedTask;
                });

            WriteUpdateLog(
                installPlan.RelocatesInstallFolder
                    ? $"Update {manifest.Version} applied successfully to '{installPlan.TargetDirectory}'."
                    : $"Update {manifest.Version} applied successfully.");
        }
        catch (Exception ex)
        {
            WriteUpdateLog($"Update apply failed: {ex}");
            ShowUpdateFailure(ex, releasePageUrl);
        }
    }

    public static async Task<bool> CleanupCompletedUpdateAsync(
        ApplicationSelfUpdateCleanupRequest request,
        CancellationToken cancellationToken = default)
    {
        try
        {
            await WaitForProcessExitAsync(request.ProcessId, CleanupProcessExitTimeout, cancellationToken);

            var manifestPath = Path.GetFullPath(request.ManifestPath);
            ApplicationUpdatePathSafety.EnsureNoReparsePointInExistingAncestors(manifestPath);
            var updateRoot = Path.GetFullPath(AppDataPaths.UpdatesFolder);
            if (!IsPathInside(updateRoot, manifestPath) || !File.Exists(manifestPath))
            {
                WriteUpdateLog("Update cleanup could not find a manifest inside updater storage.");
                return false;
            }

            var sessionRoot = Path.GetDirectoryName(manifestPath);
            if (string.IsNullOrWhiteSpace(sessionRoot)
                || AreSamePath(updateRoot, sessionRoot)
                || !IsPathInside(updateRoot, sessionRoot))
            {
                WriteUpdateLog("Update cleanup found an invalid update session path.");
                return false;
            }

            var manifest = await ReadApplyManifestAsync(manifestPath, cancellationToken);
            var installPlan = CreateInstallPlan(manifest);
            ValidateCleanupManifest(manifest, installPlan);
            await ApplicationUpdateRollback.WaitForStartupAcknowledgementAsync(
                manifestPath,
                manifest.Version,
                TimeSpan.Zero,
                cancellationToken);

            if (!TryDeleteSuccessfulUpdateBackup(manifest.BackupDirectory))
            {
                return false;
            }

            if (!TryDeletePreviousInstallDirectory(installPlan))
            {
                return false;
            }

            try
            {
                DeleteDirectoryTree(sessionRoot);
                if (Directory.Exists(sessionRoot))
                {
                    WriteUpdateLog("Could not remove the completed Crystal Relay update session.");
                    return false;
                }
            }
            catch (Exception ex)
            {
                WriteUpdateLog($"Could not remove the completed Crystal Relay update session: {ex.Message}");
                return false;
            }

            try
            {
                PruneStaleUpdateArtifacts();
            }
            catch (Exception ex)
            {
                WriteUpdateLog($"Could not prune stale Crystal Relay update artifacts: {ex.Message}");
            }

            return true;
        }
        catch (Exception ex)
        {
            WriteUpdateLog($"Update cleanup skipped: {ex.Message}");
            return false;
        }
    }

    public static async Task AcknowledgeUpdateStartupAsync(
        ApplicationSelfUpdateCleanupRequest request,
        CancellationToken cancellationToken = default)
    {
        var manifestPath = Path.GetFullPath(request.ManifestPath);
        ApplicationUpdatePathSafety.EnsureNoReparsePointInExistingAncestors(manifestPath);
        var updateRoot = NormalizeDirectoryPath(AppDataPaths.UpdatesFolder);
        if (!IsPathInside(updateRoot, manifestPath) || !File.Exists(manifestPath))
        {
            throw new ApplicationSelfUpdateException("The Crystal Relay update apply manifest is outside updater storage.");
        }

        var sessionRoot = Path.GetDirectoryName(manifestPath);
        if (string.IsNullOrWhiteSpace(sessionRoot)
            || AreSamePath(updateRoot, sessionRoot)
            || !IsPathInside(updateRoot, sessionRoot))
        {
            throw new ApplicationSelfUpdateException("The Crystal Relay update session path is invalid.");
        }

        var manifest = await ReadApplyManifestAsync(manifestPath, cancellationToken);
        ValidateApplyManifest(manifest);
        var installPlan = CreateInstallPlan(manifest);
        ValidateInstallPlan(installPlan);
        if (!ApplicationUpdateRollback.IsCompleteBackup(
                manifest.BackupDirectory,
                installPlan.RelocatesInstallFolder,
                directory => IsValidPackageContents(directory)))
        {
            throw new ApplicationSelfUpdateException(
                "The Crystal Relay rollback backup is incomplete or invalid.");
        }

        ApplicationUpdateRollback.WriteStartupAcknowledgement(manifestPath, manifest.Version);
    }

    public void Dispose() => httpClient.Dispose();

    internal static void ValidateReleaseAsset(ApplicationUpdateInfo update)
    {
        if (string.IsNullOrWhiteSpace(update.AssetName)
            || !update.AssetName.EndsWith(".zip", StringComparison.OrdinalIgnoreCase))
        {
            throw new ApplicationSelfUpdateException("The GitHub release does not include a usable Crystal Relay ZIP asset.");
        }

        var expectedAssetName = ApplicationUpdatePackageRules.GetExpectedAssetName(
            update.Channel,
            update.LatestVersion);
        var nameComparison = update.Channel == ApplicationUpdateChannel.BugFix
            ? StringComparison.Ordinal
            : StringComparison.OrdinalIgnoreCase;
        if (!string.Equals(update.AssetName, expectedAssetName, nameComparison))
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
        var manifestPaths = ApplicationUpdatePathSafety.GetRegularFiles(stagingRoot)
            .Where(path => string.Equals(
                Path.GetFileName(path),
                PackageManifestFileName,
                StringComparison.OrdinalIgnoreCase))
            .ToArray();
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
            ValidatePackageMarker(packageRoot, manifest);

            var entryPath = Path.Combine(packageRoot, manifest.EntryExecutableName);
            if (!File.Exists(entryPath))
            {
                throw new ApplicationSelfUpdateException("The update package entry executable is missing.");
            }

            return new UpdatePackage(packageRoot, manifest, entryPath);
        }

        if (update.IsBugFix)
        {
            throw new ApplicationSelfUpdateException("The Bug Fix update package manifest is missing.");
        }

        var executablePath = ResolveExpectedExecutable(
            stagingRoot,
            update.Channel,
            update.LatestVersion);
        var fallbackManifest = new ApplicationUpdatePackageManifest(
            ProductName,
            update.LatestVersion,
            ApplicationUpdatePackageRules.GetManifestChannel(update.Channel),
            RuntimeName,
            Path.GetFileName(executablePath));
        return new UpdatePackage(Path.GetDirectoryName(executablePath)!, fallbackManifest, executablePath);
    }

    internal static void ValidatePackageMarker(
        string packageRoot,
        ApplicationUpdatePackageManifest manifest)
    {
        if (!ApplicationUpdatePackageRules.TryParseManifestChannel(manifest.Channel, out var channel))
        {
            throw new ApplicationSelfUpdateException("The update package channel is invalid.");
        }

        var expectedMarker = ApplicationUpdatePackageRules.GetExpectedBuildMarker(
            channel,
            manifest.Version);
        if (channel != ApplicationUpdateChannel.BugFix || string.IsNullOrWhiteSpace(expectedMarker))
        {
            if (channel == ApplicationUpdateChannel.BugFix)
            {
                throw new ApplicationSelfUpdateException("The Bug Fix update package version is invalid.");
            }

            return;
        }

        var markerPath = Path.Combine(packageRoot, "bugfix-build.flag");
        var markerText = File.Exists(markerPath) ? File.ReadAllText(markerPath) : null;
        if (!ApplicationUpdatePackageRules.IsExpectedBuildMarker(channel, manifest.Version, markerText))
        {
            throw new ApplicationSelfUpdateException("The Bug Fix update package marker does not match the selected release.");
        }
    }

    internal static void ValidatePackageManifest(
        ApplicationUpdatePackageManifest manifest,
        ApplicationUpdateInfo update)
    {
        if (!string.Equals(manifest.ProductName, ProductName, StringComparison.Ordinal))
        {
            throw new ApplicationSelfUpdateException("The update package is not a Crystal Relay package.");
        }

        if (!string.Equals(manifest.Runtime, RuntimeName, StringComparison.OrdinalIgnoreCase))
        {
            throw new ApplicationSelfUpdateException("The update package runtime does not match this Crystal Relay build.");
        }

        if (!ApplicationUpdatePackageRules.TryParseManifestChannel(manifest.Channel, out var manifestChannel)
            || manifestChannel != update.Channel)
        {
            throw new ApplicationSelfUpdateException("The update package channel does not match the selected release.");
        }

        if (!string.Equals(manifest.Version, update.LatestVersion, StringComparison.Ordinal))
        {
            throw new ApplicationSelfUpdateException("The update package version does not match the selected release.");
        }

        if (string.IsNullOrWhiteSpace(manifest.EntryExecutableName)
            || Path.IsPathRooted(manifest.EntryExecutableName)
            || manifest.EntryExecutableName.Contains(Path.DirectorySeparatorChar)
            || manifest.EntryExecutableName.Contains(Path.AltDirectorySeparatorChar)
            || !ApplicationUpdatePackageRules.IsExpectedInstalledPackageEntryExecutableName(
                manifestChannel,
                manifest.Version,
                manifest.EntryExecutableName))
        {
            throw new ApplicationSelfUpdateException("The update package entry executable name is invalid.");
        }
    }

    private static void ExtractZipSafely(string zipPath, string destinationRoot)
    {
        var fullDestinationRoot = NormalizeDirectoryPath(destinationRoot);
        ApplicationUpdatePathSafety.EnsureNoReparsePointInExistingAncestors(fullDestinationRoot);
        if (Directory.Exists(fullDestinationRoot))
        {
            ApplicationUpdatePathSafety.ValidateDirectoryTree(fullDestinationRoot);
        }

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

            ApplicationUpdatePathSafety.EnsureNoReparsePointInExistingAncestors(destinationPath);

            if (string.IsNullOrWhiteSpace(entry.Name))
            {
                Directory.CreateDirectory(destinationPath);
                ApplicationUpdatePathSafety.ValidateDirectoryTree(destinationPath);
                continue;
            }

            var destinationDirectory = Path.GetDirectoryName(destinationPath)
                ?? throw new ApplicationSelfUpdateException("The update ZIP contains an invalid file path.");
            ApplicationUpdatePathSafety.EnsureNoReparsePointInExistingAncestors(destinationDirectory);
            Directory.CreateDirectory(destinationDirectory);
            ApplicationUpdatePathSafety.ValidateDirectoryTree(destinationDirectory);
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

    private static void ValidateApplyManifestPath(string manifestPath)
    {
        var fullManifestPath = Path.GetFullPath(manifestPath);
        ApplicationUpdatePathSafety.EnsureNoReparsePointInExistingAncestors(fullManifestPath);

        var updateRoot = NormalizeDirectoryPath(AppDataPaths.UpdatesFolder);
        var sessionRoot = Path.GetDirectoryName(fullManifestPath);
        if (string.IsNullOrWhiteSpace(sessionRoot)
            || AreSamePath(updateRoot, sessionRoot)
            || !IsPathInside(updateRoot, sessionRoot))
        {
            throw new ApplicationSelfUpdateException("The Crystal Relay update apply manifest is outside updater storage.");
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
            throw new ApplicationSelfUpdateException("The Crystal Relay update apply manifest is incomplete.");
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
            throw new ApplicationSelfUpdateException("The Crystal Relay update apply manifest channel or entry executable is invalid.");
        }

        if (!string.IsNullOrWhiteSpace(manifest.PreviousEntryExecutableName)
            && (Path.IsPathRooted(manifest.PreviousEntryExecutableName)
                || manifest.PreviousEntryExecutableName.Contains(Path.DirectorySeparatorChar)
                || manifest.PreviousEntryExecutableName.Contains(Path.AltDirectorySeparatorChar)
                || !ApplicationUpdatePackageRules.IsApplicationExecutableName(manifest.PreviousEntryExecutableName)))
        {
            throw new ApplicationSelfUpdateException("The Crystal Relay restored entry executable is invalid.");
        }

        var packageRoot = NormalizeDirectoryPath(manifest.PackageRoot);
        var installDirectory = NormalizeDirectoryPath(manifest.InstallDirectory);
        var backupDirectory = NormalizeDirectoryPath(manifest.BackupDirectory);
        ApplicationUpdatePathSafety.EnsureNoReparsePointInExistingAncestors(packageRoot);
        ApplicationUpdatePathSafety.EnsureNoReparsePointInExistingAncestors(installDirectory);
        ApplicationUpdatePathSafety.EnsureNoReparsePointInExistingAncestors(backupDirectory);
        if (!Directory.Exists(packageRoot))
        {
            throw new ApplicationSelfUpdateException("The staged Crystal Relay update folder is missing.");
        }

        var updateRoot = NormalizeDirectoryPath(AppDataPaths.UpdatesFolder);
        if (!IsPathInside(updateRoot, packageRoot))
        {
            throw new ApplicationSelfUpdateException("The staged Crystal Relay update folder is outside updater storage.");
        }

        ValidateStagedPackageIdentity(packageRoot, manifest);

        ValidateInstallDirectory(installDirectory);

        var backupRoot = NormalizeDirectoryPath(AppDataPaths.UpdateBackupsFolder);
        if (AreSamePath(backupRoot, backupDirectory)
            || !IsPathInside(backupRoot, backupDirectory))
        {
            throw new ApplicationSelfUpdateException("The update backup folder is outside Crystal Relay's updater storage.");
        }
    }

    private static void ValidateCleanupManifest(
        ApplicationUpdateApplyManifest manifest,
        UpdateInstallPlan installPlan)
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
            throw new ApplicationSelfUpdateException("The Crystal Relay update apply manifest channel or entry executable is invalid.");
        }

        if (!string.IsNullOrWhiteSpace(manifest.PreviousEntryExecutableName)
            && (Path.IsPathRooted(manifest.PreviousEntryExecutableName)
                || manifest.PreviousEntryExecutableName.Contains(Path.DirectorySeparatorChar)
                || manifest.PreviousEntryExecutableName.Contains(Path.AltDirectorySeparatorChar)
                || !ApplicationUpdatePackageRules.IsApplicationExecutableName(manifest.PreviousEntryExecutableName)))
        {
            throw new ApplicationSelfUpdateException("The Crystal Relay restored entry executable is invalid.");
        }

        var packageRoot = NormalizeDirectoryPath(manifest.PackageRoot);
        var backupDirectory = NormalizeDirectoryPath(manifest.BackupDirectory);
        var updateRoot = NormalizeDirectoryPath(AppDataPaths.UpdatesFolder);
        if (!IsPathInside(updateRoot, packageRoot))
        {
            throw new ApplicationSelfUpdateException("The staged Crystal Relay update folder is outside updater storage.");
        }

        var backupRoot = NormalizeDirectoryPath(AppDataPaths.UpdateBackupsFolder);
        if (AreSamePath(backupRoot, backupDirectory)
            || !IsPathInside(backupRoot, backupDirectory))
        {
            throw new ApplicationSelfUpdateException("The update backup folder is outside Crystal Relay's updater storage.");
        }

        ValidateInstallPlan(installPlan);
        ValidateStagedPackageIdentity(packageRoot, manifest);

        if (Directory.Exists(installPlan.SourceDirectory))
        {
            ValidatePackageInstallDirectory(installPlan.SourceDirectory, "The previous Crystal Relay install folder");
        }

        if (Directory.Exists(backupDirectory)
            && !ApplicationUpdateRollback.IsCompleteBackup(
                 backupDirectory,
                 installPlan.RelocatesInstallFolder,
                 directory => IsValidPackageContents(directory)))
        {
                throw new ApplicationSelfUpdateException("The Crystal Relay rollback backup is incomplete or invalid.");
        }
    }

    private static void ValidateStagedPackageIdentity(
        string packageRoot,
        ApplicationUpdateApplyManifest applyManifest)
    {
        ApplicationUpdatePathSafety.EnsureNoReparsePointInExistingAncestors(packageRoot);
        ApplicationUpdatePathSafety.ValidateDirectoryTree(packageRoot);
        if (!ApplicationUpdatePackageRules.TryParseManifestChannel(
                applyManifest.Channel,
                out var applyChannel))
        {
            throw new ApplicationSelfUpdateException("The staged Crystal Relay package channel is invalid.");
        }

        if (!File.Exists(Path.Combine(packageRoot, applyManifest.EntryExecutableName)))
        {
            throw new ApplicationSelfUpdateException("The staged Crystal Relay update entry executable is missing.");
        }

        var packageManifestPath = Path.Combine(packageRoot, PackageManifestFileName);
        if (!File.Exists(packageManifestPath))
        {
            if (applyChannel == ApplicationUpdateChannel.BugFix)
            {
                throw new ApplicationSelfUpdateException("The Bug Fix update package manifest is missing.");
            }

            return;
        }

        ApplicationUpdatePackageManifest? packageManifest;
        try
        {
            packageManifest = JsonSerializer.Deserialize<ApplicationUpdatePackageManifest>(
                File.ReadAllText(packageManifestPath),
                JsonOptions);
        }
        catch (Exception ex)
        {
            throw new ApplicationSelfUpdateException(
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
            throw new ApplicationSelfUpdateException(
                "The staged Crystal Relay package manifest does not match the apply manifest.");
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
            throw new ApplicationSelfUpdateException("The staged Crystal Relay package manifest is invalid.");
        }

        ValidatePackageMarker(
            packageRoot,
            new ApplicationUpdatePackageManifest(
                applyManifest.ProductName,
                applyManifest.Version,
                applyManifest.Channel,
                applyManifest.Runtime,
                applyManifest.EntryExecutableName));

        if (!File.Exists(Path.Combine(packageRoot, applyManifest.EntryExecutableName)))
        {
            throw new ApplicationSelfUpdateException("The staged Crystal Relay update entry executable is missing.");
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
        ApplicationUpdatePathSafety.EnsureNoReparsePointInExistingAncestors(normalizedInstallDirectory);

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

        if (Directory.Exists(normalizedInstallDirectory))
        {
            ApplicationUpdatePathSafety.ValidateDirectoryTree(normalizedInstallDirectory);
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
        ApplicationUpdatePathSafety.EnsureNoReparsePointInExistingAncestors(installedUpdaterPath);
        if (!File.Exists(installedUpdaterPath))
        {
            throw new ApplicationSelfUpdateException("Crystal Relay could not find its dedicated updater executable.");
        }

        var updaterExecutablePath = Path.Combine(sessionRoot, DedicatedUpdaterExecutableName);
        ApplicationUpdatePathSafety.EnsureNoReparsePointInExistingAncestors(updaterExecutablePath);
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
            ?? throw new ApplicationSelfUpdateException("Crystal Relay could not launch the installed application.");
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
            throw new ApplicationSelfUpdateException(
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
            throw new ApplicationSelfUpdateException("The Crystal Relay update apply manifest channel is invalid.");
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
            throw new ApplicationSelfUpdateException("The restored Crystal Relay executable is outside the previous install folder.");
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
        }) ?? throw new ApplicationSelfUpdateException("Crystal Relay could not relaunch the restored application.");
    }

    private static void RestoreBackup(ApplicationUpdateApplyManifest manifest, UpdateInstallPlan installPlan)
    {
        if (!ApplicationUpdateRollback.IsCompleteBackup(
                manifest.BackupDirectory,
                installPlan.RelocatesInstallFolder,
                directory => IsValidPackageContents(directory)))
        {
            throw new ApplicationSelfUpdateException(
                "The Crystal Relay rollback backup is incomplete or invalid; the current installation was left untouched.");
        }

        if (installPlan.RelocatesInstallFolder)
        {
            RestoreRelocatedTarget(manifest, installPlan);
            return;
        }

        RestoreDirectoryBackup(installPlan.SourceBackupDirectory, installPlan.SourceDirectory);
        WriteUpdateLog("Restored previous Crystal Relay files from update backup.");
    }

    private static void RestoreRelocatedTarget(
        ApplicationUpdateApplyManifest manifest,
        UpdateInstallPlan installPlan)
    {
        if (ApplicationUpdateRollback.GetRecordedTargetPresence(manifest.BackupDirectory))
        {
            if (ApplicationUpdateRollback.IsEntryMissing(installPlan.TargetBackupDirectory))
            {
                throw new ApplicationSelfUpdateException(
                    "Missing target backup for a relocation target that was recorded as present.");
            }

            RestoreDirectoryBackup(installPlan.TargetBackupDirectory, installPlan.TargetDirectory);
            WriteUpdateLog("Restored previous Crystal Relay target files from update backup.");
        }
        else if (!ApplicationUpdateRollback.IsEntryMissing(installPlan.TargetBackupDirectory))
        {
            throw new ApplicationSelfUpdateException(
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
                    throw new ApplicationSelfUpdateException(
                        "Crystal Relay could not remove the newly-created relocation target folder.");
                }

                WriteUpdateLog("Removed partial Crystal Relay update target folder.");
            }
            else if (!ApplicationUpdateRollback.IsEntryMissing(installPlan.TargetDirectory))
            {
                throw new ApplicationSelfUpdateException(
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
            throw new ApplicationSelfUpdateException(
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
            throw new ApplicationSelfUpdateException(
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
        var executables = ApplicationUpdatePathSafety.GetRegularFiles(root)
            .Where(path => path.EndsWith(".exe", StringComparison.OrdinalIgnoreCase))
            .Where(path => ApplicationUpdatePackageRules.IsApplicationExecutableName(Path.GetFileName(path)))
            .ToArray();
        return executables.Length == 1
            ? executables[0]
            : throw new ApplicationSelfUpdateException("The update package must contain exactly one Crystal Relay executable.");
    }

    private static string ResolveExpectedExecutable(
        string root,
        ApplicationUpdateChannel channel,
        string version)
    {
        var executables = ApplicationUpdatePathSafety.GetRegularFiles(root)
            .Where(path => path.EndsWith(".exe", StringComparison.OrdinalIgnoreCase))
            .Where(path => ApplicationUpdatePackageRules.IsExpectedInstalledPackageEntryExecutableName(
                channel,
                version,
                Path.GetFileName(path)))
            .ToArray();
        return executables.Length == 1
            ? executables[0]
            : throw new ApplicationSelfUpdateException(
                "The update package must contain exactly one expected Crystal Relay executable.");
    }

    private static void PruneStaleUpdateArtifacts()
    {
        DeleteOldUpdateSessions(AppDataPaths.UpdatesFolder);
        DeleteOldUpdateBackups(AppDataPaths.UpdateBackupsFolder);
    }

    private static void DeleteOldUpdateSessions(string updateRoot)
    {
        if (!Directory.Exists(updateRoot))
        {
            return;
        }

        ApplicationUpdatePathSafety.ValidateDirectoryTree(updateRoot);
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
        if (!Directory.Exists(backupRoot))
        {
            return;
        }

        ApplicationUpdatePathSafety.ValidateDirectoryTree(backupRoot);
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

    private static bool TryDeleteSuccessfulUpdateBackup(string backupDirectory)
    {
        try
        {
            var backupRoot = NormalizeDirectoryPath(AppDataPaths.UpdateBackupsFolder);
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
            throw new ApplicationSelfUpdateException($"{description} is not a validated Crystal Relay install folder. {validationError}");
        }
    }

    private static void ValidatePackageContents(
        string directoryPath)
    {
        if (!TryValidatePackageContents(directoryPath, out var validationError))
        {
            throw new ApplicationSelfUpdateException($"The rollback backup is not a validated Crystal Relay package. {validationError}");
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

        var expectedMarker = ApplicationUpdatePackageRules.GetExpectedBuildMarker(channel, manifest.Version);
        if (channel == ApplicationUpdateChannel.BugFix)
        {
            var markerPath = Path.Combine(fullDirectoryPath, "bugfix-build.flag");
            ApplicationUpdatePathSafety.EnsureNoReparsePointInExistingAncestors(markerPath);
            var markerText = File.Exists(markerPath)
                ? ReadTextWithPathSafety(markerPath)
                : null;
            if (string.IsNullOrWhiteSpace(expectedMarker)
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
        ApplicationUpdateChannel Channel,
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
