# Compact Editor Typed Actions Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Fix the compact Avatar Sets rule editor so every ComboBox is readable on the current theme and the Int parameter type has the same action inputs as the full editor (mode selector, Min/Max, When/After).

**Architecture:** Add a window-scoped `ComboBoxStyle` in `AvatarSetsManagerWindow.xaml` that uses theme brushes (`TextBrush` foreground, `InputBrush` background), apply it to the three ComboBoxes the user can see (ParameterType, RewardSyncMode, FloatClampMode), and add a new Int action-inputs `StackPanel` mirroring the full editor's Int block. Float Action Mode visibility is already correct and is not changed. Bool True/False chips already exist and are not changed.

**Tech Stack:** C# / WPF / XAML on .NET 10, xUnit tests, Crystal Relay localization JSON files.

**Working tree constraint:** The repo has unrelated in-progress changes (VRChat cache, MainWindow, BridgeCoordinator, etc.). Stage only the files listed in each task. Do not run `git add .` or `git commit -a`.

**Spec:** `docs/superpowers/specs/2026-06-22-compact-editor-typed-actions-design.md` (committed `263bbee`).

---

## File Structure

**Modify**
- `VrcTwitchOscBridge/AvatarSetsManagerWindow.xaml` — add `ComboBoxStyle` resource, apply to three ComboBoxes, add Int action inputs section
- `VrcTwitchOscBridge.Tests/AvatarSetsManagerWindowXamlTests.cs` — add three new regression tests

**No new files.** No model changes. No localization file changes (all 6 needed keys already exist from the full editor). No new XAML resources except the `ComboBoxStyle` key in the existing Window resources.

---

## Task 1: Add failing regression test for ComboBoxStyle

**Files:**
- Modify: `VrcTwitchOscBridge.Tests/AvatarSetsManagerWindowXamlTests.cs` (append one new test)

- [ ] **Step 1: Add the failing test**

Append to `AvatarSetsManagerWindowXamlTests` class:

```csharp
[Fact]
public void ComboBoxStyle_ExistsAndUsesThemeBrushes()
{
    var xaml = File.ReadAllText(FindSourceFile("VrcTwitchOscBridge", "AvatarSetsManagerWindow.xaml"));
    var styleIndex = xaml.IndexOf("x:Key=\"ComboBoxStyle\"", StringComparison.Ordinal);
    Assert.True(styleIndex >= 0, "ComboBoxStyle should be defined as a resource.");
    var styleEnd = xaml.IndexOf("</Style>", styleIndex, StringComparison.Ordinal);
    var styleBlock = xaml.Substring(styleIndex, styleEnd - styleIndex);

    Assert.Contains("{DynamicResource TextBrush}", styleBlock, StringComparison.Ordinal);
    Assert.Contains("{DynamicResource InputBrush}", styleBlock, StringComparison.Ordinal);
}
```

- [ ] **Step 2: Run test to verify it fails**

Run: `dotnet test "E:\!!!Program to work on\Proper Crystal Relay\VrcTwitchOscBridge.Tests\VrcTwitchOscBridge.Tests.csproj" --no-restore --filter "FullyQualifiedName~AvatarSetsManagerWindowXamlTests.ComboBoxStyle_ExistsAndUsesThemeBrushes"`

Expected: FAIL with "ComboBoxStyle should be defined as a resource."

- [ ] **Step 3: Stop the running app if any**

Run: `Stop-Process -Name CrystalRelayTwitchOsc -Force -ErrorAction SilentlyContinue; Start-Sleep -Seconds 2`

- [ ] **Step 4: Commit the failing test**

```bash
cd "E:\!!!Program to work on\Proper Crystal Relay"
git add "VrcTwitchOscBridge.Tests/AvatarSetsManagerWindowXamlTests.cs"
git commit -m "test(compact-editor): require ComboBoxStyle resource with theme brushes"
```

---

## Task 2: Add ComboBoxStyle resource to AvatarSetsManagerWindow.xaml

**Files:**
- Modify: `VrcTwitchOscBridge/AvatarSetsManagerWindow.xaml:105-112` (immediately after the `SecondaryButtonStyle` closing tag)

