# Avatar Swap Power Up Section — Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Add a dedicated Power Up trigger section to the Avatar Swap editor that links to existing Twitch Custom Power-ups and fires avatar changes on redemption.

**Architecture:** Mirror the existing Bits section pattern. Power Up rules live in `AvatarSwapProfile.PowerUpRules`, snapshot as global overrides (paid priority), match by `PowerUpId` in `HandlePowerUpEventAsync()`, and execute avatar changes via `FindAvatarSwapProfileForRule()`.

**Tech Stack:** C#, WPF/XAML, .NET 10

## Global Constraints

- Types: `TriggerRule` for Power Up rules (same as Bits/Subs), `PersistedTriggerRule` for serialization
- Priority in avatar swap: Cash > Power Up > Bits/Subs > Channel Points
- Power Up matching uses `PowerUpId` field on `TriggerRule` (already exists) and `TriggerRuleSnapshot`
- Inline row control mirrors `InlineBitsRuleRowControl` exactly (same layout, different prefix)
- Remove the old "⚡ Power-up" button from Advanced Triggers in the swap editor (replaced by dedicated section)
- Roulette's "Power Up" button stays unchanged

---

### Task 1: Model — Add PowerUpRules to AvatarSwapProfile

**Files:**
- Modify: `VrcTwitchOscBridge\Models\AvatarSwapProfile.cs`

- [ ] **Step 1: Add PowerUpRules collection and helper properties**

In `AvatarSwapProfile.cs`, add:
```csharp
public ObservableCollection<TriggerRule> PowerUpRules { get; } = new();
```

Update `HasRules`:
```csharp
public bool HasRules =>
    ChannelPointRules.Count + BitsRules.Count + SubsRules.Count + PaymentRules.Count + PowerUpRules.Count > 0;
```

Add computed properties:
```csharp
public bool UsesPowerUpRules => PowerUpRules.Count > 0;
```

Update `AvatarSubtitle`:
```csharp
public string AvatarSubtitle =>
    $"{ChannelPointRules.Count} cp · {BitsRules.Count} bits · {SubsRules.Count} subs · {PaymentRules.Count} pay · {PowerUpRules.Count} pow";
```

Update `Bump()` to add:
```csharp
PowerUpRules.CollectionChanged += (_, _) => Bump();
```

And in the bump handler body:
```csharp
RaisePropertyChanged(nameof(UsesPowerUpRules));
```

- [ ] **Step 2: Build and verify**

```powershell
dotnet build "E:\!!!Program to work on\Proper Crystal Relay\VrcTwitchOscBridge\VrcTwitchOscBridge.csproj" --no-restore
```
Expected: Build succeeds.

- [ ] **Step 3: Commit**

```bash
git add "VrcTwitchOscBridge/Models/AvatarSwapProfile.cs"
git commit -m "feat(swap): add PowerUpRules collection to AvatarSwapProfile"
```

---

### Task 2: Serialization — Persist AvatarSwapProfile PowerUpRules

**Files:**
- Modify: `VrcTwitchOscBridge\Services\SettingsStore.cs`

- [ ] **Step 1: Find `PersistedAvatarSwapProfile` class**

Read `SettingsStore.cs` around line ~3261-3301. Add to the persisted class:
```csharp
public List<PersistedTriggerRule>? PowerUpRules { get; set; }
```

- [ ] **Step 2: Find `ToPersistedAvatarSwapProfile()`** (around line ~1181-1199)

Add serialization:
```csharp
PowerUpRules = profile.PowerUpRules
    .Select(rule => ToPersistedRule(rule))
    .ToList(),
```

- [ ] **Step 3: Find `ToAvatarSwapProfile()`** (around line ~1202-1238)

Add deserialization. After the PaymentRules block:
```csharp
if (persisted.PowerUpRules is { Count: > 0 })
{
    foreach (var persistedRule in persisted.PowerUpRules)
    {
        var rule = ToRule(persistedRule);
        profile.PowerUpRules.Add(rule);
    }
}
```

- [ ] **Step 4: Build and verify**

