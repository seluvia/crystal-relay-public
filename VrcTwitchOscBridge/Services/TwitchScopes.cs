namespace VrcTwitchOscBridge.Services;

public static class TwitchScopes
{
    public const string RewardManagement = "channel:manage:redemptions";
    public const string UserEmotes = "user:read:emotes";
    public const string FollowRead = "moderator:read:followers";

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
        "user:read:chat",
        FollowRead,
        UserEmotes
    ];

    public static readonly string[] Bot =
    [
        "user:write:chat"
    ];
}
