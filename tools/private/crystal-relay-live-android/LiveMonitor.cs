using System.Net;
using System.Text.Json;
using Android;
using Android.App;
using Android.Content;
using Android.Content.PM;
using Android.OS;

namespace CrystalRelayLiveAndroid;

internal static class LiveWatchConstants
{
    public const string DefaultEndpoint = "https://crystal-relay-live-worker.screminpal-animation.workers.dev/api/ping";
    public const string NotificationChannelId = "crystal_relay_live_watch";
    public const string CheckAction = "dev.seluvia.crystalrelay.livewatch.CHECK_LIVE";
    public const int NotificationId = 4215;
    public const int CheckRequestCode = 4215;
    public const int CheckIntervalMinutes = 15;
    public const long CheckIntervalMillis = CheckIntervalMinutes * 60 * 1000L;
}

internal static class LiveSettings
{
    private const string PreferencesName = "crystal_relay_live_watch";
    private const string EndpointKey = "endpoint";
    private const string NotificationsEnabledKey = "notifications_enabled";
    private const string HasSnapshotKey = "has_snapshot";
    private const string KnownLiveKeysKey = "known_live_keys";

    public static string GetEndpoint(Context context)
    {
        return Preferences(context).GetString(EndpointKey, LiveWatchConstants.DefaultEndpoint)
            ?? LiveWatchConstants.DefaultEndpoint;
    }

    public static void SaveEndpoint(Context context, string endpoint)
    {
        Preferences(context).Edit()?.PutString(EndpointKey, endpoint.Trim())?.Apply();
    }

    public static bool GetNotificationsEnabled(Context context)
    {
        return Preferences(context).GetBoolean(NotificationsEnabledKey, true);
    }

    public static void SaveNotificationsEnabled(Context context, bool enabled)
    {
        Preferences(context).Edit()?.PutBoolean(NotificationsEnabledKey, enabled)?.Apply();
    }

    public static bool HasSnapshot(Context context)
    {
        return Preferences(context).GetBoolean(HasSnapshotKey, false);
    }

    public static HashSet<string> GetKnownLiveKeys(Context context)
    {
        var saved = Preferences(context).GetString(KnownLiveKeysKey, string.Empty) ?? string.Empty;
        return saved
            .Split('\n', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
            .ToHashSet(StringComparer.OrdinalIgnoreCase);
    }

    public static void SaveSnapshot(Context context, IEnumerable<string> liveKeys)
    {
        var saved = string.Join('\n', liveKeys.Distinct(StringComparer.OrdinalIgnoreCase));
        Preferences(context)
            .Edit()
            ?.PutBoolean(HasSnapshotKey, true)
            ?.PutString(KnownLiveKeysKey, saved)
            ?.Apply();
    }

    public static void ResetSnapshot(Context context)
    {
        Preferences(context)
            .Edit()
            ?.PutBoolean(HasSnapshotKey, false)
            ?.Remove(KnownLiveKeysKey)
            ?.Apply();
    }

    private static ISharedPreferences Preferences(Context context)
    {
        return context.GetSharedPreferences(PreferencesName, FileCreationMode.Private)!;
    }
}

internal static class LiveEndpointTools
{
    public static Uri? BuildLiveApiUri(string endpoint)
    {
        if (string.IsNullOrWhiteSpace(endpoint)
            || !Uri.TryCreate(endpoint.Trim(), UriKind.Absolute, out var uri)
            || !string.IsNullOrEmpty(uri.UserInfo)
            || (uri.Scheme != Uri.UriSchemeHttps && uri.Scheme != Uri.UriSchemeHttp))
        {
            return null;
        }

        var builder = new UriBuilder(uri)
        {
            Query = string.Empty,
            Fragment = string.Empty
        };

        var path = builder.Path.TrimEnd('/');
        if (path.EndsWith("/api/live", StringComparison.OrdinalIgnoreCase))
        {
            builder.Path = path;
        }
        else if (path.EndsWith("/api/ping", StringComparison.OrdinalIgnoreCase))
        {
            builder.Path = string.Concat(path.AsSpan(0, path.Length - "/api/ping".Length), "/api/live");
        }
        else
        {
            builder.Path = string.IsNullOrWhiteSpace(path) || path == "/"
                ? "/api/live"
                : $"{path}/api/live";
        }

        return builder.Uri;
    }
}

internal static class LiveMonitorClient
{
    private static readonly HttpClient HttpClient = new()
    {
        Timeout = TimeSpan.FromSeconds(12)
    };

