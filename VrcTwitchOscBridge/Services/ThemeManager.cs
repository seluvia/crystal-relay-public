using System.IO;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using VrcTwitchOscBridge.Models;

namespace VrcTwitchOscBridge.Services;

internal sealed class ThemePalette
{
    public ThemePalette(
        AppTheme theme,
        string bodyFontFamily,
        string headingFontFamily,
        double homeTitleFontSize,
        IReadOnlyDictionary<string, Color> brushColors,
        string? backgroundImageAbsolutePath = null)
    {
        Theme = theme;
        BodyFontFamily = bodyFontFamily;
        HeadingFontFamily = headingFontFamily;
        HomeTitleFontSize = homeTitleFontSize;
        BrushColors = brushColors;
        BackgroundImageAbsolutePath = backgroundImageAbsolutePath;
    }

    public AppTheme Theme { get; }

    public string BodyFontFamily { get; }

    public string HeadingFontFamily { get; }

    public double HomeTitleFontSize { get; }

    public IReadOnlyDictionary<string, Color> BrushColors { get; }

    public string? BackgroundImageAbsolutePath { get; }

    public Color GetColor(string resourceKey) => BrushColors[resourceKey];
}

internal static class ThemeManager
{
    private static readonly object gate = new();
    private static ThemePalette currentPalette = ThemePaletteFactory.CreatePalette(AppTheme.VoidCrystal, null);

    public static event EventHandler? ThemeChanged;

    public static AppTheme CurrentTheme { get; private set; } = AppTheme.VoidCrystal;

    public static ThemePalette CurrentPalette
    {
        get
        {
            lock (gate)
            {
                return currentPalette;
            }
        }
    }

    public static void UpdateTheme(AppTheme theme, CustomThemeSettings? customTheme)
    {
        lock (gate)
        {
            CurrentTheme = theme;
            currentPalette = ThemePaletteFactory.CreatePalette(theme, customTheme);
        }

        ThemeChanged?.Invoke(null, EventArgs.Empty);
    }

    public static void ApplyToResources(ResourceDictionary resources)
    {
        ApplyToResources(resources, CurrentPalette);
    }

    public static void ApplyToResources(ResourceDictionary resources, AppTheme theme)
    {
        if (theme == CurrentTheme)
        {
            ApplyToResources(resources, CurrentPalette);
            return;
        }

        ApplyToResources(resources, ThemePaletteFactory.CreatePalette(theme, null));
    }

    public static CustomThemeSettings CreateSeededCustomTheme(AppTheme sourceTheme)
    {
        var palette = ThemePaletteFactory.CreatePalette(
            sourceTheme == AppTheme.Custom ? AppTheme.VoidCrystal : sourceTheme,
            null);

        return new CustomThemeSettings
        {
            IsInitialized = true,
            WindowBackgroundHex = ToHex(palette.GetColor("WindowBackgroundBrush")),
            PanelBackgroundHex = ToHex(palette.GetColor("PanelBrush")),
            PanelSecondaryHex = ToHex(palette.GetColor("PanelSecondaryBrush")),
            BorderHex = ToHex(palette.GetColor("BorderBrush")),
            AccentHex = ToHex(palette.GetColor("AccentBrush")),
            TextHex = ToHex(palette.GetColor("TextBrush")),
            MutedTextHex = ToHex(palette.GetColor("MutedBrush")),
            InputBackgroundHex = ToHex(palette.GetColor("InputBrush")),
            InputBorderHex = ToHex(palette.GetColor("InputBorderBrush")),
            SecondaryButtonHex = ToHex(palette.GetColor("SecondaryButtonBrush")),
            TitleBarHex = ToHex(palette.GetColor("TitleBarBrush")),
            DangerHex = ToHex(palette.GetColor("DangerBrush")),
            BodyFontFamily = palette.BodyFontFamily,
            HeadingFontFamily = palette.HeadingFontFamily,
            BackgroundImageRelativePath = string.Empty
        };
    }

    public static FrameworkElement CreateMainWindowBackgroundElement()
    {
        var palette = CurrentPalette;
        if (palette.Theme != AppTheme.Custom
            || string.IsNullOrWhiteSpace(palette.BackgroundImageAbsolutePath)
            || !File.Exists(palette.BackgroundImageAbsolutePath))
        {
            return new Grid();
        }

        try
        {
            var bitmap = new BitmapImage();
            bitmap.BeginInit();
            bitmap.CacheOption = BitmapCacheOption.OnLoad;
            bitmap.UriSource = new Uri(palette.BackgroundImageAbsolutePath, UriKind.Absolute);
            bitmap.EndInit();
            bitmap.Freeze();

            var overlayColor = palette.GetColor("WindowBackgroundBrush");
            overlayColor.A = 176;

            var grid = new Grid();
            grid.Children.Add(new Image
            {
                Source = bitmap,
                Stretch = Stretch.UniformToFill,
                SnapsToDevicePixels = true
            });
            grid.Children.Add(new Border
            {
                Background = new SolidColorBrush(overlayColor)
            });
            return grid;
        }
        catch
        {
            return new Grid();
        }
    }

    public static string ToHex(Color color) =>
        color.A == 255
            ? $"#{color.R:X2}{color.G:X2}{color.B:X2}"
            : $"#{color.A:X2}{color.R:X2}{color.G:X2}{color.B:X2}";

    private static void ApplyToResources(ResourceDictionary resources, ThemePalette palette)
    {
        resources["BodyFontFamily"] = new FontFamily(palette.BodyFontFamily);
        resources["HeadingFontFamily"] = new FontFamily(palette.HeadingFontFamily);
        resources["HomeTitleFontSize"] = palette.HomeTitleFontSize;

        foreach (var pair in palette.BrushColors)
        {
            SetBrushColor(resources, pair.Key, pair.Value);
        }
    }

    private static void SetBrushColor(ResourceDictionary resources, string resourceKey, Color color)
    {
        if (resources[resourceKey] is SolidColorBrush brush && !brush.IsFrozen)
        {
            brush.Color = color;
            return;
        }

        resources[resourceKey] = new SolidColorBrush(color);
    }
}

internal static class ThemeAssetStore
{
    private const string CustomBackgroundFilePrefix = "custom-background";

    public static string ImportBackgroundImage(string sourcePath)
    {
        if (string.IsNullOrWhiteSpace(sourcePath) || !File.Exists(sourcePath))
        {
            throw new FileNotFoundException("The selected image could not be found.", sourcePath);
        }

        Directory.CreateDirectory(AppDataPaths.ThemeAssetsFolder);
        ClearKnownCustomBackgroundFiles();

        var extension = Path.GetExtension(sourcePath);
        if (string.IsNullOrWhiteSpace(extension))
        {
            extension = ".png";
        }

        var destinationFileName = $"{CustomBackgroundFilePrefix}{extension.ToLowerInvariant()}";
        var destinationPath = Path.Combine(AppDataPaths.ThemeAssetsFolder, destinationFileName);
        File.Copy(sourcePath, destinationPath, overwrite: true);
        return Path.Combine("ThemeAssets", destinationFileName);
    }

    public static void ClearBackgroundImage(string? relativePath)
    {
        var resolvedPath = ResolveBackgroundImagePath(relativePath);
        if (!string.IsNullOrWhiteSpace(resolvedPath) && File.Exists(resolvedPath))
        {
            File.Delete(resolvedPath);
        }

        ClearKnownCustomBackgroundFiles();
    }

