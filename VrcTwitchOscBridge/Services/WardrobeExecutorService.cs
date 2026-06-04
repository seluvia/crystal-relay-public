using System.Diagnostics;
using VrcTwitchOscBridge.Models;

namespace VrcTwitchOscBridge.Services;

/// <summary>
/// Executes Wardrobe outfit snapshots: capture current values, apply outfit, restore on timeout.
/// </summary>
internal sealed class WardrobeExecutorService : IAsyncDisposable
{
    private readonly VrChatOscClient oscClient;
    private readonly OscRouterService oscRouterService;
    private readonly VrChatLocalOscCacheService localOscCacheService;
    private readonly Action<string> logWritten;
    private readonly object stateGate = new();
    private readonly Dictionary<Guid, CancellationTokenSource> activeRestores = new();
    private DateTimeOffset? wardrobeCooldownUntil;

    public WardrobeExecutorService(
        VrChatOscClient oscClient,
        OscRouterService oscRouterService,
        VrChatLocalOscCacheService localOscCacheService,
        Action<string> logWritten)
    {
        this.oscClient = oscClient;
        this.oscRouterService = oscRouterService;
        this.localOscCacheService = localOscCacheService;
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

        // Auto-capture current values from VRChat via OSCQuery
        var capturedValues = new Dictionary<string, OscObservedValue?>();
        foreach (var param in snapshot.Params)
        {
            var currentValue = await oscRouterService.GetCurrentOscValueAsync(param.ParameterName, cancellationToken);
            capturedValues[param.ParameterName] = currentValue;
        }

        // Cancel any previous restore for this outfit (independent snapshots: last one wins)
        CancelActiveRestore(snapshot.Id);

        // Apply snapshot
        var packets = new List<byte[]>();
        foreach (var param in snapshot.Params)
        {
            var packet = oscClient.BuildAvatarParameterPacket(param.ParameterName, param.ParameterType, param.SetValue);
            packets.Add(packet);
        }

        await SendPacketsAsync(packets, cancellationToken);
        logWritten($"Applied Wardrobe outfit '{snapshot.Name}' ({packets.Count} params).");

        // Schedule restore
        var restoreCts = new CancellationTokenSource();
        lock (stateGate)
        {
            activeRestores[snapshot.Id] = restoreCts;
        }

        _ = Task.Run(async () =>
        {
            try
            {
                await Task.Delay(TimeSpan.FromSeconds(snapshot.ActiveTimeSeconds), restoreCts.Token);

                if (restoreCts.IsCancellationRequested) return;

                // Restore captured values
                var restorePackets = new List<byte[]>();
                foreach (var param in snapshot.Params)
                {
                    var captured = capturedValues[param.ParameterName];
                    if (captured != null)
                    {
                        var restorePacket = oscClient.BuildAvatarParameterPacket(
                            param.ParameterName,
                            param.ParameterType,
                            captured!.Value?.ToString() ?? string.Empty);
                        restorePackets.Add(restorePacket);
                    }
                }

                await SendPacketsAsync(restorePackets, CancellationToken.None);
                logWritten($"Restored Wardrobe outfit '{snapshot.Name}' captured values ({restorePackets.Count} params).");

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
                lock (stateGate)
                {
                    activeRestores.Remove(snapshot.Id);
                }
                restoreCts.Dispose();
            }
        }, CancellationToken.None);

        return true;
    }

    /// <summary>
    /// Cancels the active restore timer for a specific outfit.
    /// </summary>
    private void CancelActiveRestore(Guid outfitId)
    {
        lock (stateGate)
        {
            if (activeRestores.TryGetValue(outfitId, out var cts))
            {
                cts.Cancel();
                activeRestores.Remove(outfitId);
            }
        }
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
        lock (stateGate)
        {
            foreach (var cts in activeRestores.Values)
            {
                cts.Cancel();
                cts.Dispose();
            }
            activeRestores.Clear();
        }
        await Task.CompletedTask;
    }
}