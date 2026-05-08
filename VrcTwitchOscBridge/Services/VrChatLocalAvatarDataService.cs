using System.Globalization;
using System.IO;
using System.Text.Json;
using VrcTwitchOscBridge.Models;

namespace VrcTwitchOscBridge.Services;

/// <summary>
/// Reads VRChat's LocalAvatarData cache for the current avatar's saved animation parameter values.
/// Crystal Relay uses this only as a Set Trigger restore source, never for avatar scale height.
/// </summary>
internal sealed class VrChatLocalAvatarDataService
{
    private static readonly string[] AvatarScaleAddresses =
    [
        "/avatar/eyeheight",
        "/avatar/eyeheightmin",
        "/avatar/eyeheightmax",
        "/avatar/eyeheightscalingallowed"
    ];

    private static readonly string[] OutfitParameterTerms =
    [
        "accessory",
        "bag",
        "belt",
        "boot",
        "bottom",
        "bra",
        "bracelet",
        "choker",
        "cloth",
        "coat",
        "dress",
        "glasses",
        "glove",
        "hair",
        "hat",
        "hoodie",
        "jacket",
        "mask",
        "necklace",
        "outfit",
        "pant",
        "shirt",
        "shoe",
        "short",
        "skirt",
        "sock",
        "sweater",
        "top",
        "underwear",
        "warmer"
    ];

    private static readonly string[] TransientParameterTokens =
    [
        "afk",
        "angle",
        "blink",
        "contact",
        "crouch",
        "emote",
        "fall",
        "finger",
        "fist",
        "gesture",
        "grab",
        "grabbed",
        "grounded",
        "idle",
        "jump",
        "locomotion",
        "mute",
        "pose",
        "posed",
        "prone",
        "radial",
        "seated",
        "speed",
        "tracking",
        "velocity",
        "viseme",
        "voice"
    ];

    public async Task<LocalAvatarDataParameterBatchReadResult> TryReadAvatarParameterValuesAsync(
        string avatarId,
        IReadOnlyList<LocalAvatarDataParameterRequest> requests,
        CancellationToken cancellationToken = default)
    {
        return await TryReadAvatarParameterValuesCoreAsync(
            avatarId,
            requests,
            includeOutfitSnapshotValues: false,
            cancellationToken);
    }

    public async Task<LocalAvatarDataParameterBatchReadResult> TryReadAvatarOutfitSnapshotValuesAsync(
        string avatarId,
        IReadOnlyList<LocalAvatarDataParameterRequest> requiredRequests,
        CancellationToken cancellationToken = default)
    {
        return await TryReadAvatarParameterValuesCoreAsync(
            avatarId,
            requiredRequests,
            includeOutfitSnapshotValues: true,
            cancellationToken);
    }

