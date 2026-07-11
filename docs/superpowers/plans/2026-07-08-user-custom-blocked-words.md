# User-Custom Blocked Words — Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Add a user-configurable banned-word list to the Twitch Chatbox settings, exposed alongside the existing hardcoded words, with add/remove/suppress controls.

**Architecture:** Two new `ObservableCollection<string>` fields in `AppSettings` (persisted through `SettingsStore`). `ChatboxRelayModerationFilter` gains a `SetUserBlockList()` static method that rebuilds the internal regex array. The chatbox settings UI gets a "Blocked Words" section with add/remove/restore controls backed by `MainWindowViewModel`.

**Tech Stack:** C# / .NET 10 / WPF / xUnit

## Global Constraints

- Only word-level terms (the 33 `BlockedSlurTerms`) are user-editable; harassment phrases and doxxing patterns stay hardcoded
- Suppression uses case-insensitive string comparison, not hashes/IDs
- `SetUserBlockList` rebuilds patterns from the merged effective list
- `blockedPatterns` array is `volatile` for safe runtime swap
- UI follows existing chatbox settings patterns (ToggleButton headers, `{loc:Translate}` labels)
- No new files outside of test project

---

### Task 1: Add data model fields and persistence

**Files:**
- Modify: `VrcTwitchOscBridge/Models/AppSettings.cs`
- Modify: `VrcTwitchOscBridge/Services/SettingsStore.cs`
- Test: `VrcTwitchOscBridge.Tests/` (existing test patterns)

**Interfaces:**
- Produces: `AppSettings.CustomBlockedWords` (ObservableCollection<string>), `AppSettings.SuppressedBlockedWords` (ObservableCollection<string>)

- [ ] **Step 1: Add fields to `AppSettings.cs`**

Find the class `AppSettings : ObservableObject`. Add near the other chatbox fields:

```csharp
private ObservableCollection<string> _customBlockedWords = [];
public ObservableCollection<string> CustomBlockedWords
{
    get => _customBlockedWords;
    set => SetProperty(ref _customBlockedWords, value);
}

private ObservableCollection<string> _suppressedBlockedWords = [];
public ObservableCollection<string> SuppressedBlockedWords
{
    get => _suppressedBlockedWords;
    set => SetProperty(ref _suppressedBlockedWords, value);
}
```

- [ ] **Step 2: Add fields to `PersistedProfileSettings` in `SettingsStore.cs`**

In the `PersistedProfileSettings` class, add:

```csharp
public List<string>? CustomBlockedWords { get; set; }
public List<string>? SuppressedBlockedWords { get; set; }
```

- [ ] **Step 3: Read the fields in `LoadAsync()`**

Find the section where `PersistedProfileSettings` is applied onto `AppSettings`. Add:

```csharp
if (profile.CustomBlockedWords is { Count: > 0 })
    settings.CustomBlockedWords = new ObservableCollection<string>(profile.CustomBlockedWords);
if (profile.SuppressedBlockedWords is { Count: > 0 })
    settings.SuppressedBlockedWords = new ObservableCollection<string>(profile.SuppressedBlockedWords);
```

- [ ] **Step 4: Write the fields in `SaveAsync()`**

In the `SaveAsync` method where `PersistedProfileSettings` is built from `AppSettings`, add:

```csharp
CustomBlockedWords = appSettings.CustomBlockedWords?.ToList(),
SuppressedBlockedWords = appSettings.SuppressedBlockedWords?.ToList(),
```

- [ ] **Step 5: Build to verify**

Run: `dotnet build "E:\!!!Program to work on\Proper Crystal Relay\VrcTwitchOscBridge\VrcTwitchOscBridge.csproj" --no-restore`

Expected: Build succeeded

- [ ] **Step 6: Commit**

```bash
git add "VrcTwitchOscBridge/Models/AppSettings.cs" "VrcTwitchOscBridge/Services/SettingsStore.cs"
git commit -m "feat: add CustomBlockedWords and SuppressedBlockedWords to settings model"
```

---

### Task 2: Add SetUserBlockList to moderation filter

**Files:**
- Modify: `VrcTwitchOscBridge/Services/ChatboxRelayModerationFilter.cs`
- Modify: `VrcTwitchOscBridge.Tests/ChatboxRelayModerationFilterTests.cs`

**Interfaces:**
- Consumes: `string[]` custom words, `string[]` suppressed words
- Produces: `ChatboxRelayModerationFilter.SetUserBlockList(IEnumerable<string>, IEnumerable<string>)` — must be called after settings load and after any user edit