```powershell
dotnet build "E:\!!!Program to work on\Proper Crystal Relay\VrcTwitchOscBridge\VrcTwitchOscBridge.csproj" --no-restore
```
Expected: Build succeeds.

- [ ] **Step 5: Commit**

```bash
git add "VrcTwitchOscBridge/Services/SettingsStore.cs"
git commit -m "feat(swap): persist PowerUpRules in AvatarSwapProfile"
```

---

### Task 3: TriggerRuleSnapshot — Add PowerUpId

**Files:**
- Modify: `VrcTwitchOscBridge\Services\BridgeRuntimeConfiguration.cs`

- [ ] **Step 1: Add PowerUpId to TriggerRuleSnapshot record** (around line 58-148)

Add to the record parameter list (after `ChannelPointRewardTitle`):
```csharp
string PowerUpId,
```

- [ ] **Step 2: Add PowerUpId to `FromRule()`** (around line 150-)

Find the `FromRule(TriggerRule rule)` static method. Add to the constructor call:
```csharp
PowerUpId: rule.PowerUpId ?? string.Empty,
```

- [ ] **Step 3: Add PowerUpRules to `AvatarSwapProfileSnapshot`** (around line 404-416)

Add to the record:
```csharp
IReadOnlyList<TriggerRuleSnapshot> PowerUpRules,
```

- [ ] **Step 4: Build and verify**

```powershell
dotnet build "E:\!!!Program to work on\Proper Crystal Relay\VrcTwitchOscBridge\VrcTwitchOscBridge.csproj" --no-restore
```
Expected: Build fails or shows errors where `AvatarSwapProfileSnapshot` is constructed without the new field.

- [ ] **Step 5: Fix all AvatarSwapProfileSnapshot construction sites**

In `BridgeRuntimeConfiguration.cs`, find every `new AvatarSwapProfileSnapshot(...)` call (likely just one in `FromSettings()`, around line 638-650). Add the new field. The new PowerUpRules should be an empty array initially:
```csharp
powerUpSnapshots.ToArray(),
```

Where `powerUpSnapshots` is a new list (see Task 4).

- [ ] **Step 6: Build and verify**

```powershell
dotnet build "E:\!!!Program to work on\Proper Crystal Relay\VrcTwitchOscBridge\VrcTwitchOscBridge.csproj" --no-restore
```
Expected: Build succeeds.

- [ ] **Step 7: Commit**

```bash
git add "VrcTwitchOscBridge/Services/BridgeRuntimeConfiguration.cs"
git commit -m "feat(swap): add PowerUpId to TriggerRuleSnapshot and PowerUpRules to AvatarSwapProfileSnapshot"
```

---

### Task 4: Snapshot conversion — Power Up rules in FromSettings()

**Files:**
- Modify: `VrcTwitchOscBridge\Services\BridgeRuntimeConfiguration.cs`

- [ ] **Step 1: Add PowerUpRules snapshot loop** in `FromSettings()`

In the avatar swap profile loop (around line 590-650), after the `SubsRules` snapshot block (after line ~618) and before the `PaymentRules` block (line ~620), add:

```csharp
var powerUpSnapshots = new List<TriggerRuleSnapshot>();
foreach (var rule in swapProfile.PowerUpRules)
{
    if (TryToSnapshot(rule, isGlobalOverride: true, profile: null, linkedRewardCooldownSecondsById, out var snapshot))
    {
        powerUpSnapshots.Add(snapshot);
        rules.Add(snapshot);
    }
}
```

This mirrors the Bits/Subs pattern: `isGlobalOverride: true` and `profile: null`.

- [ ] **Step 2: Pass powerUpSnapshots to AvatarSwapProfileSnapshot**

Update the `AvatarSwapProfileSnapshot` construction to pass `powerUpSnapshots.ToArray()`.

- [ ] **Step 3: Build and verify**

```powershell
dotnet build "E:\!!!Program to work on\Proper Crystal Relay\VrcTwitchOscBridge\VrcTwitchOscBridge.csproj" --no-restore
```
Expected: Build succeeds.

- [ ] **Step 4: Commit**

