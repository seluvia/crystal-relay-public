# Scale Actions: Per-Mode Transition Seconds, Pre-text Fix, Theme Color Fix — Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Add per-mode transition seconds to all 6 Avatar Scaling modes, fix the ScalePreviewConverter pre-text that shows "---", and fix the hardcoded theme colors on the live preview block.

**Architecture:** Replace the shared `SmoothTransitionSeconds` with 6 independent per-mode fields on `AvatarScaleRule`. A computed `SmoothTransitionSeconds` alias preserves runtime compatibility. Fix the converter's enum handling and replace hardcoded XAML colors with dynamic theme brushes.

**Tech Stack:** C#, WPF/XAML, .NET 10

---

## File Structure

| File | Responsibility |
|------|---------------|
| `Models/AvatarScaleRule.cs` | 7 new backing fields + 7 new properties + computed `SmoothTransitionSeconds` + `SupporterGrowthTransitionSeconds` |
| `Services/SettingsStore.cs` | DTO fields, serialization, deserialization with backward compat |
| `Services/BridgeRuntimeConfiguration.cs` | Snapshot record + factory update |
| `Services/BridgeCoordinator.cs` | `GetAvatarScaleEffectDurationSeconds` + Supporter Growth paths |
| `ViewModels/MainWindowViewModel.cs` | `GetAvatarScaleEffectDurationSeconds` + `CreateDefaultAvatarScaleRule` |
| `Converters.cs` | Fix enum handling in `ScalePreviewConverter` |
| `MainWindow.xaml` | Per-mode transition fields in UI, theme color fix, preview binding update |

---

### Task 1: Add Per-Mode Fields to AvatarScaleRule Model

**Files:**
- Modify: `Models/AvatarScaleRule.cs`

- [ ] **Step 1: Add backing fields for the 7 new per-mode transition fields**

After line 327 (`private double glitchyTransitionSeconds = 0.4;`), add:

```csharp
    private double setHeightTransitionSeconds;
    private double randomHeightTransitionSeconds;
    private double relativeHeightTransitionSeconds;
    private double multiplierTransitionSeconds;
    private double presetTransitionSeconds;
    private double supporterGrowthTransitionSeconds;
```

- [ ] **Step 2: Add properties for the 6 per-mode transition fields**

After the `GlitchyTransitionSeconds` property (line 687), add:

```csharp
    public double SetHeightTransitionSeconds
    {
        get => setHeightTransitionSeconds;
        set => SetAndRaiseScale(ref setHeightTransitionSeconds, Math.Clamp(value, 0, 30));
    }

    public double RandomHeightTransitionSeconds
    {
        get => randomHeightTransitionSeconds;
        set => SetAndRaiseScale(ref randomHeightTransitionSeconds, Math.Clamp(value, 0, 30));
    }

    public double RelativeHeightTransitionSeconds
    {
        get => relativeHeightTransitionSeconds;
        set => SetAndRaiseScale(ref relativeHeightTransitionSeconds, Math.Clamp(value, 0, 30));
    }

    public double MultiplierTransitionSeconds
    {
        get => multiplierTransitionSeconds;
        set => SetAndRaiseScale(ref multiplierTransitionSeconds, Math.Clamp(value, 0, 30));
    }

    public double PresetTransitionSeconds
    {
        get => presetTransitionSeconds;
        set => SetAndRaiseScale(ref presetTransitionSeconds, Math.Clamp(value, 0, 30));
    }

    public double GlitchyRandomHeightTransitionSeconds
    {
        get => glitchyTransitionSeconds;
        set => SetAndRaiseScale(ref glitchyTransitionSeconds, Math.Clamp(value, 0, 30));
    }
```

- [ ] **Step 3: Add SupporterGrowthTransitionSeconds property**

After the new per-mode properties, add:

```csharp
    public double SupporterGrowthTransitionSeconds
    {
        get => supporterGrowthTransitionSeconds;
        set => SetAndRaiseSupporterGrowth(ref supporterGrowthTransitionSeconds, Math.Clamp(value, 0, 30));
    }
```

