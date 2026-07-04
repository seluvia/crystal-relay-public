# Avatar Scaling Manager Redesign Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Build a dedicated Avatar Scaling manager window with reward-focused organization, a right-side editor, and a shared `Current Max Height Allowed` safety cap used by every Avatar Scaling source.

**Architecture:** Add a shared safety settings model under `AppSettings`, persist it through `SettingsStore`, carry it into runtime snapshots, and clamp all Avatar Scaling sources against it. Add a new WPF manager window/viewmodel that adapts existing `AvatarScaleRule`, `CashPaymentRule`, and `PowerUpRule` objects instead of replacing existing persistence or runtime models.

**Tech Stack:** C# 13 / .NET 10, WPF/XAML, xUnit, Crystal Relay localization JSON, existing `ObservableObject`/`RelayCommand` infrastructure.

**Commit Policy:** This repo's agent rules say not to commit unless the user explicitly asks. The checkpoint steps below include commit commands for human-managed workflows, but automated agents must skip those commit commands unless the user has explicitly requested commits.

---

## File Structure

Create:

- `VrcTwitchOscBridge/Models/AvatarScaleSafetySettings.cs` - shared safety cap model and clamp helpers.
- `VrcTwitchOscBridge/ViewModels/AvatarScalingSourceCardViewModel.cs` - card adapter for master reward, scale rules, cash scaling, and Power Up scaling.
- `VrcTwitchOscBridge/ViewModels/AvatarScalingManagerViewModel.cs` - manager source navigation, card collections, selection, and commands.
- `VrcTwitchOscBridge/AvatarScalingManagerWindow.xaml` - dedicated manager UI with left source nav, main reward list, and right-side editor.
- `VrcTwitchOscBridge/AvatarScalingManagerWindow.xaml.cs` - custom chrome, theme application, and small UI handlers.
- `VrcTwitchOscBridge.Tests/AvatarScaleSafetySettingsTests.cs` - model clamp and migration tests.
- `VrcTwitchOscBridge.Tests/BridgeRuntimeConfigurationAvatarScaleSafetyTests.cs` - runtime snapshot safety tests.
- `VrcTwitchOscBridge.Tests/AvatarScalingManagerViewModelTests.cs` - manager card/source tests.
- `VrcTwitchOscBridge.Tests/AvatarScalingManagerWindowXamlTests.cs` - XAML regression tests for side panel, safety label, and themed inputs.

Modify:

- `VrcTwitchOscBridge/Models/AppSettings.cs` - add `AvatarScaleSafety` property and nested change forwarding.
- `VrcTwitchOscBridge/Services/SettingsStore.cs` - persist/load `AvatarScaleSafety`, derive migration default from existing scale rules.
- `VrcTwitchOscBridge/Services/BridgeRuntimeConfiguration.cs` - add safety snapshot and pass safety into every avatar-scale snapshot path.
- `VrcTwitchOscBridge/Services/BridgeCoordinator.cs` - clamp runtime height sends to the shared safety cap.
- `VrcTwitchOscBridge/ViewModels/MainWindowViewModel.cs` - open the new manager, wire safety changes, keep existing save/sync paths, adjust lockout picker availability.
- `VrcTwitchOscBridge/MainWindow.xaml` - change Avatar Scaling button to open the manager and remove the old inline Avatar Scaling workspace after the manager is working.
- `VrcTwitchOscBridge/MainWindow.xaml.cs` - move or duplicate Avatar Scaling editor handlers into the manager window.
- `VrcTwitchOscBridge/VrcTwitchOscBridge.csproj` - explicit includes for new app files.
- `VrcTwitchOscBridge/Resources/Localization/*.extra.json` - new localized strings.

---

### Task 1: Add Shared Safety Settings Model

**Files:**

- Create: `VrcTwitchOscBridge.Tests/AvatarScaleSafetySettingsTests.cs`
- Create: `VrcTwitchOscBridge/Models/AvatarScaleSafetySettings.cs`
- Modify: `VrcTwitchOscBridge/Models/AppSettings.cs`
- Modify: `VrcTwitchOscBridge/VrcTwitchOscBridge.csproj`

- [ ] **Step 1: Write failing safety model tests**

Create `VrcTwitchOscBridge.Tests/AvatarScaleSafetySettingsTests.cs`:

```csharp
using System.Collections.Generic;
using VrcTwitchOscBridge.Models;
using Xunit;

namespace VrcTwitchOscBridge.Tests;

public sealed class AvatarScaleSafetySettingsTests
{
    [Fact]
    public void Defaults_UseSafeAvatarScaleRange()
    {
        var settings = new AvatarScaleSafetySettings();

        Assert.Equal(AvatarScaleRule.SafeMinimumHeightMeters, settings.CurrentMinimumHeightMeters);
        Assert.Equal(AvatarScaleRule.SafeMaximumHeightMeters, settings.CurrentMaximumHeightMeters);
        Assert.Equal("100m", settings.CurrentMaxHeightAllowedText);
    }

    [Theory]
    [InlineData(double.NaN, 1.6)]
    [InlineData(double.PositiveInfinity, 1.6)]
    [InlineData(0.01, 0.1)]
    [InlineData(2.4, 2.4)]
    [InlineData(500, 100)]
    public void ClampHeight_UsesCurrentRange(double value, double expected)
    {
        var settings = new AvatarScaleSafetySettings();

        Assert.Equal(expected, settings.ClampHeight(value), precision: 3);
    }

    [Fact]
    public void CurrentMaximumHeightMeters_ClampsToAdvancedRangeAndKeepsMinimumBelowMaximum()
    {
        var settings = new AvatarScaleSafetySettings
        {
            CurrentMinimumHeightMeters = 5,
            CurrentMaximumHeightMeters = 2
        };

        Assert.Equal(5, settings.CurrentMinimumHeightMeters);
        Assert.Equal(5, settings.CurrentMaximumHeightMeters);

        settings.CurrentMaximumHeightMeters = 20000;

        Assert.Equal(AvatarScaleRule.AdvancedMaximumHeightMeters, settings.CurrentMaximumHeightMeters);
    }

    [Fact]
    public void FromExistingRules_UsesLargestAdvancedValueAboveSafeDefault()
    {
        var rules = new[]
        {
            new AvatarScaleRule
            {
                AdvancedRangeEnabled = true,
                TargetHeightMeters = 250,
                MaximumHeightMeters = 150,
                RestoreHeightMeters = 1.6
            },
            new AvatarScaleRule
            {
                AdvancedRangeEnabled = false,
                TargetHeightMeters = 500
            }
        };

        var settings = AvatarScaleSafetySettings.FromExistingRules(rules);

        Assert.Equal(250, settings.CurrentMaximumHeightMeters);
    }

    [Fact]
    public void FromExistingRules_DefaultsToSafeMaxWhenNoAdvancedValuesExist()
    {
        var settings = AvatarScaleSafetySettings.FromExistingRules(new List<AvatarScaleRule>
        {
            new() { TargetHeightMeters = 2.4, MaximumHeightMeters = 3 }
        });

        Assert.Equal(AvatarScaleRule.SafeMaximumHeightMeters, settings.CurrentMaximumHeightMeters);
    }
}
```

- [ ] **Step 2: Run the focused test and verify it fails**

Run:

```powershell
dotnet test "E:\!!!Program to work on\Proper Crystal Relay\VrcTwitchOscBridge.Tests\VrcTwitchOscBridge.Tests.csproj" --no-restore --filter "FullyQualifiedName~AvatarScaleSafetySettingsTests"
```

Expected: FAIL because `AvatarScaleSafetySettings` does not exist.

- [ ] **Step 3: Add the safety settings model**

Create `VrcTwitchOscBridge/Models/AvatarScaleSafetySettings.cs`:

```csharp
using VrcTwitchOscBridge.Infrastructure;

namespace VrcTwitchOscBridge.Models;

public sealed class AvatarScaleSafetySettings : ObservableObject
{
    private double currentMinimumHeightMeters = AvatarScaleRule.SafeMinimumHeightMeters;
    private double currentMaximumHeightMeters = AvatarScaleRule.SafeMaximumHeightMeters;

    public double CurrentMinimumHeightMeters
    {
        get => currentMinimumHeightMeters;
        set
        {
            var nextValue = NormalizeHeight(value, AvatarScaleRule.SafeMinimumHeightMeters);
            nextValue = Math.Clamp(nextValue, AvatarScaleRule.AdvancedMinimumHeightMeters, AvatarScaleRule.AdvancedMaximumHeightMeters);
            if (SetProperty(ref currentMinimumHeightMeters, nextValue))
            {
                if (currentMaximumHeightMeters < currentMinimumHeightMeters)
                {
                    CurrentMaximumHeightMeters = currentMinimumHeightMeters;
                }

                RaisePropertyChanged(nameof(CurrentMaxHeightAllowedText));
            }
        }
    }

    public double CurrentMaximumHeightMeters
    {
        get => currentMaximumHeightMeters;
        set
        {
            var nextValue = NormalizeHeight(value, AvatarScaleRule.SafeMaximumHeightMeters);
            nextValue = Math.Clamp(nextValue, CurrentMinimumHeightMeters, AvatarScaleRule.AdvancedMaximumHeightMeters);
            if (SetProperty(ref currentMaximumHeightMeters, nextValue))
            {
                RaisePropertyChanged(nameof(CurrentMaxHeightAllowedText));
            }
        }
    }

    public string CurrentMaxHeightAllowedText => $"{CurrentMaximumHeightMeters:0.###}m";

    public double ClampHeight(double value)
    {
        if (double.IsNaN(value) || double.IsInfinity(value))
        {
            return 1.6;
        }

        return Math.Clamp(value, CurrentMinimumHeightMeters, CurrentMaximumHeightMeters);
    }

    public double ClampRelativeHeight(double value)
    {
        if (double.IsNaN(value) || double.IsInfinity(value))
        {
            return 0;
        }

        var limit = Math.Max(Math.Abs(CurrentMinimumHeightMeters), Math.Abs(CurrentMaximumHeightMeters));
        return Math.Clamp(value, -limit, limit);
    }

    public static AvatarScaleSafetySettings FromExistingRules(IEnumerable<AvatarScaleRule> rules)
    {
        var settings = new AvatarScaleSafetySettings();
        var largestAdvancedValue = rules
            .Where(rule => rule.AdvancedRangeEnabled)
            .SelectMany(GetConfiguredHeightValues)
            .Where(value => !double.IsNaN(value) && !double.IsInfinity(value))
            .Where(value => value > AvatarScaleRule.SafeMaximumHeightMeters)
            .DefaultIfEmpty(AvatarScaleRule.SafeMaximumHeightMeters)
            .Max();

        settings.CurrentMaximumHeightMeters = largestAdvancedValue;
        return settings;
    }

    private static IEnumerable<double> GetConfiguredHeightValues(AvatarScaleRule rule)
    {
        yield return rule.TargetHeightMeters;
        yield return rule.MinimumHeightMeters;
        yield return rule.MaximumHeightMeters;
        yield return rule.RelativeMinimumHeightMeters;
        yield return rule.RelativeMaximumHeightMeters;
        yield return rule.RestoreHeightMeters;
        yield return rule.SupporterGrowthNormalHeightMeters;
        yield return rule.SupporterGrowthNormalHeightMeters + rule.SupporterGrowthMaxAddedHeightMeters;
        yield return rule.SupporterGrowthNormalHeightMeters + rule.SupporterGrowthTier1HeightMeters;
        yield return rule.SupporterGrowthNormalHeightMeters + rule.SupporterGrowthTier2HeightMeters;
        yield return rule.SupporterGrowthNormalHeightMeters + rule.SupporterGrowthTier3HeightMeters;
    }

    private static double NormalizeHeight(double value, double fallback)
    {
        return double.IsNaN(value) || double.IsInfinity(value) || value <= 0
            ? fallback
            : value;
    }
}
```

