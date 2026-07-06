# Movement Redeem Editor Enhancement Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Add all missing editor fields to the MovementRedeemsManagerWindow slide-out editor (trigger config, direction picker, amount scaling, avatar scope, bot message, set triggers).

**Architecture:** Expand the slide-out panel width, replace the current flat editor with scrollable bordered sections, bind directly to `TriggerRule` properties on the manager VM. Trigger type selector drives visibility of conditional sub-sections via `DataTrigger`.

**Tech Stack:** C# WPF, MVVM with ObservableObject, XAML DataTemplates/Triggers

---

### Task 1: Add editor properties, enum lists, and commands to MovementRedeemsManagerViewModel

**Files:**
- Modify: `E:\!!!Program to work on\Proper Crystal Relay\VrcTwitchOscBridge\ViewModels\MovementRedeemsManagerViewModel.cs`

- [ ] **Step 1: Add backing fields**

Add after `private bool disposed;`:
```csharp
private TriggerRule? selectedRule;
private bool isNewRule;
```

- [ ] **Step 2: Add SelectedRule, IsNewRule, enum lists, computed visibility properties**

Add after `public bool IsEditorOpen`:
```csharp
public TriggerRule? SelectedRule
{
    get => selectedRule;
    set
    {
        if (SetProperty(ref selectedRule, value))
        {
            RaisePropertyChanged(nameof(UsesChannelPointReward));
            RaisePropertyChanged(nameof(UsesChatCommand));
            RaisePropertyChanged(nameof(UsesBits));
            RaisePropertyChanged(nameof(UsesSubscription));
            RaisePropertyChanged(nameof(UsesFollow));
            RaisePropertyChanged(nameof(UsesGiftSub));
            RaisePropertyChanged(nameof(IsAxisType));
            RaisePropertyChanged(nameof(IsVrOnly));
            RaisePropertyChanged(nameof(EditorTitle));
        }
    }
}

public bool IsNewRule
{
    get => isNewRule;
    set => SetProperty(ref isNewRule, value);
}

public string EditorTitle => IsNewRule ? "Add Movement Rule" : "Edit Movement Rule";

public IList TriggerTypeValues => Enum.GetValues(typeof(TwitchTriggerType));
public IList RewardSyncModeValues => Enum.GetValues(typeof(TwitchRewardSyncMode));
public IList ChatCommandPermissionValues => Enum.GetValues(typeof(ChatCommandPermission));
public IList OscParameterTypeValues => Enum.GetValues(typeof(OscParameterType));

public bool UsesChannelPointReward => selectedRule?.TriggerType == TwitchTriggerType.ChannelPoints;
public bool UsesChatCommand => selectedRule?.TriggerType == TwitchTriggerType.ChatCommand;
public bool UsesBits => selectedRule?.TriggerType == TwitchTriggerType.Bits;
public bool UsesSubscription => selectedRule?.TriggerType == TwitchTriggerType.Subscriptions;
public bool UsesGiftSub => selectedRule?.TriggerType == TwitchTriggerType.GiftSubscription;
public bool UsesFollow => selectedRule?.TriggerType == TwitchTriggerType.Follow;
public bool IsAxisType => selectedRule is not null && MovementTypeClassifier.IsAxisType(selectedRule.MovementDirection);
public bool IsVrOnly => selectedRule is not null && MovementTypeClassifier.IsVrOnly(selectedRule.MovementDirection);
```

- [ ] **Step 3: Add IList import**

Ensure the using at the top includes:
```csharp
using System.Collections;
```

- [ ] **Step 4: Update OpenEditorCommand**

Replace existing:
```csharp
OpenEditorCommand = new RelayCommand(p => { SelectedCard = p as MovementRedeemCardViewModel; IsEditorOpen = SelectedCard is not null; });
```

With:
```csharp
OpenEditorCommand = new RelayCommand(p =>
{
    SelectedCard = p as MovementRedeemCardViewModel;
    if (SelectedCard is not null)
    {
        SelectedRule = SelectedCard.GetRule();
        IsNewRule = false;
        IsEditorOpen = true;
    }
});
```

- [ ] **Step 5: Add editor commands after TestCardCommand**

Add after `public RelayCommand TestCardCommand { get; }`:
```csharp
public RelayCommand SaveEditorCommand { get; }
public RelayCommand DeleteRuleCommand { get; }
public RelayCommand TestRuleCommand { get; }
public RelayCommand AddSetTriggerCommand { get; }
public RelayCommand RemoveSetTriggerCommand { get; }
```

Add initialization after the existing command assignments:
```csharp
SaveEditorCommand = new RelayCommand(SaveEditor);
DeleteRuleCommand = new RelayCommand(() => DeleteCard(SelectedCard));
TestRuleCommand = new RelayCommand(() => { if (SelectedCard is not null) TestCard(SelectedCard); });
AddSetTriggerCommand = new RelayCommand(AddSetTrigger);
RemoveSetTriggerCommand = new RelayCommand(p => RemoveSetTrigger(p));
```

- [ ] **Step 6: Add SaveEditor method**

Add after `TestCard`:
```csharp
private void SaveEditor()
{
    if (SelectedRule is null) return;
    RefreshCards();
    IsEditorOpen = false;
}
```

