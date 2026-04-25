using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Windows.Threading;

namespace VrcTwitchOscBridge.Services;

[Flags]
public enum DesktopInputLockScope
{
    None = 0,
    Movement = 1,
    Turning = 2
}

/// <summary>
/// Runtime-only Windows input blocker for desktop-mode stop-input redeems.
/// Crystal Relay only installs these hooks while a desktop hard lock is active,
/// and an emergency hotkey always releases the lock immediately.
/// </summary>
public sealed class DesktopInputLockService : IAsyncDisposable
{
    private const int WH_KEYBOARD_LL = 13;
    private const int WH_MOUSE_LL = 14;
    private const int WM_KEYDOWN = 0x0100;
    private const int WM_KEYUP = 0x0101;
    private const int WM_SYSKEYDOWN = 0x0104;
    private const int WM_SYSKEYUP = 0x0105;
    private const int WM_MOUSEMOVE = 0x0200;
    private const uint LLKHF_INJECTED = 0x00000010;
    private const uint INPUT_KEYBOARD = 1;
    private const uint KEYEVENTF_KEYUP = 0x0002;
    private const int VK_SHIFT = 0x10;
    private const int VK_CONTROL = 0x11;
    private const int VK_MENU = 0x12;
    private const int VK_F12 = 0x7B;

    private static readonly int[] MovementVirtualKeys =
    [
        0x57, // W
        0x41, // A
        0x53, // S
        0x44, // D
        0x26, // Up
        0x28, // Down
        0x25, // Left
        0x27, // Right
        0x20, // Space
        0x10  // Shift
    ];

    private static readonly int[] TurningVirtualKeys =
    [
        0x51, // Q
        0x45, // E
        0x25, // Left
        0x27  // Right
    ];

    private readonly Dispatcher dispatcher;
    private readonly HookCallback keyboardCallback;
    private readonly HookCallback mouseCallback;
    private IntPtr keyboardHookHandle;
    private IntPtr mouseHookHandle;
    private DesktopInputLockScope currentScope;
    private bool disposed;

    public DesktopInputLockService(Dispatcher dispatcher)
    {
        this.dispatcher = dispatcher;
        keyboardCallback = OnKeyboardHook;
        mouseCallback = OnMouseHook;
    }

    public event Action? EmergencyUnlockTriggered;

    public static string EmergencyHotkeyDisplay => "Ctrl+Alt+Shift+F12";

    public DesktopInputLockScope CurrentScope => currentScope;

    public bool IsActive => currentScope != DesktopInputLockScope.None;

    public async Task SetScopeAsync(DesktopInputLockScope scope, CancellationToken cancellationToken = default)
    {
        var normalizedScope = scope & (DesktopInputLockScope.Movement | DesktopInputLockScope.Turning);
        await InvokeOnDispatcherAsync(() => SetScopeCore(normalizedScope), cancellationToken);
    }

    public async Task ForceReleaseAsync(CancellationToken cancellationToken = default)
    {
        await InvokeOnDispatcherAsync(() => ReleaseCore(triggerEmergencyEvent: false), cancellationToken);
    }

    public async ValueTask DisposeAsync()
    {
        if (disposed)
        {
            return;
        }

        await ForceReleaseAsync();
        disposed = true;
    }

    private async Task InvokeOnDispatcherAsync(Action action, CancellationToken cancellationToken)
    {
        if (dispatcher.CheckAccess())
        {
            cancellationToken.ThrowIfCancellationRequested();
            action();
            return;
        }

        await dispatcher.InvokeAsync(action, DispatcherPriority.Send, cancellationToken);
    }

    private void SetScopeCore(DesktopInputLockScope scope)
    {
        ThrowIfDisposed();

        if (scope == DesktopInputLockScope.None)
        {
            ReleaseCore(triggerEmergencyEvent: false);
            return;
        }

        EnsureHooksInstalled();
        currentScope = scope;
        SendKeyUpBurst(scope);
    }

