namespace VrcTwitchOscBridge.Models;

/// <summary>
/// Display-only projection of an AvatarTag for chip rendering in the picker
/// and the manager's Avatars tab. Carries no mutation capability.
/// </summary>
public sealed record AvatarTagDisplay(string Id, string Name, string ColorHex);
