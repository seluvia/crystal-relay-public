# Wardrobe Editor Migration Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Move the Wardrobe editor from `MainWindow.xaml:6729-6991` into the Step 4 slot of `AvatarSetsManagerWindow.xaml:1400-1501`, add per-outfit managed reward ready/cooldown color pickers, and delete the old in-main editor.

**Architecture:** Lift-and-shift migration. The runtime (WardrobeExecutorService, BridgeRuntimeConfiguration.TryToWardrobeSnapshot) is already wired and unchanged. We move UI state from MainWindowViewModel into AvatarSetsManagerViewModel, add two color fields to WardrobeOutfit (mirror TriggerRule), and replace the Step 4 placeholder XAML with the full editor.

**Tech Stack:** C# / WPF / .NET 10 / ObservableObject pattern / ManagedRewardPresentation for color normalization.

**Spec:** `docs/superpowers/specs/2026-06-12-wardrobe-editor-migration-design.md`

**Working directory:** `E:\!!!Program to work on\Proper Crystal Relay`

**Active build:** 3.1.9 (beta2). No version bump in this plan; version bump happens when the user asks for a test/beta/release build.

---

## File Structure

| File | Role | Change |
|---|---|---|
| `Models\WardrobeOutfit.cs` | Per-outfit data | Add 2 color fields + 2 brushes |
| `Services\SettingsStore.cs` | JSON round-trip | Add 2 fields to PersistedWardrobeOutfit + ToPersisted/FromPersisted |
| `ViewModels\MainWindowViewModel.cs` | Main runtime VM | Delete wardrobe state; add 1 public test method |
| `ViewModels\AvatarSetsManagerViewModel.cs` | Manager VM | Add 9 fields, 7 properties, 8 commands, ~14 helpers |
| `MainWindow.xaml` | Main window XAML | Delete wardrobe editor blocks |
| `AvatarSetsManagerWindow.xaml` | Manager window XAML | Replace Step 4 placeholder with full editor |
| `AvatarSetsManagerWindow.xaml.cs` | Manager code-behind | Add 4 color picker handlers |
| `MainWindow.xaml.cs` | Main code-behind | (optional) Remove orphaned OnHelpButtonClicked if unused |
| `RELEASE-CHANGE-RECORD.txt` | Release notes | Append "Changed" entry |
| `CHANGELOG.txt` | Public changelog | Mirror to v3.1.9 beta 2 section |

**No new files. No csproj changes. No new converters. No new themes.**

---

## Task 1: Add color fields to `WardrobeOutfit` model

**Files:**
- Modify: `VrcTwitchOscBridge\Models\WardrobeOutfit.cs` (add usings, 2 fields, 2 properties, 2 brush properties)

**Reference:** Mirror `Models\TriggerRule.cs:65-66, 258-282, 1315-1317` (the existing pattern). The `CreateColorBrush` helper is inherited from `ObservableObject` — do not redefine it.

- [ ] **Step 1: Add the WPF using statements at the top of `WardrobeOutfit.cs`**

After the existing `using VrcTwitchOscBridge.Services;` (line 5), add:
```csharp
using System.Windows.Media;
using Brush = System.Windows.Media.Brush;
```

- [ ] **Step 2: Add the two private fields**

After the existing `private ObservableCollection<WardrobeSnapshotParam> snapshotParams = [];` (line 23), add:
```csharp
private string managedRewardReadyColor = ManagedRewardPresentation.ReadyBackgroundColor;
private string managedRewardCooldownColor = ManagedRewardPresentation.InUseBackgroundColor;
```

- [ ] **Step 3: Add the two public color properties and two brush properties**

After the `ChatCommandText` property (line 123, before the `SnapshotParams` property block), add:
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

- [ ] **Step 4: Build to verify**

Run from repo root:
```powershell
dotnet build "E:\!!!Program to work on\Proper Crystal Relay\VrcTwitchOscBridge\VrcTwitchOscBridge.csproj" --no-restore
```

Expected: Build succeeds. Zero new warnings. The `ManagedRewardReadyColor` and `ManagedRewardCooldownColor` properties are now available on every `WardrobeOutfit` instance.

- [ ] **Step 5: Commit**

```bash
git add "VrcTwitchOscBridge/Models/WardrobeOutfit.cs"
git commit -m "feat(wardrobe): add per-outfit managed reward ready/cooldown colors

Mirror the TriggerRule pattern. Color values normalize through
ManagedRewardPresentation so the setters accept any case/whitespace
and fall back to the standard green/red on invalid input."
```

---

## Task 2: Persist the new color fields in `SettingsStore`

**Files:**
- Modify: `VrcTwitchOscBridge\Services\SettingsStore.cs` (3 spots: PersistedWardrobeOutfit, ToPersistedWardrobeOutfit, ToWardrobeOutfit)

- [ ] **Step 1: Add the two fields to `PersistedWardrobeOutfit`**

Find the `PersistedWardrobeOutfit` class (around line 2946-2969). After `public TwitchRewardSyncMode TwitchRewardSyncMode { get; set; }` (line 2964), add:
```csharp
public string? ManagedRewardReadyColor { get; set; }
public string? ManagedRewardCooldownColor { get; set; }
```

- [ ] **Step 2: Update `ToPersistedWardrobeOutfit` to write the colors**

Find `ToPersistedWardrobeOutfit` (around line 1064-1080). After `TwitchRewardSyncMode = outfit.TwitchRewardSyncMode,` (line 1076), add:
```csharp
ManagedRewardReadyColor = outfit.ManagedRewardReadyColor,
ManagedRewardCooldownColor = outfit.ManagedRewardCooldownColor,
```

- [ ] **Step 3: Update `ToWardrobeOutfit` to read the colors with safe defaults**

Find `ToWardrobeOutfit` (around line 1249-1269). After `TwitchRewardSyncMode = Enum.IsDefined(persisted.TwitchRewardSyncMode)` block (around line 1263-1265), add:
```csharp
ManagedRewardReadyColor = ManagedRewardPresentation.NormalizeReadyBackgroundColor(persisted.ManagedRewardReadyColor),
ManagedRewardCooldownColor = ManagedRewardPresentation.NormalizeCooldownBackgroundColor(persisted.ManagedRewardCooldownColor),
```

- [ ] **Step 4: Build to verify**

```powershell
dotnet build "E:\!!!Program to work on\Proper Crystal Relay\VrcTwitchOscBridge\VrcTwitchOscBridge.csproj" --no-restore
```

Expected: Build succeeds. The two new fields round-trip through save/load. Old saves without the fields get green/red defaults via `NormalizeReadyBackgroundColor(null)` and `NormalizeCooldownBackgroundColor(null)`.

- [ ] **Step 5: Commit**

```bash
git add "VrcTwitchOscBridge/Services/SettingsStore.cs"
git commit -m "feat(wardrobe): persist managed reward colors

PersistedWardrobeOutfit gains two nullable color fields. Old saves
load with green/red defaults via ManagedRewardPresentation normalization."
```

---

## Task 3: Add `TestWardrobeOutfitPublicAsync` to `MainWindowViewModel`

**Files:**
- Modify: `VrcTwitchOscBridge\ViewModels\MainWindowViewModel.cs` (refactor existing TestWardrobeOutfitAsync into shared helper, add public overload)

- [ ] **Step 1: Refactor the existing `TestWardrobeOutfitAsync` (line 6756-6798) into a shared private helper**

Replace the existing method (lines 6756-6798):
```csharp
private async Task TestWardrobeOutfitAsync()
{
    if (SelectedWardrobeOutfit is null || SelectedAvatarProfile is null)
    {
        return;
    }

    await ReloadRuntimeConfigAsync();

    await bridgeRefreshGate.WaitAsync();
    try
    {
        await EnsureBridgeStateAsync(CancellationToken.None, allowOscOnly: true);

        if (!BridgeRuntimeConfiguration.TryToWardrobeSnapshot(
                SelectedWardrobeOutfit, SelectedAvatarProfile, out var snapshot))
        {
            BridgeStatus = "Wardrobe outfit test did not run: outfit has no valid parameters.";
            AppendLog("Could not test wardrobe outfit: outfit is missing valid parameter snapshots.");
            return;
        }

        var applied = await bridgeCoordinator.ExecuteWardrobeOutfitAsync(snapshot, CancellationToken.None);
        if (applied)
        {
            BridgeStatus = $"Sent test for wardrobe outfit '{snapshot.Name}'.";
        }
        else
        {
            BridgeStatus = "Wardrobe outfit test did not run: VRChat may not be connected or avatar cache may not be available.";
            AppendLog("Could not test wardrobe outfit: VRChat may not be connected or avatar cache may not be available.");
        }
    }
    catch (Exception ex)
    {
        BridgeStatus = "Wardrobe outfit test did not run.";
        AppendLog($"Could not test wardrobe outfit: {ex.Message}");
    }
    finally
    {
        bridgeRefreshGate.Release();
    }
}
```

With:
```csharp
private Task TestWardrobeOutfitAsync()
{
    return ExecuteTestWardrobeOutfitAsync(SelectedWardrobeOutfit, SelectedAvatarProfile, CancellationToken.None);
}

public Task TestWardrobeOutfitPublicAsync(
    WardrobeOutfit outfit,
    AvatarTriggerProfile profile,
    CancellationToken cancellationToken)
{
    return ExecuteTestWardrobeOutfitAsync(outfit, profile, cancellationToken);
}

private async Task ExecuteTestWardrobeOutfitAsync(
    WardrobeOutfit outfit,
    AvatarTriggerProfile profile,
    CancellationToken cancellationToken)
{
    if (outfit is null || profile is null)
    {
        return;
    }

    await ReloadRuntimeConfigAsync();

    await bridgeRefreshGate.WaitAsync();
    try
    {
        await EnsureBridgeStateAsync(cancellationToken, allowOscOnly: true);

        if (!BridgeRuntimeConfiguration.TryToWardrobeSnapshot(outfit, profile, out var snapshot))
        {
            BridgeStatus = "Wardrobe outfit test did not run: outfit has no valid parameters.";
            AppendLog("Could not test wardrobe outfit: outfit is missing valid parameter snapshots.");
            return;
        }

        var applied = await bridgeCoordinator.ExecuteWardrobeOutfitAsync(snapshot, cancellationToken);
        if (applied)
        {
            BridgeStatus = $"Sent test for wardrobe outfit '{snapshot.Name}'.";
        }
        else
        {
            BridgeStatus = "Wardrobe outfit test did not run: VRChat may not be connected or avatar cache may not be available.";
            AppendLog("Could not test wardrobe outfit: VRChat may not be connected or avatar cache may not be available.");
        }
    }
    catch (Exception ex)
    {
        BridgeStatus = "Wardrobe outfit test did not run.";
        AppendLog($"Could not test wardrobe outfit: {ex.Message}");
    }
    finally
    {
        bridgeRefreshGate.Release();
    }
}
```