```bash
git add "VrcTwitchOscBridge/Services/BridgeRuntimeConfiguration.cs"
git commit -m "feat(swap): snapshot avatar swap PowerUpRules as global overrides"
```

---

### Task 5: Runtime matching — Handle Power Up events for avatar swap rules

**Files:**
- Modify: `VrcTwitchOscBridge\Services\BridgeCoordinator.cs`

- [ ] **Step 1: Find `HandlePowerUpEventAsync()`** (around line 3658-3720)

After the loop over `matchingRules` from top-level `PowerUpRuleSnapshot` objects, add a second matching pass over avatar swap profile PowerUp rules. After line ~3719 (end of the existing loop), add:

```csharp
// Also match avatar swap profile PowerUp rules (paid override, like Bits/Subs).
var swapPowerUpRules = configuration.AvatarSwapProfiles
    .SelectMany(profile => profile.PowerUpRules.Select(rule => (Profile: profile, Rule: rule)))
    .Where(t => t.Rule.IsEnabled
        && !temporarilyDisabledRuleIds.Contains(t.Rule.Id)
        && PowerUpSnapshotIdentityMatches(t.Rule, bridgeEvent))
    .ToArray();

foreach (var (profile, rule) in swapPowerUpRules)
{
    if (AreRedeemsPaused())
    {
        LogRedeemsPaused();
        return;
    }

    await ExecuteRuleAsync(rule, bridgeEvent, cancellationToken);
}
```

- [ ] **Step 2: Add `PowerUpSnapshotIdentityMatches()` helper**

Add a new private static method:
```csharp
private static bool PowerUpSnapshotIdentityMatches(TriggerRuleSnapshot rule, BridgeIncomingEvent bridgeEvent)
{
    var configuredId = rule.PowerUpId.Trim();
    var incomingId = bridgeEvent.RewardId?.Trim() ?? string.Empty;
    if (!string.IsNullOrWhiteSpace(configuredId))
    {
        return string.Equals(configuredId, incomingId, StringComparison.Ordinal);
    }

    return false;
}
```

This matches only by `PowerUpId` (no title fallback needed — avatar swap rules always have a linked ID).

- [ ] **Step 3: Build and verify**

```powershell
dotnet build "E:\!!!Program to work on\Proper Crystal Relay\VrcTwitchOscBridge\VrcTwitchOscBridge.csproj" --no-restore
```
Expected: Build succeeds.

- [ ] **Step 4: Commit**

```bash
git add "VrcTwitchOscBridge/Services/BridgeCoordinator.cs"
git commit -m "feat(swap): match avatar swap PowerUp rules at runtime"
```

---

### Task 6: New inline row control — InlinePowerUpRuleRowViewModel + XAML

**Files:**
- Create: `VrcTwitchOscBridge\UserControls\InlinePowerUpRuleRowViewModel.cs`
- Create: `VrcTwitchOscBridge\UserControls\InlinePowerUpRuleRowControl.xaml`
- Create: `VrcTwitchOscBridge\UserControls\InlinePowerUpRuleRowControl.xaml.cs`

- [ ] **Step 1: Create InlinePowerUpRuleRowViewModel.cs**

