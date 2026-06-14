# Wardrobe Param Multi-Select + Copy/Paste Set

- **Date:** 2026-06-13
- **Status:** Draft, pending user review
- **Project:** Crystal Relay (VrcTwitchOscBridge)
- **Scope:** Avatar Sets Manager → Wardrobe editor → Outfit parameter list

## Problem

The Avatar Sets Manager wardrobe editor has a single-item Copy / Paste for `WardrobeSnapshotParam`. Streamers building outfits need to replicate a small **set** of param toggles (e.g. "hat, shirt, shoes") across many outfits. Today that means: copy one param, switch outfit, paste, copy the next, switch back, paste — many round-trips. The selection is hard-coded to `Single`, there is no keyboard shortcut, and the clipboard is a single nullable field.

## Goal

Add **Ctrl+click / Shift+click multi-select** of params in an outfit's param list, plus **Copy / Paste of the whole selected set** into another outfit, so a streamer can build a toggle palette once and stamp it onto many outfits. Cross-profile paste is allowed. Editor gracefully shows a banner when multiple items are selected.

## Non-goals (YAGNI)

- Multi-edit of N selected params (no "edit common properties" panel).
- Drag-to-reorder of params within the list.
- System clipboard (`System.Windows.Clipboard`) — keep the in-memory model.
- Undo/redo for the wardrobe editor.
- Persisting the clipboard across app restarts.

---

## 1. Architecture

**Key idea:** Reuse the existing `CloneWardrobeSnapshotParam` helper and the existing toolbar button locations. The clipboard becomes an `ObservableCollection<WardrobeSnapshotParam>` (was a single nullable). The param `ListBox` gets `SelectionMode=Extended` plus a mirrored `SelectedWardrobeSnapshotParams` collection on the ViewModel so commands can iterate the selection. Keyboard shortcuts are wired via `ListBox.InputBindings` so they only fire when the list has focus.

### What stays the same
- Model classes (`WardrobeOutfit`, `WardrobeSnapshotParam`) — untouched
- Save/load path (per-param state is already persisted; the new clipboard is in-memory only)
- Toolbar button positions and captions
- The single-item editor below the list (still binds to `SelectedWardrobeSnapshotParam`)
- Cross-profile paste works for free (the in-memory clipboard is per-window ViewModel)
- `CloneWardrobeSnapshotParam` (line 1506) is reused unchanged

### What's new
- `ObservableCollection<WardrobeSnapshotParam> SelectedWardrobeSnapshotParams` on the ViewModel (mirrors `ListBox.SelectedItems`)
- `ObservableCollection<WardrobeSnapshotParam> _copiedWardrobeSnapshotParams` (was single nullable)
- New commands: `CopySelectedWardrobeSnapshotParamsCommand`, `PasteWardrobeSnapshotParamsCommand`, `SelectAllWardrobeSnapshotParamsCommand`, `ClearWardrobeSnapshotParamSelectionCommand`
- `ListBox.SelectionMode=Extended` + derived flags for the banner
- `InputBindings` for `Ctrl+C`, `Ctrl+V`, `Ctrl+A`, `Delete`, `Esc` on the param list
- A small banner that shows `N params selected` and hides the editor when count > 1
- `RemoveWardrobeSnapshotParamCommand` updated to remove all selected (was single)
- New localized strings (one new key per concern, mirrored in every locale)

---

## 2. Data model and ViewModel changes

**No model changes.** `WardrobeOutfit` and `WardrobeSnapshotParam` stay untouched. The clipboard is in-memory only and is not persisted, so save/load is unaffected.

### ViewModel changes in `ViewModels/AvatarSetsManagerViewModel.cs`

Replace the single-item clipboard state with collection versions:

```csharp
// REMOVE (line 275)
private Models.WardrobeSnapshotParam? _copiedWardrobeSnapshotParam;

// ADD
private readonly ObservableCollection<Models.WardrobeSnapshotParam> _copiedWardrobeSnapshotParams = [];
public ObservableCollection<Models.WardrobeSnapshotParam> CopiedWardrobeSnapshotParams => _copiedWardrobeSnapshotParams;
public int CopiedWardrobeSnapshotParamCount => _copiedWardrobeSnapshotParams.Count;
public bool HasCopiedWardrobeSnapshotParams => _copiedWardrobeSnapshotParams.Count > 0;

public ObservableCollection<Models.WardrobeSnapshotParam> SelectedWardrobeSnapshotParams { get; } = [];
public int SelectedWardrobeSnapshotParamCount => SelectedWardrobeSnapshotParams.Count;
public bool HasMultipleWardrobeSnapshotParamsSelected => SelectedWardrobeSnapshotParams.Count > 1;
public bool HasAnyWardrobeSnapshotParamSelected => SelectedWardrobeSnapshotParams.Count > 0;
```

