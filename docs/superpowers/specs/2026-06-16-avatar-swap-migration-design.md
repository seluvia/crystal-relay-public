# Avatar Swap — Migration of the Avatar Change Redeem Library

**Date:** 2026-06-16
**Status:** Draft — pending user review
**Author:** opencode / MiniMax-M3
**Target version:** Active development (3.1.9 → 3.1.10)

---

## 1. Summary

The existing **Avatar Change** Redeem Library in Crystal Relay is a text-based,
rule-list-only UI that is woven into three separate surfaces of `MainWindow.xaml`
(Master tab, Supporter override lane, per-rule action panel) and shares a single
`OscActionType.AvatarChange` enum value with Avatar Roulette and the rule
editor. Avatar images only appear inside the modal `AvatarPickerWindow` — the
rule form itself does not show the picked avatar.

This spec migrates Avatar Change into a dedicated **Avatar Swap** manager
window, following the same hand-rolled WPF pattern that `AvatarSetsManagerWindow`
and `UniversalTriggersManagerWindow` already use. Avatar Change is renamed
"Avatar Swap" in every user-facing string. The picked avatar is shown as an
image-and-name card so the streamer can see what they are swapping to at a
glance, while the existing `AvatarImageService` / `AvatarPickerWindow` pipeline
is preserved unchanged.

## 2. Goals

- Replace the three scattered Avatar Change surfaces with **one manager
  window** opened from the Redeem Library.
- Group the existing Avatar Swap rules **per target avatar** (mirrors the
  Avatar Sets card layout, but for swap targets instead of redeem hosts).
- Show a **header banner** at the top of the manager that owns the global
  Return Avatar picker.
- Split the cards into **two collapsible sections**: Channel Point Swaps and
  Bits + Subs Swaps. The old "Avatar Change Override" lane in the Supporter
  Overrides section is removed in favor of the Bits + Subs section.
- Rename "Avatar Change" → "Avatar Swap" in **every user-facing string**, but
  keep the existing internal field names (`OscActionType.AvatarChange`,
  `TriggerRule.AvatarChangeTargetId`, etc.) to avoid breaking serialized save
  files. The old names become pure code artifacts.
- Reuse `AvatarImageService` and `AvatarPickerWindow` exactly as they exist
  today — no image-pipeline changes.
- Migrate existing settings automatically on first load after upgrade.

## 3. Non-Goals

- No rename of `OscActionType.AvatarChange` to `OscActionType.AvatarSwap`. The
  enum value keeps its current name; the new `AvatarSwapProfile` model lives
  on top of the same `TriggerRule` rule data.
- No rename of `TriggerRule.AvatarChangeTargetId` /
  `TriggerRule.AvatarChangeResetId`. Existing saves and Fooma imports keep
  working without a one-time JSON rewrite.
- `OscActionType.AvatarChange` stays in the generic rule editor's `ActionType`
  combo. Power-ups, Universal Triggers, Avatar Roulette, Cash Payments, and
  Fooma-imported rules can still produce `OscActionType.AvatarChange` rules
  outside the new manager. The manager simply groups any rule that already
  targets a given avatar into the matching `AvatarSwapProfile` on next load.
- The **per-rule editor's `UsesAvatarChange` action block** (the
  inline avatar-picker panel that opens inside the rule form when the rule's
  action is `AvatarChange`) **is removed** in this migration. New Avatar Swap
  rules are created through the new manager. Pre-existing rules still fire
  at runtime through the existing path.
- No changes to the Twitch reward-sync logic, the Bits + Subs override
  priority, the cooldown-only mode, the avatar-scaling "block avatar
  changes" guard, or any other runtime behavior in `BridgeCoordinator`.
- No changes to `Avatar Sets` or `Universal Triggers` beyond what is strictly
  required to keep them compiling.

## 4. Architecture Overview

