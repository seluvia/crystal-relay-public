# Wardrobe Parameter Picker Search Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Replace the wardrobe `Parameter Path` editable `ComboBox` in `AvatarSetsManagerWindow.xaml` Step 4 with a search TextBox + scrollable filtered list of clickable parameters, mirroring the rule parameter picker pattern.

**Architecture:** Mirror the rule picker's XAML pattern exactly. Add 1 VM field, 2 properties, 1 new helper, modify 1 existing helper. Add 2 new code-behind event handlers. The click-pick path bypasses the typed-text resolution chain by writing to `SelectedWardrobeSnapshotParam` directly. Custom paths still work via the existing `WardrobeParameterText` setter.

**Tech Stack:** C# / WPF / .NET 10 / ObservableObject pattern / `VrChatOscParameterSummary` records.

**Spec:** `docs/superpowers/specs/2026-06-12-wardrobe-param-picker-search-design.md`

**Working directory:** `E:\!!!Program to work on\Proper Crystal Relay`

**Active build:** 3.1.9 (beta2). No version bump in this plan; version bump happens when the user asks for a test/beta/release build.

---

## File Structure

| File | Role | Change |
|---|---|---|
| `VrcTwitchOscBridge\ViewModels\AvatarSetsManagerViewModel.cs` | Manager VM | Add 1 field, 2 properties, 1 helper. Modify 1 existing helper. |
| `VrcTwitchOscBridge\AvatarSetsManagerWindow.xaml` | Manager XAML | Replace wardrobe `Parameter Path` ComboBox with search + list. |
| `VrcTwitchOscBridge\AvatarSetsManagerWindow.xaml.cs` | Manager code-behind | Add 2 new event handlers. |

**No new files. No csproj changes. No model changes. No persistence changes.**

---

## Task 1: Add `WardrobeParameterNameFilter` state, `FilteredWardrobeParameters` property, and `ApplyWardrobeParameterFilter` helper to `AvatarSetsManagerViewModel`

**Files:**
- Modify: `VrcTwitchOscBridge\ViewModels\AvatarSetsManagerViewModel.cs` (add field, 2 properties, 1 helper)

- [ ] **Step 1: Add the new field**

After the existing wardrobe state fields (around line 240, after `_isRestoringWardrobeParameterText`), add:
```csharp
private string _wardrobeParameterNameFilter = string.Empty;
```

- [ ] **Step 2: Add the `WardrobeParameterNameFilter` public property**

Place it near the other wardrobe properties (after `AvailableWardrobeParameters`, around line 320). Add:
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

- [ ] **Step 3: Add the `FilteredWardrobeParameters` public property**

Place it right after `WardrobeParameterNameFilter`:
```csharp
public IReadOnlyList<Models.VrChatOscParameterSummary> FilteredWardrobeParameters { get; private set; } = [];
```

- [ ] **Step 4: Add the `ApplyWardrobeParameterFilter` helper**

Place it near the other wardrobe helpers (e.g., right after `BuildWardrobeParameterOptionsForType`, around line 950). Add:
```csharp
private void ApplyWardrobeParameterFilter()
{
    // Filter the type-filtered list (which is in _availableWardrobeParameters, set
    // by RefreshWardrobeParameterOptions) by the name search text. The typed-text
    // resolution path in RefreshWardrobeParameterOptions still uses
    // _availableWardrobeParameters directly so the match is computed against the
    // full same-type set, not just the filtered subset.
    var query = (_wardrobeParameterNameFilter ?? string.Empty).Trim();
    var nameFiltered = string.IsNullOrEmpty(query)
        ? _availableWardrobeParameters.ToList()
        : _availableWardrobeParameters.Where(p =>
            p.Name.Contains(query, StringComparison.OrdinalIgnoreCase) ||
            p.Address.Contains(query, StringComparison.OrdinalIgnoreCase) ||
            p.DisplayLabel.Contains(query, StringComparison.OrdinalIgnoreCase)).ToList();
    FilteredWardrobeParameters = nameFiltered;
}
```

This helper only updates `FilteredWardrobeParameters` (the new bindable list). It does not touch `AvailableWardrobeParameters` — that stays as the type-filtered list and is used by the typed-text resolution path in `RefreshWardrobeParameterOptions`.

- [ ] **Step 5: Modify `RefreshWardrobeParameterOptions` to call the new helper**

Find `RefreshWardrobeParameterOptions` in the file (around line 850). The current method's relevant block is:
```csharp
        AvailableWardrobeParameters = BuildWardrobeParameterOptionsForType(
            _selectedWardrobeSnapshotParam.ParameterType,
            _selectedWardrobeSnapshotParam.ParameterName ?? string.Empty);
        var match = AvailableWardrobeParameters.FirstOrDefault(p =>
            string.Equals(p.Address, address, StringComparison.Ordinal));
```

