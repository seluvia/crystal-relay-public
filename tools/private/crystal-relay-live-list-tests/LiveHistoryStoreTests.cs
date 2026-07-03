using CrystalRelayLiveList.Services;
using CrystalRelayLiveList.ViewModels;
using Xunit;

namespace CrystalRelayLiveList.Tests;

public sealed class LiveHistoryStoreTests
{
    private static LiveUserViewModel User(string name, string url, DateTimeOffset first, DateTimeOffset last) =>
        new(name, url, "1.0", "stable", last, false, false);

    private static LiveHistoryEntryRecord Entry(string name, string url, DateTimeOffset first, DateTimeOffset last) =>
        new()
        {
            Key = LiveUserKey.Normalize(url, name),
            DisplayName = name,
            TwitchUrl = url,
            FirstSeenLiveAt = first,
            LastSeenLiveAt = last
        };

    [Fact]
    public void Upsert_NewEntry_MarksDirty()
    {
        var store = new LiveHistoryStore();
        var now = DateTimeOffset.UtcNow;

        store.Upsert(new[] { User("A", "https://www.twitch.tv/a", now, now) }, now);

        Assert.True(store.IsDirty);
    }

    [Fact]
    public void Upsert_UnchangedEntry_DoesNotMarkDirty()
    {
        var now = DateTimeOffset.UtcNow;
        var store = new LiveHistoryStore();
        store.Upsert(new[] { User("A", "https://www.twitch.tv/a", now, now) }, now);
        store.MarkClean();

        store.Upsert(new[] { User("A", "https://www.twitch.tv/a", now, now) }, now);

        Assert.False(store.IsDirty);
    }

    [Fact]
    public void Prune_RemovesOlderThan24h()
    {
        var now = DateTimeOffset.UtcNow;
        var store = new LiveHistoryStore();
        store.Load(new[] { Entry("Old", "https://www.twitch.tv/old", now - TimeSpan.FromHours(25), now - TimeSpan.FromHours(25)) });
        store.MarkClean();

        store.Prune(now);

        Assert.Empty(store.Snapshot());
    }

    [Fact]
    public void SortedSnapshot_IsOrderedByLastSeenDescThenName()
    {
        var now = DateTimeOffset.UtcNow;
        var store = new LiveHistoryStore();
        store.Upsert(new[]
        {
            User("B", "https://www.twitch.tv/b", now - TimeSpan.FromHours(2), now - TimeSpan.FromHours(2)),
            User("A", "https://www.twitch.tv/a", now, now)
        }, now);

        var sorted = store.SortedSnapshot();

        Assert.Equal("A", sorted[0].DisplayName);
        Assert.Equal("B", sorted[1].DisplayName);
    }
}
