# Streaming Status Illustration Redesign Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Replace the Streaming Status card's generic vector icon with a Void Crystal-themed crystal monitor illustration that changes appearance and pulses when live.

**Architecture:** All changes are in XAML (`MainWindow.xaml`). The new illustration uses WPF `Path` elements with `DataTrigger`-driven styles, plus `Storyboard` animations triggered only in the "Live" state. No ViewModel or localization changes are needed.

**Tech Stack:** WPF, XAML, .NET 10, C# (no code changes needed)

---

## Task 1: Remove Old Illustration Styles

**Files:**
- Modify: `VrcTwitchOscBridge/MainWindow.xaml:2151-2205` (remove old styles)
- Modify: `VrcTwitchOscBridge/MainWindow.xaml:2270-2306` (remove old illustration grid)

- [ ] **Step 1: Delete old styles**

Remove these four old styles from the card's `Border.Resources`:
- `StreamingStatusIllustrationBorderStyle` (lines 2151-2171)
- `StreamingStatusIllustrationShapeStyle` (lines 2173-2190)
- `StreamingStatusIndicatorDotStyle` (lines 2192-2205)

Keep the two chip styles (`StreamingStatusChipBorderStyle` and `StreamingStatusChipValueStyle`) — they are still needed.

- [ ] **Step 2: Delete old illustration grid**

Remove the entire illustration `Grid` (lines 2270-2306) inside the Streaming Status card. The old grid contains:
- The `Border` with `StreamingStatusIllustrationBorderStyle`
- The `Ellipse` (dot) and `Path` (screen shapes) elements

This leaves the `TextBlock` headers (lines 2247-2261) and the two chip `Border`s (lines 2309-2351) intact.

---

## Task 2: Add New Crystal Monitor Styles

