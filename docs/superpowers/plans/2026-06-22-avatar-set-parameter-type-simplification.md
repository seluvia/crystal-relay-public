# Avatar Set Parameter Type Simplification Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Remove the redundant visible `Value Type` selector from the Avatar Set rule editor while keeping search, type filtering, and picker-driven type assignment intact.

**Architecture:** This is a targeted XAML cleanup. `AvatarSetsManagerWindow.xaml` already sets `TriggerRule.ParameterType` from the picked `VrChatOscParameterSummary` in `OnParameterItemClicked`, so implementation only removes the manual selector and updates XAML regression tests to guard the simplified flow.

**Tech Stack:** C# / .NET 10, WPF XAML, xUnit string-based XAML regression tests.

---

## File Structure

| File | Responsibility |
|---|---|
| `VrcTwitchOscBridge.Tests/AvatarSetsManagerWindowXamlTests.cs` | XAML regression tests for the Avatar Set manager editor. Update the tests that currently require the `Value Type` selector so they instead require the simplified picker/filter behavior. |
| `VrcTwitchOscBridge/AvatarSetsManagerWindow.xaml` | Avatar Set manager UI. Remove only the visible `Value Type` label and `ParameterType` `ComboBox` from the selected rule editor. |
| `VrcTwitchOscBridge/AvatarSetsManagerWindow.xaml.cs` | Existing code-behind. No implementation change expected; tests should preserve that `OnParameterItemClicked` assigns both `ParameterName` and `ParameterType`. |
| `docs/superpowers/specs/2026-06-22-avatar-set-parameter-type-simplification-design.md` | Approved design. No implementation change expected unless the implementation diverges. |

No model, persistence, localization, or OSC dispatch changes are required.

---

### Task 1: Flip Avatar Set XAML Tests To The Simplified Picker Flow

**Files:**
- Modify: `VrcTwitchOscBridge.Tests/AvatarSetsManagerWindowXamlTests.cs`

- [ ] **Step 1: Replace the old selector-required test with a removal test**

In `VrcTwitchOscBridge.Tests/AvatarSetsManagerWindowXamlTests.cs`, replace the full method `RuleEditor_ExposesSelectedRuleParameterTypeSelectorBeforeParameterPicker` with this method:

```csharp
[Fact]
public void RuleEditor_HidesValueTypeSelectorButKeepsParameterFilterAndPicker()
{
    var xaml = File.ReadAllText(FindSourceFile("VrcTwitchOscBridge", "AvatarSetsManagerWindow.xaml"));
    var parameterListFilterIndex = xaml.IndexOf("Parameter List Filter", StringComparison.Ordinal);
    var pickerLabelIndex = xaml.IndexOf("Text=\"Search &amp; Pick Parameter\"", StringComparison.Ordinal);
    var filteredParametersIndex = xaml.IndexOf("ItemsSource=\"{Binding DataContext.FilteredParameters, RelativeSource={RelativeSource AncestorType=Window}}\"", StringComparison.Ordinal);

    Assert.DoesNotContain("{loc:Translate 'Value Type'}", xaml, StringComparison.Ordinal);
    Assert.DoesNotContain("DataContext.ParameterTypes", xaml, StringComparison.Ordinal);
    Assert.DoesNotContain("SelectedItem=\"{Binding ParameterType, UpdateSourceTrigger=PropertyChanged}\"", xaml, StringComparison.Ordinal);
    Assert.True(parameterListFilterIndex >= 0, "The parameter list filter should remain visible for narrowing the picker list.");
    Assert.True(pickerLabelIndex >= 0, "The Search & Pick Parameter label should remain visible.");
    Assert.True(filteredParametersIndex > pickerLabelIndex, "The picker should continue binding to FilteredParameters.");
}
```

- [ ] **Step 2: Update the Int mode selector test so it no longer depends on the removed selector**

In the same file, replace the full method `CompactEditor_HasIntModeSelectorBoundToIntZeroDurationMode` with this method:

