# Avatar Scaling Manager — Supporter Growth Fields & Extend Checkbox Scope Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Bring the new Avatar Scaling Manager window's Supporter Growth editor to parity with the legacy `MainWindow.xaml` Supporter Growth panel (add the missing sub tier, paid-time, cap, and cheer keyword fields), and hide the "Extend the current active activity" checkbox when the selected rule's trigger type is `SupporterGrowth`.

**Architecture:** Single-file XAML edit in `AvatarScalingManagerWindow.xaml`. No model, runtime, or localization changes — every binding targets an existing `AvatarScaleRule` property and every label/helper text key already exists in all 14 localization files because the legacy `MainWindow.xaml` Supporter Growth panel uses them.

**Tech Stack:** WPF + XAML, .NET 10, Crystal Relay `VrcTwitchOscBridge` project.

---

## File Structure

- **Modify only:** `E:\!!!Program to work on\Proper Crystal Relay\VrcTwitchOscBridge\AvatarScalingManagerWindow.xaml`
  - The Supporter Growth `<StackPanel>` inside the "Timer & Return" border (lines 1573-1607) gets expanded with the missing fields.
  - The "Extend the current active activity" `<Border>` (lines 1611-1630) gets a `Style` + `DataTrigger` that collapses it when `UsesSupporterGrowth` is true.

No other files are touched. No new files are created.

---

## Task 1: Expand the Supporter Growth section with all missing fields

**Files:**
- Modify: `VrcTwitchOscBridge\AvatarScalingManagerWindow.xaml:1573-1607` (the `<StackPanel Margin="0,12,0,0" Visibility="{Binding UsesSupporterGrowth, Converter={StaticResource BoolToVisibilityConverter}}">` block inside the "Timer & Return" border)

### Context

The current block at lines 1573-1607 only contains: a "Supporter Growth" header, a 2-column Normal Height / Max Added Height grid, a "Bits Growth Ranges" header with Add button, and the bits range `ItemsControl`. It is missing the description, the overlay checkbox + helper, the "use 0 for unlimited" helper under Max Added Height, the entire "Paid Active Time" group (Bits Timer Unit, Seconds Per Bits Unit, Soft Cap Seconds, Soft Cap Multiplier Percent, Max Paid Time Seconds), the entire "Supporter Growth Cheer Keywords" group (Grow Keyword, Shrink Keyword), the entire "Subscription Growth" group (Tier 1/2/3 Height Add), the entire "Subscription Paid Time" group (Tier 1/2/3 Seconds), and the "Maximum Bits set to 0 means no upper limit for that row." helper under the ranges list.

`SmoothTransitionSeconds` already appears in the Timer & Return row above (line 1569) and must NOT be duplicated inside the Supporter Growth block.

Every binding below targets an existing property on `AvatarScaleRule` (see `Models\AvatarScaleRule.cs`):
- `SupporterGrowthAllowRewardScaleOverlay` (bool, `PropertyChanged`)
- `SupporterGrowthNormalHeightMeters` (double, `LostFocus`) — already used
- `SupporterGrowthMaxAddedHeightMeters` (double, `LostFocus`) — already used
- `SupporterGrowthBitsTimerUnit` (int, `PropertyChanged`)
- `SupporterGrowthSecondsPerBitsUnit` (int, `PropertyChanged`)
- `SupporterGrowthSoftCapSeconds` (int, `PropertyChanged`)
- `SupporterGrowthSoftCapMultiplierPercent` (int, `PropertyChanged`)
- `SupporterGrowthMaxPaidTimeSeconds` (int, `PropertyChanged`)
- `SupporterGrowthGrowKeyword` (string, `PropertyChanged`)
- `SupporterGrowthShrinkKeyword` (string, `PropertyChanged`)
- `SupporterGrowthTier1HeightMeters` / `Tier2` / `Tier3` (double, `LostFocus`)
- `SupporterGrowthTier1Seconds` / `Tier2` / `Tier3` (int, `PropertyChanged`)
- `SupporterGrowthBitRanges` (ObservableCollection) — already used

Every `loc:Translate` key below already exists in `en-US.extra.json` and all 13 non-English `.extra.json` files (verified before plan was written).

- [ ] **Step 1: Read the current block to confirm exact text before replacing**

