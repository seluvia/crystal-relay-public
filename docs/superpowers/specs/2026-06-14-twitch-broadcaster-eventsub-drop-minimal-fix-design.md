# Twitch Broadcaster EventSub Drop — Minimal Fix Design

**Status:** Approved (brainstorming complete)
**Date:** 2026-06-14
**Scope:** Minimal — narrow two over-broad `catch` blocks

## Problem

Crystal Relay's broadcaster-side Twitch EventSub connection drops and cannot recover on its own during long streaming sessions. The user must reboot Crystal Relay to restore Twitch listening. After reboot, the connection works for some time and then drops again.

### Log evidence (debug-20260614.log)

```
22:02:56  Connected to Twitch EventSub. Listening and working.
22:24:58  EventSub connection issue: Glitchy Movement must be resolved before sending movement input.
22:24:58  Bridge status: Twitch connection issue. Retrying...
22:25:03  Bridge status: Connecting background listener...
22:25:03  Connected to Twitch EventSub. Listening and working.
23:02:57  Twitch validation failed: Bot Twitch login needs reconnecting.
23:02:57  Bridge status: OAuth [redacted] expired. Please reconnect Twitch.
23:02:58+ Bot account is not ready yet... (repeats ~1/sec)
```

### Root cause

Two separate over-broad `catch (Exception ex)` blocks combine to kill the bridge even when only one small subsystem has a transient error.

#### Bug A — `RunEventSubLoopAsync` catch in `BridgeCoordinator.cs:3054`

A single `try` wraps the entire EventSub session lifecycle, including the `session.ListenAsync(notification => HandleNotificationAsync(...))` call. When the notification handler throws (e.g., the "Glitchy Movement must be resolved before sending movement input" error logged at 22:24:58), the healthy WebSocket is disposed and a new connection is forced. If the handler bug keeps re-firing, the fixed 5-second retry delay hammers Twitch with reconnect attempts.

The catch should only fire for true WebSocket I/O failures, not for handler-side exceptions.

#### Bug B — `RunValidationLoopAsync` catch in `BridgeCoordinator.cs:2614-2653`

The hourly validation timer validates both broadcaster and bot accounts in one `try` block. If either throws, the catch calls `runtimeCancellation?.Cancel()` and `return`s, killing the entire bridge. The cancellation is unrecoverable until the user reboots the app.

The user has `UseBroadcasterAsBotSender = true` (settings field on `AppSettings`, surfaced in `BridgeRuntimeConfiguration`). In that mode, the bot account is configured but not actually used to send chat messages — the broadcaster account does. So a bot-side failure should not be fatal to the bridge. Currently it is.

## Approach

Narrow both catches. No new dependencies, no architecture changes, no new timers, no new health-check code. Approximately 30 lines of changed code across two methods in one file.

### Change 1: Bug A — `RunEventSubLoopAsync` (`BridgeCoordinator.cs:3012-3062`)

Split the single `try` into two scopes:

- **Outer scope:** WebSocket I/O lifecycle (connect, session_welcome wait, listen, dispose). Only exceptions from this scope tear down the session.
- **Inner scope:** notification-handler dispatch. Exceptions here are logged and swallowed so the WebSocket stays open and continues reading.

Outer catch keeps the existing behavior for true connection failures: log, post status, 5-second delay, loop again. The inner catch logs with a distinct prefix so we can tell the two paths apart in the log.

```
StatusChanged?.Invoke(reconnectUrl is null ? "Connecting background listener..." : "Reconnecting background listener...");
await session.ConnectAsync(reconnectUrl, cancellationToken);

WriteLog("Connected to Twitch EventSub. Listening and working.");

if (reconnectUrl is null) {
    await RefreshSubscriptionsAsync(session.SessionId, cancellationToken);
}

await RefreshChatBadgeCatalogAsync(cancellationToken);

StatusChanged?.Invoke("Listening for Twitch triggers.");
try {
    var result = await session.ListenAsync(notification => {
        try {
            return HandleNotificationAsync(notification, cancellationToken);
        }
        catch (Exception handlerEx) {
            WriteLog($"Notification handler error (keeping connection open): {handlerEx.Message}");
            return Task.CompletedTask;
        }
    }, cancellationToken);

    if (cancellationToken.IsCancellationRequested) {
        return;
    }

    reconnectUrl = result.ReconnectRequested ? result.ReconnectUrl : null;
    WriteLog(result.Reason);

    if (!result.ReconnectRequested) {
        StatusChanged?.Invoke("Listener disconnected. Retrying...");
        await Task.Delay(TimeSpan.FromSeconds(3), cancellationToken);
    }
}
catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested) {
    return;
}
catch (Exception ex) {
    reconnectUrl = null;
    WriteLog($"EventSub connection issue: {ex.Message}");
    StatusChanged?.Invoke("Twitch connection issue. Retrying...");
    await Task.Delay(TimeSpan.FromSeconds(5), cancellationToken);
}
```

