# Avatar Swap Migration Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Migrate the Avatar Change Redeem Library to a dedicated Avatar Swap manager window with per-avatar cards, image+name display, and a global Return Avatar header banner. Rename "Avatar Change" → "Avatar Swap" in every user-facing string.

**Architecture:** New `AvatarSwapManagerWindow` (custom-chrome WPF) following the same pattern as `AvatarSetsManagerWindow` and `UniversalTriggersManagerWindow`. One `AvatarSwapProfile` per target avatar, grouping existing `TriggerRule` rules by their `AvatarChangeTargetId`. Header banner owns the global Return Avatar picker. Two collapsible sections: Channel Point Swaps and Bits + Subs Swaps. One-time migration runs at settings load to fold existing `MasterAvatarProfile.ChannelPointRules` and `GlobalOverrideRules` Avatar-Change rules into the new collection.

**Tech Stack:** WPF / XAML / C# / .NET 10 (net10.0-windows), existing `AvatarImageService` + `AvatarPickerWindow` reused unchanged, `LocalizationAudit` project for end-of-build verification.

**Spec:** `docs/superpowers/specs/2026-06-16-avatar-swap-migration-design.md`

**Commit policy:** Per `AGENTS.md`, do not commit without explicit user request. Mark each task's "Commit" step as a **gated** step that asks the user before running `git commit`. The same applies to `dotnet build` (run after every code change to catch compile errors early).

---

## File Structure

**New files (8):**
- `VrcTwitchOscBridge/Models/ReturnAvatarMode.cs` — enum
- `VrcTwitchOscBridge/Models/AvatarSwapProfile.cs` — per-target-avatar model
- `VrcTwitchOscBridge/ViewModels/AvatarSwapCardViewModel.cs` — per-card wrapper
- `VrcTwitchOscBridge/ViewModels/AvatarSwapManagerViewModel.cs` — manager
- `VrcTwitchOscBridge/AvatarSwapManagerWindow.xaml` — manager window UI
- `VrcTwitchOscBridge/AvatarSwapManagerWindow.xaml.cs` — code-behind
- `VrcTwitchOscBridge/Services/AvatarSwapMigrationService.cs` — one-time upgrade
- `VrcTwitchOscBridge/Converters/AvatarSwapConverters.cs` — small string + bool helpers

**Modified files (10+):**
- `VrcTwitchOscBridge/VrcTwitchOscBridge.csproj` — register new files
- `VrcTwitchOscBridge/Models/AppSettings.cs` — add new collection + fields
- `VrcTwitchOscBridge/Services/SettingsStore.cs` — persist new collection + run migration
- `VrcTwitchOscBridge/Services/BridgeRuntimeConfiguration.cs` — add snapshot
- `VrcTwitchOscBridge/Services/BridgeCoordinator.cs` — new `ResolveAvatarSwapAction`
- `VrcTwitchOscBridge/MainWindow.xaml` — remove old surfaces, add new button
- `VrcTwitchOscBridge/ViewModels/MainWindowViewModel.cs` — add `OpenAvatarSwapManagerCommand`, remove legacy command and picker branch
- `VrcTwitchOscBridge/CHANGELOG.txt` — beta entry
- `VrcTwitchOscBridge/RELEASE-CHANGE-RECORD.txt` — scratchpad
- All `VrcTwitchOscBridge/Localization/*.json` and `Localization/*.extra.json` — rename + new keys

**Reused unchanged (read-only):**
- `VrcTwitchOscBridge/AvatarPickerWindow.xaml` + `.xaml.cs`
- `VrcTwitchOscBridge/Services/AvatarImageService.cs`
- `VrcTwitchOscBridge/Services/AvatarPickerService.cs`
- `VrcTwitchOscBridge/Models/AvatarTriggerProfile.cs` (read from, no edits)
- `VrcTwitchOscBridge/Models/TriggerRule.cs` (no edits)

---

## Task 1: Add `ReturnAvatarMode` enum

**Files:**
- Create: `VrcTwitchOscBridge/Models/ReturnAvatarMode.cs`

- [ ] **Step 1: Create the file**

Write the file `VrcTwitchOscBridge/Models/ReturnAvatarMode.cs`:

```csharp
namespace VrcTwitchOscBridge.Models;

public enum ReturnAvatarMode
{
    UseGlobal,
    UseCustom,
    SameAsTarget
}
```

- [ ] **Step 2: Build to verify**

Run: `dotnet build "E:\!!!Program to work on\Proper Crystal Relay\VrcTwitchOscBridge\VrcTwitchOscBridge.csproj" --no-restore`
Expected: `Build succeeded. 0 Warning(s) 0 Error(s)`

- [ ] **Step 3: Commit (gated)**

Ask the user: "Task 1 done. Want me to commit?" If yes:
```bash
git add VrcTwitchOscBridge/Models/ReturnAvatarMode.cs
git commit -m "feat(avatar-swap): add ReturnAvatarMode enum"
```

---

## Task 2: Add `AvatarSwapProfile` model

**Files:**
- Create: `VrcTwitchOscBridge/Models/AvatarSwapProfile.cs`

- [ ] **Step 1: Create the model file**

Write `VrcTwitchOscBridge/Models/AvatarSwapProfile.cs`:

```csharp
using System;
using System.Collections.ObjectModel;
using System.Collections.Specialized;
using System.Windows.Media;
using VrcTwitchOscBridge.Infrastructure;

namespace VrcTwitchOscBridge.Models;

public sealed class AvatarSwapProfile : ObservableObject
{
    private Guid id = Guid.NewGuid();
    private string targetAvatarId = string.Empty;
    private string targetAvatarName = string.Empty;
    private string? targetThumbnailUrl;
    private ReturnAvatarMode returnAvatarMode = ReturnAvatarMode.UseGlobal;
    private string? returnAvatarId;
    private string? returnAvatarName;
    private bool isEnabled = true;
    private DateTime createdAt = DateTime.UtcNow;
    private DateTime updatedAt = DateTime.UtcNow;
    private ObservableCollection<TriggerRule> channelPointRules = [];
    private ObservableCollection<TriggerRule> bitsSubsRules = [];

    public AvatarSwapProfile()
    {
        channelPointRules.CollectionChanged += OnCollectionChanged;
        bitsSubsRules.CollectionChanged += OnCollectionChanged;
    }

    public Guid Id
    {
        get => id;
        set => SetProperty(ref id, value);
    }

    public string TargetAvatarId
    {
        get => targetAvatarId;
        set
        {
            if (SetProperty(ref targetAvatarId, value ?? string.Empty))
            {
                RaisePropertyChanged(nameof(DisplayTitle));
                RaisePropertyChanged(nameof(HasTarget));
            }
        }
    }

    public string TargetAvatarName
    {
        get => targetAvatarName;
        set
        {
            if (SetProperty(ref targetAvatarName, value ?? string.Empty))
            {
                RaisePropertyChanged(nameof(DisplayTitle));
            }
        }
    }

    public string? TargetThumbnailUrl
    {
        get => targetThumbnailUrl;
        set => SetProperty(ref targetThumbnailUrl, value);
    }

    public ReturnAvatarMode ReturnAvatarMode
    {
        get => returnAvatarMode;
        set
        {
            if (SetProperty(ref returnAvatarMode, value))
            {
                RaisePropertyChanged(nameof(ReturnAvatarDisplay));
            }
        }
    }

    public string? ReturnAvatarId
    {
        get => returnAvatarId;
        set => SetProperty(ref returnAvatarId, value);
    }

    public string? ReturnAvatarName
    {
        get => returnAvatarName;
        set
        {
            if (SetProperty(ref returnAvatarName, value))
            {
                RaisePropertyChanged(nameof(ReturnAvatarDisplay));
            }
        }
    }

    public bool IsEnabled
    {
        get => isEnabled;
        set
        {
            if (SetProperty(ref isEnabled, value))
            {
                RaisePropertyChanged(nameof(StatusText));
            }
        }
    }

    public DateTime CreatedAt
    {
        get => createdAt;
        set => SetProperty(ref createdAt, value);
    }

    public DateTime UpdatedAt
    {
        get => updatedAt;
        set => SetProperty(ref updatedAt, value);
    }

    public ObservableCollection<TriggerRule> ChannelPointRules
    {
        get => channelPointRules;
        set
        {
            if (channelPointRules is not null)
            {
                channelPointRules.CollectionChanged -= OnCollectionChanged;
            }
            SetProperty(ref channelPointRules, value ?? []);
            if (channelPointRules is not null)
            {
                channelPointRules.CollectionChanged += OnCollectionChanged;
            }
            RaisePropertyChanged(nameof(HasRules));
            RaisePropertyChanged(nameof(UsesChannelPointRules));
            RaisePropertyChanged(nameof(AvatarSubtitle));
        }
    }

    public ObservableCollection<TriggerRule> BitsSubsRules
    {
        get => bitsSubsRules;
        set
        {
            if (bitsSubsRules is not null)
            {
                bitsSubsRules.CollectionChanged -= OnCollectionChanged;
            }
            SetProperty(ref bitsSubsRules, value ?? []);
            if (bitsSubsRules is not null)
            {
                bitsSubsRules.CollectionChanged += OnCollectionChanged;
            }
            RaisePropertyChanged(nameof(HasRules));
            RaisePropertyChanged(nameof(UsesBitsSubsRules));
            RaisePropertyChanged(nameof(AvatarSubtitle));
        }
    }

    public string DisplayTitle => string.IsNullOrWhiteSpace(TargetAvatarName)
        ? (string.IsNullOrWhiteSpace(TargetAvatarId) ? "New Avatar Swap" : TargetAvatarId)
        : TargetAvatarName;

    public bool HasTarget => !string.IsNullOrWhiteSpace(TargetAvatarId);

    public string AvatarSubtitle
    {
        get
        {
            var cp = ChannelPointRules?.Count ?? 0;
            var bs = BitsSubsRules?.Count ?? 0;
            if (cp == 0 && bs == 0) return "No rules yet";
            if (cp > 0 && bs > 0) return $"{cp} channel point · {bs} bits/subs";
            if (cp > 0) return $"{cp} channel point rule{(cp == 1 ? "" : "s")}";
            return $"{bs} bits/subs rule{(bs == 1 ? "" : "s")}";
        }
    }

    public bool HasRules => (ChannelPointRules?.Count ?? 0) + (BitsSubsRules?.Count ?? 0) > 0;

    public bool UsesChannelPointRules => (ChannelPointRules?.Count ?? 0) > 0;

    public bool UsesBitsSubsRules => (BitsSubsRules?.Count ?? 0) > 0;

    public string ReturnAvatarDisplay => ReturnAvatarMode switch
    {
        ReturnAvatarMode.UseGlobal => "Global return",
        ReturnAvatarMode.UseCustom => string.IsNullOrWhiteSpace(ReturnAvatarName)
            ? "Custom return"
            : $"Returns to {ReturnAvatarName}",
        ReturnAvatarMode.SameAsTarget => "One-way swap",
        _ => string.Empty
    };

    public string StatusText => IsEnabled ? "Ready" : "Disabled";

    public SolidColorBrush StatusStripeReadyBrush { get; } = CreateFrozenBrush("#4ADE80");

    public SolidColorBrush StatusStripeWarnBrush { get; } = CreateFrozenBrush("#FBBF24");

    public SolidColorBrush StatusStripeOffBrush { get; } = CreateFrozenBrush("#6B7280");

    private static SolidColorBrush CreateFrozenBrush(string hex)
    {
        try
        {
            var color = (Color)ColorConverter.ConvertFromString(hex);
            var brush = new SolidColorBrush(color);
            brush.Freeze();
            return brush;
        }
        catch
        {
            return Brushes.Gray;
        }
    }

    private void OnCollectionChanged(object? sender, NotifyCollectionChangedEventArgs e)
    {
        RaisePropertyChanged(nameof(HasRules));
        RaisePropertyChanged(nameof(UsesChannelPointRules));
        RaisePropertyChanged(nameof(UsesBitsSubsRules));
        RaisePropertyChanged(nameof(AvatarSubtitle));
        UpdatedAt = DateTime.UtcNow;
    }
}
```