    public async Task<LocalAvatarDataParameterBatchReadResult> TryReadAvatarFullSnapshotValuesAsync(
        string avatarId,
        CancellationToken cancellationToken = default)
    {
        var normalizedAvatarId = avatarId?.Trim() ?? string.Empty;
        if (string.IsNullOrWhiteSpace(normalizedAvatarId))
        {
            return LocalAvatarDataParameterBatchReadResult.Unavailable("Crystal Relay does not know the current VRChat avatar yet.");
        }

        var avatarFile = FindLatestAvatarDataFile(normalizedAvatarId);
        if (avatarFile is null)
        {
            return LocalAvatarDataParameterBatchReadResult.Unavailable("VRChat has not written a LocalAvatarData file for the current avatar yet.");
        }

        var typeMetadata = await TryReadAvatarOscParameterTypesAsync(normalizedAvatarId, cancellationToken);
        if (!typeMetadata.Found)
        {
            return typeMetadata.FailureMode == LocalAvatarDataReadFailureMode.Unsafe
                ? LocalAvatarDataParameterBatchReadResult.Unsafe(typeMetadata.FailureReason)
                : LocalAvatarDataParameterBatchReadResult.Unavailable(typeMetadata.FailureReason);
        }

        try
        {
            var animationParameters = await ReadAnimationParametersAsync(avatarFile, cancellationToken);
            if (animationParameters.Count == 0)
            {
                return LocalAvatarDataParameterBatchReadResult.Unsafe("The current avatar's LocalAvatarData file has no animationParameters list.");
            }

            var observedValues = new Dictionary<string, OscObservedValue>(StringComparer.OrdinalIgnoreCase);
            var matchedNames = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
            var duplicateAddresses = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

            foreach (var entry in animationParameters)
            {
                cancellationToken.ThrowIfCancellationRequested();

                string address;
                try
                {
                    address = VrChatOscClient.NormalizeAvatarParameterAddress(entry.Name);
                }
                catch (InvalidOperationException)
                {
                    continue;
                }

                if (IsHeightOrScaleParameter(address)
                    || !typeMetadata.Types.TryGetValue(address, out var declaredType)
                    || declaredType is not (OscParameterType.Bool or OscParameterType.Int or OscParameterType.Float)
                    || !TryConvertValue(address, declaredType, entry.Value, out var observedValue))
                {
                    continue;
                }

                if (observedValues.ContainsKey(address))
                {
                    duplicateAddresses.Add(address);
                    continue;
                }

                observedValues[address] = observedValue;
                matchedNames[address] = entry.Name;
            }

            if (duplicateAddresses.Count > 0)
            {
                return LocalAvatarDataParameterBatchReadResult.Unsafe(
                    $"LocalAvatarData contains duplicate animation parameter entries for {string.Join(", ", duplicateAddresses.OrderBy(address => address, StringComparer.OrdinalIgnoreCase))}.");
            }

            if (observedValues.Count == 0)
            {
                return LocalAvatarDataParameterBatchReadResult.Unsafe("Crystal Relay could not match any LocalAvatarData animationParameters to typed avatar OSC parameters.");
            }

            return LocalAvatarDataParameterBatchReadResult.Success(
                observedValues,
                avatarFile.LastWriteTimeUtc,
                matchedNames,
                [],
                avatarFile.FullName);
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            throw;
        }
        catch
        {
            return LocalAvatarDataParameterBatchReadResult.Unsafe("Crystal Relay could not read the current avatar's LocalAvatarData file.");
        }
    }

    private async Task<LocalAvatarDataParameterBatchReadResult> TryReadAvatarParameterValuesCoreAsync(
        string avatarId,
        IReadOnlyList<LocalAvatarDataParameterRequest> requests,
        bool includeOutfitSnapshotValues,
        CancellationToken cancellationToken)
    {
        var normalizedAvatarId = avatarId?.Trim() ?? string.Empty;
        if (string.IsNullOrWhiteSpace(normalizedAvatarId))
        {
            return LocalAvatarDataParameterBatchReadResult.Unavailable("Crystal Relay does not know the current VRChat avatar yet.");
        }

        if (requests.Count == 0)
        {
            return LocalAvatarDataParameterBatchReadResult.Success(
                new Dictionary<string, OscObservedValue>(StringComparer.Ordinal),
                DateTime.MinValue,
                new Dictionary<string, string>(StringComparer.Ordinal),
                [],
                "No Set Trigger parameters needed LocalAvatarData.");
        }

        if (!TryPrepareRequests(requests, out var preparedRequests, out var prepareFailure))
        {
            return LocalAvatarDataParameterBatchReadResult.Unsafe(prepareFailure);
        }

        var avatarFile = FindLatestAvatarDataFile(normalizedAvatarId);
        if (avatarFile is null)
        {
            return LocalAvatarDataParameterBatchReadResult.Unavailable("VRChat has not written a LocalAvatarData file for the current avatar yet.");
        }

        var typeMetadata = await TryReadAvatarOscParameterTypesAsync(normalizedAvatarId, cancellationToken);
        if (typeMetadata.FailureMode == LocalAvatarDataReadFailureMode.Unsafe)
        {
            return LocalAvatarDataParameterBatchReadResult.Unsafe(typeMetadata.FailureReason);
        }

        try
        {
            var animationParameters = await ReadAnimationParametersAsync(avatarFile, cancellationToken);
            if (animationParameters.Count == 0)
            {
                return LocalAvatarDataParameterBatchReadResult.Unsafe("The current avatar's LocalAvatarData file has no animationParameters list.");
            }

            var observedValues = new Dictionary<string, OscObservedValue>(StringComparer.Ordinal);
            var matchedNames = new Dictionary<string, string>(StringComparer.Ordinal);
            var typeFallbackAddresses = new List<string>();

            foreach (var request in preparedRequests)
            {
                var match = FindAnimationParameterMatch(animationParameters, request.AnimationParameterKey);
                if (match.Status != LocalAvatarDataParameterMatchStatus.Matched || match.Entry is null)
                {
                    return LocalAvatarDataParameterBatchReadResult.Unsafe(match.FailureReason);
                }

                var declaredType = typeMetadata.Types.TryGetValue(request.Address, out var metadataType)
                    ? metadataType
                    : request.ExpectedType;
                if (!typeMetadata.Types.ContainsKey(request.Address))
                {
                    typeFallbackAddresses.Add(request.Address);
                }
                else if (declaredType != request.ExpectedType)
                {
                    return LocalAvatarDataParameterBatchReadResult.Unsafe(
                        $"The avatar OSC config says {request.Address} is {declaredType}, but the Set Trigger child is configured as {request.ExpectedType}.");
                }

                if (!TryConvertValue(request.Address, declaredType, match.Entry.Value, out var observedValue))
                {
                    return LocalAvatarDataParameterBatchReadResult.Unsafe(
                        $"The LocalAvatarData value for '{match.Entry.Name}' did not match the {declaredType} type declared for {request.Address}.");
                }

                observedValues[request.Address] = observedValue;
                matchedNames[request.Address] = match.Entry.Name;
            }

            if (includeOutfitSnapshotValues)
            {
                AddRelatedOutfitSnapshotValues(
                    animationParameters,
                    preparedRequests,
                    typeMetadata.Types,
                    observedValues,
                    matchedNames);
            }

            return LocalAvatarDataParameterBatchReadResult.Success(
                observedValues,
                avatarFile.LastWriteTimeUtc,
                matchedNames,
                typeFallbackAddresses,
                avatarFile.FullName);
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            throw;
        }
        catch
        {
            return LocalAvatarDataParameterBatchReadResult.Unsafe("Crystal Relay could not read the current avatar's LocalAvatarData file.");
        }
    }

