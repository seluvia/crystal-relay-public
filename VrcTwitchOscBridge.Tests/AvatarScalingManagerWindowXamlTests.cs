using System;
using System.IO;
using System.Linq;
using Xunit;

namespace VrcTwitchOscBridge.Tests;

public sealed class AvatarScalingManagerWindowXamlTests
{
    [Fact]
    public void Window_UsesRightSideEditorPanel()
    {
        var xaml = File.ReadAllText(FindSourceFile("VrcTwitchOscBridge", "AvatarScalingManagerWindow.xaml"));

        Assert.Contains("SelectedCard", xaml, StringComparison.Ordinal);
        Assert.Contains("HasNoSelectedCard", xaml, StringComparison.Ordinal);
        Assert.Contains("Grid.Column=\"3\"", xaml, StringComparison.Ordinal);
        Assert.Contains("Text=\"{loc:Translate 'Editing child reward'}\"", xaml, StringComparison.Ordinal);
        Assert.DoesNotContain("Editor Coming Next", xaml, StringComparison.Ordinal);
    }

    [Fact]
    public void Window_RightSideEditorColumnIsNotBlankWhenNoCardIsSelected()
    {
        var xaml = File.ReadAllText(FindSourceFile("VrcTwitchOscBridge", "AvatarScalingManagerWindow.xaml"));
        var editorStart = xaml.IndexOf("<Border Grid.Column=\"3\"", StringComparison.Ordinal);
        Assert.True(editorStart >= 0, "Right-side editor border should exist.");
        var editorBlock = xaml[editorStart..Math.Min(xaml.Length, editorStart + 6500)];

        Assert.DoesNotContain("Visibility=\"{Binding IsEditorOpen", editorBlock, StringComparison.Ordinal);
        Assert.Contains("HasNoSelectedCard", editorBlock, StringComparison.Ordinal);
    }

    [Fact]
    public void Window_TwitchRewardsPageUsesRewardFocusedBrainstormLayout()
    {
        var xaml = File.ReadAllText(FindSourceFile("VrcTwitchOscBridge", "AvatarScalingManagerWindow.xaml"));
        var listAreaStart = xaml.IndexOf("<ScrollViewer Grid.Column=\"1\"", StringComparison.Ordinal);
        var editorStart = xaml.IndexOf("<Border Grid.Column=\"3\"", listAreaStart, StringComparison.Ordinal);
        var listArea = listAreaStart >= 0 && editorStart > listAreaStart
            ? xaml[listAreaStart..editorStart]
            : string.Empty;

        Assert.Contains("Twitch Reward Scaling", listArea, StringComparison.Ordinal);
        Assert.DoesNotContain("Reward System Controls", listArea, StringComparison.Ordinal);
        Assert.Contains("TwitchScaleSetGroups", listArea, StringComparison.Ordinal);
        Assert.Contains("UniformGrid Columns=\"2\"", listArea, StringComparison.Ordinal);
    }

    [Fact]
    public void Window_CenterPanelUsesTwoColumnLayoutForRedeemsAndPaySystems()
    {
        var xaml = File.ReadAllText(FindSourceFile("VrcTwitchOscBridge", "AvatarScalingManagerWindow.xaml"));
        var listAreaStart = xaml.IndexOf("<ScrollViewer Grid.Column=\"1\"", StringComparison.Ordinal);
        var editorStart = xaml.IndexOf("<Border Grid.Column=\"3\"", listAreaStart, StringComparison.Ordinal);
        var listArea = listAreaStart >= 0 && editorStart > listAreaStart
            ? xaml[listAreaStart..editorStart]
            : string.Empty;

        Assert.Contains("Child Scale Rewards", listArea, StringComparison.Ordinal);
        Assert.Contains("Pay System Rewards", listArea, StringComparison.Ordinal);
        Assert.Contains("UniformGrid Columns=\"2\"", listArea, StringComparison.Ordinal);
        Assert.Contains("ChannelPointColumn", listArea, StringComparison.Ordinal);
        Assert.Contains("PaySystemColumn", listArea, StringComparison.Ordinal);
        Assert.DoesNotContain("Width=\"330\"", listArea, StringComparison.Ordinal);
    }

    [Fact]
    public void Window_BrainstormLayoutStringsAreLocalizedInAllLocaleFiles()
    {
        var expectedKeys = new[]
        {
            "Twitch Reward Scaling",
            "Set up channel-point rewards that change avatar height. Reward settings are kept together here, separate from paid support, cash payments, and Power Ups.",
            "1 reward",
            "{0} rewards",
            "Pay System Rewards",
            "Supporter Growth, Cash Payments & Power Ups",
            "Advanced range is on. Crystal Relay accepts 0.01m to 10000m technically; extreme values can be uncomfortable or world-blocked.",
            "Safe range is 0.1m to 100m.",
            "VRChat world min/max will be bypassed for this redeem."
        };
        var localizationFolder = FindSourceDirectory("VrcTwitchOscBridge", "Resources", "Localization");
        var localeFiles = Directory.GetFiles(localizationFolder, "*.json");

        Assert.Equal(14, localeFiles.Length);
        foreach (var file in localeFiles)
        {
            var content = File.ReadAllText(file);
            foreach (var key in expectedKeys)
            {
                Assert.Contains($"\"{key}\"", content, StringComparison.Ordinal);
            }
        }
    }