    public static string? ResolveBackgroundImagePath(string? relativePath)
    {
        if (string.IsNullOrWhiteSpace(relativePath))
        {
            return null;
        }

        try
        {
            var candidatePath = Path.GetFullPath(Path.Combine(AppDataPaths.PortableSaveFolder, relativePath));
            var themeAssetsRoot = Path.GetFullPath(AppDataPaths.ThemeAssetsFolder);
            if (!candidatePath.StartsWith(themeAssetsRoot, StringComparison.OrdinalIgnoreCase))
            {
                return null;
            }

            return candidatePath;
        }
        catch
        {
            return null;
        }
    }

    private static void ClearKnownCustomBackgroundFiles()
    {
        if (!Directory.Exists(AppDataPaths.ThemeAssetsFolder))
        {
            return;
        }

        foreach (var existingFile in Directory.GetFiles(AppDataPaths.ThemeAssetsFolder, $"{CustomBackgroundFilePrefix}.*"))
        {
            try
            {
                File.Delete(existingFile);
            }
            catch
            {
            }
        }
    }
}

internal static class ThemePaletteFactory
{
    public static ThemePalette CreatePalette(AppTheme theme, CustomThemeSettings? customTheme)
    {
        if (theme == AppTheme.Custom)
        {
            return CreateCustomPalette(customTheme);
        }

        return theme switch
        {
            AppTheme.TreetendersArm => CreateBuiltInPalette(
                AppTheme.TreetendersArm,
                "Trebuchet MS",
                "Georgia",
                24d,
                ("WindowBackgroundBrush", "#07140B"),
                ("PanelBrush", "#D00E2415"),
                ("PanelSecondaryBrush", "#CC15361F"),
                ("PanelHighlightBrush", "#D01B4528"),
                ("BorderBrush", "#7B5F39"),
                ("AccentBrush", "#37B34A"),
                ("TextBrush", "#F3F8E8"),
                ("MutedBrush", "#B8CDAA"),
                ("InputBrush", "#BF102B19"),
                ("InputBorderBrush", "#5FA9F2"),
                ("ComboSurfaceBrush", "#F4F8EA"),
                ("ComboTextBrush", "#122015"),
                ("ComboHighlightBrush", "#CFE8B8"),
                ("SecondaryButtonBrush", "#26391F"),
                ("SecondaryButtonBorderBrush", "#7B5F39"),
                ("SectionActiveBrush", "#37B34A"),
                ("RuleCardBrush", "#C615321D"),
                ("RuleCardSelectedBrush", "#244B2B"),
                ("RuleCardHoverBrush", "#63C76E"),
                ("StatusChipBrush", "#9D352F1C"),
                ("NestedPanelBrush", "#BD14311C"),
                ("DangerBrush", "#5C2D24"),
                ("DangerBorderBrush", "#A4614D"),
                ("WarnBrush", "#4a3a1a"),
                ("WarnBorderBrush", "#a08a3a"),
                ("WarnTextBrush", "#f0d878"),
                ("HighlightBorderBrush", "#5FA9F2"),
                ("PopupBorderBrush", "#7B5F39"),
                ("ComboDropButtonBrush", "#DDEECF"),
                ("ComboDropButtonHoverBrush", "#CDE8BC"),
                ("ComboDropButtonPressedBrush", "#B9DEAA"),
                ("ScrollTrackBrush", "#102215"),
                ("ScrollThumbBrush", "#4F8F44"),
                ("ScrollThumbHoverBrush", "#63AD58"),
                ("ScrollThumbPressedBrush", "#7DCA70"),
                ("TitleBarBrush", "#0D1C11"),
                ("TitleBarTextBrush", "#F6FAED"),
                ("TitleBarSubTextBrush", "#C7D7B8"),
                ("TitleBarButtonBrush", "#00000000"),
                ("TitleBarButtonHoverBrush", "#1C3A23"),
                ("TitleBarButtonPressedBrush", "#2A5130"),
                ("TitleBarCloseHoverBrush", "#6B3325"),
                ("TitleBarClosePressedBrush", "#4D241A")),
            AppTheme.Baked => CreateBuiltInPalette(
                AppTheme.Baked,
                "Cambria",
                "Georgia",
                24d,
                ("WindowBackgroundBrush", "#1A120D"),
                ("PanelBrush", "#D2261A13"),
                ("PanelSecondaryBrush", "#CC2D1F17"),
                ("PanelHighlightBrush", "#D3332319"),
                ("BorderBrush", "#B89267"),
                ("AccentBrush", "#A2472A"),
                ("TextBrush", "#F3E7D7"),
                ("MutedBrush", "#C9B39A"),
                ("InputBrush", "#B433251B"),
                ("InputBorderBrush", "#A97C56"),
                ("ComboSurfaceBrush", "#EFE1D0"),
                ("ComboTextBrush", "#2C1B12"),
                ("ComboHighlightBrush", "#D7BA9B"),
                ("SecondaryButtonBrush", "#3A281C"),
                ("SecondaryButtonBorderBrush", "#A47A56"),
                ("SectionActiveBrush", "#A2472A"),
                ("RuleCardBrush", "#BC2E2017"),
                ("RuleCardSelectedBrush", "#4A3122"),
                ("RuleCardHoverBrush", "#B97B58"),
                ("StatusChipBrush", "#A939251A"),
                ("NestedPanelBrush", "#B22F2016"),
                ("DangerBrush", "#5A2318"),
                ("DangerBorderBrush", "#B16652"),
                ("WarnBrush", "#5a4a2a"),
                ("WarnBorderBrush", "#a89060"),
                ("WarnTextBrush", "#f0d090"),
                ("HighlightBorderBrush", "#D3A173"),
                ("PopupBorderBrush", "#B89267"),
                ("ComboDropButtonBrush", "#E3D0BC"),
                ("ComboDropButtonHoverBrush", "#D9C0A5"),
                ("ComboDropButtonPressedBrush", "#CEB08F"),
                ("ScrollTrackBrush", "#271A13"),
                ("ScrollThumbBrush", "#9B6545"),
                ("ScrollThumbHoverBrush", "#B57957"),
                ("ScrollThumbPressedBrush", "#CE8F68"),
                ("TitleBarBrush", "#221710"),
                ("TitleBarTextBrush", "#F6EADB"),
                ("TitleBarSubTextBrush", "#D6C0A9"),
                ("TitleBarButtonBrush", "#00000000"),
                ("TitleBarButtonHoverBrush", "#41291D"),
                ("TitleBarButtonPressedBrush", "#5A3828"),
                ("TitleBarCloseHoverBrush", "#8D3B29"),
                ("TitleBarClosePressedBrush", "#6F2D1F")),
            AppTheme.DreadNightBar => CreateBuiltInPalette(
                AppTheme.DreadNightBar,
                "Cambria",
                "Book Antiqua",
                24d,
                ("WindowBackgroundBrush", "#090708"),
                ("PanelBrush", "#D2141318"),
                ("PanelSecondaryBrush", "#CC18161B"),
                ("PanelHighlightBrush", "#D01E181E"),
                ("BorderBrush", "#8B2F3E"),
                ("AccentBrush", "#C54757"),
                ("TextBrush", "#F2ECEC"),
                ("MutedBrush", "#BDAFB2"),
                ("InputBrush", "#B41A171C"),
                ("InputBorderBrush", "#7C3B45"),
                ("ComboSurfaceBrush", "#F1ECEE"),
                ("ComboTextBrush", "#20181B"),
                ("ComboHighlightBrush", "#E6C4CA"),
                ("SecondaryButtonBrush", "#2A2024"),
                ("SecondaryButtonBorderBrush", "#7A3C46"),
                ("SectionActiveBrush", "#C54757"),
                ("RuleCardBrush", "#B31A171D"),
                ("RuleCardSelectedBrush", "#402328"),
                ("RuleCardHoverBrush", "#9A4A58"),
                ("StatusChipBrush", "#9D291E24"),
                ("NestedPanelBrush", "#B219171C"),
                ("DangerBrush", "#4D151C"),
                ("DangerBorderBrush", "#A24A57"),
                ("WarnBrush", "#4a3a1f"),
                ("WarnBorderBrush", "#a08a55"),
                ("WarnTextBrush", "#f0d090"),
                ("HighlightBorderBrush", "#C75C69"),
                ("PopupBorderBrush", "#8B2F3E"),
                ("ComboDropButtonBrush", "#E7DADF"),
                ("ComboDropButtonHoverBrush", "#DCC9D0"),
                ("ComboDropButtonPressedBrush", "#D0B8C1"),
                ("ScrollTrackBrush", "#221A1D"),
                ("ScrollThumbBrush", "#8C3947"),
                ("ScrollThumbHoverBrush", "#A84A59"),
                ("ScrollThumbPressedBrush", "#C45E6E"),
                ("TitleBarBrush", "#1C1418"),
                ("TitleBarTextBrush", "#F6EFF0"),
                ("TitleBarSubTextBrush", "#CDBFC2"),
                ("TitleBarButtonBrush", "#00000000"),
                ("TitleBarButtonHoverBrush", "#3B2328"),
                ("TitleBarButtonPressedBrush", "#5A3138"),
                ("TitleBarCloseHoverBrush", "#A23543"),
                ("TitleBarClosePressedBrush", "#7D2430")),
            AppTheme.MoonBunnyWink => CreateBuiltInPalette(
                AppTheme.MoonBunnyWink,
                "Verdana",
                "Segoe Print",
                23d,
                ("WindowBackgroundBrush", "#12091E"),
                ("PanelBrush", "#D31C1230"),
                ("PanelSecondaryBrush", "#CC24163A"),
                ("PanelHighlightBrush", "#D226173F"),
                ("BorderBrush", "#AA99D8"),
                ("AccentBrush", "#F2D24F"),
                ("TextBrush", "#FFFDFB"),
                ("MutedBrush", "#D8CFE7"),
                ("InputBrush", "#BE25183C"),
                ("InputBorderBrush", "#B4A5E0"),
                ("ComboSurfaceBrush", "#FFFDF8"),
                ("ComboTextBrush", "#241C30"),
                ("ComboHighlightBrush", "#FBE992"),
                ("SecondaryButtonBrush", "#33204D"),
                ("SecondaryButtonBorderBrush", "#8D7AC6"),
                ("SectionActiveBrush", "#F2D24F"),
                ("RuleCardBrush", "#C225163A"),
                ("RuleCardSelectedBrush", "#40265A"),
                ("RuleCardHoverBrush", "#C2A9FF"),
                ("StatusChipBrush", "#A72B1C43"),
                ("NestedPanelBrush", "#C2241638"),
                ("DangerBrush", "#5A2440"),
                ("DangerBorderBrush", "#C67AA2"),
                ("WarnBrush", "#5a4a3a"),
                ("WarnBorderBrush", "#c0a070"),
                ("WarnTextBrush", "#604020"),
                ("HighlightBorderBrush", "#F2D24F"),
                ("PopupBorderBrush", "#AA99D8"),
                ("ComboDropButtonBrush", "#F6E9FF"),
                ("ComboDropButtonHoverBrush", "#ECD8FF"),
                ("ComboDropButtonPressedBrush", "#DFC8F5"),
                ("ScrollTrackBrush", "#241633"),
                ("ScrollThumbBrush", "#F0D34F"),
                ("ScrollThumbHoverBrush", "#F6E07B"),
                ("ScrollThumbPressedBrush", "#FFEA9D"),
                ("TitleBarBrush", "#26153A"),
                ("TitleBarTextBrush", "#FFFDFB"),
                ("TitleBarSubTextBrush", "#ECE4F9"),
                ("TitleBarButtonBrush", "#00000000"),
                ("TitleBarButtonHoverBrush", "#3E2758"),
                ("TitleBarButtonPressedBrush", "#58377A"),
                ("TitleBarCloseHoverBrush", "#B85B7D"),
                ("TitleBarClosePressedBrush", "#954766")),
            AppTheme.PeachesAndCream => CreateBuiltInPalette(
                AppTheme.PeachesAndCream,
                "Calibri",
                "Georgia",
                24d,
                ("WindowBackgroundBrush", "#FFF9EE"),
                ("PanelBrush", "#DDFBF7ED"),
                ("PanelSecondaryBrush", "#D8FFF5E7"),
                ("PanelHighlightBrush", "#DDFDEFD8"),
                ("BorderBrush", "#4DB6BE"),
                ("AccentBrush", "#F5826F"),
                ("TextBrush", "#2C2A2B"),
                ("MutedBrush", "#635B58"),
                ("InputBrush", "#D7FFF8F1"),
                ("InputBorderBrush", "#5BB8BF"),
                ("ComboSurfaceBrush", "#FFFDF6"),
                ("ComboTextBrush", "#262323"),
                ("ComboHighlightBrush", "#FFE0D6"),
                ("SecondaryButtonBrush", "#4DB6BE"),
                ("SecondaryButtonBorderBrush", "#379AA2"),
                ("SectionActiveBrush", "#F5826F"),
                ("RuleCardBrush", "#DDEFE8DB"),
                ("RuleCardSelectedBrush", "#FFF2E8"),
                ("RuleCardHoverBrush", "#F6A592"),
                ("StatusChipBrush", "#E051B2B9"),
                ("NestedPanelBrush", "#D8FFF6EC"),
                ("DangerBrush", "#E49786"),
                ("DangerBorderBrush", "#CE7260"),
                ("WarnBrush", "#5a4a2a"),
                ("WarnBorderBrush", "#a89060"),
                ("WarnTextBrush", "#806030"),
                ("HighlightBorderBrush", "#F2A08E"),
                ("PopupBorderBrush", "#4DB6BE"),
                ("ComboDropButtonBrush", "#FFE7DE"),
                ("ComboDropButtonHoverBrush", "#FFD8CC"),
                ("ComboDropButtonPressedBrush", "#FFC8BA"),
                ("ScrollTrackBrush", "#EEE6D8"),
                ("ScrollThumbBrush", "#53B5BD"),
                ("ScrollThumbHoverBrush", "#65C0C8"),
                ("ScrollThumbPressedBrush", "#79CDD4"),
                ("TitleBarBrush", "#F5826F"),
                ("TitleBarTextBrush", "#FFFDF8"),
                ("TitleBarSubTextBrush", "#FFF0EA"),
                ("TitleBarButtonBrush", "#00000000"),
                ("TitleBarButtonHoverBrush", "#E67463"),
                ("TitleBarButtonPressedBrush", "#D66658"),
                ("TitleBarCloseHoverBrush", "#CB5A56"),
                ("TitleBarClosePressedBrush", "#B84D4A")),
            AppTheme.CosmicPuppyGirl => CreateBuiltInPalette(
                AppTheme.CosmicPuppyGirl,
                "Trebuchet MS",
                "Book Antiqua",
                23d,
                ("WindowBackgroundBrush", "#120816"),
                ("PanelBrush", "#CE1F132A"),
                ("PanelSecondaryBrush", "#C7231433"),
                ("PanelHighlightBrush", "#CF2A173A"),
                ("BorderBrush", "#6B3A89"),
                ("AccentBrush", "#93D944"),
                ("TextBrush", "#F7F1FF"),
                ("MutedBrush", "#D4C6E6"),
                ("InputBrush", "#BA26183A"),
                ("InputBorderBrush", "#7A4BA2"),
                ("ComboSurfaceBrush", "#F8F4FF"),
                ("ComboTextBrush", "#1A1124"),
                ("ComboHighlightBrush", "#D4F0AE"),
                ("SecondaryButtonBrush", "#351B4A"),
                ("SecondaryButtonBorderBrush", "#6A4290"),
                ("SectionActiveBrush", "#96D947"),
                ("RuleCardBrush", "#BE2A173C"),
                ("RuleCardSelectedBrush", "#4B244E"),
                ("RuleCardHoverBrush", "#B14C62"),
                ("StatusChipBrush", "#9D2A1C3E"),
                ("NestedPanelBrush", "#BC251736"),
                ("DangerBrush", "#632031"),
                ("DangerBorderBrush", "#C65E79"),
                ("WarnBrush", "#4a3a2a"),
                ("WarnBorderBrush", "#a08a5a"),
                ("WarnTextBrush", "#f0d8a0"),
                ("HighlightBorderBrush", "#8FD33E"),
                ("PopupBorderBrush", "#7A4BA2"),
                ("ComboDropButtonBrush", "#E9D9FF"),
                ("ComboDropButtonHoverBrush", "#DBCBF8"),
                ("ComboDropButtonPressedBrush", "#CCB8EC"),
                ("ScrollTrackBrush", "#261338"),
                ("ScrollThumbBrush", "#9BDB4A"),
                ("ScrollThumbHoverBrush", "#B6EC6F"),
                ("ScrollThumbPressedBrush", "#C8F591"),
                ("TitleBarBrush", "#2A1436"),
                ("TitleBarTextBrush", "#F8F2FF"),
                ("TitleBarSubTextBrush", "#D8CAE8"),
                ("TitleBarButtonBrush", "#00000000"),
                ("TitleBarButtonHoverBrush", "#3E2056"),
                ("TitleBarButtonPressedBrush", "#573176"),
                ("TitleBarCloseHoverBrush", "#B54562"),
                ("TitleBarClosePressedBrush", "#8E334B")),
            AppTheme.Bubblegum => CreateBuiltInPalette(
                AppTheme.Bubblegum,
                "Candara",
                "Ink Free",
                23d,
                ("WindowBackgroundBrush", "#FFF5FD"),
                ("PanelBrush", "#D9FFF7FB"),
                ("PanelSecondaryBrush", "#D2F7FBFF"),
                ("PanelHighlightBrush", "#D9FFF8D9"),
                ("BorderBrush", "#8BCBFF"),
                ("AccentBrush", "#FF94C8"),
                ("TextBrush", "#6A4A7B"),
                ("MutedBrush", "#8A719A"),
                ("InputBrush", "#C8FFF9FF"),
                ("InputBorderBrush", "#99CFFD"),
                ("ComboSurfaceBrush", "#FFFFFCFF"),
                ("ComboTextBrush", "#5B4872"),
                ("ComboHighlightBrush", "#FFF7D3"),
                ("SecondaryButtonBrush", "#B9D8FF"),
                ("SecondaryButtonBorderBrush", "#86B8F4"),
                ("SectionActiveBrush", "#FF9AD0"),
                ("RuleCardBrush", "#D7FFF2F9"),
                ("RuleCardSelectedBrush", "#FFF9E0"),
                ("RuleCardHoverBrush", "#FFC2E6"),
                ("StatusChipBrush", "#D8FFF4D1"),
                ("NestedPanelBrush", "#D5FFF8FC"),
                ("DangerBrush", "#F6A7C3"),
                ("DangerBorderBrush", "#F39ABA"),
                ("WarnBrush", "#5a4a2a"),
                ("WarnBorderBrush", "#a89060"),
                ("WarnTextBrush", "#f0d090"),
                ("HighlightBorderBrush", "#F5D77C"),
                ("PopupBorderBrush", "#A7CFFF"),
                ("ComboDropButtonBrush", "#FFE2F4"),
                ("ComboDropButtonHoverBrush", "#FFD1ED"),
                ("ComboDropButtonPressedBrush", "#FFC0E5"),
                ("ScrollTrackBrush", "#E7F5FF"),
                ("ScrollThumbBrush", "#FFAFD7"),
                ("ScrollThumbHoverBrush", "#FF9ACA"),
                ("ScrollThumbPressedBrush", "#F889BD"),
                ("TitleBarBrush", "#B7D9FF"),
                ("TitleBarTextBrush", "#5A426B"),
                ("TitleBarSubTextBrush", "#715B82"),
                ("TitleBarButtonBrush", "#00000000"),
                ("TitleBarButtonHoverBrush", "#FFD3EA"),
                ("TitleBarButtonPressedBrush", "#FFC0DF"),
                ("TitleBarCloseHoverBrush", "#FF9CBF"),
                ("TitleBarClosePressedBrush", "#F57FA8")),
            AppTheme.DreamScape => CreateBuiltInPalette(
                AppTheme.DreamScape,
                "Leelawadee UI",
                "Segoe Print",
                22d,
                ("WindowBackgroundBrush", "#08132F"),
                ("PanelBrush", "#CC1E1A56"),
                ("PanelSecondaryBrush", "#CB20276A"),
                ("PanelHighlightBrush", "#CC2C2D7E"),
                ("BorderBrush", "#5F8BFF"),
                ("AccentBrush", "#FF58D8"),
                ("TextBrush", "#FFF6FF"),
                ("MutedBrush", "#E8D8FF"),
                ("InputBrush", "#BC222560"),
                ("InputBorderBrush", "#7B92FF"),
                ("ComboSurfaceBrush", "#FFF5FF"),
                ("ComboTextBrush", "#140F2A"),
                ("ComboHighlightBrush", "#E7DBFF"),
                ("SecondaryButtonBrush", "#22347A"),
                ("SecondaryButtonBorderBrush", "#6C8EFF"),
                ("SectionActiveBrush", "#7C62FF"),
                ("RuleCardBrush", "#BE211D5A"),
                ("RuleCardSelectedBrush", "#3E3AA0"),
                ("RuleCardHoverBrush", "#FF73D9"),
                ("StatusChipBrush", "#A82F276D"),
                ("NestedPanelBrush", "#BD232462"),
                ("DangerBrush", "#A92955"),
                ("DangerBorderBrush", "#FFA1CD"),
                ("WarnBrush", "#3a2c4a"),
                ("WarnBorderBrush", "#a080c0"),
                ("WarnTextBrush", "#f0d8ff"),
                ("HighlightBorderBrush", "#FF6ECF"),
                ("PopupBorderBrush", "#7B92FF"),
                ("ComboDropButtonBrush", "#F9D7FF"),
                ("ComboDropButtonHoverBrush", "#F4BFFF"),
                ("ComboDropButtonPressedBrush", "#E7A4FF"),
                ("ScrollTrackBrush", "#1A2459"),
                ("ScrollThumbBrush", "#FF68D8"),
                ("ScrollThumbHoverBrush", "#FF88E2"),
                ("ScrollThumbPressedBrush", "#FFA6ED"),
                ("TitleBarBrush", "#14245E"),
                ("TitleBarTextBrush", "#FFF7FF"),
                ("TitleBarSubTextBrush", "#F0DFFF"),
                ("TitleBarButtonBrush", "#00000000"),
                ("TitleBarButtonHoverBrush", "#3447A8"),
                ("TitleBarButtonPressedBrush", "#5165DD"),
                ("TitleBarCloseHoverBrush", "#D03B75"),
                ("TitleBarClosePressedBrush", "#A82A5A")),
            AppTheme.MainFrame => CreateBuiltInPalette(
                AppTheme.MainFrame,
                "Consolas",
                "Bahnschrift",
                24d,
                ("WindowBackgroundBrush", "#05070D"),
                ("PanelBrush", "#CC111524"),
                ("PanelSecondaryBrush", "#CC151A2B"),
                ("PanelHighlightBrush", "#CC172036"),
                ("BorderBrush", "#5B43A8"),
                ("AccentBrush", "#67F7A6"),
                ("TextBrush", "#F2F7FF"),
                ("MutedBrush", "#A4AFCA"),
                ("InputBrush", "#BF141B2C"),
                ("InputBorderBrush", "#7058C4"),
                ("ComboSurfaceBrush", "#F3F8FF"),
                ("ComboTextBrush", "#09111A"),
                ("ComboHighlightBrush", "#CEF7DA"),
                ("SecondaryButtonBrush", "#1A2139"),
                ("SecondaryButtonBorderBrush", "#5A47A1"),
                ("SectionActiveBrush", "#4E3BA4"),
                ("RuleCardBrush", "#BC151C2D"),
                ("RuleCardSelectedBrush", "#1C2745"),
                ("RuleCardHoverBrush", "#72FFB3"),
                ("StatusChipBrush", "#A01B2440"),
                ("NestedPanelBrush", "#BC161C2E"),
                ("DangerBrush", "#4B1724"),
                ("DangerBorderBrush", "#B95D79"),
                ("WarnBrush", "#4a3a1a"),
                ("WarnBorderBrush", "#a08a3a"),
                ("WarnTextBrush", "#f0d878"),
                ("HighlightBorderBrush", "#72FFB3"),
                ("PopupBorderBrush", "#6954BA"),
                ("ComboDropButtonBrush", "#D9FBE5"),
                ("ComboDropButtonHoverBrush", "#C6F4D6"),
                ("ComboDropButtonPressedBrush", "#AFEBC4"),
                ("ScrollTrackBrush", "#0E1320"),
                ("ScrollThumbBrush", "#5744A4"),
                ("ScrollThumbHoverBrush", "#6C56C7"),
                ("ScrollThumbPressedBrush", "#8570E3"),
                ("TitleBarBrush", "#0F141F"),
                ("TitleBarTextBrush", "#F2F7FF"),
                ("TitleBarSubTextBrush", "#B4C0D8"),
                ("TitleBarButtonBrush", "#00000000"),
                ("TitleBarButtonHoverBrush", "#1E2940"),
                ("TitleBarButtonPressedBrush", "#29385A"),
                ("TitleBarCloseHoverBrush", "#8F2F57"),
                ("TitleBarClosePressedBrush", "#712243")),
            AppTheme.TrashKitty => CreateBuiltInPalette(
                AppTheme.TrashKitty,
                "Trebuchet MS",
                "Bahnschrift",
                24d,
                ("WindowBackgroundBrush", "#07090C"),
                ("PanelBrush", "#D014171B"),
                ("PanelSecondaryBrush", "#D01F242B"),
                ("PanelHighlightBrush", "#D02C333B"),
                ("BorderBrush", "#62758F"),
                ("AccentBrush", "#9FC8FF"),
                ("TextBrush", "#F1F5F8"),
                ("MutedBrush", "#B7C1CB"),
                ("InputBrush", "#C51C2228"),
                ("InputBorderBrush", "#7C92AF"),
                ("ComboSurfaceBrush", "#EEF4FA"),
                ("ComboTextBrush", "#101419"),
                ("ComboHighlightBrush", "#C9DFFF"),
                ("SecondaryButtonBrush", "#2E363F"),
                ("SecondaryButtonBorderBrush", "#7C92AF"),
                ("SectionActiveBrush", "#5F78A2"),
                ("RuleCardBrush", "#C91B2026"),
                ("RuleCardSelectedBrush", "#2D3846"),
                ("RuleCardHoverBrush", "#9FC8FF"),
                ("StatusChipBrush", "#A62A323A"),
                ("NestedPanelBrush", "#C620262D"),
                ("DangerBrush", "#4A2127"),
                ("DangerBorderBrush", "#B46A7A"),
                ("WarnBrush", "#4a3a1a"),
                ("WarnBorderBrush", "#a08a3a"),
                ("WarnTextBrush", "#f0d878"),
                ("HighlightBorderBrush", "#9FC8FF"),
                ("PopupBorderBrush", "#7C92AF"),
                ("ComboDropButtonBrush", "#DDEBFA"),
                ("ComboDropButtonHoverBrush", "#CCE0F7"),
                ("ComboDropButtonPressedBrush", "#B8D1EF"),
                ("ScrollTrackBrush", "#14191F"),
                ("ScrollThumbBrush", "#596A7F"),
                ("ScrollThumbHoverBrush", "#7189A5"),
                ("ScrollThumbPressedBrush", "#94B8E8"),
                ("TitleBarBrush", "#101317"),
                ("TitleBarTextBrush", "#F1F5F8"),
                ("TitleBarSubTextBrush", "#B9C5D1"),
                ("TitleBarButtonBrush", "#00000000"),
                ("TitleBarButtonHoverBrush", "#2B333C"),
                ("TitleBarButtonPressedBrush", "#374454"),
                ("TitleBarCloseHoverBrush", "#5E7085"),
                ("TitleBarClosePressedBrush", "#435466")),
            AppTheme.Bratwurst => CreateBuiltInPalette(
                AppTheme.Bratwurst,
                "Bahnschrift",
                "Georgia",
                24d,
                ("WindowBackgroundBrush", "#090503"),
                ("PanelBrush", "#D21B0806"),
                ("PanelSecondaryBrush", "#CC25100A"),
                ("PanelHighlightBrush", "#D033170D"),
                ("BorderBrush", "#D6A11D"),
                ("AccentBrush", "#38B348"),
                ("TextBrush", "#FFF3D2"),
                ("MutedBrush", "#D7B77B"),
                ("InputBrush", "#BF240E08"),
                ("InputBorderBrush", "#D1A02A"),
                ("ComboSurfaceBrush", "#FFF0C8"),
                ("ComboTextBrush", "#1A0B06"),
                ("ComboHighlightBrush", "#BDE39A"),
                ("SecondaryButtonBrush", "#32140B"),
                ("SecondaryButtonBorderBrush", "#B9871D"),
                ("SectionActiveBrush", "#D92D1E"),
                ("RuleCardBrush", "#BC240E08"),
                ("RuleCardSelectedBrush", "#4A1A0C"),
                ("RuleCardHoverBrush", "#4AC45A"),
                ("StatusChipBrush", "#A632120A"),
                ("NestedPanelBrush", "#B9261009"),
                ("DangerBrush", "#6E160E"),
                ("DangerBorderBrush", "#E1472D"),
                ("WarnBrush", "#5a4a2a"),
                ("WarnBorderBrush", "#a89060"),
                ("WarnTextBrush", "#806030"),
                ("HighlightBorderBrush", "#E5B427"),
                ("PopupBorderBrush", "#D6A11D"),
                ("ComboDropButtonBrush", "#F7DB8F"),
                ("ComboDropButtonHoverBrush", "#EFD16F"),
                ("ComboDropButtonPressedBrush", "#E1BD4A"),
                ("ScrollTrackBrush", "#190A06"),
                ("ScrollThumbBrush", "#C2911B"),
                ("ScrollThumbHoverBrush", "#D8AD2A"),
                ("ScrollThumbPressedBrush", "#F0CA45"),
                ("TitleBarBrush", "#160806"),
                ("TitleBarTextBrush", "#FFF3D2"),
                ("TitleBarSubTextBrush", "#E0C48A"),
                ("TitleBarButtonBrush", "#00000000"),
                ("TitleBarButtonHoverBrush", "#35120B"),
                ("TitleBarButtonPressedBrush", "#541A10"),
                ("TitleBarCloseHoverBrush", "#B92517"),
                ("TitleBarClosePressedBrush", "#8C1A11")),
            AppTheme.CarrotPatch => CreateBuiltInPalette(
                AppTheme.CarrotPatch,
                "Candara",
                "Georgia",
                24d,
                ("WindowBackgroundBrush", "#F8DED9"),
                ("PanelBrush", "#EDE8CFC6"),
                ("PanelSecondaryBrush", "#EDE4B7B1"),
                ("PanelHighlightBrush", "#F0F7EAE0"),
                ("BorderBrush", "#B47A41"),
                ("AccentBrush", "#D62835"),
                ("TextBrush", "#3E261F"),
                ("MutedBrush", "#6F4F45"),
                ("InputBrush", "#F4FFF2E6"),
                ("InputBorderBrush", "#BD8450"),
                ("ComboSurfaceBrush", "#FFF7EA"),
                ("ComboTextBrush", "#321E18"),
                ("ComboHighlightBrush", "#F3BBB8"),
                ("SecondaryButtonBrush", "#F1D2C7"),
                ("SecondaryButtonBorderBrush", "#B47A41"),
                ("SectionActiveBrush", "#D62835"),
                ("RuleCardBrush", "#EACF8D7C"),
                ("RuleCardSelectedBrush", "#F3B0AC"),
                ("RuleCardHoverBrush", "#EA8F91"),
                ("StatusChipBrush", "#DED49A78"),
                ("NestedPanelBrush", "#E8EBC8BE"),
                ("DangerBrush", "#863B36"),
                ("DangerBorderBrush", "#C9535D"),
                ("WarnBrush", "#4a3a1a"),
                ("WarnBorderBrush", "#a08a3a"),
                ("WarnTextBrush", "#f0d878"),
                ("HighlightBorderBrush", "#E05C6F"),
                ("PopupBorderBrush", "#D6A35D"),
                ("ComboDropButtonBrush", "#F6D5CC"),
                ("ComboDropButtonHoverBrush", "#EFBDB6"),
                ("ComboDropButtonPressedBrush", "#E7A7A1"),
                ("ScrollTrackBrush", "#E4BEB0"),
                ("ScrollThumbBrush", "#8B5D3D"),
                ("ScrollThumbHoverBrush", "#A8744A"),
                ("ScrollThumbPressedBrush", "#C78C5B"),
                ("TitleBarBrush", "#4B3028"),
                ("TitleBarTextBrush", "#FFF4E6"),
                ("TitleBarSubTextBrush", "#F4D6C4"),
                ("TitleBarButtonBrush", "#00000000"),
                ("TitleBarButtonHoverBrush", "#6A4135"),
                ("TitleBarButtonPressedBrush", "#805342"),
                ("TitleBarCloseHoverBrush", "#B82F3F"),
                ("TitleBarClosePressedBrush", "#8E2330")),
            AppTheme.NeonBorb => CreateBuiltInPalette(
                AppTheme.NeonBorb,
                "Bahnschrift",
                "Segoe UI Semibold",
                24d,
                ("WindowBackgroundBrush", "#090814"),
                ("PanelBrush", "#D2141028"),
                ("PanelSecondaryBrush", "#CC1C1638"),
                ("PanelHighlightBrush", "#D0261F4A"),
                ("BorderBrush", "#27F4FF"),
                ("AccentBrush", "#28F5D1"),
                ("TextBrush", "#F6F0FF"),
                ("MutedBrush", "#C7BAF4"),
                ("InputBrush", "#B9161230"),
                ("InputBorderBrush", "#40EFFF"),
                ("ComboSurfaceBrush", "#F5F1FF"),
                ("ComboTextBrush", "#111020"),
                ("ComboHighlightBrush", "#B6FFF4"),
                ("SecondaryButtonBrush", "#211A45"),
                ("SecondaryButtonBorderBrush", "#2FE9FF"),
                ("SectionActiveBrush", "#FF53D5"),
                ("RuleCardBrush", "#BC17142E"),
                ("RuleCardSelectedBrush", "#2E245F"),
                ("RuleCardHoverBrush", "#37F2E6"),
                ("StatusChipBrush", "#A61F1842"),
                ("NestedPanelBrush", "#B7191636"),
                ("DangerBrush", "#551432"),
                ("DangerBorderBrush", "#FF5CCB"),
                ("WarnBrush", "#4a3a1a"),
                ("WarnBorderBrush", "#a08a3a"),
                ("WarnTextBrush", "#f0d878"),
                ("HighlightBorderBrush", "#38FFF4"),
                ("PopupBorderBrush", "#2FE9FF"),
                ("ComboDropButtonBrush", "#D9FFF9"),
                ("ComboDropButtonHoverBrush", "#BEFFF3"),
                ("ComboDropButtonPressedBrush", "#9AF5E7"),
                ("ScrollTrackBrush", "#141027"),
                ("ScrollThumbBrush", "#2FE9FF"),
                ("ScrollThumbHoverBrush", "#66FFF4"),
                ("ScrollThumbPressedBrush", "#FF63D8"),
                ("TitleBarBrush", "#0E0B1D"),
                ("TitleBarTextBrush", "#F7F1FF"),
                ("TitleBarSubTextBrush", "#D6CAFF"),
                ("TitleBarButtonBrush", "#00000000"),
                ("TitleBarButtonHoverBrush", "#241B48"),
                ("TitleBarButtonPressedBrush", "#352665"),
                ("TitleBarCloseHoverBrush", "#9B2D76"),
                ("TitleBarClosePressedBrush", "#741F59")),
            AppTheme.StinkyOnline => CreateBuiltInPalette(
                AppTheme.StinkyOnline,
                "Bahnschrift",
                "Georgia",
                24d,
                ("WindowBackgroundBrush", "#070707"),
                ("PanelBrush", "#D2141010"),
                ("PanelSecondaryBrush", "#CC201812"),
                ("PanelHighlightBrush", "#D02C2118"),
                ("BorderBrush", "#22D3EE"),
                ("AccentBrush", "#FF4FB8"),
                ("TextBrush", "#FFF2D5"),
                ("MutedBrush", "#D8C39A"),
                ("InputBrush", "#C0100D0B"),
                ("InputBorderBrush", "#31D8EA"),
                ("ComboSurfaceBrush", "#FFF1D4"),
                ("ComboTextBrush", "#17120C"),
                ("ComboHighlightBrush", "#FFD3EC"),
                ("SecondaryButtonBrush", "#271E17"),
                ("SecondaryButtonBorderBrush", "#29CDE3"),
                ("SectionActiveBrush", "#FF4FB8"),
                ("RuleCardBrush", "#BC1B120E"),
                ("RuleCardSelectedBrush", "#42261B"),
                ("RuleCardHoverBrush", "#39DDEC"),
                ("StatusChipBrush", "#A9251B13"),
                ("NestedPanelBrush", "#B617100D"),
                ("DangerBrush", "#5A1330"),
                ("DangerBorderBrush", "#FF6ABF"),
                ("WarnBrush", "#4a3a1a"),
                ("WarnBorderBrush", "#a08a3a"),
                ("WarnTextBrush", "#f0d878"),
                ("HighlightBorderBrush", "#39DDEC"),
                ("PopupBorderBrush", "#29CDE3"),
                ("ComboDropButtonBrush", "#FFE3AE"),
                ("ComboDropButtonHoverBrush", "#FFD997"),
                ("ComboDropButtonPressedBrush", "#F4C77D"),
                ("ScrollTrackBrush", "#110D0B"),
                ("ScrollThumbBrush", "#22D3EE"),
                ("ScrollThumbHoverBrush", "#4BE7F6"),
                ("ScrollThumbPressedBrush", "#FF63C7"),
                ("TitleBarBrush", "#100C0A"),
                ("TitleBarTextBrush", "#FFF2D5"),
                ("TitleBarSubTextBrush", "#D8C39A"),
                ("TitleBarButtonBrush", "#00000000"),
                ("TitleBarButtonHoverBrush", "#2E1E25"),
                ("TitleBarButtonPressedBrush", "#422735"),
                ("TitleBarCloseHoverBrush", "#BD2D6C"),
                ("TitleBarClosePressedBrush", "#90204F")),
            _ => CreateBuiltInPalette(
                AppTheme.VoidCrystal,
                "Verdana",
                "Constantia",
                24d,
                ("WindowBackgroundBrush", "#130B1E"),
                ("PanelBrush", "#CC1C132B"),
                ("PanelSecondaryBrush", "#C8241739"),
                ("PanelHighlightBrush", "#C72E1A4B"),
                ("BorderBrush", "#5A3D92"),
                ("AccentBrush", "#B16BFF"),
                ("TextBrush", "#F5EEFF"),
                ("MutedBrush", "#BFAFD8"),
                ("InputBrush", "#B8271A3D"),
                ("InputBorderBrush", "#7552BC"),
                ("ComboSurfaceBrush", "#F7F2FF"),
                ("ComboTextBrush", "#140C20"),
                ("ComboHighlightBrush", "#D8C2FF"),
                ("SecondaryButtonBrush", "#2C1C48"),
                ("SecondaryButtonBorderBrush", "#7F5FD0"),
                ("SectionActiveBrush", "#8E54FF"),
                ("RuleCardBrush", "#B8241739"),
                ("RuleCardSelectedBrush", "#351C57"),
                ("RuleCardHoverBrush", "#A978FF"),
                ("StatusChipBrush", "#8E241841"),
                ("NestedPanelBrush", "#B8241739"),
                ("DangerBrush", "#4A1729"),
                ("DangerBorderBrush", "#B45A84"),
                ("WarnBrush", "#4a3a1a"),
                ("WarnBorderBrush", "#a08a3a"),
                ("WarnTextBrush", "#f0d878"),
                ("HighlightBorderBrush", "#A978FF"),
                ("PopupBorderBrush", "#8C6EE8"),
                ("ComboDropButtonBrush", "#E9DBFF"),
                ("ComboDropButtonHoverBrush", "#D9C2FF"),
                ("ComboDropButtonPressedBrush", "#C7A8FF"),
                ("ScrollTrackBrush", "#25183D"),
                ("ScrollThumbBrush", "#7B57D0"),
                ("ScrollThumbHoverBrush", "#966AF0"),
                ("ScrollThumbPressedBrush", "#B388FF"),
                ("TitleBarBrush", "#20122F"),
                ("TitleBarTextBrush", "#F5EEFF"),
                ("TitleBarSubTextBrush", "#CBB9E5"),
                ("TitleBarButtonBrush", "#00000000"),
                ("TitleBarButtonHoverBrush", "#3B235B"),
                ("TitleBarButtonPressedBrush", "#543183"),
                ("TitleBarCloseHoverBrush", "#B43D62"),
                ("TitleBarClosePressedBrush", "#8C2648"))
        };
    }

