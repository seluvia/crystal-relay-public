# Wardrobe Editor Migration into Avatar Sets Manager

**Date:** 2026-06-12
**Status:** Design approved, ready for implementation plan
**Active development build:** 3.1.9 (beta2)

## Goal

Move the Wardrobe editor from its current in-main-window location
(`MainWindow.xaml:6729-6991`) into the existing `AvatarSetsManagerWindow`
(Step 4 placeholder at lines 1400-1501). Add the per-outfit managed reward
ready/cooldown color pickers the user asked for. Delete the old in-main editor
and the now-unused ViewModel state, commands, and `IsViewingWardrobe` flag.

## Why

The user has used the current Wardrobe editor and wants it:

- Polished (fix the indentation bug, add the missing IsEnabled checkbox)
- Inside the new card-based manager UI alongside the rest of the Avatar Set
  editor (not buried in the main window rule list)
- Feature-parity with the rest of the Wardrobe (everything the current editor
  does, plus the per-outfit cooldown colors that the Avatar Set rules have)

The user explicitly scoped this to "parity with current Wardrobe + colors" —
**not** full Avatar Set feature parity. Master reward, disable pairing, and
avatar-change blockers are out of scope.

## Non-Goals

- Wardrobe Master Reward editor (the per-profile fields exist but are
  unused; out of scope for this change)
- New "set" features not in the current Wardrobe (disable pairing,
  avatar-change blocker, Bits+Subs override)
- New files, new converters, new themes
- Data migration script (configs already live per `AvatarTriggerProfile`)
- Build or test package creation

## High-Level Architecture

The Wardrobe system is **already mostly built** and **already wired** to the
runtime. What's missing is:

1. The editor UI is in the wrong place (in-main-window vs the new manager)
2. The placeholder in the manager (Step 4) is incomplete
3. Per-outfit color pickers don't exist (the model has no fields for them)

The fix is a lift-and-shift: take the existing in-main editor XAML, port it
into the manager's Step 4 slot with the manager's resources and styles, add
the color pickers, then delete the in-main editor and its ViewModel state.

**Data flow is unchanged:** Avatar Set card → click → manager slide-out
opens → Step 4 lists outfits → click outfit → editor shows → save writes
to `AvatarTriggerProfile.WardrobeOutfits` → runtime consumes via
`BridgeRuntimeConfiguration.TryToWardrobeSnapshot` →
`WardrobeExecutorService.ExecuteOutfitAsync`.

## Files Touched (8, no new files)

| File | Change |
|---|---|
| `Models\WardrobeOutfit.cs` | Add `ManagedRewardReadyColor` / `ManagedRewardCooldownColor` properties + brushes. Mirror `TriggerRule` exactly. |
| `Services\SettingsStore.cs` | Extend `PersistedWardrobeOutfit` with the two new color fields. Update `ToPersistedWardrobeOutfit` / `ToWardrobeOutfit` with safe defaults. |
| `MainWindow.xaml` | Delete lines 6637-6714 (outer Border + sibling StackPanel) and 6729-6991 (wardrobe editor). Update lines 4486-4494 (workspace hide logic). |
| `MainWindowViewModel.cs` | Delete wardrobe state, commands, helpers. **Keep** runtime bridges (`TestAvatarSet`, `RetireWardrobeManagedReward`, `QueueManagedRewardSyncPublic`, `LoadAvatarParameterSummariesAsync`, `LoadTwitchCustomRewardsAsync`, `TryGetVrChatAvatarThumbnailUrl`). Add one new public method: `TestWardrobeOutfitPublicAsync(outfit, profile, ct)`. |
| `ViewModels\AvatarSetsManagerViewModel.cs` | Add wardrobe state fields, properties, commands, helpers. Port logic from MainWindow VM. Add `ParameterTypes`, `BoolValueOptions`, `RewardSyncModeOptions` (pass-through). Extend `_outfitSyncProperties` with the new color fields. |
| `AvatarSetsManagerWindow.xaml` | Replace lines 1400-1501 (Step 4 placeholder) with the full editor. Add color pickers, full Manage-vs-Link branching, type-aware value editor, all toolbar buttons. |
| `AvatarSetsManagerWindow.xaml.cs` | Add 4 color picker handlers (`OnPickWardrobeReadyColorClicked` / `OnResetWardrobeReadyColorClicked` / `OnPickWardrobeCooldownColorClicked` / `OnResetWardrobeCooldownClicked`). `PickColorAndApply` is already in the file. |
| (no csproj change) | All touched files are already in `<Compile>` and `<Page>` lists. |

