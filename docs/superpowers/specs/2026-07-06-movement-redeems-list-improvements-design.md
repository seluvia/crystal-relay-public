# Movement Redeems List and Editor Improvements

## Summary

Two visual/UX changes to the Movement Redeems manager window:

1. **Remove the editable Rule Name field** from the editor panel and instead auto-generate display names from trigger/reward info.
2. **Add alternating row backgrounds** to the DataGrid for readability.

## Changes

### 1. Display Name Logic

The DataGrid Name column will show an auto-generated label instead of the editable `TriggerRule.Name`. The internal `Name` field stays on the model (it's shared with `TriggerRule`) but is never shown or directly edited by the user.

| Trigger Type | Display Name Source |
|---|---|
| Channel Points | `ChannelPointRewardTitle` (the Twitch reward title) |
| Chat Command | The chat command text (e.g. `!movefwd`) |
| Bits / Subs / Gift Sub / Follow | Auto-generated: `"{trigger type} {direction}"` (e.g. `"Bits Move Forward"`, `"Subs Spin Left"`) |

The editor panel's **Rule Name** textbox is removed. The **Reward Title** textbox (visible under Channel Points trigger) remains. When the user links an existing Twitch reward, the synced `ChannelPointRewardTitle` automatically populates the display name.

### 2. Grid Readability

Replace the current all-transparent rows with alternating row backgrounds:

- Even rows: transparent (no change)
- Odd rows: subtle tint (existing `AccentDimBrush` or a new semi-transparent overlay) to guide the eye horizontally

This follows the pattern used elsewhere in the app (main window lists, etc.) and is cleaner than visible grid lines against the dark purple theme.

## Files Affected

| File | Change |
|---|---|
| `ViewModels/MovementRedeemCardViewModel.cs` | Add `DisplayName` computed property mirroring `TriggerRule.DisplayTitle` logic; remove direct `Name` binding exposure if unused |
| `MovementRedeemsManagerWindow.xaml` | Bind Name column to `DisplayName`; remove Rule Name textbox from editor panel; add `AlternatingRowBackground` to DataGrid |
| `TriggerRule.cs` | No structural changes; existing `DisplayTitle` logic is sufficient |
