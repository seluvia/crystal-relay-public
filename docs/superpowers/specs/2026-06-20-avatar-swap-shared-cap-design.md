# Per-Avatar Avatar Swap: Shared Cap and Subs Toggle Rename

Date: 2026-06-20
Lane: in-progress 3.1.9 beta 4
Status: design approved, pending user spec review

## Problem

The per-avatar Bits and Subs triggers inside the new Avatar Swap manager window each have their own "Cap max accumulated duration" setting (on `TriggerRule`). When a streamer wants to cap the total override time on a specific avatar, they have to set the same cap on every Bits rule and every Subs rule independently. If they edit the cap on the Bits side and want the same value on the Subs side, they have to copy it manually. The cap is a property of the avatar's override, not of individual rules.

Additionally, the Subs editor uses the same toggle name as the Bits editor ("Add bits time to swap"), which is confusing for streamers. The Subs editor is also missing the "Cap max accumulated duration" section that the Bits editor has.

## Goal

1. Move the per-avatar cap to the `AvatarSwapProfile` so all Bits and Subs rules in the same avatar share a single cap. Both editors show the same value; editing one updates the other.
2. Rename the Subs toggle from "Add bits time to swap" to "Add sub time to swap".
3. Add the "Cap max accumulated duration" section to the Subs editor (currently missing), bound to the same profile cap.
4. Hide the T1/T2/T3 inputs in the Subs editor when the toggle is off, matching the Bits editor's behavior.
5. Keep the per-rule `MaxAccumulatedDuration` on `TriggerRule` for the global override context. The cap moves to the profile only for the per-avatar Avatar Swap context.

## Non-goals

- No change to the global Bits+Subs override behavior.
- No change to the math function (`SupportOverrideDurationMath`).
- No change to the existing `AmountScaledDurationEnabled` field on `TriggerRule`.
- No change to the Avatar Roulette, Avatar Set, or Avatar Scaling action paths.
- No change to the per-avatar Channel Point or Payment trigger rules.
- No rename of the persisted field `AmountScaledDurationEnabled`.

## Current architecture (ground truth from code reading)

### Model
- `Models/AvatarSwapProfile.cs:7-50` - `AvatarSwapProfile` class. Holds four `ObservableCollection<TriggerRule>` collections (ChannelPoint, Bits, Subs, Payment). No cap field on the profile today.
- `Models/TriggerRule.cs:570-593` - per-rule `MaxAccumulatedDurationEnabled` and `MaxAccumulatedDurationSeconds`. Used by both global and per-avatar contexts.
- `Models/TriggerRule.cs:74-87` - new `AddBitsToSwapTime` field from the previous task.

### Snapshot
- `Services/BridgeRuntimeConfiguration.cs:300-309` - `AvatarSwapProfileSnapshot` record. Holds four `IReadOnlyList<TriggerRuleSnapshot>` collections. No cap field on the snapshot today.
- `Services/BridgeRuntimeConfiguration.cs:58-143` - `TriggerRuleSnapshot` record. Includes per-rule cap fields at lines 91-92.
- `Services/BridgeRuntimeConfiguration.cs:607-621` - `FindAvatarSwapProfileForRule` locates the parent `AvatarSwapProfileSnapshot` for a given `TriggerRule`. Already exists.

### Runtime
- `Services/BridgeCoordinator.cs:6517-6537` - `ClampSupporterOverrideAddedDuration`. Uses `rule.MaxAccumulatedDurationEnabled` and `rule.MaxAccumulatedDurationSeconds`. The single cap-clamp function.
- `Services/BridgeCoordinator.cs:8440-8558` - `HandleTimedSupporterOverrideTriggerAsync`. The dispatch path. Calls the clamp function at line 8510.
- `Services/BridgeCoordinator.cs:8155` - already calls `FindAvatarSwapProfileForRule` to look up the parent profile for a rule.

