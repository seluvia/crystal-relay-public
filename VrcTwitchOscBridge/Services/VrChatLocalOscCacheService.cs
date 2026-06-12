using System.IO;
using System.Text.Json;
using VrcTwitchOscBridge.Models;

namespace VrcTwitchOscBridge.Services;

/// <summary>
/// Reads VRChat's local OSC avatar JSON files.
/// Crystal Relay uses this to learn avatar names and saved OSC parameters directly from the local client cache.
/// </summary>
internal sealed class VrChatLocalOscCacheService
{
    private static readonly JsonSerializerOptions LocalOscJsonOptions = new()
    {
        PropertyNameCaseInsensitive = true
    };

    private readonly Dictionary<string, CachedAvatarFileEntry> cachedAvatarFilesByPath = new(StringComparer.OrdinalIgnoreCase);
    private string cachedAvatarFolderPath = string.Empty;

    // Loads the local list of avatar JSON files written by VRChat's OSC system.
    public async Task<IReadOnlyList<LocalVrChatOscAvatarSummary>> LoadKnownAvatarsAsync(
        string vrChatUserId,
        CancellationToken cancellationToken = default)
    {
        var folderPath = GetAvatarOscFolderPath(vrChatUserId);
        if (string.IsNullOrWhiteSpace(folderPath) || !Directory.Exists(folderPath))
        {
            return [];
        }

        var normalizedFolderPath = Path.GetFullPath(folderPath);
        if (!string.Equals(cachedAvatarFolderPath, normalizedFolderPath, StringComparison.OrdinalIgnoreCase))
        {
            cachedAvatarFolderPath = normalizedFolderPath;
            cachedAvatarFilesByPath.Clear();
        }

        var avatarFiles = Directory.GetFiles(folderPath, "*.json", SearchOption.TopDirectoryOnly);
        var knownAvatarFilePaths = new HashSet<string>(avatarFiles, StringComparer.OrdinalIgnoreCase);
        RemoveMissingAvatarFileEntries(knownAvatarFilePaths);

        foreach (var avatarFilePath in avatarFiles)
        {
            cancellationToken.ThrowIfCancellationRequested();

            var lastWriteTimeUtc = File.GetLastWriteTimeUtc(avatarFilePath);
            if (cachedAvatarFilesByPath.TryGetValue(avatarFilePath, out var cachedEntry)
                && cachedEntry.LastWriteTimeUtc == lastWriteTimeUtc)
            {
                continue;
            }

            cachedAvatarFilesByPath[avatarFilePath] = await LoadAvatarFileEntryAsync(
                avatarFilePath,
                lastWriteTimeUtc,
                cancellationToken);
        }

        return cachedAvatarFilesByPath.Values
            .Where(entry => entry.Avatar is not null)
            .Select(entry => entry.Avatar!)
            .GroupBy(avatar => avatar.AvatarId, StringComparer.Ordinal)
            .Select(group => group.OrderByDescending(avatar => avatar.LastWriteTimeUtc).First())
            .OrderByDescending(avatar => avatar.LastWriteTimeUtc)
            .ToList();
    }

    public async Task<LocalVrChatOscAvatarSummary?> LoadLatestKnownAvatarAsync(
        string vrChatUserId,
        CancellationToken cancellationToken = default)
    {
        var avatars = await LoadKnownAvatarsAsync(vrChatUserId, cancellationToken);
        return avatars.FirstOrDefault();
    }

    // Loads one avatar's OSC parameter list from VRChat's local cache file and maps it
    // into the parameter shapes Crystal Relay uses in the editor.
    public async Task<IReadOnlyList<VrChatOscParameterSummary>> LoadAvatarParametersAsync(
        string vrChatUserId,
        string avatarId,
        CancellationToken cancellationToken = default)
    {
        var filePath = GetAvatarOscFilePath(vrChatUserId, avatarId);
        if (string.IsNullOrWhiteSpace(filePath) || !File.Exists(filePath))
        {
            return [];
        }

        return await LoadAvatarParametersFromFileAsync(filePath, cancellationToken);
    }

