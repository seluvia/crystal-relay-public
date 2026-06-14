# Wardrobe Param Multi-Select + Copy/Paste Set — Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Add Ctrl/Shift multi-select of `WardrobeSnapshotParam` rows in the Avatar Sets Manager wardrobe editor, with Copy / Paste of the whole selected set into another outfit (cross-profile allowed, skip on name conflict, append to end of destination in source order), plus keyboard shortcuts (Ctrl+C/V/A, Delete, Esc) and a multi-select banner that hides the single-item editor.

**Architecture:** Extend the existing in-memory single-item `Copy`/`Paste` of `WardrobeSnapshotParam` to a collection. The `ListBox` gets `SelectionMode=Extended` plus a mirrored `SelectedWardrobeSnapshotParams` collection on the ViewModel. `ListBox.InputBindings` wire the shortcuts. Toolbar buttons rewire to the new commands (captions unchanged). The `OnWardrobeParamListSelectionChanged` handler syncs the ViewModel collection from `ListBox.SelectedItems`. `SelectedWardrobeOutfit` and `SelectedProfile` setters clear the param selection so switching starts fresh. No model changes. No new dependencies. No new converters — the editor's visibility uses a single derived `IsWardrobeParamEditorVisible` ViewModel property bound with the existing `BoolToVisibilityConverter`.

**Tech Stack:** C# / WPF / .NET 10 / ObservableObject MVVM. Existing `RelayCommand`, `AppendLog`, `T()` localization helper, `CloneWardrobeSnapshotParam` are reused. New `Key` strings go in `en-US.extra.json` + 12 locale `.extra.json` files.

**Spec:** `docs/superpowers/specs/2026-06-13-wardrobe-multiselect-copypaste-design.md` (commit `6af0ee0`)

---

## File Map

| File | Responsibility for this plan |
|---|---|
| `ViewModels/AvatarSetsManagerViewModel.cs` | Replace 2 single-item commands with 4 multi-item commands. Add 5 new derived properties + 1 combined `IsWardrobeParamEditorVisible` property. Update `SelectedWardrobeOutfit` and `SelectedProfile` setters to clear the param selection. Update `RemoveWardrobeSnapshotParam` to handle multi. Add `SyncWardrobeParamSelectionFromList`, `RefreshWardrobeParamSelectionDerived`, `ClearWardrobeSnapshotParamSelection`, `SelectAllWardrobeSnapshotParams`, `CopySelectedWardrobeSnapshotParams`, `PasteWardrobeSnapshotParams`. |
| `AvatarSetsManagerWindow.xaml` | On the param `ListBox` (around line 1907): add `x:Name="WardrobeParamListBox"`, `SelectionMode="Extended"`, `ToolTip`, `SelectionChanged="OnWardrobeParamListSelectionChanged"`, `KeyDown="OnWardrobeParamListKeyDown"`, `InputBindings` (Ctrl+C/V/A, Delete, Esc), `ContextMenu`. Add multi-select banner `Border` between the list and the editor. Update the editor `Border` (around line 1921) `Visibility` to bind to `IsWardrobeParamEditorVisible` via `BoolToVisibilityConverter`. Rewire 3 toolbar buttons (`Remove`, `Copy`, `Paste`) at lines 1891–1904. |
| `AvatarSetsManagerWindow.xaml.cs` | Add `OnWardrobeParamListSelectionChanged` (calls `Vm.SyncWardrobeParamSelectionFromList`). Add stub `OnWardrobeParamListKeyDown`. |
| `Resources/Localization/en-US.extra.json` | Add 4 new keys. |
| `Resources/Localization/de-DE.extra.json` | Add 4 new keys (German, informal `du`). |
| `Resources/Localization/es-ES.extra.json` | Add 4 new keys (Spanish, informal `tú`). |
| `Resources/Localization/fr-FR.extra.json` | Add 4 new keys (French, informal `tu`). |
| `Resources/Localization/it-IT.extra.json` | Add 4 new keys (Italian, informal `tu`). |
| `Resources/Localization/ja-JP.extra.json` | Add 4 new keys (Japanese, informal). |
| `Resources/Localization/ko-KR.extra.json` | Add 4 new keys (Korean, informal). |
| `Resources/Localization/pl-PL.extra.json` | Add 4 new keys (Polish, informal `ty`). |
| `Resources/Localization/pt-BR.extra.json` | Add 4 new keys (Portuguese-BR, informal). |
| `Resources/Localization/ru-RU.extra.json` | Add 4 new keys (Russian, informal `ты`). |
| `Resources/Localization/sv-SE.extra.json` | Add 4 new keys (Swedish, informal `du`). |
| `Resources/Localization/th-TH.extra.json` | Add 4 new keys (Thai, informal). |
| `Resources/Localization/zh-CN.extra.json` | Add 4 new keys (Chinese Simplified, informal). |
| `Resources/Localization/zh-TW.extra.json` | Add 4 new keys (Chinese Traditional, informal). |

No new files. No model changes. No new NuGet packages. No new converters.

---

## Task 1: Add multi-select state properties to `AvatarSetsManagerViewModel`

**Files:**
- Modify: `ViewModels/AvatarSetsManagerViewModel.cs` — add new state collection and 5 derived properties.

- [ ] **Step 1: Read the field declarations around line 274-275**

```powershell
Select-String -Path "E:\!!!Program to work on\Proper Crystal Relay\VrcTwitchOscBridge\ViewModels\AvatarSetsManagerViewModel.cs" -Pattern "_copiedWardrobeSnapshotParam|_copiedWardrobeOutfit" | Select-Object -First 10
```

Expected: confirm the existing `_copiedWardrobeSnapshotParam` and `_copiedWardrobeOutfit` fields at line 274-275.

- [ ] **Step 2: Replace the single-item clipboard field with a collection**

Find the line:
```csharp
private Models.WardrobeSnapshotParam? _copiedWardrobeSnapshotParam;
```

