# Twitch Broadcaster EventSub Drop — Minimal Fix Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Stop Crystal Relay from killing the Twitch EventSub bridge when a notification handler throws or when the optional bot account's OAuth validation fails.

**Architecture:** Two surgical edits in `BridgeCoordinator.cs`. The EventSub loop's `catch` is narrowed so notification-handler errors do not tear down a healthy WebSocket. The validation loop's bot-validation path is moved into its own non-fatal `try/catch` so a bot OAuth failure cannot cancel the runtime.

**Tech Stack:** C# / .NET 10 / WPF. No new dependencies, no new files, no new types. No automated test framework in this repo — verification is build + log search per the established Crystal Relay pattern.

**Spec:** `docs/superpowers/specs/2026-06-14-twitch-broadcaster-eventsub-drop-minimal-fix-design.md`

---

## File Structure

**Files modified:**
- `VrcTwitchOscBridge/Services/BridgeCoordinator.cs` — two methods edited (`RunEventSubLoopAsync` at lines 3012–3062, `RunValidationLoopAsync` at lines 2614–2653). No new files, no new methods, no new fields, no new types.

**Files unchanged:** everything else.

---

## Task 1: Narrow `RunEventSubLoopAsync` catch (Bug A)

**Files:**
- Modify: `VrcTwitchOscBridge/Services/BridgeCoordinator.cs:3012-3062` (the `RunEventSubLoopAsync` method body)

- [ ] **Step 1: Confirm the current code matches the spec**

Open `VrcTwitchOscBridge/Services/BridgeCoordinator.cs`, navigate to line 3012, and confirm the `RunEventSubLoopAsync` method body looks like this (this is the pre-edit state):

```csharp
private async Task RunEventSubLoopAsync(CancellationToken cancellationToken)
{
    string? reconnectUrl = null;

    while (!cancellationToken.IsCancellationRequested)
    {
        try
        {
            await using var session = new TwitchEventSubSession();
            StatusChanged?.Invoke(reconnectUrl is null ? "Connecting background listener..." : "Reconnecting background listener...");
            await session.ConnectAsync(reconnectUrl, cancellationToken);

            WriteLog("Connected to Twitch EventSub. Listening and working.");

            if (reconnectUrl is null)
            {
                await RefreshSubscriptionsAsync(session.SessionId, cancellationToken);
            }

            await RefreshChatBadgeCatalogAsync(cancellationToken);

            StatusChanged?.Invoke("Listening for Twitch triggers.");
            var result = await session.ListenAsync(notification => HandleNotificationAsync(notification, cancellationToken), cancellationToken);

            if (cancellationToken.IsCancellationRequested)
            {
                return;
            }

            reconnectUrl = result.ReconnectRequested ? result.ReconnectUrl : null;
            WriteLog(result.Reason);

            if (!result.ReconnectRequested)
            {
                StatusChanged?.Invoke("Listener disconnected. Retrying...");
                await Task.Delay(TimeSpan.FromSeconds(3), cancellationToken);
            }
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            return;
        }
        catch (Exception ex)
        {
            reconnectUrl = null;
            WriteLog($"EventSub connection issue: {ex.Message}");
            StatusChanged?.Invoke("Twitch connection issue. Retrying...");
            await Task.Delay(TimeSpan.FromSeconds(5), cancellationToken);
        }
    }
}
```

If the code does not match (e.g., it has already been edited), stop and ask the user before proceeding.

- [ ] **Step 2: Verify the pre-edit build succeeds**

Run:
```
dotnet build "E:\!!!Program to work on\Proper Crystal Relay\VrcTwitchOscBridge\VrcTwitchOscBridge.csproj" --no-restore
```

Expected: `Build succeeded` with zero errors. Warnings are fine. If the build fails, stop and resolve before proceeding.

- [ ] **Step 3: Apply the edit — narrow the catch**

In `VrcTwitchOscBridge/Services/BridgeCoordinator.cs`, replace the body of `RunEventSubLoopAsync` (lines 3012–3062) with the following. The change wraps the `session.ListenAsync` call in its own try/catch and moves the connection-level catch outside it.

