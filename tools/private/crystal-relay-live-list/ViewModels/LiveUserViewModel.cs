using System.ComponentModel;
using System.Runtime.CompilerServices;

namespace CrystalRelayLiveList.ViewModels;

public sealed class LiveUserViewModel : INotifyPropertyChanged
{
    private bool isFavorite;
    private bool isDisliked;

    public LiveUserViewModel(
        string displayName,
        string twitchUrl,
        string relayVersion,
        string buildChannel,
        DateTimeOffset? lastPingAt,
        bool isFavorite,
        bool isDisliked)
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
        this.isFavorite = isFavorite;
        this.isDisliked = isDisliked;

        DetailText = LastPingAt is { } lastPing
            ? $"Last heartbeat {lastPing.ToLocalTime():g}"
            : "Live heartbeat active.";
    }

    public event PropertyChangedEventHandler? PropertyChanged;

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

    public bool IsFavorite
    {
        get => isFavorite;
        private set
        {
            if (isFavorite != value)
            {
                isFavorite = value;
                OnPropertyChanged();
            }
        }
    }

    public bool IsDisliked
    {
        get => isDisliked;
        private set
        {
            if (isDisliked != value)
            {
                isDisliked = value;
                OnPropertyChanged();
            }
        }
    }

    public void RefreshClassification(bool favorite, bool disliked)
    {
        IsFavorite = favorite;
        IsDisliked = disliked;
    }

    private void OnPropertyChanged([CallerMemberName] string? propertyName = null)
    {
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
    }
}
