# Bits + Subs: Always Add to Active Time Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Change the Bits and Subs override "amount-scaled" timer math so that every matching trigger extends the running timer by `Active Time + scaled amount` (instead of only adding the base `Active Time` on the very first trigger).

**Architecture:** Isolate the per-event add math into a new `SupportOverrideDurationMath` helper class so it can be unit-tested. Replace the body of `GetSupporterOverrideDuration` in `BridgeCoordinator.cs` to always compose `max(0, DurationSeconds) + scaled`. Drop the `includeStartingDuration` parameter from the call site. Update the master toggle label, the help text, the changelog, the release record, and AGENTS.md housekeeping. All other behavior (cap, queued override, cooldown, per-action reset) stays untouched.

**Tech Stack:** C# .NET 10, WPF, xUnit, FluentAssertions, JSON localization files.

**Working directory:** `E:\!!!Program to work on\Proper Crystal Relay`

**Build/test commands:**
- Build app: `dotnet build "E:\!!!Program to work on\Proper Crystal Relay\VrcTwitchOscBridge\VrcTwitchOscBridge.csproj" --no-restore`
- Build + run tests: `dotnet test "E:\!!!Program to work on\Proper Crystal Relay\VrcTwitchOscBridge.Tests\VrcTwitchOscBridge.Tests.csproj"`

---

## File structure

**New files:**
- `VrcTwitchOscBridge/Services/Support/SupportOverrideDurationMath.cs` - public static class with `ComputePerEventAddSeconds(TriggerRuleSnapshot rule, int amount, string subscriptionTier)`.
- `VrcTwitchOscBridge.Tests/SupportOverrideDurationMathTests.cs` - xUnit tests for the helper.
- `VrcTwitchOscBridge.Tests/TestTriggerRuleSnapshotBuilder.cs` - small helper to build `TriggerRuleSnapshot` instances with sensible defaults so tests can `with`-override only the fields they need.

**Modified files:**
- `VrcTwitchOscBridge/Services/BridgeCoordinator.cs` - `GetSupporterOverrideDuration` body change; `HandleTimedSupporterOverrideTriggerAsync` call site loses `includeStartingDuration`, `hasSameRuleActive`, `hasSameRuleQueued`.
- `VrcTwitchOscBridge/Models/TriggerRule.cs` - `DurationHelpText` `UsesAmountScaledDuration` branch text.
- `VrcTwitchOscBridge/SupporterOverrideTimeSettingsWindow.xaml` - master toggle label.
- `VrcTwitchOscBridge/Resources/Localization/en-US.json` - "Scale active time by amount" key value.
- `VrcTwitchOscBridge/Resources/Localization/en-US.extra.json` - new help text key + remove the now-unused old key.
- 14 non-English `*.json` files in `VrcTwitchOscBridge/Resources/Localization/` - matching label update.
- 14 non-English `*.extra.json` files in `VrcTwitchOscBridge/Resources/Localization/` - new help text key + remove old key.
- `CHANGELOG.txt` - 2 bullets added to `v3.1.9 beta 4` section.
- `RELEASE-CHANGE-RECORD.txt` - new `v3.1.9 beta 4 (in progress)` section; baseline and pending draft headers updated.
- `AGENTS.md` - "Project Identity" block housekeeping.

---

## Task 1: Add the test snapshot builder

**Files:**
- Create: `VrcTwitchOscBridge.Tests/TestTriggerRuleSnapshotBuilder.cs`

- [ ] **Step 1: Write the builder**

Create `VrcTwitchOscBridge.Tests/TestTriggerRuleSnapshotBuilder.cs` with a static helper that returns a `TriggerRuleSnapshot` with safe defaults so each test only has to override the few fields it cares about. The helper uses C# `with` expressions on a single shared default snapshot to avoid listing all 70+ record parameters in every test.