    [Fact]
    public void Window_SourceCardsForceReadableTextBrushes()
    {
        var xaml = File.ReadAllText(FindSourceFile("VrcTwitchOscBridge", "AvatarScalingManagerWindow.xaml"));
        var templateStart = xaml.IndexOf("x:Key=\"SourceCardTemplate\"", StringComparison.Ordinal);
        Assert.True(templateStart >= 0, "SourceCardTemplate should exist.");
        var templateBlock = xaml[templateStart..Math.Min(xaml.Length, templateStart + 3000)];

        Assert.Contains("Text=\"{Binding Title}\"", templateBlock, StringComparison.Ordinal);
        Assert.Contains("Foreground=\"{DynamicResource TitleBarTextBrush}\"", templateBlock, StringComparison.Ordinal);
        Assert.Contains("Text=\"{Binding SafetySummary}\"", templateBlock, StringComparison.Ordinal);
        Assert.Contains("Foreground=\"{DynamicResource TitleBarSubTextBrush}\"", templateBlock, StringComparison.Ordinal);
    }

    [Fact]
    public void Window_CloseButtonsDoNotUseHardcodedVisibleXText()
    {
        var xaml = File.ReadAllText(FindSourceFile("VrcTwitchOscBridge", "AvatarScalingManagerWindow.xaml"));

        Assert.DoesNotContain("Content=\"X\"", xaml, StringComparison.Ordinal);
    }

    [Fact]
    public void WindowClosed_DisposesDisposableDataContext()
    {
        var codeBehind = File.ReadAllText(FindSourceFile("VrcTwitchOscBridge", "AvatarScalingManagerWindow.xaml.cs"));
        var handlerIndex = codeBehind.IndexOf("private void OnWindowClosed", StringComparison.Ordinal);
        Assert.True(handlerIndex >= 0, "OnWindowClosed should exist.");

        var handlerBlock = codeBehind.Substring(handlerIndex);
        Assert.Contains("DataContext is IDisposable", handlerBlock, StringComparison.Ordinal);
        Assert.Contains(".Dispose();", handlerBlock, StringComparison.Ordinal);
    }

    [Fact]
    public void Window_ShowsCurrentMaxHeightAllowedAndAdvancedSafety()
    {
        var xaml = File.ReadAllText(FindSourceFile("VrcTwitchOscBridge", "AvatarScalingManagerWindow.xaml"));

        Assert.Contains("Current Max Height Allowed", xaml, StringComparison.Ordinal);
        Assert.Contains("CurrentMaxHeightAllowedText", xaml, StringComparison.Ordinal);
        Assert.Contains("Open Advanced Safety", xaml, StringComparison.Ordinal);

        var safetySectionIndex = xaml.IndexOf("Safety &amp; Pairing", StringComparison.Ordinal);
        var advancedSafetyIndex = xaml.IndexOf("Open Advanced Safety", safetySectionIndex, StringComparison.Ordinal);
        Assert.True(safetySectionIndex >= 0, "Safety & Pairing editor section should exist.");
        Assert.True(advancedSafetyIndex > safetySectionIndex, "Open Advanced Safety should stay inside Safety & Pairing.");
    }

    [Fact]
    public void Window_AdvancedSafetyButtonsTogglePanelAndPanelBindsSafetyRange()
    {
        var xaml = File.ReadAllText(FindSourceFile("VrcTwitchOscBridge", "AvatarScalingManagerWindow.xaml"));
        var safetySectionIndex = xaml.IndexOf("Safety &amp; Pairing", StringComparison.Ordinal);
        var panelIndex = xaml.IndexOf("IsAdvancedSafetyOpen", safetySectionIndex, StringComparison.Ordinal);
        var panelBlock = panelIndex >= 0
            ? xaml[panelIndex..Math.Min(xaml.Length, panelIndex + 1800)]
            : string.Empty;

        Assert.True(CountOccurrences(xaml, "OpenAdvancedSafetyCommand") >= 2,
            "Both Open Advanced Safety buttons should be bound to the manager command.");
        Assert.Contains("Command=\"{Binding OpenAdvancedSafetyCommand}\"", xaml, StringComparison.Ordinal);
        Assert.Contains("Command=\"{Binding DataContext.OpenAdvancedSafetyCommand, RelativeSource={RelativeSource AncestorType=Window}}\"", xaml, StringComparison.Ordinal);
        Assert.True(safetySectionIndex >= 0, "Safety & Pairing editor section should exist.");
        Assert.True(panelIndex > safetySectionIndex, "Advanced safety panel should stay inside Safety & Pairing.");
        Assert.Contains("Settings.AvatarScaleSafety.CurrentMinimumHeightMeters", panelBlock, StringComparison.Ordinal);
        Assert.Contains("Settings.AvatarScaleSafety.CurrentMaximumHeightMeters", panelBlock, StringComparison.Ordinal);
    }