### UI
- `UserControls/InlineRuleEditorControl.xaml` - the per-rule editor. The Bits section (lines 162-210) has the "Cap max accumulated duration" bound to the rule's cap. The Subs section (lines 213-260) has T1/T2/T3 but no cap section. Both sections have the "Add bits time to swap" toggle (the previous task added these checkboxes; the Subs one needs renaming).
- `UserControls/InlineBitsRuleRowViewModel.cs` and `InlineSubsRuleRowViewModel.cs` - row summary text. Already show the ", swap time" chip when the toggle is on.
- `ViewModels/AvatarSwapManagerViewModel.cs` - the orchestrator. Creates the inline editor for each rule. The current code needs to be updated to pass the parent profile to the editor.

### Persistence
- `Services/SettingsStore.cs:3074-3096` - `PersistedAvatarSwapProfile`. No cap field.
- `Services/SettingsStore.cs:3160-3315` - `PersistedTriggerRule`. Has the per-rule cap fields.
- `Services/AvatarSwapMigrationService.cs:9` - `CurrentMigrationVersion = 6`. Bump to 7.

## Design

### Profile cap field

`Models/AvatarSwapProfile.cs` gains two new fields:

```csharp
public bool MaxSwapTimeEnabled { get; set; } = false;
public int MaxSwapTimeSeconds { get; set; } = 1800;
```

Both properties use simple auto-properties with default values. The class already implements `INotifyPropertyChanged` via `ObservableObject`; the cap properties should raise change notifications so the UI updates when one editor changes the value. Use `SetProperty` if `ObservableObject` provides it; otherwise use `RaisePropertyChanged` directly.

`Services/BridgeRuntimeConfiguration.cs` `AvatarSwapProfileSnapshot` gains two new positional parameters:

```csharp
public sealed record AvatarSwapProfileSnapshot(
    Guid Id,
    string TargetAvatarId,
    string TargetAvatarName,
    string? TargetThumbnailUrl,
    bool IsEnabled,
    bool MaxSwapTimeEnabled,
    int MaxSwapTimeSeconds,
    IReadOnlyList<TriggerRuleSnapshot> ChannelPointRules,
    IReadOnlyList<TriggerRuleSnapshot> BitsRules,
    IReadOnlyList<TriggerRuleSnapshot> SubsRules,
    IReadOnlyList<TriggerRuleSnapshot> PaymentRules);
```

`Services/SettingsStore.cs` `PersistedAvatarSwapProfile` gains the same two fields. The DTO round-trip in `ToPersistedProfile` / `ToProfile` copies the fields.

### Editor gets a Profile dependency

`UserControls/InlineRuleEditorControl.xaml.cs` gains a new `Profile` dependency property:

```csharp
public static readonly DependencyProperty ProfileProperty = DependencyProperty.Register(
    nameof(Profile),
    typeof(AvatarSwapProfile),
    typeof(InlineRuleEditorControl),
    new PropertyMetadata(null));

public AvatarSwapProfile? Profile
{
    get => (AvatarSwapProfile?)GetValue(ProfileProperty);
    set => SetValue(ProfileProperty, value);
}
```

`ViewModels/AvatarSwapManagerViewModel.cs` passes the parent profile to the editor when constructing it for a rule. Read the existing pattern first to match the construction style.

### UI changes

`UserControls/InlineRuleEditorControl.xaml` Bits section (around line 201-204): change the "Cap max accumulated duration" bindings from `Rule.MaxAccumulatedDurationEnabled` to `Profile.MaxSwapTimeEnabled` and from `Rule.MaxAccumulatedDurationSeconds` to `Profile.MaxSwapTimeSeconds`. The CheckBox and TextBox stay where they are; only the binding paths change.

`UserControls/InlineRuleEditorControl.xaml` Subs section:
1. Rename the existing "Add bits time to swap" CheckBox to "Add sub time to swap". The XAML binding stays on `Rule.AddBitsToSwapTime`; only the `Content` literal changes.
2. Wrap the T1/T2/T3 inputs in a `Visibility` binding to `Rule.AddBitsToSwapTime` using `BoolToVisibilityConverter`. The "Include gift subs" CheckBox stays outside the visibility-bound group.
3. Add a new "Cap max accumulated duration" section after the T1/T2/T3 block (and before "Include gift subs"), bound to `Profile.MaxSwapTimeEnabled` and `Profile.MaxSwapTimeSeconds`. Same XAML pattern as the Bits section's cap section, just bound to the profile.

