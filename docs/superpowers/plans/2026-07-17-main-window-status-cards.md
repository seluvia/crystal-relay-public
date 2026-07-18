# Main Window Status Cards Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Add a row of 4 compact connection-status cards below the Redeem Library 2x2 nav grid in the main window.

**Architecture:** Pure XAML layout change in `MainWindow.xaml`. Reuses existing ViewModel bindings — no new properties or services.

**Tech Stack:** WPF XAML, existing panel styles

## Global Constraints

- Use existing `NestedPanelBrush` / `HighlightBorderBrush` / `PanelBorderStyle` patterns
- Do not add new ViewModel properties or services
- Use existing `VrChatStatus`, `OscBridgeSummary`, `BroadcasterStatusDisplayText`, `IsBroadcasterLive` bindings
- No new localization keys needed — labels use simple English text matching existing patterns

---

### Task 1: Add status card row to MainWindow.xaml

**Files:**
- Modify: `VrcTwitchOscBridge/MainWindow.xaml:3580-3583` (inside the Redeem Library StackPanel, after the 2x2 grid closes, before `</StackPanel>`)

- [ ] **Step 1: Add the status card row below the 2x2 nav grid**

Insert after `</Grid>` (line ~3580, closes the 2x2 nav grid), before `</StackPanel>` (line ~3581):

```xml
                            <!-- Connection Status Cards -->
                            <Border Margin="0,20,0,0"
                                    Background="{DynamicResource NestedPanelBrush}"
                                    BorderBrush="{DynamicResource HighlightBorderBrush}"
                                    BorderThickness="1"
                                    CornerRadius="14"
                                    Padding="16,12">
                                <Grid>
                                    <Grid.ColumnDefinitions>
                                        <ColumnDefinition Width="*" />
                                        <ColumnDefinition Width="12" />
                                        <ColumnDefinition Width="*" />
                                        <ColumnDefinition Width="12" />
                                        <ColumnDefinition Width="*" />
                                        <ColumnDefinition Width="12" />
                                        <ColumnDefinition Width="*" />
                                    </Grid.ColumnDefinitions>

                                    <!-- VRChat -->
                                    <Border Grid.Column="0"
                                            Background="{DynamicResource PanelSecondaryBrush}"
                                            BorderBrush="{DynamicResource InputBorderBrush}"
                                            BorderThickness="1"
                                            CornerRadius="10"
                                            Padding="10,8">
                                        <StackPanel>
                                            <StackPanel Orientation="Horizontal">
                                                <Ellipse Width="8" Height="8"
                                                         Fill="{DynamicResource AccentBrush}"
                                                         VerticalAlignment="Center"
                                                         Margin="0,0,6,0" />
                                                <TextBlock Text="VRChat"
                                                           FontSize="10"
                                                           FontWeight="SemiBold"
                                                           Foreground="{DynamicResource MutedBrush}"
                                                           VerticalAlignment="Center" />
                                            </StackPanel>
                                            <TextBlock Text="{Binding VrChatStatus}"
                                                       Margin="0,4,0,0"
                                                       FontSize="13"
                                                       FontWeight="SemiBold"
                                                       Foreground="{DynamicResource TextBrush}"
                                                       TextTrimming="CharacterEllipsis" />
                                        </StackPanel>
                                    </Border>

                                    <!-- OSC -->
                                    <Border Grid.Column="2"
                                            Background="{DynamicResource PanelSecondaryBrush}"
                                            BorderBrush="{DynamicResource InputBorderBrush}"
                                            BorderThickness="1"
                                            CornerRadius="10"
                                            Padding="10,8">
                                        <StackPanel>
                                            <StackPanel Orientation="Horizontal">
                                                <Ellipse Width="8" Height="8"
                                                         Fill="{DynamicResource AccentBrush}"
                                                         VerticalAlignment="Center"
                                                         Margin="0,0,6,0" />
                                                <TextBlock Text="OSC"
                                                           FontSize="10"
                                                           FontWeight="SemiBold"
                                                           Foreground="{DynamicResource MutedBrush}"
                                                           VerticalAlignment="Center" />
                                            </StackPanel>
                                            <TextBlock Text="{Binding OscBridgeSummary}"
                                                       Margin="0,4,0,0"
                                                       FontSize="13"
                                                       FontWeight="SemiBold"
                                                       Foreground="{DynamicResource TextBrush}"
                                                       TextTrimming="CharacterEllipsis" />
                                        </StackPanel>
                                    </Border>

                                    <!-- Twitch -->
                                    <Border Grid.Column="4"
                                            Background="{DynamicResource PanelSecondaryBrush}"
                                            BorderBrush="{DynamicResource InputBorderBrush}"
                                            BorderThickness="1"
                                            CornerRadius="10"
                                            Padding="10,8">
                                        <StackPanel>
                                            <StackPanel Orientation="Horizontal">
                                                <Ellipse Width="8" Height="8"
                                                         Fill="{DynamicResource AccentBrush}"
                                                         VerticalAlignment="Center"
                                                         Margin="0,0,6,0" />
                                                <TextBlock Text="Twitch"
                                                           FontSize="10"
                                                           FontWeight="SemiBold"
                                                           Foreground="{DynamicResource MutedBrush}"
                                                           VerticalAlignment="Center" />
                                            </StackPanel>
                                            <TextBlock Text="{Binding BroadcasterStatusDisplayText}"
                                                       Margin="0,4,0,0"
                                                       FontSize="13"
                                                       FontWeight="SemiBold"
                                                       Foreground="{DynamicResource TextBrush}"
                                                       TextTrimming="CharacterEllipsis" />
                                        </StackPanel>
                                    </Border>

                                    <!-- Stream -->
                                    <Border Grid.Column="6"
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
                                            <TextBlock Margin="0,4,0,0"
                                                       FontSize="13"
                                                       FontWeight="SemiBold"
                                                       TextTrimming="CharacterEllipsis">
                                                <TextBlock.Style>
                                                    <Style TargetType="TextBlock">
                                                        <Setter Property="Text" Value="{loc:Translate 'Offline'}" />
                                                        <Setter Property="Foreground" Value="{DynamicResource MutedBrush}" />
                                                        <Style.Triggers>
                                                            <DataTrigger Binding="{Binding IsBroadcasterLive}" Value="True">
                                                                <Setter Property="Text" Value="{loc:Translate 'Live'}" />
                                                                <Setter Property="Foreground" Value="{DynamicResource AccentBrush}" />
                                                            </DataTrigger>
                                                        </Style.Triggers>
                                                    </Style>
                                                </TextBlock.Style>
                                            </TextBlock>
                                        </StackPanel>
                                    </Border>
                                </Grid>
                            </Border>
```

- [ ] **Step 2: Build and verify**

```powershell
dotnet build "E:\!!!Program to work on\Proper Crystal Relay\VrcTwitchOscBridge\VrcTwitchOscBridge.csproj" --no-restore
```

Expected: Build succeeded, 0 errors.

- [ ] **Step 3: Commit**

```bash
git add VrcTwitchOscBridge/MainWindow.xaml
git commit -m "feat: add connection status cards below Redeem Library nav grid"
```
