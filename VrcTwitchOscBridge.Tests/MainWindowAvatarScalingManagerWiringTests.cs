using System;
using System.IO;
using System.Linq;
using System.Text.RegularExpressions;
using Xunit;

namespace VrcTwitchOscBridge.Tests;

public sealed class MainWindowAvatarScalingManagerWiringTests
{
    [Fact]
    public void LoadingStarField_FormatsCrystalGeometryWithInvariantCulture()
    {
        var source = File.ReadAllText(FindSourceFile("VrcTwitchOscBridge", "MainWindow.xaml.cs"));
        var starFieldBody = GetMethodBody(source, "private void CreateStarField()");

        Assert.Contains("Geometry.Parse(FormattableString.Invariant($\"", starFieldBody, StringComparison.Ordinal);
    }

    [Fact]
    public void AvatarScalingLibraryButton_OpensManagerCommandWithoutInlineSelectionHighlight()
    {
        var xaml = File.ReadAllText(FindSourceFile("VrcTwitchOscBridge", "MainWindow.xaml"));
        var buttonBlock = GetButtonBlock(xaml, "Content=\"{loc:Translate 'Avatar Scaling'}\"");

        Assert.Contains("Command=\"{Binding OpenAvatarScalingManagerCommand}\"", buttonBlock, StringComparison.Ordinal);
        Assert.DoesNotContain("Command=\"{Binding ShowAvatarScalingCommand}\"", buttonBlock, StringComparison.Ordinal);
        Assert.DoesNotContain("IsViewingAvatarScaling", buttonBlock, StringComparison.Ordinal);
    }

    [Fact]
    public void MainWindowViewModel_OpenAvatarScalingManagerCommand_FocusesExistingVisibleWindowAndCreatesManager()
    {
        var source = File.ReadAllText(FindSourceFile("VrcTwitchOscBridge", "ViewModels", "MainWindowViewModel.cs"));
        var constructorBody = GetMethodBody(source, "public MainWindowViewModel()");
        var openMethodBody = NormalizeWhitespace(GetMethodBody(source, "private void OpenAvatarScalingManager()"));

        Assert.Contains("private AvatarScalingManagerWindow? _avatarScalingManagerWindow;", source, StringComparison.Ordinal);
        Assert.Contains("public RelayCommand OpenAvatarScalingManagerCommand { get; }", source, StringComparison.Ordinal);
        Assert.Contains("OpenAvatarScalingManagerCommand = new RelayCommand(OpenAvatarScalingManager);", constructorBody, StringComparison.Ordinal);
        Assert.Contains("if (_avatarScalingManagerWindow is { IsVisible: true }) { _avatarScalingManagerWindow.Activate(); return; }", openMethodBody, StringComparison.Ordinal);
        Assert.Contains("new AvatarScalingManagerViewModel(Settings, this)", openMethodBody, StringComparison.Ordinal);
        Assert.Contains("new AvatarScalingManagerWindow(managerVm)", openMethodBody, StringComparison.Ordinal);
        Assert.Contains("Owner = Application.Current?.MainWindow", openMethodBody, StringComparison.Ordinal);
        Assert.Contains("_avatarScalingManagerWindow.Closed += (_, _) => _avatarScalingManagerWindow = null;", openMethodBody, StringComparison.Ordinal);
        Assert.Contains("_avatarScalingManagerWindow.Show();", openMethodBody, StringComparison.Ordinal);
    }

    [Fact]
    public void MainWindowViewModel_AvatarScaleLockoutPickerGuards_DoNotRequireInlineAvatarScalingView()
    {
        var source = File.ReadAllText(FindSourceFile("VrcTwitchOscBridge", "ViewModels", "MainWindowViewModel.cs"));
        var availableOptionsBody = GetMethodBody(source, "private IReadOnlyList<TriggerRuleReferenceOption> BuildAvailableAvatarScaleRuleLockoutOptions()");
        var configuredOptionsBody = GetMethodBody(source, "private IReadOnlyList<TriggerRuleReferenceOption> BuildConfiguredAvatarScaleRuleLockoutOptions()");
        var canOpenBody = GetMethodBody(source, "private bool CanOpenAvatarScaleRuleLockoutPicker()");

        Assert.DoesNotContain("IsViewingAvatarScaling", availableOptionsBody, StringComparison.Ordinal);
        Assert.DoesNotContain("IsViewingAvatarScaling", configuredOptionsBody, StringComparison.Ordinal);
        Assert.DoesNotContain("IsViewingAvatarScaling", canOpenBody, StringComparison.Ordinal);
        Assert.Contains("SelectedAvatarScaleRule is not null", canOpenBody, StringComparison.Ordinal);
        Assert.Contains("BuildAvailableAvatarScaleRuleLockoutOptions().Count", canOpenBody, StringComparison.Ordinal);
    }

