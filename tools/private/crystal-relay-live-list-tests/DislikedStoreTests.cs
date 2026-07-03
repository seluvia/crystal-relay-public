using System.IO;
using CrystalRelayLiveList.Services;
using Xunit;

namespace CrystalRelayLiveList.Tests;

public sealed class DislikedStoreTests
{
    [Fact]
    public void Toggle_AddsAndRemovesDisliked()
    {
        var dir = Path.Combine(Path.GetTempPath(), "crl-tests-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(dir);
        try
        {
            var path = Path.Combine(dir, "disliked.json");
            var store = new DislikedStore(path);

            Assert.True(store.Toggle("https://www.twitch.tv/a"));
            Assert.True(store.IsDisliked("https://www.twitch.tv/a"));
            Assert.False(store.Toggle("https://www.twitch.tv/a"));
            Assert.False(store.IsDisliked("https://www.twitch.tv/a"));
        }
        finally
        {
            Directory.Delete(dir, true);
        }
    }

    [Fact]
    public void IsDisliked_CaseInsensitive()
    {
        var dir = Path.Combine(Path.GetTempPath(), "crl-tests-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(dir);
        try
        {
            var path = Path.Combine(dir, "disliked.json");
            var store = new DislikedStore(path);
            store.Toggle("https://www.twitch.tv/casey");

            Assert.True(store.IsDisliked("https://www.twitch.tv/Casey"));
        }
        finally
        {
            Directory.Delete(dir, true);
        }
    }

    [Fact]
    public void Persisted_AcrossInstances()
    {
        var dir = Path.Combine(Path.GetTempPath(), "crl-tests-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(dir);
        try
        {
            var path = Path.Combine(dir, "disliked.json");
            new DislikedStore(path).Toggle("https://www.twitch.tv/a");

            Assert.True(new DislikedStore(path).IsDisliked("https://www.twitch.tv/a"));
        }
        finally
        {
            Directory.Delete(dir, true);
        }
    }
}
