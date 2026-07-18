# OSC Status 2x2 Reorganization Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Fix the OSC card "Disconnected" bug and reorganize the main window connection status into a 2x2 grid with OSC controls in the Settings right-column.

**Architecture:** Two files change: `MainWindowViewModel.cs` (add PropertyChanged notification), `MainWindow.xaml` (remove Streaming Status and old OSC Status from Home, replace 1x4 status row with 2x2 grid + OSC controls panel in Settings).

**Tech Stack:** C#, WPF/XAML, .NET 10

## Global Constraints

- Follow existing XAML styling patterns (PanelBorderStyle, SecondaryButtonStyle, DataTrigger bindings)
- Use existing ViewModel bindings - do not add new ViewModel properties unless necessary
- Keep localization keys intact for any preserved text
- `IsOscConnected` = `bridgeCoordinator.IsOscActive`; `IsOscDisconnected = !IsOscConnected`
- `IsVrChatConnected` = `Settings.VrChat.IsConnected`
- `IsBroadcasterConnected` = `HasRecoverableBroadcasterSession && !broadcasterReconnectRequired`
- `IsBroadcasterLive` (Stream) already exists

---

### Task 1: Fix Missing PropertyChanged for IsOscConnected

**Files:**
- Modify: `VrcTwitchOscBridge/ViewModels/MainWindowViewModel.cs:16033-16092`

**Interfaces:**
- Consumes: `UpdateOscStatusSummary()` — already called when `BridgeStatus` changes (line 2332)
- Produces: `RaisePropertyChanged(nameof(IsOscConnected))` and `nameof(IsOscDisconnected)` fired after summary is recalculated

- [ ] **Step 1: Read the target method**

Read `MainWindowViewModel.cs` lines 16033-16092 to see `UpdateOscStatusSummary()`.

- [ ] **Step 2: Add PropertyChanged notifications**

At the end of `UpdateOscStatusSummary()`, just before the closing brace, add:

```csharp
        RaisePropertyChanged(nameof(IsOscConnected));
        RaisePropertyChanged(nameof(IsOscDisconnected));
    }
```

- [ ] **Step 3: Verify the edit**

```bash
dotnet build "E:\!!!Program to work on\Proper Crystal Relay\VrcTwitchOscBridge\VrcTwitchOscBridge.csproj" --no-restore
```
Expected: Build succeeds with no errors.

---

### Task 2: Remove Streaming Status Panel from Home (left column)

**Files:**
- Modify: `VrcTwitchOscBridge/MainWindow.xaml:1887-2448`

- [ ] **Step 1: Read current Streaming Status section**

Read `MainWindow.xaml` lines 1887-2450 to confirm the exact boundary.

- [ ] **Step 2: Remove the Streaming Status Border block**

Remove the entire Streaming Status `Border` block at lines 1887-2448. This is the block starting with:
```xaml
                    <Border Style="{StaticResource PanelBorderStyle}" Margin="0,16,0,0" Background="{DynamicResource PanelSecondaryBrush}" Padding="18">
                        <Border.Resources>
...
```
down to its closing `</Border>` just before the line:
```xaml
                    </StackPanel>
                </ScrollViewer>
            </Border>
```

- [ ] **Step 3: Verify the edit**

```bash
dotnet build "E:\!!!Program to work on\Proper Crystal Relay\VrcTwitchOscBridge\VrcTwitchOscBridge.csproj" --no-restore
```
Expected: Build succeeds.

---

### Task 3: Remove Old OSC Status Panel from Home (left column)

**Files:**
- Modify: `VrcTwitchOscBridge/MainWindow.xaml:1860-1885`

- [ ] **Step 1: Read current OSC Status section**

Read `MainWindow.xaml` lines 1860-1886 to confirm the exact boundary.

- [ ] **Step 2: Remove the old OSC Status Border block**

Remove lines 1860-1885 (the `Border` block containing the "OSC Status" heading, `OscBridgeSummary`, `OscStatusDetail`, restart buttons, desktop mode checkbox). This is the block starting with:
```xaml
                    <Border Style="{StaticResource PanelBorderStyle}" Margin="0,16,0,0" Background="{DynamicResource PanelSecondaryBrush}" Padding="18">
                        <StackPanel>
                            <TextBlock Text="{loc:Translate 'OSC Status'}" ...
```
down to its closing `</Border>`.

- [ ] **Step 3: Verify the edit**

```bash
dotnet build "E:\!!!Program to work on\Proper Crystal Relay\VrcTwitchOscBridge\VrcTwitchOscBridge.csproj" --no-restore
```
Expected: Build succeeds.

