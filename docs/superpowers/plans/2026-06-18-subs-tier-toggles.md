# Subs Tier Toggles Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Remove the unused Chat keyword control from the Subs section and add explicit per-tier enable/disable toggles (T1/T2/T3) that skip the rule at runtime when a tier is disabled.

**Architecture:** Three new `bool` properties on `TriggerRule` (one per tier, default `true`). Runtime guard in `HandleTimedSupporterOverrideTriggerAsync` skips the rule early when the incoming sub's tier is disabled. UI replaces each tier's label with a CheckBox bound to `IsEnabled` on the seconds textbox. DTOs use C# property initializers (`= true`) for backward-compatible defaults.

**Tech Stack:** C# / WPF / .NET 10 / xUnit

---

## File Structure

| File | Responsibility |
|---|---|
| `VrcTwitchOscBridge/Models/TriggerRule.cs` | 3 new bool properties: `SubscriptionTier1Enabled`, `SubscriptionTier2Enabled`, `SubscriptionTier3Enabled` (default `true`) |
| `VrcTwitchOscBridge/Services/BridgeCoordinator.cs` | New `IsSubscriptionTierEnabled` helper near `GetSupporterOverrideSubscriptionSecondsPerSub`; early-return guard at the top of `HandleTimedSupporterOverrideTriggerAsync` |
| `VrcTwitchOscBridge/UserControls/InlineRuleEditorControl.xaml` | Remove "Chat keyword" label+textbox from Subs section; replace each tier's label with a CheckBox bound to `IsEnabled` on the textbox |
| `VrcTwitchOscBridge/Services/SettingsStore.cs` | Add 3 properties to `PersistedTriggerRule` DTO with `= true` initializer; add to both mapping blocks |
| `VrcTwitchOscBridge/Services/BridgeRuntimeConfiguration.cs` | Add 3 positional parameters to `TriggerRuleSnapshot` record with default values; add to mapping |
| `VrcTwitchOscBridge.Tests/TriggerRuleRoundTripTests.cs` | Default-state + round-trip tests for the 3 new properties |
| `VrcTwitchOscBridge.Tests/AvatarSwapManagerViewModelTests.cs` | Test that `AddSubsRuleCommand` produces a rule with all tier toggles `true` |

---

## Task 1: Add 3 new bool properties to TriggerRule

**Files:**
- Modify: `VrcTwitchOscBridge/Models/TriggerRule.cs` (add 3 backing fields after `subscriptionTier3SecondsPerSub` at around line 82, add 3 properties after `SubscriptionTier3SecondsPerSub` setter around line 528)
- Test: `VrcTwitchOscBridge.Tests/TriggerRuleRoundTripTests.cs`

- [ ] **Step 1: Write the failing tests**

Add to `TriggerRuleRoundTripTests.cs` (append at the end, before the closing `}`):

```csharp
[Fact]
public void SubscriptionTier1Enabled_DefaultsToTrue()
{
    var rule = new TriggerRule();
    Assert.True(rule.SubscriptionTier1Enabled);
}

[Fact]
public void SubscriptionTier2Enabled_DefaultsToTrue()
{
    var rule = new TriggerRule();
    Assert.True(rule.SubscriptionTier2Enabled);
}

[Fact]
public void SubscriptionTier3Enabled_DefaultsToTrue()
{
    var rule = new TriggerRule();
    Assert.True(rule.SubscriptionTier3Enabled);
}

[Fact]
public void SubscriptionTier1Enabled_RoundTrips()
{
    var rule = new TriggerRule { SubscriptionTier1Enabled = false };
    Assert.False(rule.SubscriptionTier1Enabled);
}

[Fact]
public void SubscriptionTier2Enabled_RoundTrips()
{
    var rule = new TriggerRule { SubscriptionTier2Enabled = false };
    Assert.False(rule.SubscriptionTier2Enabled);
}

[Fact]
public void SubscriptionTier3Enabled_RoundTrips()
{
    var rule = new TriggerRule { SubscriptionTier3Enabled = false };
    Assert.False(rule.SubscriptionTier3Enabled);
}
```

- [ ] **Step 2: Run the tests to verify they fail**