### Command changes

```csharp
// REMOVE
CopyWardrobeSnapshotParamCommand         (line 160, 708)
PasteWardrobeSnapshotParamCommand        (line 161, 709)

// ADD
CopySelectedWardrobeSnapshotParamsCommand = new RelayCommand(CopySelectedWardrobeSnapshotParams,
    () => SelectedWardrobeSnapshotParams.Count > 0);
PasteWardrobeSnapshotParamsCommand = new RelayCommand(PasteWardrobeSnapshotParams,
    () => SelectedWardrobeOutfit is not null && _copiedWardrobeSnapshotParams.Count > 0);
SelectAllWardrobeSnapshotParamsCommand = new RelayCommand(SelectAllWardrobeSnapshotParams,
    () => SelectedWardrobeOutfit is not null);
ClearWardrobeSnapshotParamSelectionCommand = new RelayCommand(ClearWardrobeSnapshotParamSelection,
    () => SelectedWardrobeSnapshotParams.Count > 0);
```

`RemoveWardrobeSnapshotParamCommand` keeps the same name and external signature but its body changes: it removes every param in `SelectedWardrobeSnapshotParams` (highest index first to keep indices valid), then clears the selection. `CanExecute` becomes `SelectedWardrobeSnapshotParams.Count > 0`.

### New command implementations

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

public void SyncWardrobeParamSelectionFromList(IEnumerable<WardrobeSnapshotParam> newSelection)
{
    SelectedWardrobeSnapshotParams.Clear();
    foreach (var p in newSelection) SelectedWardrobeSnapshotParams.Add(p);
    RefreshWardrobeParamSelectionDerived();
}

private void ClearWardrobeSnapshotParamSelection()
{
    SelectedWardrobeSnapshotParams.Clear();
    RefreshWardrobeParamSelectionDerived();
}

private void RefreshWardrobeParamSelectionDerived()
{
    RaisePropertyChanged(nameof(SelectedWardrobeSnapshotParamCount));
    RaisePropertyChanged(nameof(HasMultipleWardrobeSnapshotParamsSelected));
    RaisePropertyChanged(nameof(HasAnyWardrobeSnapshotParamSelected));
    RemoveWardrobeSnapshotParamCommand.NotifyCanExecuteChanged();
    CopySelectedWardrobeSnapshotParamsCommand.NotifyCanExecuteChanged();
    ClearWardrobeSnapshotParamSelectionCommand.NotifyCanExecuteChanged();
    SelectAllWardrobeSnapshotParamsCommand.NotifyCanExecuteChanged();
}
```

### Selection-clearing hooks

The `SelectedWardrobeOutfit` setter (line 304) must call `SelectedWardrobeSnapshotParams.Clear()` + `RefreshWardrobeParamSelectionDerived()` so switching outfits starts with an empty selection. The `SelectedProfile` setter must do the same so profile switching cascades. Adding these to the existing setters does not affect any other behavior.

`AppendLog` and `T` (the localization helper) are already used throughout the file. `CloneWardrobeSnapshotParam` (line 1506) is reused unchanged.

---

## 3. UI changes

All changes are in `AvatarSetsManagerWindow.xaml` (wardrobe section starts at line 1548, param list at line 1907, param editor at line 1921) and `AvatarSetsManagerWindow.xaml.cs`.

### 3a. Param ListBox (around line 1907)

```xaml
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

The `ContextMenu` is a small bonus for mouse users: right-click on a param gets Copy / Paste / Select All.

### 3b. Multi-select banner

Sits between the list and the editor panel. Visible only when 2+ are selected. Editor panel swaps visibility with the banner.