    [Fact]
    public void Window_TopAdvancedSafetyPanelIsVisibleFromGlobalSafetyCard()
    {
        var xaml = File.ReadAllText(FindSourceFile("VrcTwitchOscBridge", "AvatarScalingManagerWindow.xaml"));
        var topSafetyIndex = xaml.IndexOf("Global Safety Rule", StringComparison.Ordinal);
        var nextSectionIndex = xaml.IndexOf("Master Unlock Reward", topSafetyIndex, StringComparison.Ordinal);
        var topSafetyBlock = topSafetyIndex >= 0 && nextSectionIndex > topSafetyIndex
            ? xaml[topSafetyIndex..nextSectionIndex]
            : string.Empty;

        Assert.True(topSafetyIndex >= 0, "Global Safety Rule card should exist.");
        Assert.Contains("OpenAdvancedSafetyCommand", topSafetyBlock, StringComparison.Ordinal);
        Assert.Contains("IsAdvancedSafetyOpen", topSafetyBlock, StringComparison.Ordinal);
        Assert.Contains("Settings.AvatarScaleSafety.CurrentMinimumHeightMeters", topSafetyBlock, StringComparison.Ordinal);
        Assert.Contains("Settings.AvatarScaleSafety.CurrentMaximumHeightMeters", topSafetyBlock, StringComparison.Ordinal);
    }

    [Fact]
    public void Window_ListAreaExposesAlwaysReachableScaleCreateActions()
    {
        var xaml = File.ReadAllText(FindSourceFile("VrcTwitchOscBridge", "AvatarScalingManagerWindow.xaml"));
        var listAreaStart = xaml.IndexOf("<ScrollViewer Grid.Column=\"1\"", StringComparison.Ordinal);
        var editorStart = xaml.IndexOf("<Border Grid.Column=\"3\"", listAreaStart, StringComparison.Ordinal);
        var listArea = listAreaStart >= 0 && editorStart > listAreaStart
            ? xaml[listAreaStart..editorStart]
            : string.Empty;

        Assert.True(listAreaStart >= 0, "Central list area should exist.");
        Assert.Contains("Content=\"{loc:Translate 'Add Scale Set'}\"", listArea, StringComparison.Ordinal);
        Assert.Contains("Command=\"{Binding AddAvatarScaleSetCommand}\"", listArea, StringComparison.Ordinal);
        Assert.Contains("Content=\"{loc:Translate 'Add Scale Redeem'}\"", listArea, StringComparison.Ordinal);
        Assert.Contains("Command=\"{Binding AddAvatarScaleRuleCommand}\"", listArea, StringComparison.Ordinal);
    }

    [Fact]
    public void Window_EditorIncludesMasterRewardFields()
    {
        var xaml = File.ReadAllText(FindSourceFile("VrcTwitchOscBridge", "AvatarScalingManagerWindow.xaml"));
        var masterTemplateIndex = xaml.IndexOf("DataType=\"{x:Type models:AvatarScaleMasterRewardSettings}\"", StringComparison.Ordinal);
        var masterTemplateBlock = ExtractTemplateBlock(xaml, masterTemplateIndex);

        Assert.Contains("SelectedCard.MasterReward", xaml, StringComparison.Ordinal);
        Assert.True(masterTemplateIndex >= 0, "Master reward editor template should exist.");
        Assert.Contains("IsEnabled", masterTemplateBlock, StringComparison.Ordinal);
        Assert.DoesNotContain("RewardSyncMode", masterTemplateBlock, StringComparison.Ordinal);
        Assert.DoesNotContain("Existing Twitch Reward", masterTemplateBlock, StringComparison.Ordinal);
        Assert.DoesNotContain("SelectedValue=\"{Binding RewardId, UpdateSourceTrigger=PropertyChanged}\"", masterTemplateBlock, StringComparison.Ordinal);
        Assert.DoesNotContain("Text=\"{loc:Translate 'Reward ID'}\"", masterTemplateBlock, StringComparison.Ordinal);
        Assert.DoesNotContain("<TextBox Text=\"{Binding RewardId", masterTemplateBlock, StringComparison.Ordinal);
        Assert.Contains("RewardTitle", masterTemplateBlock, StringComparison.Ordinal);
        Assert.DoesNotContain("RewardDescription", masterTemplateBlock, StringComparison.Ordinal);
        Assert.Contains("RewardCost", masterTemplateBlock, StringComparison.Ordinal);
        Assert.Contains("UnlockDurationSeconds", masterTemplateBlock, StringComparison.Ordinal);
        Assert.Contains("CooldownSeconds", masterTemplateBlock, StringComparison.Ordinal);
        Assert.Contains("FreeChildRewardSlotsWhenLocked", masterTemplateBlock, StringComparison.Ordinal);
        Assert.DoesNotContain("DeleteMasterRewardWhenInactive", masterTemplateBlock, StringComparison.Ordinal);
        Assert.DoesNotContain("PreventAvatarChangesDuringActiveScaling", masterTemplateBlock, StringComparison.Ordinal);
    }

    [Fact]
    public void Window_MasterRewardEditorIncludesReadyAndCooldownColors()
    {
        var xaml = File.ReadAllText(FindSourceFile("VrcTwitchOscBridge", "AvatarScalingManagerWindow.xaml"));
        var templateIndex = xaml.IndexOf(
            "DataType=\"{x:Type models:AvatarScaleMasterRewardSettings}\"",
            StringComparison.Ordinal);
        var templateEnd = templateIndex >= 0
            ? xaml.IndexOf("</DataTemplate>", templateIndex, StringComparison.Ordinal)
            : -1;
        var template = templateIndex >= 0 && templateEnd > templateIndex
            ? xaml[templateIndex..templateEnd]
            : string.Empty;

        Assert.Contains("Managed Reward Colors", template, StringComparison.Ordinal);
        Assert.Contains("ManagedRewardReadyColorBrush", template, StringComparison.Ordinal);
        Assert.Contains("ManagedRewardCooldownColorBrush", template, StringComparison.Ordinal);
        Assert.Contains("Tag=\"Ready\"", template, StringComparison.Ordinal);
        Assert.Contains("Tag=\"Cooldown\"", template, StringComparison.Ordinal);
        Assert.Contains("OnPickManagedRewardColorClicked", template, StringComparison.Ordinal);
    }