- [ ] **Step 1: Insert the new style**

Find this existing block (around line 112-113):

```xml
        <Style x:Key="SecondaryButtonStyle" TargetType="Button">
            <Setter Property="Background" Value="{DynamicResource SecondaryButtonBrush}" />
            <Setter Property="Foreground" Value="{DynamicResource TextBrush}" />
            <Setter Property="BorderBrush" Value="{DynamicResource SecondaryButtonBorderBrush}" />
            <Setter Property="BorderThickness" Value="1" />
            <Setter Property="Padding" Value="10,4" />
            <Setter Property="Cursor" Value="Hand" />
        </Style>
        <Style x:Key="AccentButtonStyle" TargetType="Button">
```

Insert this new `ComboBoxStyle` between `SecondaryButtonStyle` and `AccentButtonStyle`:

```xml
        <Style x:Key="ComboBoxStyle" TargetType="ComboBox">
            <Setter Property="Foreground" Value="{DynamicResource TextBrush}" />
            <Setter Property="Background" Value="{DynamicResource InputBrush}" />
            <Setter Property="BorderBrush" Value="{DynamicResource BorderBrush}" />
            <Setter Property="Padding" Value="8,4" />
        </Style>
        <Style x:Key="AccentButtonStyle" TargetType="Button">
```

- [ ] **Step 2: Run Task 1 test to verify it passes**

Run: `dotnet test "E:\!!!Program to work on\Proper Crystal Relay\VrcTwitchOscBridge.Tests\VrcTwitchOscBridge.Tests.csproj" --no-restore --filter "FullyQualifiedName~AvatarSetsManagerWindowXamlTests.ComboBoxStyle_ExistsAndUsesThemeBrushes"`

Expected: PASS

- [ ] **Step 3: Commit**

```bash
cd "E:\!!!Program to work on\Proper Crystal Relay"
git add "VrcTwitchOscBridge/AvatarSetsManagerWindow.xaml"
git commit -m "feat(compact-editor): add ComboBoxStyle with theme brushes"
```

---

## Task 3: Apply ComboBoxStyle to ParameterType, RewardSyncMode, FloatClampMode ComboBoxes

**Files:**
- Modify: `VrcTwitchOscBridge/AvatarSetsManagerWindow.xaml` at three locations

- [ ] **Step 1: Apply to ParameterType ComboBox (around line 1250)**

Find this existing block:

```xml
                                          <ComboBox ItemsSource="{Binding DataContext.ParameterTypes, RelativeSource={RelativeSource AncestorType=Window}, FallbackValue={x:Null}}"
                                                    SelectedItem="{Binding ParameterType, UpdateSourceTrigger=PropertyChanged}"
                                                    Margin="0,0,0,8" />
```

Replace with:

```xml
                                          <ComboBox ItemsSource="{Binding DataContext.ParameterTypes, RelativeSource={RelativeSource AncestorType=Window}, FallbackValue={x:Null}}"
                                                    SelectedItem="{Binding ParameterType, UpdateSourceTrigger=PropertyChanged}"
                                                    Style="{StaticResource ComboBoxStyle}"
                                                    Margin="0,0,0,8" />
```

- [ ] **Step 2: Apply to RewardSyncMode ComboBox (around line 992)**

Find this existing block:

```xml
                                         <ComboBox SelectedValue="{Binding RewardSyncMode, UpdateSourceTrigger=PropertyChanged}"
                                                   SelectedValuePath="Tag"
                                                   Margin="0,0,0,8">
```

Replace with:

```xml
                                         <ComboBox SelectedValue="{Binding RewardSyncMode, UpdateSourceTrigger=PropertyChanged}"
                                                   SelectedValuePath="Tag"
                                                   Style="{StaticResource ComboBoxStyle}"
                                                   Margin="0,0,0,8">
```

- [ ] **Step 3: Apply to FloatClampMode ComboBox (around line 1417)**

Find this existing block:

```xml
                                                   <Grid Margin="0,6,0,0" Visibility="{Binding UsesFloatClampMode, Converter={StaticResource BoolToVisibilityConverter}}">
                                                       <TextBlock Text="{loc:Translate 'FloatActionModeClampLabel'}" />
                                                       <ComboBox SelectedValue="{Binding FloatClampMode}" SelectedValuePath="Tag">
```

Replace with:

```xml
                                                   <Grid Margin="0,6,0,0" Visibility="{Binding UsesFloatClampMode, Converter={StaticResource BoolToVisibilityConverter}}">
                                                       <TextBlock Text="{loc:Translate 'FloatActionModeClampLabel'}" />
                                                       <ComboBox SelectedValue="{Binding FloatClampMode}" SelectedValuePath="Tag"
                                                                 Style="{StaticResource ComboBoxStyle}">
```

- [ ] **Step 4: Build to confirm no XAML errors**

Run: `dotnet build "E:\!!!Program to work on\Proper Crystal Relay\VrcTwitchOscBridge\VrcTwitchOscBridge.csproj" --no-restore`

Expected: Build succeeded, 0 errors (warnings about pre-existing duplicate-include are OK).

- [ ] **Step 5: Run full test suite to confirm no regression**

Run: `dotnet test "E:\!!!Program to work on\Proper Crystal Relay\VrcTwitchOscBridge.Tests\VrcTwitchOscBridge.Tests.csproj" --no-restore`

Expected: All previously-passing tests still pass (246+ passed, 7 skipped, 0 failed).

- [ ] **Step 6: Commit**

```bash
cd "E:\!!!Program to work on\Proper Crystal Relay"
git add "VrcTwitchOscBridge/AvatarSetsManagerWindow.xaml"
git commit -m "feat(compact-editor): apply ComboBoxStyle to ParameterType, RewardSyncMode, FloatClampMode"
```

---

## Task 4: Add failing regression test for Int mode selector in compact editor

**Files:**
- Modify: `VrcTwitchOscBridge.Tests/AvatarSetsManagerWindowXamlTests.cs` (append one new test)

- [ ] **Step 1: Add the failing test**

Append to `AvatarSetsManagerWindowXamlTests` class:

```csharp
[Fact]
public void CompactEditor_HasIntModeSelectorBoundToIntZeroDurationMode()
{
    var xaml = File.ReadAllText(FindSourceFile("VrcTwitchOscBridge", "AvatarSetsManagerWindow.xaml"));
    var parameterTypeIndex = xaml.IndexOf("SelectedItem=\"{Binding ParameterType, UpdateSourceTrigger=PropertyChanged}\"", StringComparison.Ordinal);
    var intModeSelector = xaml.IndexOf("SelectedItem=\"{Binding IntZeroDurationMode, UpdateSourceTrigger=PropertyChanged}\"", StringComparison.Ordinal);
    var intModeDataSource = xaml.IndexOf("DataContext.IntZeroDurationModes", StringComparison.Ordinal);

    Assert.True(parameterTypeIndex >= 0, "ParameterType selector should exist in the compact editor.");
    Assert.True(intModeSelector > parameterTypeIndex, "Int mode selector should appear after the Parameter Type selector.");
    Assert.True(intModeDataSource >= 0, "Int mode selector should bind to DataContext.IntZeroDurationModes.");
}
```

- [ ] **Step 2: Run test to verify it fails**

Run: `dotnet test "E:\!!!Program to work on\Proper Crystal Relay\VrcTwitchOscBridge.Tests\VrcTwitchOscBridge.Tests.csproj" --no-restore --filter "FullyQualifiedName~AvatarSetsManagerWindowXamlTests.CompactEditor_HasIntModeSelectorBoundToIntZeroDurationMode"`

Expected: FAIL with "Int mode selector should appear after the Parameter Type selector."

- [ ] **Step 3: Commit the failing test**

```bash
cd "E:\!!!Program to work on\Proper Crystal Relay"
git add "VrcTwitchOscBridge.Tests/AvatarSetsManagerWindowXamlTests.cs"
git commit -m "test(compact-editor): require Int mode selector bound to IntZeroDurationMode"
```

---

## Task 5: Add Int action inputs section to compact editor

