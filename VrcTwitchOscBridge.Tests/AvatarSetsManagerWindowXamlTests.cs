using System;
using System.IO;
using System.Linq;
using Xunit;

namespace VrcTwitchOscBridge.Tests;

public sealed class AvatarSetsManagerWindowXamlTests
{
    [Fact]
    public void RuleEditor_HidesValueTypeSelectorButKeepsParameterFilterAndPicker()
    {
        var xaml = File.ReadAllText(FindSourceFile("VrcTwitchOscBridge", "AvatarSetsManagerWindow.xaml"));
        var parameterListFilterIndex = xaml.IndexOf("Parameter List Filter", StringComparison.Ordinal);
        var pickerLabelIndex = xaml.IndexOf("Text=\"Search &amp; Pick Parameter\"", StringComparison.Ordinal);
        var filteredParametersIndex = xaml.IndexOf("ItemsSource=\"{Binding DataContext.FilteredParameters, RelativeSource={RelativeSource AncestorType=Window}}\"", StringComparison.Ordinal);
        var pickerBlockEnd = filteredParametersIndex >= 0
            ? xaml.IndexOf("</ItemsControl>", filteredParametersIndex, StringComparison.Ordinal)
            : -1;

        Assert.DoesNotContain("{loc:Translate 'Value Type'}", xaml, StringComparison.Ordinal);
        Assert.DoesNotContain("DataContext.ParameterTypes", xaml, StringComparison.Ordinal);
        Assert.DoesNotContain("SelectedItem=\"{Binding ParameterType, UpdateSourceTrigger=PropertyChanged}\"", xaml, StringComparison.Ordinal);
        Assert.True(parameterListFilterIndex >= 0, "The parameter list filter should remain visible for narrowing the picker list.");
        Assert.True(pickerLabelIndex >= 0, "The Search & Pick Parameter label should remain visible.");
        Assert.True(filteredParametersIndex > pickerLabelIndex, "The picker should continue binding to FilteredParameters.");
        Assert.True(pickerBlockEnd > filteredParametersIndex, "The picker binding should be inside a bounded ItemsControl block.");
        var pickerBlock = xaml.Substring(filteredParametersIndex, pickerBlockEnd - filteredParametersIndex);
        Assert.Contains("Click=\"OnParameterItemClicked\"", pickerBlock, StringComparison.Ordinal);
        Assert.Contains("Tag=\"{Binding}\"", pickerBlock, StringComparison.Ordinal);
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
        var implicitStyleIndex = xaml.IndexOf("<Style TargetType=\"ComboBox\">", StringComparison.Ordinal);
        var styleIndex = xaml.IndexOf("x:Key=\"ComboBoxStyle\"", StringComparison.Ordinal);
        Assert.Contains("<SolidColorBrush x:Key=\"ComboTextBrush\" Color=\"#140C20\"", xaml, StringComparison.Ordinal);
        Assert.Contains("<SolidColorBrush x:Key=\"ComboSurfaceBrush\" Color=\"#F7F2FF\"", xaml, StringComparison.Ordinal);
        Assert.True(implicitStyleIndex >= 0, "The implicit themed ComboBox style should be defined.");
        Assert.True(styleIndex >= 0, "ComboBoxStyle should be defined as a resource.");
        Assert.True(styleIndex > implicitStyleIndex, "ComboBoxStyle should be defined after the implicit ComboBox style so BasedOn can inherit the themed template.");

        var implicitStyleEnd = xaml.IndexOf("</Style>", implicitStyleIndex, StringComparison.Ordinal);
        var implicitStyleBlock = xaml.Substring(implicitStyleIndex, implicitStyleEnd - implicitStyleIndex);
        Assert.Contains("{DynamicResource ComboTextBrush}", implicitStyleBlock, StringComparison.Ordinal);
        Assert.Contains("{DynamicResource ComboSurfaceBrush}", implicitStyleBlock, StringComparison.Ordinal);
        Assert.Contains("TextElement.Foreground=\"{DynamicResource ComboTextBrush}\"", implicitStyleBlock, StringComparison.Ordinal);
        Assert.Contains("PART_Popup", implicitStyleBlock, StringComparison.Ordinal);

        var styleEnd = xaml.IndexOf("</Style>", styleIndex, StringComparison.Ordinal);
        var styleBlock = xaml.Substring(styleIndex, styleEnd - styleIndex);

        Assert.Contains("BasedOn=\"{StaticResource {x:Type ComboBox}}\"", styleBlock, StringComparison.Ordinal);
        Assert.Contains("{DynamicResource ComboTextBrush}", styleBlock, StringComparison.Ordinal);
        Assert.Contains("{DynamicResource ComboSurfaceBrush}", styleBlock, StringComparison.Ordinal);

        var comboBoxItemStyleIndex = xaml.IndexOf("<Style TargetType=\"ComboBoxItem\">", styleIndex, StringComparison.Ordinal);
        Assert.True(comboBoxItemStyleIndex > styleIndex, "ComboBoxItem style should be defined after ComboBoxStyle.");
        var comboBoxItemStyleEnd = xaml.IndexOf("</Style>", comboBoxItemStyleIndex, StringComparison.Ordinal);
        var comboBoxItemStyleBlock = xaml.Substring(comboBoxItemStyleIndex, comboBoxItemStyleEnd - comboBoxItemStyleIndex);
        Assert.Contains("{DynamicResource ComboTextBrush}", comboBoxItemStyleBlock, StringComparison.Ordinal);
        Assert.Contains("{DynamicResource ComboHighlightBrush}", comboBoxItemStyleBlock, StringComparison.Ordinal);
    }

