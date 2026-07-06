using System;
using System.IO;
using System.Linq;
using Xunit;

namespace VrcTwitchOscBridge.Tests;

public sealed class MovementRedeemsManagerWindowXamlTests
{
    [Fact]
    public void Window_UsesWorkingOverlayPatternWithoutGlobalScrollViewerTemplate()
    {
        var xaml = File.ReadAllText(FindSourceFile("VrcTwitchOscBridge", "MovementRedeemsManagerWindow.xaml"));

        Assert.Contains("Background=\"{DynamicResource WindowBackgroundBrush}\"", xaml, StringComparison.Ordinal);
        Assert.Contains("<Grid Grid.Row=\"0\" Grid.RowSpan=\"2\"", xaml, StringComparison.Ordinal);
        Assert.Contains("MouseLeftButtonUp=\"OnEditorBackdropClicked\"", xaml, StringComparison.Ordinal);
        Assert.Contains("<Border Width=\"480\"", xaml, StringComparison.Ordinal);
        Assert.Contains("HorizontalAlignment=\"Right\"", xaml, StringComparison.Ordinal);
        Assert.Contains("ScrollViewer.HorizontalScrollBarVisibility=\"Disabled\"", xaml, StringComparison.Ordinal);
        Assert.DoesNotContain("MouseDown=\"OnCloseEditor\"", xaml, StringComparison.Ordinal);
        Assert.DoesNotContain("Storyboard.TargetProperty=\"Margin\"", xaml, StringComparison.Ordinal);
    }

    [Fact]
    public void Window_ScrollBarStyleUsesCustomDarkTemplateToHideWhiteThumb()
    {
        var xaml = File.ReadAllText(FindSourceFile("VrcTwitchOscBridge", "MovementRedeemsManagerWindow.xaml"));

        var scrollBarStyleIndex = xaml.IndexOf("<Style TargetType=\"ScrollBar\">", StringComparison.Ordinal);
        Assert.True(scrollBarStyleIndex >= 0, "Style TargetType=ScrollBar should exist.");
        var styleEndIndex = xaml.IndexOf("</Style>", scrollBarStyleIndex, StringComparison.Ordinal);
        var styleBlock = xaml[scrollBarStyleIndex..(styleEndIndex + "</Style>".Length)];

        Assert.Contains("Template", styleBlock, StringComparison.Ordinal);
        Assert.Contains("VerticalScrollBarTemplate", styleBlock, StringComparison.Ordinal);
        Assert.Contains("HorizontalScrollBarTemplate", styleBlock, StringComparison.Ordinal);
    }

    private static string FindSourceFile(params string[] relativeParts)
    {
        var current = AppContext.BaseDirectory;
        while (!string.IsNullOrWhiteSpace(current))
        {
            var candidate = Path.Combine(new[] { current }.Concat(relativeParts).ToArray());
            if (File.Exists(candidate))
            {
                return candidate;
            }

            current = Directory.GetParent(current)?.FullName ?? string.Empty;
        }

        throw new FileNotFoundException($"Could not find source file {Path.Combine(relativeParts)}.");
    }
}