- [ ] **Step 4: Add `AvatarScaleSafety` to `AppSettings`**

Modify `VrcTwitchOscBridge/Models/AppSettings.cs`.

Add `using System.ComponentModel;` at the top:

```csharp
using System.Collections.ObjectModel;
using System.ComponentModel;
using VrcTwitchOscBridge.Infrastructure;
```

Add the backing field near `avatarScaleMasterReward`:

```csharp
private AvatarScaleSafetySettings avatarScaleSafety = new();
```

Change the constructor:

```csharp
public AppSettings()
{
    WireCustomTheme(customTheme);
    WireAvatarScaleSafety(avatarScaleSafety);
}
```

Add the property after `AvatarScaleMasterReward`:

```csharp
public AvatarScaleSafetySettings AvatarScaleSafety
{
    get => avatarScaleSafety;
    set
    {
        var nextValue = value ?? new AvatarScaleSafetySettings();
        if (ReferenceEquals(avatarScaleSafety, nextValue))
        {
            return;
        }

        UnwireAvatarScaleSafety(avatarScaleSafety);
        avatarScaleSafety = nextValue;
        WireAvatarScaleSafety(avatarScaleSafety);
        RaisePropertyChanged();
    }
}
```

Add these private helpers near the existing custom-theme wiring helpers at the bottom of `AppSettings.cs`:

```csharp
private void WireAvatarScaleSafety(AvatarScaleSafetySettings settings)
{
    settings.PropertyChanged += AvatarScaleSafetyChanged;
}

private void UnwireAvatarScaleSafety(AvatarScaleSafetySettings settings)
{
    settings.PropertyChanged -= AvatarScaleSafetyChanged;
}

private void AvatarScaleSafetyChanged(object? sender, PropertyChangedEventArgs e)
{
    RaisePropertyChanged(nameof(AvatarScaleSafety));
}
```

- [ ] **Step 5: Include the new model in the app project**

Modify `VrcTwitchOscBridge/VrcTwitchOscBridge.csproj` near the existing model includes:

```xml
<Compile Include="Models\AvatarScaleRule.cs" />
<Compile Include="Models\AvatarScaleSafetySettings.cs" />
<Compile Include="Models\CashPaymentRule.cs" />
```

- [ ] **Step 6: Run focused tests and verify pass**

Run:

```powershell
dotnet test "E:\!!!Program to work on\Proper Crystal Relay\VrcTwitchOscBridge.Tests\VrcTwitchOscBridge.Tests.csproj" --no-restore --filter "FullyQualifiedName~AvatarScaleSafetySettingsTests"
```

Expected: PASS.

- [ ] **Step 7: Checkpoint**

Do not commit unless explicitly approved by the user. If commits are approved, run:

```powershell
git add "VrcTwitchOscBridge/Models/AvatarScaleSafetySettings.cs" "VrcTwitchOscBridge/Models/AppSettings.cs" "VrcTwitchOscBridge/VrcTwitchOscBridge.csproj" "VrcTwitchOscBridge.Tests/AvatarScaleSafetySettingsTests.cs"
git commit -m "feat(scale): add shared avatar scale safety settings"
```

---

### Task 2: Persist Shared Safety Settings

**Files:**

- Modify: `VrcTwitchOscBridge/Services/SettingsStore.cs`
- Modify: `VrcTwitchOscBridge.Tests/AvatarScaleSafetySettingsTests.cs`

- [ ] **Step 1: Add migration aggregation test**

Append this test to `AvatarScaleSafetySettingsTests`:

```csharp
[Fact]
public void FromExistingRules_IncludesNestedCashAndPowerUpScaleActionsWhenCallerAggregatesThem()
{
    var cash = new CashPaymentRule { ActionKind = CashPaymentActionKind.AvatarScaling };
    cash.ScaleAction.AdvancedRangeEnabled = true;
    cash.ScaleAction.MaximumHeightMeters = 180;

    var power = new PowerUpRule { ActionKind = PowerUpActionKind.AvatarScaling };
    power.ScaleAction.AdvancedRangeEnabled = true;
    power.ScaleAction.TargetHeightMeters = 240;

    var allRules = new[]
    {
        cash.ScaleAction,
        power.ScaleAction
    };

    var settings = AvatarScaleSafetySettings.FromExistingRules(allRules);

    Assert.Equal(240, settings.CurrentMaximumHeightMeters);
}
```

- [ ] **Step 2: Run focused test and verify it passes before persistence edits**

Run:

```powershell
dotnet test "E:\!!!Program to work on\Proper Crystal Relay\VrcTwitchOscBridge.Tests\VrcTwitchOscBridge.Tests.csproj" --no-restore --filter "FullyQualifiedName~AvatarScaleSafetySettingsTests"
```

Expected: PASS. This confirms the aggregation helper works before wiring it into `SettingsStore`.

- [ ] **Step 3: Add persisted DTO and profile property**

Modify `VrcTwitchOscBridge/Services/SettingsStore.cs`.

In `PersistedProfileSettings`, add this after `AvatarScaleMasterReward`:

```csharp
public PersistedAvatarScaleSafetySettings? AvatarScaleSafety { get; set; }
```

Add this DTO near `PersistedAvatarScaleRule`:

```csharp
private sealed class PersistedAvatarScaleSafetySettings
{
    public double CurrentMinimumHeightMeters { get; set; }

    public double CurrentMaximumHeightMeters { get; set; }
}
```

- [ ] **Step 4: Add conversion helpers**

Add these methods before `ToPersistedAvatarScaleRule`:

```csharp
private static PersistedAvatarScaleSafetySettings ToPersistedAvatarScaleSafety(AvatarScaleSafetySettings settings)
{
    return new PersistedAvatarScaleSafetySettings
    {
        CurrentMinimumHeightMeters = settings.CurrentMinimumHeightMeters,
        CurrentMaximumHeightMeters = settings.CurrentMaximumHeightMeters
    };
}

private static AvatarScaleSafetySettings ToAvatarScaleSafety(PersistedAvatarScaleSafetySettings? persisted, IEnumerable<AvatarScaleRule> migrationRules)
{
    if (persisted is null)
    {
        return AvatarScaleSafetySettings.FromExistingRules(migrationRules);
    }

    return new AvatarScaleSafetySettings
    {
        CurrentMinimumHeightMeters = persisted.CurrentMinimumHeightMeters,
        CurrentMaximumHeightMeters = persisted.CurrentMaximumHeightMeters
    };
}
```

Add this helper near `BuildAvatarScaleSets`:

```csharp
private static IEnumerable<AvatarScaleRule> EnumerateAvatarScaleSafetyMigrationRules(AppSettings settings)
{
    foreach (var rule in settings.AvatarScaleSets.SelectMany(set => set.ScaleRules))
    {
        yield return rule;
    }

    foreach (var rule in settings.AvatarScaleRules)
    {
        yield return rule;
    }

    foreach (var rule in settings.CashPaymentRules.Where(rule => rule.UsesAvatarScaling))
    {
        yield return rule.ScaleAction;
    }

    foreach (var rule in settings.PowerUpRules.Where(rule => rule.UsesAvatarScaling))
    {
        yield return rule.ScaleAction;
    }

    foreach (var rule in settings.AvatarSwapProfiles.SelectMany(profile => profile.PaymentRules).Where(rule => rule.UsesAvatarScaling))
    {
        yield return rule.ScaleAction;
    }
}
```

- [ ] **Step 5: Load and save safety settings**

In `LoadAsync`, after `CashPaymentRules` and `PowerUpRules` are loaded, set:

```csharp
settings.AvatarScaleSafety = ToAvatarScaleSafety(
    profile.AvatarScaleSafety,
    EnumerateAvatarScaleSafetyMigrationRules(settings));
```

Place this after `settings.CashPaymentRules = ...` and after `settings.PowerUpRules = ...` are both populated, so migration sees nested scaling actions.

In `SaveAsync`, add to the profile initializer near avatar scale settings:

```csharp
AvatarScaleSafety = ToPersistedAvatarScaleSafety(settings.AvatarScaleSafety),
```

- [ ] **Step 6: Run focused tests and build**

Run:

```powershell
dotnet test "E:\!!!Program to work on\Proper Crystal Relay\VrcTwitchOscBridge.Tests\VrcTwitchOscBridge.Tests.csproj" --no-restore --filter "FullyQualifiedName~AvatarScaleSafetySettingsTests"
dotnet build "E:\!!!Program to work on\Proper Crystal Relay\VrcTwitchOscBridge\VrcTwitchOscBridge.csproj" --no-restore
```

Expected: tests PASS; build PASS.

- [ ] **Step 7: Checkpoint**

Do not commit unless explicitly approved by the user. If commits are approved, run:

```powershell
git add "VrcTwitchOscBridge/Services/SettingsStore.cs" "VrcTwitchOscBridge.Tests/AvatarScaleSafetySettingsTests.cs"
git commit -m "feat(scale): persist shared avatar scale safety"
```

---

### Task 3: Carry Safety Into Runtime Snapshots

**Files:**

- Create: `VrcTwitchOscBridge.Tests/BridgeRuntimeConfigurationAvatarScaleSafetyTests.cs`
- Modify: `VrcTwitchOscBridge/Services/BridgeRuntimeConfiguration.cs`

- [ ] **Step 1: Write failing runtime snapshot tests**

Create `VrcTwitchOscBridge.Tests/BridgeRuntimeConfigurationAvatarScaleSafetyTests.cs`:

```csharp
using System.Collections.ObjectModel;
using System.Linq;
using VrcTwitchOscBridge.Models;
using VrcTwitchOscBridge.Services;
using Xunit;

namespace VrcTwitchOscBridge.Tests;

public sealed class BridgeRuntimeConfigurationAvatarScaleSafetyTests
{
    [Fact]
    public void FromSettings_ClampsNormalScaleRuleToCurrentMaxHeightAllowed()
    {
        var settings = new AppSettings();
        settings.AvatarScaleSafety.CurrentMaximumHeightMeters = 2;
        var set = new AvatarScaleSet();
        set.ScaleRules.Add(new AvatarScaleRule
        {
            Name = "Too Tall",
            TriggerType = AvatarScaleTriggerType.ChatCommand,
            CommandText = "!tall",
            AdvancedRangeEnabled = true,
            TargetHeightMeters = 50,
            RestoreHeightMeters = 20
        });
        settings.AvatarScaleSets.Add(set);

        var configuration = BridgeRuntimeConfiguration.FromSettings(settings, RuntimeConfig.CreateDefault());
        var snapshot = Assert.Single(configuration.AvatarScaleRules);

        Assert.Equal(2, snapshot.TargetHeightMeters, precision: 3);
        Assert.Equal(2, snapshot.RestoreHeightMeters, precision: 3);
        Assert.Equal(2, snapshot.CurrentMaximumHeightAllowedMeters, precision: 3);
    }

    [Fact]
    public void FromSettings_ClampsCashPaymentScaleActionToCurrentMaxHeightAllowed()
    {
        var settings = new AppSettings();
        settings.AvatarScaleSafety.CurrentMaximumHeightMeters = 3;
        var rule = new CashPaymentRule
        {
            Name = "Tip Tall",
            ActionKind = CashPaymentActionKind.AvatarScaling,
            Provider = CashPaymentProvider.StreamElements,
            MinimumAmount = 1
        };
        rule.ScaleAction.AdvancedRangeEnabled = true;
        rule.ScaleAction.TargetHeightMeters = 50;
        settings.CashPaymentRules.Add(rule);

        var configuration = BridgeRuntimeConfiguration.FromSettings(settings, RuntimeConfig.CreateDefault());
        var snapshot = Assert.Single(configuration.CashPaymentRules);

        Assert.NotNull(snapshot.ScaleAction);
        Assert.Equal(3, snapshot.ScaleAction!.TargetHeightMeters, precision: 3);
        Assert.Equal(3, snapshot.ScaleAction.CurrentMaximumHeightAllowedMeters, precision: 3);
    }
}
```

- [ ] **Step 2: Run focused tests and verify failure**

Run:

```powershell
dotnet test "E:\!!!Program to work on\Proper Crystal Relay\VrcTwitchOscBridge.Tests\VrcTwitchOscBridge.Tests.csproj" --no-restore --filter "FullyQualifiedName~BridgeRuntimeConfigurationAvatarScaleSafetyTests"
```

Expected: FAIL because `CurrentMaximumHeightAllowedMeters` does not exist and snapshots do not use shared safety.

- [ ] **Step 3: Add safety snapshot fields**

Modify `VrcTwitchOscBridge/Services/BridgeRuntimeConfiguration.cs`.

Add this record near `AvatarScaleMasterRewardSnapshot`:

```csharp
public sealed record AvatarScaleSafetySnapshot(
    double CurrentMinimumHeightAllowedMeters,
    double CurrentMaximumHeightAllowedMeters);
```

Add two fields to `AvatarScaleRuleSnapshot` after `BypassVrChatScaleLimits`:

```csharp
double CurrentMinimumHeightAllowedMeters,
double CurrentMaximumHeightAllowedMeters,
```

Add one field to `BridgeRuntimeConfiguration` after `AvatarScaleMasterReward`:

```csharp
AvatarScaleSafetySnapshot AvatarScaleSafety,
```

- [ ] **Step 4: Convert safety settings once in `FromSettings`**

Inside `FromSettings`, after list initialization, add:

```csharp
var avatarScaleSafety = ToAvatarScaleSafetySnapshot(settings.AvatarScaleSafety);
```

Pass `avatarScaleSafety` into every `TryToAvatarScaleSnapshot` call.

Change method signature:

```csharp
private static bool TryToAvatarScaleSnapshot(
    AvatarScaleRule rule,
    AvatarScaleSafetySnapshot safety,
    bool requireTriggerFilter,
    out AvatarScaleRuleSnapshot snapshot)
```

Add conversion helper near `ToAvatarScaleMasterRewardSnapshot`:

```csharp
private static AvatarScaleSafetySnapshot ToAvatarScaleSafetySnapshot(AvatarScaleSafetySettings settings)
{
    return new AvatarScaleSafetySnapshot(
        settings.CurrentMinimumHeightMeters,
        settings.CurrentMaximumHeightMeters);
}
```

Add the safety field to the `BridgeRuntimeConfiguration` constructor call near existing `ToAvatarScaleMasterRewardSnapshot(settings.AvatarScaleMasterReward)`:

```csharp
ToAvatarScaleMasterRewardSnapshot(settings.AvatarScaleMasterReward),
avatarScaleSafety,
ToCashPaymentConnectionSnapshot(settings.CashPayments),
```

- [ ] **Step 5: Clamp snapshot values with shared safety**

Replace snapshot height construction values in `TryToAvatarScaleSnapshot` so they call safety-aware helpers:

```csharp
ClampScaleHeight(rule.TargetHeightMeters, rule.AdvancedRangeEnabled, safety),
ClampScaleHeight(rule.MinimumHeightMeters, rule.AdvancedRangeEnabled, safety),
ClampScaleHeight(rule.MaximumHeightMeters, rule.AdvancedRangeEnabled, safety),
ClampRelativeScaleHeight(rule.RelativeHeightMeters, rule.AdvancedRangeEnabled, safety),
ClampScaleHeight(rule.RelativeMinimumHeightMeters, rule.AdvancedRangeEnabled, safety),
ClampScaleHeight(rule.RelativeMaximumHeightMeters, rule.AdvancedRangeEnabled, safety),
```

Replace restore and supporter-growth clamps similarly:

```csharp
ClampScaleHeight(rule.RestoreHeightMeters, rule.AdvancedRangeEnabled, safety),
rule.AdvancedRangeEnabled,
rule.BypassVrChatScaleLimits,
safety.CurrentMinimumHeightAllowedMeters,
safety.CurrentMaximumHeightAllowedMeters,
ClampScaleHeight(rule.SupporterGrowthNormalHeightMeters, rule.AdvancedRangeEnabled, safety),
ClampRelativeScaleHeight(rule.SupporterGrowthMaxAddedHeightMeters, rule.AdvancedRangeEnabled, safety),
```

Update helper signatures:

```csharp
private static double ClampScaleHeight(double value, bool advancedRangeEnabled, AvatarScaleSafetySnapshot safety)
{
    if (double.IsNaN(value) || double.IsInfinity(value))
    {
        return 1.6;
    }

    var minimum = advancedRangeEnabled
        ? Math.Max(AvatarScaleRule.AdvancedMinimumHeightMeters, safety.CurrentMinimumHeightAllowedMeters)
        : Math.Max(AvatarScaleRule.SafeMinimumHeightMeters, safety.CurrentMinimumHeightAllowedMeters);
    var maximum = advancedRangeEnabled
        ? Math.Min(AvatarScaleRule.AdvancedMaximumHeightMeters, safety.CurrentMaximumHeightAllowedMeters)
        : Math.Min(AvatarScaleRule.SafeMaximumHeightMeters, safety.CurrentMaximumHeightAllowedMeters);
    if (maximum < minimum)
    {
        maximum = minimum;
    }

    return Math.Clamp(value, minimum, maximum);
}

private static double ClampRelativeScaleHeight(double value, bool advancedRangeEnabled, AvatarScaleSafetySnapshot safety)
{
    if (double.IsNaN(value) || double.IsInfinity(value))
    {
        return 0;
    }

    var baseLimit = advancedRangeEnabled ? AvatarScaleRule.AdvancedMaximumHeightMeters : AvatarScaleRule.SafeMaximumHeightMeters;
    var limit = Math.Min(baseLimit, Math.Max(Math.Abs(safety.CurrentMinimumHeightAllowedMeters), Math.Abs(safety.CurrentMaximumHeightAllowedMeters)));
    return Math.Clamp(value, -limit, limit);
}
```

- [ ] **Step 6: Update manual test snapshot methods**

Change manual snapshot methods so tests use shared safety when a caller passes it, but keep a default for existing call sites:

```csharp
public static AvatarScaleRuleSnapshot CreateManualTestSnapshot(
    AvatarScaleRule rule,
    AvatarScaleSafetySettings? safetySettings = null)
{
    var safety = ToAvatarScaleSafetySnapshot(safetySettings ?? new AvatarScaleSafetySettings());
    if (!TryToAvatarScaleSnapshot(rule, safety, requireTriggerFilter: false, out var snapshot))
    {
        throw new InvalidOperationException(GetAvatarScaleManualTestReadinessError(rule));
    }

    return snapshot;
}
```

Update cash and Power Up manual snapshot methods to accept `AvatarScaleSafetySettings? safetySettings = null` and pass the converted snapshot into nested `TryToAvatarScaleSnapshot` calls.

- [ ] **Step 7: Run focused tests**

Run:

```powershell
dotnet test "E:\!!!Program to work on\Proper Crystal Relay\VrcTwitchOscBridge.Tests\VrcTwitchOscBridge.Tests.csproj" --no-restore --filter "FullyQualifiedName~BridgeRuntimeConfigurationAvatarScaleSafetyTests"
```

Expected: PASS.

- [ ] **Step 8: Checkpoint**

Do not commit unless explicitly approved by the user. If commits are approved, run:

```powershell
git add "VrcTwitchOscBridge/Services/BridgeRuntimeConfiguration.cs" "VrcTwitchOscBridge.Tests/BridgeRuntimeConfigurationAvatarScaleSafetyTests.cs"
git commit -m "feat(scale): apply shared safety to runtime snapshots"
```

---

### Task 4: Apply Shared Safety In Runtime Execution And Main VM Wiring

**Files:**

- Modify: `VrcTwitchOscBridge/Services/BridgeCoordinator.cs`
- Modify: `VrcTwitchOscBridge/ViewModels/MainWindowViewModel.cs`

- [ ] **Step 1: Clamp runtime height helper to snapshot safety fields**

Modify `BridgeCoordinator.ClampAvatarScaleHeight`:

```csharp
private static double ClampAvatarScaleHeight(AvatarScaleRuleSnapshot rule, double value)
{
    if (double.IsNaN(value) || double.IsInfinity(value))
    {
        return 1.6;
    }

    var minimum = rule.AdvancedRangeEnabled
        ? Math.Max(AvatarScaleRule.AdvancedMinimumHeightMeters, rule.CurrentMinimumHeightAllowedMeters)
        : Math.Max(AvatarScaleRule.SafeMinimumHeightMeters, rule.CurrentMinimumHeightAllowedMeters);
    var maximum = rule.AdvancedRangeEnabled
        ? Math.Min(AvatarScaleRule.AdvancedMaximumHeightMeters, rule.CurrentMaximumHeightAllowedMeters)
        : Math.Min(AvatarScaleRule.SafeMaximumHeightMeters, rule.CurrentMaximumHeightAllowedMeters);
    if (maximum < minimum)
    {
        maximum = minimum;
    }

    return Math.Clamp(value, minimum, maximum);
}
```

