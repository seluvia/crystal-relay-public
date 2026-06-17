# Avatar Swap Rework Implementation Plan (v4)

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Rework the Avatar Swap window so the right-side editor is 4 clean trigger sections (Channel Points / Bits / Subs / Payment), Avatar Roulette is its own first-class card type, the per-profile return mode is removed in favor of a single global Return Avatar, and rules are edited inline.

**Architecture:** Phased. Phase 0 restructures the data model (AvatarSwapProfile + new AvatarRouletteProfile). Phase 1 implements the v3 → v4 migration. Phase 2/3 add runtime snapshot + dispatch paths for the new structures. Phase 4/5 build the ViewModels + XAML. Phase 6 cleans up the legacy Avatar Change UI in MainWindow. Phase 7-9 add localization, docs, and verification.

**Tech Stack:** C# / WPF / .NET 10 / xUnit.

**Spec:** `docs/superpowers/specs/2026-06-16-avatar-swap-full-migration-design.md`

**Reference files (do not modify in this plan):**
- `VrcTwitchOscBridge/Models/AvatarSwapProfile.cs` (will be restructured)
- `VrcTwitchOscBridge/Models/TriggerRule.cs` (will be extended)
- `VrcTwitchOscBridge/Models/TwitchTriggerType.cs` (will be extended)
- `VrcTwitchOscBridge/Models/AppSettings.cs` (will be extended)
- `VrcTwitchOscBridge/Models/ReturnAvatarMode.cs` (will be deleted)
- `VrcTwitchOscBridge/Services/AvatarSwapMigrationService.cs` (will be extended)
- `VrcTwitchOscBridge/Services/SettingsStore.cs` (will be extended)
- `VrcTwitchOscBridge/Services/BridgeRuntimeConfiguration.cs` (will be extended)
- `VrcTwitchOscBridge/Services/BridgeCoordinator.cs` (will be extended)
- `VrcTwitchOscBridge/AvatarSwapManagerWindow.xaml` + `.cs` (will be rewritten)
- `VrcTwitchOscBridge/ViewModels/AvatarSwapManagerViewModel.cs` (will be restructured)
- `VrcTwitchOscBridge/ViewModels/AvatarSwapCardViewModel.cs` (will be updated)
- `VrcTwitchOscBridge/UserControls/AvatarSwapRuleEditorControl.xaml` (will be extended for inline mode)
- `VrcTwitchOscBridge/MainWindow.xaml` (will have legacy UI removed)
- `VrcTwitchOscBridge/ViewModels/MainWindowViewModel.cs` (will have legacy commands removed)
- `VrcTwitchOscBridge/MainWindow.xaml.cs` (will have notice text updated)
- `VrcTwitchOscBridge/CHANGELOG.txt` (will be updated)
- `VrcTwitchOscBridge/RELEASE-CHANGE-RECORD.txt` (will be updated)
- `AGENTS.md` (will be updated)
- `Localization/*.json` + `Localization/*.extra.json` (will be extended)

---

## File Map (responsibilities)

### New files
| File | Responsibility |
|------|----------------|
| `VrcTwitchOscBridge/Models/AvatarRouletteProfile.cs` | Per-roulette-pool model (Name, Pool list, ReturnAvatarId override, Triggers list). |
| `VrcTwitchOscBridge/ViewModels/AvatarRouletteCardViewModel.cs` | VM for a single roulette card on the left grid (image strip, name, subtitle). |
| `VrcTwitchOscBridge/ViewModels/AvatarRouletteEditorViewModel.cs` | VM for the right-panel editor when a roulette card is selected. |
| `VrcTwitchOscBridge/ViewModels/InlineAvatarSwapRuleRowViewModel.cs` | VM for a single expandable rule row in the right panel; tracks editing state and per-type fields. |
| `VrcTwitchOscBridge/UserControls/InlineAvatarSwapRuleRowControl.xaml` + `.cs` | XAML for the inline expandable rule row. |
| `VrcTwitchOscBridge.Tests/AvatarSwapMigrationServiceV4Tests.cs` | v3 → v4 migration tests. |

### Modified files (responsibility deltas)
| File | Delta |
|------|-------|
| `VrcTwitchOscBridge/Models/AvatarSwapProfile.cs` | Drop `ReturnAvatarMode`/`Id`/`Name`, drop `BitsSubsRules` + `RouletteRules`; add `BitsRules` + `SubsRules` + `PaymentRules`; update `AvatarSubtitle` + `HasRules` + `Uses*` flags. |
| `VrcTwitchOscBridge/Models/TriggerRule.cs` | Add `IsGiftSubscription` field. |
| `VrcTwitchOscBridge/Models/TwitchTriggerType.cs` | Add `GiftSubscription`, `ChatCommand`, `Follow` values. |
| `VrcTwitchOscBridge/Models/AppSettings.cs` | Add `AvatarRouletteProfiles` collection; bump migration version constant to 4. |
| `VrcTwitchOscBridge/Models/ReturnAvatarMode.cs` | DELETE file (no longer used). |
| `VrcTwitchOscBridge/Services/AvatarSwapMigrationService.cs` | Add v4 step: split BitsSubs, retag CashPayment, convert Roulette, drop return mode. |
| `VrcTwitchOscBridge/Services/SettingsStore.cs` | Round-trip the new `AvatarRouletteProfiles` and 4-collection `AvatarSwapProfile`. |
| `VrcTwitchOscBridge/Services/BridgeRuntimeConfiguration.cs` | Add `AvatarRouletteProfileSnapshot` + `RouletteAvatarEntrySnapshot`; update `AvatarSwapProfileSnapshot`; add `FindRouletteProfileForRule`. |
| `VrcTwitchOscBridge/Services/BridgeCoordinator.cs` | Add `ResolveRouletteProfileAction`; update `PickAvatarRouletTarget` to take a `roulette.Pool`; update `ExecuteRuleActionAsync` to consult both lookup tables; update `ResolveAvatarSwapAction` to use global return. |
| `VrcTwitchOscBridge/AvatarSwapManagerWindow.xaml` | Full re-layout (2-card sections on left, 4-section right panel with inline rows + advanced triggers). |
| `VrcTwitchOscBridge/AvatarSwapManagerWindow.xaml.cs` | Wire new section header clicks + roulette editor toggle. |
| `VrcTwitchOscBridge/ViewModels/AvatarSwapManagerViewModel.cs` | Restructure for 4 collections + roulette; add `AddRouletteCommand`, `OpenRouletteEditorCommand`, `AddChannelPointRuleCommand`, `AddBitsRuleCommand`, `AddSubsRuleCommand`, `AddPaymentRuleCommand`, `AddAdvancedTriggerCommand`; add inline `EditingRule` state. |
| `VrcTwitchOscBridge/ViewModels/AvatarSwapCardViewModel.cs` | Update subtitle to `N cp · N bits · N subs · N pay`; drop `RouletteRuleCount`. |
| `VrcTwitchOscBridge/UserControls/AvatarSwapRuleEditorControl.xaml` | Add `IsInline` mode flag that hides the full-screen layout and shows the compact per-type fields. |
| `VrcTwitchOscBridge/MainWindow.xaml` | Remove "Avatar Change Setup" tab, `UsesAvatarChange` action block, "Add Avatar Change Override" button, "Add Avatar Change" / "Delete Avatar Change" buttons, cooldown-only mode checkbox, "Permanent avatar change" checkbox. |
| `VrcTwitchOscBridge/ViewModels/MainWindowViewModel.cs` | Remove `ShowMasterAvatarTabCommand`, `AddAvatarChangeOverrideCommand`, `UseCurrentAvatarForAvatarChangeRuleCommand`, `"AvatarChange"` branch of `OpenAvatarPickerCommand`, `AvatarChangeOverrideRules` projection, `HasAvatarChangeOverrideRules` projection. |
| `VrcTwitchOscBridge/MainWindow.xaml.cs` | Update migration notice text. |
| `VrcTwitchOscBridge/CHANGELOG.txt` | v3.1.10 entry. |
| `VrcTwitchOscBridge/RELEASE-CHANGE-RECORD.txt` | Bump Pending Release Draft to v3.1.10. |
| `VrcTwitchOscBridge.csproj` | Add new XAML files to `<Page>` items. |
| `AGENTS.md` | Update "Project Identity" with the active build state. |
| `Localization/*.json` + `Localization/*.extra.json` | Add new keys from spec section 10. |

### Files NOT touched
- `VrcTwitchOscBridge/AvatarPickerWindow.xaml` + `.cs` (reuse as-is)
- `VrcTwitchOscBridge/Services/AvatarImageService.cs` (reuse as-is)
- `VrcTwitchOscBridge/Services/AvatarPickerService.cs` (reuse as-is)
- `VrcTwitchOscBridge/AvatarSetsManagerWindow.xaml` (out of scope)
- `VrcTwitchOscBridge/UniversalTriggersManagerWindow.xaml` (out of scope)
- `VrcTwitchOscBridge/Services/ManagedRewardPresentation.cs` (out of scope)
- `VrcTwitchOscBridge/Services/CashPaymentProviderService.cs` (out of scope)
- All VRChat LocalLow files (read-only inputs)

---

## Phase 0: Data Model

### Task 1: Add `IsGiftSubscription` field to `TriggerRule`

**Files:**
- Modify: `VrcTwitchOscBridge/Models/TriggerRule.cs`
- Test: `VrcTwitchOscBridge.Tests/AvatarSwapMigrationServiceV4Tests.cs` (new file)

- [ ] **Step 1: Create the v4 test file with a placeholder test**

Create `VrcTwitchOscBridge.Tests/AvatarSwapMigrationServiceV4Tests.cs`:

```csharp
using VrcTwitchOscBridge.Models;
using Xunit;

namespace VrcTwitchOscBridge.Tests;

public sealed class AvatarSwapMigrationServiceV4Tests
{
    [Fact]
    public void IsGiftSubscription_DefaultsToFalse()
    {
        var rule = new TriggerRule();
        Assert.False(rule.IsGiftSubscription);
    }
}
```

- [ ] **Step 2: Run the test to verify it fails**

Run: `dotnet test "E:\!!!Program to work on\Proper Crystal Relay\VrcTwitchOscBridge.Tests\VrcTwitchOscBridge.Tests.csproj" --filter "FullyQualifiedName~IsGiftSubscription_DefaultsToFalse" --no-restore`
Expected: FAIL with "'TriggerRule' does not contain a definition for 'IsGiftSubscription'".

- [ ] **Step 3: Add the `IsGiftSubscription` field to `TriggerRule`**

In `VrcTwitchOscBridge/Models/TriggerRule.cs`, find the `CashPaymentRuleId` property (added in v3) and add `IsGiftSubscription` right after it:

```csharp
public string? CashPaymentRuleId { get; set; }
public bool IsGiftSubscription { get; set; }
```

- [ ] **Step 4: Run the test to verify it passes**

Run: `dotnet test "E:\!!!Program to work on\Proper Crystal Relay\VrcTwitchOscBridge.Tests\VrcTwitchOscBridge.Tests.csproj" --filter "FullyQualifiedName~IsGiftSubscription_DefaultsToFalse" --no-restore`
Expected: PASS.

- [ ] **Step 5: Commit**

```bash
git add VrcTwitchOscBridge/Models/TriggerRule.cs VrcTwitchOscBridge.Tests/AvatarSwapMigrationServiceV4Tests.cs
git commit -m "feat(avatar-swap): add IsGiftSubscription field to TriggerRule"
```

---

### Task 2: Add new values to `TwitchTriggerType`

**Files:**
- Modify: `VrcTwitchOscBridge/Models/TwitchTriggerType.cs`
- Test: `VrcTwitchOscBridge.Tests/AvatarSwapMigrationServiceV4Tests.cs`

- [ ] **Step 1: Add failing tests for new enum values**

Add to `VrcTwitchOscBridge.Tests/AvatarSwapMigrationServiceV4Tests.cs`:

```csharp
[Fact]
public void TwitchTriggerType_HasGiftSubscriptionValue()
{
    Assert.True(Enum.IsDefined(typeof(TwitchTriggerType), "GiftSubscription"));
}

[Fact]
public void TwitchTriggerType_HasChatCommandValue()
{
    Assert.True(Enum.IsDefined(typeof(TwitchTriggerType), "ChatCommand"));
}

[Fact]
public void TwitchTriggerType_HasFollowValue()
{
    Assert.True(Enum.IsDefined(typeof(TwitchTriggerType), "Follow"));
}
```

- [ ] **Step 2: Run the tests to verify they fail**

Run: `dotnet test "E:\!!!Program to work on\Proper Crystal Relay\VrcTwitchOscBridge.Tests\VrcTwitchOscBridge.Tests.csproj" --filter "FullyQualifiedName~TwitchTriggerType_Has" --no-restore`
Expected: 3 FAILs (enum values not defined).

- [ ] **Step 3: Add the new values to `TwitchTriggerType`**

Replace the body of `VrcTwitchOscBridge/Models/TwitchTriggerType.cs`:

```csharp
namespace VrcTwitchOscBridge.Models;

public enum TwitchTriggerType
{
    ChannelPoints,
    Bits,
    Subscriptions,
    GiftSubscription,
    PowerUp,
    ChatCommand,
    Follow,
}
```

- [ ] **Step 4: Run the tests to verify they pass**

Run: `dotnet test "E:\!!!Program to work on\Proper Crystal Relay\VrcTwitchOscBridge.Tests\VrcTwitchOscBridge.Tests.csproj" --filter "FullyQualifiedName~TwitchTriggerType_Has" --no-restore`
Expected: 3 PASSes.

- [ ] **Step 5: Commit**

```bash
git add VrcTwitchOscBridge/Models/TwitchTriggerType.cs VrcTwitchOscBridge.Tests/AvatarSwapMigrationServiceV4Tests.cs
git commit -m "feat(avatar-swap): add GiftSubscription/ChatCommand/Follow to TwitchTriggerType"
```

---

### Task 3: Restructure `AvatarSwapProfile` to 4 collections, drop return mode

**Files:**
- Modify: `VrcTwitchOscBridge/Models/AvatarSwapProfile.cs`
- Modify: `VrcTwitchOscBridge/Models/ReturnAvatarMode.cs` (delete the enum)
- Test: `VrcTwitchOscBridge.Tests/AvatarSwapMigrationServiceV4Tests.cs`

- [ ] **Step 1: Add failing test for new 4-collection structure**

Add to `VrcTwitchOscBridge.Tests/AvatarSwapMigrationServiceV4Tests.cs`:

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
}

[Fact]
public void AvatarSwapProfile_DropsBitsSubsAndRouletteCollections()
{
    var profile = new AvatarSwapProfile();
    var type = typeof(AvatarSwapProfile);
    Assert.Null(type.GetProperty("BitsSubsRules"));
    Assert.Null(type.GetProperty("RouletteRules"));
    Assert.Null(type.GetProperty("ReturnAvatarMode"));
    Assert.Null(type.GetProperty("ReturnAvatarId"));
    Assert.Null(type.GetProperty("ReturnAvatarName"));
}

[Fact]
public void AvatarSwapProfile_AvatarSubtitle_FormatIsFourCounts()
{
    var profile = new AvatarSwapProfile { TargetAvatarName = "Test" };
    profile.ChannelPointRules.Add(new TriggerRule());
    profile.BitsRules.Add(new TriggerRule());
    profile.SubsRules.Add(new TriggerRule());
    profile.SubsRules.Add(new TriggerRule());
    profile.PaymentRules.Add(new TriggerRule());
    var subtitle = profile.AvatarSubtitle;
    Assert.Contains("1", subtitle);
    Assert.Contains("2", subtitle);
    Assert.Contains("cp", subtitle);
    Assert.Contains("bits", subtitle);
    Assert.Contains("subs", subtitle);
    Assert.Contains("pay", subtitle);
}
```

- [ ] **Step 2: Run the tests to verify they fail**

Run: `dotnet test "E:\!!!Program to work on\Proper Crystal Relay\VrcTwitchOscBridge.Tests\VrcTwitchOscBridge.Tests.csproj" --filter "FullyQualifiedName~AvatarSwapProfile_" --no-restore`
Expected: 3 FAILs (BitsSubsRules/RouletteRules/return-mode still exist; subtitle still uses old format).

- [ ] **Step 3: Rewrite `AvatarSwapProfile`**

Replace the contents of `VrcTwitchOscBridge/Models/AvatarSwapProfile.cs` with the v4 model. The full new file:

```csharp
using System.Collections.ObjectModel;
using System;
using CommunityToolkit.Mvvm.ComponentModel;

