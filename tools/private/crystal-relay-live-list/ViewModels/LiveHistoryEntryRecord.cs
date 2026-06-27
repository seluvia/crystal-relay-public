namespace CrystalRelayLiveList.ViewModels;

public sealed class LiveHistoryEntryRecord
{
    public string Key { get; set; } = string.Empty;

    public string DisplayName { get; set; } = string.Empty;

    public string TwitchUrl { get; set; } = string.Empty;

    public string RelayVersion { get; set; } = string.Empty;

    public string BuildChannel { get; set; } = string.Empty;

    public DateTimeOffset FirstSeenLiveAt { get; set; }

    public DateTimeOffset LastSeenLiveAt { get; set; }
}
