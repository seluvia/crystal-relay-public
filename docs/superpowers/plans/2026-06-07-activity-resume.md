# Activity Resume / Preserve System Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Implement a system that preserves active bridge activities (avatar scaling, movement locks, avatar changes) across app restarts and resumes them once OSC is connected and the same VRChat avatar is detected.

**Architecture:** A new `ActivityResumeService` owns a single JSON file in `AppDataPaths.SecureFolder`. `BridgeCoordinator` writes activity snapshots when effects start and removes them when they end. On startup, `BridgeCoordinator` checks for pending snapshots and safely rebuilds state by re-invoking its existing execution methods with an `isResuming` flag that skips user-facing side effects.

**Tech Stack:** C# 10, .NET 10, WPF, System.Text.Json, Crystal Relay's existing `BridgeCoordinator` / `OscRouterService` / `AppDataPaths` patterns.

---

## File Map

| File | Role |
|------|------|
| `VrcTwitchOscBridge/Models/ResumeActivityType.cs` | Enum for activity types (Scale, Movement, AvatarChange) |
| `VrcTwitchOscBridge/Models/ResumeActivity.cs` | Single activity snapshot model |
| `VrcTwitchOscBridge/Models/ActivityResumeSnapshot.cs` | Root snapshot container with version and avatar ID |
| `VrcTwitchOscBridge/Services/IActivityResumeService.cs` | Service interface |
| `VrcTwitchOscBridge/Services/ActivityResumeService.cs` | JSON persistence and query logic |
| `VrcTwitchOscBridge/Services/OscRouterService.cs` | Add `DiscoveryStateChanged` event |
| `VrcTwitchOscBridge/Services/BridgeCoordinator.cs` | Add `isResuming` flags, write hooks, resume logic |
| `VrcTwitchOscBridge/ViewModels/MainWindowViewModel.cs` | Wire up service creation and resume trigger |
| `VrcTwitchOscBridge/VrcTwitchOscBridge.csproj` | Add new `.cs` files to `<Compile>` group |

---

### Task 1: Create `ResumeActivityType.cs`

**Files:**
- Create: `VrcTwitchOscBridge/Models/ResumeActivityType.cs`

- [ ] **Step 1: Write the enum**

```csharp
namespace VrcTwitchOscBridge.Models;

public enum ResumeActivityType
{
    AvatarScale,
    Movement,
    AvatarChange
}
```

- [ ] **Step 2: Commit**

```bash
git add "VrcTwitchOscBridge/Models/ResumeActivityType.cs"
git commit -m "feat: add ResumeActivityType enum"
```

---

### Task 2: Create `ResumeActivity.cs`

**Files:**
- Create: `VrcTwitchOscBridge/Models/ResumeActivity.cs`

- [ ] **Step 1: Write the model**

```csharp
using System.Text.Json.Serialization;

namespace VrcTwitchOscBridge.Models;

public sealed class ResumeActivity
{
    [JsonPropertyName("type")]
    public ResumeActivityType Type { get; set; }

    [JsonPropertyName("ruleId")]
    public Guid RuleId { get; set; }

    [JsonPropertyName("expiresAt")]
    public DateTimeOffset? ExpiresAt { get; set; }

    [JsonPropertyName("currentValue")]
    public double? CurrentValue { get; set; }

    [JsonPropertyName("payload")]
    public Dictionary<string, object> Payload { get; set; } = new();
}
```

- [ ] **Step 2: Commit**

```bash
git add "VrcTwitchOscBridge/Models/ResumeActivity.cs"
git commit -m "feat: add ResumeActivity model"
```

---

### Task 3: Create `ActivityResumeSnapshot.cs`

**Files:**
- Create: `VrcTwitchOscBridge/Models/ActivityResumeSnapshot.cs`

- [ ] **Step 1: Write the model**

```csharp
using System.Text.Json.Serialization;

namespace VrcTwitchOscBridge.Models;

public sealed class ActivityResumeSnapshot
{
    [JsonPropertyName("version")]
    public int Version { get; set; } = 1;

    [JsonPropertyName("savedAt")]
    public DateTimeOffset SavedAt { get; set; }

    [JsonPropertyName("currentAvatarId")]
    public string CurrentAvatarId { get; set; } = string.Empty;

    [JsonPropertyName("activities")]
    public List<ResumeActivity> Activities { get; set; } = new();
}
```

- [ ] **Step 2: Commit**

```bash
git add "VrcTwitchOscBridge/Models/ActivityResumeSnapshot.cs"
git commit -m "feat: add ActivityResumeSnapshot model"
```

---

### Task 4: Create `IActivityResumeService.cs`

**Files:**
- Create: `VrcTwitchOscBridge/Services/IActivityResumeService.cs`

- [ ] **Step 1: Write the interface**

```csharp
using VrcTwitchOscBridge.Models;

namespace VrcTwitchOscBridge.Services;

public interface IActivityResumeService
{
    Task LoadPendingAsync();
    bool HasPendingResume { get; }
    bool IsPendingForAvatar(string avatarId);
    IReadOnlyList<ResumeActivity> GetPendingActivities();
    Task RecordActivityStartedAsync(ResumeActivity activity);
    Task RecordActivityEndedAsync(Guid ruleId);
    Task ClearAllAsync();
    Task CommitAsync();
}
```

- [ ] **Step 2: Commit**

```bash
git add "VrcTwitchOscBridge/Services/IActivityResumeService.cs"
git commit -m "feat: add IActivityResumeService interface"
```

---

