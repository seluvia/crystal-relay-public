# Scale Action UI Redesign — Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Replace the current flat Scale Action panel with a mode-driven, grouped UI that includes silent VRChat-bypass behavior, clamped Min/Max, a Multiplier `× / ÷` toggle, a per-mode live preview, and a new Glitchy per-jump transition field.

**Architecture:** All changes are localized to the Scale Action section in `MainWindow.xaml` (two XAML blocks), the `AvatarScaleRule` model, a new `ScalePreviewConverter`, the snapshot in `BridgeRuntimeConfiguration`, the executor in `BridgeCoordinator`, the persistence in `SettingsStore`, and the localization `.json` files. No new architectural patterns; we follow the existing `Style` + `DataTrigger` visibility pattern and the existing `ObservableObject` property pattern.

**Tech Stack:** C# / WPF / .NET 10 / `loc:Translate` markup extension / `IMultiValueConverter`.

**Reference spec:** `docs/superpowers/specs/2026-06-08-scale-action-ui-redesign-design.md`

---

## File Map

**Create / modify:**

- `VrcTwitchOscBridge/Models/AvatarScaleRule.cs` — add `GlitchyTransitionSeconds`, `MultiplierDirection` enum + property, `ActiveMode` computed property, raise helper. Keep `BypassVrChatScaleLimits` and existing `SafeMinimumHeightMeters` (0.1) unchanged for backward compat; new UI bound is 0.20.
- `VrcTwitchOscBridge/Converters.cs` — add `ScalePreviewConverter : IMultiValueConverter` that switches on mode name and returns the formatted preview string.
- `VrcTwitchOscBridge/MainWindow.xaml` — replace lines 6848–7157 (primary panel) and lines 7853–7964 (nested Power-Up / Cash panel) with the new mode-driven layout.
- `VrcTwitchOscBridge/Services/BridgeRuntimeConfiguration.cs` — extend `AvatarScaleRuleSnapshot` (the runtime snapshot) with `MultiplierDirection` and `GlitchyTransitionSeconds`; populate in the snapshot factory.
- `VrcTwitchOscBridge/Services/BridgeCoordinator.cs` — update `ResolveAvatarScaleTargetHeight` and the `GlitchyRandomHeight` transition lookup to honor `MultiplierDirection` and the new per-jump transition field. Update the auto-bypass logic to silently ignore VRChat limits when Min/Max are set in `Relative` or `Multiplier` mode, regardless of `BypassVrChatScaleLimits`.
- `VrcTwitchOscBridge/Services/SettingsStore.cs` — round-trip the new `GlitchyTransitionSeconds` and `MultiplierDirection` fields through the persistence layer; keep `BypassVrChatScaleLimits` round-trip for backward compat.
- `VrcTwitchOscBridge/Resources/Localization/en-US.json` and `en-US.extra.json` — add the new keys.
- All other `*.extra.json` localization files — add matching translations (informal register, placeholders preserved).

**No changes to:**

- `VrcTwitchOscBridge.csproj` (no new dependencies).
- `VrcTwitchOscBridge.slnx`.
- `oscquery-lib` (no API surface change).
- Twitch reward sync, Bits/Subs override, Power-Up Redeem Library, Universal Triggers, Cash Payment systems.

---

## Task 1: Add `MultiplierDirection` enum and `MultiplierDirectionId` property to `AvatarScaleRule`

**Files:**
- Modify: `VrcTwitchOscBridge/Models/AvatarScaleRule.cs` (after the `AvatarScaleRestoreMode` enum, before the `AvatarScaleBitGrowthRange` class)

- [ ] **Step 1: Add the enum and the persisted property**

Insert after the `AvatarScaleRestoreMode` enum (around line 45) and add the private field + property near the other scale state (around line 320).

```csharp
public enum AvatarScaleMultiplierDirection
{
    Grow,
    Divide
}
```

Add a private field and a property in `AvatarScaleRule`:

```csharp
private int multiplierDirectionId;
public int MultiplierDirectionId
{
    get => multiplierDirectionId;
    set
    {
        var normalized = Enum.IsDefined((AvatarScaleMultiplierDirection)value)
            ? value
            : (int)AvatarScaleMultiplierDirection.Grow;
        if (SetAndRaiseScale(ref multiplierDirectionId, normalized))
        {
            RaisePropertyChanged(nameof(MultiplierDirection));
        }
    }
}

public AvatarScaleMultiplierDirection MultiplierDirection =>
    Enum.IsDefined((AvatarScaleMultiplierDirection)multiplierDirectionId)
        ? (AvatarScaleMultiplierDirection)multiplierDirectionId
        : AvatarScaleMultiplierDirection.Grow;
```

- [ ] **Step 2: Build**

Run: `dotnet build "E:\!!!Program to work on\Proper Crystal Relay\VrcTwitchOscBridge\VrcTwitchOscBridge.csproj" --no-restore`
Expected: `Build succeeded. 0 Warning(s) 0 Error(s)` (or warnings unrelated to this file).

- [ ] **Step 3: Commit**

```bash
git add "VrcTwitchOscBridge/Models/AvatarScaleRule.cs"
git commit -m "feat(scale): add MultiplierDirection enum and persisted MultiplierDirectionId"
```

---

## Task 2: Add `GlitchyTransitionSeconds` property to `AvatarScaleRule`

**Files:**
- Modify: `VrcTwitchOscBridge/Models/AvatarScaleRule.cs` (add the field near line 320, the property near `SmoothTransitionSeconds`)

- [ ] **Step 1: Add the field and property**