**Files:**
- Modify: `VrcTwitchOscBridge/AvatarSetsManagerWindow.xaml` — insert new section immediately after the existing Parameter Value TextBox (around line 1243) and before the Parameter Type label (line 1245)

- [ ] **Step 1: Locate the insertion point**

The current code between the Bool chips and the Parameter Type label is:

```xml
                                            <TextBox Text="{Binding ParameterValue, UpdateSourceTrigger=PropertyChanged}"
                                                     Margin="0,0,0,8"
                                                     Visibility="{Binding UsesBoolParameter, Converter={StaticResource InverseBoolToVisibilityConverter}}" />

                                            <TextBlock Text="Parameter Type"
                                                     Foreground="{DynamicResource TextBrush}"
                                                     FontWeight="SemiBold"
                                                     FontSize="11"
                                                     Margin="0,12,0,6" />
```

- [ ] **Step 2: Insert the new Int action inputs section between the TextBox and the TextBlock**

Insert this block immediately after the `Visibility="{Binding UsesBoolParameter, Converter={StaticResource InverseBoolToVisibilityConverter}}" />` line and before the `<TextBlock Text="Parameter Type"` line:

```xml
                                            <!-- Int action inputs (mode selector + Min/Max or When/After) -->
                                            <StackPanel Margin="0,0,0,8"
                                                        Visibility="{Binding UsesIntParameter, Converter={StaticResource BoolToVisibilityConverter}}">
                                                <StackPanel Visibility="{Binding UsesIntInstantModeOptions, Converter={StaticResource BoolToVisibilityConverter}}">
                                                    <TextBlock Text="{loc:Translate 'Instant Int Action'}"
                                                               Foreground="{DynamicResource TextBrush}"
                                                               FontWeight="SemiBold"
                                                               FontSize="11"
                                                               Margin="0,0,0,4" />
                                                    <ComboBox ItemsSource="{Binding DataContext.IntZeroDurationModes, RelativeSource={RelativeSource AncestorType=Window}, FallbackValue={x:Null}}"
                                                              SelectedItem="{Binding IntZeroDurationMode, UpdateSourceTrigger=PropertyChanged}"
                                                              Style="{StaticResource ComboBoxStyle}"
                                                              Margin="0,0,0,8" />
                                                    <StackPanel Margin="0,0,0,8"
                                                                Visibility="{Binding UsesIntFixedInstantValue, Converter={StaticResource BoolToVisibilityConverter}}">
                                                        <TextBlock Text="{loc:Translate 'Send This Number'}"
                                                                   Foreground="{DynamicResource TextBrush}"
                                                                   FontWeight="SemiBold"
                                                                   FontSize="11"
                                                                   Margin="0,0,0,4" />
                                                        <TextBox Text="{Binding ParameterValue, UpdateSourceTrigger=PropertyChanged}" />
                                                    </StackPanel>
                                                    <UniformGrid Columns="2" Margin="0,0,0,8"
                                                                 Visibility="{Binding UsesIntRangeInputs, Converter={StaticResource BoolToVisibilityConverter}}">
                                                        <StackPanel Margin="0,0,4,0">
                                                            <TextBlock Text="{loc:Translate 'Minimum Number'}"
                                                                       Foreground="{DynamicResource TextBrush}"
                                                                       FontWeight="SemiBold"
                                                                       FontSize="11"
                                                                       Margin="0,0,0,4" />
                                                            <TextBox Text="{Binding RangeMinimum, UpdateSourceTrigger=PropertyChanged}" />
                                                        </StackPanel>
                                                        <StackPanel Margin="4,0,0,0">
                                                            <TextBlock Text="{loc:Translate 'Maximum Number'}"
                                                                       Foreground="{DynamicResource TextBrush}"
                                                                       FontWeight="SemiBold"
                                                                       FontSize="11"
                                                                       Margin="0,0,0,4" />
                                                            <TextBox Text="{Binding RangeMaximum, UpdateSourceTrigger=PropertyChanged}" />
                                                        </StackPanel>
                                                    </UniformGrid>
                                                </StackPanel>
                                                <StackPanel Visibility="{Binding UsesIntTimedValues, Converter={StaticResource BoolToVisibilityConverter}}">
                                                    <UniformGrid Columns="2" Margin="0,0,0,8">
                                                        <StackPanel Margin="0,0,4,0">
                                                            <TextBlock Text="{loc:Translate 'When Triggered, Set To'}"
                                                                       Foreground="{DynamicResource TextBrush}"
                                                                       FontWeight="SemiBold"
                                                                       FontSize="11"
                                                                       Margin="0,0,0,4" />
                                                            <TextBox Text="{Binding ParameterValue, UpdateSourceTrigger=PropertyChanged}" />
                                                        </StackPanel>
                                                        <StackPanel Margin="4,0,0,0">
                                                            <TextBlock Text="{loc:Translate 'After Active Time Ends, Set Back To'}"
                                                                       Foreground="{DynamicResource TextBrush}"
                                                                       FontWeight="SemiBold"
                                                                       FontSize="11"
                                                                       Margin="0,0,0,4" />
                                                            <TextBox Text="{Binding ResetValue, UpdateSourceTrigger=PropertyChanged}" />
                                                        </StackPanel>
                                                    </UniformGrid>
                                                </StackPanel>
                                            </StackPanel>

```

