# Supporter Growth + Avatar Swap Timing Coordination — Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Make Supporter Growth (and scale carryover) wait for VRChat's OSC `/avatar/eyeheight` feedback before applying height after an avatar change, replacing the current fixed 2.5s guessing delay.

**Architecture:** Add an OSC-feedback-based signal system to `BridgeCoordinator.cs`. When `SetCurrentVrChatAvatar` runs, it creates a `TaskCompletionSource`. `ObserveOscValue` completes it when `/avatar/eyeheight` arrives ≥500ms after the change (filtering out old-avatar UDP stragglers). Both `RunSupporterGrowthScaleSessionAsync` and `HandleAvatarScaleAvatarChangedAsync` await this signal before sending height, with an 8s timeout fallback.

**Tech Stack:** C#, .NET, OSC, single-file change to `BridgeCoordinator.cs`

## Global Constraints

- All changes in `VrcTwitchOscBridge\Services\BridgeCoordinator.cs` only
- No new user-facing settings
- 500ms grace period to filter old-avatar OSC stragglers
- 8s timeout fallback for avatar load signal
- Thread-safe access to new fields via `stateGate` lock (existing pattern)
- Build must pass with `dotnet build`

---

### Task 1: Add avatar-load tracking fields and timeout constant

**Files:**
- Modify: `VrcTwitchOscBridge\Services\BridgeCoordinator.cs` (near other `AvatarScale*` constants and fields)

- [ ] **Step 1: Add timeout constant**

Add near the existing `AvatarScale*` constants (~line 46-73):

```csharp
private static readonly TimeSpan AvatarScaleAvatarLoadSignalTimeout = TimeSpan.FromSeconds(8);
```

- [ ] **Step 2: Add tracking fields**

Add near the existing `avatarScale*` state fields (~line 189):

```csharp
private TaskCompletionSource? _avatarLoadedForScalingTcs;
private string? _pendingAvatarChangeId;
private CancellationTokenSource? _avatarLoadTimeoutCts;
```

- [ ] **Step 3: Build to verify**

```
dotnet build "E:\!!!Program to work on\Proper Crystal Relay\VrcTwitchOscBridge\VrcTwitchOscBridge.csproj" --no-restore
```

Expected: build succeeds (new fields have no references yet but are just nullable fields — no warnings)

---

### Task 2: Create avatar-load signal in `SetCurrentVrChatAvatar`

**Files:**
- Modify: `VrcTwitchOscBridge\Services\BridgeCoordinator.cs:14709-14749`

- [ ] **Step 1: Add signal creation inside the `changed` block**

After line 14741 (`lastAvatarChangeAt = DateTimeOffset.UtcNow;`), add:

```csharp
// Create avatar-loaded signal so growth/carryover can wait for the
// new avatar's /avatar/eyeheight OSC feedback before applying height
_pendingAvatarChangeId = normalizedAvatarId;
_avatarLoadTimeoutCts?.Cancel();
_avatarLoadTimeoutCts?.Dispose();
_avatarLoadTimeoutCts = new CancellationTokenSource();
_avatarLoadTimeoutCts.CancelAfter(AvatarScaleAvatarLoadSignalTimeout);
_avatarLoadedForScalingTcs = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
```

- [ ] **Step 2: Build to verify**

```
dotnet build "E:\!!!Program to work on\Proper Crystal Relay\VrcTwitchOscBridge\VrcTwitchOscBridge.csproj" --no-restore
```

Expected: build succeeds

---

### Task 3: Add `TrySignalAvatarLoadedForScaling` helper

**Files:**
- Modify: `VrcTwitchOscBridge\Services\BridgeCoordinator.cs` (add method near other avatar-scale helper methods)

- [ ] **Step 1: Add the signal helper method**

Add this method in the avatar-scale helper region (e.g., near `UpdateActiveAvatarScaleCarriedHeightLocked` around line 18026):

