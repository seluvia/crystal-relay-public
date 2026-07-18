# Remove Power Up From Viewer Support — Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task.

**Goal:** Remove the old Power Up system from the Redeem Library's Viewer Support section and its associated MainWindow code.

**Architecture:** Pure removal — no new code. Delete the "Power Up" navigation button from the Viewer Support sidebar, the Power Up workspace content panels, all ViewModel commands/properties/methods, and dependent UI in AvatarSwapManager and AvatarScalingManager.

**Tech Stack:** C#, WPF/XAML, .NET 10

## Global Constraints

- Run `dotnet build` after all changes to verify compilation
- Do not remove `PowerUpRule` model class or `Settings.PowerUpRules` collection (safe deserialization)
- Do not remove `DiscountManagedPowerUpsEnabled` from model/settings (safe deserialization)
- Follow existing code patterns — pure removal, no refactoring

---

### Task 1: MainWindow.xaml — Remove Power Up navigation button + action buttons + ListBox + empty state

**Files:**
- Modify: `E:\!!!Program to work on\Proper Crystal Relay\VrcTwitchOscBridge\MainWindow.xaml` (lines 3549-3563, 3609-3658, 3709-3749, 3768-3775, 5877-5879)

- [ ] **Remove the "Power Up" navigation button** (lines 3549-3563):
  Delete the `<Button Content="{loc:Translate 'Power Up'}" ...>` block and its entire `<Button.Style>` including the DataTrigger.

- [ ] **Remove the Power Up action buttons panel** (lines 3609-3658):
  Delete the entire `<StackPanel>` with `IsViewingPowerUps` DataTrigger.

- [ ] **Remove the PowerUpRules ListBox** (lines 3709-3749):
  Delete the entire `<ListBox ItemsSource="{Binding PowerUpRules}" ...>` block including ItemTemplate.

- [ ] **Remove the Power Up empty-state trigger** (lines 3768-3775):
  Delete the second `<MultiDataTrigger>` condition block that checks `IsViewingPowerUps` and `Settings.PowerUpRules.Count`.

- [ ] **Remove the empty-state IsViewingPowerUps trigger** (lines 5877-5879):
  Delete `<DataTrigger Binding="{Binding IsViewingPowerUps}" Value="True">` block.

### Task 2: MainWindow.xaml — Remove Power Up Setup content editors

**Files:**
- Modify: `E:\!!!Program to work on\Proper Crystal Relay\VrcTwitchOscBridge\MainWindow.xaml` (lines 4264-4548, 4549-4563, 4587-4589)

- [ ] **Remove the Power Up Setup ContentControl** (lines 4264-4548):
  Delete the `<ContentControl Content="{Binding SelectedPowerUpRule}" ...>` block and its full ContentTemplate.

- [ ] **Remove the Scale Redeem ContentControl with IsViewingPowerUps trigger** (lines 4549-4563):
  Delete the `<ContentControl Content="{Binding SelectedAvatarScaleRule}" ...>` block that uses `IsViewingPowerUps` visibility DataTrigger.

- [ ] **Remove the Scale Redeem Setup IsViewingPowerUps hide trigger** (lines 4587-4589):
  Delete the `<DataTrigger Binding="{Binding DataContext.IsViewingPowerUps ...}" Value="True">` block.

### Task 3: MainWindow.xaml — Remove Discount managed Power Ups from Reward Fire Sale

**Files:**
- Modify: `E:\!!!Program to work on\Proper Crystal Relay\VrcTwitchOscBridge\MainWindow.xaml` (lines 4009-4018)

- [ ] **Remove the Discount managed Power Ups checkbox + info text** (lines 4009-4018):
  Delete the `<CheckBox>` for `DiscountManagedPowerUpsEnabled` and its `<TextBlock>` info text below.

### Task 4: MainWindowViewModel.cs — Remove Power Up enum value, IsViewingPowerUps, and all Power Up commands/properties

**Files:**
- Modify: `E:\!!!Program to work on\Proper Crystal Relay\VrcTwitchOscBridge\ViewModels\MainWindowViewModel.cs`

- [ ] **Remove `PowerUps` from `RuleListView` enum** (line 20989)

- [ ] **Remove `IsViewingPowerUps` property** (line 1430) and all references to it throughout the file (lines 1694, 1706, 1720, 1732, 1744, 1756, 1768, 2047, 2395, 4434-4435, 4438-4439, 4453, 4464, 4479, 4486, 4490, 4496-4497, 4567, 4569, 4571, 4574, 4576)

- [ ] **Remove `ShowPowerUpsCommand`** (line 916/3058) and `ShowPowerUps()` method (lines 5251-5256)

- [ ] **Remove all Power Up commands**: `AddPowerUpRuleCommand`, `AddAvatarScalingPowerUpRuleCommand`, `RemoveSelectedPowerUpRuleCommand`, `EnableAllPowerUpRulesCommand`, `DisableAllPowerUpRulesCommand`, `DeleteAllPowerUpRulesCommand`, `TestSelectedPowerUpRuleCommand`, `UnlinkPowerUpCommand`, `UseCurrentAvatarForPowerUpRuleCommand` (lines 963-973, 3132-3148)

