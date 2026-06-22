# Compact Editor: Typed Action Inputs + ComboBox Theme

## Problem

The compact Avatar Sets rule editor (`AvatarSetsManagerWindow.xaml`) has four
issues that show up when a streamer is editing a rule that targets a Float,
Int, or Bool avatar parameter:

1. ComboBox selected text is white-on-white (invisible).
2. ComboBox dropdown items render in near-black on the dark theme (unreadable).
3. The Int parameter type is missing its action inputs. The full editor
   (`UserControls/AvatarSwapRuleEditorControl.xaml`) has a Cycle / Random /
   Fixed mode selector plus Min/Max and When/After inputs, but the compact
   editor only shows a generic `ParameterValue` text box.
4. The Float Action Mode card is showing even when the Parameter List Filter
   is set to "Bool". This is not actually broken — the card is bound to
   `UsesFloatActionMode` which correctly checks the rule's `ParameterType`,
   not the filter — but the missing theme on the Parameter Type ComboBox
   makes it look like the card and the filter disagree, which is
   confusing.

## Goals

- Make every ComboBox in the compact editor readable on the current theme.
- Give the Int parameter type a compact set of action inputs that mirrors
  the full editor: mode selector, Min/Max, and When/After.
- Keep the Float Action Mode card bound to the rule's `ParameterType` (it
  is correct as-is).
- Keep the Bool True/False chips (they already exist and work).
- Do not change the full editor or any runtime behavior.

## Non-Goals

- Not porting every Float-side feature (Float Value Mode, Smooth
  Transition, Active Boost, Bits/Subs Add) into the compact editor.
  Those are out of scope for this change.
- Not changing the Parameter List Filter semantics. The filter still
  controls only the parameter list below it.
- Not changing the Parameter Type ComboBox's data source or binding.
- Not changing the `UsesFloatActionMode` definition or the Float Action
  Mode card's visibility logic.

## Design

### 1. Global ComboBox theme (in `AvatarSetsManagerWindow.xaml`)

Add a single `Style x:Key="ComboBoxStyle" TargetType="ComboBox"` to the
Window's resources that sets:

- `Foreground = {DynamicResource TextBrush}`
- `Background = {DynamicResource InputBrush}`

Apply this style to every ComboBox in the compact editor that currently
has no explicit style or `Foreground`/`Background`:

- `ParameterType` ComboBox
- `RewardSyncMode` ComboBox
- `FloatClampMode` ComboBox

Why scope to this window: the full editor's ComboBoxes are styled inside
its own control template and look correct. Touching the global app
resources risks regressing other windows. The compact editor is the
reported regression site, so fix it there.

### 2. Int action inputs (in `AvatarSetsManagerWindow.xaml`)

Add a new `StackPanel` immediately after the existing Parameter Value
section, visible when `UsesIntParameter`. The section mirrors the full
editor's Int block (`AvatarSwapRuleEditorControl.xaml:1332-1391`):

- Int mode selector ComboBox bound to `IntZeroDurationMode` with options
  pulled from `DataContext.IntZeroDurationModes`. Visible when
  `UsesIntInstantModeOptions` (instant actions only).
- `Send This Number` text box bound to `ParameterValue`. Visible when
  `UsesIntFixedInstantValue`.
- `Minimum Number` / `Maximum Number` text boxes bound to
  `RangeMinimum` / `RangeMaximum` in a 2-column `UniformGrid`. Visible
  when `UsesIntRangeInputs`.
- `When Triggered, Set To` / `After Active Time Ends, Set Back To` text
  boxes bound to `ParameterValue` / `ResetValue` in a 2-column
  `UniformGrid`. Visible when `UsesIntTimedValues`.

All `UsesXxx` properties already exist on `TriggerRule` and are raised by
`RaiseActionVisibilityProperties()`, so no model changes are required.

### 3. Localized labels (in all base locale JSON files)

Add the following keys to every `VrcTwitchOscBridge/Resources/Localization/*.json`
base file (the float-mode labels were added to base files in the prior
session, so follow the same pattern):

- `InstantIntAction` — header above the Int mode selector
- `SendThisNumber` — label above the Fixed mode value box
- `MinimumNumber` — label above the Min input
- `MaximumNumber` — label above the Max input
- `IntWhenTriggered` — label above the When Triggered box
- `IntAfterActiveTime` — label above the After Active Time box

### 4. No change to Float Action Mode visibility

`UsesFloatActionMode` is defined as
`UsesAvatarParameter && ParameterType == OscParameterType.Float` and is
already raised by `RaiseActionVisibilityProperties()`. The card already
shows only when the rule's `ParameterType` is Float. The user's confusion
came from the unreadable Parameter Type ComboBox hiding which type is
selected; fixing the ComboBox theme resolves the confusion.

### 5. No change to Bool True/False chips

The compact editor already has True/False chips for `ParameterValue` and
`ResetValue`, both gated on `UsesBoolParameter`. They are correct.

## Files Affected

- `VrcTwitchOscBridge/AvatarSetsManagerWindow.xaml`
  - Add `ComboBoxStyle` resource
  - Apply `Style="{StaticResource ComboBoxStyle}"` to the three
    ComboBoxes
  - Add Int action inputs `StackPanel`
- `VrcTwitchOscBridge/Resources/Localization/*.json` (16 base files)
  - Add 6 new keys each
- `VrcTwitchOscBridge.Tests/AvatarSetsManagerWindowXamlTests.cs`
  - Add regression test for the new ComboBox style
  - Add regression test for Int mode selector presence
  - Add regression test for Min/Max and When/After inputs

## Testing

Three new XAML-source regression tests in
`VrcTwitchOscBridge.Tests/AvatarSetsManagerWindowXamlTests.cs`:

1. `ComboBoxStyle_ExistsAndUsesThemeBrushes` — asserts the new
   `ComboBoxStyle` resource exists and references `TextBrush` and
   `InputBrush` as DynamicResources.
2. `CompactEditor_HasIntModeSelector` — asserts the Int mode selector
   ComboBox exists in the compact editor and is bound to
   `IntZeroDurationMode`.
3. `CompactEditor_HasIntMinMaxAndWhenAfterInputs` — asserts the Min and
   Max text boxes are bound to `RangeMinimum` / `RangeMaximum` and the
   When/After text boxes are bound to `ParameterValue` / `ResetValue`,
   all under the Int parameter section.

Existing tests must continue to pass: 246 passed, 7 skipped (pre-existing
skipped count).

The localization audit must continue to report no new hardcoded text
introduced by this change.

## Risks

- The new `ComboBoxStyle` is scoped to the AvatarSetsManagerWindow. If
  other windows in the app have the same ComboBox readability issue, this
  fix will not address them. That is acceptable for this change; a wider
  audit can be a follow-up.
- Adding 6 localization keys to 16 locale files is a known pattern in
  this project (the prior session added 1 key across the same 16 files).
  Translation quality rules in `AGENTS.md` apply: keep brand/technical
  terms in English, use informal register, preserve placeholders exactly.
- The Int action inputs use the same `UsesXxx` visibility properties that
  the full editor uses. If any of those properties have a bug, the compact
  editor will inherit it. Existing float-mode tests cover
  `UsesFloatActionMode`; the new tests should cover the Int equivalents
  at least at the XAML-source level.
