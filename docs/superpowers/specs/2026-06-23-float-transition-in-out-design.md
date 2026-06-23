# Float Transition In/Out Split — Design

**Date:** 2026-06-23
**Status:** Approved
**Target build:** v3.1.9 beta 4
**Active build lane:** beta4

## Goal

Replace the single `FloatTransitionSeconds` field on `TriggerRule` with separate
`FloatTransitionInSeconds` and `FloatTransitionOutSeconds` fields, and apply
smooth eased transitions to every Float avatar parameter redeem path (timed
redeems, instant redeems, and all Float action modes) so the value glides
between current and target instead of snapping.

## Background

Today, `FloatTransitionSeconds` is a single value on `TriggerRule` (default 0,
clamped 0–30). It only applies to **timed** Float avatar parameter redeems
(`DurationSeconds > 0`), used as the same duration for both the ramp-in to
the target and the ramp-out back to the reset value. It is consumed by
`Services/BridgeCoordinator.cs:8113` `SendFloatAvatarParameterValueAsync`,
which steps through 60 updates per second using a `SmoothStep` ease curve.

It is **not** applied to:
- Instant (0-duration) Float redeems.
- Float action modes (`Set`, `Add`, `Subtract`, `AddSubtract`, `Multiply`,
  `Toggle`, `Random`, `Cycle`, `Glitchy`, `Pulse`) when fired as a one-shot.
- Set Trigger multi-param snapshots.
- AvatarChange / AvatarRoulet / PlayerMovement / Avatar Scale.

The editor surface is `UserControls/AvatarSwapRuleEditorControl.xaml:1183-1186`,
a single `TextBox` labeled "Smooth Transition (seconds)", shown only when
`UsesFloatTimedValues` is true.

## Design

### 1. Model & data

**`Models/TriggerRule.cs`**
- Remove `FloatTransitionSeconds` (field, property, `Math.Clamp` validator).
- Add `floatTransitionInSeconds` (default 0, clamped 0–30).
- Add `floatTransitionOutSeconds` (default 0, clamped 0–30).
- Replace `UsesFloatTransition` with two properties:
  - `UsesFloatInTransition => UsesFloatParameter && FloatTransitionInSeconds > 0`
  - `UsesFloatOutTransition => UsesFloatParameter && FloatTransitionOutSeconds > 0`
- Drop the `UsesTimedAction` / `UsesInstantAction` gate from the transition
  visibility — the new properties only require `UsesFloatParameter`, so they
  show on instant redeems too.
- `ParameterType` setter (line 693) and `DurationSeconds` setter (line 1109)
  must raise `UsesFloatInTransition` and `UsesFloatOutTransition` in
  `RaiseActionVisibilityProperties` (line 1949) instead of the old
  `UsesFloatTransition`.
- `FloatValueMode` setter does not need to raise these (FloatValueMode doesn't
  change transition behavior).

**`Services/BridgeRuntimeConfiguration.cs`**
- Replace `FloatTransitionSeconds` in the `TriggerRuleSnapshot` record (line 100)
  with `FloatTransitionInSeconds` and `FloatTransitionOutSeconds`.
- Update the `ToTriggerRuleSnapshot` mapping (around line 956) to populate the
  new fields from `rule.FloatTransitionInSeconds` /
  `rule.FloatTransitionOutSeconds`.

**Save migration** (`Services/SettingsStore.cs` load path)
- On load, if a rule JSON contains `FloatTransitionSeconds` but is missing
  `FloatTransitionInSeconds` / `FloatTransitionOutSeconds`, copy the old value
  into BOTH new fields and drop the old key. Old rules with old=0 stay at
  0/0 (no behavior change). Old rules with old=2.0 become In=2.0, Out=2.0
  (identical visual behavior to pre-change).
- Save path serializes the new fields and never writes the old key.

**`CHANGELOG.txt`**
- Add a beta bullet under the existing `v3.1.9 beta 4` section:
  - "Split the Float Smooth Transition into separate Transition In and
    Transition Out values. The transition now applies to every Float avatar
    parameter redeem (timed, instant, and all action modes), so values
    smoothly glide to and from the target instead of snapping."

**`RELEASE-CHANGE-RECORD.txt`**
- Add the same item under the v3.1.9 working notes so it rolls up cleanly
  into the next stable.

### 2. Runtime behavior

Two paths share one ramp primitive.

**Ramp primitive (unchanged):**
`Services/BridgeCoordinator.cs:8113` `SendFloatAvatarParameterValueAsync`
already takes `(address, startValue, targetValue, transitionSeconds,
cancellationToken)`. It is the single source of truth for the eased glide.
Callers feed it `In` or `Out` depending on direction.

**Path A: Timed Float redeem (modify existing)**
`Services/BridgeCoordinator.cs:7692` `ExecuteTimedFloatAvatarParameterRuleActionAsync`:
- Replace `transitionSeconds = Math.Clamp(rule.FloatTransitionSeconds, 0, 30)`
  with two clamped values: `inSeconds = Clamp(In, 0, 30)` and
  `outSeconds = Clamp(Out, 0, 30)`.
