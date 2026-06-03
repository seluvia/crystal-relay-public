using VrcTwitchOscBridge.Infrastructure;

namespace VrcTwitchOscBridge.Models;

public sealed class WorldCommandBlacklistSettings : ObservableObject
{
    public const string DefaultWorkerCheckEndpoint =
        "https://crystal-relay-world-guard.screminpal-animation.workers.dev/api/check";

    public const string DefaultWorkerStatusEndpoint =
        "https://crystal-relay-world-guard.screminpal-animation.workers.dev/api/status";

    /// <summary>
    /// World Guard is always-on and cannot be disabled by the user.
    /// This property always returns true.
    /// </summary>
    public bool IsEnabled
    {
        get => true;
        set { /* Always-on: ignore attempts to disable. */ }
    }
}