---

### Task 4: Replace 1x4 Status Row with 2x2 Grid + OSC Controls in Settings

**Files:**
- Modify: `VrcTwitchOscBridge/MainWindow.xaml:3581-3775`

- [ ] **Step 1: Read the 1x4 status row**

Read `MainWindow.xaml` lines 3578-3780 to see the exact boundaries of the 1x4 row and what section it's in.

- [ ] **Step 2: Replace the 1x4 row with 2x2 grid + OSC controls**

Remove lines 3581-3775 (the `Border` containing the old 1x4 Connection Status Cards row: VRChat, OSC, Twitch, Stream in a 1x7 grid).

Replace with:

```xaml
                            <!-- Connection Status -->
                            <Border Margin="0,20,0,0"
                                    Background="{DynamicResource NestedPanelBrush}"
                                    BorderBrush="{DynamicResource HighlightBorderBrush}"
                                    BorderThickness="1"
                                    CornerRadius="14"
                                    Padding="16,14">
                                <StackPanel>
                                    <TextBlock Text="{loc:Translate 'Connection Status'}"
                                               Style="{StaticResource HeadingTextStyle}"
                                               FontSize="15"
                                               FontWeight="Bold"
                                               Foreground="{DynamicResource AccentBrush}"
                                               Margin="0,0,0,12" />

                                    <!-- 2x2 Status Grid -->
                                    <Grid>
                                        <Grid.ColumnDefinitions>
                                            <ColumnDefinition Width="*" />
                                            <ColumnDefinition Width="8" />
                                            <ColumnDefinition Width="*" />
                                        </Grid.ColumnDefinitions>
                                        <Grid.RowDefinitions>
                                            <RowDefinition Height="Auto" />
                                            <RowDefinition Height="8" />
                                            <RowDefinition Height="Auto" />
                                        </Grid.RowDefinitions>

                                        <!-- VRChat (top-left) -->
                                        <Border Grid.Row="0" Grid.Column="0"
                                                Background="{DynamicResource PanelSecondaryBrush}"
                                                BorderBrush="{DynamicResource InputBorderBrush}"
                                                BorderThickness="1"
                                                CornerRadius="10"
                                                Padding="10,8">
                                            <StackPanel>
                                                <StackPanel Orientation="Horizontal">
                                                    <Ellipse Width="8" Height="8"
                                                             Fill="{DynamicResource MutedBrush}"
                                                             VerticalAlignment="Center"
                                                             Margin="0,0,6,0">
                                                        <Ellipse.Style>
                                                            <Style TargetType="Ellipse">
                                                                <Style.Triggers>
                                                                    <DataTrigger Binding="{Binding IsVrChatConnected}" Value="True">
                                                                        <Setter Property="Fill" Value="{DynamicResource AccentBrush}" />
                                                                    </DataTrigger>
                                                                </Style.Triggers>
                                                            </Style>
                                                        </Ellipse.Style>
                                                    </Ellipse>
                                                    <TextBlock Text="VRChat"
                                                               FontSize="10"
                                                               FontWeight="SemiBold"
                                                               Foreground="{DynamicResource MutedBrush}"
                                                               VerticalAlignment="Center" />
                                                </StackPanel>
                                                <TextBlock Text="Connected"
                                                           Margin="0,4,0,0"
                                                           FontSize="13"
                                                           FontWeight="SemiBold"
                                                           Foreground="{DynamicResource AccentBrush}"
                                                           Visibility="{Binding IsVrChatConnected, Converter={StaticResource BoolToVisibilityConverter}}" />
                                                <TextBlock Text="Disconnected"
                                                           Margin="0,4,0,0"
                                                           FontSize="13"
                                                           FontWeight="SemiBold"
                                                           Foreground="{DynamicResource MutedBrush}"
                                                           Visibility="{Binding IsVrChatDisconnected, Converter={StaticResource BoolToVisibilityConverter}}" />
                                            </StackPanel>
                                        </Border>

                                        <!-- OSC (top-right) -->
                                        <Border Grid.Row="0" Grid.Column="2"
                                                Background="{DynamicResource PanelSecondaryBrush}"
                                                BorderBrush="{DynamicResource InputBorderBrush}"
                                                BorderThickness="1"
                                                CornerRadius="10"
                                                Padding="10,8">
                                            <StackPanel>
                                                <StackPanel Orientation="Horizontal">
                                                    <Ellipse Width="8" Height="8"
                                                             Fill="{DynamicResource MutedBrush}"
                                                             VerticalAlignment="Center"
                                                             Margin="0,0,6,0">
                                                        <Ellipse.Style>
                                                            <Style TargetType="Ellipse">
                                                                <Style.Triggers>
                                                                    <DataTrigger Binding="{Binding IsOscConnected}" Value="True">
                                                                        <Setter Property="Fill" Value="{DynamicResource AccentBrush}" />
                                                                    </DataTrigger>
                                                                </Style.Triggers>
                                                            </Style>
                                                        </Ellipse.Style>
                                                    </Ellipse>
                                                    <TextBlock Text="OSC"
                                                               FontSize="10"
                                                               FontWeight="SemiBold"
                                                               Foreground="{DynamicResource MutedBrush}"
                                                               VerticalAlignment="Center" />
                                                </StackPanel>
                                                <TextBlock Text="Connected"
                                                           Margin="0,4,0,0"
                                                           FontSize="13"
                                                           FontWeight="SemiBold"
                                                           Foreground="{DynamicResource AccentBrush}"
                                                           Visibility="{Binding IsOscConnected, Converter={StaticResource BoolToVisibilityConverter}}" />
                                                <TextBlock Text="Disconnected"
                                                           Margin="0,4,0,0"
                                                           FontSize="13"
                                                           FontWeight="SemiBold"
                                                           Foreground="{DynamicResource MutedBrush}"
                                                           Visibility="{Binding IsOscDisconnected, Converter={StaticResource BoolToVisibilityConverter}}" />
                                            </StackPanel>
                                        </Border>

                                        <!-- Twitch (bottom-left) -->
                                        <Border Grid.Row="2" Grid.Column="0"
                                                Background="{DynamicResource PanelSecondaryBrush}"
                                                BorderBrush="{DynamicResource InputBorderBrush}"
                                                BorderThickness="1"
                                                CornerRadius="10"
                                                Padding="10,8">
                                            <StackPanel>
                                                <StackPanel Orientation="Horizontal">
                                                    <Ellipse Width="8" Height="8"
                                                             Fill="{DynamicResource MutedBrush}"
                                                             VerticalAlignment="Center"
                                                             Margin="0,0,6,0">
                                                        <Ellipse.Style>
                                                            <Style TargetType="Ellipse">
                                                                <Style.Triggers>
                                                                    <DataTrigger Binding="{Binding IsBroadcasterConnected}" Value="True">
                                                                        <Setter Property="Fill" Value="{DynamicResource AccentBrush}" />
                                                                    </DataTrigger>
                                                                </Style.Triggers>
                                                            </Style>
                                                        </Ellipse.Style>
                                                    </Ellipse>
                                                    <TextBlock Text="Twitch"
                                                               FontSize="10"
                                                               FontWeight="SemiBold"
                                                               Foreground="{DynamicResource MutedBrush}"
                                                               VerticalAlignment="Center" />
                                                </StackPanel>
                                                <TextBlock Text="Connected"
                                                           Margin="0,4,0,0"
                                                           FontSize="13"
                                                           FontWeight="SemiBold"
                                                           Foreground="{DynamicResource AccentBrush}"
                                                           Visibility="{Binding IsBroadcasterConnected, Converter={StaticResource BoolToVisibilityConverter}}" />
                                                <TextBlock Text="Disconnected"
                                                           Margin="0,4,0,0"
                                                           FontSize="13"
                                                           FontWeight="SemiBold"
                                                           Foreground="{DynamicResource MutedBrush}"
                                                           Visibility="{Binding IsBroadcasterDisconnected, Converter={StaticResource BoolToVisibilityConverter}}" />
                                            </StackPanel>
                                        </Border>

                                        <!-- Stream (bottom-right) -->
                                        <Border Grid.Row="2" Grid.Column="2"
                                                Background="{DynamicResource PanelSecondaryBrush}"
                                                BorderBrush="{DynamicResource InputBorderBrush}"
                                                BorderThickness="1"
                                                CornerRadius="10"
                                                Padding="10,8">
                                            <StackPanel>
                                                <StackPanel Orientation="Horizontal">
                                                    <Ellipse Width="8" Height="8"
                                                             Fill="{DynamicResource MutedBrush}"
                                                             VerticalAlignment="Center"
                                                             Margin="0,0,6,0">
                                                        <Ellipse.Style>
                                                            <Style TargetType="Ellipse">
                                                                <Style.Triggers>
                                                                    <DataTrigger Binding="{Binding IsBroadcasterLive}" Value="True">
                                                                        <Setter Property="Fill" Value="{DynamicResource AccentBrush}" />
                                                                    </DataTrigger>
                                                                </Style.Triggers>
                                                            </Style>
                                                        </Ellipse.Style>
                                                    </Ellipse>
                                                    <TextBlock Text="Stream"
                                                               FontSize="10"
                                                               FontWeight="SemiBold"
                                                               Foreground="{DynamicResource MutedBrush}"
                                                               VerticalAlignment="Center" />
                                                </StackPanel>
                                                <TextBlock Text="Live"
                                                           Margin="0,4,0,0"
                                                           FontSize="13"
                                                           FontWeight="SemiBold"
                                                           Foreground="{DynamicResource AccentBrush}"
                                                           Visibility="{Binding IsBroadcasterLive, Converter={StaticResource BoolToVisibilityConverter}}" />
                                                <TextBlock Text="Offline"
                                                           Margin="0,4,0,0"
                                                           FontSize="13"
                                                           FontWeight="SemiBold"
                                                           Foreground="{DynamicResource MutedBrush}"
                                                           Visibility="{Binding IsBroadcasterLive, Converter={StaticResource InverseBoolToVisibilityConverter}}" />
                                            </StackPanel>
                                        </Border>
                                    </Grid>

                                    <!-- OSC Controls Panel -->
                                    <Border Margin="0,12,0,0"
                                            Background="{DynamicResource PanelSecondaryBrush}"
                                            BorderBrush="{DynamicResource InputBorderBrush}"
                                            BorderThickness="1"
                                            CornerRadius="10"
                                            Padding="12">
                                        <StackPanel>
                                            <TextBlock Text="{Binding OscBridgeSummary}"
                                                       FontSize="13"
                                                       FontWeight="SemiBold"
                                                       Foreground="{DynamicResource TextBrush}"
                                                       TextWrapping="Wrap" />
                                            <TextBlock Margin="0,4,0,0"
                                                       Text="{Binding OscStatusDetail}"
                                                       Foreground="{DynamicResource MutedBrush}"
                                                       TextWrapping="Wrap"
                                                       FontSize="11" />
                                            <WrapPanel Margin="0,10,0,0">
                                                <Button Margin="0,0,8,8"
                                                        Style="{StaticResource SecondaryButtonStyle}"
                                                        Content="{loc:Translate 'Restart Crystal Relay'}"
                                                        Click="OnRestartCrystalRelayClicked" />
                                                <Button Margin="0,0,0,8"
                                                        Style="{StaticResource SecondaryButtonStyle}"
                                                        Content="{loc:Translate 'Restart VRChat + Crystal Relay'}"
                                                        Click="OnRestartVrChatAndCrystalRelayClicked" />
                                            </WrapPanel>
                                            <CheckBox Margin="0,4,0,0"
                                                      IsChecked="{Binding Settings.RestartVrChatInDesktopMode, UpdateSourceTrigger=PropertyChanged}">
                                                <TextBlock Text="{loc:Translate 'Restart VRChat in desktop mode'}"
                                                           TextWrapping="Wrap" />
                                            </CheckBox>
                                            <TextBlock Margin="26,4,0,0"
                                                       Text="{loc:Translate 'When checked, the VRChat restart button launches VRChat with --no-vr.'}"
                                                       Foreground="{DynamicResource MutedBrush}"
                                                       TextWrapping="Wrap"
                                                       FontSize="11" />
                                        </StackPanel>
                                    </Border>
                                </StackPanel>
                            </Border>
```

- [ ] **Step 3: Verify the edit**

```bash
dotnet build "E:\!!!Program to work on\Proper Crystal Relay\VrcTwitchOscBridge\VrcTwitchOscBridge.csproj" --no-restore
```
Expected: Build succeeds.

---

### Task 5: Final Build Verification and Review

- [ ] **Step 1: Run final build**

```bash
dotnet build "E:\!!!Program to work on\Proper Crystal Relay\VrcTwitchOscBridge\VrcTwitchOscBridge.csproj" --no-restore 2>&1
```
Expected: Build succeeds with 0 errors and 0 warnings.

- [ ] **Step 2: Verify changes match spec**

- Bug fix: `RaisePropertyChanged(nameof(IsOscConnected))` added to `UpdateOscStatusSummary()`
- Home panel: Streaming Status panel removed
- Home panel: Old OSC Status panel removed
- Settings: 1x4 row replaced with 2x2 grid
- Settings: OSC controls panel below the 2x2 grid
