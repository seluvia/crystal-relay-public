# Scale Timer Fairness and Support Extension Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Make avatar scale restore timers fair across multiple triggers (highest-seen for channel points, pay-system preemption) and let Bits/Subs/Cash/PowerUp support events extend any currently-active timed activity instead of running their own action.

**Architecture:** Two coordinated changes to `BridgeCoordinator` and the rule/snapshot/persistence stack. Part A adds a tier-aware highest-seen restore timer to the avatar scale restore sequence. Part B adds `ExtendCurrentActivity` + `ExtendSeconds` fields to `TriggerRule` and `AvatarScaleRule`, flows them through snapshots and persistence, and adds a `ExtendActiveActivityTimers` method to the coordinator that extends all active timed states.

**Tech Stack:** C# / .NET 10 / WPF / xUnit / VrcTwitchOscBridge

---

## File Structure

### Model files to modify
- `VrcTwitchOscBridge\Models\TriggerRule.cs` — add `ExtendCurrentActivity` + `ExtendSeconds` fields/properties
- `VrcTwitchOscBridge\Models\AvatarScaleRule.cs` — add `ExtendCurrentActivity` + `ExtendSeconds` fields/properties

### Snapshot file to modify
- `VrcTwitchOscBridge\Services\BridgeRuntimeConfiguration.cs` — add fields to `TriggerRuleSnapshot` and `AvatarScaleRuleSnapshot`, update builders

### Persistence file to modify
- `VrcTwitchOscBridge\Services\SettingsStore.cs` — add fields to `PersistedTriggerRule` and `PersistedAvatarScaleRule`, update `ToPersistedRule`/`ToRule` and `ToPersistedAvatarScaleRule`/`ToAvatarScaleRule`

### Coordinator file to modify
- `VrcTwitchOscBridge\Services\BridgeCoordinator.cs` — Part A: tier-aware restore sequence; Part B: `ExtendActiveActivityTimers` method and extend-path dispatch

### UI files to modify
- `VrcTwitchOscBridge\SupporterOverrideTimeSettingsWindow.xaml` — add extend toggle + seconds for Bits/Subs
- `VrcTwitchOscBridge\AvatarScalingManagerWindow.xaml` — add extend toggle + seconds for scale rules (Bits/Subs/SupporterGrowth)
- `VrcTwitchOscBridge\MainWindow.xaml` — add extend toggle + seconds for Power Up and Cash Payment editors

### Localization files to modify (all 14)
- `VrcTwitchOscBridge\Resources\Localization\*.extra.json` — 8 new keys each

### Test files to create
- `VrcTwitchOscBridge.Tests\ScaleTimerFairnessTests.cs` — Part A tests
- `VrcTwitchOscBridge.Tests\ExtendActiveActivityTests.cs` — Part B tests
- `VrcTwitchOscBridge.Tests\ExtendFieldsPersistenceTests.cs` — migration tests

---

## Part A — Scale Timer Fairness

### Task 1: Add tier field to AvatarScaleRuleSnapshot and restore sequence state

**Files:**
- Modify: `VrcTwitchOscBridge\Services\BridgeRuntimeConfiguration.cs:195-255`
- Modify: `VrcTwitchOscBridge\Services\BridgeCoordinator.cs:19473-19483`

- [ ] **Step 1: Add `IsPaySystemTrigger` to `AvatarScaleRuleSnapshot`**

In `BridgeRuntimeConfiguration.cs`, add a trailing parameter to the `AvatarScaleRuleSnapshot` record (after `SupporterGrowthBitRanges`):

```csharp
    IReadOnlyList<AvatarScaleBitGrowthRangeSnapshot> SupporterGrowthBitRanges,
    bool IsPaySystemTrigger = false);
```

- [ ] **Step 2: Populate `IsPaySystemTrigger` in the snapshot builder**

In `BridgeRuntimeConfiguration.cs`, find `TryToAvatarScaleSnapshot` (around line 1275-1361). In the `new AvatarScaleRuleSnapshot(...)` constructor call (around line 1299), add the new parameter at the end:

```csharp
    rule.TriggerType is AvatarScaleTriggerType.Bits or AvatarScaleTriggerType.Subscription or AvatarScaleTriggerType.GiftSubscription
        && rule is { UsesSupporterGrowth: false });
```

Wait — per the design, Tier 2 (pay systems) = Cash Payments & Power Ups, not Bits/Subs scale rules. Bits/Subs scale rules are Tier 1 (supporter-growth-adjacent). The pay-system tier is determined by the *source* of the trigger, not the `AvatarScaleTriggerType`. Let me fix: `IsPaySystemTrigger` should be set by the caller context, not derived from `TriggerType`.

Replace step 2 with: set `IsPaySystemTrigger = false` by default in the snapshot builder (it will be overridden by the caller when a Cash Payment or Power Up triggers a scale action). The `TryToAvatarScaleSnapshot` builder already has a `requireTriggerFilter` parameter; add an optional `bool isPaySystemTrigger = false` parameter to it and pass it through.

In `TryToAvatarScaleSnapshot` signature (around line 1275), add `bool isPaySystemTrigger = false`:

```csharp
private static bool TryToAvatarScaleSnapshot(
    AvatarScaleRule rule,
    AvatarScaleSafetySnapshot safety,
    bool requireTriggerFilter,
    bool isPaySystemTrigger,
    out AvatarScaleRuleSnapshot snapshot)
```

In the `new AvatarScaleRuleSnapshot(...)` call, add at the end:

```csharp
    isPaySystemTrigger);
```

Update all callers of `TryToAvatarScaleSnapshot` to pass the new parameter. Search for `TryToAvatarScaleSnapshot` calls and add `false` for the normal path. The Cash Payment and Power Up callers will pass `true`.

- [ ] **Step 3: Add `HighestSeenActiveTimeSeconds` to restore sequence state**

In `BridgeCoordinator.cs`, update the `ActiveAvatarScaleRestoreSequenceState` record (line 19473):

```csharp
private sealed record ActiveAvatarScaleRestoreSequenceState(
    long SequenceId,
    string AvatarId,
    double CarriedHeightMeters,
    double RestoreHeightMeters,
    DateTimeOffset ActiveUntil,
    string SourceRuleName,
    double RestoreSmoothTransitionSeconds,
    bool RestoreToPaidGrowthIfActive,
    bool IsTest,
    AvatarScaleRuleSnapshot? Rule,
    double HighestSeenActiveTimeSeconds = 0,
    bool IsPaySystemTier = false);
```

- [ ] **Step 4: Add `highestSeenActiveTimeSeconds` coordinator field**

In `BridgeCoordinator.cs`, near line 199 (where `activeAvatarScaleRestoreSequence` is declared), add:

```csharp
private double currentScaleWindowHighestSeenActiveTimeSeconds;
private bool currentScaleWindowIsPaySystemTier;
```

- [ ] **Step 5: Build to verify compilation**

Run: `dotnet build "E:\!!!Program to work on\Proper Crystal Relay\VrcTwitchOscBridge\VrcTwitchOscBridge.csproj" --no-restore`
Expected: Build succeeds (0 errors; existing warnings OK).

- [ ] **Step 6: Commit**

```bash
git add VrcTwitchOscBridge/Services/BridgeRuntimeConfiguration.cs VrcTwitchOscBridge/Services/BridgeCoordinator.cs
git commit -m "feat: add tier and highest-seen fields to scale restore sequence state"
```

---

### Task 2: Write failing test for Tier-1 highest-seen timer behavior

**Files:**
- Create: `VrcTwitchOscBridge.Tests\ScaleTimerFairnessTests.cs`

- [ ] **Step 1: Write the failing test**

Create `VrcTwitchOscBridge.Tests\ScaleTimerFairnessTests.cs`:

