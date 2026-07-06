# Movement Redeem Editor Enhancement Design

## Overview

Enhance the `MovementRedeemsManagerWindow` slide-out editor panel to expose all `TriggerRule` model properties that are currently missing from the UI. The editor follows the same patterns as the Universal Triggers and Avatar Scaling editors.

## Layout

The slide-out panel expands from its current narrow form to 480px wide. Content is organized into scrollable bordered sections with a footer bar. The slide-out opens when clicking "Edit" on a card and overlays the card list with a semi-transparent backdrop.

## Sections

### Section 1: General Settings
- **Rule Name** — text box
- **Enabled** — toggle switch
- **Movement Direction** — grouped ComboBox using `MovementTypeClassifier.GetCategory()` for category headers (Movement, Turning, Hand Interactions, Held Object, UI Toggles). Replaces the current read-only display.
- **VR Only** — informational badge, visible when selected direction is VR-only
- **Behavior Tooltip** — read-only info text from classifier

### Section 2: Trigger Configuration
- **Trigger Type** — ComboBox (Channel Points, Bits, Subs, Gift Sub, Follow, Chat Command)
- Conditional sub-sections via `DataTrigger` on computed boolean properties:

  **Channel Points selected:**
  - Sync Mode: CreateOrManage / LinkExisting
  - Reward Title, Cost, Description
  - Ready Color, Cooldown Color (text boxes with hex values)
  - Delete-when-inactive checkbox

  **Chat Command selected:**
  - Command Text
  - Permission level (Everyone / Moderators / Subs / VIPs)

  **Bits selected:**
  - Minimum Amount
  - Bits Keyword toggle + keyword text

  **Subs / Gift Sub selected:**
  - Minimum Amount
  - Sub tier checkboxes (Tier 1, Tier 2, Tier 3 — from SubscriptionTier flags)
  - Gift Sub checkbox

  **Follow selected:**
  - No extra fields

### Section 3: Movement Behavior
- **Duration (seconds)** — text box, min 1
- **Cooldown (seconds)** — text box, 0 = no cooldown
- **Speed (0.1–1.0)** — text box, visible only for axis-type directions via DataTrigger on `IsAxisType`
- **Amount Scaled Duration** — toggle section with expand/collapse:
  - Units Per Duration
  - Seconds Per Unit
  - Conditional visibility based on Bits/Subs trigger
- **Max Accumulated Duration** — toggle + max seconds text box
- **Extend Current Activity** — toggle (extend vs replace active timer)

### Section 4: Avatar Scope
- Expandable/collapsible sub-section
- Supporter Avatar Profile ID
- Supporter Avatar ID
- Supporter Avatar Name
- Optional: restricts the rule to the specified VRChat avatar

### Section 5: Bot Message
- Text box for custom Twitch chat bot message template
- Placeholder / help text explaining available template variables

### Section 6: Set Triggers (OSC Actions)
- Dynamic list of `SetTriggerAction` items matching Universal Triggers pattern
- Each item row: OSC address text box, value type ComboBox (Bool/Int/Float/String), target value input (switches per type), default value input, duration text box
- Add button in section header
- Delete button per item

### Footer Bar
- **Delete** button
- **Test Now** button
- **Save** button

## ViewModel Changes

### MovementRedeemsManagerViewModel additions:
- `SelectedRule` property (the `TriggerRule` being edited)
- `MovementDirections` — grouped collection for the direction ComboBox
- Computed boolean properties for conditional visibility:
  - `UsesChannelPointReward`
  - `UsesChatCommand`
  - `UsesBits`
  - `UsesSubscription`
  - `UsesFollow`
  - `UsesGiftSub`
  - `IsAxisType`
  - `IsVrOnly`
- Command: `SaveEditorCommand`, `DeleteRuleCommand`, `TestRuleCommand`, `CloseEditorCommand`
- `IsEditorOpen` — controls slide-out visibility
- `IsNewRule` — flag to show "Add" vs "Edit" in title

### MovementRedeemCardViewModel changes:
- Already exists — minor property adjustments as needed

## XAML Changes

### MovementRedeemsManagerWindow.xaml
- Expand slide-out `Grid` width from current to 480
- Add scrollable `StackPanel` inside the slide-out with bordered sections
- Add `DataTemplate` for each conditional sub-section
- Wire `DataTrigger` visibility to ViewModel computed properties
- Maintain existing card list and category filter layout

### No changes to:
- MainWindow.xaml
- AvatarSwapRuleEditorControl.xaml (old editor stays as-is for now)
- Model classes (TriggerRule already has all properties)

## Data Flow

1. User clicks "Edit" on a card → sets `IsEditorOpen = true` and `SelectedRule` to the clicked rule
2. User modifies fields → changes go directly to `SelectedRule` properties (two-way binding)
3. User clicks "Save" → validates, closes editor, persists settings
4. User clicks "Delete" → confirms, removes rule from set, closes editor
5. User clicks "Test Now" → runs `QuickTestRule` with the current rule

## Build Impact

- Only files changed: `MovementRedeemsManagerWindow.xaml`, `MovementRedeemsManagerViewModel.cs`, `MovementRedeemCardViewModel.cs`
- No new model files needed
- Follows existing patterns; no new NuGet dependencies