Run:
```
Read E:\!!!Program to work on\Proper Crystal Relay\VrcTwitchOscBridge\AvatarScalingManagerWindow.xaml offset=1573 limit=35
```
Expected: the block starts with `<StackPanel Margin="0,12,0,0" Visibility="{Binding UsesSupporterGrowth, ...` and ends with `</ItemsControl>` then `</StackPanel>` around line 1607.

- [ ] **Step 2: Replace the entire Supporter Growth `<StackPanel>` block**

Use the `edit` tool with `oldString` = the entire current block from line 1573 through line 1607 (the closing `</StackPanel>` of the `UsesSupporterGrowth` StackPanel — NOT the outer Timer & Return StackPanel). Use `newString` = the expanded block below.

**oldString** (exact current content, lines 1573-1607):
```xml
                                                    <StackPanel Margin="0,12,0,0" Visibility="{Binding UsesSupporterGrowth, Converter={StaticResource BoolToVisibilityConverter}}">
                                                        <TextBlock Text="{loc:Translate 'Supporter Growth'}" FontWeight="SemiBold" />
                                                        <UniformGrid Columns="2" Margin="0,8,0,0">
                                                            <StackPanel Margin="0,0,6,0">
                                                                <TextBlock Text="{loc:Translate 'Normal Height'}" FontWeight="SemiBold" />
                                                                <TextBox Text="{Binding SupporterGrowthNormalHeightMeters, UpdateSourceTrigger=LostFocus}" />
                                                            </StackPanel>
                                                            <StackPanel Margin="6,0,0,0">
                                                                <TextBlock Text="{loc:Translate 'Max Added Height'}" FontWeight="SemiBold" />
                                                                <TextBox Text="{Binding SupporterGrowthMaxAddedHeightMeters, UpdateSourceTrigger=LostFocus}" />
                                                            </StackPanel>
                                                        </UniformGrid>
                                                        <DockPanel Margin="0,10,0,0" LastChildFill="False">
                                                            <TextBlock Text="{loc:Translate 'Bits Growth Ranges'}" FontWeight="SemiBold" />
                                                            <Button Margin="10,0,0,0" Content="{loc:Translate 'Add Bits Range'}" Click="OnAddSupporterGrowthBitRangeClicked" Style="{StaticResource SecondaryButtonStyle}" />
                                                        </DockPanel>
                                                        <ItemsControl Margin="0,8,0,0" ItemsSource="{Binding SupporterGrowthBitRanges}">
                                                            <ItemsControl.ItemTemplate>
                                                                <DataTemplate DataType="{x:Type models:AvatarScaleBitGrowthRange}">
                                                                    <Grid Margin="0,0,0,8">
                                                                        <Grid.ColumnDefinitions>
                                                                            <ColumnDefinition Width="*" />
                                                                            <ColumnDefinition Width="*" />
                                                                            <ColumnDefinition Width="*" />
                                                                            <ColumnDefinition Width="Auto" />
                                                                        </Grid.ColumnDefinitions>
                                                                        <TextBox Grid.Column="0" Margin="0,0,4,0" Text="{Binding MinimumBits, UpdateSourceTrigger=PropertyChanged}" />
                                                                        <TextBox Grid.Column="1" Margin="4,0,4,0" Text="{Binding MaximumBits, UpdateSourceTrigger=PropertyChanged}" />
                                                                        <TextBox Grid.Column="2" Margin="4,0,4,0" Text="{Binding HeightAddedMeters, UpdateSourceTrigger=LostFocus}" />
                                                                        <Button Grid.Column="3" Content="{loc:Translate 'Remove'}" Click="OnRemoveSupporterGrowthBitRangeClicked" Style="{StaticResource SecondaryButtonStyle}" />
                                                                    </Grid>
                                                                </DataTemplate>
                                                            </ItemsControl.ItemTemplate>
                                                        </ItemsControl>
                                                    </StackPanel>
```

