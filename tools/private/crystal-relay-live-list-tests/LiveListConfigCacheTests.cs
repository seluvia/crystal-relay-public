using System.IO;
using CrystalRelayLiveList.Services;
using Xunit;

namespace CrystalRelayLiveList.Tests;

public sealed class LiveListConfigCacheTests
{
    [Fact]
    public void Invalidate_ForcesReloadOnNextResolve()
    {
        var dir = Path.Combine(Path.GetTempPath(), "crl-tests-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(dir);
        try
        {
            var localPath = Path.Combine(dir, "live-list.local.json");
            File.WriteAllText(localPath, "{\"liveApiEndpoint\":\"https://example.org/api/ping\"}");

            var cache = new LiveListConfigCache(new[] { localPath });
            var first = cache.Resolve();
            Assert.NotNull(first.Endpoint);
            cache.Invalidate();
            var second = cache.Resolve();
            Assert.NotNull(second.Endpoint);
            Assert.Equal(first.Endpoint, second.Endpoint);
        }
        finally
        {
            Directory.Delete(dir, true);
        }
    }

    [Fact]
    public void Resolve_ReturnsEmpty_WhenNoConfig()
    {
        var cache = new LiveListConfigCache(new[] { Path.Combine(Path.GetTempPath(), "does-not-exist-" + Guid.NewGuid()) });
        var resolved = cache.Resolve();
        Assert.Null(resolved.Endpoint);
        Assert.Equal(string.Empty, resolved.AlertSoundPath);
    }
}
