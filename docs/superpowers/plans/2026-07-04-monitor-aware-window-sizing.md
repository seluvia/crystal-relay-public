# Monitor-Aware Window Sizing Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Always constrain the main window to the current monitor's working area so it never overflows the taskbar or screen edges on small displays.

**Architecture:** Extend the existing `WM_GETMINMAXINFO` hook to clamp `MaxTrackSize`, `MaxSize`, and `MinTrackSize` to the monitor's working area at all times (not just when maximized). Add `WM_DPICHANGED` handling for clean per-monitor DPI transitions. Add off-screen position recovery to prevent the window from restoring invisibly on a disconnected monitor.

**Tech Stack:** WPF, user32 P/Invoke, System.Windows.Forms (Screen)

---

### Task 1: Extend `WM_GETMINMAXINFO` to constrain all window states

**Files:**
- Modify: `VrcTwitchOscBridge\MainWindow.xaml.cs` — lines 2340-2384

- [ ] **Step 1: Rename `ApplyMaximizedWorkArea` to `ConstrainToWorkArea` and update its logic**

  Change the call site in `WindowMessageHook`:
  ```csharp
  if (msg == WmGetMinMaxInfo)
  {
      ConstrainToWorkArea(hwnd, lParam);
      handled = true;
  }
  ```

  Replace the method body. The new logic always clamps `MaxSize` and `MaxTrackSize` to the working area, and if `MinTrackSize` exceeds the working area, clamps that too:
  ```csharp
  private static void ConstrainToWorkArea(IntPtr hwnd, IntPtr lParam)
  {
      var minMaxInfo = Marshal.PtrToStructure<MinMaxInfo>(lParam);
      var monitor = MonitorFromWindow(hwnd, MonitorDefaultToNearest);
      if (monitor == IntPtr.Zero)
      {
          return;
      }

      var monitorInfo = new MonitorInfo();
      monitorInfo.Size = Marshal.SizeOf<MonitorInfo>();

      if (!GetMonitorInfo(monitor, ref monitorInfo))
      {
          return;
      }

      var workArea = monitorInfo.WorkArea;
      var monitorArea = monitorInfo.MonitorArea;

      var workWidth = Math.Abs(workArea.Right - workArea.Left);
      var workHeight = Math.Abs(workArea.Bottom - workArea.Top);

      // Always constrain maximized size to working area
      minMaxInfo.MaxPosition.X = Math.Abs(workArea.Left - monitorArea.Left);
      minMaxInfo.MaxPosition.Y = Math.Abs(workArea.Top - monitorArea.Top);
      minMaxInfo.MaxSize.X = workWidth;
      minMaxInfo.MaxSize.Y = workHeight;

      // Always constrain max tracking size (resize boundary) to working area
      // Windows already initializes MaxTrackSize to the virtual screen size;
      // we just clamp it down to the current monitor's working area.
      if (minMaxInfo.MaxTrackSize.X > workWidth)
          minMaxInfo.MaxTrackSize.X = workWidth;
      if (minMaxInfo.MaxTrackSize.Y > workHeight)
          minMaxInfo.MaxTrackSize.Y = workHeight;

      // If minimum size exceeds working area, clamp minimum down to fit
      if (minMaxInfo.MinTrackSize.X > minMaxInfo.MaxTrackSize.X)
          minMaxInfo.MinTrackSize.X = minMaxInfo.MaxTrackSize.X;
      if (minMaxInfo.MinTrackSize.Y > minMaxInfo.MaxTrackSize.Y)
          minMaxInfo.MinTrackSize.Y = minMaxInfo.MaxTrackSize.Y;

      Marshal.StructureToPtr(minMaxInfo, lParam, true);
  }
  ```

- [ ] **Step 2: Build to verify compilation**

  Run:
  ```powershell
  dotnet build "E:\!!!Program to work on\Proper Crystal Relay\VrcTwitchOscBridge\VrcTwitchOscBridge.csproj" --no-restore
  ```

  Expected: Build succeeds with no errors.

---

### Task 2: Add `WM_DPICHANGED` handler for per-monitor DPI transitions

**Files:**
- Modify: `VrcTwitchOscBridge\MainWindow.xaml.cs`

