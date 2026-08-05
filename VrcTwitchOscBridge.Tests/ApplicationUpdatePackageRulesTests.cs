using System.IO;
using VrcTwitchOscBridge.Services;
using Xunit;

namespace VrcTwitchOscBridge.Tests;

public sealed class ApplicationUpdatePackageRulesTests
{
    [Theory]
    [InlineData(ApplicationUpdateChannel.Stable, "3.2.0", "CrystalRelayTwitchOsc-v3.2.0-win-x64.zip")]
    [InlineData(ApplicationUpdateChannel.Beta, "3.2.1-beta1", "CrystalRelayTwitchOsc-v3.2.1-beta1-win-x64.zip")]
    [InlineData(ApplicationUpdateChannel.BugFix, "3.2.0-bugfix2", "CrystalRelayBugFix-v3.2.0-bugfix2-win-x64.zip")]
    public void GetExpectedAssetName_UsesChannelSpecificPattern(
        ApplicationUpdateChannel channel,
        string version,
        string expected)
    {
        Assert.Equal(expected, ApplicationUpdatePackageRules.GetExpectedAssetName(channel, version));
    }

    [Theory]
    [InlineData("3.2.0-bugfix1", true, "3.2.0", 1)]
    [InlineData("3.2.0-bugfix27", true, "3.2.0", 27)]
    [InlineData("v3.2.0-bugfix1", false, "", 0)]
    [InlineData("3.2.0-bugfix", false, "", 0)]
    [InlineData("3.2.0-bugfix0", false, "", 0)]
    [InlineData("3.2.0-bugfix01", false, "", 0)]
    [InlineData("3.2.0-BugFix1", false, "", 0)]
    [InlineData(" 3.2.0-bugfix1", false, "", 0)]
    [InlineData("3.2.0-bugfix1 ", false, "", 0)]
    [InlineData("3.2.0-bugfix1-extra", false, "", 0)]
    public void TryParseBugFixVersion_RequiresExactManifestIdentity(
        string value,
        bool expected,
        string expectedBase,
        int expectedSequence)
    {
        var parsed = ApplicationUpdatePackageRules.TryParseBugFixVersion(
            value,
            out var baseVersion,
            out var sequence);

        Assert.Equal(expected, parsed);
        Assert.Equal(expectedBase, baseVersion);
        Assert.Equal(expectedSequence, sequence);
    }

    [Theory]
    [InlineData(ApplicationUpdateChannel.Stable, "stable")]
    [InlineData(ApplicationUpdateChannel.Beta, "beta")]
    [InlineData(ApplicationUpdateChannel.BugFix, "bugfix")]
    public void ManifestChannel_RoundTrips(ApplicationUpdateChannel channel, string text)
    {
        Assert.Equal(text, ApplicationUpdatePackageRules.GetManifestChannel(channel));
        Assert.True(ApplicationUpdatePackageRules.TryParseManifestChannel(text, out var parsed));
        Assert.Equal(channel, parsed);
    }

    [Theory]
    [InlineData("")]
    [InlineData("test")]
    [InlineData("hotfix")]
    [InlineData("stable-beta")]
    public void TryParseManifestChannel_RejectsUnsupportedValues(string value)
    {
        Assert.False(ApplicationUpdatePackageRules.TryParseManifestChannel(value, out _));
    }

    [Theory]
    [InlineData(ApplicationUpdateChannel.Stable, true)]
    [InlineData(ApplicationUpdateChannel.Beta, true)]
    [InlineData(ApplicationUpdateChannel.BugFix, false)]
    public void ShouldRelocateInstallFolder_KeepsBugFixInPlace(
        ApplicationUpdateChannel channel,
        bool expected)
    {
        Assert.Equal(expected, ApplicationUpdatePackageRules.ShouldRelocateInstallFolder(channel));
    }