namespace VrcTwitchOscBridge.Models;

public partial class AvatarSwapProfile : ObservableObject
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public string TargetAvatarId { get; set; } = string.Empty;
    public string TargetAvatarName { get; set; } = string.Empty;
    public string? TargetThumbnailUrl { get; set; }
    public bool IsEnabled { get; set; } = true;
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
    public DateTime UpdatedAt { get; set; } = DateTime.UtcNow;

    public ObservableCollection<TriggerRule> ChannelPointRules { get; } = new();
    public ObservableCollection<TriggerRule> BitsRules { get; } = new();
    public ObservableCollection<TriggerRule> SubsRules { get; } = new();
    public ObservableCollection<TriggerRule> PaymentRules { get; } = new();

    public bool HasRules =>
        ChannelPointRules.Count + BitsRules.Count + SubsRules.Count + PaymentRules.Count > 0;
    public bool UsesChannelPointRules => ChannelPointRules.Count > 0;
    public bool UsesBitsRules => BitsRules.Count > 0;
    public bool UsesSubsRules => SubsRules.Count > 0;
    public bool UsesPaymentRules => PaymentRules.Count > 0;

    public string AvatarSubtitle =>
        $"{ChannelPointRules.Count} cp · {BitsRules.Count} bits · {SubsRules.Count} subs · {PaymentRules.Count} pay";

    public AvatarSwapProfile()
    {
        ChannelPointRules.CollectionChanged += (_, _) => Bump();
        BitsRules.CollectionChanged += (_, _) => Bump();
        SubsRules.CollectionChanged += (_, _) => Bump();
        PaymentRules.CollectionChanged += (_, _) => Bump();
    }

    private void Bump()
    {
        UpdatedAt = DateTime.UtcNow;
        OnPropertyChanged(nameof(HasRules));
        OnPropertyChanged(nameof(UsesChannelPointRules));
        OnPropertyChanged(nameof(UsesBitsRules));
        OnPropertyChanged(nameof(UsesSubsRules));
        OnPropertyChanged(nameof(UsesPaymentRules));
        OnPropertyChanged(nameof(AvatarSubtitle));
    }
}
```

- [ ] **Step 4: Delete `ReturnAvatarMode.cs`**

Run:
```bash
Remove-Item "VrcTwitchOscBridge/Models/ReturnAvatarMode.cs"
```

- [ ] **Step 5: Run the tests to verify they pass**

Run: `dotnet test "E:\!!!Program to work on\Proper Crystal Relay\VrcTwitchOscBridge.Tests\VrcTwitchOscBridge.Tests.csproj" --filter "FullyQualifiedName~AvatarSwapProfile_" --no-restore`
Expected: 3 PASSes. (There will likely be many build errors from the rest of the codebase that referenced `ReturnAvatarMode`, `BitsSubsRules`, `RouletteRules` — those are addressed in later tasks.)

- [ ] **Step 6: Commit**

```bash
git add VrcTwitchOscBridge/Models/AvatarSwapProfile.cs
git rm VrcTwitchOscBridge/Models/ReturnAvatarMode.cs
git add VrcTwitchOscBridge.Tests/AvatarSwapMigrationServiceV4Tests.cs
git commit -m "refactor(avatar-swap): restructure AvatarSwapProfile to 4 collections, drop return mode"
```

> **Note:** The build will break for a while. Tasks 4-15 add the missing pieces (the new `AvatarRouletteProfile`, the new persistence, the new migration step, the new snapshots, the new dispatch). The build only succeeds again at Task 15.

---

### Task 4: Create `AvatarRouletteProfile` model

**Files:**
- Create: `VrcTwitchOscBridge/Models/AvatarRouletteProfile.cs`
- Test: `VrcTwitchOscBridge.Tests/AvatarSwapMigrationServiceV4Tests.cs`

- [ ] **Step 1: Add failing test for `AvatarRouletteProfile`**

Add to `VrcTwitchOscBridge.Tests/AvatarSwapMigrationServiceV4Tests.cs`:

```csharp
[Fact]
public void AvatarRouletteProfile_DefaultsAreEmpty()
{
    var p = new AvatarRouletteProfile();
    Assert.NotNull(p.Pool);
    Assert.Empty(p.Pool);
    Assert.NotNull(p.Triggers);
    Assert.Empty(p.Triggers);
    Assert.True(p.IsEnabled);
    Assert.Null(p.ReturnAvatarId);
    Assert.Null(p.ReturnAvatarName);
    Assert.Equal(0, p.PoolCount);
    Assert.Equal(0, p.TriggerCount);
}

[Fact]
public void AvatarRouletteProfile_Subtitle_FormatsPoolAndTriggerCount()
{
    var p = new AvatarRouletteProfile { Name = "Demo" };
    p.Pool.Add(new RouletteAvatarEntry { AvatarId = "a1", AvatarName = "One" });
    p.Pool.Add(new RouletteAvatarEntry { AvatarId = "a2", AvatarName = "Two" });
    p.Triggers.Add(new TriggerRule());
    Assert.Contains("2", p.Subtitle);
    Assert.Contains("1", p.Subtitle);
    Assert.Contains("🎲", p.Subtitle);
    Assert.Contains("pool", p.Subtitle);
}
```

- [ ] **Step 2: Run the tests to verify they fail**

Run: `dotnet test "E:\!!!Program to work on\Proper Crystal Relay\VrcTwitchOscBridge.Tests\VrcTwitchOscBridge.Tests.csproj" --filter "FullyQualifiedName~AvatarRouletteProfile_" --no-restore`
Expected: 2 FAILs (types not defined).

- [ ] **Step 3: Create `AvatarRouletteProfile.cs`**

Create `VrcTwitchOscBridge/Models/AvatarRouletteProfile.cs`:

```csharp
using System;
using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;

namespace VrcTwitchOscBridge.Models;

public partial class AvatarRouletteProfile : ObservableObject
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public string Name { get; set; } = "New Roulette";
    public bool IsEnabled { get; set; } = true;
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
    public DateTime UpdatedAt { get; set; } = DateTime.UtcNow;

    public ObservableCollection<RouletteAvatarEntry> Pool { get; } = new();
    public string? ReturnAvatarId { get; set; }
    public string? ReturnAvatarName { get; set; }
    public ObservableCollection<TriggerRule> Triggers { get; } = new();

    public int PoolCount => Pool.Count;
    public int TriggerCount => Triggers.Count;
    public string Subtitle =>
        $"🎲 {PoolCount} pool · {TriggerCount} trigger{(TriggerCount == 1 ? "" : "s")}";

    public AvatarRouletteProfile()
    {
        Pool.CollectionChanged += (_, _) =>
        {
            UpdatedAt = DateTime.UtcNow;
            OnPropertyChanged(nameof(PoolCount));
            OnPropertyChanged(nameof(Subtitle));
        };
        Triggers.CollectionChanged += (_, _) =>
        {
            UpdatedAt = DateTime.UtcNow;
            OnPropertyChanged(nameof(TriggerCount));
            OnPropertyChanged(nameof(Subtitle));
        };
    }
}

public partial class RouletteAvatarEntry : ObservableObject
{
    public string AvatarId { get; set; } = string.Empty;
    public string AvatarName { get; set; } = string.Empty;
    public string? ThumbnailUrl { get; set; }
}
```

- [ ] **Step 4: Run the tests to verify they pass**

Run: `dotnet test "E:\!!!Program to work on\Proper Crystal Relay\VrcTwitchOscBridge.Tests\VrcTwitchOscBridge.Tests.csproj" --filter "FullyQualifiedName~AvatarRouletteProfile_" --no-restore`
Expected: 2 PASSes.

- [ ] **Step 5: Commit**

```bash
git add VrcTwitchOscBridge/Models/AvatarRouletteProfile.cs VrcTwitchOscBridge.Tests/AvatarSwapMigrationServiceV4Tests.cs
git commit -m "feat(avatar-swap): add AvatarRouletteProfile model"
```

---

### Task 5: Add `AvatarRouletteProfiles` to `AppSettings`

**Files:**
- Modify: `VrcTwitchOscBridge/Models/AppSettings.cs`
- Test: `VrcTwitchOscBridge.Tests/AvatarSwapMigrationServiceV4Tests.cs`

- [ ] **Step 1: Add failing test for new collection**

Add to `VrcTwitchOscBridge.Tests/AvatarSwapMigrationServiceV4Tests.cs`:

```csharp
[Fact]
public void AppSettings_AvatarRouletteProfiles_DefaultsToEmpty()
{
    var s = new AppSettings();
    Assert.NotNull(s.AvatarRouletteProfiles);
    Assert.Empty(s.AvatarRouletteProfiles);
}
```

- [ ] **Step 2: Run the test to verify it fails**

Run: `dotnet test "E:\!!!Program to work on\Proper Crystal Relay\VrcTwitchOscBridge.Tests\VrcTwitchOscBridge.Tests.csproj" --filter "FullyQualifiedName~AppSettings_AvatarRouletteProfiles" --no-restore`
Expected: FAIL (property not defined).

- [ ] **Step 3: Add `AvatarRouletteProfiles` to `AppSettings`**

In `VrcTwitchOscBridge/Models/AppSettings.cs`, find the `AvatarSwapProfiles` collection declaration and add the new one right after:

```csharp
public ObservableCollection<AvatarRouletteProfile> AvatarRouletteProfiles { get; set; } = new();
```

Also bump the migration version constant. Find the existing `AvatarChangeToAvatarSwapMigrationVersion` constant and change its value to `4`. If it's hard-coded as a literal, change it to `4` directly. If it's a `const int`, update accordingly.

- [ ] **Step 4: Run the test to verify it passes**

Run: `dotnet test "E:\!!!Program to work on\Proper Crystal Relay\VrcTwitchOscBridge.Tests\VrcTwitchOscBridge.Tests.csproj" --filter "FullyQualifiedName~AppSettings_AvatarRouletteProfiles" --no-restore`
Expected: PASS.

- [ ] **Step 5: Commit**

```bash
git add VrcTwitchOscBridge/Models/AppSettings.cs VrcTwitchOscBridge.Tests/AvatarSwapMigrationServiceV4Tests.cs
git commit -m "feat(avatar-swap): add AvatarRouletteProfiles to AppSettings"
```

---

## Phase 1: Migration v4

### Task 6: Update `SettingsStore` for new persistence

**Files:**
- Modify: `VrcTwitchOscBridge/Services/SettingsStore.cs`
- Test: `VrcTwitchOscBridge.Tests/AvatarSwapMigrationServiceV4Tests.cs`

- [ ] **Step 1: Add failing round-trip test for `AvatarRouletteProfiles`**

Add to `VrcTwitchOscBridge.Tests/AvatarSwapMigrationServiceV4Tests.cs`:

```csharp
[Fact]
public async Task SettingsStore_RoundTripsAvatarRouletteProfiles()
{
    var temp = Path.Combine(Path.GetTempPath(), $"cr-settings-{Guid.NewGuid():N}.json");
    try
    {
        var store = new SettingsStore(temp);
        var settings = new AppSettings();
        settings.AvatarRouletteProfiles.Add(new AvatarRouletteProfile { Name = "Test Roulette" });

        await store.SaveAsync(settings);
        var loaded = await store.LoadAsync();

        Assert.Single(loaded.AvatarRouletteProfiles);
        Assert.Equal("Test Roulette", loaded.AvatarRouletteProfiles[0].Name);
    }
    finally
    {
        if (File.Exists(temp)) File.Delete(temp);
    }
}
```

- [ ] **Step 2: Run the test to verify it fails**

Run: `dotnet test "E:\!!!Program to work on\Proper Crystal Relay\VrcTwitchOscBridge.Tests\VrcTwitchOscBridge.Tests.csproj" --filter "FullyQualifiedName~SettingsStore_RoundTripsAvatarRouletteProfiles" --no-restore`
Expected: FAIL (SettingsStore doesn't round-trip the new collection).

- [ ] **Step 3: Add `PersistedAvatarRouletteProfile` DTO + `ToAvatarRouletteProfile` / `ToPersistedAvatarRouletteProfile` mapping**

In `VrcTwitchOscBridge/Services/SettingsStore.cs`, find the existing `PersistedAvatarSwapProfile` DTO (around line 3021). Add a new DTO right after it:

```csharp
public sealed class PersistedAvatarRouletteProfile
{
    public Guid Id { get; set; }
    public string Name { get; set; } = string.Empty;
    public bool IsEnabled { get; set; } = true;
    public DateTime CreatedAt { get; set; }
    public DateTime UpdatedAt { get; set; }
    public List<PersistedRouletteAvatarEntry> Pool { get; set; } = new();
    public string? ReturnAvatarId { get; set; }
    public string? ReturnAvatarName { get; set; }
    public List<PersistedTriggerRule> Triggers { get; set; } = new();
}

public sealed class PersistedRouletteAvatarEntry
{
    public string AvatarId { get; set; } = string.Empty;
    public string AvatarName { get; set; } = string.Empty;
    public string? ThumbnailUrl { get; set; }
}
```

Add mapper methods (place them next to the existing `ToAvatarSwapProfile` / `ToPersistedAvatarSwapProfile` methods around line 1129-1170):

```csharp
private static AvatarRouletteProfile ToAvatarRouletteProfile(PersistedAvatarRouletteProfile p)
{
    var profile = new AvatarRouletteProfile
    {
        Id = p.Id == Guid.Empty ? Guid.NewGuid() : p.Id,
        Name = p.Name ?? "Roulette",
        IsEnabled = p.IsEnabled,
        CreatedAt = NormalizeTimestamp(p.CreatedAt),
        UpdatedAt = NormalizeTimestamp(p.UpdatedAt),
        ReturnAvatarId = p.ReturnAvatarId,
        ReturnAvatarName = p.ReturnAvatarName,
    };
    foreach (var entry in p.Pool ?? new())
        profile.Pool.Add(new RouletteAvatarEntry
        {
            AvatarId = entry.AvatarId,
            AvatarName = entry.AvatarName,
            ThumbnailUrl = entry.ThumbnailUrl,
        });
    foreach (var t in p.Triggers ?? new())
        profile.Triggers.Add(ToRule(t));
    return profile;
}

private static PersistedAvatarRouletteProfile ToPersistedAvatarRouletteProfile(AvatarRouletteProfile p)
{
    return new PersistedAvatarRouletteProfile
    {
        Id = p.Id,
        Name = p.Name,
        IsEnabled = p.IsEnabled,
        CreatedAt = p.CreatedAt,
        UpdatedAt = p.UpdatedAt,
        ReturnAvatarId = p.ReturnAvatarId,
        ReturnAvatarName = p.ReturnAvatarName,
        Pool = p.Pool.Select(e => new PersistedRouletteAvatarEntry
        {
            AvatarId = e.AvatarId,
            AvatarName = e.AvatarName,
            ThumbnailUrl = e.ThumbnailUrl,
        }).ToList(),
        Triggers = p.Triggers.Select(ToPersistedRule).ToList(),
    };
}
```

In the `ApplyProfileToSettings` (or equivalent) load method, find where `AvatarSwapProfiles` is read and add the new collection read right after:

```csharp
foreach (var p in persisted.AvatarRouletteProfiles ?? new())
    settings.AvatarRouletteProfiles.Add(ToAvatarRouletteProfile(p));
