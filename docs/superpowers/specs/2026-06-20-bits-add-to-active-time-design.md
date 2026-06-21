# Bits + Subs: Always Add to Active Time

Date: 2026-06-20
Lane: in-progress 3.1.9 beta 4
Status: design approved, pending user spec review

## Problem

Crystal Relay's Bits and Subs overrides have a "Scale active time by amount" toggle. When ON, the math today is:

- First trigger on a fresh sequence: `DurationSeconds + bitsSeconds` (base + scaled)
- Subsequent triggers on the same already-active rule: `bitsSeconds` only (no base re-added)

This means a streamer who sets `Active Time = 30s` and gets a single 100-bit cheer at `1s per 50 bits` sees the override run for `30 + 2 = 32s`. A second cheer that lands 10s later extends the running timer by `2s` (the bitsSeconds only), not `32s` again. The base Active Time only "counts" on the very first trigger.

The user wants every matching trigger to extend the running timer by `Active Time + bitsSeconds`, so the base time contributes on every event. The cap (`Max accumulated duration`) still applies as the ceiling on the total running time.

## Goal

When `AmountScaledDurationEnabled` is ON on a Bits or Subs override:

- Every matching trigger (Bits cheer, sub, gift sub) extends the current timer by `max(0, DurationSeconds) + scaledSeconds`.
- The cap behavior is unchanged: `existingRemainingDuration + thisAdd <= MaxAccumulatedDurationSeconds`.
- When the toggle is OFF, behavior is unchanged: `DurationSeconds` is the fixed duration.

## Non-goals

- No new persisted fields. `AmountScaledDurationEnabled` keeps its storage name; only the user-facing label changes.
- No change to the cap math, the queued-override path, the cooldown path, or the per-action reset scheduling.
- No new model fields, no new snapshot fields, no SettingsStore migration.
- No changes to non-Bits/non-Subs trigger types (Channel Points, Chat Command, Follow, PowerUp).
- No change to the Avatar Roulette, Avatar Change, Avatar Set, or Avatar Scaling action paths; they all consume `DurationSeconds` from the snapshot exactly as they do today.

## Current architecture (ground truth from code reading)

Files and line numbers come from a code review of the working tree.

### Model
- `Models/TriggerRule.cs:50` - `TriggerRule` class. Bits/Subs are not separate classes; they share `TriggerRule` and discriminate on `TwitchTriggerType`.
- `Models/TwitchTriggerType.cs:3-12` - `TwitchTriggerType` enum includes `Bits`, `Subscriptions`, `GiftSubscription`, `PowerUp`, `ChannelPoints`, `ChatCommand`, `Follow`.
- `Models/TriggerRule.cs:395` - `AmountScaledDurationEnabled` property (the master toggle).
- `Models/TriggerRule.cs:436` - `BitsAmountUnitsPerDuration` ("every X bits").
- `Models/TriggerRule.cs:450` - `BitsSecondsPerAmountUnit` ("= Y seconds").
- `Models/TriggerRule.cs:464-528` - Subs T1/T2/T3 seconds-per-sub fields.
- `Models/TriggerRule.cs:570` - `MaxAccumulatedDurationEnabled` (the cap).
- `Models/TriggerRule.cs:582` - `MaxAccumulatedDurationSeconds`.
- `Models/TriggerRule.cs:917` - `DurationSeconds` (the "Active Time" the user sees).
- `Models/TriggerRule.cs:1335-1371` - computed properties: `UsesAmountThreshold`, `UsesAmountScaledDuration`, `SupporterTimeSettingsSummary`, `UsesBitsOutfitSetTrigger`, `UsesForceMovementBitsTrigger`, `UsesSupporterAmountTimerSettings`.
- `Models/TriggerRule.cs:1619-1629` - `DurationHelpText`, the help string shown beneath the Active Time field in the Edit Trigger window. The `UsesAmountScaledDuration` branch currently says: "Amount-scaled timer is enabled, so Active Time is the starting time. Bits and subs add time on top when the override first starts; later same-rule triggers extend the current timer by the amount only."
- `Models/TriggerRule.cs:1638-1703` - `TriggerSummary`, the one-line text on the rule card.

