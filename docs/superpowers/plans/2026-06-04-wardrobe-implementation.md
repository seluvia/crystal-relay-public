# Wardrobe System Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Add a Wardrobe mode to Avatar Sets that replaces numbered Set Trigger outfits with named outfits containing mixed Bool/Int/Float parameter snapshots, auto-capture restore, global cooldown, and flexible trigger support.

**Architecture:** Toggle `UseWardrobeMode` on `AvatarTriggerProfile`. New `WardrobeOutfit` and `WardrobeSnapshotParam` models replace old Set Trigger outfit rules in the UI when toggled on. `WardrobeExecutorService` handles capture → apply → restore lifecycle. Existing `OscRouterService` and `BridgeCoordinator` OSC pipeline is reused for sending packets.

**Tech Stack:** C#, WPF, .NET 10, ObservableObject pattern, OSC/OSCQuery, Twitch EventSub

---

### Task 1: Create WardrobeOutfit and WardrobeSnapshotParam Models

**Files:**
- Create: `VrcTwitchOscBridge/Models/WardrobeOutfit.cs`
- Create: `VrcTwitchOscBridge/Models/WardrobeSnapshotParam.cs`

- [ ] **Step 1: Create `WardrobeSnapshotParam.cs`**

```csharp
using VrcTwitchOscBridge.Infrastructure;
using VrcTwitchOscBridge.Services;

namespace VrcTwitchOscBridge.Models;

public sealed class WardrobeSnapshotParam : ObservableObject
{
    private Guid id = Guid.NewGuid();
    private string parameterName = string.Empty;
    private OscParameterType parameterType = OscParameterType.Bool;
    private string setValue = "True";

    private static string T(string sourceText) => LocalizationService.Translate(sourceText);
    private static string TF(string sourceFormat, params object[] args) => LocalizationService.Format(sourceFormat, args);

    public Guid Id
    {
        get => id;
        set => SetProperty(ref id, value == Guid.Empty ? Guid.NewGuid() : value);
    }

    public string ParameterName
    {
        get => parameterName;
        set
        {
            if (SetProperty(ref parameterName, value ?? string.Empty))
            {
                RaisePropertyChanged(nameof(DisplaySummary));
            }
        }
    }

    public OscParameterType ParameterType
    {
        get => parameterType;
        set
        {
            var normalizedValue = value is OscParameterType.Bool or OscParameterType.Int or OscParameterType.Float
                ? value
                : OscParameterType.Bool;
            if (SetProperty(ref parameterType, normalizedValue))
            {
                SetValue = normalizedValue switch
                {
                    OscParameterType.Bool => "True",
                    OscParameterType.Float => "0.0",
                    _ => "0"
                };
                RaisePropertyChanged(nameof(UsesBoolParameter));
                RaisePropertyChanged(nameof(UsesIntParameter));
                RaisePropertyChanged(nameof(UsesFloatParameter));
                RaisePropertyChanged(nameof(DisplaySummary));
            }
        }
    }

    public string SetValue
    {
        get => setValue;
        set
        {
            if (SetProperty(ref setValue, value ?? string.Empty))
            {
                RaisePropertyChanged(nameof(DisplaySummary));
            }
        }
    }

    public bool UsesBoolParameter => ParameterType == OscParameterType.Bool;
    public bool UsesIntParameter => ParameterType == OscParameterType.Int;
    public bool UsesFloatParameter => ParameterType == OscParameterType.Float;

    public string DisplaySummary
    {
        get
        {
            var param = string.IsNullOrWhiteSpace(ParameterName)
                ? T("Pick parameter")
                : ParameterName.Trim();
            var val = string.IsNullOrWhiteSpace(SetValue)
                ? T("Set value")
                : SetValue.Trim();
            return TF("{0} -> {1} ({2})", param, val, ParameterType);
        }
    }
}
```

- [ ] **Step 2: Create `WardrobeOutfit.cs`**

```csharp
using System.Collections.ObjectModel;
using System.Collections.Specialized;
using VrcTwitchOscBridge.Infrastructure;
using VrcTwitchOscBridge.Services;

namespace VrcTwitchOscBridge.Models;

public sealed class WardrobeOutfit : ObservableObject
{
    private Guid id = Guid.NewGuid();
    private bool isEnabled = true;
    private string name = "New Outfit";
    private int activeTimeSeconds = 30;
    private string twitchRewardId = string.Empty;
    private string twitchRewardTitle = string.Empty;
    private TwitchRewardSyncMode twitchRewardSyncMode = TwitchRewardSyncMode.CreateOrManage;
    private string chatCommandText = string.Empty;
    private ObservableCollection<WardrobeSnapshotParam> snapshotParams = [];

    private static string T(string sourceText) => LocalizationService.Translate(sourceText);
    private static string TF(string sourceFormat, params object[] args) => LocalizationService.Format(sourceFormat, args);

    public WardrobeOutfit()
    {
        snapshotParams.CollectionChanged += OnSnapshotParamsChanged;
    }

    public Guid Id
    {
        get => id;
        set => SetProperty(ref id, value == Guid.Empty ? Guid.NewGuid() : value);
    }

    public bool IsEnabled
    {
        get => isEnabled;
        set => SetProperty(ref isEnabled, value);
    }

    public string Name
    {
        get => name;
        set
        {
            if (SetProperty(ref name, value ?? string.Empty))
            {
                RaisePropertyChanged(nameof(DisplayTitle));
                RaisePropertyChanged(nameof(DisplaySummary));
            }
        }
    }

    public int ActiveTimeSeconds
    {
        get => activeTimeSeconds;
        set => SetProperty(ref activeTimeSeconds, Math.Max(1, value));
    }

    public string TwitchRewardId
    {
        get => twitchRewardId;
        set => SetProperty(ref twitchRewardId, value ?? string.Empty);
    }

    public string TwitchRewardTitle
    {
        get => twitchRewardTitle;
        set
        {
            if (SetProperty(ref twitchRewardTitle, value ?? string.Empty))
            {
                RaisePropertyChanged(nameof(DisplayTitle));
            }
        }
    }

    public TwitchRewardSyncMode TwitchRewardSyncMode
    {
        get => twitchRewardSyncMode;
        set
        {
            var normalizedValue = Enum.IsDefined(value)
                ? value
                : TwitchRewardSyncMode.CreateOrManage;
            if (SetProperty(ref twitchRewardSyncMode, normalizedValue))
            {
                RaisePropertyChanged(nameof(UsesCreateOrManageReward));
                RaisePropertyChanged(nameof(UsesLinkedExistingReward));
            }
        }
    }

    public bool UsesCreateOrManageReward => TwitchRewardSyncMode == TwitchRewardSyncMode.CreateOrManage;
    public bool UsesLinkedExistingReward => TwitchRewardSyncMode == TwitchRewardSyncMode.LinkExisting;

    public string ChatCommandText
    {
        get => chatCommandText;
        set => SetProperty(ref chatCommandText, value ?? string.Empty);
    }

    public ObservableCollection<WardrobeSnapshotParam> SnapshotParams
    {
        get => snapshotParams;
        set
        {
            if (ReferenceEquals(snapshotParams, value)) return;
            snapshotParams.CollectionChanged -= OnSnapshotParamsChanged;
            if (SetProperty(ref snapshotParams, value ?? []))
            {
                snapshotParams.CollectionChanged += OnSnapshotParamsChanged;
                RaisePropertyChanged(nameof(ParamCountText));
                RaisePropertyChanged(nameof(DisplaySummary));
            }
        }
    }

    public string DisplayTitle => !string.IsNullOrWhiteSpace(Name) ? Name : "New Outfit";
    public string DisplaySummary => TF("{0} ({1} param{2})", DisplayTitle, SnapshotParams.Count, SnapshotParams.Count == 1 ? string.Empty : "s");
    public string ParamCountText => SnapshotParams.Count == 1 ? "1 param" : $"{SnapshotParams.Count} params";

    private void OnSnapshotParamsChanged(object? sender, NotifyCollectionChangedEventArgs e)
    {
        RaisePropertyChanged(nameof(ParamCountText));
        RaisePropertyChanged(nameof(DisplaySummary));
    }
}
```

- [ ] **Step 3: Add to `VrcTwitchOscBridge.csproj`**

