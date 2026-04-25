using System.Collections.ObjectModel;
using System.Diagnostics;
using System.IO;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using VrcTwitchOscBridge.Models;

namespace VrcTwitchOscBridge.Services;

/// <summary>
/// Saves and loads Crystal Relay data.
/// This class handles normal app settings, portable save-transfer files,
/// secure metadata, VRChat caches, and secrets stored in Windows Credential Manager.
/// </summary>
public sealed class SettingsStore
{
    private static readonly JsonSerializerOptions SerializerOptions = new()
    {
        WriteIndented = true
    };

    private const string BroadcasterAccessTokenCredential = "CrystalRelay:Twitch:Broadcaster:AccessToken";
    private const string BroadcasterRefreshTokenCredential = "CrystalRelay:Twitch:Broadcaster:RefreshToken";
    private const string BotAccessTokenCredential = "CrystalRelay:Twitch:Bot:AccessToken";
    private const string BotRefreshTokenCredential = "CrystalRelay:Twitch:Bot:RefreshToken";
    private const string VrChatAuthCookieCredential = "CrystalRelay:VRChat:AuthCookie";

    private readonly string legacySettingsPath;
    private readonly string legacyPortableProfileFolderPath;
    private readonly string legacyPortableProfilePath;
    private readonly string portableProfileFolderPath;
    private readonly string portableProfilePath;
    private readonly string portableProfileBackupPath;
    private readonly string secureFolderPath;
    private readonly string legacySecureAccountsPath;
    private readonly string legacySecureSessionPath;
    private readonly string secureSessionPath;
    private readonly string secureSessionBackupPath;
    private readonly string secureVrChatAvatarCachePath;
    private readonly string secureVrChatAvatarCacheBackupPath;
    private readonly string secureVrChatOscParameterCachePath;
    private readonly string secureVrChatOscParameterCacheBackupPath;
    // OAuth tokens and the VRChat auth cookie live in Windows Credential Manager
    // instead of plain JSON files inside the app data folder.
    private readonly WindowsCredentialStore credentialStore = new();

    // Constructor resolves all app-data paths up front so the rest of the class
    // can read and write from stable locations.
    public SettingsStore()
    {
        AppDataPaths.MigrateLegacyRootIfNeeded();

        var baseFolder = AppDataPaths.RootFolder;

        legacySettingsPath = Path.Combine(baseFolder, "settings.json");
        legacyPortableProfileFolderPath = Path.Combine(baseFolder, "Void Hub Save Transfer");
        legacyPortableProfilePath = Path.Combine(legacyPortableProfileFolderPath, "void-hub.rules.json");
        portableProfileFolderPath = AppDataPaths.PortableSaveFolder;
        portableProfilePath = Path.Combine(portableProfileFolderPath, "crystal-relay.rules.json");
        portableProfileBackupPath = GetBackupPath(portableProfilePath);
        secureFolderPath = AppDataPaths.SecureFolder;
        legacySecureAccountsPath = Path.Combine(secureFolderPath, "twitch-accounts.json");
        legacySecureSessionPath = Path.Combine(secureFolderPath, "void-hub-session.secure");
        secureSessionPath = Path.Combine(secureFolderPath, "crystal-relay-session.secure");
        secureSessionBackupPath = GetBackupPath(secureSessionPath);
        secureVrChatAvatarCachePath = Path.Combine(secureFolderPath, "vrchat-avatar-cache.secure");
        secureVrChatAvatarCacheBackupPath = GetBackupPath(secureVrChatAvatarCachePath);
        secureVrChatOscParameterCachePath = Path.Combine(secureFolderPath, "vrchat-osc-parameter-cache.secure");
        secureVrChatOscParameterCacheBackupPath = GetBackupPath(secureVrChatOscParameterCachePath);

        AppDataPaths.EnsureCoreFolders();
    }

    public string PortableProfileFolderPath => portableProfileFolderPath;

    public string RootFolderPath => AppDataPaths.RootFolder;

    public AppLanguage LoadLanguagePreference()
    {
        try
        {
            MigrateLegacyBrandingFilesIfNeeded();

            if (!File.Exists(portableProfilePath))
            {
                return AppLanguage.SystemDefault;
            }

            var json = File.ReadAllText(portableProfilePath);
            var profile = JsonSerializer.Deserialize<PersistedProfileSettings>(json);
            return profile is not null && Enum.IsDefined(profile.Language)
                ? profile.Language
                : AppLanguage.SystemDefault;
        }
        catch
        {
            return AppLanguage.SystemDefault;
        }
    }

    // VRChat avatar cache stores the last known avatar list for the signed-in VRChat user
    // so setup stays faster between launches.
    public async Task<IReadOnlyList<VrChatAvatarSummary>> LoadVrChatAvatarCacheAsync(
        string userId,
        CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(userId))
        {
            return [];
        }

        var payload = await LoadProtectedJsonAsync<PersistedVrChatAvatarCache>(
            secureVrChatAvatarCachePath,
            secureVrChatAvatarCacheBackupPath,
            cancellationToken);
        if (payload is null || !string.Equals(payload.UserId, userId, StringComparison.Ordinal))
        {
            return [];
        }