    private static ThemePalette CreateCustomPalette(CustomThemeSettings? customTheme)
    {
        var fallbackPalette = CreatePalette(AppTheme.VoidCrystal, null);
        if (customTheme is null || !customTheme.IsInitialized)
        {
            return new ThemePalette(
                AppTheme.Custom,
                fallbackPalette.BodyFontFamily,
                fallbackPalette.HeadingFontFamily,
                fallbackPalette.HomeTitleFontSize,
                new Dictionary<string, Color>(fallbackPalette.BrushColors),
                null);
        }

        var windowBackground = ParseColorOrFallback(customTheme.WindowBackgroundHex, fallbackPalette.GetColor("WindowBackgroundBrush"));
        var panelBackground = ParseColorOrFallback(customTheme.PanelBackgroundHex, fallbackPalette.GetColor("PanelBrush"));
        var panelSecondary = ParseColorOrFallback(customTheme.PanelSecondaryHex, fallbackPalette.GetColor("PanelSecondaryBrush"));
        var border = ParseColorOrFallback(customTheme.BorderHex, fallbackPalette.GetColor("BorderBrush"));
        var accent = ParseColorOrFallback(customTheme.AccentHex, fallbackPalette.GetColor("AccentBrush"));
        var text = ParseColorOrFallback(customTheme.TextHex, fallbackPalette.GetColor("TextBrush"));
        var muted = ParseColorOrFallback(customTheme.MutedTextHex, fallbackPalette.GetColor("MutedBrush"));
        var inputBackground = ParseColorOrFallback(customTheme.InputBackgroundHex, fallbackPalette.GetColor("InputBrush"));
        var inputBorder = ParseColorOrFallback(customTheme.InputBorderHex, fallbackPalette.GetColor("InputBorderBrush"));
        var secondaryButton = ParseColorOrFallback(customTheme.SecondaryButtonHex, fallbackPalette.GetColor("SecondaryButtonBrush"));
        var titleBar = ParseColorOrFallback(customTheme.TitleBarHex, fallbackPalette.GetColor("TitleBarBrush"));
        var danger = ParseColorOrFallback(customTheme.DangerHex, fallbackPalette.GetColor("DangerBrush"));
        var comboSurface = CreateContrastSafeComboSurface(inputBackground);
        var comboText = GetReadableTextColor(comboSurface);
        var titleBarText = GetReadableTextColor(titleBar);
        var titleBarSubText = Blend(titleBarText, titleBar, 0.25d);
        var scrollThumb = Blend(accent, border, 0.25d);

        var colors = new Dictionary<string, Color>(StringComparer.Ordinal)
        {
            ["WindowBackgroundBrush"] = windowBackground,
            ["PanelBrush"] = panelBackground,
            ["PanelSecondaryBrush"] = panelSecondary,
            ["PanelHighlightBrush"] = Lighten(panelSecondary, 0.06d),
            ["BorderBrush"] = border,
            ["AccentBrush"] = accent,
            ["TextBrush"] = text,
            ["MutedBrush"] = muted,
            ["InputBrush"] = inputBackground,
            ["InputBorderBrush"] = inputBorder,
            ["ComboSurfaceBrush"] = comboSurface,
            ["ComboTextBrush"] = comboText,
            ["ComboHighlightBrush"] = Blend(comboSurface, accent, 0.20d),
            ["SecondaryButtonBrush"] = secondaryButton,
            ["SecondaryButtonBorderBrush"] = border,
            ["SectionActiveBrush"] = accent,
            ["RuleCardBrush"] = panelSecondary,
            ["RuleCardSelectedBrush"] = Blend(windowBackground, accent, 0.32d),
            ["RuleCardHoverBrush"] = Lighten(accent, 0.10d),
            ["StatusChipBrush"] = Blend(panelBackground, danger, 0.45d),
            ["NestedPanelBrush"] = panelSecondary,
            ["DangerBrush"] = danger,
            ["DangerBorderBrush"] = Lighten(danger, 0.12d),
            ["WarnBrush"] = Color.FromRgb(0x4a, 0x3a, 0x1a),
            ["WarnBorderBrush"] = Color.FromRgb(0xa0, 0x8a, 0x3a),
            ["WarnTextBrush"] = Color.FromRgb(0xf0, 0xd8, 0x78),
            ["HighlightBorderBrush"] = accent,
            ["PopupBorderBrush"] = border,
            ["ComboDropButtonBrush"] = Blend(comboSurface, accent, 0.12d),
            ["ComboDropButtonHoverBrush"] = Blend(comboSurface, accent, 0.20d),
            ["ComboDropButtonPressedBrush"] = Blend(comboSurface, accent, 0.30d),
            ["ScrollTrackBrush"] = Darken(panelBackground, 0.08d),
            ["ScrollThumbBrush"] = scrollThumb,
            ["ScrollThumbHoverBrush"] = Lighten(scrollThumb, 0.08d),
            ["ScrollThumbPressedBrush"] = Lighten(scrollThumb, 0.16d),
            ["TitleBarBrush"] = titleBar,
            ["TitleBarTextBrush"] = titleBarText,
            ["TitleBarSubTextBrush"] = titleBarSubText,
            ["TitleBarButtonBrush"] = Color.FromArgb(0, 0, 0, 0),
            ["TitleBarButtonHoverBrush"] = Blend(titleBar, accent, 0.18d),
            ["TitleBarButtonPressedBrush"] = Blend(titleBar, accent, 0.30d),
            ["TitleBarCloseHoverBrush"] = Blend(titleBar, danger, 0.22d),
            ["TitleBarClosePressedBrush"] = Blend(titleBar, danger, 0.36d)
        };

        return new ThemePalette(
            AppTheme.Custom,
            string.IsNullOrWhiteSpace(customTheme.BodyFontFamily) ? fallbackPalette.BodyFontFamily : customTheme.BodyFontFamily,
            string.IsNullOrWhiteSpace(customTheme.HeadingFontFamily) ? fallbackPalette.HeadingFontFamily : customTheme.HeadingFontFamily,
            fallbackPalette.HomeTitleFontSize,
            colors,
            ThemeAssetStore.ResolveBackgroundImagePath(customTheme.BackgroundImageRelativePath));
    }

