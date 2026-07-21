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
    [InlineData(ApplicationUpdateChannel.BugFix, "3.2.0-bugfix2", "CrystalRelayTwitchOsc-v3.2.0-bugfix2.exe")]
    public void GetExpectedEntryExecutableName_PreservesStableAndVersionsBugFix(
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
            "CrystalRelayTwitchOsc-v3.2.0-bugfix2.exe"));
        Assert.False(ApplicationUpdatePackageRules.IsExpectedEntryExecutableName(
            ApplicationUpdateChannel.BugFix,
            "3.2.0-bugfix2",
            "CrystalRelayTwitchOsc-v3.2.0-bugfix1.exe"));
        Assert.True(ApplicationUpdatePackageRules.IsExpectedEntryExecutableName(
            ApplicationUpdateChannel.Stable,
            "3.2.0",
            "Crystal Relay.exe"));
        Assert.True(ApplicationUpdatePackageRules.IsExpectedEntryExecutableName(
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
        var source = Path.GetFullPath(Path.Combine("C:\\", "Apps", "Crystal Relay"));
        var package = Path.GetFullPath(Path.Combine("C:\\", "Staging", "CrystalRelayBugFix-v3.2.0-bugfix1-win-x64"));

        var target = ApplicationUpdatePackageRules.GetInstallTargetDirectory(
            ApplicationUpdateChannel.BugFix,
            source,
            package);

        Assert.Equal(source, target, ignoreCase: true);
    }

    [Fact]
    public void GetInstallTargetDirectory_PreservesExistingStableRelocation()
    {
        var source = Path.GetFullPath(Path.Combine("C:\\", "Apps", "CrystalRelayTwitchOsc-v3.1.9-win-x64"));
        var package = Path.GetFullPath(Path.Combine("C:\\", "Staging", "CrystalRelayTwitchOsc-v3.2.0-win-x64"));

        var target = ApplicationUpdatePackageRules.GetInstallTargetDirectory(
            ApplicationUpdateChannel.Stable,
            source,
            package);

        Assert.Equal(
            Path.GetFullPath(Path.Combine("C:\\", "Apps", "CrystalRelayTwitchOsc-v3.2.0-win-x64")),
            target,
            ignoreCase: true);
    }
}
