# Scale Timer Fairness and Support Extension — Design

Date: 2026-07-02
Status: Approved (brainstorm complete, pending implementation plan)

## Problem

Two issues affect Avatar Scaling runtime fairness:

1. **Scale timer reset on new trigger.** Today `ScheduleAvatarScaleRestoreSequence` in `BridgeCoordinator.cs` always replaces the restore timer with the new rule's `ActiveTimeSeconds`, canceling the previous restore sequence (`previousCancellation?.Cancel()` at line 5316). A late 10s shrink wipes out a 25s-remaining grow — the earlier redeemer's paid window is cut short.

2. **No way for support events (Bits/Subs/Cash) to extend an active activity.** Streamers want Bits/Subs/Cash support to prolong whatever is currently happening on stream (avatar change, scale effect, movement, etc.) rather than always triggering a new action.

## Design

### Part A — Scale timer fairness (two-tier highest-seen)

Split scale triggers into two priority tiers:

**Tier 1 — Channel points & Supporter Growth:**
- Track `highestSeenActiveTimeSeconds` across the current active scale window (starts at 0 when no scale is active).
- Each new Tier-1 trigger:
  - Changes avatar height to its own target immediately (newest wins height, per user decision).
  - Updates `highestSeenActiveTimeSeconds = max(highestSeenActiveTimeSeconds, newRule.ActiveTimeSeconds)`.
  - Resets the restore timer to `highestSeenActiveTimeSeconds`, counted from now.
  - Does NOT shorten the window below the highest `ActiveTimeSeconds` seen in this window.
- `highestSeenActiveTimeSeconds` resets to 0 when the restore sequence completes (no active scale).

Trace example:
- t=0: 60s grow triggers → timer=60s, highest=60s
- t=10: 30s shrink triggers → highest stays 60s, timer resets to 60s from t=10 (expires t=70). Avatar is small (shrink won height).
- t=20: 45s grow triggers → highest stays 60s, timer resets to 60s from t=20 (expires t=80)
- t=30: 90s grow triggers → highest becomes 90s, timer resets to 90s from t=30 (expires t=120)

**Tier 2 — Cash Payments & Power Ups (pay systems):**
- Always reset the timer to their own `ActiveTimeSeconds` and override height.
- Preempt Tier-1: a Tier-1 trigger after a Tier-2 trigger cannot shorten the Tier-2 window. The Tier-2 window runs its full course.
- When Tier-2 takes over, the Tier-1 `highestSeenActiveTimeSeconds` resets. If a Tier-1 trigger fires after Tier-2's window ends, it starts a fresh window with a fresh highest-seen.

**Restore height:** unchanged — always the new triggering rule's `RestoreHeightMeters`, since the new rule owns the height now.

**Where it changes:**
- `ScheduleAvatarScaleRestoreSequence` (`BridgeCoordinator.cs:5275`) and the restore-sequence state.
- Add a `highestSeenActiveTimeSeconds` field to the restore sequence state or a separate field on the coordinator.
- The `ActiveUntil` computation at line 5297 becomes tier-aware.
- The `previousCancellation?.Cancel()` at line 5316 stays for Tier-2 but becomes a reschedule-with-extended-`ActiveUntil` for Tier-1.
- The `AvatarScaleOperationPriority` enum already has `TestSimulation = 1`, `LiveRedeem = 2`, `SupporterGrowth = 3` — use these or add a tier field to distinguish Tier-1 vs Tier-2 for timer purposes.

### Part B — Support extends active activities

**Feature:** A new per-rule toggle "Extend current activity" on Bits, Subs, Gift Subs, Cash Payment, Power Up, and Supporter Growth rules. When enabled and a support event fires, it extends the timer of whatever timed activity is currently active instead of running its own scale/avatar action. If nothing is active, it logs only.

**Eligible activities (all timed):**
- Avatar Scale effects (restore sequence)
- Avatar Swap timed swaps
- Avatar Roulette timed swaps
- Avatar Sets wardrobe outfits
- Movement redeems (active time)
- Universal Triggers with timed OSC actions

**Per-rule fields:**
- `ExtendCurrentActivity` (bool toggle, default off)
- `ExtendSeconds` (double, "Extend by (seconds)" field, default 0, only meaningful when toggle on)

**Behavior when fired with toggle ON:**
1. Check if any timed activity is currently active across the coordinator.
2. If yes: add `ExtendSeconds` to that activity's remaining timer. Stack additively — multiple support events keep adding.
3. If no: log "Cheer/sub/donation received — no active activity to extend" and perform no scale/avatar action.
4. The support event's own configured scale/avatar action is skipped (extend only).
5. Multiple activities active at once (e.g. a scale effect and a movement redeem both running): extend all of them.
6. Extension on a Tier-2 (pay) scale timer: allowed — support events can extend pay-system windows too.
7. Extension after a Tier-1 highest-seen scale window: the extension adds to current remaining; it does NOT change `highestSeenActiveTimeSeconds`. The highest-seen only grows from new triggers, not from extensions.