    private static ThemePalette CreateBuiltInPalette(
        AppTheme theme,
        string bodyFontFamily,
        string headingFontFamily,
        double homeTitleFontSize,
        params (string Key, string ColorText)[] colors)
    {
        var brushColors = new Dictionary<string, Color>(StringComparer.Ordinal);
        foreach (var (key, colorText) in colors)
        {
            brushColors[key] = ParseColor(colorText);
        }

        return new ThemePalette(theme, bodyFontFamily, headingFontFamily, homeTitleFontSize, brushColors);
    }

    private static Color ParseColor(string colorText) =>
        (Color)ColorConverter.ConvertFromString(colorText);

    private static Color ParseColorOrFallback(string? colorText, Color fallback)
    {
        if (string.IsNullOrWhiteSpace(colorText))
        {
            return fallback;
        }

        try
        {
            return ParseColor(colorText.Trim());
        }
        catch
        {
            return fallback;
        }
    }

    private static Color CreateContrastSafeComboSurface(Color inputBackground)
    {
        var (_, _, lightness) = ToHsl(inputBackground);
        var targetLightness = lightness < 0.75d ? 0.95d : 0.12d;
        return SetLightness(inputBackground, targetLightness);
    }

    private static Color GetReadableTextColor(Color background) =>
        GetRelativeLuminance(background) > 0.5d ? Colors.Black : Colors.White;

