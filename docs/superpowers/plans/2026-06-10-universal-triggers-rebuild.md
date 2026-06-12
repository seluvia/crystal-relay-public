# Universal Triggers Rebuild Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Strip the broken legacy Universal Triggers UI and the orphaned half-built new-UI files, then ship a fresh themed secondary window (`UniversalTriggersManagerWindow.xaml`) with empty-state landing + populated grid + slide-out editor. The runtime engine, models, persistence, Fooma importer, EventSub routing, and managed-reward sync stay completely intact.

**Architecture:** Demolition first (kill legacy XAML + orphaned files + obsolete VM members), then build the new window in five vertical layers (window shell → empty state → toolbar/filter strip → card grid → slide-out editor). The runtime engine in `BridgeCoordinator` is never touched. Persistence is `AppSettings.UniversalTriggers` and survives the rebuild.

**Tech Stack:** C# .NET 10 / `net10.0-windows`, WPF + XAML, `CommunityToolkit.Mvvm` for observable VMs, custom themed window chrome (`shell:WindowChrome` with `WindowStyle="None"`), `DynamicResource` brushes for theme palettes, `loc:Translate` markup extension for localization, `RenderOptions.BitmapScalingMode="NearestNeighbor"` for pixel-art assets. No new NuGet packages. No new external services.

**Reference spec:** `docs/superpowers/specs/2026-06-10-universal-triggers-rebuild-design.md`

**Already done before this plan starts:**
- Raw source backup: `Backups\v3.1.9\CrystalRelayTwitchOsc-v3.1.9-restore-20260610-180654.zip`

---

## File Map

### Created files
- `VrcTwitchOscBridge/Assets/fooma-icon.png` (binary asset)
- `VrcTwitchOscBridge/UniversalTriggersManagerWindow.xaml`
- `VrcTwitchOscBridge/UniversalTriggersManagerWindow.xaml.cs`
- `VrcTwitchOscBridge/ViewModels/UniversalTriggersManagerViewModel.cs`
- `VrcTwitchOscBridge/ViewModels/UniversalTriggerCardViewModel.cs` (NEW — different from the deleted orphan; lives next to the manager VM)

### Modified files
- `VrcTwitchOscBridge/VrcTwitchOscBridge.csproj` (remove orphan `<Page>` + `<Compile>`, add Asset + new XAML/CS entries)
- `VrcTwitchOscBridge/MainWindow.xaml` (rip ~760 lines of inline Universal Triggers UI; replace sidebar button command)
- `VrcTwitchOscBridge/MainWindow.xaml.cs` (remove `UniversalTriggerRule` cooldown color switch cases)
- `VrcTwitchOscBridge/ViewModels/MainWindowViewModel.cs` (remove `IsViewingUniversalTriggers`, `SelectedUniversalTrigger`, `SelectedUniversalTriggerAction`, `UniversalTriggerTypes`, `UniversalTriggerValueKinds`, `ShowUniversalTriggersCommand`, lazy `UniversalTriggersViewModel`; add `OpenUniversalTriggersManagerCommand`)
- `VrcTwitchOscBridge/Models/AppSettings.cs` (add 5 nullable `IsXxxSectionCollapsed` bool properties)
- `VrcTwitchOscBridge/Services/SettingsStore.cs` (persist new collapse flags)
- `VrcTwitchOscBridge/Resources/Localization/en-US.extra.json` (add ~80 new keys, remove orphan wizard/import preview keys)
- All other `*.extra.json` localization files (13 languages) (matching translations)
- `CHANGELOG.txt`, `RELEASE-CHANGE-RECORD.txt` (v3.1.9 entry)

### Deleted files
- `VrcTwitchOscBridge/UniversalTriggersView.xaml` and `.xaml.cs`
- `VrcTwitchOscBridge/UniversalTriggerCreateWizardWindow.xaml` and `.xaml.cs`
- `VrcTwitchOscBridge/UniversalTriggerImportPreviewWindow.xaml` and `.xaml.cs`
- `VrcTwitchOscBridge/ViewModels/UniversalTriggersViewModel.cs`
- `VrcTwitchOscBridge/ViewModels/UniversalTriggerCardViewModel.cs` (orphan version — replaced by a fresh one)
- `VrcTwitchOscBridge/ViewModels/UniversalTriggerCreateWizardViewModel.cs`
- `VrcTwitchOscBridge/ViewModels/UniversalTriggerImportPreviewViewModel.cs`

### Untouched (do NOT modify)
- `VrcTwitchOscBridge/Models/UniversalTriggerRule.cs`
- `VrcTwitchOscBridge/Models/UniversalTriggerAction.cs`
- `VrcTwitchOscBridge/Models/UniversalTriggerType.cs`
- `VrcTwitchOscBridge/Models/UniversalTriggerValueKind.cs`
- `VrcTwitchOscBridge/Services/FoomaInteractionConfigImporter.cs`
- `VrcTwitchOscBridge/Services/UniversalTriggerFusionService.cs`
- `VrcTwitchOscBridge/Services/BridgeCoordinator.cs` (Universal Trigger code is hot path)
- `VrcTwitchOscBridge/Services/BridgeRuntimeConfiguration.cs` (snapshot shape)
- `oscquery-lib/` (vendored library)
- All other Crystal Relay sections (Avatar Sets, Avatar Change, Cash Payments, Avatar Scaling, etc.)

---

## Verification Rules

- Crystal Relay has no automated UI test suite. The standard verification step is `dotnet build` + manual launch.
- Standard build: `dotnet build "E:\!!!Program to work on\Proper Crystal Relay\VrcTwitchOscBridge\VrcTwitchOscBridge.csproj" --no-restore --nologo`
- Standard manual launch: `E:\!!!Program to work on\Proper Crystal Relay\Launch-Crystal-Relay-Debug.bat`
- Localization audit: `dotnet run --project "E:\!!!Program to work on\Proper Crystal Relay\LocalizationAudit\LocalizationAudit.csproj" --no-restore`
- Do NOT run `git commit` unless the user explicitly requests a commit. Per AGENTS.md, all commits must be user-initiated.

---


## Task 1: Add Fooma pixel-art asset

**Files:**
- Create: `VrcTwitchOscBridge/Assets/fooma-icon.png` (copy from `C:\Users\screm\Downloads\pp_background_removed.png`)
- Modify: `VrcTwitchOscBridge/VrcTwitchOscBridge.csproj` (add `<Resource>` entry)

- [ ] **Step 1: Copy asset**

Run (PowerShell):
```powershell
Copy-Item -Path "C:\Users\screm\Downloads\pp_background_removed.png" -Destination "E:\!!!Program to work on\Proper Crystal Relay\VrcTwitchOscBridge\Assets\fooma-icon.png" -Force
```

Expected: file exists at `VrcTwitchOscBridge\Assets\fooma-icon.png`, ~5 KB, valid PNG with alpha background.

- [ ] **Step 2: Register asset in csproj**

Open `VrcTwitchOscBridge/VrcTwitchOscBridge.csproj`. Find the existing `<ItemGroup>` containing other `<Resource Include="Assets\...">` entries (search for `Assets\crystal-relay-icon.ico` to locate the group). Append:

```xml
<Resource Include="Assets\fooma-icon.png" />
```

- [ ] **Step 3: Build**

Run:
```
dotnet build "E:\!!!Program to work on\Proper Crystal Relay\VrcTwitchOscBridge\VrcTwitchOscBridge.csproj" --no-restore --nologo
```

Expected: `Build succeeded. 0 Warning(s). 0 Error(s).`

---

## Task 2: Delete orphaned new-UI files

**Files:**
- Delete: 4 XAML files + 4 view models (8 files total)

- [ ] **Step 1: Delete orphan files**

Run (PowerShell):
```powershell
$base = "E:\!!!Program to work on\Proper Crystal Relay\VrcTwitchOscBridge"
Remove-Item -LiteralPath (Join-Path $base "UniversalTriggersView.xaml") -Force
Remove-Item -LiteralPath (Join-Path $base "UniversalTriggersView.xaml.cs") -Force
Remove-Item -LiteralPath (Join-Path $base "UniversalTriggerCreateWizardWindow.xaml") -Force
Remove-Item -LiteralPath (Join-Path $base "UniversalTriggerCreateWizardWindow.xaml.cs") -Force
Remove-Item -LiteralPath (Join-Path $base "UniversalTriggerImportPreviewWindow.xaml") -Force
Remove-Item -LiteralPath (Join-Path $base "UniversalTriggerImportPreviewWindow.xaml.cs") -Force
Remove-Item -LiteralPath (Join-Path $base "ViewModels\UniversalTriggersViewModel.cs") -Force
Remove-Item -LiteralPath (Join-Path $base "ViewModels\UniversalTriggerCardViewModel.cs") -Force
Remove-Item -LiteralPath (Join-Path $base "ViewModels\UniversalTriggerCreateWizardViewModel.cs") -Force
Remove-Item -LiteralPath (Join-Path $base "ViewModels\UniversalTriggerImportPreviewViewModel.cs") -Force
```

Expected: 10 files removed (3 .xaml + 3 .xaml.cs + 4 .cs view models).

- [ ] **Step 2: Verify build fails with missing-file errors (sanity check)**

Run:
```
dotnet build "E:\!!!Program to work on\Proper Crystal Relay\VrcTwitchOscBridge\VrcTwitchOscBridge.csproj" --no-restore --nologo
```

Expected: build fails with errors about the missing files still listed in csproj. This confirms the next step is needed.

---

## Task 3: Remove orphan csproj entries

**Files:**
- Modify: `VrcTwitchOscBridge/VrcTwitchOscBridge.csproj`

- [ ] **Step 1: Remove `<Page>` entries for the deleted XAML**

Open the csproj. Delete the following lines (search for each):
```xml
<Page Include="UniversalTriggersView.xaml" />
<Page Include="UniversalTriggerCreateWizardWindow.xaml" />
<Page Include="UniversalTriggerImportPreviewWindow.xaml" />
```

- [ ] **Step 2: Remove `<Compile>` entries for the deleted .cs**

Delete:
```xml
<Compile Include="UniversalTriggersView.xaml.cs">
  <DependentUpon>UniversalTriggersView.xaml</DependentUpon>
</Compile>
<Compile Include="UniversalTriggerCreateWizardWindow.xaml.cs">
  <DependentUpon>UniversalTriggerCreateWizardWindow.xaml</DependentUpon>
</Compile>
<Compile Include="UniversalTriggerImportPreviewWindow.xaml.cs">
  <DependentUpon>UniversalTriggerImportPreviewWindow.xaml</DependentUpon>
</Compile>
<Compile Include="ViewModels\UniversalTriggersViewModel.cs" />
<Compile Include="ViewModels\UniversalTriggerCardViewModel.cs" />
<Compile Include="ViewModels\UniversalTriggerCreateWizardViewModel.cs" />
<Compile Include="ViewModels\UniversalTriggerImportPreviewViewModel.cs" />
```

The exact form may vary (single-line vs multi-line). Use Grep on the csproj to find each occurrence and delete the full element.

- [ ] **Step 3: Build expects fail in `MainWindowViewModel` (next task fixes it)**

Run the standard build. Expected: failures in `MainWindowViewModel.cs` due to the missing `UniversalTriggersViewModel` reference at line ~2527. Errors specifically about Universal Trigger orphan types being missing are expected; errors about other things are not. Proceed if and only if the only errors mention `UniversalTriggersViewModel` / `UniversalTriggerCardViewModel` types.


---

## Task 4: Strip orphan VM references from MainWindowViewModel

**Files:**
- Modify: `VrcTwitchOscBridge/ViewModels/MainWindowViewModel.cs`

- [ ] **Step 1: Find and delete the orphan `UniversalTriggersViewModel` property**

Grep for the lazy property (search pattern `UniversalTriggersViewModel`). Around line 2527 there is a block that lazy-constructs `new UniversalTriggersViewModel(...)`. Delete the property accessor and its backing field. This is the only consumer of the deleted orphan VM.

- [ ] **Step 2: Find and delete the orphan `UniversalTriggerCardViewModel` references**

Grep for `UniversalTriggerCardViewModel`. Delete any property, field, or method that references it.

- [ ] **Step 3: Find and delete the orphan wizard / import preview VM references**

Grep for `UniversalTriggerCreateWizardViewModel` and `UniversalTriggerImportPreviewViewModel`. Delete any references found.

- [ ] **Step 4: Build**

Run the standard build. Expected: no more errors about the deleted orphan types. The legacy XAML in `MainWindow.xaml` still references commands that the next tasks will remove, so build should be green at this point because those commands still exist on the VM — we have not deleted them yet.

---

## Task 5: Remove legacy DataTemplates from MainWindow.xaml

**Files:**
- Modify: `VrcTwitchOscBridge/MainWindow.xaml`

- [ ] **Step 1: Locate and remove `UniversalTriggerRule` DataTemplate**

Grep `MainWindow.xaml` for:
```
DataTemplate DataType="{x:Type models:UniversalTriggerRule}"
```

Two matches expected (~line 1584 in the top-level resources, ~line 8753 inside the editor). Delete the FIRST occurrence (the top-level one in `<Window.Resources>`). Leave the one at ~line 8753 because it lives inside the inline editor block that will be deleted whole in Task 9.

- [ ] **Step 2: Locate and remove `UniversalTriggerAction` DataTemplate**

Grep `MainWindow.xaml` for:
```
DataTemplate DataType="{x:Type models:UniversalTriggerAction}"
```

Two matches expected (~line 1615 in resources, ~line 9170 inside the editor). Delete the FIRST occurrence (top-level). Leave the inner one for Task 9.

- [ ] **Step 3: Build**

Standard build. Expected: green. Removing top-level DataTemplates does not break the inline editor because the inline templates at 8753/9170 are local matches inside the editor scope.

---

## Task 6: Replace sidebar "Universal Triggers" button

**Files:**
- Modify: `VrcTwitchOscBridge/MainWindow.xaml` (~line 3770)
- Modify: `VrcTwitchOscBridge/ViewModels/MainWindowViewModel.cs`

- [ ] **Step 1: Add stub `OpenUniversalTriggersManagerCommand` to MainWindowViewModel**

Find an existing block of `RelayCommand` declarations in `MainWindowViewModel.cs`. Add:

```csharp
[RelayCommand]
private void OpenUniversalTriggersManager()
{
    // Stub. Full implementation lands in Task 18.
}
```

(If the codebase uses manual `ICommand` properties rather than `[RelayCommand]` attribute, follow the established pattern instead.)

- [ ] **Step 2: Update sidebar button binding in MainWindow.xaml**

Find the existing sidebar `<Button>` at ~line 3770:
```xml
<Button Content="{loc:Translate 'Universal Triggers'}"
        Command="{Binding ShowUniversalTriggersCommand}">
```

Change `Command="{Binding ShowUniversalTriggersCommand}"` to `Command="{Binding OpenUniversalTriggersManagerCommand}"`.

- [ ] **Step 3: Remove the `IsViewingUniversalTriggers` DataTrigger inside the button style**

Inside this same button's `<Style>` block (just below the Command change), there is a `<DataTrigger Binding="{Binding IsViewingUniversalTriggers}" Value="True">` that highlights the button when the workspace is on Universal Triggers. Delete the whole `<DataTrigger>` element (it will not be needed once the inline workspace is gone).

- [ ] **Step 4: Build**

Standard build. Expected: green (we are still keeping the old `IsViewingUniversalTriggers` property and `ShowUniversalTriggersCommand` on the VM for now to keep other XAML happy).


---

## Task 7: Remove all `IsViewingUniversalTriggers` DataTrigger blocks in MainWindow.xaml

**Files:**
- Modify: `VrcTwitchOscBridge/MainWindow.xaml`

These DataTriggers scoped main-workspace UI (headers, lists, editor visibility, empty states) to the Universal Triggers tab. With the tab gone, none of them apply.

- [ ] **Step 1: List all sites**

Run (PowerShell):
```powershell
Select-String -Path "E:\!!!Program to work on\Proper Crystal Relay\VrcTwitchOscBridge\MainWindow.xaml" -Pattern "IsViewingUniversalTriggers" | Select-Object LineNumber, Line
```

Expected sites (line numbers approximate, may shift after previous edits): 3776, 3910, 4311, 4495, 4675, 5533, 5711, 8477, 9241, 10776, 10804.

- [ ] **Step 2: Delete each `<DataTrigger Binding="{Binding IsViewingUniversalTriggers}" ...>` element**

For each site, delete the entire `<DataTrigger>` element including its `<Setter>` children and closing tag. Do NOT delete the surrounding `<Style.Triggers>` block or any sibling `<DataTrigger>` blocks - only the Universal Triggers-specific one.

For the two `<MultiDataTrigger>` blocks at ~lines 10804-10805 (`SelectedUniversalTrigger == null` empty-state pair), delete the entire `<MultiDataTrigger>` element.

- [ ] **Step 3: Re-grep to confirm zero remaining sites**

Re-run the grep from Step 1. Expected: zero matches.

- [ ] **Step 4: Build**

Standard build. Expected: green.

---

## Task 8: Remove legacy Universal Triggers header/filter strip

**Files:**
- Modify: `VrcTwitchOscBridge/MainWindow.xaml`

The header bar at ~lines 4336-4495 has Add / Enable All / Disable All / Delete All buttons + a "Delete Universal Triggers" header text. All of these scoped to the legacy workspace and are now redundant with the new manager window.

- [ ] **Step 1: Locate the header block**

