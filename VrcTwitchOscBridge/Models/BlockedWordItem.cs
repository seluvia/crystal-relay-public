namespace VrcTwitchOscBridge.Models;

public sealed record BlockedWordItem(
    string Word,
    bool IsCustom,
    bool IsSuppressed);