```csharp
using System;
using System.Reflection;
using System.Runtime.CompilerServices;
using VrcTwitchOscBridge.Services;
using VrcTwitchOscBridge.Models;
using Xunit;

namespace VrcTwitchOscBridge.Tests;

public class ScaleTimerFairnessTests
{
    private static BridgeCoordinator CreateUninitializedCoordinator()
    {
        return (BridgeCoordinator)RuntimeHelpers.GetUninitializedObject(typeof(BridgeCoordinator));
    }

    private static void SetPrivateField(object target, string fieldName, object? value)
    {
        var field = typeof(BridgeCoordinator).GetField(fieldName, BindingFlags.NonPublic | BindingFlags.Instance);
        Assert.NotNull(field);
        field!.SetValue(target, value);
    }

    private static object? GetPrivateField(object target, string fieldName)
    {
        var field = typeof(BridgeCoordinator).GetField(fieldName, BindingFlags.NonPublic | BindingFlags.Instance);
        Assert.NotNull(field);
        return field!.GetValue(target);
    }

    [Fact]
    public void ScheduleAvatarScaleRestoreSequence_Tier1ThirtySecondAfterSixtySecond_UsesHighestSeenSixty()
    {
        var coordinator = CreateUninitializedCoordinator();
        var stateGate = new object();
        var runtimeCancellation = new CancellationTokenSource();
        SetPrivateField(coordinator, "stateGate", stateGate);
        SetPrivateField(coordinator, "runtimeCancellation", runtimeCancellation);
        SetPrivateField(coordinator, "nextAvatarScaleRestoreSequenceId", 0L);
        SetPrivateField(coordinator, "currentScaleWindowHighestSeenActiveTimeSeconds", 0.0);
        SetPrivateField(coordinator, "currentScaleWindowIsPaySystemTier", false);

        var safety = new AvatarScaleSafetySettings();
        var sixtySecondRule = new AvatarScaleRule
        {
            Name = "Big Grow",
            ActiveTimeSeconds = 60,
            RestoreHeightMeters = 1.6,
            TargetHeightMeters = 2.0,
        };
        var sixtySnapshot = BridgeRuntimeConfiguration.CreateManualTestSnapshot(sixtySecondRule, safety);

        var thirtySecondRule = new AvatarScaleRule
        {
            Name = "Small Shrink",
            ActiveTimeSeconds = 30,
            RestoreHeightMeters = 1.6,
            TargetHeightMeters = 0.8,
        };
        var thirtySnapshot = BridgeRuntimeConfiguration.CreateManualTestSnapshot(thirtySecondRule, safety);

        var scheduleMethod = typeof(BridgeCoordinator).GetMethod(
            "ScheduleAvatarScaleRestoreSequence",
            BindingFlags.NonPublic | BindingFlags.Instance);
        Assert.NotNull(scheduleMethod);

        // First trigger: 60s grow
        scheduleMethod!.Invoke(coordinator, [sixtySnapshot, false, 2.0]);

        var sequenceAfterFirst = (object?)GetPrivateField(coordinator, "activeAvatarScaleRestoreSequence");
        Assert.NotNull(sequenceAfterFirst);
        var activeUntilAfterFirst = (DateTimeOffset)sequenceAfterFirst!.GetType().GetField("ActiveUntil")!.GetValue(sequenceAfterFirst)!;
        var highestAfterFirst = (double)GetPrivateField(coordinator, "currentScaleWindowHighestSeenActiveTimeSeconds")!;
        Assert.Equal(60, highestAfterFirst);
        Assert.True((activeUntilAfterFirst - DateTimeOffset.UtcNow).TotalSeconds >= 55);

        // Second trigger: 30s shrink — should use highest-seen 60s, not 30s
        scheduleMethod.Invoke(coordinator, [thirtySnapshot, false, 0.8]);

        var sequenceAfterSecond = (object?)GetPrivateField(coordinator, "activeAvatarScaleRestoreSequence");
        Assert.NotNull(sequenceAfterSecond);
        var activeUntilAfterSecond = (DateTimeOffset)sequenceAfterSecond!.GetType().GetField("ActiveUntil")!.GetValue(sequenceAfterSecond)!;
        var highestAfterSecond = (double)GetPrivateField(coordinator, "currentScaleWindowHighestSeenActiveTimeSeconds")!;
        Assert.Equal(60, highestAfterSecond);
        // Timer should be ~60s from now (the highest seen), not 30s
        var remainingAfterSecond = (activeUntilAfterSecond - DateTimeOffset.UtcNow).TotalSeconds;
        Assert.True(remainingAfterSecond >= 55, $"Expected >= 55s remaining after 60s highest-seen, got {remainingAfterSecond}");
    }
}
```

- [ ] **Step 2: Run test to verify it fails**

Run: `dotnet test "E:\!!!Program to work on\Proper Crystal Relay\VrcTwitchOscBridge.Tests\VrcTwitchOscBridge.Tests.csproj" --filter ScaleTimerFairnessTests --no-restore`
Expected: FAIL — the current code resets to 30s, so the assertion `remainingAfterSecond >= 55` fails.

- [ ] **Step 3: Commit the failing test**

```bash
git add VrcTwitchOscBridge.Tests/ScaleTimerFairnessTests.cs
git commit -m "test: add failing test for tier-1 highest-seen scale timer"
```

---

### Task 3: Implement Tier-1 highest-seen timer logic in ScheduleAvatarScaleRestoreSequence

**Files:**
- Modify: `VrcTwitchOscBridge\Services\BridgeCoordinator.cs:5275-5323`

- [ ] **Step 1: Rewrite `ScheduleAvatarScaleRestoreSequence` to be tier-aware**

Replace the method at line 5275 with:

```csharp
private void ScheduleAvatarScaleRestoreSequence(
    AvatarScaleRuleSnapshot rule,
    bool isTest,
    double carriedHeightMeters)
{
    var now = DateTimeOffset.UtcNow;
    var avatarId = GetCurrentVrChatAvatarId();
    var sourceRuleName = string.IsNullOrWhiteSpace(rule.Name) ? "Avatar Scale" : rule.Name;
    var restoreHeight = rule.RestoreHeightMeters;
    var newCancellation = runtimeCancellation is null
        ? new CancellationTokenSource()
        : CancellationTokenSource.CreateLinkedTokenSource(runtimeCancellation.Token);
    CancellationTokenSource? previousCancellation = null;
    ActiveAvatarScaleRestoreSequenceState? sequence = null;

    var isPaySystem = rule.IsPaySystemTrigger;
    var ruleActiveTime = Math.Max(0.001, rule.ActiveTimeSeconds);

    lock (stateGate)
    {
        double effectiveActiveTime;
        double newHighestSeen;

        if (isPaySystem)
        {
            // Tier 2 (pay): always reset to own ActiveTimeSeconds, reset highest-seen
            effectiveActiveTime = ruleActiveTime;
            newHighestSeen = ruleActiveTime;
            currentScaleWindowHighestSeenActiveTimeSeconds = newHighestSeen;
            currentScaleWindowIsPaySystemTier = true;
        }
        else if (currentScaleWindowIsPaySystemTier)
        {
            // Tier 1 after Tier 2: Tier-1 cannot shorten a Tier-2 window.
            // Check if the Tier-2 window is still active.
            if (activeAvatarScaleRestoreSequence is { } existing
                && existing.IsPaySystemTier
                && existing.ActiveUntil > now)
            {
                // Tier-2 still running — Tier-1 cannot preempt. Do not reschedule.
                // The height change still goes through (handled by the caller),
                // but the restore timer stays on the Tier-2 schedule.
                newCancellation.Dispose();
                WriteLog($"Avatar scale '{sourceRuleName}' height changed but restore timer stays on the active pay-system schedule.");
                return;
            }

            // Tier-2 expired — start a fresh Tier-1 window
            effectiveActiveTime = ruleActiveTime;
            newHighestSeen = ruleActiveTime;
            currentScaleWindowHighestSeenActiveTimeSeconds = newHighestSeen;
            currentScaleWindowIsPaySystemTier = false;
        }
        else
        {
            // Tier 1 normal: highest-seen rule
            newHighestSeen = Math.Max(currentScaleWindowHighestSeenActiveTimeSeconds, ruleActiveTime);
            effectiveActiveTime = newHighestSeen;
            currentScaleWindowHighestSeenActiveTimeSeconds = newHighestSeen;
        }

        sequence = new ActiveAvatarScaleRestoreSequenceState(
            ++nextAvatarScaleRestoreSequenceId,
            avatarId,
            carriedHeightMeters,
            ApplyAvatarScaleHeightLimits(rule, restoreHeight, "return height"),
            now.AddSeconds(effectiveActiveTime),
            sourceRuleName,
            Math.Max(0, rule.SmoothTransitionSeconds),
            RestoreToPaidGrowthIfActive: true,
            isTest,
            rule,
            HighestSeenActiveTimeSeconds: newHighestSeen,
            IsPaySystemTier: isPaySystem);
        previousCancellation = avatarScaleRestoreSequenceCancellation;
        avatarScaleRestoreSequenceCancellation = newCancellation;
        activeAvatarScaleRestoreSequence = sequence;
        UpdateActiveAvatarScaleCarryoverRestoreSequenceLocked(rule.Id, sequence);
    }

    if (sequence is null)
    {
        newCancellation.Dispose();
        WriteLog($"Avatar scale '{sourceRuleName}' could not schedule its return height reset.");
        return;
    }

    previousCancellation?.Cancel();
    _ = Task.Run(() => RunAvatarScaleRestoreSequenceAsync(sequence, newCancellation), CancellationToken.None);

    var activeSeconds = effectiveActiveTime;
    WriteLog(isTest
        ? $"Avatar scale test/simulated effect '{sourceRuleName}' reset the inactive restore timer for {DescribeDuration(activeSeconds)}."
        : $"Avatar scale '{sourceRuleName}' reset the inactive restore timer for {DescribeDuration(activeSeconds)}.");
}
```

