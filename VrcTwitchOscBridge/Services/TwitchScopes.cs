namespace VrcTwitchOscBridge.Services;

public static class TwitchScopes
{
    public const string RewardManagement = "channel:manage:redemptions";

    public static readonly string[] BroadcasterRequired =
    [
        "bits:read",
        "channel:read:redemptions",
        "channel:read:subscriptions"
    ];

    public static readonly string[] Broadcaster =
    [
        .. BroadcasterRequired,
        RewardManagement,
        "user:read:chat"
    ];

    public static readonly string[] Bot =
    [
        "user:write:chat"
    ];
}