- [ ] **Step 7: Add AddSetTrigger and RemoveSetTrigger methods**

Add after `SaveEditor`:
```csharp
private void AddSetTrigger()
{
    if (SelectedRule is null) return;
    SelectedRule.SetTriggerActions.Add(new SetTriggerAction());
}

private void RemoveSetTrigger(object? param)
{
    if (param is SetTriggerAction action && SelectedRule is not null)
    {
        SelectedRule.SetTriggerActions.Remove(action);
    }
}
```

- [ ] **Step 8: Add trigger type change notification helper**

Add at the end, before `Dispose`:
```csharp
public void OnTriggerTypeChanged()
{
    RaisePropertyChanged(nameof(UsesChannelPointReward));
    RaisePropertyChanged(nameof(UsesChatCommand));
    RaisePropertyChanged(nameof(UsesBits));
    RaisePropertyChanged(nameof(UsesSubscription));
    RaisePropertyChanged(nameof(UsesGiftSub));
    RaisePropertyChanged(nameof(UsesFollow));
}
```

- [ ] **Step 9: Update AddNewRule to open editor for new rule**

Replace `AddNewRule()`:
```csharp
private void AddNewRule()
{
    var firstSet = settings.MovementRedeemSets.FirstOrDefault();
    if (firstSet is null)
    {
        firstSet = new MovementRedeemSet { Name = "Default" };
        settings.MovementRedeemSets.Add(firstSet);
    }
    var rule = new TriggerRule
    {
        Name = "New Movement Rule",
        MovementDirection = PlayerMovementDirection.Forward,
        ActionType = OscActionType.PlayerMovement,
        DurationSeconds = 5,
        CooldownSeconds = 60
    };
    firstSet.MovementRules.Add(rule);
    RefreshCards();
    var card = Cards.FirstOrDefault(c => c.Id == rule.Id);
    if (card is not null)
    {
        SelectedCard = card;
        SelectedRule = rule;
        IsNewRule = true;
        IsEditorOpen = true;
    }
}
```

- [ ] **Step 10: Update DeleteCard to close editor if deleting selected**

Replace `DeleteCard`:
```csharp
private void DeleteCard(MovementRedeemCardViewModel? card)
{
    if (card is null) return;
    var rule = card.GetRule();
    foreach (var set in settings.MovementRedeemSets)
    {
        if (set.MovementRules.Remove(rule))
            break;
    }
    if (SelectedCard == card)
    {
        SelectedCard = null;
        SelectedRule = null;
        IsEditorOpen = false;
    }
    RefreshCards();
}
```

- [ ] **Step 11: Commit**

```bash
git add "VrcTwitchOscBridge/ViewModels/MovementRedeemsManagerViewModel.cs"
git commit -m "feat: add editor properties and commands to MovementRedeemsManagerViewModel"
```

---

### Task 2: Make GetDisplayName public static on MovementRedeemCardViewModel

**Files:**
- Modify: `E:\!!!Program to work on\Proper Crystal Relay\VrcTwitchOscBridge\ViewModels\MovementRedeemCardViewModel.cs`

- [ ] **Step 1: Change GetDisplayName to public static**

Change `private static string GetDisplayName` to `public static string GetDisplayName`:
```csharp
public static string GetDisplayName(PlayerMovementDirection direction) => direction switch
```

- [ ] **Step 2: Commit**

```bash
git add "VrcTwitchOscBridge/ViewModels/MovementRedeemCardViewModel.cs"
git commit -m "feat: make GetDisplayName public static for reuse in editor"
```

---

### Task 3: Rewrite the slide-out editor XAML

**Files:**
- Modify: `E:\!!!Program to work on\Proper Crystal Relay\VrcTwitchOscBridge\MovementRedeemsManagerWindow.xaml`

- [ ] **Step 1: Replace the overlay opening Grid and backdrop**

Replace lines 497-537 (from `<Grid Grid.Row="0" Grid.RowSpan="2"` through the title bar `</Border>`) with:

```xml
<Grid Grid.Row="0" Grid.RowSpan="2"
      Visibility="{Binding IsEditorOpen, Converter={StaticResource BoolToVisibilityConverter}}">
    <Border Background="{DynamicResource BackdropBrush}"
            MouseLeftButtonUp="OnEditorBackdropClicked" />
    <Border Width="480"
            HorizontalAlignment="Right"
            Background="{DynamicResource PanelBrush}"
            BorderBrush="{DynamicResource BorderBrush}"
            BorderThickness="1,0,0,0">
        <Grid>
            <Grid.RowDefinitions>
                <RowDefinition Height="Auto" />
                <RowDefinition Height="*" />
            </Grid.RowDefinitions>

            <Border Grid.Row="0"
                    Background="{DynamicResource TitleBarBrush}"
                    BorderBrush="{DynamicResource BorderBrush}"
                    BorderThickness="0,0,0,1"
                    Padding="12,10">
                <DockPanel LastChildFill="True">
                    <Button DockPanel.Dock="Right"
                            Content="✕"
                            Background="Transparent"
                            Foreground="{DynamicResource TitleBarTextBrush}"
                            BorderBrush="{DynamicResource BorderBrush}"
                            Padding="6,2"
                            Margin="6,0,0,0"
                            Command="{Binding CloseEditorCommand}"
                            AutomationProperties.Name="{loc:Translate 'Close'}"
                            ToolTip="{loc:Translate 'Close'}" />
                    <StackPanel>
                        <TextBlock Text="{Binding EditorTitle}"
                                   FontWeight="Bold"
                                   FontSize="13"
                                   Foreground="{DynamicResource TitleBarTextBrush}" />
                        <TextBlock Text="{Binding SelectedRule.MovementDirection}"
                                   Margin="0,2,0,0"
                                   FontSize="10"
                                   Foreground="{DynamicResource TitleBarSubTextBrush}" />
                    </StackPanel>
                </DockPanel>
            </Border>
```

