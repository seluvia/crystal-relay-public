using System.IO;
using CrystalRelayLiveList.Services;
using Xunit;

namespace CrystalRelayLiveList.Tests;

public sealed class FavoritesStoreTests
{
    [Fact]
    public void Toggle_AddsAndRemovesFavorite()
    {
        var dir = Path.Combine(Path.GetTempPath(), "crl-tests-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(dir);
        try
        {
            var path = Path.Combine(dir, "favorites.json");
            var store = new FavoritesStore(path);

            Assert.True(store.Toggle("https://www.twitch.tv/a"));
            Assert.True(store.IsFavorite("https://www.twitch.tv/a"));
            Assert.False(store.Toggle("https://www.twitch.tv/a"));
            Assert.False(store.IsFavorite("https://www.twitch.tv/a"));
        }
        finally
        {
            Directory.Delete(dir, true);
        }
    }

    [Fact]
    public void IsFavorite_CaseInsensitive()
    {
        var dir = Path.Combine(Path.GetTempPath(), "crl-tests-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(dir);
        try
        {
            var path = Path.Combine(dir, "favorites.json");
            var store = new FavoritesStore(path);
            store.Toggle("https://www.twitch.tv/casey");

            Assert.True(store.IsFavorite("https://www.twitch.tv/Casey"));
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
            var path = Path.Combine(dir, "favorites.json");
            new FavoritesStore(path).Toggle("https://www.twitch.tv/a");

            Assert.True(new FavoritesStore(path).IsFavorite("https://www.twitch.tv/a"));
        }
        finally
        {
            Directory.Delete(dir, true);
        }
    }
}
