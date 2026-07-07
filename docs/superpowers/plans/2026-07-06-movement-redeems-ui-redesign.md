# Movement Redeems UI Redesign Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Replace Movement Redeems card-based layout with a compact DataGrid table and polish the editor panel spacing.

**Architecture:** Replace the `ListBox` + `DataTemplate` card layout in `MovementRedeemsManagerWindow.xaml` with a `DataGrid` styled to match the dark theme. Tighten editor spacing values. Add a computed display property to the card ViewModel for the duration/cooldown column.

**Tech Stack:** WPF XAML, DataGrid control, existing theme brushes/resources.

## Global Constraints

- No new files — only modify existing XAML and ViewModel
- Follow existing theme brush naming (PanelBrush, AccentBrush, BorderBrush, etc.)
- Keep all existing functionality: search, filter by category, add/delete/edit, editor overlay, window chrome
- Keep data bindings and commands intact
- Editor overlay must remain as a right-side panel (480px wide)
- All existing tests must pass

---

### Task 1: Add computed display properties to ViewModel

**Files:**
- Modify: `VrcTwitchOscBridge\ViewModels\MovementRedeemCardViewModel.cs`

**Changes:**
- Add `DurationWithCooldownText` property that combines duration and cooldown into "5.0s" or "5.0s / 60s"
- Notify the new property in `UpdateFromRule()`

- [ ] **Step 1: Add properties to MovementRedeemCardViewModel**

Add after line 102 (`public string CooldownText => ...`):

```csharp
public string DurationWithCooldownText => CooldownSeconds > 0
    ? $"{DurationSeconds:F1}s / {CooldownSeconds:F0}s"
    : $"{DurationSeconds:F1}s";
```

- [ ] **Step 2: Add property notification in UpdateFromRule()**

Add after line 152 (`RaisePropertyChanged(nameof(HasFollowTrigger));`):

```csharp
RaisePropertyChanged(nameof(DurationWithCooldownText));
```

- [ ] **Step 3: Build to verify**

```bash
dotnet build "E:\!!!Program to work on\Proper Crystal Relay\VrcTwitchOscBridge\VrcTwitchOscBridge.csproj" --no-restore
```

Expected: Build succeeds, no warnings related to MovementRedeemCardViewModel.

---

### Task 2: Replace ListBox with DataGrid in MovementRedeemsManagerWindow.xaml

**Files:**
- Modify: `VrcTwitchOscBridge\MovementRedeemsManagerWindow.xaml`

**Changes:**
1. Remove the card DataTemplate (`MovementCardTemplate` resource, lines 408-608)
2. Remove the ListBox (lines 698-726 including ItemContainerStyle)
3. Add DataGrid with styled columns in its place
4. Add DataGrid column/row/cell styles as resources

- [ ] **Step 1: Remove the MovementCardTemplate DataTemplate**

Delete lines 408-608 (everything from `<DataTemplate x:Key="MovementCardTemplate"...` through the closing `</DataTemplate>`).

- [ ] **Step 2: Remove the ListBox and its container style**

Delete lines 698-726 (the `<ListBox...` through its closing `</ListBox>`, including the ItemContainerStyle).

- [ ] **Step 3: Add DataGrid resources**

Add to the `<Window.Resources>` section (after the existing styles, before the close of `</Window.Resources>`):