- [ ] **Step 4: Replace SmoothTransitionSeconds with computed alias**

Replace the existing `SmoothTransitionSeconds` property (lines 677-681) with:

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

- [ ] **Step 5: Remove the old SmoothTransitionSeconds setter and backing field**

Remove the `smoothTransitionSeconds` backing field declaration (line 326). The computed property has no setter — it derives from the per-mode fields.

- [ ] **Step 6: Update RaiseScaleProperties to include SmoothTransitionSeconds**

In `RaiseScaleProperties()` (line 1101), add after line 1116:

```csharp
        RaisePropertyChanged(nameof(SmoothTransitionSeconds));
```

- [ ] **Step 7: Verify build compiles**

Run: `dotnet build "E:\!!!Program to work on\Proper Crystal Relay\VrcTwitchOscBridge\VrcTwitchOscBridge.csproj" --no-restore`

Expected: Build succeeds (some warnings about missing DTO fields are expected until Task 2).

---

### Task 2: Update SettingsStore Serialization

**Files:**
- Modify: `Services/SettingsStore.cs`

- [ ] **Step 1: Add new DTO properties**

In the `PersistedAvatarScaleRule` class, after line 3347 (`public double SmoothTransitionSeconds { get; set; }`), add:

```csharp
        public double SetHeightTransitionSeconds { get; set; }
        public double RandomHeightTransitionSeconds { get; set; }
        public double RelativeHeightTransitionSeconds { get; set; }
        public double MultiplierTransitionSeconds { get; set; }
        public double PresetTransitionSeconds { get; set; }
        public double GlitchyRandomHeightTransitionSeconds { get; set; }
        public double SupporterGrowthTransitionSeconds { get; set; }
```

- [ ] **Step 2: Update serialization (model → DTO)**

In `ToPersistedAvatarScaleRule`, replace line 1728 (`GlitchyTransitionSeconds = rule.GlitchyTransitionSeconds,`) and line 1733 (`SmoothTransitionSeconds = rule.SmoothTransitionSeconds,`) with:

```csharp
            GlitchyTransitionSeconds = rule.GlitchyRandomHeightTransitionSeconds,
            SetHeightTransitionSeconds = rule.SetHeightTransitionSeconds,
            RandomHeightTransitionSeconds = rule.RandomHeightTransitionSeconds,
            RelativeHeightTransitionSeconds = rule.RelativeHeightTransitionSeconds,
            MultiplierTransitionSeconds = rule.MultiplierTransitionSeconds,
            PresetTransitionSeconds = rule.PresetTransitionSeconds,
            GlitchyRandomHeightTransitionSeconds = rule.GlitchyRandomHeightTransitionSeconds,
            SupporterGrowthTransitionSeconds = rule.SupporterGrowthTransitionSeconds,
            SmoothTransitionSeconds = 0,
```

- [ ] **Step 3: Update deserialization (DTO → model)**

In `ToAvatarScaleRule`, replace line 1816 (`GlitchyTransitionSeconds = rule.GlitchyTransitionSeconds <= 0 ? 0.4 : rule.GlitchyTransitionSeconds,`) and line 1821 (`SmoothTransitionSeconds = Math.Max(0, rule.SmoothTransitionSeconds),`) with:

```csharp
            SetHeightTransitionSeconds = Math.Max(0, rule.SetHeightTransitionSeconds),
            RandomHeightTransitionSeconds = Math.Max(0, rule.RandomHeightTransitionSeconds),
            RelativeHeightTransitionSeconds = Math.Max(0, rule.RelativeHeightTransitionSeconds > 0
                ? rule.RelativeHeightTransitionSeconds
                : rule.SmoothTransitionSeconds),
            MultiplierTransitionSeconds = Math.Max(0, rule.MultiplierTransitionSeconds),
            PresetTransitionSeconds = Math.Max(0, rule.PresetTransitionSeconds),
            GlitchyRandomHeightTransitionSeconds = Math.Max(0, rule.GlitchyRandomHeightTransitionSeconds > 0
                ? rule.GlitchyRandomHeightTransitionSeconds
                : rule.GlitchyTransitionSeconds),
            SupporterGrowthTransitionSeconds = Math.Max(0, rule.SupporterGrowthTransitionSeconds > 0
                ? rule.SupporterGrowthTransitionSeconds
                : rule.SmoothTransitionSeconds),
```