- [ ] **Step 2: Add final direct-send safety clamp**

Add this helper near `ClampAvatarScaleHeight`:

```csharp
private double ClampAvatarScaleHeightToActiveSafety(double value)
{
    if (double.IsNaN(value) || double.IsInfinity(value))
    {
        return 1.6;
    }

    var safety = activeConfiguration?.AvatarScaleSafety;
    if (safety is null)
    {
        return Math.Clamp(value, AvatarScaleRule.AdvancedMinimumHeightMeters, AvatarScaleRule.AdvancedMaximumHeightMeters);
    }

    var minimum = Math.Clamp(
        safety.CurrentMinimumHeightAllowedMeters,
        AvatarScaleRule.AdvancedMinimumHeightMeters,
        AvatarScaleRule.AdvancedMaximumHeightMeters);
    var maximum = Math.Clamp(
        safety.CurrentMaximumHeightAllowedMeters,
        minimum,
        AvatarScaleRule.AdvancedMaximumHeightMeters);
    return Math.Clamp(value, minimum, maximum);
}
```

At the start of `SendAvatarHeightValueAsync(double heightMeters, CancellationToken cancellationToken)`, add:

```csharp
heightMeters = ClampAvatarScaleHeightToActiveSafety(heightMeters);
```

This protects restore, carryover, dev commands, cash scaling, and Power Up scaling even when a path sends `/avatar/eyeheight` directly.

- [ ] **Step 3: Pass safety into manual tests from `MainWindowViewModel`**

Update calls to `BridgeRuntimeConfiguration.CreateManualTestSnapshot` in:

- `TestSelectedAvatarScaleRuleAsync`
- `TestSelectedCashPaymentRuleAsync`
- `TestSelectedPowerUpRuleAsync`

Use:

```csharp
BridgeRuntimeConfiguration.CreateManualTestSnapshot(rule, Settings.AvatarScaleSafety)
```

For cash and Power Up overloads, pass `Settings.AvatarScaleSafety` to the new parameter.

- [ ] **Step 4: Wire safety settings changes**

In `WireSettings`, add after master reward wiring:

```csharp
appSettings.AvatarScaleSafety.PropertyChanged += AvatarScaleSafetyChanged;
```

In `UnwireSettings`, add:

```csharp
appSettings.AvatarScaleSafety.PropertyChanged -= AvatarScaleSafetyChanged;
```

Add handler near `AvatarScaleMasterRewardChanged`:

```csharp
private void AvatarScaleSafetyChanged(object? sender, PropertyChangedEventArgs e)
{
    QueueSave();
    QueueBridgeRefresh();
    RaisePropertyChanged(nameof(AvatarScaleRuntimeStatusText));
    RaisePropertyChanged(nameof(AvatarScaleSets));
    RaisePropertyChanged(nameof(AvatarScaleRules));
    QueueManagedRewardSync();
}
```

- [ ] **Step 5: Build after runtime changes**

Run:

```powershell
dotnet build "E:\!!!Program to work on\Proper Crystal Relay\VrcTwitchOscBridge\VrcTwitchOscBridge.csproj" --no-restore
```

Expected: PASS.

- [ ] **Step 6: Run focused safety tests**

Run:

```powershell
dotnet test "E:\!!!Program to work on\Proper Crystal Relay\VrcTwitchOscBridge.Tests\VrcTwitchOscBridge.Tests.csproj" --no-restore --filter "FullyQualifiedName~AvatarScaleSafetySettingsTests|FullyQualifiedName~BridgeRuntimeConfigurationAvatarScaleSafetyTests"
```

Expected: PASS.

- [ ] **Step 7: Checkpoint**

Do not commit unless explicitly approved by the user. If commits are approved, run:

```powershell
git add "VrcTwitchOscBridge/Services/BridgeCoordinator.cs" "VrcTwitchOscBridge/ViewModels/MainWindowViewModel.cs"
git commit -m "feat(scale): enforce shared safety during runtime scaling"
```

---

### Task 5: Add Manager Card ViewModels

**Files:**

- Create: `VrcTwitchOscBridge.Tests/AvatarScalingManagerViewModelTests.cs`
- Create: `VrcTwitchOscBridge/ViewModels/AvatarScalingSourceCardViewModel.cs`
- Create: `VrcTwitchOscBridge/ViewModels/AvatarScalingManagerViewModel.cs`
- Modify: `VrcTwitchOscBridge/VrcTwitchOscBridge.csproj`

- [ ] **Step 1: Write failing manager ViewModel tests**

Create `VrcTwitchOscBridge.Tests/AvatarScalingManagerViewModelTests.cs`:

```csharp
using System.Linq;
using VrcTwitchOscBridge.Models;
using VrcTwitchOscBridge.ViewModels;
using Xunit;

namespace VrcTwitchOscBridge.Tests;

public sealed class AvatarScalingManagerViewModelTests
{
    [Fact]
    public void Constructor_BuildsRewardCardsFromScaleSets()
    {
        var settings = new AppSettings();
        var set = new AvatarScaleSet { Name = "Default Scale Set" };
        set.ScaleRules.Add(new AvatarScaleRule
        {
            Name = "Grow Big",
            TriggerType = AvatarScaleTriggerType.ChannelPointReward,
            RewardTitle = "Grow Big"
        });
        settings.AvatarScaleSets.Add(set);

        var vm = new AvatarScalingManagerViewModel(settings, null);

        var card = Assert.Single(vm.TwitchRewardCards);
        Assert.Equal(AvatarScalingSourceKind.TwitchReward, card.Kind);
        Assert.Equal("Grow Big", card.Title);
        Assert.Contains("Current max height allowed: 100m", card.SafetySummary);
    }

    [Fact]
    public void Constructor_ShowsOnlyAvatarScalingCashAndPowerUpCards()
    {
        var settings = new AppSettings();
        settings.CashPaymentRules.Add(new CashPaymentRule { Name = "Tip Scale", ActionKind = CashPaymentActionKind.AvatarScaling });
        settings.CashPaymentRules.Add(new CashPaymentRule { Name = "Tip OSC", ActionKind = CashPaymentActionKind.TriggerAction });
        settings.PowerUpRules.Add(new PowerUpRule { Name = "Power Scale", ActionKind = PowerUpActionKind.AvatarScaling });
        settings.PowerUpRules.Add(new PowerUpRule { Name = "Power OSC", ActionKind = PowerUpActionKind.TriggerAction });

        var vm = new AvatarScalingManagerViewModel(settings, null);

        Assert.Single(vm.CashPaymentCards);
        Assert.Equal("Tip Scale", vm.CashPaymentCards.Single().Title);
        Assert.Single(vm.PowerUpCards);
        Assert.Equal("Power Scale", vm.PowerUpCards.Single().Title);
    }

    [Fact]
    public void OpenEditorCommand_SelectsCardAndOpensSidePanel()
    {
        var settings = new AppSettings();
        var set = new AvatarScaleSet();
        set.ScaleRules.Add(new AvatarScaleRule { Name = "Grow Big", RewardTitle = "Grow Big" });
        settings.AvatarScaleSets.Add(set);
        var vm = new AvatarScalingManagerViewModel(settings, null);
        var card = vm.TwitchRewardCards.Single();

        vm.OpenEditorCommand.Execute(card);

        Assert.True(vm.IsEditorOpen);
        Assert.Same(card, vm.SelectedCard);
        Assert.Same(card.ScaleRule, vm.SelectedAvatarScaleRule);
    }
}
```

- [ ] **Step 2: Run tests and verify failure**

Run:

```powershell
dotnet test "E:\!!!Program to work on\Proper Crystal Relay\VrcTwitchOscBridge.Tests\VrcTwitchOscBridge.Tests.csproj" --no-restore --filter "FullyQualifiedName~AvatarScalingManagerViewModelTests"
```

Expected: FAIL because manager ViewModel types do not exist.

- [ ] **Step 3: Add card ViewModel**

Create `VrcTwitchOscBridge/ViewModels/AvatarScalingSourceCardViewModel.cs`:

```csharp
using VrcTwitchOscBridge.Infrastructure;
using VrcTwitchOscBridge.Models;

namespace VrcTwitchOscBridge.ViewModels;

public enum AvatarScalingSourceKind
{
    MasterReward,
    TwitchReward,
    SupporterGrowth,
    CashPayment,
    PowerUp
}

public enum AvatarScalingCardStatus
{
    Ready,
    NeedsSetup,
    Disabled
}

public sealed class AvatarScalingSourceCardViewModel : ObservableObject
{
    private readonly AvatarScaleSafetySettings safety;

    public AvatarScalingSourceCardViewModel(
        AvatarScalingSourceKind kind,
        AvatarScaleSafetySettings safety,
        AvatarScaleRule? scaleRule = null,
        AvatarScaleMasterRewardSettings? masterReward = null,
        CashPaymentRule? cashPaymentRule = null,
        PowerUpRule? powerUpRule = null)
    {
        Kind = kind;
        this.safety = safety;
        ScaleRule = scaleRule;
        MasterReward = masterReward;
        CashPaymentRule = cashPaymentRule;
        PowerUpRule = powerUpRule;
    }

    public AvatarScalingSourceKind Kind { get; }

    public AvatarScaleRule? ScaleRule { get; }

    public AvatarScaleMasterRewardSettings? MasterReward { get; }

    public CashPaymentRule? CashPaymentRule { get; }

    public PowerUpRule? PowerUpRule { get; }

    public string Title => Kind switch
    {
        AvatarScalingSourceKind.MasterReward => string.IsNullOrWhiteSpace(MasterReward?.RewardTitle) ? "Master Unlock Reward" : MasterReward!.RewardTitle,
        AvatarScalingSourceKind.CashPayment => CashPaymentRule?.DisplayTitle ?? "Cash Payment Scaling",
        AvatarScalingSourceKind.PowerUp => PowerUpRule?.DisplayTitle ?? "Power Up Scaling",
        _ => ScaleRule?.DisplayTitle ?? "Avatar Scale"
    };

    public string SourcePill => Kind switch
    {
        AvatarScalingSourceKind.MasterReward => "Master",
        AvatarScalingSourceKind.TwitchReward => "Reward",
        AvatarScalingSourceKind.SupporterGrowth => "Supporter Growth",
        AvatarScalingSourceKind.CashPayment => "Cash",
        AvatarScalingSourceKind.PowerUp => "Power Up",
        _ => string.Empty
    };

    public AvatarScalingCardStatus Status
    {
        get
        {
            if (ScaleRule is { IsEnabled: false }
                || CashPaymentRule is { IsEnabled: false }
                || PowerUpRule is { IsEnabled: false })
            {
                return AvatarScalingCardStatus.Disabled;
            }

            if (Kind == AvatarScalingSourceKind.TwitchReward
                && ScaleRule is { UsesChannelPointReward: true }
                && string.IsNullOrWhiteSpace(ScaleRule.RewardId)
                && string.IsNullOrWhiteSpace(ScaleRule.RewardTitle))
            {
                return AvatarScalingCardStatus.NeedsSetup;
            }

            if (ScaleRule is { UsesChatCommand: true } && string.IsNullOrWhiteSpace(ScaleRule.CommandText))
            {
                return AvatarScalingCardStatus.NeedsSetup;
            }

            return AvatarScalingCardStatus.Ready;
        }
    }

    public string StatusText => Status switch
    {
        AvatarScalingCardStatus.Ready => "Ready",
        AvatarScalingCardStatus.NeedsSetup => "Needs setup",
        AvatarScalingCardStatus.Disabled => "Disabled",
        _ => string.Empty
    };

    public string ActionSummary => ScaleRule?.ScaleSummary
        ?? CashPaymentRule?.ScaleAction.ScaleSummary
        ?? PowerUpRule?.ScaleAction.ScaleSummary
        ?? "Unlocks child scale rewards";

    public string SafetySummary => $"Current max height allowed: {safety.CurrentMaxHeightAllowedText}";
}
```

