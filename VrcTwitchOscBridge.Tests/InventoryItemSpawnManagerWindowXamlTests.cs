using System;
using System.IO;
using System.Linq;
using Xunit;

namespace VrcTwitchOscBridge.Tests;

public sealed class InventoryItemSpawnManagerWindowXamlTests
{
    private static string FindSourceFile(string projectName, string fileName)
    {
        var dir = AppContext.BaseDirectory;
        while (dir != null)
        {
            var candidate = Path.Combine(dir, projectName, fileName);
            if (File.Exists(candidate))
                return candidate;
            var parent = Directory.GetParent(dir);
            dir = parent?.FullName;
        }
        throw new FileNotFoundException($"Could not find {fileName} in any parent of {AppContext.BaseDirectory}");
    }

    [Fact]
    public void Window_HasCustomChrome()
    {
        var xaml = File.ReadAllText(FindSourceFile("VrcTwitchOscBridge", "InventoryItemSpawnManagerWindow.xaml"));
        Assert.Contains("WindowStyle=\"None\"", xaml);
        Assert.Contains("shell:WindowChrome", xaml);
    }

    [Fact]
    public void Window_HasInventoryItemSpawnManagerViewModelDataContext()
    {
        var xaml = File.ReadAllText(FindSourceFile("VrcTwitchOscBridge", "InventoryItemSpawnManagerWindow.xaml"));
        Assert.Contains("d:DataContext=\"{d:DesignInstance Type=vm:InventoryItemSpawnManagerViewModel", xaml);
    }

    [Fact]
    public void CardGrid_BindsToCardsView()
    {
        var xaml = File.ReadAllText(FindSourceFile("VrcTwitchOscBridge", "InventoryItemSpawnManagerWindow.xaml"));
        Assert.Contains("ItemsSource=\"{Binding CardsView}\"", xaml);
    }

    [Fact]
    public void EditorPanel_BindsToSelectedRule()
    {
        var xaml = File.ReadAllText(FindSourceFile("VrcTwitchOscBridge", "InventoryItemSpawnManagerWindow.xaml"));
        Assert.Contains("{Binding SelectedRule.", xaml);
    }

    [Fact]
    public void Toolbar_HasSearchRefreshAndAddNew()
    {
        var xaml = File.ReadAllText(FindSourceFile("VrcTwitchOscBridge", "InventoryItemSpawnManagerWindow.xaml"));
        Assert.Contains("{Binding SearchText,", xaml);
        Assert.Contains("{Binding RefreshInventoryCommand", xaml);
        Assert.Contains("{Binding AddNewRuleCommand", xaml);
    }

    [Fact]
    public void ItemPicker_BindsToFilteredInventoryItems()
    {
        var xaml = File.ReadAllText(FindSourceFile("VrcTwitchOscBridge", "InventoryItemSpawnManagerWindow.xaml"));
        Assert.Contains("{Binding FilteredInventoryItems", xaml);
        Assert.Contains("{Binding SelectedInventoryItem", xaml);
    }

    [Fact]
    public void SyncModeComboBox_HasCreateAndLinkOptions()
    {
        var xaml = File.ReadAllText(FindSourceFile("VrcTwitchOscBridge", "InventoryItemSpawnManagerWindow.xaml"));
        Assert.Contains("Create &amp; Manage", xaml);
        Assert.Contains("Link Existing", xaml);
    }
}