- [ ] **Step 2: Replace everything from `<ScrollViewer Grid.Row="1"` through the end of the file**

Replace lines 541-674 (the entire editor scrollviewer content through closing Window tags) with the new full editor (see next steps).

- [ ] **Step 2a: The new ScrollViewer and Section 1 (General Settings)**

```xml
<ScrollViewer Grid.Row="1"
              Margin="14,14,14,14"
              VerticalScrollBarVisibility="Auto"
              HorizontalScrollBarVisibility="Disabled">
    <StackPanel TextElement.Foreground="{DynamicResource TitleBarTextBrush}">
        <!-- General Settings -->
        <Border Padding="12"
                CornerRadius="14"
                Background="{DynamicResource NestedPanelBrush}"
                BorderBrush="{DynamicResource BorderBrush}"
                BorderThickness="1"
                TextElement.Foreground="{DynamicResource TitleBarTextBrush}">
            <StackPanel>
                <TextBlock Text="General Settings"
                           FontWeight="Bold"
                           FontSize="13"
                           Margin="0,0,0,8" />
                <UniformGrid Columns="2" Margin="0,0,0,8">
                    <StackPanel Margin="0,0,8,0">
                        <TextBlock Text="{loc:Translate 'Rule Name'}"
                                   FontWeight="SemiBold" />
                        <TextBox Text="{Binding SelectedRule.Name, UpdateSourceTrigger=PropertyChanged}"
                                 Margin="0,6,0,0" />
                    </StackPanel>
                    <StackPanel Margin="8,0,0,0">
                        <TextBlock Text="{loc:Translate 'Enabled'}"
                                   FontWeight="SemiBold" />
                        <ToggleButton IsChecked="{Binding SelectedRule.IsEnabled, Mode=TwoWay}"
                                      Cursor="Hand"
                                      Width="36"
                                      Height="20"
                                      Margin="0,6,0,0"
                                      HorizontalAlignment="Left">
                            <ToggleButton.Style>
                                <Style TargetType="ToggleButton">
                                    <Setter Property="Template">
                                        <Setter.Value>
                                            <ControlTemplate TargetType="ToggleButton">
                                                <Border x:Name="ToggleTrack"
                                                        Width="36" Height="20"
                                                        CornerRadius="10"
                                                        Background="{DynamicResource InputBrush}"
                                                        BorderBrush="{DynamicResource InputBorderBrush}"
                                                        BorderThickness="1"
                                                        Cursor="Hand">
                                                    <Border x:Name="ToggleThumb"
                                                            Width="16" Height="16"
                                                            CornerRadius="8"
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
                    </StackPanel>
                </UniformGrid>
                <TextBlock Text="{loc:Translate 'Movement Direction'}"
                           FontWeight="SemiBold" />
                <ComboBox ItemsSource="{Binding MovementDirections}"
                          SelectedValue="{Binding SelectedRule.MovementDirection}"
                          DisplayMemberPath="Display"
                          SelectedValuePath="Value"
                          Margin="0,6,0,0" />
                <Border Padding="6,2"
                        CornerRadius="6"
                        Background="{DynamicResource AccentDimBrush}"
                        BorderBrush="{DynamicResource BorderBrush}"
                        BorderThickness="1"
                        Margin="0,6,0,0"
                        Visibility="{Binding IsVrOnly, Converter={StaticResource BoolToVisibilityConverter}}">
                    <TextBlock Text="{loc:Translate 'VR Only'}"
                               FontSize="10"
                               Foreground="{DynamicResource TitleBarSubTextBrush}" />
                </Border>
            </StackPanel>
        </Border>
```

- [ ] **Step 2b: Section 2 (Trigger Configuration)**