    private void ReleaseCore(bool triggerEmergencyEvent)
    {
        if (currentScope == DesktopInputLockScope.None
            && keyboardHookHandle == IntPtr.Zero
            && mouseHookHandle == IntPtr.Zero)
        {
            return;
        }

        SendKeyUpBurst(currentScope);
        currentScope = DesktopInputLockScope.None;
        RemoveHooks();

        if (triggerEmergencyEvent)
        {
            EmergencyUnlockTriggered?.Invoke();
        }
    }

    private void EnsureHooksInstalled()
    {
        if (keyboardHookHandle != IntPtr.Zero && mouseHookHandle != IntPtr.Zero)
        {
            return;
        }

        var moduleName = Process.GetCurrentProcess().MainModule?.ModuleName;
        var moduleHandle = string.IsNullOrWhiteSpace(moduleName)
            ? IntPtr.Zero
            : GetModuleHandle(moduleName);

        keyboardHookHandle = SetWindowsHookEx(WH_KEYBOARD_LL, keyboardCallback, moduleHandle, 0);
        if (keyboardHookHandle == IntPtr.Zero)
        {
            throw new InvalidOperationException($"Crystal Relay could not start the desktop input lock keyboard hook. Win32 error {Marshal.GetLastWin32Error()}.");
        }

        mouseHookHandle = SetWindowsHookEx(WH_MOUSE_LL, mouseCallback, moduleHandle, 0);
        if (mouseHookHandle == IntPtr.Zero)
        {
            UnhookWindowsHookEx(keyboardHookHandle);
            keyboardHookHandle = IntPtr.Zero;
            throw new InvalidOperationException($"Crystal Relay could not start the desktop input lock mouse hook. Win32 error {Marshal.GetLastWin32Error()}.");
        }
    }

    private void RemoveHooks()
    {
        if (keyboardHookHandle != IntPtr.Zero)
        {
            UnhookWindowsHookEx(keyboardHookHandle);
            keyboardHookHandle = IntPtr.Zero;
        }

        if (mouseHookHandle != IntPtr.Zero)
        {
            UnhookWindowsHookEx(mouseHookHandle);
            mouseHookHandle = IntPtr.Zero;
        }
    }

    private IntPtr OnKeyboardHook(int code, IntPtr wParam, IntPtr lParam)
    {
        if (code < 0 || currentScope == DesktopInputLockScope.None)
        {
            return CallNextHookEx(IntPtr.Zero, code, wParam, lParam);
        }

        var message = unchecked((int)wParam);
        if (!IsKeyboardMessage(message))
        {
            return CallNextHookEx(IntPtr.Zero, code, wParam, lParam);
        }

        var hookData = Marshal.PtrToStructure<KbdLlHookStruct>(lParam);
        if ((hookData.Flags & LLKHF_INJECTED) != 0)
        {
            return CallNextHookEx(IntPtr.Zero, code, wParam, lParam);
        }

        if (message is WM_KEYDOWN or WM_SYSKEYDOWN
            && hookData.VkCode == VK_F12
            && IsEmergencyHotkeyHeld())
        {
            dispatcher.BeginInvoke(
                DispatcherPriority.Send,
                new Action(() => ReleaseCore(triggerEmergencyEvent: true)));
            return (IntPtr)1;
        }

        if (ShouldBlockKeyboardKey((int)hookData.VkCode))
        {
            return (IntPtr)1;
        }

        return CallNextHookEx(IntPtr.Zero, code, wParam, lParam);
    }

    private IntPtr OnMouseHook(int code, IntPtr wParam, IntPtr lParam)
    {
        if (code < 0 || currentScope == DesktopInputLockScope.None)
        {
            return CallNextHookEx(IntPtr.Zero, code, wParam, lParam);
        }

        var message = unchecked((int)wParam);
        if ((currentScope & DesktopInputLockScope.Turning) != 0
            && message == WM_MOUSEMOVE)
        {
            return (IntPtr)1;
        }

        return CallNextHookEx(IntPtr.Zero, code, wParam, lParam);
    }

