using System;
using System.IO;
using System.Linq;
using System.Threading;
using System.Windows;
using System.Windows.Controls;
using System.Xml.Linq;
using Xunit;

namespace VrcTwitchOscBridge.Tests;

public sealed class AvatarSwapPopupLayoutXamlTests
{
    private static readonly string[] RewardRowControlFiles =
    [
        "InlineChannelPointRuleRowControl.xaml",
        "InlineBitsRuleRowControl.xaml",
        "InlinePowerUpRuleRowControl.xaml",
        "InlineSubsRuleRowControl.xaml",
        "InlinePaymentRuleRowControl.xaml",
        "InlineRouletteRuleRowControl.xaml"
    ];

    [Fact]
    public void AvatarSwapManager_EditorColumnIsBoundedByAvailableWindowWidth()
    {
        var sourceFile = FindSourceFile("VrcTwitchOscBridge", "AvatarSwapManagerWindow.xaml");
        var xaml = File.ReadAllText(sourceFile);
        var document = XDocument.Parse(xaml, LoadOptions.PreserveWhitespace);
        var start = xaml.IndexOf("<!-- Left cards + right editor -->", StringComparison.Ordinal);
        var end = xaml.IndexOf("<!-- Left grid -->", start, StringComparison.Ordinal);

        Assert.True(start >= 0, "The Avatar Swap manager should retain its left/right editor layout marker.");
        Assert.True(end > start, "The left/right editor layout should contain the left-grid marker.");

        var columns = xaml[start..end];
        Assert.Contains("<ColumnDefinition Width=\"220\" />", columns, StringComparison.Ordinal);
        Assert.Contains("<ColumnDefinition Width=\"*\" MinWidth=\"0\" />", columns, StringComparison.Ordinal);
        Assert.DoesNotContain("<ColumnDefinition Width=\"Auto\" />", columns, StringComparison.Ordinal);

        var rightEditor = Assert.Single(document.Descendants(), element =>
            IsElement(element, "ContentControl")
            && AttributeValue(element, "Grid.Column") == "1"
            && element.Attribute("HorizontalContentAlignment") is not null);
        Assert.Equal("Stretch", AttributeValue(rightEditor, "HorizontalContentAlignment"));
    }

    [Fact]
    public void AvatarSwapManager_LeftCardPaneScrollsIndependently()
    {
        var document = LoadSourceFile("VrcTwitchOscBridge", "AvatarSwapManagerWindow.xaml");
        var cardPane = document.Descendants().SingleOrDefault(element =>
            IsElement(element, "ScrollViewer")
            && AttributeValue(element, "Grid.Column") == "0"
            && AttributeValue(element, "VerticalScrollBarVisibility") == "Auto");

        Assert.NotNull(cardPane);
        Assert.Equal("Disabled", AttributeValue(cardPane!, "HorizontalScrollBarVisibility"));
        Assert.Equal("Stretch", AttributeValue(cardPane, "HorizontalContentAlignment"));
        Assert.NotNull(cardPane.Descendants().SingleOrDefault(element =>
            IsElement(element, "ItemsControl")
            && AttributeValue(element, "ItemsSource") == "{Binding SwapCards}"));
    }

    [Fact]
    public void AvatarSwapManager_RewardListsAndRoulettePoolCannotCreateHorizontalOverflow()
    {
        var document = LoadSourceFile("VrcTwitchOscBridge", "AvatarSwapManagerWindow.xaml");
        var listScrollViewers = document.Descendants()
            .Where(element =>
                IsElement(element, "ScrollViewer")
                && AttributeValue(element, "Grid.Column") is null
                && AttributeValue(element, "VerticalScrollBarVisibility") == "Auto")
            .ToArray();

        Assert.Equal(2, listScrollViewers.Length);
        Assert.All(listScrollViewers, viewer =>
            Assert.Equal("Disabled", AttributeValue(viewer, "HorizontalScrollBarVisibility")));

        var rewardRowLists = document.Descendants()
            .Where(element =>
                IsElement(element, "ItemsControl")
                && AttributeValue(element, "ItemsSource")?.Contains("DataContext.", StringComparison.Ordinal) == true
                && AttributeValue(element, "ItemsSource")?.Contains("Rows", StringComparison.Ordinal) == true)
            .ToArray();
        Assert.Equal(11, rewardRowLists.Length);
        Assert.All(rewardRowLists, itemsControl =>
            Assert.Equal("Stretch", AttributeValue(itemsControl, "HorizontalContentAlignment")));

        var pool = FindSegment(
            File.ReadAllText(FindSourceFile("VrcTwitchOscBridge", "AvatarSwapManagerWindow.xaml")),
            "<ItemsControl ItemsSource=\"{Binding DataContext.RoulettePoolRows",
            "<Button Content=\"+ Add Avatar to Pool\"");
        Assert.Contains("HorizontalContentAlignment=\"Stretch\"", pool, StringComparison.Ordinal);
        Assert.Contains("<WrapPanel />", pool, StringComparison.Ordinal);
    }