- [ ] **Step 2: Build to verify**

```powershell
dotnet build "E:\!!!Program to work on\Proper Crystal Relay\VrcTwitchOscBridge\VrcTwitchOscBridge.csproj" --no-restore
```

Expected: Build succeeds. Both the card-level Test (which still calls `TestWardrobeOutfitCommand`) and the new public overload are wired through the same `ExecuteTestWardrobeOutfitAsync` helper.

- [ ] **Step 3: Commit**

```bash
git add "VrcTwitchOscBridge/ViewModels/MainWindowViewModel.cs"
git commit -m "refactor(wardrobe): extract test helper, add public overload

ExecuteTestWardrobeOutfitAsync is now a shared private method.
TestWardrobeOutfitAsync (used by the card-level Test button) and the
new TestWardrobeOutfitPublicAsync (used by the manager editor's
per-outfit Test button) both delegate to it."
```

---

## Task 4: Delete wardrobe state from `MainWindowViewModel`

**Files:**
- Modify: `VrcTwitchOscBridge\ViewModels\MainWindowViewModel.cs` (delete wardrobe fields, properties, commands, helpers; keep bridges)

- [ ] **Step 1: Delete the wardrobe state fields (lines 441-449)**

Delete:
```csharp
private WardrobeOutfit? selectedWardrobeOutfit;
private TriggerRule? selectedAvatarRule;
private WardrobeSnapshotParam? selectedWardrobeSnapshotParam;
private VrChatOscParameterSummary? selectedWardrobeParameterOption;
private string wardrobeParameterText = string.Empty;
private IReadOnlyList<VrChatOscParameterSummary> wardrobeParameterSourceParameters = [];
private IReadOnlyList<VrChatOscParameterSummary> availableWardrobeParameters = [];
private WardrobeOutfit? copiedWardrobeOutfit;
private WardrobeSnapshotParam? copiedWardrobeSnapshotParam;
```

Note: `selectedAvatarRule` (line 442) is shared with the TriggerRule editor. If removing the line 441-449 block breaks the rule editor, **keep** `selectedAvatarRule` and only delete the wardrobe-specific lines. Verify by searching for `selectedAvatarRule` in the rest of the file after deletion.

- [ ] **Step 2: Delete the re-entrancy guard fields (lines 492-493)**

Delete:
```csharp
private bool isRestoringWardrobeParameterSelection;
private bool isRestoringWardrobeParameterText;
```

- [ ] **Step 3: Delete the wardrobe field initializers**

Find and delete the initializers (do NOT delete the property declarations):
- `RewardSyncModeOptions = [...]` (lines 638-642, the array literal)
- `ParameterTypes = [OscParameterType.Bool, OscParameterType.Int, OscParameterType.Float];` (line 807)
- `BoolValueOptions = ["True", "False"];` (line 810)

After deletion, the property declarations at lines 1085, 1348, 1354 still exist as auto-properties. The manager does NOT depend on these; it exposes its own. They remain as back-compat shims for any leftover binding.

- [ ] **Step 4: Delete `ShowWardrobeCommand` field assignment (line 929)**

Delete:
```csharp
ShowWardrobeCommand = new RelayCommand(ShowWardrobe);
```

- [ ] **Step 5: Delete the 10 wardrobe command field assignments (lines 952-961)**

Delete:
```csharp
AddWardrobeOutfitCommand = new RelayCommand(AddWardrobeOutfit, () => (IsViewingAvatarTriggers || IsViewingWardrobe) && SelectedAvatarProfile is not null);
RemoveWardrobeOutfitCommand = new RelayCommand(RemoveWardrobeOutfit, () => (IsViewingAvatarTriggers || IsViewingWardrobe) && SelectedWardrobeOutfit is not null);
AddWardrobeSnapshotParamCommand = new RelayCommand(AddWardrobeSnapshotParam, () => SelectedWardrobeOutfit is not null);
RemoveWardrobeSnapshotParamCommand = new RelayCommand(RemoveWardrobeSnapshotParam, () => SelectedWardrobeSnapshotParam is not null);
CopyWardrobeOutfitCommand = new RelayCommand(CopyWardrobeOutfit, () => SelectedWardrobeOutfit is not null);
PasteWardrobeOutfitCommand = new RelayCommand(PasteWardrobeOutfit, () => SelectedAvatarProfile is not null && copiedWardrobeOutfit is not null);
CopyWardrobeSnapshotParamCommand = new RelayCommand(CopyWardrobeSnapshotParam, () => SelectedWardrobeSnapshotParam is not null);
PasteWardrobeSnapshotParamCommand = new RelayCommand(PasteWardrobeSnapshotParam, () => SelectedWardrobeOutfit is not null && copiedWardrobeSnapshotParam is not null);
RefreshWardrobeParametersCommand = new RelayCommand(async () => await RefreshWardrobeParametersAsync());
TestWardrobeOutfitCommand = new AsyncRelayCommand(TestWardrobeOutfitAsync, () => SelectedWardrobeOutfit is not null && SelectedAvatarProfile is not null);
```

- [ ] **Step 6: Delete the `IsViewingWardrobe` property (line 1447)**

Delete:
```csharp
public bool IsViewingWardrobe => activeRuleListView == RuleListView.Wardrobe;
```

- [ ] **Step 7: Delete the wardrobe enum member and any switch branches**

Find the `RuleListView` enum (around line 20572-20584). Delete the `Wardrobe` member:
```csharp
Wardrobe
```

Then search for `RuleListView.Wardrobe` and `case RuleListView.Wardrobe` across the file. Delete every match. Specifically check `SwitchRuleView` (around line 8169) and delete the `RaisePropertyChanged(nameof(IsViewingWardrobe))` line plus the `if (IsViewingWardrobe) { _ = RefreshWardrobeParametersAsync(); }` block.

- [ ] **Step 8: Delete the wardrobe property declarations**

Delete:
- `SelectedWardrobeOutfit` property (lines 2545-2564)
- `SelectedWardrobeSnapshotParam` property (lines 2582-2606)
- `SelectedWardrobeParameterOption` property (lines 2608-2624)
- `WardrobeParameterText` property (lines 2626-2637)
- `AvailableWardrobeParameters` property (lines 2899-2903)

- [ ] **Step 9: Delete the wardrobe helper methods**

Delete:
- `SelectedWardrobeSnapshotParamChanged` (lines 2639-2652)
- `CommitWardrobeParameterText` (lines 2654-2720)
- `RefreshWardrobeParameterOptions` (lines 2722-2751)
- `TryRepairSelectedWardrobeParameter` (lines 2753-2797)
- `BuildWardrobeParameterOptionsForType` (lines 2799-2817)
- `TryResolveWardrobeParameterInput` (lines 2819-2862)
- `StripWardrobeParameterDisplayTypeSuffix` (lines 2864-2884)
- `SetWardrobeParameterText` (lines 2886-2897)

- [ ] **Step 10: Delete `ShowWardrobeCommand` property declaration and `ShowWardrobe` method**

Delete:
- `public RelayCommand ShowWardrobeCommand { get; }` (line 3473)
- `private void ShowWardrobe()` method (lines 5566-5571)

- [ ] **Step 11: Delete wardrobe Add/Remove/Copy/Paste/Clone methods**

Delete:
- `AddWardrobeOutfit` (lines 6733-6742)
- `RemoveWardrobeOutfit` (lines 6744-6754)
- `AddWardrobeSnapshotParam` (lines 6800-6806)
- `RemoveWardrobeSnapshotParam` (lines 6808-6817)
- `CopyWardrobeOutfit` (lines 6819-6829)
- `PasteWardrobeOutfit` (lines 6831-6851)
- `CopyWardrobeSnapshotParam` (lines 6853-6863)
- `PasteWardrobeSnapshotParam` (lines 6865-6884)
- `CloneWardrobeOutfit` (lines 6886-6903)
- `CloneWardrobeSnapshotParam` (lines 6905-6914)
- `GetUniqueWardrobeCopyName` (lines 6916-6940)
- `RefreshWardrobeParametersAsync` (lines 6942-6959)

- [ ] **Step 12: Verify no orphan references remain**

Run from repo root:
```powershell
git grep -nE "selectedWardrobeOutfit|SelectedWardrobeOutfit|SelectedWardrobeSnapshotParam|SelectedWardrobeParameterOption|WardrobeParameterText|AvailableWardrobeParameters|ShowWardrobeCommand|IsViewingWardrobe|AddWardrobeOutfit|RemoveWardrobeOutfit|AddWardrobeSnapshotParam|RemoveWardrobeSnapshotParam|CopyWardrobeOutfit|PasteWardrobeOutfit|CopyWardrobeSnapshotParam|PasteWardrobeSnapshotParam|RefreshWardrobeParametersCommand|TestWardrobeOutfitCommand|RefreshWardrobeParametersAsync|ShowWardrobe|RuleListView.Wardrobe|wardrobeParameterSourceParameters|isRestoringWardrobeParameter|copiedWardrobeOutfit|copiedWardrobeSnapshotParam|StripWardrobeParameterDisplayTypeSuffix|BuildWardrobeParameterOptionsForType|TryRepairSelectedWardrobeParameter|RefreshWardrobeParameterOptions|CommitWardrobeParameterText|SetWardrobeParameterText|TryResolveWardrobeParameterInput|SelectedWardrobeSnapshotParamChanged|CloneWardrobeOutfit|CloneWardrobeSnapshotParam|GetUniqueWardrobeCopyName" -- "VrcTwitchOscBridge/ViewModels/MainWindowViewModel.cs"
```

Expected: Zero hits (except `TestWardrobeOutfitCommand` may appear in `TestAvatarSet` — verify and keep only that one). If anything remains, delete it and re-run.

- [ ] **Step 13: Build to verify**