    private static Color Lighten(Color color, double amount)
    {
        var (hue, saturation, lightness) = ToHsl(color);
        return FromHsl(hue, saturation, Math.Clamp(lightness + amount, 0d, 1d), color.A);
    }

    private static Color Darken(Color color, double amount)
    {
        var (hue, saturation, lightness) = ToHsl(color);
        return FromHsl(hue, saturation, Math.Clamp(lightness - amount, 0d, 1d), color.A);
    }

    private static Color SetLightness(Color color, double lightness)
    {
        var (hue, saturation, _) = ToHsl(color);
        return FromHsl(hue, saturation, Math.Clamp(lightness, 0d, 1d), color.A);
    }

    private static Color Blend(Color start, Color end, double amount)
    {
        amount = Math.Clamp(amount, 0d, 1d);
        return Color.FromArgb(
            MixChannel(start.A, end.A, amount),
            MixChannel(start.R, end.R, amount),
            MixChannel(start.G, end.G, amount),
            MixChannel(start.B, end.B, amount));
    }

    private static byte MixChannel(byte start, byte end, double amount) =>
        (byte)Math.Round(start + ((end - start) * amount));

    private static (double Hue, double Saturation, double Lightness) ToHsl(Color color)
    {
        var red = color.R / 255d;
        var green = color.G / 255d;
        var blue = color.B / 255d;
        var max = Math.Max(red, Math.Max(green, blue));
        var min = Math.Min(red, Math.Min(green, blue));
        var lightness = (max + min) / 2d;

        if (Math.Abs(max - min) < 0.0001d)
        {
            return (0d, 0d, lightness);
        }

        var delta = max - min;
        var saturation = lightness > 0.5d
            ? delta / (2d - max - min)
            : delta / (max + min);

        double hue;
        if (Math.Abs(max - red) < 0.0001d)
        {
            hue = ((green - blue) / delta) + (green < blue ? 6d : 0d);
        }
        else if (Math.Abs(max - green) < 0.0001d)
        {
            hue = ((blue - red) / delta) + 2d;
        }
        else
        {
            hue = ((red - green) / delta) + 4d;
        }

        hue /= 6d;
        return (hue, saturation, lightness);
    }

