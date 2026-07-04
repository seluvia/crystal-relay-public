# Supporter Growth Require Cheer Keyword Design Spec

**Date:** 2026-07-03
**Scope:** Per-rule toggle on Supporter Growth that requires a cheer keyword (`grow` or `shrink`) for bits to change height. Bits without a keyword still add paid time. Touches the model, snapshot/persistence layer, runtime matcher/executor, UI, and localization.

## Problem

Today, Supporter Growth bits always change height when the bits amount matches a configured Bits Growth Range. The grow/shrink cheer keywords are optional and only choose direction. A streamer who wants bits to be time-only support unless the viewer explicitly says "grow" or "shrink" has no way to enforce that.

## Goal

Add a per-rule checkbox `SupporterGrowthRequireCheerKeyword` (default `false`). When enabled:
- A bits event whose cheer message contains `grow` → height grows + paid time added (existing behavior).
- A bits event whose cheer message contains `shrink` → height shrinks + paid time added (existing behavior).
- A bits event whose cheer message contains **both** `grow` and `shrink` → rejected entirely (existing behavior, unchanged).
- A bits event whose cheer message contains **neither** keyword → **no height change, but paid time is still added**.
- Subs, resubs, and gift subs are unaffected — they always add height + time as configured.

When disabled (default): existing behavior unchanged. Bits change height without needing a keyword.

## Non-Goals

- No new global setting. This is per-rule only.
- No change to sub/gift-sub height behavior.
- No change to the Bits Growth Range matching logic — ranges still decide how much height a matching bits event adds. The toggle only gates whether height is applied at all when no keyword is present.
- No change to the test-mode path — test mode always applies the tier-1 height regardless of keywords (existing behavior).

## Design

### Model — `Models\AvatarScaleRule.cs`

Add a new bool property `SupporterGrowthRequireCheerKeyword` following the existing `SetAndRaiseSupporterGrowth` pattern. Default `false`. Place it near `SupporterGrowthAllowRewardScaleOverlay` (line ~858).

### Snapshot — `Services\BridgeRuntimeConfiguration.cs`

1. Add `bool SupporterGrowthRequireCheerKeyword` to the `AvatarScaleRuleSnapshot` record (after `SupporterGrowthAllowRewardScaleOverlay`, line ~243).
2. Add mapping `rule.SupporterGrowthRequireCheerKeyword` in `ToAvatarScaleRuleSnapshot` (after line ~1353).

### Persistence — `Services\SettingsStore.cs`

1. Add `public bool SupporterGrowthRequireCheerKeyword { get; set; }` to `PersistedAvatarScaleRule` (near line ~3827).
2. Add `SupporterGrowthRequireCheerKeyword = rule.SupporterGrowthRequireCheerKeyword` to the model→DTO mapping (near line ~2010).
3. Add `SupporterGrowthRequireCheerKeyword = rule.SupporterGrowthRequireCheerKeyword` to the DTO→model mapping (near line ~2116). No fallback needed — default `false` is safe.

### Runtime — `Services\BridgeCoordinator.cs`

Two changes:

**1. `TryResolveSupporterGrowthBitsHeightDirection` (line ~6039):** Add an `out bool anyKeywordMatched` parameter. Set it to `growMatched || shrinkMatched` before the both-matched early return. Update all three call sites.

**2. `SupporterGrowthEventMatches` (line ~6579):** After the existing both-keywords check, if `rule.SupporterGrowthRequireCheerKeyword` is true and the event is bits and `!anyKeywordMatched`, return `GetSupporterGrowthAddedTimeSeconds(rule, incomingEvent, isTest: false) > 0` so the rule still fires for time-only.

**3. `ExecuteSupporterGrowthAvatarScaleRuleAsync` (line ~5650):** After resolving the direction, compute `keywordRequiredButMissing = rule.SupporterGrowthRequireCheerKeyword && !isTest && incomingEvent.TriggerType == Bits && !anyKeywordMatched`. When true, set `addedHeight = 0` instead of calling `GetSupporterGrowthHeightAdd`. Change the `addedHeight == 0` skip to `if (addedHeight == 0 && !keywordRequiredButMissing) skip` so the time-only path proceeds. Update the success log line to reflect time-only when `keywordRequiredButMissing` is true.

### UI — `AvatarScalingManagerWindow.xaml`

In the Supporter Growth Cheer Keywords section (after the Grow/Shrink keyword grid, before "Subscription Growth"), add:
- CheckBox: `IsChecked="{Binding SupporterGrowthRequireCheerKeyword, UpdateSourceTrigger=PropertyChanged}"` with label "Require grow or shrink keyword for height changes"
- Helper TextBlock (muted, wrapping, indented `Margin="26,6,0,0"`): "When enabled, bits only change height if the cheer message says your grow or shrink keyword. Bits without a keyword still add paid time, but do not grow or shrink the avatar."

### Localization — 14 `.extra.json` files

Two new keys in `en-US.extra.json` and all 13 non-English files:
- `"Require grow or shrink keyword for height changes"`
- `"When enabled, bits only change height if the cheer message says your grow or shrink keyword. Bits without a keyword still add paid time, but do not grow or shrink the avatar."`

Translations follow the project's Localization Translation Quality Rules: informal register, brand/technical terms (`Bits`) in English, preserve exact wording of `grow`/`shrink` as the keyword examples.

## Verification

1. `dotnet build VrcTwitchOscBridge\VrcTwitchOscBridge.csproj --no-restore` — succeeds.
2. Localization audit — no new missing keys.
3. Manual debug-build test:
   - Create a Supporter Growth rule. Add a bits range (e.g., 1-1000 → 0.1m). Enable "Require grow or shrink keyword".
   - Test with a bits event whose message has no keyword → height unchanged, paid time increases.
   - Test with a bits event whose message says "grow" → height grows + paid time increases.
   - Test with a bits event whose message says "shrink" → height shrinks + paid time increases.
   - Disable the toggle. Test with a bits event with no keyword → height grows (existing behavior restored).
