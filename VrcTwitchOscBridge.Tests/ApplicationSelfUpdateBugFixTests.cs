using System.IO;
using System.Runtime.CompilerServices;
using VrcTwitchOscBridge.Services;
using Xunit;

namespace VrcTwitchOscBridge.Tests;

public sealed class ApplicationSelfUpdateBugFixTests
{
    [Fact]
    public void ValidateReleaseAsset_AcceptsDedicatedBugFixAsset()
    {
        var update = CreateBugFixUpdate("CrystalRelayBugFix-v3.2.0-bugfix1-win-x64.zip");
        ApplicationSelfUpdateService.ValidateReleaseAsset(update);
    }

    [Fact]
    public void ValidateReleaseAsset_RejectsStableAssetForBugFixChannel()
    {
        var update = CreateBugFixUpdate("CrystalRelayTwitchOsc-v3.2.0-bugfix1-win-x64.zip");
        Assert.Throws<ApplicationSelfUpdateException>(() =>
            ApplicationSelfUpdateService.ValidateReleaseAsset(update));
    }

    [Fact]
    public void ValidateReleaseAsset_RejectsWrongBugFixAssetCasing()
    {
        var update = CreateBugFixUpdate("crystalrelaybugfix-v3.2.0-bugfix1-win-x64.zip");
        Assert.Throws<ApplicationSelfUpdateException>(() =>
            ApplicationSelfUpdateService.ValidateReleaseAsset(update));
    }

    [Fact]
    public void ValidatePackageManifest_AcceptsBugFixChannelAndVersion()
    {
        var update = CreateBugFixUpdate("CrystalRelayBugFix-v3.2.0-bugfix1-win-x64.zip");
        var manifest = new ApplicationUpdatePackageManifest(
            "Crystal Relay",
            "3.2.0-bugfix1",
            "bugfix",
            "win-x64",
            "Crystal Relay.exe");

        ApplicationSelfUpdateService.ValidatePackageManifest(manifest, update);
    }

    [Fact]
    public void ValidatePackageManifest_AcceptsLegacyVersionedBugFixEntry()
    {
        var update = CreateBugFixUpdate("CrystalRelayBugFix-v3.2.0-bugfix1-win-x64.zip");
        var manifest = new ApplicationUpdatePackageManifest(
            "Crystal Relay",
            "3.2.0-bugfix1",
            "bugfix",
            "win-x64",
            "CrystalRelayTwitchOsc-v3.2.0-bugfix1.exe");

        ApplicationSelfUpdateService.ValidatePackageManifest(manifest, update);
    }

    [Theory]
    [InlineData("stable")]
    [InlineData("beta")]
    [InlineData("test")]
    public void ValidatePackageManifest_RejectsWrongBugFixChannel(string channel)
    {
        var update = CreateBugFixUpdate("CrystalRelayBugFix-v3.2.0-bugfix1-win-x64.zip");
        var manifest = new ApplicationUpdatePackageManifest(
            "Crystal Relay",
            "3.2.0-bugfix1",
            channel,
            "win-x64",
            "CrystalRelayTwitchOsc-v3.2.0-bugfix1.exe");

        Assert.Throws<ApplicationSelfUpdateException>(() =>
            ApplicationSelfUpdateService.ValidatePackageManifest(manifest, update));
    }

    [Theory]
    [InlineData("3.2.0-bugfix2", "CrystalRelayTwitchOsc-v3.2.0-bugfix1.exe")]
    [InlineData("3.2.0-bugfix1", "CrystalRelayTwitchOsc-v3.2.0-bugfix2.exe")]
    public void ValidatePackageManifest_RejectsWrongVersionOrEntry(
        string version,
        string entryExecutableName)
    {
        var update = CreateBugFixUpdate("CrystalRelayBugFix-v3.2.0-bugfix1-win-x64.zip");
        var manifest = new ApplicationUpdatePackageManifest(
            "Crystal Relay",
            version,
            "bugfix",
            "win-x64",
            entryExecutableName);

        Assert.Throws<ApplicationSelfUpdateException>(() =>
            ApplicationSelfUpdateService.ValidatePackageManifest(manifest, update));
    }

