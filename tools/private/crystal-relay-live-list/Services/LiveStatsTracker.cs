using System.Collections.Generic;

namespace CrystalRelayLiveList.Services;

public sealed class LiveStatsTracker
{
    private readonly HashSet<string> uniqueKeys = new(StringComparer.OrdinalIgnoreCase);

    public DateTimeOffset SessionStartedAt { get; } = DateTimeOffset.UtcNow;
    public int PeakLive { get; private set; }
    public int CurrentLive { get; private set; }
    public int UniqueStreamersSeen => uniqueKeys.Count;

    public void RecordSnapshot(int liveCount, IEnumerable<string>? liveKeys = null)
    {
        CurrentLive = liveCount;
        if (liveCount > PeakLive)
        {
            PeakLive = liveCount;
        }
        if (liveKeys is not null)
        {
            foreach (var key in liveKeys)
            {
                if (!string.IsNullOrWhiteSpace(key))
                {
                    uniqueKeys.Add(key);
                }
            }
        }
    }

    public TimeSpan SessionDuration => DateTimeOffset.UtcNow - SessionStartedAt;
}