```xml
        <!-- Trigger Configuration -->
        <Border Margin="0,12,0,0"
                Padding="12"
                CornerRadius="14"
                Background="{DynamicResource NestedPanelBrush}"
                BorderBrush="{DynamicResource BorderBrush}"
                BorderThickness="1">
            <StackPanel>
                <TextBlock Text="Trigger Configuration"
                           FontWeight="Bold"
                           FontSize="13"
                           Margin="0,0,0,8" />
                <TextBlock Text="{loc:Translate 'Trigger Type'}"
                           FontWeight="SemiBold" />
                <ComboBox ItemsSource="{Binding TriggerTypeValues}"
                          SelectedItem="{Binding SelectedRule.TriggerType}"
                          Margin="0,6,0,0" />

                <!-- Channel Points sub-section -->
                <Border Padding="10"
                        CornerRadius="10"
                        Background="{DynamicResource PanelSecondaryBrush}"
                        BorderBrush="{DynamicResource BorderBrush}"
                        BorderThickness="1"
                        Margin="0,12,0,0"
                        Visibility="{Binding UsesChannelPointReward, Converter={StaticResource BoolToVisibilityConverter}}">
                    <StackPanel>
                        <TextBlock Text="{loc:Translate 'Reward Sync Mode'}"
                                   FontWeight="SemiBold" />
                        <ComboBox ItemsSource="{Binding RewardSyncModeValues}"
                                  SelectedItem="{Binding SelectedRule.RewardSyncMode}"
                                  Margin="0,6,0,0" />
                        <UniformGrid Columns="2" Margin="0,12,0,0">
                            <StackPanel Margin="0,0,8,0">
                                <TextBlock Text="{loc:Translate 'Reward Title'}"
                                           FontWeight="SemiBold" />
                                <TextBox Text="{Binding SelectedRule.ChannelPointRewardTitle, UpdateSourceTrigger=PropertyChanged}"
                                         Margin="0,6,0,0" />
                            </StackPanel>
                            <StackPanel Margin="8,0,0,0">
                                <TextBlock Text="{loc:Translate 'Cost'}"
                                           FontWeight="SemiBold" />
                                <TextBox Text="{Binding SelectedRule.ChannelPointRewardCost, UpdateSourceTrigger=PropertyChanged}"
                                         Margin="0,6,0,0" />
                            </StackPanel>
                        </UniformGrid>
                        <TextBlock Text="{loc:Translate 'Description'}"
                                   FontWeight="SemiBold"
                                   Margin="0,12,0,0" />
                        <TextBox Text="{Binding SelectedRule.ChannelPointRewardDescription, UpdateSourceTrigger=PropertyChanged}"
                                 Margin="0,6,0,0" />
                        <UniformGrid Columns="2" Margin="0,12,0,0">
                            <StackPanel Margin="0,0,8,0">
                                <TextBlock Text="{loc:Translate 'Ready Color'}"
                                           FontWeight="SemiBold" />
                                <TextBox Text="{Binding SelectedRule.ManagedRewardReadyColor, UpdateSourceTrigger=PropertyChanged}"
                                         Margin="0,6,0,0" />
                            </StackPanel>
                            <StackPanel Margin="8,0,0,0">
                                <TextBlock Text="{loc:Translate 'Cooldown Color'}"
                                           FontWeight="SemiBold" />
                                <TextBox Text="{Binding SelectedRule.ManagedRewardCooldownColor, UpdateSourceTrigger=PropertyChanged}"
                                         Margin="0,6,0,0" />
                            </StackPanel>
                        </UniformGrid>
                        <CheckBox IsChecked="{Binding SelectedRule.DeleteManagedRewardWhenInactive}"
                                  Content="{loc:Translate 'Delete reward when inactive'}"
                                  Margin="0,12,0,0" />
                    </StackPanel>
                </Border>

                <!-- Chat Command sub-section -->
                <Border Padding="10"
                        CornerRadius="10"
                        Background="{DynamicResource PanelSecondaryBrush}"
                        BorderBrush="{DynamicResource BorderBrush}"
                        BorderThickness="1"
                        Margin="0,12,0,0"
                        Visibility="{Binding UsesChatCommand, Converter={StaticResource BoolToVisibilityConverter}}">
                    <StackPanel>
                        <UniformGrid Columns="2">
                            <StackPanel Margin="0,0,8,0">
                                <TextBlock Text="{loc:Translate 'Command Text'}"
                                           FontWeight="SemiBold" />
                                <TextBox Text="{Binding SelectedRule.ChatCommandText, UpdateSourceTrigger=PropertyChanged}"
                                         Margin="0,6,0,0" />
                            </StackPanel>
                            <StackPanel Margin="8,0,0,0">
                                <TextBlock Text="{loc:Translate 'Permission'}"
                                           FontWeight="SemiBold" />
                                <ComboBox ItemsSource="{Binding ChatCommandPermissionValues}"
                                          SelectedItem="{Binding SelectedRule.ChatCommandPermission}"
                                          Margin="0,6,0,0" />
                            </StackPanel>
                        </UniformGrid>
                    </StackPanel>
                </Border>

                <!-- Bits sub-section -->
                <Border Padding="10"
                        CornerRadius="10"
                        Background="{DynamicResource PanelSecondaryBrush}"
                        BorderBrush="{DynamicResource BorderBrush}"
                        BorderThickness="1"
                        Margin="0,12,0,0"
                        Visibility="{Binding UsesBits, Converter={StaticResource BoolToVisibilityConverter}}">
                    <StackPanel>
                        <TextBlock Text="{loc:Translate 'Minimum Bits'}"
                                   FontWeight="SemiBold" />
                        <TextBox Text="{Binding SelectedRule.MinimumAmount, UpdateSourceTrigger=PropertyChanged}"
                                 Margin="0,6,0,0" />
                        <CheckBox IsChecked="{Binding SelectedRule.BitsKeywordEnabled}"
                                  Content="{loc:Translate 'Force motion keyword'}"
                                  Margin="0,12,0,0" />
                        <TextBox Text="{Binding SelectedRule.SupporterKeywordText, UpdateSourceTrigger=PropertyChanged}"
                                 Margin="0,6,0,0"
                                 Visibility="{Binding SelectedRule.BitsKeywordEnabled, Converter={StaticResource BoolToVisibilityConverter}}" />
                    </StackPanel>
                </Border>

                <!-- Subs sub-section -->
                <Border Padding="10"
                        CornerRadius="10"
                        Background="{DynamicResource PanelSecondaryBrush}"
                        BorderBrush="{DynamicResource BorderBrush}"
                        BorderThickness="1"
                        Margin="0,12,0,0"
                        Visibility="{Binding UsesSubscription, Converter={StaticResource BoolToVisibilityConverter}}">
                    <StackPanel>
                        <TextBlock Text="{loc:Translate 'Minimum Subs'}"
                                   FontWeight="SemiBold" />
                        <TextBox Text="{Binding SelectedRule.MinimumAmount, UpdateSourceTrigger=PropertyChanged}"
                                 Margin="0,6,0,0" />
                        <CheckBox IsChecked="{Binding SelectedRule.IsGiftSubscription}"
                                  Content="{loc:Translate 'Gift Sub'}"
                                  Margin="0,12,0,0" />
                    </StackPanel>
                </Border>

                <!-- Gift Sub sub-section -->
                <Border Padding="10"
                        CornerRadius="10"
                        Background="{DynamicResource PanelSecondaryBrush}"
                        BorderBrush="{DynamicResource BorderBrush}"
                        BorderThickness="1"
                        Margin="0,12,0,0"
                        Visibility="{Binding UsesGiftSub, Converter={StaticResource BoolToVisibilityConverter}}">
                    <StackPanel>
                        <TextBlock Text="{loc:Translate 'Minimum Gift Subs'}"
                                   FontWeight="SemiBold" />
                        <TextBox Text="{Binding SelectedRule.MinimumAmount, UpdateSourceTrigger=PropertyChanged}"
                                 Margin="0,6,0,0" />
                    </StackPanel>
                </Border>

                <!-- Follow sub-section -->
                <Border Padding="10"
                        CornerRadius="10"
                        Background="{DynamicResource PanelSecondaryBrush}"
                        BorderBrush="{DynamicResource BorderBrush}"
                        BorderThickness="1"
                        Margin="0,12,0,0"
                        Visibility="{Binding UsesFollow, Converter={StaticResource BoolToVisibilityConverter}}">
                    <TextBlock Text="{loc:Translate 'Triggers on follow event.'}"
                               Foreground="{DynamicResource TitleBarSubTextBrush}" />
                </Border>
            </StackPanel>
        </Border>
```