- Total active time = `inSeconds + activeSeconds + outSeconds` (so 0/0
  matches the current "no transition" timing for default rules).
- Ramp-in call uses `inSeconds`. Ramp-out call uses `outSeconds`.
- `ActiveFloatRedeemSessionState` gets `FloatTransitionInSeconds` and
  `FloatTransitionOutSeconds` fields so the deferred completion can still
  read them.
- `ScheduleActiveFloatRedeemCompletion` (line 7834) and its
  `AfterGracePeriod` sibling (line 7895) pass `outSeconds` to the reset ramp.
- `IsTimedFloatAvatarParameterRule` (line 7659) drops the
  `(FloatTransitionSeconds > 0 || ActiveFloatBoostRewardEnabled)` requirement.
  Any timed Float rule can run; the In/Out pair just defaults to 0/0 which
  means snap behavior.

**Path B: Instant Float redeem + action modes (new unified path)**

New helper `ExecuteFloatAvatarParameterWithTransitionAsync(rule,
cancellationToken)` that:
1. Parses the target value. For `Set` mode, target is `ParameterValue`. For
   all other modes, target is `FloatActionDispatch.ComputeNext(rule,
   currentOscValue).nextValue`.
2. Captures the current OSC value via `TryGetCurrentAvatarFloatValueAsync`.
3. If `target == current` OR `FloatTransitionInSeconds <= 0`, send target
   instantly (one OSC packet).
4. Otherwise, call `SendFloatAvatarParameterValueAsync(address, currentValue,
   target, FloatTransitionInSeconds, cancellationToken)`.

This single helper covers:
- Instant redeems (`DurationSeconds == 0`).
- All action modes: `Set`, `Add`, `Subtract`, `AddSubtract`, `Multiply`,
   `Toggle`, `Random`, `Cycle`, `Glitchy`, `Pulse`.

The current dispatch site for non-timed Float action mode paths routes
through this helper. The existing one-shot send functions stay as a fast
path when `In == 0`.

**Glitchy (multi-tick) — smooth every value change**
Glitchy already runs a tick loop on `FloatGlitchyIntervalMs`. Each tick:
1. Read current OSC value (the in-flight ramp may not have finished — that's
   fine, we just sample).
2. Compute the next glitchy value via `FloatActionDispatch.ComputeNext`
   with the sampled value.
3. Ramp from sampled value → new glitchy value over `FloatTransitionInSeconds`
   (`SendFloatAvatarParameterValueAsync`).
4. Update `session.CurrentValue` to the new target.

At active-time end, the existing schedule does a final ramp from
`session.CurrentValue` → reset value over `FloatTransitionOutSeconds`. Because
`CurrentValue` is updated each tick, the release ramp starts from wherever
Glitchy last sent.

### 3. UI changes

**`UserControls/AvatarSwapRuleEditorControl.xaml:1181-1197`**

Restructure the Float Input Mode section:
1. Move the transition subpanel out of the `UsesFloatTimedValues` parent
   (line 1182). The current parent only shows for `DurationSeconds > 0`,
   which would hide the new controls on instant redeems.
2. Replace the single `Smooth Transition (seconds)` `TextBox` (line
   1183-1186) with a `UniformGrid Columns="2"` containing:
   - Left: `Transition In (seconds)` → `TextBox` bound to
     `FloatTransitionInSeconds`
   - Right: `Transition Out (seconds)` → `TextBox` bound to
     `FloatTransitionOutSeconds`
3. The new subpanel visibility becomes `UsesFloatParameter` (same as the
   Float Input Mode parent) but NOT inside the `UsesFloatTimedValues` block.
   Use a fresh `StackPanel` with
   `Visibility="{Binding UsesFloatParameter, Converter={StaticResource BoolToVisibilityConverter}}"`.
4. Order: Transition In/Out sits BEFORE the Active Boost Reward subpanel,
   BEFORE Bits/Subs Add. Keeps the natural read order: how it animates in,
   how it animates out, then optional sub-features.
5. Add a help line under the In/Out pair:
   "0 = snap instantly. Higher values glide the value smoothly to and from
   the target."

**`UserControls/AvatarSwapRuleEditorControl.xaml` Bits/Subs Add / Pulse
sections** — no change. They keep their own visibility gates.

**Localization**
- New `en-US` keys: `Transition In (seconds)`, `Transition Out (seconds)`,
  the help text "0 = snap instantly. Higher values glide the value smoothly
  to and from the target."
- Drop the old `Smooth Transition (seconds)` key from `en-US` after the
  editor is updated. If the key is referenced anywhere else, leave it; this
  change only removes the one editor binding.
- The `loc:Translate` markup wraps the new labels the same way the old one
  did. All non-English `.extra.json` files need translations for the new
  keys before the next non-test build.

**`ViewModels/MainWindowViewModel.cs:200`**
- Replace `nameof(TriggerRule.FloatTransitionSeconds)` with both
  `nameof(TriggerRule.FloatTransitionInSeconds)` and
  `nameof(TriggerRule.FloatTransitionOutSeconds)` so property-change
  broadcasts fire for both new fields.