```powershell
dotnet build "E:\!!!Program to work on\Proper Crystal Relay\VrcTwitchOscBridge\VrcTwitchOscBridge.csproj" --no-restore
```

Expected: Build succeeds. The runtime bridges (`TestAvatarSet`, `RetireWardrobeManagedReward`, `QueueManagedRewardSyncPublic`, `LoadAvatarParameterSummariesAsync`, `LoadTwitchCustomRewardsAsync`, `TryGetVrChatAvatarThumbnailUrl`) are intact.

- [ ] **Step 14: Commit**

```bash
git add "VrcTwitchOscBridge/ViewModels/MainWindowViewModel.cs"
git commit -m "refactor(wardrobe): remove old in-main editor state from MainWindowViewModel

Move wardrobe editor state to AvatarSetsManagerViewModel. Keep runtime
bridges (TestAvatarSet, RetireWardrobeManagedReward, LoadAvatarParameter
SummariesAsync, LoadTwitchCustomRewardsAsync, TryGetVrChatAvatar
ThumbnailUrl) so the manager can still call into the bridge.

The TriggerRule editor (IsViewingAvatarTriggers, SelectedAvatarRule,
RuleListView.AvatarTriggers) is unaffected."
```

---

## Task 5: Delete the in-main wardrobe editor XAML

**Files:**
- Modify: `VrcTwitchOscBridge\MainWindow.xaml` (delete lines 6637-6714, 6729-6991; update 4486-4494)

- [ ] **Step 1: Delete the outer Border with style triggers (lines 6637-6657)**

Delete the entire `Border` block that contains the `Style.Triggers` showing/hiding the wardrobe/avatar-triggers workspace. The block starts with the `<Border>` opening tag at line 6637 and ends with `</Border>` at line 6657.

- [ ] **Step 2: Delete the sibling StackPanel (lines 6660-6714)**

Delete the sibling `<StackPanel>` that collapses on `IsViewingWardrobe` (the "Redeems In This Avatar Set" section that the wardrobe editor used to share a parent with).

- [ ] **Step 3: Delete the wardrobe editor StackPanel (lines 6729-6991)**

Delete the entire wardrobe editor block (the one that starts with `<DockPanel>` containing the "Wardrobe" heading at line 6729 and ends with `</StackPanel>` at line 6991).

- [ ] **Step 4: Update the workspace hide logic (lines 4486-4494)**

Find the line that hides the rule workspace. It currently checks both `IsAvatarSetsManagerOpen` and `IsViewingWardrobe`. Remove the `IsViewingWardrobe` check. The line becomes a single-condition check on `IsAvatarSetsManagerOpen`.

- [ ] **Step 5: Verify no orphan references remain**

```powershell
git grep -nE "IsViewingWardrobe|Wardrobe" "VrcTwitchOscBridge/MainWindow.xaml"
```