## Section 1: Data Model Changes

### `Models\WardrobeOutfit.cs` — add per-outfit managed reward colors

Mirror `TriggerRule` (lines 65-66, 258-282, 1315-1317). Two raw string fields
with normalized setters + two computed `Brush` properties.

Add at the top:
```csharp
using System.Windows.Media;
using Brush = System.Windows.Media.Brush;
```

Add two private fields alongside the others:
```csharp
private string managedRewardReadyColor = ManagedRewardPresentation.ReadyBackgroundColor;
private string managedRewardCooldownColor = ManagedRewardPresentation.InUseBackgroundColor;
```

Add two public properties (after `ChatCommandText`):
```csharp
public string ManagedRewardReadyColor
{
    get => managedRewardReadyColor;
    set
    {
        var normalizedValue = ManagedRewardPresentation.NormalizeReadyBackgroundColor(value);
        if (SetProperty(ref managedRewardReadyColor, normalizedValue))
        {
            RaisePropertyChanged(nameof(ManagedRewardReadyColorBrush));
        }
    }
}

public string ManagedRewardCooldownColor
{
    get => managedRewardCooldownColor;
    set
    {
        var normalizedValue = ManagedRewardPresentation.NormalizeCooldownBackgroundColor(value);
        if (SetProperty(ref managedRewardCooldownColor, normalizedValue))
        {
            RaisePropertyChanged(nameof(ManagedRewardCooldownColorBrush));
        }
    }
}

public Brush ManagedRewardReadyColorBrush => CreateColorBrush(ManagedRewardReadyColor);
public Brush ManagedRewardCooldownColorBrush => CreateColorBrush(ManagedRewardCooldownColor);
```

`CreateColorBrush` is inherited from `ObservableObject` (uses `ColorConverter`,
freezes the brush) — no helper to add.

**Naming note:** these are per-outfit colors, distinct from the existing
per-profile `WardrobeMasterRewardReadyColor` on `AvatarTriggerProfile`
(lines 332-359). Different surface, different lifetime. Do not collapse them.

**No other model changes.** `WardrobeSnapshotParam` stays as-is. The
type-aware `SetValue` behavior (Bool auto-defaults to "True", Float to "0.0",
Int to "0") is already in the ParameterType setter at lines 44-49.

### `Services\SettingsStore.cs` — persist the two new color fields

Add to `PersistedWardrobeOutfit` (after `TwitchRewardSyncMode`, around line
2964):
```csharp
public string? ManagedRewardReadyColor { get; set; }
public string? ManagedRewardCooldownColor { get; set; }
```

Update `ToPersistedWardrobeOutfit` (around line 1064):
```csharp
ManagedRewardReadyColor = outfit.ManagedRewardReadyColor,
ManagedRewardCooldownColor = outfit.ManagedRewardCooldownColor,
```

Update `ToWardrobeOutfit` (around line 1249):
```csharp
ManagedRewardReadyColor = ManagedRewardPresentation.NormalizeReadyBackgroundColor(persisted.ManagedRewardReadyColor),
ManagedRewardCooldownColor = ManagedRewardPresentation.NormalizeCooldownBackgroundColor(persisted.ManagedRewardCooldownColor),
```

**Old-save safety:** `ManagedRewardPresentation.NormalizeReadyBackgroundColor(null)`
returns `#22C55E` and `NormalizeCooldownBackgroundColor(null)` returns `#EF4444`
(lines 104-108 of `ManagedRewardPresentation.cs`). Existing v3.1.9 beta saves
load with sensible defaults — no migration step.

## Section 2: ViewModel Changes

### New state fields on `AvatarSetsManagerViewModel`

```csharp
private Models.WardrobeSnapshotParam? _selectedWardrobeSnapshotParam;
private Models.VrChatOscParameterSummary? _selectedWardrobeParameterOption;
private string _wardrobeParameterText = string.Empty;
private IReadOnlyList<Models.VrChatOscParameterSummary> _wardrobeParameterSourceParameters = [];
private IReadOnlyList<Models.VrChatOscParameterSummary> _availableWardrobeParameters = [];
private Models.WardrobeOutfit? _copiedWardrobeOutfit;
private Models.WardrobeSnapshotParam? _copiedWardrobeSnapshotParam;
private bool _isRestoringWardrobeParameterSelection;
private bool _isRestoringWardrobeParameterText;
```

Extend `SelectedWardrobeOutfit` setter (line 234) to also reset
`SelectedWardrobeSnapshotParam` to the first param and notify all the new
commands (otherwise they start disabled). Mirror `MainWindowViewModel.cs:2545-2564`.

