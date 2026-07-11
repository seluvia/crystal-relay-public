# Avatar Scaling Cooldown UI — Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Add a "Cooldown Seconds" field to the avatar scaling rule editor in the Avatar Scaling Manager window, visible for ChannelPointReward + CreateOrManage rules only, with a default of 30s for new rules and a timing summary helper text.

**Architecture:** Three small changes across the model layer (computed `TimingSummary` property + default value), the view (XAML field + timing text), and the default-rule factory. No changes to the persistence or runtime enforcement layers — those already handle `CooldownSeconds` correctly.

**Tech Stack:** C#, WPF/XAML, .NET 10

## Global Constraints

- Cooldown only applies to `ChannelPointReward` + `CreateOrManage` trigger types. All other trigger types keep zero cooldown (enforced in `BridgeRuntimeConfiguration.cs` — not changed here).
- Follow existing patterns in `AvatarScalingManagerWindow.xaml` for visibility gating (see managed reward color section).
- Follow existing localization key pattern — "Cooldown Seconds" key already exists in other editors.
- Build command: `dotnet build "E:\!!!Program to work on\Proper Crystal Relay\VrcTwitchOscBridge\VrcTwitchOscBridge.csproj" --no-restore`

---

### Task 1: Add TimingSummary computed property to AvatarScaleRule

**Files:**
- Modify: `E:\!!!Program to work on\Proper Crystal Relay\VrcTwitchOscBridge\Models\AvatarScaleRule.cs:630-647` (ActiveTimeSeconds setter)
- Modify: `E:\!!!Program to work on\Proper Crystal Relay\VrcTwitchOscBridge\Models\AvatarScaleRule.cs:527-531` (CooldownSeconds setter)

**Interfaces:**
- Consumes: `ActiveTimeSeconds` (double), `CooldownSeconds` (int) — both already exist
- Produces: `TimingSummary` (string) — computed property on `AvatarScaleRule`

- [ ] **Step 1: Add `TimingSummary` property**

In `AvatarScaleRule.cs`, add a computed property near the other computed properties (~line 1008 area, after `HasActiveTime`):

```csharp
public string TimingSummary
{
    get
    {
        var activeSeconds = (int)Math.Ceiling(ActiveTimeSeconds);
        if (CooldownSeconds <= 0)
        {
            return CooldownSeconds <= 0 && activeSeconds <= 0
                ? string.Empty
                : $"Active: {activeSeconds}s";
        }

        var total = activeSeconds + CooldownSeconds;
        return CooldownSeconds > 0
            ? $"Active: {activeSeconds}s \u2192 Cooldown: {CooldownSeconds}s \u2192 Ready: {total}s"
            : $"Active: {activeSeconds}s";
    }
}
```

Use `\u2192` (Unicode RIGHT ARROW) for the arrow character.

- [ ] **Step 2: Raise TimingSummary in ActiveTimeSeconds setter**

In the `ActiveTimeSeconds` setter (line 637-647), add after the existing `RaisePropertyChanged(nameof(HasActiveTime))`:

```csharp
RaisePropertyChanged(nameof(TimingSummary));
```

- [ ] **Step 3: Raise TimingSummary in CooldownSeconds setter**

In the `CooldownSeconds` setter (line 527-531), change from:

```csharp
public int CooldownSeconds
{
    get => cooldownSeconds;
    set => SetProperty(ref cooldownSeconds, Math.Max(0, value));
}
```

to:

```csharp
public int CooldownSeconds
{
    get => cooldownSeconds;
    set
    {
        if (SetProperty(ref cooldownSeconds, Math.Max(0, value)))
        {
            RaisePropertyChanged(nameof(TimingSummary));
        }
    }
}
```

- [ ] **Step 4: Build to verify**

```powershell
dotnet build "E:\!!!Program to work on\Proper Crystal Relay\VrcTwitchOscBridge\VrcTwitchOscBridge.csproj" --no-restore
```

Expected: Build succeeds with no errors.

---

### Task 2: Set default cooldown to 30s for new rules

**Files:**
- Modify: `E:\!!!Program to work on\Proper Crystal Relay\VrcTwitchOscBridge\ViewModels\MainWindowViewModel.cs:19709-19737`

**Interfaces:**
- Consumes: `AvatarScaleRule` constructor (parameterless)
- Produces: `CreateDefaultAvatarScaleRule()` returns `AvatarScaleRule` with `CooldownSeconds = 30`

- [ ] **Step 1: Add CooldownSeconds = 30 to CreateDefaultAvatarScaleRule**

At `MainWindowViewModel.cs` line ~19727 (in the object initializer, after `ActiveTimeSeconds = 0`), add:

```csharp
CooldownSeconds = 30,
```

- [ ] **Step 2: Build to verify**