- [ ] **Step 2: Reset highest-seen when restore sequence completes**

In `RunAvatarScaleRestoreSequenceAsync` (around line 5348), find the point where the sequence completes and the restore is sent (search for where it exits the `while` loop successfully or calls `ClearAvatarScaleRestoreSequenceIfCurrent`). Just before or after the sequence is cleared, add under `stateGate`:

```csharp
currentScaleWindowHighestSeenActiveTimeSeconds = 0;
currentScaleWindowIsPaySystemTier = false;
```

Find the `ClearAvatarScaleRestoreSequenceIfCurrent` method and add these resets inside its `lock (stateGate)` block after it nulls `activeAvatarScaleRestoreSequence`.

- [ ] **Step 3: Run test to verify it passes**

Run: `dotnet test "E:\!!!Program to work on\Proper Crystal Relay\VrcTwitchOscBridge.Tests\VrcTwitchOscBridge.Tests.csproj" --filter ScaleTimerFairnessTests --no-restore`
Expected: PASS

- [ ] **Step 4: Build the app project**

Run: `dotnet build "E:\!!!Program to work on\Proper Crystal Relay\VrcTwitchOscBridge\VrcTwitchOscBridge.csproj" --no-restore`
Expected: Build succeeds (0 errors)

- [ ] **Step 5: Commit**

```bash
git add VrcTwitchOscBridge/Services/BridgeCoordinator.cs VrcTwitchOscBridge.Tests/ScaleTimerFairnessTests.cs
git commit -m "feat: implement tier-1 highest-seen and tier-2 pay-system preemption for scale restore timer"
```

---

### Task 4: Write and implement test for Tier-2 pay-system preemption

**Files:**
- Modify: `VrcTwitchOscBridge.Tests\ScaleTimerFairnessTests.cs`
- Modify: `VrcTwitchOscBridge\Services\BridgeCoordinator.cs` (only if test reveals issues)

- [ ] **Step 1: Add the failing test**

Add to `ScaleTimerFairnessTests.cs`:

```csharp
[Fact]
public void ScheduleAvatarScaleRestoreSequence_Tier2PaySystem_ResetsToOwnActiveTime()
{
    var coordinator = CreateUninitializedCoordinator();
    SetPrivateField(coordinator, "stateGate", new object());
    SetPrivateField(coordinator, "runtimeCancellation", new CancellationTokenSource());
    SetPrivateField(coordinator, "nextAvatarScaleRestoreSequenceId", 0L);
    SetPrivateField(coordinator, "currentScaleWindowHighestSeenActiveTimeSeconds", 0.0);
    SetPrivateField(coordinator, "currentScaleWindowIsPaySystemTier", false);

    var safety = new AvatarScaleSafetySettings();

    // First: 60s Tier-1 grow
    var tier1Rule = new AvatarScaleRule { Name = "Grow", ActiveTimeSeconds = 60, RestoreHeightMeters = 1.6, TargetHeightMeters = 2.0 };
    var tier1Snapshot = BridgeRuntimeConfiguration.CreateManualTestSnapshot(tier1Rule, safety);

    // Second: 20s Tier-2 pay-system
    var tier2Rule = new AvatarScaleRule { Name = "Cash Pay", ActiveTimeSeconds = 20, RestoreHeightMeters = 1.6, TargetHeightMeters = 1.4 };
    var tier2Snapshot = BridgeRuntimeConfiguration.CreateManualTestSnapshot(tier2Rule, safety) with { IsPaySystemTrigger = true };

    var scheduleMethod = typeof(BridgeCoordinator).GetMethod(
        "ScheduleAvatarScaleRestoreSequence", BindingFlags.NonPublic | BindingFlags.Instance);
    Assert.NotNull(scheduleMethod);

    scheduleMethod!.Invoke(coordinator, [tier1Snapshot, false, 2.0]);
    scheduleMethod.Invoke(coordinator, [tier2Snapshot, false, 1.4]);

    var sequence = GetPrivateField(coordinator, "activeAvatarScaleRestoreSequence");
    Assert.NotNull(sequence);
    var activeUntil = (DateTimeOffset)sequence!.GetType().GetField("ActiveUntil")!.GetValue(sequence)!;
    var remaining = (activeUntil - DateTimeOffset.UtcNow).TotalSeconds;
    Assert.True(remaining <= 21 && remaining >= 15, $"Tier-2 should reset to ~20s, got {remaining}");
    Assert.True((bool)sequence.GetType().GetField("IsPaySystemTier")!.GetValue(sequence)!);
}

[Fact]
public void ScheduleAvatarScaleRestoreSequence_Tier1AfterTier2Active_DoesNotPreempt()
{
    var coordinator = CreateUninitializedCoordinator();
    SetPrivateField(coordinator, "stateGate", new object());
    SetPrivateField(coordinator, "runtimeCancellation", new CancellationTokenSource());
    SetPrivateField(coordinator, "nextAvatarScaleRestoreSequenceId", 0L);
    SetPrivateField(coordinator, "currentScaleWindowHighestSeenActiveTimeSeconds", 0.0);
    SetPrivateField(coordinator, "currentScaleWindowIsPaySystemTier", false);

    var safety = new AvatarScaleSafetySettings();

    // First: 20s Tier-2 pay-system
    var tier2Rule = new AvatarScaleRule { Name = "Cash Pay", ActiveTimeSeconds = 20, RestoreHeightMeters = 1.6, TargetHeightMeters = 1.4 };
    var tier2Snapshot = BridgeRuntimeConfiguration.CreateManualTestSnapshot(tier2Rule, safety) with { IsPaySystemTrigger = true };

    // Second: 60s Tier-1 grow (should NOT preempt the active Tier-2 window)
    var tier1Rule = new AvatarScaleRule { Name = "Grow", ActiveTimeSeconds = 60, RestoreHeightMeters = 1.6, TargetHeightMeters = 2.0 };
    var tier1Snapshot = BridgeRuntimeConfiguration.CreateManualTestSnapshot(tier1Rule, safety);

    var scheduleMethod = typeof(BridgeCoordinator).GetMethod(
        "ScheduleAvatarScaleRestoreSequence", BindingFlags.NonPublic | BindingFlags.Instance);
    Assert.NotNull(scheduleMethod);

    scheduleMethod!.Invoke(coordinator, [tier2Snapshot, false, 1.4]);
    var sequenceAfterTier2 = GetPrivateField(coordinator, "activeAvatarScaleRestoreSequence");
    var activeUntilAfterTier2 = (DateTimeOffset)sequenceAfterTier2!.GetType().GetField("ActiveUntil")!.GetValue(sequenceAfterTier2)!;

    scheduleMethod.Invoke(coordinator, [tier1Snapshot, false, 2.0]);
    var sequenceAfterTier1 = GetPrivateField(coordinator, "activeAvatarScaleRestoreSequence");

    // The sequence should still be the Tier-2 one (not replaced)
    Assert.Equal(
        sequenceAfterTier2.GetType().GetField("SequenceId")!.GetValue(sequenceAfterTier2),
        sequenceAfterTier1!.GetType().GetField("SequenceId")!.GetValue(sequenceAfterTier1));
    var activeUntilAfterTier1 = (DateTimeOffset)sequenceAfterTier1.GetType().GetField("ActiveUntil")!.GetValue(sequenceAfterTier1)!;
    Assert.Equal(activeUntilAfterTier2, activeUntilAfterTier1);
}
```