- [ ] **Step 2: Build to verify**

Run: `dotnet build "E:\!!!Program to work on\Proper Crystal Relay\VrcTwitchOscBridge\VrcTwitchOscBridge.csproj" --no-restore`
Expected: 0 errors. Warnings about unused members are acceptable.

- [ ] **Step 3: Commit (gated)**

Ask the user. If yes:
```bash
git add VrcTwitchOscBridge/Models/AvatarSwapProfile.cs
git commit -m "feat(avatar-swap): add AvatarSwapProfile model"
```

---

## Task 3: Add new fields to `AppSettings`

**Files:**
- Modify: `VrcTwitchOscBridge/Models/AppSettings.cs`

- [ ] **Step 1: Read the current AppSettings.cs top to find where fields are declared**

Look at the existing private field block. Match the pattern (private field with initial value, public property with `SetProperty`).

- [ ] **Step 2: Add private fields**

Find a good insertion point (near the existing `avatarProfiles` and `globalOverrideRules` fields) and add:

```csharp
private ObservableCollection<AvatarSwapProfile> avatarSwapProfiles = [];
private string? masterAvatarSwapReturnId;
private string? masterAvatarSwapReturnName;
private int avatarChangeToAvatarSwapMigrationVersion;
```

- [ ] **Step 3: Add the public properties**

Right after the `GlobalOverrideRules` property (around line 136), add:

```csharp
public ObservableCollection<AvatarSwapProfile> AvatarSwapProfiles
{
    get => avatarSwapProfiles;
    set => SetProperty(ref avatarSwapProfiles, value ?? []);
}

public string? MasterAvatarSwapReturnId
{
    get => masterAvatarSwapReturnId;
    set
    {
        if (SetProperty(ref masterAvatarSwapReturnId, value))
        {
            RaisePropertyChanged(nameof(MasterAvatarSwapReturnDisplayName));
        }
    }
}

public string? MasterAvatarSwapReturnName
{
    get => masterAvatarSwapReturnName;
    set
    {
        if (SetProperty(ref masterAvatarSwapReturnName, value))
        {
            RaisePropertyChanged(nameof(MasterAvatarSwapReturnDisplayName));
        }
    }
}

public int AvatarChangeToAvatarSwapMigrationVersion
{
    get => avatarChangeToAvatarSwapMigrationVersion;
    set => SetProperty(ref avatarChangeToAvatarSwapMigrationVersion, value);
}

public bool HasMasterAvatarSwapReturn => !string.IsNullOrWhiteSpace(MasterAvatarSwapReturnId);

public string MasterAvatarSwapReturnDisplayName => string.IsNullOrWhiteSpace(MasterAvatarSwapReturnName)
    ? (string.IsNullOrWhiteSpace(MasterAvatarSwapReturnId) ? "(no return avatar picked)" : MasterAvatarSwapReturnId)
    : MasterAvatarSwapReturnName;
```

- [ ] **Step 4: Build to verify**

Run: `dotnet build "E:\!!!Program to work on\Proper Crystal Relay\VrcTwitchOscBridge\VrcTwitchOscBridge.csproj" --no-restore`
Expected: 0 errors.

- [ ] **Step 5: Commit (gated)**

Ask the user. If yes:
```bash
git add VrcTwitchOscBridge/Models/AppSettings.cs
git commit -m "feat(avatar-swap): add AvatarSwapProfiles and global return avatar to AppSettings"
```

---

## Task 4: Add a TDD test project baseline check

**Files:**
- Read: `VrcTwitchOscBridge.Tests/` (verify it exists and what test framework is used)

- [ ] **Step 1: Verify the test project exists and uses xUnit/MSTest**

Run: `Get-ChildItem "E:\!!!Program to work on\Proper Crystal Relay\VrcTwitchOscBridge.Tests" -ErrorAction SilentlyContinue`
Expected: a `*.csproj` file and at least one existing test file. Note the test framework in use.

- [ ] **Step 2: Read the test project's csproj to understand the reference to the main project**

Read `VrcTwitchOscBridge.Tests/VrcTwitchOscBridge.Tests.csproj` and confirm it references `VrcTwitchOscBridge.csproj` with `OutputType=Exe` or library and uses xUnit/MSTest. The exact framework isn't critical — just confirm a test can be added that touches `AppSettings` and a service.

- [ ] **Step 3: Run the existing test suite to confirm it builds and passes**

Run: `dotnet test "E:\!!!Program to work on\Proper Crystal Relay\VrcTwitchOscBridge.Tests\VrcTwitchOscBridge.Tests.csproj" --no-restore`
Expected: existing tests pass.

If the test project does not exist or cannot be run, skip the TDD parts of Tasks 5 and 6 and rely on `dotnet build` + a manual smoke test instead. Update the tasks accordingly.

---

## Task 5: Build `AvatarSwapMigrationService` with TDD

**Files:**
- Create: `VrcTwitchOscBridge/Services/AvatarSwapMigrationService.cs`
- Create: `VrcTwitchOscBridge.Tests/AvatarSwapMigrationServiceTests.cs` (only if test project exists)

- [ ] **Step 1: Write the failing test (only if Task 4 confirmed tests can run)**

If a test project is available, create `VrcTwitchOscBridge.Tests/AvatarSwapMigrationServiceTests.cs`:

```csharp
using VrcTwitchOscBridge.Models;
using VrcTwitchOscBridge.Services;
using Xunit;

namespace VrcTwitchOscBridge.Tests;

public sealed class AvatarSwapMigrationServiceTests
{
    [Fact]
    public void Migrate_FoldsMasterProfileRulesIntoAvatarSwapProfiles()
    {
        var settings = new AppSettings();
        var master = new AvatarTriggerProfile
        {
            IsMasterProfile = true,
            AvatarId = "avtr_return",
            AvatarName = "Return Avatar"
        };
        master.ChannelPointRules.Add(new TriggerRule
        {
            ActionType = OscActionType.AvatarChange,
            AvatarChangeTargetId = "avtr_a",
            AvatarTargetName = "Avatar A"
        });
        master.ChannelPointRules.Add(new TriggerRule
        {
            ActionType = OscActionType.AvatarChange,
            AvatarChangeTargetId = "avtr_a",
            AvatarTargetName = "Avatar A"
        });
        master.ChannelPointRules.Add(new TriggerRule
        {
            ActionType = OscActionType.AvatarChange,
            AvatarChangeTargetId = "avtr_b",
            AvatarTargetName = "Avatar B"
        });
        settings.AvatarProfiles.Add(master);

        AvatarSwapMigrationService.Migrate(settings);

        Assert.Equal(2, settings.AvatarSwapProfiles.Count);
        var a = settings.AvatarSwapProfiles.Single(p => p.TargetAvatarId == "avtr_a");
        var b = settings.AvatarSwapProfiles.Single(p => p.TargetAvatarId == "avtr_b");
        Assert.Equal(2, a.ChannelPointRules.Count);
        Assert.Single(b.ChannelPointRules);
        Assert.Equal("avtr_return", settings.MasterAvatarSwapReturnId);
        Assert.Equal(1, settings.AvatarChangeToAvatarSwapMigrationVersion);
    }

    [Fact]
    public void Migrate_FoldsGlobalOverrideRulesIntoBitsSubsRules()
    {
        var settings = new AppSettings();
        settings.GlobalOverrideRules.Add(new TriggerRule
        {
            ActionType = OscActionType.AvatarChange,
            AvatarChangeTargetId = "avtr_a",
            AvatarTargetName = "Avatar A",
            MinimumBits = 100
        });

        AvatarSwapMigrationService.Migrate(settings);

        var a = Assert.Single(settings.AvatarSwapProfiles);
        Assert.Single(a.BitsSubsRules);
        Assert.Equal(100, a.BitsSubsRules[0].MinimumBits);
    }

    [Fact]
    public void Migrate_SkipsWhenAlreadyMigrated()
    {
        var settings = new AppSettings
        {
            AvatarChangeToAvatarSwapMigrationVersion = 1
        };
        settings.AvatarProfiles.Add(new AvatarTriggerProfile
        {
            IsMasterProfile = true,
            AvatarId = "avtr_return"
        });
        settings.AvatarProfiles.First().ChannelPointRules.Add(new TriggerRule
        {
            ActionType = OscActionType.AvatarChange,
            AvatarChangeTargetId = "avtr_a"
        });

        AvatarSwapMigrationService.Migrate(settings);

        Assert.Empty(settings.AvatarSwapProfiles);
    }
}
```

