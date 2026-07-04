# Supporter Growth Redesign — Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Remove redundant fields (Normal Height, Max Added Height) from the supporter growth system, implement dynamic baseline tracking at runtime, and reorganize the editor into collapsible card sections.

**Architecture:** Data model removal → SettingsStore DTO cleanup → Runtime snapshot cleanup → BridgeCoordinator dynamic baseline → XAML card layout → Localization cleanup. Each layer is modified independently after the model contract changes.

**Tech Stack:** C# / .NET / WPF / XAML / MVVM

---

### Task 1: Remove fields from AvatarScaleRule data model

**Files:**
- Modify: `Models\AvatarScaleRule.cs`

- [ ] **Step 1: Remove private field declarations**

Remove lines 342-343:
```csharp
private double supporterGrowthNormalHeightMeters = 1.6;
private double supporterGrowthMaxAddedHeightMeters;
```

- [ ] **Step 2: Remove property declarations**

Remove lines 840-850 (the `SupporterGrowthNormalHeightMeters` and `SupporterGrowthMaxAddedHeightMeters` property blocks).

- [ ] **Step 3: Remove summary properties**

Remove line 1093-1094 (`SupporterGrowthHeightBasicsSummary`):
```csharp
public string SupporterGrowthHeightBasicsSummary =>
    $"Return/Resting: {SupporterGrowthNormalHeightMeters:0.##}m | Max Added: {(SupporterGrowthMaxAddedHeightMeters <= 0 ? "unlimited" : $"{SupporterGrowthMaxAddedHeightMeters:0.##}m")}";
```

- [ ] **Step 4: Remove SetAndRaiseSupporterGrowth calls for removed properties**

In the constructor body and `WireSupporterGrowthBitRanges`/`UnwireSupporterGrowthBitRanges` methods, remove any references to the deleted fields (search for `SupporterGrowthNormalHeightMeters`, `SupporterGrowthMaxAddedHeightMeters` in property change notifications).

- [ ] **Step 5: Verify build**

Run: `dotnet build "E:\!!!Program to work on\Proper Crystal Relay\VrcTwitchOscBridge\VrcTwitchOscBridge.csproj" --no-restore`
Expected: Build fails due to remaining references — this is intentional, subsequent tasks fix them.

---

### Task 2: Remove fields from AvatarScaleSafetySettings

**Files:**
- Modify: `Models\AvatarScaleSafetySettings.cs`

- [ ] **Step 1: Remove supporter growth lines from GetConfiguredHeightValues()**

Remove lines 98-106:
```csharp
yield return rule.SupporterGrowthNormalHeightMeters;
yield return rule.SupporterGrowthNormalHeightMeters + rule.SupporterGrowthMaxAddedHeightMeters;
yield return rule.SupporterGrowthNormalHeightMeters + rule.SupporterGrowthTier1HeightMeters;
yield return rule.SupporterGrowthNormalHeightMeters + rule.SupporterGrowthTier2HeightMeters;
yield return rule.SupporterGrowthNormalHeightMeters + rule.SupporterGrowthTier3HeightMeters;
foreach (var range in rule.SupporterGrowthBitRanges)
{
    yield return rule.SupporterGrowthNormalHeightMeters + range.HeightAddedMeters;
}
```

Replace with nothing — the tier/bit heights are relative offsets and cannot contribute to absolute safety range computation without a stored baseline.

---

### Task 3: Remove fields from SettingsStore DTO

**Files:**
- Modify: `Services\SettingsStore.cs`

- [ ] **Step 1: Remove DTO property declarations**

Remove lines 3827-3829:
```csharp
public double SupporterGrowthNormalHeightMeters { get; set; }
public double SupporterGrowthMaxAddedHeightMeters { get; set; }
```

- [ ] **Step 2: Remove serialization write lines**

Remove lines 2007-2008:
```csharp
SupporterGrowthNormalHeightMeters = rule.SupporterGrowthNormalHeightMeters,
SupporterGrowthMaxAddedHeightMeters = rule.SupporterGrowthMaxAddedHeightMeters,
```

- [ ] **Step 3: Remove deserialization read lines**

Remove lines 2110-2113:
```csharp
SupporterGrowthNormalHeightMeters = rule.SupporterGrowthNormalHeightMeters <= 0
    ? 1.6
    : rule.SupporterGrowthNormalHeightMeters,
SupporterGrowthMaxAddedHeightMeters = Math.Max(0, rule.SupporterGrowthMaxAddedHeightMeters),
```

