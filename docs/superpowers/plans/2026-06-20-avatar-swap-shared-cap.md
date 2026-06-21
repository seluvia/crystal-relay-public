# Per-Avatar Avatar Swap: Shared Cap and Subs Toggle Rename Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Move the per-avatar "Cap max accumulated duration" from individual `TriggerRule`s to the parent `AvatarSwapProfile` so Bits and Subs editors in the same avatar share a single cap. Rename the Subs toggle to "Add sub time to swap" and add the cap section to the Subs editor. Hide T1/T2/T3 inputs when the Subs toggle is off.

**Architecture:** Two new fields (`MaxSwapTimeEnabled`, `MaxSwapTimeSeconds`) on `AvatarSwapProfile` and its snapshot/DAO. A new `Profile` dependency property on `InlineRuleEditorControl` so both Bits and Subs editors bind to the same profile cap. The runtime uses the profile's cap for per-avatar rules via `FindAvatarSwapProfileForRule`; the per-rule cap stays for the global override context. A new migration V6→V7.

**Tech Stack:** C# .NET 10, WPF, xUnit, JSON localization files.

**Working directory:** `E:\!!!Program to work on\Proper Crystal Relay`

**Build/test commands:**
- Build app: `dotnet build "E:\!!!Program to work on\Proper Crystal Relay\VrcTwitchOscBridge\VrcTwitchOscBridge.csproj" --no-restore`
- Build + run tests: `dotnet test "E:\!!!Program to work on\Proper Crystal Relay\VrcTwitchOscBridge.Tests\VrcTwitchOscBridge.Tests.csproj"`
- Localization audit: `dotnet run --project "E:\!!!Program to work on\Proper Crystal Relay\LocalizationAudit" -- "E:\!!!Program to work on\Proper Crystal Relay\VrcTwitchOscBridge\Resources\Localization"`

---

## File structure

**New files:**
- `VrcTwitchOscBridge.Tests/SupportOverrideCapClampTests.cs` - tests for the new clamp function signature.
- `VrcTwitchOscBridge.Tests/AvatarSwapMigrationServiceV7Tests.cs` - migration test for V6→V7.

**Modified files:**
- `VrcTwitchOscBridge/Models/AvatarSwapProfile.cs` - two new properties.
- `VrcTwitchOscBridge/Services/BridgeRuntimeConfiguration.cs` - snapshot gets two new positional parameters.
- `VrcTwitchOscBridge/Services/SettingsStore.cs` - DTO gets two new fields; round-trip mapping.
- `VrcTwitchOscBridge/Services/AvatarSwapMigrationService.cs` - bump to v7; add `MigrateV6ToV7`.
- `VrcTwitchOscBridge/Services/BridgeCoordinator.cs` - `ClampSupporterOverrideAddedDuration` gets two new parameters; new `GetOverrideCap` helper; call site updates.
- `VrcTwitchOscBridge/UserControls/InlineRuleEditorControl.xaml` - Bits cap rebinds; Subs toggle rename, visibility, cap section.
- `VrcTwitchOscBridge/UserControls/InlineRuleEditorControl.xaml.cs` - new `Profile` dependency property.
- `VrcTwitchOscBridge/ViewModels/AvatarSwapManagerViewModel.cs` - pass the parent profile to the inline editor.
- `VrcTwitchOscBridge/Resources/Localization/en-US.json` - 1 new English key.
- 13 non-English `*.json` files - matching translations.
- `CHANGELOG.txt` - 2 new bullets in `v3.1.9 beta 4` section.
- `RELEASE-CHANGE-RECORD.txt` - 2 new bullets in `v3.1.9 beta 4 (in progress)` section.

The csproj has `EnableDefaultCompileItems=false` so any new `.cs` file must be explicitly added to `VrcTwitchOscBridge.csproj` under the appropriate `<ItemGroup>`. The new test files are added to `VrcTwitchOscBridge.Tests.csproj` which has default item inclusion.

---

## Task 1: Add profile cap fields to `AvatarSwapProfile`

**Files:**
- Modify: `VrcTwitchOscBridge/Models/AvatarSwapProfile.cs`

- [ ] **Step 1: Add the two new properties**

Open `Models/AvatarSwapProfile.cs`. Add two new auto-properties right after the existing `IsEnabled` property (around line 13):

```csharp
    public bool MaxSwapTimeEnabled { get; set; } = false;
    public int MaxSwapTimeSeconds { get; set; } = 1800;
```