Expected: Zero hits (or only the in-main rule editor's wardrobe-related text fields, if any — the rule editor itself is not wardrobe code, so there should be nothing).

- [ ] **Step 6: Build to verify**

```powershell
dotnet build "E:\!!!Program to work on\Proper Crystal Relay\VrcTwitchOscBridge\VrcTwitchOscBridge.csproj" --no-restore
```

Expected: Build succeeds. The old in-main wardrobe editor is gone.

- [ ] **Step 7: Commit**

```bash
git add "VrcTwitchOscBridge/MainWindow.xaml"
git commit -m "refactor(wardrobe): remove in-main wardrobe editor

The Wardrobe editor now lives inside AvatarSetsManagerWindow Step 4.
The old in-main editor XAML and its IsViewingWardrobe hide logic are deleted."
```

---

## Task 6: Add wardrobe state fields to `AvatarSetsManagerViewModel`

**Files:**
- Modify: `VrcTwitchOscBridge\ViewModels\AvatarSetsManagerViewModel.cs` (add 9 fields, 3 static list properties, extend SelectedWardrobeOutfit setter, add 4 dynamic properties)

- [ ] **Step 1: Add the 9 new state fields**

After the existing `_selectedWardrobeOutfit` field (line 233), add:
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

- [ ] **Step 2: Add the 3 static list properties (place near `ParameterTypeFilter` around line 61)**

After the `ParameterTypeFilter` property (around line 72), add:
```csharp
public IReadOnlyList<Models.OscParameterType> ParameterTypes { get; } =
    [Models.OscParameterType.Bool, Models.OscParameterType.Int, Models.OscParameterType.Float];

public IReadOnlyList<string> BoolValueOptions { get; } = ["True", "False"];

public IReadOnlyList<TwitchRewardSyncModeOption> RewardSyncModeOptions => _mainVm.RewardSyncModeOptions;
```

- [ ] **Step 3: Extend the `SelectedWardrobeOutfit` setter (line 234)**

Replace the existing setter:
```csharp
public Models.WardrobeOutfit? SelectedWardrobeOutfit
{
    get => _selectedWardrobeOutfit;
    set
    {
        if (SetProperty(ref _selectedWardrobeOutfit, value))
        {
            if (value != null) _selectedAvatarRule = null;
            RaisePropertyChanged(nameof(SelectedAvatarRule));
        }
    }
}
```

With:
```csharp
public Models.WardrobeOutfit? SelectedWardrobeOutfit
{
    get => _selectedWardrobeOutfit;
    set
    {
        if (SetProperty(ref _selectedWardrobeOutfit, value))
        {
            if (value != null) _selectedAvatarRule = null;
            RaisePropertyChanged(nameof(SelectedAvatarRule));

            SelectedWardrobeSnapshotParam = value?.SnapshotParams.FirstOrDefault();
            AddWardrobeSnapshotParamCommand.NotifyCanExecuteChanged();
            RemoveWardrobeSnapshotParamCommand.NotifyCanExecuteChanged();
            CopyWardrobeOutfitCommand.NotifyCanExecuteChanged();
            PasteWardrobeOutfitCommand.NotifyCanExecuteChanged();
            CopyWardrobeSnapshotParamCommand.NotifyCanExecuteChanged();
            PasteWardrobeSnapshotParamCommand.NotifyCanExecuteChanged();
            AddWardrobeOutfitCommand.NotifyCanExecuteChanged();
            DeleteWardrobeOutfitCommand.NotifyCanExecuteChanged();
            TestWardrobeOutfitCommand.NotifyCanExecuteChanged();
        }
    }
}
```

- [ ] **Step 4: Add the 4 dynamic properties (place after `SelectedWardrobeOutfit`)**

After the `SelectedWardrobeOutfit` property, add:
```csharp
public Models.WardrobeSnapshotParam? SelectedWardrobeSnapshotParam
{
    get => _selectedWardrobeSnapshotParam;
    set
    {
        var previous = _selectedWardrobeSnapshotParam;
        if (SetProperty(ref _selectedWardrobeSnapshotParam, value))
        {
            if (previous is not null)
            {
                previous.PropertyChanged -= SelectedWardrobeSnapshotParamChanged;
            }

            if (_selectedWardrobeSnapshotParam is not null)
            {
                _selectedWardrobeSnapshotParam.PropertyChanged += SelectedWardrobeSnapshotParamChanged;
            }

            RefreshWardrobeParameterOptions();
            RemoveWardrobeSnapshotParamCommand.NotifyCanExecuteChanged();
            CopyWardrobeSnapshotParamCommand.NotifyCanExecuteChanged();
            PasteWardrobeSnapshotParamCommand.NotifyCanExecuteChanged();
        }
    }
}

public Models.VrChatOscParameterSummary? SelectedWardrobeParameterOption
{
    get => _selectedWardrobeParameterOption;
    set
    {
        if (SetProperty(ref _selectedWardrobeParameterOption, value)
            && !_isRestoringWardrobeParameterSelection
            && SelectedWardrobeSnapshotParam is not null
            && value is not null)
        {
            if (SelectedWardrobeSnapshotParam.ParameterType != value.ParameterType)
                SelectedWardrobeSnapshotParam.ParameterType = value.ParameterType;
            SelectedWardrobeSnapshotParam.ParameterName = value.Address;
            SetWardrobeParameterText(value.DisplayLabel);
        }
    }
}

public string WardrobeParameterText
{
    get => _wardrobeParameterText;
    set
    {
        if (SetProperty(ref _wardrobeParameterText, value ?? string.Empty)
            && !_isRestoringWardrobeParameterText)
        {
            CommitWardrobeParameterText(value);
        }
    }
}

public IReadOnlyList<Models.VrChatOscParameterSummary> AvailableWardrobeParameters
{
    get => _availableWardrobeParameters;
    private set => SetProperty(ref _availableWardrobeParameters, value);
}
```

- [ ] **Step 5: Add a placeholder private method stub for `SelectedWardrobeSnapshotParamChanged`**

Without this, the build will fail because the setter subscribes to it. Add this stub at the bottom of the class (after the existing helpers, around line 850):
```csharp
private void SelectedWardrobeSnapshotParamChanged(object? sender, System.ComponentModel.PropertyChangedEventArgs e)
{
    if (_isRestoringWardrobeParameterSelection)
    {
        return;
    }

    if (ReferenceEquals(sender, SelectedWardrobeSnapshotParam)
        && (e.PropertyName == nameof(Models.WardrobeSnapshotParam.ParameterType)
            || e.PropertyName == nameof(Models.WardrobeSnapshotParam.ParameterName)))
    {
        RefreshWardrobeParameterOptions();
    }
}
```

Also add the other helper stubs (Task 8 fills them in with real bodies):
```csharp
private void RefreshWardrobeParameterOptions() => throw new NotImplementedException();
private void CommitWardrobeParameterText(string? rawText) => throw new NotImplementedException();
private void SetWardrobeParameterText(string text) => throw new NotImplementedException();
```

- [ ] **Step 6: Build to verify**

```powershell
dotnet build "E:\!!!Program to work on\Proper Crystal Relay\VrcTwitchOscBridge\VrcTwitchOscBridge.csproj" --no-restore
```

Expected: Build succeeds. The setter references `RefreshWardrobeParameterOptions`, `CommitWardrobeParameterText`, `SetWardrobeParameterText` (stubbed) and the new commands (will be added in Task 7). If the build complains about missing command properties, that's expected — proceed to Task 7.

- [ ] **Step 7: Commit**

```bash
git add "VrcTwitchOscBridge/ViewModels/AvatarSetsManagerViewModel.cs"
git commit -m "feat(wardrobe): add state fields and properties to AvatarSetsManagerViewModel

SelectedWardrobeOutfit now also drives SelectedWardrobeSnapshotParam
and notifies the new wardrobe commands. Stubs for the helpers
(RefreshWardrobeParameterOptions, CommitWardrobeParameterText,
SetWardrobeParameterText) are added to keep the build green until
Task 8 fills them in."
```

---

## Task 7: Add new commands to `AvatarSetsManagerViewModel`

**Files:**
- Modify: `VrcTwitchOscBridge\ViewModels\AvatarSetsManagerViewModel.cs` (8 new command declarations in constructor, 8 new command properties, extend _outfitSyncProperties)

- [ ] **Step 1: Declare the 8 new commands in the constructor (around line 158)**

After the existing wardrobe command declarations (lines 145-148), add:
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

- [ ] **Step 2: Add the 8 new command property declarations (around line 537)**

After the existing `SelectWardrobeOutfitCommand` property declaration (line 531), add:
```csharp
public RelayCommand AddWardrobeSnapshotParamCommand { get; }
public RelayCommand RemoveWardrobeSnapshotParamCommand { get; }
public RelayCommand CopyWardrobeOutfitCommand { get; }
public RelayCommand PasteWardrobeOutfitCommand { get; }
public RelayCommand CopyWardrobeSnapshotParamCommand { get; }
public RelayCommand PasteWardrobeSnapshotParamCommand { get; }
public AsyncRelayCommand RefreshWardrobeParametersCommand { get; }
public AsyncRelayCommand TestWardrobeOutfitCommand { get; }
```

- [ ] **Step 3: Extend `_outfitSyncProperties` (line 807)**

Replace:
```csharp
private static readonly string[] _outfitSyncProperties =
{
    nameof(Models.WardrobeOutfit.Name),
    nameof(Models.WardrobeOutfit.IsEnabled),
    nameof(Models.WardrobeOutfit.TwitchRewardId),
    nameof(Models.WardrobeOutfit.TwitchRewardTitle),
    nameof(Models.WardrobeOutfit.TwitchRewardCost),
    nameof(Models.WardrobeOutfit.TwitchRewardSyncMode)
};
```

With:
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

- [ ] **Step 4: Add stub methods for the new commands (so build succeeds)**

Add these stubs at the bottom of the class (the real bodies come in Task 8):
```csharp
private void AddWardrobeSnapshotParam() => throw new NotImplementedException();
private void RemoveWardrobeSnapshotParam() => throw new NotImplementedException();
private void CopyWardrobeOutfit() => throw new NotImplementedException();
private void PasteWardrobeOutfit() => throw new NotImplementedException();
private void CopyWardrobeSnapshotParam() => throw new NotImplementedException();
private void PasteWardrobeSnapshotParam() => throw new NotImplementedException();
private Task RefreshWardrobeParametersAsync() => throw new NotImplementedException();
private Task TestWardrobeOutfitAsync() => throw new NotImplementedException();
```

- [ ] **Step 5: Build to verify**

```powershell
dotnet build "E:\!!!Program to work on\Proper Crystal Relay\VrcTwitchOscBridge\VrcTwitchOscBridge.csproj" --no-restore
```

Expected: Build succeeds. The 8 new commands are wired (executing them throws NotImplementedException until Task 8).

- [ ] **Step 6: Commit**

```bash
git add "VrcTwitchOscBridge/ViewModels/AvatarSetsManagerViewModel.cs"
git commit -m "feat(wardrobe): add new commands and sync triggers to AvatarSetsManagerViewModel

8 new wardrobe commands declared: param add/remove/copy/paste, outfit
copy/paste, refresh, test. _outfitSyncProperties extended to fire
QueueManagedRewardSyncPublic when ManagedRewardReadyColor or
ManagedRewardCooldownColor change."
```

---

## Task 8: Port helper method bodies from `MainWindowViewModel` to `AvatarSetsManagerViewModel`

**Files:**
- Modify: `VrcTwitchOscBridge\ViewModels\AvatarSetsManagerViewModel.cs` (replace ~14 stubs with real bodies)

- [ ] **Step 1: Replace `RefreshWardrobeParameterOptions` stub with real body**

Replace:
```csharp
private void RefreshWardrobeParameterOptions() => throw new NotImplementedException();
```

With:
```csharp
private void RefreshWardrobeParameterOptions()
{
    _isRestoringWardrobeParameterSelection = true;
    try
    {
        if (_selectedWardrobeSnapshotParam is null)
        {
            AvailableWardrobeParameters = [];
            _selectedWardrobeParameterOption = null;
            RaisePropertyChanged(nameof(SelectedWardrobeParameterOption));
            SetWardrobeParameterText(string.Empty);
            return;
        }

        TryRepairSelectedWardrobeParameter();
        var address = NormalizeAvatarParameterAddressOrEmpty(_selectedWardrobeSnapshotParam.ParameterName ?? string.Empty);
        AvailableWardrobeParameters = BuildWardrobeParameterOptionsForType(
            _selectedWardrobeSnapshotParam.ParameterType,
            _selectedWardrobeSnapshotParam.ParameterName ?? string.Empty);
        var match = AvailableWardrobeParameters.FirstOrDefault(p =>
            string.Equals(p.Address, address, StringComparison.Ordinal));
        _selectedWardrobeParameterOption = match;
        RaisePropertyChanged(nameof(SelectedWardrobeParameterOption));
        SetWardrobeParameterText(match?.DisplayLabel ?? _selectedWardrobeSnapshotParam.ParameterName ?? string.Empty);
    }
    finally
    {
        _isRestoringWardrobeParameterSelection = false;
    }
}
```

- [ ] **Step 2: Replace `CommitWardrobeParameterText` stub**

Replace:
```csharp
private void CommitWardrobeParameterText(string? rawText) => throw new NotImplementedException();
```

With:
```csharp
private void CommitWardrobeParameterText(string? rawText)
{
    if (SelectedWardrobeSnapshotParam is not { } selectedParam)
    {
        return;
    }

    var text = rawText?.Trim() ?? string.Empty;
    if (string.IsNullOrWhiteSpace(text))
    {
        if (!string.IsNullOrWhiteSpace(selectedParam.ParameterName))
        {
            selectedParam.ParameterName = string.Empty;
        }

        RefreshWardrobeParameterOptions();
        return;
    }

    var changed = false;
    if (TryResolveWardrobeParameterInput(
            text,
            selectedParam.ParameterType,
            out var resolvedAddress,
            out var resolvedType,
            out var matchedOption))
    {
        if (selectedParam.ParameterType != resolvedType)
        {
            selectedParam.ParameterType = resolvedType;
            changed = true;
        }

        if (!string.Equals(selectedParam.ParameterName?.Trim(), resolvedAddress, StringComparison.Ordinal))
        {
            selectedParam.ParameterName = resolvedAddress;
            changed = true;
        }

        if (matchedOption is not null)
        {
            _selectedWardrobeParameterOption = matchedOption;
            RaisePropertyChanged(nameof(SelectedWardrobeParameterOption));
        }
    }
    else
    {
        var cleanedText = StripWardrobeParameterDisplayTypeSuffix(text, out var parsedType);
        if (parsedType is Models.OscParameterType supportedType && selectedParam.ParameterType != supportedType)
        {
            selectedParam.ParameterType = supportedType;
            changed = true;
        }

        var normalizedAddress = NormalizeAvatarParameterAddressOrEmpty(cleanedText);
        if (!string.Equals(selectedParam.ParameterName?.Trim(), normalizedAddress, StringComparison.Ordinal))
        {
            selectedParam.ParameterName = normalizedAddress;
            changed = true;
        }
    }

    if (!changed)
    {
        RefreshWardrobeParameterOptions();
    }
}
```

- [ ] **Step 3: Replace `SetWardrobeParameterText` stub**

Replace:
```csharp
private void SetWardrobeParameterText(string text) => throw new NotImplementedException();
```

With:
```csharp
private void SetWardrobeParameterText(string text)
{
    _isRestoringWardrobeParameterText = true;
    try
    {
        WardrobeParameterText = text ?? string.Empty;
    }
    finally
    {
        _isRestoringWardrobeParameterText = false;
    }
}
```

- [ ] **Step 4: Add `TryRepairSelectedWardrobeParameter` (was stubbed as `SelectedWardrobeSnapshotParamChanged` already has a real body)**

Add below the `SelectedWardrobeSnapshotParamChanged` method:
```csharp
private void TryRepairSelectedWardrobeParameter()
{
    if (_selectedWardrobeSnapshotParam is null)
    {
        return;
    }

    var rawName = _selectedWardrobeSnapshotParam.ParameterName?.Trim() ?? string.Empty;
    if (string.IsNullOrWhiteSpace(rawName))
    {
        return;
    }

    if (TryResolveWardrobeParameterInput(
            rawName,
            _selectedWardrobeSnapshotParam.ParameterType,
            out var resolvedAddress,
            out var resolvedType,
            out _))
    {
        if (_selectedWardrobeSnapshotParam.ParameterType != resolvedType)
        {
            _selectedWardrobeSnapshotParam.ParameterType = resolvedType;
        }

        if (!string.Equals(_selectedWardrobeSnapshotParam.ParameterName?.Trim(), resolvedAddress, StringComparison.Ordinal))
        {
            _selectedWardrobeSnapshotParam.ParameterName = resolvedAddress;
        }

        return;
    }

    var cleanedName = StripWardrobeParameterDisplayTypeSuffix(rawName, out var parsedType);
    if (parsedType is Models.OscParameterType supportedType && _selectedWardrobeSnapshotParam.ParameterType != supportedType)
    {
        _selectedWardrobeSnapshotParam.ParameterType = supportedType;
    }

    var normalizedAddress = NormalizeAvatarParameterAddressOrEmpty(cleanedName);
    if (!string.Equals(_selectedWardrobeSnapshotParam.ParameterName?.Trim(), normalizedAddress, StringComparison.Ordinal))
    {
        _selectedWardrobeSnapshotParam.ParameterName = normalizedAddress;
    }
}
```

- [ ] **Step 5: Add `BuildWardrobeParameterOptionsForType`**

```csharp
private List<Models.VrChatOscParameterSummary> BuildWardrobeParameterOptionsForType(
    Models.OscParameterType parameterType,
    string selectedParameterName)
{
    var nextOptions = _wardrobeParameterSourceParameters
        .Where(parameter => parameter.ParameterType == parameterType)
        .OrderBy(parameter => parameter.Name, StringComparer.OrdinalIgnoreCase)
        .ToList();

    var cleanedName = StripWardrobeParameterDisplayTypeSuffix(selectedParameterName ?? string.Empty, out _);
    var selectedParameterAddress = NormalizeAvatarParameterAddressOrEmpty(cleanedName);
    if (!string.IsNullOrWhiteSpace(selectedParameterAddress)
        && !nextOptions.Any(option => string.Equals(option.Address, selectedParameterAddress, StringComparison.Ordinal)))
    {
        nextOptions.Insert(0, CreateCustomAvatarParameterOption(selectedParameterAddress, parameterType));
    }

    return nextOptions;
}
```

- [ ] **Step 6: Add `TryResolveWardrobeParameterInput`**

```csharp
private bool TryResolveWardrobeParameterInput(
    string rawText,
    Models.OscParameterType preferredType,
    out string address,
    out Models.OscParameterType parameterType,
    out Models.VrChatOscParameterSummary? matchedOption)
{
    address = string.Empty;
    parameterType = preferredType;
    matchedOption = null;

    var trimmedText = rawText?.Trim() ?? string.Empty;
    if (string.IsNullOrWhiteSpace(trimmedText))
    {
        return false;
    }

    var cleanedText = StripWardrobeParameterDisplayTypeSuffix(trimmedText, out var parsedType);
    var normalizedAddress = NormalizeAvatarParameterAddressOrEmpty(cleanedText);
    var sourceParameters = _wardrobeParameterSourceParameters.Count > 0
        ? _wardrobeParameterSourceParameters
        : _availableWardrobeParameters;
    var candidates = parsedType is Models.OscParameterType parsed
        ? sourceParameters.Where(parameter => parameter.ParameterType == parsed)
        : sourceParameters
            .Where(parameter => parameter.ParameterType == preferredType)
            .Concat(sourceParameters.Where(parameter => parameter.ParameterType != preferredType));

    matchedOption = candidates.FirstOrDefault(parameter =>
        string.Equals(parameter.Address, trimmedText, StringComparison.OrdinalIgnoreCase)
        || string.Equals(parameter.Address, normalizedAddress, StringComparison.OrdinalIgnoreCase)
        || string.Equals(parameter.Name, trimmedText, StringComparison.OrdinalIgnoreCase)
        || string.Equals(parameter.Name, cleanedText, StringComparison.OrdinalIgnoreCase)
        || string.Equals(parameter.DisplayLabel, trimmedText, StringComparison.OrdinalIgnoreCase));

    if (matchedOption is null)
    {
        return false;
    }

    address = matchedOption.Address;
    parameterType = matchedOption.ParameterType;
    return true;
}
```

- [ ] **Step 7: Add `StripWardrobeParameterDisplayTypeSuffix`**

```csharp
private static string StripWardrobeParameterDisplayTypeSuffix(string rawText, out Models.OscParameterType? parsedType)
{
    parsedType = null;
    var text = rawText?.Trim() ?? string.Empty;
    if (string.IsNullOrWhiteSpace(text))
    {
        return string.Empty;
    }

    foreach (var parameterType in new[] { Models.OscParameterType.Bool, Models.OscParameterType.Int, Models.OscParameterType.Float })
    {
        var suffix = $" [{parameterType}]";
        if (text.EndsWith(suffix, StringComparison.OrdinalIgnoreCase))
        {
            parsedType = parameterType;
            return text[..^suffix.Length].Trim();
        }
    }

    return text;
}
```

- [ ] **Step 8: Replace the param/outfit command stubs with real bodies**

Replace each stub one at a time. The bodies are direct ports from `MainWindowViewModel.cs:6800-6940`, with `SelectedAvatarProfile` swapped to `SelectedProfile`.

Replace `AddWardrobeSnapshotParam` stub:
```csharp
private void AddWardrobeSnapshotParam()
{
    if (SelectedWardrobeOutfit is null) return;
    var param = new Models.WardrobeSnapshotParam();
    SelectedWardrobeOutfit.SnapshotParams.Add(param);
    SelectedWardrobeSnapshotParam = param;
}
```

Replace `RemoveWardrobeSnapshotParam` stub:
```csharp
private void RemoveWardrobeSnapshotParam()
{
    if (SelectedWardrobeOutfit is null || SelectedWardrobeSnapshotParam is null) return;
    var param = SelectedWardrobeSnapshotParam;
    var index = SelectedWardrobeOutfit.SnapshotParams.IndexOf(param);
    SelectedWardrobeOutfit.SnapshotParams.Remove(param);
    SelectedWardrobeSnapshotParam = index < SelectedWardrobeOutfit.SnapshotParams.Count
        ? SelectedWardrobeOutfit.SnapshotParams[index]
        : SelectedWardrobeOutfit.SnapshotParams.FirstOrDefault();
}
```

Replace `CopyWardrobeOutfit` stub:
```csharp
private void CopyWardrobeOutfit()
{
    if (SelectedWardrobeOutfit is null) return;
    _copiedWardrobeOutfit = CloneWardrobeOutfit(SelectedWardrobeOutfit, clearRewardId: false, copyName: SelectedWardrobeOutfit.Name);
    PasteWardrobeOutfitCommand.NotifyCanExecuteChanged();
}
```

Replace `PasteWardrobeOutfit` stub:
```csharp
private void PasteWardrobeOutfit()
{
    if (SelectedProfile is null || _copiedWardrobeOutfit is null) return;

    var pastedName = GetUniqueWardrobeCopyName(_copiedWardrobeOutfit.Name, SelectedProfile.WardrobeOutfits.Select(outfit => outfit.Name));
    var outfit = CloneWardrobeOutfit(_copiedWardrobeOutfit, clearRewardId: true, copyName: pastedName);
    if (!string.IsNullOrWhiteSpace(outfit.TwitchRewardTitle))
    {
        outfit.TwitchRewardTitle = GetUniqueWardrobeCopyName(
            outfit.TwitchRewardTitle,
            SelectedProfile.WardrobeOutfits.Select(existing => existing.TwitchRewardTitle));
    }

    SelectedProfile.WardrobeOutfits.Add(outfit);
    SelectedWardrobeOutfit = outfit;
    SelectedWardrobeSnapshotParam = outfit.SnapshotParams.FirstOrDefault();
}
```

Replace `CopyWardrobeSnapshotParam` stub:
```csharp
private void CopyWardrobeSnapshotParam()
{
    if (SelectedWardrobeSnapshotParam is null) return;
    _copiedWardrobeSnapshotParam = CloneWardrobeSnapshotParam(SelectedWardrobeSnapshotParam);
    PasteWardrobeSnapshotParamCommand.NotifyCanExecuteChanged();
}
```

Replace `PasteWardrobeSnapshotParam` stub:
```csharp
private void PasteWardrobeSnapshotParam()
{
    if (SelectedWardrobeOutfit is null || _copiedWardrobeSnapshotParam is null) return;

    var param = CloneWardrobeSnapshotParam(_copiedWardrobeSnapshotParam);
    var insertIndex = SelectedWardrobeSnapshotParam is not null
        ? SelectedWardrobeOutfit.SnapshotParams.IndexOf(SelectedWardrobeSnapshotParam) + 1
        : SelectedWardrobeOutfit.SnapshotParams.Count;
    if (insertIndex < 0 || insertIndex > SelectedWardrobeOutfit.SnapshotParams.Count)
    {
        insertIndex = SelectedWardrobeOutfit.SnapshotParams.Count;
    }

    SelectedWardrobeOutfit.SnapshotParams.Insert(insertIndex, param);
    SelectedWardrobeSnapshotParam = param;
}
```

Replace `RefreshWardrobeParametersAsync` stub:
```csharp
private async Task RefreshWardrobeParametersAsync()
{
    if (SelectedProfile is null || string.IsNullOrWhiteSpace(SelectedProfile.AvatarId))
    {
        _wardrobeParameterSourceParameters = [];
        AvailableWardrobeParameters = [];
        RefreshWardrobeParameterOptions();
        return;
    }

    try
    {
        var parameters = await _mainVm.LoadAvatarParameterSummariesAsync(SelectedProfile.AvatarId);
        _wardrobeParameterSourceParameters = parameters;
        RefreshWardrobeParameterOptions();
    }
    catch
    {
        _wardrobeParameterSourceParameters = [];
        AvailableWardrobeParameters = [];
        RefreshWardrobeParameterOptions();
    }
}
```

Replace `TestWardrobeOutfitAsync` stub:
```csharp
private async Task TestWardrobeOutfitAsync()
{
    if (SelectedWardrobeOutfit is null || SelectedProfile is null) return;
    await _mainVm.TestWardrobeOutfitPublicAsync(SelectedWardrobeOutfit, SelectedProfile, CancellationToken.None);
}
```

- [ ] **Step 8.5: Add private static helpers `NormalizeAvatarParameterAddressOrEmpty` and `CreateCustomAvatarParameterOption`**

These are private static helpers on `MainWindowViewModel` (lines 19575, 19971) used by both the wardrobe editor and the supporter-override code. The manager can't reach them (they're private), so add a private copy to the manager. The duplication is small (4 lines) and matches the lift-and-shift approach.