```csharp
[Fact]
public void CompactEditor_HasIntModeSelectorBeforeParameterListFilter()
{
    var xaml = File.ReadAllText(FindSourceFile("VrcTwitchOscBridge", "AvatarSetsManagerWindow.xaml"));
    var intModeSelector = xaml.IndexOf("SelectedItem=\"{Binding IntZeroDurationMode, UpdateSourceTrigger=PropertyChanged}\"", StringComparison.Ordinal);
    var intModeDataSource = xaml.IndexOf("DataContext.IntZeroDurationModes", StringComparison.Ordinal);
    var parameterListFilterIndex = xaml.IndexOf("Parameter List Filter", StringComparison.Ordinal);

    Assert.True(intModeSelector >= 0, "Int mode selector should remain available for Int parameters.");
    Assert.True(intModeDataSource >= 0, "Int mode selector should bind to DataContext.IntZeroDurationModes.");
    Assert.True(parameterListFilterIndex > intModeSelector, "The parameter list filter should appear after the Int action controls.");
}
```

- [ ] **Step 3: Replace the old distinct-label test with a picker-driven type assignment guard**

In the same file, replace the full method `RuleEditor_ValueTypeLabel_IsLocalizedAndDistinctFromFilter` with this method:

```csharp
[Fact]
public void RuleEditor_ParameterPickerKeepsCodeBehindTypeAssignment()
{
    var codeBehind = File.ReadAllText(FindSourceFile("VrcTwitchOscBridge", "AvatarSetsManagerWindow.xaml.cs"));
    var handlerIndex = codeBehind.IndexOf("private void OnParameterItemClicked", StringComparison.Ordinal);
    Assert.True(handlerIndex >= 0, "OnParameterItemClicked should exist.");

    var handlerEnd = codeBehind.IndexOf("private void OnWardrobeParameterItemClicked", handlerIndex, StringComparison.Ordinal);
    Assert.True(handlerEnd > handlerIndex, "OnParameterItemClicked should be bounded by the wardrobe picker handler.");
    var handlerBlock = codeBehind.Substring(handlerIndex, handlerEnd - handlerIndex);

    Assert.Contains("rule.ParameterName = p.Name;", handlerBlock, StringComparison.Ordinal);
    Assert.Contains("rule.ParameterType = p.ParameterType;", handlerBlock, StringComparison.Ordinal);
    Assert.Contains("Vm.ParameterNameFilter = string.Empty;", handlerBlock, StringComparison.Ordinal);
}
```

- [ ] **Step 4: Run the targeted XAML tests and verify the new removal test fails**

Run from the repo root:

```powershell
dotnet test "E:\!!!Program to work on\Proper Crystal Relay\VrcTwitchOscBridge.Tests\VrcTwitchOscBridge.Tests.csproj" --no-restore --filter "FullyQualifiedName~AvatarSetsManagerWindowXamlTests"
```

Expected result before implementation:

```text
Failed: 1
```

The expected failing test is `RuleEditor_HidesValueTypeSelectorButKeepsParameterFilterAndPicker` because `AvatarSetsManagerWindow.xaml` still contains `{loc:Translate 'Value Type'}`, `DataContext.ParameterTypes`, and `SelectedItem="{Binding ParameterType, UpdateSourceTrigger=PropertyChanged}"`.

- [ ] **Step 5: Commit the failing test update only if the project workflow wants red commits**

Crystal Relay normally keeps commits buildable, so do not commit this failing-test-only state. Continue to Task 2 and commit after the implementation passes.

---

### Task 2: Remove The Visible Value Type Selector From The Avatar Set Rule Editor

**Files:**
- Modify: `VrcTwitchOscBridge/AvatarSetsManagerWindow.xaml`

- [ ] **Step 1: Remove the redundant label and ComboBox block**

In `VrcTwitchOscBridge/AvatarSetsManagerWindow.xaml`, find this block immediately after the bool `Parameter Value` chip panel and before the comment `<!-- Int action inputs (mode selector + Min/Max or When/After) -->`:

```xml
                                            <TextBlock Text="{loc:Translate 'Value Type'}"
                                                       Foreground="{DynamicResource TextBrush}"
                                                       FontWeight="SemiBold"
                                                       FontSize="11"
                                                       Margin="0,12,0,6" />
                                          <ComboBox ItemsSource="{Binding DataContext.ParameterTypes, RelativeSource={RelativeSource AncestorType=Window}, FallbackValue={x:Null}}"
                                                    SelectedItem="{Binding ParameterType, UpdateSourceTrigger=PropertyChanged}"
                                                    Style="{StaticResource ComboBoxStyle}"
                                                     Margin="0,0,0,8" />
```

Delete that block completely.

- [ ] **Step 2: Preserve spacing for the Int section**

In the same file, update the opening Int section `StackPanel` immediately below the removed block from:

```xml
                                            <StackPanel Margin="0,0,0,8"
                                                        Visibility="{Binding UsesIntParameter, Converter={StaticResource BoolToVisibilityConverter}}">
```

to:

```xml
                                            <StackPanel Margin="0,8,0,8"
                                                        Visibility="{Binding UsesIntParameter, Converter={StaticResource BoolToVisibilityConverter}}">
```

This keeps the section readable after removing the selector's bottom margin.

- [ ] **Step 3: Do not remove `AvatarSetsManagerViewModel.ParameterTypes`**

Leave this property in `VrcTwitchOscBridge/ViewModels/AvatarSetsManagerViewModel.cs` unchanged:

```csharp
public IReadOnlyList<Models.OscParameterType> ParameterTypes { get; } =
    [Models.OscParameterType.Bool, Models.OscParameterType.Int, Models.OscParameterType.Float];
```

Reason: removing the visible selector is the requested UI simplification. Removing the public property is not needed for behavior and can be handled later if a compiler or analyzer flags it.

- [ ] **Step 4: Run targeted XAML tests and verify they pass**

Run from the repo root:

```powershell
dotnet test "E:\!!!Program to work on\Proper Crystal Relay\VrcTwitchOscBridge.Tests\VrcTwitchOscBridge.Tests.csproj" --no-restore --filter "FullyQualifiedName~AvatarSetsManagerWindowXamlTests"
```

Expected result after implementation:

```text
Failed: 0
```

- [ ] **Step 5: Inspect the targeted diff**

Run from the repo root:

```powershell
git diff -- VrcTwitchOscBridge.Tests/AvatarSetsManagerWindowXamlTests.cs VrcTwitchOscBridge/AvatarSetsManagerWindow.xaml
```

Expected result:

- The tests no longer require the visible `Value Type` selector.
- The tests still require `Parameter List Filter`, `Search & Pick Parameter`, and `FilteredParameters`.
- `AvatarSetsManagerWindow.xaml` removes only the `Value Type` label and `ParameterType` ComboBox block, plus the Int section top margin adjustment.

---

### Task 3: Full Verification And Commit

**Files:**
- Verify: `VrcTwitchOscBridge.Tests/AvatarSetsManagerWindowXamlTests.cs`
- Verify: `VrcTwitchOscBridge/AvatarSetsManagerWindow.xaml`
- Commit: all files changed by this implementation plan

- [ ] **Step 1: Run the main app build**

Run from the repo root:

```powershell
dotnet build "E:\!!!Program to work on\Proper Crystal Relay\VrcTwitchOscBridge\VrcTwitchOscBridge.csproj" --no-restore
```

Expected result:

```text
Build succeeded.
    0 Error(s)
```

- [ ] **Step 2: Run the full app test project**

Run from the repo root:

```powershell
dotnet test "E:\!!!Program to work on\Proper Crystal Relay\VrcTwitchOscBridge.Tests\VrcTwitchOscBridge.Tests.csproj" --no-restore
```

Expected result:

```text
Failed:     0
```

Skipped tests may remain skipped if they are already skipped by the existing test suite.

- [ ] **Step 3: Confirm only intended files changed**

Run from the repo root:

```powershell
git status --short
```

Expected files for the implementation itself:

```text
 M VrcTwitchOscBridge.Tests/AvatarSetsManagerWindowXamlTests.cs
 M VrcTwitchOscBridge/AvatarSetsManagerWindow.xaml
 untracked docs/superpowers/plans/2026-06-22-avatar-set-parameter-type-simplification.md
```

If the plan file was committed before execution, it will not appear in this list.

- [ ] **Step 4: Run whitespace check**

Run from the repo root:

```powershell
git diff --check
```

Expected result: no whitespace errors. Git may still print line-ending warnings on Windows; those are not whitespace failures.

- [ ] **Step 5: Commit the implementation**

Run from the repo root:

```powershell
git add VrcTwitchOscBridge.Tests/AvatarSetsManagerWindowXamlTests.cs VrcTwitchOscBridge/AvatarSetsManagerWindow.xaml docs/superpowers/plans/2026-06-22-avatar-set-parameter-type-simplification.md
git commit -m "fix(avatar-sets): remove redundant value type selector"
```

Expected result:

```text
[main <hash>] fix(avatar-sets): remove redundant value type selector
```

- [ ] **Step 6: Verify clean worktree after commit**

Run from the repo root:

```powershell
git status --short
```

Expected result: no output.
