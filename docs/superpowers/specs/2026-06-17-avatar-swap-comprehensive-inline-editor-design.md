# Avatar Swap — Comprehensive Inline Channel Points Editor

**Date:** 2026-06-17
**Status:** Approved during brainstorming — ready for implementation planning.

## Overview

The v3.1.10 beta 1 Avatar Swap window replaced the legacy "Avatar Change Setup" tab with a clean four-section layout (Channel Points, Bits, Subs, Payment) and a **minimal** inline rule editor (Name + Cost/Amount/Command only).

The legacy full editor (`VrcTwitchOscBridge/UserControls/AvatarSwapRuleEditorControl.xaml`) — which contained Cooldown, Active Time, Paired Rules, Managed Reward Colors, Reward Sync Mode (Create/Manage vs Link Existing), Reward Name/Cost/Description, Delete When Inactive, Chat Command, Bot Reply, and Shared/Numbered Reward Choice — was left only referenced by the old tab and is not surfaced in the new window.

This spec restores those 10 feature groups to the inline editor, matching the legacy feature set without requiring users to open a separate window.

## Decisions (locked during brainstorming)

| # | Decision | Choice |
|---|----------|--------|
| 1 | Scope | **All 10 feature groups** restored |
| 2 | Layout | **A — All visible, scrollable** (no collapsible sub-sections, no separate full-editor button) |
| 3 | Trigger-type visibility | **A — Show all 10 groups for every trigger type** (Bits/Subs/Payment/ChatCommand/Follow/Power-up all show the same fields; some are no-op for non-CP types, by design) |
| 4 | Twitch reward list integration | **A — Wire it through.** Inline "Link Existing" picker + Refresh + Unlink work inline via a constructor-injected `ITwitchRewardSource` |
| 5 | Progressive disclosure | **Shared/Numbered Choice** and **Chat Command** hide their sub-fields until their checkbox is checked (matches the existing Link Existing pattern) |
| 6 | Pairing | **Mode-only inline** (Hide/Show Paired While On Cooldown dropdown). The full paired-rules list (add/remove other rules) stays in the Full Editor with an inline note |
| 7 | Color picker | Reuse the existing WinForms `ColorDialog` pattern (Pick/Reset buttons). Handler relocated to the inline control's code-behind |
| 8 | Group order | Name → Twitch Reward → Reward Sync Mode (+ Link Existing picker) → Delete When Inactive → Shared / Numbered Choice → Timing → Pairing → Reward Colors → Chat Command → Bot Reply |
| 9 | Model changes | **None.** `TriggerRule` already exposes every required property (verified in v3.1.9 CHANGELOG). No migration, no serialization change |

## Architecture

### New interface: `ITwitchRewardSource`

A slim contract exposing only what the inline editor needs from the main window. Keeps coupling tight and testable.

```csharp
public interface ITwitchRewardSource
{
    ObservableCollection<TwitchRewardOption> RewardOptions { get; }
    ICommand RefreshTwitchRewardsCommand { get; }
    ICommand UnlinkTwitchRewardCommand { get; }
}
```

`MainWindowViewModel` implements this interface — it already owns all three members.

### `AvatarSwapManagerViewModel` changes

- New constructor parameter: `ITwitchRewardSource twitchRewardSource`.
- New public properties forwarding the source:
  - `ObservableCollection<TwitchRewardOption> TwitchRewardOptions`
  - `ICommand RefreshTwitchRewardsCommand`
  - `ICommand UnlinkTwitchRewardCommand`
- In the constructor, subscribe to the source's `RewardOptions.CollectionChanged` and the commands' `CanExecuteChanged` to forward change notifications to the UI.
- The inline row's XAML reaches these via `RelativeSource AncestorType=Window` → `DataContext.TwitchRewardOptions`, `DataContext.RefreshTwitchRewardsCommand`, `DataContext.UnlinkTwitchRewardCommand`.

### `InlineAvatarSwapRuleRowControl.xaml` changes

