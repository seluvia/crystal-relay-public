# Float Reward Action Modes Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Add ten float action modes (Set, Random, Add, Subtract, AddSubtract, Multiply, Toggle, Cycle, Glitchy, Pulse) to the `TriggerRule` model and the Avatar Sets / Avatar Swap rule editors, with dispatch wired through `BridgeCoordinator` and persistence in `crystal-relay.rules.json`. Backward-compatible.

**Architecture:** Mirror the existing `IntZeroDurationMode` (`Fixed / Random / Cycle`) pattern. New `FloatActionMode` enum + per-mode fields + computed `UsesXxx` properties live directly on `TriggerRule`. Dispatch in `BridgeCoordinator` branches on `rule.ParameterType == Float` and calls a new `ResolveFloatActionAsync` that picks a per-mode value-calculator. Glitchy and Pulse get their own short-lived sessions. UI is a new "Float Action Mode" section that appears only when `ParameterType == Float`, with the same row-of-buttons pattern used by the existing "Parameter Type" filter.

**Tech Stack:** C# / .NET 10 / WPF + XAML / xUnit. `System.Text.Json` for persistence (already used by `SettingsStore`). No new dependencies.

**Spec:** `docs/superpowers/specs/2026-06-21-float-reward-action-modes-design.md`

**Working tree note:** the repo currently has uncommitted changes from another in-progress feature. Every commit in this plan only stages files owned by this plan (`VrcTwitchOscBridge/Models/`, `Services/BridgeCoordinator.cs`, `Services/SettingsStore.cs`, the two rule-editor XAML/code-behind files, localization, and the new test files). Do not stage unrelated modifications.

---

## File structure

**New files:**
- `VrcTwitchOscBridge/Models/FloatActionMode.cs` — the action-mode enum
- `VrcTwitchOscBridge/Models/FloatClampMode.cs` — the clamp-mode enum for relative operations
- `VrcTwitchOscBridge/Services/FloatActionDispatch.cs` — pure value-calculator helpers (one method per mode)
- `VrcTwitchOscBridge.Tests/FloatActionModeTests.cs`
- `VrcTwitchOscBridge.Tests/FloatClampModeTests.cs`
- `VrcTwitchOscBridge.Tests/TriggerRuleFloatModeFieldsTests.cs`
- `VrcTwitchOscBridge.Tests/FloatActionDispatchTests.cs`
- `VrcTwitchOscBridge.Tests/TriggerRuleFloatModePersistenceTests.cs`

**Modified files:**
- `VrcTwitchOscBridge/Models/TriggerRule.cs` — new fields, setters, `UsesXxx` properties
- `VrcTwitchOscBridge/Services/BridgeCoordinator.cs` — new dispatch branch, glitchy session, pulse scheduler
- `VrcTwitchOscBridge/Services/SettingsStore.cs` — `PersistedTriggerRule` fields, `ToPersistedRule`, `ToRule` migration
- `VrcTwitchOscBridge/AvatarSetsManagerWindow.xaml` and `.xaml.cs` — new mode section
- `VrcTwitchOscBridge/UserControls/AvatarSwapRuleEditorControl.xaml` and `.xaml.cs` — new mode section
- `VrcTwitchOscBridge/Resources/Localization/en-US.json` plus 15 locale files

The new `Services/FloatActionDispatch.cs` keeps the per-mode math in one focused file so the dispatch tests don't need to touch the 16k-line `BridgeCoordinator`.

---

## Task 1: Add `FloatActionMode` enum

**Files:**
- Create: `VrcTwitchOscBridge/Models/FloatActionMode.cs`
- Test: `VrcTwitchOscBridge.Tests/FloatActionModeTests.cs`

- [ ] **Step 1.1: Write the failing test**

Create `VrcTwitchOscBridge.Tests/FloatActionModeTests.cs`:

```csharp
using VrcTwitchOscBridge.Models;
using Xunit;

namespace VrcTwitchOscBridge.Tests;

public sealed class FloatActionModeTests
{
    [Fact]
    public void Set_IsDefaultValue()
    {
        Assert.Equal(FloatActionMode.Set, default(FloatActionMode));
    }

    [Fact]
    public void HasTenMembersInExpectedOrder()
    {
        var expected = new[]
        {
            "Set", "Random", "Add", "Subtract", "AddSubtract",
            "Multiply", "Toggle", "Cycle", "Glitchy", "Pulse"
        };
        var actual = System.Enum.GetNames<FloatActionMode>();
        Assert.Equal(expected, actual);
    }
}
```

- [ ] **Step 1.2: Run the test to confirm it fails**

Run:
```bash
dotnet test "E:\!!!Program to work on\Proper Crystal Relay\VrcTwitchOscBridge.Tests\VrcTwitchOscBridge.Tests.csproj" --filter "FullyQualifiedName~FloatActionModeTests"
```
Expected: build error "The type or namespace name `FloatActionMode` could not be found".

- [ ] **Step 1.3: Create the enum**

Create `VrcTwitchOscBridge/Models/FloatActionMode.cs`:

```csharp
namespace VrcTwitchOscBridge.Models;

public enum FloatActionMode
{
    Set = 0,
    Random = 1,
    Add = 2,
    Subtract = 3,
    AddSubtract = 4,
    Multiply = 5,
    Toggle = 6,
    Cycle = 7,
    Glitchy = 8,
    Pulse = 9,
}
```

- [ ] **Step 1.4: Add the new file to the app project**

Open `VrcTwitchOscBridge/VrcTwitchOscBridge.csproj` and find the explicit `<Compile Include="Models\..." />` list (the project has `EnableDefaultCompileItems=false` per AGENTS.md). Add the new file in alphabetical order with the other `Models\*` entries:

```xml
<Compile Include="Models\FloatActionMode.cs" />
```

- [ ] **Step 1.5: Re-run the test to confirm it passes**

Run:
```bash
dotnet test "E:\!!!Program to work on\Proper Crystal Relay\VrcTwitchOscBridge.Tests\VrcTwitchOscBridge.Tests.csproj" --filter "FullyQualifiedName~FloatActionModeTests"
```
Expected: 2 passed.

- [ ] **Step 1.6: Commit**

```bash
git add "VrcTwitchOscBridge/Models/FloatActionMode.cs" "VrcTwitchOscBridge/VrcTwitchOscBridge.csproj" "VrcTwitchOscBridge.Tests/FloatActionModeTests.cs"
git commit -m "feat(float-modes): add FloatActionMode enum"
```

---

## Task 2: Add `FloatClampMode` enum

**Files:**
- Create: `VrcTwitchOscBridge/Models/FloatClampMode.cs`
- Modify: `VrcTwitchOscBridge/VrcTwitchOscBridge.csproj`
- Test: `VrcTwitchOscBridge.Tests/FloatClampModeTests.cs`

- [ ] **Step 2.1: Write the failing test**

Create `VrcTwitchOscBridge.Tests/FloatClampModeTests.cs`:

```csharp
using VrcTwitchOscBridge.Models;
using Xunit;

namespace VrcTwitchOscBridge.Tests;

public sealed class FloatClampModeTests
{
    [Fact]
    public void HasThreeMembersInExpectedOrder()
    {
        var actual = System.Enum.GetNames<FloatClampMode>();
        Assert.Equal(new[] { "None", "ZeroToOne", "MinToMax" }, actual);
    }

    [Fact]
    public void Values_AreDistinctNonNegativeInts()
    {
        var values = System.Enum.GetValues<FloatClampMode>();
        Assert.Equal(3, values.Length);
        Assert.Equal(0, (int)FloatClampMode.None);
        Assert.Equal(1, (int)FloatClampMode.ZeroToOne);
        Assert.Equal(2, (int)FloatClampMode.MinToMax);
    }
}
```

- [ ] **Step 2.2: Run the test to confirm it fails**

Run:
```bash
dotnet test "E:\!!!Program to work on\Proper Crystal Relay\VrcTwitchOscBridge.Tests\VrcTwitchOscBridge.Tests.csproj" --filter "FullyQualifiedName~FloatClampModeTests"
```
Expected: build error "The type or namespace name `FloatClampMode` could not be found".

- [ ] **Step 2.3: Create the enum**

Create `VrcTwitchOscBridge/Models/FloatClampMode.cs`:

```csharp
namespace VrcTwitchOscBridge.Models;

public enum FloatClampMode
{
    None = 0,
    ZeroToOne = 1,
    MinToMax = 2,
}
```

- [ ] **Step 2.4: Add the new file to the app project**

In `VrcTwitchOscBridge/VrcTwitchOscBridge.csproj`, add to the `Models\*` list in alphabetical order:

```xml
<Compile Include="Models\FloatClampMode.cs" />
```

- [ ] **Step 2.5: Re-run the test to confirm it passes**

Run:
```bash
dotnet test "E:\!!!Program to work on\Proper Crystal Relay\VrcTwitchOscBridge.Tests\VrcTwitchOscBridge.Tests.csproj" --filter "FullyQualifiedName~FloatClampModeTests"
```
Expected: 2 passed.

- [ ] **Step 2.6: Commit**

```bash
git add "VrcTwitchOscBridge/Models/FloatClampMode.cs" "VrcTwitchOscBridge/VrcTwitchOscBridge.csproj" "VrcTwitchOscBridge.Tests/FloatClampModeTests.cs"
git commit -m "feat(float-modes): add FloatClampMode enum"
```

---

## Task 3: Add float-mode fields to `TriggerRule`

**Files:**
- Modify: `VrcTwitchOscBridge/Models/TriggerRule.cs` (add backing fields + properties + setters near the existing `parameterValue`/`resetValue` at lines 89-106)
- Test: `VrcTwitchOscBridge.Tests/TriggerRuleFloatModeFieldsTests.cs`

- [ ] **Step 3.1: Write the failing tests**

Create `VrcTwitchOscBridge.Tests/TriggerRuleFloatModeFieldsTests.cs`:

```csharp
using VrcTwitchOscBridge.Models;
using Xunit;

namespace VrcTwitchOscBridge.Tests;

public sealed class TriggerRuleFloatModeFieldsTests
{
    [Fact]
    public void FloatActionMode_DefaultsToSet()
    {
        var rule = new TriggerRule();
        Assert.Equal(FloatActionMode.Set, rule.FloatActionMode);
    }

    [Fact]
    public void FloatRange_DefaultsToZeroOne()
    {
        var rule = new TriggerRule();
        Assert.Equal(0.0, rule.FloatRangeMin);
        Assert.Equal(1.0, rule.FloatRangeMax);
    }

    [Fact]
    public void FloatCycleStep_DefaultsToPointOne()
    {
        var rule = new TriggerRule();
        Assert.Equal(0.1, rule.FloatCycleStep);
    }

    [Fact]
    public void FloatAmounts_DefaultToExpectedValues()
    {
        var rule = new TriggerRule();
        Assert.Equal(0.1, rule.FloatAddAmount);
        Assert.Equal(0.1, rule.FloatSubtractAmount);
        Assert.Equal(0.1, rule.FloatAddSubtractAmount);
        Assert.Equal(1.5, rule.FloatMultiplyFactor);
    }

    [Fact]
    public void FloatToggleValues_DefaultToOneAndZero()
    {
        var rule = new TriggerRule();
        Assert.Equal(1.0, rule.FloatToggleOnValue);
        Assert.Equal(0.0, rule.FloatToggleOffValue);
    }

    [Fact]
    public void FloatGlitchyInterval_DefaultsTo200()
    {
        var rule = new TriggerRule();
        Assert.Equal(200, rule.FloatGlitchyIntervalMs);
    }

    [Fact]
    public void FloatPulseSeconds_DefaultsToPointFive()
    {
        var rule = new TriggerRule();
        Assert.Equal(0.5, rule.FloatPulseSeconds);
    }

    [Fact]
    public void FloatClampMode_DefaultsToZeroToOne()
    {
        var rule = new TriggerRule();
        Assert.Equal(FloatClampMode.ZeroToOne, rule.FloatClampMode);
    }

    [Fact]
    public void FloatRangeMax_SetBelowMin_ClampsToMinPlusEpsilon()
    {
        var rule = new TriggerRule { FloatRangeMin = 0.4 };
        rule.FloatRangeMax = 0.1;
        Assert.True(rule.FloatRangeMax >= rule.FloatRangeMin + 0.0001);
    }

    [Fact]
    public void FloatPulseSeconds_SetToNegative_ClampsToZero()
    {
        var rule = new TriggerRule { FloatPulseSeconds = -1.0 };
        Assert.Equal(0.0, rule.FloatPulseSeconds);
    }

    [Fact]
    public void FloatGlitchyInterval_SetToZero_ClampsToOne()
    {
        var rule = new TriggerRule { FloatGlitchyIntervalMs = 0 };
        Assert.Equal(1, rule.FloatGlitchyIntervalMs);
    }

    [Fact]
    public void RoundTripsAllFieldsThroughPublicProperties()
    {
        var rule = new TriggerRule
        {
            FloatActionMode = FloatActionMode.Glitchy,
            FloatRangeMin = 0.2,
            FloatRangeMax = 0.8,
            FloatCycleStep = 0.05,
            FloatAddAmount = 0.2,
            FloatSubtractAmount = 0.3,
            FloatAddSubtractAmount = -0.4,
            FloatMultiplyFactor = 2.0,
            FloatToggleOnValue = 1.0,
            FloatToggleOffValue = 0.0,
            FloatGlitchyIntervalMs = 150,
            FloatPulseSeconds = 0.75,
            FloatClampMode = FloatClampMode.MinToMax,
        };
        Assert.Equal(FloatActionMode.Glitchy, rule.FloatActionMode);
        Assert.Equal(0.2, rule.FloatRangeMin);
        Assert.Equal(0.8, rule.FloatRangeMax);
        Assert.Equal(0.05, rule.FloatCycleStep);
        Assert.Equal(0.2, rule.FloatAddAmount);
        Assert.Equal(0.3, rule.FloatSubtractAmount);
        Assert.Equal(-0.4, rule.FloatAddSubtractAmount);
        Assert.Equal(2.0, rule.FloatMultiplyFactor);
        Assert.Equal(1.0, rule.FloatToggleOnValue);
        Assert.Equal(0.0, rule.FloatToggleOffValue);
        Assert.Equal(150, rule.FloatGlitchyIntervalMs);
        Assert.Equal(0.75, rule.FloatPulseSeconds);
        Assert.Equal(FloatClampMode.MinToMax, rule.FloatClampMode);
    }
}
```

