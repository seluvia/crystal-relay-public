using System.Diagnostics;
using System.Globalization;
using VrcTwitchOscBridge.Models;

namespace VrcTwitchOscBridge.Services;

/// <summary>
/// Executes Wardrobe outfit snapshots: capture current values, apply outfit, restore on timeout.
/// </summary>
internal sealed class WardrobeExecutorService : IAsyncDisposable
{
    private static readonly TimeSpan WardrobeDiffObservationDelay = TimeSpan.FromSeconds(70);

    private readonly VrChatOscClient oscClient;
    private readonly OscRouterService oscRouterService;
    private readonly VrChatLocalOscCacheService localOscCacheService;
    private readonly VrChatLocalAvatarDataService localAvatarDataService;
    private readonly Action<string> logWritten;
    private readonly object stateGate = new();
    private readonly Dictionary<WardrobeRestoreKey, WardrobeRestoreSession> activeRestores = new();
    private DateTimeOffset? wardrobeCooldownUntil;

    public WardrobeExecutorService(
        VrChatOscClient oscClient,
        OscRouterService oscRouterService,
        VrChatLocalOscCacheService localOscCacheService,
        VrChatLocalAvatarDataService localAvatarDataService,
        Action<string> logWritten)
    {
        this.oscClient = oscClient;
        this.oscRouterService = oscRouterService;
        this.localOscCacheService = localOscCacheService;
        this.localAvatarDataService = localAvatarDataService;
        this.logWritten = logWritten;
    }

    /// <summary>
    /// Checks if the Wardrobe is currently on cooldown.
    /// </summary>
    public bool IsOnCooldown()
    {
        lock (stateGate)
        {
            return wardrobeCooldownUntil.HasValue && wardrobeCooldownUntil.Value > DateTimeOffset.UtcNow;
        }
    }

