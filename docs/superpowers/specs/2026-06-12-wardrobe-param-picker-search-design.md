# Wardrobe Parameter Picker Search Design

**Date:** 2026-06-12
**Status:** Design approved, ready for implementation plan
**Active development build:** 3.1.9 (beta2)
**Related spec:** `2026-06-12-wardrobe-editor-migration-design.md` (the broader migration that landed this picker)

## Goal

Replace the wardrobe `Parameter Path` editable `ComboBox` (inside `AvatarSetsManagerWindow.xaml` Step 4) with a search TextBox + scrollable filtered list of clickable parameters. Type to filter the avatar's OSC parameters, click a result to populate the wardrobe param's `ParameterName` and `ParameterType`.

## Why

The current `ComboBox` is editable but its dropdown is cramped for avatars with 50+ parameters. There's no real-time type-to-filter, so users scroll. The rule parameter picker in the same manager already uses the right pattern (search TextBox + filtered list of buttons) — the wardrobe is the outlier. Aligning the wardrobe picker with the rule picker reduces cognitive friction and makes the editor usable on complex avatars.

## Non-Goals

- No persistence changes
- No new converters
- No new files (only modifications to existing XAML and code-behind)
- No changes to the rule parameter picker (the one at `AvatarSetsManagerWindow.xaml:1150-1209` stays exactly as it is)
- No changes to `AvailableWardrobeParameters` field semantics — it still exists and still powers the typed-text resolution path
- No changes to the `SelectedWardrobeParameterOption` / `SelectedWardrobeSnapshotParam` two-way binding chain
- No runtime changes — the bridge executor reads `ParameterName` and `ParameterType` from the snapshot, those don't change

## High-Level Architecture

Mirror the rule parameter picker (`AvatarSetsManagerWindow.xaml:1150-1209`) exactly, with the XAML element names prefixed `Wardrobe` and the data target swapped from `SelectedAvatarRule` to `SelectedWardrobeSnapshotParam`. Add one new VM field (`WardrobeParameterNameFilter`), one new public property, one new read-only list property (`FilteredWardrobeParameters`), and one new helper (`ApplyWardrobeParameterFilter`). Modify one existing helper (`RefreshWardrobeParameterOptions`) to call the new filter helper instead of setting `AvailableWardrobeParameters` directly.

**Data flow:**
- User types in search TextBox → `WardrobeParameterNameFilter` setter → `ApplyWardrobeParameterFilter()` runs → updates `FilteredWardrobeParameters` (binds to the list) and `AvailableWardrobeParameters` (binds to the existing `SelectedWardrobeParameterOption` machinery)
- User clicks a result in the list → `OnWardrobeParameterItemClicked` code-behind → reads `param` from `SelectedWardrobeSnapshotParam` and `p` from `btn.Tag` → sets `ParameterName` and `ParameterType` → clears filter
- User types a custom path → `WardrobeParameterText` setter (existing) → `CommitWardrobeParameterText` (existing) → resolves against source list (existing) → if no match, leaves the typed text in place (no warning)
- User changes `ParameterType` ComboBox → existing `ParameterType` setter → `SelectedWardrobeSnapshotParamChanged` → `RefreshWardrobeParameterOptions` → `ApplyWardrobeParameterFilter` → list updates

## Section 1: XAML changes

**File:** `VrcTwitchOscBridge\AvatarSetsManagerWindow.xaml`

**Replace lines 1737-1747** (the existing wardrobe `Parameter Path` ComboBox) with the new search-and-list block.

**Current block to replace:**
```xaml
<ComboBox IsEditable="True"
          TextSearch.TextPath="DisplayLabel"
          DisplayMemberPath="DisplayLabel"
          ItemsSource="{Binding DataContext.AvailableWardrobeParameters, RelativeSource={RelativeSource AncestorType=Window}}"
          SelectedItem="{Binding DataContext.SelectedWardrobeParameterOption, RelativeSource={RelativeSource AncestorType=Window}}"
          Text="{Binding DataContext.WardrobeParameterText, RelativeSource={RelativeSource AncestorType=Window}, UpdateSourceTrigger=PropertyChanged}" />
<TextBlock Text="Pick from list or type a custom path. Auto-detects Bool/Int/Float type."
           Foreground="{DynamicResource MutedBrush}"
           FontSize="9"
           TextWrapping="Wrap"
           Margin="0,2,0,6" />
```