    [Fact]
    public void ValidatePackageMarker_RequiresExactBugFixMarker()
    {
        using var folder = TemporaryFolder.Create();
        var manifest = CreateBugFixManifest();
        File.WriteAllText(Path.Combine(folder.Path, "bugfix-build.flag"), "bugfix1");

        ApplicationSelfUpdateService.ValidatePackageMarker(folder.Path, manifest);

        File.WriteAllText(Path.Combine(folder.Path, "bugfix-build.flag"), "bugfix2");
        Assert.Throws<ApplicationSelfUpdateException>(() =>
            ApplicationSelfUpdateService.ValidatePackageMarker(folder.Path, manifest));

        File.WriteAllText(Path.Combine(folder.Path, "bugfix-build.flag"), "bugfix1\n");
        Assert.Throws<ApplicationSelfUpdateException>(() =>
            ApplicationSelfUpdateService.ValidatePackageMarker(folder.Path, manifest));
    }

    [Fact]
    public void ValidatePackageMarker_RejectsMissingBugFixMarker()
    {
        using var folder = TemporaryFolder.Create();

        Assert.Throws<ApplicationSelfUpdateException>(() =>
            ApplicationSelfUpdateService.ValidatePackageMarker(folder.Path, CreateBugFixManifest()));
    }

    [Fact]
    public void MainAndDedicatedUpdater_UseSharedInstallTargetPolicy()
    {
        var mainSource = File.ReadAllText(FindSourceFile("VrcTwitchOscBridge", "Services", "ApplicationSelfUpdateService.cs"));
        var updaterSource = File.ReadAllText(FindSourceFile("CrystalRelayUpdater", "Program.cs"));
        var updaterProject = File.ReadAllText(FindSourceFile("CrystalRelayUpdater", "CrystalRelayUpdater.csproj"));

        Assert.Contains("ApplicationUpdatePackageRules.GetInstallTargetDirectory", mainSource, StringComparison.Ordinal);
        Assert.Contains("ApplicationUpdatePackageRules.GetInstallTargetDirectory", updaterSource, StringComparison.Ordinal);
        Assert.Contains("ApplicationUpdatePackageRules.TryParseManifestChannel", mainSource, StringComparison.Ordinal);
        Assert.Contains("ApplicationUpdatePackageRules.TryParseManifestChannel", updaterSource, StringComparison.Ordinal);
        Assert.Contains("ApplicationUpdatePackageRules.IsExpectedInstalledPackageEntryExecutableName", mainSource, StringComparison.Ordinal);
        Assert.Contains("ApplicationUpdatePackageRules.IsExpectedInstalledPackageEntryExecutableName", updaterSource, StringComparison.Ordinal);
        Assert.Contains("ApplicationUpdatePackageRules.IsExpectedBuildMarker", mainSource, StringComparison.Ordinal);
        Assert.Contains("ApplicationUpdatePackageRules.IsExpectedBuildMarker", updaterSource, StringComparison.Ordinal);
        Assert.Contains("ApplicationUpdatePackageRules.IsApplicationExecutableName", mainSource, StringComparison.Ordinal);
        Assert.Contains("ApplicationUpdatePackageRules.IsApplicationExecutableName", updaterSource, StringComparison.Ordinal);
        Assert.Contains("ApplicationUpdatePackageRules.cs", updaterProject, StringComparison.Ordinal);
    }

    [Fact]
    public void DedicatedUpdater_UsesAcknowledgedTransactionalRollbackSequence()
    {
        var updaterSource = File.ReadAllText(FindSourceFile("CrystalRelayUpdater", "Program.cs"));

        Assert.Contains("ApplicationUpdateRollback.PrepareCompleteBackup", updaterSource, StringComparison.Ordinal);
        Assert.Contains("ApplicationUpdateRollback.WaitForStartupAcknowledgementAsync", updaterSource, StringComparison.Ordinal);
        Assert.Contains("ApplicationUpdateApplyTransaction.RunAsync", updaterSource, StringComparison.Ordinal);

        var restoreCallbackIndex = updaterSource.IndexOf(
            "restoreBackupAsync:",
            StringComparison.Ordinal);
        var relaunchCallbackIndex = updaterSource.IndexOf(
            "relaunchRestoredApplicationAsync:",
            StringComparison.Ordinal);
        var cleanupCallbackIndex = updaterSource.IndexOf(
            "cleanupAfterAcknowledgementAsync:",
            relaunchCallbackIndex,
            StringComparison.Ordinal);
        Assert.True(restoreCallbackIndex >= 0);
        Assert.True(relaunchCallbackIndex > restoreCallbackIndex);
        Assert.True(cleanupCallbackIndex > relaunchCallbackIndex);

        var restoreCallback = updaterSource[restoreCallbackIndex..relaunchCallbackIndex];
        var relaunchCallback = updaterSource[relaunchCallbackIndex..cleanupCallbackIndex];
        Assert.Contains("RestoreBackup(manifest, installPlan);", restoreCallback, StringComparison.Ordinal);
        Assert.DoesNotContain("TryRestoreBackup(", restoreCallback, StringComparison.Ordinal);
        Assert.Contains("LaunchRestoredApplication(manifest, installPlan);", relaunchCallback, StringComparison.Ordinal);
        Assert.DoesNotContain("TryLaunchRestoredApplication(", relaunchCallback, StringComparison.Ordinal);

        var acknowledgementIndex = updaterSource.IndexOf(
            "ApplicationUpdateRollback.WaitForStartupAcknowledgementAsync",
            StringComparison.Ordinal);
        var backupDeletionIndex = updaterSource.IndexOf(
            "TryDeleteSuccessfulUpdateBackup",
            StringComparison.Ordinal);
        Assert.True(acknowledgementIndex >= 0);
        Assert.True(backupDeletionIndex > acknowledgementIndex);
    }

