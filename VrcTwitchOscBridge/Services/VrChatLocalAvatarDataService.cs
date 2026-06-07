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
            var ambiguousNames = new List<string>();

            foreach (var entry in animationParameters)
            {
                cancellationToken.ThrowIfCancellationRequested();

                var metadataMatch = ResolveOscMetadataForLocalName(typeMetadata, entry.Name);
                if (metadataMatch.Status == LocalAvatarDataParameterMatchStatus.Failed || metadataMatch.Metadata is null)
                {
                    if (!string.IsNullOrWhiteSpace(metadataMatch.FailureReason)
                        && metadataMatch.FailureReason.Contains("multiple", StringComparison.OrdinalIgnoreCase))
                    {
                        ambiguousNames.Add(entry.Name);
                    }

                    continue;
                }

                var metadata = metadataMatch.Metadata;
                if (!IsSupportedLocalAvatarDataType(metadata.ParameterType)
                    || !TryConvertValue(metadata.Address, metadata.ParameterType, entry.Value, out var observedValue))
                {
                    continue;
                }

                if (observedValues.ContainsKey(metadata.Address))
                {
                    duplicateAddresses.Add(metadata.Address);
                    continue;
                }

                observedValues[metadata.Address] = observedValue;
                matchedNames[metadata.Address] = entry.Name;
            }

            if (duplicateAddresses.Count > 0)
            {
                return LocalAvatarDataParameterBatchReadResult.Unsafe(
                    $"LocalAvatarData contains duplicate animation parameter entries for {string.Join(", ", duplicateAddresses.OrderBy(address => address, StringComparer.OrdinalIgnoreCase))}.");
            }

            if (ambiguousNames.Count > 0 && observedValues.Count == 0)
            {
                return LocalAvatarDataParameterBatchReadResult.Unsafe(
                    $"LocalAvatarData parameter names matched multiple OSC paths and were skipped: {string.Join(", ", ambiguousNames.Distinct(StringComparer.OrdinalIgnoreCase).OrderBy(name => name, StringComparer.OrdinalIgnoreCase))}.");
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

            var observedValues = new Dictionary<string, OscObservedValue>(StringComparer.Ordinal);
            var matchedNames = new Dictionary<string, string>(StringComparer.Ordinal);
            var resolvedRequestMetadata = new List<LocalAvatarOscParameterMetadata>();

            foreach (var request in preparedRequests)
            {
                var metadataMatch = ResolveOscMetadataForRequest(typeMetadata, request);
                if (metadataMatch.Status != LocalAvatarDataParameterMatchStatus.Matched || metadataMatch.Metadata is null)
                {
                    return LocalAvatarDataParameterBatchReadResult.Unsafe(metadataMatch.FailureReason);
                }

                var metadata = metadataMatch.Metadata;
                if (metadata.ParameterType != request.ExpectedType)
                {
                    return LocalAvatarDataParameterBatchReadResult.Unsafe(
                        $"The avatar OSC config says {metadata.Address} is {metadata.ParameterType}, but the Set Trigger child is configured as {request.ExpectedType}.");
                }

                var match = FindAnimationParameterMatch(animationParameters, metadata);
                if (match.Status != LocalAvatarDataParameterMatchStatus.Matched || match.Entry is null)
                {
                    return LocalAvatarDataParameterBatchReadResult.Unsafe(match.FailureReason);
                }

                if (!TryConvertValue(metadata.Address, metadata.ParameterType, match.Entry.Value, out var observedValue))
                {
                    return LocalAvatarDataParameterBatchReadResult.Unsafe(
                        $"The LocalAvatarData value for '{match.Entry.Name}' did not match the {metadata.ParameterType} type declared for {metadata.Address}.");
                }

                observedValues[metadata.Address] = observedValue;
                matchedNames[metadata.Address] = match.Entry.Name;
                resolvedRequestMetadata.Add(metadata);
            }

            if (includeOutfitSnapshotValues)
            {
                AddRelatedOutfitSnapshotValues(
                    animationParameters,
                    resolvedRequestMetadata,
                    typeMetadata,
                    observedValues,
                    matchedNames);
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

    private static void AddRelatedOutfitSnapshotValues(
        IReadOnlyList<LocalAnimationParameterEntry> animationParameters,
        IReadOnlyList<LocalAvatarOscParameterMetadata> requiredMetadata,
        LocalAvatarDataTypeMetadataResult typeMetadata,
        IDictionary<string, OscObservedValue> observedValues,
        IDictionary<string, string> matchedNames)
    {
        if (requiredMetadata.Count == 0)
        {
            return;
        }

        var requiredDescriptors = requiredMetadata
            .SelectMany(CreateOutfitDescriptorCandidates)
            .Where(descriptor => descriptor is not null)
            .Cast<OutfitParameterDescriptor>()
            .ToArray();
        if (requiredDescriptors.Length == 0)
        {
            return;
        }

        var requiredKeys = new HashSet<string>(
            requiredMetadata.SelectMany(CreateParameterNameCandidates),
            StringComparer.OrdinalIgnoreCase);

        foreach (var entry in animationParameters)
        {
            if (!ShouldIncludeRelatedOutfitSnapshotParameter(entry.Name, requiredKeys, requiredDescriptors))
            {
                continue;
            }

            var metadataMatch = ResolveOscMetadataForLocalName(typeMetadata, entry.Name);
            if (metadataMatch.Status != LocalAvatarDataParameterMatchStatus.Matched || metadataMatch.Metadata is null)
            {
                continue;
            }

            var metadata = metadataMatch.Metadata;
            if (observedValues.ContainsKey(metadata.Address)
                || !IsSupportedLocalAvatarDataType(metadata.ParameterType)
                || !TryConvertValue(metadata.Address, metadata.ParameterType, entry.Value, out var observedValue))
            {
                continue;
            }

            observedValues[metadata.Address] = observedValue;
            matchedNames[metadata.Address] = entry.Name;
        }
    }

    private static IEnumerable<OutfitParameterDescriptor?> CreateOutfitDescriptorCandidates(LocalAvatarOscParameterMetadata metadata)
    {
        foreach (var candidate in CreateParameterNameCandidates(metadata))
        {
            if (TryCreateOutfitParameterDescriptor(candidate, out var descriptor))
            {
                yield return descriptor;
            }
        }
    }

    private static IEnumerable<string> CreateParameterNameCandidates(LocalAvatarOscParameterMetadata metadata)
    {
        if (!string.IsNullOrWhiteSpace(metadata.ParameterName))
        {
            yield return metadata.ParameterName;
        }

        if (!string.IsNullOrWhiteSpace(metadata.FinalPathSegment))
        {
            yield return metadata.FinalPathSegment;
        }

        if (!string.IsNullOrWhiteSpace(metadata.AnimationParameterKey))
        {
            yield return metadata.AnimationParameterKey;
        }

        yield return metadata.Address;
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
        return FindAnimationParameterMatch(entries, [parameterKey], parameterKey);
    }

    private static LocalAvatarDataParameterMatch FindAnimationParameterMatch(
        IReadOnlyList<LocalAnimationParameterEntry> entries,
        LocalAvatarOscParameterMetadata metadata)
    {
        var candidates = CreateParameterNameCandidates(metadata)
            .Where(candidate => !string.IsNullOrWhiteSpace(candidate))
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToArray();

        return FindAnimationParameterMatch(entries, candidates, metadata.Address);
    }

    private static LocalAvatarDataParameterMatch FindAnimationParameterMatch(
        IReadOnlyList<LocalAnimationParameterEntry> entries,
        IReadOnlyList<string> parameterKeys,
        string displayName)
    {
        var exactMatches = entries
            .Where(entry => parameterKeys.Any(parameterKey => string.Equals(entry.Name, parameterKey, StringComparison.Ordinal)))
            .ToArray();
        var exactMatch = SelectUniqueEntry(exactMatches);
        if (exactMatch.Status == LocalAvatarDataParameterMatchStatus.Matched)
        {
            return exactMatch;
        }

        if (exactMatches.Length > 1)
        {
            return LocalAvatarDataParameterMatch.Failed($"LocalAvatarData contains '{displayName}' more than once.");
        }

        var caseInsensitiveMatches = entries
            .Where(entry => parameterKeys.Any(parameterKey => string.Equals(entry.Name, parameterKey, StringComparison.OrdinalIgnoreCase)))
            .ToArray();
        var caseInsensitiveMatch = SelectUniqueEntry(caseInsensitiveMatches);
        if (caseInsensitiveMatch.Status == LocalAvatarDataParameterMatchStatus.Matched)
        {
            return caseInsensitiveMatch;
        }

        if (caseInsensitiveMatches.Length > 1)
        {
            return LocalAvatarDataParameterMatch.Failed($"LocalAvatarData contains multiple case-insensitive matches for '{displayName}'.");
        }

        var normalizedKeys = parameterKeys
            .Select(NormalizeParameterLookupKey)
            .Where(key => !string.IsNullOrWhiteSpace(key))
            .Distinct(StringComparer.Ordinal)
            .ToArray();
        var normalizedMatches = entries
            .Where(entry => normalizedKeys.Contains(NormalizeParameterLookupKey(entry.Name), StringComparer.Ordinal))
            .ToArray();
        var normalizedMatch = SelectUniqueEntry(normalizedMatches);
        if (normalizedMatch.Status == LocalAvatarDataParameterMatchStatus.Matched)
        {
            return normalizedMatch;
        }

        return normalizedMatches.Length > 1
            ? LocalAvatarDataParameterMatch.Failed($"LocalAvatarData contains multiple normalized matches for '{displayName}'.")
            : LocalAvatarDataParameterMatch.Failed($"LocalAvatarData did not contain '{displayName}'.");
    }

    private static LocalAvatarDataParameterMatch SelectUniqueEntry(IReadOnlyList<LocalAnimationParameterEntry> matches)
    {
        if (matches.Count == 0)
        {
            return LocalAvatarDataParameterMatch.Failed(string.Empty);
        }

        var uniqueEntries = matches
            .GroupBy(entry => entry.Name, StringComparer.Ordinal)
            .Select(group => group.First())
            .ToArray();

        return uniqueEntries.Length == 1
            ? LocalAvatarDataParameterMatch.Matched(uniqueEntries[0])
            : LocalAvatarDataParameterMatch.Failed(string.Empty);
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

            var parameterMetadata = new Dictionary<string, LocalAvatarOscParameterMetadata>(StringComparer.OrdinalIgnoreCase);
            foreach (var parameter in parameters.EnumerateArray())
            {
                cancellationToken.ThrowIfCancellationRequested();
                if (!AddEndpointMetadata(parameterMetadata, parameter, "output", out var failureReason)
                    || !AddEndpointMetadata(parameterMetadata, parameter, "input", out failureReason))
                {
                    return LocalAvatarDataTypeMetadataResult.Unsafe(failureReason);
                }
            }

            return LocalAvatarDataTypeMetadataResult.Success(parameterMetadata);
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

    private static bool AddEndpointMetadata(
        IDictionary<string, LocalAvatarOscParameterMetadata> parameterMetadata,
        JsonElement parameter,
        string endpointPropertyName,
        out string failureReason)
    {
        failureReason = string.Empty;
        if (!parameter.TryGetProperty(endpointPropertyName, out var endpoint)
            || endpoint.ValueKind != JsonValueKind.Object
            || !endpoint.TryGetProperty("address", out var addressElement)
            || addressElement.ValueKind != JsonValueKind.String
            || !endpoint.TryGetProperty("type", out var typeElement)
            || typeElement.ValueKind != JsonValueKind.String
            || !TryMapParameterType(typeElement.GetString(), out var parameterType))
        {
            return true;
        }

        var address = addressElement.GetString()?.Trim() ?? string.Empty;
        if (string.IsNullOrWhiteSpace(address) || IsHeightOrScaleParameter(address))
        {
            return true;
        }

        try
        {
            var normalizedAddress = VrChatOscClient.NormalizeAvatarParameterAddress(address);
            if (parameterMetadata.TryGetValue(normalizedAddress, out var existing))
            {
                if (existing.ParameterType != parameterType)
                {
                    failureReason = $"The avatar OSC config declares conflicting types for {normalizedAddress}.";
                    return false;
                }

                return true;
            }

            var parameterName = string.Empty;
            if (parameter.TryGetProperty("name", out var nameElement)
                && nameElement.ValueKind == JsonValueKind.String)
            {
                parameterName = nameElement.GetString()?.Trim() ?? string.Empty;
            }

            if (string.IsNullOrWhiteSpace(parameterName))
            {
                parameterName = GetAnimationParameterKey(normalizedAddress);
            }

            var animationParameterKey = GetAnimationParameterKey(normalizedAddress);
            var finalPathSegment = GetFinalPathSegment(animationParameterKey);
            parameterMetadata[normalizedAddress] = new LocalAvatarOscParameterMetadata(
                normalizedAddress,
                parameterName,
                animationParameterKey,
                finalPathSegment,
                parameterType);
        }
        catch (InvalidOperationException)
        {
        }

        return true;
    }

    private static LocalAvatarOscMetadataMatch ResolveOscMetadataForRequest(
        LocalAvatarDataTypeMetadataResult typeMetadata,
        PreparedLocalAvatarDataParameterRequest request)
    {
        if (typeMetadata.ParametersByAddress.TryGetValue(request.Address, out var exactMetadata))
        {
            return LocalAvatarOscMetadataMatch.Matched(exactMetadata);
        }

        var match = ResolveOscMetadataForLocalName(typeMetadata, request.AnimationParameterKey);
        return match.Status == LocalAvatarDataParameterMatchStatus.Matched
            ? match
            : LocalAvatarOscMetadataMatch.Failed(
                $"The avatar OSC config did not contain a safe typed match for {request.Address}. {match.FailureReason}");
    }

    private static LocalAvatarOscMetadataMatch ResolveOscMetadataForLocalName(
        LocalAvatarDataTypeMetadataResult typeMetadata,
        string localName)
    {
        var candidate = localName?.Trim() ?? string.Empty;
        if (string.IsNullOrWhiteSpace(candidate))
        {
            return LocalAvatarOscMetadataMatch.Failed("LocalAvatarData contained a blank animation parameter name.");
        }

        if (TryNormalizeOptionalAvatarParameterAddress(candidate, out var normalizedAddress)
            && typeMetadata.ParametersByAddress.TryGetValue(normalizedAddress, out var exactMetadata))
        {
            return LocalAvatarOscMetadataMatch.Matched(exactMetadata);
        }

        var exactNameMatch = SelectUniqueMetadata(
            typeMetadata.Parameters.Where(parameter => string.Equals(parameter.ParameterName, candidate, StringComparison.Ordinal)),
            candidate,
            "OSC parameter name");
        if (exactNameMatch.Status == LocalAvatarDataParameterMatchStatus.Matched)
        {
            return exactNameMatch;
        }
        if (!string.IsNullOrWhiteSpace(exactNameMatch.FailureReason))
        {
            return exactNameMatch;
        }

        var exactFinalSegmentMatch = SelectUniqueMetadata(
            typeMetadata.Parameters.Where(parameter => string.Equals(parameter.FinalPathSegment, candidate, StringComparison.Ordinal)),
            candidate,
            "OSC path suffix");
        if (exactFinalSegmentMatch.Status == LocalAvatarDataParameterMatchStatus.Matched)
        {
            return exactFinalSegmentMatch;
        }
        if (!string.IsNullOrWhiteSpace(exactFinalSegmentMatch.FailureReason))
        {
            return exactFinalSegmentMatch;
        }

        var exactKeyMatch = SelectUniqueMetadata(
            typeMetadata.Parameters.Where(parameter => string.Equals(parameter.AnimationParameterKey, candidate, StringComparison.Ordinal)),
            candidate,
            "OSC parameter path");
        if (exactKeyMatch.Status == LocalAvatarDataParameterMatchStatus.Matched)
        {
            return exactKeyMatch;
        }
        if (!string.IsNullOrWhiteSpace(exactKeyMatch.FailureReason))
        {
            return exactKeyMatch;
        }

        var ignoreCaseMatch = SelectUniqueMetadata(
            typeMetadata.Parameters.Where(parameter =>
                string.Equals(parameter.ParameterName, candidate, StringComparison.OrdinalIgnoreCase)
                || string.Equals(parameter.FinalPathSegment, candidate, StringComparison.OrdinalIgnoreCase)
                || string.Equals(parameter.AnimationParameterKey, candidate, StringComparison.OrdinalIgnoreCase)),
            candidate,
            "case-insensitive OSC metadata");
        if (ignoreCaseMatch.Status == LocalAvatarDataParameterMatchStatus.Matched)
        {
            return ignoreCaseMatch;
        }
        if (!string.IsNullOrWhiteSpace(ignoreCaseMatch.FailureReason))
        {
            return ignoreCaseMatch;
        }

        var lookupKey = NormalizeParameterLookupKey(candidate);
        if (!string.IsNullOrWhiteSpace(lookupKey))
        {
            var normalizedMatch = SelectUniqueMetadata(
                typeMetadata.Parameters.Where(parameter =>
                    string.Equals(NormalizeParameterLookupKey(parameter.ParameterName), lookupKey, StringComparison.Ordinal)
                    || string.Equals(NormalizeParameterLookupKey(parameter.FinalPathSegment), lookupKey, StringComparison.Ordinal)
                    || string.Equals(NormalizeParameterLookupKey(parameter.AnimationParameterKey), lookupKey, StringComparison.Ordinal)),
                candidate,
                "normalized OSC metadata");
            if (normalizedMatch.Status == LocalAvatarDataParameterMatchStatus.Matched)
            {
                return normalizedMatch;
            }

            if (!string.IsNullOrWhiteSpace(normalizedMatch.FailureReason))
            {
                return normalizedMatch;
            }
        }

        return LocalAvatarOscMetadataMatch.Failed($"LocalAvatarData parameter '{candidate}' did not match any typed OSC parameter name or path suffix.");
    }

    private static LocalAvatarOscMetadataMatch SelectUniqueMetadata(
        IEnumerable<LocalAvatarOscParameterMetadata> matches,
        string candidate,
        string matchKind)
    {
        var uniqueMatches = matches
            .GroupBy(parameter => parameter.Address, StringComparer.OrdinalIgnoreCase)
            .Select(group => group.First())
            .ToArray();

        return uniqueMatches.Length switch
        {
            0 => LocalAvatarOscMetadataMatch.Failed(string.Empty),
            1 => LocalAvatarOscMetadataMatch.Matched(uniqueMatches[0]),
            _ => LocalAvatarOscMetadataMatch.Failed(
                $"LocalAvatarData parameter '{candidate}' matched multiple {matchKind} entries: {string.Join(", ", uniqueMatches.Select(match => match.Address).OrderBy(address => address, StringComparer.OrdinalIgnoreCase))}.")
        };
    }

    private static bool TryNormalizeOptionalAvatarParameterAddress(string parameterName, out string normalizedAddress)
    {
        normalizedAddress = string.Empty;
        try
        {
            normalizedAddress = VrChatOscClient.NormalizeAvatarParameterAddress(parameterName);
            return true;
        }
        catch (InvalidOperationException)
        {
            return false;
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

    private static string GetFinalPathSegment(string? parameterName)
    {
        var normalized = (parameterName ?? string.Empty).Trim().Replace('\\', '/');
        return normalized.Split('/', StringSplitOptions.RemoveEmptyEntries).LastOrDefault()?.Trim() ?? normalized;
    }

    private static string NormalizeParameterLookupKey(string? parameterName)
    {
        var normalized = GetAnimationParameterKey(parameterName).Trim();
        if (string.IsNullOrWhiteSpace(normalized))
        {
            return string.Empty;
        }

        return new string(normalized
            .Where(character => char.IsLetterOrDigit(character))
            .Select(char.ToLowerInvariant)
            .ToArray());
    }

    private static bool IsSupportedLocalAvatarDataType(OscParameterType parameterType) =>
        parameterType is OscParameterType.Bool or OscParameterType.Int or OscParameterType.Float;

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

internal sealed record LocalAvatarOscParameterMetadata(
    string Address,
    string ParameterName,
    string AnimationParameterKey,
    string FinalPathSegment,
    OscParameterType ParameterType);

internal sealed record LocalAvatarOscMetadataMatch(
    LocalAvatarDataParameterMatchStatus Status,
    LocalAvatarOscParameterMetadata? Metadata,
    string FailureReason)
{
    public static LocalAvatarOscMetadataMatch Matched(LocalAvatarOscParameterMetadata metadata) =>
        new(LocalAvatarDataParameterMatchStatus.Matched, metadata, string.Empty);

    public static LocalAvatarOscMetadataMatch Failed(string failureReason) =>
        new(LocalAvatarDataParameterMatchStatus.Failed, null, failureReason);
}

internal sealed record LocalAvatarDataTypeMetadataResult(
    bool Found,
    IReadOnlyDictionary<string, OscParameterType> Types,
    IReadOnlyDictionary<string, LocalAvatarOscParameterMetadata> ParametersByAddress,
    IReadOnlyList<LocalAvatarOscParameterMetadata> Parameters,
    LocalAvatarDataReadFailureMode FailureMode,
    string FailureReason)
{
    public static LocalAvatarDataTypeMetadataResult Success(IReadOnlyDictionary<string, LocalAvatarOscParameterMetadata> parametersByAddress) =>
        new(
            true,
            parametersByAddress.ToDictionary(
                pair => pair.Key,
                pair => pair.Value.ParameterType,
                StringComparer.OrdinalIgnoreCase),
            parametersByAddress,
            parametersByAddress.Values
                .OrderBy(parameter => parameter.Address, StringComparer.OrdinalIgnoreCase)
                .ToArray(),
            LocalAvatarDataReadFailureMode.None,
            string.Empty);

    public static LocalAvatarDataTypeMetadataResult Unavailable(string failureReason) =>
        new(
            false,
            new Dictionary<string, OscParameterType>(StringComparer.OrdinalIgnoreCase),
            new Dictionary<string, LocalAvatarOscParameterMetadata>(StringComparer.OrdinalIgnoreCase),
            [],
            LocalAvatarDataReadFailureMode.Unavailable,
            failureReason);

    public static LocalAvatarDataTypeMetadataResult Unsafe(string failureReason) =>
        new(
            false,
            new Dictionary<string, OscParameterType>(StringComparer.OrdinalIgnoreCase),
            new Dictionary<string, LocalAvatarOscParameterMetadata>(StringComparer.OrdinalIgnoreCase),
            [],
            LocalAvatarDataReadFailureMode.Unsafe,
            failureReason);
}