**newString** (expanded block):
```xml
                                                    <StackPanel Margin="0,12,0,0" Visibility="{Binding UsesSupporterGrowth, Converter={StaticResource BoolToVisibilityConverter}}">
                                                        <TextBlock Text="{loc:Translate 'Supporter Growth'}" FontWeight="SemiBold" />
                                                        <TextBlock Margin="0,6,0,0"
                                                                   Text="{loc:Translate 'Supporter Growth listens to bits, new subs, resubs, and gift subs. Paid events add height and add fair active time, then return to normal when the paid timer ends.'}"
                                                                   Foreground="{DynamicResource TitleBarSubTextBrush}"
                                                                   TextWrapping="Wrap" />
                                                        <CheckBox Margin="0,10,0,0"
                                                                  Content="{loc:Translate 'Allow reward scale changes during paid growth'}"
                                                                  IsChecked="{Binding SupporterGrowthAllowRewardScaleOverlay, UpdateSourceTrigger=PropertyChanged}" />
                                                        <TextBlock Margin="26,6,0,0"
                                                                   Text="{loc:Translate 'When enabled, channel-point and chat scale redeems can temporarily adjust height during paid growth without changing the paid timer.'}"
                                                                   Foreground="{DynamicResource TitleBarSubTextBrush}"
                                                                   TextWrapping="Wrap" />
                                                        <UniformGrid Columns="2" Margin="0,10,0,0">
                                                            <StackPanel Margin="0,0,6,0">
                                                                <TextBlock Text="{loc:Translate 'Normal Height'}" FontWeight="SemiBold" />
                                                                <TextBox Text="{Binding SupporterGrowthNormalHeightMeters, UpdateSourceTrigger=LostFocus}" />
                                                            </StackPanel>
                                                            <StackPanel Margin="6,0,0,0">
                                                                <TextBlock Text="{loc:Translate 'Max Added Height'}" FontWeight="SemiBold" />
                                                                <TextBox Text="{Binding SupporterGrowthMaxAddedHeightMeters, UpdateSourceTrigger=LostFocus}" />
                                                                <TextBlock Margin="0,6,0,0"
                                                                           Text="{loc:Translate 'Use 0 for unlimited added height until VRChat or safe range clamps it.'}"
                                                                           Foreground="{DynamicResource TitleBarSubTextBrush}"
                                                                           TextWrapping="Wrap" />
                                                            </StackPanel>
                                                        </UniformGrid>
                                                        <TextBlock Margin="0,14,0,0"
                                                                   Text="{loc:Translate 'Paid Active Time'}"
                                                                   FontWeight="SemiBold" />
                                                        <TextBlock Margin="0,6,0,0"
                                                                   Text="{loc:Translate 'Paid time is shared by bits, subs, resubs, and gift subs. Time adds to the remaining paid timer, then slows above the soft cap and never exceeds the max.'}"
                                                                   Foreground="{DynamicResource TitleBarSubTextBrush}"
                                                                   TextWrapping="Wrap" />
                                                        <UniformGrid Columns="3" Margin="0,8,0,0">
                                                            <StackPanel Margin="0,0,6,0">
                                                                <TextBlock Text="{loc:Translate 'Bits Timer Unit'}" FontWeight="SemiBold" />
                                                                <TextBox Text="{Binding SupporterGrowthBitsTimerUnit, UpdateSourceTrigger=PropertyChanged}" />
                                                            </StackPanel>
                                                            <StackPanel Margin="6,0,6,0">
                                                                <TextBlock Text="{loc:Translate 'Seconds Per Bits Unit'}" FontWeight="SemiBold" />
                                                                <TextBox Text="{Binding SupporterGrowthSecondsPerBitsUnit, UpdateSourceTrigger=PropertyChanged}" />
                                                            </StackPanel>
                                                            <StackPanel Margin="6,0,0,0">
                                                                <TextBlock Text="{loc:Translate 'Soft Cap Seconds'}" FontWeight="SemiBold" />
                                                                <TextBox Text="{Binding SupporterGrowthSoftCapSeconds, UpdateSourceTrigger=PropertyChanged}" />
                                                            </StackPanel>
                                                        </UniformGrid>
                                                        <UniformGrid Columns="3" Margin="0,8,0,0">
                                                            <StackPanel Margin="0,0,6,0">
                                                                <TextBlock Text="{loc:Translate 'Soft Cap Multiplier Percent'}" FontWeight="SemiBold" />
                                                                <TextBox Text="{Binding SupporterGrowthSoftCapMultiplierPercent, UpdateSourceTrigger=PropertyChanged}" />
                                                            </StackPanel>
                                                            <StackPanel Margin="6,0,6,0">
                                                                <TextBlock Text="{loc:Translate 'Max Paid Time Seconds'}" FontWeight="SemiBold" />
                                                                <TextBox Text="{Binding SupporterGrowthMaxPaidTimeSeconds, UpdateSourceTrigger=PropertyChanged}" />
                                                            </StackPanel>
                                                            <StackPanel Margin="6,0,0,0" />
                                                        </UniformGrid>
                                                        <TextBlock Margin="0,14,0,0"
                                                                   Text="{loc:Translate 'Supporter Growth Cheer Keywords'}"
                                                                   FontWeight="SemiBold" />
                                                        <TextBlock Margin="0,6,0,0"
                                                                   Text="{loc:Translate 'Use cheer text like Cheer100 grow or Cheer100 shrink. No keyword keeps the existing positive growth behavior; if both words appear, Crystal Relay skips the scale instead of guessing.'}"
                                                                   Foreground="{DynamicResource TitleBarSubTextBrush}"
                                                                   TextWrapping="Wrap" />
                                                        <UniformGrid Columns="2" Margin="0,8,0,0">
                                                            <StackPanel Margin="0,0,6,0">
                                                                <TextBlock Text="{loc:Translate 'Grow Keyword'}" FontWeight="SemiBold" />
                                                                <TextBox Text="{Binding SupporterGrowthGrowKeyword, UpdateSourceTrigger=PropertyChanged}" />
                                                            </StackPanel>
                                                            <StackPanel Margin="6,0,0,0">
                                                                <TextBlock Text="{loc:Translate 'Shrink Keyword'}" FontWeight="SemiBold" />
                                                                <TextBox Text="{Binding SupporterGrowthShrinkKeyword, UpdateSourceTrigger=PropertyChanged}" />
                                                            </StackPanel>
                                                        </UniformGrid>
                                                        <TextBlock Margin="0,14,0,0"
                                                                   Text="{loc:Translate 'Subscription Growth'}"
                                                                   FontWeight="SemiBold" />
                                                        <UniformGrid Columns="3" Margin="0,8,0,0">
                                                            <StackPanel Margin="0,0,6,0">
                                                                <TextBlock Text="{loc:Translate 'Tier 1 Height Add'}" FontWeight="SemiBold" />
                                                                <TextBox Text="{Binding SupporterGrowthTier1HeightMeters, UpdateSourceTrigger=LostFocus}" />
                                                            </StackPanel>
                                                            <StackPanel Margin="6,0,6,0">
                                                                <TextBlock Text="{loc:Translate 'Tier 2 Height Add'}" FontWeight="SemiBold" />
                                                                <TextBox Text="{Binding SupporterGrowthTier2HeightMeters, UpdateSourceTrigger=LostFocus}" />
                                                            </StackPanel>
                                                            <StackPanel Margin="6,0,0,0">
                                                                <TextBlock Text="{loc:Translate 'Tier 3 Height Add'}" FontWeight="SemiBold" />
                                                                <TextBox Text="{Binding SupporterGrowthTier3HeightMeters, UpdateSourceTrigger=LostFocus}" />
                                                            </StackPanel>
                                                        </UniformGrid>
                                                        <TextBlock Margin="0,14,0,0"
                                                                   Text="{loc:Translate 'Subscription Paid Time'}"
                                                                   FontWeight="SemiBold" />
                                                        <UniformGrid Columns="3" Margin="0,8,0,0">
                                                            <StackPanel Margin="0,0,6,0">
                                                                <TextBlock Text="{loc:Translate 'Tier 1 Seconds'}" FontWeight="SemiBold" />
                                                                <TextBox Text="{Binding SupporterGrowthTier1Seconds, UpdateSourceTrigger=PropertyChanged}" />
                                                            </StackPanel>
                                                            <StackPanel Margin="6,0,6,0">
                                                                <TextBlock Text="{loc:Translate 'Tier 2 Seconds'}" FontWeight="SemiBold" />
                                                                <TextBox Text="{Binding SupporterGrowthTier2Seconds, UpdateSourceTrigger=PropertyChanged}" />
                                                            </StackPanel>
                                                            <StackPanel Margin="6,0,0,0">
                                                                <TextBlock Text="{loc:Translate 'Tier 3 Seconds'}" FontWeight="SemiBold" />
                                                                <TextBox Text="{Binding SupporterGrowthTier3Seconds, UpdateSourceTrigger=PropertyChanged}" />
                                                            </StackPanel>
                                                        </UniformGrid>
                                                        <DockPanel Margin="0,14,0,0" LastChildFill="False">
                                                            <TextBlock Text="{loc:Translate 'Bits Growth Ranges'}" FontWeight="SemiBold" />
                                                            <Button Margin="10,0,0,0" Content="{loc:Translate 'Add Bits Range'}" Click="OnAddSupporterGrowthBitRangeClicked" Style="{StaticResource SecondaryButtonStyle}" />
                                                        </DockPanel>
                                                        <ItemsControl Margin="0,8,0,0" ItemsSource="{Binding SupporterGrowthBitRanges}">
                                                            <ItemsControl.ItemTemplate>
                                                                <DataTemplate DataType="{x:Type models:AvatarScaleBitGrowthRange}">
                                                                    <Grid Margin="0,0,0,8">
                                                                        <Grid.ColumnDefinitions>
                                                                            <ColumnDefinition Width="*" />
                                                                            <ColumnDefinition Width="*" />
                                                                            <ColumnDefinition Width="*" />
                                                                            <ColumnDefinition Width="Auto" />
                                                                        </Grid.ColumnDefinitions>
                                                                        <TextBox Grid.Column="0" Margin="0,0,4,0" Text="{Binding MinimumBits, UpdateSourceTrigger=PropertyChanged}" />
                                                                        <TextBox Grid.Column="1" Margin="4,0,4,0" Text="{Binding MaximumBits, UpdateSourceTrigger=PropertyChanged}" />
                                                                        <TextBox Grid.Column="2" Margin="4,0,4,0" Text="{Binding HeightAddedMeters, UpdateSourceTrigger=LostFocus}" />
                                                                        <Button Grid.Column="3" Content="{loc:Translate 'Remove'}" Click="OnRemoveSupporterGrowthBitRangeClicked" Style="{StaticResource SecondaryButtonStyle}" />
                                                                    </Grid>
                                                                </DataTemplate>
                                                            </ItemsControl.ItemTemplate>
                                                        </ItemsControl>
                                                        <TextBlock Margin="0,6,0,0"
                                                                   Text="{loc:Translate 'Maximum Bits set to 0 means no upper limit for that row.'}"
                                                                   Foreground="{DynamicResource TitleBarSubTextBrush}"
                                                                   TextWrapping="Wrap" />
                                                    </StackPanel>
```