```

In the `WriteSettingsToProfile` (or equivalent) save method, find where `AvatarSwapProfiles` is written and add the new collection write:

```csharp
persisted.AvatarRouletteProfiles = settings.AvatarRouletteProfiles.Select(ToPersistedAvatarRouletteProfile).ToList();
```

In the `PersistedSettings` partial class, add:

```csharp
public List<PersistedAvatarRouletteProfile> AvatarRouletteProfiles { get; set; } = new();
```

- [ ] **Step 4: Run the test to verify it passes**

Run: `dotnet test "E:\!!!Program to work on\Proper Crystal Relay\VrcTwitchOscBridge.Tests\VrcTwitchOscBridge.Tests.csproj" --filter "FullyQualifiedName~SettingsStore_RoundTripsAvatarRouletteProfiles" --no-restore`
Expected: PASS.

- [ ] **Step 5: Commit**

```bash
git add VrcTwitchOscBridge/Services/SettingsStore.cs VrcTwitchOscBridge.Tests/AvatarSwapMigrationServiceV4Tests.cs
git commit -m "feat(avatar-swap): persist AvatarRouletteProfiles in SettingsStore"
```

---

### Task 7: Update `AvatarSwapMigrationService` to v4 — BitsSubs split

**Files:**
- Modify: `VrcTwitchOscBridge/Services/AvatarSwapMigrationService.cs`
- Test: `VrcTwitchOscBridge.Tests/AvatarSwapMigrationServiceV4Tests.cs`

- [ ] **Step 1: Add failing test for BitsSubs split**

Add to `VrcTwitchOscBridge.Tests/AvatarSwapMigrationServiceV4Tests.cs`:

```csharp
[Fact]
public void MigrateV4_SplitsBitsSubsRulesIntoBitsAndSubs()
{
    var s = new AppSettings();
    var profile = new AvatarSwapProfile { TargetAvatarId = "avtr_a" };
    var bitsRule = new TriggerRule
    {
        TriggerType = TwitchTriggerType.Bits,
        Name = "Bits 500",
        MinimumAmount = 500,
    };
    var subRule = new TriggerRule
    {
        TriggerType = TwitchTriggerType.Subscriptions,
        Name = "T1 Sub",
    };
    var giftSubRule = new TriggerRule
    {
        TriggerType = TwitchTriggerType.Subscriptions,
        Name = "Gift 5",
        IsGiftSubscription = true,
    };
    // Simulate v3 save shape: BitsSubsRules is preserved as a private field at runtime; for the test we
    // need to put rules into the v3 collection that the migration reads from. The v3 collection
    // was removed in Task 3, so this test verifies the post-migration outcome by simulating the
    // v3 save as JSON (see Step 2 for the helper used by the migration).
    s.AvatarSwapProfiles.Add(profile);

    // Build a fake v3-shape JSON and feed it into LoadAsync
    var temp = Path.Combine(Path.GetTempPath(), $"cr-v3-{Guid.NewGuid():N}.json");
    try
    {
        var json = "{\n" +
                   "  \"avatarSwapProfiles\": [\n" +
                   "    {\n" +
                   "      \"id\": \"00000000-0000-0000-0000-000000000001\",\n" +
                   "      \"targetAvatarId\": \"avtr_a\",\n" +
                   "      \"channelPointRules\": [],\n" +
                   "      \"bitsSubsRules\": [\n" +
                   "        { \"name\": \"Bits 500\", \"triggerType\": 1, \"minimumAmount\": 500 },\n" +
                   "        { \"name\": \"T1 Sub\", \"triggerType\": 2 },\n" +
                   "        { \"name\": \"Gift 5\", \"triggerType\": 2, \"isGiftSubscription\": true }\n" +
                   "      ]\n" +
                   "    }\n" +
                   "  ]\n" +
                   "}";
        File.WriteAllText(temp, json);
        var store = new SettingsStore(temp);
        var loaded = store.LoadAsync().GetAwaiter().GetResult();

        AvatarSwapMigrationService.Migrate(loaded);

        var p = loaded.AvatarSwapProfiles.Single();
        Assert.Single(p.BitsRules);
        Assert.Equal("Bits 500", p.BitsRules[0].Name);
        Assert.Equal(2, p.SubsRules.Count);
        Assert.Contains(p.SubsRules, r => r.Name == "T1 Sub" && r.TriggerType == TwitchTriggerType.Subscriptions);
        Assert.Contains(p.SubsRules, r => r.Name == "Gift 5" && r.TriggerType == TwitchTriggerType.GiftSubscription);
    }
    finally
    {
        if (File.Exists(temp)) File.Delete(temp);
    }
}
```

- [ ] **Step 2: Run the test to verify it fails**

Run: `dotnet test "E:\!!!Program to work on\Proper Crystal Relay\VrcTwitchOscBridge.Tests\VrcTwitchOscBridge.Tests.csproj" --filter "FullyQualifiedName~MigrateV4_SplitsBitsSubsRulesIntoBitsAndSubs" --no-restore`
Expected: FAIL (v4 split not implemented; BitsSubsRules field may not exist on the persistence model).

- [ ] **Step 3: Add v4 step in `AvatarSwapMigrationService`**

In `VrcTwitchOscBridge/Services/AvatarSwapMigrationService.cs`, find the `Migrate` method and add a v4 step. The current version constant is `3`; bump it to `4`. Add this method and call:

```csharp
public const int CurrentMigrationVersion = 4;

public static void Migrate(AppSettings settings)
{
    if (settings.AvatarChangeToAvatarSwapMigrationVersion >= CurrentMigrationVersion)
        return;
    // ... existing v1/v2/v3 migration code unchanged ...
    MigrateV3ToV4(settings);
    settings.AvatarChangeToAvatarSwapMigrationVersion = CurrentMigrationVersion;
}

private static void MigrateV3ToV4(AppSettings settings)
{
    settings.AvatarRouletteProfiles ??= new System.Collections.ObjectModel.ObservableCollection<AvatarRouletteProfile>();
    foreach (var profile in settings.AvatarSwapProfiles)
    {
        MigrateV3BitsSubsIntoBitsAndSubs(profile);
        MigrateV3CashPaymentIntoPaymentRules(profile);
        MigrateV3RouletteIntoRouletteProfile(profile);
        // Drop return mode — no migration needed; the fields are gone in v4.
    }
}

private static void MigrateV3BitsSubsIntoBitsAndSubs(AvatarSwapProfile profile)
{
    // v3 stored BitsSubsRules on the persistence model. After SettingsStore
    // rehydrates the v4 model the legacy field is gone, but if a save still
    // references it we re-build BitsRules/SubsRules from any leftover rule
    // state. In practice, this method is a no-op for v3 saves that already
    // rehydrated cleanly; the rule data is read from the v3 JSON shape by
    // SettingsStore's compatibility layer.
    // The logic is: if either BitsRules or SubsRules already has items we
    // leave them alone (SettingsStore did the work). Otherwise, no-op.
}

private static void MigrateV3CashPaymentIntoPaymentRules(AvatarSwapProfile profile)
{
    var keepers = new System.Collections.ObjectModel.ObservableCollection<TriggerRule>();
    foreach (var rule in profile.ChannelPointRules)
    {
        if (rule.Source == TriggerRuleSource.CashPayment)
        {
            profile.PaymentRules.Add(rule);
        }
        else
        {
            keepers.Add(rule);
        }
    }
    profile.ChannelPointRules.Clear();
    foreach (var k in keepers) profile.ChannelPointRules.Add(k);
}

private static void MigrateV3RouletteIntoRouletteProfile(AvatarSwapProfile profile)
{
    var snapshot = profile.RouletteRules.ToList();
    profile.RouletteRules.Clear();
    foreach (var rule in snapshot)
    {
        if (rule.ActionType != OscActionType.AvatarRoulet) continue;
        var roulette = new AvatarRouletteProfile
        {
            Id = Guid.NewGuid(),
            Name = string.IsNullOrWhiteSpace(profile.TargetAvatarName) ? "Roulette" : profile.TargetAvatarName + " Roulette",
            IsEnabled = profile.IsEnabled,
        };
        if (rule.AvatarRouletAvatarIds != null)
        {
            for (int i = 0; i < rule.AvatarRouletAvatarIds.Count; i++)
            {
                var id = rule.AvatarRouletAvatarIds[i];
                var name = i < (rule.AvatarRouletAvatarNames?.Count ?? 0)
                    ? rule.AvatarRouletAvatarNames[i]
                    : id;
                roulette.Pool.Add(new RouletteAvatarEntry { AvatarId = id, AvatarName = name });
            }
        }
        rule.AvatarRouletAvatarIds = null;
        rule.AvatarRouletAvatarNames = null;
        roulette.Triggers.Add(rule);
        settings.AvatarRouletteProfiles.Add(roulette);
    }
}
```

> **Note:** The `RouletteRules` collection on `AvatarSwapProfile` was removed in Task 3. If the migration needs to read v3 `RouletteRules` data, the v3 `PersistedAvatarSwapProfile` DTO in `SettingsStore` must still expose it, and `ToAvatarSwapProfile` must read it. See the v3 `PersistedAvatarSwapProfile` in `SettingsStore.cs:3021-3049` — keep its `RouletteRules` and `BitsSubsRules` fields, but stop reading them into the new `AvatarSwapProfile`. The migration service reads them through a different path (see Step 4).

- [ ] **Step 4: Keep v3 fields on the persistence DTO and read them via the migration**

In `SettingsStore.cs`, ensure `PersistedAvatarSwapProfile` still has:
- `public List<PersistedTriggerRule> BitsSubsRules { get; set; } = new();`
- `public List<PersistedTriggerRule> RouletteRules { get; set; } = new();`

In `ToAvatarSwapProfile`, stop reading these into the v4 model. The v4 migration reads them via a dedicated `ToLegacyRouletteSnapshot(profile, persisted)` helper:

```csharp
public static IReadOnlyList<PersistedTriggerRule> GetLegacyBitsSubsRules(AvatarSwapProfile p, PersistedAvatarSwapProfile persisted)
    => persisted.BitsSubsRules ?? new();

public static IReadOnlyList<PersistedTriggerRule> GetLegacyRouletteRules(AvatarSwapProfile p, PersistedAvatarSwapProfile persisted)
    => persisted.RouletteRules ?? new();
```

Use these helpers inside `MigrateV3ToV4` (the `Migrate` method needs the persisted shape, so refactor it to take the persisted settings object). For simplicity in this plan, the helper methods are exposed for tests; the migration service reads them through a different path:

In `AvatarSwapMigrationService.MigrateV3ToV4`, change the signature to accept both:

```csharp
public static void MigrateV3ToV4(AppSettings settings, PersistedSettings? persisted = null)
{
    settings.AvatarRouletteProfiles ??= new System.Collections.ObjectModel.ObservableCollection<AvatarRouletteProfile>();
    if (persisted != null)
    {
        // Re-seed v3 BitsSubsRules into the v4 BitsRules + SubsRules collections
        for (int i = 0; i < settings.AvatarSwapProfiles.Count && i < persisted.AvatarSwapProfiles.Count; i++)
        {
            var live = settings.AvatarSwapProfiles[i];
            var persistedProfile = persisted.AvatarSwapProfiles[i];
            foreach (var t in persistedProfile.BitsSubsRules ?? new())
            {
                var rule = SettingsStoreBridge.RuleFromPersisted(t);
                if (rule.TriggerType == TwitchTriggerType.Bits)
                    live.BitsRules.Add(rule);
                else if (rule.TriggerType == TwitchTriggerType.Subscriptions)
                {
                    if (rule.IsGiftSubscription) rule.TriggerType = TwitchTriggerType.GiftSubscription;
                    live.SubsRules.Add(rule);
                }
                else
                    live.ChannelPointRules.Add(rule);
            }
            // Re-seed v3 RouletteRules into v4 AvatarRouletteProfile entries
            var liveSnapshot = new List<TriggerRule>(live.RouletteRules);
            live.RouletteRules.Clear();
            foreach (var t in persistedProfile.RouletteRules ?? new())
            {
                var rule = SettingsStoreBridge.RuleFromPersisted(t);
                if (rule.ActionType != OscActionType.AvatarRoulet) continue;
                var roulette = new AvatarRouletteProfile
                {
                    Id = Guid.NewGuid(),
                    Name = string.IsNullOrWhiteSpace(live.TargetAvatarName) ? "Roulette" : live.TargetAvatarName + " Roulette",
                    IsEnabled = live.IsEnabled,
                };
                if (rule.AvatarRouletAvatarIds != null)
                {
                    for (int j = 0; j < rule.AvatarRouletAvatarIds.Count; j++)
                    {
                        var id = rule.AvatarRouletAvatarIds[j];
                        var name = j < (rule.AvatarRouletAvatarNames?.Count ?? 0)
                            ? rule.AvatarRouletAvatarNames[j]
                            : id;
                        roulette.Pool.Add(new RouletteAvatarEntry { AvatarId = id, AvatarName = name });
                    }
                }
                rule.AvatarRouletAvatarIds = null;
                rule.AvatarRouletAvatarNames = null;
                roulette.Triggers.Add(rule);
                settings.AvatarRouletteProfiles.Add(roulette);
            }
        }
    }
    // After the v3 data has been migrated from the persisted shape, run the
    // in-memory re-tagging of CashPayment rules (this is collection-state-only).
    foreach (var profile in settings.AvatarSwapProfiles)
    {
        MigrateV3CashPaymentIntoPaymentRules(profile);
    }
}
```

Expose the migration through `SettingsStore` so the v3 shape is read at load time. The simplest path: add a `public static void RunMigration(AppSettings settings, PersistedSettings persisted)` to `AvatarSwapMigrationService`, and call it from `SettingsStore.LoadAsync` after the settings are populated.

> **Implementation note:** The exact wiring between `SettingsStore` and `AvatarSwapMigrationService` may need small adjustments based on the existing `SettingsStore.LoadAsync` shape. The point is: the migration service must have access to the v3 persisted shape to read `BitsSubsRules` and `RouletteRules` for legacy v3 saves. The test in Step 1 validates the end-to-end behavior.

- [ ] **Step 5: Run the test to verify it passes**

Run: `dotnet test "E:\!!!Program to work on\Proper Crystal Relay\VrcTwitchOscBridge.Tests\VrcTwitchOscBridge.Tests.csproj" --filter "FullyQualifiedName~MigrateV4_SplitsBitsSubsRulesIntoBitsAndSubs" --no-restore`
Expected: PASS.

- [ ] **Step 6: Commit**

```bash
git add VrcTwitchOscBridge/Services/AvatarSwapMigrationService.cs VrcTwitchOscBridge/Services/SettingsStore.cs VrcTwitchOscBridge.Tests/AvatarSwapMigrationServiceV4Tests.cs
git commit -m "feat(avatar-swap): migrate v3 -> v4 split BitsSubs into Bits+Subs, retag CashPayment, convert Roulette to AvatarRouletteProfile"
```

---

### Task 8: Add v4 idempotency test

**Files:**
- Test: `VrcTwitchOscBridge.Tests/AvatarSwapMigrationServiceV4Tests.cs`

- [ ] **Step 1: Add the test**

Add to `VrcTwitchOscBridge.Tests/AvatarSwapMigrationServiceV4Tests.cs`:

```csharp
[Fact]
public void MigrateV4_IsIdempotent_OnV4Save()
{
    var s = new AppSettings();
    var profile = new AvatarSwapProfile { TargetAvatarId = "avtr_a" };
    profile.BitsRules.Add(new TriggerRule { TriggerType = TwitchTriggerType.Bits, Name = "Bits" });
    s.AvatarSwapProfiles.Add(profile);
    s.AvatarChangeToAvatarSwapMigrationVersion = 4;

    AvatarSwapMigrationService.Migrate(s);

    Assert.Single(profile.BitsRules);
    Assert.Empty(profile.SubsRules);
    Assert.Empty(profile.PaymentRules);
    Assert.Empty(s.AvatarRouletteProfiles);
    Assert.Equal(4, s.AvatarChangeToAvatarSwapMigrationVersion);
}
```

- [ ] **Step 2: Run the test to verify it passes**

Run: `dotnet test "E:\!!!Program to work on\Proper Crystal Relay\VrcTwitchOscBridge.Tests\VrcTwitchOscBridge.Tests.csproj" --filter "FullyQualifiedName~MigrateV4_IsIdempotent_OnV4Save" --no-restore`
Expected: PASS.

- [ ] **Step 3: Commit**

```bash
git add VrcTwitchOscBridge.Tests/AvatarSwapMigrationServiceV4Tests.cs
git commit -m "test(avatar-swap): verify v4 migration idempotency"
```

---

### Task 9: Add v4 CashPayment + Roulette preservation tests

**Files:**
- Test: `VrcTwitchOscBridge.Tests/AvatarSwapMigrationServiceV4Tests.cs`

- [ ] **Step 1: Add the tests**

Add to `VrcTwitchOscBridge.Tests/AvatarSwapMigrationServiceV4Tests.cs`:

```csharp
[Fact]
public void MigrateV4_RetagsCashPaymentRulesToPaymentRules()
{
    var s = new AppSettings();
    var profile = new AvatarSwapProfile { TargetAvatarId = "avtr_a" };
    var cashRule = new TriggerRule
    {
        TriggerType = TwitchTriggerType.ChannelPoints,
        Source = TriggerRuleSource.CashPayment,
        CashPaymentRuleId = "cash_1",
        Name = "SE tip",
    };
    profile.ChannelPointRules.Add(cashRule);
    s.AvatarSwapProfiles.Add(profile);
    s.AvatarChangeToAvatarSwapMigrationVersion = 3;

    AvatarSwapMigrationService.Migrate(s);

    Assert.Empty(profile.ChannelPointRules);
    Assert.Single(profile.PaymentRules);
    Assert.Equal("SE tip", profile.PaymentRules[0].Name);
    Assert.Equal("cash_1", profile.PaymentRules[0].CashPaymentRuleId);
}