- [ ] **Step 2c: Section 3 (Movement Behavior)**

```xml
        <!-- Movement Behavior -->
        <Border Margin="0,12,0,0"
                Padding="12"
                CornerRadius="14"
                Background="{DynamicResource NestedPanelBrush}"
                BorderBrush="{DynamicResource BorderBrush}"
                BorderThickness="1">
            <StackPanel>
                <TextBlock Text="Movement Behavior"
                           FontWeight="Bold"
                           FontSize="13"
                           Margin="0,0,0,8" />
                <UniformGrid Columns="2">
                    <StackPanel Margin="0,0,8,0">
                        <TextBlock Text="{loc:Translate 'Duration (seconds)'}"
                                   FontWeight="SemiBold" />
                        <TextBox Text="{Binding SelectedRule.DurationSeconds, UpdateSourceTrigger=PropertyChanged}"
                                 Margin="0,6,0,0" />
                        <TextBlock Text="{loc:Translate 'Minimum 1 second.'}"
                                   Foreground="{DynamicResource TitleBarSubTextBrush}"
                                   FontSize="10"
                                   Margin="0,4,0,0" />
                    </StackPanel>
                    <StackPanel Margin="8,0,0,0">
                        <TextBlock Text="{loc:Translate 'Cooldown (seconds)'}"
                                   FontWeight="SemiBold" />
                        <TextBox Text="{Binding SelectedRule.CooldownSeconds, UpdateSourceTrigger=PropertyChanged}"
                                 Margin="0,6,0,0" />
                        <TextBlock Text="{loc:Translate '0 means no cooldown.'}"
                                   Foreground="{DynamicResource TitleBarSubTextBrush}"
                                   FontSize="10"
                                   Margin="0,4,0,0" />
                    </StackPanel>
                </UniformGrid>

                <!-- Speed (axis types only) -->
                <Border Padding="10"
                        CornerRadius="10"
                        Background="{DynamicResource PanelSecondaryBrush}"
                        BorderBrush="{DynamicResource BorderBrush}"
                        BorderThickness="1"
                        Margin="0,12,0,0"
                        Visibility="{Binding IsAxisType, Converter={StaticResource BoolToVisibilityConverter}}">
                    <StackPanel>
                        <TextBlock Text="{loc:Translate 'Speed (0.1 - 1.0)'}"
                                   FontWeight="SemiBold" />
                        <TextBox Text="{Binding SelectedRule.FloatValue, UpdateSourceTrigger=PropertyChanged}"
                                 Margin="0,6,0,0" />
                    </StackPanel>
                </Border>

                <!-- Bits amount scaling -->
                <Border Padding="10"
                        CornerRadius="10"
                        Background="{DynamicResource PanelSecondaryBrush}"
                        BorderBrush="{DynamicResource BorderBrush}"
                        BorderThickness="1"
                        Margin="0,12,0,0"
                        Visibility="{Binding UsesBits, Converter={StaticResource BoolToVisibilityConverter}}">
                    <StackPanel>
                        <CheckBox IsChecked="{Binding SelectedRule.AmountScaledDurationEnabled}"
                                  Content="{loc:Translate 'Amount-scaled duration'}" />
                        <UniformGrid Columns="2" Margin="0,8,0,0"
                                     Visibility="{Binding SelectedRule.AmountScaledDurationEnabled, Converter={StaticResource BoolToVisibilityConverter}}">
                            <StackPanel Margin="0,0,8,0">
                                <TextBlock Text="{loc:Translate 'Units per duration'}"
                                           FontSize="10" />
                                <TextBox Text="{Binding SelectedRule.BitsAmountUnitsPerDuration, UpdateSourceTrigger=PropertyChanged}"
                                         Margin="0,4,0,0" />
                            </StackPanel>
                            <StackPanel Margin="8,0,0,0">
                                <TextBlock Text="{loc:Translate 'Seconds per unit'}"
                                           FontSize="10" />
                                <TextBox Text="{Binding SelectedRule.BitsSecondsPerAmountUnit, UpdateSourceTrigger=PropertyChanged}"
                                         Margin="0,4,0,0" />
                            </StackPanel>
                        </UniformGrid>
                    </StackPanel>
                </Border>

                <!-- Sub tier duration scaling -->
                <Border Padding="10"
                        CornerRadius="10"
                        Background="{DynamicResource PanelSecondaryBrush}"
                        BorderBrush="{DynamicResource BorderBrush}"
                        BorderThickness="1"
                        Margin="0,12,0,0"
                        Visibility="{Binding UsesSubscription, Converter={StaticResource BoolToVisibilityConverter}}">
                    <StackPanel>
                        <TextBlock Text="{loc:Translate 'Sub duration scaling'}"
                                   FontWeight="SemiBold"
                                   Margin="0,0,0,8" />
                        <UniformGrid Columns="3">
                            <StackPanel Margin="0,0,4,0">
                                <TextBlock Text="T1 sec/sub" FontSize="10" />
                                <TextBox Text="{Binding SelectedRule.SubscriptionTier1SecondsPerSub, UpdateSourceTrigger=PropertyChanged}"
                                         Margin="0,4,0,0" />
                            </StackPanel>
                            <StackPanel Margin="4,0,4,0">
                                <TextBlock Text="T2 sec/sub" FontSize="10" />
                                <TextBox Text="{Binding SelectedRule.SubscriptionTier2SecondsPerSub, UpdateSourceTrigger=PropertyChanged}"
                                         Margin="0,4,0,0" />
                            </StackPanel>
                            <StackPanel Margin="4,0,0,0">
                                <TextBlock Text="T3 sec/sub" FontSize="10" />
                                <TextBox Text="{Binding SelectedRule.SubscriptionTier3SecondsPerSub, UpdateSourceTrigger=PropertyChanged}"
                                         Margin="0,4,0,0" />
                            </StackPanel>
                        </UniformGrid>
                    </StackPanel>
                </Border>

                <CheckBox IsChecked="{Binding SelectedRule.MaxAccumulatedDurationEnabled}"
                          Content="{loc:Translate 'Max accumulated duration'}"
                          Margin="0,12,0,0" />
                <TextBox Text="{Binding SelectedRule.MaxAccumulatedDurationSeconds, UpdateSourceTrigger=PropertyChanged}"
                         Margin="0,6,0,0"
                         Visibility="{Binding SelectedRule.MaxAccumulatedDurationEnabled, Converter={StaticResource BoolToVisibilityConverter}}" />
                <CheckBox IsChecked="{Binding SelectedRule.ExtendCurrentActivity}"
                          Content="{loc:Translate 'Extend current activity'}"
                          Margin="0,8,0,0" />
            </StackPanel>
        </Border>
```