### Task 5: Create `ActivityResumeService.cs`

**Files:**
- Create: `VrcTwitchOscBridge/Services/ActivityResumeService.cs`

- [ ] **Step 1: Write the implementation**

```csharp
using System.Text.Json;
using System.Text.Json.Serialization;
using VrcTwitchOscBridge.Models;

namespace VrcTwitchOscBridge.Services;

public sealed class ActivityResumeService : IActivityResumeService
{
    private static readonly JsonSerializerOptions SerializerOptions = new()
    {
        WriteIndented = true
    };

    private static string SnapshotPath => Path.Combine(AppDataPaths.SecureFolder, "activity-resume.json");

    private readonly object stateGate = new();
    private ActivityResumeSnapshot? pendingSnapshot;

    public async Task LoadPendingAsync()
    {
        lock (stateGate)
        {
            pendingSnapshot = null;
        }

        if (!File.Exists(SnapshotPath))
        {
            return;
        }

        try
        {
            var json = await File.ReadAllTextAsync(SnapshotPath);
            var snapshot = JsonSerializer.Deserialize<ActivityResumeSnapshot>(json, SerializerOptions);
            if (snapshot is not null && snapshot.Version == 1)
            {
                lock (stateGate)
                {
                    pendingSnapshot = snapshot;
                }
            }
            else
            {
                File.Delete(SnapshotPath);
            }
        }
        catch (Exception)
        {
            try
            {
                File.Delete(SnapshotPath);
            }
            catch
            {
            }
        }
    }

    public bool HasPendingResume
    {
        get
        {
            lock (stateGate)
            {
                return pendingSnapshot?.Activities.Count > 0;
            }
        }
    }

    public bool IsPendingForAvatar(string avatarId)
    {
        var normalized = avatarId?.Trim() ?? string.Empty;
        lock (stateGate)
        {
            if (pendingSnapshot is null)
            {
                return false;
            }

            return string.Equals(pendingSnapshot.CurrentAvatarId, normalized, StringComparison.Ordinal);
        }
    }

    public IReadOnlyList<ResumeActivity> GetPendingActivities()
    {
        lock (stateGate)
        {
            return pendingSnapshot?.Activities.ToList() ?? (IReadOnlyList<ResumeActivity>)Array.Empty<ResumeActivity>();
        }
    }

    public Task RecordActivityStartedAsync(ResumeActivity activity)
    {
        lock (stateGate)
        {
            pendingSnapshot ??= new ActivityResumeSnapshot();
            pendingSnapshot.Activities.RemoveAll(a => a.RuleId == activity.RuleId);
            pendingSnapshot.Activities.Add(activity);
        }

        return CommitAsync();
    }

    public Task RecordActivityEndedAsync(Guid ruleId)
    {
        lock (stateGate)
        {
            if (pendingSnapshot is null)
            {
                return Task.CompletedTask;
            }

            pendingSnapshot.Activities.RemoveAll(a => a.RuleId == ruleId);
        }

        return CommitAsync();
    }

    public Task ClearAllAsync()
    {
        lock (stateGate)
        {
            pendingSnapshot = null;
        }

        try
        {
            if (File.Exists(SnapshotPath))
            {
                File.Delete(SnapshotPath);
            }
        }
        catch
        {
        }

        return Task.CompletedTask;
    }

    public async Task CommitAsync()
    {
        ActivityResumeSnapshot? snapshot;
        lock (stateGate)
        {
            snapshot = pendingSnapshot is null ? null : new ActivityResumeSnapshot
            {
                Version = pendingSnapshot.Version,
                SavedAt = DateTimeOffset.UtcNow,
                CurrentAvatarId = pendingSnapshot.CurrentAvatarId,
                Activities = pendingSnapshot.Activities.ToList()
            };
        }

        try
        {
            if (snapshot is null || snapshot.Activities.Count == 0)
            {
                if (File.Exists(SnapshotPath))
                {
                    File.Delete(SnapshotPath);
                }
                return;
            }

            var tempPath = SnapshotPath + ".tmp";
            var json = JsonSerializer.Serialize(snapshot, SerializerOptions);
            await File.WriteAllTextAsync(tempPath, json);
            File.Move(tempPath, SnapshotPath, overwrite: true);
        }
        catch
        {
        }
    }
}
```

- [ ] **Step 2: Commit**

```bash
git add "VrcTwitchOscBridge/Services/ActivityResumeService.cs"
git commit -m "feat: add ActivityResumeService implementation"
```

---

### Task 6: Add `DiscoveryStateChanged` event to `OscRouterService`

**Files:**
- Modify: `VrcTwitchOscBridge/Services/OscRouterService.cs`

- [ ] **Step 1: Add the event declaration**

Find the existing event declarations (around line 43):

```csharp
public event Action<string>? LogWritten;
public event Action<OscObservedValue>? ObservedValueReceived;
```

Add after `ObservedValueReceived`:

```csharp
public event Action<OscDiscoveryState>? DiscoveryStateChanged;
```

- [ ] **Step 2: Fire the event when VRChat is discovered**

Find the block where `discoveryState = OscDiscoveryState.Discovered` is set (around line 496):

```csharp
lock (stateGate)
{
    if (activeVrChatTarget is not null && !ShouldReplaceTarget(activeVrChatTarget, match))
    {
        return;
    }

    activeVrChatTarget = match;
    discoveryState = OscDiscoveryState.Discovered;
}

LogWritten?.Invoke($"Discovered VRChat through OSCQuery: {match.Name}...");
```

Add after the lock block (before the `LogWritten` line):

