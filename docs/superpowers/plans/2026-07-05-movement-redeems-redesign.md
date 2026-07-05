# Movement Redeems Redesign Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Replace the inline Movement Redeems UI with a dedicated manager window and expand the movement action catalog from 12 to 26 VRChat OSC Input Controller types across 5 categories.

**Architecture:** New `MovementRedeemsManagerWindow` + `MovementRedeemsManagerViewModel` following the modern pattern used by Avatar Scaling, Universal Triggers, and Avatar Sets. Model changes are backward-compatible — existing enum values retain their positions. `PlayerMovementDirection` enum expands from 12 to 31 values. `BridgeCoordinator.ResolvePlayerMovementAction()` gains handling for all new input types (button vs axis, VR-only, UI toggle behavior).

**Tech Stack:** C#, WPF/XAML, .NET 10, xUnit (tests), CommunityToolkit.Mvvm (ObservableObject/RelayCommand)

---

### Task 1: Expand PlayerMovementDirection enum

**Files:**
- Modify: `VrcTwitchOscBridge\Models\PlayerMovementDirection.cs`

- [ ] **Step 1: Read current enum**

Read the full contents of `VrcTwitchOscBridge\Models\PlayerMovementDirection.cs` to see the current values and any attributes.

- [ ] **Step 2: Add new enum values (existing values keep their positions)**

Append the following values after `GlitchyMovement` (the current last value):

```csharp
Run,
LookHorizontal,
LookLeft,
LookRight,
ComfortLeft,
ComfortRight,
GrabLeft,
GrabRight,
UseLeft,
UseRight,
DropLeft,
DropRight,
MoveHoldFB,
SpinHoldCwCcw,
SpinHoldUD,
SpinHoldLR,
QuickMenuToggleLeft,
QuickMenuToggleRight,
PanicButton,
Voice,
```

- [ ] **Step 3: Update any display-label attributes or helpers**

Check if the enum has `[Display(Name=...)]` attributes or a static helper method that maps enum values to display strings. Add entries for all new values.

- [ ] **Step 4: Build to verify**

```powershell
dotnet build "E:\!!!Program to work on\Proper Crystal Relay\VrcTwitchOscBridge\VrcTwitchOscBridge.csproj" --no-restore 2>&1
```

Expected: Build succeeds with only "defined but not used" warnings.

---

### Task 2: Add FloatValue to TriggerRule

**Files:**
- Modify: `VrcTwitchOscBridge\Models\TriggerRule.cs`

- [ ] **Step 1: Read TriggerRule model**

Read `VrcTwitchOscBridge\Models\TriggerRule.cs` to find the existing properties and understand serialization attributes.

- [ ] **Step 2: Add FloatValue property**

Add a nullable float property for axis speed control (used by Held Object movement types):

```csharp
[JsonPropertyName("fv")]
public float? FloatValue { get; set; }
```

Use `[JsonPropertyName("fv")]` to keep JSON key short (existing TriggerRule uses short keys).

- [ ] **Step 3: Check for copy/clone methods**

Search for any `Clone()` or `CopyFrom()` methods on `TriggerRule` and ensure `FloatValue` is included.

- [ ] **Step 4: Check TriggerRuleSnapshot**

Search for `TriggerRuleSnapshot` class (used in BridgeCoordinator) and add `FloatValue` there too if it exists.

- [ ] **Step 5: Build**

```powershell
dotnet build "E:\!!!Program to work on\Proper Crystal Relay\VrcTwitchOscBridge\VrcTwitchOscBridge.csproj" --no-restore 2>&1
```

---

### Task 3: Create Movement Category Classification Helper

**Files:**
- Create: `VrcTwitchOscBridge\Models\MovementCategory.cs`
- Create: `VrcTwitchOscBridge\Models\MovementTypeClassifier.cs`

- [ ] **Step 1: Create MovementCategory enum**

```csharp
namespace VrcTwitchOscBridge.Models;

public enum MovementCategory
{
    Movement,
    Turning,
    HandInteractions,
    HeldObject,
    UiToggles,
}
```

- [ ] **Step 2: Create MovementTypeClassifier static class**

