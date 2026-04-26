using System.Text.Json;
using System.Text.RegularExpressions;

var root = args.Length > 0
    ? Path.GetFullPath(args[0])
    : Path.GetFullPath(Path.Combine(AppContext.BaseDirectory, "..", "..", "..", "..", "VrcTwitchOscBridge", "Resources", "Localization"));

if (!Directory.Exists(root))
{
    Console.Error.WriteLine($"Localization root not found: {root}");
    return 1;
}

var appRoot = Path.GetFullPath(Path.Combine(root, "..", ".."));
var languages = new[]
{
    "en-US",
    "es-ES",
    "ja-JP",
    "de-DE",
    "fr-FR",
    "pt-BR",
    "sv-SE",
    "it-IT",
    "zh-CN",
    "zh-TW",
    "ko-KR",
    "ru-RU",
    "pl-PL",
    "th-TH"
};

var failures = new List<string>();
var englishStrings = LoadLanguage(root, "en-US");
var englishKeys = englishStrings.Keys.ToHashSet(StringComparer.Ordinal);

foreach (var sourceKey in CollectSourceLocalizationKeys(appRoot).OrderBy(key => key, StringComparer.Ordinal))
{
    if (!englishKeys.Contains(sourceKey))
    {
        failures.Add($"en-US is missing source string: {sourceKey}");
    }
}

foreach (var hardcodedText in CollectHardcodedXamlText(appRoot).OrderBy(value => value, StringComparer.Ordinal))
{
    failures.Add($"Hardcoded XAML text should use localization: {hardcodedText}");
}

foreach (var language in languages)
{
    try
    {
        var strings = LoadLanguage(root, language);
        if (language == "en-US")
        {
            continue;
        }

        var keys = strings.Keys.ToHashSet(StringComparer.Ordinal);
        var missing = englishKeys
            .Where(key => !keys.Contains(key))
            .OrderBy(key => key, StringComparer.Ordinal)
            .ToArray();
        if (missing.Length > 0)
        {
            failures.Add($"{language} is missing {missing.Length} key(s): {JoinSample(missing)}");
        }

        var empty = englishKeys
            .Where(key => strings.TryGetValue(key, out var value) && string.IsNullOrWhiteSpace(value))
            .OrderBy(key => key, StringComparer.Ordinal)
            .ToArray();
        if (empty.Length > 0)
        {
            failures.Add($"{language} has {empty.Length} empty value(s): {JoinSample(empty)}");
        }

        var placeholderMismatches = englishKeys
            .Where(key => strings.ContainsKey(key) && !PlaceholdersMatch(englishStrings[key], strings[key]))
            .OrderBy(key => key, StringComparer.Ordinal)
            .ToArray();
        if (placeholderMismatches.Length > 0)
        {
            failures.Add($"{language} has {placeholderMismatches.Length} placeholder mismatch(es): {JoinSample(placeholderMismatches)}");
        }

        var untranslated = englishKeys
            .Where(key =>
                strings.TryGetValue(key, out var value)
                && string.Equals(value, englishStrings[key], StringComparison.Ordinal)
                && ShouldRequireNonEnglishValue(key, englishStrings[key]))
            .OrderBy(key => key, StringComparer.Ordinal)
            .ToArray();
        if (untranslated.Length > 0)
        {
            failures.Add($"{language} has {untranslated.Length} likely untranslated value(s): {JoinSample(untranslated)}");
        }
    }
    catch (Exception ex)
    {
        failures.Add($"{language} failed to load: {ex.Message}");
    }
}

if (failures.Count > 0)
{
    foreach (var failure in failures)
    {
        Console.Error.WriteLine(failure);
    }

    return 1;
}

Console.WriteLine("Localization audit passed.");
return 0;

static Dictionary<string, string> LoadLanguage(string root, string cultureName)
{
    var values = new Dictionary<string, string>(StringComparer.Ordinal);
    foreach (var suffix in new[] { ".json", ".extra.json" })
    {
        var path = Path.Combine(root, $"{cultureName}{suffix}");
        if (!File.Exists(path))
        {
            throw new FileNotFoundException("Missing localization file.", path);
        }

        var json = File.ReadAllText(path);
        var nextValues = JsonSerializer.Deserialize<Dictionary<string, string>>(json)
            ?? throw new InvalidOperationException($"Localization file is empty: {Path.GetFileName(path)}");

        foreach (var pair in nextValues)
        {
            values[pair.Key] = pair.Value;
        }
    }

    return values;
}

