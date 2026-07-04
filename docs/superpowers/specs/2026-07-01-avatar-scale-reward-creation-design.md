# Avatar Scaling Manager — Reward Creation & Card Actions Rework

## Date
2026-07-01

## Problem
1. "Add Scale Redeem" always creates a Channel Point Reward with no way to choose the reward type. Pay-system rewards (Supporter Growth, Cash Payments, Power Ups) can't be added from this manager at all.
2. The two-column layout doesn't show by default because `ActiveSourceView` defaults to `TwitchRewards`, which collapses the right column.
3. Delete/add buttons live in the editor header (a WrapPanel that appears when editing any card), making it easy to accidentally delete all cards in a set.
4. Cards only have an Edit button — no Delete button on the card itself.

## Approved Design

### 1. Default to "All Sources" view
Change the default `ActiveSourceView` from `TwitchRewards` to `AllSources` so both columns (Child Scale Rewards + Pay System Rewards) show side-by-side immediately when opening the manager.

**File:** `AvatarScalingManagerViewModel.cs` — change `activeSourceView` field initializer.

### 2. Separate Add buttons per section

**Child Scale Rewards section (left column):**
- Add an "Add Scale Redeem" button in the section header (next to the "Child Scale Rewards" title)
- This button calls the existing `AddAvatarScaleRuleCommand`, which creates a Channel Point Reward rule in the selected scale set
- Only visible when a scale set exists (`HasSelectedAvatarScaleSet`)
- Remove the "Add Scale Redeem" button from the "Twitch Reward Scaling" header at the top (line ~914)

**Pay System Rewards section (right column):**
- Add an "Add Reward Growth" button in the section header (next to the "Pay System Rewards" title)
- This button calls a new `AddRewardGrowthCommand` that creates a Supporter Growth rule in the selected scale set
- The new command lives in `MainWindowViewModel` and mirrors `AddAvatarScaleRule` but sets `TriggerType = AvatarScaleTriggerType.SupporterGrowth`
- Cash Payment and Power Up rules are NOT created from here — they're managed from their respective main sections and appear in this column when they have `ActionKind = AvatarScaling`

### 3. Delete button on each card

Add a "Delete" button next to the "Edit" button in the `SourceCardTemplate` DataTemplate.

**Behavior by card kind:**
- **Twitch Reward cards:** Calls `RemoveSelectedAvatarScaleRuleCommand` (or a new card-level delete command) with the card's `ScaleRule`
- **Supporter Growth cards:** Same as Twitch Reward — removes the scale rule
- **Cash Payment cards:** Calls `RemoveSelectedCashPaymentRuleCommand` with the card's `CashPaymentRule`
- **Power Up cards:** Calls `RemoveSelectedPowerUpRuleCommand` with the card's `PowerUpRule`
- **Master Reward card:** Delete button is hidden (can't delete the master reward from a card)

**Implementation approach:** Add a `DeleteCommand` to `AvatarScalingSourceCardViewModel` that's wired up during card construction with the appropriate removal action, or add a new `DeleteCardCommand` on the manager ViewModel that takes the card as parameter and dispatches to the right removal command based on `Kind`.

The simpler approach: add a single `DeleteCardCommand` on `AvatarScalingManagerViewModel` that takes an `AvatarScalingSourceCardViewModel` parameter and calls the right removal command based on the card's `Kind` and underlying rule.

### 4. Remove editor action buttons

Remove the entire WrapPanel at lines ~1121-1138 from the editor header. This removes:
- Add Scale Set
- Delete Scale Set
- Add Scale Redeem
- Delete Scale Redeem
- Test Scale

The editor header only shows the card title and close button. "Add Scale Set" remains in the Global Safety Rule card area (line ~865) and "Add Scale Redeem" moves to the Child Scale Rewards section header.

"Test Scale" is removed from the editor. It can be re-added to cards in a future iteration if needed.

### 5. Reward type control

With separate buttons:
- "Add Scale Redeem" → creates Channel Point Reward type (for Twitch chat redeems)
- "Add Reward Growth" → creates Supporter Growth type (for Bits/Subs-driven growth)

The trigger type is still changeable in the editor's "Trigger Type" dropdown after creating, so you can switch from Channel Point Reward to Bits/Subs/Follow/Chat Command after creating.

## What stays unchanged
- Global Safety Rule card on top
- Master Unlock Reward section below it
- Left navigation panel (Scaling Sources filter)
- Right editor panel (the editing form itself)
- SourceCardTemplate layout (compact version from previous rework)
- Two-column Grid layout structure from previous rework
- `UpdateColumnLayout` code-behind for column collapsing

## Files to modify
- `VrcTwitchOscBridge/ViewModels/AvatarScalingManagerViewModel.cs` — default to AllSources, add `DeleteCardCommand`, expose `AddRewardGrowthCommand`
- `VrcTwitchOscBridge/ViewModels/MainWindowViewModel.cs` — add `AddRewardGrowth` method and command
- `VrcTwitchOscBridge/AvatarScalingManagerWindow.xaml` — move Add buttons to section headers, add Delete button to SourceCardTemplate, remove editor WrapPanel, remove top Add Scale Redeem button
- `VrcTwitchOscBridge.Tests/AvatarScalingManagerViewModelTests.cs` — update test for AllSources default, add test for DeleteCardCommand
- `VrcTwitchOscBridge.Tests/AvatarScalingManagerWindowXamlTests.cs` — update tests that reference the removed editor WrapPanel
- All 14 `VrcTwitchOscBridge/Resources/Localization/*.extra.json` — add "Add Reward Growth" and "Delete" keys

## Risks
- Removing Test Scale from the editor means there's no quick way to test a scale rule from the manager. This is acceptable per user's decision.
- The `DeleteCardCommand` needs to handle the case where the card's underlying rule is null (e.g., Master Reward card) — in that case the Delete button should be hidden, not just disabled.
- Changing the default to AllSources means the window opens showing both columns, which requires enough horizontal space. The GridSplitter on the editor panel lets the user adjust.