- [ ] **Step 4: Add manager ViewModel**

Create `VrcTwitchOscBridge/ViewModels/AvatarScalingManagerViewModel.cs`:

```csharp
using System.Collections.ObjectModel;
using VrcTwitchOscBridge.Infrastructure;
using VrcTwitchOscBridge.Models;

namespace VrcTwitchOscBridge.ViewModels;

public enum AvatarScalingManagerSourceView
{
    TwitchRewards,
    SupporterGrowth,
    CashPayments,
    PowerUps,
    AllSources
}

public sealed class AvatarScalingManagerViewModel : ObservableObject
{
    private readonly AppSettings settings;
    private readonly MainWindowViewModel? mainWindowViewModel;
    private AvatarScalingSourceCardViewModel? selectedCard;
    private bool isEditorOpen;
    private AvatarScalingManagerSourceView activeSourceView = AvatarScalingManagerSourceView.TwitchRewards;

    public AvatarScalingManagerViewModel(AppSettings settings, MainWindowViewModel? mainWindowViewModel)
    {
        this.settings = settings ?? throw new ArgumentNullException(nameof(settings));
        this.mainWindowViewModel = mainWindowViewModel;

        OpenEditorCommand = new RelayCommand(parameter => OpenEditor(parameter as AvatarScalingSourceCardViewModel));
        CloseEditorCommand = new RelayCommand(CloseEditor);

        RefreshCards();
    }

    public AppSettings Settings => settings;

    public ObservableCollection<AvatarScalingSourceCardViewModel> TwitchRewardCards { get; } = [];

    public ObservableCollection<AvatarScalingSourceCardViewModel> SupporterGrowthCards { get; } = [];

    public ObservableCollection<AvatarScalingSourceCardViewModel> CashPaymentCards { get; } = [];

    public ObservableCollection<AvatarScalingSourceCardViewModel> PowerUpCards { get; } = [];

    public AvatarScalingSourceCardViewModel MasterRewardCard { get; private set; } = null!;

    public AvatarScalingSourceCardViewModel? SelectedCard
    {
        get => selectedCard;
        private set
        {
            if (SetProperty(ref selectedCard, value))
            {
                RaisePropertyChanged(nameof(SelectedAvatarScaleRule));
                RaisePropertyChanged(nameof(SelectedCashPaymentRule));
                RaisePropertyChanged(nameof(SelectedPowerUpRule));
            }
        }
    }

    public AvatarScaleRule? SelectedAvatarScaleRule => SelectedCard?.ScaleRule
        ?? SelectedCard?.CashPaymentRule?.ScaleAction
        ?? SelectedCard?.PowerUpRule?.ScaleAction;

    public CashPaymentRule? SelectedCashPaymentRule => SelectedCard?.CashPaymentRule;

    public PowerUpRule? SelectedPowerUpRule => SelectedCard?.PowerUpRule;

    public AvatarScalingManagerSourceView ActiveSourceView
    {
        get => activeSourceView;
        set => SetProperty(ref activeSourceView, value);
    }

    public bool IsEditorOpen
    {
        get => isEditorOpen;
        private set => SetProperty(ref isEditorOpen, value);
    }

    public string CurrentMaxHeightAllowedText => Settings.AvatarScaleSafety.CurrentMaxHeightAllowedText;

    public RelayCommand OpenEditorCommand { get; }

    public RelayCommand CloseEditorCommand { get; }

    public void RefreshCards()
    {
        TwitchRewardCards.Clear();
        SupporterGrowthCards.Clear();
        CashPaymentCards.Clear();
        PowerUpCards.Clear();

        MasterRewardCard = new AvatarScalingSourceCardViewModel(
            AvatarScalingSourceKind.MasterReward,
            Settings.AvatarScaleSafety,
            masterReward: Settings.AvatarScaleMasterReward);
        RaisePropertyChanged(nameof(MasterRewardCard));

        foreach (var rule in Settings.AvatarScaleSets.SelectMany(set => set.ScaleRules))
        {
            var kind = rule.TriggerType == AvatarScaleTriggerType.SupporterGrowth
                ? AvatarScalingSourceKind.SupporterGrowth
                : AvatarScalingSourceKind.TwitchReward;
            var card = new AvatarScalingSourceCardViewModel(kind, Settings.AvatarScaleSafety, scaleRule: rule);
            if (kind == AvatarScalingSourceKind.SupporterGrowth)
            {
                SupporterGrowthCards.Add(card);
            }
            else
            {
                TwitchRewardCards.Add(card);
            }
        }

        foreach (var rule in Settings.CashPaymentRules.Where(rule => rule.UsesAvatarScaling))
        {
            CashPaymentCards.Add(new AvatarScalingSourceCardViewModel(
                AvatarScalingSourceKind.CashPayment,
                Settings.AvatarScaleSafety,
                cashPaymentRule: rule));
        }

        foreach (var rule in Settings.PowerUpRules.Where(rule => rule.UsesAvatarScaling))
        {
            PowerUpCards.Add(new AvatarScalingSourceCardViewModel(
                AvatarScalingSourceKind.PowerUp,
                Settings.AvatarScaleSafety,
                powerUpRule: rule));
        }
    }

    private void OpenEditor(AvatarScalingSourceCardViewModel? card)
    {
        if (card is null)
        {
            return;
        }

        SelectedCard = card;
        IsEditorOpen = true;
    }

    private void CloseEditor()
    {
        IsEditorOpen = false;
        SelectedCard = null;
    }
}
```

- [ ] **Step 5: Include new ViewModel files**

Modify `VrcTwitchOscBridge/VrcTwitchOscBridge.csproj` near existing ViewModel includes:

```xml
<Compile Include="ViewModels\AvatarScalingManagerViewModel.cs" />
<Compile Include="ViewModels\AvatarScalingSourceCardViewModel.cs" />
```

- [ ] **Step 6: Run focused tests**

Run:

```powershell
dotnet test "E:\!!!Program to work on\Proper Crystal Relay\VrcTwitchOscBridge.Tests\VrcTwitchOscBridge.Tests.csproj" --no-restore --filter "FullyQualifiedName~AvatarScalingManagerViewModelTests"
```

Expected: PASS.

- [ ] **Step 7: Checkpoint**

Do not commit unless explicitly approved by the user. If commits are approved, run:

```powershell
git add "VrcTwitchOscBridge/ViewModels/AvatarScalingManagerViewModel.cs" "VrcTwitchOscBridge/ViewModels/AvatarScalingSourceCardViewModel.cs" "VrcTwitchOscBridge/VrcTwitchOscBridge.csproj" "VrcTwitchOscBridge.Tests/AvatarScalingManagerViewModelTests.cs"
git commit -m "feat(scale): add avatar scaling manager view models"
```

---

### Task 6: Add Manager Window Shell And XAML Regression Tests

**Files:**

- Create: `VrcTwitchOscBridge.Tests/AvatarScalingManagerWindowXamlTests.cs`
- Create: `VrcTwitchOscBridge/AvatarScalingManagerWindow.xaml`
- Create: `VrcTwitchOscBridge/AvatarScalingManagerWindow.xaml.cs`
- Modify: `VrcTwitchOscBridge/VrcTwitchOscBridge.csproj`

- [ ] **Step 1: Write failing XAML tests**

Create `VrcTwitchOscBridge.Tests/AvatarScalingManagerWindowXamlTests.cs`:

```csharp
using System;
using System.IO;
using Xunit;

namespace VrcTwitchOscBridge.Tests;

public sealed class AvatarScalingManagerWindowXamlTests
{
    [Fact]
    public void Window_UsesRightSideEditorPanel()
    {
        var xaml = File.ReadAllText(FindSourceFile("VrcTwitchOscBridge", "AvatarScalingManagerWindow.xaml"));

        Assert.Contains("SelectedCard", xaml, StringComparison.Ordinal);
        Assert.Contains("IsEditorOpen", xaml, StringComparison.Ordinal);
        Assert.Contains("Grid.Column=\"2\"", xaml, StringComparison.Ordinal);
        Assert.Contains("Editing child reward", xaml, StringComparison.Ordinal);
    }

    [Fact]
    public void Window_ShowsCurrentMaxHeightAllowedAndAdvancedSafety()
    {
        var xaml = File.ReadAllText(FindSourceFile("VrcTwitchOscBridge", "AvatarScalingManagerWindow.xaml"));

        Assert.Contains("Current Max Height Allowed", xaml, StringComparison.Ordinal);
        Assert.Contains("CurrentMaxHeightAllowedText", xaml, StringComparison.Ordinal);
        Assert.Contains("Open Advanced Safety", xaml, StringComparison.Ordinal);
    }

    [Fact]
    public void Window_UsesReadableInputThemeBrushes()
    {
        var xaml = File.ReadAllText(FindSourceFile("VrcTwitchOscBridge", "AvatarScalingManagerWindow.xaml"));

        Assert.Contains("ComboTextBrush", xaml, StringComparison.Ordinal);
        Assert.Contains("ComboSurfaceBrush", xaml, StringComparison.Ordinal);
        Assert.DoesNotContain("Foreground=\"Black\"", xaml, StringComparison.Ordinal);
        Assert.DoesNotContain("Background=\"#222222\"", xaml, StringComparison.Ordinal);
    }

    private static string FindSourceFile(params string[] relativeParts)
    {
        var current = AppContext.BaseDirectory;
        while (!string.IsNullOrWhiteSpace(current))
        {
            var candidate = Path.Combine(new[] { current }.Concat(relativeParts).ToArray());
            if (File.Exists(candidate))
            {
                return candidate;
            }

            current = Directory.GetParent(current)?.FullName ?? string.Empty;
        }

        throw new FileNotFoundException($"Could not find source file {Path.Combine(relativeParts)}.");
    }
}
```

- [ ] **Step 2: Run tests and verify failure**

Run:

```powershell
dotnet test "E:\!!!Program to work on\Proper Crystal Relay\VrcTwitchOscBridge.Tests\VrcTwitchOscBridge.Tests.csproj" --no-restore --filter "FullyQualifiedName~AvatarScalingManagerWindowXamlTests"
```

Expected: FAIL because the window file does not exist.

- [ ] **Step 3: Add manager window code-behind**

Create `VrcTwitchOscBridge/AvatarScalingManagerWindow.xaml.cs`:

```csharp
using System;
using System.Windows;
using System.Windows.Input;
using VrcTwitchOscBridge.Services;
using VrcTwitchOscBridge.ViewModels;

namespace VrcTwitchOscBridge;

public partial class AvatarScalingManagerWindow : Window
{
    public AvatarScalingManagerWindow(AvatarScalingManagerViewModel viewModel)
    {
        DataContext = viewModel ?? throw new ArgumentNullException(nameof(viewModel));
        InitializeComponent();
        ThemeManager.ApplyToResources(Resources, ThemeManager.CurrentTheme);
        ThemeManager.ThemeChanged += OnThemeManagerThemeChanged;
        Closed += OnWindowClosed;
    }

    private void OnTitleBarMouseDown(object sender, MouseButtonEventArgs e)
    {
        if (e.ChangedButton == MouseButton.Left && e.ButtonState == MouseButtonState.Pressed)
        {
            try
            {
                DragMove();
            }
            catch (InvalidOperationException)
            {
            }
        }
    }

    private void OnCloseClicked(object sender, RoutedEventArgs e) => Close();

    private void OnThemeManagerThemeChanged(object? sender, EventArgs e)
    {
        Dispatcher.BeginInvoke(() => ThemeManager.ApplyToResources(Resources));
    }

    private void OnWindowClosed(object? sender, EventArgs e)
    {
        ThemeManager.ThemeChanged -= OnThemeManagerThemeChanged;
        Closed -= OnWindowClosed;
    }
}
```

- [ ] **Step 4: Add manager XAML shell**

Create `VrcTwitchOscBridge/AvatarScalingManagerWindow.xaml` with a minimal compileable version first:

```xml
<Window x:Class="VrcTwitchOscBridge.AvatarScalingManagerWindow"
        xmlns="http://schemas.microsoft.com/winfx/2006/xaml/presentation"
        xmlns:x="http://schemas.microsoft.com/winfx/2006/xaml"
        xmlns:loc="clr-namespace:VrcTwitchOscBridge.Services"
        xmlns:shell="clr-namespace:System.Windows.Shell;assembly=PresentationFramework"
        xmlns:vm="clr-namespace:VrcTwitchOscBridge.ViewModels"
        xmlns:d="http://schemas.microsoft.com/expression/blend/2008"
        xmlns:mc="http://schemas.openxmlformats.org/markup-compatibility/2006"
        mc:Ignorable="d"
        d:DataContext="{d:DesignInstance Type=vm:AvatarScalingManagerViewModel}"
        Title="{loc:Translate 'Avatar Scaling Manager'}"
        Icon="Assets/crystal-relay-icon.ico"
        Width="1100"
        Height="700"
        MinWidth="860"
        MinHeight="540"
        WindowStyle="None"
        WindowStartupLocation="CenterOwner"
        FontFamily="{DynamicResource BodyFontFamily}"
        Background="{DynamicResource WindowBackgroundBrush}">
    <shell:WindowChrome.WindowChrome>
        <shell:WindowChrome CaptionHeight="0" CornerRadius="0" GlassFrameThickness="0" ResizeBorderThickness="6" UseAeroCaptionButtons="False" />
    </shell:WindowChrome.WindowChrome>

    <Window.Resources>
        <SolidColorBrush x:Key="WindowBackgroundBrush" Color="#130B1E" />
        <SolidColorBrush x:Key="PanelBrush" Color="#CC1C132B" />
        <SolidColorBrush x:Key="NestedPanelBrush" Color="#B8241739" />
        <SolidColorBrush x:Key="BorderBrush" Color="#4B2B78" />
        <SolidColorBrush x:Key="AccentBrush" Color="#A855F7" />
        <SolidColorBrush x:Key="TextBrush" Color="#F5EEFF" />
        <SolidColorBrush x:Key="MutedBrush" Color="#C9B8E3" />
        <SolidColorBrush x:Key="InputBorderBrush" Color="#5B3A8E" />
        <SolidColorBrush x:Key="ComboTextBrush" Color="#241133" />
        <SolidColorBrush x:Key="ComboSurfaceBrush" Color="#F2EAFF" />
        <FontFamily x:Key="HeadingFontFamily">Constantia</FontFamily>
        <FontFamily x:Key="BodyFontFamily">Verdana</FontFamily>
        <BooleanToVisibilityConverter x:Key="BoolToVisibilityConverter" />

        <Style TargetType="TextBlock">
            <Setter Property="Foreground" Value="{DynamicResource TextBrush}" />
            <Setter Property="TextWrapping" Value="Wrap" />
        </Style>
        <Style TargetType="TextBox">
            <Setter Property="Background" Value="{DynamicResource ComboSurfaceBrush}" />
            <Setter Property="Foreground" Value="{DynamicResource ComboTextBrush}" />
            <Setter Property="BorderBrush" Value="{DynamicResource InputBorderBrush}" />
            <Setter Property="Padding" Value="8,6" />
        </Style>
    </Window.Resources>

    <Border BorderBrush="{DynamicResource BorderBrush}" BorderThickness="1" Background="{DynamicResource WindowBackgroundBrush}">
        <Grid>
            <Grid.RowDefinitions>
                <RowDefinition Height="Auto" />
                <RowDefinition Height="*" />
            </Grid.RowDefinitions>

            <Border Grid.Row="0" Background="{DynamicResource PanelBrush}" BorderBrush="{DynamicResource BorderBrush}" BorderThickness="0,0,0,1" Padding="14,10" MouseLeftButtonDown="OnTitleBarMouseDown">
                <DockPanel LastChildFill="True">
                    <Button DockPanel.Dock="Right" Content="x" Padding="8,4" Click="OnCloseClicked" shell:WindowChrome.IsHitTestVisibleInChrome="True" />
                    <StackPanel>
                        <TextBlock Text="{loc:Translate 'Avatar Scaling Manager'}" FontWeight="Bold" FontSize="15" />
                        <TextBlock Text="{loc:Translate 'Manage Twitch reward scaling, Supporter Growth, Cash Payment scaling, and Power Up scaling.'}" Foreground="{DynamicResource MutedBrush}" FontSize="11" />
                    </StackPanel>
                </DockPanel>
            </Border>

            <Grid Grid.Row="1" Margin="14">
                <Grid.ColumnDefinitions>
                    <ColumnDefinition Width="190" />
                    <ColumnDefinition Width="*" MinWidth="360" />
                    <ColumnDefinition Width="370" />
                </Grid.ColumnDefinitions>

                <Border Grid.Column="0" Background="{DynamicResource NestedPanelBrush}" BorderBrush="{DynamicResource BorderBrush}" BorderThickness="1" CornerRadius="16" Padding="12" Margin="0,0,12,0">
                    <StackPanel>
                        <TextBlock Text="{loc:Translate 'Scaling Sources'}" FontWeight="Bold" />
                        <Button Margin="0,10,0,0" Content="{loc:Translate 'Twitch Rewards'}" />
                        <Button Margin="0,8,0,0" Content="{loc:Translate 'Supporter Growth'}" />
                        <Button Margin="0,8,0,0" Content="{loc:Translate 'Cash Payments'}" />
                        <Button Margin="0,8,0,0" Content="{loc:Translate 'Power Ups'}" />
                    </StackPanel>
                </Border>

                <ScrollViewer Grid.Column="1" VerticalScrollBarVisibility="Auto">
                    <StackPanel>
                        <Border Background="{DynamicResource NestedPanelBrush}" BorderBrush="{DynamicResource AccentBrush}" BorderThickness="1" CornerRadius="16" Padding="14" Margin="0,0,0,12">
                            <StackPanel>
                                <TextBlock Text="{loc:Translate 'Global Safety Rule'}" FontWeight="Bold" />
                                <TextBlock Margin="0,6,0,0" Text="{Binding CurrentMaxHeightAllowedText, StringFormat=Current Max Height Allowed: {0}}" FontSize="18" FontWeight="Bold" />
                                <Button Margin="0,10,0,0" HorizontalAlignment="Left" Content="{loc:Translate 'Open Advanced Safety'}" />
                            </StackPanel>
                        </Border>

                        <Border Background="{DynamicResource NestedPanelBrush}" BorderBrush="{DynamicResource BorderBrush}" BorderThickness="1" CornerRadius="16" Padding="14">
                            <StackPanel>
                                <TextBlock Text="{loc:Translate 'Child Scale Rewards'}" FontWeight="Bold" FontSize="17" />
                                <ItemsControl Margin="0,10,0,0" ItemsSource="{Binding TwitchRewardCards}">
                                    <ItemsControl.ItemTemplate>
                                        <DataTemplate DataType="{x:Type vm:AvatarScalingSourceCardViewModel}">
                                            <Border Margin="0,0,0,10" Padding="12" CornerRadius="12" Background="{DynamicResource PanelBrush}" BorderBrush="{DynamicResource BorderBrush}" BorderThickness="1">
                                                <StackPanel>
                                                    <DockPanel LastChildFill="True">
                                                        <TextBlock DockPanel.Dock="Right" Text="{Binding StatusText}" Foreground="{DynamicResource MutedBrush}" />
                                                        <TextBlock Text="{Binding Title}" FontWeight="Bold" />
                                                    </DockPanel>
                                                    <TextBlock Margin="0,6,0,0" Text="{Binding ActionSummary}" Foreground="{DynamicResource MutedBrush}" />
                                                    <TextBlock Margin="0,6,0,0" Text="{Binding SafetySummary}" />
                                                    <Button Margin="0,8,0,0" HorizontalAlignment="Left" Content="{loc:Translate 'Edit'}" Command="{Binding DataContext.OpenEditorCommand, RelativeSource={RelativeSource AncestorType=Window}}" CommandParameter="{Binding}" />
                                                </StackPanel>
                                            </Border>
                                        </DataTemplate>
                                    </ItemsControl.ItemTemplate>
                                </ItemsControl>
                            </StackPanel>
                        </Border>
                    </StackPanel>
                </ScrollViewer>

                <Border Grid.Column="2" Background="{DynamicResource PanelBrush}" BorderBrush="{DynamicResource BorderBrush}" BorderThickness="1" CornerRadius="16" Padding="14" Margin="12,0,0,0" Visibility="{Binding IsEditorOpen, Converter={StaticResource BoolToVisibilityConverter}}">
                    <StackPanel>
                        <DockPanel LastChildFill="True">
                            <Button DockPanel.Dock="Right" Content="x" Command="{Binding CloseEditorCommand}" />
                            <StackPanel>
                                <TextBlock Text="Editing child reward" Foreground="{DynamicResource MutedBrush}" FontSize="11" />
                                <TextBlock Text="{Binding SelectedCard.Title}" FontWeight="Bold" FontSize="17" />
                            </StackPanel>
                        </DockPanel>
                        <Border Margin="0,14,0,0" Padding="12" CornerRadius="14" Background="{DynamicResource NestedPanelBrush}" BorderBrush="{DynamicResource BorderBrush}" BorderThickness="1">
                            <StackPanel>
                                <TextBlock Text="{loc:Translate 'Safety &amp; Pairing'}" FontWeight="Bold" />
                                <TextBlock Margin="0,6,0,0" Text="{Binding CurrentMaxHeightAllowedText, StringFormat=Current Max Height Allowed: {0}}" />
                                <Button Margin="0,10,0,0" Content="{loc:Translate 'Open Advanced Safety'}" />
                            </StackPanel>
                        </Border>
                    </StackPanel>
                </Border>
            </Grid>
        </Grid>
    </Border>
</Window>
```