    [Fact]
    public void DedicatedUpdater_ValidatesPreviousEntryBeforeRestoredLaunch()
    {
        var updaterSource = File.ReadAllText(FindSourceFile("CrystalRelayUpdater", "Program.cs"));
        var validationStart = updaterSource.IndexOf(
            "private static void ValidateApplyManifest",
            StringComparison.Ordinal);
        var launchStart = updaterSource.IndexOf(
            "private static Process LaunchRestoredApplication",
            validationStart,
            StringComparison.Ordinal);

        Assert.True(validationStart >= 0);
        Assert.True(launchStart > validationStart);

        var validation = updaterSource[validationStart..launchStart];
        Assert.Contains("string.IsNullOrWhiteSpace(manifest.PreviousEntryExecutableName)", validation, StringComparison.Ordinal);
        Assert.Contains("Path.IsPathRooted(manifest.PreviousEntryExecutableName)", validation, StringComparison.Ordinal);
        Assert.Contains("manifest.PreviousEntryExecutableName.Contains(Path.DirectorySeparatorChar)", validation, StringComparison.Ordinal);
        Assert.Contains("manifest.PreviousEntryExecutableName.Contains(Path.AltDirectorySeparatorChar)", validation, StringComparison.Ordinal);
        Assert.Contains("ApplicationUpdatePackageRules.IsApplicationExecutableName(manifest.PreviousEntryExecutableName)", validation, StringComparison.Ordinal);

        var launch = updaterSource[launchStart..];
        Assert.Contains("IsPathInside(installPlan.SourceDirectory, restoredExecutable)", launch, StringComparison.Ordinal);
    }

    [Fact]
    public void App_DefersUpdateCleanupUntilAfterMainWindowIsShown()
    {
        var appSource = File.ReadAllText(FindSourceFile("VrcTwitchOscBridge", "App.xaml.cs"));
        var cleanupRequestIndex = appSource.IndexOf(
            "if (ApplicationSelfUpdateService.TryGetCleanupRequest(e.Args, out var updateCleanupRequest))",
            StringComparison.Ordinal);
        var showWindowIndex = appSource.IndexOf("MainWindow.Show();", StringComparison.Ordinal);

        Assert.Contains("private ApplicationSelfUpdateCleanupRequest? pendingUpdateCleanupRequest;", appSource, StringComparison.Ordinal);
        Assert.Contains("pendingUpdateCleanupRequest = updateCleanupRequest;", appSource, StringComparison.Ordinal);
        Assert.DoesNotContain(
            "await ApplicationSelfUpdateService.CleanupCompletedUpdateAsync(updateCleanupRequest);",
            appSource,
            StringComparison.Ordinal);
        Assert.True(cleanupRequestIndex >= 0);
        Assert.True(showWindowIndex > cleanupRequestIndex);
    }