        return (payload.Avatars ?? [])
            .Where(avatar => !string.IsNullOrWhiteSpace(avatar.Id))
            .Select(avatar => new VrChatAvatarSummary(
                avatar.Id ?? string.Empty,
                avatar.Name ?? (avatar.Id ?? string.Empty),
                avatar.SourceLabel ?? "Cached",
                avatar.IsCurrentAvatar))
            .ToArray();
    }

    public async Task SaveVrChatAvatarCacheAsync(
        string userId,
        IReadOnlyList<VrChatAvatarSummary> avatars,
        CancellationToken cancellationToken = default)
    {
        Directory.CreateDirectory(secureFolderPath);
        PrepareSecureFileForWrite(secureVrChatAvatarCachePath);

        var payload = new PersistedVrChatAvatarCache
        {
            UserId = userId,
            SavedAt = DateTimeOffset.UtcNow,
            Avatars = avatars.Select(avatar => new PersistedVrChatAvatar
            {
                Id = avatar.Id,
                Name = avatar.Name,
                SourceLabel = avatar.SourceLabel,
                IsCurrentAvatar = avatar.IsCurrentAvatar
            }).ToList()
        };

        await SaveProtectedJsonAsync(
            secureVrChatAvatarCachePath,
            secureVrChatAvatarCacheBackupPath,
            payload,
            cancellationToken);
    }

    // OSC parameter cache stores the saved parameter list for a specific avatar.
    // Crystal Relay uses this to keep avatar-set editing usable even when VRChat is not actively queried.
    public Task ClearVrChatAvatarCacheAsync(CancellationToken cancellationToken = default)
    {
        DeleteSecureFileIfExists(secureVrChatAvatarCachePath);
        return Task.CompletedTask;
    }

    public async Task<IReadOnlyList<VrChatOscParameterSummary>> LoadVrChatOscParameterCacheAsync(
        string userId,
        string avatarId,
        CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(userId)
            || string.IsNullOrWhiteSpace(avatarId))
        {
            return [];
        }

        var payload = await LoadProtectedJsonAsync<PersistedVrChatOscParameterCache>(
            secureVrChatOscParameterCachePath,
            secureVrChatOscParameterCacheBackupPath,
            cancellationToken);
        if (payload is null || !string.Equals(payload.UserId, userId, StringComparison.Ordinal))
        {
            return [];
        }

        var cacheEntry = (payload.Avatars ?? [])
            .FirstOrDefault(entry => string.Equals(entry.AvatarId, avatarId, StringComparison.Ordinal));

        return (cacheEntry?.Parameters ?? [])
            .Where(parameter => !string.IsNullOrWhiteSpace(parameter.Address) && !string.IsNullOrWhiteSpace(parameter.Name))
            .Select(parameter => new VrChatOscParameterSummary(
                parameter.Address ?? string.Empty,
                parameter.Name ?? string.Empty,
                parameter.ParameterType))
            .ToArray();
    }

    public async Task SaveVrChatOscParameterCacheAsync(
        string userId,
        string avatarId,
        IReadOnlyList<VrChatOscParameterSummary> parameters,
        CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(userId) || string.IsNullOrWhiteSpace(avatarId))
        {
            return;
        }

        Directory.CreateDirectory(secureFolderPath);
        PrepareSecureFileForWrite(secureVrChatOscParameterCachePath);

        var payload = await LoadProtectedJsonAsync<PersistedVrChatOscParameterCache>(
                secureVrChatOscParameterCachePath,
                secureVrChatOscParameterCacheBackupPath,
                cancellationToken)
            ?? new PersistedVrChatOscParameterCache();

        payload.UserId = userId;
        payload.SavedAt = DateTimeOffset.UtcNow;
        payload.Avatars ??= [];

        var existingEntry = payload.Avatars.FirstOrDefault(entry => string.Equals(entry.AvatarId, avatarId, StringComparison.Ordinal));
        if (existingEntry is null)
        {
            existingEntry = new PersistedVrChatOscParameterCacheEntry
            {
                AvatarId = avatarId
            };
            payload.Avatars.Add(existingEntry);
        }

        existingEntry.AvatarId = avatarId;
        existingEntry.SavedAt = DateTimeOffset.UtcNow;
        existingEntry.Parameters = parameters
            .Where(parameter => !string.IsNullOrWhiteSpace(parameter.Address) && !string.IsNullOrWhiteSpace(parameter.Name))
            .Select(parameter => new PersistedVrChatOscParameter
            {
                Address = parameter.Address,
                Name = parameter.Name,
                ParameterType = parameter.ParameterType
            })
            .ToList();

        await SaveProtectedJsonAsync(
            secureVrChatOscParameterCachePath,
            secureVrChatOscParameterCacheBackupPath,
            payload,
            cancellationToken);
    }

    public Task ClearVrChatOscParameterCacheAsync(CancellationToken cancellationToken = default)
    {
        DeleteSecureFileIfExists(secureVrChatOscParameterCachePath);
        return Task.CompletedTask;
    }

    private static void PrepareSecureFileForWrite(string path)
    {
        if (!File.Exists(path))
        {
            return;
        }

        File.SetAttributes(path, FileAttributes.Normal);
    }

    private static void HideSecureFile(string path)
    {
        File.SetAttributes(path, FileAttributes.Hidden);
    }

    private static void DeleteSecureFileIfExists(string path)
    {
        if (!File.Exists(path))
        {
            return;
        }

        File.SetAttributes(path, FileAttributes.Normal);
        File.Delete(path);
    }

    private static string GetTwitchAccessTokenCredential(BridgeAccountRole role) => role switch
    {
        BridgeAccountRole.Bot => BotAccessTokenCredential,
        _ => BroadcasterAccessTokenCredential
    };

    private static string GetTwitchRefreshTokenCredential(BridgeAccountRole role) => role switch
    {
        BridgeAccountRole.Bot => BotRefreshTokenCredential,
        _ => BroadcasterRefreshTokenCredential
    };

    public async Task<AppSettings> LoadAsync(CancellationToken cancellationToken = default)
    {
        // Load is intentionally tolerant of mixed storage generations. Crystal Relay rebuilds
        // the latest settings shape from current files first, then quietly pulls legacy data
        // forward so upgrades do not force manual repair or credential re-entry.
        MigrateLegacyBrandingFilesIfNeeded();

        if (!File.Exists(portableProfilePath)
            && !File.Exists(secureSessionPath)
            && !File.Exists(legacySecureAccountsPath)
            && File.Exists(legacySettingsPath))
        {
            var migrated = await LoadLegacySettingsAsync(cancellationToken);
            if (migrated is not null)
            {
                await SaveAsync(migrated, cancellationToken);
                return migrated;
            }
        }

        var settings = CreateDefaultSettings();

        var profile = await LoadJsonAsync<PersistedProfileSettings>(
            portableProfilePath,
            portableProfileBackupPath,
            cancellationToken);
        if (profile is not null)
        {
            settings.Language = Enum.IsDefined(profile.Language)
                ? profile.Language
                : settings.Language;
            settings.Theme = Enum.IsDefined(profile.Theme)
                ? profile.Theme
                : AppTheme.VoidCrystal;
            settings.InterfaceOpacityPercent = profile.InterfaceOpacityPercent > 0
                ? profile.InterfaceOpacityPercent
                : settings.InterfaceOpacityPercent;
            settings.ChatTextSize = profile.ChatTextSize > 0
                ? profile.ChatTextSize
                : settings.ChatTextSize;
            settings.ChatOpacityPercent = profile.ChatOpacityPercent > 0
                ? profile.ChatOpacityPercent
                : settings.ChatOpacityPercent;
            settings.ChatShowTimestamps = profile.ChatShowTimestamps ?? settings.ChatShowTimestamps;
            settings.ChatTimestampFormat = profile.ChatTimestampFormat is not null
                && Enum.IsDefined(profile.ChatTimestampFormat.Value)
                    ? profile.ChatTimestampFormat.Value
                    : settings.ChatTimestampFormat;
            settings.ChatFontFamily = string.IsNullOrWhiteSpace(profile.ChatFontFamily)
                ? settings.ChatFontFamily
                : profile.ChatFontFamily;
            settings.ChatboxAlwaysOnTop = profile.ChatboxAlwaysOnTop ?? settings.ChatboxAlwaysOnTop;
            settings.ChatboxSettingsPanelOpen = profile.ChatboxSettingsPanelOpen ?? settings.ChatboxSettingsPanelOpen;
            settings.ChatboxOverlayMode = profile.ChatboxOverlayMode ?? settings.ChatboxOverlayMode;
            settings.ChatboxOscEnabled = profile.ChatboxOscEnabled ?? settings.ChatboxOscEnabled;
            settings.ChatboxOscDelaySeconds = profile.ChatboxOscDelaySeconds is >= 1 and <= 6
                ? profile.ChatboxOscDelaySeconds.Value
                : settings.ChatboxOscDelaySeconds;
            settings.ChatboxViewerSoundEnabled = profile.ChatboxViewerSoundEnabled ?? settings.ChatboxViewerSoundEnabled;
            settings.SupporterOverrideInfoMessageEnabled = profile.SupporterOverrideInfoMessageEnabled ?? settings.SupporterOverrideInfoMessageEnabled;
            settings.ChannelPointRewardTestModeEnabled = profile.ChannelPointRewardTestModeEnabled ?? settings.ChannelPointRewardTestModeEnabled;
            settings.EmergencyRedeemStopEnabled = profile.EmergencyRedeemStopEnabled ?? settings.EmergencyRedeemStopEnabled;
            settings.DesktopModeInputLockEnabled = profile.DesktopModeInputLockEnabled ?? settings.DesktopModeInputLockEnabled;
            settings.EasterEggsEnabled = profile.EasterEggsEnabled ?? settings.EasterEggsEnabled;
            settings.MainWindowTrayTipShown = profile.MainWindowTrayTipShown ?? settings.MainWindowTrayTipShown;
            settings.AvatarProfiles = new ObservableCollection<AvatarTriggerProfile>((profile.AvatarProfiles ?? []).Select(ToAvatarProfile));
            settings.GlobalMovementRules = new ObservableCollection<TriggerRule>((profile.GlobalMovementRules ?? []).Select(ToRule));
            settings.GlobalOverrideRules = new ObservableCollection<TriggerRule>((profile.GlobalOverrideRules ?? []).Select(ToRule));
            settings.Rules = new ObservableCollection<TriggerRule>((profile.Rules ?? []).Select(ToRule));
        }

        var secureMetadata = await LoadProtectedJsonAsync<PersistedSecureMetadataSettings>(
            secureSessionPath,
            secureSessionBackupPath,
            cancellationToken);
        var legacyProtectedSecure = await LoadProtectedJsonAsync<PersistedSecureSettings>(
            secureSessionPath,
            secureSessionBackupPath,
            cancellationToken);
        var legacyPlainSecure = await LoadJsonAsync<PersistedSecureSettings>(legacySecureAccountsPath, cancellationToken);
        var migratedSecrets = false;
        var needsMetadataRewrite = false;

        if (legacyProtectedSecure is not null)
        {
            migratedSecrets |= MigrateSecretsFromLegacyPayload(legacyProtectedSecure);
            needsMetadataRewrite |= HasLegacySecrets(legacyProtectedSecure);

            if (secureMetadata is null)
            {
                secureMetadata = ToSecureMetadata(legacyProtectedSecure);
            }
        }

        if (legacyPlainSecure is not null)
        {
            migratedSecrets |= MigrateSecretsFromLegacyPayload(legacyPlainSecure);
            needsMetadataRewrite = true;

            if (secureMetadata is null)
            {
                secureMetadata = ToSecureMetadata(legacyPlainSecure);
            }
        }

        if (secureMetadata is not null)
        {
            settings.Broadcaster = ToAccountSettings(secureMetadata.Broadcaster, BridgeAccountRole.Broadcaster);
            settings.Bot = ToAccountSettings(secureMetadata.Bot, BridgeAccountRole.Bot);
            settings.VrChat = ToVrChatAccountSettings(secureMetadata.VrChat);
        }

        if (settings.AvatarProfiles.Count == 0
            && settings.GlobalMovementRules.Count == 0
            && settings.GlobalOverrideRules.Count == 0
            && settings.Rules.Count > 0)
        {
            MigrateLegacyRulesIntoNewCollections(settings, settings.Rules);
        }

        if (needsMetadataRewrite || legacyPlainSecure is not null || migratedSecrets)
        {
            await SaveSecureMetadataAsync(settings, cancellationToken);
            DeleteSecureFileIfExists(legacySecureAccountsPath);
            DeleteSecureFileIfExists(legacySecureSessionPath);
        }

        return settings;
    }

    private void MigrateLegacyBrandingFilesIfNeeded()
    {
        if (!File.Exists(portableProfilePath) && File.Exists(legacyPortableProfilePath))
        {
            Directory.CreateDirectory(portableProfileFolderPath);
            File.Copy(legacyPortableProfilePath, portableProfilePath, overwrite: false);
        }

        if (!File.Exists(secureSessionPath) && File.Exists(legacySecureSessionPath))
        {
            Directory.CreateDirectory(secureFolderPath);
            File.Copy(legacySecureSessionPath, secureSessionPath, overwrite: false);
            HideSecureFile(secureSessionPath);
        }
    }

    public async Task SaveAsync(AppSettings settings, CancellationToken cancellationToken = default)
    {
        Directory.CreateDirectory(portableProfileFolderPath);
        Directory.CreateDirectory(secureFolderPath);

        var profile = new PersistedProfileSettings
        {
            Language = settings.Language,
            Theme = settings.Theme,
            InterfaceOpacityPercent = settings.InterfaceOpacityPercent,
            ChatTextSize = settings.ChatTextSize,
            ChatOpacityPercent = settings.ChatOpacityPercent,
            ChatShowTimestamps = settings.ChatShowTimestamps,
            ChatTimestampFormat = settings.ChatTimestampFormat,
            ChatFontFamily = settings.ChatFontFamily,
            ChatboxAlwaysOnTop = settings.ChatboxAlwaysOnTop,
            ChatboxSettingsPanelOpen = settings.ChatboxSettingsPanelOpen,
            ChatboxOverlayMode = settings.ChatboxOverlayMode,
            ChatboxOscEnabled = settings.ChatboxOscEnabled,
            ChatboxOscDelaySeconds = settings.ChatboxOscDelaySeconds,
            ChatboxViewerSoundEnabled = settings.ChatboxViewerSoundEnabled,
            SupporterOverrideInfoMessageEnabled = settings.SupporterOverrideInfoMessageEnabled,
            ChannelPointRewardTestModeEnabled = settings.ChannelPointRewardTestModeEnabled,
            EmergencyRedeemStopEnabled = settings.EmergencyRedeemStopEnabled,
            DesktopModeInputLockEnabled = settings.DesktopModeInputLockEnabled,
            EasterEggsEnabled = settings.EasterEggsEnabled,
            MainWindowTrayTipShown = settings.MainWindowTrayTipShown,
            AvatarProfiles = [.. settings.AvatarProfiles.Select(ToPersistedAvatarProfile)],
            GlobalMovementRules = [.. settings.GlobalMovementRules.Select(ToPersistedRule)],
            GlobalOverrideRules = [.. settings.GlobalOverrideRules.Select(ToPersistedRule)]
        };

        await SaveTextFileAtomicallyAsync(
            portableProfilePath,
            portableProfileBackupPath,
            JsonSerializer.Serialize(profile, SerializerOptions),
            cancellationToken);

        SaveSecrets(settings);
        await SaveSecureMetadataAsync(settings, cancellationToken);
        DeleteSecureFileIfExists(legacySecureAccountsPath);
        DeleteSecureFileIfExists(legacySecureSessionPath);
    }

    private async Task<AppSettings?> LoadLegacySettingsAsync(CancellationToken cancellationToken)
    {
        var persisted = await LoadJsonAsync<PersistedLegacySettings>(legacySettingsPath, cancellationToken);
        return persisted is null ? null : ToSettings(persisted);
    }

    private void SaveSecrets(AppSettings settings)
    {
        SaveTwitchSecrets(settings.Broadcaster, BridgeAccountRole.Broadcaster);
        SaveTwitchSecrets(settings.Bot, BridgeAccountRole.Bot);
        credentialStore.SaveSecret(VrChatAuthCookieCredential, settings.VrChat.AuthCookie);
    }

    private void SaveTwitchSecrets(TwitchAccountSettings account, BridgeAccountRole role)
    {
        credentialStore.SaveSecret(GetTwitchAccessTokenCredential(role), account.AccessToken);
        credentialStore.SaveSecret(GetTwitchRefreshTokenCredential(role), account.RefreshToken);
    }

    private bool MigrateSecretsFromLegacyPayload(PersistedSecureSettings secure)
    {
        var migrated = false;
        migrated |= MigrateLegacyTwitchSecrets(secure.Broadcaster, BridgeAccountRole.Broadcaster);
        migrated |= MigrateLegacyTwitchSecrets(secure.Bot, BridgeAccountRole.Bot);

        if (!string.IsNullOrWhiteSpace(secure.VrChat?.AuthCookie))
        {
            var authCookie = Unprotect(secure.VrChat.AuthCookie ?? string.Empty);
            if (!string.IsNullOrWhiteSpace(authCookie))
            {
                credentialStore.SaveSecret(VrChatAuthCookieCredential, authCookie);
                migrated = true;
            }
        }

        return migrated;
    }

    private bool MigrateLegacyTwitchSecrets(PersistedTwitchAccountSettings? account, BridgeAccountRole role)
    {
        if (account is null)
        {
            return false;
        }

        var accessToken = Unprotect(account.AccessToken ?? string.Empty);
        var refreshToken = Unprotect(account.RefreshToken ?? string.Empty);
        if (string.IsNullOrWhiteSpace(accessToken) && string.IsNullOrWhiteSpace(refreshToken))
        {
            return false;
        }

        credentialStore.SaveSecret(GetTwitchAccessTokenCredential(role), accessToken);
        credentialStore.SaveSecret(GetTwitchRefreshTokenCredential(role), refreshToken);
        return true;
    }

    private async Task SaveSecureMetadataAsync(AppSettings settings, CancellationToken cancellationToken)
    {
        var secureMetadata = new PersistedSecureMetadataSettings
        {
            Broadcaster = ToPersistedAccountMetadata(settings.Broadcaster),
            Bot = ToPersistedAccountMetadata(settings.Bot),
            VrChat = ToPersistedVrChatMetadata(settings.VrChat)
        };

        await SaveProtectedJsonAsync(secureSessionPath, secureSessionBackupPath, secureMetadata, cancellationToken);
    }

    private static async Task<T?> LoadJsonAsync<T>(string path, CancellationToken cancellationToken)
    {
        return await LoadJsonAsync<T>(path, backupPath: null, cancellationToken);
    }

    private static async Task<T?> LoadJsonAsync<T>(
        string path,
        string? backupPath,
        CancellationToken cancellationToken)
    {
        var primaryAttempt = await TryLoadJsonFileAsync<T>(path, cancellationToken);
        if (primaryAttempt.Success)
        {
            return primaryAttempt.Value;
        }

        if (!string.IsNullOrWhiteSpace(primaryAttempt.ErrorMessage))
        {
            Debug.WriteLine($"Crystal Relay could not read '{path}': {primaryAttempt.ErrorMessage}");
        }

        if (string.IsNullOrWhiteSpace(backupPath))
        {
            return default;
        }

        var backupAttempt = await TryLoadJsonFileAsync<T>(backupPath, cancellationToken);
        if (backupAttempt.Success)
        {
            Debug.WriteLine($"Crystal Relay restored settings from backup '{backupPath}'.");
            return backupAttempt.Value;
        }

        if (!string.IsNullOrWhiteSpace(backupAttempt.ErrorMessage))
        {
            Debug.WriteLine($"Crystal Relay could not read backup '{backupPath}': {backupAttempt.ErrorMessage}");
        }

        return default;
    }

    private static async Task<LoadAttemptResult<T>> TryLoadJsonFileAsync<T>(string path, CancellationToken cancellationToken)
    {
        if (!File.Exists(path))
        {
            return new LoadAttemptResult<T>(false, default, null);
        }

        try
        {
            await using var stream = File.OpenRead(path);
            return new LoadAttemptResult<T>(
                true,
                await JsonSerializer.DeserializeAsync<T>(stream, SerializerOptions, cancellationToken),
                null);
        }
        catch (Exception ex)
        {
            return new LoadAttemptResult<T>(false, default, ex.Message);
        }
    }

    private static async Task<T?> LoadProtectedJsonAsync<T>(string path, CancellationToken cancellationToken)
    {
        return await LoadProtectedJsonAsync<T>(path, backupPath: null, cancellationToken);
    }

    private static async Task<T?> LoadProtectedJsonAsync<T>(
        string path,
        string? backupPath,
        CancellationToken cancellationToken)
    {
        var primaryAttempt = await TryLoadProtectedJsonFileAsync<T>(path, cancellationToken);
        if (primaryAttempt.Success)
        {
            return primaryAttempt.Value;
        }

        if (!string.IsNullOrWhiteSpace(primaryAttempt.ErrorMessage))
        {
            Debug.WriteLine($"Crystal Relay could not read secure file '{path}': {primaryAttempt.ErrorMessage}");
        }

        if (string.IsNullOrWhiteSpace(backupPath))
        {
            return default;
        }

        var backupAttempt = await TryLoadProtectedJsonFileAsync<T>(backupPath, cancellationToken);
        if (backupAttempt.Success)
        {
            Debug.WriteLine($"Crystal Relay restored secure data from backup '{backupPath}'.");
            return backupAttempt.Value;
        }

        if (!string.IsNullOrWhiteSpace(backupAttempt.ErrorMessage))
        {
            Debug.WriteLine($"Crystal Relay could not read secure backup '{backupPath}': {backupAttempt.ErrorMessage}");
        }

        return default;
    }

    private static async Task<LoadAttemptResult<T>> TryLoadProtectedJsonFileAsync<T>(string path, CancellationToken cancellationToken)
    {
        if (!File.Exists(path))
        {
            return new LoadAttemptResult<T>(false, default, null);
        }

        try
        {
            var encryptedBytes = await File.ReadAllBytesAsync(path, cancellationToken);
            var json = UnprotectBytes(encryptedBytes);
            if (string.IsNullOrWhiteSpace(json))
            {
                var protectedText = Encoding.UTF8.GetString(encryptedBytes);
                json = Unprotect(protectedText);
            }

            return string.IsNullOrWhiteSpace(json)
                ? new LoadAttemptResult<T>(false, default, "Protected file was empty after decryption.")
                : new LoadAttemptResult<T>(true, JsonSerializer.Deserialize<T>(json, SerializerOptions), null);
        }
        catch (Exception ex)
        {
            return new LoadAttemptResult<T>(false, default, ex.Message);
        }
    }

    private static async Task SaveProtectedJsonAsync<T>(
        string path,
        string backupPath,
        T value,
        CancellationToken cancellationToken)
    {
        var json = JsonSerializer.Serialize(value, SerializerOptions);
        var encryptedBytes = ProtectBytes(json);
        await SaveBytesFileAtomicallyAsync(path, backupPath, encryptedBytes, cancellationToken, hiddenResult: true);
    }

    private static AppSettings CreateDefaultSettings()
    {
        return new AppSettings
        {
            AvatarProfiles = new ObservableCollection<AvatarTriggerProfile>(),
            GlobalMovementRules = new ObservableCollection<TriggerRule>(),
            GlobalOverrideRules = new ObservableCollection<TriggerRule>(),
            Rules = new ObservableCollection<TriggerRule>()
        };
    }

    private static PersistedAvatarTriggerProfile ToPersistedAvatarProfile(AvatarTriggerProfile profile)
    {
        return new PersistedAvatarTriggerProfile
        {
            Id = profile.Id,
            IsEnabled = profile.IsEnabled,
            IsMasterProfile = profile.IsMasterProfile,
            IsRewardTestOverrideEnabled = profile.IsRewardTestOverrideEnabled,
            Name = profile.Name,
            AvatarId = profile.AvatarId,
            AvatarName = profile.AvatarName,
            ChannelPointRules = [.. profile.ChannelPointRules.Select(ToPersistedRule)]
        };
    }

    private static AvatarTriggerProfile ToAvatarProfile(PersistedAvatarTriggerProfile profile)
    {
        return new AvatarTriggerProfile
        {
            Id = profile.Id == Guid.Empty ? Guid.NewGuid() : profile.Id,
            IsEnabled = profile.IsEnabled,
            IsMasterProfile = profile.IsMasterProfile,
            IsRewardTestOverrideEnabled = profile.IsRewardTestOverrideEnabled,
            Name = string.IsNullOrWhiteSpace(profile.Name) ? "New Avatar Set" : profile.Name,
            AvatarId = profile.AvatarId ?? string.Empty,
            AvatarName = profile.AvatarName ?? string.Empty,
            ChannelPointRules = new ObservableCollection<TriggerRule>((profile.ChannelPointRules ?? [])
                .Select(ToRule)
                .Select(rule =>
                {
                    rule.TriggerType = TwitchTriggerType.ChannelPoints;
                    return rule;
                }))
        };
    }

    private static PersistedTriggerRule ToPersistedRule(TriggerRule rule)
    {
        return new PersistedTriggerRule
        {
            Id = rule.Id,
            IsEnabled = rule.IsEnabled,
            Name = rule.Name,
            TriggerType = rule.TriggerType,
            ChannelPointRewardId = rule.ChannelPointRewardId,
            ChannelPointRewardTitle = rule.ChannelPointRewardTitle,
            ChannelPointRewardCost = rule.ChannelPointRewardCost,
            ManagedRewardReadyColor = rule.ManagedRewardReadyColor,
            ManagedRewardCooldownColor = rule.ManagedRewardCooldownColor,
            ChatCommandEnabled = rule.ChatCommandEnabled,
            ChatCommandText = rule.ChatCommandText,
            ChatCommandPermission = rule.ChatCommandPermission,
            MinimumAmount = rule.MinimumAmount,
            AmountScaledDurationEnabled = rule.AmountScaledDurationEnabled,
            AmountUnitsPerDuration = rule.AmountUnitsPerDuration,
            SecondsPerAmountUnit = rule.SecondsPerAmountUnit,
            BitsAmountUnitsPerDuration = rule.BitsAmountUnitsPerDuration,
            BitsSecondsPerAmountUnit = rule.BitsSecondsPerAmountUnit,
            SubscriptionsAmountUnitsPerDuration = rule.SubscriptionsAmountUnitsPerDuration,
            SubscriptionsSecondsPerAmountUnit = rule.SubscriptionsSecondsPerAmountUnit,
            MaxAccumulatedDurationEnabled = rule.MaxAccumulatedDurationEnabled,
            MaxAccumulatedDurationSeconds = rule.MaxAccumulatedDurationSeconds,
            ActionType = rule.ActionType,
            MovementDirection = rule.MovementDirection,
            ParameterName = rule.ParameterName,
            ParameterType = rule.ParameterType,
            IntZeroDurationMode = rule.IntZeroDurationMode,
            ParameterValue = rule.ParameterValue,
            AvatarChangeTargetId = rule.AvatarChangeTargetId,
            AvatarTargetName = rule.AvatarTargetName,
            ResetValue = rule.ResetValue,
            AvatarChangeResetId = rule.AvatarChangeResetId,
            ResetAvatarName = rule.ResetAvatarName,
            AvatarRouletAvatarIds = [.. rule.AvatarRouletAvatarIds],
            AvatarRouletAvatarNames = [.. rule.AvatarRouletAvatarNames],
            RangeMinimum = rule.RangeMinimum,
            RangeMaximum = rule.RangeMaximum,
            DurationSeconds = rule.DurationSeconds,
            CooldownSeconds = rule.CooldownSeconds,
            BotMessageTemplate = rule.BotMessageTemplate,
            TemporarilyDisabledRuleIds = [.. rule.TemporarilyDisabledRuleIds]
        };
    }

    private static TriggerRule ToRule(PersistedTriggerRule rule)
    {
        var migratedAvatarChangeTargetId = !string.IsNullOrWhiteSpace(rule.AvatarChangeTargetId)
            ? rule.AvatarChangeTargetId
            : rule.ActionType == OscActionType.AvatarChange
                ? rule.ParameterValue
                : string.Empty;
        var migratedAvatarChangeResetId = !string.IsNullOrWhiteSpace(rule.AvatarChangeResetId)
            ? rule.AvatarChangeResetId
            : rule.ActionType == OscActionType.AvatarChange
                ? rule.ResetValue
                : string.Empty;
        var migratedParameterValue = rule.ActionType == OscActionType.AvatarChange && string.IsNullOrWhiteSpace(rule.AvatarChangeTargetId)
            ? string.Empty
            : (rule.ParameterValue ?? string.Empty);
        var migratedResetValue = rule.ActionType == OscActionType.AvatarChange && string.IsNullOrWhiteSpace(rule.AvatarChangeResetId)
            ? string.Empty
            : (rule.ResetValue ?? string.Empty);

        return new TriggerRule
        {
            Id = rule.Id == Guid.Empty ? Guid.NewGuid() : rule.Id,
            IsEnabled = rule.IsEnabled,
            Name = string.IsNullOrWhiteSpace(rule.Name) ? "New Twitch trigger" : rule.Name,
            TriggerType = (int)rule.TriggerType >= 2 ? TwitchTriggerType.Subscriptions : rule.TriggerType,
            ChannelPointRewardId = rule.ChannelPointRewardId ?? string.Empty,
            ChannelPointRewardTitle = !string.IsNullOrWhiteSpace(rule.ChannelPointRewardTitle)
                ? rule.ChannelPointRewardTitle
                : (rule.MatchText ?? string.Empty),
            ChannelPointRewardCost = rule.ChannelPointRewardCost <= 0 ? 100 : rule.ChannelPointRewardCost,
            ManagedRewardReadyColor = ManagedRewardPresentation.NormalizeReadyBackgroundColor(rule.ManagedRewardReadyColor),
            ManagedRewardCooldownColor = ManagedRewardPresentation.NormalizeCooldownBackgroundColor(rule.ManagedRewardCooldownColor),
            ChatCommandEnabled = rule.ChatCommandEnabled,
            ChatCommandText = rule.ChatCommandText ?? string.Empty,
            ChatCommandPermission = Enum.IsDefined(rule.ChatCommandPermission)
                ? rule.ChatCommandPermission
                : ChatCommandPermission.Moderators,
            MinimumAmount = rule.MinimumAmount <= 0 ? 1 : rule.MinimumAmount,
            AmountScaledDurationEnabled = rule.AmountScaledDurationEnabled,
            AmountUnitsPerDuration = rule.AmountUnitsPerDuration <= 0 ? 1 : rule.AmountUnitsPerDuration,
            SecondsPerAmountUnit = rule.SecondsPerAmountUnit <= 0 ? 1 : rule.SecondsPerAmountUnit,
            BitsAmountUnitsPerDuration = rule.BitsAmountUnitsPerDuration <= 0
                ? (rule.AmountUnitsPerDuration <= 0 ? 1 : rule.AmountUnitsPerDuration)
                : rule.BitsAmountUnitsPerDuration,
            BitsSecondsPerAmountUnit = rule.BitsSecondsPerAmountUnit <= 0
                ? (rule.SecondsPerAmountUnit <= 0 ? 1 : rule.SecondsPerAmountUnit)
                : rule.BitsSecondsPerAmountUnit,
            SubscriptionsAmountUnitsPerDuration = rule.SubscriptionsAmountUnitsPerDuration <= 0
                ? (rule.AmountUnitsPerDuration <= 0 ? 1 : rule.AmountUnitsPerDuration)
                : rule.SubscriptionsAmountUnitsPerDuration,
            SubscriptionsSecondsPerAmountUnit = rule.SubscriptionsSecondsPerAmountUnit <= 0
                ? (rule.SecondsPerAmountUnit <= 0 ? 1 : rule.SecondsPerAmountUnit)
                : rule.SubscriptionsSecondsPerAmountUnit,
            MaxAccumulatedDurationEnabled = rule.MaxAccumulatedDurationEnabled,
            MaxAccumulatedDurationSeconds = rule.MaxAccumulatedDurationSeconds <= 0
                ? 1800
                : rule.MaxAccumulatedDurationSeconds,
            ActionType = rule.ActionType,
            MovementDirection = rule.MovementDirection,
            ParameterName = rule.ParameterName ?? string.Empty,
            ParameterType = rule.ParameterType,
            IntZeroDurationMode = rule.IntZeroDurationMode,
            ParameterValue = migratedParameterValue,
            AvatarChangeTargetId = migratedAvatarChangeTargetId ?? string.Empty,
            AvatarTargetName = rule.AvatarTargetName ?? string.Empty,
            ResetValue = migratedResetValue,
            AvatarChangeResetId = migratedAvatarChangeResetId ?? string.Empty,
            ResetAvatarName = rule.ResetAvatarName ?? string.Empty,
            AvatarRouletAvatarIds = new ObservableCollection<string>((rule.AvatarRouletAvatarIds ?? [])
                .Where(avatarId => !string.IsNullOrWhiteSpace(avatarId))
                .Select(avatarId => avatarId!.Trim())
                .Distinct(StringComparer.Ordinal)),
            AvatarRouletAvatarNames = new ObservableCollection<string>((rule.AvatarRouletAvatarNames ?? [])
                .Select(avatarName => avatarName?.Trim() ?? string.Empty)),
            RangeMinimum = rule.RangeMinimum,
            RangeMaximum = rule.RangeMaximum == 0 && rule.RangeMinimum == 0 ? 5 : rule.RangeMaximum,
            DurationSeconds = Math.Max(0, rule.DurationSeconds),
            CooldownSeconds = Math.Max(0, rule.CooldownSeconds),
            TemporarilyDisabledRuleIds = new ObservableCollection<Guid>((rule.TemporarilyDisabledRuleIds ?? [])
                .Where(ruleId => ruleId != Guid.Empty)
                .Distinct()),
            BotMessageTemplate = string.IsNullOrWhiteSpace(rule.BotMessageTemplate)
                ? "{user} triggered {rule}. Active for {duration}. Cooldown {cooldown}."
                : rule.BotMessageTemplate
        };
    }

    private static AppSettings ToSettings(PersistedLegacySettings persisted)
    {
        var settings = new AppSettings
        {
            Broadcaster = ToLegacyAccountSettings(persisted.Broadcaster),
            Bot = ToLegacyAccountSettings(persisted.Bot),
            VrChat = new VrChatAccountSettings(),
            Rules = new ObservableCollection<TriggerRule>((persisted.Rules ?? []).Select(ToRule))
        };

        MigrateLegacyRulesIntoNewCollections(settings, settings.Rules);
        return settings;
    }

    private static void MigrateLegacyRulesIntoNewCollections(AppSettings settings, IEnumerable<TriggerRule> legacyRules)
    {
        var clonedRules = legacyRules
            .Select(rule => ToRule(ToPersistedRule(rule)))
            .ToArray();

        settings.AvatarProfiles.Clear();
        settings.GlobalMovementRules.Clear();
        settings.GlobalOverrideRules.Clear();

        var legacyChannelPointRules = clonedRules
            .Where(rule => rule.TriggerType == TwitchTriggerType.ChannelPoints && rule.ActionType != OscActionType.PlayerMovement)
            .ToArray();

        foreach (var movementRule in clonedRules.Where(rule =>
                     rule.TriggerType == TwitchTriggerType.ChannelPoints
                     && rule.ActionType == OscActionType.PlayerMovement))
        {
            settings.GlobalMovementRules.Add(movementRule);
        }

        if (legacyChannelPointRules.Length > 0)
        {
            var importedProfile = new AvatarTriggerProfile
            {
                Name = legacyChannelPointRules.Any(rule => rule.ActionType == OscActionType.AvatarChange)
                    ? "Imported Avatar Change"
                    : "Imported Avatar Sets",
                AvatarId = settings.VrChat.CurrentAvatarId,
                AvatarName = string.Empty,
                IsMasterProfile = legacyChannelPointRules.Any(rule => rule.ActionType == OscActionType.AvatarChange),
                ChannelPointRules = new ObservableCollection<TriggerRule>(legacyChannelPointRules.Select(rule =>
                {
                    rule.TriggerType = TwitchTriggerType.ChannelPoints;
                    return rule;
                }))
            };

            settings.AvatarProfiles.Add(importedProfile);
        }

        foreach (var overrideRule in clonedRules.Where(rule => rule.TriggerType != TwitchTriggerType.ChannelPoints))
        {
            settings.GlobalOverrideRules.Add(overrideRule);
        }
    }

    private TwitchAccountSettings ToAccountSettings(PersistedTwitchAccountMetadata? account, BridgeAccountRole role)
    {
        if (account is null)
        {
            return new TwitchAccountSettings();
        }

        var accessToken = credentialStore.LoadSecret(GetTwitchAccessTokenCredential(role));
        var refreshToken = credentialStore.LoadSecret(GetTwitchRefreshTokenCredential(role));
        if (string.IsNullOrWhiteSpace(accessToken) || string.IsNullOrWhiteSpace(account.UserId))
        {
            return new TwitchAccountSettings();
        }

        return new TwitchAccountSettings
        {
            AccessToken = accessToken,
            RefreshToken = refreshToken,
            UserId = account.UserId ?? string.Empty,
            Login = account.Login ?? string.Empty,
            DisplayName = account.DisplayName ?? string.Empty,
            ProfileImageUrl = account.ProfileImageUrl ?? string.Empty,
            AccessTokenExpiresAt = account.AccessTokenExpiresAt,
            SessionRenewalDueAt = account.SessionRenewalDueAt
                ?? (!string.IsNullOrWhiteSpace(refreshToken) ? DateTimeOffset.UtcNow.AddDays(30) : null),
            Scopes = account.Scopes ?? []
        };
    }

    private static TwitchAccountSettings ToLegacyAccountSettings(PersistedTwitchAccountSettings? account)
    {
        if (account is null)
        {
            return new TwitchAccountSettings();
        }

        var accessToken = Unprotect(account.AccessToken ?? string.Empty);
        var refreshToken = Unprotect(account.RefreshToken ?? string.Empty);

        return new TwitchAccountSettings
        {
            AccessToken = accessToken,
            RefreshToken = refreshToken,
            UserId = account.UserId ?? string.Empty,
            Login = account.Login ?? string.Empty,
            DisplayName = account.DisplayName ?? string.Empty,
            ProfileImageUrl = account.ProfileImageUrl ?? string.Empty,
            AccessTokenExpiresAt = account.AccessTokenExpiresAt,
            SessionRenewalDueAt = account.SessionRenewalDueAt
                ?? (!string.IsNullOrWhiteSpace(refreshToken) ? DateTimeOffset.UtcNow.AddDays(30) : null),
            Scopes = account.Scopes ?? []
        };
    }

    private VrChatAccountSettings ToVrChatAccountSettings(PersistedVrChatAccountMetadata? account)
    {
        if (account is null)
        {
            return new VrChatAccountSettings();
        }

        var authCookie = credentialStore.LoadSecret(VrChatAuthCookieCredential);
        if (string.IsNullOrWhiteSpace(authCookie) || string.IsNullOrWhiteSpace(account.UserId))
        {
            return new VrChatAccountSettings();
        }

        return new VrChatAccountSettings
        {
            AuthCookie = authCookie,
            UserId = account.UserId ?? string.Empty,
            DisplayName = account.DisplayName ?? string.Empty,
            CurrentAvatarId = account.CurrentAvatarId ?? string.Empty
        };
    }

    private static bool HasLegacySecrets(PersistedSecureSettings secure)
    {
        return !string.IsNullOrWhiteSpace(secure.Broadcaster?.AccessToken)
            || !string.IsNullOrWhiteSpace(secure.Broadcaster?.RefreshToken)
            || !string.IsNullOrWhiteSpace(secure.Bot?.AccessToken)
            || !string.IsNullOrWhiteSpace(secure.Bot?.RefreshToken)
            || !string.IsNullOrWhiteSpace(secure.VrChat?.AuthCookie);
    }

    private static PersistedSecureMetadataSettings ToSecureMetadata(PersistedSecureSettings secure)
    {
        return new PersistedSecureMetadataSettings
        {
            Broadcaster = ToPersistedAccountMetadata(secure.Broadcaster),
            Bot = ToPersistedAccountMetadata(secure.Bot),
            VrChat = ToPersistedVrChatMetadata(secure.VrChat)
        };
    }

    private static PersistedTwitchAccountMetadata? ToPersistedAccountMetadata(TwitchAccountSettings account)
    {
        if (string.IsNullOrWhiteSpace(account.UserId))
        {
            return null;
        }

        return new PersistedTwitchAccountMetadata
        {
            UserId = account.UserId,
            Login = account.Login,
            DisplayName = account.DisplayName,
            ProfileImageUrl = account.ProfileImageUrl,
            AccessTokenExpiresAt = account.AccessTokenExpiresAt,
            SessionRenewalDueAt = account.SessionRenewalDueAt,
            Scopes = [.. account.Scopes]
        };
    }

    private static PersistedTwitchAccountMetadata? ToPersistedAccountMetadata(PersistedTwitchAccountSettings? account)
    {
        if (account is null)
        {
            return null;
        }

        return new PersistedTwitchAccountMetadata
        {
            UserId = account.UserId,
            Login = account.Login,
            DisplayName = account.DisplayName,
            ProfileImageUrl = account.ProfileImageUrl,
            AccessTokenExpiresAt = account.AccessTokenExpiresAt,
            SessionRenewalDueAt = account.SessionRenewalDueAt,
            Scopes = account.Scopes
        };
    }

    private static PersistedVrChatAccountMetadata? ToPersistedVrChatMetadata(VrChatAccountSettings account)
    {
        if (string.IsNullOrWhiteSpace(account.UserId))
        {
            return null;
        }

        return new PersistedVrChatAccountMetadata
        {
            UserId = account.UserId,
            DisplayName = account.DisplayName,
            CurrentAvatarId = account.CurrentAvatarId
        };
    }

    private static PersistedVrChatAccountMetadata? ToPersistedVrChatMetadata(PersistedVrChatAccountSettings? account)
    {
        if (account is null)
        {
            return null;
        }

        return new PersistedVrChatAccountMetadata
        {
            UserId = account.UserId,
            DisplayName = account.DisplayName,
            CurrentAvatarId = account.CurrentAvatarId
        };
    }

    private static string Protect(string value)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return string.Empty;
        }

        var bytes = Encoding.UTF8.GetBytes(value);
        var protectedBytes = ProtectedData.Protect(bytes, null, DataProtectionScope.CurrentUser);
        return Convert.ToBase64String(protectedBytes);
    }

    private static byte[] ProtectBytes(string value)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return [];
        }

        var bytes = Encoding.UTF8.GetBytes(value);
        return ProtectedData.Protect(bytes, null, DataProtectionScope.CurrentUser);
    }

    private static string Unprotect(string value)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return string.Empty;
        }

        try
        {
            var bytes = Convert.FromBase64String(value);
            var unprotectedBytes = ProtectedData.Unprotect(bytes, null, DataProtectionScope.CurrentUser);
            return Encoding.UTF8.GetString(unprotectedBytes);
        }
        catch
        {
            return string.Empty;
        }
    }

    private static string UnprotectBytes(byte[] value)
    {
        if (value.Length == 0)
        {
            return string.Empty;
        }

        try
        {
            var unprotectedBytes = ProtectedData.Unprotect(value, null, DataProtectionScope.CurrentUser);
            return Encoding.UTF8.GetString(unprotectedBytes);
        }
        catch
        {
            return string.Empty;
        }
    }

    private static string GetBackupPath(string path) => $"{path}.bak";

    private static async Task SaveTextFileAtomicallyAsync(
        string path,
        string backupPath,
        string contents,
        CancellationToken cancellationToken)
    {
        var bytes = Encoding.UTF8.GetBytes(contents);
        await SaveBytesFileAtomicallyAsync(path, backupPath, bytes, cancellationToken, hiddenResult: false);
    }

    private static async Task SaveBytesFileAtomicallyAsync(
        string path,
        string backupPath,
        byte[] contents,
        CancellationToken cancellationToken,
        bool hiddenResult)
    {
        // Always write settings through a temp file plus replace/backup swap so an app crash
        // or sudden shutdown cannot leave the main settings file half-written on disk.
        var directoryPath = Path.GetDirectoryName(path);
        if (!string.IsNullOrWhiteSpace(directoryPath))
        {
            Directory.CreateDirectory(directoryPath);
        }

        var tempPath = Path.Combine(
            directoryPath ?? string.Empty,
            $"{Path.GetFileName(path)}.{Guid.NewGuid():N}.tmp");

        try
        {
            await using (var stream = new FileStream(
                             tempPath,
                             FileMode.CreateNew,
                             FileAccess.Write,
                             FileShare.None,
                             4096,
                             FileOptions.Asynchronous | FileOptions.WriteThrough))
            {
                await stream.WriteAsync(contents, cancellationToken);
                await stream.FlushAsync(cancellationToken);
            }

            PrepareSecureFileForWrite(path);
            PrepareSecureFileForWrite(backupPath);

            if (File.Exists(path))
            {
                if (File.Exists(backupPath))
                {
                    File.Delete(backupPath);
                }

                File.Replace(tempPath, path, backupPath, ignoreMetadataErrors: true);
            }
            else
            {
                if (File.Exists(backupPath))
                {
                    File.Delete(backupPath);
                }

                File.Move(tempPath, path);
            }

            if (hiddenResult)
            {
                HideSecureFile(path);
                if (File.Exists(backupPath))
                {
                    HideSecureFile(backupPath);
                }
            }
        }
        catch
        {
            if (File.Exists(tempPath))
            {
                File.Delete(tempPath);
            }

            throw;
        }
    }

    private readonly record struct LoadAttemptResult<T>(bool Success, T? Value, string? ErrorMessage);

    private sealed class PersistedProfileSettings
    {
        public AppLanguage Language { get; set; }

        public AppTheme Theme { get; set; }

        public int InterfaceOpacityPercent { get; set; }

        public int ChatTextSize { get; set; }

        public int ChatOpacityPercent { get; set; }

        public bool? ChatShowTimestamps { get; set; }

        public ChatTimestampFormat? ChatTimestampFormat { get; set; }

        public string? ChatFontFamily { get; set; }

        public bool? ChatboxAlwaysOnTop { get; set; }

        public bool? ChatboxSettingsPanelOpen { get; set; }

        public bool? ChatboxOverlayMode { get; set; }

        public bool? ChatboxOscEnabled { get; set; }

        public int? ChatboxOscDelaySeconds { get; set; }

        public bool? ChatboxViewerSoundEnabled { get; set; }

        public bool? SupporterOverrideInfoMessageEnabled { get; set; }

        public bool? ChannelPointRewardTestModeEnabled { get; set; }

        public bool? EmergencyRedeemStopEnabled { get; set; }

        public bool? DesktopModeInputLockEnabled { get; set; }

        public bool? EasterEggsEnabled { get; set; }

        public bool? MainWindowTrayTipShown { get; set; }

        public List<PersistedAvatarTriggerProfile>? AvatarProfiles { get; set; }

        public List<PersistedTriggerRule>? GlobalMovementRules { get; set; }

        public List<PersistedTriggerRule>? GlobalOverrideRules { get; set; }

        public List<PersistedTriggerRule>? Rules { get; set; }
    }

    private sealed class PersistedSecureSettings
    {
        public PersistedTwitchAccountSettings? Broadcaster { get; set; }

        public PersistedTwitchAccountSettings? Bot { get; set; }

        public PersistedVrChatAccountSettings? VrChat { get; set; }
    }

    private sealed class PersistedSecureMetadataSettings
    {
        public PersistedTwitchAccountMetadata? Broadcaster { get; set; }

        public PersistedTwitchAccountMetadata? Bot { get; set; }

        public PersistedVrChatAccountMetadata? VrChat { get; set; }
    }

    private sealed class PersistedLegacySettings
    {
        public PersistedTwitchAccountSettings? Broadcaster { get; set; }

        public PersistedTwitchAccountSettings? Bot { get; set; }

        public List<PersistedTriggerRule>? Rules { get; set; }
    }

    private sealed class PersistedTwitchAccountSettings
    {
        public string? AccessToken { get; set; }

        public string? RefreshToken { get; set; }

        public string? UserId { get; set; }

        public string? Login { get; set; }

        public string? DisplayName { get; set; }

        public string? ProfileImageUrl { get; set; }

        public DateTimeOffset? AccessTokenExpiresAt { get; set; }

        public DateTimeOffset? SessionRenewalDueAt { get; set; }

        public List<string>? Scopes { get; set; }
    }

    private sealed class PersistedTwitchAccountMetadata
    {
        public string? UserId { get; set; }

        public string? Login { get; set; }

        public string? DisplayName { get; set; }

        public string? ProfileImageUrl { get; set; }

        public DateTimeOffset? AccessTokenExpiresAt { get; set; }

        public DateTimeOffset? SessionRenewalDueAt { get; set; }

        public List<string>? Scopes { get; set; }
    }

    private sealed class PersistedVrChatAccountSettings
    {
        public string? AuthCookie { get; set; }

        public string? UserId { get; set; }

        public string? DisplayName { get; set; }

        public string? CurrentAvatarId { get; set; }
    }

    private sealed class PersistedVrChatAccountMetadata
    {
        public string? UserId { get; set; }

        public string? DisplayName { get; set; }

        public string? CurrentAvatarId { get; set; }
    }

    private sealed class PersistedVrChatAvatarCache
    {
        public string? UserId { get; set; }

        public DateTimeOffset SavedAt { get; set; }

        public List<PersistedVrChatAvatar>? Avatars { get; set; }
    }

    private sealed class PersistedVrChatOscParameterCache
    {
        public string? UserId { get; set; }

        public DateTimeOffset SavedAt { get; set; }

        public List<PersistedVrChatOscParameterCacheEntry>? Avatars { get; set; }
    }

    private sealed class PersistedVrChatOscParameterCacheEntry
    {
        public string? AvatarId { get; set; }

        public DateTimeOffset SavedAt { get; set; }

        public List<PersistedVrChatOscParameter>? Parameters { get; set; }
    }

    private sealed class PersistedVrChatOscParameter
    {
        public string? Address { get; set; }

        public string? Name { get; set; }

        public OscParameterType ParameterType { get; set; }
    }

    private sealed class PersistedVrChatAvatar
    {
        public string? Id { get; set; }

        public string? Name { get; set; }

        public string? SourceLabel { get; set; }

        public bool IsCurrentAvatar { get; set; }
    }

    private sealed class PersistedAvatarTriggerProfile
    {
        public Guid Id { get; set; }

        public bool IsEnabled { get; set; }

        public bool IsMasterProfile { get; set; }

        public bool IsRewardTestOverrideEnabled { get; set; }

        public string? Name { get; set; }

        public string? AvatarId { get; set; }

        public string? AvatarName { get; set; }

        public List<PersistedTriggerRule>? ChannelPointRules { get; set; }
    }

    private sealed class PersistedTriggerRule
    {
        public Guid Id { get; set; }

        public bool IsEnabled { get; set; }

        public string? Name { get; set; }

        public TwitchTriggerType TriggerType { get; set; }

        public string? MatchText { get; set; }

        public string? ChannelPointRewardId { get; set; }

        public string? ChannelPointRewardTitle { get; set; }

        public int ChannelPointRewardCost { get; set; }

        public string? ManagedRewardReadyColor { get; set; }

        public string? ManagedRewardCooldownColor { get; set; }

        public bool ChatCommandEnabled { get; set; }

        public string? ChatCommandText { get; set; }

        public ChatCommandPermission ChatCommandPermission { get; set; }

        public int MinimumAmount { get; set; }

        public bool AmountScaledDurationEnabled { get; set; }

        public int AmountUnitsPerDuration { get; set; }

        public int SecondsPerAmountUnit { get; set; }

        public int BitsAmountUnitsPerDuration { get; set; }

        public int BitsSecondsPerAmountUnit { get; set; }

        public int SubscriptionsAmountUnitsPerDuration { get; set; }

        public int SubscriptionsSecondsPerAmountUnit { get; set; }

        public bool MaxAccumulatedDurationEnabled { get; set; }

        public int MaxAccumulatedDurationSeconds { get; set; }

        public OscActionType ActionType { get; set; }

        public PlayerMovementDirection MovementDirection { get; set; }

        public string? ParameterName { get; set; }

        public OscParameterType ParameterType { get; set; }

        public IntZeroDurationMode IntZeroDurationMode { get; set; }

        public string? ParameterValue { get; set; }

        public string? AvatarChangeTargetId { get; set; }

        public string? AvatarTargetName { get; set; }

        public string? ResetValue { get; set; }

        public string? AvatarChangeResetId { get; set; }

        public string? ResetAvatarName { get; set; }

        public List<string>? AvatarRouletAvatarIds { get; set; }

        public List<string>? AvatarRouletAvatarNames { get; set; }

        public int RangeMinimum { get; set; }

        public int RangeMaximum { get; set; }

        public int DurationSeconds { get; set; }

        public int CooldownSeconds { get; set; }

        public List<Guid>? TemporarilyDisabledRuleIds { get; set; }

        public string? BotMessageTemplate { get; set; }
    }
}