    public static async Task<LiveMonitorResult> CheckAsync(Context context, bool notifyOnNewLive)
    {
        var appContext = context.ApplicationContext ?? context;
        var endpoint = LiveSettings.GetEndpoint(appContext);
        var liveUri = LiveEndpointTools.BuildLiveApiUri(endpoint);
        if (liveUri is null)
        {
            return new LiveMonitorResult(
                [],
                "Endpoint is not configured.",
                "Not refreshed yet.");
        }

        using var response = await HttpClient.GetAsync(liveUri).ConfigureAwait(false);
        if (response.StatusCode == HttpStatusCode.NotFound)
        {
            return new LiveMonitorResult(
                [],
                "The live endpoint was not found.",
                $"Last attempt: {DateTimeOffset.Now:g}");
        }

        response.EnsureSuccessStatusCode();
        var json = await response.Content.ReadAsStringAsync().ConfigureAwait(false);
        var parsed = ParseLiveList(json);
        var currentKeys = parsed.Users
            .Select(CreateLiveUserKey)
            .ToHashSet(StringComparer.OrdinalIgnoreCase);

        var newUsers = Array.Empty<LiveUserInfo>();
        if (LiveSettings.HasSnapshot(appContext))
        {
            var knownKeys = LiveSettings.GetKnownLiveKeys(appContext);
            newUsers = parsed.Users
                .Where(user => !knownKeys.Contains(CreateLiveUserKey(user)))
                .ToArray();
        }

        LiveSettings.SaveSnapshot(appContext, currentKeys);

        if (notifyOnNewLive
            && LiveSettings.GetNotificationsEnabled(appContext)
            && newUsers.Length > 0)
        {
            LiveNotificationService.ShowLiveNotification(appContext, newUsers, parsed.Users.Count);
        }

        var updatedAt = parsed.UpdatedAt?.ToLocalTime().ToString("g") ?? DateTimeOffset.Now.ToString("g");
        var status = parsed.Users.Count == 1
            ? "1 Crystal Relay user is live."
            : $"{parsed.Users.Count} Crystal Relay users are live.";

        return new LiveMonitorResult(
            parsed.Users,
            status,
            $"Last updated: {updatedAt}");
    }

    private static ParsedLiveList ParseLiveList(string json)
    {
        using var document = JsonDocument.Parse(json);
        var root = document.RootElement;
        DateTimeOffset? updatedAt = null;
        var users = new List<LiveUserInfo>();

        if (root.TryGetProperty("updatedAt", out var updatedAtElement)
            && updatedAtElement.ValueKind == JsonValueKind.String
            && DateTimeOffset.TryParse(updatedAtElement.GetString(), out var parsedUpdatedAt))
        {
            updatedAt = parsedUpdatedAt;
        }

        if (!root.TryGetProperty("users", out var usersElement)
            || usersElement.ValueKind != JsonValueKind.Array)
        {
            return new ParsedLiveList(updatedAt, users);
        }

        foreach (var userElement in usersElement.EnumerateArray())
        {
            var displayName = ReadString(userElement, "displayName");
            var twitchUrl = ReadString(userElement, "twitchUrl");
            if (string.IsNullOrWhiteSpace(displayName) || string.IsNullOrWhiteSpace(twitchUrl))
            {
                continue;
            }

            DateTimeOffset? lastPingAt = null;
            var lastPingValue = ReadString(userElement, "lastPingAt");
            if (DateTimeOffset.TryParse(lastPingValue, out var parsedLastPing))
            {
                lastPingAt = parsedLastPing;
            }

            users.Add(new LiveUserInfo(
                displayName.Trim(),
                twitchUrl.Trim(),
                ReadString(userElement, "relayVersion").Trim(),
                ReadString(userElement, "buildChannel").Trim(),
                lastPingAt));
        }

        return new ParsedLiveList(updatedAt, users);
    }

    private static string ReadString(JsonElement element, string propertyName)
    {
        return element.TryGetProperty(propertyName, out var value) && value.ValueKind == JsonValueKind.String
            ? value.GetString() ?? string.Empty
            : string.Empty;
    }

    private static string CreateLiveUserKey(LiveUserInfo user)
    {
        return !string.IsNullOrWhiteSpace(user.TwitchUrl)
            ? user.TwitchUrl.Trim()
            : user.DisplayName.Trim();
    }
}

internal static class LiveNotificationService
{
    public static void EnsureChannel(Context context)
    {
        if (Build.VERSION.SdkInt < BuildVersionCodes.O)
        {
            return;
        }

        var manager = (NotificationManager?)context.GetSystemService(Context.NotificationService);
        var channel = new NotificationChannel(
            LiveWatchConstants.NotificationChannelId,
            "Crystal Relay live alerts",
            NotificationImportance.Default)
        {
            Description = "Alerts when a Crystal Relay streamer newly goes live."
        };

        manager?.CreateNotificationChannel(channel);
    }

