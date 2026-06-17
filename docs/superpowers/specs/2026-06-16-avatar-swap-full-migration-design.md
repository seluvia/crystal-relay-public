# Avatar Swap — Rework Design

**Supersedes:** the previous design at this same path (3-section layout with per-profile `ReturnAvatarMode`). The new design splits the manager into 4 clean trigger sections, lifts Avatar Roulette to its own first-class card type, removes the per-profile return mode in favor of a single global return avatar, and introduces inline rule editing.

**Date:** 2026-06-16
**Status:** Approved
**Target version:** v3.1.10 (next post-release)
**Active build lane:** v3.1.10 development lane

---

## 1. Summary

The previous Avatar Swap rework put the manager window in place but left it messy to use:

- The right-side editor listed all of an avatar's rules in a single "Channel Point Swaps" sub-section, so users with multiple rules per avatar saw long, undifferentiated lists.
- The per-profile `ReturnAvatarMode` (UseGlobal / UseCustom / SameAsTarget) added complexity that the user does not want — the global return avatar should be used everywhere by default.
- Avatar Roulette was a 3rd section in the same card grid, visually mixed with direct swaps, even though the data model is structurally different (a pool of avatars, not a single target).
- Bits and Subs were combined in one collection, so the user could not tell at a glance which rules were Bits and which were Subs.
- Cash Payment rules showed up as a "Source" badge on a channel-point rule, which is confusing — the user thinks of them as their own category.
- Editing a rule required opening a separate full-screen editor; common fields like cost / duration / cooldown could not be tweaked in place.

This rework fixes all of that:

- The right-side editor becomes 4 distinct sections: **Channel Points / Bits / Subs / Payment**, with a tiny badge per row in Subs to distinguish regular sub from gift sub and tier.
- Avatar Roulette moves out of the regular grid into its own **🎰 Avatar Roulette** card group, with a visually distinct card style (gold-bordered, with a thumbnail strip showing the pool).
- The per-profile `ReturnAvatarMode` is removed. All swaps and roulettes return to the **single global Return Avatar** that lives at the top of the window.
- Clicking a rule row expands it **inline** in the section, showing the fields relevant to its trigger type. No separate dialog for common edits.
- Chat Command, Follow, and Power-up become **first-class trigger sources** in the Avatar Swap editor, with Chat Command and Follow opening inline like the other types, and Power-up opening the full rule editor (because of its advanced fields).
- The data model grows to support all of this: a new `AvatarRouletteProfile` model for the roulette cards, a new 4-collection `AvatarSwapProfile`, and new values in `TwitchTriggerType`.

## 2. Goals

1. **Four clean rule sections per avatar card.** Channel Points / Bits / Subs / Payment, each with a count badge and an `+ Add` button. Subs shows a per-row badge for tier or "GIFT".
2. **Roulette is its own first-class card type.** Visually distinct, with a thumbnail strip showing the pool and a trigger count. Each roulette card has its own return-avatar override (optional) and its own list of triggers (any source).
3. **Single global Return Avatar.** Lives at the top of the manager window. Every swap and every roulette returns here. No per-profile return-mode logic.
4. **Inline rule editing.** Click a rule row → it expands in place to show the relevant fields. Edit and collapse. Common fields are always visible; advanced fields live in the existing full rule editor.
5. **Chat Command, Follow, Power-up, Cash Payment as first-class trigger sources** in the Avatar Swap editor. Chat Command and Follow are inline-editable; Power-up and Cash Payment open the full rule editor.
6. **One runtime dispatch path.** `BridgeCoordinator.ExecuteRuleActionAsync` looks up the parent profile (AvatarSwapProfile or AvatarRouletteProfile) for any avatar-swap rule and calls `ResolveAvatarSwapAction` or `ResolveRouletteProfileAction`.
7. **Clean migration from v3 to v4.** Old v3 saves load without losing data. Bits+Subs splits into Bits+Subs. Old Cash Payment rules move to `PaymentRules`. Old Roulette rules become `AvatarRouletteProfile` instances. Per-profile return-mode is dropped (replaced by global).
8. **Cards are smaller.** Card grid goes from 2 columns × 240×200 to 3 columns × ~180×130. The "Return Avatar" bar is removed from the per-avatar editor (it lives in the global banner instead).

## 3. Non-Goals

- Renaming `OscActionType.AvatarChange` to `OscActionType.AvatarSwap`. The enum value, the `AvatarChangeTargetId` / `AvatarChangeResetId` fields, and the `AvatarChangeSetup` JSON key stay in place for one release as a safety net.
- Renaming the `AvatarChangeSetup` JSON key.
- Changing the Twitch reward-sync logic, the Bits + Subs override priority stack, the Avatar Scaling avatar-change blocker, the cooldown-only mode, or the Fire Sale rules. They all stay exactly as they are.
- Removing the legacy per-rule editor in `MainWindow.xaml`. It is still used for non-avatar-swap rules (OSC parameters, Set Trigger, movement) and for the Power-up / Cash Payment full editor path.
- Removing the stub `ActionRule` / `TriggerAction` from `PowerUpRule` / `CashPaymentRule`. They stay as no-op stubs.

## 4. Architecture