- [ ] **Step 1: Read the current filter file**

Read `VrcTwitchOscBridge/Services/ChatboxRelayModerationFilter.cs` to understand the current structure.

- [ ] **Step 2: Make `BlockedPatterns` volatile and add `SetUserBlockList`**

Change the field declaration and add the new method:

```csharp
// Old (line ~38):
// private static readonly Regex[] BlockedPatterns = [.. BuildBlockedPatterns()];
// New:
private static volatile Regex[] blockedPatterns = [.. BuildBlockedPatterns()];
```

Add a new method after the existing fields:

```csharp
public static void SetUserBlockList(
    IEnumerable<string> customWords,
    IEnumerable<string> suppressedWords)
{
    var suppressed = suppressedWords is not null
        ? new HashSet<string>(suppressedWords, StringComparer.OrdinalIgnoreCase)
        : [];

    var effectiveSlurTerms = BlockedSlurTerms
        .Concat(customWords ?? [])
        .Where(w => !string.IsNullOrWhiteSpace(w) && !suppressed.Contains(w))
        .ToArray();

    var allTerms = effectiveSlurTerms.Concat(BlockedHarassmentPhrases);
    blockedPatterns = [.. BuildBlockedPatterns(allTerms)];
}
```

Also update `BuildBlockedPatterns()` to accept a parameter instead of using the field directly. The original method uses `BlockedSlurTerms.Concat(BlockedHarassmentPhrases)`. Change it to accept an `IEnumerable<string>` parameter:

```csharp
private static IEnumerable<Regex> BuildBlockedPatterns()
{
    return BuildBlockedPatterns(BlockedSlurTerms.Concat(BlockedHarassmentPhrases));
}

private static IEnumerable<Regex> BuildBlockedPatterns(IEnumerable<string> terms)
{
    foreach (var term in terms)
    {
        // ... existing logic unchanged ...
    }
}
```

Update `ShouldBlockMessage` to use `blockedPatterns` (lowercase, non-readonly) instead of `BlockedPatterns`:

```csharp
// In ShouldBlockMessage:
&& blockedPatterns.Any(pattern => pattern.IsMatch(normalizedText)))
```

- [ ] **Step 3: Add tests for custom words and suppression**

Add to `ChatboxRelayModerationFilterTests.cs`:

```csharp
[Fact]
public void CustomWord_IsBlocked()
{
    ChatboxRelayModerationFilter.SetUserBlockList(["customslur"], []);
    Assert.True(ChatboxRelayModerationFilter.ShouldBlockMessage("customslur"));
    Assert.True(ChatboxRelayModerationFilter.ShouldBlockMessage("custom slur"));
    Assert.True(ChatboxRelayModerationFilter.ShouldBlockMessage("customslur"));
    // Reset
    ChatboxRelayModerationFilter.SetUserBlockList([], []);
}

[Fact]
public void SuppressedHardcodedWord_IsNotBlocked()
{
    ChatboxRelayModerationFilter.SetUserBlockList([], ["nigger"]);
    Assert.False(ChatboxRelayModerationFilter.ShouldBlockMessage("nigger"));
    // Other words still blocked
    Assert.True(ChatboxRelayModerationFilter.ShouldBlockMessage("chink"));
    // Reset
    ChatboxRelayModerationFilter.SetUserBlockList([], []);
}

[Fact]
public void AddedAndThenSuppressed_IsNotBlocked()
{
    ChatboxRelayModerationFilter.SetUserBlockList(["customslur"], ["customslur"]);
    Assert.False(ChatboxRelayModerationFilter.ShouldBlockMessage("customslur"));
    ChatboxRelayModerationFilter.SetUserBlockList([], []);
}

[Fact]
public void ExistingSlursStillBlockedAfterSetUserBlockList()
{
    ChatboxRelayModerationFilter.SetUserBlockList([], []);
    Assert.True(ChatboxRelayModerationFilter.ShouldBlockMessage("faggot"));
    Assert.True(ChatboxRelayModerationFilter.ShouldBlockMessage("dyke"));
    Assert.True(ChatboxRelayModerationFilter.ShouldBlockMessage("chink"));
}
```

**Important:** xUnit runs test classes in parallel by default. Since `SetUserBlockList` modifies global static state, the test class must use `[Collection]` to disable parallelization. The existing test class already has `[Collection("ChatboxModerationFilter")]` — ensure it's there. If not, add it:

```csharp
[Collection("ChatboxModerationFilter")]
public sealed class ChatboxRelayModerationFilterTests
```

- [ ] **Step 4: Run tests to verify they pass**

Run: `dotnet test "E:\!!!Program to work on\Proper Crystal Relay\VrcTwitchOscBridge.Tests\VrcTwitchOscBridge.Tests.csproj" --no-restore --filter "FullyQualifiedName~ChatboxRelayModerationFilterTests"`

Expected: all 23+ tests pass

- [ ] **Step 5: Commit**

```bash
git add "VrcTwitchOscBridge/Services/ChatboxRelayModerationFilter.cs" "VrcTwitchOscBridge.Tests/ChatboxRelayModerationFilterTests.cs"
git commit -m "feat: add SetUserBlockList to moderation filter with custom/suppressed word support"
```

---

### Task 3: Wire SetUserBlockList into BridgeCoordinator startup

**Files:**
- Modify: `VrcTwitchOscBridge/Services/BridgeCoordinator.cs`

**Interfaces:**
- Consumes: `ChatboxRelayModerationFilter.SetUserBlockList(IEnumerable<string>, IEnumerable<string>)`
- Consumes: `AppSettings.CustomBlockedWords`, `AppSettings.SuppressedBlockedWords`

- [ ] **Step 1: Find the settings-load completion handler**

In `BridgeCoordinator.cs`, find where app settings are fully loaded and ready. Search for existing code that reads `activeConfiguration` or `AppSettings` after load.

Add a call after settings are loaded:

```csharp
if (activeConfiguration is not null)
{
    ChatboxRelayModerationFilter.SetUserBlockList(
        activeConfiguration.CustomBlockedWords ?? [],
        activeConfiguration.SuppressedBlockedWords ?? []);
}
```

Place this near other initialization that uses `activeConfiguration`.

- [ ] **Step 2: Build to verify**

Run: `dotnet build "E:\!!!Program to work on\Proper Crystal Relay\VrcTwitchOscBridge\VrcTwitchOscBridge.csproj" --no-restore`

Expected: Build succeeded

- [ ] **Step 3: Commit**

```bash
git add "VrcTwitchOscBridge/Services/BridgeCoordinator.cs"
git commit -m "feat: call SetUserBlockList on BridgeCoordinator startup"
```

---

### Task 4: Add blocked word list view model

**Files:**
- Modify: `VrcTwitchOscBridge/ViewModels/MainWindowViewModel.cs`

**Interfaces:**
- Consumes: `AppSettings.CustomBlockedWords`, `AppSettings.SuppressedBlockedWords`
- Consumes: `ChatboxRelayModerationFilter.SetUserBlockList`
- Produces: `ObservableCollection<BlockedWordItem> BlockedWordItems`, add/remove/restore commands, `BlockedWordsSectionOpen`
- Pending consumption by: `TwitchChatboxWindow.xaml`

- [ ] **Step 1: Create a `BlockedWordItem` record or class**

Add a small data type near the ViewModel or in a models file:

```csharp
public sealed record BlockedWordItem(
    string Word,
    bool IsCustom,
    bool IsSuppressed);
```

- [ ] **Step 2: Add ViewModel properties and commands**

In `MainWindowViewModel.cs`, add:

```csharp
private bool _blockedWordsSectionOpen;
public bool BlockedWordsSectionOpen
{
    get => _blockedWordsSectionOpen;
    set => SetProperty(ref _blockedWordsSectionOpen, value);
}

private ObservableCollection<BlockedWordItem> _blockedWordItems = [];
public ObservableCollection<BlockedWordItem> BlockedWordItems
{
    get => _blockedWordItems;
    set => SetProperty(ref _blockedWordItems, value);
}

private string _newBlockedWordText = string.Empty;
public string NewBlockedWordText
{
    get => _newBlockedWordText;
    set => SetProperty(ref _newBlockedWordText, value);
}
```

Commands (initialize in the ViewModel constructor):

```csharp
public RelayCommand AddBlockedWordCommand { get; }
public RelayCommand<BlockedWordItem> RemoveBlockedWordCommand { get; }
public RelayCommand<BlockedWordItem> RestoreBlockedWordCommand { get; }
```

```csharp
AddBlockedWordCommand = new RelayCommand(AddBlockedWord, () => !string.IsNullOrWhiteSpace(NewBlockedWordText));
RemoveBlockedWordCommand = new RelayCommand<BlockedWordItem>(RemoveBlockedWord);
RestoreBlockedWordCommand = new RelayCommand<BlockedWordItem>(RestoreBlockedWord);
```