- [ ] **Step 3: Read the edited region to confirm the replacement landed correctly**

Run:
```
Read E:\!!!Program to work on\Proper Crystal Relay\VrcTwitchOscBridge\AvatarScalingManagerWindow.xaml offset=1573 limit=150
```
Expected: the block now starts with the "Supporter Growth" header, then the description TextBlock, then the "Allow reward scale changes during paid growth" CheckBox, then the Normal Height / Max Added Height grid with the "Use 0 for unlimited..." helper, then the "Paid Active Time" subheader + helper + two 3-column grids, then the "Supporter Growth Cheer Keywords" subheader + helper + 2-column grid, then "Subscription Growth" + 3-column grid, then "Subscription Paid Time" + 3-column grid, then the unchanged "Bits Growth Ranges" DockPanel + ItemsControl, then the new "Maximum Bits set to 0 means no upper limit for that row." helper, then the closing `</StackPanel>`.

- [ ] **Step 4: Build the app project to confirm the XAML compiles**

Run:
```
dotnet build "E:\!!!Program to work on\Proper Crystal Relay\VrcTwitchOscBridge\VrcTwitchOscBridge.csproj" --no-restore
```
Expected: build succeeds with no XAML parse errors. WPF XAML compile errors would appear here if a binding path or `{loc:Translate}` key were malformed — but missing localization values do not fail the build (they fall back to the key text at runtime), so a clean build only confirms structural correctness, not that keys exist.

