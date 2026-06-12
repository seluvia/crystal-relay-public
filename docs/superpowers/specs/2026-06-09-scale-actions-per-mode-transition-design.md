# Scale Actions: Per-Mode Transition Seconds, Pre-text Fix, Theme Color Fix

## Problem Statement

Three issues in the Avatar Scaling Scale Actions UI:

1. **Missing transition seconds per mode**: Only Relative mode shows `SmoothTransitionSeconds` and Glitchy shows `GlitchyTransitionSeconds`. SetHeight, RandomHeight, Multiplier, and Preset have no transition field visible. All modes use the shared `SmoothTransitionSeconds` at runtime, but users cannot configure different transition times per mode.

2. **Pre-text shows "---"**: The `ScalePreviewConverter` receives an `AvatarScaleMode` enum from the `ActiveMode` binding, but checks `values[0] is not string mode` — the enum fails this check and returns the em-dash fallback `"ΓÇö"`.

3. **Hardcoded theme colors**: The live preview block uses `Background="#1a0f30"` and `BorderBrush="#4a2c7a"` instead of dynamic theme brushes, so it doesn't adapt to any theme.

## Design

### 1. Model Changes — Per-Mode Transition Seconds

**File:** `Models/AvatarScaleRule.cs`

Add 6 new per-mode fields:

| Field | Default | Clamp | Replaces |
|-------|---------|-------|----------|
| `SetHeightTransitionSeconds` | `0` | [0, 30] | — (new) |
| `RandomHeightTransitionSeconds` | `0` | [0, 30] | — (new) |
| `RelativeHeightTransitionSeconds` | `0` | [0, 30] | current `SmoothTransitionSeconds` usage |
| `MultiplierTransitionSeconds` | `0` | [0, 30] | — (new) |
| `PresetTransitionSeconds` | `0` | [0, 30] | — (new) |
| `GlitchyRandomHeightTransitionSeconds` | `0` | [0, 30] | replaces `GlitchyTransitionSeconds` |

All allow `0.00` as a valid value (no minimum, no auto-upgrade).

**`SmoothTransitionSeconds` becomes a computed alias:**

```csharp
public double SmoothTransitionSeconds
{
    get => ScaleMode switch
    {
        AvatarScaleMode.SetHeight => SetHeightTransitionSeconds,
        AvatarScaleMode.RandomHeight => RandomHeightTransitionSeconds,
        AvatarScaleMode.RelativeHeight => RelativeHeightTransitionSeconds,
        AvatarScaleMode.Multiplier => MultiplierTransitionSeconds,
        AvatarScaleMode.Preset => PresetTransitionSeconds,
        AvatarScaleMode.GlitchyRandomHeight => GlitchyRandomHeightTransitionSeconds,
        _ => 0
    };
}
```

This preserves all runtime call sites that read `rule.SmoothTransitionSeconds` — they now get the active mode's per-mode value automatically.

**Supporter Growth:** Add a dedicated `SupporterGrowthTransitionSeconds` field (default `0`, clamp [0, 30]) so Supporter Growth has its own independent transition setting. Update the Supporter Growth XAML binding from `SmoothTransitionSeconds` to `SupporterGrowthTransitionSeconds`.

**Remove:** `GlitchyTransitionSeconds` (replaced by `GlitchyRandomHeightTransitionSeconds`).

**`RaiseScaleProperties()` update:** Add `SupporterGrowthTransitionSeconds` to the set of properties raised. Each per-mode field setter calls `SetAndRaiseScale` which already raises `SmoothTransitionSeconds`.

### 2. Runtime Changes

**`Services/BridgeCoordinator.cs`:**
- All call sites reading `rule.SmoothTransitionSeconds` for standard scale actions need no change — the computed alias returns the per-mode value.
- `GetAvatarScaleEffectDurationSeconds`: for Glitchy mode, use `GlitchyRandomHeightTransitionSeconds` instead of `GlitchyTransitionSeconds`. For other modes, `SmoothTransitionSeconds` (computed) handles it.
- Restore sequences: `RestoreSmoothTransitionSeconds` still comes from `SmoothTransitionSeconds` (computed).
- Supporter Growth paths: change from `rule.SmoothTransitionSeconds` to `rule.SupporterGrowthTransitionSeconds`.

