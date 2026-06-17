# Avatar Swap Card Visual Overhaul — Design

**Date:** 2026-06-17
**Status:** Draft (awaiting user review)
**Author:** Brainstorming session
**Active build lane:** `beta1` on `3.1.10`

## Context

The Avatar Swap manager window (`VrcTwitchOscBridge/AvatarSwapManagerWindow.xaml`) and its inline rule row control (`VrcTwitchOscBridge/UserControls/InlineAvatarSwapRuleRowControl.xaml`) are the last two themed surfaces in the app that still use hardcoded hex colors instead of binding to the shared `ThemeManager` palette. As a result:

- The cards are unreadable on most themes (the only theme they look reasonable on is `Void Crystal`).
- The empty avatar cards (bottom row of the user's screenshot) look broken — they show a solid `#3a2a5a` block with no "Pick Avatar" affordance, so users cannot tell whether the avatar is still loading or simply hasn't been picked yet.
- Card subtitle text (`Foreground="#b0a3d0"` at `FontSize 9`) is low contrast against the card fill (`#322250`).
- Card border (`#4a3868`) is nearly indistinguishable from the card fill.
- The "Power-up" advanced trigger button can clip when the right editor column is at its 420px width.
- The view model already exposes `HasTarget`, `IsEnabled`, and `StatusStripeBrush`, but the XAML ignores them, so the user gets no at-a-glance state.

This spec covers a full visual overhaul of the manager window and its inline rule row, bringing both in line with the rest of the app's themed controls.

## Goals

- Migrate `AvatarSwapManagerWindow.xaml` and `InlineAvatarSwapRuleRowControl.xaml` from hardcoded hex to `DynamicResource` bindings against `ThemeManager`.
- Add the missing affordances the user identified: status stripe, empty-state hint, rule-count pill, hover state, enabled/disabled visibility.
- Wrap the right editor's advanced trigger button row so "Power-up" can never clip.
- Fix the subtitle contrast and card border contrast.
- Keep the 180×130 card size; do not upsize.
- Keep the change scoped to XAML, the small code-behind wiring needed for theme integration, and one new view-model property. No behavior change.

## Non-Goals

- No changes to swap dispatch, save/load, or migration logic.
- No changes to `AvatarSwapProfile`, `AvatarSwapManagerViewModel`, or any service.
- No upsizing of the cards to 280×320 (AvatarSets style).
- No new theme palette entries — every brush we need already exists.
- No new button styles — the existing `SecondaryButtonStyle`, `DangerButtonStyle`, and `AccentButtonStyle` (already defined in other themed windows, copied into this window's local resources) cover every button in the window.
- No new theme-aware background image logic for this window.

## Architecture

The fix is **a XAML refactor of the manager window and its inline rule row**, plus minimal code-behind wiring to plug the window into the existing `ThemeManager` flow, plus one additive view-model property and one new localization key.

### Theme integration model (matches the rest of the app)

The project pattern for theme-aware windows is:

1. **Window declares a `<Window.Resources>` block** with placeholder `SolidColorBrush` entries for every theme brush key the window uses. These placeholders are never the runtime color — they exist so the `{DynamicResource ...}` lookups in the XAML can resolve at parse time.
2. **Code-behind calls `ThemeManager.ApplyToResources(Resources, theme)`** in the constructor (with the active theme) and again via `Dispatcher.BeginInvoke(...)` on `ThemeManager.ThemeChanged`. The theme manager mutates the placeholder brushes in place (or creates them if missing), so the XAML picks up the new color via the dynamic resource binding.
3. **XAML uses `{DynamicResource ...}` bindings** to reference those brushes. Switching the active theme at runtime re-colors the window live, with no code changes.

`AvatarSwapManagerWindow.xaml` currently has none of this. `AvatarSwapManagerWindow.xaml.cs` also has no theme wiring. The fix adds both.

### Data flow

The view models already expose every property the new visuals need. No new events, no new commands, no new state.

- `AvatarSwapCardViewModel`:
  - Existing: `Image`, `HasImage`, `DisplayTitle`, `Profile.TargetAvatarName`, `AvatarSubtitle`, `HasTarget`, `IsEnabled`, `UsesChannelPointRules`, `UsesBitsRules`, `UsesSubsRules`, `UsesPaymentRules`, `StatusStripeBrush`, `RuleCountText` (the string total of CP+Bits+Subs+Pay).
  - New: `HasAnyRules` (additive). Implementation: `public bool HasAnyRules => UsesChannelPointRules || UsesBitsRules || UsesSubsRules || UsesPaymentRules;` plus a `RaisePropertyChanged(nameof(HasAnyRules))` call in the existing rule-collection PropertyChanged handler.
- `AvatarSwapManagerViewModel`: no changes.
- `AvatarSwapProfile`: no changes.

### Files touched

- `VrcTwitchOscBridge/AvatarSwapManagerWindow.xaml` — primary (new `<Window.Resources>` block + full XAML migration).
- `VrcTwitchOscBridge/AvatarSwapManagerWindow.xaml.cs` — add `ThemeManager.ApplyToResources(...)` calls in the constructor and on `ThemeChanged`. Pattern is copied verbatim from `AvatarSetsManagerWindow.xaml.cs:14` and `AvatarSetsManagerWindow.xaml.cs:364`.
- `VrcTwitchOscBridge/UserControls/InlineAvatarSwapRuleRowControl.xaml` — theme migration only (brush swaps).
- `VrcTwitchOscBridge/ViewModels/AvatarSwapCardViewModel.cs` — one new property.
- `VrcTwitchOscBridge.Tests/AvatarSwapManagerViewModelTests.cs` (or a new small test file) — one unit test for `HasAnyRules`.
- `VrcTwitchOscBridge/Localization/Resources/en-US.json` — one new key.
- `VrcTwitchOscBridge/Localization/Resources/<lang>.extra.json` for every other supported language.

### Files NOT touched

- `AvatarSwapProfile.cs` (model is fine).
- `ThemeManager.cs`, `ThemePaletteFactory.cs` (no new palette entries; every brush key the new visuals need already exists in the palette).
- `csproj` files (no new XAML/code files to register, since the only edited files are already included in the project).

## Detailed Design

### 1. Card visual structure (180×130)

```
┌──┬─────────────────────────┐
│  │  ┌──────────────────┐ 3 │  <- 4px status stripe | 64px hero | rule-count pill
│  │  │   image / 🎭     │   │
│  │  │   Pick Avatar    │   │
│  │  └──────────────────┘   │
├──┴─────────────────────────┤
│ Name (TextBrush, 12pt)     │  <- 16px title row
│ 3 cp · 0 bits · 0 · 0      │  <- 14px subtitle row (MutedBrush, 10pt)
└────────────────────────────┘
```

(The "16px" and "14px" in the right-hand annotations are the **row heights**, not the font sizes. The font sizes are listed inline: title 12pt, subtitle 10pt.)

Element-by-element:

1. **Outer border** — 180×130, `Background="{DynamicResource PanelBrush}"`, `BorderBrush="{DynamicResource BorderBrush}"` (with hover override), `BorderThickness="1"`, `CornerRadius="7"`. Padding `6`.

2. **Status stripe (left, 4px)** — `BorderThickness="4,0,0,0"` on the outer border, with `BorderBrush="{Binding StatusStripeBrush}"` overriding the base border. The view model already returns `MediumSeaGreen` when enabled and `Gray` when disabled.

3. **Hero image area (top, 64px)** — `Background="{DynamicResource NestedPanelBrush}"`, `CornerRadius="4"`, `ClipToBounds="True"`. Contains:
   - `Image` bound to `{Binding Image}` (existing) with `Visibility="{Binding HasTarget, Converter={StaticResource BoolToVisibilityConverter}}"`.
   - Empty-state `Border` (centered `StackPanel` with `🎭` at 24pt + localized "Pick Avatar" text at 10pt) with `Visibility="{Binding HasTarget, Converter={StaticResource InverseBoolToVisibilityConverter}}"`. The empty-state foregrounds are `MutedBrush`.

4. **Rule-count pill (top-right corner, overlays the hero)** — Small 22×16 pill, `Background="{DynamicResource AccentBrush}"`, `CornerRadius="8"`. Inside: the existing `RuleCountText` string at 9pt Bold `Foreground="{DynamicResource ComboTextBrush}"` (so a card with 3 CP + 2 bits shows "5"). Hidden when zero via `Visibility="{Binding HasAnyRules, Converter={StaticResource BoolToVisibilityConverter}}"`.

5. **Title** — `Text="{Binding Profile.TargetAvatarName}"`, `Foreground="{DynamicResource TextBrush}"`, `FontSize="12"`, `FontWeight="SemiBold"`, `TextTrimming="CharacterEllipsis"`, `ToolTip` bound to same.

6. **Subtitle** — `Text="{Binding AvatarSubtitle}"`, `Foreground="{DynamicResource MutedBrush}"`, `FontSize="10"` (was 9), `TextTrimming="CharacterEllipsis"`, `ToolTip` bound to same.

7. **Hover state** — `<Trigger Property="IsMouseOver" Value="True">` updates the outer border's `BorderBrush` to `RuleCardHoverBrush`. Standard WPF trigger on the card border.

### 2. Roulette cards

Same structure as swap cards, with these differences:
- Outer border: `BorderBrush="{DynamicResource WarnBrush}"` to preserve the gold-accent distinction.
- Roulette view model exposes `Name` and `Subtitle` directly; no `Profile.TargetAvatarName` indirection, no `HasTarget` (a roulette has no single target avatar — it has a pool of avatars).
- **No `🎭` empty-state hint** for roulette cards. A roulette is always created with a name, so the empty-state hint does not apply. The hero area is just `NestedPanelBrush` background.
- No `HasAnyRules`-gated rule-count pill on the roulette card; the rule-count pill lives only on the swap cards. (Roulette triggers are configured inside the right editor instead.)

### 3. Right editor panel — `IsSwapEditorOpen` Border

- Outer border: `Background="{DynamicResource PanelBrush}"`, `BorderBrush="{DynamicResource BorderBrush}"`, `CornerRadius="6"`, `Padding="10"`.
- 40×40 avatar header: `Background="{DynamicResource NestedPanelBrush}"`, `CornerRadius="5"`.
- "Target Avatar" subtitle: `Foreground="{DynamicResource MutedBrush}"`, `FontSize 10`.
- Avatar name: `Foreground="{DynamicResource TextBrush}"`, `FontSize 13`, `FontWeight="SemiBold"`.
- "↩ Returns to global return avatar" line: `Foreground="{DynamicResource MutedBrush}"`, `FontSize 10`.
- Section headings (🏆 Channel Points, 💎 Bits, ⭐ Subs, 💵 Payment): `Foreground="{DynamicResource TextBrush}"`, `FontSize 12`, `FontWeight="SemiBold"`.
- Browse / Use Current / + Add … buttons: `Style="{StaticResource SecondaryButtonStyle}"`.
- Delete Avatar button: `Style="{StaticResource DangerButtonStyle}"` (which uses `WarnBrush` / `WarnBorderBrush` / `WarnTextBrush` per the existing pattern in `AvatarSetsManagerWindow.xaml:121`).
- Save button: `Style="{StaticResource AccentButtonStyle}"` (which uses `AccentBrush` / `ComboTextBrush` per `AvatarSetsManagerWindow.xaml:112`).
- "Advanced triggers (open full editor)" label: `Foreground="{DynamicResource MutedBrush}"`, `FontSize 10`.

### 4. Power-up clipping fix

Replace the `StackPanel Orientation="Horizontal"` at lines 183-187 of the current XAML with a `WrapPanel`:

```xml
<WrapPanel Orientation="Horizontal">
    <Button Content="💬 Chat Command" Style="{StaticResource SecondaryButtonStyle}" Padding="6,3" Margin="0,0,4,4" />
    <Button Content="👥 Follow" Style="{StaticResource SecondaryButtonStyle}" Padding="6,3" Margin="0,0,4,4" />
    <Button Content="⚡ Power-up" Style="{StaticResource SecondaryButtonStyle}" Padding="6,3" Margin="0,0,4,4" />
</WrapPanel>
```

This guarantees "Power-up" wraps to a new row when the column shrinks below the combined width.

### 5. Roulette editor panel — `IsRouletteEditorOpen` Border

Same panel chrome as the swap editor. "Roulette" title uses `TextBrush` at `FontSize 13`. "Pool" and "Triggers" subheadings use `TextBrush` at `FontSize 12`. The 60×60 pool tile uses `NestedPanelBrush` background. Delete Roulette / Save use the same button styling as the swap editor.

### 6. Window chrome

- Outer `Border` (line 20): `Background="{DynamicResource WindowBackgroundBrush}"`, `BorderBrush="{DynamicResource BorderBrush}"`.
- Title bar `Border` (line 28): `Background="{DynamicResource TitleBarBrush}"`. "Avatar Swap" `TextBlock`: `Foreground="{DynamicResource TitleBarTextBrush}"`, `FontFamily="{DynamicResource HeadingFontFamily}"`.
- Close button (line 31): apply `Style="{StaticResource TitleBarButtonStyle}"` (defined locally in the window's resources with hover/pressed triggers to `TitleBarButtonHoverBrush` / `TitleBarButtonPressedBrush`, following the pattern in `BugReportWindow.xaml:66`).

### 7. Global Return Avatar banner

- Outer border: `PanelBrush` background, `BorderBrush` border.
- "↩ RETURN AVATAR (used by all swaps + roulettes)" heading: `MutedBrush` foreground, `FontSize 10`.
- 32×32 avatar placeholder: `NestedPanelBrush` background.
- Global return name: `TextBrush`.
- Pick… / Use Current / Clear buttons: `SecondaryButtonStyle`.

### 8. List-level controls

- "Avatar Swaps" heading (line 64): `MutedBrush` foreground, `FontSize 10`, `FontWeight="SemiBold"`.
- "+ Add Avatar" button (line 95): `SecondaryButtonStyle`, `HorizontalAlignment="Left"`, `Padding="10,5"`, `Margin="0,6,0,14"`.
- "🎰 Avatar Roulette" heading (line 97): `WarnBrush` foreground (preserves gold accent), `FontSize 10`, `FontWeight="SemiBold"`.
- "+ Add Roulette" button (line 119): `SecondaryButtonStyle`, same layout as Add Avatar.

### 9. `InlineAvatarSwapRuleRowControl.xaml` migration

Scoped to color/border migration only. No new affordances.

- `#0d0a18` → `InputBrush` (TextBox background)
- `#3a2a5a` → `InputBorderBrush` (TextBox border)
- `#e8e3f5` → `TextBrush` (TextBox foreground)
- `#241a3a` → `PanelBrush` (collapsed row + expanded editor backgrounds)
- `#9b86c9` → `MutedBrush` (LabelStyle foreground, 🗑 button foreground)
- `#b0a3d0` → `MutedBrush` ("(Inline editor — fields vary by trigger type)" text)
- `#6b3fa0` → `AccentBrush` (expanded editor border)
- `LabelStyle.FontSize` 9 → 10 for readability parity with the cards.

### 10. Converter and ScrollViewer cleanups

- Remove the local `<BooleanToVisibilityConverter x:Key="BoolToVis" />` declaration from `AvatarSwapManagerWindow.xaml:15`. Define a local `<BooleanToVisibilityConverter x:Key="BoolToVisibilityConverter" />` and `<local:InverseBooleanToVisibilityConverter x:Key="InverseBoolToVisibilityConverter" />` (or pull the existing one in from a `local:` namespace, matching the pattern in `AvatarSetsManagerWindow.xaml:85-86`). Replace `Converter={StaticResource BoolToVis}` with `Converter={StaticResource BoolToVisibilityConverter}` and use `InverseBoolToVisibilityConverter` for the empty-state hint.
- Apply the shared themed `ScrollBar` style + `ScrollBarThumbStyle` + `VerticalScrollBarTemplate` to both `ScrollViewer` instances (lines 125 and 200) for visual consistency with the rest of the app. The styles are copied verbatim from `AvatarSetsManagerWindow.xaml:164-231` into the window's `<Window.Resources>` block.

### 11. Window resources block (new in this window)

This is the biggest concrete addition. The window currently has a 3-line `<Window.Resources>` block (just the `BoolToVis` converter). The new block needs to declare placeholder brushes and the per-window styles, all scoped to this window only (the project pattern is local resources, not global ones — `App.xaml` is intentionally empty).

Concretely, the new `<Window.Resources>` block adds:

**Font family resources** (placeholder values, replaced at runtime by `ThemeManager.ApplyToResources`):
- `BodyFontFamily` — `Verdana` (or the theme's body font)
- `HeadingFontFamily` — `Constantia` (or the theme's heading font)

**Color brush placeholders** (one `SolidColorBrush` per key, with any reasonable hex value as a placeholder; `ThemeManager.ApplyToResources` overwrites them at runtime):
- `WindowBackgroundBrush`, `PanelBrush`, `NestedPanelBrush`, `BorderBrush`, `AccentBrush`, `TextBrush`, `MutedBrush`, `InputBrush`, `InputBorderBrush`, `SecondaryButtonBrush`, `SecondaryButtonBorderBrush`, `WarnBrush`, `WarnBorderBrush`, `WarnTextBrush`, `RuleCardHoverBrush`, `TitleBarBrush`, `TitleBarTextBrush`, `TitleBarSubTextBrush`, `TitleBarButtonBrush`, `TitleBarButtonHoverBrush`, `TitleBarButtonPressedBrush`, `TitleBarCloseHoverBrush`, `TitleBarClosePressedBrush`, `ScrollTrackBrush`, `ScrollThumbBrush`, `ComboTextBrush`.

(Placeholder hex values can match the Void Crystal palette from `ThemeManager.cs:1014-1062` since that's the default theme at startup. The values are written once and immediately overwritten by the theme manager on first paint.)

**Default `TextBlock` and `TextBox` styles** — copied from `AvatarSetsManagerWindow.xaml:88-103` so the whole window inherits the same body font, sizes, and themed text colors.

**Button styles** — `SecondaryButtonStyle`, `AccentButtonStyle`, `DangerButtonStyle`. Copied verbatim from `AvatarSetsManagerWindow.xaml:104-128`.

**Title bar button style** — `TitleBarButtonStyle` with hover/pressed triggers. Copied from `BugReportWindow.xaml:66-96`.

**Scrollbar styles** — `ScrollBarThumbStyle`, `ScrollBarTrackButtonStyle`, `VerticalScrollBarTemplate`, plus the `ScrollBar` default style. Copied from `AvatarSetsManagerWindow.xaml:164-231`.

**Converters** — `BoolToVisibilityConverter` and `InverseBoolToVisibilityConverter`, either declared locally in the window or imported via a `local:` namespace reference (matching the pattern at `AvatarSetsManagerWindow.xaml:85-86`).

The full new `<Window.Resources>` block is approximately 150-180 lines, which is consistent with the resource block size in every other themed window in the project.

### 12. Code-behind wiring (`AvatarSwapManagerWindow.xaml.cs`)

Add two small changes to the constructor and a `ThemeChanged` handler. Pattern copied from `AvatarSetsManagerWindow.xaml.cs:14` and `AvatarSetsManagerWindow.xaml.cs:364`:

```csharp
public AvatarSwapManagerWindow(...)
{
    InitializeComponent();
    ThemeManager.ApplyToResources(Resources, ThemeManager.CurrentTheme);
    ThemeManager.ThemeChanged += OnThemeChanged;
}

private void OnThemeChanged(object? sender, EventArgs e)
{
    Dispatcher.BeginInvoke(() => ThemeManager.ApplyToResources(Resources));
}

protected override void OnClosed(EventArgs e)
{
    ThemeManager.ThemeChanged -= OnThemeChanged;
    base.OnClosed(e);
}
```

Net code-behind delta: ~15 lines.

## Localization

**New keys** (one per language):

- `en-US`: `"Avatar Swap Card Pick Avatar": "Pick Avatar"`
- Each non-English `.extra.json`: a natural-language translation. Translations must follow `AGENTS.md` Localization Translation Quality Rules:
  - Informal register (`du` in de-DE, `tú` in es-ES, `tu` in fr-FR, etc.)
  - Brand names in English: `Crystal Relay`, `Twitch`, `VRChat`, `Bits`, `Subs`
  - No placeholders to preserve (the value is a fixed string)
  - Consistency: pick one natural term for "Pick" within each language and use it for any future "Pick" affordances

The build scripts (`Build-Crystal-Relay-Release.ps1`, `Build-Crystal-Relay-Beta.ps1`, `Build-Crystal-Relay-Test.ps1`) run the localization audit as a pre-flight gate. All three must pass for the change to be promoted.

## Testing

### Build gate

```
dotnet build "E:\!!!Program to work on\Proper Crystal Relay\VrcTwitchOscBridge\VrcTwitchOscBridge.csproj" --no-restore
```

Must complete with 0 errors and no new warnings.

### Unit tests

```
dotnet test "E:\!!!Program to work on\Proper Crystal Relay\VrcTwitchOscBridge.Tests\VrcTwitchOscBridge.Tests.csproj"
```

The existing `AvatarSwapManagerViewModelTests`, `AvatarSwapRuntimeDispatchTests`, and `AvatarSwapMigrationService*Tests` cover the swap behavior we are not changing. Add one small test that exercises `AvatarSwapCardViewModel.HasAnyRules` across all four rule-collection permutations (none, CP only, bits only, all four).

### Localization audit

Run as part of the build pipeline. Must pass with no empty values and no missing keys.

### Visual smoke test

1. Launch via `Launch-Crystal-Relay-Debug.bat`.
2. Open the Avatar Swap manager.
3. Verify with the active theme set to `Void Crystal`:
   - Cards render with the themed palette (no hardcoded purple blocks).
   - Empty cards show `🎭` + "Pick Avatar" centered in the hero area.
   - Status stripe is green on enabled cards, gray on disabled cards.
   - Rule-count pill appears top-right of cards that have any rules; absent otherwise.
   - Hovering a card switches the border to the accent color.
   - Subtitle text is readable (MutedBrush, 10pt).
   - Right editor panel is themed.
   - "Power-up" button wraps to a new line if the right column is narrowed.
   - Inline rule rows in the right editor are themed.
4. Switch the active theme to at least one other theme (`Custom`, `Dream Scape`):
   - All of the above still holds; no leftover hardcoded purple anywhere.
5. Toggle a card's enabled state in the editor and confirm the status stripe updates.

## Verification Gates

Before declaring done:

- [ ] Build green
- [ ] Tests green
- [ ] Localization audit green
- [ ] Visual smoke test in debug launcher passes
- [ ] `AGENTS.md` `Active build lane` and `Active development build` reflect this work (`3.1.10` on `beta1`)
- [ ] `CHANGELOG.txt` updated with a `v3.1.10 beta 1` entry covering the visual overhaul (per the Changelog and Release Notes Workflow in `AGENTS.md`)

## Risks and Open Questions

- **Brush availability across themes.** Every brush we declare as a placeholder in the new `<Window.Resources>` block matches a key in `ThemePaletteFactory` (every built-in theme defines all 26 keys listed in section 11). If a future theme forgets a key, `ThemeManager.ApplyToResources` will create a new entry rather than mutate one, which means the missing key would fall back to no color (effectively transparent) until the theme is fixed. This is a known and accepted pattern in the existing code.
- **Custom theme saturation.** The `Custom` theme lets users pick any color. The status stripe, hover, and empty-state colors all derive from the theme palette, so a user who picks a very dark `TextBrush` and a very light `PanelBrush` may end up with poor contrast on the card text. This is the same risk every other themed card in the app has, and `AGENTS.md` already accepts it as a baseline trade-off.
- **No emulator for tests.** The visual smoke test relies on a human reviewer running the debug launcher. This matches the rest of the project's testing posture.

## Future Work (out of scope)

- Toggle switch on the card (AvatarSets style). Would require either a smaller card or a redesigned card layout.
- Inline rule editing inside the right editor (the "(Inline editor — fields vary by trigger type)" placeholder is a known WIP).
- Upgrading the cards to 280×320 to fully match AvatarSets.
- Drag-to-reorder for the swap and roulette lists.
