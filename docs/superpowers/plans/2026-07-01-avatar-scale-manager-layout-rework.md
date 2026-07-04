# Avatar Scaling Manager — Center Panel Layout Rework Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Reorganize the Avatar Scaling Manager center panel so Child Scale Rewards (channel-point redeems) and Pay System Rewards (Supporter Growth, Cash Payments, Power Ups) sit side-by-side in two columns instead of stacking vertically, reducing scroll length.

**Architecture:** Pure XAML layout change in `AvatarScalingManagerWindow.xaml`. The four vertically-stacked `Border` sections (Child Scale Rewards, Supporter Growth, Cash Payments, Power Ups) are reorganized into a two-column `Grid` below the Master Unlock Reward. Child Scale Reward cards switch from a `WrapPanel` with fixed 330px width to a 2-column `UniformGrid`. A new ViewModel property `IsPaySystemViewActive` drives column collapsing when a source filter is active. New localization keys are added for the "Pay System Rewards" header.

**Tech Stack:** WPF XAML, C# ViewModel, JSON localization files

**Spec:** `docs/superpowers/specs/2026-07-01-avatar-scale-manager-layout-rework-design.md`

---

## File Map

- **Modify:** `VrcTwitchOscBridge/AvatarScalingManagerWindow.xaml` — restructure center panel layout, compact SourceCardTemplate
- **Modify:** `VrcTwitchOscBridge/ViewModels/AvatarScalingManagerViewModel.cs` — add `IsPaySystemViewActive` and `IsChannelPointViewActive` computed properties for column collapsing
- **Modify:** `VrcTwitchOscBridge.Tests/AvatarScalingManagerWindowXamlTests.cs` — update tests that check for WrapPanel and Grid.Column, add new layout tests
- **Modify:** all `VrcTwitchOscBridge/Resources/Localization/*.extra.json` (14 files) — add "Pay System Rewards" and subtitle keys

---

### Task 1: Add ViewModel properties for column collapsing

**Files:**
- Modify: `VrcTwitchOscBridge/ViewModels/AvatarScalingManagerViewModel.cs`

- [ ] **Step 1: Add `IsChannelPointViewActive` and `IsPaySystemViewActive` computed properties**

In `AvatarScalingManagerViewModel.cs`, after the `ActiveSourceView` property (around line 128), add these two computed properties:

```csharp
public bool IsChannelPointViewActive =>
    ActiveSourceView == AvatarScalingManagerSourceView.TwitchRewards
    || ActiveSourceView == AvatarScalingManagerSourceView.AllSources;

public bool IsPaySystemViewActive =>
    ActiveSourceView == AvatarScalingManagerSourceView.SupporterGrowth
    || ActiveSourceView == AvatarScalingManagerSourceView.CashPayments
    || ActiveSourceView == AvatarScalingManagerSourceView.PowerUps
    || ActiveSourceView == AvatarScalingManagerSourceView.AllSources;
```

Also, in the `ActiveSourceView` property setter, raise change notifications for the new properties. Update the setter:

```csharp
public AvatarScalingManagerSourceView ActiveSourceView
{
    get => activeSourceView;
    set
    {
        if (SetProperty(ref activeSourceView, value))
        {
            RaisePropertyChanged(nameof(IsChannelPointViewActive));
            RaisePropertyChanged(nameof(IsPaySystemViewActive));
        }
    }
}
```

- [ ] **Step 2: Build to verify it compiles**

Run: `dotnet build "E:\!!!Program to work on\Proper Crystal Relay\VrcTwitchOscBridge\VrcTwitchOscBridge.csproj" --no-restore`
Expected: 0 errors

- [ ] **Step 3: Commit**

```bash
git add VrcTwitchOscBridge/ViewModels/AvatarScalingManagerViewModel.cs
git commit -m "Add IsChannelPointViewActive and IsPaySystemViewActive to AvatarScalingManagerViewModel"
```

---

### Task 2: Add localization keys for "Pay System Rewards" header

**Files:**
- Modify: all 14 `VrcTwitchOscBridge/Resources/Localization/*.extra.json` files

- [ ] **Step 1: Add keys to `en-US.extra.json`**

Open `VrcTwitchOscBridge/Resources/Localization/en-US.extra.json` and add these two keys (near the existing "Child Scale Rewards" key around line 214):

