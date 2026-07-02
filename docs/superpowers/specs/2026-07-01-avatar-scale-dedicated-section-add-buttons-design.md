# Avatar Scaling — Dedicated Section Add Buttons

**Date:** 2026-07-01
**Status:** Approved
**Component:** Avatar Scaling Manager (`AvatarScalingManagerWindow.xaml`, `AvatarScalingManagerViewModel.cs`, `MainWindowViewModel.cs`)

## Problem

The Avatar Scaling Manager currently has only two Add buttons in the center panel:

1. **Add Scale Redeem** in the Child Scale Rewards header — creates a Channel Point Reward rule (correct).
2. **Add Reward Growth** in the shared Pay System Rewards header — creates a Supporter Growth rule.

The Cash Payments and Power Ups sections have **no Add button** at all. To create a Cash Payment or Power Up rule that shows up as an Avatar Scaling card, the user must leave the Avatar Scaling Manager, open the Cash Payments or Power Ups tab in the main window, create a rule there, switch its `ActionKind` to `AvatarScaling`, then come back to the manager. This is not user-friendly.

The shared "Add Reward Growth" button also only creates one type (Supporter Growth), so it is misleading to place it at the top of a section that contains three distinct source types.

## Goal

Each of the four Scaling Source sections gets its own dedicated Add button that creates a new card pre-set to that section's system type:

| Section | Add Button | Creates |
|---|---|---|
| Child Scale Rewards | Add Scale Redeem | `AvatarScaleRule` with `TriggerType = ChannelPointReward` (existing, unchanged) |
| Supporter Growth | Add Supporter Growth | `AvatarScaleRule` with `TriggerType = SupporterGrowth` |
| Cash Payments | Add Cash Payment | `CashPaymentRule` with `ActionKind = AvatarScaling` |
| Power Ups | Add Power Up | `PowerUpRule` with `ActionKind = AvatarScaling` |

The shared "Add Reward Growth" button in the Pay System Rewards header is removed and replaced by the three section-specific buttons.

## Design

### ViewModel changes — `MainWindowViewModel.cs`

Two new private methods and two new `RelayCommand` properties:

- `AddAvatarScalingCashPaymentRule()` — calls a new factory that creates a `CashPaymentRule` with `ActionKind = CashPaymentActionKind.AvatarScaling` (so `UsesAvatarScaling` is true and the card appears in the manager). Adds to `Settings.CashPaymentRules`, sets `SelectedCashPaymentRule`, queues save/refresh, logs.
- `AddAvatarScalingPowerUpRule()` — calls a new factory that creates a `PowerUpRule` with `ActionKind = PowerUpActionKind.AvatarScaling`. Adds to `Settings.PowerUpRules`, sets `SelectedPowerUpRule`, queues save/refresh, logs.
- `AddAvatarScalingCashPaymentRuleCommand` (new `RelayCommand`)
- `AddAvatarScalingPowerUpRuleCommand` (new `RelayCommand`)

Rationale for new commands instead of reusing `AddCashPaymentRuleCommand` / `AddPowerUpRuleCommand`: the existing commands create rules with `ActionKind = TriggerAction`, which would NOT show up as Avatar Scaling cards (the manager filters on `UsesAvatarScaling`). Creating scaling-specific commands keeps the existing main-window behavior intact and avoids surprising users who create Cash Payment / Power Up rules from the main window.

