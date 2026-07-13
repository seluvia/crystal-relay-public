# Avatar Swap Return Modes

## Overview

Add two per-rule avatar swap options — **Permanent Avatar Change** and **Return to Previous Avatar** — as a single 3-way choice in the rule editor. These are mutually exclusive with the existing default (Return to Global Return Avatar).

## Data Model

### TriggerRule (Model)

- `PermanentAvatarChange` (bool) — already exists at `TriggerRule.cs:1091`
- `ReturnToPreviousAvatar` (bool) — new field

The two bools encode three states:

| PermanentAvatarChange | ReturnToPreviousAvatar | Mode |
|---|---|---|
| `false` | `false` | Return to Global Return Avatar (default) |
| `false` | `true` | Return to Previous Avatar |
| `true` | `false` | Permanent (No Return) |
| `true` | `true` | Invalid — prevented by UI |

Both bools are serialized through the existing JSON flow (already handled for `PermanentAvatarChange`, add for `ReturnToPreviousAvatar`).

### Clone method

Add `ReturnToPreviousAvatar = r.ReturnToPreviousAvatar` to `TriggerRule.Clone()`.

## UI (AvatarSwapRuleEditorControl.xaml)

Place a 3-option radio group after the Action Type badge (~line 1043), before the Active Time section:

```
↩ Return Behavior
  ○ Return to Global Return Avatar  (default)
  ○ Return to Previous Avatar
  ○ Permanent (No Return)
```

- **Permanent** selected → Active Time field hides (no timer needed, duration forced to 0)
- **Return to Previous** selected → Active Time shown; return avatar picker replaced by notice: "Will return to the avatar you were wearing before this swap"
- **Return to Global** selected → Active Time shown; existing behavior unchanged

### AvatarSwapRuleEditorViewModel

Add:
- `IsReturnToPrevious` (bool) — maps to `ReturnToPreviousAvatar`
- Computed properties `IsReturnToGlobal`, `IsReturnToPrevious`, `IsPermanent` for radio button bindings
- Save: `Rule.ReturnToPreviousAvatar = IsReturnToPrevious`, `Rule.PermanentAvatarChange = IsPermanent`
- Load: read both bools, set the corresponding computed state
- Mutual exclusion enforced in setters

## Runtime — BridgeCoordinator

### Execution entry point (`ExecuteRuleActionAsync`)

Before processing, read the resolved mode from the rule's bool pair.

#### Permanent Mode

1. Force `DurationSeconds = 0` (suppress timer scheduling)
2. Send avatar change OSC packet (normal flow)
3. Do **not** call `SetSharedReturnAvatar` — the target avatar does not become the new global return point
4. Register the rule's ID in a `PermanentChangeCompletedRules` tracker (see Reward Visibility below)

#### Return to Previous Mode

1. Capture `currentVrChatAvatarId` **before** sending the switch
2. Override the `capturedReturnAvatar` / `avatarResetId` with this captured ID (instead of `currentSharedReturnAvatarId`)
3. Send avatar change to the target avatar
4. Schedule timer normally (`DurationSeconds` must be > 0)
5. On timer expiry: `ResetRuleEffectAsync` sends the reset packet to the stamped previous avatar ID
6. Do **not** update `SetSharedReturnAvatar`

#### Return to Global Mode

Existing behavior — no changes. Uses `currentSharedReturnAvatarId` as the return target.

### Reward Visibility

Add a runtime-only set in `BridgeCoordinator`:
- `HashSet<string> PermanentChangeCompletedRules` — holds rule IDs where `PermanentAvatarChange = true` has been activated

In `AvatarRuleActivationPolicy.IsRuleActiveForCurrentAvatar`:
- Add check: if the rule has `PermanentAvatarChange = true` and the rule's ID is in `PermanentChangeCompletedRules`, return `false` (hide the reward)

The set is:
- Populated when a permanent change rule activates
- Cleared when the user edits/re-saves the rule (editor save triggers removal)
- Cleared on app restart (clean slate — rewards reappear)

### Activity / Resume

`ReturnToPreviousAvatar` timed swaps need the stamped previous avatar ID to survive app restarts if the timer is still active. Extend `ActivityResumeService` to persist the previous avatar ID alongside the existing reset state.

## Files Changed

| File | Changes |
|------|---------|
| `Models/TriggerRule.cs` | Add `ReturnToPreviousAvatar` bool; update `Clone()` |
| `ViewModels/AvatarSwapRuleEditorViewModel.cs` | Add `IsReturnToPrevious`, computed radio properties, save/load for new field |
| `UserControls/AvatarSwapRuleEditorControl.xaml` | Add 3-option radio group |
| `Services/BridgeCoordinator.cs` | Handle Permanent and Return-to-Previous modes; add completion tracker |
| `Services/BridgeRuntimeConfiguration.cs` | Update `AvatarRuleActivationPolicy` for permanent completion check |
| `Services/ActivityResumeService.cs` | Persist previous avatar ID for Return-to-Previous swaps |

## Open Questions (None — resolved during brainstorming)

- [x] Permanent vs Return-to-Previous: mutually exclusive
- [x] Scope: per-rule checkboxes presented as a 3-way radio group
- [x] Permanent hides only its own reward; other swaps remain visible
- [x] App restart: permanent completion tracker is ephemeral (resets on restart)