Add at the bottom of the class:
```csharp
private static string NormalizeAvatarParameterAddressOrEmpty(string parameterName)
{
    return string.IsNullOrWhiteSpace(parameterName)
        ? string.Empty
        : Services.VrChatOscClient.NormalizeAvatarParameterAddress(parameterName);
}

private static Models.VrChatOscParameterSummary CreateCustomAvatarParameterOption(string parameterName, Models.OscParameterType parameterType)
{
    var normalizedAddress = Services.VrChatOscClient.NormalizeAvatarParameterAddress(parameterName);
    var displayName = normalizedAddress.Split('/', StringSplitOptions.RemoveEmptyEntries).LastOrDefault() ?? normalizedAddress;
    return new Models.VrChatOscParameterSummary(normalizedAddress, displayName, parameterType);
}
```

- [ ] **Step 9: Add `CloneWardrobeOutfit`, `CloneWardrobeSnapshotParam`, `GetUniqueWardrobeCopyName`**

```csharp
private static Models.WardrobeOutfit CloneWardrobeOutfit(Models.WardrobeOutfit source, bool clearRewardId, string copyName)
{
    return new Models.WardrobeOutfit
    {
        Id = Guid.NewGuid(),
        IsEnabled = source.IsEnabled,
        Name = string.IsNullOrWhiteSpace(copyName) ? "New Outfit Copy" : copyName.Trim(),
        ActiveTimeSeconds = source.ActiveTimeSeconds,
        TwitchRewardId = clearRewardId ? string.Empty : source.TwitchRewardId,
        TwitchRewardTitle = source.TwitchRewardTitle,
        TwitchRewardCost = source.TwitchRewardCost,
        TwitchRewardDescription = source.TwitchRewardDescription,
        TwitchRewardSyncMode = source.TwitchRewardSyncMode,
        ChatCommandText = source.ChatCommandText,
        ManagedRewardReadyColor = source.ManagedRewardReadyColor,
        ManagedRewardCooldownColor = source.ManagedRewardCooldownColor,
        SnapshotParams = new ObservableCollection<Models.WardrobeSnapshotParam>(
            source.SnapshotParams.Select(CloneWardrobeSnapshotParam))
    };
}

private static Models.WardrobeSnapshotParam CloneWardrobeSnapshotParam(Models.WardrobeSnapshotParam source)
{
    return new Models.WardrobeSnapshotParam
    {
        Id = Guid.NewGuid(),
        ParameterName = source.ParameterName,
        ParameterType = source.ParameterType,
        SetValue = source.SetValue
    };
}

private static string GetUniqueWardrobeCopyName(string sourceName, IEnumerable<string> existingNames)
{
    var baseName = string.IsNullOrWhiteSpace(sourceName) ? "New Outfit" : sourceName.Trim();
    if (!baseName.EndsWith(" Copy", StringComparison.OrdinalIgnoreCase))
    {
        baseName += " Copy";
    }

    var usedNames = existingNames
        .Where(name => !string.IsNullOrWhiteSpace(name))
        .Select(name => name.Trim())
        .ToHashSet(StringComparer.OrdinalIgnoreCase);
    if (!usedNames.Contains(baseName))
    {
        return baseName;
    }

    var index = 2;
    while (usedNames.Contains($"{baseName} {index}"))
    {
        index++;
    }

    return $"{baseName} {index}";
}
```