### 4. Edge cases, test plan, open questions

#### Edge cases

1. **Both 0** → identical to current "no transition" behavior. Default for
   new and migrated rules.
2. **In=0, Out>0** (or vice versa) → asymmetric ramp. Allowed, e.g. snap in,
   glide out for a quick attack.
3. **Target equals current** → skip the ramp, send one packet. Optimization
   to avoid wasted OSC sends during the ramp.
4. **Current OSC read fails** → fall back to instant send, same as current
   behavior.
5. **Multiple redeems fire on the same rule** → previous session's
   `CompletionCancellation` is canceled by the new session. The new
   instant/action-mode path also calls the same cancellation cleanup.
6. **`session.CurrentValue` tracking** → updated after each successful ramp
   so the release ramp in `ScheduleActiveFloatRedeemCompletion` (and the
   Glitchy release) starts from the actual current OSC value, not a stale
   snapshot.
7. **World-transition grace period** → unchanged.
   `ScheduleActiveFloatRedeemCompletionAfterGracePeriod` still defers the
   reset if the avatar is mid-swap.
8. **FloatClampMode during ramp** → intermediate ramp values can briefly
   leave `[0,1]`; the existing `SendSingleFloatAvatarParameterValueAsync`
   already clamps to `[0,1]` before sending, so no change.
9. **Active Float Boost cap** → target is already capped before the ramp, so
   the glide respects the cap automatically.
10. **Test mode** → the test path goes through the same helper, so test
    triggers show the transition in logs and VRChat.
11. **Pulse mode + In/Out set** → In/Out are no-ops for Pulse (Pulse is
    one-shot, not held). Documented as deliberate. UI shows the fields
    anyway so the user isn't surprised by hidden controls.
12. **Bits/Subs Add (`SupporterFloatAdd`)** → unchanged. Out of scope.

#### Test plan

**Build**
- `dotnet build VrcTwitchOscBridge/VrcTwitchOscBridge.csproj --no-restore`
  clean.

**Localization audit**
- Run the existing `LocalizationAudit` project. Verify new keys are present
  in `en-US` and all `.extra.json` files have non-empty translations. No
  placeholder leftovers. The world-guard message key stays present in every
  `.extra.json` file.

**Manual scenarios** (all under a real VRChat session + a test avatar with a
float parameter)
1. Instant Set with In=1.0 → fire, value glides from current → target over
   1s.
2. Instant Set with In=0 → fire, value snaps (current behavior).
3. Timed Set with In=1, Out=2, Active=5 → fire, glides in 1s, holds 5s,
   glides out 2s. Total 8s. Log shows the correct ramp boundaries.
4. Timed Set with In=0, Out=2 → snaps in, holds, glides out.
5. Add mode with In=0.5 → fire twice, each addition glides to the new value
   over 0.5s (e.g., 0.3 → 0.4 glides, then 0.4 → 0.5 glides).
6. Glitchy with In=0.2, glitch interval=200 → rapid ticks, each new random
   value glides over 0.2s. At active end, glides from last sampled value
   to reset.
7. Target equals current → confirm only one OSC packet is sent (no ramp).
8. Migrate old save → load a saved rule that has `FloatTransitionSeconds=2.0`,
   confirm it becomes `In=2.0, Out=2.0` and behaves identically to
   pre-change.
9. New save round-trip → save a rule with `In=1.0, Out=2.0`, reload,
   confirm values persist.

**Regression**
- Existing Float action mode behaviors (no transition) still work.
- Existing Float active boost reward still works.
- Set Trigger, AvatarChange, AvatarRoulet, PlayerMovement, Avatar Scale
  untouched.
- Bool/Int avatar parameters untouched.

#### Open questions (resolved)

1. **Should Pulse mode hide the In/Out fields?** — **No.** Keep them
   visible, no-op at runtime. Simpler UI logic, less conditional
   visibility, less surprise.
2. **Default values for In/Out on a brand-new rule?** — **0/0** (off,
   backward compatible). No surprise for streamers who don't want a
   transition.
3. **Should we add help text under the In/Out fields?** — **Yes.** One
   short line: "0 = snap instantly. Higher values glide the value
   smoothly to and from the target."

## Out of scope

- Pulse mode transition wrap (Pulse stays one-shot + one-shot reset).
- Bits/Subs Add (`SupporterFloatAdd`) transition wrap.
- Set Trigger multi-param snapshot transitions.
- AvatarChange / AvatarRoulet / PlayerMovement / Avatar Scale transitions.
- Per-mode transition overrides (one In/Out pair is enough for this
  release).
- Separate per-mode defaults (e.g., shorter transition for Toggle, longer
  for Glitchy).

## Implementation notes

- After implementation, run a code review pass with GLM 5.2 to catch
  errors and confirm the design was followed before merging to the active
  build lane. Apply any corrections GLM 5.2 flags before considering the
  implementation complete.
- This change ships under v3.1.9 beta 4. If beta 4 is already released by
  the time the code lands, add a new `v3.1.9 beta 5` entry to
  `CHANGELOG.txt` instead.