Run: `dotnet test "E:\!!!Program to work on\Proper Crystal Relay\VrcTwitchOscBridge.Tests\VrcTwitchOscBridge.Tests.csproj" --no-restore --filter "FullyQualifiedName~TriggerRuleRoundTripTests.SubscriptionTier"`

Expected: FAIL with `error CS1061: 'TriggerRule' does not contain a definition for 'SubscriptionTier1Enabled'` (or 2/3)

- [ ] **Step 3: Add the 3 backing fields**

In `TriggerRule.cs`, find the backing field block (around lines 56-127). Add the 3 new backing fields right after `subscriptionTier3SecondsPerSub` (line 82). The new fields are:

```csharp
private bool subscriptionTier1Enabled = true;
private bool subscriptionTier2Enabled = true;
private bool subscriptionTier3Enabled = true;
```

- [ ] **Step 4: Add the 3 properties**

In `TriggerRule.cs`, find the `SubscriptionTier3SecondsPerSub` setter (around line 528). Add the 3 new properties right after it. Each property follows the same shape as the existing `AmountScaledDurationEnabled` (which raises `TriggerSummary` on change):

```csharp
public bool SubscriptionTier1Enabled
{
    get => subscriptionTier1Enabled;
    set
    {
        if (SetProperty(ref subscriptionTier1Enabled, value))
        {
            RaisePropertyChanged(nameof(TriggerSummary));
        }
    }
}

public bool SubscriptionTier2Enabled
{
    get => subscriptionTier2Enabled;
    set
    {
        if (SetProperty(ref subscriptionTier2Enabled, value))
        {
            RaisePropertyChanged(nameof(TriggerSummary));
        }
    }
}

public bool SubscriptionTier3Enabled
{
    get => subscriptionTier3Enabled;
    set
    {
        if (SetProperty(ref subscriptionTier3Enabled, value))
        {
            RaisePropertyChanged(nameof(TriggerSummary));
        }
    }
}
```

- [ ] **Step 5: Run the tests to verify they pass**

Run: `dotnet test "E:\!!!Program to work on\Proper Crystal Relay\VrcTwitchOscBridge.Tests\VrcTwitchOscBridge.Tests.csproj" --no-restore --filter "FullyQualifiedName~TriggerRuleRoundTripTests.SubscriptionTier"`

Expected: PASS

- [ ] **Step 6: Run the full TriggerRuleRoundTripTests suite to check for regressions**

Run: `dotnet test "E:\!!!Program to work on\Proper Crystal Relay\VrcTwitchOscBridge.Tests\VrcTwitchOscBridge.Tests.csproj" --no-restore --filter "FullyQualifiedName~TriggerRuleRoundTripTests"`

Expected: PASS (all 22+ tests)

- [ ] **Step 7: Commit**

```bash
git -C "E:\!!!Program to work on\Proper Crystal Relay" add "VrcTwitchOscBridge/Models/TriggerRule.cs" "VrcTwitchOscBridge.Tests/TriggerRuleRoundTripTests.cs"
git -C "E:\!!!Program to work on\Proper Crystal Relay" commit -m "Add SubscriptionTier{1,2,3}Enabled properties to TriggerRule"
```

---

## Task 2: Add `IsSubscriptionTierEnabled` helper in BridgeCoordinator

**Files:**
- Modify: `VrcTwitchOscBridge/Services/BridgeCoordinator.cs` (add helper after `GetSupporterOverrideSubscriptionSecondsPerSub` at around line 6508)

- [ ] **Step 1: Read the current code around the helper location**

Read `BridgeCoordinator.cs` around line 6498-6508 to confirm the exact text of `GetSupporterOverrideSubscriptionSecondsPerSub`.

- [ ] **Step 2: Add the helper method**

Add the new helper immediately after the closing `}` of `GetSupporterOverrideSubscriptionSecondsPerSub` (around line 6508):

```csharp
private static bool IsSubscriptionTierEnabled(TriggerRuleSnapshot rule, string tier)
{
    return tier?.Trim() switch
    {
        "1000" => rule.SubscriptionTier1Enabled,
        "2000" => rule.SubscriptionTier2Enabled,
        "3000" => rule.SubscriptionTier3Enabled,
        _ => true
    };
}
```

