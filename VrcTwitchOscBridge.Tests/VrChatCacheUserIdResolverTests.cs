using System;
using VrcTwitchOscBridge.Services;
using Xunit;

namespace VrcTwitchOscBridge.Tests;

public sealed class VrChatCacheUserIdResolverTests
{
    [Fact]
    public void ResolveCacheUserId_WithSettingsUserId_ReturnsSettingsUserId()
    {
        var result = VrChatCacheUserIdResolverTestsHelpers.Resolve(
            settingsUserId: "usr_settings",
            lastInferredUserId: "usr_inferred",
            localLowScanner: () => "usr_scanner");

        Assert.Equal("usr_settings", result);
    }

    [Fact]
    public void ResolveCacheUserId_WithBlankSettingsUserId_FallsBackToLastInferred()
    {
        var scannerCalled = false;
        var result = VrChatCacheUserIdResolverTestsHelpers.Resolve(
            settingsUserId: "   ",
            lastInferredUserId: "usr_inferred",
            localLowScanner: () =>
            {
                scannerCalled = true;
                return "usr_scanner";
            });

        Assert.Equal("usr_inferred", result);
        Assert.False(scannerCalled);
    }

    [Fact]
    public void ResolveCacheUserId_WithNoSettingsOrInferred_UsesLocalLowScanner()
    {
        var result = VrChatCacheUserIdResolverTestsHelpers.Resolve(
            settingsUserId: null,
            lastInferredUserId: null,
            localLowScanner: () => "usr_scanned");

        Assert.Equal("usr_scanned", result);
    }

    [Fact]
    public void ResolveCacheUserId_WithNoSources_ReturnsNull()
    {
        var result = VrChatCacheUserIdResolverTestsHelpers.Resolve(
            settingsUserId: null,
            lastInferredUserId: null,
            localLowScanner: () => null);

        Assert.Null(result);
    }

    [Fact]
    public void ResolveCacheUserId_WithNoSourcesAndNoScanner_ReturnsNull()
    {
        var result = VrChatLocalOscCacheService.ResolveCacheUserId(
            settingsUserId: null,
            lastInferredUserId: null,
            localLowScanner: null);

        Assert.Null(result);
    }
}

internal static class VrChatCacheUserIdResolverTestsHelpers
{
    public static string? Resolve(string? settingsUserId, string? lastInferredUserId, Func<string?>? localLowScanner)
    {
        return VrChatLocalOscCacheService.ResolveCacheUserId(settingsUserId, lastInferredUserId, localLowScanner);
    }
}