**`Services/BridgeRuntimeConfiguration.cs`:**
- `AvatarScaleRuleSnapshot` record: replace `SmoothTransitionSeconds` and `GlitchyTransitionSeconds` with the 6 per-mode fields plus `SupporterGrowthTransitionSeconds`.
- Snapshot factory: copy each per-mode field with clamping.
- All runtime code reading the snapshot transitions fields: update to use per-mode fields or the computed alias pattern.

**`ViewModels/MainWindowViewModel.cs`:**
- `GetAvatarScaleEffectDurationSeconds`: mirror the same change as BridgeCoordinator.
- `CreateDefaultScaleAction`: set all 6 per-mode fields to `0`.

### 3. Serialization Changes

**`Services/SettingsStore.cs`:**

DTO changes:
- Add 6 new `double` properties (defaults all `0`): `SetHeightTransitionSeconds`, `RandomHeightTransitionSeconds`, `RelativeHeightTransitionSeconds`, `MultiplierTransitionSeconds`, `PresetTransitionSeconds`, `GlitchyRandomHeightTransitionSeconds`.
- Add `SupporterGrowthTransitionSeconds` (default `0`).
- Keep `SmoothTransitionSeconds` and `GlitchyTransitionSeconds` in the DTO for backward-compatible deserialization only. Do not write them on serialization.

Serialization (model → DTO):
- Copy each per-mode field directly.

Deserialization (DTO → model):
- If new fields are present (non-zero or explicitly saved), use them directly.
- Backward compatibility: if new fields are all `0` and old `SmoothTransitionSeconds` is non-zero, migrate old value to `RelativeHeightTransitionSeconds` (the mode that previously used it in the UI).
- `GlitchyRandomHeightTransitionSeconds`: if `0` and old `GlitchyTransitionSeconds` is non-zero, migrate old value.

### 4. XAML UI Changes

**File:** `MainWindow.xaml`

**Main Avatar Scaling section (lines ~6972-7160):**

Each mode's value sub-card gets a "Transition Seconds" textbox at the bottom of its section:

- **SetHeight variant** (after Target Height Meters textbox):
  ```xml
  <TextBlock Text="{loc:Translate 'Transition Seconds'}" ... />
  <TextBox Text="{Binding SetHeightTransitionSeconds, UpdateSourceTrigger=LostFocus}" />
  ```

- **Random/Glitchy variant** (after min/max height, replacing the Glitchy-only transition field):
  - Remove the Glitchy-only conditional visibility block for `GlitchyTransitionSeconds`.
  - Add a "Transition Seconds" textbox visible for all Random/Glitchy modes:
    ```xml
    <TextBlock Text="{loc:Translate 'Transition Seconds'}" ... />
    <TextBox Text="{Binding RandomHeightTransitionSeconds, UpdateSourceTrigger=LostFocus}" />
    ```
  - Add a separate Glitchy-specific "Transition Seconds" textbox:
    ```xml
    <TextBlock Text="{loc:Translate 'Glitchy Transition Seconds'}" ... />
    <TextBox Text="{Binding GlitchyRandomHeightTransitionSeconds, UpdateSourceTrigger=LostFocus}" />
    ```

  Since Random and Glitchy share the same XAML variant (`UsesRandomHeightRange`), use visibility triggers to show the correct field:
  - When `UsesGlitchyRandomHeight` is true: show `GlitchyRandomHeightTransitionSeconds`
  - When `UsesRandomHeightRange` is true AND `UsesGlitchyRandomHeight` is false: show `RandomHeightTransitionSeconds`

- **Relative variant** (after Change/Current fields):
  - Rename binding from `SmoothTransitionSeconds` to `RelativeHeightTransitionSeconds`.

- **Multiplier variant** (after Value/Current fields):
  ```xml
  <TextBlock Text="{loc:Translate 'Transition Seconds'}" ... />
  <TextBox Text="{Binding MultiplierTransitionSeconds, UpdateSourceTrigger=LostFocus}" />
  ```