These follow the same pattern as `IsEnabled` (auto-property with default). The class already extends `ObservableObject` but these two properties don't need to raise change notifications because the editor reads them via direct binding; the change comes through when the editor's `Profile` dependency property is updated. If the existing `IsEnabled` property raises change notifications via `SetProperty`, match that pattern; otherwise auto-properties are fine.

- [ ] **Step 2: Build to verify**

Run:
```bash
dotnet build "E:\!!!Program to work on\Proper Crystal Relay\VrcTwitchOscBridge\VrcTwitchOscBridge.csproj" --no-restore
```

Expected: build succeeds.

- [ ] **Step 3: Commit**

```bash
git add VrcTwitchOscBridge/Models/AvatarSwapProfile.cs
git commit -m "feat(avatar-swap): add MaxSwapTime fields to AvatarSwapProfile"
```

---

## Task 2: Add profile cap fields to `AvatarSwapProfileSnapshot`

**Files:**
- Modify: `VrcTwitchOscBridge/Services/BridgeRuntimeConfiguration.cs`

- [ ] **Step 1: Add the two new positional parameters to the record**

Open `Services/BridgeRuntimeConfiguration.cs`. Find the `AvatarSwapProfileSnapshot` record (around line 300-309). Replace it with:

```csharp
public sealed record AvatarSwapProfileSnapshot(
    Guid Id,
    string TargetAvatarId,
    string TargetAvatarName,
    string? TargetThumbnailUrl,
    bool IsEnabled,
    bool MaxSwapTimeEnabled,
    int MaxSwapTimeSeconds,
    IReadOnlyList<TriggerRuleSnapshot> ChannelPointRules,
    IReadOnlyList<TriggerRuleSnapshot> BitsRules,
    IReadOnlyList<TriggerRuleSnapshot> SubsRules,
    IReadOnlyList<TriggerRuleSnapshot> PaymentRules);
```

The two new positional parameters (`MaxSwapTimeEnabled`, `MaxSwapTimeSeconds`) go between `IsEnabled` and the rule collections.

- [ ] **Step 2: Update the snapshot constructor call**

Find the `new AvatarSwapProfileSnapshot(...)` call (around line 505). The call site constructs the snapshot from a `PersistedAvatarSwapProfile`. Add the two new fields to the call. Read the surrounding code to see the existing pattern, then add:

```csharp
                MaxSwapTimeEnabled = profile.MaxSwapTimeEnabled,
                MaxSwapTimeSeconds = profile.MaxSwapTimeSeconds,
```

The exact placement is right after the existing `IsEnabled = profile.IsEnabled,` line. Use `grep` to find all call sites that construct an `AvatarSwapProfileSnapshot` and update each one.

- [ ] **Step 3: Build to verify**

Run:
```bash
dotnet build "E:\!!!Program to work on\Proper Crystal Relay\VrcTwitchOscBridge\VrcTwitchOscBridge.csproj" --no-restore
```

Expected: build fails with errors about missing positional arguments at every `AvatarSwapProfileSnapshot` call site. This is the expected red state for the intermediate build.

- [ ] **Step 4: Commit**

```bash
git add VrcTwitchOscBridge/Services/BridgeRuntimeConfiguration.cs
git commit -m "feat(avatar-swap): add MaxSwapTime fields to AvatarSwapProfileSnapshot"
```

---

## Task 3: Add profile cap fields to `PersistedAvatarSwapProfile` and the round-trip mapping

**Files:**
- Modify: `VrcTwitchOscBridge/Services/SettingsStore.cs`

- [ ] **Step 1: Add the two new fields to the DTO**

In `Services/SettingsStore.cs`, find `PersistedAvatarSwapProfile` (around line 3074-3096). Add two new properties after `IsEnabled`:

```csharp
        public bool MaxSwapTimeEnabled { get; set; }
        public int MaxSwapTimeSeconds { get; set; }
```

- [ ] **Step 2: Update `ToPersistedProfile` (if it exists) and `ToProfile`**

Find the methods that convert between `PersistedAvatarSwapProfile` and `AvatarSwapProfile`. Use `grep` to locate them. In each method, add the two new field assignments. For `ToPersistedProfile` (model -> DTO):

```csharp
                MaxSwapTimeEnabled = profile.MaxSwapTimeEnabled,
                MaxSwapTimeSeconds = profile.MaxSwapTimeSeconds,
```

For `ToProfile` (DTO -> model):

```csharp
                MaxSwapTimeEnabled = persisted.MaxSwapTimeEnabled,
                MaxSwapTimeSeconds = persisted.MaxSwapTimeSeconds,
```