    private bool ShouldBlockKeyboardKey(int virtualKey)
    {
        if ((currentScope & DesktopInputLockScope.Movement) != 0
            && MovementVirtualKeys.Contains(virtualKey))
        {
            return true;
        }

        return (currentScope & DesktopInputLockScope.Turning) != 0
            && TurningVirtualKeys.Contains(virtualKey);
    }

    private static bool IsKeyboardMessage(int message) => message is
        WM_KEYDOWN
        or WM_KEYUP
        or WM_SYSKEYDOWN
        or WM_SYSKEYUP;

    private static bool IsEmergencyHotkeyHeld()
    {
        return IsVirtualKeyDown(VK_CONTROL)
            && IsVirtualKeyDown(VK_MENU)
            && IsVirtualKeyDown(VK_SHIFT);
    }

    private static bool IsVirtualKeyDown(int virtualKey) => (GetAsyncKeyState(virtualKey) & 0x8000) != 0;

    private void SendKeyUpBurst(DesktopInputLockScope scope)
    {
        if (scope == DesktopInputLockScope.None)
        {
            return;
        }

        var keyUps = new HashSet<int>();
        if ((scope & DesktopInputLockScope.Movement) != 0)
        {
            foreach (var virtualKey in MovementVirtualKeys)
            {
                keyUps.Add(virtualKey);
            }
        }

        if ((scope & DesktopInputLockScope.Turning) != 0)
        {
            foreach (var virtualKey in TurningVirtualKeys)
            {
                keyUps.Add(virtualKey);
            }
        }

        if (keyUps.Count == 0)
        {
            return;
        }

        var inputs = keyUps
            .Select(virtualKey => new Input
            {
                Type = INPUT_KEYBOARD,
                Data = new InputUnion
                {
                    Keyboard = new KeyboardInput
                    {
                        VirtualKey = (ushort)virtualKey,
                        Flags = KEYEVENTF_KEYUP
                    }
                }
            })
            .ToArray();

        _ = SendInput((uint)inputs.Length, inputs, Marshal.SizeOf<Input>());
    }

    private void ThrowIfDisposed()
    {
        ObjectDisposedException.ThrowIf(disposed, this);
    }

    private delegate IntPtr HookCallback(int code, IntPtr wParam, IntPtr lParam);

    [StructLayout(LayoutKind.Sequential)]
    private struct KbdLlHookStruct
    {
        public uint VkCode;
        public uint ScanCode;
        public uint Flags;
        public uint Time;
        public nint ExtraInfo;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct Input
    {
        public uint Type;
        public InputUnion Data;
    }

    [StructLayout(LayoutKind.Explicit)]
    private struct InputUnion
    {
        [FieldOffset(0)]
        public KeyboardInput Keyboard;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct KeyboardInput
    {
        public ushort VirtualKey;
        public ushort ScanCode;
        public uint Flags;
        public uint Time;
        public nint ExtraInfo;
    }

    [DllImport("user32.dll", SetLastError = true)]
    private static extern IntPtr SetWindowsHookEx(int hookId, HookCallback callback, IntPtr moduleHandle, uint threadId);

    [DllImport("user32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool UnhookWindowsHookEx(IntPtr hookHandle);

    [DllImport("user32.dll")]
    private static extern IntPtr CallNextHookEx(IntPtr hookHandle, int code, IntPtr wParam, IntPtr lParam);

    [DllImport("user32.dll")]
    private static extern short GetAsyncKeyState(int virtualKey);

    [DllImport("user32.dll", SetLastError = true)]
    private static extern uint SendInput(uint inputCount, Input[] inputs, int size);

    [DllImport("kernel32.dll", CharSet = CharSet.Auto, SetLastError = true)]
    private static extern IntPtr GetModuleHandle(string? moduleName);
}