    [Fact]
    public void CompactEditor_HasIntModeSelectorBeforeParameterListFilter()
    {
        var xaml = File.ReadAllText(FindSourceFile("VrcTwitchOscBridge", "AvatarSetsManagerWindow.xaml"));
        var intModeSelector = xaml.IndexOf("SelectedItem=\"{Binding IntZeroDurationMode, UpdateSourceTrigger=PropertyChanged}\"", StringComparison.Ordinal);
        var intModeDataSource = xaml.IndexOf("DataContext.IntZeroDurationModes", StringComparison.Ordinal);
        var parameterListFilterIndex = xaml.IndexOf("Parameter List Filter", StringComparison.Ordinal);

        Assert.True(intModeSelector >= 0, "Int mode selector should remain available for Int parameters.");
        Assert.True(intModeDataSource >= 0, "Int mode selector should bind to DataContext.IntZeroDurationModes.");
        Assert.True(parameterListFilterIndex > intModeSelector, "The parameter list filter should appear after the Int action controls.");
    }

    [Fact]
    public void CompactEditor_IntInputs_BindToRangeAndWhenAfter()
    {
        var xaml = File.ReadAllText(FindSourceFile("VrcTwitchOscBridge", "AvatarSetsManagerWindow.xaml"));
        var intModeIndex = xaml.IndexOf("SelectedItem=\"{Binding IntZeroDurationMode, UpdateSourceTrigger=PropertyChanged}\"", StringComparison.Ordinal);
        Assert.True(intModeIndex >= 0, "Int mode selector must exist before Min/Max/When/After inputs.");

        var minBinding = xaml.IndexOf("Text=\"{Binding RangeMinimum, UpdateSourceTrigger=PropertyChanged}\"", StringComparison.Ordinal);
        var maxBinding = xaml.IndexOf("Text=\"{Binding RangeMaximum, UpdateSourceTrigger=PropertyChanged}\"", StringComparison.Ordinal);
        var whenBinding = xaml.IndexOf("Text=\"{Binding ResetValue, UpdateSourceTrigger=PropertyChanged}\"", StringComparison.Ordinal);

        Assert.True(minBinding > intModeIndex, "RangeMinimum text box should be in the Int section, after the Int mode selector.");
        Assert.True(maxBinding > intModeIndex, "RangeMaximum text box should be in the Int section, after the Int mode selector.");
        Assert.True(whenBinding > intModeIndex, "ResetValue (After Active Time) text box should be in the Int section, after the Int mode selector.");
    }

    [Fact]
    public void RuleEditor_ParameterPickerKeepsCodeBehindTypeAssignment()
    {
        var codeBehind = File.ReadAllText(FindSourceFile("VrcTwitchOscBridge", "AvatarSetsManagerWindow.xaml.cs"));
        var handlerIndex = codeBehind.IndexOf("private void OnParameterItemClicked", StringComparison.Ordinal);
        Assert.True(handlerIndex >= 0, "OnParameterItemClicked should exist.");

        var handlerEnd = codeBehind.IndexOf("private void OnWardrobeParameterItemClicked", handlerIndex, StringComparison.Ordinal);
        Assert.True(handlerEnd > handlerIndex, "OnParameterItemClicked should be bounded by the wardrobe picker handler.");
        var handlerBlock = codeBehind.Substring(handlerIndex, handlerEnd - handlerIndex);

        Assert.Contains("rule.ParameterName = p.Name;", handlerBlock, StringComparison.Ordinal);
        Assert.Contains("rule.ParameterType = p.ParameterType;", handlerBlock, StringComparison.Ordinal);
        Assert.Contains("Vm.ParameterNameFilter = string.Empty;", handlerBlock, StringComparison.Ordinal);
    }