The existing `AddRewardGrowthCommand` stays in `MainWindowViewModel` (it is still the backing for the manager's Supporter Growth add), but the manager XAML will rebind it into the Supporter Growth section header instead of the shared Pay System header.

### Factory methods

- `CreateDefaultAvatarScalingCashPaymentRule()` — private static in `MainWindowViewModel`. Builds a `CashPaymentRule` with `ActionKind = AvatarScaling`, `Provider = StreamElements`, sensible defaults, and a default `ScaleAction` (already provided by `CashPaymentRule.CreateDefaultScaleAction()`).
- `CreateDefaultAvatarScalingPowerUpRule()` — private static in `MainWindowViewModel`. Builds a `PowerUpRule` with `ActionKind = AvatarScaling`, `SourceMode = LinkExisting`, `BitsCost = 100`, and a default `ScaleAction`.

### ViewModel pass-through — `AvatarScalingManagerViewModel.cs`

Two new pass-through properties mirroring the existing pattern:

```csharp
public RelayCommand? AddAvatarScalingCashPaymentRuleCommand => mainWindowViewModel?.AddAvatarScalingCashPaymentRuleCommand;
public RelayCommand? AddAvatarScalingPowerUpRuleCommand => mainWindowViewModel?.AddAvatarScalingPowerUpRuleCommand;
```

### XAML changes — `AvatarScalingManagerWindow.xaml`

1. **Supporter Growth section header** — add a DockPanel with a right-docked "Add Supporter Growth" button bound to `AddRewardGrowthCommand`. **Remove** the `Visibility` gate that hid the button based on `HasSelectedAvatarScaleSet` — the button should always be visible when the Supporter Growth section is visible. The command's internal `EnsureSelectedAvatarScaleSet()` guard already no-ops safely when no scale set is selected, so the button can stay visible without creating broken state.

2. **Cash Payments section header** — add a DockPanel with a right-docked "Add Cash Payment" button bound to `AddAvatarScalingCashPaymentRuleCommand`. No `HasSelectedAvatarScaleSet` gate (Cash Payment rules are top-level, not owned by a scale set).

3. **Power Ups section header** — add a DockPanel with a right-docked "Add Power Up" button bound to `AddAvatarScalingPowerUpRuleCommand`. No `HasSelectedAvatarScaleSet` gate (Power Up rules are top-level).

4. **Remove** the shared "Add Reward Growth" button and its DockPanel from the Pay System Rewards header. Keep the section title and description text.

5. **Keep** the existing "Add Scale Redeem" button in the Child Scale Rewards header unchanged.

### Card Delete behavior (unchanged)

The existing `DeleteCardCommand` already handles all four card kinds (`TwitchReward`, `SupporterGrowth`, `CashPayment`, `PowerUp`) and dispatches to the correct removal method. No changes needed.

### Localization

Two new keys added to `en-US.extra.json` and translated in all non-English `*.extra.json` files (14 files total):

- `"Add Supporter Growth"` (the dedicated section button; the existing `"Add Reward Growth"` key is kept for compatibility but no longer bound in the manager)
- `"Add Cash Payment"`

Note: `"Add Power Up"` already exists in all 14 localization files and is reused by the Power Ups section Add button — no new key needed for it.

Brand/technical terms stay in English across all languages: `Bits`, `Subs`, `Cash Payment`, `Power Up`, `Supporter Growth`.

### Tests

- `AvatarScalingManagerViewModelTests.cs` — add tests asserting the two new pass-through commands are exposed when a parent `MainWindowViewModel` is present, and null when absent.
- New test: `AddAvatarScalingCashPaymentRuleCommand_CreatesRuleWithAvatarScalingActionKind` — executes the command via the parent `MainWindowViewModel`, asserts a `CashPaymentRule` with `ActionKind = AvatarScaling` was added to `Settings.CashPaymentRules`.
- New test: `AddAvatarScalingPowerUpRuleCommand_CreatesRuleWithAvatarScalingActionKind` — same for `PowerUpRule`.
- `AvatarScalingManagerWindowXamlTests.cs` — add a test asserting each section header contains its dedicated Add button bound to the correct command.

## Non-goals

- No changes to the main-window Cash Payments / Power Ups tabs or their existing add commands.
- No changes to the card template, editor panel, or delete behavior.
- No changes to scale set creation (`AddAvatarScaleSetCommand`).
- No changes to the Master Unlock Reward card.

## Files touched

- `VrcTwitchOscBridge\ViewModels\MainWindowViewModel.cs` — 2 new factory methods, 2 new commands, 2 new command fields
- `VrcTwitchOscBridge\ViewModels\AvatarScalingManagerViewModel.cs` — 2 new pass-through properties
- `VrcTwitchOscBridge\AvatarScalingManagerWindow.xaml` — rework section headers (3 sections get Add buttons, shared header button removed)
- `VrcTwitchOscBridge\Resources\Localization\*.extra.json` (14 files) — 2 new keys
- `VrcTwitchOscBridge.Tests\AvatarScalingManagerViewModelTests.cs` — new command exposure + creation tests
- `VrcTwitchOscBridge.Tests\AvatarScalingManagerWindowXamlTests.cs` — section header button tests
