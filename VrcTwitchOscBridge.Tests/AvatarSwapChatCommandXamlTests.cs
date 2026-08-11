using System;
using System.IO;
using System.Linq;
using System.Xml.Linq;
using Xunit;

namespace VrcTwitchOscBridge.Tests;

public sealed class AvatarSwapChatCommandXamlTests
{
    [Fact]
    public void InlineEditor_ChatCommandPanelExposesEditableToggleTextAndPermission()
    {
        var document = LoadInlineEditor();
        var panel = FindTriggerPanel(document, "ChatCommand", "{loc:Translate 'Chat Command'}");

        var toggle = Assert.Single(panel.Descendants(), element =>
            IsElement(element, "CheckBox")
            && AttributeValue(element, "Content") == "{loc:Translate 'Enable chat command'}");
        Assert.Equal(
            "{Binding Rule.ChatCommandEnabled, UpdateSourceTrigger=PropertyChanged}",
            AttributeValue(toggle, "IsChecked"));

        var commandTextBox = Assert.Single(panel.Descendants(), element =>
            IsElement(element, "TextBox")
            && AttributeValue(element, "Text") == "{Binding Rule.ChatCommandText, UpdateSourceTrigger=PropertyChanged}");
        Assert.Equal(
            "{Binding Rule.ChatCommandText, UpdateSourceTrigger=PropertyChanged}",
            AttributeValue(commandTextBox, "Text"));

        var permissionComboBox = Assert.Single(panel.Descendants(), element =>
            IsElement(element, "ComboBox")
            && AttributeValue(element, "SelectedItem") == "{Binding Rule.ChatCommandPermission, UpdateSourceTrigger=PropertyChanged}");
        Assert.Equal(
            "{Binding Rule.ChatCommandPermission, UpdateSourceTrigger=PropertyChanged}",
            AttributeValue(permissionComboBox, "SelectedItem"));
    }

    [Fact]
    public void InlineEditor_ChannelPointsPanelExposesOptionalChatCommandFallback()
    {
        var document = LoadInlineEditor();
        var panel = FindTriggerPanel(document, "ChannelPoints", "{loc:Translate 'Twitch Reward'}");
        var childBorders = panel.Elements()
            .Where(element => IsElement(element, "Border"))
            .ToArray();

        var rewardBorderIndex = Array.FindIndex(childBorders, border =>
            border.Descendants().Any(element =>
                IsElement(element, "TextBlock")
                && AttributeValue(element, "Text") == "{loc:Translate 'Twitch Reward'}"));
        var fallbackBorderIndex = Array.FindIndex(childBorders, border =>
            border.Descendants().Any(element =>
                IsElement(element, "TextBlock")
                && AttributeValue(element, "Text") == "{loc:Translate 'Chat Command Fallback'}"));

        Assert.True(rewardBorderIndex >= 0, "Channel Points reward configuration should remain present.");
        Assert.True(
            fallbackBorderIndex > rewardBorderIndex,
            "Chat Command Fallback should be a distinct block after reward configuration.");

        var fallbackBorder = childBorders[fallbackBorderIndex];
        var fallbackToggle = Assert.Single(fallbackBorder.Descendants(), element =>
            IsElement(element, "CheckBox")
            && AttributeValue(element, "Content") == "{loc:Translate 'Enable chat command fallback'}");
        Assert.Equal(
            "{Binding Rule.ChatCommandEnabled, UpdateSourceTrigger=PropertyChanged}",
            AttributeValue(fallbackToggle, "IsChecked"));

        var commandTextBox = Assert.Single(fallbackBorder.Descendants(), element =>
            IsElement(element, "TextBox")
            && AttributeValue(element, "Text") == "{Binding Rule.ChatCommandText, UpdateSourceTrigger=PropertyChanged}");
        Assert.Equal(
            "{Binding Rule.ChatCommandText, UpdateSourceTrigger=PropertyChanged}",
            AttributeValue(commandTextBox, "Text"));

        var permissionComboBox = Assert.Single(fallbackBorder.Descendants(), element =>
            IsElement(element, "ComboBox")
            && AttributeValue(element, "SelectedItem") == "{Binding Rule.ChatCommandPermission, UpdateSourceTrigger=PropertyChanged}");
        Assert.Equal(
            "{Binding Rule.ChatCommandPermission, UpdateSourceTrigger=PropertyChanged}",
            AttributeValue(permissionComboBox, "SelectedItem"));
    }

    [Fact]
    public void AvatarSwapManager_AdvancedTriggerHeadingUsesLocalization()
    {
        var xaml = File.ReadAllText(FindSourceFile(
            "VrcTwitchOscBridge",
            "AvatarSwapManagerWindow.xaml"));
        var document = XDocument.Parse(xaml, LoadOptions.PreserveWhitespace);

        Assert.Equal(
            2,
            document.Descendants()
                .Count(element =>
                    IsElement(element, "TextBlock")
                    && AttributeValue(element, "Text") == "{loc:Translate 'Advanced triggers (open full editor)'}"));
        Assert.DoesNotContain(
            "Text=\"Advanced triggers (open full editor)\"",
            xaml,
            StringComparison.Ordinal);
    }

    private static XDocument LoadInlineEditor()
    {
        return XDocument.Parse(
            File.ReadAllText(FindSourceFile(
                "VrcTwitchOscBridge",
                "UserControls",
                "InlineRuleEditorControl.xaml")),
            LoadOptions.PreserveWhitespace);
    }

    private static XElement FindTriggerPanel(XDocument document, string triggerType, string requiredText)
    {
        var panels = document.Descendants()
            .Where(element =>
                IsElement(element, "StackPanel")
                && element.Elements()
                    .Where(child => IsElement(child, "StackPanel.Style"))
                    .SelectMany(style => style.Descendants().Where(trigger => IsElement(trigger, "DataTrigger")))
                    .Any(trigger =>
                        AttributeValue(trigger, "Binding") == "{Binding Rule.TriggerType}"
                        && AttributeValue(trigger, "Value") == triggerType
                        && trigger.Descendants().Any(setter =>
                            IsElement(setter, "Setter")
                            && AttributeValue(setter, "Property") == "Visibility"
                            && AttributeValue(setter, "Value") == "Visible"))
                && element.Descendants().Any(descendant =>
                    IsElement(descendant, "TextBlock")
                    && AttributeValue(descendant, "Text") == requiredText))
            .ToArray();

        return Assert.Single(panels);
    }

    private static bool IsElement(XElement element, string localName) =>
        element.Name.LocalName == localName;

    private static string? AttributeValue(XElement element, string localName) =>
        element.Attributes().SingleOrDefault(attribute => attribute.Name.LocalName == localName)?.Value;

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
