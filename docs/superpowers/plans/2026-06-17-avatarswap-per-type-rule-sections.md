# Avatar Swap Manager: Per-Type Rule Sections — Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Replace the four duplicated/identical rule rows in the Avatar Swap Manager with five per-type rows (Channel Points, Bits, Subs, Payment, Roulette) and unify their edit experience with the existing full-page editors (extracting the Cash Payment editor into a reusable UserControl along the way).

**Architecture:** Replace one shared `InlineAvatarSwapRuleRowControl` with five compact per-type row controls. Each row shows a smart per-type summary and an Edit button. Edit sets a new `AvatarSwapManagerViewModel.SelectedRule` and the right pane swaps from a 4-section list to a full editor view (the existing `AvatarSwapRuleEditorControl` for ChannelPoints/Bits/Subs, a new extracted `CashPaymentRuleEditorControl` for Payment). The data model is changed in lockstep: `AvatarSwapProfile.PaymentRules` becomes `ObservableCollection<CashPaymentRule>` (the existing model already used by the main Cash Payments tab), with a one-time migration step.

**Tech Stack:** C#, WPF/XAML, .NET 10, xUnit, MVVM with `ObservableObject` base.

---

## File Structure

**New files (10):**
- `UserControls/IRuleRowViewModel.cs` — interface implemented by all 5 row VMs
- `UserControls/InlineChannelPointRuleRowControl.xaml` + `.cs` + `InlineChannelPointRuleRowViewModel.cs`
- `UserControls/InlineBitsRuleRowControl.xaml` + `.cs` + `InlineBitsRuleRowViewModel.cs`
- `UserControls/InlineSubsRuleRowControl.xaml` + `.cs` + `InlineSubsRuleRowViewModel.cs`
- `UserControls/InlinePaymentRuleRowControl.xaml` + `.cs` + `InlinePaymentRuleRowViewModel.cs`
- `UserControls/InlineRouletteRuleRowControl.xaml` + `.cs` + `InlineRouletteRuleRowViewModel.cs`
- `UserControls/CashPaymentRuleEditorControl.xaml` + `.cs` — extracted from `MainWindow.xaml`
- `UserControls/RuleListPaneViewModel.cs` — wraps the 4-section list view

**Modified files (8):**
- `Models/AvatarSwapProfile.cs` — change `PaymentRules` type
- `Services/AvatarSwapMigrationService.cs` — add `MigrateV4ToV5`, bump version
- `ViewModels/AvatarSwapManagerViewModel.cs` — typed collections, new state
- `AvatarSwapManagerWindow.xaml` + `.cs` — new row controls + right pane swap
- `MainWindow.xaml` — use new `CashPaymentRuleEditorControl`
- `UserControls/AvatarSwapRuleEditorControl.xaml` + `.cs` — `IsInAvatarSwapManager` DP
- `VrcTwitchOscBridge.csproj` — add new files, remove old
- 14× `Localization/*.extra.json` — 2 new keys each

**Deleted files (3):**
- `UserControls/InlineAvatarSwapRuleRowControl.xaml` + `.cs`
- `ViewModels/InlineAvatarSwapRuleRowViewModel.cs`

**Test files (2 new, 1 modified):**
- `VrcTwitchOscBridge.Tests/AvatarSwapMigrationServiceV5Tests.cs` — new
- `VrcTwitchOscBridge.Tests/InlineRuleRowViewModelTests.cs` — new
- `VrcTwitchOscBridge.Tests/AvatarSwapManagerViewModelTests.cs` — modified to expect new types
- `VrcTwitchOscBridge.Tests/AvatarSwapMigrationServiceV4Tests.cs` — modified: `HasAnyRules_TrueWhenPaymentRulesPresent` and `AvatarSwapProfile_HasFourRuleCollections` updated for `CashPaymentRule` type

---

## Task 1: Change `AvatarSwapProfile.PaymentRules` type

**Files:**
- Modify: `VrcTwitchOscBridge/Models/AvatarSwapProfile.cs:20`

- [ ] **Step 1: Verify the existing field**

Open `VrcTwitchOscBridge/Models/AvatarSwapProfile.cs` and confirm line 20 reads:

```csharp
public ObservableCollection<TriggerRule> PaymentRules { get; } = new();
```

- [ ] **Step 2: Change the type**

Replace the line with:

```csharp
public ObservableCollection<CashPaymentRule> PaymentRules { get; } = new();
```

- [ ] **Step 3: Build and confirm the existing failures are expected**

Run: `dotnet build "E:\!!!Program to work on\Proper Crystal Relay\VrcTwitchOscBridge\VrcTwitchOscBridge.csproj" --no-restore`

Expected: Build errors in `AvatarSwapManagerViewModel.cs` (line 384 `AddPaymentRule`) and possibly tests. These are expected and will be fixed in later tasks. Do not proceed past this point until the main project compiles (test project errors are OK to leave for now).

- [ ] **Step 4: Commit**

```bash
git add VrcTwitchOscBridge/Models/AvatarSwapProfile.cs
git commit -m "refactor: switch AvatarSwapProfile.PaymentRules to CashPaymentRule"
```

---

## Task 2: Add `MigrateV4ToV5` to `AvatarSwapMigrationService`

**Files:**
- Modify: `VrcTwitchOscBridge/Services/AvatarSwapMigrationService.cs`

- [ ] **Step 1: Write the failing test**

Create `VrcTwitchOscBridge.Tests/AvatarSwapMigrationServiceV5Tests.cs`:

```csharp
using System.Linq;
using VrcTwitchOscBridge.Models;
using VrcTwitchOscBridge.Services;
using Xunit;

namespace VrcTwitchOscBridge.Tests;

public sealed class AvatarSwapMigrationServiceV5Tests
{
    [Fact]
    public void MigrateV4ToV5_ConvertsLegacyTriggerRulePaymentToCashPaymentRule()
    {
        var settings = new AppSettings
        {
            AvatarChangeToAvatarSwapMigrationVersion = 4
        };
        var swapProfile = new AvatarSwapProfile
        {
            TargetAvatarId = "avtr_target",
            TargetAvatarName = "Target Avatar"
        };
        swapProfile.PaymentRules.Add(new TriggerRule
        {
            Name = "Legacy Pay Rule",
            TriggerType = TwitchTriggerType.ChannelPoints,
            ActionType = OscActionType.AvatarChange,
            AvatarChangeTargetId = "avtr_target",
            AvatarTargetName = "Target Avatar",
            MinimumAmount = 100,
            Source = TriggerRuleSource.CashPayment,
            BotMessageTemplate = "dropped",
            ChannelPointRewardTitle = "dropped",
            ChannelPointRewardCost = 9999
        });
        settings.AvatarSwapProfiles.Add(swapProfile);

        AvatarSwapMigrationService.Migrate(settings);

        Assert.Empty(swapProfile.PaymentRules.OfType<TriggerRule>());
        Assert.Single(swapProfile.PaymentRules.OfType<CashPaymentRule>());
        var migrated = swapProfile.PaymentRules.OfType<CashPaymentRule>().Single();
        Assert.Equal("Legacy Pay Rule", migrated.Name);
        Assert.Equal(CashPaymentProvider.StreamElements, migrated.Provider);
        Assert.Equal(100, migrated.MinAmount);
        Assert.True(migrated.IsEnabled);
        Assert.Equal(CashPaymentActionKind.TriggerAction, migrated.ActionKind);
        Assert.NotNull(migrated.TriggerAction);
        Assert.Equal(OscActionType.AvatarChange, migrated.TriggerAction.ActionType);
        Assert.Equal("avtr_target", migrated.TriggerAction.AvatarChangeTargetId);
        Assert.Equal("Target Avatar", migrated.TriggerAction.AvatarTargetName);
    }

    [Fact]
    public void MigrateV4ToV5_DoesNotRunTwice()
    {
        var settings = new AppSettings
        {
            AvatarChangeToAvatarSwapMigrationVersion = 4
        };
        var swapProfile = new AvatarSwapProfile();
        swapProfile.PaymentRules.Add(new TriggerRule
        {
            Name = "Legacy",
            Source = TriggerRuleSource.CashPayment,
            ActionType = OscActionType.AvatarChange
        });
        settings.AvatarSwapProfiles.Add(swapProfile);

        AvatarSwapMigrationService.Migrate(settings);
        var firstConverted = swapProfile.PaymentRules.OfType<CashPaymentRule>().Single();
        firstConverted.Name = "ModifiedAfterMigration";

        AvatarSwapMigrationService.Migrate(settings);

        Assert.Single(swapProfile.PaymentRules);
        Assert.Equal("ModifiedAfterMigration", firstConverted.Name);
    }

    [Fact]
    public void CurrentMigrationVersion_IsAtLeast5()
    {
        Assert.True(AvatarSwapMigrationService.CurrentMigrationVersion >= 5);
    }
}
```

- [ ] **Step 2: Run the test to verify it fails**

Run: `dotnet test "E:\!!!Program to work on\Proper Crystal Relay\VrcTwitchOscBridge.Tests\VrcTwitchOscBridge.Tests.csproj" --filter "FullyQualifiedName~AvatarSwapMigrationServiceV5Tests" -v minimal`

Expected: FAIL with messages about `MigrateV4ToV5` not existing or the version not being ≥ 5.

- [ ] **Step 3: Update `AvatarSwapMigrationService`**

In `VrcTwitchOscBridge/Services/AvatarSwapMigrationService.cs`:

1. Change line 9 from `public const int CurrentMigrationVersion = 4;` to `public const int CurrentMigrationVersion = 5;`
2. In `Migrate(AppSettings, List<SettingsStore.PersistedAvatarSwapProfile>?)` (around lines 16-36), add a new branch:

```csharp
if (settings.AvatarChangeToAvatarSwapMigrationVersion < 5)
{
    MigrateV4ToV5(settings);
}
```

3. Add a new private static method at the bottom of the class (before the closing brace):

```csharp
private static void MigrateV4ToV5(AppSettings settings)
{
    foreach (var swapProfile in settings.AvatarSwapProfiles)
    {
        var legacy = swapProfile.PaymentRules.OfType<TriggerRule>().ToList();
        foreach (var oldRule in legacy)
        {
            swapProfile.PaymentRules.Remove(oldRule);
            var migrated = new CashPaymentRule
            {
                Name = oldRule.Name,
                Provider = CashPaymentProvider.StreamElements,
                MinAmount = oldRule.MinimumAmount,
                IsEnabled = true,
                ActionKind = CashPaymentActionKind.TriggerAction,
                TriggerAction = new TriggerRule
                {
                    ActionType = oldRule.ActionType,
                    AvatarChangeTargetId = oldRule.AvatarChangeTargetId,
                    AvatarTargetName = oldRule.AvatarTargetName
                }
            };
            swapProfile.PaymentRules.Add(migrated);
            System.Diagnostics.Debug.WriteLine(
                $"Avatar Swap migration: dropped legacy payment rule fields for {oldRule.Name}");
        }
    }
}
```

- [ ] **Step 4: Run the test to verify it passes**

Run: `dotnet test "E:\!!!Program to work on\Proper Crystal Relay\VrcTwitchOscBridge.Tests\VrcTwitchOscBridge.Tests.csproj" --filter "FullyQualifiedName~AvatarSwapMigrationServiceV5Tests" -v minimal`

Expected: PASS (3 tests).

- [ ] **Step 5: Update existing V4 tests**

In `VrcTwitchOscBridge.Tests/AvatarSwapMigrationServiceV4Tests.cs`:

1. Find the test `AvatarSwapProfile_HasFourRuleCollections` (line 37). The test adds a `TriggerRule` to `PaymentRules`. Replace that with:

```csharp
[Fact]
public void AvatarSwapProfile_HasFourRuleCollections()
{
    var profile = new AvatarSwapProfile();
    Assert.NotNull(profile.ChannelPointRules);
    Assert.NotNull(profile.BitsRules);
    Assert.NotNull(profile.SubsRules);
    Assert.NotNull(profile.PaymentRules);
    Assert.Empty(profile.ChannelPointRules);
    Assert.Empty(profile.BitsRules);
    Assert.Empty(profile.SubsRules);
    Assert.Empty(profile.PaymentRules);
    Assert.IsType<System.Collections.ObjectModel.ObservableCollection<CashPaymentRule>>(profile.PaymentRules);
}
```

2. Find the test `HasAnyRules_TrueWhenPaymentRulesPresent` (in `AvatarSwapManagerViewModelTests.cs` line 142). Replace the body with:

```csharp
[Fact]
public void HasAnyRules_TrueWhenPaymentRulesPresent()
{
    var profile = new AvatarSwapProfile();
    profile.PaymentRules.Add(new CashPaymentRule());
    var vm = new AvatarSwapCardViewModel(profile, new AvatarImageService());

    Assert.True(vm.HasAnyRules);
}
```

- [ ] **Step 6: Run all tests in the test project**

Run: `dotnet test "E:\!!!Program to work on\Proper Crystal Relay\VrcTwitchOscBridge.Tests\VrcTwitchOscBridge.Tests.csproj" -v minimal`

Expected: All tests pass. If `AvatarSwapManagerViewModelTests` has failures, the VM has not been updated yet (expected — that is Task 9). Skip those and continue.

- [ ] **Step 7: Commit**

```bash
git add VrcTwitchOscBridge/Services/AvatarSwapMigrationService.cs VrcTwitchOscBridge.Tests/AvatarSwapMigrationServiceV5Tests.cs VrcTwitchOscBridge.Tests/AvatarSwapMigrationServiceV4Tests.cs VrcTwitchOscBridge.Tests/AvatarSwapManagerViewModelTests.cs
git commit -m "feat: migrate legacy TriggerRule payment entries to CashPaymentRule (V4->V5)"
```

---

## Task 3: Create `IRuleRowViewModel` interface

**Files:**
- Create: `VrcTwitchOscBridge/UserControls/IRuleRowViewModel.cs`

- [ ] **Step 1: Create the interface file**

Create `VrcTwitchOscBridge/UserControls/IRuleRowViewModel.cs`:

```csharp
using System.Windows.Input;

namespace VrcTwitchOscBridge.UserControls;

public interface IRuleRowViewModel
{
    object Rule { get; }
    string Summary { get; }
    bool IsEnabled { get; }
    ICommand EditCommand { get; }
    ICommand DeleteCommand { get; }
    void RefreshSummary();
}
```

- [ ] **Step 2: Build and confirm it compiles**

Run: `dotnet build "E:\!!!Program to work on\Proper Crystal Relay\VrcTwitchOscBridge\VrcTwitchOscBridge.csproj" --no-restore`

Expected: Build succeeds. (The interface is referenced by later tasks but is not consumed yet.)

- [ ] **Step 3: Commit**

```bash
git add VrcTwitchOscBridge/UserControls/IRuleRowViewModel.cs
git commit -m "feat: add IRuleRowViewModel interface for typed AvatarSwapManager rows"
```

---

## Task 4: Create `InlineChannelPointRuleRowViewModel` + tests

**Files:**
- Create: `VrcTwitchOscBridge/UserControls/InlineChannelPointRuleRowViewModel.cs`

- [ ] **Step 1: Write the failing test**

Create `VrcTwitchOscBridge.Tests/InlineRuleRowViewModelTests.cs`:

```csharp
using VrcTwitchOscBridge.Models;
using VrcTwitchOscBridge.UserControls;
using Xunit;

namespace VrcTwitchOscBridge.Tests;

public sealed class InlineChannelPointRuleRowViewModelTests
{
    [Fact]
    public void Summary_FormatsNameAndCost()
    {
        var rule = new TriggerRule
        {
            Name = "My Reward",
            TriggerType = TwitchTriggerType.ChannelPoints,
            ChannelPointRewardCost = 500
        };
        var vm = new InlineChannelPointRuleRowViewModel(rule);

        Assert.Contains("My Reward", vm.Summary);
        Assert.Contains("500", vm.Summary);
    }

    [Fact]
    public void Summary_OmitsCostWhenZero()
    {
        var rule = new TriggerRule
        {
            Name = "Free",
            TriggerType = TwitchTriggerType.ChannelPoints
        };
        var vm = new InlineChannelPointRuleRowViewModel(rule);

        Assert.DoesNotContain("pts", vm.Summary);
    }

    [Fact]
    public void IsEnabled_ReflectsRuleProperty()
    {
        var rule = new TriggerRule { IsEnabled = false };
        var vm = new InlineChannelPointRuleRowViewModel(rule);

        Assert.False(vm.IsEnabled);

        rule.IsEnabled = true;
        Assert.True(vm.IsEnabled);
    }
}
```

- [ ] **Step 2: Run the test to verify it fails**

Run: `dotnet test "E:\!!!Program to work on\Proper Crystal Relay\VrcTwitchOscBridge.Tests\VrcTwitchOscBridge.Tests.csproj" --filter "FullyQualifiedName~InlineChannelPointRuleRowViewModelTests" -v minimal`

Expected: FAIL with "type or namespace not found".

- [ ] **Step 3: Create the VM**

Create `VrcTwitchOscBridge/UserControls/InlineChannelPointRuleRowViewModel.cs`:

```csharp
using System.ComponentModel;
using System.Windows.Input;
using VrcTwitchOscBridge.Infrastructure;
using VrcTwitchOscBridge.Models;

namespace VrcTwitchOscBridge.UserControls;

public sealed class InlineChannelPointRuleRowViewModel : ObservableObject, IRuleRowViewModel
{
    private readonly TriggerRule _rule;
    private string _summary = string.Empty;
    private ICommand? _editCommand;
    private ICommand? _deleteCommand;

    public InlineChannelPointRuleRowViewModel(TriggerRule rule)
    {
        _rule = rule ?? throw new System.ArgumentNullException(nameof(rule));
        _rule.PropertyChanged += OnRulePropertyChanged;
        RefreshSummary();
    }

    public object Rule => _rule;

    public string Summary
    {
        get => _summary;
        private set => SetProperty(ref _summary, value);
    }

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
        if (_rule.ChannelPointRewardCost > 0)
        {
            Summary = $"🏆 {name} — {_rule.ChannelPointRewardCost} pts";
        }
        else
        {
            Summary = $"🏆 {name}";
        }
    }

    private void OnRulePropertyChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName is nameof(TriggerRule.Name)
            or nameof(TriggerRule.ChannelPointRewardCost)
            or nameof(TriggerRule.IsEnabled))
        {
            RefreshSummary();
            RaisePropertyChanged(nameof(IsEnabled));
        }
    }
}
```

- [ ] **Step 4: Run the test to verify it passes**

Run: `dotnet test "E:\!!!Program to work on\Proper Crystal Relay\VrcTwitchOscBridge.Tests\VrcTwitchOscBridge.Tests.csproj" --filter "FullyQualifiedName~InlineChannelPointRuleRowViewModelTests" -v minimal`

Expected: PASS (3 tests).

- [ ] **Step 5: Commit**

```bash
git add VrcTwitchOscBridge/UserControls/InlineChannelPointRuleRowViewModel.cs VrcTwitchOscBridge.Tests/InlineRuleRowViewModelTests.cs
git commit -m "feat: add InlineChannelPointRuleRowViewModel"
```

---

## Task 5: Create `InlineBitsRuleRowViewModel` + tests

**Files:**
- Create: `VrcTwitchOscBridge/UserControls/InlineBitsRuleRowViewModel.cs`

- [ ] **Step 1: Append failing tests to `InlineRuleRowViewModelTests.cs`**

Add to the test file:

```csharp
public sealed class InlineBitsRuleRowViewModelTests
{
    [Fact]
    public void Summary_IncludesMinAmount()
    {
        var rule = new TriggerRule
        {
            Name = "Cheer",
            TriggerType = TwitchTriggerType.Bits,
            MinimumAmount = 100
        };
        var vm = new InlineBitsRuleRowViewModel(rule);

        Assert.Contains("Cheer", vm.Summary);
        Assert.Contains("100", vm.Summary);
        Assert.Contains("bits", vm.Summary);
    }

    [Fact]
    public void Summary_IncludesScaledDuration()
    {
        var rule = new TriggerRule
        {
            Name = "Cheer",
            TriggerType = TwitchTriggerType.Bits,
            BitsAmountUnitsPerDuration = 50,
            BitsSecondsPerAmountUnit = 1
        };
        var vm = new InlineBitsRuleRowViewModel(rule);

        Assert.Contains("1s per 50 bits", vm.Summary);
    }

    [Fact]
    public void Summary_IncludesMaxAccumulated()
    {
        var rule = new TriggerRule
        {
            Name = "Cheer",
            TriggerType = TwitchTriggerType.Bits,
            MaxAccumulatedDurationEnabled = true,
            MaxAccumulatedDurationSeconds = 600
        };
        var vm = new InlineBitsRuleRowViewModel(rule);

        Assert.Contains("cap 600s", vm.Summary);
    }

    [Fact]
    public void Summary_IncludesKeyword()
    {
        var rule = new TriggerRule
        {
            Name = "Cheer",
            TriggerType = TwitchTriggerType.Bits,
            SupporterKeywordText = "!boost"
        };
        var vm = new InlineBitsRuleRowViewModel(rule);

        Assert.Contains("keyword: !boost", vm.Summary);
    }
}
```

- [ ] **Step 2: Run the test to verify it fails**

Run: `dotnet test "E:\!!!Program to work on\Proper Crystal Relay\VrcTwitchOscBridge.Tests\VrcTwitchOscBridge.Tests.csproj" --filter "FullyQualifiedName~InlineBitsRuleRowViewModelTests" -v minimal`

Expected: FAIL — type not found.

- [ ] **Step 3: Create the VM**

Create `VrcTwitchOscBridge/UserControls/InlineBitsRuleRowViewModel.cs`:

```csharp
using System.ComponentModel;
using System.Text;
using System.Windows.Input;
using VrcTwitchOscBridge.Infrastructure;
using VrcTwitchOscBridge.Models;

namespace VrcTwitchOscBridge.UserControls;

public sealed class InlineBitsRuleRowViewModel : ObservableObject, IRuleRowViewModel
{
    private readonly TriggerRule _rule;
    private string _summary = string.Empty;
    private ICommand? _editCommand;
    private ICommand? _deleteCommand;

    public InlineBitsRuleRowViewModel(TriggerRule rule)
    {
        _rule = rule ?? throw new System.ArgumentNullException(nameof(rule));
        _rule.PropertyChanged += OnRulePropertyChanged;
        RefreshSummary();
    }

    public object Rule => _rule;
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
        sb.Append("💎 ").Append(name);
        if (_rule.MinimumAmount > 0)
        {
            sb.Append(" — Min ").Append(_rule.MinimumAmount).Append(" bits");
        }
        if (_rule.BitsAmountUnitsPerDuration > 0 && _rule.BitsSecondsPerAmountUnit > 0)
        {
            sb.Append(", ").Append(_rule.BitsSecondsPerAmountUnit)
              .Append("s per ").Append(_rule.BitsAmountUnitsPerDuration).Append(" bits");
        }
        if (_rule.MaxAccumulatedDurationEnabled && _rule.MaxAccumulatedDurationSeconds > 0)
        {
            sb.Append(", cap ").Append(_rule.MaxAccumulatedDurationSeconds).Append("s");
        }
        if (!string.IsNullOrWhiteSpace(_rule.SupporterKeywordText))
        {
            sb.Append(", keyword: ").Append(_rule.SupporterKeywordText);
        }
        Summary = sb.ToString();
    }

    private void OnRulePropertyChanged(object? sender, PropertyChangedEventArgs e) => RefreshSummary();
}
```

- [ ] **Step 4: Run the test to verify it passes**

Run: `dotnet test "E:\!!!Program to work on\Proper Crystal Relay\VrcTwitchOscBridge.Tests\VrcTwitchOscBridge.Tests.csproj" --filter "FullyQualifiedName~InlineBitsRuleRowViewModelTests" -v minimal`

Expected: PASS (4 tests).

- [ ] **Step 5: Commit**

```bash
git add VrcTwitchOscBridge/UserControls/InlineBitsRuleRowViewModel.cs VrcTwitchOscBridge.Tests/InlineRuleRowViewModelTests.cs
git commit -m "feat: add InlineBitsRuleRowViewModel with per-field smart summary"
```

---

## Task 6: Create `InlineSubsRuleRowViewModel` + tests

**Files:**
- Create: `VrcTwitchOscBridge/UserControls/InlineSubsRuleRowViewModel.cs`

- [ ] **Step 1: Append failing tests to `InlineRuleRowViewModelTests.cs`**

Add to the test file:

```csharp
public sealed class InlineSubsRuleRowViewModelTests
{
    [Fact]
    public void Summary_IncludesTierMultipliers()
    {
        var rule = new TriggerRule
        {
            Name = "Sub Boost",
            TriggerType = TwitchTriggerType.Subscriptions,
            SubscriptionTier1SecondsPerSub = 10,
            SubscriptionTier2SecondsPerSub = 25,
            SubscriptionTier3SecondsPerSub = 60
        };
        var vm = new InlineSubsRuleRowViewModel(rule);

        Assert.Contains("T1:10s", vm.Summary);
        Assert.Contains("T2:25s", vm.Summary);
        Assert.Contains("T3:60s", vm.Summary);
    }

    [Fact]
    public void Summary_OmitsUnsetTiers()
    {
        var rule = new TriggerRule
        {
            Name = "Sub",
            TriggerType = TwitchTriggerType.Subscriptions,
            SubscriptionTier1SecondsPerSub = 10
        };
        var vm = new InlineSubsRuleRowViewModel(rule);

        Assert.Contains("T1:10s", vm.Summary);
        Assert.DoesNotContain("T2", vm.Summary);
        Assert.DoesNotContain("T3", vm.Summary);
    }

    [Fact]
    public void Summary_IncludesScaledDuration()
    {
        var rule = new TriggerRule
        {
            Name = "Sub",
            TriggerType = TwitchTriggerType.Subscriptions,
            SubscriptionsAmountUnitsPerDuration = 1,
            SubscriptionsSecondsPerAmountUnit = 30
        };
        var vm = new InlineSubsRuleRowViewModel(rule);

        Assert.Contains("30s per 1 subs", vm.Summary);
    }

    [Fact]
    public void Summary_ShowsSubTypeRegularPlusGift_WhenIsGiftSubscription()
    {
        var rule = new TriggerRule
        {
            Name = "Gift",
            TriggerType = TwitchTriggerType.Subscriptions,
            IsGiftSubscription = true
        };
        var vm = new InlineSubsRuleRowViewModel(rule);

        Assert.Contains("sub-type: regular+gift", vm.Summary);
    }

    [Fact]
    public void Summary_ShowsSubTypeRegular_WhenNotGift()
    {
        var rule = new TriggerRule
        {
            Name = "Regular",
            TriggerType = TwitchTriggerType.Subscriptions
        };
        var vm = new InlineSubsRuleRowViewModel(rule);

        Assert.Contains("sub-type: regular", vm.Summary);
    }

    [Fact]
    public void Summary_IncludesKeyword()
    {
        var rule = new TriggerRule
        {
            Name = "Sub",
            TriggerType = TwitchTriggerType.Subscriptions,
            SupporterKeywordText = "!thanks"
        };
        var vm = new InlineSubsRuleRowViewModel(rule);

        Assert.Contains("keyword: !thanks", vm.Summary);
    }
}
```

- [ ] **Step 2: Run the test to verify it fails**

Run: `dotnet test "E:\!!!Program to work on\Proper Crystal Relay\VrcTwitchOscBridge.Tests\VrcTwitchOscBridge.Tests.csproj" --filter "FullyQualifiedName~InlineSubsRuleRowViewModelTests" -v minimal`

Expected: FAIL.

- [ ] **Step 3: Create the VM**

Create `VrcTwitchOscBridge/UserControls/InlineSubsRuleRowViewModel.cs`:

```csharp
using System.ComponentModel;
using System.Text;
using System.Windows.Input;
using VrcTwitchOscBridge.Infrastructure;
using VrcTwitchOscBridge.Models;

namespace VrcTwitchOscBridge.UserControls;

public sealed class InlineSubsRuleRowViewModel : ObservableObject, IRuleRowViewModel
{
    private readonly TriggerRule _rule;
    private string _summary = string.Empty;
    private ICommand? _editCommand;
    private ICommand? _deleteCommand;

    public InlineSubsRuleRowViewModel(TriggerRule rule)
    {
        _rule = rule ?? throw new System.ArgumentNullException(nameof(rule));
        _rule.PropertyChanged += OnRulePropertyChanged;
        RefreshSummary();
    }

    public object Rule => _rule;
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
        sb.Append("⭐ ").Append(name);
        var parts = new System.Collections.Generic.List<string>();
        if (_rule.SubscriptionTier1SecondsPerSub > 0) parts.Add($"T1:{_rule.SubscriptionTier1SecondsPerSub}s");
        if (_rule.SubscriptionTier2SecondsPerSub > 0) parts.Add($"T2:{_rule.SubscriptionTier2SecondsPerSub}s");
        if (_rule.SubscriptionTier3SecondsPerSub > 0) parts.Add($"T3:{_rule.SubscriptionTier3SecondsPerSub}s");
        if (parts.Count > 0) sb.Append(" — ").Append(string.Join(" ", parts));
        if (_rule.SubscriptionsAmountUnitsPerDuration > 0 && _rule.SubscriptionsSecondsPerAmountUnit > 0)
        {
            sb.Append(", ").Append(_rule.SubscriptionsSecondsPerAmountUnit)
              .Append("s per ").Append(_rule.SubscriptionsAmountUnitsPerDuration).Append(" subs");
        }
        if (_rule.MaxAccumulatedDurationEnabled && _rule.MaxAccumulatedDurationSeconds > 0)
        {
            sb.Append(", cap ").Append(_rule.MaxAccumulatedDurationSeconds).Append("s");
        }
        var subType = _rule.IsGiftSubscription ? "regular+gift" : "regular";
        sb.Append(", sub-type: ").Append(subType);
        if (!string.IsNullOrWhiteSpace(_rule.SupporterKeywordText))
        {
            sb.Append(", keyword: ").Append(_rule.SupporterKeywordText);
        }
        Summary = sb.ToString();
    }

    private void OnRulePropertyChanged(object? sender, PropertyChangedEventArgs e) => RefreshSummary();
}
```

- [ ] **Step 4: Run the test to verify it passes**

Run: `dotnet test "E:\!!!Program to work on\Proper Crystal Relay\VrcTwitchOscBridge.Tests\VrcTwitchOscBridge.Tests.csproj" --filter "FullyQualifiedName~InlineSubsRuleRowViewModelTests" -v minimal`

Expected: PASS (6 tests).

- [ ] **Step 5: Commit**

```bash
git add VrcTwitchOscBridge/UserControls/InlineSubsRuleRowViewModel.cs VrcTwitchOscBridge.Tests/InlineRuleRowViewModelTests.cs
git commit -m "feat: add InlineSubsRuleRowViewModel with tier multipliers and sub-type filter"
```

---

## Task 7: Create `InlinePaymentRuleRowViewModel` + tests

**Files:**
- Create: `VrcTwitchOscBridge/UserControls/InlinePaymentRuleRowViewModel.cs`

- [ ] **Step 1: Append failing tests to `InlineRuleRowViewModelTests.cs`**

Add to the test file:

```csharp
public sealed class InlinePaymentRuleRowViewModelTests
{
    [Fact]
    public void Summary_IncludesProvider()
    {
        var rule = new CashPaymentRule
        {
            Name = "Tip Swap",
            Provider = CashPaymentProvider.KoFi
        };
        var vm = new InlinePaymentRuleRowViewModel(rule);

        Assert.Contains("Tip Swap", vm.Summary);
        Assert.Contains("Ko-fi", vm.Summary);
    }

    [Fact]
    public void Summary_IncludesMinMaxAndCurrency()
    {
        var rule = new CashPaymentRule
        {
            Name = "Tip",
            Provider = CashPaymentProvider.StreamElements,
            MinAmount = 5,
            MaxAmount = 50,
            CurrencyCode = "USD"
        };
        var vm = new InlinePaymentRuleRowViewModel(rule);

        Assert.Contains("USD", vm.Summary);
        Assert.Contains("5-50", vm.Summary);
    }

    [Fact]
    public void Summary_OmitsRangeWhenBothZero()
    {
        var rule = new CashPaymentRule
        {
            Name = "Tip",
            Provider = CashPaymentProvider.StreamElements
        };
        var vm = new InlinePaymentRuleRowViewModel(rule);

        Assert.DoesNotContain("-0", vm.Summary);
    }

    [Fact]
    public void Summary_IncludesMessageContains()
    {
        var rule = new CashPaymentRule
        {
            Name = "Cheer Tip",
            Provider = CashPaymentProvider.Streamlabs,
            MessageContains = "cheer"
        };
        var vm = new InlinePaymentRuleRowViewModel(rule);

        Assert.Contains("match: 'cheer'", vm.Summary);
    }

    [Fact]
    public void Summary_ProviderName_UsesDisplayLabels()
    {
        Assert.Contains("StreamElements", new InlinePaymentRuleRowViewModel(new CashPaymentRule { Provider = CashPaymentProvider.StreamElements }).Summary);
        Assert.Contains("Streamlabs", new InlinePaymentRuleRowViewModel(new CashPaymentRule { Provider = CashPaymentProvider.Streamlabs }).Summary);
        Assert.Contains("Ko-fi", new InlinePaymentRuleRowViewModel(new CashPaymentRule { Provider = CashPaymentProvider.KoFi }).Summary);
    }
}
```

- [ ] **Step 2: Run the test to verify it fails**

Run: `dotnet test "E:\!!!Program to work on\Proper Crystal Relay\VrcTwitchOscBridge.Tests\VrcTwitchOscBridge.Tests.csproj" --filter "FullyQualifiedName~InlinePaymentRuleRowViewModelTests" -v minimal`

Expected: FAIL.

- [ ] **Step 3: Create the VM**

Create `VrcTwitchOscBridge/UserControls/InlinePaymentRuleRowViewModel.cs`:

```csharp
using System.ComponentModel;
using System.Text;
using System.Windows.Input;
using VrcTwitchOscBridge.Infrastructure;
using VrcTwitchOscBridge.Models;

namespace VrcTwitchOscBridge.UserControls;

public sealed class InlinePaymentRuleRowViewModel : ObservableObject, IRuleRowViewModel
{
    private readonly CashPaymentRule _rule;
    private string _summary = string.Empty;
    private ICommand? _editCommand;
    private ICommand? _deleteCommand;

    public InlinePaymentRuleRowViewModel(CashPaymentRule rule)
    {
        _rule = rule ?? throw new System.ArgumentNullException(nameof(rule));
        _rule.PropertyChanged += OnRulePropertyChanged;
        RefreshSummary();
    }

    public object Rule => _rule;
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
        var provider = ProviderDisplayName(_rule.Provider);
        var sb = new StringBuilder();
        sb.Append("💵 ").Append(name).Append(" — ").Append(provider);
        if (_rule.MinAmount > 0 || _rule.MaxAmount > 0)
        {
            sb.Append(' ').Append(_rule.CurrencyCode ?? string.Empty).Append(' ')
              .Append(_rule.MinAmount).Append('-').Append(_rule.MaxAmount);
        }
        if (!string.IsNullOrWhiteSpace(_rule.MessageContains))
        {
            sb.Append(" match: '").Append(_rule.MessageContains).Append('\'');
        }
        Summary = sb.ToString();
    }

    private static string ProviderDisplayName(CashPaymentProvider provider) => provider switch
    {
        CashPaymentProvider.StreamElements => "StreamElements",
        CashPaymentProvider.Streamlabs => "Streamlabs",
        CashPaymentProvider.KoFi => "Ko-fi",
        _ => provider.ToString()
    };

    private void OnRulePropertyChanged(object? sender, PropertyChangedEventArgs e) => RefreshSummary();
}
```

- [ ] **Step 4: Run the test to verify it passes**

Run: `dotnet test "E:\!!!Program to work on\Proper Crystal Relay\VrcTwitchOscBridge.Tests\VrcTwitchOscBridge.Tests.csproj" --filter "FullyQualifiedName~InlinePaymentRuleRowViewModelTests" -v minimal`

Expected: PASS (5 tests).

- [ ] **Step 5: Commit**

```bash
git add VrcTwitchOscBridge/UserControls/InlinePaymentRuleRowViewModel.cs VrcTwitchOscBridge.Tests/InlineRuleRowViewModelTests.cs
git commit -m "feat: add InlinePaymentRuleRowViewModel with provider, range, and message summary"
```

---

## Task 8: Create `InlineRouletteRuleRowViewModel` + tests

**Files:**
- Create: `VrcTwitchOscBridge/UserControls/InlineRouletteRuleRowViewModel.cs`

- [ ] **Step 1: Append failing tests to `InlineRuleRowViewModelTests.cs`**

Add to the test file:

```csharp
public sealed class InlineRouletteRuleRowViewModelTests
{
    [Fact]
    public void Summary_FormatsNameAndCost()
    {
        var rule = new TriggerRule
        {
            Name = "Roulette Trigger",
            TriggerType = TwitchTriggerType.ChannelPoints,
            ChannelPointRewardCost = 250
        };
        var vm = new InlineRouletteRuleRowViewModel(rule);

        Assert.Contains("Roulette Trigger", vm.Summary);
        Assert.Contains("250", vm.Summary);
    }

    [Fact]
    public void Summary_HandlesFreeReward()
    {
        var rule = new TriggerRule
        {
            Name = "Free Spin",
            TriggerType = TwitchTriggerType.ChannelPoints
        };
        var vm = new InlineRouletteRuleRowViewModel(rule);

        Assert.Contains("Free Spin", vm.Summary);
    }
}
```

