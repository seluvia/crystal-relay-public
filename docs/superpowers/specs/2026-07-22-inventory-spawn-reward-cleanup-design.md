# Inventory Spawn Reward Cleanup Design

## Purpose

Add the same opt-in inactive Twitch reward cleanup control used by Crystal Relay's other managed channel-point systems to each Inventory Item Spawn rule.

This change is limited to managed-reward lifecycle behavior. It does not change VRChat inventory spawning, item eligibility, thumbnail decoding, or linked-existing reward behavior.

## User Experience

The Inventory Item Spawn editor shows a localized `Delete reward when inactive` checkbox below the rule's `Enabled` checkbox.

- The checkbox is visible only when `Sync Mode` is `Create & Manage`.
- It is hidden for `Link Existing`, because linked Twitch rewards are listen-only.
- The setting defaults to off for new and existing rules.
- Turning it on opts that individual rule into the existing managed-reward cleanup behavior.

## Data Model And Persistence

`InventoryItemSpawnRule` gains a `DeleteManagedRewardWhenInactive` boolean property backed by `ObservableObject.SetProperty`.

The property defaults to `false`. It is stored with the rest of the rule through the existing settings serialization flow. Older saves that do not contain the property load it as `false` without migration code.

## Reward Sync Behavior

`CreateManagedRewardTargetForInventorySpawnRule` passes the saved opt-in value to `ManagedRewardSyncTarget.deleteWhenInactive`.

Cleanup follows the existing managed-reward safety rules:

- Only Crystal Relay-owned `Create & Manage` rewards may be deleted.
- Linked-existing rewards are never deleted or otherwise mutated.
- Inactive-rule deletion runs only during the existing `Maintenance` and `ManualCleanup` sync reasons.
- Ordinary settings synchronization may identify an inactive reward but must suppress deletion when the existing sync policy disallows it.
- Missing Twitch permissions, an unavailable reward-management session, and transient catalog failures preserve the saved reward ID.
- Cooldowns and temporary runtime disable states do not delete the reward.
- A disabled rule with the option enabled is eligible for deliberate inactive cleanup.
- A rule with the option disabled keeps its Twitch reward disabled or hidden according to the normal visibility flow.

Direct deletion of an Inventory Item Spawn rule uses the existing managed-reward retirement path only when `DeleteManagedRewardWhenInactive` is enabled. The retirement path must remain limited to managed rewards and must not operate on linked-existing rewards. Deleting a rule with the option disabled leaves its Twitch reward in place.

## UI And Localization

The editor reuses the existing `Delete reward when inactive` localization key and themed `CheckBox` styling. This change adds no new user-facing string.

The visibility trigger follows the Universal Triggers pattern: show the checkbox only for `Create & Manage`. The control remains inside the editor scroll area so it does not affect the window's minimum size.

## Testing

Regression coverage must verify:

- New rules default the option to off.
- The XAML binds the checkbox to `SelectedRule.DeleteManagedRewardWhenInactive`.
- The checkbox is shown only for `Create & Manage`.
- The inventory managed-reward target receives the opt-in value.
- Linked-existing rewards remain listen-only regardless of the saved boolean.
- Cooldown and temporary-disable states do not make an inventory reward eligible for deletion.
- Direct rule removal retires only an owned managed reward whose cleanup option is enabled.
- The app project builds and the focused Inventory Item Spawn tests pass.

## Out Of Scope

- Making non-cloneable VRChat inventory items spawn through the pedestal API.
- Consuming VRChat inventory spawn tokens in the game client.
- Changing Twitch reward visibility while Crystal Relay is closed.
- Aggressive deletion during every hidden, offline, cooldown, or transient runtime state.