**Behavior when fired with toggle OFF:** unchanged — the rule runs its own configured action as today.

**Test mode:** test-mode simulation paths still run the rule's own action (not extend), so the extend behavior only applies to live events.

**Where it changes:**
- `AppSettings` model: add `ExtendCurrentActivity` and `ExtendSeconds` to `BitsRule`, `SubsRule`/`GiftSubsRule`, `CashPaymentRule`, `PowerUpRule`, and the Supporter Growth rule shape.
- `BridgeCoordinator`: add a public `ExtendActiveActivityTimers(TimeSpan extension, string sourceLabel)` method that walks all active timed states (scale restore sequence, avatar swap timers, movement active timers, wardrobe outfit timers, universal trigger timers) and adds `extension` to each one's expiry. Log the extension.
- `BridgeRuntimeConfiguration`: when building a snapshot for a rule with `ExtendCurrentActivity=true`, flag the snapshot so the coordinator knows to take the extend path instead of the normal execution path.
- `SendTestRuleAsync`/`SendTestPowerUpRuleAsync`/`SendTestCashPaymentRuleAsync` paths: unchanged (test mode runs own action).

### Part C — UI & settings surface

**Where the toggle appears:**

- **Bits / Subs / Gift Subs rules:** in the existing Bits+Subs override editor (Avatar Swap manager) and the Avatar Scaling editor for Bits/Subs/Gift Subs scale rules, add a small section with the toggle and extend-seconds field.
- **Cash Payment rules:** in the Cash Payment rule editor, same two fields.
- **Power Up rules:** in the Power Up rule editor, same two fields.
- **Supporter Growth rules (Avatar Scaling):** same two fields. When a supporter event fires and the toggle is on, it extends instead of growing the float.

**Placement:** grouped with the existing action configuration, not buried in advanced settings. Use a collapsible section or a compact card so it doesn't clutter the editor when off.

**Localization:** new keys for all 14 languages:
- `Extend current activity`
- `Extend the current active activity instead of running this rule's action`
- `Extend by (seconds)`
- `No active activity to extend`
- `Extended {0} by {1} seconds` (log format, e.g. "Extended Avatar Scale 'Grow' by 15 seconds")
- `Cheer {0} received — no active activity to extend`
- `Sub received — no active activity to extend`
- `Donation received — no active activity to extend`

**Settings persistence:** `ExtendCurrentActivity` (bool) and `ExtendSeconds` (double) added to the persisted rule shapes in `SettingsStore` with migration for older saves (default `false` / `0.0`).

### Part D — Testing & verification

**Unit tests (VrcTwitchOscBridge.Tests):**

1. Scale timer fairness — Tier-1 highest-seen:
   - Trigger 60s grow, then 30s shrink at t=10 → assert restore timer = 60s from t=10 (expires t=70)
   - Trigger 60s grow, then 90s grow at t=20 → assert highestSeen = 90s, timer = 90s from t=20
   - Trigger 30s grow, let it expire, then 60s grow → assert new window starts fresh, highestSeen = 60s

2. Scale timer fairness — Tier-2 preemption:
   - Trigger 60s Tier-1 grow, then 20s Tier-2 cash pay at t=5 → assert timer = 20s from t=5 (Tier-2 resets)
   - Trigger 20s Tier-2 cash pay, then 60s Tier-1 grow at t=5 → assert Tier-1 cannot shorten Tier-2 window

3. Extend active activity:
   - Activity active with 20s remaining, fire support event with ExtendSeconds=15 → assert new remaining = 35s
   - No activity active, fire support event with toggle on → assert no action runs, log message emitted
   - Two support events fire while one activity active → assert additive (20s + 15s + 15s = 50s)
   - Multiple activities active (scale + movement) → assert both extended
   - Rule with toggle OFF → assert normal action runs, no extension

4. Settings migration:
   - Load older save without ExtendCurrentActivity/ExtendSeconds → assert defaults false/0.0

**Manual verification:**
- Debug build, Twitch reward test mode: trigger a 60s scale, then a 30s scale, observe timer in About page live status doesn't reset below 60s
- Bits simulation in test mode with extend toggle on, activity active → observe extension
- Bits simulation with no activity active → observe log message, no avatar/scale change

**No changes to:** test-mode simulation paths, Twitch API reward sync, OSC address handling, avatar cache, World Guard, self-update.

## Scope

This spec covers:
- Scale restore timer fairness (Part A)
- Support event extension of active activities (Part B)
- UI and localization for the new fields (Part C)
- Tests (Part D)

Out of scope:
- Changing how height is chosen on new trigger (newest wins, already decided)
- Changing test-mode simulation behavior
- Cross-category extension restrictions (all timed activities are eligible)
- A global extension value (per-rule field chosen instead)
