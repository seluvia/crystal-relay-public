# Scale Preview Text v2 Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Update the ScalePreviewConverter to always show the user's current avatar height as the first sentence across all 6 scale modes.

**Architecture:** Single-file change to `Converters.cs` — update the `ScalePreviewConverter.Convert` method to extract the current height from `values[6]` and prefix all mode outputs with "Your current height is Xm." No XAML or ViewModel changes needed.

**Tech Stack:** C# WPF, IMultiValueConverter

---

## Task 1: Update ScalePreviewConverter to prefix current height

**Files:**
- Modify: `E:\!!!Program to work on\Proper Crystal Relay\VrcTwitchOscBridge\Converters.cs:76-122`

**Current state of the converter (for reference):**

```csharp
// values[0] = ActiveMode (AvatarScaleMode enum)
// values[1] = TargetHeightMeters
// values[2] = MinimumHeightMeters
// values[3] = MaximumHeightMeters
// values[4] = GlitchyTransitionSeconds
// values[5] = RelativeHeightMeters
// values[6] = DataContext.CurrentAvatarHeightMeters (from ViewModel, live OSC height)
// values[7] = HeightMultiplier
// values[8] = IsDivideDirection
// values[9] = Preset (string label)
// values[10] = PresetHeight
```

- [ ] **Step 1: Replace the Convert method body**

Replace the entire `Convert` method (lines 76-123) with the updated version:

```csharp
public object Convert(object[] values, Type targetType, object parameter, CultureInfo culture)
{
    if (values is null || values.Length == 0)
    {
        return "\u2014";
    }

    string? mode = values[0] switch
    {
        AvatarScaleMode modeEnum => modeEnum.ToString(),
        string modeStr => modeStr,
        _ => null
    };

    if (mode is null)
    {
        return "\u2014";
    }

    // Extract current height with 1.6 fallback
    TryGetDouble(values[6], out var cu);
    if (cu <= 0) cu = 1.6;
    var currentPrefix = string.Format(culture, "Your current height is {0:0.##}m. ", cu);

    return mode switch
    {
        "SetHeight" => values.Length >= 2 && TryGetDouble(values[1], out var h)
            ? string.Format(culture, "{0}Sets avatar height to {1:0.##}m.", currentPrefix, h)
            : string.Format(culture, "{0}Sets avatar height directly.", currentPrefix),
        "RandomHeight" or "GlitchyRandomHeight" =>
            values.Length >= 3 && TryGetDouble(values[1], out var lo) && TryGetDouble(values[2], out var hi)
                ? string.Format(culture, mode == "GlitchyRandomHeight"
                    ? "{0}Rapidly rolls random heights between {1:0.##}m and {2:0.##}m with a {3:0.##}s transition between each jump, until Active Time ends."
                    : "{0}Each trigger rolls a random height between {1:0.##}m and {2:0.##}m.", currentPrefix, lo, hi,
                    values.Length >= 4 ? values[3] : null)
                : "\u2014",
        "RelativeHeight" =>
            values.Length >= 6 && TryGetDouble(values[4], out var ch) && TryGetDouble(values[5], out var mcu)
                ? string.Format(culture, "{0}Adds {1:+0.##;-0.##;0}m, changing height to {2:0.##}m.", currentPrefix, ch, mcu + ch)
                : "\u2014",
        "Multiplier" =>
            values.Length >= 8 && TryGetDouble(values[7], out var mul) && TryGetDouble(values[5], out var mulCu) && values[8] is bool divide
                ? string.Format(culture, "{0}Multiplies height by {1}{2:0.##}, changing to {3:0.##}m.",
                    currentPrefix, divide ? "\u00F7" : "\u00D7", mul, divide && mul != 0 ? mulCu / mul : mulCu * mul)
                : "\u2014",
        "Preset" =>
            values.Length >= 10 && values[9] is string label && TryGetDouble(values[10], out var ph)
                ? string.Format(culture, "{0}Sets avatar height to {1} preset ({2:0.##}m).", currentPrefix, label, ph)
                : "\u2014",
        _ => "\u2014"
    };
}
```

- [ ] **Step 2: Build verification**

Run: `dotnet build "E:\!!!Program to work on\Proper Crystal Relay\VrcTwitchOscBridge\VrcTwitchOscBridge.csproj" --no-restore`

Expected: Build succeeded. 0 Warning(s). 0 Error(s).

- [ ] **Step 3: Commit**

```bash
git add VrcTwitchOscBridge/Converters.cs
git commit -m "fix: show current height in all scale preview modes"
```