    [Fact]
    public void Window_MasterUnlockerEditorOnlyShowsUnlockerChannelRewardControls()
    {
        var xaml = File.ReadAllText(FindSourceFile("VrcTwitchOscBridge", "AvatarScalingManagerWindow.xaml"));
        var masterTemplateIndex = xaml.IndexOf("DataType=\"{x:Type models:AvatarScaleMasterRewardSettings}\"", StringComparison.Ordinal);
        var masterTemplateBlock = ExtractTemplateBlock(xaml, masterTemplateIndex);

        Assert.Contains("Enable Master Reward", masterTemplateBlock, StringComparison.Ordinal);
        Assert.Contains("Reward Title", masterTemplateBlock, StringComparison.Ordinal);
        Assert.Contains("Reward Cost", masterTemplateBlock, StringComparison.Ordinal);
        Assert.Contains("Unlock Duration Seconds", masterTemplateBlock, StringComparison.Ordinal);
        Assert.Contains("Cooldown Seconds", masterTemplateBlock, StringComparison.Ordinal);
        Assert.Contains("Free child reward slots while locked", masterTemplateBlock, StringComparison.Ordinal);
        Assert.DoesNotContain("Reward Source", masterTemplateBlock, StringComparison.Ordinal);
        Assert.DoesNotContain("Existing Twitch Reward", masterTemplateBlock, StringComparison.Ordinal);
        Assert.DoesNotContain("Reward Description", masterTemplateBlock, StringComparison.Ordinal);
        Assert.DoesNotContain("Delete master reward when inactive", masterTemplateBlock, StringComparison.Ordinal);
        Assert.DoesNotContain("Prevent avatar-change rewards while scaling is active", masterTemplateBlock, StringComparison.Ordinal);
    }

    [Fact]
    public void Window_EditorIncludesTriggerSpecificFields()
    {
        var xaml = File.ReadAllText(FindSourceFile("VrcTwitchOscBridge", "AvatarScalingManagerWindow.xaml"));
        var triggerSectionIndex = xaml.IndexOf("Trigger Type", StringComparison.Ordinal);
        var heightSectionIndex = xaml.IndexOf("Height Change", triggerSectionIndex, StringComparison.Ordinal);
        var triggerBlock = triggerSectionIndex >= 0 && heightSectionIndex > triggerSectionIndex
            ? xaml[triggerSectionIndex..heightSectionIndex]
            : string.Empty;

        Assert.Contains("UsesChatCommand", triggerBlock, StringComparison.Ordinal);
        Assert.Contains("CommandText", triggerBlock, StringComparison.Ordinal);
        Assert.Contains("ChatCommandPermissionOptions", triggerBlock, StringComparison.Ordinal);
        Assert.Contains("ChatCommandPermission", triggerBlock, StringComparison.Ordinal);
        Assert.Contains("UsesBits", triggerBlock, StringComparison.Ordinal);
        Assert.Contains("MinimumBits", triggerBlock, StringComparison.Ordinal);
        Assert.Contains("MaximumBits", triggerBlock, StringComparison.Ordinal);
        Assert.Contains("UsesSubscription", triggerBlock, StringComparison.Ordinal);
        Assert.Contains("AvatarScaleSubscriptionTierOptions", triggerBlock, StringComparison.Ordinal);
        Assert.Contains("SubscriptionTier", triggerBlock, StringComparison.Ordinal);
        Assert.Contains("MinimumMonths", triggerBlock, StringComparison.Ordinal);
        Assert.Contains("MaximumMonths", triggerBlock, StringComparison.Ordinal);
        Assert.Contains("UsesFollow", triggerBlock, StringComparison.Ordinal);
    }

    [Fact]
    public void Window_CheckBoxesUseThemedForeground()
    {
        var xaml = File.ReadAllText(FindSourceFile("VrcTwitchOscBridge", "AvatarScalingManagerWindow.xaml"));
        var styleIndex = xaml.IndexOf("<Style TargetType=\"CheckBox\">", StringComparison.Ordinal);
        var styleEnd = styleIndex >= 0
            ? xaml.IndexOf("</Style>", styleIndex, StringComparison.Ordinal)
            : -1;
        var styleBlock = styleIndex >= 0 && styleEnd > styleIndex
            ? xaml[styleIndex..styleEnd]
            : string.Empty;

        Assert.True(styleIndex >= 0, "CheckBox controls should have a themed style in this window.");
        Assert.Contains("<Setter Property=\"Foreground\" Value=\"{DynamicResource TitleBarTextBrush}\"", styleBlock, StringComparison.Ordinal);
        Assert.Contains("<Setter Property=\"FontFamily\" Value=\"{DynamicResource BodyFontFamily}\"", styleBlock, StringComparison.Ordinal);
    }

