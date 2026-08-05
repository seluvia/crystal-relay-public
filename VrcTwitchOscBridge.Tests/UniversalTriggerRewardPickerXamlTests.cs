using System;
using System.IO;
using System.Linq;
using System.Xml.Linq;
using Xunit;
using Xunit.Sdk;

namespace VrcTwitchOscBridge.Tests;

public sealed class UniversalTriggerRewardPickerXamlTests
{
    [Fact]
    public void ComboBoxPopup_HostsItemsInsideBoundedVerticalScrollViewer()
    {
        var xaml = File.ReadAllText(FindSourceFile(
            "VrcTwitchOscBridge",
            "UniversalTriggersManagerWindow.xaml"));

        AssertComboBoxPopupAndLinkedRewardPicker(xaml);
    }

    [Fact]
    public void ComboBoxPopup_RejectsSiblingAndUnrelatedScrollViewerFalsePositive()
    {
        const string xaml = """
            <Window>
                <Window.Resources>
                    <Style TargetType="ScrollBar">
                        <Setter Property="Template" Value="{StaticResource VerticalScrollBarTemplate}" />
                    </Style>
                    <Style TargetType="ComboBox">
                        <Setter Property="Template">
                            <Setter.Value>
                                <ControlTemplate TargetType="ComboBox">
                                    <Popup Name="PART_Popup">
                                        <Grid MaxHeight="{TemplateBinding MaxDropDownHeight}">
                                            <Border>
                                                <Grid>
                                                    <ScrollViewer />
                                                    <!-- <ScrollViewer HorizontalScrollBarVisibility="Disabled" VerticalScrollBarVisibility="Auto"> -->
                                                    <ItemsPresenter KeyboardNavigation.DirectionalNavigation="Contained" />
                                                    <ScrollViewer>
                                                        <Border />
                                                    </ScrollViewer>
                                                </Grid>
                                            </Border>
                                        </Grid>
                                    </Popup>
                                </ControlTemplate>
                            </Setter.Value>
                        </Setter>
                    </Style>
                    <Style TargetType="ComboBoxItem"></Style>
                </Window.Resources>
                <Grid>
                    <ComboBox ItemsSource="{Binding AvailableTwitchRewards}" />
                    <ComboBox SelectedValue="{Binding SelectedTrigger.RewardId, Mode=TwoWay, UpdateSourceTrigger=PropertyChanged}" />
                </Grid>
            </Window>
            """;

        var exception = Assert.Throws<XunitException>(() => AssertComboBoxPopupAndLinkedRewardPicker(xaml));
        Assert.Contains("real ScrollViewer ancestor", exception.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void ComboBoxPopup_RejectsScrollHostAndBoundedGridOutsideSelectedPopup()
    {
        const string xaml = """
            <Window>
                <Window.Resources>
                    <Style TargetType="ComboBox">
                        <Setter Property="Template">
                            <Setter.Value>
                                <ControlTemplate TargetType="ComboBox">
                                    <Grid MaxHeight="{TemplateBinding MaxDropDownHeight}">
                                        <ScrollViewer HorizontalScrollBarVisibility="Disabled"
                                                      VerticalScrollBarVisibility="Auto">
                                            <Popup Name="PART_Popup">
                                                <Border>
                                                    <ItemsPresenter KeyboardNavigation.DirectionalNavigation="Contained" />
                                                </Border>
                                            </Popup>
                                        </ScrollViewer>
                                    </Grid>
                                </ControlTemplate>
                            </Setter.Value>
                        </Setter>
                    </Style>
                </Window.Resources>
                <StackPanel>
                    <StackPanel.Style>
                        <Style TargetType="StackPanel">
                            <Style.Triggers>
                                <DataTrigger Binding="{Binding SelectedTrigger.UsesLinkedExistingReward}" Value="True">
                                    <Setter Property="Visibility" Value="Visible" />
                                </DataTrigger>
                            </Style.Triggers>
                        </Style>
                    </StackPanel.Style>
                    <ComboBox ItemsSource="{Binding AvailableTwitchRewards}"
                              SelectedValue="{Binding SelectedTrigger.RewardId, Mode=TwoWay, UpdateSourceTrigger=PropertyChanged}" />
                </StackPanel>
            </Window>
            """;

        var exception = Assert.Throws<XunitException>(() => AssertComboBoxPopupAndLinkedRewardPicker(xaml));
        Assert.Contains("inside PART_Popup", exception.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void LinkedRewardComboBox_RejectsBindingsOnUnrelatedControls()
    {
        const string xaml = """
            <Window>
                <Window.Resources>
                    <Style TargetType="ScrollBar">
                        <Setter Property="Template" Value="{StaticResource VerticalScrollBarTemplate}" />
                    </Style>
                    <Style TargetType="ComboBox">
                        <Setter Property="Template">
                            <Setter.Value>
                                <ControlTemplate TargetType="ComboBox">
                                    <Popup Name="PART_Popup">
                                        <Grid MaxHeight="{TemplateBinding MaxDropDownHeight}">
                                            <ScrollViewer HorizontalScrollBarVisibility="Disabled"
                                                          VerticalScrollBarVisibility="Auto">
                                                <ItemsPresenter KeyboardNavigation.DirectionalNavigation="Contained" />
                                            </ScrollViewer>
                                        </Grid>
                                    </Popup>
                                </ControlTemplate>
                            </Setter.Value>
                        </Setter>
                    </Style>
                </Window.Resources>
                <StackPanel>
                    <StackPanel.Style>
                        <Style TargetType="StackPanel">
                            <Style.Triggers>
                                <DataTrigger Binding="{Binding SelectedTrigger.UsesLinkedExistingReward}" Value="True">
                                    <Setter Property="Visibility" Value="Visible" />
                                </DataTrigger>
                            </Style.Triggers>
                        </Style>
                    </StackPanel.Style>
                    <ComboBox ItemsSource="{Binding AvailableTwitchRewards}"
                              SelectedValue="{Binding SelectedTrigger.WrongRewardId, Mode=TwoWay, UpdateSourceTrigger=PropertyChanged}" />
                </StackPanel>
                <Grid>
                    <ComboBox ItemsSource="{Binding AvailableTwitchRewards}" />
                    <ComboBox SelectedValue="{Binding SelectedTrigger.RewardId, Mode=TwoWay, UpdateSourceTrigger=PropertyChanged}" />
                </Grid>
            </Window>
            """;

        var exception = Assert.ThrowsAny<XunitException>(() => AssertComboBoxPopupAndLinkedRewardPicker(xaml));
        Assert.Contains("SelectedTrigger.RewardId", exception.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void ComboBoxPopup_RejectsMissingKeyboardContainment()
    {
        var xaml = BuildValidFixture(
            itemsPresenterAttributes: string.Empty,
            includeImplicitScrollBarStyle: true);

        var exception = Assert.Throws<XunitException>(() => AssertComboBoxPopupAndLinkedRewardPicker(xaml));
        Assert.Contains("KeyboardNavigation.DirectionalNavigation", exception.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void ComboBoxPopup_RejectsMissingImplicitThemedScrollBarStyle()
    {
        var xaml = BuildValidFixture(
            itemsPresenterAttributes: "KeyboardNavigation.DirectionalNavigation=\"Contained\"",
            includeImplicitScrollBarStyle: false);

        var exception = Assert.Throws<XunitException>(() => AssertComboBoxPopupAndLinkedRewardPicker(xaml));
        Assert.Contains("implicit themed ScrollBar style", exception.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void ComboBoxItemTemplate_ForwardsThemeForegroundAndTypography()
    {
        var xaml = File.ReadAllText(FindSourceFile(
            "VrcTwitchOscBridge",
            "UniversalTriggersManagerWindow.xaml"));
        var document = XDocument.Parse(xaml, LoadOptions.PreserveWhitespace);
        var comboItemStyle = Assert.Single(document.Descendants(), element =>
            IsElement(element, "Style")
            && AttributeValue(element, "TargetType") == "ComboBoxItem"
            && element.Attributes().All(attribute => attribute.Name.LocalName != "Key"));
        var contentPresenter = Assert.Single(
            comboItemStyle.Descendants(),
            element => IsElement(element, "ContentPresenter"));

        Assert.Equal(
            "{TemplateBinding Foreground}",
            AttributeValue(contentPresenter, "TextElement.Foreground"));
        Assert.Equal(
            "{TemplateBinding FontFamily}",
            AttributeValue(contentPresenter, "TextElement.FontFamily"));
        Assert.Equal(
            "{TemplateBinding FontSize}",
            AttributeValue(contentPresenter, "TextElement.FontSize"));
    }

    [Fact]
    public void CrystalRelayTheme_UsesConditionalDarkPurpleComboPalette()
    {
        var xaml = File.ReadAllText(FindSourceFile(
            "VrcTwitchOscBridge",
            "UniversalTriggersManagerWindow.xaml"));
        var document = XDocument.Parse(xaml, LoadOptions.PreserveWhitespace);
        var comboStyle = Assert.Single(document.Descendants(), element =>
            IsElement(element, "Style")
            && AttributeValue(element, "TargetType") == "ComboBox"
            && element.Attributes().All(attribute => attribute.Name.LocalName != "Key"));
        var comboItemStyle = Assert.Single(document.Descendants(), element =>
            IsElement(element, "Style")
            && AttributeValue(element, "TargetType") == "ComboBoxItem"
            && element.Attributes().All(attribute => attribute.Name.LocalName != "Key"));
        var comboToggleButtonStyle = Assert.Single(document.Descendants(), element =>
            IsElement(element, "Style")
            && AttributeValue(element, "Key") == "ComboBoxToggleButtonStyle");
        var source = File.ReadAllText(FindSourceFile(
            "VrcTwitchOscBridge",
            "UniversalTriggersManagerWindow.xaml.cs"));

        foreach (var resourceKey in new[]
        {
            "UniversalTriggerComboSurfaceBrush",
            "UniversalTriggerComboTextBrush",
            "UniversalTriggerComboHighlightBrush",
            "UniversalTriggerComboDropButtonBrush",
            "UniversalTriggerComboDropButtonHoverBrush",
            "UniversalTriggerComboDropButtonPressedBrush",
        })
        {
            Assert.Contains($"x:Key=\"{resourceKey}\"", xaml, StringComparison.Ordinal);
        }

        Assert.Contains(
            "{DynamicResource UniversalTriggerComboSurfaceBrush}",
            comboStyle.ToString(),
            StringComparison.Ordinal);
        Assert.Contains(
            "{DynamicResource UniversalTriggerComboTextBrush}",
            comboStyle.ToString(),
            StringComparison.Ordinal);
        Assert.Contains(
            "{DynamicResource UniversalTriggerComboTextBrush}",
            comboItemStyle.ToString(),
            StringComparison.Ordinal);
        Assert.Contains(
            "{DynamicResource UniversalTriggerComboHighlightBrush}",
            comboItemStyle.ToString(),
            StringComparison.Ordinal);
        Assert.Contains(
            "{DynamicResource UniversalTriggerComboDropButtonBrush}",
            comboToggleButtonStyle.ToString(),
            StringComparison.Ordinal);
        Assert.Contains(
            "{DynamicResource UniversalTriggerComboDropButtonHoverBrush}",
            comboToggleButtonStyle.ToString(),
            StringComparison.Ordinal);
        Assert.Contains(
            "{DynamicResource UniversalTriggerComboDropButtonPressedBrush}",
            comboToggleButtonStyle.ToString(),
            StringComparison.Ordinal);
        Assert.Contains(
            "private void ApplyUniversalTriggerThemeResources()",
            source,
            StringComparison.Ordinal);
        Assert.Contains(
            "ThemeManager.ApplyToResources(Resources, ThemeManager.CurrentTheme);",
            source,
            StringComparison.Ordinal);
        Assert.Contains(
            "if (ThemeManager.CurrentTheme != AppTheme.VoidCrystal)",
            source,
            StringComparison.Ordinal);
        Assert.Contains(
            "CopyUniversalTriggerBrushColor(\"ComboSurfaceBrush\", \"UniversalTriggerComboSurfaceBrush\")",
            source,
            StringComparison.Ordinal);
        Assert.Contains(
            "CopyUniversalTriggerBrushColor(\"ComboTextBrush\", \"UniversalTriggerComboTextBrush\")",
            source,
            StringComparison.Ordinal);
        Assert.Contains(
            "CopyUniversalTriggerBrushColor(\"ComboHighlightBrush\", \"UniversalTriggerComboHighlightBrush\")",
            source,
            StringComparison.Ordinal);
        Assert.Contains(
            "CopyUniversalTriggerBrushColor(\"ComboDropButtonBrush\", \"UniversalTriggerComboDropButtonBrush\")",
            source,
            StringComparison.Ordinal);
        Assert.Contains(
            "CopyUniversalTriggerBrushColor(\"ComboDropButtonHoverBrush\", \"UniversalTriggerComboDropButtonHoverBrush\")",
            source,
            StringComparison.Ordinal);
        Assert.Contains(
            "CopyUniversalTriggerBrushColor(\"ComboDropButtonPressedBrush\", \"UniversalTriggerComboDropButtonPressedBrush\")",
            source,
            StringComparison.Ordinal);
        Assert.Contains(
            "SetUniversalTriggerBrushColor(\"UniversalTriggerComboSurfaceBrush\", \"#2F1A4A\")",
            source,
            StringComparison.Ordinal);
        Assert.Contains(
            "SetUniversalTriggerBrushColor(\"UniversalTriggerComboTextBrush\", \"#F8F1FF\")",
            source,
            StringComparison.Ordinal);
        Assert.Contains(
            "SetUniversalTriggerBrushColor(\"UniversalTriggerComboHighlightBrush\", \"#5A2E8A\")",
            source,
            StringComparison.Ordinal);
        Assert.Contains(
            "SetUniversalTriggerBrushColor(\"UniversalTriggerComboDropButtonBrush\", \"#3A2160\")",
            source,
            StringComparison.Ordinal);
        Assert.Contains(
            "SetUniversalTriggerBrushColor(\"UniversalTriggerComboDropButtonHoverBrush\", \"#6F3FB0\")",
            source,
            StringComparison.Ordinal);
        Assert.Contains(
            "SetUniversalTriggerBrushColor(\"UniversalTriggerComboDropButtonPressedBrush\", \"#824DCF\")",
            source,
            StringComparison.Ordinal);
        Assert.Contains(
            "Dispatcher.BeginInvoke(ApplyUniversalTriggerThemeResources)",
            source,
            StringComparison.Ordinal);
    }

    [Fact]
    public void DeleteRewardWhenInactiveCheckbox_InheritsThemedCheckBoxStyle()
    {
        var xaml = File.ReadAllText(FindSourceFile(
            "VrcTwitchOscBridge",
            "UniversalTriggersManagerWindow.xaml"));
        var document = XDocument.Parse(xaml, LoadOptions.PreserveWhitespace);
        var checkbox = Assert.Single(document.Descendants(), element =>
            IsElement(element, "CheckBox")
            && AttributeValue(element, "Content") == "{loc:Translate 'Delete reward when inactive'}");
        var styleProperty = Assert.Single(
            checkbox.Elements(),
            element => IsElement(element, "CheckBox.Style"));
        var localStyle = Assert.Single(
            styleProperty.Elements(),
            element => IsElement(element, "Style"));

        Assert.Equal(
            "{StaticResource {x:Type CheckBox}}",
            AttributeValue(localStyle, "BasedOn"));
    }

    [Fact]
    public void ManagerWindow_DisposesViewModelWhenClosed()
    {
        var codeBehind = File.ReadAllText(FindSourceFile(
            "VrcTwitchOscBridge",
            "UniversalTriggersManagerWindow.xaml.cs"));

        Assert.Contains(
            "if (DataContext is IDisposable disposableDataContext)",
            codeBehind,
            StringComparison.Ordinal);
        Assert.Contains("disposableDataContext.Dispose();", codeBehind, StringComparison.Ordinal);
    }

    private static void AssertComboBoxPopupAndLinkedRewardPicker(string xaml)
    {
        var document = XDocument.Parse(xaml, LoadOptions.PreserveWhitespace);
        var comboStyle = Assert.Single(document.Descendants(), element =>
            IsElement(element, "Style")
            && AttributeValue(element, "TargetType") == "ComboBox"
            && element.Attributes().All(attribute => attribute.Name.LocalName != "Key"));
        var popup = Assert.Single(comboStyle.Descendants(), element =>
            IsElement(element, "Popup")
            && AttributeValue(element, "Name") == "PART_Popup");
        var itemsPresenter = Assert.Single(popup.Descendants(), element =>
            IsElement(element, "ItemsPresenter"));
        if (AttributeValue(itemsPresenter, "KeyboardNavigation.DirectionalNavigation") != "Contained")
        {
            throw new XunitException(
                "Popup ItemsPresenter must retain KeyboardNavigation.DirectionalNavigation=\"Contained\".");
        }

        var popupContentAncestors = itemsPresenter.Ancestors()
            .TakeWhile(element => !ReferenceEquals(element, popup))
            .ToArray();

        var scrollViewer = popupContentAncestors.FirstOrDefault(element =>
            IsElement(element, "ScrollViewer"));
        if (scrollViewer is null)
        {
            throw new XunitException("Popup ItemsPresenter must have a real ScrollViewer ancestor inside PART_Popup.");
        }

        Assert.Equal("Disabled", AttributeValue(scrollViewer, "HorizontalScrollBarVisibility"));
        Assert.Equal("Auto", AttributeValue(scrollViewer, "VerticalScrollBarVisibility"));

        var boundedGrid = popupContentAncestors.FirstOrDefault(element =>
            IsElement(element, "Grid")
            && AttributeValue(element, "MaxHeight") == "{TemplateBinding MaxDropDownHeight}");
        if (boundedGrid is null)
        {
            throw new XunitException("Popup ItemsPresenter must have the MaxDropDownHeight Grid ancestor inside PART_Popup.");
        }

        var windowResources = document.Root?.Elements()
            .SingleOrDefault(element => IsElement(element, "Window.Resources"));
        var implicitScrollBarStyles = windowResources?.Elements()
            .Where(element =>
                IsElement(element, "Style")
                && AttributeValue(element, "TargetType") == "ScrollBar"
                && element.Attributes().All(attribute => attribute.Name.LocalName != "Key"))
            .ToArray() ?? [];
        if (implicitScrollBarStyles.Length != 1
            || !implicitScrollBarStyles[0].Descendants().Any(element =>
                IsElement(element, "Setter")
                && AttributeValue(element, "Property") == "Template"
                && AttributeValue(element, "Value") == "{StaticResource VerticalScrollBarTemplate}"))
        {
            throw new XunitException(
                "Window resources must retain the implicit themed ScrollBar style used by popup scrollbars.");
        }

        var linkedRewardPanel = Assert.Single(document.Descendants(), IsLinkedRewardPanel);
        var linkedRewardComboBox = Assert.Single(linkedRewardPanel.Descendants(), element =>
            IsElement(element, "ComboBox"));
        Assert.Equal(
            "{Binding AvailableTwitchRewards}",
            AttributeValue(linkedRewardComboBox, "ItemsSource"));
        Assert.Equal(
            "{Binding SelectedTrigger.RewardId, Mode=TwoWay, UpdateSourceTrigger=PropertyChanged}",
            AttributeValue(linkedRewardComboBox, "SelectedValue"));
    }

    private static bool IsLinkedRewardPanel(XElement element)
    {
        return IsElement(element, "StackPanel")
            && element.Elements()
                .Where(child => IsElement(child, "StackPanel.Style"))
                .SelectMany(style => style.Descendants().Where(trigger => IsElement(trigger, "DataTrigger")))
                .Any(trigger =>
                    AttributeValue(trigger, "Binding") == "{Binding SelectedTrigger.UsesLinkedExistingReward}"
                    && AttributeValue(trigger, "Value") == "True"
                    && trigger.Descendants().Any(setter =>
                        IsElement(setter, "Setter")
                        && AttributeValue(setter, "Property") == "Visibility"
                        && AttributeValue(setter, "Value") == "Visible"));
    }

    private static bool IsElement(XElement element, string localName)
    {
        return element.Name.LocalName == localName;
    }

    private static string? AttributeValue(XElement element, string localName)
    {
        return element.Attributes().SingleOrDefault(attribute => attribute.Name.LocalName == localName)?.Value;
    }

    private static string BuildValidFixture(
        string itemsPresenterAttributes,
        bool includeImplicitScrollBarStyle)
    {
        const string fixture = """
            <Window>
                <Window.Resources>
                    __SCROLLBAR_STYLE__
                    <Style TargetType="ComboBox">
                        <Setter Property="Template">
                            <Setter.Value>
                                <ControlTemplate TargetType="ComboBox">
                                    <Popup Name="PART_Popup">
                                        <Grid MaxHeight="{TemplateBinding MaxDropDownHeight}">
                                            <ScrollViewer HorizontalScrollBarVisibility="Disabled"
                                                          VerticalScrollBarVisibility="Auto">
                                                <ItemsPresenter __ITEMS_PRESENTER_ATTRIBUTES__ />
                                            </ScrollViewer>
                                        </Grid>
                                    </Popup>
                                </ControlTemplate>
                            </Setter.Value>
                        </Setter>
                    </Style>
                </Window.Resources>
                <StackPanel>
                    <StackPanel.Style>
                        <Style TargetType="StackPanel">
                            <Style.Triggers>
                                <DataTrigger Binding="{Binding SelectedTrigger.UsesLinkedExistingReward}" Value="True">
                                    <Setter Property="Visibility" Value="Visible" />
                                </DataTrigger>
                            </Style.Triggers>
                        </Style>
                    </StackPanel.Style>
                    <ComboBox ItemsSource="{Binding AvailableTwitchRewards}"
                              SelectedValue="{Binding SelectedTrigger.RewardId, Mode=TwoWay, UpdateSourceTrigger=PropertyChanged}" />
                </StackPanel>
            </Window>
            """;
        const string scrollBarStyle = """
            <Style TargetType="ScrollBar">
                <Setter Property="Template" Value="{StaticResource VerticalScrollBarTemplate}" />
            </Style>
            """;

        return fixture
            .Replace(
                "__SCROLLBAR_STYLE__",
                includeImplicitScrollBarStyle ? scrollBarStyle : string.Empty,
                StringComparison.Ordinal)
            .Replace(
                "__ITEMS_PRESENTER_ATTRIBUTES__",
                itemsPresenterAttributes,
                StringComparison.Ordinal);
    }

    private static string FindSourceFile(params string[] relativeParts)
    {
        var directory = new DirectoryInfo(AppContext.BaseDirectory);
        while (directory is not null)
        {
            var candidate = Path.Combine(new[] { directory.FullName }.Concat(relativeParts).ToArray());
            if (File.Exists(candidate))
            {
                return candidate;
            }

            directory = directory.Parent;
        }

        throw new FileNotFoundException("Could not find source file.", Path.Combine(relativeParts));
    }
}