    /// <summary>
    /// Executes a Wardrobe outfit: validates params, captures current values, applies snapshot, schedules restore.
    /// Returns true if the outfit was applied, false if blocked.
    /// </summary>
    public async Task<bool> ExecuteOutfitAsync(
        WardrobeOutfitSnapshot snapshot,
        string vrChatUserId,
        CancellationToken cancellationToken = default)
    {
        // Check global cooldown
        if (IsOnCooldown())
        {
            logWritten($"Wardrobe outfit '{snapshot.Name}' blocked: Wardrobe is on cooldown.");
            return false;
        }

        // Validate all params exist on current avatar
        var avatarFilePath = VrChatLocalOscCacheService.GetAvatarOscFilePath(vrChatUserId, snapshot.AvatarId);
        if (string.IsNullOrWhiteSpace(avatarFilePath) || !System.IO.File.Exists(avatarFilePath))
        {
            logWritten($"Wardrobe outfit '{snapshot.Name}' blocked: Avatar parameter cache not available for '{snapshot.AvatarId}'.");
            return false;
        }

        var cachedParams = await localOscCacheService.LoadAvatarParametersAsync(vrChatUserId, snapshot.AvatarId, cancellationToken);
        var cachedParamNames = new HashSet<string>(cachedParams.Select(p => p.Address), StringComparer.OrdinalIgnoreCase);

        foreach (var param in snapshot.Params)
        {
            if (!cachedParamNames.Contains(param.ParameterName))
            {
                var shortName = param.ParameterName.Split('/').LastOrDefault() ?? param.ParameterName;
                logWritten($"Wardrobe outfit '{snapshot.Name}' blocked: Parameter '{shortName}' not found on current avatar.");
                return false;
            }
        }

        var configuredRestoreAddresses = GetConfiguredRestoreAddresses(snapshot);
        var preRestoreSnapshot = await CaptureRestoreSnapshotAsync(snapshot, configuredRestoreAddresses, cancellationToken);
        if (preRestoreSnapshot is null)
        {
            return false;
        }

        var fallbackRestoreValues = GetConfiguredRestoreValues(preRestoreSnapshot.Values, configuredRestoreAddresses);
        if (!TryBuildRestorePackets(
                fallbackRestoreValues,
                configuredRestoreAddresses,
                out var fallbackRestorePackets,
                out var restoreFailure))
        {
            logWritten($"Safe-canceled Wardrobe outfit '{snapshot.Name}' because Crystal Relay could not build restore packets: {restoreFailure}");
            return false;
        }

        var effectiveActiveSeconds = Math.Max(1d, snapshot.ActiveTimeSeconds);
        if (effectiveActiveSeconds < WardrobeDiffObservationDelay.TotalSeconds)
        {
            logWritten($"Wardrobe outfit '{snapshot.Name}' active time was extended from {DescribeDuration(effectiveActiveSeconds)} to {DescribeDuration(WardrobeDiffObservationDelay.TotalSeconds)} so Crystal Relay can re-check LocalAvatarData before restoring.");
            effectiveActiveSeconds = WardrobeDiffObservationDelay.TotalSeconds;
        }

        var packets = new List<byte[]>();
        foreach (var param in snapshot.Params)
        {
            var packet = oscClient.BuildAvatarParameterPacket(param.ParameterName, param.ParameterType, param.SetValue);
            packets.Add(packet);
        }

        var restoreKey = CreateRestoreKey(snapshot);
        CancelActiveRestore(restoreKey);

        var restoreCts = new CancellationTokenSource();
        var restoreSession = new WardrobeRestoreSession(restoreCts);
        lock (stateGate)
        {
            activeRestores[restoreKey] = restoreSession;
        }

        _ = Task.Run(async () =>
        {
            try
            {
                logWritten($"Wardrobe outfit '{snapshot.Name}' is waiting {DescribeDuration(WardrobeDiffObservationDelay.TotalSeconds)} before reading the post-change LocalAvatarData snapshot.");
                await Task.Delay(WardrobeDiffObservationDelay, restoreCts.Token);

                if (restoreCts.IsCancellationRequested) return;

                if (!IsRestoreSessionCurrent(restoreKey, restoreSession))
                {
                    return;
                }

                var restoreResolution = await ResolveDiffRestoreAsync(
                    snapshot,
                    preRestoreSnapshot,
                    configuredRestoreAddresses,
                    fallbackRestorePackets,
                    restoreCts.Token);
                var restorePackets = restoreResolution.Packets;

                var remainingDelay = TimeSpan.FromSeconds(effectiveActiveSeconds) - WardrobeDiffObservationDelay;
                if (remainingDelay > TimeSpan.Zero)
                {
                    await Task.Delay(remainingDelay, restoreCts.Token);
                }

                if (restoreCts.IsCancellationRequested) return;

                if (!IsRestoreSessionCurrent(restoreKey, restoreSession))
                {
                    return;
                }

                if (restorePackets.Count > 0)
                {
                    await SendPacketsAsync(restorePackets, CancellationToken.None);
                    logWritten($"Restored Wardrobe outfit '{snapshot.Name}' with {restorePackets.Count} param{(restorePackets.Count == 1 ? string.Empty : "s")}.");
                }
                else
                {
                    logWritten($"Wardrobe outfit '{snapshot.Name}' had no captured values to restore.");
                }

                // Start global cooldown after restore
                if (snapshot.CooldownSeconds > 0)
                {
                    lock (stateGate)
                    {
                        wardrobeCooldownUntil = DateTimeOffset.UtcNow.AddSeconds(snapshot.CooldownSeconds);
                    }
                    logWritten($"Wardrobe cooldown started: {snapshot.CooldownSeconds}s.");
                }
            }
            catch (OperationCanceledException)
            {
                // Restore was cancelled by a newer outfit
            }
            catch (Exception ex)
            {
                logWritten($"Wardrobe restore failed for '{snapshot.Name}': {ex.Message}");
            }
            finally
            {
                ClearActiveRestoreIfCurrent(restoreKey, restoreSession);
                restoreCts.Dispose();
            }
        }, CancellationToken.None);

        await SendPacketsAsync(packets, cancellationToken);
        logWritten($"Applied Wardrobe outfit '{snapshot.Name}' ({packets.Count} params).");
        return true;
    }