- [ ] **Step 3: Build to verify**

Run: `dotnet build "E:\!!!Program to work on\Proper Crystal Relay\VrcTwitchOscBridge\VrcTwitchOscBridge.csproj" --no-restore`

Expected: Build succeeded, 0 errors (pre-existing warnings are OK)

- [ ] **Step 4: Commit**

```bash
git -C "E:\!!!Program to work on\Proper Crystal Relay" add "VrcTwitchOscBridge/Services/BridgeCoordinator.cs"
git -C "E:\!!!Program to work on\Proper Crystal Relay" commit -m "Add IsSubscriptionTierEnabled helper in BridgeCoordinator"
```

---

## Task 3: Add tier-enabled guard in HandleTimedSupporterOverrideTriggerAsync

**Files:**
- Modify: `VrcTwitchOscBridge/Services/BridgeCoordinator.cs:8429` (add guard after the float-add diagnostic check at around line 8445)

- [ ] **Step 1: Read the current method to confirm the insertion point**

Read `BridgeCoordinator.cs` around line 8429-8450 to confirm the exact text of `HandleTimedSupporterOverrideTriggerAsync` and the float-add diagnostic check.

- [ ] **Step 2: Add the guard**

Find the existing float-add diagnostic block:

```csharp
if (IsSupporterFloatAddRule(rule)
    && !TryResolveSupporterFloatAddAmount(rule, bridgeEvent, out supporterFloatAddAmount, out var supporterFloatAddDiagnostic))
{
    WriteLog(supporterFloatAddDiagnostic);
    return;
}
```

Add the tier-enabled guard immediately after its closing `}`:

```csharp
if (rule.TriggerType == TwitchTriggerType.Subscriptions
    && !IsSubscriptionTierEnabled(rule, bridgeEvent.SubscriptionTier))
{
    return;
}
```

- [ ] **Step 3: Build to verify**

Run: `dotnet build "E:\!!!Program to work on\Proper Crystal Relay\VrcTwitchOscBridge\VrcTwitchOscBridge.csproj" --no-restore`

Expected: Build succeeded, 0 errors

- [ ] **Step 4: Commit**

```bash
git -C "E:\!!!Program to work on\Proper Crystal Relay" add "VrcTwitchOscBridge/Services/BridgeCoordinator.cs"
git -C "E:\!!!Program to work on\Proper Crystal Relay" commit -m "Skip subs rules when the incoming sub's tier is disabled"
```

---

## Task 4: Update PersistedTriggerRule DTO in SettingsStore

**Files:**
- Modify: `VrcTwitchOscBridge/Services/SettingsStore.cs:3204-3208` (add 3 properties to DTO after `SubscriptionTier3SecondsPerSub`)
- Modify: `VrcTwitchOscBridge/Services/SettingsStore.cs:1033-1035` (add to rule→DTO mapping)
- Modify: `VrcTwitchOscBridge/Services/SettingsStore.cs:1277-1285` (add to DTO→rule mapping)

- [ ] **Step 1: Add the 3 properties to the DTO**

In `SettingsStore.cs`, find the `PersistedTriggerRule` class. Add the 3 new properties right after the `SubscriptionTier3SecondsPerSub` property (around line 3208). The current code at line 3208 is:

```csharp
public int SubscriptionTier3SecondsPerSub { get; set; }
```

Add immediately after it:

```csharp
public bool SubscriptionTier1Enabled { get; set; } = true;
public bool SubscriptionTier2Enabled { get; set; } = true;
public bool SubscriptionTier3Enabled { get; set; } = true;
```

The `= true` initializer is critical for backward compat — old saves where these fields are missing will deserialize to `true`, preserving current behavior.

- [ ] **Step 2: Add to the rule→DTO mapping**

In `SettingsStore.cs`, find the rule→DTO mapping block (around line 1033-1035). The current code is:

```csharp
SubscriptionTier1SecondsPerSub = rule.SubscriptionTier1SecondsPerSub,
SubscriptionTier2SecondsPerSub = rule.SubscriptionTier2SecondsPerSub,
SubscriptionTier3SecondsPerSub = rule.SubscriptionTier3SecondsPerSub,
```