### Snapshot
- `Services/BridgeRuntimeConfiguration.cs:58-143` - `TriggerRuleSnapshot` immutable record. The Bits/Subs duration fields are mirrored here (lines 80-91). `with` expressions are used everywhere to derive new snapshots; live mutation of the underlying `TriggerRule` is not part of the normal path.
- `Services/BridgeRuntimeConfiguration.cs:929-940` - `FromSettings` mapping that copies Bits/Subs fields from the model into the snapshot.

### Runtime math
- `Services/BridgeCoordinator.cs:6411-6427` - `GetSupporterOverrideDuration(rule, bridgeEvent, includeStartingDuration)`. The function to change.
- `Services/BridgeCoordinator.cs:6485-6508` - `GetSupporterOverrideAmountScaledDurationSeconds` and `GetSupporterOverrideSubscriptionSecondsPerSub`. Pure math; unchanged.
- `Services/BridgeCoordinator.cs:6521-6541` - `ClampSupporterOverrideAddedDuration`. Cap logic; unchanged.
- `Services/BridgeCoordinator.cs:8440-8558` - `HandleTimedSupporterOverrideTriggerAsync`. The dispatch path that calls `GetSupporterOverrideDuration` and decides between active/queued/new branches. It computes `includeStartingDuration = !hasSameRuleActive && !hasSameRuleQueued` and passes that in.
- `Services/BridgeCoordinator.cs:8560-8621` - `ExtendActiveSupporterOverrideAsync`. Extends the running timer's `ActiveUntil`.
- `Services/BridgeCoordinator.cs:8623-8651` - `ExtendQueuedSupporterOverride`. Extends the queued override's `RemainingDuration`.
- `Services/BridgeCoordinator.cs:8769-8880` - `StartTimedSupporterOverrideAsync`. Creates a new active state for a fresh sequence.
- `Services/BridgeCoordinator.cs:6849-6857` - `CreateTimedSupporterOverrideExecutionRule`. Writes the per-event duration into `DurationSeconds` of a fresh snapshot via `with`.

### UI
- `SupporterOverrideTimeSettingsWindow.xaml:220-221` - master toggle currently labeled `Scale active time by amount`.
- `SupporterOverrideTimeSettingsWindow.xaml:223` - live summary `Text="{Binding SupporterTimeSettingsSummary}"`.
- `SupporterOverrideTimeSettingsWindow.xaml:235-306` - Bits and Subs timer fields, plus the cap. These stay as-is; their meaning is the same.
- `UserControls/InlineRuleEditorControl.xaml:162-210` - the Bits section of the Edit Trigger window (the screenshot the user shared). It does not currently expose the master toggle; that lives in the modal above.
- `UserControls/InlineRuleEditorControl.xaml:261-278` - Common section with `Active Time (seconds)` and `Cooldown (seconds)`. Unchanged.
- `UserControls/InlineBitsRuleRowViewModel.cs:40-63` - row summary text. Unchanged.
- `Resources/Localization/en-US.json:488` - source English string for "Scale active time by amount".

### Tests
- `VrcTwitchOscBridge.Tests/InlineRuleRowViewModelTests.cs:51-112` - existing row summary tests. Unchanged.
- `VrcTwitchOscBridge.Tests/AvatarSwapRuntimeDispatchTests.cs` - existing dispatch tests. Unchanged.
- No existing test covers the bits-to-seconds math directly; both `GetSupporterOverrideAmountScaledDurationSeconds` and `GetSupporterOverrideDuration` are private static and untested.

## Design

### Math change (the only behavior change)

Replace the current `GetSupporterOverrideDuration` at `Services/BridgeCoordinator.cs:6411-6427` with:

```csharp
private static TimeSpan GetSupporterOverrideDuration(
    TriggerRuleSnapshot rule,
    BridgeIncomingEvent bridgeEvent)
{
    double seconds = Math.Max(1, rule.DurationSeconds);
    if (rule.AmountScaledDurationEnabled)
    {
        var scaled = GetSupporterOverrideAmountScaledDurationSeconds(rule, bridgeEvent);
        seconds = Math.Max(0, rule.DurationSeconds) + scaled;
    }
    return TimeSpan.FromSeconds(Math.Min(Math.Max(1, seconds), TimeSpan.MaxValue.TotalSeconds));
}
```

Changes from the current implementation:

1. The `includeStartingDuration` parameter is removed. The caller no longer needs to compute or pass it; every trigger that flows through this function now treats base + scaled as the add.
2. When the toggle is OFF, the function returns `max(1, DurationSeconds)` (fixed duration, same as today).
3. When the toggle is ON, the function returns `max(0, DurationSeconds) + scaled`. The `max(0, ...)` lets a user set `Active Time = 0` to mean "no base add, just scaled", which is a useful edge case for streamers who only want the bits-driven portion.
4. The final `Math.Min(Math.Max(1, ...), TimeSpan.MaxValue.TotalSeconds)` clamps to a positive value, matching the current safety net.

Call site update at `Services/BridgeCoordinator.cs:8440-8558` (`HandleTimedSupporterOverrideTriggerAsync`):

- Remove the `includeStartingDuration` local variable and the two helper locals that exist only to compute it: `hasSameRuleActive` (lines 8504-8506) and `hasSameRuleQueued` (line 8507). After this change, those two locals have no remaining reader in the function.
- The active/queued/new branch (lines 8520-8539) does not depend on the removed locals. It re-checks `activeState is not null && activeState.ActiveUntil > now && activeState.Rule.Id == rule.Id` and `queuedIndex >= 0` directly, so the dispatch wiring stays intact.
- Replace `GetSupporterOverrideDuration(rule, bridgeEvent, includeStartingDuration)` with `GetSupporterOverrideDuration(rule, bridgeEvent)`.
- Keep `existingRemainingDuration` (line 8501, used at line 8510) since the cap function still needs it.
- The rest of the function (active/queued/new branching, cap clamp, extend/start dispatch) is unchanged. The new math simply feeds a different `requestedDuration` into the same dispatch tree.

### Cap behavior (unchanged)

`ClampSupporterOverrideAddedDuration` at `Services/BridgeCoordinator.cs:6521-6541` is unchanged. It still receives a `requestedDuration` and `existingRemainingDuration` and clamps the requested add so `existingRemainingDuration + requestedAdd <= MaxAccumulatedDurationSeconds`. With the new math, `requestedDuration` is now `base + scaled` instead of just `scaled`, so the cap correctly limits the total running timer including the new base portion added by this trigger.

Worked example with cap = 1800s, base = 30s, scale = 1s per 50 bits, current remaining = 1750s, cheer = 200 bits:

- `scaled = 200 / 50 * 1 = 4s`
- `requested = 30 + 4 = 34s`
- `remainingCapacity = 1800 - 1750 = 50s`
- `requested (34) <= remainingCapacity (50)` -> add 34s -> new remaining = 1784s

If the same cheer lands when remaining = 1790s:

- `requested = 34s`
- `remainingCapacity = 10s`
- Clamped to 10s -> new remaining = 1800s (capped)

### UI wording (3 changes)

1. **Master toggle label** - `SupporterOverrideTimeSettingsWindow.xaml:220-221`
   - From: `Scale active time by amount`
   - To: `Add amount to active time`
   - Source `en-US.json` key updated; all 14 non-English locale files get a translation pass.

2. **Live summary caption** - `Models/TriggerRule.cs:1341-1365` (`SupporterTimeSettingsSummary`)
   - Format stays: `Start: {0}s | Bits: {1}s per {2} bits | Subs: T1 {0}s, T2 {1}s, T3 {2}s | Cap: {0}s max`
   - No text change; semantics shift is in the help text below.

