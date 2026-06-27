# Púca Theme — Design Spec

**Date:** 2026-06-24
**Topic:** Add a new built-in visual theme "Púca" to Crystal Relay
**Status:** Approved (pending implementation)

## Overview

Add a new built-in WPF visual theme named **Púca** to Crystal Relay. The theme uses a **cyan / purple / pastel pink** palette in a **dark mystical night** mood with a **fantasy / mythical** aesthetic and a **Pokémon-style** background (fused aurora-ribbon + orbiting-circle + constellation composition around a central arcane magic circle and a cyan crescent moon).

The theme follows the established built-in theme pattern exactly: an `AppTheme` enum member, a `ThemePaletteFactory` switch arm, a vector `ThemeBackgrounds` XAML file, a `ThemeOption` entry, per-theme branches in the six legacy windows that still hardcode colors, and an `AGENTS.md` housekeeping entry.

## Visual Direction (locked from brainstorming)

- **Mood:** Dark Mystical Night — deep indigo/purple base, cyan + pink as glowing magical accents, starfield and auras. Legendary-encounter energy.
- **Background composition:** Fused from two approved directions:
  - Aurora ribbon wisps (flowing gradient strokes in cyan/purple/pink sweeping across the background)
  - Orbiting small magic circles + faint constellation lines linking bright stars
  - All arranged around a central **arcane magic circle** (concentric rings with dashed rune-circle, cardinal/diagonal rune-ticks) and a **centered, larger cyan crescent moon**. A single bright **pink spark** sits off the moon (not over it). Dense starfield.
- **UI chrome palette:** Near-black panels with dual cyan + pink accents. Cyan is the primary accent (primary buttons, active states, scrollbar thumb, section active). Pastel pink is the equal secondary accent (secondary buttons, title-bar sub-text/"Púca" label, selected card border). Purple stays in borders and background only.
- **Fonts:** Cambria (heading) + Verdana (body). Cambria gives refined, crisp serif headings with a polished mystical feel; Verdana keeps body text ultra-legible at small sizes.
- **Home title font size:** 24 (matches the most common value across existing themes; range is 22–24).

## Approach

**Chosen: Follow the established built-in theme pattern exactly.**

Add the enum member, the factory switch arm, the background XAML, the view-model option, and per-theme branches in the six legacy windows that still hardcode colors.

**Rejected alternative:** Same plus refactoring the six legacy per-window `ApplyTheme(AppTheme)` methods to delegate to `ThemeManager.ApplyToResources` instead of hardcoding colors. This would be cleaner long-term but touches unrelated code paths and risks regressions in every existing theme's rendering in those windows. Not appropriate for a theme addition; violates the AGENTS.md "Prefer minimal, targeted edits over broad renames" rule.

## Palette — 44 brush keys

Anchored to the approved near-black / dual cyan+pink mockup. Deep indigo background, near-black-purple panels, purple borders, cyan + pink as equal dual accents.

| Key | Hex | Role |
|---|---|---|
| WindowBackgroundBrush | `#0C0716` | deep indigo-black base |
| PanelBrush | `#E6140C24` | 90% near-black-purple |
| PanelSecondaryBrush | `#D910081A` | 85% darker |
| PanelHighlightBrush | `#D91C1238` | 85% lighter purple |
| BorderBrush | `#3A2868` | purple |
| AccentBrush | `#22D3EE` | cyan (primary) |
| TextBrush | `#E8DEF8` | light lavender-white |
| MutedBrush | `#A896C8` | muted lavender |
| InputBrush | `#E6080410` | 90% very dark |
| InputBorderBrush | `#4A2D8A` | purple |
| ComboSurfaceBrush | `#1A1030` | dark purple |
| ComboTextBrush | `#E8DEF8` | light lavender-white |
| ComboHighlightBrush | `#22D3EE` | cyan |
| SecondaryButtonBrush | `#2AF5B8E0` | 16% pink |
| SecondaryButtonBorderBrush | `#F5B8E0` | pink |
| SectionActiveBrush | `#22D3EE` | cyan |
| RuleCardBrush | `#E6140C24` | 90% near-black-purple |
| RuleCardSelectedBrush | `#2622D3EE` | 15% cyan tint |
| AccentDimBrush | `#1A7E92` | dim cyan |
| RuleCardHoverBrush | `#2A1A3450` | lighter purple tint |
| StatusChipBrush | `#2CA78BFA` | 17% purple tint |
| NestedPanelBrush | `#D910081A` | 85% darker |
| DangerBrush | `#C0395F` | magenta-red (fits pink family) |
| DangerBorderBrush | `#8C2648` | dark magenta |
| WarnBrush | `#7A5A2A` | dark amber |
| WarnBorderBrush | `#A8884A` | amber |
| WarnTextBrush | `#F0D878` | light amber |
| HighlightBorderBrush | `#22D3EE` | cyan |
| PopupBorderBrush | `#3A2868` | purple |
| ComboDropButtonBrush | `#2A1A3A` | dark purple |
| ComboDropButtonHoverBrush | `#3A2868` | purple |
| ComboDropButtonPressedBrush | `#4A3878` | bright purple |
| ScrollTrackBrush | `#1A1028` | very dark purple |
| ScrollThumbBrush | `#22D3EE` | cyan |
| ScrollThumbHoverBrush | `#7DEFFF` | bright cyan |
| ScrollThumbPressedBrush | `#5DE8F5` | mid cyan |
| TitleBarBrush | `#1A0E2E` | deep purple |
| TitleBarTextBrush | `#7DEFFF` | cyan |
| TitleBarSubTextBrush | `#F5B8E0` | pink (Púca accent) |
| TitleBarButtonBrush | `#00000000` | transparent |
| TitleBarButtonHoverBrush | `#3A2868` | purple hover |
| TitleBarButtonPressedBrush | `#4A3878` | bright purple |
| TitleBarCloseHoverBrush | `#C0395F` | danger pink-red |
| TitleBarClosePressedBrush | `#8C2648` | dark magenta |

