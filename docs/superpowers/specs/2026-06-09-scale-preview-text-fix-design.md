# Fix Scale Preview Text and Remove Current Textbox — Design

## Problem Statement

1. **"Current (meters)" textbox** appears in Relative and Multiplier modes. It's a manually-typed preview value (`MultCurrentPreview`, default 1.64) that is not connected to the actual avatar height. Users don't need it.

2. **Preview text is wrong** — The converter uses `MultCurrentPreview` (1.64) and `MinimumHeightMeters` (0.5) instead of the actual current height and change value. For Relative mode, it shows "Adds +2m to the current height, going from 0.5m to 2.5m" which is completely wrong.

3. **Preview text should use live OSC height** — The app already tracks the current avatar height via OSC (`/avatar/eyeheight`). The preview text should use this as the baseline.

## Design

### 1. Remove "Current (meters)" textbox from all modes

**File:** `MainWindow.xaml`

Remove the "Current (meters)" StackPanel from:
- Relative mode variant (main section, around lines 7086-7091)
- Multiplier mode variant (main section, around lines 7131-7135)
- Relative mode variant (Cash Payment section, around lines 8259-8263)
- Multiplier mode variant (Cash Payment section, around lines 8299-8303)

The `MultCurrentPreview` property on `AvatarScaleRule` stays (it may be used elsewhere) but is no longer shown in the UI.

### 2. Add live OSC height property to ViewModel

**File:** `ViewModels/MainWindowViewModel.cs`

Add a `CurrentAvatarHeightMeters` property that reads from `bridgeCoordinator.GetAvatarScaleRuntimeStatus().CurrentHeightMeters`:

```csharp
public double CurrentAvatarHeightMeters
{
    get
    {
        var status = bridgeCoordinator.GetAvatarScaleRuntimeStatus();
        return status.CurrentHeightMeters ?? 1.6;
    }
}
```

Raise `RaisePropertyChanged(nameof(CurrentAvatarHeightMeters))` in `HandleAvatarScaleStatusChanged()` (line ~9306) alongside the existing `AvatarScaleRuntimeStatusText` raise.

### 3. Update converter bindings

**File:** `MainWindow.xaml`

Update the MultiBinding in the live preview block to include `CurrentAvatarHeightMeters` from the ViewModel (using `RelativeSource AncestorType=Window`):

Replace the `MultCurrentPreview` binding with:
```xml
<Binding Path="DataContext.CurrentAvatarHeightMeters" RelativeSource="{RelativeSource AncestorType=Window}" />
```

### 4. Fix converter logic and indices

**File:** `Converters.cs`

The converter currently uses WRONG indices for Relative and Multiplier modes:
- **RelativeHeight** uses `values[1]` (TargetHeightMeters) as "change" and `values[2]` (MinimumHeightMeters) as "current" — WRONG
- **Multiplier** uses `values[1]` (TargetHeightMeters) as "multiplier" and `values[2]` (MinimumHeightMeters) as "current" — WRONG

Fix the converter to use the correct indices:
- **RelativeHeight**: Use `values[4]` (RelativeHeightMeters = change) and `values[5]` (CurrentAvatarHeightMeters = current)
- **Multiplier**: Use `values[6]` (HeightMultiplier = multiplier) and `values[5]` (CurrentAvatarHeightMeters = current)

### 5. Update preview text wording

**File:** `Converters.cs`

Update the format strings:
- **RelativeHeight**: "Adds {0:+0.##;-0.##;0}m to your height, going from {1:0.##}m to {2:0.##}m."
- **Multiplier (×)**: "Going from {0:0.##}m to {1:0.##}m using ×{2:0.##}."
- **Multiplier (÷)**: "Going from {0:0.##}m to {1:0.##}m using ÷{2:0.##}."

## Files To Modify

| File | Changes |
|------|---------|
| `ViewModels/MainWindowViewModel.cs` | Add `CurrentAvatarHeightMeters` property, raise on status change |
| `MainWindow.xaml` | Remove "Current (meters)" textbox from 4 locations, update converter bindings |
| `Converters.cs` | Update converter to use new binding index and fix preview text wording |

## Backward Compatibility

- `MultCurrentPreview` property stays on `AvatarScaleRule` (no model change)
- Only UI changes — no serialization or runtime changes needed
- The converter gets the current height from the ViewModel instead of the model