```
+-----------------------------------------------------+
|  MainWindow.xaml  (Redeem Library right column)      |
|                                                     |
|   [Avatar Sets] [Avatar Swap]  [Avatar Scaling] ... |
|                            |                        |
+----------------------------|------------------------+
                             |
                             v
+-----------------------------------------------------+
|  AvatarSwapManagerWindow (new, custom-chrome)       |
|                                                     |
|   [ Return Avatar banner: image + name + Pick... ]  |
|                                                     |
|   v Channel Point Swaps (3)  [Disable All] [Add]    |
|     +-------+ +-------+ +-------+                   |
|     | card  | | card  | | card  |   (per-target    |
|     +-------+ +-------+ +-------+    avatar cards)  |
|                                                     |
|   v Bits + Subs Swaps (2)  [Disable All] [Add]     |
|     +-------+ +-------+                             |
|     | card  | | card  |                             |
|     +-------+ +-------+                             |
|                                                     |
|   [side-docked 480px editor slides in on click]     |
+-----------------------------------------------------+
```

The manager window follows the exact same shape as
`UniversalTriggersManagerWindow`: `WindowChrome`, inline resource dictionary,
`DynamicResource` brushes, `WrapPanel` of cards, side-docked editor pane
with snapshot rollback, `ThemeManager.ThemeChanged` re-skin hook.

## 5. Data Model

### 5.1 New `AvatarSwapProfile` model

`VrcTwitchOscBridge/Models/AvatarSwapProfile.cs` — new file, 200–250 LOC,
`ObservableObject` in the same style as `AvatarTriggerProfile`.

```
public sealed class AvatarSwapProfile : ObservableObject
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public string TargetAvatarId { get; set; } = string.Empty;
    public string TargetAvatarName { get; set; } = string.Empty;
    public string? TargetThumbnailUrl { get; set; }
    public ReturnAvatarMode ReturnAvatarMode { get; set; } = ReturnAvatarMode.UseGlobal;
    public string? ReturnAvatarId { get; set; }    // used when Mode == UseCustom
    public string? ReturnAvatarName { get; set; }
    public bool IsEnabled { get; set; } = true;
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
    public DateTime UpdatedAt { get; set; } = DateTime.UtcNow;

    public ObservableCollection<TriggerRule> ChannelPointRules { get; set; } = new();
    public ObservableCollection<TriggerRule> BitsSubsRules { get; set; } = new();

    // Computed display
    public string DisplayTitle => string.IsNullOrWhiteSpace(TargetAvatarName)
        ? TargetAvatarId : TargetAvatarName;
    public string AvatarSubtitle => $"{(ChannelPointRules.Count + BitsSubsRules.Count)} swap rules";
    public bool HasRules => ChannelPointRules.Count + BitsSubsRules.Count > 0;
    public bool UsesChannelPointRules => ChannelPointRules.Count > 0;
    public bool UsesBitsSubsRules => BitsSubsRules.Count > 0;
    public string ReturnAvatarDisplay => ReturnAvatarMode switch
    {
        ReturnAvatarMode.UseGlobal => "↩ Global",
        ReturnAvatarMode.UseCustom => $"↩ {ReturnAvatarName}",
        ReturnAvatarMode.SameAsTarget => "↻ Same as target",
        _ => string.Empty
    };

    // Brushes (mirroring AvatarTriggerProfile — initialized in ctor, frozen)
    public SolidColorBrush StatusStripeReadyBrush { get; }
    public SolidColorBrush StatusStripeWarnBrush { get; }
    public SolidColorBrush StatusStripeOffBrush { get; }
    public string StatusText => IsEnabled ? "Ready" : "Disabled";
}

public enum ReturnAvatarMode
{
    UseGlobal,    // uses Settings.MasterAvatarSwapReturnId
    UseCustom,    // uses this profile's ReturnAvatarId
    SameAsTarget  // timed swaps "stick" — covered by cooldown-only mode at runtime
}
```

**`IsEnabled` semantics:** `AvatarSwapProfile.IsEnabled` is the master
switch for the profile. A disabled profile's rules are not synced to
Twitch and not fired at runtime (this matches `AvatarTriggerProfile`).
Each individual `TriggerRule.IsEnabled` is also respected — a disabled
rule within an enabled profile is hidden but its slot is preserved.

The `Image` (ImageSource) and the async load/cancel pipeline are owned by
`AvatarSwapCardViewModel` (just like `AvatarSetCardViewModel`). The card VM
uses the existing `AvatarImageService.GetAvatarImageAsync(TargetAvatarId,
null, TargetThumbnailUrl)` flow with a per-card `CancellationTokenSource`
that is replaced whenever the target avatar changes.

### 5.2 AppSettings changes

`VrcTwitchOscBridge/Models/AppSettings.cs`:

- **Add** `ObservableCollection<AvatarSwapProfile> AvatarSwapProfiles = new();`
- **Add** `string? MasterAvatarSwapReturnId` and `string? MasterAvatarSwapReturnName`
  (the global return avatar owned by the header banner).
- **Keep** `AvatarChangeCooldownOnlyModeEnabled` (still relevant to existing rules
  outside the new manager).
- **Keep** `MasterAvatarProfile` and its `ChannelPointRules` for **one release**
  so the migration can read from it without losing data. They are removed
  after the migration is verified to round-trip.
- **Keep** `GlobalOverrideRules` for the same reason.

### 5.3 TriggerRule

No changes. `ActionType=AvatarChange`, `AvatarChangeTargetId`,
`AvatarChangeResetId`, `AvatarTargetName`, `ResetAvatarName` all stay
verbatim. The Avatar Swap manager reads and writes these fields through the
existing `TriggerRule` API.

## 6. UI Design

### 6.1 New `AvatarSwapManagerWindow.xaml`

A `Window` with `WindowStyle="None"` and `shell:WindowChrome` (matching the
two existing manager windows). Inline resource dictionary carries every
brush, template, and converter the manager needs.

**Layout (top to bottom):**

1. **Title bar** — "Avatar Swap" title, theme-aware drag region, close (✕).
2. **Command bar** — `NestedPanelBrush` strip with: search box, sort combo
   (Name / Status / Recently Edited), "Enable All" / "Disable All" buttons,
   and a "New Swap" button (opens the picker in single-select mode for the
   target avatar and creates a new `AvatarSwapProfile`).
3. **Return Avatar banner** — full-width `NestedPanelBrush` panel with the
   current Return Avatar's image (96×96 from `AvatarImageService`), name,
   "Pick..." button, "Use Current Avatar" button, and a "Clear" button.
4. **Channel Point Swaps section** — collapsible (chevron + count) section
   header with "Disable Section" and "Add Swap" buttons. `ItemsControl` of
   `AvatarSwapCardViewModel` cards in a `WrapPanel`.
5. **Bits + Subs Swaps section** — same shape, different section. Filtered
   to profiles that have any `BitsSubsRules`.
6. **Editor pane** — 480px right-docked slide-in with the same open /
   save / cancel snapshot pattern as `UniversalTriggersManagerViewModel`.

### 6.2 The `AvatarSwapCardViewModel` card

Mirrors `AvatarSetCardViewModel`. Fixed size 280×320, 6px left status stripe
(Ready / Warn / Off), 200px hero avatar image with placeholder fallback
(`🎭` + "Pick Avatar"), the avatar name as the title, the return-avatar
hint as the subtitle, pills for rule count and "Channel Points" / "Bits +
Subs" mode, and an Edit button. The entire card is a click target that
opens the editor.

### 6.3 The editor

When the user opens a card, the right pane shows:

1. **Avatar header** — target avatar image (large) + name + "Pick Different
   Avatar" / "Use Current Avatar" buttons.
2. **Return Avatar** — per-profile override picker. "Use Global" / "Use
   Custom" / "Same as Target" radio group driven by the
   `ReturnAvatarMode` enum on the profile. When `UseGlobal` is selected,
   `ReturnAvatarId` is null and the manager's banner return avatar is
   used at runtime. When `UseCustom` is selected, the picker + name field
   are enabled. When `SameAsTarget` is selected, the timed-swap becomes
   a one-way swap (no auto-return) — this is the new way to express
   cooldown-only mode at the profile level.
3. **Channel Point Swaps** — `ItemsControl` of `TriggerRule` rows (kept as
   `TriggerRule` to avoid model churn). Each row shows the rule title,
   cost, cooldown, "Channel Points" pill, and 🗑 button. "Add Rule" button
   pushes a slide-in sub-editor (the existing rule editor reused) and on
   save appends the rule to `ChannelPointRules` with
   `ActionType=AvatarChange` and `AvatarChangeTargetId=TargetAvatarId`.
4. **Bits + Subs Swaps** — same shape, different row. Each row supports a
   "Trigger Type" combo (Bits / Subscription / Gift Sub / Follow) and a
   "Minimum" / "Maximum" field. The rule's `ActionType=AvatarChange` and
   `AvatarChangeTargetId=TargetAvatarId` (this is the same field the
   existing `GlobalOverrideRules` use; it is the override-equivalent of
   the channel-point target). The rule's `MinimumBits` /
   `MinimumMonths` / `MaximumMonths` are surfaced for editing in this
   row.
