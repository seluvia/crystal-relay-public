# Power Up Avatar Swap Polish

## Problem
The Avatar Swap window has a "⚡ Power-up" button (`AvatarSwapManagerWindow.xaml:425-427`) that creates a `TriggerRule` with `TriggerType.PowerUp` via `AddAdvancedTrigger("PowerUp")`. This rule is added to `ChannelPointRules` on the swap profile, but the runtime Power Up event dispatcher (`BridgeCoordinator.HandlePowerUpEventAsync`) only matches against `PowerUpRuleSnapshot` objects from the main Power Up tab — never against individual `TriggerRule` entries from avatar swap profiles. The button produces dead rules that silently do nothing.

## Solution (Approach B)
Centralize Power Up configuration in the main Power Up tab. Remove the broken inline trigger button from the swap editor. The roulette editor's "Power Up" button already navigates to the main tab and stays unchanged.

### Changes

1. **`AvatarSwapManagerWindow.xaml`** — Remove the "⚡ Power-up" button from the swap editor's Advanced Triggers section (line ~427).

2. **No other changes.** The `AddAdvancedTrigger` method's `default` fallback still handles `"PowerUp"` if ever called programmatically, but no UI path reaches it.

### UX Flow
- User in Avatar Swap window wants Power Up → avatar change → clicks "Power Up" → main Power Up tab opens → creates/links a `PowerUpRule` there with Avatar Change as its action
- No dead inline triggers

## Non-Changes
- Roulette's "Power Up" button — already navigates correctly to the main tab
- Main Power Up tab — linking combo box already works (reads Twitch Custom Power-up catalog, links by ID)
- `AddAdvancedTrigger` method — kept as-is; removing the button alone is sufficient
