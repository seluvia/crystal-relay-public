using System.Text;

namespace VrcTwitchOscBridge.Services;

/// <summary>
/// Shared naming and color rules for Crystal Relay-managed Twitch rewards.
/// Keeping this in one helper makes reward matching more predictable across sync paths.
/// </summary>
public static class ManagedRewardPresentation
{
    public const string Prefix = "VRC: ";
    public const string ReadyBackgroundColor = "#22C55E";
    public const string InUseBackgroundColor = "#EF4444";

    // Managed rewards are identified by a stable title prefix so Crystal Relay can
    // tell its own rewards apart from normal broadcaster-created rewards.
    public static bool IsManagedTitle(string? title)
    {
        var normalizedTitle = title?.Trim() ?? string.Empty;
        return normalizedTitle.StartsWith(Prefix, StringComparison.OrdinalIgnoreCase);
    }

    public static string StripPrefix(string? title)
    {
        var trimmedTitle = title?.Trim() ?? string.Empty;
        if (trimmedTitle.StartsWith(Prefix, StringComparison.OrdinalIgnoreCase))
        {
            return trimmedTitle[Prefix.Length..].TrimStart();
        }

        return trimmedTitle;
    }

    public static string NormalizeTitleIdentityKey(string? title)
    {
        var strippedTitle = StripPrefix(title);
        return NormalizeCollapsedTitle(strippedTitle);
    }

    public static string NormalizeTitlePresentationKey(string? title) => NormalizeCollapsedTitle(title);

    private static string NormalizeCollapsedTitle(string? title)
    {
        var strippedTitle = title?.Trim() ?? string.Empty;
        if (string.IsNullOrWhiteSpace(strippedTitle))
        {
            return string.Empty;
        }

        var builder = new StringBuilder(strippedTitle.Length);
        var previousWasWhitespace = true;
        foreach (var character in strippedTitle)
        {
            if (char.IsWhiteSpace(character))
            {
                if (!previousWasWhitespace)
                {
                    builder.Append(' ');
                    previousWasWhitespace = true;
                }

                continue;
            }

            builder.Append(character);
            previousWasWhitespace = false;
        }

        return builder.ToString().Trim();
    }

    public static bool HasSameTitleIdentity(string? firstTitle, string? secondTitle)
    {
        var firstKey = NormalizeTitleIdentityKey(firstTitle);
        var secondKey = NormalizeTitleIdentityKey(secondTitle);
        return !string.IsNullOrWhiteSpace(firstKey)
            && !string.IsNullOrWhiteSpace(secondKey)
            && string.Equals(firstKey, secondKey, StringComparison.OrdinalIgnoreCase);
    }

    public static bool HasSameTitlePresentation(string? firstTitle, string? secondTitle)
    {
        var firstKey = NormalizeTitlePresentationKey(firstTitle);
        var secondKey = NormalizeTitlePresentationKey(secondTitle);
        return !string.IsNullOrWhiteSpace(firstKey)
            && !string.IsNullOrWhiteSpace(secondKey)
            && string.Equals(firstKey, secondKey, StringComparison.Ordinal);
    }

    // BuildTitle always normalizes the configured title into the managed reward format.
    public static string BuildTitle(string? title) => BuildTitle(title, includePrefix: true);

    public static string BuildTitle(string? title, bool includePrefix)
    {
        var baseTitle = StripPrefix(title);
        if (string.IsNullOrWhiteSpace(baseTitle))
        {
            return string.Empty;
        }

        return includePrefix ? $"{Prefix}{baseTitle}" : baseTitle;
    }

    public static string NormalizeReadyBackgroundColor(string? colorText) =>
        NormalizeBackgroundColor(colorText, ReadyBackgroundColor);

    public static string NormalizeCooldownBackgroundColor(string? colorText) =>
        NormalizeBackgroundColor(colorText, InUseBackgroundColor);

    public static bool IsValidBackgroundColor(string? colorText)
    {
        var normalizedColorText = colorText?.Trim() ?? string.Empty;
        return normalizedColorText.Length == 7
            && normalizedColorText[0] == '#'
            && normalizedColorText[1..].All(Uri.IsHexDigit);
    }

    public static string NormalizeBackgroundColor(string? colorText, string fallbackColor)
    {
        var normalizedFallbackColor = IsValidBackgroundColor(fallbackColor)
            ? fallbackColor.Trim().ToUpperInvariant()
            : ReadyBackgroundColor;
        if (!IsValidBackgroundColor(colorText))
        {
            return normalizedFallbackColor;
        }

        return colorText!.Trim().ToUpperInvariant();
    }

    public static System.Drawing.Color ToDrawingColor(string? colorText, string fallbackColor)
    {
        var normalizedColorText = NormalizeBackgroundColor(colorText, fallbackColor);
        return System.Drawing.ColorTranslator.FromHtml(normalizedColorText);
    }

    public static string ToHex(System.Drawing.Color color) => $"#{color.R:X2}{color.G:X2}{color.B:X2}";

    // Reward title matching accepts either the raw configured title or the managed prefixed title
    // so older saved data and live Twitch rewards can still line up cleanly.
    public static bool TitleMatches(string? actualTitle, string? configuredTitle)
        => HasSameTitleIdentity(actualTitle, configuredTitle);
}