    [Theory]
    [InlineData(ApplicationUpdateChannel.Stable, "3.2.0", "Crystal Relay.exe")]
    [InlineData(ApplicationUpdateChannel.Beta, "3.2.1-beta1", "Crystal Relay.exe")]
    [InlineData(ApplicationUpdateChannel.BugFix, "3.2.0-bugfix2", "Crystal Relay.exe")]
    public void GetExpectedEntryExecutableName_UsesStaticNameForEveryReleaseChannel(
        ApplicationUpdateChannel channel,
        string version,
        string expected)
    {
        Assert.Equal(
            expected,
            ApplicationUpdatePackageRules.GetExpectedEntryExecutableName(channel, version));
    }

    [Theory]
    [InlineData("Crystal Relay.exe", true)]
    [InlineData("CrystalRelayTwitchOsc-v3.2.0-bugfix1.exe", true)]
    [InlineData("CrystalRelayUpdater.exe", false)]
    [InlineData("CrystalRelayTwitchOsc-v3.2.0-bugfix1.dll", false)]
    public void IsApplicationExecutableName_AcceptsStaticAndVersionedAppNames(
        string fileName,
        bool expected)
    {
        Assert.Equal(expected, ApplicationUpdatePackageRules.IsApplicationExecutableName(fileName));
    }

    [Fact]
    public void BugFixEntryAndMarker_MustMatchExactSequence()
    {
        Assert.True(ApplicationUpdatePackageRules.IsExpectedEntryExecutableName(
            ApplicationUpdateChannel.BugFix,
            "3.2.0-bugfix2",
            "Crystal Relay.exe"));
        Assert.False(ApplicationUpdatePackageRules.IsExpectedEntryExecutableName(
            ApplicationUpdateChannel.BugFix,
            "3.2.0-bugfix2",
            "CrystalRelayTwitchOsc-v3.2.0-bugfix1.exe"));
        Assert.True(ApplicationUpdatePackageRules.IsExpectedEntryExecutableName(
            ApplicationUpdateChannel.Stable,
            "3.2.0",
            "Crystal Relay.exe"));
        Assert.False(ApplicationUpdatePackageRules.IsExpectedEntryExecutableName(
            ApplicationUpdateChannel.Stable,
            "3.2.0",
            "CrystalRelayTwitchOsc-v3.2.0.exe"));
        Assert.False(ApplicationUpdatePackageRules.IsExpectedEntryExecutableName(
            ApplicationUpdateChannel.Stable,
            "3.2.0",
            "CrystalRelayTwitchOsc-v3.1.9.exe"));
        Assert.Equal(
            "bugfix2",
            ApplicationUpdatePackageRules.GetExpectedBuildMarker(
                ApplicationUpdateChannel.BugFix,
                "3.2.0-bugfix2"));
        Assert.True(ApplicationUpdatePackageRules.IsExpectedBuildMarker(
            ApplicationUpdateChannel.BugFix,
            "3.2.0-bugfix2",
            "bugfix2"));
        Assert.False(ApplicationUpdatePackageRules.IsExpectedBuildMarker(
            ApplicationUpdateChannel.BugFix,
            "3.2.0-bugfix2",
            "bugfix1"));
    }

    [Theory]
    [InlineData(ApplicationUpdateChannel.Stable, "3.2.0", "Crystal Relay.exe")]
    [InlineData(ApplicationUpdateChannel.Stable, "3.2.0", "CrystalRelayTwitchOsc-v3.2.0.exe")]
    [InlineData(ApplicationUpdateChannel.Beta, "3.2.1-beta1", "crystalrelaytwitchosc-V3.2.1-BETA1.EXE")]
    [InlineData(ApplicationUpdateChannel.BugFix, "3.2.0-bugfix1", "CrystalRelayTwitchOsc-v3.2.0-bugfix1.exe")]
    public void IsExpectedInstalledPackageEntryExecutableName_AcceptsStaticOrExactLegacyName(
        ApplicationUpdateChannel channel,
        string version,
        string fileName)
    {
        Assert.True(ApplicationUpdatePackageRules.IsExpectedInstalledPackageEntryExecutableName(
            channel,
            version,
            fileName));
    }

