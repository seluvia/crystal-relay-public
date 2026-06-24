# FalxPlays Custom Supporter Card — Design

**Date:** 2026-06-24
**Status:** Approved (pending implementation)
**Scope:** Add a new custom supporter card for Twitch user `FalxPlays` to the Crystal Relay Twitch Chatbox, following the existing custom-supporter pattern.

## Background

The Twitch Chatbox renders three card types per chatter (chat, channel-point redemption, support event). Custom supporters get a unique visual theme (card background, border, left rail, badge, name gradient, and optional glow) plus a special tag label next to their name. Existing custom supporters are:

| Login | Enum kind | Tag label |
|---|---|---|
| `kai_bloodwolf` | `KaiBloodwolf` | "KFC/popeyes chugger" |
| `hypercraftiing` | `Hypercraftiing` | "The Great Cuddly Synth" |
| `kyou_zakira` | `KyouZakira` | "Chatoic Umbreon" |
| `phil13938` | `Phil13938` | "The Canadian Bnuy" |

The Crystal Relay developer (`Screminpal_`) uses a separate special-case pattern (inline badge, `RoleCardKind.None`) and is **not** the template for this work.

## Requirements

- Match Twitch user **`FalxPlays`** (login `falxplays`, checked against both display name and login, `OrdinalIgnoreCase`, leading `@` stripped — same as all other supporters).
- Special tag text next to name: **`Awooey`** (exact mixed-case spelling, hardcoded — not localized, matching the existing custom-supporter convention).
- Color theme: **Emerald Sapphire** — black base with jewel-tone emerald green and sapphire blue.
- Name glow: a themed emerald drop shadow on the name, mirroring the Phil13938 name-glow pattern.
- The card must render correctly across all three card types (chat, redemption, support).

## Approach

Follow the **standard custom-supporter pattern** (KaiBloodwolf/Hypercraftiing/KyouZakira/Phil13938), not the Dev inline-badge pattern. This is the idiomatic fit: it uses the shared `ChatboxRoleBadgeBorderStyle` + `RoleCardLabel` + `RoleCardKind` enum flow that the four named supporters already use.

## Color Theme — Emerald Sapphire

