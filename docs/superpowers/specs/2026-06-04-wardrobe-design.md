# Wardrobe System Design

**Date:** 2026-06-04
**Status:** Draft
**Author:** Crystal Dev

## Overview

The Wardrobe system extends the existing Avatar Set outfit system with named outfits, multi-parameter snapshots (Bool, Int, Float), auto-capture restore, global cooldown, and flexible trigger support (Channel Points, Bits, Subs, Chat Commands, Follows, Gift Subs). It replaces the old numbered Set Trigger outfit choices when toggled on, preserving all existing data.

## Architecture

### Toggle Pattern

`AvatarTriggerProfile` gains a `UseWardrobeMode` boolean. When `true`:
- Old `ChannelPointRules` with `ActionType == SetTrigger` are hidden in the UI but NOT deleted
- Wardrobe editor panel replaces the old outfit choices UI
- Runtime ignores old Set Trigger outfit rules for this profile
- When toggled back to `false`, old rules reappear, Wardrobe data is preserved

### New Models

#### `WardrobeOutfit` (`Models/WardrobeOutfit.cs`)

```csharp
public sealed class WardrobeOutfit : ObservableObject
{
    Guid Id
    string Name                          // Free-form: "Cyberpunk Look"
    bool IsEnabled
    int ActiveTimeSeconds                // Duration before auto-restore
    ObservableCollection<WardrobeSnapshotParam> SnapshotParams
    string TwitchRewardId                // Individual reward (optional)
    string TwitchRewardTitle
    TwitchRewardSyncMode TwitchRewardSyncMode
    string ChatCommandText               // Optional chat command fallback
    string DisplaySummary                // Computed: "Cyberpunk Look (5 params)"
}
```

#### `WardrobeSnapshotParam` (`Models/WardrobeSnapshotParam.cs`)

```csharp
public sealed class WardrobeSnapshotParam : ObservableObject
{
    Guid Id
    string ParameterName                 // "VRCEmote"
    OscParameterType ParameterType       // Bool, Int, Float
    string SetValue                      // "7"
}
```

#### Changes to `AvatarTriggerProfile` (`Models/AvatarTriggerProfile.cs`)

```csharp
bool UseWardrobeMode
int WardrobeCooldownSeconds              // Global cooldown for the wardrobe
ObservableCollection<WardrobeOutfit> WardrobeOutfits

// Master Wardrobe Reward (optional)
bool UseWardrobeMasterReward
string WardrobeMasterRewardId
string WardrobeMasterRewardTitle
int WardrobeMasterRewardCost
TwitchRewardSyncMode WardrobeMasterRewardSyncMode
int WardrobeMasterRewardCooldownSeconds
string WardrobeMasterRewardReadyColor
string WardrobeMasterRewardCooldownColor
```

### Runtime Snapshots

#### `WardrobeOutfitSnapshot` (`Services/BridgeRuntimeConfiguration.cs`)

```csharp
public sealed record WardrobeOutfitSnapshot(
    Guid Id,
    bool IsEnabled,
    string Name,
    Guid AvatarProfileId,
    string AvatarId,
    int ActiveTimeSeconds,
    int CooldownSeconds,
    IReadOnlyList<WardrobeParamSnapshot> Params,
    bool UsesMasterReward);

public sealed record WardrobeParamSnapshot(
    string ParameterName,
    OscParameterType ParameterType,
    string SetValue);
```

### Active Time vs Global Cooldown

- **Active Time** (`WardrobeOutfit.ActiveTimeSeconds`) — Per-outfit duration. How long the outfit stays applied before auto-restoring captured values.
- **Global Cooldown** (`AvatarTriggerProfile.WardrobeCooldownSeconds`) — Profile-level lock. After ANY outfit fires, the entire Wardrobe is locked for this duration. No other outfit can fire until the cooldown expires.

### ViewModel

Wardrobe UI state is managed directly in `MainWindowViewModel` alongside existing Avatar Set state. No separate ViewModel file is needed. `WardrobeOutfit` and `WardrobeSnapshotParam` are `ObservableObject` subclasses that bind directly to the XAML editor.

## Runtime Execution Flow

### WardrobeExecutorService (`Services/WardrobeExecutorService.cs`)

Single responsible service for the full Wardrobe lifecycle:

1. **Trigger fires** — Twitch redeem, chat command, bits, subs, follow, gift sub
2. **Check global cooldown** — If `WardrobeCooldownSeconds > 0` and cooldown active → reject
3. **Validate params** — Check ALL params exist on current VRChat avatar via `VrChatLocalOscCacheService`
   - If ANY missing → block entirely, log warning, show bridge status message
