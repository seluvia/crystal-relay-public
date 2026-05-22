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
    RandomAvatarScale,
    Movement,
    RandomMovementSequence,
    FireSale
}

internal sealed record DevChatCommand(
    DevChatCommandKind Kind,
    double RelativeHeightMeters,
    PlayerMovementDirection MovementDirection,
    int DiscountPercent,
    int DurationSeconds,
    double TransitionSeconds,
    string CommandText,
    double MinimumHeightMeters = 0,
    double MaximumHeightMeters = 0);

internal static class DevChatCommandParser
{
    private const string AuthorizedChatterName = "Screminpal_";
    private const string CommandPrefix = "!screm";

    public static bool IsDevCommandMessage(string? messageText)
    {
        var tokens = SplitTokens(messageText);
        return tokens.Length > 0
            && string.Equals(tokens[0], CommandPrefix, StringComparison.OrdinalIgnoreCase);
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
            diagnostic = "The !screm command was empty.";
            return false;
        }

        if (!string.Equals(tokens[0], CommandPrefix, StringComparison.OrdinalIgnoreCase))
        {
            diagnostic = $"Dev commands now use {CommandPrefix} <command>.";
            return false;
        }

        if (tokens.Length < 2)
        {
            diagnostic = $"{CommandPrefix} expects a command name.";
            return false;
        }

        var commandName = tokens[1].Trim();
        var commandText = $"{CommandPrefix} {commandName}";
        var commandTokens = tokens.Skip(1).ToArray();
        switch (commandName.ToLowerInvariant())
        {
            case "grow":
            case "shrink":
                return TryParseRelativeAvatarScale(commandTokens, commandText, out command, out diagnostic);

            case "scalerandom":
                return TryParseRandomAvatarScale(commandTokens, commandText, out command, out diagnostic);

            case "move":
                return TryParseMovement(commandTokens, commandText, out command, out diagnostic);

            case "moverandom":
                return TryParseRandomMovementSequence(commandTokens, commandText, out command, out diagnostic);

            case "firesale":
                return TryParseFireSale(commandTokens, commandText, out command, out diagnostic);

            default:
                diagnostic = $"Unknown !screm command '{commandName}'.";
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

        if (tokens.Count is not 3 and not 4)
        {
            diagnostic = $"{commandName} expects <meters> <seconds> [transitionSeconds].";
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

        var transitionSeconds = 0d;
        if (tokens.Count == 4
            && !TryParseNonNegativeDouble(tokens[3], out transitionSeconds))
        {
            diagnostic = $"{commandName} needs a non-negative transition duration in seconds.";
            return false;
        }

        var direction = commandName.EndsWith(" shrink", StringComparison.OrdinalIgnoreCase) ? -1d : 1d;
        command = new DevChatCommand(
            DevChatCommandKind.RelativeAvatarScale,
            meters * direction,
            PlayerMovementDirection.Forward,
            0,
            durationSeconds,
            Math.Clamp(transitionSeconds, 0, 30),
            commandName);
        return true;
    }

    private static bool TryParseRandomAvatarScale(
        IReadOnlyList<string> tokens,
        string commandName,
        out DevChatCommand command,
        out string diagnostic)
    {
        command = default!;
        diagnostic = string.Empty;

        string rangeText;
        string durationText;
        if (tokens.Count == 3)
        {
            rangeText = tokens[1];
            durationText = tokens[2];
        }
        else if (tokens.Count == 5 && tokens[2] == "-")
        {
            rangeText = $"{tokens[1]}-{tokens[3]}";
            durationText = tokens[4];
        }
        else
        {
            diagnostic = $"{commandName} expects <minHeight-maxHeight> <seconds>.";
            return false;
        }

        if (!TryParsePositiveHeightRange(rangeText, out var minimumHeightMeters, out var maximumHeightMeters))
        {
            diagnostic = $"{commandName} needs two different positive heights in meters, like 0.5-2.0.";
            return false;
        }

        if (!TryParsePositiveInt(durationText, out var durationSeconds))
        {
            diagnostic = $"{commandName} needs a positive duration in seconds.";
            return false;
        }

        command = new DevChatCommand(
            DevChatCommandKind.RandomAvatarScale,
            0,
            PlayerMovementDirection.Forward,
            0,
            durationSeconds,
            0,
            commandName,
            minimumHeightMeters,
            maximumHeightMeters);
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
            0,
            commandName);
        return true;
    }

    private static bool TryParseRandomMovementSequence(
        IReadOnlyList<string> tokens,
        string commandName,
        out DevChatCommand command,
        out string diagnostic)
    {
        command = default!;
        diagnostic = string.Empty;

        if (tokens.Count != 2)
        {
            diagnostic = $"{commandName} expects <seconds>.";
            return false;
        }

        if (!TryParsePositiveInt(tokens[1], out var durationSeconds))
        {
            diagnostic = $"{commandName} needs a positive duration in seconds.";
            return false;
        }

        command = new DevChatCommand(
            DevChatCommandKind.RandomMovementSequence,
            0,
            PlayerMovementDirection.Forward,
            0,
            durationSeconds,
            0,
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
            0,
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

    private static bool TryParsePositiveHeightRange(
        string value,
        out double minimumHeightMeters,
        out double maximumHeightMeters)
    {
        minimumHeightMeters = 0;
        maximumHeightMeters = 0;

        var parts = value.Split('-', StringSplitOptions.TrimEntries);
        if (parts.Length != 2
            || !TryParsePositiveDouble(parts[0], out var firstHeight)
            || !TryParsePositiveDouble(parts[1], out var secondHeight)
            || Math.Abs(firstHeight - secondHeight) < double.Epsilon)
        {
            return false;
        }

        minimumHeightMeters = Math.Min(firstHeight, secondHeight);
        maximumHeightMeters = Math.Max(firstHeight, secondHeight);
        return true;
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

    private static bool TryParseNonNegativeDouble(string value, out double result)
    {
        return double.TryParse(value, NumberStyles.Float, CultureInfo.InvariantCulture, out result)
            && result >= 0
            && !double.IsNaN(result)
            && !double.IsInfinity(result);
    }

    private static bool IsAuthorizedName(string? value)
    {
        return string.Equals(value?.Trim(), AuthorizedChatterName, StringComparison.OrdinalIgnoreCase);
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