Add 10 group sections inside the existing expanded panel (the panel visible when `IsExpanded == True`). All bindings target the `Rule` (the underlying `TriggerRule`).

**Order and bindings:**

1. **Name** — `TextBox` bound to `Rule.Name` (already present).
2. **Twitch Reward** — Reward Name (`Rule.ChannelPointRewardTitle`), Reward Cost (`Rule.ChannelPointRewardCost`), Description (`Rule.ChannelPointRewardDescription`, multi-line `TextBox`).
3. **Reward Sync Mode** — two `RadioButton`s bound to `Rule.RewardSyncMode` (values: `CreateOrManage`, `LinkExisting`). A nested panel visible only when `LinkExisting` is selected: a `ComboBox` with `ItemsSource={Binding DataContext.TwitchRewardOptions, RelativeSource={RelativeSource AncestorType=Window}}` and `SelectedValue={Binding Rule.ChannelPointRewardId}`; plus **Refresh Rewards** and **Unlink** buttons bound to the forwarded commands with `CommandParameter={Binding}`.
4. **Delete When Inactive** — `CheckBox` bound to `Rule.DeleteManagedRewardWhenInactive`.
5. **Shared / Numbered Choice** — `CheckBox` bound to `Rule.SharedRewardChoiceEnabled`. Nested panel (visible only when checked) with `Rule.SharedRewardChoiceNumber` and `Rule.SharedRewardHelpText`.
6. **Timing** — Active Time (`Rule.DurationSeconds`), Cooldown (`Rule.CooldownSeconds`).
7. **Pairing** — `ComboBox` bound to `Rule.SpecialRulePairingMode` (values: `HidePairedWhileActive`, `ShowPairedWhileActive`). A small italic note: "Manage paired rules in the Full Editor."
8. **Reward Colors** — Ready (`Rule.ManagedRewardReadyColor`) and Cooldown (`Rule.ManagedRewardCooldownColor`) color swatches with **Pick...** and **Reset** buttons. The Pick button uses a new `OnPickManagedRewardColorClicked` click handler that walks the visual tree to find the `TriggerRule` `DataContext`, opens a `WinForms.ColorDialog`, and writes the selected hex back to `Rule.ManagedRewardReadyColor` or `Rule.ManagedRewardCooldownColor` based on `button.Tag`.
9. **Chat Command** — `CheckBox` bound to `Rule.ChatCommandEnabled`. Nested panel (visible only when checked) with `Rule.ChatCommandText` and `Rule.ChatCommandPermission` (ComboBox with Everyone / Moderators / Broadcaster options).
10. **Bot Reply** — multi-line `TextBox` bound to `Rule.BotMessageTemplate`.

All new bindings use `UpdateSourceTrigger=PropertyChanged` so changes flow to the rule live (matches the existing pattern in this control). The existing **Done** / **Cancel** buttons call `CommitInlineEditCommand` / `CancelInlineEditCommand` on the parent VM (no change).

### `InlineAvatarSwapRuleRowControl.xaml.cs` changes

- Add `OnPickManagedRewardColorClicked(object sender, RoutedEventArgs e)`. Same pattern as the existing `AvatarSwapRuleEditorControl.OnPickManagedRewardColorClicked`: resolve the rule by walking up the visual tree to the first `FrameworkElement` whose `DataContext` is a `TriggerRule`; open a `WinForms.ColorDialog` initialized with the current color; on `OK`, write the selected hex back via `ManagedRewardPresentation.ToHex(dialog.Color)`. Use `button.Tag` to distinguish "Ready" vs "Cooldown".

### `MainWindowViewModel` changes

- Add `ITwitchRewardSource` to the class declaration. All three members already exist (`RewardOptions`, `RefreshTwitchRewardsCommand`, `UnlinkTwitchRewardCommand`).

### Call-site change