```csharp
using System.Collections.Generic;
using VrcTwitchOscBridge.Models;
using VrcTwitchOscBridge.Services;

namespace VrcTwitchOscBridge.Tests;

internal static class TestTriggerRuleSnapshotBuilder
{
    private static readonly TriggerRuleSnapshot Default = new(
        Id: System.Guid.NewGuid(),
        IsEnabled: true,
        Name: "TestRule",
        IsGlobalOverride: true,
        AvatarProfileId: System.Guid.Empty,
        AvatarProfileName: string.Empty,
        RequiredAvatarId: string.Empty,
        RequiredAvatarName: string.Empty,
        SupporterAvatarProfileId: System.Guid.Empty,
        SupporterAvatarId: string.Empty,
        SupporterAvatarName: string.Empty,
        BelongsToMasterAvatarProfile: false,
        TriggerType: TwitchTriggerType.Bits,
        ChannelPointRewardId: string.Empty,
        ChannelPointRewardTitle: string.Empty,
        ManagedRewardReadyColor: string.Empty,
        ManagedRewardCooldownColor: string.Empty,
        ChatCommandEnabled: false,
        ChatCommandText: string.Empty,
        ChatCommandPermission: ChatCommandPermission.Everyone,
        MinimumAmount: 1,
        AmountScaledDurationEnabled: false,
        AmountUnitsPerDuration: 1,
        SecondsPerAmountUnit: 1,
        BitsAmountUnitsPerDuration: 50,
        BitsSecondsPerAmountUnit: 1,
        SubscriptionsAmountUnitsPerDuration: 1,
        SubscriptionsSecondsPerAmountUnit: 1,
        SubscriptionTier1SecondsPerSub: 0,
        SubscriptionTier2SecondsPerSub: 0,
        SubscriptionTier3SecondsPerSub: 0,
        MaxAccumulatedDurationEnabled: false,
        MaxAccumulatedDurationSeconds: 0,
        ActionType: OscActionType.Osc,
        PlayerMovementDirection: PlayerMovementDirection.Forward,
        ParameterName: string.Empty,
        ParameterType: OscParameterType.Float,
        IntZeroDurationMode: IntZeroDurationMode.Instant,
        ParameterValue: string.Empty,
        FloatValueMode: FloatValueMode.Absolute,
        FloatTransitionSeconds: 0,
        ResetValue: string.Empty,
        ActiveFloatBoostRewardEnabled: false,
        ActiveFloatBoostRewardId: string.Empty,
        ActiveFloatBoostRewardTitle: string.Empty,
        ActiveFloatBoostRewardDescription: string.Empty,
        ActiveFloatBoostRewardCost: 0,
        ActiveFloatBoostRewardCooldownSeconds: 0,
        ActiveFloatBoostRewardReadyColor: string.Empty,
        ActiveFloatBoostRewardCooldownColor: string.Empty,
        ActiveFloatBoostAddValue: string.Empty,
        ActiveFloatBoostMinimumValue: string.Empty,
        ActiveFloatBoostMaximumValue: string.Empty,
        SupporterFloatAddEnabled: false,
        SupporterFloatAddMinimumValue: string.Empty,
        SupporterFloatAddMaximumValue: string.Empty,
        SupporterFloatAddRanges: new List<SupporterFloatAddRangeSnapshot>(),
        AvatarChangeTargetId: string.Empty,
        AvatarChangeResetId: string.Empty,
        AvatarTargetName: string.Empty,
        ResetAvatarName: string.Empty,
        AvatarRouletAvatarIds: new List<string>(),
        AvatarRouletAvatarNames: new List<string>(),
        RangeMinimum: 0,
        RangeMaximum: 0,
        DurationSeconds: 0,
        CooldownSeconds: 0,
        UsesLinkedChannelPointReward: false,
        BotMessageCooldownSeconds: null,
        SharedRewardChoiceEnabled: false,
        SharedRewardChoiceNumber: 0,
        SharedRewardHelpText: string.Empty,
        UsesSharedNumberedOutfitReward: false,
        PostOutfitChoiceListToTwitchChat: false,
        SetTriggerRestoreMode: SetTriggerRestoreMode.Snapshot,
        SupporterKeywordText: string.Empty,
        BitsKeywordEnabled: false,
        SetTriggerActions: new List<SetTriggerActionSnapshot>(),
        SpecialRulePairingMode: SpecialRulePairingMode.None,
        TemporarilyDisabledRuleIds: new List<System.Guid>(),
        BotMessageTemplate: string.Empty,
        Rule: null!);

    public static TriggerRuleSnapshot Build(
        TwitchTriggerType triggerType = TwitchTriggerType.Bits,
        bool amountScaledDurationEnabled = false,
        double durationSeconds = 0,
        int bitsAmountUnitsPerDuration = 50,
        int bitsSecondsPerAmountUnit = 1,
        int subscriptionTier1SecondsPerSub = 0,
        int subscriptionTier2SecondsPerSub = 0,
        int subscriptionTier3SecondsPerSub = 0)
    {
        return Default with
        {
            TriggerType = triggerType,
            AmountScaledDurationEnabled = amountScaledDurationEnabled,
            DurationSeconds = durationSeconds,
            BitsAmountUnitsPerDuration = bitsAmountUnitsPerDuration,
            BitsSecondsPerAmountUnit = bitsSecondsPerAmountUnit,
            SubscriptionTier1SecondsPerSub = subscriptionTier1SecondsPerSub,
            SubscriptionTier2SecondsPerSub = subscriptionTier2SecondsPerSub,
            SubscriptionTier3SecondsPerSub = subscriptionTier3SecondsPerSub
        };
    }
}
```