- [ ] **Step 10: Build to verify**

```powershell
dotnet build "E:\!!!Program to work on\Proper Crystal Relay\VrcTwitchOscBridge\VrcTwitchOscBridge.csproj" --no-restore
```

Expected: Build succeeds. The new commands and helpers are all functional.

- [ ] **Step 11: Commit**

```bash
git add "VrcTwitchOscBridge/ViewModels/AvatarSetsManagerViewModel.cs"
git commit -m "feat(wardrobe): port helper methods to AvatarSetsManagerViewModel

14 helper methods ported from MainWindowViewModel: param add/remove/
copy/paste, outfit copy/paste, refresh, test, plus the param picker
machinery (Refresh/Build/TryResolve/Strip/Commit/SetWardrobeParameterText)
and the 3 clone helpers. Refresh uses _mainVm.LoadAvatarParameterSummariesAsync.
Test uses _mainVm.TestWardrobeOutfitPublicAsync."
```

---

## Task 9: Replace the Step 4 placeholder in `AvatarSetsManagerWindow.xaml`

**Files:**
- Modify: `VrcTwitchOscBridge\AvatarSetsManagerWindow.xaml` (replace lines 1400-1501 with the full editor)

This is the largest single change. Read the existing Step 4 block (lines 1400-1501) before replacing.

- [ ] **Step 1: Read the existing Step 4 placeholder**

Open the file and locate lines 1400-1501. Confirm the structure matches the spec.

- [ ] **Step 2: Replace lines 1400-1501 with the new full editor**

Find:
```xaml
                            <!-- Wardrobe Mode: Wardrobe Outfits with add/edit/delete -->
                            <StackPanel Visibility="{Binding UseWardrobeMode, Converter={StaticResource BoolToVisibilityConverter}}">
```
(through line 1501 `</StackPanel>`)

