# Movement Redeems UI Redesign

## Summary

Redesign the Movement Redeems manager window to replace the current horizontally-spread card layout with a compact DataGrid table, and polish the editor overlay panel with tighter spacing and better visual hierarchy.

## Motivation

The current Movement Redeems card layout (in `MovementRedeemsManagerWindow.xaml`) stretches each rule horizontally with content spread across a wide card, wasting horizontal space and making it hard to scan many rules at a glance. The editor overlay panel has loose spacing and inconsistent padding.

## Design

### Card List → DataGrid Table

Replace the `ListBox` with a `DataGrid` styled to match the dark custom theme.

**Columns (left to right):**

| Column | Width | Content |
|--------|-------|---------|
| Category | 90px | Color-coded pill: blue (Movement), amber (Turning), green (Hand), red (Object), purple (UI). Same colors as current pills. |
| Direction | 160px | Direction display name, bold, e.g. "Move Forward", "Spin Left" |
| Name | * (flex) | Rule name, text trimming with ellipsis |
| Duration / CD | 100px | "5.0s" or "5.0s / 60s" — combines duration and cooldown |
| Triggers | 140px | Compact icon badges showing active trigger types. Single-letter badges: R (Reward), C (Command), B (Bits), S (Subs), G (GiftSub), F (Follow). Only shown when active. |
| Enabled | 50px | Mini toggle switch (same toggle style as current) |
| Actions | 110px | Three compact icon buttons: ▶ Test, ✏ Edit, 🗑 Delete |

**DataGrid styling:**
- No grid lines (or very subtle 1px BorderBrush lines)
- Row background: transparent (normal), `PanelBrush` (selected/hover)
- Row border: 1px `BorderBrush` on bottom only
- Hover: bright accent border (same `RuleCardHoverBrush` as current)
- Selected: same visual as hover (single selection, no extra highlight)
- Header: dark background matching `TitleBarBrush`, bold text matching `TitleBarTextBrush`, sort arrow styled in accent color
- Row height: ~36px compact
- No alternating rows
- ScrollViewer vertical auto, horizontal disabled

**Interaction:**
- Single-click selects the row
- Double-click opens the editor overlay for that rule
- Clicking column header sorts by that column
- Search/filter bar above the DataGrid stays unchanged

**What stays the same:**
- Window dimensions (1000x700, min 860x540)
- Title bar
- Search & filter bar with category filter buttons
- Editor overlay panel (right side, 480px)

### Editor Panel Polish

Changes to the right-side overlay editor:

**Spacing:**
- Section gap: 12 → 8
- Section padding: 12 → 10 (consistent across all sections)
- Inner sub-section gap: 12 → 8
- UniformGrid column margins: 8 → 6

**Visual hierarchy:**
- Section headers: bold 13px heading text (same as now, but with a subtle 3px colored left border on the section border)
- Sub-section backgrounds: keep `PanelSecondaryBrush` but reduce inner padding from 10 to 8

**Trigger Configuration:**
- Keep the visibility-gated sub-sections (Channel Points, Chat Command, Bits, etc.) as they are
- Just tighten internal spacing

**Footer:**
- Keep the right-aligned button bar: Delete | Test | Cancel | Save
- Reduce margin from 16 to 12

## Files Changed

- `VrcTwitchOscBridge\MovementRedeemsManagerWindow.xaml` — Replace card template and ListBox with DataGrid; tighten editor panel spacing
- `VrcTwitchOscBridge\ViewModels\MovementRedeemCardViewModel.cs` — May need additional properties for DataGrid binding (e.g., trigger badge visibility)
- `VrcTwitchOscBridge\ViewModels\MovementRedeemsManagerViewModel.cs` — May need sort support or data wrappers
- No new files

## Test Impact

- `MovementRedeemsManagerWindowXamlTests.cs` — Update tests that check for the card template pattern to match the new DataGrid structure

## Open Questions

None. Design is approved.