- [ ] **Step 3.2: Run the tests to confirm they fail**

Run:
```bash
dotnet test "E:\!!!Program to work on\Proper Crystal Relay\VrcTwitchOscBridge.Tests\VrcTwitchOscBridge.Tests.csproj" --filter "FullyQualifiedName~TriggerRuleFloatModeFieldsTests"
```
Expected: build error "TriggerRule does not contain a definition for `FloatActionMode`".

- [ ] **Step 3.3: Add the new fields to `TriggerRule.cs`**

Open `VrcTwitchOscBridge/Models/TriggerRule.cs`. Find the existing float-related backing fields near line 89-106 (look for `parameterName`, `parameterType`, `parameterValue`, `floatValueMode`, `floatTransitionSeconds`, `resetValue`, `rangeMinimum`, `rangeMaximum`). Add the new backing fields directly after `floatTransitionSeconds` and before `resetValue`, in this exact order:

```csharp
private FloatActionMode floatActionMode = FloatActionMode.Set;
private double floatRangeMin = 0.0;
private double floatRangeMax = 1.0;
private double floatCycleStep = 0.1;
private double floatAddAmount = 0.1;
private double floatSubtractAmount = 0.1;
private double floatAddSubtractAmount = 0.1;
private double floatMultiplyFactor = 1.5;
private double floatToggleOnValue = 1.0;
private double floatToggleOffValue = 0.0;
private int floatGlitchyIntervalMs = 200;
private double floatPulseSeconds = 0.5;
private FloatClampMode floatClampMode = FloatClampMode.ZeroToOne;
```

Now add the public properties (in the same area where the other parameter-related properties live, near `ParameterValue` setter at line 732 and `FloatTransitionSeconds` setter at line 772). Insert the following block right after the existing `FloatTransitionSeconds` setter (around line 803 or wherever the existing setter ends):

```csharp
public FloatActionMode FloatActionMode
{
    get => floatActionMode;
    set
    {
        if (floatActionMode == value) return;
        if (!Enum.IsDefined(value)) value = FloatActionMode.Set;
        floatActionMode = value;
        OnPropertyChanged();
    }
}

public double FloatRangeMin
{
    get => floatRangeMin;
    set
    {
        var clamped = Math.Clamp(value, 0.0, 1.0);
        if (clamped > floatRangeMax - 0.0001) clamped = floatRangeMax - 0.0001;
        if (clamped < 0) clamped = 0;
        floatRangeMin = clamped;
        OnPropertyChanged();
    }
}

public double FloatRangeMax
{
    get => floatRangeMax;
    set
    {
        var clamped = Math.Clamp(value, 0.0, 1.0);
        if (clamped < floatRangeMin + 0.0001) clamped = floatRangeMin + 0.0001;
        if (clamped > 1) clamped = 1;
        floatRangeMax = clamped;
        OnPropertyChanged();
    }
}

public double FloatCycleStep
{
    get => floatCycleStep;
    set
    {
        floatCycleStep = Math.Max(0.0, value);
        OnPropertyChanged();
    }
}

public double FloatAddAmount
{
    get => floatAddAmount;
    set
    {
        floatAddAmount = Math.Max(0.0, value);
        OnPropertyChanged();
    }
}

public double FloatSubtractAmount
{
    get => floatSubtractAmount;
    set
    {
        floatSubtractAmount = Math.Max(0.0, value);
        OnPropertyChanged();
    }
}

public double FloatAddSubtractAmount
{
    get => floatAddSubtractAmount;
    set
    {
        floatAddSubtractAmount = value;
        OnPropertyChanged();
    }
}

public double FloatMultiplyFactor
{
    get => floatMultiplyFactor;
    set
    {
        floatMultiplyFactor = value;
        OnPropertyChanged();
    }
}

public double FloatToggleOnValue
{
    get => floatToggleOnValue;
    set
    {
        floatToggleOnValue = Math.Clamp(value, 0.0, 1.0);
        OnPropertyChanged();
    }
}

public double FloatToggleOffValue
{
    get => floatToggleOffValue;
    set
    {
        floatToggleOffValue = Math.Clamp(value, 0.0, 1.0);
        OnPropertyChanged();
    }
}

public int FloatGlitchyIntervalMs
{
    get => floatGlitchyIntervalMs;
    set
    {
        floatGlitchyIntervalMs = Math.Max(1, value);
        OnPropertyChanged();
    }
}

public double FloatPulseSeconds
{
    get => floatPulseSeconds;
    set
    {
        floatPulseSeconds = Math.Max(0.0, value);
        OnPropertyChanged();
    }
}

public FloatClampMode FloatClampMode
{
    get => floatClampMode;
    set
    {
        if (!Enum.IsDefined(value)) value = FloatClampMode.ZeroToOne;
        floatClampMode = value;
        OnPropertyChanged();
    }
}
```

Note: the `OnPropertyChanged` calls (no parameter) match the existing pattern in this file. If the file uses `OnPropertyChanged(nameof(...))` for these specific properties, switch to that variant — match whichever pattern the surrounding setters use.

- [ ] **Step 3.4: Re-run the tests to confirm they pass**

Run:
```bash
dotnet test "E:\!!!Program to work on\Proper Crystal Relay\VrcTwitchOscBridge.Tests\VrcTwitchOscBridge.Tests.csproj" --filter "FullyQualifiedName~TriggerRuleFloatModeFieldsTests"
```
Expected: 12 passed.

- [ ] **Step 3.5: Commit**

```bash
git add "VrcTwitchOscBridge/Models/TriggerRule.cs" "VrcTwitchOscBridge.Tests/TriggerRuleFloatModeFieldsTests.cs"
git commit -m "feat(float-modes): add float action fields to TriggerRule"
```

---

## Task 4: Add `UsesFloatActionMode` and per-mode `UsesXxx` properties

**Files:**
- Modify: `VrcTwitchOscBridge/Models/TriggerRule.cs` (add computed properties near the existing `UsesFloatTimedValues` / `UsesFloatTransition` block at lines 1511-1522)
- Test: append to `VrcTwitchOscBridge.Tests/TriggerRuleFloatModeFieldsTests.cs`

- [ ] **Step 4.1: Write the failing tests**

Append to `VrcTwitchOscBridge.Tests/TriggerRuleFloatModeFieldsTests.cs`:

```csharp
public sealed class TriggerRuleFloatModeUsesTests
{
    [Fact]
    public void UsesFloatActionMode_TrueOnlyWhenParameterTypeIsFloat()
    {
        var rule = new TriggerRule { ParameterType = OscParameterType.Float };
        Assert.True(rule.UsesFloatActionMode);
        rule.ParameterType = OscParameterType.Bool;
        Assert.False(rule.UsesFloatActionMode);
        rule.ParameterType = OscParameterType.Int;
        Assert.False(rule.UsesFloatActionMode);
        rule.ParameterType = OscParameterType.String;
        Assert.False(rule.UsesFloatActionMode);
    }

    [Fact]
    public void UsesFloatSetMode_TrueWhenActionModeIsSet()
    {
        var rule = new TriggerRule
        {
            ParameterType = OscParameterType.Float,
            FloatActionMode = FloatActionMode.Set
        };
        Assert.True(rule.UsesFloatSetMode);
        rule.FloatActionMode = FloatActionMode.Random;
        Assert.False(rule.UsesFloatSetMode);
    }

    [Fact]
    public void UsesFloatRangeInputs_TrueForRandomCycleGlitchy()
    {
        var rule = new TriggerRule { ParameterType = OscParameterType.Float };
        foreach (var mode in new[] { FloatActionMode.Random, FloatActionMode.Cycle, FloatActionMode.Glitchy })
        {
            rule.FloatActionMode = mode;
            Assert.True(rule.UsesFloatRangeInputs, $"expected true for {mode}");
        }
        rule.FloatActionMode = FloatActionMode.Add;
        Assert.False(rule.UsesFloatRangeInputs);
    }

    [Fact]
    public void UsesFloatCycleStep_TrueOnlyForCycle()
    {
        var rule = new TriggerRule
        {
            ParameterType = OscParameterType.Float,
            FloatActionMode = FloatActionMode.Cycle
        };
        Assert.True(rule.UsesFloatCycleStep);
        rule.FloatActionMode = FloatActionMode.Random;
        Assert.False(rule.UsesFloatCycleStep);
    }

    [Fact]
    public void UsesFloatToggleValues_TrueOnlyForToggle()
    {
        var rule = new TriggerRule
        {
            ParameterType = OscParameterType.Float,
            FloatActionMode = FloatActionMode.Toggle
        };
        Assert.True(rule.UsesFloatToggleValues);
        rule.FloatActionMode = FloatActionMode.Add;
        Assert.False(rule.UsesFloatToggleValues);
    }

    [Fact]
    public void UsesFloatGlitchyInterval_TrueOnlyForGlitchy()
    {
        var rule = new TriggerRule
        {
            ParameterType = OscParameterType.Float,
            FloatActionMode = FloatActionMode.Glitchy
        };
        Assert.True(rule.UsesFloatGlitchyInterval);
        rule.FloatActionMode = FloatActionMode.Random;
        Assert.False(rule.UsesFloatGlitchyInterval);
    }

    [Fact]
    public void UsesFloatPulseSeconds_TrueOnlyForPulse()
    {
        var rule = new TriggerRule
        {
            ParameterType = OscParameterType.Float,
            FloatActionMode = FloatActionMode.Pulse
        };
        Assert.True(rule.UsesFloatPulseSeconds);
        rule.FloatActionMode = FloatActionMode.Add;
        Assert.False(rule.UsesFloatPulseSeconds);
    }

    [Fact]
    public void UsesFloatClampMode_TrueForRelativeModes()
    {
        var rule = new TriggerRule { ParameterType = OscParameterType.Float };
        foreach (var mode in new[]
                 {
                     FloatActionMode.Add, FloatActionMode.Subtract,
                     FloatActionMode.AddSubtract, FloatActionMode.Multiply
                 })
        {
            rule.FloatActionMode = mode;
            Assert.True(rule.UsesFloatClampMode, $"expected true for {mode}");
        }
        rule.FloatActionMode = FloatActionMode.Random;
        Assert.False(rule.UsesFloatClampMode);
    }
}
```