- [ ] **Step 5: Commit**

```
git add VrcTwitchOscBridge/AvatarScalingManagerWindow.xaml
git commit -m "Expand Supporter Growth editor with paid time, cheer keyword, and sub tier fields"
```

---

## Task 2: Hide the "Extend the current active activity" checkbox for Supporter Growth

**Files:**
- Modify: `VrcTwitchOscBridge\AvatarScalingManagerWindow.xaml` — the `<Border>` immediately after the Timer & Return border that wraps the Extend checkbox and "Extend by (seconds)" input. After Task 1, this border has moved down by the number of lines added in Task 1. Identify it by its content, not its line number: it is the `<Border Background="{DynamicResource NestedPanelBrush}" ...>` whose `<StackPanel>` contains `<CheckBox IsChecked="{Binding ExtendCurrentActivity, Mode=TwoWay}" Content="{loc:Translate &quot;Extend the current active activity instead of running this rule&apos;s action&quot;}" ... />`.

### Context

Today this border is always visible. We want it to collapse when the selected rule is a Supporter Growth rule. The rule's view model exposes `UsesSupporterGrowth` (bool) which is `True` when `TriggerType == AvatarScaleTriggerType.SupporterGrowth`. We add a `Border.Style` with a default `Visibility="Visible"` and a `DataTrigger` that sets `Visibility="Collapsed"` when `UsesSupporterGrowth` is `True`.

