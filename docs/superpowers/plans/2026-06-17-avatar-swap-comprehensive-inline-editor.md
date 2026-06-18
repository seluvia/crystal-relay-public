# Avatar Swap — Comprehensive Inline Channel Points Editor Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Restore the 10 missing feature groups (Cooldown, Active Time, Paired Rules mode, Managed Reward Colors, Reward Sync Mode, Reward Name/Cost/Description, Delete When Inactive, Chat Command, Bot Reply, Shared/Numbered Choice) to the Avatar Swap window's inline rule editor.

**Architecture:** Add a slim `ITwitchRewardSource` interface; `MainWindowViewModel` implements it; `AvatarSwapManagerViewModel` takes it via constructor and forwards the Twitch reward list + Refresh/Unlink commands. Expand the existing `InlineAvatarSwapRuleRowControl.xaml` with 10 grouped sections. Reuse the existing `WinForms.ColorDialog` pattern for the color pickers.

**Tech Stack:** C# / WPF / .NET 10, xUnit, existing `RelayCommand` / `ObservableObject` infrastructure, `WinForms.ColorDialog`.

**Spec:** `docs/superpowers/specs/2026-06-17-avatar-swap-comprehensive-inline-editor-design.md`

---

## File Structure

**New files:**
- `VrcTwitchOscBridge/ViewModels/ITwitchRewardSource.cs` — slim interface (3 members).

**Modified files:**
- `VrcTwitchOscBridge/ViewModels/MainWindowViewModel.cs` — implement `ITwitchRewardSource`; pass `this` into the Avatar Swap manager VM at the construction call site (line 5159).
- `VrcTwitchOscBridge/ViewModels/AvatarSwapManagerViewModel.cs` — accept `ITwitchRewardSource`; expose forwarded `TwitchRewardOptions` / `RefreshTwitchRewardsCommand` / `UnlinkTwitchRewardCommand`.
- `VrcTwitchOscBridge/UserControls/InlineAvatarSwapRuleRowControl.xaml` — add 10 group sections inside the expanded panel.
- `VrcTwitchOscBridge/UserControls/InlineAvatarSwapRuleRowControl.xaml.cs` — add `OnPickManagedRewardColorClicked`, `OnResetManagedRewardColorClicked`, `FindRuleFromButton` helper, `NativeWin32Window` helper.
- `VrcTwitchOscBridge/Resources/Localization/en-US.extra.json` + all `<lang>.extra.json` — add new keys.
- `VrcTwitchOscBridge.Tests/AvatarSwapManagerViewModelTests.cs` — update 4 existing constructor call sites; add forwarding tests.
- `VrcTwitchOscBridge.Tests/TriggerRuleRoundTripTests.cs` (new) — property/serialization tests for the new fields.

**Unchanged:** `TriggerRule` model (no changes needed), `AppSettings` serialization, the full `AvatarSwapRuleEditorControl`, the 420px right column width.

---

## Task 1: Create `ITwitchRewardSource` interface

**Files:**
- Create: `VrcTwitchOscBridge/ViewModels/ITwitchRewardSource.cs`

- [ ] **Step 1: Create the interface file**

Create `VrcTwitchOscBridge/ViewModels/ITwitchRewardSource.cs` with:

```csharp
using System.Collections.ObjectModel;
using System.Windows.Input;
using VrcTwitchOscBridge.ViewModels;

namespace VrcTwitchOscBridge.ViewModels;

public interface ITwitchRewardSource
{
    ObservableCollection<TwitchRewardOption> RewardOptions { get; }
    ICommand RefreshTwitchRewardsCommand { get; }
    ICommand UnlinkTwitchRewardCommand { get; }
}
```

- [ ] **Step 2: Build to confirm it compiles**

Run: `dotnet build "E:\!!!Program to work on\Proper Crystal Relay\VrcTwitchOscBridge\VrcTwitchOscBridge.csproj" --no-restore`
Expected: Build succeeded, 0 errors. (The interface is unused so no consumer warnings.)

- [ ] **Step 3: Commit**

```bash
git add VrcTwitchOscBridge/ViewModels/ITwitchRewardSource.cs
git commit -m "feat(avatar-swap): add ITwitchRewardSource interface for inline editor wiring"
```

---

## Task 2: Make `MainWindowViewModel` implement `ITwitchRewardSource`

**Files:**
- Modify: `VrcTwitchOscBridge/ViewModels/MainWindowViewModel.cs:5149` (class declaration)

`MainWindowViewModel` already declares (line 1053): `public ObservableCollection<TwitchRewardOption> RewardOptions { get; }`, and owns `RefreshTwitchRewardsCommand` and `UnlinkTwitchRewardCommand`. Only the interface declaration needs to be added.

- [ ] **Step 1: Add `ITwitchRewardSource` to the class declaration**

Locate the `MainWindowViewModel` class declaration (around line 5149) and add `, ITwitchRewardSource` to its base list. For example, if it currently reads:

```csharp
public sealed class MainWindowViewModel : ObservableObject
```

change it to:

```csharp
public sealed class MainWindowViewModel : ObservableObject, ITwitchRewardSource
```

If the class already implements other interfaces, append `, ITwitchRewardSource` to the existing list (order does not matter). The file is in the `VrcTwitchOscBridge.ViewModels` namespace, so no additional `using` is required.

- [ ] **Step 2: Build**

Run: `dotnet build "E:\!!!Program to work on\Proper Crystal Relay\VrcTwitchOscBridge\VrcTwitchOscBridge.csproj" --no-restore`
Expected: Build succeeded, 0 errors. (All three interface members already exist with matching signatures.)

- [ ] **Step 3: Commit**

```bash
git add VrcTwitchOscBridge/ViewModels/MainWindowViewModel.cs
git commit -m "feat(avatar-swap): make MainWindowViewModel implement ITwitchRewardSource"
```

---

## Task 3 (TDD): Forward Twitch reward source through `AvatarSwapManagerViewModel`

**Files:**
- Modify: `VrcTwitchOscBridge/ViewModels/AvatarSwapManagerViewModel.cs`
- Modify: `VrcTwitchOscBridge.Tests/AvatarSwapManagerViewModelTests.cs`

- [ ] **Step 1: Update the 4 existing test call sites + add a stub + write failing forwarding tests**

Open `VrcTwitchOscBridge.Tests/AvatarSwapManagerViewModelTests.cs`. Add this private stub class at the bottom of the class (inside the `AvatarSwapManagerViewModelTests` class):

```csharp
    private sealed class StubTwitchRewardSource : ITwitchRewardSource
    {
        public ObservableCollection<TwitchRewardOption> RewardOptions { get; } = new();
        public ICommand RefreshTwitchRewardsCommand { get; } = new RelayCommand(() => { });
        public ICommand UnlinkTwitchRewardCommand { get; } = new RelayCommand(p => { });
    }
```

Add the following `using` directives at the top of the test file if not already present:

```csharp
using System.Collections.ObjectModel;
using System.Windows.Input;
using VrcTwitchOscBridge.Infrastructure;
```

Update the 4 existing constructor calls in the test file to pass the stub. Each currently reads:

```csharp
var vm = new AvatarSwapManagerViewModel(settings);
```

Change to:

```csharp
var vm = new AvatarSwapManagerViewModel(settings, new StubTwitchRewardSource());
```

Add the following new failing tests inside the class:

```csharp
    [Fact]
    public void Constructor_ForwardsTwitchRewardSourceProperties()
    {
        var settings = new AppSettings();
        var source = new StubTwitchRewardSource();
        var option = TwitchRewardOption.Placeholder("test-reward");
        source.RewardOptions.Add(option);

        var vm = new AvatarSwapManagerViewModel(settings, source);

        Assert.Same(source.RewardOptions, vm.TwitchRewardOptions);
        Assert.Same(source.RefreshTwitchRewardsCommand, vm.RefreshTwitchRewardsCommand);
        Assert.Same(source.UnlinkTwitchRewardCommand, vm.UnlinkTwitchRewardCommand);
        Assert.Contains(option, vm.TwitchRewardOptions);
    }

    [Fact]
    public void Constructor_PropagatesRewardOptionsCollectionChanges()
    {
        var settings = new AppSettings();
        var source = new StubTwitchRewardSource();
        var vm = new AvatarSwapManagerViewModel(settings, source);

        var option = TwitchRewardOption.Placeholder("late-add");
        source.RewardOptions.Add(option);

        Assert.Contains(option, vm.TwitchRewardOptions);
    }
```

- [ ] **Step 2: Run tests to confirm they fail (and the 4 existing tests fail to compile)**

Run: `dotnet test "E:\!!!Program to work on\Proper Crystal Relay\VrcTwitchOscBridge.Tests\VrcTwitchOscBridge.Tests.csproj" --filter "FullyQualifiedName~AvatarSwapManagerViewModelTests"`
Expected: compile error on the 4 existing `new AvatarSwapManagerViewModel(settings)` calls (no constructor matches) AND on the new tests (no `TwitchRewardOptions` member). This is the failing state.

- [ ] **Step 3: Add the constructor parameter + forwarded properties to `AvatarSwapManagerViewModel`**

Open `VrcTwitchOscBridge/ViewModels/AvatarSwapManagerViewModel.cs`. Change the constructor signature and body. Find:

```csharp
    public AvatarSwapManagerViewModel(AppSettings settings)
    {
        _settings = settings ?? throw new ArgumentNullException(nameof(settings));
        _imageService = new AvatarImageService();
```

Replace with:

```csharp
    public AvatarSwapManagerViewModel(AppSettings settings, ITwitchRewardSource twitchRewardSource)
    {
        _settings = settings ?? throw new ArgumentNullException(nameof(settings));
        _twitchRewardSource = twitchRewardSource ?? throw new ArgumentNullException(nameof(twitchRewardSource));
        _imageService = new AvatarImageService();

        TwitchRewardOptions = _twitchRewardSource.RewardOptions;
        RefreshTwitchRewardsCommand = _twitchRewardSource.RefreshTwitchRewardsCommand;
        UnlinkTwitchRewardCommand = _twitchRewardSource.UnlinkTwitchRewardCommand;
```

(Leave the rest of the existing constructor body intact — the command initializers, event subscriptions, and `RebuildCards()` call remain below.)

Add the backing field and the three public properties. Add this private field near the other private fields (around line 14, after `_imageService`):

```csharp
    private readonly ITwitchRewardSource _twitchRewardSource;
```

Add these public properties near the other `ObservableCollection` properties (e.g. right after `public ObservableCollection<AvatarRouletteCardViewModel> RouletteCards { get; } = new();`):

```csharp
    public ObservableCollection<TwitchRewardOption> TwitchRewardOptions { get; }
    public ICommand RefreshTwitchRewardsCommand { get; }
    public ICommand UnlinkTwitchRewardCommand { get; }
```

The constructor already forwards the references; WPF's `RelativeSource AncestorType=Window` binding in the XAML will pick them up through the DataContext.

- [ ] **Step 4: Run tests to confirm they pass**

