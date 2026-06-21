# Float Reward Action Modes — Design Spec

**Date:** 2026-06-21
**Status:** Approved (pending written review)
**Target version:** Next post-`v3.1.9` development build (`v3.1.10`)

## Context

Crystal Relay's Twitch channel-point rewards already support sending a single
float value to a single VRChat avatar parameter via `TriggerRule` (the
canonical rule model in `VrcTwitchOscBridge/Models/TriggerRule.cs`). Today
the float path supports a smoothed, timed send with a configurable restore
value, plus the `FloatValueMode` (Decimal/Percent) input toggle, the
`FloatTransitionSeconds` ramp, and the existing `ActiveFloatBoost` /
`SupporterFloatAdd` sub-systems (see `TriggerRule.cs:117-132`).

What is **not** supported on plain float rewards today is anything beyond
"send a fixed value." The Int path already has a `IntZeroDurationMode` enum
with `Fixed / Random / Cycle` (see `TriggerRule.cs:93, 717, 9992-10005`),
but the float path has no equivalent. Streamers have asked for the obvious
gaps: random floats, add/subtract/multiply against the current avatar value,
toggle, cycle, glitchy oscillation, and pulse.

## Goal

Extend `TriggerRule` (the one rule model that drives every Twitch channel-
point, bits, sub, follow, chat-command, universal, power-up, and cash-payment
redeem) so that **float-typed avatar-parameter rules** can pick one of ten
action modes. The new modes share a single, consistent UI and one consistent
restore-on-expire semantic, do not regress any existing behavior, and
serialize cleanly to `crystal-relay.rules.json`.

## Non-goals

- **No Int modes added.** Int keeps its existing `Fixed / Random / Cycle`.
  Adding Add/Subtract/Multiply to Int is a separate task; this spec is
  float-only.
- **No SetTrigger changes.** `Models/SetTriggerAction.cs` is untouched; it
  keeps its simple `Set Bool/Int/Float value` behavior.
- **No polymorphic action refactor.** The `ParameterValue` / `ResetValue` /
  `ParameterType` shape stays. The new modes plug in as a `FloatActionMode`
  enum + per-mode fields, mirroring how `IntZeroDurationMode` already works.
- **No avatar-state snapshotting for restore.** Restore keeps the existing
  `ResetValue`-based semantic for every mode. Snapshotting the pre-trigger
  value is a separate future feature.

## Data model

### New enum: `FloatActionMode`

New file: `VrcTwitchOscBridge/Models/FloatActionMode.cs`

```
Set         = 0  (default — current behavior)
Random      = 1
Add         = 2
Subtract    = 3
AddSubtract = 4   (single signed ±value field)
Multiply    = 5
Toggle      = 6
Cycle       = 7
Glitchy     = 8
Pulse       = 9
```

### New enum: `FloatClampMode`

New file: `VrcTwitchOscBridge/Models/FloatClampMode.cs`

```
None     = 0
ZeroToOne = 1  (default — clamp result to [0, 1])
MinToMax = 2   (clamp result to [FloatRangeMin, FloatRangeMax])
```

### New fields on `TriggerRule` (`Models/TriggerRule.cs`)

| Field | Type | Default | Used by |
|---|---|---|---|
| `FloatActionMode` | `FloatActionMode` | `Set` | all modes |
| `FloatRangeMin` | `double` | `0.0` | Random, Cycle, Glitchy |
| `FloatRangeMax` | `double` | `1.0` | Random, Cycle, Glitchy |
| `FloatCycleStep` | `double` | `0.1` | Cycle |
| `FloatAddAmount` | `double` | `0.1` | Add |
| `FloatSubtractAmount` | `double` | `0.1` | Subtract |
| `FloatAddSubtractAmount` | `double` | `0.1` | AddSubtract (signed) |
| `FloatMultiplyFactor` | `double` | `1.5` | Multiply |
| `FloatToggleOnValue` | `double` | `1.0` | Toggle |
| `FloatToggleOffValue` | `double` | `0.0` | Toggle |
| `FloatGlitchyIntervalMs` | `int` | `200` | Glitchy |
| `FloatPulseSeconds` | `double` | `0.5` | Pulse |
| `FloatClampMode` | `FloatClampMode` | `ZeroToOne` | Add, Subtract, AddSubtract, Multiply |

All new fields are placed near the existing float-related fields at
`TriggerRule.cs:89-106`, in the same style (backing field + public property
+ setter that clamps/validates + `OnPropertyChanged`).

The existing `ParameterValue` (line 94), `ResetValue` (line 99),
`FloatValueMode` (line 95), `FloatTransitionSeconds` (line 96), and
`DurationSeconds` (line 106) keep their current roles.

### New computed `UsesXxx` properties