Add the private field alongside the other `double` scale state:

```csharp
private double glitchyTransitionSeconds = 0.4;
```

Add the property after `SmoothTransitionSeconds` (around line 651):

```csharp
public double GlitchyTransitionSeconds
{
    get => glitchyTransitionSeconds;
    set => SetAndRaiseScale(ref glitchyTransitionSeconds, Math.Clamp(value, 0, 5));
}
```

- [ ] **Step 2: Build**

Run: `dotnet build "E:\!!!Program to work on\Proper Crystal Relay\VrcTwitchOscBridge\VrcTwitchOscBridge.csproj" --no-restore`
Expected: Build succeeds.

- [ ] **Step 3: Commit**

```bash
git add "VrcTwitchOscBridge/Models/AvatarScaleRule.cs"
git commit -m "feat(scale): add GlitchyTransitionSeconds property"
```

---

## Task 3: Add `ActiveMode` computed property to `AvatarScaleRule`

**Files:**
- Modify: `VrcTwitchOscBridge/Models/AvatarScaleRule.cs` (add after `UsesConfiguredRestoreHeight` near line 846)

- [ ] **Step 1: Add the property**

```csharp
public AvatarScaleMode ActiveMode => ScaleMode;
```

- [ ] **Step 2: Add it to `RaiseScaleProperties()` so changes propagate**

Edit `RaiseScaleProperties()` (around line 1040) to include the new property:

```csharp
private void RaiseScaleProperties()
{
    RaisePropertyChanged(nameof(UsesTargetHeight));
    RaisePropertyChanged(nameof(UsesRandomHeight));
    RaisePropertyChanged(nameof(UsesGlitchyRandomHeight));
    RaisePropertyChanged(nameof(UsesRandomHeightRange));
    RaisePropertyChanged(nameof(UsesRelativeHeight));
    RaisePropertyChanged(nameof(UsesRelativeMinimumHeight));
    RaisePropertyChanged(nameof(UsesRelativeMaximumHeight));
    RaisePropertyChanged(nameof(UsesMultiplier));
    RaisePropertyChanged(nameof(UsesPreset));
    RaisePropertyChanged(nameof(HasActiveTime));
    RaisePropertyChanged(nameof(UsesConfiguredRestoreHeight));
    RaisePropertyChanged(nameof(ActiveMode));
    RaisePropertyChanged(nameof(ScaleSummary));
    RaisePropertyChanged(nameof(TriggerSummary));
}
```

- [ ] **Step 3: Build**

Run: `dotnet build "E:\!!!Program to work on\Proper Crystal Relay\VrcTwitchOscBridge\VrcTwitchOscBridge.csproj" --no-restore`
Expected: Build succeeds.

- [ ] **Step 4: Commit**

```bash
git add "VrcTwitchOscBridge/Models/AvatarScaleRule.cs"
git commit -m "feat(scale): expose ActiveMode computed property for XAML mode row"
```

---

## Task 4: Update the per-mode summary in `AvatarScaleRule.ScaleSummary`

**Files:**
- Modify: `VrcTwitchOscBridge/Models/AvatarScaleRule.cs` (the `ScaleSummary` getter around line 886)

- [ ] **Step 1: Extend the summary to include the direction**

```csharp
public string ScaleSummary => ScaleMode switch
{
    AvatarScaleMode.SetHeight => $"Set {TargetHeightMeters:0.##}m",
    AvatarScaleMode.RandomHeight => $"Random {Math.Min(MinimumHeightMeters, MaximumHeightMeters):0.##}-{Math.Max(MinimumHeightMeters, MaximumHeightMeters):0.##}m",
    AvatarScaleMode.GlitchyRandomHeight => $"Glitchy {Math.Min(MinimumHeightMeters, MaximumHeightMeters):0.##}-{Math.Max(MinimumHeightMeters, MaximumHeightMeters):0.##}m",
    AvatarScaleMode.RelativeHeight => $"{RelativeHeightMeters:+0.##;-0.##;0}m relative",
    AvatarScaleMode.Multiplier => MultiplierDirection == AvatarScaleMultiplierDirection.Divide
        ? $"÷{HeightMultiplier:0.##}"
        : $"x{HeightMultiplier:0.##}",
    AvatarScaleMode.Preset => Preset.ToString(),
    _ => "Scale"
};
```

- [ ] **Step 2: Build**

Run: `dotnet build "E:\!!!Program to work on\Proper Crystal Relay\VrcTwitchOscBridge\VrcTwitchOscBridge.csproj" --no-restore`
Expected: Build succeeds.

- [ ] **Step 3: Commit**

```bash
git add "VrcTwitchOscBridge/Models/AvatarScaleRule.cs"
git commit -m "feat(scale): include MultiplierDirection in ScaleSummary"
```

---

## Task 5: Add `ScalePreviewConverter` to `Converters.cs`

**Files:**
- Modify: `VrcTwitchOscBridge/Converters.cs` (append at the end of the file)

- [ ] **Step 1: Add the converter**