```csharp
using System;
using System.ComponentModel;
using System.Text;
using System.Windows.Input;
using VrcTwitchOscBridge.Infrastructure;
using VrcTwitchOscBridge.Models;

namespace VrcTwitchOscBridge.UserControls;

public sealed class InlinePowerUpRuleRowViewModel : ObservableObject, IRuleRowViewModel
{
    private readonly TriggerRule _rule;
    private string _summary = string.Empty;
    private ICommand? _editCommand;
    private ICommand? _deleteCommand;

    public InlinePowerUpRuleRowViewModel(TriggerRule rule, AvatarSwapProfile? profile = null)
    {
        _rule = rule ?? throw new ArgumentNullException(nameof(rule));
        Profile = profile;
        _rule.PropertyChanged += OnRulePropertyChanged;
        RefreshSummary();
    }

    public object Rule => _rule;
    public AvatarSwapProfile? Profile { get; }
    public string Summary { get => _summary; private set => SetProperty(ref _summary, value); }
    public bool IsEnabled => _rule.IsEnabled;

    public ICommand EditCommand
    {
        get => _editCommand ??= new RelayCommand(_ => { });
        set => _editCommand = value;
    }

    public ICommand DeleteCommand
    {
        get => _deleteCommand ??= new RelayCommand(_ => { });
        set => _deleteCommand = value;
    }

    public void RefreshSummary()
    {
        var name = string.IsNullOrWhiteSpace(_rule.Name) ? "Untitled" : _rule.Name;
        var sb = new StringBuilder();
        sb.Append("⚡ ").Append(name);
        if (!string.IsNullOrWhiteSpace(_rule.PowerUpId))
        {
            var title = string.IsNullOrWhiteSpace(_rule.ChannelPointRewardTitle)
                ? "linked"
                : _rule.ChannelPointRewardTitle;
            sb.Append(" — ").Append(title);
        }
        Summary = sb.ToString();
    }

    private void OnRulePropertyChanged(object? sender, PropertyChangedEventArgs e) => RefreshSummary();
}
```

- [ ] **Step 2: Create InlinePowerUpRuleRowControl.xaml**

```xml
<UserControl x:Class="VrcTwitchOscBridge.UserControls.InlinePowerUpRuleRowControl"
             xmlns="http://schemas.microsoft.com/winfx/2006/xaml/presentation"
             xmlns:x="http://schemas.microsoft.com/winfx/2006/xaml"
             xmlns:loc="clr-namespace:VrcTwitchOscBridge.Services">
    <UserControl.Resources>
        <Style x:Key="RowIconButtonStyle" TargetType="Button">
            <Setter Property="Width" Value="26" />
            <Setter Property="Height" Value="26" />
            <Setter Property="Background" Value="Transparent" />
            <Setter Property="BorderThickness" Value="0" />
            <Setter Property="Foreground" Value="{DynamicResource MutedBrush}" />
            <Setter Property="Cursor" Value="Hand" />
            <Setter Property="FontSize" Value="13" />
            <Setter Property="Template">
                <Setter.Value>
                    <ControlTemplate TargetType="Button">
                        <Border x:Name="Chrome" Background="{TemplateBinding Background}" CornerRadius="4">
                            <ContentPresenter HorizontalAlignment="Center" VerticalAlignment="Center" />
                        </Border>
                        <ControlTemplate.Triggers>
                            <Trigger Property="IsMouseOver" Value="True">
                                <Setter TargetName="Chrome" Property="Background" Value="{DynamicResource SecondaryButtonBrush}" />
                            </Trigger>
                        </ControlTemplate.Triggers>
                    </ControlTemplate>
                </Setter.Value>
            </Setter>
        </Style>
    </UserControl.Resources>
    <Border Background="{DynamicResource PanelBrush}" BorderBrush="{DynamicResource InputBorderBrush}" BorderThickness="1" Padding="8,6" CornerRadius="4" Margin="0,0,0,4">
        <DockPanel>
            <StackPanel DockPanel.Dock="Right" Orientation="Horizontal" VerticalAlignment="Center">
                <Button Content="⚙" Style="{StaticResource RowIconButtonStyle}" Margin="0,0,2,0"
                        Foreground="{DynamicResource TextBrush}"
                        Command="{Binding EditCommand}" ToolTip="{loc:Translate 'Edit'}" />
                <Button Content="🗑" Style="{StaticResource RowIconButtonStyle}"
                        Command="{Binding DeleteCommand}" ToolTip="{loc:Translate 'Delete'}" />
            </StackPanel>
            <TextBlock Text="{Binding Summary}" VerticalAlignment="Center" TextTrimming="CharacterEllipsis"
                       Foreground="{DynamicResource TextBrush}" ToolTip="{Binding Summary}" />
        </DockPanel>
    </Border>
</UserControl>
```

- [ ] **Step 3: Create InlinePowerUpRuleRowControl.xaml.cs**

