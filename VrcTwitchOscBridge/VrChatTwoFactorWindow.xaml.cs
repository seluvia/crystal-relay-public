using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using VrcTwitchOscBridge.Models;
using VrcTwitchOscBridge.Services;

namespace VrcTwitchOscBridge;

public partial class VrChatTwoFactorWindow : Window
{
    private readonly AppTheme currentTheme;

    public VrChatTwoFactorWindow(AppTheme theme, IReadOnlyCollection<VrChatTwoFactorMethod> availableMethods)
    {
        InitializeComponent();
        currentTheme = theme;
        ApplyTheme(theme);

        var options = availableMethods.Count == 0
            ? [new VrChatTwoFactorMethodOption(VrChatTwoFactorMethod.Totp, LocalizationService.Translate("Authenticator App"), LocalizationService.Translate("Use the 6-digit code from your authenticator app."))]
            : availableMethods.Select(method => new VrChatTwoFactorMethodOption(
                    method,
                    method switch
                    {
                        VrChatTwoFactorMethod.EmailOtp => LocalizationService.Translate("Email Code"),
                        VrChatTwoFactorMethod.RecoveryCode => LocalizationService.Translate("Recovery Code"),
                        _ => LocalizationService.Translate("Authenticator App")
                    },
                    method switch
                    {
                        VrChatTwoFactorMethod.EmailOtp => LocalizationService.Translate("Use the verification code VRChat emailed to you."),
                        VrChatTwoFactorMethod.RecoveryCode => LocalizationService.Translate("Use one of your VRChat recovery codes."),
                        _ => LocalizationService.Translate("Use the 6-digit code from your authenticator app.")
                    }))
                .ToArray();

        MethodComboBox.ItemsSource = options;
        MethodComboBox.DisplayMemberPath = nameof(VrChatTwoFactorMethodOption.Label);
        MethodComboBox.SelectedIndex = 0;

        Loaded += (_, _) => CodeTextBox.Focus();
        UpdateHint();
    }

    public VrChatTwoFactorMethod SelectedMethod =>
        MethodComboBox.SelectedItem is VrChatTwoFactorMethodOption option
            ? option.Value
            : VrChatTwoFactorMethod.Totp;

    public string VerificationCode => CodeTextBox.Text.Trim();

    private void OnVerifyClicked(object sender, RoutedEventArgs e)
    {
        if (string.IsNullOrWhiteSpace(VerificationCode))
        {
            ThemedDialogWindow.ShowOk(
                this,
                currentTheme,
                LocalizationService.Translate("VRChat 2FA"),
                LocalizationService.Translate("Enter the VRChat verification code before continuing."),
                LocalizationService.Translate("OK"));
            return;
        }

        DialogResult = true;
    }

    private void OnCancelClicked(object sender, RoutedEventArgs e) => DialogResult = false;

    private void OnMethodSelectionChanged(object sender, SelectionChangedEventArgs e) => UpdateHint();

    private void UpdateHint()
    {
        HintTextBlock.Text = MethodComboBox.SelectedItem is VrChatTwoFactorMethodOption option
            ? option.Hint
            : LocalizationService.Translate("Use the VRChat verification code you were given.");
    }

