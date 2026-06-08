# Scale Action UI Redesign — Design Spec

**Date:** 2026-06-08
**Lane:** v3.1.10 beta
**Scope:** Avatar Scaling → Scale Action section in the main editor (both the full editor panel and the nested "Avatar Scaling Action" panel inside Power-Up and Cash Payment rules).

## 1. Problem

The current `Scale Action` block in `MainWindow.xaml` (lines 6848–7157) and its nested twin (lines 7853–7964) show every field for every Mode in one long list. The result:

- A `Relative Height` user sees `Maximum Height` and may not realize it means something different from the `Maximum Height` shown for `Random` mode.
- `Bypass VRChat world min/max` is a hidden opt-in checkbox that most users never touch, so they are silently capped by the world's 5m radial.
- `Bypass` is a separate concept from the per-action Min/Max the user typed, and the UI does not connect them.
- The `Advanced Range` toggle (0.01m – 10000m) is not visually tied to the Min/Max inputs above it.
- The Multiplier mode has a single `Height Multiplier` value with no obvious way to express a divisor.
- There is no live example anywhere in the panel to confirm what a given configuration will actually do.

## 2. Goals

- Make Mode the primary switch and only show fields that are relevant to the current Mode.
- Make Min/Max the user's true limits; when they are set, ignore VRChat's per-world 5m radial silently (no extra checkbox, no status pill).
- Keep Advanced Range as a separate explicit toggle with the bound written into the label.
- Give the user a simple `× / ÷` toggle in Multiplier mode so one reward can grow or shrink.
- Add a per-mode live preview line so the user can see what a configuration will do before they trigger it.
- Group the previously-flat "Hide at min / Hide at max" checkboxes under a single sub-heading so they read as one behavior.

## 3. Non-Goals

- No change to the underlying `AvatarScaleRule` model, persistence format, or runtime behavior of the scale action beyond what is described in §6.
- No change to the second (nested Power-Up / Cash Payment) panel's data binding or localization keys beyond label renames listed in §8.
- No new top-level "wizard" or stepper. The user stays in one card; the Mode swap is the stepper.
- No new `BoolToVisibility` converters. Existing `Style` + `DataTrigger` visibility pattern is reused.

## 4. Layout

The redesigned Scale Action card is one `Border` (existing pattern, `NestedPanelBrush` + `HighlightBorderBrush`, `CornerRadius="18"`, `Padding="16"`) containing five sub-cards in this order:

1. **Mode** — segmented row of mode buttons (Set / Random / Relative / Multiplier / Preset / Glitchy). Active button uses `HighlightBorderBrush` background; inactive uses a darker shade. Mirrors the visual style of the existing Theme tab strip in the same window.
2. **Value** (mode-specific card) — only one of these is visible at a time:
   - Set Height → `Target Height` + `Smooth Transition`
   - Random → `Random Min`, `Random Max`, `Smooth Transition`
   - Relative → `Change`, `Current` (default 1.64m, used by the live preview), `Smooth Transition`
   - Multiplier → `× / ÷` toggle button + `Value` + `Current` (default 1.64m, used by the live preview)
   - Preset → `Avatar Preset` dropdown + `Smooth Transition`
   - Glitchy → `Random Min`, `Random Max`, `Transition` (per-jump)
   - Each value card ends with a small dashed-border preview block (see §7).
3. **Range limits** (only for Relative and Multiplier) — `Minimum Height` + `Maximum Height` with a single helper line: `Min and Max are clamped between 0.20m and 100m. To go past the safe limit, enable Advanced VRChat scale range below.` Sub-section "Hide this reward when reaching a limit" groups the two existing Hide-at checkboxes.
4. **Behavior** — `Reward Cooldown`, `Active Time`, `Return Height`. Visibility of `Reward Cooldown` and `Active Time` continues to follow existing rules (Cooldown only when `UsesCreateOrManageReward`; Active Time only when `HasActiveTime`).
5. **Advanced** — single checkbox: `Unlock advanced VRChat scale range (0.01m – 10000m)`. No status pill, no helper text, no other checkboxes.

The two existing `Bypass VRChat world min/max` and `BypassVrChatScaleLimits` UI elements are removed from the visible UI. The behavior is folded into the runtime (see §6).

## 5. Mode Row

The Mode row is a `Grid` with 6 equal columns, each cell a `Button` styled to match the existing theme tab strip:

- Inactive: `Background = #3a2167` (one shade darker than the sub-card), `Foreground = TextBrush`, `FontWeight = Normal`.
- Active: `Background = #774ac7` (`HighlightBorderBrush`), `Foreground = #ffffff`, `FontWeight = SemiBold`.
- Same outer wrapper as the value card (1px border, 6px inner padding, 8px radius).

Clicking a button:
1. Sets the active style on itself and clears the active style on the others.
2. Sets the visibility of the matching value sub-card to `Visible` and the others to `Collapsed` via inline `Style` + `DataTrigger` against a new computed property `ActiveMode` on the rule (see §6.2).
3. Sets the visibility of the Range limits sub-card to `Visible` only when `ActiveMode ∈ {Relative, Multiplier}`.