Run: `dotnet test "E:\!!!Program to work on\Proper Crystal Relay\VrcTwitchOscBridge.Tests\VrcTwitchOscBridge.Tests.csproj" --filter "FullyQualifiedName~AvatarSwapManagerViewModelTests"`
Expected: All tests pass (the 4 existing + 2 new = 6 from this class, plus the other `HasAnyRules_*` tests that don't construct the VM = total 11 in this file). Failures: 0.

- [ ] **Step 5: Commit**

```bash
git add VrcTwitchOscBridge/ViewModels/AvatarSwapManagerViewModel.cs VrcTwitchOscBridge.Tests/AvatarSwapManagerViewModelTests.cs
git commit -m "feat(avatar-swap): forward Twitch reward source through AvatarSwapManagerViewModel"
```

---

## Task 4: Update production call site to pass `this` as the reward source

**Files:**
- Modify: `VrcTwitchOscBridge/ViewModels/MainWindowViewModel.cs:5159`

- [ ] **Step 1: Update the construction call**

In `VrcTwitchOscBridge/ViewModels/MainWindowViewModel.cs`, find the `OpenAvatarSwapManagerCommand` body (line 5159):

```csharp
        var managerVm = new AvatarSwapManagerViewModel(Settings);
```

Change to:

```csharp
        var managerVm = new AvatarSwapManagerViewModel(Settings, this);
```

(`this` is the `MainWindowViewModel`, which now implements `ITwitchRewardSource` per Task 2.)

- [ ] **Step 2: Build**

Run: `dotnet build "E:\!!!Program to work on\Proper Crystal Relay\VrcTwitchOscBridge\VrcTwitchOscBridge.csproj" --no-restore`
Expected: Build succeeded, 0 errors.

- [ ] **Step 3: Run all AvatarSwap tests**

Run: `dotnet test "E:\!!!Program to work on\Proper Crystal Relay\VrcTwitchOscBridge.Tests\VrcTwitchOscBridge.Tests.csproj" --filter "FullyQualifiedName~AvatarSwap"`
Expected: 0 failures, all pass.

- [ ] **Step 4: Commit**

```bash
git add VrcTwitchOscBridge/ViewModels/MainWindowViewModel.cs
git commit -m "feat(avatar-swap): pass MainWindowViewModel as ITwitchRewardSource to manager"
```

---

## Task 5: Add XAML — Twitch Reward + Delete When Inactive + Timing + Bot Reply groups

**Files:**
- Modify: `VrcTwitchOscBridge/UserControls/InlineAvatarSwapRuleRowControl.xaml` (inside the expanded `Border` that has `Visibility="{Binding IsExpanded, ...}"`)

The existing expanded panel currently contains a header row (Edit Trigger / Done / Cancel) and 3 fields: Name, Reward Cost (under `Rule.UsesChannelPointReward`), Minimum Amount, Chat Command text. The new groups slot in between the existing Name field and the existing Cost/Amount panels. Replace the entire content of the expanded panel's inner `StackPanel` (the one starting after the header `Grid` and ending before the closing `</StackPanel>` of the panel) with the new ordered list.

- [ ] **Step 1: Locate the expanded panel in `InlineAvatarSwapRuleRowControl.xaml`**

Open `VrcTwitchOscBridge/UserControls/InlineAvatarSwapRuleRowControl.xaml`. The expanded panel is the second `Border` in the outer `StackPanel` (it has `BorderBrush="{DynamicResource AccentBrush}"` and `Visibility="{Binding IsExpanded, ...}"`). It contains a header `Grid` followed by a `StackPanel` with the existing field sections (Name, Reward Cost, Minimum Amount, Chat Command).

- [ ] **Step 2: Add the 4 new simple groups after the existing Name section**

Inside that inner `StackPanel`, **after** the existing Name `TextBox` (the one bound to `Rule.Name`) and **before** the existing `StackPanel Visibility="{Binding Rule.UsesChannelPointReward, ...}"` (the Reward Cost block), insert the following 4 new sections. They use the existing `LabelStyle` and `InputStyle` resources.

```xaml
                <TextBlock Style="{StaticResource LabelStyle}" Text="{loc:Translate 'Twitch Reward'}" />
                <TextBlock Style="{StaticResource LabelStyle}" Text="{loc:Translate 'Reward Name'}" />
                <TextBox Style="{StaticResource InputStyle}" Text="{Binding Rule.ChannelPointRewardTitle, UpdateSourceTrigger=PropertyChanged}" Margin="0,0,0,6" />
                <TextBlock Style="{StaticResource LabelStyle}" Text="{loc:Translate 'Reward Description'}" />
                <TextBox Style="{StaticResource InputStyle}" Text="{Binding Rule.ChannelPointRewardDescription, UpdateSourceTrigger=PropertyChanged}" AcceptsReturn="True" Height="60" TextWrapping="Wrap" Margin="0,0,0,6" />

                <TextBlock Style="{StaticResource LabelStyle}" Text="{loc:Translate 'Delete Reward When Inactive'}" />
                <CheckBox IsChecked="{Binding Rule.DeleteManagedRewardWhenInactive, UpdateSourceTrigger=PropertyChanged}" Content="{loc:Translate 'Free Twitch reward slots when inactive'}" Foreground="{DynamicResource MutedBrush}" Margin="0,0,0,6" />

                <TextBlock Style="{StaticResource LabelStyle}" Text="{loc:Translate 'Active Time (seconds)'}" />
                <TextBox Style="{StaticResource InputStyle}" Text="{Binding Rule.DurationSeconds, UpdateSourceTrigger=PropertyChanged}" Margin="0,0,0,6" />
                <TextBlock Style="{StaticResource LabelStyle}" Text="{loc:Translate 'Cooldown (seconds)'}" />
                <TextBox Style="{StaticResource InputStyle}" Text="{Binding Rule.CooldownSeconds, UpdateSourceTrigger=PropertyChanged}" Margin="0,0,0,6" />

                <TextBlock Style="{StaticResource LabelStyle}" Text="{loc:Translate 'Bot Reply'}" />
                <TextBox Style="{StaticResource InputStyle}" Text="{Binding Rule.BotMessageTemplate, UpdateSourceTrigger=PropertyChanged}" AcceptsReturn="True" Height="60" TextWrapping="Wrap" Margin="0,0,0,6" />
```

- [ ] **Step 3: Build**

Run: `dotnet build "E:\!!!Program to work on\Proper Crystal Relay\VrcTwitchOscBridge\VrcTwitchOscBridge.csproj" --no-restore`
Expected: Build succeeded, 0 errors. (XAML compiles; missing loc keys will show as the English fallback at runtime until Task 9 adds them.)

- [ ] **Step 4: Commit**

```bash
git add VrcTwitchOscBridge/UserControls/InlineAvatarSwapRuleRowControl.xaml
git commit -m "feat(avatar-swap): add Twitch Reward, Delete Inactive, Timing, Bot Reply groups to inline editor"
```

---

## Task 6: Add XAML — Reward Sync Mode (with Link Existing picker + Refresh/Unlink) + Pairing

**Files:**
- Modify: `VrcTwitchOscBridge/UserControls/InlineAvatarSwapRuleRowControl.xaml`

- [ ] **Step 1: Add the Reward Sync Mode group (after Delete Inactive) and Pairing group (after Timing)**

In the same expanded `StackPanel`, insert the following two sections. Place **Reward Sync Mode** immediately after the "Delete Reward When Inactive" `CheckBox` block, and place **Pairing** immediately after the "Cooldown" `TextBox` block (and before "Bot Reply").

```xaml
                <TextBlock Style="{StaticResource LabelStyle}" Text="{loc:Translate 'Reward Sync Mode'}" />
                <StackPanel Margin="0,0,0,4">
                    <RadioButton GroupName="SyncMode" Content="{loc:Translate 'Create or Manage'}"
                                 IsChecked="{Binding Rule.RewardSyncMode, Converter={StaticResource CreateOrManageToBoolConverter}, ConverterParameter=CreateOrManage}"
                                 Foreground="{DynamicResource TextBrush}" />
                    <RadioButton GroupName="SyncMode" Content="{loc:Translate 'Link Existing'}"
                                 IsChecked="{Binding Rule.RewardSyncMode, Converter={StaticResource CreateOrManageToBoolConverter}, ConverterParameter=LinkExisting}"
                                 Foreground="{DynamicResource TextBrush}" />
                </StackPanel>
                <Border Background="{DynamicResource NestedPanelBrush}" BorderBrush="{DynamicResource AccentBrush}" BorderThickness="2,0,0,0" CornerRadius="0,3,3,0" Padding="8,4" Margin="0,0,0,6">
                    <StackPanel>
                        <TextBlock Style="{StaticResource LabelStyle}" Text="{loc:Translate 'Existing Twitch Reward'}" />
                        <ComboBox ItemsSource="{Binding DataContext.TwitchRewardOptions, RelativeSource={RelativeSource AncestorType=Window}}"
                                  SelectedValuePath="Id" SelectedValue="{Binding Rule.ChannelPointRewardId, UpdateSourceTrigger=PropertyChanged}"
                                  Background="{DynamicResource InputBrush}" Foreground="{DynamicResource TextBrush}" BorderBrush="{DynamicResource InputBorderBrush}" Margin="0,0,0,4" />
                        <StackPanel Orientation="Horizontal">
                            <Button Content="{loc:Translate 'Refresh Rewards'}" Command="{Binding DataContext.RefreshTwitchRewardsCommand, RelativeSource={RelativeSource AncestorType=Window}}" Style="{StaticResource InputStyle}" Margin="0,0,4,0" Padding="6,2" />
                            <Button Content="{loc:Translate 'Unlink Reward'}" Command="{Binding DataContext.UnlinkTwitchRewardCommand, RelativeSource={RelativeSource AncestorType=Window}}" CommandParameter="{Binding Rule}" Style="{StaticResource InputStyle}" Padding="6,2" />
                        </StackPanel>
                    </StackPanel>
                </Border>
```

```xaml
                <TextBlock Style="{StaticResource LabelStyle}" Text="{loc:Translate 'Pairing'}" />
                <ComboBox SelectedValuePath="Tag" SelectedValue="{Binding Rule.SpecialRulePairingMode, UpdateSourceTrigger=PropertyChanged}"
                          Background="{DynamicResource InputBrush}" Foreground="{DynamicResource TextBrush}" BorderBrush="{DynamicResource InputBorderBrush}" Margin="0,0,0,4">
                    <ComboBoxItem Content="{loc:Translate 'Hide Paired While On Cooldown'}" Tag="{x:Static models:SpecialRulePairingMode.HidePairedWhileActive}" />
                    <ComboBoxItem Content="{loc:Translate 'Show Paired Only While On Cooldown'}" Tag="{x:Static models:SpecialRulePairingMode.ShowPairedWhileActive}" />
                </ComboBox>
                <TextBlock Text="{loc:Translate 'Manage paired rules in the Full Editor.'}" Foreground="{DynamicResource MutedBrush}" FontSize="10" FontStyle="Italic" Margin="0,0,0,6" />
```

- [ ] **Step 2: Add the missing value converter for the RadioButton IsChecked binding**

The two-way RadioButton binding to an enum requires a `BoolToEnumConverter` (or `EnumToBoolConverter`). Add a converter resource to the `UserControl.Resources` of `InlineAvatarSwapRuleRowControl.xaml`. Find the opening `<UserControl.Resources>` block and add this entry next to the existing `BoolToVis` converter:

```xaml
        <local:EnumToBoolConverter x:Key="CreateOrManageToBoolConverter" />
```

(Where `local` is the `xmlns:local="clr-namespace:VrcTwitchOscBridge"` namespace that the old editor's `InverseBoolToVisibilityConverter` uses — add the same `xmlns:local` declaration to the `UserControl` root if it isn't already present.)

The converter implementation is a new small file `VrcTwitchOscBridge/UserControls/EnumToBoolConverter.cs` with this exact content:

```csharp
using System;
using System.Globalization;
using System.Windows.Data;

namespace VrcTwitchOscBridge.UserControls;

public sealed class EnumToBoolConverter : IValueConverter
{
    public object Convert(object value, Type targetType, object parameter, CultureInfo culture)
    {
        if (value is null || parameter is null) return false;
        return string.Equals(value.ToString(), parameter.ToString(), StringComparison.OrdinalIgnoreCase);
    }

    public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture)
    {
        if (value is true && parameter is not null)
        {
            return Enum.Parse(targetType, parameter.ToString()!, ignoreCase: true);
        }
        return Binding.DoNothing;
    }
}
```

- [ ] **Step 3: Build**

Run: `dotnet build "E:\!!!Program to work on\Proper Crystal Relay\VrcTwitchOscBridge\VrcTwitchOscBridge.csproj" --no-restore`
Expected: Build succeeded, 0 errors.

- [ ] **Step 4: Commit**

```bash
git add VrcTwitchOscBridge/UserControls/InlineAvatarSwapRuleRowControl.xaml VrcTwitchOscBridge/UserControls/EnumToBoolConverter.cs
git commit -m "feat(avatar-swap): add Reward Sync Mode (Link Existing picker + Refresh/Unlink) and Pairing"
```

---

## Task 7: Add XAML — Shared / Numbered Choice + Chat Command (both with progressive disclosure)

**Files:**
- Modify: `VrcTwitchOscBridge/UserControls/InlineAvatarSwapRuleRowControl.xaml`

- [ ] **Step 1: Add the Shared/Numbered group (after Pairing, before Bot Reply)**

Insert after the "Manage paired rules..." italic note:

```xaml
                <TextBlock Style="{StaticResource LabelStyle}" Text="{loc:Translate 'Shared / Numbered Reward Choice'}" />
                <CheckBox x:Name="SharedChoiceEnabledCheckBox" IsChecked="{Binding Rule.SharedRewardChoiceEnabled, UpdateSourceTrigger=PropertyChanged}" Content="{loc:Translate 'Use shared numbered reward'}" Foreground="{DynamicResource TextBrush}" />
                <StackPanel Margin="0,4,0,6" Visibility="{Binding IsChecked, ElementName=SharedChoiceEnabledCheckBox, Converter={StaticResource BoolToVis}}">
                    <TextBlock Style="{StaticResource LabelStyle}" Text="{loc:Translate 'Choice number'}" />
                    <TextBox Style="{StaticResource InputStyle}" Text="{Binding Rule.SharedRewardChoiceNumber, UpdateSourceTrigger=PropertyChanged}" Margin="0,0,0,4" />
                    <TextBlock Style="{StaticResource LabelStyle}" Text="{loc:Translate 'Choice label'}" />
                    <TextBox Style="{StaticResource InputStyle}" Text="{Binding Rule.SharedRewardHelpText, UpdateSourceTrigger=PropertyChanged}" />
                </StackPanel>
```

- [ ] **Step 2: Replace the existing Chat Command section with the progressive-disclosure version**

The existing XAML has a single `StackPanel` for Chat Command (with a `DataTrigger` on `Rule.TriggerType == ChatCommand`) that shows the `ChatCommandText` textbox. **Replace the entire existing Chat Command block** (the one from `<StackPanel>` with the `DataTrigger` on `ChatCommand` through its closing `</StackPanel>`) with this progressive-disclosure version. The new block does NOT depend on `Rule.TriggerType` — per the "show all for all" decision, it shows for every trigger type:

```xaml
                <TextBlock Style="{StaticResource LabelStyle}" Text="{loc:Translate 'Chat Command'}" />
                <CheckBox x:Name="ChatCommandEnabledCheckBox" IsChecked="{Binding Rule.ChatCommandEnabled, UpdateSourceTrigger=PropertyChanged}" Content="{loc:Translate 'Enabled'}" Foreground="{DynamicResource TextBrush}" />
                <StackPanel Margin="0,4,0,6" Visibility="{Binding IsChecked, ElementName=ChatCommandEnabledCheckBox, Converter={StaticResource BoolToVis}}">
                    <TextBlock Style="{StaticResource LabelStyle}" Text="{loc:Translate 'Chat command'}" />
                    <TextBox Style="{StaticResource InputStyle}" Text="{Binding Rule.ChatCommandText, UpdateSourceTrigger=PropertyChanged}" Margin="0,0,0,4" />
                    <TextBlock Style="{StaticResource LabelStyle}" Text="{loc:Translate 'Permission'}" />
                    <ComboBox SelectedValuePath="Tag" SelectedValue="{Binding Rule.ChatCommandPermission, UpdateSourceTrigger=PropertyChanged}"
                              Background="{DynamicResource InputBrush}" Foreground="{DynamicResource TextBrush}" BorderBrush="{DynamicResource InputBorderBrush}">
                        <ComboBoxItem Content="{loc:Translate 'Everyone'}" Tag="{x:Static models:ChatCommandPermission.Everyone}" />
                        <ComboBoxItem Content="{loc:Translate 'Moderators'}" Tag="{x:Static models:ChatCommandPermission.Moderators}" />
                        <ComboBoxItem Content="{loc:Translate 'Broadcaster'}" Tag="{x:Static models:ChatCommandPermission.Broadcaster}" />
                    </ComboBox>
                </StackPanel>
```

The `x:Static` references require the `xmlns:models="clr-namespace:VrcTwitchOscBridge.Models"` declaration (already present on the root `UserControl` from earlier work — verify and add if missing).

- [ ] **Step 3: Build**

Run: `dotnet build "E:\!!!Program to work on\Proper Crystal Relay\VrcTwitchOscBridge\VrcTwitchOscBridge.csproj" --no-restore`
Expected: Build succeeded, 0 errors.

- [ ] **Step 4: Commit**

```bash
git add VrcTwitchOscBridge/UserControls/InlineAvatarSwapRuleRowControl.xaml
git commit -m "feat(avatar-swap): add Shared Numbered Choice and Chat Command with progressive disclosure"
```

---

## Task 8: Add XAML — Reward Colors (swatches + Pick/Reset) + code-behind handlers

**Files:**
- Modify: `VrcTwitchOscBridge/UserControls/InlineAvatarSwapRuleRowControl.xaml`
- Modify: `VrcTwitchOscBridge/UserControls/InlineAvatarSwapRuleRowControl.xaml.cs`

- [ ] **Step 1: Add the Reward Colors group (after Chat Command, before Bot Reply)**

Insert the following XAML:

```xaml
                <TextBlock Style="{StaticResource LabelStyle}" Text="{loc:Translate 'Reward Colors'}" />
                <Grid Margin="0,0,0,4">
                    <Grid.ColumnDefinitions>
                        <ColumnDefinition Width="Auto" />
                        <ColumnDefinition Width="*" />
                        <ColumnDefinition Width="Auto" />
                        <ColumnDefinition Width="Auto" />
                    </Grid.ColumnDefinitions>
                    <Ellipse Grid.Column="0" Width="16" Height="16" Fill="{Binding Rule.ManagedRewardReadyBrush}" Stroke="{DynamicResource TextBrush}" StrokeThickness="1" Margin="0,0,6,0" VerticalAlignment="Center" />
                    <TextBlock Grid.Column="1" Text="{loc:Translate 'Ready'}" Foreground="{DynamicResource TextBrush}" VerticalAlignment="Center" />
                    <Button Grid.Column="2" Content="{loc:Translate 'Pick...'}" Tag="Ready" Click="OnPickManagedRewardColorClicked" Style="{StaticResource InputStyle}" Padding="6,2" Margin="0,0,4,0" />
                    <Button Grid.Column="3" Content="{loc:Translate 'Reset'}" Tag="Ready" Click="OnResetManagedRewardColorClicked" Style="{StaticResource InputStyle}" Padding="6,2" />
                </Grid>
                <Grid Margin="0,0,0,6">
                    <Grid.ColumnDefinitions>
                        <ColumnDefinition Width="Auto" />
                        <ColumnDefinition Width="*" />
                        <ColumnDefinition Width="Auto" />
                        <ColumnDefinition Width="Auto" />
                    </Grid.ColumnDefinitions>
                    <Ellipse Grid.Column="0" Width="16" Height="16" Fill="{Binding Rule.ManagedRewardCooldownBrush}" Stroke="{DynamicResource TextBrush}" StrokeThickness="1" Margin="0,0,6,0" VerticalAlignment="Center" />
                    <TextBlock Grid.Column="1" Text="{loc:Translate 'Cooldown'}" Foreground="{DynamicResource TextBrush}" VerticalAlignment="Center" />
                    <Button Grid.Column="2" Content="{loc:Translate 'Pick...'}" Tag="Cooldown" Click="OnPickManagedRewardColorClicked" Style="{StaticResource InputStyle}" Padding="6,2" Margin="0,0,4,0" />
                    <Button Grid.Column="3" Content="{loc:Translate 'Reset'}" Tag="Cooldown" Click="OnResetManagedRewardColorClicked" Style="{StaticResource InputStyle}" Padding="6,2" />
                </Grid>
```

The bindings to `Rule.ManagedRewardReadyBrush` and `Rule.ManagedRewardCooldownBrush` require two computed brush properties on `TriggerRule`. Open `VrcTwitchOscBridge/Models/TriggerRule.cs`, find the `ManagedRewardReadyColor` getter area, and add these two properties (anywhere in the class — they just read the hex strings and produce a `SolidColorBrush` via a small `ColorConverter`-style helper). Add a private static helper and the two public read-only properties:

```csharp
    public System.Windows.Media.Brush ManagedRewardReadyBrush => HexToBrush(ManagedRewardReadyColor);

    public System.Windows.Media.Brush ManagedRewardCooldownBrush => HexToBrush(ManagedRewardCooldownColor);

    private static System.Windows.Media.Brush HexToBrush(string? hex)
    {
        if (string.IsNullOrWhiteSpace(hex)) return System.Windows.Media.Brushes.Transparent;
        try
        {
            var converter = new System.Windows.Media.BrushConverter();
            return (System.Windows.Media.Brush?)converter.ConvertFromString(hex) ?? System.Windows.Media.Brushes.Transparent;
        }
        catch
        {
            return System.Windows.Media.Brushes.Transparent;
        }
    }
```

- [ ] **Step 2: Add the click handlers and helpers to the code-behind**

Open `VrcTwitchOscBridge/UserControls/InlineAvatarSwapRuleRowControl.xaml.cs`. Replace the entire file with:

```csharp
using System;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using VrcTwitchOscBridge.Models;
using VrcTwitchOscBridge.Services;
using VrcTwitchOscBridge.ViewModels;
using WinForms = System.Windows.Forms;

namespace VrcTwitchOscBridge.UserControls;

public partial class InlineAvatarSwapRuleRowControl : UserControl
{
    public static readonly DependencyProperty RowProperty = DependencyProperty.Register(
        nameof(Row), typeof(InlineAvatarSwapRuleRowViewModel), typeof(InlineAvatarSwapRuleRowControl),
        new PropertyMetadata(null));

    public InlineAvatarSwapRuleRowViewModel? Row
    {
        get => (InlineAvatarSwapRuleRowViewModel?)GetValue(RowProperty);
        set => SetValue(RowProperty, value);
    }

    public InlineAvatarSwapRuleRowControl() => InitializeComponent();

    private void OnPickManagedRewardColorClicked(object sender, RoutedEventArgs e)
    {
        if (sender is not Button button) return;

        var isCooldown = string.Equals(button.Tag?.ToString(), "Cooldown", StringComparison.OrdinalIgnoreCase);
        var fallback = isCooldown
            ? ManagedRewardPresentation.InUseBackgroundColor
            : ManagedRewardPresentation.ReadyBackgroundColor;

        var rule = FindRuleFromButton(button);
        if (rule is null) return;

        var initial = isCooldown ? rule.ManagedRewardCooldownColor : rule.ManagedRewardReadyColor;
        if (string.IsNullOrWhiteSpace(initial)) return;

        using var dialog = new WinForms.ColorDialog
        {
            AllowFullOpen = true,
            FullOpen = true,
            AnyColor = true,
            SolidColorOnly = false,
            Color = ManagedRewardPresentation.ToDrawingColor(initial, fallback)
        };

        var owner = Window.GetWindow(this);
        var ownerHandle = owner is not null
            ? new System.Windows.Interop.WindowInteropHelper(owner).Handle
            : IntPtr.Zero;
        var result = ownerHandle != IntPtr.Zero
            ? dialog.ShowDialog(new NativeWin32Window(ownerHandle))
            : dialog.ShowDialog();
        if (result != WinForms.DialogResult.OK) return;

        var hex = ManagedRewardPresentation.ToHex(dialog.Color);
        if (isCooldown) rule.ManagedRewardCooldownColor = hex;
        else rule.ManagedRewardReadyColor = hex;
    }

    private void OnResetManagedRewardColorClicked(object sender, RoutedEventArgs e)
    {
        if (sender is not Button button) return;
        var isCooldown = string.Equals(button.Tag?.ToString(), "Cooldown", StringComparison.OrdinalIgnoreCase);

        var rule = FindRuleFromButton(button);
        if (rule is null) return;

        if (isCooldown) rule.ManagedRewardCooldownColor = ManagedRewardPresentation.InUseBackgroundColor;
        else rule.ManagedRewardReadyColor = ManagedRewardPresentation.ReadyBackgroundColor;
    }

    private static TriggerRule? FindRuleFromButton(Button button)
    {
        DependencyObject? candidate = button;
        while (candidate is not null)
        {
            if (candidate is FrameworkElement { DataContext: TriggerRule rule })
            {
                return rule;
            }
            candidate = VisualTreeHelper.GetParent(candidate);
        }
        return null;
    }

    private sealed class NativeWin32Window : WinForms.IWin32Window
    {
        public NativeWin32Window(IntPtr handle) => Handle = handle;
        public IntPtr Handle { get; }
    }
}
```

- [ ] **Step 3: Build**

Run: `dotnet build "E:\!!!Program to work on\Proper Crystal Relay\VrcTwitchOscBridge\VrcTwitchOscBridge.csproj" --no-restore`
Expected: Build succeeded, 0 errors.

- [ ] **Step 4: Run all AvatarSwap tests**

Run: `dotnet test "E:\!!!Program to work on\Proper Crystal Relay\VrcTwitchOscBridge.Tests\VrcTwitchOscBridge.Tests.csproj" --filter "FullyQualifiedName~AvatarSwap"`
Expected: 0 failures.

- [ ] **Step 5: Commit**

```bash
git add VrcTwitchOscBridge/UserControls/InlineAvatarSwapRuleRowControl.xaml VrcTwitchOscBridge/UserControls/InlineAvatarSwapRuleRowControl.xaml.cs VrcTwitchOscBridge/Models/TriggerRule.cs
git commit -m "feat(avatar-swap): add Reward Colors swatches with Pick/Reset (WinForms ColorDialog)"
```

---

## Task 9: Localization — add new keys + translate to all `.extra.json` files

**Files:**
- Modify: all 14 `VrcTwitchOscBridge/Resources/Localization/<lang>.extra.json` files

The XAML in Tasks 5–8 references these `loc:Translate` keys. The keys below are **new** (not already in the en-US base/extra files). Existing keys reused without changes: `Name`, `Reward Cost`, `Reward Name`, `Reward Description`, `Active Time (seconds)`, `Cooldown (seconds)`, `Cooldown`, `Ready`, `Pick...`, `Reset`, `Chat Command`, `Enabled`, `Chat command`, `Permission`, `Bot Reply`, `Existing Twitch Reward`, `Refresh Rewards`, `Unlink Reward`, `Use shared numbered reward`, `Choice number`, `Choice label`, `Delete Twitch reward when inactive` (close enough to reuse for the "Delete Reward When Inactive" label via direct key match — but use a new key for the localized label and a separate one for the checkbox text). To stay unambiguous, the new keys are:

| Key | English value |
|-----|---------------|
| `Twitch Reward` | "Twitch Reward" |
| `Reward Sync Mode` | "Reward Sync Mode" |
| `Create or Manage` | "Create or Manage" |
| `Link Existing` | "Link Existing" |
| `Delete Reward When Inactive` | "Delete Reward When Inactive" |
| `Free Twitch reward slots when inactive` | "Free Twitch reward slots when inactive" |
| `Shared / Numbered Reward Choice` | "Shared / Numbered Reward Choice" |
| `Pairing` | "Pairing" |
| `Hide Paired While On Cooldown` | "Hide Paired While On Cooldown" |
| `Show Paired Only While On Cooldown` | "Show Paired Only While On Cooldown" |
| `Manage paired rules in the Full Editor.` | "Manage paired rules in the Full Editor." |
| `Reward Colors` | "Reward Colors" |
| `Everyone` | "Everyone" |
| `Moderators` | "Moderators" |
| `Broadcaster` | "Broadcaster" |

- [ ] **Step 1: Add the new keys to `en-US.extra.json`**

Open `E:\!!!Program to work on\Proper Crystal Relay\VrcTwitchOscBridge\Resources\Localization\en-US.extra.json`. Find the closing `}` (last line). Before it, add a comma to the previous line if it doesn't end with one, then add the 15 new keys (English values as above). The final lines should look like:

```json
  "Edit Trigger": "Edit Trigger",
  "Twitch Reward": "Twitch Reward",
  "Reward Sync Mode": "Reward Sync Mode",
  "Create or Manage": "Create or Manage",
  "Link Existing": "Link Existing",
  "Delete Reward When Inactive": "Delete Reward When Inactive",
  "Free Twitch reward slots when inactive": "Free Twitch reward slots when inactive",
  "Shared / Numbered Reward Choice": "Shared / Numbered Reward Choice",
  "Pairing": "Pairing",
  "Hide Paired While On Cooldown": "Hide Paired While On Cooldown",
  "Show Paired Only While On Cooldown": "Show Paired Only While On Cooldown",
  "Manage paired rules in the Full Editor.": "Manage paired rules in the Full Editor.",
  "Reward Colors": "Reward Colors",
  "Everyone": "Everyone",
  "Moderators": "Moderators",
  "Broadcaster": "Broadcaster"
}
```

(Keep the closing `}` on the last line.)

- [ ] **Step 2: Add translated values to every other `.extra.json`**

Run the following PowerShell script (it appends the 15 keys to each `<culture>.extra.json` with a translations map, using the same `before-the-last-}` pattern from the previous "Edit Trigger" fix). Save this as `E:\!!!Program to work on\Proper Crystal Relay\tools\private\Add-AvatarSwapInlineEditorLoc.ps1` and run it from a normal PowerShell prompt (the project already has `tools/private` for one-off local scripts per `AGENTS.md`):

```powershell
$map = [ordered]@{
  'Twitch Reward' = @{
    'de-DE' = 'Twitch-Belohnung'; 'es-ES' = 'Recompensa de Twitch'; 'fr-FR' = 'Récompense Twitch'
    'it-IT' = 'Ricompensa Twitch'; 'ja-JP' = 'Twitchリワード'; 'ko-KR' = '트위치 보상'
    'pt-BR' = 'Recompensa da Twitch'; 'ru-RU' = 'Награда Twitch'; 'sv-SE' = 'Twitch-belöning'
    'zh-CN' = 'Twitch 奖励'; 'zh-TW' = 'Twitch 獎勵'; 'th-TH' = 'รางวัล Twitch'; 'pl-PL' = 'Nagroda Twitch'
  }
  'Reward Sync Mode' = @{ 'de-DE'='Belohnungs-Sync-Modus'; 'es-ES'='Modo de sincronización de recompensa'; 'fr-FR'='Mode de synchronisation de la récompense'; 'it-IT'='Modalità sincronizzazione ricompensa'; 'ja-JP'='リワード同期モード'; 'ko-KR'='보상 동기화 모드'; 'pt-BR'='Modo de sincronização da recompensa'; 'ru-RU'='Режим синхронизации награды'; 'sv-SE'='Synk-läge för belöning'; 'zh-CN'='奖励同步模式'; 'zh-TW'='獎勵同步模式'; 'th-TH'='โหมดซิงค์รางวัล'; 'pl-PL'='Tryb synchronizacji nagrody' }
  'Create or Manage' = @{ 'de-DE'='Erstellen oder verwalten'; 'es-ES'='Crear o administrar'; 'fr-FR'='Créer ou gérer'; 'it-IT'='Crea o gestisci'; 'ja-JP'='作成または管理'; 'ko-KR'='만들기 또는 관리'; 'pt-BR'='Criar ou gerenciar'; 'ru-RU'='Создать или управлять'; 'sv-SE'='Skapa eller hantera'; 'zh-CN'='创建或管理'; 'zh-TW'='建立或管理'; 'th-TH'='สร้างหรือจัดการ'; 'pl-PL'='Utwórz lub zarządzaj' }
  'Link Existing' = @{ 'de-DE'='Vorhandene verknüpfen'; 'es-ES'='Vincular existente'; 'fr-FR'='Associer une récompense existante'; 'it-IT'='Collega esistente'; 'ja-JP'='既存を連携'; 'ko-KR'='기존 보상 연결'; 'pt-BR'='Vincular existente'; 'ru-RU'='Привязать существующую'; 'sv-SE'='Länka befintlig'; 'zh-CN'='关联现有奖励'; 'zh-TW'='連結現有獎勵'; 'th-TH'='เชื่อมต่อรางวัลที่มีอยู่'; 'pl-PL'='Połącz istniejącą' }
  'Delete Reward When Inactive' = @{ 'de-DE'='Belohnung im inaktiven Zustand löschen'; 'es-ES'='Eliminar recompensa cuando esté inactiva'; 'fr-FR'='Supprimer la récompense quand inactive'; 'it-IT'='Elimina ricompensa quando inattiva'; 'ja-JP'='非アクティブ時にリワードを削除'; 'ko-KR'='비활성 시 보상 삭제'; 'pt-BR'='Excluir recompensa quando inativa'; 'ru-RU'='Удалить награду в неактивном состоянии'; 'sv-SE'='Ta bort belöning när inaktiv'; 'zh-CN'='非活动时删除奖励'; 'zh-TW'='非活動時刪除獎勵'; 'th-TH'='ลบรางวัลเมื่อไม่ใช้งาน'; 'pl-PL'='Usuń nagrodę, gdy nieaktywna' }
  'Free Twitch reward slots when inactive' = @{ 'de-DE'='Twitch-Belohnungs-Slots freigeben, wenn inaktiv'; 'es-ES'='Liberar espacios de recompensa de Twitch cuando esté inactivo'; 'fr-FR'='Libérer les emplacements de récompense Twitch quand inactif'; 'it-IT'='Libera slot ricompensa Twitch quando inattivo'; 'ja-JP'='非アクティブ時にTwitchリワード枠を解放'; 'ko-KR'='비활성 시 트위치 보상 슬롯 해제'; 'pt-BR'='Liberar slots de recompensa da Twitch quando inativo'; 'ru-RU'='Освобождать слоты наград Twitch в неактивном состоянии'; 'sv-SE'='Frigör Twitch-belöningsplatser när inaktiv'; 'zh-CN'='非活动时释放 Twitch 奖励位'; 'zh-TW'='非活動時釋出 Twitch 獎勵名額'; 'th-TH'='ปลดปล่อยช่องรางวัล Twitch เมื่อไม่ใช้งาน'; 'pl-PL'='Zwolnij miejsca nagród Twitch, gdy nieaktywne' }
  'Shared / Numbered Reward Choice' = @{ 'de-DE'='Geteilte / nummerierte Belohnungsauswahl'; 'es-ES'='Elección de recompensa compartida / numerada'; 'fr-FR'='Choix de récompense partagé / numéroté'; 'it-IT'='Scelta ricompensa condivisa / numerata'; 'ja-JP'='共有 / 番号付きリワード選択'; 'ko-KR'='공유 / 번호 지정 보상 선택'; 'pt-BR'='Escolha de recompensa compartilhada / numerada'; 'ru-RU'='Общий / пронумерованный выбор награды'; 'sv-SE'='Delad / numrerad belöning'; 'zh-CN'='共享 / 编号奖励选项'; 'zh-TW'='共用 / 編號獎勵選項'; 'th-TH'='ตัวเลือกรางวัลแบบแชร์ / มีหมายเลข'; 'pl-PL'='Wspólny / numerowany wybór nagrody' }
  'Pairing' = @{ 'de-DE'='Paarung'; 'es-ES'='Emparejamiento'; 'fr-FR'='Jumelage'; 'it-IT'='Accoppiamento'; 'ja-JP'='ペアリング'; 'ko-KR'='페어링'; 'pt-BR'='Emparelhamento'; 'ru-RU'='Спаривание'; 'sv-SE'='Parkoppling'; 'zh-CN'='配对'; 'zh-TW'='配對'; 'th-TH'='การจับคู่'; 'pl-PL'='Parowanie' }
  'Hide Paired While On Cooldown' = @{ 'de-DE'='Gepaarte während Abklingzeit ausblenden'; 'es-ES'='Ocultar emparejadas durante el enfriamiento'; 'fr-FR'='Masquer les règles jumelées pendant le temps de recharge'; 'it-IT'='Nascondi accoppiate durante il cooldown'; 'ja-JP'='クールダウン中はペアを非表示'; 'ko-KR'='쿨다운 중 페어링 숨기기'; 'pt-BR'='Ocultar emparelhadas durante o cooldown'; 'ru-RU'='Скрыть парные во время кулдауна'; 'sv-SE'='Dölj parkopplade under cooldown'; 'zh-CN'='冷却时隐藏配对'; 'zh-TW'='冷卻時隱藏配對'; 'th-TH'='ซ่อนที่จับคู่ระหว่างคูลดาวน์'; 'pl-PL'='Ukryj sparowane podczas cooldownu' }
  'Show Paired Only While On Cooldown' = @{ 'de-DE'='Gepaarte nur während Abklingzeit anzeigen'; 'es-ES'='Mostrar emparejadas solo durante el enfriamiento'; 'fr-FR'='Afficher les règles jumelées uniquement pendant le temps de recharge'; 'it-IT'='Mostra accoppiate solo durante il cooldown'; 'ja-JP'='クールダウン中のみペアを表示'; 'ko-KR'='쿨다운 중에만 페어링 표시'; 'pt-BR'='Mostrar emparelhadas somente durante o cooldown'; 'ru-RU'='Показывать парные только во время кулдауна'; 'sv-SE'='Visa parkopplade endast under cooldown'; 'zh-CN'='仅在冷却时显示配对'; 'zh-TW'='僅在冷卻時顯示配對'; 'th-TH'='แสดงที่จับคู่เฉพาะระหว่างคูลดาวน์'; 'pl-PL'='Pokaż sparowane tylko podczas cooldownu' }
  'Manage paired rules in the Full Editor.' = @{ 'de-DE'='Gepaarte Regeln im vollständigen Editor verwalten.'; 'es-ES'='Administra las reglas emparejadas en el editor completo.'; 'fr-FR'='Gérez les règles jumelées dans l''éditeur complet.'; 'it-IT'='Gestisci le regole accoppiate nell''editor completo.'; 'ja-JP'='ペアのルールはフルエディタで管理します。'; 'ko-KR'='페어링 규칙은 전체 편집기에서 관리하세요.'; 'pt-BR'='Gerencie as regras emparelhadas no editor completo.'; 'ru-RU'='Управляйте парными правилами в полном редакторе.'; 'sv-SE'='Hantera parkopplade regler i hela editorn.'; 'zh-CN'='请在完整编辑器中管理配对规则。'; 'zh-TW'='請在完整編輯器中管理配對規則。'; 'th-TH'='จัดการกฎที่จับคู่ในตัวแก้ไขแบบเต็ม'; 'pl-PL'='Zarządzaj sparowanymi regułami w pełnym edytorze.' }
  'Reward Colors' = @{ 'de-DE'='Belohnungsfarben'; 'es-ES'='Colores de la recompensa'; 'fr-FR'='Couleurs de la récompense'; 'it-IT'='Colori della ricompensa'; 'ja-JP'='リワードの色'; 'ko-KR'='보상 색상'; 'pt-BR'='Cores da recompensa'; 'ru-RU'='Цвета награды'; 'sv-SE'='Belöningsfärger'; 'zh-CN'='奖励颜色'; 'zh-TW'='獎勵顏色'; 'th-TH'='สีของรางวัล'; 'pl-PL'='Kolory nagród' }
  'Everyone' = @{ 'de-DE'='Alle'; 'es-ES'='Todos'; 'fr-FR'='Tout le monde'; 'it-IT'='Tutti'; 'ja-JP'='全員'; 'ko-KR'='모두'; 'pt-BR'='Todos'; 'ru-RU'='Все'; 'sv-SE'='Alla'; 'zh-CN'='所有人'; 'zh-TW'='所有人'; 'th-TH'='ทุกคน'; 'pl-PL'='Wszyscy' }
  'Moderators' = @{ 'de-DE'='Moderatoren'; 'es-ES'='Moderadores'; 'fr-FR'='Modérateurs'; 'it-IT'='Moderatori'; 'ja-JP'='モデレーター'; 'ko-KR'='중재자'; 'pt-BR'='Moderadores'; 'ru-RU'='Модераторы'; 'sv-SE'='Moderatorer'; 'zh-CN'='版主'; 'zh-TW'='版主'; 'th-TH'='ผู้ดูแล'; 'pl-PL'='Moderatorzy' }
  'Broadcaster' = @{ 'de-DE'='Streamer'; 'es-ES'='Streamer'; 'fr-FR'='Streamer'; 'it-IT'='Streamer'; 'ja-JP'='配信者'; 'ko-KR'='방송자'; 'pt-BR'='Streamer'; 'ru-RU'='Стример'; 'sv-SE'='Streamer'; 'zh-CN'='主播'; 'zh-TW'='主播'; 'th-TH'='ผู้ถ่ายทอดสด'; 'pl-PL'='Streamer' }
}

$root = 'E:\!!!Program to work on\Proper Crystal Relay\VrcTwitchOscBridge\Resources\Localization'
foreach ($culture in $map['Twitch Reward'].Keys) {
  $path = Join-Path $root "$culture.extra.json"
  $text = [System.IO.File]::ReadAllText($path).TrimEnd()
  $lastBrace = $text.LastIndexOf('}')
  if ($lastBrace -lt 0) { throw "No closing brace in $path" }
  $before = $text.Substring(0, $lastBrace).TrimEnd()
  if (-not $before.EndsWith(',')) { $before += ',' }
  $lines = @()
  foreach ($key in $map.Keys) {
    $value = $map[$key][$culture]
    if ($null -eq $value) { $value = $key }  # fallback to English
    $lines += "  `"$key`": `"$value`""
  }
  $newText = $before + "`n" + ($lines -join ",`n") + "`n}"
  [System.IO.File]::WriteAllText($path, $newText)
  Write-Host "Updated $culture"
}
```

- [ ] **Step 3: Build to confirm the new loc keys resolve**

Run: `dotnet build "E:\!!!Program to work on\Proper Crystal Relay\VrcTwitchOscBridge\VrcTwitchOscBridge.csproj" --no-restore`
Expected: Build succeeded, 0 errors.

- [ ] **Step 4: Run the localization audit and confirm no new failures**

Run: `dotnet run --project "E:\!!!Program to work on\Proper Crystal Relay\LocalizationAudit\LocalizationAudit.csproj" -- "E:\!!!Program to work on\Proper Crystal Relay\VrcTwitchOscBridge\Resources\Localization"`
Expected: The audit prints many pre-existing failures (out of scope per the spec). Confirm that **no new failure mentions any of the 15 new keys** — every `<lang> is missing` / `<lang> has empty value` line for the new keys should be absent. (The pre-existing `en-US is missing source string: Bits`, `Hardcoded XAML text should use localization: AvatarSwapManagerWindow.xaml:...`, etc. lines are expected and pre-existing — do not regress them.)

- [ ] **Step 5: Commit**

```bash
git add VrcTwitchOscBridge/Resources/Localization/*.extra.json tools/private/Add-AvatarSwapInlineEditorLoc.ps1
git commit -m "feat(avatar-swap): add localization keys for inline editor feature groups"
```

---

## Task 10: Add `TriggerRule` property tests + final verification

**Files:**
- Create: `VrcTwitchOscBridge.Tests/TriggerRuleRoundTripTests.cs`

- [ ] **Step 1: Create the test file with property-level tests**

Create `VrcTwitchOscBridge.Tests/TriggerRuleRoundTripTests.cs`:

```csharp
using VrcTwitchOscBridge.Models;
using VrcTwitchOscBridge.Services;
using Xunit;

namespace VrcTwitchOscBridge.Tests;

public sealed class TriggerRuleRoundTripTests
{
    [Fact]
    public void SpecialRulePairingMode_NormalizesOutOfRangeValue()
    {
        var rule = new TriggerRule();
        rule.SpecialRulePairingMode = (SpecialRulePairingMode)999;
        Assert.Equal(SpecialRulePairingMode.HidePairedWhileActive, rule.SpecialRulePairingMode);
    }

    [Fact]
    public void SharedRewardChoiceFields_RoundTripThroughPublicProperties()
    {
        var rule = new TriggerRule
        {
            SharedRewardChoiceEnabled = true,
            SharedRewardChoiceNumber = 3,
            SharedRewardHelpText = "Third option"
        };
        Assert.True(rule.SharedRewardChoiceEnabled);
        Assert.Equal(3, rule.SharedRewardChoiceNumber);
        Assert.Equal("Third option", rule.SharedRewardHelpText);
    }

    [Fact]
    public void DeleteManagedRewardWhenInactive_RoundTrips()
    {
        var rule = new TriggerRule { DeleteManagedRewardWhenInactive = true };
        Assert.True(rule.DeleteManagedRewardWhenInactive);
    }

    [Fact]
    public void BotMessageTemplate_RoundTrips()
    {
        var rule = new TriggerRule { BotMessageTemplate = "{user} did the thing" };
        Assert.Equal("{user} did the thing", rule.BotMessageTemplate);
    }

    [Fact]
    public void ChannelPointRewardDescription_RoundTrips()
    {
        var rule = new TriggerRule { ChannelPointRewardDescription = "Long description" };
        Assert.Equal("Long description", rule.ChannelPointRewardDescription);
    }

    [Fact]
    public void ManagedRewardColors_NormalizeAndRoundTrip()
    {
        var rule = new TriggerRule();
        rule.ManagedRewardReadyColor = "#22C55E";
        Assert.Equal("#22C55E", rule.ManagedRewardReadyColor, ignoreCase: true);
        rule.ManagedRewardCooldownColor = "#EF4444";
        Assert.Equal("#EF4444", rule.ManagedRewardCooldownColor, ignoreCase: true);
    }

    [Fact]
    public void ChatCommandPermission_RoundTrips()
    {
        var rule = new TriggerRule { ChatCommandPermission = ChatCommandPermission.Broadcaster };
        Assert.Equal(ChatCommandPermission.Broadcaster, rule.ChatCommandPermission);
    }

    [Fact]
    public void ManagedRewardReadyBrush_ReturnsTransparentForEmptyColor()
    {
        var rule = new TriggerRule();
        Assert.Equal(System.Windows.Media.Brushes.Transparent, rule.ManagedRewardReadyBrush);
    }
}
```

- [ ] **Step 2: Run the new tests**

Run: `dotnet test "E:\!!!Program to work on\Proper Crystal Relay\VrcTwitchOscBridge.Tests\VrcTwitchOscBridge.Tests.csproj" --filter "FullyQualifiedName~TriggerRuleRoundTripTests"`
Expected: All 8 tests pass, 0 failures.

- [ ] **Step 3: Run the full test suite to confirm no regressions**

Run: `dotnet test "E:\!!!Program to work on\Proper Crystal Relay\VrcTwitchOscBridge.Tests\VrcTwitchOscBridge.Tests.csproj"`
Expected: 0 failures across the whole suite. (The suite has many tests; some are pre-existing `[SKIP]` per the V4 migration tests — that is fine.)

- [ ] **Step 4: Smoke-launch the debug executable to confirm the window starts**

```powershell
$exe = 'E:\!!!Program to work on\Proper Crystal Relay\VrcTwitchOscBridge\bin\Debug\net10.0-windows\CrystalRelayTwitchOsc.exe'
$proc = Start-Process -FilePath $exe -PassThru
Start-Sleep -Seconds 6
if (-not $proc.HasExited) { Stop-Process -Id $proc.Id -Force; Write-Host 'Started and stopped cleanly' } else { throw "Process exited early with code $($proc.ExitCode)" }
```

Expected: `Started and stopped cleanly`.

- [ ] **Step 5: Commit**

```bash
git add VrcTwitchOscBridge.Tests/TriggerRuleRoundTripTests.cs
git commit -m "test(avatar-swap): add TriggerRule round-trip tests for inline editor fields"
```

---

## Self-Review

**1. Spec coverage:**
- ITwitchRewardSource slim interface → Task 1 ✓
- MainWindowVM implements it → Task 2 ✓
- AvatarSwapManagerVM forwards properties → Task 3 ✓
- Call site updated to pass `this` → Task 4 ✓
- 10 group sections in expanded panel → Tasks 5, 6, 7, 8 ✓
- Color picker reusing WinForms ColorDialog pattern → Task 8 ✓
- Localization for new keys → Task 9 ✓
- TriggerRule property/serialization tests → Task 10 ✓
- No model changes (property additions only, no schema change) → Task 8 (brush helpers are computed read-only props, no serialization impact) ✓
- Error handling (color dialog cancel, refresh failure, invalid numeric input, broadcaster not connected) → covered by code in Tasks 5–8 (no separate task; behavior is inherent in the implementation) ✓
- "Paired Rules list stays in Full Editor" → Task 6 (italic note inline) ✓
- "All 10 groups show for every trigger type" → enforced by not adding `Rule.TriggerType` data triggers on the new groups ✓

**2. Placeholder scan:** No TBD/TODO. All code blocks are complete. All commands are exact.

**3. Type consistency:** `ITwitchRewardSource.RewardOptions` → `ObservableCollection<TwitchRewardOption>` (matches `MainWindowViewModel.RewardOptions` at line 1053). `AvatarSwapManagerViewModel.TwitchRewardOptions` matches. `RefreshTwitchRewardsCommand` / `UnlinkTwitchRewardCommand` are `ICommand` (matches the existing `RelayCommand` properties on `MainWindowViewModel`). The `EnumToBoolConverter` is consumed via `x:Key` in Task 6 and defined in Task 6. The `FindRuleFromButton` helper in Task 8 returns `TriggerRule?` and is used by both click handlers. The two `ManagedReward*Brush` properties added in Task 8 are referenced by the XAML in Task 8 (same task — no cross-task drift).