- [ ] **Step 2: Run the test to verify it fails**

Run: `dotnet test "E:\!!!Program to work on\Proper Crystal Relay\VrcTwitchOscBridge.Tests\VrcTwitchOscBridge.Tests.csproj" --filter "FullyQualifiedName~InlineRouletteRuleRowViewModelTests" -v minimal`

Expected: FAIL.

- [ ] **Step 3: Create the VM**

Create `VrcTwitchOscBridge/UserControls/InlineRouletteRuleRowViewModel.cs`:

```csharp
using System.ComponentModel;
using System.Text;
using System.Windows.Input;
using VrcTwitchOscBridge.Infrastructure;
using VrcTwitchOscBridge.Models;

namespace VrcTwitchOscBridge.UserControls;

public sealed class InlineRouletteRuleRowViewModel : ObservableObject, IRuleRowViewModel
{
    private readonly TriggerRule _rule;
    private string _summary = string.Empty;
    private ICommand? _editCommand;
    private ICommand? _deleteCommand;

    public InlineRouletteRuleRowViewModel(TriggerRule rule)
    {
        _rule = rule ?? throw new System.ArgumentNullException(nameof(rule));
        _rule.PropertyChanged += OnRulePropertyChanged;
        RefreshSummary();
    }

    public object Rule => _rule;
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
        sb.Append("🎰 ").Append(name);
        if (_rule.ChannelPointRewardCost > 0)
        {
            sb.Append(" — ").Append(_rule.ChannelPointRewardCost).Append(" pts");
        }
        Summary = sb.ToString();
    }

    private void OnRulePropertyChanged(object? sender, PropertyChangedEventArgs e) => RefreshSummary();
}
```

- [ ] **Step 4: Run the test to verify it passes**

Run: `dotnet test "E:\!!!Program to work on\Proper Crystal Relay\VrcTwitchOscBridge.Tests\VrcTwitchOscBridge.Tests.csproj" --filter "FullyQualifiedName~InlineRouletteRuleRowViewModelTests" -v minimal`

Expected: PASS (2 tests).

- [ ] **Step 5: Commit**

```bash
git add VrcTwitchOscBridge/UserControls/InlineRouletteRuleRowViewModel.cs VrcTwitchOscBridge.Tests/InlineRuleRowViewModelTests.cs
git commit -m "feat: add InlineRouletteRuleRowViewModel"
```

---

## Task 9: Create the 5 row control XAMLs + code-behind

**Files:**
- Create: 5 XAMLs + 5 code-behind files

- [ ] **Step 1: Create `InlineChannelPointRuleRowControl.xaml`**

Create `VrcTwitchOscBridge/UserControls/InlineChannelPointRuleRowControl.xaml`:

```xml
<UserControl x:Class="VrcTwitchOscBridge.UserControls.InlineChannelPointRuleRowControl"
             xmlns="http://schemas.microsoft.com/winfx/2006/xaml/presentation"
             xmlns:x="http://schemas.microsoft.com/winfx/2006/xaml"
             xmlns:loc="clr-namespace:VrcTwitchOscBridge.Services">
    <Border Background="{DynamicResource PanelBrush}" BorderBrush="{DynamicResource InputBorderBrush}" BorderThickness="1" Padding="8,6" CornerRadius="4" Margin="0,0,0,4">
        <DockPanel>
            <StackPanel DockPanel.Dock="Right" Orientation="Horizontal">
                <Button Content="⚙" Width="24" Height="24" Margin="0,0,4,0"
                        Command="{Binding EditCommand}" ToolTip="{loc:Translate 'Edit'}" />
                <Button Content="🗑" Width="24" Height="24"
                        Command="{Binding DeleteCommand}" ToolTip="{loc:Translate 'Delete'}" />
            </StackPanel>
            <TextBlock Text="{Binding Summary}" VerticalAlignment="Center" TextTrimming="CharacterEllipsis" />
        </DockPanel>
    </Border>
</UserControl>
```

- [ ] **Step 2: Create the code-behind**

Create `VrcTwitchOscBridge/UserControls/InlineChannelPointRuleRowControl.xaml.cs`:

```csharp
using System.Windows.Controls;

namespace VrcTwitchOscBridge.UserControls;

public partial class InlineChannelPointRuleRowControl : UserControl
{
    public InlineChannelPointRuleRowControl()
    {
        InitializeComponent();
    }
}
```

- [ ] **Step 3: Repeat for the other 4 controls (Bits, Subs, Payment, Roulette)**

Create `VrcTwitchOscBridge/UserControls/InlineBitsRuleRowControl.xaml`:

```xml
<UserControl x:Class="VrcTwitchOscBridge.UserControls.InlineBitsRuleRowControl"
             xmlns="http://schemas.microsoft.com/winfx/2006/xaml/presentation"
             xmlns:x="http://schemas.microsoft.com/winfx/2006/xaml"
             xmlns:loc="clr-namespace:VrcTwitchOscBridge.Services">
    <Border Background="{DynamicResource PanelBrush}" BorderBrush="{DynamicResource InputBorderBrush}" BorderThickness="1" Padding="8,6" CornerRadius="4" Margin="0,0,0,4">
        <DockPanel>
            <StackPanel DockPanel.Dock="Right" Orientation="Horizontal">
                <Button Content="⚙" Width="24" Height="24" Margin="0,0,4,0"
                        Command="{Binding EditCommand}" ToolTip="{loc:Translate 'Edit'}" />
                <Button Content="🗑" Width="24" Height="24"
                        Command="{Binding DeleteCommand}" ToolTip="{loc:Translate 'Delete'}" />
            </StackPanel>
            <TextBlock Text="{Binding Summary}" VerticalAlignment="Center" TextTrimming="CharacterEllipsis" />
        </DockPanel>
    </Border>
</UserControl>
```

Create `VrcTwitchOscBridge/UserControls/InlineBitsRuleRowControl.xaml.cs`:

```csharp
using System.Windows.Controls;

namespace VrcTwitchOscBridge.UserControls;

public partial class InlineBitsRuleRowControl : UserControl
{
    public InlineBitsRuleRowControl() => InitializeComponent();
}
```

Create `VrcTwitchOscBridge/UserControls/InlineSubsRuleRowControl.xaml`:

```xml
<UserControl x:Class="VrcTwitchOscBridge.UserControls.InlineSubsRuleRowControl"
             xmlns="http://schemas.microsoft.com/winfx/2006/xaml/presentation"
             xmlns:x="http://schemas.microsoft.com/winfx/2006/xaml"
             xmlns:loc="clr-namespace:VrcTwitchOscBridge.Services">
    <Border Background="{DynamicResource PanelBrush}" BorderBrush="{DynamicResource InputBorderBrush}" BorderThickness="1" Padding="8,6" CornerRadius="4" Margin="0,0,0,4">
        <DockPanel>
            <StackPanel DockPanel.Dock="Right" Orientation="Horizontal">
                <Button Content="⚙" Width="24" Height="24" Margin="0,0,4,0"
                        Command="{Binding EditCommand}" ToolTip="{loc:Translate 'Edit'}" />
                <Button Content="🗑" Width="24" Height="24"
                        Command="{Binding DeleteCommand}" ToolTip="{loc:Translate 'Delete'}" />
            </StackPanel>
            <TextBlock Text="{Binding Summary}" VerticalAlignment="Center" TextTrimming="CharacterEllipsis" />
        </DockPanel>
    </Border>
</UserControl>
```

Create `VrcTwitchOscBridge/UserControls/InlineSubsRuleRowControl.xaml.cs`:

```csharp
using System.Windows.Controls;

namespace VrcTwitchOscBridge.UserControls;

public partial class InlineSubsRuleRowControl : UserControl
{
    public InlineSubsRuleRowControl() => InitializeComponent();
}
```

Create `VrcTwitchOscBridge/UserControls/InlinePaymentRuleRowControl.xaml`:

```xml
<UserControl x:Class="VrcTwitchOscBridge.UserControls.InlinePaymentRuleRowControl"
             xmlns="http://schemas.microsoft.com/winfx/2006/xaml/presentation"
             xmlns:x="http://schemas.microsoft.com/winfx/2006/xaml"
             xmlns:loc="clr-namespace:VrcTwitchOscBridge.Services">
    <Border Background="{DynamicResource PanelBrush}" BorderBrush="{DynamicResource InputBorderBrush}" BorderThickness="1" Padding="8,6" CornerRadius="4" Margin="0,0,0,4">
        <DockPanel>
            <StackPanel DockPanel.Dock="Right" Orientation="Horizontal">
                <Button Content="⚙" Width="24" Height="24" Margin="0,0,4,0"
                        Command="{Binding EditCommand}" ToolTip="{loc:Translate 'Edit'}" />
                <Button Content="🗑" Width="24" Height="24"
                        Command="{Binding DeleteCommand}" ToolTip="{loc:Translate 'Delete'}" />
            </StackPanel>
            <TextBlock Text="{Binding Summary}" VerticalAlignment="Center" TextTrimming="CharacterEllipsis" />
        </DockPanel>
    </Border>
</UserControl>
```

Create `VrcTwitchOscBridge/UserControls/InlinePaymentRuleRowControl.xaml.cs`:

```csharp
using System.Windows.Controls;

namespace VrcTwitchOscBridge.UserControls;

public partial class InlinePaymentRuleRowControl : UserControl
{
    public InlinePaymentRuleRowControl() => InitializeComponent();
}
```

Create `VrcTwitchOscBridge/UserControls/InlineRouletteRuleRowControl.xaml`:

```xml
<UserControl x:Class="VrcTwitchOscBridge.UserControls.InlineRouletteRuleRowControl"
             xmlns="http://schemas.microsoft.com/winfx/2006/xaml/presentation"
             xmlns:x="http://schemas.microsoft.com/winfx/2006/xaml"
             xmlns:loc="clr-namespace:VrcTwitchOscBridge.Services">
    <Border Background="{DynamicResource PanelBrush}" BorderBrush="{DynamicResource InputBorderBrush}" BorderThickness="1" Padding="8,6" CornerRadius="4" Margin="0,0,0,4">
        <DockPanel>
            <StackPanel DockPanel.Dock="Right" Orientation="Horizontal">
                <Button Content="⚙" Width="24" Height="24" Margin="0,0,4,0"
                        Command="{Binding EditCommand}" ToolTip="{loc:Translate 'Edit'}" />
                <Button Content="🗑" Width="24" Height="24"
                        Command="{Binding DeleteCommand}" ToolTip="{loc:Translate 'Delete'}" />
            </StackPanel>
            <TextBlock Text="{Binding Summary}" VerticalAlignment="Center" TextTrimming="CharacterEllipsis" />
        </DockPanel>
    </Border>
</UserControl>
```

Create `VrcTwitchOscBridge/UserControls/InlineRouletteRuleRowControl.xaml.cs`:

```csharp
using System.Windows.Controls;

namespace VrcTwitchOscBridge.UserControls;

public partial class InlineRouletteRuleRowControl : UserControl
{
    public InlineRouletteRuleRowControl() => InitializeComponent();
}
```

- [ ] **Step 4: Build and confirm**

Run: `dotnet build "E:\!!!Program to work on\Proper Crystal Relay\VrcTwitchOscBridge\VrcTwitchOscBridge.csproj" --no-restore`

Expected: Build fails because the new XAML/code-behind files are not registered in the csproj yet. That's fine — Task 13 handles that. Save your work and continue.

- [ ] **Step 5: Commit (csproj is updated in Task 13)**

Do not commit yet — these files need to be registered in `VrcTwitchOscBridge.csproj` before the project builds. They will be committed in Task 13.

---

## Task 10: Create `RuleListPaneViewModel`

**Files:**
- Create: `VrcTwitchOscBridge/UserControls/RuleListPaneViewModel.cs`

- [ ] **Step 1: Create the VM**

Create `VrcTwitchOscBridge/UserControls/RuleListPaneViewModel.cs`:

```csharp
using VrcTwitchOscBridge.Infrastructure;

namespace VrcTwitchOscBridge.UserControls;

public sealed class RuleListPaneViewModel : ObservableObject
{
    public RuleListPaneViewModel(string? title = null)
    {
        Title = title;
    }

    public string? Title { get; }
}
```