Add these lines under the existing `<Compile Include=...>` section:

```xml
<Compile Include="Models\WardrobeOutfit.cs" />
<Compile Include="Models\WardrobeSnapshotParam.cs" />
```

- [ ] **Step 4: Build and verify**

Run: `dotnet build "VrcTwitchOscBridge/VrcTwitchOscBridge.csproj" --no-restore`
Expected: Build succeeded, 0 warnings, 0 errors

- [ ] **Step 5: Commit**

```bash
git add VrcTwitchOscBridge/Models/WardrobeOutfit.cs VrcTwitchOscBridge/Models/WardrobeSnapshotParam.cs VrcTwitchOscBridge/VrcTwitchOscBridge.csproj
git commit -m "feat: add WardrobeOutfit and WardrobeSnapshotParam models"
```

---

### Task 2: Add Wardrobe Properties to AvatarTriggerProfile

**Files:**
- Modify: `VrcTwitchOscBridge/Models/AvatarTriggerProfile.cs`

- [ ] **Step 1: Add Wardrobe fields and properties**

Add these private fields after `private ObservableCollection<TriggerRule> channelPointRules = [];`:

```csharp
    private bool useWardrobeMode;
    private int wardrobeCooldownSeconds;
    private ObservableCollection<WardrobeOutfit> wardrobeOutfits = [];
    private bool useWardrobeMasterReward;
    private string wardrobeMasterRewardId = string.Empty;
    private string wardrobeMasterRewardTitle = string.Empty;
    private int wardrobeMasterRewardCost = 100;
    private TwitchRewardSyncMode wardrobeMasterRewardSyncMode = TwitchRewardSyncMode.CreateOrManage;
    private int wardrobeMasterRewardCooldownSeconds;
    private string wardrobeMasterRewardReadyColor = ManagedRewardPresentation.ReadyBackgroundColor;
    private string wardrobeMasterRewardCooldownColor = ManagedRewardPresentation.InUseBackgroundColor;
```

Add these properties after `PostOutfitChoiceListToTwitchChat`:

```csharp
    public bool UseWardrobeMode
    {
        get => useWardrobeMode;
        set
        {
            if (SetProperty(ref useWardrobeMode, value))
            {
                RaisePropertyChanged(nameof(WardrobeModeSummary));
            }
        }
    }

    public int WardrobeCooldownSeconds
    {
        get => wardrobeCooldownSeconds;
        set => SetProperty(ref wardrobeCooldownSeconds, Math.Max(0, value));
    }

    public ObservableCollection<WardrobeOutfit> WardrobeOutfits
    {
        get => wardrobeOutfits;
        set
        {
            if (ReferenceEquals(wardrobeOutfits, value)) return;
            wardrobeOutfits.CollectionChanged -= OnWardrobeOutfitsChanged;
            if (SetProperty(ref wardrobeOutfits, value ?? []))
            {
                wardrobeOutfits.CollectionChanged += OnWardrobeOutfitsChanged;
            }
        }
    }

    public bool UseWardrobeMasterReward
    {
        get => useWardrobeMasterReward;
        set => SetProperty(ref useWardrobeMasterReward, value);
    }

    public string WardrobeMasterRewardId
    {
        get => wardrobeMasterRewardId;
        set => SetProperty(ref wardrobeMasterRewardId, value ?? string.Empty);
    }

    public string WardrobeMasterRewardTitle
    {
        get => wardrobeMasterRewardTitle;
        set => SetProperty(ref wardrobeMasterRewardTitle, value ?? string.Empty);
    }

    public int WardrobeMasterRewardCost
    {
        get => wardrobeMasterRewardCost;
        set => SetProperty(ref wardrobeMasterRewardCost, Math.Max(1, value));
    }

    public TwitchRewardSyncMode WardrobeMasterRewardSyncMode
    {
        get => wardrobeMasterRewardSyncMode;
        set
        {
            var normalizedValue = Enum.IsDefined(value)
                ? value
                : TwitchRewardSyncMode.CreateOrManage;
            if (SetProperty(ref wardrobeMasterRewardSyncMode, normalizedValue))
            {
                RaisePropertyChanged(nameof(UsesCreateOrManageWardrobeMasterReward));
                RaisePropertyChanged(nameof(UsesLinkedExistingWardrobeMasterReward));
            }
        }
    }

    public bool UsesCreateOrManageWardrobeMasterReward =>
        WardrobeMasterRewardSyncMode == TwitchRewardSyncMode.CreateOrManage;

    public bool UsesLinkedExistingWardrobeMasterReward =>
        WardrobeMasterRewardSyncMode == TwitchRewardSyncMode.LinkExisting;

    public int WardrobeMasterRewardCooldownSeconds
    {
        get => wardrobeMasterRewardCooldownSeconds;
        set => SetProperty(ref wardrobeMasterRewardCooldownSeconds, Math.Max(0, value));
    }

    public string WardrobeMasterRewardReadyColor
    {
        get => wardrobeMasterRewardReadyColor;
        set
        {
            var normalizedValue = ManagedRewardPresentation.NormalizeReadyBackgroundColor(value);
            if (SetProperty(ref wardrobeMasterRewardReadyColor, normalizedValue))
            {
                RaisePropertyChanged(nameof(WardrobeMasterRewardReadyColorBrush));
            }
        }
    }

    public string WardrobeMasterRewardCooldownColor
    {
        get => wardrobeMasterRewardCooldownColor;
        set
        {
            var normalizedValue = ManagedRewardPresentation.NormalizeCooldownBackgroundColor(value);
            if (SetProperty(ref wardrobeMasterRewardCooldownColor, normalizedValue))
            {
                RaisePropertyChanged(nameof(WardrobeMasterRewardCooldownColorBrush));
            }
        }
    }

    public System.Windows.Media.Brush WardrobeMasterRewardReadyColorBrush => CreateColorBrush(WardrobeMasterRewardReadyColor);
    public System.Windows.Media.Brush WardrobeMasterRewardCooldownColorBrush => CreateColorBrush(WardrobeMasterRewardCooldownColor);

    public string WardrobeModeSummary => UseWardrobeMode
        ? "Named outfits with mixed Bool/Int/Float snapshots and auto-capture restore."
        : "Numbered outfit choices using Set Trigger. Toggle on to switch to Wardrobe mode.";
```

Add the collection change handler after `OnChannelPointRulesChanged`:

```csharp
    private void OnWardrobeOutfitsChanged(object? sender, NotifyCollectionChangedEventArgs e)
    {
        // Future: raise aggregate properties if needed
    }
```

- [ ] **Step 2: Build and verify**

Run: `dotnet build "VrcTwitchOscBridge/VrcTwitchOscBridge.csproj" --no-restore`
Expected: Build succeeded, 0 warnings, 0 errors

- [ ] **Step 3: Commit**

```bash
git add VrcTwitchOscBridge/Models/AvatarTriggerProfile.cs
git commit -m "feat: add Wardrobe properties to AvatarTriggerProfile"
```

---

### Task 3: Add Persistence (SettingsStore)

**Files:**
- Modify: `VrcTwitchOscBridge/Services/SettingsStore.cs`

- [ ] **Step 1: Add persisted DTO classes**

Add these nested classes at the end of the `SettingsStore` class (before the closing brace), after `PersistedAvatarTriggerProfile`:

```csharp
    private sealed class PersistedWardrobeOutfit
    {
        public Guid Id { get; set; }
        public bool IsEnabled { get; set; }
        public string? Name { get; set; }
        public int ActiveTimeSeconds { get; set; }
        public string? TwitchRewardId { get; set; }
        public string? TwitchRewardTitle { get; set; }
        public TwitchRewardSyncMode TwitchRewardSyncMode { get; set; }
        public string? ChatCommandText { get; set; }
        public List<PersistedWardrobeSnapshotParam>? SnapshotParams { get; set; }
    }

    private sealed class PersistedWardrobeSnapshotParam
    {
        public Guid Id { get; set; }
        public string? ParameterName { get; set; }
        public OscParameterType ParameterType { get; set; }
        public string? SetValue { get; set; }
    }
```

- [ ] **Step 2: Update `PersistedAvatarTriggerProfile`**