Grep `MainWindow.xaml` for `'Delete Universal Triggers'` (the localized header text). The match should be at approximately line 4355.

Find the enclosing `<StackPanel>` or `<Grid>` that contains:
- The header text (line ~4355)
- The "Add Universal Trigger" `<Button Command="{Binding AddUniversalTriggerCommand}" />` (~line 4336)
- The "Enable All Universal Triggers" `<Button Command="{Binding EnableAllUniversalTriggersCommand}" />` (~line 4340)
- The "Disable All Universal Triggers" `<Button Command="{Binding DisableAllUniversalTriggersCommand}" />` (~line 4347)
- The "Delete All Universal Triggers" `<Button Command="{Binding DeleteAllUniversalTriggersCommand}" />` (~line 4365)

- [ ] **Step 2: Delete the whole block**

Delete the entire container element. If the container had other purposes (e.g., it was a shared header bar used by multiple sections), only delete the Universal Triggers-specific buttons + header text and leave the container shell. Verify by checking which other bindings the container has.

- [ ] **Step 3: Build**

Standard build. Expected: green.

---

## Task 9: Remove legacy ListBox + inline editor

**Files:**
- Modify: `VrcTwitchOscBridge/MainWindow.xaml`

- [ ] **Step 1: Delete the `Settings.UniversalTriggers` `ListBox`**

Grep for `ItemsSource="{Binding Settings.UniversalTriggers}"`. Expected: one match at ~line 4666. Delete the entire `<ListBox>` element including its `SelectedItem` binding and `<ListBox.ItemTemplate>` (if any inline).

If the empty-state placeholder uses a `<MultiDataTrigger>` checking `Settings.UniversalTriggers.Count == 0` at ~lines 4811-4812, delete that `<MultiDataTrigger>` too.

- [ ] **Step 2: Delete the inline editor block**

Find the inline editor block. Grep for the comment header or scope marker. The block runs from ~line 8477 to ~line 9241 and contains:
- A `<DataTrigger Binding="{Binding IsViewingUniversalTriggers}" Value="True">` (already removed in Task 7)
- The "Universal Trigger Editor" header (`{loc:Translate 'Universal Trigger Editor'}`)
- Multiple `<ComboBox>` and `<TextBox>` controls bound to `SelectedUniversalTrigger.*`
- Inner `<DataTemplate DataType="{x:Type models:UniversalTriggerRule}">` at ~line 8753
- Inner `<DataTemplate DataType="{x:Type models:UniversalTriggerAction}">` at ~line 9170
- Test / Delete / Save buttons

Delete the entire block. Trace the enclosing element (likely a `<Border>` or `<Grid>` with a `Style` referencing one of the deleted DataTriggers) and remove from outer open tag to outer close tag.

- [ ] **Step 3: Re-grep for any remaining Universal Trigger XAML bindings**

Run:
```powershell
Select-String -Path "E:\!!!Program to work on\Proper Crystal Relay\VrcTwitchOscBridge\MainWindow.xaml" -Pattern "Universal" | Select-Object LineNumber, Line
```

Expected: only the sidebar button (Task 6) should remain. If there are stragglers (e.g. mention of `UniversalTriggerTypes`, `SelectedUniversalTriggerAction`), find and delete those elements too.

- [ ] **Step 4: Build**

Standard build. Expected: green.

- [ ] **Step 5: Manual smoke test 1 — main window opens**

Run `E:\!!!Program to work on\Proper Crystal Relay\Launch-Crystal-Relay-Debug.bat`.

Expected:
- Main window opens.
- Sidebar still shows the "Universal Triggers" button.
- Clicking it does nothing yet (stub command).
- Other Crystal Relay sections (Avatar Sets, Channel Point Rules, Avatar Scaling, Cash Payments, Twitch Chatbox, About page) still open and look unchanged.
- No XAML binding error popups.

Close the app cleanly.


---

## Task 10: Remove legacy VM members from MainWindowViewModel

**Files:**
- Modify: `VrcTwitchOscBridge/ViewModels/MainWindowViewModel.cs`

Now that no XAML references the legacy members, delete them all.

- [ ] **Step 1: Delete `IsViewingUniversalTriggers` property + setter**

Grep `MainWindowViewModel.cs` for `IsViewingUniversalTriggers`. Delete:
- The property declaration (auto-property with `RaisePropertyChanged`)
- Any backing field
- Any `RaisePropertyChanged(nameof(IsViewingUniversalTriggers))` calls in OTHER methods (search and delete each line)

- [ ] **Step 2: Delete `SelectedUniversalTrigger` and `SelectedUniversalTriggerAction`**

Grep for each. Delete property + backing fields + `RaisePropertyChanged` callers.

- [ ] **Step 3: Delete `UniversalTriggerTypes` and `UniversalTriggerValueKinds` collection properties**

These get re-created in the new manager VM, not here. Delete the property declarations and any initialization in the constructor.

- [ ] **Step 4: Delete `ShowUniversalTriggersCommand` + handler**

Grep `ShowUniversalTriggersCommand`. Delete the `[RelayCommand]` attribute + method, or the manual `ICommand` property + handler — whichever pattern is used.

- [ ] **Step 5: Audit pass-through commands the legacy XAML referenced**

These commands were referenced by the old inline UI. If they still exist on the VM and are NOT used by anything else, delete them. Grep each name in the whole project to confirm zero external consumers before deleting:
- `AddUniversalTriggerCommand`
- `EnableAllUniversalTriggersCommand`
- `DisableAllUniversalTriggersCommand`
- `DeleteAllUniversalTriggersCommand`
- `RemoveSelectedUniversalTriggerCommand`
- `TestSelectedUniversalTriggerCommand`
- `AddUniversalTriggerActionCommand`
- `RemoveSelectedUniversalTriggerActionCommand`

For each, run:
```powershell
Select-String -Path "E:\!!!Program to work on\Proper Crystal Relay\VrcTwitchOscBridge\*" -Recurse -Pattern "<command-name>"
```

If the only match is the VM definition itself, delete the command. If anything else references it (e.g., a context menu, keyboard shortcut), preserve the VM member and add a note in the task plan.

- [ ] **Step 6: Preserve `ImportFoomaInteractionConfigAsync` for now**

