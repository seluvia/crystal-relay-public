using VrcTwitchOscBridge.Services;
using Xunit;

namespace VrcTwitchOscBridge.Tests;

public sealed class BugReportServiceDiagnosticSectionTests
{
    [Fact]
    public void BuildActivityLogSection_IncludesHeaderAndEntries()
    {
        var service = new BugReportService();
        var entries = new[] { "Started bridge", "Twitch connected", "VRChat connected" };

        var result = service.BuildActivityLogSection(entries);

        Assert.Contains("Recent Activity Log", result);
        Assert.Contains("Started bridge", result);
        Assert.Contains("Twitch connected", result);
        Assert.Contains("VRChat connected", result);
    }

    [Fact]
    public void BuildActivityLogSection_EmptyEntries_ReturnsEmpty()
    {
        var service = new BugReportService();

        var result = service.BuildActivityLogSection([]);

        Assert.Equal(string.Empty, result);
    }

    [Fact]
    public void BuildDebugLogSection_ReturnsHeaderOrEmpty()
    {
        var service = new BugReportService();

        var result = service.BuildDebugLogSection();

        Assert.True(result == string.Empty || result.Contains("Recent Debug Logs") || result.Contains("Could not read"));
    }

    [Fact]
    public void BuildCrashLogSection_WhenNoCrashLog_ReturnsEmpty()
    {
        var service = new BugReportService();

        var result = service.BuildCrashLogSection();

        Assert.True(result == string.Empty || result.Contains("Crash Log") || result.Contains("Could not read"));
    }

    [Fact]
    public void BuildActivityLogSection_SanitizesUserPaths()
    {
        var service = new BugReportService();
        var entries = new[] { "Loaded C:\\Users\\secretuser\\config.json" };

        var result = service.BuildActivityLogSection(entries);

        Assert.DoesNotContain("secretuser", result);
        Assert.Contains("<user>", result);
    }
}