    [Fact]
    public void MainWindow_AcknowledgesUpdateAfterFullStartupBeforeOptionalNotices()
    {
        var mainWindowSource = File.ReadAllText(FindSourceFile("VrcTwitchOscBridge", "MainWindow.xaml.cs"));
        var initializeIndex = mainWindowSource.IndexOf("await viewModel.InitializeAsync();", StringComparison.Ordinal);
        var revealIndex = mainWindowSource.IndexOf("await RunRevealTransitionAsync();", StringComparison.Ordinal);
        var restoreWindowsIndex = mainWindowSource.IndexOf("RestoreRestartSessionWindows();", StringComparison.Ordinal);
        var acknowledgementIndex = mainWindowSource.IndexOf(
            "await app.AcknowledgePendingUpdateStartupAsync();",
            StringComparison.Ordinal);
        var updateNoticeIndex = mainWindowSource.IndexOf("QueueApplicationUpdateCheck();", restoreWindowsIndex, StringComparison.Ordinal);

        Assert.Contains("if (Application.Current is App app)", mainWindowSource, StringComparison.Ordinal);
        Assert.True(initializeIndex >= 0);
        Assert.True(revealIndex > initializeIndex);
        Assert.True(restoreWindowsIndex > revealIndex);
        Assert.True(acknowledgementIndex > restoreWindowsIndex);
        Assert.True(updateNoticeIndex > acknowledgementIndex);
    }

    [Fact]
    public void MainApplication_TerminationFailureIsSignaledAfterKillWait()
    {
        var serviceSource = File.ReadAllText(
            FindSourceFile("VrcTwitchOscBridge", "Services", "ApplicationSelfUpdateService.cs"));
        var terminationStart = serviceSource.IndexOf(
            "private static void TryTerminateProcess",
            StringComparison.Ordinal);
        var nextMethodStart = serviceSource.IndexOf(
            "private static UpdateInstallPlan CreateInstallPlan",
            terminationStart,
            StringComparison.Ordinal);

        Assert.True(terminationStart >= 0);
        Assert.True(nextMethodStart > terminationStart);

        var termination = serviceSource[terminationStart..nextMethodStart];
        Assert.Contains(
            "ApplicationUpdateRollback.TerminateProcessTreeAndWait(process, ProcessTerminationTimeout);",
            termination,
            StringComparison.Ordinal);
        Assert.Contains("catch (Exception ex)", termination, StringComparison.Ordinal);
        Assert.Contains("throw new ApplicationSelfUpdateException", termination, StringComparison.Ordinal);
    }

    [Fact]
    public void DedicatedUpdater_TerminationFailureIsSignaledAfterKillWait()
    {
        var updaterSource = File.ReadAllText(FindSourceFile("CrystalRelayUpdater", "Program.cs"));
        var terminationStart = updaterSource.IndexOf(
            "private static void TryTerminateProcess",
            StringComparison.Ordinal);
        var nextMethodStart = updaterSource.IndexOf(
            "private static UpdateInstallPlan CreateInstallPlan",
            terminationStart,
            StringComparison.Ordinal);

        Assert.True(terminationStart >= 0);
        Assert.True(nextMethodStart > terminationStart);

        var termination = updaterSource[terminationStart..nextMethodStart];
        Assert.Contains(
            "ApplicationUpdateRollback.TerminateProcessTreeAndWait(process, ProcessTerminationTimeout);",
            termination,
            StringComparison.Ordinal);
        Assert.Contains("catch (Exception ex)", termination, StringComparison.Ordinal);
        Assert.Contains("throw new UpdaterException", termination, StringComparison.Ordinal);
        var catchIndex = termination.IndexOf("catch (Exception ex)", StringComparison.Ordinal);
        Assert.True(catchIndex >= 0);
        Assert.Contains("throw new UpdaterException", termination[catchIndex..], StringComparison.Ordinal);
    }

    [Fact]
    public void BothUpdatePaths_UseSharedProcessTreeTerminationHelper()
    {
        var rollbackSource = File.ReadAllText(
            FindSourceFile("VrcTwitchOscBridge", "Services", "ApplicationUpdateRollback.cs"));
        var serviceSource = File.ReadAllText(
            FindSourceFile("VrcTwitchOscBridge", "Services", "ApplicationSelfUpdateService.cs"));
        var updaterSource = File.ReadAllText(FindSourceFile("CrystalRelayUpdater", "Program.cs"));

        Assert.Contains("TerminateProcessTreeAndWait", rollbackSource, StringComparison.Ordinal);
        Assert.Contains("process.Kill(entireProcessTree: true);", rollbackSource, StringComparison.Ordinal);
        Assert.Contains(
            "ApplicationUpdateRollback.TerminateProcessTreeAndWait(process, ProcessTerminationTimeout);",
            serviceSource,
            StringComparison.Ordinal);
        Assert.Contains(
            "ApplicationUpdateRollback.TerminateProcessTreeAndWait(process, ProcessTerminationTimeout);",
            updaterSource,
            StringComparison.Ordinal);
    }

