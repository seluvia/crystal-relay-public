# Streaming Status Illustration Redesign

**Date:** 2026-06-07
**Status:** Approved
**Author:** Crystal Relay Design Session

## Overview

Replace the current Streaming Status card's generic vector screen icon with a **Void Crystal-themed monitor illustration** that changes appearance based on the stream state. The illustration uses vector shapes (not raster images) and features a pulsing gem indicator when the user is live.

## Goals

- Give the Streaming Status card a unique, on-brand visual identity matching the "Void Crystal" aesthetic
- Make the live/offline state visually distinct at a glance
- Keep the illustration as vector shapes so it scales crisply and adapts to any theme color
- Add a gentle pulsing animation to the gem indicator when streaming is active
- Preserve the existing card layout, text, and chip behavior

## Current State

The Streaming Status card (in `MainWindow.xaml` lines ~2149–2353) currently uses:
- A generic rounded-rectangle screen icon with a user silhouette and a dot indicator
- Shape-based styles (`StreamingStatusIllustrationBorderStyle`, `StreamingStatusIllustrationShapeStyle`, `StreamingStatusIndicatorDotStyle`) that change stroke/fill based on `StreamingStatusVisualState` binding
- Two status chips below: "Twitch Stream" and "Twitch Listener"
- The icon is `92×78` and relatively small within the card

## Proposed Design: Void Crystal Monitor

### Visual Elements

The new illustration is a **crystal-cut monitor** with these features:

1. **Octagonal frame** — Angular shard edges instead of rounded corners (`M20 40 L40 20 L180 20 L200 40 L200 120 L180 140 L40 140 L20 120 Z`)
2. **Inner bezel glow** — A secondary path inside the frame for depth, with opacity that strengthens in Live state
3. **Screen area** — Inner octagonal screen with dark fill
4. **Crystal webcam** — A faceted shard shape on top of the monitor instead of a plain circle
5. **Gem dot** — A small circle inside the webcam that acts as the recording indicator
6. **User silhouette** — Simple arc-and-circle figure inside the screen, brighter in Live state
7. **Crystal stand** — An angular, faceted base centered under the monitor
8. **LIVE badge** — A small crystal-shaped badge with "LIVE" text, visible only in Live state
9. **Top-right crystal dot** — A small diamond indicator that also pulses when live
10. **Ambient glow** — A faint outer glow around the entire frame in Live state

### Color Behavior (Live vs. Offline)

| Element | Offline | Live |
|---------|---------|------|
| Frame stroke | `#7552BC` (muted) | `#B16BFF` (accent) |
| Inner bezel | 0.3 opacity | 0.6 opacity |
| Screen fill | `#1C132B` | `#25183D` (slightly brighter) |
| Stand fill | `#2C1C48` | `#3B235B` |
| Webcam stroke | `#7552BC` | `#B16BFF` |
| Gem dot | 0.4 opacity, no glow | Bright, pulsing with glow rings |
| User silhouette | `#7552BC` | `#B16BFF` |
| Top-right dot | Dim | Bright, pulsing |
| LIVE badge | Hidden | Visible (`#E91916`) |
| Outer glow | Hidden | Visible (0.15 opacity) |

### Animation: Gem Pulse

When the stream is **live**, the gem dot and the top-right crystal dot pulse gently:

- **Duration:** 2.5 seconds per cycle (breathing pace)
- **Gem dot opacity:** 0.35 → 1.0
- **Glow rings:** 3 concentric circles around the gem dot that fade in/out
  - Ring 1: radius 7, opacity 0.7 → 0.25
  - Ring 2: radius 11, opacity 0.35 → 0.12
  - Ring 3: radius 15, opacity 0.15 → 0.05
- **Top-right dot:** Same rhythm, smaller scale (2 rings: r=6, r=10)
- **WPF implementation:** Use `DoubleAnimation` on `Opacity` with `RepeatBehavior="Forever"` and `AutoReverse="True"`, driven by a `DataTrigger` on the Live visual state

### Layout Changes

- **Icon size:** Increase from `92×78` to `110×90` (SVG scale from 0.42 to 0.52)
- **Stand:** Centered under the monitor (stand triangle: `95 140 L125 140 L110 152`, base: `80 152 L140 152 L130 162 L90 162`)
- **No text changes:** The "Streaming Status" title, summary, detail, and chip labels remain unchanged
- **No chip behavior changes:** The Twitch Stream / Twitch Listener chips continue working as before

### States Covered

The redesign must visually handle all existing states:

1. **Disconnected** — Broadcaster not connected. Icon dimmed, muted frame.
2. **Error** — Listener in error state. Icon uses DangerBrush tones, no pulse.
3. **Checking/Connecting** — Listener connecting. Icon in "Checking" state (StatusChipBrush), no pulse.
4. **Offline (Healthy)** — Listener connected but stream offline. Icon in Healthy state (accent border, muted gem).
5. **Live** — Stream is live. Icon fully bright, gem pulses, LIVE badge visible.

## Architecture

### Files to Modify

- `VrcTwitchOscBridge/MainWindow.xaml` — Replace the `StreamingStatusIllustrationBorderStyle` and illustration grid (lines ~2151–2306) with the new crystal monitor SVG paths and styles
- `VrcTwitchOscBridge/MainWindow.xaml` — Add animation resources (`DoubleAnimation` for the pulse) within the card's `Border.Resources`
- `VrcTwitchOscBridge/ViewModels/MainWindowViewModel.cs` — No changes needed; the existing `StreamingStatusVisualState` and state machine already drive the correct states
- `VrcTwitchOscBridge/Resources/Localization/*.json` — No new keys needed; all existing text stays the same

### Implementation Notes

- The illustration is entirely **SVG paths inside a WPF `Path` element** — no raster images, no external assets
- The LIVE badge is a `Path` + `TextBlock` inside the same grid, hidden via `Visibility` trigger when not in Live state
- The pulse animation uses WPF `Storyboard` resources that are triggered by `DataTrigger` on the `Tag` (visual state) binding
- The animation must only run when `StreamingStatusVisualState == "Live"` to avoid CPU waste in other states
- The `BeginStoryboard` action should be inside the same `DataTrigger` that sets the bright colors

### Testing

- Verify the card renders correctly in all 5 states (Disconnected, Error, Checking, Offline, Live)
- Verify the gem pulse animation only runs in Live state
- Verify the animation does not cause UI lag or excessive CPU usage
- Verify the icon is crisp at 100% and 125% DPI scaling
- Verify the stand is visually centered under the monitor
- Verify the LIVE badge is hidden in all non-Live states

## Localization

No new UI text is added. The existing localized strings (`"Streaming Status"`, `"You are offline."`, `"Twitch Stream"`, `"Twitch Listener"`) continue to be used. No new localization keys are required.

## SVG Path Reference (Live State)

For the implementation, the crystal monitor SVG paths are:

```xml
<!-- Outer frame -->
<Path Data="M20 40 L40 20 L180 20 L200 40 L200 120 L180 140 L40 140 L20 120 Z" ... />

<!-- Inner bezel glow -->
<Path Data="M24 42 L42 24 L178 24 L196 42 L196 118 L178 136 L42 136 L24 118 Z" ... />

<!-- Screen -->
<Path Data="M30 48 L46 32 L174 32 L190 48 L190 112 L174 128 L46 128 L30 112 Z" ... />

<!-- Stand (centered) -->
<Path Data="M95 140 L125 140 L110 152 Z" ... />
<Path Data="M80 152 L140 152 L130 162 L90 162 Z" ... />

<!-- Webcam crystal shard -->
<Path Data="M100 12 L110 4 L120 12 L115 20 L105 20 Z" ... />

<!-- Gem dot (with glow rings for animation) -->
<Ellipse ... />
<Ellipse ... /> <!-- Ring 1 -->
<Ellipse ... /> <!-- Ring 2 -->
<Ellipse ... /> <!-- Ring 3 -->

<!-- User silhouette -->
<Ellipse ... />
<Path Data="M95 88 Q110 75 125 88" ... />
<Path Data="M88 98 Q110 82 132 98" ... />

<!-- LIVE badge -->
<Path Data="..." Fill="#E91916" ... />
<TextBlock Text="LIVE" ... />

<!-- Top-right crystal dot -->
<Path Data="M175 28 L180 22 L185 28 L180 34 Z" ... />
```

## Open Questions (None)

All design decisions have been resolved during the brainstorming session:
- Visual style: Void Crystal angular/crystal-cut aesthetic
- Icon size: 110×90 (up from 92×78)
- Stand: Centered under the monitor
- Gem pulse: 2.5s breathing cycle, 3 concentric glow rings
- Offline behavior: Dimmed, no animation
- Live badge: Crystal-shaped hexagon with "LIVE" text, red (`#E91916`)
- Implementation: Vector paths only, no raster images

## Decision Log

- **2026-06-07**: User chose direction C (Crystal Display) from 3 options, then refined to Void Crystal style
- **2026-06-07**: Stand centered after user feedback
- **2026-06-07**: Icon size increased from 92×78 to 110×90 after user feedback
- **2026-06-07**: Gem pulse slowed from 1.5s to 2.5s after user feedback
- **2026-06-07**: Spec approved by user