```csharp
namespace VrcTwitchOscBridge.Models;

public static class MovementTypeClassifier
{
    public static MovementCategory GetCategory(PlayerMovementDirection direction) => direction switch
    {
        PlayerMovementDirection.Forward or PlayerMovementDirection.Backward
            or PlayerMovementDirection.Left or PlayerMovementDirection.Right
            or PlayerMovementDirection.Jump or PlayerMovementDirection.Run
            or PlayerMovementDirection.RandomMovement or PlayerMovementDirection.GlitchyMovement
            => MovementCategory.Movement,

        PlayerMovementDirection.LookHorizontal or PlayerMovementDirection.LookLeft
            or PlayerMovementDirection.LookRight or PlayerMovementDirection.ComfortLeft
            or PlayerMovementDirection.ComfortRight
            => MovementCategory.Turning,

        PlayerMovementDirection.GrabLeft or PlayerMovementDirection.GrabRight
            or PlayerMovementDirection.UseLeft or PlayerMovementDirection.UseRight
            or PlayerMovementDirection.DropLeft or PlayerMovementDirection.DropRight
            => MovementCategory.HandInteractions,

        PlayerMovementDirection.MoveHoldFB or PlayerMovementDirection.SpinHoldCwCcw
            or PlayerMovementDirection.SpinHoldUD or PlayerMovementDirection.SpinHoldLR
            => MovementCategory.HeldObject,

        PlayerMovementDirection.QuickMenuToggleLeft or PlayerMovementDirection.QuickMenuToggleRight
            or PlayerMovementDirection.PanicButton or PlayerMovementDirection.Voice
            => MovementCategory.UiToggles,

        _ => MovementCategory.Movement,
    };

    public static bool IsVrOnly(PlayerMovementDirection direction) => direction switch
    {
        PlayerMovementDirection.ComfortLeft or PlayerMovementDirection.ComfortRight
            or PlayerMovementDirection.GrabLeft or PlayerMovementDirection.GrabRight
            or PlayerMovementDirection.UseLeft or PlayerMovementDirection.UseRight
            or PlayerMovementDirection.DropLeft or PlayerMovementDirection.DropRight
            => true,
        _ => false,
    };

    public static bool IsAxisType(PlayerMovementDirection direction) => direction switch
    {
        PlayerMovementDirection.LookHorizontal
            or PlayerMovementDirection.MoveHoldFB
            or PlayerMovementDirection.SpinHoldCwCcw
            or PlayerMovementDirection.SpinHoldUD
            or PlayerMovementDirection.SpinHoldLR
            => true,
        _ => false,
    };

    public static string GetBehaviorTooltip(PlayerMovementDirection direction) => direction switch
    {
        PlayerMovementDirection.LookLeft or PlayerMovementDirection.LookRight
            => "Smooth on Desktop. Snap-turn in VR if Comfort Turning is ON.",
        PlayerMovementDirection.LookHorizontal
            => "Smooth on Desktop. Snap-turn in VR if Comfort Turning is ON.",
        PlayerMovementDirection.ComfortLeft or PlayerMovementDirection.ComfortRight
            => "VR-only. Always snap-turn regardless of Comfort Turning setting.",
        PlayerMovementDirection.GrabLeft or PlayerMovementDirection.GrabRight
            or PlayerMovementDirection.UseLeft or PlayerMovementDirection.UseRight
            or PlayerMovementDirection.DropLeft or PlayerMovementDirection.DropRight
            => "VR-only input. No effect on Desktop.",
        PlayerMovementDirection.MoveHoldFB or PlayerMovementDirection.SpinHoldCwCcw
            or PlayerMovementDirection.SpinHoldUD or PlayerMovementDirection.SpinHoldLR
            => "Controls held objects. Axis speed value = speed setting.",
        PlayerMovementDirection.QuickMenuToggleLeft or PlayerMovementDirection.QuickMenuToggleRight
            or PlayerMovementDirection.PanicButton
            => "Triggers UI action. Duration = hold time before reset.",
        PlayerMovementDirection.Voice
            => "Toggles voice. Behavior depends on VRChat 'Toggle Voice' setting.",
        _ => string.Empty,
    };
}
```

- [ ] **Step 3: Build**

```powershell
dotnet build "E:\!!!Program to work on\Proper Crystal Relay\VrcTwitchOscBridge\VrcTwitchOscBridge.csproj" --no-restore 2>&1
```

---

### Task 4: Expand ResolvePlayerMovementAction in BridgeCoordinator

**Files:**
- Modify: `VrcTwitchOscBridge\Services\BridgeCoordinator.cs`

- [ ] **Step 1: Read current ResolvePlayerMovementAction**

Read `VrcTwitchOscBridge\Services\BridgeCoordinator.cs` around line 8808 to understand the current switch/if-else structure.

- [ ] **Step 2: Expand the action resolver**

The current method uses `vrChatOscClient.BuildInputButtonPacket(address, bool)` for all existing types.
For the new types, three patterns apply:

**Button types** (Run, LookLeft, ComfortLeft, GrabLeft, DropRight, etc.): Same as existing — `BuildInputButtonPacket(address, true)` for start, `BuildInputButtonPacket(address, false)` for stop.

**Axis types** (LookHorizontal, MoveHoldFB, SpinHoldCwCcw, SpinHoldUD, SpinHoldLR): Use `BuildInputAxisPacket(address, floatValue)` — uses `rule.FloatValue ?? 1.0f` as the float. Reset sends `BuildInputAxisPacket(address, 0.0f)`.

**UI Toggle types** (QuickMenuToggleLeft/Right, PanicButton, Voice): Same as button types — `BuildInputButtonPacket(address, true)` / `BuildInputButtonPacket(address, false)`.

Add cases to the switch for the new OSC addresses:

```csharp
PlayerMovementDirection.Run => "/input/Run",
PlayerMovementDirection.LookHorizontal => "/input/LookHorizontal",
PlayerMovementDirection.LookLeft => "/input/LookLeft",
PlayerMovementDirection.LookRight => "/input/LookRight",
PlayerMovementDirection.ComfortLeft => "/input/ComfortLeft",
PlayerMovementDirection.ComfortRight => "/input/ComfortRight",
PlayerMovementDirection.GrabLeft => "/input/GrabLeft",
PlayerMovementDirection.GrabRight => "/input/GrabRight",
PlayerMovementDirection.UseLeft => "/input/UseLeft",
PlayerMovementDirection.UseRight => "/input/UseRight",
PlayerMovementDirection.DropLeft => "/input/DropLeft",
PlayerMovementDirection.DropRight => "/input/DropRight",
PlayerMovementDirection.MoveHoldFB => "/input/MoveHoldFB",
PlayerMovementDirection.SpinHoldCwCcw => "/input/SpinHoldCwCcw",
PlayerMovementDirection.SpinHoldUD => "/input/SpinHoldUD",
PlayerMovementDirection.SpinHoldLR => "/input/SpinHoldLR",
PlayerMovementDirection.QuickMenuToggleLeft => "/input/QuickMenuToggleLeft",
PlayerMovementDirection.QuickMenuToggleRight => "/input/QuickMenuToggleRight",
PlayerMovementDirection.PanicButton => "/input/PanicButton",
PlayerMovementDirection.Voice => "/input/Voice",
```