    [Fact]
    public void RestoreHelpers_RejectDisappearingSourcesAndValidateRestoredContent()
    {
        var serviceSource = File.ReadAllText(
            FindSourceFile("VrcTwitchOscBridge", "Services", "ApplicationSelfUpdateService.cs"));
        var updaterSource = File.ReadAllText(FindSourceFile("CrystalRelayUpdater", "Program.cs"));

        foreach (var source in new[] { serviceSource, updaterSource })
        {
            var restoreStart = source.IndexOf(
                "private static void RestoreDirectoryBackup",
                StringComparison.Ordinal);
            var copyStart = source.IndexOf(
                "private static void CopyDirectoryContents",
                restoreStart,
                StringComparison.Ordinal);

            Assert.True(restoreStart >= 0);
            Assert.True(copyStart > restoreStart);
            var restore = source[restoreStart..copyStart];
            Assert.Contains("if (!Directory.Exists(backupDirectory))", restore, StringComparison.Ordinal);
            Assert.Contains("throw new", restore, StringComparison.Ordinal);
            Assert.Contains(
                "ApplicationUpdatePathSafety.ValidateDirectoryTree(backupDirectory)",
                restore,
                StringComparison.Ordinal);
            Assert.Contains(
                "ApplicationUpdatePathSafety.ValidateCopiedDirectoryTree(",
                restore,
                StringComparison.Ordinal);
            Assert.Contains("ValidatePackageContents(targetDirectory", restore, StringComparison.Ordinal);
        }
    }

    [Fact]
    public void RelocationRestore_UsesRecordedTargetPresenceInsteadOfDirectoryExistenceRace()
    {
        var rollbackSource = File.ReadAllText(
            FindSourceFile("VrcTwitchOscBridge", "Services", "ApplicationUpdateRollback.cs"));
        var serviceSource = File.ReadAllText(
            FindSourceFile("VrcTwitchOscBridge", "Services", "ApplicationSelfUpdateService.cs"));
        var updaterSource = File.ReadAllText(FindSourceFile("CrystalRelayUpdater", "Program.cs"));

        Assert.Contains("target-presence.flag", rollbackSource, StringComparison.Ordinal);
        Assert.Contains("GetRecordedTargetPresence", rollbackSource, StringComparison.Ordinal);
        foreach (var source in new[] { serviceSource, updaterSource })
        {
            Assert.Contains("GetRecordedTargetPresence", source, StringComparison.Ordinal);
            Assert.Contains("Missing target backup", source, StringComparison.Ordinal);
        }
    }

    [Fact]
    public void BugFixPreviousInstallValidation_IsChannelAgnosticButStagingRemainsStrict()
    {
        var serviceSource = File.ReadAllText(
            FindSourceFile("VrcTwitchOscBridge", "Services", "ApplicationSelfUpdateService.cs"));
        var updaterSource = File.ReadAllText(FindSourceFile("CrystalRelayUpdater", "Program.cs"));

        foreach (var source in new[] { serviceSource, updaterSource })
        {
            Assert.Contains("ValidatePackageContents(directory)", source, StringComparison.Ordinal);
            Assert.Contains("IsValidPackageContents(directory)", source, StringComparison.Ordinal);
            Assert.Contains("packageChannel != applyChannel", source, StringComparison.Ordinal);
            Assert.True(
                source.Contains("ValidatePackageMarker", StringComparison.Ordinal)
                || source.Contains("ValidateBuildMarker", StringComparison.Ordinal));
        }
    }