    [Fact]
    public void Window_ComboBoxesUseCustomPopupTemplateWithReadableDropdownText()
    {
        var xaml = File.ReadAllText(FindSourceFile("VrcTwitchOscBridge", "AvatarScalingManagerWindow.xaml"));
        var comboStyleIndex = xaml.IndexOf("<Style TargetType=\"ComboBox\">", StringComparison.Ordinal);
        var comboItemStyleIndex = xaml.IndexOf("<Style TargetType=\"ComboBoxItem\">", StringComparison.Ordinal);
        var comboStyleBlock = comboStyleIndex >= 0 && comboItemStyleIndex > comboStyleIndex
            ? xaml[comboStyleIndex..comboItemStyleIndex]
            : string.Empty;
        var comboItemBlock = comboItemStyleIndex >= 0
            ? xaml[comboItemStyleIndex..Math.Min(xaml.Length, comboItemStyleIndex + 2500)]
            : string.Empty;

        Assert.True(comboStyleIndex >= 0, "ComboBox controls should use a custom style in this window.");
        Assert.Contains("PART_Popup", comboStyleBlock, StringComparison.Ordinal);
        Assert.Contains("ComboBoxToggleButtonStyle", comboStyleBlock, StringComparison.Ordinal);
        Assert.Contains("ComboPopupScrollBarStyle", comboStyleBlock, StringComparison.Ordinal);
        Assert.Contains("TextElement.Foreground=\"{DynamicResource TitleBarTextBrush}\"", comboStyleBlock, StringComparison.Ordinal);
        Assert.Contains("Background=\"{DynamicResource InputBrush}\"", comboStyleBlock, StringComparison.Ordinal);
        Assert.Contains("<ControlTemplate TargetType=\"ComboBoxItem\">", comboItemBlock, StringComparison.Ordinal);
        Assert.Contains("ContentPresenter TextElement.Foreground=\"{DynamicResource TitleBarTextBrush}\"", comboItemBlock, StringComparison.Ordinal);
    }

    [Fact]
    public void Window_ScrollViewersUseCustomThemedScrollBars()
    {
        var xaml = File.ReadAllText(FindSourceFile("VrcTwitchOscBridge", "AvatarScalingManagerWindow.xaml"));

        Assert.Contains("x:Key=\"ScrollBarThumbStyle\"", xaml, StringComparison.Ordinal);
        Assert.Contains("x:Key=\"ScrollBarTrackButtonStyle\"", xaml, StringComparison.Ordinal);
        Assert.Contains("x:Key=\"VerticalScrollBarTemplate\"", xaml, StringComparison.Ordinal);
        Assert.Contains("x:Key=\"HorizontalScrollBarTemplate\"", xaml, StringComparison.Ordinal);
        Assert.Contains("<Style TargetType=\"ScrollViewer\">", xaml, StringComparison.Ordinal);
        Assert.Contains("PART_VerticalScrollBar", xaml, StringComparison.Ordinal);
        Assert.Contains("PART_HorizontalScrollBar", xaml, StringComparison.Ordinal);
    }

    [Fact]
    public void Window_CheckBoxesUseCustomThemedTemplate()
    {
        var xaml = File.ReadAllText(FindSourceFile("VrcTwitchOscBridge", "AvatarScalingManagerWindow.xaml"));
        var styleIndex = xaml.IndexOf("<Style TargetType=\"CheckBox\">", StringComparison.Ordinal);
        var controlTemplateIndex = styleIndex >= 0
            ? xaml.IndexOf("<ControlTemplate TargetType=\"CheckBox\">", styleIndex, StringComparison.Ordinal)
            : -1;
        var styleEnd = controlTemplateIndex >= 0
            ? xaml.IndexOf("</Style>", xaml.IndexOf("</ControlTemplate>", controlTemplateIndex, StringComparison.Ordinal), StringComparison.Ordinal)
            : -1;
        var styleBlock = styleIndex >= 0 && styleEnd > styleIndex
            ? xaml[styleIndex..styleEnd]
            : string.Empty;

        Assert.Contains("<ControlTemplate TargetType=\"CheckBox\">", styleBlock, StringComparison.Ordinal);
        Assert.Contains("CheckChrome", styleBlock, StringComparison.Ordinal);
        Assert.Contains("CheckMark", styleBlock, StringComparison.Ordinal);
        Assert.Contains("RuleCardHoverBrush", styleBlock, StringComparison.Ordinal);
    }

    [Fact]
    public void Window_EditorIncludesTask8Sections()
    {
        var xaml = File.ReadAllText(FindSourceFile("VrcTwitchOscBridge", "AvatarScalingManagerWindow.xaml"));
        var editorIndex = xaml.IndexOf("Grid.Column=\"3\"", StringComparison.Ordinal);

        Assert.True(editorIndex >= 0, "Right-side editor should stay in Grid.Column=\"3\".");
        Assert.Contains("Twitch Reward", xaml[editorIndex..], StringComparison.Ordinal);
        Assert.Contains("Height Change", xaml[editorIndex..], StringComparison.Ordinal);
        Assert.Contains("Timer &amp; Return", xaml[editorIndex..], StringComparison.Ordinal);
        Assert.Contains("Safety &amp; Pairing", xaml[editorIndex..], StringComparison.Ordinal);
    }