```
+-----------------------------------------------------+
|  AvatarSwapManagerWindow (custom-chrome)             |
|                                                     |
|   [ Global Return Avatar banner ]                   |
|     image | name | Pick | Use Current | Clear       |
|                                                     |
|   ┌──────────────────────┬────────────────────────┐  |
|   │ Avatar Swaps         │ Edit Avatar Swap       │  |
|   │ (3-col grid, 180x130)│ ┌────────────────────┐ │  |
|   │  ┌──┐ ┌──┐ ┌──┐      │ │ Target Avatar     │ │  |
|   │  │UM│ │SL│ │SV│ ...  │ │ [Browse][Current]  │ │  |
|   │  └──┘ └──┘ └──┘      │ ├────────────────────┤ │  |
|   │  ┌──┐                │ │ 🏆 Channel Points │ │  |
|   │  │+ │ add avatar     │ │   [rule row]      │ │  |
|   │  └──┘                │ │   [rule row ▼]    │ │  |
|   ├──────────────────────┤ │     inline editor │ │  |
|   │ 🎰 Avatar Roulette   │ ├────────────────────┤ │  |
|   │ (3-col grid, 180x130)│ │ 💎 Bits           │ │  |
|   │  ┌──┐ ┌──┐ ┌──┐      │ │   [rule row]      │ │  |
|   │  │🎲│ │🎲│ │+ │      │ ├────────────────────┤ │  |
|   │  └──┘ └──┘ └──┘      │ │ ⭐ Subs           │ │  |
|   │  gold border         │ │   [T1][GIFT]      │ │  |
|   │  pool thumbs         │ ├────────────────────┤ │  |
|   └──────────────────────┤ │ 💵 Payment        │ │  |
|                          │ │   [SE tip $5]     │ │  |
|                          │ ├────────────────────┤ │  |
|                          │ │ Advanced triggers │ │  |
|                          │ │  [💬 Chat][👥 Fol]│ │  |
|                          │ │  [⚡ Power-up]    │ │  |
|                          │ ├────────────────────┤ │  |
|                          │ │ [Delete]   [Save] │ │  |
|                          │ └────────────────────┘ │  |
|                          └────────────────────────┘  |
+-----------------------------------------------------+

+-----------------------------------------------------+
|  BridgeCoordinator (runtime)                        |
|                                                     |
|   Channel Point / Bits / Sub / Chat / Follow / etc.  |
|       |                                             |
|       v                                             |
|   ExecuteRuleActionAsync(rule)                      |
|       |                                             |
|       v                                             |
|   Find parent profile (AvatarSwapProfile OR         |
|                       AvatarRouletteProfile)        |
|       |                                             |
|       v                                             |
|   ResolveAvatarSwapAction(profile, rule)            |
|       OR                                            |
|   ResolveRouletteProfileAction(roulette, rule)      |
|       |                                             |
|       v                                             |
|   /avatar/change <target> + /avatar/change <return> |
+-----------------------------------------------------+
```

## 5. Data Model

### 5.1 `AvatarSwapProfile` (replaces today's 3-collection model)

`VrcTwitchOscBridge/Models/AvatarSwapProfile.cs` — restructured:

```csharp
public class AvatarSwapProfile : ObservableObject
{
    public Guid Id { get; set; }
    public string TargetAvatarId { get; set; }
    public string TargetAvatarName { get; set; }
    public string? TargetThumbnailUrl { get; set; }
    public bool IsEnabled { get; set; }
    public DateTime CreatedAt { get; set; }
    public DateTime UpdatedAt { get; set; }

    // ReturnAvatarMode / ReturnAvatarId / ReturnAvatarName REMOVED.
    // All swaps return to the global Return Avatar (Settings.MasterAvatarSwapReturnId).

    public ObservableCollection<TriggerRule> ChannelPointRules { get; } = new();
    public ObservableCollection<TriggerRule> BitsRules { get; } = new();
    public ObservableCollection<TriggerRule> SubsRules { get; } = new();
    public ObservableCollection<TriggerRule> PaymentRules { get; } = new();
    // BitsSubsRules and RouletteRules are REMOVED.

    // Computed
    public string AvatarSubtitle =>
        $"{ChannelPointRules.Count} cp · {BitsRules.Count} bits · {SubsRules.Count} subs · {PaymentRules.Count} pay";
    public bool HasRules => ChannelPointRules.Count + BitsRules.Count + SubsRules.Count + PaymentRules.Count > 0;
    public bool UsesChannelPointRules => ChannelPointRules.Count > 0;
    public bool UsesBitsRules => BitsRules.Count > 0;
    public bool UsesSubsRules => SubsRules.Count > 0;
    public bool UsesPaymentRules => PaymentRules.Count > 0;
}
```

### 5.2 `AvatarRouletteProfile` (new)

`VrcTwitchOscBridge/Models/AvatarRouletteProfile.cs` — new file:

```csharp
public class AvatarRouletteProfile : ObservableObject
{
    public Guid Id { get; set; }
    public string Name { get; set; } = "New Roulette";
    public bool IsEnabled { get; set; }
    public DateTime CreatedAt { get; set; }
    public DateTime UpdatedAt { get; set; }

    // The avatar pool.
    public ObservableCollection<RouletteAvatarEntry> Pool { get; } = new();
    // (RouletteAvatarEntry: AvatarId, AvatarName, ThumbnailUrl — mirrors AvatarImageService)

    // Optional override for the return avatar; null = use global.
    public string? ReturnAvatarId { get; set; }
    public string? ReturnAvatarName { get; set; }

    // Triggers (any source: ChannelPoint, Bits, Sub, ChatCommand, Follow, PowerUp, CashPayment).
    public ObservableCollection<TriggerRule> Triggers { get; } = new();

    // Computed
    public int TriggerCount => Triggers.Count;
    public int PoolCount => Pool.Count;
    public string Subtitle => $"🎲 {PoolCount} pool · {TriggerCount} trigger{(TriggerCount == 1 ? "" : "s")}";
}
```

### 5.3 `TwitchTriggerType` expansion

`VrcTwitchOscBridge/Models/TwitchTriggerType.cs` — adds 3 new values:

```csharp
public enum TwitchTriggerType
{
    ChannelPoints,
    Bits,
    Subscriptions,         // regular sub
    GiftSubscription,      // NEW — gift sub. Badge "GIFT" in UI.
    PowerUp,
    ChatCommand,           // NEW
    Follow,                // NEW
}
```

`CashPayment` is **NOT** a `TwitchTriggerType` value. Cash payments are webhook-driven, not Twitch events, and the existing `TriggerRule.Source == TriggerRuleSource.CashPayment` field already identifies them. The manager's "Payment" section is just a filter on `Source = CashPayment` (and stores them in `PaymentRules` for clean collection access).

**Subs tier distinction** stays on `TriggerRule` itself: `SubscriptionTier1SecondsPerSub`, `SubscriptionTier2SecondsPerSub`, `SubscriptionTier3SecondsPerSub`. A regular sub rule has `TriggerType = Subscriptions`; a gift sub rule has `TriggerType = GiftSubscription` and `IsGiftSubscription = true`. The UI badge per row in the Subs section shows the trigger kind ("T1", "T2", "T3", or "GIFT x5").

### 5.4 `TriggerRule` extensions

Add 2 new fields to `VrcTwitchOscBridge/Models/TriggerRule.cs`:

```csharp
public string? PowerUpId { get; set; }                  // existing in old design, preserve
public string? CashPaymentRuleId { get; set; }           // existing in old design, preserve
public bool IsGiftSubscription { get; set; }             // NEW — set on rules with TriggerType=GiftSubscription
```

### 5.5 `AppSettings` changes

- `ObservableCollection<AvatarSwapProfile> AvatarSwapProfiles` — already in place, model changes propagate.
- `ObservableCollection<AvatarRouletteProfile> AvatarRouletteProfiles` — **NEW**.
- `string? MasterAvatarSwapReturnId` / `MasterAvatarSwapReturnName` — already in place, now the only return avatar in the system.
- `int AvatarChangeToAvatarSwapMigrationVersion` — bump from `3` to `4`.
- `bool AvatarSwapMigrationNoticeShown` — already in place.
- `ReturnAvatarMode` settings (if any) — **REMOVED**. Per-profile return-mode is gone.

### 5.6 `AvatarSwapProfileSnapshot` and new `AvatarRouletteProfileSnapshot`

`VrcTwitchOscBridge/Services/BridgeRuntimeConfiguration.cs`:

```csharp
public record AvatarSwapProfileSnapshot(
    Guid Id,
    string TargetAvatarId,
    string TargetAvatarName,
    string? TargetThumbnailUrl,
    bool IsEnabled,
    IReadOnlyList<TriggerRuleSnapshot> ChannelPointRules,   // was 1 list of "ChannelPointRules"
    IReadOnlyList<TriggerRuleSnapshot> BitsRules,           // NEW
    IReadOnlyList<TriggerRuleSnapshot> SubsRules,           // NEW
    IReadOnlyList<TriggerRuleSnapshot> PaymentRules         // NEW
);

public record AvatarRouletteProfileSnapshot(                // NEW
    Guid Id,
    string Name,
    bool IsEnabled,
    IReadOnlyList<RouletteAvatarEntrySnapshot> Pool,
    string? ReturnAvatarId,                                 // null = use global
    IReadOnlyList<TriggerRuleSnapshot> Triggers
);

public record RouletteAvatarEntrySnapshot(
    string AvatarId,
    string AvatarName,
    string? ThumbnailUrl
);
```

The lookup index becomes:

```csharp
Dictionary<TriggerRuleSnapshot, AvatarSwapProfileSnapshot> _ruleToSwapProfile;
Dictionary<TriggerRuleSnapshot, AvatarRouletteProfileSnapshot> _ruleToRouletteProfile;
```

`FindAvatarSwapProfileForRule` (existing) extends to consult both dictionaries.

## 6. UI Design

### 6.1 `AvatarSwapManagerWindow.xaml` — full re-layout

**Top banner — Global Return Avatar:**

```
┌────────────────────────────────────────────────────────┐
│ ↩ RETURN AVATAR (used by all swaps + roulettes)        │
│ ┌──┐  Seluvia - Fitness Coach New Shading              │
│ │📷│  [Pick…]  [Use Current]  [Clear]                  │
│ └──┘                                                    │
└────────────────────────────────────────────────────────┘
```

**Left card grid — two sections:**

Section 1: **Avatar Swaps** (purple-bordered cards, 3 columns, ~180×130)
- Each card: 64px hero image, avatar name, subtitle "2 cp · 1 bits · 2 subs · 1 pay", Ready/Disabled pill.
- Click → opens the right editor with the 4-section panel.
- "Add Avatar" tile at the end.

Section 2: **🎰 Avatar Roulette** (gold-bordered cards, 3 columns, ~180×130)
- Each card: thumbnail strip of 3 pool avatars, roulette name, subtitle "🎲 N pool · M triggers".
- Click → opens the right editor with the roulette editor.
- "Add Roulette" tile at the end.

**Right editor panel — vertical, max width 420:**