Add immediately after it:

```csharp
SubscriptionTier1Enabled = rule.SubscriptionTier1Enabled,
SubscriptionTier2Enabled = rule.SubscriptionTier2Enabled,
SubscriptionTier3Enabled = rule.SubscriptionTier3Enabled,
```

- [ ] **Step 3: Add to the DTO→rule mapping**

In `SettingsStore.cs`, find the DTO→rule mapping block (around line 1277-1285). The current code is:

```csharp
SubscriptionTier1SecondsPerSub = rule.SubscriptionTier1SecondsPerSub <= 0
    ? 1
    : rule.SubscriptionTier1SecondsPerSub,
SubscriptionTier2SecondsPerSub = rule.SubscriptionTier2SecondsPerSub <= 0
    ? 1
    : rule.SubscriptionTier2SecondsPerSub,
SubscriptionTier3SecondsPerSub = rule.SubscriptionTier3SecondsPerSub <= 0
    ? 1
    : rule.SubscriptionTier3SecondsPerSub,
```

Add immediately after it:

```csharp
SubscriptionTier1Enabled = rule.SubscriptionTier1Enabled,
SubscriptionTier2Enabled = rule.SubscriptionTier2Enabled,
SubscriptionTier3Enabled = rule.SubscriptionTier3Enabled,
```

- [ ] **Step 4: Build to verify**

Run: `dotnet build "E:\!!!Program to work on\Proper Crystal Relay\VrcTwitchOscBridge\VrcTwitchOscBridge.csproj" --no-restore`

Expected: Build succeeded, 0 errors

- [ ] **Step 5: Commit**

```bash
git -C "E:\!!!Program to work on\Proper Crystal Relay" add "VrcTwitchOscBridge/Services/SettingsStore.cs"
git -C "E:\!!!Program to work on\Proper Crystal Relay" commit -m "Add SubscriptionTier{1,2,3}Enabled to PersistedTriggerRule DTO"
```

---

## Task 5: Update TriggerRuleSnapshot in BridgeRuntimeConfiguration

**Files:**
- Modify: `VrcTwitchOscBridge/Services/BridgeRuntimeConfiguration.cs:87-89` (add 3 positional parameters to the record)
- Modify: `VrcTwitchOscBridge/Services/BridgeRuntimeConfiguration.cs:933-935` (add to mapping)

- [ ] **Step 1: Read the record definition and mapping**

Read `BridgeRuntimeConfiguration.cs` around lines 87-89 (the `SubscriptionTier*SecondsPerSub` fields in the record) and around lines 933-935 (the mapping). Confirm the exact text.

- [ ] **Step 2: Add to the record**

In `BridgeRuntimeConfiguration.cs`, find the `TriggerRuleSnapshot` record. Add the 3 new positional parameters right after `int SubscriptionTier3SecondsPerSub,` (around line 89). The new parameters use default values for backward compat:

```csharp
int SubscriptionTier1SecondsPerSub,
int SubscriptionTier2SecondsPerSub,
int SubscriptionTier3SecondsPerSub,
bool SubscriptionTier1Enabled = true,
bool SubscriptionTier2Enabled = true,
bool SubscriptionTier3Enabled = true,
```

**IMPORTANT:** Default-valued parameters must come after non-default-valued parameters in C#. Check that the existing parameters after these (e.g. `int MaxAccumulatedDurationSeconds,`) don't already have defaults. If they do, this placement is fine. If not, the new parameters with defaults are placed in the middle — C# allows this but it's a code style concern. Verify the build succeeds.

- [ ] **Step 3: Add to the mapping**

In `BridgeRuntimeConfiguration.cs`, find the mapping that creates `TriggerRuleSnapshot` from `TriggerRule` (around line 933-935). Add the 3 new mapping lines right after the existing `SubscriptionTier3SecondsPerSub` line:

```csharp
rule.SubscriptionTier1SecondsPerSub,
rule.SubscriptionTier2SecondsPerSub,
rule.SubscriptionTier3SecondsPerSub,
rule.SubscriptionTier1Enabled,
rule.SubscriptionTier2Enabled,
rule.SubscriptionTier3Enabled,
```