```csharp
public sealed class ScalePreviewConverter : IMultiValueConverter
{
    public object Convert(object[] values, Type targetType, object parameter, CultureInfo culture)
    {
        if (values is null || values.Length == 0 || values[0] is not string mode)
        {
            return "—";
        }

        return mode switch
        {
            "SetHeight" => values.Length >= 2 && TryGetDouble(values[1], out var h)
                ? string.Format(culture, "Sets the avatar height directly to {0:0.##}m.", h)
                : "Sets the avatar height directly.",
            "RandomHeight" or "GlitchyRandomHeight" =>
                values.Length >= 3 && TryGetDouble(values[1], out var lo) && TryGetDouble(values[2], out var hi)
                    ? string.Format(culture, mode == "GlitchyRandomHeight"
                        ? "Rapidly rolls random heights between {0:0.##}m and {1:0.##}m with a {2:0.##}s transition between each jump, until Active Time ends."
                        : "Each trigger rolls a random height between {0:0.##}m and {1:0.##}m.", lo, hi,
                        values.Length >= 4 ? values[3] : null)
                    : "—",
            "RelativeHeight" =>
                values.Length >= 3 && TryGetDouble(values[1], out var ch) && TryGetDouble(values[2], out var cu)
                    ? string.Format(culture, "Adds {0:+0.##;-0.##;0}m to the current height, going from {1:0.##}m to {2:0.##}m.", ch, cu, cu + ch)
                    : "—",
            "Multiplier" =>
                values.Length >= 4 && TryGetDouble(values[1], out var mul) && TryGetDouble(values[2], out var mcu) && values[3] is bool divide
                    ? string.Format(culture, divide
                        ? "Going from {0:0.##}m to {1:0.##}m using ÷{2:0.##}."
                        : "Going from {0:0.##}m to {1:0.##}m using ×{2:0.##}.", mcu, divide && mul != 0 ? mcu / mul : mcu * mul, mul)
                    : "—",
            "Preset" =>
                values.Length >= 3 && values[1] is string label && TryGetDouble(values[2], out var h2)
                    ? string.Format(culture, "Sets the avatar height to the {0} preset, which is {1:0.##}m.", label, h2)
                    : "—",
            _ => "—"
        };
    }

    public object[] ConvertBack(object value, Type[] targetTypes, object parameter, CultureInfo culture)
        => throw new NotSupportedException();

    private static bool TryGetDouble(object value, out double result)
    {
        result = 0;
        if (value is null) return false;
        try { result = System.Convert.ToDouble(value, CultureInfo.InvariantCulture); return true; }
        catch { return false; }
    }
}
```

- [ ] **Step 2: Build**

Run: `dotnet build "E:\!!!Program to work on\Proper Crystal Relay\VrcTwitchOscBridge\VrcTwitchOscBridge.csproj" --no-restore`
Expected: Build succeeds.

- [ ] **Step 3: Commit**

```bash
git add "VrcTwitchOscBridge/Converters.cs"
git commit -m "feat(scale): add ScalePreviewConverter for mode-driven preview strings"
```

---

## Task 6: Add the converter as a resource in `MainWindow.xaml`

**Files:**
- Modify: `VrcTwitchOscBridge/MainWindow.xaml` (find the `<Application.Resources>` or window resource section around line 30–50 where `BoolToVisibilityConverter` is declared)

- [ ] **Step 1: Locate the existing converter declaration**

Open the file and search for `BoolToVisibilityConverter`. The existing declaration looks like:

```xml
<BooleanToVisibilityConverter x:Key="BoolToVisibilityConverter" />
```

- [ ] **Step 2: Add the new converter immediately after it**

```xml
<converters:ScalePreviewConverter x:Key="ScalePreviewConverter" />
```

- [ ] **Step 3: Confirm the namespace**

Check that the file already imports the `VrcTwitchOscBridge` namespace at the top (`xmlns:converters="clr-namespace:VrcTwitchOscBridge"`). If it uses a different alias for converters, match the existing alias. If no `xmlns:converters` exists, add:

```xml
xmlns:converters="clr-namespace:VrcTwitchOscBridge"
```

to the root element.

- [ ] **Step 4: Build**

Run: `dotnet build "E:\!!!Program to work on\Proper Crystal Relay\VrcTwitchOscBridge\VrcTwitchOscBridge.csproj" --no-restore`
Expected: Build succeeds. (XAML resource registration does not require runtime validation at this step.)

- [ ] **Step 5: Commit**

```bash
git add "VrcTwitchOscBridge/MainWindow.xaml"
git commit -m "feat(scale): register ScalePreviewConverter in MainWindow resources"
```

---

## Task 7: Update the snapshot in `BridgeRuntimeConfiguration.cs` to carry the new fields

**Files:**
- Modify: `VrcTwitchOscBridge/Services/BridgeRuntimeConfiguration.cs` (around line 208 the `AvatarScaleRuleSnapshot` record, and around line 1072 the factory)

- [ ] **Step 1: Extend the snapshot record**

Find the `AvatarScaleRuleSnapshot` record (around line 208) and add two new fields. Match the existing ordering:

```csharp
public sealed record AvatarScaleRuleSnapshot(
    // ... existing fields ...
    double HeightMultiplier,
    int MultiplierDirectionId,
    double GlitchyTransitionSeconds,
    bool BypassVrChatScaleLimits,
    // ... rest ...
);
```

- [ ] **Step 2: Update every call site that constructs the snapshot**

There is exactly one call site (around line 1072 in the same file) that builds the snapshot. Add the two new arguments in the same order:

```csharp
Math.Clamp(rule.HeightMultiplier, 0.01, AvatarScaleRule.AdvancedMaximumHeightMeters),
(int)rule.MultiplierDirection,
Math.Clamp(rule.GlitchyTransitionSeconds, 0, 5),
rule.BypassVrChatScaleLimits,
```

- [ ] **Step 3: Build**

Run: `dotnet build "E:\!!!Program to work on\Proper Crystal Relay\VrcTwitchOscBridge\VrcTwitchOscBridge.csproj" --no-restore`
Expected: Build succeeds.