- [ ] **Step 3: Build to verify**

Run:
```bash
dotnet build "E:\!!!Program to work on\Proper Crystal Relay\VrcTwitchOscBridge\VrcTwitchOscBridge.csproj" --no-restore
```

Expected: build succeeds.

- [ ] **Step 4: Commit**

```bash
git add VrcTwitchOscBridge/Services/SettingsStore.cs
git commit -m "feat(avatar-swap): persist MaxSwapTime fields in SettingsStore"
```

---

## Task 4: Bump migration to v7 and add V6→V7

**Files:**
- Modify: `VrcTwitchOscBridge/Services/AvatarSwapMigrationService.cs`

- [ ] **Step 1: Bump the version constant**

In `Services/AvatarSwapMigrationService.cs`, find `CurrentMigrationVersion = 6` (line 9). Change to:

```csharp
    public const int CurrentMigrationVersion = 7;
```

- [ ] **Step 2: Add the V6→V7 method**

Find the `MigrateV5ToV6` method (the one added in the previous task). Add a new `MigrateV6ToV7` method right after it:

```csharp
    private static void MigrateV6ToV7(AppSettings settings)
    {
        // V6->V7: AvatarSwapProfile gained a shared "MaxSwapTime" cap field for the
        // per-avatar Bits and Subs editors. The field defaults to disabled (false) and
        // 1800 seconds on the model. Legacy saves load with the new field set to these
        // defaults automatically. No data transformation is needed; this step simply
        // bumps the version.
    }
```

- [ ] **Step 3: Wire the migration into the chain**

Find the migration chain (around line 20-40). Add a new `if` block right after the V5→V6 check:

```csharp
        if (settings.AvatarChangeToAvatarSwapMigrationVersion < 7)
        {
            MigrateV6ToV7(settings);
        }
```

- [ ] **Step 4: Build to verify**

Run:
```bash
dotnet build "E:\!!!Program to work on\Proper Crystal Relay\VrcTwitchOscBridge\VrcTwitchOscBridge.csproj" --no-restore
```

Expected: build succeeds.

- [ ] **Step 5: Commit**

```bash
git add VrcTwitchOscBridge/Services/AvatarSwapMigrationService.cs
git commit -m "feat(avatar-swap): bump migration to v7"
```

---

## Task 5: Add the V7 migration test

**Files:**
- Create: `VrcTwitchOscBridge.Tests/AvatarSwapMigrationServiceV7Tests.cs`

- [ ] **Step 1: Write the test**

Create `VrcTwitchOscBridge.Tests/AvatarSwapMigrationServiceV7Tests.cs`:

```csharp
using VrcTwitchOscBridge.Services;
using Xunit;

namespace VrcTwitchOscBridge.Tests;

public sealed class AvatarSwapMigrationServiceV7Tests
{
    [Fact]
    public void CurrentMigrationVersion_IsAtLeast7()
    {
        Assert.True(AvatarSwapMigrationService.CurrentMigrationVersion >= 7);
    }
}
```

- [ ] **Step 2: Run the test**

Run:
```bash
dotnet test "E:\!!!Program to work on\Proper Crystal Relay\VrcTwitchOscBridge.Tests\VrcTwitchOscBridge.Tests.csproj" --filter "FullyQualifiedName~AvatarSwapMigrationServiceV7Tests" --no-restore
```

Expected: test passes.

- [ ] **Step 3: Commit**

```bash
git add VrcTwitchOscBridge.Tests/AvatarSwapMigrationServiceV7Tests.cs
git commit -m "test: cover V6->V7 migration version bump"
```

---

## Task 6: Add failing tests for the new clamp function signature

**Files:**
- Create: `VrcTwitchOscBridge.Tests/SupportOverrideCapClampTests.cs`

- [ ] **Step 1: Write the failing tests**

The `ClampSupporterOverrideAddedDuration` function in `BridgeCoordinator.cs` is private static. The new tests need to test the runtime behavior. Since the function is private, the tests verify behavior through the public path (HandleTimedSupporterOverrideTriggerAsync) OR by extracting the cap logic into a testable helper.

The simplest path: extract a small `GetOverrideCap(rule, profile)` helper that returns `(bool enabled, int seconds)`, make it `internal static` (or `public static` if needed), and test it directly.

For now, the failing tests target the existing `ClampSupporterOverrideAddedDuration` which currently uses the rule's cap. The new tests verify that the new behavior - accepting cap from the profile - works. Write the tests to assert the EXISTING behavior first (which is the per-rule cap), then the new behavior in Task 7 will be to switch to the profile's cap.