Replace it with:
```csharp
private readonly ObservableCollection<Models.WardrobeSnapshotParam> _copiedWardrobeSnapshotParams = [];
public ObservableCollection<Models.WardrobeSnapshotParam> CopiedWardrobeSnapshotParams => _copiedWardrobeSnapshotParams;
public int CopiedWardrobeSnapshotParamCount => _copiedWardrobeSnapshotParams.Count;
public bool HasCopiedWardrobeSnapshotParams => _copiedWardrobeSnapshotParams.Count > 0;

public ObservableCollection<Models.WardrobeSnapshotParam> SelectedWardrobeSnapshotParams { get; } = [];
public int SelectedWardrobeSnapshotParamCount => SelectedWardrobeSnapshotParams.Count;
public bool HasMultipleWardrobeSnapshotParamsSelected => SelectedWardrobeSnapshotParams.Count > 1;
public bool HasAnyWardrobeSnapshotParamSelected => SelectedWardrobeSnapshotParams.Count > 0;
public bool IsWardrobeParamEditorVisible =>
    SelectedWardrobeOutfit is not null && !HasMultipleWardrobeSnapshotParamsSelected;
```

If `using System.Collections.ObjectModel;` is not already in the file's `using` directives at the top, add it. (Check: it almost certainly is, because `WardrobeOutfit.SnapshotParams` is `ObservableCollection<>` and the file already references it.)

- [ ] **Step 3: Add the selection-refresh helper near the other private helpers**

Find a quiet place near the bottom of the class (e.g. just before the `Dispose` pattern or near the existing `private void CopyWardrobeSnapshotParam()` block around line 1424). Add this method:

```csharp
private void RefreshWardrobeParamSelectionDerived()
{
    RaisePropertyChanged(nameof(SelectedWardrobeSnapshotParamCount));
    RaisePropertyChanged(nameof(HasMultipleWardrobeSnapshotParamsSelected));
    RaisePropertyChanged(nameof(HasAnyWardrobeSnapshotParamSelected));
    RaisePropertyChanged(nameof(IsWardrobeParamEditorVisible));
    RemoveWardrobeSnapshotParamCommand.NotifyCanExecuteChanged();
    CopySelectedWardrobeSnapshotParamsCommand.NotifyCanExecuteChanged();
    ClearWardrobeSnapshotParamSelectionCommand.NotifyCanExecuteChanged();
    SelectAllWardrobeSnapshotParamsCommand.NotifyCanExecuteChanged();
}
```

Note: the commands `RemoveWardrobeSnapshotParamCommand`, `CopySelectedWardrobeSnapshotParamsCommand`, `ClearWardrobeSnapshotParamSelectionCommand`, `SelectAllWardrobeSnapshotParamsCommand` are added in Task 2. The build will be red until Task 2 lands, but the file still compiles standalone because the field types are already declared.

- [ ] **Step 4: Build to verify syntax (expect commands-not-found errors for now)**

Run:
```powershell
dotnet build "E:\!!!Program to work on\Proper Crystal Relay\VrcTwitchOscBridge\VrcTwitchOscBridge.csproj" --no-restore 2>&1 | Select-Object -Last 20
```

Expected: errors referencing `CopySelectedWardrobeSnapshotParamsCommand`, `ClearWardrobeSnapshotParamSelectionCommand`, `SelectAllWardrobeSnapshotParamsCommand` (these are added in Task 2). If only those errors appear, the syntax of what we added in this task is correct.

- [ ] **Step 5: Commit (note: build is red until Task 2)**

```powershell
git add "VrcTwitchOscBridge/ViewModels/AvatarSetsManagerViewModel.cs"
git commit -m "Add wardrobe param multi-select state properties"
```

---

## Task 2: Replace single-item commands with multi-item commands in the ViewModel

**Files:**
- Modify: `ViewModels/AvatarSetsManagerViewModel.cs` — remove 2 old commands, add 4 new ones, add implementations, update `RemoveWardrobeSnapshotParam`.

- [ ] **Step 1: Remove the old single-item command declarations**

Find these lines (around line 160-161 and 708-709):
```csharp
CopyWardrobeSnapshotParamCommand = new RelayCommand(CopyWardrobeSnapshotParam, ...);
PasteWardrobeSnapshotParamCommand = new RelayCommand(PasteWardrobeSnapshotParam, ...);
```

Delete both command declarations AND the constructor assignments for them (i.e. the `CopyWardrobeSnapshotParamCommand = ...` and `PasteWardrobeSnapshotParamCommand = ...` lines). Keep the existing `CopyWardrobeOutfitCommand` and `PasteWardrobeOutfitCommand` (those are outfit-level, separate feature).

- [ ] **Step 2: Add the new command declarations in the same place**

Add these four commands:

```csharp
CopySelectedWardrobeSnapshotParamsCommand = new RelayCommand(CopySelectedWardrobeSnapshotParams,
    () => SelectedWardrobeSnapshotParams.Count > 0);
PasteWardrobeSnapshotParamsCommand = new RelayCommand(PasteWardrobeSnapshotParams,
    () => SelectedWardrobeOutfit is not null && _copiedWardrobeSnapshotParams.Count > 0);
SelectAllWardrobeSnapshotParamsCommand = new RelayCommand(SelectAllWardrobeSnapshotParams,
    () => SelectedWardrobeOutfit is not null);
ClearWardrobeSnapshotParamSelectionCommand = new RelayCommand(ClearWardrobeSnapshotParamSelection,
    () => SelectedWardrobeSnapshotParams.Count > 0);
```

- [ ] **Step 3: Add the public sync method (called from the XAML code-behind)**

Add this public method somewhere in the class body (next to `RefreshWardrobeParamSelectionDerived`):

```csharp
public void SyncWardrobeParamSelectionFromList(IEnumerable<Models.WardrobeSnapshotParam> newSelection)
{
    SelectedWardrobeSnapshotParams.Clear();
    foreach (var p in newSelection) SelectedWardrobeSnapshotParams.Add(p);
    RefreshWardrobeParamSelectionDerived();
}
```

- [ ] **Step 4: Replace the old `CopyWardrobeSnapshotParam` and `PasteWardrobeSnapshotParam` methods**

Find the existing implementations (around line 1424-1446). Replace them with:

```csharp
private void CopySelectedWardrobeSnapshotParams()
{
    _copiedWardrobeSnapshotParams.Clear();
    foreach (var p in SelectedWardrobeSnapshotParams.ToList())
        _copiedWardrobeSnapshotParams.Add(CloneWardrobeSnapshotParam(p));
    RaisePropertyChanged(nameof(CopiedWardrobeSnapshotParamCount));
    RaisePropertyChanged(nameof(HasCopiedWardrobeSnapshotParams));
    PasteWardrobeSnapshotParamsCommand.NotifyCanExecuteChanged();
    AppendLog(T("Wardrobe Copy Log", _copiedWardrobeSnapshotParams.Count));
}

private void PasteWardrobeSnapshotParams()
{
    var outfit = SelectedWardrobeOutfit;
    if (outfit is null || _copiedWardrobeSnapshotParams.Count == 0) return;

    var existingNames = new HashSet<string>(
        outfit.SnapshotParams.Select(p => p.ParameterName),
        StringComparer.Ordinal);
    int added = 0, skipped = 0;
    foreach (var src in _copiedWardrobeSnapshotParams.ToList())
    {
        if (string.IsNullOrWhiteSpace(src.ParameterName)) { skipped++; continue; }
        if (existingNames.Contains(src.ParameterName)) { skipped++; continue; }
        var clone = CloneWardrobeSnapshotParam(src);
        outfit.SnapshotParams.Add(clone);
        existingNames.Add(clone.ParameterName);
        added++;
    }
    AppendLog(T("Wardrobe Paste Log", added, outfit.Name, skipped));
}

private void SelectAllWardrobeSnapshotParams()
{
    if (SelectedWardrobeOutfit is null) return;
    SelectedWardrobeSnapshotParams.Clear();
    foreach (var p in SelectedWardrobeOutfit.SnapshotParams)
        SelectedWardrobeSnapshotParams.Add(p);
    RefreshWardrobeParamSelectionDerived();
}

private void ClearWardrobeSnapshotParamSelection()
{
    SelectedWardrobeSnapshotParams.Clear();
    RefreshWardrobeParamSelectionDerived();
}
```

- [ ] **Step 5: Update `RemoveWardrobeSnapshotParam` to handle multi**

Find the existing method (around line 1388):
```csharp
private void RemoveWardrobeSnapshotParam()
{
    if (SelectedWardrobeSnapshotParam is null) return;
    var outfit = SelectedWardrobeOutfit;
    if (outfit is null) return;
    outfit.SnapshotParams.Remove(SelectedWardrobeSnapshotParam);
}
```

Replace it with:
```csharp
private void RemoveWardrobeSnapshotParam()
{
    var outfit = SelectedWardrobeOutfit;
    if (outfit is null) return;
    var toRemove = SelectedWardrobeSnapshotParams
        .OrderByDescending(p => outfit.SnapshotParams.IndexOf(p))
        .ToList();
    foreach (var p in toRemove) outfit.SnapshotParams.Remove(p);
    SelectedWardrobeSnapshotParams.Clear();
    RefreshWardrobeParamSelectionDerived();
}
```

The `OrderByDescending` keeps indices valid during the removal loop. The `IndexOf` returns -1 for items not in the outfit (defensive — should not happen since `SyncWardrobeParamSelectionFromList` mirrors the live list, but the sort still works since -1 sorts last).

- [ ] **Step 6: Build to verify**

Run:
```powershell
dotnet build "E:\!!!Program to work on\Proper Crystal Relay\VrcTwitchOscBridge\VrcTwitchOscBridge.csproj" --no-restore
```

Expected: `Build succeeded. 0 Warning(s) 0 Error(s)`. The XAML still references the old `CopyWardrobeSnapshotParamCommand` and `PasteWardrobeSnapshotParamCommand` (those are runtime warnings, not compile errors). Task 4 will update the XAML.

- [ ] **Step 7: Commit**

```powershell
git add "VrcTwitchOscBridge/ViewModels/AvatarSetsManagerViewModel.cs"
git commit -m "Replace single-item wardrobe param copy/paste with multi-item"
```

---

## Task 3: Update `SelectedWardrobeOutfit` and `SelectedProfile` setters to clear param selection

**Files:**
- Modify: `ViewModels/AvatarSetsManagerViewModel.cs` — add `SelectedWardrobeSnapshotParams.Clear()` + `RefreshWardrobeParamSelectionDerived()` to both setters.

- [ ] **Step 1: Update the `SelectedWardrobeOutfit` setter**

Find the setter (around line 304). Inside the `if (SetProperty(...))` block, after the existing `RaisePropertyChanged(nameof(SelectedWardrobeOutfit))` line and before the closing `}`, add:

```csharp
        SelectedWardrobeSnapshotParams.Clear();
        RefreshWardrobeParamSelectionDerived();
```

- [ ] **Step 2: Update the `SelectedProfile` setter**

Find the `SelectedProfile` setter (it raises several `PropertyChanged` for outfit/wardrobe state). At the end of its setter body (after the existing `RaisePropertyChanged` calls but before the closing `}`), add the same two lines:

```csharp
        SelectedWardrobeSnapshotParams.Clear();
        RefreshWardrobeParamSelectionDerived();
```

(If `SelectedProfile` is a property that already calls many `RaisePropertyChanged` for other reasons, just append the two new lines at the bottom of the setter body. Make sure the lines are inside the `if (SetProperty(...))` block — they should only fire when the profile actually changed.)

- [ ] **Step 3: Build to verify**

```powershell
dotnet build "E:\!!!Program to work on\Proper Crystal Relay\VrcTwitchOscBridge\VrcTwitchOscBridge.csproj" --no-restore
```

Expected: `Build succeeded. 0 Warning(s) 0 Error(s)`.

- [ ] **Step 4: Commit**

```powershell
git add "VrcTwitchOscBridge/ViewModels/AvatarSetsManagerViewModel.cs"
git commit -m "Clear wardrobe param selection on outfit/profile switch"
```

---

## Task 4: Update the param `ListBox` XAML — add multi-select, shortcuts, and context menu

**Files:**
- Modify: `AvatarSetsManagerWindow.xaml` — modify the param `ListBox` block (around line 1907), rewire 3 toolbar buttons (around line 1891-1904).

- [ ] **Step 1: Rewire the 3 toolbar buttons**