Replace with:
```xaml
                            <!-- Wardrobe Mode: Wardrobe Outfits with add/edit/delete -->
                            <StackPanel Visibility="{Binding UseWardrobeMode, Converter={StaticResource BoolToVisibilityConverter}}">
                                <Grid Margin="0,0,0,10">
                                    <Grid.ColumnDefinitions>
                                        <ColumnDefinition Width="*" />
                                        <ColumnDefinition Width="Auto" />
                                    </Grid.ColumnDefinitions>
                                    <TextBlock Grid.Column="0"
                                               Text="{loc:Translate 'Avatar Sets Step 4 Outfits'}"
                                               Foreground="{DynamicResource TextBrush}"
                                               FontWeight="SemiBold"
                                               FontSize="14"
                                               VerticalAlignment="Center" />
                                    <Button Grid.Column="1"
                                            Content="+ Add Outfit"
                                            Style="{StaticResource AccentButtonStyle}"
                                            Padding="8,4"
                                            FontSize="10"
                                            Command="{Binding DataContext.AddWardrobeOutfitCommand, RelativeSource={RelativeSource AncestorType=Window}}"
                                            CommandParameter="{Binding}" />
                                </Grid>
                                <ItemsControl ItemsSource="{Binding WardrobeOutfits}">
                                    <ItemsControl.ItemTemplate>
                                        <DataTemplate>
                                            <Border Background="{DynamicResource PanelBrush}"
                                                    BorderBrush="{Binding DataContext.SelectedWardrobeOutfit, RelativeSource={RelativeSource AncestorType=Window}, Converter={StaticResource GuidToSelectedOutfitBrushConverter}, FallbackValue=#4B2B78}"
                                                    BorderThickness="1"
                                                    CornerRadius="8"
                                                    Padding="10,6"
                                                    Margin="0,0,0,4"
                                                    Cursor="Hand"
                                                    MouseLeftButtonUp="OnOutfitItemClicked"
                                                    Tag="{Binding}">
                                                <Grid>
                                                    <Grid.ColumnDefinitions>
                                                        <ColumnDefinition Width="*" />
                                                        <ColumnDefinition Width="Auto" />
                                                    </Grid.ColumnDefinitions>
                                                    <TextBlock Grid.Column="0"
                                                               Text="{Binding Name}"
                                                               Foreground="{DynamicResource TextBrush}"
                                                               FontSize="11"
                                                               VerticalAlignment="Center" />
                                                    <Button Grid.Column="1"
                                                            Content="✕"
                                                            Style="{StaticResource SecondaryButtonStyle}"
                                                            Padding="6,2"
                                                            FontSize="10"
                                                            Click="OnDeleteOutfitClicked"
                                                            Tag="{Binding}" />
                                                </Grid>
                                            </Border>
                                        </DataTemplate>
                                    </ItemsControl.ItemTemplate>
                                </ItemsControl>

                                <!-- Outfit editor (visible when an outfit is selected) -->
                                <Border Visibility="{Binding DataContext.SelectedWardrobeOutfit, RelativeSource={RelativeSource AncestorType=Window}, Converter={StaticResource NullToVisibilityConverter}}"
                                        Background="{DynamicResource NestedPanelBrush}"
                                        BorderBrush="{DynamicResource AccentBrush}"
                                        BorderThickness="1"
                                        CornerRadius="12"
                                        Padding="14"
                                        Margin="0,14,0,0">
                                    <StackPanel DataContext="{Binding DataContext.SelectedWardrobeOutfit, RelativeSource={RelativeSource AncestorType=Window}}">
                                        <TextBlock Text="Edit Outfit"
                                                   Foreground="{DynamicResource AccentBrush}"
                                                   FontWeight="Bold"
                                                   FontSize="13"
                                                   Margin="0,0,0,10" />
                                        <TextBlock Text="Active time is clamped to 70s at runtime so the LocalAvatarData snapshot can refresh. Set a value at or above 70s."
                                                   Foreground="{DynamicResource WarnTextBrush}"
                                                   FontSize="10"
                                                   TextWrapping="Wrap"
                                                   Margin="0,0,0,8" />
                                        <CheckBox IsChecked="{Binding IsEnabled, UpdateSourceTrigger=PropertyChanged}"
                                                  Content="Enabled"
                                                  Foreground="{DynamicResource TextBrush}"
                                                  Margin="0,0,0,8" />
                                        <TextBlock Text="Name"
                                                   Foreground="{DynamicResource TextBrush}"
                                                   FontSize="11"
                                                   Margin="0,0,0,4" />
                                        <TextBox Text="{Binding Name, UpdateSourceTrigger=PropertyChanged}"
                                                 Margin="0,0,0,8" />
                                        <TextBlock Text="Active Time (seconds)"
                                                   Foreground="{DynamicResource TextBrush}"
                                                   FontSize="11"
                                                   Margin="0,0,0,2" />
                                        <TextBox Text="{Binding ActiveTimeSeconds, UpdateSourceTrigger=PropertyChanged}"
                                                 Margin="0,0,0,4" />
                                        <TextBlock Text="Below 70s the runtime will clamp and extend the active window."
                                                   Foreground="{DynamicResource WarnTextBrush}"
                                                   FontSize="9"
                                                   TextWrapping="Wrap"
                                                   Margin="0,0,0,8"
                                                   Visibility="{Binding UsesShortActiveTime, Converter={StaticResource BoolToVisibilityConverter}}" />

                                        <TextBlock Text="Reward Sync Mode"
                                                   Foreground="{DynamicResource TextBrush}"
                                                   FontWeight="SemiBold"
                                                   FontSize="11"
                                                   Margin="0,4,0,4" />
                                        <TextBlock Text="Manage and Create: Crystal Relay creates the reward, hides/deletes it when not in use. Link to listen only: use an existing reward, listen-only."
                                                   Foreground="{DynamicResource MutedBrush}"
                                                   FontSize="10"
                                                   TextWrapping="Wrap"
                                                   Margin="0,0,0,6" />
                                        <ComboBox SelectedValue="{Binding TwitchRewardSyncMode, UpdateSourceTrigger=PropertyChanged}"
                                                  SelectedValuePath="Tag" Margin="0,0,0,8">
                                            <ComboBoxItem Content="Manage and Create" Tag="{x:Static models:TwitchRewardSyncMode.CreateOrManage}" />
                                            <ComboBoxItem Content="Link to listen only" Tag="{x:Static models:TwitchRewardSyncMode.LinkExisting}" />
                                        </ComboBox>

                                        <!-- LinkExisting mode -->
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
                                                <TextBlock Text="Existing Twitch Reward"
                                                           Foreground="{DynamicResource TextBrush}"
                                                           FontSize="11"
                                                           Margin="0,0,0,4" />
                                                <Button Grid.Column="1"
                                                        Content="↻"
                                                        ToolTip="Reload Twitch rewards"
                                                        Padding="6,2"
                                                        Margin="0,0,0,4"
                                                        Command="{Binding DataContext.LoadTwitchRewardsCommand, RelativeSource={RelativeSource AncestorType=Window}}"
                                                        Style="{StaticResource SecondaryButtonStyle}" />
                                            </Grid>
                                            <ComboBox ItemsSource="{Binding DataContext.AvailableTwitchRewards, RelativeSource={RelativeSource AncestorType=Window}}"
                                                      SelectedValue="{Binding TwitchRewardId, UpdateSourceTrigger=PropertyChanged}"
                                                      SelectedValuePath="Id"
                                                      DisplayMemberPath="Title"
                                                      IsEditable="False"
                                                      Margin="0,0,0,4" />
                                            <TextBlock Text="{Binding DataContext.TwitchRewardsLoadStatus, RelativeSource={RelativeSource AncestorType=Window}}"
                                                       Foreground="{DynamicResource MutedBrush}"
                                                       FontSize="10"
                                                       Margin="0,0,0,4" />
                                            <TextBlock Text="Reward ID (auto-filled when selected)"
                                                       Foreground="{DynamicResource MutedBrush}"
                                                       FontSize="10"
                                                       Margin="0,0,0,2" />
                                            <TextBox Text="{Binding TwitchRewardId, UpdateSourceTrigger=PropertyChanged}" IsReadOnly="True" Background="{DynamicResource NestedPanelBrush}" />
                                        </StackPanel>

                                        <!-- CreateOrManage mode -->
                                        <StackPanel Margin="0,0,0,8">
                                            <StackPanel.Style>
                                                <Style TargetType="StackPanel">
                                                    <Setter Property="Visibility" Value="Collapsed" />
                                                    <Style.Triggers>
                                                        <DataTrigger Binding="{Binding UsesCreateOrManageReward}" Value="True">
                                                            <Setter Property="Visibility" Value="Visible" />
                                                        </DataTrigger>
                                                    </Style.Triggers>
                                                </Style>
                                            </StackPanel.Style>
                                            <UniformGrid Columns="2" Margin="0,0,0,8">
                                                <StackPanel Margin="0,0,4,0">
                                                    <TextBlock Text="Reward Title"
                                                               Foreground="{DynamicResource TextBrush}"
                                                               FontSize="11"
                                                               Margin="0,0,0,2" />
                                                    <TextBox Text="{Binding TwitchRewardTitle, UpdateSourceTrigger=PropertyChanged}" />
                                                </StackPanel>
                                                <StackPanel Margin="4,0,0,0">
                                                    <TextBlock Text="Reward Cost"
                                                               Foreground="{DynamicResource TextBrush}"
                                                               FontSize="11"
                                                               Margin="0,0,0,2" />
                                                    <TextBox Text="{Binding TwitchRewardCost, UpdateSourceTrigger=PropertyChanged}" />
                                                </StackPanel>
                                            </UniformGrid>
                                            <TextBlock Text="Reward Description"
                                                       Foreground="{DynamicResource TextBrush}"
                                                       FontSize="11"
                                                       Margin="0,0,0,2" />
                                            <TextBox Text="{Binding TwitchRewardDescription, UpdateSourceTrigger=PropertyChanged}"
                                                     AcceptsReturn="True" TextWrapping="Wrap" Height="48"
                                                     Margin="0,0,0,8" />
                                            <TextBlock Text="Chat Command"
                                                       Foreground="{DynamicResource TextBrush}"
                                                       FontSize="11"
                                                       Margin="0,0,0,2" />
                                            <TextBox Text="{Binding ChatCommandText, UpdateSourceTrigger=PropertyChanged}" Margin="0,0,0,8" />

                                            <!-- Reward Background Colors (Ready / Cooldown) -->
                                            <TextBlock Text="Reward Colors"
                                                       Foreground="{DynamicResource TextBrush}"
                                                       FontSize="11"
                                                       FontWeight="SemiBold"
                                                       Margin="0,4,0,4" />
                                            <TextBlock Text="Ready color = the reward's background when available. Cooldown color = the reward's background while the timer is running or it's been redeemed. Twitch shows these on the channel points page."
                                                       Foreground="{DynamicResource MutedBrush}"
                                                       FontSize="10"
                                                       TextWrapping="Wrap"
                                                       Margin="0,0,0,8" />

                                            <!-- Ready Color row -->
                                            <Grid Margin="0,0,0,6">
                                                <Grid.ColumnDefinitions>
                                                    <ColumnDefinition Width="Auto" />
                                                    <ColumnDefinition Width="*" />
                                                    <ColumnDefinition Width="Auto" />
                                                    <ColumnDefinition Width="Auto" />
                                                </Grid.ColumnDefinitions>
                                                <Border Grid.Column="0"
                                                        Width="24" Height="24"
                                                        CornerRadius="4"
                                                        BorderBrush="{DynamicResource BorderBrush}"
                                                        BorderThickness="1"
                                                        Background="{Binding ManagedRewardReadyColorBrush}"
                                                        VerticalAlignment="Center"
                                                        Margin="0,0,8,0" />
                                                <TextBox Grid.Column="1"
                                                         Text="{Binding ManagedRewardReadyColor, UpdateSourceTrigger=PropertyChanged}"
                                                         VerticalContentAlignment="Center"
                                                         ToolTip="Hex color in #RRGGBB format" />
                                                <Button Grid.Column="2"
                                                        Content="Pick..."
                                                        Style="{StaticResource SecondaryButtonStyle}"
                                                        Margin="6,0,0,0"
                                                        Padding="8,4"
                                                        Click="OnPickWardrobeReadyColorClicked"
                                                        Tag="{Binding}" />
                                                <Button Grid.Column="3"
                                                        Content="Reset"
                                                        Style="{StaticResource SecondaryButtonStyle}"
                                                        Margin="4,0,0,0"
                                                        Padding="8,4"
                                                        Click="OnResetWardrobeReadyColorClicked"
                                                        Tag="{Binding}"
                                                        ToolTip="Reset to default ready color" />
                                            </Grid>

                                            <!-- Cooldown Color row -->
                                            <Grid Margin="0,0,0,4">
                                                <Grid.ColumnDefinitions>
                                                    <ColumnDefinition Width="Auto" />
                                                    <ColumnDefinition Width="*" />
                                                    <ColumnDefinition Width="Auto" />
                                                    <ColumnDefinition Width="Auto" />
                                                </Grid.ColumnDefinitions>
                                                <Border Grid.Column="0"
                                                        Width="24" Height="24"
                                                        CornerRadius="4"
                                                        BorderBrush="{DynamicResource BorderBrush}"
                                                        BorderThickness="1"
                                                        Background="{Binding ManagedRewardCooldownColorBrush}"
                                                        VerticalAlignment="Center"
                                                        Margin="0,0,8,0" />
                                                <TextBox Grid.Column="1"
                                                         Text="{Binding ManagedRewardCooldownColor, UpdateSourceTrigger=PropertyChanged}"
                                                         VerticalContentAlignment="Center"
                                                         ToolTip="Hex color in #RRGGBB format" />
                                                <Button Grid.Column="2"
                                                        Content="Pick..."
                                                        Style="{StaticResource SecondaryButtonStyle}"
                                                        Margin="6,0,0,0"
                                                        Padding="8,4"
                                                        Click="OnPickWardrobeCooldownColorClicked"
                                                        Tag="{Binding}" />
                                                <Button Grid.Column="3"
                                                        Content="Reset"
                                                        Style="{StaticResource SecondaryButtonStyle}"
                                                        Margin="4,0,0,0"
                                                        Padding="8,4"
                                                        Click="OnResetWardrobeCooldownColorClicked"
                                                        Tag="{Binding}"
                                                        ToolTip="Reset to default cooldown color" />
                                            </Grid>
                                        </StackPanel>

                                        <!-- Param sub-toolbar -->
                                        <TextBlock Text="Outfit Parameters"
                                                   Foreground="{DynamicResource TextBrush}"
                                                   FontWeight="SemiBold"
                                                   FontSize="11"
                                                   Margin="0,8,0,4" />
                                        <WrapPanel Margin="0,0,0,6">
                                            <Button Content="+ Add Param" Style="{StaticResource AccentButtonStyle}" Padding="6,3" FontSize="10" Margin="0,0,4,0"
                                                    Command="{Binding DataContext.AddWardrobeSnapshotParamCommand, RelativeSource={RelativeSource AncestorType=Window}}" />
                                            <Button Content="Remove" Style="{StaticResource SecondaryButtonStyle}" Padding="6,3" FontSize="10" Margin="0,0,4,0"
                                                    Command="{Binding DataContext.RemoveWardrobeSnapshotParamCommand, RelativeSource={RelativeSource AncestorType=Window}}" />
                                            <Button Content="Copy" Style="{StaticResource SecondaryButtonStyle}" Padding="6,3" FontSize="10" Margin="0,0,4,0"
                                                    Command="{Binding DataContext.CopyWardrobeSnapshotParamCommand, RelativeSource={RelativeSource AncestorType=Window}}" />
                                            <Button Content="Paste" Style="{StaticResource SecondaryButtonStyle}" Padding="6,3" FontSize="10" Margin="0,0,4,0"
                                                    Command="{Binding DataContext.PasteWardrobeSnapshotParamCommand, RelativeSource={RelativeSource AncestorType=Window}}" />
                                            <Button Content="Refresh" Style="{StaticResource SecondaryButtonStyle}" Padding="6,3" FontSize="10" Margin="0,0,4,0"
                                                    Command="{Binding DataContext.RefreshWardrobeParametersCommand, RelativeSource={RelativeSource AncestorType=Window}}" />
                                            <Button Content="Test Outfit" Style="{StaticResource SecondaryButtonStyle}" Padding="6,3" FontSize="10"
                                                    Command="{Binding DataContext.TestWardrobeOutfitCommand, RelativeSource={RelativeSource AncestorType=Window}}" />
                                        </WrapPanel>

                                        <!-- Param list -->
                                        <ListBox ItemsSource="{Binding SnapshotParams}"
                                                 SelectedItem="{Binding DataContext.SelectedWardrobeSnapshotParam, RelativeSource={RelativeSource AncestorType=Window}}"
                                                 Background="{DynamicResource PanelBrush}"
                                                 Foreground="{DynamicResource TextBrush}"
                                                 MaxHeight="160"
                                                 Margin="0,0,0,6"
                                                 BorderBrush="{DynamicResource BorderBrush}">
                                            <ListBox.ItemTemplate>
                                                <DataTemplate>
                                                    <TextBlock Text="{Binding DisplaySummary}" Foreground="{DynamicResource TextBrush}" FontSize="11" Padding="4,2" />
                                                </DataTemplate>
                                            </ListBox.ItemTemplate>
                                        </ListBox>

                                        <!-- Param editor (visible when a param is selected) -->
                                        <Border Visibility="{Binding DataContext.SelectedWardrobeSnapshotParam, RelativeSource={RelativeSource AncestorType=Window}, Converter={StaticResource NullToVisibilityConverter}}"
                                                Background="{DynamicResource PanelBrush}"
                                                BorderBrush="{DynamicResource BorderBrush}"
                                                BorderThickness="1"
                                                CornerRadius="8"
                                                Padding="10"
                                                Margin="0,4,0,0">
                                            <StackPanel DataContext="{Binding DataContext.SelectedWardrobeSnapshotParam, RelativeSource={RelativeSource AncestorType=Window}}">
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
                                                <UniformGrid Columns="2" Margin="0,0,0,4">
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
                                            </StackPanel>
                                        </Border>
                                    </StackPanel>
                                </Border>

                                <!-- Global wardrobe cooldown (per profile) -->
                                <Grid Margin="0,12,0,0">
                                    <StackPanel>
                                        <TextBlock Text="Global Wardrobe Cooldown (seconds)"
                                                   Foreground="{DynamicResource TextBrush}"
                                                   FontSize="11"
                                                   Margin="0,0,0,2" />
                                        <TextBlock Text="All outfits share this cooldown. 0 = no cooldown."
                                                   Foreground="{DynamicResource MutedBrush}"
                                                   FontSize="9"
                                                   TextWrapping="Wrap"
                                                   Margin="0,0,0,4" />
                                        <TextBox Text="{Binding WardrobeCooldownSeconds, UpdateSourceTrigger=PropertyChanged}" />
                                    </StackPanel>
                                </Grid>
                            </StackPanel>
```