```json
"Pay System Rewards": "Pay System Rewards",
"Supporter Growth, Cash Payments & Power Ups": "Supporter Growth, Cash Payments & Power Ups",
```

- [ ] **Step 2: Add translated keys to all 13 non-English `.extra.json` files**

For each file, add the same two keys with natural translations. Use informal register per the AGENTS.md translation rules. Keep brand/technical terms in English ("Bits", "Subs", "Power Ups", "Cash Payments", "Supporter Growth").

**de-DE:**
```json
"Pay System Rewards": "Bezahl-System-Belohnungen",
"Supporter Growth, Cash Payments & Power Ups": "Supporter Growth, Cash Payments & Power Ups",
```

**es-ES:**
```json
"Pay System Rewards": "Recompensas del sistema de pago",
"Supporter Growth, Cash Payments & Power Ups": "Supporter Growth, Cash Payments & Power Ups",
```

**fr-FR:**
```json
"Pay System Rewards": "Récompenses du système de paiement",
"Supporter Growth, Cash Payments & Power Ups": "Supporter Growth, Cash Payments & Power Ups",
```

**it-IT:**
```json
"Pay System Rewards": "Ricompense del sistema di pagamento",
"Supporter Growth, Cash Payments & Power Ups": "Supporter Growth, Cash Payments & Power Ups",
```

**ja-JP:**
```json
"Pay System Rewards": "決済システム報酬",
"Supporter Growth, Cash Payments & Power Ups": "Supporter Growth, Cash Payments & Power Ups",
```

**ko-KR:**
```json
"Pay System Rewards": "결제 시스템 보상",
"Supporter Growth, Cash Payments & Power Ups": "Supporter Growth, Cash Payments & Power Ups",
```

**pl-PL:**
```json
"Pay System Rewards": "Nagrody systemu płatności",
"Supporter Growth, Cash Payments & Power Ups": "Supporter Growth, Cash Payments & Power Ups",
```

**pt-BR:**
```json
"Pay System Rewards": "Recompensas do sistema de pagamento",
"Supporter Growth, Cash Payments & Power Ups": "Supporter Growth, Cash Payments & Power Ups",
```

**ru-RU:**
```json
"Pay System Rewards": "Награды платёжной системы",
"Supporter Growth, Cash Payments & Power Ups": "Supporter Growth, Cash Payments & Power Ups",
```

**sv-SE:**
```json
"Pay System Rewards": "Betalningssystem-belöningar",
"Supporter Growth, Cash Payments & Power Ups": "Supporter Growth, Cash Payments & Power Ups",
```

**th-TH:**
```json
"Pay System Rewards": "รางวัลระบบการชำระเงิน",
"Supporter Growth, Cash Payments & Power Ups": "Supporter Growth, Cash Payments & Power Ups",
```

**zh-CN:**
```json
"Pay System Rewards": "付费系统奖励",
"Supporter Growth, Cash Payments & Power Ups": "Supporter Growth, Cash Payments & Power Ups",
```

**zh-TW:**
```json
"Pay System Rewards": "付費系統獎勵",
"Supporter Growth, Cash Payments & Power Ups": "Supporter Growth, Cash Payments & Power Ups",
```

- [ ] **Step 3: Run the localization audit**

Run: `dotnet build "E:\!!!Program to work on\Proper Crystal Relay\VrcTwitchOscBridge\VrcTwitchOscBridge.csproj" --no-restore`
Expected: 0 errors (the audit runs as part of the build process)

- [ ] **Step 4: Commit**

```bash
git add VrcTwitchOscBridge/Resources/Localization/*.extra.json
git commit -m "Add Pay System Rewards localization keys to all languages"
```

---

### Task 3: Compact the SourceCardTemplate

**Files:**
- Modify: `VrcTwitchOscBridge/AvatarScalingManagerWindow.xaml` (lines 713-745)

- [ ] **Step 1: Make the SourceCardTemplate more compact**

Replace the existing `SourceCardTemplate` DataTemplate (lines 713-745) with a more compact version that combines ActionSummary and SafetySummary onto fewer lines and reduces padding:

```xml
        <DataTemplate x:Key="SourceCardTemplate" DataType="{x:Type vm:AvatarScalingSourceCardViewModel}">
            <Border Margin="0,0,0,8"
                    Padding="10"
                    CornerRadius="12"
                    Background="{DynamicResource PanelBrush}"
                    BorderBrush="{DynamicResource BorderBrush}"
                    BorderThickness="1">
                <StackPanel>
                    <DockPanel LastChildFill="True">
                        <Border DockPanel.Dock="Right"
                                Background="{DynamicResource AccentDimBrush}"
                                BorderBrush="{DynamicResource BorderBrush}"
                                BorderThickness="1"
                                CornerRadius="10"
                                Padding="6,2"
                                Margin="6,0,0,0">
                            <TextBlock Text="{Binding SourcePill}" FontSize="9" Foreground="{DynamicResource TitleBarSubTextBrush}" />
                        </Border>
                        <StackPanel MinWidth="0">
                            <TextBlock Text="{Binding Title}" FontWeight="Bold" FontSize="13" Foreground="{DynamicResource TitleBarTextBrush}" TextTrimming="CharacterEllipsis" />
                            <TextBlock Text="{Binding StatusText}" Foreground="{DynamicResource TitleBarSubTextBrush}" FontSize="10" />
                        </StackPanel>
                    </DockPanel>
                    <TextBlock Margin="0,6,0,0" Text="{Binding ActionSummary}" Foreground="{DynamicResource TitleBarSubTextBrush}" FontSize="10" TextWrapping="Wrap" TextTrimming="CharacterEllipsis" />
                    <TextBlock Margin="0,2,0,0" Text="{Binding SafetySummary}" Foreground="{DynamicResource TitleBarSubTextBrush}" FontSize="10" TextTrimming="CharacterEllipsis" />
                    <Button Margin="0,8,0,0"
                            HorizontalAlignment="Left"
                            Content="{loc:Translate 'Edit'}"
                            Command="{Binding DataContext.OpenEditorCommand, RelativeSource={RelativeSource AncestorType=Window}}"
                            CommandParameter="{Binding}"
                            Style="{StaticResource SecondaryButtonStyle}" />
                </StackPanel>
            </Border>
        </DataTemplate>
```

- [ ] **Step 2: Build to verify XAML compiles**

Run: `dotnet build "E:\!!!Program to work on\Proper Crystal Relay\VrcTwitchOscBridge\VrcTwitchOscBridge.csproj" --no-restore`
Expected: 0 errors

- [ ] **Step 3: Run XAML tests to check for regressions**

Run: `dotnet test "E:\!!!Program to work on\Proper Crystal Relay\VrcTwitchOscBridge.Tests\VrcTwitchOscBridge.Tests.csproj" --no-restore --filter "FullyQualifiedName~AvatarScalingManagerWindowXamlTests.Window_SourceCardsForceReadableTextBrushes"`
Expected: PASS

- [ ] **Step 4: Commit**

```bash
git add VrcTwitchOscBridge/AvatarScalingManagerWindow.xaml
git commit -m "Compact SourceCardTemplate for tighter card layout"
```

---

### Task 4: Restructure center panel into two-column layout

**Files:**
- Modify: `VrcTwitchOscBridge/AvatarScalingManagerWindow.xaml` (lines 947-1069)
- Modify: `VrcTwitchOscBridge.Tests/AvatarScalingManagerWindowXamlTests.cs`

This is the core change. The four vertically-stacked `Border` sections (Child Scale Rewards at line 947, Supporter Growth at line 1001, Cash Payments at line 1024, Power Ups at line 1047) are reorganized into a two-column `Grid`.

- [ ] **Step 1: Replace the Child Scale Rewards + pay system Borders with a two-column Grid**

Replace the entire block from line 947 (`<Border>` for Child Scale Rewards) through line 1069 (the closing `</Border>` of the Power Ups section) with:

```xml
                        <!-- Two-column layout: Channel-Point Redeems | Pay System Rewards -->
                        <Grid>
                            <Grid.ColumnDefinitions>
                                <ColumnDefinition Width="*" MinWidth="200" />
                                <ColumnDefinition Width="12" />
                                <ColumnDefinition Width="*" MinWidth="200" />
                            </Grid.ColumnDefinitions>

                            <!-- LEFT COLUMN: Child Scale Rewards (channel-point redeems) -->
                            <Border Grid.Column="0">
                                <Border.Style>
                                    <Style TargetType="Border" BasedOn="{StaticResource CardGroupStyle}">
                                        <Setter Property="Visibility" Value="Collapsed" />
                                        <Style.Triggers>
                                            <DataTrigger Binding="{Binding ActiveSourceView}" Value="TwitchRewards">
                                                <Setter Property="Visibility" Value="Visible" />
                                            </DataTrigger>
                                            <DataTrigger Binding="{Binding ActiveSourceView}" Value="AllSources">
                                                <Setter Property="Visibility" Value="Visible" />
                                            </DataTrigger>
                                        </Style.Triggers>
                                    </Style>
                                </Border.Style>
                                <StackPanel>
                                    <TextBlock Text="{loc:Translate 'Child Scale Rewards'}" FontWeight="Bold" FontSize="16" />
                                    <TextBlock Margin="0,4,0,10"
                                               Text="{loc:Translate 'Channel point rewards and chat command fallbacks that change avatar height.'}"
                                               Foreground="{DynamicResource TitleBarSubTextBrush}" />
                                    <ItemsControl ItemsSource="{Binding TwitchScaleSetGroups}">
                                        <ItemsControl.ItemTemplate>
                                            <DataTemplate DataType="{x:Type vm:AvatarScalingScaleSetGroupViewModel}">
                                                <Border Margin="0,0,0,12"
                                                        Padding="10"
                                                        CornerRadius="14"
                                                        Background="{DynamicResource PanelSecondaryBrush}"
                                                        BorderBrush="{DynamicResource BorderBrush}"
                                                        BorderThickness="1">
                                                    <StackPanel>
                                                        <DockPanel LastChildFill="True">
                                                            <TextBlock DockPanel.Dock="Right" Text="{Binding CountText}" Foreground="{DynamicResource TitleBarSubTextBrush}" />
                                                            <TextBlock Text="{Binding Title}" FontWeight="Bold" FontSize="14" Foreground="{DynamicResource TitleBarTextBrush}" />
                                                        </DockPanel>
                                                        <ItemsControl Margin="0,10,0,0" ItemsSource="{Binding Cards}" ItemTemplate="{StaticResource SourceCardTemplate}">
                                                            <ItemsControl.ItemsPanel>
                                                                <ItemsPanelTemplate>
                                                                    <UniformGrid Columns="2" />
                                                                </ItemsPanelTemplate>
                                                            </ItemsControl.ItemsPanel>
                                                            <ItemsControl.ItemContainerStyle>
                                                                <Style TargetType="ContentPresenter">
                                                                    <Setter Property="Margin" Value="0,0,8,8" />
                                                                </Style>
                                                            </ItemsControl.ItemContainerStyle>
                                                        </ItemsControl>
                                                    </StackPanel>
                                                </Border>
                                            </DataTemplate>
                                        </ItemsControl.ItemTemplate>
                                    </ItemsControl>
                                </StackPanel>
                            </Border>

                            <!-- Spacer column (Grid.Column="1") is empty -->

                            <!-- RIGHT COLUMN: Pay System Rewards -->
                            <StackPanel Grid.Column="2">
                                <TextBlock Text="{loc:Translate 'Pay System Rewards'}" FontWeight="Bold" FontSize="16" />
                                <TextBlock Margin="0,4,0,10"
                                           Text="{loc:Translate 'Supporter Growth, Cash Payments & Power Ups'}"
                                           Foreground="{DynamicResource TitleBarSubTextBrush}" />

                                <!-- Supporter Growth -->
                                <Border>
                                    <Border.Style>
                                        <Style TargetType="Border" BasedOn="{StaticResource CardGroupStyle}">
                                            <Setter Property="Visibility" Value="Collapsed" />
                                            <Style.Triggers>
                                                <DataTrigger Binding="{Binding ActiveSourceView}" Value="SupporterGrowth">
                                                    <Setter Property="Visibility" Value="Visible" />
                                                </DataTrigger>
                                                <DataTrigger Binding="{Binding ActiveSourceView}" Value="AllSources">
                                                    <Setter Property="Visibility" Value="Visible" />
                                                </DataTrigger>
                                            </Style.Triggers>
                                        </Style>
                                    </Border.Style>
                                    <StackPanel>
                                        <TextBlock Text="{loc:Translate 'Supporter Growth'}" FontWeight="Bold" FontSize="14" />
                                        <TextBlock Margin="0,4,0,10"
                                                   Text="{loc:Translate 'Event-driven Bits and Subs growth rules.'}"
                                                   Foreground="{DynamicResource TitleBarSubTextBrush}" />
                                        <ItemsControl ItemsSource="{Binding SupporterGrowthCards}" ItemTemplate="{StaticResource SourceCardTemplate}" />
                                    </StackPanel>
                                </Border>

                                <!-- Cash Payments -->
                                <Border>
                                    <Border.Style>
                                        <Style TargetType="Border" BasedOn="{StaticResource CardGroupStyle}">
                                            <Setter Property="Visibility" Value="Collapsed" />
                                            <Style.Triggers>
                                                <DataTrigger Binding="{Binding ActiveSourceView}" Value="CashPayments">
                                                    <Setter Property="Visibility" Value="Visible" />
                                                </DataTrigger>
                                                <DataTrigger Binding="{Binding ActiveSourceView}" Value="AllSources">
                                                    <Setter Property="Visibility" Value="Visible" />
                                                </DataTrigger>
                                            </Style.Triggers>
                                        </Style>
                                    </Border.Style>
                                    <StackPanel>
                                        <TextBlock Text="{loc:Translate 'Cash Payments'}" FontWeight="Bold" FontSize="14" />
                                        <TextBlock Margin="0,4,0,10"
                                                   Text="{loc:Translate 'StreamElements, Streamlabs, and Ko-fi payment scaling rules.'}"
                                                   Foreground="{DynamicResource TitleBarSubTextBrush}" />
                                        <ItemsControl ItemsSource="{Binding CashPaymentCards}" ItemTemplate="{StaticResource SourceCardTemplate}" />
                                    </StackPanel>
                                </Border>

                                <!-- Power Ups -->
                                <Border>
                                    <Border.Style>
                                        <Style TargetType="Border" BasedOn="{StaticResource CardGroupStyle}">
                                            <Setter Property="Visibility" Value="Collapsed" />
                                            <Style.Triggers>
                                                <DataTrigger Binding="{Binding ActiveSourceView}" Value="PowerUps">
                                                    <Setter Property="Visibility" Value="Visible" />
                                                </DataTrigger>
                                                <DataTrigger Binding="{Binding ActiveSourceView}" Value="AllSources">
                                                    <Setter Property="Visibility" Value="Visible" />
                                                </DataTrigger>
                                            </Style.Triggers>
                                        </Style>
                                    </Border.Style>
                                    <StackPanel>
                                        <TextBlock Text="{loc:Translate 'Power Ups'}" FontWeight="Bold" FontSize="14" />
                                        <TextBlock Margin="0,4,0,10"
                                                   Text="{loc:Translate 'Twitch Power-up Bits scaling rules.'}"
                                                   Foreground="{DynamicResource TitleBarSubTextBrush}" />
                                        <ItemsControl ItemsSource="{Binding PowerUpCards}" ItemTemplate="{StaticResource SourceCardTemplate}" />
                                    </StackPanel>
                                </Border>
                            </StackPanel>
                        </Grid>
```