- [ ] **Step 2: Run tests to verify they pass**

Run: `dotnet test "E:\!!!Program to work on\Proper Crystal Relay\VrcTwitchOscBridge.Tests\VrcTwitchOscBridge.Tests.csproj" --filter ScaleTimerFairnessTests --no-restore`
Expected: PASS (the implementation from Task 3 should handle these)

- [ ] **Step 3: Commit**

```bash
git add VrcTwitchOscBridge.Tests/ScaleTimerFairnessTests.cs
git commit -m "test: add tier-2 pay-system preemption tests for scale restore timer"
```

---

### Task 5: Wire IsPaySystemTrigger for Cash Payment and Power Up scale actions

**Files:**
- Modify: `VrcTwitchOscBridge\Services\BridgeRuntimeConfiguration.cs` — `TryToCashPaymentSnapshot` and `TryToPowerUpSnapshot`
- Modify: `VrcTwitchOscBridge\Services\BridgeCoordinator.cs` — `SendTestCashPaymentRuleAsync`, `SendTestPowerUpRuleAsync`, and the live execution paths

- [ ] **Step 1: Pass `isPaySystemTrigger: true` from Cash Payment and Power Up snapshot builders**

In `BridgeRuntimeConfiguration.cs`, find `TryToCashPaymentSnapshot` (around line 772). It calls `TryToAvatarScaleSnapshot(rule.ScaleAction, safety, requireTriggerFilter: false, out var scaleSnapshot)`. Update to pass `isPaySystemTrigger: true`:

```csharp
if (!TryToAvatarScaleSnapshot(rule.ScaleAction, safety, requireTriggerFilter: false, isPaySystemTrigger: true, out var scaleSnapshot))
```

Find `TryToPowerUpSnapshot` (around line 1130+). It calls `TryToAvatarScaleSnapshot` for the `ScaleAction`. Update similarly:

```csharp
if (!TryToAvatarScaleSnapshot(rule.ScaleAction, safety, requireTriggerFilter: false, isPaySystemTrigger: true, out var scaleSnapshot))
```

- [ ] **Step 2: Build and run all tests**

Run: `dotnet build "E:\!!!Program to work on\Proper Crystal Relay\VrcTwitchOscBridge\VrcTwitchOscBridge.csproj" --no-restore`
Run: `dotnet test "E:\!!!Program to work on\Proper Crystal Relay\VrcTwitchOscBridge.Tests\VrcTwitchOscBridge.Tests.csproj" --no-restore`
Expected: Build 0 errors, all tests pass

- [ ] **Step 3: Commit**

```bash
git add VrcTwitchOscBridge/Services/BridgeRuntimeConfiguration.cs
git commit -m "feat: mark cash payment and power up scale snapshots as pay-system tier"
```

---

## Part B — Support Extends Active Activities

### Task 6: Add ExtendCurrentActivity and ExtendSeconds to TriggerRule model

**Files:**
- Modify: `VrcTwitchOscBridge\Models\TriggerRule.cs`

- [ ] **Step 1: Add backing fields**

In `TriggerRule.cs`, after line 88 (`private int maxAccumulatedDurationSeconds = 1800;`), add:

```csharp
private bool extendCurrentActivity;
private double extendSeconds;
```

- [ ] **Step 2: Add properties**

Find the `MaxAccumulatedDurationSeconds` property (search for it) and add after it:

```csharp
public bool ExtendCurrentActivity
{
    get => extendCurrentActivity;
    set => SetProperty(ref extendCurrentActivity, value);
}

public double ExtendSeconds
{
    get => extendSeconds;
    set => SetProperty(ref extendSeconds, Math.Max(0, value));
}
```

- [ ] **Step 3: Build to verify**

Run: `dotnet build "E:\!!!Program to work on\Proper Crystal Relay\VrcTwitchOscBridge\VrcTwitchOscBridge.csproj" --no-restore`
Expected: Build succeeds

- [ ] **Step 4: Commit**

```bash
git add VrcTwitchOscBridge/Models/TriggerRule.cs
git commit -m "feat: add ExtendCurrentActivity and ExtendSeconds to TriggerRule model"
```

---

### Task 7: Add ExtendCurrentActivity and ExtendSeconds to AvatarScaleRule model

**Files:**
- Modify: `VrcTwitchOscBridge\Models\AvatarScaleRule.cs`

- [ ] **Step 1: Add backing fields**

In `AvatarScaleRule.cs`, after line 353 (`private int supporterGrowthMaxPaidTimeSeconds = 3600;`), add:

```csharp
private bool extendCurrentActivity;
private double extendSeconds;
```

- [ ] **Step 2: Add properties**

Find the `HasActiveTime` property (line 994) and add before it:

```csharp
public bool ExtendCurrentActivity
{
    get => extendCurrentActivity;
    set
    {
        if (SetAndRaiseScale(ref extendCurrentActivity, value))
        {
            RaisePropertyChanged(nameof(ExtendCurrentActivity));
        }
    }
}

public double ExtendSeconds
{
    get => extendSeconds;
    set => SetAndRaiseScale(ref extendSeconds, Math.Max(0, value));
}
```

- [ ] **Step 3: Build to verify**

Run: `dotnet build "E:\!!!Program to work on\Proper Crystal Relay\VrcTwitchOscBridge\VrcTwitchOscBridge.csproj" --no-restore`
Expected: Build succeeds

- [ ] **Step 4: Commit**

```bash
git add VrcTwitchOscBridge/Models/AvatarScaleRule.cs
git commit -m "feat: add ExtendCurrentActivity and ExtendSeconds to AvatarScaleRule model"
```

---

### Task 8: Add extend fields to TriggerRuleSnapshot and AvatarScaleRuleSnapshot

**Files:**
- Modify: `VrcTwitchOscBridge\Services\BridgeRuntimeConfiguration.cs`

- [ ] **Step 1: Add trailing fields to `TriggerRuleSnapshot`**

In the `TriggerRuleSnapshot` record (line 58-145), add after the last parameter (`bool SubscriptionTier3Enabled = true`):

```csharp
    bool SubscriptionTier3Enabled = true,
    bool ExtendCurrentActivity = false,
    double ExtendSeconds = 0);
```

- [ ] **Step 2: Populate in the `TriggerRuleSnapshot` builder**

Find `CreateSnapshot` / `TryToSnapshot` (around line 966 where `new TriggerRuleSnapshot(...)` is constructed). Add at the end of the constructor call:

```csharp
    rule.ExtendCurrentActivity,
    rule.ExtendSeconds);
```

- [ ] **Step 3: Add trailing fields to `AvatarScaleRuleSnapshot`**

In the `AvatarScaleRuleSnapshot` record (line 195-255), add after `IsPaySystemTrigger`:

```csharp
    bool IsPaySystemTrigger = false,
    bool ExtendCurrentActivity = false,
    double ExtendSeconds = 0);
```

- [ ] **Step 4: Populate in `TryToAvatarScaleSnapshot`**

In the `new AvatarScaleRuleSnapshot(...)` call (around line 1299), add after `isPaySystemTrigger`:

```csharp
    rule.ExtendCurrentActivity,
    rule.ExtendSeconds);
```

- [ ] **Step 5: Build to verify**

Run: `dotnet build "E:\!!!Program to work on\Proper Crystal Relay\VrcTwitchOscBridge\VrcTwitchOscBridge.csproj" --no-restore`
Expected: Build succeeds (fix any snapshot constructor call that now has wrong arg count)

- [ ] **Step 6: Commit**

```bash
git add VrcTwitchOscBridge/Services/BridgeRuntimeConfiguration.cs
git commit -m "feat: add extend fields to TriggerRuleSnapshot and AvatarScaleRuleSnapshot"
```

---

### Task 9: Add extend fields to persistence (PersistedTriggerRule and PersistedAvatarScaleRule)

**Files:**
- Modify: `VrcTwitchOscBridge\Services\SettingsStore.cs`

- [ ] **Step 1: Add fields to `PersistedTriggerRule`**

In `PersistedTriggerRule` (around line 3393), after `MaxAccumulatedDurationSeconds`, add:

```csharp
public bool ExtendCurrentActivity { get; set; }

public double ExtendSeconds { get; set; }
```

- [ ] **Step 2: Map in `ToPersistedRule`**

In `ToPersistedRule` (line 1014), after `MaxAccumulatedDurationSeconds = rule.MaxAccumulatedDurationSeconds,` (line 1049), add:

```csharp
            ExtendCurrentActivity = rule.ExtendCurrentActivity,
            ExtendSeconds = rule.ExtendSeconds,
```

- [ ] **Step 3: Map in `ToRule`**

In `ToRule` (line 1270), after `MaxAccumulatedDurationSeconds = ...` (line 1355-1357), add:

```csharp
            ExtendCurrentActivity = rule.ExtendCurrentActivity,
            ExtendSeconds = rule.ExtendSeconds <= 0 ? 0 : rule.ExtendSeconds,
```

- [ ] **Step 4: Add fields to `PersistedAvatarScaleRule`**

In `PersistedAvatarScaleRule` (around line 3785), after `ActiveTimeSeconds`, add:

```csharp
public bool ExtendCurrentActivity { get; set; }

public double ExtendSeconds { get; set; }
```

- [ ] **Step 5: Map in `ToPersistedAvatarScaleRule`**

In `ToPersistedAvatarScaleRule` (line 1948), after `ActiveTimeSeconds = rule.ActiveTimeSeconds,` (line 1996), add:

```csharp
            ExtendCurrentActivity = rule.ExtendCurrentActivity,
            ExtendSeconds = rule.ExtendSeconds,
```

- [ ] **Step 6: Map in `ToAvatarScaleRule`**

In `ToAvatarScaleRule` (line 2032), after `ActiveTimeSeconds = ...` (search for the assignment), add:

```csharp
            ExtendCurrentActivity = persisted.ExtendCurrentActivity,
            ExtendSeconds = persisted.ExtendSeconds <= 0 ? 0 : persisted.ExtendSeconds,
```

- [ ] **Step 7: Write persistence round-trip test**

Create `VrcTwitchOscBridge.Tests\ExtendFieldsPersistenceTests.cs`:

```csharp
using VrcTwitchOscBridge.Models;
using VrcTwitchOscBridge.Services;
using Xunit;

namespace VrcTwitchOscBridge.Tests;

public class ExtendFieldsPersistenceTests
{
    [Fact]
    public void TriggerRule_ExtendFields_RoundTripThroughPersistence()
    {
        var rule = new TriggerRule
        {
            Name = "Test Bits",
            TriggerType = TwitchTriggerType.Bits,
            ExtendCurrentActivity = true,
            ExtendSeconds = 15,
        };

        var persisted = SettingsStore.ToPersistedRulePublic(rule);
        var restored = SettingsStore.ToRule(persisted);

        Assert.True(restored.ExtendCurrentActivity);
        Assert.Equal(15, restored.ExtendSeconds);
    }

    [Fact]
    public void TriggerRule_ExtendFields_DefaultWhenMissing()
    {
        var persisted = new SettingsStore.PersistedTriggerRule
        {
            Name = "Old Save",
            TriggerType = TwitchTriggerType.Bits,
            // ExtendCurrentActivity and ExtendSeconds not set (default false/0)
        };

        var restored = SettingsStore.ToRule(persisted);

        Assert.False(restored.ExtendCurrentActivity);
        Assert.Equal(0, restored.ExtendSeconds);
    }

    [Fact]
    public void AvatarScaleRule_ExtendFields_RoundTripThroughPersistence()
    {
        var rule = new AvatarScaleRule
        {
            Name = "Test Scale",
            ExtendCurrentActivity = true,
            ExtendSeconds = 20,
        };

        var persisted = SettingsStore.ToPersistedAvatarScaleRulePublic(rule);
        var restored = SettingsStore.ToAvatarScaleRule(persisted);

        Assert.True(restored.ExtendCurrentActivity);
        Assert.Equal(20, restored.ExtendSeconds);
    }
}
```

Note: `ToPersistedRule` is currently `private`. To make it testable, either:
- Change `ToPersistedRule` to `internal` (and add `InternalsVisibleTo` if not already present)
- Or add a public wrapper method `ToPersistedRulePublic` that calls `ToPersistedRule`

Check if `InternalsVisibleTo` is already set for the test project. If so, change `ToPersistedRule` from `private` to `internal`. Same for `ToPersistedAvatarScaleRule`.

- [ ] **Step 8: Run tests to verify they pass**

Run: `dotnet test "E:\!!!Program to work on\Proper Crystal Relay\VrcTwitchOscBridge.Tests\VrcTwitchOscBridge.Tests.csproj" --filter ExtendFieldsPersistenceTests --no-restore`
Expected: PASS

- [ ] **Step 9: Commit**

```bash
git add VrcTwitchOscBridge/Services/SettingsStore.cs VrcTwitchOscBridge.Tests/ExtendFieldsPersistenceTests.cs
git commit -m "feat: persist ExtendCurrentActivity and ExtendSeconds for trigger and scale rules"
```

---

### Task 10: Implement ExtendActiveActivityTimers in BridgeCoordinator

**Files:**
- Modify: `VrcTwitchOscBridge\Services\BridgeCoordinator.cs`

- [ ] **Step 1: Write the failing test**

Create `VrcTwitchOscBridge.Tests\ExtendActiveActivityTests.cs`:

```csharp
using System;
using System.Reflection;
using System.Runtime.CompilerServices;
using VrcTwitchOscBridge.Services;
using VrcTwitchOscBridge.Models;
using Xunit;

namespace VrcTwitchOscBridge.Tests;

public class ExtendActiveActivityTests
{
    private static BridgeCoordinator CreateUninitializedCoordinator()
    {
        return (BridgeCoordinator)RuntimeHelpers.GetUninitializedObject(typeof(BridgeCoordinator));
    }

    private static void SetPrivateField(object target, string fieldName, object? value)
    {
        var field = typeof(BridgeCoordinator).GetField(fieldName, BindingFlags.NonPublic | BindingFlags.Instance);
        Assert.NotNull(field);
        field!.SetValue(target, value);
    }

    private static object? GetPrivateField(object target, string fieldName)
    {
        var field = typeof(BridgeCoordinator).GetField(fieldName, BindingFlags.NonPublic | BindingFlags.Instance);
        Assert.NotNull(field);
        return field!.GetValue(target);
    }

    [Fact]
    public void ExtendActiveActivityTimers_WithActiveScaleSequence_ExtendsActiveUntil()
    {
        var coordinator = CreateUninitializedCoordinator();
        SetPrivateField(coordinator, "stateGate", new object());

        var originalActiveUntil = DateTimeOffset.UtcNow.AddSeconds(20);
        var safety = new AvatarScaleSafetySettings();
        var rule = new AvatarScaleRule { Name = "Grow", ActiveTimeSeconds = 20, RestoreHeightMeters = 1.6 };
        var snapshot = BridgeRuntimeConfiguration.CreateManualTestSnapshot(rule, safety);

        // Simulate an active restore sequence
        var sequenceType = typeof(BridgeCoordinator).GetNestedType(
            "ActiveAvatarScaleRestoreSequenceState", BindingFlags.NonPublic);
        Assert.NotNull(sequenceType);
        var sequence = Activator.CreateInstance(sequenceType!,
            1L, "avatar-id", 2.0, 1.6, originalActiveUntil, "Grow", 0.0,
            true, false, snapshot, 20.0, false);
        SetPrivateField(coordinator, "activeAvatarScaleRestoreSequence", sequence);

        var extendMethod = typeof(BridgeCoordinator).GetMethod(
            "ExtendActiveActivityTimers", BindingFlags.NonPublic | BindingFlags.Instance);
        Assert.NotNull(extendMethod);

        extendMethod!.Invoke(coordinator, [TimeSpan.FromSeconds(15), "Cheer 100"]);

        var updatedSequence = GetPrivateField(coordinator, "activeAvatarScaleRestoreSequence");
        Assert.NotNull(updatedSequence);
        var updatedActiveUntil = (DateTimeOffset)updatedSequence!.GetType().GetField("ActiveUntil")!.GetValue(updatedSequence)!;
        var expected = originalActiveUntil.AddSeconds(15);
        Assert.True(Math.Abs((updatedActiveUntil - expected).TotalSeconds) < 2,
            $"Expected ~{expected:O}, got {updatedActiveUntil:O}");
    }

    [Fact]
    public void ExtendActiveActivityTimers_WithNoActiveActivity_DoesNothing()
    {
        var coordinator = CreateUninitializedCoordinator();
        SetPrivateField(coordinator, "stateGate", new object());
        SetPrivateField(coordinator, "activeAvatarScaleRestoreSequence", null);

        var extendMethod = typeof(BridgeCoordinator).GetMethod(
            "ExtendActiveActivityTimers", BindingFlags.NonPublic | BindingFlags.Instance);
        Assert.NotNull(extendMethod);

        // Should not throw when nothing is active
        extendMethod!.Invoke(coordinator, [TimeSpan.FromSeconds(15), "Cheer 100"]);

        Assert.Null(GetPrivateField(coordinator, "activeAvatarScaleRestoreSequence"));
    }
}
```