    private static Color FromHsl(double hue, double saturation, double lightness, byte alpha)
    {
        if (saturation <= 0.0001d)
        {
            var value = (byte)Math.Round(lightness * 255d);
            return Color.FromArgb(alpha, value, value, value);
        }

        var q = lightness < 0.5d
            ? lightness * (1d + saturation)
            : lightness + saturation - (lightness * saturation);
        var p = (2d * lightness) - q;

        var red = HueToRgb(p, q, hue + (1d / 3d));
        var green = HueToRgb(p, q, hue);
        var blue = HueToRgb(p, q, hue - (1d / 3d));
        return Color.FromArgb(
            alpha,
            (byte)Math.Round(red * 255d),
            (byte)Math.Round(green * 255d),
            (byte)Math.Round(blue * 255d));
    }

    private static double HueToRgb(double p, double q, double hue)
    {
        if (hue < 0d)
        {
            hue += 1d;
        }
        else if (hue > 1d)
        {
            hue -= 1d;
        }

        if (hue < (1d / 6d))
        {
            return p + ((q - p) * 6d * hue);
        }

        if (hue < 0.5d)
        {
            return q;
        }

        if (hue < (2d / 3d))
        {
            return p + ((q - p) * ((2d / 3d) - hue) * 6d);
        }

        return p;
    }

    private static double GetRelativeLuminance(Color color)
    {
        static double ConvertChannel(byte channel)
        {
            var value = channel / 255d;
            return value <= 0.03928d
                ? value / 12.92d
                : Math.Pow((value + 0.055d) / 1.055d, 2.4d);
        }

        var red = ConvertChannel(color.R);
        var green = ConvertChannel(color.G);
        var blue = ConvertChannel(color.B);
        return (0.2126d * red) + (0.7152d * green) + (0.0722d * blue);
    }
}