    private static void AddRelatedOutfitSnapshotValues(
        IReadOnlyList<LocalAnimationParameterEntry> animationParameters,
        IReadOnlyList<PreparedLocalAvatarDataParameterRequest> requiredRequests,
        IReadOnlyDictionary<string, OscParameterType> declaredTypes,
        IDictionary<string, OscObservedValue> observedValues,
        IDictionary<string, string> matchedNames)
    {
        if (declaredTypes.Count == 0)
        {
            return;
        }

        var requiredDescriptors = requiredRequests
            .Select(request => TryCreateOutfitParameterDescriptor(request.AnimationParameterKey, out var descriptor)
                ? descriptor
                : null)
            .Where(descriptor => descriptor is not null)
            .Cast<OutfitParameterDescriptor>()
            .ToArray();
        if (requiredDescriptors.Length == 0)
        {
            return;
        }

        var requiredKeys = new HashSet<string>(
            requiredRequests.Select(request => request.AnimationParameterKey),
            StringComparer.OrdinalIgnoreCase);

        foreach (var entry in animationParameters)
        {
            if (!ShouldIncludeRelatedOutfitSnapshotParameter(entry.Name, requiredKeys, requiredDescriptors))
            {
                continue;
            }

            string address;
            try
            {
                address = VrChatOscClient.NormalizeAvatarParameterAddress(entry.Name);
            }
            catch (InvalidOperationException)
            {
                continue;
            }

            if (observedValues.ContainsKey(address)
                || !declaredTypes.TryGetValue(address, out var declaredType)
                || declaredType is not (OscParameterType.Bool or OscParameterType.Int or OscParameterType.Float)
                || !TryConvertValue(address, declaredType, entry.Value, out var observedValue))
            {
                continue;
            }

            observedValues[address] = observedValue;
            matchedNames[address] = entry.Name;
        }
    }

    private static bool ShouldIncludeRelatedOutfitSnapshotParameter(
        string parameterKey,
        IReadOnlySet<string> requiredKeys,
        IReadOnlyList<OutfitParameterDescriptor> requiredDescriptors)
    {
        if (requiredKeys.Contains(parameterKey))
        {
            return true;
        }

        if (!TryCreateOutfitParameterDescriptor(parameterKey, out var candidate))
        {
            return false;
        }

        return requiredDescriptors.Any(required =>
            (!string.IsNullOrWhiteSpace(required.Prefix)
                && string.Equals(required.Prefix, candidate.Prefix, StringComparison.Ordinal)
                && IsKnownOutfitTerm(candidate.Suffix))
            || (!string.IsNullOrWhiteSpace(required.Suffix)
                && string.Equals(required.Suffix, candidate.Suffix, StringComparison.OrdinalIgnoreCase)));
    }

