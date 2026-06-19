# Implementation Plan: Avatar Roulette UI and Rewards Fix

This plan outlines the steps to fix the Avatar Roulette UI and complete the missing rewards/addition functionality in the `AvatarSwapManagerWindow`.

## Goals
- Fix the summary display in `InlineRouletteRuleRowViewModel` for all trigger types.
- Ensure `AvatarSwapManagerViewModel` uses the correct `Inline...RowViewModel` for Roulette triggers so the UI can render the proper templates (Bits, Subs, etc.).
- Enable adding triggers of different types (Bits, Subs, etc.) in the Roulette editor.
- Add functionality to add new avatars to the Roulette Pool.
- Improve the UI layout in `AvatarSwapManagerWindow.xaml` to support these additions.

## Implementation Steps

### 1. Fix Summary Display
- **File**: `VrcTwitchOscBridge/UserControls/InlineRouletteRuleRowViewModel.cs`
- **Task**: Update `RefreshSummary()` to check `TriggerType` and include relevant info (cost/amount) for Bits, Subs, etc.

### 2. Update ViewModel Logic
- **File**: `VrcTwitchOscBridge/ViewModels/AvatarSwapManagerViewModel.cs`
- **Tasks**:
    - **RebuildRows**: In the loop for `SelectedRouletteCard.Roulette.Triggers`, use a `switch` or `if/else` to instantiate the correct `Inline...RowViewModel` based on `TriggerType`.
    - **AddAdvancedTrigger**: Update the command to handle `SelectedRouletteCard` specifically.
    - **AddRoulettePoolEntry**: Implement a new command to add an `AvatarRouletteEntry` to the selected roulette's pool.

### 3. Update UI (XAML)
- **File**: `VrcTwitchOscBridge/AvatarSwapManagerWindow.xaml`
- **Tasks**:
    - **Roulette Editor Content**:
        - Add buttons to the `Roulette` `DataTemplate` (in `ContentControl.Resources`) for:
            - `+ Add Channel Point Trigger`
            - `+ Add Bits Trigger`
            - `+ Add Subs Trigger`
            - `+ Add Payment Trigger`
        - Add an `Add Avatar to Pool` button in the `Pool` section.
    - **Update DataTemplate for Roulette**: Ensure the `ItemsControl` for `Triggers` uses the generic `uc:InlineRouletteRuleRowViewModel` which will now benefit from correct templates in the `DataTemplate` (via `AvatarSwapManagerWindow.xaml` resources).

### 4. Verification
- Compile and run the app (via Debug launcher).
- Verify that selecting a Roulette shows the correct trigger summaries.
- Verify that adding different types of triggers works and they appear with correct UI.
- Verify that adding avatars to the pool works and updates the list.