All brushes are frozen (either via `Freeze()` in C# factory methods or as XAML resources). Approximate hex values (final values may be tuned during implementation but stay within this palette):

| Resource | Purpose | Value |
|---|---|---|
| `FalxPlaysTextBrush` | body/name text | `#D8F5EE` (soft mint-white) |
| `FalxPlaysMutedBrush` | timestamp/muted text | `#6A8A8A` |
| `FalxPlaysInsetBrush` | inset panel background | `#78061418` |
| `FalxPlaysInsetBorderBrush` | inset panel border | `#8F00BFA5` |
| `FalxPlaysCardBrush` | card background gradient (diagonal) | `#030608` → `#0A1418` → `#060C12` |
| `FalxPlaysBorderBrush` | card border gradient | `#00BFA5` → `#2979FF` accents |
| `FalxPlaysRailBrush` | left rail gradient (top→bottom) | `#FFFFFF` → `#00BFA5` → `#2979FF` |
| `FalxPlaysBadgeBrush` | badge gradient (diagonal) | `#00BFA5` → `#2979FF` |
| `FalxPlaysGlowEffect` | card drop shadow | emerald-tinted (`#00BFA5`), `BlurRadius="14"`, `ShadowDepth="0"`, `Opacity="0.34"` (matching other supporters' glow params) |
| `FalxPlaysNameBrush` (C#) | name text gradient (horizontal) | `#00BFA5` → `#2979FF` |
| Name glow (inline XAML) | name effect on chat card | `DropShadowEffect` emerald `#00BFA5`, `BlurRadius="3"`, `ShadowDepth="0"`, `Opacity="0.9"` |

The name glow is applied only to the **chat-card** name TextBlock, mirroring Phil13938 (which also only glows on the chat card). The redemption and support card name TextBlocks are not modified for glow — matching the existing convention.

## Touch Points

### `VrcTwitchOscBridge\ViewModels\MainWindowViewModel.cs` (within lines 21198–21818)

11 edits, all mirroring the existing custom-supporter shape:

1. **`TwitchChatRoleCardKind` enum** (`:21204` area) — add `Falx,` after `Phil13938,`.
2. **Login const** (`:21222` area) — add `private const string FalxPlaysLogin = "falxplays";`
3. **Name-brush field** (`:21229` area) — add `private static readonly LinearGradientBrush FalxPlaysNameBrush = CreateFrozenFalxPlaysNameBrush();`
4. **Constructor identity check** (`:21273` area) — add `IsFalxPlays = IsFalxPlaysAccount(UserDisplayName, UserLogin);`
5. **Constructor `RoleCardKind` ternary** (`:21285` area) — add `: IsPhil13938 ? TwitchChatRoleCardKind.Phil13938 : IsFalxPlays ? TwitchChatRoleCardKind.Falx : ResolveRoleCardKind(...)`.
6. **Constructor `NameBrush` ternary** (`:21303` area) — add `: IsPhil13938 ? Phil13938NameBrush : IsFalxPlays ? FalxPlaysNameBrush : ParseNameBrush(...)`.
7. **Identity property** (`:21348` area) — add `public bool IsFalxPlays { get; }`
8. **Role-card boolean property** (`:21366` area) — add `public bool IsFalxPlaysRoleCard => RoleCardKind == TwitchChatRoleCardKind.Falx;`
9. **`RoleCardLabel` switch** (`:21391` area) — add `TwitchChatRoleCardKind.Falx => "Awooey",`
10. **Name-brush factory method** (`:21770` area) — add `CreateFrozenFalxPlaysNameBrush()` returning a frozen horizontal `LinearGradientBrush` with emerald→sapphire stops.
11. **Username matcher** (`:21815` area) — add `IsFalxPlaysAccount(displayName, login)` + `IsFalxPlaysName(value)` methods using `NormalizeTwitchName` and `OrdinalIgnoreCase` comparison against `FalxPlaysLogin`.

### `VrcTwitchOscBridge\TwitchChatboxWindow.xaml`

12. **9 resource definitions** (~line 255 area, after Phil13938's block): `FalxPlaysTextBrush`, `FalxPlaysMutedBrush`, `FalxPlaysInsetBrush`, `FalxPlaysInsetBorderBrush`, `FalxPlaysCardBrush`, `FalxPlaysBorderBrush`, `FalxPlaysRailBrush`, `FalxPlaysBadgeBrush`, `FalxPlaysGlowEffect`.

13. **`IsFalxPlaysRoleCard` DataTriggers added to all 12 styles**:
    - `ChatboxChatCardBorderStyle` (chat card border)
    - `ChatboxChannelPointCardBorderStyle` (redeem card border)
    - `ChatboxSupportCardBorderStyle` (support card border)
    - `ChatboxTimestampTextStyle` (timestamp)
    - `ChatboxPrimaryEntryTextStyle` (primary text)
    - `ChatboxMessageBodyTextStyle` (body text)
    - `ChatboxMutedEntryTextStyle` (muted text)
    - `ChatboxInlinePanelStyle` (inset panel)
    - `ChatboxInputInlinePanelStyle` (input inset panel)
    - `ChatboxEventRailStyle` (event rail)
    - `ChatboxRoleRailStyle` (chat rail — toggles `Visibility=Visible` + rail brush)
    - `ChatboxRoleBadgeBorderStyle` (role badge — swaps badge Background/BorderBrush)

    The Dev-only `ChatboxDevContentOffsetStyle` does **not** need a Falx entry (it keys off `HasBadgeRoleCard`, which already covers Falx via `RoleCardKind != None`).

14. **Name-glow DataTrigger** on the chat-card name TextBlock (`:1644` area) — add an `IsFalxPlaysRoleCard` DataTrigger alongside the existing `IsPhil13938RoleCard` one, setting `Effect` to an emerald `DropShadowEffect`.

## Out of Scope

- **Localization:** The `Awooey` tag stays hardcoded in `RoleCardLabel`, matching the existing custom-supporter convention (none of KaiBloodwolf/Hypercraftiing/KyouZakira/Phil13938 are localized). No `.extra.json` changes, no localization audit required.
- **Code-behind:** No changes to `TwitchChatboxWindow.xaml.cs`.
- **Moderation filter:** No changes to `ChatboxRelayModerationFilter.cs`.
- **Tests:** No changes to `VrcTwitchOscBridge.Tests`.
- **Dev badge pattern:** Not modifying the Crystal Relay Dev inline-badge flow.
- **Redemption/support card name glow:** Not adding glow to the redeem or support card name TextBlocks — Phil13938 only glows on the chat card, and FalxPlays matches that convention.

## Verification

- Build: `dotnet build "E:\!!!Program to work on\Proper Crystal Relay\VrcTwitchOscBridge\VrcTwitchOscBridge.csproj" --no-restore`
- Manual: Launch the debug build via `Launch-Crystal-Relay-Debug.bat`, open the Twitch Chatbox, and confirm a FalxPlays message renders with the emerald/sapphire theme, the `Awooey` badge, and the emerald name glow across chat, redemption, and support card types.

## Stability Impact

Low risk. This is an additive change following an established, isolated pattern. It touches only the chatbox rendering path and does not affect Twitch API calls, reward sync, OSC, or any runtime behavior beyond visual styling for one named user.
