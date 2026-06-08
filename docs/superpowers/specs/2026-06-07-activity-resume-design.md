# Activity Resume / Preserve System Design

**Date:** 2026-06-07  
**Product:** Crystal Relay  
**Status:** Approved — ready for implementation plan  

---

## 1. Goal

Provide a reliable way to preserve active bridge activities (avatar scaling, movement locks, temporary avatar changes) across app restarts, and resume them once OSC is fully connected and the same VRChat avatar is detected.

---

## 2. Scope

### In scope
- Avatar scaling (shrink / grow / eye height effects)
- Movement locks (glitchy movement, random movement, stop movement, stop turning, stop all)
- Temporary avatar changes / roulette (the current "swapped to" avatar)

### Out of scope
- Outfit / wardrobe changes (too complex, not requested)
- Rule cooldowns (acceptable to reset)
- Active float redeem sessions (not requested)
- Supporter growth scaling (not requested)
- Cash payment triggers (not a persistent state)
- Desktop hard locks (stop-input is a safety feature; re-triggering it automatically is risky)

---

## 3. Architecture Overview

A new `ActivityResumeService` is introduced. It sits next to `BridgeCoordinator` and owns a single JSON file: `activity-resume.json` in `%LOCALAPPDATA%\CrystalRelay\Secure\`.

### Write path
When `BridgeCoordinator` starts an in-scope activity (scale, movement, avatar change), it tells `ActivityResumeService` to record a snapshot. The service appends or updates it in the JSON file. When an activity ends or is cancelled, the service removes it.

### Read path
On startup, if the file exists, `ActivityResumeService` loads it into a `PendingResumeState`. It does nothing with it yet.

### Resume gate
`BridgeCoordinator` already waits for OSC connection and avatar detection. Once both are satisfied, it asks `ActivityResumeService` if there is a pending resume. If yes, the service calls back into `BridgeCoordinator` with each snapshot, and `BridgeCoordinator` safely rebuilds the state using its existing execution methods.

### Cleanup
After all snapshots are resumed, or if the user manually cancels, the file is deleted.

---

## 4. Resume Snapshot Model

```csharp
public class ActivityResumeSnapshot
{
    public int Version { get; set; } = 1;
    public DateTimeOffset SavedAt { get; set; }
    public string CurrentAvatarId { get; set; }
    public List<ResumeActivity> Activities { get; set; }
}

public class ResumeActivity
{
    public ResumeActivityType Type { get; set; }
    public Guid RuleId { get; set; }
    public DateTimeOffset? ExpiresAt { get; set; } // null = indefinite
    public double? CurrentValue { get; set; }       // e.g., current eye height
    public Dictionary<string, object> Payload { get; set; }
}

