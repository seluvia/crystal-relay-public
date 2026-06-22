using System;
using System.IO;
using System.Linq;
using Xunit;

namespace VrcTwitchOscBridge.Tests;

public sealed class AvatarSetsManagerWindowXamlTests
{
    [Fact]
    public void RuleEditor_ExposesSelectedRuleParameterTypeSelectorBeforeParameterPicker()
    {
        var xaml = File.ReadAllText(FindSourceFile("VrcTwitchOscBridge", "AvatarSetsManagerWindow.xaml"));
        var parameterTypeLabelIndex = xaml.IndexOf("Text=\"Parameter Type\"", StringComparison.Ordinal);
        var pickerLabelIndex = xaml.IndexOf("Text=\"Search &amp; Pick Parameter\"", StringComparison.Ordinal);
        var selectorIndex = xaml.IndexOf("SelectedItem=\"{Binding ParameterType, UpdateSourceTrigger=PropertyChanged}\"", StringComparison.Ordinal);

        Assert.True(parameterTypeLabelIndex >= 0, "The rule editor should still label the selected parameter type section.");
        Assert.True(pickerLabelIndex > parameterTypeLabelIndex, "The parameter picker should follow the selected parameter type section.");
        Assert.InRange(selectorIndex, parameterTypeLabelIndex, pickerLabelIndex);
    }

    [Fact]
    public void FloatModeChips_AreEachTaggedWithTheirModeAndNotSelectedByStyleTriggersAlone()
    {
        var xaml = File.ReadAllText(FindSourceFile("VrcTwitchOscBridge", "AvatarSetsManagerWindow.xaml"));
        var chipStyleIndex = xaml.IndexOf("x:Key=\"ChipButtonStyle\"", StringComparison.Ordinal);
        Assert.True(chipStyleIndex >= 0, "ChipButtonStyle should exist.");
        var chipStyleEnd = xaml.IndexOf("</Style>", chipStyleIndex, StringComparison.Ordinal);
        var chipStyleBlock = xaml.Substring(chipStyleIndex, chipStyleEnd - chipStyleIndex);

        foreach (var mode in new[] { "Set", "Random", "Add", "Subtract", "AddSubtract", "Multiply", "Toggle", "Cycle", "Glitchy", "Pulse" })
        {
            var tagNeedle = $"Tag=\"{{x:Static models:FloatActionMode.{mode}}}\"";
            Assert.Contains(tagNeedle, xaml, StringComparison.Ordinal);
            Assert.DoesNotContain($"Value=\"{mode}\"", chipStyleBlock, StringComparison.Ordinal);
        }
    }

    [Fact]
    public void FloatModeChips_UseUniformGridLayoutNotWrapPanel()
    {
        var xaml = File.ReadAllText(FindSourceFile("VrcTwitchOscBridge", "AvatarSetsManagerWindow.xaml"));
        var headerIndex = xaml.IndexOf("FloatActionModeHeader", StringComparison.Ordinal);
        Assert.True(headerIndex >= 0, "FloatActionModeHeader should exist.");
        var searchWindow = xaml.Substring(headerIndex, Math.Min(2400, xaml.Length - headerIndex));

        Assert.Contains("UniformGrid Columns=\"5\"", searchWindow, StringComparison.Ordinal);
        Assert.DoesNotContain("<WrapPanel>", searchWindow, StringComparison.Ordinal);
    }

    [Fact]
    public void FloatModeCard_UsesThemeBrushNotHardcodedColor()
    {
        var xaml = File.ReadAllText(FindSourceFile("VrcTwitchOscBridge", "AvatarSetsManagerWindow.xaml"));
        var headerIndex = xaml.IndexOf("FloatActionModeHeader", StringComparison.Ordinal);
        Assert.True(headerIndex >= 0, "FloatActionModeHeader should exist.");
        var searchWindow = xaml.Substring(Math.Max(0, headerIndex - 600), 600);

        Assert.DoesNotContain("Background=\"#22FFFFFF\"", searchWindow, StringComparison.Ordinal);
        Assert.Contains("{DynamicResource", searchWindow, StringComparison.Ordinal);
    }

    [Fact]
    public void ComboBoxStyle_ExistsAndUsesThemeBrushes()
    {
        var xaml = File.ReadAllText(FindSourceFile("VrcTwitchOscBridge", "AvatarSetsManagerWindow.xaml"));
        var styleIndex = xaml.IndexOf("x:Key=\"ComboBoxStyle\"", StringComparison.Ordinal);
        Assert.True(styleIndex >= 0, "ComboBoxStyle should be defined as a resource.");
        var styleEnd = xaml.IndexOf("</Style>", styleIndex, StringComparison.Ordinal);
        var styleBlock = xaml.Substring(styleIndex, styleEnd - styleIndex);

        Assert.Contains("{DynamicResource TextBrush}", styleBlock, StringComparison.Ordinal);
        Assert.Contains("{DynamicResource InputBrush}", styleBlock, StringComparison.Ordinal);
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