- **Preset variant** (after Preset ComboBox):
  ```xml
  <TextBlock Text="{loc:Translate 'Transition Seconds'}" ... />
  <TextBox Text="{Binding PresetTransitionSeconds, UpdateSourceTrigger=LostFocus}" />
  ```

**Cash Payment embedded scale action (lines ~8113-8272):**
Same per-mode transition fields as above, bound to the same properties.

**Supporter Growth section (line ~6685-6688):**
Change binding from `SmoothTransitionSeconds` to `SupporterGrowthTransitionSeconds`.

### 5. Converter Fix (Pre-text "---")

**File:** `Converters.cs`

**`ScalePreviewConverter.Convert`:**
- Before the `mode switch`, check if `values[0]` is an `AvatarScaleMode` enum and convert to string:
  ```csharp
  if (values[0] is AvatarScaleMode modeEnum)
      mode = modeEnum.ToString();
  else if (values[0] is string modeStr)
      mode = modeStr;
  else
      return "—";
  ```
- Use a proper em-dash `—` (U+2014) instead of the misencoded `"ΓÇö"`.
- Update the MultiBinding in XAML to pass the per-mode transition seconds for each mode's preview text (e.g., for SetHeight, pass `SetHeightTransitionSeconds`; for Random, pass `RandomHeightTransitionSeconds`).

**XAML MultiBinding update:**
The current binding passes `GlitchyTransitionSeconds` as values[3]. Update to pass the appropriate per-mode field. Since the converter needs to know which field to use based on the mode, the simplest approach is to pass all 6 per-mode fields and let the converter pick the right one based on the mode.

### 6. Theme Color Fix

**File:** `MainWindow.xaml`

**Live preview block (lines 7138-7141):**
Replace hardcoded colors with dynamic theme brushes:
```xml
<!-- Before -->
Background="#1a0f30"
BorderBrush="#4a2c7a"

<!-- After -->
Background="{DynamicResource NestedPanelBrush}"
BorderBrush="{DynamicResource HighlightBorderBrush}"
```

This applies to the main Avatar Scaling section's live preview block. The Cash Payment section does not have a live preview block, so no change needed there.

### 7. Localization

Add new `en-US` localization keys:
- `"Transition Seconds"` — used for all modes except Glitchy
- `"Glitchy Transition Seconds"` — already exists
- `"Supporter Growth Transition Seconds"` — for the Supporter Growth section

Run the localization audit after changes.

## Files To Modify

| File | Changes |
|------|---------|
| `Models/AvatarScaleRule.cs` | Add 7 new fields, make `SmoothTransitionSeconds` computed, remove `GlitchyTransitionSeconds` |
| `Services/SettingsStore.cs` | Update DTO, serialization, deserialization with backward compat |
| `Services/BridgeCoordinator.cs` | Update `GetAvatarScaleEffectDurationSeconds`, Supporter Growth paths |
| `Services/BridgeRuntimeConfiguration.cs` | Update snapshot record and factory |
| `ViewModels/MainWindowViewModel.cs` | Update `GetAvatarScaleEffectDurationSeconds`, `CreateDefaultScaleAction` |
| `MainWindow.xaml` | Per-mode transition fields, theme color fix, preview binding update |
| `Converters.cs` | Fix enum handling, proper em-dash, per-mode transition text |

## Backward Compatibility

- Old saves with `SmoothTransitionSeconds` migrate to `RelativeHeightTransitionSeconds`.
- Old saves with `GlitchyTransitionSeconds` migrate to `GlitchyRandomHeightTransitionSeconds`.
- The computed `SmoothTransitionSeconds` alias preserves all runtime call sites.
- New saves use the 6 per-mode fields; old fields are no longer written.

## Testing

- Build the app project: `dotnet build "VrcTwitchOscBridge\VrcTwitchOscBridge.csproj" --no-restore`
- Run localization audit
- Verify each mode shows its own transition seconds field
- Verify the live preview text works for all modes (no "---")
- Verify the live preview block adapts to different themes
- Verify old saves load correctly with migration