- [ ] **Step 5: Include new window files**

Modify `VrcTwitchOscBridge/VrcTwitchOscBridge.csproj`:

```xml
<Page Include="AvatarScalingManagerWindow.xaml" />
```

Add compile include near other manager windows:

```xml
<Compile Include="AvatarScalingManagerWindow.xaml.cs">
  <DependentUpon>AvatarScalingManagerWindow.xaml</DependentUpon>
</Compile>
```

- [ ] **Step 6: Run XAML tests and build**

Run:

```powershell
dotnet test "E:\!!!Program to work on\Proper Crystal Relay\VrcTwitchOscBridge.Tests\VrcTwitchOscBridge.Tests.csproj" --no-restore --filter "FullyQualifiedName~AvatarScalingManagerWindowXamlTests"
dotnet build "E:\!!!Program to work on\Proper Crystal Relay\VrcTwitchOscBridge\VrcTwitchOscBridge.csproj" --no-restore
```

Expected: tests PASS; build PASS.

- [ ] **Step 7: Checkpoint**

Do not commit unless explicitly approved by the user. If commits are approved, run:

```powershell
git add "VrcTwitchOscBridge/AvatarScalingManagerWindow.xaml" "VrcTwitchOscBridge/AvatarScalingManagerWindow.xaml.cs" "VrcTwitchOscBridge/VrcTwitchOscBridge.csproj" "VrcTwitchOscBridge.Tests/AvatarScalingManagerWindowXamlTests.cs"
git commit -m "feat(scale): add avatar scaling manager window shell"
```

---

### Task 7: Wire Main Window Button To Manager

**Files:**

- Modify: `VrcTwitchOscBridge/ViewModels/MainWindowViewModel.cs`
- Modify: `VrcTwitchOscBridge/MainWindow.xaml`

- [ ] **Step 1: Add manager field and command**

In `MainWindowViewModel.cs`, add a field near `_universalTriggersManagerWindow`:

```csharp
private AvatarScalingManagerWindow? _avatarScalingManagerWindow;
```

Add command property near `OpenUniversalTriggersManagerCommand`:

```csharp
public RelayCommand OpenAvatarScalingManagerCommand { get; }
```

Initialize it near `OpenUniversalTriggersManagerCommand`:

```csharp
OpenAvatarScalingManagerCommand = new RelayCommand(OpenAvatarScalingManager);
```

- [ ] **Step 2: Add open method**

Add near `OpenUniversalTriggersManager()`:

```csharp
private void OpenAvatarScalingManager()
{
    if (_avatarScalingManagerWindow is { IsVisible: true })
    {
        _avatarScalingManagerWindow.Activate();
        return;
    }

    var managerVm = new AvatarScalingManagerViewModel(Settings, this);
    _avatarScalingManagerWindow = new AvatarScalingManagerWindow(managerVm)
    {
        Owner = System.Windows.Application.Current?.MainWindow,
    };
    _avatarScalingManagerWindow.Closed += (_, _) => _avatarScalingManagerWindow = null;
    _avatarScalingManagerWindow.Show();
}
```

- [ ] **Step 3: Change the Redeem Library Avatar Scaling button**

In `MainWindow.xaml`, find the Avatar Scaling button around the existing `ShowAvatarScalingCommand` binding and change:

```xml
Command="{Binding ShowAvatarScalingCommand}"
```

to:

```xml
Command="{Binding OpenAvatarScalingManagerCommand}"
```

Remove the button highlight trigger that depends on `IsViewingAvatarScaling`, because this button now opens a manager window instead of selecting an inline workspace.

- [ ] **Step 4: Adjust lockout picker availability for standalone manager**

Find `CanOpenAvatarScaleRuleLockoutPicker`. If it requires `IsViewingAvatarScaling`, change it so it only requires a selected scale rule and available lockout options:

```csharp
private bool CanOpenAvatarScaleRuleLockoutPicker()
{
    return SelectedAvatarScaleRule is not null
        && BuildAvailableAvatarScaleRuleLockoutOptions().Count > 0;
}
```

If `BuildAvailableAvatarScaleRuleLockoutOptions` or `BuildConfiguredAvatarScaleRuleLockoutOptions` filters out options when not viewing Avatar Scaling, remove that `IsViewingAvatarScaling` guard.

- [ ] **Step 5: Build**

Run:

```powershell
dotnet build "E:\!!!Program to work on\Proper Crystal Relay\VrcTwitchOscBridge\VrcTwitchOscBridge.csproj" --no-restore
```

Expected: PASS.

- [ ] **Step 6: Checkpoint**

Do not commit unless explicitly approved by the user. If commits are approved, run:

```powershell
git add "VrcTwitchOscBridge/ViewModels/MainWindowViewModel.cs" "VrcTwitchOscBridge/MainWindow.xaml"
git commit -m "feat(scale): open avatar scaling manager from library"
```

---

### Task 8: Port Editing Commands Into The Manager

**Files:**

- Modify: `VrcTwitchOscBridge/ViewModels/AvatarScalingManagerViewModel.cs`
- Modify: `VrcTwitchOscBridge/AvatarScalingManagerWindow.xaml`
- Modify: `VrcTwitchOscBridge/AvatarScalingManagerWindow.xaml.cs`
- Modify: `VrcTwitchOscBridge/ViewModels/MainWindowViewModel.cs`

- [ ] **Step 1: Expose parent-backed commands on manager ViewModel**

Add these pass-through properties to `AvatarScalingManagerViewModel`:

```csharp
public RelayCommand? AddAvatarScaleSetCommand => mainWindowViewModel?.AddAvatarScaleSetCommand;
public RelayCommand? RemoveSelectedAvatarScaleSetCommand => mainWindowViewModel?.RemoveSelectedAvatarScaleSetCommand;
public RelayCommand? AddAvatarScaleRuleCommand => mainWindowViewModel?.AddAvatarScaleRuleCommand;
public RelayCommand? RemoveSelectedAvatarScaleRuleCommand => mainWindowViewModel?.RemoveSelectedAvatarScaleRuleCommand;
public RelayCommand? TestSelectedAvatarScaleRuleCommand => mainWindowViewModel?.TestSelectedAvatarScaleRuleCommand;
public RelayCommand? OpenAvatarScaleRuleLockoutPickerCommand => mainWindowViewModel?.OpenAvatarScaleRuleLockoutPickerCommand;
public RelayCommand? RefreshTwitchRewardsCommand => mainWindowViewModel?.RefreshTwitchRewardsCommand;
public RelayCommand? UnlinkTwitchRewardCommand => mainWindowViewModel?.UnlinkTwitchRewardCommand;
```

Add pass-through option lists used by editor bindings:

```csharp
public IReadOnlyList<AvatarScaleTriggerType> AvailableAvatarScaleTriggerTypesForSelectedRule =>
    mainWindowViewModel?.AvailableAvatarScaleTriggerTypesForSelectedRule ?? [];
public IReadOnlyList<AvatarScalePreset> AvatarScalePresets => mainWindowViewModel?.AvatarScalePresets ?? [];
public IReadOnlyList<AvatarScaleRestoreMode> AvatarScaleRestoreModes => mainWindowViewModel?.AvatarScaleRestoreModes ?? [];
public IReadOnlyList<AvatarScaleSubscriptionTierOption> AvatarScaleSubscriptionTierOptions => mainWindowViewModel?.AvatarScaleSubscriptionTierOptions ?? [];
public IReadOnlyList<RewardSyncModeOption> RewardSyncModeOptions => mainWindowViewModel?.RewardSyncModeOptions ?? [];
public IReadOnlyList<TwitchRewardOption> RewardOptions => mainWindowViewModel?.RewardOptions ?? [];
```

If any option record type is not publicly accessible, make the manager expose `IEnumerable<object>` wrappers or move the option record visibility from private to public internal only as needed.

- [ ] **Step 2: Sync selected scale rule with parent before editor actions**

In `OpenEditor`, after `SelectedCard = card`, add:

```csharp
if (mainWindowViewModel is not null)
{
    mainWindowViewModel.SelectedAvatarScaleRule = SelectedAvatarScaleRule;
}
```

If the selected card belongs to a normal Scale Set, also set `SelectedAvatarScaleSet` to the owning set by scanning `Settings.AvatarScaleSets`.

- [ ] **Step 3: Move scale mode button logic into manager window**

In `AvatarScalingManagerWindow.xaml.cs`, add handlers adapted from `MainWindow.xaml.cs`:

```csharp
private AvatarScalingManagerViewModel Vm => (AvatarScalingManagerViewModel)DataContext;

private void ScaleActionModeButton_Click(object sender, RoutedEventArgs e)
{
    if (sender is not FrameworkElement fe || fe.Tag is not string tagName) return;
    if (!Enum.TryParse<AvatarScaleMode>(tagName, out var mode)) return;
    if (Vm.SelectedAvatarScaleRule is { } rule)
    {
        rule.ScaleMode = mode;
    }
}

private void ScaleActionMultOpButton_Click(object sender, RoutedEventArgs e)
{
    if (Vm.SelectedAvatarScaleRule is not { } rule) return;
    rule.MultiplierDirectionId = rule.MultiplierDirection == AvatarScaleMultiplierDirection.Grow
        ? (int)AvatarScaleMultiplierDirection.Divide
        : (int)AvatarScaleMultiplierDirection.Grow;
}

private void ScaleActionRelHeightOpButton_Click(object sender, RoutedEventArgs e)
{
    if (Vm.SelectedAvatarScaleRule is not { } rule) return;
    rule.RelativeHeightDirectionId = rule.IsSubtractRelativeHeight
        ? (int)AvatarScaleRelativeHeightDirection.Add
        : (int)AvatarScaleRelativeHeightDirection.Subtract;
}
```

Add `using VrcTwitchOscBridge.Models;`.

- [ ] **Step 4: Move color picker and Supporter Growth bit range handlers**

Copy the existing logic from `MainWindow.xaml.cs` methods:

- `OnPickManagedRewardColorClicked`
- `OnAddSupporterGrowthBitRangeClicked`
- `OnRemoveSupporterGrowthBitRangeClicked`