Actually, a cleaner approach: the new clamp function signature has two new parameters `(bool capEnabled, int capSeconds)`. The tests verify that the new parameters are used. To make this testable without exposing the private function, extract the clamp logic into a public testable helper.

Create `VrcTwitchOscBridge.Tests/SupportOverrideCapClampTests.cs`:

```csharp
using VrcTwitchOscBridge.Services;
using VrcTwitchOscBridge.Services.Support;
using Xunit;

namespace VrcTwitchOscBridge.Tests;

public sealed class SupportOverrideCapClampTests
{
    [Fact]
    public void ClampWithProfileCapEnabled_At1800_AddsRequested()
    {
        var requested = TimeSpan.FromSeconds(34);
        var existing = TimeSpan.FromSeconds(1750);
        var result = SupportOverrideCapMath.ClampAddedDuration(
            capEnabled: true,
            capSeconds: 1800,
            requestedDuration: requested,
            existingRemainingDuration: existing);
        Assert.Equal(TimeSpan.FromSeconds(34), result);
    }

    [Fact]
    public void ClampWithProfileCapEnabled_ClampsToRemainingCapacity()
    {
        var requested = TimeSpan.FromSeconds(34);
        var existing = TimeSpan.FromSeconds(1790);
        var result = SupportOverrideCapMath.ClampAddedDuration(
            capEnabled: true,
            capSeconds: 1800,
            requestedDuration: requested,
            existingRemainingDuration: existing);
        Assert.Equal(TimeSpan.FromSeconds(10), result);
    }

    [Fact]
    public void ClampWithProfileCapDisabled_NoClamp()
    {
        var requested = TimeSpan.FromSeconds(1000);
        var existing = TimeSpan.FromSeconds(500);
        var result = SupportOverrideCapMath.ClampAddedDuration(
            capEnabled: false,
            capSeconds: 1800,
            requestedDuration: requested,
            existingRemainingDuration: existing);
        Assert.Equal(TimeSpan.FromSeconds(1000), result);
    }

    [Fact]
    public void ClampWithZeroRequested_ReturnsZero()
    {
        var result = SupportOverrideCapMath.ClampAddedDuration(
            capEnabled: true,
            capSeconds: 1800,
            requestedDuration: TimeSpan.Zero,
            existingRemainingDuration: TimeSpan.FromSeconds(1750));
        Assert.Equal(TimeSpan.Zero, result);
    }
}
```

- [ ] **Step 2: Run tests to verify they fail**

Run:
```bash
dotnet test "E:\!!!Program to work on\Proper Crystal Relay\VrcTwitchOscBridge.Tests\VrcTwitchOscBridge.Tests.csproj" --filter "FullyQualifiedName~SupportOverrideCapClampTests" --no-restore
```

Expected: build error. `SupportOverrideCapMath` does not exist yet. This is the expected red state for Task 7.

- [ ] **Step 3: Commit the failing tests**

```bash
git add VrcTwitchOscBridge.Tests/SupportOverrideCapClampTests.cs
git commit -m "test: add failing tests for cap clamp helper"
```

---

## Task 7: Extract the cap clamp logic into a testable helper

**Files:**
- Create: `VrcTwitchOscBridge/Services/Support/SupportOverrideCapMath.cs`
- Modify: `VrcTwitchOscBridge/Services/BridgeCoordinator.cs`
- Modify: `VrcTwitchOscBridge/VrcTwitchOscBridge.csproj`

- [ ] **Step 1: Create the new helper file**

Create `VrcTwitchOscBridge/Services/Support/SupportOverrideCapMath.cs`:

```csharp
using System;

namespace VrcTwitchOscBridge.Services.Support;

public static class SupportOverrideCapMath
{
    public static TimeSpan ClampAddedDuration(
        bool capEnabled,
        int capSeconds,
        TimeSpan requestedDuration,
        TimeSpan existingRemainingDuration)
    {
        if (requestedDuration <= TimeSpan.Zero || !capEnabled)
        {
            return requestedDuration;
        }
        var maxAccumulatedDuration = TimeSpan.FromSeconds(Math.Max(1, capSeconds));
        var remainingCapacity = maxAccumulatedDuration - existingRemainingDuration;
        if (remainingCapacity <= TimeSpan.Zero) return TimeSpan.Zero;
        return requestedDuration <= remainingCapacity ? requestedDuration : remainingCapacity;
    }
}
```

- [ ] **Step 2: Add to the csproj**