Then after the switch, build the start/stop packets:

```csharp
var inputAddress = /* switch result above */;
bool isAxis = MovementTypeClassifier.IsAxisType(rule.MovementDirection);

byte[] startPacket;
byte[] stopPacket;

if (isAxis)
{
    var floatValue = rule.FloatValue ?? 1.0f;
    startPacket = vrChatOscClient.BuildInputAxisPacket(inputAddress, floatValue);
    stopPacket = vrChatOscClient.BuildInputAxisPacket(inputAddress, 0.0f);
}
else
{
    startPacket = vrChatOscClient.BuildInputButtonPacket(inputAddress, true);
    stopPacket = vrChatOscClient.BuildInputButtonPacket(inputAddress, false);
}

return new ResolvedRuleAction(startPacket, stopPacket, displayValue);
```

Keep the existing `MovementDirection.Forward => "/input/MoveForward"` etc. for the original 8 directions. The Random/Glitchy exceptions stay as they are (pre-resolved).

- [ ] **Step 3: Check for any IsSoftLockMovement references**

Search for `IsSoftLockMovement` and ensure the new movement types are categorized correctly (basic movement directions trigger soft lock, not UI toggles or held object spins).

- [ ] **Step 4: Build**

```powershell
dotnet build "E:\!!!Program to work on\Proper Crystal Relay\VrcTwitchOscBridge\VrcTwitchOscBridge.csproj" --no-restore 2>&1
```

---

### Task 5: Remove inline Movement Redeems from MainWindow.xaml

**Files:**
- Modify: `VrcTwitchOscBridge\MainWindow.xaml`

- [ ] **Step 1: Read the Movement Redeems section**

Read `MainWindow.xaml` around lines 3900–6920 to identify the exact XAML to remove:
- Movement Redeems sidebar button (around line 3536)
- Movement Redeems action buttons panel (around lines 3903–3932)
- Movement Redeems set ListBox (around lines 4031–4047)
- Movement Redeems workspace section (around lines 6817–6915)

- [ ] **Step 2: Remove the Movement Redeems workspace section**

Remove the workspace section (lines ~6817–6915) that starts with the "Movement Redeems" heading inside the Redeem Workspace. The section is gated by `IsViewingMovementRedeems`.

- [ ] **Step 3: Remove the Movement Redeems set list and action buttons**

Remove the Movement Redeems action buttons panel and set ListBox from the sidebar/configuration area.

- [ ] **Step 4: Keep or rewire the sidebar nav button**

Either remove the sidebar "Movement Redeems" button or rewire its `Command` to open the new window (requires the command to exist in MainWindowViewModel first — check Task 9 timing).

- [ ] **Step 5: Build**

```powershell
dotnet build "E:\!!!Program to work on\Proper Crystal Relay\VrcTwitchOscBridge\VrcTwitchOscBridge.csproj" --no-restore 2>&1
```

---

### Task 6: Rewire MainWindowViewModel nav and remove inline logic

**Files:**
- Modify: `VrcTwitchOscBridge\ViewModels\MainWindowViewModel.cs`

- [ ] **Step 1: Find inline Movement Redeems properties and commands**

Search for all Movement Redeems-related members in MainWindowViewModel:
- `IsViewingMovementRedeems`
- `MovementRedeemSets` (property)
- `MovementRedeemRules` (property)
- `selectedMovementRedeemSet` (field)
- `lastSelectedMovementSetId` / `lastSelectedMovementRuleId` (fields)
- `ShowMovementRedeemsCommand`
- `AddMovementRedeemSetCommand`
- `RemoveSelectedMovementRedeemSetCommand`
- `DeleteAllMovementRedeemSetsCommand`
- `ShowMovementRedeems()` (method)
- `AddMovementRedeemSet()` (method)
- `RemoveSelectedMovementRedeemSet()` (method)
- `DeleteAllMovementRedeemSets()` (method)
- `EnsureSelectedMovementRedeemSet()`
- `GetRememberedMovementRedeemSet()`
- `GetOwningMovementRedeemSet()`
- `GetAllMovementRules()`
- `IsSupportedMovementDirection()`
- `IsSupportedMovementRule()`
- `MovementRedeemSetsCollectionChanged`
- `WireMovementRedeemSet` / `UnwireMovementRedeemSet`

- [ ] **Step 2: Add window field and open method**

Following the existing pattern:

```csharp
private MovementRedeemsManagerWindow? _movementRedeemsManagerWindow;

private void OpenMovementRedeemsManager()
{
    if (_movementRedeemsManagerWindow is { IsVisible: true })
    {
        _movementRedeemsManagerWindow.Activate();
        return;
    }

    var managerVm = new MovementRedeemsManagerViewModel(Settings, this);
    _movementRedeemsManagerWindow = new MovementRedeemsManagerWindow(managerVm)
    {
        Owner = System.Windows.Application.Current?.MainWindow,
    };
    _movementRedeemsManagerWindow.Closed += (_, _) => _movementRedeemsManagerWindow = null;
    _movementRedeemsManagerWindow.Show();
}
```

- [ ] **Step 3: Rewire the ShowMovementRedeemsCommand**

Change `ShowMovementRedeemsCommand` to call `OpenMovementRedeemsManager()` instead of switching the inline view state.