    private async Task<WardrobeRestoreResolution> ResolveDiffRestoreAsync(
        WardrobeOutfitSnapshot snapshot,
        LocalAvatarDataParameterBatchReadResult preRestoreSnapshot,
        IReadOnlyList<string> configuredRestoreAddresses,
        IReadOnlyList<byte[]> fallbackRestorePackets,
        CancellationToken cancellationToken)
    {
        var postRestoreSnapshot = await localAvatarDataService.TryReadAvatarFullSnapshotValuesAsync(
            snapshot.AvatarId,
            cancellationToken);
        if (!postRestoreSnapshot.Found)
        {
            logWritten($"Wardrobe outfit '{snapshot.Name}' could not re-check LocalAvatarData after {DescribeDuration(WardrobeDiffObservationDelay.TotalSeconds)}, so Crystal Relay will restore the configured Wardrobe params only. {postRestoreSnapshot.FailureReason}");
            return new WardrobeRestoreResolution(fallbackRestorePackets, UsedFallback: true);
        }

        var postAgeSeconds = Math.Max(0, (DateTime.UtcNow - postRestoreSnapshot.LastWriteTimeUtc).TotalSeconds);
        logWritten($"Re-checked full Wardrobe LocalAvatarData for '{snapshot.Name}' with {postRestoreSnapshot.Values.Count} safe typed value(s) from {DescribeLocalAvatarDataSource(postRestoreSnapshot.SourcePath)}. Cache age: {DescribeDuration(postAgeSeconds)}.");

        var restoreValues = new Dictionary<string, OscObservedValue>(StringComparer.OrdinalIgnoreCase);
        foreach (var originalValue in preRestoreSnapshot.Values.Values)
        {
            if (postRestoreSnapshot.Values.TryGetValue(originalValue.Address, out var currentValue)
                && !AreObservedValuesEquivalent(originalValue, currentValue))
            {
                restoreValues[originalValue.Address] = originalValue;
            }
        }

        var changedCount = restoreValues.Count;
        foreach (var configuredAddress in configuredRestoreAddresses)
        {
            if (preRestoreSnapshot.Values.TryGetValue(configuredAddress, out var originalConfiguredValue))
            {
                restoreValues[originalConfiguredValue.Address] = originalConfiguredValue;
            }
        }

        if (!TryBuildRestorePackets(
                restoreValues,
                configuredRestoreAddresses,
                out var restorePackets,
                out var restoreFailure))
        {
            logWritten($"Wardrobe outfit '{snapshot.Name}' could not build diff restore packets ({restoreFailure}), so Crystal Relay will restore the configured Wardrobe params only.");
            return new WardrobeRestoreResolution(fallbackRestorePackets, UsedFallback: true);
        }

        logWritten($"Wardrobe outfit '{snapshot.Name}' learned {changedCount} changed LocalAvatarData param{(changedCount == 1 ? string.Empty : "s")} and will restore {restorePackets.Count} param{(restorePackets.Count == 1 ? string.Empty : "s")}.");
        return new WardrobeRestoreResolution(restorePackets, UsedFallback: false);
    }

    private async Task<LocalAvatarDataParameterBatchReadResult?> CaptureRestoreSnapshotAsync(
        WardrobeOutfitSnapshot snapshot,
        IReadOnlyList<string> configuredRestoreAddresses,
        CancellationToken cancellationToken)
    {
        var localValues = await localAvatarDataService.TryReadAvatarFullSnapshotValuesAsync(
            snapshot.AvatarId,
            cancellationToken);

        if (!localValues.Found)
        {
            logWritten($"Safe-canceled Wardrobe outfit '{snapshot.Name}' because Crystal Relay could not capture a full LocalAvatarData restore snapshot: {localValues.FailureReason}");
            return null;
        }

        var missingConfiguredAddresses = configuredRestoreAddresses
            .Where(address => !localValues.Values.ContainsKey(address))
            .ToArray();
        if (missingConfiguredAddresses.Length > 0)
        {
            logWritten($"Safe-canceled Wardrobe outfit '{snapshot.Name}' because the full LocalAvatarData restore snapshot did not include {string.Join(", ", missingConfiguredAddresses)}.");
            return null;
        }

        var ageSeconds = Math.Max(0, (DateTime.UtcNow - localValues.LastWriteTimeUtc).TotalSeconds);
        logWritten($"Captured full Wardrobe restore snapshot for '{snapshot.Name}' with {localValues.Values.Count} safe typed value(s) from {DescribeLocalAvatarDataSource(localValues.SourcePath)}. Cache age: {DescribeDuration(ageSeconds)}.");

        return localValues with
        {
            Values = new Dictionary<string, OscObservedValue>(localValues.Values, StringComparer.OrdinalIgnoreCase),
            MatchedParameterNames = new Dictionary<string, string>(localValues.MatchedParameterNames, StringComparer.OrdinalIgnoreCase)
        };
    }