Find the toolbar `WrapPanel` (around line 1891-1904). The existing commands are `RemoveWardrobeSnapshotParamCommand`, `CopyWardrobeSnapshotParamCommand`, `PasteWardrobeSnapshotParamCommand`. Change `CopyWardrobeSnapshotParamCommand` → `CopySelectedWardrobeSnapshotParamsCommand` and `PasteWardrobeSnapshotParamCommand` → `PasteWardrobeSnapshotParamsCommand`. Leave `RemoveWardrobeSnapshotParamCommand` as-is (it keeps the same name).

Diff:
```xml
        <!-- BEFORE -->
        <Button Content="Remove"      Command="{Binding DataContext.RemoveWardrobeSnapshotParamCommand, RelativeSource={RelativeSource AncestorType=Window}}" />
        <Button Content="Copy"        Command="{Binding DataContext.CopyWardrobeSnapshotParamCommand, RelativeSource={RelativeSource AncestorType=Window}}" />
        <Button Content="Paste"       Command="{Binding DataContext.PasteWardrobeSnapshotParamCommand, RelativeSource={RelativeSource AncestorType=Window}}" />

        <!-- AFTER -->
        <Button Content="Remove"      Command="{Binding DataContext.RemoveWardrobeSnapshotParamCommand, RelativeSource={RelativeSource AncestorType=Window}}" />
        <Button Content="Copy"        Command="{Binding DataContext.CopySelectedWardrobeSnapshotParamsCommand, RelativeSource={RelativeSource AncestorType=Window}}" />
        <Button Content="Paste"       Command="{Binding DataContext.PasteWardrobeSnapshotParamsCommand, RelativeSource={RelativeSource AncestorType=Window}}" />
```

- [ ] **Step 2: Replace the param `ListBox` block with the new multi-select version**

Find the existing `ListBox` (around line 1906-1919). The existing block looks like:
```xml
                <ListBox ItemsSource="{Binding SnapshotParams}"
                         SelectedItem="{Binding DataContext.SelectedWardrobeSnapshotParam,
                                                RelativeSource={RelativeSource AncestorType=Window}}"
                         Background="{DynamicResource PanelBrush}"
                         Foreground="{DynamicResource TextBrush}"
                         MaxHeight="160"
                         Margin="0,0,0,6"
                         BorderBrush="{DynamicResource BorderBrush}">
                    <ListBox.ItemTemplate>
                        <DataTemplate>
                            <TextBlock Text="{Binding DisplaySummary}"
                                       Foreground="{DynamicResource TextBrush}"
                                       FontSize="11"
                                       Padding="4,2" />
                        </DataTemplate>
                    </ListBox.ItemTemplate>
                </ListBox>
```

Replace it with:
```xml
                <ListBox x:Name="WardrobeParamListBox"
                         ItemsSource="{Binding SnapshotParams}"
                         SelectionMode="Extended"
                         ToolTip="{loc:Translate 'Wardrobe Multi-Select Tooltip'}"
                         SelectedItem="{Binding DataContext.SelectedWardrobeSnapshotParam,
                                                RelativeSource={RelativeSource AncestorType=Window}}"
                         SelectionChanged="OnWardrobeParamListSelectionChanged"
                         KeyDown="OnWardrobeParamListKeyDown"
                         Background="{DynamicResource PanelBrush}"
                         Foreground="{DynamicResource TextBrush}"
                         MaxHeight="160"
                         Margin="0,0,0,6"
                         BorderBrush="{DynamicResource BorderBrush}">
                    <ListBox.InputBindings>
                        <KeyBinding Key="C" Modifiers="Ctrl"
                                    Command="{Binding DataContext.CopySelectedWardrobeSnapshotParamsCommand,
                                                      RelativeSource={RelativeSource AncestorType=Window}}" />
                        <KeyBinding Key="V" Modifiers="Ctrl"
                                    Command="{Binding DataContext.PasteWardrobeSnapshotParamsCommand,
                                                      RelativeSource={RelativeSource AncestorType=Window}}" />
                        <KeyBinding Key="A" Modifiers="Ctrl"
                                    Command="{Binding DataContext.SelectAllWardrobeSnapshotParamsCommand,
                                                      RelativeSource={RelativeSource AncestorType=Window}}" />
                        <KeyBinding Key="Delete"
                                    Command="{Binding DataContext.RemoveWardrobeSnapshotParamCommand,
                                                      RelativeSource={RelativeSource AncestorType=Window}}" />
                        <KeyBinding Key="Escape"
                                    Command="{Binding DataContext.ClearWardrobeSnapshotParamSelectionCommand,
                                                      RelativeSource={RelativeSource AncestorType=Window}}" />
                    </ListBox.InputBindings>
                    <ListBox.ContextMenu>
                        <ContextMenu>
                            <MenuItem Header="{loc:Translate 'Copy'}"
                                      Command="{Binding DataContext.CopySelectedWardrobeSnapshotParamsCommand,
                                                        RelativeSource={RelativeSource AncestorType=Window}}" />
                            <MenuItem Header="{loc:Translate 'Paste'}"
                                      Command="{Binding DataContext.PasteWardrobeSnapshotParamsCommand,
                                                        RelativeSource={RelativeSource AncestorType=Window}}" />
                            <Separator />
                            <MenuItem Header="{loc:Translate 'Select All'}"
                                      Command="{Binding DataContext.SelectAllWardrobeSnapshotParamsCommand,
                                                        RelativeSource={RelativeSource AncestorType=Window}}" />
                        </ContextMenu>
                    </ListBox.ContextMenu>
                    <ListBox.ItemTemplate>
                        <DataTemplate>
                            <TextBlock Text="{Binding DisplaySummary}"
                                       Foreground="{DynamicResource TextBrush}"
                                       FontSize="11"
                                       Padding="4,2" />
                        </DataTemplate>
                    </ListBox.ItemTemplate>
                </ListBox>
```

Note: the `Key="Escape"` literal in WPF `KeyBinding` uses the `Key` enum value. `System.Windows.Input.Key.Escape` is the correct name. In XAML, the bare string `"Escape"` is parsed to the `Key` enum by the type converter — this is the standard WPF idiom.

