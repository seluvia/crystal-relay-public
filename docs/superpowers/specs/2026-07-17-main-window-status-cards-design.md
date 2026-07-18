# Main Window Status Cards Design

## Problem

The Redeem Library 2x2 nav grid (right panel, default workspace view) has empty space below the four nav cards when the window is at typical sizes. Status information like VRChat connection, OSC status, Twitch connection, and stream state is currently only visible on the left Home panel — users must scroll or switch sections to check it.

## Design

Add a horizontal row of four compact status cards below the 2x2 nav grid, filling the empty space with always-visible status indicators.

### Layout

```
┌─────────────────────────────────────────────────────────────┐
│  Redeem Library                                              │
│  ┌──────────┐ ┌──────────┐  ┌──────────┐ ┌──────────┐      │
│  │Avatar    │ │Avatar    │  │Trigger   │ │Viewer    │      │
│  │Sets      │ │Actions   │  │Systems   │ │Support   │      │
│  └──────────┘ └──────────┘  └──────────┘ └──────────┘      │
│  ┌──────────┐ ┌──────────┐  ┌──────────┐ ┌──────────┐      │
│  │● VRChat  │ │● OSC     │  │● Twitch  │ │● Stream  │      │
│  │Connected │ │Listening │  │Connected │ │Live      │      │
│  └──────────┘ └──────────┘  └──────────┘ └──────────┘      │
└─────────────────────────────────────────────────────────────┘
```

- Sits in the same `StackPanel` (line 3420 of `MainWindow.xaml`) below the 2x2 grid
- Separated by `Margin="0,16,0,0"` from the grid
- Uses a horizontal `WrapPanel` or `Grid` with 4 `Border` cards
- Each card uses existing panel styling (`NestedPanelBrush` background, `HighlightBorderBrush` border, `CornerRadius="14"`, `Padding="14,10"`)

### Cards

| Card | Label | Status Binding | Dot Color Logic |
|------|-------|---------------|-----------------|
| VRChat | "VRChat" | `VrChatStatus` | Green if contains "Connected", gray otherwise |
| OSC | "OSC" | `OscBridgeSummary` | Green if contains "Live" or "Listening" or "connected", red if "error"/"offline"/"attention", gray otherwise |
| Twitch | "Twitch" | `BroadcasterStatusDisplayText` | Green if "Connected" or "Live", red if "error"/"missing"/"reconnect", gray otherwise |
| Stream | "Stream" | Localized "Live" / "Offline" derived from `IsBroadcasterLive` | Green if live, gray if offline |

### Visual Details

- Colored dot: 10x10 `Ellipse` left of the label with `Margin="0,0,8,0"`
- Label text: small muted font (10-11px), `MutedBrush`
- Status text: semi-bold below the label, `TextBrush`
- Cards are equal-width, distributing available space via `UniformGrid` columns or `Width="*"`
- Cards use the `Tag` property or a `DataTrigger` binding pattern (like `StreamingStatusChipBorderStyle` in the Home panel) for dot color

### ViewModel Changes

No new ViewModel properties needed — all bindings exist already:
- `VrChatStatus` (line 2437)
- `OscBridgeSummary` (line 2371)
- `BroadcasterStatusDisplayText` (line 2349)
- `IsBroadcasterLive` (line 2166)

### Files Changed

1. `VrcTwitchOscBridge/MainWindow.xaml` — add the status card row below the 2x2 grid

### Out of Scope

- Not duplicating the elaborate Crystal Monitor visual from the Home panel
- Not adding new properties or services — pure layout change using existing data
- Not modifying the Home panel status cards
