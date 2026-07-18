# Main Window Layout Reorganization Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Remove the unused Redeem Workspace column and restructure the Redeem Library into a 2x2 navigation grid.

**Architecture:** Pure XAML layout change in MainWindow.xaml — grid column definitions, content removal, card restructuring. Minor ViewModel cleanup for orphaned properties.

**Tech Stack:** WPF/XAML, C#

## Global Constraints

- Do not change the Home section, Settings sections, Activity section, or About section content
- Do not change any manager window (AvatarSwap, AvatarScaling, etc.)
- Every nav card in the Redeem Library must open its popup manager — no inline content
- Preserve existing visibility triggers on Redeem Library (collapses for Activity/About)

---

### Task 1: Restructure Grid Columns and ColumnSpan References

**Files:**
- Modify: `VrcTwitchOscBridge/MainWindow.xaml`

**Interfaces:**
- Consumes: existing XAML structure
- Produces: 3-column layout with correct ColumnSpan on overlays

- [ ] **Step 1: Change grid column definitions**

Replace the 5-column definition at lines 1632-1638:

```xml
<Grid.ColumnDefinitions>
    <ColumnDefinition Width="1.14*" />
    <ColumnDefinition Width="20" />
    <ColumnDefinition Width="1.2*" />
    <ColumnDefinition Width="20" />
    <ColumnDefinition Width="1.66*" />
</Grid.ColumnDefinitions>
```

With a 3-column definition:

```xml
<Grid.ColumnDefinitions>
    <ColumnDefinition Width="*" />
    <ColumnDefinition Width="20" />
    <ColumnDefinition Width="2*" />
</Grid.ColumnDefinitions>
```

- [ ] **Step 2: Update top nav bar ColumnSpan**

At line 1640, change `Grid.ColumnSpan="5"` to `Grid.ColumnSpan="3"`:

```xml
<Grid Grid.Row="0" Grid.ColumnSpan="3" Margin="0,8,0,16">
```

- [ ] **Step 3: Update Activity section ColumnSpan**

At line 4048 (approximately — search for `Grid.ColumnSpan="5"` near `IsActivitySectionSelected`), change to:

```xml
<Border Grid.Row="1" Grid.ColumnSpan="3"
```

- [ ] **Step 4: Update About section ColumnSpan**

At line 4098 (approximately — search for the other `Grid.ColumnSpan="5"` near `IsAboutSectionSelected`), change to:

```xml
<Border Grid.Row="1" Grid.ColumnSpan="3"
```

- [ ] **Step 5: Build and verify**

Run: `dotnet build "E:\!!!Program to work on\Proper Crystal Relay\VrcTwitchOscBridge\VrcTwitchOscBridge.csproj" --no-restore`
Expected: 0 errors (pre-existing warnings OK)

- [ ] **Step 6: Commit**

```bash
git add "VrcTwitchOscBridge/MainWindow.xaml"
git commit -m "refactor: reduce main window grid from 5 to 3 columns"
```

---

### Task 2: Remove Redeem Workspace Column (Column 4)

**Files:**
- Modify: `VrcTwitchOscBridge/MainWindow.xaml`

**Interfaces:**
- Consumes: 3-column layout from Task 1
- Produces: Column 4 element and its content removed

- [ ] **Step 1: Remove the Redeem Workspace Grid block**

Delete the entire `<Grid Grid.Row="1" Grid.Column="4">` block (approximately lines 3847-4046). The block starts with:

```xml
<Grid Grid.Row="1" Grid.Column="4">
    <Grid.Style>
        <Style TargetType="Grid">
            <Setter Property="Visibility" Value="Visible" />
            <Style.Triggers>
                <DataTrigger Binding="{Binding IsActivitySectionSelected}" Value="True">
                    <Setter Property="Visibility" Value="Collapsed" />
                </DataTrigger>
                <DataTrigger Binding="{Binding IsAboutSectionSelected}" Value="True">
                    <Setter Property="Visibility" Value="Collapsed" />
                </DataTrigger>
                <DataTrigger Binding="{Binding IsAvatarSetsManagerOpen}" Value="True">
                    <Setter Property="Visibility" Value="Collapsed" />
                </DataTrigger>
                <DataTrigger Binding="{Binding IsViewingAvatarTriggers}" Value="True">
                    <Setter Property="Visibility" Value="Collapsed" />
                </DataTrigger>
            </Style.Triggers>
        </Style>
    </Grid.Style>
    ... (content including Global Return Avatar,
         AvatarSwapRuleEditorControl, empty state text)
</Grid>
```