- [ ] **Step 3: Run Task 4 test to verify it passes**

Run: `dotnet test "E:\!!!Program to work on\Proper Crystal Relay\VrcTwitchOscBridge.Tests\VrcTwitchOscBridge.Tests.csproj" --no-restore --filter "FullyQualifiedName~AvatarSetsManagerWindowXamlTests.CompactEditor_HasIntModeSelectorBoundToIntZeroDurationMode"`

Expected: PASS

- [ ] **Step 4: Build to confirm no XAML errors**

Run: `dotnet build "E:\!!!Program to work on\Proper Crystal Relay\VrcTwitchOscBridge\VrcTwitchOscBridge.csproj" --no-restore`

Expected: Build succeeded, 0 errors.

- [ ] **Step 5: Commit**

```bash
cd "E:\!!!Program to work on\Proper Crystal Relay"
git add "VrcTwitchOscBridge/AvatarSetsManagerWindow.xaml"
git commit -m "feat(compact-editor): add Int action inputs (mode selector, Min/Max, When/After)"
```

---

## Task 6: Add failing regression test for Min/Max and When/After bindings

**Files:**
- Modify: `VrcTwitchOscBridge.Tests/AvatarSetsManagerWindowXamlTests.cs` (append one new test)

- [ ] **Step 1: Add the failing test (this will pass after Task 5)**

Append to `AvatarSetsManagerWindowXamlTests` class:

```csharp
[Fact]
public void CompactEditor_IntInputs_BindToRangeAndWhenAfter()
{
    var xaml = File.ReadAllText(FindSourceFile("VrcTwitchOscBridge", "AvatarSetsManagerWindow.xaml"));
    var intModeIndex = xaml.IndexOf("SelectedItem=\"{Binding IntZeroDurationMode, UpdateSourceTrigger=PropertyChanged}\"", StringComparison.Ordinal);
    Assert.True(intModeIndex >= 0, "Int mode selector must exist before Min/Max/When/After inputs.");

    var minBinding = xaml.IndexOf("Text=\"{Binding RangeMinimum, UpdateSourceTrigger=PropertyChanged}\"", StringComparison.Ordinal);
    var maxBinding = xaml.IndexOf("Text=\"{Binding RangeMaximum, UpdateSourceTrigger=PropertyChanged}\"", StringComparison.Ordinal);
    var whenBinding = xaml.IndexOf("Text=\"{Binding ResetValue, UpdateSourceTrigger=PropertyChanged}\"", StringComparison.Ordinal);

    Assert.True(minBinding > intModeIndex, "RangeMinimum text box should be in the Int section, after the Int mode selector.");
    Assert.True(maxBinding > intModeIndex, "RangeMaximum text box should be in the Int section, after the Int mode selector.");
    Assert.True(whenBinding > intModeIndex, "ResetValue (After Active Time) text box should be in the Int section, after the Int mode selector.");
}
```

