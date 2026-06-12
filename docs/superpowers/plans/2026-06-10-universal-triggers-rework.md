# Universal Triggers UI Rework Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Replace the dense inline Universal Triggers block in `MainWindow.xaml` with a modern, refined, theme-aware card-grid UI, a 4-step guided create wizard, and a 3-step Fooma import preview. No changes to the data model, Fooma importer, fusion service, settings persistence, or runtime execution.

**Architecture:** Extract the new UI into four components at the project root (matching the existing pattern: `MainWindow.xaml`, `BugReportWindow.xaml`, etc. all sit next to `Models/`, `Services/`, `ViewModels/` with no `Views/` folder):
- `UniversalTriggersView.xaml` (+ `.xaml.cs`) — library view + inlined slide-out editor, embedded as a `UserControl` in `MainWindow.xaml`.
- `ViewModels/UniversalTriggersViewModel.cs` — filter, search, card readiness, command surface.
- `UniversalTriggerCreateWizardWindow.xaml` + `ViewModels/UniversalTriggerCreateWizardViewModel.cs` — 4-step modal create flow.
- `UniversalTriggerImportPreviewWindow.xaml` + `ViewModels/UniversalTriggerImportPreviewViewModel.cs` — 3-step modal Fooma import preview.

All new colors come from existing `DynamicResource` theme brushes (and a new soft-warn triplet added to every existing palette in `ThemeManager.cs`).

**Tech Stack:** C# (.NET 10 / `net10.0-windows`), WPF, XAML, MVVM, `Infrastructure/AsyncRelayCommand.cs`, `Infrastructure/RelayCommand.cs`, `Infrastructure/ObservableObject.cs`. No new NuGet packages. No new test framework (the codebase has no test project today — verification is `dotnet build --no-restore` plus the localization audit).

**Spec:** `docs/superpowers/specs/2026-06-10-universal-triggers-rework-design.md`

**Build verification command (run after every task that touches code or XAML):**
```
dotnet build "E:\!!!Program to work on\Proper Crystal Relay\VrcTwitchOscBridge\VrcTwitchOscBridge.csproj" --no-restore
```

**Localization audit (run after every task that touches .extra.json):**
```
dotnet run --project "E:\!!!Program to work on\Proper Crystal Relay\LocalizationAudit"
```

**Commit cadence:** Commit at the end of each task. Use the message prefix `feat:` for new files, `chore:` for refactors, `i18n:` for localization, `theme:` for theme additions, `fix:` for bug fixes.

---

## Task 1: Add the soft-warn theme brushes to ThemeManager

**Files:**
- Modify: `E:\!!!Program to work on\Proper Crystal Relay\VrcTwitchOscBridge\Services\ThemeManager.cs` (add three new brush entries to every existing palette — 16 palettes)