### Runtime change

`Services/BridgeCoordinator.cs` `ClampSupporterOverrideAddedDuration` gets two new parameters `(bool capEnabled, int capSeconds)`. The function uses these instead of `rule.MaxAccumulatedDurationEnabled` / `rule.MaxAccumulatedDurationSeconds`.

`HandleTimedSupporterOverrideTriggerAsync` at line 8510 updates the call to `ClampSupporterOverrideAddedDuration` to pass the cap from the rule's parent profile when one exists, or fall back to the rule's own cap for the global override context.

The cleanest implementation: extract a `GetOverrideCap(rule)` helper that returns `(bool enabled, int seconds)` - it looks up the profile via `FindAvatarSwapProfileForRule(rule.Rule)`; if a profile is found, returns the profile's cap; if not, returns the rule's cap. The call site uses this helper.

`Services/BridgeCoordinator.cs:8507` uses the cap from the existing line that already calls `DescribeDuration(Math.Max(1, rule.MaxAccumulatedDurationSeconds))`. This is for the bot message text. Update to use the resolved cap.

### Migration

`Services/AvatarSwapMigrationService.cs:9` bumps `CurrentMigrationVersion` to 7. Add a `MigrateV6ToV7` method that defaults the new fields to `false` / `1800` for all existing `PersistedAvatarSwapProfile` instances. Wire it into the migration chain at line 35-38.

### Localization

`Resources/Localization/en-US.json` gets a new key:

```json
"Add sub time to swap": "Add sub time to swap",
```

All 14 non-English locale files get matching translations. Suggested translations:
- `de-DE`: `Sub-Zeit zum Wechsel hinzufügen`
- `es-ES`: `Tiempo de sub al cambio`
- `fr-FR`: `Temps de sub au changement`
- `it-IT`: `Tempo di sub al cambio`
- `ja-JP`: `スワップにSubの時間を追加`
- `ko-KR`: `교환에 Sub 시간 추가`
- `pl-PL`: `Dodaj czas z sub do zmiany`
- `pt-BR`: `Tempo de sub à troca`
- `ru-RU`: `Добавить время от sub к переключению`
- `sv-SE`: `Lägg till sub-tid till bytet`
- `th-TH`: `เพิ่มเวลาจาก sub ให้การสลับ`
- `zh-CN`: `将 Sub 时间加到切换中`
- `zh-TW`: `將 Sub 時間加到切換中`

### Backwards compatibility

Existing saves have no `MaxSwapTimeEnabled` / `MaxSwapTimeSeconds` fields on the profile. The migration V6→V7 defaults them to `false` / `1800`. The deserializer on `PersistedAvatarSwapProfile` will default the new fields to their C# defaults when absent from JSON.

