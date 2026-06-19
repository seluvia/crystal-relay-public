# Bits Trigger: Keyword Toggle and Seconds-per-Bits Labeling

**Status:** Draft
**Date:** 2026-06-18
**Scope:** AvatarSwapManagerWindow → Edit Trigger panel → Bits Settings section

## Problem

The Bits Settings editor in the AvatarSwapManagerWindow's "Edit Trigger" panel
(`UserControls/InlineRuleEditorControl.xaml`, Bits-specific StackPanel) has two
usability issues:

1. **Unlabeled "Seconds per Bits" boxes.** The two side-by-side `TextBox`es
   share a single "Seconds per Bits" header but have no individual labels.
   Users don't know which box is "bits" and which is "seconds", or what the
   relationship between the two values is.

2. **Implicit chat-keyword behavior.** The `SupporterKeywordText` field is
   always shown for bits rules, but the runtime silently treats an empty
   keyword as "bits only" and a non-empty keyword as "keyword required".
   There is no UI affordance to make this choice explicit, and users cannot
   tell which mode a given rule is in without inspecting the saved JSON.

## Goal

Make the two "Seconds per Bits" boxes self-explanatory and add an explicit
checkbox to control whether the chat keyword is required for a bits rule.

## Design

### 1. New model property: `TriggerRule.BitsKeywordEnabled`

Add one new boolean property to `Models/TriggerRule.cs`:

```csharp
private bool bitsKeywordEnabled;

public bool BitsKeywordEnabled
{
    get => bitsKeywordEnabled;
    set
    {
        if (SetProperty(ref bitsKeywordEnabled, value))
        {
            RaisePropertyChanged(nameof(UsesBitsKeyword));
            RaisePropertyChanged(nameof(TriggerSummary));
        }
    }
}
```

**Default:** `false` (new rules are bits-only until the user opts in to a
keyword).

**Auto-migration via the existing `SupporterKeywordText` setter:** modify the
setter so that:

- Setting to a non-empty (trimmed) value → also sets `BitsKeywordEnabled = true`
- Setting to empty → also sets `BitsKeywordEnabled = false`

This preserves the behavior of every existing saved rule. A rule that already
has a keyword saved will auto-check the box on next load; a rule without a
keyword stays unchecked. No data migration is needed.

### 2. Computed property: `TriggerRule.UsesBitsKeyword`

```csharp
[JsonIgnore]
public bool UsesBitsKeyword
    => BitsKeywordEnabled && !string.IsNullOrWhiteSpace(SupporterKeywordText);
```

Single source of truth for runtime and UI. A rule "uses the keyword" only when
both the toggle is on AND a keyword is configured.

### 3. UI changes: `UserControls/InlineRuleEditorControl.xaml`

#### 3a. Seconds-per-Bits labeling

Replace the current single-label/two-box block (currently two `TextBox`es
inside one `StackPanel` with one "Seconds per Bits" header) with two labeled
`StackPanel`s in a `UniformGrid Columns="2"`, each with its own small label,
plus a hint line below the boxes:

```xml
<StackPanel Margin="8,0,0,0">
    <UniformGrid Columns="2">
        <StackPanel Margin="0,0,4,0">
            <TextBlock Text="{loc:Translate 'Bits'}" Foreground="{DynamicResource MutedBrush}" FontSize="11" Margin="0,0,0,2" />
            <TextBox Text="{Binding Rule.BitsAmountUnitsPerDuration, UpdateSourceTrigger=PropertyChanged}" />
        </StackPanel>
        <StackPanel Margin="4,0,0,0">
            <TextBlock Text="{loc:Translate 'Seconds'}" Foreground="{DynamicResource MutedBrush}" FontSize="11" Margin="0,0,0,2" />
            <TextBox Text="{Binding Rule.BitsSecondsPerAmountUnit, UpdateSourceTrigger=PropertyChanged}" />
        </StackPanel>
    </UniformGrid>
    <TextBlock Text="{loc:Translate 'Every X bits = Y seconds'}"
               Foreground="{DynamicResource MutedBrush}" FontSize="10"
               FontStyle="Italic" Margin="0,4,0,0" />
</StackPanel>
```

The formula reminder uses the existing pattern of greyed, italicized helper
text under the inputs (same style used elsewhere in the editor).

#### 3b. Chat keyword toggle

Below the existing "Cap max accumulated duration" block and before the
"Chat keyword" `TextBox`, add a `CheckBox`:

```xml
<CheckBox IsChecked="{Binding Rule.BitsKeywordEnabled}"
          Content="{loc:Translate 'Require chat keyword'}"
          Margin="0,8,0,0" />
<TextBlock Text="Chat keyword" Foreground="{DynamicResource MutedBrush}"
           FontSize="11" Margin="0,8,0,2" />
<TextBox Text="{Binding Rule.SupporterKeywordText, UpdateSourceTrigger=PropertyChanged}"
         IsEnabled="{Binding Rule.BitsKeywordEnabled}" />
```

When the checkbox is unchecked, the `TextBox` greys out (WPF default disabled
appearance). The saved keyword value is preserved in the model — re-checking
the box restores the previous value.

### 4. Runtime changes: `Services/BridgeCoordinator.cs`