    [Theory]
    [InlineData(ApplicationUpdateChannel.Stable, "3.2.0", "CrystalRelayTwitchOsc-v3.1.9.exe")]
    [InlineData(ApplicationUpdateChannel.Beta, "3.2.1-beta1", "CrystalRelayTwitchOsc-v3.2.1-beta2.exe")]
    [InlineData(ApplicationUpdateChannel.BugFix, "3.2.0-bugfix1", "CrystalRelayTwitchOsc-v3.2.0-bugfix2.exe")]
    [InlineData(ApplicationUpdateChannel.Stable, "3.2.0", "CrystalRelayUpdater.exe")]
    public void IsExpectedInstalledPackageEntryExecutableName_RejectsWrongOrUnrelatedName(
        ApplicationUpdateChannel channel,
        string version,
        string fileName)
    {
        Assert.False(ApplicationUpdatePackageRules.IsExpectedInstalledPackageEntryExecutableName(
            channel,
            version,
            fileName));
    }

    [Theory]
    [InlineData(ApplicationUpdateChannel.Stable, "3.2.0", "CrystalRelayTwitchOsc-v3.2.0.exe")]
    [InlineData(ApplicationUpdateChannel.Beta, "3.2.1-beta1", "CrystalRelayTwitchOsc-v3.2.1-beta1.exe")]
    [InlineData(ApplicationUpdateChannel.BugFix, "3.2.0-bugfix1", "CrystalRelayTwitchOsc-v3.2.0-bugfix1.exe")]
    public void IsExpectedEntryExecutableName_RejectsVersionedNameForNewManifest(
        ApplicationUpdateChannel channel,
        string version,
        string fileName)
    {
        Assert.False(ApplicationUpdatePackageRules.IsExpectedEntryExecutableName(
            channel,
            version,
            fileName));
    }

    [Theory]
    [InlineData("Crystal Relay")]
    [InlineData("CrystalRelayTwitchOsc-v3.2.0-win-x64")]
    [InlineData("CrystalRelayBugFix-v3.2.0-bugfix1-win-x64")]
    [InlineData("CrystalRelay-v3.1.9-win-x64")]
    public void IsPackageInstallFolderName_AcceptsSupportedShapes(string folderName)
    {
        Assert.True(ApplicationUpdatePackageRules.IsPackageInstallFolderName(folderName));
    }

    [Fact]
    public void GetInstallTargetDirectory_ReturnsCurrentDirectoryForBugFix()
    {
        var source = UnderSystemRoot("Apps", "Crystal Relay");
        var package = UnderSystemRoot("Staging", "CrystalRelayBugFix-v3.2.0-bugfix1-win-x64");

        var target = ApplicationUpdatePackageRules.GetInstallTargetDirectory(
            ApplicationUpdateChannel.BugFix,
            source,
            package);

        Assert.Equal(source, target, ignoreCase: true);
    }

    [Fact]
    public void GetInstallTargetDirectory_PreservesExistingStableRelocation()
    {
        var source = UnderSystemRoot("Apps", "CrystalRelayTwitchOsc-v3.1.9-win-x64");
        var package = UnderSystemRoot("Staging", "CrystalRelayTwitchOsc-v3.2.0-win-x64");

        var target = ApplicationUpdatePackageRules.GetInstallTargetDirectory(
            ApplicationUpdateChannel.Stable,
            source,
            package);

        Assert.Equal(
            UnderSystemRoot("Apps", "CrystalRelayTwitchOsc-v3.2.0-win-x64"),
            target,
            ignoreCase: true);
    }

    private static string UnderSystemRoot(params string[] parts)
    {
        var path = Path.GetPathRoot(Environment.SystemDirectory) ?? Path.DirectorySeparatorChar.ToString();
        foreach (var part in parts)
        {
            path = Path.Combine(path, part);
        }

        return Path.GetFullPath(path);
    }
}