- [ ] **Step 3: Build to verify**

```powershell
dotnet build "E:\!!!Program to work on\Proper Crystal Relay\VrcTwitchOscBridge\VrcTwitchOscBridge.csproj" --no-restore
```

Expected: `Build succeeded. 0 Warning(s) 0 Error(s)`. The XAML now references the new command names, which exist after Task 2.

- [ ] **Step 4: Commit**

```powershell
git add "VrcTwitchOscBridge/AvatarSetsManagerWindow.xaml"
git commit -m "Wire wardrobe param list for multi-select + keyboard shortcuts"
```

---

## Task 5: Add the multi-select banner and toggle the editor's visibility

**Files:**
- Modify: `AvatarSetsManagerWindow.xaml` — add the banner between the param list and the editor; update the editor `Border`'s `Visibility`.

- [ ] **Step 1: Update the editor's `Visibility` to use the new property**

Find the editor `Border` block (around line 1921). The current `Visibility` is:
```xml
                <Border Visibility="{Binding DataContext.SelectedWardrobeOutfit, RelativeSource={RelativeSource AncestorType=Window}, Converter={StaticResource NullToVisibilityConverter}}"
```

Replace it with:
```xml
                <Border Visibility="{Binding DataContext.IsWardrobeParamEditorVisible, RelativeSource={RelativeSource AncestorType=Window}, Converter={StaticResource BoolToVisibilityConverter}, FallbackValue=Collapsed}"
```

The new `IsWardrobeParamEditorVisible` property (added in Task 1) handles both the "no outfit selected" and "2+ params selected" cases.

- [ ] **Step 2: Add the multi-select banner between the list and the editor**

Find the closing `</ListBox>` of the param list (from Task 4) and the opening `<Border>` of the editor. Insert this banner in between:

```xml
                <!-- Multi-select banner (visible when 2+ params are selected) -->
                <Border Margin="0,0,0,6"
                        Padding="10,6"
                        CornerRadius="6"
                        Background="{DynamicResource PanelHighlightBrush}"
                        BorderBrush="{DynamicResource AccentBrush}"
                        BorderThickness="1"
                        Visibility="{Binding DataContext.HasMultipleWardrobeSnapshotParamsSelected, RelativeSource={RelativeSource AncestorType=Window}, Converter={StaticResource BoolToVisibilityConverter}, FallbackValue=Collapsed}">
                    <StackPanel Orientation="Horizontal">
                        <TextBlock Text="{Binding DataContext.SelectedWardrobeSnapshotParamCount, RelativeSource={RelativeSource AncestorType=Window}}"
                                   Foreground="{DynamicResource AccentBrush}"
                                   FontWeight="SemiBold"
                                   FontSize="11" />
                        <TextBlock Margin="6,0,0,0"
                                   Text="{loc:Translate 'Wardrobe Multi-Select Banner'}"
                                   Foreground="{DynamicResource TextBrush}"
                                   FontSize="11" />
                    </StackPanel>
                </Border>
```

- [ ] **Step 3: Build to verify**

```powershell
dotnet build "E:\!!!Program to work on\Proper Crystal Relay\VrcTwitchOscBridge\VrcTwitchOscBridge.csproj" --no-restore
```

Expected: `Build succeeded. 0 Warning(s) 0 Error(s)`.

- [ ] **Step 4: Commit**

```powershell
git add "VrcTwitchOscBridge/AvatarSetsManagerWindow.xaml"
git commit -m "Add wardrobe multi-select banner; editor hidden when 2+ selected"
```

---

## Task 6: Add the code-behind handlers in `AvatarSetsManagerWindow.xaml.cs`

**Files:**
- Modify: `AvatarSetsManagerWindow.xaml.cs` — add two new event handlers.

- [ ] **Step 1: Read the existing handlers to match style**

```powershell
Get-Content "E:\!!!Program to work on\Proper Crystal Relay\VrcTwitchOscBridge\AvatarSetsManagerWindow.xaml.cs" | Select-Object -First 90
```

Expected: see `OnRuleItemClicked` and `OnOutfitItemClicked` around lines 60-76.

- [ ] **Step 2: Add the two new handlers right after `OnOutfitItemClicked` (around line 76)**

```csharp
        private void OnWardrobeParamListSelectionChanged(object sender, System.Windows.Controls.SelectionChangedEventArgs e)
        {
            if (sender is not System.Windows.Controls.ListBox lb || Vm is null) return;
            Vm.SyncWardrobeParamSelectionFromList(lb.SelectedItems.Cast<Models.WardrobeSnapshotParam>());
        }

        private void OnWardrobeParamListKeyDown(object sender, System.Windows.Input.KeyEventArgs e)
        {
            // Reserved for future custom shortcuts. Intentionally empty so the
            // ListBox.InputBindings (Ctrl+C/V/A, Delete, Esc) get first crack.
        }
```

The second handler is a stub for future expansion (e.g., a "duplicate" shortcut). WPF fires `InputBindings` before the routed `KeyDown` event, so leaving the handler empty does not swallow the bindings.

- [ ] **Step 3: Build to verify**

```powershell
dotnet build "E:\!!!Program to work on\Proper Crystal Relay\VrcTwitchOscBridge\VrcTwitchOscBridge.csproj" --no-restore
```

Expected: `Build succeeded. 0 Warning(s) 0 Error(s)`.

- [ ] **Step 4: Commit**

```powershell
git add "VrcTwitchOscBridge/AvatarSetsManagerWindow.xaml.cs"
git commit -m "Wire wardrobe param list selection-change + keydown handlers"
```

---

## Task 7: Add the four new keys to `en-US.extra.json`

**Files:**
- Modify: `VrcTwitchOscBridge/Resources/Localization/en-US.extra.json`

- [ ] **Step 1: Find a good insertion point**

```powershell
Select-String -Path "E:\!!!Program to work on\Proper Crystal Relay\VrcTwitchOscBridge\Resources\Localization\en-US.extra.json" -Pattern '"Reveal pairings: \{0\}"'
```

Expected: find the key at approximately line 806.

- [ ] **Step 2: Insert the four new keys after `"Reveal pairings: {0}"`**