When an Avatar Swap card is selected:
```
┌──────────────────────────────────────────────┐
│ [target avatar img] UmbreonPal               │
│   Target Avatar                               │
│   [Browse] [Use Current]                      │
│   ↩ Returns to global return avatar          │
│ ──────────────────────────────────────────── │
│ 🏆 Channel Points (2)                [+ Add] │
│   ▸ Umbreon but 30min · 1000 pts     [🗑]    │
│   ▾ I AM NOT AN UMBREAON!?          [🗑]    │
│     ┌─ REWARD NAME ──────────────┐           │
│     │ I AM NOT AN UMBREAON!?     │           │
│     └────────────────────────────┘           │
│     COST: 500  ACTIVE: 1800  CD: 60          │
│     PROMPT: [_____________]                  │
│     ☐ Chat cmd fallback [!umbreon]           │
│     ☐ Keep after stream                      │
│     ☐ Delete when inactive                   │
│ ──────────────────────────────────────────── │
│ 💎 Bits (1)                          [+ Add] │
│   ▸ Cheer 500 = 30m                 [🗑]    │
│ ──────────────────────────────────────────── │
│ ⭐ Subs (2)                          [+ Add] │
│   ▸ [T1] Sub = 60m                 [🗑]    │
│   ▸ [GIFT] Gift x5 = 5m            [🗑]    │
│ ──────────────────────────────────────────── │
│ 💵 Payment (1)                       [+ Add] │
│   ▸ SE tip $5 = 10m                [🗑]    │
│ ──────────────────────────────────────────── │
│ Advanced triggers (open full editor)         │
│   [💬 Chat Command] [👥 Follow] [⚡ Power-up] │
│ ──────────────────────────────────────────── │
│ [Delete Avatar]                  [Save]      │
└──────────────────────────────────────────────┘
```

When a Roulette card is selected:
```
┌──────────────────────────────────────────────┐
│ [roulette thumbnail strip]                   │
│   Furry Roulette                              │
│   [Browse pool] [Use avatars]                 │
│ ──────────────────────────────────────────── │
│ Pool (5 avatars)            [+ Add] [- Remove]│
│   [avatar 1] [avatar 2] [avatar 3] ...        │
│ ──────────────────────────────────────────── │
│ Return Avatar                                │
│   ( ) Use global return avatar               │
│   ( ) Use custom: [Picker] [Use Current]     │
│ ──────────────────────────────────────────── │
│ Triggers (1)                          [+ Add] │
│   ▸ 🏆 Cheer 500 in chat         [🗑]        │
│   ▾ [inline editor with trigger-type fields]│
│ ──────────────────────────────────────────── │
│ [Delete Roulette]                 [Save]      │
└──────────────────────────────────────────────┘
```

### 6.2 Inline editor field sets per trigger type

| Trigger | Inline fields |
|---------|---------------|
| **Channel Points** | Reward Name, Cost, Active Time, Cooldown, Prompt (description), Chat cmd fallback + command, Keep after stream, Delete when inactive, Managed reward color |
| **Bits** | Minimum amount, Active time per amount unit (sec per bits), Max accumulated duration, Permanent, Cooldown |
| **Subs** | Tier (T1/T2/T3), Gift sub toggle, Active time per sub, Max accumulated duration, Cooldown |
| **Payment** | Source rule picker (linked Cash Payment rule), Min amount, Active time per amount, Cooldown |
| **Chat Command** | Command text, Permission (Everyone / Sub / Mod / Broadcaster), Cooldown |
| **Follow** | Active time, Cooldown (mostly read-only) |
| **Power-up** | Opens the full rule editor (advanced fields) |
| **Cash Payment** | Opens the full rule editor (linked rule has its own UI) |

All inline fields save on blur. The footer **Save** button commits the whole profile or roulette. The **Delete Avatar / Delete Roulette** button removes the entire card.

### 6.3 Per-rule editor (`UserControls/AvatarSwapRuleEditorControl.xaml`)

The existing `AvatarSwapRuleEditorControl` is **kept as-is** and used when the user picks Chat Command, Follow, Power-up, or Cash Payment from the Advanced triggers row, and when editing a roulette trigger. It is also used for the inline Power-up and Cash Payment rules.

The existing per-rule editor already handles the `DataContext.IsViewingAvatarTriggers` flag to hide the reward-source picker. That logic is preserved.

### 6.4 Removed from `MainWindow.xaml`

- The "Avatar Change Setup" tab (around lines 3515-4200) and its help text.
- The "Add Avatar Change Override" button + "Avatar Change Override Rules" list (around lines 4280-4310).
- The "Add Avatar Change" / "Delete Avatar Change" buttons on the master tab.
- The "Use cooldown-only avatar changes (no return avatar)" checkbox on the master tab.
- The per-rule `UsesAvatarChange` action block (around lines 8825-8861).
- The `ShowMasterAvatarTabCommand`, `AddAvatarChangeOverrideCommand`, `UseCurrentAvatarForAvatarChangeRuleCommand`, and the `"AvatarChange"` branch of `OpenAvatarPickerCommand` are removed from `MainWindowViewModel.cs`.

### 6.5 Added to `MainWindow.xaml`

- A single "Avatar Swap" button in the Avatar Actions group of the Redeem Library right column, bound to `OpenAvatarSwapManagerCommand`. Already in place.
- The "Master Avatar" tab is repurposed to show only the global return avatar picker (image + name + "Pick" + "Use Current Avatar" + "Clear"), with a button "Open Avatar Swap Manager" that opens the new manager.

## 7. Migration Plan (v3 → v4)

Runs **once at app startup**, in `SettingsStore.LoadAsync`, called from `AvatarSwapMigrationService.Migrate`. Bumps `AvatarChangeToAvatarSwapMigrationVersion` from `3` to `4`.

**Step 1 — Idempotency check.**
- If `Settings.AvatarChangeToAvatarSwapMigrationVersion >= 4`, return immediately.

**Step 2 — Initialize new collections.**
- `Settings.AvatarRouletteProfiles = new ObservableCollection<AvatarRouletteProfile>();` if null.
- For each `Settings.AvatarSwapProfiles[*]`, ensure `BitsRules`, `SubsRules`, `PaymentRules` exist as empty collections.