```xml
<!-- DataGrid styles -->
<Style x:Key="DataGridColumnHeaderStyle" TargetType="DataGridColumnHeader">
    <Setter Property="Background" Value="{DynamicResource TitleBarBrush}" />
    <Setter Property="Foreground" Value="{DynamicResource TitleBarTextBrush}" />
    <Setter Property="FontFamily" Value="{DynamicResource BodyFontFamily}" />
    <Setter Property="FontWeight" Value="SemiBold" />
    <Setter Property="FontSize" Value="11" />
    <Setter Property="BorderBrush" Value="{DynamicResource BorderBrush}" />
    <Setter Property="BorderThickness" Value="0,0,0,1" />
    <Setter Property="Padding" Value="8,4" />
    <Setter Property="Height" Value="30" />
    <Setter Property="HorizontalContentAlignment" Value="Left" />
    <Setter Property="Template">
        <Setter.Value>
            <ControlTemplate TargetType="DataGridColumnHeader">
                <Border Background="{TemplateBinding Background}"
                        BorderBrush="{TemplateBinding BorderBrush}"
                        BorderThickness="{TemplateBinding BorderThickness}"
                        Padding="{TemplateBinding Padding}">
                    <Grid>
                        <Grid.ColumnDefinitions>
                            <ColumnDefinition Width="*" />
                            <ColumnDefinition Width="Auto" />
                        </Grid.ColumnDefinitions>
                        <ContentPresenter Grid.Column="0"
                                          VerticalAlignment="Center"
                                          RecognizesAccessKey="True" />
                        <Path Grid.Column="1"
                              x:Name="SortArrow"
                              Width="8"
                              Height="6"
                              Stretch="Fill"
                              Margin="4,0,0,0"
                              VerticalAlignment="Center"
                              Data="M 0 0 L 4 6 L 8 0"
                              Stroke="{DynamicResource AccentBrush}"
                              StrokeThickness="1.5"
                              Visibility="Collapsed" />
                    </Grid>
                </Border>
                <ControlTemplate.Triggers>
                    <Trigger Property="SortDirection" Value="Ascending">
                        <Setter TargetName="SortArrow" Property="Visibility" Value="Visible" />
                        <Setter TargetName="SortArrow" Property="Data" Value="M 0 6 L 4 0 L 8 6" />
                    </Trigger>
                    <Trigger Property="SortDirection" Value="Descending">
                        <Setter TargetName="SortArrow" Property="Visibility" Value="Visible" />
                        <Setter TargetName="SortArrow" Property="Data" Value="M 0 0 L 4 6 L 8 0" />
                    </Trigger>
                </ControlTemplate.Triggers>
            </ControlTemplate>
        </Setter.Value>
    </Setter>
</Style>

<Style TargetType="DataGridRow">
    <Setter Property="Background" Value="Transparent" />
    <Setter Property="BorderBrush" Value="{DynamicResource BorderBrush}" />
    <Setter Property="BorderThickness" Value="0,0,0,0.5" />
    <Setter Property="MinHeight" Value="36" />
    <Setter Property="VerticalAlignment" Value="Center" />
    <Setter Property="Template">
        <Setter.Value>
            <ControlTemplate TargetType="DataGridRow">
                <Border Background="{TemplateBinding Background}"
                        BorderBrush="{TemplateBinding BorderBrush}"
                        BorderThickness="{TemplateBinding BorderThickness}"
                        SnapsToDevicePixels="True">
                    <SelectiveScrollingGrid>
                        <SelectiveScrollingGrid.RowDefinitions>
                            <RowDefinition Height="*" />
                        </SelectiveScrollingGrid.RowDefinitions>
                        <SelectiveScrollingGrid.ColumnDefinitions>
                            <ColumnDefinition Width="Auto" />
                            <ColumnDefinition Width="*" />
                        </SelectiveScrollingGrid.ColumnDefinitions>
                        <DataGridCellsPresenter Grid.Column="1"
                                                ItemsPanel="{TemplateBinding ItemsPanel}"
                                                SnapsToDevicePixels="{TemplateBinding SnapsToDevicePixels}" />
                    </SelectiveScrollingGrid>
                </Border>
                <ControlTemplate.Triggers>
                    <Trigger Property="IsMouseOver" Value="True">
                        <Setter Property="BorderBrush" Value="{DynamicResource RuleCardHoverBrush}" />
                        <Setter Property="Background" Value="{DynamicResource AccentDimBrush}" />
                    </Trigger>
                    <Trigger Property="IsSelected" Value="True">
                        <Setter Property="BorderBrush" Value="{DynamicResource AccentBrush}" />
                        <Setter Property="Background" Value="{DynamicResource AccentDimBrush}" />
                    </Trigger>
                </ControlTemplate.Triggers>
            </ControlTemplate>
        </Setter.Value>
    </Setter>
</Style>

<Style TargetType="DataGridCell">
    <Setter Property="Background" Value="Transparent" />
    <Setter Property="BorderThickness" Value="0" />
    <Setter Property="Padding" Value="6,0" />
    <Setter Property="VerticalAlignment" Value="Center" />
    <Setter Property="Template">
        <Setter.Value>
            <ControlTemplate TargetType="DataGridCell">
                <Border Background="{TemplateBinding Background}"
                        BorderBrush="{TemplateBinding BorderBrush}"
                        BorderThickness="{TemplateBinding BorderThickness}"
                        Padding="{TemplateBinding Padding}"
                        VerticalAlignment="{TemplateBinding VerticalAlignment}">
                    <ContentPresenter VerticalAlignment="Center" />
                </Border>
            </ControlTemplate>
        </Setter.Value>
    </Setter>
</Style>

<Style x:Key="CompactButtonStyle" TargetType="Button" BasedOn="{StaticResource SecondaryButtonStyle}">
    <Setter Property="Padding" Value="5,2" />
    <Setter Property="MinWidth" Value="24" />
    <Setter Property="MinHeight" Value="22" />
    <Setter Property="FontSize" Value="10" />
    <Setter Property="Margin" Value="2,0,0,0" />
</Style>
```