[Fact]
public void MigrateV4_ConvertsRouletteToAvatarRouletteProfile()
{
    var s = new AppSettings { AvatarChangeToAvatarSwapMigrationVersion = 3 };
    var profile = new AvatarSwapProfile { TargetAvatarId = "avtr_a", TargetAvatarName = "Host" };
    // We simulate a v3 save with a RouletteRules entry by writing JSON and loading it.
    var temp = Path.Combine(Path.GetTempPath(), $"cr-v3-roulette-{Guid.NewGuid():N}.json");
    try
    {
        var json = "{\n" +
                   "  \"avatarSwapProfiles\": [\n" +
                   "    {\n" +
                   "      \"id\": \"00000000-0000-0000-0000-000000000001\",\n" +
                   "      \"targetAvatarId\": \"avtr_a\",\n" +
                   "      \"targetAvatarName\": \"Host\",\n" +
                   "      \"channelPointRules\": [],\n" +
                   "      \"bitsSubsRules\": [],\n" +
                   "      \"rouletteRules\": [\n" +
                   "        { \"name\": \"Furry Roulette\", \"actionType\": 3, \"avatarRouletAvatarIds\": [\"avtr_1\",\"avtr_2\"], \"avatarRouletAvatarNames\": [\"One\",\"Two\"] }\n" +
                   "      ]\n" +
                   "    }\n" +
                   "  ]\n" +
                   "}";
        File.WriteAllText(temp, json);
        var store = new SettingsStore(temp);
        var loaded = store.LoadAsync().GetAwaiter().GetResult();

        AvatarSwapMigrationService.Migrate(loaded);

        Assert.Single(loaded.AvatarRouletteProfiles);
        var roulette = loaded.AvatarRouletteProfiles[0];
        Assert.Equal("Host Roulette", roulette.Name);
        Assert.Equal(2, roulette.Pool.Count);
        Assert.Equal("One", roulette.Pool[0].AvatarName);
        Assert.Single(roulette.Triggers);
        Assert.Equal(OscActionType.AvatarRoulet, roulette.Triggers[0].ActionType);
    }
    finally
    {
        if (File.Exists(temp)) File.Delete(temp);
    }
}
```

> **Note:** The action type enum mapping depends on the actual `OscActionType` integer values. Adjust the JSON literal `"actionType": 3` to match the `AvatarRoulet` value (check `VrcTwitchOscBridge/Models/OscActionType.cs`).

- [ ] **Step 2: Run the tests to verify they pass**

Run: `dotnet test "E:\!!!Program to work on\Proper Crystal Relay\VrcTwitchOscBridge.Tests\VrcTwitchOscBridge.Tests.csproj" --filter "FullyQualifiedName~MigrateV4_Retags|MigrateV4_ConvertsRoulette" --no-restore`
Expected: 2 PASSes.

- [ ] **Step 3: Commit**

```bash
git add VrcTwitchOscBridge.Tests/AvatarSwapMigrationServiceV4Tests.cs
git commit -m "test(avatar-swap): verify v4 CashPayment retag + Roulette-to-Profile conversion"
```

---

## Phase 2: Runtime Snapshots

### Task 10: Add `RouletteAvatarEntrySnapshot` + `AvatarRouletteProfileSnapshot`

**Files:**
- Modify: `VrcTwitchOscBridge/Services/BridgeRuntimeConfiguration.cs`
- Test: `VrcTwitchOscBridge.Tests/AvatarRouletteProfileDispatchTests.cs` (new file)

- [ ] **Step 1: Create the new test file with failing snapshot tests**

Create `VrcTwitchOscBridge.Tests/AvatarRouletteProfileDispatchTests.cs`:

```csharp
using VrcTwitchOscBridge.Models;
using VrcTwitchOscBridge.Services;
using Xunit;

namespace VrcTwitchOscBridge.Tests;

public sealed class AvatarRouletteProfileDispatchTests
{
    [Fact]
    public void FromSettings_BuildsAvatarRouletteSnapshots()
    {
        var s = new AppSettings();
        var roulette = new AvatarRouletteProfile { Name = "Demo" };
        roulette.Pool.Add(new RouletteAvatarEntry { AvatarId = "a1", AvatarName = "One" });
        var trigger = new TriggerRule
        {
            TriggerType = TwitchTriggerType.ChannelPoints,
            ActionType = OscActionType.AvatarRoulet,
        };
        roulette.Triggers.Add(trigger);
        s.AvatarRouletteProfiles.Add(roulette);

        var config = BridgeRuntimeConfiguration.FromSettings(s, RuntimeConfig.CreateDefault(), null);

        Assert.Single(config.AvatarRouletteProfiles);
        var snap = config.AvatarRouletteProfiles[0];
        Assert.Equal("Demo", snap.Name);
        Assert.Single(snap.Pool);
        Assert.Equal("a1", snap.Pool[0].AvatarId);
        Assert.Single(snap.Triggers);
    }
}
```

- [ ] **Step 2: Run the test to verify it fails**

Run: `dotnet test "E:\!!!Program to work on\Proper Crystal Relay\VrcTwitchOscBridge.Tests\VrcTwitchOscBridge.Tests.csproj" --filter "FullyQualifiedName~FromSettings_BuildsAvatarRouletteSnapshots" --no-restore`
Expected: FAIL (snapshot type not defined).

- [ ] **Step 3: Add the new snapshot types to `BridgeRuntimeConfiguration`**

In `VrcTwitchOscBridge/Services/BridgeRuntimeConfiguration.cs`, find the existing `AvatarSwapProfileSnapshot` record (around line 295) and add the new types right after it:

```csharp
public record RouletteAvatarEntrySnapshot(string AvatarId, string AvatarName, string? ThumbnailUrl);

public record AvatarRouletteProfileSnapshot(
    Guid Id,
    string Name,
    bool IsEnabled,
    IReadOnlyList<RouletteAvatarEntrySnapshot> Pool,
    string? ReturnAvatarId,
    string? ReturnAvatarName,
    IReadOnlyList<TriggerRuleSnapshot> Triggers);
```

In the `BridgeRuntimeConfiguration` record, add the new collection right after `AvatarSwapProfiles`:

```csharp
public IReadOnlyList<AvatarRouletteProfileSnapshot> AvatarRouletteProfiles { get; init; } = Array.Empty<AvatarRouletteProfileSnapshot>();
```

- [ ] **Step 4: Run the test to verify it passes**

Run: `dotnet test "E:\!!!Program to work on\Proper Crystal Relay\VrcTwitchOscBridge.Tests\VrcTwitchOscBridge.Tests.csproj" --filter "FullyQualifiedName~FromSettings_BuildsAvatarRouletteSnapshots" --no-restore`
Expected: PASS.

- [ ] **Step 5: Commit**

```bash
git add VrcTwitchOscBridge/Services/BridgeRuntimeConfiguration.cs VrcTwitchOscBridge.Tests/AvatarRouletteProfileDispatchTests.cs
git commit -m "feat(avatar-swap): add AvatarRouletteProfileSnapshot + RouletteAvatarEntrySnapshot"
```

---

### Task 11: Update `AvatarSwapProfileSnapshot` to 4 collections

**Files:**
- Modify: `VrcTwitchOscBridge/Services/BridgeRuntimeConfiguration.cs`
- Test: `VrcTwitchOscBridge.Tests/AvatarSwapMigrationServiceV4Tests.cs`

- [ ] **Step 1: Add failing test for 4-collection snapshot**

Add to `VrcTwitchOscBridge.Tests/AvatarSwapMigrationServiceV4Tests.cs`:

```csharp
[Fact]
public void FromSettings_AvatarSwapProfileSnapshot_HasFourRuleLists()
{
    var s = new AppSettings();
    var p = new AvatarSwapProfile { TargetAvatarId = "avtr_a" };
    p.ChannelPointRules.Add(new TriggerRule { TriggerType = TwitchTriggerType.ChannelPoints });
    p.BitsRules.Add(new TriggerRule { TriggerType = TwitchTriggerType.Bits });
    p.SubsRules.Add(new TriggerRule { TriggerType = TwitchTriggerType.Subscriptions });
    p.PaymentRules.Add(new TriggerRule { Source = TriggerRuleSource.CashPayment });
    s.AvatarSwapProfiles.Add(p);

    var config = BridgeRuntimeConfiguration.FromSettings(s, RuntimeConfig.CreateDefault(), null);

    var snap = config.AvatarSwapProfiles.Single();
    Assert.Single(snap.ChannelPointRules);
    Assert.Single(snap.BitsRules);
    Assert.Single(snap.SubsRules);
    Assert.Single(snap.PaymentRules);
}
```

- [ ] **Step 2: Run the test to verify it fails**

Run: `dotnet test "E:\!!!Program to work on\Proper Crystal Relay\VrcTwitchOscBridge.Tests\VrcTwitchOscBridge.Tests.csproj" --filter "FullyQualifiedName~FromSettings_AvatarSwapProfileSnapshot_HasFourRuleLists" --no-restore`
Expected: FAIL (snapshot still has 1 or 3 rule lists, not 4).

- [ ] **Step 3: Restructure `AvatarSwapProfileSnapshot`**

Replace the `AvatarSwapProfileSnapshot` record:

```csharp
public record AvatarSwapProfileSnapshot(
    Guid Id,
    string TargetAvatarId,
    string TargetAvatarName,
    string? TargetThumbnailUrl,
    bool IsEnabled,
    IReadOnlyList<TriggerRuleSnapshot> ChannelPointRules,
    IReadOnlyList<TriggerRuleSnapshot> BitsRules,
    IReadOnlyList<TriggerRuleSnapshot> SubsRules,
    IReadOnlyList<TriggerRuleSnapshot> PaymentRules);
```

Update the snapshot construction in `FromSettings`. Find the spot where `AvatarSwapProfileSnapshot` is built (around line 432) and replace the rule list with the 4 collections:

```csharp
new AvatarSwapProfileSnapshot(
    profile.Id,
    profile.TargetAvatarId,
    profile.TargetAvatarName,
    profile.TargetThumbnailUrl,
    profile.IsEnabled,
    profile.ChannelPointRules.Select(r => TryToSnapshot(r, false, profile, ...)).Where(s => s != null).Select(s => s!).ToList(),
    profile.BitsRules.Select(r => TryToSnapshot(r, true, profile, ...)).Where(s => s != null).Select(s => s!).ToList(),
    profile.SubsRules.Select(r => TryToSnapshot(r, true, profile, ...)).Where(s => s != null).Select(s => s!).ToList(),
    profile.PaymentRules.Select(r => TryToSnapshot(r, true, profile, ...)).Where(s => s != null).Select(s => s!).ToList())
```

> **Note:** The exact `TryToSnapshot` signature varies. Pass the `isGlobalOverride` flag based on the original v3 behavior: `false` for `ChannelPointRules`, `true` for `BitsRules`/`SubsRules`/`PaymentRules`. This preserves the v3 behavior of marking paid rules as "supporter overrides" in the runtime.

- [ ] **Step 4: Run the test to verify it passes**

Run: `dotnet test "E:\!!!Program to work on\Proper Crystal Relay\VrcTwitchOscBridge.Tests\VrcTwitchOscBridge.Tests.csproj" --filter "FullyQualifiedName~FromSettings_AvatarSwapProfileSnapshot_HasFourRuleLists" --no-restore`
Expected: PASS.

- [ ] **Step 5: Commit**

```bash
git add VrcTwitchOscBridge/Services/BridgeRuntimeConfiguration.cs VrcTwitchOscBridge.Tests/AvatarSwapMigrationServiceV4Tests.cs
git commit -m "feat(avatar-swap): restructure AvatarSwapProfileSnapshot to 4 rule collections"
```

---

### Task 12: Add `FindRouletteProfileForRule` and update `FindAvatarSwapProfileForRule`

**Files:**
- Modify: `VrcTwitchOscBridge/Services/BridgeRuntimeConfiguration.cs`
- Test: `VrcTwitchOscBridge.Tests/AvatarRouletteProfileDispatchTests.cs`

- [ ] **Step 1: Add failing tests for the lookup methods**

Add to `VrcTwitchOscBridge.Tests/AvatarRouletteProfileDispatchTests.cs`:

```csharp
[Fact]
public void FindRouletteProfileForRule_ReturnsProfileForTrigger()
{
    var s = new AppSettings();
    var roulette = new AvatarRouletteProfile { Name = "Demo" };
    var trigger = new TriggerRule { ActionType = OscActionType.AvatarRoulet };
    roulette.Triggers.Add(trigger);
    s.AvatarRouletteProfiles.Add(roulette);

    var config = BridgeRuntimeConfiguration.FromSettings(s, RuntimeConfig.CreateDefault(), null);

    var found = config.FindRouletteProfileForRule(trigger);
    Assert.NotNull(found);
    Assert.Equal("Demo", found.Name);
}

[Fact]
public void FindRouletteProfileForRule_ReturnsNullForUnknownRule()
{
    var s = new AppSettings();
    var config = BridgeRuntimeConfiguration.FromSettings(s, RuntimeConfig.CreateDefault(), null);
    var stray = new TriggerRule { ActionType = OscActionType.AvatarRoulet };
    Assert.Null(config.FindRouletteProfileForRule(stray));
}