- [ ] **Step 3: Build to verify**

```powershell
dotnet build "E:\!!!Program to work on\Proper Crystal Relay\VrcTwitchOscBridge\VrcTwitchOscBridge.csproj" --no-restore
```

Expected: Build succeeds. The new XAML is in place; bindings resolve. (Build-time data-binding errors only surface at runtime, so launch the app next.)

- [ ] **Step 4: Commit**

```bash
git add "VrcTwitchOscBridge/AvatarSetsManagerWindow.xaml"
git commit -m "feat(wardrobe): replace Step 4 placeholder with full editor

Adds: Sync Mode ComboBox, LinkExisting branch with AvailableTwitchRewards
ComboBox, CreateOrManage branch with Title/Cost/Description/ChatCommand
and the new Ready/Cooldown color pickers, full param sub-toolbar
(add/remove/copy/paste/refresh/test), type-aware value editor
(Bool ComboBox / Int TextBox / Float TextBox), and the per-profile
global wardrobe cooldown footer."
```

---

## Task 10: Add color picker handlers to `AvatarSetsManagerWindow.xaml.cs`

**Files:**
- Modify: `VrcTwitchOscBridge\AvatarSetsManagerWindow.xaml.cs` (add 4 new handlers; `PickColorAndApply` already exists)

- [ ] **Step 1: Add the 4 color picker handlers**

After the existing `OnResetCooldownColorClicked` method (around line 198), add:
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

- [ ] **Step 2: Build to verify**

```powershell
dotnet build "E:\!!!Program to work on\Proper Crystal Relay\VrcTwitchOscBridge\VrcTwitchOscBridge.csproj" --no-restore
```

Expected: Build succeeds. The 4 handlers are wired to the XAML `Click` events added in Task 9.

- [ ] **Step 3: Commit**

```bash
git add "VrcTwitchOscBridge/AvatarSetsManagerWindow.xaml.cs"
git commit -m "feat(wardrobe): add 4 color picker handlers for outfit ready/cooldown

OnPickWardrobeReadyColorClicked / OnResetWardrobeReadyColorClicked
OnPickWardrobeCooldownColorClicked / OnResetWardrobeCooldownColorClicked.
Reuses the existing PickColorAndApply helper."
```

---

## Task 11: Update CHANGELOG and release notes

**Files:**
- Modify: `E:\!!!Program to work on\Proper Crystal Relay\RELEASE-CHANGE-RECORD.txt` (append under "Changed")
- Modify: `E:\!!!Program to work on\Proper Crystal Relay\CHANGELOG.txt` (mirror under v3.1.9 beta 2)

- [ ] **Step 1: Find the current v3.1.9 beta 2 section in `CHANGELOG.txt`**

```powershell
git grep -n "v3.1.9 beta 2" "CHANGELOG.txt"
```

- [ ] **Step 2: Find the "Changed" section in `RELEASE-CHANGE-RECORD.txt`**

```powershell
git grep -n "Changed" "RELEASE-CHANGE-RECORD.txt"
```

- [ ] **Step 3: Add the entry to `RELEASE-CHANGE-RECORD.txt`**

Under the `Changed` heading, add:
```
- Moved Wardrobe editor from main window into the Avatar Sets manager
  (Step 4) with per-outfit managed reward ready/cooldown color pickers and
  a full Create vs Link existing reward branch.
```

- [ ] **Step 4: Mirror to `CHANGELOG.txt`**

Under the `v3.1.9 beta 2` section, add the same bullet in user-facing wording:
```
- Wardrobe editor moved into the Avatar Sets manager. Outfits now have
  per-reward ready/cooldown color pickers (green/red by default, like
  Avatar Set rules) and a full Create/Manage vs Link existing reward
  branch.
```

- [ ] **Step 5: Commit**

```bash
git add "RELEASE-CHANGE-RECORD.txt" "CHANGELOG.txt"
git commit -m "docs: note wardrobe editor migration in v3.1.9 beta 2

Internal scratchpad and public changelog both updated."
```

---

## Task 12: Final verification

**Files:** None modified. This is a runtime + grep audit.

- [ ] **Step 1: Final build**

```powershell
dotnet build "E:\!!!Program to work on\Proper Crystal Relay\VrcTwitchOscBridge\VrcTwitchOscBridge.csproj" --no-restore
```

Expected: Build succeeds with zero new warnings. The only warnings should be the ones already present before this work.

- [ ] **Step 2: Launch debug build**

```powershell
& "E:\!!!Program to work on\Proper Crystal Relay\Launch-Crystal-Relay-Debug.bat"
```

Expected: Window title shows `- DEBUG`. App starts.

- [ ] **Step 3: Migration smoke test (manual)**

1. With a save that has Wardrobe outfits from 3.1.9 beta 1, open the Avatar
   Sets manager.
2. Click an Avatar Set card with Wardrobe mode on.
3. Confirm the outfit list appears in Step 4.
4. Click an outfit → confirm Name / IsEnabled / ActiveTime / SyncMode
   populate.
5. Confirm the color swatches render green/red.
6. Add a new outfit → confirm it appears in the list and the
   managed-reward sync fires.
7. Toggle SyncMode to Link → confirm the AvailableTwitchRewards ComboBox
   appears and the Color pickers / Title / Cost / Description / Chat
   Command disappear.
8. Toggle back to Manage → confirm everything reappears.
9. Add a parameter → confirm the type-aware value editor appears.
10. Change the parameter type → confirm the value editor switches
    (Bool ComboBox / Int TextBox / Float TextBox).
11. Click "Test Outfit" → confirm the runtime executor runs (log message
    or `BridgeStatus` update).
12. Click "Pick..." next to Ready color → confirm the color picker opens,
    pick a non-default color, confirm the swatch and the hex TextBox
    update.
13. Click "Reset" → confirm it goes back to default green/red.

- [ ] **Step 4: Backwards-compat test (manual)**

1. Open a save from before this change (no color fields).
2. Confirm the colors default to green/red.
3. Confirm no `NullReferenceException` in the editor.

- [ ] **Step 5: Grep audit**

```powershell
git grep -nE "IsViewingWardrobe|ShowWardrobeCommand|ShowWardrobe\b" -- "VrcTwitchOscBridge/"
git grep -n "selectedWardrobeOutfit" -- "VrcTwitchOscBridge/ViewModels/MainWindowViewModel.cs"
git grep -n "AddWardrobeOutfitCommand|RemoveWardrobeOutfitCommand|AddWardrobeSnapshotParamCommand" -- "VrcTwitchOscBridge/ViewModels/MainWindowViewModel.cs"
```

Expected:
- First command: zero hits (all in MainWindow files).
- Second command: zero hits (moved to manager).
- Third command: zero hits (moved to manager).

- [ ] **Step 6: No-regressions check (manual)**

- Avatar Set mode (non-Wardrobe) cards still work.
- Master reward (in profile) still appears.
- The card-level Test button (line 628 of manager VM) still tests the
  first outfit of a Wardrobe-mode profile.
- Theme switching still applies.

- [ ] **Step 7: Final commit if any verification-only changes were made**

If anything was tweaked during verification, commit it:
```bash
git add -A
git commit -m "chore: verification-time adjustments to wardrobe migration"
```

Otherwise skip this step.

---

## Summary

12 tasks, each independently buildable. Each task ends with a commit. The runtime (WardrobeExecutorService, BridgeRuntimeConfiguration.TryToWardrobeSnapshot) is unchanged. The new editor lives in the manager and the old in-main editor is gone.

**Out of scope (per spec):** Wardrobe Master Reward editor, disable pairing, avatar-change blocker, new files, build/test package.