- `MainWindowViewModel.OpenAvatarSwapManagerCommand` (the call site that constructs the `AvatarSwapManagerWindow`) must pass `this` (the main VM) as the new `ITwitchRewardSource` parameter to the `AvatarSwapManagerViewModel` constructor.

## Data flow

```
MainWindowViewModel
  └─ (already owns) RewardOptions  (ObservableCollection<TwitchRewardOption>)
  └─ (already owns) RefreshTwitchRewardsCommand
  └─ (already owns) UnlinkTwitchRewardCommand
       │
       │  implements ITwitchRewardSource
       ▼
AvatarSwapManagerViewModel  ── forwards ──▶  TwitchRewardOptions / Refresh / Unlink
       │
       │  Window.DataContext
       ▼
AvatarSwapManagerWindow.xaml
       │
       │  RelativeSource AncestorType=Window
       ▼
InlineAvatarSwapRuleRowControl.xaml
       │
       │  ItemsSource / Command
       ▼
ComboBox (Link Existing picker) + Refresh + Unlink buttons
```

## Error handling

| Scenario | Behavior |
|----------|----------|
| Color dialog cancel | `ColorDialog.ShowDialog` returns `Cancel`; handler early-returns; no change |
| Refresh Rewards fails (network/permission) | Existing `MainWindowViewModel.RefreshTwitchRewardsCommand` surfaces the error to the activity feed. Inline picker just doesn't update. User retries |
| Unlink with no linked reward | `UnlinkTwitchRewardCommand` is a no-op when `ChannelPointRewardId` is empty (existing behavior) |
| Invalid numeric input (cost, cooldown, active time) | `TriggerRule` setters normalize: `ChannelPointRewardCost` clamps to `Math.Max(0, value)`; `DurationSeconds` / `CooldownSeconds` / `MinimumAmount` similarly. No crash |
| Broadcaster not connected | `RewardOptions` is empty; picker shows blank; user can still pick Create/Manage or use the Full Editor |
| Fields on non-CP triggers (e.g., Reward Cost on a Bits rule) | Editable but no-op at runtime, per the "show all for all" decision. No validation, no error |

## Testing

New unit tests in `VrcTwitchOscBridge.Tests/AvatarSwapManagerViewModelTests.cs`:

- Constructor with a stub `ITwitchRewardSource` forwards `TwitchRewardOptions` (same reference).
- Forwarded `RefreshTwitchRewardsCommand` and `UnlinkTwitchRewardCommand` are the same instances from the source.
- When the source's `RewardOptions.CollectionChanged` fires, the VM's `TwitchRewardOptions` reflects the change.
- Command `CanExecuteChanged` on the source propagates to the forwarded commands.

New round-trip tests in `VrcTwitchOscBridge.Tests/TriggerRuleTests.cs` (new file, or extend an existing one):

- `SharedRewardChoiceEnabled`, `SharedRewardChoiceNumber`, `SharedRewardHelpText` round-trip through `SettingsStore` save/load.
- `DeleteManagedRewardWhenInactive`, `ChannelPointRewardDescription`, `BotMessageTemplate`, `ManagedRewardReadyColor`, `ManagedRewardCooldownColor`, `SpecialRulePairingMode` round-trip and normalize correctly.

Build + existing test suite must stay green. Localization audit: confirm no **new** failures introduced (pre-existing failures are out of scope per `AGENTS.md`).

## Out of scope

- The full paired-rules list (add/remove other rules) — stays in the Full Editor, noted inline in the Pairing section.
- Model changes / migration — none needed; `TriggerRule` already has every property.
- Widening the 420px right column — the panel scrolls vertically instead.
- Smart per-trigger-type visibility — all fields show for all types, by user decision.
- Touching the full `AvatarSwapRuleEditorControl` — left as-is (still used by the legacy Avatar Change Setup tab at `MainWindow.xaml:7432`).
- Pre-existing localization audit failures (unrelated to this change).
- A separate "Full Editor" window or slide-out panel for the inline editor (Option C from the layout brainstorm was rejected).
