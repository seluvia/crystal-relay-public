# Hide Avatar Set Float Reward on Limit Reached

**Date:** 2026-06-23
**Status:** Approved design, pending implementation plan
**Scope:** Avatar Sets → float action mode rewards

## Problem

When a float parameter reward uses a cumulative action mode (Add, Subtract, AddSubtract, Multiply) with a clamp, viewers keep redeeming it after the value has already reached the clamp boundary. Each redeem past the limit does nothing useful but still costs channel points and clutters the reward queue.

## Solution

Add two opt-in checkboxes per Avatar Set float rule: "Hide reward at maximum float" and "Hide reward at minimum float". When enabled and the float value reaches the configured limit, Crystal Relay hides the Twitch channel-point reward (sets `isEnabled = false` via the existing managed reward sync pipeline). The reward re-appears when the timed redeem's Active Time expires and the value resets.

## Confirmed Decisions

| Decision | Choice |
|---|---|
| Re-show trigger | Active Time end only (option only available when `DurationSeconds > 0`) |
| Applicable float modes | Cumulative only: `Add`, `Subtract`, `AddSubtract`, `Multiply` |
| Max vs min | Both, as two separate checkboxes (mirrors Avatar Scaling) |
| Default | Opt-in (both `false` by default for new and existing rules) |
| Scope | Avatar Sets editor only (`AvatarSetsManagerWindow`); Power-up fixed-float-add unchanged |
| Approach | Mirror the proven Avatar Scaling `HideRewardWhenMaximumHeightReached` pattern |

## Reference Implementations in Codebase

This feature generalizes two existing patterns:

1. **Avatar Scaling limit hide** — `AvatarScaleRule.HideRewardWhenMinimumHeightReached` / `HideRewardWhenMaximumHeightReached` (`Models\AvatarScaleRule.cs:605-615`, default `true`). Limit detection with hysteresis at `MainWindowViewModel.cs:13929-13988`. State-flip cache `avatarScaleLimitInactiveStateByRuleId` (537). Sync trigger `HandleAvatarScaleStatusChanged` (8960-9023) queues `ManagedRewardSyncReason.AvatarScaleStatus` only on actual flips. Desired-enabled gate `isHiddenAtRelativeLimit` in `CreateManagedRewardTargetForAvatarScaleRule` (12632, 12653-12654).

2. **Active Float Boost max-reached** — `ActiveFloatRedeemSessionState.BoostMaximumReached` (`BridgeCoordinator.cs:18774`). Detection `IsAtOrAboveActiveFloatBoostMaximum` (7769). Public getter `GetActiveFloatBoostMaximumReachedRuleIds` (388-396). Desired-enabled gate in `CreateManagedRewardTargetForActiveFloatBoostReward` (`MainWindowViewModel.cs:12222-12277`).

## Architecture

### Section 1: Data Model

Add two opt-in bool properties to `TriggerRule` (`Models\TriggerRule.cs`, near line 110 next to `FloatClampMode`):

```csharp
private bool hideRewardWhenFloatMaxReached;  // default false
private bool hideRewardWhenFloatMinReached;  // default false
public bool HideRewardWhenFloatMaxReached { get => ...; set => SetProperty(ref ..., value); }
public bool HideRewardWhenFloatMinReached { get => ...; set => SetProperty(ref ..., value); }
```

Add a computed visibility helper `UsesFloatHideOnLimit` (mirroring `UsesFloatClampMode` at line 1764):

```csharp
[JsonIgnore]
public bool UsesFloatHideOnLimit
    => UsesFloatActionMode
       && RewardSyncMode == TwitchRewardSyncMode.CreateOrManage
       && DurationSeconds > 0
       && (FloatActionMode is Add or Subtract or AddSubtract or Multiply);
```

This gates the UI to only show the checkboxes when the rule is a managed, timed, cumulative-mode float reward.

No migration needed: older saves load `false` for both new fields (safe default), matching how `ActiveFloatBoostRewardEnabled` and `SupporterFloatAddEnabled` were added without a version bump.

### Section 2: Runtime State & Limit Detection

Add two mutable bools to `ActiveFloatRedeemSessionState` (`BridgeCoordinator.cs:18730`, next to `BoostMaximumReached` at 18774):

```csharp
public bool FloatMaxReached;
public bool FloatMinReached;
```

**Limit detection** — a new private helper in `BridgeCoordinator` mirroring `IsAtOrAboveActiveFloatBoostMaximum` (7769):