If any of the type names above do not match the actual `TriggerRuleSnapshot` parameter list (e.g. `ChatCommandPermission`, `OscActionType`, `PlayerMovementDirection`, `OscParameterType`, `IntZeroDurationMode`, `FloatValueMode`, `SetTriggerRestoreMode`, `SpecialRulePairingMode`, `SetTriggerActionSnapshot`, `SupporterFloatAddRangeSnapshot`), open `VrcTwitchOscBridge/Services/BridgeRuntimeConfiguration.cs:58-143` and copy the exact parameter list and types from there. The plan's structure is unchanged; only the literal type identifiers may need correction.

- [ ] **Step 2: Build the test project**

Run:
```bash
dotnet build "E:\!!!Program to work on\Proper Crystal Relay\VrcTwitchOscBridge.Tests\VrcTwitchOscBridge.Tests.csproj" --no-restore
```

Expected: build succeeds with no errors. (Tests have no test methods yet, but the project must still compile.)

- [ ] **Step 3: Commit**

```bash
git add VrcTwitchOscBridge.Tests/TestTriggerRuleSnapshotBuilder.cs
git commit -m "test: add TestTriggerRuleSnapshotBuilder for override math tests"
```

---

## Task 2: Add failing tests for the new math helper

**Files:**
- Create: `VrcTwitchOscBridge.Tests/SupportOverrideDurationMathTests.cs`

- [ ] **Step 1: Write the failing tests**

Create `VrcTwitchOscBridge.Tests/SupportOverrideDurationMathTests.cs`:

```csharp
using VrcTwitchOscBridge.Models;
using VrcTwitchOscBridge.Services.Support;
using Xunit;

namespace VrcTwitchOscBridge.Tests;

public sealed class SupportOverrideDurationMathTests
{
    [Fact]
    public void ComputePerEventAddSeconds_ToggleOff_ReturnsBaseDuration()
    {
        var rule = TestTriggerRuleSnapshotBuilder.Build(
            amountScaledDurationEnabled: false,
            durationSeconds: 30);

        var result = SupportOverrideDurationMath.ComputePerEventAddSeconds(rule, amount: 100, subscriptionTier: string.Empty);

        Assert.Equal(30, result);
    }

    [Fact]
    public void ComputePerEventAddSeconds_BitsZeroBase_ReturnsScaledOnly()
    {
        var rule = TestTriggerRuleSnapshotBuilder.Build(
            amountScaledDurationEnabled: true,
            durationSeconds: 0,
            bitsAmountUnitsPerDuration: 50,
            bitsSecondsPerAmountUnit: 1);

        var result = SupportOverrideDurationMath.ComputePerEventAddSeconds(rule, amount: 100, subscriptionTier: string.Empty);

        Assert.Equal(2, result);
    }

    [Fact]
    public void ComputePerEventAddSeconds_BitsWithBase_ReturnsBasePlusScaled()
    {
        var rule = TestTriggerRuleSnapshotBuilder.Build(
            amountScaledDurationEnabled: true,
            durationSeconds: 30,
            bitsAmountUnitsPerDuration: 50,
            bitsSecondsPerAmountUnit: 1);

        var result = SupportOverrideDurationMath.ComputePerEventAddSeconds(rule, amount: 100, subscriptionTier: string.Empty);

        Assert.Equal(32, result);
    }

    [Fact]
    public void ComputePerEventAddSeconds_BitsDifferentRatio_ReturnsBasePlusScaled()
    {
        var rule = TestTriggerRuleSnapshotBuilder.Build(
            amountScaledDurationEnabled: true,
            durationSeconds: 30,
            bitsAmountUnitsPerDuration: 25,
            bitsSecondsPerAmountUnit: 2);

        var result = SupportOverrideDurationMath.ComputePerEventAddSeconds(rule, amount: 50, subscriptionTier: string.Empty);

        Assert.Equal(34, result);
    }

    [Fact]
    public void ComputePerEventAddSeconds_SubsTier1_ReturnsBasePlusScaled()
    {
        var rule = TestTriggerRuleSnapshotBuilder.Build(
            triggerType: TwitchTriggerType.Subscriptions,
            amountScaledDurationEnabled: true,
            durationSeconds: 60,
            subscriptionTier1SecondsPerSub: 30);

        var result = SupportOverrideDurationMath.ComputePerEventAddSeconds(rule, amount: 1, subscriptionTier: "1000");

        Assert.Equal(90, result);
    }

    [Fact]
    public void ComputePerEventAddSeconds_GiftSubs_ReturnsBasePlusScaledTimesCount()
    {
        var rule = TestTriggerRuleSnapshotBuilder.Build(
            triggerType: TwitchTriggerType.Subscriptions,
            amountScaledDurationEnabled: true,
            durationSeconds: 60,
            subscriptionTier1SecondsPerSub: 30);

        var result = SupportOverrideDurationMath.ComputePerEventAddSeconds(rule, amount: 5, subscriptionTier: "1000");

        Assert.Equal(210, result);
    }

    [Fact]
    public void ComputePerEventAddSeconds_ZeroBase_ScalingOn_ReturnsScaledOnly()
    {
        var rule = TestTriggerRuleSnapshotBuilder.Build(
            amountScaledDurationEnabled: true,
            durationSeconds: 0,
            bitsAmountUnitsPerDuration: 50,
            bitsSecondsPerAmountUnit: 1);

        var result = SupportOverrideDurationMath.ComputePerEventAddSeconds(rule, amount: 50, subscriptionTier: string.Empty);

        Assert.Equal(1, result);
    }

    [Fact]
    public void ComputePerEventAddSeconds_BitsZeroRatio_FallsBackToAmountTimesSeconds()
    {
        var rule = TestTriggerRuleSnapshotBuilder.Build(
            amountScaledDurationEnabled: true,
            durationSeconds: 10,
            bitsAmountUnitsPerDuration: 0,
            bitsSecondsPerAmountUnit: 3);

        var result = SupportOverrideDurationMath.ComputePerEventAddSeconds(rule, amount: 7, subscriptionTier: string.Empty);

        Assert.Equal(31, result);
    }
}
```

