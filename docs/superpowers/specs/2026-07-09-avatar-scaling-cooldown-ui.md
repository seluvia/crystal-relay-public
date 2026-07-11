# Avatar Scaling Cooldown UI

**Date:** 2026-07-09
**Status:** Design approved

## Problem

Users cannot configure cooldowns on avatar scaling channel-point rewards. The `CooldownSeconds` property exists on the model and flows through the full persistence and runtime pipeline, but the UI field is missing from the main scaling rule editor. New rules default to 0 (no cooldown), and there is no way to change it.

## Scope

Add a "Cooldown Seconds" text field to the Avatar Scale Rule editor in `AvatarScalingManagerWindow.xaml`, visible only when the trigger type is `ChannelPointReward` with `CreateOrManage` sync mode. Set a sensible default of 30s for new channel-point scaling rules. Add a timing summary text to help users understand the relationship between Active Time and Cooldown.

Cooldowns remain restricted to channel-point rewards (bits, subs, chat commands, cash payments, and other paid triggers keep no cooldown enforcement — per current design, since paid triggers should not be throttled).

## Changes

### 1. XAML: Add CooldownSeconds field

**File:** `AvatarScalingManagerWindow.xaml` — "Timer & Return" section (~line 1550)

Insert a new `UniformGrid Columns="2"` row between the Active Time/Return Height row and the Return Mode/Smooth Transition row:

```
Active Time Seconds   | Cooldown Seconds
Return Height         | Return Mode
Smooth Transition Sec | Timing summary text
```

The Cooldown Seconds row visibility is gated to `ChannelPointReward + CreateOrManage` using the same pattern as managed reward colors (collapsed for other trigger types).

### 2. Model: Add TimingSummary property

**File:** `AvatarScaleRule.cs`

Add a computed `TimingSummary` string property:

```
Active: {ActiveTimeSeconds}s → Cooldown: {CooldownSeconds}s → Ready: {total}s
```

Where `total = ActiveTimeSeconds + CooldownSeconds`. The summary is empty when `CooldownSeconds <= 0`.

Raise `PropertyChanged(nameof(TimingSummary))` in the setters of `ActiveTimeSeconds` and `CooldownSeconds`.

### 3. Default: Set 30s cooldown for new rules

**File:** `MainWindowViewModel.cs` — `CreateDefaultAvatarScaleRule()` method (~line 19707)

Add `CooldownSeconds = 30` to the object initializer.

### What does NOT change

- `SettingsStore.cs` — `CooldownSeconds` is already persisted and restored
- `BridgeRuntimeConfiguration.cs` — snapshot cooldown zeroing for non-ChannelPointReward triggers stays
- `BridgeCoordinator.cs` — runtime cooldown enforcement stays per-rule (global)
- Master reward cooldown field (already in UI)
- Cash Payment / Power-Up cooldown fields (already in UI)

## Testing

- Build the app project and verify the cooldown field appears in the scaling rule editor when the trigger type is ChannelPointReward
- Verify it is hidden for Bits, Chat Command, Follow, Subscription, Supporter Growth trigger types
- Verify the timing summary updates reactively when Active Time or Cooldown changes
- Verify that a new scaling rule defaults to 30s cooldown
- Verify that an existing rule loaded from settings preserves its cooldown value