- [ ] **Step 2: Run the test to verify it fails (compile error counts as fail)**

Run: `dotnet test "E:\!!!Program to work on\Proper Crystal Relay\VrcTwitchOscBridge.Tests\VrcTwitchOscBridge.Tests.csproj" --no-restore --filter AvatarSwapMigrationServiceTests`
Expected: build failure because `AvatarSwapMigrationService` does not exist.

- [ ] **Step 3: Write the implementation**

Create `VrcTwitchOscBridge/Services/AvatarSwapMigrationService.cs`:

```csharp
using System.Linq;
using VrcTwitchOscBridge.Models;

namespace VrcTwitchOscBridge.Services;

public static class AvatarSwapMigrationService
{
    public const int CurrentMigrationVersion = 1;

    public static void Migrate(AppSettings settings)
    {
        ArgumentNullException.ThrowIfNull(settings);

        if (settings.AvatarChangeToAvatarSwapMigrationVersion >= CurrentMigrationVersion)
        {
            return;
        }

        var masterProfile = settings.AvatarProfiles.FirstOrDefault(p => p.IsMasterProfile);
        if (masterProfile is not null && !string.IsNullOrWhiteSpace(masterProfile.AvatarId))
        {
            settings.MasterAvatarSwapReturnId = masterProfile.AvatarId;
            settings.MasterAvatarSwapReturnName = masterProfile.AvatarName;
        }

        if (masterProfile is not null)
        {
            foreach (var rule in masterProfile.ChannelPointRules
                         .Where(r => r.ActionType == OscActionType.AvatarChange
                                     && !string.IsNullOrWhiteSpace(r.AvatarChangeTargetId))
                         .ToList())
            {
                var profile = FindOrCreateProfile(settings, rule.AvatarChangeTargetId, rule.AvatarTargetName);
                profile.ChannelPointRules.Add(rule);
            }
        }

        foreach (var rule in settings.GlobalOverrideRules
                     .Where(r => r.ActionType == OscActionType.AvatarChange
                                 && !string.IsNullOrWhiteSpace(r.AvatarChangeTargetId))
                     .ToList())
        {
            var profile = FindOrCreateProfile(settings, rule.AvatarChangeTargetId, rule.AvatarTargetName);
            profile.BitsSubsRules.Add(rule);
        }

        settings.AvatarChangeToAvatarSwapMigrationVersion = CurrentMigrationVersion;
    }

    private static AvatarSwapProfile FindOrCreateProfile(AppSettings settings, string targetAvatarId, string targetAvatarName)
    {
        var existing = settings.AvatarSwapProfiles.FirstOrDefault(p =>
            string.Equals(p.TargetAvatarId, targetAvatarId, StringComparison.OrdinalIgnoreCase));
        if (existing is not null)
        {
            return existing;
        }

        var profile = new AvatarSwapProfile
        {
            TargetAvatarId = targetAvatarId,
            TargetAvatarName = targetAvatarName
        };
        settings.AvatarSwapProfiles.Add(profile);
        return profile;
    }
}
```

- [ ] **Step 4: Run the test to verify it passes**

Run: `dotnet test "E:\!!!Program to work on\Proper Crystal Relay\VrcTwitchOscBridge.Tests\VrcTwitchOscBridge.Tests.csproj" --no-restore --filter AvatarSwapMigrationServiceTests`
Expected: 3 passed.

- [ ] **Step 5: Commit (gated)**

Ask the user. If yes:
```bash
git add VrcTwitchOscBridge/Services/AvatarSwapMigrationService.cs VrcTwitchOscBridge.Tests/AvatarSwapMigrationServiceTests.cs
git commit -m "feat(avatar-swap): add migration service for Avatar Change -> Avatar Swap"
```

---

## Task 6: Wire migration into settings load

**Files:**
- Modify: `VrcTwitchOscBridge/Services/SettingsStore.cs`

- [ ] **Step 1: Find the existing LoadAsync method**

Search for `public async Task<AppSettings> LoadAsync` in `SettingsStore.cs`. The migration should run after the settings object is hydrated and before it is handed to the rest of the app.

- [ ] **Step 2: Add a call to the migration service**

Inside `LoadAsync`, just before `return settings;` (or at the equivalent success return point), add:

```csharp
AvatarSwapMigrationService.Migrate(settings);
```

If LoadAsync is structured differently (e.g., it returns a DTO and a separate method applies it to AppSettings), find the equivalent "hydrate" or "apply" method and add the call there instead.

- [ ] **Step 3: Add the using directive at the top of the file if not already present**

Ensure `using VrcTwitchOscBridge.Services;` is present. (It usually is, since `SettingsStore` lives in this namespace.)

- [ ] **Step 4: Build to verify**

Run: `dotnet build "E:\!!!Program to work on\Proper Crystal Relay\VrcTwitchOscBridge\VrcTwitchOscBridge.csproj" --no-restore`
Expected: 0 errors.

- [ ] **Step 5: Run the test suite to verify the test from Task 5 still passes**

Run: `dotnet test "E:\!!!Program to work on\Proper Crystal Relay\VrcTwitchOscBridge.Tests\VrcTwitchOscBridge.Tests.csproj" --no-restore`
Expected: all tests pass.

- [ ] **Step 6: Commit (gated)**

Ask the user. If yes:
```bash
git add VrcTwitchOscBridge/Services/SettingsStore.cs
git commit -m "feat(avatar-swap): run Avatar Swap migration at settings load"
```

---

## Task 7: Add `AvatarSwapProfiles` round-trip in `SettingsStore`

**Files:**
- Modify: `VrcTwitchOscBridge/Services/SettingsStore.cs`

- [ ] **Step 1: Find the persisted DTO and the DTO ⇄ model converters**

Search for `PersistedAvatarTriggerProfile` and the `ToAvatarProfile` / `ToPersistedAvatarProfile` methods in `SettingsStore.cs`. Add a parallel pair for `PersistedAvatarSwapProfile` and its converters.

- [ ] **Step 2: Add the persisted DTO class**

Inside the same partial class / namespace that holds the other persisted DTOs, add:

```csharp
public sealed class PersistedAvatarSwapProfile
{
    public Guid Id { get; set; }
    public string TargetAvatarId { get; set; } = string.Empty;
    public string TargetAvatarName { get; set; } = string.Empty;
    public string? TargetThumbnailUrl { get; set; }
    public ReturnAvatarMode ReturnAvatarMode { get; set; } = ReturnAvatarMode.UseGlobal;
    public string? ReturnAvatarId { get; set; }
    public string? ReturnAvatarName { get; set; }
    public bool IsEnabled { get; set; } = true;
    public DateTime CreatedAt { get; set; }
    public DateTime UpdatedAt { get; set; }
    public List<PersistedTriggerRule>? ChannelPointRules { get; set; }
    public List<PersistedTriggerRule>? BitsSubsRules { get; set; }
}
```

- [ ] **Step 3: Add the global return avatar + migration version fields to the root persisted DTO**

In the same root persisted settings DTO, add:

```csharp
public string? MasterAvatarSwapReturnId { get; set; }
public string? MasterAvatarSwapReturnName { get; set; }
public int AvatarChangeToAvatarSwapMigrationVersion { get; set; }
```

- [ ] **Step 4: Add the conversion methods**

Add `ToAvatarSwapProfile` and `ToPersistedAvatarSwapProfile` (mirror the existing avatar profile converters). Make sure `ChannelPointRules` and `BitsSubsRules` are passed through the existing `ToRule` / `ToPersistedRule` helpers so the rule data round-trips.

- [ ] **Step 5: Wire round-trip on load and save**

In the load path (where `settings.AvatarProfiles = ...` is set), add:

```csharp
settings.AvatarSwapProfiles = new ObservableCollection<AvatarSwapProfile>(
    (profile.AvatarSwapProfiles ?? []).Select(ToAvatarSwapProfile));
settings.MasterAvatarSwapReturnId = profile.MasterAvatarSwapReturnId;
settings.MasterAvatarSwapReturnName = profile.MasterAvatarSwapReturnName;
settings.AvatarChangeToAvatarSwapMigrationVersion = profile.AvatarChangeToAvatarSwapMigrationVersion;
```

In the save path (where the persisted DTO is built from settings), add:

```csharp
AvatarSwapProfiles = [.. settings.AvatarSwapProfiles.Select(ToPersistedAvatarSwapProfile)],
MasterAvatarSwapReturnId = settings.MasterAvatarSwapReturnId,
MasterAvatarSwapReturnName = settings.MasterAvatarSwapReturnName,
AvatarChangeToAvatarSwapMigrationVersion = settings.AvatarChangeToAvatarSwapMigrationVersion,
```

In the default-empty settings path (the "no save file yet" branch), initialize the new fields to safe defaults.

- [ ] **Step 6: Build to verify**

Run: `dotnet build "E:\!!!Program to work on\Proper Crystal Relay\VrcTwitchOscBridge\VrcTwitchOscBridge.csproj" --no-restore`
Expected: 0 errors.

- [ ] **Step 7: Run the test suite**

Run: `dotnet test "E:\!!!Program to work on\Proper Crystal Relay\VrcTwitchOscBridge.Tests\VrcTwitchOscBridge.Tests.csproj" --no-restore`
Expected: all tests pass.

- [ ] **Step 8: Commit (gated)**

Ask the user. If yes:
```bash
git add VrcTwitchOscBridge/Services/SettingsStore.cs
git commit -m "feat(avatar-swap): round-trip AvatarSwapProfiles in settings persistence"
```

---

## Task 8: Add `AvatarSwapProfileSnapshot` to `BridgeRuntimeConfiguration`

**Files:**
- Modify: `VrcTwitchOscBridge/Services/BridgeRuntimeConfiguration.cs`

- [ ] **Step 1: Find where `AvatarTriggerProfileSnapshot` is declared**

Grep for `AvatarTriggerProfileSnapshot` in `BridgeRuntimeConfiguration.cs`. Find the record definition (a `public sealed record` or `public record`).

- [ ] **Step 2: Add a parallel snapshot record**

Right after `AvatarTriggerProfileSnapshot`, add:

```csharp
public sealed record AvatarSwapProfileSnapshot(
    Guid Id,
    string TargetAvatarId,
    string TargetAvatarName,
    ReturnAvatarMode ReturnAvatarMode,
    string? ReturnAvatarId,
    string? ReturnAvatarName,
    bool IsEnabled,
    IReadOnlyList<TriggerRuleSnapshot> ChannelPointRules,
    IReadOnlyList<TriggerRuleSnapshot> BitsSubsRules);
```

- [ ] **Step 3: Add it to the top-level runtime config snapshot**

Find the main runtime configuration record (likely called `BridgeRuntimeConfiguration` or `RuntimeConfiguration` — it will have a `List<AvatarTriggerProfileSnapshot> AvatarProfiles` field). Add a parallel field:

```csharp
IReadOnlyList<AvatarSwapProfileSnapshot> AvatarSwapProfiles { get; init; }
public string? MasterAvatarSwapReturnId { get; init; }
public string? MasterAvatarSwapReturnName { get; init; }
```

- [ ] **Step 4: Populate the new fields in the snapshot builder**

Find the method that builds this snapshot (often called `Create` or `Build`). Mirror the existing `AvatarProfiles` population:

```csharp
AvatarSwapProfiles = [.. settings.AvatarSwapProfiles.Select(profile => new AvatarSwapProfileSnapshot(
    profile.Id,
    profile.TargetAvatarId,
    profile.TargetAvatarName,
    profile.ReturnAvatarMode,
    profile.ReturnAvatarId,
    profile.ReturnAvatarName,
    profile.IsEnabled,
    [.. profile.ChannelPointRules.Select(ToTriggerRuleSnapshot)],
    [.. profile.BitsSubsRules.Select(ToTriggerRuleSnapshot)]))],
MasterAvatarSwapReturnId = settings.MasterAvatarSwapReturnId,
MasterAvatarSwapReturnName = settings.MasterAvatarSwapReturnName,
```

If `ToTriggerRuleSnapshot` does not exist, use the helper the file already uses for the `AvatarProfiles` loop.

- [ ] **Step 5: Build to verify**

Run: `dotnet build "E:\!!!Program to work on\Proper Crystal Relay\VrcTwitchOscBridge\VrcTwitchOscBridge.csproj" --no-restore`
Expected: 0 errors.

- [ ] **Step 6: Commit (gated)**

Ask the user. If yes:
```bash
git add VrcTwitchOscBridge/Services/BridgeRuntimeConfiguration.cs
git commit -m "feat(avatar-swap): add AvatarSwapProfileSnapshot to runtime config"
```

---

## Task 9: Add `ResolveAvatarSwapAction` to `BridgeCoordinator`

**Files:**
- Modify: `VrcTwitchOscBridge/Services/BridgeCoordinator.cs`

- [ ] **Step 1: Find `ResolveAvatarChangeAction`**

Grep for `ResolveAvatarChangeAction` in `BridgeCoordinator.cs`. Read the method and understand its inputs (rule + cooldown context) and outputs (avatar-change OSC packet).

- [ ] **Step 2: Add a new `ResolveAvatarSwapAction` method**

Right after `ResolveAvatarChangeAction`, add a method that resolves the target avatar from an `AvatarSwapProfileSnapshot`:

```csharp
private (string targetId, string? returnId) ResolveAvatarSwapAction(AvatarSwapProfileSnapshot profile, string globalReturnId)
{
    if (!profile.IsEnabled)
    {
        return (string.Empty, null);
    }

    var returnId = profile.ReturnAvatarMode switch
    {
        ReturnAvatarMode.UseCustom => profile.ReturnAvatarId,
        ReturnAvatarMode.UseGlobal => string.IsNullOrWhiteSpace(globalReturnId) ? null : globalReturnId,
        ReturnAvatarMode.SameAsTarget => null,
        _ => null
    };

    return (profile.TargetAvatarId, returnId);
}
```

(If the runtime needs a packet builder here, mirror the call structure of `ResolveAvatarChangeAction`.)

- [ ] **Step 3: Add a method that walks `AvatarSwapProfiles` for a given redemption**

Find the existing `TriggerRule` resolution path (the part of `BridgeCoordinator` that fires when a Twitch channel-point redemption hits). Add a parallel `AvatarSwapProfile`-driven path: for each enabled profile whose `ChannelPointRules` contains a rule that matches the incoming `RewardId`, build the OSC packet using `ResolveAvatarSwapAction`.

If you are unsure where to inject this, search for `BuildAvatarChangePacket` and add the call in the same function.

- [ ] **Step 4: Make sure the per-rule fallback still works**

