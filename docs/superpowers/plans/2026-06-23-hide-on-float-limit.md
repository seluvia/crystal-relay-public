# Hide Avatar Set Float Reward on Limit Reached — Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Add two opt-in checkboxes to Avatar Set float rules that hide the Twitch reward when the float value reaches the configured max or min, and re-show it when Active Time expires.

**Architecture:** Mirror the proven Avatar Scaling `HideRewardWhenMaximumHeightReached` pattern. Add bool properties to `TriggerRule`, track limit-reached state on `ActiveFloatRedeemSessionState`, raise a dedicated `FloatLimitStatusChanged` event that drives a debounced, fingerprint-protected managed reward sync. The sync's `DesiredEnabled` gate flips the Twitch reward's `isEnabled` off while at the limit, and back on when the session ends.

**Tech Stack:** C# / .NET 10 / WPF + XAML / PowerShell for localization audit

**Design spec:** `docs/superpowers/specs/2026-06-23-hide-on-float-limit-design.md`

---

## File Structure

| File | Responsibility | Action |
|---|---|---|
| `VrcTwitchOscBridge\Models\TriggerRule.cs` | Data model: two new bool properties + `UsesFloatHideOnLimit` computed helper | Modify |
| `VrcTwitchOscBridge\Services\FloatLimitDetection.cs` | Pure static limit-detection logic with hysteresis (testable without BridgeCoordinator) | Create |
| `VrcTwitchOscBridge\Services\SettingsStore.cs` | Serialize/deserialize the two new bools in `PersistedTriggerRule` | Modify |
| `VrcTwitchOscBridge\Services\BridgeCoordinator.cs` | Session state fields, public accessor, event, lifecycle hooks | Modify |
| `VrcTwitchOscBridge\ViewModels\MainWindowViewModel.cs` | Sync reason, passive list, state cache, handler, target builder gate, logging | Modify |
| `VrcTwitchOscBridge\AvatarSetsManagerWindow.xaml` | Two checkboxes in the float action mode card | Modify |
| `VrcTwitchOscBridge\Resources\Localization\*.extra.json` (15 files) | Two new keys + two tooltip keys per language | Modify |
| `VrcTwitchOscBridge.Tests\TriggerRuleFloatModeFieldsTests.cs` | Test new properties + `UsesFloatHideOnLimit` | Modify |
| `VrcTwitchOscBridge.Tests\TriggerRuleFloatModePersistenceTests.cs` | Test serialization round-trip + safe defaults | Modify |
| `VrcTwitchOscBridge.Tests\FloatLimitDetectionTests.cs` | Test the pure limit-detection helper | Create |
| `VrcTwitchOscBridge.Tests\AvatarSetsManagerWindowXamlTests.cs` | Test XAML contains the new checkboxes | Modify |

---

### Task 1: TriggerRule Data Model

**Files:**
- Modify: `VrcTwitchOscBridge\Models\TriggerRule.cs:110` (add fields after `floatClampMode`)
- Modify: `VrcTwitchOscBridge\Models\TriggerRule.cs:984` (add properties after `FloatClampMode`)
- Modify: `VrcTwitchOscBridge\Models\TriggerRule.cs:1768` (add helper after `UsesFloatClampMode`)
- Test: `VrcTwitchOscBridge.Tests\TriggerRuleFloatModeFieldsTests.cs`

- [ ] **Step 1: Write failing tests for new properties and `UsesFloatHideOnLimit`**

Add to `VrcTwitchOscBridge.Tests\TriggerRuleFloatModeFieldsTests.cs`, at the end of the class (before the closing `}`):

```csharp
[Fact]
public void HideRewardWhenFloatLimit_DefaultsToFalse()
{
    var rule = new TriggerRule();
    Assert.False(rule.HideRewardWhenFloatMaxReached);
    Assert.False(rule.HideRewardWhenFloatMinReached);
}

[Fact]
public void HideRewardWhenFloatLimit_RoundTripsThroughPublicProperties()
{
    var rule = new TriggerRule
    {
        HideRewardWhenFloatMaxReached = true,
        HideRewardWhenFloatMinReached = true,
    };
    Assert.True(rule.HideRewardWhenFloatMaxReached);
    Assert.True(rule.HideRewardWhenFloatMinReached);
}

[Fact]
public void UsesFloatHideOnLimit_TrueForManagedTimedCumulativeFloat()
{
    var rule = new TriggerRule
    {
        ParameterType = OscParameterType.Float,
        RewardSyncMode = TwitchRewardSyncMode.CreateOrManage,
        DurationSeconds = 10,
        FloatActionMode = FloatActionMode.Add,
    };
    Assert.True(rule.UsesFloatHideOnLimit);

    rule.FloatActionMode = FloatActionMode.Subtract;
    Assert.True(rule.UsesFloatHideOnLimit);

    rule.FloatActionMode = FloatActionMode.AddSubtract;
    Assert.True(rule.UsesFloatHideOnLimit);

    rule.FloatActionMode = FloatActionMode.Multiply;
    Assert.True(rule.UsesFloatHideOnLimit);
}

[Fact]
public void UsesFloatHideOnLimit_FalseForLinkExisting()
{
    var rule = new TriggerRule
    {
        ParameterType = OscParameterType.Float,
        RewardSyncMode = TwitchRewardSyncMode.LinkExisting,
        DurationSeconds = 10,
        FloatActionMode = FloatActionMode.Add,
    };
    Assert.False(rule.UsesFloatHideOnLimit);
}

[Fact]
public void UsesFloatHideOnLimit_FalseWhenInstant()
{
    var rule = new TriggerRule
    {
        ParameterType = OscParameterType.Float,
        RewardSyncMode = TwitchRewardSyncMode.CreateOrManage,
        DurationSeconds = 0,
        FloatActionMode = FloatActionMode.Add,
    };
    Assert.False(rule.UsesFloatHideOnLimit);
}

[Fact]
public void UsesFloatHideOnLimit_FalseForNonCumulativeModes()
{
    var rule = new TriggerRule
    {
        ParameterType = OscParameterType.Float,
        RewardSyncMode = TwitchRewardSyncMode.CreateOrManage,
        DurationSeconds = 10,
    };

    foreach (var mode in new[] { FloatActionMode.Set, FloatActionMode.Random,
                                 FloatActionMode.Toggle, FloatActionMode.Cycle,
                                 FloatActionMode.Glitchy, FloatActionMode.Pulse })
    {
        rule.FloatActionMode = mode;
        Assert.False(rule.UsesFloatHideOnLimit, $"{mode} should not show hide-on-limit.");
    }
}
```

- [ ] **Step 2: Run tests to verify they fail**

Run: `dotnet test "E:\!!!Program to work on\Proper Crystal Relay\VrcTwitchOscBridge.Tests\VrcTwitchOscBridge.Tests.csproj" --no-restore -f net10.0-windows --filter "FullyQualifiedName~HideRewardWhenFloatLimit|FullyQualifiedName~UsesFloatHideOnLimit"`
Expected: FAIL with compile errors (`HideRewardWhenFloatMaxReached` not found, `UsesFloatHideOnLimit` not found)

- [ ] **Step 3: Add the backing fields**

In `VrcTwitchOscBridge\Models\TriggerRule.cs`, after line 110 (`private FloatClampMode floatClampMode = FloatClampMode.ZeroToOne;`), add:

```csharp
    private bool hideRewardWhenFloatMaxReached;
    private bool hideRewardWhenFloatMinReached;
```

- [ ] **Step 4: Add the public properties**

In `VrcTwitchOscBridge\Models\TriggerRule.cs`, after the `FloatClampMode` property closing brace at line 984, add:

```csharp
    public bool HideRewardWhenFloatMaxReached
    {
        get => hideRewardWhenFloatMaxReached;
        set => SetProperty(ref hideRewardWhenFloatMaxReached, value);
    }

    public bool HideRewardWhenFloatMinReached
    {
        get => hideRewardWhenFloatMinReached;
        set => SetProperty(ref hideRewardWhenFloatMinReached, value);
    }
```

- [ ] **Step 5: Add the `UsesFloatHideOnLimit` computed helper**

In `VrcTwitchOscBridge\Models\TriggerRule.cs`, after `UsesFloatClampMode` (line 1768), add:

```csharp
    public bool UsesFloatHideOnLimit => UsesFloatActionMode
        && RewardSyncMode == TwitchRewardSyncMode.CreateOrManage
        && DurationSeconds > 0
        && (FloatActionMode == FloatActionMode.Add
            || FloatActionMode == FloatActionMode.Subtract
            || FloatActionMode == FloatActionMode.AddSubtract
            || FloatActionMode == FloatActionMode.Multiply);
```

- [ ] **Step 6: Run tests to verify they pass**

Run: `dotnet test "E:\!!!Program to work on\Proper Crystal Relay\VrcTwitchOscBridge.Tests\VrcTwitchOscBridge.Tests.csproj" --no-restore -f net10.0-windows --filter "FullyQualifiedName~HideRewardWhenFloatLimit|FullyQualifiedName~UsesFloatHideOnLimit"`
Expected: PASS (all 6 new tests)

- [ ] **Step 7: Commit**

```bash
git add VrcTwitchOscBridge/Models/TriggerRule.cs VrcTwitchOscBridge.Tests/TriggerRuleFloatModeFieldsTests.cs
git commit -m "Add HideRewardWhenFloatMax/MinReached properties to TriggerRule"
```

---

### Task 2: Settings Serialization

**Files:**
- Modify: `VrcTwitchOscBridge\Services\SettingsStore.cs:1063` (add to `ToPersistedRule` copy block)
- Modify: `VrcTwitchOscBridge\Services\SettingsStore.cs:1369` (add to `ToRule` read block)
- Modify: `VrcTwitchOscBridge\Services\SettingsStore.cs:3374` (add to `PersistedTriggerRule` DTO)
- Test: `VrcTwitchOscBridge.Tests\TriggerRuleFloatModePersistenceTests.cs`

- [ ] **Step 1: Write failing tests for serialization**

Add to `VrcTwitchOscBridge.Tests\TriggerRuleFloatModePersistenceTests.cs`, inside the `ToRule_MissingNewFields_AppliesSafeDefaults` method, after the existing assertions (after line 39 `Assert.Equal(FloatClampMode.ZeroToOne, rule.FloatClampMode);`):

```csharp
        Assert.False(rule.HideRewardWhenFloatMaxReached);
        Assert.False(rule.HideRewardWhenFloatMinReached);
```

Then add a new test method after `RoundTrip_AllNewFieldsPreserved` (after line 77):

```csharp
    [Fact]
    public void RoundTrip_HideRewardWhenFloatLimit_Preserved()
    {
        var original = new TriggerRule
        {
            ParameterType = OscParameterType.Float,
            FloatActionMode = FloatActionMode.Add,
            DurationSeconds = 10,
            HideRewardWhenFloatMaxReached = true,
            HideRewardWhenFloatMinReached = true,
        };
        var persisted = ToPersistedViaReflection(original);
        var roundTripped = SettingsStore.ToRule(persisted);
        Assert.True(roundTripped.HideRewardWhenFloatMaxReached);
        Assert.True(roundTripped.HideRewardWhenFloatMinReached);
    }
```

- [ ] **Step 2: Run tests to verify they fail**

Run: `dotnet test "E:\!!!Program to work on\Proper Crystal Relay\VrcTwitchOscBridge.Tests\VrcTwitchOscBridge.Tests.csproj" --no-restore -f net10.0-windows --filter "FullyQualifiedName~RoundTrip_HideRewardWhenFloatLimit|FullyQualifiedName~ToRule_MissingNewFields_AppliesSafeDefaults"`
Expected: FAIL with compile errors (`HideRewardWhenFloatMaxReached` not on `PersistedTriggerRule`)

- [ ] **Step 3: Add the two properties to the `PersistedTriggerRule` DTO**

In `VrcTwitchOscBridge\Services\SettingsStore.cs`, after line 3374 (`public FloatClampMode FloatClampMode { get; set; } = FloatClampMode.ZeroToOne;`), add:

```csharp
        public bool HideRewardWhenFloatMaxReached { get; set; }

        public bool HideRewardWhenFloatMinReached { get; set; }
```

- [ ] **Step 4: Add the copy in `ToPersistedRule`**

In `VrcTwitchOscBridge\Services\SettingsStore.cs`, after line 1063 (`FloatClampMode = rule.FloatClampMode,`), add:

```csharp
            HideRewardWhenFloatMaxReached = rule.HideRewardWhenFloatMaxReached,
            HideRewardWhenFloatMinReached = rule.HideRewardWhenFloatMinReached,
```

- [ ] **Step 5: Add the read in `ToRule`**

In `VrcTwitchOscBridge\Services\SettingsStore.cs`, after line 1369 (`FloatClampMode = Enum.IsDefined(rule.FloatClampMode) ? rule.FloatClampMode : FloatClampMode.ZeroToOne,`), add:

```csharp
            HideRewardWhenFloatMaxReached = rule.HideRewardWhenFloatMaxReached,
            HideRewardWhenFloatMinReached = rule.HideRewardWhenFloatMinReached,
```

- [ ] **Step 6: Run tests to verify they pass**

Run: `dotnet test "E:\!!!Program to work on\Proper Crystal Relay\VrcTwitchOscBridge.Tests\VrcTwitchOscBridge.Tests.csproj" --no-restore -f net10.0-windows --filter "FullyQualifiedName~RoundTrip_HideRewardWhenFloatLimit|FullyQualifiedName~ToRule_MissingNewFields_AppliesSafeDefaults"`
Expected: PASS

- [ ] **Step 7: Commit**

```bash
git add VrcTwitchOscBridge/Services/SettingsStore.cs VrcTwitchOscBridge.Tests/TriggerRuleFloatModePersistenceTests.cs
git commit -m "Serialize HideRewardWhenFloatMax/MinReached in PersistedTriggerRule"
```

---

### Task 3: FloatLimitDetection Static Helper

**Files:**
- Create: `VrcTwitchOscBridge\Services\FloatLimitDetection.cs`
- Test: `VrcTwitchOscBridge.Tests\FloatLimitDetectionTests.cs`

- [ ] **Step 1: Write failing tests for the limit detection helper**

Create `VrcTwitchOscBridge.Tests\FloatLimitDetectionTests.cs`:

```csharp
using VrcTwitchOscBridge.Models;
using VrcTwitchOscBridge.Services;
using Xunit;

namespace VrcTwitchOscBridge.Tests;

public sealed class FloatLimitDetectionTests
{
    private static TriggerRule Rule(FloatActionMode mode, FloatClampMode clamp,
        double min = 0.0, double max = 1.0) => new()
    {
        ParameterType = OscParameterType.Float,
        FloatActionMode = mode,
        FloatClampMode = clamp,
        FloatRangeMin = min,
        FloatRangeMax = max,
    };

    [Fact]
    public void ZeroToOne_AtMax_ReportsMaxReached()
    {
        var rule = Rule(FloatActionMode.Add, FloatClampMode.ZeroToOne);
        var (max, min) = FloatLimitDetection.ComputeLimitState(rule, 1.0, previousMaxReached: false);
        Assert.True(max);
        Assert.False(min);
    }

    [Fact]
    public void ZeroToOne_AtMin_ReportsMinReached()
    {
        var rule = Rule(FloatActionMode.Add, FloatClampMode.ZeroToOne);
        var (max, min) = FloatLimitDetection.ComputeLimitState(rule, 0.0, previousMaxReached: false);
        Assert.False(max);
        Assert.True(min);
    }

    [Fact]
    public void ZeroToOne_AtMidpoint_ReportsNeither()
    {
        var rule = Rule(FloatActionMode.Add, FloatClampMode.ZeroToOne);
        var (max, min) = FloatLimitDetection.ComputeLimitState(rule, 0.5, previousMaxReached: false);
        Assert.False(max);
        Assert.False(min);
    }

    [Fact]
    public void MinToMax_AtMax_ReportsMaxReached()
    {
        var rule = Rule(FloatActionMode.Add, FloatClampMode.MinToMax, min: 0.2, max: 0.8);
        var (max, min) = FloatLimitDetection.ComputeLimitState(rule, 0.8, previousMaxReached: false);
        Assert.True(max);
        Assert.False(min);
    }

    [Fact]
    public void MinToMax_AtMin_ReportsMinReached()
    {
        var rule = Rule(FloatActionMode.Add, FloatClampMode.MinToMax, min: 0.2, max: 0.8);
        var (max, min) = FloatLimitDetection.ComputeLimitState(rule, 0.2, previousMaxReached: false);
        Assert.False(max);
        Assert.True(min);
    }

    [Fact]
    public void None_NeverReportsLimitReached()
    {
        var rule = Rule(FloatActionMode.Add, FloatClampMode.None);
        var (max, min) = FloatLimitDetection.ComputeLimitState(rule, 1.0, previousMaxReached: false);
        Assert.False(max);
        Assert.False(min);
    }

    [Fact]
    public void Hysteresis_StaysAtMaxUntilBelowReleaseTolerance()
    {
        var rule = Rule(FloatActionMode.Add, FloatClampMode.ZeroToOne);
        // At max
        var (max1, _) = FloatLimitDetection.ComputeLimitState(rule, 1.0, previousMaxReached: false);
        Assert.True(max1);
        // Slightly below max — hysteresis keeps it true
        var (max2, _) = FloatLimitDetection.ComputeLimitState(rule, 0.99999, previousMaxReached: true);
        Assert.True(max2);
        // Well below release tolerance — clears
        var (max3, _) = FloatLimitDetection.ComputeLimitState(rule, 0.5, previousMaxReached: true);
        Assert.False(max3);
    }

    [Fact]
    public void Hysteresis_StaysAtMinUntilAboveReleaseTolerance()
    {
        var rule = Rule(FloatActionMode.Subtract, FloatClampMode.ZeroToOne);
        // At min
        var (_, min1) = FloatLimitDetection.ComputeLimitState(rule, 0.0, previousMinReached: false);
        Assert.True(min1);
        // Slightly above min — hysteresis keeps it true
        var (_, min2) = FloatLimitDetection.ComputeLimitState(rule, 0.00001, previousMinReached: true);
        Assert.True(min2);
        // Well above release tolerance — clears
        var (_, min3) = FloatLimitDetection.ComputeLimitState(rule, 0.5, previousMinReached: true);
        Assert.False(min3);
    }

    [Fact]
    public void NonCumulativeMode_AlwaysReturnsFalse()
    {
        foreach (var mode in new[] { FloatActionMode.Set, FloatActionMode.Random,
                                     FloatActionMode.Toggle, FloatActionMode.Cycle,
                                     FloatActionMode.Glitchy, FloatActionMode.Pulse })
        {
            var rule = Rule(mode, FloatClampMode.ZeroToOne);
            var (max, min) = FloatLimitDetection.ComputeLimitState(rule, 1.0, previousMaxReached: false);
            Assert.False(max, $"{mode} should not report max reached.");
            Assert.False(min, $"{mode} should not report min reached.");
        }
    }

    [Fact]
    public void FeatureDisabled_WhenBothFlagsOff_ReturnsFalse()
    {
        var rule = Rule(FloatActionMode.Add, FloatClampMode.ZeroToOne);
        rule.HideRewardWhenFloatMaxReached = false;
        rule.HideRewardWhenFloatMinReached = false;
        var (max, min) = FloatLimitDetection.ComputeLimitState(
            rule, 1.0, previousMaxReached: false, previousMinReached: false,
            featureEnabled: false);
        Assert.False(max);
        Assert.False(min);
    }

    [Fact]
    public void OnlyMaxEnabled_ReportsMaxOnly()
    {
        var rule = Rule(FloatActionMode.Add, FloatClampMode.ZeroToOne);
        rule.HideRewardWhenFloatMaxReached = true;
        rule.HideRewardWhenFloatMinReached = false;
        var (max, min) = FloatLimitDetection.ComputeLimitState(
            rule, 0.0, previousMaxReached: false, previousMinReached: false,
            featureEnabled: true);
        Assert.False(min);  // min checkbox is off, so min never reports
    }
}
```

- [ ] **Step 2: Run tests to verify they fail**

Run: `dotnet test "E:\!!!Program to work on\Proper Crystal Relay\VrcTwitchOscBridge.Tests\VrcTwitchOscBridge.Tests.csproj" --no-restore -f net10.0-windows --filter "FullyQualifiedName~FloatLimitDetectionTests"`
Expected: FAIL with compile error (`FloatLimitDetection` not found)

- [ ] **Step 3: Create the `FloatLimitDetection` static helper**

Create `VrcTwitchOscBridge\Services\FloatLimitDetection.cs`:

```csharp
using VrcTwitchOscBridge.Models;

namespace VrcTwitchOscBridge.Services;

public static class FloatLimitDetection
{
    private const double Tolerance = 0.000001d;
    private const double ReleaseTolerance = 0.0001d;

    public static (bool MaxReached, bool MinReached) ComputeLimitState(
        TriggerRule rule,
        double currentValue,
        bool previousMaxReached,
        bool previousMinReached = false,
        bool featureEnabled = true)
    {
        if (!featureEnabled)
        {
            return (false, false);
        }

        if (!IsCumulativeMode(rule.FloatActionMode))
        {
            return (false, false);
        }

        var (lower, upper) = ResolveLimits(rule);

        bool maxReached;
        if (previousMaxReached)
        {
            maxReached = currentValue >= upper - ReleaseTolerance;
        }
        else
        {
            maxReached = currentValue >= upper - Tolerance;
        }

        bool minReached;
        if (previousMinReached)
        {
            minReached = currentValue <= lower + ReleaseTolerance;
        }
        else
        {
            minReached = currentValue <= lower + Tolerance;
        }

        if (!rule.HideRewardWhenFloatMaxReached)
        {
            maxReached = false;
        }

        if (!rule.HideRewardWhenFloatMinReached)
        {
            minReached = false;
        }

        return (maxReached, minReached);
    }

    private static bool IsCumulativeMode(FloatActionMode mode) =>
        mode == FloatActionMode.Add
        || mode == FloatActionMode.Subtract
        || mode == FloatActionMode.AddSubtract
        || mode == FloatActionMode.Multiply;

    private static (double Lower, double Upper) ResolveLimits(TriggerRule rule)
    {
        return rule.FloatClampMode switch
        {
            FloatClampMode.None => (double.MinValue, double.MaxValue),
            FloatClampMode.ZeroToOne => (0.0, 1.0),
            FloatClampMode.MinToMax => (rule.FloatRangeMin, rule.FloatRangeMax),
            _ => (0.0, 1.0),
        };
    }
}
```

- [ ] **Step 4: Run tests to verify they pass**

Run: `dotnet test "E:\!!!Program to work on\Proper Crystal Relay\VrcTwitchOscBridge.Tests\VrcTwitchOscBridge.Tests.csproj" --no-restore -f net10.0-windows --filter "FullyQualifiedName~FloatLimitDetectionTests"`
Expected: PASS (all 11 tests)

- [ ] **Step 5: Commit**

```bash
git add VrcTwitchOscBridge/Services/FloatLimitDetection.cs VrcTwitchOscBridge.Tests/FloatLimitDetectionTests.cs
git commit -m "Add FloatLimitDetection static helper with hysteresis"
```

---

### Task 4: Session State, Public Accessor, and Event

**Files:**
- Modify: `VrcTwitchOscBridge\Services\BridgeCoordinator.cs:18774` (add fields to `ActiveFloatRedeemSessionState`)
- Modify: `VrcTwitchOscBridge\Services\BridgeCoordinator.cs:396` (add `GetActiveFloatLimitReachedRuleIds`)
- Modify: `VrcTwitchOscBridge\Services\BridgeCoordinator.cs:317` (add `FloatLimitStatusChanged` event)

- [ ] **Step 1: Add `FloatLimitDetection.cs` to the project file**

In `VrcTwitchOscBridge\VrcTwitchOscBridge.csproj`, after line 218 (`<Compile Include="Services\FloatActionDispatch.cs" />`), add:

```xml
    <Compile Include="Services\FloatLimitDetection.cs" />
```

This is required because the project has `EnableDefaultCompileItems=false`.

- [ ] **Step 2: Add `FloatMaxReached` and `FloatMinReached` to `ActiveFloatRedeemSessionState`**

In `VrcTwitchOscBridge\Services\BridgeCoordinator.cs`, after line 18774 (`public bool BoostMaximumReached { get; set; }`), add:

```csharp
        public bool FloatMaxReached { get; set; }

        public bool FloatMinReached { get; set; }
```

- [ ] **Step 2: Add the `FloatLimitStatusChanged` event**

In `VrcTwitchOscBridge\Services\BridgeCoordinator.cs`, near the other events (search for `AvatarScaleMasterRewardUnlockStateChanged` at ~line 317, or `AvatarScaleStatusChanged`), add alongside them:

```csharp
    public event Action? FloatLimitStatusChanged;
```

Place it right after the `AvatarScaleStatusChanged` event declaration (or whichever event is nearest — keep it grouped with the other reward-visibility events).

- [ ] **Step 3: Add `GetActiveFloatLimitReachedRuleIds` public accessor**

In `VrcTwitchOscBridge\Services\BridgeCoordinator.cs`, after `GetActiveFloatBoostMaximumReachedRuleIds` (line 396), add:

```csharp
    public IReadOnlyCollection<Guid> GetActiveFloatLimitReachedRuleIds()
    {
        lock (stateGate)
        {
            return [.. activeFloatRedeemSessions
                .Where(session => session.Value.FloatMaxReached || session.Value.FloatMinReached)
                .Select(session => session.Key)];
        }
    }
```

- [ ] **Step 4: Build to verify it compiles**

Run: `dotnet build "E:\!!!Program to work on\Proper Crystal Relay\VrcTwitchOscBridge\VrcTwitchOscBridge.csproj" --no-restore`
Expected: Build succeeded (no errors). The new fields/accessor/event are not yet used, but they must compile.

- [ ] **Step 5: Commit**

```bash
git add VrcTwitchOscBridge/VrcTwitchOscBridge.csproj VrcTwitchOscBridge/Services/BridgeCoordinator.cs
git commit -m "Add FloatMaxReached/MinReached to session state, public accessor, and event"
```

---

### Task 5: Session Lifecycle Hooks

**Files:**
- Modify: `VrcTwitchOscBridge\Services\BridgeCoordinator.cs:7803` (session creation — compute limit state)
- Modify: `VrcTwitchOscBridge\Services\BridgeCoordinator.cs:8083` (boost application — recompute)
- Modify: `VrcTwitchOscBridge\Services\BridgeCoordinator.cs:8149` (session finish — raise event)

- [ ] **Step 1: Compute limit state at session creation**

In `VrcTwitchOscBridge\Services\BridgeCoordinator.cs`, after the `boostMaximumReached` computation (line 7805) and before the `var session = new ActiveFloatRedeemSessionState(` (line 7806), add:

```csharp
        var (floatMaxReached, floatMinReached) = FloatLimitDetection.ComputeLimitState(
            rule.Rule,
            targetValue,
            previousMaxReached: false,
            previousMinReached: false,
            featureEnabled: rule.Rule.HideRewardWhenFloatMaxReached || rule.Rule.HideRewardWhenFloatMinReached);
```

Then modify the `new ActiveFloatRedeemSessionState(` constructor call (lines 7806-7816) — add the two new bools after `boostMaximumReached`:

Change:
```csharp
        var session = new ActiveFloatRedeemSessionState(
            rule,
            address,
            targetValue,
            resetValue,
            activeUntil,
            completionCancellation,
            laneKeys,
            laneLeaseId,
            isTest,
            boostMaximumReached);
```
To:
```csharp
        var session = new ActiveFloatRedeemSessionState(
            rule,
            address,
            targetValue,
            resetValue,
            activeUntil,
            completionCancellation,
            laneKeys,
            laneLeaseId,
            isTest,
            boostMaximumReached)
        {
            FloatMaxReached = floatMaxReached,
            FloatMinReached = floatMinReached,
        };
```

After the session is stored in `activeFloatRedeemSessions` (after line 7826), add a raise call (after the lock block closes at line 7847, before `previousSession?.CompletionCancellation.Cancel();` at 7849):

```csharp
        if (floatMaxReached || floatMinReached)
        {
            FloatLimitStatusChanged?.Invoke();
        }
```

- [ ] **Step 2: Recompute limit state after boost application**

In `VrcTwitchOscBridge\Services\BridgeCoordinator.cs`, after line 8083 (`session.BoostMaximumReached = boostMaximumReached;`), add:

```csharp
            var (updatedFloatMax, updatedFloatMin) = FloatLimitDetection.ComputeLimitState(
                session.Rule.Rule,
                boostedValue,
                previousMaxReached: session.FloatMaxReached,
                previousMinReached: session.FloatMinReached,
                featureEnabled: session.Rule.Rule.HideRewardWhenFloatMaxReached
                    || session.Rule.Rule.HideRewardWhenFloatMinReached);
            var floatLimitChanged = updatedFloatMax != session.FloatMaxReached
                || updatedFloatMin != session.FloatMinReached;
            session.FloatMaxReached = updatedFloatMax;
            session.FloatMinReached = updatedFloatMin;
```

Then after the boost method's `RememberAvatarParameterValue` call (line 8116), add:

```csharp
        if (floatLimitChanged)
        {
            FloatLimitStatusChanged?.Invoke();
        }
```

- [ ] **Step 3: Raise event on session finish**

In `VrcTwitchOscBridge\Services\BridgeCoordinator.cs`, in `FinishActiveFloatRedeemSession`, the session is removed at line 8133. Before removal, capture whether the flags were set. After line 8133 (`activeFloatRedeemSessions.Remove(session.Rule.Id);`), but still inside the lock block, add:

```csharp
                var hadFloatLimitReached = session.FloatMaxReached || session.FloatMinReached;
```

Then after the lock block, after `if (notifyManagedRewardState)` block (line 8147-8150), add:

```csharp
        if (hadFloatLimitReached)
        {
            FloatLimitStatusChanged?.Invoke();
        }
```

Make sure `hadFloatLimitReached` is declared outside the lock so it's accessible after. The final `FinishActiveFloatRedeemSession` structure:

```csharp
    private void FinishActiveFloatRedeemSession(
        ActiveFloatRedeemSessionState session,
        CancellationTokenSource completionCancellation,
        bool notifyManagedRewardState)
    {
        var releasedSession = false;
        var hadFloatLimitReached = false;
        lock (stateGate)
        {
            if (activeFloatRedeemSessions.TryGetValue(session.Rule.Id, out var currentSession)
                && ReferenceEquals(currentSession, session)
                && ReferenceEquals(session.CompletionCancellation, completionCancellation))
            {
                activeFloatRedeemSessions.Remove(session.Rule.Id);
                releasedSession = true;
                hadFloatLimitReached = session.FloatMaxReached || session.FloatMinReached;
            }
        }

        if (!releasedSession)
        {
            return;
        }

        completionCancellation.Dispose();
        session.SendGate.Dispose();
        var releasedLaneKeys = ReleaseMovementLanes(session.MovementLaneLeaseId, session.MovementLaneKeys);
        ReleaseActiveRuleLockoutState(session.Rule.Id, logRelease: true);
        if (notifyManagedRewardState)
        {
            ManagedRewardAvailabilityChanged?.Invoke();
        }

        if (hadFloatLimitReached)
        {
            FloatLimitStatusChanged?.Invoke();
        }

        foreach (var releasedLaneKey in releasedLaneKeys)
        {
            EnsureQueuedLaneDrain(releasedLaneKey);
        }
    }
```

- [ ] **Step 4: Build to verify it compiles**

Run: `dotnet build "E:\!!!Program to work on\Proper Crystal Relay\VrcTwitchOscBridge\VrcTwitchOscBridge.csproj" --no-restore`
Expected: Build succeeded

- [ ] **Step 5: Run all existing tests to verify no regressions**

Run: `dotnet test "E:\!!!Program to work on\Proper Crystal Relay\VrcTwitchOscBridge.Tests\VrcTwitchOscBridge.Tests.csproj" --no-restore -f net10.0-windows`
Expected: All existing tests PASS

- [ ] **Step 6: Commit**

```bash
git add VrcTwitchOscBridge/Services/BridgeCoordinator.cs
git commit -m "Compute and raise float limit state at session create, boost, and finish"
```

---

### Task 6: Sync Pipeline — Reason, Cache, Handler