    private static bool TryCreateOutfitParameterDescriptor(
        string parameterKey,
        out OutfitParameterDescriptor descriptor)
    {
        descriptor = default!;
        var normalized = parameterKey?.Trim() ?? string.Empty;
        if (string.IsNullOrWhiteSpace(normalized)
            || normalized.Contains('/', StringComparison.Ordinal)
            || IsHeightOrScaleParameter(normalized)
            || IsTransientAvatarParameter(normalized))
        {
            return false;
        }

        var prefix = string.Empty;
        var suffix = normalized;
        if (normalized.Length >= 2
            && char.IsUpper(normalized[0])
            && char.IsUpper(normalized[1]))
        {
            prefix = normalized[..1];
            suffix = normalized[1..];
        }

        if (!IsKnownOutfitTerm(suffix))
        {
            return false;
        }

        descriptor = new OutfitParameterDescriptor(prefix, suffix);
        return true;
    }

    private static bool IsKnownOutfitTerm(string parameterKey)
    {
        var lowered = (parameterKey ?? string.Empty).Trim().ToLowerInvariant();
        if (string.IsNullOrWhiteSpace(lowered))
        {
            return false;
        }

        return OutfitParameterTerms.Any(term => lowered.Contains(term, StringComparison.Ordinal));
    }

    private static bool IsTransientAvatarParameter(string parameterKey)
    {
        var lowered = (parameterKey ?? string.Empty).Trim().ToLowerInvariant();
        if (string.IsNullOrWhiteSpace(lowered))
        {
            return true;
        }

        if (lowered.Contains('/', StringComparison.Ordinal))
        {
            return true;
        }

        return TransientParameterTokens.Any(token => lowered.Contains(token, StringComparison.Ordinal));
    }

    public async Task<LocalAvatarDataParameterReadResult> TryReadAvatarParameterValueAsync(
        string avatarId,
        string parameterName,
        OscParameterType expectedType,
        CancellationToken cancellationToken = default)
    {
        var result = await TryReadAvatarParameterValuesAsync(
            avatarId,
            [new LocalAvatarDataParameterRequest(parameterName, expectedType)],
            cancellationToken);

        if (result.Found
            && result.Values.TryGetValue(VrChatOscClient.NormalizeAvatarParameterAddress(parameterName), out var observedValue))
        {
            return LocalAvatarDataParameterReadResult.Success(observedValue, result.LastWriteTimeUtc);
        }

        return LocalAvatarDataParameterReadResult.NotFound(result.FailureReason);
    }

    public static bool IsHeightOrScaleParameter(string? parameterName)
    {
        var normalized = (parameterName ?? string.Empty).Trim().Replace('\\', '/');
        if (string.IsNullOrWhiteSpace(normalized))
        {
            return false;
        }

        var lowered = normalized.ToLowerInvariant();
        if (AvatarScaleAddresses.Any(address => string.Equals(lowered, address, StringComparison.Ordinal)))
        {
            return true;
        }

        if (lowered.StartsWith("/avatar/eyeheight", StringComparison.Ordinal))
        {
            return true;
        }

        var parameterKey = GetAnimationParameterKey(lowered);
        var compact = new string(parameterKey
            .Where(character => !char.IsWhiteSpace(character) && character is not '_' and not '-' and not '.')
            .ToArray());

        return compact.Contains("eyeheight", StringComparison.Ordinal)
            || compact.Contains("height", StringComparison.Ordinal);
    }