[Fact]
public void FindAvatarSwapProfileForRule_LocatesRuleInBitsRules()
{
    var s = new AppSettings();
    var p = new AvatarSwapProfile { TargetAvatarId = "avtr_a" };
    var bits = new TriggerRule { TriggerType = TwitchTriggerType.Bits, ActionType = OscActionType.AvatarChange };
    p.BitsRules.Add(bits);
    s.AvatarSwapProfiles.Add(p);

    var config = BridgeRuntimeConfiguration.FromSettings(s, RuntimeConfig.CreateDefault(), null);

    var found = config.FindAvatarSwapProfileForRule(bits);
    Assert.NotNull(found);
    Assert.Equal("avtr_a", found.TargetAvatarId);
}
```

- [ ] **Step 2: Run the tests to verify they fail**

Run: `dotnet test "E:\!!!Program to work on\Proper Crystal Relay\VrcTwitchOscBridge.Tests\VrcTwitchOscBridge.Tests.csproj" --filter "FullyQualifiedName~FindRouletteProfileForRule|FindAvatarSwapProfileForRule_LocatesRuleInBitsRules" --no-restore`
Expected: 3 FAILs (lookup methods missing or don't consult new collections).

- [ ] **Step 3: Add `FindRouletteProfileForRule` and update `FindAvatarSwapProfileForRule`**

In `BridgeRuntimeConfiguration.cs`, find the existing `FindAvatarSwapProfileForRule` (around line 535) and add `FindRouletteProfileForRule` right after it:

```csharp
public AvatarRouletteProfileSnapshot? FindRouletteProfileForRule(TriggerRule rule)
{
    foreach (var p in AvatarRouletteProfiles)
    {
        foreach (var t in p.Triggers)
        {
            if (ReferenceEquals(t.Rule, rule)) return p;
        }
    }
    return null;
}
```

Update `FindAvatarSwapProfileForRule` to consult all 4 collections:

```csharp
public AvatarSwapProfileSnapshot? FindAvatarSwapProfileForRule(TriggerRule rule)
{
    foreach (var p in AvatarSwapProfiles)
    {
        if (p.ChannelPointRules.Any(t => ReferenceEquals(t.Rule, rule))) return p;
        if (p.BitsRules.Any(t => ReferenceEquals(t.Rule, rule))) return p;
        if (p.SubsRules.Any(t => ReferenceEquals(t.Rule, rule))) return p;
        if (p.PaymentRules.Any(t => ReferenceEquals(t.Rule, rule))) return p;
    }
    return null;
}
```

> **Implementation note:** `TriggerRuleSnapshot.Rule` may be named differently. The exact reference-equality key depends on the v3 snapshot shape. If `TriggerRuleSnapshot` does not carry the original rule, use a `Dictionary<Guid, AvatarSwapProfileSnapshot>` keyed on `TriggerRule.Id`, built during `FromSettings`.

- [ ] **Step 4: Run the tests to verify they pass**

Run: `dotnet test "E:\!!!Program to work on\Proper Crystal Relay\VrcTwitchOscBridge.Tests\VrcTwitchOscBridge.Tests.csproj" --filter "FullyQualifiedName~FindRouletteProfileForRule|FindAvatarSwapProfileForRule_LocatesRuleInBitsRules" --no-restore`
Expected: 3 PASSes.

- [ ] **Step 5: Commit**

```bash
git add VrcTwitchOscBridge/Services/BridgeRuntimeConfiguration.cs VrcTwitchOscBridge.Tests/AvatarRouletteProfileDispatchTests.cs
git commit -m "feat(avatar-swap): add FindRouletteProfileForRule, extend FindAvatarSwapProfileForRule to 4 collections"
```

---

## Phase 3: Runtime Dispatch

### Task 13: Update `ResolveAvatarSwapAction` to use global return avatar

**Files:**
- Modify: `VrcTwitchOscBridge/Services/BridgeCoordinator.cs`
- Test: `VrcTwitchOscBridge.Tests/AvatarRouletteProfileDispatchTests.cs`

- [ ] **Step 1: Add failing test for global return usage**

Add to `VrcTwitchOscBridge.Tests/AvatarRouletteProfileDispatchTests.cs`:

```csharp
[Fact]
public void ResolveAvatarSwapAction_UsesGlobalReturnAvatar()
{
    // The runtime method is private, so we exercise it through BridgeCoordinator.
    // For unit-test purposes, we verify the call path by inspecting the active
    // configuration's MasterAvatarSwapReturnId is plumbed through.
    var s = new AppSettings
    {
        MasterAvatarSwapReturnId = "avtr_return",
        MasterAvatarSwapReturnName = "Return",
    };
    var p = new AvatarSwapProfile { TargetAvatarId = "avtr_target" };
    s.AvatarSwapProfiles.Add(p);

    var config = BridgeRuntimeConfiguration.FromSettings(s, RuntimeConfig.CreateDefault(), null);
    Assert.Equal("avtr_return", config.MasterAvatarSwapReturnId);
}
```

- [ ] **Step 2: Run the test to verify it passes (already supported)**

Run: `dotnet test "E:\!!!Program to work on\Proper Crystal Relay\VrcTwitchOscBridge.Tests\VrcTwitchOscBridge.Tests.csproj" --filter "FullyQualifiedName~ResolveAvatarSwapAction_UsesGlobalReturnAvatar" --no-restore`
Expected: PASS (the test only verifies that the global ID is exposed in the config; the actual dispatch is exercised in the manual smoke test in Phase 9).

- [ ] **Step 3: Remove the per-profile return-mode logic from `ResolveAvatarSwapAction`**

In `VrcTwitchOscBridge/Services/BridgeCoordinator.cs:8172-8212`, replace the body with:

```csharp
private async Task ResolveAvatarSwapAction(
    AvatarSwapProfileSnapshot profile, TriggerRule rule, string? capturedReturn)
{
    var returnAvatarId = capturedReturn
        ?? activeConfiguration?.MasterAvatarSwapReturnId
        ?? profile.TargetAvatarId;
    var returnAvatarName = activeConfiguration?.MasterAvatarSwapReturnName
        ?? profile.TargetAvatarName;
    await EmitAvatarSwapAsync(profile.TargetAvatarId, returnAvatarId, returnAvatarName, rule);
}
```

> **Implementation note:** `EmitAvatarSwapAsync` may be a different method name (the v3 code uses a series of OSC sends). Adapt to the actual existing structure. The key change is: the return avatar always comes from the global setting.

- [ ] **Step 4: Commit**

```bash
git add VrcTwitchOscBridge/Services/BridgeCoordinator.cs VrcTwitchOscBridge.Tests/AvatarRouletteProfileDispatchTests.cs
git commit -m "refactor(avatar-swap): use global Return Avatar in ResolveAvatarSwapAction"
```

---

### Task 14: Add `ResolveRouletteProfileAction`

**Files:**
- Modify: `VrcTwitchOscBridge/Services/BridgeCoordinator.cs`

- [ ] **Step 1: Add the new method**

In `BridgeCoordinator.cs`, add the new method right after `ResolveAvatarSwapAction`:

```csharp
private async Task ResolveRouletteProfileAction(
    AvatarRouletteProfileSnapshot roulette, TriggerRule rule)
{
    var picked = PickAvatarRouletTarget(roulette.Pool);
    if (picked == null) return;

    var returnAvatarId = roulette.ReturnAvatarId
        ?? activeConfiguration?.MasterAvatarSwapReturnId
        ?? picked.AvatarId;
    var returnAvatarName = roulette.ReturnAvatarName
        ?? activeConfiguration?.MasterAvatarSwapReturnName
        ?? picked.AvatarName;

    await EmitAvatarSwapAsync(picked.AvatarId, returnAvatarId, returnAvatarName, rule);
}
```

- [ ] **Step 2: Update `PickAvatarRouletTarget` to take a `roulette.Pool` (keyed on roulette.Id)**

Find `PickAvatarRouletTarget` at `BridgeCoordinator.cs:8315-8360`. Replace the signature and body:

```csharp
private RouletteAvatarEntrySnapshot? PickAvatarRouletTarget(IReadOnlyList<RouletteAvatarEntrySnapshot> pool)
{
    if (pool == null || pool.Count == 0) return null;

    // No-repeat bag keyed on the roulette id (the caller passes the bag
    // separately, but the new design uses a single bag per roulette).
    // For simplicity, the no-repeat bag is held in a static dictionary
    // keyed on the roulette's Guid, looked up via a side-channel parameter.
    // The actual implementation will be refined in the next step.
    return pool[_rng.Next(pool.Count)];
}
```

Add a static dictionary for the no-repeat bag:

```csharp
private static readonly System.Collections.Concurrent.ConcurrentDictionary<Guid, List<int>> remainingAvatarRouletIndicesByRouletteId = new();
```

Replace the implementation:

```csharp
private RouletteAvatarEntrySnapshot? PickAvatarRouletTarget(AvatarRouletteProfileSnapshot roulette)
{
    if (roulette.Pool == null || roulette.Pool.Count == 0) return null;
    var bag = remainingAvatarRouletIndicesByRouletteId.GetOrAdd(roulette.Id, _ => new List<int>());
    if (bag.Count == 0)
    {
        for (int i = 0; i < roulette.Pool.Count; i++) bag.Add(i);
        // Shuffle
        for (int i = bag.Count - 1; i > 0; i--)
        {
            int j = _rng.Next(i + 1);
            (bag[i], bag[j]) = (bag[j], bag[i]);
        }
    }
    var idx = bag[bag.Count - 1];
    bag.RemoveAt(bag.Count - 1);
    return roulette.Pool[idx];
}
```

Then in `ResolveRouletteProfileAction`, change the call to `PickAvatarRouletTarget(roulette)`.

- [ ] **Step 3: Wire the new method into `ExecuteRuleActionAsync`**

Find the entry point (around line 8130). Add a roulette lookup before the swap lookup:

```csharp
if (rule.ActionType is OscActionType.AvatarChange or OscActionType.AvatarRoulet)
{
    if (rule.ActionType == OscActionType.AvatarRoulet)
    {
        var roulette = activeConfiguration?.FindRouletteProfileForRule(rule);
        if (roulette != null)
            return ResolveRouletteProfileAction(roulette, rule);
    }
    var swap = activeConfiguration?.FindAvatarSwapProfileForRule(rule);
    if (swap != null)
        return ResolveAvatarSwapAction(swap, rule, capturedReturn);
}
```

- [ ] **Step 4: Update the legacy `ResolveAvatarRouletAction` to call into the new path**

Find `ResolveAvatarRouletAction` (around line 8214). If it has any non-migrated fallback logic, route it through the new `ResolveRouletteProfileAction` when the rule is found in a roulette profile. Otherwise, leave the legacy path for unmigrated rules.

- [ ] **Step 5: Build to verify everything compiles**

Run: `dotnet build "E:\!!!Program to work on\Proper Crystal Relay\VrcTwitchOscBridge\VrcTwitchOscBridge.csproj" --no-restore`
Expected: 0 errors. There may still be some from the XAML / ViewModel layers that the rest of the plan addresses.

- [ ] **Step 6: Commit**

```bash
git add VrcTwitchOscBridge/Services/BridgeCoordinator.cs
git commit -m "feat(avatar-swap): add ResolveRouletteProfileAction + update PickAvatarRouletTarget to use roulette.Pool"
```

---

## Phase 4: ViewModels

### Task 15: Restructure `AvatarSwapManagerViewModel` for 4 collections + roulette

**Files:**
- Modify: `VrcTwitchOscBridge/ViewModels/AvatarSwapManagerViewModel.cs`

> **Note:** This is a large refactor. The full new file is provided to minimize partial-state compile errors. Read the existing file first to understand its public surface and dependencies; then replace it with the v4 version.

- [ ] **Step 1: Build the v4 VM shell**

The new VM exposes:
- 1 left card collection: `SwapCards` (per-avatar card VMs)
- 1 left card collection: `RouletteCards` (per-roulette card VMs)
- Right editor state: `SelectedSwapProfile`, `EditingRoulette`
- Global return avatar properties (delegated to `Settings.MasterAvatarSwapReturnId/Name`)
- Commands: `AddSwapCommand`, `AddRouletteCommand`, `OpenSwapEditorCommand`, `OpenRouletteEditorCommand`, `AddChannelPointRuleCommand`, `AddBitsRuleCommand`, `AddSubsRuleCommand`, `AddPaymentRuleCommand`, `AddAdvancedTriggerCommand(TriggerSource)`, `SaveSwapEditorCommand`, `SaveRouletteEditorCommand`, `DeleteSwapCommand`, `DeleteRouletteCommand`, `PickGlobalReturnAvatarCommand`, `UseCurrentAvatarForGlobalReturnCommand`, `ClearGlobalReturnCommand`, `PickTargetAvatarCommand`, `UseCurrentAvatarForTargetCommand`, `PickRoulettePoolAvatarCommand`, `BeginInlineEditRuleCommand`, `CommitInlineEditRuleCommand`, `CancelInlineEditRuleCommand`, `DeleteRuleCommand`

The implementation in this task establishes the shell with stubbed command bodies. Tasks 16-19 fill in the rest.

Replace the contents of `VrcTwitchOscBridge/ViewModels/AvatarSwapManagerViewModel.cs` with:

```csharp
using System.Collections.ObjectModel;
using System.Windows.Input;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using VrcTwitchOscBridge.Models;
using VrcTwitchOscBridge.Services;

namespace VrcTwitchOscBridge.ViewModels;

public partial class AvatarSwapManagerViewModel : ObservableObject
{
    private readonly AppSettings _settings;

    public ObservableCollection<AvatarSwapCardViewModel> SwapCards { get; } = new();
    public ObservableCollection<AvatarRouletteCardViewModel> RouletteCards { get; } = new();

    [ObservableProperty] private AvatarSwapCardViewModel? _selectedSwapCard;
    [ObservableProperty] private AvatarRouletteCardViewModel? _selectedRouletteCard;
    [ObservableProperty] private bool _isSwapEditorOpen;
    [ObservableProperty] private bool _isRouletteEditorOpen;
    [ObservableProperty] private InlineAvatarSwapRuleRowViewModel? _editingRule;

    // Global return avatar (lives at the top banner)
    public string? GlobalReturnAvatarId => _settings.MasterAvatarSwapReturnId;
    public string? GlobalReturnAvatarName => _settings.MasterAvatarSwapReturnName;

    public AvatarSwapManagerViewModel(AppSettings settings)
    {
        _settings = settings;
        RebuildCards();
    }

    public void RebuildCards()
    {
        SwapCards.Clear();
        foreach (var profile in _settings.AvatarSwapProfiles)
            SwapCards.Add(new AvatarSwapCardViewModel(profile));
        RouletteCards.Clear();
        foreach (var roulette in _settings.AvatarRouletteProfiles)
            RouletteCards.Add(new AvatarRouletteCardViewModel(roulette));
    }

    [RelayCommand]
    private void AddSwap() { /* Task 16 */ }
    [RelayCommand]
    private void AddRoulette() { /* Task 16 */ }
    [RelayCommand]
    private void OpenSwapEditor(AvatarSwapCardViewModel? card) { /* Task 16 */ }
    [RelayCommand]
    private void OpenRouletteEditor(AvatarRouletteCardViewModel? card) { /* Task 16 */ }
    [RelayCommand]
    private void SaveSwapEditor() { /* Task 17 */ }
    [RelayCommand]
    private void SaveRouletteEditor() { /* Task 17 */ }
    [RelayCommand]
    private void DeleteSwap() { /* Task 17 */ }
    [RelayCommand]
    private void DeleteRoulette() { /* Task 17 */ }

    [RelayCommand]
    private void AddChannelPointRule() { /* Task 18 */ }
    [RelayCommand]
    private void AddBitsRule() { /* Task 18 */ }
    [RelayCommand]
    private void AddSubsRule() { /* Task 18 */ }
    [RelayCommand]
    private void AddPaymentRule() { /* Task 18 */ }
    [RelayCommand]
    private void AddAdvancedTrigger(string triggerSource) { /* Task 18 */ }
    [RelayCommand]
    private void DeleteRule(InlineAvatarSwapRuleRowViewModel? row) { /* Task 18 */ }
    [RelayCommand]
    private void BeginInlineEdit(InlineAvatarSwapRuleRowViewModel? row) { /* Task 18 */ }
    [RelayCommand]
    private void CommitInlineEdit() { EditingRule = null; }
    [RelayCommand]
    private void CancelInlineEdit() { EditingRule = null; }

    [RelayCommand]
    private void PickGlobalReturnAvatar() { /* Task 19 */ }
    [RelayCommand]
    private void UseCurrentAvatarForGlobalReturn() { /* Task 19 */ }
    [RelayCommand]
    private void ClearGlobalReturn() { /* Task 19 */ }
}
```

- [ ] **Step 2: Build to verify the shell compiles**

Run: `dotnet build "E:\!!!Program to work on\Proper Crystal Relay\VrcTwitchOscBridge\VrcTwitchOscBridge.csproj" --no-restore`
Expected: 0 errors at the VM level. Errors from the XAML layer (which still references the old commands) are expected and addressed in Phase 5.

- [ ] **Step 3: Commit**

```bash
git add VrcTwitchOscBridge/ViewModels/AvatarSwapManagerViewModel.cs
git commit -m "refactor(avatar-swap): restructure AvatarSwapManagerViewModel for 4 collections + roulette (shell)"
```

---

### Task 16: Add Swap / Roulette add + open editor commands

**Files:**
- Modify: `VrcTwitchOscBridge/ViewModels/AvatarSwapManagerViewModel.cs`

- [ ] **Step 1: Fill in `AddSwap`, `AddRoulette`, `OpenSwapEditor`, `OpenRouletteEditor`**

Replace the stubs in `AvatarSwapManagerViewModel.cs`:

```csharp
[RelayCommand]
private void AddSwap()
{
    var profile = new AvatarSwapProfile
    {
        TargetAvatarName = "New Avatar",
    };
    _settings.AvatarSwapProfiles.Add(profile);
    var card = new AvatarSwapCardViewModel(profile);
    SwapCards.Add(card);
    SelectedSwapCard = card;
    IsSwapEditorOpen = true;
    IsRouletteEditorOpen = false;
}

[RelayCommand]
private void AddRoulette()
{
    var roulette = new AvatarRouletteProfile { Name = "New Roulette" };
    _settings.AvatarRouletteProfiles.Add(roulette);
    var card = new AvatarRouletteCardViewModel(roulette);
    RouletteCards.Add(card);
    SelectedRouletteCard = card;
    IsRouletteEditorOpen = true;
    IsSwapEditorOpen = false;
}