```csharp
DiscoveryStateChanged?.Invoke(OscDiscoveryState.Discovered);
```

- [ ] **Step 3: Fire the event when target is lost**

Find `MarkTargetLost` (around line 700):

```csharp
private void MarkTargetLost(string reason)
{
    var shouldLog = false;

    lock (stateGate)
    {
        if (activeVrChatTarget is null && discoveryState == OscDiscoveryState.Lost)
        {
            return;
        }

        activeVrChatTarget = null;
        discoveryState = OscDiscoveryState.Lost;
        nextDiscoveryLogAt = DateTimeOffset.MinValue;
        shouldLog = true;
    }

    if (shouldLog)
    {
        LogWritten?.Invoke(reason);
    }
}
```

Change to:

```csharp
private void MarkTargetLost(string reason)
{
    var shouldLog = false;
    var shouldNotify = false;

    lock (stateGate)
    {
        if (activeVrChatTarget is null && discoveryState == OscDiscoveryState.Lost)
        {
            return;
        }

        activeVrChatTarget = null;
        discoveryState = OscDiscoveryState.Lost;
        nextDiscoveryLogAt = DateTimeOffset.MinValue;
        shouldLog = true;
        shouldNotify = true;
    }

    if (shouldNotify)
    {
        DiscoveryStateChanged?.Invoke(OscDiscoveryState.Lost);
    }

    if (shouldLog)
    {
        LogWritten?.Invoke(reason);
    }
}
```

- [ ] **Step 4: Commit**

```bash
git add "VrcTwitchOscBridge/Services/OscRouterService.cs"
git commit -m "feat: add DiscoveryStateChanged event to OscRouterService"
```

---

### Task 7: Add `isResuming` flag to `ExecuteAvatarScaleRuleAsync`

**Files:**
- Modify: `VrcTwitchOscBridge/Services/BridgeCoordinator.cs`

- [ ] **Step 1: Update the method signature**

Find the method (around line 4472):

```csharp
private async Task<bool> ExecuteAvatarScaleRuleAsync(
    AvatarScaleRuleSnapshot rule,
    UniversalIncomingEvent incomingEvent,
    bool isTest,
    CancellationToken cancellationToken)
```

Add `bool isResuming = false`:

```csharp
private async Task<bool> ExecuteAvatarScaleRuleAsync(
    AvatarScaleRuleSnapshot rule,
    UniversalIncomingEvent incomingEvent,
    bool isTest,
    CancellationToken cancellationToken,
    bool isResuming = false)
```

- [ ] **Step 2: Skip cooldown and follow deduplication when resuming**

Find the cooldown check (around line 4502):

```csharp
var cooldownSeconds = GetAvatarScaleEffectiveCooldownSeconds(rule);
if (!isTest)
{
    var now = DateTimeOffset.UtcNow;
    lock (stateGate)
    {
        if (TryGetTemporarilyDisabledUntilLock(rule.Id, now, out var temporarilyDisabledUntil))
        {
            WriteLog($"Avatar scale '{rule.Name}' skipped because it is temporarily disabled for {DescribeDuration((temporarilyDisabledUntil - now).TotalSeconds)}.");
            return true;
        }

        if (cooldownSeconds <= 0)
        {
            cooldowns.Remove(rule.Id);
        }
        else if (cooldowns.TryGetValue(rule.Id, out var cooldownUntil) && cooldownUntil > now)
        {
            WriteLog($"Avatar scale '{rule.Name}' skipped because it is still on cooldown for {DescribeDuration((cooldownUntil - now).TotalSeconds)}.");
            return true;
        }
    }
}
```

Change to:

```csharp
var cooldownSeconds = isResuming ? 0 : GetAvatarScaleEffectiveCooldownSeconds(rule);
if (!isTest && !isResuming)
{
    var now = DateTimeOffset.UtcNow;
    lock (stateGate)
    {
        if (TryGetTemporarilyDisabledUntilLock(rule.Id, now, out var temporarilyDisabledUntil))
        {
            WriteLog($"Avatar scale '{rule.Name}' skipped because it is temporarily disabled for {DescribeDuration((temporarilyDisabledUntil - now).TotalSeconds)}.");
            return true;
        }

        if (cooldownSeconds <= 0)
        {
            cooldowns.Remove(rule.Id);
        }
        else if (cooldowns.TryGetValue(rule.Id, out var cooldownUntil) && cooldownUntil > now)
        {
            WriteLog($"Avatar scale '{rule.Name}' skipped because it is still on cooldown for {DescribeDuration((cooldownUntil - now).TotalSeconds)}.");
            return true;
        }
    }
}
```

Find the follow deduplication block (around line 4484):

```csharp
if (rule.TriggerType == AvatarScaleTriggerType.Follow && !isTest)
{
    lock (stateGate)
    {
        if (!avatarScaleFollowTriggeredUsers.TryGetValue(rule.Id, out var triggeredUsers))
        {
            triggeredUsers = new HashSet<string>(StringComparer.Ordinal);
            avatarScaleFollowTriggeredUsers[rule.Id] = triggeredUsers;
        }

        if (!string.IsNullOrWhiteSpace(incomingEvent.UserId) && triggeredUsers.Contains(incomingEvent.UserId))
        {
            WriteLog($"Avatar scale '{rule.Name}' skipped because {incomingEvent.UserDisplayName} has already triggered this follow rule.");
            return true;
        }
    }
}
```

Change to:

```csharp
if (rule.TriggerType == AvatarScaleTriggerType.Follow && !isTest && !isResuming)
{
    lock (stateGate)
    {
        if (!avatarScaleFollowTriggeredUsers.TryGetValue(rule.Id, out var triggeredUsers))
        {
            triggeredUsers = new HashSet<string>(StringComparer.Ordinal);
            avatarScaleFollowTriggeredUsers[rule.Id] = triggeredUsers;
        }

        if (!string.IsNullOrWhiteSpace(incomingEvent.UserId) && triggeredUsers.Contains(incomingEvent.UserId))
        {
            WriteLog($"Avatar scale '{rule.Name}' skipped because {incomingEvent.UserDisplayName} has already triggered this follow rule.");
            return true;
        }
    }
}
```

- [ ] **Step 3: Skip lockout and reward state side effects in `StartRewardStateAfterFirstScaleSend`**

Inside the local function (around line 4568), wrap the side-effect-only parts:

```csharp
if (!isResuming)
{
    UpdateActiveAvatarScaleRuleLockoutState(rule);
}
```

And wrap the cooldown/effect notification block at the end of the local function:

```csharp
if (!isResuming)
{
    if (cooldownSeconds > 0)
    {
        ScheduleCooldownStateNotification(rule.Id, TimeSpan.FromSeconds(cooldownSeconds));
        ManagedRewardAvailabilityChanged?.Invoke();
    }
    else
    {
        CancelCooldownStateNotification(rule.Id);
    }

    if (effectDurationSeconds > 0)
    {
        ScheduleAvatarScaleEffectStateNotification(rule.Id, TimeSpan.FromSeconds(effectDurationSeconds));
        ManagedRewardAvailabilityChanged?.Invoke();
    }
}
```

And wrap the cooldown/follow dedup in the lock block:

```csharp
if (!isResuming)
{
    if (cooldownSeconds > 0)
    {
        cooldowns[rule.Id] = DateTimeOffset.UtcNow.AddSeconds(cooldownSeconds);
    }
    else
    {
        cooldowns.Remove(rule.Id);
    }

    if (rule.TriggerType == AvatarScaleTriggerType.Follow && !string.IsNullOrWhiteSpace(incomingEvent.UserId))
    {
        if (!avatarScaleFollowTriggeredUsers.TryGetValue(rule.Id, out var triggeredUsers))
        {
            triggeredUsers = new HashSet<string>(StringComparer.Ordinal);
            avatarScaleFollowTriggeredUsers[rule.Id] = triggeredUsers;
        }
        triggeredUsers.Add(incomingEvent.UserId);
    }
}
```

- [ ] **Step 4: Update all call sites to pass `isResuming: false`**

Find these call sites and add `isResuming: false`:

1. Line ~688: `await ExecuteAvatarScaleRuleAsync(rule, UniversalIncomingEvent.Test, isTest: true, cancellationToken);`
2. Line ~3437: `await ExecuteAvatarScaleRuleAsync(rule.ScaleAction, incomingEvent, isTest: false, cancellationToken);`
3. Line ~4193: `await ExecuteAvatarScaleRuleAsync(rule, incomingEvent, isTest: false, executionToken);`
4. Line ~4370: `var completed = await ExecuteAvatarScaleRuleAsync(...`

- [ ] **Step 5: Commit**

```bash
git add "VrcTwitchOscBridge/Services/BridgeCoordinator.cs"
git commit -m "feat: add isResuming flag to ExecuteAvatarScaleRuleAsync"
```

---

### Task 8: Add `isResuming` flag to `ExecuteMovementSoftLockAsync`

**Files:**
- Modify: `VrcTwitchOscBridge/Services/BridgeCoordinator.cs`

- [ ] **Step 1: Update the method signature**

Find the method (around line 9103):

```csharp
private async Task ExecuteMovementSoftLockAsync(TriggerRuleSnapshot rule, CancellationToken cancellationToken)
```

Add `bool isResuming = false`:

```csharp
private async Task ExecuteMovementSoftLockAsync(TriggerRuleSnapshot rule, CancellationToken cancellationToken, bool isResuming = false)
```

- [ ] **Step 2: Update callers in `ExecuteRuleActionAsync`**

Find the two call sites (around lines 7114 and 7119):

```csharp
await ExecuteMovementSoftLockAsync(executionRule, cancellationToken);
```

Change both to:

```csharp
await ExecuteMovementSoftLockAsync(executionRule, cancellationToken, isResuming);
```

- [ ] **Step 3: Commit**

```bash
git add "VrcTwitchOscBridge/Services/BridgeCoordinator.cs"
git commit -m "feat: add isResuming flag to ExecuteMovementSoftLockAsync"
```

---

### Task 9: Add `isResuming` flag to `ExecuteGlitchyMovementRuleActionAsync`

**Files:**
- Modify: `VrcTwitchOscBridge/Services/BridgeCoordinator.cs`

- [ ] **Step 1: Update the method signature**

Find the method (around line 7317):

```csharp
private async Task ExecuteGlitchyMovementRuleActionAsync(
    TriggerRuleSnapshot rule,
    BridgeIncomingEvent? bridgeEvent,
    CancellationToken cancellationToken,
    bool isTest,
    bool queuedReplay,
    int cooldownSeconds)
```

Add `bool isResuming = false`:

```csharp
private async Task ExecuteGlitchyMovementRuleActionAsync(
    TriggerRuleSnapshot rule,
    BridgeIncomingEvent? bridgeEvent,
    CancellationToken cancellationToken,
    bool isTest,
    bool queuedReplay,
    int cooldownSeconds,
    bool isResuming = false)
```