    private static bool TryPrepareRequests(
        IReadOnlyList<LocalAvatarDataParameterRequest> requests,
        out IReadOnlyList<PreparedLocalAvatarDataParameterRequest> preparedRequests,
        out string failureReason)
    {
        var prepared = new List<PreparedLocalAvatarDataParameterRequest>(requests.Count);
        var keys = new HashSet<string>(StringComparer.Ordinal);
        var keysIgnoreCase = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var request in requests)
        {
            if (IsHeightOrScaleParameter(request.ParameterName))
            {
                preparedRequests = [];
                failureReason = "Height and avatar scale parameters are intentionally ignored.";
                return false;
            }

            string normalizedAddress;
            try
            {
                normalizedAddress = VrChatOscClient.NormalizeAvatarParameterAddress(request.ParameterName);
            }
            catch (InvalidOperationException)
            {
                preparedRequests = [];
                failureReason = "A Set Trigger parameter name was incomplete.";
                return false;
            }

            if (IsHeightOrScaleParameter(normalizedAddress))
            {
                preparedRequests = [];
                failureReason = "Height and avatar scale parameters are intentionally ignored.";
                return false;
            }

            var parameterKey = GetAnimationParameterKey(normalizedAddress);
            if (string.IsNullOrWhiteSpace(parameterKey))
            {
                preparedRequests = [];
                failureReason = "A Set Trigger parameter name was blank.";
                return false;
            }

            if (!keys.Add(parameterKey) || !keysIgnoreCase.Add(parameterKey))
            {
                preparedRequests = [];
                failureReason = $"The Set Trigger parameter '{parameterKey}' was requested more than once.";
                return false;
            }

            prepared.Add(new PreparedLocalAvatarDataParameterRequest(normalizedAddress, parameterKey, request.ExpectedType));
        }