    [Fact]
    public void MainWindowXaml_RemovesObsoleteInlineAvatarScaleSetsList()
    {
        var xaml = File.ReadAllText(FindSourceFile("VrcTwitchOscBridge", "MainWindow.xaml"));

        Assert.DoesNotContain("ItemsSource=\"{Binding AvatarScaleSets}\"", xaml, StringComparison.Ordinal);
        Assert.DoesNotContain("SelectedItem=\"{Binding SelectedAvatarScaleSet}\"", xaml, StringComparison.Ordinal);
        Assert.DoesNotContain("ItemTemplate=\"{StaticResource AvatarScaleSetListItemTemplate}\"", xaml, StringComparison.Ordinal);
        Assert.DoesNotContain("x:Key=\"AvatarScaleSetListItemTemplate\"", xaml, StringComparison.Ordinal);
    }

    [Fact]
    public void MainWindowXaml_RemovesInlineAvatarScalingWorkspaceScope()
    {
        var xaml = File.ReadAllText(FindSourceFile("VrcTwitchOscBridge", "MainWindow.xaml"));

        Assert.DoesNotContain("IsViewingAvatarScaling", xaml, StringComparison.Ordinal);
        Assert.DoesNotContain("Master Reward Redeem", xaml, StringComparison.Ordinal);
        Assert.DoesNotContain("Scale Set Setup", xaml, StringComparison.Ordinal);
        Assert.DoesNotContain("Scale Redeems In This Set", xaml, StringComparison.Ordinal);

        var sharedPowerUpScaleEditor = GetElementBlock(
            xaml,
            "Content=\"{Binding SelectedAvatarScaleRule}\"",
            "<ContentControl",
            "</ContentControl>");
        Assert.Contains("Binding=\"{Binding IsViewingPowerUps}\"", sharedPowerUpScaleEditor, StringComparison.Ordinal);
        Assert.DoesNotContain("Binding=\"{Binding IsViewingAvatarScaling}\"", sharedPowerUpScaleEditor, StringComparison.Ordinal);
        Assert.Contains("Scale Redeem Setup", sharedPowerUpScaleEditor, StringComparison.Ordinal);
        Assert.Contains("Scale Action", sharedPowerUpScaleEditor, StringComparison.Ordinal);
    }

    [Fact]
    public void MainWindowXaml_RemovesOrphanedInlineAvatarScaleRuleListTemplate()
    {
        var xaml = File.ReadAllText(FindSourceFile("VrcTwitchOscBridge", "MainWindow.xaml"));

        Assert.DoesNotContain("x:Key=\"AvatarScaleRuleListItemTemplate\"", xaml, StringComparison.Ordinal);
        Assert.DoesNotContain("ItemTemplate=\"{StaticResource AvatarScaleRuleListItemTemplate}\"", xaml, StringComparison.Ordinal);
    }

    [Fact]
    public void MainWindowXaml_PreservesCashPaymentAvatarScalingActionEditor()
    {
        var xaml = File.ReadAllText(FindSourceFile("VrcTwitchOscBridge", "MainWindow.xaml"));
        var selectedCashPaymentIndex = xaml.IndexOf("Content=\"{Binding SelectedCashPaymentRule}\"", StringComparison.Ordinal);
        Assert.True(selectedCashPaymentIndex >= 0, "Cash Payment editor should still bind SelectedCashPaymentRule.");

        var avatarScalingActionIndex = xaml.IndexOf("Avatar Scaling Action", selectedCashPaymentIndex, StringComparison.Ordinal);
        Assert.True(avatarScalingActionIndex > selectedCashPaymentIndex, "Cash Payment editor should still include Avatar Scaling Action.");

        var cashPaymentScaleEditor = xaml[selectedCashPaymentIndex..];
        Assert.Contains("Binding=\"{Binding UsesAvatarScaling}\"", cashPaymentScaleEditor, StringComparison.Ordinal);
        Assert.Contains("DataContext=\"{Binding ScaleAction}\"", cashPaymentScaleEditor, StringComparison.Ordinal);
        Assert.Contains("ScaleActionModeButton_Click", cashPaymentScaleEditor, StringComparison.Ordinal);
        Assert.Contains("ScaleActionMultOpButton_Click", cashPaymentScaleEditor, StringComparison.Ordinal);
    }

