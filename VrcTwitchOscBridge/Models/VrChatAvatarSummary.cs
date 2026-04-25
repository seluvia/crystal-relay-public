namespace VrcTwitchOscBridge.Models;

public sealed record VrChatAvatarSummary(
    string Id,
    string Name,
    string SourceLabel,
    bool IsCurrentAvatar);
