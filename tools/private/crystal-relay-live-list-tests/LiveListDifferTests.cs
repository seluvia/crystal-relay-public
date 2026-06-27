using System.Collections.ObjectModel;
using CrystalRelayLiveList.Services;
using CrystalRelayLiveList.ViewModels;
using Xunit;

namespace CrystalRelayLiveList.Tests;

public sealed class LiveListDifferTests
{
    private static LiveUserViewModel User(string name, string url, string ver = "1.0") =>
        new(name, url, ver, "stable", DateTimeOffset.UtcNow);

    private static string Key(LiveUserViewModel u) => LiveUserKey.Normalize(u.TwitchUrl, u.DisplayName);

    [Fact]
    public void Apply_AddsNewUsers()
    {
        var users = new ObservableCollection<LiveUserViewModel>();
        var incoming = new[] { User("A", "https://www.twitch.tv/a"), User("B", "https://www.twitch.tv/b") };
        var diff = LiveListDiffer.Diff(users, incoming, Key, (a, b) => false);

        LiveListDiffer.Apply(users, diff, Key);

        Assert.Equal(2, users.Count);
    }

    [Fact]
    public void Apply_RemovesMissingUsers()
    {
        var users = new ObservableCollection<LiveUserViewModel>
        {
            User("A", "https://www.twitch.tv/a"),
            User("B", "https://www.twitch.tv/b")
        };
        var incoming = new[] { User("A", "https://www.twitch.tv/a") };
        var diff = LiveListDiffer.Diff(users, incoming, Key, (a, b) => true);

        LiveListDiffer.Apply(users, diff, Key);

        Assert.Single(users);
        Assert.Equal("A", users[0].DisplayName);
    }

    [Fact]
    public void Apply_KeepsCountOne_WhenVersionChanges()
    {
        var users = new ObservableCollection<LiveUserViewModel> { User("A", "https://www.twitch.tv/a") };
        var incoming = new[] { User("A", "https://www.twitch.tv/a", "2.0") };
        var diff = LiveListDiffer.Diff(users, incoming, Key, (a, b) => string.Equals(a.RelayVersion, b.RelayVersion, StringComparison.Ordinal));

        LiveListDiffer.Apply(users, diff, Key);

        Assert.Single(users);
        Assert.Equal("2.0", users[0].RelayVersion);
    }

    [Fact]
    public void Apply_NoChanges_LeavesInstanceUntouched()
    {
        var a = User("A", "https://www.twitch.tv/a");
        var users = new ObservableCollection<LiveUserViewModel> { a };
        var incoming = new[] { User("A", "https://www.twitch.tv/a", a.RelayVersion) };
        var diff = LiveListDiffer.Diff(users, incoming, Key, (a, b) => string.Equals(a.RelayVersion, b.RelayVersion, StringComparison.Ordinal));

        LiveListDiffer.Apply(users, diff, Key);

        Assert.Same(a, users[0]);
    }
}