    [Fact]
    public void InlineRewardRows_ReserveActionsAndExposeFullTrimmedSummary()
    {
        foreach (var fileName in RewardRowControlFiles)
        {
            var document = LoadSourceFile(
                "VrcTwitchOscBridge",
                "UserControls",
                fileName);
            var rowBorder = Assert.Single(document.Root!.Elements(), element => IsElement(element, "Border"));
            var rowGrid = Assert.Single(rowBorder.Elements(), element => IsElement(element, "Grid"));
            var columns = Assert.Single(rowGrid.Elements(), element => IsElement(element, "Grid.ColumnDefinitions"))
                .Elements()
                .Where(element => IsElement(element, "ColumnDefinition"))
                .Select(element => AttributeValue(element, "Width"))
                .ToArray();

            Assert.Contains("*", columns);
            Assert.Contains("Auto", columns);

            var summary = Assert.Single(rowGrid.Descendants(), element =>
                IsElement(element, "TextBlock")
                && AttributeValue(element, "Text") == "{Binding Summary}");
            Assert.Equal("CharacterEllipsis", AttributeValue(summary, "TextTrimming"));
            Assert.Equal("{Binding Summary}", AttributeValue(summary, "ToolTip"));

            var actionPanel = Assert.Single(rowGrid.Descendants(), element =>
                IsElement(element, "StackPanel")
                && element.Descendants().Any(button =>
                    IsElement(button, "Button")
                    && AttributeValue(button, "Command") == "{Binding EditCommand}")
                && element.Descendants().Any(button =>
                    IsElement(button, "Button")
                    && AttributeValue(button, "Command") == "{Binding DeleteCommand}"));
            var summaryColumn = ParseGridColumn(summary);
            var actionColumn = ParseGridColumn(actionPanel);

            Assert.InRange(summaryColumn, 0, columns.Length - 1);
            Assert.InRange(actionColumn, 0, columns.Length - 1);
            Assert.Equal("*", columns[summaryColumn]);
            Assert.Equal("Auto", columns[actionColumn]);
            Assert.True(actionColumn > summaryColumn, $"{fileName} should place actions after the summary column.");
        }
    }

    [Fact]
    public void StarSummaryAndAutoActionsRemainVisibleAtConstrainedWidth()
    {
        Exception? failure = null;
        var thread = new Thread(() =>
        {
            try
            {
                const double rowWidth = 240;
                var row = new Grid();
                row.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
                row.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });

                var summary = new TextBlock
                {
                    Text = new string('X', 500),
                    TextTrimming = TextTrimming.CharacterEllipsis
                };
                var actions = new StackPanel { Orientation = Orientation.Horizontal };
                actions.Children.Add(new Button { Width = 26, Height = 26 });
                actions.Children.Add(new Button { Width = 26, Height = 26 });

                Grid.SetColumn(summary, 0);
                Grid.SetColumn(actions, 1);
                row.Children.Add(summary);
                row.Children.Add(actions);

                row.Measure(new Size(rowWidth, double.PositiveInfinity));
                row.Arrange(new Rect(0, 0, rowWidth, row.DesiredSize.Height));
                var actionRight = actions.TranslatePoint(new Point(0, 0), row).X + actions.ActualWidth;

                Assert.True(summary.ActualWidth > 0, "The summary should receive the constrained star-column width.");
                Assert.True(actions.ActualWidth > 0, "The action column should remain measurable.");
                Assert.True(actionRight <= rowWidth, "The action column should remain inside the constrained row.");
            }
            catch (Exception exception)
            {
                failure = exception;
            }
        });
        thread.SetApartmentState(ApartmentState.STA);
        thread.Start();
        thread.Join();

        Assert.Null(failure);
    }

    private static XDocument LoadSourceFile(params string[] relativeParts)
    {
        return XDocument.Parse(
            File.ReadAllText(FindSourceFile(relativeParts)),
            LoadOptions.PreserveWhitespace);
    }

    private static string FindSegment(string text, string startMarker, string endMarker)
    {
        var start = text.IndexOf(startMarker, StringComparison.Ordinal);
        Assert.True(start >= 0, $"Could not find segment start: {startMarker}");
        if (start < 0)
        {
            return string.Empty;
        }

        var end = text.IndexOf(endMarker, start, StringComparison.Ordinal);
        Assert.True(end > start, $"Could not find segment end: {endMarker}");
        if (end <= start)
        {
            return string.Empty;
        }

        return text[start..end];
    }

    private static bool IsElement(XElement element, string localName) =>
        element.Name.LocalName == localName;

    private static string? AttributeValue(XElement element, string localName) =>
        element.Attributes().SingleOrDefault(attribute => attribute.Name.LocalName == localName)?.Value;

    private static int ParseGridColumn(XElement element)
    {
        return int.Parse(AttributeValue(element, "Grid.Column") ?? "0");
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