    [Fact]
    public void RuleEditor_ParameterValueBoolChips_AreGatedToBoolParameters()
    {
        var xaml = File.ReadAllText(FindSourceFile("VrcTwitchOscBridge", "AvatarSetsManagerWindow.xaml"));
        var trueChipIndex = xaml.IndexOf("Click=\"OnParameterValueTrueClicked\"", StringComparison.Ordinal);
        Assert.True(trueChipIndex >= 0, "The True parameter-value chip should exist.");

        // Walk back to the parent StackPanel that gates the bool chips.
        var gridStart = xaml.LastIndexOf("<UniformGrid", trueChipIndex, StringComparison.Ordinal);
        Assert.True(gridStart >= 0, "The bool chips should live inside a UniformGrid.");
        var panelStart = xaml.LastIndexOf("<StackPanel", gridStart, StringComparison.Ordinal);
        Assert.True(panelStart >= 0, "The bool chips should be wrapped in a StackPanel.");
        var panelEnd = xaml.IndexOf(">", panelStart, StringComparison.Ordinal);
        var panelTag = xaml.Substring(panelStart, panelEnd - panelStart + 1);

        Assert.Contains("UsesBoolParameter", panelTag, StringComparison.Ordinal);
        Assert.Contains("BoolToVisibilityConverter", panelTag, StringComparison.Ordinal);
    }

    [Fact]
    public void RuleEditor_ResetValueControls_AreTypeSpecific()
    {
        var xaml = File.ReadAllText(FindSourceFile("VrcTwitchOscBridge", "AvatarSetsManagerWindow.xaml"));

        // Bool reset chips must only show for bool parameters.
        var resetTrueChipIndex = xaml.IndexOf("Click=\"OnResetValueTrueClicked\"", StringComparison.Ordinal);
        Assert.True(resetTrueChipIndex >= 0, "The True reset-value chip should exist.");
        var gridStart = xaml.LastIndexOf("<UniformGrid", resetTrueChipIndex, StringComparison.Ordinal);
        Assert.True(gridStart >= 0, "The reset bool chips should live inside a UniformGrid.");
        var boolPanelStart = xaml.LastIndexOf("<StackPanel", gridStart, StringComparison.Ordinal);
        Assert.True(boolPanelStart >= 0, "The reset bool chips should be wrapped in a StackPanel.");
        var boolPanelEnd = xaml.IndexOf(">", boolPanelStart, StringComparison.Ordinal);
        var boolPanelTag = xaml.Substring(boolPanelStart, boolPanelEnd - boolPanelStart + 1);
        Assert.Contains("UsesBoolParameter", boolPanelTag, StringComparison.Ordinal);
        Assert.Contains("BoolToVisibilityConverter", boolPanelTag, StringComparison.Ordinal);

        // The free-form reset text box is for Float/String (Int has its own After Active Time input).
        var resetTextBoxIndex = xaml.IndexOf("Text=\"{Binding ResetValue, UpdateSourceTrigger=PropertyChanged}\"", resetTrueChipIndex, StringComparison.Ordinal);
        Assert.True(resetTextBoxIndex >= 0, "A text box bound to ResetValue should exist for float/string types.");
        var textBoxStart = xaml.LastIndexOf("<TextBox", resetTextBoxIndex, StringComparison.Ordinal);
        var nonBoolPanelStart = xaml.LastIndexOf("<StackPanel", textBoxStart, StringComparison.Ordinal);
        Assert.True(nonBoolPanelStart >= 0, "The non-bool reset text box should be wrapped in a StackPanel.");
        var nonBoolPanelEnd = xaml.IndexOf(">", nonBoolPanelStart, StringComparison.Ordinal);
        var nonBoolPanelTag = xaml.Substring(nonBoolPanelStart, nonBoolPanelEnd - nonBoolPanelStart + 1);
        Assert.Contains("UsesTextOrFloatParameter", nonBoolPanelTag, StringComparison.Ordinal);
        Assert.Contains("BoolToVisibilityConverter", nonBoolPanelTag, StringComparison.Ordinal);
    }