    [Fact]
    public void Window_HeightEditorExposesAdvancedScaleRange()
    {
        var xaml = File.ReadAllText(FindSourceFile("VrcTwitchOscBridge", "AvatarScalingManagerWindow.xaml"));
        var heightSectionIndex = xaml.IndexOf("Text=\"{loc:Translate 'Height Change'}\"", StringComparison.Ordinal);
        var timerSectionIndex = xaml.IndexOf("Text=\"{loc:Translate 'Timer &amp; Return'}\"", heightSectionIndex, StringComparison.Ordinal);
        var heightSection = heightSectionIndex >= 0 && timerSectionIndex > heightSectionIndex
            ? xaml[heightSectionIndex..timerSectionIndex]
            : string.Empty;

        Assert.True(heightSectionIndex >= 0, "Height Change editor section should exist.");
        Assert.True(timerSectionIndex > heightSectionIndex, "Height Change editor section should end before Timer & Return.");
        Assert.Contains("Unlock advanced VRChat scale range (0.01m - 10000m)", heightSection, StringComparison.Ordinal);
        Assert.Contains("IsChecked=\"{Binding AdvancedRangeEnabled, UpdateSourceTrigger=PropertyChanged}\"", heightSection, StringComparison.Ordinal);
        Assert.Contains("Bypass VRChat world min/max", heightSection, StringComparison.Ordinal);
        Assert.Contains("IsChecked=\"{Binding BypassVrChatScaleLimits, UpdateSourceTrigger=PropertyChanged}\"", heightSection, StringComparison.Ordinal);
        Assert.Contains("ScaleRangeHelpText", heightSection, StringComparison.Ordinal);
    }

    [Fact]
    public void Window_WiresAvatarScaleEditorCodeBehindHandlers()
    {
        var xaml = File.ReadAllText(FindSourceFile("VrcTwitchOscBridge", "AvatarScalingManagerWindow.xaml"));
        var codeBehind = File.ReadAllText(FindSourceFile("VrcTwitchOscBridge", "AvatarScalingManagerWindow.xaml.cs"));

        Assert.Contains("ScaleActionModeButton_Click", xaml, StringComparison.Ordinal);
        Assert.Contains("ScaleActionMultOpButton_Click", xaml, StringComparison.Ordinal);
        Assert.Contains("ScaleActionRelHeightOpButton_Click", xaml, StringComparison.Ordinal);
        Assert.Contains("OnPickManagedRewardColorClicked", xaml, StringComparison.Ordinal);
        Assert.Contains("OnAddSupporterGrowthBitRangeClicked", xaml, StringComparison.Ordinal);
        Assert.Contains("OnRemoveSupporterGrowthBitRangeClicked", xaml, StringComparison.Ordinal);
        Assert.Contains("private void ScaleActionModeButton_Click", codeBehind, StringComparison.Ordinal);
        Assert.Contains("private void ScaleActionMultOpButton_Click", codeBehind, StringComparison.Ordinal);
        Assert.Contains("private void ScaleActionRelHeightOpButton_Click", codeBehind, StringComparison.Ordinal);
        Assert.Contains("private void OnPickManagedRewardColorClicked", codeBehind, StringComparison.Ordinal);
        Assert.Contains("private void OnAddSupporterGrowthBitRangeClicked", codeBehind, StringComparison.Ordinal);
        Assert.Contains("private void OnRemoveSupporterGrowthBitRangeClicked", codeBehind, StringComparison.Ordinal);
        Assert.Contains("Vm.SelectedAvatarScaleRule", codeBehind, StringComparison.Ordinal);
        Assert.Contains("Vm.SelectedCard?.MasterReward", codeBehind, StringComparison.Ordinal);
        Assert.Contains("masterReward.ManagedRewardCooldownColor", codeBehind, StringComparison.Ordinal);
        Assert.Contains("masterReward.ManagedRewardReadyColor", codeBehind, StringComparison.Ordinal);
    }