    private void ApplyTheme(AppTheme theme)
    {
        if (theme == AppTheme.Baked)
        {
            Resources["BodyFontFamily"] = new FontFamily("Cambria");
            Resources["HeadingFontFamily"] = new FontFamily("Georgia");
            SetBrushColor("WindowBackgroundBrush", "#1A120D");
            SetBrushColor("PanelBrush", "#D2261A13");
            SetBrushColor("BorderBrush", "#B89267");
            SetBrushColor("AccentBrush", "#A2472A");
            SetBrushColor("TextBrush", "#F3E7D7");
            SetBrushColor("MutedBrush", "#C9B39A");
            SetBrushColor("InputBrush", "#B433251B");
            SetBrushColor("InputBorderBrush", "#A97C56");
            SetBrushColor("ComboSurfaceBrush", "#EFE1D0");
            SetBrushColor("ComboTextBrush", "#2C1B12");
            SetBrushColor("ComboHighlightBrush", "#D7BA9B");
            SetBrushColor("StatusChipBrush", "#A939251A");
            SetBrushColor("SecondaryButtonBrush", "#3A281C");
            SetBrushColor("SecondaryButtonBorderBrush", "#A47A56");
            return;
        }

        if (theme == AppTheme.DreadNightBar)
        {
            Resources["BodyFontFamily"] = new FontFamily("Cambria");
            Resources["HeadingFontFamily"] = new FontFamily("Book Antiqua");
            SetBrushColor("WindowBackgroundBrush", "#090708");
            SetBrushColor("PanelBrush", "#D2141318");
            SetBrushColor("BorderBrush", "#8B2F3E");
            SetBrushColor("AccentBrush", "#C54757");
            SetBrushColor("TextBrush", "#F2ECEC");
            SetBrushColor("MutedBrush", "#BDAFB2");
            SetBrushColor("InputBrush", "#B41A171C");
            SetBrushColor("InputBorderBrush", "#7C3B45");
            SetBrushColor("ComboSurfaceBrush", "#F1ECEE");
            SetBrushColor("ComboTextBrush", "#20181B");
            SetBrushColor("ComboHighlightBrush", "#E6C4CA");
            SetBrushColor("StatusChipBrush", "#9D291E24");
            SetBrushColor("SecondaryButtonBrush", "#2A2024");
            SetBrushColor("SecondaryButtonBorderBrush", "#7A3C46");
            return;
        }

        if (theme == AppTheme.MoonBunnyWink)
        {
            Resources["BodyFontFamily"] = new FontFamily("Verdana");
            Resources["HeadingFontFamily"] = new FontFamily("Segoe Print");
            SetBrushColor("WindowBackgroundBrush", "#12091E");
            SetBrushColor("PanelBrush", "#D31C1230");
            SetBrushColor("BorderBrush", "#AA99D8");
            SetBrushColor("AccentBrush", "#F2D24F");
            SetBrushColor("TextBrush", "#FFFDFB");
            SetBrushColor("MutedBrush", "#D8CFE7");
            SetBrushColor("InputBrush", "#BE25183C");
            SetBrushColor("InputBorderBrush", "#B4A5E0");
            SetBrushColor("ComboSurfaceBrush", "#FFFDF8");
            SetBrushColor("ComboTextBrush", "#241C30");
            SetBrushColor("ComboHighlightBrush", "#FBE992");
            SetBrushColor("StatusChipBrush", "#A72B1C43");
            SetBrushColor("SecondaryButtonBrush", "#33204D");
            SetBrushColor("SecondaryButtonBorderBrush", "#8D7AC6");
            return;
        }

        if (theme == AppTheme.PeachesAndCream)
        {
            Resources["BodyFontFamily"] = new FontFamily("Calibri");
            Resources["HeadingFontFamily"] = new FontFamily("Georgia");
            SetBrushColor("WindowBackgroundBrush", "#FFF9EE");
            SetBrushColor("PanelBrush", "#DDFBF7ED");
            SetBrushColor("BorderBrush", "#4DB6BE");
            SetBrushColor("AccentBrush", "#F5826F");
            SetBrushColor("TextBrush", "#2C2A2B");
            SetBrushColor("MutedBrush", "#635B58");
            SetBrushColor("InputBrush", "#D7FFF8F1");
            SetBrushColor("InputBorderBrush", "#5BB8BF");
            SetBrushColor("ComboSurfaceBrush", "#FFFDF6");
            SetBrushColor("ComboTextBrush", "#262323");
            SetBrushColor("ComboHighlightBrush", "#FFE0D6");
            SetBrushColor("StatusChipBrush", "#E051B2B9");
            SetBrushColor("SecondaryButtonBrush", "#4DB6BE");
            SetBrushColor("SecondaryButtonBorderBrush", "#379AA2");
            return;
        }

        if (theme == AppTheme.CosmicPuppyGirl)
        {
            Resources["BodyFontFamily"] = new FontFamily("Trebuchet MS");
            Resources["HeadingFontFamily"] = new FontFamily("Book Antiqua");
            SetBrushColor("WindowBackgroundBrush", "#120816");
            SetBrushColor("PanelBrush", "#CE1F132A");
            SetBrushColor("BorderBrush", "#6B3A89");
            SetBrushColor("AccentBrush", "#93D944");
            SetBrushColor("TextBrush", "#F7F1FF");
            SetBrushColor("MutedBrush", "#D4C6E6");
            SetBrushColor("InputBrush", "#BA26183A");
            SetBrushColor("InputBorderBrush", "#7A4BA2");
            SetBrushColor("ComboSurfaceBrush", "#F8F4FF");
            SetBrushColor("ComboTextBrush", "#1A1124");
            SetBrushColor("ComboHighlightBrush", "#D4F0AE");
            SetBrushColor("StatusChipBrush", "#9D2A1C3E");
            SetBrushColor("SecondaryButtonBrush", "#351B4A");
            SetBrushColor("SecondaryButtonBorderBrush", "#6A4290");
            return;
        }

        if (theme == AppTheme.Bubblegum)
        {
            Resources["BodyFontFamily"] = new FontFamily("Candara");
            Resources["HeadingFontFamily"] = new FontFamily("Ink Free");
            SetBrushColor("WindowBackgroundBrush", "#FFF5FD");
            SetBrushColor("PanelBrush", "#D9FFF7FB");
            SetBrushColor("BorderBrush", "#8BCBFF");
            SetBrushColor("AccentBrush", "#FF94C8");
            SetBrushColor("TextBrush", "#6A4A7B");
            SetBrushColor("MutedBrush", "#8A719A");
            SetBrushColor("InputBrush", "#C8FFF9FF");
            SetBrushColor("InputBorderBrush", "#99CFFD");
            SetBrushColor("ComboSurfaceBrush", "#FFFFFCFF");
            SetBrushColor("ComboTextBrush", "#5B4872");
            SetBrushColor("ComboHighlightBrush", "#FFF7D3");
            SetBrushColor("StatusChipBrush", "#D8FFF4D1");
            SetBrushColor("SecondaryButtonBrush", "#B9D8FF");
            SetBrushColor("SecondaryButtonBorderBrush", "#86B8F4");
            return;
        }

        if (theme == AppTheme.DreamScape)
        {
            Resources["BodyFontFamily"] = new FontFamily("Leelawadee UI");
            Resources["HeadingFontFamily"] = new FontFamily("Segoe Print");
            SetBrushColor("WindowBackgroundBrush", "#08132F");
            SetBrushColor("PanelBrush", "#CC1E1A56");
            SetBrushColor("BorderBrush", "#5F8BFF");
            SetBrushColor("AccentBrush", "#FF58D8");
            SetBrushColor("TextBrush", "#FFF6FF");
            SetBrushColor("MutedBrush", "#E8D8FF");
            SetBrushColor("InputBrush", "#BC222560");
            SetBrushColor("InputBorderBrush", "#7B92FF");
            SetBrushColor("ComboSurfaceBrush", "#FFF5FF");
            SetBrushColor("ComboTextBrush", "#140F2A");
            SetBrushColor("ComboHighlightBrush", "#E7DBFF");
            SetBrushColor("StatusChipBrush", "#A82F276D");
            SetBrushColor("SecondaryButtonBrush", "#22347A");
            SetBrushColor("SecondaryButtonBorderBrush", "#6C8EFF");
            return;
        }

        if (theme == AppTheme.MainFrame)
        {
            Resources["BodyFontFamily"] = new FontFamily("Consolas");
            Resources["HeadingFontFamily"] = new FontFamily("Bahnschrift");
            SetBrushColor("WindowBackgroundBrush", "#05070D");
            SetBrushColor("PanelBrush", "#CC111524");
            SetBrushColor("BorderBrush", "#5B43A8");
            SetBrushColor("AccentBrush", "#67F7A6");
            SetBrushColor("TextBrush", "#F2F7FF");
            SetBrushColor("MutedBrush", "#A4AFCA");
            SetBrushColor("InputBrush", "#BF141B2C");
            SetBrushColor("InputBorderBrush", "#7058C4");
            SetBrushColor("ComboSurfaceBrush", "#F3F8FF");
            SetBrushColor("ComboTextBrush", "#09111A");
            SetBrushColor("ComboHighlightBrush", "#CEF7DA");
            SetBrushColor("StatusChipBrush", "#A01B2440");
            SetBrushColor("SecondaryButtonBrush", "#1A2139");
            SetBrushColor("SecondaryButtonBorderBrush", "#5A47A1");
            return;
        }

        if (theme == AppTheme.TrashKitty)
        {
            Resources["BodyFontFamily"] = new FontFamily("Trebuchet MS");
            Resources["HeadingFontFamily"] = new FontFamily("Bahnschrift");
            SetBrushColor("WindowBackgroundBrush", "#07090C");
            SetBrushColor("PanelBrush", "#D014171B");
            SetBrushColor("BorderBrush", "#62758F");
            SetBrushColor("AccentBrush", "#9FC8FF");
            SetBrushColor("TextBrush", "#F1F5F8");
            SetBrushColor("MutedBrush", "#B7C1CB");
            SetBrushColor("InputBrush", "#C51C2228");
            SetBrushColor("InputBorderBrush", "#7C92AF");
            SetBrushColor("ComboSurfaceBrush", "#EEF4FA");
            SetBrushColor("ComboTextBrush", "#101419");
            SetBrushColor("ComboHighlightBrush", "#C9DFFF");
            SetBrushColor("StatusChipBrush", "#A62A323A");
            SetBrushColor("SecondaryButtonBrush", "#2E363F");
            SetBrushColor("SecondaryButtonBorderBrush", "#7C92AF");
            return;
        }

        Resources["BodyFontFamily"] = new FontFamily("Verdana");
        Resources["HeadingFontFamily"] = new FontFamily("Constantia");
        SetBrushColor("WindowBackgroundBrush", "#130B1E");
        SetBrushColor("PanelBrush", "#CC1C132B");
        SetBrushColor("BorderBrush", "#4B2B78");
        SetBrushColor("AccentBrush", "#A855F7");
        SetBrushColor("TextBrush", "#F5EEFF");
        SetBrushColor("MutedBrush", "#C9B8E3");
        SetBrushColor("InputBrush", "#B8271A3D");
        SetBrushColor("InputBorderBrush", "#5B3A8E");
        SetBrushColor("ComboSurfaceBrush", "#F7F2FF");
        SetBrushColor("ComboTextBrush", "#140C20");
        SetBrushColor("ComboHighlightBrush", "#D8C2FF");
        SetBrushColor("StatusChipBrush", "#8E241841");
        SetBrushColor("SecondaryButtonBrush", "#2C1C48");
        SetBrushColor("SecondaryButtonBorderBrush", "#6942A7");
    }

    private void SetBrushColor(string resourceKey, string colorText)
    {
        var color = (Color)ColorConverter.ConvertFromString(colorText);
        if (Resources[resourceKey] is SolidColorBrush brush && !brush.IsFrozen)
        {
            brush.Color = color;
            return;
        }

        Resources[resourceKey] = new SolidColorBrush(color);
    }

    private sealed record VrChatTwoFactorMethodOption(VrChatTwoFactorMethod Value, string Label, string Hint);
}
