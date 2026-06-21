# Per-Avatar Avatar Swap: Add Bits/Subs Time to Swap

Date: 2026-06-20
Lane: in-progress 3.1.9 beta 4
Status: design approved, pending user spec review

## Problem

The per-avatar Bits and Subs triggers inside the new Avatar Swap manager window have a fixed Active Time. There is no built-in way for a streamer to say "let viewers add time to the running avatar swap by cheering bits or subscribing" without also flipping the global "Add amount to active time" toggle (which is the old Bits+Subs override behavior shared with the global supporter override system).

The user wants a per-avatar option that is **independent** of the global toggle: when ON, every matching Bits or Subs trigger extends the current avatar swap by `Active Time + scaled amount`; when OFF, only the fixed Active Time runs. The option should work whether or not the chat keyword is required.

## Goal

When `AddBitsToSwapTime` is ON on a per-avatar Bits or Subs rule:

- The rule uses the existing Bits/Subs scaled math (`DurationSeconds + scaled`) to extend the running avatar swap.
- The cap (`MaxAccumulatedDuration`) still applies as the ceiling on the total running time.
- The option is independent of the global `AmountScaledDurationEnabled` toggle.

When the option is OFF:

- The rule uses `max(1, DurationSeconds)` as a fixed duration. Bits/Subs events do not extend the swap.

The option applies to both Bits and Subs triggers in the per-avatar Avatar Swap manager. It is purely a per-avatar-editor feature and does not change the global override behavior.

## Non-goals

- No change to the global Bits+Subs override behavior (the previous task already shipped that change).
- No change to the per-avatar Channel Point or Payment triggers.
- No change to the Avatar Roulette or Avatar Set action paths.
- No change to the cap, cooldown, queue, or preemption logic.
- No change to the `AmountScaledDurationEnabled` field or its help text. The new field is a parallel, per-avatar-specific option.

## Current architecture (ground truth from code reading)

### Model
- `Models/TriggerRule.cs:50` - `TriggerRule` class. The per-avatar Bits/Subs rules reuse this class; they are not a new model.
- `Models/TriggerRule.cs:76-87` - the bits/subs fields block. We will add `addBitsToSwapTime` here.
- `Models/TriggerRule.cs:1335-1371` - computed properties: `UsesAmountThreshold`, `UsesAmountScaledDuration`, etc. We will add `UsesAddBitsToSwapTime` here.
- `Models/TriggerRule.cs:1619-1629` - `DurationHelpText`. Unchanged for this task.
- `Models/TriggerRule.cs:1638-1703` - `TriggerSummary`. Will gain a mention of the new flag for Bits and Subs branches.
- `Models/AvatarSwapProfile.cs:17-20` - four `ObservableCollection<TriggerRule>` collections (ChannelPoint, Bits, Subs, Payment). The Bits and Subs collections get the new toggle surfaced in their editor.

### Snapshot
- `Services/BridgeRuntimeConfiguration.cs:58-143` - `TriggerRuleSnapshot` immutable record. The bits/subs duration fields are mirrored here (lines 80-91). We will add `AddBitsToSwapTime` to the record.
- `Services/BridgeRuntimeConfiguration.cs:466-484` - per-avatar Bits/Subs rules are added to the runtime rule index with `isGlobalOverride: true`. The new field flows through the same path.

### Runtime math
- `Services/Support/SupportOverrideDurationMath.cs:7-20` - `ComputePerEventAddSeconds`. The single function that runs the math. We will branch the gate to also check the new field.
- `Services/BridgeCoordinator.cs:6412-6423` - `GetSupporterOverrideDuration`. Wraps the helper in a `TimeSpan`. Unchanged.
- `Services/BridgeCoordinator.cs:6517-6537` - `ClampSupporterOverrideAddedDuration`. Cap logic. Unchanged.
- `Services/BridgeCoordinator.cs:8551` - `ExtendActiveSupporterOverrideAsync`. The function that extends the running timer. Unchanged.
- `Services/BridgeCoordinator.cs:8189-8223` - `ResolveAvatarSwapAction`. Picks the target avatar and return avatar. Unchanged.

### UI
- `UserControls/InlineRuleEditorControl.xaml:162-210` - Bits section of the per-avatar editor. We will add a new checkbox at the top of this section.
- `UserControls/InlineRuleEditorControl.xaml:213-259` - Subs section of the per-avatar editor. We will add the same checkbox here.
- `UserControls/InlineBitsRuleRowViewModel.cs:40-63` - row summary text. We will add a chip for the new toggle.
- `UserControls/InlineSubsRuleRowViewModel.cs` - same row summary treatment.
- `Resources/Localization/en-US.json` and 14 non-English `.json` files - add the new key `"Add bits time to swap"`.