    // Loads avatar parameters by scanning all user folders in the OSC directory.
    // This is a fallback when the VRChat user ID is not known.
    public async Task<IReadOnlyList<VrChatOscParameterSummary>> LoadAvatarParametersByAvatarIdAsync(
        string avatarId,
        CancellationToken cancellationToken = default)
    {
        var filePath = FindAvatarOscFilePathByAvatarId(avatarId);
        if (string.IsNullOrWhiteSpace(filePath) || !File.Exists(filePath))
        {
            return [];
        }

        return await LoadAvatarParametersFromFileAsync(filePath, cancellationToken);
    }

    private async Task<IReadOnlyList<VrChatOscParameterSummary>> LoadAvatarParametersFromFileAsync(
        string filePath,
        CancellationToken cancellationToken)
    {
        try
        {
            await using var stream = File.OpenRead(filePath);
            var payload = await JsonSerializer.DeserializeAsync<LocalOscAvatarFile>(
                stream,
                LocalOscJsonOptions,
                cancellationToken);
            if (payload?.Parameters is null || payload.Parameters.Count == 0)
            {
                return [];
            }

            var parameters = payload.Parameters
                .Select(SelectBestEndpoint)
                .Where(endpoint => endpoint is not null)
                .Select(endpoint => endpoint!)
                .Where(endpoint => !string.IsNullOrWhiteSpace(endpoint.Address) && TryMapParameterType(endpoint.Type, out _))
                .Select(endpoint =>
                {
                    TryMapParameterType(endpoint.Type, out var parameterType);
                    var normalizedAddress = VrChatOscClient.NormalizeAvatarParameterAddress(endpoint.Address!);
                    var displayName = string.IsNullOrWhiteSpace(endpoint.Name)
                        ? normalizedAddress.Split('/', StringSplitOptions.RemoveEmptyEntries).LastOrDefault() ?? normalizedAddress
                        : endpoint.Name.Trim();

                    return new VrChatOscParameterSummary(normalizedAddress, displayName, parameterType);
                })
                .GroupBy(parameter => parameter.Address, StringComparer.OrdinalIgnoreCase)
                .Select(group => group.First())
                .OrderBy(parameter => parameter.Name, StringComparer.OrdinalIgnoreCase)
                .ThenBy(parameter => parameter.Address, StringComparer.OrdinalIgnoreCase)
                .ToList();

            return parameters;
        }
        catch
        {
            return [];
        }
    }

    // Resolves the LocalLow OSC avatar folder for one VRChat user ID.
    public static string GetAvatarOscFolderPath(string vrChatUserId)
    {
        var normalizedUserId = vrChatUserId?.Trim() ?? string.Empty;
        if (string.IsNullOrWhiteSpace(normalizedUserId))
        {
            return string.Empty;
        }

        var rootPath = VrChatLocalClientStateService.GetVrChatRootPath();
        if (string.IsNullOrWhiteSpace(rootPath))
        {
            return string.Empty;
        }

        return Path.Combine(
            rootPath,
            "OSC",
            normalizedUserId,
            "Avatars");
    }

    public static string GetAvatarOscFilePath(string vrChatUserId, string avatarId)
    {
        var normalizedAvatarId = avatarId?.Trim() ?? string.Empty;
        if (string.IsNullOrWhiteSpace(normalizedAvatarId))
        {
            return string.Empty;
        }

        var avatarFolderPath = GetAvatarOscFolderPath(vrChatUserId);
        if (string.IsNullOrWhiteSpace(avatarFolderPath))
        {
            return string.Empty;
        }

        return Path.Combine(avatarFolderPath, $"{normalizedAvatarId}.json");
    }

    // Finds the avatar OSC file by scanning all user folders in the OSC directory.
    // This is a fallback when the VRChat user ID is not known.
    public static string? FindAvatarOscFilePathByAvatarId(string avatarId)
    {
        var normalizedAvatarId = avatarId?.Trim() ?? string.Empty;
        if (string.IsNullOrWhiteSpace(normalizedAvatarId))
        {
            return null;
        }

        var rootPath = VrChatLocalClientStateService.GetVrChatRootPath();
        if (string.IsNullOrWhiteSpace(rootPath))
        {
            return null;
        }

        var oscRoot = Path.Combine(rootPath, "OSC");
        if (!Directory.Exists(oscRoot))
        {
            return null;
        }

        try
        {
            foreach (var userDir in Directory.GetDirectories(oscRoot, "*", SearchOption.TopDirectoryOnly))
            {
                var avatarsDir = Path.Combine(userDir, "Avatars");
                if (!Directory.Exists(avatarsDir))
                {
                    continue;
                }

                var avatarFilePath = Path.Combine(avatarsDir, $"{normalizedAvatarId}.json");
                if (File.Exists(avatarFilePath))
                {
                    return avatarFilePath;
                }
            }
        }
        catch
        {
            // Ignore directory access errors during scan
        }

        return null;
    }

