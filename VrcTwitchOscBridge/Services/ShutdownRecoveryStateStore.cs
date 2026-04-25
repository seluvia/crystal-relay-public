using System;
using System.IO;

namespace VrcTwitchOscBridge.Services;

/// <summary>
/// Tracks whether the previous Crystal Relay session shut down cleanly.
/// A simple marker file lets the next launch decide whether recovery cleanup should run.
/// </summary>
internal static class ShutdownRecoveryStateStore
{
    private static string SessionMarkerPath => Path.Combine(AppDataPaths.RootFolder, "session.recovery.marker");

    // BeginSession returns true when the marker already existed, which means the app
    // likely closed unexpectedly last time and recovery should run.
    public static bool BeginSession()
    {
        try
        {
            AppDataPaths.EnsureCoreFolders();
            var hadUncleanShutdown = File.Exists(SessionMarkerPath);
            File.WriteAllText(SessionMarkerPath, DateTimeOffset.UtcNow.ToString("O"));
            return hadUncleanShutdown;
        }
        catch
        {
            return false;
        }
    }

    // CompleteSession removes the marker once shutdown reached a safe end state.
    public static void CompleteSession()
    {
        try
        {
            if (File.Exists(SessionMarkerPath))
            {
                File.Delete(SessionMarkerPath);
            }
        }
        catch
        {
            // Never allow marker cleanup issues to block shutdown.
        }
    }
}