**Files:**
- Modify: `VrcTwitchOscBridge\ViewModels\MainWindowViewModel.cs:48` (add enum value)
- Modify: `VrcTwitchOscBridge\ViewModels\MainWindowViewModel.cs:11054` (add to passive list)
- Modify: `VrcTwitchOscBridge\ViewModels\MainWindowViewModel.cs:11093` (add description)
- Modify: `VrcTwitchOscBridge\ViewModels\MainWindowViewModel.cs:1042` (subscribe to event)
- Modify: `VrcTwitchOscBridge\ViewModels\MainWindowViewModel.cs:537` (add state cache)
- Modify: `VrcTwitchOscBridge\ViewModels\MainWindowViewModel.cs:9037` (add handler + logging, after `LogAvatarScaleLimitVisibilityChange`)

- [ ] **Step 1: Add `FloatLimitStatus` to the `ManagedRewardSyncReason` enum**

In `VrcTwitchOscBridge\ViewModels\MainWindowViewModel.cs`, after line 48 (`AvatarScaleStatus,`), add:

```csharp
        FloatLimitStatus,
```

- [ ] **Step 2: Add `FloatLimitStatus` to the passive reason list**

In `VrcTwitchOscBridge\ViewModels\MainWindowViewModel.cs`, at line 11053-11054, change:

```csharp
    private static bool IsPassiveManagedRewardSyncReason(ManagedRewardSyncReason reason) =>
        reason is ManagedRewardSyncReason.RuntimeAvailability or ManagedRewardSyncReason.AvatarScaleStatus;
```
To:
```csharp
    private static bool IsPassiveManagedRewardSyncReason(ManagedRewardSyncReason reason) =>
        reason is ManagedRewardSyncReason.RuntimeAvailability
            or ManagedRewardSyncReason.AvatarScaleStatus
            or ManagedRewardSyncReason.FloatLimitStatus;
```

- [ ] **Step 3: Add the sync reason description**

In `VrcTwitchOscBridge\ViewModels\MainWindowViewModel.cs`, after line 11093 (`ManagedRewardSyncReason.AvatarScaleStatus => "avatar scale status",`), add:

```csharp
        ManagedRewardSyncReason.FloatLimitStatus => "float limit status",
```

- [ ] **Step 4: Add the state cache fields**

In `VrcTwitchOscBridge\ViewModels\MainWindowViewModel.cs`, after line 537 (`private readonly Dictionary<Guid, bool> avatarScaleLimitInactiveStateByRuleId = [];`), add:

```csharp
    private readonly object floatLimitStateGate = new();
    private readonly Dictionary<Guid, (bool MaxReached, bool MinReached)> floatLimitStateByRuleId = [];
```

- [ ] **Step 5: Subscribe to the `FloatLimitStatusChanged` event**

In `VrcTwitchOscBridge\ViewModels\MainWindowViewModel.cs`, after line 1042 (`bridgeCoordinator.AvatarScaleStatusChanged += () => RunOnUi(HandleAvatarScaleStatusChanged);`), add:

```csharp
            bridgeCoordinator.FloatLimitStatusChanged += () => RunOnUi(HandleFloatLimitStatusChanged);
```

- [ ] **Step 6: Add the `HandleFloatLimitStatusChanged` handler and logging**

In `VrcTwitchOscBridge\ViewModels\MainWindowViewModel.cs`, after `LogAvatarScaleLimitVisibilityChange` (line 9037), add:

```csharp
    private void HandleFloatLimitStatusChanged()
    {
        if (!isInitialized || isShuttingDown)
        {
            return;
        }

        var limitReachedRuleIds = bridgeCoordinator.GetActiveFloatLimitReachedRuleIds();
        var currentStates = limitReachedRuleIds
            .ToDictionary(id => id, id => (MaxReached: true, MinReached: true));

        var shouldSync = false;
        var activeRuleIds = new HashSet<Guid>();

        foreach (var rule in EnumerateAllRules().Where(r => r.UsesFloatHideOnLimit))
        {
            activeRuleIds.Add(rule.Id);
            var currentState = currentStates.TryGetValue(rule.Id, out var s)
                ? s
                : (MaxReached: false, MinReached: false);

            bool hadPreviousState;
            (bool PrevMax, bool PrevMin) previousState;
            lock (floatLimitStateGate)
            {
                hadPreviousState = floatLimitStateByRuleId.TryGetValue(rule.Id, out previousState);
            }

            if (!hadPreviousState
                || previousState.PrevMax != currentState.MaxReached
                || previousState.PrevMin != currentState.MinReached)
            {
                lock (floatLimitStateGate)
                {
                    floatLimitStateByRuleId[rule.Id] = currentState;
                }

                if (hadPreviousState || currentState.MaxReached || currentState.MinReached)
                {
                    LogFloatLimitVisibilityChange(rule, currentState);
                }

                shouldSync = true;
            }
        }

        lock (floatLimitStateGate)
        {
            foreach (var removedRuleId in floatLimitStateByRuleId.Keys.Except(activeRuleIds).ToArray())
            {
                floatLimitStateByRuleId.Remove(removedRuleId);
            }
        }

        if (shouldSync)
        {
            QueueManagedRewardSync(
                (int)AvatarScaleLimitRewardSyncDebounce.TotalMilliseconds,
                ManagedRewardSyncReason.FloatLimitStatus);
        }
    }

    private void LogFloatLimitVisibilityChange(TriggerRule rule, (bool MaxReached, bool MinReached) state)
    {
        var limitName = state.MaxReached ? "maximum" : "minimum";
        var message = (state.MaxReached || state.MinReached)
            ? $"Avatar set reward '{rule.DisplayTitle}' is hidden on Twitch because its float value reached the configured {limitName}."
            : $"Avatar set reward '{rule.DisplayTitle}' can show on Twitch again because its float value left the configured limit.";

        AppendThrottledLog(
            $"float-limit-visibility:{rule.Id}:{limitName}:{state.MaxReached || state.MinReached}",
            message,
            ThrottledRewardSyncLogWindow);
    }
```

Note: `EnumerateAllRules()` is the existing method at `MainWindowViewModel.cs:18121` that returns all `TriggerRule` instances from Avatar Profiles, Movement Rules, and Global Override Rules. The `.Where(r => r.UsesFloatHideOnLimit)` filter narrows to only the rules where the feature applies.

- [ ] **Step 7: Build to verify it compiles**

Run: `dotnet build "E:\!!!Program to work on\Proper Crystal Relay\VrcTwitchOscBridge\VrcTwitchOscBridge.csproj" --no-restore`
Expected: Build succeeded. If `GetAllAvatarSetRules` is not the correct method name, search for the method that returns the trigger rules and adjust.

- [ ] **Step 8: Run all tests**

Run: `dotnet test "E:\!!!Program to work on\Proper Crystal Relay\VrcTwitchOscBridge.Tests\VrcTwitchOscBridge.Tests.csproj" --no-restore -f net10.0-windows`
Expected: All tests PASS

- [ ] **Step 9: Commit**

```bash
git add VrcTwitchOscBridge/ViewModels/MainWindowViewModel.cs
git commit -m "Add FloatLimitStatus sync reason, state cache, and handler"
```

---

### Task 7: Target Builder Gate

**Files:**
- Modify: `VrcTwitchOscBridge\ViewModels\MainWindowViewModel.cs:12107-12131` (add `floatLimitReached` to `desiredEnabled`, `deleteWhenInactive`, `protectFromCapReclaim`)
- Modify: `VrcTwitchOscBridge\ViewModels\MainWindowViewModel.cs:12913` (pass `activeFloatLimitReachedRuleIds` into sync assembly)

- [ ] **Step 1: Add `activeFloatLimitReachedRuleIds` to the sync assembly**

In `VrcTwitchOscBridge\ViewModels\MainWindowViewModel.cs`, after line 12913 (`var activeFloatBoostMaximumReachedRuleIds = bridgeCoordinator.GetActiveFloatBoostMaximumReachedRuleIds();`), add:

```csharp
            var activeFloatLimitReachedRuleIds = bridgeCoordinator.GetActiveFloatLimitReachedRuleIds();
```

- [ ] **Step 2: Add `floatLimitReached` to the plain-rule target builder**

In `VrcTwitchOscBridge\ViewModels\MainWindowViewModel.cs`, in the method that builds the plain `TriggerRule` target (ending at line 12131), after the `isActiveFloatBoostParent` line (12103), add:

```csharp
        var floatLimitReached = activeFloatLimitReachedRuleIds.Contains(rule.Id)
            && rule.UsesFloatHideOnLimit
            && (rule.HideRewardWhenFloatMaxReached || rule.HideRewardWhenFloatMinReached);
```

Then modify the `desiredEnabled` computation (lines 12107-12113). Change:

```csharp
        var desiredEnabled = allowManagedRewardActivation
            && ruleHasRuntimeReadyAction
            && (profile?.IsEnabled ?? true)
            && rule.IsEnabled
            && !temporarilyDisabledRuleIds.Contains(rule.Id)
            && ruleIsVisibleForCurrentAvatar
            && !isActiveFloatBoostParent;
```
To:
```csharp
        var desiredEnabled = allowManagedRewardActivation
            && ruleHasRuntimeReadyAction
            && (profile?.IsEnabled ?? true)
            && rule.IsEnabled
            && !temporarilyDisabledRuleIds.Contains(rule.Id)
            && ruleIsVisibleForCurrentAvatar
            && !isActiveFloatBoostParent
            && !floatLimitReached;
```

Then modify `deleteWhenInactive` (line 12129). Change:

```csharp
            deleteWhenInactive: rule.DeleteManagedRewardWhenInactive && !isCooldownOnlyDirectAvatarChange && !temporarilyDisabledRuleIds.Contains(rule.Id) && !isActiveFloatBoostParent,
```
To:
```csharp
            deleteWhenInactive: rule.DeleteManagedRewardWhenInactive && !isCooldownOnlyDirectAvatarChange && !temporarilyDisabledRuleIds.Contains(rule.Id) && !isActiveFloatBoostParent && !floatLimitReached,
```

Then modify `protectFromCapReclaim` (line 12130). Change:

```csharp
            protectFromCapReclaim: desiredEnabled || isOnLocalCooldown || temporarilyDisabledRuleIds.Contains(rule.Id) || isActiveFloatBoostParent || isCooldownOnlyDirectAvatarChange,
```
To:
```csharp
            protectFromCapReclaim: desiredEnabled || isOnLocalCooldown || temporarilyDisabledRuleIds.Contains(rule.Id) || isActiveFloatBoostParent || isCooldownOnlyDirectAvatarChange || floatLimitReached,
```

- [ ] **Step 3: Build to verify it compiles**

Run: `dotnet build "E:\!!!Program to work on\Proper Crystal Relay\VrcTwitchOscBridge\VrcTwitchOscBridge.csproj" --no-restore`
Expected: Build succeeded

- [ ] **Step 4: Run all tests**

Run: `dotnet test "E:\!!!Program to work on\Proper Crystal Relay\VrcTwitchOscBridge.Tests\VrcTwitchOscBridge.Tests.csproj" --no-restore -f net10.0-windows`
Expected: All tests PASS

- [ ] **Step 5: Commit**

```bash
git add VrcTwitchOscBridge/ViewModels/MainWindowViewModel.cs
git commit -m "Gate managed reward desiredEnabled on float limit reached"
```

---

### Task 8: UI — AvatarSetsManagerWindow.xaml

**Files:**
- Modify: `VrcTwitchOscBridge\AvatarSetsManagerWindow.xaml:1648` (add checkboxes after the clamp `ComboBox`)

- [ ] **Step 1: Add the hide-on-limit checkboxes to the XAML**

In `VrcTwitchOscBridge\AvatarSetsManagerWindow.xaml`, find the clamp `ComboBox` block ending at ~line 1648. After the closing `</ComboBox>` (or after the `StackPanel` that contains the clamp ComboBox), add:

```xml
                                                <StackPanel Visibility="{Binding UsesFloatHideOnLimit, Converter={StaticResource BoolToVisibilityConverter}}"
                                                            Margin="0,8,0,0">
                                                    <CheckBox IsChecked="{Binding HideRewardWhenFloatMaxReached, UpdateSourceTrigger=PropertyChanged}"
                                                              Content="{loc:Translate 'Hide reward at maximum float'}" />
                                                    <CheckBox Margin="0,4,0,0"
                                                              IsChecked="{Binding HideRewardWhenFloatMinReached, UpdateSourceTrigger=PropertyChanged}"
                                                              Content="{loc:Translate 'Hide reward at minimum float'}" />
                                                </StackPanel>
```

Place it at the same indentation level as the clamp `StackPanel` — inside the parent container that holds all the float-mode-specific field panels.

- [ ] **Step 2: Build to verify XAML compiles**

Run: `dotnet build "E:\!!!Program to work on\Proper Crystal Relay\VrcTwitchOscBridge\VrcTwitchOscBridge.csproj" --no-restore`
Expected: Build succeeded (XAML compiles, `UsesFloatHideOnLimit` binding resolves, `BoolToVisibilityConverter` is already a shared resource in the window)

- [ ] **Step 3: Commit**

```bash
git add VrcTwitchOscBridge/AvatarSetsManagerWindow.xaml
git commit -m "Add hide-on-float-limit checkboxes to Avatar Sets editor"
```

---

### Task 9: Localization