- [ ] **Step 4: Add DataGrid in place of the ListBox**

Replace the deleted ListBox with this DataGrid (in the second row of the content grid, `Grid.Row="1"`):

```xml
<DataGrid Grid.Row="1"
          ItemsSource="{Binding Cards}"
          AutoGenerateColumns="False"
          HeadersVisibility="Column"
          GridLinesVisibility="None"
          Background="Transparent"
          BorderThickness="0"
          RowHeaderWidth="0"
          SelectionMode="Single"
          SelectionUnit="FullRow"
          CanUserSortColumns="True"
          CanUserReorderColumns="False"
          CanUserResizeColumns="False"
          CanUserAddRows="False"
          CanUserDeleteRows="False"
          IsReadOnly="True"
          RowHeight="36"
          FontSize="11"
          FontFamily="{DynamicResource BodyFontFamily}"
          Foreground="{DynamicResource TitleBarTextBrush}"
          ColumnHeaderStyle="{StaticResource DataGridColumnHeaderStyle}"
          ScrollViewer.VerticalScrollBarVisibility="Auto"
          ScrollViewer.HorizontalScrollBarVisibility="Disabled"
          MouseDoubleClick="OnDataGridRowDoubleClick">

    <DataGrid.Resources>
        <SolidColorBrush x:Key="DataGrid.CurrentCellBorderBrush" Color="Transparent" />
    </DataGrid.Resources>

    <DataGrid.Columns>
        <!-- Category -->
        <DataGridTemplateColumn Header="Category" Width="80" SortMemberPath="Category">
            <DataGridTemplateColumn.CellTemplate>
                <DataTemplate>
                    <Border Padding="6,2"
                            CornerRadius="6"
                            BorderThickness="1"
                            Background="{DynamicResource MovementPillBrush}"
                            BorderBrush="{DynamicResource MovementPillBrush}"
                            HorizontalAlignment="Center">
                        <Border.Style>
                            <Style TargetType="Border">
                                <Style.Triggers>
                                    <DataTrigger Binding="{Binding Category}" Value="Turning">
                                        <Setter Property="Background" Value="{DynamicResource TurningPillBrush}" />
                                        <Setter Property="BorderBrush" Value="{DynamicResource TurningPillBrush}" />
                                    </DataTrigger>
                                    <DataTrigger Binding="{Binding Category}" Value="HandInteractions">
                                        <Setter Property="Background" Value="{DynamicResource HandPillBrush}" />
                                        <Setter Property="BorderBrush" Value="{DynamicResource HandPillBrush}" />
                                    </DataTrigger>
                                    <DataTrigger Binding="{Binding Category}" Value="HeldObject">
                                        <Setter Property="Background" Value="{DynamicResource ObjectPillBrush}" />
                                        <Setter Property="BorderBrush" Value="{DynamicResource ObjectPillBrush}" />
                                    </DataTrigger>
                                    <DataTrigger Binding="{Binding Category}" Value="UiToggles">
                                        <Setter Property="Background" Value="{DynamicResource UiPillBrush}" />
                                        <Setter Property="BorderBrush" Value="{DynamicResource UiPillBrush}" />
                                    </DataTrigger>
                                </Style.Triggers>
                            </Style>
                        </Border.Style>
                        <TextBlock Text="{Binding CategoryDisplayName}"
                                   FontSize="9"
                                   FontWeight="Bold"
                                   Foreground="White" />
                    </Border>
                </DataTemplate>
            </DataGridTemplateColumn.CellTemplate>
        </DataGridTemplateColumn>

        <!-- Direction -->
        <DataGridTextColumn Header="Direction"
                            Width="150"
                            Binding="{Binding DirectionDisplayName}" />

        <!-- Name -->
        <DataGridTextColumn Header="Name"
                            Width="*"
                            Binding="{Binding Name}" />

        <!-- Duration / Cooldown -->
        <DataGridTextColumn Header="Dur / CD"
                            Width="90"
                            Binding="{Binding DurationWithCooldownText}" />

        <!-- Triggers -->
        <DataGridTemplateColumn Header="Triggers" Width="110">
            <DataGridTemplateColumn.CellTemplate>
                <DataTemplate>
                    <StackPanel Orientation="Horizontal">
                        <Border Padding="4,1"
                                Margin="0,0,2,0"
                                CornerRadius="4"
                                Background="{DynamicResource AccentDimBrush}"
                                BorderBrush="{DynamicResource BorderBrush}"
                                BorderThickness="1"
                                Visibility="{Binding HasChannelPointTrigger, Converter={StaticResource BoolToVisibilityConverter}}">
                            <TextBlock Text="R" FontSize="9" Foreground="{DynamicResource TitleBarSubTextBrush}" />
                        </Border>
                        <Border Padding="4,1"
                                Margin="0,0,2,0"
                                CornerRadius="4"
                                Background="{DynamicResource AccentDimBrush}"
                                BorderBrush="{DynamicResource BorderBrush}"
                                BorderThickness="1"
                                Visibility="{Binding HasChatCommandTrigger, Converter={StaticResource BoolToVisibilityConverter}}">
                            <TextBlock Text="C" FontSize="9" Foreground="{DynamicResource TitleBarSubTextBrush}" />
                        </Border>
                        <Border Padding="4,1"
                                Margin="0,0,2,0"
                                CornerRadius="4"
                                Background="{DynamicResource AccentDimBrush}"
                                BorderBrush="{DynamicResource BorderBrush}"
                                BorderThickness="1"
                                Visibility="{Binding HasBitsTrigger, Converter={StaticResource BoolToVisibilityConverter}}">
                            <TextBlock Text="B" FontSize="9" Foreground="{DynamicResource TitleBarSubTextBrush}" />
                        </Border>
                        <Border Padding="4,1"
                                Margin="0,0,2,0"
                                CornerRadius="4"
                                Background="{DynamicResource AccentDimBrush}"
                                BorderBrush="{DynamicResource BorderBrush}"
                                BorderThickness="1"
                                Visibility="{Binding HasSubsTrigger, Converter={StaticResource BoolToVisibilityConverter}}">
                            <TextBlock Text="S" FontSize="9" Foreground="{DynamicResource TitleBarSubTextBrush}" />
                        </Border>
                        <Border Padding="4,1"
                                Margin="0,0,2,0"
                                CornerRadius="4"
                                Background="{DynamicResource AccentDimBrush}"
                                BorderBrush="{DynamicResource BorderBrush}"
                                BorderThickness="1"
                                Visibility="{Binding HasGiftSubTrigger, Converter={StaticResource BoolToVisibilityConverter}}">
                            <TextBlock Text="G" FontSize="9" Foreground="{DynamicResource TitleBarSubTextBrush}" />
                        </Border>
                        <Border Padding="4,1"
                                Margin="0,0,2,0"
                                CornerRadius="4"
                                Background="{DynamicResource AccentDimBrush}"
                                BorderBrush="{DynamicResource BorderBrush}"
                                BorderThickness="1"
                                Visibility="{Binding HasFollowTrigger, Converter={StaticResource BoolToVisibilityConverter}}">
                            <TextBlock Text="F" FontSize="9" Foreground="{DynamicResource TitleBarSubTextBrush}" />
                        </Border>
                    </StackPanel>
                </DataTemplate>
            </DataGridTemplateColumn.CellTemplate>
        </DataGridTemplateColumn>

        <!-- Enabled -->
        <DataGridTemplateColumn Header="On" Width="50">
            <DataGridTemplateColumn.CellTemplate>
                <DataTemplate>
                    <ToggleButton IsChecked="{Binding IsEnabled, Mode=TwoWay}"
                                  Cursor="Hand"
                                  Width="32"
                                  Height="18"
                                  HorizontalAlignment="Center">
                        <ToggleButton.Style>
                            <Style TargetType="ToggleButton">
                                <Setter Property="Template">
                                    <Setter.Value>
                                        <ControlTemplate TargetType="ToggleButton">
                                            <Border x:Name="ToggleTrack"
                                                    Width="32"
                                                    Height="18"
                                                    CornerRadius="9"
                                                    Background="{DynamicResource InputBrush}"
                                                    BorderBrush="{DynamicResource InputBorderBrush}"
                                                    BorderThickness="1"
                                                    Cursor="Hand">
                                                <Border x:Name="ToggleThumb"
                                                        Width="14"
                                                        Height="14"
                                                        CornerRadius="7"
                                                        Background="{DynamicResource MutedBrush}"
                                                        HorizontalAlignment="Left"
                                                        Margin="1,0,0,0" />
                                            </Border>
                                            <ControlTemplate.Triggers>
                                                <Trigger Property="IsChecked" Value="True">
                                                    <Setter TargetName="ToggleTrack" Property="Background" Value="{DynamicResource AccentBrush}" />
                                                    <Setter TargetName="ToggleTrack" Property="BorderBrush" Value="{DynamicResource AccentBrush}" />
                                                    <Setter TargetName="ToggleThumb" Property="Background" Value="{DynamicResource TitleBarTextBrush}" />
                                                    <Setter TargetName="ToggleThumb" Property="HorizontalAlignment" Value="Right" />
                                                    <Setter TargetName="ToggleThumb" Property="Margin" Value="0,0,1,0" />
                                                </Trigger>
                                            </ControlTemplate.Triggers>
                                        </ControlTemplate>
                                    </Setter.Value>
                                </Setter>
                            </Style>
                        </ToggleButton.Style>
                    </ToggleButton>
                </DataTemplate>
            </DataGridTemplateColumn.CellTemplate>
        </DataGridTemplateColumn>

        <!-- Actions -->
        <DataGridTemplateColumn Header="" Width="100">
            <DataGridTemplateColumn.CellTemplate>
                <DataTemplate>
                    <StackPanel Orientation="Horizontal" HorizontalAlignment="Right">
                        <Button Content="&#x25B6;"
                                ToolTip="Test"
                                Command="{Binding DataContext.TestCardCommand, RelativeSource={RelativeSource AncestorType=Window}}"
                                CommandParameter="{Binding}"
                                Style="{StaticResource CompactButtonStyle}" />
                        <Button Content="&#x270E;"
                                ToolTip="Edit"
                                Command="{Binding DataContext.OpenEditorCommand, RelativeSource={RelativeSource AncestorType=Window}}"
                                CommandParameter="{Binding}"
                                Style="{StaticResource CompactButtonStyle}" />
                        <Button Content="&#x2715;"
                                ToolTip="Delete"
                                Command="{Binding DataContext.DeleteCardCommand, RelativeSource={RelativeSource AncestorType=Window}}"
                                CommandParameter="{Binding}"
                                Style="{StaticResource CompactButtonStyle}" />
                    </StackPanel>
                </DataTemplate>
            </DataGridTemplateColumn.CellTemplate>
        </DataGridTemplateColumn>
    </DataGrid.Columns>
</DataGrid>
```