### New public properties

| Property | Behavior |
|---|---|
| `SelectedWardrobeSnapshotParam` | Unwires/wires `PropertyChanged` from previous/next param. Calls `RefreshWardrobeParameterOptions()`. Notifies copy/paste/remove commands. Mirror `MainWindowViewModel.cs:2582-2606`. |
| `SelectedWardrobeParameterOption` | Guarded by `isRestoringWardrobeParameterSelection`. Sets the param's `ParameterType` and `ParameterName` from the option, then calls `SetWardrobeParameterText`. Mirror `MainWindowViewModel.cs:2608-2624`. |
| `WardrobeParameterText` | Two-way text for the editable combo. Guarded by `isRestoringWardrobeParameterText`. Calls `CommitWardrobeParameterText`. Mirror `MainWindowViewModel.cs:2626-2637`. |
| `AvailableWardrobeParameters` | Public with `private set`. Mirror `MainWindowViewModel.cs:2899-2903`. |
| `ParameterTypes` | `public IReadOnlyList<OscParameterType> ParameterTypes { get; } = [OscParameterType.Bool, OscParameterType.Int, OscParameterType.Float];` |
| `BoolValueOptions` | `public IReadOnlyList<string> BoolValueOptions { get; } = ["True", "False"];` |
| `RewardSyncModeOptions` | **Pass-through:** `public IReadOnlyList<TwitchRewardSyncModeOption> RewardSyncModeOptions => _mainVm.RewardSyncModeOptions;` (mirror `UniversalTriggersManagerViewModel.cs:342`). |

### New commands (declare in constructor around line 158)

```csharp
AddWardrobeSnapshotParamCommand = new RelayCommand(AddWardrobeSnapshotParam, () => SelectedWardrobeOutfit is not null);
RemoveWardrobeSnapshotParamCommand = new RelayCommand(RemoveWardrobeSnapshotParam, () => SelectedWardrobeSnapshotParam is not null);
CopyWardrobeOutfitCommand = new RelayCommand(CopyWardrobeOutfit, () => SelectedWardrobeOutfit is not null);
PasteWardrobeOutfitCommand = new RelayCommand(PasteWardrobeOutfit, () => SelectedProfile is not null && _copiedWardrobeOutfit is not null);
CopyWardrobeSnapshotParamCommand = new RelayCommand(CopyWardrobeSnapshotParam, () => SelectedWardrobeSnapshotParam is not null);
PasteWardrobeSnapshotParamCommand = new RelayCommand(PasteWardrobeSnapshotParam, () => SelectedWardrobeOutfit is not null && _copiedWardrobeSnapshotParam is not null);
RefreshWardrobeParametersCommand = new AsyncRelayCommand(RefreshWardrobeParametersAsync);
TestWardrobeOutfitCommand = new AsyncRelayCommand(TestWardrobeOutfitAsync, () => SelectedWardrobeOutfit is not null && SelectedProfile is not null);
```

### Extend `_outfitSyncProperties` (line 807)

Add the two new color fields so the sync fires when colors change:
```csharp
private static readonly string[] _outfitSyncProperties =
{
    nameof(Models.WardrobeOutfit.Name),
    nameof(Models.WardrobeOutfit.IsEnabled),
    nameof(Models.WardrobeOutfit.TwitchRewardId),
    nameof(Models.WardrobeOutfit.TwitchRewardTitle),
    nameof(Models.WardrobeOutfit.TwitchRewardCost),
    nameof(Models.WardrobeOutfit.TwitchRewardSyncMode),
    nameof(Models.WardrobeOutfit.ManagedRewardReadyColor),
    nameof(Models.WardrobeOutfit.ManagedRewardCooldownColor)
};
```

### New helper methods on the manager

Port 1:1 from `MainWindowViewModel` lines 2639-2897 and 6800-6940, replacing
the `SelectedAvatarProfile` reference with `SelectedProfile`:

- `AddWardrobeSnapshotParam` / `RemoveWardrobeSnapshotParam`
- `CopyWardrobeOutfit` / `PasteWardrobeOutfit`
- `CopyWardrobeSnapshotParam` / `PasteWardrobeSnapshotParam`
- `RefreshWardrobeParameterOptions` / `BuildWardrobeParameterOptionsForType` /
  `TryRepairSelectedWardrobeParameter` / `TryResolveWardrobeParameterInput` /
  `SetWardrobeParameterText` / `CommitWardrobeParameterText` /
  `SelectedWardrobeSnapshotParamChanged`