        preparedRequests = prepared;
        failureReason = string.Empty;
        return true;
    }

    private static async Task<IReadOnlyList<LocalAnimationParameterEntry>> ReadAnimationParametersAsync(
        FileInfo avatarFile,
        CancellationToken cancellationToken)
    {
        await using var stream = new FileStream(
            avatarFile.FullName,
            FileMode.Open,
            FileAccess.Read,
            FileShare.ReadWrite | FileShare.Delete);
        using var document = await JsonDocument.ParseAsync(stream, cancellationToken: cancellationToken);
        if (!document.RootElement.TryGetProperty("animationParameters", out var animationParameters)
            || animationParameters.ValueKind != JsonValueKind.Array)
        {
            return [];
        }

        var entries = new List<LocalAnimationParameterEntry>();
        foreach (var entry in animationParameters.EnumerateArray())
        {
            cancellationToken.ThrowIfCancellationRequested();
            if (!entry.TryGetProperty("name", out var nameElement)
                || nameElement.ValueKind != JsonValueKind.String
                || !entry.TryGetProperty("value", out var valueElement))
            {
                continue;
            }

            var candidateName = nameElement.GetString()?.Trim() ?? string.Empty;
            if (string.IsNullOrWhiteSpace(candidateName) || IsHeightOrScaleParameter(candidateName))
            {
                continue;
            }

            entries.Add(new LocalAnimationParameterEntry(candidateName, valueElement.Clone()));
        }

        return entries;
    }

    private static LocalAvatarDataParameterMatch FindAnimationParameterMatch(
        IReadOnlyList<LocalAnimationParameterEntry> entries,
        string parameterKey)
    {
        var exactMatches = entries
            .Where(entry => string.Equals(entry.Name, parameterKey, StringComparison.Ordinal))
            .ToArray();
        if (exactMatches.Length == 1)
        {
            return LocalAvatarDataParameterMatch.Matched(exactMatches[0]);
        }

        if (exactMatches.Length > 1)
        {
            return LocalAvatarDataParameterMatch.Failed($"LocalAvatarData contains '{parameterKey}' more than once.");
        }

        var caseInsensitiveMatches = entries
            .Where(entry => string.Equals(entry.Name, parameterKey, StringComparison.OrdinalIgnoreCase))
            .ToArray();
        if (caseInsensitiveMatches.Length == 1)
        {
            return LocalAvatarDataParameterMatch.Matched(caseInsensitiveMatches[0]);
        }

        return caseInsensitiveMatches.Length > 1
            ? LocalAvatarDataParameterMatch.Failed($"LocalAvatarData contains multiple case-insensitive matches for '{parameterKey}'.")
            : LocalAvatarDataParameterMatch.Failed($"LocalAvatarData did not contain '{parameterKey}'.");
    }

    private static async Task<LocalAvatarDataTypeMetadataResult> TryReadAvatarOscParameterTypesAsync(
        string avatarId,
        CancellationToken cancellationToken)
    {
        var avatarFile = FindLatestAvatarOscConfigFile(avatarId);
        if (avatarFile is null)
        {
            return LocalAvatarDataTypeMetadataResult.Unavailable("VRChat has not written an OSC avatar config file for the current avatar yet.");
        }

        try
        {
            await using var stream = new FileStream(
                avatarFile.FullName,
                FileMode.Open,
                FileAccess.Read,
                FileShare.ReadWrite | FileShare.Delete);
            using var document = await JsonDocument.ParseAsync(stream, cancellationToken: cancellationToken);
            if (!document.RootElement.TryGetProperty("parameters", out var parameters)
                || parameters.ValueKind != JsonValueKind.Array)
            {
                return LocalAvatarDataTypeMetadataResult.Unavailable("The current avatar's OSC config file has no parameters list.");
            }

            var parameterTypes = new Dictionary<string, OscParameterType>(StringComparer.OrdinalIgnoreCase);
            foreach (var parameter in parameters.EnumerateArray())
            {
                cancellationToken.ThrowIfCancellationRequested();
                AddEndpointType(parameterTypes, parameter, "output");
                AddEndpointType(parameterTypes, parameter, "input");
            }

            return LocalAvatarDataTypeMetadataResult.Success(parameterTypes);
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            throw;
        }
        catch
        {
            return LocalAvatarDataTypeMetadataResult.Unsafe("Crystal Relay could not read the current avatar's OSC config file.");
        }
    }

    private static void AddEndpointType(
        IDictionary<string, OscParameterType> parameterTypes,
        JsonElement parameter,
        string endpointPropertyName)
    {
        if (!parameter.TryGetProperty(endpointPropertyName, out var endpoint)
            || endpoint.ValueKind != JsonValueKind.Object
            || !endpoint.TryGetProperty("address", out var addressElement)
            || addressElement.ValueKind != JsonValueKind.String
            || !endpoint.TryGetProperty("type", out var typeElement)
            || typeElement.ValueKind != JsonValueKind.String
            || !TryMapParameterType(typeElement.GetString(), out var parameterType))
        {
            return;
        }

        var address = addressElement.GetString()?.Trim() ?? string.Empty;
        if (string.IsNullOrWhiteSpace(address) || IsHeightOrScaleParameter(address))
        {
            return;
        }

        try
        {
            var normalizedAddress = VrChatOscClient.NormalizeAvatarParameterAddress(address);
            parameterTypes.TryAdd(normalizedAddress, parameterType);
        }
        catch (InvalidOperationException)
        {
        }
    }

    private static FileInfo? FindLatestAvatarDataFile(string avatarId)
    {
        var rootPath = VrChatLocalClientStateService.GetVrChatRootPath();
        if (string.IsNullOrWhiteSpace(rootPath))
        {
            return null;
        }

        var localAvatarDataPath = Path.Combine(rootPath, "LocalAvatarData");
        if (!Directory.Exists(localAvatarDataPath))
        {
            return null;
        }

        try
        {
            return Directory.EnumerateFiles(localAvatarDataPath, "avtr_*", SearchOption.AllDirectories)
                .Select(path => new FileInfo(path))
                .Where(file => file.Exists
                    && file.Directory?.Name.StartsWith("usr_", StringComparison.OrdinalIgnoreCase) == true
                    && (string.Equals(file.Name, avatarId, StringComparison.OrdinalIgnoreCase)
                        || string.Equals(Path.GetFileNameWithoutExtension(file.Name), avatarId, StringComparison.OrdinalIgnoreCase)))
                .OrderByDescending(file => file.LastWriteTimeUtc)
                .FirstOrDefault();
        }
        catch
        {
            return null;
        }
    }

    private static FileInfo? FindLatestAvatarOscConfigFile(string avatarId)
    {
        var rootPath = VrChatLocalClientStateService.GetVrChatRootPath();
        if (string.IsNullOrWhiteSpace(rootPath))
        {
            return null;
        }

        var oscPath = Path.Combine(rootPath, "OSC");
        if (!Directory.Exists(oscPath))
        {
            return null;
        }

        try
        {
            return Directory.EnumerateFiles(oscPath, $"{avatarId}.json", SearchOption.AllDirectories)
                .Select(path => new FileInfo(path))
                .Where(file => file.Exists
                    && string.Equals(file.Directory?.Name, "Avatars", StringComparison.OrdinalIgnoreCase)
                    && file.Directory?.Parent?.Name.StartsWith("usr_", StringComparison.OrdinalIgnoreCase) == true)
                .OrderByDescending(file => file.LastWriteTimeUtc)
                .FirstOrDefault();
        }
        catch
        {
            return null;
        }
    }

    private static string GetAnimationParameterKey(string? parameterName)
    {
        var normalized = (parameterName ?? string.Empty).Trim().Replace('\\', '/');
        const string avatarParameterPrefix = "/avatar/parameters/";
        return normalized.StartsWith(avatarParameterPrefix, StringComparison.OrdinalIgnoreCase)
            ? normalized[avatarParameterPrefix.Length..].Trim()
            : normalized.Split('/', StringSplitOptions.RemoveEmptyEntries).LastOrDefault()?.Trim() ?? normalized;
    }

    private static bool TryConvertValue(
        string parameterName,
        OscParameterType expectedType,
        JsonElement valueElement,
        out OscObservedValue observedValue)
    {
        var normalizedAddress = VrChatOscClient.NormalizeAvatarParameterAddress(parameterName);
        observedValue = default!;
        switch (expectedType)
        {
            case OscParameterType.Bool:
                if (TryReadBool(valueElement, out var boolValue))
                {
                    observedValue = new OscObservedValue(normalizedAddress, OscParameterType.Bool, boolValue);
                    return true;
                }

                return false;
            case OscParameterType.Int:
                if (TryReadInt(valueElement, out var intValue))
                {
                    observedValue = new OscObservedValue(normalizedAddress, OscParameterType.Int, intValue);
                    return true;
                }

                return false;
            case OscParameterType.Float:
                if (TryReadFloat(valueElement, out var floatValue))
                {
                    observedValue = new OscObservedValue(normalizedAddress, OscParameterType.Float, floatValue);
                    return true;
                }

                return false;
            default:
                return false;
        }
    }

    private static bool TryReadBool(JsonElement valueElement, out bool value)
    {
        value = false;
        switch (valueElement.ValueKind)
        {
            case JsonValueKind.True:
                value = true;
                return true;
            case JsonValueKind.False:
                return true;
            case JsonValueKind.Number:
                if (valueElement.TryGetInt32(out var intValue) && intValue is 0 or 1)
                {
                    value = intValue == 1;
                    return true;
                }

                return false;
            case JsonValueKind.String:
                var text = valueElement.GetString()?.Trim() ?? string.Empty;
                if (bool.TryParse(text, out var boolValue))
                {
                    value = boolValue;
                    return true;
                }

                if (int.TryParse(text, NumberStyles.Integer, CultureInfo.InvariantCulture, out intValue)
                    && intValue is 0 or 1)
                {
                    value = intValue == 1;
                    return true;
                }

                return false;
            default:
                return false;
        }
    }

    private static bool TryReadInt(JsonElement valueElement, out int value)
    {
        value = 0;
        switch (valueElement.ValueKind)
        {
            case JsonValueKind.Number:
                if (valueElement.TryGetInt32(out value))
                {
                    return true;
                }

                if (valueElement.TryGetDouble(out var doubleValue)
                    && double.IsFinite(doubleValue)
                    && Math.Abs(doubleValue % 1d) < double.Epsilon
                    && doubleValue >= int.MinValue
                    && doubleValue <= int.MaxValue)
                {
                    value = (int)doubleValue;
                    return true;
                }

                return false;
            case JsonValueKind.String:
                return int.TryParse(valueElement.GetString(), NumberStyles.Integer, CultureInfo.InvariantCulture, out value);
            default:
                return false;
        }
    }

    private static bool TryReadFloat(JsonElement valueElement, out float value)
    {
        value = 0f;
        switch (valueElement.ValueKind)
        {
            case JsonValueKind.Number:
                if (valueElement.TryGetSingle(out value) && float.IsFinite(value))
                {
                    return true;
                }

                if (valueElement.TryGetDouble(out var doubleValue)
                    && double.IsFinite(doubleValue)
                    && doubleValue >= -float.MaxValue
                    && doubleValue <= float.MaxValue)
                {
                    value = (float)doubleValue;
                    return true;
                }

                return false;
            case JsonValueKind.String:
                return float.TryParse(
                        valueElement.GetString(),
                        NumberStyles.Float | NumberStyles.AllowThousands,
                        CultureInfo.InvariantCulture,
                        out value)
                    && float.IsFinite(value);
            default:
                return false;
        }
    }

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
}

