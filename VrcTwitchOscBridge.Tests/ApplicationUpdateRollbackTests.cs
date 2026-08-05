using System.Collections.Generic;
using System.IO;
using System.Text.Json;
using System.Threading;
using VrcTwitchOscBridge.Services;
using Xunit;

namespace VrcTwitchOscBridge.Tests;

public sealed class ApplicationUpdateRollbackTests
{
    [Fact]
    public async Task BackupFailure_DoesNotInstallRestoreOrRelaunch()
    {
        var trace = new List<string>();

        await Assert.ThrowsAsync<IOException>(() => ApplicationUpdateApplyTransaction.RunAsync(
            prepareBackupAsync: () => throw new IOException("backup copy failed"),
            replaceInstallAsync: () => { trace.Add("install"); return Task.CompletedTask; },
            launchInstalledApplicationAsync: () => { trace.Add("launch"); return Task.CompletedTask; },
            waitForAcknowledgementAsync: () => Task.CompletedTask,
            terminateLaunchedApplicationAsync: () => { trace.Add("terminate"); return Task.CompletedTask; },
            restoreBackupAsync: () => { trace.Add("restore"); return Task.CompletedTask; },
            relaunchRestoredApplicationAsync: () => { trace.Add("relaunch"); return Task.CompletedTask; },
            cleanupAfterAcknowledgementAsync: () => { trace.Add("cleanup"); return Task.CompletedTask; }));

        Assert.Empty(trace);
    }

    [Fact]
    public async Task InstallFailure_RestoresCompleteBackupAndRelaunchesPreviousApplication()
    {
        var trace = new List<string>();

        await Assert.ThrowsAsync<IOException>(() => ApplicationUpdateApplyTransaction.RunAsync(
            prepareBackupAsync: () => { trace.Add("backup"); return Task.CompletedTask; },
            replaceInstallAsync: () => throw new IOException("install copy failed"),
            launchInstalledApplicationAsync: () => { trace.Add("launch"); return Task.CompletedTask; },
            waitForAcknowledgementAsync: () => Task.CompletedTask,
            terminateLaunchedApplicationAsync: () => { trace.Add("terminate"); return Task.CompletedTask; },
            restoreBackupAsync: () => { trace.Add("restore"); return Task.CompletedTask; },
            relaunchRestoredApplicationAsync: () => { trace.Add("relaunch"); return Task.CompletedTask; },
            cleanupAfterAcknowledgementAsync: () => { trace.Add("cleanup"); return Task.CompletedTask; }));

        Assert.Equal(["backup", "restore", "relaunch"], trace);
    }

    [Fact]
    public void RelocationBackup_RejectsExistingFileAtTargetInsteadOfTreatingItAsMissing()
    {
        using var folder = TemporaryFolder.Create();
        var source = CreateCrystalRelayPackage(folder.Path, "source-package");
        var backup = Path.Combine(folder.Path, "backup");
        var target = Path.Combine(folder.Path, "target");
        File.WriteAllText(target, "existing target file");

        Assert.Throws<IOException>(() => ApplicationUpdateRollback.PrepareCompleteBackup(
            backupDirectory: backup,
            sourceDirectory: source,
            targetDirectory: target,
            relocatesInstallFolder: true,
            copyDirectoryContents: CopyDirectoryContents,
            validateBackupDirectory: ValidateCrystalRelayPackage,
            deleteDirectoryTree: DeleteDirectoryTree));

        Assert.False(Directory.Exists(backup));
        Assert.False(Directory.Exists(ApplicationUpdateRollback.GetIncompleteBackupDirectory(backup)));
        Assert.True(File.Exists(target));
    }