Add these properties inside the `PersistedAvatarTriggerProfile` class (after `ChannelPointRules`):

```csharp
        public bool UseWardrobeMode { get; set; }
        public int WardrobeCooldownSeconds { get; set; }
        public List<PersistedWardrobeOutfit>? WardrobeOutfits { get; set; }
        public bool UseWardrobeMasterReward { get; set; }
        public string? WardrobeMasterRewardId { get; set; }
        public string? WardrobeMasterRewardTitle { get; set; }
        public int WardrobeMasterRewardCost { get; set; }
        public TwitchRewardSyncMode WardrobeMasterRewardSyncMode { get; set; }
        public int WardrobeMasterRewardCooldownSeconds { get; set; }
        public string? WardrobeMasterRewardReadyColor { get; set; }
        public string? WardrobeMasterRewardCooldownColor { get; set; }
```

- [ ] **Step 3: Update `ToPersistedAvatarProfile`**

Add these lines inside `ToPersistedAvatarProfile`, after `ChannelPointRules = [.. profile.ChannelPointRules.Select(ToPersistedRule)]`:

```csharp
            UseWardrobeMode = profile.UseWardrobeMode,
            WardrobeCooldownSeconds = profile.WardrobeCooldownSeconds,
            WardrobeOutfits = [.. profile.WardrobeOutfits.Select(ToPersistedWardrobeOutfit)],
            UseWardrobeMasterReward = profile.UseWardrobeMasterReward,
            WardrobeMasterRewardId = profile.WardrobeMasterRewardId,
            WardrobeMasterRewardTitle = profile.WardrobeMasterRewardTitle,
            WardrobeMasterRewardCost = profile.WardrobeMasterRewardCost,
            WardrobeMasterRewardSyncMode = profile.WardrobeMasterRewardSyncMode,
            WardrobeMasterRewardCooldownSeconds = profile.WardrobeMasterRewardCooldownSeconds,
            WardrobeMasterRewardReadyColor = profile.WardrobeMasterRewardReadyColor,
            WardrobeMasterRewardCooldownColor = profile.WardrobeMasterRewardCooldownColor,
```

- [ ] **Step 4: Add `ToPersistedWardrobeOutfit` and `ToPersistedWardrobeSnapshotParam` methods**

Add these private static methods near `ToPersistedRule`:

```csharp
    private static PersistedWardrobeOutfit ToPersistedWardrobeOutfit(WardrobeOutfit outfit)
    {
        return new PersistedWardrobeOutfit
        {
            Id = outfit.Id,
            IsEnabled = outfit.IsEnabled,
            Name = outfit.Name,
            ActiveTimeSeconds = outfit.ActiveTimeSeconds,
            TwitchRewardId = outfit.TwitchRewardId,
            TwitchRewardTitle = outfit.TwitchRewardTitle,
            TwitchRewardSyncMode = outfit.TwitchRewardSyncMode,
            ChatCommandText = outfit.ChatCommandText,
            SnapshotParams = [.. outfit.SnapshotParams.Select(ToPersistedWardrobeSnapshotParam)]
        };
    }

    private static PersistedWardrobeSnapshotParam ToPersistedWardrobeSnapshotParam(WardrobeSnapshotParam param)
    {
        return new PersistedWardrobeSnapshotParam
        {
            Id = param.Id,
            ParameterName = param.ParameterName,
            ParameterType = param.ParameterType,
            SetValue = param.SetValue
        };
    }
```

- [ ] **Step 5: Update `ToAvatarProfile`**

Add these lines inside `ToAvatarProfile`, after the `ChannelPointRules` assignment:

```csharp
            UseWardrobeMode = profile.UseWardrobeMode,
            WardrobeCooldownSeconds = Math.Max(0, profile.WardrobeCooldownSeconds),
            WardrobeOutfits = new ObservableCollection<WardrobeOutfit>((profile.WardrobeOutfits ?? []).Select(ToWardrobeOutfit)),
            UseWardrobeMasterReward = profile.UseWardrobeMasterReward,
            WardrobeMasterRewardId = profile.WardrobeMasterRewardId ?? string.Empty,
            WardrobeMasterRewardTitle = profile.WardrobeMasterRewardTitle ?? string.Empty,
            WardrobeMasterRewardCost = profile.WardrobeMasterRewardCost <= 0 ? 100 : profile.WardrobeMasterRewardCost,
            WardrobeMasterRewardSyncMode = Enum.IsDefined(profile.WardrobeMasterRewardSyncMode)
                ? profile.WardrobeMasterRewardSyncMode
                : TwitchRewardSyncMode.CreateOrManage,
            WardrobeMasterRewardCooldownSeconds = Math.Max(0, profile.WardrobeMasterRewardCooldownSeconds),
            WardrobeMasterRewardReadyColor = ManagedRewardPresentation.NormalizeReadyBackgroundColor(profile.WardrobeMasterRewardReadyColor),
            WardrobeMasterRewardCooldownColor = ManagedRewardPresentation.NormalizeCooldownBackgroundColor(profile.WardrobeMasterRewardCooldownColor),
```

- [ ] **Step 6: Add `ToWardrobeOutfit` and `ToWardrobeSnapshotParam` methods**

```csharp
    private static WardrobeOutfit ToWardrobeOutfit(PersistedWardrobeOutfit persisted)
    {
        return new WardrobeOutfit
        {
            Id = persisted.Id == Guid.Empty ? Guid.NewGuid() : persisted.Id,
            IsEnabled = persisted.IsEnabled,
            Name = string.IsNullOrWhiteSpace(persisted.Name) ? "New Outfit" : persisted.Name,
            ActiveTimeSeconds = persisted.ActiveTimeSeconds <= 0 ? 30 : persisted.ActiveTimeSeconds,
            TwitchRewardId = persisted.TwitchRewardId ?? string.Empty,
            TwitchRewardTitle = persisted.TwitchRewardTitle ?? string.Empty,
            TwitchRewardSyncMode = Enum.IsDefined(persisted.TwitchRewardSyncMode)
                ? persisted.TwitchRewardSyncMode
                : TwitchRewardSyncMode.CreateOrManage,
            ChatCommandText = persisted.ChatCommandText ?? string.Empty,
            SnapshotParams = new ObservableCollection<WardrobeSnapshotParam>((persisted.SnapshotParams ?? []).Select(ToWardrobeSnapshotParam))
        };
    }

    private static WardrobeSnapshotParam ToWardrobeSnapshotParam(PersistedWardrobeSnapshotParam persisted)
    {
        return new WardrobeSnapshotParam
        {
            Id = persisted.Id == Guid.Empty ? Guid.NewGuid() : persisted.Id,
            ParameterName = persisted.ParameterName ?? string.Empty,
            ParameterType = Enum.IsDefined(persisted.ParameterType)
                ? persisted.ParameterType
                : OscParameterType.Bool,
            SetValue = persisted.SetValue ?? string.Empty
        };
    }
```

- [ ] **Step 7: Build and verify**

Run: `dotnet build "VrcTwitchOscBridge/VrcTwitchOscBridge.csproj" --no-restore`
Expected: Build succeeded, 0 warnings, 0 errors

- [ ] **Step 8: Commit**

```bash
git add VrcTwitchOscBridge/Services/SettingsStore.cs
git commit -m "feat: add Wardrobe persistence to SettingsStore"
```

---

### Task 4: Add Runtime Snapshot Records and Conversion

**Files:**
- Modify: `VrcTwitchOscBridge/Services/BridgeRuntimeConfiguration.cs`

- [ ] **Step 1: Add snapshot records**

Add these records after `SetTriggerActionSnapshot`:

```csharp
public sealed record WardrobeOutfitSnapshot(
    Guid Id,
    bool IsEnabled,
    string Name,
    Guid AvatarProfileId,
    string AvatarId,
    int ActiveTimeSeconds,
    int CooldownSeconds,
    IReadOnlyList<WardrobeParamSnapshot> Params,
    bool UsesMasterReward);

public sealed record WardrobeParamSnapshot(
    string ParameterName,
    OscParameterType ParameterType,
    string SetValue);
```

- [ ] **Step 2: Add `TryToWardrobeSnapshot` method**

Add this private static method near the other `TryTo*Snapshot` methods:

```csharp
    private static bool TryToWardrobeSnapshot(
        WardrobeOutfit outfit,
        AvatarTriggerProfile profile,
        out WardrobeOutfitSnapshot snapshot)
    {
        snapshot = default!;

        if (!outfit.IsEnabled || string.IsNullOrWhiteSpace(profile.AvatarId))
        {
            return false;
        }

        var validParams = outfit.SnapshotParams
            .Where(p => !string.IsNullOrWhiteSpace(p.ParameterName)
                     && !string.IsNullOrWhiteSpace(p.SetValue)
                     && p.ParameterType is OscParameterType.Bool or OscParameterType.Int or OscParameterType.Float)
            .Select(p => new WardrobeParamSnapshot(
                VrChatOscClient.NormalizeAvatarParameterAddress(p.ParameterName),
                p.ParameterType,
                p.SetValue))
            .ToList();

        if (validParams.Count == 0)
        {
            return false;
        }

        snapshot = new WardrobeOutfitSnapshot(
            outfit.Id,
            outfit.IsEnabled,
            outfit.DisplayTitle,
            profile.Id,
            profile.AvatarId,
            Math.Max(1, outfit.ActiveTimeSeconds),
            Math.Max(0, profile.WardrobeCooldownSeconds),
            validParams,
            profile.UseWardrobeMasterReward);
        return true;
    }
```

- [ ] **Step 3: Build and verify**

Run: `dotnet build "VrcTwitchOscBridge/VrcTwitchOscBridge.csproj" --no-restore`
Expected: Build succeeded, 0 warnings, 0 errors

- [ ] **Step 4: Commit**

```bash
git add VrcTwitchOscBridge/Services/BridgeRuntimeConfiguration.cs
git commit -m "feat: add Wardrobe snapshot records and conversion"
```

---

### Task 5: Create WardrobeExecutorService

**Files:**
- Create: `VrcTwitchOscBridge/Services/WardrobeExecutorService.cs`

- [ ] **Step 1: Create the service**

```csharp
using System.Diagnostics;
using VrcTwitchOscBridge.Models;

namespace VrcTwitchOscBridge.Services;

/// <summary>
/// Executes Wardrobe outfit snapshots: capture current values, apply outfit, restore on timeout.
/// </summary>
internal sealed class WardrobeExecutorService : IAsyncDisposable
{
    private readonly VrChatOscClient oscClient;
    private readonly OscRouterService oscRouterService;
    private readonly VrChatLocalOscCacheService localOscCacheService;
    private readonly Action<string> logWritten;
    private readonly object stateGate = new();
    private readonly Dictionary<Guid, CancellationTokenSource> activeRestores = new();
    private DateTimeOffset? wardrobeCooldownUntil;

    public WardrobeExecutorService(
        VrChatOscClient oscClient,
        OscRouterService oscRouterService,
        VrChatLocalOscCacheService localOscCacheService,
        Action<string> logWritten)
    {
        this.oscClient = oscClient;
        this.oscRouterService = oscRouterService;
        this.localOscCacheService = localOscCacheService;
        this.logWritten = logWritten;
    }

    /// <summary>
    /// Checks if the Wardrobe is currently on cooldown.
    /// </summary>
    public bool IsOnCooldown()
    {
        lock (stateGate)
        {
            return wardrobeCooldownUntil.HasValue && wardrobeCooldownUntil.Value > DateTimeOffset.UtcNow;
        }
    }

    /// <summary>
    /// Executes a Wardrobe outfit: validates params, captures current values, applies snapshot, schedules restore.
    /// Returns true if the outfit was applied, false if blocked.
    /// </summary>
    public async Task<bool> ExecuteOutfitAsync(
        WardrobeOutfitSnapshot snapshot,
        string vrChatUserId,
        CancellationToken cancellationToken = default)
    {
        // Check global cooldown
        if (IsOnCooldown())
        {
            logWritten($"Wardrobe outfit '{snapshot.Name}' blocked: Wardrobe is on cooldown.");
            return false;
        }

        // Validate all params exist on current avatar
        var avatarFilePath = VrChatLocalOscCacheService.GetAvatarOscFilePath(vrChatUserId, snapshot.AvatarId);
        if (string.IsNullOrWhiteSpace(avatarFilePath) || !System.IO.File.Exists(avatarFilePath))
        {
            logWritten($"Wardrobe outfit '{snapshot.Name}' blocked: Avatar parameter cache not available for '{snapshot.AvatarId}'.");
            return false;
        }

        var cachedParams = await localOscCacheService.LoadAvatarParametersAsync(vrChatUserId, snapshot.AvatarId, cancellationToken);
        var cachedParamNames = new HashSet<string>(cachedParams.Select(p => p.Address), StringComparer.OrdinalIgnoreCase);

        foreach (var param in snapshot.Params)
        {
            if (!cachedParamNames.Contains(param.ParameterName))
            {
                var shortName = param.ParameterName.Split('/').LastOrDefault() ?? param.ParameterName;
                logWritten($"Wardrobe outfit '{snapshot.Name}' blocked: Parameter '{shortName}' not found on current avatar.");
                return false;
            }
        }

        // Auto-capture current values from VRChat via OSCQuery
        var capturedValues = new Dictionary<string, OscObservedValue?>();
        foreach (var param in snapshot.Params)
        {
            var currentValue = await oscRouterService.GetCurrentOscValueAsync(param.ParameterName, cancellationToken);
            capturedValues[param.ParameterName] = currentValue;
        }

        // Cancel any previous restore for this outfit (independent snapshots: last one wins)
        CancelActiveRestore(snapshot.Id);

        // Apply snapshot
        var packets = new List<byte[]>();
        foreach (var param in snapshot.Params)
        {
            var packet = oscClient.BuildAvatarParameterPacket(param.ParameterName, param.ParameterType, param.SetValue);
            packets.Add(packet);
        }

        await SendPacketsAsync(packets, cancellationToken);
        logWritten($"Applied Wardrobe outfit '{snapshot.Name}' ({packets.Count} params).");

        // Schedule restore
        var restoreCts = new CancellationTokenSource();
        lock (stateGate)
        {
            activeRestores[snapshot.Id] = restoreCts;
        }

        _ = Task.Run(async () =>
        {
            try
            {
                await Task.Delay(TimeSpan.FromSeconds(snapshot.ActiveTimeSeconds), restoreCts.Token);

                if (restoreCts.IsCancellationRequested) return;

                // Restore captured values
                var restorePackets = new List<byte[]>();
                foreach (var param in snapshot.Params)
                {
                    var captured = capturedValues[param.ParameterName];
                    if (captured.HasValue)
                    {
                        var restorePacket = oscClient.BuildAvatarParameterPacket(
                            param.ParameterName,
                            param.ParameterType,
                            captured.Value.ToString());
                        restorePackets.Add(restorePacket);
                    }
                }

                await SendPacketsAsync(restorePackets, CancellationToken.None);
                logWritten($"Restored Wardrobe outfit '{snapshot.Name}' captured values ({restorePackets.Count} params).");

                // Start global cooldown after restore
                if (snapshot.CooldownSeconds > 0)
                {
                    lock (stateGate)
                    {
                        wardrobeCooldownUntil = DateTimeOffset.UtcNow.AddSeconds(snapshot.CooldownSeconds);
                    }
                    logWritten($"Wardrobe cooldown started: {snapshot.CooldownSeconds}s.");
                }
            }
            catch (OperationCanceledException)
            {
                // Restore was cancelled by a newer outfit
            }
            catch (Exception ex)
            {
                logWritten($"Wardrobe restore failed for '{snapshot.Name}': {ex.Message}");
            }
            finally
            {
                lock (stateGate)
                {
                    activeRestores.Remove(snapshot.Id);
                }
                restoreCts.Dispose();
            }
        }, CancellationToken.None);

        return true;
    }

    /// <summary>
    /// Cancels the active restore timer for a specific outfit.
    /// </summary>
    private void CancelActiveRestore(Guid outfitId)
    {
        lock (stateGate)
        {
            if (activeRestores.TryGetValue(outfitId, out var cts))
            {
                cts.Cancel();
                activeRestores.Remove(outfitId);
            }
        }
    }

    private async Task SendPacketsAsync(IEnumerable<byte[]> packets, CancellationToken cancellationToken)
    {
        var spacing = TimeSpan.FromMilliseconds(80);
        var sentAny = false;
        foreach (var packet in packets)
        {
            if (sentAny && spacing > TimeSpan.Zero)
            {
                await Task.Delay(spacing, cancellationToken);
            }
            await oscRouterService.SendToVrChatAsync(packet, cancellationToken);
            sentAny = true;
        }
    }

    public async ValueTask DisposeAsync()
    {
        lock (stateGate)
        {
            foreach (var cts in activeRestores.Values)
            {
                cts.Cancel();
                cts.Dispose();
            }
            activeRestores.Clear();
        }
        await Task.CompletedTask;
    }
}
```