```csharp
private const double FloatLimitTolerance = 0.000001d;
private const double FloatLimitReleaseTolerance = 0.0001d;  // hysteresis

private (bool maxReached, bool minReached) ComputeFloatLimitState(
    TriggerRule rule, double currentValue)
{
    var (lower, upper) = ResolveFloatLimits(rule);
    bool atMax = currentValue >= upper - FloatLimitTolerance;
    bool atMin = currentValue <= lower + FloatLimitTolerance;
    // hysteresis: once at limit, require value to move past release tolerance before clearing
    return (atMax, atMin);
}
```

`ResolveFloatLimits` returns `(0,1)` for `ZeroToOne`, `(FloatRangeMin, FloatRangeMax)` for `MinToMax`, `(double.MinValue, double.MaxValue)` for `None` (None ⇒ never reaches a limit ⇒ both flags stay false).

**When to compute:**
- At session creation (`ExecuteTimedFloatAvatarParameterRuleActionAsync`, ~7803–7816): compute after `targetValue` is known, store on session. Skip computation entirely if `!rule.HideRewardWhenFloatMaxReached && !rule.HideRewardWhenFloatMinReached` (zero-cost when feature off).
- On boost application (`ApplyActiveFloatBoostRewardAsync`, ~8079–8083): recompute after `session.CurrentValue` updates.
- On supporter-float-add (`BridgeCoordinator.cs:8724` area): recompute after the clamp.

**Public accessor** mirroring `GetActiveFloatBoostMaximumReachedRuleIds` (388–396):

```csharp
public IReadOnlyList<Guid> GetActiveFloatLimitReachedRuleIds()
    => activeFloatRedeemSessions.Values
        .Where(s => s.FloatMaxReached || s.FloatMinReached)
        .Select(s => s.Rule.Id).ToList();
```

**Re-show on Active Time end:** `FinishActiveFloatRedeemSession` (8121–8156) already fires `ManagedRewardAvailabilityChanged` — which is a documented no-op in the VM (19756). The new feature adds an explicit call there to raise the new `FloatLimitStatusChanged` event (Section 3) so the VM detects the flip from "at limit" → "session gone" and queues a re-show sync. Cooldown-expire path (`ScheduleCooldownStateNotification`, 11710) already fires the same events — same explicit raise there.

**Hysteresis:** copy the Avatar Scaling pattern (`AvatarScaleLimitHeightToleranceMeters` vs `AvatarScaleLimitHeightReleaseToleranceMeters`, 13957–13981). Once `FloatMaxReached` is true, keep it true until value drops below `upper - FloatLimitReleaseTolerance`. Prevents flicker when a value sits exactly at 1.0 and a re-send ticks it to 0.999999.

**Mode applicability:** the session only sets the flags when `rule.FloatActionMode` is `Add`, `Subtract`, `AddSubtract`, or `Multiply` (cumulative). For `Set`/`Random`/`Toggle`/`Cycle`/`Glitchy`/`Pulse` the value doesn't accumulate, so the flags stay false even if both checkboxes were on.

### Section 3: Sync Pipeline Integration

Mirrors Avatar Scaling's `AvatarScaleStatus` flow exactly.

**New sync reason** (`ManagedRewardSyncReason` enum, ~11089):

```csharp
FloatLimitStatus,  // add after AvatarScaleStatus
```

Treat it as a passive reason in the fingerprint-skip list (13131–13138) so identical desired state doesn't trigger a PATCH.

**New event** on `BridgeCoordinator` (mirroring `AvatarScaleMasterRewardUnlockStateChanged` at line 317):

```csharp
public event Action? FloatLimitStatusChanged;
```

Raised only when a rule's `(FloatMaxReached, FloatMinReached)` state actually flips, never on every value tick. Raised from: session creation, boost application, supporter-float-add, `FinishActiveFloatRedeemSession`, and `ScheduleCooldownStateNotification`'s expire callback — the same points where Section 2 recomputes the flags.

**VM handler** mirroring `HandleAvatarScaleStatusChanged` (8960–9023):

```csharp
private readonly Dictionary<Guid, (bool max, bool min)> floatLimitStateByRuleId = new();

private void HandleFloatLimitStatusChanged()
{
    var current = bridgeCoordinator.GetActiveFloatLimitReachedRuleIds()
        .ToDictionary(id => id);
    // for each rule with an active float session, compute (max, min)
    // compare against floatLimitStateByRuleId
    // if any flipped => QueueManagedRewardSync(debounceMs, ManagedRewardSyncReason.FloatLimitStatus)
    // update floatLimitStateByRuleId
}
```