Open `VrcTwitchOscBridge.csproj`. Find the `<Compile Include>` block. Add a new line for the helper file, in alphabetical order (alphabetical order is the existing convention):

```xml
    <Compile Include="Services\Support\SupportOverrideCapMath.cs" />
```

- [ ] **Step 3: Update `ClampSupporterOverrideAddedDuration` in `BridgeCoordinator.cs`**

Find the `ClampSupporterOverrideAddedDuration` method (line 6517-6537). Replace its body with a delegation to the new helper:

```csharp
    private static TimeSpan ClampSupporterOverrideAddedDuration(
        TriggerRuleSnapshot rule,
        TimeSpan requestedDuration,
        TimeSpan existingRemainingDuration,
        bool capEnabled,
        int capSeconds) =>
        SupportOverrideCapMath.ClampAddedDuration(capEnabled, capSeconds, requestedDuration, existingRemainingDuration);
```

The method signature gains two new parameters. The body is now a one-liner that delegates.

- [ ] **Step 4: Update the call site in `HandleTimedSupporterOverrideTriggerAsync`**

Find the call to `ClampSupporterOverrideAddedDuration` (around line 8510). It currently passes `(rule, requestedDuration, existingRemainingDuration)`. Update it to resolve the cap from the profile (via `FindAvatarSwapProfileForRule`) or fall back to the rule's cap:

```csharp
                var (capEnabled, capSeconds) = ResolveOverrideCap(rule);
                var triggerDuration = ClampSupporterOverrideAddedDuration(rule, requestedDuration, existingRemainingDuration, capEnabled, capSeconds);
```

Add a new private static helper at the bottom of `BridgeCoordinator.cs` (or near the existing cap function):

```csharp
    private static (bool enabled, int seconds) ResolveOverrideCap(TriggerRuleSnapshot rule)
    {
        var configuration = BridgeCoordinatorStatics.CurrentConfiguration;
        var profile = configuration?.FindAvatarSwapProfileForRule(rule.Rule);
        if (profile is not null)
        {
            return (profile.MaxSwapTimeEnabled, profile.MaxSwapTimeSeconds);
        }
        return (rule.MaxAccumulatedDurationEnabled, rule.MaxAccumulatedDurationSeconds);
    }
```

If `BridgeCoordinatorStatics.CurrentConfiguration` doesn't exist or is named differently, look for the existing pattern in the file (search for `FindAvatarSwapProfileForRule` usage to see how the current configuration is accessed). Adapt the helper to match the actual pattern.

- [ ] **Step 5: Update the bot message line**

Find the line around 8507 that uses `rule.MaxAccumulatedDurationSeconds` for the bot message text:

```csharp
                DescribeDuration(Math.Max(1, rule.MaxAccumulatedDurationSeconds)))));
```

Update to use the resolved cap from the profile:

```csharp
                DescribeDuration(Math.Max(1, ResolveOverrideCap(rule).seconds))));
```

If this is in a different position or uses a different variable name, match the existing style. The goal is to use the profile's cap for the bot message, not the rule's.

- [ ] **Step 6: Build to verify**

Run:
```bash
dotnet build "E:\!!!Program to work on\Proper Crystal Relay\VrcTwitchOscBridge\VrcTwitchOscBridge.csproj" --no-restore
```

Expected: build succeeds.

- [ ] **Step 7: Run the new tests**

Run:
```bash
dotnet test "E:\!!!Program to work on\Proper Crystal Relay\VrcTwitchOscBridge.Tests\VrcTwitchOscBridge.Tests.csproj" --filter "FullyQualifiedName~SupportOverrideCapClampTests" --no-restore
```

Expected: all 4 tests pass.

- [ ] **Step 8: Run the full test suite**

Run:
```bash
dotnet test "E:\!!!Program to work on\Proper Crystal Relay\VrcTwitchOscBridge.Tests\VrcTwitchOscBridge.Tests.csproj" --no-restore
```

Expected: all tests pass.

- [ ] **Step 9: Commit**

```bash
git add VrcTwitchOscBridge/Services/Support/SupportOverrideCapMath.cs VrcTwitchOscBridge/Services/BridgeCoordinator.cs VrcTwitchOscBridge/VrcTwitchOscBridge.csproj
git commit -m "feat(avatar-swap): extract cap clamp helper and use profile cap"
```

---

## Task 8: Add the new English localization key

**Files:**
- Modify: `VrcTwitchOscBridge/Resources/Localization/en-US.json`

- [ ] **Step 1: Add the new key**

Open `Resources/Localization/en-US.json`. Add a new line (near the other Bits/Subs keys):