    [Fact]
    public void DedicatedUpdater_PostAcknowledgementCleanupChecksDeletionResults()
    {
        var updaterSource = File.ReadAllText(FindSourceFile("CrystalRelayUpdater", "Program.cs"));
        var cleanupStart = updaterSource.IndexOf(
            "cleanupAfterAcknowledgementAsync:",
            StringComparison.Ordinal);
        var cleanupEnd = updaterSource.IndexOf("});", cleanupStart, StringComparison.Ordinal);

        Assert.True(cleanupStart >= 0);
        Assert.True(cleanupEnd > cleanupStart);

        var cleanup = updaterSource[cleanupStart..cleanupEnd];
        Assert.Contains("if (!TryDeleteSuccessfulUpdateBackup(manifest.BackupDirectory)", cleanup, StringComparison.Ordinal);
        Assert.Contains("|| !TryDeletePreviousInstallDirectory(installPlan)", cleanup, StringComparison.Ordinal);
        Assert.Contains("throw new UpdaterException", cleanup, StringComparison.Ordinal);
        Assert.DoesNotContain("RestoreBackup(", cleanup, StringComparison.Ordinal);
        Assert.DoesNotContain("LaunchRestoredApplication(", cleanup, StringComparison.Ordinal);
        Assert.Contains(
            "private static bool TryDeleteSuccessfulUpdateBackup",
            updaterSource,
            StringComparison.Ordinal);
        Assert.Contains(
            "private static bool TryDeletePreviousInstallDirectory",
            updaterSource,
            StringComparison.Ordinal);
        var previousCleanupStart = updaterSource.IndexOf(
            "private static bool TryDeletePreviousInstallDirectory",
            StringComparison.Ordinal);
        var nextValidationStart = updaterSource.IndexOf(
            "private static void ValidatePackageInstallDirectory",
            previousCleanupStart,
            StringComparison.Ordinal);
        Assert.True(previousCleanupStart >= 0);
        Assert.True(nextValidationStart > previousCleanupStart);
        var previousCleanup = updaterSource[previousCleanupStart..nextValidationStart];
        var currentProcessSkipIndex = previousCleanup.IndexOf(
            "IsCurrentProcessInsideDirectory",
            StringComparison.Ordinal);
        Assert.True(currentProcessSkipIndex >= 0);
        Assert.Contains("return true;", previousCleanup[currentProcessSkipIndex..], StringComparison.Ordinal);
    }

    [Fact]
    public void UpdateBackupRootEqualityIsRejectedBeforeValidationOrDeletion()
    {
        var serviceSource = File.ReadAllText(
            FindSourceFile("VrcTwitchOscBridge", "Services", "ApplicationSelfUpdateService.cs"));
        var updaterSource = File.ReadAllText(FindSourceFile("CrystalRelayUpdater", "Program.cs"));

        foreach (var source in new[] { serviceSource, updaterSource })
        {
            Assert.Contains("AreSamePath(backupRoot, backupDirectory)", source, StringComparison.Ordinal);
            Assert.Contains("AreSamePath(backupRoot, fullBackupDirectory)", source, StringComparison.Ordinal);
            Assert.Contains("!IsPathInside(backupRoot, backupDirectory)", source, StringComparison.Ordinal);
            Assert.Contains("!IsPathInside(backupRoot, fullBackupDirectory)", source, StringComparison.Ordinal);
        }
    }

    [Fact]
    public void ManifestlessFallbackAndLegacyBackupValidationRemainChannelSafe()
    {
        var serviceSource = File.ReadAllText(
            FindSourceFile("VrcTwitchOscBridge", "Services", "ApplicationSelfUpdateService.cs"));
        var updaterSource = File.ReadAllText(FindSourceFile("CrystalRelayUpdater", "Program.cs"));

        var fallbackStart = serviceSource.IndexOf(
            "var executablePath =",
            serviceSource.IndexOf("private static UpdatePackage ResolvePackage", StringComparison.Ordinal),
            StringComparison.Ordinal);
        var fallbackEnd = serviceSource.IndexOf(
            "internal static void ValidatePackageMarker",
            fallbackStart,
            StringComparison.Ordinal);
        Assert.True(fallbackStart >= 0);
        Assert.True(fallbackEnd > fallbackStart);
        var fallback = serviceSource[fallbackStart..fallbackEnd];
        Assert.Contains("ResolveExpectedExecutable", fallback, StringComparison.Ordinal);
        Assert.Contains("private static string ResolveExpectedExecutable", serviceSource, StringComparison.Ordinal);
        Assert.Contains("IsExpectedInstalledPackageEntryExecutableName", serviceSource, StringComparison.Ordinal);

        foreach (var source in new[] { serviceSource, updaterSource })
        {
            Assert.Contains("if (!File.Exists(packageManifestPath))", source, StringComparison.Ordinal);
            Assert.True(
                source.Contains("manifestChannel == ApplicationUpdateChannel.BugFix", StringComparison.Ordinal)
                || source.Contains("applyChannel == ApplicationUpdateChannel.BugFix", StringComparison.Ordinal));

            var contentsStart = source.IndexOf(
                "private static bool TryValidatePackageContents",
                StringComparison.Ordinal);
            Assert.True(contentsStart >= 0);
            var contents = source[contentsStart..];
            Assert.Contains("ApplicationUpdatePackageRules.IsApplicationExecutableName", contents, StringComparison.Ordinal);
            Assert.Contains("!Path.IsPathRooted(fileName)", contents, StringComparison.Ordinal);
            Assert.Contains("fileName.Contains(Path.DirectorySeparatorChar)", contents, StringComparison.Ordinal);
            Assert.Contains("fileName.Contains(Path.AltDirectorySeparatorChar)", contents, StringComparison.Ordinal);
            Assert.Contains("SearchOption.TopDirectoryOnly", contents, StringComparison.Ordinal);
        }
    }