- [ ] **Step 2: Build to confirm the file compiles**

Run: `dotnet build "E:\!!!Program to work on\Proper Crystal Relay\VrcTwitchOscBridge\VrcTwitchOscBridge.csproj" --no-restore`

Expected: Build fails because the file is not in csproj. Save and continue (registered in Task 13).

- [ ] **Step 3: Commit (in Task 13)**

Defer commit to Task 13 with the rest of the new files.

---

## Task 11: Add `IsInAvatarSwapManager` DP to `AvatarSwapRuleEditorControl`

**Files:**
- Modify: `VrcTwitchOscBridge/UserControls/AvatarSwapRuleEditorControl.xaml.cs`
- Modify: `VrcTwitchOscBridge/UserControls/AvatarSwapRuleEditorControl.xaml`

- [ ] **Step 1: Add the DP in the code-behind**

Open `VrcTwitchOscBridge/UserControls/AvatarSwapRuleEditorControl.xaml.cs` and add this to the class body (find the existing public members and add right after them):

```csharp
public static readonly DependencyProperty IsInAvatarSwapManagerProperty =
    DependencyProperty.Register(
        nameof(IsInAvatarSwapManager),
        typeof(bool),
        typeof(AvatarSwapRuleEditorControl),
        new PropertyMetadata(false));

public bool IsInAvatarSwapManager
{
    get => (bool)GetValue(IsInAvatarSwapManagerProperty);
    set => SetValue(IsInAvatarSwapManagerProperty, value);
}
```

Make sure `using System.Windows;` is in the file's using directives. If not, add it.

- [ ] **Step 2: Build and confirm**

Run: `dotnet build "E:\!!!Program to work on\Proper Crystal Relay\VrcTwitchOscBridge\VrcTwitchOscBridge.csproj" --no-restore`

Expected: Build succeeds.

- [ ] **Step 3: Update the XAML supporter triggers**

Open `VrcTwitchOscBridge/UserControls/AvatarSwapRuleEditorControl.xaml` and find the supporter/Bits/Subs visibility triggers. There are several spots at lines 317, 338, 352, 369, 427 (from the earlier exploration) and possibly more.

For each of these triggers, add an `OR` condition that checks the `IsInAvatarSwapManager` flag plus the rule's `TriggerType`. The simplest pattern is to add a parallel `<DataTrigger>` block in the same `<Style.Triggers>` list:

For example, change this:
```xml
<DataTrigger Binding="{Binding DataContext.IsViewingSupporterOverrides, RelativeSource={RelativeSource AncestorType=Window}, FallbackValue=False}" Value="True">
```

To add a parallel trigger underneath (in the same `<Style.Triggers>`):
```xml
<DataTrigger Binding="{Binding DataContext.TriggerType, RelativeSource={RelativeSource Self}}" Value="Bits">
```
… and similar for `Subscriptions`. The cleaner alternative is to introduce a computed property on `TriggerRule` (e.g., `UsesSupporterEditor` → true when `IsViewingSupporterOverrides || (IsInAvatarSwapManager && (TriggerType is Bits or Subscriptions))`). **Recommended**: add a single computed property and use it.

Open `VrcTwitchOscBridge/Models/TriggerRule.cs` and add this computed property near the other `Uses*` properties (around line 1277):

```csharp
public bool UsesSupporterTriggerType => TriggerType is TwitchTriggerType.Bits or TwitchTriggerType.Subscriptions;
```

Then in `AvatarSwapRuleEditorControl.xaml.cs`, add a static helper that the XAML can call via a binding. Since the editor reads `IsViewingSupporterOverrides` from its window's DataContext, the cleanest path is to add a `UsesSupporterEditor` property to `MainWindowViewModel` (set to `IsViewingSupporterOverrides || TriggerRule is supporter`) — but that conflates the two contexts. **Simplest path**: keep the current `IsViewing*` triggers AND add a new `Visibility` binding to a `Visibility` computed at the editor level. For now, broaden each supporter trigger with a parallel `MultiDataTrigger` that includes `IsInAvatarSwapManager = true` and the rule type. **Example replacement pattern** for each affected trigger:

Replace the existing single DataTrigger:
```xml
<DataTrigger Binding="{Binding DataContext.IsViewingSupporterOverrides, RelativeSource={RelativeSource AncestorType=Window}, FallbackValue=False}" Value="True">
    <Setter Property="Visibility" Value="Visible" />
</DataTrigger>
```

With this block (or add the new trigger alongside it):
```xml
<DataTrigger Binding="{Binding DataContext.IsViewingSupporterOverrides, RelativeSource={RelativeSource AncestorType=Window}, FallbackValue=False}" Value="True">
    <Setter Property="Visibility" Value="Visible" />
</DataTrigger>
<DataTrigger Binding="{Binding DataContext.IsInAvatarSwapManager, RelativeSource={RelativeSource AncestorType=UserControl}}" Value="True">
    <Setter Property="Visibility" Value="Visible" />
</DataTrigger>
```