- [ ] **Step 2: Run tests to verify they fail (helper does not exist yet)**

Run:
```bash
dotnet test "E:\!!!Program to work on\Proper Crystal Relay\VrcTwitchOscBridge.Tests\VrcTwitchOscBridge.Tests.csproj" --filter "FullyQualifiedName~SupportOverrideDurationMathTests" --no-restore
```

Expected: build error referencing `SupportOverrideDurationMath.ComputePerEventAddSeconds` (type does not exist). All 8 tests will not run.

- [ ] **Step 3: Commit the failing tests**

```bash
git add VrcTwitchOscBridge.Tests/SupportOverrideDurationMathTests.cs
git commit -m "test: add failing tests for SupportOverrideDurationMath helper"
```

---

## Task 3: Implement the math helper

**Files:**
- Create: `VrcTwitchOscBridge/Services/Support/SupportOverrideDurationMath.cs`

- [ ] **Step 1: Create the new namespace folder and file**

Create `VrcTwitchOscBridge/Services/Support/SupportOverrideDurationMath.cs`:

```csharp
using VrcTwitchOscBridge.Models;

namespace VrcTwitchOscBridge.Services.Support;

public static class SupportOverrideDurationMath
{
    public static double ComputePerEventAddSeconds(
        TriggerRuleSnapshot rule,
        int amount,
        string subscriptionTier)
    {
        var baseSeconds = Math.Max(0, rule.DurationSeconds);
        if (!rule.AmountScaledDurationEnabled)
        {
            return Math.Max(1, baseSeconds);
        }

        var scaled = ComputeScaledSeconds(rule, amount, subscriptionTier);
        return baseSeconds + scaled;
    }

    private static double ComputeScaledSeconds(
        TriggerRuleSnapshot rule,
        int amount,
        string subscriptionTier)
    {
        var safeAmount = Math.Max(1, amount);
        if (rule.TriggerType == TwitchTriggerType.Subscriptions)
        {
            var secondsPerSub = ResolveSubscriptionSecondsPerSub(rule, subscriptionTier);
            return safeAmount * secondsPerSub;
        }

        var unitsPerDuration = Math.Max(1, rule.BitsAmountUnitsPerDuration);
        var secondsPerUnit = Math.Max(1, rule.BitsSecondsPerAmountUnit);
        return (double)safeAmount / unitsPerDuration * secondsPerUnit;
    }

    private static int ResolveSubscriptionSecondsPerSub(TriggerRuleSnapshot rule, string subscriptionTier)
    {
        return subscriptionTier?.Trim() switch
        {
            "2000" => Math.Max(1, rule.SubscriptionTier2SecondsPerSub),
            "3000" => Math.Max(1, rule.SubscriptionTier3SecondsPerSub),
            _ => Math.Max(1, rule.SubscriptionTier1SecondsPerSub)
        };
    }
}
```

- [ ] **Step 2: Build the app project**

Run:
```bash
dotnet build "E:\!!!Program to work on\Proper Crystal Relay\VrcTwitchOscBridge\VrcTwitchOscBridge.csproj" --no-restore
```

Expected: build succeeds.

- [ ] **Step 3: Run the tests to verify they pass**

Run:
```bash
dotnet test "E:\!!!Program to work on\Proper Crystal Relay\VrcTwitchOscBridge.Tests\VrcTwitchOscBridge.Tests.csproj" --filter "FullyQualifiedName~SupportOverrideDurationMathTests" --no-restore
```

Expected: all 8 tests pass.

- [ ] **Step 4: Commit**

```bash
git add VrcTwitchOscBridge/Services/Support/SupportOverrideDurationMath.cs
git commit -m "feat(support): add SupportOverrideDurationMath helper"
```

---

## Task 4: Wire the helper into BridgeCoordinator.GetSupporterOverrideDuration

