namespace VrcTwitchOscBridge.Models;

public sealed record BlockedWordItem(
    string Word,
    bool IsCustom,
    bool IsSuppressed)
{
    public string DisplayWord => !IsCustom && Word.Length > 2
        ? string.Concat(Word.AsSpan(0, 2), new string('*', Word.Length - 2))
        : Word;
}