Methods:

```csharp
private void AddBlockedWord()
{
    var word = NewBlockedWordText.Trim().ToLowerInvariant();
    if (string.IsNullOrWhiteSpace(word) || word.Length < 2)
        return;

    // If it's a hardcoded word that was suppressed, just restore it
    if (Settings.SuppressedBlockedWords.Remove(word))
    {
        NewBlockedWordText = string.Empty;
        RefreshBlockedWordItems();
        return;
    }

    // If already in custom list, don't duplicate
    if (Settings.CustomBlockedWords.Contains(word))
    {
        NewBlockedWordText = string.Empty;
        return;
    }

    // If it's a hardcoded word not suppressed, nothing to do (already in list)
    // But we shouldn't be able to "add" a hardcoded word
    var blockedSlurTerms = ...; // Can't access private field. Instead, check via ShouldBlockMessage?

    Settings.CustomBlockedWords.Add(word);
    NewBlockedWordText = string.Empty;
    RefreshBlockedWordItems();
    SyncFilterWithUserList();
}

private void RemoveBlockedWord(BlockedWordItem item)
{
    if (item.IsCustom)
    {
        Settings.CustomBlockedWords.Remove(item.Word);
    }
    else
    {
        // Suppress hardcoded word
        Settings.SuppressedBlockedWords.Add(item.Word);
    }
    RefreshBlockedWordItems();
    SyncFilterWithUserList();
}

private void RestoreBlockedWord(BlockedWordItem item)
{
    Settings.SuppressedBlockedWords.Remove(item.Word);
    RefreshBlockedWordItems();
    SyncFilterWithUserList();
}

private void RefreshBlockedWordItems()
{
    var items = new ObservableCollection<BlockedWordItem>();
    var suppressed = new HashSet<string>(Settings.SuppressedBlockedWords, StringComparer.OrdinalIgnoreCase);
    var custom = new HashSet<string>(Settings.CustomBlockedWords, StringComparer.OrdinalIgnoreCase);

    foreach (var word in ChatboxRelayModerationFilter.BlockedSlurTerms)
    {
        items.Add(new BlockedWordItem(word, IsCustom: false, IsSuppressed: suppressed.Contains(word)));
    }

    foreach (var word in Settings.CustomBlockedWords)
    {
        if (!string.IsNullOrWhiteSpace(word))
        {
            items.Add(new BlockedWordItem(word, IsCustom: true, IsSuppressed: false));
        }
    }

    BlockedWordItems = items;
}

private void SyncFilterWithUserList()
{
    ChatboxRelayModerationFilter.SetUserBlockList(
        Settings.CustomBlockedWords,
        Settings.SuppressedBlockedWords);
}
```

**In Task 2:** Also make `BlockedSlurTerms` public:

```csharp
public static readonly string[] BlockedSlurTerms =
```

- [ ] **Step 3: Build to verify**

- [ ] **Step 4: Commit**

---

### Task 5: Add Blocked Words UI to chatbox settings

**Files:**
- Modify: `VrcTwitchOscBridge/TwitchChatboxWindow.xaml`
- Modify: `VrcTwitchOscBridge/TwitchChatboxWindow.xaml.cs`

**Interfaces:**
- Consumes: `MainWindowViewModel.BlockedWordItems`, `AddBlockedWordCommand`, etc.

- [ ] **Step 1: Add the Blocked Words section to the XAML**

In `TwitchChatboxWindow.xaml`, after the VRChat Chatbox panel (row=1, column span=2, around line 2298), add a new row:

```xml
<!-- Blocked Words -->
<Border Grid.Row="2" Grid.ColumnSpan="2" Style="{StaticResource SettingsPanelSectionBorderStyle}">
    <StackPanel>
        <ToggleButton x:Name="BlockedWordsToggle"
                      Style="{StaticResource SettingsSectionToggleStyle}"
                      Content="Blocked Words"
                      IsChecked="{Binding BlockedWordsSectionOpen}" />

        <StackPanel Visibility="{Binding BlockedWordsSectionOpen, Converter={StaticResource BoolToVisibilityConverter}}"
                    Margin="8,4,8,8">
            <!-- Add word row -->
            <DockPanel Margin="0,0,0,6">
                <Button DockPanel.Dock="Right"
                        Style="{StaticResource SecondaryButtonStyle}"
                        Padding="8,2"
                        Command="{Binding AddBlockedWordCommand}">+ Add</Button>
                <TextBox Text="{Binding NewBlockedWordText, UpdateSourceTrigger=PropertyChanged}"
                         Style="{StaticResource SettingsTextBoxStyle}"
                         ToolTip="Enter a word to block" />
            </DockPanel>

            <!-- Word list -->
            <ItemsControl ItemsSource="{Binding BlockedWordItems}"
                          MaxHeight="200"
                          VirtualizingStackPanel.IsVirtualizing="True">
                <ItemsControl.ItemTemplate>
                    <DataTemplate>
                        <DockPanel Margin="0,1">
                            <Button DockPanel.Dock="Right"
                                    Style="{StaticResource SecondaryButtonStyle}"
                                    Padding="4,0"
                                    FontSize="11"
                                    Command="{Binding DataContext.RemoveBlockedWordCommand, RelativeSource={RelativeSource AncestorType=Window}}"
                                    CommandParameter="{Binding}"
                                    Visibility="{Binding IsCustom, Converter={StaticResource BoolToVisibilityConverter}}"
                                    ToolTip="Remove this word">✕</Button>
                            <Button DockPanel.Dock="Right"
                                    Style="{StaticResource SecondaryButtonStyle}"
                                    Padding="4,0"
                                    FontSize="11"
                                    Command="{Binding DataContext.RestoreBlockedWordCommand, RelativeSource={RelativeSource AncestorType=Window}}"
                                    CommandParameter="{Binding}"
                                    Visibility="{Binding IsSuppressed, Converter={StaticResource BoolToVisibilityConverter}}"
                                    ToolTip="Restore this word">Restore</Button>
                            <TextBlock Text="{Binding Word}"
                                       VerticalAlignment="Center"
                                       FontFamily="Consolas"
                                       FontSize="12"
                                       Opacity="{Binding IsSuppressed, Converter={StaticResource SuppressedOpacityConverter}}" />
                        </DockPanel>
                    </DataTemplate>
                </ItemsControl.ItemTemplate>
            </ItemsControl>
        </StackPanel>
    </StackPanel>
</Border>
```

- [ ] **Step 2: Wire up the code-behind toggle in `TwitchChatboxWindow.xaml.cs`**

In `ApplyChatboxStateFromSettings()` or equivalent method, ensure the blocked words section visibility is handled. If the existing pattern uses manual visibility, add:

```csharp
BlockedWordsToggle.IsChecked = viewModel?.Settings?.BlockedWordsSectionOpen ?? false;
```

And in the toggle handler:

```csharp
private void OnBlockedWordsToggleClicked(object sender, RoutedEventArgs e)
{
    if (viewModel?.Settings is not null)
    {
        viewModel.Settings.BlockedWordsSectionOpen = BlockedWordsToggle.IsChecked == true;
    }
}
```

- [ ] **Step 3: Add a `SuppressedOpacityConverter` or use a DataTrigger**

In the XAML, suppressed words should be dimmed. If `BoolToVisibilityConverter` isn't suitable, add a simple converter that returns 0.4 for true, 1.0 for false. Or use a DataTrigger in the TextBlock style:

```xml
<TextBlock.Style>
    <Style TargetType="TextBlock">
        <Setter Property="Opacity" Value="1.0" />
        <Style.Triggers>
            <DataTrigger Binding="{Binding IsSuppressed}" Value="True">
                <Setter Property="Opacity" Value="0.4" />
                <Setter Property="TextDecorations" Value="Strikethrough" />
            </DataTrigger>
        </Style.Triggers>
    </Style>
</TextBlock.Style>
```

- [ ] **Step 4: Build to verify**

Run: `dotnet build "E:\!!!Program to work on\Proper Crystal Relay\VrcTwitchOscBridge\VrcTwitchOscBridge.csproj" --no-restore`

Expected: Build succeeded

- [ ] **Step 5: Commit**

---

### Task 6: Build & run full test suite

- [ ] **Step 1: Run the moderation filter tests**

Run: `dotnet test "E:\!!!Program to work on\Proper Crystal Relay\VrcTwitchOscBridge.Tests\VrcTwitchOscBridge.Tests.csproj" --no-restore --filter "FullyQualifiedName~ChatboxRelayModerationFilterTests"`

Expected: all PASS

- [ ] **Step 2: Build main project**

Run: `dotnet build "E:\!!!Program to work on\Proper Crystal Relay\VrcTwitchOscBridge\VrcTwitchOscBridge.csproj" --no-restore`

Expected: Build succeeded

- [ ] **Step 3: Commit**