```json
  "Add sub time to swap": "Add sub time to swap",
```

- [ ] **Step 2: Build to verify**

Run:
```bash
dotnet build "E:\!!!Program to work on\Proper Crystal Relay\VrcTwitchOscBridge\VrcTwitchOscBridge.csproj" --no-restore
```

Expected: build succeeds.

- [ ] **Step 3: Commit**

```bash
git add VrcTwitchOscBridge/Resources/Localization/en-US.json
git commit -m "i18n(en): add 'Add sub time to swap' key"
```

---

## Task 9: Update the 13 non-English locale files

**Files:**
- Modify: 13 non-English `*.json` files in `VrcTwitchOscBridge/Resources/Localization/`

- [ ] **Step 1: Add the new key to each locale file with a translation**

Use PowerShell to add the key to all 13 non-English locale files:

```powershell
$translations = @{
  "de-DE" = "Sub-Zeit zum Wechsel hinzufügen"
  "es-ES" = "Tiempo de sub al cambio"
  "fr-FR" = "Temps de sub au changement"
  "it-IT" = "Tempo di sub al cambio"
  "ja-JP" = "スワップにSubの時間を追加"
  "ko-KR" = "교환에 Sub 시간 추가"
  "pl-PL" = "Dodaj czas z sub do zmiany"
  "pt-BR" = "Tempo de sub à troca"
  "ru-RU" = "Добавить время от sub к переключению"
  "sv-SE" = "Lägg till sub-tid till bytet"
  "th-TH" = "เพิ่มเวลาจาก sub ให้การสลับ"
  "zh-CN" = "将 Sub 时间加到切换中"
  "zh-TW" = "將 Sub 時間加到切換中"
}
foreach ($k in $translations.Keys) {
  $file = "E:\!!!Program to work on\Proper Crystal Relay\VrcTwitchOscBridge\Resources\Localization\$k.json"
  $content = Get-Content -LiteralPath $file -Raw -Encoding UTF8
  $newLine = "  `"Add sub time to swap`": `"$($translations[$k])`","
  if (-not $content.Contains('"Add sub time to swap"')) {
    $updated = $content -replace '(\r?\n)(}\s*)$', "`r`n$newLine`$2"
    Set-Content -LiteralPath $file -Value $updated -Encoding UTF8 -NoNewline
    Write-Host "Updated: $k"
  } else {
    Write-Host "Skipped (already has key): $k"
  }
}
```

- [ ] **Step 2: Commit all locale files in one commit**

```bash
git add VrcTwitchOscBridge/Resources/Localization/*.json
git commit -m "i18n: add 'Add sub time to swap' key in all locales"
```

---

## Task 10: Add the `Profile` dependency property to `InlineRuleEditorControl`

**Files:**
- Modify: `VrcTwitchOscBridge/UserControls/InlineRuleEditorControl.xaml.cs`

- [ ] **Step 1: Add the dependency property**

Open `UserControls/InlineRuleEditorControl.xaml.cs`. Find the class declaration (it should be a `UserControl` subclass). Add the new dependency property. Read the file first to see the existing pattern, then add at the top of the class:

```csharp
    public static readonly DependencyProperty ProfileProperty = DependencyProperty.Register(
        nameof(Profile),
        typeof(AvatarSwapProfile),
        typeof(InlineRuleEditorControl),
        new PropertyMetadata(null));

    public AvatarSwapProfile? Profile
    {
        get => (AvatarSwapProfile?)GetValue(ProfileProperty);
        set => SetValue(ProfileProperty, value);
    }
```

Make sure `using VrcTwitchOscBridge.Models;` is at the top of the file (it should already be, but verify).

- [ ] **Step 2: Build to verify**

Run:
```bash
dotnet build "E:\!!!Program to work on\Proper Crystal Relay\VrcTwitchOscBridge\VrcTwitchOscBridge.csproj" --no-restore
```

Expected: build succeeds.

- [ ] **Step 3: Commit**

```bash
git add VrcTwitchOscBridge/UserControls/InlineRuleEditorControl.xaml.cs
git commit -m "feat(avatar-swap): add Profile dependency property to inline editor"
```

---

## Task 11: Pass the profile to the editor from the manager

**Files:**
- Modify: `VrcTwitchOscBridge/ViewModels/AvatarSwapManagerViewModel.cs`

- [ ] **Step 1: Find the editor construction site**

Run:
```bash
Select-String -Path "E:\!!!Program to work on\Proper Crystal Relay\VrcTwitchOscBridge\ViewModels\AvatarSwapManagerViewModel.cs" -Pattern "InlineRuleEditorControl" -List
```

This finds the place where the manager creates the inline editor. Read the surrounding code.

- [ ] **Step 2: Set the Profile property on the editor**

Wherever the manager creates the `InlineRuleEditorControl`, set its `Profile` property to the parent `AvatarSwapProfile`. The exact pattern depends on the existing code; the most common pattern is:

```csharp
            var editor = new InlineRuleEditorControl
            {
                DataContext = rule,
                Profile = parentProfile
            };
```

If the editor is created via a different mechanism (e.g., a factory, a DataTemplate), the implementation differs. The goal is to make sure the parent profile is passed to the editor.

- [ ] **Step 3: Build to verify**

Run:
```bash
dotnet build "E:\!!!Program to work on\Proper Crystal Relay\VrcTwitchOscBridge\VrcTwitchOscBridge.csproj" --no-restore
```

Expected: build succeeds.

- [ ] **Step 4: Commit**

```bash
git add VrcTwitchOscBridge/ViewModels/AvatarSwapManagerViewModel.cs
git commit -m "feat(avatar-swap): pass parent profile to inline editor"
```

---

## Task 12: Update Bits section cap bindings to profile

**Files:**
- Modify: `VrcTwitchOscBridge/UserControls/InlineRuleEditorControl.xaml`

- [ ] **Step 1: Find and rebind the Bits cap section**

Open `UserControls/InlineRuleEditorControl.xaml`. Find the Bits section's "Cap max accumulated duration" CheckBox and TextBox (around line 201-204). They currently bind to `Rule.MaxAccumulatedDurationEnabled` and `Rule.MaxAccumulatedDurationSeconds`.

Change the CheckBox from:
```xml
<CheckBox IsChecked="{Binding Rule.MaxAccumulatedDurationEnabled}" Content="Cap max accumulated duration" Margin="0,8,0,0" />
```

To:
```xml
<CheckBox IsChecked="{Binding Profile.MaxSwapTimeEnabled}" Content="Cap max accumulated duration" Margin="0,8,0,0" />
```

Change the TextBox from:
```xml
<TextBox Text="{Binding Rule.MaxAccumulatedDurationSeconds, UpdateSourceTrigger=PropertyChanged}" />
```

To:
```xml
<TextBox Text="{Binding Profile.MaxSwapTimeSeconds, UpdateSourceTrigger=PropertyChanged}" />
```

- [ ] **Step 2: Build to verify**

Run:
```bash
dotnet build "E:\!!!Program to work on\Proper Crystal Relay\VrcTwitchOscBridge\VrcTwitchOscBridge.csproj" --no-restore
```

Expected: build succeeds (the bindings reference `Profile` which is the new dependency property).

- [ ] **Step 3: Commit**

```bash
git add VrcTwitchOscBridge/UserControls/InlineRuleEditorControl.xaml
git commit -m "feat(avatar-swap): rebind Bits cap section to profile"
```

---

## Task 13: Rename Subs toggle, add visibility, add cap section

**Files:**
- Modify: `VrcTwitchOscBridge/UserControls/InlineRuleEditorControl.xaml`

- [ ] **Step 1: Rename the Subs toggle label**

Find the Subs section's "Add bits time to swap" CheckBox. Change the `Content`:

```xml
<CheckBox Content="{loc:Translate 'Add bits time to swap'}" ... />
```

To:

```xml
<CheckBox Content="{loc:Translate 'Add sub time to swap'}" ... />
```

- [ ] **Step 2: Wrap the T1/T2/T3 inputs in a visibility binding**

Find the T1/T2/T3 UniformGrid and the "Every this many bits/subs" caption. Wrap them in a `StackPanel` with a `Visibility` binding to `Rule.AddBitsToSwapTime` using the `BoolToVisibilityConverter`.

The exact XAML depends on the current structure. Read the Subs section first to see the indentation. The wrapping looks like:

```xml
<StackPanel Visibility="{Binding Rule.AddBitsToSwapTime, Converter={StaticResource BoolToVisibilityConverter}}">
    <!-- existing T1/T2/T3 UniformGrid and caption here -->
</StackPanel>
```

- [ ] **Step 3: Add the cap section to the Subs editor**

After the T1/T2/T3 block (and before the "Include gift subs" CheckBox), add a new cap section. The XAML matches the Bits cap section but binds to `Profile`:

```xml
<CheckBox IsChecked="{Binding Profile.MaxSwapTimeEnabled}" Content="Cap max accumulated duration" Margin="0,8,0,0" />
<StackPanel Visibility="{Binding Profile.MaxSwapTimeEnabled, Converter={StaticResource BoolToVisibilityConverter}}" Margin="20,4,0,0">
    <TextBox Text="{Binding Profile.MaxSwapTimeSeconds, UpdateSourceTrigger=PropertyChanged}" />
</StackPanel>
```

- [ ] **Step 4: Build to verify**

Run:
```bash
dotnet build "E:\!!!Program to work on\Proper Crystal Relay\VrcTwitchOscBridge\VrcTwitchOscBridge.csproj" --no-restore
```

Expected: build succeeds.

- [ ] **Step 5: Commit**

```bash
git add VrcTwitchOscBridge/UserControls/InlineRuleEditorControl.xaml
git commit -m "feat(avatar-swap): rename Subs toggle, add visibility, add cap section"
```

---

## Task 14: Update CHANGELOG.txt

**Files:**
- Modify: `CHANGELOG.txt`

- [ ] **Step 1: Add the two bullets**

Open `CHANGELOG.txt`. Find the `v3.1.9 beta 4` section. Append two new bullets after the existing ones:

```text
- Renamed: the per-avatar Subs trigger toggle from "Add bits time to swap" to "Add sub time to swap".
- Added: a shared max-time cap on the per-avatar Avatar Swap manager. The cap is stored on the avatar profile and is shown in both the Bits and Subs rule editors. Editing one updates the other. The T1/T2/T3 inputs in the Subs editor are now hidden when the new toggle is off.
```

- [ ] **Step 2: Commit**

```bash
git add CHANGELOG.txt
git commit -m "docs(changelog): add shared cap and Subs toggle rename notes to 3.1.9 beta 4"
```

---

## Task 15: Update RELEASE-CHANGE-RECORD.txt

**Files:**
- Modify: `RELEASE-CHANGE-RECORD.txt`

- [ ] **Step 1: Add the two bullets**

Open `RELEASE-CHANGE-RECORD.txt`. Find the `v3.1.9 beta 4 (in progress)` section. Append two new bullets:

```text
- Renamed: the per-avatar Subs trigger toggle from "Add bits time to swap" to "Add sub time to swap".
- Added: a shared max-time cap on the per-avatar Avatar Swap manager. The cap is stored on the avatar profile and is shown in both the Bits and Subs rule editors. Editing one updates the other. The T1/T2/T3 inputs in the Subs editor are now hidden when the new toggle is off.
```

- [ ] **Step 2: Commit**

```bash
git add RELEASE-CHANGE-RECORD.txt
git commit -m "docs(release-record): add shared cap and Subs toggle rename notes to 3.1.9 beta 4"
```

---

## Task 16: Final build and test verification

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

Expected: all tests pass, including the 4 new `SupportOverrideCapClampTests` and the 1 new V7 migration test. Previous test count was 173; expected new count is 178.

- [ ] **Step 3: Localization audit**

```bash
dotnet run --project "E:\!!!Program to work on\Proper Crystal Relay\LocalizationAudit" -- "E:\!!!Program to work on\Proper Crystal Relay\VrcTwitchOscBridge\Resources\Localization"
```

Expected: the audit does not report any new missing keys for `"Add sub time to swap"`. Pre-existing issues (39 missing keys, etc.) are unchanged.

- [ ] **Step 4: Verify no uncommitted changes remain**

```bash
git status
```

Expected: clean working tree. If any files appear (other than the pre-existing untracked `!start-server.sh - Shortcut.lnk` etc.), address them.

- [ ] **Step 5: Report completion**

Print a one-line summary to the user:

```
Done. Last stable: 3.1.8; in-progress: 3.1.9 beta 4. Per-avatar shared max-time cap on profile, Subs toggle renamed to "Add sub time to swap", T1/T2/T3 hidden when toggle off. Tests green. No push (dev mode).
```

---

## Out of scope (do not do in this plan)

- Migrating existing per-rule `MaxAccumulatedDuration` values to the profile's new cap field. The profile's cap starts fresh.
- A per-rule cap override. The profile's cap is the only cap in the per-avatar context.
- Removing the per-rule cap from `TriggerRule` entirely. It stays for the global override context.
- Refactoring the cap math, the queue path, or the per-action reset scheduling.
- Renaming any other UI labels.
- Updating `README.md` or the Void Crystal website.
- Pushing to the public or private GitHub repos. This is dev mode; no pushes.
- Building or publishing a beta 4 test package or release package.