```csharp
private async Task RunEventSubLoopAsync(CancellationToken cancellationToken)
{
    string? reconnectUrl = null;

    while (!cancellationToken.IsCancellationRequested)
    {
        try
        {
            await using var session = new TwitchEventSubSession();
            StatusChanged?.Invoke(reconnectUrl is null ? "Connecting background listener..." : "Reconnecting background listener...");
            await session.ConnectAsync(reconnectUrl, cancellationToken);

            WriteLog("Connected to Twitch EventSub. Listening and working.");

            if (reconnectUrl is null)
            {
                await RefreshSubscriptionsAsync(session.SessionId, cancellationToken);
            }

            await RefreshChatBadgeCatalogAsync(cancellationToken);

            StatusChanged?.Invoke("Listening for Twitch triggers.");
            try
            {
                var result = await session.ListenAsync(notification => HandleNotificationSafelyAsync(notification, cancellationToken), cancellationToken);

                if (cancellationToken.IsCancellationRequested)
                {
                    return;
                }

                reconnectUrl = result.ReconnectRequested ? result.ReconnectUrl : null;
                WriteLog(result.Reason);

                if (!result.ReconnectRequested)
                {
                    StatusChanged?.Invoke("Listener disconnected. Retrying...");
                    await Task.Delay(TimeSpan.FromSeconds(3), cancellationToken);
                }
            }
            catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
            {
                return;
            }
            catch (Exception ex)
            {
                reconnectUrl = null;
                WriteLog($"EventSub connection issue: {ex.Message}");
                StatusChanged?.Invoke("Twitch connection issue. Retrying...");
                await Task.Delay(TimeSpan.FromSeconds(5), cancellationToken);
            }
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            return;
        }
        catch (Exception ex)
        {
            reconnectUrl = null;
            WriteLog($"EventSub connection issue: {ex.Message}");
            StatusChanged?.Invoke("Twitch connection issue. Retrying...");
            await Task.Delay(TimeSpan.FromSeconds(5), cancellationToken);
        }
    }
}

private async Task HandleNotificationSafelyAsync(EventSubNotification notification, CancellationToken cancellationToken)
{
    try
    {
        await HandleNotificationAsync(notification, cancellationToken);
    }
    catch (Exception ex)
    {
        WriteLog($"Notification handler error (keeping connection open): {ex.Message}");
    }
}
```

Notes for the engineer:
- The outer try/catch is preserved as a safety net for any exception thrown outside the inner `session.ListenAsync` call (e.g., from `session.ConnectAsync` or `RefreshSubscriptionsAsync`).
- `HandleNotificationSafelyAsync` is a new private method on the same class. It just wraps the existing `HandleNotificationAsync` in a try/catch and logs. No other behavior change.
- Do not modify `HandleNotificationAsync` itself.

- [ ] **Step 4: Verify the post-edit build succeeds**

Run:
```
dotnet build "E:\!!!Program to work on\Proper Crystal Relay\VrcTwitchOscBridge\VrcTwitchOscBridge.csproj" --no-restore
```

Expected: `Build succeeded` with zero errors. Warnings about unused fields or nullable references are fine. If the build fails, the most likely cause is a missing `using` statement or a typo in the method signature — fix and rebuild.

- [ ] **Step 5: Manual scenario A — verify the fix**