- [ ] **Step 4: Remove inline movement methods and properties**

Remove all the inline movement-related properties, fields, and methods listed in Step 1 that are no longer needed. Keep `IsSupportedMovementDirection()` and `IsSupportedMovementRule()` if they are still referenced from BridgeCoordinator or elsewhere. Keep `GetAllMovementRules()` if it's used by the stop-input system.

- [ ] **Step 5: Build**

```powershell
dotnet build "E:\!!!Program to work on\Proper Crystal Relay\VrcTwitchOscBridge\VrcTwitchOscBridge.csproj" --no-restore 2>&1
```

---

### Task 7: Create MovementRedeemCardViewModel

**Files:**
- Create: `VrcTwitchOscBridge\ViewModels\MovementRedeemCardViewModel.cs`

- [ ] **Step 1: Create card ViewModel**

```csharp
using System;
using System.ComponentModel;
using System.Runtime.CompilerServices;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using VrcTwitchOscBridge.Models;

namespace VrcTwitchOscBridge.ViewModels;

public sealed class MovementRedeemCardViewModel : ObservableObject
{
    private readonly TriggerRule rule;

    public MovementRedeemCardViewModel(TriggerRule rule, Action<MovementRedeemCardViewModel>? testAction)
    {
        this.rule = rule ?? throw new ArgumentNullException(nameof(rule));
        this.testAction = testAction;
        UpdateFromRule();
    }

    public Guid Id => rule.Id;

    public string Name
    {
        get => rule.Name;
        set
        {
            if (rule.Name != value)
            {
                rule.Name = value;
                OnPropertyChanged();
            }
        }
    }

    public PlayerMovementDirection MovementDirection => rule.MovementDirection;

    public MovementCategory Category => MovementTypeClassifier.GetCategory(rule.MovementDirection);

    public bool IsVrOnly => MovementTypeClassifier.IsVrOnly(rule.MovementDirection);

    public bool IsAxisType => MovementTypeClassifier.IsAxisType(rule.MovementDirection);

    public string BehaviorTooltip => MovementTypeClassifier.GetBehaviorTooltip(rule.MovementDirection);

    public double DurationSeconds
    {
        get => rule.DurationSeconds;
        set
        {
            var clamped = Math.Max(1, value);
            if (Math.Abs(rule.DurationSeconds - clamped) > 0.01)
            {
                rule.DurationSeconds = clamped;
                OnPropertyChanged();
                OnPropertyChanged(nameof(DurationText));
            }
        }
    }

    public double CooldownSeconds
    {
        get => rule.CooldownSeconds;
        set
        {
            var clamped = Math.Max(0, value);
            if (Math.Abs(rule.CooldownSeconds - clamped) > 0.01)
            {
                rule.CooldownSeconds = clamped;
                OnPropertyChanged();
                OnPropertyChanged(nameof(CooldownText));
            }
        }
    }

    public float? FloatValue
    {
        get => rule.FloatValue;
        set
        {
            if (rule.FloatValue != value)
            {
                rule.FloatValue = value;
                OnPropertyChanged();
            }
        }
    }

    public bool IsEnabled
    {
        get => rule.IsEnabled;
        set
        {
            if (rule.IsEnabled != value)
            {
                rule.IsEnabled = value;
                OnPropertyChanged();
            }
        }
    }

    public string DurationText => $"{DurationSeconds:F1}s";

    public string CooldownText => CooldownSeconds > 0 ? $"{CooldownSeconds:F0}s cooldown" : "No cooldown";

    public string DirectionDisplayName => GetDisplayName(rule.MovementDirection);

    public string CategoryDisplayName => Category switch
    {
        MovementCategory.Movement => "Movement",
        MovementCategory.Turning => "Turning",
        MovementCategory.HandInteractions => "Hand",
        MovementCategory.HeldObject => "Object",
        MovementCategory.UiToggles => "UI",
        _ => "Movement",
    };

    public bool HasChannelPointTrigger => rule.ChannelPointRewardId.HasValue;
    public bool HasChatCommandTrigger => !string.IsNullOrEmpty(rule.TriggerChatCommand);
    public bool HasBitsTrigger => rule.BitsAmount > 0;
    public bool HasSubsTrigger => rule.SubPlan is not null;
    public bool HasGiftSubTrigger => rule.GiftSubAmount > 0;
    public bool HasFollowTrigger => rule.TriggerOnFollow;

    public TriggerRule GetRule() => rule;

    private readonly Action<MovementRedeemCardViewModel>? testAction;

    public string TestButtonText => "Test";

    public void Test()
    {
        testAction?.Invoke(this);
    }

    private void UpdateFromRule()
    {
        OnPropertyChanged(nameof(Name));
        OnPropertyChanged(nameof(MovementDirection));
        OnPropertyChanged(nameof(Category));
        OnPropertyChanged(nameof(IsVrOnly));
        OnPropertyChanged(nameof(IsAxisType));
        OnPropertyChanged(nameof(BehaviorTooltip));
        OnPropertyChanged(nameof(DurationSeconds));
        OnPropertyChanged(nameof(CooldownSeconds));
        OnPropertyChanged(nameof(FloatValue));
        OnPropertyChanged(nameof(IsEnabled));
        OnPropertyChanged(nameof(DurationText));
        OnPropertyChanged(nameof(CooldownText));
        OnPropertyChanged(nameof(DirectionDisplayName));
        OnPropertyChanged(nameof(CategoryDisplayName));
        OnPropertyChanged(nameof(HasChannelPointTrigger));
        OnPropertyChanged(nameof(HasChatCommandTrigger));
        OnPropertyChanged(nameof(HasBitsTrigger));
        OnPropertyChanged(nameof(HasSubsTrigger));
        OnPropertyChanged(nameof(HasGiftSubTrigger));
        OnPropertyChanged(nameof(HasFollowTrigger));
    }

    private static string GetDisplayName(PlayerMovementDirection direction) => direction switch
    {
        PlayerMovementDirection.Forward => "Move Forward",
        PlayerMovementDirection.Backward => "Move Backward",
        PlayerMovementDirection.Left => "Strafe Left",
        PlayerMovementDirection.Right => "Strafe Right",
        PlayerMovementDirection.Jump => "Jump",
        PlayerMovementDirection.Run => "Run / Sprint",
        PlayerMovementDirection.SpinLeft => "Spin Left",
        PlayerMovementDirection.SpinRight => "Spin Right",
        PlayerMovementDirection.RandomMovement => "Random Movement",
        PlayerMovementDirection.GlitchyMovement => "Glitchy Movement",
        PlayerMovementDirection.LookHorizontal => "Look Horizontal (Axis)",
        PlayerMovementDirection.LookLeft => "Look Left",
        PlayerMovementDirection.LookRight => "Look Right",
        PlayerMovementDirection.ComfortLeft => "Snap Turn Left (VR)",
        PlayerMovementDirection.ComfortRight => "Snap Turn Right (VR)",
        PlayerMovementDirection.GrabLeft => "Grab (Left Hand)",
        PlayerMovementDirection.GrabRight => "Grab (Right Hand)",
        PlayerMovementDirection.UseLeft => "Use (Left Hand)",
        PlayerMovementDirection.UseRight => "Use (Right Hand)",
        PlayerMovementDirection.DropLeft => "Drop (Left Hand)",
        PlayerMovementDirection.DropRight => "Drop (Right Hand)",
        PlayerMovementDirection.MoveHoldFB => "Move Held F/B",
        PlayerMovementDirection.SpinHoldCwCcw => "Spin Held CW/CCW",
        PlayerMovementDirection.SpinHoldUD => "Spin Held Up/Down",
        PlayerMovementDirection.SpinHoldLR => "Spin Held Left/Right",
        PlayerMovementDirection.QuickMenuToggleLeft => "Quick Menu (Left)",
        PlayerMovementDirection.QuickMenuToggleRight => "Quick Menu (Right)",
        PlayerMovementDirection.PanicButton => "Safe Mode (Panic)",
        PlayerMovementDirection.Voice => "Voice Toggle",
        _ => direction.ToString(),
    };
}
```

