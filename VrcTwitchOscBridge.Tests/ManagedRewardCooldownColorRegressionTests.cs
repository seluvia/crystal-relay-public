using System.IO;
using System.Runtime.CompilerServices;
using Xunit;

namespace VrcTwitchOscBridge.Tests;

public sealed class ManagedRewardCooldownColorRegressionTests
{
    [Fact]
    public void CooldownColorPatch_WaitsForCatalogUpdateBeforeNextRewardTransition()
    {
        var source = File.ReadAllText(GetMainWindowViewModelPath());
        var handlerStart = source.IndexOf(
            "private async Task HandleRewardCooldownColorChangedAsync",
            StringComparison.Ordinal);
        var catalogUpdateStart = source.IndexOf(
            "private async Task ApplySingleRewardCatalogUpdateAsync",
            StringComparison.Ordinal);

        Assert.True(handlerStart >= 0, "The cooldown color handler should exist.");
        Assert.True(catalogUpdateStart >= 0, "The catalog update should be awaitable.");

        var handlerEnd = source.IndexOf(
            "private bool IsCreateOrManageRewardOwner",
            handlerStart,
            StringComparison.Ordinal);
        var catalogUpdateEnd = source.IndexOf(
            "// The ManagedRewardAvailabilityChanged event",
            catalogUpdateStart,
            StringComparison.Ordinal);

        Assert.True(handlerEnd > handlerStart, "The cooldown color handler should have a bounded method body.");
        Assert.True(catalogUpdateEnd > catalogUpdateStart, "The catalog update method should have a bounded method body.");

        var handler = source[handlerStart..handlerEnd];
        var catalogUpdate = source[catalogUpdateStart..catalogUpdateEnd];

        var catalogAwaitIndex = handler.IndexOf(
            "await ApplySingleRewardCatalogUpdateAsync(updatedReward);",
            StringComparison.Ordinal);
        var gateReleaseIndex = handler.IndexOf(
            "managedRewardSyncGate.Release();",
            StringComparison.Ordinal);

        Assert.True(catalogAwaitIndex >= 0, "The color handler should await its catalog update.");
        Assert.True(gateReleaseIndex > catalogAwaitIndex, "The catalog update must complete before the shared sync gate is released.");
        Assert.Contains("await dispatcher.InvokeAsync", catalogUpdate, StringComparison.Ordinal);
    }

    private static string GetMainWindowViewModelPath([CallerFilePath] string testFilePath = "")
    {
        var testDirectory = Path.GetDirectoryName(testFilePath)
            ?? throw new DirectoryNotFoundException("Could not resolve test source directory.");
        var repoRoot = Directory.GetParent(testDirectory)?.FullName
            ?? throw new DirectoryNotFoundException("Could not resolve repository root.");
        return Path.Combine(repoRoot, "VrcTwitchOscBridge", "ViewModels", "MainWindowViewModel.cs");
    }
}