```csharp
private void TrySignalAvatarLoadedForScaling()
{
    if (_pendingAvatarChangeId is null || _avatarLoadedForScalingTcs is null)
        return;

    var elapsed = DateTimeOffset.UtcNow - lastAvatarChangeAt;
    if (elapsed.TotalMilliseconds < 500)
        return; // Too soon — could be old avatar's last OSC packet in-flight

    _avatarLoadedForScalingTcs.TrySetResult();
    _pendingAvatarChangeId = null;
    _avatarLoadTimeoutCts?.Dispose();
    _avatarLoadTimeoutCts = null;
    _avatarLoadedForScalingTcs = null;
}
```

- [ ] **Step 2: Call it from `ObserveOscValue` in the `/avatar/eyeheight` handler**

In `ObserveOscValue` at line 17956, inside the block that checks for `/avatar/eyeheight`, add `TrySignalAvatarLoadedForScaling();` before the existing carryover logic:

Old (around lines 17956-17968):
```csharp
if (string.Equals(observedValue.Address, "/avatar/eyeheight", StringComparison.Ordinal)
    && observedValue.ParameterType == OscParameterType.Float
    && observedValue.Value is float heightMeters)
{
    if (updateActiveAvatarScaleCarryover)
    {
        UpdateActiveAvatarScaleCarriedHeightLocked(heightMeters);
    }
    else
    {
        passiveCarryoverDiagnostic = TryCreatePassiveAvatarScaleCarryoverDiagnosticLocked(heightMeters);
    }
}
```

New:
```csharp
if (string.Equals(observedValue.Address, "/avatar/eyeheight", StringComparison.Ordinal)
    && observedValue.ParameterType == OscParameterType.Float
    && observedValue.Value is float heightMeters)
{
    TrySignalAvatarLoadedForScaling();

    if (updateActiveAvatarScaleCarryover)
    {
        UpdateActiveAvatarScaleCarriedHeightLocked(heightMeters);
    }
    else
    {
        passiveCarryoverDiagnostic = TryCreatePassiveAvatarScaleCarryoverDiagnosticLocked(heightMeters);
    }
}
```

- [ ] **Step 3: Build to verify**

```
dotnet build "E:\!!!Program to work on\Proper Crystal Relay\VrcTwitchOscBridge\VrcTwitchOscBridge.csproj" --no-restore
```

Expected: build succeeds

---

### Task 4: Add `WaitForAvatarLoadedForScalingAsync` helper

**Files:**
- Modify: `VrcTwitchOscBridge\Services\BridgeCoordinator.cs` (add method near `TrySignalAvatarLoadedForScaling`)

- [ ] **Step 1: Add the async waiter method**

Add this method right after `TrySignalAvatarLoadedForScaling`:

```csharp
/// <summary>
/// If an avatar change is in progress, waits for the new avatar's
/// /avatar/eyeheight OSC feedback (proxied by
/// <see cref="TrySignalAvatarLoadedForScaling"/>) before returning.
/// Falls back silently after the configured timeout.
/// </summary>
private async Task WaitForAvatarLoadedForScalingAsync(CancellationToken cancellationToken)
{
    TaskCompletionSource? tcs;
    CancellationTokenSource? timeoutCts;

    lock (stateGate)
    {
        tcs = _avatarLoadedForScalingTcs;
        timeoutCts = _avatarLoadTimeoutCts;
    }

    if (tcs is null || timeoutCts is null)
        return;

    using var linkedCts = CancellationTokenSource.CreateLinkedTokenSource(
        cancellationToken, timeoutCts.Token);
    try
    {
        await tcs.Task.WaitAsync(linkedCts.Token);
    }
    catch (OperationCanceledException)
    {
        // Timeout or cancellation — proceed anyway;
        // carryover/growth will apply height best-effort.
    }
}
```

- [ ] **Step 2: Build to verify**