- [ ] **Step 2: Add to `VrcTwitchOscBridge.csproj`**

```xml
<Compile Include="Services\WardrobeExecutorService.cs" />
```

- [ ] **Step 3: Build and verify**

Run: `dotnet build "VrcTwitchOscBridge/VrcTwitchOscBridge.csproj" --no-restore`
Expected: Build succeeded, 0 warnings, 0 errors

- [ ] **Step 4: Commit**

```bash
git add VrcTwitchOscBridge/Services/WardrobeExecutorService.cs VrcTwitchOscBridge/VrcTwitchOscBridge.csproj
git commit -m "feat: add WardrobeExecutorService for capture/apply/restore lifecycle"
```

---

### Task 6: Integrate Wardrobe into BridgeCoordinator

**Files:**
- Modify: `VrcTwitchOscBridge/Services/BridgeCoordinator.cs`

- [ ] **Step 1: Add WardrobeExecutorService field and constructor parameter**

Find the constructor parameters and add:

```csharp
        WardrobeExecutorService wardrobeExecutor,
```

Find the field declarations and add:

```csharp
    private readonly WardrobeExecutorService wardrobeExecutor;
```

Assign it in the constructor body:

```csharp
        this.wardrobeExecutor = wardrobeExecutor;
```

- [ ] **Step 2: Add Wardrobe execution method**

Add this public method near the other execution methods:

```csharp
    /// <summary>
    /// Executes a Wardrobe outfit snapshot. Returns true if applied, false if blocked.
    /// </summary>
    public async Task<bool> ExecuteWardrobeOutfitAsync(
        WardrobeOutfitSnapshot snapshot,
        CancellationToken cancellationToken = default)
    {
        var vrChatUserId = activeConfiguration?.VrChat?.UserId?.Trim() ?? string.Empty;
        if (string.IsNullOrWhiteSpace(vrChatUserId))
        {
            WriteLog("Wardrobe outfit blocked: VRChat user ID not configured.");
            return false;
        }

        var applied = await wardrobeExecutor.ExecuteOutfitAsync(snapshot, vrChatUserId, cancellationToken);
        if (applied)
        {
            WriteLog($"Wardrobe outfit '{snapshot.Name}' applied successfully.");
        }
        return applied;
    }
```

- [ ] **Step 3: Build and verify**

Run: `dotnet build "VrcTwitchOscBridge/VrcTwitchOscBridge.csproj" --no-restore`
Expected: Build will fail — WardrobeExecutorService not yet injected into BridgeCoordinator constructor. This is expected; Task 7 will fix it.

---

### Task 7: Wire Up Wardrobe in MainWindowViewModel

**Files:**
- Modify: `VrcTwitchOscBridge/ViewModels/MainWindowViewModel.cs`

- [ ] **Step 1: Add WardrobeExecutorService field**

Add after the existing service fields:

```csharp
    private readonly WardrobeExecutorService wardrobeExecutorService;
```

- [ ] **Step 2: Initialize in constructor**

Find where `bridgeCoordinator` is constructed and add before it:

```csharp
        wardrobeExecutorService = new WardrobeExecutorService(
            vrChatOscClient,
            oscRouterService,
            vrChatLocalOscCacheService,
            msg => AppendLog(msg));
```

Pass it to `bridgeCoordinator` constructor. Find the `new BridgeCoordinator(` call and add `wardrobeExecutorService` to the argument list at the appropriate position.

- [ ] **Step 3: Add Wardrobe UI commands and state**

Add these properties and commands near the existing Avatar Set commands:

```csharp
    public WardrobeOutfit? SelectedWardrobeOutfit
    {
        get => selectedWardrobeOutfit;
        set
        {
            if (SetProperty(ref selectedWardrobeOutfit, value))
            {
                AddWardrobeSnapshotParamCommand.NotifyCanExecuteChanged();
                RemoveWardrobeSnapshotParamCommand.NotifyCanExecuteChanged();
            }
        }
    }

    public WardrobeSnapshotParam? SelectedWardrobeSnapshotParam
    {
        get => selectedWardrobeSnapshotParam;
        set => SetProperty(ref selectedWardrobeSnapshotParam, value);
    }

    public IReadOnlyList<VrChatOscParameterSummary> AvailableWardrobeParameters
    {
        get => availableWardrobeParameters;
        private set => SetProperty(ref availableWardrobeParameters, value);
    }
```

Add commands:

```csharp
    public RelayCommand AddWardrobeOutfitCommand { get; }
    public RelayCommand RemoveWardrobeOutfitCommand { get; }
    public RelayCommand AddWardrobeSnapshotParamCommand { get; }
    public RelayCommand RemoveWardrobeSnapshotParamCommand { get; }
    public RelayCommand RefreshWardrobeParametersCommand { get; }
```

Initialize in constructor:

```csharp
        AddWardrobeOutfitCommand = new RelayCommand(AddWardrobeOutfit, () => IsViewingAvatarTriggers && SelectedAvatarProfile is not null);
        RemoveWardrobeOutfitCommand = new RelayCommand(RemoveWardrobeOutfit, () => IsViewingAvatarTriggers && SelectedWardrobeOutfit is not null);
        AddWardrobeSnapshotParamCommand = new RelayCommand(AddWardrobeSnapshotParam, () => SelectedWardrobeOutfit is not null);
        RemoveWardrobeSnapshotParamCommand = new RelayCommand(RemoveWardrobeSnapshotParam, () => SelectedWardrobeSnapshotParam is not null);
        RefreshWardrobeParametersCommand = new RelayCommand(async () => await RefreshWardrobeParametersAsync());
```

- [ ] **Step 4: Add command implementations**

```csharp
    private void AddWardrobeOutfit()
    {
        if (SelectedAvatarProfile is null) return;
        var outfit = new WardrobeOutfit();
        SelectedAvatarProfile.WardrobeOutfits.Add(outfit);
        SelectedWardrobeOutfit = outfit;
        AppendLog($"Added Wardrobe outfit '{outfit.Name}'.");
    }

    private void RemoveWardrobeOutfit()
    {
        if (SelectedAvatarProfile is null || SelectedWardrobeOutfit is null) return;
        var outfit = SelectedWardrobeOutfit;
        SelectedAvatarProfile.WardrobeOutfits.Remove(outfit);
        SelectedWardrobeOutfit = SelectedAvatarProfile.WardrobeOutfits.FirstOrDefault();
        AppendLog($"Removed Wardrobe outfit '{outfit.Name}'.");
    }

    private void AddWardrobeSnapshotParam()
    {
        if (SelectedWardrobeOutfit is null) return;
        var param = new WardrobeSnapshotParam();
        SelectedWardrobeOutfit.SnapshotParams.Add(param);
        SelectedWardrobeSnapshotParam = param;
    }

    private void RemoveWardrobeSnapshotParam()
    {
        if (SelectedWardrobeOutfit is null || SelectedWardrobeSnapshotParam is null) return;
        var param = SelectedWardrobeSnapshotParam;
        var index = SelectedWardrobeOutfit.SnapshotParams.IndexOf(param);
        SelectedWardrobeOutfit.SnapshotParams.Remove(param);
        SelectedWardrobeSnapshotParam = index < SelectedWardrobeOutfit.SnapshotParams.Count
            ? SelectedWardrobeOutfit.SnapshotParams[index]
            : SelectedWardrobeOutfit.SnapshotParams.FirstOrDefault();
    }

    private async Task RefreshWardrobeParametersAsync()
    {
        if (Settings?.VrChat?.UserId is not { } userId || string.IsNullOrWhiteSpace(userId)) return;
        if (SelectedAvatarProfile?.AvatarId is not { } avatarId || string.IsNullOrWhiteSpace(avatarId)) return;

        try
        {
            var parameters = await vrChatLocalOscCacheService.LoadAvatarParametersAsync(userId, avatarId, CancellationToken.None);
            AvailableWardrobeParameters = parameters;
        }
        catch (Exception ex)
        {
            AppendLog($"Could not load avatar parameters for Wardrobe: {ex.Message}");
            AvailableWardrobeParameters = [];
        }
    }
```