static IEnumerable<string> CollectSourceLocalizationKeys(string appRoot)
{
    var keys = new HashSet<string>(StringComparer.Ordinal);
    if (!Directory.Exists(appRoot))
    {
        return keys;
    }

    foreach (var path in Directory.EnumerateFiles(appRoot, "*.*", SearchOption.AllDirectories)
        .Where(path => path.EndsWith(".xaml", StringComparison.OrdinalIgnoreCase)
            || path.EndsWith(".cs", StringComparison.OrdinalIgnoreCase)))
    {
        if (path.Contains($"{Path.DirectorySeparatorChar}bin{Path.DirectorySeparatorChar}", StringComparison.OrdinalIgnoreCase)
            || path.Contains($"{Path.DirectorySeparatorChar}obj{Path.DirectorySeparatorChar}", StringComparison.OrdinalIgnoreCase))
        {
            continue;
        }

        var text = File.ReadAllText(path);
        if (path.EndsWith(".xaml", StringComparison.OrdinalIgnoreCase))
        {
            AddMatches(keys, text, @"\{loc:Translate\s+'([^']+)'\}");
            AddMatches(keys, text, @"<loc:TranslateExtension\s+Text=""([^""]+)""");
            AddMatches(keys, text, @"\b(?:CommandParameter|Tag)=""([^""{}]+[A-Za-z][^""{}]*)""");
        }
        else
        {
            AddMatches(keys, text, @"(?:LocalizationService\.Translate|LocalizationService\.Format|T|TF)\(\s*""((?:\\.|[^""\\])*)""");
        }
    }

    return keys.Where(IsLikelyUserFacingText);
}

static IEnumerable<string> CollectHardcodedXamlText(string appRoot)
{
    var results = new List<string>();
    if (!Directory.Exists(appRoot))
    {
        return results;
    }

    const string attributePattern = @"\b(Text|Content|Header|ToolTip|Title|Watermark)=""([^""{}]*[A-Za-z][^""{}]*)""";

    foreach (var path in Directory.EnumerateFiles(appRoot, "*.xaml", SearchOption.AllDirectories))
    {
        if (path.Contains($"{Path.DirectorySeparatorChar}bin{Path.DirectorySeparatorChar}", StringComparison.OrdinalIgnoreCase)
            || path.Contains($"{Path.DirectorySeparatorChar}obj{Path.DirectorySeparatorChar}", StringComparison.OrdinalIgnoreCase))
        {
            continue;
        }

        var text = File.ReadAllText(path);
        foreach (Match match in Regex.Matches(text, attributePattern, RegexOptions.CultureInvariant))
        {
            var value = match.Groups[2].Value;
            if (!IsLikelyUserFacingText(value)
                || IsAllowedHardcodedXamlLiteral(value)
                || IsInsideLocalizationExtension(text, match.Index))
            {
                continue;
            }

            var line = text[..match.Index].Count(character => character == '\n') + 1;
            var relativePath = Path.GetRelativePath(appRoot, path);
            results.Add($"{relativePath}:{line}: {match.Groups[1].Value}=\"{value}\"");
        }
    }

    return results;
}

static bool IsInsideLocalizationExtension(string text, int matchIndex)
{
    var start = Math.Max(0, matchIndex - 96);
    var prefix = text[start..matchIndex];
    return prefix.Contains("<loc:TranslateExtension", StringComparison.Ordinal);
}

static bool IsAllowedHardcodedXamlLiteral(string value)
{
    if (value.StartsWith("&#x", StringComparison.Ordinal)
        || value.StartsWith("pack://", StringComparison.OrdinalIgnoreCase)
        || value.StartsWith("http://", StringComparison.OrdinalIgnoreCase)
        || value.StartsWith("https://", StringComparison.OrdinalIgnoreCase))
    {
        return true;
    }

    return IsAllowedEnglishLiteral(value);
}

static void AddMatches(ISet<string> keys, string text, string pattern)
{
    foreach (Match match in Regex.Matches(text, pattern, RegexOptions.CultureInvariant))
    {
        var value = UnescapeSourceString(match.Groups[1].Value);
        if (!string.IsNullOrWhiteSpace(value))
        {
            keys.Add(value);
        }
    }
}