**Files:**
- Modify: `VrcTwitchOscBridge/Services/BridgeCoordinator.cs:6411-6427`

- [ ] **Step 1: Replace the body of GetSupporterOverrideDuration**

Open `VrcTwitchOscBridge/Services/BridgeCoordinator.cs`. Find lines 6411-6427 (the `GetSupporterOverrideDuration` method). Replace the method with:

```csharp
    private static TimeSpan GetSupporterOverrideDuration(
        TriggerRuleSnapshot rule,
        BridgeIncomingEvent bridgeEvent)
    {
        var perEventAddSeconds = SupportOverrideDurationMath.ComputePerEventAddSeconds(
            rule,
            bridgeEvent.Amount,
            bridgeEvent.SubscriptionTier);

        return TimeSpan.FromSeconds(
            Math.Min(Math.Max(1, perEventAddSeconds), TimeSpan.MaxValue.TotalSeconds));
    }
```

Add the using directive near the top of the file if not already present (next to other `using VrcTwitchOscBridge.Services...` lines):

```csharp
using VrcTwitchOscBridge.Services.Support;
```

- [ ] **Step 2: Build to verify**

Run:
```bash
dotnet build "E:\!!!Program to work on\Proper Crystal Relay\VrcTwitchOscBridge\VrcTwitchOscBridge.csproj" --no-restore
```

Expected: build fails with a compile error at the call site in `HandleTimedSupporterOverrideTriggerAsync` (line 8509), because the helper signature changed and the call still passes `includeStartingDuration`. This is expected; the next task fixes the call site.

- [ ] **Step 3: Commit**

```bash
git add VrcTwitchOscBridge/Services/BridgeCoordinator.cs
git commit -m "refactor(support): route GetSupporterOverrideDuration through helper"
```

---

## Task 5: Update the call site in HandleTimedSupporterOverrideTriggerAsync

**Files:**
- Modify: `VrcTwitchOscBridge/Services/BridgeCoordinator.cs:8440-8558`

- [ ] **Step 1: Remove the three local variables and update the call**

In `HandleTimedSupporterOverrideTriggerAsync`, delete these lines (currently 8504-8509):

```csharp
        var hasSameRuleActive = activeState is not null
            && activeState.ActiveUntil > now
            && activeState.Rule.Id == rule.Id;
        var hasSameRuleQueued = queuedIndex >= 0;
        var includeStartingDuration = !hasSameRuleActive && !hasSameRuleQueued;
        var requestedDuration = GetSupporterOverrideDuration(rule, bridgeEvent, includeStartingDuration);
```

Replace with:

```csharp
        var requestedDuration = GetSupporterOverrideDuration(rule, bridgeEvent);
```

- [ ] **Step 2: Build the app project**

Run:
```bash
dotnet build "E:\!!!Program to work on\Proper Crystal Relay\VrcTwitchOscBridge\VrcTwitchOscBridge.csproj" --no-restore
```

Expected: build succeeds with no errors. The downstream branching code at lines 8520+ uses `activeState is not null && activeState.ActiveUntil > now && activeState.Rule.Id == rule.Id` and `queuedIndex >= 0` directly, so removing the locals does not break the dispatch.

- [ ] **Step 3: Run the full test suite**

Run:
```bash
dotnet test "E:\!!!Program to work on\Proper Crystal Relay\VrcTwitchOscBridge.Tests\VrcTwitchOscBridge.Tests.csproj" --no-restore
```

Expected: all tests pass, including the 8 new `SupportOverrideDurationMathTests` and the existing `InlineRuleRowViewModelTests` and `AvatarSwapRuntimeDispatchTests`.

- [ ] **Step 4: Commit**

```bash
git add VrcTwitchOscBridge/Services/BridgeCoordinator.cs
git commit -m "feat(support): always add base + scaled per trigger for Bits/Subs overrides"
```

---

## Task 6: Update the help text in TriggerRule.cs

**Files:**
- Modify: `VrcTwitchOscBridge/Models/TriggerRule.cs:1619-1629`

- [ ] **Step 1: Replace the `UsesAmountScaledDuration` branch help text**

In `Models/TriggerRule.cs`, find the `DurationHelpText` property (around line 1619). Locate the `UsesAmountScaledDuration` ternary branch. The current text is:

```csharp
T("Amount-scaled timer is enabled, so Active Time is the starting time. Bits and subs add time on top when the override first starts; later same-rule triggers extend the current timer by the amount only.")
```

Replace with:

```csharp
T("Active time and amount both add on top. Each matching trigger extends the current timer by Active Time + the scaled amount.")
```

- [ ] **Step 2: Build to verify**

Run:
```bash
dotnet build "E:\!!!Program to work on\Proper Crystal Relay\VrcTwitchOscBridge\VrcTwitchOscBridge.csproj" --no-restore
```