- [ ] **Step 4.2: Run the new tests to confirm they fail**

Run:
```bash
dotnet test "E:\!!!Program to work on\Proper Crystal Relay\VrcTwitchOscBridge.Tests\VrcTwitchOscBridge.Tests.csproj" --filter "FullyQualifiedName~TriggerRuleFloatModeUsesTests"
```
Expected: build error "TriggerRule does not contain a definition for `UsesFloatActionMode`".

- [ ] **Step 4.3: Add the `UsesXxx` computed properties**

Open `VrcTwitchOscBridge/Models/TriggerRule.cs`. Find the existing `UsesFloatTimedValues` / `UsesFloatTransition` block (around line 1511-1522) and add the following block immediately after it:

```csharp
public bool UsesFloatActionMode => ParameterType == OscParameterType.Float;

public bool UsesFloatSetMode => UsesFloatActionMode && FloatActionMode == FloatActionMode.Set;
public bool UsesFloatRandomMode => UsesFloatActionMode && FloatActionMode == FloatActionMode.Random;
public bool UsesFloatAddMode => UsesFloatActionMode && FloatActionMode == FloatActionMode.Add;
public bool UsesFloatSubtractMode => UsesFloatActionMode && FloatActionMode == FloatActionMode.Subtract;
public bool UsesFloatAddSubtractMode => UsesFloatActionMode && FloatActionMode == FloatActionMode.AddSubtract;
public bool UsesFloatMultiplyMode => UsesFloatActionMode && FloatActionMode == FloatActionMode.Multiply;
public bool UsesFloatToggleMode => UsesFloatActionMode && FloatActionMode == FloatActionMode.Toggle;
public bool UsesFloatCycleMode => UsesFloatActionMode && FloatActionMode == FloatActionMode.Cycle;
public bool UsesFloatGlitchyMode => UsesFloatActionMode && FloatActionMode == FloatActionMode.Glitchy;
public bool UsesFloatPulseMode => UsesFloatActionMode && FloatActionMode == FloatActionMode.Pulse;

public bool UsesFloatRangeInputs => UsesFloatActionMode &&
    (FloatActionMode == FloatActionMode.Random
     || FloatActionMode == FloatActionMode.Cycle
     || FloatActionMode == FloatActionMode.Glitchy);

public bool UsesFloatCycleStep => UsesFloatActionMode && FloatActionMode == FloatActionMode.Cycle;

public bool UsesFloatToggleValues => UsesFloatActionMode && FloatActionMode == FloatActionMode.Toggle;

public bool UsesFloatGlitchyInterval => UsesFloatActionMode && FloatActionMode == FloatActionMode.Glitchy;

public bool UsesFloatPulseSeconds => UsesFloatActionMode && FloatActionMode == FloatActionMode.Pulse;

public bool UsesFloatClampMode => UsesFloatActionMode &&
    (FloatActionMode == FloatActionMode.Add
     || FloatActionMode == FloatActionMode.Subtract
     || FloatActionMode == FloatActionMode.AddSubtract
     || FloatActionMode == FloatActionMode.Multiply);
```

- [ ] **Step 4.4: Re-run all `TriggerRuleFloatMode*` tests**

Run:
```bash
dotnet test "E:\!!!Program to work on\Proper Crystal Relay\VrcTwitchOscBridge.Tests\VrcTwitchOscBridge.Tests.csproj" --filter "FullyQualifiedName~TriggerRuleFloatMode"
```
Expected: 20 passed (12 from Task 3 + 8 new).

- [ ] **Step 4.5: Commit**

```bash
git add "VrcTwitchOscBridge/Models/TriggerRule.cs" "VrcTwitchOscBridge.Tests/TriggerRuleFloatModeFieldsTests.cs"
git commit -m "feat(float-modes): add UsesXxx visibility properties for float action modes"
```

---

## Task 5: Build the pure `FloatActionDispatch` value-calculator

**Files:**
- Create: `VrcTwitchOscBridge/Services/FloatActionDispatch.cs`
- Modify: `VrcTwitchOscBridge/VrcTwitchOscBridge.csproj`
- Test: `VrcTwitchOscBridge.Tests/FloatActionDispatchTests.cs`

This task is the core logic. Each mode has a small pure method that takes the rule, an optional current observed value, and returns the next value to send plus the reset value. Keeping it pure lets us unit-test every mode without touching the OSC client.

- [ ] **Step 5.1: Write the failing tests**

Create `VrcTwitchOscBridge.Tests/FloatActionDispatchTests.cs`:

```csharp
using VrcTwitchOscBridge.Models;
using VrcTwitchOscBridge.Services;
using Xunit;

namespace VrcTwitchOscBridge.Tests;

public sealed class FloatActionDispatchTests
{
    private static TriggerRule Rule(FloatActionMode mode, double parameterValue = 0.5, string? resetValue = "0")
    {
        return new TriggerRule
        {
            ParameterType = OscParameterType.Float,
            FloatActionMode = mode,
            ParameterValue = parameterValue.ToString(System.Globalization.CultureInfo.InvariantCulture),
            ResetValue = resetValue ?? string.Empty,
        };
    }

    [Fact]
    public void Set_ReturnsParameterValue()
    {
        var (next, _) = FloatActionDispatch.ComputeNext(
            Rule(FloatActionMode.Set, 0.42), currentValue: 0.0);
        Assert.Equal(0.42, next);
    }

    [Fact]
    public void Random_ReturnsValueInRange()
    {
        var rule = Rule(FloatActionMode.Random);
        rule.FloatRangeMin = 0.2;
        rule.FloatRangeMax = 0.8;
        for (int i = 0; i < 50; i++)
        {
            var (next, _) = FloatActionDispatch.ComputeNext(rule, currentValue: 0.0);
            Assert.InRange(next, 0.2, 0.8);
        }
    }

    [Fact]
    public void Add_AddsToCurrentAndClampsToZeroOne()
    {
        var rule = Rule(FloatActionMode.Add, 0);
        rule.FloatAddAmount = 0.3;
        rule.FloatClampMode = FloatClampMode.ZeroToOne;
        var (next, _) = FloatActionDispatch.ComputeNext(rule, currentValue: 0.6);
        Assert.Equal(0.9, next);
    }

    [Fact]
    public void Add_ClampsToOneWhenOverflowing()
    {
        var rule = Rule(FloatActionMode.Add);
        rule.FloatAddAmount = 0.5;
        rule.FloatClampMode = FloatClampMode.ZeroToOne;
        var (next, _) = FloatActionDispatch.ComputeNext(rule, currentValue: 0.9);
        Assert.Equal(1.0, next);
    }

    [Fact]
    public void Subtract_SubtractsFromCurrentAndClampsToZero()
    {
        var rule = Rule(FloatActionMode.Subtract);
        rule.FloatSubtractAmount = 0.4;
        rule.FloatClampMode = FloatClampMode.ZeroToOne;
        var (next, _) = FloatActionDispatch.ComputeNext(rule, currentValue: 0.3);
        Assert.Equal(0.0, next);
    }

    [Fact]
    public void AddSubtract_NegativeAmountSubtracts()
    {
        var rule = Rule(FloatActionMode.AddSubtract);
        rule.FloatAddSubtractAmount = -0.2;
        rule.FloatClampMode = FloatClampMode.ZeroToOne;
        var (next, _) = FloatActionDispatch.ComputeNext(rule, currentValue: 0.5);
        Assert.Equal(0.3, next);
    }

    [Fact]
    public void Multiply_MultipliesAndClampsToZeroOne()
    {
        var rule = Rule(FloatActionMode.Multiply);
        rule.FloatMultiplyFactor = 1.5;
        rule.FloatClampMode = FloatClampMode.ZeroToOne;
        var (next, _) = FloatActionDispatch.ComputeNext(rule, currentValue: 0.8);
        Assert.Equal(1.0, next);
    }

    [Fact]
    public void Multiply_NoClamp_AllowsOverOne()
    {
        var rule = Rule(FloatActionMode.Multiply);
        rule.FloatMultiplyFactor = 1.5;
        rule.FloatClampMode = FloatClampMode.None;
        var (next, _) = FloatActionDispatch.ComputeNext(rule, currentValue: 0.8);
        Assert.Equal(1.2, next);
    }

    [Fact]
    public void Toggle_CurrentNearOn_SendsOff()
    {
        var rule = Rule(FloatActionMode.Toggle);
        rule.FloatToggleOnValue = 1.0;
        rule.FloatToggleOffValue = 0.0;
        var (next, _) = FloatActionDispatch.ComputeNext(rule, currentValue: 1.0);
        Assert.Equal(0.0, next);
    }

    [Fact]
    public void Toggle_CurrentNearOff_SendsOn()
    {
        var rule = Rule(FloatActionMode.Toggle);
        rule.FloatToggleOnValue = 1.0;
        rule.FloatToggleOffValue = 0.0;
        var (next, _) = FloatActionDispatch.ComputeNext(rule, currentValue: 0.0);
        Assert.Equal(1.0, next);
    }

    [Fact]
    public void Cycle_IncrementsAndWrapsAtMax()
    {
        var rule = Rule(FloatActionMode.Cycle);
        rule.FloatRangeMin = 0.0;
        rule.FloatRangeMax = 1.0;
        rule.FloatCycleStep = 0.4;
        var (n1, _) = FloatActionDispatch.ComputeNext(rule, currentValue: 0.0);
        var (n2, _) = FloatActionDispatch.ComputeNext(rule, currentValue: 0.4);
        var (n3, _) = FloatActionDispatch.ComputeNext(rule, currentValue: 0.8);
        Assert.Equal(0.4, n1);
        Assert.Equal(0.8, n2);
        Assert.True(n3 < 0.4, $"expected wrap to land below 0.4, got {n3}");
    }

    [Fact]
    public void Pulse_ReturnsParameterValue()
    {
        var rule = Rule(FloatActionMode.Pulse, 0.7);
        var (next, _) = FloatActionDispatch.ComputeNext(rule, currentValue: 0.0);
        Assert.Equal(0.7, next);
    }

    [Fact]
    public void ComputeNext_PassThroughResetValue()
    {
        var rule = Rule(FloatActionMode.Random, resetValue: "0.25");
        var (_, reset) = FloatActionDispatch.ComputeNext(rule, currentValue: 0.0);
        Assert.Equal(0.25, reset);
    }

    [Fact]
    public void ComputeNext_EmptyResetValue_ReturnsNullReset()
    {
        var rule = Rule(FloatActionMode.Set, resetValue: "");
        var (_, reset) = FloatActionDispatch.ComputeNext(rule, currentValue: 0.0);
        Assert.Null(reset);
    }

    [Fact]
    public void ComputeNext_UnknownMode_FallsBackToParameterValue()
    {
        var rule = new TriggerRule
        {
            ParameterType = OscParameterType.Float,
            FloatActionMode = (FloatActionMode)999,
            ParameterValue = "0.33",
        };
        var (next, _) = FloatActionDispatch.ComputeNext(rule, currentValue: 0.0);
        Assert.Equal(0.33, next);
    }
}
```

- [ ] **Step 5.2: Run the tests to confirm they fail**

Run:
```bash
dotnet test "E:\!!!Program to work on\Proper Crystal Relay\VrcTwitchOscBridge.Tests\VrcTwitchOscBridge.Tests.csproj" --filter "FullyQualifiedName~FloatActionDispatchTests"
```
Expected: build error "The type or namespace name `FloatActionDispatch` could not be found".

- [ ] **Step 5.3: Create `FloatActionDispatch.cs`**

Create `VrcTwitchOscBridge/Services/FloatActionDispatch.cs`:

```csharp
using System.Globalization;
using VrcTwitchOscBridge.Models;

namespace VrcTwitchOscBridge.Services;

public static class FloatActionDispatch
{
    public const double ToggleTolerance = 0.0001;

    public static (double nextValue, double? resetValue) ComputeNext(TriggerRule rule, double currentValue)
    {
        var reset = ParseReset(rule.ResetValue);
        var next = rule.FloatActionMode switch
        {
            FloatActionMode.Random       => RollRandom(rule),
            FloatActionMode.Add          => ApplyClamp(rule, currentValue + rule.FloatAddAmount),
            FloatActionMode.Subtract     => ApplyClamp(rule, currentValue - rule.FloatSubtractAmount),
            FloatActionMode.AddSubtract  => ApplyClamp(rule, currentValue + rule.FloatAddSubtractAmount),
            FloatActionMode.Multiply     => ApplyClamp(rule, currentValue * rule.FloatMultiplyFactor),
            FloatActionMode.Toggle       => ComputeToggle(rule, currentValue),
            FloatActionMode.Cycle        => ComputeCycle(rule, currentValue),
            FloatActionMode.Pulse        => ParseParameter(rule),
            _                            => ParseParameter(rule),
        };
        return (next, reset);
    }

    private static double RollRandom(TriggerRule rule)
    {
        var min = rule.FloatRangeMin;
        var max = rule.FloatRangeMax;
        if (max <= min) return min;
        return Random.Shared.NextDouble() * (max - min) + min;
    }

    private static double ComputeToggle(TriggerRule rule, double currentValue)
    {
        if (Math.Abs(currentValue - rule.FloatToggleOnValue) < ToggleTolerance)
            return rule.FloatToggleOffValue;
        return rule.FloatToggleOnValue;
    }

    private static double ComputeCycle(TriggerRule rule, double currentValue)
    {
        var min = rule.FloatRangeMin;
        var max = rule.FloatRangeMax;
        var range = max - min;
        if (range <= 0) return min;
        var step = rule.FloatCycleStep;
        var next = currentValue + step;
        if (next > max)
        {
            var overflow = next - max;
            next = min + (overflow % range);
        }
        return next;
    }

    private static double ApplyClamp(TriggerRule rule, double value)
    {
        return rule.FloatClampMode switch
        {
            FloatClampMode.None     => value,
            FloatClampMode.ZeroToOne => Math.Clamp(value, 0.0, 1.0),
            FloatClampMode.MinToMax => Math.Clamp(value, rule.FloatRangeMin, rule.FloatRangeMax),
            _                       => Math.Clamp(value, 0.0, 1.0),
        };
    }

    private static double ParseParameter(TriggerRule rule)
    {
        if (double.TryParse(rule.ParameterValue, NumberStyles.Float, CultureInfo.InvariantCulture, out var v))
            return Math.Clamp(v, 0.0, 1.0);
        return 0.0;
    }

    private static double? ParseReset(string raw)
    {
        if (string.IsNullOrWhiteSpace(raw)) return null;
        if (double.TryParse(raw, NumberStyles.Float, CultureInfo.InvariantCulture, out var v))
            return Math.Clamp(v, 0.0, 1.0);
        return null;
    }
}
```

- [ ] **Step 5.4: Add the new file to the app project**

In `VrcTwitchOscBridge/VrcTwitchOscBridge.csproj`, add to the `Services\*` list in alphabetical order:

```xml
<Compile Include="Services\FloatActionDispatch.cs" />
```

- [ ] **Step 5.5: Re-run the tests to confirm they pass**

Run:
```bash
dotnet test "E:\!!!Program to work on\Proper Crystal Relay\VrcTwitchOscBridge.Tests\VrcTwitchOscBridge.Tests.csproj" --filter "FullyQualifiedName~FloatActionDispatchTests"
```
Expected: 15 passed.

- [ ] **Step 5.6: Commit**

```bash
git add "VrcTwitchOscBridge/Services/FloatActionDispatch.cs" "VrcTwitchOscBridge/VrcTwitchOscBridge.csproj" "VrcTwitchOscBridge.Tests/FloatActionDispatchTests.cs"
git commit -m "feat(float-modes): add FloatActionDispatch pure value calculator"
```

---

## Task 6: Persist the new fields through `SettingsStore`

**Files:**
- Modify: `VrcTwitchOscBridge/Services/SettingsStore.cs` (extend `PersistedTriggerRule` around line 3196, update `ToPersistedRule` at line 1006, update `ToRule` at line 1231)
- Test: `VrcTwitchOscBridge.Tests/TriggerRuleFloatModePersistenceTests.cs`

- [ ] **Step 6.1: Write the failing tests**

Create `VrcTwitchOscBridge.Tests/TriggerRuleFloatModePersistenceTests.cs`:

```csharp
using System.Reflection;
using VrcTwitchOscBridge.Models;
using VrcTwitchOscBridge.Services;
using Xunit;

namespace VrcTwitchOscBridge.Tests;

public sealed class TriggerRuleFloatModePersistenceTests
{
    [Fact]
    public void ToRule_MissingNewFields_AppliesSafeDefaults()
    {
        // Simulates a JSON file written by an older Crystal Relay that did
        // not know about the float action fields.
        var old = new PersistedTriggerRule
        {
            Id = Guid.NewGuid(),
            ParameterName = "VRCEmote",
            ParameterType = OscParameterType.Float,
            ParameterValue = "0.5",
            ResetValue = "0",
            FloatValueMode = FloatValueMode.Decimal,
            // Intentionally do NOT set FloatActionMode / FloatRangeMin / etc.
        };
        var rule = SettingsStore.ToRule(old);
        Assert.Equal(FloatActionMode.Set, rule.FloatActionMode);
        Assert.Equal(0.0, rule.FloatRangeMin);
        Assert.Equal(1.0, rule.FloatRangeMax);
        Assert.Equal(0.1, rule.FloatCycleStep);
        Assert.Equal(0.1, rule.FloatAddAmount);
        Assert.Equal(0.1, rule.FloatSubtractAmount);
        Assert.Equal(0.1, rule.FloatAddSubtractAmount);
        Assert.Equal(1.5, rule.FloatMultiplyFactor);
        Assert.Equal(1.0, rule.FloatToggleOnValue);
        Assert.Equal(0.0, rule.FloatToggleOffValue);
        Assert.Equal(200, rule.FloatGlitchyIntervalMs);
        Assert.Equal(0.5, rule.FloatPulseSeconds);
        Assert.Equal(FloatClampMode.ZeroToOne, rule.FloatClampMode);
    }

    [Fact]
    public void RoundTrip_AllNewFieldsPreserved()
    {
        var original = new TriggerRule
        {
            ParameterType = OscParameterType.Float,
            FloatActionMode = FloatActionMode.Glitchy,
            FloatRangeMin = 0.1,
            FloatRangeMax = 0.9,
            FloatCycleStep = 0.05,
            FloatAddAmount = 0.2,
            FloatSubtractAmount = 0.3,
            FloatAddSubtractAmount = -0.4,
            FloatMultiplyFactor = 2.5,
            FloatToggleOnValue = 0.8,
            FloatToggleOffValue = 0.2,
            FloatGlitchyIntervalMs = 350,
            FloatPulseSeconds = 1.25,
            FloatClampMode = FloatClampMode.MinToMax,
        };
        var persisted = ToPersistedViaReflection(original);
        var roundTripped = SettingsStore.ToRule(persisted);
        Assert.Equal(original.FloatActionMode, roundTripped.FloatActionMode);
        Assert.Equal(original.FloatRangeMin, roundTripped.FloatRangeMin);
        Assert.Equal(original.FloatRangeMax, roundTripped.FloatRangeMax);
        Assert.Equal(original.FloatCycleStep, roundTripped.FloatCycleStep);
        Assert.Equal(original.FloatAddAmount, roundTripped.FloatAddAmount);
        Assert.Equal(original.FloatSubtractAmount, roundTripped.FloatSubtractAmount);
        Assert.Equal(original.FloatAddSubtractAmount, roundTripped.FloatAddSubtractAmount);
        Assert.Equal(original.FloatMultiplyFactor, roundTripped.FloatMultiplyFactor);
        Assert.Equal(original.FloatToggleOnValue, roundTripped.FloatToggleOnValue);
        Assert.Equal(original.FloatToggleOffValue, roundTripped.FloatToggleOffValue);
        Assert.Equal(original.FloatGlitchyIntervalMs, roundTripped.FloatGlitchyIntervalMs);
        Assert.Equal(original.FloatPulseSeconds, roundTripped.FloatPulseSeconds);
        Assert.Equal(original.FloatClampMode, roundTripped.FloatClampMode);
    }

    [Fact]
    public void ToRule_OutOfRangeFloatActionMode_FallsBackToSet()
    {
        var old = new PersistedTriggerRule
        {
            ParameterType = OscParameterType.Float,
            FloatActionMode = (FloatActionMode)999,
        };
        var rule = SettingsStore.ToRule(old);
        Assert.Equal(FloatActionMode.Set, rule.FloatActionMode);
    }

    // The ToPersistedRule method on SettingsStore is private. We invoke it
    // via reflection so we can verify the writing side of the round-trip
    // without touching the real AppData folder.
    private static PersistedTriggerRule ToPersistedViaReflection(TriggerRule rule)
    {
        var method = typeof(SettingsStore).GetMethod(
            "ToPersistedRule",
            BindingFlags.Static | BindingFlags.NonPublic);
        Assert.NotNull(method);
        return (PersistedTriggerRule)method!.Invoke(null, new object[] { rule })!;
    }
}
```

- [ ] **Step 6.2: Run the tests to confirm they fail**

Run:
```bash
dotnet test "E:\!!!Program to work on\Proper Crystal Relay\VrcTwitchOscBridge.Tests\VrcTwitchOscBridge.Tests.csproj" --filter "FullyQualifiedName~TriggerRuleFloatModePersistenceTests"
```
Expected: build errors on `PersistedTriggerRule.FloatActionMode` and `SettingsStore.ToRule(...)` not finding the new fields.

- [ ] **Step 6.3: Extend `PersistedTriggerRule`**

Open `VrcTwitchOscBridge/Services/SettingsStore.cs`. Find the `PersistedTriggerRule` record/class (around line 3196). Add the new fields right after `FloatTransitionSeconds` (around line 3278). Match the existing style — if it's a record with positional parameters, add them positionally; if it's a class with init-only properties, add them as init properties:

```csharp
FloatActionMode FloatActionMode = FloatActionMode.Set,
double FloatRangeMin = 0.0,
double FloatRangeMax = 1.0,
double FloatCycleStep = 0.1,
double FloatAddAmount = 0.1,
double FloatSubtractAmount = 0.1,
double FloatAddSubtractAmount = 0.1,
double FloatMultiplyFactor = 1.5,
double FloatToggleOnValue = 1.0,
double FloatToggleOffValue = 0.0,
int FloatGlitchyIntervalMs = 200,
double FloatPulseSeconds = 0.5,
FloatClampMode FloatClampMode = FloatClampMode.ZeroToOne,
```

(If the type is a class instead of a record, change the commas to semicolons and add `{ get; init; } = ...;` accessors in the same style as the surrounding fields.)

- [ ] **Step 6.4: Update `ToPersistedRule`**

In the same file, find the private `ToPersistedRule(TriggerRule rule)` method (around line 1006). Add matching assignments right after the existing `FloatTransitionSeconds = rule.FloatTransitionSeconds` line:

```csharp
FloatActionMode = rule.FloatActionMode,
FloatRangeMin = rule.FloatRangeMin,
FloatRangeMax = rule.FloatRangeMax,
FloatCycleStep = rule.FloatCycleStep,
FloatAddAmount = rule.FloatAddAmount,
FloatSubtractAmount = rule.FloatSubtractAmount,
FloatAddSubtractAmount = rule.FloatAddSubtractAmount,
FloatMultiplyFactor = rule.FloatMultiplyFactor,
FloatToggleOnValue = rule.FloatToggleOnValue,
FloatToggleOffValue = rule.FloatToggleOffValue,
FloatGlitchyIntervalMs = rule.FloatGlitchyIntervalMs,
FloatPulseSeconds = rule.FloatPulseSeconds,
FloatClampMode = rule.FloatClampMode,
```

- [ ] **Step 6.5: Update `ToRule`**

In the same file, find the internal `ToRule(PersistedTriggerRule rule)` method (around line 1231). Add matching assignments in the returned `new TriggerRule { ... }` literal, right after the existing `FloatTransitionSeconds = ...` line:

```csharp
FloatActionMode = Enum.IsDefined(rule.FloatActionMode) ? rule.FloatActionMode : FloatActionMode.Set,
FloatRangeMin = rule.FloatRangeMin,
FloatRangeMax = rule.FloatRangeMax,
FloatCycleStep = rule.FloatCycleStep,
FloatAddAmount = rule.FloatAddAmount,
FloatSubtractAmount = rule.FloatSubtractAmount,
FloatAddSubtractAmount = rule.FloatAddSubtractAmount,
FloatMultiplyFactor = rule.FloatMultiplyFactor,
FloatToggleOnValue = rule.FloatToggleOnValue,
FloatToggleOffValue = rule.FloatToggleOffValue,
FloatGlitchyIntervalMs = Math.Max(1, rule.FloatGlitchyIntervalMs),
FloatPulseSeconds = Math.Max(0.0, rule.FloatPulseSeconds),
FloatClampMode = Enum.IsDefined(rule.FloatClampMode) ? rule.FloatClampMode : FloatClampMode.ZeroToOne,
```

- [ ] **Step 6.6: Re-run the tests to confirm they pass**

Run:
```bash
dotnet test "E:\!!!Program to work on\Proper Crystal Relay\VrcTwitchOscBridge.Tests\VrcTwitchOscBridge.Tests.csproj" --filter "FullyQualifiedName~TriggerRuleFloatModePersistenceTests"
```
Expected: 3 passed.

- [ ] **Step 6.7: Commit**

```bash
git add "VrcTwitchOscBridge/Services/SettingsStore.cs" "VrcTwitchOscBridge.Tests/TriggerRuleFloatModePersistenceTests.cs"
git commit -m "feat(float-modes): persist float action fields through SettingsStore"
```

---

## Task 7: Wire `ResolveFloatActionAsync` into `BridgeCoordinator`

**Files:**
- Modify: `VrcTwitchOscBridge/Services/BridgeCoordinator.cs` (add the new branch in `ResolveAvatarParameterActionAsync` at line 9945, add the new `ResolveFloatActionAsync` helper)
- No new test file (logic is covered by `FloatActionDispatch`; integration is covered by manual smoke)

- [ ] **Step 7.1: Read the existing resolver to plan the insertion**

Open `VrcTwitchOscBridge/Services/BridgeCoordinator.cs` and read `ResolveAvatarParameterActionAsync` (starts at line 9945, ~95 lines). Note the three existing branches:
- `OscParameterType.Bool && DurationSeconds <= 0` → instant bool toggle
- `OscParameterType.Int && DurationSeconds <= 0` → instant int
- Default → resolve target + optional reset packet

Also note the existing `TryGetCurrentAvatarFloatValueAsync` helper at line 8073 and `ResolveAvatarParameterPacketValue` at line 10292.

- [ ] **Step 7.2: Add the Float branch**

Inside `ResolveAvatarParameterActionAsync`, add the new branch as the first check inside the `if (rule.ActionType == OscActionType.AvatarParameter)` path (or right at the start of the method, whichever matches the surrounding style). Use this code:

```csharp
if (rule.ParameterType == OscParameterType.Float)
    return await ResolveFloatActionAsync(rule, cancellationToken).ConfigureAwait(false);
```

- [ ] **Step 7.3: Add the new helper method**

Add the following private helper to `BridgeCoordinator` (place it right after `ResolveAvatarParameterActionAsync` ends, or grouped with the other resolve helpers near line 10292):

```csharp
private async Task<ResolvedRuleAction> ResolveFloatActionAsync(
    TriggerRule rule, CancellationToken cancellationToken)
{
    var address = VrChatOscClient.NormalizeAvatarParameterAddress(rule.ParameterName);

    if (rule.FloatActionMode == FloatActionMode.Pulse)
    {
        // Pulse: send the value immediately, schedule a single restore
        // after FloatPulseSeconds. DurationSeconds is intentionally ignored.
        var (pulseValue, _) = FloatActionDispatch.ComputeNext(rule, currentValue: 0.0);
        var pulsePacket = vrChatOscClient.BuildAvatarParameterPacket(
            address, OscParameterType.Float,
            FloatValueModeConverter.ToOscText(pulseValue));
        ScheduleFloatPulseRestore(rule, address, pulsePacket);
        return new ResolvedRuleAction(
            packets: new[] { pulsePacket },
            resetPackets: Array.Empty<byte[]>(),
            resolvedDurationSeconds: Math.Max(0.0, rule.FloatPulseSeconds));
    }

    var currentValue = await TryGetCurrentAvatarFloatValueAsync(
        address,
        fallback: FloatValueModeConverter.TryParseNormalized(
            rule.FloatValueMode, rule.ParameterValue, out var fb) ? fb : 0.0,
        cancellationToken).ConfigureAwait(false);

    var (nextValue, resetValue) = FloatActionDispatch.ComputeNext(rule, currentValue);
    var targetPacket = vrChatOscClient.BuildAvatarParameterPacket(
        address, OscParameterType.Float,
        FloatValueModeConverter.ToOscText(nextValue));
    var resetPackets = resetValue.HasValue
        ? new[]
          {
              vrChatOscClient.BuildAvatarParameterPacket(
                  address, OscParameterType.Float,
                  FloatValueModeConverter.ToOscText(resetValue.Value))
          }
        : Array.Empty<byte[]>();

    if (rule.FloatActionMode == FloatActionMode.Glitchy && rule.DurationSeconds > 0)
    {
        // Hand the per-tick random loop to a dedicated session. The
        // session handles both the re-rolls during the active time and
        // the reset packet at the end, so the standard
        // ScheduleActionResetPackets path is bypassed.
        return ResolveGlitchyFloatSession(rule, address, targetPacket);
    }

    return new ResolvedRuleAction(
        packets: new[] { targetPacket },
        resetPackets: resetPackets,
        resolvedDurationSeconds: rule.DurationSeconds);
}
```

- [ ] **Step 7.4: Add the pulse-restore scheduler stub**

Add the following method to `BridgeCoordinator` (you can fill in the body in Task 9; for now, a thin implementation is enough to keep the build green):

```csharp
private void ScheduleFloatPulseRestore(
    TriggerRule rule, string address, byte[] initialPacket)
{
    var (_, reset) = FloatActionDispatch.ComputeNext(rule, currentValue: 0.0);
    var seconds = Math.Max(0.0, rule.FloatPulseSeconds);
    _ = Task.Run(async () =>
    {
        try
        {
            await Task.Delay(TimeSpan.FromSeconds(seconds)).ConfigureAwait(false);
            if (reset.HasValue)
            {
                var resetPacket = vrChatOscClient.BuildAvatarParameterPacket(
                    address, OscParameterType.Float,
                    FloatValueModeConverter.ToOscText(reset.Value));
                await oscRouterService.SendToVrChatAsync(resetPacket).ConfigureAwait(false);
                ObserveOscValue(new OscObservedValue(address, OscParameterType.Float, (float)reset.Value));
            }
        }
        catch (TaskCanceledException) { /* expected on stop */ }
        catch (Exception ex)
        {
            LogDispatchDiagnostic(rule, $"Pulse restore failed: {ex.Message}");
        }
    });
}
```

Note: the exact `LogDispatchDiagnostic` / `oscRouterService` / `ObserveOscValue` member names may differ in this file — search for the closest existing pattern (e.g. `LogBridge(...)`, `oscRouterService.SendToVrChatAsync(...)`, `ObserveOscValue(...)`) and use those names. The build will tell you which ones are wrong; fix them in place. The test in Task 9 will exercise the restore path end-to-end.

- [ ] **Step 7.5: Build the app to catch naming/visibility issues**

Run:
```bash
dotnet build "E:\!!!Program to work on\Proper Crystal Relay\VrcTwitchOscBridge\VrcTwitchOscBridge.csproj" --no-restore
```
Expected: build succeeds (warnings are fine).

- [ ] **Step 7.6: Run the full test suite to make sure nothing regressed**

Run:
```bash
dotnet test "E:\!!!Program to work on\Proper Crystal Relay\VrcTwitchOscBridge.Tests\VrcTwitchOscBridge.Tests.csproj"
```
Expected: all existing tests still pass; the new `FloatActionMode*` and `TriggerRuleFloatMode*` tests still pass.

- [ ] **Step 7.7: Commit**

```bash
git add "VrcTwitchOscBridge/Services/BridgeCoordinator.cs"
git commit -m "feat(float-modes): dispatch float action modes through BridgeCoordinator"
```

---

## Task 8: Implement the Glitchy session

**Files:**
- Modify: `VrcTwitchOscBridge/Services/BridgeCoordinator.cs` (add `ResolveGlitchyFloatSession` and the loop method, mirroring `ExecuteTimedFloatAvatarParameterRuleActionAsync` at line 7691)

The Glitchy session replaces the smooth-ramp path with a loop that re-rolls a random value in `[FloatRangeMin, FloatRangeMax]` every `FloatGlitchyIntervalMs` for the active time, then runs the existing end-ramp to `ResetValue` via the standard timed-float completion path.

- [ ] **Step 8.1: Add the glitchy session class**

Near the existing `ActiveFloatRedeemSessionState` definition (search for that class name in `BridgeCoordinator.cs`; it is a private class near the timed-float dispatch path around line 7691), add a sibling:

```csharp
private sealed class ActiveFloatGlitchyRedeemSessionState
{
    public TriggerRule Rule { get; init; } = null!;
    public string Address { get; init; } = string.Empty;
    public double Min { get; init; }
    public double Max { get; init; }
    public int IntervalMs { get; init; }
    public DateTimeOffset ActiveUntil { get; init; }
    public double ResetValue { get; init; }
    public CancellationTokenSource CompletionCancellation { get; init; } = new();
    public List<string> LaneKeys { get; init; } = new();
    public Guid LeaseId { get; init; }
    public bool IsTest { get; init; }
}
```

- [ ] **Step 8.2: Add the glitchy session dictionary**

Find the existing `activeFloatRedeemSessions` field (around line 7700). Add a sibling field right next to it:

```csharp
private readonly Dictionary<Guid, ActiveFloatGlitchyRedeemSessionState> activeGlitchyRedeemSessions = new();
```

- [ ] **Step 8.3: Add the glitchy resolve helper**

Add this helper to `BridgeCoordinator` (right next to the new `ResolveFloatActionAsync` from Task 7):

```csharp
private ResolvedRuleAction ResolveGlitchyFloatSession(
    TriggerRule rule, string address, byte[] initialPacket)
{
    var leaseId = Guid.NewGuid();
    var resetValue = ParseResetValueForGlitchy(rule);
    var session = new ActiveFloatGlitchyRedeemSessionState
    {
        Rule = rule,
        Address = address,
        Min = rule.FloatRangeMin,
        Max = rule.FloatRangeMax,
        IntervalMs = rule.FloatGlitchyIntervalMs,
        ActiveUntil = DateTimeOffset.UtcNow.AddSeconds(rule.DurationSeconds),
        ResetValue = resetValue,
        LeaseId = leaseId,
    };

    // Replace any prior glitchy session for this rule (mirror the
    // existing active-float session replacement at line 7737-7744).
    if (activeGlitchyRedeemSessions.TryGetValue(rule.Id, out var prior))
    {
        prior.CompletionCancellation.Cancel();
        activeGlitchyRedeemSessions.Remove(rule.Id);
    }
    activeGlitchyRedeemSessions[rule.Id] = session;

    _ = Task.Run(() => RunGlitchyLoopAsync(session));
    // No reset packet in the return value: the session sends the reset
    // value at the end of the active time itself, so the standard
    // ScheduleActionResetPackets path would double-send.
    return new ResolvedRuleAction(
        packets: new[] { initialPacket },
        resetPackets: Array.Empty<byte[]>(),
        resolvedDurationSeconds: rule.DurationSeconds);
}

private static double ParseResetValueForGlitchy(TriggerRule rule)
{
    if (string.IsNullOrWhiteSpace(rule.ResetValue)) return 0.0;
    if (double.TryParse(rule.ResetValue, System.Globalization.NumberStyles.Float,
        System.Globalization.CultureInfo.InvariantCulture, out var v))
    {
        return Math.Clamp(v, 0.0, 1.0);
    }
    return 0.0;
}
```

Note: the `ParseFirstResetFloat` helper is intentionally simple. If the codebase already exposes a public OSC packet decoder, replace this stub with a call to that decoder. The behavior only matters when `FloatTransitionSeconds > 0`, which is the smooth-ramp case for the restore packet; if no decoder is available, set the reset to `0.0` and add a TODO comment to revisit.