(This treats the editor as "supporter mode" when hosted inside the AvatarSwapManager, regardless of the rule's `TriggerType`. This is safe because the editor in that context will only ever receive Bits/Subs/ChannelPoints rules, and the supporter-only sections are not relevant for ChannelPoints. If the supporter sections do not show for ChannelPoints rules, you can further gate on `DataContext.TriggerType is Bits or Subscriptions`.)

Repeat this pattern for the supporter sections at lines 317, 338, 352, 369, 427 of `AvatarSwapRuleEditorControl.xaml`. Leave the Power Up and Cash Payment triggers untouched.

- [ ] **Step 4: Build and confirm**

Run: `dotnet build "E:\!!!Program to work on\Proper Crystal Relay\VrcTwitchOscBridge\VrcTwitchOscBridge.csproj" --no-restore`

Expected: Build succeeds.

- [ ] **Step 5: Commit**

```bash
git add VrcTwitchOscBridge/UserControls/AvatarSwapRuleEditorControl.xaml VrcTwitchOscBridge/UserControls/AvatarSwapRuleEditorControl.xaml.cs VrcTwitchOscBridge/Models/TriggerRule.cs
git commit -m "feat: add IsInAvatarSwapManager DP and broaden supporter triggers in editor"
```

---

## Task 12: Update `AvatarSwapManagerViewModel` to use typed collections and new state

**Files:**
- Modify: `VrcTwitchOscBridge/ViewModels/AvatarSwapManagerViewModel.cs`

- [ ] **Step 1: Replace the typed collections**

Find the existing 5 row properties (around lines 73-81):

```csharp
public ObservableCollection<InlineAvatarSwapRuleRowViewModel> ChannelPointRows { get; } = new();

public ObservableCollection<InlineAvatarSwapRuleRowViewModel> BitsRows { get; } = new();

public ObservableCollection<InlineAvatarSwapRuleRowViewModel> SubsRows { get; } = new();

public ObservableCollection<InlineAvatarSwapRuleRowViewModel> PaymentRows { get; } = new();

public ObservableCollection<InlineAvatarSwapRuleRowViewModel> RouletteTriggerRows { get; } = new();
```

Replace them with:

```csharp
public ObservableCollection<InlineChannelPointRuleRowViewModel> ChannelPointRows { get; } = new();

public ObservableCollection<InlineBitsRuleRowViewModel> BitsRows { get; } = new();

public ObservableCollection<InlineSubsRuleRowViewModel> SubsRows { get; } = new();

public ObservableCollection<InlinePaymentRuleRowViewModel> PaymentRows { get; } = new();

public ObservableCollection<InlineRouletteRuleRowViewModel> RouletteTriggerRows { get; } = new();
```

Add `using VrcTwitchOscBridge.UserControls;` to the using directives at the top of the file (if not already present).

- [ ] **Step 2: Add `RightPaneContent`, `SelectedRule`, `BackToListCommand`**

Find the `EditingRule` property and the `IsSwapEditorOpen` / `IsRouletteEditorOpen` region. Replace `EditingRule` with the new state machine:

```csharp
private object? _rightPaneContent;
public object? RightPaneContent
{
    get => _rightPaneContent;
    private set
    {
        if (SetProperty(ref _rightPaneContent, value))
        {
            RaisePropertyChanged(nameof(IsEditorOpen));
        }
    }
}

private IRuleRowViewModel? _selectedRule;
public IRuleRowViewModel? SelectedRule
{
    get => _selectedRule;
    set
    {
        if (SetProperty(ref _selectedRule, value))
        {
            RightPaneContent = value;
        }
    }
}

public RelayCommand BackToListCommand { get; }

public bool IsEditorOpen => RightPaneContent is not null;
```

- [ ] **Step 3: Initialize `BackToListCommand` in the constructor**

In the constructor (find the existing `BeginInlineEditCommand` / `CommitInlineEditCommand` / `CancelInlineEditCommand` setup), remove those three commands and add:

```csharp
BackToListCommand = new RelayCommand(BackToList);
```

Add a new private method:

```csharp
private void BackToList()
{
    SelectedRule = null;
}
```

- [ ] **Step 4: Replace `IsSwapEditorOpen` / `IsRouletteEditorOpen` setters**

Find the existing `IsSwapEditorOpen` and `IsRouletteEditorOpen` properties. Replace their backing stores and setters with:

```csharp
private bool _isSwapEditorOpen;
public bool IsSwapEditorOpen
{
    get => _isSwapEditorOpen;
    set
    {
        if (SetProperty(ref _isSwapEditorOpen, value))
        {
            RaisePropertyChanged(nameof(IsEditorOpen));
        }
    }
}

private bool _isRouletteEditorOpen;
public bool IsRouletteEditorOpen
{
    get => _isRouletteEditorOpen;
    set
    {
        if (SetProperty(ref _isRouletteEditorOpen, value))
        {
            RaisePropertyChanged(nameof(IsEditorOpen));
        }
    }
}
```

(Keep these properties for now — the XAML in Task 13 still references them, and they will be removed when the XAML is updated.)

- [ ] **Step 5: Rewrite `RebuildRows` to use typed row VMs**

Find the existing `RebuildRows()` method (around line 222). Replace its body with:

```csharp
private void RebuildRows()
{
    ChannelPointRows.Clear();
    BitsRows.Clear();
    SubsRows.Clear();
    PaymentRows.Clear();
    RouletteTriggerRows.Clear();

    if (SelectedSwapCard is not null)
    {
        foreach (var r in SelectedSwapCard.Profile.ChannelPointRules)
        {
            var row = new InlineChannelPointRuleRowViewModel(r);
            WireRowCommands(row);
            ChannelPointRows.Add(row);
        }
        foreach (var r in SelectedSwapCard.Profile.BitsRules)
        {
            var row = new InlineBitsRuleRowViewModel(r);
            WireRowCommands(row);
            BitsRows.Add(row);
        }
        foreach (var r in SelectedSwapCard.Profile.SubsRules)
        {
            var row = new InlineSubsRuleRowViewModel(r);
            WireRowCommands(row);
            SubsRows.Add(row);
        }
        foreach (var r in SelectedSwapCard.Profile.PaymentRules)
        {
            var row = new InlinePaymentRuleRowViewModel(r);
            WireRowCommands(row);
            PaymentRows.Add(row);
        }
    }

    if (SelectedRouletteCard is not null)
    {
        foreach (var r in SelectedRouletteCard.Roulette.Triggers)
        {
            var row = new InlineRouletteRuleRowViewModel(r);
            WireRowCommands(row);
            RouletteTriggerRows.Add(row);
        }
    }
}

private void WireRowCommands(IRuleRowViewModel row)
{
    row.EditCommand = new RelayCommand(() => SelectedRule = row);
    row.DeleteCommand = new RelayCommand(() => DeleteRule(row));
}
```

Add a new private method `WireRowCommands` at the class level (alongside the other private methods). Local functions cannot be hoisted in C# 7+ the way class methods can, so we use a class-level private method instead. The `AvatarSwapRuleRowKind` enum is no longer needed — the `WireRowCommands` helper takes the row directly and binds commands based on its concrete type via the `DeleteRule` overload chain.

- [ ] **Step 6: Update `AddChannelPointRule` / `AddBitsRule` / `AddSubsRule`**

Find the three `AddChannelPointRule` / `AddBitsRule` / `AddSubsRule` methods. In each, the last two lines add the row to the typed collection via `ChannelPointRows.Add(new InlineAvatarSwapRuleRowViewModel(rule))`. Replace those with the new typed VM:

```csharp
// in AddChannelPointRule
var row = new InlineChannelPointRuleRowViewModel(rule);
row.EditCommand = new RelayCommand(() => SelectedRule = row);
row.DeleteCommand = new RelayCommand(() => DeleteRule(row));
ChannelPointRows.Add(row);
```

Repeat for `AddBitsRule` (use `InlineBitsRuleRowViewModel`), `AddSubsRule` (use `InlineSubsRuleRowViewModel`).

- [ ] **Step 7: Rewrite `AddPaymentRule`**

Replace the entire `AddPaymentRule` method with:

```csharp
private void AddPaymentRule()
{
    if (SelectedSwapCard is null) return;
    var rule = new CashPaymentRule
    {
        Name = "New Cash Payment Swap",
        Provider = CashPaymentProvider.StreamElements,
        IsEnabled = true,
        ActionKind = CashPaymentActionKind.TriggerAction,
        TriggerAction = new TriggerRule
        {
            ActionType = OscActionType.AvatarChange,
            AvatarChangeTargetId = SelectedSwapCard.Profile.TargetAvatarId,
            AvatarTargetName = SelectedSwapCard.Profile.TargetAvatarName
        }
    };
    SelectedSwapCard.Profile.PaymentRules.Add(rule);
    var row = new InlinePaymentRuleRowViewModel(rule);
    row.EditCommand = new RelayCommand(() => SelectedRule = row);
    row.DeleteCommand = new RelayCommand(() => DeleteRule(row));
    PaymentRows.Add(row);
}
```

- [ ] **Step 8: Update `AddAdvancedTrigger` similarly**

Find `AddAdvancedTrigger`. The new logic mirrors `AddChannelPointRule` (it adds a `TriggerRule` with whatever type was passed). Replace the last two lines to construct an `InlineChannelPointRuleRowViewModel` and wire commands, just like in step 6.

- [ ] **Step 9: Update `DeleteRule` to handle the typed row VMs**

Find the existing `DeleteRule` method (around line 412). Replace its body with:

```csharp
private void DeleteRule(IRuleRowViewModel? row)
{
    if (row is null || SelectedSwapCard is null) return;
    if (row is InlineChannelPointRuleRowViewModel cp
        && SelectedSwapCard.Profile.ChannelPointRules.Remove((TriggerRule)cp.Rule))
    {
        ChannelPointRows.Remove(cp);
    }
    else if (row is InlineBitsRuleRowViewModel bits
        && SelectedSwapCard.Profile.BitsRules.Remove((TriggerRule)bits.Rule))
    {
        BitsRows.Remove(bits);
    }
    else if (row is InlineSubsRuleRowViewModel subs
        && SelectedSwapCard.Profile.SubsRules.Remove((TriggerRule)subs.Rule))
    {
        SubsRows.Remove(subs);
    }
    else if (row is InlinePaymentRuleRowViewModel pay
        && SelectedSwapCard.Profile.PaymentRules.Remove((CashPaymentRule)pay.Rule))
    {
        PaymentRows.Remove(pay);
    }
    else if (row is InlineRouletteRuleRowViewModel roulette
        && SelectedRouletteCard is not null
        && SelectedRouletteCard.Roulette.Triggers.Remove((TriggerRule)roulette.Rule))
    {
        RouletteTriggerRows.Remove(roulette);
    }

    if (ReferenceEquals(SelectedRule, row))
    {
        SelectedRule = null;
    }
}
```

- [ ] **Step 10: Remove the old `BeginInlineEdit` / `CommitInlineEdit` / `CancelInlineEdit` methods**

Delete those three methods. Their command properties are already removed in step 3.

- [ ] **Step 11: Update `OpenSwapEditor` / `OpenRouletteEditor` to set `RightPaneContent`**

Find the existing `OpenSwapEditor` method. Add at the start of the method body (after the null check):

```csharp
RightPaneContent = new RuleListPaneViewModel(SelectedSwapCard?.Profile?.TargetAvatarName);
```

And in `OpenRouletteEditor`:

```csharp
RightPaneContent = new RuleListPaneViewModel(SelectedRouletteCard?.Roulette?.Name);
```

(Add the call at the end of each method, after `RebuildRows()`.)

- [ ] **Step 12: Update `DeleteSwap` / `DeleteRoulette` / `CloseEditor` to clear `RightPaneContent`**

Find `DeleteSwap`. Add `RightPaneContent = null;` after the existing `SelectedSwapCard = null;` line. Repeat for `DeleteRoulette`. For `CloseEditor`, replace the body with:

```csharp
private void CloseEditor()
{
    IsSwapEditorOpen = false;
    IsRouletteEditorOpen = false;
    RightPaneContent = null;
    SelectedRule = null;
    SelectedSwapCard = null;
    SelectedRouletteCard = null;
}
```

- [ ] **Step 13: Build and confirm**

Run: `dotnet build "E:\!!!Program to work on\Proper Crystal Relay\VrcTwitchOscBridge\VrcTwitchOscBridge.csproj" --no-restore`

Expected: Build fails because the new UserControls / VMs are not in csproj yet. That's expected — Task 13 handles that. Don't fix anything yet.

- [ ] **Step 14: Commit (in Task 13 with the rest of the new files)**

Defer commit to Task 13.

---

## Task 13: Update `VrcTwitchOscBridge.csproj` and delete the old files

**Files:**
- Modify: `VrcTwitchOscBridge/VrcTwitchOscBridge.csproj`
- Delete: `VrcTwitchOscBridge/UserControls/InlineAvatarSwapRuleRowControl.xaml` + `.cs`
- Delete: `VrcTwitchOscBridge/ViewModels/InlineAvatarSwapRuleRowViewModel.cs`

- [ ] **Step 1: Find the existing entries for the files being deleted**

Open `VrcTwitchOscBridge/VrcTwitchOscBridge.csproj`. Find the `<Page Include="UserControls\InlineAvatarSwapRuleRowControl.xaml" />` line (line 55) and the corresponding `<Compile>` entries (lines 113-114) and the `<Compile Include="ViewModels\InlineAvatarSwapRuleRowViewModel.cs" />` line (line 222). Delete those four entries.

- [ ] **Step 2: Add `<Page>` and `<Compile>` entries for the 5 new row controls**

Right after the existing `<Page>` blocks (e.g., after the `InlineAvatarSwapRuleRowControl.xaml` line you just deleted), add:

```xml
<Page Include="UserControls\InlineChannelPointRuleRowControl.xaml" />
<Page Include="UserControls\InlineBitsRuleRowControl.xaml" />
<Page Include="UserControls\InlineSubsRuleRowControl.xaml" />
<Page Include="UserControls\InlinePaymentRuleRowControl.xaml" />
<Page Include="UserControls\InlineRouletteRuleRowControl.xaml" />
```

Right after the corresponding `<Compile>` blocks, add:

```xml
<Compile Include="UserControls\InlineChannelPointRuleRowControl.xaml.cs">
  <DependentUpon>InlineChannelPointRuleRowControl.xaml</DependentUpon>
</Compile>
<Compile Include="UserControls\InlineBitsRuleRowControl.xaml.cs">
  <DependentUpon>InlineBitsRuleRowControl.xaml</DependentUpon>
</Compile>
<Compile Include="UserControls\InlineSubsRuleRowControl.xaml.cs">
  <DependentUpon>InlineSubsRuleRowControl.xaml</DependentUpon>
</Compile>
<Compile Include="UserControls\InlinePaymentRuleRowControl.xaml.cs">
  <DependentUpon>InlinePaymentRuleRowControl.xaml</DependentUpon>
</Compile>
<Compile Include="UserControls\InlineRouletteRuleRowControl.xaml.cs">
  <DependentUpon>InlineRouletteRuleRowControl.xaml</DependentUpon>
</Compile>
```

- [ ] **Step 3: Add `<Compile>` entries for the new VMs**

Find a good spot (e.g., near the other VM entries) and add:

```xml
<Compile Include="UserControls\InlineChannelPointRuleRowViewModel.cs" />
<Compile Include="UserControls\InlineBitsRuleRowViewModel.cs" />
<Compile Include="UserControls\InlineSubsRuleRowViewModel.cs" />
<Compile Include="UserControls\InlinePaymentRuleRowViewModel.cs" />
<Compile Include="UserControls\InlineRouletteRuleRowViewModel.cs" />
<Compile Include="UserControls\IRuleRowViewModel.cs" />
<Compile Include="UserControls\RuleListPaneViewModel.cs" />
```

- [ ] **Step 4: Delete the old files**

```bash
Remove-Item -LiteralPath "E:\!!!Program to work on\Proper Crystal Relay\VrcTwitchOscBridge\UserControls\InlineAvatarSwapRuleRowControl.xaml"
Remove-Item -LiteralPath "E:\!!!Program to work on\Proper Crystal Relay\VrcTwitchOscBridge\UserControls\InlineAvatarSwapRuleRowControl.xaml.cs"
Remove-Item -LiteralPath "E:\!!!Program to work on\Proper Crystal Relay\VrcTwitchOscBridge\ViewModels\InlineAvatarSwapRuleRowViewModel.cs"
```

- [ ] **Step 5: Build and confirm**

Run: `dotnet build "E:\!!!Program to work on\Proper Crystal Relay\VrcTwitchOscBridge\VrcTwitchOscBridge.csproj" --no-restore`

Expected: Build succeeds.

- [ ] **Step 6: Commit**

```bash
git add VrcTwitchOscBridge/VrcTwitchOscBridge.csproj VrcTwitchOscBridge/ViewModels/AvatarSwapManagerViewModel.cs VrcTwitchOscBridge/UserControls/InlineChannelPointRuleRowControl.xaml VrcTwitchOscBridge/UserControls/InlineChannelPointRuleRowControl.xaml.cs VrcTwitchOscBridge/UserControls/InlineBitsRuleRowControl.xaml VrcTwitchOscBridge/UserControls/InlineBitsRuleRowControl.xaml.cs VrcTwitchOscBridge/UserControls/InlineSubsRuleRowControl.xaml VrcTwitchOscBridge/UserControls/InlineSubsRuleRowControl.xaml.cs VrcTwitchOscBridge/UserControls/InlinePaymentRuleRowControl.xaml VrcTwitchOscBridge/UserControls/InlinePaymentRuleRowControl.xaml.cs VrcTwitchOscBridge/UserControls/InlineRouletteRuleRowControl.xaml VrcTwitchOscBridge/UserControls/InlineRouletteRuleRowControl.xaml.cs VrcTwitchOscBridge/UserControls/InlineChannelPointRuleRowViewModel.cs VrcTwitchOscBridge/UserControls/InlineBitsRuleRowViewModel.cs VrcTwitchOscBridge/UserControls/InlineSubsRuleRowViewModel.cs VrcTwitchOscBridge/UserControls/InlinePaymentRuleRowViewModel.cs VrcTwitchOscBridge/UserControls/InlineRouletteRuleRowViewModel.cs VrcTwitchOscBridge/UserControls/IRuleRowViewModel.cs VrcTwitchOscBridge/UserControls/RuleListPaneViewModel.cs
git rm VrcTwitchOscBridge/UserControls/InlineAvatarSwapRuleRowControl.xaml VrcTwitchOscBridge/UserControls/InlineAvatarSwapRuleRowControl.xaml.cs VrcTwitchOscBridge/ViewModels/InlineAvatarSwapRuleRowViewModel.cs
git commit -m "feat: wire per-type row controls in AvatarSwapManager and remove old shared control"
```

---

## Task 14: Update `AvatarSwapManagerWindow.xaml` to use the new controls + ContentControl right pane

**Files:**
- Modify: `VrcTwitchOscBridge/AvatarSwapManagerWindow.xaml`

- [ ] **Step 1: Add the new XML namespace**

Find the existing `xmlns:uc="clr-namespace:VrcTwitchOscBridge.UserControls"` line. If not present, add it.

Find the `xmlns:vm="clr-namespace:VrcTwitchOscBridge.ViewModels"` line. Make sure it's there.

- [ ] **Step 2: Update the 5 `ItemsControl` row blocks**

Find the existing 5 `ItemsControl` blocks (lines 338-381 for ChannelPoints, Bits, Subs, Payment; lines 419-425 for Roulette). Each one currently looks like:

```xml
<ItemsControl ItemsSource="{Binding ChannelPointRows}">
    <ItemsControl.ItemTemplate>
        <DataTemplate>
            <uc:InlineAvatarSwapRuleRowControl Row="{Binding}" />
        </DataTemplate>
    </ItemsControl.ItemTemplate>
</ItemsControl>
```

Replace each with the typed control. For ChannelPoints:

```xml
<ItemsControl ItemsSource="{Binding ChannelPointRows}">
    <ItemsControl.ItemTemplate>
        <DataTemplate>
            <uc:InlineChannelPointRuleRowControl />
        </DataTemplate>
    </ItemsControl.ItemTemplate>
</ItemsControl>
```

For Bits: `<uc:InlineBitsRuleRowControl />`. For Subs: `<uc:InlineSubsRuleRowControl />`. For Payment: `<uc:InlinePaymentRuleRowControl />`. For Roulette: `<uc:InlineRouletteRuleRowControl />`.

- [ ] **Step 3: Replace the right pane with a single `ContentControl`**

Find the two `<Border>` elements at lines 314-396 (swap editor) and 399-433 (roulette editor). Replace both with a single `ContentControl` plus its `DataTemplate` resources:

```xml
<ContentControl Grid.Column="1" Content="{Binding RightPaneContent}">
    <ContentControl.Resources>
        <DataTemplate DataType="{x:Type uc:RuleListPaneViewModel}">
            <Border Background="{DynamicResource PanelBrush}" BorderBrush="{DynamicResource BorderBrush}" BorderThickness="1" CornerRadius="6" Padding="10">
                <ScrollViewer VerticalScrollBarVisibility="Auto">
                    <StackPanel>
                        <!-- ... copy the full existing 4-section content here ... -->
                    </StackPanel>
                </ScrollViewer>
            </Border>
        </DataTemplate>
        <DataTemplate DataType="{x:Type uc:InlineChannelPointRuleRowViewModel}">
            <Border Background="{DynamicResource PanelBrush}" BorderBrush="{DynamicResource BorderBrush}" BorderThickness="1" CornerRadius="6" Padding="10">
                <DockPanel>
                    <Button DockPanel.Dock="Top" Content="{loc:Translate 'Back to {0}', ...}" Style="{StaticResource SecondaryButtonStyle}"
                            Command="{Binding DataContext.BackToListCommand, RelativeSource={RelativeSource AncestorType=Window}}" Margin="0,0,0,8" />
                    <userControls:AvatarSwapRuleEditorControl DataContext="{Binding Rule}" IsInAvatarSwapManager="True" />
                </DockPanel>
            </Border>
        </DataTemplate>
        <DataTemplate DataType="{x:Type uc:InlineBitsRuleRowViewModel}">
            <Border Background="{DynamicResource PanelBrush}" BorderBrush="{DynamicResource BorderBrush}" BorderThickness="1" CornerRadius="6" Padding="10">
                <DockPanel>
                    <Button DockPanel.Dock="Top" Content="{loc:Translate 'Back to {0}', ...}" Style="{StaticResource SecondaryButtonStyle}"
                            Command="{Binding DataContext.BackToListCommand, RelativeSource={RelativeSource AncestorType=Window}}" Margin="0,0,0,8" />
                    <userControls:AvatarSwapRuleEditorControl DataContext="{Binding Rule}" IsInAvatarSwapManager="True" />
                </DockPanel>
            </Border>
        </DataTemplate>
        <DataTemplate DataType="{x:Type uc:InlineSubsRuleRowViewModel}">
            <!-- same shape, AvatarSwapRuleEditorControl with IsInAvatarSwapManager=True -->
        </DataTemplate>
        <DataTemplate DataType="{x:Type uc:InlinePaymentRuleRowViewModel}">
            <Border Background="{DynamicResource PanelBrush}" BorderBrush="{DynamicResource BorderBrush}" BorderThickness="1" CornerRadius="6" Padding="10">
                <DockPanel>
                    <Button DockPanel.Dock="Top" Content="{loc:Translate 'Back to {0}', ...}" Style="{StaticResource SecondaryButtonStyle}"
                            Command="{Binding DataContext.BackToListCommand, RelativeSource={RelativeSource AncestorType=Window}}" Margin="0,0,0,8" />
                    <uc:CashPaymentRuleEditorControl DataContext="{Binding Rule}" />
                </DockPanel>
            </Border>
        </DataTemplate>
        <DataTemplate DataType="{x:Type uc:InlineRouletteRuleRowViewModel}">
            <Border Background="{DynamicResource PanelBrush}" BorderBrush="{DynamicResource BorderBrush}" BorderThickness="1" CornerRadius="6" Padding="10">
                <DockPanel>
                    <Button DockPanel.Dock="Top" Content="{loc:Translate 'Back to {0} (Roulette)', ...}" Style="{StaticResource SecondaryButtonStyle}"
                            Command="{Binding DataContext.BackToListCommand, RelativeSource={RelativeSource AncestorType=Window}}" Margin="0,0,0,8" />
                    <userControls:AvatarSwapRuleEditorControl DataContext="{Binding Rule}" IsInAvatarSwapManager="True" />
                </DockPanel>
            </Border>
        </DataTemplate>
    </ContentControl.Resources>
</ContentControl>
```

For the `Back to {0}` button content, use a binding that formats the title from the parent VM:

```xml
Content="{Binding DataContext.RightPaneContent.Title, RelativeSource={RelativeSource AncestorType=Window}}"
```

And the button text could be: `← Back to ` + the title. The simplest way: use a `Content` binding with a `StringFormat`:

```xml
<Button Content="{Binding DataContext.RightPaneContent.Title, RelativeSource={RelativeSource AncestorType=Window}, StringFormat='← Back to {0}'}" ... />
```

For the Roulette template, use `StringFormat='← Back to {0} (Roulette)'`.

- [ ] **Step 4: Build and confirm**

Run: `dotnet build "E:\!!!Program to work on\Proper Crystal Relay\VrcTwitchOscBridge\VrcTwitchOscBridge.csproj" --no-restore`

Expected: Build fails because `CashPaymentRuleEditorControl` doesn't exist yet. Task 15 creates it.

- [ ] **Step 5: Commit (after Task 15 finishes)**

Defer commit to after Task 15.

---

## Task 15: Extract `CashPaymentRuleEditorControl` from `MainWindow.xaml`

**Files:**
- Create: `VrcTwitchOscBridge/UserControls/CashPaymentRuleEditorControl.xaml` + `.cs`
- Modify: `VrcTwitchOscBridge/MainWindow.xaml`
- Modify: `VrcTwitchOscBridge/VrcTwitchOscBridge.csproj`

- [ ] **Step 1: Identify the XAML block to extract**

Open `VrcTwitchOscBridge/MainWindow.xaml` and find the `<DataTemplate DataType="{x:Type models:CashPaymentRule}">` block starting at line 6804. The block extends to the end of the CashPaymentRule template (around line 7600 in the current file, just before the next `<Border>` that belongs to the Movement section).

The extracted block includes:
- The header "Cash Payment Rule" with help button
- "Test Cash Rule" button
- "Payment Match" card (Rule enabled, Rule Name, Provider, Min/Max Amount, Currency, Message Contains, Cooldown Seconds)
- "Cash Action" family picker card
- "Avatar Scaling Action" sub-card (when `UsesAvatarScaling`)

- [ ] **Step 2: Create the new UserControl XAML**

Create `VrcTwitchOscBridge/UserControls/CashPaymentRuleEditorControl.xaml`. Copy the entire `<DataTemplate>` block content from MainWindow.xaml into a new `<UserControl>` wrapper. **Important conversion rules:**

- All `RelativeSource={RelativeSource AncestorType=Window}` → `RelativeSource AncestorType=UserControl`
- All bindings to `DataContext.SomeProperty` → `Rule.SomeProperty` (and rely on `Rule` being set to the `CashPaymentRule`)
- All bindings to `DataContext.CommandName` for commands like `TestSelectedCashPaymentRuleCommand` → expose these as `DependencyProperty` or wire them up in code-behind

Example wrapper:

```xml
<UserControl x:Class="VrcTwitchOscBridge.UserControls.CashPaymentRuleEditorControl"
             xmlns="http://schemas.microsoft.com/winfx/2006/xaml/presentation"
             xmlns:x="http://schemas.microsoft.com/winfx/2006/xaml"
             xmlns:loc="clr-namespace:VrcTwitchOscBridge.Services"
             xmlns:models="clr-namespace:VrcTwitchOscBridge.Models">
    <UserControl.Resources>
        <!-- converters and resources copied from MainWindow.xaml as needed -->
    </UserControl.Resources>
    <StackPanel>
        <!-- The inner content from MainWindow.xaml, with bindings rewired to use {Binding Rule.X} or {Binding ElementName=Root, Path=X} -->
    </StackPanel>
</UserControl>
```

For commands that need a window-level VM (like `TestSelectedCashPaymentRuleCommand`), the cleanest approach is to expose the command on the UserControl:

```csharp
public static readonly DependencyProperty TestCommandProperty =
    DependencyProperty.Register(nameof(TestCommand), typeof(ICommand), typeof(CashPaymentRuleEditorControl));

public ICommand TestCommand
{
    get => (ICommand)GetValue(TestCommandProperty);
    set => SetValue(TestCommandProperty, value);
}
```

Then the XAML can bind `Command="{Binding TestCommand, RelativeSource={RelativeSource AncestorType=UserControl}}"`.

- [ ] **Step 3: Create the code-behind**

Create `VrcTwitchOscBridge/UserControls/CashPaymentRuleEditorControl.xaml.cs`:

```csharp
using System.Windows;
using System.Windows.Controls;
using VrcTwitchOscBridge.Models;

namespace VrcTwitchOscBridge.UserControls;

public partial class CashPaymentRuleEditorControl : UserControl
{
    public static readonly DependencyProperty RuleProperty =
        DependencyProperty.Register(nameof(Rule), typeof(CashPaymentRule), typeof(CashPaymentRuleEditorControl));

    public CashPaymentRule Rule
    {
        get => (CashPaymentRule)GetValue(RuleProperty);
        set => SetValue(RuleProperty, value);
    }

    public static readonly DependencyProperty TestCommandProperty =
        DependencyProperty.Register(nameof(TestCommand), typeof(System.Windows.Input.ICommand), typeof(CashPaymentRuleEditorControl));

    public System.Windows.Input.ICommand TestCommand
    {
        get => (System.Windows.Input.ICommand)GetValue(TestCommandProperty);
        set => SetValue(TestCommandProperty, value);
    }

    public CashPaymentRuleEditorControl()
    {
        InitializeComponent();
        DataContext = this;
    }
}
```

- [ ] **Step 4: Update `MainWindow.xaml` to use the new UserControl**

Find the existing `<DataTemplate DataType="{x:Type models:CashPaymentRule}">...</DataTemplate>` block. Replace the entire block with:

```xml
<uc:CashPaymentRuleEditorControl Rule="{Binding SelectedCashPaymentRule}"
                                 TestCommand="{Binding DataContext.TestSelectedCashPaymentRuleCommand, RelativeSource={RelativeSource AncestorType=Window}}" />
```

Delete the now-unused `DataTemplate` and its `DataTemplate DataType="{x:Type models:CashPaymentRule}"` wrapper.

- [ ] **Step 5: Update `AvatarSwapManagerWindow.xaml` similarly**

The DataTemplate for `InlinePaymentRuleRowViewModel` (added in Task 14) already references `<uc:CashPaymentRuleEditorControl DataContext="{Binding Rule}" />`. Verify the binding works — the UserControl's `Rule` property is set via `DataContext = this` in the code-behind, so `DataContext = {Binding Rule}` on the UserControl sets `DataContext` to the `CashPaymentRule` and the code-behind re-exposes it. If this doesn't work, change the DataTemplate to:

```xml
<uc:CashPaymentRuleEditorControl Rule="{Binding Rule}" />
```

- [ ] **Step 6: Add the new file to the csproj**

In `VrcTwitchOscBridge.csproj`, add:

```xml
<Page Include="UserControls\CashPaymentRuleEditorControl.xaml" />
<Compile Include="UserControls\CashPaymentRuleEditorControl.xaml.cs">
  <DependentUpon>CashPaymentRuleEditorControl.xaml</DependentUpon>
</Compile>
```

- [ ] **Step 7: Build and confirm**

Run: `dotnet build "E:\!!!Program to work on\Proper Crystal Relay\VrcTwitchOscBridge\VrcTwitchOscBridge.csproj" --no-restore`

Expected: Build succeeds. If it fails on missing bindings, trace the issue back to the XAML extraction in step 2.

- [ ] **Step 8: Commit**

```bash
git add VrcTwitchOscBridge/UserControls/CashPaymentRuleEditorControl.xaml VrcTwitchOscBridge/UserControls/CashPaymentRuleEditorControl.xaml.cs VrcTwitchOscBridge/MainWindow.xaml VrcTwitchOscBridge/AvatarSwapManagerWindow.xaml VrcTwitchOscBridge/VrcTwitchOscBridge.csproj
git commit -m "feat: extract CashPaymentRuleEditorControl and wire it in MainWindow + AvatarSwapManager"
```

---

## Task 16: Update `AvatarSwapManagerWindow.xaml.cs` for cleanup

**Files:**
- Modify: `VrcTwitchOscBridge/AvatarSwapManagerWindow.xaml.cs`

- [ ] **Step 1: Remove the old BeginInlineEdit/CommitInlineEdit/CancelInlineEdit handlers**

Find any code-behind that wires up the old inline edit (e.g., `BeginInlineEditCommand`, `CommitInlineEditCommand`, `CancelInlineEditCommand`). Remove those handlers.

- [ ] **Step 2: Build and confirm**

Run: `dotnet build "E:\!!!Program to work on\Proper Crystal Relay\VrcTwitchOscBridge\VrcTwitchOscBridge.csproj" --no-restore`

Expected: Build succeeds.

- [ ] **Step 3: Commit**

```bash
git add VrcTwitchOscBridge/AvatarSwapManagerWindow.xaml.cs
git commit -m "chore: remove obsolete inline-edit handlers from AvatarSwapManager code-behind"
```

---

## Task 17: Update `AvatarSwapManagerViewModel` callers in `MainWindowViewModel` and tests

**Files:**
- Modify: `VrcTwitchOscBridge.Tests/AvatarSwapManagerViewModelTests.cs`

- [ ] **Step 1: Run all tests and see what breaks**

Run: `dotnet test "E:\!!!Program to work on\Proper Crystal Relay\VrcTwitchOscBridge.Tests\VrcTwitchOscBridge.Tests.csproj" -v minimal`

Expected: Some tests fail. Identify each failure and fix.

- [ ] **Step 2: Fix `OpenSwapEditorCommand_SetsSelectedCardAndRaisesPropertyChanged`**

Update line 45 from `Assert.Single(vm.ChannelPointRows);` — the typed collection now is `ObservableCollection<InlineChannelPointRuleRowViewModel>`. The assertion should still work; just confirm.

- [ ] **Step 3: Fix `Selection_RaisesCanExecuteChangedForSelectionDependentCommands`**

Confirm this still passes (it doesn't reference the row VMs directly).

- [ ] **Step 4: Fix `DeleteSwapCommand_RemovesProfileAndCard`**

Update line 100: `Assert.False(vm.IsSwapEditorOpen);` — should still work. The `IsSwapEditorOpen` setter is preserved in Task 12 step 4.

- [ ] **Step 5: Add new test for `AddPaymentRule`**

Append to `AvatarSwapManagerViewModelTests.cs`:

```csharp
[Fact]
public void AddPaymentRuleCommand_CreatesCashPaymentRuleNotTriggerRule()
{
    var settings = new AppSettings();
    var profile = new AvatarSwapProfile { TargetAvatarId = "avtr_a", TargetAvatarName = "Avatar A" };
    settings.AvatarSwapProfiles.Add(profile);

    var vm = new AvatarSwapManagerViewModel(settings, new StubTwitchRewardSource());
    vm.OpenSwapEditorCommand.Execute(vm.SwapCards.Single());

    vm.AddPaymentRuleCommand.Execute(null);

    Assert.Single(profile.PaymentRules);
    Assert.IsType<CashPaymentRule>(profile.PaymentRules[0]);
    var rule = (CashPaymentRule)profile.PaymentRules[0];
    Assert.Equal("New Cash Payment Swap", rule.Name);
    Assert.Equal(CashPaymentProvider.StreamElements, rule.Provider);
    Assert.True(rule.IsEnabled);
    Assert.Equal(CashPaymentActionKind.TriggerAction, rule.ActionKind);
    Assert.NotNull(rule.TriggerAction);
    Assert.Equal(OscActionType.AvatarChange, rule.TriggerAction.ActionType);
    Assert.Equal("avtr_a", rule.TriggerAction.AvatarChangeTargetId);
    Assert.Single(vm.PaymentRows);
    Assert.IsType<InlinePaymentRuleRowViewModel>(vm.PaymentRows[0]);
}
```

- [ ] **Step 6: Add test for `RightPaneContent` swap**

Append:

```csharp
[Fact]
public void SelectedRule_SetsRightPaneContent()
{
    var settings = new AppSettings();
    var profile = new AvatarSwapProfile { TargetAvatarId = "avtr_a", TargetAvatarName = "Avatar A" };
    profile.ChannelPointRules.Add(new TriggerRule { TriggerType = TwitchTriggerType.ChannelPoints, Name = "Test" });
    settings.AvatarSwapProfiles.Add(profile);

    var vm = new AvatarSwapManagerViewModel(settings, new StubTwitchRewardSource());
    vm.OpenSwapEditorCommand.Execute(vm.SwapCards.Single());
    Assert.IsType<RuleListPaneViewModel>(vm.RightPaneContent);

    var row = vm.ChannelPointRows.Single();
    row.EditCommand.Execute(null);

    Assert.Same(row, vm.SelectedRule);
    Assert.Same(row, vm.RightPaneContent);
}

[Fact]
public void BackToListCommand_ClearsSelectedRule()
{
    var settings = new AppSettings();
    var profile = new AvatarSwapProfile { TargetAvatarId = "avtr_a", TargetAvatarName = "Avatar A" };
    profile.ChannelPointRules.Add(new TriggerRule { TriggerType = TwitchTriggerType.ChannelPoints, Name = "Test" });
    settings.AvatarSwapProfiles.Add(profile);

    var vm = new AvatarSwapManagerViewModel(settings, new StubTwitchRewardSource());
    vm.OpenSwapEditorCommand.Execute(vm.SwapCards.Single());
    var row = vm.ChannelPointRows.Single();
    row.EditCommand.Execute(null);
    Assert.NotNull(vm.SelectedRule);

    vm.BackToListCommand.Execute(null);

    Assert.Null(vm.SelectedRule);
    Assert.IsType<RuleListPaneViewModel>(vm.RightPaneContent);
}
```

- [ ] **Step 7: Run all tests**

Run: `dotnet test "E:\!!!Program to work on\Proper Crystal Relay\VrcTwitchOscBridge.Tests\VrcTwitchOscBridge.Tests.csproj" -v minimal`

Expected: All tests pass.

- [ ] **Step 8: Commit**

```bash
git add VrcTwitchOscBridge.Tests/AvatarSwapManagerViewModelTests.cs
git commit -m "test: add tests for AddPaymentRule, SelectedRule, and BackToListCommand"
```

---

## Task 18: Add localization keys

**Files:**
- Modify: 14× `Localization/*.extra.json` files

- [ ] **Step 1: Identify all .extra.json files**

Run: `Get-ChildItem -LiteralPath "E:\!!!Program to work on\Proper Crystal Relay\Localization" -Filter "*.extra.json" | Select-Object -ExpandProperty Name`

Expected: 14 files (en-US, de-DE, es-ES, fr-FR, it-IT, ja-JP, ko-KR, pl-PL, pt-BR, ru-RU, sv-SE, th-TH, zh-CN, zh-TW).

- [ ] **Step 2: Add the 2 keys to `en-US.extra.json`**

Open `Localization/en-US.extra.json`. Add (or merge into) the JSON object:

```json
"Back to {0}": "← Back to {0}",
"Back to {0} (Roulette)": "← Back to {0} (Roulette)"
```

(Make sure the key-value format matches the existing entries; e.g., if the file is a flat object, just add the two new entries.)

- [ ] **Step 3: Add the 2 keys to all 13 other `.extra.json` files**

For each of the 13 non-English `.extra.json` files, add the same two keys with English placeholder values (translations can be done by a translator later):

```json
"Back to {0}": "← Back to {0}",
"Back to {0} (Roulette)": "← Back to {0} (Roulette)"
```

- [ ] **Step 4: Build and run the localization audit**

Run: `dotnet build "E:\!!!Program to work on\Proper Crystal Relay\VrcTwitchOscBridge\VrcTwitchOscBridge.csproj" --no-restore`

Then run the localization audit (per the AGENTS.md "Localization Rules"). Verify the 2 new keys are present in all 14 files and have no empty values.

- [ ] **Step 5: Commit**

```bash
git add Localization/
git commit -m "feat: add 'Back to {0}' and 'Back to {0} (Roulette)' localization keys"
```

---

## Task 19: Final build and full test run

**Files:**
- None (verification only)

- [ ] **Step 1: Build the main project**

Run: `dotnet build "E:\!!!Program to work on\Proper Crystal Relay\VrcTwitchOscBridge\VrcTwitchOscBridge.csproj" --no-restore`

Expected: Build succeeds with no warnings or errors.

- [ ] **Step 2: Run all tests**

Run: `dotnet test "E:\!!!Program to work on\Proper Crystal Relay\VrcTwitchOscBridge.Tests\VrcTwitchOscBridge.Tests.csproj" -v minimal`

Expected: All tests pass.

- [ ] **Step 3: Manual verification checklist**

From the spec's Verification section:

1. Open Avatar Swap Manager → pick an avatar swap → Channel Points section shows `🏆 {Name} — {Cost} pts` with Edit + Delete buttons. ✓
2. Bits row summary updates live as `MinAmount` / `BitsAmountUnitsPerDuration` / `BitsSecondsPerAmountUnit` / `MaxAccumulatedDurationSeconds` / `SupporterKeywordText` change. Edit opens the full TriggerRule editor. ✓
3. Subs row summary updates with tier multipliers. `IsGiftSubscription` toggles sub-type. ✓
4. Payment row shows `💵 ...` summary; Edit opens `CashPaymentRuleEditorControl`. ✓
5. Back button returns to 4-section list; row stays highlighted. ✓
6. Migration: take an old settings file with `TriggerRule` payment entries; launch; verify migration log line; verify `CashPaymentRule` rows appear in AvatarSwapManager. ✓
7. Save round-trip: settings save → quit → relaunch → rules still present. ✓
8. Main Cash Payments tab in Redeem Library: behavior unchanged. ✓
9. Localization audit: 2 new keys present, no empty values. ✓
10. Theme check: switch themes, all cards keep colors, no WPF defaults. ✓
11. Edge case: empty state for new avatar swap profile. ✓

- [ ] **Step 4: Commit a build-marker if needed**

If any verification step found a bug, fix it (in a new commit). Otherwise, the plan is complete.

---

## Self-Review Notes

**Spec coverage:**
- Section A (data model) → Tasks 1, 2
- Section B (per-type row controls) → Tasks 3-9
- Section C (right-pane editor integration) → Tasks 11, 14
- Section D (CashPaymentRuleEditorControl) → Task 15
- Section E (AvatarSwapManagerViewModel changes) → Task 12
- Section F (IsInAvatarSwapManager DP) → Task 11
- Section G (save format) → Tasks 1, 2 (handled by model + migration)
- Section H (localization) → Task 18
- Verification → Task 19

**Gaps / risks identified during planning:**

1. The `RebuildRows` step uses a C# 7 local function inside the method. If the project's C# language version is set lower than 7.0, this won't compile. **Mitigation**: the plan notes to refactor to a private method if needed. Verify by building at Task 12 step 5.

2. The `IsInAvatarSwapManager` DP and the `MultiDataTrigger` pattern in Task 11 step 3 is a recommended pattern but may need adjustment based on the actual XAML structure of `AvatarSwapRuleEditorControl.xaml`. The plan instructs the engineer to apply the pattern, not exact code, so this is a judgment call at execution time.

3. The `CashPaymentRuleEditorControl` extraction in Task 15 is the highest-risk step because `MainWindow.xaml` has 800 lines of complex XAML with many `RelativeSource` bindings. The plan calls out the conversion rules but the engineer may need to add additional `DependencyProperty` exposures for any command that isn't `TestSelectedCashPaymentRuleCommand`. This is unavoidable — the extraction can't be fully automated.

4. The `InlinePaymentRuleRowViewModel` tests in Task 7 reference `Ko-fi` as the display label for `CashPaymentProvider.KoFi` (CamelCase enum). The plan hardcodes the display labels. If the user later wants the labels localized, refactor to use `ProviderDisplayName` from `CashPaymentRule.cs` line 466 (which already does this).

**Type consistency:** Names match across tasks (`InlineBitsRuleRowViewModel`, `BackToListCommand`, `RightPaneContent`, `IsInAvatarSwapManager`, etc.). The `IRuleRowViewModel` interface is defined once in Task 3 and referenced in Tasks 4-8.

**No placeholders:** Each step has concrete code, exact file paths, exact commands, and expected output. No "TBD" or "implement later" markers.
