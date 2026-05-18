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
    or "Add Action"
    or "Add Universal"
    or "Add Universal Trigger"
    or "Add this action to the trigger queue"
    or "Choose one random action instead of running all actions"
    or "Default Value"
    or "Delete Action"
    or "Delete All Universal"
    or "Delete All Universal Triggers"
    or "Delete Universal"
    or "Delete Universal Trigger"
    or "Delete Universal Triggers"
    or "Delete every universal trigger? This does not delete Avatar Sets, Movement Redeems, Avatar Change, or Bits + Subs overrides."
    or "Duration"
    or "Enabled"
    or "Exact Chat Command"
    or "Fooma Import Complete"
    or "Fooma Import Failed"
    or "Global Delay (seconds)"
    or "Import Fooma Config"
    or "Import Fooma Interaction Config"
    or "Import a Fooma config or add a universal trigger to edit it."
    or "Import a Fooma config or add a universal trigger."
    or "Imported {0} universal trigger(s). Skipped {1} invalid item(s)."
    or "Action Family"
    or "Add Cash Rule"
    or "Add a cash payment rule to get started."
    or "Add or select a cash payment rule to edit it."
    or "Add or select a cash payment rule to edit its provider, amount filters, and action."
    or "Avatar Scaling Action"
    or "Cash Action"
    or "Cash Payment Action"
    or "Cash Payment Connections"
    or "Cash Payment Rule"
    or "Cash Payments"
    or "Cash Payments listen locally for StreamElements tips, Streamlabs donations, and Ko-fi webhook posts. Tokens and webhook verification secrets are stored in Windows Credential Manager, not in the portable profile."
    or "Cash Payments listen for StreamElements tips, Streamlabs donations, and Ko-fi payments. Ko-fi uses the Crystal Relay hosted relay by default, with a local webhook fallback for advanced setups. Tokens, client secrets, and webhook verification secrets are stored in Windows Credential Manager."
    or "Cooldown Seconds"
    or "Delete All Cash Rules"
    or "Delete Cash Payment Rules"
    or "Delete Cash Rule"
    or "Delete every cash payment rule? Provider connection settings and saved credentials are kept."
    or "Enable Ko-fi"
    or "Enable StreamElements"
    or "Enable Streamlabs"
    or "Ko-fi hosted relay URL"
    or "Ko-fi public webhook URL"
    or "Ko-fi webhook URL"
    or "Ko-fi verification token"
    or "Leave maximum amount at 0 for no upper limit. Leave currency blank to accept any currency; v1 does not convert between currencies."
    or "Local port"
    or "Maximum Amount"
    or "Message Contains"
    or "OSC / Avatar Action"
    or "Open Ko-fi Webhooks Page"
    or "Payment Match"
    or "Provider Connections"
    or "Random Maximum"
    or "Random Movement"
    or "Random Minimum"
    or "Regenerate Ko-fi Relay Link"
    or "Relative Height"
    or "Restore Height"
    or "Rule Name"
    or "Rule enabled"
    or "Scale Mode"
    or "Smooth Seconds"
    or "StreamElements JWT token"
    or "StreamElements account / room ID"
    or "Streamlabs access token"
    or "Target Height"
    or "Test Cash Rule"
    or "This cash rule runs an Avatar Scaling action and does not create a Twitch reward."
    or "This cash rule runs the same OSC, Set Trigger, Avatar Change, or Avatar Roulette actions as other redeems, but it is triggered by a cash payment instead of Twitch."
    or "This tab is for cash payment triggers. Connect StreamElements, Streamlabs, or Ko-fi locally, then add rules that fire OSC, avatar-change, roulette, Set Trigger, or avatar-scaling actions."
    or "This tab is for cash payment triggers. Connect StreamElements, Streamlabs, or Ko-fi, then add rules that fire OSC, avatar-change, roulette, Set Trigger, or avatar-scaling actions."
    or "Use Cash Payments for tip and donation triggers from StreamElements, Streamlabs, and Ko-fi. These rules do not create Twitch rewards."
    or "Use Crystal Relay hosted Ko-fi relay"
    or "Paste this webhook URL into Ko-fi. Crystal Relay connects outward to the hosted relay, so streamers do not need Cloudflare, ngrok, port forwarding, or a public local URL."
    or "Use the action editor below for OSC parameters, Set Trigger, Avatar Change, or Avatar Roulette."
    or "Use the scaling controls below for this cash payment rule."
    or "Webhook path"
    or "any currency"
    or "{0} listens for {1} {2} payments from {3}."
    or "Add Movement Set"
    or "Add a movement set to get started."
    or "Delete All Movement Sets"
    or "Delete Movement Set"
    or "Movement Set Name"
    or "Movement Sets"
    or "Movement Sets are only for organization. Every movement redeem in every set still works globally across avatars."
    or "Movement Sets are organization-only folders. Movement redeems inside them still run globally across every avatar and keep their existing Twitch reward links."
    or "Redeems In This Movement Set"
    or "Select a movement set, then add a movement redeem to edit it."
    or "This tab is for organizing global movement redeems like forward, back, left, right, and spin. Movement Sets do not add avatar matching; they only keep the movement library easier to manage."
    or "Use Movement Sets to organize global movement redeems. The sets are folders only; every movement redeem still works across every avatar and keeps its existing Twitch reward link."
    or "Max Months"
    or "Maximum Bits"
    or "Min Months"
    or "Minimum Bits"
    or "OSC Actions"
    or "OSC Address"
    or "Reward ID"
    or "Run all actions in order, or enable random mode above to pick one action per trigger. Queued actions serialize repeated activations for this trigger."
    or "Selected Action"
    or "Sub Tier"
    or "Target Value"
    or "Test Trigger"
    or "This tab is for universal Twitch interaction triggers. Import a Fooma Twitch Interaction config here, then test and adjust the OSC actions without changing the existing Avatar Sets, Movement Redeems, Avatar Change, or Bits + Subs override sections."
    or "Trigger Settings"
    or "Trigger Type"
    or "Universal Trigger Editor"
    or "Universal Triggers"
    or "Universal Triggers run direct OSC action lists from Twitch events. Use this for imported Fooma Twitch Interaction configs or custom assist-style triggers without changing Avatar Sets, Movement Redeems, Avatar Change, or Bits + Subs overrides."
    or "Universal triggers run globally from Twitch events and send direct OSC actions."
    or "Use this list for imported or universal Twitch interactions. These triggers can listen for chat commands, channel point rewards, bits, subscriptions, gift subs, and follows without mixing into avatar sets or paid override rules."
    or "User Delay (seconds)"
    or "Active Time Seconds"
    or "Add Scale Set"
    or "Add Scale Redeem"
    or "Add an avatar scale redeem to edit it."
    or "Add an avatar scale redeem to get started."
    or "Add a scale set to get started."
    or "Avatar Scaling"
    or "Avatar Scaling redeems send VRChat's /avatar/eyeheight OSC value and are not tied to one avatar set."
    or "Avatar Scaling sends VRChat OSC Avatar Scaling values to /avatar/eyeheight. Use it for Twitch rewards, commands, bits, subs, gift subs, and follows that change avatar height without mixing those rules into Avatar Sets."
    or "Bits Range"
    or "Channel Point Reward"
    or "Chat Command"
    or "Command Text"
    or "Configured Restore Height"
    or "Create Twitch-triggered height changes using /avatar/eyeheight. VRChat may still block scaling in some worlds or Udon setups."
    or "Delete All Scale Sets"
    or "Delete All Scale Redeems"
    or "Delete Avatar Scale Sets"
    or "Delete Avatar Scale Redeems"
    or "Delete Scale Set"
    or "Delete Scale Redeem"
    or "Delete every avatar scale set and scale redeem? This only clears the Avatar Scaling section."
    or "Delete every avatar scale redeem? This only clears the Avatar Scaling section."
    or "Disable Pairing lets this scale redeem temporarily turn off other scale redeems in the same scale set while it is active. Use it when two height effects would fight each other or should behave like separate modes instead of stacking together."
    or "Height Multiplier"
    or "Maximum Height"
    or "Maximum Height stops positive relative scaling at this height."
    or "Minimum Height"
    or "Minimum Height stops negative relative scaling at this height."
    or "Mode"
    or "Permission"
    or "Pick a scale redeem in this set to edit it below, test it, or delete it."
    or "Preset"
    or "Random Max Height"
    or "Random Min Height"
    or "Relative Height Change"
    or "Restore Mode"
    or "Scale Action"
    or "Scale Redeems"
    or "Scale Redeems In This Set"
    or "Scale Set Name"
    or "Scale Set Setup"
    or "Scale Trigger Settings"
    or "Select or add a scale set to edit scale redeems."
    or "Select or add a scale set, then add a scale redeem to edit it."
    or "Smooth Transition Seconds"
    or "Add Bits Range"
    or "Bits Growth Ranges"
    or "Height Added"
    or "Inactivity Timer Seconds"
    or "Max Added Height"
    or "Maximum Bits set to 0 means no upper limit for that row."
    or "Normal Height"
    or "Remove"
    or "Subscription Growth"
    or "Supporter Growth"
    or "Supporter Growth listens to subs, gift subs, and bits. Each event adds height, resets the timer, then returns to normal when support stops."
    or "Tier 1 Height Add"
    or "Tier 2 Height Add"
    or "Tier 3 Height Add"
    or "Use 0 for unlimited added height until VRChat or safe range clamps it."
    or "Avatar Scaling Master Reward"
    or "Delete master reward when inactive"
    or "Enable Master Reward"
    or "Free child reward slots while locked"
    or "Master Reward Redeem"
    or "Pick"
    or "The master reward temporarily unlocks Avatar Scaling channel-point rewards so they do not need to stay visible on Twitch all the time. Supporter Growth is not affected because it listens to bits and subs directly."
    or "Unlock Duration Seconds"
    or "When locked, child scale rewards are hidden or deleted until viewers redeem the master reward."
    or "Subscription Filter"
    or "Target Height Meters"
    or "Test Scale"
    or "This tab is for avatar height scale redeems using VRChat OSC Avatar Scaling. Build fixed, random, relative, multiplier, or preset height changes here without mixing them into other redeem systems."
    or "This tab is for avatar height scale redeems using VRChat OSC Avatar Scaling. Use Scale Sets to keep different height reward ideas organized without changing how the triggers run."
    or "Tier"
    or "Unlock advanced VRChat scale range"
    or "Use Scale Sets to organize VRChat OSC avatar height scaling. Scale redeems send /avatar/eyeheight and stay separate from avatar sets, movement, universal triggers, and paid overrides."
    or "Use this list for VRChat OSC avatar height scaling. Scale redeems send /avatar/eyeheight and stay separate from avatar sets, movement, universal triggers, and paid overrides."
    or "Avatar Roulette Pool | Crystal Relay" => true,
    _ => false
};
