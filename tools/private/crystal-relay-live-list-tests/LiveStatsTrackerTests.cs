using CrystalRelayLiveList.Services;
using Xunit;

namespace CrystalRelayLiveList.Tests;

public sealed class LiveStatsTrackerTests
{
    [Fact]
    public void PeakLive_TracksMaximum()
    {
        var tracker = new LiveStatsTracker();
        tracker.RecordSnapshot(3);
        tracker.RecordSnapshot(5);
        tracker.RecordSnapshot(2);
        Assert.Equal(5, tracker.PeakLive);
    }

    [Fact]
    public void UniqueStreamers_AccumulatesDistinctKeys()
    {
        var tracker = new LiveStatsTracker();
        tracker.RecordSnapshot(1, new[] { "a", "b" });
        tracker.RecordSnapshot(1, new[] { "b", "c" });
        Assert.Equal(3, tracker.UniqueStreamersSeen);
    }

    [Fact]
    public void CurrentLive_IsLastSnapshot()
    {
        var tracker = new LiveStatsTracker();
        tracker.RecordSnapshot(3);
        tracker.RecordSnapshot(2);
        Assert.Equal(2, tracker.CurrentLive);
    }
}