- [ ] **Step 2: Add column-collapse DataTriggers so filtered views get full width**

Add a `Grid.Style` to the two-column `Grid` that collapses the spacer and unused column when only one side is active. Add this inside the `<Grid>` element, right after `<Grid.ColumnDefinitions>`:

```xml
                            <Grid.Style>
                                <Style TargetType="Grid">
                                    <Style.Triggers>
                                        <DataTrigger Binding="{Binding IsChannelPointViewActive}" Value="False">
                                            <Setter Property="ColumnDefinitions[0].Width" Value="0" />
                                            <Setter Property="ColumnDefinitions[1].Width" Value="0" />
                                        </DataTrigger>
                                        <DataTrigger Binding="{Binding IsPaySystemViewActive}" Value="False">
                                            <Setter Property="ColumnDefinitions[1].Width" Value="0" />
                                            <Setter Property="ColumnDefinitions[2].Width" Value="0" />
                                        </DataTrigger>
                                    </Style.Triggers>
                                </Style>
                            </Grid.Style>
```

Note: WPF doesn't support `ColumnDefinitions[N].Width` setter syntax in Styles. Instead, use a `DataTrigger` with `Setter` targeting the `ColumnDefinition` elements directly by giving them `x:Name` and using `SourceName` in triggers. Alternatively, bind the `Width` properties to ViewModel properties via a value converter.