- [ ] **Step 2: Run test to verify it fails**

Run: `dotnet test "E:\!!!Program to work on\Proper Crystal Relay\VrcTwitchOscBridge.Tests\VrcTwitchOscBridge.Tests.csproj" --filter ExtendActiveActivityTests --no-restore`
Expected: FAIL — `ExtendActiveActivityTimers` method does not exist.

- [ ] **Step 3: Implement `ExtendActiveActivityTimers`**

In `BridgeCoordinator.cs`, add a new method (place it near `ScheduleAvatarScaleRestoreSequence`, around line 5320):

```csharp
private void ExtendActiveActivityTimers(TimeSpan extension, string sourceLabel)
{
    var now = DateTimeOffset.UtcNow;
    var anyExtended = false;

    lock (stateGate)
    {
        // Extend avatar scale restore sequence
        if (activeAvatarScaleRestoreSequence is { } scaleSequence
            && scaleSequence.ActiveUntil > now)
        {
            var newActiveUntil = scaleSequence.ActiveUntil.Add(extension);
            activeAvatarScaleRestoreSequence = scaleSequence with { ActiveUntil = newActiveUntil };
            anyExtended = true;
            WriteLog($"Extended avatar scale '{scaleSequence.SourceRuleName}' by {extension.TotalSeconds:0.#} seconds from {sourceLabel}.");
        }

        // Extend active float redeem sessions
        foreach (var kvp in activeFloatRedeemSessions.ToArray())
        {
            if (kvp.Value.ActiveUntil > now)
            {
                kvp.Value.ActiveUntil = kvp.Value.ActiveUntil.Add(extension);
                anyExtended = true;
            }
        }

        // Extend active supporter override
        if (activeSupporterOverride is { } supporterOverride
            && supporterOverride.ActiveUntil > now)
        {
            supporterOverride.ActiveUntil = supporterOverride.ActiveUntil.Add(extension);
            anyExtended = true;
        }

        // Extend pending resets (avatar swap / roulette timed resets)
        foreach (var kvp in pendingResets.ToArray())
        {
            if (kvp.Value.DueAt > now)
            {
                var updated = kvp.Value with { DueAt = kvp.Value.DueAt.Add(extension) };
                pendingResets[kvp.Key] = updated;
                anyExtended = true;
            }
        }

        // Extend movement lanes
        foreach (var kvp in actionLanes.ToArray())
        {
            if (kvp.Value.BusyUntil > now)
            {
                actionLanes[kvp.Key] = kvp.Value with { BusyUntil = kvp.Value.BusyUntil.Add(extension) };
                anyExtended = true;
            }
        }
    }

    if (!anyExtended)
    {
        WriteLog($"{sourceLabel} received — no active activity to extend.");
    }
}
```

Note: The `ActiveFloatRedeemSessionState.ActiveUntil` and `ActiveSupporterOverrideState.ActiveUntil` fields are marked as settable in the exploration report. The `PendingResetState` and `ActiveMovementLaneState` are records (support `with` expressions). Verify field mutability when implementing — if `ActiveUntil` is init-only on the float session, use `with` expression instead.

- [ ] **Step 4: Run test to verify it passes**

Run: `dotnet test "E:\!!!Program to work on\Proper Crystal Relay\VrcTwitchOscBridge.Tests\VrcTwitchOscBridge.Tests.csproj" --filter ExtendActiveActivityTests --no-restore`
Expected: PASS

- [ ] **Step 5: Commit**

```bash
git add VrcTwitchOscBridge/Services/BridgeCoordinator.cs VrcTwitchOscBridge.Tests/ExtendActiveActivityTests.cs
git commit -m "feat: implement ExtendActiveActivityTimers in BridgeCoordinator"
```

---

### Task 11: Wire extend-path dispatch in coordinator event handling

**Files:**
- Modify: `VrcTwitchOscBridge\Services\BridgeCoordinator.cs`

This task connects the extend fields on snapshots to the `ExtendActiveActivityTimers` method. When a Bits/Subs/Cash/PowerUp event fires and the rule has `ExtendCurrentActivity = true`, the coordinator calls `ExtendActiveActivityTimers` instead of executing the rule's own action.

- [ ] **Step 1: Find the Bits/Subs execution entry point**

Search for where Bits events are handled in `BridgeCoordinator.cs`. Look for `HandleBitsEventAsync` or similar, and where `TriggerRuleSnapshot` is checked for supporter override behavior. The entry point will be where the snapshot's `TriggerType == Bits` is dispatched.

- [ ] **Step 2: Add extend check at the dispatch point**

At the top of the Bits/Subs event handler, after the snapshot is resolved but before the action executes, add:

```csharp
if (ruleSnapshot.ExtendCurrentActivity && ruleSnapshot.ExtendSeconds > 0)
{
    ExtendActiveActivityTimers(TimeSpan.FromSeconds(ruleSnapshot.ExtendSeconds), $"Cheer {event.Amount}");
    return;
}
```

For Subs:
```csharp
if (ruleSnapshot.ExtendCurrentActivity && ruleSnapshot.ExtendSeconds > 0)
{
    ExtendActiveActivityTimers(TimeSpan.FromSeconds(ruleSnapshot.ExtendSeconds), "Sub");
    return;
}
```

- [ ] **Step 3: Add extend check for Cash Payment scale actions**

In the Cash Payment execution path (where `CashPaymentRuleSnapshot.ScaleAction` is used), add:

```csharp
if (cashSnapshot.ScaleAction is { ExtendCurrentActivity: true, ExtendSeconds: > 0 } scaleAction)
{
    ExtendActiveActivityTimers(TimeSpan.FromSeconds(scaleAction.ExtendSeconds), "Donation");
    return;
}
```

- [ ] **Step 4: Add extend check for Power Up scale actions**

In the Power Up execution path (where `PowerUpRuleSnapshot.ScaleAction` is used), add:

```csharp
if (powerUpSnapshot.ScaleAction is { ExtendCurrentActivity: true, ExtendSeconds: > 0 } scaleAction)
{
    ExtendActiveActivityTimers(TimeSpan.FromSeconds(scaleAction.ExtendSeconds), "Power Up");
    return;
}
```

- [ ] **Step 5: Add extend check for Supporter Growth scale rules**

In the Supporter Growth execution path, add:

```csharp
if (scaleSnapshot.ExtendCurrentActivity && scaleSnapshot.ExtendSeconds > 0)
{
    ExtendActiveActivityTimers(TimeSpan.FromSeconds(scaleSnapshot.ExtendSeconds), "Supporter Growth");
    return;
}
```

- [ ] **Step 6: Build to verify**

Run: `dotnet build "E:\!!!Program to work on\Proper Crystal Relay\VrcTwitchOscBridge\VrcTwitchOscBridge.csproj" --no-restore`
Expected: Build succeeds

- [ ] **Step 7: Commit**

```bash
git add VrcTwitchOscBridge/Services/BridgeCoordinator.cs
git commit -m "feat: dispatch to ExtendActiveActivityTimers when extend toggle is enabled"
```

---

## Part C — UI & Localization

### Task 12: Add localization keys for extend feature

**Files:**
- Modify: all 14 `VrcTwitchOscBridge\Resources\Localization\*.extra.json` files

- [ ] **Step 1: Add keys to en-US.extra.json**

In `en-US.extra.json`, add these keys (near the existing `All Sources` / `Avatar Scaling Card Test` keys around line 204):