- [ ] **Step 2d: Section 4 (Avatar Scope)**

```xml
        <!-- Avatar Scope -->
        <Border Margin="0,12,0,0"
                Padding="12"
                CornerRadius="14"
                Background="{DynamicResource NestedPanelBrush}"
                BorderBrush="{DynamicResource BorderBrush}"
                BorderThickness="1">
            <StackPanel>
                <TextBlock Text="Avatar Scope (Optional)"
                           FontWeight="Bold"
                           FontSize="13"
                           Margin="0,0,0,8" />
                <TextBlock Text="{loc:Translate 'Avatar Profile ID'}"
                           FontWeight="SemiBold" />
                <TextBox Text="{Binding SelectedRule.SupporterAvatarProfileId, UpdateSourceTrigger=PropertyChanged}"
                         Margin="0,6,0,0" />
                <TextBlock Text="{loc:Translate 'Avatar ID'}"
                           FontWeight="SemiBold"
                           Margin="0,8,0,0" />
                <TextBox Text="{Binding SelectedRule.SupporterAvatarId, UpdateSourceTrigger=PropertyChanged}"
                         Margin="0,6,0,0" />
                <TextBlock Text="{loc:Translate 'Avatar Name'}"
                           FontWeight="SemiBold"
                           Margin="0,8,0,0" />
                <TextBox Text="{Binding SelectedRule.SupporterAvatarName, UpdateSourceTrigger=PropertyChanged}"
                         Margin="0,6,0,0" />
            </StackPanel>
        </Border>
```

