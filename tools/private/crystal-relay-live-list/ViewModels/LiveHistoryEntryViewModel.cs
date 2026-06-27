using CrystalRelayLiveList.Services;

namespace CrystalRelayLiveList.ViewModels;

public sealed class LiveHistoryEntryViewModel
{
    public LiveHistoryEntryViewModel(LiveHistoryEntryRecord entry)
    {
        DisplayName = string.IsNullOrWhiteSpace(entry.DisplayName)
            ? "Unknown streamer"
            : entry.DisplayName.Trim();
        TwitchUrl = entry.TwitchUrl?.Trim() ?? string.Empty;
        RelayVersion = entry.RelayVersion?.Trim() ?? string.Empty;
        BuildChannel = entry.BuildChannel?.Trim() ?? string.Empty;
        FirstSeenLiveAt = entry.FirstSeenLiveAt.ToLocalTime();
        LastSeenLiveAt = entry.LastSeenLiveAt.ToLocalTime();
        VodUrl = LiveUserKey.BuildVodUrl(TwitchUrl);

        var details = new List<string>();
        if (!string.IsNullOrWhiteSpace(RelayVersion))
        {
            details.Add($"Crystal Relay {RelayVersion}");
        }

        if (!string.IsNullOrWhiteSpace(BuildChannel))
        {
            details.Add(BuildChannel);
        }

        details.Add($"First seen live {FirstSeenLiveAt:g}");
        details.Add($"Last heartbeat {LastSeenLiveAt:g}");
        DetailText = string.Join(Environment.NewLine, details);
    }

    public string DisplayName { get; }

    public string TwitchUrl { get; }

    public string VodUrl { get; }

    public string RelayVersion { get; }

    public string BuildChannel { get; }

    public DateTimeOffset FirstSeenLiveAt { get; }

    public DateTimeOffset LastSeenLiveAt { get; }

    public string DetailText { get; }
}