- `RefreshWardrobeParametersAsync` — call
  `_mainVm.LoadAvatarParameterSummariesAsync(SelectedProfile.AvatarId)`
  instead of the inline cache call
- `TestWardrobeOutfitAsync` — call
  `_mainVm.TestWardrobeOutfitPublicAsync(SelectedWardrobeOutfit,
  SelectedProfile, CancellationToken.None)` (see Section 3.7)
- `CloneWardrobeOutfit(outfit, clearRewardId, copyName)` /
  `CloneWardrobeSnapshotParam(param)` / `GetUniqueWardrobeCopyName`
- `StripWardrobeParameterDisplayTypeSuffix`

### Required addition to `MainWindowViewModel`

Add one public overload so the manager can test a specific outfit:

```csharp
public async Task TestWardrobeOutfitPublicAsync(
    WardrobeOutfit outfit,
    AvatarTriggerProfile profile,
    CancellationToken cancellationToken)
{
    if (outfit is null || profile is null) return;
    // Body mirrors existing TestWardrobeOutfitAsync (lines 6756-6798)
    // but uses the parameters instead of SelectedWardrobeOutfit / SelectedAvatarProfile.
    // Sets BridgeStatus and AppendLog on failure paths.
    // Uses bridgeRefreshGate, ReloadRuntimeConfigAsync, EnsureBridgeStateAsync,
    // BridgeRuntimeConfiguration.TryToWardrobeSnapshot, bridgeCoordinator.ExecuteWardrobeOutfitAsync.
}
```

**Refactor opportunity:** extract the body into a private
`ExecuteTestWardrobeOutfitAsync(outfit, profile, ct)` and have both
`TestWardrobeOutfitAsync` and `TestWardrobeOutfitPublicAsync` call it. Cleaner.

## Section 3: XAML Changes

### `AvatarSetsManagerWindow.xaml` — replace Step 4 (lines 1400-1501)

**Style/resource compatibility:** all manager resources exist. No substitutions:
- `AccentButtonStyle` (line 107), `SecondaryButtonStyle` (line 99)
- `NullToVisibilityConverter` (line 76), `BoolToVisibilityConverter` (line 73)
- `GuidToSelectedOutfitBrushConverter` (line 78)
- `TextBrush` / `MutedBrush` / `NestedPanelBrush` / `AccentBrush` / `PanelBrush` /
  `BorderBrush` / `WarnTextBrush` (all defined in lines 29-69)

**New Step 4 block structure:**

```
Border "Step 4 Wardrobe"
├── StackPanel  [Visible when UseWardrobeMode]            (KEEP existing wrapper)
│   ├── Header Grid  [Step 4 Outfits text + + Add Outfit]  (KEEP existing, 1402-1419)
│   ├── ItemsControl of WardrobeOutfits  (KEEP existing list, 1421-1454)
│   ├── Border  [Visible when SelectedWardrobeOutfit != null]  (REPLACE editor, 1457-1498)
│   │   └── StackPanel DataContext={SelectedWardrobeOutfit}
│   │       ├── Heading "Edit Outfit" + 70s safety warning text
│   │       ├── IsEnabled CheckBox
│   │       ├── Name TextBox
│   │       ├── Active Time (Number) TextBox + ShortActiveTime warning
│   │       ├── Reward Sync Mode ComboBox  (mirror lines 785-801)
│   │       ├── LinkExisting branch StackPanel  (mirror lines 803-847)
│   │       │   └── Existing Twitch Reward ComboBox + ↻ reload + Reward ID (read-only)
│   │       ├── CreateOrManage branch StackPanel  (mirror lines 849-970)
│   │       │   ├── Title 2-col UniformGrid: TwitchRewardTitle + TwitchRewardCost
│   │       │   ├── TwitchRewardDescription multi-line TextBox
│   │       │   ├── ChatCommandText TextBox
│   │       │   ├── "Reward Colors" section header
│   │       │   ├── Ready color row (swatch + hex TextBox + Pick + Reset)
│   │       │   └── Cooldown color row (swatch + hex TextBox + Pick + Reset)
│   │       ├── Sub-toolbar: + Add Param / Remove / Copy / Paste / Refresh / Test (WrapPanel)
│   │       ├── ListBox of SnapshotParams
│   │       └── Border  [Visible when SelectedWardrobeSnapshotParam != null]
│   │           └── StackPanel DataContext={SelectedWardrobeSnapshotParam}
│   │               ├── Heading "Edit Parameter"
│   │               ├── Parameter Path ComboBox (editable, type-filtered)
│   │               ├── Type/Value 2-col UniformGrid:
│   │               │   ├── Type ComboBox bound to ParameterTypes
│   │               │   └── Value cell: Bool ComboBox | Int TextBox | Float TextBox
│   │               └── Hint text about typing custom path
│   └── Footer Grid: Global Wardrobe Cooldown (TextBox bound to WardrobeCooldownSeconds)
```

