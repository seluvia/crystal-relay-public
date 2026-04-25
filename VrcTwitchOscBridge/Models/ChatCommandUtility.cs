namespace VrcTwitchOscBridge.Models;

public static class ChatCommandUtility
{
    public static string Normalize(string? commandText)
    {
        var trimmed = commandText?.Trim() ?? string.Empty;
        if (string.IsNullOrWhiteSpace(trimmed))
        {
            return string.Empty;
        }

        if (!trimmed.StartsWith('!'))
        {
            trimmed = "!" + trimmed;
        }

        return trimmed.Length <= 1
            ? string.Empty
            : trimmed;
    }

    public static bool IsConfigured(string? commandText) => !string.IsNullOrWhiteSpace(Normalize(commandText));

    public static bool MessageMatches(string? commandText, string? messageText)
    {
        var normalizedCommand = Normalize(commandText);
        var normalizedMessage = messageText?.Trim() ?? string.Empty;
        if (string.IsNullOrWhiteSpace(normalizedCommand) || string.IsNullOrWhiteSpace(normalizedMessage))
        {
            return false;
        }

        return string.Equals(normalizedCommand, normalizedMessage, StringComparison.OrdinalIgnoreCase);
    }
}