    [Fact]
    public void App_ClearsPendingCleanupOnlyAfterSuccessfulPostAcknowledgementCleanup()
    {
        var appSource = File.ReadAllText(FindSourceFile("VrcTwitchOscBridge", "App.xaml.cs"));
        var acknowledgementIndex = appSource.IndexOf(
            "await ApplicationSelfUpdateService.AcknowledgeUpdateStartupAsync(request);",
            StringComparison.Ordinal);
        var cleanupIndex = appSource.IndexOf(
            "ApplicationSelfUpdateService.CleanupCompletedUpdateAsync(request)",
            acknowledgementIndex,
            StringComparison.Ordinal);
        var clearIndex = appSource.IndexOf(
            "pendingUpdateCleanupRequest = null;",
            cleanupIndex,
            StringComparison.Ordinal);

        Assert.Contains(
            "public static async Task<bool> CleanupCompletedUpdateAsync",
            File.ReadAllText(FindSourceFile("VrcTwitchOscBridge", "Services", "ApplicationSelfUpdateService.cs")),
            StringComparison.Ordinal);
        Assert.Contains("if (!await ApplicationSelfUpdateService.CleanupCompletedUpdateAsync(request))", appSource, StringComparison.Ordinal);
        Assert.True(acknowledgementIndex >= 0);
        Assert.True(cleanupIndex > acknowledgementIndex);
        Assert.True(clearIndex > cleanupIndex);
    }

    [Fact]
    public void CleanupValidation_ReusesInstallPlanAndBindsStagedPackageIdentity()
    {
        var serviceSource = File.ReadAllText(
            FindSourceFile("VrcTwitchOscBridge", "Services", "ApplicationSelfUpdateService.cs"));
        var validationStart = serviceSource.IndexOf(
            "private static void ValidateCleanupManifest",
            StringComparison.Ordinal);
        var nextMethodStart = serviceSource.IndexOf(
            "private static void ValidateInstallDirectory",
            validationStart,
            StringComparison.Ordinal);

        Assert.True(validationStart >= 0);
        Assert.True(nextMethodStart > validationStart);

        var validation = serviceSource[validationStart..nextMethodStart];
        Assert.Contains("ValidateInstallPlan(installPlan);", validation, StringComparison.Ordinal);
        Assert.Contains("ValidateStagedPackageIdentity", validation, StringComparison.Ordinal);
        Assert.Contains("manifest.EntryExecutableName", validation, StringComparison.Ordinal);
        Assert.Contains("ValidatePackageMarker", validation, StringComparison.Ordinal);
    }

    [Fact]
    public void SharedFilesystemSafety_IsWiredIntoBothUpdatePaths()
    {
        var rollbackSource = File.ReadAllText(
            FindSourceFile("VrcTwitchOscBridge", "Services", "ApplicationUpdateRollback.cs"));
        var serviceSource = File.ReadAllText(
            FindSourceFile("VrcTwitchOscBridge", "Services", "ApplicationSelfUpdateService.cs"));
        var updaterSource = File.ReadAllText(FindSourceFile("CrystalRelayUpdater", "Program.cs"));

        Assert.Contains("internal static class ApplicationUpdatePathSafety", rollbackSource, StringComparison.Ordinal);
        Assert.Contains("ValidateCopiedDirectoryTree", rollbackSource, StringComparison.Ordinal);
        Assert.Contains("EnsureNoReparsePointInExistingAncestors", rollbackSource, StringComparison.Ordinal);
        Assert.Contains("ValidateDirectoryTreeCore", rollbackSource, StringComparison.Ordinal);
        Assert.Contains("CollectRegularFiles", rollbackSource, StringComparison.Ordinal);

        foreach (var source in new[] { serviceSource, updaterSource })
        {
            Assert.Contains("ApplicationUpdatePathSafety.ValidateDirectoryTree", source, StringComparison.Ordinal);
            Assert.Contains("ApplicationUpdatePathSafety.EnsureNoReparsePointInExistingAncestors", source, StringComparison.Ordinal);
            Assert.Contains("ApplicationUpdatePathSafety.GetRegularFiles", source, StringComparison.Ordinal);
            Assert.DoesNotContain("SearchOption.AllDirectories", source, StringComparison.Ordinal);
        }
    }