The `await using var session = new TwitchEventSubSession();` line stays outside both catches (it must be, because the `using` needs to span the inner try for the inner catch to actually keep the session alive).

### Change 2: Bug B — `RunValidationLoopAsync` (`BridgeCoordinator.cs:2614-2653`)

Split the broadcaster + bot validation into two independent try blocks. The broadcaster block keeps its existing fatal-failure behavior. The bot block is non-fatal:

- If `UseBroadcasterAsBotSender = true`: log a non-fatal "broadcaster is used as chat sender" message and clear the in-memory `bot` field so downstream operations stop trying to use it. Runtime continues.
- If `UseBroadcasterAsBotSender = false`: log a non-fatal warning, post a status message about bot needing reconnect, but do **not** cancel the runtime. The user can reconnect the bot later. Clear `bot = null` so the loop and downstream skip it.

The runtime is only cancelled if the **broadcaster** validation fails, because EventSub subscription registration and reward sync both depend on the broadcaster's access token.

```
while (await timer.WaitForNextTickAsync(cancellationToken)) {
    // Broadcaster validation - fatal on failure.
    try {
        if (broadcaster is not null) {
            broadcaster = await EnsureAccountReadyAsync(broadcaster, TwitchScopes.BroadcasterRequired, BridgeAccountRole.Broadcaster, cancellationToken);
            await RefreshBroadcasterLiveStateAsync(cancellationToken);
        }
        WriteLog("Validated the Twitch OAuth sessions.");
    }
    catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested) {
        throw;
    }
    catch (Exception ex) {
        WriteLog($"Twitch validation failed: {ex.Message}");
        if (isBroadcasterLive) {
            isBroadcasterLive = false;
            nextTriggerInfoAnnouncementAt = DateTimeOffset.MinValue;
            StreamStateChanged?.Invoke(false, false);
        }
        StatusChanged?.Invoke("OAuth session expired. Please reconnect Twitch.");
        runtimeCancellation?.Cancel();
        return;
    }

    // Bot validation - non-fatal on failure.
    if (bot is not null) {
        try {
            bot = await EnsureAccountReadyAsync(bot, TwitchScopes.Bot, BridgeAccountRole.Bot, cancellationToken);
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested) {
            throw;
        }
        catch (Exception ex) {
            var broadcasterIsSender = activeConfiguration?.UseBroadcasterAsBotSender ?? false;
            if (broadcasterIsSender) {
                WriteLog($"Bot Twitch validation failed (broadcaster is used as chat sender, so the bridge is unaffected): {ex.Message}");
                bot = null;
            }
            else {
                WriteLog($"Bot Twitch validation failed: {ex.Message}");
                StatusChanged?.Invoke("Bot Twitch login needs reconnecting. Chat announcements are disabled until then.");
                bot = null;
            }
        }
    }
}
```

## Out of scope

The following were considered and explicitly rejected for this minimal fix:

- **Exponential backoff / jitter on EventSub reconnect** — would prevent the rate-limit-storm failure mode but is a separate change with its own testing surface.
- **Max retry count** — would prevent infinite retry loops but is not strictly needed once Bug A no longer causes the storm.
- **Silent-socket-death watchdog** — would detect dead WebSockets that never receive a close frame, but is a much larger change and there's no evidence from the current logs that the user's sessions are dying silently rather than through a handler error.
- **Twitch-level (application-level) pings** — Twitch's own `session_keepalive` messages are already being received (just silently dropped). Adding an app-level ping on top is redundant.
- **Automated unit/integration tests** — the repo has no test framework for `VrcTwitchOscBridge`. Manual + log verification is the established pattern for this codebase.
- **The 1-second "Bot account is not ready yet" log spam after OAuth failure** — this is a separate, lower-priority issue. It originates from a different service that polls the bot. Not blocking this fix.
- **More aggressive `UseBroadcasterAsBotSender` short-circuits in `ResolveChatMessageSenderAsync`** — already handled there at `BridgeCoordinator.cs:10353-10366`. The fix is only needed in the validation loop.