- [ ] **Step 4: Build to verify**

Run: `dotnet build "E:\!!!Program to work on\Proper Crystal Relay\VrcTwitchOscBridge\VrcTwitchOscBridge.csproj" --no-restore`

Expected: Build succeeded, 0 errors

- [ ] **Step 5: Commit**

```bash
git -C "E:\!!!Program to work on\Proper Crystal Relay" add "VrcTwitchOscBridge/Services/BridgeRuntimeConfiguration.cs"
git -C "E:\!!!Program to work on\Proper Crystal Relay" commit -m "Add SubscriptionTier{1,2,3}Enabled to TriggerRuleSnapshot"
```

---

## Task 6: Update InlineRuleEditorControl.xaml — remove chat keyword and add tier checkboxes

**Files:**
- Modify: `VrcTwitchOscBridge/UserControls/InlineRuleEditorControl.xaml:212-249` (Subs section)

**⚠️ WORKING TREE WARNING:** The working tree may have uncommitted changes from other in-progress tasks. Use the backup-and-revert approach to avoid bundling unrelated changes.

- [ ] **Step 1: Check working tree state**

Run: `git -C "E:\!!!Program to work on\Proper Crystal Relay" status --short`

If the working tree has uncommitted changes to `InlineRuleEditorControl.xaml` or other files you don't want to commit, use the backup-and-revert approach (see end of task).

- [ ] **Step 2: Find the current Subs section**

Read `InlineRuleEditorControl.xaml` to find the current Subs section (the StackPanel with `DataTrigger` for `Subscriptions` and `GiftSubscription`). The current code is approximately at lines 212-249 and looks like:

```xml
<!-- Subs-specific fields -->
<StackPanel Margin="0,8,0,0">
    <StackPanel.Style>...</StackPanel.Style>
    <Border Background="{DynamicResource NestedPanelBrush}" CornerRadius="4" Padding="10" Margin="0,0,0,8">
        <StackPanel>
            <TextBlock Text="Subscription Settings" ... />
            <UniformGrid Columns="3">
                <StackPanel Margin="0,0,6,0">
                    <TextBlock Text="T1 seconds/sub" ... />
                    <TextBox Text="{Binding Rule.SubscriptionTier1SecondsPerSub, ...}" />
                </StackPanel>
                <StackPanel Margin="6,0,6,0">
                    <TextBlock Text="T2 seconds/sub" ... />
                    <TextBox Text="{Binding Rule.SubscriptionTier2SecondsPerSub, ...}" />
                </StackPanel>
                <StackPanel Margin="6,0,0,0">
                    <TextBlock Text="T3 seconds/sub" ... />
                    <TextBox Text="{Binding Rule.SubscriptionTier3SecondsPerSub, ...}" />
                </StackPanel>
            </UniformGrid>
            <CheckBox IsChecked="{Binding Rule.IsGiftSubscription}" Content="Include gift subs" Margin="0,8,0,0" />
            <TextBlock Text="Chat keyword" ... />
            <TextBox Text="{Binding Rule.SupporterKeywordText, ...}" />
        </StackPanel>
    </Border>
</StackPanel>
```

- [ ] **Step 3: Replace the UniformGrid (3 tiers) and remove Chat keyword**

Replace the three tier `StackPanel`s inside the `UniformGrid` and remove the Chat keyword label+textbox. The new structure:

```xml
<UniformGrid Columns="3">
    <StackPanel Margin="0,0,6,0">
        <CheckBox IsChecked="{Binding Rule.SubscriptionTier1Enabled}"
                  Content="T1 seconds/sub"
                  Foreground="{DynamicResource MutedBrush}"
                  FontSize="11" Margin="0,0,0,2" />
        <TextBox Text="{Binding Rule.SubscriptionTier1SecondsPerSub, UpdateSourceTrigger=PropertyChanged}"
                 IsEnabled="{Binding Rule.SubscriptionTier1Enabled}" />
    </StackPanel>
    <StackPanel Margin="6,0,6,0">
        <CheckBox IsChecked="{Binding Rule.SubscriptionTier2Enabled}"
                  Content="T2 seconds/sub"
                  Foreground="{DynamicResource MutedBrush}"
                  FontSize="11" Margin="0,0,0,2" />
        <TextBox Text="{Binding Rule.SubscriptionTier2SecondsPerSub, UpdateSourceTrigger=PropertyChanged}"
                 IsEnabled="{Binding Rule.SubscriptionTier2Enabled}" />
    </StackPanel>
    <StackPanel Margin="6,0,0,0">
        <CheckBox IsChecked="{Binding Rule.SubscriptionTier3Enabled}"
                  Content="T3 seconds/sub"
                  Foreground="{DynamicResource MutedBrush}"
                  FontSize="11" Margin="0,0,0,2" />
        <TextBox Text="{Binding Rule.SubscriptionTier3SecondsPerSub, UpdateSourceTrigger=PropertyChanged}"
                 IsEnabled="{Binding Rule.SubscriptionTier3Enabled}" />
    </StackPanel>
</UniformGrid>
<CheckBox IsChecked="{Binding Rule.IsGiftSubscription}" Content="Include gift subs" Margin="0,8,0,0" />
```

Note: the `<TextBlock Text="Chat keyword" ... />` and `<TextBox Text="{Binding Rule.SupporterKeywordText, ...}" />` lines are **removed** entirely.

- [ ] **Step 4: Build to verify**

Run: `dotnet build "E:\!!!Program to work on\Proper Crystal Relay\VrcTwitchOscBridge\VrcTwitchOscBridge.csproj" --no-restore`

Expected: Build succeeded, 0 errors

- [ ] **Step 5: Commit — using backup-and-revert if working tree is dirty**

If the working tree was clean (no uncommitted changes before this task):
```bash
git -C "E:\!!!Program to work on\Proper Crystal Relay" add "VrcTwitchOscBridge/UserControls/InlineRuleEditorControl.xaml"
git -C "E:\!!!Program to work on\Proper Crystal Relay" commit -m "Remove Chat keyword from Subs, add per-tier enable toggles"
```

If the working tree had uncommitted changes, use the backup-and-revert approach:
1. Save current file to `C:\Users\screm\AppData\Local\Temp\opencode\inline_full.xaml`
2. `git -C "E:\!!!Program to work on\Proper Crystal Relay" restore --staged .` (if anything staged)
3. `git -C "E:\!!!Program to work on\Proper Crystal Relay" checkout HEAD -- "VrcTwitchOscBridge/UserControls/InlineRuleEditorControl.xaml"` (restore to HEAD)
4. Apply ONLY the Task 6 edit using `Edit`
5. `git -C "E:\!!!Program to work on\Proper Crystal Relay" add "VrcTwitchOscBridge/UserControls/InlineRuleEditorControl.xaml"`
6. Verify the staged diff is small: `git -C "E:\!!!Program to work on\Proper Crystal Relay" diff --staged -- "VrcTwitchOscBridge/UserControls/InlineRuleEditorControl.xaml"` (should show ~12 lines changed: 3 CheckBoxes added, 3 TextBlocks removed, 3 IsEnabled bindings added, Chat keyword lines removed)
7. Commit: `git -C "E:\!!!Program to work on\Proper Crystal Relay" commit -m "Remove Chat keyword from Subs, add per-tier enable toggles"`
8. Restore the backup to leave the working tree in its original state

---

## Task 7: Add default-state test for AddSubsRuleCommand

**Files:**
- Modify: `VrcTwitchOscBridge.Tests/AvatarSwapManagerViewModelTests.cs`

- [ ] **Step 1: Add the test**

Add to `AvatarSwapManagerViewModelTests.cs` (after the existing `AddBitsRuleCommand_NewRuleHasBitsKeywordEnabledFalse` test, which is at around line 344):

```csharp
[Fact]
public void AddSubsRuleCommand_NewRuleHasAllTierTogglesEnabled()
{
    var settings = new AppSettings();
    var profile = new AvatarSwapProfile { TargetAvatarId = "avtr_a", TargetAvatarName = "Avatar A" };
    settings.AvatarSwapProfiles.Add(profile);

    var vm = new AvatarSwapManagerViewModel(settings, new StubTwitchRewardSource());
    vm.OpenSwapEditorCommand.Execute(vm.SwapCards.Single());

    vm.AddSubsRuleCommand.Execute(null);

    var rule = profile.SubsRules.Single();
    Assert.True(rule.SubscriptionTier1Enabled);
    Assert.True(rule.SubscriptionTier2Enabled);
    Assert.True(rule.SubscriptionTier3Enabled);
}
```