1. Launch Crystal Relay from the debug launcher: `E:\!!!Program to work on\Proper Crystal Relay\Launch-Crystal-Relay-Debug.bat`
2. Connect broadcaster account, start the bridge.
3. Confirm the log shows `Connected to Twitch EventSub. Listening and working.`
4. Trigger a condition that causes `HandleNotificationAsync` to throw. The most reliable reproducer based on the existing logs: trigger a movement redeem while a Glitchy Movement is mid-resolve, which produces the "Glitchy Movement must be resolved before sending movement input" exception. Alternatively, temporarily inject `throw new InvalidOperationException("test handler error");` at the top of `HandleNotificationAsync` for one cycle.
5. **Expected log line:** `[timestamp] Notification handler error (keeping connection open): Glitchy Movement must be resolved before sending movement input.`
6. **Expected status:** Bridge status stays `Listening for Twitch triggers.`
7. **Expected behavior:** Subsequent redemptions still work. The WebSocket is not torn down.
8. **Failure indicator:** A new `Bridge status: Twitch connection issue. Retrying...` line followed by `Bridge status: Connecting background listener...` and `Connected to Twitch EventSub.` appears. That means the outer catch is still firing. Re-read the diff and ensure the inner try wraps the `session.ListenAsync` call and the outer catch is still present as a safety net.
9. After verifying, revert any temporary test-only edits (e.g., the injected `throw` in `HandleNotificationAsync`) if you added them.

- [ ] **Step 6: Commit**

Run from `E:\!!!Program to work on\Proper Crystal Relay`:
```
git add VrcTwitchOscBridge/Services/BridgeCoordinator.cs
git commit -m "fix(eventsub): keep WebSocket open when a notification handler throws"
```

Expected: a single-file commit. Confirm with `git log --oneline -1` that the new commit is on top.

---

## Task 2: Make bot validation non-fatal in `RunValidationLoopAsync` (Bug B)

**Files:**
- Modify: `VrcTwitchOscBridge/Services/BridgeCoordinator.cs:2614-2653` (the `RunValidationLoopAsync` method body)

- [ ] **Step 1: Confirm the current code matches the spec**

Open `VrcTwitchOscBridge/Services/BridgeCoordinator.cs`, navigate to line 2614, and confirm the `RunValidationLoopAsync` method body looks like this (this is the pre-edit state):

```csharp
private async Task RunValidationLoopAsync(CancellationToken cancellationToken)
{
    using var timer = new PeriodicTimer(TimeSpan.FromHours(1));

    while (await timer.WaitForNextTickAsync(cancellationToken))
    {
        try
        {
            if (broadcaster is not null)
            {
                broadcaster = await EnsureAccountReadyAsync(broadcaster, TwitchScopes.BroadcasterRequired, BridgeAccountRole.Broadcaster, cancellationToken);
                await RefreshBroadcasterLiveStateAsync(cancellationToken);
            }

            if (bot is not null)
            {
                bot = await EnsureAccountReadyAsync(bot, TwitchScopes.Bot, BridgeAccountRole.Bot, cancellationToken);
            }

            WriteLog("Validated the Twitch OAuth sessions.");
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            throw;
        }
        catch (Exception ex)
        {
            WriteLog($"Twitch validation failed: {ex.Message}");
            if (isBroadcasterLive)
            {
                isBroadcasterLive = false;
                nextTriggerInfoAnnouncementAt = DateTimeOffset.MinValue;
                StreamStateChanged?.Invoke(false, false);
            }

            StatusChanged?.Invoke("OAuth session expired. Please reconnect Twitch.");
            runtimeCancellation?.Cancel();
            return;
        }
    }
}
```

If the code does not match (e.g., it has already been edited), stop and ask the user before proceeding.

- [ ] **Step 2: Apply the edit — separate broadcaster and bot validation**

In `VrcTwitchOscBridge/Services/BridgeCoordinator.cs`, replace the body of `RunValidationLoopAsync` with the following. The change splits the single try block into two: the broadcaster block keeps its existing fatal-failure behavior; the bot block is moved into its own non-fatal try/catch.

