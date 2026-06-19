# Subs Tier Toggles Design

**Status:** Draft
**Date:** 2026-06-18
**Scope:** AvatarSwapManagerWindow → Edit Trigger panel → Subscription Settings section

## Problem

The Subscription Settings editor in the AvatarSwapManagerWindow's "Edit Trigger"
panel has two usability issues:

1. **Unnecessary Chat keyword field.** The "Chat keyword" textbox is shown
   for both Bits and Subs, but the keyword semantics are bits-specific (it
   gates the bits outfit/force-movement matchers, not subs). Exposing it on
   the Subs section invites confusion — users may set a keyword expecting
   it to filter subs, but it does nothing for subs at runtime.

2. **All-or-nothing tier configuration.** T1, T2, and T3 each have a
   "seconds/sub" textbox, but there is no way to disable a specific tier.
   If a streamer wants the rule to fire only for T1 subs (e.g. for a
   special tier perk), they have to set T2 and T3 to `0` and hope the
   runtime treats `0` as "skip" — which it doesn't (it uses
   `Math.Max(1, value)`, so a `0` becomes `1`).

## Goal

Remove the unused Chat keyword control from the Subs section, and add an
explicit per-tier enable/disable toggle that gives the runtime a clear
"skip this tier" signal.

## Design

### 1. Model: `Models/TriggerRule.cs` — three new bool properties

Add backing fields and public properties alongside the existing
`SubscriptionTier{1,2,3}SecondsPerSub` properties (around line 489-528):

```csharp
private bool subscriptionTier1Enabled = true;
private bool subscriptionTier2Enabled = true;
private bool subscriptionTier3Enabled = true;

public bool SubscriptionTier1Enabled
{
    get => subscriptionTier1Enabled;
    set
    {
        if (SetProperty(ref subscriptionTier1Enabled, value))
        {
            RaisePropertyChanged(nameof(TriggerSummary));
        }
    }
}

// ... same shape for SubscriptionTier2Enabled, SubscriptionTier3Enabled
```

**Defaults:** all `true` (all tiers start enabled — matches current behavior
where every sub tier triggers the rule).

### 2. Runtime: `Services/BridgeCoordinator.cs:8429` — early return at the entry point

The tier→seconds map at `BridgeCoordinator.cs:6502-6507` cannot signal
"skip" by returning `0` because `GetSupporterOverrideDuration` at line
6426 clamps with `Math.Max(1, seconds)`, so a `0` return becomes
`TimeSpan.FromSeconds(1)` and the rule still fires for 1 second.

The correct skip point is the entry of
`HandleTimedSupporterOverrideTriggerAsync` (line 8429). After the existing
float-add diagnostic check (line 8440-8445), add a tier-enabled guard:

```csharp
if (rule.TriggerType == TwitchTriggerType.Subscriptions
    && !IsSubscriptionTierEnabled(rule, bridgeEvent.SubscriptionTier))
{
    return;
}
```

Add the helper near the other subscription helpers (around line 6498):

```csharp
private static bool IsSubscriptionTierEnabled(TriggerRuleSnapshot rule, string tier)
{
    return tier?.Trim() switch
    {
        "1000" => rule.SubscriptionTier1Enabled,
        "2000" => rule.SubscriptionTier2Enabled,
        "3000" => rule.SubscriptionTier3Enabled,
        _ => true  // unknown tier — don't skip, preserve current behavior
    };
}
```

This skips the rule entirely (no duration added, no cooldown consumed, no
log entry) when the incoming sub's tier is disabled for this rule.

The tier→seconds map at line 6502-6507 is **not modified** — it still
returns a value for the bot-message path at line 16739, which is
informational and not affected by the toggle.

### 3. UI: `UserControls/InlineRuleEditorControl.xaml` — Subs section (lines 212-249)

**Remove** the "Chat keyword" label and textbox (current lines 245-246).

**Replace** each tier's label+textbox pair with a CheckBox+textbox pair where
the CheckBox is the label and the textbox is bound to `IsEnabled`:

```xml
<StackPanel Margin="0,0,6,0">
    <CheckBox IsChecked="{Binding Rule.SubscriptionTier1Enabled}"
              Content="T1 seconds/sub"
              Foreground="{DynamicResource MutedBrush}"
              FontSize="11" Margin="0,0,0,2" />
    <TextBox Text="{Binding Rule.SubscriptionTier1SecondsPerSub, UpdateSourceTrigger=PropertyChanged}"
             IsEnabled="{Binding Rule.SubscriptionTier1Enabled}" />
</StackPanel>
```