- [ ] **Step 5: Add code-behind for double-click handler**

Add to `MovementRedeemsManagerWindow.xaml.cs`:

```csharp
private void OnDataGridRowDoubleClick(object sender, System.Windows.Input.MouseButtonEventArgs e)
{
    if (DataContext is MovementRedeemsManagerViewModel vm
        && (e.OriginalSource as FrameworkElement)?.DataContext is MovementRedeemCardViewModel card)
    {
        vm.OpenEditorCommand.Execute(card);
    }
}
```

- [ ] **Step 6: Build to verify**

```bash
dotnet build "E:\!!!Program to work on\Proper Crystal Relay\VrcTwitchOscBridge\VrcTwitchOscBridge.csproj" --no-restore
```

Expected: Build succeeds.

---

### Task 3: Polish editor panel spacing

**Files:**
- Modify: `VrcTwitchOscBridge\MovementRedeemsManagerWindow.xaml`

**Changes:** Tighten spacing values in the editor overlay panel (lines 773-1206).

- [ ] **Step 1: Reduce section gap from 12 to 8**

Change all `Margin="0,12,0,0"` on section borders to `Margin="0,8,0,0"` (lines 864, 1049, 1166).

- [ ] **Step 2: Replace section padding with left accent bar**

Restructure each section border to move `Padding="12"` from the outer Border into an inner Grid column, and add a 4px colored accent strip on the left.

