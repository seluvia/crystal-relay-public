using System.Globalization;
using System.Text;
using System.Text.RegularExpressions;

namespace VrcTwitchOscBridge.Services;

public static class ChatboxRelayModerationFilter
{
    // Crystal Relay intentionally allows common non-racial profanity in the
    // VRChat chat relay. This filter is only for zero-tolerance slurs,
    // self-harm encouragement, harassment/doxxing patterns, and common
    // bypass styles around them.

    private static readonly string[] BlockedSlurTerms =
    [
        // Racial (24)
        "nigger", "nigga", "coon", "jigaboo", "porchmonkey", "spearchucker",
        "darkie", "golliwog", "chink", "gook", "zipperhead", "slopehead",
        "spic", "wetback", "beaner", "kike", "hebe", "paki", "raghead",
        "towelhead", "cameljockey", "sandnigger", "injun", "redskin",

        // Anti-LGBTQ+ (5)
        "faggot", "fag", "dyke", "tranny", "shemale",

        // Additional racial (4)
        "nip", "chingchong", "yid", "wop"
    ];

    private static readonly string[] BlockedHarassmentPhrases =
    [
        "kys",
        "killyourself", "endyourself", "neckyourself", "ropeyourself",
        "justkillyourself", "godie", "hopeyoudie", "youshoulddie",
        "iknowwhereyoulive", "ifoundyouraddress",
        "iknowyourrealname", "ifoundyourrealname"
    ];

    private static readonly Regex[] BlockedPatterns = [.. BuildBlockedPatterns()];

    private static readonly Regex[] DoxxingPatterns =
    [
        // US phone: XXX-XXX-XXXX, XXX.XXX.XXXX, XXX XXX XXXX
        new(@"\b\d{3}[-.\s]\d{3}[-.\s]\d{4}\b",
            RegexOptions.Compiled | RegexOptions.CultureInvariant),

        // SSN: XXX-XX-XXXX
        new(@"\b\d{3}-\d{2}-\d{4}\b",
            RegexOptions.Compiled | RegexOptions.CultureInvariant),

        // Email: standard pattern
        new(@"\b[A-Za-z0-9._%+-]+@[A-Za-z0-9.-]+\.[A-Za-z]{2,}\b",
            RegexOptions.Compiled | RegexOptions.CultureInvariant)
    ];

    public static bool ShouldBlockMessage(string? text)
    {
        if (string.IsNullOrWhiteSpace(text))
        {
            return false;
        }

        var normalizedText = NormalizeForMatching(text);
        if (!string.IsNullOrWhiteSpace(normalizedText)
            && BlockedPatterns.Any(pattern => pattern.IsMatch(normalizedText)))
        {
            return true;
        }

        var doxxingText = NormalizeForDoxxing(text);
        if (!string.IsNullOrWhiteSpace(doxxingText)
            && DoxxingPatterns.Any(pattern => pattern.IsMatch(doxxingText)))
        {
            return true;
        }

        return false;
    }

    private static IEnumerable<Regex> BuildBlockedPatterns()
    {
        var allTerms = BlockedSlurTerms.Concat(BlockedHarassmentPhrases);

        foreach (var term in allTerms)
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
            yield return new Regex(pattern,
                RegexOptions.Compiled | RegexOptions.CultureInvariant | RegexOptions.IgnoreCase);
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

    private static string NormalizeForDoxxing(string text)
    {
        var builder = new StringBuilder(text.Length);
        foreach (var character in text.Normalize(NormalizationForm.FormKD))
        {
            var category = CharUnicodeInfo.GetUnicodeCategory(character);
            if (category == UnicodeCategory.NonSpacingMark
                || category == UnicodeCategory.Control
                || category == UnicodeCategory.Format)
            {
                continue;
            }

            builder.Append(character);
        }

        return builder.ToString().Trim();
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