**New block:**
```xaml
<Grid Margin="0,0,0,4">
    <Grid.ColumnDefinitions>
        <ColumnDefinition Width="*" />
        <ColumnDefinition Width="Auto" />
    </Grid.ColumnDefinitions>
    <TextBox Grid.Column="0"
             x:Name="WardrobeParameterNameTextBox"
             Text="{Binding DataContext.WardrobeParameterNameFilter, RelativeSource={RelativeSource AncestorType=Window}, UpdateSourceTrigger=PropertyChanged}"
             TextChanged="OnWardrobeParameterNameTextChanged" />
    <Button Grid.Column="1"
            Content="⟳"
            Style="{StaticResource SecondaryButtonStyle}"
            Padding="8,4"
            Margin="6,0,0,0"
            Command="{Binding DataContext.RefreshWardrobeParametersCommand, RelativeSource={RelativeSource AncestorType=Window}}"
            ToolTip="Refresh parameters from avatar OSC file" />
</Grid>
<Border Background="{DynamicResource InputBrush}"
        BorderBrush="{DynamicResource BorderBrush}"
        BorderThickness="1"
        CornerRadius="4"
        MaxHeight="140">
    <ScrollViewer VerticalScrollBarVisibility="Auto">
        <ItemsControl ItemsSource="{Binding DataContext.FilteredWardrobeParameters, RelativeSource={RelativeSource AncestorType=Window}}">
            <ItemsControl.ItemTemplate>
                <DataTemplate>
                    <Button BorderBrush="{DynamicResource BorderBrush}"
                            BorderThickness="0,0,0,1"
                            Background="Transparent"
                            Padding="6,4"
                            Cursor="Hand"
                            HorizontalContentAlignment="Stretch"
                            HorizontalAlignment="Stretch"
                            Click="OnWardrobeParameterItemClicked"
                            Tag="{Binding}">
                        <Button.Template>
                            <ControlTemplate TargetType="Button">
                                <Border Background="{TemplateBinding Background}"
                                        BorderBrush="{TemplateBinding BorderBrush}"
                                        BorderThickness="{TemplateBinding BorderThickness}"
                                        Padding="{TemplateBinding Padding}">
                                    <ContentPresenter HorizontalAlignment="Stretch" />
                                </Border>
                            </ControlTemplate>
                        </Button.Template>
                        <StackPanel HorizontalAlignment="Stretch">
                            <TextBlock Text="{Binding Name}" Foreground="{DynamicResource TextBrush}" FontSize="11" />
                            <TextBlock Text="{Binding ParameterType}" Foreground="{DynamicResource MutedBrush}" FontSize="9" />
                        </StackPanel>
                    </Button>
                </DataTemplate>
            </ItemsControl.ItemTemplate>
        </ItemsControl>
    </ScrollViewer>
</Border>
<TextBlock Text="Pick from list or type a custom path. Auto-detects Bool/Int/Float type."
           Foreground="{DynamicResource MutedBrush}"
           FontSize="9"
           TextWrapping="Wrap"
           Margin="0,2,0,6" />
```

The hint TextBlock ("Pick from list or type a custom path…") stays — just moves below the list.

**New code-behind handlers** in `VrcTwitchOscBridge\AvatarSetsManagerWindow.xaml.cs`:

```csharp
private void OnWardrobeParameterNameTextChanged(object sender, System.Windows.Controls.TextChangedEventArgs e)
{
    if (Vm == null) return;
    if (sender is System.Windows.Controls.TextBox tb && tb.Text is string text)
    {
        Vm.WardrobeParameterNameFilter = text;
    }
}

private void OnWardrobeParameterItemClicked(object sender, RoutedEventArgs e)
{
    if (Vm?.SelectedWardrobeSnapshotParam is not { } param ||
        sender is not System.Windows.Controls.Button btn ||
        btn.Tag is not Models.VrChatOscParameterSummary p)
    {
        return;
    }

    param.ParameterName = p.Name;
    param.ParameterType = p.ParameterType;
    Vm.WardrobeParameterNameFilter = string.Empty;
}
```