- [ ] **Step 2: Skip cooldown and lockout state updates when resuming**

Find the cooldown block inside the method (around line 7344):

```csharp
if (!isTest)
{
    if (cooldownSeconds > 0)
    {
        cooldowns[rule.Id] = DateTimeOffset.UtcNow.AddSeconds(cooldownSeconds);
    }
    else
    {
        cooldowns.Remove(rule.Id);
    }
}
```

Change to:

```csharp
if (!isTest && !isResuming)
{
    if (cooldownSeconds > 0)
    {
        cooldowns[rule.Id] = DateTimeOffset.UtcNow.AddSeconds(cooldownSeconds);
    }
    else
    {
        cooldowns.Remove(rule.Id);
    }
}
```

- [ ] **Step 3: Update caller in `ExecuteRuleActionAsync`**

Find the call site (around line 7160):

```csharp
await ExecuteGlitchyMovementRuleActionAsync(
    executionRule,
    bridgeEvent,
    cancellationToken,
    isTest,
    queuedReplay,
    cooldownSeconds);
```

Change to:

```csharp
await ExecuteGlitchyMovementRuleActionAsync(
    executionRule,
    bridgeEvent,
    cancellationToken,
    isTest,
    queuedReplay,
    cooldownSeconds,
    isResuming);
```

- [ ] **Step 4: Commit**

```bash
git add "VrcTwitchOscBridge/Services/BridgeCoordinator.cs"
git commit -m "feat: add isResuming flag to ExecuteGlitchyMovementRuleActionAsync"
```

---

### Task 10: Add `isResuming` flag to `ExecuteRuleActionAsync`

**Files:**
- Modify: `VrcTwitchOscBridge/Services/BridgeCoordinator.cs`

- [ ] **Step 1: Update the method signature**

Find the method (around line 7040):

```csharp
private async Task ExecuteRuleActionAsync(
    TriggerRuleSnapshot rule,
    BridgeIncomingEvent? bridgeEvent,
    CancellationToken cancellationToken,
    bool isTest,
    bool queuedReplay,
    bool allowLaneQueue)
```

Add `bool isResuming = false`:

```csharp
private async Task ExecuteRuleActionAsync(
    TriggerRuleSnapshot rule,
    BridgeIncomingEvent? bridgeEvent,
    CancellationToken cancellationToken,
    bool isTest,
    bool queuedReplay,
    bool allowLaneQueue,
    bool isResuming = false)
```

- [ ] **Step 2: Skip side effects when resuming**

**Skip rule lockout update:**
Find the block (around line 7212):

```csharp
if (!isTest)
{
    UpdateActiveRuleLockoutState(executionRule);
}
```

Change to:

```csharp
if (!isTest && !isResuming)
{
    UpdateActiveRuleLockoutState(executionRule);
}
```

**Skip cooldown in lock block:**
Find the block (around line 7228):

```csharp
if (!isTest)
{
    if (cooldownSeconds > 0)
    {
        cooldowns[rule.Id] = DateTimeOffset.UtcNow.AddSeconds(cooldownSeconds);
    }
    else
    {
        cooldowns.Remove(rule.Id);
    }
}
```

Change to:

```csharp
if (!isTest && !isResuming)
{
    if (cooldownSeconds > 0)
    {
        cooldowns[rule.Id] = DateTimeOffset.UtcNow.AddSeconds(cooldownSeconds);
    }
    else
    {
        cooldowns.Remove(rule.Id);
    }
}
```

**Skip cooldown notification:**
Find the block (around line 7241):

```csharp
if (!isTest)
{
    if (cooldownSeconds > 0)
    {
        ScheduleCooldownStateNotification(rule.Id, TimeSpan.FromSeconds(cooldownSeconds));
    }
    else
    {
        CancelCooldownStateNotification(rule.Id);
    }
}
```

Change to:

```csharp
if (!isTest && !isResuming)
{
    if (cooldownSeconds > 0)
    {
        ScheduleCooldownStateNotification(rule.Id, TimeSpan.FromSeconds(cooldownSeconds));
    }
    else
    {
        CancelCooldownStateNotification(rule.Id);
    }
}
```

**Skip avatar switch lockout:**
Find the block (around line 7283):

```csharp
var lockoutDurationSeconds = isTest ? 0 : GetLockoutDurationSeconds(executionRule);
if (!isTest)
{
    UpdateActiveAvatarSwitchLockoutState(executionRule);
}
```

Change to:

```csharp
var lockoutDurationSeconds = isTest ? 0 : GetLockoutDurationSeconds(executionRule);
if (!isTest && !isResuming)
{
    UpdateActiveAvatarSwitchLockoutState(executionRule);
}
```

**Skip managed reward notification:**
Find the block (around line 7288):

```csharp
var shouldNotifyManagedRewardState = !isTest && cooldownSeconds > 0;
```

Change to:

```csharp
var shouldNotifyManagedRewardState = !isTest && !isResuming && cooldownSeconds > 0;
```

**Skip bot message and user trigger log:**
Find the block (around line 7269):

```csharp
if (isTest)
{
    WriteLog(queuedReplay
        ? $"Sent queued test trigger for '{rule.Name}'."
        : $"Sent a test trigger for '{rule.Name}'.");
}
else if (bridgeEvent is not null)
{
    WriteLog(queuedReplay
        ? $"{bridgeEvent.UserDisplayName} triggered '{rule.Name}' from the queue."
        : $"{bridgeEvent.UserDisplayName} triggered '{rule.Name}'.");
}
```