5. **Delete profile** (red, footer-left) and **Save** (accent, footer-right).

### 6.4 Removed from MainWindow.xaml

- The "Avatar Change Setup" tab (around lines 3515–4200) and its help text.
- The "Add Avatar Change Override" button + "Avatar Change Override Rules"
  list (around lines 4280–4310).
- The `ShowMasterAvatarTabCommand` plumbing (the command itself is removed
  from `MainWindowViewModel`).
- The individual rule editor's `UsesAvatarChange` action block (around
  lines 8825–8861) — Avatar Swap rules now live in the manager.
- `MainWindowViewModel.AddAvatarChangeOverrideCommand` and the
  `OpenAvatarPickerCommand` `"AvatarChange"` branch (the picker command
  keeps its `"Profile"` / `"PowerUp"` / `"Supporter"` branches for the
  other features that still need it).

### 6.5 Added to MainWindow.xaml

- A single "Avatar Swap" button in the Avatar Actions group of the Redeem
  Library right column (alongside "Avatar Sets" and "Avatar Scaling"),
  bound to `OpenAvatarSwapManagerCommand`. Same `RuleLibraryTabButtonStyle`.
- `MainWindowViewModel.OpenAvatarSwapManagerCommand` — lazily creates
  and opens `AvatarSwapManagerWindow` with `Owner = MainWindow`, clears
  the reference on close (same pattern as
  `OpenUniversalTriggersManagerCommand`).

## 7. Migration Plan

Runs **once at app startup**, in `SettingsStore.LoadAsync` (or a new
`MigrateAvatarChangeToAvatarSwapAsync` helper called from there).

**Step 1 — Read the old return avatar.**
- If `Settings.MasterAvatarProfile?.AvatarId` is set, copy it into
  `Settings.MasterAvatarSwapReturnId` / `MasterAvatarSwapReturnName`.

**Step 2 — Group existing channel-point Avatar Change rules.**
- Walk `Settings.MasterAvatarProfile.ChannelPointRules`.
- For each rule where `ActionType == OscActionType.AvatarChange` and
  `AvatarChangeTargetId` is non-empty:
  - Find or create an `AvatarSwapProfile` whose `TargetAvatarId` matches.
  - Append the rule to that profile's `ChannelPointRules`.

**Step 3 — Group existing Bits + Subs override rules.**
- Walk `Settings.GlobalOverrideRules`.
- For each rule where `ActionType == OscActionType.AvatarChange` and
  `AvatarChangeTargetId` is non-empty:
  - Find or create the matching `AvatarSwapProfile`.
  - Append the rule to that profile's `BitsSubsRules`.

**Step 4 — Persist a migration marker.**
- `Settings.AvatarChangeToAvatarSwapMigrationVersion` (int). Bump to `1`
  on first successful migration. Skip the migration on subsequent loads.

**Step 5 — Leave the old fields in place for one release.**
- `Settings.MasterAvatarProfile`, `Settings.GlobalOverrideRules`, and the
  `OscActionType.AvatarChange` enum value stay functional so users with
  existing setups don't lose anything. The per-rule editor's
  `UsesAvatarChange` action block is removed (per Section 3) but the
  rules themselves still fire at runtime through the existing
  `ResolveAvatarChangeAction` path. A future release can remove the
  legacy fields once the migration is verified stable in the field.

## 8. Localization

- Add new keys in every language (base + `.extra.json`):
  - "Avatar Swap" — the manager window title and primary label.
  - "Avatar Swap Manager | Crystal Relay" — window title.
  - "Return Avatar" — the header banner label.
  - "Channel Point Swaps" / "Bits + Subs Swaps" — section headers.
  - "New Swap" / "Add Swap" — primary action buttons.
  - "Use Global" / "Use Custom" / "Same as Target" — return avatar
    radio labels.
  - "Avatar Swap Card Edit" / "Avatar Swap Card Pick Avatar" — card
    controls.
  - "Pick the Return Avatar first so timed Avatar Swap and Avatar
    Roulette redeems know the exact VRChat avatar ID to switch back to.
    If this is wrong, timed avatar switches cannot return correctly."
    — banner empty-state hint.
  - "Avatar Swap was renamed from Avatar Change. The old "Avatar Change
    Setup" tab and the "Avatar Change Override" lane in Supporter
    Overrides are now reachable through this new window." — first-run
    one-time notice.
