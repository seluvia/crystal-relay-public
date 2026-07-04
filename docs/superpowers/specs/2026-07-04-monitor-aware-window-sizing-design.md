# Monitor-Aware Window Sizing

## Problem

The Crystal Relay main window has a hardcoded default size of 1540×940 and a minimum of 1280×820. On a small display such as a 15-inch TV (typically 1366×768), the window overflows the visible area and taskbar, cutting off UI features. There is no mechanism to constrain the window to the current monitor's working area.

## Solution

Three targeted changes that together ensure the window always fits within the visible working area of whatever monitor it is displayed on:

### 1. Extend `WM_GETMINMAXINFO` to constrain all states (not just maximized)

**File:** `MainWindow.xaml.cs` — `ApplyMaximizedWorkArea()` → rename to `ConstrainToWorkArea()`

**Current behavior:** The `WM_GETMINMAXINFO` hook only modifies `MaxSize` and `MaxPosition` for the maximized state, leaving normal/resize tracking unconstrained.

**New behavior:**
- Always set `MaxSize` (maximized size) to the current monitor's working area dimensions
- Always set `MaxTrackSize` (maximum resize tracking size) to the working area — this prevents the user from dragging the window corner larger than the screen
- If `MinTrackSize` (from XAML `MinWidth`/`MinHeight`) exceeds `MaxTrackSize` in either dimension (e.g., MinHeight 820 on a display with 728px usable height), clamp `MinTrackSize` down to `MaxTrackSize`. This means on very small displays the window becomes effectively fixed-size, but it fits and remains usable.

**Implementation notes:**
- The existing `MonitorFromWindow` + `GetMonitorInfo` P/Invoke calls are reused as-is
- Called from `WindowMessageHook` on `WM_GETMINMAXINFO` (0x0024), same as today, just with updated logic
- Applies to ALL windows, not just MainWindow (TwitchChatboxWindow also has the same message hook pattern)

### 2. Add DPI change handling

**File:** `MainWindow.xaml.cs` — new handler

- Handle `WM_DPICHANGED` (0x02E0) in the existing `WindowMessageHook`
- When DPI changes, call `InvalidateVisual()` to trigger clean re-render
- The `WM_GETMINMAXINFO` message fires automatically after a DPI change, so `ConstrainToWorkArea()` re-evaluates against the correct monitor working area without explicit re-triggering

### 3. Off-screen position recovery in `WindowPlacementStateStore`

**File:** `WindowPlacementStateStore.cs` — extend `ApplyWindowPlacement()`

**Current behavior:** Restores saved `Left`/`Top` without checking whether the position is still visible on an available monitor. If the monitor was disconnected, the window lands off-screen.

**New behavior:**
- After applying saved placement, check whether the window rectangle intersects any available monitor's working area
- Use `System.Windows.Forms.Screen.AllScreens` to enumerate monitors (already available — project has `UseWindowsForms=true`)
- If the saved position is completely off-screen, reset to `WindowStartupLocation = CenterScreen` and discard saved coordinates

## Files Changed

| File | Change |
|------|--------|
| `MainWindow.xaml.cs` | Extend `WM_GETMINMAXINFO` handler to constrain all states; add DPI change handler |
| `WindowPlacementStateStore.cs` | Add off-screen position validation on restore |

## Edge Cases

- **Min size exceeds working area:** `MinTrackSize` is clamped to `MaxTrackSize` in `ConstrainToWorkArea()`, so the window can never be forced larger than the screen
- **Window dragged from large monitor to small monitor:** Windows re-sends `WM_GETMINMAXINFO` when the window moves to a new monitor, `ConstrainToWorkArea()` re-evaluates against the new monitor's working area
- **Multiple monitors with different taskbar positions:** Each monitor's `MONITORINFO.WorkArea` correctly reflects its own taskbar, and `MonitorFromWindow` resolves the nearest monitor
- **Taskbar auto-hide:** Windows adjusts `WorkArea` dynamically; `ConstrainToWorkArea()` captures the current state at the time of the `WM_GETMINMAXINFO` message
- **Saved off-screen position:** Detected in `WindowPlacementStateStore` and recovered to center-screen

## Non-Goals

- Not changing the XAML `MinWidth`/`MinHeight` values themselves
- Not adding scrollbars or reflowing UI for sub-minimum sizes
- Not touching secondary windows (Chatbox, dialogs) unless they exhibit the same issue — they are smaller and less likely to overflow
- Not altering the user's ability to freely resize the window on normal-sized monitors

## Secondary Windows

If the same overflow issue appears on secondary windows (checked during implementation), the same pattern applies: extend their `WM_GETMINMAXINFO` handlers identically.