Change to:

```csharp
if (isTest)
{
    WriteLog(queuedReplay
        ? $"Sent queued test trigger for '{rule.Name}'."
        : $"Sent a test trigger for '{rule.Name}'.");
}
else if (bridgeEvent is not null && !isResuming)
{
    WriteLog(queuedReplay
        ? $"{bridgeEvent.UserDisplayName} triggered '{rule.Name}' from the queue."
        : $"{bridgeEvent.UserDisplayName} triggered '{rule.Name}'.");
}
```

And the `TrySendBotMessageAsync` call (around line 7311):

```csharp
if (!isTest && bridgeEvent is not null)
{
    await TrySendBotMessageAsync(executionRule, bridgeEvent, action.DisplayValue, cancellationToken);
}
```

Change to:

```csharp
if (!isTest && !isResuming && bridgeEvent is not null)
{
    await TrySendBotMessageAsync(executionRule, bridgeEvent, action.DisplayValue, cancellationToken);
}
```

- [ ] **Step 3: Update all callers to pass `isResuming: false`**

Find these call sites and add `isResuming: false`:

1. Line ~666: `await ExecuteRuleActionAsync(rule, null, cancellationToken, isTest: true, queuedReplay: false, allowLaneQueue: true);`
2. Line ~7035: `await ExecuteRuleActionAsync(rule, bridgeEvent, cancellationToken, isTest: false, queuedReplay: false, allowLaneQueue: true);`
3. Line ~17279: `await ExecuteRuleActionAsync(ruleSnapshot, queuedTrigger.Event, cancellationToken, isTest: false, queuedReplay: true, allowLaneQueue: true);`
4. Line ~17441: `await ExecuteRuleActionAsync(ruleToExecute, queuedAction.Event, cancellationToken, queuedAction.IsTest, queuedReplay: true, allowLaneQueue: true);`
5. Line ~17718: `await ExecuteRuleActionAsync(...`

- [ ] **Step 4: Commit**

```bash
git add "VrcTwitchOscBridge/Services/BridgeCoordinator.cs"
git commit -m "feat: add isResuming flag to ExecuteRuleActionAsync"
```

---

### Task 11: Add write hooks and resume logic to `BridgeCoordinator`

**Files:**
- Modify: `VrcTwitchOscBridge/Services/BridgeCoordinator.cs`

- [ ] **Step 1: Add `IActivityResumeService` field and constructor injection**

Find the constructor (around line 245):

```csharp
public BridgeCoordinator(
    DesktopInputLockService desktopInputLockService,
    WorldCommandBlacklistService worldCommandBlacklistService,
    VrChatLocalOscCacheService vrChatLocalOscCacheService)
{
    this.desktopInputLockService = desktopInputLockService;
    this.worldCommandBlacklistService = worldCommandBlacklistService;
    this.vrChatLocalOscCacheService = vrChatLocalOscCacheService;
    oscRouterService.LogWritten += WriteLog;
    oscRouterService.ObservedValueReceived += observedValue => ObserveOscValue(observedValue);
    vrChatLocalOscCacheService.AvatarCacheUpdated += OnLocalOscCacheUpdated;
}
```

Change to:

```csharp
private readonly IActivityResumeService activityResumeService;

public BridgeCoordinator(
    DesktopInputLockService desktopInputLockService,
    WorldCommandBlacklistService worldCommandBlacklistService,
    VrChatLocalOscCacheService vrChatLocalOscCacheService,
    IActivityResumeService? activityResumeService = null)
{
    this.desktopInputLockService = desktopInputLockService;
    this.worldCommandBlacklistService = worldCommandBlacklistService;
    this.vrChatLocalOscCacheService = vrChatLocalOscCacheService;
    this.activityResumeService = activityResumeService ?? new ActivityResumeService();
    oscRouterService.LogWritten += WriteLog;
    oscRouterService.ObservedValueReceived += observedValue => ObserveOscValue(observedValue);
    oscRouterService.DiscoveryStateChanged += OnOscDiscoveryStateChanged;
    vrChatLocalOscCacheService.AvatarCacheUpdated += OnLocalOscCacheUpdated;
}
```

- [ ] **Step 2: Add `OnOscDiscoveryStateChanged` handler**

Add this method near the constructor or near other event handlers:

```csharp
private void OnOscDiscoveryStateChanged(OscDiscoveryState state)
{
    if (state == OscDiscoveryState.Discovered)
    {
        _ = TryResumePendingActivitiesAsync();
    }
}
```

- [ ] **Step 3: Add `TryResumePendingActivitiesAsync` and `ResumeActivityAsync`**

Add these methods near `SetCurrentVrChatAvatar` or other state-related methods:

```csharp
private bool hasAttemptedResume;

public async Task TryResumePendingActivitiesAsync()
{
    if (hasAttemptedResume)
    {
        return;
    }

    if (!HasDiscoveredVrChat)
    {
        return;
    }

    var currentAvatarId = GetCurrentVrChatAvatarId();
    if (string.IsNullOrWhiteSpace(currentAvatarId))
    {
        return;
    }

    if (!activityResumeService.HasPendingResume)
    {
        hasAttemptedResume = true;
        return;
    }

    if (!activityResumeService.IsPendingForAvatar(currentAvatarId))
    {
        return;
    }

    hasAttemptedResume = true;
    var pendingActivities = activityResumeService.GetPendingActivities();
    if (pendingActivities.Count == 0)
    {
        return;
    }

    WriteLog("Resuming saved activities...");
    foreach (var activity in pendingActivities)
    {
        try
        {
            await ResumeActivityAsync(activity);
        }
        catch (Exception ex)
        {
            WriteLog($"Failed to resume activity {activity.Type} for rule {activity.RuleId}: {ex.Message}");
        }
    }

    await activityResumeService.ClearAllAsync();
    WriteLog("Saved activities resumed.");
}

private async Task ResumeActivityAsync(ResumeActivity activity)
{
    if (activeConfiguration is null)
    {
        return;
    }

    var cancellationToken = runtimeCancellation?.Token ?? CancellationToken.None;

    switch (activity.Type)
    {
        case ResumeActivityType.AvatarScale:
            {
                var rule = activeConfiguration.AvatarScaleRules.FirstOrDefault(r => r.Id == activity.RuleId);
                if (rule is null)
                {
                    return;
                }

                var incomingEvent = new UniversalIncomingEvent(
                    string.Empty,
                    string.Empty,
                    string.Empty,
                    string.Empty,
                    0,
                    DateTimeOffset.UtcNow);

                await ExecuteAvatarScaleRuleAsync(
                    rule,
                    incomingEvent,
                    isTest: false,
                    cancellationToken,
                    isResuming: true);
                break;
            }

        case ResumeActivityType.Movement:
            {
                var rule = activeConfiguration.Rules.FirstOrDefault(r => r.Id == activity.RuleId);
                if (rule is null)
                {
                    return;
                }

                await ExecuteRuleActionAsync(
                    rule,
                    null,
                    cancellationToken,
                    isTest: false,
                    queuedReplay: false,
                    allowLaneQueue: false,
                    isResuming: true);
                break;
            }

        case ResumeActivityType.AvatarChange:
            {
                var rule = activeConfiguration.Rules.FirstOrDefault(r => r.Id == activity.RuleId);
                if (rule is null)
                {
                    return;
                }

                await ExecuteRuleActionAsync(
                    rule,
                    null,
                    cancellationToken,
                    isTest: false,
                    queuedReplay: false,
                    allowLaneQueue: false,
                    isResuming: true);
                break;
            }
    }
}
```

- [ ] **Step 4: Add write hooks for activity start**

**Avatar scale start hook:**
Find the end of `ExecuteAvatarScaleRuleAsync` where the method returns `true` after successfully sending the scale. Look for the block after `ScheduleAvatarScaleRestoreSequence` (around line 4662). After the `ScheduleAvatarScaleRestoreSequence` call, add:

```csharp
if (!isTest && !isResuming)
{
    var effectDurationSeconds = GetAvatarScaleEffectDurationSeconds(rule);
    var expiresAt = effectDurationSeconds > 0 ? DateTimeOffset.UtcNow.AddSeconds(effectDurationSeconds) : (DateTimeOffset?)null;
    await activityResumeService.RecordActivityStartedAsync(new ResumeActivity
    {
        Type = ResumeActivityType.AvatarScale,
        RuleId = rule.Id,
        ExpiresAt = expiresAt,
        CurrentValue = targetHeight,
        Payload = new Dictionary<string, object>
        {
            ["scaleMode"] = rule.ScaleMode.ToString(),
            ["targetHeight"] = targetHeight
        }
    });
}
```

**Movement start hook:**
Find the end of `ExecuteRuleActionAsync` after the `ScheduleReset` or `ScheduleJumpPulseReset` call (around line 7302). After the schedule call, add:

```csharp
if (!isTest && !isResuming && executionRule.ActionType == OscActionType.PlayerMovement)
{
    var expiresAt = executionRule.DurationSeconds > 0 ? DateTimeOffset.UtcNow.AddSeconds(executionRule.DurationSeconds) : (DateTimeOffset?)null;
    await activityResumeService.RecordActivityStartedAsync(new ResumeActivity
    {
        Type = ResumeActivityType.Movement,
        RuleId = rule.Id,
        ExpiresAt = expiresAt,
        Payload = new Dictionary<string, object>
        {
            ["movementDirection"] = executionRule.MovementDirection.ToString()
        }
    });
}
```

**Avatar change start hook:**
In the same `ExecuteRuleActionAsync` method, after the `SetCurrentVrChatAvatar` call for avatar changes (around line 7257), add:

```csharp
if (!isTest && !isResuming && executionRule.ActionType is OscActionType.AvatarChange or OscActionType.AvatarRoulet)
{
    var expiresAt = executionRule.DurationSeconds > 0 ? DateTimeOffset.UtcNow.AddSeconds(executionRule.DurationSeconds) : (DateTimeOffset?)null;
    await activityResumeService.RecordActivityStartedAsync(new ResumeActivity
    {
        Type = ResumeActivityType.AvatarChange,
        RuleId = rule.Id,
        ExpiresAt = expiresAt,
        Payload = new Dictionary<string, object>
        {
            ["avatarTargetId"] = action.AvatarTargetId ?? string.Empty
        }
    });
}
```

- [ ] **Step 5: Add write hooks for activity end**

**Avatar scale end hook:**
Find `RunAvatarScaleRestoreSequenceAsync` or `ClearAvatarScaleState` or similar methods that clear the scale state. Look for a method that removes `activeAvatarScaleEffects` or `activeAvatarScaleHeightSessions`. When the scale is cleared, call:

```csharp
await activityResumeService.RecordActivityEndedAsync(ruleId);
```

A good place is in the `ClearAvatarScaleState` or similar method, or in `ScheduleAvatarScaleHeightSessionEnd` callback. Search for `activeAvatarScaleEffects.Remove` and add the hook after it.