- [ ] **Step 4: Verify build compiles**

Run: `dotnet build "E:\!!!Program to work on\Proper Crystal Relay\VrcTwitchOscBridge\VrcTwitchOscBridge.csproj" --no-restore`

Expected: Build succeeds.

---

### Task 3: Update Runtime Snapshot

**Files:**
- Modify: `Services/BridgeRuntimeConfiguration.cs`

- [ ] **Step 1: Update AvatarScaleRuleSnapshot record**

Replace line 210 (`double GlitchyTransitionSeconds,`) and line 215 (`double SmoothTransitionSeconds,`) with:

```csharp
    double SetHeightTransitionSeconds,
    double RandomHeightTransitionSeconds,
    double RelativeHeightTransitionSeconds,
    double MultiplierTransitionSeconds,
    double PresetTransitionSeconds,
    double GlitchyRandomHeightTransitionSeconds,
    double SupporterGrowthTransitionSeconds,
    double SmoothTransitionSeconds,
```

- [ ] **Step 2: Update snapshot factory**

In `TryToAvatarScaleSnapshot`, replace line 1076 (`Math.Clamp(rule.GlitchyTransitionSeconds, 0, 5),`) and line 1081 (`Math.Clamp(rule.SmoothTransitionSeconds, 0, 30),`) with:

```csharp
            Math.Clamp(rule.SetHeightTransitionSeconds, 0, 30),
            Math.Clamp(rule.RandomHeightTransitionSeconds, 0, 30),
            Math.Clamp(rule.RelativeHeightTransitionSeconds, 0, 30),
            Math.Clamp(rule.MultiplierTransitionSeconds, 0, 30),
            Math.Clamp(rule.PresetTransitionSeconds, 0, 30),
            Math.Clamp(rule.GlitchyRandomHeightTransitionSeconds, 0, 30),
            Math.Clamp(rule.SupporterGrowthTransitionSeconds, 0, 30),
            Math.Clamp(rule.SmoothTransitionSeconds, 0, 30),
```

- [ ] **Step 3: Verify build compiles**

Run: `dotnet build "E:\!!!Program to work on\Proper Crystal Relay\VrcTwitchOscBridge\VrcTwitchOscBridge.csproj" --no-restore`

Expected: Build succeeds.

---

### Task 4: Update BridgeCoordinator Runtime

**Files:**
- Modify: `Services/BridgeCoordinator.cs`

- [ ] **Step 1: Update GetAvatarScaleEffectDurationSeconds**

Replace lines 11874-11884 with:

```csharp
    private static int GetAvatarScaleEffectDurationSeconds(AvatarScaleRuleSnapshot rule)
    {
        var transitionSeconds = rule.ScaleMode == AvatarScaleMode.GlitchyRandomHeight
            ? Math.Clamp(rule.GlitchyRandomHeightTransitionSeconds, 0, 30)
            : Math.Max(0, rule.SmoothTransitionSeconds);
        var activeSeconds = Math.Max(0, rule.ActiveTimeSeconds);
        var restoreTransitionSeconds = activeSeconds > 0 && rule.RestoreMode != AvatarScaleRestoreMode.None
            ? Math.Max(0, rule.SmoothTransitionSeconds)
            : 0;
        return (int)Math.Ceiling(transitionSeconds + activeSeconds + restoreTransitionSeconds);
    }
```

- [ ] **Step 2: Update Supporter Growth paths**

Replace `rule.SmoothTransitionSeconds` with `rule.SupporterGrowthTransitionSeconds` at lines 5411, 5427, and 5488.

- [ ] **Step 3: Verify build compiles**

Run: `dotnet build "E:\!!!Program to work on\Proper Crystal Relay\VrcTwitchOscBridge\VrcTwitchOscBridge.csproj" --no-restore`

Expected: Build succeeds.

---

### Task 5: Update ViewModel