For any `TriggerRule` with `ActionType=AvatarChange` that is **not** in any `AvatarSwapProfile.ChannelPointRules` (e.g., an old rule that wasn't migrated), keep the existing `ResolveAvatarChangeAction` path. The new path is additive; it does not remove the old path.

- [ ] **Step 5: Build to verify**

Run: `dotnet build "E:\!!!Program to work on\Proper Crystal Relay\VrcTwitchOscBridge\VrcTwitchOscBridge.csproj" --no-restore`
Expected: 0 errors.

- [ ] **Step 6: Commit (gated)**

Ask the user. If yes:
```bash
git add VrcTwitchOscBridge/Services/BridgeCoordinator.cs
git commit -m "feat(avatar-swap): add AvatarSwap action resolution path to bridge runtime"
```

---

## Task 10: Build `AvatarSwapCardViewModel`

**Files:**
- Create: `VrcTwitchOscBridge/ViewModels/AvatarSwapCardViewModel.cs`

- [ ] **Step 1: Read `AvatarSetCardViewModel.cs` to mirror its pattern**

Read the first 100 lines of `VrcTwitchOscBridge/ViewModels/AvatarSetCardViewModel.cs`. Note the image load/cancel pipeline (`_imageCts`, `TriggerImageLoad`, `GetAvatarImageAsync`) and the per-card wrapper shape.

- [ ] **Step 2: Create the card VM**

Write `VrcTwitchOscBridge/ViewModels/AvatarSwapCardViewModel.cs`:

```csharp
using System;
using System.Threading;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Media;
using VrcTwitchOscBridge.Models;
using VrcTwitchOscBridge.Services;

namespace VrcTwitchOscBridge.ViewModels;

public sealed class AvatarSwapCardViewModel : ObservableObject
{
    private readonly AvatarImageService _imageService;
    private CancellationTokenSource? _imageCts;
    private ImageSource? image;
    private bool isUpdatingThumbnail;

    public AvatarSwapCardViewModel(AvatarSwapProfile profile, AvatarImageService imageService)
    {
        Profile = profile ?? throw new ArgumentNullException(nameof(profile));
        _imageService = imageService ?? throw new ArgumentNullException(nameof(imageService));
        profile.PropertyChanged += OnProfileChanged;
    }

    public AvatarSwapProfile Profile { get; }

    public ImageSource? Image
    {
        get => image;
        private set
        {
            if (SetProperty(ref image, value))
            {
                RaisePropertyChanged(nameof(HasImage));
            }
        }
    }

    public bool HasImage => image is not null;

    public string DisplayTitle => Profile.DisplayTitle;

    public string AvatarSubtitle => Profile.AvatarSubtitle;

    public string ReturnAvatarDisplay => Profile.ReturnAvatarDisplay;

    public string StatusText => Profile.StatusText;

    public string RuleCountText
    {
        get
        {
            var cp = Profile.ChannelPointRules.Count;
            var bs = Profile.BitsSubsRules.Count;
            if (cp == 0 && bs == 0) return "0";
            return $"{(cp + bs)}";
        }
    }

    public bool HasTarget => Profile.HasTarget;

    public bool IsEnabled => Profile.IsEnabled;

    public SolidColorBrush StatusStripeBrush => Profile.IsEnabled
        ? Profile.StatusStripeReadyBrush
        : Profile.StatusStripeOffBrush;

    public void SetThumbnailUrl(string? thumbnailUrl)
    {
        if (isUpdatingThumbnail)
        {
            return;
        }
        isUpdatingThumbnail = true;
        try
        {
            Profile.TargetThumbnailUrl = thumbnailUrl;
        }
        finally
        {
            isUpdatingThumbnail = false;
        }
        TriggerImageLoad(thumbnailUrl);
    }

    private void TriggerImageLoad(string? thumbnailUrl)
    {
        _imageCts?.Cancel();
        _imageCts?.Dispose();
        _imageCts = new CancellationTokenSource();
        var avatarId = Profile.TargetAvatarId;
        var ct = _imageCts.Token;

        var syncImage = _imageService.GetAvatarImage(avatarId, null, thumbnailUrl);
        if (syncImage is not null && !ct.IsCancellationRequested)
        {
            Application.Current?.Dispatcher.InvokeAsync(() => Image = syncImage);
            return;
        }

        _ = Task.Run(async () =>
        {
            try
            {
                var asyncImage = await _imageService.GetAvatarImageAsync(avatarId, null, thumbnailUrl, ct);
                if (asyncImage is not null && !ct.IsCancellationRequested)
                {
                    Application.Current?.Dispatcher.InvokeAsync(() => Image = asyncImage);
                }
            }
            catch (OperationCanceledException)
            {
                // expected when the avatar changes mid-load
            }
            catch
            {
                if (!ct.IsCancellationRequested)
                {
                    var placeholder = _imageService.GetPlaceholderImage();
                    Application.Current?.Dispatcher.InvokeAsync(() => Image = placeholder);
                }
            }
        }, ct);
    }

    private void OnProfileChanged(object? sender, System.ComponentModel.PropertyChangedEventArgs e)
    {
        switch (e.PropertyName)
        {
            case nameof(AvatarSwapProfile.TargetAvatarId):
            case nameof(AvatarSwapProfile.TargetThumbnailUrl):
                TriggerImageLoad(Profile.TargetThumbnailUrl);
                break;
            case nameof(AvatarSwapProfile.IsEnabled):
                RaisePropertyChanged(nameof(IsEnabled));
                RaisePropertyChanged(nameof(StatusText));
                RaisePropertyChanged(nameof(StatusStripeBrush));
                break;
            case nameof(AvatarSwapProfile.ChannelPointRules):
            case nameof(AvatarSwapProfile.BitsSubsRules):
                RaisePropertyChanged(nameof(AvatarSubtitle));
                RaisePropertyChanged(nameof(RuleCountText));
                break;
            case nameof(AvatarSwapProfile.ReturnAvatarDisplay):
                RaisePropertyChanged(nameof(ReturnAvatarDisplay));
                break;
        }
    }
}
```

- [ ] **Step 3: Build to verify**

Run: `dotnet build "E:\!!!Program to work on\Proper Crystal Relay\VrcTwitchOscBridge\VrcTwitchOscBridge.csproj" --no-restore`
Expected: 0 errors.

- [ ] **Step 4: Commit (gated)**

Ask the user. If yes:
```bash
git add VrcTwitchOscBridge/ViewModels/AvatarSwapCardViewModel.cs
git commit -m "feat(avatar-swap): add AvatarSwapCardViewModel"
```

---

## Task 11: Build `AvatarSwapManagerViewModel`

**Files:**
- Create: `VrcTwitchOscBridge/ViewModels/AvatarSwapManagerViewModel.cs`

- [ ] **Step 1: Read `AvatarSetsManagerViewModel` to mirror its constructor + commands**

Read the constructor and the first ~200 lines of `AvatarSetsManagerViewModel.cs`. Note how it:
- Wires `Settings.AvatarProfiles` into a backing `ObservableCollection<AvatarSetCardViewModel>`.
- Subscribes to the main window view model for `OpenAvatarPicker` (`"Profile"` context).
- Manages add/edit/delete/profile selection state.
- Owns the editor lifecycle (open / save / cancel / snapshot).

- [ ] **Step 2: Create the manager VM**

Write `VrcTwitchOscBridge/ViewModels/AvatarSwapManagerViewModel.cs`. The file will be ~600 lines and mirrors the structure of `UniversalTriggersManagerViewModel`. Key responsibilities:

- Hold `Settings.AvatarSwapProfiles` as the source of truth and maintain a per-card `AvatarSwapCardViewModel` collection.
- `AddSwapCommand` — opens `AvatarPickerService.OpenSingle(...)` in single-select mode; on confirm creates a new `AvatarSwapProfile` with the picked avatar's id+name and `ThumbnailUrl`, adds it to the collection, persists settings, syncs managed rewards.
- `OpenEditorCommand` (param = `AvatarSwapProfile`) — opens the side-docked editor with a snapshot.
- `SaveEditorCommand` — saves settings, runs `SynchronizeManagedChannelPointRewardsAsync`, closes the editor.
- `DeleteSelectedProfileCommand` — themed Yes/No confirm, removes the profile, saves, syncs.
- `PickReturnAvatarCommand` — opens the picker and writes back to `Settings.MasterAvatarSwapReturnId` / `Name`.
- `UseCurrentAvatarForReturnCommand` — copies `Settings.VrChat.CurrentAvatarId` into the return avatar fields.
- `ClearReturnAvatarCommand` — sets the return avatar fields back to null.
- `EnableAllCommand` / `DisableAllCommand` — bulk `IsEnabled = true/false` over the collection, persist, sync.
- `AvatarSection*`, `BitsSubsSection*` (filter / sort / count / collapse).
- `FilterText`, `SortMode`, `IsFilterActive` for the search box and sort combo.
- The card click → open editor; the per-card "Edit" footer button reuses the same command.

The constructor takes `(AppSettings settings, MainWindowViewModel mainWindowViewModel, AvatarImageService imageService)` so the manager can drive the picker via the main VM and call into the existing reward-sync flow.

- [ ] **Step 3: Build to verify**

Run: `dotnet build "E:\!!!Program to work on\Proper Crystal Relay\VrcTwitchOscBridge\VrcTwitchOscBridge.csproj" --no-restore`
Expected: 0 errors. (XAML will reference this VM, so if any public member is missing, the next task will fail.)

- [ ] **Step 4: Commit (gated)**

Ask the user. If yes:
```bash
git add VrcTwitchOscBridge/ViewModels/AvatarSwapManagerViewModel.cs
git commit -m "feat(avatar-swap): add AvatarSwapManagerViewModel"
```

---

## Task 12: Build `AvatarSwapManagerWindow` XAML and code-behind

**Files:**
- Create: `VrcTwitchOscBridge/AvatarSwapManagerWindow.xaml`
- Create: `VrcTwitchOscBridge/AvatarSwapManagerWindow.xaml.cs`

- [ ] **Step 1: Read `UniversalTriggersManagerWindow.xaml` to mirror its structure**

Read the file (it is 1669 lines). Note:
- The `<WindowChrome>` declaration and the `xmlns:shell="clr-namespace:Microsoft.Windows.Shell;assembly=PresentationFramework"` line.
- The inline `<Window.Resources>` block with brushes, `ToggleButton`, `CheckBox`, `ComboBox`, and `FilterChipToggleStyle` templates.
- The title bar (with theme-aware `HeadingFontFamily` / `BodyFontFamily`).
- The command bar with search box, sort combo, enable-all, add.
- The section header pattern (chevron + count + disable-all).
- The `ItemsControl` with `<ItemsPanelTemplate><WrapPanel Orientation="Horizontal" /></ItemsPanelTemplate>`.
- The `AvatarSwapCardTemplate` (you will write this — mirror `AvatarSetCardTemplate`).
- The side-docked editor pane (480px wide, three rows: title, body, footer).
- The themed Yes/No confirm dialog pattern (`ThemedDialogWindow`).

- [ ] **Step 2: Create the XAML**

Write `VrcTwitchOscBridge/AvatarSwapManagerWindow.xaml`. Required elements (in order):

1. `WindowChrome` declaration.
2. Inline `<Window.Resources>` with: `WindowBackgroundBrush`, `PanelBrush`, `NestedPanelBrush`, `BorderBrush`, `AccentBrush`, `MutedBrush`, `InputBrush`, `InputBorderBrush`, `TitleBar*`, `StatusStripe*` brushes, `WarnBrush*`, scrollbar `Thumb` / `Track` / `VerticalScrollBarTemplate` styles, custom `ComboBox` template, `ComboBoxItem` highlight, custom `CheckBox` template, `FilterChipToggleStyle`, `SecondaryButtonStyle`, `AvatarSwapCardTemplate`, `AvatarSwapReturnAvatarTemplate`.
3. Title bar with "Avatar Swap" title and close (✕) button.
4. Command bar: search `TextBox`, sort `ComboBox`, "Enable All" / "Disable All" buttons, "New Swap" button.
5. Return Avatar banner: image (`{Binding ReturnAvatarImage}`), name (`{Binding ReturnAvatarName}`), "Pick..." button, "Use Current Avatar" button, "Clear" button.
6. Channel Point Swaps section: section header (chevron + count + section disable), `ItemsControl` of cards.
7. Bits + Subs Swaps section: same shape, different collection.
8. Editor pane (480px slide-in): avatar header, return avatar radio group (`Use Global` / `Use Custom` / `Same as Target`), per-section rule list with "Add Rule" buttons, footer with `Delete` (red) and `Save` (accent).

Use `loc:Translate` for every user-facing string and bind to the `AvatarSwapManagerViewModel` properties.

- [ ] **Step 3: Create the code-behind**

Write `VrcTwitchOscBridge/AvatarSwapManagerWindow.xaml.cs`:

```csharp
using System.Windows;
using VrcTwitchOscBridge.Services;
using VrcTwitchOscBridge.ViewModels;

namespace VrcTwitchOscBridge;

public partial class AvatarSwapManagerWindow : Window
{
    private readonly AvatarSwapManagerViewModel _viewModel;

    public AvatarSwapManagerWindow(AvatarSwapManagerViewModel viewModel)
    {
        InitializeComponent();
        _viewModel = viewModel ?? throw new System.ArgumentNullException(nameof(viewModel));
        DataContext = _viewModel;
        ThemeManager.ThemeChanged += OnThemeChanged;
        ThemeManager.ApplyToResources(Resources);
        Closed += OnClosed;
    }

    public AvatarSwapManagerViewModel ViewModel => _viewModel;

    private void OnTitleBarMouseDown(object sender, System.Windows.Input.MouseButtonEventArgs e)
    {
        if (e.ChangedButton == System.Windows.Input.MouseButton.Left)
        {
            try
            {
                DragMove();
            }
            catch
            {
                // DragMove can throw if mouse is released before capture
            }
        }
    }

    private void OnSectionToggle(object sender, System.Windows.Input.MouseButtonEventArgs e)
    {
        if (sender is not System.Windows.FrameworkElement element || element.DataContext is null) return;
        if (element.Tag is string sectionName)
        {
            _viewModel.ToggleSectionCommand.Execute(sectionName);
        }
    }

    private void OnEditorBackdropClick(object sender, System.Windows.Input.MouseButtonEventArgs e)
    {
        _viewModel.CloseEditorCommand.Execute(null);
    }

    private void OnThemeChanged(object? sender, System.EventArgs e)
    {
        Dispatcher.InvokeAsync(() => ThemeManager.ApplyToResources(Resources));
    }

    private void OnClosed(object? sender, System.EventArgs e)
    {
        ThemeManager.ThemeChanged -= OnThemeChanged;
        _viewModel.OnWindowClosed();
    }
}
```

- [ ] **Step 4: Build to verify**

Run: `dotnet build "E:\!!!Program to work on\Proper Crystal Relay\VrcTwitchOscBridge\VrcTwitchOscBridge.csproj" --no-restore`
Expected: 0 errors. If XAML compile errors point to missing properties on the VM, fix the VM in Task 11 first.

- [ ] **Step 5: Commit (gated)**

Ask the user. If yes:
```bash
git add VrcTwitchOscBridge/AvatarSwapManagerWindow.xaml VrcTwitchOscBridge/AvatarSwapManagerWindow.xaml.cs
git commit -m "feat(avatar-swap): add AvatarSwapManagerWindow XAML and code-behind"
```

---

## Task 13: Add small `AvatarSwapConverters`

**Files:**
- Create: `VrcTwitchOscBridge/Converters/AvatarSwapConverters.cs`

- [ ] **Step 1: Create the file**

Write `VrcTwitchOscBridge/Converters/AvatarSwapConverters.cs`:

```csharp
using System;
using System.Globalization;
using System.Windows;
using System.Windows.Data;
using VrcTwitchOscBridge.Models;

namespace VrcTwitchOscBridge.Converters;

public sealed class ReturnAvatarModeToVisibilityConverter : IValueConverter
{
    public object Convert(object value, Type targetType, object parameter, CultureInfo culture)
    {
        if (value is ReturnAvatarMode mode && parameter is string paramName)
        {
            return string.Equals(mode.ToString(), paramName, StringComparison.OrdinalIgnoreCase)
                ? Visibility.Visible
                : Visibility.Collapsed;
        }
        return Visibility.Collapsed;
    }

    public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture)
    {
        throw new NotSupportedException();
    }
}

public sealed class ReturnAvatarModeToBoolConverter : IValueConverter
{
    public object Convert(object value, Type targetType, object parameter, CultureInfo culture)
    {
        if (value is ReturnAvatarMode mode && parameter is string paramName)
        {
            return string.Equals(mode.ToString(), paramName, StringComparison.OrdinalIgnoreCase);
        }
        return false;
    }

    public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture)
    {
        if (value is bool b && b && parameter is string paramName
            && Enum.TryParse<ReturnAvatarMode>(paramName, out var mode))
        {
            return mode;
        }
        return Binding.DoNothing;
    }
}
```

- [ ] **Step 2: Build to verify**

Run: `dotnet build "E:\!!!Program to work on\Proper Crystal Relay\VrcTwitchOscBridge\VrcTwitchOscBridge.csproj" --no-restore`
Expected: 0 errors.

- [ ] **Step 3: Commit (gated)**

Ask the user. If yes:
```bash
git add VrcTwitchOscBridge/Converters/AvatarSwapConverters.cs
git commit -m "feat(avatar-swap): add AvatarSwap converters"
```

---

## Task 14: Register all new files in the csproj

**Files:**
- Modify: `VrcTwitchOscBridge/VrcTwitchOscBridge.csproj`

- [ ] **Step 1: Find the existing `<Page>` and `<Compile>` sections for `AvatarSetsManagerWindow` and `UniversalTriggersManagerWindow`**

Use the file index from the spec exploration. There are explicit `<Page Include="AvatarSetsManagerWindow.xaml" />` and `<Compile Include="AvatarSetsManagerWindow.xaml.cs"><DependentUpon>AvatarSetsManagerWindow.xaml</DependentUpon></Compile>` entries.

- [ ] **Step 2: Add the new XAML page entry**

Right after the existing `UniversalTriggersManagerWindow.xaml` page entry, add:

```xml
<Page Include="AvatarSwapManagerWindow.xaml" />
```

- [ ] **Step 3: Add the new code-behind compile entry**

Right after the existing `UniversalTriggersManagerWindow.xaml.cs` compile entry, add:

```xml
<Compile Include="AvatarSwapManagerWindow.xaml.cs">
  <DependentUpon>AvatarSwapManagerWindow.xaml</DependentUpon>
</Compile>
```

- [ ] **Step 4: Add the new model and VM compile entries**

Find a sensible insertion point in the `<Compile>` block (near `AvatarSetsManagerViewModel.cs` / `AvatarSetCardViewModel.cs` / `Models\AvatarTriggerProfile.cs`) and add:

```xml
<Compile Include="Models\ReturnAvatarMode.cs" />
<Compile Include="Models\AvatarSwapProfile.cs" />
<Compile Include="ViewModels\AvatarSwapCardViewModel.cs" />
<Compile Include="ViewModels\AvatarSwapManagerViewModel.cs" />
<Compile Include="Services\AvatarSwapMigrationService.cs" />
<Compile Include="Converters\AvatarSwapConverters.cs" />
```

- [ ] **Step 5: Build to verify**

Run: `dotnet build "E:\!!!Program to work on\Proper Crystal Relay\VrcTwitchOscBridge\VrcTwitchOscBridge.csproj" --no-restore`
Expected: 0 errors. (If files are not in the csproj, the build will report "type or namespace not found" — fix any missing entries.)

- [ ] **Step 6: Commit (gated)**

Ask the user. If yes:
```bash
git add VrcTwitchOscBridge/VrcTwitchOscBridge.csproj
git commit -m "build(avatar-swap): register new Avatar Swap files in csproj"
```

---

## Task 15: Add `OpenAvatarSwapManagerCommand` to `MainWindowViewModel`

**Files:**
- Modify: `VrcTwitchOscBridge/ViewModels/MainWindowViewModel.cs`

- [ ] **Step 1: Find `OpenUniversalTriggersManager` and `OpenAvatarSetsManager`**

Grep for those two methods. Read them to see the lazy-create + Owner = MainWindow + clear-on-close pattern.

- [ ] **Step 2: Add the new command and method**

Mirror the `OpenUniversalTriggersManager` pattern:

```csharp
public RelayCommand OpenAvatarSwapManagerCommand { get; }

private AvatarSwapManagerWindow? _avatarSwapManagerWindow;

public void OpenAvatarSwapManager()
{
    if (_avatarSwapManagerWindow is null)
    {
        var vm = new AvatarSwapManagerViewModel(Settings, this, _avatarImageService);
        _avatarSwapManagerWindow = new AvatarSwapManagerWindow(vm)
        {
            Owner = Application.Current?.MainWindow as Window
        };
        _avatarSwapManagerWindow.Closed += (_, _) =>
        {
            _avatarSwapManagerWindow = null;
        };
    }
    _avatarSwapManagerWindow.Show();
    _avatarSwapManagerWindow.Activate();
}
```

In the constructor (or wherever other commands are wired), initialize the command:

```csharp
OpenAvatarSwapManagerCommand = new RelayCommand(OpenAvatarSwapManager);
```

Add `using VrcTwitchOscBridge;` and `using VrcTwitchOscBridge.ViewModels;` at the top of the file if not already present.

- [ ] **Step 3: Build to verify**

Run: `dotnet build "E:\!!!Program to work on\Proper Crystal Relay\VrcTwitchOscBridge\VrcTwitchOscBridge.csproj" --no-restore`
Expected: 0 errors. If `RelayCommand` is from a different namespace (e.g., `CommunityToolkit.Mvvm.Input`), match the existing imports.

- [ ] **Step 4: Commit (gated)**

Ask the user. If yes:
```bash
git add VrcTwitchOscBridge/ViewModels/MainWindowViewModel.cs
git commit -m "feat(avatar-swap): add OpenAvatarSwapManagerCommand to MainWindowViewModel"
```

---

## Task 16: Update `MainWindow.xaml` — remove old surfaces, add new button

**Files:**
- Modify: `VrcTwitchOscBridge/MainWindow.xaml`

- [ ] **Step 1: Find the Avatar Actions group in the Redeem Library right column**

Grep for `Avatar Change` and `ShowMasterAvatarTabCommand` and `AddAvatarChangeOverrideCommand` in `MainWindow.xaml`. Identify:
- The "Avatar Change Setup" tab button (around line 3519).
- The "Add Avatar Change Override" button and the "Avatar Change Override Rules" list (around lines 4024, 4301).
- The per-rule editor's `UsesAvatarChange` action block (around lines 8825–8861).

- [ ] **Step 2: Remove the Avatar Change tab button and its help text**

Delete the tab button bound to `ShowMasterAvatarTabCommand` and any of its help-button siblings in the Redeem Library.

- [ ] **Step 3: Remove the Avatar Change Override button and list**

Delete the "Add Avatar Change Override" button and the list bound to `AvatarChangeOverrideRules`. Remove the related `HasAvatarChangeOverrideRules` text if present.

- [ ] **Step 4: Remove the per-rule editor's `UsesAvatarChange` action block**

Delete the XAML block from line 8825 through line 8861 (the `Visibility="{Binding UsesAvatarChange, ...}"` StackPanel). Be careful to keep the surrounding editor intact — only remove the AvatarChange-specific subsection, not the `UsesAvatarRoulet` subsection right below it.

- [ ] **Step 5: Add the new "Avatar Swap" button in the Avatar Actions group**

In the same Avatar Actions group where "Avatar Scaling" lives, add a new button that mirrors the existing button style and binds to `OpenAvatarSwapManagerCommand`:

```xml
<Button Content="{loc:Translate 'Avatar Swap'}"
        Command="{Binding OpenAvatarSwapManagerCommand}"
        Style="{StaticResource RuleLibraryTabButtonStyle}" />
```

Position it next to "Avatar Scaling" so the Avatar Actions group reads Avatar Sets · Avatar Swap · Avatar Scaling · Movement Redeems.

- [ ] **Step 6: Build to verify**

Run: `dotnet build "E:\!!!Program to work on\Proper Crystal Relay\VrcTwitchOscBridge\VrcTwitchOscBridge.csproj" --no-restore`
Expected: 0 errors. If any XAML reference to `ShowMasterAvatarTabCommand`, `AddAvatarChangeOverrideCommand`, or the per-rule `UsesAvatarChange` block was missed, the build will fail.

- [ ] **Step 7: Commit (gated)**

Ask the user. If yes:
```bash
git add VrcTwitchOscBridge/MainWindow.xaml
git commit -m "feat(avatar-swap): remove legacy Avatar Change surfaces and add Avatar Swap button"
```

---

## Task 17: Remove legacy commands and picker branch from `MainWindowViewModel`

**Files:**
- Modify: `VrcTwitchOscBridge/ViewModels/MainWindowViewModel.cs`

- [ ] **Step 1: Find the legacy commands**

Grep for `ShowMasterAvatarTabCommand`, `AddAvatarChangeOverrideCommand`, `UseCurrentAvatarForAvatarChangeRuleCommand`, and the `"AvatarChange"` case inside `OpenAvatarPickerCommand`.

- [ ] **Step 2: Remove the legacy commands**

Delete the `ShowMasterAvatarTabCommand` declaration, its handler, and any view-state helpers that are now unreachable (`IsViewingMasterAvatar`, `MasterAvatarProfile`, `MasterAvatarRules`, `SelectedAvatarSetupTitle`, `SelectedAvatarPickerLabel`, `UseCurrentAvatarButtonText`, `MasterAvatarReturnText`, `MasterAvatarDisplayName`).

Delete the `AddAvatarChangeOverrideCommand` declaration and its handler. Delete `AvatarChangeOverrideRules` and `HasAvatarChangeOverrideRules` properties.

- [ ] **Step 3: Remove the `"AvatarChange"` branch from `OpenAvatarPickerCommand`**

In the switch on `CommandParameter` inside `OpenAvatarPickerCommand`, delete the case for `"AvatarChange"`. Keep `"Profile"`, `"PowerUp"`, `"Supporter"`.

- [ ] **Step 4: Remove the `UseCurrentAvatarForAvatarChangeRuleCommand`**

Delete the declaration and its handler. (This was only used by the per-rule editor block we just removed.)

- [ ] **Step 5: Build to verify**

Run: `dotnet build "E:\!!!Program to work on\Proper Crystal Relay\VrcTwitchOscBridge\VrcTwitchOscBridge.csproj" --no-restore`
Expected: 0 errors. Any leftover binding in XAML or other code that referenced the removed commands will produce a build error and tell you what to fix.

- [ ] **Step 6: Commit (gated)**

Ask the user. If yes:
```bash
git add VrcTwitchOscBridge/ViewModels/MainWindowViewModel.cs
git commit -m "refactor(avatar-swap): remove legacy Avatar Change commands and picker branch"
```

---

## Task 18: Localization — rename "Avatar Change Setup" → "Avatar Swap"

**Files:**
- Modify: every `VrcTwitchOscBridge/Localization/*.json` and `Localization/*.extra.json`

- [ ] **Step 1: List all language files**

Run: `Get-ChildItem "E:\!!!Program to work on\Proper Crystal Relay\VrcTwitchOscBridge\Localization\*.json" -ErrorAction SilentlyContinue | Select-Object -ExpandProperty Name`
Expected: a list of `en-US.json`, `de-DE.json`, `es-ES.json`, `fr-FR.json`, `it-IT.json`, `ja-JP.json`, `ko-KR.json`, `pl-PL.json`, `pt-BR.json`, `ru-RU.json`, `sv-SE.json`, `th-TH.json`, `zh-CN.json`, `zh-TW.json`.

- [ ] **Step 2: In each file, rename the value of the `Avatar Change Setup` key to `Avatar Swap`**

In each `.json` and `.extra.json` file, find the line `"Avatar Change Setup": "Avatar Change Setup"` (or the localized equivalent) and change the value to the localized "Avatar Swap". Examples:
- `en-US.json`: `"Avatar Swap"` and `"Avatar Swap Setup"` (matching keys).
- `de-DE.json`: `"Avatar-Wechsel"` (or whatever the natural translation is — see the existing `Avatar Change Setup` value in that file for the German phrasing pattern).
- `fr-FR.json`: `"Changement d'avatar"`.
- `es-ES.json`: `"Cambio de avatar"`.
- `ja-JP.json`: `"アバター変更"`.
- `ko-KR.json`: `"아바타 변경"`.
- `pt-BR.json`: `"Troca de avatar"`.
- `ru-RU.json`: `"Смена аватара"`.
- `zh-CN.json` / `zh-TW.json`: `"头像切换"`.
- `th-TH.json`: `"การสลับอวาตาร์"`.
- `it-IT.json`: `"Cambio avatar"`.
- `pl-PL.json`: `"Zmiana awatara"`.
- `sv-SE.json`: `"Avatarbyte"`.

**Use the existing `Avatar Change Setup` translation in each file as the base for the rename — change the *value* only, keep the key name stable to avoid JSON churn.**

- [ ] **Step 3: Verify the changes**

Re-grep every `Localization/*.json` for the string `"Avatar Change Setup"` in a value field. If any file still has the old value, fix it.

- [ ] **Step 4: Commit (gated)**

Ask the user. If yes:
```bash
git add VrcTwitchOscBridge/Localization/
git commit -m "feat(avatar-swap): rename Avatar Change Setup to Avatar Swap in localization"
```

---

## Task 19: Localization — add new keys for the Avatar Swap UI

**Files:**
- Modify: every `VrcTwitchOscBridge/Localization/*.json` and `Localization/*.extra.json`

- [ ] **Step 1: Add new keys in every language file**

In each `*.json` and `*.extra.json` file, add (or update) the following keys. Use the natural-language equivalents per the `Localization Translation Quality Rules` in `AGENTS.md`. Brand terms stay in English: `Crystal Relay`, `Twitch`, `VRChat`, `OSC`, `OSCQuery`, `Bits`, `Subs`, `StreamElements`, `Streamlabs`, `Ko-fi`, `VRC:`.

| Key | en-US value |
|---|---|
| `Avatar Swap` | `Avatar Swap` |
| `Avatar Swap Manager \| Crystal Relay` | `Avatar Swap Manager \| Crystal Relay` |
| `Return Avatar` | `Return Avatar` |
| `Channel Point Swaps` | `Channel Point Swaps` |
| `Bits + Subs Swaps` | `Bits + Subs Swaps` |
| `New Swap` | `New Swap` |
| `Add Swap` | `Add Swap` |
| `Use Global` | `Use Global` |
| `Use Custom` | `Use Custom` |
| `Same as Target` | `Same as Target` |
| `Avatar Swap Card Edit` | `Edit` |
| `Avatar Swap Card Pick Avatar` | `Pick Avatar` |
| `Avatar Swap Global Return Empty` | `Pick the Return Avatar first so timed Avatar Swap and Avatar Roulette redeems know the exact VRChat avatar ID to switch back to. If this is wrong, timed avatar switches cannot return correctly.` |
| `Avatar Swap Renamed Notice` | `Avatar Swap was renamed from Avatar Change. The old "Avatar Change Setup" tab and the "Avatar Change Override" lane in Supporter Overrides are now reachable through this new window.` |
| `Avatar Swap Enable All` | `Enable All` |
| `Avatar Swap Disable All` | `Disable All` |
| `Avatar Swap Search Placeholder` | `Search avatar swaps…` |
| `Avatar Swap Section Channel Points` | `Channel Point Swaps` |
| `Avatar Swap Section Bits Subs` | `Bits + Subs Swaps` |
| `Avatar Swap Editor Avatar Picked` | `Avatar picked` |
| `Avatar Swap Editor Use Current` | `Use Current Avatar` |
| `Avatar Swap Editor Clear Return` | `Clear` |
| `Avatar Swap Editor Pick Different` | `Pick Different Avatar` |
| `Avatar Swap Editor Save` | `Save` |
| `Avatar Swap Editor Delete` | `Delete Swap` |
| `Avatar Swap Editor Delete Confirm` | `Delete this Avatar Swap and all of its rules? This cannot be undone.` |
| `Avatar Swap Empty State` | `No Avatar Swaps yet. Click "New Swap" to add one.` |

- [ ] **Step 2: Build to verify**

Run: `dotnet build "E:\!!!Program to work on\Proper Crystal Relay\VrcTwitchOscBridge\VrcTwitchOscBridge.csproj" --no-restore`
Expected: 0 errors. (The XAML references these keys through `loc:Translate`; missing keys produce runtime XAML parse errors that won't show up at compile time, but the localization audit will catch them.)

- [ ] **Step 3: Commit (gated)**

Ask the user. If yes:
```bash
git add VrcTwitchOscBridge/Localization/
git commit -m "feat(avatar-swap): add new localization keys for the Avatar Swap manager UI"
```

---

## Task 20: Update `CHANGELOG.txt` and `RELEASE-CHANGE-RECORD.txt`

**Files:**
- Modify: `E:\!!!Program to work on\Proper Crystal Relay\CHANGELOG.txt`
- Modify: `E:\!!!Program to work on\Proper Crystal Relay\RELEASE-CHANGE-RECORD.txt`

- [ ] **Step 1: Read the current top of `CHANGELOG.txt`**

Open `CHANGELOG.txt`. Find the most recent beta section for the active development version. Add a new entry above the previous beta block.

- [ ] **Step 2: Add a beta entry for this work**

In `CHANGELOG.txt`, add a new section at the top:

```
v3.1.9 beta N

Added
- New Avatar Swap manager window with per-target-avatar cards showing the avatar image and name.
- Return Avatar header banner with global picker.
- Channel Point Swaps and Bits + Subs Swaps sections inside the manager.

Changed
- "Avatar Change" renamed to "Avatar Swap" throughout the Redeem Library.
- Avatar Change redeems are now grouped per target avatar.

Fixed
- Avatar image is no longer only visible inside the picker — it now appears on the rule card.
```

(Pick the correct `beta N` value based on the current `Active build lane` in `AGENTS.md`. The current lane is `beta3` per the file, so this would be `beta3`. Confirm with the user before writing the value.)

- [ ] **Step 3: Update `RELEASE-CHANGE-RECORD.txt`**

In `RELEASE-CHANGE-RECORD.txt`, add a `Pending Release Draft` block that mirrors the changelog entry but uses the `Added` / `Changed` / `Removed` heading structure. Update the `Current Baseline` block to note that the Avatar Change surfaces are now read-only legacy data and the new Avatar Swap manager is the primary UI.

- [ ] **Step 4: Commit (gated)**

Ask the user. If yes:
```bash
git add CHANGELOG.txt RELEASE-CHANGE-RECORD.txt
git commit -m "docs(avatar-swap): add changelog and release record entries for Avatar Swap"
```

---

## Task 21: Run the localization audit and fix any issues

**Files:**
- Read: `LocalizationAudit/` project

- [ ] **Step 1: Build the localization audit project**

Run: `dotnet build "E:\!!!Program to work on\Proper Crystal Relay\LocalizationAudit\LocalizationAudit.csproj" --no-restore`
Expected: build succeeds. If the project uses a different name, find it via `Get-ChildItem "E:\!!!Program to work on\Proper Crystal Relay" -Recurse -Filter "*.csproj" | Where-Object { $_.FullName -match "Localization" }`.

- [ ] **Step 2: Run the audit**

Run: `dotnet run --project "E:\!!!Program to work on\Proper Crystal Relay\LocalizationAudit\LocalizationAudit.csproj" --no-restore`
Expected: no errors, no missing keys, no empty values, no placeholder copies. (The audit script in the build pipeline runs the same check.)

- [ ] **Step 3: Fix any issues the audit flags**

If the audit reports a missing key, add the key to the language file (and its `.extra.json` if applicable). If it reports an empty value or a placeholder copy, replace the value with a proper translation.

- [ ] **Step 4: Re-run the audit**

Run: `dotnet run --project "E:\!!!Program to work on\Proper Crystal Relay\LocalizationAudit\LocalizationAudit.csproj" --no-restore`
Expected: clean pass.

- [ ] **Step 5: Commit (gated)**

Ask the user. If yes:
```bash
git add VrcTwitchOscBridge/Localization/
git commit -m "fix(avatar-swap): fix localization audit findings for Avatar Swap keys"
```

---

## Task 22: Full build and run the test suite

**Files:**
- Read: `VrcTwitchOscBridge.slnx`

- [ ] **Step 1: Build the app project directly (per `AGENTS.md`, the slnx is not a reliable validation target)**

Run: `dotnet build "E:\!!!Program to work on\Proper Crystal Relay\VrcTwitchOscBridge\VrcTwitchOscBridge.csproj" --no-restore`
Expected: 0 errors.

- [ ] **Step 2: Build the test project**

Run: `dotnet build "E:\!!!Program to work on\Proper Crystal Relay\VrcTwitchOscBridge.Tests\VrcTwitchOscBridge.Tests.csproj" --no-restore`
Expected: 0 errors.

- [ ] **Step 3: Run the full test suite**

Run: `dotnet test "E:\!!!Program to work on\Proper Crystal Relay\VrcTwitchOscBridge.Tests\VrcTwitchOscBridge.Tests.csproj" --no-restore`
Expected: all tests pass (including the `AvatarSwapMigrationServiceTests` from Task 5).

- [ ] **Step 4: If the slnx file is intentionally fixed and includes the test project, build the whole solution once for sanity**

Run: `dotnet build "E:\!!!Program to work on\Proper Crystal Relay\VrcTwitchOscBridge.slnx" --no-restore`
Expected: 0 errors. If the slnx has known issues (per `AGENTS.md`), skip this step and rely on the per-project builds above.

---

## Task 23: Manual smoke test with the debug launcher

**Files:**
- Read: `Launch-Crystal-Relay-Debug.bat`

- [ ] **Step 1: Launch the debug build**

Run: `& "E:\!!!Program to work on\Proper Crystal Relay\Launch-Crystal-Relay-Debug.bat"`
Expected: the Crystal Relay debug window opens with ` - DEBUG` in the title bar.

- [ ] **Step 2: Confirm the new "Avatar Swap" button is in the Redeem Library**

Click the "Avatar Swap" button in the Redeem Library right column. The manager window should open.

- [ ] **Step 3: Confirm the Return Avatar banner shows**

The manager window should show a "Return Avatar" banner at the top. If a Return Avatar was previously set, it should appear with the avatar's image. If not, the banner should show the empty-state hint.

- [ ] **Step 4: Add a new Avatar Swap**

Click "New Swap", pick an avatar from the picker, save. A new card should appear in the "Channel Point Swaps" section.

- [ ] **Step 5: Confirm migration**

If you have an existing save with `MasterAvatarProfile.ChannelPointRules` containing Avatar Change rules, restart the app and confirm those rules are now in the matching `AvatarSwapProfile.ChannelPointRules` (the same for `GlobalOverrideRules` → `BitsSubsRules`).

- [ ] **Step 6: Confirm a pre-existing rule still fires at runtime**

If a Twitch broadcaster token is active, redeem one of the migrated channel-point rewards and confirm the avatar change still happens. (If no broadcaster token is active, skip this step.)

- [ ] **Step 7: Close the debug build**

Click the ✕ on the title bar. The window should close cleanly.

- [ ] **Step 8: Report results**

Tell the user which manual checks passed and which (if any) need follow-up.

---

## Self-Review

After completing all tasks, run the writing-plans self-review against the spec:

1. **Spec coverage:** Walk through each section of `docs/superpowers/specs/2026-06-16-avatar-swap-migration-design.md` and confirm a task implements it. Gaps to check:
   - Section 4 (Architecture overview) — Tasks 11, 12.
   - Section 5 (Data model) — Tasks 1, 2, 3.
   - Section 6 (UI design) — Tasks 10, 11, 12, 13.
   - Section 6.4 (Removed from MainWindow.xaml) — Task 16.
   - Section 6.5 (Added to MainWindow.xaml) — Tasks 15, 16.
   - Section 7 (Migration plan) — Tasks 5, 6.
   - Section 8 (Localization) — Tasks 18, 19, 21.
   - Section 9 (File-level change list) — All tasks.
   - Section 10 (Risks & considerations) — Verified by Tasks 5, 6, 7 (save-format churn mitigated by the version marker).
   - Section 11 (Testing approach) — Tasks 4, 5, 21, 22, 23.

2. **Placeholder scan:** Search this plan for `TODO`, `TBD`, "implement later", "fill in details", "add appropriate", "similar to". Fix any matches.

3. **Type consistency:** Check that property names in earlier tasks (e.g., `AvatarSwapProfiles`, `MasterAvatarSwapReturnId`, `ReturnAvatarMode.UseGlobal`) match usage in later tasks. Fix any drift.

---

## Final Housekeeping

After all tasks pass and the user has confirmed the manual smoke test, remind the user that:

- `AGENTS.md` should be updated to reflect the `Last stable release` / `Current source version` / `Next post-release development version` if this beta is being promoted to stable. (Not done in this plan — only done at release time per `AGENTS.md`.)
- A test build (`Build-Crystal-Relay-Test.ps1`) should be run with the new version once the user has signed off on the implementation.
- The visual companion session can be stopped (and the temp session dir under `C:\Users\screm\AppData\Local\Temp\brainstorm-*` deleted if the user wants to reclaim space).
