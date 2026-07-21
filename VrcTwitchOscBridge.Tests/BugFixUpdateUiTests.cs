using System.IO;
using System.Runtime.CompilerServices;
using Xunit;

namespace VrcTwitchOscBridge.Tests;

public sealed class BugFixUpdateUiTests
{
    [Fact]
    public void MainWindow_HasDedicatedBugFixBadge()
    {
        var xaml = File.ReadAllText(FindSourceFile("VrcTwitchOscBridge", "MainWindow.xaml"));
        Assert.Contains("Visibility=\"{Binding HasBugFixBuildLabel", xaml, StringComparison.Ordinal);
        Assert.Contains("Text=\"{Binding BugFixBuildBadgeText}\"", xaml, StringComparison.Ordinal);
    }

    [Fact]
    public void MainWindowViewModel_UsesApplicationBuildIdentity()
    {
        var source = File.ReadAllText(FindSourceFile("VrcTwitchOscBridge", "ViewModels", "MainWindowViewModel.cs"));
        Assert.Contains("ApplicationBuildIdentity.Detect", source, StringComparison.Ordinal);
        Assert.Contains("AppBuildIdentity.HasBugFixLabel", source, StringComparison.Ordinal);
        Assert.Contains("AppBuildIdentity.UpdateVersion", source, StringComparison.Ordinal);
        Assert.DoesNotContain("private static string DetectBetaBuildLabel()", source, StringComparison.Ordinal);
        Assert.DoesNotContain("private static string GetAppUpdateVersion()", source, StringComparison.Ordinal);
    }

    [Fact]
    public void ThemedDialog_HasScrollableBugFixDetailsAndNonClosingLink()
    {
        var xaml = File.ReadAllText(FindSourceFile("VrcTwitchOscBridge", "ThemedDialogWindow.xaml"));
        var code = File.ReadAllText(FindSourceFile("VrcTwitchOscBridge", "ThemedDialogWindow.xaml.cs"));

        Assert.Contains("x:Name=\"DetailsScrollViewer\"", xaml, StringComparison.Ordinal);
        Assert.Contains("x:Name=\"DetailsBodyTextBlock\"", xaml, StringComparison.Ordinal);
        Assert.Contains("x:Name=\"DetailsLinkButton\"", xaml, StringComparison.Ordinal);
        Assert.Contains("ShowBugFixUpdate", code, StringComparison.Ordinal);
        var linkHandlerStart = code.IndexOf("private void OnDetailsLinkClicked", StringComparison.Ordinal);
        Assert.True(linkHandlerStart >= 0, "The details-link handler should exist.");
        var nextHandler = linkHandlerStart >= 0
            ? code.IndexOf("private void OnPrimaryClicked", linkHandlerStart, StringComparison.Ordinal)
            : -1;
        Assert.True(nextHandler > linkHandlerStart, "The primary handler should follow the details-link handler.");
        var linkHandler = code[linkHandlerStart..nextHandler];
        Assert.Contains("detailsLinkAction?.Invoke()", linkHandler, StringComparison.Ordinal);
        Assert.DoesNotContain("DialogResult", linkHandler, StringComparison.Ordinal);
        Assert.DoesNotContain("Close()", linkHandler, StringComparison.Ordinal);
    }

    [Fact]
    public void StartupUpdateFlow_HandlesBugFixBeforeBetaWithoutIgnoreCall()
    {
        var source = File.ReadAllText(FindSourceFile("VrcTwitchOscBridge", "MainWindow.xaml.cs"));
        var bugFixStart = source.IndexOf("if (availableUpdate.IsBugFix)", StringComparison.Ordinal);
        Assert.True(bugFixStart >= 0);
        var betaStart = bugFixStart >= 0
            ? source.IndexOf("if (availableUpdate.IsBeta)", bugFixStart, StringComparison.Ordinal)
            : -1;
        Assert.True(betaStart > bugFixStart);
        var bugFixBlock = source[bugFixStart..betaStart];
        Assert.Contains("ShowBugFixUpdate", bugFixBlock, StringComparison.Ordinal);
        Assert.Contains("StartApplicationSelfUpdateAsync", bugFixBlock, StringComparison.Ordinal);
        Assert.DoesNotContain("IgnoreApplicationUpdate", bugFixBlock, StringComparison.Ordinal);
        Assert.DoesNotContain("IgnoreBetaApplicationUpdatesUntilStable", bugFixBlock, StringComparison.Ordinal);
    }

    private static string FindSourceFile(params string[] parts)
    {
        var testPath = GetTestPath();
        var testDirectory = Path.GetDirectoryName(testPath)!;
        var repoRoot = Directory.GetParent(testDirectory)!.FullName;
        return Path.Combine([repoRoot, .. parts]);
    }

    private static string GetTestPath([CallerFilePath] string path = "") => path;
}