Add a call to `ApplyWardrobeParameterFilter()` right after the `AvailableWardrobeParameters = ...` line (and before the `var match = ...` line). The new block becomes:
```csharp
        AvailableWardrobeParameters = BuildWardrobeParameterOptionsForType(
            _selectedWardrobeSnapshotParam.ParameterType,
            _selectedWardrobeSnapshotParam.ParameterName ?? string.Empty);
        ApplyWardrobeParameterFilter();
        var match = AvailableWardrobeParameters.FirstOrDefault(p =>
            string.Equals(p.Address, address, StringComparison.Ordinal));
```

**Why this works:** `AvailableWardrobeParameters` stays the type-filtered list (used for typed-text `match` resolution). `FilteredWardrobeParameters` is the new name-filtered subset (used for the list display). Both are kept in sync because every call to `RefreshWardrobeParameterOptions` triggers `ApplyWardrobeParameterFilter` at the end.

- [ ] **Step 6: Build to verify**

```powershell
dotnet build "E:\!!!Program to work on\Proper Crystal Relay\VrcTwitchOscBridge\VrcTwitchOscBridge.csproj" --no-restore
```

Expected: Build succeeds. 0 warnings, 0 errors.

- [ ] **Step 7: Commit**

```bash
git add "VrcTwitchOscBridge/ViewModels/AvatarSetsManagerViewModel.cs"
git commit -m "feat(wardrobe): add WardrobeParameterNameFilter and FilteredWardrobeParameters

Adds the search state and the bindable filtered list that the new
XAML ItemsControl will bind to. ApplyWardrobeParameterFilter runs
when the filter changes or when RefreshWardrobeParameterOptions
rebuilds the type-filtered list, keeping both lists in sync."
```

---

## Task 2: Add the 2 new code-behind event handlers in `AvatarSetsManagerWindow.xaml.cs`

**Files:**
- Modify: `VrcTwitchOscBridge\AvatarSetsManagerWindow.xaml.cs` (add 2 new handlers)

- [ ] **Step 1: Read the existing rule picker handlers to mirror them**

Open the file and find the existing handlers around line 105:
```csharp
private void OnParameterNameTextChanged(object sender, System.Windows.Controls.TextChangedEventArgs e)
{
    if (Vm == null) return;
    if (sender is System.Windows.Controls.TextBox tb && tb.Text is string text)
    {
        Vm.ParameterNameFilter = text;
    }
}

private void OnParameterItemClicked(object sender, RoutedEventArgs e)
{
    if (Vm?.SelectedAvatarRule is Models.TriggerRule rule &&
        sender is System.Windows.Controls.Button btn &&
        btn.Tag is Models.VrChatOscParameterSummary p)
    {
        rule.ParameterName = p.Name;
        // Also auto-set the parameter type to match
        rule.ParameterType = p.ParameterType;
        // Clear the search filter so the user sees the change
        Vm.ParameterNameFilter = string.Empty;
    }
}
```

These are the patterns to mirror, with the targets changed to the wardrobe param.

- [ ] **Step 2: Add `OnWardrobeParameterNameTextChanged`**

Place it right after `OnParameterNameTextChanged`. Add:
```csharp
private void OnWardrobeParameterNameTextChanged(object sender, System.Windows.Controls.TextChangedEventArgs e)
{
    if (Vm == null) return;
    if (sender is System.Windows.Controls.TextBox tb && tb.Text is string text)
    {
        Vm.WardrobeParameterNameFilter = text;
    }
}
```

- [ ] **Step 3: Add `OnWardrobeParameterItemClicked`**

Place it right after `OnWardrobeParameterItemClicked`'s mirror in the existing handlers section (right after `OnWardrobeParameterNameTextChanged` from Step 2, or grouped with the other `On*` handlers). Add:
```csharp
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

- [ ] **Step 4: Build to verify**

```powershell
dotnet build "E:\!!!Program to work on\Proper Crystal Relay\VrcTwitchOscBridge\VrcTwitchOscBridge.csproj" --no-restore
```

Expected: Build succeeds. The new handlers compile. They are not yet referenced from XAML (Task 3 will wire them up), so the build does not error on missing references.

- [ ] **Step 5: Commit**

```bash
git add "VrcTwitchOscBridge/AvatarSetsManagerWindow.xaml.cs"
git commit -m "feat(wardrobe): add 2 handlers for the new param picker

OnWardrobeParameterNameTextChanged updates WardrobeParameterNameFilter
on every keystroke. OnWardrobeParameterItemClicked picks a list item
and writes the param's name and type. Mirror the existing rule picker
handlers but target the wardrobe param."
```

---

## Task 3: Replace the wardrobe `Parameter Path` ComboBox with the search + list XAML

**Files:**
- Modify: `VrcTwitchOscBridge\AvatarSetsManagerWindow.xaml` (replace lines 1737-1747)

- [ ] **Step 1: Read the existing `Parameter Path` block**

Open the file and locate the wardrobe `Parameter Path` editor (around line 1733). The current block looks like:
```xaml
<TextBlock Text="Parameter Path"
           Foreground="{DynamicResource TextBrush}"
           FontSize="11"
           Margin="0,0,0,2" />
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