- Update existing keys:
  - "Avatar Change Setup" is renamed to "Avatar Swap" in every language.
    To avoid JSON churn, **re-use the same key slot** in each `.json`
    file and update the value in place. (Example: the key
    `"Avatar Change Setup"` in `en-US.json` keeps its name; the value
    becomes `"Avatar Swap"`.)
  - "Pick Avatar Roulette Pool" stays as is.
  - "Avatar Change" inside the rule editor's action-type combo stays
    as "Avatar Change" because that combo is still rendered for
    non-Avatar-Swap rule paths (Power-ups, Cash Payments, etc.).
- Run the `LocalizationAudit` project at the end. The audit must pass
  with no empty values and no placeholder copies in the renamed
  strings.

## 9. File-Level Change List

### New files
- `VrcTwitchOscBridge/Models/AvatarSwapProfile.cs`
- `VrcTwitchOscBridge/ViewModels/AvatarSwapCardViewModel.cs`
- `VrcTwitchOscBridge/ViewModels/AvatarSwapManagerViewModel.cs`
- `VrcTwitchOscBridge/AvatarSwapManagerWindow.xaml`
- `VrcTwitchOscBridge/AvatarSwapManagerWindow.xaml.cs`
- `VrcTwitchOscBridge/Services/AvatarSwapMigrationService.cs`
  (runs the one-time upgrade from `MasterAvatarProfile` / `GlobalOverrideRules`
  into `AvatarSwapProfiles`).
- `VrcTwitchOscBridge/Converters/AvatarSwapConverters.cs`
  (any small converters specific to the new manager — likely just a
  string-formatter for "X swap rules").

### Modified files
- `VrcTwitchOscBridge/VrcTwitchOscBridge.csproj` — register the new XAML +
  code-behind + VMs + model.
- `VrcTwitchOscBridge/Models/AppSettings.cs` — add `AvatarSwapProfiles`,
  `MasterAvatarSwapReturnId`, `MasterAvatarSwapReturnName`,
  `AvatarChangeToAvatarSwapMigrationVersion`.
- `VrcTwitchOscBridge/Models/AvatarTriggerProfile.cs` — no changes (out of
  scope; only read from `MasterAvatarProfile` for migration).
- `VrcTwitchOscBridge/Services/SettingsStore.cs` — add
  `AvatarSwapProfiles` round-trip; call
  `AvatarSwapMigrationService.MigrateAsync` from `LoadAsync`.
- `VrcTwitchOscBridge/Services/BridgeRuntimeConfiguration.cs` — add
  `AvatarSwapProfileSnapshot` record so the runtime can read the new
  structure. Keep the existing `TriggerRuleSnapshot` flow so all
  non-migrated `ActionType=AvatarChange` rules still execute.
- `VrcTwitchOscBridge/Services/BridgeCoordinator.cs` — add a
  `ResolveAvatarSwapAction` path that walks `AvatarSwapProfiles` and
  resolves the return avatar using the profile override or the global
  banner avatar. Keep the existing `ResolveAvatarChangeAction` for
  rules that haven't been migrated yet.
- `VrcTwitchOscBridge/MainWindow.xaml` — remove the "Avatar Change Setup"
  tab, the "Avatar Change Override" lane, and the per-rule
  `UsesAvatarChange` action block. Add the "Avatar Swap" button in the
  Avatar Actions group of the Redeem Library.
- `VrcTwitchOscBridge/MainWindowViewModel.cs` — add
  `OpenAvatarSwapManagerCommand`, remove `ShowMasterAvatarTabCommand`,
  `AddAvatarChangeOverrideCommand`, and the
  `OpenAvatarPickerCommand` `"AvatarChange"` branch. Keep
  `UseCurrentAvatarForAvatarChangeRuleCommand` and the
  `OpenAvatarPickerCommand` for `"Profile"` / `"PowerUp"` / `"Supporter"`
  branches (those still serve Avatar Sets, Power-ups, and Supporter
  rules).