(Same shape for T2 and T3.) When unchecked, the textbox greys out and the
seconds value is preserved in the model — re-checking restores the previous
value. This matches the pattern used by the "Require chat keyword" CheckBox
added in the previous spec.

The "Include gift subs" checkbox is unchanged.

### 4. Persistence: backward-compatible defaults

#### `Services/SettingsStore.cs` — `PersistedTriggerRule` DTO

Add three properties with C# property initializers so missing JSON fields
deserialize to `true` (preserving current behavior for old saves):

```csharp
public bool SubscriptionTier1Enabled { get; set; } = true;
public bool SubscriptionTier2Enabled { get; set; } = true;
public bool SubscriptionTier3Enabled { get; set; } = true;
```

Add the three fields to both mapping blocks
(`SettingsStore.cs:1033-1035` for rule→DTO and `SettingsStore.cs:1277-1285`
for DTO→rule). The mapping is a direct field assignment
(`SubscriptionTier1Enabled = rule.SubscriptionTier1Enabled,`).

#### `Services/BridgeRuntimeConfiguration.cs` — `TriggerRuleSnapshot` record

Add three positional parameters with default values:

```csharp
bool SubscriptionTier1Enabled = true,
bool SubscriptionTier2Enabled = true,
bool SubscriptionTier3Enabled = true,
```

Add the three fields to the mapping
(`BridgeRuntimeConfiguration.cs:933-935`).

### 5. Summary text

The `SupporterTimeSettingsSummary` getter in `TriggerRule.cs:1313-1320`
currently shows `"Subs: T1 {0}s, T2 {1}s, T3 {2}s"` regardless of toggle
state. This spec does **not** change the summary — the UI checkbox clearly
shows the on/off state, and the summary's job is to show the configured
seconds values. Adding a "T1: off" indicator is a possible follow-up.

### 6. Bits section is untouched

The previous spec added a "Require chat keyword" CheckBox to the Bits
section and a "Chat keyword" textbox bound to `Rule.SupporterKeywordText`.
This spec does **not** modify the Bits section. The `SupporterKeywordText`
property stays on the model and the auto-sync setter from the previous
spec continues to work for bits.

## Files Changed

| File | Change |
|---|---|
| `VrcTwitchOscBridge/Models/TriggerRule.cs` | 3 new bool properties (Tier1/2/3Enabled) |
| `VrcTwitchOscBridge/Services/BridgeCoordinator.cs` | Tier→seconds map at line 6504-6506; skip when disabled |
| `VrcTwitchOscBridge/UserControls/InlineRuleEditorControl.xaml` | Remove chat keyword from Subs; add 3 tier CheckBoxes + IsEnabled bindings |
| `VrcTwitchOscBridge/Services/SettingsStore.cs` | 3 new DTO fields with `= true` default; both mapping blocks |
| `VrcTwitchOscBridge/Services/BridgeRuntimeConfiguration.cs` | 3 new record fields with default values; mapping |
| `VrcTwitchOscBridge.Tests/TriggerRuleRoundTripTests.cs` | Default + round-trip tests |
| `VrcTwitchOscBridge.Tests/AvatarSwapManagerViewModelTests.cs` | New subs rules have all tiers enabled |

## Tests

1. **Default state** — `TriggerRuleRoundTripTests`:
   - `SubscriptionTier1Enabled` defaults to `true` on a fresh rule
   - `SubscriptionTier2Enabled` defaults to `true`
   - `SubscriptionTier3Enabled` defaults to `true`
   - Setting to `false` round-trips correctly

2. **Backward compat** — `TriggerRuleRoundTripTests`:
   - Round-trip a rule with the new fields through JSON save/load (via
     `PersistedTriggerRule`) and verify all three fields are preserved
   - Verify a DTO with missing fields deserializes the tier fields to
     `true` (C# property initializer default)

3. **New subs rule defaults** — `AvatarSwapManagerViewModelTests`:
   - `AddSubsRuleCommand` produces a rule with all three tier toggles `true`

4. **Runtime skip behavior** — manual smoke test during Task 11 (the
   existing test infrastructure doesn't cover the bits coordinator's
   sub-path directly; a dedicated unit test would require significant
   scaffolding that's out of scope for this spec).

## Out of Scope

- Bits section is not modified
- `SupporterKeywordText` model property is not removed (still used for bits)
- Summary text (`SupporterTimeSettingsSummary`) is not updated to indicate
  disabled tiers
- `AvatarSwapRuleEditorControl.xaml` (the main window's rule editor) is
  not modified — same XAML pattern can be applied in a follow-up if needed
- No changes to `IsGiftSubscription`, `AmountScaledDurationEnabled`, or
  any other existing subs-related property
