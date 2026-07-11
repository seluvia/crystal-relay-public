# Supporter Growth + Avatar Swap Timing Coordination

## Problem

When a single Twitch/Bits/CashPayment/Subs event triggers **both** a Supporter Growth (avatar scaling) rule **and** an avatar change rule, they run concurrently:

1. `StartMatchingAvatarScaleRules` runs first — fires Supporter Growth **fire-and-forget**
2. `SelectMatchingRules` runs later — executes avatar change **sequentially**

The growth sends `/avatar/eyeheight` to the **current avatar** (or mid-transition), then the avatar changes, losing the height. The existing carryover mechanism (2.5s fixed delay + 4×1s retry) tries to recover but is timing-based — it doesn't actually wait for the avatar to finish loading.

## Solution: OSC Eyeheight Feedback Signal

Use VRChat's own OSC feedback as a concrete "avatar fully loaded" signal. When VRChat finishes loading a new avatar, it sends the avatar's parameters back via OSC, including `/avatar/eyeheight`. By waiting for this feedback before applying growth height, we guarantee the new avatar is active.

### How It Works

1. **Avatar change initiates** — `SetCurrentVrChatAvatar()` creates/resets a `TaskCompletionSource` representing "avatar is loaded"
2. **VRChat loads the avatar** — On completion, VRChat sends OSC parameters back, including `/avatar/eyeheight`
3. **`ObserveOscValue()` detects the feedback** — When `/avatar/eyeheight` arrives and we're tracking a pending avatar change, it signals the TCS
4. **Growth (and carryover) await the signal** — Instead of blindly waiting 2.5s, they await the TCS with a configurable timeout fallback

### Files Changed

| File | Changes |
|------|---------|
| `Services/BridgeCoordinator.cs` | Add avatar-load tracking fields; modify `ObserveOscValue` to detect post-change eyeheight; gate growth/carryover on the signal |
| `Models/AvatarScaleRule.cs` | Optionally add a timeout config for avatar load wait |

### Detailed Flow

#### 1. Tracking State

```csharp
// Fields in BridgeCoordinator
private TaskCompletionSource? _avatarLoadedForScalingTcs;
private string? _pendingAvatarChangeId;
private CancellationTokenSource? _avatarLoadTimeoutCts;
```

#### 2. Signal Creation (`SetCurrentVrChatAvatar`)

`SetCurrentVrChatAvatar` is already called synchronously inside `ExecuteRuleActionAsync` after sending the avatar-change OSC to VRChat. At this point, add:

- Record `_avatarChangeInitiatedAt` = `DateTimeOffset.UtcNow`
- Set `_pendingAvatarChangeId` to the new avatar ID
- Create a fresh `_avatarLoadedForScalingTcs`
- Start a timeout CTS (e.g., 8 seconds)

#### 3. Signal Detection (`ObserveOscValue`)

When `/avatar/eyeheight` is observed and `_pendingAvatarChangeId` is not null:

- Compute elapsed = `DateTimeOffset.UtcNow - _avatarChangeInitiatedAt`
- If elapsed < 500ms: skip — this eyeheight could be from the old avatar (UDP in-flight)
- If elapsed >= 500ms: this is the new avatar's feedback — signal loaded
- Complete `_avatarLoadedForScalingTcs` and clear `_pendingAvatarChangeId`

#### 4. Consumer: Supporter Growth

In `ExecuteSupporterGrowthAvatarScaleRuleAsync` (or `RunSupporterGrowthScaleSessionAsync`), before sending height:

- Check if `_avatarLoadedForScalingTcs` exists (avatar change pending)
- If so, await it with the timeout
- Then proceed to send `/avatar/eyeheight`

#### 5. Consumer: Carryover Flow

In `HandleAvatarScaleAvatarChangedAsync`, replace the fixed 2.5s `Task.Delay` with awaiting `_avatarLoadedForScalingTcs.Task`:

- If the signal arrives before timeout, continue immediately (avatar confirmed loaded)
- If timeout expires, fall through to the existing retry logic
- This makes carryover react to actual load times instead of guessing

### Edge Cases

- **No avatar change** — `_avatarLoadedForScalingTcs` is null; growth proceeds immediately (existing behavior)
- **Timeout** — Signal never arrives; fall back to the existing fixed-delay behavior
- **Multiple rapid changes** — Each `SetCurrentVrChatAvatar` resets the TCS, so only the latest change is tracked
- **App shutdown** — CTS linked to runtime cancellation; all awaits cancel cleanly
- **VRChat not running** — Timeout fires; growth sends height anyway (best-effort)

### Non-Goals

- No UI changes
- No new user-facing settings (timeout is a constant)
- No changes to Bits/CashPayment word-trigger matching (confirmed working)
