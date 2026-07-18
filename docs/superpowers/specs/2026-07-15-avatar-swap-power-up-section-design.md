# Avatar Swap Power Up Section Design

## Problem
The Avatar Swap window has no dedicated Power Up trigger section. Clicking "Power-up" in the Advanced Triggers area creates a `TriggerRule` with `TriggerType.PowerUp` that lands in `ChannelPointRules` — a dead rule that can never match incoming Power Up redemptions.

## Solution
Add a full Power Up section to the Avatar Swap editor, mirroring the existing Bits section pattern, with runtime matching by `PowerUpId`.

## Priority Order
Cash > Power Up > Bits/Subs > Channel Points

Power Up in avatar swap uses `isGlobalOverride: true` (like Bits/Subs) — paid priority that fires regardless of current avatar.

## Changes

### 1. Model — `AvatarSwapProfile.cs`
- Add `ObservableCollection<TriggerRule> PowerUpRules { get; }`
- Add `bool UsesPowerUpRules => PowerUpRules.Count > 0`
- Update `HasRules`, `AvatarSubtitle`, `Bump()` to include Power Up rules

### 2. Serialization — `SettingsStore.cs`
- Add `List<PersistedTriggerRule>? PowerUpRules` to `PersistedAvatarSwapProfile`
- Serialize/deserialize in `ToPersistedAvatarSwapProfile()` / `ToAvatarSwapProfile()`

### 3. TriggerRuleSnapshot — `BridgeRuntimeConfiguration.cs`
- Add `string PowerUpId` field to `TriggerRuleSnapshot`
- Add `PowerUpId: rule.PowerUpId ?? string.Empty` to `FromRule()`

### 4. AvatarSwapProfileSnapshot — `BridgeRuntimeConfiguration.cs`
- Add `IReadOnlyList<TriggerRuleSnapshot> PowerUpRules` field

### 5. Snapshot conversion — `BridgeRuntimeConfiguration.FromSettings()`
- Snapshot `profile.PowerUpRules` with `isGlobalOverride: true` (like Bits/Subs)
- Add snapshotted rules to the main rule index

### 6. Runtime matching — `BridgeCoordinator.cs`
- In `HandlePowerUpEventAsync()`, after matching top-level `PowerUpRuleSnapshot` objects, also match indexed global override rules with `TriggerType.PowerUp`
- Matching by `PowerUpId` (same as top-level rules)
- Use `FindAvatarSwapProfileForRule()` to resolve the target avatar for matched swap profile rules
- Match priority: top-level Power Up rules first, then avatar swap Power Up rules

### 7. ViewModel — `AvatarSwapManagerViewModel.cs`
- Add `ObservableCollection<InlinePowerUpRuleRowViewModel> PowerUpRows`
- Add `RelayCommand AddPowerUpRuleCommand` — creates `TriggerRule` with `TriggerType = PowerUp`, auto-sets action to avatar change targeting the swap profile
- Add handling in `RebuildRows()` — iterate `Profile.PowerUpRules`
- Add handling in `DeleteRule()` — remove from `PowerUpRules`
- Wire command in constructor

### 8. Inline row control — New files
- `InlinePowerUpRuleRowViewModel.cs` — mirrors `InlineBitsRuleRowViewModel`, shows ⚡ prefix and Power Up title
- `InlinePowerUpRuleRowControl.xaml` / `.xaml.cs` — mirrors `InlineBitsRuleRowControl.xaml`, minimal inline card

### 9. XAML — `AvatarSwapManagerWindow.xaml`
- Add "⚡ Power Up" section in the swap editor (above Bits): section header + ItemsControl + "+ Add Power Up" button
- Add `DataTemplate` for `InlinePowerUpRuleRowViewModel` → `InlineRuleEditorControl`
- Remove the old "Power-up" button from Advanced Triggers (it was dead code)

### 10. Project file — `VrcTwitchOscBridge.csproj`
- Add new `.xaml` files as `<Page>` entries

## Non-Changes
- Roulette's "Power Up" button — stays as-is, navigates to main Power Up tab
- Top-level `PowerUpRule` system — unchanged, still works alongside avatar swap rules
- Main Power Up tab — unchanged
