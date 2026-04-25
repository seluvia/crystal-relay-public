using System.Globalization;
using System.IO;
using System.Reflection;
using System.Text.Json;
using System.Windows.Markup;
using VrcTwitchOscBridge.Models;

namespace VrcTwitchOscBridge.Services;

public static class LocalizationService
{
    private const string ResourceRoot = "VrcTwitchOscBridge.Resources.Localization";
    private static readonly object gate = new();
    private static readonly IReadOnlyDictionary<AppLanguage, string> ResourceNames = new Dictionary<AppLanguage, string>
    {
        [AppLanguage.English] = "en-US.json",
        [AppLanguage.Spanish] = "es-ES.json",
        [AppLanguage.Japanese] = "ja-JP.json",
        [AppLanguage.German] = "de-DE.json",
        [AppLanguage.French] = "fr-FR.json",
        [AppLanguage.PortugueseBrazil] = "pt-BR.json",
        [AppLanguage.Swedish] = "sv-SE.json",
        [AppLanguage.Italian] = "it-IT.json",
        [AppLanguage.ChineseSimplified] = "zh-CN.json",
        [AppLanguage.ChineseTraditional] = "zh-TW.json",
        [AppLanguage.Korean] = "ko-KR.json",
        [AppLanguage.Russian] = "ru-RU.json",
        [AppLanguage.Polish] = "pl-PL.json",
        [AppLanguage.Thai] = "th-TH.json"
    };
    private static readonly IReadOnlyDictionary<AppLanguage, string> CultureNames = new Dictionary<AppLanguage, string>
    {
        [AppLanguage.English] = "en-US",
        [AppLanguage.Spanish] = "es-ES",
        [AppLanguage.Japanese] = "ja-JP",
        [AppLanguage.German] = "de-DE",
        [AppLanguage.French] = "fr-FR",
        [AppLanguage.PortugueseBrazil] = "pt-BR",
        [AppLanguage.Swedish] = "sv-SE",
        [AppLanguage.Italian] = "it-IT",
        [AppLanguage.ChineseSimplified] = "zh-CN",
        [AppLanguage.ChineseTraditional] = "zh-TW",
        [AppLanguage.Korean] = "ko-KR",
        [AppLanguage.Russian] = "ru-RU",
        [AppLanguage.Polish] = "pl-PL",
        [AppLanguage.Thai] = "th-TH"
    };

    private static IReadOnlyDictionary<string, string> englishStrings = new Dictionary<string, string>(StringComparer.Ordinal);
    private static IReadOnlyDictionary<string, string> activeStrings = new Dictionary<string, string>(StringComparer.Ordinal);

    static LocalizationService()
    {
        englishStrings = LoadTranslations(AppLanguage.English);
        activeStrings = englishStrings;
        ActiveLanguage = AppLanguage.English;
        ActiveCulture = CultureInfo.GetCultureInfo(CultureNames[AppLanguage.English]);
    }

    public static AppLanguage ActiveLanguage { get; private set; }

    public static CultureInfo ActiveCulture { get; private set; }

    public static void Initialize(AppLanguage languagePreference)
    {
        lock (gate)
        {
            var resolvedLanguage = ResolveLanguage(languagePreference);
            var resolvedCulture = CultureInfo.GetCultureInfo(CultureNames[resolvedLanguage]);
            var resolvedStrings = resolvedLanguage == AppLanguage.English
                ? englishStrings
                : LoadTranslations(resolvedLanguage);

            ActiveLanguage = resolvedLanguage;
            ActiveCulture = resolvedCulture;
            activeStrings = resolvedStrings;
            CultureInfo.DefaultThreadCurrentCulture = resolvedCulture;
            CultureInfo.DefaultThreadCurrentUICulture = resolvedCulture;
            Thread.CurrentThread.CurrentCulture = resolvedCulture;
            Thread.CurrentThread.CurrentUICulture = resolvedCulture;
        }
    }