Adjust the data-context lookup to use `Vm.SelectedAvatarScaleRule` instead of `DataContext is MainWindowViewModel`.

- [ ] **Step 5: Expand XAML editor sections**

In `AvatarScalingManagerWindow.xaml`, replace the minimal editor body with sections ported from the current Avatar Scaling XAML:

- `Twitch Reward`
- `Height Change`
- `Timer & Return`
- `Safety & Pairing`

Keep these approved visual rules:

- editor stays in `Grid.Column="2"`
- input text uses `ComboSurfaceBrush` + `ComboTextBrush`
- every card and editor shows `Current Max Height Allowed`
- `Open Advanced Safety` stays inside `Safety & Pairing`

The first XAML port should prioritize compile correctness and the approved layout over full visual polish.

- [ ] **Step 6: Build and run XAML tests**

Run:

```powershell
dotnet test "E:\!!!Program to work on\Proper Crystal Relay\VrcTwitchOscBridge.Tests\VrcTwitchOscBridge.Tests.csproj" --no-restore --filter "FullyQualifiedName~AvatarScalingManagerWindowXamlTests|FullyQualifiedName~AvatarScalingManagerViewModelTests"
dotnet build "E:\!!!Program to work on\Proper Crystal Relay\VrcTwitchOscBridge\VrcTwitchOscBridge.csproj" --no-restore
```

Expected: tests PASS; build PASS.

- [ ] **Step 7: Checkpoint**

Do not commit unless explicitly approved by the user. If commits are approved, run:

```powershell
git add "VrcTwitchOscBridge/AvatarScalingManagerWindow.xaml" "VrcTwitchOscBridge/AvatarScalingManagerWindow.xaml.cs" "VrcTwitchOscBridge/ViewModels/AvatarScalingManagerViewModel.cs" "VrcTwitchOscBridge/ViewModels/MainWindowViewModel.cs"
git commit -m "feat(scale): wire avatar scaling manager editor"
```

---

### Task 9: Remove Or Disable Old Inline Avatar Scaling Workspace

**Files:**

- Modify: `VrcTwitchOscBridge/MainWindow.xaml`
- Modify: `VrcTwitchOscBridge/ViewModels/MainWindowViewModel.cs`

- [ ] **Step 1: Confirm new manager covers the old inline entry point**

Run the app debug build or inspect XAML. Confirm the Avatar Scaling button uses:

```xml
Command="{Binding OpenAvatarScalingManagerCommand}"
```

and does not call:

```xml
Command="{Binding ShowAvatarScalingCommand}"
```

- [ ] **Step 2: Remove obsolete inline Avatar Scaling command panel**

In `MainWindow.xaml`, remove the StackPanel scoped by:

```xml
<DataTrigger Binding="{Binding IsViewingAvatarScaling}" Value="True">
```

that contains these buttons:

- `Add Scale Set`
- `Delete Scale Set`
- `Enable All Scale Redeems`
- `Disable All Scale Redeems`
- `Delete Avatar Scale Sets`

- [ ] **Step 3: Remove obsolete inline AvatarScaleSets list**

In `MainWindow.xaml`, remove the `ListBox` with:

```xml
ItemsSource="{Binding AvatarScaleSets}"
SelectedItem="{Binding SelectedAvatarScaleSet}"
ItemTemplate="{StaticResource AvatarScaleSetListItemTemplate}"
```

Keep the `AvatarScaleSetListItemTemplate` resource only if the new manager uses it. If it is unused after the manager XAML is complete, remove it too.

- [ ] **Step 4: Remove obsolete inline Avatar Scaling workspace blocks**

In `MainWindow.xaml`, remove the content blocks scoped to `IsViewingAvatarScaling` that render:

- `Avatar Scaling`
- `Master Reward Redeem`
- `Scale Set Setup`
- `Scale Redeems In This Set`
- `Scale Redeem Setup`
- `Scale Action`

Do not remove the Avatar Scaling action editor blocks used by Cash Payments or Power Ups unless the new manager explicitly owns those exact embedded editors.

- [ ] **Step 5: Keep commands for manager reuse**

Do not delete these `MainWindowViewModel` commands yet because the manager delegates to them:

- `AddAvatarScaleSetCommand`
- `RemoveSelectedAvatarScaleSetCommand`
- `AddAvatarScaleRuleCommand`
- `RemoveSelectedAvatarScaleRuleCommand`
- `EnableAllAvatarScaleRulesCommand`
- `DisableAllAvatarScaleRulesCommand`
- `DeleteAllAvatarScaleRulesCommand`
- `TestSelectedAvatarScaleRuleCommand`
- `OpenAvatarScaleRuleLockoutPickerCommand`

- [ ] **Step 6: Build after XAML removal**

Run:

```powershell
dotnet build "E:\!!!Program to work on\Proper Crystal Relay\VrcTwitchOscBridge\VrcTwitchOscBridge.csproj" --no-restore
```

Expected: PASS.

- [ ] **Step 7: Checkpoint**

Do not commit unless explicitly approved by the user. If commits are approved, run:

```powershell
git add "VrcTwitchOscBridge/MainWindow.xaml" "VrcTwitchOscBridge/ViewModels/MainWindowViewModel.cs"
git commit -m "refactor(scale): remove inline avatar scaling workspace"
```

---

### Task 10: Add Localization

**Files:**

- Modify: `VrcTwitchOscBridge/Resources/Localization/en-US.extra.json`
- Modify: every non-English `VrcTwitchOscBridge/Resources/Localization/*.extra.json`

- [ ] **Step 1: Add English keys**

Add these keys to `en-US.extra.json`:

```json
{
  "Avatar Scaling Manager": "Avatar Scaling Manager",
  "Manage Twitch reward scaling, Supporter Growth, Cash Payment scaling, and Power Up scaling.": "Manage Twitch reward scaling, Supporter Growth, Cash Payment scaling, and Power Up scaling.",
  "Scaling Sources": "Scaling Sources",
  "Twitch Rewards": "Twitch Rewards",
  "Cash Payments": "Cash Payments",
  "Power Ups": "Power Ups",
  "Global Safety Rule": "Global Safety Rule",
  "Current Max Height Allowed": "Current Max Height Allowed",
  "Open Advanced Safety": "Open Advanced Safety",
  "Child Scale Rewards": "Child Scale Rewards",
  "Safety & Pairing": "Safety & Pairing",
  "Needs setup": "Needs setup",
  "Edit": "Edit"
}
```

Preserve valid JSON comma placement in the existing file.

- [ ] **Step 2: Add non-English translations**

Add matching keys to every `.extra.json` file listed below. Keep brand/technical terms in English where project rules require it: `Twitch`, `Supporter Growth`, `Cash Payment`, `Power Up`, `Avatar Scaling`, `Crystal Relay`, `VRChat`.

Files:

- `de-DE.extra.json`
- `es-ES.extra.json`
- `fr-FR.extra.json`
- `it-IT.extra.json`
- `ja-JP.extra.json`
- `ko-KR.extra.json`
- `pl-PL.extra.json`
- `pt-BR.extra.json`
- `ru-RU.extra.json`
- `sv-SE.extra.json`
- `th-TH.extra.json`
- `zh-CN.extra.json`
- `zh-TW.extra.json`

- [ ] **Step 3: Run localization audit**

Run:

```powershell
dotnet run --project "E:\!!!Program to work on\Proper Crystal Relay\LocalizationAudit\LocalizationAudit.csproj" --configuration Release -- "E:\!!!Program to work on\Proper Crystal Relay\VrcTwitchOscBridge\Resources\Localization"
```

Expected: PASS with no missing keys, empty values, or placeholder errors.

- [ ] **Step 4: Checkpoint**

Do not commit unless explicitly approved by the user. If commits are approved, run:

```powershell
git add "VrcTwitchOscBridge/Resources/Localization/*.extra.json"
git commit -m "feat(scale): localize avatar scaling manager"
```

---

### Task 11: Full Verification

**Files:**

- No code changes expected.

- [ ] **Step 1: Run focused tests**

Run:

```powershell
dotnet test "E:\!!!Program to work on\Proper Crystal Relay\VrcTwitchOscBridge.Tests\VrcTwitchOscBridge.Tests.csproj" --no-restore --filter "FullyQualifiedName~AvatarScaleSafetySettingsTests|FullyQualifiedName~BridgeRuntimeConfigurationAvatarScaleSafetyTests|FullyQualifiedName~AvatarScalingManagerViewModelTests|FullyQualifiedName~AvatarScalingManagerWindowXamlTests"
```

Expected: PASS.

- [ ] **Step 2: Run full test project**

Run:

```powershell
dotnet test "E:\!!!Program to work on\Proper Crystal Relay\VrcTwitchOscBridge.Tests\VrcTwitchOscBridge.Tests.csproj" --no-restore
```

Expected: PASS, except tests already marked `[Fact(Skip=...)]` remain skipped.

- [ ] **Step 3: Run localization audit**

Run:

```powershell
dotnet run --project "E:\!!!Program to work on\Proper Crystal Relay\LocalizationAudit\LocalizationAudit.csproj" --configuration Release -- "E:\!!!Program to work on\Proper Crystal Relay\VrcTwitchOscBridge\Resources\Localization"
```

Expected: PASS.

- [ ] **Step 4: Build app project**

Run:

```powershell
dotnet build "E:\!!!Program to work on\Proper Crystal Relay\VrcTwitchOscBridge\VrcTwitchOscBridge.csproj" --no-restore
```

Expected: PASS.

- [ ] **Step 5: Manual debug smoke test**

Run:

```powershell
E:\!!!Program to work on\Proper Crystal Relay\Launch-Crystal-Relay-Debug.bat
```

Manual expected results:

- Main window opens with ` - DEBUG` in the title.
- Clicking `Avatar Scaling` opens `Avatar Scaling Manager`.
- Clicking `Avatar Scaling` again focuses the existing manager instead of opening duplicates.
- `Twitch Rewards` page shows `Current Max Height Allowed`.
- Child reward cards show `Current max height allowed: 100m` or the configured value.
- Clicking a child reward opens the right-side editor, not a bottom editor.
- `Safety & Pairing` shows `Current max height allowed` and `Open Advanced Safety`.
- Cash Payment source shows only cash rules with `ActionKind == AvatarScaling`.
- Power Up source shows only Power Up rules with `ActionKind == AvatarScaling`.
- Linked existing rewards remain listen-only.
- Non-scaling Cash Payment and Power Up rules remain in their existing sections.

- [ ] **Step 6: Final checkpoint**

Do not commit unless explicitly approved by the user. If commits are approved, run:

```powershell
git status --short
git diff --stat
git add "VrcTwitchOscBridge" "VrcTwitchOscBridge.Tests" "docs/superpowers/plans/2026-06-27-avatar-scaling-manager-redesign.md"
git commit -m "feat(scale): add dedicated avatar scaling manager"
```