- [ ] **Step 2: Run the test to verify it passes**

Run: `dotnet test "E:\!!!Program to work on\Proper Crystal Relay\VrcTwitchOscBridge.Tests\VrcTwitchOscBridge.Tests.csproj" --no-restore --filter "FullyQualifiedName~AvatarSwapManagerViewModelTests.AddSubsRuleCommand_NewRuleHasAllTierTogglesEnabled"`

Expected: PASS

- [ ] **Step 3: Run the full test suite to check for regressions**

Run: `dotnet test "E:\!!!Program to work on\Proper Crystal Relay\VrcTwitchOscBridge.Tests\VrcTwitchOscBridge.Tests.csproj" --no-restore`

Expected: PASS — 154+ tests passed, 0 failed (some skipped is fine)

- [ ] **Step 4: Commit**

```bash
git -C "E:\!!!Program to work on\Proper Crystal Relay" add "VrcTwitchOscBridge.Tests/AvatarSwapManagerViewModelTests.cs"
git -C "E:\!!!Program to work on\Proper Crystal Relay" commit -m "Test new subs rules default to all tier toggles enabled"
```

---

## Task 8: Final build and full test verification

**Files:** (no changes — verification only)

- [ ] **Step 1: Build the main project**

Run: `dotnet build "E:\!!!Program to work on\Proper Crystal Relay\VrcTwitchOscBridge\VrcTwitchOscBridge.csproj" --no-restore`

Expected: Build succeeded, 0 errors (pre-existing warnings about nullability in unrelated files are OK)

- [ ] **Step 2: Run the full test suite**

Run: `dotnet test "E:\!!!Program to work on\Proper Crystal Relay\VrcTwitchOscBridge.Tests\VrcTwitchOscBridge.Tests.csproj" --no-restore`

Expected: PASS — 154+ tests passed, 0 failed (some skipped is fine)

- [ ] **Step 3: Report the commit history**

Run: `git -C "E:\!!!Program to work on\Proper Crystal Relay" log --oneline 7fcc29b..HEAD`

Expected: 7 commits, one per task, each with a clear message.

- [ ] **Step 4: Manual smoke test (optional but recommended)**

Launch the debug build:
```
"E:\!!!Program to work on\Proper Crystal Relay\Launch-Crystal-Relay-Debug.bat"
```

Open the Avatar Swap manager, click any avatar, click a Subs trigger row, verify:
- The "Chat keyword" textbox is no longer present in the Subs section
- Each tier (T1, T2, T3) has a CheckBox next to its label
- Unchecking a tier greys out its seconds textbox
- The seconds value is preserved when re-checked

Runtime skip behavior can't be easily tested without a live Twitch connection, so it's verified by code review only.

---

## Self-Review

**Spec coverage:**
- [x] 3 new bool properties on TriggerRule — Task 1
- [x] `IsSubscriptionTierEnabled` helper — Task 2
- [x] Tier-enabled guard in `HandleTimedSupporterOverrideTriggerAsync` — Task 3
- [x] `PersistedTriggerRule` DTO with `= true` initializer — Task 4
- [x] `TriggerRuleSnapshot` record with default values — Task 5
- [x] UI: remove Chat keyword, add 3 tier CheckBoxes + IsEnabled bindings — Task 6
- [x] Default-state tests — Tasks 1, 7
- [x] Round-trip tests — Task 1

**Placeholder scan:** No TBD/TODO/placeholder patterns found. All code blocks are complete.

**Type consistency:** `SubscriptionTier1Enabled`, `SubscriptionTier2Enabled`, `SubscriptionTier3Enabled` are used consistently across all tasks. The DTO field names match the model property names. The `IsSubscriptionTierEnabled` helper takes a `TriggerRuleSnapshot` (not `TriggerRule`) because it's called from `HandleTimedSupporterOverrideTriggerAsync` which receives a snapshot.
