using System.Collections.Generic;
using CrystalRelayLiveList.ViewModels;

namespace CrystalRelayLiveList.Services;

public sealed class LiveHistoryStore
{
    private static readonly TimeSpan HistoryWindow = TimeSpan.FromHours(24);
    private readonly Dictionary<string, LiveHistoryEntryRecord> entries = new(StringComparer.OrdinalIgnoreCase);
    private bool dirty;

    public bool IsDirty => dirty;

    public void MarkClean() => dirty = false;

    public void Load(IEnumerable<LiveHistoryEntryRecord> loaded)
    {
        entries.Clear();
        foreach (var entry in loaded)
        {
            var key = LiveUserKey.Normalize(entry.TwitchUrl, entry.DisplayName);
            if (string.IsNullOrWhiteSpace(key))
            {
                key = entry.Key?.Trim() ?? string.Empty;
            }
            if (string.IsNullOrWhiteSpace(key))
            {
                continue;
            }
            entries[key] = CleanEntry(entry, key);
        }
        dirty = true;
    }

    public void Upsert(IEnumerable<LiveUserViewModel> liveUsers, DateTimeOffset observedAt)
    {
        foreach (var user in liveUsers)
        {
            var key = LiveUserKey.Normalize(user.TwitchUrl, user.DisplayName);
            if (string.IsNullOrWhiteSpace(key))
            {
                continue;
            }

            var lastSeen = (user.LastPingAt ?? observedAt).ToUniversalTime();
            if (entries.TryGetValue(key, out var existing))
            {
                var changed = false;
                if (!string.Equals(existing.DisplayName, user.DisplayName, StringComparison.Ordinal))
                {
                    existing.DisplayName = user.DisplayName;
                    changed = true;
                }
                if (!string.Equals(existing.TwitchUrl, user.TwitchUrl, StringComparison.Ordinal))
                {
                    existing.TwitchUrl = user.TwitchUrl;
                    changed = true;
                }
                if (!string.Equals(existing.RelayVersion, user.RelayVersion, StringComparison.Ordinal))
                {
                    existing.RelayVersion = user.RelayVersion;
                    changed = true;
                }
                if (!string.Equals(existing.BuildChannel, user.BuildChannel, StringComparison.Ordinal))
                {
                    existing.BuildChannel = user.BuildChannel;
                    changed = true;
                }
                var newLast = lastSeen > existing.LastSeenLiveAt ? lastSeen : existing.LastSeenLiveAt;
                if (newLast != existing.LastSeenLiveAt)
                {
                    existing.LastSeenLiveAt = newLast;
                    changed = true;
                }
                if (existing.LastSeenLiveAt < existing.FirstSeenLiveAt)
                {
                    existing.LastSeenLiveAt = observedAt.ToUniversalTime();
                    changed = true;
                }
                if (changed)
                {
                    dirty = true;
                }
            }
            else
            {
                var first = observedAt.ToUniversalTime();
                entries[key] = new LiveHistoryEntryRecord
                {
                    Key = key,
                    DisplayName = user.DisplayName,
                    TwitchUrl = user.TwitchUrl,
                    RelayVersion = user.RelayVersion,
                    BuildChannel = user.BuildChannel,
                    FirstSeenLiveAt = first,
                    LastSeenLiveAt = lastSeen < first ? first : lastSeen
                };
                dirty = true;
            }
        }
    }

    public void Prune(DateTimeOffset now)
    {
        var cutoff = now.ToUniversalTime() - HistoryWindow;
        var stale = new List<string>();
        foreach (var (key, entry) in entries)
        {
            if (entry.LastSeenLiveAt.ToUniversalTime() < cutoff)
            {
                stale.Add(key);
            }
        }
        if (stale.Count == 0)
        {
            return;
        }
        foreach (var key in stale)
        {
            entries.Remove(key);
        }
        dirty = true;
    }

    public IReadOnlyList<LiveHistoryEntryRecord> Snapshot() => entries.Values.ToList();

    public IReadOnlyList<LiveHistoryEntryRecord> SortedSnapshot() =>
        entries.Values
            .OrderByDescending(e => e.LastSeenLiveAt)
            .ThenBy(e => e.DisplayName, StringComparer.OrdinalIgnoreCase)
            .ToList();

    private static LiveHistoryEntryRecord CleanEntry(LiveHistoryEntryRecord entry, string key)
    {
        var clean = new LiveHistoryEntryRecord
        {
            Key = key,
            DisplayName = entry.DisplayName?.Trim() ?? string.Empty,
            TwitchUrl = entry.TwitchUrl?.Trim() ?? string.Empty,
            RelayVersion = entry.RelayVersion?.Trim() ?? string.Empty,
            BuildChannel = entry.BuildChannel?.Trim() ?? string.Empty,
            FirstSeenLiveAt = entry.FirstSeenLiveAt.ToUniversalTime(),
            LastSeenLiveAt = entry.LastSeenLiveAt.ToUniversalTime()
        };
        if (clean.FirstSeenLiveAt == default)
        {
            clean.FirstSeenLiveAt = clean.LastSeenLiveAt == default ? DateTimeOffset.UtcNow : clean.LastSeenLiveAt;
        }
        if (clean.LastSeenLiveAt == default || clean.LastSeenLiveAt < clean.FirstSeenLiveAt)
        {
            clean.LastSeenLiveAt = clean.FirstSeenLiveAt;
        }
        return clean;
    }
}