    public static string Translate(string sourceText)
    {
        if (string.IsNullOrWhiteSpace(sourceText))
        {
            return string.Empty;
        }

        var currentActiveStrings = activeStrings;
        if (currentActiveStrings.TryGetValue(sourceText, out var translated)
            && !string.IsNullOrWhiteSpace(translated))
        {
            return translated;
        }

        var currentEnglishStrings = englishStrings;
        if (currentEnglishStrings.TryGetValue(sourceText, out var english)
            && !string.IsNullOrWhiteSpace(english))
        {
            return english;
        }

        return sourceText;
    }

    public static string Format(string sourceFormat, params object[] arguments)
    {
        var translatedFormat = Translate(sourceFormat);
        var currentCulture = ActiveCulture;
        return string.Format(currentCulture, translatedFormat, arguments);
    }

    private static AppLanguage ResolveLanguage(AppLanguage preference)
    {
        if (preference != AppLanguage.SystemDefault && ResourceNames.ContainsKey(preference))
        {
            return preference;
        }

        var culture = CultureInfo.CurrentUICulture;
        var cultureName = culture.Name;
        var languageCode = culture.TwoLetterISOLanguageName;

        if (cultureName.StartsWith("zh", StringComparison.OrdinalIgnoreCase))
        {
            return cultureName.Equals("zh-TW", StringComparison.OrdinalIgnoreCase)
                || cultureName.Equals("zh-HK", StringComparison.OrdinalIgnoreCase)
                || cultureName.Equals("zh-MO", StringComparison.OrdinalIgnoreCase)
                ? AppLanguage.ChineseTraditional
                : AppLanguage.ChineseSimplified;
        }

        if (cultureName.StartsWith("pt", StringComparison.OrdinalIgnoreCase))
        {
            return AppLanguage.PortugueseBrazil;
        }

        return languageCode switch
        {
            "es" => AppLanguage.Spanish,
            "ja" => AppLanguage.Japanese,
            "de" => AppLanguage.German,
            "fr" => AppLanguage.French,
            "sv" => AppLanguage.Swedish,
            "it" => AppLanguage.Italian,
            "ko" => AppLanguage.Korean,
            "ru" => AppLanguage.Russian,
            "pl" => AppLanguage.Polish,
            "th" => AppLanguage.Thai,
            _ => AppLanguage.English
        };
    }

    private static IReadOnlyDictionary<string, string> LoadTranslations(AppLanguage language)
    {
        if (!ResourceNames.TryGetValue(language, out var resourceFileName))
        {
            return new Dictionary<string, string>(StringComparer.Ordinal);
        }

        var mergedStrings = new Dictionary<string, string>(StringComparer.Ordinal);
        MergeTranslations(mergedStrings, resourceFileName);
        MergeTranslations(mergedStrings, $"{Path.GetFileNameWithoutExtension(resourceFileName)}.extra.json");
        return mergedStrings;
    }

    private static void MergeTranslations(IDictionary<string, string> target, string resourceFileName)
    {
        var resourceName = $"{ResourceRoot}.{resourceFileName}";
        using var stream = Assembly.GetExecutingAssembly().GetManifestResourceStream(resourceName);
        if (stream is null)
        {
            return;
        }

        using var reader = new StreamReader(stream);
        var json = reader.ReadToEnd();
        var nextStrings = JsonSerializer.Deserialize<Dictionary<string, string>>(json);
        if (nextStrings is null)
        {
            return;
        }

        foreach (var pair in nextStrings)
        {
            target[pair.Key] = pair.Value;
        }
    }
}

[MarkupExtensionReturnType(typeof(string))]
public sealed class TranslateExtension : MarkupExtension
{
    public TranslateExtension()
    {
    }

    public TranslateExtension(string text)
    {
        Text = text;
    }

    public string Text { get; set; } = string.Empty;

    public override object ProvideValue(IServiceProvider serviceProvider) => LocalizationService.Translate(Text);
}