Expected: build succeeds. (The `T` helper is a localization wrapper; the build does not validate the localization key exists. That is checked by the localization audit in Task 12.)

- [ ] **Step 3: Commit**

```bash
git add VrcTwitchOscBridge/Models/TriggerRule.cs
git commit -m "docs(ui): clarify Bits/Subs amount timer always adds on top"
```

---

## Task 7: Rename the master toggle label in XAML

**Files:**
- Modify: `VrcTwitchOscBridge/SupporterOverrideTimeSettingsWindow.xaml:220-221`

- [ ] **Step 1: Update the checkbox content**

Open `VrcTwitchOscBridge/SupporterOverrideTimeSettingsWindow.xaml`. Find the CheckBox that contains:

```xml
Content="{loc:Translate 'Scale active time by amount'}"
```

Replace with:

```xml
Content="{loc:Translate 'Add amount to active time'}"
```

- [ ] **Step 2: Build to verify**

Run:
```bash
dotnet build "E:\!!!Program to work on\Proper Crystal Relay\VrcTwitchOscBridge\VrcTwitchOscBridge.csproj" --no-restore
```

Expected: build succeeds.

- [ ] **Step 3: Commit**

```bash
git add VrcTwitchOscBridge/SupporterOverrideTimeSettingsWindow.xaml
git commit -m "ui(rename): label Bits/Subs amount timer as 'Add amount to active time'"
```

---

## Task 8: Update en-US.json

**Files:**
- Modify: `VrcTwitchOscBridge/Resources/Localization/en-US.json:488`

- [ ] **Step 1: Update the English source string**

Open `VrcTwitchOscBridge/Resources/Localization/en-US.json`. Find line 488:

```json
  "Scale active time by amount": "Scale active time by amount",
```

Replace with:

```json
  "Add amount to active time": "Add amount to active time",
```

- [ ] **Step 2: Commit**

```bash
git add VrcTwitchOscBridge/Resources/Localization/en-US.json
git commit -m "i18n(en): rename 'Scale active time by amount' toggle"
```

---

## Task 9: Update en-US.extra.json with the new help text key

**Files:**
- Modify: `VrcTwitchOscBridge/Resources/Localization/en-US.extra.json:596-597`

- [ ] **Step 1: Replace the old help text key and add the new one**

Open `VrcTwitchOscBridge/Resources/Localization/en-US.extra.json`. Find lines 596-597. Delete the line:

```json
  "Amount-scaled timer is enabled, so Active Time is the starting time. Bits and subs add time on top when the override first starts; later same-rule triggers extend the current timer by the amount only.": "Amount-scaled timer is enabled, so Active Time is the starting time. Bits and subs add time on top when the override first starts; later same-rule triggers extend the current timer by the amount only.",
```

(Leave the line 596 sibling alone unless Task 6 already removed it; if both lines reference the old text, remove both.)

Add in its place:

```json
  "Active time and amount both add on top. Each matching trigger extends the current timer by Active Time + the scaled amount.": "Active time and amount both add on top. Each matching trigger extends the current timer by Active Time + the scaled amount.",
```

- [ ] **Step 2: Commit**

```bash
git add VrcTwitchOscBridge/Resources/Localization/en-US.extra.json
git commit -m "i18n(en): update Bits/Subs amount timer help text"
```

---

## Task 10: Update all 14 non-English locale .json files (the toggle label)

**Files:**
- Modify: 14 non-English `*.json` files in `VrcTwitchOscBridge/Resources/Localization/`

- [ ] **Step 1: Identify all locale .json files**

Run:
```bash
Get-ChildItem -LiteralPath "E:\!!!Program to work on\Proper Crystal Relay\VrcTwitchOscBridge\Resources\Localization\*.json" | Where-Object { $_.Name -ne "en-US.json" } | Select-Object -ExpandProperty Name
```

Expected: a list of 14 locale files (e.g., `de-DE.json`, `es-ES.json`, `fr-FR.json`, `it-IT.json`, `ja-JP.json`, `ko-KR.json`, `pl-PL.json`, `pt-BR.json`, `ru-RU.json`, `sv-SE.json`, `th-TH.json`, `zh-CN.json`, `zh-TW.json`, plus one more).

- [ ] **Step 2: For each non-English .json file, update the key**

For each locale file, find the line:

```json
  "Scale active time by amount": "<old translation>",
```

Replace the key on the left with `"Add amount to active time"`. Leave the right-hand value as-is (the build localization audit will flag missing translations; the value is updated in Task 11).

For example, in `es-ES.json` the line becomes:

```json
  "Add amount to active time": "<existing Spanish translation of the old label, if present>",
```

If the locale file does not currently contain the key `"Scale active time by amount"`, skip that file (the localization audit will catch the missing key, and Task 11 will add the value if needed).

- [ ] **Step 3: Build to verify**