For users who had `MaxAccumulatedDuration` on individual per-avatar rules: the existing per-rule cap is now ignored in the per-avatar context (the profile's cap is used instead). The per-rule cap is still used in the global override context. This is a behavior change for the per-avatar context: if a user had different caps on different per-avatar rules, all of those rules will now share the profile's cap (default `1800s`, disabled). The migration does NOT migrate the old per-rule cap values to the profile - the profile's cap starts fresh. Streamers who want a non-default cap need to set it on the profile.

### Tests

**New test file `VrcTwitchOscBridge.Tests/SupportOverrideCapClampTests.cs`:**
- `ClampWithProfileCapEnabled_At1800_AddsRequested` - profile cap 1800s, current 1750s, requested 34s -> adds 34s
- `ClampWithProfileCapEnabled_ClampsToRemainingCapacity` - profile cap 1800s, current 1790s, requested 34s -> clamps to 10s
- `ClampWithProfileCapDisabled_NoClamp` - profile cap disabled, requested 1000s -> no clamp
- `ClampWithProfileNull_UsesRuleCap` - profile null, rule's own cap enabled at 1800s -> uses rule's cap (global fallback)
- `ClampWithProfileNull_RuleCapDisabled_NoClamp` - profile null, rule's cap disabled -> no clamp

**New test file `VrcTwitchOscBridge.Tests/AvatarSwapMigrationServiceV7Tests.cs`:**
- `CurrentMigrationVersion_IsAtLeast7` - matches the V6 test pattern

**Update `VrcTwitchOscBridge.Tests/TestTriggerRuleSnapshotBuilder.cs`:**
- No change - the builder is for rule-level tests, not profile-level tests.

## Files touched

Code:
- `VrcTwitchOscBridge/Models/AvatarSwapProfile.cs` - two new properties.
- `VrcTwitchOscBridge/Services/BridgeRuntimeConfiguration.cs` - `AvatarSwapProfileSnapshot` gets two new positional parameters; `FindAvatarSwapProfileForRule` unchanged; `CreateSnapshot` for the profile maps the new fields.
- `VrcTwitchOscBridge/Services/BridgeCoordinator.cs` - `ClampSupporterOverrideAddedDuration` gets two new parameters; `HandleTimedSupporterOverrideTriggerAsync` resolves the cap from profile or rule; new `GetOverrideCap` helper.
- `VrcTwitchOscBridge/Services/SettingsStore.cs` - `PersistedAvatarSwapProfile` gets two new fields; round-trip mapping updated.
- `VrcTwitchOscBridge/Services/AvatarSwapMigrationService.cs` - bump to version 7; add `MigrateV6ToV7`; wire into chain.
- `VrcTwitchOscBridge/UserControls/InlineRuleEditorControl.xaml` - Bits cap section rebinds to profile; Subs section gets new toggle label, visibility binding, and cap section.
- `VrcTwitchOscBridge/UserControls/InlineRuleEditorControl.xaml.cs` - new `Profile` dependency property.
- `VrcTwitchOscBridge/ViewModels/AvatarSwapManagerViewModel.cs` - pass the parent profile to the inline editor when creating it.
- `VrcTwitchOscBridge/Resources/Localization/en-US.json` - new key.
- 14 non-English `*.json` files - matching translations.

Tests:
- `VrcTwitchOscBridge.Tests/SupportOverrideCapClampTests.cs` - new file, 5 test cases.
- `VrcTwitchOscBridge.Tests/AvatarSwapMigrationServiceV7Tests.cs` - new file, 1 test case.

Docs:
- `CHANGELOG.txt` - 2 new bullets in `v3.1.9 beta 4` section.
- `RELEASE-CHANGE-RECORD.txt` - 2 new bullets in `v3.1.9 beta 4 (in progress)` section.

No change to `AGENTS.md`, `README.md`, or the Void Crystal Website.

## Risk and rollback

- The cap moves from per-rule to per-profile. Existing per-avatar rules with per-rule cap values will see their cap reset to the profile's default (1800s, disabled). This is a behavior change for users who had non-default per-rule caps. Rollback restores per-rule cap usage.
- The runtime change is isolated to the cap function and its single call site. The math function (`ComputePerEventAddSeconds`) is unchanged. The dispatch path is unchanged.
- The editor change adds a new dependency property and rebinds existing controls. The risk is breaking XAML data binding. Rollback reverts the binding paths.
- The migration is a no-op that just bumps the version. Rollback is safe.
- If a regression slips through, reverting the cap function signature, the editor bindings, and the profile fields restores the prior behavior. No data migration is needed because the data format is unchanged (the new fields default to safe values).

## Out of scope / not in this change

- Migrating existing per-rule `MaxAccumulatedDuration` values to the profile's new cap. The profile's cap starts fresh.
- A per-rule cap override. The profile's cap is the only cap in the per-avatar context.
- Removing the per-rule cap from `TriggerRule` entirely. It stays for the global override context.
- Refactoring the cap math, the queue path, or the per-action reset scheduling.
- Renaming any other UI labels.
- Updating `README.md` or the Void Crystal website.
- Pushing to the public or private GitHub repos. This is dev mode; no pushes.
- Building or publishing a beta 4 test package or release package.
