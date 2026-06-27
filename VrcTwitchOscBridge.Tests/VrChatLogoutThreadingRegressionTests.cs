using System.IO;
using System.Runtime.CompilerServices;
using Xunit;

namespace VrcTwitchOscBridge.Tests;

public sealed class VrChatLogoutThreadingRegressionTests
{
    [Fact]
    public void MainWindowViewModel_DoesNotUseConfigureAwaitFalseForUiBoundContinuations()
    {
        var source = File.ReadAllText(GetMainWindowViewModelPath());

        Assert.DoesNotContain(".ConfigureAwait(false)", source);
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
