# Per-Avatar Avatar Swap: Add Bits/Subs Time to Swap Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Add a per-avatar `Add bits time to swap` option to the Avatar Swap manager's Bits and Subs triggers. When enabled, every matching trigger extends the current avatar swap by `Active Time + scaled amount`. When disabled, only the fixed `Active Time` runs. Independent of the existing global `AmountScaledDurationEnabled` toggle.

**Architecture:** New boolean field `AddBitsToSwapTime` on `TriggerRule` (and mirrored to `TriggerRuleSnapshot` and `PersistedTriggerRule`). The math helper `SupportOverrideDurationMath.ComputePerEventAddSeconds` branches on the new field. UI exposes a new checkbox at the top of the Bits and Subs sections in the per-avatar editor. Migration V5→V6 adds the field with default `false`.

**Tech Stack:** C# .NET 10, WPF, xUnit, JSON localization files.

**Working directory:** `E:\!!!Program to work on\Proper Crystal Relay`

**Build/test commands:**
- Build app: `dotnet build "E:\!!!Program to work on\Proper Crystal Relay\VrcTwitchOscBridge\VrcTwitchOscBridge.csproj" --no-restore`
- Build + run tests: `dotnet test "E:\!!!Program to work on\Proper Crystal Relay\VrcTwitchOscBridge.Tests\VrcTwitchOscBridge.Tests.csproj"`
- Localization audit: `dotnet run --project "E:\!!!Program to work on\Proper Crystal Relay\LocalizationAudit" -- "E:\!!!Program to work on\Proper Crystal Relay\VrcTwitchOscBridge\Resources\Localization"`

---

## File structure

**New files:**
- `VrcTwitchOscBridge.Tests/AvatarSwapMigrationServiceV6Tests.cs` - migration test for V5→V6 defaulting.

**Modified files:**
- `VrcTwitchOscBridge/Models/TriggerRule.cs` - new field, property, computed property, `TriggerSummary` update.
- `VrcTwitchOscBridge/Services/BridgeRuntimeConfiguration.cs` - new snapshot parameter; `TryToSnapshot` mapping.
- `VrcTwitchOscBridge/Services/Support/SupportOverrideDurationMath.cs` - new `UsesScaledMath` helper, branch the gate.
- `VrcTwitchOscBridge/Services/SettingsStore.cs` - new `PersistedTriggerRule` field; `ToPersistedRule` / `ToRule` mapping.
- `VrcTwitchOscBridge/Services/AvatarSwapMigrationService.cs` - bump `CurrentMigrationVersion` to 6; add `MigrateV5ToV6`.
- `VrcTwitchOscBridge/UserControls/InlineRuleEditorControl.xaml` - new checkbox in Bits and Subs sections.
- `VrcTwitchOscBridge/UserControls/InlineBitsRuleRowViewModel.cs` - new chip in summary.
- `VrcTwitchOscBridge/UserControls/InlineSubsRuleRowViewModel.cs` - new chip in summary.
- `VrcTwitchOscBridge/Resources/Localization/en-US.json` - 2 new English keys.
- 14 non-English `*.json` files - matching translations.
- `VrcTwitchOscBridge.Tests/TestTriggerRuleSnapshotBuilder.cs` - new `addBitsToSwapTime` parameter.
- `VrcTwitchOscBridge.Tests/SupportOverrideDurationMathTests.cs` - 5 new test methods.
- `VrcTwitchOscBridge.Tests/InlineRuleRowViewModelTests.cs` - 2 new test methods.
- `CHANGELOG.txt` - 1 new bullet in `v3.1.9 beta 4` section.
- `RELEASE-CHANGE-RECORD.txt` - 1 new bullet in `v3.1.9 beta 4 (in progress)` section.

The csproj has `EnableDefaultCompileItems=false` so any new `.cs` file must be explicitly added to `VrcTwitchOscBridge.csproj` under the appropriate `<ItemGroup>`. The new test file is added to `VrcTwitchOscBridge.Tests.csproj` which has default item inclusion.

---

## Task 1: Add failing tests for the new math

**Files:**
- Modify: `VrcTwitchOscBridge.Tests/SupportOverrideDurationMathTests.cs`
- Modify: `VrcTwitchOscBridge.Tests/TestTriggerRuleSnapshotBuilder.cs`

- [ ] **Step 1: Add the new builder parameter to `TestTriggerRuleSnapshotBuilder`**

In `VrcTwitchOscBridge.Tests/TestTriggerRuleSnapshotBuilder.cs`, add a new optional parameter to `Build`:

```csharp
    public static TriggerRuleSnapshot Build(
        TwitchTriggerType triggerType = TwitchTriggerType.Bits,
        bool amountScaledDurationEnabled = false,
        double durationSeconds = 0,
        int bitsAmountUnitsPerDuration = 50,
        int bitsSecondsPerAmountUnit = 1,
        int subscriptionTier1SecondsPerSub = 0,
        int subscriptionTier2SecondsPerSub = 0,
        int subscriptionTier3SecondsPerSub = 0,
        bool addBitsToSwapTime = false)
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
            SubscriptionTier3SecondsPerSub = subscriptionTier3SecondsPerSub,
            AddBitsToSwapTime = addBitsToSwapTime
        };
    }
```

