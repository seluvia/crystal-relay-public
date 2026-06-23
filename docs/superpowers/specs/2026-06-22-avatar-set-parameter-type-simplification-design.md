# Avatar Set Parameter Type Simplification Design

## Goal

Reduce confusion in the Avatar Set rule editor by removing the redundant `Value Type` dropdown from the main parameter-editing flow. Users should find a parameter through search and type filters, pick it, and let Crystal Relay configure the correct parameter type automatically.

## Approved Direction

Use the parameter picker as the source of truth for parameter type.

- Keep `Search & Pick Parameter` as the primary UI.
- Keep `All`, `Bool`, `Int`, and `Float` as parameter-list filters only.
- Remove the visible `Value Type` dropdown from the Avatar Set rule editor.
- When a user clicks a parameter in the picker, copy both the parameter name and the OSC parameter type onto the selected rule.
- The existing type-specific editor sections continue to appear from the saved rule type: bool value chips, int controls, and float action mode controls.

## Current Behavior

`AvatarSetsManagerWindow.xaml` currently shows both `Value Type` and `Parameter List Filter`. This makes the user choose between two controls that appear to describe the same concept. The picker click handler already assigns both `ParameterName` and `ParameterType`, so the dropdown duplicates the normal path.

Manual text entry into `Parameter Name (selected)` is still allowed, but it only edits the parameter name. It does not infer or change `ParameterType`.

## UI Behavior

The main UI change is removal of the visible `Value Type` dropdown. The parameter picker remains the obvious path for normal users:

1. `Search & Pick Parameter` keeps text search, refresh, and `All / Bool / Int / Float` filter buttons.
2. `Parameter Name (selected)` remains available for the selected or manually typed parameter name.
3. Type-specific controls remain driven by the rule's current saved type.

The `All / Bool / Int / Float` buttons only narrow the list. They do not change the rule type by themselves.

## Data Flow

When the user picks a parameter from the list:

1. The selected `VrChatOscParameterSummary` supplies the parameter name.
2. The same summary supplies the parameter type.
3. `TriggerRule.ParameterName` is updated.
4. `TriggerRule.ParameterType` is updated.
5. Existing `TriggerRule` property-change notifications refresh the visible type-specific controls.
6. The search filter clears after selection, matching current behavior.

When the user manually types a parameter name:

- Only `TriggerRule.ParameterName` changes.
- The existing rule type remains unchanged.
- Users should prefer the picker when the parameter appears in the avatar OSC cache.

## Scope

In scope:

- Avatar Set rule editor only.
- Remove the visible `Value Type` control from the selected-rule editing panel.
- Preserve the parameter picker and type filters.
- Preserve existing saved rule data and runtime behavior.

Out of scope:

- Changing wardrobe parameter picker behavior.
- Adding a new manual/custom parameter workflow.
- Changing persisted model fields or migration behavior.
- Changing OSC dispatch behavior.

## Error Handling

No new error path is required. Existing behavior remains:

- If no avatar parameters are loaded, the picker list is empty until refresh/load succeeds.
- If the user manually types a parameter that is missing from the picker, the app uses the rule's existing saved type.
- If an existing saved rule has a type, that type continues to drive the visible controls.

## Testing

Update or add tests around the Avatar Set manager XAML to verify:

- The `Value Type` label/control is no longer present in the Avatar Set rule editor.
- The `Parameter List Filter` buttons remain present.
- The picker binding to `FilteredParameters` remains present.
- Type-specific sections still bind to `UsesBoolParameter`, `UsesIntParameter`, and `UsesFloatActionMode`.

Run the app project build after implementation:

```powershell
dotnet build "E:\!!!Program to work on\Proper Crystal Relay\VrcTwitchOscBridge\VrcTwitchOscBridge.csproj" --no-restore
```