```xaml
<!-- Multi-select banner (visible when 2+ params are selected) -->
<Border Margin="0,0,0,6"
        Padding="10,6"
        CornerRadius="6"
        Background="{DynamicResource PanelHighlightBrush}"
        BorderBrush="{DynamicResource AccentBrush}"
        BorderThickness="1"
        Visibility="{Binding DataContext.HasMultipleWardrobeSnapshotParamsSelected,
                             RelativeSource={RelativeSource AncestorType=Window},
                             Converter={StaticResource BoolToVisibilityConverter}}">
    <StackPanel Orientation="Horizontal">
        <TextBlock Text="{Binding DataContext.SelectedWardrobeSnapshotParamCount,
                                  RelativeSource={RelativeSource AncestorType=Window}}"
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

The count is bound in its own `TextBlock` (no `StringFormat` placeholder needed in the localized string), then the localized text follows. Renders as: `2 params selected. Click one to edit, copy with Ctrl+C, or press Esc to clear.`

The existing param editor `Border` (line 1921) gets a new outer visibility that hides it when `HasMultipleWardrobeSnapshotParamsSelected` is true. A small `InverseBoolConverter` is added to `Converters.cs` (~10 lines) so the existing `BoolToVisibilityConverter` can be chained off the inverted value.

### 3c. Toolbar buttons (lines 1891–1904)

Captions stay the same; commands rewire:

```xaml
<Button Content="+ Add Param" Command="{Binding ... AddWardrobeSnapshotParamCommand}" />
<Button Content="Remove"      Command="{Binding ... RemoveWardrobeSnapshotParamCommand}" />
<Button Content="Copy"        Command="{Binding ... CopySelectedWardrobeSnapshotParamsCommand}" />
<Button Content="Paste"       Command="{Binding ... PasteWardrobeSnapshotParamsCommand}" />
<Button Content="Refresh"     Command="{Binding ... RefreshWardrobeParametersCommand}" />
<Button Content="Test Outfit" Command="{Binding ... TestWardrobeOutfitCommand}" />
```

### 3d. Code-behind (`AvatarSetsManagerWindow.xaml.cs`)

```csharp
private void OnWardrobeParamListSelectionChanged(object sender, SelectionChangedEventArgs e)
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

### 3e. No changes to
- The list item template (still a single `TextBlock` — standard ListBox highlight is enough)
- The editor panel layout (only its outer `Visibility` changes)
- The `+ Add Param`, `Refresh`, `Test Outfit` buttons

---

## 4. Edge cases and behavior

| Case | Behavior |
|---|---|
| Copy with 0 selected | Button disabled; Ctrl+C no-op (Command `CanExecute=false`) |
| Paste with no destination outfit | Button disabled |
| Paste with empty clipboard | Button disabled |
| Paste with a 1-param clipboard | Works identically to today's single-item paste (clones, appends to end). No regression. |
| Click a param while multi-select is active | The click toggles the row (Ctrl-click semantics under `SelectionMode=Extended`). To edit a single param, the user just clicks an unselected row. |
| Source outfit deleted while params are on the clipboard | Clones are deep-copied at Copy time; no stale-reference risk. |
| Order preservation on paste | Params append in the order they appeared in the source list, not the click order. Only deterministic order. |
| Skipped-on-conflict feedback | Activity log line: `Pasted N wardrobe param(s) into 'Outfit' (skipped M duplicate).` Single log line, no popup. |
| Empty / whitespace `ParameterName` in source | Silently skipped, counted in the `skipped` total. |
| Remove with 1 selected | Identical to today. |
| Remove with 2+ selected | All selected removed highest-index-first, then selection cleared. |
| Switch outfit | `SelectedWardrobeOutfit` setter clears the param selection so the new outfit starts fresh. |
| Switch avatar profile | `SelectedProfile` setter clears the param selection so the new profile starts fresh. |
| Wardrobe-mode-disabled profile | The whole wardrobe section is hidden, so the buttons are unreachable. No extra guard. |
| Undo | None. User can re-Copy and re-Paste. Adding undo is a separate feature. |
| Clipboard persistence | None. In-memory only. Cleared on window close. |
| Thread safety | All access is on the UI thread (WPF). |

### Behavior decisions (from brainstorming)
- **Conflict resolution:** Skip — leave destination untouched for that name. (Safest, no data loss, no duplicates.)
- **Paste position:** Always append to end of destination, in source order. (Predictable, matches "paste the whole set" wording.)
- **Selection model:** Standard list selection — Ctrl+click toggles, Shift+click ranges, Ctrl+A all, Esc clears. (Familiar to Windows users.)
- **Editor with multi-select:** Show a "N selected" banner, hide editor. (Cleanest, least surprising.)
- **Copy replaces or accumulates:** Replace — Copy overwrites the clipboard with the current selection. (Standard clipboard behavior.)
- **Cross-profile paste:** Allowed — paste into any outfit in the same window. (The in-memory clipboard is per-window ViewModel.)