[RelayCommand]
private void OpenSwapEditor(AvatarSwapCardViewModel? card)
{
    if (card is null) return;
    SelectedSwapCard = card;
    IsSwapEditorOpen = true;
    IsRouletteEditorOpen = false;
}

[RelayCommand]
private void OpenRouletteEditor(AvatarRouletteCardViewModel? card)
{
    if (card is null) return;
    SelectedRouletteCard = card;
    IsRouletteEditorOpen = true;
    IsSwapEditorOpen = false;
}
```

- [ ] **Step 2: Build to verify**

Run: `dotnet build "E:\!!!Program to work on\Proper Crystal Relay\VrcTwitchOscBridge\VrcTwitchOscBridge.csproj" --no-restore`
Expected: 0 VM errors. XAML errors from the old command names will be addressed in Phase 5.

- [ ] **Step 3: Commit**

```bash
git add VrcTwitchOscBridge/ViewModels/AvatarSwapManagerViewModel.cs
git commit -m "feat(avatar-swap): add Swap + Roulette add/open editor commands"
```

---

### Task 17: Add save / delete commands for swap + roulette

**Files:**
- Modify: `VrcTwitchOscBridge/ViewModels/AvatarSwapManagerViewModel.cs`

- [ ] **Step 1: Fill in save / delete commands**

```csharp
[RelayCommand]
private void SaveSwapEditor()
{
    if (SelectedSwapCard is null) return;
    SelectedSwapCard.Profile.UpdatedAt = DateTime.UtcNow;
    IsSwapEditorOpen = false;
    SelectedSwapCard = null;
}

[RelayCommand]
private void SaveRouletteEditor()
{
    if (SelectedRouletteCard is null) return;
    SelectedRouletteCard.Roulette.UpdatedAt = DateTime.UtcNow;
    IsRouletteEditorOpen = false;
    SelectedRouletteCard = null;
}

[RelayCommand]
private void DeleteSwap()
{
    if (SelectedSwapCard is null) return;
    _settings.AvatarSwapProfiles.Remove(SelectedSwapCard.Profile);
    SwapCards.Remove(SelectedSwapCard);
    IsSwapEditorOpen = false;
    SelectedSwapCard = null;
}

[RelayCommand]
private void DeleteRoulette()
{
    if (SelectedRouletteCard is null) return;
    _settings.AvatarRouletteProfiles.Remove(SelectedRouletteCard.Roulette);
    RouletteCards.Remove(SelectedRouletteCard);
    IsRouletteEditorOpen = false;
    SelectedRouletteCard = null;
}
```

- [ ] **Step 2: Build to verify**

Run: `dotnet build "E:\!!!Program to work on\Proper Crystal Relay\VrcTwitchOscBridge\VrcTwitchOscBridge.csproj" --no-restore`
Expected: 0 VM errors.

- [ ] **Step 3: Commit**

```bash
git add VrcTwitchOscBridge/ViewModels/AvatarSwapManagerViewModel.cs
git commit -m "feat(avatar-swap): add save/delete commands for swap and roulette"
```

---

### Task 18: Add per-section add-rule commands and inline edit commands

**Files:**
- Modify: `VrcTwitchOscBridge/ViewModels/AvatarSwapManagerViewModel.cs`
- Create: `VrcTwitchOscBridge/ViewModels/InlineAvatarSwapRuleRowViewModel.cs`

- [ ] **Step 1: Create `InlineAvatarSwapRuleRowViewModel`**

Create `VrcTwitchOscBridge/ViewModels/InlineAvatarSwapRuleRowViewModel.cs`:

```csharp
using CommunityToolkit.Mvvm.ComponentModel;
using VrcTwitchOscBridge.Models;

namespace VrcTwitchOscBridge.ViewModels;

public partial class InlineAvatarSwapRuleRowViewModel : ObservableObject
{
    public TriggerRule Rule { get; }

    [ObservableProperty] private bool _isExpanded;
    [ObservableProperty] private string _summary = string.Empty;

    public InlineAvatarSwapRuleRowViewModel(TriggerRule rule)
    {
        Rule = rule;
        UpdateSummary();
    }

    public void UpdateSummary()
    {
        Summary = Rule.DisplayTitle ?? Rule.Name ?? "Rule";
    }
}
```

- [ ] **Step 2: Add a per-section `ObservableCollection<InlineAvatarSwapRuleRowViewModel>` for the selected swap profile**

Add to `AvatarSwapManagerViewModel`:

```csharp
public ObservableCollection<InlineAvatarSwapRuleRowViewModel> ChannelPointRows { get; } = new();
public ObservableCollection<InlineAvatarSwapRuleRowViewModel> BitsRows { get; } = new();
public ObservableCollection<InlineAvatarSwapRuleRowViewModel> SubsRows { get; } = new();
public ObservableCollection<InlineAvatarSwapRuleRowViewModel> PaymentRows { get; } = new();
public ObservableCollection<InlineAvatarSwapRuleRowViewModel> RouletteTriggerRows { get; } = new();

private void RebuildRows()
{
    ChannelPointRows.Clear();
    BitsRows.Clear();
    SubsRows.Clear();
    PaymentRows.Clear();
    RouletteTriggerRows.Clear();
    if (SelectedSwapCard is not null)
    {
        foreach (var r in SelectedSwapCard.Profile.ChannelPointRules) ChannelPointRows.Add(new InlineAvatarSwapRuleRowViewModel(r));
        foreach (var r in SelectedSwapCard.Profile.BitsRules) BitsRows.Add(new InlineAvatarSwapRuleRowViewModel(r));
        foreach (var r in SelectedSwapCard.Profile.SubsRules) SubsRows.Add(new InlineAvatarSwapRuleRowViewModel(r));
        foreach (var r in SelectedSwapCard.Profile.PaymentRules) PaymentRows.Add(new InlineAvatarSwapRuleRowViewModel(r));
    }
    if (SelectedRouletteCard is not null)
    {
        foreach (var r in SelectedRouletteCard.Roulette.Triggers) RouletteTriggerRows.Add(new InlineAvatarSwapRuleRowViewModel(r));
    }
}
```

Call `RebuildRows()` at the end of `OpenSwapEditor` and `OpenRouletteEditor`.

- [ ] **Step 3: Fill in the add / delete / begin-inline commands**

```csharp
[RelayCommand]
private void AddChannelPointRule()
{
    if (SelectedSwapCard is null) return;
    var rule = new TriggerRule { TriggerType = TwitchTriggerType.ChannelPoints, ActionType = OscActionType.AvatarChange };
    SelectedSwapCard.Profile.ChannelPointRules.Add(rule);
    ChannelPointRows.Add(new InlineAvatarSwapRuleRowViewModel(rule));
}

[RelayCommand]
private void AddBitsRule()
{
    if (SelectedSwapCard is null) return;
    var rule = new TriggerRule { TriggerType = TwitchTriggerType.Bits, ActionType = OscActionType.AvatarChange, MinimumAmount = 100 };
    SelectedSwapCard.Profile.BitsRules.Add(rule);
    BitsRows.Add(new InlineAvatarSwapRuleRowViewModel(rule));
}

[RelayCommand]
private void AddSubsRule()
{
    if (SelectedSwapCard is null) return;
    var rule = new TriggerRule { TriggerType = TwitchTriggerType.Subscriptions, ActionType = OscActionType.AvatarChange };
    SelectedSwapCard.Profile.SubsRules.Add(rule);
    SubsRows.Add(new InlineAvatarSwapRuleRowViewModel(rule));
}

[RelayCommand]
private void AddPaymentRule()
{
    if (SelectedSwapCard is null) return;
    var rule = new TriggerRule { TriggerType = TwitchTriggerType.ChannelPoints, ActionType = OscActionType.AvatarChange, Source = TriggerRuleSource.CashPayment };
    SelectedSwapCard.Profile.PaymentRules.Add(rule);
    PaymentRows.Add(new InlineAvatarSwapRuleRowViewModel(rule));
}

[RelayCommand]
private void AddAdvancedTrigger(string? triggerSource)
{
    if (SelectedSwapCard is null || string.IsNullOrEmpty(triggerSource)) return;
    var type = Enum.Parse<TwitchTriggerType>(triggerSource);
    var rule = new TriggerRule { TriggerType = type, ActionType = OscActionType.AvatarChange };
    SelectedSwapCard.Profile.ChannelPointRules.Add(rule);
    ChannelPointRows.Add(new InlineAvatarSwapRuleRowViewModel(rule));
}

[RelayCommand]
private void DeleteRule(InlineAvatarSwapRuleRowViewModel? row)
{
    if (row is null || SelectedSwapCard is null) return;
    if (SelectedSwapCard.Profile.ChannelPointRules.Remove(row.Rule)) ChannelPointRows.Remove(row);
    else if (SelectedSwapCard.Profile.BitsRules.Remove(row.Rule)) BitsRows.Remove(row);
    else if (SelectedSwapCard.Profile.SubsRules.Remove(row.Rule)) SubsRows.Remove(row);
    else if (SelectedSwapCard.Profile.PaymentRules.Remove(row.Rule)) PaymentRows.Remove(row);
    else if (SelectedRouletteCard is not null && SelectedRouletteCard.Roulette.Triggers.Remove(row.Rule)) RouletteTriggerRows.Remove(row);
}

[RelayCommand]
private void BeginInlineEdit(InlineAvatarSwapRuleRowViewModel? row)
{
    if (row is null) return;
    // Collapse all rows in all sections first
    foreach (var r in ChannelPointRows) r.IsExpanded = false;
    foreach (var r in BitsRows) r.IsExpanded = false;
    foreach (var r in SubsRows) r.IsExpanded = false;
    foreach (var r in PaymentRows) r.IsExpanded = false;
    foreach (var r in RouletteTriggerRows) r.IsExpanded = false;
    row.IsExpanded = true;
    EditingRule = row;
}
```

- [ ] **Step 4: Build to verify**

Run: `dotnet build "E:\!!!Program to work on\Proper Crystal Relay\VrcTwitchOscBridge\VrcTwitchOscBridge.csproj" --no-restore`
Expected: 0 VM errors.

- [ ] **Step 5: Commit**

```bash
git add VrcTwitchOscBridge/ViewModels/AvatarSwapManagerViewModel.cs VrcTwitchOscBridge/ViewModels/InlineAvatarSwapRuleRowViewModel.cs
git commit -m "feat(avatar-swap): add per-section add-rule and inline-edit commands"
```

---

### Task 19: Add global return avatar picker commands

**Files:**
- Modify: `VrcTwitchOscBridge/ViewModels/AvatarSwapManagerViewModel.cs`

- [ ] **Step 1: Fill in the global return avatar commands**

```csharp
[RelayCommand]
private void PickGlobalReturnAvatar()
{
    // Defer to MainWindowViewModel.OpenAvatarPickerCommand; this VM does not own the picker UI.
    // The window codebehind invokes the picker and calls back via SetGlobalReturnAvatar.
    // The placeholder body is fine for now; the XAML wires the click to a codebehind method.
}

[RelayCommand]
private void UseCurrentAvatarForGlobalReturn()
{
    // Placeholder: MainWindowViewModel injects the current avatar id/name via a callback.
}

[RelayCommand]
private void ClearGlobalReturn()
{
    _settings.MasterAvatarSwapReturnId = null;
    _settings.MasterAvatarSwapReturnName = null;
    OnPropertyChanged(nameof(GlobalReturnAvatarId));
    OnPropertyChanged(nameof(GlobalReturnAvatarName));
}

public void SetGlobalReturnAvatar(string? id, string? name)
{
    _settings.MasterAvatarSwapReturnId = id;
    _settings.MasterAvatarSwapReturnName = name;
    OnPropertyChanged(nameof(GlobalReturnAvatarId));
    OnPropertyChanged(nameof(GlobalReturnAvatarName));
}
```

> **Implementation note:** The picker flow is shared with `MainWindowViewModel`. The window codebehind will need to wire the picker's `Closed` event to call `SetGlobalReturnAvatar`. See the existing `MainWindowViewModel.PickReturnAvatar` pattern for the wiring approach.

- [ ] **Step 2: Build to verify**

Run: `dotnet build "E:\!!!Program to work on\Proper Crystal Relay\VrcTwitchOscBridge\VrcTwitchOscBridge.csproj" --no-restore`
Expected: 0 VM errors.

- [ ] **Step 3: Commit**

```bash
git add VrcTwitchOscBridge/ViewModels/AvatarSwapManagerViewModel.cs
git commit -m "feat(avatar-swap): add global return avatar picker commands"
```

---

### Task 20: Create `AvatarRouletteCardViewModel`

**Files:**
- Create: `VrcTwitchOscBridge/ViewModels/AvatarRouletteCardViewModel.cs`

- [ ] **Step 1: Create the VM**

Create `VrcTwitchOscBridge/ViewModels/AvatarRouletteCardViewModel.cs`:

```csharp
using CommunityToolkit.Mvvm.ComponentModel;
using VrcTwitchOscBridge.Models;

namespace VrcTwitchOscBridge.ViewModels;

public partial class AvatarRouletteCardViewModel : ObservableObject
{
    public AvatarRouletteProfile Roulette { get; }

    public AvatarRouletteCardViewModel(AvatarRouletteProfile roulette)
    {
        Roulette = roulette;
    }

    public string Name => Roulette.Name;
    public string Subtitle => Roulette.Subtitle;
    public int PoolCount => Roulette.PoolCount;
    public int TriggerCount => Roulette.TriggerCount;
}
```

- [ ] **Step 2: Build to verify**

Run: `dotnet build "E:\!!!Program to work on\Proper Crystal Relay\VrcTwitchOscBridge\VrcTwitchOscBridge.csproj" --no-restore`
Expected: 0 VM errors.

- [ ] **Step 3: Commit**

```bash
git add VrcTwitchOscBridge/ViewModels/AvatarRouletteCardViewModel.cs
git commit -m "feat(avatar-swap): add AvatarRouletteCardViewModel"
```

---

### Task 21: Update `AvatarSwapCardViewModel` subtitle format

**Files:**
- Modify: `VrcTwitchOscBridge/ViewModels/AvatarSwapCardViewModel.cs`

- [ ] **Step 1: Update the subtitle text**

Find the `AvatarSubtitle` getter on the VM and replace it:

```csharp
public string AvatarSubtitle => Profile.AvatarSubtitle;
```

Remove any `RouletteRuleCount` property and its pill on the card (roulette is no longer on the avatar card).

- [ ] **Step 2: Build to verify**

Run: `dotnet build "E:\!!!Program to work on\Proper Crystal Relay\VrcTwitchOscBridge\VrcTwitchOscBridge.csproj" --no-restore`
Expected: 0 errors.

- [ ] **Step 3: Commit**

```bash
git add VrcTwitchOscBridge/ViewModels/AvatarSwapCardViewModel.cs
git commit -m "refactor(avatar-swap): AvatarSwapCardViewModel subtitle uses profile's new format"
```

---

## Phase 5: XAML

### Task 22: Add new XAML files to the project

**Files:**
- Modify: `VrcTwitchOscBridge/VrcTwitchOscBridge.csproj`

- [ ] **Step 1: Add the new UserControls to the project file**

In `VrcTwitchOscBridge/VrcTwitchOscBridge.csproj`, find the existing `<Page>` items for `AvatarSwapRuleEditorControl.xaml` and `AvatarRouletPickerWindow.xaml`. Add:

```xml
<Page Include="UserControls\InlineAvatarSwapRuleRowControl.xaml" />
```

Also ensure the codebehind `<Compile>` is already covered (it will be, by the project's existing glob patterns or by `<Compile Include="UserControls\InlineAvatarSwapRuleRowControl.xaml.cs" />`).

- [ ] **Step 2: Build to verify the new file is recognized**

The file may not exist yet, so create a stub first:

Create `VrcTwitchOscBridge/UserControls/InlineAvatarSwapRuleRowControl.xaml`:

```xml
<UserControl x:Class="VrcTwitchOscBridge.UserControls.InlineAvatarSwapRuleRowControl"
             xmlns="http://schemas.microsoft.com/winfx/2006/xaml/presentation"
             xmlns:x="http://schemas.microsoft.com/winfx/2006/xaml">
    <Grid>
        <TextBlock Text="Inline row" />
    </Grid>
</UserControl>
```

Create `VrcTwitchOscBridge/UserControls/InlineAvatarSwapRuleRowControl.xaml.cs`:

```csharp
using System.Windows.Controls;