Delete the entire closing tag and everything in between.

- [ ] **Step 2: Build and verify**

Run: `dotnet build "E:\!!!Program to work on\Proper Crystal Relay\VrcTwitchOscBridge\VrcTwitchOscBridge.csproj" --no-restore`
Expected: 0 errors

- [ ] **Step 3: Commit**

```bash
git add "VrcTwitchOscBridge/MainWindow.xaml"
git commit -m "refactor: remove unused Redeem Workspace column"
```

---

### Task 3: Restructure Redeem Library into 2x2 Nav Grid

**Files:**
- Modify: `VrcTwitchOscBridge/MainWindow.xaml`

**Interfaces:**
- Consumes: Redeem Library container in Column 2 (Grid.Column="2")
- Produces: 2x2 grid of nav cards, no inline content

- [ ] **Step 1: Remove Tab Actions section**

Delete the `<StackPanel Margin="0,16,0,0" Width="340" ...>` block that contains "Tab Actions" divider (approximately lines 3559-3583). The block starts with:

```xml
<StackPanel Margin="0,16,0,0"
            Width="340"
            HorizontalAlignment="Center">
```

- [ ] **Step 2: Remove Cash Payment inline section**

Delete the `<StackPanel Margin="0,14,0,0" Width="340" ...>` block that is the "IsViewingCashPayments" section (approximately lines 3586-3766). The block starts with:

```xml
<StackPanel Margin="0,14,0,0"
            Width="340"
            HorizontalAlignment="Center">
    <StackPanel.Style>
        <Style TargetType="StackPanel">
            <Setter Property="Visibility" Value="Collapsed" />
            <Style.Triggers>
                <DataTrigger Binding="{Binding IsViewingCashPayments}" Value="True">
```

Delete everything up to the closing `</StackPanel>` tag of this block.

- [ ] **Step 3: Remove CashPaymentRules ListBox**

Delete the `<ListBox ItemsSource="{Binding CashPaymentRules}" ...>` block (approximately lines 3773-3809). The block starts with:

```xml
<ListBox ItemsSource="{Binding CashPaymentRules}"
         SelectedItem="{Binding SelectedCashPaymentRule}"
         ScrollViewer.CanContentScroll="True"
         VirtualizingStackPanel.IsVirtualizing="True"
         VirtualizingStackPanel.VirtualizationMode="Recycling">
```

- [ ] **Step 4: Remove empty state TextBlock**

Delete the `<TextBlock HorizontalAlignment="Center" ...>` with the MultiDataTrigger empty state (approximately lines 3813-3842). The block starts with:

```xml
<TextBlock HorizontalAlignment="Center"
           VerticalAlignment="Center"
           Foreground="{DynamicResource MutedBrush}"
           TextWrapping="Wrap"
           TextAlignment="Center">
    <TextBlock.Style>
        <Style TargetType="TextBlock">
            <Setter Property="Visibility" Value="Collapsed" />
            <Setter Property="Text" Value="{loc:Translate 'Add an avatar set to get started.'}" />
```

Also remove the blank lines between sections (lines 3768-3772 and 3810-3812).

- [ ] **Step 5: Restructure nav cards into 2x2 grid**

Replace the current single-column card stack with the following. The existing cards for Avatar Sets, Avatar Actions, Trigger Systems, and Viewer Support are at approximately lines 3431-3558.

Replace the four `<Border>` card blocks with this grid layout:

```xml
<Grid>
    <Grid.Resources>
        <Style x:Key="NavCardStyle" TargetType="Border">
            <Setter Property="Background" Value="{DynamicResource NestedPanelBrush}" />
            <Setter Property="BorderBrush" Value="{DynamicResource HighlightBorderBrush}" />
            <Setter Property="BorderThickness" Value="1" />
            <Setter Property="CornerRadius" Value="14" />
            <Setter Property="Padding" Value="18" />
            <Setter Property="Margin" Value="0,0,0,0" />
        </Style>
        <Style x:Key="NavCardButtonStyle" TargetType="Button">
            <Setter Property="Background" Value="{DynamicResource PanelBrush}" />
            <Setter Property="Foreground" Value="{DynamicResource TextBrush}" />
            <Setter Property="BorderBrush" Value="{DynamicResource InputBorderBrush}" />
            <Setter Property="BorderThickness" Value="1" />
            <Setter Property="Padding" Value="8,10" />
            <Setter Property="Cursor" Value="Hand" />
            <Setter Property="FontSize" Value="11" />
        </Style>
        <Style x:Key="NavCardActionButtonStyle" TargetType="Button" BasedOn="{StaticResource NavCardButtonStyle}">
            <Setter Property="FontWeight" Value="Bold" />
            <Setter Property="Foreground" Value="{DynamicResource AccentBrush}" />
        </Style>
    </Grid.Resources>

    <Grid.ColumnDefinitions>
        <ColumnDefinition Width="*" />
        <ColumnDefinition Width="12" />
        <ColumnDefinition Width="*" />
    </Grid.ColumnDefinitions>
    <Grid.RowDefinitions>
        <RowDefinition Height="Auto" />
        <RowDefinition Height="12" />
        <RowDefinition Height="Auto" />
    </Grid.RowDefinitions>

    <!-- Card 1: Avatar Sets (top-left) -->
    <Border Grid.Row="0" Grid.Column="0" Style="{StaticResource NavCardStyle}"
            BorderBrush="{DynamicResource HighlightBorderBrush}">
        <StackPanel>
            <TextBlock Text="{loc:Translate 'Avatar Sets'}"
                       FontFamily="{DynamicResource HeadingFontFamily}"
                       FontSize="10"
                       FontWeight="SemiBold"
                       Foreground="{DynamicResource MutedBrush}" />
            <TextBlock Margin="0,4,0,0"
                       Text="{loc:Translate 'Group redeems by avatar set'}"
                       FontWeight="Bold"
                       FontSize="15"
                       Foreground="{DynamicResource TextBrush}" />
            <TextBlock Margin="0,6,0,14"
                       Text="{loc:Translate 'Organize avatar swap and scale rules into themed sets'}"
                       Foreground="{DynamicResource MutedBrush}"
                       FontSize="11"
                       TextWrapping="Wrap" />
            <Button Content="{loc:Translate 'Manage'} →"
                    Style="{StaticResource NavCardActionButtonStyle}"
                    Command="{Binding OpenAvatarSetsManagerCommand}"
                    HorizontalAlignment="Left" />
        </StackPanel>
    </Border>

    <!-- Card 2: Avatar Actions (top-right) -->
    <Border Grid.Row="0" Grid.Column="2" Style="{StaticResource NavCardStyle}">
        <StackPanel>
            <TextBlock Text="{loc:Translate 'Avatar Actions'}"
                       FontFamily="{DynamicResource HeadingFontFamily}"
                       FontSize="10"
                       FontWeight="SemiBold"
                       Foreground="{DynamicResource MutedBrush}" />
            <TextBlock Margin="0,4,0,0"
                       Text="{loc:Translate 'Swap, scale, and roulette'}"
                       FontWeight="Bold"
                       FontSize="15"
                       Foreground="{DynamicResource TextBrush}" />
            <TextBlock Margin="0,6,0,14"
                       Text="{loc:Translate 'Change avatars, height, or run avatar roulette'}"
                       Foreground="{DynamicResource MutedBrush}"
                       FontSize="11"
                       TextWrapping="Wrap" />
            <UniformGrid Columns="2">
                <Button Content="{loc:Translate 'Avatar Swap'}"
                        Style="{StaticResource NavCardButtonStyle}"
                        Command="{Binding OpenAvatarSwapManagerCommand}"
                        Margin="0,0,6,0" />
                <Button Content="{loc:Translate 'Avatar Scaling'}"
                        Style="{StaticResource NavCardButtonStyle}"
                        Command="{Binding OpenAvatarScalingManagerCommand}"
                        Margin="6,0,0,0" />
            </UniformGrid>
        </StackPanel>
    </Border>

    <!-- Card 3: Trigger Systems (bottom-left) -->
    <Border Grid.Row="2" Grid.Column="0" Style="{StaticResource NavCardStyle}">
        <StackPanel>
            <TextBlock Text="{loc:Translate 'Trigger Systems'}"
                       FontFamily="{DynamicResource HeadingFontFamily}"
                       FontSize="10"
                       FontWeight="SemiBold"
                       Foreground="{DynamicResource MutedBrush}" />
            <TextBlock Margin="0,4,0,0"
                       Text="{loc:Translate 'Universal triggers and movement'}"
                       FontWeight="Bold"
                       FontSize="15"
                       Foreground="{DynamicResource TextBrush}" />
            <TextBlock Margin="0,6,0,14"
                       Text="{loc:Translate 'Chat commands, channel points, bits, subs, and movement redeems'}"
                       Foreground="{DynamicResource MutedBrush}"
                       FontSize="11"
                       TextWrapping="Wrap" />
            <UniformGrid Columns="2">
                <Button Content="{loc:Translate 'Universal Triggers'}"
                        Style="{StaticResource NavCardButtonStyle}"
                        Command="{Binding OpenUniversalTriggersManagerCommand}"
                        Margin="0,0,6,0" />
                <Button Content="{loc:Translate 'Movement Redeems'}"
                        Style="{StaticResource NavCardButtonStyle}"
                        Command="{Binding ShowMovementRedeemsCommand}"
                        Margin="6,0,0,0" />
            </UniformGrid>
        </StackPanel>
    </Border>

    <!-- Card 4: Viewer Support (bottom-right) -->
    <Border Grid.Row="2" Grid.Column="2" Style="{StaticResource NavCardStyle}">
        <StackPanel>
            <TextBlock Text="{loc:Translate 'Viewer Support'}"
                       FontFamily="{DynamicResource HeadingFontFamily}"
                       FontSize="10"
                       FontWeight="SemiBold"
                       Foreground="{DynamicResource MutedBrush}" />
            <TextBlock Margin="0,4,0,0"
                       Text="{loc:Translate 'Tips, donations, and sales'}"
                       FontWeight="Bold"
                       FontSize="15"
                       Foreground="{DynamicResource TextBrush}" />
            <TextBlock Margin="0,6,0,14"
                       Text="{loc:Translate 'StreamElements, Streamlabs, Ko-fi payments and reward fire sales'}"
                       Foreground="{DynamicResource MutedBrush}"
                       FontSize="11"
                       TextWrapping="Wrap" />
            <UniformGrid Columns="2">
                <Button Content="{loc:Translate 'Cash Payments'}"
                        Style="{StaticResource NavCardButtonStyle}"
                        Command="{Binding OpenCashPaymentManagerCommand}"
                        Margin="0,0,6,0" />
                <Button Content="{loc:Translate 'Reward Fire Sale'}"
                        Style="{StaticResource NavCardButtonStyle}"
                        Command="{Binding OpenRewardFireSaleManagerCommand}"
                        Margin="6,0,0,0" />
            </UniformGrid>
        </StackPanel>
    </Border>
</Grid>
```

- [ ] **Step 6: Build and verify**

Run: `dotnet build "E:\!!!Program to work on\Proper Crystal Relay\VrcTwitchOscBridge\VrcTwitchOscBridge.csproj" --no-restore`
Expected: 0 errors (pre-existing warnings OK)

- [ ] **Step 7: Commit**

```bash
git add "VrcTwitchOscBridge/MainWindow.xaml"
git commit -m "refactor: restructure Redeem Library into 2x2 nav grid"
```