    [Fact]
    public void DedicatedUpdater_ReturnsFailureStatusWhenApplyFails()
    {
        var updaterSource = File.ReadAllText(FindSourceFile("CrystalRelayUpdater", "Program.cs"));
        var mainStart = updaterSource.IndexOf("public static async Task<int> Main", StringComparison.Ordinal);
        var applyStart = updaterSource.IndexOf("private static async Task<int> ApplyUpdateAsync", StringComparison.Ordinal);
        var catchStart = updaterSource.IndexOf("catch (Exception ex)", applyStart, StringComparison.Ordinal);

        Assert.True(mainStart >= 0);
        Assert.True(applyStart > mainStart);
        Assert.True(catchStart > applyStart);
        Assert.Contains("return await ApplyUpdateAsync(manifestPath);", updaterSource[mainStart..applyStart], StringComparison.Ordinal);
        Assert.Contains("return 1;", updaterSource[catchStart..], StringComparison.Ordinal);
        Assert.Contains("return 0;", updaterSource[applyStart..catchStart], StringComparison.Ordinal);
    }

    [Fact]
    public void UpdateArtifactNames_IncludeGuidNonceAndApplyClearsStaleAcknowledgement()
    {
        var serviceSource = File.ReadAllText(
            FindSourceFile("VrcTwitchOscBridge", "Services", "ApplicationSelfUpdateService.cs"));
        var updaterSource = File.ReadAllText(FindSourceFile("CrystalRelayUpdater", "Program.cs"));

        Assert.Contains("Guid.NewGuid():N", serviceSource, StringComparison.Ordinal);
        Assert.Contains("ClearStaleStartupAcknowledgement", serviceSource, StringComparison.Ordinal);
        Assert.Contains("ClearStaleStartupAcknowledgement", updaterSource, StringComparison.Ordinal);
    }

    private static ApplicationUpdatePackageManifest CreateBugFixManifest() => new(
        "Crystal Relay",
        "3.2.0-bugfix1",
        "bugfix",
        "win-x64",
        "Crystal Relay.exe");

    private static ApplicationUpdateInfo CreateBugFixUpdate(string assetName) => new(
        CurrentVersion: "3.2.0",
        LatestVersion: "3.2.0-bugfix1",
        LatestBaseVersion: "3.2.0",
        Channel: ApplicationUpdateChannel.BugFix,
        BugFixSequence: 1,
        ReleaseTitle: "Crystal Relay v3.2.0 Bug Fix Push 1",
        ReleaseBody: "Fix details",
        ReleasePageUrl: "https://github.com/seluvia/crystal-relay-public/releases/tag/v3.2.0-bugfix1",
        AssetName: assetName,
        AssetDownloadUrl: $"https://github.com/seluvia/crystal-relay-public/releases/download/v3.2.0-bugfix1/{assetName}",
        AssetSizeBytes: 1024,
        Sha256Digest: $"sha256:{new string('a', 64)}");

    private static string FindSourceFile(params string[] parts)
    {
        var testPath = GetTestPath();
        var testDirectory = Path.GetDirectoryName(testPath)!;
        var repoRoot = Directory.GetParent(testDirectory)!.FullName;
        return Path.Combine([repoRoot, .. parts]);
    }

    private static string GetTestPath([CallerFilePath] string path = "") => path;

    private sealed class TemporaryFolder : IDisposable
    {
        private TemporaryFolder(string path) => Path = path;
        public string Path { get; }

        public static TemporaryFolder Create()
        {
            var path = System.IO.Path.Combine(
                System.IO.Path.GetTempPath(),
                $"CrystalRelayUpdate-{Guid.NewGuid():N}");
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