- [ ] **Step 2e: Section 5 (Bot Message)**

```xml
        <!-- Bot Message -->
        <Border Margin="0,12,0,0"
                Padding="12"
                CornerRadius="14"
                Background="{DynamicResource NestedPanelBrush}"
                BorderBrush="{DynamicResource BorderBrush}"
                BorderThickness="1">
            <StackPanel>
                <TextBlock Text="{loc:Translate 'Bot Message (Optional)'}"
                           FontWeight="Bold"
                           FontSize="13"
                           Margin="0,0,0,8" />
                <TextBox Text="{Binding SelectedRule.BotMessageTemplate, UpdateSourceTrigger=PropertyChanged}"
                         AcceptsReturn="True"
                         TextWrapping="Wrap"
                         MinHeight="60" />
                <TextBlock Text="{loc:Translate 'Use {user}, {rule}, {duration}, {cooldown} as template variables.'}"
                           Foreground="{DynamicResource TitleBarSubTextBrush}"
                           FontSize="10"
                           Margin="0,4,0,0" />
            </StackPanel>
        </Border>
```

- [ ] **Step 2f: Section 6 (Set Triggers)**

```xml
        <!-- Set Triggers (OSC Actions) -->
        <Border Margin="0,12,0,0"
                Padding="12"
                CornerRadius="14"
                Background="{DynamicResource NestedPanelBrush}"
                BorderBrush="{DynamicResource BorderBrush}"
                BorderThickness="1">
            <StackPanel>
                <DockPanel LastChildFill="True" Margin="0,0,0,8">
                    <TextBlock Text="{loc:Translate 'Set Triggers'}"
                               FontWeight="Bold"
                               FontSize="13"
                               VerticalAlignment="Center" />
                    <Button Content="{loc:Translate 'Add'}"
                            Style="{StaticResource AccentButtonStyle}"
                            DockPanel.Dock="Right"
                            Padding="8,2"
                            FontSize="11"
                            Command="{Binding AddSetTriggerCommand}" />
                </DockPanel>
                <ItemsControl ItemsSource="{Binding SelectedRule.SetTriggerActions}">
                    <ItemsControl.ItemTemplate>
                        <DataTemplate DataType="{x:Type models:SetTriggerAction}">
                            <Border Padding="8"
                                    CornerRadius="8"
                                    Background="{DynamicResource PanelSecondaryBrush}"
                                    BorderBrush="{DynamicResource BorderBrush}"
                                    BorderThickness="1"
                                    Margin="0,0,0,4">
                                <Grid>
                                    <Grid.ColumnDefinitions>
                                        <ColumnDefinition Width="*" />
                                        <ColumnDefinition Width="Auto" />
                                    </Grid.ColumnDefinitions>
                                    <StackPanel Grid.Column="0">
                                        <UniformGrid Columns="2">
                                            <StackPanel Margin="0,0,4,0">
                                                <TextBlock Text="{loc:Translate 'OSC Address'}"
                                                           FontSize="10" />
                                                <TextBox Text="{Binding ParameterName, UpdateSourceTrigger=PropertyChanged}"
                                                         Margin="0,2,0,0"
                                                         Padding="6,4"
                                                         FontSize="11" />
                                            </StackPanel>
                                            <StackPanel Margin="4,0,0,0">
                                                <TextBlock Text="{loc:Translate 'Type'}"
                                                           FontSize="10" />
                                                <ComboBox ItemsSource="{Binding Source={x:Static models:OscParameterType.Bool}}"
                                                          SelectedItem="{Binding ParameterType}"
                                                          Margin="0,2,0,0"
                                                          FontSize="11" />
                                            </StackPanel>
                                        </UniformGrid>
                                        <UniformGrid Columns="2" Margin="0,4,0,0">
                                            <StackPanel Margin="0,0,4,0">
                                                <TextBlock Text="{loc:Translate 'Value'}"
                                                           FontSize="10" />
                                                <TextBox Text="{Binding ParameterValue, UpdateSourceTrigger=PropertyChanged}"
                                                         Margin="0,2,0,0"
                                                         Padding="6,4"
                                                         FontSize="11" />
                                            </StackPanel>
                                        </UniformGrid>
                                    </StackPanel>
                                    <Button Grid.Column="1"
                                            Content="✕"
                                            Width="24"
                                            Height="24"
                                            Margin="8,0,0,0"
                                            VerticalAlignment="Top"
                                            Style="{StaticResource DangerButtonStyle}"
                                            Padding="0"
                                            FontSize="10"
                                            Command="{Binding RelativeSource={RelativeSource AncestorType=Window}, Path=DataContext.RemoveSetTriggerCommand}"
                                            CommandParameter="{Binding}" />
                                </Grid>
                            </Border>
                        </DataTemplate>
                    </ItemsControl.ItemTemplate>
                </ItemsControl>
            </StackPanel>
        </Border>
```

- [ ] **Step 2g: Footer buttons and closing tags**

