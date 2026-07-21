using System.IO;
using VrcTwitchOscBridge.Services;
using Xunit;

namespace VrcTwitchOscBridge.Tests;

public sealed class ApplicationBuildIdentityTests
{
    [Fact]
    public void Detect_NoMarkers_ReturnsStableIdentity()
    {
        using var folder = TemporaryFolder.Create();
        var identity = ApplicationBuildIdentity.Detect("3.2.0", folder.Path);

        Assert.Equal(ApplicationUpdateChannel.Stable, identity.Channel);
        Assert.Equal("3.2.0", identity.UpdateVersion);
        Assert.Equal("stable", identity.BuildChannel);
        Assert.False(identity.IsTestBuild);
    }

    [Fact]
    public void Detect_BugFixMarker_ReturnsExactSequence()
    {
        using var folder = TemporaryFolder.Create();
        File.WriteAllText(Path.Combine(folder.Path, "bugfix-build.flag"), "bugfix2");

        var identity = ApplicationBuildIdentity.Detect("3.2.0", folder.Path);

        Assert.Equal(ApplicationUpdateChannel.BugFix, identity.Channel);
        Assert.Equal("bugfix2", identity.ChannelIdentity);
        Assert.Equal("Bug Fix 2", identity.DisplayLabel);
        Assert.Equal(2, identity.BugFixSequence);
        Assert.Equal("3.2.0-bugfix2", identity.UpdateVersion);
        Assert.Equal("bugfix", identity.BuildChannel);
    }

    [Theory]
    [InlineData("")]
    [InlineData("bugfix")]
    [InlineData("bugfix0")]
    [InlineData("bugfix-1")]
    [InlineData("BugFix1")]
    [InlineData(" bugfix1")]
    [InlineData("bugfix1 ")]
    [InlineData("hotfix1")]
    public void Detect_InvalidBugFixMarker_FallsBackToStable(string marker)
    {
        using var folder = TemporaryFolder.Create();
        File.WriteAllText(Path.Combine(folder.Path, "bugfix-build.flag"), marker);

        var identity = ApplicationBuildIdentity.Detect("3.2.0", folder.Path);

        Assert.Equal(ApplicationUpdateChannel.Stable, identity.Channel);
        Assert.Equal(0, identity.BugFixSequence);
    }

    [Fact]
    public void Detect_BetaMarker_PreservesExistingBetaIdentity()
    {
        using var folder = TemporaryFolder.Create();
        File.WriteAllText(Path.Combine(folder.Path, "beta-build.flag"), "beta3");
        File.WriteAllText(Path.Combine(folder.Path, "bugfix-build.flag"), "bugfix2");

        var identity = ApplicationBuildIdentity.Detect("3.2.0", folder.Path);

        Assert.Equal(ApplicationUpdateChannel.Beta, identity.Channel);
        Assert.Equal("beta3", identity.ChannelIdentity);
        Assert.Equal("Beta 3", identity.DisplayLabel);
        Assert.Equal("3.2.0-beta3", identity.UpdateVersion);
        Assert.Equal("beta3", identity.BuildChannel);
    }

    [Fact]
    public void Detect_TestMarker_TakesPrecedenceOverOtherMarkers()
    {
        using var folder = TemporaryFolder.Create();
        File.WriteAllText(Path.Combine(folder.Path, "test-build.flag"), "test-build");
        File.WriteAllText(Path.Combine(folder.Path, "beta-build.flag"), "beta3");
        File.WriteAllText(Path.Combine(folder.Path, "bugfix-build.flag"), "bugfix2");

        var identity = ApplicationBuildIdentity.Detect("3.2.0", folder.Path);

        Assert.True(identity.IsTestBuild);
        Assert.Equal(ApplicationUpdateChannel.Stable, identity.Channel);
        Assert.Equal("test", identity.BuildChannel);
        Assert.Equal("3.2.0", identity.UpdateVersion);
    }

    private sealed class TemporaryFolder : IDisposable
    {
        private TemporaryFolder(string path) => Path = path;
        public string Path { get; }

        public static TemporaryFolder Create()
        {
            var path = System.IO.Path.Combine(System.IO.Path.GetTempPath(), $"CrystalRelayIdentity-{Guid.NewGuid():N}");
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