Simpler approach: give the ColumnDefinitions `x:Name` and use code-behind, OR use two separate `DataTrigger` blocks on the Grid that set `Visibility` on the column content containers. The cleanest XAML-only approach is to bind ColumnDefinition Width to ViewModel properties:

Replace the `<Grid.ColumnDefinitions>` with:

```xml
                            <Grid.ColumnDefinitions>
                                <ColumnDefinition Width="{Binding ChannelPointColumnWidth}" />
                                <ColumnDefinition Width="{Binding PaySystemSpacerWidth}" />
                                <ColumnDefinition Width="{Binding PaySystemColumnWidth}" />
                            </Grid.ColumnDefinitions>
```

- [ ] **Step 3: Add GridLength properties to the ViewModel**

In `AvatarScalingManagerViewModel.cs`, add these properties after the `IsPaySystemViewActive` property:

```csharp
private static readonly GridLength ZeroGrid = new GridLength(0);
private static readonly GridLength StarGrid = new GridLength(1, GridUnitType.Star);
private static readonly GridLength SpacerGrid = new GridLength(12);

public GridLength ChannelPointColumnWidth => IsChannelPointViewActive ? StarGrid : ZeroGrid;
public GridLength PaySystemSpacerWidth =>
    (IsChannelPointViewActive && IsPaySystemViewActive) ? SpacerGrid : ZeroGrid;
public GridLength PaySystemColumnWidth => IsPaySystemViewActive ? StarGrid : ZeroGrid;
```