```csharp
using System.Windows.Controls;

namespace VrcTwitchOscBridge.UserControls;

public partial class InlinePowerUpRuleRowControl : UserControl
{
    public InlinePowerUpRuleRowControl() => InitializeComponent();
}
```

- [ ] **Step 4: Build and verify** (may fail until csproj entries are added in Task 9)

```powershell
dotnet build "E:\!!!Program to work on\Proper Crystal Relay\VrcTwitchOscBridge\VrcTwitchOscBridge.csproj" --no-restore
```
Expected: May fail if csproj not yet updated. This is OK — Task 9 will fix it.

- [ ] **Step 5: Commit**

```bash
git add "VrcTwitchOscBridge/UserControls/InlinePowerUpRuleRowViewModel.cs"
git add "VrcTwitchOscBridge/UserControls/InlinePowerUpRuleRowControl.xaml"
git add "VrcTwitchOscBridge/UserControls/InlinePowerUpRuleRowControl.xaml.cs"
git commit -m "feat(swap): add InlinePowerUpRuleRowViewModel and control"
```

---

### Task 7: ViewModel — PowerUpRows in AvatarSwapManagerViewModel

**Files:**
- Modify: `VrcTwitchOscBridge\ViewModels\AvatarSwapManagerViewModel.cs`

- [ ] **Step 1: Add PowerUpRows collection and AddPowerUpRuleCommand**

After line ~137 (`PaymentRows`), add:
```csharp
public ObservableCollection<InlinePowerUpRuleRowViewModel> PowerUpRows { get; } = new();
```

After line ~53 (`AddPaymentRuleCommand`), add:
```csharp
AddPowerUpRuleCommand = new RelayCommand(AddPowerUpRule, () => SelectedSwapCard is not null);
```

In the command declarations section (around line 305), add:
```csharp
public RelayCommand AddPowerUpRuleCommand { get; }
```

- [ ] **Step 2: Add AddPowerUpRule() method** (after `AddPaymentRule()` around line 595)

```csharp
private void AddPowerUpRule()
{
    if (SelectedSwapCard is null) return;
    var rule = new TriggerRule
    {
        TriggerType = TwitchTriggerType.PowerUp,
        ActionType = OscActionType.AvatarChange,
        AvatarChangeTargetId = SelectedSwapCard.Profile.TargetAvatarId,
        AvatarTargetName = SelectedSwapCard.Profile.TargetAvatarName,
        Name = "New Power Up Swap"
    };
    SelectedSwapCard.Profile.PowerUpRules.Add(rule);
    var row = new InlinePowerUpRuleRowViewModel(rule, SelectedSwapCard.Profile);
    WireRowCommands(row);
    PowerUpRows.Add(row);
    NotifySettingsChanged();
}
```

- [ ] **Step 3: Update `RebuildRows()`** — after the PaymentRules block (around line 372), add:

```csharp
PowerUpRows.Clear();
```

And after `foreach (var r in swapProfile.PaymentRules)` block, add:
```csharp
foreach (var r in swapProfile.PowerUpRules)
{
    var row = new InlinePowerUpRuleRowViewModel(r, swapProfile);
    WireRowCommands(row);
    PowerUpRows.Add(row);
}
```

- [ ] **Step 4: Update `DeleteRule()`** — after the `InlinePaymentRuleRowViewModel` check (around line ~806), add:

```csharp
else if (row is InlinePowerUpRuleRowViewModel pow
    && SelectedSwapCard.Profile.PowerUpRules.Remove((TriggerRule)pow.Rule))
{
    PowerUpRows.Remove(pow);
}
```

- [ ] **Step 5: Build and verify**

```powershell
dotnet build "E:\!!!Program to work on\Proper Crystal Relay\VrcTwitchOscBridge\VrcTwitchOscBridge.csproj" --no-restore
```
Expected: Build succeeds.

- [ ] **Step 6: Commit**

```bash
git add "VrcTwitchOscBridge/ViewModels/AvatarSwapManagerViewModel.cs"
git commit -m "feat(swap): add PowerUpRows, AddPowerUpRule, and delete handling"
```

---

### Task 8: XAML — Power Up section in AvatarSwapManagerWindow