**Files:**
- Modify: `ViewModels/MainWindowViewModel.cs`

- [ ] **Step 1: Update GetAvatarScaleEffectDurationSeconds**

Replace lines 13055-13065 with:

```csharp
    private static int GetAvatarScaleEffectDurationSeconds(AvatarScaleRule rule)
    {
        var transitionSeconds = rule.ScaleMode == AvatarScaleMode.GlitchyRandomHeight
            ? Math.Clamp(rule.GlitchyRandomHeightTransitionSeconds, 0, 30)
            : Math.Max(0, rule.SmoothTransitionSeconds);
        var activeSeconds = Math.Max(0, rule.ActiveTimeSeconds);
        var restoreTransitionSeconds = activeSeconds > 0 && rule.RestoreMode != AvatarScaleRestoreMode.None
            ? Math.Max(0, rule.SmoothTransitionSeconds)
            : 0;
        return (int)Math.Ceiling(transitionSeconds + activeSeconds + restoreTransitionSeconds);
    }
```

- [ ] **Step 2: Update CreateDefaultAvatarScaleRule**

Replace line 19291 (`SmoothTransitionSeconds = 0`) with:

```csharp
            SetHeightTransitionSeconds = 0,
            RandomHeightTransitionSeconds = 0,
            RelativeHeightTransitionSeconds = 0,
            MultiplierTransitionSeconds = 0,
            PresetTransitionSeconds = 0,
            GlitchyRandomHeightTransitionSeconds = 0,
            SupporterGrowthTransitionSeconds = 0
```

- [ ] **Step 3: Update PowerUpRule.CreateDefaultScaleAction**

In `Models/PowerUpRule.cs`, replace line 408 (`SmoothTransitionSeconds = 0,`) with:

```csharp
            SetHeightTransitionSeconds = 0,
            RandomHeightTransitionSeconds = 0,
            RelativeHeightTransitionSeconds = 0,
            MultiplierTransitionSeconds = 0,
            PresetTransitionSeconds = 0,
            GlitchyRandomHeightTransitionSeconds = 0,
            SupporterGrowthTransitionSeconds = 0,
```

- [ ] **Step 4: Update CashPaymentRule.CreateDefaultScaleAction**

In `Models/CashPaymentRule.cs`, replace line 533 (`SmoothTransitionSeconds = 0`) with:

```csharp
            SetHeightTransitionSeconds = 0,
            RandomHeightTransitionSeconds = 0,
            RelativeHeightTransitionSeconds = 0,
            MultiplierTransitionSeconds = 0,
            PresetTransitionSeconds = 0,
            GlitchyRandomHeightTransitionSeconds = 0,
            SupporterGrowthTransitionSeconds = 0
```

- [ ] **Step 5: Verify build compiles**

Run: `dotnet build "E:\!!!Program to work on\Proper Crystal Relay\VrcTwitchOscBridge\VrcTwitchOscBridge.csproj" --no-restore`

Expected: Build succeeds.

---

### Task 6: Fix ScalePreviewConverter

**Files:**
- Modify: `Converters.cs`

- [ ] **Step 1: Fix enum handling and em-dash encoding**

Replace the entire `ScalePreviewConverter.Convert` method (lines 75-109) with:

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
                    : "\u2014",
            "RelativeHeight" =>
                values.Length >= 3 && TryGetDouble(values[1], out var ch) && TryGetDouble(values[2], out var cu)
                    ? string.Format(culture, "Adds {0:+0.##;-0.##;0}m to the current height, going from {1:0.##}m to {2:0.##}m.", ch, cu, cu + ch)
                    : "\u2014",
            "Multiplier" =>
                values.Length >= 4 && TryGetDouble(values[1], out var mul) && TryGetDouble(values[2], out var mcu) && values[3] is bool divide
                    ? string.Format(culture, divide
                        ? "Going from {0:0.##}m to {1:0.##}m using \u00F7{2:0.##}."
                        : "Going from {0:0.##}m to {1:0.##}m using \u00D7{2:0.##}.", mcu, divide && mul != 0 ? mcu / mul : mcu * mul, mul)
                    : "\u2014",
            "Preset" =>
                values.Length >= 3 && values[1] is string label && TryGetDouble(values[2], out var h2)
                    ? string.Format(culture, "Sets the avatar height to the {0} preset, which is {1:0.##}m.", label, h2)
                    : "\u2014",
            _ => "\u2014"
        };
    }