(These mirror `OnParameterNameTextChanged` and `OnParameterItemClicked` at `AvatarSetsManagerWindow.xaml.cs:105, 114` but operate on the wardrobe param's `SelectedWardrobeSnapshotParam` instead of the rule's `SelectedAvatarRule`.)

## Section 2: ViewModel changes

**File:** `VrcTwitchOscBridge\ViewModels\AvatarSetsManagerViewModel.cs`

### 2.1 New field

```csharp
private string _wardrobeParameterNameFilter = string.Empty;
```

(Place near the other wardrobe state fields, around line 235.)

### 2.2 New public property

```csharp
public string WardrobeParameterNameFilter
{
    get => _wardrobeParameterNameFilter;
    set
    {
        if (SetProperty(ref _wardrobeParameterNameFilter, value ?? string.Empty))
        {
            ApplyWardrobeParameterFilter();
        }
    }
}
```

### 2.3 New public read-only property

```csharp
public IReadOnlyList<Models.VrChatOscParameterSummary> FilteredWardrobeParameters { get; private set; } = [];
```

This is what the new XAML `ItemsControl` binds to. It updates whenever `ApplyWardrobeParameterFilter` runs.

### 2.4 New private helper

```csharp
private void ApplyWardrobeParameterFilter()
{
    // Re-fetch the type-filtered options (also handles the custom-path insertion
    // for paths the user typed that aren't in the avatar JSON), then apply the
    // name filter. The result populates BOTH the new bindable list and the
    // existing AvailableWardrobeParameters (used for SelectedItem resolution).
    var typeFiltered = BuildWardrobeParameterOptionsForType(
        _selectedWardrobeSnapshotParam?.ParameterType ?? Models.OscParameterType.Float,
        _selectedWardrobeSnapshotParam?.ParameterName ?? string.Empty);
    var query = (_wardrobeParameterNameFilter ?? string.Empty).Trim();
    var nameFiltered = string.IsNullOrEmpty(query)
        ? typeFiltered
        : typeFiltered.Where(p =>
            p.Name.Contains(query, StringComparison.OrdinalIgnoreCase) ||
            p.Address.Contains(query, StringComparison.OrdinalIgnoreCase) ||
            p.DisplayLabel.Contains(query, StringComparison.OrdinalIgnoreCase)).ToList();
    FilteredWardrobeParameters = nameFiltered;
    AvailableWardrobeParameters = nameFiltered;
}
```

### 2.5 Update existing `RefreshWardrobeParameterOptions` helper

The current method (around line 2686) ends with:
```csharp
SetWardrobeParameterText(match?.DisplayLabel ?? _selectedWardrobeSnapshotParam.ParameterName ?? string.Empty);
```

Right before that line, it currently does:
```csharp
AvailableWardrobeParameters = BuildWardrobeParameterOptionsForType(
    _selectedWardrobeSnapshotParam.ParameterType,
    _selectedWardrobeSnapshotParam.ParameterName ?? string.Empty);
```

**Change that line to:**
```csharp
ApplyWardrobeParameterFilter();
```

The match resolution (finding the typed-text match in the type-filtered list), `_selectedWardrobeParameterOption = match`, and `SetWardrobeParameterText(...)` all stay exactly as they are. They are about the typed-text path, not the search filter.

## Section 3: Behavior matrix