### Persistence and migration
- `Services/SettingsStore.cs:3074-3096` - `PersistedAvatarSwapProfile`. Unchanged.
- `Services/SettingsStore.cs:3160-3315` - `PersistedTriggerRule`. Add `public bool AddBitsToSwapTime { get; set; }`.
- `Services/SettingsStore.cs:1026-1040` and `1267-1295` - `ToPersistedRule` / `ToRule` mapping. Add the field to both.
- `Services/AvatarSwapMigrationService.cs:9` - `CurrentMigrationVersion = 5`. Bump to 6.
- `Services/AvatarSwapMigrationService.cs:161-302` - existing V3->V4 and V4->V5 migrations. Add a V5->V6 method that defaults the new field for legacy saves.

### Tests
- `VrcTwitchOscBridge.Tests/TestTriggerRuleSnapshotBuilder.cs` - update `Build` to accept and override `addBitsToSwapTime` (default `false`).
- `VrcTwitchOscBridge.Tests/SupportOverrideDurationMathTests.cs` - add 5 new test cases for the new field.
- `VrcTwitchOscBridge.Tests/InlineRuleRowViewModelTests.cs` - add 2 new test cases for the row summary.
- `VrcTwitchOscBridge.Tests/AvatarSwapMigrationService{V5,V6}Tests.cs` - add V6 tests that cover the defaulting.

## Design

### Math change (the only behavior change)

`Services/Support/SupportOverrideDurationMath.cs` becomes:

```csharp
public static double ComputePerEventAddSeconds(
    TriggerRuleSnapshot rule, int amount, string subscriptionTier)
{
    var baseSeconds = Math.Max(0, rule.DurationSeconds);
    if (!UsesScaledMath(rule))
    {
        return Math.Max(1, baseSeconds);
    }
    var scaled = ComputeScaledSeconds(rule, amount, subscriptionTier);
    return baseSeconds + scaled;
}

private static bool UsesScaledMath(TriggerRuleSnapshot rule) =>
    rule.AmountScaledDurationEnabled || rule.AddBitsToSwapTime;
```

The new `AddBitsToSwapTime` field is a parallel gate. The math function runs scaled add if either `AmountScaledDurationEnabled` (the global override toggle) is on OR `AddBitsToSwapTime` (the new per-avatar option) is on. When both are on, the math runs once - no double-add.

The per-avatar Bits and Subs rules in the Avatar Swap manager go through this same function. With the new field added, a per-avatar rule can have `AmountScaledDurationEnabled = false` (which the global override context would see) and `AddBitsToSwapTime = true` (which the per-avatar editor surfaces). The two flags are independent; only their union matters to the math.

### Cap behavior (unchanged)

`ClampSupporterOverrideAddedDuration` is unchanged. It still receives `requestedDuration` (now `base + scaled` when the new field is on) and `existingRemainingDuration` and clamps the requested add so the total running time never exceeds `MaxAccumulatedDurationSeconds`.

Worked example with cap = 1800s, base = 30s, Bits 1s/50bits, current remaining = 1750s, cheer = 200 bits, `AddBitsToSwapTime = true`:

- `scaled = 200/50 * 1 = 4s`
- `requested = 30 + 4 = 34s`
- `remainingCapacity = 1800 - 1750 = 50s`
- `requested (34) <= remainingCapacity (50)` -> add 34s -> new remaining = 1784s

If the same cheer lands when remaining = 1790s:

- `requested = 34s`
- `remainingCapacity = 10s`
- Clamped to 10s -> new remaining = 1800s (capped)

### Model and snapshot

`Models/TriggerRule.cs` gains a new field and property:

```csharp
private bool addBitsToSwapTime;

public bool AddBitsToSwapTime
{
    get => addBitsToSwapTime;
    set => SetField(ref addBitsToSwapTime, value);
}

public bool UsesAddBitsToSwapTime => UsesAmountThreshold && AddBitsToSwapTime;
```

The default in the `TriggerRule` constructor is `false`. The field is included in `Equals` / `GetHashCode` (which the existing `SetField` helper handles) and in the property-change notifications.

`TriggerRuleSnapshot` (line 58-143 in `BridgeRuntimeConfiguration.cs`) gains a new positional parameter:

```csharp
public sealed record TriggerRuleSnapshot(
    ...existing 80+ parameters...,
    bool AddBitsToSwapTime,
    ...more existing parameters...);
```

The snapshot's `ToPersistedRule` / `ToRule` mapping copies the field through. The default value for legacy data (after the V5->V6 migration) is `false`.

### UI

`UserControls/InlineRuleEditorControl.xaml` Bits section (around line 175), inserted at the top of the `Bits Settings` border:

```xml
<CheckBox Content="{loc:Translate 'Add bits time to swap'}"
          IsChecked="{Binding Rule.AddBitsToSwapTime, UpdateSourceTrigger=PropertyChanged}"
          Margin="0,0,0,8" />
```

Same checkbox added at the top of the Subs Settings border (around line 213). The existing Bits (X) / Seconds (Y) inputs and the T1/T2/T3 inputs stay where they are; the new toggle gates whether they're used in the math.

`UserControls/InlineBitsRuleRowViewModel.cs` (and Subs variant) get a new `+swap time` chip appended to the summary when the toggle is on, following the same style as the existing `+keyword` / `+cap` chips.

`Models/TriggerRule.cs:1638-1703` `TriggerSummary` for the Bits branch becomes:

```csharp
TwitchTriggerType.Bits => (AmountScaledDurationEnabled || AddBitsToSwapTime)
    ? TF("Bits >= {0} ({1}s per {2} bits)", Math.Max(1, MinimumAmount), Math.Max(1, BitsSecondsPerAmountUnit), Math.Max(1, BitsAmountUnitsPerDuration))
    : TF("Bits >= {0}", Math.Max(1, MinimumAmount)),
```

Same pattern for the Subs branch.

### Localization

`Resources/Localization/en-US.json` and 14 non-English `.json` files gain a new key:

- `Add bits time to swap`: `Add bits time to swap` (en-US)

The 14 non-English locale files get natural translations following the AGENTS.md translation rules (informal register, brand/technical terms in English, no placeholders to preserve in this key).

A second new key for the row summary chip text (also goes in all 14 locale files):

- `+swap time`: `+swap time` (en-US)

The localization audit runs as part of the build script and will catch any missing translations or empty values.

### Backwards compatibility and migration

Existing saves have no `addBitsToSwapTime` field in JSON. The deserializer on `PersistedTriggerRule` will default the field to `false` when absent. The V5->V6 migration is a no-op for the data (it just bumps the version and sets the new field to `false` for every rule). The migration is added for clarity and to give the codebase a single point of truth for "this is V6+ data".

The behavior for users who do not have `addBitsToSwapTime` set in their existing rules: identical to today's behavior. The new field is opt-in. A user who wants the new behavior flips the toggle in the per-avatar editor and saves.

### Tests

**Extend `TestTriggerRuleSnapshotBuilder.Build`** with a new optional parameter `bool addBitsToSwapTime = false` that goes into the `with` block.

**Extend `SupportOverrideDurationMathTests.cs`** with 5 new test methods:

1. `ComputePerEventAddSeconds_AddBitsToSwapTimeOff_ReturnsBaseDuration` - `AddBitsToSwapTime = false`, `AmountScaledDurationEnabled = false`, `DurationSeconds = 30`, expect `30`.
2. `ComputePerEventAddSeconds_AddBitsToSwapTimeOn_ReturnsBasePlusScaled` - `AddBitsToSwapTime = true`, `AmountScaledDurationEnabled = false`, `DurationSeconds = 30`, `BitsAmountUnitsPerDuration = 50`, `BitsSecondsPerAmountUnit = 1`, `Amount = 100`, expect `32`.
3. `ComputePerEventAddSeconds_AddBitsToSwapTimeOn_SubsT1` - `AddBitsToSwapTime = true`, `AmountScaledDurationEnabled = false`, `DurationSeconds = 60`, `SubscriptionTier1SecondsPerSub = 30`, `Amount = 1`, `SubscriptionTier = "1000"`, expect `90`.
4. `ComputePerEventAddSeconds_BothTogglesOff_ReturnsBaseDuration` - both false, expect base.
5. `ComputePerEventAddSeconds_BothTogglesOn_StillScaled` - both true, expect `30 + 100/50*1 = 32` (no double-add).

**Extend `InlineRuleRowViewModelTests.cs`** with 2 new test methods in the existing `InlineBitsRuleRowViewModelTests` and `InlineSubsRuleRowViewModelTests` classes:

6. `Summary_IncludesAddBitsToSwapTime_WhenEnabled` - `AddBitsToSwapTime = true`, expect the new chip in `vm.Summary`.
7. `Summary_OmitsAddBitsToSwapTime_WhenDisabled` - `AddBitsToSwapTime = false`, expect the chip absent.

**New `VrcTwitchOscBridge.Tests/AvatarSwapMigrationServiceV6Tests.cs`** (or extend existing V5 tests) with one test:

8. `MigrateV5ToV6_DefaultsAddBitsToSwapTimeToFalse` - construct a V5 persisted profile with no `addBitsToSwapTime` field, run migration, assert the field is `false` on every rule.

The existing `AvatarSwapRuntimeDispatchTests.cs::FromSettings_FlattensAvatarSwapBitsRuleIntoMatchableRules` test should still pass because the new field defaults to `false`. No change needed.

## Files touched

Code:
- `VrcTwitchOscBridge/Models/TriggerRule.cs` - new field, property, computed property, `TriggerSummary` update.
- `VrcTwitchOscBridge/Services/BridgeRuntimeConfiguration.cs` - new snapshot field; `TryToSnapshot` mapping.
- `VrcTwitchOscBridge/Services/Support/SupportOverrideDurationMath.cs` - new `UsesScaledMath` helper, branch the gate.
- `VrcTwitchOscBridge/Services/SettingsStore.cs` - new `PersistedTriggerRule` field; `ToPersistedRule` / `ToRule` mapping.
- `VrcTwitchOscBridge/Services/AvatarSwapMigrationService.cs` - bump `CurrentMigrationVersion` to 6; add `MigrateV5ToV6`.
- `VrcTwitchOscBridge/UserControls/InlineRuleEditorControl.xaml` - new checkbox in Bits and Subs sections.
- `VrcTwitchOscBridge/UserControls/InlineBitsRuleRowViewModel.cs` - new chip in summary.
- `VrcTwitchOscBridge/UserControls/InlineSubsRuleRowViewModel.cs` - new chip in summary.
- `VrcTwitchOscBridge/Resources/Localization/en-US.json` - 2 new English keys.
- 14 non-English `*.json` files - matching translations.

Tests:
- `VrcTwitchOscBridge.Tests/TestTriggerRuleSnapshotBuilder.cs` - add `addBitsToSwapTime` parameter.
- `VrcTwitchOscBridge.Tests/SupportOverrideDurationMathTests.cs` - 5 new test methods.
- `VrcTwitchOscBridge.Tests/InlineRuleRowViewModelTests.cs` - 2 new test methods.
- `VrcTwitchOscBridge.Tests/AvatarSwapMigrationServiceV6Tests.cs` - 1 new test method (or extend V5).

Docs / changelog / release record:
- `CHANGELOG.txt` - 1 new bullet in `v3.1.9 beta 4` section.
- `RELEASE-CHANGE-RECORD.txt` - 1 new bullet in `v3.1.9 beta 4 (in progress)` section.
- `AGENTS.md` - no change (housekeeping is already correct from the previous task).
- `README.md` - no change.
- `Void Crystal Website` - no change.

No code change is expected to:
- The avatar-change dispatch (target/return avatar resolution).
- The Twitch reward sync code.
- The OSCQuery library.
- The updater, release scripts, beta scripts, or test scripts.
- The public GitHub repo (no push during this dev cycle).

## Risk and rollback

- The math change is isolated to one helper function. Reverting the new `UsesScaledMath` helper to the prior `rule.AmountScaledDurationEnabled` check restores the prior behavior.
- The new field defaults to `false`, so existing users see no change unless they opt in.
- The migration is a no-op data migration; rolling back the migration version does not corrupt existing saves.
- If a regression slips through, reverting the field, the helper change, and the UI additions restores the prior behavior. No data migration is needed because the data format is unchanged.
- The localization audit runs on every release/beta/test build and will catch any missing or empty translation before packaging.

## Out of scope / not in this change

- A separate "Replace" mode (the old "scale" behavior where bits replace the active time rather than adding to it). Streamers who want the old "replace" behavior can keep `AddBitsToSwapTime = false` and use the global `AmountScaledDurationEnabled` toggle if it applies.
- A per-rule option for the global override context. The global override's `AmountScaledDurationEnabled` is unchanged.
- Refactoring the cap logic, the queue path, or the per-action reset scheduling.
- Renaming `AmountScaledDurationEnabled` to anything else.
- Updates to the legacy `AvatarSwapRuleEditorControl` XAML that the main window's Redeem Editor used before the v3.1.9 beta 4 rework. That editor was replaced by the inline editor and is no longer wired into the UI.