Add `using System.Windows.Controls;` at the top if `GridLength` / `GridUnitType` are not already imported (they're in `System.Windows` namespace).

Also update the `ActiveSourceView` setter to raise notifications for these:

```csharp
public AvatarScalingManagerSourceView ActiveSourceView
{
    get => activeSourceView;
    set
    {
        if (SetProperty(ref activeSourceView, value))
        {
            RaisePropertyChanged(nameof(IsChannelPointViewActive));
            RaisePropertyChanged(nameof(IsPaySystemViewActive));
            RaisePropertyChanged(nameof(ChannelPointColumnWidth));
            RaisePropertyChanged(nameof(PaySystemSpacerWidth));
            RaisePropertyChanged(nameof(PaySystemColumnWidth));
        }
    }
}
```

- [ ] **Step 4: Build to verify XAML compiles**

Run: `dotnet build "E:\!!!Program to work on\Proper Crystal Relay\VrcTwitchOscBridge\VrcTwitchOscBridge.csproj" --no-restore`
Expected: 0 errors

- [ ] **Step 5: Commit**

```bash
git add VrcTwitchOscBridge/AvatarScalingManagerWindow.xaml VrcTwitchOscBridge/ViewModels/AvatarScalingManagerViewModel.cs
git commit -m "Restructure center panel into two-column layout with column collapsing"
```

---

### Task 5: Update tests for the new layout

**Files:**
- Modify: `VrcTwitchOscBridge.Tests/AvatarScalingManagerWindowXamlTests.cs`

- [ ] **Step 1: Update `Window_TwitchRewardsPageUsesRewardFocusedBrainstormLayout` test**

The test at line 35 currently asserts `Assert.Contains("WrapPanel", listArea, ...)`. Since we replaced `WrapPanel` with `UniformGrid`, update the assertion. Also the test extracts `listArea` between `<ScrollViewer Grid.Column="1"` and `<Border Grid.Column="3"`. The two-column Grid is now inside that area, so the extraction still works.

Change line 47 from:
```csharp
Assert.Contains("WrapPanel", listArea, StringComparison.Ordinal);
```
to:
```csharp
Assert.Contains("UniformGrid Columns=\"2\"", listArea, StringComparison.Ordinal);
```

- [ ] **Step 2: Update `Window_BrainstormLayoutStringsAreLocalizedInAllExtraFiles` test**

Add the new localization keys to the `expectedKeys` array (around line 53):

```csharp
var expectedKeys = new[]
{
    "Twitch Reward Scaling",
    "Set up channel-point rewards that change avatar height. Reward settings are kept together here, separate from paid support, cash payments, and Power Ups.",
    "1 reward",
    "{0} rewards",
    "Pay System Rewards",
    "Supporter Growth, Cash Payments & Power Ups"
};
```

- [ ] **Step 3: Update `Window_ScaleSetCommandArea_IsVisibleAndEnabledOnlyForScaleSetOwnedCards` test**

This test (line 161) searches for `<WrapPanel` and `</WrapPanel>` to extract the command area. Since the WrapPanel was replaced, update the extraction to use a different boundary. The command area is inside the editor panel (`Grid.Column="3"`), not the center panel, so it should still have its own `WrapPanel`. Check if this test still passes first before changing.

- [ ] **Step 4: Add a new test for the two-column layout**

Add this test to `AvatarScalingManagerWindowXamlTests.cs`:

```csharp
    [Fact]
    public void Window_CenterPanelUsesTwoColumnLayoutForRedeemsAndPaySystems()
    {
        var xaml = File.ReadAllText(FindSourceFile("VrcTwitchOscBridge", "AvatarScalingManagerWindow.xaml"));
        var listAreaStart = xaml.IndexOf("<ScrollViewer Grid.Column=\"1\"", StringComparison.Ordinal);
        var editorStart = xaml.IndexOf("<Border Grid.Column=\"3\"", listAreaStart, StringComparison.Ordinal);
        var listArea = listAreaStart >= 0 && editorStart > listAreaStart
            ? xaml[listAreaStart..editorStart]
            : string.Empty;

        Assert.Contains("Child Scale Rewards", listArea, StringComparison.Ordinal);
        Assert.Contains("Pay System Rewards", listArea, StringComparison.Ordinal);
        Assert.Contains("UniformGrid Columns=\"2\"", listArea, StringComparison.Ordinal);
        Assert.Contains("ChannelPointColumnWidth", listArea, StringComparison.Ordinal);
        Assert.Contains("PaySystemColumnWidth", listArea, StringComparison.Ordinal);
        Assert.DoesNotContain("Width=\"330\"", listArea, StringComparison.Ordinal);
    }
```

- [ ] **Step 5: Run all Avatar Scaling tests**

Run: `dotnet test "E:\!!!Program to work on\Proper Crystal Relay\VrcTwitchOscBridge.Tests\VrcTwitchOscBridge.Tests.csproj" --no-restore --filter "FullyQualifiedName~AvatarScaling"`
Expected: All tests PASS

- [ ] **Step 6: Commit**

```bash
git add VrcTwitchOscBridge.Tests/AvatarScalingManagerWindowXamlTests.cs
git commit -m "Update XAML tests for two-column center panel layout"
```

---

### Task 6: Final build and full test run

**Files:** None (verification only)

- [ ] **Step 1: Full build**

Run: `dotnet build "E:\!!!Program to work on\Proper Crystal Relay\VrcTwitchOscBridge\VrcTwitchOscBridge.csproj" --no-restore`
Expected: 0 errors

- [ ] **Step 2: Run all Avatar Scaling tests**

Run: `dotnet test "E:\!!!Program to work on\Proper Crystal Relay\VrcTwitchOscBridge.Tests\VrcTwitchOscBridge.Tests.csproj" --no-restore --filter "FullyQualifiedName~AvatarScaling"`
Expected: All tests PASS

- [ ] **Step 3: Visual verification**

Launch the debug build to visually confirm the layout:
`E:\!!!Program to work on\Proper Crystal Relay\Launch-Crystal-Relay-Debug.bat`
Open the Avatar Scaling Manager and verify:
- Global Safety Rule is on top
- Master Unlock Reward is below it
- Child Scale Rewards and Pay System Rewards are side-by-side
- Scale set reward cards are in a 2-column grid
- Clicking "Supporter Growth" in the nav collapses the left column
- Clicking "Twitch Rewards" in the nav collapses the right column
- "All Sources" shows both columns