| User action | Result |
|---|---|
| Open param editor, no filter typed | `FilteredWardrobeParameters` shows all type-filtered params (sorted by name). `AvailableWardrobeParameters` matches. |
| Type a partial name (e.g., "ear") | List filters in real time to params whose name/address/display label contains the text (case-insensitive). |
| Type then clear | All type-filtered params reappear. |
| Click a result in the list | `SelectedWardrobeSnapshotParam.ParameterName = p.Name`, `ParameterType = p.ParameterType`, filter clears. Type-aware value editor (Bool/Int/Float) updates. |
| Type a custom path that doesn't exist (e.g., `/avatar/parameters/MyParam`) | Text stays in the param. No warning. The list still filters by the typed text. `SelectedWardrobeParameterOption` is null. `ParameterType` keeps its last value. |
| Type a known path | List filters and the matching item is first. User can click to confirm. If they don't click, the existing `RefreshWardrobeParameterOptions` already resolved `ParameterName` and `ParameterType` to the match. |
| Click ⟳ refresh | Runs `RefreshWardrobeParametersAsync`, which reloads the avatar OSC JSON and re-runs `ApplyWardrobeParameterFilter`. |
| Change `ParameterType` ComboBox | `SelectedWardrobeSnapshotParam.ParameterType` setter raises `PropertyChanged`, the existing `SelectedWardrobeSnapshotParamChanged` handler calls `RefreshWardrobeParameterOptions`, which calls `ApplyWardrobeParameterFilter`. List updates. |
| Pick a different outfit param | `SelectedWardrobeSnapshotParam` setter raises `PropertyChanged`, the existing handler calls `RefreshWardrobeParameterOptions`. List updates. |
| Non-empty filter, no matches | List area is empty. The user's typed text still drives the param (custom-path behavior). |
| Old saves from v3.1.9 beta 2 | Load unchanged. The new `FilteredWardrobeParameters` is a derived view, not persisted. |

## Section 4: Files touched (3)

| File | Change |
|---|---|
| `VrcTwitchOscBridge\AvatarSetsManagerWindow.xaml` | Replace the wardrobe `Parameter Path` ComboBox block (lines 1737-1747) with the new search TextBox + list XAML. |
| `VrcTwitchOscBridge\AvatarSetsManagerWindow.xaml.cs` | Add 2 new event handlers (`OnWardrobeParameterNameTextChanged`, `OnWardrobeParameterItemClicked`). |
| `VrcTwitchOscBridge\ViewModels\AvatarSetsManagerViewModel.cs` | Add 1 field, 2 properties, 1 helper. Modify 1 existing helper (`RefreshWardrobeParameterOptions`). |

**No model changes. No persistence changes. No MainWindow changes. No csproj changes. No new converters.**

## Section 5: Verification plan

1. **Build** — `dotnet build "E:\!!!Program to work on\Proper Crystal Relay\VrcTwitchOscBridge\VrcTwitchOscBridge.csproj" --no-restore` succeeds with 0 warnings.

2. **Launch** — `Launch-Crystal-Relay-Debug.bat`, confirm `- DEBUG` suffix.

3. **Smoke test (manual):**
   - Open Avatar Sets manager, click a card with Wardrobe mode on.
   - Switch to Step 4, select an outfit, add a param, click into the param editor.
   - Verify the new search TextBox is present, the refresh ⟳ button works, and the list shows the avatar's parameters.
   - Type a partial name — list filters. Clear it — list restores.
   - Click a result — `ParameterName` populates, `ParameterType` populates, value editor switches, filter clears.
   - Type a custom path (`/avatar/parameters/MyParam`) — accepted, no warning.
   - Type a known path — list filters, click to confirm.
   - Change `ParameterType` ComboBox — list updates to the new type's parameters.
   - Pick a different outfit param — list updates.

4. **Backwards-compat:** Existing outfit configs from v3.1.9 beta 2 load unchanged. No migration needed.

5. **No-regressions:**
   - Rule parameter picker (Step 2 / Step 3 in the same manager) still works the same.
   - `Test Outfit` still sends the correct OSC packet for the new param.
   - `Add Param` / `Remove` / `Copy` / `Paste` still work.

6. **Grep audit:**
   - `rg "OnParameterItemClicked|OnParameterNameTextChanged" -- "VrcTwitchOscBridge/"` — only the rule picker versions (no new wardrobe versions with the same name).
   - `rg "WardrobeParameterNameFilter|FilteredWardrobeParameters" -- "VrcTwitchOscBridge/"` — only the new code.
   - `rg "OnWardrobeParameterNameTextChanged|OnWardrobeParameterItemClicked" -- "VrcTwitchOscBridge/"` — exactly 2 hits (XAML and code-behind).