## 6. Data Model Changes

### 6.1 New property

`AvatarScaleRule` gains one new persisted property:

- `double GlitchyTransitionSeconds { get; set; } = 0.4;`

This is the per-jump transition time used only by `GlitchyRandomHeight` mode. It is independent of the existing `SmoothTransitionSeconds` (which still applies to non-Glitchy modes and to the global wrap-around). It is **not** bound to the existing `SmoothTransitionSeconds` field.

### 6.2 New computed property

`AvatarScaleRule` gains one new computed property:

- `AvatarScaleMode ActiveMode => ScaleMode;`

Used to drive the Mode row's `DataTrigger` visibility rules for the value cards and the Range limits card.

### 6.3 Runtime change: silent auto-bypass

The runtime scale-execution path is updated so that, when **both** `RelativeMinimumHeightMeters` and `RelativeMaximumHeightMeters` are set on the rule AND the rule is in `Relative` or `Multiplier` mode, the world's 5m radial limit is ignored. No property change is needed; this is purely a behavior update inside the scale executor.

When `AdvancedRangeEnabled` is true, the bounds `0.01m – 10000m` are used as the safe range. When false, `0.20m – 100m` is used. The existing `BypassVrChatScaleLimits` property is kept in the model for backward compatibility with saved profiles (older profiles may have it as `true`), but the property is no longer bound to any UI control and is ignored by the runtime. The runtime now decides bypass purely from whether Min/Max are set.

### 6.4 Multiplier operator

The Multiplier mode gains a runtime interpretation of the existing `HeightMultiplier` value:

- Default behavior (`HeightMultiplier >= 1`): multiply, as today.
- When the user presses the `÷` button in the UI, the value is treated as a divisor. Internally this is implemented as a new enum-flag-style property on the rule:

  - `enum MultiplierDirection { Grow, Divide }` (default `Grow`)
  - Persisted as a new `int` field `MultiplierDirectionId` (0 = Grow, 1 = Divide) on `AvatarScaleRule`, with a default of 0 for backward compatibility.

The runtime interprets `Divide` as `currentEyeheight / value` clamped into the Range limits and then bounded by Advanced Range.

### 6.5 Input clamping

The Min/Max text inputs in the Range limits card clamp the entered value to the active bound on `LostFocus` and on `PropertyChanged` with `UpdateSourceTrigger=LostFocus`:

- Advanced off: clamp to `[0.20, 100]`.
- Advanced on: clamp to `[0.01, 10000]`.

The clamp is implemented in the property setter in `AvatarScaleRule` and runs every time the binding writes back. A tooltip on the input shows the active bound so the user knows why their value was changed.

## 7. Live Preview Block

Each value sub-card ends with a small dashed-border `Border` (`Background = #1a0f30`, `BorderBrush = #4a2c7a`, `BorderThickness = 1`, `BorderDashArray = 4 2`, `CornerRadius = 8`, `Padding = 8 10`, `Margin = 10 0 0 0`) that holds a single `TextBlock`. The text is recomputed on every input change and reads:

- **Set Height** — `Sets the avatar height directly to {TargetHeight}m.`
- **Random** — `Each trigger rolls a random height between {Min}m and {Max}m.`
- **Relative** — `Adds {+/-Change}m to the current height, going from {Current}m to {Result}m.`
- **Multiplier** — `Going from {Current}m to {Result}m using {× or ÷}{Value}.`
- **Preset** — `Sets the avatar height to the {Label} preset, which is {Height}m.`
- **Glitchy** — `Rapidly rolls random heights between {Min}m and {Max}m with a {Transition}s transition between each jump, until Active Time ends.`

The `Current` value used in the Relative and Multiplier previews is a small `TextBox` placed in the value card (default `1.64`) and labeled `Current (m)`. It is a **local UI-only input** that does not bind to any persisted property on `AvatarScaleRule`. Its only purpose is to make the preview meaningful before the user has triggered the rule. The actual runtime uses the avatar's current eye height at trigger time.

The preview text is rendered by a `MultiBinding` + `StringFormat`-style `IMultiValueConverter` in `Converters.cs` named `ScalePreviewConverter`. The converter takes the mode name, the relevant numeric inputs, and the divider direction, and returns the formatted string. One converter handles all six modes; the `Convert` method switches on the mode.

## 8. Localization

The `en-US.json` and `en-US.extra.json` files gain the following keys; all other languages' `.extra.json` files must be updated to match. The same localization rules in `AGENTS.md` apply: informal register, placeholders preserved exactly, brand/technical terms left in English.

New `en-US` keys:

- `Scale Action Mode Set Height` → `Set Height`
- `Scale Action Mode Random Height` → `Random`
- `Scale Action Mode Relative Height` → `Relative`
- `Scale Action Mode Multiplier` → `Multiplier`
- `Scale Action Mode Preset` → `Preset`
- `Scale Action Mode Glitchy Random Height` → `Glitchy`
- `Scale Action Current (m)` → `Current (m)`
- `Scale Action Multiplier Divide` → `÷`
- `Scale Action Multiplier Grow` → `×`
- `Scale Action Multiplier Direction` → `Direction`
- `Scale Action Hide At Limit` → `Hide this reward when reaching a limit`
- `Scale Action Glitchy Transition` → `Transition (s)`
- `Scale Action Behavior Header` → `Behavior`
- `Scale Action Advanced Header` → `Advanced`
- `Scale Action Range Limits Header` → `Range limits`
- `Scale Action Value Label` → `Value`
- The six preview strings each get a key: `Scale Preview Set`, `Scale Preview Random`, `Scale Preview Relative`, `Scale Preview Multiplier`, `Scale Preview Preset`, `Scale Preview Glitchy`. The placeholders are `{0}`, `{1}`, etc., matching the converter's argument order.

Renamed (existing keys with new value):

- `Bypass VRChat world min/max` is removed from the UI; the key may be removed or retained for legacy message lookup. Decision: **removed**.

## 9. File Changes

### 9.1 XAML

- `VrcTwitchOscBridge\MainWindow.xaml` — replace lines 6848–7157 (primary Scale Action card) and lines 7853–7964 (nested Avatar Scaling Action card) with the new mode-driven layout. The two cards continue to use the same `Border` chrome but the inner content becomes five sub-cards driven by `ActiveMode` `DataTrigger`s. Add a new `Style` resource for the mode-button row at the top of the file's resource section.

### 9.2 C#

- `VrcTwitchOscBridge\Models\AvatarScaleRule.cs` — add `GlitchyTransitionSeconds`, `ActiveMode`, `MultiplierDirection`, `MultiplierDirectionId`. Update the `Uses*` computed booleans as needed for the new property. Add the clamp logic in the Min/Max setters. Add the safe-bound tooltip text.
- `VrcTwitchOscBridge\Converters.cs` — add `ScalePreviewConverter : IMultiValueConverter`. Switches on mode name in `Convert`.
- `VrcTwitchOscBridge\ViewModels\MainWindowViewModel.cs` — extend `AvatarScaleModeOption` (or add a new field) so the `Mode` segmented row can bind to enum values directly. No other VM changes expected.
- The runtime scale-execution path inside `VrcTwitchOscBridge` (the file that reads `AvatarScaleRule` and writes `/avatar/eyeheight`) is updated so that, for `Relative` and `Multiplier` modes, it uses the rule's Min/Max as the true bounds and ignores the world radial. For `Multiplier` with `Direction = Divide`, it computes `eyeheight / value` instead of `eyeheight * value`.

### 9.3 Localization

- `VrcTwitchOscBridge\Resources\Localization\en-US.json` and `en-US.extra.json` — add the new keys from §8.
- All other `*.extra.json` files in the same folder — add matching translations. Run the `LocalizationAudit` project after to verify.

## 10. Acceptance Criteria

- Selecting each Mode reveals only the relevant value sub-card and updates the preview line.
- Typing a Min or Max value outside `[0.20, 100]` (Advanced off) snaps the value back into the bound on focus loss and shows the bound in a tooltip.
- Toggling Advanced widens the bound to `[0.01, 10000]` and updates the helper line.
- Setting both Min and Max in `Relative` or `Multiplier` mode causes the runtime to ignore the world's 5m radial cap. Unsetting either Min or Max falls back to the world cap. No UI element is required.
- The `× / ÷` button in Multiplier mode flips the runtime behavior between `eyeheight * value` and `eyeheight / value`. The preview line reflects the current operator.
- The `Glitchy Transition` field exists only when Mode is `Glitchy`, defaults to `0.4`, and is a separate persisted property from `SmoothTransitionSeconds`.
- The nested "Avatar Scaling Action" panel inside Power-Up and Cash Payment rules uses the same five sub-card layout (compact variant: Mode row, Value, Behavior, Advanced — Range limits only when needed).
- All new UI text is present in `en-US.json` / `en-US.extra.json` and in every other language's `*.extra.json`. `LocalizationAudit` passes.
- `dotnet build "E:\!!!Program to work on\Proper Crystal Relay\VrcTwitchOscBridge\VrcTwitchOscBridge.csproj" --no-restore` succeeds.
- No secrets, tokens, runtime state, or user-local paths are added to the repo.
- `AGENTS.md` and `CHANGELOG.txt` are updated as part of the release prep when this work is promoted.

## 11. Out of Scope

- Reworking the "Avatar Sets" tab layout.
- New theme styles — the redesign reuses existing brushes and styles.
- Any change to the Twitch reward sync behavior, Bits/Subs override, Cash Payment, Power-Up Redeem Library, or Universal Trigger subsystems.
- Any change to the `SafeMinimumHeightMeters` / `SafeMaximumHeightMeters` constants; the redesign's `0.20m` lower bound is a deliberate UX choice and replaces the existing `0.1m` lower bound in the visible UI. The model constant stays at `0.1m` for backward compatibility; the UI only enforces `0.20m` when Advanced is off.