Run:
```bash
dotnet build "E:\!!!Program to work on\Proper Crystal Relay\VrcTwitchOscBridge\VrcTwitchOscBridge.csproj" --no-restore
```

Expected: build succeeds. (The build does not validate localization key coverage; the audit in Task 12 does.)

- [ ] **Step 4: Commit all locale .json changes in one commit**

```bash
git add VrcTwitchOscBridge/Resources/Localization/*.json
git commit -m "i18n: rename 'Scale active time by amount' key in all locales"
```

---

## Task 11: Update all 14 non-English locale .extra.json files (the help text)

**Files:**
- Modify: 14 non-English `*.extra.json` files in `VrcTwitchOscBridge/Resources/Localization/`

- [ ] **Step 1: Identify all locale .extra.json files**

Run:
```bash
Get-ChildItem -LiteralPath "E:\!!!Program to work on\Proper Crystal Relay\VrcTwitchOscBridge\Resources\Localization\*.extra.json" | Select-Object -ExpandProperty Name
```

Expected: 14 files (one per non-English locale).

- [ ] **Step 2: For each non-English .extra.json file, replace the old help text key with the new one**

For each locale file:

1. Find the line whose key is `"Amount-scaled timer is enabled, so Active Time is the starting time. Bits and subs add time on top when the override first starts; later same-rule triggers extend the current timer by the amount only."`.

2. Delete that entire line.

3. Add a new line with the key `"Active time and amount both add on top. Each matching trigger extends the current timer by Active Time + the scaled amount."` and a fresh translation.

The translation must follow AGENTS.md rules:

- Natural and conversational in the target language, not stiff or machine-translated.
- Informal/friendly register: `du` for de-DE, `tú` for es-ES, `tu` for fr-FR, informal equivalents elsewhere.
- Keep these in English: `Bits`, `Subs`, `OSC`, `OSCQuery`, `VRChat`, `Twitch`, `Crystal Relay`, `StreamElements`, `Streamlabs`, `Ko-fi`.
- Preserve placeholders exactly: `{0}`, `{1}`, `{2}`, etc. (this new key has no placeholders).
- Use consistent terminology within the same language file. Look up how the file already translates `Active Time`, `Bits`, `Subs`, `trigger`, `scaled amount`, and reuse those terms.

Example for `es-ES.extra.json`:

```json
  "Active time and amount both add on top. Each matching trigger extends the current timer by Active Time + the scaled amount.": "El tiempo activo y la cantidad se suman encima. Cada trigger que coincide amplía el temporizador actual por Tiempo Activo + la cantidad escalada.",
```

Example for `de-DE.extra.json`:

```json
  "Active time and amount both add on top. Each matching trigger extends the current timer by Active Time + the scaled amount.": "Aktive Zeit und Betrag werden oben drauf addiert. Jeder passende Trigger verlängert den aktuellen Timer um Aktive Zeit + den skalierten Betrag.",
```

- [ ] **Step 3: Commit all locale .extra.json changes in one commit**

```bash
git add VrcTwitchOscBridge/Resources/Localization/*.extra.json
git commit -m "i18n: update Bits/Subs amount timer help text in all locales"
```

---

## Task 12: Run the localization audit

**Files:**
- Run: `LocalizationAudit` project (existing tool, referenced from `AGENTS.md`)

- [ ] **Step 1: Run the audit**

```bash
dotnet run --project "E:\!!!Program to work on\Proper Crystal Relay\LocalizationAudit" -- "E:\!!!Program to work on\Proper Crystal Relay\VrcTwitchOscBridge\Resources\Localization"
```

Expected: the audit reports no missing keys, no empty values, and no placeholder mismatches for the two changed keys (`Add amount to active time` in every `*.json`, and the new help text in every `*.extra.json`).

If the audit reports issues, fix them in this task before moving on. Common issues: a locale file is missing the new key, a value is empty, a placeholder was changed.

- [ ] **Step 2: Commit any audit fixes**

```bash
git add VrcTwitchOscBridge/Resources/Localization/
git commit -m "i18n: fix localization audit findings"
```

If no fixes were needed, skip this step.

---

## Task 13: Update CHANGELOG.txt

**Files:**
- Modify: `CHANGELOG.txt` (append to the existing `v3.1.9 beta 4` section around line 20)

- [ ] **Step 1: Add the two bullets**

Open `CHANGELOG.txt`. Find the `v3.1.9 beta 4` section header (around line 20) and append two bullets to the end of that section's existing bullet list:

```text
- Changed: the "Add amount to active time" toggle on Bits and Subs overrides now always adds Active Time + the scaled amount to the current timer on every matching trigger, instead of only adding the base time on the first trigger. Cap max accumulated duration still limits the total running time.
- Renamed: "Scale active time by amount" -> "Add amount to active time" in the Bits and Subs override time-settings popup.
```