- `VrcTwitchOscBridge/CHANGELOG.txt` — beta entry.
- `VrcTwitchOscBridge/RELEASE-CHANGE-RECORD.txt` — scratchpad entry.
- Every `Localization/*.json` and `Localization/*.extra.json` file —
  rename "Avatar Change" → "Avatar Swap" where the string refers to the
  user-facing redeem library (not the rule editor's action combo).

### Untouched (read-only reference)
- `VrcTwitchOscBridge/AvatarPickerWindow.xaml` + `.xaml.cs` (reuse as-is).
- `VrcTwitchOscBridge/Services/AvatarImageService.cs` (reuse as-is).
- `VrcTwitchOscBridge/Services/AvatarPickerService.cs` (reuse as-is).
- `VrcTwitchOscBridge/AvatarSetsManagerWindow.xaml` (out of scope).
- `VrcTwitchOscBridge/UniversalTriggersManagerWindow.xaml` (out of scope).
- `VrcTwitchOscBridge/Services/ManagedRewardPresentation.cs` (out of scope).
- All VRChat LocalLow files (read-only inputs).

## 10. Risks & Considerations

- **Save-format churn.** The migration writes a new collection on first
  load. If a user rolls back to the previous version, they will lose the
  grouping but the underlying rules are still there. Mitigated by leaving
  `MasterAvatarProfile` and `GlobalOverrideRules` in place.
- **Two places for the same concept.** While the migration marker is
  below the version that removes the old surfaces, the same Avatar Change
  rule can be edited from either the new manager or the old
  `MasterAvatarProfile` tab. This is acceptable for one release but
  should be cleaned up.
- **Localization regressions.** Renaming a public string affects every
  language file. Run `LocalizationAudit` and inspect for empty values,
  placeholder copies, and missing keys.
- **Twitch API call patterns.** No new Twitch API calls are introduced.
  The new manager reuses the existing `CreateManagedRewardTarget` flow
  for each rule in `ChannelPointRules`. Verified that
  `MainWindowViewModel.SynchronizeManagedChannelPointRewardsAsync`
  already iterates over every rule regardless of where it lives.
- **Build / XAML compile issues.** New XAML must be added to the
  `<Page>` section of `VrcTwitchOscBridge.csproj` (existing project
  rules have `EnableDefaultItems=false`). The skill rule applies.

## 11. Testing Approach

- **Unit-style smoke tests** — none today, but the existing
  `VrcTwitchOscBridge.Tests` project (referenced in
  `VrcTwitchOscBridge.slnx`) can host a small test that:
  1. Builds an `AppSettings` with a `MasterAvatarProfile` containing
     three Avatar Change rules and a `GlobalOverrideRules` with one
     Avatar Change override.
  2. Runs `AvatarSwapMigrationService.MigrateAsync`.
  3. Asserts the resulting `AvatarSwapProfiles` has one profile per
     unique target, with the expected rules in `ChannelPointRules`
     and `BitsSubsRules`.
  4. Asserts the migration marker is set to `1`.
- **Build check** — `dotnet build VrcTwitchOscBridge.csproj --no-restore`
  after the change.
- **Localization audit** — run the `LocalizationAudit` project.
- **Manual smoke test** — `Launch-Crystal-Relay-Debug.bat`, then:
  1. Confirm the new "Avatar Swap" button is in the Redeem Library.
  2. Open the manager, confirm the Return Avatar banner shows.
  3. Add a new swap via the picker, save, and confirm the card appears.
  4. Confirm a pre-existing Avatar Change rule (created before the
     migration) still appears in its profile's `ChannelPointRules` or
     `BitsSubsRules` after migration, and still fires at runtime
     through the existing `ResolveAvatarChangeAction` path.
  5. Restart the app and confirm the migration marker prevents
     re-migration.

## 12. Out-of-Scope Followups (Not Part of This Spec)

- Removing the `OscActionType.AvatarChange` enum value or renaming it to
  `OscActionType.AvatarSwap`.
- Removing `MasterAvatarProfile`, `GlobalOverrideRules`, or the
  per-rule editor's `AvatarChange` action block (the action block is
  removed in this spec; the underlying data fields stay).
- Folding "Avatar Roulette" pool rules into the same manager (they are
  a related but distinct concept and stay on `OscActionType.AvatarRoulet`).
- A potential future "Save Transfer" round-trip check for the
  `AvatarSwapProfiles` collection.

---

**Pending:** user review of this spec, then `writing-plans` skill to
produce the implementation plan.