    [Fact]
    public void Window_UsesReadableInputThemeBrushes()
    {
        var xaml = File.ReadAllText(FindSourceFile("VrcTwitchOscBridge", "AvatarScalingManagerWindow.xaml"));
        var textBoxStyleIndex = xaml.IndexOf("<Style TargetType=\"TextBox\">", StringComparison.Ordinal);
        var comboBoxStyleIndex = xaml.IndexOf("<Style TargetType=\"ComboBox\">", StringComparison.Ordinal);
        var textBoxStyleEnd = textBoxStyleIndex >= 0
            ? xaml.IndexOf("</Style>", textBoxStyleIndex, StringComparison.Ordinal)
            : -1;
        var comboBoxStyleEnd = comboBoxStyleIndex >= 0
            ? xaml.IndexOf("</Style>", comboBoxStyleIndex, StringComparison.Ordinal)
            : -1;
        var textBoxStyleBlock = textBoxStyleIndex >= 0 && textBoxStyleEnd > textBoxStyleIndex
            ? xaml[textBoxStyleIndex..textBoxStyleEnd]
            : string.Empty;
        var comboBoxStyleBlock = comboBoxStyleIndex >= 0 && comboBoxStyleEnd > comboBoxStyleIndex
            ? xaml[comboBoxStyleIndex..comboBoxStyleEnd]
            : string.Empty;

        Assert.True(textBoxStyleIndex >= 0, "TextBox inputs should have a themed style.");
        Assert.True(comboBoxStyleIndex >= 0, "ComboBox inputs should have a themed style.");
        Assert.Contains("<Setter Property=\"Background\" Value=\"{DynamicResource InputBrush}\"", textBoxStyleBlock, StringComparison.Ordinal);
        Assert.Contains("<Setter Property=\"Foreground\" Value=\"{DynamicResource TitleBarTextBrush}\"", textBoxStyleBlock, StringComparison.Ordinal);
        Assert.Contains("<Setter Property=\"CaretBrush\" Value=\"{DynamicResource TitleBarTextBrush}\"", textBoxStyleBlock, StringComparison.Ordinal);
        Assert.Contains("<Setter Property=\"Background\" Value=\"{DynamicResource InputBrush}\"", comboBoxStyleBlock, StringComparison.Ordinal);
        Assert.Contains("<Setter Property=\"Foreground\" Value=\"{DynamicResource TitleBarTextBrush}\"", comboBoxStyleBlock, StringComparison.Ordinal);
        Assert.DoesNotContain("<Setter Property=\"Background\" Value=\"{DynamicResource ComboSurfaceBrush}\"", textBoxStyleBlock, StringComparison.Ordinal);
        Assert.DoesNotContain("<Setter Property=\"Foreground\" Value=\"{DynamicResource ComboTextBrush}\"", textBoxStyleBlock, StringComparison.Ordinal);
        Assert.DoesNotContain("Foreground=\"Black\"", xaml, StringComparison.Ordinal);
        Assert.DoesNotContain("Foreground=\"#000000\"", xaml, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("Background=\"#222222\"", xaml, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void Window_ManagedRewardEditorsDoNotAskForRawRewardId()
    {
        var xaml = File.ReadAllText(FindSourceFile("VrcTwitchOscBridge", "AvatarScalingManagerWindow.xaml"));
        var editorIndex = xaml.IndexOf("Grid.Column=\"3\"", StringComparison.Ordinal);
        var editorBlock = editorIndex >= 0 ? xaml[editorIndex..] : string.Empty;

        Assert.DoesNotContain("Text=\"{loc:Translate 'Reward ID'}\"", editorBlock, StringComparison.Ordinal);
        Assert.DoesNotContain("<TextBox Text=\"{Binding RewardId", editorBlock, StringComparison.Ordinal);
        Assert.Contains("Existing Twitch Reward", editorBlock, StringComparison.Ordinal);
        Assert.Contains("SelectedValue=\"{Binding RewardId, UpdateSourceTrigger=PropertyChanged}\"", editorBlock, StringComparison.Ordinal);
    }

    [Fact]
    public void Window_MajorTextUsesDarkPanelSafeThemeBrushes()
    {
        var xaml = File.ReadAllText(FindSourceFile("VrcTwitchOscBridge", "AvatarScalingManagerWindow.xaml"));
        var textBlockStyleIndex = xaml.IndexOf("<Style TargetType=\"TextBlock\">", StringComparison.Ordinal);
        var textBlockStyleEnd = textBlockStyleIndex >= 0
            ? xaml.IndexOf("</Style>", textBlockStyleIndex, StringComparison.Ordinal)
            : -1;
        var textBlockStyleBlock = textBlockStyleIndex >= 0 && textBlockStyleEnd > textBlockStyleIndex
            ? xaml[textBlockStyleIndex..textBlockStyleEnd]
            : string.Empty;
        var masterTemplateIndex = xaml.IndexOf("DataType=\"{x:Type models:AvatarScaleMasterRewardSettings}\"", StringComparison.Ordinal);
        var masterTemplateBlock = ExtractTemplateBlock(xaml, masterTemplateIndex);

        Assert.Contains("<Setter Property=\"Foreground\" Value=\"{DynamicResource TitleBarTextBrush}\"", textBlockStyleBlock, StringComparison.Ordinal);
        Assert.Contains("Foreground=\"{DynamicResource TitleBarSubTextBrush}\"", masterTemplateBlock, StringComparison.Ordinal);
        Assert.DoesNotContain("Foreground=\"{DynamicResource MutedBrush}\"", masterTemplateBlock, StringComparison.Ordinal);
    }

    [Fact]
    public void Window_LinkedScaleRewardHidesManagedRewardFields()
    {
        var xaml = File.ReadAllText(FindSourceFile("VrcTwitchOscBridge", "AvatarScalingManagerWindow.xaml"));
        var rewardSourceIndex = xaml.IndexOf("Text=\"{loc:Translate 'Reward Source'}\"", StringComparison.Ordinal);
        var heightChangeIndex = xaml.IndexOf("Text=\"{loc:Translate 'Height Change'}\"", rewardSourceIndex, StringComparison.Ordinal);
        var rewardEditorBlock = rewardSourceIndex >= 0 && heightChangeIndex > rewardSourceIndex
            ? xaml[rewardSourceIndex..heightChangeIndex]
            : string.Empty;

        Assert.Contains("Existing Twitch Reward", rewardEditorBlock, StringComparison.Ordinal);
        Assert.Contains("UsesLinkedExistingReward", rewardEditorBlock, StringComparison.Ordinal);
        Assert.Contains("Text=\"{loc:Translate 'Reward Name'}\"", rewardEditorBlock, StringComparison.Ordinal);
        Assert.Contains("Text=\"{loc:Translate 'Managed Reward Colors'}\"", rewardEditorBlock, StringComparison.Ordinal);
        Assert.True(CountOccurrences(rewardEditorBlock, "<DataTrigger Binding=\"{Binding UsesLinkedExistingReward}\" Value=\"True\">") >= 3,
            "Managed name/cost, description, and colors should be hidden for linked existing rewards.");
    }

    [Fact]
    public void Window_SourceNavigationIncludesSupportedSourceViews()
    {
        var xaml = File.ReadAllText(FindSourceFile("VrcTwitchOscBridge", "AvatarScalingManagerWindow.xaml"));

        Assert.Contains("Twitch Rewards", xaml, StringComparison.Ordinal);
        Assert.Contains("Supporter Growth", xaml, StringComparison.Ordinal);
        Assert.Contains("Cash Payments", xaml, StringComparison.Ordinal);
        Assert.Contains("Power Ups", xaml, StringComparison.Ordinal);
        Assert.Contains("All Sources", xaml, StringComparison.Ordinal);
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

    private static string ExtractTemplateBlock(string xaml, int templateIndex)
    {
        if (templateIndex < 0)
        {
            return string.Empty;
        }

        var templateEnd = xaml.IndexOf("</DataTemplate>", templateIndex, StringComparison.Ordinal);
        return templateEnd > templateIndex
            ? xaml[templateIndex..(templateEnd + "</DataTemplate>".Length)]
            : string.Empty;
    }

    private static string FindSourceDirectory(params string[] relativeParts)
    {
        var current = AppContext.BaseDirectory;
        while (!string.IsNullOrWhiteSpace(current))
        {
            var candidate = Path.Combine(new[] { current }.Concat(relativeParts).ToArray());
            if (Directory.Exists(candidate))
            {
                return candidate;
            }

            current = Directory.GetParent(current)?.FullName ?? string.Empty;
        }

        throw new DirectoryNotFoundException($"Could not find source directory {Path.Combine(relativeParts)}.");
    }

    private static int CountOccurrences(string value, string substring)
    {
        var count = 0;
        var index = 0;
        while ((index = value.IndexOf(substring, index, StringComparison.Ordinal)) >= 0)
        {
            count++;
            index += substring.Length;
        }

        return count;
    }

    [Fact]
    public void Window_EachPaySystemSectionHasDedicatedAddButton()
    {
        var xaml = File.ReadAllText(FindSourceFile("VrcTwitchOscBridge", "AvatarScalingManagerWindow.xaml"));
        var listAreaStart = xaml.IndexOf("<ScrollViewer Grid.Column=\"1\"", StringComparison.Ordinal);
        var editorStart = xaml.IndexOf("<Border Grid.Column=\"3\"", listAreaStart, StringComparison.Ordinal);
        var listArea = listAreaStart >= 0 && editorStart > listAreaStart
            ? xaml[listAreaStart..editorStart]
            : string.Empty;

        Assert.Contains("Add Supporter Growth", listArea, StringComparison.Ordinal);
        Assert.Contains("AddRewardGrowthCommand", listArea, StringComparison.Ordinal);
        Assert.Contains("Add Cash Payment", listArea, StringComparison.Ordinal);
        Assert.Contains("AddAvatarScalingCashPaymentRuleCommand", listArea, StringComparison.Ordinal);
        Assert.Contains("Add Power Up", listArea, StringComparison.Ordinal);
        Assert.Contains("AddAvatarScalingPowerUpRuleCommand", listArea, StringComparison.Ordinal);
    }

    [Fact]
    public void Window_PaySystemHeaderDoesNotHaveSharedAddRewardGrowthButton()
    {
        var xaml = File.ReadAllText(FindSourceFile("VrcTwitchOscBridge", "AvatarScalingManagerWindow.xaml"));
        var listAreaStart = xaml.IndexOf("<ScrollViewer Grid.Column=\"1\"", StringComparison.Ordinal);
        var editorStart = xaml.IndexOf("<Border Grid.Column=\"3\"", listAreaStart, StringComparison.Ordinal);
        var listArea = listAreaStart >= 0 && editorStart > listAreaStart
            ? xaml[listAreaStart..editorStart]
            : string.Empty;

        var paySystemStart = listArea.IndexOf("Pay System Rewards", StringComparison.Ordinal);
        Assert.True(paySystemStart >= 0, "Pay System Rewards header should exist.");
        var paySystemBlock = listArea[paySystemStart..Math.Min(listArea.Length, paySystemStart + 400)];

        Assert.DoesNotContain("Add Reward Growth", paySystemBlock, StringComparison.Ordinal);
        Assert.DoesNotContain("AddRewardGrowthCommand", paySystemBlock, StringComparison.Ordinal);
    }

    [Fact]
    public void Window_DedicatedAddButtonStringsAreLocalizedInAllLocaleFiles()
    {
        var expectedKeys = new[]
        {
            "Add Supporter Growth",
            "Add Cash Payment"
        };
        var localizationFolder = FindSourceDirectory("VrcTwitchOscBridge", "Resources", "Localization");
        var localeFiles = Directory.GetFiles(localizationFolder, "*.json");

        Assert.Equal(14, localeFiles.Length);
        foreach (var file in localeFiles)
        {
            var content = File.ReadAllText(file);
            foreach (var key in expectedKeys)
            {
                Assert.Contains($"\"{key}\"", content, StringComparison.Ordinal);
            }
        }
    }
}