4. **Auto-capture** — Read current values for ALL snapshot params from `VrChatLocalOscCacheService`
5. **Apply snapshot** — Send all params via `OscRouterService.SendParameterAsync()`
6. **Start restore timer** — `ActiveTimeSeconds` countdown
7. **On timeout** — Restore captured values via `OscRouterService`
8. **Start global cooldown** — If `WardrobeCooldownSeconds > 0`

### Concurrency (Independent Snapshots)

If Outfit A fires, then Outfit B fires before A's timeout:
- A's restore timer is cancelled
- B captures current values (which includes A's applied params)
- B applies its snapshot
- B's restore timer takes over
- When B's timer fires, it restores B's captured values

### Master Reward Flow

When `UseWardrobeMasterReward = true`:
- Single Twitch reward created/linked on the profile
- Viewer redeems and types outfit name in redemption input
- `BridgeCoordinator` matches input against `WardrobeOutfit.Name` (case-insensitive, trimmed)
- Match found → fire that outfit
- No match → log warning, do nothing

### Twitch Reward Sync

- Individual outfits: each can have its own Twitch reward (CreateOrManage or LinkExisting)
- Master reward: optional single reward for typed selection
- Rewards are synced through the existing `ManagedRewardSyncService` pipeline
- Reward titles use the outfit name for individual rewards
- Master reward title is user-configurable

## UI Design

### Toggle Location

Checkbox `Use Wardrobe Mode` in the Avatar Set header, alongside existing options like `Use Shared Numbered Outfit Reward`.

### Wardrobe Editor Panel

Replaces the old outfit choices tab content when toggle is on:

- **Add Outfit** button at top
- **Expandable outfit cards** with:
  - Name field
  - Active Time (seconds)
  - Twitch Reward sync selector (Create / Link / None)
  - Chat Command field
  - Parameter list table: Parameter Name (dropdown), Type (auto-detected), Value, Delete button
  - Add Parameter button
- **Global Wardrobe Cooldown** field
- **Master Wardrobe Reward** checkbox + settings panel (mirrors existing Set Trigger Master Reward UI)

### Parameter Discovery

- Dropdown populated from `VrChatLocalOscCacheService` cached params for the current avatar
- Type auto-detected from cached parameter metadata
- User picks parameter → type fills automatically → user sets value

## Persistence

### SettingsStore

- `WardrobeOutfits` serialized alongside `ChannelPointRules` in `AvatarTriggerProfile`
- `WardrobeSnapshotParam` uses simple DTO pattern for JSON serialization
- No migration needed — `UseWardrobeMode` defaults to `false`, existing profiles unchanged

### Migration

- No data migration required
- Old Set Trigger outfit rules are preserved when `UseWardrobeMode = false`
- Toggling to Wardrobe mode hides old rules but does not delete them
- Toggling back restores them

## Error Handling

| Scenario | Behavior |
|----------|----------|
| Parameter missing on avatar | Block entire outfit, log warning, show bridge status |
| VRChat not connected | Block, show "VRChat not connected" status |
| Avatar cache empty | Block, show "Avatar parameter cache not available" |
| Twitch reward sync fails | Show error, outfit still fires locally |
| Global cooldown active | Reject trigger, show "Wardrobe on cooldown" status |
| No outfits defined | Show "Add an outfit to get started" empty state |
| Empty outfit (no params) | Block, show "Add at least one parameter" |

## Testing

- Unit tests for `WardrobeExecutorService` capture/apply/restore cycle
- Unit tests for parameter validation against avatar cache
- Unit tests for concurrency (A fires, B fires, A cancelled)
- Unit tests for master reward name matching
- Integration test: full flow from Twitch redeem to OSC send to restore
- Localization audit for all new UI strings

## Files Changed

### New Files
- `Models/WardrobeOutfit.cs`
- `Models/WardrobeSnapshotParam.cs`
- `Services/WardrobeExecutorService.cs`

### Modified Files
- `Models/AvatarTriggerProfile.cs` — Add Wardrobe properties
- `Services/BridgeRuntimeConfiguration.cs` — Add snapshot records + conversion methods
- `Services/BridgeCoordinator.cs` — Add Wardrobe trigger resolution + execution
- `Services/SettingsStore.cs` — Add Wardrobe persistence
- `ViewModels/MainWindowViewModel.cs` — Add Wardrobe UI commands + state
- `MainWindow.xaml` (or relevant XAML) — Add Wardrobe editor panel
- `LocalizationAudit/` — Add new UI string keys