- [ ] **Remove `SelectedPowerUpRule`** property + backing field (lines 440, 558, 2387-2407), `PowerUpRuleStatusText` (line 1930), `PowerUpActionEditorHelpText` (line 1954), `PowerUpRules` (line 2424)

- [ ] **Remove `PowerUpOptions`** (line 1068), `PowerUpSourceModeOptions` (line 1070), `PowerUpActionKindOptions` (lines 1076-1079)

- [ ] **Remove `GetRememberedPowerUpRule()` method** (line 7618)

- [ ] **Remove `QueuePowerUpRefreshAsync()` method** (line 10854)

- [ ] **Remove `DeletePowerUpRuleByCard()` method** (line 7039)

- [ ] **Remove `CreateDefaultPowerUpRule()` method** (line 7122)

- [ ] **Remove call to `QueuePowerUpRefreshAsync()`** from initialization (line 3275) and from `OpenAvatarSwapManager` (line 5352)

- [ ] **Remove Power Up avatar resolution code** from avatar name resolution methods (lines 4760-4773, 4990-4998)

- [ ] **Remove `ITwitchRewardSource.PowerUpOptions` implementation** (line 2978)

- [ ] **Remove Power Up avatar logic from `QueueAvatarOptionsUpdateAsync`** (lines 4434-4576 sections related to Power Up)

- [ ] **Remove `CommandParameter="PowerUp"` handling** in avatar picker response (lines 6403, 6429-6433)

### Task 5: AvatarSwapManagerWindow.xaml — Remove Power Up library navigation button

**Files:**
- Modify: `E:\!!!Program to work on\Proper Crystal Relay\VrcTwitchOscBridge\AvatarSwapManagerWindow.xaml` (line 538)

- [ ] **Remove "Power Up" library button**: Delete `<Button Content="{loc:Translate 'Power Up'}" Click="OnOpenPowerUpLibraryClicked" ... />`

### Task 6: AvatarScalingManagerWindow.xaml — Remove Power Ups section

**Files:**
- Modify: `E:\!!!Program to work on\Proper Crystal Relay\VrcTwitchOscBridge\AvatarScalingManagerWindow.xaml` (lines 846, 849, 1096-1125, 2031-2070+)

- [ ] **Remove "Power Ups" radio button** from navigation panel (lines 846-849)

- [ ] **Remove Power Ups content panel** (lines 1096-1125): the "Add Power Up" button, "Power Ups" label, and PowerUpCards ItemsControl

- [ ] **Remove Power Up Scaling editor ContentControl** (lines 2031-2070+)

### Task 7: AvatarScalingManagerViewModel.cs — Remove Power Up references

**Files:**
- Modify: `E:\!!!Program to work on\Proper Crystal Relay\VrcTwitchOscBridge\ViewModels\AvatarScalingManagerViewModel.cs`

- [ ] **Remove `PowerUps` from `ActiveSourceView` enum** (line 16)

- [ ] **Remove `observedPowerUpRules` field** (line 49), `PowerUpCards` (line 87), `SelectedPowerUpRule` (line 116), `AddAvatarScalingPowerUpRuleCommand` reference (line 201)

- [ ] **Remove Power Up collection wiring**: `WirePowerUpRule` (line 503), `UnwirePowerUpRule` (line 511), `OnPowerUpRulesChanged` (line 624), `ReconcileObservedPowerUpRules` (line 677), `OnPowerUpRulePropertyChanged` (line 730), subscription in constructor (line 413), unsubscribe in cleanup (line 827, 843)

- [ ] **Remove `PowerUpCards.Clear()`** from refresh logic (line 253)

- [ ] **Remove Power Up card creation** from refresh loop (lines 294-297)

- [ ] **Remove `AvatarScalingSourceKind.PowerUp` case** (line 360)

### Task 8: AvatarSwapManagerViewModel.cs — Remove Power Up references

**Files:**
- Modify: `E:\!!!Program to work on\Proper Crystal Relay\VrcTwitchOscBridge\ViewModels\AvatarSwapManagerViewModel.cs`

- [ ] **Remove `PowerUpOptions`** (line 35), `AddPowerUpRuleCommand` (line 55/311), `PowerUpRows` (line 140)

- [ ] **Remove Power Up row creation** from LoadProfile (lines 380-384), AddPowerUpRule method (lines 632-646)

- [ ] **Remove `PowerUpRows.Clear()`** from refresh (line 345)

- [ ] **Remove `DeletePowerUpRuleByCard` handling** in delete logic (lines 838-841)

- [ ] **Remove `AddPowerUpRuleCommand.NotifyCanExecuteChanged()`** (line 509)

### Task 9: Verify build

- [ ] **Build the project**:
  Run `dotnet build "E:\!!!Program to work on\Proper Crystal Relay\VrcTwitchOscBridge\VrcTwitchOscBridge.csproj" --no-restore`
  Fix any compilation errors.

- [ ] **Build the test project**: 
  Run `dotnet build "E:\!!!Program to work on\Proper Crystal Relay\VrcTwitchOscBridge.Tests\VrcTwitchOscBridge.Tests.csproj" --no-restore`
  Fix any compilation errors.
