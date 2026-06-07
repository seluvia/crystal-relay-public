using System.IO;
using System.Text.RegularExpressions;

namespace VrcTwitchOscBridge.Services;

/// <summary>
/// Reads VRChat's local output log to detect the user's current avatar.
/// This is used as a lightweight helper when Crystal Relay wants to track avatar changes
/// without depending only on direct VRChat API polling.
/// </summary>
internal sealed class VrChatLocalClientStateService
{
    private const int InitialTailByteCount = 262144;
    private const int InstanceLookupTailByteCount = 8388608;
    private static readonly TimeSpan LatestLogRescanInterval = TimeSpan.FromSeconds(30);
    private static readonly Regex AvatarSwitchRegex = new(
        @"\[Behaviour\] Switching (?<player>.+?) to avatar (?<avatar>.+)$",
        RegexOptions.Compiled | RegexOptions.CultureInvariant | RegexOptions.IgnoreCase);
    private static readonly Regex JoinedInstanceRegex = new(
        @"\[(?:Behaviour|RoomManager)\]\s+Joining\s+(?<location>wrld_[0-9a-fA-F-]{36}:[^\s]+)",
        RegexOptions.Compiled | RegexOptions.CultureInvariant | RegexOptions.IgnoreCase);
    private readonly object latestLogStateGate = new();
    private string cachedLatestOutputLogPath = string.Empty;
    private DateTimeOffset nextLatestLogRescanAt = DateTimeOffset.MinValue;

    // Finds VRChat's LocalLow folder where the desktop client writes output logs.
    public static string GetVrChatRootPath()
    {
        var userProfile = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);
        if (string.IsNullOrWhiteSpace(userProfile))
        {
            return string.Empty;
        }

