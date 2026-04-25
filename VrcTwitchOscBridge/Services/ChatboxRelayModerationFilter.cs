using System.Globalization;
using System.Text;
using System.Text.RegularExpressions;

namespace VrcTwitchOscBridge.Services;

public static class ChatboxRelayModerationFilter
{
    // Crystal Relay intentionally allows common non-racial profanity in the
    // VRChat chat relay. This filter is only for zero-tolerance racial /
    // ethnic slurs and common bypass styles around them.
    private static readonly string[] BlockedRacialTerms =
    [
        "nigger",
        "nigga",
        "coon",
        "jigaboo",
        "porchmonkey",
        "spearchucker",
        "darkie",
        "golliwog",
        "chink",
        "gook",
        "zipperhead",
        "slopehead",
        "spic",
        "wetback",
        "beaner",
        "kike",
        "hebe",
        "paki",
        "raghead",
        "towelhead",
        "cameljockey",
        "sandnigger",
        "injun",
        "redskin"
    ];

    private static readonly Regex[] BlockedPatterns = [.. BuildBlockedPatterns()];

    public static bool ContainsBlockedRacialContent(string? text)
    {
        if (string.IsNullOrWhiteSpace(text))
        {
            return false;
        }

        var normalizedText = NormalizeForMatching(text);
        if (string.IsNullOrWhiteSpace(normalizedText))
        {
            return false;
        }

        return BlockedPatterns.Any(pattern => pattern.IsMatch(normalizedText));
    }

    private static IEnumerable<Regex> BuildBlockedPatterns()
    {
        foreach (var term in BlockedRacialTerms)
        {
            var compactTerm = CollapseToLetters(term);
            if (string.IsNullOrWhiteSpace(compactTerm))
            {
                continue;
            }

            var pieces = compactTerm
                .Select(character => Regex.Escape(character.ToString()))
                .ToArray();
            var separatorPattern = @"[^a-z]*";
            var pattern = $"(?<![a-z]){string.Join(separatorPattern, pieces)}s?(?![a-z])";
            yield return new Regex(pattern, RegexOptions.Compiled | RegexOptions.CultureInvariant | RegexOptions.IgnoreCase);
        }
    }

    private static string NormalizeForMatching(string text)
    {
        var builder = new StringBuilder(text.Length);
        foreach (var character in text.Normalize(NormalizationForm.FormKD))
        {
            var category = CharUnicodeInfo.GetUnicodeCategory(character);
            if (category == UnicodeCategory.NonSpacingMark)
            {
                continue;
            }

            var mapped = MapCharacter(character);
            if (mapped is >= 'a' and <= 'z')
            {
                builder.Append(mapped);
            }
            else
            {
                builder.Append(' ');
            }
        }

        return Regex.Replace(builder.ToString(), @"\s+", " ").Trim();
    }

    private static string CollapseToLetters(string text)
    {
        var normalized = NormalizeForMatching(text);
        return new string(normalized.Where(character => character is >= 'a' and <= 'z').ToArray());
    }

    private static char MapCharacter(char character)
    {
        var lowerCharacter = char.ToLowerInvariant(character);
        return lowerCharacter switch
        {
            >= 'a' and <= 'z' => lowerCharacter,
            '0' => 'o',
            '1' => 'i',
            '3' => 'e',
            '4' => 'a',
            '5' => 's',
            '7' => 't',
            '8' => 'b',
            '9' => 'g',
            '@' => 'a',
            '$' => 's',
            '!' => 'i',
            '|' => 'i',
            '+' => 't',
            _ => ' '
        };
    }
}
