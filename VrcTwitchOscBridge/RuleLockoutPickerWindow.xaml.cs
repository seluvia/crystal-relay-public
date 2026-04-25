using System.Collections.ObjectModel;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using VrcTwitchOscBridge.Models;
using VrcTwitchOscBridge.Services;
using VrcTwitchOscBridge.ViewModels;

namespace VrcTwitchOscBridge;

public partial class RuleLockoutPickerWindow : Window
{
    private readonly ObservableCollection<TriggerRuleReferenceOption> availableOptions;
    private readonly ObservableCollection<TriggerRuleReferenceOption> configuredOptions;

    public RuleLockoutPickerWindow(
        AppTheme theme,
        string avatarRuleName,
        string currentRedeemName,
        IReadOnlyList<TriggerRuleReferenceOption> availableOptions,
        IReadOnlyList<TriggerRuleReferenceOption> configuredOptions)
    {
        InitializeComponent();
        ApplyTheme(theme);

        this.availableOptions = new ObservableCollection<TriggerRuleReferenceOption>(
            availableOptions.OrderBy(option => option.Label, StringComparer.OrdinalIgnoreCase));
        this.configuredOptions = new ObservableCollection<TriggerRuleReferenceOption>(
            configuredOptions.OrderBy(option => option.Label, StringComparer.OrdinalIgnoreCase));

        ContextTextBlock.Text = LocalizationService.Format(
            "Avatar rule: {0}{2}Current redeem: {1}",
            avatarRuleName,
            currentRedeemName,
            Environment.NewLine);
        AvailableListBox.ItemsSource = this.availableOptions;
        ConfiguredListBox.ItemsSource = this.configuredOptions;
        UpdateUiState();
    }

    public IReadOnlyList<Guid> SelectedRuleIds => configuredOptions.Select(option => option.RuleId).ToArray();

    private void OnAddClicked(object sender, RoutedEventArgs e)
    {
        if (AvailableListBox.SelectedItem is not TriggerRuleReferenceOption selectedOption)
        {
            return;
        }

        availableOptions.Remove(selectedOption);
        configuredOptions.Add(selectedOption);
        SortCollection(configuredOptions);
        ConfiguredListBox.SelectedItem = selectedOption;
        UpdateUiState();
    }

    private void OnRemoveClicked(object sender, RoutedEventArgs e)
    {
        if (ConfiguredListBox.SelectedItem is not TriggerRuleReferenceOption selectedOption)
        {
            return;
        }

        configuredOptions.Remove(selectedOption);
        availableOptions.Add(selectedOption);
        SortCollection(availableOptions);
        AvailableListBox.SelectedItem = selectedOption;
        UpdateUiState();
    }