**Movement end hook:**
Find the reset completion callback. For `ScheduleReset`, look for the method that handles the reset completion. When a movement reset completes, add:

```csharp
await activityResumeService.RecordActivityEndedAsync(ruleId);
```

**Avatar change end hook:**
Find the `pendingResets` completion logic. When an avatar change reset completes, add:

```csharp
await activityResumeService.RecordActivityEndedAsync(ruleId);
```

- [ ] **Step 6: Add `ClearAllAsync` call in `ClearRuntimeState`**

Find `ClearRuntimeState` (around line 16938). At the end of the method, add:

```csharp
await activityResumeService.ClearAllAsync();
```

- [ ] **Step 7: Reset `hasAttemptedResume` on stop**

Find `StopAsync` (around line 2427). At the end of the method, add:

```csharp
hasAttemptedResume = false;
```

- [ ] **Step 8: Commit**

```bash
git add "VrcTwitchOscBridge/Services/BridgeCoordinator.cs"
git commit -m "feat: add resume logic and write hooks to BridgeCoordinator"
```

---

### Task 12: Wire up `ActivityResumeService` in `MainWindowViewModel`

**Files:**
- Modify: `VrcTwitchOscBridge/ViewModels/MainWindowViewModel.cs`

- [ ] **Step 1: Create the service in the constructor**

Find the constructor (around line 626):

```csharp
public MainWindowViewModel()
{
    dispatcher = Dispatcher.CurrentDispatcher;
    desktopInputLockService = new DesktopInputLockService(dispatcher);
    bridgeCoordinator = new BridgeCoordinator(desktopInputLockService, worldCommandBlacklistService, vrChatLocalOscCacheService);
```

Change to:

```csharp
public MainWindowViewModel()
{
    dispatcher = Dispatcher.CurrentDispatcher;
    desktopInputLockService = new DesktopInputLockService(dispatcher);
    var activityResumeService = new ActivityResumeService();
    bridgeCoordinator = new BridgeCoordinator(desktopInputLockService, worldCommandBlacklistService, vrChatLocalOscCacheService, activityResumeService);
```

- [ ] **Step 2: Load pending resume in `InitializeAsync`**

Find `InitializeAsync` (around line 3718). After `QueueSave()` or before `QueueBridgeRefresh()`, add:

```csharp
await activityResumeService.LoadPendingAsync();
```

Wait, `activityResumeService` is a local variable in the constructor. We need to make it a field. Add a field:

```csharp
private readonly IActivityResumeService activityResumeService;
```

Then in the constructor:

```csharp
activityResumeService = new ActivityResumeService();
bridgeCoordinator = new BridgeCoordinator(desktopInputLockService, worldCommandBlacklistService, vrChatLocalOscCacheService, activityResumeService);
```

And in `InitializeAsync`, after loading settings, add:

```csharp
await activityResumeService.LoadPendingAsync();
```

- [ ] **Step 3: Commit**

```bash
git add "VrcTwitchOscBridge/ViewModels/MainWindowViewModel.cs"
git commit -m "feat: wire up ActivityResumeService in MainWindowViewModel"
```

---

### Task 13: Update `VrcTwitchOscBridge.csproj`

**Files:**
- Modify: `VrcTwitchOscBridge/VrcTwitchOscBridge.csproj`

- [ ] **Step 1: Add new `.cs` files to the `<Compile>` group**

Find the `<Compile>` group in the project file. Add these lines in alphabetical order with the other `Models` and `Services` entries:

```xml
<Compile Include="Models\ActivityResumeSnapshot.cs" />
<Compile Include="Models\ResumeActivity.cs" />
<Compile Include="Models\ResumeActivityType.cs" />
<Compile Include="Services\ActivityResumeService.cs" />
<Compile Include="Services\IActivityResumeService.cs" />
```

- [ ] **Step 2: Commit**

```bash
git add "VrcTwitchOscBridge/VrcTwitchOscBridge.csproj"
git commit -m "feat: add ActivityResume files to project"
```

---

### Task 14: Build and verify

**Files:**
- None (verification only)

- [ ] **Step 1: Build the project**

Run:

```bash
dotnet build "VrcTwitchOscBridge/VrcTwitchOscBridge.csproj" --no-restore
```

Expected: Build succeeds with 0 errors.

- [ ] **Step 2: Fix any build errors**

If there are errors, fix them based on the error messages. Common issues:
- Missing `using` directives
- Type mismatches in `isResuming` parameter additions
- Missing `await` in new async calls

- [ ] **Step 3: Commit fixes**

```bash
git add .
git commit -m "fix: resolve build errors from ActivityResume implementation"
```

---

## Self-Review Checklist

- [ ] **Spec coverage:** Every requirement from the design spec is represented by at least one task.
- [ ] **Placeholder scan:** No `TBD`, `TODO`, or `implement later` strings remain.
- [ ] **Type consistency:** `isResuming` parameter name is consistent across all methods.
- [ ] **File inclusion:** All new `.cs` files are added to the `.csproj`.
- [ ] **Event wiring:** `DiscoveryStateChanged` is fired from `OscRouterService` and subscribed to in `BridgeCoordinator`.
- [ ] **Cleanup path:** `ClearRuntimeState` calls `activityResumeService.ClearAllAsync()`.
- [ ] **Resume gating:** `hasAttemptedResume` is reset on `StopAsync` and checked before resuming.

---

*Plan generated on 2026-06-07 based on approved design spec.*