**Step 3 — Split `BitsSubsRules` into `BitsRules` + `SubsRules`.**
- For each `profile.BitsSubsRules`:
  - If `rule.TriggerType == TwitchTriggerType.Bits` → move to `profile.BitsRules`.
  - Else if `rule.TriggerType == TwitchTriggerType.Subscriptions`:
    - If `rule.IsGiftSubscription == true` → set `TriggerType = GiftSubscription` and move to `profile.SubsRules`.
    - Else → keep `TriggerType = Subscriptions` and move to `profile.SubsRules`.
  - Else (PowerUp legacy) → move to `profile.ChannelPointRules` with `Source = PowerUp`.
- Clear `profile.BitsSubsRules`.

**Step 4 — Re-tag Cash Payment rules to `PaymentRules`.**
- For each `profile.ChannelPointRules`:
  - If `rule.Source == TriggerRuleSource.CashPayment`:
    - Move to `profile.PaymentRules`. `TriggerType` is left as-is (it stays as the original Twitch trigger type for any runtime that needs it; the manager's Payment section is filtered by `Source = CashPayment`).
  - If `rule.Source == TriggerRuleSource.PowerUp`:
    - Stay in `profile.ChannelPointRules` (Power-up rules live in Channel Points with a "From Power-up" badge).

**Step 5 — Convert `RouletteRules` to `AvatarRouletteProfile`.**
- For each `profile.RouletteRules`:
  - Create a new `AvatarRouletteProfile`:
    - `Id = Guid.NewGuid()`
    - `Name = profile.TargetAvatarName + " Roulette"`
    - `IsEnabled = profile.IsEnabled`
    - `ReturnAvatarId = profile.ReturnAvatarId` (preserve v3 override)
    - `ReturnAvatarName = profile.ReturnAvatarName`
    - `Pool = [RouletteAvatarEntry { AvatarId, AvatarName } for each in rule.AvatarRouletAvatarIds]`
  - Move the rule into `roulette.Triggers` (with `ActionType = AvatarRoulet`).
  - Clear `rule.AvatarRouletAvatarIds` and `rule.AvatarRouletAvatarNames` (the pool now lives on the profile).
  - Add the roulette to `Settings.AvatarRouletteProfiles`.
- Clear `profile.RouletteRules`.

**Step 6 — Remove `ReturnAvatarMode` from `AvatarSwapProfile`.**
- `ReturnAvatarMode`, `ReturnAvatarId`, `ReturnAvatarName` are removed from the model. The migration does not move them anywhere — the global Return Avatar (`Settings.MasterAvatarSwapReturnId`) is now the only return.
- If a profile had `ReturnAvatarMode = UseCustom` with a custom `ReturnAvatarId`, log a warning ("Per-profile return avatar has been replaced by the global Return Avatar. If you had a custom one, the global one is now used.").

**Step 7 — Persist.**
- `Settings.AvatarChangeToAvatarSwapMigrationVersion = 4`.
- Save.

**Step 8 — One-time user notice.**
- The notice text becomes:
  > "Avatar Swap has been reworked! Avatar Roulette is now its own card type, and Bits / Subs / Payment each have their own section. The per-avatar 'Return Avatar' is gone — all swaps now return to the global Return Avatar at the top of the window. This notice will not appear again."
- Shown only once, when `AvatarSwapMigrationNoticeShown == false`. Set to `true` after dismissal.

## 8. Runtime Wiring

### 8.1 `BridgeCoordinator.ExecuteRuleActionAsync` change

Add a parent-profile lookup at the top of action resolution for any avatar-swap rule:

```csharp
if (rule.ActionType is OscActionType.AvatarChange or OscActionType.AvatarRoulet)
{
    var rouletteProfile = activeConfiguration?.FindRouletteProfileForRule(rule);
    if (rouletteProfile != null)
        return ResolveRouletteProfileAction(rouletteProfile, rule);

    var swapProfile = activeConfiguration?.FindAvatarSwapProfileForRule(rule);
    if (swapProfile != null)
        return ResolveAvatarSwapAction(swapProfile, rule, capturedReturnAvatar);

    // Fall through to legacy paths for any non-migrated rule.
}
```

`FindRouletteProfileForRule` (new) walks `configuration.AvatarRouletteProfiles` and returns the profile whose `Triggers` contains the rule. `FindAvatarSwapProfileForRule` (existing) extends to consult `BitsRules` and `SubsRules` in addition to `ChannelPointRules` and `PaymentRules`.

### 8.2 `ResolveAvatarSwapAction` (existing; finalizes without return mode)

`BridgeCoordinator.cs:8172-8212`. Now ignores the per-profile return mode and always uses the **global** return avatar:

```csharp
private async Task ResolveAvatarSwapAction(
    AvatarSwapProfileSnapshot profile, TriggerRule rule, string? capturedReturn)
{
    var returnAvatarId = capturedReturn
        ?? activeConfiguration?.MasterAvatarSwapReturnId
        ?? profile.TargetAvatarId; // safe fallback
    // ... emit /avatar/change <target> + /avatar/change <return>
}
```

The `SameAsTarget` mode from v3 is gone. The user can still achieve "one-way swap" by clearing the global return avatar (set it to a deliberately chosen avatar and don't use the swap to set the return).

### 8.3 `ResolveRouletteProfileAction` (new)

```csharp
private async Task ResolveRouletteProfileAction(
    AvatarRouletteProfileSnapshot roulette, TriggerRule rule)
{
    var picked = PickAvatarRouletTarget(roulette.Pool);  // moved from TriggerRule to roulette.Pool
    if (picked == null) return;

    var returnAvatarId = roulette.ReturnAvatarId
        ?? activeConfiguration?.MasterAvatarSwapReturnId
        ?? picked.AvatarId;
    // emit /avatar/change <picked> + /avatar/change <return>
}
```

`PickAvatarRouletTarget` (existing at `BridgeCoordinator.cs:8315-8360`) updates to accept `IReadOnlyList<RouletteAvatarEntrySnapshot>` instead of two parallel arrays from `TriggerRule.AvatarRouletAvatarIds/Names`. The "no repeat" bag moves to be keyed on `roulette.Id` (not `rule.Id`).

### 8.4 `BridgeRuntimeConfiguration.FromSettings` cleanup

- For each `AvatarSwapProfile`, snapshot all 4 rule collections.
- For each `AvatarRouletteProfile`, snapshot the pool and the triggers.
- Build the two lookup dictionaries.
- Remove the legacy skip blocks at lines 365-369, 379-383, 642-654, 966-978 (per the v3 spec) — already done in the v3 migration.

## 9. Paid System Routing

After v4 migration, paid avatar-swap rules live in:
- `AvatarSwapProfile.ChannelPointRules` (Power-up source)
- `AvatarSwapProfile.BitsRules` (Bits)
- `AvatarSwapProfile.SubsRules` (regular sub + gift sub)
- `AvatarSwapProfile.PaymentRules` (Cash Payment source)

Or, for roulettes, in `AvatarRouletteProfile.Triggers`.

Runtime dispatch:
- **Channel-point redeem** — `BridgeCoordinator` matches the redemption against `configuration.AvatarSwapProfiles[*].ChannelPointRules` and `RouletteRules`. For each match, call `ResolveAvatarSwapAction` or `ResolveRouletteProfileAction`.
- **Bits / Sub / Gift Sub / Follow** — matches against `BitsRules`, `SubsRules`, or roulette triggers.
- **Chat Command** — handled in the same way, no special path.
- **Power-up Bits** — `BridgeCoordinator.HandlePowerUp` looks up the avatar-swap rule by `Source = PowerUp` and `PowerUpId`, finds the parent profile (swap or roulette), and dispatches.
- **Cash payments** — `BridgeCoordinator.HandleCashPayment` looks up the rule by `Source = CashPayment` and `CashPaymentRuleId`. (The manager's "Payment" section is filtered by `Source = CashPayment`; the rule's `TriggerType` is left as-is so any runtime that needs it still works.)

The paid-priority stack (`IsPaidAvatarChangeBypassRule`, `IsSupporterOverrideRule`, `queuedSupporterOverrides`) is unchanged.

## 10. Localization

Add new keys in every language (base + `.extra.json`):

- "Avatar Swap" — already in `en-US.json`, add to all other locales.
- "Avatar Swap Manager" — window title.
- "Global Return Avatar" — banner header.
- "Avatar Swaps" / "Avatar Roulette" — section headers.
- "Channel Points" / "Bits" / "Subs" / "Payment" — section headers in the right panel.
- "+ Add Channel Point" / "+ Add Bits" / "+ Add Sub" / "+ Add Payment" — per-section add buttons.
- "Advanced triggers" — small header above the [Chat Command][Follow][Power-up] row.
- "GIFT" — gift-sub badge.
- "T1" / "T2" / "T3" — tier badges.
- "Roulette" / "Add Roulette" / "Pool" / "Triggers" — roulette section labels.
- "Edit Avatar Swap" / "Edit Roulette" — editor titles.
- "From Power-up" / "From Cash Payment" — source badges. Already in en-US, add to other locales.
- The one-time migration notice text.
- The "Per-profile return avatar has been replaced by the global Return Avatar" warning text.

Update existing keys:
- "Avatar Change Setup" → value "Avatar Swap" in every locale.
- "Avatar Change Redeems" → value "Avatar Swap Redeems".
- "Add Avatar Change" → value "Add Avatar Swap".
- "Delete Avatar Change" → value "Delete Avatar Swap".

Keep "Avatar Change" inside the rule editor's `ActionType` combo as "Avatar Change" — the combo is still rendered for non-avatar-swap paths.

Run `LocalizationAudit` and verify no empty values, no placeholder copies, no English fallbacks in non-English locales.

## 11. File-Level Change List

### New files

- `VrcTwitchOscBridge/Models/AvatarRouletteProfile.cs` — new model.
- `VrcTwitchOscBridge/ViewModels/AvatarRouletteCardViewModel.cs` — per-roulette-card VM.
- `VrcTwitchOscBridge/ViewModels/AvatarRouletteEditorViewModel.cs` — per-roulette editor VM.
- `VrcTwitchOscBridge/ViewModels/InlineAvatarSwapRuleRowViewModel.cs` — inline-editable rule row VM (one per rule).
- `VrcTwitchOscBridge/UserControls/InlineAvatarSwapRuleRowControl.xaml` (+ `.cs`) — the inline editor control.
- `VrcTwitchOscBridge/UserControls/AvatarRoulettePoolEditorControl.xaml` (+ `.cs`) — the pool editor embedded in the roulette editor.
- `VrcTwitchOscBridge/Services/AvatarRouletteRuntimeDispatcher.cs` — the new `ResolveRouletteProfileAction` and `PickAvatarRouletTarget(roulette.Pool)`.
- `VrcTwitchOscBridge.Tests/AvatarSwapMigrationServiceV4Tests.cs` — v3 → v4 migration tests.
- `VrcTwitchOscBridge.Tests/AvatarRouletteProfileDispatchTests.cs` — roulette dispatch tests.

### Modified files

- `VrcTwitchOscBridge/Models/AvatarSwapProfile.cs` — restructure: remove `ReturnAvatarMode` / `ReturnAvatarId` / `ReturnAvatarName` / `BitsSubsRules` / `RouletteRules`; add `BitsRules` / `SubsRules` / `PaymentRules`; update `AvatarSubtitle` / `HasRules` / `Uses*` flags.
- `VrcTwitchOscBridge/Models/TriggerRule.cs` — add `IsGiftSubscription` field; preserve `PowerUpId` / `CashPaymentRuleId` (already added in v3 spec).
- `VrcTwitchOscBridge/Models/TwitchTriggerType.cs` — add `ChatCommand`, `Follow`, `CashPayment`, `GiftSubscription` values.
- `VrcTwitchOscBridge/Models/AppSettings.cs` — add `AvatarRouletteProfiles`; bump migration version constant to 4.
- `VrcTwitchOscBridge/Services/AvatarSwapMigrationService.cs` — extend to v4: split BitsSubs, retag CashPayment, convert Roulette, drop return mode.
- `VrcTwitchOscBridge/Services/SettingsStore.cs` — round-trip the new `AvatarRouletteProfiles` collection and the 4-collection `AvatarSwapProfile`.
- `VrcTwitchOscBridge/Services/BridgeRuntimeConfiguration.cs` — add `AvatarRouletteProfileSnapshot`, `RouletteAvatarEntrySnapshot`; update `AvatarSwapProfileSnapshot` to 4 collections; build the two lookup dictionaries; update `FindAvatarSwapProfileForRule`; add `FindRouletteProfileForRule`.
- `VrcTwitchOscBridge/Services/BridgeCoordinator.cs` — add `FindRouletteProfileForRule` helper; route avatar-swap rules through `ResolveAvatarSwapAction` (no return mode) and `ResolveRouletteProfileAction` (new); update `PickAvatarRouletTarget` to take `roulette.Pool`; move the "no repeat" bag key to `roulette.Id`; update migration-notice text; update cash + power-up dispatch re-routing (already in v3 spec).
- `VrcTwitchOscBridge/AvatarSwapManagerWindow.xaml` (+ `.cs`) — full re-layout: 3-col card grid, two sections (Avatar Swaps / Avatar Roulette), new right-editor template with 4 sections + advanced triggers row + inline rule editor; remove the per-avatar Return Avatar block; remove the old `ChannelPointCards` / `BitsSubsCards` / `RouletteCards` collections and the matching rebuild code.
- `VrcTwitchOscBridge/ViewModels/AvatarSwapManagerViewModel.cs` — restructure for 4 collections, add `RouletteProfiles` collection, add `AddRouletteCommand` / `OpenRouletteEditorCommand` / `SaveRouletteEditorCommand` / `DeleteRouletteCommand`, add `AddChannelPointRuleCommand` / `AddBitsRuleCommand` / `AddSubsRuleCommand` / `AddPaymentRuleCommand`, add `AddAdvancedTriggerCommand` (chat / follow / power-up), add inline `EditingRule` state with `BeginInlineEdit` / `CommitInlineEdit` / `CancelInlineEdit`.
- `VrcTwitchOscBridge/ViewModels/AvatarSwapCardViewModel.cs` — update subtitle format to `N cp · N bits · N subs · N pay`; remove the `RouletteRuleCount` pill (roulette is no longer on the avatar card).
- `VrcTwitchOscBridge/UserControls/AvatarSwapRuleEditorControl.xaml` — add inline mode (a flag on the VM that hides the full-screen layout and shows the compact inline fields for Channel Points / Bits / Subs / Payment / Chat Command / Follow).
- `VrcTwitchOscBridge/MainWindow.xaml` — remove the "Avatar Change Setup" tab; remove the per-rule `UsesAvatarChange` action block; remove the "Add Avatar Change Override" button + list; remove the "Add Avatar Change" / "Delete Avatar Change" buttons; remove the cooldown-only mode checkbox; remove the "Permanent avatar change" checkbox on Power-up editor; replace the Master Avatar tab body with the global return-avatar picker.
- `VrcTwitchOscBridge/ViewModels/MainWindowViewModel.cs` — remove `ShowMasterAvatarTabCommand`, `AddAvatarChangeOverrideCommand`, `UseCurrentAvatarForAvatarChangeRuleCommand`, the `"AvatarChange"` branch of `OpenAvatarPickerCommand`, the `AvatarChangeOverrideRules` projection, the `HasAvatarChangeOverrideRules` projection; keep `OpenAvatarSwapManagerCommand`; add `ShowAvatarSwapMigrationNotice` flag and update the notice text.
- `VrcTwitchOscBridge/MainWindow.xaml.cs` — update the migration-notice text.
- `VrcTwitchOscBridge/CHANGELOG.txt` — v3.1.10 entry.
- `VrcTwitchOscBridge/RELEASE-CHANGE-RECORD.txt` — bump Pending Release Draft to v3.1.10.
- Every `Localization/*.json` and `Localization/*.extra.json` file — add the new keys from Section 10.
- `AGENTS.md` — update "Project Identity": active build lane, version, etc.

### Untouched (read-only reference)

- `VrcTwitchOscBridge/AvatarPickerWindow.xaml` + `.cs` (reuse as-is).
- `VrcTwitchOscBridge/Services/AvatarImageService.cs` (reuse as-is).
- `VrcTwitchOscBridge/Services/AvatarPickerService.cs` (reuse as-is).
- `VrcTwitchOscBridge/AvatarSetsManagerWindow.xaml` (out of scope).
- `VrcTwitchOscBridge/UniversalTriggersManagerWindow.xaml` (out of scope).
- `VrcTwitchOscBridge/Services/ManagedRewardPresentation.cs` (out of scope).
- `VrcTwitchOscBridge/Services/CashPaymentProviderService.cs` (out of scope).
- All VRChat LocalLow files (read-only inputs).

## 12. Risks & Considerations

- **Bigger data model change.** The v3 → v4 migration rewrites the structure of every `AvatarSwapProfile`. If a user has a large save, the migration is fast (in-memory) but irreversible without a backup. Mitigated by the one-time notice.
- **The old "Return Avatar" block is gone.** Users who had a per-profile custom return avatar will see the global return avatar used instead. The migration notice explains this.
- **Chat Command / Follow / Power-up inline editors** are a new pattern. Need thorough testing in the inline-edit path.
- **Roulette pool picker UI.** The `AvatarRouletPickerWindow` is reused but is now embedded in the roulette editor. UX risk: editor height grows.
- **Build / XAML compile issues.** New XAML must be added to the `<Page>` section of `VrcTwitchOscBridge.csproj` (existing project rules have `EnableDefaultItems=false`). Apply the project file rules carefully.
- **Roulette "no repeat" bag key change.** The bag is keyed on `roulette.Id` instead of `rule.Id` in v4. This is a behavior change for users who have multiple rules pointing at the same roulette pool — they will now share the no-repeat state per roulette, not per rule. This is the correct semantics: the pool is the thing with no-repeat state, not the rule.

## 13. Testing Approach

### Unit tests (VrcTwitchOscBridge.Tests)

1. **`AvatarSwapMigrationServiceV4Tests`** (new):
   - `MigrateV4_SplitsBitsSubsIntoBitsAndSubs` — confirms a v3 BitsSubs rule moves to the right new collection.
   - `MigrateV4_TagsGiftSubscriptionTriggerType` — confirms `IsGiftSubscription=true` rules become `TriggerType=GiftSubscription`.
   - `MigrateV4_RetagsCashPaymentToPaymentRules` — confirms `Source=CashPayment` rules move to `PaymentRules` and get `TriggerType=CashPayment`.
   - `MigrateV4_ConvertsRouletteToAvatarRouletteProfile` — confirms a v3 roulette rule becomes an `AvatarRouletteProfile` with the pool extracted, the rule moved to `Triggers`, and the profile added to `Settings.AvatarRouletteProfiles`.
   - `MigrateV4_PreservesPerRouletteReturnAvatarOverride` — confirms a v3 profile with `ReturnAvatarMode=UseCustom` moves that override onto the new `AvatarRouletteProfile`.
   - `MigrateV4_BumpsVersionTo4` — confirms the marker becomes 4.
   - `MigrateV4_IsIdempotent` — calling twice does not duplicate rules.
   - `MigrateV4_DropsPerProfileReturnMode` — confirms the per-profile return mode fields are gone after migration.

2. **`AvatarRouletteProfileDispatchTests`** (new):
   - `PickAvatarRouletTarget_NoRepeatBag_KeyedOnRouletteId`.
   - `PickAvatarRouletTarget_RespectsDisabledAvatar`.
   - `ResolveRouletteProfileAction_EmitsPickedAvatarAndGlobalReturn`.
   - `ResolveRouletteProfileAction_EmitsCustomReturn_WhenOverrideSet`.

3. **`AvatarSwapRuntimeDispatchTests`** (updated):
   - `FindAvatarSwapProfileForRule_LocatesRuleInBitsRules`.
   - `FindAvatarSwapProfileForRule_LocatesRuleInSubsRules`.
   - `FindAvatarSwapProfileForRule_LocatesRuleInPaymentRules`.
   - `FindRouletteProfileForRule_LocatesTrigger`.

4. **Existing tests preserved** — all 8 `AvatarSwapMigrationServiceTests` and the v3 tests continue to pass.

### Build check

- `dotnet build "E:\!!!Program to work on\Proper Crystal Relay\VrcTwitchOscBridge\VrcTwitchOscBridge.csproj" --no-restore` after each batch of changes.

### Localization audit

- Run `LocalizationAudit` project; verify no empty values, no placeholder copies, no English fallbacks in non-English locales.

### Manual smoke test

- `Launch-Crystal-Relay-Debug.bat`:
  1. Confirm the "Avatar Swap" button is in the Redeem Library.
  2. Open the manager, confirm the Global Return Avatar banner, the two card sections (Avatar Swaps + Avatar Roulette), and existing rules.
  3. Add a new direct swap → expand its row inline → edit name, cost, cooldown → collapse → save. Confirm persistence.
  4. Add a Bits trigger → expand inline → edit min amount and per-amount duration → save.
  5. Add a Sub trigger → expand inline → set tier and gift flag → save.
  6. Add a Cash Payment trigger → expand inline → pick a linked cash payment rule → save.
  7. Add a Chat Command trigger from Advanced → expand inline → set command text and permission → save.
  8. Add an Avatar Roulette card → pick 3 avatars for the pool → add a channel-point trigger → save.
  9. Trigger the roulette from a test redemption → confirm a random avatar is picked and the return-avatar restore fires.
  10. Delete an avatar card → confirm all its rules are removed.
  11. Delete a roulette card → confirm the pool and triggers are removed.
  12. Verify the "Avatar Change Setup" tab is gone, the `UsesAvatarChange` action block is gone, the "Add Avatar Change Override" button is gone.
  13. Restart the app → confirm the migration marker prevents re-migration and the one-time notice does not show on second run.

## 14. Out of Scope (Future Releases)

- Renaming `OscActionType.AvatarChange` to `OscActionType.AvatarSwap`.
- Removing `TriggerRule.AvatarChangeTargetId` / `AvatarChangeResetId` / `AvatarTargetName` / `ResetAvatarName`.
- Removing `AvatarTriggerProfile` entirely.
- Removing the stub `ActionRule` / `TriggerAction` from `PowerUpRule` / `CashPaymentRule`.
- A "Save Transfer" round-trip check for the `AvatarRouletteProfiles` collection.
- Per-roulette thumbnail animation in the gold-bordered card.