- [ ] **Step 4: Commit**

```bash
git add "VrcTwitchOscBridge/Services/BridgeRuntimeConfiguration.cs"
git commit -m "feat(scale): carry MultiplierDirection and GlitchyTransitionSeconds in runtime snapshot"
```

---

## Task 8: Honor `MultiplierDirection` in the runtime scale executor

**Files:**
- Modify: `VrcTwitchOscBridge/Services/BridgeCoordinator.cs` (line 5953, inside `ResolveAvatarScaleTargetHeight`)

- [ ] **Step 1: Update the `Multiplier` branch**

Replace the single line at line 5953 with:

```csharp
AvatarScaleMode.Multiplier => rule.MultiplierDirectionId == (int)AvatarScaleMultiplierDirection.Divide
    ? current / Math.Max(0.01, rule.HeightMultiplier)
    : current * Math.Max(0.01, rule.HeightMultiplier),
```

- [ ] **Step 2: Build**

Run: `dotnet build "E:\!!!Program to work on\Proper Crystal Relay\VrcTwitchOscBridge\VrcTwitchOscBridge.csproj" --no-restore`
Expected: Build succeeds.

- [ ] **Step 3: Commit**

```bash
git add "VrcTwitchOscBridge/Services/BridgeCoordinator.cs"
git commit -m "feat(scale): honor MultiplierDirection in avatar scale executor"
```

---

## Task 9: Use the new `GlitchyTransitionSeconds` in the runtime Glitchy path

**Files:**
- Modify: `VrcTwitchOscBridge/Services/BridgeCoordinator.cs` (line 13049 in `MainWindowViewModel.cs` and the equivalent in `BridgeCoordinator.cs` line 11861 — both call the same `GetAvatarScaleEffectDurationSeconds`-style helper)

- [ ] **Step 1: Find the existing Glitchy transition lookup**

There are two locations that compute the per-jump transition for Glitchy mode. Both currently use `SmoothTransitionSeconds` (with a special-case `=> 0` for Glitchy). Replace the `=> 0` with a per-rule value:

In `ViewModels/MainWindowViewModel.cs` (line 13049) and `Services/BridgeCoordinator.cs` (line 11861), change:

```csharp
var transitionSeconds = rule.ScaleMode == AvatarScaleMode.GlitchyRandomHeight
    ? 0
    : Math.Max(0, rule.SmoothTransitionSeconds);
```

to:

```csharp
var transitionSeconds = rule.ScaleMode == AvatarScaleMode.GlitchyRandomHeight
    ? Math.Clamp(rule.GlitchyTransitionSeconds, 0, 5)
    : Math.Max(0, rule.SmoothTransitionSeconds);
```

Both files need the same change. Use `replaceAll: true` if both lines are identical, otherwise edit them individually.

- [ ] **Step 2: Build**

Run: `dotnet build "E:\!!!Program to work on\Proper Crystal Relay\VrcTwitchOscBridge\VrcTwitchOscBridge.csproj" --no-restore`
Expected: Build succeeds.

- [ ] **Step 3: Commit**

```bash
git add "VrcTwitchOscBridge/Services/BridgeCoordinator.cs" "VrcTwitchOscBridge/ViewModels/MainWindowViewModel.cs"
git commit -m "feat(scale): use GlitchyTransitionSeconds for Glitchy per-jump timing"
```

---

## Task 10: Implement silent auto-bypass in the runtime

**Files:**
- Modify: `VrcTwitchOscBridge/Services/BridgeCoordinator.cs` (lines 5965 and 6031)

- [ ] **Step 1: Add a helper that returns whether bypass is in effect for a rule**

Insert this static helper just above `IsRelativeScaleAtLimit` (around line 5959):

```csharp
private static bool IsAutoBypassingVrChatLimits(AvatarScaleRuleSnapshot rule)
{
    if (rule.BypassVrChatScaleLimits)
    {
        return true;
    }

    return rule.ScaleMode is AvatarScaleMode.RelativeHeight or AvatarScaleMode.Multiplier
        && rule.RelativeMinimumHeightMeters > 0
        && rule.RelativeMaximumHeightMeters > 0
        && rule.RelativeMinimumHeightMeters < rule.RelativeMaximumHeightMeters;
}
```

- [ ] **Step 2: Update `ApplyAvatarScaleHeightLimits` to use the helper**

Replace the body of the method (line 6025) so the bypass check uses the helper:

```csharp
private double ApplyAvatarScaleHeightLimits(
    AvatarScaleRuleSnapshot rule,
    double value,
    string targetDescription)
{
    var clampedValue = ClampAvatarScaleHeight(rule, value);
    if (!IsAutoBypassingVrChatLimits(rule))
    {
        return ClampToVrChatScaleLimits(clampedValue);
    }

    var vrChatLimitedValue = GetVrChatScaleLimitedHeight(clampedValue);
    if (Math.Abs(vrChatLimitedValue - clampedValue) > 0.0001)
    {
        var displayName = string.IsNullOrWhiteSpace(rule.Name) ? "Avatar Scale" : rule.Name;
        WriteLog($"Avatar scale '{displayName}' bypassed VRChat world min/max for {targetDescription}; using {clampedValue:0.###}m instead of VRChat's {vrChatLimitedValue:0.###}m limit.");
    }

    return clampedValue;
}
```

- [ ] **Step 3: Build**

Run: `dotnet build "E:\!!!Program to work on\Proper Crystal Relay\VrcTwitchOscBridge\VrcTwitchOscBridge.csproj" --no-restore`
Expected: Build succeeds.

