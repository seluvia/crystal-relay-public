# Supporter Growth UI Rework Design Spec

**Date:** 2026-06-09
**Scope:** Avatar Scaling Supporter Growth section UI only — no other sections affected
**Goal:** Reorganize the flat 15+ field panel into collapsible sections with summaries, improve field labels and descriptions, fix spelling/spacing issues, expose the hidden inactivity timeout field, and pair sub tier height+time visually.

---

## Current State

The Supporter Growth panel lives at `MainWindow.xaml:6672-6909` inside a single `Border`/`StackPanel`. It contains ~15 fields in flat `UniformGrid` rows with no grouping:

- Description text (two versions exist — one outdated at line 160, one current at line 161)
- Allow reward scale overlay checkbox + help text
- Normal Height, Max Added Height (UniformGrid 2-col)
- Bits Timer Unit, Seconds Per Bits Unit, Smooth Transition (UniformGrid 3-col)
- Soft Cap Seconds, Soft Cap Multiplier Percent, Max Paid Time (UniformGrid 3-col)
- Grow Keyword, Shrink Keyword (UniformGrid 2-col)
- Tier 1/2/3 Height Add (UniformGrid 3-col)
- Tier 1/2/3 Seconds (UniformGrid 3-col)
- Bits Growth Ranges (ItemsControl with Add/Remove)

### Issues Found

1. **Dense flat layout** — all fields visible at once, no visual grouping
2. **Confusing labels** — "Bits Timer Unit" and "Seconds Per Bits Unit" don't explain the relationship
3. **Missing UI field** — `SupporterGrowthInactivityTimerSeconds` exists in the model (default 60s) but is not exposed in the UI
4. **Outdated localization** — `en-US.extra.json:160` has old description text that doesn't mention resubs
5. **Sub tiers visually separated** — height add and time for each tier are in two separate UniformGrids, making it hard to see that T1 height goes with T1 time
6. **No collapsed summary** — no way to see config at a glance without expanding everything

---

## Proposed Design

### Overall Layout

Reorganize into **5 collapsible `Expander` sections**, each with a summary line visible when collapsed. Uses the existing `Expander` pattern already in the app (`MainWindow.xaml:4011-4126`).

```
Supporter Growth (header + description + overlay checkbox — stays at top, NOT collapsible)

[▼ Height Basics]          Normal: 1.60m | Max Added: 0m (unlimited)
[▶ Paid Time Config]       100 bits = 30s | Soft cap: 1800s @ 50% | Max: 3600s
[▶ Sub Tier Rules]         T1: +0.10m / 300s | T2: +0.20m / 600s | T3: +0.30m / 1500s
[▶ Bits Growth Ranges]     1 range(s) configured
[▶ Cheer Keywords]         grow / shrink
```

- **Height Basics** starts expanded (most commonly edited)
- All other sections start collapsed
- Each collapsed section header shows a compact summary of its current values

### Section 1: Height Basics (expanded by default)

**Fields:** Normal Height, Max Added Height

**Collapsed summary:** `Normal: {value}m | Max Added: {value}m (unlimited)`

**Expanded content:**
- Normal Height input + existing help text
- Max Added Height input + existing help text

**Changes:** Moved into its own `Expander`. Summary text added.

### Section 2: Paid Time Config (collapsed by default)

**Fields:** Bits per Timer Unit, Seconds Added per Unit, Smooth Transition, Soft Cap Seconds, Soft Cap Multiplier %, Max Paid Time, Inactivity Timeout

**Collapsed summary:** `{bits} bits = {seconds}s | Soft cap: {cap}s @ {pct}% | Max: {max}s`

**Expanded content:**

Renamed labels (with localization key updates):
- ~~"Bits Timer Unit"~~ → **"Bits per Timer Unit"**
- ~~"Seconds Per Bits Unit"~~ → **"Seconds Added per Unit"**
- Added example text below the two fields: `Example: {bits} bits adds {seconds} seconds of paid time`

New field:
- **"Inactivity Timeout (seconds)"** — binds to `SupporterGrowthInactivityTimerSeconds` (already in model, default 60s, `Math.Max(1, value)`)

Improved description:
- ~~"Paid time is shared by bits, subs, resubs, and gift subs. Time adds to the remaining paid timer, then slows above the soft cap and never exceeds the max."~~
- → **"Paid time is shared by bits, subs, resubs, and gift subs. Each event adds time to the remaining timer. Time adds at full speed until the soft cap, then slows down and never exceeds the max. Height returns to normal after the inactivity timeout."**

