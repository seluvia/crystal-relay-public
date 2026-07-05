# Avatar Swap Single Name Field Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Merge the internal trigger name and Twitch reward name into a single "Name" field for Avatar Swap channel point rewards.

**Architecture:** Three independent changes: (1) XAML removal of the "Reward Name" textbox, (2) model-level sync from Name to ChannelPointRewardTitle for channel points, (3) ViewModel sets ChannelPointRewardTitle on rule creation.

**Tech Stack:** C# / WPF / XAML

---

### Task 1: Remove "Reward Name" textbox from `InlineRuleEditorControl.xaml`

**Files:**
- Modify: `E:\!!!Program to work on\Proper Crystal Relay\VrcTwitchOscBridge\UserControls\InlineRuleEditorControl.xaml:120-131`

- [ ] **Remove the Reward Name field and simplify the UniformGrid**

The current section (lines 120-131) has a `UniformGrid Columns="2"` with both "Reward Name" and "Reward Cost". Remove the Reward Name `StackPanel` and the `UniformGrid` wrapper, leaving just the "Reward Cost" StackPanel directly.

From:
```xml
<StackPanel Visibility="{Binding Rule.RewardSyncMode, Converter={StaticResource EnumToBoolConverter}, ConverterParameter=CreateOrManage, FallbackValue=Visible}">
    <UniformGrid Columns="2">
        <StackPanel Margin="0,0,8,0">
            <TextBlock Text="{loc:Translate 'Reward Name'}" Foreground="{DynamicResource MutedBrush}" FontSize="11" Margin="0,0,0,2" />
            <TextBox Text="{Binding Rule.ChannelPointRewardTitle, UpdateSourceTrigger=PropertyChanged}" />
        </StackPanel>
        <StackPanel Margin="8,0,0,0">
            <TextBlock Text="{loc:Translate 'Reward Cost'}" Foreground="{DynamicResource MutedBrush}" FontSize="11" Margin="0,0,0,2" />
            <TextBox Text="{Binding Rule.ChannelPointRewardCost, UpdateSourceTrigger=PropertyChanged}" />
        </StackPanel>
    </UniformGrid>
</StackPanel>
```

To:
```xml
<StackPanel Visibility="{Binding Rule.RewardSyncMode, Converter={StaticResource EnumToBoolConverter}, ConverterParameter=CreateOrManage, FallbackValue=Visible}">
    <StackPanel>
        <TextBlock Text="{loc:Translate 'Reward Cost'}" Foreground="{DynamicResource MutedBrush}" FontSize="11" Margin="0,0,0,2" />
        <TextBox Text="{Binding Rule.ChannelPointRewardCost, UpdateSourceTrigger=PropertyChanged}" />
    </StackPanel>
</StackPanel>
```

- [ ] **Build to verify**

Run: `dotnet build "E:\!!!Program to work on\Proper Crystal Relay\VrcTwitchOscBridge\VrcTwitchOscBridge.csproj" --no-restore`
Expected: Build succeeds

---

### Task 2: Sync Name to ChannelPointRewardTitle in `TriggerRule.cs`

**Files:**
- Modify: `E:\!!!Program to work on\Proper Crystal Relay\VrcTwitchOscBridge\Models\TriggerRule.cs:179-190`

- [ ] **Update the Name setter to sync ChannelPointRewardTitle for channel points**

From:
```csharp
public string Name
{
    get => name;
    set
    {
        if (SetProperty(ref name, value))
        {
            RaisePropertyChanged(nameof(DisplayTitle));
            RaisePropertyChanged(nameof(TriggerSummary));
        }
    }
}
```

To:
```csharp
public string Name
{
    get => name;
    set
    {
        if (SetProperty(ref name, value))
        {
            if (TriggerType == TwitchTriggerType.ChannelPoints)
            {
                if (!string.Equals(channelPointRewardTitle, value, StringComparison.Ordinal))
                {
                    channelPointRewardTitle = value;
                    OnPropertyChanged(nameof(ChannelPointRewardTitle));
                }
            }
            RaisePropertyChanged(nameof(DisplayTitle));
            RaisePropertyChanged(nameof(TriggerSummary));
        }
    }
}
```

- [ ] **Build to verify**

Run: `dotnet build "E:\!!!Program to work on\Proper Crystal Relay\VrcTwitchOscBridge\VrcTwitchOscBridge.csproj" --no-restore`
Expected: Build succeeds

---

### Task 3: Set ChannelPointRewardTitle on create in `AvatarSwapManagerViewModel.cs`

**Files:**
- Modify: `E:\!!!Program to work on\Proper Crystal Relay\VrcTwitchOscBridge\ViewModels\AvatarSwapManagerViewModel.cs:524-541`

- [ ] **Add ChannelPointRewardTitle to the new rule initialization**

From:
```csharp
private void AddChannelPointRule()
{
    if (SelectedSwapCard is null) return;
    var rule = new TriggerRule
    {
        TriggerType = TwitchTriggerType.ChannelPoints,
        ActionType = OscActionType.AvatarChange,
        AvatarChangeTargetId = SelectedSwapCard.Profile.TargetAvatarId,
        AvatarTargetName = SelectedSwapCard.Profile.TargetAvatarName,
        ChannelPointRewardCost = 100,
        Name = "New Channel Point Swap"
    };
```

To:
```csharp
private void AddChannelPointRule()
{
    if (SelectedSwapCard is null) return;
    var rule = new TriggerRule
    {
        TriggerType = TwitchTriggerType.ChannelPoints,
        ActionType = OscActionType.AvatarChange,
        AvatarChangeTargetId = SelectedSwapCard.Profile.TargetAvatarId,
        AvatarTargetName = SelectedSwapCard.Profile.TargetAvatarName,
        ChannelPointRewardCost = 100,
        Name = "New Channel Point Swap",
        ChannelPointRewardTitle = "New Channel Point Swap"
    };
```

Note: `Name` setter will auto-sync `ChannelPointRewardTitle` (from Task 2), but setting it explicitly here ensures the initial value is set even if `TriggerType` hasn't propagated through the property setter chain yet at construction time.

- [ ] **Build to verify**

Run: `dotnet build "E:\!!!Program to work on\Proper Crystal Relay\VrcTwitchOscBridge\VrcTwitchOscBridge.csproj" --no-restore`
Expected: Build succeeds

---

### Self-Review

**1. Spec coverage:**
- Remove "Reward Name" textbox from Avatar Swap channel point editor → Task 1
- Sync Name → ChannelPointRewardTitle for channel point rules → Task 2
- Set ChannelPointRewardTitle on new rule creation → Task 3
- All spec requirements covered ✓

**2. Placeholder scan:** No TBDs, TODOs, or placeholder patterns found ✓

**3. Type consistency:** `ChannelPointRewardTitle` matches across all three files. `Name` and `channelPointRewardTitle` backing fields match the existing model. `TwitchTriggerType.ChannelPoints` is the correct enum value ✓