- [ ] **Step 5: Add private fields**

```csharp
    private WardrobeOutfit? selectedWardrobeOutfit;
    private WardrobeSnapshotParam? selectedWardrobeSnapshotParam;
    private IReadOnlyList<VrChatOscParameterSummary> availableWardrobeParameters = [];
```

- [ ] **Step 6: NotifyCanExecuteChanged on profile change**

Find where `SelectedAvatarProfile` setter calls `NotifyCanExecuteChanged` for existing commands and add:

```csharp
                AddWardrobeOutfitCommand.NotifyCanExecuteChanged();
                RemoveWardrobeOutfitCommand.NotifyCanExecuteChanged();
```

- [ ] **Step 7: Build and verify**

Run: `dotnet build "VrcTwitchOscBridge/VrcTwitchOscBridge.csproj" --no-restore`
Expected: Build succeeded, 0 warnings, 0 errors

- [ ] **Step 8: Commit**

```bash
git add VrcTwitchOscBridge/ViewModels/MainWindowViewModel.cs
git commit -m "feat: add Wardrobe commands and state to MainWindowViewModel"
```

---

### Task 8: Add Wardrobe Editor UI to MainWindow.xaml

**Files:**
- Modify: `VrcTwitchOscBridge/MainWindow.xaml`

- [ ] **Step 1: Add Wardrobe Mode toggle**

Find the "Use Shared Numbered Outfit Reward" checkbox section (around line 6780). Add the Wardrobe Mode toggle checkbox right after it:

```xml
<CheckBox Margin="0,12,0,0"
          IsChecked="{Binding UseWardrobeMode, UpdateSourceTrigger=PropertyChanged}">
    <TextBlock Text="{loc:Translate 'Use Wardrobe Mode'}"
               TextWrapping="Wrap" />
</CheckBox>
<TextBlock Margin="26,6,0,0"
           Foreground="{DynamicResource MutedBrush}"
           TextWrapping="Wrap"
           Text="{loc:Translate 'Named outfits with mixed Bool/Int/Float snapshots and auto-capture restore. Existing outfit choices are preserved but hidden while Wardrobe mode is on.'}" />
```

- [ ] **Step 2: Add conditional visibility for old vs new outfit UI**

Wrap the existing outfit choices section (from "Add Outfit" button through the ListBox and empty state text, approximately lines 6816-6846) in a StackPanel with visibility bound to `UseWardrobeMode` inverted:

```xml
<StackPanel>
    <StackPanel.Style>
        <Style TargetType="StackPanel">
            <Setter Property="Visibility" Value="Visible" />
            <Style.Triggers>
                <DataTrigger Binding="{Binding UseWardrobeMode}" Value="True">
                    <Setter Property="Visibility" Value="Collapsed" />
                </DataTrigger>
            </Style.Triggers>
        </Style>
    </StackPanel.Style>
    <!-- EXISTING OUTFIT CHOICES UI (lines 6816-6846) goes here unchanged -->
</StackPanel>
```

- [ ] **Step 3: Add Wardrobe Editor Panel**

Add this after the old outfit choices section (inside the same parent container):

