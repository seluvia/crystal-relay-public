using System.Collections.ObjectModel;
using System.Diagnostics;
using System.IO;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
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
        WriteIndented = true,
        Converters = { new CashPaymentRuleJsonConverter() }
    };

    private const string BroadcasterAccessTokenCredential = "CrystalRelay:Twitch:Broadcaster:AccessToken";
    private const string BroadcasterRefreshTokenCredential = "CrystalRelay:Twitch:Broadcaster:RefreshToken";
    private const string BotAccessTokenCredential = "CrystalRelay:Twitch:Bot:AccessToken";
    private const string BotRefreshTokenCredential = "CrystalRelay:Twitch:Bot:RefreshToken";
    private const string VrChatAuthCookieCredential = "CrystalRelay:VRChat:AuthCookie";
    private const string WorldCommandBlacklistGitHubTokenCredential = "CrystalRelay:WorldCommandBlacklist:GitHubToken";
    private const string StreamElementsJwtCredential = "CrystalRelay:CashPayments:StreamElements:JwtToken";
    private const string StreamlabsAccessTokenCredential = "CrystalRelay:CashPayments:Streamlabs:AccessToken";
    private const string KoFiVerificationTokenCredential = "CrystalRelay:CashPayments:KoFi:VerificationToken";
    private const string KoFiRelayClientSecretCredential = "CrystalRelay:CashPayments:KoFi:RelayClientSecret";
    internal static readonly string[] SavedSecretCredentialTargets =
    [
        BroadcasterAccessTokenCredential,
        BroadcasterRefreshTokenCredential,
        BotAccessTokenCredential,
        BotRefreshTokenCredential,
        VrChatAuthCookieCredential,
        StreamElementsJwtCredential,
        StreamlabsAccessTokenCredential,
        KoFiVerificationTokenCredential,
        KoFiRelayClientSecretCredential
    ];

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
    private readonly Dictionary<string, string> lastSavedSecretsByTarget = new(StringComparer.Ordinal);

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
                avatar.IsCurrentAvatar,
                avatar.ThumbnailUrl))
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
                IsCurrentAvatar = avatar.IsCurrentAvatar,
                ThumbnailUrl = avatar.ThumbnailUrl
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
            settings.ChatboxXsOverlayCompatibilityMode = profile.ChatboxXsOverlayCompatibilityMode ?? settings.ChatboxXsOverlayCompatibilityMode;
            settings.ChatboxOscEnabled = profile.ChatboxOscEnabled ?? settings.ChatboxOscEnabled;
            settings.ChatboxOscDelaySeconds = profile.ChatboxOscDelaySeconds is >= 1 and <= 6
                ? profile.ChatboxOscDelaySeconds.Value
                : settings.ChatboxOscDelaySeconds;
            settings.ChatboxViewerSoundEnabled = profile.ChatboxViewerSoundEnabled ?? settings.ChatboxViewerSoundEnabled;
            settings.CustomBlockedWords = profile.CustomBlockedWords is { Count: > 0 }
                ? new ObservableCollection<string>(profile.CustomBlockedWords)
                : [];
            settings.SuppressedBlockedWords = profile.SuppressedBlockedWords is { Count: > 0 }
                ? new ObservableCollection<string>(profile.SuppressedBlockedWords)
                : [];
            settings.UseBroadcasterAsBotSender = profile.UseBroadcasterAsBotSender ?? settings.UseBroadcasterAsBotSender;
            settings.SupporterOverrideInfoMessageEnabled = profile.SupporterOverrideInfoMessageEnabled ?? settings.SupporterOverrideInfoMessageEnabled;
            settings.TriggerInfoAnnouncementsEnabled = profile.TriggerInfoAnnouncementsEnabled ?? settings.TriggerInfoAnnouncementsEnabled;
            settings.TriggerInfoAnnouncementIntervalMinutes = profile.TriggerInfoAnnouncementIntervalMinutes is > 0
                ? profile.TriggerInfoAnnouncementIntervalMinutes.Value
                : settings.TriggerInfoAnnouncementIntervalMinutes;
            settings.TriggerInfoCommandEnabled = profile.TriggerInfoCommandEnabled ?? settings.TriggerInfoCommandEnabled;
            settings.TriggerInfoCommandText = string.IsNullOrWhiteSpace(profile.TriggerInfoCommandText)
                ? settings.TriggerInfoCommandText
                : profile.TriggerInfoCommandText;
            settings.TriggerInfoCommandCooldownSeconds = profile.TriggerInfoCommandCooldownSeconds is >= 0
                ? profile.TriggerInfoCommandCooldownSeconds.Value
                : settings.TriggerInfoCommandCooldownSeconds;
            settings.TriggerInfoCommandPermission = profile.TriggerInfoCommandPermission is not null
                && Enum.IsDefined(profile.TriggerInfoCommandPermission.Value)
                    ? profile.TriggerInfoCommandPermission.Value
                    : settings.TriggerInfoCommandPermission;
            settings.UseManagedRewardTitlePrefix = profile.UseManagedRewardTitlePrefix ?? settings.UseManagedRewardTitlePrefix;
            settings.WorldCommandEnabled = profile.WorldCommandEnabled ?? settings.WorldCommandEnabled;
            settings.WorldCommandText = string.IsNullOrWhiteSpace(profile.WorldCommandText)
                ? settings.WorldCommandText
                : profile.WorldCommandText;
            settings.WorldCommandCooldownSeconds = profile.WorldCommandCooldownSeconds is >= 0
                ? profile.WorldCommandCooldownSeconds.Value
                : settings.WorldCommandCooldownSeconds;
            settings.WorldCommandPermission = profile.WorldCommandPermission is not null
                && Enum.IsDefined(profile.WorldCommandPermission.Value)
                    ? profile.WorldCommandPermission.Value
                    : settings.WorldCommandPermission;
            settings.ChannelPointRewardTestModeEnabled = profile.ChannelPointRewardTestModeEnabled ?? settings.ChannelPointRewardTestModeEnabled;
            settings.AvatarChangeCooldownOnlyModeEnabled = profile.AvatarChangeCooldownOnlyModeEnabled ?? settings.AvatarChangeCooldownOnlyModeEnabled;
            settings.PermanentSwapModeEnabled = profile.PermanentSwapModeEnabled ?? settings.PermanentSwapModeEnabled;
            settings.AvatarSwapManagerUseFullRuleEditor = profile.AvatarSwapManagerUseFullRuleEditor ?? settings.AvatarSwapManagerUseFullRuleEditor;
            settings.AvatarSwapMigrationNoticeShown = profile.AvatarSwapMigrationNoticeShown ?? settings.AvatarSwapMigrationNoticeShown;
            settings.EmergencyRedeemStopEnabled = profile.EmergencyRedeemStopEnabled ?? settings.EmergencyRedeemStopEnabled;
            settings.DesktopModeInputLockEnabled = profile.DesktopModeInputLockEnabled ?? settings.DesktopModeInputLockEnabled;
            settings.RestartVrChatInDesktopMode = profile.RestartVrChatInDesktopMode ?? settings.RestartVrChatInDesktopMode;
            settings.LiveFeedbackHeartbeatEnabled = true;
            settings.BetaApplicationUpdatesEnabled = profile.BetaApplicationUpdatesEnabled ?? settings.BetaApplicationUpdatesEnabled;
            settings.EasterEggsEnabled = profile.EasterEggsEnabled ?? settings.EasterEggsEnabled;
            settings.MainWindowTrayTipShown = profile.MainWindowTrayTipShown ?? settings.MainWindowTrayTipShown;
            settings.IgnoredUpdateVersion = profile.IgnoredUpdateVersion ?? settings.IgnoredUpdateVersion;
            settings.IgnoredBetaUpdateBaseVersion = profile.IgnoredBetaUpdateBaseVersion ?? settings.IgnoredBetaUpdateBaseVersion;
            settings.PauseCommandEnabled = profile.PauseCommandEnabled ?? settings.PauseCommandEnabled;
            settings.PauseCommandText = string.IsNullOrWhiteSpace(profile.PauseCommandText)
                ? settings.PauseCommandText
                : profile.PauseCommandText;
            settings.RedeemGroupCommandEnabled = profile.RedeemGroupCommandEnabled ?? settings.RedeemGroupCommandEnabled;
            settings.RedeemControlCommandEnabled = profile.RedeemControlCommandEnabled ?? settings.RedeemControlCommandEnabled;
            settings.RedeemGroups = new ObservableCollection<RedeemGroup>((profile.RedeemGroups ?? []).Select(ToRedeemGroup));
            settings.CustomTheme = profile.CustomTheme is null
                ? settings.CustomTheme
                : ToCustomThemeSettings(profile.CustomTheme);
            settings.AvatarProfiles = new ObservableCollection<AvatarTriggerProfile>((profile.AvatarProfiles ?? []).Select(ToAvatarProfile));
            settings.GlobalMovementRules = new ObservableCollection<TriggerRule>((profile.GlobalMovementRules ?? []).Select(ToRule));
            settings.MovementRedeemSets = BuildMovementRedeemSets(profile, settings.GlobalMovementRules);
            settings.GlobalMovementRules = new ObservableCollection<TriggerRule>(settings.MovementRedeemSets.SelectMany(set => set.MovementRules));
            settings.GlobalOverrideRules = new ObservableCollection<TriggerRule>((profile.GlobalOverrideRules ?? []).Select(ToRule));
            settings.UniversalTriggers = new ObservableCollection<UniversalTriggerRule>((profile.UniversalTriggers ?? []).Select(ToUniversalTriggerRule));
            settings.UniversalTriggersChatCollapsed = profile.UniversalTriggersChatCollapsed ?? settings.UniversalTriggersChatCollapsed;
            settings.UniversalTriggersRewardCollapsed = profile.UniversalTriggersRewardCollapsed ?? settings.UniversalTriggersRewardCollapsed;
            settings.UniversalTriggersBitsCollapsed = profile.UniversalTriggersBitsCollapsed ?? settings.UniversalTriggersBitsCollapsed;
            settings.UniversalTriggersSubsCollapsed = profile.UniversalTriggersSubsCollapsed ?? settings.UniversalTriggersSubsCollapsed;
            settings.UniversalTriggersFollowsCollapsed = profile.UniversalTriggersFollowsCollapsed ?? settings.UniversalTriggersFollowsCollapsed;
            settings.AvatarScaleSets = BuildAvatarScaleSets(profile);
            settings.AvatarScaleRules = [];
            settings.AvatarScaleMasterReward = profile.AvatarScaleMasterReward is null
                ? settings.AvatarScaleMasterReward
                : ToAvatarScaleMasterReward(profile.AvatarScaleMasterReward);
            settings.PowerUpRules = new ObservableCollection<PowerUpRule>(
                (profile.PowerUpRules ?? []).Select(ToPowerUpRule));
            settings.RewardFireSale = profile.RewardFireSale is null
                ? settings.RewardFireSale
                : ToRewardFireSaleSettings(profile.RewardFireSale);
            settings.CashPayments = profile.CashPayments is null
                ? settings.CashPayments
                : ToCashPaymentConnectionSettings(profile.CashPayments);
            settings.CashPaymentRules = new ObservableCollection<CashPaymentRule>(
                (profile.CashPaymentRules ?? []).Select(ToCashPaymentRule));
            settings.Rules = new ObservableCollection<TriggerRule>((profile.Rules ?? []).Select(ToRule));
            settings.MasterAvatarSwapReturnId = profile.MasterAvatarSwapReturnId;
            settings.MasterAvatarSwapReturnName = profile.MasterAvatarSwapReturnName;
            settings.AvatarChangeToAvatarSwapMigrationVersion = profile.AvatarChangeToAvatarSwapMigrationVersion;
            settings.AvatarSwapProfiles = new ObservableCollection<AvatarSwapProfile>(
                (profile.AvatarSwapProfiles ?? []).Select(ToAvatarSwapProfile));
            settings.AvatarRouletteProfiles = new ObservableCollection<AvatarRouletteProfile>(
                (profile.AvatarRouletteProfiles ?? new()).Select(ToAvatarRouletteProfile));
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
            settings.WorldCommandBlacklist = ToWorldCommandBlacklistSettings(secureMetadata.WorldCommandBlacklist);
        }

        LoadCashPaymentSecrets(settings.CashPayments);

        if (settings.AvatarProfiles.Count == 0
            && settings.GlobalMovementRules.Count == 0
            && settings.MovementRedeemSets.Count == 0
            && settings.GlobalOverrideRules.Count == 0
            && settings.Rules.Count > 0)
        {
            MigrateLegacyRulesIntoNewCollections(settings, settings.Rules);
        }

        AvatarSwapMigrationService.Migrate(settings, profile?.AvatarSwapProfiles);

        if (profile is not null)
        {
            settings.AvatarScaleSafety = ToAvatarScaleSafety(
                profile.AvatarScaleSafety,
                EnumerateAvatarScaleSafetyMigrationRules(settings));
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
            ChatboxXsOverlayCompatibilityMode = settings.ChatboxXsOverlayCompatibilityMode,
            ChatboxOscEnabled = settings.ChatboxOscEnabled,
            ChatboxOscDelaySeconds = settings.ChatboxOscDelaySeconds,
            ChatboxViewerSoundEnabled = settings.ChatboxViewerSoundEnabled,
            UseBroadcasterAsBotSender = settings.UseBroadcasterAsBotSender,
            SupporterOverrideInfoMessageEnabled = settings.SupporterOverrideInfoMessageEnabled,
            TriggerInfoAnnouncementsEnabled = settings.TriggerInfoAnnouncementsEnabled,
            TriggerInfoAnnouncementIntervalMinutes = settings.TriggerInfoAnnouncementIntervalMinutes,
            TriggerInfoCommandEnabled = settings.TriggerInfoCommandEnabled,
            TriggerInfoCommandText = settings.TriggerInfoCommandText,
            TriggerInfoCommandCooldownSeconds = settings.TriggerInfoCommandCooldownSeconds,
            TriggerInfoCommandPermission = settings.TriggerInfoCommandPermission,
            UseManagedRewardTitlePrefix = settings.UseManagedRewardTitlePrefix,
            WorldCommandEnabled = settings.WorldCommandEnabled,
            WorldCommandText = settings.WorldCommandText,
            WorldCommandCooldownSeconds = settings.WorldCommandCooldownSeconds,
            WorldCommandPermission = settings.WorldCommandPermission,
            ChannelPointRewardTestModeEnabled = settings.ChannelPointRewardTestModeEnabled,
            AvatarChangeCooldownOnlyModeEnabled = settings.AvatarChangeCooldownOnlyModeEnabled,
            PermanentSwapModeEnabled = settings.PermanentSwapModeEnabled,
            AvatarSwapManagerUseFullRuleEditor = settings.AvatarSwapManagerUseFullRuleEditor,
            AvatarSwapMigrationNoticeShown = settings.AvatarSwapMigrationNoticeShown,
            EmergencyRedeemStopEnabled = settings.EmergencyRedeemStopEnabled,
            DesktopModeInputLockEnabled = settings.DesktopModeInputLockEnabled,
            RestartVrChatInDesktopMode = settings.RestartVrChatInDesktopMode,
            LiveFeedbackHeartbeatEnabled = settings.LiveFeedbackHeartbeatEnabled,
            BetaApplicationUpdatesEnabled = settings.BetaApplicationUpdatesEnabled,
            EasterEggsEnabled = settings.EasterEggsEnabled,
            MainWindowTrayTipShown = settings.MainWindowTrayTipShown,
            IgnoredUpdateVersion = settings.IgnoredUpdateVersion,
            IgnoredBetaUpdateBaseVersion = settings.IgnoredBetaUpdateBaseVersion,
            PauseCommandEnabled = settings.PauseCommandEnabled,
            PauseCommandText = settings.PauseCommandText,
            RedeemGroupCommandEnabled = settings.RedeemGroupCommandEnabled,
            RedeemControlCommandEnabled = settings.RedeemControlCommandEnabled,
            RedeemGroups = [.. settings.RedeemGroups.Select(ToPersistedRedeemGroup)],
            CustomTheme = ToPersistedCustomThemeSettings(settings.CustomTheme),
            AvatarProfiles = [.. settings.AvatarProfiles.Select(ToPersistedAvatarProfile)],
            MovementRedeemSets = [.. settings.MovementRedeemSets.Select(ToPersistedMovementRedeemSet)],
            GlobalMovementRules = [.. settings.MovementRedeemSets.SelectMany(set => set.MovementRules).Select(ToPersistedRule)],
            GlobalOverrideRules = [.. settings.GlobalOverrideRules.Select(ToPersistedRule)],
            UniversalTriggers = [.. settings.UniversalTriggers.Select(ToPersistedUniversalTriggerRule)],
            UniversalTriggersChatCollapsed = settings.UniversalTriggersChatCollapsed,
            UniversalTriggersRewardCollapsed = settings.UniversalTriggersRewardCollapsed,
            UniversalTriggersBitsCollapsed = settings.UniversalTriggersBitsCollapsed,
            UniversalTriggersSubsCollapsed = settings.UniversalTriggersSubsCollapsed,
            UniversalTriggersFollowsCollapsed = settings.UniversalTriggersFollowsCollapsed,
            AvatarScaleSets = [.. settings.AvatarScaleSets.Select(ToPersistedAvatarScaleSet)],
            AvatarScaleMasterReward = ToPersistedAvatarScaleMasterReward(settings.AvatarScaleMasterReward),
            AvatarScaleSafety = ToPersistedAvatarScaleSafety(settings.AvatarScaleSafety),
            PowerUpRules = [.. settings.PowerUpRules.Select(ToPersistedPowerUpRule)],
            RewardFireSale = ToPersistedRewardFireSaleSettings(settings.RewardFireSale),
            CashPayments = ToPersistedCashPaymentConnectionSettings(settings.CashPayments),
            CashPaymentRules = [.. settings.CashPaymentRules.Select(ToPersistedCashPaymentRule)],
            MasterAvatarSwapReturnId = settings.MasterAvatarSwapReturnId,
            MasterAvatarSwapReturnName = settings.MasterAvatarSwapReturnName,
            AvatarChangeToAvatarSwapMigrationVersion = settings.AvatarChangeToAvatarSwapMigrationVersion,
            AvatarSwapProfiles = [.. settings.AvatarSwapProfiles.Select(ToPersistedAvatarSwapProfile)],
            AvatarRouletteProfiles = [.. settings.AvatarRouletteProfiles.Select(ToPersistedAvatarRouletteProfile)],
            CustomBlockedWords = settings.CustomBlockedWords?.ToList(),
            SuppressedBlockedWords = settings.SuppressedBlockedWords?.ToList(),
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
        SaveSecretIfChanged(VrChatAuthCookieCredential, settings.VrChat.AuthCookie);
        DeleteLegacyWorldCommandBlacklistSecret();
        SaveCashPaymentSecrets(settings.CashPayments);
    }

    private void SaveTwitchSecrets(TwitchAccountSettings account, BridgeAccountRole role)
    {
        SaveSecretIfChanged(GetTwitchAccessTokenCredential(role), account.AccessToken);
        SaveSecretIfChanged(GetTwitchRefreshTokenCredential(role), account.RefreshToken);
    }

    private void SaveCashPaymentSecrets(CashPaymentConnectionSettings settings)
    {
        SaveSecretIfChanged(StreamElementsJwtCredential, settings.StreamElementsJwtToken);
        SaveSecretIfChanged(StreamlabsAccessTokenCredential, settings.StreamlabsAccessToken);
        SaveSecretIfChanged(KoFiVerificationTokenCredential, settings.KoFiVerificationToken);
        SaveSecretIfChanged(KoFiRelayClientSecretCredential, settings.KoFiRelayClientSecret);
    }

    private void LoadCashPaymentSecrets(CashPaymentConnectionSettings settings)
    {
        settings.StreamElementsJwtToken = credentialStore.LoadSecret(StreamElementsJwtCredential);
        settings.StreamlabsAccessToken = credentialStore.LoadSecret(StreamlabsAccessTokenCredential);
        settings.KoFiVerificationToken = credentialStore.LoadSecret(KoFiVerificationTokenCredential);
        settings.KoFiRelayClientSecret = credentialStore.LoadSecret(KoFiRelayClientSecretCredential);
    }

    private void SaveSecretIfChanged(string targetName, string? value)
    {
        var normalizedValue = value ?? string.Empty;
        if (!lastSavedSecretsByTarget.TryGetValue(targetName, out var previousValue))
        {
            try
            {
                previousValue = credentialStore.LoadSecret(targetName);
            }
            catch
            {
                previousValue = "\u0000";
            }
        }

        if (string.Equals(previousValue, normalizedValue, StringComparison.Ordinal))
        {
            lastSavedSecretsByTarget[targetName] = normalizedValue;
            return;
        }

        credentialStore.SaveSecret(targetName, normalizedValue);
        lastSavedSecretsByTarget[targetName] = normalizedValue;
    }

    private void DeleteLegacyWorldCommandBlacklistSecret()
    {
        try
        {
            credentialStore.DeleteSecret(WorldCommandBlacklistGitHubTokenCredential);
            lastSavedSecretsByTarget.Remove(WorldCommandBlacklistGitHubTokenCredential);
        }
        catch
        {
            // Cleanup should not block saving unrelated app settings.
        }
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
                SaveSecretIfChanged(VrChatAuthCookieCredential, authCookie);
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

        SaveSecretIfChanged(GetTwitchAccessTokenCredential(role), accessToken);
        SaveSecretIfChanged(GetTwitchRefreshTokenCredential(role), refreshToken);
        return true;
    }

    private async Task SaveSecureMetadataAsync(AppSettings settings, CancellationToken cancellationToken)
    {
        var secureMetadata = new PersistedSecureMetadataSettings
        {
            Broadcaster = ToPersistedAccountMetadata(settings.Broadcaster),
            Bot = ToPersistedAccountMetadata(settings.Bot),
            VrChat = ToPersistedVrChatMetadata(settings.VrChat),
            WorldCommandBlacklist = ToPersistedWorldCommandBlacklist(settings.WorldCommandBlacklist)
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
            MovementRedeemSets = new ObservableCollection<MovementRedeemSet>(),
            GlobalOverrideRules = new ObservableCollection<TriggerRule>(),
            UniversalTriggers = new ObservableCollection<UniversalTriggerRule>(),
            AvatarScaleSets = new ObservableCollection<AvatarScaleSet>(),
            CashPayments = new CashPaymentConnectionSettings(),
            CashPaymentRules = new ObservableCollection<CashPaymentRule>(),
            Rules = new ObservableCollection<TriggerRule>(),
            AvatarSwapProfiles = new ObservableCollection<AvatarSwapProfile>(),
            AvatarRouletteProfiles = new ObservableCollection<AvatarRouletteProfile>()
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
            SetTriggerMasterRewardId = profile.SetTriggerMasterRewardId,
            SetTriggerMasterRewardTitle = profile.SetTriggerMasterRewardTitle,
            SetTriggerMasterRewardDescription = profile.SetTriggerMasterRewardDescription,
            SetTriggerMasterRewardCost = profile.SetTriggerMasterRewardCost,
            SetTriggerMasterRewardSyncMode = profile.SetTriggerMasterRewardSyncMode,
            SetTriggerMasterRewardCooldownSeconds = profile.SetTriggerMasterRewardCooldownSeconds,
            SetTriggerMasterRewardReadyColor = profile.SetTriggerMasterRewardReadyColor,
            SetTriggerMasterRewardCooldownColor = profile.SetTriggerMasterRewardCooldownColor,
            DeleteSetTriggerMasterRewardWhenInactive = profile.DeleteSetTriggerMasterRewardWhenInactive,
            UseSharedNumberedOutfitReward = profile.UseSharedNumberedOutfitReward,
            PostOutfitChoiceListToTwitchChat = profile.PostOutfitChoiceListToTwitchChat,
ChannelPointRules = [.. profile.ChannelPointRules.Select(ToPersistedRule)],
            UseWardrobeMode = profile.UseWardrobeMode,
            WardrobeCooldownSeconds = profile.WardrobeCooldownSeconds,
            WardrobeOutfits = [.. profile.WardrobeOutfits.Select(ToPersistedWardrobeOutfit)],
            UseWardrobeMasterReward = profile.UseWardrobeMasterReward,
            WardrobeMasterRewardId = profile.WardrobeMasterRewardId,
            WardrobeMasterRewardTitle = profile.WardrobeMasterRewardTitle,
            WardrobeMasterRewardCost = profile.WardrobeMasterRewardCost,
            WardrobeMasterRewardSyncMode = profile.WardrobeMasterRewardSyncMode,
            WardrobeMasterRewardCooldownSeconds = profile.WardrobeMasterRewardCooldownSeconds,
            WardrobeMasterRewardReadyColor = profile.WardrobeMasterRewardReadyColor,
            WardrobeMasterRewardCooldownColor = profile.WardrobeMasterRewardCooldownColor,
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
            SetTriggerMasterRewardId = profile.SetTriggerMasterRewardId ?? string.Empty,
            SetTriggerMasterRewardTitle = profile.SetTriggerMasterRewardTitle ?? string.Empty,
            SetTriggerMasterRewardDescription = profile.SetTriggerMasterRewardDescription ?? string.Empty,
            SetTriggerMasterRewardCost = profile.SetTriggerMasterRewardCost <= 0 ? 100 : profile.SetTriggerMasterRewardCost,
            SetTriggerMasterRewardSyncMode = Enum.IsDefined(profile.SetTriggerMasterRewardSyncMode)
                ? profile.SetTriggerMasterRewardSyncMode
                : TwitchRewardSyncMode.CreateOrManage,
            SetTriggerMasterRewardCooldownSeconds = Math.Max(0, profile.SetTriggerMasterRewardCooldownSeconds),
            SetTriggerMasterRewardReadyColor = ManagedRewardPresentation.NormalizeReadyBackgroundColor(profile.SetTriggerMasterRewardReadyColor),
            SetTriggerMasterRewardCooldownColor = ManagedRewardPresentation.NormalizeCooldownBackgroundColor(profile.SetTriggerMasterRewardCooldownColor),
            DeleteSetTriggerMasterRewardWhenInactive = profile.DeleteSetTriggerMasterRewardWhenInactive,
            UseSharedNumberedOutfitReward = profile.UseSharedNumberedOutfitReward ?? true,
            PostOutfitChoiceListToTwitchChat = profile.PostOutfitChoiceListToTwitchChat,
            ChannelPointRules = new ObservableCollection<TriggerRule>((profile.ChannelPointRules ?? [])
                .Select(ToRule)
                .Select(rule =>
                {
                    rule.TriggerType = TwitchTriggerType.ChannelPoints;
                    return rule;
                })),
            UseWardrobeMode = profile.UseWardrobeMode,
            WardrobeCooldownSeconds = Math.Max(0, profile.WardrobeCooldownSeconds),
            WardrobeOutfits = new ObservableCollection<WardrobeOutfit>((profile.WardrobeOutfits ?? []).Select(ToWardrobeOutfit)),
            UseWardrobeMasterReward = profile.UseWardrobeMasterReward,
            WardrobeMasterRewardId = profile.WardrobeMasterRewardId ?? string.Empty,
            WardrobeMasterRewardTitle = profile.WardrobeMasterRewardTitle ?? string.Empty,
            WardrobeMasterRewardCost = profile.WardrobeMasterRewardCost <= 0 ? 100 : profile.WardrobeMasterRewardCost,
            WardrobeMasterRewardSyncMode = Enum.IsDefined(profile.WardrobeMasterRewardSyncMode)
                ? profile.WardrobeMasterRewardSyncMode
                : TwitchRewardSyncMode.CreateOrManage,
            WardrobeMasterRewardCooldownSeconds = Math.Max(0, profile.WardrobeMasterRewardCooldownSeconds),
            WardrobeMasterRewardReadyColor = ManagedRewardPresentation.NormalizeReadyBackgroundColor(profile.WardrobeMasterRewardReadyColor),
            WardrobeMasterRewardCooldownColor = ManagedRewardPresentation.NormalizeCooldownBackgroundColor(profile.WardrobeMasterRewardCooldownColor),
        };
    }

    internal static PersistedTriggerRule ToPersistedRule(TriggerRule rule)
    {
        return new PersistedTriggerRule
        {
            Id = rule.Id,
            IsEnabled = rule.IsEnabled,
            Name = rule.Name,
            TriggerType = rule.TriggerType,
            ChannelPointRewardId = rule.ChannelPointRewardId,
            ChannelPointRewardTitle = rule.ChannelPointRewardTitle,
            ChannelPointRewardDescription = rule.ChannelPointRewardDescription,
            ChannelPointRewardCost = rule.ChannelPointRewardCost,
            RewardSyncMode = rule.RewardSyncMode,
            ManagedRewardReadyColor = rule.ManagedRewardReadyColor,
            ManagedRewardCooldownColor = rule.ManagedRewardCooldownColor,
            DeleteManagedRewardWhenInactive = rule.DeleteManagedRewardWhenInactive,
            ChatCommandEnabled = rule.ChatCommandEnabled,
            ChatCommandText = rule.ChatCommandText,
            ChatCommandPermission = rule.ChatCommandPermission,
            MinimumAmount = rule.MinimumAmount,
            AmountScaledDurationEnabled = rule.AmountScaledDurationEnabled,
            AddBitsToSwapTime = rule.AddBitsToSwapTime,
            AmountUnitsPerDuration = rule.AmountUnitsPerDuration,
            SecondsPerAmountUnit = rule.SecondsPerAmountUnit,
            BitsAmountUnitsPerDuration = rule.BitsAmountUnitsPerDuration,
            BitsSecondsPerAmountUnit = rule.BitsSecondsPerAmountUnit,
            SubscriptionsAmountUnitsPerDuration = rule.SubscriptionsAmountUnitsPerDuration,
            SubscriptionsSecondsPerAmountUnit = rule.SubscriptionsSecondsPerAmountUnit,
            SubscriptionTier1SecondsPerSub = rule.SubscriptionTier1SecondsPerSub,
            SubscriptionTier2SecondsPerSub = rule.SubscriptionTier2SecondsPerSub,
            SubscriptionTier3SecondsPerSub = rule.SubscriptionTier3SecondsPerSub,
            SubscriptionTier1Enabled = rule.SubscriptionTier1Enabled,
            SubscriptionTier2Enabled = rule.SubscriptionTier2Enabled,
            SubscriptionTier3Enabled = rule.SubscriptionTier3Enabled,
            MaxAccumulatedDurationEnabled = rule.MaxAccumulatedDurationEnabled,
            MaxAccumulatedDurationSeconds = rule.MaxAccumulatedDurationSeconds,
            ExtendCurrentActivity = rule.ExtendCurrentActivity,
            ExtendSeconds = rule.ExtendSeconds,
            ActionType = rule.ActionType,
            MovementDirection = rule.MovementDirection,
            ParameterName = rule.ParameterName,
            ParameterType = rule.ParameterType,
            IntZeroDurationMode = rule.IntZeroDurationMode,
            ParameterValue = rule.ParameterValue,
            FloatValueMode = rule.FloatValueMode,
            FloatTransitionInSeconds = rule.FloatTransitionInSeconds,
            FloatTransitionOutSeconds = rule.FloatTransitionOutSeconds,
            FloatActionMode = rule.FloatActionMode,
            FloatRangeMin = rule.FloatRangeMin,
            FloatRangeMax = rule.FloatRangeMax,
            FloatCycleStep = rule.FloatCycleStep,
            FloatAddAmount = rule.FloatAddAmount,
            FloatSubtractAmount = rule.FloatSubtractAmount,
            FloatAddSubtractAmount = rule.FloatAddSubtractAmount,
            FloatMultiplyFactor = rule.FloatMultiplyFactor,
            FloatToggleOnValue = rule.FloatToggleOnValue,
            FloatToggleOffValue = rule.FloatToggleOffValue,
            FloatGlitchyIntervalMs = rule.FloatGlitchyIntervalMs,
            FloatPulseSeconds = rule.FloatPulseSeconds,
            FloatClampMode = rule.FloatClampMode,
            HideRewardWhenFloatMaxReached = rule.HideRewardWhenFloatMaxReached,
            HideRewardWhenFloatMinReached = rule.HideRewardWhenFloatMinReached,
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
            SharedRewardChoiceEnabled = rule.SharedRewardChoiceEnabled,
            SharedRewardChoiceNumber = rule.SharedRewardChoiceNumber,
            SharedRewardHelpText = rule.SharedRewardHelpText,
            SupporterKeywordText = rule.SupporterKeywordText,
            BitsKeywordEnabled = rule.BitsKeywordEnabled,
            ActiveFloatBoostRewardOwnerId = rule.ActiveFloatBoostRewardOwnerId,
            ActiveFloatBoostRewardEnabled = rule.ActiveFloatBoostRewardEnabled,
            ActiveFloatBoostRewardId = rule.ActiveFloatBoostRewardId,
            ActiveFloatBoostRewardTitle = rule.ActiveFloatBoostRewardTitle,
            ActiveFloatBoostRewardDescription = rule.ActiveFloatBoostRewardDescription,
            ActiveFloatBoostRewardCost = rule.ActiveFloatBoostRewardCost,
            ActiveFloatBoostRewardCooldownSeconds = rule.ActiveFloatBoostRewardCooldownSeconds,
            ActiveFloatBoostRewardReadyColor = rule.ActiveFloatBoostRewardReadyColor,
            ActiveFloatBoostRewardCooldownColor = rule.ActiveFloatBoostRewardCooldownColor,
            ActiveFloatBoostAddValue = rule.ActiveFloatBoostAddValue,
            ActiveFloatBoostMinimumValue = rule.ActiveFloatBoostMinimumValue,
            ActiveFloatBoostMaximumValue = rule.ActiveFloatBoostMaximumValue,
            SupporterFloatAddEnabled = rule.SupporterFloatAddEnabled,
            SupporterFloatAddMinimumValue = rule.SupporterFloatAddMinimumValue,
            SupporterFloatAddMaximumValue = rule.SupporterFloatAddMaximumValue,
            SupporterFloatAddRanges = [.. rule.SupporterFloatAddRanges.Select(ToPersistedSupporterFloatAddRange)],
            SetTriggerActions = [.. rule.SetTriggerActions.Select(ToPersistedSetTriggerAction)],
            SetTriggerRestoreMode = rule.SetTriggerRestoreMode,
            SpecialRulePairingMode = rule.SpecialRulePairingMode,
            BotMessageTemplate = rule.BotMessageTemplate,
            ReturnToPreviousAvatar = rule.ReturnToPreviousAvatar,
            PermanentAvatarChange = rule.PermanentAvatarChange,
            CooldownOnlyAvatarChange = rule.CooldownOnlyAvatarChange,
            IsGiftSubscription = rule.IsGiftSubscription,
            TemporarilyDisabledRuleIds = [.. rule.TemporarilyDisabledRuleIds]
};
    }

    private static PersistedWardrobeOutfit ToPersistedWardrobeOutfit(WardrobeOutfit outfit)
    {
        return new PersistedWardrobeOutfit
        {
            Id = outfit.Id,
            IsEnabled = outfit.IsEnabled,
            Name = outfit.Name,
            ActiveTimeSeconds = outfit.ActiveTimeSeconds,
            TwitchRewardId = outfit.TwitchRewardId,
            TwitchRewardTitle = outfit.TwitchRewardTitle,
            TwitchRewardCost = outfit.TwitchRewardCost,
            TwitchRewardDescription = outfit.TwitchRewardDescription,
            TwitchRewardSyncMode = outfit.TwitchRewardSyncMode,
            ManagedRewardReadyColor = outfit.ManagedRewardReadyColor,
            ManagedRewardCooldownColor = outfit.ManagedRewardCooldownColor,
            DeleteManagedRewardWhenInactive = outfit.DeleteManagedRewardWhenInactive,
            ChatCommandText = outfit.ChatCommandText,
            SnapshotParams = [.. outfit.SnapshotParams.Select(ToPersistedWardrobeSnapshotParam)]
        };
    }

    private static PersistedWardrobeSnapshotParam ToPersistedWardrobeSnapshotParam(WardrobeSnapshotParam param)
    {
        return new PersistedWardrobeSnapshotParam
        {
            Id = param.Id,
            ParameterName = param.ParameterName,
            ParameterType = param.ParameterType,
            SetValue = param.SetValue
        };
    }

    private static PersistedSupporterFloatAddRange ToPersistedSupporterFloatAddRange(SupporterFloatAddRange range) =>
        new()
        {
            MinimumAmount = range.MinimumAmount,
            MaximumAmount = range.MaximumAmount,
            AddValue = range.AddValue
        };

    private static PersistedSetTriggerAction ToPersistedSetTriggerAction(SetTriggerAction action) =>
        new()
        {
            Id = action.Id,
            ParameterName = action.ParameterName,
            ParameterType = action.ParameterType,
            ParameterValue = action.ParameterValue
        };

    internal static PersistedAvatarSwapProfile ToPersistedAvatarSwapProfile(AvatarSwapProfile profile)
    {
        return new PersistedAvatarSwapProfile
        {
            Id = profile.Id,
            IsEnabled = profile.IsEnabled,
            BitsMaxSwapTimeEnabled = profile.BitsMaxSwapTimeEnabled,
            SubsMaxSwapTimeEnabled = profile.SubsMaxSwapTimeEnabled,
            MaxSwapTimeSeconds = profile.MaxSwapTimeSeconds,
            TargetAvatarId = profile.TargetAvatarId,
            TargetAvatarName = profile.TargetAvatarName,
            TargetThumbnailUrl = profile.TargetThumbnailUrl,
            CreatedAt = profile.CreatedAt,
            UpdatedAt = profile.UpdatedAt,
            ChannelPointRules = [.. profile.ChannelPointRules.Select(ToPersistedRule)],
            BitsRules = [.. profile.BitsRules.Select(ToPersistedRule)],
            SubsRules = [.. profile.SubsRules.Select(ToPersistedRule)],
            PaymentRules = [.. profile.PaymentRules.Select(ToPersistedCashPaymentRule)]
        };
    }

    internal static AvatarSwapProfile ToAvatarSwapProfile(PersistedAvatarSwapProfile profile)
    {
        var legacyCap = false;
#pragma warning disable CS0618
        legacyCap = profile.LegacyMaxSwapTimeEnabled ?? false;
#pragma warning restore CS0618
        var result = new AvatarSwapProfile
        {
            Id = profile.Id,
            IsEnabled = profile.IsEnabled,
            BitsMaxSwapTimeEnabled = profile.BitsMaxSwapTimeEnabled || legacyCap,
            SubsMaxSwapTimeEnabled = profile.SubsMaxSwapTimeEnabled || legacyCap,
            MaxSwapTimeSeconds = profile.MaxSwapTimeSeconds <= 0 ? 1800 : profile.MaxSwapTimeSeconds,
            TargetAvatarId = profile.TargetAvatarId ?? string.Empty,
            TargetAvatarName = profile.TargetAvatarName ?? string.Empty,
            TargetThumbnailUrl = profile.TargetThumbnailUrl,
            CreatedAt = profile.CreatedAt == default ? DateTime.UtcNow : profile.CreatedAt,
            UpdatedAt = profile.UpdatedAt == default ? DateTime.UtcNow : profile.UpdatedAt
        };
        foreach (var rule in (profile.ChannelPointRules ?? []).Select(ToRule))
        {
            result.ChannelPointRules.Add(rule);
        }
        foreach (var rule in (profile.BitsRules ?? []).Select(ToRule))
        {
            result.BitsRules.Add(rule);
        }
        foreach (var rule in (profile.SubsRules ?? []).Select(ToRule))
        {
            result.SubsRules.Add(rule);
        }
        foreach (var paymentRule in (profile.PaymentRules ?? []).Select(ToCashPaymentRule))
        {
            result.PaymentRules.Add(paymentRule);
        }
        return result;
    }

    private static AvatarRouletteProfile ToAvatarRouletteProfile(PersistedAvatarRouletteProfile p)
    {
        var profile = new AvatarRouletteProfile
        {
            Id = p.Id == Guid.Empty ? Guid.NewGuid() : p.Id,
            Name = p.Name ?? "Roulette",
            IsEnabled = p.IsEnabled,
            CreatedAt = NormalizeTimestamp(p.CreatedAt),
            UpdatedAt = NormalizeTimestamp(p.UpdatedAt),
            ReturnAvatarId = p.ReturnAvatarId,
            ReturnAvatarName = p.ReturnAvatarName,
        };
        foreach (var entry in p.Pool ?? new())
            profile.Pool.Add(new RouletteAvatarEntry
            {
                AvatarId = entry.AvatarId,
                AvatarName = entry.AvatarName,
                ThumbnailUrl = entry.ThumbnailUrl,
            });
        foreach (var t in p.Triggers ?? new())
            profile.Triggers.Add(ToRule(t));
        return profile;
    }

    private static PersistedAvatarRouletteProfile ToPersistedAvatarRouletteProfile(AvatarRouletteProfile p)
    {
        return new PersistedAvatarRouletteProfile
        {
            Id = p.Id,
            Name = p.Name,
            IsEnabled = p.IsEnabled,
            CreatedAt = p.CreatedAt,
            UpdatedAt = p.UpdatedAt,
            ReturnAvatarId = p.ReturnAvatarId,
            ReturnAvatarName = p.ReturnAvatarName,
            Pool = p.Pool.Select(e => new PersistedRouletteAvatarEntry
            {
                AvatarId = e.AvatarId,
                AvatarName = e.AvatarName,
                ThumbnailUrl = e.ThumbnailUrl,
            }).ToList(),
            Triggers = p.Triggers.Select(ToPersistedRule).ToList(),
        };
    }

    private static DateTime NormalizeTimestamp(DateTime value) =>
        value == default ? DateTime.UtcNow : value;

    internal static TriggerRule ToRule(PersistedTriggerRule rule)
    {
        // Migration: if the saved JSON has the old FloatTransitionSeconds key
        // and the new In/Out keys are 0, copy the old value into both and clear it.
        if (rule.FloatTransitionSeconds > 0
            && rule.FloatTransitionInSeconds <= 0
            && rule.FloatTransitionOutSeconds <= 0)
        {
            rule.FloatTransitionInSeconds = Math.Clamp(rule.FloatTransitionSeconds, 0, 30);
            rule.FloatTransitionOutSeconds = Math.Clamp(rule.FloatTransitionSeconds, 0, 30);
            rule.FloatTransitionSeconds = 0;
        }

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

        var migratedSubscriptionSecondsPerAmountUnit = rule.SubscriptionsSecondsPerAmountUnit <= 0
            ? (rule.SecondsPerAmountUnit <= 0 ? 1 : rule.SecondsPerAmountUnit)
            : rule.SubscriptionsSecondsPerAmountUnit;

        return new TriggerRule
        {
            Id = rule.Id == Guid.Empty ? Guid.NewGuid() : rule.Id,
            IsEnabled = rule.IsEnabled,
            Name = string.IsNullOrWhiteSpace(rule.Name) ? "New Twitch trigger" : rule.Name,
            TriggerType = Enum.IsDefined(rule.TriggerType) ? rule.TriggerType : TwitchTriggerType.ChannelPoints,
            ChannelPointRewardId = rule.ChannelPointRewardId ?? string.Empty,
            ChannelPointRewardTitle = !string.IsNullOrWhiteSpace(rule.ChannelPointRewardTitle)
                ? rule.ChannelPointRewardTitle
                : (rule.MatchText ?? string.Empty),
            ChannelPointRewardDescription = rule.ChannelPointRewardDescription ?? string.Empty,
            ChannelPointRewardCost = rule.ChannelPointRewardCost <= 0 ? 100 : rule.ChannelPointRewardCost,
            RewardSyncMode = Enum.IsDefined(rule.RewardSyncMode)
                ? rule.RewardSyncMode
                : TwitchRewardSyncMode.CreateOrManage,
            ManagedRewardReadyColor = ManagedRewardPresentation.NormalizeReadyBackgroundColor(rule.ManagedRewardReadyColor),
            ManagedRewardCooldownColor = ManagedRewardPresentation.NormalizeCooldownBackgroundColor(rule.ManagedRewardCooldownColor),
            DeleteManagedRewardWhenInactive = rule.DeleteManagedRewardWhenInactive,
            ChatCommandEnabled = rule.ChatCommandEnabled,
            ChatCommandText = rule.ChatCommandText ?? string.Empty,
            ChatCommandPermission = Enum.IsDefined(rule.ChatCommandPermission)
                ? rule.ChatCommandPermission
                : ChatCommandPermission.Moderators,
            MinimumAmount = rule.MinimumAmount <= 0 ? 1 : rule.MinimumAmount,
            AmountScaledDurationEnabled = rule.AmountScaledDurationEnabled,
            AddBitsToSwapTime = rule.AddBitsToSwapTime,
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
            SubscriptionsSecondsPerAmountUnit = migratedSubscriptionSecondsPerAmountUnit,
            SubscriptionTier1SecondsPerSub = rule.SubscriptionTier1SecondsPerSub <= 0
                ? migratedSubscriptionSecondsPerAmountUnit
                : rule.SubscriptionTier1SecondsPerSub,
            SubscriptionTier2SecondsPerSub = rule.SubscriptionTier2SecondsPerSub <= 0
                ? migratedSubscriptionSecondsPerAmountUnit
                : rule.SubscriptionTier2SecondsPerSub,
            SubscriptionTier3SecondsPerSub = rule.SubscriptionTier3SecondsPerSub <= 0
                ? migratedSubscriptionSecondsPerAmountUnit
                : rule.SubscriptionTier3SecondsPerSub,
            SubscriptionTier1Enabled = rule.SubscriptionTier1Enabled,
            SubscriptionTier2Enabled = rule.SubscriptionTier2Enabled,
            SubscriptionTier3Enabled = rule.SubscriptionTier3Enabled,
            MaxAccumulatedDurationEnabled = rule.MaxAccumulatedDurationEnabled,
            MaxAccumulatedDurationSeconds = rule.MaxAccumulatedDurationSeconds <= 0
                ? 1800
                : rule.MaxAccumulatedDurationSeconds,
            ExtendCurrentActivity = rule.ExtendCurrentActivity,
            ExtendSeconds = rule.ExtendSeconds <= 0 ? 0 : rule.ExtendSeconds,
            ActionType = rule.ActionType,
            MovementDirection = rule.MovementDirection,
            ParameterName = rule.ParameterName ?? string.Empty,
            ParameterType = rule.ParameterType,
            IntZeroDurationMode = rule.IntZeroDurationMode,
            ParameterValue = migratedParameterValue,
            FloatValueMode = Enum.IsDefined(rule.FloatValueMode) ? rule.FloatValueMode : FloatValueMode.Decimal,
            FloatTransitionInSeconds = Math.Clamp(rule.FloatTransitionInSeconds, 0, 30),
            FloatTransitionOutSeconds = Math.Clamp(rule.FloatTransitionOutSeconds, 0, 30),
            FloatActionMode = Enum.IsDefined(rule.FloatActionMode) ? rule.FloatActionMode : FloatActionMode.Set,
            FloatRangeMin = rule.FloatRangeMin,
            FloatRangeMax = rule.FloatRangeMax,
            FloatCycleStep = rule.FloatCycleStep,
            FloatAddAmount = rule.FloatAddAmount,
            FloatSubtractAmount = rule.FloatSubtractAmount,
            FloatAddSubtractAmount = rule.FloatAddSubtractAmount,
            FloatMultiplyFactor = rule.FloatMultiplyFactor,
            FloatToggleOnValue = rule.FloatToggleOnValue,
            FloatToggleOffValue = rule.FloatToggleOffValue,
            FloatGlitchyIntervalMs = Math.Max(1, rule.FloatGlitchyIntervalMs),
            FloatPulseSeconds = Math.Max(0.0, rule.FloatPulseSeconds),
            FloatClampMode = Enum.IsDefined(rule.FloatClampMode) ? rule.FloatClampMode : FloatClampMode.ZeroToOne,
            HideRewardWhenFloatMaxReached = rule.HideRewardWhenFloatMaxReached,
            HideRewardWhenFloatMinReached = rule.HideRewardWhenFloatMinReached,
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
            SharedRewardChoiceEnabled = rule.SharedRewardChoiceEnabled,
            SharedRewardChoiceNumber = Math.Max(0, rule.SharedRewardChoiceNumber),
            SharedRewardHelpText = rule.SharedRewardHelpText ?? string.Empty,
            SupporterKeywordText = rule.SupporterKeywordText ?? string.Empty,
            ActiveFloatBoostRewardOwnerId = rule.ActiveFloatBoostRewardOwnerId == Guid.Empty
                ? Guid.NewGuid()
                : rule.ActiveFloatBoostRewardOwnerId,
            ActiveFloatBoostRewardEnabled = rule.ActiveFloatBoostRewardEnabled,
            ActiveFloatBoostRewardId = rule.ActiveFloatBoostRewardId ?? string.Empty,
            ActiveFloatBoostRewardTitle = rule.ActiveFloatBoostRewardTitle ?? string.Empty,
            ActiveFloatBoostRewardDescription = rule.ActiveFloatBoostRewardDescription ?? string.Empty,
            ActiveFloatBoostRewardCost = rule.ActiveFloatBoostRewardCost <= 0 ? 100 : rule.ActiveFloatBoostRewardCost,
            ActiveFloatBoostRewardCooldownSeconds = Math.Max(0, rule.ActiveFloatBoostRewardCooldownSeconds),
            ActiveFloatBoostRewardReadyColor = ManagedRewardPresentation.NormalizeReadyBackgroundColor(rule.ActiveFloatBoostRewardReadyColor),
            ActiveFloatBoostRewardCooldownColor = ManagedRewardPresentation.NormalizeCooldownBackgroundColor(rule.ActiveFloatBoostRewardCooldownColor),
            ActiveFloatBoostAddValue = string.IsNullOrWhiteSpace(rule.ActiveFloatBoostAddValue) ? "0.05" : rule.ActiveFloatBoostAddValue,
            ActiveFloatBoostMinimumValue = string.IsNullOrWhiteSpace(rule.ActiveFloatBoostMinimumValue) ? "0" : rule.ActiveFloatBoostMinimumValue,
            ActiveFloatBoostMaximumValue = string.IsNullOrWhiteSpace(rule.ActiveFloatBoostMaximumValue) ? "1" : rule.ActiveFloatBoostMaximumValue,
            SupporterFloatAddEnabled = rule.SupporterFloatAddEnabled,
            SupporterFloatAddMinimumValue = string.IsNullOrWhiteSpace(rule.SupporterFloatAddMinimumValue) ? "0" : rule.SupporterFloatAddMinimumValue,
            SupporterFloatAddMaximumValue = string.IsNullOrWhiteSpace(rule.SupporterFloatAddMaximumValue) ? "1" : rule.SupporterFloatAddMaximumValue,
            SupporterFloatAddRanges = new ObservableCollection<SupporterFloatAddRange>(
                (rule.SupporterFloatAddRanges is { Count: > 0 }
                    ? rule.SupporterFloatAddRanges.Select(ToSupporterFloatAddRange)
                    : [new SupporterFloatAddRange()])),
            SetTriggerActions = new ObservableCollection<SetTriggerAction>((rule.SetTriggerActions ?? [])
                .Select(ToSetTriggerAction)
                .Where(action => !string.IsNullOrWhiteSpace(action.ParameterName))),
            SetTriggerRestoreMode = Enum.IsDefined(rule.SetTriggerRestoreMode)
                ? rule.SetTriggerRestoreMode
                : SetTriggerRestoreMode.FullSafeDiff,
            SpecialRulePairingMode = Enum.IsDefined(rule.SpecialRulePairingMode)
                ? rule.SpecialRulePairingMode
                : SpecialRulePairingMode.HidePairedWhileActive,
            TemporarilyDisabledRuleIds = new ObservableCollection<Guid>((rule.TemporarilyDisabledRuleIds ?? [])
                .Where(ruleId => ruleId != Guid.Empty)
                .Distinct()),
            BotMessageTemplate = string.IsNullOrWhiteSpace(rule.BotMessageTemplate)
                ? "{user} triggered {rule}. Active for {duration}. Cooldown {cooldown}."
                : rule.BotMessageTemplate,
            ReturnToPreviousAvatar = rule.ReturnToPreviousAvatar,
            PermanentAvatarChange = rule.PermanentAvatarChange,
            CooldownOnlyAvatarChange = rule.CooldownOnlyAvatarChange,
            IsGiftSubscription = rule.IsGiftSubscription
};
    }

    private static WardrobeOutfit ToWardrobeOutfit(PersistedWardrobeOutfit persisted)
    {
        return new WardrobeOutfit
        {
            Id = persisted.Id == Guid.Empty ? Guid.NewGuid() : persisted.Id,
            IsEnabled = persisted.IsEnabled,
            Name = string.IsNullOrWhiteSpace(persisted.Name) ? "New Outfit" : persisted.Name,
            ActiveTimeSeconds = persisted.ActiveTimeSeconds <= 0
                ? WardrobeOutfit.SafeObservationSeconds
                : persisted.ActiveTimeSeconds,
            TwitchRewardId = persisted.TwitchRewardId ?? string.Empty,
            TwitchRewardTitle = persisted.TwitchRewardTitle ?? string.Empty,
            TwitchRewardCost = persisted.TwitchRewardCost ?? "100",
            TwitchRewardDescription = persisted.TwitchRewardDescription ?? string.Empty,
            TwitchRewardSyncMode = Enum.IsDefined(persisted.TwitchRewardSyncMode)
                ? persisted.TwitchRewardSyncMode
                : TwitchRewardSyncMode.CreateOrManage,
            ManagedRewardReadyColor = ManagedRewardPresentation.NormalizeReadyBackgroundColor(persisted.ManagedRewardReadyColor),
            ManagedRewardCooldownColor = ManagedRewardPresentation.NormalizeCooldownBackgroundColor(persisted.ManagedRewardCooldownColor),
            DeleteManagedRewardWhenInactive = persisted.DeleteManagedRewardWhenInactive,
            ChatCommandText = persisted.ChatCommandText ?? string.Empty,
            SnapshotParams = new ObservableCollection<WardrobeSnapshotParam>((persisted.SnapshotParams ?? []).Select(ToWardrobeSnapshotParam))
        };
    }

    private static WardrobeSnapshotParam ToWardrobeSnapshotParam(PersistedWardrobeSnapshotParam persisted)
    {
        return new WardrobeSnapshotParam
        {
            Id = persisted.Id == Guid.Empty ? Guid.NewGuid() : persisted.Id,
            ParameterName = persisted.ParameterName ?? string.Empty,
            ParameterType = Enum.IsDefined(persisted.ParameterType)
                ? persisted.ParameterType
                : OscParameterType.Bool,
            SetValue = persisted.SetValue ?? string.Empty
        };
    }

    private static SupporterFloatAddRange ToSupporterFloatAddRange(PersistedSupporterFloatAddRange range)
    {
        return new SupporterFloatAddRange
        {
            MinimumAmount = range.MinimumAmount <= 0 ? 1 : range.MinimumAmount,
            MaximumAmount = Math.Max(0, range.MaximumAmount),
            AddValue = string.IsNullOrWhiteSpace(range.AddValue) ? "0.05" : range.AddValue
        };
    }

    private static SetTriggerAction ToSetTriggerAction(PersistedSetTriggerAction action)
    {
        return new SetTriggerAction
        {
            Id = action.Id == Guid.Empty ? Guid.NewGuid() : action.Id,
            ParameterName = action.ParameterName ?? string.Empty,
            ParameterType = action.ParameterType is OscParameterType.Bool or OscParameterType.Int or OscParameterType.Float
                ? action.ParameterType
                : OscParameterType.Int,
            ParameterValue = action.ParameterValue ?? string.Empty
        };
    }

    private static PersistedUniversalTriggerRule ToPersistedUniversalTriggerRule(UniversalTriggerRule rule)
    {
        return new PersistedUniversalTriggerRule
        {
            Id = rule.Id,
            IsEnabled = rule.IsEnabled,
            Name = rule.Name,
            TriggerType = rule.TriggerType,
            ChatCommandEnabled = rule.ChatCommandEnabled,
            CommandText = rule.CommandText,
            ChatCommandPermission = rule.ChatCommandPermission,
            RewardId = rule.RewardId,
            RewardTitle = rule.RewardTitle,
            RewardDescription = rule.RewardDescription,
            RewardCost = rule.RewardCost,
            RewardCooldownSeconds = rule.RewardCooldownSeconds,
            RewardSyncMode = rule.RewardSyncMode,
            ManagedRewardReadyColor = rule.ManagedRewardReadyColor,
            ManagedRewardCooldownColor = rule.ManagedRewardCooldownColor,
            DeleteManagedRewardWhenInactive = rule.DeleteManagedRewardWhenInactive,
            MinimumBits = rule.MinimumBits,
            MaximumBits = rule.MaximumBits,
            SubscriptionTier = rule.SubscriptionTier,
            MinimumMonths = rule.MinimumMonths,
            MaximumMonths = rule.MaximumMonths,
            GlobalDelaySeconds = rule.GlobalDelaySeconds,
            UserDelaySeconds = rule.UserDelaySeconds,
            ExecuteRandomAction = rule.ExecuteRandomAction,
            ImportSource = rule.ImportSource,
            ImportIdentity = rule.ImportIdentity,
            Actions = [.. rule.Actions.Select(ToPersistedUniversalTriggerAction)]
        };
    }

    private static PersistedUniversalTriggerAction ToPersistedUniversalTriggerAction(UniversalTriggerAction action)
    {
        return new PersistedUniversalTriggerAction
        {
            Id = action.Id,
            OscAddress = action.OscAddress,
            ValueKind = action.ValueKind,
            TargetValue = action.TargetValue,
            DefaultValue = action.DefaultValue,
            DurationSeconds = action.DurationSeconds,
            AddToQueue = action.AddToQueue,
            ImportGroupKey = action.ImportGroupKey
        };
    }

    private static UniversalTriggerRule ToUniversalTriggerRule(PersistedUniversalTriggerRule rule)
    {
        return new UniversalTriggerRule
        {
            Id = rule.Id == Guid.Empty ? Guid.NewGuid() : rule.Id,
            IsEnabled = rule.IsEnabled,
            Name = string.IsNullOrWhiteSpace(rule.Name) ? "New Universal Trigger" : rule.Name,
            TriggerType = Enum.IsDefined(rule.TriggerType) ? rule.TriggerType : UniversalTriggerType.ChatCommand,
            ChatCommandEnabled = rule.ChatCommandEnabled,
            CommandText = rule.CommandText ?? string.Empty,
            ChatCommandPermission = Enum.IsDefined(rule.ChatCommandPermission)
                ? rule.ChatCommandPermission
                : ChatCommandPermission.Moderators,
            RewardId = rule.RewardId ?? string.Empty,
            RewardTitle = rule.RewardTitle ?? string.Empty,
            RewardDescription = rule.RewardDescription ?? string.Empty,
            RewardCost = rule.RewardCost <= 0 ? 100 : rule.RewardCost,
            RewardCooldownSeconds = Math.Max(0, rule.RewardCooldownSeconds),
            RewardSyncMode = Enum.IsDefined(rule.RewardSyncMode)
                ? rule.RewardSyncMode
                : TwitchRewardSyncMode.CreateOrManage,
            ManagedRewardReadyColor = ManagedRewardPresentation.NormalizeReadyBackgroundColor(rule.ManagedRewardReadyColor),
            ManagedRewardCooldownColor = ManagedRewardPresentation.NormalizeCooldownBackgroundColor(rule.ManagedRewardCooldownColor),
            DeleteManagedRewardWhenInactive = rule.DeleteManagedRewardWhenInactive,
            MinimumBits = rule.MinimumBits <= 0 ? 1 : rule.MinimumBits,
            MaximumBits = rule.MaximumBits <= 0 ? Math.Max(1, rule.MinimumBits) : rule.MaximumBits,
            SubscriptionTier = rule.SubscriptionTier ?? string.Empty,
            MinimumMonths = rule.MinimumMonths,
            MaximumMonths = rule.MaximumMonths,
            GlobalDelaySeconds = Math.Max(0, rule.GlobalDelaySeconds),
            UserDelaySeconds = Math.Max(0, rule.UserDelaySeconds),
            ExecuteRandomAction = rule.ExecuteRandomAction,
            ImportSource = rule.ImportSource ?? string.Empty,
            ImportIdentity = rule.ImportIdentity ?? string.Empty,
            Actions = new ObservableCollection<UniversalTriggerAction>((rule.Actions ?? []).Select(ToUniversalTriggerAction))
        };
    }

    private static UniversalTriggerAction ToUniversalTriggerAction(PersistedUniversalTriggerAction action)
    {
        return new UniversalTriggerAction
        {
            Id = action.Id == Guid.Empty ? Guid.NewGuid() : action.Id,
            OscAddress = action.OscAddress ?? string.Empty,
            ValueKind = Enum.IsDefined(action.ValueKind) ? action.ValueKind : UniversalTriggerValueKind.Int,
            TargetValue = action.TargetValue ?? string.Empty,
            DefaultValue = action.DefaultValue ?? string.Empty,
            DurationSeconds = Math.Max(0, action.DurationSeconds),
            AddToQueue = action.AddToQueue,
            ImportGroupKey = action.ImportGroupKey ?? string.Empty
        };
    }

    private static ObservableCollection<AvatarScaleSet> BuildAvatarScaleSets(PersistedProfileSettings profile)
    {
        if (profile.AvatarScaleSets?.Count > 0)
        {
            return new ObservableCollection<AvatarScaleSet>(profile.AvatarScaleSets.Select(ToAvatarScaleSet));
        }

        var legacyRules = (profile.AvatarScaleRules ?? [])
            .Select(ToAvatarScaleRule)
            .ToArray();
        if (legacyRules.Length == 0)
        {
            return [];
        }

        return
        [
            new AvatarScaleSet
            {
                Name = "Default Scale Set",
                ScaleRules = new ObservableCollection<AvatarScaleRule>(legacyRules)
            }
        ];
    }

    private static IEnumerable<AvatarScaleRule> EnumerateAvatarScaleSafetyMigrationRules(AppSettings settings)
    {
        foreach (var rule in settings.AvatarScaleSets.SelectMany(set => set.ScaleRules))
        {
            yield return rule;
        }

        foreach (var rule in settings.AvatarScaleRules)
        {
            yield return rule;
        }

        foreach (var rule in settings.CashPaymentRules.Where(rule => rule.UsesAvatarScaling))
        {
            yield return rule.ScaleAction;
        }

        foreach (var rule in settings.PowerUpRules.Where(rule => rule.UsesAvatarScaling))
        {
            yield return rule.ScaleAction;
        }

        foreach (var rule in settings.AvatarSwapProfiles
                     .SelectMany(profile => profile.PaymentRules)
                     .Where(rule => rule.UsesAvatarScaling))
        {
            yield return rule.ScaleAction;
        }
    }

    private static ObservableCollection<MovementRedeemSet> BuildMovementRedeemSets(
        PersistedProfileSettings profile,
        IEnumerable<TriggerRule> legacyMovementRules)
    {
        if (profile.MovementRedeemSets?.Count > 0)
        {
            return new ObservableCollection<MovementRedeemSet>(profile.MovementRedeemSets.Select(ToMovementRedeemSet));
        }

        var legacyRules = legacyMovementRules.ToArray();
        if (legacyRules.Length == 0)
        {
            return [];
        }

        return
        [
            new MovementRedeemSet
            {
                Name = "Default Movement Set",
                MovementRules = new ObservableCollection<TriggerRule>(legacyRules)
            }
        ];
    }

    private static PersistedMovementRedeemSet ToPersistedMovementRedeemSet(MovementRedeemSet set)
    {
        return new PersistedMovementRedeemSet
        {
            Id = set.Id,
            Name = set.Name,
            MovementRules = [.. set.MovementRules.Select(ToPersistedRule)]
        };
    }

    private static MovementRedeemSet ToMovementRedeemSet(PersistedMovementRedeemSet set)
    {
        return new MovementRedeemSet
        {
            Id = set.Id == Guid.Empty ? Guid.NewGuid() : set.Id,
            Name = string.IsNullOrWhiteSpace(set.Name) ? "Default Movement Set" : set.Name,
            MovementRules = new ObservableCollection<TriggerRule>((set.MovementRules ?? []).Select(ToRule))
        };
    }

    private static PersistedAvatarScaleSet ToPersistedAvatarScaleSet(AvatarScaleSet set)
    {
        return new PersistedAvatarScaleSet
        {
            Id = set.Id,
            Name = set.Name,
            ScaleRules = [.. set.ScaleRules.Select(ToPersistedAvatarScaleRule)]
        };
    }

    private static AvatarScaleSet ToAvatarScaleSet(PersistedAvatarScaleSet set)
    {
        return new AvatarScaleSet
        {
            Id = set.Id == Guid.Empty ? Guid.NewGuid() : set.Id,
            Name = string.IsNullOrWhiteSpace(set.Name) ? "Default Scale Set" : set.Name,
            ScaleRules = new ObservableCollection<AvatarScaleRule>((set.ScaleRules ?? []).Select(ToAvatarScaleRule))
        };
    }

    private static PersistedAvatarScaleMasterRewardSettings ToPersistedAvatarScaleMasterReward(
        AvatarScaleMasterRewardSettings settings)
    {
        return new PersistedAvatarScaleMasterRewardSettings
        {
            IsEnabled = settings.IsEnabled,
            RewardId = settings.RewardId,
            RewardTitle = settings.RewardTitle,
            RewardDescription = settings.RewardDescription,
            RewardCost = settings.RewardCost,
            RewardSyncMode = settings.RewardSyncMode,
            UnlockDurationSeconds = settings.UnlockDurationSeconds,
            CooldownSeconds = settings.CooldownSeconds,
            ManagedRewardReadyColor = settings.ManagedRewardReadyColor,
            ManagedRewardCooldownColor = settings.ManagedRewardCooldownColor,
            DeleteMasterRewardWhenInactive = settings.DeleteMasterRewardWhenInactive,
            FreeChildRewardSlotsWhenLocked = settings.FreeChildRewardSlotsWhenLocked,
            PreventAvatarChangesDuringActiveScaling = settings.PreventAvatarChangesDuringActiveScaling
        };
    }

    private static AvatarScaleMasterRewardSettings ToAvatarScaleMasterReward(
        PersistedAvatarScaleMasterRewardSettings settings)
    {
        return new AvatarScaleMasterRewardSettings
        {
            IsEnabled = settings.IsEnabled,
            RewardId = settings.RewardId ?? string.Empty,
            RewardTitle = string.IsNullOrWhiteSpace(settings.RewardTitle)
                ? "Avatar Scaling"
                : settings.RewardTitle,
            RewardDescription = settings.RewardDescription ?? string.Empty,
            RewardCost = settings.RewardCost <= 0 ? 100 : settings.RewardCost,
            RewardSyncMode = Enum.IsDefined(settings.RewardSyncMode)
                ? settings.RewardSyncMode
                : TwitchRewardSyncMode.CreateOrManage,
            UnlockDurationSeconds = settings.UnlockDurationSeconds <= 0 ? 60 : settings.UnlockDurationSeconds,
            CooldownSeconds = Math.Max(0, settings.CooldownSeconds),
            ManagedRewardReadyColor = ManagedRewardPresentation.NormalizeReadyBackgroundColor(settings.ManagedRewardReadyColor),
            ManagedRewardCooldownColor = ManagedRewardPresentation.NormalizeCooldownBackgroundColor(settings.ManagedRewardCooldownColor),
            DeleteMasterRewardWhenInactive = settings.DeleteMasterRewardWhenInactive,
            FreeChildRewardSlotsWhenLocked = settings.FreeChildRewardSlotsWhenLocked ?? true,
            PreventAvatarChangesDuringActiveScaling = settings.PreventAvatarChangesDuringActiveScaling
        };
    }

    private static PersistedPowerUpRule ToPersistedPowerUpRule(PowerUpRule rule)
    {
        return new PersistedPowerUpRule
        {
            Id = rule.Id,
            IsEnabled = rule.IsEnabled,
            Name = rule.Name,
            SourceMode = rule.SourceMode,
            PowerUpId = rule.PowerUpId,
            PowerUpTitle = rule.PowerUpTitle,
            BitsCost = rule.BitsCost,
            Prompt = rule.Prompt,
            AvatarScoped = rule.AvatarScoped,
            AvatarId = rule.AvatarId,
            AvatarName = rule.AvatarName,
            CooldownSeconds = rule.CooldownSeconds,
            FixedFloatAddEnabled = rule.FixedFloatAddEnabled,
            FixedFloatAddValue = rule.FixedFloatAddValue,
            FixedFloatAddMinimumValue = rule.FixedFloatAddMinimumValue,
            FixedFloatAddMaximumValue = rule.FixedFloatAddMaximumValue,
            ActionKind = rule.ActionKind,
            ActionRule = ToPersistedRule(rule.ActionRule),
            ScaleAction = ToPersistedAvatarScaleRule(rule.ScaleAction)
        };
    }

    private static PowerUpRule ToPowerUpRule(PersistedPowerUpRule rule)
    {
        var powerUp = new PowerUpRule
        {
            Id = rule.Id == Guid.Empty ? Guid.NewGuid() : rule.Id,
            IsEnabled = rule.IsEnabled ?? true,
            Name = string.IsNullOrWhiteSpace(rule.Name) ? "New Power Up" : rule.Name.Trim(),
            SourceMode = Enum.IsDefined(rule.SourceMode) ? rule.SourceMode : TwitchRewardSyncMode.LinkExisting,
            PowerUpId = rule.PowerUpId ?? string.Empty,
            PowerUpTitle = rule.PowerUpTitle ?? string.Empty,
            BitsCost = rule.BitsCost <= 0 ? 100 : rule.BitsCost,
            Prompt = rule.Prompt ?? string.Empty,
            AvatarScoped = rule.AvatarScoped,
            AvatarId = rule.AvatarId ?? string.Empty,
            AvatarName = rule.AvatarName ?? string.Empty,
            CooldownSeconds = Math.Max(0, rule.CooldownSeconds ?? 30),
            FixedFloatAddEnabled = rule.FixedFloatAddEnabled,
            FixedFloatAddValue = string.IsNullOrWhiteSpace(rule.FixedFloatAddValue) ? "0.05" : rule.FixedFloatAddValue.Trim(),
            FixedFloatAddMinimumValue = string.IsNullOrWhiteSpace(rule.FixedFloatAddMinimumValue) ? "0" : rule.FixedFloatAddMinimumValue.Trim(),
            FixedFloatAddMaximumValue = string.IsNullOrWhiteSpace(rule.FixedFloatAddMaximumValue) ? "1" : rule.FixedFloatAddMaximumValue.Trim(),
            ActionKind = Enum.IsDefined(rule.ActionKind) ? rule.ActionKind : PowerUpActionKind.TriggerAction,
            ActionRule = rule.ActionRule is null ? PowerUpRule.CreateDefaultTriggerAction() : ToRule(rule.ActionRule),
            ScaleAction = rule.ScaleAction is null ? PowerUpRule.CreateDefaultScaleAction() : ToAvatarScaleRule(rule.ScaleAction)
        };

        powerUp.ActionRule.TriggerType = TwitchTriggerType.PowerUp;
        powerUp.ActionRule.RewardSyncMode = TwitchRewardSyncMode.LinkExisting;
        powerUp.ActionRule.ChannelPointRewardId = string.Empty;
        powerUp.ActionRule.ChannelPointRewardTitle = string.Empty;
        powerUp.ActionRule.ChatCommandEnabled = false;
        powerUp.ScaleAction.TriggerType = AvatarScaleTriggerType.Bits;
        powerUp.ScaleAction.RewardId = string.Empty;
        powerUp.ScaleAction.RewardTitle = string.Empty;
        powerUp.ScaleAction.MinimumBits = 1;
        powerUp.ScaleAction.MaximumBits = int.MaxValue;
        return powerUp;
    }

    private static PersistedRewardFireSaleSettings ToPersistedRewardFireSaleSettings(
        RewardFireSaleSettings settings)
    {
        return new PersistedRewardFireSaleSettings
        {
            IsEnabled = settings.IsEnabled,
            CountBits = settings.CountBits,
            CountManagedRewards = settings.CountManagedRewards,
            DiscountManagedPowerUpsEnabled = settings.DiscountManagedPowerUpsEnabled,
            FundingRewardEnabled = settings.FundingRewardEnabled,
            FundingRewardId = settings.FundingRewardId,
            FundingRewardTitle = settings.FundingRewardTitle,
            FundingRewardDescription = settings.FundingRewardDescription,
            FundingRewardCost = settings.FundingRewardCost,
            FundingRewardCooldownSeconds = settings.FundingRewardCooldownSeconds,
            FundingRewardReadyColor = settings.FundingRewardReadyColor,
            FundingRewardCooldownColor = settings.FundingRewardCooldownColor,
            RewardPointsPerProgressUnit = settings.RewardPointsPerProgressUnit,
            MultiTierEnabled = settings.MultiTierEnabled,
            SaleMode = settings.SaleMode,
            TemporaryDurationSeconds = settings.TemporaryDurationSeconds,
            CurrentProgress = settings.CurrentProgress,
            IsSaleActive = settings.IsSaleActive,
            ActiveDiscountPercent = settings.ActiveDiscountPercent,
            ActiveTierGoalAmount = settings.ActiveTierGoalAmount,
            ActiveUntilUtc = settings.ActiveUntilUtc,
            Tiers = [.. settings.Tiers.Select(ToPersistedRewardFireSaleTier)]
        };
    }

    private static PersistedRewardFireSaleTier ToPersistedRewardFireSaleTier(RewardFireSaleTier tier)
    {
        return new PersistedRewardFireSaleTier
        {
            Id = tier.Id,
            GoalAmount = tier.GoalAmount,
            DiscountPercent = tier.DiscountPercent
        };
    }

    private static RewardFireSaleSettings ToRewardFireSaleSettings(PersistedRewardFireSaleSettings settings)
    {
        var tiers = (settings.Tiers ?? [])
            .Select(ToRewardFireSaleTier)
            .Where(tier => tier.GoalAmount > 0)
            .OrderBy(tier => tier.GoalAmount)
            .ToArray();

        return new RewardFireSaleSettings
        {
            IsEnabled = settings.IsEnabled,
            CountBits = settings.CountBits ?? true,
            CountManagedRewards = settings.CountManagedRewards ?? true,
            DiscountManagedPowerUpsEnabled = settings.DiscountManagedPowerUpsEnabled ?? false,
            FundingRewardEnabled = settings.FundingRewardEnabled,
            FundingRewardId = settings.FundingRewardId?.Trim() ?? string.Empty,
            FundingRewardTitle = string.IsNullOrWhiteSpace(settings.FundingRewardTitle)
                ? "Fire Sale Fund"
                : settings.FundingRewardTitle.Trim(),
            FundingRewardDescription = settings.FundingRewardDescription ?? string.Empty,
            FundingRewardCost = settings.FundingRewardCost <= 0 ? 100 : settings.FundingRewardCost,
            FundingRewardCooldownSeconds = Math.Max(0, settings.FundingRewardCooldownSeconds),
            FundingRewardReadyColor = ManagedRewardPresentation.NormalizeReadyBackgroundColor(settings.FundingRewardReadyColor),
            FundingRewardCooldownColor = ManagedRewardPresentation.NormalizeCooldownBackgroundColor(settings.FundingRewardCooldownColor),
            RewardPointsPerProgressUnit = settings.RewardPointsPerProgressUnit <= 0 ? 10 : settings.RewardPointsPerProgressUnit,
            MultiTierEnabled = settings.MultiTierEnabled ?? true,
            SaleMode = Enum.IsDefined(settings.SaleMode) ? settings.SaleMode : RewardFireSaleMode.Temporary,
            TemporaryDurationSeconds = settings.TemporaryDurationSeconds <= 0 ? 300 : settings.TemporaryDurationSeconds,
            CurrentProgress = Math.Max(0, settings.CurrentProgress),
            IsSaleActive = settings.IsSaleActive,
            ActiveDiscountPercent = Math.Clamp(settings.ActiveDiscountPercent, 0, 100),
            ActiveTierGoalAmount = Math.Max(0, settings.ActiveTierGoalAmount),
            ActiveUntilUtc = settings.ActiveUntilUtc,
            Tiers = new ObservableCollection<RewardFireSaleTier>(
                tiers.Length == 0
                    ? new[] { new RewardFireSaleTier() }
                    : tiers)
        };
    }

    private static RewardFireSaleTier ToRewardFireSaleTier(PersistedRewardFireSaleTier tier)
    {
        return new RewardFireSaleTier
        {
            Id = tier.Id == Guid.Empty ? Guid.NewGuid() : tier.Id,
            GoalAmount = tier.GoalAmount <= 0 ? 5000 : tier.GoalAmount,
            DiscountPercent = tier.DiscountPercent <= 0 ? 25 : tier.DiscountPercent
        };
    }

    private static PersistedAvatarScaleSafetySettings ToPersistedAvatarScaleSafety(AvatarScaleSafetySettings settings)
    {
        return new PersistedAvatarScaleSafetySettings
        {
            CurrentMinimumHeightMeters = settings.CurrentMinimumHeightMeters,
            CurrentMaximumHeightMeters = settings.CurrentMaximumHeightMeters
        };
    }

    private static AvatarScaleSafetySettings ToAvatarScaleSafety(
        PersistedAvatarScaleSafetySettings? persisted,
        IEnumerable<AvatarScaleRule> migrationRules)
    {
        if (persisted is null)
        {
            return AvatarScaleSafetySettings.FromExistingRules(migrationRules);
        }

        return new AvatarScaleSafetySettings
        {
            CurrentMinimumHeightMeters = persisted.CurrentMinimumHeightMeters,
            CurrentMaximumHeightMeters = persisted.CurrentMaximumHeightMeters
        };
    }

    internal static PersistedAvatarScaleRule ToPersistedAvatarScaleRule(AvatarScaleRule rule)
    {
        return new PersistedAvatarScaleRule
        {
            Id = rule.Id,
            IsEnabled = rule.IsEnabled,
            Name = rule.Name,
            TriggerType = rule.TriggerType,
            ChatCommandEnabled = rule.ChatCommandEnabled,
            CommandText = rule.CommandText,
            ChatCommandPermission = rule.ChatCommandPermission,
            RewardId = rule.RewardId,
            RewardTitle = rule.RewardTitle,
            RewardDescription = rule.RewardDescription,
            RewardCost = rule.RewardCost,
            RewardSyncMode = rule.RewardSyncMode,
            ManagedRewardReadyColor = rule.ManagedRewardReadyColor,
            ManagedRewardCooldownColor = rule.ManagedRewardCooldownColor,
            DeleteManagedRewardWhenInactive = rule.DeleteManagedRewardWhenInactive,
            MinimumBits = rule.MinimumBits,
            MaximumBits = rule.MaximumBits,
            SubscriptionTier = rule.SubscriptionTier,
            MinimumMonths = rule.MinimumMonths,
            MaximumMonths = rule.MaximumMonths,
            CooldownSeconds = rule.CooldownSeconds,
            TemporarilyDisabledScaleRuleIds = [.. rule.TemporarilyDisabledScaleRuleIds],
            ScaleMode = rule.ScaleMode,
            TargetHeightMeters = rule.TargetHeightMeters,
            MinimumHeightMeters = rule.MinimumHeightMeters,
            MaximumHeightMeters = rule.MaximumHeightMeters,
            RelativeHeightMeters = rule.RelativeHeightMeters,
            RelativeMinimumHeightMeters = rule.RelativeMinimumHeightMeters,
            RelativeMaximumHeightMeters = rule.RelativeMaximumHeightMeters,
            HideRewardWhenMinimumHeightReached = rule.HideRewardWhenMinimumHeightReached,
            HideRewardWhenMaximumHeightReached = rule.HideRewardWhenMaximumHeightReached,
            HeightMultiplier = rule.HeightMultiplier,
            MultiplierDirectionId = (int)rule.MultiplierDirection,
            RelativeHeightDirectionId = (int)rule.RelativeHeightDirection,
            GlitchyTransitionSeconds = rule.GlitchyRandomHeightTransitionSeconds,
            SetHeightTransitionSeconds = rule.SetHeightTransitionSeconds,
            RandomHeightTransitionSeconds = rule.RandomHeightTransitionSeconds,
            RelativeHeightTransitionSeconds = rule.RelativeHeightTransitionSeconds,
            MultiplierTransitionSeconds = rule.MultiplierTransitionSeconds,
            PresetTransitionSeconds = rule.PresetTransitionSeconds,
            GlitchyRandomHeightTransitionSeconds = rule.GlitchyRandomHeightTransitionSeconds,
            SupporterGrowthTransitionSeconds = rule.SupporterGrowthTransitionSeconds,
            SmoothTransitionSeconds = 0,
            Preset = rule.Preset,
            ActiveTimeSeconds = rule.ActiveTimeSeconds,
            ExtendCurrentActivity = rule.ExtendCurrentActivity,
            ExtendSeconds = rule.ExtendSeconds,
            RestoreMode = rule.RestoreMode,
            RestoreHeightMeters = rule.RestoreHeightMeters,
            AdvancedRangeEnabled = rule.AdvancedRangeEnabled,
            BypassVrChatScaleLimits = rule.BypassVrChatScaleLimits,
            SupporterGrowthInactivityTimerSeconds = rule.SupporterGrowthInactivityTimerSeconds,
            SupporterGrowthAllowRewardScaleOverlay = rule.SupporterGrowthAllowRewardScaleOverlay,
            SupporterGrowthRequireCheerKeyword = rule.SupporterGrowthRequireCheerKeyword,
            SupporterGrowthBitsTimerUnit = rule.SupporterGrowthBitsTimerUnit,
            SupporterGrowthSecondsPerBitsUnit = rule.SupporterGrowthSecondsPerBitsUnit,
            SupporterGrowthTier1Seconds = rule.SupporterGrowthTier1Seconds,
            SupporterGrowthTier2Seconds = rule.SupporterGrowthTier2Seconds,
            SupporterGrowthTier3Seconds = rule.SupporterGrowthTier3Seconds,
            SupporterGrowthSoftCapSeconds = rule.SupporterGrowthSoftCapSeconds,
            SupporterGrowthSoftCapMultiplierPercent = rule.SupporterGrowthSoftCapMultiplierPercent,
            SupporterGrowthMaxPaidTimeSeconds = rule.SupporterGrowthMaxPaidTimeSeconds,
            SupporterGrowthGrowKeyword = rule.SupporterGrowthGrowKeyword,
            SupporterGrowthShrinkKeyword = rule.SupporterGrowthShrinkKeyword,
            SupporterGrowthTier1HeightMeters = rule.SupporterGrowthTier1HeightMeters,
            SupporterGrowthTier2HeightMeters = rule.SupporterGrowthTier2HeightMeters,
            SupporterGrowthTier3HeightMeters = rule.SupporterGrowthTier3HeightMeters,
            SupporterGrowthBitRanges = [.. rule.SupporterGrowthBitRanges.Select(ToPersistedAvatarScaleBitGrowthRange)]
        };
    }

    private static PersistedAvatarScaleBitGrowthRange ToPersistedAvatarScaleBitGrowthRange(AvatarScaleBitGrowthRange range)
    {
        return new PersistedAvatarScaleBitGrowthRange
        {
            MinimumBits = range.MinimumBits,
            MaximumBits = range.MaximumBits,
            HeightAddedMeters = range.HeightAddedMeters
        };
    }

    internal static AvatarScaleRule ToAvatarScaleRule(PersistedAvatarScaleRule rule)
    {
        var scaleRule = new AvatarScaleRule
        {
            Id = rule.Id == Guid.Empty ? Guid.NewGuid() : rule.Id,
            IsEnabled = rule.IsEnabled,
            Name = string.IsNullOrWhiteSpace(rule.Name) ? "New Avatar Scale" : rule.Name,
            TriggerType = Enum.IsDefined(rule.TriggerType) ? rule.TriggerType : AvatarScaleTriggerType.ChannelPointReward,
            ChatCommandEnabled = rule.ChatCommandEnabled,
            CommandText = rule.CommandText ?? string.Empty,
            ChatCommandPermission = Enum.IsDefined(rule.ChatCommandPermission)
                ? rule.ChatCommandPermission
                : ChatCommandPermission.Moderators,
            RewardId = rule.RewardId ?? string.Empty,
            RewardTitle = rule.RewardTitle ?? string.Empty,
            RewardDescription = rule.RewardDescription ?? string.Empty,
            RewardCost = rule.RewardCost <= 0 ? 100 : rule.RewardCost,
            RewardSyncMode = Enum.IsDefined(rule.RewardSyncMode)
                ? rule.RewardSyncMode
                : TwitchRewardSyncMode.CreateOrManage,
            ManagedRewardReadyColor = ManagedRewardPresentation.NormalizeReadyBackgroundColor(rule.ManagedRewardReadyColor),
            ManagedRewardCooldownColor = ManagedRewardPresentation.NormalizeCooldownBackgroundColor(rule.ManagedRewardCooldownColor),
            DeleteManagedRewardWhenInactive = rule.DeleteManagedRewardWhenInactive,
            MinimumBits = rule.MinimumBits <= 0 ? 1 : rule.MinimumBits,
            MaximumBits = rule.MaximumBits <= 0 ? Math.Max(1, rule.MinimumBits) : rule.MaximumBits,
            SubscriptionTier = rule.SubscriptionTier ?? string.Empty,
            MinimumMonths = rule.MinimumMonths,
            MaximumMonths = rule.MaximumMonths,
            CooldownSeconds = Math.Max(0, rule.CooldownSeconds),
            TemporarilyDisabledScaleRuleIds = new ObservableCollection<Guid>((rule.TemporarilyDisabledScaleRuleIds ?? [])
                .Where(ruleId => ruleId != Guid.Empty)
                .Distinct()),
            AdvancedRangeEnabled = rule.AdvancedRangeEnabled,
            BypassVrChatScaleLimits = rule.BypassVrChatScaleLimits,
            ScaleMode = Enum.IsDefined(rule.ScaleMode) ? rule.ScaleMode : AvatarScaleMode.SetHeight,
            TargetHeightMeters = rule.TargetHeightMeters <= 0 ? 1.6 : rule.TargetHeightMeters,
            MinimumHeightMeters = rule.MinimumHeightMeters <= 0 ? 0.5 : rule.MinimumHeightMeters,
            MaximumHeightMeters = rule.MaximumHeightMeters <= 0 ? 2.5 : rule.MaximumHeightMeters,
            RelativeHeightMeters = rule.RelativeHeightMeters == 0 ? 0.25 : Math.Abs(rule.RelativeHeightMeters),
            RelativeMinimumHeightMeters = rule.RelativeMinimumHeightMeters <= 0
                ? AvatarScaleRule.SafeMinimumHeightMeters
                : rule.RelativeMinimumHeightMeters,
            RelativeMaximumHeightMeters = rule.RelativeMaximumHeightMeters <= 0
                ? AvatarScaleRule.SafeMaximumHeightMeters
                : rule.RelativeMaximumHeightMeters,
            HideRewardWhenMinimumHeightReached = rule.HideRewardWhenMinimumHeightReached,
            HideRewardWhenMaximumHeightReached = rule.HideRewardWhenMaximumHeightReached,
            HeightMultiplier = rule.HeightMultiplier <= 0 ? 1.25 : rule.HeightMultiplier,
            MultiplierDirectionId = rule.MultiplierDirectionId,
            RelativeHeightDirectionId = rule.RelativeHeightDirectionId != 0
                ? rule.RelativeHeightDirectionId
                : (rule.RelativeHeightMeters < 0 ? (int)AvatarScaleRelativeHeightDirection.Subtract : (int)AvatarScaleRelativeHeightDirection.Add),
            SetHeightTransitionSeconds = Math.Max(0, rule.SetHeightTransitionSeconds),
            RandomHeightTransitionSeconds = Math.Max(0, rule.RandomHeightTransitionSeconds),
            RelativeHeightTransitionSeconds = Math.Max(0, rule.RelativeHeightTransitionSeconds > 0
                ? rule.RelativeHeightTransitionSeconds
                : rule.SmoothTransitionSeconds),
            MultiplierTransitionSeconds = Math.Max(0, rule.MultiplierTransitionSeconds),
            PresetTransitionSeconds = Math.Max(0, rule.PresetTransitionSeconds),
            GlitchyRandomHeightTransitionSeconds = Math.Max(0, rule.GlitchyRandomHeightTransitionSeconds > 0
                ? rule.GlitchyRandomHeightTransitionSeconds
                : rule.GlitchyTransitionSeconds),
            SupporterGrowthTransitionSeconds = Math.Max(0, rule.SupporterGrowthTransitionSeconds > 0
                ? rule.SupporterGrowthTransitionSeconds
                : rule.SmoothTransitionSeconds),
            Preset = Enum.IsDefined(rule.Preset) ? rule.Preset : AvatarScalePreset.Normal,
            ActiveTimeSeconds = Math.Max(0, rule.ActiveTimeSeconds),
            ExtendCurrentActivity = rule.ExtendCurrentActivity,
            ExtendSeconds = rule.ExtendSeconds <= 0 ? 0 : rule.ExtendSeconds,
            RestoreMode = AvatarScaleRestoreMode.ConfiguredHeight,
            RestoreHeightMeters = rule.RestoreHeightMeters <= 0 ? 1.6 : rule.RestoreHeightMeters,
            SupporterGrowthInactivityTimerSeconds = rule.SupporterGrowthInactivityTimerSeconds <= 0
                ? 60
                : rule.SupporterGrowthInactivityTimerSeconds,
            SupporterGrowthAllowRewardScaleOverlay = rule.SupporterGrowthAllowRewardScaleOverlay,
            SupporterGrowthRequireCheerKeyword = rule.SupporterGrowthRequireCheerKeyword,
            SupporterGrowthBitsTimerUnit = rule.SupporterGrowthBitsTimerUnit <= 0
                ? 100
                : rule.SupporterGrowthBitsTimerUnit,
            SupporterGrowthSecondsPerBitsUnit = rule.SupporterGrowthSecondsPerBitsUnit <= 0
                ? 30
                : rule.SupporterGrowthSecondsPerBitsUnit,
            SupporterGrowthTier1Seconds = rule.SupporterGrowthTier1Seconds <= 0
                ? 300
                : rule.SupporterGrowthTier1Seconds,
            SupporterGrowthTier2Seconds = rule.SupporterGrowthTier2Seconds <= 0
                ? 600
                : rule.SupporterGrowthTier2Seconds,
            SupporterGrowthTier3Seconds = rule.SupporterGrowthTier3Seconds <= 0
                ? 1500
                : rule.SupporterGrowthTier3Seconds,
            SupporterGrowthSoftCapSeconds = rule.SupporterGrowthSoftCapSeconds <= 0
                ? 1800
                : rule.SupporterGrowthSoftCapSeconds,
            SupporterGrowthSoftCapMultiplierPercent = rule.SupporterGrowthSoftCapMultiplierPercent <= 0
                ? 50
                : Math.Clamp(rule.SupporterGrowthSoftCapMultiplierPercent, 0, 100),
            SupporterGrowthMaxPaidTimeSeconds = rule.SupporterGrowthMaxPaidTimeSeconds <= 0
                ? 3600
                : rule.SupporterGrowthMaxPaidTimeSeconds,
            SupporterGrowthGrowKeyword = string.IsNullOrWhiteSpace(rule.SupporterGrowthGrowKeyword)
                ? "grow"
                : rule.SupporterGrowthGrowKeyword.Trim(),
            SupporterGrowthShrinkKeyword = string.IsNullOrWhiteSpace(rule.SupporterGrowthShrinkKeyword)
                ? "shrink"
                : rule.SupporterGrowthShrinkKeyword.Trim(),
            SupporterGrowthTier1HeightMeters = rule.SupporterGrowthTier1HeightMeters <= 0
                ? 0.10
                : rule.SupporterGrowthTier1HeightMeters,
            SupporterGrowthTier2HeightMeters = rule.SupporterGrowthTier2HeightMeters <= 0
                ? 0.20
                : rule.SupporterGrowthTier2HeightMeters,
            SupporterGrowthTier3HeightMeters = rule.SupporterGrowthTier3HeightMeters <= 0
                ? 0.30
                : rule.SupporterGrowthTier3HeightMeters,
            SupporterGrowthBitRanges = new ObservableCollection<AvatarScaleBitGrowthRange>(
                (rule.SupporterGrowthBitRanges is { Count: > 0 }
                    ? rule.SupporterGrowthBitRanges.Select(ToAvatarScaleBitGrowthRange)
                    : [new AvatarScaleBitGrowthRange()]))
        };

        if (scaleRule.MaximumHeightMeters < scaleRule.MinimumHeightMeters)
        {
            scaleRule.MaximumHeightMeters = scaleRule.MinimumHeightMeters;
        }

        if (scaleRule.ScaleMode == AvatarScaleMode.GlitchyRandomHeight && scaleRule.ActiveTimeSeconds <= 0)
        {
            scaleRule.ActiveTimeSeconds = 10;
        }

        return scaleRule;
    }

    private static AvatarScaleBitGrowthRange ToAvatarScaleBitGrowthRange(PersistedAvatarScaleBitGrowthRange range)
    {
        return new AvatarScaleBitGrowthRange
        {
            MinimumBits = range.MinimumBits <= 0 ? 1 : range.MinimumBits,
            MaximumBits = Math.Max(0, range.MaximumBits),
            HeightAddedMeters = Math.Max(0, range.HeightAddedMeters)
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
        settings.MovementRedeemSets.Clear();
        settings.GlobalOverrideRules.Clear();

        var legacyChannelPointRules = clonedRules
            .Where(rule => rule.TriggerType == TwitchTriggerType.ChannelPoints && rule.ActionType != OscActionType.PlayerMovement)
            .ToArray();

        var movementRules = clonedRules.Where(rule =>
                     rule.TriggerType == TwitchTriggerType.ChannelPoints
                     && rule.ActionType == OscActionType.PlayerMovement)
            .ToArray();
        foreach (var movementRule in movementRules)
        {
            settings.GlobalMovementRules.Add(movementRule);
        }

        if (movementRules.Length > 0)
        {
            settings.MovementRedeemSets.Add(new MovementRedeemSet
            {
                Name = "Default Movement Set",
                MovementRules = new ObservableCollection<TriggerRule>(movementRules)
            });
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
        var broadcasterCanRecoverFromRefreshToken = role == BridgeAccountRole.Broadcaster
            && !string.IsNullOrWhiteSpace(refreshToken);
        if ((string.IsNullOrWhiteSpace(accessToken) || string.IsNullOrWhiteSpace(account.UserId))
            && !broadcasterCanRecoverFromRefreshToken)
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
        return CreateVrChatAccountSettingsForLoad(
            authCookie,
            account.UserId,
            account.DisplayName,
            account.CurrentAvatarId);
    }

    internal static VrChatAccountSettings CreateVrChatAccountSettingsForLoad(
        string? authCookie,
        string? userId,
        string? displayName,
        string? currentAvatarId)
    {
        var normalizedUserId = userId?.Trim() ?? string.Empty;
        if (string.IsNullOrWhiteSpace(normalizedUserId))
        {
            return new VrChatAccountSettings();
        }

        return new VrChatAccountSettings
        {
            AuthCookie = authCookie ?? string.Empty,
            UserId = normalizedUserId,
            DisplayName = displayName?.Trim() ?? string.Empty,
            CurrentAvatarId = currentAvatarId?.Trim() ?? string.Empty
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

    private static PersistedWorldCommandBlacklistSettings ToPersistedWorldCommandBlacklist(WorldCommandBlacklistSettings settings)
    {
        return new PersistedWorldCommandBlacklistSettings
        {
            IsEnabled = settings.IsEnabled
        };
    }

    private static WorldCommandBlacklistSettings ToWorldCommandBlacklistSettings(PersistedWorldCommandBlacklistSettings? settings)
    {
        if (settings is null)
        {
            return new WorldCommandBlacklistSettings();
        }

        return new WorldCommandBlacklistSettings
        {
            IsEnabled = settings.IsEnabled ?? false
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

    private static CustomThemeSettings ToCustomThemeSettings(PersistedCustomThemeSettings persisted) =>
        new()
        {
            IsInitialized = persisted.IsInitialized ?? false,
            WindowBackgroundHex = persisted.WindowBackgroundHex ?? string.Empty,
            PanelBackgroundHex = persisted.PanelBackgroundHex ?? string.Empty,
            PanelSecondaryHex = persisted.PanelSecondaryHex ?? string.Empty,
            BorderHex = persisted.BorderHex ?? string.Empty,
            AccentHex = persisted.AccentHex ?? string.Empty,
            TextHex = persisted.TextHex ?? string.Empty,
            MutedTextHex = persisted.MutedTextHex ?? string.Empty,
            InputBackgroundHex = persisted.InputBackgroundHex ?? string.Empty,
            InputBorderHex = persisted.InputBorderHex ?? string.Empty,
            SecondaryButtonHex = persisted.SecondaryButtonHex ?? string.Empty,
            TitleBarHex = persisted.TitleBarHex ?? string.Empty,
            DangerHex = persisted.DangerHex ?? string.Empty,
            BodyFontFamily = persisted.BodyFontFamily ?? "Verdana",
            HeadingFontFamily = persisted.HeadingFontFamily ?? "Constantia",
            BackgroundImageRelativePath = persisted.BackgroundImageRelativePath ?? string.Empty
        };

    private static PersistedRedeemGroup ToPersistedRedeemGroup(RedeemGroup group) =>
        new()
        {
            Name = group.Name,
            CommandText = group.CommandText,
            AssignedRuleIds = [.. group.AssignedRuleIds]
        };

    private static RedeemGroup ToRedeemGroup(PersistedRedeemGroup group) =>
        new()
        {
            Name = group.Name ?? string.Empty,
            CommandText = group.CommandText ?? string.Empty,
            AssignedRuleIds = new ObservableCollection<Guid>(group.AssignedRuleIds ?? [])
        };

    private static PersistedCustomThemeSettings ToPersistedCustomThemeSettings(CustomThemeSettings settings) =>
        new()
        {
            IsInitialized = settings.IsInitialized,
            WindowBackgroundHex = settings.WindowBackgroundHex,
            PanelBackgroundHex = settings.PanelBackgroundHex,
            PanelSecondaryHex = settings.PanelSecondaryHex,
            BorderHex = settings.BorderHex,
            AccentHex = settings.AccentHex,
            TextHex = settings.TextHex,
            MutedTextHex = settings.MutedTextHex,
            InputBackgroundHex = settings.InputBackgroundHex,
            InputBorderHex = settings.InputBorderHex,
            SecondaryButtonHex = settings.SecondaryButtonHex,
            TitleBarHex = settings.TitleBarHex,
            DangerHex = settings.DangerHex,
            BodyFontFamily = settings.BodyFontFamily,
            HeadingFontFamily = settings.HeadingFontFamily,
            BackgroundImageRelativePath = settings.BackgroundImageRelativePath
        };

    private static PersistedCashPaymentConnectionSettings ToPersistedCashPaymentConnectionSettings(
        CashPaymentConnectionSettings settings) =>
        new()
        {
            StreamElementsEnabled = settings.StreamElementsEnabled,
            StreamElementsAccountId = settings.StreamElementsAccountId,
            StreamlabsEnabled = settings.StreamlabsEnabled,
            KoFiEnabled = settings.KoFiEnabled,
            KoFiConnectionMode = settings.KoFiConnectionMode,
            KoFiRelayBaseUrl = settings.KoFiRelayBaseUrl,
            KoFiRelayChannelId = settings.KoFiRelayChannelId,
            KoFiLocalPort = settings.KoFiLocalPort,
            KoFiWebhookPath = settings.KoFiWebhookPath,
            KoFiPublicWebhookUrl = settings.KoFiPublicWebhookUrl
        };

    private static CashPaymentConnectionSettings ToCashPaymentConnectionSettings(
        PersistedCashPaymentConnectionSettings settings) =>
        new()
        {
            StreamElementsEnabled = settings.StreamElementsEnabled ?? false,
            StreamElementsAccountId = settings.StreamElementsAccountId ?? string.Empty,
            StreamlabsEnabled = settings.StreamlabsEnabled ?? false,
            KoFiEnabled = settings.KoFiEnabled ?? false,
            KoFiConnectionMode = settings.KoFiConnectionMode
                ?? (string.IsNullOrWhiteSpace(settings.KoFiPublicWebhookUrl)
                    ? KoFiConnectionMode.HostedRelay
                    : KoFiConnectionMode.LocalWebhook),
            KoFiRelayBaseUrl = settings.KoFiRelayBaseUrl ?? CashPaymentConnectionSettings.DefaultKoFiRelayBaseUrl,
            KoFiRelayChannelId = settings.KoFiRelayChannelId ?? string.Empty,
            KoFiLocalPort = settings.KoFiLocalPort is >= 1 and <= 65535
                ? settings.KoFiLocalPort.Value
                : 47891,
            KoFiWebhookPath = settings.KoFiWebhookPath ?? "/kofi",
            KoFiPublicWebhookUrl = settings.KoFiPublicWebhookUrl ?? string.Empty
        };

    private static PersistedCashPaymentRule ToPersistedCashPaymentRule(CashPaymentRule rule) =>
        new()
        {
            Id = rule.Id,
            IsEnabled = rule.IsEnabled,
            Name = rule.Name,
            Provider = rule.Provider,
            MinimumAmount = rule.MinimumAmount,
            MaximumAmount = rule.MaximumAmount,
            CurrencyCode = rule.CurrencyCode,
            MessageContains = rule.MessageContains,
            RequireMessageKeyword = rule.RequireMessageKeyword,
            CooldownSeconds = rule.CooldownSeconds,
            ActionKind = rule.ActionKind,
            TriggerAction = ToPersistedRule(rule.TriggerAction),
            ScaleAction = ToPersistedAvatarScaleRule(rule.ScaleAction)
        };

    private static CashPaymentRule ToCashPaymentRule(PersistedCashPaymentRule rule)
    {
        var cashRule = new CashPaymentRule
        {
            Id = rule.Id == Guid.Empty ? Guid.NewGuid() : rule.Id,
            IsEnabled = rule.IsEnabled ?? true,
            Name = string.IsNullOrWhiteSpace(rule.Name) ? "New Cash Payment" : rule.Name.Trim(),
            Provider = Enum.IsDefined(rule.Provider) ? rule.Provider : CashPaymentProvider.StreamElements,
            MinimumAmount = rule.MinimumAmount < 0 ? 0 : rule.MinimumAmount,
            MaximumAmount = rule.MaximumAmount < 0 ? 0 : rule.MaximumAmount,
            CurrencyCode = rule.CurrencyCode ?? string.Empty,
            MessageContains = rule.MessageContains ?? string.Empty,
            RequireMessageKeyword = rule.RequireMessageKeyword ?? !string.IsNullOrWhiteSpace(rule.MessageContains),
            CooldownSeconds = Math.Max(0, rule.CooldownSeconds ?? 30),
            ActionKind = Enum.IsDefined(rule.ActionKind) ? rule.ActionKind : CashPaymentActionKind.TriggerAction,
            TriggerAction = rule.TriggerAction is null
                ? CashPaymentRule.CreateDefaultTriggerAction()
                : ToRule(rule.TriggerAction),
            ScaleAction = rule.ScaleAction is null
                ? CashPaymentRule.CreateDefaultScaleAction()
                : ToAvatarScaleRule(rule.ScaleAction)
        };
        return cashRule;
    }

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

        public bool? ChatboxXsOverlayCompatibilityMode { get; set; }

        public bool? ChatboxOscEnabled { get; set; }

        public int? ChatboxOscDelaySeconds { get; set; }

        public bool? ChatboxViewerSoundEnabled { get; set; }

        public bool? UseBroadcasterAsBotSender { get; set; }

        public bool? SupporterOverrideInfoMessageEnabled { get; set; }

        public bool? TriggerInfoAnnouncementsEnabled { get; set; }

        public int? TriggerInfoAnnouncementIntervalMinutes { get; set; }

        public bool? TriggerInfoCommandEnabled { get; set; }

        public string? TriggerInfoCommandText { get; set; }

        public int? TriggerInfoCommandCooldownSeconds { get; set; }

        public ChatCommandPermission? TriggerInfoCommandPermission { get; set; }

        public bool? UseManagedRewardTitlePrefix { get; set; }

        public bool? WorldCommandEnabled { get; set; }

        public string? WorldCommandText { get; set; }

        public int? WorldCommandCooldownSeconds { get; set; }

        public ChatCommandPermission? WorldCommandPermission { get; set; }

        public bool? ChannelPointRewardTestModeEnabled { get; set; }

        public bool? AvatarChangeCooldownOnlyModeEnabled { get; set; }

        [JsonPropertyName("permanentSwapModeEnabled")]
        public bool? PermanentSwapModeEnabled { get; set; }

        [JsonPropertyName("avatarSwapManagerUseFullRuleEditor")]
        public bool? AvatarSwapManagerUseFullRuleEditor { get; set; }

        [JsonPropertyName("avatarSwapMigrationNoticeShown")]
        public bool? AvatarSwapMigrationNoticeShown { get; set; }

        public bool? EmergencyRedeemStopEnabled { get; set; }

        public bool? DesktopModeInputLockEnabled { get; set; }

        public bool? RestartVrChatInDesktopMode { get; set; }

        [JsonPropertyName("liveFeedbackHeartbeatEnabled")]
        public bool? LiveFeedbackHeartbeatEnabled { get; set; }

        public bool? BetaApplicationUpdatesEnabled { get; set; }

        public bool? EasterEggsEnabled { get; set; }

        public bool? MainWindowTrayTipShown { get; set; }

        public string? IgnoredUpdateVersion { get; set; }

        public string? IgnoredBetaUpdateBaseVersion { get; set; }

        public bool? PauseCommandEnabled { get; set; }

        public string? PauseCommandText { get; set; }

        public bool? RedeemGroupCommandEnabled { get; set; }

        public bool? RedeemControlCommandEnabled { get; set; }

        public List<PersistedRedeemGroup>? RedeemGroups { get; set; }

        public PersistedCustomThemeSettings? CustomTheme { get; set; }

        public List<PersistedAvatarTriggerProfile>? AvatarProfiles { get; set; }

        public List<PersistedMovementRedeemSet>? MovementRedeemSets { get; set; }

        public List<PersistedTriggerRule>? GlobalMovementRules { get; set; }

        public List<PersistedTriggerRule>? GlobalOverrideRules { get; set; }

        public List<PersistedUniversalTriggerRule>? UniversalTriggers { get; set; }

        public bool? UniversalTriggersChatCollapsed { get; set; }

        public bool? UniversalTriggersRewardCollapsed { get; set; }

        public bool? UniversalTriggersBitsCollapsed { get; set; }

        public bool? UniversalTriggersSubsCollapsed { get; set; }

        public bool? UniversalTriggersFollowsCollapsed { get; set; }

        public List<PersistedAvatarScaleSet>? AvatarScaleSets { get; set; }

        public PersistedAvatarScaleMasterRewardSettings? AvatarScaleMasterReward { get; set; }

        public PersistedAvatarScaleSafetySettings? AvatarScaleSafety { get; set; }

        public List<PersistedPowerUpRule>? PowerUpRules { get; set; }

        public PersistedRewardFireSaleSettings? RewardFireSale { get; set; }

        public PersistedCashPaymentConnectionSettings? CashPayments { get; set; }

        public List<PersistedCashPaymentRule>? CashPaymentRules { get; set; }

        public List<PersistedAvatarScaleRule>? AvatarScaleRules { get; set; }

        public List<PersistedTriggerRule>? Rules { get; set; }

        public string? MasterAvatarSwapReturnId { get; set; }

        public string? MasterAvatarSwapReturnName { get; set; }

        public int AvatarChangeToAvatarSwapMigrationVersion { get; set; }

        public List<PersistedAvatarSwapProfile>? AvatarSwapProfiles { get; set; }

        public List<PersistedAvatarRouletteProfile> AvatarRouletteProfiles { get; set; } = new();

        public List<string>? CustomBlockedWords { get; set; }

        public List<string>? SuppressedBlockedWords { get; set; }
    }

    private sealed class PersistedRedeemGroup
    {
        public string? Name { get; set; }

        public string? CommandText { get; set; }

        public List<Guid>? AssignedRuleIds { get; set; }
    }

    private sealed class PersistedCustomThemeSettings
    {
        public bool? IsInitialized { get; set; }

        public string? WindowBackgroundHex { get; set; }

        public string? PanelBackgroundHex { get; set; }

        public string? PanelSecondaryHex { get; set; }

        public string? BorderHex { get; set; }

        public string? AccentHex { get; set; }

        public string? TextHex { get; set; }

        public string? MutedTextHex { get; set; }

        public string? InputBackgroundHex { get; set; }

        public string? InputBorderHex { get; set; }

        public string? SecondaryButtonHex { get; set; }

        public string? TitleBarHex { get; set; }

        public string? DangerHex { get; set; }

        public string? BodyFontFamily { get; set; }

        public string? HeadingFontFamily { get; set; }

        public string? BackgroundImageRelativePath { get; set; }
    }

    private sealed class PersistedCashPaymentConnectionSettings
    {
        public bool? StreamElementsEnabled { get; set; }

        public string? StreamElementsAccountId { get; set; }

        public bool? StreamlabsEnabled { get; set; }

        public bool? KoFiEnabled { get; set; }

        public KoFiConnectionMode? KoFiConnectionMode { get; set; }

        public string? KoFiRelayBaseUrl { get; set; }

        public string? KoFiRelayChannelId { get; set; }

        public int? KoFiLocalPort { get; set; }

        public string? KoFiWebhookPath { get; set; }

        public string? KoFiPublicWebhookUrl { get; set; }
    }

    internal sealed class PersistedCashPaymentRule
    {
        public Guid Id { get; set; }

        public bool? IsEnabled { get; set; }

        public string? Name { get; set; }

        public CashPaymentProvider Provider { get; set; }

        public decimal MinimumAmount { get; set; }

        public decimal MaximumAmount { get; set; }

        public string? CurrencyCode { get; set; }

        public string? MessageContains { get; set; }

        public bool? RequireMessageKeyword { get; set; }

        public int? CooldownSeconds { get; set; }

        public CashPaymentActionKind ActionKind { get; set; }

        public PersistedTriggerRule? TriggerAction { get; set; }

        public PersistedAvatarScaleRule? ScaleAction { get; set; }
    }

    private sealed class PersistedPowerUpRule
    {
        public Guid Id { get; set; }

        public bool? IsEnabled { get; set; }

        public string? Name { get; set; }

        public TwitchRewardSyncMode SourceMode { get; set; }

        public string? PowerUpId { get; set; }

        public string? PowerUpTitle { get; set; }

        public int BitsCost { get; set; }

        public string? Prompt { get; set; }

        public bool AvatarScoped { get; set; }

        public string? AvatarId { get; set; }

        public string? AvatarName { get; set; }

        public int? CooldownSeconds { get; set; }

        public bool FixedFloatAddEnabled { get; set; }

        public string? FixedFloatAddValue { get; set; }

        public string? FixedFloatAddMinimumValue { get; set; }

        public string? FixedFloatAddMaximumValue { get; set; }

        public PowerUpActionKind ActionKind { get; set; }

        public PersistedTriggerRule? ActionRule { get; set; }

        public PersistedAvatarScaleRule? ScaleAction { get; set; }
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

        public PersistedWorldCommandBlacklistSettings? WorldCommandBlacklist { get; set; }
    }

    private sealed class PersistedWorldCommandBlacklistSettings
    {
        public bool? IsEnabled { get; set; }
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

        public string? ThumbnailUrl { get; set; }
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

        public string? SetTriggerMasterRewardId { get; set; }

        public string? SetTriggerMasterRewardTitle { get; set; }

        public string? SetTriggerMasterRewardDescription { get; set; }

        public int SetTriggerMasterRewardCost { get; set; }

        public TwitchRewardSyncMode SetTriggerMasterRewardSyncMode { get; set; }

        public int SetTriggerMasterRewardCooldownSeconds { get; set; }

        public string? SetTriggerMasterRewardReadyColor { get; set; }

        public string? SetTriggerMasterRewardCooldownColor { get; set; }

        public bool DeleteSetTriggerMasterRewardWhenInactive { get; set; }

        public bool? UseSharedNumberedOutfitReward { get; set; }

        public bool PostOutfitChoiceListToTwitchChat { get; set; }

public List<PersistedTriggerRule>? ChannelPointRules { get; set; }

        public bool UseWardrobeMode { get; set; }

        public int WardrobeCooldownSeconds { get; set; }

        public List<PersistedWardrobeOutfit>? WardrobeOutfits { get; set; }

        public bool UseWardrobeMasterReward { get; set; }

        public string? WardrobeMasterRewardId { get; set; }

        public string? WardrobeMasterRewardTitle { get; set; }

        public int WardrobeMasterRewardCost { get; set; }

        public TwitchRewardSyncMode WardrobeMasterRewardSyncMode { get; set; }

        public int WardrobeMasterRewardCooldownSeconds { get; set; }

        public string? WardrobeMasterRewardReadyColor { get; set; }

        public string? WardrobeMasterRewardCooldownColor { get; set; }
    }

    internal sealed class PersistedAvatarSwapProfile
    {
        public Guid Id { get; set; }

        public bool IsEnabled { get; set; }

        public bool BitsMaxSwapTimeEnabled { get; set; }

        public bool SubsMaxSwapTimeEnabled { get; set; }

        public int MaxSwapTimeSeconds { get; set; } = 1800;

        [JsonPropertyName("MaxSwapTimeEnabled")]
        [Obsolete("Migrated to BitsMaxSwapTimeEnabled and SubsMaxSwapTimeEnabled. Kept for one release to preserve saved cap settings.")]
        public bool? LegacyMaxSwapTimeEnabled { get; set; }

        public string TargetAvatarId { get; set; } = string.Empty;

        public string TargetAvatarName { get; set; } = string.Empty;

        public string? TargetThumbnailUrl { get; set; }

        public DateTime CreatedAt { get; set; }

        public DateTime UpdatedAt { get; set; }

        public List<PersistedTriggerRule>? ChannelPointRules { get; set; }

        public List<PersistedTriggerRule>? BitsRules { get; set; }

        public List<PersistedTriggerRule>? SubsRules { get; set; }

        public List<PersistedCashPaymentRule>? PaymentRules { get; set; }

        [Obsolete("Migrated to BitsRules and SubsRules in V4. Kept for loading legacy saves.")]
        public List<PersistedTriggerRule>? BitsSubsRules { get; set; }

        [JsonPropertyName("rouletteRules")]
        [Obsolete("Migrated to AvatarRouletteProfiles in V4. Kept for loading legacy saves.")]
        public List<PersistedTriggerRule>? RouletteRules { get; set; }
    }

    internal sealed class PersistedAvatarRouletteProfile
    {
        public Guid Id { get; set; }
        public string Name { get; set; } = string.Empty;
        public bool IsEnabled { get; set; } = true;
        public DateTime CreatedAt { get; set; }
        public DateTime UpdatedAt { get; set; }
        public List<PersistedRouletteAvatarEntry> Pool { get; set; } = new();
        public string? ReturnAvatarId { get; set; }
        public string? ReturnAvatarName { get; set; }
        public List<PersistedTriggerRule> Triggers { get; set; } = new();
    }

    internal sealed class PersistedRouletteAvatarEntry
    {
        public string AvatarId { get; set; } = string.Empty;
        public string AvatarName { get; set; } = string.Empty;
        public string? ThumbnailUrl { get; set; }
    }

    private sealed class PersistedWardrobeOutfit
    {
        public Guid Id { get; set; }

        public bool IsEnabled { get; set; }

        public string? Name { get; set; }

        public int ActiveTimeSeconds { get; set; }

        public string? TwitchRewardId { get; set; }

        public string? TwitchRewardTitle { get; set; }

        public string? TwitchRewardCost { get; set; }

        public string? TwitchRewardDescription { get; set; }

        public TwitchRewardSyncMode TwitchRewardSyncMode { get; set; }

        public string? ManagedRewardReadyColor { get; set; }

        public string? ManagedRewardCooldownColor { get; set; }

        public bool DeleteManagedRewardWhenInactive { get; set; }

        public string? ChatCommandText { get; set; }

        public List<PersistedWardrobeSnapshotParam>? SnapshotParams { get; set; }
    }

    private sealed class PersistedWardrobeSnapshotParam
    {
        public Guid Id { get; set; }

        public string? ParameterName { get; set; }

        public OscParameterType ParameterType { get; set; }

        public string? SetValue { get; set; }
    }

    internal sealed class PersistedTriggerRule
    {
        public Guid Id { get; set; }

        public bool IsEnabled { get; set; }

        public string? Name { get; set; }

        public TwitchTriggerType TriggerType { get; set; }

        public string? MatchText { get; set; }

        public string? ChannelPointRewardId { get; set; }

        public string? ChannelPointRewardTitle { get; set; }

        public string? ChannelPointRewardDescription { get; set; }

        public int ChannelPointRewardCost { get; set; }

        public TwitchRewardSyncMode RewardSyncMode { get; set; }

        public string? ManagedRewardReadyColor { get; set; }

        public string? ManagedRewardCooldownColor { get; set; }

        public bool DeleteManagedRewardWhenInactive { get; set; }

        public bool ChatCommandEnabled { get; set; }

        public string? ChatCommandText { get; set; }

        public ChatCommandPermission ChatCommandPermission { get; set; }

        public int MinimumAmount { get; set; }

        public bool AmountScaledDurationEnabled { get; set; }

        public bool AddBitsToSwapTime { get; set; }

        public int AmountUnitsPerDuration { get; set; }

        public int SecondsPerAmountUnit { get; set; }

        public int BitsAmountUnitsPerDuration { get; set; }

        public int BitsSecondsPerAmountUnit { get; set; }

        public int SubscriptionsAmountUnitsPerDuration { get; set; }

        public int SubscriptionsSecondsPerAmountUnit { get; set; }

        public int SubscriptionTier1SecondsPerSub { get; set; }

        public int SubscriptionTier2SecondsPerSub { get; set; }

        public int SubscriptionTier3SecondsPerSub { get; set; }

        public bool SubscriptionTier1Enabled { get; set; } = true;

        public bool SubscriptionTier2Enabled { get; set; } = true;

        public bool SubscriptionTier3Enabled { get; set; } = true;

        public bool MaxAccumulatedDurationEnabled { get; set; }

        public int MaxAccumulatedDurationSeconds { get; set; }

        public bool ExtendCurrentActivity { get; set; }

        public double ExtendSeconds { get; set; }

        public OscActionType ActionType { get; set; }

        public PlayerMovementDirection MovementDirection { get; set; }

        public string? ParameterName { get; set; }

        public OscParameterType ParameterType { get; set; }

        public IntZeroDurationMode IntZeroDurationMode { get; set; }

        public string? ParameterValue { get; set; }

        public FloatValueMode FloatValueMode { get; set; }

        public double FloatTransitionInSeconds { get; set; }
        public double FloatTransitionOutSeconds { get; set; }
        // Legacy field kept for migration. Read on deserialize so old saves with
        // this key are picked up by the ToRule migration (which copies the value
        // into FloatTransitionInSeconds and FloatTransitionOutSeconds and then
        // clears it). Ignored on write once the value is the default 0.0, so new
        // save files do not include this key.
        [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingDefault)]
        public double FloatTransitionSeconds { get; set; }

        public FloatActionMode FloatActionMode { get; set; } = FloatActionMode.Set;

        public double FloatRangeMin { get; set; }

        public double FloatRangeMax { get; set; } = 1.0;

        public double FloatCycleStep { get; set; } = 0.1;

        public double FloatAddAmount { get; set; } = 0.1;

        public double FloatSubtractAmount { get; set; } = 0.1;

        public double FloatAddSubtractAmount { get; set; } = 0.1;

        public double FloatMultiplyFactor { get; set; } = 1.5;

        public double FloatToggleOnValue { get; set; } = 1.0;

        public double FloatToggleOffValue { get; set; }

        public int FloatGlitchyIntervalMs { get; set; } = 200;

        public double FloatPulseSeconds { get; set; } = 0.5;

        public FloatClampMode FloatClampMode { get; set; } = FloatClampMode.ZeroToOne;

        public bool HideRewardWhenFloatMaxReached { get; set; }

        public bool HideRewardWhenFloatMinReached { get; set; }

        public string? AvatarChangeTargetId { get; set; }

        public string? AvatarTargetName { get; set; }

        public string? ResetValue { get; set; }

        public string? AvatarChangeResetId { get; set; }

        public string? ResetAvatarName { get; set; }

        public List<string>? AvatarRouletAvatarIds { get; set; }

        public List<string>? AvatarRouletAvatarNames { get; set; }

        public int RangeMinimum { get; set; }

        public int RangeMaximum { get; set; }

        public double DurationSeconds { get; set; }

        public int CooldownSeconds { get; set; }

        public bool SharedRewardChoiceEnabled { get; set; }

        public int SharedRewardChoiceNumber { get; set; }

        public string? SharedRewardHelpText { get; set; }

        public string? SupporterKeywordText { get; set; }

        public bool BitsKeywordEnabled { get; set; }

        public Guid ActiveFloatBoostRewardOwnerId { get; set; }

        public bool ActiveFloatBoostRewardEnabled { get; set; }

        public string? ActiveFloatBoostRewardId { get; set; }

        public string? ActiveFloatBoostRewardTitle { get; set; }

        public string? ActiveFloatBoostRewardDescription { get; set; }

        public int ActiveFloatBoostRewardCost { get; set; }

        public int ActiveFloatBoostRewardCooldownSeconds { get; set; }

        public string? ActiveFloatBoostRewardReadyColor { get; set; }

        public string? ActiveFloatBoostRewardCooldownColor { get; set; }

        public string? ActiveFloatBoostAddValue { get; set; }

        public string? ActiveFloatBoostMinimumValue { get; set; }

        public string? ActiveFloatBoostMaximumValue { get; set; }

        public bool SupporterFloatAddEnabled { get; set; }

        public string? SupporterFloatAddMinimumValue { get; set; }

        public string? SupporterFloatAddMaximumValue { get; set; }

        public List<PersistedSupporterFloatAddRange>? SupporterFloatAddRanges { get; set; }

        public List<PersistedSetTriggerAction>? SetTriggerActions { get; set; }

        public SetTriggerRestoreMode SetTriggerRestoreMode { get; set; }

        public SpecialRulePairingMode SpecialRulePairingMode { get; set; }

        public List<Guid>? TemporarilyDisabledRuleIds { get; set; }

        public string? BotMessageTemplate { get; set; }
        public bool ReturnToPreviousAvatar { get; set; }
        public bool PermanentAvatarChange { get; set; }
        public bool CooldownOnlyAvatarChange { get; set; }
        public bool IsGiftSubscription { get; set; }
    }

    internal sealed class PersistedSupporterFloatAddRange
    {
        public int MinimumAmount { get; set; }

        public int MaximumAmount { get; set; }

        public string? AddValue { get; set; }
    }

    internal sealed class PersistedSetTriggerAction
    {
        public Guid Id { get; set; }

        public string? ParameterName { get; set; }

        public OscParameterType ParameterType { get; set; }

        public string? ParameterValue { get; set; }
    }

    private sealed class PersistedUniversalTriggerRule
    {
        public Guid Id { get; set; }

        public bool IsEnabled { get; set; }

        public string? Name { get; set; }

        public UniversalTriggerType TriggerType { get; set; }

        public bool ChatCommandEnabled { get; set; }

        public string? CommandText { get; set; }

        public ChatCommandPermission ChatCommandPermission { get; set; }

        public string? RewardId { get; set; }

        public string? RewardTitle { get; set; }

        public string? RewardDescription { get; set; }

        public int RewardCost { get; set; }

        public int RewardCooldownSeconds { get; set; }

        public TwitchRewardSyncMode RewardSyncMode { get; set; }

        public string? ManagedRewardReadyColor { get; set; }

        public string? ManagedRewardCooldownColor { get; set; }

        public bool DeleteManagedRewardWhenInactive { get; set; }

        public int MinimumBits { get; set; }

        public int MaximumBits { get; set; }

        public string? SubscriptionTier { get; set; }

        public int MinimumMonths { get; set; }

        public int MaximumMonths { get; set; }

        public int GlobalDelaySeconds { get; set; }

        public int UserDelaySeconds { get; set; }

        public bool ExecuteRandomAction { get; set; }

        public string? ImportSource { get; set; }

        public string? ImportIdentity { get; set; }

        public List<PersistedUniversalTriggerAction>? Actions { get; set; }
    }

    private sealed class PersistedAvatarScaleSet
    {
        public Guid Id { get; set; }

        public string? Name { get; set; }

        public List<PersistedAvatarScaleRule>? ScaleRules { get; set; }
    }

    private sealed class PersistedMovementRedeemSet
    {
        public Guid Id { get; set; }

        public string? Name { get; set; }

        public List<PersistedTriggerRule>? MovementRules { get; set; }
    }

    private sealed class PersistedAvatarScaleMasterRewardSettings
    {
        public bool IsEnabled { get; set; }

        public string? RewardId { get; set; }

        public string? RewardTitle { get; set; }

        public string? RewardDescription { get; set; }

        public int RewardCost { get; set; }

        public TwitchRewardSyncMode RewardSyncMode { get; set; }

        public int UnlockDurationSeconds { get; set; }

        public int CooldownSeconds { get; set; }

        public string? ManagedRewardReadyColor { get; set; }

        public string? ManagedRewardCooldownColor { get; set; }

        public bool DeleteMasterRewardWhenInactive { get; set; }

        public bool? FreeChildRewardSlotsWhenLocked { get; set; }

        public bool PreventAvatarChangesDuringActiveScaling { get; set; }
    }

    private sealed class PersistedRewardFireSaleSettings
    {
        public bool IsEnabled { get; set; }

        public bool? CountBits { get; set; }

        public bool? CountManagedRewards { get; set; }

        public bool? DiscountManagedPowerUpsEnabled { get; set; }

        public bool FundingRewardEnabled { get; set; }

        public string? FundingRewardId { get; set; }

        public string? FundingRewardTitle { get; set; }

        public string? FundingRewardDescription { get; set; }

        public int FundingRewardCost { get; set; }

        public int FundingRewardCooldownSeconds { get; set; }

        public string? FundingRewardReadyColor { get; set; }

        public string? FundingRewardCooldownColor { get; set; }

        public int RewardPointsPerProgressUnit { get; set; }

        public bool? MultiTierEnabled { get; set; }

        public List<PersistedRewardFireSaleTier>? Tiers { get; set; }

        public RewardFireSaleMode SaleMode { get; set; }

        public int TemporaryDurationSeconds { get; set; }

        public long CurrentProgress { get; set; }

        public bool IsSaleActive { get; set; }

        public int ActiveDiscountPercent { get; set; }

        public int ActiveTierGoalAmount { get; set; }

        public DateTimeOffset? ActiveUntilUtc { get; set; }
    }

    private sealed class PersistedRewardFireSaleTier
    {
        public Guid Id { get; set; }

        public int GoalAmount { get; set; }

        public int DiscountPercent { get; set; }
    }

    private sealed class PersistedAvatarScaleSafetySettings
    {
        public double CurrentMinimumHeightMeters { get; set; }

        public double CurrentMaximumHeightMeters { get; set; }
    }

    internal sealed class PersistedAvatarScaleRule
    {
        public Guid Id { get; set; }

        public bool IsEnabled { get; set; }

        public string? Name { get; set; }

        public AvatarScaleTriggerType TriggerType { get; set; }

        public bool ChatCommandEnabled { get; set; }

        public string? CommandText { get; set; }

        public ChatCommandPermission ChatCommandPermission { get; set; }

        public string? RewardId { get; set; }

        public string? RewardTitle { get; set; }

        public string? RewardDescription { get; set; }

        public int RewardCost { get; set; }

        public TwitchRewardSyncMode RewardSyncMode { get; set; }

        public string? ManagedRewardReadyColor { get; set; }

        public string? ManagedRewardCooldownColor { get; set; }

        public bool DeleteManagedRewardWhenInactive { get; set; }

        public int MinimumBits { get; set; }

        public int MaximumBits { get; set; }

        public string? SubscriptionTier { get; set; }

        public int MinimumMonths { get; set; }

        public int MaximumMonths { get; set; }

        public int CooldownSeconds { get; set; }

        public List<Guid>? TemporarilyDisabledScaleRuleIds { get; set; }

        public AvatarScaleMode ScaleMode { get; set; }

        public double TargetHeightMeters { get; set; }

        public double MinimumHeightMeters { get; set; }

        public double MaximumHeightMeters { get; set; }

        public double RelativeHeightMeters { get; set; }

        public double RelativeMinimumHeightMeters { get; set; }

        public double RelativeMaximumHeightMeters { get; set; }

        public bool HideRewardWhenMinimumHeightReached { get; set; } = true;

        public bool HideRewardWhenMaximumHeightReached { get; set; } = true;

        public double HeightMultiplier { get; set; }

        public int MultiplierDirectionId { get; set; }

        public int RelativeHeightDirectionId { get; set; }

        public double GlitchyTransitionSeconds { get; set; } = 0.4;

        public AvatarScalePreset Preset { get; set; }

        public double ActiveTimeSeconds { get; set; }

        public bool ExtendCurrentActivity { get; set; }

        public double ExtendSeconds { get; set; }

        public AvatarScaleRestoreMode RestoreMode { get; set; }

        public double RestoreHeightMeters { get; set; }

        public double SmoothTransitionSeconds { get; set; }

        public double SetHeightTransitionSeconds { get; set; }
        public double RandomHeightTransitionSeconds { get; set; }
        public double RelativeHeightTransitionSeconds { get; set; }
        public double MultiplierTransitionSeconds { get; set; }
        public double PresetTransitionSeconds { get; set; }
        public double GlitchyRandomHeightTransitionSeconds { get; set; }
        public double SupporterGrowthTransitionSeconds { get; set; }

        public bool AdvancedRangeEnabled { get; set; }

        public bool BypassVrChatScaleLimits { get; set; }

        public int SupporterGrowthInactivityTimerSeconds { get; set; }

        public bool SupporterGrowthAllowRewardScaleOverlay { get; set; } = true;

        public bool SupporterGrowthRequireCheerKeyword { get; set; }

        public int SupporterGrowthBitsTimerUnit { get; set; }

        public int SupporterGrowthSecondsPerBitsUnit { get; set; }

        public int SupporterGrowthTier1Seconds { get; set; }

        public int SupporterGrowthTier2Seconds { get; set; }

        public int SupporterGrowthTier3Seconds { get; set; }

        public int SupporterGrowthSoftCapSeconds { get; set; }

        public int SupporterGrowthSoftCapMultiplierPercent { get; set; }

        public int SupporterGrowthMaxPaidTimeSeconds { get; set; }

        public string? SupporterGrowthGrowKeyword { get; set; }

        public string? SupporterGrowthShrinkKeyword { get; set; }

        public double SupporterGrowthTier1HeightMeters { get; set; }

        public double SupporterGrowthTier2HeightMeters { get; set; }

        public double SupporterGrowthTier3HeightMeters { get; set; }

        public List<PersistedAvatarScaleBitGrowthRange>? SupporterGrowthBitRanges { get; set; }
    }

    internal sealed class PersistedAvatarScaleBitGrowthRange
    {
        public int MinimumBits { get; set; }

        public int MaximumBits { get; set; }

        public double HeightAddedMeters { get; set; }
    }

    private sealed class PersistedUniversalTriggerAction
    {
        public Guid Id { get; set; }

        public string? OscAddress { get; set; }

        public UniversalTriggerValueKind ValueKind { get; set; }

        public string? TargetValue { get; set; }

        public string? DefaultValue { get; set; }

        public double DurationSeconds { get; set; }

        public bool AddToQueue { get; set; }

        public string? ImportGroupKey { get; set; }
    }
}
