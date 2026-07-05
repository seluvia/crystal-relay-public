# Movement Redeems Redesign — v3.2.0

**Date:** 2026-07-05
**Status:** Design approved, pending implementation
**Product:** Crystal Relay
**Version target:** v3.2.0

## Overview

Replace the current inline Movement Redeems UI (embedded in `MainWindow.xaml`) with a dedicated manager window matching the modern pattern used by Avatar Scaling, Universal Triggers, and Avatar Sets. Expand the movement action catalog to cover the full VRChat OSC Input Controller surface (~26 input types across 5 categories).

## Current State (v3.1.9)

- Movement Redeems UI is inline in `MainWindow.xaml` (lines ~3910–6915)
- No separate ViewModel — logic lives in `MainWindowViewModel.cs`
- Uses shared `AvatarSwapRuleEditorControl` for rule editing
- 12 `PlayerMovementDirection` enum values
- 3 hidden Stop actions (internal only, used by stop-input system)
- `TriggerRule` has `MovementDirection`, `DurationSeconds`, `CooldownSeconds`
- `BridgeCoordinator.ResolvePlayerMovementAction()` executes movement OSC sends

## Design

### Architecture

- **New file:** `MovementRedeemsManagerWindow.xaml` + `MovementRedeemsManagerWindow.xaml.cs`
- **New file:** `MovementRedeemsManagerViewModel.cs` (dedicated ViewModel)
- **Pattern:** Dedicated themed window (WindowStyle="None" + WindowChrome), two-pane layout
  - Left pane: card list with category tabs and toolbar
  - Right pane: slide-in editor overlay (480px wide) with backdrop click-to-close
- **Nav wiring:** "Movement Redeems" button in MainWindow opens the new window instead of toggling inline workspace

### Tab Structure (5 category tabs)

| Tab | Types | VR-only notes |
|-----|-------|---------------|
| Movement (8) | Forward, Backward, Left, Right, Jump, Run, Random Movement, Glitchy Movement | None |
| Turning (5) | LookHorizontal (axis), LookLeft, LookRight, ComfortLeft, ComfortRight | ComfortLeft/Right: VR-only (always snap) |
| Hand Interactions (6) | GrabLeft, GrabRight, UseLeft, UseRight, DropLeft, DropRight | All VR-only |
| Held Object (4) | MoveHoldFB, SpinHoldCwCcw, SpinHoldUD, SpinHoldLR | None |
| UI Toggles (4) | QuickMenuToggleLeft, QuickMenuToggleRight, PanicButton, Voice | None |

Each tab shows rules from that category only. Tab badge shows count (e.g., "Turning (3)"). VR-only items display a `[VR]` badge/pill.

### Card Layout

Each movement rule appears as a themed card with:
- **Category pill** (color-coded per tab)
- **Name** — human-readable movement name
- **VR badge** — only on VR-only inputs (with tooltip explaining desktop behavior)
- **Enable/disable toggle** — right side of card header
- **Trigger source pills** — which triggers are wired (Channel Points, Chat Command, Bits, etc.)
- **Duration + Cooldown** — quick stats
- **Behavior tooltip** — for turning inputs, explains snap/smooth behavior differences
- **Action buttons** — Test | Edit | Delete

### Slide-In Editor (Dynamic Fields)

Editor appears as right-side overlay (480px). Fields change based on selected movement type:

**Common fields (always present):**
- Rule Name (text input)
- Movement Type (dropdown — changing it reshows type-specific fields)
- Duration (number input, min 1)
- Cooldown (number input, min 0)
- Trigger Sources (chip-style toggle buttons)

**Type-specific additional fields:**
- Basic Movement / Turning / Hand Interactions / UI Toggles: no extra fields
- Held Object: Speed (number input, 0.1–1.0)

**Info boxes (contextual):**
- VR-only inputs: red-tinted box "VR-only input. No effect on Desktop."
- Turning inputs: neutral tooltip "Smooth on Desktop. Snap in VR if Comfort Turning is ON."
- Held Object: green info "Controls held objects. Axis value = speed."
- UI Toggles: neutral "Triggers UI action. Duration = hold time before reset."