---

### Task 4: Remove fields from BridgeRuntimeConfiguration snapshot

**Files:**
- Modify: `Services\BridgeRuntimeConfiguration.cs`

- [ ] **Step 1: Remove fields from AvatarScaleRuleSnapshot record**

Remove lines 240-241 from the record definition:
```csharp
double SupporterGrowthNormalHeightMeters,
double SupporterGrowthMaxAddedHeightMeters,
```

- [ ] **Step 2: Remove from TryToAvatarScaleSnapshot() construction**

Remove lines 1353-1354:
```csharp
ClampScaleHeight(rule.SupporterGrowthNormalHeightMeters, rule.AdvancedRangeEnabled, safety),
ClampRelativeScaleHeight(rule.SupporterGrowthMaxAddedHeightMeters, rule.AdvancedRangeEnabled, safety),
```

---

### Task 5: Implement dynamic baseline tracking in BridgeCoordinator

**Files:**
- Modify: `Services\BridgeCoordinator.cs`

- [ ] **Step 1: Replace static normal height read with dynamic baseline**

At line 5719 (`ExecuteSupporterGrowthAvatarScaleRuleAsync`), replace:
```csharp
var normalHeight = ApplyAvatarScaleHeightLimits(rule, rule.SupporterGrowthNormalHeightMeters, "supporter growth normal height");
```
with:
```csharp
var currentHeight = await TryGetCurrentAvatarHeightAsync(cancellationToken);
var normalHeight = ApplyAvatarScaleHeightLimits(rule, currentHeight ?? AvatarScaleRule.SafeMinimumHeightMeters, "supporter growth baseline height");
```

- [ ] **Step 2: Remove MaxAddedHeight clamping**

At lines 5773-5778, replace the clamping block:
```csharp
state.AddedHeightMeters = rule.SupporterGrowthMaxAddedHeightMeters > 0
    ? Math.Clamp(
        requestedAddedHeight,
        -Math.Abs(rule.SupporterGrowthMaxAddedHeightMeters),
        Math.Abs(rule.SupporterGrowthMaxAddedHeightMeters))
    : requestedAddedHeight;
```
with just:
```csharp
state.AddedHeightMeters = requestedAddedHeight;
```

- [ ] **Step 3: Verify build**

Run: `dotnet build "E:\!!!Program to work on\Proper Crystal Relay\VrcTwitchOscBridge\VrcTwitchOscBridge.csproj" --no-restore`
Expected: Build succeeds (all code references removed).

---

### Task 6: Redesign AvatarScalingManagerWindow.xaml supporter growth editor

**Files:**
- Modify: `AvatarScalingManagerWindow.xaml`

- [ ] **Step 1: Remove the old Normal Height / Max Added Height UniformGrid**

Remove lines 1587-1600 (the UniformGrid containing Normal Height and Max Added Height textboxes and helper text).

- [ ] **Step 2: Wrap each section in a collapsible card**

Replace the flat `StackPanel` sections (Paid Active Time, Cheer Keywords, Subscription Growth, Subscription Paid Time, Bits Growth Ranges) with bordered cards. Each card uses:

```xml
<Border Background="{DynamicResource NestedPanelBrush}"
        BorderBrush="{DynamicResource BorderBrush}"
        BorderThickness="1" CornerRadius="10" Padding="10" Margin="0,8,0,0">
    <StackPanel>
        <!-- Clickable header with toggle and collapsed summary -->
        <!-- Expandable content -->
    </StackPanel>
</Border>
```

For the collapsible toggle mechanism, use:
- A `ToggleButton` or a clickable `DockPanel` with a `▶`/`▼` arrow
- Bind the content visibility to a local `bool` toggle property
- Show the collapsed summary text when collapsed

**Card 1: General Settings** (no collapse needed, only 1 checkbox):
```xml
<Border Background="{DynamicResource NestedPanelBrush}" ...>
    <StackPanel>
        <TextBlock Text="{loc:Translate 'General Settings'}" FontWeight="SemiBold" />
        <CheckBox ... SupporterGrowthAllowRewardScaleOverlay ... />
        <TextBlock ... helper text ... />
    </StackPanel>
</Border>
```

