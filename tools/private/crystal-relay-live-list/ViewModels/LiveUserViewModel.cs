namespace CrystalRelayLiveList.ViewModels;

public sealed class LiveUserViewModel
{
    public LiveUserViewModel(
        string displayName,
        string twitchUrl,
        string relayVersion,
        string buildChannel,
        DateTimeOffset? lastPingAt)
    {
        DisplayName = displayName.Trim();
        TwitchUrl = twitchUrl.Trim();
        RelayVersion = relayVersion.Trim();
        BuildChannel = buildChannel.Trim();
        LastPingAt = lastPingAt?.ToUniversalTime();
        VersionBadgeText = RelayVersion;
        ChannelBadgeText = BuildChannel;
        HasVersionBadge = !string.IsNullOrWhiteSpace(RelayVersion);
        HasChannelBadge = !string.IsNullOrWhiteSpace(BuildChannel);

        DetailText = LastPingAt is { } lastPing
            ? $"Last heartbeat {lastPing.ToLocalTime():g}"
            : "Live heartbeat active.";
    }

    public string DisplayName { get; }

    public string TwitchUrl { get; }

    public string RelayVersion { get; }

    public string BuildChannel { get; }

    public DateTimeOffset? LastPingAt { get; }

    public string DetailText { get; }

    public string VersionBadgeText { get; }

    public string ChannelBadgeText { get; }

    public bool HasVersionBadge { get; }

    public bool HasChannelBadge { get; }
}