    [Fact]
    public void FloatActionModeLabels_UseThemeForeground()
    {
        var xaml = File.ReadAllText(FindSourceFile("VrcTwitchOscBridge", "AvatarSetsManagerWindow.xaml"));
        var headerIndex = xaml.IndexOf("FloatActionModeHeader", StringComparison.Ordinal);
        Assert.True(headerIndex >= 0, "Float Action Mode section should exist.");

        // Inspect the Float Action Mode card (bounded by the next major section: Parameter Name).
        var parameterNameLabelIndex = xaml.IndexOf("Parameter Name (selected)", headerIndex, StringComparison.Ordinal);
        var cardBlock = xaml.Substring(headerIndex, parameterNameLabelIndex - headerIndex);

        // Every TextBlock label in the card should explicitly use the theme foreground.
        var labelMatches = System.Text.RegularExpressions.Regex.Matches(cardBlock, @"<TextBlock\b[^>]*>");
        Assert.All(labelMatches, match =>
        {
            Assert.Contains("Foreground=\"{DynamicResource TextBrush}\"", match.Value, StringComparison.Ordinal);
        });
    }

    [Fact]
    public void FloatModeSetInput_ExistsAndBindsToParameterValue()
    {
        var xaml = File.ReadAllText(FindSourceFile("VrcTwitchOscBridge", "AvatarSetsManagerWindow.xaml"));
        var headerIndex = xaml.IndexOf("FloatActionModeHeader", StringComparison.Ordinal);
        Assert.True(headerIndex >= 0, "Float Action Mode section should exist.");

        var setInputIndex = xaml.IndexOf("UsesFloatSetMode", headerIndex, StringComparison.Ordinal);
        Assert.True(setInputIndex >= 0, "A 'Set' mode input should exist in the Float Action Mode card.");

        var parameterValueBinding = xaml.IndexOf("Text=\"{Binding ParameterValue, UpdateSourceTrigger=PropertyChanged}\"", setInputIndex, StringComparison.Ordinal);
        Assert.True(parameterValueBinding > setInputIndex, "The Set mode input should bind to ParameterValue.");
    }

    [Fact]
    public void FloatModeChips_HighlightSelectedMode()
    {
        var xaml = File.ReadAllText(FindSourceFile("VrcTwitchOscBridge", "AvatarSetsManagerWindow.xaml"));
        var chipStyleIndex = xaml.IndexOf("x:Key=\"FloatModeChipButtonStyle\"", StringComparison.Ordinal);
        Assert.True(chipStyleIndex >= 0, "FloatModeChipButtonStyle should exist for selected-mode highlighting.");
        var headerIndex = xaml.IndexOf("FloatActionModeHeader", StringComparison.Ordinal);
        Assert.True(headerIndex >= 0, "Float Action Mode section should exist.");
        var sectionEnd = xaml.IndexOf("Paired Rules", headerIndex, StringComparison.Ordinal);
        var sectionBlock = xaml.Substring(headerIndex, sectionEnd - headerIndex);

        foreach (var mode in new[] { "Set", "Random", "Add", "Subtract", "AddSubtract", "Multiply", "Toggle", "Cycle", "Glitchy", "Pulse" })
        {
            var triggerNeedle = $"ConverterParameter={mode}";
            Assert.Contains(triggerNeedle, sectionBlock, StringComparison.Ordinal);
        }
    }

    [Fact]
    public void FloatModeCard_ContainsHideOnLimitCheckboxes()
    {
        var xaml = File.ReadAllText(FindSourceFile("VrcTwitchOscBridge", "AvatarSetsManagerWindow.xaml"));
        var headerIndex = xaml.IndexOf("FloatActionModeHeader", StringComparison.Ordinal);
        Assert.True(headerIndex >= 0, "Float Action Mode section should exist.");

        Assert.Contains("HideRewardWhenFloatMaxReached", xaml, StringComparison.Ordinal);
        Assert.Contains("HideRewardWhenFloatMinReached", xaml, StringComparison.Ordinal);
        Assert.Contains("UsesFloatHideOnLimit", xaml, StringComparison.Ordinal);
        Assert.Contains("Hide reward at maximum float", xaml, StringComparison.Ordinal);
        Assert.Contains("Hide reward at minimum float", xaml, StringComparison.Ordinal);
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