**Files:**
- Modify: `VrcTwitchOscBridge\Resources\Localization\en-US.extra.json:467` (add after "Hide reward at maximum height")
- Modify: 14 other `.extra.json` files (de-DE, es-ES, fr-FR, it-IT, ja-JP, ko-KR, pl-PL, pt-BR, ru-RU, sv-SE, th-TH, zh-CN, zh-TW)
- Modify: `VrcTwitchOscBridge\Resources\Localization\en-US.json` (add keys to base file if the `.extra.json` merge doesn't cover `{loc:Translate}` lookups — verify by checking how the existing "Hide reward at maximum height" key is resolved)

- [ ] **Step 1: Add keys to `en-US.extra.json`**

In `VrcTwitchOscBridge\Resources\Localization\en-US.extra.json`, after line 467 (`"Hide reward at maximum height": "Hide reward at maximum height",`), add:

```json
  "Hide reward at maximum float": "Hide reward at maximum float",
  "Hide reward at minimum float": "Hide reward at minimum float",
  "When enabled, Crystal Relay hides this managed Twitch reward while the float value is at the configured maximum, then shows it again after Active Time ends.": "When enabled, Crystal Relay hides this managed Twitch reward while the float value is at the configured maximum, then shows it again after Active Time ends.",
  "When enabled, Crystal Relay hides this managed Twitch reward while the float value is at the configured minimum, then shows it again after Active Time ends.": "When enabled, Crystal Relay hides this managed Twitch reward while the float value is at the configured minimum, then shows it again after Active Time ends.",
```

- [ ] **Step 2: Add keys to `de-DE.extra.json`**

After the existing `"Hide reward at maximum height"` line, add:

```json
  "Hide reward at maximum float": "Belohnung bei maximalem Float-Wert verbergen",
  "Hide reward at minimum float": "Belohnung bei minimalem Float-Wert verbergen",
  "When enabled, Crystal Relay hides this managed Twitch reward while the float value is at the configured maximum, then shows it again after Active Time ends.": "Wenn aktiviert, versteckt Crystal Relay diese verwaltete Twitch-Belohnung, solange der Float-Wert am konfigurierten Maximum ist, und zeigt sie wieder an, nachdem die aktive Zeit endet.",
  "When enabled, Crystal Relay hides this managed Twitch reward while the float value is at the configured minimum, then shows it again after Active Time ends.": "Wenn aktiviert, versteckt Crystal Relay diese verwaltete Twitch-Belohnung, solange der Float-Wert am konfigurierten Minimum ist, und zeigt sie wieder an, nachdem die aktive Zeit endet.",
```

- [ ] **Step 3: Add keys to `es-ES.extra.json`**

After the existing `"Hide reward at maximum height"` line, add:

```json
  "Hide reward at maximum float": "Ocultar recompensa al llegar al valor float máximo",
  "Hide reward at minimum float": "Ocultar recompensa al llegar al valor float mínimo",
  "When enabled, Crystal Relay hides this managed Twitch reward while the float value is at the configured maximum, then shows it again after Active Time ends.": "Cuando está activado, Crystal Relay oculta esta recompensa gestionada de Twitch mientras el valor float esté en el máximo configurado, y la muestra de nuevo cuando termina el tiempo activo.",
  "When enabled, Crystal Relay hides this managed Twitch reward while the float value is at the configured minimum, then shows it again after Active Time ends.": "Cuando está activado, Crystal Relay oculta esta recompensa gestionada de Twitch mientras el valor float esté en el mínimo configurado, y la muestra de nuevo cuando termina el tiempo activo.",
```

- [ ] **Step 4: Add keys to `fr-FR.extra.json`**

After the existing `"Hide reward at maximum height"` line, add:

```json
  "Hide reward at maximum float": "Masquer la récompense à la valeur float maximale",
  "Hide reward at minimum float": "Masquer la récompense à la valeur float minimale",
  "When enabled, Crystal Relay hides this managed Twitch reward while the float value is at the configured maximum, then shows it again after Active Time ends.": "Si activé, Crystal Relay masque cette récompense Twitch gérée tant que la valeur float est au maximum configuré, puis l'affiche à nouveau quand le temps actif se termine.",
  "When enabled, Crystal Relay hides this managed Twitch reward while the float value is at the configured minimum, then shows it again after Active Time ends.": "Si activé, Crystal Relay masque cette récompense Twitch gérée tant que la valeur float est au minimum configuré, puis l'affiche à nouveau quand le temps actif se termine.",
```

- [ ] **Step 5: Add keys to `it-IT.extra.json`**

After the existing `"Hide reward at maximum height"` line, add:

```json
  "Hide reward at maximum float": "Nascondi ricompensa al valore float massimo",
  "Hide reward at minimum float": "Nascondi ricompensa al valore float minimo",
  "When enabled, Crystal Relay hides this managed Twitch reward while the float value is at the configured maximum, then shows it again after Active Time ends.": "Quando attivato, Crystal Relay nasconde questa ricompensa Twitch gestita mentre il valore float è al massimo configurato, poi la mostra di nuovo quando il tempo attivo finisce.",
  "When enabled, Crystal Relay hides this managed Twitch reward while the float value is at the configured minimum, then shows it again after Active Time ends.": "Quando attivato, Crystal Relay nasconde questa ricompensa Twitch gestita mentre il valore float è al minimo configurato, poi la mostra di nuovo quando il tempo attivo finisce.",
```

- [ ] **Step 6: Add keys to `ja-JP.extra.json`**

After the existing `"Hide reward at maximum height"` line, add:

```json
  "Hide reward at maximum float": "float値が最大に達したらリワードを非表示",
  "Hide reward at minimum float": "float値が最小に達したらリワードを非表示",
  "When enabled, Crystal Relay hides this managed Twitch reward while the float value is at the configured maximum, then shows it again after Active Time ends.": "有効にすると、float値が設定された最大値に達している間、Crystal Relayはこの管理Twitchリワードを非表示にし、アクティブタイム終了後に再表示します。",
  "When enabled, Crystal Relay hides this managed Twitch reward while the float value is at the configured minimum, then shows it again after Active Time ends.": "有効にすると、float値が設定された最小値に達している間、Crystal Relayはこの管理Twitchリワードを非表示にし、アクティブタイム終了後に再表示します。",
```

- [ ] **Step 7: Add keys to `ko-KR.extra.json`**

After the existing `"Hide reward at maximum height"` line, add:

```json
  "Hide reward at maximum float": "float 값이 최대치에 도달하면 보상 숨기기",
  "Hide reward at minimum float": "float 값이 최소치에 도달하면 보상 숨기기",
  "When enabled, Crystal Relay hides this managed Twitch reward while the float value is at the configured maximum, then shows it again after Active Time ends.": "활성화하면 float 값이 설정된 최대치에 있는 동안 Crystal Relay가 이 관리 Twitch 보상을 숨기고, 액티브 타임이 끝나면 다시 표시합니다.",
  "When enabled, Crystal Relay hides this managed Twitch reward while the float value is at the configured minimum, then shows it again after Active Time ends.": "활성화하면 float 값이 설정된 최소치에 있는 동안 Crystal Relay가 이 관리 Twitch 보상을 숨기고, 액티브 타임이 끝나면 다시 표시합니다.",
```

- [ ] **Step 8: Add keys to `pl-PL.extra.json`**

After the existing `"Hide reward at maximum height"` line, add:

```json
  "Hide reward at maximum float": "Ukryj nagrodę przy maksymalnej wartości float",
  "Hide reward at minimum float": "Ukryj nagrodę przy minimalnej wartości float",
  "When enabled, Crystal Relay hides this managed Twitch reward while the float value is at the configured maximum, then shows it again after Active Time ends.": "Gdy włączone, Crystal Relay ukrywa tę zarządzaną nagrodę Twitch, dopóki wartość float jest na ustawionym maksimum, a potem pokazuje ją ponownie po zakończeniu czasu aktywnego.",
  "When enabled, Crystal Relay hides this managed Twitch reward while the float value is at the configured minimum, then shows it again after Active Time ends.": "Gdy włączone, Crystal Relay ukrywa tę zarządzaną nagrodę Twitch, dopóki wartość float jest na ustawionym minimum, a potem pokazuje ją ponownie po zakończeniu czasu aktywnego.",
```

- [ ] **Step 9: Add keys to `pt-BR.extra.json`**

After the existing `"Hide reward at maximum height"` line, add:

```json
  "Hide reward at maximum float": "Ocultar recompensa no valor float máximo",
  "Hide reward at minimum float": "Ocultar recompensa no valor float mínimo",
  "When enabled, Crystal Relay hides this managed Twitch reward while the float value is at the configured maximum, then shows it again after Active Time ends.": "Quando ativado, o Crystal Relay oculta esta recompensa gerenciada da Twitch enquanto o valor float está no máximo configurado, e a mostra novamente quando o tempo ativo termina.",
  "When enabled, Crystal Relay hides this managed Twitch reward while the float value is at the configured minimum, then shows it again after Active Time ends.": "Quando ativado, o Crystal Relay oculta esta recompensa gerenciada da Twitch enquanto o valor float está no mínimo configurado, e a mostra novamente quando o tempo ativo termina.",
```

- [ ] **Step 10: Add keys to `ru-RU.extra.json`**

After the existing `"Hide reward at maximum height"` line, add:

```json
  "Hide reward at maximum float": "Скрывать награду при максимальном значении float",
  "Hide reward at minimum float": "Скрывать награду при минимальном значении float",
  "When enabled, Crystal Relay hides this managed Twitch reward while the float value is at the configured maximum, then shows it again after Active Time ends.": "Если включено, Crystal Relay скрывает эту управляемую награду Twitch, пока значение float на настроенном максимуме, и снова показывает её после окончания активного времени.",
  "When enabled, Crystal Relay hides this managed Twitch reward while the float value is at the configured minimum, then shows it again after Active Time ends.": "Если включено, Crystal Relay скрывает эту управляемую награду Twitch, пока значение float на настроенном минимуме, и снова показывает её после окончания активного времени.",
```

- [ ] **Step 11: Add keys to `sv-SE.extra.json`**

After the existing `"Hide reward at maximum height"` line, add:

```json
  "Hide reward at maximum float": "Dölj belöning vid maximalt float-värde",
  "Hide reward at minimum float": "Dölj belöning vid minimum float-värde",
  "When enabled, Crystal Relay hides this managed Twitch reward while the float value is at the configured maximum, then shows it again after Active Time ends.": "När aktiverad döljer Crystal Relay denna hanterade Twitch-belöning medan float-värdet är vid det inställda maximivärdet och visar den igen när aktiv tid tar slut.",
  "When enabled, Crystal Relay hides this managed Twitch reward while the float value is at the configured minimum, then shows it again after Active Time ends.": "När aktiverad döljer Crystal Relay denna hanterade Twitch-belöning medan float-värdet är vid det inställda minimivärdet och visar den igen när aktiv tid tar slut.",
```

- [ ] **Step 12: Add keys to `th-TH.extra.json`**

After the existing `"Hide reward at maximum height"` line, add:

```json
  "Hide reward at maximum float": "ซ่อนรางวัลเมื่อ float ถึงค่าสูงสุด",
  "Hide reward at minimum float": "ซ่อนรางวัลเมื่อ float ถึงค่าต่ำสุด",
  "When enabled, Crystal Relay hides this managed Twitch reward while the float value is at the configured maximum, then shows it again after Active Time ends.": "เปิดใช้งานแล้ว Crystal Relay จะซ่อนรางวัล Twitch ที่จัดการนี้ขณะที่ค่า float อยู่ที่ค่าสูงสุดที่ตั้งไว้ แล้วแสดงอีกครั้งเมื่อเวลาแอคทีฟหมด",
  "When enabled, Crystal Relay hides this managed Twitch reward while the float value is at the configured minimum, then shows it again after Active Time ends.": "เปิดใช้งานแล้ว Crystal Relay จะซ่อนรางวัล Twitch ที่จัดการนี้ขณะที่ค่า float อยู่ที่ค่าต่ำสุดที่ตั้งไว้ แล้วแสดงอีกครั้งเมื่อเวลาแอคทีฟหมด",
```

- [ ] **Step 13: Add keys to `zh-CN.extra.json`**

After the existing `"Hide reward at maximum height"` line, add:

```json
  "Hide reward at maximum float": "float 达到最大值时隐藏奖励",
  "Hide reward at minimum float": "float 达到最小值时隐藏奖励",
  "When enabled, Crystal Relay hides this managed Twitch reward while the float value is at the configured maximum, then shows it again after Active Time ends.": "启用后，当 float 值处于配置的最大值时，Crystal Relay 会隐藏此托管 Twitch 奖励，并在活跃时间结束后重新显示。",
  "When enabled, Crystal Relay hides this managed Twitch reward while the float value is at the configured minimum, then shows it again after Active Time ends.": "启用后，当 float 值处于配置的最小值时，Crystal Relay 会隐藏此托管 Twitch 奖励，并在活跃时间结束后重新显示。",
```

- [ ] **Step 14: Add keys to `zh-TW.extra.json`**

After the existing `"Hide reward at maximum height"` line, add:

```json
  "Hide reward at maximum float": "float 達到最大值時隱藏獎勵",
  "Hide reward at minimum float": "float 達到最小值時隱藏獎勵",
  "When enabled, Crystal Relay hides this managed Twitch reward while the float value is at the configured maximum, then shows it again after Active Time ends.": "啟用後，當 float 值處於設定的最大值時，Crystal Relay 會隱藏此託管 Twitch 獎勵，並在活躍時間結束後重新顯示。",
  "When enabled, Crystal Relay hides this managed Twitch reward while the float value is at the configured minimum, then shows it again after Active Time ends.": "啟用後，當 float 值處於設定的最小值時，Crystal Relay 會隱藏此託管 Twitch 獎勵，並在活躍時間結束後重新顯示。",
```

- [ ] **Step 15: Run the localization audit**

Run: `dotnet run --project "E:\!!!Program to work on\Proper Crystal Relay\LocalizationAudit\LocalizationAudit.csproj" --no-restore`
Expected: Audit passes with no missing keys, no empty values, no placeholder mismatches across all 15 language files.

- [ ] **Step 16: Commit**

```bash
git add VrcTwitchOscBridge/Resources/Localization/
git commit -m "Add hide-on-float-limit localization keys for all 15 languages"
```

---

### Task 10: XAML Test

**Files:**
- Modify: `VrcTwitchOscBridge.Tests\AvatarSetsManagerWindowXamlTests.cs`

- [ ] **Step 1: Write failing test for the new checkboxes**

Add to `VrcTwitchOscBridge.Tests\AvatarSetsManagerWindowXamlTests.cs`, at the end of the class (before the closing `}`):

```csharp
    [Fact]
    public void FloatModeCard_ContainsHideOnLimitCheckboxes()
    {
        var xaml = File.ReadAllText(FindSourceFile("VrcTwitchOscBridge", "AvatarSetsManagerWindow.xaml"));
        var headerIndex = xaml.IndexOf("FloatActionModeHeader", StringComparison.Ordinal);
        Assert.True(headerIndex >= 0, "Float Action Mode section should exist.");

        Assert.Contains("HideRewardWhenFloatMaxReached", xaml, StringComparison.Ordinal);
        Assert.Contains("HideRewardWhenFloatMinReached", xaml, StringComparison.Ordinal);
        Assert.Contains("UsesFloatHideOnLimit", xaml, StringComparison.Ordinal);
        Assert.Contains("Hide reward at maximum float", xaml, StringComparison.Ordinal);
        Assert.Contains("Hide reward at minimum float", xaml, StringComparison.Ordinal);
    }
```

- [ ] **Step 2: Run test to verify it passes**

Run: `dotnet test "E:\!!!Program to work on\Proper Crystal Relay\VrcTwitchOscBridge.Tests\VrcTwitchOscBridge.Tests.csproj" --no-restore -f net10.0-windows --filter "FullyQualifiedName~FloatModeCard_ContainsHideOnLimitCheckboxes"`
Expected: PASS (the XAML was already edited in Task 8)

- [ ] **Step 3: Commit**

```bash
git add VrcTwitchOscBridge.Tests/AvatarSetsManagerWindowXamlTests.cs
git commit -m "Add XAML test for hide-on-float-limit checkboxes"
```

---

### Task 11: Final Build, Test, and Localization Audit

- [ ] **Step 1: Build the app project**

Run: `dotnet build "E:\!!!Program to work on\Proper Crystal Relay\VrcTwitchOscBridge\VrcTwitchOscBridge.csproj" --no-restore`
Expected: Build succeeded with 0 errors

- [ ] **Step 2: Run the full test suite**

Run: `dotnet test "E:\!!!Program to work on\Proper Crystal Relay\VrcTwitchOscBridge.Tests\VrcTwitchOscBridge.Tests.csproj" --no-restore -f net10.0-windows`
Expected: All tests PASS (existing + new)

- [ ] **Step 3: Run the localization audit**

Run: `dotnet run --project "E:\!!!Program to work on\Proper Crystal Relay\LocalizationAudit\LocalizationAudit.csproj" --no-restore`
Expected: Audit passes

- [ ] **Step 4: Verify the project file includes the new source file**

The `FloatLimitDetection.cs` include was added in Task 4, Step 1. Verify it's present:

Run: `grep "FloatLimitDetection" "E:\!!!Program to work on\Proper Crystal Relay\VrcTwitchOscBridge\VrcTwitchOscBridge.csproj"`
Expected: `<Compile Include="Services\FloatLimitDetection.cs" />` appears in the output.

If missing, add `<Compile Include="Services\FloatLimitDetection.cs" />` after the `FloatActionDispatch.cs` line (line 218) and rebuild.

- [ ] **Step 5: If the csproj needed updating, rebuild and commit**

```bash
git add VrcTwitchOscBridge/VrcTwitchOscBridge.csproj
git commit -m "Include FloatLimitDetection.cs in project file"
```

If no csproj change was needed, skip this step.

- [ ] **Step 6: Final commit if any files remain unstaged**

Check `git status` and commit any remaining changes. All work should already be committed in prior tasks; this is just a safety check.

---

## Self-Review Notes

- **Spec coverage:** Every section of the design spec (Sections 1-7) maps to tasks above. Data model → Task 1. Runtime state → Tasks 3-5. Sync pipeline → Tasks 6-7. UI → Task 8. Settings + Localization → Tasks 2, 9. Verification → Tasks 10-11. Edge cases are covered by the `FloatLimitDetection` tests (Task 3) and the mode/feature-disabled gates.
- **Type consistency:** `HideRewardWhenFloatMaxReached` / `HideRewardWhenFloatMinReached` / `UsesFloatHideOnLimit` are used consistently across all tasks. `FloatLimitDetection.ComputeLimitState` signature matches between Task 3 (creation) and Tasks 5 (usage). `GetActiveFloatLimitReachedRuleIds` matches between Task 4 (creation) and Tasks 5, 6, 7 (usage). `FloatLimitStatusChanged` event matches between Task 4 (creation) and Tasks 5, 6 (usage). `ManagedRewardSyncReason.FloatLimitStatus` matches between Task 6 (creation) and Task 7 (passive skip list).
- **No placeholders:** All steps contain actual code. The one verification step (Task 6, Step 6) notes that `GetAllAvatarSetRules()` may need adjustment — this is a search instruction, not a placeholder, and includes a fallback search strategy.