- [ ] **Step 2: Build**

```powershell
dotnet build "E:\!!!Program to work on\Proper Crystal Relay\VrcTwitchOscBridge\VrcTwitchOscBridge.csproj" --no-restore 2>&1
```

---

### Task 8: Create MovementRedeemsManagerViewModel

**Files:**
- Create: `VrcTwitchOscBridge\ViewModels\MovementRedeemsManagerViewModel.cs`

- [ ] **Step 1: Create the ViewModel**

```csharp
using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Linq;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using VrcTwitchOscBridge.Models;

namespace VrcTwitchOscBridge.ViewModels;

public sealed class MovementRedeemsManagerViewModel : ObservableObject, IDisposable
{
    private readonly AppSettings settings;
    private readonly MainWindowViewModel? mainWindowViewModel;
    private string searchText = string.Empty;
    private MovementCategory? activeCategory;
    private bool isEditorOpen;
    private bool disposed;

    public MovementRedeemsManagerViewModel(AppSettings settings, MainWindowViewModel? mainWindowViewModel)
    {
        this.settings = settings ?? throw new ArgumentNullException(nameof(settings));
        this.mainWindowViewModel = mainWindowViewModel;
        WireCollectionChanges();
        RefreshCards();
    }

    public ObservableCollection<MovementRedeemCardViewModel> Cards { get; } = [];

    public string SearchText
    {
        get => searchText;
        set
        {
            if (SetProperty(ref searchText, value))
                RefreshCards();
        }
    }

    public MovementCategory? ActiveCategory
    {
        get => activeCategory;
        set
        {
            if (SetProperty(ref activeCategory, value))
                RefreshCards();
        }
    }

    public bool IsEditorOpen
    {
        get => isEditorOpen;
        set => SetProperty(ref isEditorOpen, value);
    }

    public MovementRedeemCardViewModel? SelectedCard { get; set; }

    public IRelayCommand<MovementCategory?> FilterCategoryCommand { get; }
    public IRelayCommand OpenEditorCommand { get; }
    public IRelayCommand CloseEditorCommand { get; }
    public IRelayCommand AddNewRuleCommand { get; }
    public IRelayCommand DeleteCardCommand { get; }
    public IRelayCommand TestCardCommand { get; }

    // Category counts for badge display
    public int MovementCount => GetCategoryCount(MovementCategory.Movement);
    public int TurningCount => GetCategoryCount(MovementCategory.Turning);
    public int HandCount => GetCategoryCount(MovementCategory.HandInteractions);
    public int HeldObjectCount => GetCategoryCount(MovementCategory.HeldObject);
    public int UiTogglesCount => GetCategoryCount(MovementCategory.UiToggles);

    private int GetCategoryCount(MovementCategory category) =>
        allRules.Count(r => MovementTypeClassifier.GetCategory(r.MovementDirection) == category);

    private readonly List<TriggerRule> allRules = [];

    private void WireCollectionChanges()
    {
        foreach (var set in settings.MovementRedeemSets)
        {
            WireSet(set);
        }
        settings.MovementRedeemSets.CollectionChanged += (_, e) =>
        {
            if (e.NewItems is not null)
            {
                foreach (MovementRedeemSet set in e.NewItems)
                    WireSet(set);
            }
            if (e.OldItems is not null)
            {
                foreach (MovementRedeemSet set in e.OldItems)
                    UnwireSet(set);
            }
            RefreshCards();
        };
    }

    private void WireSet(MovementRedeemSet set)
    {
        allRules.AddRange(set.MovementRules);
        set.MovementRules.CollectionChanged += (_, _) => RefreshCards();
    }

    private void UnwireSet(MovementRedeemSet set)
    {
        foreach (var rule in set.MovementRules)
            allRules.Remove(rule);
    }

    private void RefreshCards()
    {
        var filtered = allRules.AsEnumerable();

        if (!string.IsNullOrWhiteSpace(searchText))
        {
            var lower = searchText.ToLowerInvariant();
            filtered = filtered.Where(r =>
                r.Name?.ToLowerInvariant().Contains(lower) == true ||
                r.MovementDirection.ToString().ToLowerInvariant().Contains(lower));
        }

        if (activeCategory.HasValue)
        {
            var cat = activeCategory.Value;
            filtered = filtered.Where(r => MovementTypeClassifier.GetCategory(r.MovementDirection) == cat);
        }

        Cards.Clear();
        foreach (var rule in filtered)
        {
            Cards.Add(new MovementRedeemCardViewModel(rule, OnTestCard));
        }

        OnPropertyChanged(nameof(MovementCount));
        OnPropertyChanged(nameof(TurningCount));
        OnPropertyChanged(nameof(HandCount));
        OnPropertyChanged(nameof(HeldObjectCount));
        OnPropertyChanged(nameof(UiTogglesCount));
    }

    private void OnTestCard(MovementRedeemCardViewModel card)
    {
        mainWindowViewModel?.TestMovementRule(card.GetRule());
    }

    public void Dispose()
    {
        if (disposed) return;
        disposed = true;
    }
}
```