---

## 5. Localization, logging, testing

### 5a. New localization keys

Added to `en-US.extra.json` and mirrored in every other `*.extra.json` per the localization rules:

| Key | English | Placeholders |
|---|---|---|
| `Wardrobe Multi-Select Banner` | `params selected. Click one to edit, copy with Ctrl+C, or press Esc to clear.` | none (count is bound separately in XAML) |
| `Wardrobe Multi-Select Tooltip` | `Hold Ctrl to toggle, Shift to range, Ctrl+A to select all, Esc to clear.` | none |
| `Wardrobe Paste Log` | `Pasted {0} wardrobe param(s) into '{1}' (skipped {2} duplicate).` | `{0}` = added, `{1}` = outfit name, `{2}` = skipped |
| `Wardrobe Copy Log` | `Copied {0} wardrobe param(s) to clipboard.` | `{0}` = count |

Each non-English `*.extra.json` gets a translated variant following the existing tone/voice (informal `du`/`tú`/`tu`, brand terms in English). The localization audit runs in the existing build pipeline and fails the build if any are missing — that's the safety net.

### 5b. Logging

All copy/paste actions go through `AppendLog` (already used everywhere in the wardrobe editor). The log line shows the outfit name, count pasted, count skipped — paper trail in the activity panel. No new logging infrastructure needed.

### 5c. Build verification

After code changes:
- `dotnet build VrcTwitchOscBridge.csproj --no-restore` — must succeed with 0 warnings, 0 errors.
- `dotnet run --project LocalizationAudit/LocalizationAudit.csproj --no-restore` — must pass for the new keys (no missing, no empty, placeholders intact).

### 5d. Manual smoke-test checklist

The codebase has no WPF UI test harness; the wardrobe editor is tested by hand.

1. Open Avatar Sets Manager → Wardrobe mode → create outfit A with 3 params, create outfit B with 1 param sharing a name with one of A's.
2. Click param 1 in A. Ctrl+click param 3 in A. Confirm both are highlighted, banner shows `2 params selected`, editor is hidden.
3. Press Ctrl+C. Log shows `Copied 2 wardrobe param(s) to clipboard.`
4. Click outfit B. Param list clears selection, editor reappears for B's param.
5. Press Ctrl+V. B's list grows by 2 (the non-conflicting ones), at the end. Log shows `Pasted 2 wardrobe param(s) into 'B' (skipped 0 duplicate).`
6. Press Ctrl+A on B's list. All of B's params are selected, banner shows the count, editor hidden.
7. Press Delete. All selected are removed, list is empty.
8. Add a new param, select it, press Esc — selection cleared, editor reappears.
9. Right-click a param, choose Copy from the context menu, then Paste. Same behavior as the toolbar buttons.
10. Switch to a different avatar profile, pick an outfit, press Ctrl+V. Paste works across profiles. Log shows the destination outfit name.
11. Close and reopen the Avatar Sets Manager window. Clipboard is empty (expected — in-memory only).

### 5e. Files touched

| File | Change |
|---|---|
| `ViewModels/AvatarSetsManagerViewModel.cs` | Replace 2 commands, add 4 commands, add 5 properties, update `SelectedWardrobeOutfit` and `SelectedProfile` setters to clear selection, update `RemoveWardrobeSnapshotParam` to handle multi, add `SyncWardrobeParamSelectionFromList`, refactor copy/paste impls |
| `AvatarSetsManagerWindow.xaml` | Add `x:Name`, `SelectionMode`, `SelectionChanged`, `KeyDown`, `InputBindings`, `ContextMenu` to param `ListBox`; add multi-select banner `Border`; add inverse-bool converter resource if not present; rewire 3 toolbar button commands |
| `AvatarSetsManagerWindow.xaml.cs` | Add `OnWardrobeParamListSelectionChanged` and stub `OnWardrobeParamListKeyDown` handlers |
| `Converters.cs` | Add `InverseBoolConverter` (~10 lines) if not already present |
| `Resources/Localization/en-US.extra.json` | Add 4 new keys |
| `Resources/Localization/<locale>.extra.json` × 11 locales | Add translated variants for each new key |

No model file changes, no new model classes, no new dependencies.
