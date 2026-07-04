# Avatar Scaling Manager Redesign Design

## Status

Approved for implementation planning.

## Goal

Replace the current inline Avatar Scaling workspace with a dedicated, streamer-friendly Avatar Scaling manager window. The new manager should make reward scaling easier to understand, isolate Twitch reward setup from other scaling sources, and preserve the working runtime behavior for Twitch rewards, Supporter Growth, Cash Payments, and Power Ups.

## Non-Goals

- Do not build a global manager for every reward, cash payment, Power Up, or Bits/Subs rule in Crystal Relay.
- Do not move non-scaling Cash Payment or Power Up rules into this manager.
- Do not rewrite the runtime trigger engine or Twitch managed reward sync from scratch.
- Do not delete, rename, or migrate existing saved scale rules unless required to add the shared safety setting safely.

## Existing Context

Avatar Scaling is currently edited inside `MainWindow.xaml` as a long inline workspace. It owns the master reward editor, scale set list, scale redeem list, trigger setup, Supporter Growth settings, and scale action controls in one stacked page.

Universal Triggers already uses a better pattern: `UniversalTriggersManagerWindow` plus `UniversalTriggersManagerViewModel`, with a dedicated themed window, search/filter controls, cards, status pills, and a right-side slide-in editor. The Avatar Scaling redesign should follow that interaction style while keeping Avatar Scaling's existing source models and runtime behavior.

## UI Structure

Clicking `Avatar Scaling` in the main Redeem Library opens a new dedicated `AvatarScalingManagerWindow`. The old inline Avatar Scaling workspace is removed so there is one clear place to manage scaling.

The manager uses a themed custom window matching Crystal Relay and Universal Triggers. It has a left/source navigation area, a main content area, and a right-side editor panel.

Source navigation contains:

- `Twitch Rewards`
- `Supporter Growth`
- `Cash Payments`
- `Power Ups`
- `All Sources`

The `Twitch Rewards` page is the primary page. It shows:

- A global safety card at the top.
- `Current Max Height Allowed` as the visible shared cap.
- A Master Unlock Reward card.
- Reward health/status summary.
- Child scale rewards grouped by Scale Set.
- Reward cards that show what the viewer redeems, what the height action does, and the current max height allowed.

The editor remains on the right side, not below the reward list. Clicking a reward card opens the side editor. The editor sections are:

- `Twitch Reward`
- `Height Change`
- `Timer & Return`
- `Safety & Pairing`

Inputs must use readable themed contrast. Avoid dark input boxes with black text.

## Shared Safety Rule

The manager introduces a shared Avatar Scaling safety setting displayed as `Current Max Height Allowed`. This is not only a card label. It is the shared max height that all Avatar Scaling sources must follow.

Default behavior:

- Default current max height allowed is `100m`.
- Default minimum remains the existing safe minimum of `0.1m`.
- Advanced Safety can change the shared allowed range.
- The shared advanced range must stay within Crystal Relay's existing Avatar Scaling bounds: `0.01m` to `10000m`.

Settings behavior:

- New installs default to `100m` max.
- Add a shared settings object under `AppSettings`, such as `AvatarScaleSafetySettings`, with current min height and current max height fields.
- Existing saves should preserve behavior. If saved scale rules already use advanced values above `100m`, initialize the shared max height from the largest configured existing advanced height/range value, clamped to the advanced maximum. This includes configured target heights, random/range maximums, relative maximums, restore heights, and Supporter Growth configured height caps where present.
- Existing per-rule settings should not be silently deleted. Per-rule min/max and hide-at-limit behavior can remain, but they cannot exceed the shared safety range.

Runtime behavior:

- Twitch reward scale rules, Supporter Growth, Cash Payment scaling, and Power Up scaling all follow the shared safety cap.
- Cards and editors show `Current Max Height Allowed` so streamers can see the active cap without opening Advanced Safety.
- `Open Advanced Safety` edits the shared Avatar Scaling safety setting.

## Source Model Boundaries

The manager uses adapter/card view models over existing models. It does not create a new universal source persistence model for this phase.

Twitch reward scale rules:

- Backed by `AvatarScaleSet` and `AvatarScaleRule`.
- Scale Sets remain organization folders.
- Child scale rewards stay grouped by Scale Set.

Master reward:

- Backed by `AvatarScaleMasterRewardSettings`.
- Shown as a pinned card/editor on the Twitch Rewards page.

Supporter Growth:

- Presented as the primary Bits/Subs/Gift Subs scaling path.
- Backed by `AvatarScaleRule` where `TriggerType == SupporterGrowth`.
- Legacy standalone Bits/Subs/Gift Sub scale rules must still load safely. If they exist, show them in an advanced/legacy area instead of hiding or deleting them.

Cash Payment scaling:

- Show only `CashPaymentRule` entries where `ActionKind == AvatarScaling`.
- Edit their existing `ScaleAction` object.
- Non-scaling Cash Payment rules stay in the existing Cash Payments area.

Power Up scaling:

- Show only `PowerUpRule` entries where `ActionKind == AvatarScaling`.
- Edit their existing `ScaleAction` object.
- Non-scaling Power Up rules stay in the existing Power Up area.

## Interaction Flow

Main window:

- The `Avatar Scaling` button opens the dedicated manager directly.
- The button no longer switches to the old inline Avatar Scaling workspace.
- Universal Triggers behavior remains unchanged.

Manager flow:

- User picks a source from the source navigation.
- User clicks a card in the main content area.
- The right-side editor opens for that source.
- Save/test/delete actions operate on the selected source.
- The list updates card status and summary text immediately after edits.

Twitch Rewards flow:

- User sees the shared safety cap first.
- User configures the Master Unlock Reward if desired.
- User edits child scale rewards inside Scale Set sections.
- The `Safety & Pairing` editor card shows `Current max height allowed` and links to Advanced Safety.

## Save And Sync Behavior

The manager edits the same live settings objects the app already saves. It should reuse or delegate to existing save, bridge refresh, and managed reward sync paths from `MainWindowViewModel` rather than adding parallel sync logic.

Required behavior:

- Queue save after edits.
- Queue bridge refresh after runtime-affecting edits.
- Queue managed reward sync after Twitch-visible reward edits.
- Preserve linked-existing reward safety: linked Twitch rewards remain listen-only.
- Preserve delete safety: delete managed rewards remains opt-in.
- Preserve master reward gating and child reward slot freeing behavior.

## Card Statuses

Cards use clear streamer-facing statuses:

- `Ready`
- `Needs setup`
- `Disabled`

`Needs setup` can mean missing Twitch reward link, missing command text, missing permissions, invalid height/timer values, or source-specific missing fields.

Source sections should also show summary status so the streamer can spot which area needs attention without opening every card.

## Error Handling

The manager should not fail silently. If a source cannot be synced, tested, or saved, show a themed message and keep the user's saved rule data intact.

Twitch API limitations remain hard constraints:

- Do not mutate linked existing rewards.
- Do not call reward management APIs when required scopes are missing.
- Do not recreate rewards because of transient catalog failures.
- Keep managed reward sync coalesced and unchanged unless Twitch-visible state changed.

## Localization

All new user-facing strings should use the existing localization flow. Add `en-US` keys and translate them into existing non-English localization files. Run the localization audit after changes.

Important new text includes:

- `Avatar Scaling Manager`
- `Twitch Reward Scaling`
- `Current Max Height Allowed`
- `Open Advanced Safety`
- `Safety & Pairing`
- `Supporter Growth`
- `Cash Payment Scaling`
- `Power Up Scaling`
- `Needs setup`

## Verification

Implementation should verify:

- The app builds with `dotnet build "E:\!!!Program to work on\Proper Crystal Relay\VrcTwitchOscBridge\VrcTwitchOscBridge.csproj" --no-restore`.
- Localization audit passes.
- The `Avatar Scaling` main button opens the new manager.
- The old inline Avatar Scaling workspace no longer appears.
- Existing Avatar Scale Sets and rules load into the manager.
- The Master Unlock Reward can be edited and still syncs through existing managed reward behavior.
- Child reward cards show `Current Max Height Allowed`.
- Editing Advanced Safety updates all visible cards and editor safety panels.
- All scaling sources obey the shared current max height allowed at runtime.
- Test Selected still tests the selected scale rule.
- Cash Payment Scaling shows only cash payment rules whose action is Avatar Scaling.
- Power Up Scaling shows only Power Up rules whose action is Avatar Scaling.
- Non-scaling Cash Payment and Power Up rules remain in their existing areas.
- Linked existing rewards remain listen-only.
- Managed reward deletes remain opt-in.

## Implementation Notes

Likely new files:

- `AvatarScalingManagerWindow.xaml`
- `AvatarScalingManagerWindow.xaml.cs`
- `ViewModels/AvatarScalingManagerViewModel.cs`
- `ViewModels/AvatarScalingSourceCardViewModel.cs`
- A small shared settings type for Avatar Scaling safety, for example `AvatarScaleSafetySettings`.

Because `VrcTwitchOscBridge.csproj` disables default item inclusion, all new C# and XAML files must be explicitly added to the project file.

## Approved Visual Direction

The approved visual direction is the browser companion revision with:

- Reward-focused source navigation.
- Main reward list on the left.
- Right-side editor panel.
- `Safety & Pairing` card showing `Current max height allowed`.
- `Open Advanced Safety` button inside the safety card.
- Reward cards displaying `Current max height allowed`.
