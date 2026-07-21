# Sub Trigger Accumulation & Threshold System

**Date:** 2026-07-18
**Product:** Crystal Relay
**Feature:** Avatar Swap / Avatar Roulette — Subscription Trigger Accumulation and Threshold System

## Overview

Add a dedicated sub-trigger system for avatar swap and avatar roulette rules. Replace the generic `MinimumAmount` behavior for subscriptions with a new sub-specific threshold and optional accumulation system, with a cleaned-up editor UI.

## Data Model (`TriggerRule.cs`)

### New Fields

| Field | Type | Default | Description |
|-------|------|---------|-------------|
| `SubsTriggerCount` | `int` | `1` | Number of subs required to trigger the action. Used in both accumulation and non-accumulation modes. |
| `SubsAccumulationEnabled` | `bool` | `false` | When enabled, sub counts accumulate across multiple events until the trigger count is met. |
| `SubsCarryOverEnabled` | `bool` | `false` | When accumulation is enabled and the threshold is exceeded, carry the excess forward to the next cycle. Only meaningful when `SubsAccumulationEnabled` is true. |

### New Computed Properties

- `UsesSubsTriggerSettings` (`bool`) — true when `TriggerType is TwitchTriggerType.Subscriptions`. Controls visibility of the new sub-specific section in the editor.
- `SubsTriggerSummary` (`string`) — human-readable summary of current sub trigger settings for the inline row display and SupporterTimeSettingsSummary.

### Serialization

All three fields are serialized in the existing JSON model (they are standard auto-properties or `[JsonPropertyName]`-annotated properties). Minimal impact.

## Runtime Accumulator (`BridgeCoordinator.cs`)

### State

```csharp
private readonly Dictionary<Guid, int> _subsAccumulator = new();
```

Keyed by rule ID. Persisted in memory only (no disk serialization — lost on restart, which is fine for an accumulating counter).

### Rule Selection — Two Parallel Paths

Sub rules are evaluated in two independent paths each event:

**1. Non-Accumulation rules** (`SubsAccumulationEnabled = false`):
- Use the existing `SelectBestThresholdMatch` logic, comparing against `SubsTriggerCount`
- Only the single best-matching rule fires per event (same as current behavior)
- If `event.Amount >= SubsTriggerCount` → trigger immediately

**2. Accumulation rules** (`SubsAccumulationEnabled = true`):
- **All** accumulation-enabled rules receive the event count independently, not just the best match
- For each accumulation rule:
  1. Read current accumulator value (default 0).
  2. Add `event.Amount` to the rule's accumulator.
  3. If `accumulator >= SubsTriggerCount`:
     - Trigger the action for that rule.
     - If `SubsCarryOverEnabled`: `accumulator -= SubsTriggerCount`
     - If not `SubsCarryOverEnabled`: `accumulator = 0`
- The same event can trigger multiple accumulation rules if all their thresholds are met.

### Overflow Protection

Cap each accumulator at `SubsTriggerCount * 10` to prevent unbounded memory growth if viewers rarely reach the threshold. When the cap is hit, the oldest subs effectively expire (no further counting).

## Snapshots (`BridgeRuntimeConfiguration.cs`)

### TriggerRuleSnapshot

Add the three new fields. Update the `FromRule(TriggerRule)` factory to populate them.

### Accumulator Snapshot (optional)

Expose a read-only snapshot of the current accumulator values for diagnostics / status display. Not critical for MVP.

## UI Layout — Sub Edit Section

### Current Layout (what changes)

The current "Minimum Amount" section (line 958-987 in `AvatarSwapRuleEditorControl.xaml`) is visible for `UsesAmountThreshold` (both Bits and Subscriptions). We need to:

1. For Subscriptions: **hide** the generic "Minimum Amount" section.
2. Show a new **"Subscription Trigger Settings"** section instead.
3. The new section uses `UsesSubsTriggerSettings` for visibility.

### New "Subscription Trigger Settings" Section

```
┌──────────────────────────────────────────────────────┐
│  Subscription Trigger Settings                        │
│                                                      │
│  Subs to Trigger:  [5]                               │
│  How many subs are needed before this rule fires.    │
│                                                      │
│  ☐ Accumulate subs across events                     │
│  Subs, resubs, and gift subs add to a running count  │
│  until the trigger is reached.                       │
│                                                      │
│  ☐ Carry over excess subs                            │
│  Extra subs past the threshold carry to next round.  │
└──────────────────────────────────────────────────────┘
```

### Bits are Unchanged

The existing "Minimum Amount" section stays as-is for Bits triggers.

### Existing Sections That Remain

- "Bits/Subs Amount Timer" section — still visible for subs, unchanged
- "Bits/Subs Add" section — still visible for subs, unchanged
- Tier filter checkboxes — still in the "Configure Added Time..." dialog, unchanged
- Chat Command Fallback — still visible, unchanged
- Active Time, Cooldown, etc. — unchanged

## Summary Display (`InlineSubsRuleRowViewModel.cs`)

Update `RefreshSummary()` to include:
- Trigger count: `trigger: 5 subs`
- Accumulation badge: `accumulate ON` or `accumulate OFF`
- Carryover badge: `carryover ON` (only if accumulation is ON)

Example: `"⭐ My Rule — T1:5s T2:10s T3:15s, trigger: 5 subs, accumulate ON, carryover ON"`

## Test / Simulation

Update `SimulateTwitchSubscriptionAsync` to exercise both accumulation and non-accumulation paths, verifying that the accumulator correctly tracks across multiple simulated events.

## Files Changed

| File | Change |
|------|--------|
| `VrcTwitchOscBridge\Models\TriggerRule.cs` | Add 3 fields + computed properties |
| `VrcTwitchOscBridge\Services\BridgeCoordinator.cs` | Add accumulator, modify `SelectSubscriptionMatchingRules` |
| `VrcTwitchOscBridge\Services\BridgeRuntimeConfiguration.cs` | Update `TriggerRuleSnapshot` + `FromRule` |
| `VrcTwitchOscBridge\UserControls\AvatarSwapRuleEditorControl.xaml` | Replace generic Minimum Amount section with sub-specific section |
| `VrcTwitchOscBridge\UserControls\InlineSubsRuleRowViewModel.cs` | Update summary display |
| Localization files | Add new UI text keys |

## Not In Scope

- Universal Triggers (this is for the avatar swap/roulette sub triggers only)
- Bits threshold behavior (unchanged)
- Saved accumulator state between app restarts
- Avatar Scaling sub triggers (separate system)