static string UnescapeSourceString(string value) => value
    .Replace("\\\"", "\"", StringComparison.Ordinal)
    .Replace("\\n", "\n", StringComparison.Ordinal)
    .Replace("\\r", "\r", StringComparison.Ordinal)
    .Replace("\\t", "\t", StringComparison.Ordinal);

static bool IsLikelyUserFacingText(string value)
{
    if (string.IsNullOrWhiteSpace(value)
        || value.StartsWith("pack://", StringComparison.OrdinalIgnoreCase)
        || value.StartsWith("http://", StringComparison.OrdinalIgnoreCase)
        || value.StartsWith("https://", StringComparison.OrdinalIgnoreCase)
        || value.Contains("{Binding", StringComparison.Ordinal)
        || value.Contains("{DynamicResource", StringComparison.Ordinal)
        || value.Contains("{StaticResource", StringComparison.Ordinal))
    {
        return false;
    }

    return Regex.IsMatch(value, @"[A-Za-z]", RegexOptions.CultureInvariant);
}

static bool PlaceholdersMatch(string englishValue, string translatedValue)
{
    var englishTokens = ExtractPlaceholders(englishValue);
    var translatedTokens = ExtractPlaceholders(translatedValue);
    return englishTokens.SetEquals(translatedTokens);
}

static HashSet<string> ExtractPlaceholders(string value) =>
    Regex.Matches(value, @"\{[A-Za-z0-9_]+\}", RegexOptions.CultureInvariant)
        .Select(match => match.Value)
        .ToHashSet(StringComparer.Ordinal);

static bool ShouldRequireNonEnglishValue(string key, string englishValue)
{
    if (IsAllowedEnglishLiteral(key) || IsAllowedEnglishLiteral(englishValue))
    {
        return false;
    }

    if (!Regex.IsMatch(englishValue, @"[A-Za-z]", RegexOptions.CultureInvariant))
    {
        return false;
    }

    if (Regex.IsMatch(englishValue, @"^[A-Z0-9_+\-/| .:#{}]+$", RegexOptions.CultureInvariant)
        && englishValue.Length <= 32)
    {
        return false;
    }

    return englishValue.Contains(' ', StringComparison.Ordinal)
        || englishValue.Contains('.', StringComparison.Ordinal)
        || englishValue.Contains(',', StringComparison.Ordinal)
        || englishValue.Contains(':', StringComparison.Ordinal)
        || englishValue.Contains('?', StringComparison.Ordinal)
        || englishValue.Contains('!', StringComparison.Ordinal);
}

static string JoinSample(IReadOnlyList<string> values) =>
    string.Join(", ", values.Take(10)) + (values.Count > 10 ? ", ..." : string.Empty);

static bool IsAllowedEnglishLiteral(string value) => value switch
{
    "Crystal Relay"
    or "Twitch"
    or "VRChat"
    or "VRChat 2FA"
    or "VRChat 2FA | Crystal Relay"
    or "VRChat Login | Crystal Relay"
    or "OSC"
    or "OSC Status"
    or "OSCQuery"
    or "Bits"
    or "Subs"
    or "Bits >= {0}"
    or "Subs >= {0}"
    or "Bits {0}+: {1}"
    or "Subs {0}+: {1}"
    or "Bits >= {0} ({1}s per bit)"
    or "Subs >= {0} ({1}s per sub)"
    or "Bits >= {0} ({1}s per {2} bits)"
    or "Subs >= {0} ({1}s per {2} subs)"
    or "{0} bits"
    or "1 sub"
    or "{0} subs"
    or "Bits + Subs"
    or "Twitch Chatbox"
    or "Twitch Chatbox | Crystal Relay"
    or "Twitch Stream"
    or "Twitch Listener"
    or "Bool"
    or "Int"
    or "Float"
    or "LIVE"
    or "OK"
    or "MainFrame"
    or "Trash Kitty"
    or "Treetender's Arm"
    or "Void Crystal"
    or "Dream Scape"
    or "Bubblegum"
    or "Cosmic Puppy Girl"
    or "Peaches & Cream"
    or "Moon Bunny Wink"
    or "Dread Night Bar"
    or "Baked"
    or "TheRaccoonCat"
    or "Riku Satori Pom"
    or "#RRGGBB"
    or "12-hour"
    or "24-hour"
    or "{0} | Crystal Relay"
    or "Crystal Relay v{0}{1} | Twitch to OSC"
    or "Twitch to OSC | v{0}{1}"
    or "Avatar Roulette Pool | Crystal Relay" => true,
    _ => false
};
