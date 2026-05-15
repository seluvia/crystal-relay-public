using System.Globalization;
using VrcTwitchOscBridge.Models;

namespace VrcTwitchOscBridge.Services;

public sealed record DevFireSaleRequest(
    int DiscountPercent,
    int DurationSeconds,
    string UserDisplayName);

internal enum DevChatCommandKind
{
    RelativeAvatarScale,
    Movement,
    FireSale
}

internal sealed record DevChatCommand(
    DevChatCommandKind Kind,
    double RelativeHeightMeters,
    PlayerMovementDirection MovementDirection,
    int DiscountPercent,
    int DurationSeconds,
    string CommandText);

internal static class DevChatCommandParser
{
    private const string AuthorizedChatterName = "Screminpal_";

    public static bool IsDevCommandMessage(string? messageText)
    {
        var commandName = GetCommandName(messageText);
        return commandName.StartsWith("!dev", StringComparison.OrdinalIgnoreCase);
    }

    public static bool IsAuthorizedUser(string? userLogin, string? userDisplayName)
    {
        return IsAuthorizedName(userLogin) || IsAuthorizedName(userDisplayName);
    }

    public static bool TryParse(
        string? messageText,
        out DevChatCommand command,
        out string diagnostic)
    {
        command = default!;
        diagnostic = string.Empty;

        var tokens = SplitTokens(messageText);
        if (tokens.Length == 0)
        {
            diagnostic = "The dev command was empty.";
            return false;
        }

        var commandName = tokens[0].Trim();
        switch (commandName.ToLowerInvariant())
        {
            case "!devgrow":
            case "!devshrink":
                return TryParseRelativeAvatarScale(tokens, commandName, out command, out diagnostic);

            case "!devmove":
                return TryParseMovement(tokens, commandName, out command, out diagnostic);

            case "!devfiresale":
                return TryParseFireSale(tokens, commandName, out command, out diagnostic);

            default:
                diagnostic = $"Unknown dev command '{commandName}'.";
                return false;
        }
    }

    private static bool TryParseRelativeAvatarScale(
        IReadOnlyList<string> tokens,
        string commandName,
        out DevChatCommand command,
        out string diagnostic)
    {
        command = default!;
        diagnostic = string.Empty;

        if (tokens.Count != 3)
        {
            diagnostic = $"{commandName} expects <meters> <seconds>.";
            return false;
        }

        if (!TryParsePositiveDouble(tokens[1], out var meters))
        {
            diagnostic = $"{commandName} needs a positive height in meters, like 0.25.";
            return false;
        }

        if (!TryParsePositiveInt(tokens[2], out var durationSeconds))
        {
            diagnostic = $"{commandName} needs a positive duration in seconds.";
            return false;
        }

        var direction = string.Equals(commandName, "!devshrink", StringComparison.OrdinalIgnoreCase) ? -1d : 1d;
        command = new DevChatCommand(
            DevChatCommandKind.RelativeAvatarScale,
            meters * direction,
            PlayerMovementDirection.Forward,
            0,
            durationSeconds,
            commandName);
        return true;
    }

    private static bool TryParseMovement(
        IReadOnlyList<string> tokens,
        string commandName,
        out DevChatCommand command,
        out string diagnostic)
    {
        command = default!;
        diagnostic = string.Empty;

        if (tokens.Count != 3)
        {
            diagnostic = $"{commandName} expects <direction> <seconds>.";
            return false;
        }

        if (!TryParseMovementDirection(tokens[1], out var direction))
        {
            diagnostic = $"Unknown dev movement direction '{tokens[1]}'.";
            return false;
        }

        if (!TryParsePositiveInt(tokens[2], out var durationSeconds))
        {
            diagnostic = $"{commandName} needs a positive duration in seconds.";
            return false;
        }

        command = new DevChatCommand(
            DevChatCommandKind.Movement,
            0,
            direction,
            0,
            durationSeconds,
            commandName);
        return true;
    }

    private static bool TryParseFireSale(
        IReadOnlyList<string> tokens,
        string commandName,
        out DevChatCommand command,
        out string diagnostic)
    {
        command = default!;
        diagnostic = string.Empty;

        if (tokens.Count != 3)
        {
            diagnostic = $"{commandName} expects <percent> <seconds>.";
            return false;
        }

        if (!int.TryParse(tokens[1], NumberStyles.Integer, CultureInfo.InvariantCulture, out var percent)
            || percent is < 1 or > 100)
        {
            diagnostic = $"{commandName} needs a discount percent from 1 to 100.";
            return false;
        }

        if (!TryParsePositiveInt(tokens[2], out var durationSeconds))
        {
            diagnostic = $"{commandName} needs a positive duration in seconds.";
            return false;
        }

        command = new DevChatCommand(
            DevChatCommandKind.FireSale,
            0,
            PlayerMovementDirection.Forward,
            percent,
            durationSeconds,
            commandName);
        return true;
    }

    private static bool TryParseMovementDirection(string value, out PlayerMovementDirection direction)
    {
        direction = PlayerMovementDirection.Forward;
        var normalized = NormalizeAlias(value);
        switch (normalized)
        {
            case "forward":
            case "forwards":
            case "front":
            case "f":
                direction = PlayerMovementDirection.Forward;
                return true;

            case "back":
            case "backward":
            case "backwards":
            case "b":
                direction = PlayerMovementDirection.Backward;
                return true;

            case "left":
            case "l":
                direction = PlayerMovementDirection.Left;
                return true;

            case "right":
            case "r":
                direction = PlayerMovementDirection.Right;
                return true;

            case "jump":
            case "j":
                direction = PlayerMovementDirection.Jump;
                return true;

            case "spinleft":
            case "turnleft":
            case "lookleft":
            case "tl":
                direction = PlayerMovementDirection.SpinLeft;
                return true;

            case "spinright":
            case "turnright":
            case "lookright":
            case "tr":
                direction = PlayerMovementDirection.SpinRight;
                return true;

            default:
                return false;
        }
    }

    private static bool TryParsePositiveDouble(string value, out double result)
    {
        return double.TryParse(value, NumberStyles.Float, CultureInfo.InvariantCulture, out result)
            && result > 0
            && !double.IsNaN(result)
            && !double.IsInfinity(result);
    }

    private static bool TryParsePositiveInt(string value, out int result)
    {
        return int.TryParse(value, NumberStyles.Integer, CultureInfo.InvariantCulture, out result)
            && result > 0;
    }

    private static bool IsAuthorizedName(string? value)
    {
        return string.Equals(value?.Trim(), AuthorizedChatterName, StringComparison.OrdinalIgnoreCase);
    }

    private static string GetCommandName(string? messageText)
    {
        return SplitTokens(messageText).FirstOrDefault() ?? string.Empty;
    }

    private static string[] SplitTokens(string? messageText)
    {
        return (messageText ?? string.Empty)
            .Trim()
            .Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
    }

    private static string NormalizeAlias(string value)
    {
        var normalized = value.Trim().ToLowerInvariant();
        return new string(normalized.Where(char.IsLetterOrDigit).ToArray());
    }
}