Debounce ~750ms (reuse `AvatarScaleLimitRewardSyncDebounce` value, 95).

**Desired-enabled gate** — in the plain-rule target builder (the function ending at `MainWindowViewModel.cs:12131`, around 12107–12113), alongside the existing `isActiveFloatBoostParent` gating:

```csharp
var floatLimitReached = activeFloatLimitReachedRuleIds.Contains(rule.Id)
    && (rule.HideRewardWhenFloatMaxReached || rule.HideRewardWhenFloatMinReached);
// in desiredEnabled:
    && !floatLimitReached
// in deleteWhenInactive gate:
    && !floatLimitReached   // protect from deletion while hidden at limit
// in protectFromCapReclaim:
    && !floatLimitReached
```

This exactly mirrors `isHiddenAtRelativeLimit` in `CreateManagedRewardTargetForAvatarScaleRule` (12632, 12653–12654).

**Pass `activeFloatLimitReachedRuleIds` in** at the sync assembly site (~12913), next to `activeFloatBoostMaximumReachedRuleIds`.

**Linked-reward guard:** the target builder only applies `!floatLimitReached` when `rule.RewardSyncMode == TwitchRewardSyncMode.CreateOrManage` (matches Avatar Scaling guard at 13937). Linked-existing rewards stay listen-only.

**Composition with Active Float Boost:** when a parent rule has `ActiveFloatBoostRewardEnabled`, the parent is already hidden while active (12113). The new `floatLimitReached` flag for the parent's own value composes cleanly — both gate `desiredEnabled`, and if either is true the reward stays hidden. The boost child's `BoostMaximumReached` path is untouched and still works independently.

**Logging:** throttled via `AppendThrottledLog` (mirroring `LogAvatarScaleLimitVisibilityChange`, 9025–9037), user-facing wording like `"Avatar set reward '...' is hidden on Twitch because its float value reached the configured maximum."` Never logs tokens/secrets.

**Twitch API safety guarantees (per AGENTS.md):**
- Only one sync queued per actual state flip, not per value update
- Passive-reason fingerprint skip prevents PATCHes when desired state is unchanged
- Linked rewards never mutated
- Backoff / Retry-After respected via existing `IsManagedRewardApiBackoffActive` gate

### Section 4: UI

Mirror the Avatar Scaling checkbox layout (`MainWindow.xaml:6506-6521`).

**AvatarSetsManagerWindow.xaml** — add a new `StackPanel` inside the existing float-mode container (after the clamp `ComboBox` block ending at ~1648):

```xml
<StackPanel Visibility="{Binding UsesFloatHideOnLimit, Converter={StaticResource BoolToVisibilityConverter}}"
            Margin="0,8,0,0">
    <CheckBox Content="{loc:Translate 'Hide reward at maximum float'}"
              IsChecked="{Binding HideRewardWhenFloatMaxReached, Mode=TwoWay}" />
    <CheckBox Content="{loc:Translate 'Hide reward at minimum float'}"
              IsChecked="{Binding HideRewardWhenFloatMinReached, Mode=TwoWay}"
              Margin="0,4,0,0" />
</StackPanel>
```

**Visibility gating** — the `UsesFloatHideOnLimit` computed property (Section 1) handles everything: only shows when the rule is a managed (`CreateOrManage`), timed (`DurationSeconds > 0`), cumulative-mode (`Add`/`Subtract`/`AddSubtract`/`Multiply`) float reward. No extra MultiDataTrigger needed in XAML — the binding does the work.