Fonts: **Cambria** (heading) + **Verdana** (body). HomeTitleFontSize **24**.

## Background XAML — `ThemeBackgrounds\PucaThemeBackground.xaml`

A root `<Grid>` with `<Canvas>`, matching the convention of the other 16 built-in backgrounds (no code-behind, no `x:Class`, auto-included by the wildcard `<Page Include="ThemeBackgrounds\*.xaml" />` in the csproj).

Composition layers (back to front):

1. **LinearGradientBrush** on the Grid (4 stops): `#0C0716` (0.0) → `#161028` (0.42) → `#100A20` (0.78) → `#080410` (1.0). Matches the approved mockup base.
2. **Aurora ribbon wisps**: 3–4 `<Path>` strokes with `LinearGradientBrush` fills (cyan `#22D3EE`, purple `#A78BFA`, pink `#F5B8E0`), low opacity (~0.20–0.26 at peak, fading to 0 at ends), sweeping quadratic-curve paths across the canvas. Represents aurora / nebula trails.
3. **Central nebula glow**: one large low-alpha radial-feel `<Ellipse>` behind the magic circle (purple, ~0.40 opacity, blurred via large size + low alpha).
4. **Orbiting small circles** (outside the main circle): ~5 small `<Ellipse>` elements scattered around the canvas edges — mix of solid cyan, dashed purple, and solid pink, each with an optional smaller dashed/solid companion. These drift around the main circle.
5. **Central arcane magic circle**: 3 concentric `<Ellipse>` centered in the canvas:
   - Outer: solid stroke cyan `#22D3EE` (~0.55 opacity)
   - Middle: dashed stroke purple `#A78BFA` (~0.70 opacity)
   - Inner: solid stroke pink `#F5B8E0` (~0.60 opacity)
   - 8 rune-tick `<Line>` marks at cardinals (N/S/E/W) and diagonals (NE/NW/SE/SW), cyan, ~0.70 opacity
6. **Crescent moon** (centered, larger than the first draft): a `<Path>` crescent filled `#22D3EE` (~0.92 opacity) with a brighter `#7DEFFF` stroke (~0.70 opacity). A larger low-alpha cyan `<Ellipse>` behind it provides a glow halo. The moon is centered inside the innermost ring.
7. **Pink spark**: a single bright `<Ellipse>` `#F5B8E0` with a low-alpha halo `<Ellipse>` behind it, positioned **off** the moon (upper area of the canvas, not overlapping the crescent).
8. **Constellations**: 2–3 faint `<Polyline>` (cyan, ~0.35 opacity, thin stroke) linking bright stars in the upper-left, lower-right, and lower-left regions.
9. **Starfield**: ~12 small `<Ellipse>` (3px) scattered across the canvas — mix of cyan (`#7DEFFF`), pink (`#F5B8E0`), purple (`#A78BFA`), and white (`#FFFFFF`), some with small glow shadows.

All coordinates use `Canvas.Left` / `Canvas.Top` absolute positioning, matching the existing background files. No animations (existing backgrounds are static).

## File touch list

13 edits total. No localization keys needed (theme names are not localized — confirmed by search of all 28 localization files). No csproj edits needed (`AppTheme.cs` already compiled at `VrcTwitchOscBridge.csproj:151`, `ThemeManager.cs` at `:236`, `ThemeBackgrounds\*.xaml` is a wildcard Page include at `:40`).