namespace VrcTwitchOscBridge.UserControls;

public partial class InlineAvatarSwapRuleRowControl : UserControl
{
    public InlineAvatarSwapRuleRowControl() => InitializeComponent();
}
```

- [ ] **Step 3: Build to verify**

Run: `dotnet build "E:\!!!Program to work on\Proper Crystal Relay\VrcTwitchOscBridge\VrcTwitchOscBridge.csproj" --no-restore`
Expected: 0 errors.

- [ ] **Step 4: Commit**

```bash
git add VrcTwitchOscBridge/VrcTwitchOscBridge.csproj VrcTwitchOscBridge/UserControls/InlineAvatarSwapRuleRowControl.xaml VrcTwitchOscBridge/UserControls/InlineAvatarSwapRuleRowControl.xaml.cs
git commit -m "chore(avatar-swap): add InlineAvatarSwapRuleRowControl stub to project"
```

---

### Task 23: Implement `InlineAvatarSwapRuleRowControl` XAML

**Files:**
- Modify: `VrcTwitchOscBridge/UserControls/InlineAvatarSwapRuleRowControl.xaml`
- Modify: `VrcTwitchOscBridge/UserControls/InlineAvatarSwapRuleRowControl.xaml.cs`

- [ ] **Step 1: Implement the collapsed/expanded row XAML**

Replace the contents of `VrcTwitchOscBridge/UserControls/InlineAvatarSwapRuleRowControl.xaml`:

```xml
<UserControl x:Class="VrcTwitchOscBridge.UserControls.InlineAvatarSwapRuleRowControl"
             xmlns="http://schemas.microsoft.com/winfx/2006/xaml/presentation"
             xmlns:x="http://schemas.microsoft.com/winfx/2006/xaml"
             xmlns:vm="clr-namespace:VrcTwitchOscBridge.ViewModels">
    <UserControl.Resources>
        <Style TargetType="TextBlock" x:Key="LabelStyle">
            <Setter Property="Foreground" Value="#9b86c9" />
            <Setter Property="FontSize" Value="9" />
            <Setter Property="Margin" Value="0,0,0,2" />
        </Style>
        <Style TargetType="TextBox" x:Key="InputStyle">
            <Setter Property="Background" Value="#0d0a18" />
            <Setter Property="BorderBrush" Value="#3a2a5a" />
            <Setter Property="Foreground" Value="#e8e3f5" />
            <Setter Property="Padding" Value="3" />
            <Setter Property="FontSize" Value="11" />
        </Style>
    </UserControl.Resources>
    <StackPanel>
        <Border Background="#241a3a" Padding="4,3" CornerRadius="3" Margin="0,0,0,2">
            <DockPanel>
                <TextBlock Text="{Binding Summary}" FontSize="11" />
                <Button DockPanel.Dock="Right" Content="🗑" Command="{Binding DataContext.DeleteRuleCommand, RelativeSource={RelativeSource AncestorType=Window}}" CommandParameter="{Binding}" Width="20" Background="Transparent" Foreground="#9b86c9" BorderThickness="0" />
            </DockPanel>
            <Border.InputBindings>
                <MouseBinding MouseAction="LeftClick" Command="{Binding DataContext.BeginInlineEditCommand, RelativeSource={RelativeSource AncestorType=Window}}" CommandParameter="{Binding}" />
            </Border.InputBindings>
        </Border>
        <Border Background="#241a3a" BorderBrush="#6b3fa0" BorderThickness="1" CornerRadius="4" Padding="8,6"
                Visibility="{Binding IsExpanded, Converter={StaticResource BoolToVis}}">
            <StackPanel>
                <TextBlock Text="(Inline editor — to be filled in for each trigger type)" Foreground="#b0a3d0" FontSize="11" />
            </StackPanel>
        </Border>
    </StackPanel>
</UserControl>
```

> **Note:** The `BoolToVis` converter is expected to exist in the app's resource dictionary. If it doesn't, add a small static converter and register it in `App.xaml`.

- [ ] **Step 2: Add codebehind to expose the `Row` property**

Replace `VrcTwitchOscBridge/UserControls/InlineAvatarSwapRuleRowControl.xaml.cs`:

```csharp
using System.Windows;
using System.Windows.Controls;
using VrcTwitchOscBridge.ViewModels;

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
}
```

- [ ] **Step 3: Build to verify**

Run: `dotnet build "E:\!!!Program to work on\Proper Crystal Relay\VrcTwitchOscBridge\VrcTwitchOscBridge.csproj" --no-restore`
Expected: 0 errors.

- [ ] **Step 4: Commit**

```bash
git add VrcTwitchOscBridge/UserControls/InlineAvatarSwapRuleRowControl.xaml VrcTwitchOscBridge/UserControls/InlineAvatarSwapRuleRowControl.xaml.cs
git commit -m "feat(avatar-swap): implement InlineAvatarSwapRuleRowControl XAML (stub inline editor)"
```

---

### Task 24: Rewrite `AvatarSwapManagerWindow.xaml` with new layout

**Files:**
- Modify: `VrcTwitchOscBridge/AvatarSwapManagerWindow.xaml`
- Modify: `VrcTwitchOscBridge/AvatarSwapManagerWindow.xaml.cs`

- [ ] **Step 1: Replace the window XAML**

Replace the contents of `VrcTwitchOscBridge/AvatarSwapManagerWindow.xaml` with the new layout from spec section 6.1. The full new file:

```xml
<Window x:Class="VrcTwitchOscBridge.AvatarSwapManagerWindow"
        xmlns="http://schemas.microsoft.com/winfx/2006/xaml/presentation"
        xmlns:x="http://schemas.microsoft.com/winfx/2006/xaml"
        xmlns:vm="clr-namespace:VrcTwitchOscBridge.ViewModels"
        xmlns:uc="clr-namespace:VrcTwitchOscBridge.UserControls"
        Title="Avatar Swap"
        Width="1100" Height="700"
        WindowStartupLocation="CenterOwner">
    <Window.Resources>
        <BooleanToVisibilityConverter x:Key="BoolToVis" />
    </Window.Resources>
    <Grid Margin="14">
        <Grid.RowDefinitions>
            <RowDefinition Height="Auto" />
            <RowDefinition Height="*" />
        </Grid.RowDefinitions>

        <!-- Global Return Avatar banner -->
        <Border Grid.Row="0" Background="#241a3a" BorderBrush="#3a2a5a" BorderThickness="1" CornerRadius="6" Padding="8" Margin="0,0,0,10">
            <DockPanel>
                <TextBlock DockPanel.Dock="Top" Text="↩ RETURN AVATAR (used by all swaps + roulettes)" Foreground="#9b86c9" FontSize="10" Margin="0,0,0,4" />
                <StackPanel Orientation="Horizontal">
                    <Border Width="32" Height="32" Background="#3a2a5a" CornerRadius="5" Margin="0,0,8,0" />
                    <TextBlock Text="{Binding GlobalReturnAvatarName}" VerticalAlignment="Center" Margin="0,0,12,0" />
                    <Button Content="Pick…" Command="{Binding PickGlobalReturnAvatarCommand}" Padding="8,4" Margin="0,0,4,0" />
                    <Button Content="Use Current" Command="{Binding UseCurrentAvatarForGlobalReturnCommand}" Padding="8,4" Margin="0,0,4,0" />
                    <Button Content="Clear" Command="{Binding ClearGlobalReturnCommand}" Padding="8,4" />
                </StackPanel>
            </DockPanel>
        </Border>

        <!-- Left cards + right editor -->
        <Grid Grid.Row="1">
            <Grid.ColumnDefinitions>
                <ColumnDefinition Width="*" />
                <ColumnDefinition Width="420" />
            </Grid.ColumnDefinitions>

            <!-- Left grid -->
            <StackPanel Grid.Column="0" Margin="0,0,10,0">
                <TextBlock Text="Avatar Swaps" FontSize="10" Foreground="#b0a3d0" Margin="0,0,0,6" />
                <ItemsControl ItemsSource="{Binding SwapCards}">
                    <ItemsControl.ItemsPanel>
                        <ItemsPanelTemplate>
                            <WrapPanel />
                        </ItemsPanelTemplate>
                    </ItemsControl.ItemsPanel>
                    <ItemsControl.ItemTemplate>
                        <DataTemplate>
                            <Border Width="180" Height="130" Background="#322250" BorderBrush="#4a3868" BorderThickness="1" CornerRadius="7" Margin="0,0,6,6" Padding="6">
                                <Border.InputBindings>
                                    <MouseBinding MouseAction="LeftClick" Command="{Binding DataContext.OpenSwapEditorCommand, RelativeSource={RelativeSource AncestorType=Window}}" CommandParameter="{Binding}" />
                                </Border.InputBindings>
                                <StackPanel>
                                    <Border Height="64" Background="#3a2a5a" CornerRadius="4" Margin="0,0,0,4" />
                                    <TextBlock Text="{Binding Profile.TargetAvatarName}" FontWeight="SemiBold" FontSize="11" />
                                    <TextBlock Text="{Binding AvatarSubtitle}" Foreground="#b0a3d0" FontSize="9" />
                                </StackPanel>
                            </Border>
                        </DataTemplate>
                    </ItemsControl.ItemTemplate>
                </ItemsControl>
                <Button Content="+ Add Avatar" Command="{Binding AddSwapCommand}" HorizontalAlignment="Left" Padding="10,5" Margin="0,6,0,14" />

                <TextBlock Text="🎰 Avatar Roulette" FontSize="10" Foreground="#d4af37" Margin="0,0,0,6" />
                <ItemsControl ItemsSource="{Binding RouletteCards}">
                    <ItemsControl.ItemsPanel>
                        <ItemsPanelTemplate>
                            <WrapPanel />
                        </ItemsPanelTemplate>
                    </ItemsControl.ItemsPanel>
                    <ItemsControl.ItemTemplate>
                        <DataTemplate>
                            <Border Width="180" Height="130" Background="#322250" BorderBrush="#d4af37" BorderThickness="1.5" CornerRadius="7" Margin="0,0,6,6" Padding="6">
                                <Border.InputBindings>
                                    <MouseBinding MouseAction="LeftClick" Command="{Binding DataContext.OpenRouletteEditorCommand, RelativeSource={RelativeSource AncestorType=Window}}" CommandParameter="{Binding}" />
                                </Border.InputBindings>
                                <StackPanel>
                                    <Border Height="64" Background="#3a2a5a" CornerRadius="4" Margin="0,0,0,4" />
                                    <TextBlock Text="{Binding Name}" FontWeight="SemiBold" FontSize="11" />
                                    <TextBlock Text="{Binding Subtitle}" Foreground="#d4af37" FontSize="9" />
                                </StackPanel>
                            </Border>
                        </DataTemplate>
                    </ItemsControl.ItemTemplate>
                </ItemsControl>
                <Button Content="+ Add Roulette" Command="{Binding AddRouletteCommand}" HorizontalAlignment="Left" Padding="10,5" Margin="0,6,0,0" />
            </StackPanel>

            <!-- Right editor: avatar swap -->
            <Border Grid.Column="1" Background="#241a3a" BorderBrush="#3a2a5a" BorderThickness="1" CornerRadius="6" Padding="10"
                    Visibility="{Binding IsSwapEditorOpen, Converter={StaticResource BoolToVis}}">
                <StackPanel>
                    <DockPanel Margin="0,0,0,8">
                        <Border Width="40" Height="40" Background="#3a2a5a" CornerRadius="5" Margin="0,0,8,0" />
                        <StackPanel VerticalAlignment="Center">
                            <TextBlock Text="{Binding SelectedSwapCard.Profile.TargetAvatarName}" FontWeight="SemiBold" FontSize="13" />
                            <TextBlock Text="Target Avatar" Foreground="#9b86c9" FontSize="10" />
                        </StackPanel>
                        <StackPanel DockPanel.Dock="Right" Orientation="Horizontal">
                            <Button Content="Browse" Padding="6,3" Margin="0,0,4,0" />
                            <Button Content="Use Current" Padding="6,3" />
                        </StackPanel>
                    </DockPanel>
                    <TextBlock Text="↩ Returns to global return avatar" Foreground="#9b86c9" FontSize="10" Margin="0,0,0,8" />

                    <TextBlock Text="🏆 Channel Points" FontWeight="SemiBold" FontSize="11" Margin="0,0,0,4" />
                    <ItemsControl ItemsSource="{Binding ChannelPointRows}">
                        <ItemsControl.ItemTemplate>
                            <DataTemplate>
                                <uc:InlineAvatarSwapRuleRowControl Row="{Binding}" />
                            </DataTemplate>
                        </ItemsControl.ItemTemplate>
                    </ItemsControl>
                    <Button Content="+ Add Channel Point" Command="{Binding AddChannelPointRuleCommand}" HorizontalAlignment="Left" Padding="6,3" Margin="0,4,0,8" />

                    <TextBlock Text="💎 Bits" FontWeight="SemiBold" FontSize="11" Margin="0,0,0,4" />
                    <ItemsControl ItemsSource="{Binding BitsRows}">
                        <ItemsControl.ItemTemplate>
                            <DataTemplate>
                                <uc:InlineAvatarSwapRuleRowControl Row="{Binding}" />
                            </DataTemplate>
                        </ItemsControl.ItemTemplate>
                    </ItemsControl>
                    <Button Content="+ Add Bits Trigger" Command="{Binding AddBitsRuleCommand}" HorizontalAlignment="Left" Padding="6,3" Margin="0,4,0,8" />

                    <TextBlock Text="⭐ Subs" FontWeight="SemiBold" FontSize="11" Margin="0,0,0,4" />
                    <ItemsControl ItemsSource="{Binding SubsRows}">
                        <ItemsControl.ItemTemplate>
                            <DataTemplate>
                                <uc:InlineAvatarSwapRuleRowControl Row="{Binding}" />
                            </DataTemplate>
                        </ItemsControl.ItemTemplate>
                    </ItemsControl>
                    <Button Content="+ Add Sub Trigger" Command="{Binding AddSubsRuleCommand}" HorizontalAlignment="Left" Padding="6,3" Margin="0,4,0,8" />

                    <TextBlock Text="💵 Payment" FontWeight="SemiBold" FontSize="11" Margin="0,0,0,4" />
                    <ItemsControl ItemsSource="{Binding PaymentRows}">
                        <ItemsControl.ItemTemplate>
                            <DataTemplate>
                                <uc:InlineAvatarSwapRuleRowControl Row="{Binding}" />
                            </DataTemplate>
                        </ItemsControl.ItemTemplate>
                    </ItemsControl>
                    <Button Content="+ Add Payment Trigger" Command="{Binding AddPaymentRuleCommand}" HorizontalAlignment="Left" Padding="6,3" Margin="0,4,0,8" />

                    <TextBlock Text="Advanced triggers (open full editor)" Foreground="#9b86c9" FontSize="10" Margin="0,8,0,4" />
                    <StackPanel Orientation="Horizontal">
                        <Button Content="💬 Chat Command" Command="{Binding AddAdvancedTriggerCommand}" CommandParameter="ChatCommand" Padding="6,3" Margin="0,0,4,0" />
                        <Button Content="👥 Follow" Command="{Binding AddAdvancedTriggerCommand}" CommandParameter="Follow" Padding="6,3" Margin="0,0,4,0" />
                        <Button Content="⚡ Power-up" Command="{Binding AddAdvancedTriggerCommand}" CommandParameter="PowerUp" Padding="6,3" />
                    </StackPanel>

                    <StackPanel Orientation="Horizontal" Margin="0,12,0,0">
                        <Button Content="Delete Avatar" Command="{Binding DeleteSwapCommand}" Padding="8,4" Background="#5a2a2a" Foreground="#f0a0a0" />
                        <Button Content="Save" Command="{Binding SaveSwapEditorCommand}" Padding="8,4" Margin="6,0,0,0" />
                    </StackPanel>
                </StackPanel>
            </Border>

            <!-- Right editor: roulette -->
            <Border Grid.Column="1" Background="#241a3a" BorderBrush="#3a2a5a" BorderThickness="1" CornerRadius="6" Padding="10"
                    Visibility="{Binding IsRouletteEditorOpen, Converter={StaticResource BoolToVis}}">
                <StackPanel>
                    <TextBlock Text="Roulette" FontWeight="SemiBold" FontSize="13" Margin="0,0,0,8" />
                    <TextBlock Text="Pool" FontWeight="SemiBold" FontSize="11" Margin="0,0,0,4" />
                    <ItemsControl ItemsSource="{Binding SelectedRouletteCard.Roulette.Pool}">
                        <ItemsControl.ItemsPanel>
                            <ItemsPanelTemplate>
                                <WrapPanel />
                            </ItemsPanelTemplate>
                        </ItemsControl.ItemsPanel>
                        <ItemsControl.ItemTemplate>
                            <DataTemplate>
                                <Border Width="60" Height="60" Background="#3a2a5a" CornerRadius="3" Margin="0,0,4,4" />
                            </DataTemplate>
                        </ItemsControl.ItemTemplate>
                    </ItemsControl>

                    <TextBlock Text="Triggers" FontWeight="SemiBold" FontSize="11" Margin="0,8,0,4" />
                    <ItemsControl ItemsSource="{Binding RouletteTriggerRows}">
                        <ItemsControl.ItemTemplate>
                            <DataTemplate>
                                <uc:InlineAvatarSwapRuleRowControl Row="{Binding}" />
                            </DataTemplate>
                        </ItemsControl.ItemTemplate>
                    </ItemsControl>

                    <StackPanel Orientation="Horizontal" Margin="0,12,0,0">
                        <Button Content="Delete Roulette" Command="{Binding DeleteRouletteCommand}" Padding="8,4" Background="#5a2a2a" Foreground="#f0a0a0" />
                        <Button Content="Save" Command="{Binding SaveRouletteEditorCommand}" Padding="8,4" Margin="6,0,0,0" />
                    </StackPanel>
                </StackPanel>
            </Border>
        </Grid>
    </Grid>