The existing `Border` currently has no `Style` attribute — we add one. The `Background`, `BorderBrush`, `BorderThickness`, `CornerRadius`, `Padding`, and `Margin` attributes stay on the `Border` element itself (they are not moved into the Style). Only `Visibility` lives in the Style so the `DataTrigger` can flip it.

- [ ] **Step 1: Read the Extend border to confirm exact current text**

Run a grep to locate it after Task 1:
```
Grep pattern="ExtendCurrentActivity, Mode=TwoWay" path="E:\!!!Program to work on\Proper Crystal Relay\VrcTwitchOscBridge\AvatarScalingManagerWindow.xaml"
```
Then `Read` around that line with enough context to capture the whole `<Border>...</Border>` (about 20 lines).

Expected: a border that looks like:
```xml
                                            <Border Background="{DynamicResource NestedPanelBrush}"
                                                    BorderBrush="{DynamicResource BorderBrush}"
                                                    BorderThickness="1"
                                                    CornerRadius="10"
                                                    Padding="10"
                                                    Margin="0,8,0,0">
                                                <StackPanel>
                                                    <CheckBox IsChecked="{Binding ExtendCurrentActivity, Mode=TwoWay}"
                                                              Content="{loc:Translate &quot;Extend the current active activity instead of running this rule&apos;s action&quot;}"
                                                              Margin="0,0,0,6" />
                                                    <StackPanel Orientation="Horizontal"
                                                                Visibility="{Binding ExtendCurrentActivity, Converter={StaticResource BoolToVisibilityConverter}}">
                                                        <TextBlock Text="{loc:Translate 'Extend by (seconds)'}"
                                                                   VerticalAlignment="Center"
                                                                   Margin="0,0,8,0" />
                                                        <TextBox Text="{Binding ExtendSeconds, UpdateSourceTrigger=PropertyChanged}"
                                                                 MinWidth="80" />
                                                    </StackPanel>
                                                </StackPanel>
                                            </Border>
```

- [ ] **Step 2: Add the `Border.Style` with the `UsesSupporterGrowth` DataTrigger**

Use the `edit` tool. The `oldString` is the opening `<Border ...>` tag (the one with `Background="{DynamicResource NestedPanelBrush}"` through `Margin="0,8,0,0">`), identified uniquely by the combination of those attributes plus the following `<StackPanel>` containing the `ExtendCurrentActivity` CheckBox. To make the match unique, include the opening `<StackPanel>` and the `<CheckBox IsChecked="{Binding ExtendCurrentActivity, Mode=TwoWay}"` line in both `oldString` and `newString`.

**oldString:**
```xml
                                            <Border Background="{DynamicResource NestedPanelBrush}"
                                                    BorderBrush="{DynamicResource BorderBrush}"
                                                    BorderThickness="1"
                                                    CornerRadius="10"
                                                    Padding="10"
                                                    Margin="0,8,0,0">
                                                <StackPanel>
                                                    <CheckBox IsChecked="{Binding ExtendCurrentActivity, Mode=TwoWay}"
```

**newString:**
```xml
                                            <Border Background="{DynamicResource NestedPanelBrush}"
                                                    BorderBrush="{DynamicResource BorderBrush}"
                                                    BorderThickness="1"
                                                    CornerRadius="10"
                                                    Padding="10"
                                                    Margin="0,8,0,0">
                                                <Border.Style>
                                                    <Style TargetType="Border">
                                                        <Setter Property="Visibility" Value="Visible" />
                                                        <Style.Triggers>
                                                            <DataTrigger Binding="{Binding UsesSupporterGrowth}" Value="True">
                                                                <Setter Property="Visibility" Value="Collapsed" />
                                                            </DataTrigger>
                                                        </Style.Triggers>
                                                    </Style>
                                                </Border.Style>
                                                <StackPanel>
                                                    <CheckBox IsChecked="{Binding ExtendCurrentActivity, Mode=TwoWay}"
```

