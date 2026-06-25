using VrcTwitchOscBridge.Models;
using VrcTwitchOscBridge.Services;
using Xunit;

namespace VrcTwitchOscBridge.Tests;

public sealed class BugReportSnapshotServiceTests
{
    [Fact]
    public void Build_IncludesAllExpectedLines()
    {
        var data = new BugReportSnapshotData(
            IsBroadcasterConnected: true,
            IsBotConnected: false,
            IsVrChatConnected: true,
            OscStatusDetail: "VRChat is connected through OSCQuery.",
            CurrentAvatarName: "Ryo Adoption",
            CurrentAvatarId: "avtr_abc123def456",
            CurrentAvatarHeightMeters: 1.62,
            SelectedTheme: AppTheme.VoidCrystal,
            ThemeDisplayName: "Void Crystal",
            AppVersion: "3.1.9");

        var result = BugReportSnapshotService.Build(data);

        Assert.Contains("Crystal Relay Status Snapshot", result);
        Assert.Contains("Twitch broadcaster: Connected", result);
        Assert.Contains("Twitch bot: Disconnected", result);
        Assert.Contains("VRChat: Connected", result);
        Assert.Contains("OSC: VRChat is connected through OSCQuery.", result);
        Assert.Contains("Current avatar: Ryo Adoption", result);
        Assert.Contains("Eye height: 1.62 m", result);
        Assert.Contains("Theme: Void Crystal", result);
        Assert.Contains("App version: 3.1.9", result);
    }

    [Fact]
    public void Build_TruncatesLongAvatarId()
    {
        var longId = "avtr_" + new string('x', 30);
        var data = new BugReportSnapshotData(
            IsBroadcasterConnected: false,
            IsBotConnected: false,
            IsVrChatConnected: false,
            OscStatusDetail: string.Empty,
            CurrentAvatarName: "Test",
            CurrentAvatarId: longId,
            CurrentAvatarHeightMeters: 1.0,
            SelectedTheme: AppTheme.VoidCrystal,
            ThemeDisplayName: "Void Crystal",
            AppVersion: "3.1.9");

        var result = BugReportSnapshotService.Build(data);

        Assert.Contains("...", result);
        Assert.DoesNotContain(longId, result);
    }

    [Fact]
    public void Build_BlankAvatarName_ShowsUnknown()
    {
        var data = new BugReportSnapshotData(
            IsBroadcasterConnected: false,
            IsBotConnected: false,
            IsVrChatConnected: false,
            OscStatusDetail: string.Empty,
            CurrentAvatarName: string.Empty,
            CurrentAvatarId: string.Empty,
            CurrentAvatarHeightMeters: 0,
            SelectedTheme: AppTheme.VoidCrystal,
            ThemeDisplayName: "Void Crystal",
            AppVersion: "3.1.9");

        var result = BugReportSnapshotService.Build(data);

        Assert.Contains("Current avatar: Unknown", result);
    }

    [Fact]
    public void Build_SanitizesAvatarNameContainingPath()
    {
        var data = new BugReportSnapshotData(
            IsBroadcasterConnected: false,
            IsBotConnected: false,
            IsVrChatConnected: false,
            OscStatusDetail: string.Empty,
            CurrentAvatarName: "C:\\Users\\secret\\avatar",
            CurrentAvatarId: "avtr_123",
            CurrentAvatarHeightMeters: 1.0,
            SelectedTheme: AppTheme.VoidCrystal,
            ThemeDisplayName: "Void Crystal",
            AppVersion: "3.1.9");

        var result = BugReportSnapshotService.Build(data);

        Assert.DoesNotContain("secret", result);
        Assert.Contains("<user>", result);
    }
}