    private void OnSelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        UpdateUiState();
    }

    private void OnPrimaryClicked(object sender, RoutedEventArgs e)
    {
        DialogResult = true;
    }

    private void OnCloseButtonClicked(object sender, RoutedEventArgs e)
    {
        DialogResult = false;
    }

    private void OnTitleBarMouseLeftButtonDown(object sender, MouseButtonEventArgs e)
    {
        if (e.ChangedButton != MouseButton.Left)
        {
            return;
        }

        if (FindAncestor<Button>(e.OriginalSource as DependencyObject) is not null)
        {
            return;
        }

        try
        {
            DragMove();
        }
        catch
        {
        }
    }

    private void UpdateUiState()
    {
        AddButton.IsEnabled = AvailableListBox.SelectedItem is TriggerRuleReferenceOption;
        RemoveButton.IsEnabled = ConfiguredListBox.SelectedItem is TriggerRuleReferenceOption;
        AvailableEmptyTextBlock.Visibility = availableOptions.Count == 0 ? Visibility.Visible : Visibility.Collapsed;
        ConfiguredEmptyTextBlock.Visibility = configuredOptions.Count == 0 ? Visibility.Visible : Visibility.Collapsed;
    }

    private static void SortCollection(ObservableCollection<TriggerRuleReferenceOption> collection)
    {
        var sortedItems = collection.OrderBy(option => option.Label, StringComparer.OrdinalIgnoreCase).ToArray();
        collection.Clear();
        foreach (var item in sortedItems)
        {
            collection.Add(item);
        }
    }

    private void ApplyTheme(AppTheme theme)
    {
        if (theme == AppTheme.Baked)
        {
            Resources["BodyFontFamily"] = new FontFamily("Cambria");
            Resources["HeadingFontFamily"] = new FontFamily("Georgia");
            SetBrushColor("WindowBackgroundBrush", "#1A120D");
            SetBrushColor("PanelBrush", "#D2261A13");
            SetBrushColor("PanelSecondaryBrush", "#CC2D1F17");
            SetBrushColor("BorderBrush", "#B89267");
            SetBrushColor("AccentBrush", "#A2472A");
            SetBrushColor("TextBrush", "#F3E7D7");
            SetBrushColor("MutedBrush", "#C9B39A");
            SetBrushColor("InputBrush", "#B433251B");
            SetBrushColor("InputBorderBrush", "#A97C56");
            SetBrushColor("StatusChipBrush", "#A939251A");
            SetBrushColor("SecondaryButtonBrush", "#3A281C");
            SetBrushColor("SecondaryButtonBorderBrush", "#A47A56");
            SetBrushColor("TitleBarBrush", "#221710");
            SetBrushColor("TitleBarTextBrush", "#F6EADB");
            SetBrushColor("TitleBarSubTextBrush", "#D6C0A9");
            SetBrushColor("TitleBarButtonBrush", "#00000000");
            SetBrushColor("TitleBarButtonHoverBrush", "#41291D");
            SetBrushColor("TitleBarButtonPressedBrush", "#5A3828");
            SetBrushColor("TitleBarCloseHoverBrush", "#8D3B29");
            SetBrushColor("TitleBarClosePressedBrush", "#6F2D1F");
            return;
        }

        if (theme == AppTheme.DreadNightBar)
        {
            Resources["BodyFontFamily"] = new FontFamily("Cambria");
            Resources["HeadingFontFamily"] = new FontFamily("Book Antiqua");
            SetBrushColor("WindowBackgroundBrush", "#090708");
            SetBrushColor("PanelBrush", "#D2141318");
            SetBrushColor("PanelSecondaryBrush", "#CC18161B");
            SetBrushColor("BorderBrush", "#8B2F3E");
            SetBrushColor("AccentBrush", "#C54757");
            SetBrushColor("TextBrush", "#F2ECEC");
            SetBrushColor("MutedBrush", "#BDAFB2");
            SetBrushColor("InputBrush", "#B41A171C");
            SetBrushColor("InputBorderBrush", "#7C3B45");
            SetBrushColor("StatusChipBrush", "#9D291E24");
            SetBrushColor("SecondaryButtonBrush", "#2A2024");
            SetBrushColor("SecondaryButtonBorderBrush", "#7A3C46");
            SetBrushColor("TitleBarBrush", "#1C1418");
            SetBrushColor("TitleBarTextBrush", "#F6EFF0");
            SetBrushColor("TitleBarSubTextBrush", "#CDBFC2");
            SetBrushColor("TitleBarButtonBrush", "#00000000");
            SetBrushColor("TitleBarButtonHoverBrush", "#3B2328");
            SetBrushColor("TitleBarButtonPressedBrush", "#5A3138");
            SetBrushColor("TitleBarCloseHoverBrush", "#A23543");
            SetBrushColor("TitleBarClosePressedBrush", "#7D2430");
            return;
        }

        if (theme == AppTheme.MoonBunnyWink)
        {
            Resources["BodyFontFamily"] = new FontFamily("Verdana");
            Resources["HeadingFontFamily"] = new FontFamily("Segoe Print");
            SetBrushColor("WindowBackgroundBrush", "#12091E");
            SetBrushColor("PanelBrush", "#D31C1230");
            SetBrushColor("PanelSecondaryBrush", "#CC24163A");
            SetBrushColor("BorderBrush", "#AA99D8");
            SetBrushColor("AccentBrush", "#F2D24F");
            SetBrushColor("TextBrush", "#FFFDFB");
            SetBrushColor("MutedBrush", "#D8CFE7");
            SetBrushColor("InputBrush", "#BE25183C");
            SetBrushColor("InputBorderBrush", "#B4A5E0");
            SetBrushColor("StatusChipBrush", "#A72B1C43");
            SetBrushColor("SecondaryButtonBrush", "#33204D");
            SetBrushColor("SecondaryButtonBorderBrush", "#8D7AC6");
            SetBrushColor("TitleBarBrush", "#26153A");
            SetBrushColor("TitleBarTextBrush", "#FFFDFB");
            SetBrushColor("TitleBarSubTextBrush", "#ECE4F9");
            SetBrushColor("TitleBarButtonBrush", "#00000000");
            SetBrushColor("TitleBarButtonHoverBrush", "#3E2758");
            SetBrushColor("TitleBarButtonPressedBrush", "#58377A");
            SetBrushColor("TitleBarCloseHoverBrush", "#B85B7D");
            SetBrushColor("TitleBarClosePressedBrush", "#954766");
            return;
        }

        if (theme == AppTheme.PeachesAndCream)
        {
            Resources["BodyFontFamily"] = new FontFamily("Calibri");
            Resources["HeadingFontFamily"] = new FontFamily("Georgia");
            SetBrushColor("WindowBackgroundBrush", "#FFF9EE");
            SetBrushColor("PanelBrush", "#DDFBF7ED");
            SetBrushColor("PanelSecondaryBrush", "#D8FFF5E7");
            SetBrushColor("BorderBrush", "#4DB6BE");
            SetBrushColor("AccentBrush", "#F5826F");
            SetBrushColor("TextBrush", "#2C2A2B");
            SetBrushColor("MutedBrush", "#635B58");
            SetBrushColor("InputBrush", "#D7FFF8F1");
            SetBrushColor("InputBorderBrush", "#5BB8BF");
            SetBrushColor("StatusChipBrush", "#E051B2B9");
            SetBrushColor("SecondaryButtonBrush", "#4DB6BE");
            SetBrushColor("SecondaryButtonBorderBrush", "#379AA2");
            SetBrushColor("TitleBarBrush", "#F5826F");
            SetBrushColor("TitleBarTextBrush", "#FFFDF8");
            SetBrushColor("TitleBarSubTextBrush", "#FFF0EA");
            SetBrushColor("TitleBarButtonBrush", "#00000000");
            SetBrushColor("TitleBarButtonHoverBrush", "#E67463");
            SetBrushColor("TitleBarButtonPressedBrush", "#D66658");
            SetBrushColor("TitleBarCloseHoverBrush", "#CB5A56");
            SetBrushColor("TitleBarClosePressedBrush", "#B84D4A");
            return;
        }

        if (theme == AppTheme.CosmicPuppyGirl)
        {
            Resources["BodyFontFamily"] = new FontFamily("Trebuchet MS");
            Resources["HeadingFontFamily"] = new FontFamily("Book Antiqua");
            SetBrushColor("WindowBackgroundBrush", "#120816");
            SetBrushColor("PanelBrush", "#CE1F132A");
            SetBrushColor("PanelSecondaryBrush", "#C7231433");
            SetBrushColor("BorderBrush", "#6B3A89");
            SetBrushColor("AccentBrush", "#93D944");
            SetBrushColor("TextBrush", "#F7F1FF");
            SetBrushColor("MutedBrush", "#D4C6E6");
            SetBrushColor("InputBrush", "#BA26183A");
            SetBrushColor("InputBorderBrush", "#7A4BA2");
            SetBrushColor("StatusChipBrush", "#9D2A1C3E");
            SetBrushColor("SecondaryButtonBrush", "#351B4A");
            SetBrushColor("SecondaryButtonBorderBrush", "#6A4290");
            SetBrushColor("TitleBarBrush", "#2A1436");
            SetBrushColor("TitleBarTextBrush", "#F8F2FF");
            SetBrushColor("TitleBarSubTextBrush", "#D8CAE8");
            SetBrushColor("TitleBarButtonBrush", "#00000000");
            SetBrushColor("TitleBarButtonHoverBrush", "#3E2056");
            SetBrushColor("TitleBarButtonPressedBrush", "#573176");
            SetBrushColor("TitleBarCloseHoverBrush", "#B54562");
            SetBrushColor("TitleBarClosePressedBrush", "#8E334B");
            return;
        }

        if (theme == AppTheme.Bubblegum)
        {
            Resources["BodyFontFamily"] = new FontFamily("Candara");
            Resources["HeadingFontFamily"] = new FontFamily("Ink Free");
            SetBrushColor("WindowBackgroundBrush", "#FFF5FD");
            SetBrushColor("PanelBrush", "#D9FFF7FB");
            SetBrushColor("PanelSecondaryBrush", "#D2F7FBFF");
            SetBrushColor("BorderBrush", "#8BCBFF");
            SetBrushColor("AccentBrush", "#FF94C8");
            SetBrushColor("TextBrush", "#6A4A7B");
            SetBrushColor("MutedBrush", "#8A719A");
            SetBrushColor("InputBrush", "#C8FFF9FF");
            SetBrushColor("InputBorderBrush", "#99CFFD");
            SetBrushColor("StatusChipBrush", "#D8FFF4D1");
            SetBrushColor("SecondaryButtonBrush", "#B9D8FF");
            SetBrushColor("SecondaryButtonBorderBrush", "#86B8F4");
            SetBrushColor("TitleBarBrush", "#B7D9FF");
            SetBrushColor("TitleBarTextBrush", "#5A426B");
            SetBrushColor("TitleBarSubTextBrush", "#715B82");
            SetBrushColor("TitleBarButtonBrush", "#00000000");
            SetBrushColor("TitleBarButtonHoverBrush", "#FFD3EA");
            SetBrushColor("TitleBarButtonPressedBrush", "#FFC0DF");
            SetBrushColor("TitleBarCloseHoverBrush", "#FF9CBF");
            SetBrushColor("TitleBarClosePressedBrush", "#F57FA8");
            return;
        }

        if (theme == AppTheme.DreamScape)
        {
            Resources["BodyFontFamily"] = new FontFamily("Leelawadee UI");
            Resources["HeadingFontFamily"] = new FontFamily("Segoe Print");
            SetBrushColor("WindowBackgroundBrush", "#08132F");
            SetBrushColor("PanelBrush", "#CC1E1A56");
            SetBrushColor("PanelSecondaryBrush", "#CB20276A");
            SetBrushColor("BorderBrush", "#5F8BFF");
            SetBrushColor("AccentBrush", "#FF58D8");
            SetBrushColor("TextBrush", "#FFF6FF");
            SetBrushColor("MutedBrush", "#E8D8FF");
            SetBrushColor("InputBrush", "#BC222560");
            SetBrushColor("InputBorderBrush", "#7B92FF");
            SetBrushColor("StatusChipBrush", "#A82F276D");
            SetBrushColor("SecondaryButtonBrush", "#22347A");
            SetBrushColor("SecondaryButtonBorderBrush", "#6C8EFF");
            SetBrushColor("TitleBarBrush", "#14245E");
            SetBrushColor("TitleBarTextBrush", "#FFF7FF");
            SetBrushColor("TitleBarSubTextBrush", "#F0DFFF");
            SetBrushColor("TitleBarButtonBrush", "#00000000");
            SetBrushColor("TitleBarButtonHoverBrush", "#3447A8");
            SetBrushColor("TitleBarButtonPressedBrush", "#5165DD");
            SetBrushColor("TitleBarCloseHoverBrush", "#D03B75");
            SetBrushColor("TitleBarClosePressedBrush", "#A82A5A");
            return;
        }

        if (theme == AppTheme.MainFrame)
        {
            Resources["BodyFontFamily"] = new FontFamily("Consolas");
            Resources["HeadingFontFamily"] = new FontFamily("Bahnschrift");
            SetBrushColor("WindowBackgroundBrush", "#05070D");
            SetBrushColor("PanelBrush", "#CC111524");
            SetBrushColor("PanelSecondaryBrush", "#CC151A2B");
            SetBrushColor("BorderBrush", "#5B43A8");
            SetBrushColor("AccentBrush", "#67F7A6");
            SetBrushColor("TextBrush", "#F2F7FF");
            SetBrushColor("MutedBrush", "#A4AFCA");
            SetBrushColor("InputBrush", "#BF141B2C");
            SetBrushColor("InputBorderBrush", "#7058C4");
            SetBrushColor("StatusChipBrush", "#A01B2440");
            SetBrushColor("SecondaryButtonBrush", "#1A2139");
            SetBrushColor("SecondaryButtonBorderBrush", "#5A47A1");
            SetBrushColor("TitleBarBrush", "#0F141F");
            SetBrushColor("TitleBarTextBrush", "#F2F7FF");
            SetBrushColor("TitleBarSubTextBrush", "#B4C0D8");
            SetBrushColor("TitleBarButtonBrush", "#00000000");
            SetBrushColor("TitleBarButtonHoverBrush", "#1E2940");
            SetBrushColor("TitleBarButtonPressedBrush", "#29385A");
            SetBrushColor("TitleBarCloseHoverBrush", "#8F2F57");
            SetBrushColor("TitleBarClosePressedBrush", "#712243");
            return;
        }

        if (theme == AppTheme.TrashKitty)
        {
            Resources["BodyFontFamily"] = new FontFamily("Trebuchet MS");
            Resources["HeadingFontFamily"] = new FontFamily("Bahnschrift");
            SetBrushColor("WindowBackgroundBrush", "#07090C");
            SetBrushColor("PanelBrush", "#D014171B");
            SetBrushColor("PanelSecondaryBrush", "#D01F242B");
            SetBrushColor("BorderBrush", "#62758F");
            SetBrushColor("AccentBrush", "#9FC8FF");
            SetBrushColor("TextBrush", "#F1F5F8");
            SetBrushColor("MutedBrush", "#B7C1CB");
            SetBrushColor("InputBrush", "#C51C2228");
            SetBrushColor("InputBorderBrush", "#7C92AF");
            SetBrushColor("StatusChipBrush", "#A62A323A");
            SetBrushColor("SecondaryButtonBrush", "#2E363F");
            SetBrushColor("SecondaryButtonBorderBrush", "#7C92AF");
            SetBrushColor("TitleBarBrush", "#101317");
            SetBrushColor("TitleBarTextBrush", "#F1F5F8");
            SetBrushColor("TitleBarSubTextBrush", "#B9C5D1");
            SetBrushColor("TitleBarButtonBrush", "#00000000");
            SetBrushColor("TitleBarButtonHoverBrush", "#2B333C");
            SetBrushColor("TitleBarButtonPressedBrush", "#374454");
            SetBrushColor("TitleBarCloseHoverBrush", "#5E7085");
            SetBrushColor("TitleBarClosePressedBrush", "#435466");
            return;
        }

        Resources["BodyFontFamily"] = new FontFamily("Verdana");
        Resources["HeadingFontFamily"] = new FontFamily("Constantia");
        SetBrushColor("WindowBackgroundBrush", "#130B1E");
        SetBrushColor("PanelBrush", "#CC1C132B");
        SetBrushColor("PanelSecondaryBrush", "#C8241739");
        SetBrushColor("BorderBrush", "#4B2B78");
        SetBrushColor("AccentBrush", "#A855F7");
        SetBrushColor("TextBrush", "#F5EEFF");
        SetBrushColor("MutedBrush", "#C9B8E3");
        SetBrushColor("InputBrush", "#B8271A3D");
        SetBrushColor("InputBorderBrush", "#5B3A8E");
        SetBrushColor("StatusChipBrush", "#8E241841");
        SetBrushColor("SecondaryButtonBrush", "#2C1C48");
        SetBrushColor("SecondaryButtonBorderBrush", "#6942A7");
        SetBrushColor("TitleBarBrush", "#20122F");
        SetBrushColor("TitleBarTextBrush", "#F5EEFF");
        SetBrushColor("TitleBarSubTextBrush", "#CBB9E5");
        SetBrushColor("TitleBarButtonBrush", "#00000000");
        SetBrushColor("TitleBarButtonHoverBrush", "#3B235B");
        SetBrushColor("TitleBarButtonPressedBrush", "#543183");
        SetBrushColor("TitleBarCloseHoverBrush", "#B43D62");
        SetBrushColor("TitleBarClosePressedBrush", "#8C2648");
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

    private static T? FindAncestor<T>(DependencyObject? current) where T : DependencyObject
    {
        while (current is not null)
        {
            if (current is T match)
            {
                return match;
            }

            current = VisualTreeHelper.GetParent(current);
        }

        return null;
    }
}