- [ ] **Step 1: Add the `WmDpiChanged` constant alongside the existing `WmGetMinMaxInfo` constant (line 27)**

  Replace line 27:
  ```csharp
  private const int WmDpiChanged = 0x02E0;
  private const int WmGetMinMaxInfo = 0x0024;
  ```

  (Order doesn't strictly matter, but keep `WmDpiChanged` first for grouping new with old.)

- [ ] **Step 2: Add the `WM_DPICHANGED` case to `WindowMessageHook`**

  In the `WindowMessageHook` method (line 2340), add a new case before the `WmGetMinMaxInfo` check:
  ```csharp
  if (msg == WmDpiChanged)
  {
      InvalidateVisual();
      return IntPtr.Zero;
  }
  ```

  The full `WindowMessageHook` becomes:
  ```csharp
  private IntPtr WindowMessageHook(IntPtr hwnd, int msg, IntPtr wParam, IntPtr lParam, ref bool handled)
  {
      if (msg == App.ActivateExistingWindowMessageId)
      {
          BringWindowToFront();
          handled = true;
          return IntPtr.Zero;
      }

      if (msg == WmDpiChanged)
      {
          InvalidateVisual();
          return IntPtr.Zero;
      }

      if (msg == WmGetMinMaxInfo)
      {
          ConstrainToWorkArea(hwnd, lParam);
          handled = true;
      }

      return IntPtr.Zero;
  }
  ```

- [ ] **Step 3: Build to verify compilation**

  ```powershell
  dotnet build "E:\!!!Program to work on\Proper Crystal Relay\VrcTwitchOscBridge\VrcTwitchOscBridge.csproj" --no-restore
  ```

  Expected: Build succeeds.

---

### Task 3: Add off-screen position recovery to `WindowPlacementStateStore`

**Files:**
- Modify: `VrcTwitchOscBridge\Services\WindowPlacementStateStore.cs`

- [ ] **Step 1: Add `using System.Windows.Forms;` to the imports**

  The file currently has:
  ```csharp
  using System.IO;
  using System.Text.Json;
  using System.Windows;
  ```

  Add `using System.Windows.Forms;` (the project has `<UseWindowsForms>true</UseWindowsForms>`, so this is available):
  ```csharp
  using System.IO;
  using System.Text.Json;
  using System.Windows;
  using System.Windows.Forms;
  ```

  To avoid ambiguity with `System.Windows.Window`, keep the explicit `Window` reference as-is (it already qualifies correctly).

- [ ] **Step 2: Add off-screen visibility check after setting saved position**

  In `ApplyWindowPlacement`, after the block that sets `Left`/`Top` (lines 65-70), add an off-screen check:
  ```csharp
  if (double.IsFinite(snapshot.Left) && double.IsFinite(snapshot.Top))
  {
      window.Left = snapshot.Left;
      window.Top = snapshot.Top;
      window.WindowStartupLocation = WindowStartupLocation.Manual;

      if (snapshot.Width > 0 && snapshot.Height > 0)
      {
          var windowRect = new System.Drawing.Rectangle(
              (int)snapshot.Left, (int)snapshot.Top,
              (int)snapshot.Width, (int)snapshot.Height);

          var isOnAnyScreen = false;
          foreach (Screen screen in Screen.AllScreens)
          {
              if (screen.WorkingArea.IntersectsWith(windowRect))
              {
                  isOnAnyScreen = true;
                  break;
              }
          }

          if (!isOnAnyScreen)
          {
              window.WindowStartupLocation = WindowStartupLocation.CenterScreen;
          }
      }
  }
  ```

  The full updated method:
  ```csharp
  public static void ApplyWindowPlacement(Window window, WindowPlacementSnapshot? snapshot)
  {
      if (snapshot is not { WasOpen: true })
      {
          return;
      }

      if (snapshot.Width >= window.MinWidth && snapshot.Width < 10000)
      {
          window.Width = snapshot.Width;
      }

      if (snapshot.Height >= window.MinHeight && snapshot.Height < 10000)
      {
          window.Height = snapshot.Height;
      }

      if (double.IsFinite(snapshot.Left) && double.IsFinite(snapshot.Top))
      {
          window.Left = snapshot.Left;
          window.Top = snapshot.Top;
          window.WindowStartupLocation = WindowStartupLocation.Manual;

          if (snapshot.Width > 0 && snapshot.Height > 0)
          {
              var windowRect = new System.Drawing.Rectangle(
                  (int)snapshot.Left, (int)snapshot.Top,
                  (int)snapshot.Width, (int)snapshot.Height);

              var isOnAnyScreen = false;
              foreach (Screen screen in Screen.AllScreens)
              {
                  if (screen.WorkingArea.IntersectsWith(windowRect))
                  {
                      isOnAnyScreen = true;
                      break;
                  }
              }

              if (!isOnAnyScreen)
              {
                  window.WindowStartupLocation = WindowStartupLocation.CenterScreen;
              }
          }
      }

      window.WindowState = snapshot.WindowState == WindowState.Minimized
          ? WindowState.Normal
          : snapshot.WindowState;
  }
  ```

- [ ] **Step 3: Build to verify compilation**

  ```powershell
  dotnet build "E:\!!!Program to work on\Proper Crystal Relay\VrcTwitchOscBridge\VrcTwitchOscBridge.csproj" --no-restore
  ```

  Expected: Build succeeds.

---

### Task 4: Verify end-to-end build

- [ ] **Step 1: Full build**

  ```powershell
  dotnet build "E:\!!!Program to work on\Proper Crystal Relay\VrcTwitchOscBridge\VrcTwitchOscBridge.csproj" --no-restore
  ```

  Expected: Build succeeds with 0 errors, 0 warnings.

- [ ] **Step 2: Verify the debug launcher runs without crash**

  ```powershell
  dotnet build "E:\!!!Program to work on\Proper Crystal Relay\VrcTwitchOscBridge\VrcTwitchOscBridge.csproj" --no-restore -c Debug
  ```
  Expected: Build succeeds.