**Sync Mode ComboBox:**
```xaml
<ComboBox SelectedValue="{Binding TwitchRewardSyncMode, UpdateSourceTrigger=PropertyChanged}"
          SelectedValuePath="Tag" Margin="0,0,0,8">
    <ComboBoxItem Content="Manage and Create" Tag="{x:Static models:TwitchRewardSyncMode.CreateOrManage}" />
    <ComboBoxItem Content="Link to listen only" Tag="{x:Static models:TwitchRewardSyncMode.LinkExisting}" />
</ComboBox>
```

**LinkExisting branch** (mirror lines 803-847, swap `ChannelPointRewardId` → `TwitchRewardId`):
```xaml
<StackPanel Margin="0,0,0,8">
    <StackPanel.Style>
        <Style TargetType="StackPanel">
            <Setter Property="Visibility" Value="Collapsed" />
            <Style.Triggers>
                <DataTrigger Binding="{Binding UsesLinkedExistingReward}" Value="True">
                    <Setter Property="Visibility" Value="Visible" />
                </DataTrigger>
            </Style.Triggers>
        </Style>
    </StackPanel.Style>
    <Grid>
        <Grid.ColumnDefinitions>
            <ColumnDefinition Width="*" />
            <ColumnDefinition Width="Auto" />
        </Grid.ColumnDefinitions>
        <TextBlock Text="Existing Twitch Reward" Foreground="{DynamicResource TextBrush}" FontSize="11" Margin="0,0,0,4" />
        <Button Grid.Column="1" Content="↻" ToolTip="Reload Twitch rewards"
                Command="{Binding DataContext.LoadTwitchRewardsCommand, RelativeSource={RelativeSource AncestorType=Window}}"
                Style="{StaticResource SecondaryButtonStyle}" />
    </Grid>
    <ComboBox ItemsSource="{Binding DataContext.AvailableTwitchRewards, RelativeSource={RelativeSource AncestorType=Window}}"
              SelectedValue="{Binding TwitchRewardId, UpdateSourceTrigger=PropertyChanged}"
              SelectedValuePath="Id" DisplayMemberPath="Title" IsEditable="False" Margin="0,0,0,4" />
    <TextBlock Text="{Binding DataContext.TwitchRewardsLoadStatus, RelativeSource={RelativeSource AncestorType=Window}}"
               Foreground="{DynamicResource MutedBrush}" FontSize="10" Margin="0,0,0,4" />
    <TextBox Text="{Binding TwitchRewardId, UpdateSourceTrigger=PropertyChanged}" IsReadOnly="True"
             Background="{DynamicResource NestedPanelBrush}" />
</StackPanel>
```

**CreateOrManage branch** with the new color pickers (mirror lines 849-970,
swap `ChannelPointRewardTitle/Cost` → `TwitchRewardTitle/Cost` and bind
colors to the new `ManagedRewardReadyColor` / `ManagedRewardCooldownColor`
on the outfit):
- Reward Title + Cost in a 2-col `UniformGrid`
- Reward Description multi-line TextBox
- ChatCommand TextBox (new for wardrobe — was in main, mirror main's line
  6874-6879 pattern)
- Ready/Cooldown color pickers — use the EXACT same XAML structure as
  lines 897-968, but bind `Tag="{Binding}"` to the outfit (and the
  code-behind handlers use `WardrobeOutfit` instead of `TriggerRule`)