- [ ] **Step 2: Replace the ComboBox + post-text hint with the new search + list block**

Find:
```xaml
<ComboBox IsEditable="True"
          TextSearch.TextPath="DisplayLabel"
          DisplayMemberPath="DisplayLabel"
          ItemsSource="{Binding DataContext.AvailableWardrobeParameters, RelativeSource={RelativeSource AncestorType=Window}}"
          SelectedItem="{Binding DataContext.SelectedWardrobeParameterOption, RelativeSource={RelativeSource AncestorType=Window}}"
          Text="{Binding DataContext.WardrobeParameterText, RelativeSource={RelativeSource AncestorType=Window}, UpdateSourceTrigger=PropertyChanged}" />
```

Replace with:
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
```

The hint TextBlock below ("Pick from list or type a custom path…") stays in place — only the ComboBox gets replaced.

- [ ] **Step 3: Build to verify**

```powershell
dotnet build "E:\!!!Program to work on\Proper Crystal Relay\VrcTwitchOscBridge\VrcTwitchOscBridge.csproj" --no-restore
```

Expected: Build succeeds. 0 warnings, 0 errors.

- [ ] **Step 4: Commit**

```bash
git add "VrcTwitchOscBridge/AvatarSetsManagerWindow.xaml"
git commit -m "feat(wardrobe): replace Parameter Path ComboBox with search + list

Adds a search TextBox bound to WardrobeParameterNameFilter and a
scrollable filtered list bound to FilteredWardrobeParameters. Mirror
the rule parameter picker pattern exactly. The refresh ⟳ button
reuses the existing RefreshWardrobeParametersCommand."
```

---

## Task 4: Final verification

**Files:** None modified. Runtime + grep audit.

- [ ] **Step 1: Final build**

```powershell
dotnet build "E:\!!!Program to work on\Proper Crystal Relay\VrcTwitchOscBridge\VrcTwitchOscBridge.csproj" --no-restore
```

Expected: Build succeeds. 0 warnings, 0 errors.

- [ ] **Step 2: Launch debug build**

```powershell
& "E:\!!!Program to work on\Proper Crystal Relay\Launch-Crystal-Relay-Debug.bat"
```

Expected: Window title shows `- DEBUG`. App starts.

- [ ] **Step 3: Smoke test (manual)**

1. Open Avatar Sets manager, click a card with Wardrobe mode on.
2. Switch to Step 4, select an outfit, add a param, click into the param editor.
3. Verify the new search TextBox is present, the refresh ⟳ button works, and the list shows the avatar's parameters.
4. Type a partial name (e.g., "ear") — list filters. Clear it — list restores.
5. Click a result — `ParameterName` populates, `ParameterType` populates, value editor switches, filter clears.
6. Type a custom path (`/avatar/parameters/MyParam`) — accepted, no warning.
7. Type a known path (`/avatar/parameters/HatLong`) — list filters, click to confirm.
8. Change `ParameterType` ComboBox — list updates to the new type's parameters.
9. Pick a different outfit param — list updates.

- [ ] **Step 4: Backwards-compat**

1. Open a save from v3.1.9 beta 2.
2. Open a wardrobe outfit with params.
3. Verify the params load, the new picker works, and existing typed values are preserved.

- [ ] **Step 5: No-regressions**

1. Rule parameter picker (Step 2 / Step 3) still works the same.
2. `Test Outfit` still sends the correct OSC packet for the new param.
3. `Add Param` / `Remove` / `Copy` / `Paste` still work.

- [ ] **Step 6: Grep audit**

```powershell
git grep -nE "OnWardrobeParameterNameTextChanged|OnWardrobeParameterItemClicked" -- "VrcTwitchOscBridge/"
git grep -nE "WardrobeParameterNameFilter|FilteredWardrobeParameters" -- "VrcTwitchOscBridge/"
git grep -n "OnParameterItemClicked|OnParameterNameTextChanged" -- "VrcTwitchOscBridge/AvatarSetsManagerWindow.xaml.cs"
```

Expected:
- First command: 2 hits (1 in XAML, 1 in code-behind).
- Second command: hits only in the new code (manager VM + XAML).
- Third command: 2 hits (the existing rule picker versions, unchanged).

- [ ] **Step 7: Commit any verification-only changes**

If any cleanup was needed during verification:
```bash
git add -A
git commit -m "chore: verification-time adjustments to wardrobe param picker"
```

Otherwise skip this step.

---

## Summary

4 tasks, each independently buildable. Each task ends with a commit. The runtime (WardrobeExecutorService) is unchanged — the new picker just changes how the user selects the parameter name and type before the bridge reads them.

**Out of scope (per spec):** No persistence changes, no new files, no model changes, no rule picker changes, no new converters.