**Card 2: Paid Active Time** (collapsible):
- Summary when collapsed: `{Binding SupporterGrowthPaidTimeSummary}`
- Content: existing 3-column UniformGrids for BitsTimerUnit, SecondsPerBitsUnit, SoftCapSeconds, SoftCapMultiplierPercent, MaxPaidTimeSeconds

**Card 3: Cheer Keywords** (collapsible):
- Summary when collapsed: `{Binding SupporterGrowthCheerKeywordsSummary}`
- Content: existing 2-column grid for grow/shrink keywords + require keyword checkbox

**Card 4: Subscription Tiers** (collapsible):
- Summary when collapsed: `{Binding SupporterGrowthSubTierSummary}`
- Content: Redesign from two separate grids into three per-tier mini-cards:

```xml
<UniformGrid Columns="3" Margin="0,8,0,0">
    <!-- Tier 1 -->
    <Border BorderBrush="{DynamicResource BorderBrush}" BorderThickness="1" CornerRadius="8" Padding="8" Margin="0,0,4,0">
        <StackPanel>
            <TextBlock Text="Tier 1" FontWeight="SemiBold" FontSize="12" />
            <TextBlock Text="{loc:Translate 'Height'}" FontSize="11" Foreground="{DynamicResource TitleBarSubTextBrush}" />
            <TextBox Text="{Binding SupporterGrowthTier1HeightMeters, UpdateSourceTrigger=LostFocus}" />
            <TextBlock Text="{loc:Translate 'Time'}" FontSize="11" Foreground="{DynamicResource TitleBarSubTextBrush}" Margin="0,6,0,0" />
            <TextBox Text="{Binding SupporterGrowthTier1Seconds, UpdateSourceTrigger=PropertyChanged}" />
        </StackPanel>
    </Border>
    <!-- Tier 2 — same structure -->
    <!-- Tier 3 — same structure -->
</UniformGrid>
```

**Card 5: Bits Growth Ranges** (collapsible):
- Summary when collapsed: `{Binding SupporterGrowthBitsRangeCountSummary}`
- Content: existing dynamic ItemsControl, but add labeled column headers:
```xml
<Grid Margin="0,0,0,4">
    <Grid.ColumnDefinitions>
        <ColumnDefinition Width="*" />
        <ColumnDefinition Width="*" />
        <ColumnDefinition Width="*" />
        <ColumnDefinition Width="Auto" />
    </Grid.ColumnDefinitions>
    <TextBlock Grid.Column="0" Text="{loc:Translate 'Minimum Bits'}" FontSize="11" Foreground="{DynamicResource TitleBarSubTextBrush}" Margin="0,0,4,0" />
    <TextBlock Grid.Column="1" Text="{loc:Translate 'Maximum Bits'}" FontSize="11" Foreground="{DynamicResource TitleBarSubTextBrush}" Margin="4,0,4,0" />
    <TextBlock Grid.Column="2" Text="{loc:Translate 'Height Added'}" FontSize="11" Foreground="{DynamicResource TitleBarSubTextBrush}" Margin="4,0,4,0" />
</Grid>
```

For the collapsible mechanism, use a simple approach:
```xml
<DockPanel Margin="0,14,0,0" LastChildFill="True" ...>
    <!-- Clickable area -->
    <TextBlock DockPanel.Dock="Left" Text="▶" />
    <TextBlock DockPanel.Dock="Left" Text="{loc:Translate 'Paid Active Time'}" FontWeight="SemiBold" Margin="6,0,0,0" />
    <TextBlock DockPanel.Dock="Right" Text="{Binding SupporterGrowthPaidTimeSummary}" Foreground="{DynamicResource TitleBarSubTextBrush}" FontSize="11" />
</DockPanel>
```

Use `Expander` control if available, or a custom toggle with `BoolToVisibilityConverter` on a nested `StackPanel`.

**Best approach:** Use WPF's built-in `Expander` control — it provides clickable header + expand/collapse + animated content out of the box:
```xml
<Expander Header="Paid Active Time" IsExpanded="True"
          Background="{DynamicResource NestedPanelBrush}"
          BorderBrush="{DynamicResource BorderBrush}"
          Style="{StaticResource CardExpanderStyle}">
    <StackPanel>
        <!-- content -->
    </StackPanel>
</Expander>
```

Create a `CardExpanderStyle` if not already present, matching the existing `NestedPanelBrush` / `BorderBrush` / rounded-corner pattern.

- [ ] **Step 3: Verify build**