Per the spec, this method may either stay on `MainWindowViewModel` or migrate to the new manager VM. Decision deferred to Task 18 (where it's wired into the new window). For now, leave it on `MainWindowViewModel` even if there are no XAML callers — the new manager VM will reach into it via constructor injection.

- [ ] **Step 7: Build**

Standard build. Expected: green.

- [ ] **Step 8: Manual smoke test 2 — verify nothing broke**

Run `Launch-Crystal-Relay-Debug.bat`. Click through:
- Avatar Sets — open, list visible, can select an avatar, can pick an avatar and edit
- Avatar Change — open, can configure
- Avatar Roulette — open
- Movement Redeems — open, can add a movement rule
- Avatar Scaling — open, can edit a scale rule, can add a supporter growth bits rule
- Bits + Subs Overrides — open, can configure
- Cash Payments (StreamElements / Streamlabs / Ko-fi) — open each section
- Reward Fire Sale — open
- Twitch Chatbox — open the window
- About page — opens and shows live status

Expected: every section works. No XAML errors. No null-reference exceptions in the debug log when navigating.

Close the app cleanly.

- [ ] **Step 9: Coupling smoke — Power-up + Cash Payment scale paths**

If you have test data set up:
- Use the Power-up simulator (Test Mode → Simulate Power-up Bits) to fire a Power-up event. Verify an Avatar Scaling rule responds (height change visible in OSC log).
- Use a Cash Payment test webhook (Ko-fi test or simulated StreamElements tip) to verify the cash payment scale path still fires.

If you do not have test data, skip and add a note for Step 8 of the end-to-end test (Task 33).

---

## Task 11: Create the new manager window shell (XAML)

**Files:**
- Create: `VrcTwitchOscBridge/UniversalTriggersManagerWindow.xaml`

- [ ] **Step 1: Create the empty XAML file**

Write the following to `VrcTwitchOscBridge/UniversalTriggersManagerWindow.xaml`:

```xml
<Window x:Class="VrcTwitchOscBridge.UniversalTriggersManagerWindow"
        xmlns="http://schemas.microsoft.com/winfx/2006/xaml/presentation"
        xmlns:x="http://schemas.microsoft.com/winfx/2006/xaml"
        xmlns:loc="clr-namespace:VrcTwitchOscBridge.Services"
        xmlns:vm="clr-namespace:VrcTwitchOscBridge.ViewModels"
        xmlns:shell="clr-namespace:System.Windows.Shell;assembly=PresentationFramework"
        xmlns:models="clr-namespace:VrcTwitchOscBridge.Models"
        xmlns:d="http://schemas.microsoft.com/expression/blend/2008"
        xmlns:mc="http://schemas.openxmlformats.org/markup-compatibility/2006"
        mc:Ignorable="d"
        d:DataContext="{d:DesignInstance Type=vm:UniversalTriggersManagerViewModel}"
        Title="{loc:Translate 'Universal Triggers'}"
        Icon="Assets/crystal-relay-icon.ico"
        Width="980"
        Height="640"
        MinWidth="720"
        MinHeight="480"
        WindowStyle="None"
        WindowStartupLocation="CenterOwner"
        FontFamily="{DynamicResource BodyFontFamily}"
        UseLayoutRounding="True"
        SnapsToDevicePixels="True"
        Background="{DynamicResource WindowBackgroundBrush}">
    <shell:WindowChrome.WindowChrome>
        <shell:WindowChrome CaptionHeight="0" CornerRadius="0" GlassFrameThickness="0" ResizeBorderThickness="6" UseAeroCaptionButtons="False" />
    </shell:WindowChrome.WindowChrome>
    <Window.Resources>
        <BooleanToVisibilityConverter x:Key="BoolToVisibilityConverter" />
    </Window.Resources>
    <Grid>
        <!-- Placeholder while we wire content in later tasks -->
        <TextBlock Text="Universal Triggers Manager (under construction)"
                   Foreground="{DynamicResource TextBrush}"
                   HorizontalAlignment="Center"
                   VerticalAlignment="Center" />
    </Grid>
</Window>
```

- [ ] **Step 2: Register in csproj**

Add to `VrcTwitchOscBridge/VrcTwitchOscBridge.csproj` in the `<Page>` group:

```xml
<Page Include="UniversalTriggersManagerWindow.xaml" />
```

And in the `<Compile>` group (will be added properly in Task 12):

```xml
<Compile Include="UniversalTriggersManagerWindow.xaml.cs">
  <DependentUpon>UniversalTriggersManagerWindow.xaml</DependentUpon>
</Compile>
```

- [ ] **Step 3: Build expects fail (missing .xaml.cs file)**

Standard build. Expected: fails because `UniversalTriggersManagerWindow.xaml.cs` does not exist yet. The next task creates it.


---

## Task 12: Create the manager window code-behind

**Files:**
- Create: `VrcTwitchOscBridge/UniversalTriggersManagerWindow.xaml.cs`

- [ ] **Step 1: Write minimal code-behind**

Write:

```csharp
using System;
using System.Windows;
using System.Windows.Input;
using VrcTwitchOscBridge.ViewModels;

namespace VrcTwitchOscBridge;

public partial class UniversalTriggersManagerWindow : Window
{
    public UniversalTriggersManagerWindow(UniversalTriggersManagerViewModel viewModel)
    {
        DataContext = viewModel ?? throw new ArgumentNullException(nameof(viewModel));
        InitializeComponent();
    }

    private void OnTitleBarMouseDown(object sender, MouseButtonEventArgs e)
    {
        if (e.ChangedButton == MouseButton.Left && e.ButtonState == MouseButtonState.Pressed)
        {
            DragMove();
        }
    }
}
```

- [ ] **Step 2: Build expects fail (missing VM type)**

Standard build. Expected: fails because `UniversalTriggersManagerViewModel` does not exist yet.

---

## Task 13: Create the manager view model skeleton

**Files:**
- Create: `VrcTwitchOscBridge/ViewModels/UniversalTriggersManagerViewModel.cs`
- Modify: `VrcTwitchOscBridge/ViewModels/MainWindowViewModel.cs` (add `SaveSettingsAsync` wrapper)

The plan references `_mainWindowViewModel.SaveSettingsAsync()` from the manager VM in multiple later tasks. The codebase today does not have that wrapper method on `MainWindowViewModel`; it has a private `settingsStore` field and direct `settingsStore.SaveAsync(Settings, CancellationToken.None)` calls. Add a small public wrapper here so the manager VM can call it cleanly.

- [ ] **Step 1: Write the skeleton view model**

Write:

```csharp
using System;
using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using VrcTwitchOscBridge.Models;
using VrcTwitchOscBridge.Services;

namespace VrcTwitchOscBridge.ViewModels;

public partial class UniversalTriggersManagerViewModel : ObservableObject
{
    private readonly AppSettings _settings;
    private readonly MainWindowViewModel _mainWindowViewModel;

    public UniversalTriggersManagerViewModel(AppSettings settings, MainWindowViewModel mainWindowViewModel)
    {
        _settings = settings ?? throw new ArgumentNullException(nameof(settings));
        _mainWindowViewModel = mainWindowViewModel ?? throw new ArgumentNullException(nameof(mainWindowViewModel));
    }

    public ObservableCollection<UniversalTriggerRule> Triggers => _settings.UniversalTriggers;

    public bool IsEmpty => Triggers.Count == 0;
}
```

- [ ] **Step 2: Register in csproj**

Add to the `<Compile>` group:

```xml
<Compile Include="ViewModels\UniversalTriggersManagerViewModel.cs" />
```

- [ ] **Step 3: Add `SaveSettingsAsync` wrapper on `MainWindowViewModel`**

Open `VrcTwitchOscBridge/ViewModels/MainWindowViewModel.cs`. Find any existing `settingsStore.SaveAsync(Settings, ...)` call (grep `settingsStore.SaveAsync`) to confirm the field name (`settingsStore` or `_settingsStore`). Add this public method near other public methods (e.g., near `AppendLog`):

```csharp
public Task SaveSettingsAsync(CancellationToken cancellationToken = default)
    => settingsStore.SaveAsync(Settings, cancellationToken);
```

If the field is named differently (e.g., `_settingsStore`), use the actual name. Add `using System.Threading; using System.Threading.Tasks;` at the top if missing.

- [ ] **Step 4: Expose the `BridgeCoordinator` and Universal Trigger helpers used by the manager VM**

The plan calls `_mainWindowViewModel.Coordinator.SendTestUniversalTriggerAsync(...)`, `_mainWindowViewModel.HasUniversalTriggerAvatarParameterGate(...)`, `_mainWindowViewModel.IsUniversalTriggerReadyForCurrentAvatarJson(...)`, `_mainWindowViewModel.SynchronizeManagedChannelPointRewardsAsync()`, and `_mainWindowViewModel.CurrentVrChatAvatarId` from later tasks. These are currently private. Promote each to `public` (preferred) or `internal` (acceptable since both classes are in the same assembly) without changing the implementation:

```csharp
// Was: private readonly BridgeCoordinator bridgeCoordinator;
// Add:
public BridgeCoordinator Coordinator => bridgeCoordinator;

// Promote these existing helpers (don't change their bodies — just change the accessor):
public bool HasUniversalTriggerAvatarParameterGate(UniversalTriggerRule rule) => ...; // existing body
public bool IsUniversalTriggerReadyForCurrentAvatarJson(UniversalTriggerRule rule, string currentAvatarId) => ...; // existing body
public Task SynchronizeManagedChannelPointRewardsAsync(...) => ...; // existing body
public string CurrentVrChatAvatarId => GetResolvedCurrentVrChatAvatarId();
```

For each helper: find the existing private/internal definition (grep its name), change the accessor to `public`, keep the body intact, do NOT touch any callers.

If any helper has a different parameter list (for example `CreateManagedRewardTargetForUniversalTrigger` takes more than one argument), find the existing call sites in `MainWindowViewModel` to learn the exact signature and match it in the manager VM's calls. The manager VM should call these helpers with the same arguments those existing callers use.

- [ ] **Step 5: Build**

Standard build. Expected: green. The shell window now compiles cleanly and the manager VM can reach every dependency on `MainWindowViewModel` through public methods/properties.

---

## Task 14: Wire `OpenUniversalTriggersManagerCommand` to construct + show the window

**Files:**
- Modify: `VrcTwitchOscBridge/ViewModels/MainWindowViewModel.cs`

- [ ] **Step 1: Add a single-instance reference field**

In `MainWindowViewModel.cs`, near other window-reference fields, add:

```csharp
private UniversalTriggersManagerWindow? _universalTriggersManagerWindow;
```

(Add `using VrcTwitchOscBridge;` at the top if not already present.)

- [ ] **Step 2: Implement `OpenUniversalTriggersManager` method**

Replace the stub from Task 6 with:

```csharp
[RelayCommand]
private void OpenUniversalTriggersManager()
{
    if (_universalTriggersManagerWindow is { IsVisible: true })
    {
        _universalTriggersManagerWindow.Activate();
        return;
    }

    var vm = new UniversalTriggersManagerViewModel(Settings, this);
    _universalTriggersManagerWindow = new UniversalTriggersManagerWindow(vm)
    {
        Owner = System.Windows.Application.Current.MainWindow,
    };
    _universalTriggersManagerWindow.Closed += (_, _) => _universalTriggersManagerWindow = null;
    _universalTriggersManagerWindow.Show();
}
```

(Adjust `Settings` reference if the property is named differently in this codebase — grep for `public AppSettings ` in `MainWindowViewModel.cs`.)

- [ ] **Step 3: Build**

Standard build. Expected: green.

- [ ] **Step 4: Manual smoke test 3 — window opens**

Run `Launch-Crystal-Relay-Debug.bat`. Click the "Universal Triggers" sidebar button.

Expected: a custom-chromeless 980×640 window opens centered on the main window with the placeholder text. Click the sidebar button again — the existing window activates (no duplicate). Close the window — sidebar button reopens it.

Close the app.


---

## Task 15: Add custom themed title bar with drag and close

**Files:**
- Modify: `VrcTwitchOscBridge/UniversalTriggersManagerWindow.xaml`

- [ ] **Step 1: Replace the placeholder Grid with a 3-row layout**

Replace the `<Grid>` placeholder with:

```xml
<Grid>
    <Grid.RowDefinitions>
        <RowDefinition Height="Auto" />
        <RowDefinition Height="*" />
    </Grid.RowDefinitions>

    <!-- Custom title bar (draggable) -->
    <Border Grid.Row="0"
            Background="{DynamicResource TitleBarBrush}"
            BorderBrush="{DynamicResource BorderBrush}"
            BorderThickness="0,0,0,1"
            Padding="14,10"
            MouseLeftButtonDown="OnTitleBarMouseDown">
        <DockPanel LastChildFill="True">
            <Button DockPanel.Dock="Right"
                    Content="✕"
                    Background="Transparent"
                    Foreground="{DynamicResource TitleBarTextBrush}"
                    BorderBrush="{DynamicResource BorderBrush}"
                    Padding="8,4"
                    Margin="6,0,0,0"
                    Click="OnCloseClicked"
                    shell:WindowChrome.IsHitTestVisibleInChrome="True" />
            <StackPanel Orientation="Vertical">
                <TextBlock Text="✨ Universal Triggers"
                           Foreground="{DynamicResource TitleBarTextBrush}"
                           FontWeight="Bold"
                           FontSize="14" />
                <TextBlock Text="{Binding SubtitleSummary}"
                           Foreground="{DynamicResource TitleBarSubTextBrush}"
                           FontSize="11"
                           Margin="0,2,0,0" />
            </StackPanel>
        </DockPanel>
    </Border>

    <!-- Body (next tasks populate this) -->
    <Grid Grid.Row="1" />
</Grid>
```

- [ ] **Step 2: Add `OnCloseClicked` handler to code-behind**

In `UniversalTriggersManagerWindow.xaml.cs`, add:

```csharp
private void OnCloseClicked(object sender, RoutedEventArgs e) => Close();
```

Add `using System.Windows;` if not already imported.

- [ ] **Step 3: Add `SubtitleSummary` to the manager VM**

In `UniversalTriggersManagerViewModel.cs`, add:

```csharp
public string SubtitleSummary
{
    get
    {
        var total = Triggers.Count;
        if (total == 0) return string.Empty;
        var active = 0; var needsFix = 0;
        foreach (var t in Triggers)
        {
            if (!t.IsEnabled) continue;
            active++;
            if (HasWarning(t)) needsFix++;
        }
        return Services.LocalizationManager.Translate("Universal Triggers Subtitle Summary", total, active, needsFix);
    }
}

private bool HasWarning(UniversalTriggerRule rule) =>
    _mainWindowViewModel.HasUniversalTriggerAvatarParameterGate(rule)
    && !_mainWindowViewModel.IsUniversalTriggerReadyForCurrentAvatarJson(rule, _mainWindowViewModel.CurrentVrChatAvatarId);
```

(The `LocalizationManager.Translate` static signature may differ — match the existing convention used elsewhere in the codebase, e.g., `LocalizationManager.Instance.Translate(key, args)`.)

If `MainWindowViewModel.HasUniversalTriggerAvatarParameterGate` / `IsUniversalTriggerReadyForCurrentAvatarJson` are private, expose them as `internal` so the manager VM can call them, or duplicate the call paths through a small public helper. Audit before changing access modifiers.

- [ ] **Step 4: Build**

Standard build. Expected: green.

- [ ] **Step 5: Manual smoke test 4 — title bar works**

Run the app, open the window. Expected:
- Title bar shows "✨ Universal Triggers" + subtitle (empty if no triggers saved).
- Drag the title bar — window moves.
- Click ✕ — window closes.

---

## Task 16: Build the empty-state landing

**Files:**
- Modify: `VrcTwitchOscBridge/UniversalTriggersManagerWindow.xaml`

- [ ] **Step 1: Replace the body Grid with the empty-state layout**

Replace the empty `<Grid Grid.Row="1" />` from Task 15 with:

```xml
<Grid Grid.Row="1" Visibility="{Binding IsEmpty, Converter={StaticResource BoolToVisibilityConverter}}">
    <StackPanel HorizontalAlignment="Center" VerticalAlignment="Center" MaxWidth="540">
        <TextBlock Text="✨" FontSize="32" HorizontalAlignment="Center" />
        <TextBlock Text="{loc:Translate 'Universal Triggers Welcome Title'}"
                   Foreground="{DynamicResource TextBrush}"
                   FontSize="18" FontWeight="Bold"
                   HorizontalAlignment="Center" Margin="0,8,0,0" />
        <TextBlock Text="{loc:Translate 'Universal Triggers Welcome Body'}"
                   Foreground="{DynamicResource MutedBrush}"
                   FontSize="11"
                   TextWrapping="Wrap"
                   TextAlignment="Center"
                   Margin="0,6,0,0" />
        <StackPanel Orientation="Horizontal" HorizontalAlignment="Center" Margin="0,22,0,0">

            <!-- Import Fooma card -->
            <Border Width="240" Padding="18" CornerRadius="14"
                    Background="{DynamicResource AccentDimBrush}"
                    BorderBrush="{DynamicResource AccentBrush}"
                    BorderThickness="1"
                    Margin="0,0,14,0">
                <StackPanel>
                    <Image Source="pack://application:,,,/Assets/fooma-icon.png"
                           Width="80" Height="80"
                           HorizontalAlignment="Center"
                           RenderOptions.BitmapScalingMode="NearestNeighbor" />
                    <TextBlock Text="{loc:Translate 'Universal Triggers Welcome Import Title'}"
                               Foreground="{DynamicResource TextBrush}"
                               FontWeight="Bold"
                               HorizontalAlignment="Center"
                               Margin="0,10,0,0" />
                    <TextBlock Text="{loc:Translate 'Universal Triggers Welcome Import Body'}"
                               Foreground="{DynamicResource MutedBrush}"
                               FontSize="11"
                               TextWrapping="Wrap"
                               TextAlignment="Center"
                               Margin="0,6,0,0" />
                    <Button Content="{loc:Translate 'Universal Triggers Welcome Import Action'}"
                            Command="{Binding ImportFoomaCommand}"
                            Background="{DynamicResource AccentBrush}"
                            Foreground="{DynamicResource ComboTextBrush}"
                            FontWeight="Bold"
                            Padding="10,6"
                            Margin="0,12,0,0"
                            HorizontalAlignment="Center" />
                </StackPanel>
            </Border>

            <!-- Create New card -->
            <Border Width="240" Padding="18" CornerRadius="14"
                    Background="{DynamicResource PanelBrush}"
                    BorderBrush="{DynamicResource BorderBrush}"
                    BorderThickness="1">
                <StackPanel>
                    <TextBlock Text="🛠️" FontSize="48" Height="80"
                               HorizontalAlignment="Center" VerticalAlignment="Center" />
                    <TextBlock Text="{loc:Translate 'Universal Triggers Welcome Create Title'}"
                               Foreground="{DynamicResource TextBrush}"
                               FontWeight="Bold"
                               HorizontalAlignment="Center"
                               Margin="0,10,0,0" />
                    <TextBlock Text="{loc:Translate 'Universal Triggers Welcome Create Body'}"
                               Foreground="{DynamicResource MutedBrush}"
                               FontSize="11"
                               TextWrapping="Wrap"
                               TextAlignment="Center"
                               Margin="0,6,0,0" />
                    <Button Content="{loc:Translate 'Universal Triggers Welcome Create Action'}"
                            Command="{Binding AddNewTriggerCommand}"
                            Background="Transparent"
                            Foreground="{DynamicResource TextBrush}"
                            BorderBrush="{DynamicResource AccentBrush}"
                            BorderThickness="1"
                            Padding="10,6"
                            Margin="0,12,0,0"
                            HorizontalAlignment="Center" />
                </StackPanel>
            </Border>

        </StackPanel>
    </StackPanel>
</Grid>
```

- [ ] **Step 2: Add stubs for `ImportFoomaCommand` and `AddNewTriggerCommand` to the VM**

In `UniversalTriggersManagerViewModel.cs`, add:

```csharp
[RelayCommand]
private void ImportFooma()
{
    // Implementation lands in Task 18.
}

[RelayCommand]
private void AddNewTrigger()
{
    // Implementation lands in Task 19.
}
```

- [ ] **Step 3: Build**

Standard build. Expected: green.

- [ ] **Step 4: Manual smoke test 5 — empty state renders**

Launch app. If `Settings.UniversalTriggers` is empty (or you temporarily clear it via `AppData\Local\CrystalRelay\...`), open the window.

Expected: ✨ + welcome text + two large cards side by side (Fooma cat with purple-tinted background, hammer with neutral background). Both action buttons are clickable (do nothing yet).

If `Settings.UniversalTriggers` is not empty, the empty-state Grid is collapsed (`Visibility="Collapsed"` via the converter) and you see the title bar only. That is correct — Phase B renders later.

Close the app.


---

## Task 17: Wire `ImportFoomaCommand` to the existing importer

**Files:**
- Modify: `VrcTwitchOscBridge/ViewModels/UniversalTriggersManagerViewModel.cs`

- [ ] **Step 1: Replace the `ImportFooma` stub with the real implementation**

```csharp
[RelayCommand]
private async Task ImportFoomaAsync()
{
    var dialog = new Microsoft.Win32.OpenFileDialog
    {
        Title = LocalizationManager.Translate("Universal Triggers Welcome Import Title"),
        Filter = "JSON files (*.json)|*.json|All files (*.*)|*.*",
        Multiselect = false,
    };
    if (dialog.ShowDialog() != true) return;

    try
    {
        var result = await FoomaInteractionConfigImporter.ImportAsync(dialog.FileName, _settings.UniversalTriggers).ConfigureAwait(true);
        // Fusion is already applied inside ImportAsync; new triggers are appended to the collection.
        await _mainWindowViewModel.SaveSettingsAsync().ConfigureAwait(true);
        OnPropertyChanged(nameof(IsEmpty));
        OnPropertyChanged(nameof(SubtitleSummary));
        // Re-trigger derived collection views once those exist (Task 23).
    }
    catch (Exception ex)
    {
        ThemedDialogWindow.ShowError(LocalizationManager.Translate("Imported {0} universal trigger(s) failed: {1}", 0, ex.Message));
    }
}
```

Adjust the actual method signature on `FoomaInteractionConfigImporter.ImportAsync` to match the existing API — grep `FoomaInteractionConfigImporter.cs` for `public static async Task<` to confirm.

If `MainWindowViewModel.SaveSettingsAsync` does not exist, use whichever public save method the rest of the codebase calls (e.g., `SettingsStore.SaveAsync(_settings)`).

- [ ] **Step 2: Build**

Standard build. Expected: green.

- [ ] **Step 3: Manual smoke test 6 — Fooma import flow**

Launch app, open the window, click "Choose file…". Expected: a Windows file picker opens. Pick a real Fooma JSON config.

After selecting, the imported triggers should land in `Settings.UniversalTriggers` (verify via the AppData JSON or by re-opening the window and seeing it switch out of Phase A). The empty-state hides because `IsEmpty` flips false.

Phase B is still a blank Grid for now (next tasks build it). That is expected.

---

## Task 18: Wire `AddNewTriggerCommand` to create a blank trigger and open the editor

**Files:**
- Modify: `VrcTwitchOscBridge/ViewModels/UniversalTriggersManagerViewModel.cs`

- [ ] **Step 1: Replace the `AddNewTrigger` stub**

```csharp
[RelayCommand]
private void AddNewTrigger()
{
    var rule = new UniversalTriggerRule
    {
        Id = Guid.NewGuid(),
        Name = LocalizationManager.Translate("Universal Triggers New Trigger Default Name"),
        TriggerType = UniversalTriggerType.ChatCommand,
        IsEnabled = true,
        ChatCommandEnabled = true,
        CommandText = "!example",
        ChatCommandPermission = ChatCommandPermission.Everyone,
    };
    _settings.UniversalTriggers.Add(rule);
    OnPropertyChanged(nameof(IsEmpty));
    OnPropertyChanged(nameof(SubtitleSummary));

    SelectedTrigger = rule;
    OpenEditor();
}
```

Add `using System;` if not already imported. Confirm `ChatCommandPermission.Everyone` is the correct enum name in this codebase — grep `UniversalTriggerRule.cs` for the property type.

- [ ] **Step 2: Add `SelectedTrigger` property and `OpenEditor` / `CloseEditor` methods (stubs)**

```csharp
[ObservableProperty]
private UniversalTriggerRule? selectedTrigger;

[ObservableProperty]
private bool isEditorOpen;

[RelayCommand]
private void OpenEditor()
{
    if (SelectedTrigger is null) return;
    IsEditorOpen = true;
}

[RelayCommand]
private void CloseEditor()
{
    IsEditorOpen = false;
    SelectedTrigger = null;
}
```

- [ ] **Step 3: Add `"Universal Triggers New Trigger Default Name"` localization key**

Open `Localization/en-US.extra.json`, add:

```json
"Universal Triggers New Trigger Default Name": "New trigger"
```

(Translations land in Task 36+.)

- [ ] **Step 4: Build**

Standard build. Expected: green.

- [ ] **Step 5: Manual smoke test 7 — Create New flow**

Open the window. Click "Start blank".

Expected: a new `UniversalTriggerRule` named "New trigger" is appended to `Settings.UniversalTriggers`. The empty state hides (`IsEmpty` is now false). `IsEditorOpen` is true (but the editor panel does not exist yet — it lands in Task 32). For now the window just shows the title bar.

Close + reopen the window. The new trigger persists in `Settings.UniversalTriggers`. The empty state stays hidden because there is now 1 trigger saved.

---

## Task 19: Build the populated state top toolbar

**Files:**
- Modify: `VrcTwitchOscBridge/UniversalTriggersManagerWindow.xaml`

The toolbar holds the search box, `+ New` button, Import Fooma button (with 18×18 Fooma icon), and Fooma help "?" button.

- [ ] **Step 1: Add a Phase-B body Grid**

Below the existing empty-state `<Grid Grid.Row="1" Visibility="...">` block, add a second Grid:

```xml
<Grid Grid.Row="1">
    <Grid.Style>
        <Style TargetType="Grid">
            <Style.Triggers>
                <DataTrigger Binding="{Binding IsEmpty}" Value="True">
                    <Setter Property="Visibility" Value="Collapsed" />
                </DataTrigger>
            </Style.Triggers>
        </Style>
    </Grid.Style>
    <Grid.RowDefinitions>
        <RowDefinition Height="Auto" />
        <RowDefinition Height="Auto" />
        <RowDefinition Height="*" />
        <RowDefinition Height="Auto" />
    </Grid.RowDefinitions>

    <!-- Toolbar (Row 0) -->
    <Border Grid.Row="0"
            Background="{DynamicResource NestedPanelBrush}"
            BorderBrush="{DynamicResource BorderBrush}"
            BorderThickness="0,0,0,1"
            Padding="14,8">
        <DockPanel LastChildFill="False">
            <Button DockPanel.Dock="Right"
                    Background="{DynamicResource AccentBrush}"
                    Foreground="{DynamicResource ComboTextBrush}"
                    FontWeight="Bold"
                    Padding="12,6"
                    Margin="8,0,0,0"
                    Content="{loc:Translate 'Universal Triggers New Trigger'}"
                    Command="{Binding AddNewTriggerCommand}" />
            <Button DockPanel.Dock="Right"
                    Style="{StaticResource SecondaryButtonStyle}"
                    Padding="10,4"
                    Margin="6,0,0,0"
                    Command="{Binding ImportFoomaCommand}">
                <StackPanel Orientation="Horizontal">
                    <Image Source="pack://application:,,,/Assets/fooma-icon.png"
                           Width="18" Height="18"
                           Margin="0,0,6,0"
                           RenderOptions.BitmapScalingMode="NearestNeighbor" />
                    <TextBlock Text="{loc:Translate 'Universal Triggers Import Fooma'}"
                               VerticalAlignment="Center" />
                </StackPanel>
            </Button>
            <Button DockPanel.Dock="Right"
                    Style="{StaticResource SecondaryButtonStyle}"
                    Padding="6,4"
                    Margin="0,0,0,0"
                    Content="?"
                    Command="{Binding OpenFoomaHelpCommand}" />
            <TextBox DockPanel.Dock="Right"
                     Width="220"
                     Margin="0,0,12,0"
                     Background="{DynamicResource InputBrush}"
                     Foreground="{DynamicResource TextBrush}"
                     BorderBrush="{DynamicResource InputBorderBrush}"
                     Text="{Binding SearchText, UpdateSourceTrigger=PropertyChanged}" />
        </DockPanel>
    </Border>

    <!-- Row 1, 2, 3 fill in later tasks -->
</Grid>
```

- [ ] **Step 2: Add `SearchText` to the VM**

```csharp
[ObservableProperty]
private string searchText = string.Empty;
```

The setter will trigger filter refresh in Task 23.

- [ ] **Step 3: Add `OpenFoomaHelpCommand` to the VM**

```csharp
[RelayCommand]
private void OpenFoomaHelp()
{
    var openLink = ThemedDialogWindow.ShowYesNo(
        LocalizationManager.Translate("Universal Triggers Fooma Help Title"),
        LocalizationManager.Translate("Universal Triggers Fooma Help Body"));
    if (openLink == true)
    {
        try
        {
            System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo
            {
                FileName = "https://foomaring.gumroad.com/l/lmrjbl",
                UseShellExecute = true,
            });
        }
        catch { /* ignore launch failures */ }
    }
}
```

(Adjust `ThemedDialogWindow.ShowYesNo` return type to match the existing API — it may be `bool` or `MessageBoxResult`.)

- [ ] **Step 4: Add 3 localization keys to `en-US.extra.json`**

```json
"Universal Triggers New Trigger": "+ New",
"Universal Triggers Import Fooma": "Import Fooma",
"Universal Triggers Fooma Help Title": "About Fooma Twitch Interaction",
"Universal Triggers Fooma Help Body": "Crystal Relay can import Fooma Twitch Interaction JSON configs to create Universal Triggers in one click. Open the Fooma project page to learn more or to grab the config tool?"
```

(Translations land in Task 36+.)

- [ ] **Step 5: Build**

Standard build. Expected: green.

- [ ] **Step 6: Manual smoke test 8 — toolbar renders**

Launch app, open the window. Expected (assuming at least one trigger is saved):
- Empty state hidden.
- Toolbar visible with: search box, ?, Import Fooma (with Fooma cat icon), + New.
- Click ?: themed dialog opens with help text + "yes/no" buttons. Yes opens the Fooma URL in the default browser.
- Click + New: appends another blank trigger.
- Click Import Fooma: file picker opens.


---

## Task 20: Build filter / Enable+Disable / Sort / Collapse-all bar

**Files:**
- Modify: `VrcTwitchOscBridge/UniversalTriggersManagerWindow.xaml`
- Modify: `VrcTwitchOscBridge/ViewModels/UniversalTriggersManagerViewModel.cs`

- [ ] **Step 1: Add filter mode enum and properties to VM**

```csharp
public enum UniversalTriggerFilterMode { All, Active, Disabled, NeedsFix, FromFooma }

[ObservableProperty]
private UniversalTriggerFilterMode filterMode = UniversalTriggerFilterMode.All;

public int CountAll => Triggers.Count;
public int CountActive => Triggers.Count(t => t.IsEnabled);
public int CountDisabled => Triggers.Count(t => !t.IsEnabled);
public int CountNeedsFix => Triggers.Count(t => t.IsEnabled && HasWarning(t));
public int CountFooma => Triggers.Count(t => FoomaInteractionConfigImporter.IsFoomaImport(t));

[RelayCommand] private void ShowAll() => FilterMode = UniversalTriggerFilterMode.All;
[RelayCommand] private void ShowActive() => FilterMode = UniversalTriggerFilterMode.Active;
[RelayCommand] private void ShowDisabled() => FilterMode = UniversalTriggerFilterMode.Disabled;
[RelayCommand] private void ShowNeedsFix() => FilterMode = UniversalTriggerFilterMode.NeedsFix;
[RelayCommand] private void ShowFooma() => FilterMode = UniversalTriggerFilterMode.FromFooma;
```

Add `using System.Linq;` and `using VrcTwitchOscBridge.Services;`.

- [ ] **Step 2: Add Enable/Disable All commands**

```csharp
[RelayCommand]
private async Task EnableAllAsync()
{
    foreach (var t in Triggers) t.IsEnabled = true;
    await _mainWindowViewModel.SaveSettingsAsync().ConfigureAwait(true);
    RaiseCountsChanged();
}

[RelayCommand]
private async Task DisableAllAsync()
{
    foreach (var t in Triggers) t.IsEnabled = false;
    await _mainWindowViewModel.SaveSettingsAsync().ConfigureAwait(true);
    RaiseCountsChanged();
}

private void RaiseCountsChanged()
{
    OnPropertyChanged(nameof(CountAll));
    OnPropertyChanged(nameof(CountActive));
    OnPropertyChanged(nameof(CountDisabled));
    OnPropertyChanged(nameof(CountNeedsFix));
    OnPropertyChanged(nameof(CountFooma));
    OnPropertyChanged(nameof(SubtitleSummary));
}
```

- [ ] **Step 3: Add Sort enum + commands**

```csharp
public enum UniversalTriggerSortMode { ByType, ByStatus, ByName, RecentlyEdited }

[ObservableProperty]
private UniversalTriggerSortMode sortMode = UniversalTriggerSortMode.ByType;
```

(`RecentlyEdited` requires the model to track `LastEditedAt`. If the model does not have such a field, leave the enum value but make the sort fall through to `ByType` and add a TODO note in the spec's Future section.)

- [ ] **Step 4: Add Collapse All / Expand All commands**

```csharp
[RelayCommand]
private void CollapseAll() { IsChatSectionCollapsed = true; IsRewardSectionCollapsed = true; IsBitsSectionCollapsed = true; IsSubsSectionCollapsed = true; IsFollowsSectionCollapsed = true; PersistCollapseFlags(); }

[RelayCommand]
private void ExpandAll() { IsChatSectionCollapsed = false; IsRewardSectionCollapsed = false; IsBitsSectionCollapsed = false; IsSubsSectionCollapsed = false; IsFollowsSectionCollapsed = false; PersistCollapseFlags(); }

[ObservableProperty] private bool isChatSectionCollapsed;
[ObservableProperty] private bool isRewardSectionCollapsed;
[ObservableProperty] private bool isBitsSectionCollapsed;
[ObservableProperty] private bool isSubsSectionCollapsed;
[ObservableProperty] private bool isFollowsSectionCollapsed;

private void PersistCollapseFlags()
{
    _settings.UniversalTriggersChatCollapsed = IsChatSectionCollapsed;
    _settings.UniversalTriggersRewardCollapsed = IsRewardSectionCollapsed;
    _settings.UniversalTriggersBitsCollapsed = IsBitsSectionCollapsed;
    _settings.UniversalTriggersSubsCollapsed = IsSubsSectionCollapsed;
    _settings.UniversalTriggersFollowsCollapsed = IsFollowsSectionCollapsed;
    _ = _mainWindowViewModel.SaveSettingsAsync();
}
```

(`AppSettings` collapse properties land in Task 25.)

- [ ] **Step 5: Add the filter+controls bar XAML to Row 1**

Add inside the Phase-B body Grid, as the Row 1 element:

```xml
<Border Grid.Row="1"
        Background="{DynamicResource NestedPanelBrush}"
        BorderBrush="{DynamicResource BorderBrush}"
        BorderThickness="0,0,0,1"
        Padding="14,8">
    <DockPanel LastChildFill="False">
        <!-- Filter chips (left) -->
        <StackPanel DockPanel.Dock="Left" Orientation="Horizontal">
            <ToggleButton Margin="0,0,6,0" Padding="10,4"
                          IsChecked="{Binding FilterMode, Converter={StaticResource EnumToBoolConverter}, ConverterParameter=All}"
                          Command="{Binding ShowAllCommand}">
                <TextBlock Text="{Binding CountAll, StringFormat='{}{0} All ({0})'}" />
            </ToggleButton>
            <!-- Repeat for Active / Disabled / NeedsFix / FromFooma. The Fooma chip has the 14x14 icon inline. -->
        </StackPanel>

        <!-- Right side: collapse-all, expand-all, sort, divider, Enable/Disable All -->
        <StackPanel DockPanel.Dock="Right" Orientation="Horizontal">
            <Button Style="{StaticResource SecondaryButtonStyle}" Margin="0,0,6,0" Padding="8,3"
                    Content="{loc:Translate 'Universal Triggers Collapse All'}"
                    Command="{Binding CollapseAllCommand}" />
            <Button Style="{StaticResource SecondaryButtonStyle}" Margin="0,0,12,0" Padding="8,3"
                    Content="{loc:Translate 'Universal Triggers Expand All'}"
                    Command="{Binding ExpandAllCommand}" />
            <!-- Sort dropdown -->
            <ComboBox Width="140" SelectedItem="{Binding SortMode}"
                      ItemsSource="{Binding SortModeOptions}"
                      Margin="0,0,12,0" />
            <Rectangle Width="1" Margin="0,2,12,2" Fill="{DynamicResource BorderBrush}" />
            <Button Margin="0,0,6,0" Padding="10,3"
                    Background="{DynamicResource WarnBrush}"
                    Foreground="{DynamicResource WarnTextBrush}"
                    BorderBrush="{DynamicResource WarnBorderBrush}"
                    Content="{loc:Translate 'Universal Triggers Disable All'}"
                    Command="{Binding DisableAllCommand}" />
            <Button Padding="10,3"
                    Background="{DynamicResource AccentDimBrush}"
                    Foreground="{DynamicResource AccentBrush}"
                    BorderBrush="{DynamicResource AccentBrush}"
                    Content="{loc:Translate 'Universal Triggers Enable All'}"
                    Command="{Binding EnableAllCommand}" />
        </StackPanel>
    </DockPanel>
</Border>
```

- [ ] **Step 6: Add `EnumToBoolConverter` (skip if it already exists in the project)**

Grep `EnumToBoolConverter`. If missing, create `VrcTwitchOscBridge/Converters/EnumToBoolConverter.cs`:

```csharp
using System;
using System.Globalization;
using System.Windows.Data;

namespace VrcTwitchOscBridge.Converters;

public sealed class EnumToBoolConverter : IValueConverter
{
    public object Convert(object value, Type targetType, object parameter, CultureInfo culture)
        => value?.ToString() == parameter?.ToString();
    public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture)
        => (value is true && parameter is not null) ? Enum.Parse(targetType, parameter.ToString()!) : Binding.DoNothing;
}
```

Add `<Compile Include="Converters\EnumToBoolConverter.cs" />` to csproj. Add resource entry to `UniversalTriggersManagerWindow.xaml` `<Window.Resources>`:

```xml
<conv:EnumToBoolConverter x:Key="EnumToBoolConverter" />
```

Add `xmlns:conv="clr-namespace:VrcTwitchOscBridge.Converters"`.

- [ ] **Step 7: Build**

Standard build. Expected: green (assuming the WarnBrush / WarnTextBrush / WarnBorderBrush palette resources already exist — they were added in commit `d79ffca`).

- [ ] **Step 8: Manual smoke test 9 — filter bar renders + Enable/Disable All work**

Launch + open window. Verify:
- Filter chips appear with counts.
- Clicking a chip highlights it.
- Click Disable All → all triggers' `IsEnabled` set false → toolbar counts update → no error.
- Click Enable All → all triggers re-enabled.
- Sort dropdown shows 4 options.


---

## Task 21: Persist section collapse state in AppSettings

**Files:**
- Modify: `VrcTwitchOscBridge/Models/AppSettings.cs`
- Modify: `VrcTwitchOscBridge/Services/SettingsStore.cs`

- [ ] **Step 1: Add 5 nullable bool properties to AppSettings**

In `AppSettings.cs`, near the existing `UniversalTriggers` collection, add:

```csharp
public bool UniversalTriggersChatCollapsed { get; set; }
public bool UniversalTriggersRewardCollapsed { get; set; }
public bool UniversalTriggersBitsCollapsed { get; set; }
public bool UniversalTriggersSubsCollapsed { get; set; }
public bool UniversalTriggersFollowsCollapsed { get; set; }
```

(Defaults to `false` = expanded.)

- [ ] **Step 2: Persist in `SettingsStore.cs`**

Grep `SettingsStore.cs` for `UniversalTriggers` (the existing collection's load/save). In the same DTO class (`PersistedSettings` or similar), add 5 matching `bool?` properties:

```csharp
public bool? UniversalTriggersChatCollapsed { get; set; }
public bool? UniversalTriggersRewardCollapsed { get; set; }
public bool? UniversalTriggersBitsCollapsed { get; set; }
public bool? UniversalTriggersSubsCollapsed { get; set; }
public bool? UniversalTriggersFollowsCollapsed { get; set; }
```

In the load path: `settings.UniversalTriggersChatCollapsed = persisted.UniversalTriggersChatCollapsed ?? false;` (repeat for 5).

In the save path: `persisted.UniversalTriggersChatCollapsed = settings.UniversalTriggersChatCollapsed;` (repeat for 5).

- [ ] **Step 3: Wire VM constructor to hydrate from settings**

In `UniversalTriggersManagerViewModel.cs` constructor, after `_mainWindowViewModel = ...`:

```csharp
IsChatSectionCollapsed = _settings.UniversalTriggersChatCollapsed;
IsRewardSectionCollapsed = _settings.UniversalTriggersRewardCollapsed;
IsBitsSectionCollapsed = _settings.UniversalTriggersBitsCollapsed;
IsSubsSectionCollapsed = _settings.UniversalTriggersSubsCollapsed;
IsFollowsSectionCollapsed = _settings.UniversalTriggersFollowsCollapsed;
```

- [ ] **Step 4: Build**

Standard build. Expected: green. Existing AppData JSON files without these keys load with `false` defaults (no migration needed).

---

## Task 22: Add section CollectionViews and filter+sort logic to the VM

**Files:**
- Modify: `VrcTwitchOscBridge/ViewModels/UniversalTriggersManagerViewModel.cs`

The five sections each get their own `ICollectionView` over a derived list filtered by trigger type + the global FilterMode + SearchText.

- [ ] **Step 1: Add 5 `ICollectionView` properties + a helper to build them**

```csharp
public ICollectionView ChatSection { get; }
public ICollectionView RewardSection { get; }
public ICollectionView BitsSection { get; }
public ICollectionView SubsSection { get; }
public ICollectionView FollowsSection { get; }

// In constructor (after _settings assignment, before hydrating collapse flags):
ChatSection = BuildSection(t => t.TriggerType == UniversalTriggerType.ChatCommand);
RewardSection = BuildSection(t => t.TriggerType == UniversalTriggerType.ChannelPointReward);
BitsSection = BuildSection(t => t.TriggerType == UniversalTriggerType.Bits);
SubsSection = BuildSection(t => t.TriggerType == UniversalTriggerType.Subscription || t.TriggerType == UniversalTriggerType.GiftSubscription);
FollowsSection = BuildSection(t => t.TriggerType == UniversalTriggerType.Follow);

private ICollectionView BuildSection(Predicate<UniversalTriggerRule> typeFilter)
{
    var view = CollectionViewSource.GetDefaultView(new ObservableCollection<UniversalTriggerRule>(_settings.UniversalTriggers));
    view.Filter = o =>
    {
        var t = (UniversalTriggerRule)o;
        if (!typeFilter(t)) return false;
        if (!MatchesFilterMode(t)) return false;
        if (!MatchesSearchText(t)) return false;
        return true;
    };
    return view;
}

private bool MatchesFilterMode(UniversalTriggerRule t) => FilterMode switch
{
    UniversalTriggerFilterMode.All => true,
    UniversalTriggerFilterMode.Active => t.IsEnabled,
    UniversalTriggerFilterMode.Disabled => !t.IsEnabled,
    UniversalTriggerFilterMode.NeedsFix => t.IsEnabled && HasWarning(t),
    UniversalTriggerFilterMode.FromFooma => FoomaInteractionConfigImporter.IsFoomaImport(t),
    _ => true,
};

private bool MatchesSearchText(UniversalTriggerRule t)
{
    if (string.IsNullOrWhiteSpace(SearchText)) return true;
    var q = SearchText.Trim();
    return t.Name?.Contains(q, StringComparison.OrdinalIgnoreCase) == true
        || t.CommandText?.Contains(q, StringComparison.OrdinalIgnoreCase) == true
        || t.RewardTitle?.Contains(q, StringComparison.OrdinalIgnoreCase) == true
        || t.Actions.Any(a => a.OscAddress?.Contains(q, StringComparison.OrdinalIgnoreCase) == true);
}

partial void OnFilterModeChanged(UniversalTriggerFilterMode value) => RefreshAllSections();
partial void OnSearchTextChanged(string value) => RefreshAllSections();
partial void OnSortModeChanged(UniversalTriggerSortMode value) => RefreshAllSections();

private void RefreshAllSections()
{
    foreach (var v in new[] { ChatSection, RewardSection, BitsSection, SubsSection, FollowsSection })
    {
        v.Refresh();
        ApplySort(v);
    }
    RaiseSectionCountsChanged();
}

private void ApplySort(ICollectionView view)
{
    view.SortDescriptions.Clear();
    switch (SortMode)
    {
        case UniversalTriggerSortMode.ByName:
            view.SortDescriptions.Add(new SortDescription(nameof(UniversalTriggerRule.Name), ListSortDirection.Ascending));
            break;
        case UniversalTriggerSortMode.ByStatus:
            view.SortDescriptions.Add(new SortDescription(nameof(UniversalTriggerRule.IsEnabled), ListSortDirection.Descending));
            break;
        case UniversalTriggerSortMode.ByType:
        default:
            view.SortDescriptions.Add(new SortDescription(nameof(UniversalTriggerRule.TriggerType), ListSortDirection.Ascending));
            break;
    }
}

private void RaiseSectionCountsChanged()
{
    OnPropertyChanged(nameof(ChatSectionCount));
    OnPropertyChanged(nameof(RewardSectionCount));
    OnPropertyChanged(nameof(BitsSectionCount));
    OnPropertyChanged(nameof(SubsSectionCount));
    OnPropertyChanged(nameof(FollowsSectionCount));
}

public int ChatSectionCount => CountOf(ChatSection);
public int RewardSectionCount => CountOf(RewardSection);
public int BitsSectionCount => CountOf(BitsSection);
public int SubsSectionCount => CountOf(SubsSection);
public int FollowsSectionCount => CountOf(FollowsSection);

private static int CountOf(ICollectionView view) => view.Cast<object>().Count();
```

Add `using System.ComponentModel; using System.Windows.Data; using System.Linq;`.

The `OnXxxChanged` partial methods are auto-generated by `CommunityToolkit.Mvvm` from `[ObservableProperty]`. If the codebase uses manual setters, refactor accordingly.

- [ ] **Step 2: Subscribe to the underlying collection so external changes refresh views**

```csharp
// In constructor, after sections are built:
_settings.UniversalTriggers.CollectionChanged += (_, _) => RefreshAllSections();
```

- [ ] **Step 3: Build**

Standard build. Expected: green.

- [ ] **Step 4: Add `SortModeOptions` array property** (for the ComboBox)

```csharp
public IReadOnlyList<UniversalTriggerSortMode> SortModeOptions { get; } = (UniversalTriggerSortMode[])Enum.GetValues(typeof(UniversalTriggerSortMode));
```

- [ ] **Step 5: Manual smoke test 10 — filter/search shrinks counts**

Launch + open. Type into the search box. Counts on filter chips update live.
Click a chip → counts on it stay the same but the visible cards (later) will filter. For now this only verifies the VM logic works through binding observation.


---

## Task 23: Build the section grid layout

**Files:**
- Modify: `VrcTwitchOscBridge/UniversalTriggersManagerWindow.xaml`

The body Row 2 holds a vertical `ScrollViewer` containing five section blocks. Each section block has: a clickable header bar (emoji + name + count suffix + per-section disable button + chevron), then a collapsible `WrapPanel` of fixed-size cards.

- [ ] **Step 1: Add a `ScrollViewer` to Row 2**

Inside the Phase-B body Grid:

```xml
<ScrollViewer Grid.Row="2" VerticalScrollBarVisibility="Auto" HorizontalScrollBarVisibility="Disabled">
    <StackPanel Margin="14" x:Name="SectionStack">
        <!-- 5 section blocks land here in subsequent steps -->
    </StackPanel>
</ScrollViewer>
```

- [ ] **Step 2: Add the Chat section block**

Inside `SectionStack`:

```xml
<StackPanel Margin="0,0,0,14">
    <!-- Header -->
    <Border Background="{DynamicResource PanelBrush}"
            BorderBrush="{DynamicResource InputBorderBrush}"
            BorderThickness="1"
            CornerRadius="10"
            Padding="8,6"
            Cursor="Hand"
            MouseLeftButtonUp="OnToggleChatSection">
        <DockPanel LastChildFill="False">
            <TextBlock DockPanel.Dock="Left" FontSize="14" Text="💬" Margin="0,0,8,0" />
            <TextBlock DockPanel.Dock="Left" Foreground="{DynamicResource TextBrush}" FontWeight="Bold" FontSize="11" VerticalAlignment="Center"
                       Text="{loc:Translate 'Universal Triggers Section Chat'}" />
            <TextBlock DockPanel.Dock="Left" Foreground="{DynamicResource MutedBrush}" FontSize="11" VerticalAlignment="Center" Margin="6,0,0,0"
                       Text="{Binding ChatSectionSuffix}" />
            <TextBlock DockPanel.Dock="Right" Foreground="{Binding IsChatSectionCollapsed, Converter={StaticResource CollapseChevronColorConverter}}" FontSize="14"
                       Text="{Binding IsChatSectionCollapsed, Converter={StaticResource CollapseChevronTextConverter}}" />
            <Button DockPanel.Dock="Right" Style="{StaticResource SecondaryButtonStyle}" Padding="6,2" Margin="0,0,8,0"
                    Content="{loc:Translate 'Universal Triggers Section Disable All Mini'}"
                    Command="{Binding DisableSectionCommand}" CommandParameter="Chat" />
        </DockPanel>
    </Border>

    <!-- Card grid (collapsible) -->
    <ItemsControl ItemsSource="{Binding ChatSection}" Margin="4,8,4,0"
                  Visibility="{Binding IsChatSectionCollapsed, Converter={StaticResource InverseBoolToVisibilityConverter}}">
        <ItemsControl.ItemsPanel>
            <ItemsPanelTemplate>
                <WrapPanel Orientation="Horizontal" />
            </ItemsPanelTemplate>
        </ItemsControl.ItemsPanel>
        <ItemsControl.ItemTemplate>
            <DataTemplate>
                <ContentControl Width="240" Height="168" Margin="0,0,10,10"
                                Content="{Binding}"
                                ContentTemplate="{StaticResource UniversalTriggerCardTemplate}" />
            </DataTemplate>
        </ItemsControl.ItemTemplate>
    </ItemsControl>
</StackPanel>
```

- [ ] **Step 3: Repeat the block for Reward / Bits / Subs / Follows sections**

Use identical XAML structure, changing:
- emoji: 💬 / 🎁 / 💎 / ⭐ / ❤️
- section name key: `'Universal Triggers Section Chat'` / `Reward` / `Bits` / `Subs Combined` / `Follows`
- suffix binding: `ChatSectionSuffix` / `RewardSectionSuffix` / `BitsSectionSuffix` / `SubsSectionSuffix` / `FollowsSectionSuffix`
- collapse flag binding: `IsChatSectionCollapsed` / `IsReward...` / `IsBits...` / `IsSubs...` / `IsFollows...`
- collection binding: `ChatSection` / `RewardSection` / `BitsSection` / `SubsSection` / `FollowsSection`
- DisableSection CommandParameter: `"Chat"` / `"Reward"` / `"Bits"` / `"Subs"` / `"Follows"`
- code-behind handler: `OnToggleChatSection` / `OnToggleRewardSection` / etc.

- [ ] **Step 4: Add `DisableSectionCommand` to VM**

```csharp
[RelayCommand]
private async Task DisableSectionAsync(string section)
{
    Predicate<UniversalTriggerRule> match = section switch
    {
        "Chat" => t => t.TriggerType == UniversalTriggerType.ChatCommand,
        "Reward" => t => t.TriggerType == UniversalTriggerType.ChannelPointReward,
        "Bits" => t => t.TriggerType == UniversalTriggerType.Bits,
        "Subs" => t => t.TriggerType == UniversalTriggerType.Subscription || t.TriggerType == UniversalTriggerType.GiftSubscription,
        "Follows" => t => t.TriggerType == UniversalTriggerType.Follow,
        _ => _ => false,
    };
    foreach (var t in _settings.UniversalTriggers.Where(t => match(t)))
        t.IsEnabled = false;
    await _mainWindowViewModel.SaveSettingsAsync().ConfigureAwait(true);
    RaiseSectionCountsChanged();
    RaiseCountsChanged();
}
```

- [ ] **Step 5: Add 5 section toggle handlers to code-behind**

In `UniversalTriggersManagerWindow.xaml.cs`:

```csharp
private void OnToggleChatSection(object sender, MouseButtonEventArgs e) => Vm.IsChatSectionCollapsed = !Vm.IsChatSectionCollapsed;
private void OnToggleRewardSection(object sender, MouseButtonEventArgs e) => Vm.IsRewardSectionCollapsed = !Vm.IsRewardSectionCollapsed;
private void OnToggleBitsSection(object sender, MouseButtonEventArgs e) => Vm.IsBitsSectionCollapsed = !Vm.IsBitsSectionCollapsed;
private void OnToggleSubsSection(object sender, MouseButtonEventArgs e) => Vm.IsSubsSectionCollapsed = !Vm.IsSubsSectionCollapsed;
private void OnToggleFollowsSection(object sender, MouseButtonEventArgs e) => Vm.IsFollowsSectionCollapsed = !Vm.IsFollowsSectionCollapsed;
private UniversalTriggersManagerViewModel Vm => (UniversalTriggersManagerViewModel)DataContext;
```

Add `using ViewModels;` if not already.

Also wire `PropertyChanged` on collapse flags to call `PersistCollapseFlags`:

```csharp
// In VM constructor, after sections are built:
PropertyChanged += (_, e) =>
{
    if (e.PropertyName is nameof(IsChatSectionCollapsed) or nameof(IsRewardSectionCollapsed)
        or nameof(IsBitsSectionCollapsed) or nameof(IsSubsSectionCollapsed) or nameof(IsFollowsSectionCollapsed))
    {
        PersistCollapseFlags();
    }
};
```

- [ ] **Step 6: Add `CollapseChevronTextConverter` and `CollapseChevronColorConverter`**

Skip if equivalent converters already exist. Create simple value converters that return `"▴"` / `"▾"` and `AccentBrush` / `MutedBrush` respectively, based on the bool input. Add to csproj, register as window resources.

Also add `InverseBoolToVisibilityConverter` if not already in the project.

- [ ] **Step 7: Add 5 `XxxSectionSuffix` getters to VM**

```csharp
public string ChatSectionSuffix => BuildSuffix(ChatSection);
public string RewardSectionSuffix => BuildSuffix(RewardSection);
public string BitsSectionSuffix => BuildSuffix(BitsSection);
public string SubsSectionSuffix => BuildSuffix(SubsSection);
public string FollowsSectionSuffix => BuildSuffix(FollowsSection);

private string BuildSuffix(ICollectionView view)
{
    var rules = view.Cast<UniversalTriggerRule>().ToList();
    var active = rules.Count(t => t.IsEnabled && !HasWarning(t));
    var warn = rules.Count(t => t.IsEnabled && HasWarning(t));
    var off = rules.Count(t => !t.IsEnabled);
    return (warn, off) switch
    {
        (0, 0) => LocalizationManager.Translate("Universal Triggers Section Active Suffix", active),
        (>0, 0) => LocalizationManager.Translate("Universal Triggers Section Active Hidden Suffix", active, warn),
        (0, >0) => LocalizationManager.Translate("Universal Triggers Section Off Suffix", active, off),
        _ => LocalizationManager.Translate("Universal Triggers Section Mixed Suffix", active, warn, off),
    };
}
```

Add `RaiseSectionSuffixesChanged()` to `RefreshAllSections` and `RaiseCountsChanged`:

```csharp
OnPropertyChanged(nameof(ChatSectionSuffix));
OnPropertyChanged(nameof(RewardSectionSuffix));
OnPropertyChanged(nameof(BitsSectionSuffix));
OnPropertyChanged(nameof(SubsSectionSuffix));
OnPropertyChanged(nameof(FollowsSectionSuffix));
```

- [ ] **Step 8: Build**

Standard build. Expected: green. The `UniversalTriggerCardTemplate` resource is not yet defined — its bindings will be `{Binding}` against the `UniversalTriggerRule` and the ContentControl will fall back to the default text representation until Task 24 lands.

- [ ] **Step 9: Manual smoke test 11 — sections render with collapse**

Launch + open with at least one of each trigger type in `Settings.UniversalTriggers` (use Fooma import to populate fast). Verify:
- 5 section blocks visible.
- Click a section header → it collapses (chevron flips to ▾, grid hides).
- Click again → expands.
- Suffix text shows correct counts.
- Restart the app → collapse state restores per persisted flags.
- DisableSection button on each header disables only that section's triggers.


---

## Task 24: Build the card template

**Files:**
- Create: `VrcTwitchOscBridge/ViewModels/UniversalTriggerCardViewModel.cs` (fresh — different from the deleted orphan)
- Modify: `VrcTwitchOscBridge/UniversalTriggersManagerWindow.xaml` (add the `UniversalTriggerCardTemplate` resource)
- Modify: `VrcTwitchOscBridge/ViewModels/UniversalTriggersManagerViewModel.cs` (cache card VMs)

The card displays icon + name + pills + toggle + description + Test/Edit buttons in a fixed 240×168 box with a status stripe on the top edge.

- [ ] **Step 1: Create the card VM**

Write `VrcTwitchOscBridge/ViewModels/UniversalTriggerCardViewModel.cs`:

```csharp
using System;
using System.Linq;
using System.Windows.Media;
using CommunityToolkit.Mvvm.ComponentModel;
using VrcTwitchOscBridge.Models;
using VrcTwitchOscBridge.Services;

namespace VrcTwitchOscBridge.ViewModels;

public enum UniversalTriggerCardStatus { Ready, Warn, Disabled }

public partial class UniversalTriggerCardViewModel : ObservableObject
{
    public UniversalTriggerRule Rule { get; }
    private readonly Func<UniversalTriggerRule, bool> _isWarnFn;

    public UniversalTriggerCardViewModel(UniversalTriggerRule rule, Func<UniversalTriggerRule, bool> isWarnFn)
    {
        Rule = rule;
        _isWarnFn = isWarnFn;
        rule.PropertyChanged += (_, _) => RefreshDerived();
    }

    public UniversalTriggerCardStatus Status =>
        !Rule.IsEnabled ? UniversalTriggerCardStatus.Disabled
        : _isWarnFn(Rule) ? UniversalTriggerCardStatus.Warn
        : UniversalTriggerCardStatus.Ready;

    public string TypePill => Rule.TriggerType switch
    {
        UniversalTriggerType.ChatCommand => LocalizationManager.Translate("Universal Triggers Type Pill Chat"),
        UniversalTriggerType.ChannelPointReward => LocalizationManager.Translate("Universal Triggers Type Pill Reward"),
        UniversalTriggerType.Bits => LocalizationManager.Translate("Universal Triggers Type Pill Bits"),
        UniversalTriggerType.Subscription => LocalizationManager.Translate("Universal Triggers Type Pill Sub"),
        UniversalTriggerType.GiftSubscription => LocalizationManager.Translate("Universal Triggers Type Pill Gift Sub"),
        UniversalTriggerType.Follow => LocalizationManager.Translate("Universal Triggers Type Pill Follow"),
        _ => string.Empty,
    };

    public string EmojiIcon => Rule.TriggerType switch
    {
        UniversalTriggerType.ChatCommand => "💬",
        UniversalTriggerType.ChannelPointReward => "🎁",
        UniversalTriggerType.Bits => "💎",
        UniversalTriggerType.Subscription => "⭐",
        UniversalTriggerType.GiftSubscription => "🎀",
        UniversalTriggerType.Follow => "❤️",
        _ => "❓",
    };

    public string StatusPill => Status switch
    {
        UniversalTriggerCardStatus.Ready => LocalizationManager.Translate("Universal Triggers Status Ready"),
        UniversalTriggerCardStatus.Warn => LocalizationManager.Translate("Universal Triggers Status Avatar Missing"),
        UniversalTriggerCardStatus.Disabled => LocalizationManager.Translate("Universal Triggers Status Disabled"),
        _ => string.Empty,
    };

    public Brush StatusStripeBrush => Status switch
    {
        UniversalTriggerCardStatus.Ready => (Brush)System.Windows.Application.Current.Resources["StatusStripeReadyBrush"] ?? Brushes.Green,
        UniversalTriggerCardStatus.Warn => (Brush)System.Windows.Application.Current.Resources["StatusStripeWarnBrush"] ?? Brushes.Goldenrod,
        UniversalTriggerCardStatus.Disabled => (Brush)System.Windows.Application.Current.Resources["StatusStripeOffBrush"] ?? Brushes.Gray,
        _ => Brushes.Gray,
    };

    public bool IsFromFooma => FoomaInteractionConfigImporter.IsFoomaImport(Rule);

    public string Description
    {
        get
        {
            var actionSummary = BuildActionSummary();
            return Rule.TriggerType switch
            {
                UniversalTriggerType.ChatCommand => LocalizationManager.Translate("Universal Triggers Description Chat", Rule.CommandText ?? string.Empty, actionSummary),
                UniversalTriggerType.ChannelPointReward when Rule.RewardSyncMode == UniversalTriggerRewardSyncMode.CreateOrManage
                    => LocalizationManager.Translate("Universal Triggers Description Reward Managed", Rule.RewardTitle ?? string.Empty, Rule.RewardCost, Rule.RewardCooldownSeconds, actionSummary),
                UniversalTriggerType.ChannelPointReward
                    => LocalizationManager.Translate("Universal Triggers Description Reward Linked", Rule.RewardTitle ?? string.Empty),
                UniversalTriggerType.Bits when Rule.MaximumBits > 0
                    => LocalizationManager.Translate("Universal Triggers Description Bits Range", Rule.MinimumBits, Rule.MaximumBits, actionSummary),
                UniversalTriggerType.Bits
                    => LocalizationManager.Translate("Universal Triggers Description Bits Open", Rule.MinimumBits, actionSummary),
                UniversalTriggerType.Subscription => LocalizationManager.Translate("Universal Triggers Description Subs", Rule.SubscriptionTier.ToString(), actionSummary),
                UniversalTriggerType.GiftSubscription => LocalizationManager.Translate("Universal Triggers Description Gift Subs", actionSummary),
                UniversalTriggerType.Follow => LocalizationManager.Translate("Universal Triggers Description Follow", actionSummary),
                _ => string.Empty,
            };
        }
    }

    private string BuildActionSummary()
    {
        var actions = Rule.Actions;
        if (actions.Count == 0) return "(no actions)";
        if (actions.Count == 1)
        {
            var a = actions[0];
            return $"{a.OscAddress} {a.TargetValue} for {a.DurationSeconds}s";
        }
        var key = Rule.ExecuteRandomAction ? "Universal Triggers Action Summary Random" : "Universal Triggers Action Summary All";
        return LocalizationManager.Translate(key, actions.Count);
    }

    private void RefreshDerived()
    {
        OnPropertyChanged(nameof(Status));
        OnPropertyChanged(nameof(StatusPill));
        OnPropertyChanged(nameof(StatusStripeBrush));
        OnPropertyChanged(nameof(TypePill));
        OnPropertyChanged(nameof(EmojiIcon));
        OnPropertyChanged(nameof(Description));
        OnPropertyChanged(nameof(IsFromFooma));
    }
}
```

Add `<Compile Include="ViewModels\UniversalTriggerCardViewModel.cs" />` to csproj.

Confirm `UniversalTriggerRewardSyncMode` enum name + values match the model (grep `UniversalTriggerRule.cs`).

Add three brush resources to a theme dictionary or `Window.Resources`:

```xml
<SolidColorBrush x:Key="StatusStripeReadyBrush" Color="#4ADE80" />
<SolidColorBrush x:Key="StatusStripeWarnBrush" Color="#FBBF24" />
<SolidColorBrush x:Key="StatusStripeOffBrush" Color="#6B7280" />
```

- [ ] **Step 2: Replace the `ChatSection` `ItemsSource` binding to use card VMs instead of raw rules**

Change the VM section properties so they return `ICollectionView` over `UniversalTriggerCardViewModel` (not `UniversalTriggerRule`). The simplest way:

```csharp
private readonly Dictionary<UniversalTriggerRule, UniversalTriggerCardViewModel> _cardLookup = new();
private UniversalTriggerCardViewModel GetCard(UniversalTriggerRule rule)
{
    if (!_cardLookup.TryGetValue(rule, out var card))
    {
        card = new UniversalTriggerCardViewModel(rule, HasWarning);
        _cardLookup[rule] = card;
    }
    return card;
}

private ICollectionView BuildSection(Predicate<UniversalTriggerRule> typeFilter)
{
    var cards = new ObservableCollection<UniversalTriggerCardViewModel>(
        _settings.UniversalTriggers.Select(GetCard));
    var view = CollectionViewSource.GetDefaultView(cards);
    view.Filter = o => {
        var c = (UniversalTriggerCardViewModel)o;
        if (!typeFilter(c.Rule)) return false;
        if (!MatchesFilterMode(c.Rule)) return false;
        if (!MatchesSearchText(c.Rule)) return false;
        return true;
    };
    // Subscribe to underlying collection so adding/removing rules updates this view's source.
    _settings.UniversalTriggers.CollectionChanged += (_, e) =>
    {
        if (e.NewItems != null) foreach (UniversalTriggerRule r in e.NewItems) cards.Add(GetCard(r));
        if (e.OldItems != null) foreach (UniversalTriggerRule r in e.OldItems) { var c = GetCard(r); cards.Remove(c); _cardLookup.Remove(r); }
        view.Refresh();
    };
    return view;
}
```

- [ ] **Step 3: Define the `UniversalTriggerCardTemplate` resource in `UniversalTriggersManagerWindow.xaml`**

In `<Window.Resources>`:

```xml
<DataTemplate x:Key="UniversalTriggerCardTemplate" DataType="{x:Type vm:UniversalTriggerCardViewModel}">
    <Border Background="{DynamicResource PanelBrush}"
            BorderBrush="{DynamicResource InputBorderBrush}"
            BorderThickness="1,3,1,1"
            CornerRadius="12"
            Padding="10,12,10,12"
            Cursor="Hand">
        <Border.Resources>
            <Style TargetType="Border">
                <Style.Triggers>
                    <DataTrigger Binding="{Binding Status}" Value="Ready">
                        <Setter Property="BorderBrush" Value="{DynamicResource StatusStripeReadyBrush}" />
                    </DataTrigger>
                    <DataTrigger Binding="{Binding Status}" Value="Warn">
                        <Setter Property="BorderBrush" Value="{DynamicResource StatusStripeWarnBrush}" />
                    </DataTrigger>
                    <DataTrigger Binding="{Binding Status}" Value="Disabled">
                        <Setter Property="BorderBrush" Value="{DynamicResource StatusStripeOffBrush}" />
                        <Setter Property="Opacity" Value="0.65" />
                    </DataTrigger>
                </Style.Triggers>
            </Style>
        </Border.Resources>
        <Border.InputBindings>
            <MouseBinding MouseAction="LeftClick"
                          Command="{Binding DataContext.OpenEditorCommand, RelativeSource={RelativeSource AncestorType=Window}}"
                          CommandParameter="{Binding Rule}" />
        </Border.InputBindings>
        <Grid>
            <Grid.RowDefinitions>
                <RowDefinition Height="42" />
                <RowDefinition Height="*" />
                <RowDefinition Height="26" />
            </Grid.RowDefinitions>

            <!-- Top row: icon + name+pills + toggle -->
            <DockPanel Grid.Row="0" LastChildFill="True">
                <Border DockPanel.Dock="Left" Width="36" Height="36" CornerRadius="10"
                        Background="{DynamicResource AccentDimBrush}">
                    <TextBlock Text="{Binding EmojiIcon}" FontSize="18" HorizontalAlignment="Center" VerticalAlignment="Center" />
                </Border>
                <ToggleButton DockPanel.Dock="Right" Width="32" Height="18" Margin="6,0,0,0"
                              IsChecked="{Binding Rule.IsEnabled, Mode=TwoWay}" />
                <StackPanel Margin="8,0,6,0">
                    <TextBlock Text="{Binding Rule.Name}" FontWeight="Bold" FontSize="12"
                               Foreground="{DynamicResource TextBrush}"
                               TextTrimming="CharacterEllipsis"
                               ToolTip="{Binding Rule.Name}" />
                    <WrapPanel Margin="0,3,0,0">
                        <Border Background="{DynamicResource AccentBrush}" CornerRadius="8" Padding="6,1" Margin="0,0,4,0">
                            <TextBlock Text="{Binding TypePill}" Foreground="{DynamicResource ComboTextBrush}" FontSize="8" FontWeight="Bold" />
                        </Border>
                        <Border Background="{Binding StatusStripeBrush}" CornerRadius="8" Padding="6,1" Margin="0,0,4,0">
                            <TextBlock Text="{Binding StatusPill}" Foreground="{DynamicResource TextBrush}" FontSize="8" FontWeight="Bold" />
                        </Border>
                        <Border Background="{DynamicResource StatusChipBrush}" CornerRadius="8" Padding="5,1" Margin="0,0,4,0"
                                Visibility="{Binding IsFromFooma, Converter={StaticResource BoolToVisibilityConverter}}">
                            <StackPanel Orientation="Horizontal">
                                <Image Source="pack://application:,,,/Assets/fooma-icon.png" Width="12" Height="12" Margin="0,0,3,0"
                                       RenderOptions.BitmapScalingMode="NearestNeighbor" />
                                <TextBlock Text="{loc:Translate 'Universal Triggers Source Fooma'}" Foreground="{DynamicResource MutedBrush}" FontSize="8" />
                            </StackPanel>
                        </Border>
                    </WrapPanel>
                </StackPanel>
            </DockPanel>

            <!-- Description -->
            <TextBlock Grid.Row="1" Text="{Binding Description}" Foreground="{DynamicResource MutedBrush}" FontSize="10"
                       TextWrapping="Wrap" TextTrimming="CharacterEllipsis" MaxHeight="50" Margin="0,6,0,0" />

            <!-- Buttons row -->
            <Grid Grid.Row="2" Margin="0,4,0,0">
                <Grid.ColumnDefinitions>
                    <ColumnDefinition Width="*" />
                    <ColumnDefinition Width="5" />
                    <ColumnDefinition Width="*" />
                </Grid.ColumnDefinitions>
                <Button Grid.Column="0" Style="{StaticResource SecondaryButtonStyle}" Padding="4,2" FontSize="10"
                        Content="{loc:Translate 'Universal Triggers Card Test'}"
                        Command="{Binding DataContext.TestTriggerCommand, RelativeSource={RelativeSource AncestorType=Window}}"
                        CommandParameter="{Binding Rule}" />
                <Button Grid.Column="2" Style="{StaticResource SecondaryButtonStyle}" Padding="4,2" FontSize="10"
                        Content="{loc:Translate 'Universal Triggers Card Edit'}"
                        Command="{Binding DataContext.OpenEditorCommand, RelativeSource={RelativeSource AncestorType=Window}}"
                        CommandParameter="{Binding Rule}" />
            </Grid>
        </Grid>
    </Border>
</DataTemplate>
```

- [ ] **Step 4: Add `OpenEditorCommand(UniversalTriggerRule)` and `TestTriggerCommand(UniversalTriggerRule)` to VM**

```csharp
[RelayCommand]
private void OpenEditor(UniversalTriggerRule? rule)
{
    SelectedTrigger = rule;
    IsEditorOpen = rule is not null;
}

[RelayCommand]
private async Task TestTriggerAsync(UniversalTriggerRule rule)
{
    var snapshot = BridgeRuntimeConfiguration.CreateManualTestSnapshot(rule);
    if (snapshot is null) return;
    await _mainWindowViewModel.Coordinator.SendTestUniversalTriggerAsync(snapshot, System.Threading.CancellationToken.None).ConfigureAwait(true);
}
```

(Adjust property names: `_mainWindowViewModel.Coordinator` may be exposed as `BridgeCoordinator` or similar. Grep `MainWindowViewModel.cs` for the field name.)

- [ ] **Step 5: Build**

Standard build. Expected: green.

- [ ] **Step 6: Manual smoke test 12 — cards render**

Launch + open with several saved triggers. Verify:
- Cards appear in their respective sections.
- All cards are uniform 240×168.
- Status stripe on top is green / amber / grey based on rule state.
- Icon + name + type pill + status pill all visible.
- Fooma badge appears only on imported triggers.
- Toggle persists and immediately recolors the stripe.
- Test button fires an OSC packet (visible in the debug log: `BridgeCoordinator.SendTestUniversalTriggerAsync`).
- Edit button opens the (still empty) editor overlay (IsEditorOpen flips true).
- Long names truncate with ellipsis. Hover the name → tooltip shows full text.


---

## Task 25: Build the slide-out editor panel shell

**Files:**
- Modify: `VrcTwitchOscBridge/UniversalTriggersManagerWindow.xaml`

- [ ] **Step 1: Add the overlay Grid spanning all rows**

After the existing `<Grid Grid.Row="1">` block (the Phase B body) but still inside the outer 2-row Grid, add a second Grid that spans both rows as an overlay:

```xml
<Grid Grid.Row="0" Grid.RowSpan="2"
      Visibility="{Binding IsEditorOpen, Converter={StaticResource BoolToVisibilityConverter}}">
    <!-- Backdrop -->
    <Border Background="#80000000" MouseLeftButtonUp="OnEditorBackdropClicked" />
    <!-- Panel -->
    <Border Width="480" HorizontalAlignment="Right"
            Background="{DynamicResource PanelBrush}"
            BorderBrush="{DynamicResource BorderBrush}"
            BorderThickness="1,0,0,0">
        <Grid>
            <Grid.RowDefinitions>
                <RowDefinition Height="Auto" />
                <RowDefinition Height="*" />
                <RowDefinition Height="Auto" />
            </Grid.RowDefinitions>

            <!-- Editor title bar -->
            <Border Grid.Row="0" Background="{DynamicResource TitleBarBrush}"
                    BorderBrush="{DynamicResource BorderBrush}" BorderThickness="0,0,0,1" Padding="12,10">
                <DockPanel LastChildFill="True">
                    <Button DockPanel.Dock="Right" Content="✕" Padding="6,2" Margin="6,0,0,0"
                            Background="Transparent" BorderBrush="{DynamicResource BorderBrush}"
                            Foreground="{DynamicResource TitleBarTextBrush}"
                            Command="{Binding CloseEditorCommand}" />
                    <TextBlock Text="{Binding SelectedTrigger.Name}"
                               Foreground="{DynamicResource TitleBarTextBrush}"
                               FontWeight="Bold" FontSize="13" VerticalAlignment="Center" />
                </DockPanel>
            </Border>

            <!-- Body scroll -->
            <ScrollViewer Grid.Row="1" VerticalScrollBarVisibility="Auto">
                <StackPanel Margin="14" x:Name="EditorStack">
                    <!-- 4 editor cards land here in subsequent tasks -->
                </StackPanel>
            </ScrollViewer>

            <!-- Footer -->
            <Border Grid.Row="2" Background="{DynamicResource TitleBarBrush}"
                    BorderBrush="{DynamicResource BorderBrush}" BorderThickness="0,1,0,0" Padding="12,8">
                <DockPanel LastChildFill="False">
                    <Button DockPanel.Dock="Left" Style="{StaticResource SecondaryButtonStyle}" Padding="10,4"
                            Foreground="#FCA5A5" BorderBrush="#663033"
                            Content="{loc:Translate 'Universal Triggers Editor Delete'}"
                            Command="{Binding DeleteSelectedTriggerCommand}" />
                    <Button DockPanel.Dock="Right" Background="{DynamicResource AccentBrush}"
                            Foreground="{DynamicResource ComboTextBrush}" Padding="12,4" FontWeight="Bold"
                            Content="{loc:Translate 'Universal Triggers Editor Save'}"
                            Command="{Binding SaveEditorCommand}" />
                    <Button DockPanel.Dock="Right" Margin="0,0,8,0" Style="{StaticResource SecondaryButtonStyle}"
                            Padding="10,4"
                            Content="{loc:Translate 'Universal Triggers Editor Test Now'}"
                            Command="{Binding TestSelectedTriggerCommand}" />
                </DockPanel>
            </Border>
        </Grid>
    </Border>
</Grid>
```

- [ ] **Step 2: Add `OnEditorBackdropClicked` code-behind**

```csharp
private void OnEditorBackdropClicked(object sender, MouseButtonEventArgs e) => Vm.CloseEditorCommand.Execute(null);
```

- [ ] **Step 3: Add `SaveEditorCommand`, `DeleteSelectedTriggerCommand`, `TestSelectedTriggerCommand` to VM**

```csharp
private UniversalTriggerRuleSnapshot? _editorSnapshot;

partial void OnSelectedTriggerChanged(UniversalTriggerRule? value)
{
    _editorSnapshot = value is null ? null : BridgeRuntimeConfiguration.TryToUniversalSnapshot(value);
}

[RelayCommand]
private async Task SaveEditorAsync()
{
    if (SelectedTrigger is null) return;
    await _mainWindowViewModel.SaveSettingsAsync().ConfigureAwait(true);
    await _mainWindowViewModel.SynchronizeManagedChannelPointRewardsAsync().ConfigureAwait(true);
    IsEditorOpen = false;
    SelectedTrigger = null;
}

[RelayCommand]
private async Task DeleteSelectedTriggerAsync()
{
    if (SelectedTrigger is null) return;
    var ok = ThemedDialogWindow.ShowYesNo(
        LocalizationManager.Translate("Universal Triggers Delete Confirm Title"),
        LocalizationManager.Translate("Universal Triggers Delete Confirm Body"));
    if (ok != true) return;
    _settings.UniversalTriggers.Remove(SelectedTrigger);
    await _mainWindowViewModel.SaveSettingsAsync().ConfigureAwait(true);
    IsEditorOpen = false;
    SelectedTrigger = null;
    OnPropertyChanged(nameof(IsEmpty));
    RaiseSectionCountsChanged();
    RaiseCountsChanged();
}

[RelayCommand]
private async Task TestSelectedTriggerAsync()
{
    if (SelectedTrigger is null) return;
    await TestTriggerAsync(SelectedTrigger).ConfigureAwait(true);
}
```

For Cancel (backdrop click), the `CloseEditorCommand` from Task 14 already sets `IsEditorOpen = false` and `SelectedTrigger = null`. To make Cancel actually discard pending edits, modify `CloseEditor` to restore from `_editorSnapshot`:

```csharp
[RelayCommand]
private void CloseEditor()
{
    if (SelectedTrigger is not null && _editorSnapshot is not null)
    {
        // Discard changes by restoring properties from the snapshot.
        RestoreFromSnapshot(SelectedTrigger, _editorSnapshot.Value);
    }
    IsEditorOpen = false;
    SelectedTrigger = null;
    _editorSnapshot = null;
}

private static void RestoreFromSnapshot(UniversalTriggerRule rule, UniversalTriggerRuleSnapshot snapshot)
{
    // Mirror the properties carried in the snapshot back to the live rule.
    // Reference BridgeRuntimeConfiguration.TryToUniversalSnapshot to know exactly which fields the snapshot carries
    // and only restore those (e.g., Name, IsEnabled, TriggerType, CommandText, RewardCost, CooldownSeconds, MinimumBits,
    // MaximumBits, SubscriptionTier, MinimumMonths, MaximumMonths, GlobalDelaySeconds, UserDelaySeconds,
    // ExecuteRandomAction, plus the Actions list which needs a per-element restore).
    rule.Name = snapshot.Name ?? rule.Name;
    rule.IsEnabled = snapshot.IsEnabled;
    rule.CommandText = snapshot.CommandText;
    rule.RewardCost = snapshot.RewardCost;
    rule.RewardCooldownSeconds = snapshot.RewardCooldownSeconds;
    rule.MinimumBits = snapshot.MinimumBits;
    rule.MaximumBits = snapshot.MaximumBits;
    rule.GlobalDelaySeconds = snapshot.GlobalDelaySeconds;
    rule.UserDelaySeconds = snapshot.UserDelaySeconds;
    rule.ExecuteRandomAction = snapshot.ExecuteRandomAction;
    rule.Actions.Clear();
    foreach (var actSnap in snapshot.Actions)
    {
        rule.Actions.Add(new UniversalTriggerAction
        {
            OscAddress = actSnap.OscAddress,
            ValueKind = actSnap.ValueKind,
            TargetValue = actSnap.TargetValue,
            DefaultValue = actSnap.DefaultValue,
            DurationSeconds = actSnap.DurationSeconds,
            AddToQueue = actSnap.AddToQueue,
        });
    }
}
```

(Field names may differ — match `UniversalTriggerRuleSnapshot` exactly. Grep `BridgeRuntimeConfiguration.cs`.)

- [ ] **Step 4: Build**

Standard build. Expected: green.

- [ ] **Step 5: Manual smoke test 13 — editor opens with title bar + buttons (body still empty)**

Launch + open. Click Edit on a card. Verify:
- Backdrop appears (grid below dimmed).
- 480px panel slides in from the right.
- Title bar shows the trigger name.
- ✕ button closes the panel; backdrop click closes the panel.
- Footer shows Delete (red) on left, Test now + Save (accent) on right.
- Body is empty (next task fills it).

---

## Task 26: Build the editor body cards

**Files:**
- Modify: `VrcTwitchOscBridge/UniversalTriggersManagerWindow.xaml` (add 4 editor cards inside `EditorStack`)

These are 4 collapsible-style cards, but for editing they always show expanded. Field visibility per trigger type is controlled by data triggers on `SelectedTrigger.TriggerType`.

- [ ] **Step 1: Trigger Settings card**

Inside `EditorStack`:

```xml
<Border Background="{DynamicResource NestedPanelBrush}" BorderBrush="{DynamicResource InputBorderBrush}" BorderThickness="1" CornerRadius="12" Padding="12" Margin="0,0,0,14">
    <StackPanel>
        <TextBlock Text="{loc:Translate 'Universal Triggers Editor Trigger Settings'}" FontWeight="Bold" Foreground="{DynamicResource TextBrush}" FontSize="12" Margin="0,0,0,8" />
        <CheckBox Content="{loc:Translate 'Enabled'}" IsChecked="{Binding SelectedTrigger.IsEnabled, Mode=TwoWay}" Margin="0,0,0,8" />
        <UniformGrid Columns="2">
            <StackPanel Margin="0,0,8,0">
                <TextBlock Text="{loc:Translate 'Name'}" Foreground="{DynamicResource MutedBrush}" FontSize="10" />
                <TextBox Text="{Binding SelectedTrigger.Name, UpdateSourceTrigger=PropertyChanged}" />
            </StackPanel>
            <StackPanel Margin="8,0,0,0">
                <TextBlock Text="{loc:Translate 'Trigger Type'}" Foreground="{DynamicResource MutedBrush}" FontSize="10" />
                <ComboBox SelectedItem="{Binding SelectedTrigger.TriggerType, Mode=TwoWay}"
                          ItemsSource="{Binding DataContext.UniversalTriggerTypes, RelativeSource={RelativeSource AncestorType=Window}}" />
            </StackPanel>
        </UniformGrid>

        <!-- Chat-only fields -->
        <StackPanel Margin="0,8,0,0">
            <StackPanel.Style>
                <Style TargetType="StackPanel">
                    <Setter Property="Visibility" Value="Collapsed" />
                    <Style.Triggers>
                        <DataTrigger Binding="{Binding SelectedTrigger.TriggerType}" Value="ChatCommand">
                            <Setter Property="Visibility" Value="Visible" />
                        </DataTrigger>
                    </Style.Triggers>
                </Style>
            </StackPanel.Style>
            <UniformGrid Columns="2">
                <StackPanel Margin="0,0,8,0">
                    <TextBlock Text="{loc:Translate 'Command'}" Foreground="{DynamicResource MutedBrush}" FontSize="10" />
                    <TextBox Text="{Binding SelectedTrigger.CommandText, UpdateSourceTrigger=PropertyChanged}" />
                </StackPanel>
                <StackPanel Margin="8,0,0,0">
                    <TextBlock Text="{loc:Translate 'Permission'}" Foreground="{DynamicResource MutedBrush}" FontSize="10" />
                    <ComboBox SelectedItem="{Binding SelectedTrigger.ChatCommandPermission, Mode=TwoWay}" />
                </StackPanel>
            </UniformGrid>
        </StackPanel>

        <!-- Bits-only fields -->
        <StackPanel Margin="0,8,0,0">
            <StackPanel.Style>
                <Style TargetType="StackPanel">
                    <Setter Property="Visibility" Value="Collapsed" />
                    <Style.Triggers>
                        <DataTrigger Binding="{Binding SelectedTrigger.TriggerType}" Value="Bits">
                            <Setter Property="Visibility" Value="Visible" />
                        </DataTrigger>
                    </Style.Triggers>
                </Style>
            </StackPanel.Style>
            <UniformGrid Columns="2">
                <StackPanel Margin="0,0,8,0">
                    <TextBlock Text="{loc:Translate 'Min bits'}" Foreground="{DynamicResource MutedBrush}" FontSize="10" />
                    <TextBox Text="{Binding SelectedTrigger.MinimumBits, UpdateSourceTrigger=PropertyChanged}" />
                </StackPanel>
                <StackPanel Margin="8,0,0,0">
                    <TextBlock Text="{loc:Translate 'Max bits'}" Foreground="{DynamicResource MutedBrush}" FontSize="10" />
                    <TextBox Text="{Binding SelectedTrigger.MaximumBits, UpdateSourceTrigger=PropertyChanged}" />
                </StackPanel>
            </UniformGrid>
        </StackPanel>

        <!-- Subscription / GiftSubscription fields -->
        <StackPanel Margin="0,8,0,0">
            <StackPanel.Style>
                <Style TargetType="StackPanel">
                    <Setter Property="Visibility" Value="Collapsed" />
                    <Style.Triggers>
                        <DataTrigger Binding="{Binding SelectedTrigger.TriggerType}" Value="Subscription">
                            <Setter Property="Visibility" Value="Visible" />
                        </DataTrigger>
                        <DataTrigger Binding="{Binding SelectedTrigger.TriggerType}" Value="GiftSubscription">
                            <Setter Property="Visibility" Value="Visible" />
                        </DataTrigger>
                    </Style.Triggers>
                </Style>
            </StackPanel.Style>
            <UniformGrid Columns="3">
                <StackPanel Margin="0,0,4,0">
                    <TextBlock Text="{loc:Translate 'Tier'}" Foreground="{DynamicResource MutedBrush}" FontSize="10" />
                    <ComboBox SelectedItem="{Binding SelectedTrigger.SubscriptionTier, Mode=TwoWay}" />
                </StackPanel>
                <StackPanel Margin="4,0">
                    <TextBlock Text="{loc:Translate 'Min months'}" Foreground="{DynamicResource MutedBrush}" FontSize="10" />
                    <TextBox Text="{Binding SelectedTrigger.MinimumMonths, UpdateSourceTrigger=PropertyChanged}" />
                </StackPanel>
                <StackPanel Margin="4,0,0,0">
                    <TextBlock Text="{loc:Translate 'Max months'}" Foreground="{DynamicResource MutedBrush}" FontSize="10" />
                    <TextBox Text="{Binding SelectedTrigger.MaximumMonths, UpdateSourceTrigger=PropertyChanged}" />
                </StackPanel>
            </UniformGrid>
        </StackPanel>

        <!-- Global / User delay -->
        <UniformGrid Columns="3" Margin="0,8,0,0">
            <StackPanel Margin="0,0,4,0">
                <TextBlock Text="{loc:Translate 'Global Delay (s)'}" Foreground="{DynamicResource MutedBrush}" FontSize="10" />
                <TextBox Text="{Binding SelectedTrigger.GlobalDelaySeconds, UpdateSourceTrigger=PropertyChanged}" />
            </StackPanel>
            <StackPanel Margin="4,0">
                <TextBlock Text="{loc:Translate 'User Delay (s)'}" Foreground="{DynamicResource MutedBrush}" FontSize="10" />
                <TextBox Text="{Binding SelectedTrigger.UserDelaySeconds, UpdateSourceTrigger=PropertyChanged}" />
            </StackPanel>
            <CheckBox Content="{loc:Translate 'Run random action'}"
                      IsChecked="{Binding SelectedTrigger.ExecuteRandomAction, Mode=TwoWay}"
                      VerticalAlignment="Bottom" Margin="4,0,0,0" />
        </UniformGrid>
    </StackPanel>
</Border>
```

- [ ] **Step 2: Add `UniversalTriggerTypes` and `ChatCommandPermissions` and `SubscriptionTiers` to VM**

```csharp
public IReadOnlyList<UniversalTriggerType> UniversalTriggerTypes { get; } = (UniversalTriggerType[])Enum.GetValues(typeof(UniversalTriggerType));
public IReadOnlyList<ChatCommandPermission> ChatCommandPermissions { get; } = (ChatCommandPermission[])Enum.GetValues(typeof(ChatCommandPermission));
public IReadOnlyList<SubscriptionTier> SubscriptionTiers { get; } = (SubscriptionTier[])Enum.GetValues(typeof(SubscriptionTier));
```

(Grep `UniversalTriggerRule.cs` for the actual enum type names.)

- [ ] **Step 3: Twitch Reward card (visible only for ChannelPointReward)**

After the Trigger Settings border, add a second card with `Visibility` data-triggered to `UsesChannelPointReward`. Fields:
- Reward Sync Mode dropdown bound to `SelectedTrigger.RewardSyncMode`
- Reward Title TextBox bound to `SelectedTrigger.RewardTitle` (visible only when `RewardSyncMode == CreateOrManage`)
- Cost TextBox bound to `SelectedTrigger.RewardCost`
- Cooldown TextBox bound to `SelectedTrigger.RewardCooldownSeconds`
- ColorPicker (or two color textboxes) bound to `SelectedTrigger.ManagedRewardReadyColor` and `SelectedTrigger.ManagedRewardCooldownColor`
- DeleteWhenInactive CheckBox bound to `SelectedTrigger.DeleteManagedRewardWhenInactive` (visible only when CreateOrManage)
- Read-only status line text bound to a derived `RewardVisibilityStatusText` property on the VM that computes Visible / Hidden / Pending sync.

Pattern is identical to Step 1; copy the structure and adjust field bindings.

- [ ] **Step 4: Avatar Readiness card (always visible)**

After the Twitch Reward card:

```xml
<Border Background="{DynamicResource NestedPanelBrush}" BorderBrush="{DynamicResource InputBorderBrush}" BorderThickness="1" CornerRadius="12" Padding="12" Margin="0,0,0,14">
    <StackPanel>
        <TextBlock Text="{loc:Translate 'Universal Triggers Editor Avatar Readiness'}" FontWeight="Bold" Foreground="{DynamicResource TextBrush}" FontSize="12" Margin="0,0,0,8" />
        <ItemsControl ItemsSource="{Binding AvatarReadinessRows}">
            <ItemsControl.ItemTemplate>
                <DataTemplate>
                    <DockPanel LastChildFill="True" Margin="0,2">
                        <TextBlock DockPanel.Dock="Right" Foreground="{Binding StatusBrush}" FontSize="10" Text="{Binding StatusText}" />
                        <TextBlock Foreground="{DynamicResource TextBrush}" FontSize="11" Text="{Binding Address}" TextTrimming="CharacterEllipsis" />
                    </DockPanel>
                </DataTemplate>
            </ItemsControl.ItemTemplate>
        </ItemsControl>
        <TextBlock Foreground="{DynamicResource MutedBrush}" FontSize="10" FontStyle="Italic"
                   Text="{loc:Translate 'Universal Triggers Editor No Avatar Params'}"
                   Visibility="{Binding HasNoAvatarParams, Converter={StaticResource BoolToVisibilityConverter}}" />
    </StackPanel>
</Border>
```

Add a small DTO `AvatarReadinessRow` (Address, StatusText, StatusBrush) to the VM and an `AvatarReadinessRows` `ObservableCollection<AvatarReadinessRow>` property. Refresh when `SelectedTrigger` changes or when the current VRChat avatar changes (subscribe to the appropriate event on `MainWindowViewModel`).

- [ ] **Step 5: OSC Actions card**

After Avatar Readiness:

```xml
<Border Background="{DynamicResource NestedPanelBrush}" BorderBrush="{DynamicResource InputBorderBrush}" BorderThickness="1" CornerRadius="12" Padding="12">
    <StackPanel>
        <DockPanel LastChildFill="False" Margin="0,0,0,8">
            <TextBlock DockPanel.Dock="Left" Text="{loc:Translate 'Universal Triggers Editor OSC Actions'}" FontWeight="Bold" Foreground="{DynamicResource TextBrush}" FontSize="12" />
            <Button DockPanel.Dock="Right" Style="{StaticResource SecondaryButtonStyle}" Padding="6,2"
                    Content="{loc:Translate 'Universal Triggers Editor Add Action'}"
                    Command="{Binding AddActionCommand}" />
        </DockPanel>
        <ItemsControl ItemsSource="{Binding SelectedTrigger.Actions}">
            <ItemsControl.ItemTemplate>
                <DataTemplate>
                    <Border Background="{DynamicResource InputBrush}" BorderBrush="{DynamicResource InputBorderBrush}" BorderThickness="1" CornerRadius="6" Padding="6" Margin="0,4">
                        <Grid>
                            <Grid.RowDefinitions>
                                <RowDefinition Height="Auto" />
                                <RowDefinition Height="Auto" />
                                <RowDefinition Height="Auto" />
                            </Grid.RowDefinitions>
                            <DockPanel Grid.Row="0" LastChildFill="True">
                                <Button DockPanel.Dock="Right" Style="{StaticResource SecondaryButtonStyle}" Padding="4,1" Margin="6,0,0,0"
                                        Content="🗑"
                                        Command="{Binding DataContext.RemoveActionCommand, RelativeSource={RelativeSource AncestorType=Window}}"
                                        CommandParameter="{Binding}" />
                                <TextBox Text="{Binding OscAddress, UpdateSourceTrigger=PropertyChanged}" />
                            </DockPanel>
                            <UniformGrid Grid.Row="1" Columns="3" Margin="0,4,0,0">
                                <ComboBox SelectedItem="{Binding ValueKind, Mode=TwoWay}"
                                          ItemsSource="{Binding DataContext.UniversalTriggerValueKinds, RelativeSource={RelativeSource AncestorType=Window}}"
                                          Margin="0,0,4,0" />
                                <TextBox Text="{Binding TargetValue, UpdateSourceTrigger=PropertyChanged}" Margin="2,0" />
                                <TextBox Text="{Binding DefaultValue, UpdateSourceTrigger=PropertyChanged}" Margin="4,0,0,0" />
                            </UniformGrid>
                            <DockPanel Grid.Row="2" LastChildFill="True" Margin="0,4,0,0">
                                <CheckBox DockPanel.Dock="Right" Content="Queue" IsChecked="{Binding AddToQueue, Mode=TwoWay}" />
                                <StackPanel Orientation="Horizontal">
                                    <TextBlock Text="Duration (s):" Foreground="{DynamicResource MutedBrush}" FontSize="10" VerticalAlignment="Center" Margin="0,0,4,0" />
                                    <TextBox Text="{Binding DurationSeconds, UpdateSourceTrigger=PropertyChanged}" Width="60" />
                                </StackPanel>
                            </DockPanel>
                        </Grid>
                    </Border>
                </DataTemplate>
            </ItemsControl.ItemTemplate>
        </ItemsControl>
    </StackPanel>
</Border>
```

- [ ] **Step 6: Add `UniversalTriggerValueKinds`, `AddActionCommand`, `RemoveActionCommand` to VM**

```csharp
public IReadOnlyList<UniversalTriggerValueKind> UniversalTriggerValueKinds { get; } = (UniversalTriggerValueKind[])Enum.GetValues(typeof(UniversalTriggerValueKind));

[RelayCommand]
private void AddAction()
{
    SelectedTrigger?.Actions.Add(new UniversalTriggerAction
    {
        Id = Guid.NewGuid(),
        OscAddress = "/avatar/parameters/Example",
        ValueKind = UniversalTriggerValueKind.Bool,
        TargetValue = "true",
        DefaultValue = "false",
        DurationSeconds = 1.0,
        AddToQueue = false,
    });
}

[RelayCommand]
private void RemoveAction(UniversalTriggerAction? action)
{
    if (action is null || SelectedTrigger is null) return;
    SelectedTrigger.Actions.Remove(action);
}
```

- [ ] **Step 7: Build**

Standard build. Expected: green.

- [ ] **Step 8: Manual smoke test 14 — editor body works end to end**

Launch + open + click Edit on each trigger type in sequence. Verify:
- Trigger Settings card shows the right conditional fields per type.
- Twitch Reward card shows only for Reward triggers; sync mode dropdown switches between managed (shows title/cost/cooldown/colors/delete) and linked (hides those, shows just the title).
- Avatar Readiness lists params found/missing.
- OSC Actions list lets you add and remove. Each row's address/value-kind/target/default/duration/queue all save.
- Click Test now → packet fires.
- Click Save → settings persist; reward sync runs (verify no exception in debug log).
- Click backdrop → editor closes and changes revert (verify by re-opening and seeing the original values).
- Click Delete → confirm dialog → rule disappears from `Settings.UniversalTriggers` and from the grid.


---

## Task 27: Add bottom action bar with Delete All

**Files:**
- Modify: `VrcTwitchOscBridge/UniversalTriggersManagerWindow.xaml`
- Modify: `VrcTwitchOscBridge/ViewModels/UniversalTriggersManagerViewModel.cs`

- [ ] **Step 1: Add bottom action bar as Row 3 of the Phase-B body**

Inside the Phase-B body Grid as Row 3:

```xml
<Border Grid.Row="3"
        Background="{DynamicResource NestedPanelBrush}"
        BorderBrush="{DynamicResource BorderBrush}"
        BorderThickness="0,1,0,0"
        Padding="14,8">
    <DockPanel LastChildFill="False">
        <TextBlock DockPanel.Dock="Left" Foreground="{DynamicResource MutedBrush}" FontSize="11" VerticalAlignment="Center"
                   Text="{loc:Translate 'Universal Triggers Footer Hint'}" />
        <Button DockPanel.Dock="Right"
                Background="Transparent"
                Foreground="#FCA5A5"
                BorderBrush="#663033"
                Padding="10,4"
                Content="{loc:Translate 'Universal Triggers Delete All'}"
                Command="{Binding DeleteAllCommand}" />
    </DockPanel>
</Border>
```

- [ ] **Step 2: Add `DeleteAllCommand` to VM**

```csharp
[RelayCommand]
private async Task DeleteAllAsync()
{
    var count = _settings.UniversalTriggers.Count;
    if (count == 0) return;
    var ok = ThemedDialogWindow.ShowYesNo(
        LocalizationManager.Translate("Universal Triggers Delete All Confirm Title"),
        LocalizationManager.Translate("Universal Triggers Delete All Confirm Body", count));
    if (ok != true) return;

    // Sync managed rewards FIRST so Crystal Relay sends Twitch delete for any managed rewards.
    // Then clear locally.
    _settings.UniversalTriggers.Clear();
    await _mainWindowViewModel.SaveSettingsAsync().ConfigureAwait(true);
    await _mainWindowViewModel.SynchronizeManagedChannelPointRewardsAsync().ConfigureAwait(true);
    OnPropertyChanged(nameof(IsEmpty));
    RaiseSectionCountsChanged();
    RaiseCountsChanged();
}
```

Add localization keys to `en-US.extra.json`:
```json
"Universal Triggers Footer Hint": "💡 Tip: top stripe = ready (green) · needs fix (amber) · off (grey). Click a section header to fold."
```

- [ ] **Step 3: Build**

Standard build. Expected: green.

- [ ] **Step 4: Manual smoke test 15 — Delete All flow**

Launch + open with several triggers. Click Delete All. Confirm dialog appears with the count. Confirm Yes → all triggers gone, empty-state landing reappears, managed-reward sync runs (verify no error).

---

## Task 28: Add the missing localization keys to `en-US.extra.json`

**Files:**
- Modify: `VrcTwitchOscBridge/Resources/Localization/en-US.extra.json`

- [ ] **Step 1: Audit current state**

Grep `en-US.extra.json` for `Universal Triggers` to see which keys you have already added in earlier tasks (Welcome, Import Fooma, Fooma Help, New Trigger, etc.). Catalog what is missing against the full list in the spec's "Localization Keys" section.

- [ ] **Step 2: Append all missing keys**

Add the remaining keys from the spec's localization section that are not yet present. The full list per the spec:

Welcome / empty state / Fooma help / new trigger / search / filter / enable+disable / sort / collapse / sections / section suffixes / status pills / source pills / card buttons / descriptions / action summaries / editor / delete confirmations / Fooma source / type pills.

- [ ] **Step 3: Remove obsolete wizard / import preview keys**

Grep `en-US.extra.json` for:
- `"Universal Triggers Wizard *"`
- `"Universal Triggers Import After Note"`
- Any other key containing `Wizard` or `Import Preview` scoped to Universal Triggers.

Delete each.

- [ ] **Step 4: Build + smoke verify**

Standard build. Launch app, open the window. Verify every visible text element renders with English copy (no `??missing key??` markers).

---

## Task 29: Translate new keys to all 13 non-English languages

**Files:**
- Modify each `VrcTwitchOscBridge/Resources/Localization/<locale>.extra.json`

13 locales: `de-DE`, `es-ES`, `fr-FR`, `it-IT`, `ja-JP`, `ko-KR`, `pl-PL`, `pt-BR`, `ru-RU`, `sv-SE`, `th-TH`, `zh-CN`, `zh-TW`.

Per AGENTS.md "Localization Translation Quality Rules", all translations must:
- Sound natural in the target language, not stiff or machine-translated.
- Use informal/friendly register (`du` for de-DE, `tú` for es-ES, `tu` for fr-FR, informal forms for others).
- Keep brand and technical terms in English: `Bits`, `Subs`, `OSC`, `VRChat`, `Twitch`, `Crystal Relay`, `Fooma`, `VRC:`.
- Preserve all format placeholders exactly: `{0}`, `{1}`, `{2}` etc.
- Drop the obsolete wizard / import preview keys (same as en-US).

- [ ] **Step 1: de-DE**

Open `de-DE.extra.json`. Add the same set of keys as `en-US.extra.json`, with German translations. Examples:
- "Welcome to Universal Triggers" → "Willkommen bei Universal Triggers"
- "Fire VRChat OSC actions from Twitch chat commands, channel-point rewards, bits, subs, gift subs, and follows…" → "Löse VRChat OSC-Aktionen über Twitch-Chatbefehle, Kanalpunkte-Belohnungen, Bits, Subs, Gift Subs und Follows aus. Schnell starten kannst du, indem du eine Fooma Twitch Interaction Config importierst oder deinen ersten Trigger von Grund auf baust."
- Continue for every new key.

Remove obsolete wizard/import preview keys.

- [ ] **Step 2: es-ES**

Repeat with Spanish. Use `tú` form. Examples:
- "Welcome to Universal Triggers" → "Bienvenido a Universal Triggers"

- [ ] **Step 3: fr-FR**

Repeat with French. Use `tu` form.

- [ ] **Step 4: it-IT**

Repeat with Italian.

- [ ] **Step 5: ja-JP**

Repeat with Japanese. Use natural conversational tone.

- [ ] **Step 6: ko-KR**

Repeat with Korean.

- [ ] **Step 7: pl-PL**

Repeat with Polish.

- [ ] **Step 8: pt-BR**

Repeat with Brazilian Portuguese.

- [ ] **Step 9: ru-RU**

Repeat with Russian.

- [ ] **Step 10: sv-SE**

Repeat with Swedish.

- [ ] **Step 11: th-TH**

Repeat with Thai.

- [ ] **Step 12: zh-CN**

Repeat with Simplified Chinese.

- [ ] **Step 13: zh-TW**

Repeat with Traditional Chinese.

- [ ] **Step 14: Build**

Standard build. Expected: green.

---

## Task 30: Run localization audit

**Files:** (no files modified — verification only)

- [ ] **Step 1: Run the audit**

```
dotnet run --project "E:\!!!Program to work on\Proper Crystal Relay\LocalizationAudit\LocalizationAudit.csproj" --no-restore
```

Expected output: zero missing keys, zero empty values, all `{0}`/`{1}` placeholders preserved across all 14 languages.

If the audit fails: read the report, fix the specific language file(s) it flags, re-run. Common failures:
- A new English key not yet added to a non-English file.
- A placeholder like `{0}` accidentally dropped during translation.
- An untranslated English value left as a placeholder in a non-English file (allowed only for brand/technical terms — anything else must be translated).

- [ ] **Step 2: Iterate until clean**

Repeat fix + re-audit until the report is fully green.


---

## Task 31: End-to-end streamer journey test (10 scenarios)

**Files:** (no files modified — verification only)

Per spec Step 8 the acceptance test is a 10-scenario walkthrough. Use a real Twitch dev account + a VRChat account on a known avatar with at least one declared OSC parameter.

- [ ] **Scenario 1 — Phase A from empty**

Clear `Settings.UniversalTriggers` (delete from the AppData JSON or use Delete All from a previous session). Launch the app. Open the manager window via the sidebar button.

Expected: Phase A landing visible with both cards (Import Fooma + Create New).

- [ ] **Scenario 2 — Import Fooma → Phase B**

Click "Choose file…" on the Fooma card. Pick a real Fooma JSON config (any config with multiple trigger types).

Expected: triggers land in `Settings.UniversalTriggers`, the window flips to Phase B with grouped sections, counts in the title bar update, "From Fooma" filter chip shows the import count.

- [ ] **Scenario 3 — Filter / search / sort / collapse / expand / Enable All / Disable All**

Try each: click each filter chip and verify the visible cards change. Type in the search box and verify counts shrink live. Change the sort dropdown to By Name / By Status / By Type and verify card order. Click each section header to collapse/expand. Click Collapse All / Expand All toolbar buttons. Click Disable All → all toggles flip off + status pills go grey. Click Enable All → all toggles flip back on.

- [ ] **Scenario 4 — Edit a chat trigger → change name → Save**

Click Edit on a chat trigger card. Change the Name field. Click Save.

Expected: card updates with new name. Settings persist to disk (verify by closing + reopening the app).

- [ ] **Scenario 5 — Edit a managed reward trigger → change cost → Save**

Click Edit on a Reward trigger with `RewardSyncMode == CreateOrManage`. Change Cost. Click Save.

Expected: managed-reward sync runs; check Twitch Creator Dashboard → the VRC: reward cost has updated. No exceptions in the debug log.

- [ ] **Scenario 6 — Test now on each card type**

Click ⚡ Test on a card of each type (chat, reward, bits, sub, gift sub, follow). For each, watch the debug log for the OSC packet leaving `BridgeCoordinator.SendTestUniversalTriggerAsync`. Verify the trigger fires its actions (avatar param changes visible in VRChat).

- [ ] **Scenario 7 — Avatar switch → reward visibility re-syncs**

Switch your VRChat avatar to one that has a required param for some managed reward. Verify the reward becomes visible / hidden accordingly. The card's status pill updates ("Ready" or "Avatar missing") within a few seconds of the switch.

- [ ] **Scenario 8 — Delete a trigger from the editor → confirm dialog**

Open the editor on any trigger. Click 🗑 Delete. Verify the confirmation dialog. Click Yes → trigger gone from the grid. Settings persist.

- [ ] **Scenario 9 — Delete All → confirm → Phase A returns**

Click Delete All in the bottom bar. Confirm. All triggers gone, window flips to Phase A.

- [ ] **Scenario 10 — Close + reopen → state persists**

Close the manager window, close the app, restart, reopen the manager. Saved triggers reappear. Collapse state per section is restored. Sort and filter state may reset (those are per-session) — verify the behavior matches expectations.

- [ ] **Coupling regression check (Power-up + Cash Payment scale paths)**

Verify these still fire correctly despite the rebuild:
- Use the Power-up simulator (Test Mode → Simulate Power-up Bits) to trigger a Power-up event. Verify any Avatar Scaling rule with a matching bits threshold fires its scale action (height changes in VRChat).
- Simulate a Ko-fi tip (use Test Mode or the local webhook test); verify Cash Payment rules with scale actions still fire.

- [ ] **Final pass criteria**

All 10 scenarios pass without:
- UI hangs (>1 second of unresponsive UI)
- XAML binding errors in debug output
- Null reference exceptions in the log
- Lost saved triggers

---

## Task 32: Run dependency vulnerability scan

**Files:** (no files modified — verification only)

- [ ] **Step 1: Run the scan**

```
powershell -ExecutionPolicy Bypass -File "E:\!!!Program to work on\Proper Crystal Relay\Check-Crystal-Relay-Dependencies.ps1"
```

Expected: no new vulnerable packages flagged (we did not add any NuGet refs). If new vulnerabilities are flagged, they came from elsewhere — investigate but do not block this rebuild on them unless they affect the new code.

---

## Task 33: Update CHANGELOG and RELEASE-CHANGE-RECORD

**Files:**
- Modify: `E:\!!!Program to work on\Proper Crystal Relay\CHANGELOG.txt`
- Modify: `E:\!!!Program to work on\Proper Crystal Relay\RELEASE-CHANGE-RECORD.txt`

Per AGENTS.md, active development build is v3.1.9. This rebuild lands inside the v3.1.9 cycle. If a beta build is also coming, the entry goes under `v3.1.9 beta <N>`.

- [ ] **Step 1: Add an entry to CHANGELOG.txt**

Open `CHANGELOG.txt`. At the top, add (or append into the existing `v3.1.9` / `v3.1.9 beta <N>` section if one exists):

```
v3.1.9

- Rebuilt the Universal Triggers UI from scratch as a dedicated themed window opened from the main sidebar. New empty-state landing offers Import Fooma Config or Create New Trigger. Populated state shows a card grid grouped by event type (Chat / Reward / Bits / Subs & Gift Subs / Follows) with collapsible sections, status stripe (ready / needs fix / off), inline ⚡ Test and ⚙ Edit buttons, big toggle, plain-English description on every card, and filter chips (All / Active / Disabled / Needs Fix / From Fooma) plus search. Editor opens as a slide-out panel from the right with separate cards for Trigger settings, Twitch reward, Avatar readiness, and OSC actions. Global Enable All / Disable All / Delete All controls. Per-section collapse state persists across app restarts. All saved triggers from previous versions carry over unchanged.
- Added Fooma pixel-art icon (transparent background) across the UI: empty-state landing card, toolbar Import button, From Fooma filter chip, and per-card source badge so imported triggers are visually distinct from hand-built ones.
- Removed the old inline Universal Triggers tab from the main window and the half-built orphan UI files that never reached production. The runtime engine, models, Fooma importer, EventSub routing, managed-reward sync, and Reward Fire Sale integration are unchanged; only the UI layer was rebuilt.
```

(Adjust wording to match the codebase's changelog tone — keep user-facing, streamer-friendly.)

- [ ] **Step 2: Add to RELEASE-CHANGE-RECORD.txt**

Open `RELEASE-CHANGE-RECORD.txt`. Under the "Pending Release Draft" section for v3.1.9, add the same bullets in the same shape (this file is the internal scratchpad).

If `RELEASE-CHANGE-RECORD.txt` has a "Current working source version" header, verify it reads `v3.1.9` (per AGENTS.md). If not, update it.

- [ ] **Step 3: Verify the AGENTS.md project identity**

Open `AGENTS.md`. Confirm:
- `Last stable release: v3.1.8`
- `Current source version: v3.1.9`
- `Active development build: v3.1.9`
- `Active build lane: beta2` (or update to whatever the current lane should be — per AGENTS.md "Active build lane" must reflect what's about to be built next).

If anything is out of date, update.

---

## Task 34: Final build + smoke gate

**Files:** (no files modified — verification only)

- [ ] **Step 1: Clean build**

```
dotnet build "E:\!!!Program to work on\Proper Crystal Relay\VrcTwitchOscBridge\VrcTwitchOscBridge.csproj" --no-restore --nologo
```

Expected: `Build succeeded. 0 Warning(s). 0 Error(s).`

- [ ] **Step 2: Localization audit clean**

```
dotnet run --project "E:\!!!Program to work on\Proper Crystal Relay\LocalizationAudit\LocalizationAudit.csproj" --no-restore
```

Expected: zero issues.

- [ ] **Step 3: Final manual smoke**

Run `Launch-Crystal-Relay-Debug.bat`. Walk every Crystal Relay section once more (Avatar Sets, Avatar Change, Avatar Roulette, Movement Redeems, Avatar Scaling, Bits + Subs overrides, Cash Payments, Reward Fire Sale, Twitch Chatbox, About) and then the new Universal Triggers Manager Window. Confirm no XAML binding errors, no exceptions, all UI renders correctly.

- [ ] **Step 4: Report completion to user**

Report to the user:
- Spec: `docs/superpowers/specs/2026-06-10-universal-triggers-rebuild-design.md`
- Plan: `docs/superpowers/plans/2026-06-10-universal-triggers-rebuild.md`
- Backup taken: `Backups/v3.1.9/CrystalRelayTwitchOsc-v3.1.9-restore-20260610-180654.zip`
- Active build version: v3.1.9
- Active build lane: (current value from AGENTS.md)
- Build status: green
- Localization audit: green
- Manual smoke: all sections OK

Wait for user direction on whether to:
- Run `Build-Crystal-Relay-Test.ps1` for a test package
- Run `Build-Crystal-Relay-Beta.ps1` for a beta package
- Commit / push (per AGENTS.md, only on explicit user instruction)

---

## Plan complete

This plan covers every requirement in the spec at `docs/superpowers/specs/2026-06-10-universal-triggers-rebuild-design.md`:

- Removal of legacy UI ✓ (Tasks 2-10)
- Removal of orphaned new-UI files ✓ (Tasks 2-3)
- New themed secondary window ✓ (Tasks 11-15)
- Empty-state landing with Fooma cat + hammer ✓ (Tasks 1, 16)
- Populated state toolbar + filter chips + Enable/Disable All ✓ (Tasks 19-20)
- Collapsible section grids with five sections ✓ (Tasks 22-23)
- Uniform 240×168 cards with status stripe + inline buttons ✓ (Task 24)
- Slide-out editor panel ✓ (Tasks 25-26)
- Bottom action bar with Delete All ✓ (Task 27)
- AppSettings persistence for collapse state ✓ (Task 21)
- Localization (en-US + 13 languages + audit) ✓ (Tasks 28-30)
- End-to-end testing of 10 scenarios + coupling regression ✓ (Task 31)
- Dependency scan + changelog + final build ✓ (Tasks 32-34)