```
dotnet build "E:\!!!Program to work on\Proper Crystal Relay\VrcTwitchOscBridge\VrcTwitchOscBridge.csproj" --no-restore
```

Expected: build succeeds

---

### Task 5: Gate Supporter Growth on avatar-loaded signal

**Files:**
- Modify: `VrcTwitchOscBridge\Services\BridgeCoordinator.cs:5860-5874`

- [ ] **Step 1: Add await before first height send in `RunSupporterGrowthScaleSessionAsync`**

In `RunSupporterGrowthScaleSessionAsync`, right before the first `SendAvatarHeightForOperationAsync` call (line 5869), add:

```csharp
await WaitForAvatarLoadedForScalingAsync(cancellationToken);
```

So the sequence becomes (around lines 5866-5874):
```csharp
var cancellationToken = sessionCancellation.Token;
var activeWindowSeconds = Math.Max(1, (paidActiveUntil - DateTimeOffset.UtcNow).TotalSeconds + smoothTransitionSeconds);
await WaitForAvatarLoadedForScalingAsync(cancellationToken);
if (!await SendAvatarHeightForOperationAsync(
```

- [ ] **Step 2: Build to verify**

```
dotnet build "E:\!!!Program to work on\Proper Crystal Relay\VrcTwitchOscBridge\VrcTwitchOscBridge.csproj" --no-restore
```

Expected: build succeeds

---

### Task 6: Gate carryover flow on avatar-loaded signal

**Files:**
- Modify: `VrcTwitchOscBridge\Services\BridgeCoordinator.cs:14809-14812`

- [ ] **Step 1: Replace fixed delay with signal await in `HandleAvatarScaleAvatarChangedAsync`**

Old (line 14812):
```csharp
await Task.Delay(AvatarScaleCarryoverInitialSendDelay, cancellationToken);
```

New:
```csharp
await WaitForAvatarLoadedForScalingAsync(cancellationToken);
```

- [ ] **Step 2: Verify the surrounding code still makes sense**

The log message at line 14811 references the old delay's description:
```csharp
WriteLog($"Avatar scale carryover from '{carryover.SourceRuleName}' is waiting {AvatarScaleCarryoverInitialSendDelay.TotalSeconds:0.#}s for the new avatar to finish loading before applying {carryover.CarriedHeightMeters:0.###}m.");
```

Update it to reflect the signal-based approach:
```csharp
WriteLog($"Avatar scale carryover from '{carryover.SourceRuleName}' is waiting for the new avatar to finish loading (via OSC eyeheight feedback, {AvatarScaleAvatarLoadSignalTimeout.TotalSeconds:0.#}s timeout) before applying {carryover.CarriedHeightMeters:0.###}m.");
```

- [ ] **Step 3: Build to verify**

```
dotnet build "E:\!!!Program to work on\Proper Crystal Relay\VrcTwitchOscBridge\VrcTwitchOscBridge.csproj" --no-restore
```

Expected: build succeeds

---

### Task 7: Final build and verification

- [ ] **Step 1: Full build**

```
dotnet build "E:\!!!Program to work on\Proper Crystal Relay\VrcTwitchOscBridge\VrcTwitchOscBridge.csproj"
```

Expected: Build succeeded with 0 warnings, 0 errors

- [ ] **Step 2: Verify the changes are coherent**

Read back the changed sections of BridgeCoordinator.cs and confirm:
- Fields are declared
- `SetCurrentVrChatAvatar` creates the signal in the `changed` block
- `ObserveOscValue` calls `TrySignalAvatarLoadedForScaling` on `/avatar/eyeheight`
- `TrySignalAvatarLoadedForScaling` completes the TCS after 500ms grace
- `WaitForAvatarLoadedForScalingAsync` captures TCS under lock, awaits outside lock
- `RunSupporterGrowthScaleSessionAsync` awaits the signal before first height send
- `HandleAvatarScaleAvatarChangedAsync` awaits the signal instead of fixed 2.5s delay