    public static void ShowLiveNotification(Context context, IReadOnlyList<LiveUserInfo> newUsers, int totalLiveCount)
    {
        if (!HasNotificationPermission(context))
        {
            return;
        }

        EnsureChannel(context);

        var title = newUsers.Count == 1
            ? $"{newUsers[0].DisplayName} is live"
            : $"{newUsers.Count} streamers went live";
        var text = newUsers.Count == 1
            ? newUsers[0].TwitchUrl
            : $"{totalLiveCount} Crystal Relay users are live now.";
        var bigText = string.Join(", ", newUsers.Select(user => user.DisplayName));

        var launchIntent = new Intent(context, typeof(MainActivity));
        launchIntent.SetFlags(ActivityFlags.ClearTop | ActivityFlags.SingleTop);
        var pendingIntent = PendingIntent.GetActivity(
            context,
            0,
            launchIntent,
            PendingIntentFlags.UpdateCurrent | PendingIntentFlags.Immutable);

        Notification.Builder builder = Build.VERSION.SdkInt >= BuildVersionCodes.O
            ? new Notification.Builder(context, LiveWatchConstants.NotificationChannelId)
            : new Notification.Builder(context);

        builder
            .SetSmallIcon(Resource.Drawable.ic_crystal_notification)
            .SetContentTitle(title)
            .SetContentText(text)
            .SetStyle(new Notification.BigTextStyle().BigText(newUsers.Count == 1 ? text : bigText))
            .SetContentIntent(pendingIntent)
            .SetAutoCancel(true)
            .SetShowWhen(true)
            .SetWhen(Java.Lang.JavaSystem.CurrentTimeMillis());

        if (Build.VERSION.SdkInt < BuildVersionCodes.O)
        {
            builder.SetDefaults(NotificationDefaults.Sound | NotificationDefaults.Vibrate | NotificationDefaults.Lights);
            builder.SetPriority((int)NotificationPriority.Default);
        }

        var manager = (NotificationManager?)context.GetSystemService(Context.NotificationService);
        manager?.Notify(LiveWatchConstants.NotificationId, builder.Build());
    }

    private static bool HasNotificationPermission(Context context)
    {
        return Build.VERSION.SdkInt < BuildVersionCodes.Tiramisu
            || context.CheckSelfPermission(Manifest.Permission.PostNotifications) == Permission.Granted;
    }
}

internal static class LiveAlarmScheduler
{
    public static void Schedule(Context context)
    {
        var manager = (AlarmManager?)context.GetSystemService(Context.AlarmService);
        if (manager is null)
        {
            return;
        }

        var pendingIntent = CreatePendingIntent(context);
        manager.SetInexactRepeating(
            AlarmType.ElapsedRealtimeWakeup,
            SystemClock.ElapsedRealtime() + LiveWatchConstants.CheckIntervalMillis,
            LiveWatchConstants.CheckIntervalMillis,
            pendingIntent);
    }

    public static void Cancel(Context context)
    {
        var manager = (AlarmManager?)context.GetSystemService(Context.AlarmService);
        manager?.Cancel(CreatePendingIntent(context));
    }

    private static PendingIntent CreatePendingIntent(Context context)
    {
        var intent = new Intent(context, typeof(LiveCheckReceiver));
        intent.SetAction(LiveWatchConstants.CheckAction);
        return PendingIntent.GetBroadcast(
            context,
            LiveWatchConstants.CheckRequestCode,
            intent,
            PendingIntentFlags.UpdateCurrent | PendingIntentFlags.Immutable)!;
    }
}

[BroadcastReceiver(Enabled = true, Exported = false)]
[IntentFilter([LiveWatchConstants.CheckAction])]
public sealed class LiveCheckReceiver : BroadcastReceiver
{
    public override void OnReceive(Context? context, Intent? intent)
    {
        if (context is null)
        {
            return;
        }

        var pendingResult = GoAsync();
        _ = Task.Run(async () =>
        {
            try
            {
                await LiveMonitorClient.CheckAsync(context.ApplicationContext ?? context, true).ConfigureAwait(false);
            }
            catch
            {
                // Background checks should never crash the receiver.
            }
            finally
            {
                pendingResult?.Finish();
            }
        });
    }
}

[BroadcastReceiver(Enabled = true, Exported = true)]
[IntentFilter([Intent.ActionBootCompleted])]
public sealed class BootReceiver : BroadcastReceiver
{
    public override void OnReceive(Context? context, Intent? intent)
    {
        if (context is null || !LiveSettings.GetNotificationsEnabled(context))
        {
            return;
        }

        LiveAlarmScheduler.Schedule(context);
    }
}

internal sealed record LiveMonitorResult(
    IReadOnlyList<LiveUserInfo> Users,
    string StatusText,
    string LastUpdatedText);

internal sealed record LiveUserInfo(
    string DisplayName,
    string TwitchUrl,
    string RelayVersion,
    string BuildChannel,
    DateTimeOffset? LastPingAt)
{
    public string DetailText
    {
        get
        {
            var details = new List<string>();
            if (!string.IsNullOrWhiteSpace(RelayVersion))
            {
                details.Add($"Crystal Relay {RelayVersion}");
            }

            if (!string.IsNullOrWhiteSpace(BuildChannel))
            {
                details.Add(BuildChannel);
            }

            if (LastPingAt is { } lastPing)
            {
                details.Add($"Last heartbeat {lastPing.ToLocalTime():g}");
            }

            return details.Count > 0 ? string.Join(" | ", details) : "Live heartbeat active.";
        }
    }
}

internal sealed record ParsedLiveList(DateTimeOffset? UpdatedAt, IReadOnlyList<LiveUserInfo> Users);