    [Fact]
    public void RelocationBackup_RecordsTargetPresenceAndRejectsDisappearingTargetBackup()
    {
        using var folder = TemporaryFolder.Create();
        var source = CreateCrystalRelayPackage(folder.Path, "source-package");
        var target = CreateCrystalRelayPackage(folder.Path, "target-package");
        var backup = Path.Combine(folder.Path, "backup");

        ApplicationUpdateRollback.PrepareCompleteBackup(
            backupDirectory: backup,
            sourceDirectory: source,
            targetDirectory: target,
            relocatesInstallFolder: true,
            copyDirectoryContents: CopyDirectoryContents,
            validateBackupDirectory: ValidateCrystalRelayPackage,
            deleteDirectoryTree: DeleteDirectoryTree);

        var targetPresencePath = Path.Combine(backup, "target-presence.flag");
        Assert.Equal("present", File.ReadAllText(targetPresencePath));

        Directory.Delete(Path.Combine(backup, "target"), recursive: true);

        Assert.False(ApplicationUpdateRollback.IsCompleteBackup(
            backup,
            relocatesInstallFolder: true,
            validateBackupDirectory: _ => true));
    }

    [Fact]
    public void RelocationBackup_RecordsAbsentTargetWithoutAllowingMissingMarker()
    {
        using var folder = TemporaryFolder.Create();
        var source = CreateCrystalRelayPackage(folder.Path, "source-package");
        var target = Path.Combine(folder.Path, "missing-target");
        var backup = Path.Combine(folder.Path, "backup");

        ApplicationUpdateRollback.PrepareCompleteBackup(
            backupDirectory: backup,
            sourceDirectory: source,
            targetDirectory: target,
            relocatesInstallFolder: true,
            copyDirectoryContents: CopyDirectoryContents,
            validateBackupDirectory: ValidateCrystalRelayPackage,
            deleteDirectoryTree: DeleteDirectoryTree);

        Assert.Equal(
            "absent",
            File.ReadAllText(Path.Combine(backup, "target-presence.flag")));
        Assert.True(ApplicationUpdateRollback.IsCompleteBackup(
            backup,
            relocatesInstallFolder: true,
            validateBackupDirectory: _ => true));

        File.Delete(Path.Combine(backup, "target-presence.flag"));

        Assert.False(ApplicationUpdateRollback.IsCompleteBackup(
            backup,
            relocatesInstallFolder: true,
            validateBackupDirectory: _ => true));
    }