```

- [ ] **Step 2: Verify build compiles**

Run: `dotnet build "E:\!!!Program to work on\Proper Crystal Relay\VrcTwitchOscBridge\VrcTwitchOscBridge.csproj" --no-restore`

Expected: Build succeeds.

---

### Task 7: Update XAML — Main Avatar Scaling Section

**Files:**
- Modify: `MainWindow.xaml`

- [ ] **Step 1: Add transition seconds to SetHeight variant**

After line 6993 (`<TextBox Text="{Binding TargetHeightMeters, UpdateSourceTrigger=LostFocus}" />`), add:

```xml
                                                                          <TextBlock Text="{loc:Translate 'Transition Seconds'}"
                                                                                     Margin="0,12,0,0"
                                                                                     Foreground="{DynamicResource TextBrush}"
                                                                                     FontWeight="SemiBold" />
                                                                          <TextBox Text="{Binding SetHeightTransitionSeconds, UpdateSourceTrigger=LostFocus}" />
```

- [ ] **Step 2: Replace Glitchy-only transition field with Random/Glitchy split**

Replace lines 7022-7038 (the Glitchy-only transition field block) with:

```xml
                                                                          <!-- Random mode: Transition field (visible when Random, not Glitchy) -->
                                                                          <StackPanel Margin="0,12,0,0">
                                                                              <StackPanel.Style>
                                                                                  <Style TargetType="StackPanel">
                                                                                      <Setter Property="Visibility" Value="Collapsed" />
                                                                                      <Style.Triggers>
                                                                                          <DataTrigger Binding="{Binding UsesRandomHeight}" Value="True">
                                                                                              <Setter Property="Visibility" Value="Visible" />
                                                                                          </DataTrigger>
                                                                                      </Style.Triggers>
                                                                                  </Style>
                                                                              </StackPanel.Style>
                                                                              <TextBlock Text="{loc:Translate 'Transition Seconds'}"
                                                                                         Foreground="{DynamicResource TextBrush}"
                                                                                         FontWeight="SemiBold" />
                                                                              <TextBox Text="{Binding RandomHeightTransitionSeconds, UpdateSourceTrigger=LostFocus}" />
                                                                          </StackPanel>
                                                                          <!-- Glitchy mode: Transition field -->
                                                                          <StackPanel Margin="0,12,0,0">
                                                                              <StackPanel.Style>
                                                                                  <Style TargetType="StackPanel">
                                                                                      <Setter Property="Visibility" Value="Collapsed" />
                                                                                      <Style.Triggers>
                                                                                          <DataTrigger Binding="{Binding UsesGlitchyRandomHeight}" Value="True">
                                                                                              <Setter Property="Visibility" Value="Visible" />
                                                                                          </DataTrigger>
                                                                                      </Style.Triggers>
                                                                                  </Style>
                                                                              </StackPanel.Style>
                                                                              <TextBlock Text="{loc:Translate 'Glitchy Transition Seconds'}"
                                                                                         Foreground="{DynamicResource TextBrush}"
                                                                                         FontWeight="SemiBold" />
                                                                              <TextBox Text="{Binding GlitchyRandomHeightTransitionSeconds, UpdateSourceTrigger=LostFocus}" />
                                                                          </StackPanel>
```

- [ ] **Step 3: Update Relative variant transition binding**

Replace line 7075 (`<TextBox Text="{Binding SmoothTransitionSeconds, UpdateSourceTrigger=LostFocus}" />`) with:

```xml
                                                                          <TextBox Text="{Binding RelativeHeightTransitionSeconds, UpdateSourceTrigger=LostFocus}" />
