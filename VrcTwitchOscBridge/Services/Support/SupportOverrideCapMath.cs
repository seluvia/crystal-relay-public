using System;

namespace VrcTwitchOscBridge.Services.Support;

public static class SupportOverrideCapMath
{
    public static TimeSpan ClampAddedDuration(
        bool capEnabled,
        int capSeconds,
        TimeSpan requestedDuration,
        TimeSpan existingRemainingDuration)
    {
        if (requestedDuration <= TimeSpan.Zero || !capEnabled)
        {
            return requestedDuration;
        }
        var maxAccumulatedDuration = TimeSpan.FromSeconds(Math.Max(1, capSeconds));
        var remainingCapacity = maxAccumulatedDuration - existingRemainingDuration;
        if (remainingCapacity <= TimeSpan.Zero) return TimeSpan.Zero;
        return requestedDuration <= remainingCapacity ? requestedDuration : remainingCapacity;
    }
}
