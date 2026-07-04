# Avatar Scaling Manager — Center Panel Layout Rework

## Date
2026-07-01

## Problem
The center panel of the Avatar Scaling Manager stacks the "Child Scale Rewards" section and the pay system reward sections (Supporter Growth, Cash Payments, Power Ups) vertically, one on top of the other. Each section's reward cards are 330px wide in a WrapPanel that only fits one card per row in the available column width (~470px), producing a long vertical scroll with no sense of position or organization.

## Approved Layout

The center panel reorganizes into a clear hierarchy with two side-by-side columns below the Master Unlock Reward:

```
┌─────────────────────────────────────────────────────┐
│  Global Safety Rule                    [Advanced]   │
├─────────────────────────────────────────────────────┤
│  Twitch Reward Scaling                  [Add Redeem]│
├─────────────────────────────────────────────────────┤
│  Master Unlock Reward                                │
│  [Master reward card]                                │
├──────────────────────┬──────────────────────────────┤
│  Child Scale Rewards │  Pay System Rewards          │
│                      │                              │
│  Scale Set 1         │  Supporter Growth            │
│  [card] [card]       │  [card]                      │
│  [card]              │                              │
│                      │  Cash Payments               │
│  Scale Set 2         │  [card]                      │
│  [card] [card]       │                              │
│                      │  Power Ups                   │
│                      │  [card]                      │
└──────────────────────┴──────────────────────────────┘
```

### What stays unchanged
- **Global Safety Rule** card at the top with "Open Advanced Safety" button
- **Twitch Reward Scaling** section header with "Add Scale Redeem" button
- **Master Unlock Reward** section directly below, with its reward card
- **Left navigation panel** (Scaling Sources: Twitch Rewards, Supporter Growth, Cash Payments, Power Ups, All Sources)
- **Right editor panel** (the 3rd column, resizable via GridSplitter, unchanged)
- **Source navigation filter** — clicking a nav item (e.g. "Supporter Growth") still shows/hides sections via `ActiveSourceView` DataTriggers

### What changes
1. **Child Scale Rewards and pay system sections go side-by-side** in a two-column grid below the Master Unlock Reward
   - Left column: Child Scale Rewards (the Twitch channel-point redeems — Scale Set 1, 2, etc.)
   - Right column: Supporter Growth, Cash Payments, and Power Ups stacked within one container

2. **Source-filter visibility still applies** — when the user selects "Twitch Rewards" in the nav, only the left column (Child Scale Rewards) is visible and it spans the full center width. When "Supporter Growth" is selected, only the Supporter Growth sub-section in the right column is visible and it spans the full center width. "All Sources" shows both columns side-by-side. This is implemented via DataTriggers on the Grid's ColumnDefinition widths: when one column has no visible content, its ColumnDefinition Width collapses to 0 so the other column gets full width.

3. **Child Scale Reward cards switch from WrapPanel (330px fixed) to a 2-column grid** inside each scale set
   - Cards auto-size to fill the column width instead of being fixed 330px
   - Two cards per row inside each scale set group
   - Slightly more compact card layout: action summary and safety summary combined to one line ("Set 1.5m · Max: 5m")

4. **Pay system reward cards** use the same compact card style, stacking vertically within their sub-sections (Supporter Growth, Cash Payments, Power Ups each get their own sub-group in the right column)

5. **New "Pay System Rewards" header** — the right column gets a section header "Pay System Rewards" with subtitle "Supporter Growth, Cash Payments & Power Ups" to match the "Child Scale Rewards" header on the left. This requires new localization keys for the header and subtitle text, translated into all non-English languages.

### XAML changes required

#### 1. Wrap the Child Scale Rewards Border and pay system Borders in a two-column Grid

Current structure (lines ~906-1051): four separate `<Border>` elements stacked vertically inside the center `<ScrollViewer>`, each with its own `ActiveSourceView` visibility triggers.