public enum ResumeActivityType
{
    AvatarScale,
    Movement,
    AvatarChange
}
```

### Design rationale
- `CurrentAvatarId` is the gate. Resume only proceeds if the current avatar matches.
- `ExpiresAt` is wall-clock time. The user chose **"resume exact remaining time"**, so we do not subtract offline time. Instead, we save `ExpiresAt = DateTimeOffset.UtcNow + remainingDuration`. On resume, we recalculate the remaining duration as `ExpiresAt - UtcNow`.
- `Payload` is a flexible dictionary for type-specific data (e.g., movement direction, glitchy mode, target avatar ID).

---

## 5. ActivityResumeService

```csharp
public interface IActivityResumeService
{
    Task LoadPendingAsync();
    bool HasPendingResume { get; }
    bool IsPendingForAvatar(string avatarId);
    IReadOnlyList<ResumeActivity> GetPendingActivities();
    Task RecordActivityStartedAsync(ResumeActivity activity);
    Task RecordActivityEndedAsync(Guid ruleId);
    Task ClearAllAsync();
    Task CommitAsync(); // writes to disk atomically
}
```

### Lifecycle
- `LoadPendingAsync()` is called during `MainWindowViewModel.InitializeAsync()`.
- `RecordActivityStartedAsync` / `RecordActivityEndedAsync` are called by `BridgeCoordinator` whenever an activity starts or ends.
- `CommitAsync` is called after a batch of changes (e.g., after a rule starts, or after a timer ends). It writes the entire snapshot to disk atomically (write to temp, then move).

---

## 6. BridgeCoordinator Integration

`BridgeCoordinator` gets a new `IActivityResumeService` dependency.

### Write hooks
- When `ExecuteAvatarScaleRuleAsync` starts a scale effect, it calls `RecordActivityStartedAsync`.
- When `RunAvatarScaleRestoreSequenceAsync` completes, it calls `RecordActivityEndedAsync`.
- When `ExecuteMovementRuleAsync` starts a movement lock, it calls `RecordActivityStartedAsync`.
- When `ClearRuntimeState()` is called (clean shutdown), it calls `ClearAllAsync`.

### Read / resume hooks
- After `BridgeCoordinator.StartAsync()` completes and `OscRouterService.HasDiscoveredVrChat` becomes true, the coordinator checks if there is a pending resume.
- If the current avatar matches `CurrentAvatarId`, it iterates through the pending activities and calls its own internal methods to rebuild state:
  - For a scale activity: it calls `ExecuteAvatarScaleRuleAsync` with the same rule but a `resumeFrom` parameter that skips the initial "announce" and avoids re-charging Twitch costs.
  - For a movement activity: it calls `ExecuteMovementRuleAsync` with a `resumeFrom` flag.
  - For an avatar change: it calls the avatar change path with the saved target avatar ID.

### Resume flag
A new `bool isResuming` parameter on key execution methods. When `true`, the method skips:
- Twitch chat announcements
- Reward cost deductions
- Sound effects
- Any side effects that should only happen on user-triggered execution.

It still sends the same OSC values and sets the same timers.

---

## 7. Startup Resume Flow

1. `App.xaml.cs` launches. If `restartRestoreState` is passed (from `ApplicationRestartService`), it is noted but does not override the resume file.
2. `MainWindowViewModel.InitializeAsync()` calls `ActivityResumeService.LoadPendingAsync()`.
3. `BridgeCoordinator.StartAsync()` starts OSC and Twitch.
4. `OscRouterService` discovers VRChat. `BridgeCoordinator` detects the current avatar via `VrChatLocalClientStateService`.
5. If `ActivityResumeService.HasPendingResume && ActivityResumeService.IsPendingForAvatar(currentAvatarId)`:
   - A small "Resuming activities..." toast is shown.
   - `BridgeCoordinator.ResumeActivitiesAsync()` is called.
   - Each activity is thawed using the existing execution paths with `isResuming = true`.
6. After resume completes, `ActivityResumeService.ClearAllAsync()` deletes the file.
7. If the avatar does not match, the pending resume stays in memory. If the user later changes to the matching avatar, the resume triggers then.

---

## 8. Error Handling & Edge Cases

| Scenario | Behavior |
|----------|----------|
| **Resume file is corrupt** | Log a warning, delete the file, skip resume. |
| **Resume file is for an older version** | If `Version != 1`, log and delete. Future migrations can be added. |
| **Avatar changes during resume** | If the avatar changes mid-resume, pause. Resume resumes when the original avatar is re-detected. |
| **OSC disconnects during resume** | Wait until OSC reconnects before continuing. |
| **User manually stops an activity** | `RecordActivityEndedAsync` removes it from the file. |
| **Clean shutdown with no active activities** | File is already empty or deleted. |
| **Crash / unclean shutdown** | File is left on disk. Next startup detects it and resumes. |
| **Multiple overlapping activities** | Each gets its own `ResumeActivity` entry. They are resumed in order. |

---

## 9. Files to Create / Modify

### New files
- `VrcTwitchOscBridge/Services/ActivityResumeService.cs`
- `VrcTwitchOscBridge/Models/ActivityResumeSnapshot.cs`
- `VrcTwitchOscBridge/Models/ResumeActivity.cs`
- `VrcTwitchOscBridge/Models/ResumeActivityType.cs`

### Modified files
- `VrcTwitchOscBridge/Services/BridgeCoordinator.cs`
- `VrcTwitchOscBridge/Services/BridgeCoordinator.Activities.cs` (if split)
- `VrcTwitchOscBridge/ViewModels/MainWindowViewModel.cs`
- `VrcTwitchOscBridge/VrcTwitchOscBridge.csproj` (add new .cs files)

---

## 10. Testing Notes

- Unit test: `ActivityResumeService` writes and reads JSON correctly.
- Unit test: `CommitAsync` is atomic (temp file then move).
- Unit test: Corrupt file is handled gracefully.
- Integration test: Start a scale effect, kill the app, restart, verify it resumes after OSC connects.
- Integration test: Avatar mismatch blocks resume; matching avatar allows it.
- Integration test: `isResuming = true` skips announcements and side effects.

---

## 11. Future Extensions

- Version 2 may add outfit/wardrobe resume.
- Version 2 may add desktop hard-lock resume (if explicitly requested and safety-reviewed).
- Version 2 may add cooldown snapshotting if users request it.

---

*Approved by user on 2026-06-07.*