Use a careful edit to add these 4 lines (preserving the existing 2-space indent and trailing comma on the line above):

```json
  "Reveal pairings: {0}": "Reveal pairings: {0}",
  "Wardrobe Multi-Select Banner": "params selected. Click one to edit, copy with Ctrl+C, or press Esc to clear.",
  "Wardrobe Multi-Select Tooltip": "Hold Ctrl to toggle, Shift to range, Ctrl+A to select all, Esc to clear.",
  "Wardrobe Paste Log": "Pasted {0} wardrobe param(s) into '{1}' (skipped {2} duplicate).",
  "Wardrobe Copy Log": "Copied {0} wardrobe param(s) to clipboard.",
```

The lines are inserted after the existing `"Reveal pairings: {0}"` line. Keep the existing 2-space indentation. Make sure the line you are inserting AFTER ends with a comma, and the new lines end with commas except the last one (unless it's the last key in the file, in which case it must NOT have a trailing comma to keep the JSON valid — check the next-line content before adding).

- [ ] **Step 3: Validate JSON**

```powershell
Get-Content "E:\!!!Program to work on\Proper Crystal Relay\VrcTwitchOscBridge\Resources\Localization\en-US.extra.json" | ConvertFrom-Json
```

Expected: no error. (If the file is huge, this will take a moment. If the command errors with a parse error, you have a JSON syntax issue — fix the trailing-comma placement.)

- [ ] **Step 4: Build (the `loc:Translate` extension will load the file at runtime; the build just verifies the C# compiles)**

```powershell
dotnet build "E:\!!!Program to work on\Proper Crystal Relay\VrcTwitchOscBridge\VrcTwitchOscBridge.csproj" --no-restore
```

Expected: `Build succeeded. 0 Warning(s) 0 Error(s)`.

- [ ] **Step 5: Commit**

```powershell
git add "VrcTwitchOscBridge/Resources/Localization/en-US.extra.json"
git commit -m "Add en-US strings for wardrobe multi-select banner and logs"
```

---

## Task 8: Add the four new keys to the 12 non-English locale files

For each of the 12 locale files below, add the same 4 keys with the translated value shown. Use the same insertion point pattern as Task 7 (after `"Reveal pairings: {0}"`). Match the existing 2-space indent. Verify JSON parses after each edit. Build only at the end of the task to keep the loop fast.

The keys (identical for every locale) are:
- `Wardrobe Multi-Select Banner`
- `Wardrobe Multi-Select Tooltip`
- `Wardrobe Paste Log`
- `Wardrobe Copy Log`

- [ ] **Step 1: `de-DE.extra.json` (German, informal `du`)**

```json
  "Wardrobe Multi-Select Banner": "Params ausgewählt. Klicke auf einen, um ihn zu bearbeiten, kopiere mit Strg+C, oder drücke Esc, um die Auswahl aufzuheben.",
  "Wardrobe Multi-Select Tooltip": "Halte Strg zum Umschalten, Shift für Bereich, Strg+A für alle, Esc zum Aufheben.",
  "Wardrobe Paste Log": "{0} Wardrobe-Param(s) in '{1}' eingefügt ({2} übersprungen, da doppelt vorhanden).",
  "Wardrobe Copy Log": "{0} Wardrobe-Param(s) in die Zwischenablage kopiert.",
```

- [ ] **Step 2: `es-ES.extra.json` (Spanish, informal `tú`)**

```json
  "Wardrobe Multi-Select Banner": "parámetros seleccionados. Haz clic en uno para editarlo, copia con Ctrl+C, o pulsa Esc para borrar la selección.",
  "Wardrobe Multi-Select Tooltip": "Mantén Ctrl para alternar, Shift para rango, Ctrl+A para todos, Esc para borrar.",
  "Wardrobe Paste Log": "Se pegaron {0} parámetro(s) de vestuario en '{1}' (se omitieron {2} duplicados).",
  "Wardrobe Copy Log": "Se copiaron {0} parámetro(s) de vestuario al portapapeles.",
```

- [ ] **Step 3: `fr-FR.extra.json` (French, informal `tu`)**

```json
  "Wardrobe Multi-Select Banner": "paramètres sélectionnés. Clique sur un pour le modifier, copie avec Ctrl+C, ou appuie sur Échap pour effacer la sélection.",
  "Wardrobe Multi-Select Tooltip": "Maintiens Ctrl pour basculer, Maj pour la plage, Ctrl+A pour tout, Échap pour effacer.",
  "Wardrobe Paste Log": "{0} paramètre(s) de tenue collé(s) dans '{1}' ({2} doublon(s) ignoré(s)).",
  "Wardrobe Copy Log": "{0} paramètre(s) de tenue copié(s) dans le presse-papiers.",
```

- [ ] **Step 4: `it-IT.extra.json` (Italian, informal `tu`)**

```json
  "Wardrobe Multi-Select Banner": "parametri selezionati. Cliccane uno per modificarlo, copia con Ctrl+C, o premi Esc per cancellare la selezione.",
  "Wardrobe Multi-Select Tooltip": "Tieni premuto Ctrl per alternare, Shift per intervallo, Ctrl+A per tutti, Esc per cancellare.",
  "Wardrobe Paste Log": "Incollati {0} parametro/i del guardaroba in '{1}' ({2} duplicato/i saltato/i).",
  "Wardrobe Copy Log": "Copiati {0} parametro/i del guardaroba negli appunti.",
```

- [ ] **Step 5: `ja-JP.extra.json` (Japanese, informal)**

```json
  "Wardrobe Multi-Select Banner": "件のパラメータを選択中。クリックして編集、Ctrl+C でコピー、Esc で選択解除できます。",
  "Wardrobe Multi-Select Tooltip": "Ctrl を押しながらクリックで切り替え、Shift で範囲選択、Ctrl+A で全選択、Esc で解除します。",
  "Wardrobe Paste Log": "'{1}' に {0} 件のワードローブパラメータを貼り付けました（{2} 件の重複をスキップ）。",
  "Wardrobe Copy Log": "{0} 件のワードローブパラメータをクリップボードにコピーしました。",
```

- [ ] **Step 6: `ko-KR.extra.json` (Korean, informal)**

```json
  "Wardrobe Multi-Select Banner": "개의 매개변수가 선택됨. 하나를 클릭하여 편집하거나, Ctrl+C로 복사, Esc로 선택을 해제하세요.",
  "Wardrobe Multi-Select Tooltip": "Ctrl을 누르며 토글, Shift로 범위 선택, Ctrl+A로 모두 선택, Esc로 선택 해제.",
  "Wardrobe Paste Log": "'{1}'에 {0}개의 워드로브 매개변수를 붙여넣음 ({2}개 중복 건너뜀).",
  "Wardrobe Copy Log": "{0}개의 워드로브 매개변수를 클립보드에 복사함.",
```

- [ ] **Step 7: `pl-PL.extra.json` (Polish, informal `ty`)**

```json
  "Wardrobe Multi-Select Banner": "parametrów zaznaczonych. Kliknij jeden, aby edytować, skopiuj za pomocą Ctrl+C, lub naciśnij Esc, aby wyczyścić zaznaczenie.",
  "Wardrobe Multi-Select Tooltip": "Przytrzymaj Ctrl, aby przełączać, Shift dla zakresu, Ctrl+A dla wszystkich, Esc aby wyczyścić.",
  "Wardrobe Paste Log": "Wklejono {0} parametr(ów) garderoby do '{1}' (pominięto {2} duplikatów).",
  "Wardrobe Copy Log": "Skopiowano {0} parametr(ów) garderoby do schowka.",
```

- [ ] **Step 8: `pt-BR.extra.json` (Portuguese-BR, informal)**

```json
  "Wardrobe Multi-Select Banner": "parâmetros selecionados. Clique em um para editar, copie com Ctrl+C, ou pressione Esc para limpar a seleção.",
  "Wardrobe Multi-Select Tooltip": "Segure Ctrl para alternar, Shift para intervalo, Ctrl+A para todos, Esc para limpar.",
  "Wardrobe Paste Log": "Colados {0} parâmetro(s) de guarda-roupa em '{1}' (ignorados {2} duplicado(s)).",
  "Wardrobe Copy Log": "Copiados {0} parâmetro(s) de guarda-roupa para a área de transferência.",
```

- [ ] **Step 9: `ru-RU.extra.json` (Russian, informal `ты`)**

```json
  "Wardrobe Multi-Select Banner": "параметров выбрано. Нажми на один, чтобы изменить, скопируй с помощью Ctrl+C, или нажми Esc, чтобы снять выделение.",
  "Wardrobe Multi-Select Tooltip": "Удерживай Ctrl для переключения, Shift для диапазона, Ctrl+A для всех, Esc для снятия.",
  "Wardrobe Paste Log": "Вставлено {0} параметр(ов) гардероба в '{1}' (пропущено {2} дубликатов).",
  "Wardrobe Copy Log": "Скопировано {0} параметр(ов) гардероба в буфер обмена.",
```

- [ ] **Step 10: `sv-SE.extra.json` (Swedish, informal `du`)**

```json
  "Wardrobe Multi-Select Banner": "parametrar valda. Klicka på en för att redigera, kopiera med Ctrl+C, eller tryck Esc för att rensa valet.",
  "Wardrobe Multi-Select Tooltip": "Håll Ctrl för att växla, Shift för intervall, Ctrl+A för alla, Esc för att rensa.",
  "Wardrobe Paste Log": "Klistrade in {0} garderobsparameter/-ar i '{1}' (hoppade över {2} dubbletter).",
  "Wardrobe Copy Log": "Kopierade {0} garderobsparameter/-ar till urklipp.",
```

- [ ] **Step 11: `th-TH.extra.json` (Thai, informal)**

```json
  "Wardrobe Multi-Select Banner": "พารามิเตอร์ที่เลือก คลิกหนึ่งรายการเพื่อแก้ไข คัดลอกด้วย Ctrl+C หรือกด Esc เพื่อล้างการเลือก",
  "Wardrobe Multi-Select Tooltip": "กด Ctrl ค้างเพื่อสลับ Shift สำหรับช่วง Ctrl+A สำหรับทั้งหมด Esc เพื่อล้าง",
  "Wardrobe Paste Log": "วาง {0} พารามิเตอร์ตู้เสื้อผ้าใน '{1}' (ข้าม {2} รายการที่ซ้ำ)",
  "Wardrobe Copy Log": "คัดลอก {0} พารามิเตอร์ตู้เสื้อผ้าไปยังคลิปบอร์ด",
```

- [ ] **Step 12: `zh-CN.extra.json` (Chinese Simplified, informal)**

```json
  "Wardrobe Multi-Select Banner": "个参数已选中。点击单个进行编辑，使用 Ctrl+C 复制，或按 Esc 清除选择。",
  "Wardrobe Multi-Select Tooltip": "按住 Ctrl 切换选择，Shift 范围选择，Ctrl+A 全选，Esc 清除选择。",
  "Wardrobe Paste Log": "已将 {0} 个衣橱参数粘贴到 '{1}'（跳过 {2} 个重复项）。",
  "Wardrobe Copy Log": "已将 {0} 个衣橱参数复制到剪贴板。",
```

- [ ] **Step 13: `zh-TW.extra.json` (Chinese Traditional, informal)**

```json
  "Wardrobe Multi-Select Banner": "個參數已選取。點選單個進行編輯，使用 Ctrl+C 複製，或按 Esc 清除選取。",
  "Wardrobe Multi-Select Tooltip": "按住 Ctrl 切換選取，Shift 範圍選取，Ctrl+A 全選，Esc 清除選取。",
  "Wardrobe Paste Log": "已將 {0} 個衣櫥參數貼到 '{1}'（略過 {2} 個重複項）。",
  "Wardrobe Copy Log": "已將 {0} 個衣櫥參數複製到剪貼簿。",
```

- [ ] **Step 14: Build + run the localization audit**

```powershell
dotnet build "E:\!!!Program to work on\Proper Crystal Relay\VrcTwitchOscBridge\VrcTwitchOscBridge.csproj" --no-restore
dotnet run --project "E:\!!!Program to work on\Proper Crystal Relay\LocalizationAudit\LocalizationAudit.csproj" --no-restore
```

Expected:
- `Build succeeded. 0 Warning(s) 0 Error(s)`.
- Localization audit output lists all 13 locales with `0 missing` for the 4 new keys. The audit will report "likely untranslated" warnings for the new translations — that is expected and acceptable. The audit must NOT report the 4 new keys in any locale's "missing" list.

If a locale is missing the new keys, re-open that locale file, ensure the keys are present and the JSON parses, and re-run the audit.

- [ ] **Step 15: Commit**

```powershell
git add "VrcTwitchOscBridge/Resources/Localization/"
git commit -m "Add wardrobe multi-select strings for 12 non-English locales"
```

---

## Task 9: Final verification — full build + localization audit + manual smoke test

**Files:** none modified in this task.

- [ ] **Step 1: Clean build**

```powershell
dotnet build "E:\!!!Program to work on\Proper Crystal Relay\VrcTwitchOscBridge\VrcTwitchOscBridge.csproj" --no-restore
```

Expected: `Build succeeded. 0 Warning(s) 0 Error(s)`.

- [ ] **Step 2: Localization audit (must pass with no missing keys)**

```powershell
dotnet run --project "E:\!!!Program to work on\Proper Crystal Relay\LocalizationAudit\LocalizationAudit.csproj" --no-restore 2>&1 | Select-String -Pattern "missing"
```

Expected: the 4 new keys (`Wardrobe Multi-Select Banner`, `Wardrobe Multi-Select Tooltip`, `Wardrobe Paste Log`, `Wardrobe Copy Log`) do NOT appear in any locale's missing list. Existing pre-feature missing keys may still be reported — that is outside this plan's scope.

- [ ] **Step 3: Launch the debug build and run the manual smoke test from spec section 5d**

Launch:
```powershell
& "E:\!!!Program to work on\Proper Crystal Relay\Launch-Crystal-Relay-Debug.bat"
```

Run through the 11 smoke-test steps from `docs/superpowers/specs/2026-06-13-wardrobe-multiselect-copypaste-design.md` section 5d. Verify each step:

1. Create outfit A with 3 params, outfit B with 1 sharing a name.
2. Click param 1 in A, Ctrl+click param 3 in A. Banner shows `2 params selected`, editor hidden.
3. Press Ctrl+C. Log shows `Copied 2 wardrobe param(s) to clipboard.`
4. Click outfit B. Selection cleared, editor reappears.
5. Press Ctrl+V. B's list grows by 2, at the end. Log shows `Pasted 2 wardrobe param(s) into 'B' (skipped 0 duplicate).`
6. Press Ctrl+A on B's list. All selected, banner shows count, editor hidden.
7. Press Delete. All selected removed.
8. Add a new param, select it, press Esc. Selection cleared, editor reappears.
9. Right-click a param, choose Copy, then Paste. Same behavior.
10. Switch profile, pick an outfit, Ctrl+V. Cross-profile paste works, log shows destination outfit.
11. Close and reopen the Avatar Sets Manager window. Clipboard is empty (expected).

- [ ] **Step 4: If any smoke-test step fails, fix and commit before declaring done**

The fix is most likely in the ViewModel (Task 1-3) or XAML (Task 4-5). Re-build and re-test after the fix. Add a commit with the fix using the `fix:` prefix.

- [ ] **Step 5: No new commit if everything passed**

If all steps pass, the plan is complete. The series of feature commits already form a clean history. No final "done" commit is needed.

---

## Self-Review

**Spec coverage:**
- §1 Architecture — Task 1 (state + derived props), Task 2 (commands), Task 4 (ListBox wiring), Task 5 (banner)
- §2 Data model and ViewModel — Task 1 (properties), Task 2 (commands + impls), Task 3 (setter clears)
- §3 UI changes — Task 4 (ListBox + shortcuts + context menu + toolbar rewire), Task 5 (banner + editor visibility), Task 6 (code-behind)
- §4 Edge cases and behavior — Task 2 (skip on conflict, append at end, source order), Task 3 (clear on outfit/profile switch), Task 4 (Ctrl+A/Shift/Ctrl+Click via Extended mode, Delete/Esc bindings)
- §5 Localization, logging, testing — Task 7 (en-US), Task 8 (12 locales), Task 9 (build + audit + manual smoke test)

**Spec deviation note:** The spec mentioned adding an `InverseBoolConverter` to `Converters.cs` for the editor's `Visibility`. The plan uses a simpler approach: a derived `IsWardrobeParamEditorVisible` property on the ViewModel that already factors in both the outfit-null check and the multi-select check, bound with the existing `BoolToVisibilityConverter`. This avoids adding a new converter for a one-line check, and keeps the visibility logic in the ViewModel where it belongs.

**Placeholder scan:** No "TBD" / "TODO" / "fill in" in any task. All code blocks contain exact content. All translated strings are concrete text.

**Type consistency:**
- `SelectedWardrobeSnapshotParams` (collection property) — defined Task 1, used Tasks 1, 2, 3, 5, 6
- `CopySelectedWardrobeSnapshotParamsCommand` — declared Task 2, used Tasks 2, 4
- `PasteWardrobeSnapshotParamsCommand` — declared Task 2, used Tasks 2, 4
- `SelectAllWardrobeSnapshotParamsCommand` — declared Task 2, used Task 4
- `ClearWardrobeSnapshotParamSelectionCommand` — declared Task 2, used Task 4
- `SyncWardrobeParamSelectionFromList` — added Task 2, called Task 6
- `RefreshWardrobeParamSelectionDerived` — added Task 1, called Tasks 1, 2, 3
- `IsWardrobeParamEditorVisible` — added Task 1, used Task 5
- `OnWardrobeParamListSelectionChanged` / `OnWardrobeParamListKeyDown` — added Task 6, used Task 4
- `Wardrobe Multi-Select Banner` / `Tooltip` / `Wardrobe Paste Log` / `Wardrobe Copy Log` — used Tasks 4, 5, 7, 8

All names match across tasks. No drift.

**Builds green after Task 2 onward.** Task 1 expects a red build because it adds calls to commands that aren't declared yet — this is the planned intermediate state and Task 2 fixes it immediately. Task 3 onward is fully green.
