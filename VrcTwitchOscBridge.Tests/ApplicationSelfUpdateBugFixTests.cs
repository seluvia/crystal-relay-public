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
    [InlineData("3.2.0-bugfix1", "Crystal Relay.exe")]
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
        Assert.Contains("ApplicationUpdatePackageRules.IsExpectedEntryExecutableName", mainSource, StringComparison.Ordinal);
        Assert.Contains("ApplicationUpdatePackageRules.IsExpectedEntryExecutableName", updaterSource, StringComparison.Ordinal);
        Assert.Contains("ApplicationUpdatePackageRules.IsExpectedBuildMarker", mainSource, StringComparison.Ordinal);
        Assert.Contains("ApplicationUpdatePackageRules.IsExpectedBuildMarker", updaterSource, StringComparison.Ordinal);
        Assert.Contains("ApplicationUpdatePackageRules.IsApplicationExecutableName", mainSource, StringComparison.Ordinal);
        Assert.Contains("ApplicationUpdatePackageRules.IsApplicationExecutableName", updaterSource, StringComparison.Ordinal);
        Assert.Contains("ApplicationUpdatePackageRules.cs", updaterProject, StringComparison.Ordinal);
    }

    private static ApplicationUpdatePackageManifest CreateBugFixManifest() => new(
        "Crystal Relay",
        "3.2.0-bugfix1",
        "bugfix",
        "win-x64",
        "CrystalRelayTwitchOsc-v3.2.0-bugfix1.exe");

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