    private static IReadOnlyList<string> GetConfiguredRestoreAddresses(WardrobeOutfitSnapshot snapshot)
    {
        return [.. snapshot.Params
            .Select(param => param.ParameterName)
            .Where(address => !string.IsNullOrWhiteSpace(address))
            .Distinct(StringComparer.OrdinalIgnoreCase)];
    }

    private static IReadOnlyDictionary<string, OscObservedValue> GetConfiguredRestoreValues(
        IReadOnlyDictionary<string, OscObservedValue> snapshotValues,
        IReadOnlyList<string> configuredRestoreAddresses)
    {
        var configuredValues = new Dictionary<string, OscObservedValue>(StringComparer.OrdinalIgnoreCase);
        foreach (var address in configuredRestoreAddresses)
        {
            if (snapshotValues.TryGetValue(address, out var observedValue))
            {
                configuredValues[observedValue.Address] = observedValue;
            }
        }

        return configuredValues;
    }

    private bool TryBuildRestorePackets(
        IReadOnlyDictionary<string, OscObservedValue> restoreValues,
        IReadOnlyList<string> configuredRestoreAddresses,
        out List<byte[]> restorePackets,
        out string failureReason)
    {
        restorePackets = [];
        failureReason = string.Empty;

        var valuesByAddress = restoreValues.Values
            .GroupBy(value => value.Address, StringComparer.OrdinalIgnoreCase)
            .ToDictionary(group => group.Key, group => group.First(), StringComparer.OrdinalIgnoreCase);
        var orderedRestoreValues = new List<OscObservedValue>(valuesByAddress.Count);

        foreach (var configuredAddress in configuredRestoreAddresses)
        {
            if (!valuesByAddress.Remove(configuredAddress, out var configuredValue))
            {
                failureReason = $"missing configured restore value for {configuredAddress}";
                return false;
            }

            orderedRestoreValues.Add(configuredValue);
        }

        orderedRestoreValues.AddRange(valuesByAddress.Values.OrderBy(value => value.Address, StringComparer.OrdinalIgnoreCase));

        foreach (var restoreValue in orderedRestoreValues)
        {
            if (!TryFormatObservedValue(restoreValue, out var restoreText))
            {
                failureReason = $"invalid {restoreValue.ParameterType} restore value for {restoreValue.Address}";
                return false;
            }

            restorePackets.Add(oscClient.BuildAvatarParameterPacket(
                restoreValue.Address,
                restoreValue.ParameterType,
                restoreText));
        }

        if (restorePackets.Count == 0)
        {
            failureReason = "no restore packets were built";
            return false;
        }

        return true;
    }

    private static bool TryFormatObservedValue(OscObservedValue observedValue, out string valueText)
    {
        valueText = string.Empty;
        switch (observedValue.ParameterType)
        {
            case OscParameterType.Bool when observedValue.Value is bool boolValue:
                valueText = boolValue ? "True" : "False";
                return true;
            case OscParameterType.Int when observedValue.Value is int intValue:
                valueText = intValue.ToString(CultureInfo.InvariantCulture);
                return true;
            case OscParameterType.Float when observedValue.Value is float floatValue:
                valueText = floatValue.ToString(CultureInfo.InvariantCulture);
                return true;
            default:
                return false;
        }
    }