The hyphen and the em-dash inside the second bullet match the existing entry style in the file (use literal `->` in the file, not a typographic arrow).

- [ ] **Step 2: Commit**

```bash
git add CHANGELOG.txt
git commit -m "docs(changelog): add bits/subs amount-timer notes to 3.1.9 beta 4"
```

---

## Task 14: Update RELEASE-CHANGE-RECORD.txt

**Files:**
- Modify: `RELEASE-CHANGE-RECORD.txt`

- [ ] **Step 1: Update the Current Baseline block**

Open `RELEASE-CHANGE-RECORD.txt`. Find the `Current Baseline` block (around line 9). Update the two bullet values:

- `Last published version: v3.1.9` -> `Last published version: v3.1.8`
- `Current working source version: v3.1.10` -> `Current working source version: v3.1.9`

- [ ] **Step 2: Update the Pending Release Draft header**

Find the `Pending Release Draft` block (around line 20). Update the three bullet values:

- `Target version: v3.1.10` -> `Target version: v3.1.9`
- `Next build: v3.1.10 beta 1` -> `Next build: v3.1.9 beta 4`
- `Source files reflect v3.1.10; csproj and updater project will be updated when new work begins.` -> `Source files reflect v3.1.9; csproj and updater project will be updated when new work begins.`

- [ ] **Step 3: Add the v3.1.9 beta 4 (in progress) section**

Find the line `v3.1.9 beta 3 (already shipped)` block (around line 40). Insert a new section above it:

```text
v3.1.9 beta 4 (in progress)
Added:
Changed:
- "Add amount to active time" now always adds Active Time + scaled amount on every matching Bits or Subs trigger instead of only on the first trigger; cap still limits total running time.
- Renamed "Scale active time by amount" to "Add amount to active time" in the Bits and Subs override time-settings popup.
Removed:
```

- [ ] **Step 4: Commit**

```bash
git add RELEASE-CHANGE-RECORD.txt
git commit -m "docs(release-record): add 3.1.9 beta 4 entry and update baseline"
```

---

## Task 15: Update AGENTS.md housekeeping

**Files:**
- Modify: `AGENTS.md` (top of file, "Project Identity" block)

- [ ] **Step 1: Update the Project Identity values**

Open `AGENTS.md`. Find the "Project Identity" block at the top. Update these four lines:

- `Last stable release: v3.1.9` -> `Last stable release: v3.1.8`
- `Current source version: v3.1.10` -> `Current source version: v3.1.9`
- `Next post-release development version: v3.1.11` -> `Next post-release development version: v3.1.10`
- `Active development build: v3.1.10` -> `Active development build: v3.1.9`
- `Active build lane: beta1` -> `Active build lane: beta4`

- [ ] **Step 2: Commit**

```bash
git add AGENTS.md
git commit -m "docs(agents): sync Project Identity to 3.1.9 beta 4"
```

---

## Task 16: Final build and test verification

**Files:**
- Run: full build + test suite

- [ ] **Step 1: Full build**

```bash
dotnet build "E:\!!!Program to work on\Proper Crystal Relay\VrcTwitchOscBridge\VrcTwitchOscBridge.csproj" --no-restore
```

Expected: build succeeds with no warnings or errors.

- [ ] **Step 2: Full test run**

```bash
dotnet test "E:\!!!Program to work on\Proper Crystal Relay\VrcTwitchOscBridge.Tests\VrcTwitchOscBridge.Tests.csproj" --no-restore
```

Expected: every test passes, including:
- 8 new `SupportOverrideDurationMathTests` (Task 2/3).
- 7 existing `InlineRuleRowViewModelTests` (Bits/ChannelPoints/Subs rows).
- All `AvatarSwapRuntimeDispatchTests`.

- [ ] **Step 3: Verify no uncommitted changes remain**

```bash
git status
```

Expected: clean working tree. If any files appear, address them before declaring done (build artifacts like `bin/`, `obj/`, `.vs/`, `.dotnet/`, `.nuget/`, `.appdata/` should be ignored; if they show up, add them to `.gitignore` rather than committing).

- [ ] **Step 4: Report completion**

Print a one-line summary to the user:

```
Done. Last stable: 3.1.8; in-progress: 3.1.9 beta 4. Bits/Subs amount timer now always adds Active Time + scaled on every matching trigger. Tests green. No push (dev mode).
```

---

## Out of scope (do not do in this plan)

- Renaming the persisted `AmountScaledDurationEnabled` field in code or storage.
- Adding a per-rule toggle to pick between the old and new behavior.
- Refactoring `ClampSupporterOverrideAddedDuration` or the queue/dispatch path.
- Updating `README.md` (beta builds do not update highlights).
- Updating the Void Crystal website (the download URL tracks stable only).
- Pushing to the public or private GitHub repos. This is dev mode; no pushes.
- Building or publishing a beta 4 test package or release package. The user will ask for that explicitly when ready.