The new parameter only goes into the `with` block. The `Default` field's `AddBitsToSwapTime` (set when the snapshot is created) stays at its default of `false`.

- [ ] **Step 2: Write the failing tests**

Add these 5 new test methods to `SupportOverrideDurationMathTests.cs`, after the existing 8 tests:

```csharp
    [Fact]
    public void ComputePerEventAddSeconds_AddBitsToSwapTimeOff_ReturnsBaseDuration()
    {
        var rule = TestTriggerRuleSnapshotBuilder.Build(
            addBitsToSwapTime: false,
            amountScaledDurationEnabled: false,
            durationSeconds: 30);

        var result = SupportOverrideDurationMath.ComputePerEventAddSeconds(rule, amount: 100, subscriptionTier: string.Empty);

        Assert.Equal(30, result);
    }

    [Fact]
    public void ComputePerEventAddSeconds_AddBitsToSwapTimeOn_ReturnsBasePlusScaled()
    {
        var rule = TestTriggerRuleSnapshotBuilder.Build(
            addBitsToSwapTime: true,
            amountScaledDurationEnabled: false,
            durationSeconds: 30,
            bitsAmountUnitsPerDuration: 50,
            bitsSecondsPerAmountUnit: 1);

        var result = SupportOverrideDurationMath.ComputePerEventAddSeconds(rule, amount: 100, subscriptionTier: string.Empty);

        Assert.Equal(32, result);
    }

    [Fact]
    public void ComputePerEventAddSeconds_AddBitsToSwapTimeOn_SubsT1()
    {
        var rule = TestTriggerRuleSnapshotBuilder.Build(
            triggerType: TwitchTriggerType.Subscriptions,
            addBitsToSwapTime: true,
            amountScaledDurationEnabled: false,
            durationSeconds: 60,
            subscriptionTier1SecondsPerSub: 30);

        var result = SupportOverrideDurationMath.ComputePerEventAddSeconds(rule, amount: 1, subscriptionTier: "1000");

        Assert.Equal(90, result);
    }

    [Fact]
    public void ComputePerEventAddSeconds_BothTogglesOff_ReturnsBaseDuration()
    {
        var rule = TestTriggerRuleSnapshotBuilder.Build(
            addBitsToSwapTime: false,
            amountScaledDurationEnabled: false,
            durationSeconds: 25);

        var result = SupportOverrideDurationMath.ComputePerEventAddSeconds(rule, amount: 100, subscriptionTier: string.Empty);

        Assert.Equal(25, result);
    }

    [Fact]
    public void ComputePerEventAddSeconds_BothTogglesOn_StillScaled()
    {
        var rule = TestTriggerRuleSnapshotBuilder.Build(
            addBitsToSwapTime: true,
            amountScaledDurationEnabled: true,
            durationSeconds: 30,
            bitsAmountUnitsPerDuration: 50,
            bitsSecondsPerAmountUnit: 1);

        var result = SupportOverrideDurationMath.ComputePerEventAddSeconds(rule, amount: 100, subscriptionTier: string.Empty);

        Assert.Equal(32, result);
    }
```

- [ ] **Step 3: Run tests to verify they fail**

Run:
```bash
dotnet test "E:\!!!Program to work on\Proper Crystal Relay\VrcTwitchOscBridge.Tests\VrcTwitchOscBridge.Tests.csproj" --filter "FullyQualifiedName~SupportOverrideDurationMathTests" --no-restore
```

Expected: build error. The new tests reference `rule.AddBitsToSwapTime` which doesn't exist on `TriggerRuleSnapshot` yet. The `with` block in `TestTriggerRuleSnapshotBuilder` will also fail to compile.

- [ ] **Step 4: Commit the failing tests**

```bash
git add VrcTwitchOscBridge.Tests/SupportOverrideDurationMathTests.cs VrcTwitchOscBridge.Tests/TestTriggerRuleSnapshotBuilder.cs
git commit -m "test: add failing tests for AddBitsToSwapTime math field"
```

---

## Task 2: Add the `AddBitsToSwapTime` field to `TriggerRule` and `TriggerRuleSnapshot`

**Files:**
- Modify: `VrcTwitchOscBridge/Models/TriggerRule.cs`
- Modify: `VrcTwitchOscBridge/Services/BridgeRuntimeConfiguration.cs`

- [ ] **Step 1: Add the field and property to `TriggerRule`**

In `Models/TriggerRule.cs`, find the `private` field block around line 76-87 (the bits/subs fields). Add the new field declaration:

```csharp
        private bool addBitsToSwapTime;
```

Place it right after the `private bool amountScaledDurationEnabled;` line (or at the end of the existing private fields block - check the surrounding lines for the right placement).

Then add the public property in the public property block (around line 395-410, near `AmountScaledDurationEnabled`):

```csharp
        public bool AddBitsToSwapTime
        {
            get => addBitsToSwapTime;
            set => SetField(ref addBitsToSwapTime, value);
        }
```