    // VRChat parameter files can contain separate input/output endpoints.
    // Crystal Relay prefers output when available because that is what it sends back into VRChat.
    private static LocalOscEndpoint? SelectBestEndpoint(LocalOscParameter parameter)
    {
        if (!string.IsNullOrWhiteSpace(parameter.Output?.Address))
        {
            return new LocalOscEndpoint(parameter.Name, parameter.Output.Address, parameter.Output.Type);
        }

        if (!string.IsNullOrWhiteSpace(parameter.Input?.Address))
        {
            return new LocalOscEndpoint(parameter.Name, parameter.Input.Address, parameter.Input.Type);
        }

        return null;
    }

    // Converts raw OSC type strings from the local file into Crystal Relay's parameter enum.
    private static bool TryMapParameterType(string? oscType, out OscParameterType parameterType)
    {
        parameterType = OscParameterType.Int;
        if (string.IsNullOrWhiteSpace(oscType))
        {
            return false;
        }

        switch (oscType.Trim().ToLowerInvariant())
        {
            case "bool":
            case "boolean":
                parameterType = OscParameterType.Bool;
                return true;
            case "int":
            case "integer":
                parameterType = OscParameterType.Int;
                return true;
            case "float":
            case "single":
                parameterType = OscParameterType.Float;
                return true;
            default:
                return false;
        }
    }

    private void RemoveMissingAvatarFileEntries(IReadOnlySet<string> knownAvatarFilePaths)
    {
        if (cachedAvatarFilesByPath.Count == 0)
        {
            return;
        }

        List<string>? removedPaths = null;
        foreach (var cachedPath in cachedAvatarFilesByPath.Keys)
        {
            if (!knownAvatarFilePaths.Contains(cachedPath))
            {
                removedPaths ??= [];
                removedPaths.Add(cachedPath);
            }
        }

        if (removedPaths is null)
        {
            return;
        }

        foreach (var removedPath in removedPaths)
        {
            cachedAvatarFilesByPath.Remove(removedPath);
        }
    }

    private static async Task<CachedAvatarFileEntry> LoadAvatarFileEntryAsync(
        string avatarFilePath,
        DateTime lastWriteTimeUtc,
        CancellationToken cancellationToken)
    {
        try
        {
            await using var stream = File.OpenRead(avatarFilePath);
            var payload = await JsonSerializer.DeserializeAsync<LocalOscAvatarFile>(
                stream,
                LocalOscJsonOptions,
                cancellationToken);
            if (payload is null || string.IsNullOrWhiteSpace(payload.Id))
            {
                return new CachedAvatarFileEntry(lastWriteTimeUtc, null);
            }

            var avatarId = payload.Id.Trim();
            var avatarName = string.IsNullOrWhiteSpace(payload.Name) ? avatarId : payload.Name.Trim();
            return new CachedAvatarFileEntry(
                lastWriteTimeUtc,
                new LocalVrChatOscAvatarSummary(avatarId, avatarName, avatarFilePath, lastWriteTimeUtc));
        }
        catch
        {
            return new CachedAvatarFileEntry(lastWriteTimeUtc, null);
        }
    }

    private sealed class LocalOscAvatarFile
    {
        public string? Id { get; set; }

        public string? Name { get; set; }

        public List<LocalOscParameter>? Parameters { get; set; }
    }

    private sealed class LocalOscParameter
    {
        public string? Name { get; set; }

        public LocalOscParameterEndpoint? Input { get; set; }

        public LocalOscParameterEndpoint? Output { get; set; }
    }

    private sealed class LocalOscParameterEndpoint
    {
        public string? Address { get; set; }

        public string? Type { get; set; }
    }

    private sealed record LocalOscEndpoint(string? Name, string? Address, string? Type);

    private sealed record CachedAvatarFileEntry(DateTime LastWriteTimeUtc, LocalVrChatOscAvatarSummary? Avatar);
}

internal sealed record LocalVrChatOscAvatarSummary(
    string AvatarId,
    string AvatarName,
    string FilePath,
    DateTime LastWriteTimeUtc);