    [Fact]
    public void AvatarScalingManagerWindow_KeepsTask8EditorSections()
    {
        var xaml = File.ReadAllText(FindSourceFile("VrcTwitchOscBridge", "AvatarScalingManagerWindow.xaml"));
        var codeBehind = File.ReadAllText(FindSourceFile("VrcTwitchOscBridge", "AvatarScalingManagerWindow.xaml.cs"));
        var editorIndex = xaml.IndexOf("Grid.Column=\"2\"", StringComparison.Ordinal);

        Assert.True(editorIndex >= 0, "Right-side editor should stay in Grid.Column=\"2\".");
        var editorBlock = xaml[editorIndex..];
        Assert.Contains("Twitch Reward", editorBlock, StringComparison.Ordinal);
        Assert.Contains("Height Change", editorBlock, StringComparison.Ordinal);
        Assert.Contains("Timer &amp; Return", editorBlock, StringComparison.Ordinal);
        Assert.Contains("Safety &amp; Pairing", editorBlock, StringComparison.Ordinal);
        Assert.Contains("ScaleActionModeButton_Click", editorBlock, StringComparison.Ordinal);
        Assert.Contains("private void ScaleActionModeButton_Click", codeBehind, StringComparison.Ordinal);
    }

    private static string GetButtonBlock(string xaml, string marker)
    {
        var markerIndex = xaml.IndexOf(marker, StringComparison.Ordinal);
        Assert.True(markerIndex >= 0, $"Could not find button marker '{marker}'.");

        var buttonStart = xaml.LastIndexOf("<Button", markerIndex, StringComparison.Ordinal);
        Assert.True(buttonStart >= 0, $"Could not find button start for '{marker}'.");

        var buttonEnd = xaml.IndexOf("</Button>", markerIndex, StringComparison.Ordinal);
        Assert.True(buttonEnd >= 0, $"Could not find button end for '{marker}'.");

        return xaml.Substring(buttonStart, buttonEnd - buttonStart + "</Button>".Length);
    }

    private static string GetElementBlock(string source, string marker, string startTag, string endTag)
    {
        var markerIndex = source.IndexOf(marker, StringComparison.Ordinal);
        Assert.True(markerIndex >= 0, $"Could not find element marker '{marker}'.");

        var blockStart = source.LastIndexOf(startTag, markerIndex, StringComparison.Ordinal);
        Assert.True(blockStart >= 0, $"Could not find element start '{startTag}' for '{marker}'.");

        var blockEnd = source.IndexOf(endTag, markerIndex, StringComparison.Ordinal);
        Assert.True(blockEnd >= 0, $"Could not find element end '{endTag}' for '{marker}'.");

        return source.Substring(blockStart, blockEnd - blockStart + endTag.Length);
    }

    private static string GetMethodBody(string source, string methodSignatureStart)
    {
        var methodStart = source.IndexOf(methodSignatureStart, StringComparison.Ordinal);
        Assert.True(methodStart >= 0, $"Could not find method signature starting with '{methodSignatureStart}'.");

        var bodyStart = source.IndexOf('{', methodStart);
        Assert.True(bodyStart >= 0, $"Could not find method body for '{methodSignatureStart}'.");

        var depth = 0;
        for (var index = bodyStart; index < source.Length; index++)
        {
            if (source[index] == '{')
            {
                depth++;
            }
            else if (source[index] == '}')
            {
                depth--;
                if (depth == 0)
                {
                    return source.Substring(bodyStart, index - bodyStart + 1);
                }
            }
        }

        throw new InvalidOperationException($"Could not find method body end for '{methodSignatureStart}'.");
    }

    private static string NormalizeWhitespace(string source) => Regex.Replace(source, @"\s+", " ");

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
