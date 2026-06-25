using System.Globalization;
using System.Text;
using VrcTwitchOscBridge.Models;

namespace VrcTwitchOscBridge.Services;

internal sealed record BugReportSnapshotData(
    bool IsBroadcasterConnected,
    bool IsBotConnected,
    bool IsVrChatConnected,
    string OscStatusDetail,
    string CurrentAvatarName,
    string CurrentAvatarId,
    double CurrentAvatarHeightMeters,
    AppTheme SelectedTheme,
    string ThemeDisplayName,
    string AppVersion);

internal static class BugReportSnapshotService
{
    private const int MaxAvatarIdDisplayLength = 20;

    public static string Build(BugReportSnapshotData data)
    {
        var builder = new StringBuilder();
        builder.AppendLine("Crystal Relay Status Snapshot");
        builder.AppendLine($"Twitch broadcaster: {FormatBool(data.IsBroadcasterConnected)}");
        builder.AppendLine($"Twitch bot: {FormatBool(data.IsBotConnected)}");
        builder.AppendLine($"VRChat: {FormatBool(data.IsVrChatConnected)}");
        builder.AppendLine($"OSC: {(string.IsNullOrWhiteSpace(data.OscStatusDetail) ? "Not available" : data.OscStatusDetail)}");

        var avatarName = string.IsNullOrWhiteSpace(data.CurrentAvatarName) ? "Unknown" : data.CurrentAvatarName;
        var avatarId = TruncateAvatarId(data.CurrentAvatarId);
        builder.AppendLine($"Current avatar: {avatarName} ({avatarId})");

        builder.AppendLine($"Eye height: {data.CurrentAvatarHeightMeters.ToString("0.##", CultureInfo.InvariantCulture)} m");
        builder.AppendLine($"Theme: {data.ThemeDisplayName}");
        builder.AppendLine($"App version: {data.AppVersion}");

        return SensitiveTextSanitizer.Sanitize(builder.ToString().TrimEnd());
    }

    private static string FormatBool(bool value) => value ? "Connected" : "Disconnected";

    private static string TruncateAvatarId(string avatarId)
    {
        if (string.IsNullOrWhiteSpace(avatarId))
        {
            return "Unknown";
        }

        return avatarId.Length <= MaxAvatarIdDisplayLength
            ? avatarId
            : avatarId[..MaxAvatarIdDisplayLength] + "...";
    }
}