- [ ] **Step 4: Commit**

```bash
git add "VrcTwitchOscBridge/Services/BridgeCoordinator.cs"
git commit -m "feat(scale): silently bypass VRChat world min/max when Min/Max are set"
```

---

## Task 11: Update `IsRelativeScaleAtLimit` to also account for the Multiplier mode

**Files:**
- Modify: `VrcTwitchOscBridge/Services/BridgeCoordinator.cs` (line 5959, `IsRelativeScaleAtLimit`)

The current `IsRelativeScaleAtLimit` only triggers for `RelativeHeight`. With the new auto-bypass, `Multiplier` mode users can also have explicit limits, but the "at limit" early-out check is only meaningful for `RelativeHeight` (the other modes don't grow toward a limit; they jump to a value). Leave the function as-is — it is still correct. **Skip this task.**

If during build/test a regression is found, revisit this task and add `AvatarScaleMode.Multiplier` to the check, gated on `rule.MultiplierDirectionId == (int)AvatarScaleMultiplierDirection.Grow` and a > 0.0001 delta check.

---

## Task 12: Round-trip the new fields through `SettingsStore.cs`

**Files:**
- Modify: `VrcTwitchOscBridge/Services/SettingsStore.cs` (the persisted `AvatarScaleRuleDto` around line 3329, and the read/write helpers around lines 1726, 1798, 1876)

- [ ] **Step 1: Add the new fields to the DTO**

Find the DTO `class AvatarScaleRuleDto` (or similar; search for `public double HeightMultiplier` at line 3329) and add:

```csharp
public int MultiplierDirectionId { get; set; }
public double GlitchyTransitionSeconds { get; set; } = 0.4;
```

Add them immediately after `HeightMultiplier`.

- [ ] **Step 2: Read new fields on load**

In the read helper around line 1726, where the DTO is populated from the live rule, add:

```csharp
MultiplierDirectionId = (int)rule.MultiplierDirection,
GlitchyTransitionSeconds = rule.GlitchyTransitionSeconds,
```

- [ ] **Step 3: Write new fields on save**

In the write helper around line 1798, where the live rule is reconstructed from the DTO, add:

```csharp
MultiplierDirectionId = dto.MultiplierDirectionId,
GlitchyTransitionSeconds = dto.MultiitchyTransitionSeconds <= 0 ? 0.4 : dto.GlitchyTransitionSeconds,
```

(Correct the typo `dto.MultiitchyTransitionSeconds` to `dto.GlitchyTransitionSeconds` in the actual edit.)

- [ ] **Step 4: Build**

Run: `dotnet build "E:\!!!Program to work on\Proper Crystal Relay\VrcTwitchOscBridge\VrcTwitchOscBridge.csproj" --no-restore`
Expected: Build succeeds.

- [ ] **Step 5: Commit**

```bash
git add "VrcTwitchOscBridge/Services/SettingsStore.cs"
git commit -m "feat(scale): persist MultiplierDirection and GlitchyTransitionSeconds in settings"
```

---

## Task 13: Replace the primary Scale Action XAML block (lines 6848–7157)

**Files:**
- Modify: `VrcTwitchOscBridge/MainWindow.xaml` (lines 6848–7157 inclusive)

This is the largest single change. The replacement is a single `Border` with five nested sub-cards.

- [ ] **Step 1: Locate the start and end markers**

The block to replace begins at line 6848 with the comment-style line that contains `<Border Margin="0,14,0,0"` and `Background="{DynamicResource NestedPanelBrush}"`. It ends at line 7157 with the closing `</Border>` of that block.

Read lines 6848–7157 again to confirm the exact content.

- [ ] **Step 2: Replace the inner `StackPanel` contents (lines 6864–7156) with the new five sub-card layout**

Open `MainWindow.xaml` and replace the entire `<StackPanel>...</StackPanel>` block (lines 6864–7156) with the following XAML. Keep the outer `Border` (lines 6848–6863) unchanged.

```xml
<StackPanel>
    <TextBlock Text="{loc:Translate 'Scale Action'}"
               Style="{StaticResource HeadingTextStyle}"
               FontSize="20"
               FontWeight="Bold"
               Foreground="{DynamicResource TextBrush}" />

    <!-- 1. Mode row -->
    <Border Margin="0,12,0,0"
            Background="{DynamicResource NestedPanelBrush}"
            BorderBrush="{DynamicResource HighlightBorderBrush}"
            BorderThickness="1"
            CornerRadius="8"
            Padding="6">
        <Grid>
            <Grid.ColumnDefinitions>
                <ColumnDefinition Width="*" />
                <ColumnDefinition Width="*" />
                <ColumnDefinition Width="*" />
                <ColumnDefinition Width="*" />
                <ColumnDefinition Width="*" />
                <ColumnDefinition Width="*" />
            </Grid.ColumnDefinitions>
            <ToggleButton Grid.Column="0" Margin="2" Content="{loc:Translate 'Set Height'}"
                          IsChecked="{Binding ActiveMode, Converter={StaticResource EnumBooleanConverter}, ConverterParameter=SetHeight, Mode=OneWay}"
                          CommandParameter="SetHeight" />
            <ToggleButton Grid.Column="1" Margin="2" Content="{loc:Translate 'Random'}"
                          IsChecked="{Binding ActiveMode, Converter={StaticResource EnumBooleanConverter}, ConverterParameter=RandomHeight, Mode=OneWay}"
                          CommandParameter="RandomHeight" />
            <ToggleButton Grid.Column="2" Margin="2" Content="{loc:Translate 'Relative'}"
                          IsChecked="{Binding ActiveMode, Converter={StaticResource EnumBooleanConverter}, ConverterParameter=RelativeHeight, Mode=OneWay}"
                          CommandParameter="RelativeHeight" />
            <ToggleButton Grid.Column="3" Margin="2" Content="{loc:Translate 'Multiplier'}"
                          IsChecked="{Binding ActiveMode, Converter={StaticResource EnumBooleanConverter}, ConverterParameter=Multiplier, Mode=OneWay}"
                          CommandParameter="Multiplier" />
            <ToggleButton Grid.Column="4" Margin="2" Content="{loc:Translate 'Preset'}"
                          IsChecked="{Binding ActiveMode, Converter={StaticResource EnumBooleanConverter}, ConverterParameter=Preset, Mode=OneWay}"
                          CommandParameter="Preset" />
            <ToggleButton Grid.Column="5" Margin="2" Content="{loc:Translate 'Glitchy'}"
                          IsChecked="{Binding ActiveMode, Converter={StaticResource EnumBooleanConverter}, ConverterParameter=GlitchyRandomHeight, Mode=OneWay}"
                          CommandParameter="GlitchyRandomHeight" />
        </Grid>
        <Border.Style>
            <Style TargetType="Border">
                <Style.Triggers>
                    <DataTrigger Binding="{Binding ActiveMode}" Value="{x:Static models:AvatarScaleMode.SetHeight}">
                        <Setter Property="Tag" Value="SetHeight" />
                    </DataTrigger>
                </Style.Triggers>
            </Style>
        </Border.Style>
    </Border>
    <!-- A simple XAML command pattern below: each ToggleButton's Click sets the rule's ScaleMode. -->
```

- [ ] **Step 3: Add a click handler that maps the pressed button to `ScaleMode`**

WPF's `ToggleButton` + a one-way `IsChecked` binding alone is not enough to make a segmented control — pressing a button needs to set the underlying value, not just reflect it. Replace the six `ToggleButton` blocks above with this cleaner approach using a `ListBox` styled as a tab strip (or use six plain `Button`s and a `Click` handler that calls a VM command). The simpler approach for this codebase is six `Button` elements with `Style` triggers and a single `Click` handler in the code-behind.

Open `MainWindow.xaml.cs` and add a single click handler:

```csharp
private void ScaleActionModeButton_Click(object sender, RoutedEventArgs e)
{
    if (sender is not FrameworkElement fe || fe.Tag is not string tagName) return;
    if (!Enum.TryParse<AvatarScaleMode>(tagName, out var mode)) return;
    if (DataContext is MainWindowViewModel vm) vm.SetSelectedAvatarScaleMode(mode);
}
```

In `MainWindowViewModel.cs`, add a public method that mutates the currently selected rule's mode:

```csharp
public void SetSelectedAvatarScaleMode(AvatarScaleMode mode)
{
    if (SelectedAvatarScaleRule is { } rule)
    {
        rule.ScaleMode = mode;
    }
}
```

Now in the XAML, replace the six `ToggleButton` elements with six `Button` elements:

```xml
<Button Grid.Column="0" Margin="2" Tag="SetHeight" Click="ScaleActionModeButton_Click" Content="{loc:Translate 'Set Height'}">
    <Button.Style>
        <Style TargetType="Button" BasedOn="{StaticResource SecondaryButtonStyle}">
            <Style.Triggers>
                <DataTrigger Binding="{Binding ActiveMode}" Value="SetHeight">
                    <Setter Property="Background" Value="{DynamicResource HighlightBorderBrush}" />
                    <Setter Property="Foreground" Value="White" />
                    <Setter Property="FontWeight" Value="SemiBold" />
                </DataTrigger>
            </Style.Triggers>
        </Style>
    </Button.Style>
</Button>
```

Repeat for each of the six modes. Use `RandomHeight`, `RelativeHeight`, `Multiplier`, `Preset`, `GlitchyRandomHeight` for the `Tag`, `ActiveMode` trigger value, and a localized `Content` of `Random`, `Relative`, `Multiplier`, `Preset`, `Glitchy`.

- [ ] **Step 4: Add the four remaining sub-cards (Value, Range limits, Behavior, Advanced)**

Below the Mode row, add four `Border` sub-cards, each gated on the right `DataTrigger`. See the spec §4 for the field list. The Value card has six variants gated by `UsesTargetHeight`, `UsesRandomHeightRange`, `UsesRelativeHeight`, `UsesMultiplier`, `UsesPreset`, `UsesGlitchyRandomHeight`. The Range limits card is gated by `(UsesRelativeHeight or UsesMultiplier)`. The Behavior card holds `Reward Cooldown` (only when `UsesCreateOrManageReward`), `Active Time`, `Return Height`. The Advanced card holds a single `CheckBox` for `AdvancedRangeEnabled`.

The full replacement XAML is too large to inline here. Instead, follow the **existing pattern** in lines 6848–7157: keep the same `Border` chrome (`NestedPanelBrush` + `HighlightBorderBrush` + 1px border + 16 radius + 14 padding), keep the same `StackPanel` + `TextBlock` + `TextBox` shape, swap the visibility gates to point at `Uses*` properties (already defined). Drop the existing `Bypass VRChat world min/max` `CheckBox`, the helper text under it, and the `ScaleRangeHelpText` block. Drop the existing standalone `Hide at minimum / maximum` `CheckBox` helpers and group the two `CheckBox`es under a sub-header.

Add the per-mode preview block at the bottom of the Value card. Use:

```xml
<Border Margin="0,10,0,0"
        Background="#1a0f30"
        BorderBrush="#4a2c7a"
        BorderThickness="1"
        CornerRadius="8"
        Padding="8,6">
    <TextBlock Foreground="{DynamicResource TextBrush}" FontSize="12" TextWrapping="Wrap">
        <TextBlock.Text>
            <MultiBinding Converter="{StaticResource ScalePreviewConverter}">
                <Binding Path="ActiveMode" />
                <!-- SetHeight variant: -->
                <Binding Path="TargetHeightMeters" />
                <!-- Random / Glitchy variant: -->
                <Binding Path="MinimumHeightMeters" />
                <Binding Path="MaximumHeightMeters" />
                <Binding Path="GlitchyTransitionSeconds" />
                <!-- Relative variant: -->
                <Binding Path="RelativeHeightMeters" />
                <Binding Path="MultCurrentPreview" />
                <!-- Multiplier variant: -->
                <Binding Path="HeightMultiplier" />
                <Binding Path="MultCurrentPreview" />
                <Binding Path="MultiplierDirectionId" />
                <!-- Preset variant: -->
                <Binding Path="Preset" />
                <Binding Path="PresetHeight" />
            </MultiBinding>
        </TextBlock.Text>
    </TextBlock>
</Border>
```

- [ ] **Step 5: Add `MultCurrentPreview` and `PresetHeight` read-only helpers to `AvatarScaleRule`**

```csharp
public double MultCurrentPreview { get; set; } = 1.64;
public double PresetHeight => GetPresetHeight(Preset);
```

`MultCurrentPreview` is a local UI-only value (a `double` field) that the user can edit via a small `TextBox` in the Multiplier and Relative sub-cards. It is **not** persisted. Mark it with a comment in the model so a future maintainer does not accidentally serialize it.

- [ ] **Step 6: Build**

Run: `dotnet build "E:\!!!Program to work on\Proper Crystal Relay\VrcTwitchOscBridge\VrcTwitchOscBridge.csproj" --no-restore`
Expected: Build succeeds. If XAML errors point at the new sub-cards, verify the converter `x:Key` and the `xmlns:models` reference exist.

- [ ] **Step 7: Smoke check**

Run the debug launcher:

```bash
& "E:\!!!Program to work on\Proper Crystal Relay\Launch-Crystal-Relay-Debug.bat"
```

Verify: opening the Avatar Scaling tab → editing a rule shows the new layout; the mode row highlights the current mode; switching mode swaps the value card; typing out-of-range Min/Max values snaps them in.

- [ ] **Step 8: Commit**

```bash
git add "VrcTwitchOscBridge/MainWindow.xaml" "VrcTwitchOscBridge/MainWindow.xaml.cs" "VrcTwitchOscBridge/ViewModels/MainWindowViewModel.cs" "VrcTwitchOscBridge/Models/AvatarScaleRule.cs"
git commit -m "feat(scale): replace primary Scale Action XAML with mode-driven layout"
```

---

## Task 14: Replace the nested "Avatar Scaling Action" XAML block (lines 7853–7964)

**Files:**
- Modify: `VrcTwitchOscBridge/MainWindow.xaml` (lines 7853–7964)

This is the compact twin of the primary panel, used inside Power-Up and Cash Payment rules.

- [ ] **Step 1: Replace the inner `StackPanel` contents**

Apply the same five-sub-card pattern as Task 13, but drop the Range limits card (it is only meaningful in the full editor; in the nested panel, Min/Max and Hide-at are not exposed). The compact panel has four sub-cards: Mode, Value, Behavior (Active Time / Restore Height / Smooth Seconds), Advanced (single Advanced Range checkbox).

Drop the `Bypass VRChat world min/max` checkbox and the helper text. Drop the `ScaleRangeHelpText` block. Keep the `Avatar Scaling Action` heading at the top.

- [ ] **Step 2: Build**

Run: `dotnet build "E:\!!!Program to work on\Proper Crystal Relay\VrcTwitchOscBridge\VrcTwitchOscBridge.csproj" --no-restore`
Expected: Build succeeds.

- [ ] **Step 3: Smoke check**

Launch the debug build, navigate to a Power-Up rule whose action kind is `AvatarScaling`. Verify the nested panel renders the new layout. Repeat for a Cash Payment rule.

- [ ] **Step 4: Commit**

```bash
git add "VrcTwitchOscBridge/MainWindow.xaml"
git commit -m "feat(scale): replace nested Avatar Scaling Action XAML with compact mode-driven layout"
```

---

## Task 15: Add new `en-US` localization keys

**Files:**
- Modify: `VrcTwitchOscBridge/Resources/Localization/en-US.extra.json`

- [ ] **Step 1: Append the new keys**

Add at the end of the JSON object:

```json
"Scale Action Mode Set Height": "Set Height",
"Scale Action Mode Random Height": "Random",
"Scale Action Mode Relative Height": "Relative",
"Scale Action Mode Multiplier": "Multiplier",
"Scale Action Mode Preset": "Preset",
"Scale Action Mode Glitchy Random Height": "Glitchy",
"Scale Action Current (m)": "Current (m)",
"Scale Action Multiplier Divide": "÷",
"Scale Action Multiplier Grow": "×",
"Scale Action Multiplier Direction": "Direction",
"Scale Action Hide At Limit": "Hide this reward when reaching a limit",
"Scale Action Glitchy Transition": "Transition (s)",
"Scale Action Behavior Header": "Behavior",
"Scale Action Advanced Header": "Advanced",
"Scale Action Range Limits Header": "Range limits",
"Scale Action Value Label": "Value",
"Scale Preview Set": "Sets the avatar height directly to {0}m.",
"Scale Preview Random": "Each trigger rolls a random height between {0}m and {1}m.",
"Scale Preview Relative": "Adds {0}m to the current height, going from {1}m to {2}m.",
"Scale Preview Multiplier": "Going from {0}m to {1}m using {2}{3}.",
"Scale Preview Preset": "Sets the avatar height to the {0} preset, which is {1}m.",
"Scale Preview Glitchy": "Rapidly rolls random heights between {0}m and {1}m with a {2}s transition between each jump, until Active Time ends."
```

- [ ] **Step 2: Build**

Run: `dotnet build "E:\!!!Program to work on\Proper Crystal Relay\VrcTwitchOscBridge\VrcTwitchOscBridge.csproj" --no-restore`
Expected: Build succeeds.

- [ ] **Step 3: Commit**

```bash
git add "VrcTwitchOscBridge/Resources/Localization/en-US.extra.json"
git commit -m "feat(scale): add en-US localization keys for redesigned Scale Action"
```

---

## Task 16: Add matching translations to every other `*.extra.json`

**Files:**
- Modify: All `VrcTwitchOscBridge/Resources/Localization/*.extra.json` files **except** `en-US.extra.json` and `LocalizationAudit` files.

List the files first:

```bash
Get-ChildItem -LiteralPath "E:\!!!Program to work on\Proper Crystal Relay\VrcTwitchOscBridge\Resources\Localization" -Filter "*.extra.json" | Select-Object -ExpandProperty Name
```

- [ ] **Step 1: Add matching keys to every non-English `*.extra.json`**

For each language file, append the same key set, with values translated to the target language. Use the localization rules from `AGENTS.md`:

- Informal / friendly register (`du` for de-DE, `tú` for es-ES, `tu` for fr-FR, informal equivalents for others).
- Keep `Bits`, `Subs`, `OSC`, `OSCQuery`, `VRChat`, `Twitch`, `Crystal Relay`, `StreamElements`, `Streamlabs`, `Ko-fi` in English.
- Preserve every `{0}`, `{1}`, `{2}` placeholder exactly.
- No empty values.

- [ ] **Step 2: Run the localization audit**

Run the `LocalizationAudit` project per `AGENTS.md` § Localization Rules. Verify zero missing keys and zero empty values.

- [ ] **Step 3: Commit**

```bash
git add "VrcTwitchOscBridge/Resources/Localization/"
git commit -m "feat(scale): translate redesigned Scale Action keys into all languages"
```

---

## Task 17: Final build + smoke check + dependency scan

- [ ] **Step 1: Final build**

Run: `dotnet build "E:\!!!Program to work on\Proper Crystal Relay\VrcTwitchOscBridge\VrcTwitchOscBridge.csproj" --no-restore"`
Expected: Build succeeds. Capture the count of warnings introduced by this change.

- [ ] **Step 2: Launch the debug build and exercise the new UI**

```bash
& "E:\!!!Program to work on\Proper Crystal Relay\Launch-Crystal-Relay-Debug.bat"
```

Verify all six modes swap the value card; Min/Max clamp in the safe range; the Advanced toggle widens the bound; the Multiplier `× / ÷` button flips the preview text; the Glitchy per-jump transition field appears only in Glitchy mode; the nested "Avatar Scaling Action" panel inside a Power-Up rule uses the compact layout.

- [ ] **Step 3: Run the dependency vulnerability scan**

```bash
powershell -ExecutionPolicy Bypass -File "E:\!!!Program to work on\Proper Crystal Relay\Check-Crystal-Relay-Dependencies.ps1"
```

Expected: no new vulnerable packages introduced by this change.

- [ ] **Step 4: Update AGENTS.md**

Per `AGENTS.md` § Versioning Rules, if any code change has occurred since the last release, move `Active development build` to the next patch and confirm the lane. If the user wants to ship as a test build, follow the test build instructions in `AGENTS.md`. If the user wants to ship as a beta, follow the beta cycle workflow in § Changelog and Release Notes Workflow.

- [ ] **Step 5: Commit (only if AGENTS.md was changed)**

```bash
git add "AGENTS.md"
git commit -m "chore: update active build lane after Scale Action UI redesign"
```

---

## Spec Coverage Check

| Spec section | Task(s) |
|---|---|
| §4 Layout (five sub-cards) | 13, 14 |
| §5 Mode row (segmented control) | 13 |
| §6.1 `GlitchyTransitionSeconds` | 2 |
| §6.2 `ActiveMode` | 3 |
| §6.3 Silent auto-bypass | 10 |
| §6.4 `MultiplierDirection` enum + persistence | 1, 7, 8, 12 |
| §6.5 Input clamping | (already in model `ClampHeight`; new UI bound 0.20 is enforced at runtime by spec §6.3, no model change needed) |
| §7 Live preview | 5, 6, 13 |
| §8 Localization | 15, 16 |

## Type/Name Consistency Check

- `AvatarScaleMultiplierDirection` enum introduced in Task 1; used in Tasks 4, 7, 8, 10. ✓
- `MultiplierDirection` and `MultiplierDirectionId` properties consistent. ✓
- `GlitchyTransitionSeconds` consistent across model, snapshot, executor, and XAML. ✓
- `ActiveMode` consistent across model and XAML triggers. ✓
- `ScalePreviewConverter` x:Key matches usage. ✓
- `BypassVrChatScaleLimits` retained everywhere for backward compat. ✓
