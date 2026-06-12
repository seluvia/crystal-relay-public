# Supporter Growth UI Rework Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Reorganize the Supporter Growth panel from a flat 15+ field layout into 5 collapsible sections with summaries, fix the broken SmoothTransitionSeconds binding, rename confusing labels, expose the hidden inactivity timeout field, and fix spelling/spacing issues in localization.

**Architecture:** XAML-only UI restructure using existing WPF `Expander` pattern. New computed summary properties added to `AvatarScaleRule.cs`. Localization keys added to all `*.extra.json` files. No runtime logic changes.

**Tech Stack:** C#, WPF, XAML, .NET 10

---

## File Map

| File | Role |
|---|---|
| `VrcTwitchOscBridge\Models\AvatarScaleRule.cs` | Add 5 computed summary properties, wire them into `RaiseSupporterGrowthProperties` |
| `VrcTwitchOscBridge\MainWindow.xaml` | Rewrite lines 6672-6909: 5 collapsible `Expander` sections with summaries |
| `VrcTwitchOscBridge\Resources\Localization\en-US.extra.json` | Add new keys, rename labels, fix outdated description, remove old key |
| `VrcTwitchOscBridge\Resources\Localization\{lang}.extra.json` (13 files) | Add placeholder translations for new keys |

---

### Task 1: Add Computed Summary Properties to AvatarScaleRule.cs

**Files:**
- Modify: `VrcTwitchOscBridge\Models\AvatarScaleRule.cs:1063-1064` (after `SupporterGrowthSummary`)
- Modify: `VrcTwitchOscBridge\Models\AvatarScaleRule.cs:1238-1243` (`RaiseSupporterGrowthProperties`)

- [ ] **Step 1: Add 5 new computed summary properties after `SupporterGrowthSummary`**

Insert after line 1064 (`SupporterGrowthSummary`):

```csharp
    public string SupporterGrowthHeightBasicsSummary =>
        $"Normal: {SupporterGrowthNormalHeightMeters:0.##}m | Max Added: {(SupporterGrowthMaxAddedHeightMeters <= 0 ? "unlimited" : $"{SupporterGrowthMaxAddedHeightMeters:0.##}m")}";

    public string SupporterGrowthPaidTimeSummary =>
        $"{SupporterGrowthBitsTimerUnit} bits = {SupporterGrowthSecondsPerBitsUnit}s | Soft cap: {SupporterGrowthSoftCapSeconds}s @ {SupporterGrowthSoftCapMultiplierPercent}% | Max: {SupporterGrowthMaxPaidTimeSeconds}s";

    public string SupporterGrowthSubTierSummary =>
        $"T1: +{SupporterGrowthTier1HeightMeters:0.##}m / {SupporterGrowthTier1Seconds}s | T2: +{SupporterGrowthTier2HeightMeters:0.##}m / {SupporterGrowthTier2Seconds}s | T3: +{SupporterGrowthTier3HeightMeters:0.##}m / {SupporterGrowthTier3Seconds}s";

    public string SupporterGrowthBitsRangeCountSummary =>
        $"{SupporterGrowthBitRanges.Count} range(s) configured";

    public string SupporterGrowthCheerKeywordsSummary =>
        $"{SupporterGrowthGrowKeyword} / {SupporterGrowthShrinkKeyword}";
```

- [ ] **Step 2: Wire new properties into `RaiseSupporterGrowthProperties`**

Change `RaiseSupporterGrowthProperties` (line 1238) from:

```csharp
    private void RaiseSupporterGrowthProperties()
    {
        RaisePropertyChanged(nameof(SupporterGrowthBitRanges));
        RaisePropertyChanged(nameof(SupporterGrowthSummary));
        RaisePropertyChanged(nameof(TriggerSummary));
    }
```

To:

```csharp
    private void RaiseSupporterGrowthProperties()
    {
        RaisePropertyChanged(nameof(SupporterGrowthBitRanges));
        RaisePropertyChanged(nameof(SupporterGrowthSummary));
        RaisePropertyChanged(nameof(SupporterGrowthHeightBasicsSummary));
        RaisePropertyChanged(nameof(SupporterGrowthPaidTimeSummary));
        RaisePropertyChanged(nameof(SupporterGrowthSubTierSummary));
        RaisePropertyChanged(nameof(SupporterGrowthBitsRangeCountSummary));
        RaisePropertyChanged(nameof(SupporterGrowthCheerKeywordsSummary));
        RaisePropertyChanged(nameof(TriggerSummary));
    }
```

