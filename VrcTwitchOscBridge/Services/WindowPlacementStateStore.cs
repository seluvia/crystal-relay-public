using System.IO;
using System.Text.Json;
using System.Windows;

namespace VrcTwitchOscBridge.Services;

internal static class WindowPlacementStateStore
{
    public const string MainWindowKey = "MainWindow";
    public const string TwitchChatboxKey = "TwitchChatbox";
    public const string TestModeKey = "TestMode";
    public const string DebugLogKey = "DebugLog";

    private static readonly JsonSerializerOptions SerializerOptions = new()
    {
        WriteIndented = true
    };

    private static string ChatboxPlacementPath => Path.Combine(AppDataPaths.RootFolder, "chatbox-window-placement.json");

    public static WindowPlacementSnapshot CaptureWindow(Window? window, string windowKey, bool wasOpen = true)
    {
        if (window is null)
        {
            return new WindowPlacementSnapshot
            {
                WindowKey = windowKey,
                WasOpen = false
            };
        }

        var bounds = window.WindowState == WindowState.Normal
            ? new Rect(window.Left, window.Top, window.Width, window.Height)
            : window.RestoreBounds;

        return new WindowPlacementSnapshot
        {
            WindowKey = windowKey,
            WasOpen = wasOpen,
            Left = bounds.Left,
            Top = bounds.Top,
            Width = bounds.Width,
            Height = bounds.Height,
            WindowState = window.WindowState == WindowState.Minimized ? WindowState.Normal : window.WindowState
        };
    }

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
        }

        window.WindowState = snapshot.WindowState == WindowState.Minimized
            ? WindowState.Normal
            : snapshot.WindowState;
    }

    public static void SaveChatboxPlacement(Window window)
    {
        try
        {
            AppDataPaths.EnsureCoreFolders();
            var snapshot = CaptureWindow(window, TwitchChatboxKey);
            var json = JsonSerializer.Serialize(snapshot, SerializerOptions);
            File.WriteAllText(ChatboxPlacementPath, json);
        }
        catch
        {
        }
    }

    public static WindowPlacementSnapshot? TryLoadChatboxPlacement()
    {
        try
        {
            if (!File.Exists(ChatboxPlacementPath))
            {
                return null;
            }

            var json = File.ReadAllText(ChatboxPlacementPath);
            return JsonSerializer.Deserialize<WindowPlacementSnapshot>(json, SerializerOptions);
        }
        catch
        {
            return null;
        }
    }
}

internal sealed class WindowPlacementSnapshot
{
    public string WindowKey { get; set; } = string.Empty;

    public bool WasOpen { get; set; }

    public double Left { get; set; }

    public double Top { get; set; }

    public double Width { get; set; }

    public double Height { get; set; }

    public WindowState WindowState { get; set; } = WindowState.Normal;
}