3. **Edit Trigger help text** - `Models/TriggerRule.cs:1619-1629` (`DurationHelpText`, `UsesAmountScaledDuration` branch)
   - From: `Amount-scaled timer is enabled, so Active Time is the starting time. Bits and subs add time on top when the override first starts; later same-rule triggers extend the current timer by the amount only.`
   - To: `Active time and amount both add on top. Each matching trigger extends the current timer by Active Time + the scaled amount.`

The trigger card summary (`TriggerSummary` at `Models/TriggerRule.cs:1638-1703`, Bits branch) stays as `Bits >= {0} ({1}s per {2} bits)` - the input values are unchanged, only their meaning shifted. The row chip text in `InlineBitsRuleRowViewModel.cs:40-63` (`{seconds}s per {bits} bits`) also stays.

### Localization

Two English keys change in `Resources/Localization/en-US.json`:

- The master toggle label (key near line 488).
- The help text shown beneath the Active Time field in the Edit Trigger window.

The new English strings:

- `Add amount to active time`
- `Active time and amount both add on top. Each matching trigger extends the current timer by Active Time + the scaled amount.`

The 14 non-English locale files (`.json` and `.extra.json` pairs) need matching translations. Per AGENTS.md, translations must be natural and conversational, use informal register (`du` / `tú` / `tu`), keep brand/technical terms in English (Bits, Subs, OSC, Twitch, Crystal Relay, VRChat, OSCQuery), preserve placeholders, and use a single consistent term per recurring concept. The build script runs the localization audit before publishing, which will catch any missing or empty values.

### Backwards compatibility

No data migration. The persisted field name (`AmountScaledDurationEnabled`) and storage path in `Services/SettingsStore.cs` are unchanged. Existing user saves load with their value preserved exactly as they left it.

- Users who had the toggle ON before this update: their setups start using "add on top every trigger" semantics immediately. This is a behavior change inside the same feature, not a breaking change, and is documented in the changelog as a Changed bullet.
- Users who had the toggle OFF: no change. Fixed `DurationSeconds` still means fixed `DurationSeconds`.

The `internal` field name stays `AmountScaledDurationEnabled` in code and storage. Only the user-facing XAML label and help text change. This keeps the diff small and avoids touching the persistence layer or snapshot mapping.

### Tests

Extract `GetSupporterOverrideAmountScaledDurationSeconds` and a new `ComputeSupporterOverridePerEventAddSeconds` helper (which performs the new `max(0, DurationSeconds) + scaled` math) into a new small static class:

- New file: `Services/Support/SupportOverrideDurationMath.cs`
- Exposes one static method: `public static double ComputePerEventAddSeconds(TriggerRuleSnapshot rule, BridgeIncomingEvent bridgeEvent)`.
- `BridgeCoordinator.GetSupporterOverrideDuration` calls it and wraps the result in a `TimeSpan`.
- The helper is the single point of truth for the per-trigger add math, which is what we want to test.

New test file: `VrcTwitchOscBridge.Tests/SupportOverrideDurationMathTests.cs`. Uses xUnit + FluentAssertions in the same style as `InlineRuleRowViewModelTests.cs`. Cases:

1. `AmountScaledDurationEnabled = false`, `DurationSeconds = 30` -> returns `30` regardless of `bridgeEvent.Amount`.
2. `AmountScaledDurationEnabled = true`, Bits, `DurationSeconds = 0`, `BitsAmountUnitsPerDuration = 50`, `BitsSecondsPerAmountUnit = 1`, `Amount = 100` -> returns `2`.
3. `AmountScaledDurationEnabled = true`, Bits, `DurationSeconds = 30`, `BitsAmountUnitsPerDuration = 50`, `BitsSecondsPerAmountUnit = 1`, `Amount = 100` -> returns `32`.
4. `AmountScaledDurationEnabled = true`, Bits, `DurationSeconds = 30`, `BitsAmountUnitsPerDuration = 25`, `BitsSecondsPerAmountUnit = 2`, `Amount = 50` -> returns `34`.
5. `AmountScaledDurationEnabled = true`, Subs, T1, `DurationSeconds = 60`, `SubscriptionTier1SecondsPerSub = 30`, `Amount = 1` -> returns `90`.
6. `AmountScaledDurationEnabled = true`, Gift subs, T1, `DurationSeconds = 60`, `SubscriptionTier1SecondsPerSub = 30`, `Amount = 5` -> returns `210`.
7. `AmountScaledDurationEnabled = true`, `DurationSeconds = 0` -> returns just the scaled portion (base is 0, math still works; the new behavior intentionally allows this).
8. Legacy safety: `BitsAmountUnitsPerDuration = 0` (shouldn't happen, but the runtime uses `Math.Max(1, ...)` as a guard) -> math falls back to `base + amount * secondsPerUnit` and the function still returns a positive value.

A small `TestTriggerRuleBuilder` helper inside the test file is acceptable if it keeps the test bodies short, mirroring how `InlineRuleRowViewModelTests.cs` constructs `TriggerRule` instances directly.

The existing `InlineRuleRowViewModelTests.cs:51-112` and `AvatarSwapRuntimeDispatchTests.cs` stay as-is. No existing test breaks because the row text and dispatch wiring are unchanged.

## Files touched

Code:
- `Models/TriggerRule.cs` - help text update in `DurationHelpText` (1 line).
- `Services/BridgeCoordinator.cs` - `GetSupporterOverrideDuration` body change, call site at `HandleTimedSupporterOverrideTriggerAsync:8440-8558` adjusted to drop the `includeStartingDuration` parameter.
- `Services/Support/SupportOverrideDurationMath.cs` - new file, single static method.
- `SupporterOverrideTimeSettingsWindow.xaml` - master toggle label change (1 line).
- `Resources/Localization/en-US.json` - 2 English keys updated.
- 14 non-English locale JSON files (and their `.extra.json` siblings) - matching translations added.

Tests:
- `VrcTwitchOscBridge.Tests/SupportOverrideDurationMathTests.cs` - new file, 8 test cases.

Docs / changelog / release record:
- `CHANGELOG.txt` - 2 bullets added to the existing `v3.1.9 beta 4` section.
- `RELEASE-CHANGE-RECORD.txt` - new `v3.1.9 beta 4 (in progress)` section; baseline and pending draft headers updated.
- `AGENTS.md` - "Project Identity" block housekeeping (last stable, current source, active build, active lane) to match the actual state of the working tree.

No code change is expected to:
- The Avatar Change / Avatar Roulette / Avatar Set / Avatar Scaling action paths.
- The Twitch reward sync code.
- The SettingsStore save/load path.
- The OSCQuery library.
- The updater, release scripts, beta scripts, or test scripts.
- The public GitHub repo (no push during this dev cycle).

## Risk and rollback

- The math change is isolated to one helper function. Reverting the change to the previous `includeStartingDuration` branching restores the prior behavior.
- The `AmountScaledDurationEnabled` storage field is preserved, so users who had it OFF keep their OFF behavior; only ON users see a behavior change.
- The cap, cooldown, queued-override, and per-action reset paths are untouched. The only thing that changes is the per-event add value.
- The localization audit runs on every release/beta/test build and will catch any missing or empty translation before packaging.
- If a regression slips through, the previous behavior is recoverable by reverting `GetSupporterOverrideDuration` to the prior branching version; no migration is needed because the data format is unchanged.

## Out of scope / not in this change

- A "first trigger uses base, subsequent add only" mode toggle (a per-rule option to pick between the old and new behavior). Streamers who want the old behavior can set `Active Time = 0` and rely on the scaled portion only, or set `AmountScaledDurationEnabled = false` for a fully fixed duration.
- Renaming the persisted `AmountScaledDurationEnabled` field. Kept as-is to avoid a SettingsStore migration.
- Adding a per-trigger max-add cap (capping the size of one add rather than the total running time). The cap already applies to the total running time, which is what the user asked for.
- Refactoring the cap logic, the queue path, or the per-action reset scheduling.