```xml
        <!-- Footer Buttons -->
        <WrapPanel Margin="0,16,0,0" HorizontalAlignment="Right">
            <Button Content="{loc:Translate 'Delete'}"
                    Command="{Binding DeleteRuleCommand}"
                    Style="{StaticResource DangerButtonStyle}"
                    Margin="0,0,8,0" />
            <Button Content="{loc:Translate 'Test'}"
                    Command="{Binding TestRuleCommand}"
                    Style="{StaticResource SecondaryButtonStyle}"
                    Margin="0,0,8,0" />
            <Button Content="{loc:Translate 'Cancel'}"
                    Command="{Binding CloseEditorCommand}"
                    Style="{StaticResource SecondaryButtonStyle}"
                    Margin="0,0,8,0" />
            <Button Content="{loc:Translate 'Save'}"
                    Command="{Binding SaveEditorCommand}"
                    Style="{StaticResource AccentButtonStyle}" />
        </WrapPanel>
    </StackPanel>
</ScrollViewer>
```

- [ ] **Step 2h: Close the overlay Grid and Window**

```xml
                        </Grid>
                    </Border>
                </Grid>
            </Border>
        </Grid>
    </Border>
</Window>
```

(Note: Merge steps 2a-2h into a single replacement of the editor block. These steps are broken out for clarity but the edit is applied as one large XAML replacement.)

- [ ] **Step 3: Fix the Set Trigger ComboBox ItemsSource**

The `ItemsSource="{Binding Source={x:Static models:OscParameterType.Bool}}"` in step 2f won't work since `x:Static` on an enum member gives a single value, not a list. Change it to bind `OscParameterTypeValues` from the ViewModel:

```xml
<ComboBox ItemsSource="{Binding DataContext.OscParameterTypeValues, RelativeSource={RelativeSource AncestorType=Window}}"
          SelectedItem="{Binding ParameterType}"
          Margin="0,2,0,0"
          FontSize="11" />
```

- [ ] **Step 4: Add MovementDirectionGroup and MovementDirectionItem record classes**

Since the XAML references `Display` and `Value` properties on `MovementDirections` items, add the following to `MovementRedeemsManagerViewModel.cs` (at the end, before `Dispose`):

```csharp
public sealed record MovementDirectionItem(PlayerMovementDirection Value)
{
    public string Display => MovementRedeemCardViewModel.GetDisplayName(Value);
}
```

And in the constructor, populate `MovementDirections`:

Add this field:
```csharp
public ListCollectionView MovementDirections { get; }
```

Add this to the constructor after command initialization:
```csharp
var items = new List<MovementDirectionItem>();
foreach (PlayerMovementDirection dir in Enum.GetValues(typeof(PlayerMovementDirection)))
{
    items.Add(new MovementDirectionItem(dir));
}
MovementDirections = (ListCollectionView)CollectionViewSource.GetDefaultView(items);
```

- [ ] **Step 5: Commit**

```bash
git add "VrcTwitchOscBridge/MovementRedeemsManagerWindow.xaml" "VrcTwitchOscBridge/ViewModels/MovementRedeemsManagerViewModel.cs"
git commit -m "feat: rewrite Movement Redeem editor with full trigger and behavior configuration"
```

---

### Task 4: Update code-behind and fix SaveEditor wire-up

**Files:**
- Modify: `E:\!!!Program to work on\Proper Crystal Relay\VrcTwitchOscBridge\MovementRedeemsManagerWindow.xaml.cs`

- [ ] **Step 1: Remove the old OnSaveEditor event handler**

Remove the existing `OnSaveEditor` method (currently line 40):
```csharp
// DELETE: private void OnSaveEditor(object sender, RoutedEventArgs e) => Vm.IsEditorOpen = false;
```

- [ ] **Step 2: Commit**

```bash
git add "VrcTwitchOscBridge/MovementRedeemsManagerWindow.xaml.cs"
git commit -m "fix: remove old OnSaveEditor handler, now using SaveEditorCommand"
```

---

### Task 5: Build and verify

**Files:**

- [ ] **Step 1: Build the project**

Run:
```powershell
dotnet build "E:\!!!Program to work on\Proper Crystal Relay\VrcTwitchOscBridge\VrcTwitchOscBridge.csproj" --no-restore
```

Expected: Build succeeded with 0 warnings/errors.

- [ ] **Step 2: Fix any build errors**

If the build fails, fix errors iteratively (likely XAML binding path issues, missing properties, or namespace references) and rebuild.

- [ ] **Step 3: Run existing tests**

Run:
```powershell
dotnet test "E:\!!!Program to work on\Proper Crystal Relay\VrcTwitchOscBridge.Tests\VrcTwitchOscBridge.Tests.csproj" --no-restore
```

Expected: All tests pass.

- [ ] **Step 4: Commit**

```bash
git add -A
git commit -m "fix: address build errors and test failures"
```

---

### Task 6: Self-review checklist

- [ ] **Verify spec coverage:** All 6 sections from the design doc are implemented (General Settings, Trigger Configuration, Movement Behavior, Avatar Scope, Bot Message, Set Triggers)
- [ ] **Verify no placeholder code:** No TODOs, TBDs, or incomplete bindings in the final code
- [ ] **Verify type consistency:** `SelectedRule.TriggerType`, `SelectedRule.RewardSyncMode`, etc. match actual `TriggerRule` property names
