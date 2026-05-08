namespace VrcTwitchOscBridge.Services;

public sealed class TwitchAccountReconnectRequiredException : Exception
{
    public TwitchAccountReconnectRequiredException(BridgeAccountRole accountRole, Exception innerException)
        : base($"{accountRole} Twitch login needs reconnecting.", innerException)
    {
        AccountRole = accountRole;
    }

    public BridgeAccountRole AccountRole { get; }
}
