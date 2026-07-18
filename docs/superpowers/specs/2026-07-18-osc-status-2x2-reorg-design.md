# OSC Status — 2x2 Reorganization & Bug Fix

## Summary
Reorganize the main window's OSC/connection status display: convert the existing 1×4 status card row into a 2×2 grid, consolidate OSC controls into the Settings right-column section, remove the redundant Streaming Status and old OSC Status panels from the Home panel, and fix the missing PropertyChanged notification that prevents the OSC status card from updating from "Disconnected" to "Connected".

## Changes

### 1. Bug Fix: Missing PropertyChanged for IsOscConnected/IsOscDisconnected
**Root cause:** `IsOscConnected` (line 2499) and `IsOscDisconnected` (line 2501) are computed properties that read `bridgeCoordinator.IsOscActive` but **never** call `RaisePropertyChanged`. Unlike `IsBroadcasterConnected`, `IsBotConnected`, and `IsVrChatConnected` which have explicit notification calls, the OSC connection properties are silently never updated.

**Fix:** Add `RaisePropertyChanged(nameof(IsOscConnected))` and `RaisePropertyChanged(nameof(IsOscDisconnected))` inside `UpdateOscStatusSummary()` (line 16033), which is already called whenever the bridge status changes.

### 2. Remove Streaming Status Panel from Home
- Remove the entire Streaming Status panel (MainWindow.xaml lines 1887–2448) from the left-column Home section.
- This includes the crystal monitor SVG, "Streaming Status" heading, summary/detail text, and Twitch Stream/Twitch Listener status chips.
- Twitch/Stream connection status is now covered by the 2×2 connection status cards.

### 3. Remove Old OSC Status Panel from Home
- Remove the old OSC Status panel (MainWindow.xaml lines 1860–1885) from the left-column Home section.
- This includes the "OSC Status" heading, `OscBridgeSummary`, `OscStatusDetail`, restart buttons, and desktop mode checkbox.
- All of these move to the new consolidated section in Settings.

### 4. Convert 1×4 to 2×2 Connection Status Grid in Settings
- Remove the existing 1×4 row (MainWindow.xaml lines 3581–3775) from the Settings right-column section.
- Replace with a 2×2 grid of status cards:
  - Top-left: VRChat card (`IsVrChatConnected` / `IsVrChatDisconnected`)
  - Top-right: OSC card (`IsOscConnected` / `IsOscDisconnected`)
  - Bottom-left: Twitch card (`IsBroadcasterConnected` / `IsBroadcasterDisconnected`)
  - Bottom-right: Stream card (`IsBroadcasterLive`)
- Each card shows: colored dot (connected/disconnected), label, and status text.

### 5. Add OSC Controls Panel Below the 2×2
- Below the 2×2 grid, add a panel containing:
  - `OscBridgeSummary` text (e.g., "OSC is transmitting and working.")
  - `OscStatusDetail` text (e.g., "VRChat is connected through OSCQuery.")
  - "Restart Crystal Relay" button (calls `OnRestartCrystalRelayClicked`)
  - "Restart VRChat + Crystal Relay" button (calls `OnRestartVrChatAndCrystalRelayClicked`)
  - "Restart VRChat in desktop mode" checkbox (bound to `Settings.RestartVrChatInDesktopMode`)
  - Helper text explaining the desktop mode flag

### 6. Remove Unused ViewModel Properties (Optional Cleanup)
- The Streaming Status panel's ViewModel properties (`StreamingStatusSummary`, `StreamingStatusDetail`, `StreamingStatusVisualState`, crystal monitor styles) become unused.
- Evaluate whether to remove them or leave them (they don't break anything if unused).

## Files Changed

| File | Changes |
|------|---------|
| `VrcTwitchOscBridge/MainWindow.xaml` | Remove Streaming Status panel (~560 lines), remove old OSC Status panel (~25 lines), replace 1×4 status row with 2×2 grid + OSC controls panel (~100 lines) |
| `VrcTwitchOscBridge/ViewModels/MainWindowViewModel.cs` | Add `RaisePropertyChanged` for `IsOscConnected`/`IsOscDisconnected` in `UpdateOscStatusSummary()` (+2 lines) |