This inserts the `Border.Style` block between the `Margin="0,8,0,0">` opening tag and the `<StackPanel>` that contains the checkbox. The rest of the border (checkbox, "Extend by (seconds)" StackPanel, closing `</Border>`) is untouched.

- [ ] **Step 3: Read the edited border to confirm the Style landed correctly**

Run the same `Grep` + `Read` as Step 1.
Expected: the border now has a `<Border.Style>` block with a `Style TargetType="Border"` containing a default `<Setter Property="Visibility" Value="Visible" />` and a `<DataTrigger Binding="{Binding UsesSupporterGrowth}" Value="True">` that sets `<Setter Property="Visibility" Value="Collapsed" />`. The `CheckBox` and inner `StackPanel` are unchanged.

- [ ] **Step 4: Build the app project to confirm the XAML compiles**

Run:
```
dotnet build "E:\!!!Program to work on\Proper Crystal Relay\VrcTwitchOscBridge\VrcTwitchOscBridge.csproj" --no-restore
```
Expected: build succeeds with no XAML parse errors.

- [ ] **Step 5: Commit**

```
git add VrcTwitchOscBridge/AvatarScalingManagerWindow.xaml
git commit -m "Hide Extend active activity checkbox for Supporter Growth scale rules"
```

---

## Task 3: Run the localization audit and verify

**Files:**
- No file changes expected. This task only runs the audit and confirms no gaps were introduced.

### Context

The localization audit project lives at `E:\!!!Program to work on\Proper Crystal Relay\LocalizationAudit`. It merges base `.json` and `.extra.json` localization files and checks for missing keys, empty values, and placeholder breakage. Since Task 1 and Task 2 only use `loc:Translate` keys that already exist in all 14 language files, the audit should pass unchanged. We run it to confirm — a typo in a key would surface here as a missing-key warning.

- [ ] **Step 1: Run the localization audit**

Run:
```
dotnet run --project "E:\!!!Program to work on\Proper Crystal Relay\LocalizationAudit\LocalizationAudit.csproj"
```
Expected: audit completes with no new missing keys, no empty values, and no placeholder breakage reported for the keys used in Tasks 1 and 2. If the audit reports a missing key that this plan introduced (it should not, since all keys were verified present before the plan was written), add the missing key to `en-US.extra.json` first, then to every non-English `.extra.json` file with a natural translation following the project's Localization Translation Quality Rules, then re-run the audit.

- [ ] **Step 2: If the audit found nothing new, no commit is needed**

If Step 1 needed no file changes, there is nothing to commit. If Step 1 required adding a key, commit the localization files:
```
git add VrcTwitchOscBridge/Resources/Localization/*.extra.json
git commit -m "Add missing localization keys for Supporter Growth editor expansion"
```

---

## Task 4: Manual verification in the debug build

**Files:**
- No file changes. This task launches the debug build and visually confirms the two changes.

### Context

The debug launcher is `E:\!!!Program to work on\Proper Crystal Relay\Launch-Crystal-Relay-Debug.bat`. It launches the compiled Debug `.exe` directly, which auto-identifies itself in the title bar with a ` - DEBUG` suffix. After Tasks 1-3, the build should be current; if not, rebuild first with `dotnet build "E:\!!!Program to work on\Proper Crystal Relay\VrcTwitchOscBridge\VrcTwitchOscBridge.csproj" --no-restore`.

- [ ] **Step 1: Confirm the Debug build is current**

Run:
```
dotnet build "E:\!!!Program to work on\Proper Crystal Relay\VrcTwitchOscBridge\VrcTwitchOscBridge.csproj" --no-restore --configuration Debug
```
Expected: build succeeds, output shows the Debug `.exe` path.

- [ ] **Step 2: Launch the debug build**

Run:
```
& "E:\!!!Program to work on\Proper Crystal Relay\Launch-Crystal-Relay-Debug.bat"
```
Expected: Crystal Relay opens with ` - DEBUG` in the title bar.

- [ ] **Step 3: Open the Avatar Scaling Manager and add a Supporter Growth card**

In the app: open the Avatar Scaling Manager. Click the "Add Supporter Growth" button in the Supporter Growth section. Select the new card.