```csharp
private async Task RunValidationLoopAsync(CancellationToken cancellationToken)
{
    using var timer = new PeriodicTimer(TimeSpan.FromHours(1));

    while (await timer.WaitForNextTickAsync(cancellationToken))
    {
        try
        {
            if (broadcaster is not null)
            {
                broadcaster = await EnsureAccountReadyAsync(broadcaster, TwitchScopes.BroadcasterRequired, BridgeAccountRole.Broadcaster, cancellationToken);
                await RefreshBroadcasterLiveStateAsync(cancellationToken);
            }

            WriteLog("Validated the Twitch OAuth sessions.");
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            throw;
        }
        catch (Exception ex)
        {
            WriteLog($"Twitch validation failed: {ex.Message}");
            if (isBroadcasterLive)
            {
                isBroadcasterLive = false;
                nextTriggerInfoAnnouncementAt = DateTimeOffset.MinValue;
                StreamStateChanged?.Invoke(false, false);
            }

            StatusChanged?.Invoke("OAuth session expired. Please reconnect Twitch.");
            runtimeCancellation?.Cancel();
            return;
        }

        if (bot is not null)
        {
            try
            {
                bot = await EnsureAccountReadyAsync(bot, TwitchScopes.Bot, BridgeAccountRole.Bot, cancellationToken);
            }
            catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
            {
                throw;
            }
            catch (Exception ex)
            {
                var broadcasterIsSender = activeConfiguration?.UseBroadcasterAsBotSender ?? false;
                if (broadcasterIsSender)
                {
                    WriteLog($"Bot Twitch validation failed (broadcaster is used as chat sender, so the bridge is unaffected): {ex.Message}");
                    bot = null;
                }
                else
                {
                    WriteLog($"Bot Twitch validation failed: {ex.Message}");
                    StatusChanged?.Invoke("Bot Twitch login needs reconnecting. Chat announcements are disabled until then.");
                    bot = null;
                }
            }
        }
    }
}
```

Notes for the engineer:
- The broadcaster try block is unchanged from the pre-edit state. Its catch is still fatal.
- The bot validation block is now outside the broadcaster try and has its own catch. The catch logs and clears the in-memory `bot` field but does NOT call `runtimeCancellation?.Cancel()` and does NOT return. The outer `while` loop continues to the next tick.
- `activeConfiguration` is an existing private field on `BridgeCoordinator`. It may be null if the bridge hasn't started yet, in which case the `??` falls back to `false` (treat as fatal bot-failure mode). This is safe because the bot would not have been initialized either.
- Do not introduce any new fields, methods, or types.

- [ ] **Step 3: Verify the post-edit build succeeds**

Run:
```
dotnet build "E:\!!!Program to work on\Proper Crystal Relay\VrcTwitchOscBridge\VrcTwitchOscBridge.csproj" --no-restore
```

Expected: `Build succeeded` with zero errors. If the build fails, the most likely cause is a typo in the field access or a missing `using` statement — fix and rebuild.

- [ ] **Step 4: Manual scenario B — verify bot failure no longer kills the bridge**

This is the scenario that produced the user's 23:02:57 OAuth-expired death in the log.