- [ ] **Step 2: Add TestMovementRule helper to MainWindowViewModel**

In `MainWindowViewModel.cs`, add a public method that the manager ViewModel can call for testing. The BridgeCoordinator already has an execution path for movement rules — use the existing `_bridgeCoordinator.QuickTestRule(rule)` method. Check if `QuickTestRule` or `QuickTestMovement` exists; if not, create one that calls `ResolvePlayerMovementAction` with a brief 2-second force-duration:

```csharp
// In MainWindowViewModel.cs
public void TestMovementRule(TriggerRule rule)
{
    // Delegate to BridgeCoordinator which sends the OSC for ~2 seconds
    _bridgeCoordinator.QuickTestRule(rule);
}
```

In `BridgeCoordinator.cs`, add a public `QuickTestRule` method:

```csharp
public void QuickTestRule(TriggerRule rule)
{
    var snapshot = TriggerRuleSnapshot.FromRule(rule);
    if (snapshot.ActionType != OscActionType.PlayerMovement) return;

    // Override duration to 2 seconds for the test
    snapshot = snapshot with { DurationSeconds = 2 };

    var action = ResolvePlayerMovementAction(snapshot);
    SendMovementPackets(action, snapshot.Id);
}
```

Use the same `SendMovementPackets` / packet execution path that the normal trigger flow uses, but with the forced short duration.

- [ ] **Step 3: Build**

```powershell
dotnet build "E:\!!!Program to work on\Proper Crystal Relay\VrcTwitchOscBridge\VrcTwitchOscBridge.csproj" --no-restore 2>&1
```

---

### Task 9: Create MovementRedeemsManagerWindow XAML and code-behind

**Files:**
- Create: `VrcTwitchOscBridge\MovementRedeemsManagerWindow.xaml`
- Create: `VrcTwitchOscBridge\MovementRedeemsManagerWindow.xaml.cs`

- [ ] **Step 1: Create the XAML window**

Create `MovementRedeemsManagerWindow.xaml` with the following structure. Follow the exact patterns from `AvatarScalingManagerWindow.xaml` for theme resources, chrome, and slide-in editor.

**Window chrome (same as AvatarScalingManagerWindow):**
```xml
<Window x:Class="VrcTwitchOscBridge.MovementRedeemsManagerWindow"
        xmlns="http://schemas.microsoft.com/winfx/2006/xaml/presentation"
        xmlns:x="http://schemas.microsoft.com/winfx/2006/xaml"
        xmlns:vm="clr-namespace:VrcTwitchOscBridge.ViewModels"
        xmlns:models="clr-namespace:VrcTwitchOscBridge.Models"
        WindowStyle="None" AllowsTransparency="True"
        Background="Transparent"
        Width="1000" Height="700"
        WindowStartupLocation="CenterOwner">
    <WindowChrome.WindowChrome>
        <WindowChrome GlassFrameThickness="0" CaptionHeight="0" ResizeBorderThickness="5"
                      UseAeroCaptionButtons="False" CornerRadius="0"/>
    </WindowChrome.WindowChrome>
```

**Theme resources section:** Copy the same theme brushes (`TitleBarBrush`, `TitleBarTextBrush`, `CardBackgroundBrush`, `PanelHighlightBrush`, `NestedPanelBrush`, `SettingsTitleBrush`, `MedallionBrush`) from `AvatarScalingManagerWindow.xaml`.

