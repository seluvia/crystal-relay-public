# Supporter Growth Editor Redesign

## Overview

Simplify the Avatar Scaling Supporter Growth editor by removing redundant fields that overlap with the global Avatar Scale Safety Settings, and reorganizing the remaining fields into collapsible card sections for easier management.

## Changes

### Removed Fields

1. **`SupporterGrowthNormalHeightMeters`** — Removed from the model, UI, snapshot, and runtime. Superseded by dynamic pre-growth height tracking: when a supporter growth session starts, the system records the avatar's current VRChat height as the baseline. All tier/bit heights add onto that baseline, and the avatar returns to it when the paid timer expires or inactivity timeout fires.

2. **`SupporterGrowthMaxAddedHeightMeters`** — Removed from the model, UI, snapshot, and runtime. Redundant with `AvatarScaleSafetySettings.ClampHeight()` which already caps the final computed height against global min/max limits.

### Dynamic Baseline Tracking (Runtime Only)

When `ExecuteSupporterGrowthAvatarScaleRuleAsync()` starts a new growth session:
- Read the current VRChat `/avatar/eyeheight` as the dynamic baseline
- Use that as the `NormalHeightMeters` equivalent for the session
- The `ActiveAvatarScaleSupporterGrowthState` already stores `NormalHeightMeters` and `AddedHeightMeters` — set `NormalHeightMeters` from the live VRChat height instead of from a model field
- On inactivity/paid timer expiry, return to this recorded baseline
- Existing `ApplyAvatarScaleHeightLimits()` still applies global safety clamping to final targets

### New Layout (Both XAML Surfaces)

The supporter growth editor is now a vertical stack of bordered cards, each with a clickable header that toggles expand/collapse. Each card shows a one-line summary of its current values when collapsed.

| Card | Contents | Collapsed Summary Source |
|------|----------|-------------------------|
| **General Settings** | `SupporterGrowthAllowRewardScaleOverlay` checkbox + helper text | Not collapsible (single field) |
| **Paid Active Time** | `SupporterGrowthBitsTimerUnit`, `SupporterGrowthSecondsPerBitsUnit`, `SupporterGrowthSoftCapSeconds`, `SupporterGrowthSoftCapMultiplierPercent`, `SupporterGrowthMaxPaidTimeSeconds` | `SupporterGrowthPaidTimeSummary` |
| **Cheer Keywords** | `SupporterGrowthGrowKeyword`, `SupporterGrowthShrinkKeyword`, `SupporterGrowthRequireCheerKeyword` checkbox + helper | `SupporterGrowthCheerKeywordsSummary` |
| **Subscription Tiers** | Per-tier mini-cards: each shows Tier 1/2/3 Height Add + Tier Seconds together, in a small bordered box | `SupporterGrowthSubTierSummary` |
| **Bits Growth Ranges** | "Add Bits Range" button, dynamic `ItemsControl` rows (Min Bits, Max Bits, Height Added, Remove), helper text | `SupporterGrowthBitsRangeCountSummary` |

### Subscription Tiers Layout Change

Current layout has two separate `UniformGrid` sections (one for all three height adds, one for all three tier seconds). The new layout groups height + time per tier into a mini-card:

```
[Tier 1]            [Tier 2]            [Tier 3]
 Height: [0.10]m     Height: [0.20]m     Height: [0.30]m
 Time:   [300]s      Time:   [600]s      Time:   [1500]s
```

### Bits Growth Ranges Layout Change

Current layout has unlabeled textboxes. New layout adds column headers (Min Bits, Max Bits, Height Added) above the rows for clarity.

## Files to Modify

### Data Model
- `Models\AvatarScaleRule.cs` — Remove `SupporterGrowthNormalHeightMeters` and `SupporterGrowthMaxAddedHeightMeters` fields, properties, clamping, summary properties, and bit-range wire-up references
- `Models\AvatarScaleSafetySettings.cs` — Remove `SupporterGrowthNormalHeightMeters` and `SupporterGrowthMaxAddedHeightMeters + NormalHeight` from `GetConfiguredHeightValues()`

### ViewModel
- `ViewModels\AvatarScalingSourceCardViewModel.cs` — Update `DescribeScaleAction()` if `SupporterGrowthSummary` changes; `SupporterGrowthHeightBasicsSummary` is removed

### XAML — AvatarScalingManagerWindow
- Remove Normal Height + Max Added Height `UniformGrid` block
- Wrap remaining sections into bordered cards with toggle collapse/expand headers and summary text overlays

### XAML — MainWindow
- Same structural changes as the manager window

### Code-behind
- No changes needed (bit range add/remove handlers use `AvatarScaleBitGrowthRange` which stays unchanged)

### Runtime (BridgeCoordinator)
- Remove reading `SupporterGrowthNormalHeightMeters` from the snapshot; instead read the current height from VRChat at session start
- Remove `SupporterGrowthMaxAddedHeightMeters` clamping logic in `ExecuteSupporterGrowthAvatarScaleRuleAsync` (lines 5773-5778)
- In `RunSupporterGrowthScaleSessionAsync`, use dynamically-recorded `NormalHeightMeters` from state

### Serialization (BridgeRuntimeConfiguration)
- Remove `SupporterGrowthNormalHeightMeters` and `SupporterGrowthMaxAddedHeightMeters` from `AvatarScaleRuleSnapshot` record and `TryToAvatarScaleSnapshot()` construction
- Remove `ClampScaleHeight` / `ClampRelativeScaleHeight` calls for these fields in snapshot construction

### Localization
- Remove localization keys for "Normal Height", "Max Added Height", and related helper text
- Add localization keys for card headers and collapsed-summary descriptions if they differ from existing keys

## Migration Notes

- Existing saved runtime configs that contain `SupporterGrowthNormalHeightMeters` and `SupporterGrowthMaxAddedHeightMeters` in snapshot data will be deserialized as orphan fields (the snapshot record removes these properties). This is safe — the new runtime ignores orphan JSON fields.
- No migration of user data needed. The dynamic baseline replaces the stored normal height at runtime.
- The `BypassVrChatScaleLimits` flag and `ClampScaleHeight()` in the snapshot pipeline continue to apply global safety to computed target heights.

## Open Questions

- Should the "inactivity timer" and/or "transition seconds" fields (currently in the model but hidden from the editor) be added to the General card? They become more relevant with dynamic baseline tracking since the user may want to control how fast and how soon the return happens.