```powershell
dotnet build "E:\!!!Program to work on\Proper Crystal Relay\VrcTwitchOscBridge\VrcTwitchOscBridge.csproj" --no-restore
```

Expected: Build succeeds.

---

### Task 3: Add CooldownSeconds field to the scaling rule editor XAML

**Files:**
- Modify: `E:\!!!Program to work on\Proper Crystal Relay\VrcTwitchOscBridge\AvatarScalingManagerWindow.xaml:1544-1572` (Timer & Return section)

**Interfaces:**
- Consumes: `CooldownSeconds` (int), `TimingSummary` (string), `UsesCreateOrManageReward` (bool) — all on `AvatarScaleRule`

- [ ] **Step 1: Add CooldownSeconds row to Timer & Return section**

In `AvatarScalingManagerWindow.xaml`, replace the existing "Timer & Return" section (lines 1544-1572) with:

```xml
<Border Margin="0,12,0,0"
        Padding="12"
        CornerRadius="14"
        Background="{DynamicResource NestedPanelBrush}"
        BorderBrush="{DynamicResource BorderBrush}"
        BorderThickness="1">
    <StackPanel>
        <TextBlock Text="{loc:Translate 'Timer &amp; Return'}" FontWeight="Bold" FontSize="15" />
        <UniformGrid Columns="2" Margin="0,10,0,0">
            <StackPanel Margin="0,0,6,0">
                <TextBlock Text="{loc:Translate 'Active Time Seconds'}" FontWeight="SemiBold" />
                <TextBox Text="{Binding ActiveTimeSeconds, UpdateSourceTrigger=LostFocus}" />
            </StackPanel>
            <StackPanel Margin="6,0,0,0"
                        Visibility="{Binding UsesCreateOrManageReward, Converter={StaticResource BoolToVisibilityConverter}}">
                <TextBlock Text="{loc:Translate 'Cooldown Seconds'}" FontWeight="SemiBold" />
                <TextBox Text="{Binding CooldownSeconds, UpdateSourceTrigger=PropertyChanged}" />
            </StackPanel>
        </UniformGrid>
        <UniformGrid Columns="2" Margin="0,10,0,0">
            <StackPanel Margin="0,0,6,0">
                <TextBlock Text="{loc:Translate 'Return Height'}" FontWeight="SemiBold" />
                <TextBox Text="{Binding RestoreHeightMeters, UpdateSourceTrigger=LostFocus}" />
            </StackPanel>
            <StackPanel Margin="6,0,0,0">
                <TextBlock Text="{loc:Translate 'Return Mode'}" FontWeight="SemiBold" />
                <ComboBox ItemsSource="{Binding DataContext.AvatarScaleRestoreModes, RelativeSource={RelativeSource AncestorType=Window}}"
                          SelectedItem="{Binding RestoreMode, UpdateSourceTrigger=PropertyChanged}" />
            </StackPanel>
        </UniformGrid>
        <UniformGrid Columns="2" Margin="0,10,0,0">
            <StackPanel Margin="0,0,6,0">
                <TextBlock Text="{loc:Translate 'Smooth Transition Seconds'}" FontWeight="SemiBold" />
                <TextBox Text="{Binding SmoothTransitionSeconds, UpdateSourceTrigger=LostFocus}" />
            </StackPanel>
            <StackPanel Margin="6,0,0,0"
                        VerticalAlignment="Bottom"
                        Visibility="{Binding UsesCreateOrManageReward, Converter={StaticResource BoolToVisibilityConverter}}">
                <TextBlock Text="{Binding TimingSummary}"
                           Foreground="{DynamicResource TitleBarSubTextBrush}"
                           FontSize="11"
                           TextWrapping="Wrap" />
            </StackPanel>
        </UniformGrid>

        <StackPanel Margin="0,12,0,0" Visibility="{Binding UsesSupporterGrowth, Converter={StaticResource BoolToVisibilityConverter}}">
            ...existing supporter growth content...
        </StackPanel>
    </StackPanel>
</Border>
```

Note: The supporter growth section and Extend Current Activity section after "Smooth Transition Seconds" remain unchanged.

- [ ] **Step 2: Build to verify**

```powershell
dotnet build "E:\!!!Program to work on\Proper Crystal Relay\VrcTwitchOscBridge\VrcTwitchOscBridge.csproj" --no-restore
```

Expected: Build succeeds.

- [ ] **Step 3: Verify the localization key exists**

Check that "Cooldown Seconds" key already has an `en-US` entry (it exists from other editors like Cash Payment, Movement, Master Reward). If not, add it.

```powershell
rg -F '"Cooldown Seconds"' "E:\!!!Program to work on\Proper Crystal Relay\VrcTwitchOscBridge\Localization"
```

Expected: At least one match in `en-US.json`.