    [Fact]
    public async Task TerminationFailure_IsSurfacedWithoutRestoreOrRelaunch()
    {
        var trace = new List<string>();
        var originalFailure = new InvalidOperationException("launch failed");

        var exception = await Assert.ThrowsAsync<ApplicationUpdateRollbackException>(() =>
            ApplicationUpdateApplyTransaction.RunAsync(
                prepareBackupAsync: () => Task.CompletedTask,
                replaceInstallAsync: () => Task.CompletedTask,
                launchInstalledApplicationAsync: () => throw originalFailure,
                waitForAcknowledgementAsync: () => Task.CompletedTask,
                terminateLaunchedApplicationAsync: () =>
                {
                    trace.Add("terminate");
                    throw new IOException("terminate failed");
                },
                restoreBackupAsync: () =>
                {
                    trace.Add("restore");
                    throw new IOException("restore failed");
                },
                relaunchRestoredApplicationAsync: () =>
                {
                    trace.Add("relaunch");
                    throw new IOException("relaunch failed");
                },
                cleanupAfterAcknowledgementAsync: () => Task.CompletedTask));

        Assert.Equal(["terminate"], trace);
        Assert.Same(originalFailure, exception.OriginalException);
        Assert.Single(exception.RecoveryExceptions);
        Assert.Contains("rollback", exception.Message, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task RestoreFailure_IsSurfacedWithoutRelaunch()
    {
        var trace = new List<string>();
        var originalFailure = new InvalidOperationException("acknowledgement missing");

        var exception = await Assert.ThrowsAsync<ApplicationUpdateRollbackException>(() =>
            ApplicationUpdateApplyTransaction.RunAsync(
                prepareBackupAsync: () => Task.CompletedTask,
                replaceInstallAsync: () => Task.CompletedTask,
                launchInstalledApplicationAsync: () => Task.CompletedTask,
                waitForAcknowledgementAsync: () => throw originalFailure,
                terminateLaunchedApplicationAsync: () =>
                {
                    trace.Add("terminate");
                    return Task.CompletedTask;
                },
                restoreBackupAsync: () =>
                {
                    trace.Add("restore");
                    throw new IOException("restore failed");
                },
                relaunchRestoredApplicationAsync: () =>
                {
                    trace.Add("relaunch");
                    return Task.CompletedTask;
                },
                cleanupAfterAcknowledgementAsync: () => Task.CompletedTask));

        Assert.Equal(["terminate", "restore"], trace);
        Assert.Same(originalFailure, exception.OriginalException);
        Assert.Single(exception.RecoveryExceptions);
    }

    [Fact]
    public async Task InvalidPackage_FailsBeforeBackup()
    {
        var trace = new List<string>();

        await Assert.ThrowsAsync<ApplicationSelfUpdateException>(() => ApplicationUpdateApplyTransaction.RunAsync(
            prepareBackupAsync: () => { trace.Add("backup"); return Task.CompletedTask; },
            replaceInstallAsync: () => { trace.Add("install"); return Task.CompletedTask; },
            launchInstalledApplicationAsync: () => { trace.Add("launch"); return Task.CompletedTask; },
            waitForAcknowledgementAsync: () => Task.CompletedTask,
            terminateLaunchedApplicationAsync: () => { trace.Add("terminate"); return Task.CompletedTask; },
            restoreBackupAsync: () => { trace.Add("restore"); return Task.CompletedTask; },
            relaunchRestoredApplicationAsync: () => { trace.Add("relaunch"); return Task.CompletedTask; },
            cleanupAfterAcknowledgementAsync: () => { trace.Add("cleanup"); return Task.CompletedTask; },
            validateBeforeBackupAsync: () => throw new ApplicationSelfUpdateException("invalid package")));

        Assert.Empty(trace);
    }

    [Fact]
    public async Task LaunchFailure_TerminatesNewProcessRestoresAndRelaunchesPreviousApplication()
    {
        var trace = new List<string>();

        await Assert.ThrowsAsync<InvalidOperationException>(() => ApplicationUpdateApplyTransaction.RunAsync(
            prepareBackupAsync: () => Task.CompletedTask,
            replaceInstallAsync: () => Task.CompletedTask,
            launchInstalledApplicationAsync: () => throw new InvalidOperationException("launch failed"),
            waitForAcknowledgementAsync: () => Task.CompletedTask,
            terminateLaunchedApplicationAsync: () => { trace.Add("terminate"); return Task.CompletedTask; },
            restoreBackupAsync: () => { trace.Add("restore"); return Task.CompletedTask; },
            relaunchRestoredApplicationAsync: () => { trace.Add("relaunch"); return Task.CompletedTask; },
            cleanupAfterAcknowledgementAsync: () => { trace.Add("cleanup"); return Task.CompletedTask; }));

        Assert.Equal(["terminate", "restore", "relaunch"], trace);
    }

    [Fact]
    public async Task MissingAcknowledgement_TerminatesNewProcessRestoresAndRelaunchesPreviousApplication()
    {
        var trace = new List<string>();

        await Assert.ThrowsAsync<TimeoutException>(() => ApplicationUpdateApplyTransaction.RunAsync(
            prepareBackupAsync: () => Task.CompletedTask,
            replaceInstallAsync: () => Task.CompletedTask,
            launchInstalledApplicationAsync: () => Task.CompletedTask,
            waitForAcknowledgementAsync: () => throw new TimeoutException("acknowledgement missing"),
            terminateLaunchedApplicationAsync: () => { trace.Add("terminate"); return Task.CompletedTask; },
            restoreBackupAsync: () => { trace.Add("restore"); return Task.CompletedTask; },
            relaunchRestoredApplicationAsync: () => { trace.Add("relaunch"); return Task.CompletedTask; },
            cleanupAfterAcknowledgementAsync: () => { trace.Add("cleanup"); return Task.CompletedTask; }));

        Assert.Equal(["terminate", "restore", "relaunch"], trace);
    }

    [Fact]
    public async Task SuccessfulAcknowledgement_CleansUpWithoutRestoring()
    {
        var trace = new List<string>();

        await ApplicationUpdateApplyTransaction.RunAsync(
            prepareBackupAsync: () => { trace.Add("backup"); return Task.CompletedTask; },
            replaceInstallAsync: () => { trace.Add("install"); return Task.CompletedTask; },
            launchInstalledApplicationAsync: () => { trace.Add("launch"); return Task.CompletedTask; },
            waitForAcknowledgementAsync: () => { trace.Add("ack"); return Task.CompletedTask; },
            terminateLaunchedApplicationAsync: () => { trace.Add("terminate"); return Task.CompletedTask; },
            restoreBackupAsync: () => { trace.Add("restore"); return Task.CompletedTask; },
            relaunchRestoredApplicationAsync: () => { trace.Add("relaunch"); return Task.CompletedTask; },
            cleanupAfterAcknowledgementAsync: () => { trace.Add("cleanup"); return Task.CompletedTask; });

        Assert.Equal(["backup", "install", "launch", "ack", "cleanup"], trace);
    }

    [Fact]
    public void IncompleteBackup_IsRejectedAsRestoreSource()
    {
        using var folder = TemporaryFolder.Create();
        var backup = Path.Combine(folder.Path, "backup");
        var source = Path.Combine(backup, "source");
        Directory.CreateDirectory(source);
        File.WriteAllText(Path.Combine(source, "Crystal Relay.exe"), "partial");

        Assert.False(ApplicationUpdateRollback.IsCompleteBackup(
            backup,
            relocatesInstallFolder: false,
            validateBackupDirectory: _ => true));
    }

    [Fact]
    public void PrepareCompleteBackup_CopyFailureRemovesPartialStaging()
    {
        using var folder = TemporaryFolder.Create();
        var source = CreateCrystalRelayPackage(folder.Path, "source-package");
        var backup = Path.Combine(folder.Path, "backup");
        var target = Path.Combine(folder.Path, "target");
        var incomplete = ApplicationUpdateRollback.GetIncompleteBackupDirectory(backup);

        Assert.Throws<IOException>(() => ApplicationUpdateRollback.PrepareCompleteBackup(
            backupDirectory: backup,
            sourceDirectory: source,
            targetDirectory: target,
            relocatesInstallFolder: false,
            copyDirectoryContents: (sourceDirectory, destinationDirectory) =>
            {
                CopyDirectoryContents(sourceDirectory, destinationDirectory);
                File.WriteAllText(Path.Combine(destinationDirectory, "partial-output.tmp"), "partial");
                throw new IOException("copy failed after partial output");
            },
            validateBackupDirectory: ValidateCrystalRelayPackage,
            deleteDirectoryTree: DeleteDirectoryTree));

        Assert.False(Directory.Exists(backup));
        Assert.False(Directory.Exists(incomplete));
    }

    [Fact]
    public void PrepareCompleteBackup_RejectsSilentlyOmittedSourceFiles()
    {
        using var folder = TemporaryFolder.Create();
        var source = CreateCrystalRelayPackage(folder.Path, "source-package");
        var omittedSourceFile = Path.Combine(source, "settings.dat");
        File.WriteAllText(omittedSourceFile, "must be backed up");
        var backup = Path.Combine(folder.Path, "backup");
        var target = Path.Combine(folder.Path, "target");
        var incomplete = ApplicationUpdateRollback.GetIncompleteBackupDirectory(backup);

        Assert.Throws<IOException>(() => ApplicationUpdateRollback.PrepareCompleteBackup(
            backupDirectory: backup,
            sourceDirectory: source,
            targetDirectory: target,
            relocatesInstallFolder: false,
            copyDirectoryContents: (sourceDirectory, destinationDirectory) =>
            {
                Directory.CreateDirectory(destinationDirectory);
                foreach (var sourceFile in Directory.EnumerateFiles(sourceDirectory))
                {
                    if (string.Equals(sourceFile, omittedSourceFile, StringComparison.OrdinalIgnoreCase))
                    {
                        continue;
                    }

                    File.Copy(
                        sourceFile,
                        Path.Combine(destinationDirectory, Path.GetFileName(sourceFile)));
                }
            },
            validateBackupDirectory: ValidateCrystalRelayPackage,
            deleteDirectoryTree: DeleteDirectoryTree));

        Assert.False(Directory.Exists(backup));
        Assert.False(Directory.Exists(incomplete));
    }

    [Fact]
    public void ValidateCopiedDirectoryTree_RejectsSameSizeContentCorruption()
    {
        using var folder = TemporaryFolder.Create();
        var source = Path.Combine(folder.Path, "source");
        var copied = Path.Combine(folder.Path, "copied");
        Directory.CreateDirectory(source);
        Directory.CreateDirectory(copied);
        File.WriteAllText(Path.Combine(source, "same-size.dat"), "AAAA");
        File.WriteAllText(Path.Combine(copied, "same-size.dat"), "BBBB");

        Assert.Throws<IOException>(() =>
            ApplicationUpdatePathSafety.ValidateCopiedDirectoryTree(source, copied));
    }

    [Fact]
    public void PrepareCompleteBackup_StagesAndValidatesCompletePackage()
    {
        using var folder = TemporaryFolder.Create();
        var source = CreateCrystalRelayPackage(folder.Path, "source-package");
        var backup = Path.Combine(folder.Path, "backup");
        var target = Path.Combine(folder.Path, "target");
        var incomplete = ApplicationUpdateRollback.GetIncompleteBackupDirectory(backup);
        var validationCalls = 0;

        ApplicationUpdateRollback.PrepareCompleteBackup(
            backupDirectory: backup,
            sourceDirectory: source,
            targetDirectory: target,
            relocatesInstallFolder: false,
            copyDirectoryContents: CopyDirectoryContents,
            validateBackupDirectory: directory =>
            {
                validationCalls++;
                ValidateCrystalRelayPackage(directory);
            },
            deleteDirectoryTree: DeleteDirectoryTree);

        var stagedSource = Path.Combine(backup, "source");
        Assert.Equal(1, validationCalls);
        Assert.True(Directory.Exists(backup));
        Assert.False(Directory.Exists(incomplete));
        Assert.Equal(
            "complete",
            File.ReadAllText(Path.Combine(backup, ApplicationUpdateRollback.CompleteMarkerFileName)));
        Assert.True(File.Exists(Path.Combine(stagedSource, "crystal-relay-update.json")));
        Assert.True(File.Exists(Path.Combine(stagedSource, ExpectedEntryExecutableName)));
    }

    [Fact]
    public async Task AcknowledgementHelpers_RejectMissingAndWrongVersionThenAcceptExactVersion()
    {
        using var folder = TemporaryFolder.Create();
        var manifestPath = Path.Combine(folder.Path, "crystal-relay-apply-update.json");
        var timeout = TimeSpan.FromMilliseconds(100);

        using var cancellation = new CancellationTokenSource(TimeSpan.FromSeconds(5));
        await Assert.ThrowsAsync<TimeoutException>(() =>
            ApplicationUpdateRollback.WaitForStartupAcknowledgementAsync(
                manifestPath,
                "3.2.1",
                timeout,
                cancellation.Token));

        ApplicationUpdateRollback.WriteStartupAcknowledgement(manifestPath, "3.2.0");
        await Assert.ThrowsAsync<TimeoutException>(() =>
            ApplicationUpdateRollback.WaitForStartupAcknowledgementAsync(
                manifestPath,
                "3.2.1",
                timeout,
                cancellation.Token));

        ApplicationUpdateRollback.WriteStartupAcknowledgement(manifestPath, "3.2.1");
        await ApplicationUpdateRollback.WaitForStartupAcknowledgementAsync(
            manifestPath,
            "3.2.1",
            timeout,
            cancellation.Token);
    }

    [Fact]
    public void StartupAcknowledgementPath_UsesDistinctApplyManifestSessions()
    {
        using var folder = TemporaryFolder.Create();
        var firstManifestPath = Path.Combine(
            folder.Path,
            "Updates",
            "session-one",
            "crystal-relay-apply-update.json");
        var secondManifestPath = Path.Combine(
            folder.Path,
            "Updates",
            "session-two",
            "crystal-relay-apply-update.json");

        var firstAcknowledgementPath =
            ApplicationUpdateRollback.GetStartupAcknowledgementPath(firstManifestPath);
        var secondAcknowledgementPath =
            ApplicationUpdateRollback.GetStartupAcknowledgementPath(secondManifestPath);

        Assert.NotEqual(firstAcknowledgementPath, secondAcknowledgementPath);
    }

    [Fact]
    public async Task ClearStaleAcknowledgement_RemovesExactVersionSignalBeforeApply()
    {
        using var folder = TemporaryFolder.Create();
        var manifestPath = Path.Combine(
            folder.Path,
            "Updates",
            "3.2.1-20260802120000000-session",
            "crystal-relay-apply-update.json");
        var acknowledgementPath =
            ApplicationUpdateRollback.GetStartupAcknowledgementPath(manifestPath);

        ApplicationUpdateRollback.WriteStartupAcknowledgement(manifestPath, "3.2.1");
        Assert.True(File.Exists(acknowledgementPath));

        ApplicationUpdateRollback.ClearStaleStartupAcknowledgement(manifestPath);

        Assert.False(File.Exists(acknowledgementPath));
        await Assert.ThrowsAsync<TimeoutException>(() =>
            ApplicationUpdateRollback.WaitForStartupAcknowledgementAsync(
                manifestPath,
                "3.2.1",
                TimeSpan.FromMilliseconds(50)));
    }

    private const string ExpectedEntryExecutableName = "Crystal Relay.exe";
    private const string CrystalRelayManifest =
        "{\"product\":\"Crystal Relay\",\"runtime\":\"win-x64\",\"channel\":\"stable\",\"version\":\"3.2.0\",\"entryExecutableName\":\"Crystal Relay.exe\"}";

    private static string CreateCrystalRelayPackage(string parentDirectory, string directoryName)
    {
        var packageDirectory = Path.Combine(parentDirectory, directoryName);
        Directory.CreateDirectory(packageDirectory);
        File.WriteAllText(
            Path.Combine(packageDirectory, "crystal-relay-update.json"),
            CrystalRelayManifest);
        File.WriteAllText(
            Path.Combine(packageDirectory, ExpectedEntryExecutableName),
            "Crystal Relay entry");
        return packageDirectory;
    }

    private static void ValidateCrystalRelayPackage(string packageDirectory)
    {
        var manifestPath = Path.Combine(packageDirectory, "crystal-relay-update.json");
        using var manifest = JsonDocument.Parse(File.ReadAllText(manifestPath));
        var root = manifest.RootElement;

        Assert.Equal("Crystal Relay", root.GetProperty("product").GetString());
        Assert.Equal("win-x64", root.GetProperty("runtime").GetString());
        Assert.Equal("stable", root.GetProperty("channel").GetString());
        Assert.Equal("3.2.0", root.GetProperty("version").GetString());
        Assert.Equal(
            ExpectedEntryExecutableName,
            root.GetProperty("entryExecutableName").GetString());
        Assert.True(File.Exists(Path.Combine(packageDirectory, ExpectedEntryExecutableName)));
    }

    private static void CopyDirectoryContents(string sourceDirectory, string targetDirectory)
    {
        Directory.CreateDirectory(targetDirectory);
        foreach (var sourceFile in EnumerateFilesRecursively(sourceDirectory))
        {
            var relativePath = Path.GetRelativePath(sourceDirectory, sourceFile);
            var targetFile = Path.Combine(targetDirectory, relativePath);
            Directory.CreateDirectory(Path.GetDirectoryName(targetFile)!);
            File.Copy(sourceFile, targetFile);
        }
    }

    private static IEnumerable<string> EnumerateFilesRecursively(string directory)
    {
        foreach (var filePath in Directory.EnumerateFiles(directory))
        {
            yield return filePath;
        }

        foreach (var subDirectory in Directory.EnumerateDirectories(directory))
        {
            foreach (var filePath in EnumerateFilesRecursively(subDirectory))
            {
                yield return filePath;
            }
        }
    }

    private static void DeleteDirectoryTree(string directory)
    {
        if (Directory.Exists(directory))
        {
            Directory.Delete(directory, recursive: true);
        }
    }

    private sealed class TemporaryFolder : IDisposable
    {
        private TemporaryFolder(string path) => Path = path;
        public string Path { get; }

        public static TemporaryFolder Create()
        {
            var path = System.IO.Path.Combine(
                System.IO.Path.GetTempPath(),
                $"CrystalRelayRollback-{Guid.NewGuid():N}");
            Directory.CreateDirectory(path);
            return new TemporaryFolder(path);
        }

        public void Dispose()
        {
            if (Directory.Exists(Path))
            {
                Directory.Delete(Path, recursive: true);
            }
        }
    }
}
