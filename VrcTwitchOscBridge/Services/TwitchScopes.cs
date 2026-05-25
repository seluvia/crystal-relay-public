namespace VrcTwitchOscBridge.Services;

public static class TwitchScopes
{
    public const string RewardManagement = "channel:manage:redemptions";
    public const string UserEmotes = "user:read:emotes";
    public const string FollowRead = "moderator:read:followers";
    public const string ChatWrite = "user:write:chat";
    public const string ModerationBannedUsers = "moderator:manage:banned_users";
    public const string ModerationChatMessages = "moderator:manage:chat_messages";
    public const string ModerationSuspiciousUsers = "moderator:manage:suspicious_users";
    public const string SuspiciousUsersRead = "moderator:read:suspicious_users";

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
        ChatWrite,
        FollowRead,
        UserEmotes,
        ModerationBannedUsers,
        ModerationChatMessages,
        ModerationSuspiciousUsers,
        SuspiciousUsersRead
    ];

    public static readonly string[] Bot =
    [
        ChatWrite
    ];
}