1. Confirm `UseBroadcasterAsBotSender` is enabled in the user's settings (the user already has it on).
2. Confirm the bot account is configured but its OAuth session is invalid (the user's existing state from the logs).
3. To force the validation tick to fire quickly for testing, temporarily edit the line `using var timer = new PeriodicTimer(TimeSpan.FromHours(1));` to `using var timer = new PeriodicTimer(TimeSpan.FromSeconds(15));` so the bot validation fires within 15 seconds of bridge start.
4. Launch the app from the debug launcher. Connect broadcaster. Start the bridge.
5. **Expected log line** (within ~15 seconds of bridge start):
   ```
   Bot Twitch validation failed (broadcaster is used as chat sender, so the bridge is unaffected): Bot Twitch login needs reconnecting.
   ```
6. **Expected status:** Bridge status stays `Listening for Twitch triggers.`
7. **Expected behavior:** Subsequent redemptions still work. The bridge does not die. No `Bridge status: OAuth session expired. Please reconnect Twitch.` message.
8. **Failure indicator:** `Bridge status: OAuth session expired. Please reconnect Twitch.` appears and the bridge stops. That means the bot catch is still inside the broadcaster try block, or the `runtimeCancellation?.Cancel()` was not removed. Re-read the diff and ensure the bot try block is outside the broadcaster try block.
9. Revert the temporary 15-second timer edit back to `TimeSpan.FromHours(1)`.

- [ ] **Step 5: Manual scenario C — verify broadcaster failure is still fatal**

1. With `UseBroadcasterAsBotSender` enabled, force the broadcaster validation to fail. The simplest way: revoke/invalidate the broadcaster refresh token in Windows Credential Manager, then restart the app. On the next validation tick, `EnsureAccountReadyAsync` will throw for the broadcaster.
2. Wait for the validation tick (15 seconds if you kept the temporary short-timer edit, otherwise use the long timer and wait an hour — easier to keep the short-timer for this test too).
3. **Expected log line:** `Twitch validation failed: ...` (the broadcaster-side exception message).
4. **Expected status:** `Bridge status: OAuth session expired. Please reconnect Twitch.` appears. The bridge stops. The user must reconnect the broadcaster account. (Same as the existing behavior — broadcaster failure is still fatal because EventSub and reward sync depend on it. The fix must not change this path.)
5. **Failure indicator:** The bridge keeps running with an invalid broadcaster token. That means the broadcaster catch was accidentally non-fatal. Re-read the diff.
6. Revert the temporary 15-second timer edit back to `TimeSpan.FromHours(1)`.

- [ ] **Step 6: Commit**

Run from `E:\!!!Program to work on\Proper Crystal Relay`:
```
git add VrcTwitchOscBridge/Services/BridgeCoordinator.cs
git commit -m "fix(validation): make bot OAuth failure non-fatal so the bridge survives"
```

Expected: a single-file commit on top of Task 1's commit. Confirm with `git log --oneline -2` that both commits are present and in the right order.

---

## Task 3: Final verification

**Files:** none modified in this task. Verification only.

- [ ] **Step 1: Build the full project one more time**

Run:
```
dotnet build "E:\!!!Program to work on\Proper Crystal Relay\VrcTwitchOscBridge\VrcTwitchOscBridge.csproj" --no-restore
```

Expected: `Build succeeded` with zero errors. If the build fails, fix the regression before continuing.

- [ ] **Step 2: Confirm no temporary edits remain in the working tree**

Run from `E:\!!!Program to work on\Proper Crystal Relay`:
```
git diff
```

Expected: empty output. If anything is shown, it must be either (a) a debug edit you forgot to revert (e.g., the short-timer edit, the injected `throw` in `HandleNotificationAsync`), or (b) an unintentional change. Revert anything that isn't part of the two intentional edits from Tasks 1 and 2.

- [ ] **Step 3: Confirm the final commit history**

Run from `E:\!!!Program to work on\Proper Crystal Relay`:
```
git log --oneline -3
```

Expected output (exact commit hashes will differ, but the subject lines should be in this order, with the most recent on top):
```
fix(validation): make bot OAuth failure non-fatal so the bridge survives
fix(eventsub): keep WebSocket open when a notification handler throws
docs: design minimal fix for Twitch EventSub connection drops
```

- [ ] **Step 4: Optional — run the localization audit**

Crystal Relay's build scripts run a localization audit before publishing. This is a low-risk change with no new UI text or new localization keys, but the audit is cheap and worth running as a sanity check:

```
cd "E:\!!!Program to work on\Proper Crystal Relay\LocalizationAudit"
dotnet run --no-restore
```

Expected: audit reports no new missing keys. If it does report missing keys, this change did not introduce them (no XAML was touched, no new user-facing strings were added), but it's worth confirming.

- [ ] **Step 5: Report completion to the user**

The fix is complete. Tell the user:
- Last stable release: 3.1.8
- Active development build: 3.1.9
- Two commits applied: one narrowing the EventSub catch, one making bot validation non-fatal
- Manual verification recommended before the next test/beta build (use the debug launcher and trigger the relevant scenarios)
- If they want a test package or beta build, the next step is to run `Build-Crystal-Relay-Test.ps1` or `Build-Crystal-Relay-Beta.ps1` after updating `AGENTS.md`'s active build lane as the version policy requires.