```

- [ ] **Step 4: Add transition seconds to Multiplier variant**

After the Multiplier variant's closing `</Grid>` (line 7115), before the closing `</StackPanel>` (line 7116), add:

```xml
                                                                          <TextBlock Text="{loc:Translate 'Transition Seconds'}"
                                                                                     Margin="0,12,0,0"
                                                                                     Foreground="{DynamicResource TextBrush}"
                                                                                     FontWeight="SemiBold" />
                                                                          <TextBox Text="{Binding MultiplierTransitionSeconds, UpdateSourceTrigger=LostFocus}" />
```

- [ ] **Step 5: Add transition seconds to Preset variant**

After the Preset variant's `</ComboBox>` (line 7134), before the closing `</StackPanel>` (line 7135), add:

```xml
                                                                          <TextBlock Text="{loc:Translate 'Transition Seconds'}"
                                                                                     Margin="0,12,0,0"
                                                                                     Foreground="{DynamicResource TextBrush}"
                                                                                     FontWeight="SemiBold" />
                                                                          <TextBox Text="{Binding PresetTransitionSeconds, UpdateSourceTrigger=LostFocus}" />
```

- [ ] **Step 6: Fix hardcoded theme colors on live preview block**

Replace lines 7139-7140:

```xml
                                                                              Background="#1a0f30"
                                                                              BorderBrush="#4a2c7a"
```

With:

```xml
                                                                              Background="{DynamicResource NestedPanelBrush}"
                                                                              BorderBrush="{DynamicResource HighlightBorderBrush}"
```

- [ ] **Step 7: Update Supporter Growth transition binding**

Replace line 6688 (`<TextBox Text="{Binding SmoothTransitionSeconds, UpdateSourceTrigger=LostFocus}" />`) with:

```xml
<TextBox Text="{Binding SupporterGrowthTransitionSeconds, UpdateSourceTrigger=LostFocus}" />
```

- [ ] **Step 8: Verify build compiles**

Run: `dotnet build "E:\!!!Program to work on\Proper Crystal Relay\VrcTwitchOscBridge\VrcTwitchOscBridge.csproj" --no-restore`

Expected: Build succeeds.

---

### Task 8: Update XAML — Cash Payment Embedded Scale Action

**Files:**
- Modify: `MainWindow.xaml`

- [ ] **Step 1: Add transition seconds to Cash Payment SetHeight variant**

After line 8134 (`<TextBox Text="{Binding TargetHeightMeters, UpdateSourceTrigger=LostFocus}" />`), add:

```xml
                                                                    <TextBlock Text="{loc:Translate 'Transition Seconds'}"
                                                                               Margin="0,12,0,0"
                                                                               Foreground="{DynamicResource TextBrush}"
                                                                               FontWeight="SemiBold" />
                                                                    <TextBox Text="{Binding SetHeightTransitionSeconds, UpdateSourceTrigger=LostFocus}" />
```

- [ ] **Step 2: Replace Cash Payment Glitchy-only transition field with Random/Glitchy split**

Replace lines 8163-8179 (the Glitchy-only transition field block) with:

```xml
                                                                    <!-- Random mode: Transition field (visible when Random, not Glitchy) -->
                                                                    <StackPanel Margin="0,12,0,0">
                                                                        <StackPanel.Style>
                                                                            <Style TargetType="StackPanel">
                                                                                <Setter Property="Visibility" Value="Collapsed" />
                                                                                <Style.Triggers>
                                                                                    <DataTrigger Binding="{Binding UsesRandomHeight}" Value="True">
                                                                                        <Setter Property="Visibility" Value="Visible" />
                                                                                    </DataTrigger>
                                                                                </Style.Triggers>
                                                                            </Style>
                                                                        </StackPanel.Style>
                                                                        <TextBlock Text="{loc:Translate 'Transition Seconds'}"
                                                                                   Foreground="{DynamicResource TextBrush}"
                                                                                   FontWeight="SemiBold" />
                                                                        <TextBox Text="{Binding RandomHeightTransitionSeconds, UpdateSourceTrigger=LostFocus}" />
                                                                    </StackPanel>
                                                                    <!-- Glitchy mode: Transition field -->
                                                                    <StackPanel Margin="0,12,0,0">
                                                                        <StackPanel.Style>
                                                                            <Style TargetType="StackPanel">
                                                                                <Setter Property="Visibility" Value="Collapsed" />
                                                                                <Style.Triggers>
                                                                                    <DataTrigger Binding="{Binding UsesGlitchyRandomHeight}" Value="True">
                                                                                        <Setter Property="Visibility" Value="Visible" />
                                                                                    </DataTrigger>
                                                                                </Style.Triggers>
                                                                            </Style>
                                                                        </StackPanel.Style>
                                                                        <TextBlock Text="{loc:Translate 'Glitchy Transition Seconds'}"
                                                                                   Foreground="{DynamicResource TextBrush}"
                                                                                   FontWeight="SemiBold" />
                                                                        <TextBox Text="{Binding GlitchyRandomHeightTransitionSeconds, UpdateSourceTrigger=LostFocus}" />
                                                                    </StackPanel>
