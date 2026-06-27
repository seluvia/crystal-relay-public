using CrystalRelayLiveList.Services;
using Xunit;

namespace CrystalRelayLiveList.Tests;

public sealed class LiveUserKeyTests
{
    [Theory]
    [InlineData("https://www.twitch.tv/screminpal", "https://www.twitch.tv/screminpal")]
    [InlineData("https://twitch.tv/Screminpal", "https://www.twitch.tv/screminpal")]
    [InlineData("  https://www.twitch.tv/Casey  ", "https://www.twitch.tv/casey")]
    public void NormalizesTwitchChannelToLower(string input, string expected)
    {
        Assert.Equal(expected, LiveUserKey.Normalize(input, null));
    }

    [Theory]
    [InlineData("https://example.org/x")]
    [InlineData("not a url")]
    public void FallsBackToTrimmedUrl(string url)
    {
        Assert.Equal(string.IsNullOrWhiteSpace(url) ? string.Empty : url.Trim(), LiveUserKey.Normalize(url, null));
    }

    [Fact]
    public void EmptyUrlUsesDisplayName()
    {
        Assert.Equal("Casey", LiveUserKey.Normalize("", "Casey"));
    }

    [Theory]
    [InlineData("https://www.twitch.tv/screminpal", true, "screminpal")]
    [InlineData("https://twitch.tv/Casey/videos", false, "")]
    [InlineData("https://example.org/x", false, "")]
    public void TryGetSlug(string url, bool ok, string slug)
    {
        var result = LiveUserKey.TryGetChannelSlug(url, out var outSlug);
        Assert.Equal(ok, result);
        Assert.Equal(slug, outSlug);
    }
}
