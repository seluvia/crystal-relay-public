# Movement Redeems List Improvements — Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Replace the editable rule name with auto-generated display names from trigger/reward info and add alternating row backgrounds to the DataGrid for readability.

**Architecture:** Two independent UI changes to `MovementRedeemsManagerWindow.xaml` and `MovementRedeemCardViewModel.cs`. The `TriggerRule.Name` field stays on the model (shared base class) but is no longer user-editable in the movement editor — display names are computed from trigger type + reward title.

**Tech Stack:** C#, WPF/XAML, MVVM

## Global Constraints

- `TriggerRule.Name` must remain writable on the model (shared across rule types) but removed from the movement editor panel
- All existing trigger-type booleans (`UsesChannelPointReward`, `UsesChatCommand`, etc.) are already exposed
- The `DisplayTitle` property on `TriggerRule` already has the correct logic for ChannelPoints and fallback — reuse it
- DataGrid must keep existing hover/selected highlight behavior

---

### Task 1: Add DisplayName property to MovementRedeemCardViewModel

**Files:**
- Modify: `VrcTwitchOscBridge\ViewModels\MovementRedeemCardViewModel.cs`
- Reference model: `VrcTwitchOscBridge\Models\TriggerRule.cs` (lines 1676-1700 for `DisplayTitle`)

**Interfaces:**
- Consumes: `TriggerRule.DisplayTitle` (existing property), `rule.TriggerType`, `rule.ChannelPointRewardTitle`, `rule.ChatCommandText`, `rule.MovementDirection`
- Produces: `MovementRedeemCardViewModel.DisplayName` — a computed string property for DataGrid binding

- [ ] **Step 1: Add DisplayName property**

Add a computed `DisplayName` property to `MovementRedeemCardViewModel` that mirrors `TriggerRule.DisplayTitle` logic but tailored for movement rules:

```csharp
public string DisplayName
{
    get
    {
        if (rule.TriggerType == TwitchTriggerType.ChannelPoints)
            return string.IsNullOrWhiteSpace(rule.ChannelPointRewardTitle)
                ? rule.HasConfiguredChatCommand
                    ? rule.ChatCommandText.Trim()
                    : "New Movement Rule"
                : rule.ChannelPointRewardTitle.Trim();

        if (rule.HasConfiguredChatCommand)
            return rule.ChatCommandText.Trim();

        var dirName = GetDisplayName(rule.MovementDirection);
        return rule.TriggerType switch
        {
            TwitchTriggerType.Bits => $"Bits {dirName}",
            TwitchTriggerType.Subscriptions => rule.IsGiftSubscription ? $"Gift Subs {dirName}" : $"Subs {dirName}",
            TwitchTriggerType.Follow => $"Follow {dirName}",
            _ => dirName
        };
    }
}
```

Note: `GetDisplayName` is already a static method on this class (line 163).

- [ ] **Step 2: Update UpdateFromRule to raise DisplayName**

Add `RaisePropertyChanged(nameof(DisplayName));` to the `UpdateFromRule()` method so the grid refreshes when the rule changes.

- [ ] **Step 3: Verify build**

Run: `dotnet build "E:\!!!Program to work on\Proper Crystal Relay\VrcTwitchOscBridge\VrcTwitchOscBridge.csproj" --no-restore`
Expected: Build succeeds

---

### Task 2: Update DataGrid Name column and remove Rule Name textbox

**Files:**
- Modify: `VrcTwitchOscBridge\MovementRedeemsManagerWindow.xaml`

**Interfaces:**
- Consumes: `MovementRedeemCardViewModel.DisplayName` from Task 1
- Removes: XAML for the Rule Name textbox in the editor panel (lines 908-914)

- [ ] **Step 1: Bind DataGrid Name column to DisplayName**

Change line 695 from:
```xml
Binding="{Binding Name}"
```
to:
```xml
Binding="{Binding DisplayName}"
```

- [ ] **Step 2: Remove Rule Name textbox from editor panel**

Remove lines 908-914 (the UniformGrid left cell with "Rule Name" label and textbox). Keep the Enabled toggle on the right side.

Change the UniformGrid from `Columns="2"` to a single-column layout with only the Enabled toggle:

Before (lines 908-959):
```xml
<UniformGrid Columns="2" Margin="0,0,0,8">
    <StackPanel Margin="0,0,6,0">
        <TextBlock Text="{loc:Translate 'Rule Name'}" FontWeight="SemiBold" />
        <TextBox Text="{Binding SelectedRule.Name, UpdateSourceTrigger=PropertyChanged}" Margin="0,6,0,0" />
    </StackPanel>
    <StackPanel Margin="6,0,0,0">
        <TextBlock Text="{loc:Translate 'Enabled'}" FontWeight="SemiBold" />
        <ToggleButton ... />
    </StackPanel>
</UniformGrid>
```

After:
```xml
<StackPanel Margin="0,0,0,8">
    <TextBlock Text="{loc:Translate 'Enabled'}" FontWeight="SemiBold" />
    <ToggleButton IsChecked="{Binding SelectedRule.IsEnabled, Mode=TwoWay}"
                  Cursor="Hand"
                  Width="36" Height="20"
                  Margin="0,6,0,0"
                  HorizontalAlignment="Left">
        <ToggleButton.Style>
            <Style TargetType="ToggleButton">
                ...
            </Style>
        </ToggleButton.Style>
    </ToggleButton>
</StackPanel>
```

- [ ] **Step 3: Verify build**

Run: `dotnet build "E:\!!!Program to work on\Proper Crystal Relay\VrcTwitchOscBridge\VrcTwitchOscBridge.csproj" --no-restore`
Expected: Build succeeds

---

### Task 3: Add alternating row backgrounds to DataGrid

**Files:**
- Modify: `VrcTwitchOscBridge\MovementRedeemsManagerWindow.xaml`

- [ ] **Step 1: Add AlternatingRowBackground to DataGrid**

On the DataGrid element (line 619), add two attributes:
```xml
AlternatingRowBackground="#22181730"
AlternationCount="2"
```

This gives a subtle tint on every other row that matches the dark purple theme. The hex `#22181730` is a semi-transparent dark tone that's lighter than `Transparent` but still subtle enough not to distract from hover/selection states.

- [ ] **Step 2: Verify build**

Run: `dotnet build "E:\!!!Program to work on\Proper Crystal Relay\VrcTwitchOscBridge\VrcTwitchOscBridge.csproj" --no-restore`
Expected: Build succeeds