- [ ] **Step 3: Build to verify**

Run: `dotnet build "E:\!!!Program to work on\Proper Crystal Relay\VrcTwitchOscBridge\VrcTwitchOscBridge.csproj" --no-restore`
Expected: Build succeeded

---

### Task 2: Update en-US Localization Keys

**Files:**
- Modify: `VrcTwitchOscBridge\Resources\Localization\en-US.extra.json`

- [ ] **Step 1: Add new localization keys and fix existing ones**

In `en-US.extra.json`, make these changes:

1. **Remove** the outdated description at line 160:
   `"Supporter Growth listens to subs, gift subs, and bits. Each event adds height, resets the timer, then returns to normal when support stops."`

2. **Rename** line 166 key from `"Bits Timer Unit"` to `"Bits per Timer Unit"` (keep value same)

3. **Rename** line 167 key from `"Seconds Per Bits Unit"` to `"Seconds Added per Unit"` (keep value same)

4. **Add** these new keys (insert near the Supporter Growth section, after line 178):
```json
  "Bits per Timer Unit": "Bits per Timer Unit",
  "Seconds Added per Unit": "Seconds Added per Unit",
  "Inactivity Timeout (seconds)": "Inactivity Timeout (seconds)",
  "Height Basics": "Height Basics",
  "Paid Time Config": "Paid Time Config",
  "Sub Tier Rules": "Sub Tier Rules",
  "Cheer Keywords": "Cheer Keywords",
  "Example: {0} bits adds {1} seconds of paid time": "Example: {0} bits adds {1} seconds of paid time",
  "Paid time is shared by bits, subs, resubs, and gift subs. Each event adds time to the remaining timer. Time adds at full speed until the soft cap, then slows down and never exceeds the max. Height returns to normal after the inactivity timeout.": "Paid time is shared by bits, subs, resubs, and gift subs. Each event adds time to the remaining timer. Time adds at full speed until the soft cap, then slows down and never exceeds the max. Height returns to normal after the inactivity timeout."
```

5. **Update** the description at line 165 to use the new key:
   Remove: `"Paid time is shared by bits, subs, resubs, and gift subs. Time adds to the remaining paid timer, then slows above the soft cap and never exceeds the max."`
   Replace with the new longer description above.

- [ ] **Step 2: Verify JSON is valid**

Run: `powershell -Command "Get-Content 'E:\!!!Program to work on\Proper Crystal Relay\VrcTwitchOscBridge\Resources\Localization\en-US.extra.json' | ConvertFrom-Json | Out-Null; Write-Output 'Valid JSON'"`


Expected: `Valid JSON`

---

### Task 3: Add Placeholder Translations to All Other Localization Files

**Files:**
- Modify: `VrcTwitchOscBridge\Resources\Localization\de-DE.extra.json`
- Modify: `VrcTwitchOscBridge\Resources\Localization\es-ES.extra.json`
- Modify: `VrcTwitchOscBridge\Resources\Localization\fr-FR.extra.json`
- Modify: `VrcTwitchOscBridge\Resources\Localization\it-IT.extra.json`
- Modify: `VrcTwitchOscBridge\Resources\Localization\ja-JP.extra.json`
- Modify: `VrcTwitchOscBridge\Resources\Localization\ko-KR.extra.json`
- Modify: `VrcTwitchOscBridge\Resources\Localization\pl-PL.extra.json`
- Modify: `VrcTwitchOscBridge\Resources\Localization\pt-BR.extra.json`
- Modify: `VrcTwitchOscBridge\Resources\Localization\ru-RU.extra.json`
- Modify: `VrcTwitchOscBridge\Resources\Localization\sv-SE.extra.json`
- Modify: `VrcTwitchOscBridge\Resources\Localization\th-TH.extra.json`
- Modify: `VrcTwitchOscBridge\Resources\Localization\zh-CN.extra.json`
- Modify: `VrcTwitchOscBridge\Resources\Localization\zh-TW.extra.json`