Then add a default in the `TriggerRule` constructor body. Search for `AmountScaledDurationEnabled = false;` in the constructor and add `AddBitsToSwapTime = false;` right after it. If the constructor uses auto-property initializers instead, the field's default of `false` is already correct (bool defaults to `false`).

Then add the computed property in the `UsesAmountScaledDuration` block (around line 1339):

```csharp
        public bool UsesAddBitsToSwapTime => UsesAmountThreshold && AddBitsToSwapTime;
```

- [ ] **Step 2: Add the field to `TriggerRuleSnapshot`**

In `Services/BridgeRuntimeConfiguration.cs:58-143`, find the `TriggerRuleSnapshot` record. Add a new positional parameter:

```csharp
    bool AddBitsToSwapTime,
```

Place it near the other bits/subs duration fields (around line 90, after `SubscriptionTier3SecondsPerSub` and before `MaxAccumulatedDurationEnabled`).

- [ ] **Step 3: Find the `TryToSnapshot` (or similar) method that builds the snapshot**

Run:
```bash
Select-String -Path "E:\!!!Program to work on\Proper Crystal Relay\VrcTwitchOscBridge\Services\BridgeRuntimeConfiguration.cs" -Pattern "AmountScaledDurationEnabled = rule" -List
```

This shows the mapping that copies `AmountScaledDurationEnabled` from the `TriggerRule` into the `TriggerRuleSnapshot`. The new field goes right next to it:

```csharp
                    AddBitsToSwapTime = rule.AddBitsToSwapTime,
```

If there are multiple call sites that build a `TriggerRuleSnapshot`, update each one. Use the next grep to find them all:

```bash
Select-String -Path "E:\!!!Program to work on\Proper Crystal Relay\VrcTwitchOscBridge\Services\BridgeRuntimeConfiguration.cs" -Pattern "AmountScaledDurationEnabled =" -List
```

- [ ] **Step 4: Build to confirm the field is reachable**

Run:
```bash
dotnet build "E:\!!!Program to work on\Proper Crystal Relay\VrcTwitchOscBridge\VrcTwitchOscBridge.csproj" --no-restore
```

