namespace VrcTwitchOscBridge.Tests;

internal static class TestWindowsPaths
{
    public static string From(char driveLetter, params string[] parts)
    {
        return driveLetter + @":\" + string.Join(@"\", parts);
    }
}
