using VrcTwitchOscBridge.Infrastructure;

namespace VrcTwitchOscBridge.Models;

public sealed class CustomThemeSettings : ObservableObject
{
    private bool isInitialized;
    private string windowBackgroundHex = string.Empty;
    private string panelBackgroundHex = string.Empty;
    private string panelSecondaryHex = string.Empty;
    private string borderHex = string.Empty;
    private string accentHex = string.Empty;
    private string textHex = string.Empty;
    private string mutedTextHex = string.Empty;
    private string inputBackgroundHex = string.Empty;
    private string inputBorderHex = string.Empty;
    private string secondaryButtonHex = string.Empty;
    private string titleBarHex = string.Empty;
    private string dangerHex = string.Empty;
    private string bodyFontFamily = "Verdana";
    private string headingFontFamily = "Constantia";
    private string backgroundImageRelativePath = string.Empty;

    public bool IsInitialized
    {
        get => isInitialized;
        set => SetProperty(ref isInitialized, value);
    }

    public string WindowBackgroundHex
    {
        get => windowBackgroundHex;
        set => SetProperty(ref windowBackgroundHex, NormalizeText(value));
    }

    public string PanelBackgroundHex
    {
        get => panelBackgroundHex;
        set => SetProperty(ref panelBackgroundHex, NormalizeText(value));
    }

    public string PanelSecondaryHex
    {
        get => panelSecondaryHex;
        set => SetProperty(ref panelSecondaryHex, NormalizeText(value));
    }

    public string BorderHex
    {
        get => borderHex;
        set => SetProperty(ref borderHex, NormalizeText(value));
    }

    public string AccentHex
    {
        get => accentHex;
        set => SetProperty(ref accentHex, NormalizeText(value));
    }

    public string TextHex
    {
        get => textHex;
        set => SetProperty(ref textHex, NormalizeText(value));
    }

    public string MutedTextHex
    {
        get => mutedTextHex;
        set => SetProperty(ref mutedTextHex, NormalizeText(value));
    }

    public string InputBackgroundHex
    {
        get => inputBackgroundHex;
        set => SetProperty(ref inputBackgroundHex, NormalizeText(value));
    }

    public string InputBorderHex
    {
        get => inputBorderHex;
        set => SetProperty(ref inputBorderHex, NormalizeText(value));
    }

    public string SecondaryButtonHex
    {
        get => secondaryButtonHex;
        set => SetProperty(ref secondaryButtonHex, NormalizeText(value));
    }

    public string TitleBarHex
    {
        get => titleBarHex;
        set => SetProperty(ref titleBarHex, NormalizeText(value));
    }

    public string DangerHex
    {
        get => dangerHex;
        set => SetProperty(ref dangerHex, NormalizeText(value));
    }

    public string BodyFontFamily
    {
        get => bodyFontFamily;
        set => SetProperty(ref bodyFontFamily, NormalizeFont(value, "Verdana"));
    }

    public string HeadingFontFamily
    {
        get => headingFontFamily;
        set => SetProperty(ref headingFontFamily, NormalizeFont(value, "Constantia"));
    }

    public string BackgroundImageRelativePath
    {
        get => backgroundImageRelativePath;
        set => SetProperty(ref backgroundImageRelativePath, NormalizeRelativePath(value));
    }

    private static string NormalizeText(string? value) =>
        string.IsNullOrWhiteSpace(value) ? string.Empty : value.Trim();

    private static string NormalizeFont(string? value, string fallback) =>
        string.IsNullOrWhiteSpace(value) ? fallback : value.Trim();

    private static string NormalizeRelativePath(string? value) =>
        string.IsNullOrWhiteSpace(value)
            ? string.Empty
            : value.Trim().Replace('/', '\\');
}