Added near the existing `UsesFloatTimedValues` / `UsesFloatTransition`
properties (`TriggerRule.cs:1511-1522`):

- `UsesFloatSetMode` / `UsesFloatRandomMode` / `UsesFloatAddMode` /
  `UsesFloatSubtractMode` / `UsesFloatAddSubtractMode` /
  `UsesFloatMultiplyMode` / `UsesFloatToggleMode` / `UsesFloatCycleMode` /
  `UsesFloatGlitchyMode` / `UsesFloatPulseMode` (one per mode)
- `UsesFloatRangeInputs` (true for Random, Cycle, Glitchy)
- `UsesFloatCycleStep` (true for Cycle)
- `UsesFloatToggleValues` (true for Toggle)
- `UsesFloatGlitchyInterval` (true for Glitchy)
- `UsesFloatPulseSeconds` (true for Pulse)
- `UsesFloatClampMode` (true for Add, Subtract, AddSubtract, Multiply)
- `UsesFloatActionMode` (master toggle: true when `ParameterType == Float`)

## UI

### Both editors get a new "Float Action Mode" section

- **Compact editor:** `AvatarSetsManagerWindow.xaml:1177-1410`
  (the one shown in the user's screenshot)
- **Full editor:** `UserControls/AvatarSwapRuleEditorControl.xaml:1143+`

The section is wrapped in a `Border` / `StackPanel` whose visibility binds to
`UsesFloatActionMode`. It contains:

1. **Mode button row** — a row of `RadioButton`-styled buttons bound to
   `FloatActionMode` (mirroring the existing "Parameter Type" button row at
   `AvatarSetsManagerWindow.xaml:1222-1284`). Buttons:
   `Set | Random | Add | Subtract | ± | Multiply | Toggle | Cycle | Glitchy | Pulse`

2. **Mode-specific sub-sections**, each gated by its `UsesXxx`:
   - **Range inputs** — two text boxes for Min / Max (use
     `FloatValueMode` for display), bounded 0..1 in Decimal or 0..100 in
     Percent.
   - **Cycle step** — single text box.
   - **Add amount** — single text box.
   - **Subtract amount** — single text box.
   - **± value** — single text box (allows negative).
   - **Multiply factor** — single text box, default `1.5`.
   - **Toggle on/off values** — two text boxes (`On` and `Off`).
   - **Glitchy interval** — single text box, default `200` (ms).
   - **Pulse seconds** — single text box, default `0.5`.
   - **Clamp mode** — `ComboBox` with `No clamp`, `Clamp 0..1`,
     `Clamp to range`. Visible for Add / Subtract / AddSubtract / Multiply.

Numeric inputs respect the existing `FloatValueMode` (Decimal/Percent)
just like the existing `ParameterValue` field. They feed through
`FloatValueModeConverter` (`Services/FloatValueModeConverter.cs:8, 26, 34`)
so the underlying stored value is always a normalized 0..1 double.

The existing "Parameter Value" True/False buttons (Bool case) and the
"OSC value" free-text field (current `ParameterValue`) keep working for Set
mode. The "OSC value" field remains the single-value input for Set, Pulse
(value to pulse), and Toggle (when the user wants to type a single value
before picking mode). For all other modes, the mode-specific fields
replace the role of `ParameterValue`.

## Dispatch / runtime behavior

### Entry point

`Services/BridgeCoordinator.cs:9945` — `ResolveAvatarParameterActionAsync`
gains a top-level branch:

```csharp
if (rule.ParameterType == OscParameterType.Float)
    return await ResolveFloatActionAsync(rule, ...);
// existing bool / int / string dispatch unchanged
```

### New helper: `ResolveFloatActionAsync`

Computes the target value and the reset value, returns the same
`ResolvedRuleAction` shape the existing resolver does (target packets +
optional reset packets).

Per-mode logic:

- **Set** — `targetValue = ParseFloat(rule.ParameterValue)`;
  `resetValue = ParseFloat(rule.ResetValue)`; both clamped 0..1.
  Existing timed-float session path applies
  (`ExecuteTimedFloatAvatarParameterRuleActionAsync` at line 7691).
- **Random** — `targetValue = Random.Shared.NextDouble() * (max - min) + min`;
  `resetValue = ParseFloat(rule.ResetValue)`. Existing timed-float
  session path applies.
- **Add** — read current value via existing
  `TryGetCurrentAvatarFloatValueAsync` (line 8073). If unknown, fall back
  to `ParseFloat(rule.ParameterValue)` and log a one-line warning.
  `targetValue = current + FloatAddAmount`. Apply `FloatClampMode`.
  Reset is the existing `ResetValue`.
- **Subtract** — same as Add but `targetValue = current - FloatSubtractAmount`.
- **AddSubtract** — same as Add but uses signed `FloatAddSubtractAmount`.
- **Multiply** — read current; `targetValue = current * FloatMultiplyFactor`.
  Apply `FloatClampMode`.
- **Toggle** — read current. If
  `Math.Abs(current - FloatToggleOnValue) < 0.0001` then
  `targetValue = FloatToggleOffValue` else `targetValue = FloatToggleOnValue`.
  If current is unknown, default to `FloatToggleOnValue`. Reset is
  `ResetValue`.
- **Cycle** — read current; if unknown, treat as `FloatRangeMin`.
  `next = current + FloatCycleStep`. If `next > FloatRangeMax`,
  `next = FloatRangeMin + (next - FloatRangeMax - epsilon)` (wrap).
  Reset is `ResetValue`. Existing timed-float session applies for the
  active time, then restores to `ResetValue` via the existing
  `ScheduleActiveFloatRedeemCompletion` (line 7833).
- **Glitchy** — new timed session (see below) that re-rolls
  `Random.Shared.NextDouble() * (max - min) + min` every
  `FloatGlitchyIntervalMs` for the active time, then restores to
  `ResetValue`.
- **Pulse** — `targetValue = ParseFloat(rule.ParameterValue)`. Build the
  target packet immediately, then schedule a single restore packet
  after `FloatPulseSeconds` (independent of `DurationSeconds`). If
  `ResetValue` is empty, no restore. `DurationSeconds` and the
  `CooldownSeconds` are still honored: the Twitch cooldown still applies,
  but Active Time is not used for the pulse's own timing.

### New session type for Glitchy

`ActiveFloatGlitchyRedeemSessionState` (new private class in
`BridgeCoordinator`):

- Stores: rule, address, `min`, `max`, `intervalMs`, `activeUntil`,
  `resetValue`, `completionCancellation`, `laneKeys`, `leaseId`, `isTest`.
- Spawns a `Task` that loops every `intervalMs` until `activeUntil` is
  reached, sending a new random value each iteration.
- On completion (active time elapses), sends the restore value via
  `SendFloatAvatarParameterValueAsync` (line 8112) with
  `transitionSeconds` from the rule.
- Replaces any prior glitchy session for the rule (mirror the existing
  active-float session replacement at line 7737-7744).
- Cancellation token cancels the loop immediately on avatar change, manual
  stop, or new trigger.

`SendSingleFloatAvatarParameterValueAsync` (line 8150) is reused as-is
for each re-roll. The glitchy session never re-uses
`ScheduleActiveFloatRedeemCompletion`; it has its own loop.

### Reset / Active Time / Cooldown interactions

- **All modes except Pulse:** `DurationSeconds` (Active Time) controls how
  long the rule "owns" the float, and `ResetValue` is sent at the end
  (existing `ScheduleActiveFloatRedeemCompletion` path for Set/Random/Add/
  Subtract/AddSubtract/Multiply/Toggle/Cycle; new glitchy session for
  Glitchy). `FloatTransitionSeconds` still smooths the start ramp and
  end ramp.
- **Pulse:** `DurationSeconds` is ignored. `FloatPulseSeconds` is the
  timing. `ResetValue` controls the restore value (no restore if empty).
  `FloatTransitionSeconds` is ignored (instant pulse).
- **All modes:** `CooldownSeconds` (Twitch Cooldown) still applies, via
  the existing cooldown machinery at `BridgeCoordinator.cs:7353-7374`.
  Paired rules still apply via the existing pairing machinery
  (`GetTemporarilyDisabledRuleIds` at line 344, `SpecialRulePairingMode`
  on `TriggerRule`).

### Fallback when current value cannot be read

`TryGetCurrentAvatarFloatValueAsync` (line 8073) tries the in-memory
observed cache, then a forced OSCQuery read, then falls back to a caller-
provided default. For relative modes, the default is
`ParseFloat(rule.ParameterValue)`. For Toggle, the default is
`FloatToggleOnValue`. For Cycle, the default is `FloatRangeMin`. A one-
line diagnostic is logged with the rule title and the chosen fallback.

## Persistence

### `PersistedTriggerRule` (`Services/SettingsStore.cs:3196`)

Add matching fields for every new `TriggerRule` field above. Update
`ToPersistedRule` (`SettingsStore.cs:1006`) to copy them across, and
`ToRule` (`SettingsStore.cs:1231`) to copy them back. The
`CashPaymentRuleJsonConverter` (`Services/CashPaymentRuleJsonConverter.cs:23`)
needs no change because the new fields are all plain value types.

### Migration

`ToRule` already defaults missing fields to safe values (see line 1233 et
seq. for the migration patterns). Old rules with no `FloatActionMode`
load as `Set`, and all other new fields default to their listed defaults.
No upgrade wizard, no version bump on the rules file, no breaking change
to existing user setups.

## Backward compatibility

- A rule with `ParameterType = Float` and no `FloatActionMode` field in
  the JSON loads as `FloatActionMode.Set` — the existing behavior is
  preserved bit-for-bit.
- Bool / Int / String rules are not touched.
- SetTrigger actions are not touched.
- The serialized JSON shape grows (new optional fields), which is
  forward-compatible with older Crystal Relay versions in the sense that
  older versions that don't know the new fields will fail to load only if
  the new fields are non-default. Defaulting to safe values in the loader
  avoids that.
- The compact and full editors' existing bindings continue to work for
  Bool / Int / String rules; the new section only renders when
  `UsesFloatActionMode` is true.

## Edge cases and failure modes

- **Glitchy during avatar change:** the existing
  `ScheduleActiveFloatRedeemCompletionAfterGracePeriod` (line 7852)
  pattern is mirrored for glitchy. Avatar change during glitchy defers
  the session end until after the grace period.
- **Multiple glitchy triggers before the active time ends:** the existing
  replacement logic at line 7737-7744 is mirrored; the new session
  cancels the old one's `CompletionCancellation` and re-uses the
  same lane key / lease.
- **Pulse during avatar change:** the pulse restore is deferred via the
  same grace-period pattern.
- **Toggle when current value is exactly between On/Off:** the 0.0001
  tolerance resolves this; if it doesn't, default to
  `FloatToggleOnValue` and log a diagnostic.
- **Range with Min >= Max:** the setter for `FloatRangeMax` clamps it
  to `>= FloatRangeMin + 0.0001` (mirrors the existing
  `RangeMaximum` setter at `TriggerRule.cs:911`).
- **Cycle step larger than range:** behavior is well-defined (wraps once,
  lands on `Min`); no special case needed.
- **Multiply by zero:** clamps per `FloatClampMode`. If `None`, the
  float becomes 0. If `ZeroToOne`, stays 0. If `MinToMax`, lands on min.

## Testing approach

1. **Unit tests** in `VrcTwitchOscBridge.Tests`:
   - `FloatValueMode` setter round-trips Decimal/Percent for all new
     fields.
   - `FloatActionMode` setter on `TriggerRule` resets stale
     `ParameterValue` text appropriately when switching modes (mirror
     the existing `ParameterType` setter test pattern at line 680).
   - `FloatClampMode` enum serializes and deserializes.
   - `PersistedTriggerRule` round-trips a rule with all new fields set
     to non-default values.
   - `ToRule` migration: an old JSON blob missing the new fields loads
     with all defaults.

2. **Manual smoke** in the running debug build (`Launch-Crystal-Relay-
   Debug.bat`):
   - Create one float rule per mode, point each at a real float param,
     fire each, confirm the avatar responds and the restore value
     arrives.
   - Confirm Glitchy re-rolls visibly and stops on time.
   - Confirm Pulse fires and restores without waiting for Active Time.
   - Confirm existing rules (Set mode) still behave exactly as before.

3. **Localization audit** after UI text is added (the existing
   `LocalizationAudit` project must pass on the new en-US keys).

## File touch list (rough)

- `VrcTwitchOscBridge/Models/FloatActionMode.cs` (new)
- `VrcTwitchOscBridge/Models/FloatClampMode.cs` (new)
- `VrcTwitchOscBridge/Models/TriggerRule.cs` (new fields, setters, UsesXxx)
- `VrcTwitchOscBridge/Services/BridgeCoordinator.cs`
  (new `ResolveFloatActionAsync`, glitchy session, pulse schedule)
- `VrcTwitchOscBridge/Services/SettingsStore.cs`
  (`PersistedTriggerRule` fields, `ToPersistedRule`, `ToRule` migration)
- `VrcTwitchOscBridge/AvatarSetsManagerWindow.xaml` and `.xaml.cs`
  (new mode row + conditional fields)
- `VrcTwitchOscBridge/UserControls/AvatarSwapRuleEditorControl.xaml` and
  `.xaml.cs` (same)
- `VrcTwitchOscBridge/Resources/Localization/*.json` (new en-US keys for
  mode labels and field labels; translations for all non-English locales)
- `VrcTwitchOscBridge.Tests/` (unit tests for the new enums, field
  round-trips, and migration)

## Housekeeping at build time

- `AGENTS.md` `Project Identity` updates: this work is the next
  post-release development. Per the versioning rules, after a successful
  test build the active build lane stays `beta4` (or moves to whatever
  the user picks); the next build is the same `v3.1.9` test lane until
  the user asks for a new package.
- `CHANGELOG.txt` gets a new entry under the active lane summarizing the
  new float modes.
- `RELEASE-CHANGE-RECORD.txt` is updated under `Added` for each new
  mode.
- `Check-Crystal-Relay-Dependencies.ps1` is rerun before release prep.