- [ ] **Step 1: For each non-English *.extra.json file, add placeholder translations**

For each file:
1. Remove the old description key (the one starting with `"Supporter Growth listens to subs, gift subs, and bits..."`)
2. Remove the old `"Bits Timer Unit"` key
3. Remove the old `"Seconds Per Bits Unit"` key
4. Remove the old `"Paid time is shared by..."` short description key
5. Add the new keys with English placeholder values (translators will fill these in later):
```json
  "Bits per Timer Unit": "Bits per Timer Unit",
  "Seconds Added per Unit": "Seconds Added per Unit",
  "Inactivity Timeout (seconds)": "Inactivity Timeout (seconds)",
  "Height Basics": "Height Basics",
  "Paid Time Config": "Paid Time Config",
  "Sub Tier Rules": "Sub Tier Rules",
  "Cheer Keywords": "Cheer Keywords",
  "Example: {0} bits adds {1} seconds of paid time": "Example: {0} bits adds {1} seconds of paid time",
  "Paid time is shared by bits, subs, resubs, and gift subs. Each event adds time to the remaining timer. Time adds at full speed until the soft cap, then slows down and never exceeds the max. Height returns to normal after the inactivity timeout.": "Paid time is shared by bits, subs, resubs, and gift subs. Each event adds time to the remaining timer. Time adds at full speed until the soft cap, then slows down and never exceeds the max. Height returns to normal after the inactivity timeout."
```

- [ ] **Step 2: Verify all JSON files are valid**

Run: `powershell -Command "Get-ChildItem 'E:\!!!Program to work on\Proper Crystal Relay\VrcTwitchOscBridge\Resources\Localization\*.extra.json' | ForEach-Object { try { Get-Content $_.FullName | ConvertFrom-Json | Out-Null; Write-Output \"$($_.Name): OK\" } catch { Write-Output \"$($_.Name): INVALID - $_\" } }"`


Expected: All 14 files show `OK`

---

### Task 4: Rewrite XAML Supporter Growth Section with Expanders

**Files:**
- Modify: `VrcTwitchOscBridge\MainWindow.xaml:6672-6909`

- [ ] **Step 1: Replace the entire Supporter Growth Border content (lines 6688-6908)**

Replace the `<StackPanel>` content inside the Supporter Growth Border (lines 6688-6908) with the following. Keep the outer Border and its DataTrigger (lines 6672-6687) unchanged.

The new content structure:

```xml
<StackPanel>
    <!-- Header stays at top, NOT collapsible -->
    <TextBlock Text="{loc:Translate 'Supporter Growth'}"
               Style="{StaticResource HeadingTextStyle}"
               FontSize="20"
               FontWeight="Bold"
               Foreground="{DynamicResource TextBrush}" />
    <TextBlock Margin="0,8,0,0"
               Text="{loc:Translate 'Supporter Growth listens to bits, new subs, resubs, and gift subs. Paid events add height and add fair active time, then return to normal when the paid timer ends.'}"
               Foreground="{DynamicResource MutedBrush}"
               TextWrapping="Wrap" />
    <CheckBox Margin="0,12,0,0"
              Content="{loc:Translate 'Allow reward scale changes during paid growth'}"
              IsChecked="{Binding SupporterGrowthAllowRewardScaleOverlay, UpdateSourceTrigger=PropertyChanged}" />
    <TextBlock Margin="26,6,0,0"
               Text="{loc:Translate 'When enabled, channel-point and chat scale redeems can temporarily adjust height during paid growth without changing the paid timer.'}"
               Foreground="{DynamicResource MutedBrush}"
               TextWrapping="Wrap" />

    <!-- Section 1: Height Basics (expanded by default) -->
    <Expander Margin="0,16,0,0"
              IsExpanded="True"
              Foreground="{DynamicResource TextBrush}">
        <Expander.Header>
            <DockPanel>
                <TextBlock Text="{loc:Translate 'Height Basics'}"
                           Foreground="{DynamicResource TextBrush}"
                           FontWeight="SemiBold"
                           FontSize="14" />
                <TextBlock Margin="12,0,0,0"
                           Text="{Binding SupporterGrowthHeightBasicsSummary}"
                           Foreground="{DynamicResource MutedBrush}"
                           VerticalAlignment="Center"
                           FontSize="12" />
            </DockPanel>
        </Expander.Header>
        <UniformGrid Columns="2"
                     Margin="0,10,0,0">
            <StackPanel Margin="0,0,14,0">
                <TextBlock Text="{loc:Translate 'Normal Height'}"
                           Foreground="{DynamicResource TextBrush}"
                           FontWeight="SemiBold" />
                <TextBox Text="{Binding SupporterGrowthNormalHeightMeters, UpdateSourceTrigger=LostFocus}" />
            </StackPanel>
            <StackPanel Margin="14,0,0,0">
                <TextBlock Text="{loc:Translate 'Max Added Height'}"
                           Foreground="{DynamicResource TextBrush}"
                           FontWeight="SemiBold" />
                <TextBox Text="{Binding SupporterGrowthMaxAddedHeightMeters, UpdateSourceTrigger=LostFocus}" />
                <TextBlock Margin="0,6,0,0"
                           Text="{loc:Translate 'Use 0 for unlimited added height until VRChat or safe range clamps it.'}"
                           Foreground="{DynamicResource MutedBrush}"
                           TextWrapping="Wrap" />
            </StackPanel>
        </UniformGrid>
    </Expander>

    <!-- Section 2: Paid Time Config (collapsed by default) -->
    <Expander Margin="0,8,0,0"
              IsExpanded="False"
              Foreground="{DynamicResource TextBrush}">
        <Expander.Header>
            <DockPanel>
                <TextBlock Text="{loc:Translate 'Paid Time Config'}"
                           Foreground="{DynamicResource TextBrush}"
                           FontWeight="SemiBold"
                           FontSize="14" />
                <TextBlock Margin="12,0,0,0"
                           Text="{Binding SupporterGrowthPaidTimeSummary}"
                           Foreground="{DynamicResource MutedBrush}"
                           VerticalAlignment="Center"
                           FontSize="12" />
            </DockPanel>
        </Expander.Header>
        <StackPanel Margin="0,10,0,0">
            <TextBlock Text="{loc:Translate 'Paid time is shared by bits, subs, resubs, and gift subs. Each event adds time to the remaining timer. Time adds at full speed until the soft cap, then slows down and never exceeds the max. Height returns to normal after the inactivity timeout.'}"
                       Foreground="{DynamicResource MutedBrush}"
                       TextWrapping="Wrap" />
            <UniformGrid Columns="2"
                         Margin="0,10,0,0">
                <StackPanel Margin="0,0,14,0">
                    <TextBlock Text="{loc:Translate 'Bits per Timer Unit'}"
                               Foreground="{DynamicResource TextBrush}"
                               FontWeight="SemiBold" />
                    <TextBox Text="{Binding SupporterGrowthBitsTimerUnit, UpdateSourceTrigger=PropertyChanged}" />
                </StackPanel>
                <StackPanel Margin="14,0,0,0">
                    <TextBlock Text="{loc:Translate 'Seconds Added per Unit'}"
                               Foreground="{DynamicResource TextBrush}"
                               FontWeight="SemiBold" />
                    <TextBox Text="{Binding SupporterGrowthSecondsPerBitsUnit, UpdateSourceTrigger=PropertyChanged}" />
                </StackPanel>
            </UniformGrid>
            <TextBlock Margin="0,6,0,0"
                       Foreground="{DynamicResource MutedBrush}"
                       TextWrapping="Wrap">
                <TextBlock.Text>
                    <MultiBinding StringFormat="{loc:Translate 'Example: {0} bits adds {1} seconds of paid time'}">
                        <Binding Path="SupporterGrowthBitsTimerUnit" />
                        <Binding Path="SupporterGrowthSecondsPerBitsUnit" />
                    </MultiBinding>
                </TextBlock.Text>
            </TextBlock>
            <UniformGrid Columns="4"
                         Margin="0,10,0,0">
                <StackPanel Margin="0,0,10,0">
                    <TextBlock Text="{loc:Translate 'Smooth Transition Seconds'}"
                               Foreground="{DynamicResource TextBrush}"
                               FontWeight="SemiBold" />
                    <TextBox Text="{Binding SupporterGrowthTransitionSeconds, UpdateSourceTrigger=LostFocus}" />
                </StackPanel>
                <StackPanel Margin="10,0,10,0">
                    <TextBlock Text="{loc:Translate 'Soft Cap Seconds'}"
                               Foreground="{DynamicResource TextBrush}"
                               FontWeight="SemiBold" />
                    <TextBox Text="{Binding SupporterGrowthSoftCapSeconds, UpdateSourceTrigger=PropertyChanged}" />
                </StackPanel>
                <StackPanel Margin="10,0,10,0">
                    <TextBlock Text="{loc:Translate 'Soft Cap Multiplier Percent'}"
                               Foreground="{DynamicResource TextBrush}"
                               FontWeight="SemiBold" />
                    <TextBox Text="{Binding SupporterGrowthSoftCapMultiplierPercent, UpdateSourceTrigger=PropertyChanged}" />
                </StackPanel>
                <StackPanel Margin="10,0,0,0">
                    <TextBlock Text="{loc:Translate 'Max Paid Time Seconds'}"
                               Foreground="{DynamicResource TextBrush}"
                               FontWeight="SemiBold" />
                    <TextBox Text="{Binding SupporterGrowthMaxPaidTimeSeconds, UpdateSourceTrigger=PropertyChanged}" />
                </StackPanel>
            </UniformGrid>
            <StackPanel Margin="0,10,0,0">
                <TextBlock Text="{loc:Translate 'Inactivity Timeout (seconds)'}"
                           Foreground="{DynamicResource TextBrush}"
                           FontWeight="SemiBold" />
                <TextBox Text="{Binding SupporterGrowthInactivityTimerSeconds, UpdateSourceTrigger=PropertyChanged}" />
            </StackPanel>
        </StackPanel>
    </Expander>

    <!-- Section 3: Sub Tier Rules (collapsed by default) -->
    <Expander Margin="0,8,0,0"
              IsExpanded="False"
              Foreground="{DynamicResource TextBrush}">
        <Expander.Header>
            <DockPanel>
                <TextBlock Text="{loc:Translate 'Sub Tier Rules'}"
                           Foreground="{DynamicResource TextBrush}"
                           FontWeight="SemiBold"
                           FontSize="14" />
                <TextBlock Margin="12,0,0,0"
                           Text="{Binding SupporterGrowthSubTierSummary}"
                           Foreground="{DynamicResource MutedBrush}"
                           VerticalAlignment="Center"
                           FontSize="12" />
            </DockPanel>
        </Expander.Header>
        <StackPanel Margin="0,10,0,0">
            <TextBlock Text="{loc:Translate 'Subscription Growth'}"
                       Foreground="{DynamicResource TextBrush}"
                       FontWeight="SemiBold" />
            <!-- Tier 1 -->
            <UniformGrid Columns="2"
                         Margin="0,8,0,0">
                <StackPanel Margin="0,0,14,0">
                    <TextBlock Text="{loc:Translate 'Tier 1 Height Add'}"
                               Foreground="{DynamicResource TextBrush}"
                               FontWeight="SemiBold" />
                    <TextBox Text="{Binding SupporterGrowthTier1HeightMeters, UpdateSourceTrigger=LostFocus}" />
                </StackPanel>
                <StackPanel Margin="14,0,0,0">
                    <TextBlock Text="{loc:Translate 'Tier 1 Seconds'}"
                               Foreground="{DynamicResource TextBrush}"
                               FontWeight="SemiBold" />
                    <TextBox Text="{Binding SupporterGrowthTier1Seconds, UpdateSourceTrigger=PropertyChanged}" />
                </StackPanel>
            </UniformGrid>
            <!-- Tier 2 -->
            <UniformGrid Columns="2"
                         Margin="0,8,0,0">
                <StackPanel Margin="0,0,14,0">
                    <TextBlock Text="{loc:Translate 'Tier 2 Height Add'}"
                               Foreground="{DynamicResource TextBrush}"
                               FontWeight="SemiBold" />
                    <TextBox Text="{Binding SupporterGrowthTier2HeightMeters, UpdateSourceTrigger=LostFocus}" />
                </StackPanel>
                <StackPanel Margin="14,0,0,0">
                    <TextBlock Text="{loc:Translate 'Tier 2 Seconds'}"
                               Foreground="{DynamicResource TextBrush}"
                               FontWeight="SemiBold" />
                    <TextBox Text="{Binding SupporterGrowthTier2Seconds, UpdateSourceTrigger=PropertyChanged}" />
                </StackPanel>
            </UniformGrid>
            <!-- Tier 3 -->
            <UniformGrid Columns="2"
                         Margin="0,8,0,0">
                <StackPanel Margin="0,0,14,0">
                    <TextBlock Text="{loc:Translate 'Tier 3 Height Add'}"
                               Foreground="{DynamicResource TextBrush}"
                               FontWeight="SemiBold" />
                    <TextBox Text="{Binding SupporterGrowthTier3HeightMeters, UpdateSourceTrigger=LostFocus}" />
                </StackPanel>
                <StackPanel Margin="14,0,0,0">
                    <TextBlock Text="{loc:Translate 'Tier 3 Seconds'}"
                               Foreground="{DynamicResource TextBrush}"
                               FontWeight="SemiBold" />
                    <TextBox Text="{Binding SupporterGrowthTier3Seconds, UpdateSourceTrigger=PropertyChanged}" />
                </StackPanel>
            </UniformGrid>
        </StackPanel>
    </Expander>

    <!-- Section 4: Bits Growth Ranges (collapsed by default) -->
    <Expander Margin="0,8,0,0"
              IsExpanded="False"
              Foreground="{DynamicResource TextBrush}">
        <Expander.Header>
            <DockPanel>
                <TextBlock Text="{loc:Translate 'Bits Growth Ranges'}"
                           Foreground="{DynamicResource TextBrush}"
                           FontWeight="SemiBold"
                           FontSize="14" />
                <TextBlock Margin="12,0,0,0"
                           Text="{Binding SupporterGrowthBitsRangeCountSummary}"
                           Foreground="{DynamicResource MutedBrush}"
                           VerticalAlignment="Center"
                           FontSize="12" />
            </DockPanel>
        </Expander.Header>
        <StackPanel Margin="0,10,0,0">
            <Button Style="{StaticResource PrimaryButtonStyle}"
                    Padding="16,6"
                    HorizontalAlignment="Left"
                    Content="{loc:Translate 'Add Bits Range'}"
                    Click="OnAddSupporterGrowthBitRangeClicked" />
            <ItemsControl Margin="0,10,0,0"
                          ItemsSource="{Binding SupporterGrowthBitRanges}">
                <ItemsControl.ItemTemplate>
                    <DataTemplate DataType="{x:Type models:AvatarScaleBitGrowthRange}">
                        <Grid Margin="0,0,0,8">
                            <Grid.ColumnDefinitions>
                                <ColumnDefinition Width="*" />
                                <ColumnDefinition Width="*" />
                                <ColumnDefinition Width="*" />
                                <ColumnDefinition Width="Auto" />
                            </Grid.ColumnDefinitions>
                            <StackPanel Grid.Column="0"
                                        Margin="0,0,8,0">
                                <TextBlock Text="{loc:Translate 'Minimum Bits'}"
                                           Foreground="{DynamicResource TextBrush}"
                                           FontWeight="SemiBold" />
                                <TextBox Text="{Binding MinimumBits, UpdateSourceTrigger=PropertyChanged}" />
                            </StackPanel>
                            <StackPanel Grid.Column="1"
                                        Margin="8,0,8,0">
                                <TextBlock Text="{loc:Translate 'Maximum Bits'}"
                                           Foreground="{DynamicResource TextBrush}"
                                           FontWeight="SemiBold" />
                                <TextBox Text="{Binding MaximumBits, UpdateSourceTrigger=PropertyChanged}" />
                            </StackPanel>
                            <StackPanel Grid.Column="2"
                                        Margin="8,0,8,0">
                                <TextBlock Text="{loc:Translate 'Height Added'}"
                                           Foreground="{DynamicResource TextBrush}"
                                           FontWeight="SemiBold" />
                                <TextBox Text="{Binding HeightAddedMeters, UpdateSourceTrigger=LostFocus}" />
                            </StackPanel>
                            <Button Grid.Column="3"
                                    Margin="8,18,0,0"
                                    Style="{StaticResource SecondaryButtonStyle}"
                                    Padding="14,6"
                                    VerticalAlignment="Top"
                                    Content="{loc:Translate 'Remove'}"
                                    Click="OnRemoveSupporterGrowthBitRangeClicked" />
                        </Grid>
                    </DataTemplate>
                </ItemsControl.ItemTemplate>
            </ItemsControl>
            <TextBlock Margin="0,4,0,0"
                       Text="{loc:Translate 'Maximum Bits set to 0 means no upper limit for that row.'}"
                       Foreground="{DynamicResource MutedBrush}"
                       TextWrapping="Wrap" />
        </StackPanel>
    </Expander>

    <!-- Section 5: Cheer Keywords (collapsed by default) -->
    <Expander Margin="0,8,0,0"
              IsExpanded="False"
              Foreground="{DynamicResource TextBrush}">
        <Expander.Header>
            <DockPanel>
                <TextBlock Text="{loc:Translate 'Cheer Keywords'}"
                           Foreground="{DynamicResource TextBrush}"
                           FontWeight="SemiBold"
                           FontSize="14" />
                <TextBlock Margin="12,0,0,0"
                           Text="{Binding SupporterGrowthCheerKeywordsSummary}"
                           Foreground="{DynamicResource MutedBrush}"
                           VerticalAlignment="Center"
                           FontSize="12" />
            </DockPanel>
        </Expander.Header>
        <StackPanel Margin="0,10,0,0">
            <TextBlock Text="{loc:Translate 'Supporter Growth Cheer Keywords'}"
                       Foreground="{DynamicResource TextBrush}"
                       FontWeight="SemiBold" />
            <TextBlock Margin="0,6,0,0"
                       Text="{loc:Translate 'Use cheer text like Cheer100 grow or Cheer100 shrink. No keyword keeps the existing positive growth behavior; if both words appear, Crystal Relay skips the scale instead of guessing.'}"
                       Foreground="{DynamicResource MutedBrush}"
                       TextWrapping="Wrap" />
            <UniformGrid Columns="2"
                         Margin="0,10,0,0">
                <StackPanel Margin="0,0,14,0">
                    <TextBlock Text="{loc:Translate 'Grow Keyword'}"
                               Foreground="{DynamicResource TextBrush}"
                               FontWeight="SemiBold" />
                    <TextBox Text="{Binding SupporterGrowthGrowKeyword, UpdateSourceTrigger=PropertyChanged}" />
                </StackPanel>
                <StackPanel Margin="14,0,0,0">
                    <TextBlock Text="{loc:Translate 'Shrink Keyword'}"
                               Foreground="{DynamicResource TextBrush}"
                               FontWeight="SemiBold" />
                    <TextBox Text="{Binding SupporterGrowthShrinkKeyword, UpdateSourceTrigger=PropertyChanged}" />
                </StackPanel>
            </UniformGrid>
        </StackPanel>
    </Expander>
</StackPanel>
```

