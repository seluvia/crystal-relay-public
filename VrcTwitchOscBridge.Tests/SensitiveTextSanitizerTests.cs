using VrcTwitchOscBridge.Services;
using Xunit;

namespace VrcTwitchOscBridge.Tests;

public sealed class SensitiveTextSanitizerTests
{
    [Fact]
    public void Sanitize_RedactsWindowsSourcePathsInStackTraces()
    {
        var sourcePath = TestWindowsPaths.From('D', "ExampleWorkspace", "CrystalRelay", "VrcTwitchOscBridge", "ViewModels", "MainWindowViewModel.cs");
        var input = $"   at VrcTwitchOscBridge.ViewModels.MainWindowViewModel.InitializeAsync() in {sourcePath}:line 3373";

        var result = SensitiveTextSanitizer.Sanitize(input);

        Assert.DoesNotContain(TestWindowsPaths.From('D', "ExampleWorkspace", "CrystalRelay"), result);
        Assert.Contains("in <local path>\\MainWindowViewModel.cs:line 3373", result);
    }

    [Fact]
    public void Sanitize_RedactsAbsoluteWindowsPathsOutsideUserProfile()
    {
        var sourcePath = TestWindowsPaths.From('D', "StreamTools", "CrystalRelay", "secret.json");
        var input = $"Config file was loaded from {sourcePath}";

        var result = SensitiveTextSanitizer.Sanitize(input);

        Assert.DoesNotContain(TestWindowsPaths.From('D', "StreamTools"), result);
        Assert.DoesNotContain("secret.json", result);
        Assert.Contains("<local path>", result);
    }

    [Fact]
    public void Sanitize_RedactsSingleSegmentAbsoluteWindowsPaths()
    {
        var sourcePath = TestWindowsPaths.From('D', "PrivateProject");
        var input = $"Project folder is {sourcePath}";

        var result = SensitiveTextSanitizer.Sanitize(input);

        Assert.DoesNotContain(sourcePath, result);
        Assert.Contains("<local path>", result);
    }

    [Fact]
    public void Sanitize_RedactsAbsoluteWindowsPathsWithSpacesInFinalSegment()
    {
        var sourcePath = TestWindowsPaths.From('D', "StreamTools", "CrystalRelay", "secret file.json");
        var input = $"Config file was loaded from {sourcePath}";

        var result = SensitiveTextSanitizer.Sanitize(input);

        Assert.DoesNotContain("secret file.json", result);
        Assert.Contains("<local path>", result);
    }
}