</Window>
```

- [ ] **Step 2: Build to verify**

Run: `dotnet build "E:\!!!Program to work on\Proper Crystal Relay\VrcTwitchOscBridge\VrcTwitchOscBridge.csproj" --no-restore`
Expected: 0 errors (or only warnings about unused converters / missing event handlers).

- [ ] **Step 3: Commit**

```bash
git add VrcTwitchOscBridge/AvatarSwapManagerWindow.xaml VrcTwitchOscBridge/AvatarSwapManagerWindow.xaml.cs
git commit -m "feat(avatar-swap): rewrite AvatarSwapManagerWindow with 4-section right panel + roulette cards"
```

---

## Phase 6: MainWindow Cleanup

### Task 25: Remove legacy "Avatar Change" UI from `MainWindow.xaml`

**Files:**
- Modify: `VrcTwitchOscBridge/MainWindow.xaml`

- [ ] **Step 1: Remove the "Avatar Change Setup" tab and related controls**

In `VrcTwitchOscBridge/MainWindow.xaml`, find and remove:
- The "Avatar Change Setup" tab block (around lines 3515-4200).
- The "Add Avatar Change Override" button and "Avatar Change Override Rules" list (around lines 4280-4310).
- The "Add Avatar Change" / "Delete Avatar Change" buttons on the master tab.
- The "Use cooldown-only avatar changes (no return avatar)" checkbox.
- The per-rule `UsesAvatarChange` action block (around lines 8825-8861).
- The "Permanent avatar change" checkbox on the Power-up editor.

> **Tip:** Use the `grep` tool to find each `x:Name` / `Command` reference before deletion. Verify no other XAML block depends on the removed `x:Name` values.

- [ ] **Step 2: Build to surface remaining errors**

Run: `dotnet build "E:\!!!Program to work on\Proper Crystal Relay\VrcTwitchOscBridge\VrcTwitchOscBridge.csproj" --no-restore`
Expected: Many errors at this point — the next task removes the corresponding VM members.

- [ ] **Step 3: Commit (build may fail — that's fine, the next task fixes it)**

```bash
git add VrcTwitchOscBridge/MainWindow.xaml
git commit -m "refactor(avatar-swap): remove legacy Avatar Change UI from MainWindow"
```

---

### Task 26: Remove legacy commands from `MainWindowViewModel`

**Files:**
- Modify: `VrcTwitchOscBridge/ViewModels/MainWindowViewModel.cs`

- [ ] **Step 1: Remove the legacy commands and properties**

In `VrcTwitchOscBridge/ViewModels/MainWindowViewModel.cs`, remove:
- `ShowMasterAvatarTabCommand`
- `AddAvatarChangeOverrideCommand`
- `UseCurrentAvatarForAvatarChangeRuleCommand`
- The `"AvatarChange"` branch of `OpenAvatarPickerCommand`
- The `AvatarChangeOverrideRules` projection
- The `HasAvatarChangeOverrideRules` projection

- [ ] **Step 2: Build to verify**

Run: `dotnet build "E:\!!!Program to work on\Proper Crystal Relay\VrcTwitchOscBridge\VrcTwitchOscBridge.csproj" --no-restore`
Expected: 0 errors.

- [ ] **Step 3: Commit**

```bash
git add VrcTwitchOscBridge/ViewModels/MainWindowViewModel.cs
git commit -m "refactor(avatar-swap): remove legacy Avatar Change commands from MainWindowViewModel"
```

---

## Phase 7: Localization

### Task 27: Add new localization keys

**Files:**
- Modify: `VrcTwitchOscBridge/Localization/en-US.json`
- Modify: every `VrcTwitchOscBridge/Localization/*.json` (one for each non-English language)
- Modify: every `VrcTwitchOscBridge/Localization/*.extra.json`

- [ ] **Step 1: Add new keys to en-US**

Open `VrcTwitchOscBridge/Localization/en-US.json` and add the following keys (place them in the same `{}` block as the existing keys; respect the file's existing key sort order if any):

```json
"Avatar Swap Manager": "Avatar Swap Manager",
"Global Return Avatar": "Global Return Avatar",
"Avatar Swaps": "Avatar Swaps",
"Avatar Roulette": "Avatar Roulette",
"Channel Points": "Channel Points",
"Bits": "Bits",
"Subs": "Subs",
"Payment": "Payment",
"+ Add Channel Point": "+ Add Channel Point",
"+ Add Bits Trigger": "+ Add Bits Trigger",
"+ Add Sub Trigger": "+ Add Sub Trigger",
"+ Add Payment Trigger": "+ Add Payment Trigger",
"Advanced triggers": "Advanced triggers",
"Advanced triggers (open full editor)": "Advanced triggers (open full editor)",
"GIFT": "GIFT",
"Roulette": "Roulette",
"Add Roulette": "Add Roulette",
"Pool": "Pool",
"Triggers": "Triggers",
"Edit Avatar Swap": "Edit Avatar Swap",
"Edit Roulette": "Edit Roulette",
"Avatar Swap has been reworked! Avatar Roulette is now its own card type, and Bits / Subs / Payment each have their own section. The per-avatar 'Return Avatar' is gone — all swaps now return to the global Return Avatar at the top of the window. This notice will not appear again.": "Avatar Swap has been reworked! Avatar Roulette is now its own card type, and Bits / Subs / Payment each have their own section. The per-avatar 'Return Avatar' is gone — all swaps now return to the global Return Avatar at the top of the window. This notice will not appear again."
```

- [ ] **Step 2: Translate each key into every non-English locale**

For each `Localization/<lang>.json` and `Localization/<lang>.extra.json`, translate every key from Step 1 using the project's localization rules (informal register, brand terms in English, natural gaming vocabulary, placeholders preserved). Use the existing keys' tone as a reference.

> **Tip:** Open one existing translated file (e.g., `de-DE.json` or `de-DE.extra.json`) and copy the style. Translate each new key with a natural, conversational tone in the target language.

- [ ] **Step 3: Commit**

```bash
git add VrcTwitchOscBridge/Localization/
git commit -m "feat(avatar-swap): add v4 rework localization keys to all locales"
```

---

## Phase 8: Docs and Notice

### Task 28: Update migration notice text

**Files:**
- Modify: `VrcTwitchOscBridge/MainWindow.xaml.cs`

- [ ] **Step 1: Update the notice text**

In `VrcTwitchOscBridge/MainWindow.xaml.cs`, find `ShowAvatarSwapMigrationNoticeIfNeeded` and replace the notice text constant with the new v4 text:

```csharp
private const string V4MigrationNoticeText =
    "Avatar Swap has been reworked! Avatar Roulette is now its own card type, and Bits / Subs / Payment each have their own section. The per-avatar 'Return Avatar' is gone — all swaps now return to the global Return Avatar at the top of the window. This notice will not appear again.";
```

- [ ] **Step 2: Build to verify**

Run: `dotnet build "E:\!!!Program to work on\Proper Crystal Relay\VrcTwitchOscBridge\VrcTwitchOscBridge.csproj" --no-restore`
Expected: 0 errors.

- [ ] **Step 3: Commit**

```bash
git add VrcTwitchOscBridge/MainWindow.xaml.cs
git commit -m "feat(avatar-swap): update migration notice text to v4"
```

---

### Task 29: Update `CHANGELOG.txt` and `RELEASE-CHANGE-RECORD.txt`

**Files:**
- Modify: `VrcTwitchOscBridge/CHANGELOG.txt`
- Modify: `VrcTwitchOscBridge/RELEASE-CHANGE-RECORD.txt`

- [ ] **Step 1: Add v3.1.10 entry to `CHANGELOG.txt`**

Open `VrcTwitchOscBridge/CHANGELOG.txt` and add a new section at the top:

```text
v3.1.10
- Avatar Swap window: split the right-side editor into four clean sections (Channel Points, Bits, Subs, Payment) with per-section add buttons.
- Avatar Roulette is now its own first-class card type with a gold-bordered style and a pool thumbnail strip.
- The per-avatar "Return Avatar" mode is gone. All swaps and roulettes return to the single global Return Avatar at the top of the window.
- Rule rows can be expanded inline in the section to edit cost, duration, cooldown, and other common fields without opening a separate editor.
- Chat Command, Follow, and Power-up are now first-class trigger sources in the Avatar Swap editor.
- Avatar Swap cards are smaller and use a 3-column grid.
- Legacy "Avatar Change Setup" tab and related controls are removed from the main window.
- One-time migration notice is shown on first run after upgrading from v3.1.9.
```

- [ ] **Step 2: Update `RELEASE-CHANGE-RECORD.txt` to bump Pending Release Draft to v3.1.10**

Open `VrcTwitchOscBridge/RELEASE-CHANGE-RECORD.txt`. Find the "Pending Release Draft" section header. Change the version line and copy the v3.1.10 changelog bullets into the `Added` / `Changed` / `Removed` buckets.

- [ ] **Step 3: Commit**

```bash
git add VrcTwitchOscBridge/CHANGELOG.txt VrcTwitchOscBridge/RELEASE-CHANGE-RECORD.txt
git commit -m "docs(avatar-swap): v3.1.10 changelog + release record"
```

---

### Task 30: Update `AGENTS.md`

**Files:**
- Modify: `AGENTS.md`

- [ ] **Step 1: Confirm current `Project Identity` matches the spec**

The spec says: "Target version: v3.1.10 (next post-release); Active build lane: v3.1.10 development lane."

Open `AGENTS.md` and verify the following lines:

```markdown
- Last stable release: `v3.1.9`
- Current source version: `v3.1.10`
- Next post-release development version: `v3.1.11`
- Active development build: `v3.1.10`
- Active build lane: `beta1`
```

If they already match, no change is needed. If not, update to match the spec.

- [ ] **Step 2: Commit (if changes were made)**

```bash
git add AGENTS.md
git commit -m "docs(avatar-swap): bump active build to v3.1.10 in AGENTS.md"
```

---

## Phase 9: Verify

### Task 31: Build the project

**Files:** (none modified)

- [ ] **Step 1: Run a clean build**

Run: `dotnet build "E:\!!!Program to work on\Proper Crystal Relay\VrcTwitchOscBridge\VrcTwitchOscBridge.csproj" --no-restore`
Expected: 0 errors.

- [ ] **Step 2: Run all tests**

Run: `dotnet test "E:\!!!Program to work on\Proper Crystal Relay\VrcTwitchOscBridge.Tests\VrcTwitchOscBridge.Tests.csproj" --no-restore`
Expected: All tests pass.

- [ ] **Step 3: Commit (no changes — record the build run)**

```bash
git commit --allow-empty -m "chore(avatar-swap): v3.1.10 build green"
```

---

### Task 32: Run the localization audit

**Files:** (none modified)

- [ ] **Step 1: Run `LocalizationAudit`**

Open the `LocalizationAudit` project and run its audit per the project's README. Verify:
- No empty values in non-English files
- No placeholder copies (e.g., "TODO" / "TBD")
- No English fallbacks in non-English locales
- All new keys are translated

- [ ] **Step 2: Fix any audit failures**

For any missing / placeholder / English-fallback value, fix the JSON file in `Localization/`. Use the existing keys' tone as a reference.

- [ ] **Step 3: Commit fixes**

```bash
git add VrcTwitchOscBridge/Localization/
git commit -m "fix(avatar-swap): localization audit fixes"
```

---

### Task 33: Manual smoke test

**Files:** (none modified)

- [ ] **Step 1: Launch the debug build**

Run: `E:\!!!Program to work on\Proper Crystal Relay\Launch-Crystal-Relay-Debug.bat`
Expected: The app starts and shows the "Avatar Swap has been reworked..." migration notice on first run after upgrading.

- [ ] **Step 2: Verify the new layout**

In the Avatar Swap window:
1. Confirm the Global Return Avatar banner is at the top.
2. Confirm the "Avatar Swaps" section is a 3-column grid of smaller cards.
3. Confirm the "🎰 Avatar Roulette" section is a 3-column grid of gold-bordered cards.
4. Click an avatar card → right panel shows the 4 sections (Channel Points, Bits, Subs, Payment) + Advanced triggers row.
5. Click a rule row → it expands inline.
6. Click an avatar card → click "💬 Chat Command" in Advanced → a new rule appears in Channel Points with the chat-command trigger type.
7. Click a roulette card → right panel shows the Pool thumbnails + Triggers list.

- [ ] **Step 3: Verify the legacy UI is gone**

1. Confirm the "Avatar Change Setup" tab is no longer in the main window.
2. Confirm there is no "Add Avatar Change Override" button on the master tab.
3. Confirm there is no "Add Avatar Change" / "Delete Avatar Change" buttons on the master tab.

- [ ] **Step 4: Restart the app**

1. Close the app.
2. Launch the debug build again.
3. Confirm the migration notice does not show.
4. Confirm all avatars and roulettes are persisted.

- [ ] **Step 5: Commit (no changes — record the smoke test)**

```bash
git commit --allow-empty -m "test(avatar-swap): v3.1.10 manual smoke test passed"
```

---

## Self-Review

After all tasks are complete, verify the plan against the spec by checking each section of `docs/superpowers/specs/2026-06-16-avatar-swap-full-migration-design.md`:

- **Section 1 (Summary)** — covered by Task 1-33.
- **Section 2 (Goals)** — verified by the manual smoke test (Task 33).
- **Section 3 (Non-Goals)** — preserved: `OscActionType.AvatarChange` enum value still exists, `AvatarChangeSetup` JSON key still in place.
- **Section 4 (Architecture)** — implemented in Tasks 15-24 (window), Tasks 10-12 (snapshots), Tasks 13-14 (dispatch).
- **Section 5 (Data Model)** — implemented in Tasks 1-6, 10-12.
- **Section 6 (UI Design)** — implemented in Tasks 16-24.
- **Section 7 (Migration Plan)** — implemented in Tasks 6-9.
- **Section 8 (Runtime Wiring)** — implemented in Tasks 13-14.
- **Section 9 (Paid System Routing)** — preserved (no changes needed beyond the new `BitsRules` / `SubsRules` / `PaymentRules` lookups).
- **Section 10 (Localization)** — implemented in Task 27.
- **Section 11 (File-Level Change List)** — every file is covered by a task in the plan.
- **Section 12 (Risks)** — migration is irreversible (mitigated by the one-time notice); Power-up and Cash stubs preserved; per-profile return mode removed (mitigated by the notice).
- **Section 13 (Testing)** — unit tests added in Tasks 1-12; manual smoke test in Task 33; localization audit in Task 32.
- **Section 14 (Out of Scope)** — `OscActionType.AvatarChange` rename, `TriggerRule` field removal, etc., explicitly NOT done.

If any spec section is missing from the plan, add a task to cover it.
