using VrcTwitchOscBridge.Services;
using Xunit;

namespace VrcTwitchOscBridge.Tests;

public sealed class SensitiveTextSanitizerTests
{
    [Fact]
    public void Sanitize_RedactsWindowsSourcePathsInStackTraces()
    {
        const string input = "   at VrcTwitchOscBridge.ViewModels.MainWindowViewModel.InitializeAsync() in D:\\ExampleWorkspace\\CrystalRelay\\VrcTwitchOscBridge\\ViewModels\\MainWindowViewModel.cs:line 3373";

        var result = SensitiveTextSanitizer.Sanitize(input);

        Assert.DoesNotContain("D:\\ExampleWorkspace\\CrystalRelay", result);
        Assert.Contains("in <local path>\\MainWindowViewModel.cs:line 3373", result);
    }

    [Fact]
    public void Sanitize_RedactsAbsoluteWindowsPathsOutsideUserProfile()
    {
        const string input = "Config file was loaded from D:\\StreamTools\\CrystalRelay\\secret.json";

        var result = SensitiveTextSanitizer.Sanitize(input);

        Assert.DoesNotContain("D:\\StreamTools", result);
        Assert.DoesNotContain("secret.json", result);
        Assert.Contains("<local path>", result);
    }

    [Fact]
    public void Sanitize_RedactsSingleSegmentAbsoluteWindowsPaths()
    {
        const string input = "Project folder is D:\\PrivateProject";

        var result = SensitiveTextSanitizer.Sanitize(input);

        Assert.DoesNotContain("D:\\PrivateProject", result);
        Assert.Contains("<local path>", result);
    }
}