**Files:**
- Modify: `VrcTwitchOscBridge/MainWindow.xaml` (add styles inside the card's `Border.Resources`)

- [ ] **Step 3: Add CrystalMonitorFrameStyle**

Add a new style for the outer frame `Path`:

```xml
<Style x:Key="CrystalMonitorFrameStyle" TargetType="Path">
    <Setter Property="Stroke" Value="{DynamicResource InputBorderBrush}" />
    <Setter Property="StrokeThickness" Value="2" />
    <Setter Property="StrokeLineJoin" Value="Round" />
    <Setter Property="Fill" Value="Transparent" />
    <Style.Triggers>
        <DataTrigger Binding="{Binding Tag, RelativeSource={RelativeSource Self}}" Value="Live">
            <Setter Property="Stroke" Value="{DynamicResource AccentBrush}" />
            <Setter Property="StrokeThickness" Value="2.5" />
        </DataTrigger>
        <DataTrigger Binding="{Binding Tag, RelativeSource={RelativeSource Self}}" Value="Healthy">
            <Setter Property="Stroke" Value="{DynamicResource AccentBrush}" />
        </DataTrigger>
        <DataTrigger Binding="{Binding Tag, RelativeSource={RelativeSource Self}}" Value="Checking">
            <Setter Property="Stroke" Value="{DynamicResource InputBorderBrush}" />
        </DataTrigger>
        <DataTrigger Binding="{Binding Tag, RelativeSource={RelativeSource Self}}" Value="Error">
            <Setter Property="Stroke" Value="{DynamicResource DangerBrush}" />
        </DataTrigger>
    </Style.Triggers>
</Style>
```

- [ ] **Step 4: Add CrystalMonitorInnerBezelStyle**

Add a style for the inner bezel glow path:

```xml
<Style x:Key="CrystalMonitorInnerBezelStyle" TargetType="Path">
    <Setter Property="Stroke" Value="{DynamicResource AccentBrush}" />
    <Setter Property="StrokeThickness" Value="1" />
    <Setter Property="StrokeLineJoin" Value="Round" />
    <Setter Property="Fill" Value="Transparent" />
    <Setter Property="Opacity" Value="0.3" />
    <Style.Triggers>
        <DataTrigger Binding="{Binding Tag, RelativeSource={RelativeSource Self}}" Value="Live">
            <Setter Property="Opacity" Value="0.6" />
        </DataTrigger>
        <DataTrigger Binding="{Binding Tag, RelativeSource={RelativeSource Self}}" Value="Error">
            <Setter Property="Stroke" Value="{DynamicResource DangerBrush}" />
            <Setter Property="Opacity" Value="0.3" />
        </DataTrigger>
    </Style.Triggers>
</Style>
```

- [ ] **Step 5: Add CrystalMonitorScreenStyle**

Add a style for the screen fill:

```xml
<Style x:Key="CrystalMonitorScreenStyle" TargetType="Path">
    <Setter Property="Fill" Value="{DynamicResource PanelBrush}" />
    <Setter Property="Stroke" Value="{DynamicResource InputBorderBrush}" />
    <Setter Property="StrokeThickness" Value="1.5" />
    <Style.Triggers>
        <DataTrigger Binding="{Binding Tag, RelativeSource={RelativeSource Self}}" Value="Live">
            <Setter Property="Fill" Value="{DynamicResource PanelHighlightBrush}" />
            <Setter Property="Stroke" Value="{DynamicResource AccentBrush}" />
        </DataTrigger>
        <DataTrigger Binding="{Binding Tag, RelativeSource={RelativeSource Self}}" Value="Error">
            <Setter Property="Stroke" Value="{DynamicResource DangerBrush}" />
        </DataTrigger>
    </Style.Triggers>
</Style>
```

- [ ] **Step 6: Add CrystalMonitorStandStyle**

Add a style for the stand pieces:

```xml
<Style x:Key="CrystalMonitorStandStyle" TargetType="Path">
    <Setter Property="Fill" Value="{DynamicResource PanelSecondaryBrush}" />
    <Setter Property="Stroke" Value="{DynamicResource InputBorderBrush}" />
    <Setter Property="StrokeThickness" Value="1.5" />
    <Style.Triggers>
        <DataTrigger Binding="{Binding Tag, RelativeSource={RelativeSource Self}}" Value="Live">
            <Setter Property="Fill" Value="{DynamicResource PanelHighlightBrush}" />
            <Setter Property="Stroke" Value="{DynamicResource AccentBrush}" />
        </DataTrigger>
        <DataTrigger Binding="{Binding Tag, RelativeSource={RelativeSource Self}}" Value="Error">
            <Setter Property="Stroke" Value="{DynamicResource DangerBrush}" />
        </DataTrigger>
    </Style.Triggers>
</Style>
```

- [ ] **Step 7: Add CrystalMonitorWebcamStyle**

Add a style for the webcam shard:

```xml
<Style x:Key="CrystalMonitorWebcamStyle" TargetType="Path">
    <Setter Property="Fill" Value="{DynamicResource PanelSecondaryBrush}" />
    <Setter Property="Stroke" Value="{DynamicResource InputBorderBrush}" />
    <Setter Property="StrokeThickness" Value="1.5" />
    <Style.Triggers>
        <DataTrigger Binding="{Binding Tag, RelativeSource={RelativeSource Self}}" Value="Live">
            <Setter Property="Fill" Value="{DynamicResource PanelHighlightBrush}" />
            <Setter Property="Stroke" Value="{DynamicResource AccentBrush}" />
            <Setter Property="StrokeThickness" Value="2" />
        </DataTrigger>
        <DataTrigger Binding="{Binding Tag, RelativeSource={RelativeSource Self}}" Value="Error">
            <Setter Property="Stroke" Value="{DynamicResource DangerBrush}" />
        </DataTrigger>
    </Style.Triggers>
</Style>
```

- [ ] **Step 8: Add CrystalMonitorGemDotStyle**

Add a style for the gem dot (the recording indicator):

```xml
<Style x:Key="CrystalMonitorGemDotStyle" TargetType="Ellipse">
    <Setter Property="Fill" Value="{DynamicResource InputBorderBrush}" />
    <Setter Property="Opacity" Value="0.4" />
    <Style.Triggers>
        <DataTrigger Binding="{Binding Tag, RelativeSource={RelativeSource Self}}" Value="Live">
            <Setter Property="Fill" Value="{DynamicResource AccentBrush}" />
            <Setter Property="Opacity" Value="1.0" />
        </DataTrigger>
        <DataTrigger Binding="{Binding Tag, RelativeSource={RelativeSource Self}}" Value="Healthy">
            <Setter Property="Fill" Value="{DynamicResource AccentBrush}" />
            <Setter Property="Opacity" Value="0.6" />
        </DataTrigger>
        <DataTrigger Binding="{Binding Tag, RelativeSource={RelativeSource Self}}" Value="Error">
            <Setter Property="Fill" Value="{DynamicResource DangerBrush}" />
        </DataTrigger>
    </Style.Triggers>
</Style>
```

- [ ] **Step 9: Add CrystalMonitorGemGlowStyle**

Add a style for the glow rings around the gem:

```xml
<Style x:Key="CrystalMonitorGemGlowStyle" TargetType="Ellipse">
    <Setter Property="Stroke" Value="{DynamicResource AccentBrush}" />
    <Setter Property="Fill" Value="Transparent" />
    <Setter Property="Opacity" Value="0.0" />
    <Style.Triggers>
        <DataTrigger Binding="{Binding Tag, RelativeSource={RelativeSource Self}}" Value="Live">
            <Setter Property="Opacity" Value="0.5" />
        </DataTrigger>
    </Style.Triggers>
</Style>
```

- [ ] **Step 10: Add CrystalMonitorUserStyle**

Add a style for the user silhouette elements:

```xml
<Style x:Key="CrystalMonitorUserStyle" TargetType="Shape">
    <Setter Property="Stroke" Value="{DynamicResource InputBorderBrush}" />
    <Setter Property="StrokeThickness" Value="2" />
    <Setter Property="StrokeStartLineCap" Value="Round" />
    <Setter Property="StrokeEndLineCap" Value="Round" />
    <Setter Property="StrokeLineJoin" Value="Round" />
    <Setter Property="Fill" Value="Transparent" />
    <Style.Triggers>
        <DataTrigger Binding="{Binding Tag, RelativeSource={RelativeSource Self}}" Value="Live">
            <Setter Property="Stroke" Value="{DynamicResource AccentBrush}" />
            <Setter Property="StrokeThickness" Value="2.5" />
        </DataTrigger>
        <DataTrigger Binding="{Binding Tag, RelativeSource={RelativeSource Self}}" Value="Healthy">
            <Setter Property="Stroke" Value="{DynamicResource AccentBrush}" />
        </DataTrigger>
        <DataTrigger Binding="{Binding Tag, RelativeSource={RelativeSource Self}}" Value="Error">
            <Setter Property="Stroke" Value="{DynamicResource DangerBrush}" />
        </DataTrigger>
    </Style.Triggers>
</Style>
```

- [ ] **Step 11: Add CrystalMonitorLiveBadgeStyle**

Add a style for the LIVE badge path:

```xml
<Style x:Key="CrystalMonitorLiveBadgeStyle" TargetType="Path">
    <Setter Property="Fill" Value="{DynamicResource DangerBrush}" />
    <Setter Property="Stroke" Value="{DynamicResource DangerBorderBrush}" />
    <Setter Property="StrokeThickness" Value="1.5" />
    <Setter Property="Visibility" Value="Collapsed" />
    <Style.Triggers>
        <DataTrigger Binding="{Binding Tag, RelativeSource={RelativeSource Self}}" Value="Live">
            <Setter Property="Visibility" Value="Visible" />
        </DataTrigger>
    </Style.Triggers>
</Style>
```

- [ ] **Step 12: Add CrystalMonitorLiveBadgeTextStyle**

Add a style for the LIVE badge text:

```xml
<Style x:Key="CrystalMonitorLiveBadgeTextStyle" TargetType="TextBlock">
    <Setter Property="Foreground" Value="{DynamicResource TextBrush}" />
    <Setter Property="FontSize" Value="7" />
    <Setter Property="FontWeight" Value="Bold" />
    <Setter Property="FontFamily" Value="{DynamicResource BodyFontFamily}" />
    <Setter Property="Visibility" Value="Collapsed" />
    <Style.Triggers>
        <DataTrigger Binding="{Binding Tag, RelativeSource={RelativeSource Self}}" Value="Live">
            <Setter Property="Visibility" Value="Visible" />
        </DataTrigger>
    </Style.Triggers>
</Style>
```

- [ ] **Step 13: Add CrystalMonitorTopDotStyle**

Add a style for the top-right crystal dot:

```xml
<Style x:Key="CrystalMonitorTopDotStyle" TargetType="Path">
    <Setter Property="Fill" Value="{DynamicResource InputBorderBrush}" />
    <Setter Property="Stroke" Value="{DynamicResource InputBorderBrush}" />
    <Setter Property="StrokeThickness" Value="1" />
    <Style.Triggers>
        <DataTrigger Binding="{Binding Tag, RelativeSource={RelativeSource Self}}" Value="Live">
            <Setter Property="Fill" Value="{DynamicResource AccentBrush}" />
            <Setter Property="Stroke" Value="{DynamicResource AccentBrush}" />
        </DataTrigger>
        <DataTrigger Binding="{Binding Tag, RelativeSource={RelativeSource Self}}" Value="Healthy">
            <Setter Property="Fill" Value="{DynamicResource AccentBrush}" />
            <Setter Property="Stroke" Value="{DynamicResource AccentBrush}" />
        </DataTrigger>
        <DataTrigger Binding="{Binding Tag, RelativeSource={RelativeSource Self}}" Value="Error">
            <Setter Property="Fill" Value="{DynamicResource DangerBrush}" />
            <Setter Property="Stroke" Value="{DynamicResource DangerBrush}" />
        </DataTrigger>
    </Style.Triggers>
</Style>
```

- [ ] **Step 14: Add CrystalMonitorOuterGlowStyle**

Add a style for the outer ambient glow:

```xml
<Style x:Key="CrystalMonitorOuterGlowStyle" TargetType="Path">
    <Setter Property="Stroke" Value="{DynamicResource AccentBrush}" />
    <Setter Property="StrokeThickness" Value="4" />
    <Setter Property="StrokeLineJoin" Value="Round" />
    <Setter Property="Fill" Value="Transparent" />
    <Setter Property="Opacity" Value="0.0" />
    <Style.Triggers>
        <DataTrigger Binding="{Binding Tag, RelativeSource={RelativeSource Self}}" Value="Live">
            <Setter Property="Opacity" Value="0.15" />
        </DataTrigger>
    </Style.Triggers>
</Style>
```

---

## Task 3: Add Animation Resources

**Files:**
- Modify: `VrcTwitchOscBridge/MainWindow.xaml` (add Storyboards inside the card's `Border.Resources`)

- [ ] **Step 15: Add GemPulse Storyboard**

Add a `Storyboard` resource for the gem pulse animation:

```xml
<Storyboard x:Key="GemPulseStoryboard" RepeatBehavior="Forever" AutoReverse="True">
    <DoubleAnimation
        Storyboard.TargetName="GemDot"
        Storyboard.TargetProperty="Opacity"
        From="0.35" To="1.0" Duration="0:0:2.5"
        EasingFunction="{StaticResource CubicEaseInOut}" />
    <DoubleAnimation
        Storyboard.TargetName="GemGlowRing1"
        Storyboard.TargetProperty="Opacity"
        From="0.25" To="0.7" Duration="0:0:2.5"
        EasingFunction="{StaticResource CubicEaseInOut}" />
    <DoubleAnimation
        Storyboard.TargetName="GemGlowRing2"
        Storyboard.TargetProperty="Opacity"
        From="0.12" To="0.35" Duration="0:0:2.5"
        EasingFunction="{StaticResource CubicEaseInOut}" />
    <DoubleAnimation
        Storyboard.TargetName="GemGlowRing3"
        Storyboard.TargetProperty="Opacity"
        From="0.05" To="0.15" Duration="0:0:2.5"
        EasingFunction="{StaticResource CubicEaseInOut}" />
    <DoubleAnimation
        Storyboard.TargetName="TopRightGlowRing1"
        Storyboard.TargetProperty="Opacity"
        From="0.15" To="0.5" Duration="0:0:2.5"
        EasingFunction="{StaticResource CubicEaseInOut}" />
    <DoubleAnimation
        Storyboard.TargetName="TopRightGlowRing2"
        Storyboard.TargetProperty="Opacity"
        From="0.05" To="0.2" Duration="0:0:2.5"
        EasingFunction="{StaticResource CubicEaseInOut}" />
</Storyboard>
```

> **Note:** If `CubicEaseInOut` is not defined as a static resource, define it inline:
> ```xml
> <EasingFunction><CubicEase EasingMode="EaseInOut"/></EasingFunction>
> ```

---

## Task 4: Add New Illustration Grid

**Files:**
- Modify: `VrcTwitchOscBridge/MainWindow.xaml` (replace the old illustration grid)

- [ ] **Step 16: Add the new Crystal Monitor illustration grid**

Insert the new illustration `Grid` where the old one was (after the text block StackPanel, before the chip Grid). The grid container is `110×90` (up from `92×78`):

```xml
<Grid Grid.Column="1"
      Width="110"
      Height="90"
      Margin="16,0,0,0"
      HorizontalAlignment="Right"
      VerticalAlignment="Top">
    <Viewbox Stretch="Uniform">
        <Grid Width="220" Height="170">
            <!-- Outer ambient glow -->
            <Path Data="M20 40 L40 20 L180 20 L200 40 L200 120 L180 140 L40 140 L20 120 Z"
                  Style="{StaticResource CrystalMonitorOuterGlowStyle}"
                  Tag="{Binding StreamingStatusVisualState}" />

            <!-- Outer frame -->
            <Path Data="M20 40 L40 20 L180 20 L200 40 L200 120 L180 140 L40 140 L20 120 Z"
                  Style="{StaticResource CrystalMonitorFrameStyle}"
                  Tag="{Binding StreamingStatusVisualState}" />

            <!-- Inner bezel glow -->
            <Path Data="M24 42 L42 24 L178 24 L196 42 L196 118 L178 136 L42 136 L24 118 Z"
                  Style="{StaticResource CrystalMonitorInnerBezelStyle}"
                  Tag="{Binding StreamingStatusVisualState}" />

            <!-- Screen -->
            <Path Data="M30 48 L46 32 L174 32 L190 48 L190 112 L174 128 L46 128 L30 112 Z"
                  Style="{StaticResource CrystalMonitorScreenStyle}"
                  Tag="{Binding StreamingStatusVisualState}" />

            <!-- Stand (centered) -->
            <Path Data="M95 140 L125 140 L110 152 Z"
                  Style="{StaticResource CrystalMonitorStandStyle}"
                  Tag="{Binding StreamingStatusVisualState}" />
            <Path Data="M80 152 L140 152 L130 162 L90 162 Z"
                  Style="{StaticResource CrystalMonitorStandStyle}"
                  Tag="{Binding StreamingStatusVisualState}" />

            <!-- Webcam crystal shard -->
            <Path Data="M100 12 L110 4 L120 12 L115 20 L105 20 Z"
                  Style="{StaticResource CrystalMonitorWebcamStyle}"
                  Tag="{Binding StreamingStatusVisualState}" />

            <!-- Gem dot (centered in webcam) -->
            <Ellipse x:Name="GemDot"
                     Width="6" Height="6"
                     Margin="0,0,0,5"
                     HorizontalAlignment="Center"
                     VerticalAlignment="Top"
                     Style="{StaticResource CrystalMonitorGemDotStyle}"
                     Tag="{Binding StreamingStatusVisualState}" />

            <!-- Gem glow ring 1 -->
            <Ellipse x:Name="GemGlowRing1"
                     Width="14" Height="14"
                     Margin="0,0,0,5"
                     HorizontalAlignment="Center"
                     VerticalAlignment="Top"
                     Style="{StaticResource CrystalMonitorGemGlowStyle}" />

            <!-- Gem glow ring 2 -->
            <Ellipse x:Name="GemGlowRing2"
                     Width="22" Height="22"
                     Margin="0,0,0,5"
                     HorizontalAlignment="Center"
                     VerticalAlignment="Top"
                     Style="{StaticResource CrystalMonitorGemGlowStyle}" />

            <!-- Gem glow ring 3 -->
            <Ellipse x:Name="GemGlowRing3"
                     Width="30" Height="30"
                     Margin="0,0,0,5"
                     HorizontalAlignment="Center"
                     VerticalAlignment="Top"
                     Style="{StaticResource CrystalMonitorGemGlowStyle}" />

            <!-- User silhouette -->
            <Ellipse Width="24" Height="24"
                     Margin="0,0,0,6"
                     HorizontalAlignment="Center"
                     VerticalAlignment="Center"
                     Style="{StaticResource CrystalMonitorUserStyle}"
                     Tag="{Binding StreamingStatusVisualState}" />
            <Path Data="M95 88 Q110 75 125 88"
                  Style="{StaticResource CrystalMonitorUserStyle}"
                  Tag="{Binding StreamingStatusVisualState}" />
            <Path Data="M88 98 Q110 82 132 98"
                  Style="{StaticResource CrystalMonitorUserStyle}"
                  Tag="{Binding StreamingStatusVisualState}" />

            <!-- LIVE badge -->
            <Path Data="M160 8 L170 2 L180 8 L175 16 L165 16 Z"
                  Style="{StaticResource CrystalMonitorLiveBadgeStyle}"
                  Tag="{Binding StreamingStatusVisualState}" />
            <TextBlock Text="LIVE"
                       Margin="0,0,0,0"
                       HorizontalAlignment="Right"
                       VerticalAlignment="Top"
                       Style="{StaticResource CrystalMonitorLiveBadgeTextStyle}"
                       Tag="{Binding StreamingStatusVisualState}" />

            <!-- Top-right crystal dot -->
            <Path Data="M175 28 L180 22 L185 28 L180 34 Z"
                  Style="{StaticResource CrystalMonitorTopDotStyle}"
                  Tag="{Binding StreamingStatusVisualState}" />

            <!-- Top-right glow rings -->
            <Ellipse x:Name="TopRightGlowRing1"
                     Width="12" Height="12"
                     Margin="0,0,0,0"
                     HorizontalAlignment="Right"
                     VerticalAlignment="Top"
                     Style="{StaticResource CrystalMonitorGemGlowStyle}" />
            <Ellipse x:Name="TopRightGlowRing2"
                     Width="20" Height="20"
                     Margin="0,0,0,0"
                     HorizontalAlignment="Right"
                     VerticalAlignment="Top"
                     Style="{StaticResource CrystalMonitorGemGlowStyle}" />

            <!-- Animation trigger -->
            <Grid.Style>
                <Style TargetType="Grid">
                    <Style.Triggers>
                        <DataTrigger Binding="{Binding StreamingStatusVisualState}" Value="Live">
                            <DataTrigger.EnterActions>
                                <BeginStoryboard Storyboard="{StaticResource GemPulseStoryboard}" />
                            </DataTrigger.EnterActions>
                            <DataTrigger.ExitActions>
                                <StopStoryboard BeginStoryboardName="GemPulseStoryboard" />
                            </DataTrigger.ExitActions>
                        </DataTrigger>
                    </Style.Triggers>
                </Style>
            </Grid.Style>
        </Grid>
    </Viewbox>
</Grid>
```

> **Note:** The `Viewbox` wrapper ensures the `220×170` design grid scales down to fit the `110×90` container without needing manual `RenderTransform` scaling.

---

## Task 5: Build and Test

**Files:**
- Test: `VrcTwitchOscBridge/MainWindow.xaml`

- [ ] **Step 17: Build the project**

Run:
```powershell
dotnet build "E:\!!!Program to work on\Proper Crystal Relay\VrcTwitchOscBridge\VrcTwitchOscBridge.csproj" --no-restore
```

Expected: Build succeeds with no XAML errors.

- [ ] **Step 18: Launch the debug build**

Run:
```powershell
& "E:\!!!Program to work on\Proper Crystal Relay\Launch-Crystal-Relay-Debug.bat"
```

Expected: App opens, the Streaming Status card shows the new crystal monitor illustration.

- [ ] **Step 19: Verify all visual states**

Check the Streaming Status card in these states:
1. **Disconnected** — frame should be muted, gem dimmed, no LIVE badge
2. **Connecting** — frame should be in "Checking" state (input border color)
3. **Error** — frame should use DangerBrush, no pulse
4. **Offline (Healthy)** — frame should be accent-colored, gem at 0.6 opacity, no pulse
5. **Live** — frame should be bright accent, gem pulsing, LIVE badge visible

> **Tip:** You can force states by temporarily editing `RefreshStreamingStatusCard()` in `MainWindowViewModel.cs` to call `SetStreamingStatusCard()` with each state, then revert.

---

## Task 6: Localization Audit

**Files:**
- Run: `LocalizationAudit` project

- [ ] **Step 20: Run localization audit**

Run:
```powershell
powershell -ExecutionPolicy Bypass -File "E:\!!!Program to work on\Proper Crystal Relay\Build-Crystal-Relay-Test.ps1" -Version 3.1.9
```

> **Note:** The build script automatically runs the localization audit. Alternatively, run the audit project directly if available.

Expected: No missing localization keys, no new keys needed.

---

## Task 7: Commit

- [ ] **Step 21: Stage and commit**

```bash
git add VrcTwitchOscBridge/MainWindow.xaml
git add docs/superpowers/specs/2026-06-07-streaming-status-illustration-redesign.md
git add docs/superpowers/plans/2026-06-07-streaming-status-illustration-redesign.md
git commit -m "feat: redesign Streaming Status illustration with Void Crystal monitor"
```

---

## Spec Coverage Check

| Spec Requirement | Implementing Task |
|------------------|-------------------|
| Octagonal crystal-cut frame | Task 2, Step 3 |
| Inner bezel glow | Task 2, Step 4 |
| Screen area | Task 2, Step 5 |
| Crystal webcam | Task 2, Step 7 |
| Gem dot with pulse | Task 2, Steps 8-9, Task 3, Step 15 |
| User silhouette | Task 2, Step 10 |
| Centered crystal stand | Task 2, Step 6, Task 4, Step 16 |
| LIVE badge | Task 2, Steps 11-12 |
| Top-right crystal dot | Task 2, Step 13 |
| Ambient outer glow | Task 2, Step 14 |
| Icon size 110×90 | Task 4, Step 16 |
| Animation 2.5s cycle | Task 3, Step 15 |
| All 5 states covered | Task 2, all styles (DataTriggers for each state) |
| No ViewModel changes | Not needed (verified in Task 1) |
| No localization changes | Task 6, Step 20 |

## Placeholder Scan

- No "TBD" or "TODO" found
- No "implement later" or "fill in details"
- All code blocks contain complete XAML
- All file paths are exact
- All commands include expected output
- No "Similar to Task N" references

## Type Consistency Check

- `StreamingStatusVisualState` is used consistently as the `Tag` binding source across all styles
- `Storyboard` names (`GemPulseStoryboard`) match between definition and usage
- `x:Name` references (`GemDot`, `GemGlowRing1`, etc.) match between XAML and Storyboard targets
- No mismatched property names or types found

---

**Plan saved to:** `docs/superpowers/plans/2026-06-07-streaming-status-illustration-redesign.md`

**Two execution options:**

1. **Subagent-Driven (recommended)** — I dispatch a fresh subagent per task, review between tasks, fast iteration
2. **Inline Execution** — Execute tasks in this session using executing-plans, batch execution with checkpoints

Which approach?