**For each section (General Settings at line 779, Trigger Configuration at line 864, Movement Behavior at line 1049, Bot Message at line 1166):**

Replace this pattern:
```xml
<Border Padding="12"
        CornerRadius="14"
        Background="{DynamicResource NestedPanelBrush}"
        BorderBrush="{DynamicResource BorderBrush}"
        BorderThickness="1">
    <StackPanel>
        <TextBlock ... section header />
        ... content ...
    </StackPanel>
</Border>
```

With:
```xml
<Border Padding="0"
        CornerRadius="14"
        Background="{DynamicResource NestedPanelBrush}"
        BorderBrush="{DynamicResource BorderBrush}"
        BorderThickness="1">
    <Grid>
        <Grid.ColumnDefinitions>
            <ColumnDefinition Width="4" />
            <ColumnDefinition Width="*" />
        </Grid.ColumnDefinitions>
        <Border Grid.Column="0"
                Background="{DynamicResource AccentBrush}"
                CornerRadius="14,0,0,14" />
        <StackPanel Grid.Column="1" Padding="10,10">
            <TextBlock ... section header />
            ... content ...
        </StackPanel>
    </Grid>
</Border>
```

- [ ] **Step 3: Reduce sub-section inner padding from 10 to 8**

Change `Padding="10"` on trigger sub-section borders to `Padding="8"` in the Channel Points section (line 884), Chat Command (line 952), Bits (line 979), Subs (line 1001), Gift Sub (line 1019), Follow (line 1035).

