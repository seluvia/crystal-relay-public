# Loading Animation Redesign — HUD Scanner

## Overview

Replace Crystal Relay's current decorative looping loading overlay with a **HUD Scanner**-style sci-fi/holographic loading system that shows real progress through initialization phases and provides a smooth reveal transition.

## Design Decisions

- **Approach:** Improved in-window overlay (no separate splash window)
- **Visual Style:** Sci-fi / holographic HUD scanner
- **Loading Phases:** 5 visible phases:
  1. Loading Settings
  2. Connecting to VRChat
  3. Syncing Twitch Rewards
  4. Starting OSC Bridge
  5. Finalizing

## Layout (z-order, back to front)

1. Dark background with subtle scan lines
2. Top header bar: thin glowing scan line
3. "CRYSTAL RELAY" label in dim accent color
4. Holographic crystal icon (center):
   - Radial glow behind crystal
   - Crystal image with holographic projection effect
   - Subtle bottom glow line
5. Scanning line (sweeps top→bottom, looping)
6. HUD Status Panel (below crystal):
   - "INITIALIZATION SEQUENCE" header
   - 5 phase rows with `[OK]` / `[--]` tags
   - Active phase: pulsing ◉ indicator
   - Completed phase: checkmark ✓
7. Version + elapsed time (bottom-right)

## Animations

| Storyboard | Effect |
|---|---|
| `HologramIdle` | Crystal rotates 360°/8s + gentle pulse scale 1.0↔1.05 + glow oscillation |
| `ScanLine` | Thin line sweeps top→bottom over 3s, looping, with glow shadow |
| `PhasePulse` | Active phase ◉ pulses; brief flash on phase completion |
| `HudEntrance` | Startup power-on flicker (opacity 0→0.4→0.8→0.6→1.0 over 500ms) + HUD panel slide-up |

## Reveal Transition (replaces instant-hide)

| Step | Animation | Duration |
|---|---|---|
| 1 | "All systems operational" sign-off fades into last row | 300ms |
| 2 | Hold for readability | 700ms |
| 3 | Overlay opacity 1→0, crystal glow dims first | 500ms |

## Architecture

### LoadingPhaseService (new file)
- `ObservableCollection<LoadingPhase> Phases`
- `event Action<LoadingPhase> PhaseChanged`
- `void ReportProgress(string key, PhaseStatus status)`
- `void CompleteAll()`

### LoadingPhase model
- `Key: string`, `Label: string`, `Status: enum` (Pending/Active/Completed/Failed)

### Changes to InitializeAsync()
Inject `_loadingService.ReportProgress()` calls at phase boundaries in `MainWindowViewModel.InitializeAsync()`. Each phase boundary marks the previous phase Completed and the next phase Active.

### Changes to MainWindow.xaml.cs
- `OnLoaded`: same flow (show → start → await → reveal), but `StartLoadingAnimations` uses new storyboard keys
- Subscribe to `LoadingPhaseService.PhaseChanged` to update HUD rows
- Drive reveal transition after `InitializeAsync()` completes

### Changes to MainWindow.xaml
- Replace `LoadingOverlay` content with HUD Scanner layout
- Replace 6 old storyboards with 4 new ones
- Add `ItemsControl` for phase list binding
- Add reveal transition storyboard

## Files Changed

| File | Change |
|---|---|
| `Services/LoadingPhaseService.cs` | NEW |
| `ViewModels/MainWindowViewModel.cs` | Inject progress reports in InitializeAsync |
| `MainWindow.xaml` | Replace LoadingOverlay content + storyboards |
| `MainWindow.xaml.cs` | Update animation keys, add phase subscription, drive reveal |

## Out of Scope

- No changes to secondary window loading (chatbox, test windows)
- No changes to the ViewModel constructor loading (fast path)
- No separate splash window
- No changes to the updater's loading experience