internal enum LocalAvatarDataReadFailureMode
{
    None,
    Unavailable,
    Unsafe
}

internal enum LocalAvatarDataParameterMatchStatus
{
    Matched,
    Failed
}

internal sealed record LocalAvatarDataParameterRequest(string ParameterName, OscParameterType ExpectedType);

internal sealed record LocalAvatarDataParameterBatchReadResult(
    bool Found,
    IReadOnlyDictionary<string, OscObservedValue> Values,
    DateTime LastWriteTimeUtc,
    string FailureReason,
    LocalAvatarDataReadFailureMode FailureMode,
    IReadOnlyDictionary<string, string> MatchedParameterNames,
    IReadOnlyList<string> TypeFallbackAddresses,
    string SourcePath)
{
    public bool CanFallback => !Found && FailureMode == LocalAvatarDataReadFailureMode.Unavailable;

    public static LocalAvatarDataParameterBatchReadResult Success(
        IReadOnlyDictionary<string, OscObservedValue> values,
        DateTime lastWriteTimeUtc,
        IReadOnlyDictionary<string, string> matchedParameterNames,
        IReadOnlyList<string> typeFallbackAddresses,
        string sourcePath) =>
        new(true, values, lastWriteTimeUtc, string.Empty, LocalAvatarDataReadFailureMode.None, matchedParameterNames, typeFallbackAddresses, sourcePath);

    public static LocalAvatarDataParameterBatchReadResult Unavailable(string failureReason) =>
        new(false, new Dictionary<string, OscObservedValue>(StringComparer.Ordinal), DateTime.MinValue, failureReason, LocalAvatarDataReadFailureMode.Unavailable, new Dictionary<string, string>(StringComparer.Ordinal), [], string.Empty);

    public static LocalAvatarDataParameterBatchReadResult Unsafe(string failureReason) =>
        new(false, new Dictionary<string, OscObservedValue>(StringComparer.Ordinal), DateTime.MinValue, failureReason, LocalAvatarDataReadFailureMode.Unsafe, new Dictionary<string, string>(StringComparer.Ordinal), [], string.Empty);
}