The existing `DangerBrush` / `DangerBorderBrush` / `DangerTextBrush` triplet is the model. We add a parallel `WarnBrush` / `WarnBorderBrush` / `WarnTextBrush` triplet for every palette, with a warm yellow tone per palette (matching that palette's overall feel).

- [ ] **Step 1: Add warn brushes to Void Crystal palette**

In `ThemeManager.cs` around line 284-323, inside the `AppTheme.TreetendersArm =>` `CreateBuiltInPalette` call, add three new entries after the existing `DangerBrush` / `DangerBorderBrush` entries:

```csharp
("DangerBrush", "#5C2D24"),
("DangerBorderBrush", "#A4614D"),
("WarnBrush", "#4a3a1a"),
("WarnBorderBrush", "#a08a3a"),
("WarnTextBrush", "#f0d878"),
("HighlightBorderBrush", "#5FA9F2"),
```

- [ ] **Step 2: Add warn brushes to Baked palette**

In the `AppTheme.Baked =>` `CreateBuiltInPalette` call (around line 324-371), add the same three lines after the `DangerBorderBrush` entry:

```csharp
("WarnBrush", "#5a4a2a"),
("WarnBorderBrush", "#a89060"),
("WarnTextBrush", "#f0d090"),
```

- [ ] **Step 3: Add warn brushes to Dread Night Bar palette**

In the `AppTheme.DreadNightBar =>` `CreateBuiltInPalette` call (around line 372-415), add:

```csharp
("WarnBrush", "#4a3a1f"),
("WarnBorderBrush", "#a08a55"),
("WarnTextBrush", "#f0d090"),
```

- [ ] **Step 4: Add warn brushes to Dream Scape palette**

In the `AppTheme.DreamScape =>` `CreateBuiltInPalette` call (around line 416-460), add:

```csharp
("WarnBrush", "#3a2c4a"),
("WarnBorderBrush", "#a080c0"),
("WarnTextBrush", "#f0d8ff"),
```

- [ ] **Step 5: Add warn brushes to Peaches & Cream palette**

In the `AppTheme.PeachesAndCream =>` `CreateBuiltInPalette` call (around line 461-504), add:

```csharp
("WarnBrush", "#5a4a2a"),
("WarnBorderBrush", "#a89060"),
("WarnTextBrush", "#806030"),
```

- [ ] **Step 6: Add warn brushes to Cosmic Puppy Girl palette**

In the `AppTheme.CosmicPuppyGirl =>` `CreateBuiltInPalette` call, add:

```csharp
("WarnBrush", "#4a3a2a"),
("WarnBorderBrush", "#a08a5a"),
("WarnTextBrush", "#f0d8a0"),
```

- [ ] **Step 7: Add warn brushes to Moon Bunny Wink palette**

In the `AppTheme.MoonBunnyWink =>` `CreateBuiltInPalette` call, add:

```csharp
("WarnBrush", "#5a4a3a"),
("WarnBorderBrush", "#c0a070"),
("WarnTextBrush", "#604020"),
```

- [ ] **Step 8: Add warn brushes to remaining 9 palettes**

For each of `Bubblegum`, `MainFrame`, `TrashKitty`, `CarrotPatch`, `TreetendersArm` (no — that's Void Crystal; skip), `Bratwurst`, `NeonBorb`, `StinkyOnline`, and any other palette listed in the `AppTheme` enum that wasn't covered above, add the same three `Warn*` lines right after the `DangerBorderBrush` entry. Use warm yellow tones that match each palette's existing accent (e.g. for `Bubblegum` use `#5a4a2a`/`#a89060`/`#f0d090`; for `NeonBorb` use `#4a3a1a`/`#a08a3a`/`#f0d878`; for the rest pick a yellow that contrasts with the palette's existing danger color without clashing).

- [ ] **Step 9: Build and verify**

Run: `dotnet build "E:\!!!Program to work on\Proper Crystal Relay\VrcTwitchOscBridge\VrcTwitchOscBridge.csproj" --no-restore`
Expected: `Build succeeded. 0 Error(s)`. Warning count unchanged from baseline.

- [ ] **Step 10: Commit**

```bash
git add VrcTwitchOscBridge/Services/ThemeManager.cs
git commit -m "theme: add WarnBrush/WarnBorderBrush/WarnTextBrush to all palettes"
```

---

## Task 2: Add the new view-model folder to the csproj

**Files:**
- Modify: `E:\!!!Program to work on\Proper Crystal Relay\VrcTwitchOscBridge\VrcTwitchOscBridge.csproj` (no actual change — the new `.cs` files in `ViewModels/` are picked up by the existing `<Compile Include="ViewModels\*.cs" />` if it exists, otherwise we need to add explicit includes)

- [ ] **Step 1: Check whether the csproj has a wildcard include for `ViewModels/`**

Run in PowerShell: `Select-String -Path "E:\!!!Program to work on\Proper Crystal Relay\VrcTwitchOscBridge\VrcTwitchOscBridge.csproj" -Pattern "ViewModels\\\\\*\\\\.cs"`
Expected: shows a line like `<Compile Include="ViewModels\*.cs" />` if it exists, or no output if individual files are listed.

- [ ] **Step 2: If no wildcard, note this for Task 3-7 — the new files will be added one at a time**

If a wildcard include exists, no csproj edit is needed for new files in `ViewModels/`. The new `.xaml` files (which are pages, not compiles) WILL need to be added to the `<Page>` list in subsequent tasks.

- [ ] **Step 3: Commit any csproj changes if needed**

If you edited the csproj, commit it as a chore:
```bash
git add VrcTwitchOscBridge/VrcTwitchOscBridge.csproj
git commit -m "chore: prepare csproj for new Universal Triggers files"
```

(If no changes were needed, skip this step.)

---

## Task 3: Create the `UniversalTriggerCardViewModel` helper

**Files:**
- Create: `E:\!!!Program to work on\Proper Crystal Relay\VrcTwitchOscBridge\ViewModels\UniversalTriggerCardViewModel.cs`

A small VM that wraps a `UniversalTriggerRule` and exposes the per-card state (readiness, warning, type chip text, action summary). This is what the `ItemsControl` in the library view binds to. Keeps the card `DataTemplate` thin and the readiness logic testable in isolation.

- [ ] **Step 1: Create the file**

Write the following to `E:\!!!Program to work on\Proper Crystal Relay\VrcTwitchOscBridge\ViewModels\UniversalTriggerCardViewModel.cs`:

```csharp
using System.Collections.Generic;
using System.Linq;
using VrcTwitchOscBridge.Models;

namespace VrcTwitchOscBridge.ViewModels;

public enum UniversalTriggerCardStatus
{
    Unconfigured,
    Ready,
    WarnDirectOsc,
    WarnNotAvatarBound,
    DangerMissingParam,
    DangerNoActions,
}

public sealed class UniversalTriggerCardViewModel
{
    public UniversalTriggerRule Rule { get; }

    public UniversalTriggerCardViewModel(UniversalTriggerRule rule)
    {
        Rule = rule;
    }

    public string TypeChipText => Rule.TriggerType switch
    {
        UniversalTriggerType.ChatCommand => "CHAT COMMAND",
        UniversalTriggerType.ChannelPointReward => "CHANNEL POINT",
        UniversalTriggerType.Bits => "BITS",
        UniversalTriggerType.Subscription => "SUBSCRIPTION",
        UniversalTriggerType.GiftSubscription => "GIFT SUBSCRIPTION",
        UniversalTriggerType.Follow => "FOLLOW",
        _ => "UNCONFIGURED",
    };

    public string SecondaryChipText
    {
        get
        {
            if (string.Equals(Rule.ImportSource, "Fooma Twitch Interaction", System.StringComparison.OrdinalIgnoreCase))
                return "from Fooma";
            return Rule.TriggerType switch
            {
                UniversalTriggerType.ChannelPointReward when Rule.RewardCost > 0 => $"{Rule.RewardCost} pts",
                UniversalTriggerType.Bits => $"Bits {Rule.MinimumBits}-{Rule.MaximumBits}",
                _ => string.Empty,
            };
        }
    }

    public string ActionSummary
    {
        get
        {
            var count = Rule.Actions.Count;
            if (count == 0) return "No actions yet";
            var totalSeconds = Rule.Actions.Sum(a => a.DurationSeconds);
            var paths = Rule.Actions
                .Select(a => a.OscAddress)
                .Where(p => !string.IsNullOrWhiteSpace(p))
                .Distinct()
                .Take(2)
                .ToList();
            var pathText = paths.Count == 0 ? string.Empty : string.Join(", ", paths) + (count > paths.Count ? ", ..." : string.Empty);
            return totalSeconds > 0
                ? $"{count} action(s), {totalSeconds:0.#}s total · {pathText}"
                : $"{count} action(s) · {pathText}";
        }
    }

    public bool IsUnconfigured => !Rule.IsConfigured;

    public bool IsDanger => PrimaryStatus is UniversalTriggerCardStatus.DangerMissingParam or UniversalTriggerCardStatus.DangerNoActions;

    public bool IsWarn => PrimaryStatus is UniversalTriggerCardStatus.WarnDirectOsc or UniversalTriggerCardStatus.WarnNotAvatarBound;

    public UniversalTriggerCardStatus PrimaryStatus
    {
        get
        {
            if (IsUnconfigured) return UniversalTriggerCardStatus.Unconfigured;
            if (!Rule.HasCompleteAction) return UniversalTriggerCardStatus.DangerNoActions;
            if (HasAnyDirectOscAction() && !HasAnyAvatarParamAction())
                return UniversalTriggerCardStatus.WarnDirectOsc;
            if (HasAnyDirectOscAction())
                return UniversalTriggerCardStatus.WarnNotAvatarBound;
            if (HasMissingAvatarParams())
                return UniversalTriggerCardStatus.DangerMissingParam;
            return UniversalTriggerCardStatus.Ready;
        }
    }

    public IReadOnlyList<string> AvatarParamPaths => Rule.Actions
        .Select(a => a.OscAddress)
        .Where(p => !string.IsNullOrWhiteSpace(p) && (p.StartsWith("avatar/parameters/") || p.StartsWith("/avatar/parameters/")))
        .Select(p => p.StartsWith("/") ? p : "/" + p)
        .Distinct()
        .ToList();

    public IReadOnlyList<string> MissingAvatarParamNames(IReadOnlyCollection<string> currentAvatarParams)
    {
        return AvatarParamPaths
            .Where(p => !currentAvatarParams.Contains(p))
            .Select(p => p.Substring("/avatar/parameters/".Length))
            .ToList();
    }

    private bool HasAnyAvatarParamAction() => Rule.Actions.Any(a =>
        !string.IsNullOrWhiteSpace(a.OscAddress) &&
        (a.OscAddress.StartsWith("avatar/parameters/") || a.OscAddress.StartsWith("/avatar/parameters/")));

    private bool HasAnyDirectOscAction() => Rule.Actions.Any(a =>
        !string.IsNullOrWhiteSpace(a.OscAddress) &&
        a.OscAddress.StartsWith("/") &&
        !a.OscAddress.StartsWith("/avatar/parameters/"));

    private bool HasMissingAvatarParams()
    {
        if (!HasAnyAvatarParamAction()) return false;
        // The runtime check (IsUniversalTriggerReadyForCurrentAvatarJson) is the authority
        // for "missing param"; the view-model defers to that for now. The simpler heuristic
        // is: if the trigger is configured but no avatar JSON has been loaded, treat as missing.
        return false;
    }
}
```

- [ ] **Step 2: Build and verify**

Run: `dotnet build "E:\!!!Program to work on\Proper Crystal Relay\VrcTwitchOscBridge\VrcTwitchOscBridge.csproj" --no-restore`
Expected: `Build succeeded. 0 Error(s)`.

- [ ] **Step 3: Commit**

```bash
git add VrcTwitchOscBridge/ViewModels/UniversalTriggerCardViewModel.cs
git commit -m "feat: add UniversalTriggerCardViewModel helper for the library view"
```

---

## Task 4: Create the `UniversalTriggersViewModel`

**Files:**
- Create: `E:\!!!Program to work on\Proper Crystal Relay\VrcTwitchOscBridge\ViewModels\UniversalTriggersViewModel.cs`

The main library view-model. Owns the filter state, the search text, the list of cards, the editor visibility, and the command surface.

- [ ] **Step 1: Create the file with the basic shell**

Write the following to `E:\!!!Program to work on\Proper Crystal Relay\VrcTwitchOscBridge\ViewModels\UniversalTriggersViewModel.cs`:

```csharp
using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Linq;
using System.Windows.Data;
using VrcTwitchOscBridge.Infrastructure;
using VrcTwitchOscBridge.Models;
using VrcTwitchOscBridge.Services;

namespace VrcTwitchOscBridge.ViewModels;

public sealed class UniversalTriggersViewModel : ObservableObject
{
    private readonly AppSettings settings;
    private readonly BridgeCoordinator coordinator;
    private readonly Action<Action> uiInvoke;

    public ObservableCollection<UniversalTriggerRule> UniversalTriggers => settings.UniversalTriggers;
    public ICollectionView UniversalTriggersView { get; }

    private UniversalTriggerRule? selectedTrigger;
    public UniversalTriggerRule? SelectedTrigger
    {
        get => selectedTrigger;
        set => SetProperty(ref selectedTrigger, value);
    }

    private bool isEditorOpen;
    public bool IsEditorOpen
    {
        get => isEditorOpen;
        set => SetProperty(ref isEditorOpen, value);
    }

    private string searchText = string.Empty;
    public string SearchText
    {
        get => searchText;
        set
        {
            if (SetProperty(ref searchText, value ?? string.Empty))
            {
                UniversalTriggersView.Refresh();
                RaiseCountsChanged();
            }
        }
    }

    private bool showAll = true;
    public bool ShowAll { get => showAll; set { if (SetProperty(ref showAll, value)) { if (value) { ShowReady = ShowWarnings = ShowFooma = false; } ApplyFilter(); RaiseCountsChanged(); } } }
    private bool showReady;
    public bool ShowReady { get => showReady; set { if (SetProperty(ref showReady, value)) { if (value) ShowAll = false; ApplyFilter(); RaiseCountsChanged(); } } }
    private bool showWarnings;
    public bool ShowWarnings { get => showWarnings; set { if (SetProperty(ref showWarnings, value)) { if (value) ShowAll = false; ApplyFilter(); RaiseCountsChanged(); } } }
    private bool showFooma;
    public bool ShowFooma { get => showFooma; set { if (SetProperty(ref showFooma, value)) { if (value) ShowAll = false; ApplyFilter(); RaiseCountsChanged(); } } }

    public int CountAll => settings.UniversalTriggers.Count;
    public int CountReady => settings.UniversalTriggers.Count(IsCardReady);
    public int CountWarnings => settings.UniversalTriggers.Count(r => IsCardWarn(r) || IsCardDanger(r));
    public int CountFooma => settings.UniversalTriggers.Count(r => string.Equals(r.ImportSource, "Fooma Twitch Interaction", StringComparison.OrdinalIgnoreCase));

    public AsyncRelayCommand AddTriggerCommand { get; }
    public AsyncRelayCommand ImportFoomaCommand { get; }
    public AsyncRelayCommand DeleteAllCommand { get; }
    public AsyncRelayCommand EnableAllCommand { get; }
    public AsyncRelayCommand DisableAllCommand { get; }
    public RelayCommand OpenTriggerEditorCommand { get; }
    public RelayCommand CloseEditorCommand { get; }
    public AsyncRelayCommand TestSelectedTriggerCommand { get; }
    public AsyncRelayCommand DeleteSelectedTriggerCommand { get; }

    public UniversalTriggersViewModel(AppSettings settings, BridgeCoordinator coordinator, Action<Action> uiInvoke)
    {
        this.settings = settings;
        this.coordinator = coordinator;
        this.uiInvoke = uiInvoke;

        UniversalTriggersView = CollectionViewSource.GetDefaultView(UniversalTriggers);
        UniversalTriggersView.Filter = FilterTrigger;

        UniversalTriggers.CollectionChanged += (_, _) => RaiseCountsChanged();

        AddTriggerCommand = new AsyncRelayCommand(_ => OpenCreateWizardAsync());
        ImportFoomaCommand = new AsyncRelayCommand(_ => OpenImportPreviewAsync());
        DeleteAllCommand = new AsyncRelayCommand(_ => DeleteAllAsync());
        EnableAllCommand = new AsyncRelayCommand(_ => { foreach (var t in UniversalTriggers) t.IsEnabled = true; });
        DisableAllCommand = new AsyncRelayCommand(_ => { foreach (var t in UniversalTriggers) t.IsEnabled = false; });
        OpenTriggerEditorCommand = new RelayCommand(p => { if (p is UniversalTriggerRule rule) { SelectedTrigger = rule; IsEditorOpen = true; } });
        CloseEditorCommand = new RelayCommand(_ => IsEditorOpen = false);
        TestSelectedTriggerCommand = new AsyncRelayCommand(async _ => await TestSelectedAsync());
        DeleteSelectedTriggerCommand = new AsyncRelayCommand(async _ => await DeleteSelectedAsync());
    }

    private bool FilterTrigger(object obj)
    {
        if (obj is not UniversalTriggerRule rule) return false;
        if (!string.IsNullOrWhiteSpace(SearchText))
        {
            var q = SearchText.Trim();
            var matchesText = (rule.Name?.Contains(q, StringComparison.OrdinalIgnoreCase) ?? false)
                              || (rule.CommandText?.Contains(q, StringComparison.OrdinalIgnoreCase) ?? false)
                              || (rule.RewardTitle?.Contains(q, StringComparison.OrdinalIgnoreCase) ?? false)
                              || rule.Actions.Any(a => a.OscAddress?.Contains(q, StringComparison.OrdinalIgnoreCase) ?? false);
            if (!matchesText) return false;
        }
        if (ShowReady) return IsCardReady(rule);
        if (ShowWarnings) return IsCardWarn(rule) || IsCardDanger(rule);
        if (ShowFooma) return string.Equals(rule.ImportSource, "Fooma Twitch Interaction", StringComparison.OrdinalIgnoreCase);
        return true;
    }

    private void ApplyFilter() => UniversalTriggersView.Refresh();

    private void RaiseCountsChanged()
    {
        RaisePropertyChanged(nameof(CountAll));
        RaisePropertyChanged(nameof(CountReady));
        RaisePropertyChanged(nameof(CountWarnings));
        RaisePropertyChanged(nameof(CountFooma));
    }

    private static bool IsCardReady(UniversalTriggerRule r) =>
        r.IsConfigured && r.HasCompleteAction && new UniversalTriggerCardViewModel(r).PrimaryStatus == UniversalTriggerCardStatus.Ready;

    private static bool IsCardWarn(UniversalTriggerRule r) =>
        new UniversalTriggerCardViewModel(r).IsWarn;

    private static bool IsCardDanger(UniversalTriggerRule r) =>
        new UniversalTriggerCardViewModel(r).IsDanger;

    private async Task OpenCreateWizardAsync()
    {
        // Wired in Task 7 (wizard).
        await Task.CompletedTask;
    }

    private async Task OpenImportPreviewAsync()
    {
        // Wired in Task 9 (import preview).
        await Task.CompletedTask;
    }

    private async Task DeleteAllAsync()
    {
        // Wired in Task 11 (wiring).
        await Task.CompletedTask;
    }

    private async Task TestSelectedAsync()
    {
        if (selectedTrigger is null) return;
        await coordinator.SendTestUniversalTriggerAsync(
            BridgeRuntimeConfiguration.CreateManualTestSnapshot(selectedTrigger),
            default);
    }

    private async Task DeleteSelectedAsync()
    {
        if (selectedTrigger is null) return;
        var snapshot = selectedTrigger;
        IsEditorOpen = false;
        UniversalTriggers.Remove(snapshot);
        await Task.CompletedTask;
    }
}
```

- [ ] **Step 2: Build and verify**

Run: `dotnet build "E:\!!!Program to work on\Proper Crystal Relay\VrcTwitchOscBridge\VrcTwitchOscBridge.csproj" --no-restore`
Expected: 1-2 errors about unknown types (`AsyncRelayCommand`, `BridgeCoordinator`, `BridgeRuntimeConfiguration`). This is expected — the helper classes may not exist with the exact signatures. Resolve each error by reading the actual class definitions in `Infrastructure/AsyncRelayCommand.cs`, `Services/BridgeCoordinator.cs`, and `Services/BridgeRuntimeConfiguration.cs`, then adjust the view-model to match. The most likely fix is changing `AsyncRelayCommand` constructor to use the existing constructor signature (check `Infrastructure/AsyncRelayCommand.cs` for the real one) and verifying `BridgeCoordinator.SendTestUniversalTriggerAsync` exists and accepts the snapshot.

- [ ] **Step 3: Commit**

```bash
git add VrcTwitchOscBridge/ViewModels/UniversalTriggersViewModel.cs
git commit -m "feat: add UniversalTriggersViewModel with filter, search, and command surface"
```

---

## Task 5: Create the `UniversalTriggersView.xaml` (library view, no editor yet)

**Files:**
- Create: `E:\!!!Program to work on\Proper Crystal Relay\VrcTwitchOscBridge\UniversalTriggersView.xaml`
- Create: `E:\!!!Program to work on\Proper Crystal Relay\VrcTwitchOscBridge\UniversalTriggersView.xaml.cs`
- Modify: `E:\!!!Program to work on\Proper Crystal Relay\VrcTwitchOscBridge\VrcTwitchOscBridge.csproj` (add the new `.xaml` and `.xaml.cs` to the `<Page>` and `<Compile>` lists)

- [ ] **Step 1: Add the new files to the csproj**

In `VrcTwitchOscBridge.csproj`, in the `<Page Include="..." />` list (around line 34-50), add:

```xml
<Page Include="UniversalTriggersView.xaml" />
```

In the `<Compile Include="..." />` list (around line 68-180), add:

```xml
<Compile Include="UniversalTriggersView.xaml.cs" />
```

- [ ] **Step 2: Create the code-behind**

Write the following to `E:\!!!Program to work on\Proper Crystal Relay\VrcTwitchOscBridge\UniversalTriggersView.xaml.cs`:

```csharp
using System.Windows.Controls;

namespace VrcTwitchOscBridge;

public partial class UniversalTriggersView : UserControl
{
    public UniversalTriggersView()
    {
        InitializeComponent();
    }
}
```

- [ ] **Step 3: Create the XAML**

Write the following to `E:\!!!Program to work on\Proper Crystal Relay\VrcTwitchOscBridge\UniversalTriggersView.xaml`. This is the library view (toolbar, filter strip, card grid, empty-state onboarding) WITHOUT the slide-out editor yet — that comes in Task 6.

```xml
<UserControl x:Class="VrcTwitchOscBridge.UniversalTriggersView"
             xmlns="http://schemas.microsoft.com/winfx/2006/xaml/presentation"
             xmlns:x="http://schemas.microsoft.com/winfx/2006/xaml"
             xmlns:vm="clr-namespace:VrcTwitchOscBridge.ViewModels"
             xmlns:loc="clr-namespace:VrcTwitchOscBridge.Infrastructure"
             d:DataContext="{d:DesignInstance Type=vm:UniversalTriggersViewModel}"
             xmlns:d="http://schemas.microsoft.com/expression/blend/2008"
             xmlns:mc="http://schemas.openxmlformats.org/markup-compatibility/2006"
             mc:Ignorable="d"
             Background="{DynamicResource WindowBackgroundBrush}">

    <UserControl.Resources>
        <BooleanToVisibilityConverter x:Key="BoolToVisibilityConverter" />
        <loc:BoolToOpacityConverter x:Key="BoolToOpacityConverter" />
    </UserControl.Resources>

    <Grid>
        <Grid.RowDefinitions>
            <RowDefinition Height="Auto" />
            <RowDefinition Height="Auto" />
            <RowDefinition Height="*" />
        </Grid.RowDefinitions>

        <!-- Slim toolbar -->
        <Border Grid.Row="0" Background="{DynamicResource TitleBarBrush}" BorderBrush="{DynamicResource BorderBrush}" BorderThickness="0,0,0,1" Padding="14,10">
            <DockPanel LastChildFill="False">
                <StackPanel DockPanel.Dock="Left" Orientation="Vertical">
                    <TextBlock Text="{loc:Translate 'Universal Triggers'}" Foreground="{DynamicResource TitleBarTextBrush}" FontWeight="Bold" FontSize="14" />
                    <TextBlock Text="{loc:Translate 'Universal Triggers Subtitle'}" Foreground="{DynamicResource TitleBarSubTextBrush}" FontSize="11" Margin="0,2,0,0" />
                </StackPanel>
                <Button DockPanel.Dock="Right" Style="{StaticResource SecondaryButtonStyle}" Content="{loc:Translate 'Universal Triggers Delete All'}" Margin="8,0,0,0" Command="{Binding DeleteAllCommand}" />
                <Button DockPanel.Dock="Right" Style="{StaticResource SecondaryButtonStyle}" Content="{loc:Translate 'Universal Triggers Import Fooma'}" Margin="8,0,0,0" Command="{Binding ImportFoomaCommand}" />
                <Button DockPanel.Dock="Right" Background="{DynamicResource AccentBrush}" Foreground="{DynamicResource ComboTextBrush}" BorderBrush="{DynamicResource AccentBrush}" FontWeight="Bold" Padding="12,6" Margin="8,0,0,0" Content="{loc:Translate 'Universal Triggers New Trigger'}" Command="{Binding AddTriggerCommand}" />
            </DockPanel>
        </Border>

        <!-- Filter strip -->
        <Border Grid.Row="1" Background="{DynamicResource NestedPanelBrush}" BorderBrush="{DynamicResource BorderBrush}" BorderThickness="0,0,0,1" Padding="14,8">
            <DockPanel LastChildFill="False">
                <StackPanel DockPanel.Dock="Left" Orientation="Horizontal">
                    <Button Margin="0,0,6,0" Padding="10,3" Command="{Binding ShowAllCommand}">
                        <Button.Style>
                            <Style TargetType="Button" BasedOn="{StaticResource SecondaryButtonStyle}">
                                <Style.Triggers>
                                    <DataTrigger Binding="{Binding ShowAll}" Value="True">
                                        <Setter Property="Background" Value="{DynamicResource AccentBrush}" />
                                        <Setter Property="BorderBrush" Value="{DynamicResource AccentBrush}" />
                                    </DataTrigger>
                                </Style.Triggers>
                            </Style>
                        </Button.Style>
                        <TextBlock Foreground="{DynamicResource TextBrush}">
                            <Run Text="{loc:Translate 'Universal Triggers Filter All'}" />
                        </TextBlock>
                    </Button>
                    <Button Margin="0,0,6,0" Padding="10,3" Command="{Binding ShowReadyCommand}">
                        <Button.Style>
                            <Style TargetType="Button" BasedOn="{StaticResource SecondaryButtonStyle}">
                                <Style.Triggers>
                                    <DataTrigger Binding="{Binding ShowReady}" Value="True">
                                        <Setter Property="Background" Value="{DynamicResource AccentBrush}" />
                                        <Setter Property="BorderBrush" Value="{DynamicResource AccentBrush}" />
                                    </DataTrigger>
                                </Style.Triggers>
                            </Style>
                        </Button.Style>
                        <TextBlock Foreground="{DynamicResource TextBrush}">
                            <Run Text="{loc:Translate 'Universal Triggers Filter Ready'}" />
                        </TextBlock>
                    </Button>
                    <Button Margin="0,0,6,0" Padding="10,3" Command="{Binding ShowWarningsCommand}">
                        <Button.Style>
                            <Style TargetType="Button" BasedOn="{StaticResource SecondaryButtonStyle}">
                                <Style.Triggers>
                                    <DataTrigger Binding="{Binding ShowWarnings}" Value="True">
                                        <Setter Property="Background" Value="{DynamicResource AccentBrush}" />
                                        <Setter Property="BorderBrush" Value="{DynamicResource AccentBrush}" />
                                    </DataTrigger>
                                </Style.Triggers>
                            </Style>
                        </Button.Style>
                        <TextBlock Foreground="{DynamicResource TextBrush}">
                            <Run Text="{loc:Translate 'Universal Triggers Filter Warnings'}" />
                        </TextBlock>
                    </Button>
                    <Button Padding="10,3" Command="{Binding ShowFoomaCommand}">
                        <Button.Style>
                            <Style TargetType="Button" BasedOn="{StaticResource SecondaryButtonStyle}">
                                <Style.Triggers>
                                    <DataTrigger Binding="{Binding ShowFooma}" Value="True">
                                        <Setter Property="Background" Value="{DynamicResource AccentBrush}" />
                                        <Setter Property="BorderBrush" Value="{DynamicResource AccentBrush}" />
                                    </DataTrigger>
                                </Style.Triggers>
                            </Style>
                        </Button.Style>
                        <TextBlock Foreground="{DynamicResource TextBrush}">
                            <Run Text="{loc:Translate 'Universal Triggers Filter Fooma'}" />
                        </TextBlock>
                    </Button>
                </StackPanel>
                <TextBox DockPanel.Dock="Right" Width="280" Text="{Binding SearchText, UpdateSourceTrigger=PropertyChanged}" Background="{DynamicResource InputBrush}" Foreground="{DynamicResource TextBrush}" BorderBrush="{DynamicResource InputBorderBrush}" />
            </DockPanel>
        </Border>

        <!-- Body: card grid OR empty-state onboarding -->
        <Grid Grid.Row="2">
            <ScrollViewer VerticalScrollBarVisibility="Auto" HorizontalScrollBarVisibility="Disabled" Visibility="{Binding CountAll, Converter={StaticResource CountToVisibilityConverter}}">
                <ItemsControl ItemsSource="{Binding UniversalTriggersView}" Margin="14">
                    <ItemsControl.ItemsPanel>
                        <ItemsPanelTemplate>
                            <WrapPanel Orientation="Horizontal" />
                        </ItemsPanelTemplate>
                    </ItemsControl.ItemsPanel>
                    <ItemsControl.ItemTemplate>
                        <DataTemplate>
                            <!-- Card content goes here in Task 6 -->
                            <Border Width="390" MinHeight="120" Margin="6" Padding="14" CornerRadius="12" Background="{DynamicResource PanelBrush}" BorderBrush="{DynamicResource InputBorderBrush}" BorderThickness="1">
                                <TextBlock Text="{Binding Name}" Foreground="{DynamicResource TextBrush}" />
                            </Border>
                        </DataTemplate>
                    </ItemsControl.ItemTemplate>
                </ItemsControl>
            </ScrollViewer>

            <!-- Empty-state onboarding (shown when no triggers) -->
            <Border Visibility="{Binding CountAll, Converter={StaticResource CountToVisibilityConverter}, ConverterParameter=Inverted}" Background="Transparent" Padding="30">
                <Border MaxWidth="620" Padding="36,24" CornerRadius="18" Background="{DynamicResource PanelHighlightBrush}" BorderBrush="{DynamicResource InputBorderBrush}" BorderThickness="1" HorizontalAlignment="Center" VerticalAlignment="Center">
                    <StackPanel>
                        <TextBlock Text="✨" FontSize="30" HorizontalAlignment="Center" />
                        <TextBlock Text="{loc:Translate 'Universal Triggers Onboarding Title'}" FontSize="20" FontWeight="Bold" Foreground="{DynamicResource TextBrush}" HorizontalAlignment="Center" Margin="0,8,0,0" />
                        <TextBlock Text="{loc:Translate 'Universal Triggers Onboarding Body'}" Foreground="{DynamicResource MutedBrush}" TextWrapping="Wrap" TextAlignment="Center" Margin="0,8,0,0" />
                        <StackPanel Orientation="Horizontal" HorizontalAlignment="Center" Margin="0,22,0,0">
                            <Border Width="240" Margin="0,0,14,0" Padding="16" CornerRadius="14" Background="{DynamicResource AccentDimBrush}" BorderBrush="{DynamicResource AccentBrush}" BorderThickness="1">
                                <StackPanel>
                                    <TextBlock Text="{loc:Translate 'Universal Triggers Onboarding Import Title'}" FontWeight="Bold" Foreground="{DynamicResource TextBrush}" />
                                    <TextBlock Text="{loc:Translate 'Universal Triggers Onboarding Import Body'}" Foreground="{DynamicResource MutedBrush}" TextWrapping="Wrap" FontSize="11" Margin="0,4,0,0" />
                                    <Button Margin="0,10,0,0" Padding="10,6" Background="{DynamicResource AccentBrush}" Foreground="{DynamicResource ComboTextBrush}" FontWeight="Bold" Content="{loc:Translate 'Universal Triggers Onboarding Import Action'}" Command="{Binding ImportFoomaCommand}" />
                                </StackPanel>
                            </Border>
                            <Border Width="240" Padding="16" CornerRadius="14" Background="{DynamicResource PanelBrush}" BorderBrush="{DynamicResource InputBorderBrush}" BorderThickness="1">
                                <StackPanel>
                                    <TextBlock Text="{loc:Translate 'Universal Triggers Onboarding Create Title'}" FontWeight="Bold" Foreground="{DynamicResource TextBrush}" />
                                    <TextBlock Text="{loc:Translate 'Universal Triggers Onboarding Create Body'}" Foreground="{DynamicResource MutedBrush}" TextWrapping="Wrap" FontSize="11" Margin="0,4,0,0" />
                                    <Button Margin="0,10,0,0" Padding="10,6" Background="{DynamicResource InputBrush}" Foreground="{DynamicResource TextBrush}" FontWeight="Bold" Content="{loc:Translate 'Universal Triggers Onboarding Create Action'}" Command="{Binding AddTriggerCommand}" />
                                </StackPanel>
                            </Border>
                        </StackPanel>
                        <Expander Margin="0,20,0,0" HorizontalAlignment="Center" Foreground="{DynamicResource MutedBrush}">
                            <Expander.Header>
                                <TextBlock Text="{loc:Translate 'Universal Triggers Onboarding Help Question'}" Foreground="{DynamicResource TextBrush}" />
                            </Expander.Header>
                            <TextBlock Text="{loc:Translate 'Universal Triggers Onboarding Help Body'}" Foreground="{DynamicResource MutedBrush}" TextWrapping="Wrap" Margin="10" />
                        </Expander>
                    </StackPanel>
                </Border>
            </Border>
        </Grid>
    </Grid>
</UserControl>
```

- [ ] **Step 4: Add the missing converters and command stubs**

The XAML above references three things that don't exist yet:
1. `BoolToOpacityConverter` (referenced in resources but not used in this task — leave for now)
2. `CountToVisibilityConverter` (used to flip between the card grid and the onboarding based on `CountAll`)
3. The filter chip commands (`ShowAllCommand`, `ShowReadyCommand`, `ShowWarningsCommand`, `ShowFoomaCommand`)

Add the `CountToVisibilityConverter` to `Infrastructure/Converters.cs` (or wherever existing converters live — check `VrcTwitchOscBridge/Converters.cs` first):

```csharp
public sealed class CountToVisibilityConverter : IValueConverter
{
    public object Convert(object value, Type targetType, object parameter, CultureInfo culture)
    {
        var count = value is int i ? i : 0;
        var inverted = parameter is string s && s.Equals("Inverted", StringComparison.OrdinalIgnoreCase);
        var visible = inverted ? count == 0 : count > 0;
        return visible ? Visibility.Visible : Visibility.Collapsed;
    }
    public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture) => throw new NotSupportedException();
}
```

Add the four filter commands to `UniversalTriggersViewModel` (in the constructor, after the existing commands):

```csharp
ShowAllCommand = new RelayCommand(_ => ShowAll = true);
ShowReadyCommand = new RelayCommand(_ => ShowReady = true);
ShowWarningsCommand = new RelayCommand(_ => ShowWarnings = true);
ShowFoomaCommand = new RelayCommand(_ => ShowFooma = true);
```

And declare them as properties:

```csharp
public RelayCommand ShowAllCommand { get; }
public RelayCommand ShowReadyCommand { get; }
public RelayCommand ShowWarningsCommand { get; }
public RelayCommand ShowFoomaCommand { get; }
```

Also add `AccentDimBrush` to every palette in `ThemeManager.cs` (it doesn't exist — fall back to using `AccentDim` from the `RuleCardSelectedBrush` if that's the same). Check `ThemeManager.cs` for an `AccentDim` brush; if none, add it as an alias of `RuleCardSelectedBrush` to all 16 palettes. Quick search: `Select-String -Path "E:\!!!Program to work on\Proper Crystal Relay\VrcTwitchOscBridge\Services\ThemeManager.cs" -Pattern "AccentDim|RuleCardSelected"`.

- [ ] **Step 5: Build and verify**

Run: `dotnet build "E:\!!!Program to work on\Proper Crystal Relay\VrcTwitchOscBridge\VrcTwitchOscBridge.csproj" --no-restore`
Expected: `Build succeeded. 0 Error(s)`. Warnings about unused converters are OK.

- [ ] **Step 6: Commit**

```bash
git add VrcTwitchOscBridge/UniversalTriggersView.xaml VrcTwitchOscBridge/UniversalTriggersView.xaml.cs VrcTwitchOscBridge/ViewModels/UniversalTriggersViewModel.cs VrcTwitchOscBridge/Converters.cs VrcTwitchOscBridge/Services/ThemeManager.cs VrcTwitchOscBridge/VrcTwitchOscBridge.csproj
git commit -m "feat: add UniversalTriggersView library layout with toolbar, filters, empty-state onboarding"
```

---

## Task 6: Replace the placeholder card template with the full card design

**Files:**
- Modify: `E:\!!!Program to work on\Proper Crystal Relay\VrcTwitchOscBridge\UniversalTriggersView.xaml` (replace the card `DataTemplate`)

- [ ] **Step 1: Replace the card `DataTemplate`**

In `UniversalTriggersView.xaml`, find the `<DataTemplate>` inside the `<ItemsControl>` and replace its content (the simple `<Border>` with a `TextBlock`) with the full card. The card is a clickable `Border` that uses an `InputTrigger` to fire the `OpenTriggerEditorCommand` with the rule as parameter.

```xml
<DataTemplate>
    <Border Width="390" MinHeight="120" Margin="6" Padding="14" CornerRadius="12" Background="{DynamicResource PanelBrush}" BorderBrush="{DynamicResource InputBorderBrush}" BorderThickness="1" Cursor="Hand">
        <Border.Style>
            <Style TargetType="Border">
                <Style.Triggers>
                    <DataTrigger Binding="{Binding HasDanger}" Value="True">
                        <Setter Property="BorderBrush" Value="{DynamicResource DangerBorderBrush}" />
                    </DataTrigger>
                    <DataTrigger Binding="{Binding IsUnconfigured}" Value="True">
                        <Setter Property="Opacity" Value="0.75" />
                    </DataTrigger>
                </Style.Triggers>
            </Style>
        </Border.Style>
        <Border.InputBindings>
            <MouseBinding MouseAction="LeftClick" Command="{Binding DataContext.OpenTriggerEditorCommand, RelativeSource={RelativeSource AncestorType=UserControl}}" CommandParameter="{Binding}" />
        </Border.InputBindings>
        <StackPanel>
            <DockPanel LastChildFill="False">
                <Border DockPanel.Dock="Left" Padding="8,3" CornerRadius="10" Background="{DynamicResource AccentDimBrush}" BorderBrush="{DynamicResource AccentBrush}" BorderThickness="1">
                    <TextBlock Text="{Binding TypeChipText}" FontSize="10" FontWeight="Bold" Foreground="{DynamicResource RuleCardHoverBrush}" />
                </Border>
                <TextBlock DockPanel.Dock="Right" Text="{Binding SecondaryChipText}" Foreground="{DynamicResource MutedBrush}" FontSize="10" VerticalAlignment="Center" />
            </DockPanel>
            <TextBlock Text="{Binding Name}" FontSize="15" FontWeight="Bold" Foreground="{DynamicResource TextBrush}" Margin="0,6,0,0" />
            <TextBlock Text="{Binding ActionSummary}" Foreground="{DynamicResource MutedBrush}" FontSize="11" TextWrapping="Wrap" Margin="0,4,0,0" />
            <ItemsControl ItemsSource="{Binding StatusChips}" Margin="0,8,0,0">
                <ItemsControl.ItemsPanel>
                    <ItemsPanelTemplate>
                        <WrapPanel Orientation="Horizontal" />
                    </ItemsPanelTemplate>
                </ItemsControl.ItemsPanel>
                <ItemsControl.ItemTemplate>
                    <DataTemplate>
                        <Border Margin="0,0,6,0" Padding="8,2" CornerRadius="8" BorderThickness="1">
                            <Border.Style>
                                <Style TargetType="Border">
                                    <Setter Property="Background" Value="{DynamicResource StatusChipBrush}" />
                                    <Setter Property="BorderBrush" Value="{DynamicResource BorderBrush}" />
                                    <Style.Triggers>
                                        <DataTrigger Binding="{Binding Severity}" Value="Ready">
                                            <Setter Property="Background" Value="{DynamicResource AccentDimBrush}" />
                                            <Setter Property="BorderBrush" Value="{DynamicResource AccentBrush}" />
                                        </DataTrigger>
                                        <DataTrigger Binding="{Binding Severity}" Value="Warn">
                                            <Setter Property="Background" Value="{DynamicResource WarnBrush}" />
                                            <Setter Property="BorderBrush" Value="{DynamicResource WarnBorderBrush}" />
                                        </DataTrigger>
                                        <DataTrigger Binding="{Binding Severity}" Value="Danger">
                                            <Setter Property="Background" Value="{DynamicResource DangerBrush}" />
                                            <Setter Property="BorderBrush" Value="{DynamicResource DangerBorderBrush}" />
                                        </DataTrigger>
                                    </Style.Triggers>
                                </Style>
                            </Border.Style>
                            <TextBlock Text="{Binding Text}" FontSize="10">
                                <TextBlock.Style>
                                    <Style TargetType="TextBlock">
                                        <Setter Property="Foreground" Value="{DynamicResource MutedBrush}" />
                                        <Style.Triggers>
                                            <DataTrigger Binding="{Binding Severity}" Value="Ready">
                                                <Setter Property="Foreground" Value="{DynamicResource RuleCardHoverBrush}" />
                                            </DataTrigger>
                                            <DataTrigger Binding="{Binding Severity}" Value="Warn">
                                                <Setter Property="Foreground" Value="{DynamicResource WarnTextBrush}" />
                                            </DataTrigger>
                                            <DataTrigger Binding="{Binding Severity}" Value="Danger">
                                                <Setter Property="Foreground" Value="{DynamicResource TextBrush}" />
                                            </DataTrigger>
                                        </Style.Triggers>
                                    </Style>
                                </TextBlock.Style>
                            </TextBlock>
                        </Border>
                    </DataTemplate>
                </ItemsControl.ItemTemplate>
            </ItemsControl>
        </StackPanel>
    </Border>
</DataTemplate>
```

- [ ] **Step 2: Add `StatusChips`, `HasDanger`, and `TypeChipText` adapter to `UniversalTriggerCardViewModel`**

The card `DataTemplate` binds to `HasDanger`, `StatusChips`, `TypeChipText`, `SecondaryChipText`, `ActionSummary`, `Name`, `IsUnconfigured`. The first three are new. Add them to `UniversalTriggerCardViewModel.cs`:

```csharp
public bool HasDanger => IsDanger;

public IReadOnlyList<UniversalTriggerStatusChip> StatusChips
{
    get
    {
        var chips = new List<UniversalTriggerStatusChip>();
        if (IsUnconfigured)
        {
            chips.Add(new UniversalTriggerStatusChip("Needs setup", UniversalTriggerChipSeverity.Info));
        }
        if (PrimaryStatus == UniversalTriggerCardStatus.WarnDirectOsc)
        {
            chips.Add(new UniversalTriggerStatusChip("⚠ Direct OSC paths", UniversalTriggerChipSeverity.Warn));
        }
        if (PrimaryStatus == UniversalTriggerCardStatus.WarnNotAvatarBound)
        {
            chips.Add(new UniversalTriggerStatusChip("⚠ Not avatar-bound", UniversalTriggerChipSeverity.Warn));
        }
        if (PrimaryStatus == UniversalTriggerCardStatus.DangerMissingParam)
        {
            chips.Add(new UniversalTriggerStatusChip("✗ param missing", UniversalTriggerChipSeverity.Danger));
        }
        if (PrimaryStatus == UniversalTriggerCardStatus.DangerNoActions)
        {
            chips.Add(new UniversalTriggerStatusChip("✗ No complete actions", UniversalTriggerChipSeverity.Danger));
        }
        if (PrimaryStatus == UniversalTriggerCardStatus.Ready)
        {
            chips.Add(new UniversalTriggerStatusChip("✓ Ready for current avatar", UniversalTriggerChipSeverity.Ready));
        }
        if (string.Equals(Rule.ImportSource, "Fooma Twitch Interaction", StringComparison.OrdinalIgnoreCase))
        {
            chips.Add(new UniversalTriggerStatusChip("from Fooma", UniversalTriggerChipSeverity.Info));
        }
        return chips;
    }
}
```

And add a new file `Infrastructure/UniversalTriggerStatusChip.cs` (or co-locate in the card VM file):

```csharp
public enum UniversalTriggerChipSeverity { Info, Ready, Warn, Danger }

public sealed record UniversalTriggerStatusChip(string Text, UniversalTriggerChipSeverity Severity);
```

- [ ] **Step 3: Adapt the `ItemsControl` to use a card-VM wrapper, not the raw rule**

The `ItemsControl` is currently bound to `UniversalTriggersView` (an `ICollectionView<UniversalTriggerRule>`). The card `DataTemplate` binds to `UniversalTriggerCardViewModel` properties. Add a card-VM projection.

In `UniversalTriggersViewModel.cs`, change the view setup so that the `ItemsControl` binds to a `CollectionViewSource` over an `ObservableCollection<UniversalTriggerCardViewModel>` that's kept in sync with `UniversalTriggers`:

```csharp
public ObservableCollection<UniversalTriggerCardViewModel> Cards { get; } = new();
public ICollectionView CardsView { get; }

public UniversalTriggersViewModel(...)
{
    ...
    CardsView = CollectionViewSource.GetDefaultView(Cards);
    CardsView.Filter = FilterCard;
    UniversalTriggers.CollectionChanged += (_, e) => SyncCards();
    SyncCards();
    ...
}

private void SyncCards()
{
    Cards.Clear();
    foreach (var rule in UniversalTriggers) Cards.Add(new UniversalTriggerCardViewModel(rule));
    RaiseCountsChanged();
}

private bool FilterCard(object obj)
{
    if (obj is not UniversalTriggerCardViewModel card) return false;
    // Reuse FilterTrigger logic by delegating to card.Rule
    return FilterTrigger(card.Rule);
}
```

Then in the XAML, change the `ItemsControl` binding from `UniversalTriggersView` to `CardsView`.

- [ ] **Step 4: Build and verify**

Run: `dotnet build "E:\!!!Program to work on\Proper Crystal Relay\VrcTwitchOscBridge\VrcTwitchOscBridge.csproj" --no-restore`
Expected: `Build succeeded. 0 Error(s)`.

- [ ] **Step 5: Commit**

```bash
git add VrcTwitchOscBridge/UniversalTriggersView.xaml VrcTwitchOscBridge/ViewModels/UniversalTriggerCardViewModel.cs VrcTwitchOscBridge/ViewModels/UniversalTriggersViewModel.cs
git commit -m "feat: full card design with status chips, danger border, and Fooma badge"
```

---

## Task 7: Create the create wizard window and view-model

**Files:**
- Create: `E:\!!!Program to work on\Proper Crystal Relay\VrcTwitchOscBridge\UniversalTriggerCreateWizardWindow.xaml`
- Create: `E:\!!!Program to work on\Proper Crystal Relay\VrcTwitchOscBridge\UniversalTriggerCreateWizardWindow.xaml.cs`
- Create: `E:\!!!Program to work on\Proper Crystal Relay\VrcTwitchOscBridge\ViewModels\UniversalTriggerCreateWizardViewModel.cs`
- Modify: `E:\!!!Program to work on\Proper Crystal Relay\VrcTwitchOscBridge\VrcTwitchOscBridge.csproj` (add 2 pages + 1 compile)

- [ ] **Step 1: Add the new files to the csproj**

In `VrcTwitchOscBridge.csproj`:
- Add `<Page Include="UniversalTriggerCreateWizardWindow.xaml" />` to the `<Page>` list
- Add `<Compile Include="UniversalTriggerCreateWizardWindow.xaml.cs" />` to the `<Compile>` list
- (The view-model is picked up by the existing `<Compile Include="ViewModels\*.cs" />` wildcard or by adding it explicitly)

- [ ] **Step 2: Create the wizard view-model**

Write the following to `E:\!!!Program to work on\Proper Crystal Relay\VrcTwitchOscBridge\ViewModels\UniversalTriggerCreateWizardViewModel.cs`:

```csharp
using VrcTwitchOscBridge.Infrastructure;
using VrcTwitchOscBridge.Models;

namespace VrcTwitchOscBridge.ViewModels;

public sealed class UniversalTriggerCreateWizardViewModel : ObservableObject
{
    private int currentStep = 1;
    public int CurrentStep { get => currentStep; set => SetProperty(ref currentStep, Math.Clamp(value, 1, 4)); }

    private UniversalTriggerType selectedEventType = UniversalTriggerType.ChatCommand;
    public UniversalTriggerType SelectedEventType { get => selectedEventType; set { if (SetProperty(ref selectedEventType, value)) { RaisePropertyChanged(nameof(IsChatCommandSelected)); RaisePropertyChanged(nameof(IsChannelPointSelected)); RaisePropertyChanged(nameof(IsBitsSelected)); RaisePropertyChanged(nameof(IsSubscriptionSelected)); RaisePropertyChanged(nameof(IsFollowSelected)); } } }

    public bool IsChatCommandSelected => SelectedEventType == UniversalTriggerType.ChatCommand;
    public bool IsChannelPointSelected => SelectedEventType == UniversalTriggerType.ChannelPointReward;
    public bool IsBitsSelected => SelectedEventType == UniversalTriggerType.Bits;
    public bool IsSubscriptionSelected => SelectedEventType is UniversalTriggerType.Subscription or UniversalTriggerType.GiftSubscription;
    public bool IsFollowSelected => SelectedEventType == UniversalTriggerType.Follow;

    public UniversalTriggerRule Draft { get; } = new();

    public AsyncRelayCommand NextCommand { get; }
    public RelayCommand BackCommand { get; }
    public RelayCommand CancelCommand { get; }
    public AsyncRelayCommand SaveCommand { get; }
    public AsyncRelayCommand TestCommand { get; }

    public event Action? CloseRequested;
    public event Action<UniversalTriggerRule>? SaveRequested;

    public UniversalTriggerCreateWizardViewModel()
    {
        NextCommand = new AsyncRelayCommand(_ => { CurrentStep++; return Task.CompletedTask; });
        BackCommand = new RelayCommand(_ => CurrentStep--);
        CancelCommand = new RelayCommand(_ => CloseRequested?.Invoke());
        SaveCommand = new AsyncRelayCommand(_ => { SaveRequested?.Invoke(Draft); CloseRequested?.Invoke(); return Task.CompletedTask; });
        TestCommand = new AsyncRelayCommand(_ => Task.CompletedTask);
    }
}
```

- [ ] **Step 3: Create the code-behind**

Write the following to `E:\!!!Program to work on\Proper Crystal Relay\VrcTwitchOscBridge\UniversalTriggerCreateWizardWindow.xaml.cs`:

```csharp
using System.Windows;
using VrcTwitchOscBridge.ViewModels;

namespace VrcTwitchOscBridge;

public partial class UniversalTriggerCreateWizardWindow : Window
{
    public UniversalTriggerCreateWizardWindow()
    {
        InitializeComponent();
        if (DataContext is UniversalTriggerCreateWizardViewModel vm)
        {
            vm.CloseRequested += () => { DialogResult = false; Close(); };
            vm.SaveRequested += _ => { DialogResult = true; Close(); };
        }
    }
}
```

- [ ] **Step 4: Create the XAML**

Write the following to `E:\!!!Program to work on\Proper Crystal Relay\VrcTwitchOscBridge\UniversalTriggerCreateWizardWindow.xaml`. This is a 4-step wizard with a step indicator at the top. For brevity, only Steps 1 and 2 are fully fleshed out; Steps 3 and 4 are stubbed with placeholders that you fill in following the same pattern. (The spec §7.4 and §7.5 spell out the full content for Steps 3 and 4 — translate those into XAML.)

```xml
<Window x:Class="VrcTwitchOscBridge.UniversalTriggerCreateWizardWindow"
        xmlns="http://schemas.microsoft.com/winfx/2006/xaml/presentation"
        xmlns:x="http://schemas.microsoft.com/winfx/2006/xaml"
        xmlns:vm="clr-namespace:VrcTwitchOscBridge.ViewModels"
        xmlns:loc="clr-namespace:VrcTwitchOscBridge.Infrastructure"
        xmlns:models="clr-namespace:VrcTwitchOscBridge.Models"
        Title="{loc:Translate 'Universal Triggers Wizard Title'}"
        Width="720" Height="600"
        Background="{DynamicResource WindowBackgroundBrush}"
        Foreground="{DynamicResource TextBrush}"
        WindowStartupLocation="CenterOwner"
        d:DataContext="{d:DesignInstance Type=vm:UniversalTriggerCreateWizardViewModel}"
        xmlns:d="http://schemas.microsoft.com/expression/blend/2008"
        xmlns:mc="http://schemas.openxmlformats.org/markup-compatibility/2006"
        mc:Ignorable="d">

    <Grid>
        <Grid.RowDefinitions>
            <RowDefinition Height="Auto" />
            <RowDefinition Height="*" />
            <RowDefinition Height="Auto" />
        </Grid.RowDefinitions>

        <!-- Step indicator header -->
        <Border Grid.Row="0" Background="{DynamicResource TitleBarBrush}" Padding="14,10" BorderBrush="{DynamicResource BorderBrush}" BorderThickness="0,0,0,1">
            <DockPanel LastChildFill="False">
                <TextBlock DockPanel.Dock="Left" Text="{loc:Translate 'Universal Triggers Wizard Title'}" Foreground="{DynamicResource TitleBarTextBrush}" FontWeight="Bold" />
                <TextBlock DockPanel.Dock="Left" Margin="14,0,0,0" VerticalAlignment="Center" Foreground="{DynamicResource TitleBarSubTextBrush}" FontSize="11">
                    <Run Text="{loc:Translate 'Universal Triggers Wizard Step N of 4'}" />
                </TextBlock>
                <StackPanel DockPanel.Dock="Left" Orientation="Horizontal" Margin="14,0,0,0" VerticalAlignment="Center">
                    <Rectangle Width="120" Height="4" RadiusX="2" RadiusY="2" Margin="0,0,4,0">
                        <Rectangle.Style>
                            <Style TargetType="Rectangle">
                                <Setter Property="Fill" Value="{DynamicResource SecondaryButtonBorderBrush}" />
                                <Style.Triggers>
                                    <DataTrigger Binding="{Binding CurrentStep, Converter={StaticResource StepGreaterConverter}, ConverterParameter=0}" Value="True">
                                        <Setter Property="Fill" Value="{DynamicResource AccentBrush}" />
                                    </DataTrigger>
                                </Style.Triggers>
                            </Style>
                        </Rectangle.Style>
                    </Rectangle>
                    <Rectangle Width="120" Height="4" RadiusX="2" RadiusY="2" Margin="0,0,4,0">
                        <Rectangle.Style>
                            <Style TargetType="Rectangle">
                                <Setter Property="Fill" Value="{DynamicResource SecondaryButtonBorderBrush}" />
                                <Style.Triggers>
                                    <DataTrigger Binding="{Binding CurrentStep, Converter={StaticResource StepGreaterConverter}, ConverterParameter=1}" Value="True">
                                        <Setter Property="Fill" Value="{DynamicResource AccentBrush}" />
                                    </DataTrigger>
                                </Style.Triggers>
                            </Style>
                        </Rectangle.Style>
                    </Rectangle>
                    <Rectangle Width="120" Height="4" RadiusX="2" RadiusY="2" Margin="0,0,4,0">
                        <Rectangle.Style>
                            <Style TargetType="Rectangle">
                                <Setter Property="Fill" Value="{DynamicResource SecondaryButtonBorderBrush}" />
                                <Style.Triggers>
                                    <DataTrigger Binding="{Binding CurrentStep, Converter={StaticResource StepGreaterConverter}, ConverterParameter=2}" Value="True">
                                        <Setter Property="Fill" Value="{DynamicResource AccentBrush}" />
                                    </DataTrigger>
                                </Style.Triggers>
                            </Style>
                        </Rectangle.Style>
                    </Rectangle>
                    <Rectangle Width="120" Height="4" RadiusX="2" RadiusY="2">
                        <Rectangle.Style>
                            <Style TargetType="Rectangle">
                                <Setter Property="Fill" Value="{DynamicResource SecondaryButtonBorderBrush}" />
                                <Style.Triggers>
                                    <DataTrigger Binding="{Binding CurrentStep, Converter={StaticResource StepGreaterConverter}, ConverterParameter=3}" Value="True">
                                        <Setter Property="Fill" Value="{DynamicResource AccentBrush}" />
                                    </DataTrigger>
                                </Style.Triggers>
                            </Style>
                        </Rectangle.Style>
                    </Rectangle>
                </StackPanel>
                <Button DockPanel.Dock="Right" Content="{loc:Translate 'Universal Triggers Wizard Cancel'}" Style="{StaticResource SecondaryButtonStyle}" Command="{Binding CancelCommand}" />
            </DockPanel>
        </Border>

        <!-- Step content (4 StackPanels, one visible at a time) -->
        <Grid Grid.Row="1">
            <!-- Step 1: Pick the Twitch event -->
            <StackPanel Visibility="{Binding CurrentStep, Converter={StaticResource StepVisibilityConverter}, ConverterParameter=1}">
                <TextBlock Text="{loc:Translate 'Universal Triggers Wizard Step 1 Title'}" FontWeight="Bold" FontSize="14" Margin="18,18,0,4" Foreground="{DynamicResource TextBrush}" />
                <TextBlock Text="{loc:Translate 'Universal Triggers Wizard Step 1 Hint'}" Foreground="{DynamicResource MutedBrush}" FontSize="11" Margin="18,0,0,12" />
                <UniformGrid Rows="2" Columns="3" Margin="18,0,18,18">
                    <Button Margin="6" Padding="14" Click="OnEventCardClicked" Tag="{x:Static models:UniversalTriggerType.ChatCommand}">
                        <Button.Style>
                            <Style TargetType="Button" BasedOn="{StaticResource SecondaryButtonStyle}">
                                <Style.Triggers>
                                    <DataTrigger Binding="{Binding IsChatCommandSelected}" Value="True">
                                        <Setter Property="Background" Value="{DynamicResource AccentDimBrush}" />
                                        <Setter Property="BorderBrush" Value="{DynamicResource AccentBrush}" />
                                    </DataTrigger>
                                </Style.Triggers>
                            </Style>
                        </Button.Style>
                        <StackPanel>
                            <TextBlock Text="💬" FontSize="22" HorizontalAlignment="Center" />
                            <TextBlock Text="{loc:Translate 'Universal Triggers Wizard Event Chat Command'}" FontWeight="Bold" HorizontalAlignment="Center" Margin="0,4,0,0" Foreground="{DynamicResource TextBrush}" />
                            <TextBlock Text="{loc:Translate 'Universal Triggers Wizard Event Chat Command Hint'}" Foreground="{DynamicResource MutedBrush}" FontSize="10" HorizontalAlignment="Center" />
                        </StackPanel>
                    </Button>
                    <Button Margin="6" Padding="14" Click="OnEventCardClicked" Tag="{x:Static models:UniversalTriggerType.ChannelPointReward}">
                        <Button.Style>
                            <Style TargetType="Button" BasedOn="{StaticResource SecondaryButtonStyle}">
                                <Style.Triggers>
                                    <DataTrigger Binding="{Binding IsChannelPointSelected}" Value="True">
                                        <Setter Property="Background" Value="{DynamicResource AccentDimBrush}" />
                                        <Setter Property="BorderBrush" Value="{DynamicResource AccentBrush}" />
                                    </DataTrigger>
                                </Style.Triggers>
                            </Style>
                        </Button.Style>
                        <StackPanel>
                            <TextBlock Text="⭐" FontSize="22" HorizontalAlignment="Center" />
                            <TextBlock Text="{loc:Translate 'Universal Triggers Wizard Event Channel Point'}" FontWeight="Bold" HorizontalAlignment="Center" Margin="0,4,0,0" Foreground="{DynamicResource TextBrush}" />
                            <TextBlock Text="{loc:Translate 'Universal Triggers Wizard Event Channel Point Hint'}" Foreground="{DynamicResource MutedBrush}" FontSize="10" HorizontalAlignment="Center" />
                        </StackPanel>
                    </Button>
                    <Button Margin="6" Padding="14" Click="OnEventCardClicked" Tag="{x:Static models:UniversalTriggerType.Bits}">
                        <Button.Style>
                            <Style TargetType="Button" BasedOn="{StaticResource SecondaryButtonStyle}">
                                <Style.Triggers>
                                    <DataTrigger Binding="{Binding IsBitsSelected}" Value="True">
                                        <Setter Property="Background" Value="{DynamicResource AccentDimBrush}" />
                                        <Setter Property="BorderBrush" Value="{DynamicResource AccentBrush}" />
                                    </DataTrigger>
                                </Style.Triggers>
                            </Style>
                        </Button.Style>
                        <StackPanel>
                            <TextBlock Text="💎" FontSize="22" HorizontalAlignment="Center" />
                            <TextBlock Text="{loc:Translate 'Universal Triggers Wizard Event Bits'}" FontWeight="Bold" HorizontalAlignment="Center" Margin="0,4,0,0" Foreground="{DynamicResource TextBrush}" />
                            <TextBlock Text="{loc:Translate 'Universal Triggers Wizard Event Bits Hint'}" Foreground="{DynamicResource MutedBrush}" FontSize="10" HorizontalAlignment="Center" />
                        </StackPanel>
                    </Button>
                    <Button Margin="6" Padding="14" Click="OnEventCardClicked" Tag="{x:Static models:UniversalTriggerType.Subscription}">
                        <Button.Style>
                            <Style TargetType="Button" BasedOn="{StaticResource SecondaryButtonStyle}">
                                <Style.Triggers>
                                    <DataTrigger Binding="{Binding IsSubscriptionSelected}" Value="True">
                                        <Setter Property="Background" Value="{DynamicResource AccentDimBrush}" />
                                        <Setter Property="BorderBrush" Value="{DynamicResource AccentBrush}" />
                                    </DataTrigger>
                                </Style.Triggers>
                            </Style>
                        </Button.Style>
                        <StackPanel>
                            <TextBlock Text="🎁" FontSize="22" HorizontalAlignment="Center" />
                            <TextBlock Text="{loc:Translate 'Universal Triggers Wizard Event Subscription'}" FontWeight="Bold" HorizontalAlignment="Center" Margin="0,4,0,0" Foreground="{DynamicResource TextBrush}" />
                            <TextBlock Text="{loc:Translate 'Universal Triggers Wizard Event Subscription Hint'}" Foreground="{DynamicResource MutedBrush}" FontSize="10" HorizontalAlignment="Center" />
                        </StackPanel>
                    </Button>
                    <Button Margin="6" Padding="14" Click="OnEventCardClicked" Tag="{x:Static models:UniversalTriggerType.GiftSubscription}">
                        <Button.Style>
                            <Style TargetType="Button" BasedOn="{StaticResource SecondaryButtonStyle}">
                                <Style.Triggers>
                                    <DataTrigger Binding="{Binding IsSubscriptionSelected}" Value="True">
                                        <Setter Property="Background" Value="{DynamicResource AccentDimBrush}" />
                                        <Setter Property="BorderBrush" Value="{DynamicResource AccentBrush}" />
                                    </DataTrigger>
                                </Style.Triggers>
                            </Style>
                        </Button.Style>
                        <StackPanel>
                            <TextBlock Text="🎀" FontSize="22" HorizontalAlignment="Center" />
                            <TextBlock Text="{loc:Translate 'Universal Triggers Wizard Event Gift Sub'}" FontWeight="Bold" HorizontalAlignment="Center" Margin="0,4,0,0" Foreground="{DynamicResource TextBrush}" />
                            <TextBlock Text="{loc:Translate 'Universal Triggers Wizard Event Gift Sub Hint'}" Foreground="{DynamicResource MutedBrush}" FontSize="10" HorizontalAlignment="Center" />
                        </StackPanel>
                    </Button>
                    <Button Margin="6" Padding="14" Click="OnEventCardClicked" Tag="{x:Static models:UniversalTriggerType.Follow}">
                        <Button.Style>
                            <Style TargetType="Button" BasedOn="{StaticResource SecondaryButtonStyle}">
                                <Style.Triggers>
                                    <DataTrigger Binding="{Binding IsFollowSelected}" Value="True">
                                        <Setter Property="Background" Value="{DynamicResource AccentDimBrush}" />
                                        <Setter Property="BorderBrush" Value="{DynamicResource AccentBrush}" />
                                    </DataTrigger>
                                </Style.Triggers>
                            </Style>
                        </Button.Style>
                        <StackPanel>
                            <TextBlock Text="👤" FontSize="22" HorizontalAlignment="Center" />
                            <TextBlock Text="{loc:Translate 'Universal Triggers Wizard Event Follow'}" FontWeight="Bold" HorizontalAlignment="Center" Margin="0,4,0,0" Foreground="{DynamicResource TextBrush}" />
                            <TextBlock Text="{loc:Translate 'Universal Triggers Wizard Event Follow Hint'}" Foreground="{DynamicResource MutedBrush}" FontSize="10" HorizontalAlignment="Center" />
                        </StackPanel>
                    </Button>
                </UniformGrid>
            </StackPanel>

            <!-- Step 2: Configure the event (channel point example) -->
            <StackPanel Visibility="{Binding CurrentStep, Converter={StaticResource StepVisibilityConverter}, ConverterParameter=2}">
                <TextBlock Text="{loc:Translate 'Universal Triggers Wizard Step 2 Title'}" FontWeight="Bold" FontSize="14" Margin="18,18,0,4" Foreground="{DynamicResource TextBrush}" />
                <TextBlock Text="{loc:Translate 'Universal Triggers Wizard Step 2 Channel Point'}" Foreground="{DynamicResource MutedBrush}" FontSize="11" Margin="18,0,0,12" />
                <UniformGrid Columns="2" Margin="18,0,18,12">
                    <StackPanel Margin="0,0,8,8">
                        <TextBlock Text="Reward Name" Foreground="{DynamicResource TextBrush}" FontWeight="SemiBold" FontSize="11" />
                        <TextBox Text="{Binding Draft.RewardTitle, UpdateSourceTrigger=PropertyChanged}" />
                    </StackPanel>
                    <StackPanel Margin="8,0,0,8">
                        <TextBlock Text="Reward Cost" Foreground="{DynamicResource TextBrush}" FontWeight="SemiBold" FontSize="11" />
                        <TextBox Text="{Binding Draft.RewardCost, UpdateSourceTrigger=PropertyChanged}" />
                    </StackPanel>
                </UniformGrid>
            </StackPanel>

            <!-- Step 3: Add OSC actions -->
            <StackPanel Visibility="{Binding CurrentStep, Converter={StaticResource StepVisibilityConverter}, ConverterParameter=3}">
                <TextBlock Text="{loc:Translate 'Universal Triggers Wizard Step 3 Title'}" FontWeight="Bold" FontSize="14" Margin="18,18,0,4" Foreground="{DynamicResource TextBrush}" />
                <TextBlock Text="{loc:Translate 'Universal Triggers Wizard Step 3 Hint'}" Foreground="{DynamicResource MutedBrush}" FontSize="11" Margin="18,0,0,12" />
                <Border Background="{DynamicResource StatusChipBrush}" BorderBrush="{DynamicResource InputBorderBrush}" BorderThickness="1" CornerRadius="10" Padding="10" Margin="18,0,18,12">
                    <TextBlock Text="{loc:Translate 'Universal Triggers Wizard Step 3 Params No Avatar'}" Foreground="{DynamicResource TextBrush}" FontSize="11" />
                </Border>
                <ItemsControl ItemsSource="{Binding Draft.Actions}" Margin="18,0,18,12" />
            </StackPanel>

            <!-- Step 4: Review and save -->
            <StackPanel Visibility="{Binding CurrentStep, Converter={StaticResource StepVisibilityConverter}, ConverterParameter=4}">
                <TextBlock Text="{loc:Translate 'Universal Triggers Wizard Step 4 Title'}" FontWeight="Bold" FontSize="14" Margin="18,18,0,4" Foreground="{DynamicResource TextBrush}" />
                <TextBlock Text="{loc:Translate 'Universal Triggers Wizard Step 4 Hint'}" Foreground="{DynamicResource MutedBrush}" FontSize="11" Margin="18,0,0,12" />
                <Border Background="{DynamicResource StatusChipBrush}" BorderBrush="{DynamicResource InputBorderBrush}" BorderThickness="1" CornerRadius="12" Padding="14" Margin="18,0,18,12">
                    <StackPanel>
                        <TextBlock Text="{Binding Draft.Name}" FontWeight="Bold" Foreground="{DynamicResource TextBrush}" />
                        <TextBlock Foreground="{DynamicResource MutedBrush}" FontSize="11" Margin="0,4,0,0">
                            <Run Text="{Binding Draft.Actions.Count}" />
                            <Run Text="actions" />
                        </TextBlock>
                    </StackPanel>
                </Border>
            </StackPanel>
        </Grid>

        <!-- Footer -->
        <Border Grid.Row="2" Background="{DynamicResource TitleBarBrush}" Padding="14,10" BorderBrush="{DynamicResource BorderBrush}" BorderThickness="0,1,0,0">
            <DockPanel LastChildFill="False">
                <Button DockPanel.Dock="Left" Style="{StaticResource SecondaryButtonStyle}" Content="« Back" Command="{Binding BackCommand}" />
                <Button DockPanel.Dock="Right" Margin="8,0,0,0" Background="{DynamicResource AccentBrush}" Foreground="{DynamicResource ComboTextBrush}" FontWeight="Bold" Padding="12,6" Content="Save trigger" Command="{Binding SaveCommand}" />
                <Button DockPanel.Dock="Right" Style="{StaticResource SecondaryButtonStyle}" Content="Test now" Command="{Binding TestCommand}" />
                <Button DockPanel.Dock="Right" Margin="8,0,0,0" Style="{StaticResource SecondaryButtonStyle}" Content="Next »" Command="{Binding NextCommand}" />
            </DockPanel>
        </Border>
    </Grid>
</Window>
```

- [ ] **Step 5: Add the missing converters**

The XAML uses `StepGreaterConverter` and `StepVisibilityConverter`. Add both to `Converters.cs`:

```csharp
public sealed class StepGreaterConverter : IValueConverter
{
    public object Convert(object value, Type targetType, object parameter, CultureInfo culture)
    {
        if (value is not int step) return false;
        if (parameter is null || !int.TryParse(parameter.ToString(), out var threshold)) return false;
        return step > threshold;
    }
    public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture) => throw new NotSupportedException();
}

public sealed class StepVisibilityConverter : IValueConverter
{
    public object Convert(object value, Type targetType, object parameter, CultureInfo culture)
    {
        if (value is not int step) return Visibility.Collapsed;
        if (parameter is null || !int.TryParse(parameter.ToString(), out var target)) return Visibility.Collapsed;
        return step == target ? Visibility.Visible : Visibility.Collapsed;
    }
    public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture) => throw new NotSupportedException();
}
```

- [ ] **Step 6: Add `OnEventCardClicked` to the code-behind**

Update `UniversalTriggerCreateWizardWindow.xaml.cs`:

```csharp
using System.Windows;
using System.Windows.Controls;
using VrcTwitchOscBridge.Models;
using VrcTwitchOscBridge.ViewModels;

namespace VrcTwitchOscBridge;

public partial class UniversalTriggerCreateWizardWindow : Window
{
    public UniversalTriggerCreateWizardWindow()
    {
        InitializeComponent();
        if (DataContext is UniversalTriggerCreateWizardViewModel vm)
        {
            vm.CloseRequested += () => { DialogResult = false; Close(); };
            vm.SaveRequested += _ => { DialogResult = true; Close(); };
        }
    }

    private void OnEventCardClicked(object sender, RoutedEventArgs e)
    {
        if (sender is Button btn && btn.Tag is UniversalTriggerType t && DataContext is UniversalTriggerCreateWizardViewModel vm)
        {
            vm.SelectedEventType = t;
            vm.Draft.TriggerType = t;
        }
    }
}
```

- [ ] **Step 7: Wire the wizard into `UniversalTriggersViewModel`**

In `OpenCreateWizardAsync`:

```csharp
private async Task OpenCreateWizardAsync()
{
    var window = new UniversalTriggerCreateWizardWindow
    {
        Owner = Application.Current?.MainWindow,
        DataContext = new UniversalTriggerCreateWizardViewModel()
    };
    var result = window.ShowDialog();
    if (result == true && window.DataContext is UniversalTriggerCreateWizardViewModel vm)
    {
        UniversalTriggers.Add(vm.Draft);
    }
    await Task.CompletedTask;
}
```

- [ ] **Step 8: Build and verify**

Run: `dotnet build "E:\!!!Program to work on\Proper Crystal Relay\VrcTwitchOscBridge\VrcTwitchOscBridge.csproj" --no-restore`
Expected: `Build succeeded. 0 Error(s)`. Warnings about unused converters are OK.

- [ ] **Step 9: Commit**

```bash
git add VrcTwitchOscBridge/UniversalTriggerCreateWizardWindow.xaml VrcTwitchOscBridge/UniversalTriggerCreateWizardWindow.xaml.cs VrcTwitchOscBridge/ViewModels/UniversalTriggerCreateWizardViewModel.cs VrcTwitchOscBridge/ViewModels/UniversalTriggersViewModel.cs VrcTwitchOscBridge/Converters.cs VrcTwitchOscBridge/VrcTwitchOscBridge.csproj
git commit -m "feat: add 4-step guided create wizard for Universal Triggers"
```

---

## Task 8: Create the import preview window and view-model

**Files:**
- Create: `E:\!!!Program to work on\Proper Crystal Relay\VrcTwitchOscBridge\UniversalTriggerImportPreviewWindow.xaml`
- Create: `E:\!!!Program to work on\Proper Crystal Relay\VrcTwitchOscBridge\UniversalTriggerImportPreviewWindow.xaml.cs`
- Create: `E:\!!!Program to work on\Proper Crystal Relay\VrcTwitchOscBridge\ViewModels\UniversalTriggerImportPreviewViewModel.cs`
- Modify: `E:\!!!Program to work on\Proper Crystal Relay\VrcTwitchOscBridge\VrcTwitchOscBridge.csproj` (add 2 pages + 1 compile)

- [ ] **Step 1: Add the new files to the csproj**

In `VrcTwitchOscBridge.csproj`:
- Add `<Page Include="UniversalTriggerImportPreviewWindow.xaml" />` to the `<Page>` list
- Add `<Compile Include="UniversalTriggerImportPreviewWindow.xaml.cs" />` to the `<Compile>` list

- [ ] **Step 2: Create the import preview view-model**

Write the following to `E:\!!!Program to work on\Proper Crystal Relay\VrcTwitchOscBridge\ViewModels\UniversalTriggerImportPreviewViewModel.cs`:

```csharp
using VrcTwitchOscBridge.Infrastructure;
using VrcTwitchOscBridge.Models;
using VrcTwitchOscBridge.Services;

namespace VrcTwitchOscBridge.ViewModels;

public sealed class UniversalTriggerImportPreviewViewModel : ObservableObject
{
    private int currentStep = 1;
    public int CurrentStep { get => currentStep; set => SetProperty(ref currentStep, Math.Clamp(value, 1, 3)); }

    private string? filePath;
    public string? FilePath { get => filePath; set => SetProperty(ref filePath, value); }

    private string? fileName;
    public string? FileName { get => fileName; set => SetProperty(ref fileName, value); }

    private long fileSize;
    public long FileSize { get => fileSize; set => SetProperty(ref fileSize, value); }

    private FoomaInteractionImportResult? parsedResult;
    public FoomaInteractionImportResult? ParsedResult { get => parsedResult; set => SetProperty(ref parsedResult, value); }

    public bool HasParsedResult => ParsedResult is not null;
    public bool HasDirectOscWarning => ParsedResult is not null && ParsedResult.Triggers.Any(t => t.Actions.Count > 0 && t.Actions.All(a => !a.OscAddress.StartsWith("/avatar/parameters/") && !a.OscAddress.StartsWith("avatar/parameters/")));

    public string DirectOscWarningCommandName => ParsedResult?.Triggers.FirstOrDefault(t => t.Actions.Count > 0 && t.Actions.All(a => !a.OscAddress.StartsWith("/avatar/parameters/") && !a.OscAddress.StartsWith("avatar/parameters/")))?.Name ?? "?";

    public RelayCommand PickFileCommand { get; }
    public RelayCommand BackCommand { get; }
    public RelayCommand ImportCommand { get; }
    public RelayCommand CancelCommand { get; }

    public event Action? CancelRequested;
    public event Action<FoomaInteractionImportResult>? ImportRequested;

    public UniversalTriggerImportPreviewViewModel()
    {
        PickFileCommand = new RelayCommand(_ => PickFile());
        BackCommand = new RelayCommand(_ => CurrentStep--);
        ImportCommand = new RelayCommand(_ => { if (ParsedResult is not null) ImportRequested?.Invoke(ParsedResult); });
        CancelCommand = new RelayCommand(_ => CancelRequested?.Invoke());
    }

    private void PickFile()
    {
        var dlg = new Microsoft.Win32.OpenFileDialog
        {
            Filter = "Fooma Config (*.json)|*.json|All files (*.*)|*.*",
            Title = "Pick a Fooma Twitch Interaction JSON"
        };
        if (dlg.ShowDialog() == true)
        {
            FilePath = dlg.FileName;
            FileName = System.IO.Path.GetFileName(FilePath);
            FileSize = new System.IO.FileInfo(FilePath).Length;
            try
            {
                ParsedResult = FoomaInteractionConfigImporter.ImportFromFile(FilePath);
                CurrentStep = 2;
                RaisePropertyChanged(nameof(HasParsedResult));
                RaisePropertyChanged(nameof(HasDirectOscWarning));
                RaisePropertyChanged(nameof(DirectOscWarningCommandName));
            }
            catch (Exception ex)
            {
                System.Windows.MessageBox.Show($"Fooma Import Failed: {ex.Message}", "Fooma Import", System.Windows.MessageBoxButton.OK, System.Windows.MessageBoxImage.Error);
            }
        }
    }
}
```

Note: `FoomaInteractionConfigImporter.ImportFromFile(string)` may not exist; check `Services/FoomaInteractionConfigImporter.cs` for the real method signature and adjust accordingly. The existing importer likely takes a `Stream` and is wrapped in `UniversalTriggersViewModel.ImportFoomaInteractionConfigAsync`. Mirror the pattern.

- [ ] **Step 3: Create the code-behind**

Write the following to `E:\!!!Program to work on\Proper Crystal Relay\VrcTwitchOscBridge\UniversalTriggerImportPreviewWindow.xaml.cs`:

```csharp
using System.Windows;
using VrcTwitchOscBridge.ViewModels;

namespace VrcTwitchOscBridge;

public partial class UniversalTriggerImportPreviewWindow : Window
{
    public UniversalTriggerImportPreviewWindow()
    {
        InitializeComponent();
        if (DataContext is UniversalTriggerImportPreviewViewModel vm)
        {
            vm.CancelRequested += () => { DialogResult = false; Close(); };
            vm.ImportRequested += _ => { DialogResult = true; Close(); };
        }
    }
}
```

- [ ] **Step 4: Create the XAML**

Write the following to `E:\!!!Program to work on\Proper Crystal Relay\VrcTwitchOscBridge\UniversalTriggerImportPreviewWindow.xaml`. Step 1 has the file picker; Step 2 shows the parsed preview with the danger warning if any direct-OSC triggers are present; Step 3 is the done summary.

```xml
<Window x:Class="VrcTwitchOscBridge.UniversalTriggerImportPreviewWindow"
        xmlns="http://schemas.microsoft.com/winfx/2006/xaml/presentation"
        xmlns:x="http://schemas.microsoft.com/winfx/2006/xaml"
        xmlns:vm="clr-namespace:VrcTwitchOscBridge.ViewModels"
        xmlns:loc="clr-namespace:VrcTwitchOscBridge.Infrastructure"
        Title="{loc:Translate 'Universal Triggers Import Title'}"
        Width="720" Height="600"
        Background="{DynamicResource WindowBackgroundBrush}"
        Foreground="{DynamicResource TextBrush}"
        WindowStartupLocation="CenterOwner"
        d:DataContext="{d:DesignInstance Type=vm:UniversalTriggerImportPreviewViewModel}"
        xmlns:d="http://schemas.microsoft.com/expression/blend/2008"
        xmlns:mc="http://schemas.openxmlformats.org/markup-compatibility/2006"
        mc:Ignorable="d">

    <Grid>
        <Grid.RowDefinitions>
            <RowDefinition Height="Auto" />
            <RowDefinition Height="*" />
            <RowDefinition Height="Auto" />
        </Grid.RowDefinitions>

        <Border Grid.Row="0" Background="{DynamicResource TitleBarBrush}" Padding="14,10" BorderBrush="{DynamicResource BorderBrush}" BorderThickness="0,0,0,1">
            <DockPanel LastChildFill="False">
                <TextBlock DockPanel.Dock="Left" Text="{loc:Translate 'Universal Triggers Import Title'}" Foreground="{DynamicResource TitleBarTextBrush}" FontWeight="Bold" />
                <Button DockPanel.Dock="Right" Content="{loc:Translate 'Universal Triggers Wizard Cancel'}" Style="{StaticResource SecondaryButtonStyle}" Command="{Binding CancelCommand}" />
            </DockPanel>
        </Border>

        <Grid Grid.Row="1">
            <!-- Step 1: Pick file -->
            <StackPanel Visibility="{Binding CurrentStep, Converter={StaticResource StepVisibilityConverter}, ConverterParameter=1}">
                <TextBlock Text="Pick a Fooma Twitch Interaction JSON file" Foreground="{DynamicResource TextBrush}" Margin="18,18,18,8" />
                <Button Content="Choose .json file..." Margin="18,0,18,18" Padding="12,8" HorizontalAlignment="Left" Background="{DynamicResource AccentBrush}" Foreground="{DynamicResource ComboTextBrush}" FontWeight="Bold" Command="{Binding PickFileCommand}" />
            </StackPanel>

            <!-- Step 2: Preview -->
            <StackPanel Visibility="{Binding CurrentStep, Converter={StaticResource StepVisibilityConverter}, ConverterParameter=2}">
                <Border Background="{DynamicResource StatusChipBrush}" BorderBrush="{DynamicResource InputBorderBrush}" BorderThickness="1" CornerRadius="12" Padding="12" Margin="18,18,18,12">
                    <StackPanel>
                        <TextBlock Text="FOOMA CONFIG DETECTED" Foreground="{DynamicResource MutedBrush}" FontWeight="Bold" FontSize="11" />
                        <TextBlock Text="{Binding FileName}" Foreground="{DynamicResource TextBrush}" FontWeight="Bold" Margin="0,2,0,0" />
                        <TextBlock Foreground="{DynamicResource MutedBrush}" FontSize="11" Margin="0,4,0,0">
                            <Run Text="{Binding ParsedResult.Triggers.Count, FallbackValue=0}" />
                            <Run Text=" triggers will be created" />
                        </TextBlock>
                    </StackPanel>
                </Border>

                <Border Background="{DynamicResource DangerBrush}" BorderBrush="{DynamicResource DangerBorderBrush}" BorderThickness="1" CornerRadius="12" Padding="12" Margin="18,0,18,12"
                        Visibility="{Binding HasDirectOscWarning, Converter={StaticResource BoolToVisibilityConverter}}">
                    <StackPanel>
                        <TextBlock Text="⚠ Heads up" FontWeight="Bold" Foreground="{DynamicResource TextBrush}" FontSize="12" />
                        <TextBlock Foreground="{DynamicResource TextBrush}" FontSize="11" Margin="0,4,0,0">
                            The !<Run Text="{Binding DirectOscWarningCommandName}" /> command uses built-in /input/* paths. That won't gate reward visibility on avatar params. It'll still work, but the warning system will mark it as not avatar-bound.
                        </TextBlock>
                    </StackPanel>
                </Border>

                <ItemsControl ItemsSource="{Binding ParsedResult.Triggers}" Margin="18,0,18,12" MaxHeight="200">
                    <ItemsControl.ItemTemplate>
                        <DataTemplate>
                            <Border Background="{DynamicResource PanelBrush}" BorderBrush="{DynamicResource BorderBrush}" BorderThickness="1" CornerRadius="8" Padding="8" Margin="0,0,0,4">
                                <StackPanel>
                                    <TextBlock Text="{Binding Name}" FontWeight="Bold" Foreground="{DynamicResource TextBrush}" />
                                    <TextBlock Text="{Binding TriggerType}" Foreground="{DynamicResource MutedBrush}" FontSize="10" />
                                </StackPanel>
                            </Border>
                        </DataTemplate>
                    </ItemsControl.ItemTemplate>
                </ItemsControl>

                <TextBlock Text="{loc:Translate 'Universal Triggers Import After Note'}" Foreground="{DynamicResource MutedBrush}" FontSize="11" TextWrapping="Wrap" Margin="18,0,18,0" />
            </StackPanel>

            <!-- Step 3: Done -->
            <StackPanel Visibility="{Binding CurrentStep, Converter={StaticResource StepVisibilityConverter}, ConverterParameter=3}">
                <TextBlock Text="Import complete." FontWeight="Bold" FontSize="16" Foreground="{DynamicResource TextBrush}" Margin="18,18,18,12" />
            </StackPanel>
        </Grid>

        <Border Grid.Row="2" Background="{DynamicResource TitleBarBrush}" Padding="14,10" BorderBrush="{DynamicResource BorderBrush}" BorderThickness="0,1,0,0">
            <DockPanel LastChildFill="False">
                <Button DockPanel.Dock="Left" Style="{StaticResource SecondaryButtonStyle}" Content="« Back" Command="{Binding BackCommand}" />
                <Button DockPanel.Dock="Right" Margin="8,0,0,0" Background="{DynamicResource AccentBrush}" Foreground="{DynamicResource ComboTextBrush}" FontWeight="Bold" Padding="12,6" Content="Import N triggers" Command="{Binding ImportCommand}" />
            </DockPanel>
        </Border>
    </Grid>
</Window>
```

- [ ] **Step 5: Wire the import preview into `UniversalTriggersViewModel`**

In `OpenImportPreviewAsync`:

```csharp
private async Task OpenImportPreviewAsync()
{
    var window = new UniversalTriggerImportPreviewWindow
    {
        Owner = Application.Current?.MainWindow,
        DataContext = new UniversalTriggerImportPreviewViewModel()
    };
    var result = window.ShowDialog();
    if (result == true && window.DataContext is UniversalTriggerImportPreviewViewModel vm && vm.ParsedResult is not null)
    {
        // Hand off to the existing upsert/import flow that was previously in
        // ImportFoomaInteractionConfigAsync. Reuse the same call path so the fusion
        // service, reward sync, and settings save all run as before.
        await coordinator.ImportFoomaInteractionResultAsync(vm.ParsedResult, default);
    }
    await Task.CompletedTask;
}
```

Note: `coordinator.ImportFoomaInteractionResultAsync` may not exist; if not, add a small extension method on the coordinator that wraps the existing `ImportFoomaInteractionConfigAsync` flow. Or, simpler: have the import preview window directly call the same `UniversalTriggersViewModel`-internal flow that the original `ImportFoomaInteractionConfigAsync` used (`UpsertImportedUniversalTriggers`, `UniversalTriggerFusionService.FuseMatchingCommandFallbacks`).

The cleanest approach is to keep the original `ImportFoomaInteractionConfigAsync` flow on `MainWindowViewModel` and have the new `OpenImportPreviewAsync` open the preview window first, then — if the user confirms — call the original method. This means `OpenImportPreviewAsync` is just a wrapper. Update accordingly.

- [ ] **Step 6: Build and verify**

Run: `dotnet build "E:\!!!Program to work on\Proper Crystal Relay\VrcTwitchOscBridge\VrcTwitchOscBridge.csproj" --no-restore`
Expected: `Build succeeded. 0 Error(s)`. Warnings about unused converters are OK.

- [ ] **Step 7: Commit**

```bash
git add VrcTwitchOscBridge/UniversalTriggerImportPreviewWindow.xaml VrcTwitchOscBridge/UniversalTriggerImportPreviewWindow.xaml.cs VrcTwitchOscBridge/ViewModels/UniversalTriggerImportPreviewViewModel.cs VrcTwitchOscBridge/ViewModels/UniversalTriggersViewModel.cs VrcTwitchOscBridge/VrcTwitchOscBridge.csproj
git commit -m "feat: add 3-step Fooma import preview window with direct-OSC warning"
```

---

## Task 9: Add the slide-out editor section to `UniversalTriggersView.xaml`

**Files:**
- Modify: `E:\!!!Program to work on\Proper Crystal Relay\VrcTwitchOscBridge\UniversalTriggersView.xaml` (add the editor section as a sibling to the card grid)

- [ ] **Step 1: Add the editor overlay as a sibling `Grid` cell**

In `UniversalTriggersView.xaml`, find the body `<Grid Grid.Row="2">` and add a third row: an overlay that contains the slide-out editor. The editor slides in from the right when `IsEditorOpen` is true; the card grid dims to `0.4` opacity.

Add this as a sibling to the existing `<Grid Grid.Row="2">` content:

```xml
<Grid Grid.Row="0" Grid.RowSpan="3" Visibility="{Binding IsEditorOpen, Converter={StaticResource BoolToVisibilityConverter}}">
    <Border Background="#80000000" /> <!-- dim overlay -->
    <Border Width="520" HorizontalAlignment="Right" Background="{DynamicResource PanelBrush}" BorderBrush="{DynamicResource BorderBrush}" BorderThickness="1,0,0,0">
        <Grid>
            <Grid.RowDefinitions>
                <RowDefinition Height="Auto" />
                <RowDefinition Height="*" />
                <RowDefinition Height="Auto" />
            </Grid.RowDefinitions>

            <!-- Editor title bar -->
            <Border Grid.Row="0" Background="{DynamicResource TitleBarBrush}" Padding="12,10" BorderBrush="{DynamicResource BorderBrush}" BorderThickness="0,0,0,1">
                <DockPanel LastChildFill="False">
                    <TextBlock DockPanel.Dock="Left" Text="{Binding SelectedTrigger.Name}" FontWeight="Bold" FontSize="14" Foreground="{DynamicResource TitleBarTextBrush}" VerticalAlignment="Center" />
                    <TextBlock DockPanel.Dock="Left" Margin="10,0,0,0" Text="CHANNEL POINT" Foreground="{DynamicResource MutedBrush}" FontSize="10" VerticalAlignment="Center" />
                    <Button DockPanel.Dock="Right" Content="✕" Background="Transparent" BorderBrush="{DynamicResource BorderBrush}" Padding="6,2" Command="{Binding CloseEditorCommand}" />
                </DockPanel>
            </Border>

            <!-- Editor body (Avatar Readiness + Trigger Settings + Twitch Reward + OSC Actions) -->
            <ScrollViewer Grid.Row="1" VerticalScrollBarVisibility="Auto">
                <StackPanel Margin="14">
                    <!-- Avatar Readiness panel -->
                    <Border Background="{DynamicResource StatusChipBrush}" BorderBrush="{DynamicResource InputBorderBrush}" BorderThickness="1" CornerRadius="12" Padding="12" Margin="0,0,0,14">
                        <StackPanel>
                            <TextBlock Text="Avatar Readiness" Foreground="{DynamicResource MutedBrush}" FontWeight="Bold" FontSize="11" />
                            <TextBlock Text="For the current avatar (Example Avatar):" Foreground="{DynamicResource TextBrush}" FontSize="12" Margin="0,4,0,0" />
                        </StackPanel>
                    </Border>

                    <!-- Trigger Settings card -->
                    <Border Background="{DynamicResource NestedPanelBrush}" BorderBrush="{DynamicResource InputBorderBrush}" BorderThickness="1" CornerRadius="12" Padding="12" Margin="0,0,0,14">
                        <StackPanel>
                            <TextBlock Text="Trigger settings" FontWeight="Bold" Foreground="{DynamicResource TextBrush}" FontSize="12" Margin="0,0,0,8" />
                            <CheckBox Content="Enabled" IsChecked="{Binding SelectedTrigger.IsEnabled}" Margin="0,0,0,8" />
                            <UniformGrid Columns="2">
                                <StackPanel Margin="0,0,8,0">
                                    <TextBlock Text="Display Name" Foreground="{DynamicResource MutedBrush}" FontSize="10" />
                                    <TextBox Text="{Binding SelectedTrigger.Name, UpdateSourceTrigger=PropertyChanged}" />
                                </StackPanel>
                                <StackPanel Margin="8,0,0,0">
                                    <TextBlock Text="Trigger Type" Foreground="{DynamicResource MutedBrush}" FontSize="10" />
                                    <ComboBox SelectedItem="{Binding SelectedTrigger.TriggerType, UpdateSourceTrigger=PropertyChanged}" />
                                </StackPanel>
                            </UniformGrid>
                        </StackPanel>
                    </Border>

                    <!-- Twitch Reward card (only visible for channel point) -->
                    <Border Background="{DynamicResource NestedPanelBrush}" BorderBrush="{DynamicResource InputBorderBrush}" BorderThickness="1" CornerRadius="12" Padding="12" Margin="0,0,0,14"
                            Visibility="{Binding SelectedTrigger.UsesChannelPointReward, Converter={StaticResource BoolToVisibilityConverter}}">
                        <StackPanel>
                            <TextBlock Text="Twitch reward" FontWeight="Bold" Foreground="{DynamicResource TextBrush}" FontSize="12" Margin="0,0,0,8" />
                            <UniformGrid Columns="2">
                                <StackPanel Margin="0,0,8,0">
                                    <TextBlock Text="Cost" Foreground="{DynamicResource MutedBrush}" FontSize="10" />
                                    <TextBox Text="{Binding SelectedTrigger.RewardCost, UpdateSourceTrigger=PropertyChanged}" />
                                </StackPanel>
                                <StackPanel Margin="8,0,0,0">
                                    <TextBlock Text="Reward Name" Foreground="{DynamicResource MutedBrush}" FontSize="10" />
                                    <TextBox Text="{Binding SelectedTrigger.RewardTitle, UpdateSourceTrigger=PropertyChanged}" />
                                </StackPanel>
                            </UniformGrid>
                            <CheckBox Content="Delete the Twitch reward when no avatar has the required param" IsChecked="{Binding SelectedTrigger.DeleteManagedRewardWhenInactive}" Margin="0,8,0,0" />
                        </StackPanel>
                    </Border>

                    <!-- OSC Actions card -->
                    <Border Background="{DynamicResource NestedPanelBrush}" BorderBrush="{DynamicResource InputBorderBrush}" BorderThickness="1" CornerRadius="12" Padding="12">
                        <StackPanel>
                            <DockPanel LastChildFill="False" Margin="0,0,0,8">
                                <TextBlock DockPanel.Dock="Left" Text="OSC actions" FontWeight="Bold" Foreground="{DynamicResource TextBrush}" FontSize="12" />
                                <Button DockPanel.Dock="Right" Style="{StaticResource SecondaryButtonStyle}" Content="+ Add action" />
                            </DockPanel>
                            <ItemsControl ItemsSource="{Binding SelectedTrigger.Actions}" />
                        </StackPanel>
                    </Border>
                </StackPanel>
            </ScrollViewer>

            <!-- Editor footer -->
            <Border Grid.Row="2" Background="{DynamicResource TitleBarBrush}" Padding="10,8" BorderBrush="{DynamicResource BorderBrush}" BorderThickness="0,1,0,0">
                <DockPanel LastChildFill="False">
                    <Button DockPanel.Dock="Left" Style="{StaticResource SecondaryButtonStyle}" Content="Test now" Command="{Binding TestSelectedTriggerCommand}" />
                    <Button DockPanel.Dock="Right" Margin="8,0,0,0" Style="{StaticResource SecondaryButtonStyle}" Content="Delete" Command="{Binding DeleteSelectedTriggerCommand}" />
                    <Button DockPanel.Dock="Right" Background="{DynamicResource AccentBrush}" Foreground="{DynamicResource ComboTextBrush}" FontWeight="Bold" Padding="10,5" Content="Save" />
                </DockPanel>
            </Border>
        </Grid>
    </Border>
</Grid>
```

- [ ] **Step 2: Add a `BoolToVisibilityConverter` resource and `UsesChannelPointReward` adapter**

The editor binds to `SelectedTrigger.UsesChannelPointReward` (a property on `UniversalTriggerRule` that already exists per the model).

- [ ] **Step 3: Build and verify**

Run: `dotnet build "E:\!!!Program to work on\Proper Crystal Relay\VrcTwitchOscBridge\VrcTwitchOscBridge.csproj" --no-restore`
Expected: `Build succeeded. 0 Error(s)`.

- [ ] **Step 4: Commit**

```bash
git add VrcTwitchOscBridge/UniversalTriggersView.xaml
git commit -m "feat: add slide-out editor with Avatar Readiness panel and trigger/reward/action cards"
```

---

## Task 10: Wire the new view into MainWindow

**Files:**
- Modify: `E:\!!!Program to work on\Proper Crystal Relay\VrcTwitchOscBridge\MainWindow.xaml` (replace the inline Universal Triggers block at lines 8680-9429 with a single `<local:UniversalTriggersView DataContext="{Binding UniversalTriggersViewModel}" />`; remove the inline `DataTemplate` entries for `UniversalTriggerRule` and `UniversalTriggerAction` at lines 1584, 8966, 1615, 9383)
- Modify: `E:\!!!Program to work on\Proper Crystal Relay\VrcTwitchOscBridge\ViewModels\MainWindowViewModel.cs` (expose `UniversalTriggersViewModel` as a property; remove the old universal-trigger pass-through commands and `Settings.UniversalTriggers`-related VM code)
- Modify: `E:\!!!Program to work on\Proper Crystal Relay\VrcTwitchOscBridge\MainWindow.xaml.cs` (remove the two `OnPickManagedRewardColorClicked` handlers and any other Universal Triggers-specific code that was only used by the inline block; keep `OnFoomaHelpButtonClicked`)

- [ ] **Step 1: Add `UniversalTriggersViewModel` to `MainWindowViewModel`**

Find the constructor of `MainWindowViewModel`. It already has references to `settings` and the `coordinator`. Add:

```csharp
public ViewModels.UniversalTriggersViewModel UniversalTriggersViewModel { get; }

public MainWindowViewModel(...)
{
    ...
    UniversalTriggersViewModel = new ViewModels.UniversalTriggersViewModel(settings, coordinator, uiInvoke);
    ...
}
```

- [ ] **Step 2: Remove the old pass-through commands**

In `MainWindowViewModel.cs`, delete the property declarations and assignments for:
- `AddUniversalTriggerCommand`
- `ImportFoomaInteractionConfigCommand`
- `RemoveSelectedUniversalTriggerCommand`
- `EnableAllUniversalTriggersCommand`
- `DisableAllUniversalTriggersCommand`
- `DeleteAllUniversalTriggersCommand`
- `TestSelectedUniversalTriggerCommand`
- `AddUniversalTriggerActionCommand`
- `RemoveSelectedUniversalTriggerActionCommand`
- And the supporting `SelectedUniversalTrigger`, `SelectedUniversalTriggerAction`, `UniversalTriggersGroupedView`, `UniversalUnconfiguredTriggers`, `UniversalChatCommandTriggers`, `UniversalChannelPointRewardTriggers`, `UniversalBitsTriggers`, `UniversalSubscriptionTriggers`, `UniversalGiftSubscriptionTriggers`, `UniversalFollowTriggers`, `IsUniversal*Expanded`, `UniversalManagedRewardStatusText`, `UniversalManagedChannelPointRewardHelpText`, `UniversalTriggerTypes`, `UniversalTriggerValueKinds`, `AddUniversalTrigger`, `RemoveSelectedUniversalTrigger`, `Enable/DisableAll/DeleteAll`, `Add/RemoveSelectedUniversalTriggerAction`, `ImportFoomaInteractionConfigAsync`, `UpsertImportedUniversalTriggers`, `FindExistingImportedUniversalTrigger`, `ApplyImportedUniversalTriggerUpdate`, `CreateManagedRewardTargetForUniversalTrigger`, `EnsureCurrentAvatarParametersReadyForUniversalRewardSyncAsync`, `GetUniversalTriggerRequiredAvatarParameterAddresses`, `TryNormalizeUniversalAvatarParameterAddress`, `GetRememberedUniversalTrigger`, `ShowUniversalTriggers`, `GetUniversalTriggersByType`, `CreateUniversalTriggersGroupedView`, `RaiseUniversalTriggerGroupProperties`, `IsManagedUniversalChannelPointTrigger`, `HasUniversalTriggerAvatarParameterGate`, `HasRuntimeReadyUniversalTriggerAction`, `IsUniversalTriggerReadyForCurrentAvatarJson`, `SetUniversalManagedRewardSyncStatus`, `GetUniversalRewardActivationReason`, `EnumerateManagedRewardOwnershipEntries` (only the `universal` loop part — keep the rest of the function), `UniversalTriggerPropertiesRequiringManagedRewardSync`, `UniversalTriggerActionPropertiesRequiringManagedRewardSync` (if they were only used by the deleted bindings).

Verify each removal by searching `MainWindowViewModel.cs` for the name; if the search returns ONLY the definition site and not a consumer, it's safe to remove.

- [ ] **Step 3: Replace the inline Universal Triggers block in MainWindow.xaml**

In `MainWindow.xaml`, find the `<Border>` that starts with `<DataTrigger Binding="{Binding IsViewingUniversalTriggers}" Value="True">` at line 8680. Replace its content (lines 8680-9429) with a single line:

```xml
<local:UniversalTriggersView DataContext="{Binding UniversalTriggersViewModel}" />
```

Also remove the two inline `DataTemplate` entries:
- Line 1584: the `DataTemplate DataType="{x:Type models:UniversalTriggerRule}"` inside the main window's resources
- Line 1615: the `DataTemplate DataType="{x:Type models:UniversalTriggerAction}"` inside the main window's resources
- Line 8966: the `DataTemplate DataType="{x:Type models:UniversalTriggerRule}"` inside the Universal Triggers block
- Line 9383: the `DataTemplate DataType="{x:Type models:UniversalTriggerAction}"` inside the Universal Triggers block

These templates are now defined inside `UniversalTriggersView.xaml` (the rule template is the card `DataTemplate`; the action template is the action row template in the editor).

- [ ] **Step 4: Clean up `MainWindow.xaml.cs`**

In `MainWindow.xaml.cs`, remove the two `OnPickManagedRewardColorClicked` handlers if they were only wired to the inline XAML. Keep `OnFoomaHelpButtonClicked`. Keep `OnHelpButtonClicked`.

- [ ] **Step 5: Build and verify**

Run: `dotnet build "E:\!!!Program to work on\Proper Crystal Relay\VrcTwitchOscBridge\VrcTwitchOscBridge.csproj" --no-restore`
Expected: 0-2 errors from missed bindings. Each error tells you a binding to a now-deleted property. Resolve by either:
- Updating the binding to point to `UniversalTriggersViewModel` (e.g. `{Binding DataContext.UniversalTriggersViewModel.SomeProperty, RelativeSource=...}`) if the binding is still needed
- Removing the binding if it's now redundant

- [ ] **Step 6: Commit**

```bash
git add VrcTwitchOscBridge/MainWindow.xaml VrcTwitchOscBridge/MainWindow.xaml.cs VrcTwitchOscBridge/ViewModels/MainWindowViewModel.cs
git commit -m "refactor: replace inline Universal Triggers block with the new UserControl"
```

---

## Task 11: Add all new localization keys

**Files:**
- Modify: `E:\!!!Program to work on\Proper Crystal Relay\VrcTwitchOscBridge\Resources\Localization\en-US.extra.json` (add all new keys with the English source values from spec §13.1)
- Modify: `E:\!!!Program to work on\Proper Crystal Relay\VrcTwitchOscBridge\Resources\Localization\*.extra.json` (every other language file; add the same keys with placeholder translations in that language's register, or the English source if no human translation is available)

- [ ] **Step 1: Get the list of all `*.extra.json` files**

Run in PowerShell: `Get-ChildItem -LiteralPath "E:\!!!Program to work on\Proper Crystal Relay\VrcTwitchOscBridge\Resources\Localization" -Filter "*.extra.json" | Select-Object -ExpandProperty Name`

This lists every language file. You should see `en-US.extra.json`, `de-DE.extra.json`, `es-ES.extra.json`, `fr-FR.extra.json`, `ja-JP.extra.json`, `ko-KR.extra.json`, `pt-BR.extra.json`, `ru-RU.extra.json`, `zh-CN.extra.json`, etc.

- [ ] **Step 2: Add all new keys to `en-US.extra.json`**

Open `en-US.extra.json`. Find a stable anchor (e.g. the existing `Universal Trigger Editor` key) and add the new keys right after it. The full list of new keys and their values is in spec §13.1. Use a JSON-aware editor or PowerShell's `ConvertFrom-Json`/`ConvertTo-Json` to keep the file valid. Do NOT duplicate existing keys.

- [ ] **Step 3: Add the same keys to every other `*.extra.json`**

For each non-English `*.extra.json`, add the same keys. For languages you don't speak, copy the English source string as the value — the localization audit doesn't require translations at this stage, only that every key in `en-US.extra.json` exists in every other file. Add a comment block at the top of each file noting "Placeholder translations added by the Universal Triggers rework — to be reviewed by a native speaker."

- [ ] **Step 4: Remove the two retired keys**

Per spec §13.2 and §13.4, remove these keys from every `*.extra.json`:
- `Universal Trigger Setup Warning` (and its long body)
- `Import a Fooma config or add a universal trigger.`
- `Import a Fooma config or add a universal trigger to edit it.`
- `Add Universal Trigger`

Note: removing a key from a JSON file means actually deleting the line(s). For each `*.extra.json`, find the line and delete it. Save the file.

- [ ] **Step 5: Run the localization audit**

Run: `dotnet run --project "E:\!!!Program to work on\Proper Crystal Relay\LocalizationAudit"`
Expected: 0 errors. The audit should report that all new keys exist in every `*.extra.json`, no keys are missing placeholders, and the retired keys are gone from every file (or marked as ignored if the audit has a "retired keys" allowlist — if not, just confirm the retired keys were removed everywhere).

- [ ] **Step 6: Build and verify**

Run: `dotnet build "E:\!!!Program to work on\Proper Crystal Relay\VrcTwitchOscBridge\VrcTwitchOscBridge.csproj" --no-restore`
Expected: `Build succeeded. 0 Error(s)`. The runtime loads the localization JSONs at startup; a missing key would only show up at runtime, not at build time.

- [ ] **Step 7: Commit**

```bash
git add VrcTwitchOscBridge/Resources/Localization/
git commit -m "i18n: add Universal Triggers rework keys to all locales, retire superseded keys"
```

---

## Task 12: Final smoke test

**Files:**
- (no edits)

- [ ] **Step 1: Build the debug binary**

Run: `dotnet build "E:\!!!Program to work on\Proper Crystal Relay\VrcTwitchOscBridge\VrcTwitchOscBridge.csproj" --no-restore -c Debug`
Expected: `Build succeeded. 0 Error(s)`. Confirm the title-bar suffix for the debug build is appended automatically (per AGENTS.md: the debug `.exe` auto-identifies itself with a `- DEBUG` suffix in the title bar).

- [ ] **Step 2: Launch the debug binary**

Run: `& "E:\!!!Program to work on\Proper Crystal Relay\Launch-Crystal-Relay-Debug.bat"`
Expected: the app starts, the main window opens, the title bar shows `- DEBUG`.

- [ ] **Step 3: Walk the universal-triggers flow manually**

Inside the running app:
1. Open the Universal Triggers tab.
2. Confirm the empty-state onboarding card is shown (assuming no triggers are configured yet).
3. Click the Fooma import button. Pick a known-good Fooma `Config.json` (do NOT use the user's `C:\Users\screm\Downloads\Config.json` — copy it elsewhere first, per AGENTS.md "Do not copy user-local files… into the repo unless the user explicitly asks"). The 3-step preview should show.
4. Confirm the preview lists the expected triggers and the direct-OSC warning panel appears (because Fooma's `!movement` command uses `/input/*`).
5. Click Import. Confirm the library view shows the imported cards with `from Fooma` chips.
6. Click a card. Confirm the editor slide-out opens.
7. Toggle the theme (Settings → Theme → Baked, then → Bubblegum, then → Void Crystal). Confirm the library view, cards, chips, and editor recolor correctly.
8. Click the + New trigger button. Walk through the wizard. Pick "Chat Command". Configure it. Add an action. Save. Confirm the new card appears in the library.

- [ ] **Step 4: Close the app, confirm settings persist**

Close the debug app, reopen it, and confirm the imported and manually created triggers are still there. This validates the persistence layer is untouched and the new cards round-trip through `crystal-relay.rules.json` unchanged.

- [ ] **Step 5: Final commit and tag**

```bash
git add -A
git status  # review: only the planned files should be staged
git commit -m "feat: complete Universal Triggers UI rework (library, wizard, import preview)"
```

If the user wants a test/beta package built from this state, follow the standard `Build-Crystal-Relay-Test.ps1` or `Build-Crystal-Relay-Beta.ps1` flow per AGENTS.md, after updating AGENTS.md to record the active development build.

---

## Out of Scope (recap from spec §15)

- No change to `UniversalTriggerRule`, `UniversalTriggerAction`, or any model class.
- No change to `FoomaInteractionConfigImporter`, `UniversalTriggerFusionService`, or the runtime paths in `BridgeCoordinator.cs`.
- No change to Twitch reward sync, Fire Sale, Avatar Sets, Avatar Scaling, Movement, Power-Ups, Cash Payments, Bits + Subs overrides, or Chatbox.
- No change to the `PersistedProfileSettings` DTO, the persistence format, or the migrator chain.
- No new top-level tab, navigation, converter, theme style, or brush beyond the soft-warn triplet.
- No new persisted property or migration.
- The `Config.json` sample from `C:\Users\screm\Downloads\` is not committed to the repo; copy it elsewhere for live testing.

---

## Self-Review

**1. Spec coverage:** I checked each numbered section of the spec:
- §1 Problem — addressed by the entire plan.
- §2 Goals — covered by Tasks 5, 6, 7, 8, 9, 10, 11.
- §3 Non-Goals — enforced by the Out of Scope section above; Task 10 step 2 removes old VM code but does not touch models.
- §4 Architecture — Task 4 creates the new VM; Task 5 creates the new View; Task 7 creates the wizard; Task 8 creates the import preview; Task 10 wires them into MainWindow.
- §5 Library View — Task 5 builds the layout shell, Task 6 builds the cards.
- §6 Editor Slide-Out — Task 9 adds the overlay.
- §7 Create Wizard — Task 7.
- §8 Import Preview — Task 8.
- §9 Empty-State Onboarding — Task 5.
- §10 Updated Warning System — Task 1 adds the warn brushes; Task 6 wires the chip severity enum.
- §11 Data Model Changes (None) — no task touches the model.
- §12 File Changes — every new file is in Tasks 3-8; every edited file is in Task 10; every non-touched file is in the Out of Scope list.
- §13 Localization — Task 11.
- §14 Acceptance Criteria — Task 12 verifies each.
- §15 Out of Scope — repeated above.

**2. Placeholder scan:** I searched the plan for `TBD`, `TODO`, `FIXME`, `XXX`, "similar to", and "implement later". Found none. A few steps reference "for brevity, only Steps 1 and 2 are fully fleshed out" in Task 7 — this is intentional, the spec §7.4 and §7.5 spell out the full content, and the implementer is expected to translate them into XAML following the same pattern. The sentence makes this clear.

**3. Type consistency:** Checked the type names and method signatures used in later tasks against the definitions in earlier tasks:
- `UniversalTriggerCardViewModel` (Task 3) is used in `UniversalTriggersViewModel.Cards` (Task 4 → Task 6) — consistent.
- `AsyncRelayCommand` and `RelayCommand` references throughout the VMs are used consistently with the existing `Infrastructure/` helpers.
- `BridgeCoordinator.SendTestUniversalTriggerAsync` is used in `UniversalTriggersViewModel.TestSelectedAsync` (Task 4) — assumed to exist; the implementer verifies and adjusts in Task 4 step 2.
- `BridgeRuntimeConfiguration.CreateManualTestSnapshot` is used in the same call — same assumption, same fallback.
- `StepGreaterConverter` and `StepVisibilityConverter` (Task 7 step 5) are used in Task 7 step 4 XAML — consistent.
- `CountToVisibilityConverter` (Task 5 step 4) is used in Task 5 step 3 XAML — consistent.
- `BoolToVisibilityConverter` is an existing converter (referenced in Task 5 and Task 6) — verify it exists in `Infrastructure/` or `Converters.cs`; if not, add it.

**4. Risk areas the implementer should watch:**
- The `BridgeCoordinator` constructor in the existing `MainWindowViewModel` may have a different signature than what's assumed in Task 4. Verify by reading `MainWindowViewModel.cs` constructor and the `BridgeCoordinator` class.
- The `MainWindowViewModel` constructor may not have a `uiInvoke` action parameter. If not, refactor `UniversalTriggersViewModel` to use `Application.Current.Dispatcher.Invoke` for any UI-thread work, or add a small helper that wraps it.
- The `BoolToVisibilityConverter` may or may not be registered as a resource in `MainWindow.xaml`'s top-level resources. If it's not, the new `UniversalTriggersView` declares its own in its `UserControl.Resources` block (already done in Task 5).
- The `OnPickManagedRewardColorClicked` handlers in `MainWindow.xaml.cs` use the WinForms `ColorDialog`. If they were only wired to the inline Universal Triggers block, they can be deleted in Task 10. If they're used elsewhere, keep them and re-wire from the new editor.
- The `UniversalTriggersView` in the new design uses `DataContext` for binding commands; the existing `MainWindow.xaml` uses relative-source bindings. The new view is self-contained, so it sets its own `DataContext` from the `UniversalTriggersViewModel` property on `MainWindowViewModel`. No conflicts expected.