```

- [ ] **Step 3: Add transition seconds to Cash Payment Multiplier variant**

After the Multiplier variant's closing `</Grid>` (line 8251), before the closing `</StackPanel>` (line 8252), add:

```xml
                                                                    <TextBlock Text="{loc:Translate 'Transition Seconds'}"
                                                                               Margin="0,12,0,0"
                                                                               Foreground="{DynamicResource TextBrush}"
                                                                               FontWeight="SemiBold" />
                                                                    <TextBox Text="{Binding MultiplierTransitionSeconds, UpdateSourceTrigger=LostFocus}" />
```

- [ ] **Step 4: Add transition seconds to Cash Payment Preset variant**

After the Preset variant's `</ComboBox>` (line 8270), before the closing `</StackPanel>` (line 8271), add:

```xml
                                                                    <TextBlock Text="{loc:Translate 'Transition Seconds'}"
                                                                               Margin="0,12,0,0"
                                                                               Foreground="{DynamicResource TextBrush}"
                                                                               FontWeight="SemiBold" />
                                                                    <TextBox Text="{Binding PresetTransitionSeconds, UpdateSourceTrigger=LostFocus}" />
```

- [ ] **Step 5: Remove Smooth Seconds from Cash Payment behavior sub-card**

Since each mode now has its own transition field in the value sub-card, remove the "Smooth Seconds" column from the behavior sub-card. Replace the 3-column `UniformGrid` (lines 8281-8300) with a 2-column layout:

```xml
                                                                <UniformGrid Columns="2">
                                                                    <StackPanel Margin="0,0,10,0">
                                                                        <TextBlock Text="{loc:Translate 'Active Time Seconds'}"
                                                                                   Foreground="{DynamicResource TextBrush}"
                                                                                   FontWeight="SemiBold" />
                                                                        <TextBox Text="{Binding ActiveTimeSeconds, UpdateSourceTrigger=LostFocus}" />
                                                                    </StackPanel>
                                                                    <StackPanel Margin="10,0,0,0">
                                                                        <TextBlock Text="{loc:Translate 'Restore Height Meters'}"
                                                                                   Foreground="{DynamicResource TextBrush}"
                                                                                   FontWeight="SemiBold" />
                                                                        <TextBox Text="{Binding RestoreHeightMeters, UpdateSourceTrigger=LostFocus}" />
                                                                    </StackPanel>
                                                                </UniformGrid>
```

- [ ] **Step 6: Verify build compiles**

Run: `dotnet build "E:\!!!Program to work on\Proper Crystal Relay\VrcTwitchOscBridge\VrcTwitchOscBridge.csproj" --no-restore`

Expected: Build succeeds.

---

### Task 9: Final Build Verification

- [ ] **Step 1: Clean build**

Run: `dotnet build "E:\!!!Program to work on\Proper Crystal Relay\VrcTwitchOscBridge\VrcTwitchOscBridge.csproj" --no-restore`

Expected: Build succeeds with no errors.

- [ ] **Step 2: Run localization audit**

Run the localization audit to check for missing keys.

- [ ] **Step 3: Verify no regressions**

Check that all 6 modes show their own transition seconds field, the live preview text works, and the preview block adapts to themes.