```xml
<StackPanel Margin="0,12,0,0">
    <StackPanel.Style>
        <Style TargetType="StackPanel">
            <Setter Property="Visibility" Value="Collapsed" />
            <Style.Triggers>
                <DataTrigger Binding="{Binding UseWardrobeMode}" Value="True">
                    <Setter Property="Visibility" Value="Visible" />
                </DataTrigger>
            </Style.Triggers>
        </Style>
    </StackPanel.Style>

    <!-- Add/Remove Outfit Buttons -->
    <WrapPanel>
        <Button Style="{StaticResource PrimaryButtonStyle}"
                Content="{loc:Translate 'Add Outfit'}"
                Command="{Binding AddWardrobeOutfitCommand}" />
        <Button Style="{StaticResource SecondaryButtonStyle}"
                Content="{loc:Translate 'Delete Outfit'}"
                Command="{Binding RemoveWardrobeOutfitCommand}" />
    </WrapPanel>

    <!-- Outfit List -->
    <ListBox Margin="0,12,0,0"
             MaxHeight="200"
             ItemsSource="{Binding SelectedAvatarProfile.WardrobeOutfits}"
             SelectedItem="{Binding SelectedWardrobeOutfit, Mode=TwoWay}"
             ScrollViewer.CanContentScroll="True"
             VirtualizingStackPanel.IsVirtualizing="True"
             VirtualizingStackPanel.VirtualizationMode="Recycling">
        <ListBox.ItemTemplate>
            <DataTemplate DataType="{x:Type models:WardrobeOutfit}">
                <TextBlock Text="{Binding DisplaySummary}"
                           Foreground="{DynamicResource TextBrush}"
                           TextTrimming="CharacterEllipsis" />
            </DataTemplate>
        </ListBox.ItemTemplate>
    </ListBox>

    <!-- Empty State -->
    <TextBlock Margin="0,8,0,0"
               Text="{loc:Translate 'No outfits yet. Add Outfit to create your first Wardrobe outfit.'}"
               Foreground="{DynamicResource MutedBrush}"
               TextWrapping="Wrap">
        <TextBlock.Style>
            <Style TargetType="TextBlock">
                <Setter Property="Visibility" Value="Collapsed" />
                <Style.Triggers>
                    <DataTrigger Binding="{Binding SelectedAvatarProfile.WardrobeOutfits.Count}" Value="0">
                        <Setter Property="Visibility" Value="Visible" />
                    </DataTrigger>
                </Style.Triggers>
            </Style>
        </TextBlock.Style>
    </TextBlock>

    <!-- Selected Outfit Editor -->
    <Border Margin="0,14,0,0"
            Background="{DynamicResource NestedPanelBrush}"
            BorderBrush="{DynamicResource HighlightBorderBrush}"
            BorderThickness="1"
            CornerRadius="16"
            Padding="14"
            Visibility="{Binding SelectedWardrobeOutfit, Converter={StaticResource NullToVisibilityConverter}}">
        <StackPanel DataContext="{Binding SelectedWardrobeOutfit}">
            <!-- Outfit Name -->
            <TextBlock Text="{loc:Translate 'Outfit Name'}"
                       Foreground="{DynamicResource TextBrush}"
                       FontWeight="SemiBold" />
            <TextBox Margin="0,6,0,0"
                     Text="{Binding Name, UpdateSourceTrigger=PropertyChanged}" />

            <!-- Active Time -->
            <StackPanel Margin="0,12,0,0" Orientation="Horizontal">
                <TextBlock Text="{loc:Translate 'Active Time:'}"
                           Foreground="{DynamicResource TextBrush}"
                           FontWeight="SemiBold"
                           VerticalAlignment="Center" />
                <TextBox Width="60"
                         Margin="8,0,0,0"
                         Text="{Binding ActiveTimeSeconds, UpdateSourceTrigger=PropertyChanged}" />
                <TextBlock Margin="4,0,0,0"
                           Text="{loc:Translate 'seconds'}"
                           Foreground="{DynamicResource MutedBrush}"
                           VerticalAlignment="Center" />
            </StackPanel>

            <!-- Twitch Reward Sync -->
            <StackPanel Margin="0,12,0,0">
                <TextBlock Text="{loc:Translate 'Twitch Reward'}"
                           Foreground="{DynamicResource TextBrush}"
                           FontWeight="SemiBold" />
                <ComboBox ItemsSource="{Binding DataContext.RewardSyncModeOptions, RelativeSource={RelativeSource AncestorType=Window}}"
                          DisplayMemberPath="Label"
                          SelectedValuePath="Value"
                          SelectedValue="{Binding TwitchRewardSyncMode, UpdateSourceTrigger=PropertyChanged}" />
            </StackPanel>

            <!-- Chat Command -->
            <StackPanel Margin="0,12,0,0">
                <TextBlock Text="{loc:Translate 'Chat Command'}"
                           Foreground="{DynamicResource TextBrush}"
                           FontWeight="SemiBold" />
                <TextBox Text="{Binding ChatCommandText, UpdateSourceTrigger=PropertyChanged}" />
            </StackPanel>

            <!-- Parameters Header -->
            <StackPanel Margin="0,16,0,0">
                <WrapPanel>
                    <TextBlock Text="{loc:Translate 'Parameters'}"
                               Foreground="{DynamicResource TextBrush}"
                               FontWeight="SemiBold"
                               VerticalAlignment="Center" />
                    <Button Margin="12,0,0,0"
                            Style="{StaticResource PrimaryButtonStyle}"
                            Content="{loc:Translate 'Add Parameter'}"
                            Command="{Binding DataContext.AddWardrobeSnapshotParamCommand, RelativeSource={RelativeSource AncestorType=Window}}"
                            Padding="10,6"
                            FontSize="11" />
                    <Button Margin="6,0,0,0"
                            Style="{StaticResource SecondaryButtonStyle}"
                            Content="{loc:Translate 'Remove'}"
                            Command="{Binding DataContext.RemoveWardrobeSnapshotParamCommand, RelativeSource={RelativeSource AncestorType=Window}}"
                            Padding="10,6"
                            FontSize="11" />
                </WrapPanel>

                <!-- Parameter List -->
                <ListBox Margin="0,10,0,0"
                         MaxHeight="150"
                         ItemsSource="{Binding SnapshotParams}"
                         SelectedItem="{Binding DataContext.SelectedWardrobeSnapshotParam, RelativeSource={RelativeSource AncestorType=Window}, Mode=TwoWay}"
                         ScrollViewer.CanContentScroll="True">
                    <ListBox.ItemTemplate>
                        <DataTemplate DataType="{x:Type models:WardrobeSnapshotParam}">
                            <Grid>
                                <Grid.ColumnDefinitions>
                                    <ColumnDefinition Width="*" />
                                    <ColumnDefinition Width="80" />
                                    <ColumnDefinition Width="80" />
                                </Grid.ColumnDefinitions>
                                <TextBlock Grid.Column="0"
                                           Text="{Binding ParameterName}"
                                           TextTrimming="CharacterEllipsis"
                                           Foreground="{DynamicResource TextBrush}" />
                                <TextBlock Grid.Column="1"
                                           Text="{Binding ParameterType}"
                                           Foreground="{DynamicResource MutedBrush}"
                                           TextAlignment="Center" />
                                <TextBlock Grid.Column="2"
                                           Text="{Binding SetValue}"
                                           Foreground="{DynamicResource MutedBrush}"
                                           TextAlignment="Right" />
                            </Grid>
                        </DataTemplate>
                    </ListBox.ItemTemplate>
                </ListBox>
            </StackPanel>
        </StackPanel>
    </Border>

    <!-- Global Wardrobe Cooldown -->
    <StackPanel Margin="0,14,0,0" Orientation="Horizontal">
        <TextBlock Text="{loc:Translate 'Global Wardrobe Cooldown:'}"
                   Foreground="{DynamicResource TextBrush}"
                   FontWeight="SemiBold"
                   VerticalAlignment="Center" />
        <TextBox Width="60"
                 Margin="8,0,0,0"
                 Text="{Binding SelectedAvatarProfile.WardrobeCooldownSeconds, UpdateSourceTrigger=PropertyChanged}" />
        <TextBlock Margin="4,0,0,0"
                   Text="{loc:Translate 'seconds'}"
                   Foreground="{DynamicResource MutedBrush}"
                   VerticalAlignment="Center" />
    </StackPanel>

    <!-- Master Wardrobe Reward Toggle -->
    <CheckBox Margin="0,12,0,0"
              IsChecked="{Binding SelectedAvatarProfile.UseWardrobeMasterReward, UpdateSourceTrigger=PropertyChanged}">
        <TextBlock Text="{loc:Translate 'Enable Master Wardrobe Reward'}"
                   TextWrapping="Wrap" />
    </CheckBox>
    <TextBlock Margin="26,6,0,0"
               Foreground="{DynamicResource MutedBrush}"
               TextWrapping="Wrap"
               Text="{loc:Translate 'Viewers type the outfit name in one master reward to select which outfit fires.'}" />

    <!-- Master Reward Settings (when enabled) -->
    <Border Margin="0,10,0,0"
            Background="{DynamicResource NestedPanelBrush}"
            BorderBrush="{DynamicResource HighlightBorderBrush}"
            BorderThickness="1"
            CornerRadius="16"
            Padding="14">
        <Border.Style>
            <Style TargetType="Border">
                <Setter Property="Visibility" Value="Collapsed" />
                <Style.Triggers>
                    <DataTrigger Binding="{Binding SelectedAvatarProfile.UseWardrobeMasterReward}" Value="True">
                        <Setter Property="Visibility" Value="Visible" />
                    </DataTrigger>
                </Style.Triggers>
            </Style>
        </Border.Style>
        <StackPanel DataContext="{Binding SelectedAvatarProfile}">
            <TextBlock Text="{loc:Translate 'Master Wardrobe Reward'}"
                       Foreground="{DynamicResource TextBrush}"
                       FontWeight="SemiBold" />

            <StackPanel Margin="0,12,0,0">
                <TextBlock Text="{loc:Translate 'Reward Source'}"
                           Foreground="{DynamicResource TextBrush}"
                           FontWeight="SemiBold" />
                <ComboBox ItemsSource="{Binding DataContext.RewardSyncModeOptions, RelativeSource={RelativeSource AncestorType=Window}}"
                          DisplayMemberPath="Label"
                          SelectedValuePath="Value"
                          SelectedValue="{Binding WardrobeMasterRewardSyncMode, UpdateSourceTrigger=PropertyChanged}" />
            </StackPanel>

            <UniformGrid Columns="2" Margin="0,12,0,0">
                <StackPanel>
                    <TextBlock Text="{loc:Translate 'Reward Title'}"
                               Foreground="{DynamicResource TextBrush}"
                               FontWeight="SemiBold" />
                    <TextBox Margin="0,6,0,0"
                             Text="{Binding WardrobeMasterRewardTitle, UpdateSourceTrigger=PropertyChanged}" />
                </StackPanel>
                <StackPanel Margin="12,0,0,0">
                    <TextBlock Text="{loc:Translate 'Cost'}"
                               Foreground="{DynamicResource TextBrush}"
                               FontWeight="SemiBold" />
                    <TextBox Margin="0,6,0,0"
                             Text="{Binding WardrobeMasterRewardCost, UpdateSourceTrigger=PropertyChanged}" />
                </StackPanel>
            </UniformGrid>

            <UniformGrid Columns="2" Margin="0,12,0,0">
                <StackPanel>
                    <TextBlock Text="{loc:Translate 'Cooldown (seconds)'}"
                               Foreground="{DynamicResource TextBrush}"
                               FontWeight="SemiBold" />
                    <TextBox Margin="0,6,0,0"
                             Text="{Binding WardrobeMasterRewardCooldownSeconds, UpdateSourceTrigger=PropertyChanged}" />
                </StackPanel>
            </UniformGrid>
        </StackPanel>
    </Border>
</StackPanel>
```

- [ ] **Step 4: Check for NullToVisibilityConverter**

Search MainWindow.xaml for `NullToVisibilityConverter`. If it doesn't exist, add it to `<Window.Resources>`:

```xml
<local:NullToVisibilityConverter x:Key="NullToVisibilityConverter" />
```

If `NullToVisibilityConverter` doesn't exist as a class, check for `InverseBooleanConverter` in the codebase and follow the same pattern to create it in `VrcTwitchOscBridge/Infrastructure/Converters.cs` or wherever converters live.

- [ ] **Step 5: Build and verify**

Run: `dotnet build "VrcTwitchOscBridge/VrcTwitchOscBridge.csproj" --no-restore`
Expected: Build succeeded, 0 warnings, 0 errors

