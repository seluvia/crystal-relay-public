# Movement Direction: Add Missing VRChat OSC Inputs

## Summary
Add 4 missing VRChat OSC Input Controller axes to the `PlayerMovementDirection` enum so they appear in the movement redeem editor dropdown and work at runtime.

## Additions

| Value | OSC Address | Category | VR Only | Axis | Display Name |
|---|---|---|---|---|---|
| `Vertical` | `/input/Vertical` | Movement | No | Yes | "Move Vertical (Axis)" |
| `Horizontal` | `/input/Horizontal` | Movement | No | Yes | "Move Horizontal (Axis)" |
| `UseAxisRight` | `/input/UseAxisRight` | HandInteractions | Yes | Yes | "Use (Axis, Right Hand)" |
| `GrabAxisRight` | `/input/GrabAxisRight` | HandInteractions | Yes | Yes | "Grab (Axis, Right Hand)" |

## Files to Change
1. `PlayerMovementDirection.cs` — add enum members
2. `MovementTypeClassifier.cs` — classify in `GetCategory`, `IsVrOnly`, `IsAxisType`, `GetBehaviorTooltip`
3. `MovementRedeemCardViewModel.cs` — add display names in `GetDisplayName`
4. `BridgeCoordinator.cs` — add OSC addresses in `ResolvePlayerMovementAction` and `DescribeActionAddress`