```json
  "Extend current activity": "Extend current activity",
  "Extend the current active activity instead of running this rule's action": "Extend the current active activity instead of running this rule's action",
  "Extend by (seconds)": "Extend by (seconds)",
  "No active activity to extend": "No active activity to extend",
  "Extended {0} by {1} seconds": "Extended {0} by {1} seconds",
  "Cheer {0} received — no active activity to extend": "Cheer {0} received — no active activity to extend",
  "Sub received — no active activity to extend": "Sub received — no active activity to extend",
  "Donation received — no active activity to extend": "Donation received — no active activity to extend",
```

- [ ] **Step 2: Add translated keys to all 13 other language files**

For each language file, add the same keys with appropriate translations:

- **de-DE.extra.json**: "Aktuelle Aktivität verlängern", "Verlängert die aktuell aktive Aktivität, anstatt die Aktion dieser Regel auszuführen", "Verlängern um (Sekunden)", "Keine aktive Aktivität zum Verlängern", "{0} um {1} Sekunden verlängert", "Cheer {0} erhalten — keine aktive Aktivität zum Verlängern", "Sub erhalten — keine aktive Aktivität zum Verlängern", "Spende erhalten — keine aktive Aktivität zum Verlängern"
- **es-ES.extra.json**: "Extender actividad actual", "Extiende la actividad actual en curso en lugar de ejecutar la acción de esta regla", "Extender por (segundos)", "Sin actividad activa para extender", "{0} extendido por {1} segundos", "Cheer {0} recibido — sin actividad activa para extender", "Sub recibido — sin actividad activa para extender", "Donación recibida — sin actividad activa para extender"
- **fr-FR.extra.json**: "Étendre l'activité actuelle", "Étend l'activité en cours au lieu d'exécuter l'action de cette règle", "Étendre de (secondes)", "Aucune activité active à étendre", "{0} étendu de {1} secondes", "Cheer {0} reçu — aucune activité active à étendre", "Sub reçu — aucune activité active à étendre", "Don reçue — aucune activité active à étendre"
- **it-IT.extra.json**: "Estendi attività attuale", "Estende l'attività attiva corrente invece di eseguire l'azione di questa regola", "Estendi di (secondi)", "Nessuna attività attiva da estendere", "{0} esteso di {1} secondi", "Cheer {0} ricevuto — nessuna attività attiva da estendere", "Sub ricevuto — nessuna attività attiva da estendere", "Donazione ricevuta — nessuna attività attiva da estendere"
- **ja-JP.extra.json**: "現在のアクティビティを延長", "このルールのアクションを実行する代わりに、現在進行中のアクティビティを延長します", "延長（秒）", "延長できるアクティブなアクティビティがありません", "{0}を{1}秒延長しました", "Cheer {0}を受信 — 延長できるアクティブなアクティビティがありません", "Subを受信 — 延長できるアクティブなアクティビティがありません", "寄付を受信 — 延長できるアクティブなアクティビティがありません"
- **ko-KR.extra.json**: "현재 활동 연장", "이 규칙의 동작을 실행하는 대신 현재 진행 중인 활동을 연장합니다", "연장 시간 (초)", "연장할 활성 활동이 없습니다", "{0}을 {1}초 연장했습니다", "Cheer {0} 수신 — 연장할 활성 활동이 없습니다", "Sub 수신 — 연장할 활성 활동이 없습니다", "기부 수신 — 연장할 활성 활동이 없습니다"
- **pl-PL.extra.json**: "Wydłuż bieżącą aktywność", "Wydłuża trwającą aktywność zamiast wykonywać akcję tej reguły", "Wydłuż o (sekundy)", "Brak aktywnej aktywności do wydłużenia", "Wydłużono {0} o {1} sekund", "Cheer {0} otrzymany — brak aktywnej aktywności do wydłużenia", "Sub otrzymany — brak aktywnej aktywności do wydłużenia", "Darowizna otrzymana — brak aktywnej aktywności do wydłużenia"
- **pt-BR.extra.json**: "Estender atividade atual", "Estende a atividade atual em andamento em vez de executar a ação desta regra", "Estender por (segundos)", "Nenhuma atividade ativa para estender", "{0} estendido por {1} segundos", "Cheer {0} recebido — nenhuma atividade ativa para estender", "Sub recebido — nenhuma atividade ativa para estender", "Doação recebida — nenhuma atividade ativa para estender"
- **ru-RU.extra.json**: "Продлить текущую активность", "Продлевает текущую активность вместо выполнения действия этого правила", "Продлить на (секунд)", "Нет активной активности для продления", "{0} продлён на {1} секунд", "Cheer {0} получен — нет активной активности для продления", "Sub получен — нет активной активности для продления", "Донат получен — нет активной активности для продления"
- **sv-SE.extra.json**: "Förläng aktuell aktivitet", "Förlänger den pågående aktiviteten istället för att köra den här regelns åtgärd", "Förläng med (sekunder)", "Ingen aktiv aktivitet att förlänga", "Förlängde {0} med {1} sekunder", "Cheer {0} mottagen — ingen aktiv aktivitet att förlänga", "Sub mottagen — ingen aktiv aktivitet att förlänga", "Donation mottagen — ingen aktiv aktivitet att förlänga"
- **th-TH.extra.json**: "ขยายเวลากิจกรรมปัจจุบัน", "ขยายเวลากิจกรรมที่กำลังทำอยู่แทนการทำงานของกฎนี้", "ขยายโดย (วินาที)", "ไม่มีกิจกรรมที่ใช้งานอยู่ให้ขยายเวลา", "ขยายเวลา {0} ไป {1} วินาที", "ได้รับ Cheer {0} — ไม่มีกิจกรรมที่ใช้งานอยู่ให้ขยายเวลา", "ได้รับ Sub — ไม่มีกิจกรรมที่ใช้งานอยู่ให้ขยายเวลา", "ได้รับการบริจาค — ไม่มีกิจกรรมที่ใช้งานอยู่ให้ขยายเวลา"
- **zh-CN.extra.json**: "延长当前活动", "延长当前正在进行的活动，而不是执行此规则的动作", "延长（秒）", "没有活动中的活动可延长", "将{0}延长了{1}秒", "收到 Cheer {0} — 没有活动中的活动可延长", "收到 Sub — 没有活动中的活动可延长", "收到捐款 — 没有活动中的活动可延长"
- **zh-TW.extra.json**: "延長當前活動", "延長當前正在進行的活動，而不是執行此規則的動作", "延長（秒）", "沒有進行中的活動可延長", "將{0}延長了{1}秒", "收到 Cheer {0} — 沒有進行中的活動可延長", "收到 Sub — 沒有進行中的活動可延長", "收到捐款 — 沒有進行中的活動可延長"

- [ ] **Step 3: Run localization audit**

Run: `dotnet run --project "E:\!!!Program to work on\Proper Crystal Relay\LocalizationAudit\LocalizationAudit.csproj"`
Expected: No new missing-key errors for the 8 new keys

- [ ] **Step 4: Commit**

```bash
git add VrcTwitchOscBridge/Resources/Localization/
git commit -m "feat: add localization keys for extend current activity feature"
```

---

### Task 13: Add extend toggle UI to SupporterOverrideTimeSettingsWindow (Bits/Subs)

**Files:**
- Modify: `VrcTwitchOscBridge\SupporterOverrideTimeSettingsWindow.xaml`

- [ ] **Step 1: Add extend toggle and seconds field**

In `SupporterOverrideTimeSettingsWindow.xaml`, after the "Max Added Time" panel (around line 304), add a new section:

```xml
<Border Background="{DynamicResource PanelBrush}"
        BorderBrush="{DynamicResource BorderBrush}"
        BorderThickness="1"
        CornerRadius="10"
        Padding="12"
        Margin="0,16,0,0">
    <StackPanel>
        <TextBlock Text="{loc:Translate 'Extend current activity'}"
                   FontWeight="Bold"
                   FontSize="13"
                   Margin="0,0,0,8" />
        <CheckBox IsChecked="{Binding ExtendCurrentActivity, Mode=TwoWay}"
                  Content="{loc:Translate 'Extend the current active activity instead of running this rule\'s action'}"
                  Margin="0,0,0,8" />
        <StackPanel Orientation="Horizontal"
                    Margin="0,0,0,0"
                    Visibility="{Binding ExtendCurrentActivity, Converter={StaticResource BoolToVisibilityConverter}}">
            <TextBlock Text="{loc:Translate 'Extend by (seconds)'}"
                       VerticalAlignment="Center"
                       Margin="0,0,8,0" />
            <TextBox Text="{Binding ExtendSeconds, UpdateSourceTrigger=PropertyChanged}"
                     MinWidth="80" />
        </StackPanel>
    </StackPanel>
</Border>
```