Also for the Speed section (line 1085), Bits amount scaling (line 1101), Sub duration scaling (line 1130).

- [ ] **Step 4: Reduce UniformGrid column margins**

Change `Margin="0,0,8,0"` to `Margin="0,0,6,0"` on first-column StackPanels inside UniformGrids (lines 791, 900, 961, 1062, 1113).

Change `Margin="8,0,0,0"` to `Margin="6,0,0,0"` on second-column StackPanels (lines 797, 906, 967, 1072, 1119).

- [ ] **Step 5: Reduce footer margin from 16 to 12**

Change `Margin="0,16,0,0"` to `Margin="0,12,0,0"` on the footer WrapPanel (line 1189).

- [ ] **Step 6: Build and verify**

```bash
dotnet build "E:\!!!Program to work on\Proper Crystal Relay\VrcTwitchOscBridge\VrcTwitchOscBridge.csproj" --no-restore
```

Expected: Build succeeds.

---

### Task 4: Run tests and verify

- [ ] **Step 1: Run the project tests**

```bash
dotnet test "E:\!!!Program to work on\Proper Crystal Relay\VrcTwitchOscBridge.Tests\VrcTwitchOscBridge.Tests.csproj" --no-restore
```

Expected: All tests pass, including `Window_UsesWorkingOverlayPatternWithoutGlobalScrollViewerTemplate` and `Window_HasNoCustomScrollBarTemplatesToAvoidRenderingArtifacts`.

- [ ] **Step 2: Run the full build to confirm everything compiles**

```bash
dotnet build "E:\!!!Program to work on\Proper Crystal Relay\VrcTwitchOscBridge\VrcTwitchOscBridge.csproj"
```

Expected: Build succeeds with no errors.
