# Scale Preview Text v2 Design

## Problem

The Scale Preview text in the Avatar Scaling section does not consistently show the user's current avatar height across all modes. Currently, only RelativeHeight and Multiplier modes show current height. SetHeight, RandomHeight, GlitchyRandomHeight, and Preset modes omit it entirely.

## Goal

All 6 scale modes should show the user's current avatar height (from live OSC data) as the first piece of information, followed by what the action will do and the resulting height when applicable.

## Preview Text Format

All modes use a two-sentence format:

> "Your current height is {cu}m. {action description}."

Where `{cu}` is the live OSC-observed avatar height (fallback: 1.6m when unknown).

### Per-mode preview text

| Mode | Preview text |
|---|---|
| **SetHeight** | `"Your current height is {cu}m. Sets avatar height to {h}m."` |
| **RandomHeight** | `"Your current height is {cu}m. Each trigger rolls a random height between {lo}m and {hi}m."` |
| **GlitchyRandomHeight** | `"Your current height is {cu}m. Rapidly rolls random heights between {lo}m and {hi}m with a {t}s transition between each jump, until Active Time ends."` |
| **RelativeHeight** | `"Your current height is {cu}m. Adds {ch:+0.##;-0.##;0}m, changing height to {cu+ch}m."` |
| **Multiplier** | `"Your current height is {cu}m. Multiplies height by {op}{mul}m, changing to {result}m."` (op is × or ÷) |
| **Preset** | `"Your current height is {cu}m. Sets avatar height to {label} preset ({h}m)."` |

### Changes from current

- **All modes**: Add `"Your current height is {cu}m. "` prefix
- **RelativeHeight**: Drop "to your height, going from X to Y" → "changing height to Y"
- **Multiplier**: Drop "Going from X to Y using ×N" → "Multiplies height by ×N, changing to Y"
- **Preset**: Slight reword for consistency

## Implementation

### File: `Converters.cs` — `ScalePreviewConverter`

Update the `Convert` method to:

1. Extract `currentHeight` from `values[6]` with a 1.6 fallback
2. Build the `"Your current height is {cu}m. "` prefix
3. Append the mode-specific action description

No XAML changes are needed. The MultiBinding already passes `CurrentAvatarHeightMeters` at `values[6]`.

### Current height extraction

```csharp
TryGetDouble(values[6], out var cu);
if (cu <= 0) cu = 1.6;
```

### Prefix string

```csharp
var currentPrefix = string.Format(culture, "Your current height is {0:0.##}m. ", cu);
```

## Fallback Behavior

When no avatar has been observed yet (OSC height unknown), the preview falls back to 1.6m. This matches the existing behavior used elsewhere in the app (e.g., `GetAvatarScaleRuntimeStatus().CurrentHeightMeters` fallback).

## Scope

- Single file change: `Converters.cs`
- No XAML changes
- No ViewModel changes
- No model changes
- Localization not required (preview text is not user-facing UI text; it is a computed converter output)

## Verification

- Build: `dotnet build "VrcTwitchOscBridge.csproj" --no-restore` — 0 warnings, 0 errors
- Functional: Select each of the 6 scale modes and verify the preview text shows "Your current height is Xm." prefix followed by the correct action description