Expected: build fails with errors about `AddBitsToSwapTime` not existing on `TriggerRuleSnapshot` at every `with` site (because `Default with { ... }` chains from a snapshot that doesn't have the new field). The errors will be in `TestTriggerRuleSnapshotBuilder.cs` and any other test that builds snapshots. This is the red state we want before Task 3 implements the math.

- [ ] **Step 5: Commit**

```bash
git add VrcTwitchOscBridge/Models/TriggerRule.cs VrcTwitchOscBridge/Services/BridgeRuntimeConfiguration.cs
git commit -m "feat(avatar-swap): add AddBitsToSwapTime field to TriggerRule and snapshot"
```

---

## Task 3: Update `SupportOverrideDurationMath` to branch on the new field

**Files:**
- Modify: `VrcTwitchOscBridge/Services/Support/SupportOverrideDurationMath.cs`

- [ ] **Step 1: Replace the gate check**

Open `Services/Support/SupportOverrideDurationMath.cs`. Find the `ComputePerEventAddSeconds` method (lines 7-20). Replace the body with:

```csharp
    public static double ComputePerEventAddSeconds(
        TriggerRuleSnapshot rule,
        int amount,
        string subscriptionTier)
    {
        var baseSeconds = Math.Max(0, rule.DurationSeconds);
        if (!UsesScaledMath(rule))
        {
            return Math.Max(1, baseSeconds);
        }

        var scaled = ComputeScaledSeconds(rule, amount, subscriptionTier);
        return baseSeconds + scaled;
    }

    private static bool UsesScaledMath(TriggerRuleSnapshot rule) =>
        rule.AmountScaledDurationEnabled || rule.AddBitsToSwapTime;
```

The `ComputeScaledSeconds` and `ResolveSubscriptionSecondsPerSub` methods below stay unchanged.

- [ ] **Step 2: Build the test project**

Run:
```bash
dotnet test "E:\!!!Program to work on\Proper Crystal Relay\VrcTwitchOscBridge.Tests\VrcTwitchOscBridge.Tests.csproj" --filter "FullyQualifiedName~SupportOverrideDurationMathTests" --no-restore
```

Expected: all 13 tests in `SupportOverrideDurationMathTests` pass (8 original + 5 new).

- [ ] **Step 3: Run the full test suite**

Run:
```bash
dotnet test "E:\!!!Program to work on\Proper Crystal Relay\VrcTwitchOscBridge.Tests\VrcTwitchOscBridge.Tests.csproj" --no-restore
```

Expected: all tests pass.

- [ ] **Step 4: Commit**

```bash
git add VrcTwitchOscBridge/Services/Support/SupportOverrideDurationMath.cs
git commit -m "feat(avatar-swap): branch scaled math on AddBitsToSwapTime"
```

---

## Task 4: Update `TriggerSummary` to include the new flag

**Files:**
- Modify: `VrcTwitchOscBridge/Models/TriggerRule.cs`

- [ ] **Step 1: Update the Bits branch in `TriggerSummary`**

In `Models/TriggerRule.cs`, find the `TriggerSummary` property (around line 1638-1703). The Bits branch currently looks like:

```csharp
TwitchTriggerType.Bits => AmountScaledDurationEnabled
    ? TF("Bits >= {0} ({1}s per {2} bits)", Math.Max(1, MinimumAmount), Math.Max(1, BitsSecondsPerAmountUnit), Math.Max(1, BitsAmountUnitsPerDuration))
    : TF("Bits >= {0}", Math.Max(1, MinimumAmount)),
```

Replace with:

```csharp
TwitchTriggerType.Bits => (AmountScaledDurationEnabled || AddBitsToSwapTime)
    ? TF("Bits >= {0} ({1}s per {2} bits)", Math.Max(1, MinimumAmount), Math.Max(1, BitsSecondsPerAmountUnit), Math.Max(1, BitsAmountUnitsPerDuration))
    : TF("Bits >= {0}", Math.Max(1, MinimumAmount)),
```

- [ ] **Step 2: Update the Subscriptions branch the same way**

Find the Subscriptions branch and apply the same change pattern (the exact existing line is in the same `TriggerSummary` switch).

- [ ] **Step 3: Build to verify**

Run:
```bash
dotnet build "E:\!!!Program to work on\Proper Crystal Relay\VrcTwitchOscBridge\VrcTwitchOscBridge.csproj" --no-restore
```

Expected: build succeeds.

- [ ] **Step 4: Commit**

```bash
git add VrcTwitchOscBridge/Models/TriggerRule.cs
git commit -m "feat(avatar-swap): show AddBitsToSwapTime in TriggerSummary"
```

---

## Task 5: Update `InlineBitsRuleRowViewModel` and `InlineSubsRuleRowViewModel` to include the new chip

**Files:**
- Modify: `VrcTwitchOscBridge/UserControls/InlineBitsRuleRowViewModel.cs`
- Modify: `VrcTwitchOscBridge/UserControls/InlineSubsRuleRowViewModel.cs`

- [ ] **Step 1: Add the chip to `InlineBitsRuleRowViewModel`**

In `UserControls/InlineBitsRuleRowViewModel.cs`, find the `Summary` builder (around lines 40-63). Add a new chip right after the existing `keyword` chip (or alongside the existing `cap` chip - whichever is most readable):

```csharp
        if (_rule.AddBitsToSwapTime)
            sb.Append(", swap time");
```

The exact placement matches the style of the other `if` blocks above. The literal `", swap time"` should be replaced with a localized key in a later task; for now, the English literal is fine.

- [ ] **Step 2: Add the chip to `InlineSubsRuleRowViewModel`**

Same change in `UserControls/InlineSubsRuleRowViewModel.cs`. The same `if (_rule.AddBitsToSwapTime) sb.Append(", swap time");` line.

- [ ] **Step 3: Build to verify**

Run:
```bash
dotnet build "E:\!!!Program to work on\Proper Crystal Relay\VrcTwitchOscBridge\VrcTwitchOscBridge.csproj" --no-restore
```

Expected: build succeeds.

- [ ] **Step 4: Commit**

```bash
git add VrcTwitchOscBridge/UserControls/InlineBitsRuleRowViewModel.cs VrcTwitchOscBridge/UserControls/InlineSubsRuleRowViewModel.cs
git commit -m "feat(avatar-swap): add swap-time chip to Bits and Subs row summaries"
```

---

## Task 6: Add failing tests for the row summary chip

**Files:**
- Modify: `VrcTwitchOscBridge.Tests/InlineRuleRowViewModelTests.cs`

- [ ] **Step 1: Write the failing tests**

In `VrcTwitchOscBridge.Tests/InlineRuleRowViewModelTests.cs`, add 2 new test methods. One in `InlineBitsRuleRowViewModelTests`:

```csharp
    [Fact]
    public void Summary_IncludesAddBitsToSwapTime_WhenEnabled()
    {
        var rule = new TriggerRule
        {
            Name = "Cheer",
            TriggerType = TwitchTriggerType.Bits,
            AddBitsToSwapTime = true
        };
        var vm = new InlineBitsRuleRowViewModel(rule);

        Assert.Contains("swap time", vm.Summary);
    }

    [Fact]
    public void Summary_OmitsAddBitsToSwapTime_WhenDisabled()
    {
        var rule = new TriggerRule
        {
            Name = "Cheer",
            TriggerType = TwitchTriggerType.Bits,
            AddBitsToSwapTime = false
        };
        var vm = new InlineBitsRuleRowViewModel(rule);

        Assert.DoesNotContain("swap time", vm.Summary);
    }
```

Add the same two tests in `InlineSubsRuleRowViewModelTests` (replace `InlineBitsRuleRowViewModel` with `InlineSubsRuleRowViewModel` and `TwitchTriggerType.Bits` with `TwitchTriggerType.Subscriptions` in each test).

- [ ] **Step 2: Run tests to verify they pass**

Run:
```bash
dotnet test "E:\!!!Program to work on\Proper Crystal Relay\VrcTwitchOscBridge.Tests\VrcTwitchOscBridge.Tests.csproj" --filter "FullyQualifiedName~InlineBitsRuleRowViewModelTests|FullyQualifiedName~InlineSubsRuleRowViewModelTests" --no-restore
```

Expected: all 4 new tests pass (because the chip code was added in Task 5). The full test suite still passes.

- [ ] **Step 3: Commit**

```bash
git add VrcTwitchOscBridge.Tests/InlineRuleRowViewModelTests.cs
git commit -m "test: add row summary tests for AddBitsToSwapTime chip"
```

---

## Task 7: Update `PersistedTriggerRule` and the round-trip mapping

**Files:**
- Modify: `VrcTwitchOscBridge/Services/SettingsStore.cs`

- [ ] **Step 1: Add the field to `PersistedTriggerRule`**

In `Services/SettingsStore.cs`, find `PersistedTriggerRule` (around line 3160-3315). Add a new property right next to `AmountScaledDurationEnabled`:

```csharp
        public bool AddBitsToSwapTime { get; set; }
```

- [ ] **Step 2: Update `ToPersistedRule`**

In `ToPersistedRule` (around lines 1026-1040), add the new field to the `new PersistedTriggerRule` block:

```csharp
                AddBitsToSwapTime = rule.AddBitsToSwapTime,
```

The exact line goes right after the `AmountScaledDurationEnabled = rule.AmountScaledDurationEnabled,` line.

- [ ] **Step 3: Update `ToRule`**

In `ToRule` (around lines 1267-1295), add the new field to the returned `TriggerRule`:

```csharp
                AddBitsToSwapTime = persisted.AddBitsToSwapTime,
```

Same placement as above.

- [ ] **Step 4: Build to verify**

Run:
```bash
dotnet build "E:\!!!Program to work on\Proper Crystal Relay\VrcTwitchOscBridge\VrcTwitchOscBridge.csproj" --no-restore
```

Expected: build succeeds.

- [ ] **Step 5: Commit**

```bash
git add VrcTwitchOscBridge/Services/SettingsStore.cs
git commit -m "feat(avatar-swap): persist AddBitsToSwapTime in SettingsStore"
```

---

## Task 8: Bump migration version and add V5→V6

**Files:**
- Modify: `VrcTwitchOscBridge/Services/AvatarSwapMigrationService.cs`

- [ ] **Step 1: Bump the version constant**

In `Services/AvatarSwapMigrationService.cs`, find the `CurrentMigrationVersion` constant. Bump it from 5 to 6:

```csharp
        public const int CurrentMigrationVersion = 6;
```

- [ ] **Step 2: Add the V5→V6 method**

Read the existing V4→V5 migration method to match its style. Add a new `MigrateV5ToV6` method right after it (or after the last migration method):

```csharp
        private static void MigrateV5ToV6(PersistedAvatarSwapProfile profile)
        {
            if (profile.ChannelPointRules is not null)
                foreach (var rule in profile.ChannelPointRules)
                    rule.AddBitsToSwapTime = false;
            if (profile.BitsRules is not null)
                foreach (var rule in profile.BitsRules)
                    rule.AddBitsToSwapTime = false;
            if (profile.SubsRules is not null)
                foreach (var rule in profile.SubsRules)
                    rule.AddBitsToSwapTime = false;
            if (profile.RouletteRules is not null)
                foreach (var rule in profile.RouletteRules)
                    rule.AddBitsToSwapTime = false;
        }
```

If the persisted DTO uses a different collection name (e.g., `BitsSubsRules` instead of split `BitsRules` / `SubsRules`), adjust accordingly. Read the file's existing V4→V5 migration to see which collection names exist on `PersistedAvatarSwapProfile`.

- [ ] **Step 3: Wire the migration into the migration chain**

Find the `Migrate` method (or whatever orchestrates the V3→V4, V4→V5 calls). Add a call to `MigrateV5ToV6` for each profile. The exact wiring depends on the existing code structure. Match the style of how V4→V5 is invoked.

- [ ] **Step 4: Build to verify**

Run:
```bash
dotnet build "E:\!!!Program to work on\Proper Crystal Relay\VrcTwitchOscBridge\VrcTwitchOscBridge.csproj" --no-restore
```

Expected: build succeeds.

- [ ] **Step 5: Commit**

```bash
git add VrcTwitchOscBridge/Services/AvatarSwapMigrationService.cs
git commit -m "feat(avatar-swap): bump migration to v6 and default AddBitsToSwapTime to false"
```

---

## Task 9: Add a V6 migration test

**Files:**
- Create: `VrcTwitchOscBridge.Tests/AvatarSwapMigrationServiceV6Tests.cs`

- [ ] **Step 1: Read an existing V5 migration test to match style**

Open `VrcTwitchOscBridge.Tests/AvatarSwapMigrationServiceV5Tests.cs` and copy its structure.

- [ ] **Step 2: Write the test**

Create `VrcTwitchOscBridge.Tests/AvatarSwapMigrationServiceV6Tests.cs`:

```csharp
using VrcTwitchOscBridge.Models;
using VrcTwitchOscBridge.Services;
using Xunit;

namespace VrcTwitchOscBridge.Tests;

public sealed class AvatarSwapMigrationServiceV6Tests
{
    [Fact]
    public void MigrateV5ToV6_DefaultsAddBitsToSwapTimeToFalse()
    {
        var profile = new PersistedAvatarSwapProfile
        {
            Id = System.Guid.NewGuid(),
            TargetAvatarId = "avtr_test",
            ChannelPointRules = new List<PersistedTriggerRule>
            {
                new PersistedTriggerRule { Name = "cp-rule" }
            },
            BitsRules = new List<PersistedTriggerRule>
            {
                new PersistedTriggerRule { Name = "bits-rule" }
            },
            SubsRules = new List<PersistedTriggerRule>
            {
                new PersistedTriggerRule { Name = "subs-rule" }
            },
            RouletteRules = new List<PersistedTriggerRule>
            {
                new PersistedTriggerRule { Name = "roulette-rule" }
            }
        };

        AvatarSwapMigrationService.MigrateV5ToV6(profile);

        Assert.False(profile.ChannelPointRules[0].AddBitsToSwapTime);
        Assert.False(profile.BitsRules[0].AddBitsToSwapTime);
        Assert.False(profile.SubsRules[0].AddBitsToSwapTime);
        Assert.False(profile.RouletteRules[0].AddBitsToSwapTime);
    }
}
```

Note: if the migration method `MigrateV5ToV6` is private (matching the existing V4→V5 style), the test cannot call it directly. In that case, the test needs to go through a public entry point that runs all migrations. Look at the V5 test class to see how it invokes the migration chain.

- [ ] **Step 3: Run the test**

Run:
```bash
dotnet test "E:\!!!Program to work on\Proper Crystal Relay\VrcTwitchOscBridge.Tests\VrcTwitchOscBridge.Tests.csproj" --filter "FullyQualifiedName~AvatarSwapMigrationServiceV6Tests" --no-restore
```

Expected: the test passes (or, if the V5 method is private and the public entry point is used, the test still passes because the V6 migration runs as part of the chain).

- [ ] **Step 4: Add the new test file to the csproj if needed**

If the test csproj does not auto-include the new file, add it. Most test projects in this repo do auto-include (default items enabled). Check the `VrcTwitchOscBridge.Tests.csproj` to confirm.

- [ ] **Step 5: Commit**

```bash
git add VrcTwitchOscBridge.Tests/AvatarSwapMigrationServiceV6Tests.cs
git commit -m "test: cover V5->V6 migration default for AddBitsToSwapTime"
```

---

## Task 10: Add the new checkbox in the per-avatar Bits section

**Files:**
- Modify: `VrcTwitchOscBridge/UserControls/InlineRuleEditorControl.xaml`

- [ ] **Step 1: Add the checkbox at the top of the Bits Settings border**

In `UserControls/InlineRuleEditorControl.xaml`, find the Bits section (around line 162-210). Inside the `<Border ...>` for "Bits Settings" (around line 175), insert a new `CheckBox` as the first child of the inner `StackPanel`, right before the existing `UniformGrid` that contains "Minimum Amount" and the Bits/Seconds inputs:

```xml
            <CheckBox Content="{loc:Translate 'Add bits time to swap'}"
                      IsChecked="{Binding Rule.AddBitsToSwapTime, UpdateSourceTrigger=PropertyChanged}"
                      Margin="0,0,0,8" />
```

The exact indentation matches the surrounding elements. Verify with a `Read` of the file first to see the indentation style.

- [ ] **Step 2: Build to verify**

Run:
```bash
dotnet build "E:\!!!Program to work on\Proper Crystal Relay\VrcTwitchOscBridge\VrcTwitchOscBridge.csproj" --no-restore
```

Expected: build succeeds.

- [ ] **Step 3: Commit**

```bash
git add VrcTwitchOscBridge/UserControls/InlineRuleEditorControl.xaml
git commit -m "feat(avatar-swap): add Add bits time to swap checkbox in Bits section"
```

---

## Task 11: Add the new checkbox in the per-avatar Subs section

**Files:**
- Modify: `VrcTwitchOscBridge/UserControls/InlineRuleEditorControl.xaml`

- [ ] **Step 1: Add the checkbox at the top of the Subs Settings border**

Same as Task 10 but for the Subs section. Find the Subs border (around line 213). Insert the same `CheckBox` as the first child of the inner `StackPanel`, right before the existing T1/T2/T3 content.

- [ ] **Step 2: Build to verify**

Run:
```bash
dotnet build "E:\!!!Program to work on\Proper Crystal Relay\VrcTwitchOscBridge\VrcTwitchOscBridge.csproj" --no-restore
```

Expected: build succeeds.

- [ ] **Step 3: Commit**

```bash
git add VrcTwitchOscBridge/UserControls/InlineRuleEditorControl.xaml
git commit -m "feat(avatar-swap): add Add bits time to swap checkbox in Subs section"
```

---

## Task 12: Add the new English localization key

**Files:**
- Modify: `VrcTwitchOscBridge/Resources/Localization/en-US.json`

- [ ] **Step 1: Add the new key**

In `Resources/Localization/en-US.json`, add a new line (the order doesn't matter; pick a stable spot near the other Bits/Subs keys):

```json
  "Add bits time to swap": "Add bits time to swap",
```

- [ ] **Step 2: Build to verify**

Run:
```bash
dotnet build "E:\!!!Program to work on\Proper Crystal Relay\VrcTwitchOscBridge\VrcTwitchOscBridge.csproj" --no-restore
```

Expected: build succeeds (the build does not validate localization key coverage; the audit in Task 14 does).

- [ ] **Step 3: Commit**

```bash
git add VrcTwitchOscBridge/Resources/Localization/en-US.json
git commit -m "i18n(en): add 'Add bits time to swap' key"
```

---

## Task 13: Update the 13 non-English locale files

**Files:**
- Modify: 13 non-English `*.json` files in `VrcTwitchOscBridge/Resources/Localization/`

- [ ] **Step 1: Add the new key to each locale file with a translation**

For each of `de-DE.json`, `es-ES.json`, `fr-FR.json`, `it-IT.json`, `ja-JP.json`, `ko-KR.json`, `pl-PL.json`, `pt-BR.json`, `ru-RU.json`, `sv-SE.json`, `th-TH.json`, `zh-CN.json`, `zh-TW.json`, add a new line:

- `de-DE.json`: `"Add bits time to swap": "Bits-Zeit zum Wechsel hinzufügen",`
- `es-ES.json`: `"Add bits time to swap": "Añadir tiempo de bits al cambio",`
- `fr-FR.json`: `"Add bits time to swap": "Ajouter du temps de bits au changement",`
- `it-IT.json`: `"Add bits time to swap": "Aggiungi tempo di bits al cambio",`
- `ja-JP.json`: `"Add bits time to swap": "スワップにBitsの時間を追加",`
- `ko-KR.json`: `"Add bits time to swap": "교환에 Bits 시간 추가",`
- `pl-PL.json`: `"Add bits time to swap": "Dodaj czas z bits do zmiany",`
- `pt-BR.json`: `"Add bits time to swap": "Adicionar tempo de bits à troca",`
- `ru-RU.json`: `"Add bits time to swap": "Добавить время от bits к переключению",`
- `sv-SE.json`: `"Add bits time to swap": "Lägg till bits-tid till bytet",`
- `th-TH.json`: `"Add bits time to swap": "เพิ่มเวลาจาก Bits ให้การสลับ",`
- `zh-CN.json`: `"Add bits time to swap": "将 Bits 时间加到切换中",`
- `zh-TW.json`: `"Add bits time to swap": "將 Bits 時間加到切換中",`

Use PowerShell to do this in one batch:

```powershell
$translations = @{
  "de-DE" = "Bits-Zeit zum Wechsel hinzufügen"
  "es-ES" = "Añadir tiempo de bits al cambio"
  "fr-FR" = "Ajouter du temps de bits au changement"
  "it-IT" = "Aggiungi tempo di bits al cambio"
  "ja-JP" = "スワップにBitsの時間を追加"
  "ko-KR" = "교환에 Bits 시간 추가"
  "pl-PL" = "Dodaj czas z bits do zmiany"
  "pt-BR" = "Adicionar tempo de bits à troca"
  "ru-RU" = "Добавить время от bits к переключению"
  "sv-SE" = "Lägg till bits-tid till bytet"
  "th-TH" = "เพิ่มเวลาจาก Bits ให้การสลับ"
  "zh-CN" = "将 Bits 时间加到切换中"
  "zh-TW" = "將 Bits 時間加到切換中"
}
foreach ($k in $translations.Keys) {
  $file = "E:\!!!Program to work on\Proper Crystal Relay\VrcTwitchOscBridge\Resources\Localization\$k.json"
  $content = Get-Content -LiteralPath $file -Raw -Encoding UTF8
  $newLine = "  `"Add bits time to swap`": `"$($translations[$k])`","
  if (-not $content.Contains('"Add bits time to swap"')) {
    $updated = $content -replace '(\r?\n)(}\s*)$', "`r`n$newLine`$2"
    Set-Content -LiteralPath $file -Value $updated -Encoding UTF8 -NoNewline
    Write-Host "Updated: $k"
  } else {
    Write-Host "Skipped (already has key): $k"
  }
}
```

If the file format or line-ending style is different, the regex may need adjustment. Verify by reading 2-3 of the resulting files.

- [ ] **Step 2: Commit all locale files in one commit**

```bash
git add VrcTwitchOscBridge/Resources/Localization/*.json
git commit -m "i18n: add 'Add bits time to swap' key in all locales"
```

---

## Task 14: Run the localization audit

**Files:**
- Run: `LocalizationAudit` project

- [ ] **Step 1: Run the audit**

```bash
dotnet run --project "E:\!!!Program to work on\Proper Crystal Relay\LocalizationAudit" -- "E:\!!!Program to work on\Proper Crystal Relay\VrcTwitchOscBridge\Resources\Localization"
```

Expected: the audit does not report any new missing keys for `"Add bits time to swap"`. The pre-existing issues (39 missing keys, ~80-200+ untranslated values per locale) are unchanged and out of scope.

If the audit reports a missing key for `"Add bits time to swap"`, fix the affected locale file and re-run.

- [ ] **Step 2: Commit if any fixes were needed**

```bash
git add VrcTwitchOscBridge/Resources/Localization/
git commit -m "i18n: fix localization audit findings"
```

If no fixes were needed, skip this step.

---

## Task 15: Update `CHANGELOG.txt`

**Files:**
- Modify: `CHANGELOG.txt`

- [ ] **Step 1: Add the new bullet**

In `CHANGELOG.txt`, find the `v3.1.9 beta 4` section (around line 20-29). Append a new bullet to that section, after the existing two bullets from the previous task:

```text
- Added: an "Add bits time to swap" option on Bits and Subs triggers inside the Avatar Swap manager. When enabled, every matching trigger extends the current avatar swap by Active Time + the scaled amount. When disabled, only the fixed Active Time runs. The option is independent of the existing "Add amount to active time" toggle and works with or without a required chat keyword.
```

- [ ] **Step 2: Commit**

```bash
git add CHANGELOG.txt
git commit -m "docs(changelog): add per-avatar Add bits time to swap notes to 3.1.9 beta 4"
```

---

## Task 16: Update `RELEASE-CHANGE-RECORD.txt`

**Files:**
- Modify: `RELEASE-CHANGE-RECORD.txt`

- [ ] **Step 1: Add the new bullet to the `v3.1.9 beta 4 (in progress)` section**

In `RELEASE-CHANGE-RECORD.txt`, find the `v3.1.9 beta 4 (in progress)` block. Add a new bullet to the `Changed:` list (after the previous task's two bullets):

```text
- Added "Add bits time to swap" toggle in the per-avatar Bits and Subs settings of the Avatar Swap manager. The toggle is independent of the global "Add amount to active time" toggle and controls whether bits and subs events extend the running avatar swap (when on) or only the fixed Active Time runs (when off).
```

- [ ] **Step 2: Commit**

```bash
git add RELEASE-CHANGE-RECORD.txt
git commit -m "docs(release-record): add per-avatar Add bits time to swap notes to 3.1.9 beta 4"
```

---

## Task 17: Final build and test verification

**Files:**
- Run: full build + test suite

- [ ] **Step 1: Full build**

```bash
dotnet build "E:\!!!Program to work on\Proper Crystal Relay\VrcTwitchOscBridge\VrcTwitchOscBridge.csproj" --no-restore
```

Expected: build succeeds with 0 errors.

- [ ] **Step 2: Full test run**

```bash
dotnet test "E:\!!!Program to work on\Proper Crystal Relay\VrcTwitchOscBridge.Tests\VrcTwitchOscBridge.Tests.csproj" --no-restore
```

Expected: all tests pass, including the 5 new `SupportOverrideDurationMathTests`, the 2 new `InlineRuleRowViewModelTests`, and the 1 new V6 migration test. Total: 163 + 8 = 171 tests passing (the previous total was 163; 8 new tests added).

- [ ] **Step 3: Verify no uncommitted changes remain**

```bash
git status
```

Expected: clean working tree. If any files appear (other than the pre-existing untracked `!start-server.sh - Shortcut.lnk` etc.), address them before declaring done.

- [ ] **Step 4: Report completion**

Print a one-line summary to the user:

```
Done. Last stable: 3.1.8; in-progress: 3.1.9 beta 4. Per-avatar "Add bits time to swap" toggle added to Bits and Subs triggers in the Avatar Swap manager. Independent of the global "Add amount to active time" toggle. Tests green. No push (dev mode).
```

---

## Out of scope (do not do in this plan)

- A separate "Replace" mode (the old "scale" behavior where bits replace the active time rather than adding to it). Streamers who want the old "replace" behavior can keep `AddBitsToSwapTime = false`.
- A per-rule option for the global override context. The global override's `AmountScaledDurationEnabled` is unchanged.
- Refactoring the cap logic, the queue path, or the per-action reset scheduling.
- Renaming `AmountScaledDurationEnabled`.
- Updates to the legacy `AvatarSwapRuleEditorControl` XAML that the main window's Redeem Editor used before the v3.1.9 beta 4 rework. That editor is no longer wired into the UI.
- Adding a new row summary chip to the per-avatar Channel Point or Payment rule row VMs.
- Renaming the `AmountScaledDurationEnabled` field anywhere.
- Updating `README.md` (beta builds do not update highlights per AGENTS rules).
- Updating the Void Crystal website (the download URL tracks stable only).
- Pushing to the public or private GitHub repos. This is dev mode; no pushes.
- Building or publishing a beta 4 test package or release package. The user will ask for that explicitly when ready.
