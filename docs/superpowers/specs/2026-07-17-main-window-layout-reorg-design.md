# Main Window Layout Reorganization

## Summary

Remove the unused right-side "Redeem Workspace" column from the main window
and replace the vertically stacked Redeem Library with a 2x2 navigation grid
that fills the freed horizontal space. Strip all inline Cash Payment rules,
providers, and "Tab Actions" content since every system now opens its own
dedicated manager window.

## Current Layout

```
|  Col 0 (1.14*)   | 20px |  Col 2 (1.2*)    | 20px |  Col 4 (1.66*)   |
| Home / Settings   |      | Redeem Library    |      | Redeem Workspace  |
|                   |      | - nav cards (v)   |      | - Global Return   |
|                   |      | - Tab Actions     |      | - Swap rule ed.   |
|                   |      | - Cash Payments   |      | - empty state     |
|                   |      | - Rules list      |      |                   |
```

Column 4 is no longer active — inline rule editing was replaced by dedicated
manager windows (Avatar Swap Manager, Avatar Scaling Manager, etc.) and the
Global Return Avatar picker has moved into the Avatar Swap Manager.

## Target Layout

```
|     Col 0 (1*)    | 20px |     Col 2 (2*)         |
| Home / Settings    |      | Redeem Library         |
|                    |      | - 2x2 nav card grid    |
```

- Two content columns with a 20px spacer
- Home column keeps existing content (branding, OSC, streaming status)
- Setting sections remain in Column 0, toggled by tab selection
- Redeem Library becomes a pure navigation hub — every entry opens a popup

## Redeem Library: 2×2 Nav Grid

Four cards in a 2-column grid (2 rows):

| Avatar Sets | Avatar Actions |
|---|---|
| Trigger Systems | Viewer Support |

Each card has:
- A section label (uppercase, muted)
- A title
- A one-line description
- Action button(s) that open the relevant manager window

No "Tab Actions" divider. No inline Cash Payment rules list, provider
connections, or bulk action buttons. No empty state placeholders.

## Grid Column Changes

In `MainWindow.xaml` lines 1632-1638, replace the five-column definition:

```xml
<Grid.ColumnDefinitions>
    <ColumnDefinition Width="*" />
    <ColumnDefinition Width="20" />
    <ColumnDefinition Width="2*" />
</Grid.ColumnDefinitions>
```

Remove `Grid.Column="4"` content (lines 3847-4046) entirely — the Grid
containing Global Return Avatar, AvatarSwapRuleEditorControl, and empty
state text blocks.

## Redeem Library Content Changes

In the Column 2 `Border` (lines 3403-3845):

1. Replace the single-column vertical stack of nav cards with a 2×2 grid
2. Remove the "Tab Actions" divider (lines 3559-3583)
3. Remove the inline Cash Payment section (lines 3586-3766) — Add/Enable/
   Disable/Delete buttons, Provider Connections expander with StreamElements/
   Streamlabs/Ko-fi settings
4. Remove the CashPaymentRules ListBox (lines 3773-3809)
5. Remove the empty state MultiDataTrigger TextBlock (lines 3813-3842)

The Redeem Library `Border` visibility triggers stay unchanged
(`IsActivitySectionSelected` and `IsAboutSectionSelected` still collapse
it, `IsSettingsSectionSelected` does not — Settings shares the view).

## Grid.ColumnSpan Updates

Three elements currently span all 5 columns (`Grid.ColumnSpan="5"`) and must
be updated to span the new 3-column layout:

1. **Top nav bar** (line 1640): `Grid.Row="0" Grid.ColumnSpan="5"` → `Grid.ColumnSpan="3"`
2. **Activity section** (line 4048): `Grid.Row="1" Grid.ColumnSpan="5"` → `Grid.ColumnSpan="3"`
3. **About section** (line 4098): `Grid.Row="1" Grid.ColumnSpan="5"` → `Grid.ColumnSpan="3"`

## Affected Files

- `VrcTwitchOscBridge/MainWindow.xaml` — grid columns, ColumnSpan updates,
  Redeem Library content restructure, remove Column 4
- `VrcTwitchOscBridge/ViewModels/MainWindowViewModel.cs` — verify no
  dead bindings reference removed elements

## Out of Scope

- Reworking the Redeem Library nav cards into a tabbed/folded system
- Moving the Global Return Avatar picker (already in Avatar Swap Manager)
- Any changes to Settings tabs, Activity, About, or other sections