        return Path.Combine(
            userProfile,
            "AppData",
            "LocalLow",
            "VRChat",
            "VRChat");
    }

    public static string GetLatestOutputLogPath()
    {
        var rootPath = GetVrChatRootPath();
        if (string.IsNullOrWhiteSpace(rootPath) || !Directory.Exists(rootPath))
        {
            return string.Empty;
        }

        return FindLatestOutputLogPath(rootPath);
    }

    // Reads only the newest part of the latest output log so Crystal Relay can keep
    // tracking local avatar switches without re-reading the whole file every time.
    public async Task<VrChatLocalLogReadResult> ReadLatestLocalAvatarSwitchAsync(
        string localPlayerDisplayName,
        string? currentLogPath,
        long currentPosition,
        CancellationToken cancellationToken = default)
    {
        var normalizedDisplayName = localPlayerDisplayName?.Trim() ?? string.Empty;
        if (string.IsNullOrWhiteSpace(normalizedDisplayName))
        {
            return new VrChatLocalLogReadResult(currentLogPath ?? string.Empty, currentPosition, string.Empty);
        }

        var latestLogPath = ResolveLatestOutputLogPath(currentLogPath);
        if (string.IsNullOrWhiteSpace(latestLogPath) || !File.Exists(latestLogPath))
        {
            return new VrChatLocalLogReadResult(string.Empty, 0, string.Empty);
        }

        var fileInfo = new FileInfo(latestLogPath);
        var isNewLogFile = !string.Equals(currentLogPath, latestLogPath, StringComparison.OrdinalIgnoreCase);
        var startPosition = isNewLogFile
            ? Math.Max(0, fileInfo.Length - InitialTailByteCount)
            : Math.Min(currentPosition, fileInfo.Length);

        if (!isNewLogFile && startPosition >= fileInfo.Length)
        {
            return new VrChatLocalLogReadResult(latestLogPath, fileInfo.Length, string.Empty);
        }

        using var stream = new FileStream(
            latestLogPath,
            FileMode.Open,
            FileAccess.Read,
            FileShare.ReadWrite | FileShare.Delete);

        stream.Seek(startPosition, SeekOrigin.Begin);

        using var reader = new StreamReader(stream);
        var content = await reader.ReadToEndAsync();
        cancellationToken.ThrowIfCancellationRequested();

        var detectedAvatarName = FindMostRecentLocalAvatarName(content, normalizedDisplayName);
        return new VrChatLocalLogReadResult(latestLogPath, stream.Position, detectedAvatarName ?? string.Empty);
    }

    public async Task<VrChatLocalInstanceLookupResult> ReadLatestJoinedInstanceAsync(
        CancellationToken cancellationToken = default)
    {
        var latestLogPath = ResolveLatestOutputLogPath(currentLogPath: null);
        if (string.IsNullOrWhiteSpace(latestLogPath) || !File.Exists(latestLogPath))
        {
            return VrChatLocalInstanceLookupResult.Unavailable("Crystal Relay could not find a VRChat output log to read.");
        }

        var fileInfo = new FileInfo(latestLogPath);
        var startPosition = Math.Max(0, fileInfo.Length - InstanceLookupTailByteCount);
        using var stream = new FileStream(
            latestLogPath,
            FileMode.Open,
            FileAccess.Read,
            FileShare.ReadWrite | FileShare.Delete);

        stream.Seek(startPosition, SeekOrigin.Begin);

        using var reader = new StreamReader(stream);
        var content = await reader.ReadToEndAsync();
        cancellationToken.ThrowIfCancellationRequested();

        if (!TryFindMostRecentJoinedInstance(content, out var worldId, out var instanceId, out var location))
        {
            return VrChatLocalInstanceLookupResult.Unavailable(
                "Crystal Relay could not find a recent VRChat world instance in the local output log.");
        }

        return VrChatLocalInstanceLookupResult.Available(
            worldId,
            instanceId,
            location,
            VrChatApiClient.BuildLaunchUri(location),
            latestLogPath);
    }

    private string ResolveLatestOutputLogPath(string? currentLogPath)
    {
        var rootPath = GetVrChatRootPath();
        if (string.IsNullOrWhiteSpace(rootPath) || !Directory.Exists(rootPath))
        {
            lock (latestLogStateGate)
            {
                cachedLatestOutputLogPath = string.Empty;
                nextLatestLogRescanAt = DateTimeOffset.MinValue;
            }

            return string.Empty;
        }

        var now = DateTimeOffset.UtcNow;
        lock (latestLogStateGate)
        {
            if (!string.IsNullOrWhiteSpace(currentLogPath) && File.Exists(currentLogPath))
            {
                cachedLatestOutputLogPath = currentLogPath;

                if (now < nextLatestLogRescanAt)
                {
                    return currentLogPath;
                }
            }
            else if (!string.IsNullOrWhiteSpace(cachedLatestOutputLogPath)
                && File.Exists(cachedLatestOutputLogPath)
                && now < nextLatestLogRescanAt)
            {
                return cachedLatestOutputLogPath;
            }

            cachedLatestOutputLogPath = FindLatestOutputLogPath(rootPath);
            nextLatestLogRescanAt = now + LatestLogRescanInterval;
            return cachedLatestOutputLogPath;
        }
    }

    private static string FindLatestOutputLogPath(string rootPath)
    {
        string latestPath = string.Empty;
        DateTime latestWriteTimeUtc = DateTime.MinValue;

        foreach (var path in Directory.EnumerateFiles(rootPath, "output_log_*.txt", SearchOption.TopDirectoryOnly))
        {
            var lastWriteTimeUtc = File.GetLastWriteTimeUtc(path);
            if (lastWriteTimeUtc <= latestWriteTimeUtc)
            {
                continue;
            }

            latestWriteTimeUtc = lastWriteTimeUtc;
            latestPath = path;
        }

        return latestPath;
    }

    // Walks through the captured log chunk and returns the newest avatar switch line
    // that belongs to the local logged-in player.
    private static string? FindMostRecentLocalAvatarName(string content, string localPlayerDisplayName)
    {
        if (string.IsNullOrWhiteSpace(content) || string.IsNullOrWhiteSpace(localPlayerDisplayName))
        {
            return null;
        }

        string? detectedAvatarName = null;
        using var reader = new StringReader(content);

        while (reader.ReadLine() is { } line)
        {
            if (!TryParseLocalAvatarSwitch(line, localPlayerDisplayName, out var avatarName))
            {
                continue;
            }

            detectedAvatarName = avatarName;
        }

        return detectedAvatarName;
    }

    // One-line parser for the VRChat avatar switch log format.
    private static bool TryParseLocalAvatarSwitch(string line, string localPlayerDisplayName, out string avatarName)
    {
        avatarName = string.Empty;
        if (string.IsNullOrWhiteSpace(line) || string.IsNullOrWhiteSpace(localPlayerDisplayName))
        {
            return false;
        }

        var match = AvatarSwitchRegex.Match(line);
        if (!match.Success)
        {
            return false;
        }

        var playerName = match.Groups["player"].Value.Trim();
        if (!string.Equals(playerName, localPlayerDisplayName.Trim(), StringComparison.OrdinalIgnoreCase))
        {
            return false;
        }

        avatarName = match.Groups["avatar"].Value.Trim();
        return !string.IsNullOrWhiteSpace(avatarName);
    }

    private static bool TryFindMostRecentJoinedInstance(
        string content,
        out string worldId,
        out string instanceId,
        out string location)
    {
        worldId = string.Empty;
        instanceId = string.Empty;
        location = string.Empty;
        if (string.IsNullOrWhiteSpace(content))
        {
            return false;
        }

        using var reader = new StringReader(content);
        while (reader.ReadLine() is { } line)
        {
            if (!TryParseJoinedInstance(line, out var parsedWorldId, out var parsedInstanceId, out var parsedLocation))
            {
                continue;
            }

            worldId = parsedWorldId;
            instanceId = parsedInstanceId;
            location = parsedLocation;
        }

        return !string.IsNullOrWhiteSpace(location);
    }

    private static bool TryParseJoinedInstance(
        string line,
        out string worldId,
        out string instanceId,
        out string location)
    {
        worldId = string.Empty;
        instanceId = string.Empty;
        location = string.Empty;
        if (string.IsNullOrWhiteSpace(line))
        {
            return false;
        }

        var match = JoinedInstanceRegex.Match(line);
        if (!match.Success)
        {
            return false;
        }

        var candidate = match.Groups["location"].Value.Trim().TrimEnd('.', ',', ';');
        if (!VrChatApiClient.TryExtractWorldId(candidate, out worldId)
            || !VrChatApiClient.TryExtractInstanceId(candidate, worldId, out instanceId))
        {
            worldId = string.Empty;
            instanceId = string.Empty;
            return false;
        }

        location = $"{worldId}:{instanceId}";
        return true;
    }
}

internal sealed record VrChatLocalLogReadResult(
    string LogPath,
    long NextPosition,
    string AvatarName);

internal sealed record VrChatLocalInstanceLookupResult(
    bool IsAvailable,
    string WorldId,
    string InstanceId,
    string Location,
    string LaunchUri,
    string LogPath,
    string FailureReason)
{
    public static VrChatLocalInstanceLookupResult Unavailable(string failureReason) =>
        new(false, string.Empty, string.Empty, string.Empty, string.Empty, string.Empty, failureReason);

    public static VrChatLocalInstanceLookupResult Available(
        string worldId,
        string instanceId,
        string location,
        string launchUri,
        string logPath) =>
        new(true, worldId, instanceId, location, launchUri, logPath, string.Empty);
}