- [ ] **Step 2: Run test to verify it passes (Task 5 already wired these)**

Run: `dotnet test "E:\!!!Program to work on\Proper Crystal Relay\VrcTwitchOscBridge.Tests\VrcTwitchOscBridge.Tests.csproj" --no-restore --filter "FullyQualifiedName~AvatarSetsManagerWindowXamlTests.CompactEditor_IntInputs_BindToRangeAndWhenAfter"`

Expected: PASS

- [ ] **Step 3: Commit the regression test**

```bash
cd "E:\!!!Program to work on\Proper Crystal Relay"
git add "VrcTwitchOscBridge.Tests/AvatarSetsManagerWindowXamlTests.cs"
git commit -m "test(compact-editor): require Min/Max and When/After Int input bindings"
```

---

## Task 7: Final full verification

**Files:** none modified

- [ ] **Step 1: Run full test suite**

Run: `dotnet test "E:\!!!Program to work on\Proper Crystal Relay\VrcTwitchOscBridge.Tests\VrcTwitchOscBridge.Tests.csproj" --no-restore`

Expected: All previously-passing tests still pass. The number should be 249 passed (246 baseline + 3 new), 7 skipped, 0 failed.

- [ ] **Step 2: Build app project**

Run: `dotnet build "E:\!!!Program to work on\Proper Crystal Relay\VrcTwitchOscBridge\VrcTwitchOscBridge.csproj" --no-restore`

Expected: Build succeeded, 0 errors. Pre-existing warnings about `RouletteRewardSettingsControl.xaml.cs` and `AvatarSwapMigrationService.cs` are acceptable.

- [ ] **Step 3: Confirm git status is clean for the plan's files only**

Run: `git status --short`

Expected: Only the AvatarSetsManagerWindow.xaml and AvatarSetsManagerWindowXamlTests.cs files appear (the unrelated VRChat/MainWindow changes are still there from the in-progress work but were never touched by this plan). The commits from Tasks 1-6 should be listed in `git log --oneline -7`.

- [ ] **Step 4: Report completion to the user with a short reminder**

Report:
- Last stable release: `3.1.8`; active build: `3.1.9 beta4` (from `AGENTS.md` Project Identity).
- Build: succeeded, 0 errors.
- Tests: 249 passed, 7 skipped, 0 failed.
- Localization: no new keys (all 6 reused from the full editor).
- The unrelated working-tree changes (VRChat cache, MainWindow, BridgeCoordinator, etc.) were not touched.

---

## Self-Review

**1. Spec coverage:**
- Section 1 (Global ComboBox theme) — Tasks 1, 2, 3.
- Section 2 (Int action inputs) — Tasks 4, 5, 6.
- Section 3 (Localized labels) — no tasks needed; all 6 keys already exist in the base locale files (verified before writing this plan).
- Section 4 (No change to Float Action Mode visibility) — explicitly not changed.
- Section 5 (No change to Bool True/False chips) — explicitly not changed.
- Testing section (3 new XAML-source tests) — Tasks 1, 4, 6.
- All spec requirements are covered.

**2. Placeholder scan:** No TBDs, TODOs, "implement later", or vague validation steps. Every code step shows the exact XAML. Every commit step shows the exact commands.

**3. Type consistency:** `TriggerRule.IntZeroDurationMode`, `RangeMinimum`, `RangeMaximum`, `ParameterValue`, `ResetValue`, and the `UsesXxx` properties (`UsesIntParameter`, `UsesIntInstantModeOptions`, `UsesIntFixedInstantValue`, `UsesIntRangeInputs`, `UsesIntTimedValues`) all match the model definitions in `VrcTwitchOscBridge/Models/TriggerRule.cs`. The `DataContext.IntZeroDurationModes` collection matches `MainWindowViewModel.IntZeroDurationModes`. The localization keys (`Instant Int Action`, `Send This Number`, `Minimum Number`, `Maximum Number`, `When Triggered, Set To`, `After Active Time Ends, Set Back To`) all match the existing keys in `VrcTwitchOscBridge/Resources/Localization/en-US.json`.
