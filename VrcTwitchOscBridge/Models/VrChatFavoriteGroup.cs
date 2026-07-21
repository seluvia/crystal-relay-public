namespace VrcTwitchOscBridge.Models;

public sealed record VrChatFavoriteGroup(
    string Id,
    string DisplayName,
    string Name,
    int Count
);