- [ ] **Step 2: Build to verify XAML compiles**

Run: `dotnet build "E:\!!!Program to work on\Proper Crystal Relay\VrcTwitchOscBridge\VrcTwitchOscBridge.csproj" --no-restore`
Expected: Build succeeded

---

### Task 5: Run Localization Audit and Final Build

**Files:**
- None (verification only)

- [ ] **Step 1: Run the localization audit**

Run: `dotnet run --project "E:\!!!Program to work on\Proper Crystal Relay\LocalizationAudit\LocalizationAudit.csproj"`
Expected: No errors for Supporter Growth keys

- [ ] **Step 2: Final build verification**

Run: `dotnet build "E:\!!!Program to work on\Proper Crystal Relay\VrcTwitchOscBridge\VrcTwitchOscBridge.csproj" --no-restore`
Expected: Build succeeded with 0 errors

- [ ] **Step 3: Launch debug build to verify UI**

Run: `E:\!!!Program to work on\Proper Crystal Relay\Launch-Crystal-Relay-Debug.bat`
Expected: App launches, navigate to Avatar Scaling → Supporter Growth, verify:
- 5 collapsible sections visible
- Height Basics starts expanded
- Other sections start collapsed with summaries visible
- All fields are functional
- Inactivity Timeout field is now visible
- Smooth Transition binds correctly (not stuck at 0)
- Sub tier height+time are paired per tier