**Placement rationale:** below the clamp `ComboBox` because hide-on-limit depends on the clamp being meaningful. If clamp is `None`, `UsesFloatHideOnLimit` could still be true (the mode is cumulative) but the flags will never fire at runtime (Section 2's `ResolveFloatLimits` returns infinite bounds for `None`). The checkboxes are harmless when clamp is `None`, and the streamer's intent is preserved.

**No `MainWindow.xaml` changes needed** — Avatar Sets rules are edited in `AvatarSetsManagerWindow`, not the main window.

**Style consistency:** plain `CheckBox` controls inherit the theme from `AvatarSetsManagerWindow`'s existing theme resources. No new style needed — matches how the Avatar Scaling checkboxes in `MainWindow.xaml:6516-6520` are plain `CheckBox`es.

### Section 5: Settings Serialization & Localization

**Settings (`Services\SettingsStore.cs`):**
- `ToPersistedTriggerRule` (~1063): add both bools to the `PersistedTriggerRule` copy block.
- `ToRule` (~1369): read both back; missing fields default `false` automatically.
- `PersistedTriggerRule` DTO (~3362): add two auto-properties `public bool HideRewardWhenFloatMaxReached { get; set; }` and `public bool HideRewardWhenFloatMinReached { get; set; }` — both default `false`.

No migration needed — older saves without these fields load `false`, matching how `ActiveFloatBoostRewardEnabled`, `SupporterFloatAddEnabled`, etc. were added without a version bump.

**Localization:**
Add two new keys to **all 15** language files (en-US plus 14 translated `.extra.json` files), mirroring the existing `"Hide reward at maximum height"` / `"Hide reward at minimum height"` entries (`en-US.extra.json:466-467`):

| Key | en-US value |
|---|---|
| `Hide reward at maximum float` | `Hide reward at maximum float` |
| `Hide reward at minimum float` | `Hide reward at minimum float` |

Per AGENTS.md translation quality rules:
- Use informal/friendly register: `du` (de-DE), `tú` (es-ES), `tu` (fr-FR), informal equivalents elsewhere
- Keep brand/technical terms in English: `Crystal Relay`, `Bits`, `OSC`, `VRChat`, `Twitch`
- Preserve format placeholders exactly (none here — plain strings)

Translation drafts:

| Lang | Max | Min |
|---|---|---|
| de-DE | Belohnung bei maximalem Float-Wert verbergen | Belohnung bei minimalem Float-Wert verbergen |
| es-ES | Ocultar recompensa al llegar al valor float máximo | Ocultar recompensa al llegar al valor float mínimo |
| fr-FR | Masquer la récompense à la valeur float maximale | Masquer la récompense à la valeur float minimale |
| it-IT | Nascondi ricompensa al valore float massimo | Nascondi ricompensa al valore float minimo |
| ja-JP | float値が最大に達したらリワードを非表示 | float値が最小に達したらリワードを非表示 |
| ko-KR | float 값이 최대치에 도달하면 보상 숨기기 | float 값이 최소치에 도달하면 보상 숨기기 |
| pl-PL | Ukryj nagrodę przy maksymalnej wartości float | Ukryj nagrodę przy minimalnej wartości float |
| pt-BR | Ocultar recompensa no valor float máximo | Ocultar recompensa no valor float mínimo |
| ru-RU | Скрывать награду при максимальном значении float | Скрывать награду при минимальном значении float |
| sv-SE | Dölj belöning vid maximalt float-värde | Dölj belöning vid minimum float-värde |
| th-TH | ซ่อนรางวัลเมื่อ float ถึงค่าสูงสุด | ซ่อนรางวัลเมื่อ float ถึงค่าต่ำสุด |
| zh-CN | float 达到最大值时隐藏奖励 | float 达到最小值时隐藏奖励 |
| zh-TW | float 達到最大值時隱藏獎勵 | float 達到最小值時隱藏獎勵 |

**Audit:** run the localization audit per AGENTS.md after adding the keys — verify key coverage, placeholder integrity, and no empty values across all 15 files.

### Section 6: Verification & Testing

**Unit tests** (in `VrcTwitchOscBridge.Tests`):

1. **`TriggerRuleFloatModePersistenceTests.cs`** — extend the existing two tests:
   - `ToRule_MissingNewFields_AppliesSafeDefaults`: assert `HideRewardWhenFloatMaxReached == false` and `HideRewardWhenFloatMinReached == false` when missing from `PersistedTriggerRule` (mirrors line 11-40).
   - `RoundTrip_AllNewFieldsPreserved`: set both bools `true` and assert they survive the round-trip (mirrors line 42+).

2. **`TriggerRuleFloatModeFieldsTests.cs`** — add tests for the new `UsesFloatHideOnLimit` computed property:
   - True when: `CreateOrManage` + `DurationSeconds > 0` + cumulative mode (`Add`/`Subtract`/`AddSubtract`/`Multiply`)
   - False when: `LinkExisting`, `DurationSeconds == 0`, non-cumulative mode (`Set`/`Random`/`Toggle`/`Cycle`/`Glitchy`/`Pulse`)

3. **New `FloatLimitDetectionTests.cs`** — test the `ComputeFloatLimitState` helper:
   - `ZeroToOne` clamp: value `1.0` → `maxReached=true`; value `0.0` → `minReached=true`; value `0.5` → both false
   - `MinToMax` clamp: value at `FloatRangeMax` → max; value at `FloatRangeMin` → min
   - `None` clamp: any value → both false (infinite bounds)
   - Hysteresis: once `maxReached=true` at `1.0`, value `0.99999` stays `maxReached=true` until below `1.0 - FloatLimitReleaseTolerance`
   - Mode gate: cumulative modes compute flags; non-cumulative modes always return `(false, false)`

4. **`AvatarSetsManagerWindowXamlTests.cs`** — extend to verify the new checkboxes are bound to the right properties (mirrors existing XAML test pattern in that file).

**Build verification:**
```
dotnet build "E:\!!!Program to work on\Proper Crystal Relay\VrcTwitchOscBridge\VrcTwitchOscBridge.csproj" --no-restore
dotnet build "E:\!!!Program to work on\Proper Crystal Relay\VrcTwitchOscBridge.Tests\VrcTwitchOscBridge.Tests.csproj" --no-restore
dotnet test "E:\!!!Program to work on\Proper Crystal Relay\VrcTwitchOscBridge.Tests\VrcTwitchOscBridge.Tests.csproj" --no-restore
```

**Localization audit:** run the audit project per AGENTS.md after adding the 30 new keys (2 keys × 15 languages). Verify key coverage, placeholder integrity, no empty values.

**Runtime smoke test (manual):** after a successful debug build, launch via `Launch-Crystal-Relay-Debug.bat`, create an Avatar Set rule with `Add` mode, `FloatAddAmount = 0.3`, `FloatClampMode = ZeroToOne`, `DurationSeconds = 30`, enable `HideRewardWhenFloatMaxReached`. Verify the reward hides on Twitch when value reaches 1.0 and re-shows when Active Time expires.

### Section 7: Edge Cases & Interaction Summary

**Mode applicability gate:**
- `Set`/`Random`/`Toggle`/`Cycle`/`Glitchy`/`Pulse` — flags stay `(false, false)` regardless of checkbox state. Value doesn't accumulate, so "reached max" is meaningless.
- `Add`/`Subtract`/`AddSubtract`/`Multiply` — flags compute against clamp bounds.

**Clamp mode interaction:**
- `FloatClampMode.None` — `ResolveFloatLimits` returns `(MinValue, MaxValue)`. A cumulative mode like `Multiply` could theoretically overflow to infinity, but OSC float precision caps at ~1.0 and existing dispatch already normalizes via `NormalizeToOscPrecision`. In practice, `None` + cumulative is rare and the flags will simply never fire (value never reaches `double.MaxValue - tolerance`). Acceptable.
- `ZeroToOne` (the default) — the common case. Max = 1.0, Min = 0.0.
- `MinToMax` — uses `FloatRangeMin`/`FloatRangeMax`.

**Interaction with Active Float Boost child reward:**
- Parent rule hidden while boost active (existing, 12113) AND parent's own `floatLimitReached` may also be true. Both gate `desiredEnabled` — clean composition, reward stays hidden if either is true.
- Boost child's `BoostMaximumReached` path is **untouched** — still hides the boost child when its add value hits the boost max. Independent of parent's float-limit state.
- When boost application moves the parent value to the limit, both `BoostMaximumReached` (child) and `FloatMaxReached` (parent) may flip. Each raises its own event; the VM's state-flip cache ensures one sync, not two (both queue `FloatLimitStatus` and the existing boost path queues its own; the fingerprint-skip deduplicates the desired state).

**Interaction with Reward Fire Sale:**
- Fire Sale reprices `CreateOrManage` rewards but doesn't touch `isEnabled`. A fire-sale-active reward that also hits its float limit will hide normally — the `desiredEnabled = false` from `floatLimitReached` composes with the fire sale's repricing. When the sale ends and the float is still at limit, the reward stays hidden. When the float leaves the limit, the re-show sync restores it at the sale-ended price. Clean.

**Interaction with Avatar Scaling's avatar-change blocker:**
- Independent. Avatar Scaling's "Prevent avatar-change rewards while scaling is active" blocks Avatar Change/Roulette rewards only. Float parameter rewards in Avatar Sets are unaffected.

**Interaction with cooldown:**
- If a rule has both `DurationSeconds` and `CooldownSeconds`, the sequence is: trigger → active session (value may reach limit → hide) → Active Time end (reset value → `FloatLimitStatusChanged` event → re-show sync queued) → cooldown begins → cooldown ends (`ScheduleCooldownStateNotification` fires → reward stays enabled since not at limit). The re-show happens at Active Time end, not cooldown end — matching the confirmed choice.

**Interaction with `TemporarilyDisabledRuleIds` (disable pairing):**
- Both `temporarilyDisabledRuleIds.Contains(rule.Id)` (existing, 12112) and `floatLimitReached` (new) gate `desiredEnabled`. If a rule is temp-disabled AND at limit, it stays hidden. When temp-disable lifts, the limit state is re-evaluated — if still at limit, stays hidden; if not, re-shows. Clean composition.

**Avatar change mid-session:**
- `ScheduleActiveFloatRedeemCompletion` has an avatar-change grace-period deferral (7973–8029). If the avatar changes while a float session is active, the session completion is deferred. The `FloatMaxReached`/`FloatMinReached` flags persist on the session through the deferral — the reward stays hidden until the session truly finishes and the reset value is sent. Then the re-show fires.

**Feature off (both checkboxes unchecked):**
- `ComputeFloatLimitState` is never called (Section 2 zero-cost gate). `GetActiveFloatLimitReachedRuleIds()` returns empty. `floatLimitReached` is always false in the target builder. Zero behavior change — exactly today's behavior. This is the safety guarantee for existing streamers.

**Linked-existing rewards:**
- `UsesFloatHideOnLimit` returns false for `LinkExisting` rules. UI checkboxes don't show. Target builder's `floatLimitReached` is gated on `CreateOrManage`. Linked rewards stay listen-only per AGENTS.md.

## Files Touched (Summary)

| File | Change |
|---|---|
| `VrcTwitchOscBridge\Models\TriggerRule.cs` | Add `HideRewardWhenFloatMaxReached`, `HideRewardWhenFloatMinReached`, `UsesFloatHideOnLimit` |
| `VrcTwitchOscBridge\Services\BridgeCoordinator.cs` | Add `FloatMaxReached`/`FloatMinReached` to session state, `ComputeFloatLimitState`, `ResolveFloatLimits`, `GetActiveFloatLimitReachedRuleIds`, `FloatLimitStatusChanged` event, raise calls at session create/boost/supporter-add/finish/cooldown-expire |
| `VrcTwitchOscBridge\ViewModels\MainWindowViewModel.cs` | Add `FloatLimitStatus` sync reason, `floatLimitStateByRuleId` cache, `HandleFloatLimitStatusChanged` handler, `floatLimitReached` gate in plain-rule target builder, pass `activeFloatLimitReachedRuleIds` into sync assembly, throttled logging |
| `VrcTwitchOscBridge\AvatarSetsManagerWindow.xaml` | Add `UsesFloatHideOnLimit`-gated `StackPanel` with two `CheckBox`es |
| `VrcTwitchOscBridge\Services\SettingsStore.cs` | Add two bools to `PersistedTriggerRule`, `ToPersistedTriggerRule`, `ToRule` |
| `VrcTwitchOscBridge\Resources\Localization\*.json` (15 files) | Add `Hide reward at maximum float` / `Hide reward at minimum float` |
| `VrcTwitchOscBridge.Tests\TriggerRuleFloatModePersistenceTests.cs` | Extend default + round-trip tests |
| `VrcTwitchOscBridge.Tests\TriggerRuleFloatModeFieldsTests.cs` | Add `UsesFloatHideOnLimit` tests |
| `VrcTwitchOscBridge.Tests\FloatLimitDetectionTests.cs` (new) | `ComputeFloatLimitState` tests |
| `VrcTwitchOscBridge.Tests\AvatarSetsManagerWindowXamlTests.cs` | Extend for new checkboxes |

## Non-Goals

- Power-up fixed-float-add hide-on-max (could be a future follow-up; explicitly out of scope per confirmed decision).
- Supporter-float-add hide-on-max (same).
- Re-show on cooldown end (explicitly declined; re-show is Active Time end only).
- Non-cumulative float modes (`Set`/`Random`/`Toggle`/`Cycle`/`Glitchy`/`Pulse`).
- Instant (non-timed) float rules.
