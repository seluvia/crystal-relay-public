# Design: Live Watch Version/Channel Badges for Dev Tool

## Date
2026-06-07

## Context
The `crystal-relay-live-list` dev tool (`CrystalRelayLiveList.exe`) shows a "Live Watch" view of streamers currently using Crystal Relay. The Cloudflare live worker already collects `relayVersion` and `buildChannel` from each heartbeat. The dev tool already receives this data via `/api/live` and stores it in `LiveUserViewModel` and `LiveHistoryEntryViewModel`. Currently, the UI buries this info in a plain-text `DetailText` string.

## Goal
Make the Crystal Relay version and build channel (stable / beta / test) visually prominent and easy to scan at a glance in the **Live Now** cards.

## Scope
- **In scope:** Live Now card UI badges only
- **Out of scope:** 24h History cards (keep existing inline text), Cloudflare worker changes, main app heartbeat changes

## Design

### Badge Layout
In the Live Now card, the top row is a `Grid` with two columns: name on the left, `LIVE` pill on the right. We insert a `WrapPanel` between them holding the two badges.

```
[Streamer Name]   [Version Badge] [Channel Badge]   [LIVE pill]
```

### Badge Styling
Both badges use the existing pill/badge style from the XAML resources:
- **Version badge** (`3.1.8`, `3.1.9-beta1`, etc.): Uses `LivePillBrush` with reduced opacity, or a new subtle violet-tinted brush. Small text, bold, rounded pill.
- **Build channel badge** (`stable`, `beta`, `test`): Uses a distinct accent color (e.g., pink `#FF78D8` tint) to differentiate from version.
- Font: ~11px, bold, dark text on light background, `CornerRadius="999"`.

### Data Binding
- Add two new readonly properties to `LiveUserViewModel`:
  - `VersionBadgeText` — returns `RelayVersion` or empty string
  - `ChannelBadgeText` — returns `BuildChannel` or empty string
- Update `LiveUserViewModel.DetailText` to exclude version and build channel (they're now shown as badges). Keep only the `Last heartbeat` timestamp.
- `LiveHistoryEntryViewModel` stays unchanged.

### Files to Modify
1. `tools/private/crystal-relay-live-list/MainWindow.xaml` — Add badge elements inside the live card `DataTemplate`
2. `tools/private/crystal-relay-live-list/MainWindow.xaml.cs` — Add `VersionBadgeText` and `ChannelBadgeText` to `LiveUserViewModel`, update `DetailText` constructor logic

### No-Go Zones
- Do not change the 24h History card layout
- Do not add new fields to the Cloudflare worker or the heartbeat payload
- Do not modify the main Crystal Relay app
- Do not add new resources or styles if existing ones can be reused

## Risks
- Badge text could be long (e.g., `3.1.9-beta1`). The `WrapPanel` will wrap, which is acceptable.
- Missing version/channel data should hide the badge gracefully (collapsed when empty).

## Success Criteria
- Live Now cards show version and channel as visually distinct pills
- Detail text below the badges shows only the heartbeat timestamp
- History cards remain unchanged
- Build still passes