- [ ] **Step 8.4: Add the glitchy loop runner**

```csharp
private async Task RunGlitchyLoopAsync(ActiveFloatGlitchyRedeemSessionState session)
{
    try
    {
        while (!session.CompletionCancellation.IsCancellationRequested
               && DateTimeOffset.UtcNow < session.ActiveUntil)
        {
            await Task.Delay(session.IntervalMs, session.CompletionCancellation.Token)
                .ConfigureAwait(false);
            if (session.CompletionCancellation.IsCancellationRequested) break;
            var value = Random.Shared.NextDouble() * (session.Max - session.Min) + session.Min;
            await SendSingleFloatAvatarParameterValueAsync(
                session.Address, value, session.CompletionCancellation.Token)
                .ConfigureAwait(false);
        }
        if (!session.CompletionCancellation.IsCancellationRequested
            && !string.IsNullOrWhiteSpace(session.Rule.ResetValue))
        {
            await SendSingleFloatAvatarParameterValueAsync(
                session.Address, session.ResetValue, session.CompletionCancellation.Token)
                .ConfigureAwait(false);
        }
    }
    catch (TaskCanceledException) { /* expected on stop */ }
    catch (Exception ex)
    {
        LogDispatchDiagnostic(session.Rule, $"Glitchy loop failed: {ex.Message}");
    }
    finally
    {
        if (activeGlitchyRedeemSessions.TryGetValue(session.Rule.Id, out var current)
            && current.LeaseId == session.LeaseId)
        {
            activeGlitchyRedeemSessions.Remove(session.Rule.Id);
        }
    }
}
```

Note: the `LogDispatchDiagnostic` / `SendSingleFloatAvatarParameterValueAsync` names must match the existing private members in `BridgeCoordinator`. Search for the existing call sites to find the exact names.

- [ ] **Step 8.5: Build to verify**

Run:
```bash
dotnet build "E:\!!!Program to work on\Proper Crystal Relay\VrcTwitchOscBridge\VrcTwitchOscBridge.csproj" --no-restore
```
Expected: build succeeds.

- [ ] **Step 8.6: Run the full test suite**

Run:
```bash
dotnet test "E:\!!!Program to work on\Proper Crystal Relay\VrcTwitchOscBridge.Tests\VrcTwitchOscBridge.Tests.csproj"
```
Expected: all tests pass.

- [ ] **Step 8.7: Commit**

```bash
git add "VrcTwitchOscBridge/Services/BridgeCoordinator.cs"
git commit -m "feat(float-modes): add glitchy float session with re-roll loop"
```

---

## Task 9: Polish the Pulse restore path

**Files:**
- Modify: `VrcTwitchOscBridge/Services/BridgeCoordinator.cs` (the `ScheduleFloatPulseRestore` stub from Task 7 is functional; this task adds the cancellation token wiring so an avatar change or manual stop cancels the in-flight pulse restore)

- [ ] **Step 9.1: Read the existing pulse-restore stub**

Open the `ScheduleFloatPulseRestore` method added in Task 7.

- [ ] **Step 9.2: Add cancellation support**

Replace the method body so the `Task.Delay` uses a cancellable token sourced from the existing stop/avatar-change machinery. Search the file for how `ExecuteTimedFloatAvatarParameterRuleActionAsync` (line 7691) wires its `CompletionCancellation` and mirror that pattern. The final code should look like this:

```csharp
private void ScheduleFloatPulseRestore(
    TriggerRule rule, string address, byte[] initialPacket)
{
    var (_, reset) = FloatActionDispatch.ComputeNext(rule, currentValue: 0.0);
    var seconds = Math.Max(0.0, rule.FloatPulseSeconds);
    var cts = new CancellationTokenSource();
    cancellationTokensForPulse.Add(cts);
    _ = Task.Run(async () =>
    {
        try
        {
            await Task.Delay(TimeSpan.FromSeconds(seconds), cts.Token).ConfigureAwait(false);
            if (reset.HasValue && !cts.IsCancellationRequested)
            {
                var resetPacket = vrChatOscClient.BuildAvatarParameterPacket(
                    address, OscParameterType.Float,
                    FloatValueModeConverter.ToOscText(reset.Value));
                await oscRouterService.SendToVrChatAsync(resetPacket).ConfigureAwait(false);
                ObserveOscValue(new OscObservedValue(address, OscParameterType.Float, (float)reset.Value));
            }
        }
        catch (TaskCanceledException) { /* expected on stop */ }
        catch (Exception ex)
        {
            LogDispatchDiagnostic(rule, $"Pulse restore failed: {ex.Message}");
        }
        finally
        {
            cts.Dispose();
            cancellationTokensForPulse.Remove(cts);
        }
    });
}
```

- [ ] **Step 9.3: Add the `cancellationTokensForPulse` set**

Find the existing `activeFloatRedeemSessions` field (around line 7700). Add a sibling field right next to it:

```csharp
private readonly HashSet<CancellationTokenSource> cancellationTokensForPulse = new();
```

- [ ] **Step 9.4: Hook stop / avatar-change**

Find the existing methods that cancel timed-float sessions on stop or avatar change (look for calls to `CompletionCancellation.Cancel()` on `activeFloatRedeemSessions`). Add a sibling cancellation loop next to them:

```csharp
foreach (var cts in cancellationTokensForPulse) cts.Cancel();
cancellationTokensForPulse.Clear();
```

- [ ] **Step 9.5: Build and run tests**

Run:
```bash
dotnet build "E:\!!!Program to work on\Proper Crystal Relay\VrcTwitchOscBridge\VrcTwitchOscBridge.csproj" --no-restore
dotnet test "E:\!!!Program to work on\Proper Crystal Relay\VrcTwitchOscBridge.Tests\VrcTwitchOscBridge.Tests.csproj"
```
Expected: build succeeds; all tests pass.

- [ ] **Step 9.6: Commit**

```bash
git add "VrcTwitchOscBridge/Services/BridgeCoordinator.cs"
git commit -m "feat(float-modes): make pulse restore cancellable on stop and avatar change"
```

---

## Task 10: Add the Float Action Mode section to the compact editor

**Files:**
- Modify: `VrcTwitchOscBridge/AvatarSetsManagerWindow.xaml` (insert new section between the existing "Parameter Type" row at line 1222 and the "Parameter Name (selected)" textbox at line 1286)
- Modify: `VrcTwitchOscBridge/AvatarSetsManagerWindow.xaml.cs` (add click handlers for the new mode buttons)

- [ ] **Step 10.1: Read the existing XAML structure**

Open `VrcTwitchOscBridge/AvatarSetsManagerWindow.xaml` and locate the section in the screenshot. The key landmarks:
- True/False parameter-value buttons (line 1177-1220)
- "Parameter Type" + All/Bool/Int/Float filter buttons (line 1222-1284)
- "Parameter Name (selected)" textbox (line 1286)

The new section inserts **after** the "Parameter Type" row, **before** the "Parameter Name (selected)" header. It is wrapped in a `StackPanel` whose `Visibility` binds to `UsesFloatActionMode` on the active `rule`.

- [ ] **Step 10.2: Add the XAML block**

Insert the following XAML immediately after the closing tag of the "Parameter Type" row's parent container (find the matching `</Border>` or `</Grid>` and insert before it):

```xml
<!-- Float Action Mode (visible only when ParameterType == Float) -->
<Border Background="#22FFFFFF" CornerRadius="6" Padding="8" Margin="0,4,0,4"
        Visibility="{Binding UsesFloatActionMode, Converter={StaticResource BoolToVisibilityConverter}}">
    <StackPanel>
        <TextBlock Text="Float Action Mode" FontWeight="SemiBold" Margin="0,0,0,4" />
        <ItemsControl>
            <ItemsControl.ItemsPanel>
                <ItemsPanelTemplate>
                    <WrapPanel />
                </ItemsPanelTemplate>
            </ItemsControl.ItemsPanel>
            <ItemsControl.Items>
                <Button Content="Set" Style="{StaticResource ChipButtonStyle}"
                        Click="OnFloatModeSetClicked" />
                <Button Content="Random" Style="{StaticResource ChipButtonStyle}"
                        Click="OnFloatModeRandomClicked" />
                <Button Content="Add" Style="{StaticResource ChipButtonStyle}"
                        Click="OnFloatModeAddClicked" />
                <Button Content="Subtract" Style="{StaticResource ChipButtonStyle}"
                        Click="OnFloatModeSubtractClicked" />
                <Button Content="±" Style="{StaticResource ChipButtonStyle}"
                        Click="OnFloatModeAddSubtractClicked" />
                <Button Content="Multiply" Style="{StaticResource ChipButtonStyle}"
                        Click="OnFloatModeMultiplyClicked" />
                <Button Content="Toggle" Style="{StaticResource ChipButtonStyle}"
                        Click="OnFloatModeToggleClicked" />
                <Button Content="Cycle" Style="{StaticResource ChipButtonStyle}"
                        Click="OnFloatModeCycleClicked" />
                <Button Content="Glitchy" Style="{StaticResource ChipButtonStyle}"
                        Click="OnFloatModeGlitchyClicked" />
                <Button Content="Pulse" Style="{StaticResource ChipButtonStyle}"
                        Click="OnFloatModePulseClicked" />
            </ItemsControl.Items>
        </ItemsControl>

        <!-- Range inputs (Random / Cycle / Glitchy) -->
        <Grid Margin="0,6,0,0" Visibility="{Binding UsesFloatRangeInputs, Converter={StaticResource BoolToVisibilityConverter}}">
            <Grid.ColumnDefinitions>
                <ColumnDefinition Width="*" />
                <ColumnDefinition Width="8" />
                <ColumnDefinition Width="*" />
            </Grid.ColumnDefinitions>
            <StackPanel Grid.Column="0">
                <TextBlock Text="Min" />
                <TextBox Text="{Binding FloatRangeMin, UpdateSourceTrigger=PropertyChanged}" />
            </StackPanel>
            <StackPanel Grid.Column="2">
                <TextBlock Text="Max" />
                <TextBox Text="{Binding FloatRangeMax, UpdateSourceTrigger=PropertyChanged}" />
            </StackPanel>
        </Grid>

        <!-- Cycle step -->
        <StackPanel Margin="0,6,0,0" Visibility="{Binding UsesFloatCycleStep, Converter={StaticResource BoolToVisibilityConverter}}">
            <TextBlock Text="Step" />
            <TextBox Text="{Binding FloatCycleStep, UpdateSourceTrigger=PropertyChanged}" />
        </StackPanel>

        <!-- Add / Subtract / ± / Multiply amount -->
        <StackPanel Margin="0,6,0,0" Visibility="{Binding UsesFloatAddMode, Converter={StaticResource BoolToVisibilityConverter}}">
            <TextBlock Text="Add amount" />
            <TextBox Text="{Binding FloatAddAmount, UpdateSourceTrigger=PropertyChanged}" />
        </StackPanel>
        <StackPanel Margin="0,6,0,0" Visibility="{Binding UsesFloatSubtractMode, Converter={StaticResource BoolToVisibilityConverter}}">
            <TextBlock Text="Subtract amount" />
            <TextBox Text="{Binding FloatSubtractAmount, UpdateSourceTrigger=PropertyChanged}" />
        </StackPanel>
        <StackPanel Margin="0,6,0,0" Visibility="{Binding UsesFloatAddSubtractMode, Converter={StaticResource BoolToVisibilityConverter}}">
            <TextBlock Text="± value" />
            <TextBox Text="{Binding FloatAddSubtractAmount, UpdateSourceTrigger=PropertyChanged}" />
        </StackPanel>
        <StackPanel Margin="0,6,0,0" Visibility="{Binding UsesFloatMultiplyMode, Converter={StaticResource BoolToVisibilityConverter}}">
            <TextBlock Text="Multiply factor" />
            <TextBox Text="{Binding FloatMultiplyFactor, UpdateSourceTrigger=PropertyChanged}" />
        </StackPanel>

        <!-- Toggle on / off -->
        <Grid Margin="0,6,0,0" Visibility="{Binding UsesFloatToggleValues, Converter={StaticResource BoolToVisibilityConverter}}">
            <Grid.ColumnDefinitions>
                <ColumnDefinition Width="*" />
                <ColumnDefinition Width="8" />
                <ColumnDefinition Width="*" />
            </Grid.ColumnDefinitions>
            <StackPanel Grid.Column="0">
                <TextBlock Text="On value" />
                <TextBox Text="{Binding FloatToggleOnValue, UpdateSourceTrigger=PropertyChanged}" />
            </StackPanel>
            <StackPanel Grid.Column="2">
                <TextBlock Text="Off value" />
                <TextBox Text="{Binding FloatToggleOffValue, UpdateSourceTrigger=PropertyChanged}" />
            </StackPanel>
        </Grid>

        <!-- Glitchy interval -->
        <StackPanel Margin="0,6,0,0" Visibility="{Binding UsesFloatGlitchyInterval, Converter={StaticResource BoolToVisibilityConverter}}">
            <TextBlock Text="Re-roll every (ms)" />
            <TextBox Text="{Binding FloatGlitchyIntervalMs, UpdateSourceTrigger=PropertyChanged}" />
        </StackPanel>

        <!-- Pulse seconds -->
        <StackPanel Margin="0,6,0,0" Visibility="{Binding UsesFloatPulseSeconds, Converter={StaticResource BoolToVisibilityConverter}}">
            <TextBlock Text="Pulse seconds" />
            <TextBox Text="{Binding FloatPulseSeconds, UpdateSourceTrigger=PropertyChanged}" />
        </StackPanel>

        <!-- Clamp mode (Add / Subtract / ± / Multiply) -->
        <StackPanel Margin="0,6,0,0" Visibility="{Binding UsesFloatClampMode, Converter={StaticResource BoolToVisibilityConverter}}">
            <TextBlock Text="Clamp result" />
            <ComboBox SelectedValue="{Binding FloatClampMode}" SelectedValuePath="Tag">
                <ComboBoxItem Content="No clamp" Tag="{x:Static models:FloatClampMode.None}" />
                <ComboBoxItem Content="Clamp 0..1" Tag="{x:Static models:FloatClampMode.ZeroToOne}" />
                <ComboBoxItem Content="Clamp to range" Tag="{x:Static models:FloatClampMode.MinToMax}" />
            </ComboBox>
        </StackPanel>
    </StackPanel>
</Border>
```