| # | File | Edit |
|---|---|---|
| 1 | `VrcTwitchOscBridge\Models\AppTheme.cs` | Append `Puca` enum member at the end (after `SquishyFoxPlush`, line 23) to preserve persisted integer values. |
| 2 | `VrcTwitchOscBridge\Services\ThemeManager.cs` | New `AppTheme.Puca => CreateBuiltInPalette(...)` arm in `ThemePaletteFactory.CreatePalette` switch (before the `_ =>` default, after the `SquishyFoxPlush` arm ending at ~line 1013). Supplies all 44 brush keys from the palette table above. |
| 3 | `VrcTwitchOscBridge\ThemeBackgrounds\PucaThemeBackground.xaml` | New file (auto-included by wildcard). Root `<Grid>` + `<Canvas>` per the Background XAML section above. |
| 4 | `VrcTwitchOscBridge\MainWindow.xaml.cs` | New `AppTheme.Puca => "ThemeBackgrounds/PucaThemeBackground.xaml"` arm in `LoadThemeBackground` switch (before the `_ =>` fallback, ~line 586). |
| 5 | `VrcTwitchOscBridge\ViewModels\MainWindowViewModel.cs` | Append `new ThemeOption(AppTheme.Puca, "Púca")` to `ThemeOptions` array (~line 748). |
| 6 | `VrcTwitchOscBridge\ViewModels\MainWindowViewModel.cs` | Add `public bool IsPucaThemeSelected => SelectedTheme == AppTheme.Puca;` (~line 2823) + `RaisePropertyChanged(nameof(IsPucaThemeSelected))` inside `RaiseThemeStateChanged` (~line 3818). |
| 7 | `VrcTwitchOscBridge\ThemedDialogWindow.xaml.cs` | Per-theme `if (theme == AppTheme.Puca) { ... }` branch in `ApplyTheme` (~line 172+), using palette colors. |
| 8 | `VrcTwitchOscBridge\AvatarRouletPickerWindow.xaml.cs` | Per-theme branch (~line 166+). |
| 9 | `VrcTwitchOscBridge\RuleLockoutPickerWindow.xaml.cs` | Per-theme branch (~line 172+). |
| 10 | `VrcTwitchOscBridge\VrChatTwoFactorWindow.xaml.cs` | Per-theme branch (~line 98+). |
| 11 | `VrcTwitchOscBridge\VrChatLoginWindow.xaml.cs` | Per-theme branch (~line 72+). |
| 12 | `VrcTwitchOscBridge\TwitchChatboxWindow.xaml.cs` | Per-theme branch (~line 510+) using the **newer** pattern: call `ThemeManager.ApplyToResources(Resources, theme)` then set the 5 chatbox-specific brushes (`MessageTextBrush`, `MessageCardBrush`, `MessageBorderBrush`, `TimestampBrush`, `SecondaryButtonTextBrush`) using palette colors. |
| 13 | `AGENTS.md` | Add `- \`Púca\`` to the "Current Themes" bulleted list. |

## Verification

After implementation:

1. **Build:** `dotnet build "E:\!!!Program to work on\Proper Crystal Relay\VrcTwitchOscBridge\VrcTwitchOscBridge.csproj" --no-restore`
2. **Localization audit:** run the localization audit (per AGENTS.md) — no new keys expected, but the audit enforces coverage/placeholder integrity.
3. **Manual smoke test:** launch the debug build via `Launch-Crystal-Relay-Debug.bat`, open Settings → Visual → Theme, select "Púca", and verify:
   - Main window background renders the fused aurora/circle/constellation art with the centered crescent moon.
   - Panels, borders, buttons, scrollbar, title bar, and selected cards use the near-black / dual cyan+pink palette.
   - Cambria headings + Verdana body render correctly.
   - Open each secondary window (ThemedDialogWindow, AvatarRouletPickerWindow, RuleLockoutPickerWindow, VrChatTwoFactorWindow, VrChatLoginWindow, TwitchChatboxWindow) and confirm Púca renders correctly in each.
   - Switch away from Púca and back; confirm live theme update works.

## Out of scope

- No new brush resource keys (reuse the existing 44).
- No localization changes (theme names are not localized).
- No csproj edits.
- No XAML DataTriggers for Púca-specific chrome behavior (the two existing per-theme triggers at `MainWindow.xaml:344` and `:7571` are one-offs; Púca needs none).
- No refactor of the legacy per-window `ApplyTheme` methods (kept as-is per the minimal-edit approach).
- No background image asset (built-in themes use vector XAML; `ThemeAssetStore` is Custom-only).