There are four call sites that read `SupporterKeywordText` for bits triggers.
Replace each `!string.IsNullOrWhiteSpace(rule.SupporterKeywordText)` check
with `rule.UsesBitsKeyword`:

| Location | Context |
|---|---|
| `BridgeCoordinator.cs:12881` | Bits outfit set trigger filter |
| `BridgeCoordinator.cs:13245-13247` | Bits outfit keyword matching |
| `BridgeCoordinator.cs:16482-16485` | Bits force-movement trigger filter + summary |
| `BridgeCoordinator.cs:16657-16682` | Bits duration calculation summary |

Net effect:

- `BitsKeywordEnabled = false` → keyword ignored, rule is bits-only (even if
  a stale keyword value is still in the model)
- `BitsKeywordEnabled = true` + keyword empty → rule is dead (can't match
  anything), same as today's behavior for an empty keyword
- `BitsKeywordEnabled = true` + keyword set → keyword required, same as today

### 5. Settings persistence: `Services/SettingsStore.cs`

Add `BitsKeywordEnabled` to the rule DTOs that already carry
`SupporterKeywordText` so it round-trips through JSON save/load:

- `PersistedTriggerRule` (defined at `SettingsStore.cs:3153`, the DTO that
  holds the serialized rule fields including `SupporterKeywordText` at
  `SettingsStore.cs:3257`)
- `TriggerRuleSnapshot` (defined at `BridgeRuntimeConfiguration.cs:58`, the
  runtime snapshot record that already carries `SupporterKeywordText` at
  `BridgeRuntimeConfiguration.cs:134`)

Add the new field to the DTOs and to the two mapping blocks
(`SettingsStore.cs:1060` for rule→DTO and `SettingsStore.cs:1315` for
DTO→rule, and the corresponding mappings in `BridgeRuntimeConfiguration.cs`).

Default to `false` when the field is missing on load (backward-compatible with
existing saves that don't have the field).

### 6. Localization

Add new keys to `en-US.json` (and mirror in `.extra.json`):

- `"Bits"` — label for the first box
- `"Seconds"` — label for the second box
- `"Every X bits = Y seconds"` — hint text below the boxes
- `"Require chat keyword"` — checkbox label

All other locales fall back to `en-US` via the existing localization audit
workflow. The localization audit will flag missing keys for translators.

## Files Changed

| File | Change |
|---|---|
| `VrcTwitchOscBridge/Models/TriggerRule.cs` | New `BitsKeywordEnabled` property, auto-sync in `SupporterKeywordText` setter, new `UsesBitsKeyword` computed property |
| `VrcTwitchOscBridge/UserControls/InlineRuleEditorControl.xaml` | Relabel seconds-per-bits boxes, add hint line, add "Require chat keyword" checkbox, bind `IsEnabled` on keyword textbox |
| `VrcTwitchOscBridge/Services/BridgeCoordinator.cs` | Four call sites swap `SupporterKeywordText` emptiness check for `UsesBitsKeyword` |
| `VrcTwitchOscBridge/Services/SettingsStore.cs` | Add `BitsKeywordEnabled` to bits rule DTOs with default `false` on load |
| `VrcTwitchOscBridge/Resources/Localization/en-US.json` | New keys: `Bits`, `Seconds`, `Every X bits = Y seconds`, `Require chat keyword` |
| `VrcTwitchOscBridge.Tests/TriggerRuleRoundTripTests.cs` | Extend round-trip test to cover `BitsKeywordEnabled` and auto-sync |
| `VrcTwitchOscBridge.Tests/AvatarSwapManagerViewModelTests.cs` | Test that a new bits rule gets `BitsKeywordEnabled = false` by default |

## Tests

1. **Auto-sync setter behavior** — `TriggerRuleTests` (new or extended):
   - `BitsKeywordEnabled` starts `false` on a fresh rule
   - Setting `SupporterKeywordText = "hello"` flips `BitsKeywordEnabled` to `true`
   - Setting `SupporterKeywordText = ""` flips `BitsKeywordEnabled` back to `false`
   - `UsesBitsKeyword` returns `false` when toggle is off regardless of keyword
   - `UsesBitsKeyword` returns `false` when toggle is on but keyword is empty
   - `UsesBitsKeyword` returns `true` only when both toggle is on AND keyword is non-empty

2. **Round-trip persistence** — `TriggerRuleRoundTripTests`:
   - `BitsKeywordEnabled` survives JSON save → load

3. **Default for new rules** — `AvatarSwapManagerViewModelTests`:
   - `AddBitsRuleCommand` produces a rule with `BitsKeywordEnabled = false`

4. **Localization audit** — run after the XAML and JSON changes to confirm
   the four new keys exist in `en-US.json` and that the audit doesn't flag
   missing translations for the other locales (they fall back to `en-US`).

## Out of Scope

- Subs section does NOT get a keyword toggle. The same `SupporterKeywordText`
  field is still used there, but the checkbox is bits-only as requested.
- No changes to the main window's rule editor (`UserControls/AvatarSwapRuleEditorControl.xaml`)
  for this iteration — the same XAML pattern will be reused there in a
  follow-up if needed, but it's not part of this spec.
- No changes to Fooma import, Avatar Scaling, or any other reward system.
- No changes to `MinimumAmount` behavior — that field is independent of the
  keyword toggle.