## Testing & verification

No automated tests. Manual + log-based verification per the established pattern for this codebase.

### Build verification

```
dotnet build "E:\!!!Program to work on\Proper Crystal Relay\VrcTwitchOscBridge\VrcTwitchOscBridge.csproj" --no-restore
```

Must succeed with zero errors. Run after the code change.

### Manual scenario A — Bug A regression test (notification handler error)

1. Launch app, connect broadcaster account, start bridge.
2. Confirm "Connected to Twitch EventSub. Listening and working." appears in the log.
3. Trigger a condition that makes `HandleNotificationAsync` throw. In the existing logs, the Glitchy Movement race condition did this organically at 22:24:58. To reproduce deliberately: trigger a movement redeem while a Glitchy Movement is mid-resolve, or temporarily inject a throw into a notification handler.
4. **Expected:** The notification error is logged once with the new prefix "Notification handler error (keeping connection open):", the WebSocket stays connected, status stays "Listening for Twitch triggers.", and subsequent redemptions still work.
5. **Failure indicator:** "Twitch connection issue. Retrying..." status appears, or a new "Connected to Twitch EventSub" line appears. That means the inner catch is not swallowing the handler error and the outer catch is tearing down the session.

### Manual scenario B — Bug B regression test (bot failure with broadcaster-as-bot)

1. Confirm `UseBroadcasterAsBotSender` is enabled in the user's settings (the user already has it on).
2. Confirm the bot account is configured but its OAuth is invalid (in the user's existing logs, this was the case).
3. Start bridge. Confirm "Connected to Twitch EventSub" appears.
4. Wait for the 1-hour validation tick to fire, OR temporarily reduce the timer interval for the test (e.g., 5 seconds) by editing `TimeSpan.FromHours(1)` to `TimeSpan.FromSeconds(5)` in the test build, triggering the failure on demand.
5. **Expected:** "Bot Twitch validation failed (broadcaster is used as chat sender, so the bridge is unaffected)" appears in the log once. The bridge keeps running. Redemptions still work. Status stays "Listening for Twitch triggers." No "OAuth session expired" message, no `runtimeCancellation?.Cancel()`.
6. **Failure indicator:** "OAuth session expired. Please reconnect Twitch." appears, the bridge status changes, or `isBroadcasterLive` flips to false. That means the bot catch wasn't separated from the broadcaster catch.

### Manual scenario C — Regression check (broadcaster validation still fatal)

1. With `UseBroadcasterAsBotSender` enabled, force the broadcaster validation to fail (e.g., revoke/invalidate the broadcaster refresh token in Windows Credential Manager and restart the app so the saved token is rejected on next validation).
2. **Expected:** "Twitch validation failed:" and "OAuth session expired. Please reconnect Twitch." appear, the bridge stops, and the user is prompted to reconnect. This is the same as the existing behavior — broadcaster failure is still fatal because EventSub and reward sync depend on the broadcaster's access token. The fix should not change this path.

### Log verification

After running the manual scenarios, grep the new log file for:

- `"Notification handler error (keeping connection open):"` — should appear when a handler error occurs. Confirms the inner catch is firing.
- `"Bot Twitch validation failed (broadcaster is used as chat sender"` — should appear when bot validation fails and broadcaster-as-bot is on. Confirms the new non-fatal path is firing.
- `"Bot Twitch validation failed:"` (without the broadcaster-as-bot suffix) — should appear when bot validation fails and broadcaster-as-bot is off. Confirms the alternate non-fatal path is firing.
- `"Twitch validation failed:"` — should still appear when broadcaster validation fails. Confirms no regression in the fatal broadcaster path.

## Files changed

- `VrcTwitchOscBridge/Services/BridgeCoordinator.cs` — two methods modified, no new methods, no new fields, no new types.

## Risk

Low. Two surgical edits in one file. No new dependencies. No public API changes. No new state. No new timers. The broadcaster-fatal path is preserved exactly. The bot-fatal path becomes bot-non-fatal, which is a behavior improvement even for users who don't use broadcaster-as-bot (their bridge will keep running if only the bot needs reconnecting, instead of dying).

The only user-visible behavior change is: with `UseBroadcasterAsBotSender = true`, a bot OAuth failure no longer kills the bridge. This matches user expectations — the bot isn't actually being used for chat in that mode.