- [ ] **Step 6: Commit**

```bash
git add VrcTwitchOscBridge/MainWindow.xaml
git commit -m "feat: add Wardrobe editor UI to MainWindow.xaml"
```

---

### Task 9: Add Localization Keys

**Files:**
- Modify: `LocalizationAudit/en-US.json` (or wherever the en-US source file lives)
- Run: Localization audit script

- [ ] **Step 1: Add new keys**

Add these keys to the en-US localization file:

```json
"Use Wardrobe Mode": "Use Wardrobe Mode",
"Named outfits with mixed Bool/Int/Float snapshots and auto-capture restore. Existing outfit choices are preserved but hidden while Wardrobe mode is on.": "Named outfits with mixed Bool/Int/Float snapshots and auto-capture restore. Existing outfit choices are preserved but hidden while Wardrobe mode is on.",
"Add Outfit": "Add Outfit",
"Delete Outfit": "Delete Outfit",
"No outfits yet. Add Outfit to create your first Wardrobe outfit.": "No outfits yet. Add Outfit to create your first Wardrobe outfit.",
"Outfit Name": "Outfit Name",
"Active Time:": "Active Time:",
"seconds": "seconds",
"Twitch Reward": "Twitch Reward",
"Chat Command": "Chat Command",
"Parameters": "Parameters",
"Add Parameter": "Add Parameter",
"Remove": "Remove",
"Pick parameter": "Pick parameter",
"Set value": "Set value",
"Global Wardrobe Cooldown:": "Global Wardrobe Cooldown:",
"Enable Master Wardrobe Reward": "Enable Master Wardrobe Reward",
"Viewers type the outfit name in one master reward to select which outfit fires.": "Viewers type the outfit name in one master reward to select which outfit fires.",
"Master Wardrobe Reward": "Master Wardrobe Reward",
"Wardrobe outfit '{0}' blocked: Wardrobe is on cooldown.": "Wardrobe outfit '{0}' blocked: Wardrobe is on cooldown.",
"Wardrobe outfit '{0}' blocked: Avatar parameter cache not available for '{1}'.": "Wardrobe outfit '{0}' blocked: Avatar parameter cache not available for '{1}'.",
"Wardrobe outfit '{0}' blocked: Parameter '{1}' not found on current avatar.": "Wardrobe outfit '{0}' blocked: Parameter '{1}' not found on current avatar.",
"Applied Wardrobe outfit '{0}' ({1} params).": "Applied Wardrobe outfit '{0}' ({1} params).",
"Restored Wardrobe outfit '{0}' captured values ({1} params).": "Restored Wardrobe outfit '{0}' captured values ({1} params).",
"Wardrobe cooldown started: {0}s.": "Wardrobe cooldown started: {0}s.",
"Wardrobe restore failed for '{0}': {1}": "Wardrobe restore failed for '{0}': {1}",
"Wardrobe outfit blocked: VRChat user ID not configured.": "Wardrobe outfit blocked: VRChat user ID not configured.",
"Wardrobe outfit '{0}' applied successfully.": "Wardrobe outfit '{0}' applied successfully."
```

- [ ] **Step 2: Run localization audit**

Run: `powershell -ExecutionPolicy Bypass -File "E:\!!!Program to work on\Proper Crystal Relay\LocalizationAudit\Run-LocalizationAudit.ps1"`
Expected: Audit passes with no missing keys

- [ ] **Step 3: Commit**

```bash
git add LocalizationAudit/
git commit -m "feat: add Wardrobe localization keys"
```

---

### Task 10: Hook Wardrobe into Twitch Trigger Pipeline

**Files:**
- Modify: `VrcTwitchOscBridge/Services/BridgeCoordinator.cs`

- [ ] **Step 1: Add Wardrobe trigger resolution method**

Add this private method in `BridgeCoordinator` near the other trigger resolution methods:

```csharp
    /// <summary>
    /// Resolves and executes a Wardrobe outfit from a Twitch redemption.
    /// Supports individual outfit rewards and master reward with typed outfit name.
    /// </summary>
    private async Task TryExecuteWardrobeFromRedemptionAsync(
        string rewardId,
        string? redemptionInputText,
        CancellationToken cancellationToken)
    {
        if (activeConfiguration is null) return;

        foreach (var profile in activeConfiguration.AvatarProfiles)
        {
            if (!profile.UseWardrobeMode || !profile.IsEnabled) continue;
            if (string.IsNullOrWhiteSpace(profile.AvatarId)) continue;

            // Check individual outfit rewards
            foreach (var outfit in profile.WardrobeOutfits)
            {
                if (!outfit.IsEnabled) continue;
                if (!string.Equals(outfit.TwitchRewardId, rewardId, StringComparison.Ordinal)) continue;

                if (!BridgeRuntimeConfiguration.TryToWardrobeSnapshot(outfit, profile, out var snapshot)) continue;

                var applied = await ExecuteWardrobeOutfitAsync(snapshot, cancellationToken);
                if (applied)
                {
                    WriteLog($"Wardrobe outfit '{outfit.Name}' fired from individual reward.");
                }
                return;
            }

            // Check master reward
            if (profile.UseWardrobeMasterReward
                && !string.IsNullOrWhiteSpace(profile.WardrobeMasterRewardId)
                && string.Equals(profile.WardrobeMasterRewardId, rewardId, StringComparison.Ordinal))
            {
                if (string.IsNullOrWhiteSpace(redemptionInputText))
                {
                    WriteLog("Wardrobe master reward redeemed but no outfit name was typed.");
                    return;
                }

                var inputName = redemptionInputText.Trim();
                var matchedOutfit = profile.WardrobeOutfits
                    .FirstOrDefault(o => o.IsEnabled
                        && string.Equals(o.Name, inputName, StringComparison.OrdinalIgnoreCase));

                if (matchedOutfit is null)
                {
                    WriteLog($"Wardrobe master reward: No outfit found matching '{inputName}'.");
                    return;
                }

                if (!BridgeRuntimeConfiguration.TryToWardrobeSnapshot(matchedOutfit, profile, out var masterSnapshot)) continue;

                var masterApplied = await ExecuteWardrobeOutfitAsync(masterSnapshot, cancellationToken);
                if (masterApplied)
                {
                    WriteLog($"Wardrobe outfit '{matchedOutfit.Name}' fired from master reward.");
                }
                return;
            }
        }
    }
```

- [ ] **Step 2: Hook into existing redemption handler**

Find the method that handles Twitch channel point redemptions (search for `OnChannelPointRedemption` or `HandleRedemption`). Add a call to `TryExecuteWardrobeFromRedemptionAsync` at the start of the redemption handling flow, before the existing `TriggerRule` resolution:

```csharp
    // Try Wardrobe first (individual or master reward)
    await TryExecuteWardrobeFromRedemptionAsync(rewardId, redemptionInputText, cancellationToken);
```

- [ ] **Step 3: Build and verify**

Run: `dotnet build "VrcTwitchOscBridge/VrcTwitchOscBridge.csproj" --no-restore`
Expected: Build succeeded, 0 warnings, 0 errors

- [ ] **Step 4: Commit**

```bash
git add VrcTwitchOscBridge/Services/BridgeCoordinator.cs
git commit -m "feat: hook Wardrobe execution into Twitch redemption pipeline"
```

---

### Task 11: Build, Test, and Verify

**Files:**
- All modified files

- [ ] **Step 1: Full build**

Run: `dotnet build "VrcTwitchOscBridge/VrcTwitchOscBridge.csproj" --no-restore`
Expected: Build succeeded, 0 warnings, 0 errors

- [ ] **Step 2: Build test package**

Run: `powershell -ExecutionPolicy Bypass -Command "[Environment]::SetEnvironmentVariable('CR_SKIP_GIT_CHECK', '1', 'Process'); & 'E:\!!!Program to work on\Proper Crystal Relay\Build-Crystal-Relay-Test.ps1' -Version 3.1.9"`
Expected: Test package built successfully at `TestBuilds\v3.1.9\CrystalRelayTwitchOsc-v3.1.9-test\`

- [ ] **Step 3: Commit all changes**

```bash
git status
git add -A
git commit -m "feat: complete Wardrobe system implementation"
```
