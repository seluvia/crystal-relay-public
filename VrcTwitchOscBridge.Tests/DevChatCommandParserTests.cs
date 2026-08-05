using VrcTwitchOscBridge.Services;
using Xunit;

namespace VrcTwitchOscBridge.Tests;

public sealed class DevChatCommandParserTests
{
    [Fact]
    public void TryParse_ScaleSetCapturesHeightAndTransition()
    {
        var parsed = DevChatCommandParser.TryParse(
            "!screm scaleset 2.25 0.75",
            out var command,
            out var diagnostic);

        Assert.True(parsed, diagnostic);
        Assert.Equal(DevChatCommandKind.SetAvatarScale, command.Kind);
        Assert.Equal(2.25, command.SetHeightMeters);
        Assert.Equal(0.75, command.TransitionSeconds);
        Assert.Equal("!screm scaleset", command.CommandText);
    }

    [Fact]
    public void TryParse_ScaleSetNormalizesTransitionToDeveloperMaximum()
    {
        var parsed = DevChatCommandParser.TryParse(
            "!screm scaleset 1.6 45",
            out var command,
            out var diagnostic);

        Assert.True(parsed, diagnostic);
        Assert.Equal(30, command.TransitionSeconds);
    }

    [Theory]
    [InlineData("!screm scaleset 0 1")]
    [InlineData("!screm scaleset -1 1")]
    [InlineData("!screm scaleset NaN 1")]
    [InlineData("!screm scaleset Infinity 1")]
    [InlineData("!screm scaleset 1 -0.1")]
    [InlineData("!screm scaleset 1 NaN")]
    [InlineData("!screm scaleset 1 Infinity")]
    [InlineData("!screm scaleset 1")]
    [InlineData("!screm scaleset 1 1 extra")]
    public void TryParse_ScaleSetRejectsInvalidArguments(string message)
    {
        Assert.False(DevChatCommandParser.TryParse(message, out _, out var diagnostic));
        Assert.NotEmpty(diagnostic);
    }

    [Fact]
    public void TryParse_ScaleSetPreservesRawPositiveHeightForRuntimeClamping()
    {
        var parsed = DevChatCommandParser.TryParse(
            "!screm scaleset 12000 0",
            out var command,
            out var diagnostic);

        Assert.True(parsed, diagnostic);
        Assert.Equal(12000, command.SetHeightMeters);
        Assert.Equal(0, command.TransitionSeconds);
    }

    [Fact]
    public void IsAuthorizedUser_PreservesLoginOrDisplayNameMatching()
    {
        Assert.True(DevChatCommandParser.IsAuthorizedUser("screminpal_", null));
        Assert.True(DevChatCommandParser.IsAuthorizedUser(null, " Screminpal_ "));
        Assert.False(DevChatCommandParser.IsAuthorizedUser("different-user", "different-name"));
    }
}