New structure:
```xml
<!-- Master Unlock Reward: stays as-is, above the two-column area -->
<Border> ... Master Unlock Reward ... </Border>

<!-- Two-column container -->
<Grid>
  <Grid.ColumnDefinitions>
    <ColumnDefinition Width="*" MinWidth="200" />
    <ColumnDefinition Width="12" />  <!-- spacer -->
    <ColumnDefinition Width="*" MinWidth="200" />
  </Grid.ColumnDefinitions>

  <!-- Left column: Child Scale Rewards -->
  <Border Grid.Column="0">
    <!-- existing ActiveSourceView triggers: TwitchRewards, AllSources -->
    ... Child Scale Rewards with 2-column grid inside ...
  </Border>

  <!-- Right column: Pay System Rewards -->
  <StackPanel Grid.Column="2">
    <Border> ... Supporter Growth (existing triggers) ... </Border>
    <Border> ... Cash Payments (existing triggers) ... </Border>
    <Border> ... Power Ups (existing triggers) ... </Border>
  </StackPanel>
</Grid>
```

#### 2. Change the Child Scale Rewards ItemsControl from WrapPanel to a 2-column UniformGrid or Grid

Current (line ~980-985):
```xml
<ItemsControl ItemsPanel>
  <ItemsPanelTemplate>
    <WrapPanel />
  </ItemsPanelTemplate>
</ItemsControl>
<ItemsControl.ItemContainerStyle>
  <Style TargetType="ContentPresenter">
    <Setter Property="Width" Value="330" />
    <Setter Property="Margin" Value="0,0,10,10" />
  </Style>
</ItemsControl.ItemContainerStyle>
```

New:
```xml
<ItemsControl ItemsPanel>
  <ItemsPanelTemplate>
    <UniformGrid Columns="2" />
  </ItemsPanelTemplate>
</ItemsControl>
<ItemsControl.ItemContainerStyle>
  <Style TargetType="ContentPresenter">
    <Setter Property="Margin" Value="0,0,8,8" />
  </Style>
</ItemsControl.ItemContainerStyle>
```

#### 3. Make SourceCardTemplate more compact

The `SourceCardTemplate` DataTemplate (lines 707-739) currently shows:
- Title + StatusText (header row)
- ActionSummary (separate line)
- SafetySummary (separate line)
- Edit button

Compact version combines ActionSummary and SafetySummary into a single line and reduces padding from 12 to 10.

### Tests to update

- `Window_TwitchRewardsPageUsesRewardFocusedBrainstormLayout` — may need adjustment if it checks for WrapPanel or specific card widths
- `Window_SourceCardsForceReadableTextBrushes` — should still pass (brushes unchanged)
- Any test referencing `WrapPanel` in the Child Scale Rewards area
- `Window_BrainstormLayoutStringsAreLocalizedInAllExtraFiles` — add new keys ("Pay System Rewards", "Supporter Growth, Cash Payments & Power Ups") to expected keys list

### Localization
New keys to add to all `.extra.json` files:
- `"Pay System Rewards"` — header for the right column
- `"Supporter Growth, Cash Payments & Power Ups"` — subtitle for the right column

### Risks
- The two-column layout needs enough horizontal space. With the 190px nav + 370px editor + splitters, the center column is ~470px. Split into two columns with a 12px gap, each column gets ~229px — enough for compact cards but tight. The GridSplitter on the editor panel (added in a prior fix) lets the user widen the center by dragging the editor narrower.
- When a source filter (e.g. "Supporter Growth") is active, only the right column shows. The left column's ColumnDefinition collapses to Width=0, giving the right column the full center width.
- When "Twitch Rewards" is active, only the left column shows. The right column's ColumnDefinition collapses to Width=0.
- The 2-column UniformGrid inside scale sets means cards are ~100px wide when the center column is at its minimum — very narrow. Cards need to handle text truncation gracefully (TextTrimming/TextWrapping).