If the surrounding XAML already has a different `BoolToVisibilityConverter` or `ChipButtonStyle` resource name, search the file and use the existing names instead. Also add the `models` namespace if it is not already imported (it should already be imported for the other `models:` references in the file).

- [ ] **Step 10.3: Add click handlers in the code-behind**

Open `VrcTwitchOscBridge/AvatarSetsManagerWindow.xaml.cs`. Add the following private methods (anywhere in the class; group them next to the other `OnParameterValueXxxClicked` handlers):

```csharp
private void OnFloatModeSetClicked(object sender, RoutedEventArgs e)
    => SetSelectedRuleFloatMode(FloatActionMode.Set);
private void OnFloatModeRandomClicked(object sender, RoutedEventArgs e)
    => SetSelectedRuleFloatMode(FloatActionMode.Random);
private void OnFloatModeAddClicked(object sender, RoutedEventArgs e)
    => SetSelectedRuleFloatMode(FloatActionMode.Add);
private void OnFloatModeSubtractClicked(object sender, RoutedEventArgs e)
    => SetSelectedRuleFloatMode(FloatActionMode.Subtract);
private void OnFloatModeAddSubtractClicked(object sender, RoutedEventArgs e)
    => SetSelectedRuleFloatMode(FloatActionMode.AddSubtract);
private void OnFloatModeMultiplyClicked(object sender, RoutedEventArgs e)
    => SetSelectedRuleFloatMode(FloatActionMode.Multiply);
private void OnFloatModeToggleClicked(object sender, RoutedEventArgs e)
    => SetSelectedRuleFloatMode(FloatActionMode.Toggle);
private void OnFloatModeCycleClicked(object sender, RoutedEventArgs e)
    => SetSelectedRuleFloatMode(FloatActionMode.Cycle);
private void OnFloatModeGlitchyClicked(object sender, RoutedEventArgs e)
    => SetSelectedRuleFloatMode(FloatActionMode.Glitchy);
private void OnFloatModePulseClicked(object sender, RoutedEventArgs e)
    => SetSelectedRuleFloatMode(FloatActionMode.Pulse);

private void SetSelectedRuleFloatMode(FloatActionMode mode)
{
    if (DataContext is AvatarSetsManagerViewModel vm && vm.SelectedRule is TriggerRule rule)
        rule.FloatActionMode = mode;
}
```

Add the `using VrcTwitchOscBridge.Models;` directive if not already present. Match the existing `vm.SelectedRule` getter name; if the property is named differently, use that name.

- [ ] **Step 10.4: Build the app**

Run:
```bash
dotnet build "E:\!!!Program to work on\Proper Crystal Relay\VrcTwitchOscBridge\VrcTwitchOscBridge.csproj" --no-restore
```
Expected: build succeeds. Fix any XAML resource or namespace errors by referencing existing patterns in the file.

- [ ] **Step 10.5: Commit**

```bash
git add "VrcTwitchOscBridge/AvatarSetsManagerWindow.xaml" "VrcTwitchOscBridge/AvatarSetsManagerWindow.xaml.cs"
git commit -m "feat(float-modes): add float action mode section to Avatar Sets editor"
```

---

## Task 11: Add the Float Action Mode section to the full editor

**Files:**
- Modify: `VrcTwitchOscBridge/UserControls/AvatarSwapRuleEditorControl.xaml` (insert a new section near the existing Float/String branch at line 1395)
- Modify: `VrcTwitchOscBridge/UserControls/AvatarSwapRuleEditorControl.xaml.cs` (if the code-behind has a similar `OnParameterValueXxxClicked` pattern, mirror the same handlers; otherwise the binding-driven `ComboBox` is enough)

- [ ] **Step 11.1: Read the existing full-editor XAML structure**

Open `VrcTwitchOscBridge/UserControls/AvatarSwapRuleEditorControl.xaml`. Locate the Float/String branch:
- `UsesDirectInstantValue` ("Send This Value") at line 1395
- `UsesDirectTimedValues` ("When Triggered, Send This Value" / "After Active Time Ends, Send This Value") at line 1401

The new section inserts **after** the `UsesDirectTimedValues` block and **before** the SetTrigger branch (`UsesSetTrigger` at line 1421).

- [ ] **Step 11.2: Add the XAML block**

Insert the following XAML block at the right spot:

```xml
<!-- Float Action Mode section (visible only when ParameterType == Float) -->
<Border BorderBrush="#44FFFFFF" BorderThickness="1" CornerRadius="6" Padding="8" Margin="0,4,0,4"
        Visibility="{Binding UsesFloatActionMode, Converter={StaticResource BoolToVisibilityConverter}}">
    <StackPanel>
        <TextBlock Text="Float Action Mode" FontWeight="SemiBold" Margin="0,0,0,4" />
        <ComboBox SelectedValue="{Binding FloatActionMode}" SelectedValuePath="Tag">
            <ComboBoxItem Content="Set" Tag="{x:Static models:FloatActionMode.Set}" />
            <ComboBoxItem Content="Random" Tag="{x:Static models:FloatActionMode.Random}" />
            <ComboBoxItem Content="Add" Tag="{x:Static models:FloatActionMode.Add}" />
            <ComboBoxItem Content="Subtract" Tag="{x:Static models:FloatActionMode.Subtract}" />
            <ComboBoxItem Content="Add/Subtract (±)" Tag="{x:Static models:FloatActionMode.AddSubtract}" />
            <ComboBoxItem Content="Multiply" Tag="{x:Static models:FloatActionMode.Multiply}" />
            <ComboBoxItem Content="Toggle" Tag="{x:Static models:FloatActionMode.Toggle}" />
            <ComboBoxItem Content="Cycle" Tag="{x:Static models:FloatActionMode.Cycle}" />
            <ComboBoxItem Content="Glitchy" Tag="{x:Static models:FloatActionMode.Glitchy}" />
            <ComboBoxItem Content="Pulse" Tag="{x:Static models:FloatActionMode.Pulse}" />
        </ComboBox>

        <Grid Margin="0,6,0,0" Visibility="{Binding UsesFloatRangeInputs, Converter={StaticResource BoolToVisibilityConverter}}">
            <Grid.ColumnDefinitions>
                <ColumnDefinition Width="*" />
                <ColumnDefinition Width="8" />
                <ColumnDefinition Width="*" />
            </Grid.ColumnDefinitions>
            <StackPanel Grid.Column="0">
                <TextBlock Text="Min" />
                <TextBox Text="{Binding FloatRangeMin, UpdateSourceTrigger=PropertyChanged}" />
            </StackPanel>
            <StackPanel Grid.Column="2">
                <TextBlock Text="Max" />
                <TextBox Text="{Binding FloatRangeMax, UpdateSourceTrigger=PropertyChanged}" />
            </StackPanel>
        </Grid>

        <StackPanel Margin="0,6,0,0" Visibility="{Binding UsesFloatCycleStep, Converter={StaticResource BoolToVisibilityConverter}}">
            <TextBlock Text="Step" />
            <TextBox Text="{Binding FloatCycleStep, UpdateSourceTrigger=PropertyChanged}" />
        </StackPanel>

        <StackPanel Margin="0,6,0,0" Visibility="{Binding UsesFloatAddMode, Converter={StaticResource BoolToVisibilityConverter}}">
            <TextBlock Text="Add amount" />
            <TextBox Text="{Binding FloatAddAmount, UpdateSourceTrigger=PropertyChanged}" />
        </StackPanel>
        <StackPanel Margin="0,6,0,0" Visibility="{Binding UsesFloatSubtractMode, Converter={StaticResource BoolToVisibilityConverter}}">
            <TextBlock Text="Subtract amount" />
            <TextBox Text="{Binding FloatSubtractAmount, UpdateSourceTrigger=PropertyChanged}" />
        </StackPanel>
        <StackPanel Margin="0,6,0,0" Visibility="{Binding UsesFloatAddSubtractMode, Converter={StaticResource BoolToVisibilityConverter}}">
            <TextBlock Text="± value" />
            <TextBox Text="{Binding FloatAddSubtractAmount, UpdateSourceTrigger=PropertyChanged}" />
        </StackPanel>
        <StackPanel Margin="0,6,0,0" Visibility="{Binding UsesFloatMultiplyMode, Converter={StaticResource BoolToVisibilityConverter}}">
            <TextBlock Text="Multiply factor" />
            <TextBox Text="{Binding FloatMultiplyFactor, UpdateSourceTrigger=PropertyChanged}" />
        </StackPanel>

        <Grid Margin="0,6,0,0" Visibility="{Binding UsesFloatToggleValues, Converter={StaticResource BoolToVisibilityConverter}}">
            <Grid.ColumnDefinitions>
                <ColumnDefinition Width="*" />
                <ColumnDefinition Width="8" />
                <ColumnDefinition Width="*" />
            </Grid.ColumnDefinitions>
            <StackPanel Grid.Column="0">
                <TextBlock Text="On value" />
                <TextBox Text="{Binding FloatToggleOnValue, UpdateSourceTrigger=PropertyChanged}" />
            </StackPanel>
            <StackPanel Grid.Column="2">
                <TextBlock Text="Off value" />
                <TextBox Text="{Binding FloatToggleOffValue, UpdateSourceTrigger=PropertyChanged}" />
            </StackPanel>
        </Grid>

        <StackPanel Margin="0,6,0,0" Visibility="{Binding UsesFloatGlitchyInterval, Converter={StaticResource BoolToVisibilityConverter}}">
            <TextBlock Text="Re-roll every (ms)" />
            <TextBox Text="{Binding FloatGlitchyIntervalMs, UpdateSourceTrigger=PropertyChanged}" />
        </StackPanel>

        <StackPanel Margin="0,6,0,0" Visibility="{Binding UsesFloatPulseSeconds, Converter={StaticResource BoolToVisibilityConverter}}">
            <TextBlock Text="Pulse seconds" />
            <TextBox Text="{Binding FloatPulseSeconds, UpdateSourceTrigger=PropertyChanged}" />
        </StackPanel>

        <StackPanel Margin="0,6,0,0" Visibility="{Binding UsesFloatClampMode, Converter={StaticResource BoolToVisibilityConverter}}">
            <TextBlock Text="Clamp result" />
            <ComboBox SelectedValue="{Binding FloatClampMode}" SelectedValuePath="Tag">
                <ComboBoxItem Content="No clamp" Tag="{x:Static models:FloatClampMode.None}" />
                <ComboBoxItem Content="Clamp 0..1" Tag="{x:Static models:FloatClampMode.ZeroToOne}" />
                <ComboBoxItem Content="Clamp to range" Tag="{x:Static models:FloatClampMode.MinToMax}" />
            </ComboBox>
        </StackPanel>
    </StackPanel>
</Border>
```