**Grid layout:**
```xml
<Grid>
    <!-- Title bar -->
    <Border Height="40" Background="{StaticResource TitleBarBrush}" VerticalAlignment="Top">
        <Grid>
            <TextBlock Text="Movement Redeems" Foreground="{StaticResource TitleBarTextBrush}"
                       FontSize="16" Margin="16,0,0,0" VerticalAlignment="Center"/>
            <Button Content="✕" Width="40" Height="40" HorizontalAlignment="Right"
                    Background="Transparent" Foreground="{StaticResource TitleBarTextBrush}"
                    Command="{Binding CloseWindowCommand}" />
        </Grid>
    </Border>

    <!-- Main content below title bar -->
    <Grid Margin="0,40,0,0">
        <!-- Toolbar: Search + Category filter buttons -->
        <StackPanel Orientation="Horizontal" Margin="12,8" Height="32" VerticalAlignment="Top">
            <TextBox Text="{Binding SearchText, UpdateSourceTrigger=PropertyChanged}"
                     Width="180" Height="28" FontSize="12"
                     Style="{StaticResource TextBoxStyle}"
                     ToolTip="Search movement rules..." />
            <Button Content="All" Width="60" Height="28" Margin="8,0,0,0"
                    Command="{Binding FilterCategoryCommand}" CommandParameter="{x:Null}"
                    Style="{StaticResource FilterButtonStyle}" />
            <Button Content="Movement" Width="80" Height="28" Margin="4,0,0,0"
                    Command="{Binding FilterCategoryCommand}"
                    CommandParameter="{x:Static models:MovementCategory.Movement}"
                    Style="{StaticResource FilterButtonStyle}" />
            <Button Content="Turning" Width="70" Height="28" Margin="4,0,0,0"
                    Command="{Binding FilterCategoryCommand}"
                    CommandParameter="{x:Static models:MovementCategory.Turning}"
                    Style="{StaticResource FilterButtonStyle}" />
            <Button Content="Hand" Width="60" Height="28" Margin="4,0,0,0"
                    Command="{Binding FilterCategoryCommand}"
                    CommandParameter="{x:Static models:MovementCategory.HandInteractions}"
                    Style="{StaticResource FilterButtonStyle}" />
            <Button Content="Object" Width="65" Height="28" Margin="4,0,0,0"
                    Command="{Binding FilterCategoryCommand}"
                    CommandParameter="{x:Static models:MovementCategory.HeldObject}"
                    Style="{StaticResource FilterButtonStyle}" />
            <Button Content="UI" Width="50" Height="28" Margin="4,0,0,0"
                    Command="{Binding FilterCategoryCommand}"
                    CommandParameter="{x:Static models:MovementCategory.UiToggles}"
                    Style="{StaticResource FilterButtonStyle}" />
        </StackPanel>

        <!-- Card list -->
        <ListBox ItemsSource="{Binding Cards}" Margin="12,44,12,12"
                 Background="Transparent" BorderThickness="0"
                 ScrollViewer.HorizontalScrollBarVisibility="Disabled">
            <ListBox.ItemTemplate>
                <DataTemplate DataType="{x:Type vm:MovementRedeemCardViewModel}">
                    <Border Margin="0,0,0,8" Padding="12" CornerRadius="12"
                            Background="{StaticResource CardBackgroundBrush}"
                            BorderBrush="{StaticResource PanelHighlightBrush}" BorderThickness="1">
                        <Grid>
                            <!-- Card content: Category pill, Name, toggle, trigger pills, stats, buttons -->
                            <!-- Follow same card layout as mockup in design doc -->
                        </Grid>
                    </Border>
                </DataTemplate>
            </ListBox.ItemTemplate>
        </ListBox>
    </Grid>

    <!-- Slide-in editor backdrop -->
    <Border IsHitTestVisible="True" Visibility="{Binding IsEditorOpen, Converter={StaticResource BoolToVisibilityConverter}}"
            Background="#55000000">
        <Border.Style>
            <Style TargetType="Border">
                <Style.Triggers>
                    <DataTrigger Binding="{Binding IsEditorOpen}" Value="True">
                        <Setter Property="Visibility" Value="Visible"/>
                    </DataTrigger>
                </Style.Triggers>
            </Style>
        </Border.Style>
        <!-- Click backdrop to close -->
        <Border.InputBindings>
            <MouseBinding MouseAction="LeftClick" Command="{Binding CloseEditorCommand}"/>
        </Border.InputBindings>
    </Border>

    <!-- Slide-in editor panel (right side, 480px) -->
    <Border Width="480" HorizontalAlignment="Right" Background="{StaticResource PanelHighlightBrush}"
            Visibility="{Binding IsEditorOpen, Converter={StaticResource BoolToVisibilityConverter}}">
        <!-- Editor content: Rule name, movement type dropdown, dynamic fields, trigger sources, save/cancel -->
    </Border>
</Grid>
```

The editor panel should contain:
- Header: "Edit Movement Rule" + close button
- Rule name TextBox
- Movement type ComboBox (all PlayerMovementDirection values, grouped by category)
- Duration number input (min 1)
- Cooldown number input (min 0)
- Speed number input (only visible for Held Object types — use `Visibility bound to IsAxisType`)
- VR info box (only visible for VR-only types)
- Trigger source chips (CheckBox-style toggle buttons for each source)
- Save / Cancel buttons

- [ ] **Step 2: Create the code-behind**