Note: verify `BoolToVisibilityConverter` is declared in this window's resources. If not, add `<BooleanToVisibilityConverter x:Key="BoolToVisibilityConverter" />` to `Window.Resources`.

- [ ] **Step 2: Build to verify**

Run: `dotnet build "E:\!!!Program to work on\Proper Crystal Relay\VrcTwitchOscBridge\VrcTwitchOscBridge.csproj" --no-restore`
Expected: Build succeeds

- [ ] **Step 3: Commit**

```bash
git add VrcTwitchOscBridge/SupporterOverrideTimeSettingsWindow.xaml
git commit -m "feat: add extend current activity toggle to Bits/Subs time settings window"
```

---

### Task 14: Add extend toggle UI to Avatar Scaling editor

**Files:**
- Modify: `VrcTwitchOscBridge\AvatarScalingManagerWindow.xaml`

- [ ] **Step 1: Find the scale rule editor section**

Search in `AvatarScalingManagerWindow.xaml` for the scale rule editor fields (where `ActiveTimeSeconds` or `RestoreHeightMeters` is bound). This is where the extend toggle goes for Bits/Subs/SupporterGrowth scale rules.

- [ ] **Step 2: Add extend toggle and seconds field**

After the Active Time / Restore Height section, add:

```xml
<Border Background="{DynamicResource NestedPanelBrush}"
        BorderBrush="{DynamicResource BorderBrush}"
        BorderThickness="1"
        CornerRadius="10"
        Padding="10"
        Margin="0,8,0,0">
    <StackPanel>
        <CheckBox IsChecked="{Binding SelectedAvatarScaleRule.ExtendCurrentActivity, Mode=TwoWay}"
                  Content="{loc:Translate 'Extend the current active activity instead of running this rule\'s action'}"
                  Margin="0,0,0,6" />
        <StackPanel Orientation="Horizontal"
                    Visibility="{Binding SelectedAvatarScaleRule.ExtendCurrentActivity, Converter={StaticResource BoolToVisibilityConverter}}">
            <TextBlock Text="{loc:Translate 'Extend by (seconds)'}"
                       VerticalAlignment="Center"
                       Margin="0,0,8,0" />
            <TextBox Text="{Binding SelectedAvatarScaleRule.ExtendSeconds, UpdateSourceTrigger=PropertyChanged}"
                     MinWidth="80" />
        </StackPanel>
    </StackPanel>
</Border>
```

- [ ] **Step 3: Build to verify**

Run: `dotnet build "E:\!!!Program to work on\Proper Crystal Relay\VrcTwitchOscBridge\VrcTwitchOscBridge.csproj" --no-restore`
Expected: Build succeeds

- [ ] **Step 4: Commit**

```bash
git add VrcTwitchOscBridge/AvatarScalingManagerWindow.xaml
git commit -m "feat: add extend current activity toggle to avatar scaling editor"
```

---

### Task 15: Add extend toggle UI to Power Up and Cash Payment editors

**Files:**
- Modify: `VrcTwitchOscBridge\MainWindow.xaml` — Power Up editor section (lines ~4695-4949)
- Modify: `VrcTwitchOscBridge\UserControls\InlineCashPaymentRuleEditorControl.xaml` — Cash Payment editor

- [ ] **Step 1: Add extend toggle to Power Up editor**

In `MainWindow.xaml`, in the Power Up detail editor (around line 4897-4949, after the Fixed Float Add section), add:

```xml
<Border Background="{DynamicResource PanelHighlightBrush}"
        BorderBrush="{DynamicResource HighlightBorderBrush}"
        BorderThickness="1"
        CornerRadius="10"
        Padding="10"
        Margin="0,12,0,0">
    <StackPanel>
        <CheckBox IsChecked="{Binding SelectedPowerUpRule.ScaleAction.ExtendCurrentActivity, Mode=TwoWay}"
                  Content="{loc:Translate 'Extend the current active activity instead of running this rule\'s action'}"
                  Margin="0,0,0,6" />
        <StackPanel Orientation="Horizontal"
                    Visibility="{Binding SelectedPowerUpRule.ScaleAction.ExtendCurrentActivity, Converter={StaticResource BoolToVisibilityConverter}}">
            <TextBlock Text="{loc:Translate 'Extend by (seconds)'}"
                       VerticalAlignment="Center"
                       Margin="0,0,8,0" />
            <TextBox Text="{Binding SelectedPowerUpRule.ScaleAction.ExtendSeconds, UpdateSourceTrigger=PropertyChanged}"
                     MinWidth="80" />
        </StackPanel>
    </StackPanel>
</Border>
```

- [ ] **Step 2: Add extend toggle to Cash Payment editor**

In `InlineCashPaymentRuleEditorControl.xaml` (or the Cash Payment editor in MainWindow.xaml, wherever the scale action is configured), add a similar section bound to `ScaleAction.ExtendCurrentActivity` and `ScaleAction.ExtendSeconds`.

- [ ] **Step 3: Build to verify**

Run: `dotnet build "E:\!!!Program to work on\Proper Crystal Relay\VrcTwitchOscBridge\VrcTwitchOscBridge.csproj" --no-restore`
Expected: Build succeeds

- [ ] **Step 4: Commit**

```bash
git add VrcTwitchOscBridge/MainWindow.xaml VrcTwitchOscBridge/UserControls/InlineCashPaymentRuleEditorControl.xaml
git commit -m "feat: add extend current activity toggle to Power Up and Cash Payment editors"
```

---

## Part D — Final Verification

### Task 16: Run full build, tests, and localization audit

- [ ] **Step 1: Full build**

Run: `dotnet build "E:\!!!Program to work on\Proper Crystal Relay\VrcTwitchOscBridge\VrcTwitchOscBridge.csproj" --no-restore`
Expected: 0 errors

- [ ] **Step 2: Full test suite**

Run: `dotnet test "E:\!!!Program to work on\Proper Crystal Relay\VrcTwitchOscBridge.Tests\VrcTwitchOscBridge.Tests.csproj" --no-restore`
Expected: All tests pass

- [ ] **Step 3: Localization audit**

Run: `dotnet run --project "E:\!!!Program to work on\Proper Crystal Relay\LocalizationAudit\LocalizationAudit.csproj"`
Expected: No new missing-key or empty-value errors for the 8 new keys

- [ ] **Step 4: Commit any final fixes**

```bash
git add -A
git commit -m "chore: final verification for scale timer fairness and support extension"
```

---

## Self-Review Notes

**Spec coverage check:**
- Part A (scale timer fairness): Tasks 1-5 ✓
- Part B (support extends active activities): Tasks 6-11 ✓
- Part C (UI & localization): Tasks 12-15 ✓
- Part D (testing & verification): Task 16 + tests in Tasks 2-4, 9, 10 ✓

**Type consistency:**
- `ExtendCurrentActivity` (bool) and `ExtendSeconds` (double) used consistently across TriggerRule, AvatarScaleRule, TriggerRuleSnapshot, AvatarScaleRuleSnapshot, PersistedTriggerRule, PersistedAvatarScaleRule
- `IsPaySystemTrigger` (bool) on AvatarScaleRuleSnapshot and ActiveAvatarScaleRestoreSequenceState
- `HighestSeenActiveTimeSeconds` (double) and `IsPaySystemTier` (bool) on ActiveAvatarScaleRestoreSequenceState
- `currentScaleWindowHighestSeenActiveTimeSeconds` (double) and `currentScaleWindowIsPaySystemTier` (bool) coordinator fields

**Important implementation notes:**
- The `TryToAvatarScaleSnapshot` method signature changes in Task 1 Step 2 — all callers must be updated
- The `ToPersistedRule` method may need visibility changed from `private` to `internal` for test access in Task 9
- The `ExtendActiveActivityTimers` method extends all active timed states simultaneously (scale, float, supporter override, pending resets, movement) per the spec's "extend all" decision
- Test mode paths do NOT use the extend behavior (only live events)