### Section 3: Sub Tier Rules (collapsed by default)

**Fields:** Tier 1/2/3 Height Add, Tier 1/2/3 Seconds

**Collapsed summary:** `T1: +{h}m / {s}s | T2: +{h}m / {s}s | T3: +{h}m / {s}s`

**Expanded content:**
Each tier displayed as a paired row:
```
Tier 1:  [Height Add: 0.10m]  [Time: 300s]
Tier 2:  [Height Add: 0.20m]  [Time: 600s]
Tier 3:  [Height Add: 0.30m]  [Time: 1500s]
```

**Changes:** Height and time for each tier are visually paired in the same row (currently in two separate `UniformGrid`s). Clearer that T1 height goes with T1 time.

### Section 4: Bits Growth Ranges (collapsed by default)

**Fields:** Add Bits Range button, list of range rows

**Collapsed summary:** `{count} range(s) configured`

**Expanded content:** Unchanged from current — Add button, range rows (Min Bits, Max Bits, Height Added) with Remove buttons, same help text.

### Section 5: Cheer Keywords (collapsed by default)

**Fields:** Grow Keyword, Shrink Keyword

**Collapsed summary:** `{grow} / {shrink}`

**Expanded content:** Same two fields and description as current.

---

## Spelling/Spacing Fixes

### en-US.extra.json

1. **Line 160** — Old description: `"Supporter Growth listens to subs, gift subs, and bits. Each event adds height, resets the timer, then returns to normal when support stops."` — This is outdated. The newer description at line 161 is correct. Remove line 160 or update it to match line 161.

2. **"Bits Timer Unit"** → **"Bits per Timer Unit"** — clearer label
3. **"Seconds Per Bits Unit"** → **"Seconds Added per Unit"** — clearer label

### All other *.extra.json files

Add placeholder translations for new keys:
- `"Bits per Timer Unit"`
- `"Seconds Added per Unit"`
- `"Inactivity Timeout (seconds)"`
- `"Height Basics"`
- `"Paid Time Config"`
- `"Sub Tier Rules"`
- `"Cheer Keywords"`
- Section summary format strings
- Updated description text

### XAML labels

Update all `{loc:Translate '...'}` references to use the new key names.

---

## Files to Change

| File | Changes |
|---|---|
| `MainWindow.xaml` | Reorganize Supporter Growth section into 5 collapsible `Expander`s with summary TextBlocks |
| `en-US.extra.json` | Add new keys for section headers, summaries, renamed labels, improved descriptions; remove outdated key at line 160 |
| All other `*.extra.json` | Add placeholder translations for new keys |
| `AvatarScaleRule.cs` | Add computed summary properties: `SupporterGrowthPaidTimeSummary`, `SupporterGrowthSubTierSummary`, `SupporterGrowthBitsRangeCountSummary`, `SupporterGrowthCheerKeywordsSummary` |
| `MainWindowViewModel.cs` | No changes expected — existing bindings sufficient |

### Bug fix: SmoothTransitionSeconds binding

The XAML at line 6750 binds `SmoothTransitionSeconds` in the Supporter Growth section. This is broken:
- `SmoothTransitionSeconds` getter delegates to mode-specific properties via a switch on `ScaleMode`
- Supporter Growth is a `TriggerType`, not a `ScaleMode` — none of the cases match
- Getter returns 0, setter is a no-op
- The actual runtime uses `SupporterGrowthTransitionSeconds` (backing field at line 339, property at line 797)

**Fix:** Change the XAML binding from `SmoothTransitionSeconds` to `SupporterGrowthTransitionSeconds`.

### No other model changes needed

`SupporterGrowthInactivityTimerSeconds` already exists in `AvatarScaleRule.cs:849` with `Math.Max(1, value)` — just needs UI exposure.

---

## Implementation Approach

1. Add computed summary properties to `AvatarScaleRule.cs`
2. Add new localization keys to `en-US.extra.json`
3. Add placeholder translations to all other `*.extra.json`
4. Rewrite the XAML Supporter Growth section with `Expander` controls
5. Run localization audit
6. Build and verify

---

## Out of Scope

- No changes to `SupporterOverrideTimeSettingsWindow` (separate window for Bits + Subs override rules)
- No changes to the Bits + Subs override section
- No changes to Avatar Scaling Master Reward section
- No changes to the model's data structure or persistence
- No changes to `BridgeCoordinator.cs` runtime logic