```csharp
using System.Windows;
using VrcTwitchOscBridge.ViewModels;

namespace VrcTwitchOscBridge;

public sealed partial class MovementRedeemsManagerWindow : Window
{
    public MovementRedeemsManagerWindow(MovementRedeemsManagerViewModel viewModel)
    {
        DataContext = viewModel ?? throw new ArgumentNullException(nameof(viewModel));
        InitializeComponent();
    }
}
```

- [ ] **Step 3: Build**

```powershell
dotnet build "E:\!!!Program to work on\Proper Crystal Relay\VrcTwitchOscBridge\VrcTwitchOscBridge.csproj" --no-restore 2>&1
```

---

### Task 10: Add new files to csproj

**Files:**
- Modify: `VrcTwitchOscBridge\VrcTwitchOscBridge.csproj`

- [ ] **Step 1: Read current csproj**

Read `VrcTwitchOscBridge.csproj` to understand the existing file inclusion patterns (since `EnableDefaultItems=false`).

- [ ] **Step 2: Add new file references**

Add entries for:
```xml
<Page Include="MovementRedeemsManagerWindow.xaml" />
<Compile Include="MovementRedeemsManagerWindow.xaml.cs" />
<Compile Include="ViewModels\MovementRedeemsManagerViewModel.cs" />
<Compile Include="ViewModels\MovementRedeemCardViewModel.cs" />
<Compile Include="Models\MovementCategory.cs" />
<Compile Include="Models\MovementTypeClassifier.cs" />
```

- [ ] **Step 3: Full build**

```powershell
dotnet build "E:\!!!Program to work on\Proper Crystal Relay\VrcTwitchOscBridge\VrcTwitchOscBridge.csproj" --no-restore 2>&1
```

Expected: Build succeeds.

---

### Task 11: Write migration compatibility tests

**Files:**
- Create: `VrcTwitchOscBridge.Tests\MovementDirectionEnumMigrationTests.cs`

- [ ] **Step 1: Write enum backward-compatibility test**

```csharp
using VrcTwitchOscBridge.Models;
using Xunit;

namespace VrcTwitchOscBridge.Tests;

public sealed class MovementDirectionEnumMigrationTests
{
    [Fact]
    public void ExistingEnumValues_KeepIntegerPositions()
    {
        Assert.Equal(0, (int)PlayerMovementDirection.Forward);
        Assert.Equal(1, (int)PlayerMovementDirection.Backward);
        Assert.Equal(2, (int)PlayerMovementDirection.Left);
        Assert.Equal(3, (int)PlayerMovementDirection.Right);
        Assert.Equal(4, (int)PlayerMovementDirection.Jump);
        Assert.Equal(5, (int)PlayerMovementDirection.SpinLeft);
        Assert.Equal(6, (int)PlayerMovementDirection.SpinRight);
        Assert.Equal(7, (int)PlayerMovementDirection.StopMovement);
        Assert.Equal(8, (int)PlayerMovementDirection.StopTurning);
        Assert.Equal(9, (int)PlayerMovementDirection.StopAll);
        Assert.Equal(10, (int)PlayerMovementDirection.RandomMovement);
        Assert.Equal(11, (int)PlayerMovementDirection.GlitchyMovement);
    }

    [Fact]
    public void NewEnumValues_HaveCorrectPositions()
    {
        Assert.Equal(12, (int)PlayerMovementDirection.Run);
        Assert.Equal(13, (int)PlayerMovementDirection.LookHorizontal);
        Assert.Equal(14, (int)PlayerMovementDirection.LookLeft);
        Assert.Equal(15, (int)PlayerMovementDirection.LookRight);
        Assert.Equal(16, (int)PlayerMovementDirection.ComfortLeft);
        Assert.Equal(17, (int)PlayerMovementDirection.ComfortRight);
    }
}
```

- [ ] **Step 2: Write category classifier tests**

```csharp
[Fact]
public void MovementTypeClassifier_CategorizesForwardAsMovement()
{
    Assert.Equal(MovementCategory.Movement, MovementTypeClassifier.GetCategory(PlayerMovementDirection.Forward));
}

[Fact]
public void MovementTypeClassifier_CategorizesComfortLeftAsTurning()
{
    Assert.Equal(MovementCategory.Turning, MovementTypeClassifier.GetCategory(PlayerMovementDirection.ComfortLeft));
}

[Fact]
public void MovementTypeClassifier_ComfortLeftIsVrOnly()
{
    Assert.True(MovementTypeClassifier.IsVrOnly(PlayerMovementDirection.ComfortLeft));
}

[Fact]
public void MovementTypeClassifier_MoveForwardIsNotVrOnly()
{
    Assert.False(MovementTypeClassifier.IsVrOnly(PlayerMovementDirection.Forward));
}

[Fact]
public void MovementTypeClassifier_LookHorizontalIsAxis()
{
    Assert.True(MovementTypeClassifier.IsAxisType(PlayerMovementDirection.LookHorizontal));
}

[Fact]
public void MovementTypeClassifier_JumpIsNotAxis()
{
    Assert.False(MovementTypeClassifier.IsAxisType(PlayerMovementDirection.Jump));
}
```

- [ ] **Step 3: Run the tests**

```powershell
dotnet test "E:\!!!Program to work on\Proper Crystal Relay\VrcTwitchOscBridge.Tests\VrcTwitchOscBridge.Tests.csproj" --no-restore --filter "FullyQualifiedName~MovementDirectionEnumMigrationTests" 2>&1
```

Expected: All tests pass.

- [ ] **Step 4: Commit**

```powershell
git add -A
git commit -m "feat: redesign Movement Redeems with dedicated manager window and expanded OSC input types"
```