## Section 6: Risks and mitigations

| Risk | Mitigation |
|---|---|
| Re-entrancy loop between `WardrobeParameterNameFilter` setter and `ApplyWardrobeParameterFilter` raising `PropertyChanged` for `FilteredWardrobeParameters` | None — `FilteredWardrobeParameters` is a derived read-only list. No setter, no re-entrancy possible. |
| The search TextBox writes the filter on every keystroke, including the click handler's `WardrobeParameterNameFilter = string.Empty` | The `!_isRestoringWardrobeParameterText` guard on the existing `WardrobeParameterText` setter already prevents this. The new `WardrobeParameterNameFilter` is a separate property with no re-entrancy risk. |
| Removing the ComboBox breaks the existing `SelectedWardrobeParameterOption` two-way binding chain | `AvailableWardrobeParameters` is still set by `ApplyWardrobeParameterFilter`, so the chain works. The XAML no longer binds to it (the list uses `FilteredWardrobeParameters` instead), but the VM machinery stays. |
| Build breaks because `AvailableWardrobeParameters` is no longer bound but still has a public setter | The public setter becomes effectively dead code, but it doesn't break the build. The auto-property stays for back-compat symmetry. |
| The list scrolled to the top when the user types a new character | The user is typing in the search TextBox, not interacting with the list. The list updates but the user doesn't care about scroll position until they click. If the list ever became interactive (drag to scroll), we'd need to preserve scroll position. Not a concern today. |
| Existing user muscle memory for the ComboBox dropdown is disrupted | Acceptable — the new UX is strictly better (always visible, scrollable, type-to-filter). The change is local to the wardrobe editor. |
| `BuildWardrobeParameterOptionsForType` is called twice in the new flow (once in `RefreshWardrobeParameterOptions` via the call I added, once in `ApplyWardrobeParameterFilter`) | The current method is `private` and does O(n) filtering. Calling it twice per refresh is fine for a list of ~200 params. No memoization needed. |
| The `MaxHeight="140"` is too small for some users | Match the rule picker's setting exactly. Users can resize the window to give the slide-out more room. The list scrolls internally. |
| The list shows no highlight for the currently selected `ParameterName` | Same as the rule picker. The param's name appears in the list as a regular item; the user can find it by typing the name or partial match. A highlight would be a polish feature, not in scope. |
| `RefreshWardrobeParametersAsync` (in VM) sets `_wardrobeParameterSourceParameters` then calls `RefreshWardrobeParameterOptions` (which will now call `ApplyWardrobeParameterFilter` which will use the new source) | Already works correctly. The chain is intact. |

## Section 7: CHANGELOG and AGENTS.md

This is a small UI polish. No user-facing changelog entry is required for v3.1.9 (the wardrobe migration in beta 2 is the most recent user-facing change). If you want to note it for an internal scratchpad, add a one-liner to `RELEASE-CHANGE-RECORD.txt` under `Changed` after the migration is shipped; otherwise skip.

`AGENTS.md` doesn't need an update — this is the same active build (3.1.9 beta2).

## Section 8: Commits (1)

```bash
git add "VrcTwitchOscBridge/AvatarSetsManagerWindow.xaml" \
        "VrcTwitchOscBridge/AvatarSetsManagerWindow.xaml.cs" \
        "VrcTwitchOscBridge/ViewModels/AvatarSetsManagerViewModel.cs"
git commit -m "feat(wardrobe): add search-and-pick parameter picker in Step 4

Replaces the cramped editable ComboBox with a search TextBox +
scrollable filtered list of clickable parameters, matching the
rule parameter picker pattern. Custom paths still accepted when
typed. Type-aware value editor and all other param behavior unchanged."
```

## Summary

3 sections, 3 files, 1 commit. Mirrors the established rule picker pattern, preserves the typed-text and custom-path workflows, no model or persistence changes, no new files.