- [ ] **Step 11.3: Build the app**

Run:
```bash
dotnet build "E:\!!!Program to work on\Proper Crystal Relay\VrcTwitchOscBridge\VrcTwitchOscBridge.csproj" --no-restore
```
Expected: build succeeds.

- [ ] **Step 11.4: Commit**

```bash
git add "VrcTwitchOscBridge/UserControls/AvatarSwapRuleEditorControl.xaml"
git commit -m "feat(float-modes): add float action mode section to Avatar Swap rule editor"
```

---

## Task 12: Add localization keys

**Files:**
- Modify: `VrcTwitchOscBridge/Resources/Localization/en-US.json` (add en-US source keys)
- Modify: 15 non-English locale JSON files (mirror the new keys with translated values)

The new keys for the float action mode UI labels:

```json
"FloatActionModeHeader": "Float Action Mode",
"FloatActionModeSet": "Set",
"FloatActionModeRandom": "Random",
"FloatActionModeAdd": "Add",
"FloatActionModeSubtract": "Subtract",
"FloatActionModeAddSubtract": "Add/Subtract (±)",
"FloatActionModeMultiply": "Multiply",
"FloatActionModeToggle": "Toggle",
"FloatActionModeCycle": "Cycle",
"FloatActionModeGlitchy": "Glitchy",
"FloatActionModePulse": "Pulse",
"FloatActionModeRangeMin": "Min",
"FloatActionModeRangeMax": "Max",
"FloatActionModeCycleStep": "Step",
"FloatActionModeAddAmount": "Add amount",
"FloatActionModeSubtractAmount": "Subtract amount",
"FloatActionModeAddSubtractValue": "± value",
"FloatActionModeMultiplyFactor": "Multiply factor",
"FloatActionModeToggleOn": "On value",
"FloatActionModeToggleOff": "Off value",
"FloatActionModeGlitchyInterval": "Re-roll every (ms)",
"FloatActionModePulseSeconds": "Pulse seconds",
"FloatActionModeClampNone": "No clamp",
"FloatActionModeClampZeroToOne": "Clamp 0..1",
"FloatActionModeClampMinToMax": "Clamp to range"
```

- [ ] **Step 12.1: Add en-US source keys**

Open `VrcTwitchOscBridge/Resources/Localization/en-US.json`. Find the existing `LocalizationTriggerRule` (or similar) object and add the 25 new keys inside it, using the same indentation style as the surrounding entries.

- [ ] **Step 12.2: Update the XAML to use localization keys**

Replace the hard-coded `Text="..."` values in Tasks 10 and 11's XAML with bindings to the localization keys. Pattern:

```xml
<TextBlock Text="{Loc FloatActionModeHeader}" />
```

Use whatever the existing `Loc` markup extension / binding pattern is in this codebase. Search the file for an existing localized label (e.g. `Loc TriggerRule_ActiveTime`) and copy that exact pattern.

- [ ] **Step 12.3: Translate the 15 non-English locale files**

For each of the following files, add the same 25 keys with natural translations per the localization translation-quality rules in `AGENTS.md` (informal register, English brand terms kept, exact placeholder preservation):

- `VrcTwitchOscBridge/Resources/Localization/de-DE.json`
- `VrcTwitchOscBridge/Resources/Localization/es-ES.json`
- `VrcTwitchOscBridge/Resources/Localization/fr-FR.json`
- `VrcTwitchOscBridge/Resources/Localization/it-IT.json`
- `VrcTwitchOscBridge/Resources/Localization/ja-JP.json`
- `VrcTwitchOscBridge/Resources/Localization/ko-KR.json`
- `VrcTwitchOscBridge/Resources/Localization/pl-PL.json`
- `VrcTwitchOscBridge/Resources/Localization/pt-BR.json`
- `VrcTwitchOscBridge/Resources/Localization/ru-RU.json`
- `VrcTwitchOscBridge/Resources/Localization/sv-SE.json`
- `VrcTwitchOscBridge/Resources/Localization/th-TH.json`
- `VrcTwitchOscBridge/Resources/Localization/zh-CN.json`
- `VrcTwitchOscBridge/Resources/Localization/zh-TW.json`
- (plus `en-US.extra.json` and any other `.extra.json` overrides if present)

Brand terms to keep in English across all locales: `Bits`, `Subs`, `OSC`, `OSCQuery`, `VRChat`, `Twitch`, `Crystal Relay`, `StreamElements`, `Streamlabs`, `Ko-fi`, `VRC:`, `!world`, `Cheer`.

Sample `de-DE` translations (informal `du`):

```json
"FloatActionModeHeader": "Float-Aktionsmodus",
"FloatActionModeSet": "Setzen",
"FloatActionModeRandom": "Zufällig",
"FloatActionModeAdd": "Addieren",
"FloatActionModeSubtract": "Subtrahieren",
"FloatActionModeAddSubtract": "Addieren/Subtrahieren (±)",
"FloatActionModeMultiply": "Multiplizieren",
"FloatActionModeToggle": "Umschalten",
"FloatActionModeCycle": "Durchlaufen",
"FloatActionModeGlitchy": "Glitchy",
"FloatActionModePulse": "Puls",
"FloatActionModeRangeMin": "Min",
"FloatActionModeRangeMax": "Max",
"FloatActionModeCycleStep": "Schritt",
"FloatActionModeAddAmount": "Addierbetrag",
"FloatActionModeSubtractAmount": "Subtrahierbetrag",
"FloatActionModeAddSubtractValue": "± Wert",
"FloatActionModeMultiplyFactor": "Multiplikator",
"FloatActionModeToggleOn": "An-Wert",
"FloatActionModeToggleOff": "Aus-Wert",
"FloatActionModeGlitchyInterval": "Neu würfeln alle (ms)",
"FloatActionModePulseSeconds": "Pulsdauer (Sekunden)",
"FloatActionModeClampNone": "Kein Clamp",
"FloatActionModeClampZeroToOne": "Clamp 0..1",
"FloatActionModeClampMinToMax": "Auf Bereich clampen"
```

Use the existing translation tone and vocabulary from the same file. When in doubt, keep terminology consistent with how the file already translates other streaming/gaming words.

- [ ] **Step 12.4: Build the app**

Run:
```bash
dotnet build "E:\!!!Program to work on\Proper Crystal Relay\VrcTwitchOscBridge\VrcTwitchOscBridge.csproj" --no-restore
```
Expected: build succeeds.

- [ ] **Step 12.5: Run the localization audit**

Run the project's localization audit per `AGENTS.md` ("Run the localization audit after adding or changing UI text"):

```bash
& "E:\!!!Program to work on\Proper Crystal Relay\LocalizationAudit\run-audit.ps1"
```

(If the script path differs, find the actual entry point under `LocalizationAudit/`. The audit must report zero missing keys and zero empty values for the new keys.)

- [ ] **Step 12.6: Commit**

```bash
git add "VrcTwitchOscBridge/Resources/Localization/"
git commit -m "feat(float-modes): localize float action mode UI labels across 16 locales"
```

---

## Task 13: Final verification — build, tests, audit, and manual smoke

**Files:** none modified (verification only)

- [ ] **Step 13.1: Full build**

```bash
dotnet build "E:\!!!Program to work on\Proper Crystal Relay\VrcTwitchOscBridge\VrcTwitchOscBridge.csproj" --no-restore
```
Expected: build succeeds with no errors.

- [ ] **Step 13.2: Full test suite**

```bash
dotnet test "E:\!!!Program to work on\Proper Crystal Relay\VrcTwitchOscBridge.Tests\VrcTwitchOscBridge.Tests.csproj"
```
Expected: all existing tests still pass; the new tests from Tasks 1-6 pass:
- `FloatActionModeTests`: 2
- `FloatClampModeTests`: 2
- `TriggerRuleFloatModeFieldsTests`: 12
- `TriggerRuleFloatModeUsesTests`: 8
- `FloatActionDispatchTests`: 15
- `TriggerRuleFloatModePersistenceTests`: 3

Total: 42 new tests; all pre-existing tests still passing.

- [ ] **Step 13.3: Localization audit**

```bash
& "E:\!!!Program to work on\Proper Crystal Relay\LocalizationAudit\run-audit.ps1"
```
Expected: zero missing keys, zero empty values for the 25 new keys across all 16 locales.

- [ ] **Step 13.4: Manual smoke test in the debug build**

Launch the debug build:

```bash
"E:\!!!Program to work on\Proper Crystal Relay\Launch-Crystal-Relay-Debug.bat"
```

Walk through this checklist in the running app:

1. Open Avatar Sets manager. Add a new rule, set `Parameter Type = Float`. Confirm the "Float Action Mode" section appears.
2. Click each of the 10 mode buttons in turn. For each, confirm only the relevant sub-fields appear (e.g. Random shows Min/Max, Toggle shows On/Off, Pulse shows Pulse seconds).
3. Connect to VRChat (or use the test OSC echo if available). Pick a real float parameter from the avatar.
4. For **Set**: fire the rule, confirm the float value matches the typed value. Confirm the restore value arrives at the end of Active Time.
5. For **Random**: fire the rule several times. Confirm each fire lands in the Min/Max range.
6. For **Add / Subtract / ± / Multiply**: set the current float to a known value (e.g. via another Set fire), then fire the Add rule. Confirm the result is the current value plus the amount, clamped per the chosen Clamp Mode.
7. For **Toggle**: set the float to the On value, fire Toggle, confirm it goes to Off. Fire again, confirm it goes to On. Test with a non-On initial value (e.g. 0.5) and confirm it goes to On.
8. For **Cycle**: fire several times. Confirm the value increments by Step and wraps when it exceeds Max.
9. For **Glitchy**: fire with Active Time = 5s. Watch the float for 5 seconds — it should re-roll every 200ms (or whatever interval is set). At the end of Active Time, it should restore to the ResetValue.
10. For **Pulse**: set the rule to fire, confirm the float jumps to the ParameterValue immediately, then jumps back to ResetValue after Pulse Seconds (not after Active Time).
11. Switch `Parameter Type` between Bool, Int, Float, String. Confirm the "Float Action Mode" section only shows up for Float and that selecting Float again restores the last-selected mode + field values.
12. Close the app, reopen it. Confirm all the float mode values persisted through `crystal-relay.rules.json` and loaded back correctly.

- [ ] **Step 13.5: Update housekeeping**

Per `AGENTS.md`:
- Update `CHANGELOG.txt` under the active `v3.1.9 beta N` section with a short, user-facing bullet list of the new float modes.
- Update `RELEASE-CHANGE-RECORD.txt` under `Added` for each new mode.
- Do not bump the version — keep the active development build as `v3.1.9` beta4 until the user asks for a new package.

- [ ] **Step 13.6: Final commit (housekeeping only)**

```bash
git add "CHANGELOG.txt" "RELEASE-CHANGE-RECORD.txt"
git commit -m "docs(float-modes): record float action modes in changelog and release notes"
```

---

## Spec coverage check

| Spec section | Covered by |
|---|---|
| New enum `FloatActionMode` | Task 1 |
| New enum `FloatClampMode` | Task 2 |
| New fields on `TriggerRule` | Task 3 |
| Computed `UsesXxx` properties | Task 4 |
| Pure `FloatActionDispatch` value-calculator | Task 5 |
| `PersistedTriggerRule` extension | Task 6 |
| `ToRule` migration for old rules | Task 6 |
| `ResolveFloatActionAsync` dispatch branch | Task 7 |
| Glitchy session with re-roll loop | Task 8 |
| Pulse scheduling with cancellation | Tasks 7, 9 |
| Compact editor UI section | Task 10 |
| Full editor UI section | Task 11 |
| Localization keys for all 16 locales | Task 12 |
| Verification (build, tests, audit, manual smoke) | Task 13 |