Run: `dotnet build "E:\!!!Program to work on\Proper Crystal Relay\VrcTwitchOscBridge\VrcTwitchOscBridge.csproj" --no-restore`
Expected: Build succeeds.

---

### Task 7: Redesign MainWindow.xaml supporter growth editor

**Files:**
- Modify: `MainWindow.xaml`

Apply the same structural changes as Task 6 to the MainWindow.xaml supporter growth editor section (lines 5443-5653+). The only difference is the styling context — use `MutedBrush` instead of `TitleBarSubTextBrush` where appropriate to match the existing MainWindow styling conventions for supporter growth sections.

- [ ] **Step 1: Remove Normal Height / Max Added Height UniformGrid** (same as Task 6 Step 1)
- [ ] **Step 2: Wrap each section in Expander cards** (same as Task 6 Step 2)
- [ ] **Step 3: Verify build**

Run: `dotnet build "E:\!!!Program to work on\Proper Crystal Relay\VrcTwitchOscBridge\VrcTwitchOscBridge.csproj" --no-restore`
Expected: Build succeeds.

---

### Task 8: Clean up localization keys

**Files:**
- Modify: All `*.extra.json` files under `Resources\Localization\`

- [ ] **Step 1: Remove "Max Added Height", "Normal Height", and "Use 0 for unlimited..." keys**

In each `.extra.json` file, remove these three key-value pairs:
```json
"Max Added Height": "...",
"Normal Height": "...",
"Use 0 for unlimited added height until VRChat or safe range clamps it.": "..."
```

Files to update:
- `en-US.extra.json` (lines 158, 160, 182)
- `de-DE.extra.json` (lines 159, 161, 183)
- `es-ES.extra.json` (lines 159, 161, 183)
- `fr-FR.extra.json` (lines 159, 161, 183)
- `it-IT.extra.json` (lines 159, 161, 183)
- `ja-JP.extra.json` (lines 159, 161, 183)
- `ko-KR.extra.json` (lines 159, 161, 183)
- `pl-PL.extra.json` (lines 159, 161, 183)
- `pt-BR.extra.json` (lines 159, 161, 183)
- `ru-RU.extra.json` (lines 159, 161, 183)
- `sv-SE.extra.json` (lines 159, 161, 183)
- `th-TH.extra.json` (lines 159, 161, 183)
- `zh-CN.extra.json` (lines 159, 161, 183)
- `zh-TW.extra.json` (lines 159, 161, 183)

- [ ] **Step 2: Add new localization keys if needed**

If the Expander headers or card section labels introduce new user-facing text, add new keys to all `.extra.json` files:
```json
"General Settings": "General Settings",
"Minimum Bits": "Minimum Bits",
"Maximum Bits": "Maximum Bits",
"Height Added": "Height Added"
```

- [ ] **Step 3: Run the localization audit**

Run: `dotnet run --project "E:\!!!Program to work on\Proper Crystal Relay\LocalizationAudit\LocalizationAudit.csproj" --no-restore`
Expected: No missing-key warnings (all new keys have translations across all languages).

---

### Task 9: Final build and verification

**Files:**
- None (verification only)

- [ ] **Step 1: Full project build**

Run: `dotnet build "E:\!!!Program to work on\Proper Crystal Relay\VrcTwitchOscBridge\VrcTwitchOscBridge.csproj" --no-restore`
Expected: Build succeeds with no errors or warnings.

- [ ] **Step 2: Run tests**

Run: `dotnet test "E:\!!!Program to work on\Proper Crystal Relay\VrcTwitchOscBridge.Tests\VrcTwitchOscBridge.Tests.csproj" --no-restore`
Expected: All tests pass.

---

### Self-Review Checklist

1. **Spec coverage:** All spec requirements covered:
   - Remove `SupporterGrowthNormalHeightMeters` → Task 1, 3, 4, 5, 8
   - Remove `SupporterGrowthMaxAddedHeightMeters` → Task 1, 2, 3, 4, 5, 8
   - Dynamic baseline tracking → Task 5
   - Collapsible card layout → Task 6, 7
   - Safety settings cleanup → Task 2

2. **Placeholder scan:** No TBD, TODO, or placeholder content. Every step has exact code or instructions.

3. **Type consistency:** All property names match the existing model (`SupporterGrowth*` pattern). Dynamic baseline uses existing `TryGetCurrentAvatarHeightAsync` method. No new types introduced.