Expected in the editor pane:
- "Supporter Growth" header is visible.
- The description "Supporter Growth listens to bits, new subs, resubs, and gift subs..." is visible.
- The "Allow reward scale changes during paid growth" checkbox is visible and toggleable.
- Normal Height and Max Added Height fields are visible, with the "Use 0 for unlimited added height..." helper under Max Added Height.
- "Paid Active Time" subheader + helper are visible.
- Bits Timer Unit, Seconds Per Bits Unit, Soft Cap Seconds fields are visible in a 3-column row.
- Soft Cap Multiplier Percent, Max Paid Time Seconds fields are visible in a second 3-column row.
- "Supporter Growth Cheer Keywords" subheader + helper are visible.
- Grow Keyword, Shrink Keyword fields are visible.
- "Subscription Growth" subheader is visible. Tier 1/2/3 Height Add fields are visible.
- "Subscription Paid Time" subheader is visible. Tier 1/2/3 Seconds fields are visible.
- "Bits Growth Ranges" header with "Add Bits Range" button is visible. The ranges list is visible.
- "Maximum Bits set to 0 means no upper limit for that row." helper is visible under the ranges list.
- The "Extend the current active activity instead of running this rule's action" checkbox is NOT visible anywhere in this card's editor.

- [ ] **Step 4: Edit a few fields, close the manager, reopen it, confirm persistence**

In the Supporter Growth card: change Normal Height to `1.8`, Tier 1 Seconds to `120`, Grow Keyword to `big`. Close the Avatar Scaling Manager. Reopen it and select the same Supporter Growth card.

Expected: Normal Height is `1.8`, Tier 1 Seconds is `120`, Grow Keyword is `big`. All three persisted.

- [ ] **Step 5: Add a Twitch Reward scale card and confirm the Extend checkbox IS visible**

In the Avatar Scaling Manager: click "Add Reward" (or add a Twitch Reward scale card). Select it. Confirm its trigger type is Channel Point Reward (the default).

Expected: the "Extend the current active activity instead of running this rule's action" checkbox IS visible. Toggle it on — the "Extend by (seconds)" input appears. Toggle it off — the input collapses.

- [ ] **Step 6: Switch the Twitch Reward card through other trigger types and confirm the Extend checkbox stays visible**

With the Twitch Reward card selected, change the Trigger Type dropdown to Chat Command, then Bits, then Subscription, then Gift Subscription, then Follow.

Expected: for each of those trigger types, the "Extend the current active activity" checkbox remains visible. Only when a Supporter Growth card is selected does the checkbox disappear.

- [ ] **Step 7: Close the app**

Close Crystal Relay. No file changes in this task.

---

## Self-Review Notes

- **Spec coverage:** Spec Change 1 (expand Supporter Growth fields) → Task 1. Spec Change 2 (hide Extend checkbox for Supporter Growth) → Task 2. Spec "Localization" section → Task 3. Spec "Verification" section → Task 4. All spec sections covered.
- **Placeholder scan:** No TBD/TODO/"add appropriate"/"similar to Task N". Every step that changes code shows the exact `oldString` and `newString` or the exact command.
- **Type/property consistency:** Every binding in Task 1 (`SupporterGrowthAllowRewardScaleOverlay`, `SupporterGrowthBitsTimerUnit`, `SupporterGrowthSecondsPerBitsUnit`, `SupporterGrowthSoftCapSeconds`, `SupporterGrowthSoftCapMultiplierPercent`, `SupporterGrowthMaxPaidTimeSeconds`, `SupporterGrowthGrowKeyword`, `SupporterGrowthShrinkKeyword`, `SupporterGrowthTier1HeightMeters`, `SupporterGrowthTier2HeightMeters`, `SupporterGrowthTier3HeightMeters`, `SupporterGrowthTier1Seconds`, `SupporterGrowthTier2Seconds`, `SupporterGrowthTier3Seconds`, `SupporterGrowthBitRanges`) matches the property names verified in `Models\AvatarScaleRule.cs` before the plan was written. Task 2 uses `UsesSupporterGrowth`, which is the existing bool property on `AvatarScaleRule` at line 972.
- **Localization keys:** All 24 keys used in Task 1 were verified present in `en-US.extra.json` and all 13 non-English `.extra.json` files before the plan was written. Task 3 re-runs the audit to confirm no typo introduced a new missing key.
- **No model/runtime changes:** Confirmed. Tasks 1 and 2 only edit XAML. The runtime `BridgeCoordinator.ExecuteAvatarScaleRuleAsync` path already handles `ExtendCurrentActivity` before the Supporter Growth branch; hiding the UI for Supporter Growth does not change runtime behavior for existing saved values.