    private static bool AreObservedValuesEquivalent(OscObservedValue originalValue, OscObservedValue currentValue)
    {
        if (!string.Equals(originalValue.Address, currentValue.Address, StringComparison.OrdinalIgnoreCase)
            || originalValue.ParameterType != currentValue.ParameterType)
        {
            return false;
        }

        return originalValue.ParameterType switch
        {
            OscParameterType.Bool => originalValue.Value is bool originalBool
                && currentValue.Value is bool currentBool
                && originalBool == currentBool,
            OscParameterType.Int => originalValue.Value is int originalInt
                && currentValue.Value is int currentInt
                && originalInt == currentInt,
            OscParameterType.Float => originalValue.Value is float originalFloat
                && currentValue.Value is float currentFloat
                && Math.Abs(originalFloat - currentFloat) <= 0.0001f,
            _ => Equals(originalValue.Value, currentValue.Value)
        };
    }

    /// <summary>
    /// Cancels the active restore timer for the same avatar/profile before a newer outfit takes over.
    /// </summary>
    private void CancelActiveRestore(WardrobeRestoreKey restoreKey)
    {
        WardrobeRestoreSession? session = null;
        lock (stateGate)
        {
            if (activeRestores.Remove(restoreKey, out var activeSession))
            {
                session = activeSession;
            }
        }

        session?.Cancellation.Cancel();
    }

    private bool IsRestoreSessionCurrent(WardrobeRestoreKey restoreKey, WardrobeRestoreSession restoreSession)
    {
        lock (stateGate)
        {
            return activeRestores.TryGetValue(restoreKey, out var activeSession)
                && ReferenceEquals(activeSession, restoreSession);
        }
    }

    private void ClearActiveRestoreIfCurrent(WardrobeRestoreKey restoreKey, WardrobeRestoreSession restoreSession)
    {
        lock (stateGate)
        {
            if (activeRestores.TryGetValue(restoreKey, out var activeSession)
                && ReferenceEquals(activeSession, restoreSession))
            {
                activeRestores.Remove(restoreKey);
            }
        }
    }

    private static WardrobeRestoreKey CreateRestoreKey(WardrobeOutfitSnapshot snapshot)
    {
        return new WardrobeRestoreKey(snapshot.AvatarProfileId, snapshot.AvatarId.Trim());
    }

    private static string DescribeLocalAvatarDataSource(string sourcePath)
    {
        if (string.IsNullOrWhiteSpace(sourcePath))
        {
            return "LocalAvatarData";
        }

        var fileName = System.IO.Path.GetFileName(sourcePath);
        return string.IsNullOrWhiteSpace(fileName)
            ? "LocalAvatarData"
            : $"LocalAvatarData '{fileName}'";
    }

    private static string DescribeDuration(double seconds)
    {
        if (seconds < 1)
        {
            return "less than 1s";
        }

        if (seconds < 60)
        {
            return $"{seconds:0.#}s";
        }

        var minutes = seconds / 60;
        return $"{minutes:0.#}m";
    }

    private async Task SendPacketsAsync(IEnumerable<byte[]> packets, CancellationToken cancellationToken)
    {
        var spacing = TimeSpan.FromMilliseconds(80);
        var sentAny = false;
        foreach (var packet in packets)
        {
            if (sentAny && spacing > TimeSpan.Zero)
            {
                await Task.Delay(spacing, cancellationToken);
            }
            await oscRouterService.SendToVrChatAsync(packet, cancellationToken);
            sentAny = true;
        }
    }

    public async ValueTask DisposeAsync()
    {
        List<WardrobeRestoreSession> sessions;
        lock (stateGate)
        {
            sessions = [.. activeRestores.Values];
            activeRestores.Clear();
        }

        foreach (var session in sessions)
        {
            session.Cancellation.Cancel();
            session.Cancellation.Dispose();
        }

        await Task.CompletedTask;
    }

    private sealed record WardrobeRestoreKey(Guid AvatarProfileId, string AvatarId);

    private sealed record WardrobeRestoreSession(CancellationTokenSource Cancellation);

    private sealed record WardrobeRestoreResolution(IReadOnlyList<byte[]> Packets, bool UsedFallback);
}
