using System.IO;
using System.Runtime.CompilerServices;
using Xunit;

namespace VrcTwitchOscBridge.Tests;

public sealed class AvatarSetsManagerThreadingRegressionTests
{
    [Fact]
    public void OscFileWatcher_ReloadsParametersThroughWpfDispatcher()
    {
        var source = File.ReadAllText(GetAvatarSetsManagerViewModelPath());
        var methodStart = source.IndexOf("private async void OnOscFileChanged", StringComparison.Ordinal);
        var methodEnd = source.IndexOf("private void SetMode", methodStart, StringComparison.Ordinal);

        Assert.True(methodStart >= 0, "The OSC file watcher callback should exist.");
        Assert.True(methodEnd > methodStart, "The OSC file watcher callback should have a bounded method body.");

        var method = source[methodStart..methodEnd];
        Assert.Contains("Application.Current?.Dispatcher", method, StringComparison.Ordinal);
        Assert.Contains("await dispatcher.InvokeAsync", method, StringComparison.Ordinal);
        Assert.Contains(".Task.Unwrap()", method, StringComparison.Ordinal);
        Assert.DoesNotContain("await LoadAvailableParametersAsync();", method, StringComparison.Ordinal);
    }

    private static string GetAvatarSetsManagerViewModelPath([CallerFilePath] string testFilePath = "")
    {
        var testDirectory = Path.GetDirectoryName(testFilePath)
            ?? throw new DirectoryNotFoundException("Could not resolve test source directory.");
        var repoRoot = Directory.GetParent(testDirectory)?.FullName
            ?? throw new DirectoryNotFoundException("Could not resolve repository root.");
        return Path.Combine(repoRoot, "VrcTwitchOscBridge", "ViewModels", "AvatarSetsManagerViewModel.cs");
    }
}