**Param picker ComboBox** (mirror in-main editor's lines 6935-6942, using
the manager's `AvailableWardrobeParameters`):
```xaml
<ComboBox IsEditable="True"
          TextSearch.TextPath="DisplayLabel"
          DisplayMemberPath="DisplayLabel"
          ItemsSource="{Binding DataContext.AvailableWardrobeParameters, RelativeSource={RelativeSource AncestorType=Window}}"
          SelectedItem="{Binding DataContext.SelectedWardrobeParameterOption, RelativeSource={RelativeSource AncestorType=Window}}"
          Text="{Binding DataContext.WardrobeParameterText, RelativeSource={RelativeSource AncestorType=Window}, UpdateSourceTrigger=PropertyChanged}" />
```

**Type-aware value editor:**
```xaml
<UniformGrid Columns="2" Margin="0,0,0,8">
    <StackPanel>
        <TextBlock Text="Type" Foreground="{DynamicResource TextBrush}" FontSize="11" Margin="0,0,0,2" />
        <ComboBox ItemsSource="{Binding DataContext.ParameterTypes, RelativeSource={RelativeSource AncestorType=Window}}"
                  SelectedItem="{Binding ParameterType, UpdateSourceTrigger=PropertyChanged}" />
    </StackPanel>
    <StackPanel Margin="6,0,0,0">
        <TextBlock Text="Value" Foreground="{DynamicResource TextBrush}" FontSize="11" Margin="0,0,0,2" />
        <ComboBox ItemsSource="{Binding DataContext.BoolValueOptions, RelativeSource={RelativeSource AncestorType=Window}}"
                  SelectedItem="{Binding SetValue}"
                  Visibility="{Binding UsesBoolParameter, Converter={StaticResource BoolToVisibilityConverter}}" />
        <TextBox Text="{Binding SetValue, UpdateSourceTrigger=PropertyChanged}"
                 Visibility="{Binding UsesIntParameter, Converter={StaticResource BoolToVisibilityConverter}}" />
        <TextBox Text="{Binding SetValue, UpdateSourceTrigger=PropertyChanged}"
                 Visibility="{Binding UsesFloatParameter, Converter={StaticResource BoolToVisibilityConverter}}" />
    </StackPanel>
</UniformGrid>
```

**Global wardrobe cooldown footer:**
```xaml
<Grid Margin="0,12,0,0">
    <Grid.ColumnDefinitions>
        <ColumnDefinition Width="*" />
    </Grid.ColumnDefinitions>
    <StackPanel>
        <TextBlock Text="Global Wardrobe Cooldown (seconds)" Foreground="{DynamicResource TextBrush}" FontSize="11" Margin="0,0,0,2" />
        <TextBlock Text="All outfits share this cooldown. 0 = no cooldown." Foreground="{DynamicResource MutedBrush}" FontSize="9" TextWrapping="Wrap" Margin="0,0,0,4" />
        <TextBox Text="{Binding WardrobeCooldownSeconds, UpdateSourceTrigger=PropertyChanged}" />
    </StackPanel>
</Grid>
```

**Outfit list (lines 1421-1454)** is kept exactly as-is — the
`OnOutfitItemClicked` selection, `GuidToSelectedOutfitBrushConverter` highlight,
and per-item `✕` delete button are all correct.

### `AvatarSetsManagerWindow.xaml.cs` — add 4 color picker handlers

Mirror the rule's color picker handlers (lines 164-198) but type the `Tag` as
`Models.WardrobeOutfit`:

```csharp
private void OnPickWardrobeReadyColorClicked(object sender, RoutedEventArgs e)
{
    if (sender is System.Windows.Controls.Button btn && btn.Tag is Models.WardrobeOutfit outfit)
    {
        PickColorAndApply(outfit.ManagedRewardReadyColor, color => outfit.ManagedRewardReadyColor = color);
        e.Handled = true;
    }
}

private void OnResetWardrobeReadyColorClicked(object sender, RoutedEventArgs e)
{
    if (sender is System.Windows.Controls.Button btn && btn.Tag is Models.WardrobeOutfit outfit)
    {
        outfit.ManagedRewardReadyColor = Services.ManagedRewardPresentation.ReadyBackgroundColor;
        e.Handled = true;
    }
}

private void OnPickWardrobeCooldownColorClicked(object sender, RoutedEventArgs e)
{
    if (sender is System.Windows.Controls.Button btn && btn.Tag is Models.WardrobeOutfit outfit)
    {
        PickColorAndApply(outfit.ManagedRewardCooldownColor, color => outfit.ManagedRewardCooldownColor = color);
        e.Handled = true;
    }
}

private void OnResetWardrobeCooldownColorClicked(object sender, RoutedEventArgs e)
{
    if (sender is System.Windows.Controls.Button btn && btn.Tag is Models.WardrobeOutfit outfit)
    {
        outfit.ManagedRewardCooldownColor = Services.ManagedRewardPresentation.InUseBackgroundColor;
        e.Handled = true;
    }
}
```

`PickColorAndApply` (lines 200-220) is **already** in the file — no helper to
add.

**Help-icon decision:** the manager doesn't use `HelpIconButtonStyle` and has
no help handler. To keep the in-main editor's help text available, the
simplest path is: replace the help icon with a static caption (the existing
70s safety warning text is already in the placeholder border). No new
code-behind needed.

## Section 4: MainWindow Cleanup

### `MainWindow.xaml` — delete

| Lines | What |
|---|---|
| 6637-6657 | Outer `Border` with `Style.Triggers` showing wardrobe/avatar-triggers workspace |
| 6660-6714 | Sibling `StackPanel` that collapses on `IsViewingWardrobe` |
| 6729-6991 | The wardrobe `StackPanel` (the whole editor) |
| 4486-4494 | The workspace-hide logic with `IsViewingWardrobe` branch |

### `MainWindowViewModel.cs` — delete

| Lines | Symbol |
|---|---|
| 441-449 | All wardrobe state fields |
| 492-493 | Re-entrancy guard fields |
| 638-642 | `RewardSyncModeOptions` field initializer |
| 807 | `ParameterTypes` field initializer |
| 810 | `BoolValueOptions` field initializer |
| 929 | `ShowWardrobeCommand = new RelayCommand(ShowWardrobe)` |
| 952-961 | All 10 wardrobe command field assignments |
| 1447 | `IsViewingWardrobe` property |
| 20572-20584 | `RuleListView.Wardrobe` enum member + any `case RuleListView.Wardrobe:` branches |
| 2545-2564 | `SelectedWardrobeOutfit` property |
| 2582-2606 | `SelectedWardrobeSnapshotParam` property |
| 2608-2624 | `SelectedWardrobeParameterOption` property |
| 2626-2637 | `WardrobeParameterText` property |
| 2639-2897 | All wardrobe helpers |
| 2886-2897 | `SetWardrobeParameterText` |
| 2899-2903 | `AvailableWardrobeParameters` property |
| 3473 | `ShowWardrobeCommand` property declaration |
| 5566-5571 | `ShowWardrobe()` method |
| 6733-6798 | `AddWardrobeOutfit` / `RemoveWardrobeOutfit` / `TestWardrobeOutfitAsync` |
| 6800-6940 | All Add/Remove/Copy/Paste/Clone helpers for wardrobe |
| 6942-6959 | Old `RefreshWardrobeParametersAsync` |
| 8169-8223 | The `IsViewingWardrobe` raise in `SwitchRuleView` and the `if (IsViewingWardrobe)` block |

**Keep on `MainWindowViewModel`** (runtime bridges the manager calls):
- `TestAvatarSet(profile)` (line 5533) — card-level Test
- `RetireWardrobeManagedReward(outfit)` (line 7053) — called by manager `DeleteOutfitFrom`
- `QueueManagedRewardSyncPublic()` (line 7063)
- `LoadAvatarParameterSummariesAsync(avatarId)` (line 6961)
- `LoadTwitchCustomRewardsAsync(...)` (line 7013)
- `TryGetVrChatAvatarThumbnailUrl(avatarId)` (line 7043)
- `BridgeRuntimeConfiguration.TryToWardrobeSnapshot` (unchanged)
- `WardrobeExecutorService` (unchanged)
- The `RewardSyncModeOptions` / `ParameterTypes` / `BoolValueOptions` property
  declarations at lines 1085, 1348, 1354 — keep as auto-properties (drop the
  field initializers). The manager does NOT use these from MainWindow; it
  exposes its own. The declarations stay for any leftover binding back-compat.

### `MainWindow.xaml.cs` cleanup

The `OnHelpButtonClicked` handler (line 322) is referenced from line 6734
in the old wardrobe editor. After the editor XAML is deleted, the handler
may be orphaned. **Keep it** if anything else uses it (the rule editor,
cash payments, etc.). Verify with grep; delete if orphaned.

## Section 5: Verification Plan

### Build

```powershell
dotnet build "E:\!!!Program to work on\Proper Crystal Relay\VrcTwitchOscBridge\VrcTwitchOscBridge.csproj" --no-restore
```

Must succeed with zero warnings introduced.

### Launch

```powershell
& "E:\!!!Program to work on\Proper Crystal Relay\Launch-Crystal-Relay-Debug.bat"
```

Title bar must show `- DEBUG`.

### Migration smoke test

1. Open an existing save that has Wardrobe outfits from 3.1.9 beta 1.
2. Open the Avatar Sets manager.
3. Click an Avatar Set card that has Wardrobe mode on.
4. Verify the outfit list appears in Step 4.
5. Click an outfit → verify Name / IsEnabled / ActiveTime / SyncMode all
   populate.
6. Verify the color swatches render green/red.
7. Add a new outfit → verify it appears in the list and the runtime-config
   bridge fires.
8. Toggle SyncMode to Link → verify the AvailableTwitchRewards ComboBox
   appears and the Color pickers / Title / Cost disappear.
9. Add a parameter → verify the type-aware value editor appears.
10. Click "Test Outfit" → verify the runtime executor runs.

### New-save test

1. Fresh install. Create a new Avatar Set, switch to Wardrobe mode.
2. Add an outfit, pick a color (modify the default green), save.
3. Close, reopen, verify the colors persisted as `#XXXXXX` hex.

### Backwards-compat test

1. Open a save from before this change (no color fields).
2. Verify the colors default to green/red.
3. Verify no `NullReferenceException` in the editor.

### Bridge/restore test

1. With a real (or mock) VRChat avatar that has LocalAvatarData, trigger
   an outfit.
2. Wait 70s, verify restore packets fire.

### Grep audit

```powershell
rg "IsViewingWardrobe" "E:\!!!Program to work on\Proper Crystal Relay" --type cs --type xaml
rg "ShowWardrobeCommand" "E:\!!!Program to work on\Proper Crystal Relay" --type cs --type xaml
rg "selectedWardrobeOutfit" "E:\!!!Program to work on\Proper Crystal Relay" --type cs
```

All must return zero hits.

### No-regressions

- Avatar Set mode (non-Wardrobe) cards still work.
- Master reward (in profile) still appears.
- The card-level Test button (line 628 of manager VM) still tests the
  first outfit of a Wardrobe-mode profile.
- Theme switching still applies (`OnThemeManagerThemeChanged`).

## Risks and Mitigations

| Risk | Mitigation |
|---|---|
| Missed XAML reference (a binding still pointing to a removed MainWindowViewModel property) | Build will fail with a binding error. Sweep `rg "Wardrobe"` before/after. |
| Color picker dialog crashes on some Windows themes | `PickColorAndApply` is already used for rules — no new code, just a new caller. |
| Re-entrancy loop in the new param picker on the manager | Port the exact same `isRestoringWardrobeParameterSelection` / `isRestoringWardrobeParameterText` guards. |
| `WardrobeParameterSourceParameters` private field in MainWindowViewModel was written to but never exposed as a property; the manager needs the same private state | Add the private field on the manager; no public surface needed. The XAML only binds to `AvailableWardrobeParameters` (filtered). |
| `WardrobeParameterText` setter cascades to refresh `AvailableWardrobeParameters`, which can re-set the text, which can recurse | Already guarded by `isRestoringWardrobeParameterText` in the source VM. Mirror it. |
| `Style.Triggers` with `DataTrigger` referencing a non-existent property in the new editor | Build will fail. Test with a fresh open of the manager. |
| Removing `IsViewingWardrobe` breaks an unreferenced branch in `SwitchRuleView` | Grep before deleting. |
| The existing `TestWardrobeOutfitAsync` body uses `bridgeRefreshGate` and `bridgeCoordinator` — both are private fields on `MainWindowViewModel`. The new `TestWardrobeOutfitPublicAsync` must use them too | Refactor into a shared private helper as noted in Section 3.7. |
| The outfit detail editor inside Step 4 is nested 2 levels deep; binding errors might be hard to spot | Use `{Binding DataContext.X, RelativeSource={RelativeSource AncestorType=Window}}` consistently. The placeholder already follows this pattern. |
| `_outfitSyncProperties` was missing the color fields, so changing a color wouldn't fire `QueueManagedRewardSyncPublic` | Section 2 ("Extend `_outfitSyncProperties`") fixes this. |
| The new editor sits inside a 480px slide-out; the multi-param row might overflow horizontally on small widths | Use `WrapPanel` for the param sub-toolbar, `UniformGrid Columns="2"` for Type/Value so they stack. |
| `TestAvatarSet` (card-level Test) calls `SelectedWardrobeOutfit = firstOutfit;` which is now on the manager, not on `MainWindowViewModel` | Card-level Test still goes through `_mainVm.TestAvatarSet(profile)` (line 628 of manager VM). The main VM's `TestAvatarSet` still has its body intact — no change needed. |

## CHANGELOG / Release Notes

Append to `RELEASE-CHANGE-RECORD.txt` under `Changed`:
- "Moved Wardrobe editor from main window into the Avatar Sets manager
  (Step 4) with per-outfit managed reward ready/cooldown color pickers and
  a full Create vs Link existing reward branch."

Mirror to `CHANGELOG.txt` under the current `v3.1.9 beta 2` section.

## AGENTS.md

Update `Project Identity` to:
- `Active development build`: `3.1.9` (unchanged)
- `Active build lane`: `beta2` (unchanged, since this is part of the same
  3.1.9 beta work)

If the user wants a test build after this lands, bump to a fresh version
per the `AGENTS.md` "Versioning Rules" — but that is out of scope for the
spec.