**Save/Cancel buttons** at the bottom of the editor.

### Test Button

Each card has a Test button that sends the OSC command immediately. Auto-stops after a short safety timer (configurable, default 2s), sending the reset value (`0` or `0.0`).

### Model Changes

**`PlayerMovementDirection` enum** — expand from 12 to ~26 values:

Existing values keep their integer positions (backward compatible). New values appended at end:

```
[Existing 0-11: Forward, Backward, Left, Right, Jump, SpinLeft, SpinRight, 
 StopMovement, StopTurning, StopAll, RandomMovement, GlitchyMovement]

New values:
 Run             = 12
 LookHorizontal  = 13
 LookLeft        = 14
 LookRight       = 15
 ComfortLeft     = 16
 ComfortRight    = 17
 GrabLeft        = 18
 GrabRight       = 19
 UseLeft         = 20
 UseRight        = 21
 DropLeft        = 22
 DropRight       = 23
 MoveHoldFB      = 24
 SpinHoldCwCcw   = 25
 SpinHoldUD      = 26
 SpinHoldLR      = 27
 QuickMenuToggleLeft  = 28
 QuickMenuToggleRight = 29
 PanicButton     = 30
 Voice           = 31
```

**`TriggerRule` model:**
- No new properties needed for most types
- Add optional `FloatValue` property (0.0–1.0, nullable float) for axis speed control (Held Object)

### Behavior Execution (BridgeCoordinator)

`ResolvePlayerMovementAction()` expanded to handle all 26 types:

- **Button types** (Jump, MoveForward, LookLeft, ComfortLeft, GrabLeft, etc.): send `1` on start, send `0` on stop (or on duration expiry)
- **Axis types** (LookHorizontal, MoveHoldFB, SpinHoldCwCcw, etc.): send configured float value (default 1.0, or Speed for Held Object) on start, send `0.0` on stop
- **Run:** sends `/input/Run 1` on start, `0` on stop
- **UI Toggles** (QuickMenuToggle, PanicButton, Voice): send `1` then immediately `0` after a brief delay
- VR-only inputs still send OSC — VRChat ignores them on Desktop

### Category Classification Helper

Add a helper method (on ViewModel or a static utility) to classify each `PlayerMovementDirection` into the 5 categories. Used for tab filtering in the window.

### Stop Actions

StopMovement, StopTurning, StopAll remain hidden/internal. Not exposed as user-selectable actions. Used only by the existing stop-input system.

### Migration Path

- All existing `PlayerMovementDirection` enum values retain their integer positions
- Existing `MovementRedeemSet` saved data (JSON) loads as-is — old rules remain valid
- The new enum values are appended (no reordering of existing values)
- `IsSupportedMovementDirection()` updated to include all new types (and stop actions remain excluded)

### Files Changed / Added

**New files:**
- `VrcTwitchOscBridge\MovementRedeemsManagerWindow.xaml`
- `VrcTwitchOscBridge\MovementRedeemsManagerWindow.xaml.cs`
- `VrcTwitchOscBridge\ViewModels\MovementRedeemsManagerViewModel.cs`

**Modified files:**
- `VrcTwitchOscBridge\Models\PlayerMovementDirection.cs` — expand enum
- `VrcTwitchOscBridge\Models\TriggerRule.cs` — add optional `FloatValue`
- `VrcTwitchOscBridge\ViewModels\MainWindowViewModel.cs` — rewire nav command, remove old inline logic
- `VrcTwitchOscBridge\MainWindow.xaml` — remove Movement Redeems inline section
- `VrcTwitchOscBridge\Services\BridgeCoordinator.cs` — expand `ResolvePlayerMovementAction()`
- `VrcTwitchOscBridge\VrcTwitchOscBridge.csproj` — add new files


### Out of Scope

- Movement Sets behavioral changes (remain organizational folders)
- Stop actions as user-facing redeems
- Auto-detecting VR mode to filter inputs
- Adding a Comfort Turning setting toggle in the app
