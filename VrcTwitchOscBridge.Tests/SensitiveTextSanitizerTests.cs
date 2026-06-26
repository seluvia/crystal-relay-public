using VrcTwitchOscBridge.Services;
using Xunit;

namespace VrcTwitchOscBridge.Tests;

public sealed class SensitiveTextSanitizerTests
{
    [Fact]
    public void Sanitize_RedactsWindowsSourcePathsInStackTraces()
    {
        const string input = "   at VrcTwitchOscBridge.ViewModels.MainWindowViewModel.InitializeAsync() in E:\\!!!Program to work on\\Proper Crystal Relay\\VrcTwitchOscBridge\\ViewModels\\MainWindowViewModel.cs:line 3373";

        var result = SensitiveTextSanitizer.Sanitize(input);

        Assert.DoesNotContain("E:\\!!!Program to work on\\Proper Crystal Relay", result);
        Assert.Contains("in <local path>\\MainWindowViewModel.cs:line 3373", result);
    }
}