internal sealed record LocalAvatarDataParameterReadResult(
    bool Found,
    OscObservedValue? Value,
    DateTime LastWriteTimeUtc,
    string FailureReason)
{
    public static LocalAvatarDataParameterReadResult Success(OscObservedValue value, DateTime lastWriteTimeUtc) =>
        new(true, value, lastWriteTimeUtc, string.Empty);

    public static LocalAvatarDataParameterReadResult NotFound(string failureReason) =>
        new(false, null, DateTime.MinValue, failureReason);
}

internal sealed record PreparedLocalAvatarDataParameterRequest(
    string Address,
    string AnimationParameterKey,
    OscParameterType ExpectedType);

internal sealed record OutfitParameterDescriptor(string Prefix, string Suffix);

internal sealed record LocalAnimationParameterEntry(string Name, JsonElement Value);

internal sealed record LocalAvatarDataParameterMatch(
    LocalAvatarDataParameterMatchStatus Status,
    LocalAnimationParameterEntry? Entry,
    string FailureReason)
{
    public static LocalAvatarDataParameterMatch Matched(LocalAnimationParameterEntry entry) =>
        new(LocalAvatarDataParameterMatchStatus.Matched, entry, string.Empty);

    public static LocalAvatarDataParameterMatch Failed(string failureReason) =>
        new(LocalAvatarDataParameterMatchStatus.Failed, null, failureReason);
}

internal sealed record LocalAvatarDataTypeMetadataResult(
    bool Found,
    IReadOnlyDictionary<string, OscParameterType> Types,
    LocalAvatarDataReadFailureMode FailureMode,
    string FailureReason)
{
    public static LocalAvatarDataTypeMetadataResult Success(IReadOnlyDictionary<string, OscParameterType> types) =>
        new(true, types, LocalAvatarDataReadFailureMode.None, string.Empty);

    public static LocalAvatarDataTypeMetadataResult Unavailable(string failureReason) =>
        new(false, new Dictionary<string, OscParameterType>(StringComparer.OrdinalIgnoreCase), LocalAvatarDataReadFailureMode.Unavailable, failureReason);

    public static LocalAvatarDataTypeMetadataResult Unsafe(string failureReason) =>
        new(false, new Dictionary<string, OscParameterType>(StringComparer.OrdinalIgnoreCase), LocalAvatarDataReadFailureMode.Unsafe, failureReason);
}