**Files:**
- Modify: `VrcTwitchOscBridge\AvatarSwapManagerWindow.xaml`

- [ ] **Step 1: Add Power Up section** to the swap editor

In the swap editor right pane (around line 386-397, after the Bits section and before the Subs section), add:

```xml
<Border Background="{DynamicResource NestedPanelBrush}" BorderBrush="{DynamicResource AccentBrush}" BorderThickness="2,0,0,0" CornerRadius="0,3,3,0" Padding="6,3" Margin="0,0,0,4">
    <TextBlock Text="⚡ Power Up" FontWeight="SemiBold" FontSize="12" Foreground="{DynamicResource TextBrush}" />
</Border>
<ItemsControl ItemsSource="{Binding DataContext.PowerUpRows, RelativeSource={RelativeSource AncestorType=Window}}">
    <ItemsControl.ItemTemplate>
        <DataTemplate>
            <uc:InlinePowerUpRuleRowControl />
        </DataTemplate>
    </ItemsControl.ItemTemplate>
</ItemsControl>
<Button Content="+ Add Power Up" Command="{Binding DataContext.AddPowerUpRuleCommand, RelativeSource={RelativeSource AncestorType=Window}}" Style="{StaticResource SecondaryButtonStyle}" HorizontalAlignment="Left" Margin="0,4,0,8" />
```

- [ ] **Step 2: Add DataTemplate for the full editor** (around line 542-547)

After the `SubsRowViewModel` DataTemplate:
```xml
<DataTemplate DataType="{x:Type uc:InlinePowerUpRuleRowViewModel}">
    <uc:InlineRuleEditorControl DataContext="{Binding}" Profile="{Binding Profile}" />
</DataTemplate>
```

- [ ] **Step 3: Remove the old "⚡ Power-up" button** from Advanced Triggers

Remove this button (around line 427):
```xml
<Button Content="⚡ Power-up" Command="{Binding DataContext.AddAdvancedTriggerCommand, RelativeSource={RelativeSource AncestorType=Window}}" CommandParameter="PowerUp" Style="{StaticResource SecondaryButtonStyle}" Margin="0,0,4,4" />
```

- [ ] **Step 4: Build and verify**

```powershell
dotnet build "E:\!!!Program to work on\Proper Crystal Relay\VrcTwitchOscBridge\VrcTwitchOscBridge.csproj" --no-restore
```
Expected: Build succeeds.

- [ ] **Step 5: Commit**

```bash
git add "VrcTwitchOscBridge/AvatarSwapManagerWindow.xaml"
git commit -m "feat(swap): add Power Up section to swap editor, remove old Advanced trigger button"
```

---

### Task 9: Project file — Register new XAML files

**Files:**
- Modify: `VrcTwitchOscBridge\VrcTwitchOscBridge.csproj`

- [ ] **Step 1: Add Page entry** (after line 65 `InlineRouletteRuleRowControl.xaml`):
```xml
<Page Include="UserControls\InlinePowerUpRuleRowControl.xaml" />
```

- [ ] **Step 2: Add Compile entries** (after line ~154 `InlineRouletteRuleRowControl.xaml.cs`):
```xml
<Compile Include="UserControls\InlinePowerUpRuleRowControl.xaml.cs">
  <DependentUpon>InlinePowerUpRuleRowControl.xaml</DependentUpon>
</Compile>
```

- [ ] **Step 3: Add ViewModel compile entry** (after line ~131 `AvatarSetsManagerViewModel.cs`):
```xml
<Compile Include="UserControls\InlinePowerUpRuleRowViewModel.cs" />
```

- [ ] **Step 4: Build and verify**

```powershell
dotnet build "E:\!!!Program to work on\Proper Crystal Relay\VrcTwitchOscBridge\VrcTwitchOscBridge.csproj" --no-restore
```
Expected: Build succeeds with 0 errors.

- [ ] **Step 5: Commit**

```bash
git add "VrcTwitchOscBridge/VrcTwitchOscBridge.csproj"
git commit -m "feat(swap): register InlinePowerUpRuleRowControl in csproj"
```
