using System.Diagnostics;
using System.Globalization;
using System.Net.Http;
using System.Text;
using System.Text.Json;
using VrcTwitchOscBridge.Models;

namespace VrcTwitchOscBridge.Services;

public enum OscSessionMode
{
    Stopped,
    OscOnly,
    FullBridge
}

/// <summary>
/// Long-running Twitch/OSC runtime for Crystal Relay.
/// This class owns the live bridge loop: EventSub listening, cooldowns, lockouts,
/// queued rule execution, chat relay, and OSC sends into VRChat.
/// </summary>
public sealed class BridgeCoordinator : IAsyncDisposable
{
    private static readonly TimeSpan AccessTokenRefreshLeadTime = TimeSpan.FromMinutes(15);
    private static readonly TimeSpan PublicRefreshSessionWindow = TimeSpan.FromDays(30);
    private static readonly TimeSpan ChatboxRelayUnavailableLogThrottle = TimeSpan.FromSeconds(20);
    private static readonly TimeSpan ChatboxRelayBlockedLogThrottle = TimeSpan.FromSeconds(10);
    private static readonly TimeSpan RedeemPauseLogThrottle = TimeSpan.FromSeconds(15);
    private static readonly TimeSpan MovementSoftLockPulseInterval = TimeSpan.FromMilliseconds(200);
    private static readonly TimeSpan JumpPulsePressDuration = TimeSpan.FromMilliseconds(120);
    private static readonly TimeSpan JumpPulseInterval = TimeSpan.FromMilliseconds(650);
    private static readonly TimeSpan RecentMessageRetention = TimeSpan.FromMinutes(30);
    private static readonly TimeSpan RecentMessagePruneInterval = TimeSpan.FromMinutes(1);
    private static readonly TimeSpan ThirdPartyChatEmoteRefreshInterval = TimeSpan.FromHours(6);
    private static readonly TimeSpan ThirdPartyChatEmoteRetryInterval = TimeSpan.FromMinutes(10);
    private static readonly TimeSpan ChatEmoteDiagnosticLogThrottle = TimeSpan.FromSeconds(15);
    private const int TwitchChatMessageMaxCharacters = 450;
    private const int VrChatChatboxMaxCharacters = 144;
    private const int VrChatChatboxMaxLines = 9;
    private const int MaxChatEmoteImageUrlCacheEntries = 2048;
    private const int MaxCachedChatEmoteSetIds = 512;
    private const int MaxThirdPartyChatEmoteEntries = 8192;
    private const int AvatarScaleSmoothUpdatesPerSecond = 60;
    private const int AvatarScaleSmoothMaxSteps = 600;
    private const string BitsOutfitSetTriggerLaneKey = "set-trigger-bits-outfit";
    private static readonly TimeSpan AvatarScaleQueuePollDelay = TimeSpan.FromMilliseconds(250);
    private static readonly TimeSpan SetTriggerDiffObservationDelay = TimeSpan.FromSeconds(70);
    private static readonly TimeSpan SetTriggerPacketSpacing = TimeSpan.FromMilliseconds(80);
    private static readonly TimeSpan TriggerInfoAnnouncementPollInterval = TimeSpan.FromSeconds(15);
    private static readonly HttpClient ThirdPartyChatEmoteHttpClient = CreateThirdPartyChatEmoteHttpClient();
    private static readonly string[] ManagedSubscriptionTypes =
    [
        "channel.channel_points_custom_reward_redemption.add",
        "channel.cheer",
        "channel.subscribe",
        "channel.subscription.gift",
        "channel.subscription.message",
        "channel.follow",
        "channel.chat.message",
        "stream.online",
        "stream.offline"
    ];

    private static string T(string sourceText) => LocalizationService.Translate(sourceText);

    private static string TF(string sourceFormat, params object[] args) => LocalizationService.Format(sourceFormat, args);

    private static HttpClient CreateThirdPartyChatEmoteHttpClient()
    {
        var client = new HttpClient
        {
            Timeout = TimeSpan.FromSeconds(4)
        };
        client.DefaultRequestHeaders.UserAgent.ParseAdd("CrystalRelay/2.8.9 (+https://github.com/seluvia/crystal-relay-public)");
        return client;
    }

    private readonly TwitchApiClient twitchApiClient = new();
    private readonly VrChatOscClient vrChatOscClient = new();
    private readonly OscRouterService oscRouterService = new();
    private readonly VrChatLocalAvatarDataService vrChatLocalAvatarDataService = new();
    private readonly DesktopInputLockService desktopInputLockService;
    private readonly object stateGate = new();
    // Runtime state for cooldowns, queued redeems, movement lanes, active timed resets,
    // and the temporary disable-pairing system used by avatar-set redeems.
    private readonly Dictionary<Guid, DateTimeOffset> cooldowns = [];
    private readonly Dictionary<Guid, Queue<QueuedRuleTrigger>> queuedTriggers = [];
    private readonly HashSet<Guid> drainingQueuedRules = [];
    private readonly Dictionary<string, ActiveMovementLaneState> actionLanes = [];
    private readonly Dictionary<Guid, ActiveMovementSoftLockState> activeMovementLocks = [];
    private readonly Dictionary<Guid, ActiveDesktopInputLockState> activeDesktopInputLocks = [];
    private readonly Dictionary<string, Queue<QueuedLaneAction>> queuedLaneActions = [];
    private readonly HashSet<string> drainingQueuedLanes = [];
    private readonly Dictionary<Guid, PendingResetState> pendingResets = [];
    private readonly Dictionary<string, DateTimeOffset> recentMessageIds = [];
    private readonly Dictionary<string, OscObservedValue> avatarParameterValues = [];
    private readonly Dictionary<string, OscObservedValue> avatarScaleValues = new(StringComparer.Ordinal);
    private readonly Dictionary<string, bool> localInstantToggleStates = [];
    private readonly Dictionary<string, string> chatBadgeImageUrls = [];
    private readonly Dictionary<string, string> chatEmoteImageUrls = [];
    private readonly Queue<string> chatEmoteImageUrlInsertionOrder = [];
    private readonly HashSet<string> cachedChatEmoteSetIds = [];
    private readonly Queue<string> cachedChatEmoteSetIdInsertionOrder = [];
    private readonly SemaphoreSlim thirdPartyChatEmoteRefreshGate = new(1, 1);
    private readonly Dictionary<string, string> thirdPartyChatEmoteImageUrls = new(StringComparer.Ordinal);
    private readonly Queue<string> thirdPartyChatEmoteCodeInsertionOrder = [];
    private IReadOnlyDictionary<char, IReadOnlyList<ThirdPartyChatEmoteEntry>> thirdPartyChatEmoteIndex =
        new Dictionary<char, IReadOnlyList<ThirdPartyChatEmoteEntry>>();
    private readonly Dictionary<Guid, ActiveRuleLockoutState> activeRuleLockouts = [];
    private readonly Dictionary<Guid, ActiveRuleLockoutState> activeAvatarSwitchRuleLockouts = [];
    private readonly Dictionary<Guid, string> lastAvatarRouletResultIds = [];
    private readonly Dictionary<Guid, CancellationTokenSource> cooldownStateNotifications = [];
    private readonly Dictionary<Guid, CancellationTokenSource> lockoutStateNotifications = [];
    private readonly Dictionary<Guid, CancellationTokenSource> avatarSwitchLockoutStateNotifications = [];
    private readonly Queue<QueuedChatboxRelayLine> queuedChatboxRelayMessages = [];
    private readonly List<QueuedSupporterOverrideState> queuedSupporterOverrides = [];
    private readonly HashSet<Guid> supporterOverrideBlockedRuleIds = [];
    private readonly Dictionary<Guid, DateTimeOffset> universalTriggerGlobalDelays = [];
    private readonly Dictionary<string, DateTimeOffset> universalTriggerUserDelays = new(StringComparer.Ordinal);
    private readonly Dictionary<Guid, SemaphoreSlim> universalTriggerQueueGates = [];
    private readonly Dictionary<Guid, ActiveAvatarScaleSupporterGrowthState> avatarScaleSupporterGrowthStates = [];
    private readonly Dictionary<Guid, DateTimeOffset> activeAvatarScaleEffects = [];
    private readonly Dictionary<Guid, CancellationTokenSource> avatarScaleEffectStateNotifications = [];
    private readonly Dictionary<Guid, ActiveAvatarScaleHeightSessionState> activeAvatarScaleHeightSessions = [];
    private readonly Dictionary<string, PendingAvatarScaleHeightRestoreState> pendingAvatarScaleHeightRestores = new(StringComparer.Ordinal);
    private readonly Queue<QueuedAvatarScaleOperation> queuedAvatarScaleOperations = [];
    // Avatar scale writes share one OSC parameter, so operations are ordered by priority:
    // transition lock, active supporter growth, live redeem, test/simulated effect, then idle/default restore.
    private ActiveAvatarScaleOperationState? activeAvatarScaleOperation;
    private ActiveAvatarScaleRestoreSequenceState? activeAvatarScaleRestoreSequence;
    private CancellationTokenSource? avatarScaleRestoreSequenceCancellation;
    private bool drainingQueuedAvatarScaleOperations;

    private CancellationTokenSource? runtimeCancellation;
    private CancellationTokenSource? chatboxRelayCancellation;
    private Task? runtimeTask;
    private Task? chatboxRelayTask;
    private OscSessionMode oscSessionMode = OscSessionMode.Stopped;
    private BridgeRuntimeConfiguration? activeConfiguration;
    private RuntimeRuleIndex activeRuleIndex = RuntimeRuleIndex.Empty;
    private TwitchAccountSnapshot? broadcaster;
    private TwitchAccountSnapshot? bot;
    private string currentVrChatAvatarId = string.Empty;
    private string currentSharedReturnAvatarId = string.Empty;
    private string currentSharedReturnAvatarName = string.Empty;
    private bool isBroadcasterLive;
    private DateTimeOffset nextTriggerInfoAnnouncementAt = DateTimeOffset.MinValue;
    private DateTimeOffset nextChatboxRelayUnavailableLogAt = DateTimeOffset.MinValue;
    private DateTimeOffset nextChatboxRelayBlockedLogAt = DateTimeOffset.MinValue;
    private DateTimeOffset nextRedeemPauseLogAt = DateTimeOffset.MinValue;
    private DateTimeOffset nextRecentMessagePruneAt = DateTimeOffset.MinValue;
    private DateTimeOffset nextThirdPartyChatEmoteRefreshAt = DateTimeOffset.MinValue;
    private DateTimeOffset nextChatEmoteDiagnosticLogAt = DateTimeOffset.MinValue;
    private ActiveSupporterOverrideState? activeSupporterOverride;
    private DateTimeOffset avatarScaleMasterUnlockUntil = DateTimeOffset.MinValue;
    private DateTimeOffset avatarScaleMasterCooldownUntil = DateTimeOffset.MinValue;
    private CancellationTokenSource? avatarScaleMasterUnlockNotification;
    private CancellationTokenSource? avatarScaleMasterCooldownNotification;
    private int suppressedChatEmoteDiagnosticLogs;
    private long nextSupporterOverrideQueueOrder;
    private long nextAvatarScaleOperationId;
    private long nextAvatarScaleRestoreSequenceId;

    public BridgeCoordinator(DesktopInputLockService desktopInputLockService)
    {
        this.desktopInputLockService = desktopInputLockService;
        oscRouterService.LogWritten += WriteLog;
        oscRouterService.ObservedValueReceived += ObserveOscValue;
        desktopInputLockService.EmergencyUnlockTriggered += HandleEmergencyDesktopInputUnlock;
    }

    public event Action<string>? LogWritten;

    public event Action<string>? StatusChanged;

    public event Action<BridgeAccountRole, TwitchAccountSnapshot>? AccountUpdated;

    public event Action<BridgeChatMessage>? ChatMessageReceived;

    public event Action<string>? VrChatAvatarChanged;

    public event Action<string, string>? SharedReturnAvatarChanged;

    public event Action<bool>? StreamStateChanged;

    public event Action? ManagedRewardAvailabilityChanged;

    public event Action? AvatarScaleStatusChanged;

    public bool IsRunning => runtimeTask is { IsCompleted: false };

    public bool IsOscActive => oscRouterService.IsRunning;

    public bool HasDiscoveredVrChat => oscRouterService.HasDiscoveredVrChat;

    public OscSessionMode SessionMode => oscSessionMode;

    public OscDiscoveryState DiscoveryState => oscRouterService.DiscoveryState;

    public IReadOnlyCollection<Guid> GetTemporarilyDisabledRuleIds()
    {
        lock (stateGate)
        {
            return [.. GetTemporarilyDisabledRuleIdsLocked(DateTimeOffset.UtcNow)];
        }
    }

    public IReadOnlyCollection<Guid> GetActiveTimedRuleIds()
    {
        lock (stateGate)
        {
            var activeRuleIds = new HashSet<Guid>();

            foreach (var ruleId in pendingResets.Keys)
            {
                activeRuleIds.Add(ruleId);
            }

            foreach (var movementLock in activeMovementLocks.Values)
            {
                activeRuleIds.Add(movementLock.RuleId);
            }

            foreach (var desktopLock in activeDesktopInputLocks.Values)
            {
                activeRuleIds.Add(desktopLock.RuleId);
            }

            if (activeSupporterOverride is not null)
            {
                activeRuleIds.Add(activeSupporterOverride.Rule.Id);
            }

            return [.. activeRuleIds];
        }
    }

    public IReadOnlyCollection<Guid> GetRulesOnCooldownIds()
    {
        lock (stateGate)
        {
            var now = DateTimeOffset.UtcNow;
            List<Guid>? expiredRuleIds = null;
            foreach (var cooldown in cooldowns)
            {
                if (cooldown.Value <= now)
                {
                    expiredRuleIds ??= [];
                    expiredRuleIds.Add(cooldown.Key);
                }
            }

            if (expiredRuleIds is not null)
            {
                foreach (var expiredRuleId in expiredRuleIds)
                {
                    cooldowns.Remove(expiredRuleId);
                }
            }

            return [.. cooldowns.Keys];
        }
    }

    public IReadOnlyCollection<Guid> GetActiveAvatarScaleEffectRuleIds()
    {
        lock (stateGate)
        {
            var now = DateTimeOffset.UtcNow;
            List<Guid>? expiredRuleIds = null;
            foreach (var activeEffect in activeAvatarScaleEffects)
            {
                if (activeEffect.Value <= now)
                {
                    expiredRuleIds ??= [];
                    expiredRuleIds.Add(activeEffect.Key);
                }
            }

            if (expiredRuleIds is not null)
            {
                foreach (var expiredRuleId in expiredRuleIds)
                {
                    activeAvatarScaleEffects.Remove(expiredRuleId);
                }
            }

            return [.. activeAvatarScaleEffects.Keys];
        }
    }

    public IReadOnlyCollection<Guid> GetQueuedAvatarScaleRuleIds()
    {
        lock (stateGate)
        {
            return queuedAvatarScaleOperations
                .Where(operation => !operation.IsTest)
                .Select(operation => operation.Rule.Id)
                .Where(ruleId => ruleId != Guid.Empty)
                .Distinct()
                .ToArray();
        }
    }

    public bool IsAvatarScaleMasterUnlockActive()
    {
        lock (stateGate)
        {
            return avatarScaleMasterUnlockUntil > DateTimeOffset.UtcNow;
        }
    }

    public bool IsAvatarScaleMasterRewardOnCooldown()
    {
        lock (stateGate)
        {
            var now = DateTimeOffset.UtcNow;
            return avatarScaleMasterUnlockUntil > now || avatarScaleMasterCooldownUntil > now;
        }
    }

    public bool IsAvatarChangeTransitionActive()
    {
        lock (stateGate)
        {
            foreach (var reset in pendingResets.Values)
            {
                if (!string.IsNullOrWhiteSpace(reset.AvatarChangeResetId))
                {
                    return true;
                }
            }

            return !string.IsNullOrWhiteSpace(activeSupporterOverride?.Action.AvatarResetId);
        }
    }

    public void UpdateCurrentVrChatAvatar(string avatarId)
    {
        SetCurrentVrChatAvatar(avatarId, notify: false);
    }

    public async Task StartAsync(BridgeRuntimeConfiguration configuration)
    {
        if (IsRunning)
        {
            return;
        }

        ValidateConfiguration(configuration);
        var upgradingOscOnlySession = oscSessionMode == OscSessionMode.OscOnly && oscRouterService.IsRunning;

        SetActiveConfiguration(configuration);
        RefreshSupporterOverrideBlockedRuleIds(configuration.Rules);
        SetCurrentVrChatAvatar(configuration.CurrentVrChatAvatarId, notify: false);
        SetSharedReturnAvatar(configuration.SharedReturnAvatarId, configuration.SharedReturnAvatarName, notify: false);
        broadcaster = await EnsureAccountReadyAsync(configuration.Broadcaster, TwitchScopes.BroadcasterRequired, BridgeAccountRole.Broadcaster, CancellationToken.None);
        await RefreshBroadcasterLiveStateAsync(CancellationToken.None);

        if (configuration.Bot is not null)
        {
            try
            {
                bot = await EnsureAccountReadyAsync(configuration.Bot, TwitchScopes.Bot, BridgeAccountRole.Bot, CancellationToken.None);
            }
            catch (Exception ex)
            {
                bot = null;
                WriteLog($"Bot account is not ready yet, so chat announcements are disabled for now. {ex.Message}");
            }
        }

        await oscRouterService.StartAsync(configuration.Rules, CancellationToken.None);

        runtimeCancellation ??= new CancellationTokenSource();
        runtimeTask = Task.Run(() => RunBridgeAsync(runtimeCancellation.Token));
        oscSessionMode = OscSessionMode.FullBridge;

        if (upgradingOscOnlySession)
        {
            WriteLog("Crystal Relay reused the current OSCQuery session while the Twitch bridge came online.");
        }
    }

    public async Task StartOscOnlyAsync(BridgeRuntimeConfiguration configuration, CancellationToken cancellationToken = default)
    {
        var wasAlreadyRunning = oscRouterService.IsRunning;
        SetActiveConfiguration(configuration);
        RefreshSupporterOverrideBlockedRuleIds(configuration.Rules);
        SetCurrentVrChatAvatar(configuration.CurrentVrChatAvatarId, notify: false);
        SetSharedReturnAvatar(configuration.SharedReturnAvatarId, configuration.SharedReturnAvatarName, notify: false);
        await oscRouterService.StartAsync(configuration.Rules, cancellationToken);
        oscRouterService.UpdateRuleSubscriptions(configuration.Rules);
        runtimeCancellation ??= CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        oscSessionMode = OscSessionMode.OscOnly;

        if (!wasAlreadyRunning)
        {
            WriteLog("OSC test mode is live. Twitch login is not required for Test Rule.");
            StatusChanged?.Invoke("OSC test mode is live.");
        }
    }

    public bool CanApplyConfigurationWithoutRestart(BridgeRuntimeConfiguration configuration)
    {
        if (!IsRunning || activeConfiguration is null)
        {
            return false;
        }

        return StringComparer.Ordinal.Equals(activeConfiguration.Broadcaster.UserId, configuration.Broadcaster.UserId);
    }

    private void SetActiveConfiguration(BridgeRuntimeConfiguration configuration)
    {
        activeConfiguration = configuration;
        activeRuleIndex = RuntimeRuleIndex.Create(configuration.Rules);
        RefreshAvatarScaleMasterStateForConfiguration(configuration.AvatarScaleMasterReward);
    }

    private void ClearActiveConfiguration()
    {
        activeConfiguration = null;
        activeRuleIndex = RuntimeRuleIndex.Empty;
        RefreshAvatarScaleMasterStateForConfiguration(null);
    }

    public void ApplyConfiguration(BridgeRuntimeConfiguration configuration)
    {
        ValidateConfiguration(configuration);

        var wasPaused = activeConfiguration?.EmergencyRedeemStopEnabled == true;
        SetActiveConfiguration(configuration);
        RefreshSupporterOverrideBlockedRuleIds(configuration.Rules);
        broadcaster = configuration.Broadcaster;
        bot = configuration.Bot;
        SetCurrentVrChatAvatar(configuration.CurrentVrChatAvatarId, notify: false);
        SetSharedReturnAvatar(configuration.SharedReturnAvatarId, configuration.SharedReturnAvatarName, notify: false);
        oscRouterService.UpdateRuleSubscriptions(configuration.Rules);
        RefreshActiveRuleLockoutsForConfiguration(configuration.Rules, configuration.AvatarScaleRules);
        RefreshActiveAvatarSwitchLockoutsForConfiguration(configuration.Rules);
        RefreshAvatarScaleSupporterGrowthStatesForConfiguration(configuration.AvatarScaleRules);
        ApplyChatboxRelayConfiguration(configuration);
        if (configuration.EmergencyRedeemStopEnabled && !wasPaused)
        {
            PurgeQueuedRedeemWorkItems();
        }

        if (!configuration.DesktopModeInputLockEnabled)
        {
            ReleaseAllDesktopInputLocks(
                "Desktop mode input lock was turned off, so Crystal Relay released any active desktop stop-input lock.",
                logRelease: true);
        }
        else
        {
            _ = Task.Run(async () =>
            {
                try
                {
                    await RefreshDesktopInputLockScopeAsync(CancellationToken.None);
                }
                catch (Exception ex)
                {
                    WriteLog($"Crystal Relay could not refresh the desktop input lock state after the settings change: {ex.Message}");
                }
            });
        }

        _ = Task.Run(async () =>
        {
            try
            {
                await RefreshBroadcasterLiveStateAsync(CancellationToken.None);
            }
            catch
            {
            }
        });
    }

    public async Task<IReadOnlyList<VrChatOscParameterSummary>> GetCurrentAvatarParametersAsync(CancellationToken cancellationToken = default)
    {
        return await oscRouterService.GetCurrentAvatarParametersAsync(cancellationToken);
    }

    public async Task SendTestRuleAsync(TriggerRuleSnapshot rule, CancellationToken cancellationToken = default)
    {
        if (!IsOscActive)
        {
            throw new InvalidOperationException("OSC is not running yet, so Crystal Relay cannot send a test to VRChat.");
        }

        await ExecuteRuleActionAsync(rule, null, cancellationToken, isTest: true, queuedReplay: false, allowLaneQueue: true);
    }

    public async Task SendTestUniversalTriggerAsync(UniversalTriggerRuleSnapshot trigger, CancellationToken cancellationToken = default)
    {
        if (!IsOscActive)
        {
            throw new InvalidOperationException("OSC is not running yet, so Crystal Relay cannot send a test to VRChat.");
        }

        await ExecuteUniversalTriggerAsync(trigger, UniversalIncomingEvent.Test, isTest: true, cancellationToken);
    }

    public async Task SendTestAvatarScaleRuleAsync(AvatarScaleRuleSnapshot rule, CancellationToken cancellationToken = default)
    {
        if (!IsOscActive)
        {
            throw new InvalidOperationException("OSC is not running yet, so Crystal Relay cannot send a scale test to VRChat.");
        }

        if (rule.TriggerType == AvatarScaleTriggerType.SupporterGrowth)
        {
            await ExecuteAvatarScaleRuleAsync(rule, UniversalIncomingEvent.Test, isTest: true, cancellationToken);
            return;
        }

        await QueueAvatarScaleRuleExecutionAsync(
            rule,
            UniversalIncomingEvent.Test,
            isTest: true,
            waitForCompletion: true,
            cancellationToken);
    }

    public AvatarScaleRuntimeStatus GetAvatarScaleRuntimeStatus()
    {
        lock (stateGate)
        {
            return new AvatarScaleRuntimeStatus(
                TryGetObservedFloatLocked("/avatar/eyeheight", out var currentHeight) ? currentHeight : null,
                TryGetObservedFloatLocked("/avatar/eyeheightmin", out var minimumHeight) ? minimumHeight : null,
                TryGetObservedFloatLocked("/avatar/eyeheightmax", out var maximumHeight) ? maximumHeight : null,
                TryGetObservedBoolLocked("/avatar/eyeheightscalingallowed", out var scalingAllowed) ? scalingAllowed : null);
        }
    }

    public async Task ForceOscRefreshAsync(CancellationToken cancellationToken = default)
    {
        if (!IsOscActive)
        {
            throw new InvalidOperationException("OSC is not running yet, so there is no OSCQuery session to refresh.");
        }

        await oscRouterService.ForceRefreshAsync(cancellationToken);
    }

    public async Task StopAsync()
    {
        if (runtimeTask is null)
        {
            runtimeCancellation?.Cancel();
            runtimeCancellation?.Dispose();
            runtimeCancellation = null;
            await StopOscRouterSafelyAsync();
            ClearActiveConfiguration();
            broadcaster = null;
            bot = null;
            currentVrChatAvatarId = string.Empty;
            isBroadcasterLive = false;
            nextTriggerInfoAnnouncementAt = DateTimeOffset.MinValue;
            oscSessionMode = OscSessionMode.Stopped;
            ClearRuntimeState();
            StreamStateChanged?.Invoke(false);
            StatusChanged?.Invoke("Background bridge stopped.");
            return;
        }

        if (runtimeCancellation is null)
        {
            await StopOscRouterSafelyAsync();
            ClearActiveConfiguration();
            broadcaster = null;
            bot = null;
            currentVrChatAvatarId = string.Empty;
            isBroadcasterLive = false;
            nextTriggerInfoAnnouncementAt = DateTimeOffset.MinValue;
            oscSessionMode = OscSessionMode.Stopped;
            ClearRuntimeState();
            StreamStateChanged?.Invoke(false);
            StatusChanged?.Invoke("Background bridge stopped.");
            return;
        }

        runtimeCancellation.Cancel();

        try
        {
            await runtimeTask;
        }
        catch (OperationCanceledException)
        {
        }
        finally
        {
            runtimeCancellation.Dispose();
            runtimeCancellation = null;
            runtimeTask = null;
            await StopOscRouterSafelyAsync();
            ClearActiveConfiguration();
            broadcaster = null;
            bot = null;
            currentVrChatAvatarId = string.Empty;
            isBroadcasterLive = false;
            nextTriggerInfoAnnouncementAt = DateTimeOffset.MinValue;
            oscSessionMode = OscSessionMode.Stopped;
            ClearRuntimeState();
            StatusChanged?.Invoke("Background bridge stopped.");
        }
    }

    public async ValueTask DisposeAsync()
    {
        await StopAsync();
        desktopInputLockService.EmergencyUnlockTriggered -= HandleEmergencyDesktopInputUnlock;
        await desktopInputLockService.DisposeAsync();
        twitchApiClient.Dispose();
        thirdPartyChatEmoteRefreshGate.Dispose();
        await oscRouterService.DisposeAsync();
    }

    // Main background loop for the live bridge. EventSub is the primary listener,
    // and the validation loop runs beside it to keep Twitch sessions healthy.
    private async Task RunBridgeAsync(CancellationToken cancellationToken)
    {
        var validationTask = Task.Run(() => RunValidationLoopAsync(cancellationToken), cancellationToken);
        var triggerInfoAnnouncementTask = Task.Run(() => RunTriggerInfoAnnouncementLoopAsync(cancellationToken), cancellationToken);

        try
        {
            await RunEventSubLoopAsync(cancellationToken);
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
        }
        catch (Exception ex)
        {
            WriteLog($"Background bridge stopped because of an error: {ex.Message}");
            StatusChanged?.Invoke("Bridge error. Reconnect Twitch or check config.");
        }
        finally
        {
            try
            {
                await validationTask;
            }
            catch (OperationCanceledException)
            {
            }
            catch (Exception ex)
            {
                WriteLog($"Validation loop ended with an error: {ex.Message}");
            }

            try
            {
                await triggerInfoAnnouncementTask;
            }
            catch (OperationCanceledException)
            {
            }
            catch (Exception ex)
            {
                WriteLog($"Trigger info announcement loop ended with an error: {ex.Message}");
            }

            await ResetPendingRulesAsync();
        }
    }

    private async Task RunValidationLoopAsync(CancellationToken cancellationToken)
    {
        using var timer = new PeriodicTimer(TimeSpan.FromHours(1));

        while (await timer.WaitForNextTickAsync(cancellationToken))
        {
            try
            {
                if (broadcaster is not null)
                {
                    broadcaster = await EnsureAccountReadyAsync(broadcaster, TwitchScopes.BroadcasterRequired, BridgeAccountRole.Broadcaster, cancellationToken);
                    await RefreshBroadcasterLiveStateAsync(cancellationToken);
                }

                if (bot is not null)
                {
                    bot = await EnsureAccountReadyAsync(bot, TwitchScopes.Bot, BridgeAccountRole.Bot, cancellationToken);
                }

                WriteLog("Validated the Twitch OAuth sessions.");
            }
            catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
            {
                throw;
            }
            catch (Exception ex)
            {
                WriteLog($"Twitch validation failed: {ex.Message}");
                StatusChanged?.Invoke("OAuth session expired. Please reconnect Twitch.");
                runtimeCancellation?.Cancel();
                return;
            }
        }
    }

    private async Task RunTriggerInfoAnnouncementLoopAsync(CancellationToken cancellationToken)
    {
        using var timer = new PeriodicTimer(TriggerInfoAnnouncementPollInterval);

        while (await timer.WaitForNextTickAsync(cancellationToken))
        {
            try
            {
                var configuration = activeConfiguration;
                if (configuration is null
                    || !configuration.TriggerInfoAnnouncementsEnabled
                    || !isBroadcasterLive)
                {
                    nextTriggerInfoAnnouncementAt = DateTimeOffset.MinValue;
                    continue;
                }

                var interval = TimeSpan.FromMinutes(Math.Max(1, configuration.TriggerInfoAnnouncementIntervalMinutes));
                var now = DateTimeOffset.UtcNow;
                if (nextTriggerInfoAnnouncementAt == DateTimeOffset.MinValue)
                {
                    nextTriggerInfoAnnouncementAt = now;
                }

                if (now < nextTriggerInfoAnnouncementAt)
                {
                    continue;
                }

                var currentAvatarId = GetCurrentVrChatAvatarId();
                if (string.IsNullOrWhiteSpace(currentAvatarId))
                {
                    nextTriggerInfoAnnouncementAt = now.Add(interval);
                    continue;
                }

                var message = BuildTriggerInfoAnnouncement(configuration, currentAvatarId);
                if (string.IsNullOrWhiteSpace(message))
                {
                    nextTriggerInfoAnnouncementAt = now.Add(interval);
                    continue;
                }

                if (await TrySendChatMessageWithEffectiveSenderAsync(message, "Trigger info announcement", cancellationToken))
                {
                    WriteLog("Sent trigger info announcement to Twitch chat.");
                }

                nextTriggerInfoAnnouncementAt = DateTimeOffset.UtcNow.Add(interval);
            }
            catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
            {
                throw;
            }
            catch (Exception ex)
            {
                WriteLog($"Trigger info announcement failed: {ex.Message}");
                var configuration = activeConfiguration;
                var delay = TimeSpan.FromMinutes(Math.Max(1, configuration?.TriggerInfoAnnouncementIntervalMinutes ?? 10));
                nextTriggerInfoAnnouncementAt = DateTimeOffset.UtcNow.Add(delay);
            }
        }
    }


    // Connects to Twitch EventSub and keeps reconnecting as needed while the bridge is alive.
    // Incoming Twitch events eventually flow into the rule trigger path below.
    private async Task RunEventSubLoopAsync(CancellationToken cancellationToken)
    {
        string? reconnectUrl = null;

        while (!cancellationToken.IsCancellationRequested)
        {
            try
            {
                await using var session = new TwitchEventSubSession();
                StatusChanged?.Invoke(reconnectUrl is null ? "Connecting background listener..." : "Reconnecting background listener...");
                await session.ConnectAsync(reconnectUrl, cancellationToken);

                WriteLog("Connected to Twitch EventSub. Listening and working.");

                if (reconnectUrl is null)
                {
                    await RefreshSubscriptionsAsync(session.SessionId, cancellationToken);
                }

                await RefreshChatBadgeCatalogAsync(cancellationToken);

                StatusChanged?.Invoke("Listening for Twitch triggers.");
                var result = await session.ListenAsync(notification => HandleNotificationAsync(notification, cancellationToken), cancellationToken);

                if (cancellationToken.IsCancellationRequested)
                {
                    return;
                }

                reconnectUrl = result.ReconnectRequested ? result.ReconnectUrl : null;
                WriteLog(result.Reason);

                if (!result.ReconnectRequested)
                {
                    StatusChanged?.Invoke("Listener disconnected. Retrying...");
                    await Task.Delay(TimeSpan.FromSeconds(3), cancellationToken);
                }
            }
            catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
            {
                return;
            }
            catch (Exception ex)
            {
                reconnectUrl = null;
                WriteLog($"EventSub connection issue: {ex.Message}");
                StatusChanged?.Invoke("Twitch connection issue. Retrying...");
                await Task.Delay(TimeSpan.FromSeconds(5), cancellationToken);
            }
        }
    }

    private async Task RefreshSubscriptionsAsync(string sessionId, CancellationToken cancellationToken)
    {
        if (activeConfiguration is null || broadcaster is null)
        {
            throw new InvalidOperationException("The bridge is missing the broadcaster session.");
        }

        var subscriptions = await twitchApiClient.GetEventSubSubscriptionsAsync(
            broadcaster.AccessToken,
            activeConfiguration.TwitchClientId,
            cancellationToken);

        foreach (var subscription in subscriptions.Where(subscription =>
                     string.Equals(subscription.Transport.Method, "websocket", StringComparison.OrdinalIgnoreCase)
                     && string.Equals(subscription.Condition.BroadcasterUserId, broadcaster.UserId, StringComparison.Ordinal)
                     && ManagedSubscriptionTypes.Contains(subscription.Type, StringComparer.Ordinal)))
        {
            await twitchApiClient.DeleteEventSubSubscriptionAsync(
                broadcaster.AccessToken,
                activeConfiguration.TwitchClientId,
                subscription.Id,
                cancellationToken);
        }

        foreach (var subscriptionType in ManagedSubscriptionTypes)
        {
            if (string.Equals(subscriptionType, "channel.chat.message", StringComparison.Ordinal)
                && !HasScope(broadcaster, "user:read:chat"))
            {
                WriteLog("Broadcaster chat read scope is missing, so Twitch Chatbox will wait until broadcaster reconnect.");
                continue;
            }

            if (string.Equals(subscriptionType, "channel.follow", StringComparison.Ordinal))
            {
                var hasFollowTrigger = activeConfiguration.UniversalTriggers.Any(trigger =>
                        trigger.TriggerType == UniversalTriggerType.Follow && trigger.IsEnabled)
                    || activeConfiguration.AvatarScaleRules.Any(rule =>
                        rule.TriggerType == AvatarScaleTriggerType.Follow && rule.IsEnabled);
                if (!hasFollowTrigger)
                {
                    continue;
                }

                if (!HasScope(broadcaster, TwitchScopes.FollowRead))
                {
                    WriteLog("Follow triggers need the broadcaster to reconnect once for Twitch follower-read permission.");
                    continue;
                }
            }

            try
            {
                var condition = BuildSubscriptionCondition(subscriptionType);
                var created = await twitchApiClient.CreateEventSubSubscriptionAsync(
                    broadcaster.AccessToken,
                    activeConfiguration.TwitchClientId,
                    sessionId,
                    subscriptionType,
                    string.Equals(subscriptionType, "channel.follow", StringComparison.Ordinal) ? "2" : "1",
                    condition,
                    cancellationToken);

                if (!created)
                {
                    WriteLog($"Subscription '{subscriptionType}' already existed, so Twitch kept the current copy.");
                }
            }
            catch (Exception ex) when (string.Equals(subscriptionType, "channel.chat.message", StringComparison.Ordinal))
            {
                WriteLog($"Twitch Chatbox listener could not start yet: {ex.Message}");
            }
        }
    }

    private async Task HandleNotificationAsync(EventSubNotification notification, CancellationToken cancellationToken)
    {
        if (!RememberMessage(notification.MessageId))
        {
            return;
        }

        BridgeIncomingEvent? chatCommandEvent = null;
        var chatMessage = await ParseChatMessageAsync(notification, cancellationToken);
        if (chatMessage is not null)
        {
            ChatMessageReceived?.Invoke(chatMessage);
            TryQueueChatboxRelay(chatMessage);
            chatCommandEvent = ParseChatCommandEvent(notification);
        }

        if (TryHandleStreamStateNotification(notification))
        {
            return;
        }

        var bridgeEvent = chatCommandEvent ?? ParseEvent(notification);
        var universalEvent = ParseUniversalEvent(notification, chatCommandEvent);
        var configuration = activeConfiguration;
        var ruleIndex = activeRuleIndex;
        if (configuration is null)
        {
            return;
        }

        var avatarScaleHandled = false;
        if (universalEvent is not null)
        {
            await ExecuteMatchingUniversalTriggersAsync(configuration.UniversalTriggers, universalEvent, cancellationToken);
            avatarScaleHandled = StartMatchingAvatarScaleRules(configuration.AvatarScaleRules, universalEvent);
        }

        if (bridgeEvent is null)
        {
            return;
        }

        var currentAvatarId = GetCurrentVrChatAvatarId();
        var temporarilyDisabledRuleIds = GetTemporarilyDisabledRuleIds();
        var avatarChangeTransitionActive = IsAvatarChangeTransitionActive();
        var matchingRules = bridgeEvent.IsChatCommandTrigger
            ? SelectMatchingChatCommandRules(
                ruleIndex,
                bridgeEvent,
                currentAvatarId,
                avatarChangeTransitionActive,
                temporarilyDisabledRuleIds)
            : SelectMatchingRules(
                ruleIndex,
                bridgeEvent,
                currentAvatarId,
                avatarChangeTransitionActive,
                temporarilyDisabledRuleIds);

        if (bridgeEvent.IsChatCommandTrigger && matchingRules.Length == 0)
        {
            return;
        }

        if (AreRedeemsPaused())
        {
            LogRedeemsPaused();
            return;
        }

        if (matchingRules.Length == 0
            && !bridgeEvent.IsChatCommandTrigger
            && bridgeEvent.TriggerType == TwitchTriggerType.ChannelPoints
            && TryBuildSharedRewardChoiceHelp(
                ruleIndex,
                bridgeEvent,
                currentAvatarId,
                avatarChangeTransitionActive,
                temporarilyDisabledRuleIds,
                out var sharedChoiceHelpMessage,
                out var sharedChoiceLogMessage))
        {
            WriteLog(sharedChoiceLogMessage);
            await TrySendSharedRewardChoiceHelpAsync(sharedChoiceHelpMessage, cancellationToken);
            return;
        }

        if (matchingRules.Length == 0
            && !avatarScaleHandled
            && !bridgeEvent.IsChatCommandTrigger
            && bridgeEvent.TriggerType == TwitchTriggerType.ChannelPoints)
        {
            var rewardLabel = !string.IsNullOrWhiteSpace(bridgeEvent.RewardTitle)
                ? bridgeEvent.RewardTitle
                : bridgeEvent.RewardId ?? "unknown reward";
            if (string.IsNullOrWhiteSpace(currentAvatarId))
            {
                var candidateRules = ruleIndex.GetChannelPointCandidates(
                        bridgeEvent.RewardId?.Trim() ?? string.Empty,
                        NormalizeRewardTitle(bridgeEvent.RewardTitle))
                    .ToArray();
                if (candidateRules.Any(rule => !rule.IsGlobalOverride))
                {
                    WriteLog($"No current VRChat avatar is detected yet, so avatar-scoped reward '{rewardLabel}' cannot run.");
                    return;
                }
            }

            WriteLog($"No active channel point rule matched '{rewardLabel}'.");
        }

        foreach (var rule in matchingRules)
        {
            await ExecuteRuleAsync(rule, bridgeEvent, cancellationToken);
        }
    }

    private object BuildSubscriptionCondition(string subscriptionType)
    {
        if (broadcaster is null)
        {
            throw new InvalidOperationException("The broadcaster session is missing.");
        }

        if (string.Equals(subscriptionType, "channel.chat.message", StringComparison.Ordinal))
        {
            return new
            {
                broadcaster_user_id = broadcaster.UserId,
                user_id = broadcaster.UserId
            };
        }

        if (string.Equals(subscriptionType, "channel.follow", StringComparison.Ordinal))
        {
            return new
            {
                broadcaster_user_id = broadcaster.UserId,
                moderator_user_id = broadcaster.UserId
            };
        }

        return new
        {
            broadcaster_user_id = broadcaster.UserId
        };
    }

    private static bool IsSupporterOverrideRule(TriggerRuleSnapshot rule) =>
        rule.IsGlobalOverride && rule.TriggerType is TwitchTriggerType.Bits or TwitchTriggerType.Subscriptions;

    private static bool IsTimedSupporterOverrideRule(TriggerRuleSnapshot rule) =>
        IsSupporterOverrideRule(rule)
        && rule.ActionType != OscActionType.SetTrigger
        && (rule.AmountScaledDurationEnabled || rule.DurationSeconds > 0);

    private static bool IsBitsOutfitSetTriggerRule(TriggerRuleSnapshot rule) =>
        rule.IsGlobalOverride
        && rule.TriggerType == TwitchTriggerType.Bits
        && rule.ActionType == OscActionType.SetTrigger;

    private bool ShouldBlockAvatarChangeDuringActiveScaling(TriggerRuleSnapshot rule, bool isTest)
    {
        if (isTest
            || activeConfiguration?.AvatarScaleMasterReward.PreventAvatarChangesDuringActiveScaling != true
            || rule.ActionType is not (OscActionType.AvatarChange or OscActionType.AvatarRoulet)
            || IsSupporterOverrideRule(rule))
        {
            return false;
        }

        return IsAvatarScalingActiveForAvatarChangeBlock();
    }

    private bool IsAvatarScalingActiveForAvatarChangeBlock()
    {
        var now = DateTimeOffset.UtcNow;
        lock (stateGate)
        {
            PruneExpiredAvatarScaleHeightSessionsLocked(now);

            if (activeAvatarScaleHeightSessions.Count > 0
                || avatarScaleSupporterGrowthStates.Count > 0
                || activeAvatarScaleEffects.Values.Any(expiresAt => expiresAt > now)
                || activeAvatarScaleOperation is not null
                || activeAvatarScaleRestoreSequence?.ActiveUntil > now)
            {
                return true;
            }

            return false;
        }
    }

    private static AvatarScaleAvatarChangeCarryoverMode GetAvatarScaleAvatarChangeCarryoverMode(TriggerRuleSnapshot rule) =>
        IsSupporterOverrideRule(rule)
            ? AvatarScaleAvatarChangeCarryoverMode.ForcePaidOverride
            : AvatarScaleAvatarChangeCarryoverMode.Auto;

    private void LogPaidAvatarChangeAllowedDuringActiveScaling(TriggerRuleSnapshot rule)
    {
        if (!IsSupporterOverrideRule(rule)
            || rule.ActionType is not (OscActionType.AvatarChange or OscActionType.AvatarRoulet)
            || !IsAvatarScalingActiveForAvatarChangeBlock())
        {
            return;
        }

        WriteLog($"Paid avatar-change override '{rule.Name}' is allowed while Avatar Scaling is active, so Crystal Relay will carry the active scale height.");
    }

    private async Task ExecuteMatchingUniversalTriggersAsync(
        IReadOnlyList<UniversalTriggerRuleSnapshot> triggers,
        UniversalIncomingEvent incomingEvent,
        CancellationToken cancellationToken)
    {
        if (triggers.Count == 0)
        {
            return;
        }

        var matches = SelectMatchingUniversalTriggers(triggers, incomingEvent);
        if (matches.Length == 0)
        {
            return;
        }

        if (AreRedeemsPaused())
        {
            LogRedeemsPaused();
            return;
        }

        foreach (var trigger in matches)
        {
            if (!TryReserveUniversalTriggerDelay(trigger, incomingEvent))
            {
                continue;
            }

            try
            {
                await ExecuteUniversalTriggerAsync(trigger, incomingEvent, isTest: false, cancellationToken);
            }
            catch (Exception ex)
            {
                WriteLog($"Universal trigger '{trigger.Name}' failed: {ex.Message}");
            }
        }
    }

    private async Task ExecuteUniversalTriggerAsync(
        UniversalTriggerRuleSnapshot trigger,
        UniversalIncomingEvent incomingEvent,
        bool isTest,
        CancellationToken cancellationToken)
    {
        var actions = trigger.ExecuteRandomAction && trigger.Actions.Count > 1
            ? [trigger.Actions[Random.Shared.Next(trigger.Actions.Count)]]
            : trigger.Actions;
        if (actions.Count == 0)
        {
            throw new InvalidOperationException("This universal trigger has no OSC actions.");
        }

        var shouldQueue = actions.Any(action => action.AddToQueue);
        if (shouldQueue)
        {
            var gate = GetUniversalTriggerQueueGate(trigger.Id);
            await gate.WaitAsync(cancellationToken);
            try
            {
                await ExecuteUniversalActionsAsync(trigger, actions, cancellationToken);
            }
            finally
            {
                gate.Release();
            }
        }
        else
        {
            await ExecuteUniversalActionsAsync(trigger, actions, cancellationToken);
        }

        WriteLog(isTest
            ? $"Sent universal test trigger for '{trigger.Name}'."
            : $"{incomingEvent.UserDisplayName} triggered universal '{trigger.Name}'.");
    }

    private async Task ExecuteUniversalActionsAsync(
        UniversalTriggerRuleSnapshot trigger,
        IReadOnlyList<UniversalTriggerActionSnapshot> actions,
        CancellationToken cancellationToken)
    {
        var resetTasks = new List<Task>();
        foreach (var action in actions)
        {
            var packet = BuildUniversalOscPacket(action, action.TargetValue);
            await oscRouterService.SendToVrChatAsync(packet, cancellationToken);

            if (action.DurationSeconds > 0)
            {
                resetTasks.Add(SendUniversalActionResetAsync(action, cancellationToken));
            }
        }

        if (resetTasks.Count > 0)
        {
            await Task.WhenAll(resetTasks);
        }
    }

    private async Task SendUniversalActionResetAsync(
        UniversalTriggerActionSnapshot action,
        CancellationToken cancellationToken)
    {
        await Task.Delay(TimeSpan.FromSeconds(Math.Max(0.001, action.DurationSeconds)), cancellationToken);
        var resetPacket = BuildUniversalOscPacket(action, action.DefaultValue);
        await oscRouterService.SendToVrChatAsync(resetPacket, cancellationToken);
    }

    private byte[] BuildUniversalOscPacket(UniversalTriggerActionSnapshot action, string rawValue)
    {
        var parameterType = action.ValueKind switch
        {
            UniversalTriggerValueKind.Bool => OscParameterType.Bool,
            UniversalTriggerValueKind.Float => OscParameterType.Float,
            UniversalTriggerValueKind.String => OscParameterType.String,
            _ => OscParameterType.Int
        };

        return vrChatOscClient.BuildAvatarParameterPacket(action.OscAddress, parameterType, rawValue);
    }

    private SemaphoreSlim GetUniversalTriggerQueueGate(Guid triggerId)
    {
        lock (stateGate)
        {
            if (!universalTriggerQueueGates.TryGetValue(triggerId, out var gate))
            {
                gate = new SemaphoreSlim(1, 1);
                universalTriggerQueueGates[triggerId] = gate;
            }

            return gate;
        }
    }

    private bool TryReserveUniversalTriggerDelay(
        UniversalTriggerRuleSnapshot trigger,
        UniversalIncomingEvent incomingEvent)
    {
        var now = DateTimeOffset.UtcNow;
        lock (stateGate)
        {
            if (trigger.GlobalDelaySeconds > 0)
            {
                if (universalTriggerGlobalDelays.TryGetValue(trigger.Id, out var globalUntil) && globalUntil > now)
                {
                    return false;
                }

                universalTriggerGlobalDelays[trigger.Id] = now.AddSeconds(trigger.GlobalDelaySeconds);
            }
            else
            {
                universalTriggerGlobalDelays.Remove(trigger.Id);
            }

            if (trigger.UserDelaySeconds > 0)
            {
                var userKey = GetUniversalTriggerUserDelayKey(trigger.Id, incomingEvent);
                if (universalTriggerUserDelays.TryGetValue(userKey, out var userUntil) && userUntil > now)
                {
                    return false;
                }

                universalTriggerUserDelays[userKey] = now.AddSeconds(trigger.UserDelaySeconds);
            }

            return true;
        }
    }

    private static string GetUniversalTriggerUserDelayKey(Guid triggerId, UniversalIncomingEvent incomingEvent)
    {
        var userKey = !string.IsNullOrWhiteSpace(incomingEvent.UserId)
            ? incomingEvent.UserId
            : !string.IsNullOrWhiteSpace(incomingEvent.UserLogin)
                ? incomingEvent.UserLogin
                : incomingEvent.UserDisplayName;
        return $"{triggerId:N}:{userKey.Trim().ToLowerInvariant()}";
    }

    private static UniversalTriggerRuleSnapshot[] SelectMatchingUniversalTriggers(
        IReadOnlyList<UniversalTriggerRuleSnapshot> triggers,
        UniversalIncomingEvent incomingEvent)
    {
        return triggers
            .Where(trigger => trigger.IsEnabled && UniversalTriggerMatches(trigger, incomingEvent))
            .ToArray();
    }

    private static bool UniversalTriggerMatches(
        UniversalTriggerRuleSnapshot trigger,
        UniversalIncomingEvent incomingEvent)
    {
        if (incomingEvent.TriggerType == UniversalTriggerType.ChatCommand
            && trigger.TriggerType == UniversalTriggerType.ChannelPointReward
            && trigger.ChatCommandEnabled)
        {
            return ChatCommandUtility.MessageMatches(trigger.CommandText, incomingEvent.ChatMessageText)
                && UserCanTriggerChatCommand(trigger.ChatCommandPermission, incomingEvent);
        }

        if (trigger.TriggerType != incomingEvent.TriggerType)
        {
            return false;
        }

        return trigger.TriggerType switch
        {
            UniversalTriggerType.ChatCommand => ChatCommandUtility.MessageMatches(trigger.CommandText, incomingEvent.ChatMessageText)
                && UserCanTriggerChatCommand(trigger.ChatCommandPermission, incomingEvent),
            UniversalTriggerType.ChannelPointReward => UniversalRewardMatches(trigger, incomingEvent),
            UniversalTriggerType.Bits => incomingEvent.Amount >= Math.Min(trigger.MinimumBits, trigger.MaximumBits)
                && incomingEvent.Amount <= Math.Max(trigger.MinimumBits, trigger.MaximumBits),
            UniversalTriggerType.Subscription or UniversalTriggerType.GiftSubscription => UniversalSubscriptionMatches(trigger, incomingEvent),
            UniversalTriggerType.Follow => true,
            _ => false
        };
    }

    private static bool UniversalRewardMatches(
        UniversalTriggerRuleSnapshot trigger,
        UniversalIncomingEvent incomingEvent)
    {
        if (!string.IsNullOrWhiteSpace(trigger.RewardId)
            && !string.IsNullOrWhiteSpace(incomingEvent.RewardId)
            && string.Equals(trigger.RewardId, incomingEvent.RewardId, StringComparison.Ordinal))
        {
            return true;
        }

        if (string.IsNullOrWhiteSpace(trigger.RewardTitle)
            || string.IsNullOrWhiteSpace(incomingEvent.RewardTitle))
        {
            return false;
        }

        return string.Equals(trigger.RewardTitle, incomingEvent.RewardTitle, StringComparison.OrdinalIgnoreCase)
            || string.Equals(
                ManagedRewardPresentation.StripPrefix(trigger.RewardTitle),
                ManagedRewardPresentation.StripPrefix(incomingEvent.RewardTitle),
                StringComparison.OrdinalIgnoreCase);
    }

    private static bool UniversalSubscriptionMatches(
        UniversalTriggerRuleSnapshot trigger,
        UniversalIncomingEvent incomingEvent)
    {
        if (!string.IsNullOrWhiteSpace(trigger.SubscriptionTier)
            && !string.Equals(trigger.SubscriptionTier, incomingEvent.SubscriptionTier, StringComparison.OrdinalIgnoreCase))
        {
            return false;
        }

        if (trigger.MinimumMonths < 0 && trigger.MaximumMonths < 0)
        {
            return true;
        }

        var minimumMonths = Math.Max(0, Math.Min(trigger.MinimumMonths, trigger.MaximumMonths));
        var maximumMonths = Math.Max(minimumMonths, Math.Max(trigger.MinimumMonths, trigger.MaximumMonths));
        return incomingEvent.SubscriptionMonths >= minimumMonths
            && incomingEvent.SubscriptionMonths <= maximumMonths;
    }

    private bool StartMatchingAvatarScaleRules(
        IReadOnlyList<AvatarScaleRuleSnapshot> rules,
        UniversalIncomingEvent incomingEvent)
    {
        if (activeConfiguration is { AvatarScaleMasterReward.IsEnabled: true } configuration
            && AvatarScaleMasterRewardMatches(configuration.AvatarScaleMasterReward, incomingEvent))
        {
            HandleAvatarScaleMasterRewardRedeemed(configuration.AvatarScaleMasterReward, incomingEvent);
            return true;
        }

        if (rules.Count == 0)
        {
            return false;
        }

        var temporarilyDisabledRuleIds = GetTemporarilyDisabledRuleIds();
        var matches = SelectMatchingAvatarScaleRules(rules, incomingEvent)
            .Where(rule => !temporarilyDisabledRuleIds.Contains(rule.Id))
            .ToArray();
        if (matches.Length == 0)
        {
            return false;
        }

        if (AreRedeemsPaused())
        {
            LogRedeemsPaused();
            return true;
        }

        foreach (var rule in matches)
        {
            StartAvatarScaleRuleExecution(rule, incomingEvent);
        }

        return true;
    }

    private void StartAvatarScaleRuleExecution(
        AvatarScaleRuleSnapshot rule,
        UniversalIncomingEvent incomingEvent)
    {
        var executionToken = runtimeCancellation?.Token ?? CancellationToken.None;
        if (rule.TriggerType != AvatarScaleTriggerType.SupporterGrowth)
        {
            _ = QueueAvatarScaleRuleExecutionAsync(
                rule,
                incomingEvent,
                isTest: false,
                waitForCompletion: false,
                executionToken);
            return;
        }

        _ = Task.Run(async () =>
        {
            try
            {
                await ExecuteAvatarScaleRuleAsync(rule, incomingEvent, isTest: false, executionToken);
            }
            catch (OperationCanceledException) when (executionToken.IsCancellationRequested)
            {
            }
            catch (Exception ex)
            {
                WriteLog($"Avatar scale '{rule.Name}' failed: {ex.Message}");
            }
        }, CancellationToken.None);
    }

    private Task QueueAvatarScaleRuleExecutionAsync(
        AvatarScaleRuleSnapshot rule,
        UniversalIncomingEvent incomingEvent,
        bool isTest,
        bool waitForCompletion,
        CancellationToken cancellationToken)
    {
        var completion = waitForCompletion
            ? new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously)
            : null;
        int queuedCount;
        lock (stateGate)
        {
            queuedAvatarScaleOperations.Enqueue(new QueuedAvatarScaleOperation(
                rule,
                incomingEvent,
                isTest,
                completion));
            queuedCount = queuedAvatarScaleOperations.Count;
        }

        WriteLog(isTest
            ? $"Queued avatar scale test/simulated effect for '{rule.Name}'. {queuedCount} waiting."
            : $"Queued avatar scale '{rule.Name}' for {incomingEvent.UserDisplayName}. {queuedCount} waiting.");

        EnsureQueuedAvatarScaleOperationDrain();
        return completion is null
            ? Task.CompletedTask
            : completion.Task.WaitAsync(cancellationToken);
    }

    private void EnsureQueuedAvatarScaleOperationDrain()
    {
        var cancellationToken = runtimeCancellation?.Token ?? CancellationToken.None;
        var shouldStart = false;

        lock (stateGate)
        {
            if (!drainingQueuedAvatarScaleOperations)
            {
                drainingQueuedAvatarScaleOperations = true;
                shouldStart = true;
            }
        }

        if (!shouldStart)
        {
            return;
        }

        _ = Task.Run(async () =>
        {
            try
            {
                while (!cancellationToken.IsCancellationRequested)
                {
                    QueuedAvatarScaleOperation? queuedOperation = null;
                    AvatarScaleRuleSnapshot? ruleToExecute = null;
                    TimeSpan delay = TimeSpan.Zero;
                    string? logMessage = null;
                    TaskCompletionSource<bool>? completionToCancel = null;

                    lock (stateGate)
                    {
                        if (queuedAvatarScaleOperations.Count == 0)
                        {
                            break;
                        }

                        var nextOperation = queuedAvatarScaleOperations.Peek();
                        var now = DateTimeOffset.UtcNow;
                        if (activeAvatarScaleOperation is not null)
                        {
                            delay = AvatarScaleQueuePollDelay;
                        }
                        else if (!nextOperation.IsTest && activeConfiguration?.EmergencyRedeemStopEnabled == true)
                        {
                            queuedAvatarScaleOperations.Dequeue();
                            completionToCancel = nextOperation.Completion;
                            logMessage = $"Dropped queued avatar scale '{nextOperation.Rule.Name}' because redeems are paused.";
                        }
                        else
                        {
                            ruleToExecute = nextOperation.Rule;
                            if (!nextOperation.IsTest)
                            {
                                var currentRule = activeConfiguration?.AvatarScaleRules.FirstOrDefault(rule => rule.Id == nextOperation.Rule.Id);
                                if (currentRule is null || !currentRule.IsEnabled)
                                {
                                    queuedAvatarScaleOperations.Dequeue();
                                    completionToCancel = nextOperation.Completion;
                                    logMessage = $"Dropped queued avatar scale '{nextOperation.Rule.Name}' because that scale redeem is no longer enabled.";
                                    ruleToExecute = null;
                                }
                                else if (TryGetTemporarilyDisabledUntilLocked(currentRule.Id, now, out var temporarilyDisabledUntil)
                                    && temporarilyDisabledUntil > now)
                                {
                                    delay = temporarilyDisabledUntil - now;
                                    if (!nextOperation.TemporaryDisableWaitLogged)
                                    {
                                        nextOperation.TemporaryDisableWaitLogged = true;
                                        logMessage = $"Queued avatar scale '{currentRule.Name}' is waiting for disable pairing to clear for {DescribeDuration(delay.TotalSeconds)}.";
                                    }
                                }
                                else if (cooldowns.TryGetValue(currentRule.Id, out var cooldownUntil) && cooldownUntil > now)
                                {
                                    delay = cooldownUntil - now;
                                    if (!nextOperation.CooldownWaitLogged)
                                    {
                                        nextOperation.CooldownWaitLogged = true;
                                        logMessage = $"Queued avatar scale '{currentRule.Name}' is waiting for cooldown to clear for {DescribeDuration(delay.TotalSeconds)}.";
                                    }
                                }
                                else
                                {
                                    ruleToExecute = currentRule;
                                }
                            }

                            if (ruleToExecute is not null && delay <= TimeSpan.Zero)
                            {
                                queuedOperation = queuedAvatarScaleOperations.Dequeue();
                            }
                        }
                    }

                    if (!string.IsNullOrWhiteSpace(logMessage))
                    {
                        WriteLog(logMessage);
                    }

                    if (completionToCancel is not null)
                    {
                        completionToCancel.TrySetCanceled();
                    }

                    if (delay > TimeSpan.Zero)
                    {
                        await Task.Delay(delay > AvatarScaleQueuePollDelay ? AvatarScaleQueuePollDelay : delay, cancellationToken);
                        continue;
                    }

                    if (queuedOperation is null || ruleToExecute is null)
                    {
                        continue;
                    }

                    WriteLog(queuedOperation.IsTest
                        ? $"Starting queued avatar scale test/simulated effect for '{ruleToExecute.Name}'."
                        : $"Starting queued avatar scale '{ruleToExecute.Name}' for {queuedOperation.IncomingEvent.UserDisplayName}.");

                    try
                    {
                        var completed = await ExecuteAvatarScaleRuleAsync(
                            ruleToExecute,
                            queuedOperation.IncomingEvent,
                            queuedOperation.IsTest,
                            cancellationToken);
                        if (!completed)
                        {
                            RequeueAvatarScaleOperationAtFront(queuedOperation);
                            await Task.Delay(AvatarScaleQueuePollDelay, cancellationToken);
                            continue;
                        }

                        queuedOperation.Completion?.TrySetResult(true);
                    }
                    catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
                    {
                        queuedOperation.Completion?.TrySetCanceled();
                        throw;
                    }
                    catch (Exception ex)
                    {
                        queuedOperation.Completion?.TrySetException(ex);
                        WriteLog($"Queued avatar scale '{ruleToExecute.Name}' failed: {ex.Message}");
                    }
                }
            }
            catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
            {
            }
            finally
            {
                var restart = false;
                lock (stateGate)
                {
                    drainingQueuedAvatarScaleOperations = false;
                    restart = queuedAvatarScaleOperations.Count > 0;
                }

                if (restart)
                {
                    EnsureQueuedAvatarScaleOperationDrain();
                }
            }
        }, CancellationToken.None);
    }

    private void RequeueAvatarScaleOperationAtFront(QueuedAvatarScaleOperation operation)
    {
        lock (stateGate)
        {
            var remaining = queuedAvatarScaleOperations.ToArray();
            queuedAvatarScaleOperations.Clear();
            queuedAvatarScaleOperations.Enqueue(operation);
            foreach (var queuedOperation in remaining)
            {
                queuedAvatarScaleOperations.Enqueue(queuedOperation);
            }
        }
    }

    private (int LiveCount, int TestCount) ClearQueuedAvatarScaleOperationsLocked(bool includeTests)
    {
        var liveCount = 0;
        var testCount = 0;
        if (queuedAvatarScaleOperations.Count == 0)
        {
            return (liveCount, testCount);
        }

        var retained = includeTests ? null : new List<QueuedAvatarScaleOperation>();
        while (queuedAvatarScaleOperations.Count > 0)
        {
            var queuedOperation = queuedAvatarScaleOperations.Dequeue();
            if (!includeTests && queuedOperation.IsTest)
            {
                retained!.Add(queuedOperation);
                continue;
            }

            if (queuedOperation.IsTest)
            {
                testCount++;
            }
            else
            {
                liveCount++;
            }

            queuedOperation.Completion?.TrySetCanceled();
        }

        if (retained is not null)
        {
            foreach (var queuedOperation in retained)
            {
                queuedAvatarScaleOperations.Enqueue(queuedOperation);
            }
        }

        return (liveCount, testCount);
    }

    private async Task<bool> ExecuteAvatarScaleRuleAsync(
        AvatarScaleRuleSnapshot rule,
        UniversalIncomingEvent incomingEvent,
        bool isTest,
        CancellationToken cancellationToken)
    {
        if (rule.TriggerType == AvatarScaleTriggerType.SupporterGrowth)
        {
            await ExecuteSupporterGrowthAvatarScaleRuleAsync(rule, incomingEvent, isTest, cancellationToken);
            return true;
        }

        var cooldownSeconds = GetAvatarScaleEffectiveCooldownSeconds(rule);
        if (!isTest)
        {
            var now = DateTimeOffset.UtcNow;
            lock (stateGate)
            {
                if (TryGetTemporarilyDisabledUntilLocked(rule.Id, now, out var temporarilyDisabledUntil))
                {
                    WriteLog($"Avatar scale '{rule.Name}' skipped because it is temporarily disabled for {DescribeDuration((temporarilyDisabledUntil - now).TotalSeconds)}.");
                    return true;
                }

                if (cooldownSeconds <= 0)
                {
                    cooldowns.Remove(rule.Id);
                }
                else if (cooldowns.TryGetValue(rule.Id, out var cooldownUntil) && cooldownUntil > now)
                {
                    WriteLog($"Avatar scale '{rule.Name}' skipped because it is still on cooldown for {DescribeDuration((cooldownUntil - now).TotalSeconds)}.");
                    return true;
                }
            }
        }

        var operation = TryBeginAvatarScaleOperation(
            rule.Id,
            rule.Name,
            isTest ? AvatarScaleOperationPriority.TestSimulation : AvatarScaleOperationPriority.LiveRedeem,
            isTest);
        if (operation is null)
        {
            return false;
        }

        var heightSessionId = Guid.Empty;
        var heightSessionEndScheduled = false;
        try
        {
            var scalingAllowed = await TryGetAvatarScalingAllowedAsync(cancellationToken);
            if (scalingAllowed == false)
            {
                WriteLog($"Avatar scale '{rule.Name}' skipped because VRChat reports /avatar/eyeheightscalingallowed is false. The current world or Udon may be blocking avatar scaling.");
                return true;
            }

            var previousHeight = await TryGetCurrentAvatarHeightAsync(cancellationToken);
            if (IsRelativeScaleAtLimit(rule, previousHeight, out var limitMessage))
            {
                WriteLog($"Avatar scale '{rule.Name}' skipped because {limitMessage}");
                return true;
            }

            var targetHeight = ResolveAvatarScaleTargetHeight(rule, previousHeight);
            targetHeight = ApplyAvatarScaleHeightLimits(rule, targetHeight, "target height");

            var rewardStateStarted = false;
            var firstScaleSendStarted = false;
            void StartRewardStateAfterFirstScaleSend()
            {
                firstScaleSendStarted = true;
                if (rewardStateStarted)
                {
                    return;
                }

                rewardStateStarted = true;
                if (isTest)
                {
                    return;
                }

                UpdateActiveAvatarScaleRuleLockoutState(rule);

                var effectDurationSeconds = GetAvatarScaleEffectDurationSeconds(rule);
                var activeWindowSeconds = effectDurationSeconds;
                heightSessionId = StartAvatarScaleHeightSession(
                    rule.Id,
                    rule.Name,
                    rule.RestoreHeightMeters,
                    targetHeight,
                    activeWindowSeconds);
                if (heightSessionId != Guid.Empty)
                {
                    heightSessionEndScheduled = true;
                    ScheduleAvatarScaleHeightSessionEnd(
                        rule.Id,
                        heightSessionId,
                        TimeSpan.FromSeconds(Math.Max(0.5, activeWindowSeconds)),
                        cancellationToken);
                }

                lock (stateGate)
                {
                    if (effectDurationSeconds > 0)
                    {
                        activeAvatarScaleEffects[rule.Id] = DateTimeOffset.UtcNow.AddSeconds(effectDurationSeconds);
                    }
                    else
                    {
                        activeAvatarScaleEffects.Remove(rule.Id);
                    }

                    if (cooldownSeconds > 0)
                    {
                        cooldowns[rule.Id] = DateTimeOffset.UtcNow.AddSeconds(cooldownSeconds);
                    }
                    else
                    {
                        cooldowns.Remove(rule.Id);
                    }
                }

                if (cooldownSeconds > 0)
                {
                    ScheduleCooldownStateNotification(rule.Id, TimeSpan.FromSeconds(cooldownSeconds));
                    ManagedRewardAvailabilityChanged?.Invoke();
                }
                else
                {
                    CancelCooldownStateNotification(rule.Id);
                }

                if (effectDurationSeconds > 0)
                {
                    ScheduleAvatarScaleEffectStateNotification(rule.Id, TimeSpan.FromSeconds(effectDurationSeconds));
                    ManagedRewardAvailabilityChanged?.Invoke();
                }
            }

            if (!await SendAvatarHeightForOperationAsync(
                    operation,
                    targetHeight,
                    rule.SmoothTransitionSeconds,
                    cancellationToken,
                    StartRewardStateAfterFirstScaleSend))
            {
                return firstScaleSendStarted;
            }

            if (rule.ActiveTimeSeconds > 0)
            {
                ScheduleAvatarScaleRestoreSequence(rule, isTest);
            }
            else
            {
                CancelAvatarScaleRestoreSequenceForCurrentAvatar(
                    rule,
                    $"Avatar scale '{rule.Name}' cleared the pending inactive reset because this scale redeem has no restore timer.");
            }

            WriteLog(isTest
                ? $"Sent avatar scale test/simulated effect for '{rule.Name}' to {targetHeight:0.###}m."
                : $"{incomingEvent.UserDisplayName} triggered avatar scale '{rule.Name}' to {targetHeight:0.###}m.");
            return true;
        }
        finally
        {
            if (!heightSessionEndScheduled)
            {
                EndAvatarScaleHeightSession(rule.Id, heightSessionId);
            }

            EndAvatarScaleOperation(operation);
        }
    }

    private async Task<bool> SendAvatarHeightForOperationAsync(
        ActiveAvatarScaleOperationTicket operation,
        double targetHeight,
        double smoothSeconds,
        CancellationToken cancellationToken,
        Action? afterFirstSuccessfulSend = null)
    {
        if (!IsAvatarScaleOperationCurrent(operation))
        {
            WriteLog($"Avatar scale '{operation.RuleName}' skipped a stale OSC height write because another scale effect is active.");
            return false;
        }

        SetAvatarScaleOperationTransitionActive(operation, smoothSeconds > 0);
        try
        {
            var completed = await SendAvatarHeightAsync(
                targetHeight,
                smoothSeconds,
                cancellationToken,
                afterFirstSuccessfulSend,
                () => IsAvatarScaleOperationCurrent(operation));
            if (!completed)
            {
                WriteLog($"Avatar scale '{operation.RuleName}' stopped because a newer or higher-priority scale effect took over.");
            }

            return completed;
        }
        finally
        {
            SetAvatarScaleOperationTransitionActive(operation, false);
        }
    }

    private async Task<bool> SendAvatarHeightAsync(
        double targetHeight,
        double smoothSeconds,
        CancellationToken cancellationToken,
        Action? afterFirstSuccessfulSend = null,
        Func<bool>? shouldContinue = null)
    {
        var currentHeight = await TryGetCurrentAvatarHeightAsync(cancellationToken);
        if (smoothSeconds <= 0 || currentHeight is null)
        {
            if (shouldContinue?.Invoke() == false)
            {
                return false;
            }

            await SendAvatarHeightValueAsync(targetHeight, cancellationToken);
            afterFirstSuccessfulSend?.Invoke();
            return true;
        }

        var duration = TimeSpan.FromSeconds(Math.Max(0.001, smoothSeconds));
        var steps = Math.Clamp(
            (int)Math.Ceiling(duration.TotalSeconds * AvatarScaleSmoothUpdatesPerSecond),
            1,
            AvatarScaleSmoothMaxSteps);
        var startHeight = currentHeight.Value;
        var stopwatch = Stopwatch.StartNew();

        for (var step = 1; step <= steps; step++)
        {
            var scheduledElapsed = TimeSpan.FromSeconds(duration.TotalSeconds * step / steps);
            var waitTime = scheduledElapsed - stopwatch.Elapsed;
            if (waitTime > TimeSpan.Zero)
            {
                await Task.Delay(waitTime, cancellationToken);
            }

            if (shouldContinue?.Invoke() == false)
            {
                return false;
            }

            var linearProgress = step == steps
                ? 1
                : Math.Clamp(stopwatch.Elapsed.TotalSeconds / duration.TotalSeconds, 0, 1);
            var easedProgress = SmoothStep(linearProgress);
            var value = startHeight + ((targetHeight - startHeight) * easedProgress);
            await SendAvatarHeightValueAsync(value, cancellationToken);
            afterFirstSuccessfulSend?.Invoke();
            afterFirstSuccessfulSend = null;
        }

        return true;
    }

    private ActiveAvatarScaleOperationTicket? TryBeginAvatarScaleOperation(
        Guid ruleId,
        string ruleName,
        AvatarScaleOperationPriority priority,
        bool isTest)
    {
        var displayName = string.IsNullOrWhiteSpace(ruleName) ? "Avatar Scale" : ruleName;
        ActiveAvatarScaleOperationTicket? operation = null;
        string? logMessage = null;
        lock (stateGate)
        {
            if (activeAvatarScaleOperation is { } activeOperation)
            {
                if (activeOperation.IsTransitionActive)
                {
                    logMessage = $"Avatar scale '{displayName}' skipped because '{activeOperation.RuleName}' is in an active transition safety lock.";
                }
                else if (activeOperation.Priority > priority)
                {
                    logMessage = $"Avatar scale '{displayName}' skipped because higher-priority scale effect '{activeOperation.RuleName}' is active.";
                }
                else
                {
                    logMessage = activeOperation.Priority == priority
                    ? $"Avatar scale '{displayName}' is taking over from active scale effect '{activeOperation.RuleName}'."
                    : $"Avatar scale '{displayName}' is taking priority over lower-priority scale effect '{activeOperation.RuleName}'.";
                }
            }

            if (activeAvatarScaleOperation is { } currentOperation
                && (currentOperation.IsTransitionActive || currentOperation.Priority > priority))
            {
                operation = null;
            }
            else
            {
                var operationId = ++nextAvatarScaleOperationId;
                operation = new ActiveAvatarScaleOperationTicket(
                    operationId,
                    ruleId,
                    displayName,
                    priority,
                    isTest);
                activeAvatarScaleOperation = new ActiveAvatarScaleOperationState(
                    operationId,
                    ruleId,
                    displayName,
                    priority,
                    isTest,
                    false,
                    DateTimeOffset.UtcNow);
            }
        }

        if (!string.IsNullOrWhiteSpace(logMessage))
        {
            WriteLog(logMessage);
        }

        return operation;
    }

    private bool IsAvatarScaleOperationCurrent(ActiveAvatarScaleOperationTicket operation)
    {
        lock (stateGate)
        {
            return activeAvatarScaleOperation?.OperationId == operation.OperationId;
        }
    }

    private void SetAvatarScaleOperationTransitionActive(
        ActiveAvatarScaleOperationTicket operation,
        bool isTransitionActive)
    {
        lock (stateGate)
        {
            if (activeAvatarScaleOperation is not { } activeOperation
                || activeOperation.OperationId != operation.OperationId
                || activeOperation.IsTransitionActive == isTransitionActive)
            {
                return;
            }

            activeAvatarScaleOperation = activeOperation with
            {
                IsTransitionActive = isTransitionActive
            };
        }
    }

    private void EndAvatarScaleOperation(ActiveAvatarScaleOperationTicket operation)
    {
        lock (stateGate)
        {
            if (activeAvatarScaleOperation?.OperationId == operation.OperationId)
            {
                activeAvatarScaleOperation = null;
            }
        }
    }

    private async Task<ActiveAvatarScaleOperationTicket?> WaitForAvatarScaleOperationSlotAsync(
        Guid ruleId,
        string ruleName,
        AvatarScaleOperationPriority priority,
        bool isTest,
        CancellationToken cancellationToken)
    {
        var waitLogged = false;
        while (!cancellationToken.IsCancellationRequested)
        {
            var blockingDescription = GetBlockingAvatarScaleOperationDescription(priority);
            if (blockingDescription is null)
            {
                var operation = TryBeginAvatarScaleOperation(ruleId, ruleName, priority, isTest);
                if (operation is not null)
                {
                    return operation;
                }

                blockingDescription = "another scale effect to finish";
            }

            if (!waitLogged)
            {
                var displayName = string.IsNullOrWhiteSpace(ruleName) ? "Avatar Scale" : ruleName;
                WriteLog($"Avatar scale '{displayName}' is waiting for {blockingDescription} before starting.");
                waitLogged = true;
            }

            await Task.Delay(AvatarScaleQueuePollDelay, cancellationToken);
        }

        return null;
    }

    private string? GetBlockingAvatarScaleOperationDescription(AvatarScaleOperationPriority priority)
    {
        lock (stateGate)
        {
            if (activeAvatarScaleOperation is not { } activeOperation)
            {
                return null;
            }

            if (activeOperation.IsTransitionActive)
            {
                return $"'{activeOperation.RuleName}' to finish its active transition safety lock";
            }

            return activeOperation.Priority > priority
                ? $"higher-priority scale effect '{activeOperation.RuleName}' to finish"
                : null;
        }
    }

    private void ScheduleAvatarScaleRestoreSequence(
        AvatarScaleRuleSnapshot rule,
        bool isTest)
    {
        var now = DateTimeOffset.UtcNow;
        var avatarId = GetCurrentVrChatAvatarId();
        var sourceRuleName = string.IsNullOrWhiteSpace(rule.Name) ? "Avatar Scale" : rule.Name;
        var restoreHeight = rule.RestoreHeightMeters;
        var newCancellation = runtimeCancellation is null
            ? new CancellationTokenSource()
            : CancellationTokenSource.CreateLinkedTokenSource(runtimeCancellation.Token);
        CancellationTokenSource? previousCancellation = null;
        ActiveAvatarScaleRestoreSequenceState? sequence = null;

        lock (stateGate)
        {
            sequence = new ActiveAvatarScaleRestoreSequenceState(
                ++nextAvatarScaleRestoreSequenceId,
                avatarId,
                ApplyAvatarScaleHeightLimits(rule, restoreHeight, "return height"),
                now.AddSeconds(Math.Max(0.001, rule.ActiveTimeSeconds)),
                sourceRuleName,
                Math.Max(0, rule.SmoothTransitionSeconds),
                isTest);
            previousCancellation = avatarScaleRestoreSequenceCancellation;
            avatarScaleRestoreSequenceCancellation = newCancellation;
            activeAvatarScaleRestoreSequence = sequence;
        }

        if (sequence is null)
        {
            newCancellation.Dispose();
            WriteLog($"Avatar scale '{sourceRuleName}' could not schedule its return height reset.");
            return;
        }

        previousCancellation?.Cancel();
        _ = Task.Run(() => RunAvatarScaleRestoreSequenceAsync(sequence, newCancellation), CancellationToken.None);

        var activeSeconds = Math.Max(0.001, rule.ActiveTimeSeconds);
        WriteLog(isTest
            ? $"Avatar scale test/simulated effect '{sourceRuleName}' reset the inactive restore timer for {DescribeDuration(activeSeconds)}."
            : $"Avatar scale '{sourceRuleName}' reset the inactive restore timer for {DescribeDuration(activeSeconds)}.");
    }

    private void CancelAvatarScaleRestoreSequenceForCurrentAvatar(
        AvatarScaleRuleSnapshot rule,
        string logMessage)
    {
        var avatarId = GetCurrentVrChatAvatarId();
        CancellationTokenSource? cancellation = null;
        lock (stateGate)
        {
            if (activeAvatarScaleRestoreSequence is null
                || !string.Equals(activeAvatarScaleRestoreSequence.AvatarId, avatarId, StringComparison.Ordinal))
            {
                return;
            }

            cancellation = avatarScaleRestoreSequenceCancellation;
            avatarScaleRestoreSequenceCancellation = null;
            activeAvatarScaleRestoreSequence = null;
        }

        cancellation?.Cancel();
        WriteLog(logMessage);
    }

    private async Task RunAvatarScaleRestoreSequenceAsync(
        ActiveAvatarScaleRestoreSequenceState sequence,
        CancellationTokenSource sequenceCancellation)
    {
        var cancellationToken = sequenceCancellation.Token;
        var deferLogged = false;
        try
        {
            while (!cancellationToken.IsCancellationRequested)
            {
                var delay = sequence.ActiveUntil - DateTimeOffset.UtcNow;
                if (delay > TimeSpan.Zero)
                {
                    await Task.Delay(delay, cancellationToken);
                }

                if (!IsAvatarScaleRestoreSequenceCurrent(sequence.SequenceId))
                {
                    return;
                }

                ActiveAvatarScaleRestoreSequenceState currentSequence;
                var shouldDefer = false;
                lock (stateGate)
                {
                    if (activeAvatarScaleRestoreSequence?.SequenceId != sequence.SequenceId)
                    {
                        return;
                    }

                    currentSequence = activeAvatarScaleRestoreSequence;
                    shouldDefer = activeAvatarScaleOperation is not null
                        || queuedAvatarScaleOperations.Count > 0;
                }

                sequence = currentSequence;
                if (!string.Equals(GetCurrentVrChatAvatarId(), sequence.AvatarId, StringComparison.Ordinal))
                {
                    ClearAvatarScaleRestoreSequenceIfCurrent(sequence.SequenceId);
                    WriteLog($"Avatar scale restore from '{sequence.SourceRuleName}' was skipped because the current avatar changed before the inactive timer ended.");
                    return;
                }

                if (shouldDefer)
                {
                    if (!deferLogged)
                    {
                        deferLogged = true;
                        WriteLog($"Avatar scale restore from '{sequence.SourceRuleName}' is waiting for queued scale changes or an active transition to finish.");
                    }

                    await Task.Delay(AvatarScaleQueuePollDelay, cancellationToken);
                    continue;
                }

                var operation = TryBeginAvatarScaleOperation(
                    Guid.Empty,
                    $"Scale restore from {sequence.SourceRuleName}",
                    AvatarScaleOperationPriority.IdleRestore,
                    isTest: sequence.IsTest);
                if (operation is null)
                {
                    if (!deferLogged)
                    {
                        deferLogged = true;
                        WriteLog($"Avatar scale restore from '{sequence.SourceRuleName}' is waiting for a higher-priority scale effect to finish.");
                    }

                    await Task.Delay(AvatarScaleQueuePollDelay, cancellationToken);
                    continue;
                }

                try
                {
                    if (!IsAvatarScaleRestoreSequenceCurrent(sequence.SequenceId))
                    {
                        return;
                    }

                    if (!await SendAvatarHeightForOperationAsync(
                            operation,
                            sequence.RestoreHeightMeters,
                            sequence.RestoreSmoothTransitionSeconds,
                            cancellationToken))
                    {
                        await Task.Delay(AvatarScaleQueuePollDelay, cancellationToken);
                        continue;
                    }

                    ClearPendingAvatarScaleHeightRestoreForCurrentAvatar();
                    ClearAvatarScaleRestoreSequenceIfCurrent(sequence.SequenceId);
                    WriteLog($"Avatar scale returned to the configured return height of {sequence.RestoreHeightMeters:0.###}m after the inactive timer ended.");
                    return;
                }
                finally
                {
                    EndAvatarScaleOperation(operation);
                }
            }
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
        }
        catch (Exception ex)
        {
            WriteLog($"Avatar scale restore from '{sequence.SourceRuleName}' failed: {ex.Message}");
        }
        finally
        {
            lock (stateGate)
            {
                if (ReferenceEquals(avatarScaleRestoreSequenceCancellation, sequenceCancellation))
                {
                    avatarScaleRestoreSequenceCancellation = null;
                }
            }

            sequenceCancellation.Dispose();
        }
    }

    private bool IsAvatarScaleRestoreSequenceCurrent(long sequenceId)
    {
        lock (stateGate)
        {
            return activeAvatarScaleRestoreSequence?.SequenceId == sequenceId;
        }
    }

    private void ClearAvatarScaleRestoreSequenceIfCurrent(long sequenceId)
    {
        lock (stateGate)
        {
            if (activeAvatarScaleRestoreSequence?.SequenceId == sequenceId)
            {
                activeAvatarScaleRestoreSequence = null;
                avatarScaleRestoreSequenceCancellation = null;
            }
        }
    }

    private async Task ExecuteSupporterGrowthAvatarScaleRuleAsync(
        AvatarScaleRuleSnapshot rule,
        UniversalIncomingEvent incomingEvent,
        bool isTest,
        CancellationToken cancellationToken)
    {
        var addedHeight = GetSupporterGrowthHeightAdd(rule, incomingEvent, isTest);
        if (addedHeight <= 0)
        {
            WriteLog($"Avatar scale '{rule.Name}' skipped because this supporter event does not match a configured tier or bits range.");
            return;
        }

        var operationPriority = isTest
            ? AvatarScaleOperationPriority.TestSimulation
            : AvatarScaleOperationPriority.SupporterGrowth;
        var operation = await WaitForAvatarScaleOperationSlotAsync(
            rule.Id,
            rule.Name,
            operationPriority,
            isTest,
            cancellationToken);
        if (operation is null)
        {
            return;
        }

        var operationHandedOff = false;
        try
        {
            var scalingAllowed = await TryGetAvatarScalingAllowedAsync(cancellationToken);
            if (scalingAllowed == false)
            {
                WriteLog($"Avatar scale '{rule.Name}' skipped because VRChat reports /avatar/eyeheightscalingallowed is false. The current world or Udon may be blocking avatar scaling.");
                return;
            }

            var normalHeight = ApplyAvatarScaleHeightLimits(rule, rule.SupporterGrowthNormalHeightMeters, "supporter growth normal height");
            if (isTest)
            {
                var testTargetHeight = ApplyAvatarScaleHeightLimits(rule, normalHeight + addedHeight, "supporter growth test height");
                if (!await SendAvatarHeightForOperationAsync(
                        operation,
                        testTargetHeight,
                        rule.SmoothTransitionSeconds,
                        cancellationToken))
                {
                    return;
                }

                if (rule.SupporterGrowthInactivityTimerSeconds > 0)
                {
                    await Task.Delay(TimeSpan.FromSeconds(Math.Max(1, rule.SupporterGrowthInactivityTimerSeconds)), cancellationToken);
                    if (!IsAvatarScaleOperationCurrent(operation))
                    {
                        WriteLog($"Supporter growth test/simulated effect '{rule.Name}' skipped its inactive reset because a newer scale effect is active.");
                        return;
                    }

                    await SendAvatarHeightForOperationAsync(
                        operation,
                        normalHeight,
                        rule.SmoothTransitionSeconds,
                        cancellationToken);
                }

                WriteLog($"Sent supporter growth test/simulated effect for '{rule.Name}' to {testTargetHeight:0.###}m (+{addedHeight:0.###}m).");
                return;
            }

            CancellationTokenSource? previousSessionCancellation;
            CancellationTokenSource sessionCancellation;
            double totalAddedHeight;
            double targetHeight;

            lock (stateGate)
            {
                if (!avatarScaleSupporterGrowthStates.TryGetValue(rule.Id, out var state))
                {
                    state = new ActiveAvatarScaleSupporterGrowthState();
                    avatarScaleSupporterGrowthStates[rule.Id] = state;
                }

                previousSessionCancellation = state.SessionCancellation;
                sessionCancellation = runtimeCancellation is null
                    ? new CancellationTokenSource()
                    : CancellationTokenSource.CreateLinkedTokenSource(runtimeCancellation.Token);
                state.SessionCancellation = sessionCancellation;

                var requestedAddedHeight = state.AddedHeightMeters + addedHeight;
                state.AddedHeightMeters = rule.SupporterGrowthMaxAddedHeightMeters > 0
                    ? Math.Min(requestedAddedHeight, rule.SupporterGrowthMaxAddedHeightMeters)
                    : requestedAddedHeight;
                totalAddedHeight = state.AddedHeightMeters;
                targetHeight = ApplyAvatarScaleHeightLimits(rule, normalHeight + totalAddedHeight, "supporter growth height");
            }

            previousSessionCancellation?.Cancel();

            operationHandedOff = true;
            _ = Task.Run(
                () => RunSupporterGrowthScaleSessionAsync(
                    operation,
                    rule.Id,
                    rule.Name,
                    targetHeight,
                    normalHeight,
                    rule.SmoothTransitionSeconds,
                    rule.SupporterGrowthInactivityTimerSeconds,
                    sessionCancellation),
                CancellationToken.None);

            WriteLog($"{incomingEvent.UserDisplayName} added {addedHeight:0.###}m to supporter growth '{rule.Name}' for a target of {targetHeight:0.###}m.");
        }
        finally
        {
            if (!operationHandedOff)
            {
                EndAvatarScaleOperation(operation);
            }
        }
    }

    private async Task RunSupporterGrowthScaleSessionAsync(
        ActiveAvatarScaleOperationTicket operation,
        Guid ruleId,
        string ruleName,
        double targetHeight,
        double normalHeight,
        double smoothTransitionSeconds,
        int inactivityTimerSeconds,
        CancellationTokenSource sessionCancellation)
    {
        var heightSessionId = Guid.Empty;
        try
        {
            var cancellationToken = sessionCancellation.Token;
            var activeWindowSeconds = Math.Max(1, smoothTransitionSeconds + Math.Max(1, inactivityTimerSeconds) + smoothTransitionSeconds);
            if (!await SendAvatarHeightForOperationAsync(
                    operation,
                    targetHeight,
                    smoothTransitionSeconds,
                    cancellationToken,
                    () =>
                    {
                        heightSessionId = StartAvatarScaleHeightSession(
                            ruleId,
                            ruleName,
                            normalHeight,
                            targetHeight,
                            activeWindowSeconds);
                    }))
            {
                return;
            }

            await Task.Delay(TimeSpan.FromSeconds(Math.Max(1, inactivityTimerSeconds)), cancellationToken);
            if (!IsAvatarScaleOperationCurrent(operation))
            {
                WriteLog($"Supporter growth '{ruleName}' skipped its inactive reset because a newer scale effect is active.");
                return;
            }

            lock (stateGate)
            {
                if (avatarScaleSupporterGrowthStates.TryGetValue(ruleId, out var state)
                    && ReferenceEquals(state.SessionCancellation, sessionCancellation))
                {
                    state.AddedHeightMeters = 0;
                }
            }

            var restoreHeight = ResolveAvatarScaleRestoreHeightForCurrentAvatar(normalHeight);
            if (await SendAvatarHeightForOperationAsync(operation, restoreHeight, smoothTransitionSeconds, cancellationToken))
            {
                ClearPendingAvatarScaleHeightRestoreForCurrentAvatar();
                WriteLog($"Supporter growth '{ruleName}' returned to normal height after no new subs or bits arrived.");
            }
        }
        catch (OperationCanceledException)
        {
        }
        catch (Exception ex)
        {
            WriteLog($"Supporter growth '{ruleName}' failed: {ex.Message}");
        }
        finally
        {
            lock (stateGate)
            {
                if (avatarScaleSupporterGrowthStates.TryGetValue(ruleId, out var state)
                    && ReferenceEquals(state.SessionCancellation, sessionCancellation))
                {
                    avatarScaleSupporterGrowthStates.Remove(ruleId);
                }
            }

            EndAvatarScaleHeightSession(ruleId, heightSessionId);
            EndAvatarScaleOperation(operation);
            sessionCancellation.Dispose();
        }
    }

    private static double GetSupporterGrowthHeightAdd(
        AvatarScaleRuleSnapshot rule,
        UniversalIncomingEvent incomingEvent,
        bool isTest)
    {
        if (isTest)
        {
            return rule.SupporterGrowthTier1HeightMeters;
        }

        return incomingEvent.TriggerType switch
        {
            UniversalTriggerType.Bits => GetSupporterGrowthBitsHeightAdd(rule, incomingEvent.Amount),
            UniversalTriggerType.Subscription => GetSupporterGrowthTierHeightAdd(rule, incomingEvent.SubscriptionTier),
            UniversalTriggerType.GiftSubscription => GetSupporterGrowthTierHeightAdd(rule, incomingEvent.SubscriptionTier)
                * Math.Max(1, incomingEvent.Amount),
            _ => 0
        };
    }

    private static double GetSupporterGrowthBitsHeightAdd(AvatarScaleRuleSnapshot rule, int bits)
    {
        if (bits <= 0)
        {
            return 0;
        }

        foreach (var range in rule.SupporterGrowthBitRanges)
        {
            var minimumBits = Math.Max(1, range.MinimumBits);
            var maximumBits = Math.Max(0, range.MaximumBits);
            if (bits >= minimumBits && (maximumBits == 0 || bits <= maximumBits))
            {
                return Math.Max(0, range.HeightAddedMeters);
            }
        }

        return 0;
    }

    private static double GetSupporterGrowthTierHeightAdd(AvatarScaleRuleSnapshot rule, string tier)
    {
        return tier.Trim() switch
        {
            "1000" => rule.SupporterGrowthTier1HeightMeters,
            "2000" => rule.SupporterGrowthTier2HeightMeters,
            "3000" => rule.SupporterGrowthTier3HeightMeters,
            _ => 0
        };
    }

    private async Task SendAvatarHeightValueAsync(double heightMeters, CancellationToken cancellationToken)
    {
        var floatValue = (float)heightMeters;
        var packet = vrChatOscClient.BuildPacketForAddress(
            "/avatar/eyeheight",
            OscParameterType.Float,
            floatValue.ToString("G9", CultureInfo.InvariantCulture));
        await oscRouterService.SendToVrChatAsync(packet, cancellationToken);
        ObserveOscValue(new OscObservedValue("/avatar/eyeheight", OscParameterType.Float, floatValue));
    }

    private static double SmoothStep(double progress)
    {
        var clampedProgress = Math.Clamp(progress, 0, 1);
        return clampedProgress * clampedProgress * clampedProgress
            * (clampedProgress * ((clampedProgress * 6) - 15) + 10);
    }

    private async Task<double?> TryGetCurrentAvatarHeightAsync(CancellationToken cancellationToken)
    {
        lock (stateGate)
        {
            if (TryGetObservedFloatLocked("/avatar/eyeheight", out var height))
            {
                return height;
            }
        }

        try
        {
            var observedValue = await oscRouterService.GetCurrentOscValueAsync("/avatar/eyeheight", cancellationToken);
            if (observedValue?.ParameterType == OscParameterType.Float && observedValue.Value is float floatValue)
            {
                ObserveOscValue(observedValue);
                return floatValue;
            }
        }
        catch (Exception ex)
        {
            WriteLog($"Crystal Relay could not read /avatar/eyeheight through OSCQuery yet. Relative and multiplier scaling will use a normal 1.6m seed if needed. {ex.Message}");
        }

        return null;
    }

    private async Task<bool?> TryGetAvatarScalingAllowedAsync(CancellationToken cancellationToken)
    {
        lock (stateGate)
        {
            if (TryGetObservedBoolLocked("/avatar/eyeheightscalingallowed", out var allowed))
            {
                return allowed;
            }
        }

        try
        {
            var observedValue = await oscRouterService.GetCurrentOscValueAsync("/avatar/eyeheightscalingallowed", cancellationToken);
            if (observedValue?.ParameterType == OscParameterType.Bool && observedValue.Value is bool boolValue)
            {
                ObserveOscValue(observedValue);
                return boolValue;
            }
        }
        catch (Exception ex)
        {
            WriteLog($"Crystal Relay could not read /avatar/eyeheightscalingallowed yet, so avatar scaling will be attempted as allowed. {ex.Message}");
        }

        return null;
    }

    private double ResolveAvatarScaleTargetHeight(AvatarScaleRuleSnapshot rule, double? currentHeight)
    {
        var current = currentHeight ?? 1.6;
        return rule.ScaleMode switch
        {
            AvatarScaleMode.RandomHeight => Random.Shared.NextDouble()
                * (Math.Max(rule.MinimumHeightMeters, rule.MaximumHeightMeters) - Math.Min(rule.MinimumHeightMeters, rule.MaximumHeightMeters))
                + Math.Min(rule.MinimumHeightMeters, rule.MaximumHeightMeters),
            AvatarScaleMode.RelativeHeight => ClampRelativeScaleTarget(rule, currentHeight, current + rule.RelativeHeightMeters),
            AvatarScaleMode.Multiplier => current * Math.Max(0.01, rule.HeightMultiplier),
            AvatarScaleMode.Preset => AvatarScaleRule.GetPresetHeight(rule.Preset),
            _ => rule.TargetHeightMeters
        };
    }

    private static bool IsRelativeScaleAtLimit(
        AvatarScaleRuleSnapshot rule,
        double? currentHeight,
        out string limitMessage)
    {
        limitMessage = string.Empty;
        if (rule.ScaleMode != AvatarScaleMode.RelativeHeight
            || currentHeight is null
            || rule.RelativeHeightMeters == 0)
        {
            return false;
        }

        if (rule.RelativeHeightMeters < 0
            && currentHeight.Value <= rule.RelativeMinimumHeightMeters)
        {
            limitMessage = $"the current height is already at or below the relative minimum of {rule.RelativeMinimumHeightMeters:0.###}m.";
            return true;
        }

        if (rule.RelativeHeightMeters > 0
            && currentHeight.Value >= rule.RelativeMaximumHeightMeters)
        {
            limitMessage = $"the current height is already at or above the relative maximum of {rule.RelativeMaximumHeightMeters:0.###}m.";
            return true;
        }

        return false;
    }

    private static double ClampRelativeScaleTarget(
        AvatarScaleRuleSnapshot rule,
        double? currentHeight,
        double targetHeight)
    {
        if (currentHeight is null)
        {
            return targetHeight;
        }

        if (rule.RelativeHeightMeters < 0)
        {
            return Math.Max(targetHeight, rule.RelativeMinimumHeightMeters);
        }

        if (rule.RelativeHeightMeters > 0)
        {
            return Math.Min(targetHeight, rule.RelativeMaximumHeightMeters);
        }

        return targetHeight;
    }

    private static double ClampAvatarScaleHeight(AvatarScaleRuleSnapshot rule, double value)
    {
        if (double.IsNaN(value) || double.IsInfinity(value))
        {
            return 1.6;
        }

        return Math.Clamp(
            value,
            rule.AdvancedRangeEnabled ? AvatarScaleRule.AdvancedMinimumHeightMeters : AvatarScaleRule.SafeMinimumHeightMeters,
            rule.AdvancedRangeEnabled ? AvatarScaleRule.AdvancedMaximumHeightMeters : AvatarScaleRule.SafeMaximumHeightMeters);
    }

    private double ApplyAvatarScaleHeightLimits(
        AvatarScaleRuleSnapshot rule,
        double value,
        string targetDescription)
    {
        var clampedValue = ClampAvatarScaleHeight(rule, value);
        if (!rule.BypassVrChatScaleLimits)
        {
            return ClampToVrChatScaleLimits(clampedValue);
        }

        var vrChatLimitedValue = GetVrChatScaleLimitedHeight(clampedValue);
        if (Math.Abs(vrChatLimitedValue - clampedValue) > 0.0001)
        {
            var displayName = string.IsNullOrWhiteSpace(rule.Name) ? "Avatar Scale" : rule.Name;
            WriteLog($"Avatar scale '{displayName}' bypassed VRChat world min/max for {targetDescription}; using {clampedValue:0.###}m instead of VRChat's {vrChatLimitedValue:0.###}m limit.");
        }

        return clampedValue;
    }

    private double ClampToVrChatScaleLimits(double value)
    {
        return GetVrChatScaleLimitedHeight(value);
    }

    private double GetVrChatScaleLimitedHeight(double value)
    {
        lock (stateGate)
        {
            if (TryGetObservedFloatLocked("/avatar/eyeheightmin", out var minimumHeight)
                && minimumHeight > 0)
            {
                value = Math.Max(value, minimumHeight);
            }

            if (TryGetObservedFloatLocked("/avatar/eyeheightmax", out var maximumHeight)
                && maximumHeight > 0)
            {
                value = Math.Min(value, maximumHeight);
            }
        }

        return value;
    }

    private static AvatarScaleRuleSnapshot[] SelectMatchingAvatarScaleRules(
        IReadOnlyList<AvatarScaleRuleSnapshot> rules,
        UniversalIncomingEvent incomingEvent)
    {
        return rules
            .Where(rule => rule.IsEnabled && AvatarScaleRuleMatches(rule, incomingEvent))
            .ToArray();
    }

    private static bool AvatarScaleRuleMatches(
        AvatarScaleRuleSnapshot rule,
        UniversalIncomingEvent incomingEvent)
    {
        if (incomingEvent.TriggerType == UniversalTriggerType.ChatCommand
            && rule.TriggerType == AvatarScaleTriggerType.ChannelPointReward
            && rule.ChatCommandEnabled)
        {
            return ChatCommandUtility.MessageMatches(rule.CommandText, incomingEvent.ChatMessageText)
                && UserCanTriggerChatCommand(rule.ChatCommandPermission, incomingEvent);
        }

        if (!AvatarScaleTriggerTypeMatchesIncoming(rule.TriggerType, incomingEvent.TriggerType))
        {
            return false;
        }

        return rule.TriggerType switch
        {
            AvatarScaleTriggerType.ChatCommand => ChatCommandUtility.MessageMatches(rule.CommandText, incomingEvent.ChatMessageText)
                && UserCanTriggerChatCommand(rule.ChatCommandPermission, incomingEvent),
            AvatarScaleTriggerType.ChannelPointReward => AvatarScaleRewardMatches(rule, incomingEvent),
            AvatarScaleTriggerType.Bits => incomingEvent.Amount >= Math.Min(rule.MinimumBits, rule.MaximumBits)
                && incomingEvent.Amount <= Math.Max(rule.MinimumBits, rule.MaximumBits),
            AvatarScaleTriggerType.Subscription or AvatarScaleTriggerType.GiftSubscription => AvatarScaleSubscriptionMatches(rule, incomingEvent),
            AvatarScaleTriggerType.Follow => true,
            AvatarScaleTriggerType.SupporterGrowth => SupporterGrowthEventMatches(rule, incomingEvent),
            _ => false
        };
    }

    private static bool AvatarScaleTriggerTypeMatchesIncoming(
        AvatarScaleTriggerType triggerType,
        UniversalTriggerType incomingType)
    {
        return triggerType switch
        {
            AvatarScaleTriggerType.ChatCommand => incomingType == UniversalTriggerType.ChatCommand,
            AvatarScaleTriggerType.ChannelPointReward => incomingType == UniversalTriggerType.ChannelPointReward,
            AvatarScaleTriggerType.Bits => incomingType == UniversalTriggerType.Bits,
            AvatarScaleTriggerType.Subscription => incomingType == UniversalTriggerType.Subscription,
            AvatarScaleTriggerType.GiftSubscription => incomingType == UniversalTriggerType.GiftSubscription,
            AvatarScaleTriggerType.Follow => incomingType == UniversalTriggerType.Follow,
            AvatarScaleTriggerType.SupporterGrowth => incomingType is UniversalTriggerType.Bits
                or UniversalTriggerType.Subscription
                or UniversalTriggerType.GiftSubscription,
            _ => false
        };
    }

    private static bool AvatarScaleMasterRewardMatches(
        AvatarScaleMasterRewardSnapshot masterReward,
        UniversalIncomingEvent incomingEvent)
    {
        if (incomingEvent.TriggerType != UniversalTriggerType.ChannelPointReward)
        {
            return false;
        }

        if (!string.IsNullOrWhiteSpace(masterReward.RewardId)
            && !string.IsNullOrWhiteSpace(incomingEvent.RewardId)
            && string.Equals(masterReward.RewardId, incomingEvent.RewardId, StringComparison.Ordinal))
        {
            return true;
        }

        if (string.IsNullOrWhiteSpace(masterReward.RewardTitle)
            || string.IsNullOrWhiteSpace(incomingEvent.RewardTitle))
        {
            return false;
        }

        return string.Equals(masterReward.RewardTitle, incomingEvent.RewardTitle, StringComparison.OrdinalIgnoreCase)
            || string.Equals(
                ManagedRewardPresentation.StripPrefix(masterReward.RewardTitle),
                ManagedRewardPresentation.StripPrefix(incomingEvent.RewardTitle),
                StringComparison.OrdinalIgnoreCase);
    }

    private void HandleAvatarScaleMasterRewardRedeemed(
        AvatarScaleMasterRewardSnapshot masterReward,
        UniversalIncomingEvent incomingEvent)
    {
        var now = DateTimeOffset.UtcNow;
        DateTimeOffset unlockUntil;
        var cooldownUntil = masterReward.CooldownSeconds > 0
            ? now.AddSeconds(masterReward.CooldownSeconds)
            : DateTimeOffset.MinValue;

        lock (stateGate)
        {
            var unlockStart = avatarScaleMasterUnlockUntil > now
                ? avatarScaleMasterUnlockUntil
                : now;
            avatarScaleMasterUnlockUntil = unlockStart.AddSeconds(Math.Max(1, masterReward.UnlockDurationSeconds));
            avatarScaleMasterCooldownUntil = cooldownUntil;
            unlockUntil = avatarScaleMasterUnlockUntil;
        }

        ScheduleAvatarScaleMasterStateNotification(unlockUntil - now, isUnlockNotification: true);
        if (cooldownUntil > now)
        {
            ScheduleAvatarScaleMasterStateNotification(cooldownUntil - now, isUnlockNotification: false);
        }
        else
        {
            CancelAvatarScaleMasterCooldownNotification();
        }

        WriteLog($"{incomingEvent.UserDisplayName} unlocked Avatar Scaling rewards for {DescribeDuration((unlockUntil - now).TotalSeconds)}.");
        ManagedRewardAvailabilityChanged?.Invoke();
    }

    private static bool SupporterGrowthEventMatches(
        AvatarScaleRuleSnapshot rule,
        UniversalIncomingEvent incomingEvent)
    {
        return GetSupporterGrowthHeightAdd(rule, incomingEvent, isTest: false) > 0;
    }

    private static bool AvatarScaleRewardMatches(
        AvatarScaleRuleSnapshot rule,
        UniversalIncomingEvent incomingEvent)
    {
        if (!string.IsNullOrWhiteSpace(rule.RewardId)
            && !string.IsNullOrWhiteSpace(incomingEvent.RewardId)
            && string.Equals(rule.RewardId, incomingEvent.RewardId, StringComparison.Ordinal))
        {
            return true;
        }

        var rewardTitle = GetAvatarScaleRewardMatchTitle(rule);
        if (string.IsNullOrWhiteSpace(rewardTitle)
            || string.IsNullOrWhiteSpace(incomingEvent.RewardTitle))
        {
            return false;
        }

        return ManagedRewardPresentation.HasSameTitleIdentity(incomingEvent.RewardTitle, rewardTitle);
    }

    private static string GetAvatarScaleRewardMatchTitle(AvatarScaleRuleSnapshot rule)
    {
        if (!string.IsNullOrWhiteSpace(rule.RewardTitle))
        {
            return rule.RewardTitle.Trim();
        }

        return string.IsNullOrWhiteSpace(rule.Name)
            ? "Avatar Scale"
            : rule.Name.Trim();
    }

    private static bool AvatarScaleSubscriptionMatches(
        AvatarScaleRuleSnapshot rule,
        UniversalIncomingEvent incomingEvent)
    {
        if (!string.IsNullOrWhiteSpace(rule.SubscriptionTier)
            && !string.Equals(rule.SubscriptionTier, incomingEvent.SubscriptionTier, StringComparison.OrdinalIgnoreCase))
        {
            return false;
        }

        if (rule.MinimumMonths < 0 && rule.MaximumMonths < 0)
        {
            return true;
        }

        var minimumMonths = Math.Max(0, Math.Min(rule.MinimumMonths, rule.MaximumMonths));
        var maximumMonths = Math.Max(minimumMonths, Math.Max(rule.MinimumMonths, rule.MaximumMonths));
        return incomingEvent.SubscriptionMonths >= minimumMonths
            && incomingEvent.SubscriptionMonths <= maximumMonths;
    }

    private static bool IsAllowedDuringSupporterOverride(TriggerRuleSnapshot rule)
    {
        if (rule.TriggerType != TwitchTriggerType.ChannelPoints)
        {
            return false;
        }

        if (rule.ActionType == OscActionType.PlayerMovement)
        {
            return true;
        }

        return rule.ActionType == OscActionType.AvatarParameter
            && !rule.IsGlobalOverride
            && !rule.BelongsToMasterAvatarProfile
            && rule.ParameterType is OscParameterType.Bool or OscParameterType.Int or OscParameterType.Float;
    }

    private static bool ShouldBlockRuleDuringSupporterOverride(TriggerRuleSnapshot rule) =>
        rule.TriggerType == TwitchTriggerType.ChannelPoints && !IsAllowedDuringSupporterOverride(rule);

    // This set only changes when the live rule configuration changes, so cache it once
    // instead of rescanning every rule while the runtime is answering availability checks.
    private void RefreshSupporterOverrideBlockedRuleIds(IReadOnlyList<TriggerRuleSnapshot> rules)
    {
        lock (stateGate)
        {
            supporterOverrideBlockedRuleIds.Clear();
            foreach (var rule in rules)
            {
                if (ShouldBlockRuleDuringSupporterOverride(rule))
                {
                    supporterOverrideBlockedRuleIds.Add(rule.Id);
                }
            }
        }
    }

    private static int CompareSupporterOverridePriority(TriggerRuleSnapshot left, TriggerRuleSnapshot right)
    {
        var triggerTypeComparison = GetSupporterOverrideTriggerTypeRank(left.TriggerType)
            .CompareTo(GetSupporterOverrideTriggerTypeRank(right.TriggerType));
        if (triggerTypeComparison != 0)
        {
            return triggerTypeComparison;
        }

        return Math.Max(1, left.MinimumAmount).CompareTo(Math.Max(1, right.MinimumAmount));
    }

    private static int GetSupporterOverrideTriggerTypeRank(TwitchTriggerType triggerType) => triggerType switch
    {
        TwitchTriggerType.Subscriptions => 2,
        TwitchTriggerType.Bits => 1,
        _ => 0
    };

    private static TimeSpan GetSupporterOverrideDuration(TriggerRuleSnapshot rule, BridgeIncomingEvent bridgeEvent)
    {
        var (amountUnits, secondsPerUnit) = rule.TriggerType == TwitchTriggerType.Subscriptions
            ? (rule.SubscriptionsAmountUnitsPerDuration, rule.SubscriptionsSecondsPerAmountUnit)
            : (rule.BitsAmountUnitsPerDuration, rule.BitsSecondsPerAmountUnit);
        var seconds = rule.AmountScaledDurationEnabled
            ? (double)Math.Max(1, bridgeEvent.Amount) / Math.Max(1, amountUnits) * Math.Max(1, secondsPerUnit)
            : Math.Max(1, rule.DurationSeconds);
        return TimeSpan.FromSeconds(Math.Min(Math.Max(1, seconds), TimeSpan.MaxValue.TotalSeconds));
    }

    private static TimeSpan ClampSupporterOverrideAddedDuration(
        TriggerRuleSnapshot rule,
        TimeSpan requestedDuration,
        TimeSpan existingRemainingDuration)
    {
        if (requestedDuration <= TimeSpan.Zero || !rule.MaxAccumulatedDurationEnabled)
        {
            return requestedDuration;
        }

        var maxAccumulatedDuration = TimeSpan.FromSeconds(Math.Max(1, rule.MaxAccumulatedDurationSeconds));
        var remainingCapacity = maxAccumulatedDuration - existingRemainingDuration;
        if (remainingCapacity <= TimeSpan.Zero)
        {
            return TimeSpan.Zero;
        }

        return requestedDuration <= remainingCapacity
            ? requestedDuration
            : remainingCapacity;
    }

    private TimeSpan GetCurrentSupporterOverrideRemainingDurationLocked(Guid ruleId, DateTimeOffset now)
    {
        var remainingDuration = TimeSpan.Zero;

        if (activeSupporterOverride is not null
            && activeSupporterOverride.Rule.Id == ruleId
            && activeSupporterOverride.ActiveUntil > now)
        {
            remainingDuration += activeSupporterOverride.ActiveUntil - now;
        }

        foreach (var queuedOverride in queuedSupporterOverrides)
        {
            if (queuedOverride.Rule.Id == ruleId
                && queuedOverride.RemainingDuration > TimeSpan.Zero)
            {
                remainingDuration += queuedOverride.RemainingDuration;
            }
        }

        return remainingDuration;
    }

    private DateTimeOffset GetSupporterOverrideSequenceEndsAtLocked(DateTimeOffset now)
    {
        var remainingDuration = TimeSpan.Zero;
        if (activeSupporterOverride is not null && activeSupporterOverride.ActiveUntil > now)
        {
            remainingDuration += activeSupporterOverride.ActiveUntil - now;
        }

        foreach (var queuedOverride in queuedSupporterOverrides)
        {
            if (queuedOverride.RemainingDuration > TimeSpan.Zero)
            {
                remainingDuration += queuedOverride.RemainingDuration;
            }
        }

        return remainingDuration <= TimeSpan.Zero
            ? DateTimeOffset.MinValue
            : now.Add(remainingDuration);
    }

    private bool IsSupporterOverrideSequenceActiveLocked(DateTimeOffset now)
    {
        return (activeSupporterOverride is not null || queuedSupporterOverrides.Count > 0)
            && GetSupporterOverrideSequenceEndsAtLocked(now) > now;
    }

    private HashSet<Guid> GetSupporterOverrideBlockedRuleIdsLocked(DateTimeOffset now)
    {
        if (!IsSupporterOverrideSequenceActiveLocked(now) || supporterOverrideBlockedRuleIds.Count == 0)
        {
            return [];
        }

        return [.. supporterOverrideBlockedRuleIds];
    }

    private bool TryGetSupporterOverrideSuppressionUntilLocked(
        Guid ruleId,
        DateTimeOffset now,
        out DateTimeOffset suppressionUntil)
    {
        if (!IsSupporterOverrideSequenceActiveLocked(now)
            || !supporterOverrideBlockedRuleIds.Contains(ruleId))
        {
            suppressionUntil = default;
            return false;
        }

        suppressionUntil = GetSupporterOverrideSequenceEndsAtLocked(now);
        return suppressionUntil > now;
    }

    private int GetQueuedSupporterOverrideIndexLocked(Guid ruleId)
    {
        for (var index = 0; index < queuedSupporterOverrides.Count; index++)
        {
            if (queuedSupporterOverrides[index].Rule.Id == ruleId)
            {
                return index;
            }
        }

        return -1;
    }

    private int GetNextQueuedSupporterOverrideIndexLocked()
    {
        var bestIndex = -1;

        for (var index = 0; index < queuedSupporterOverrides.Count; index++)
        {
            var candidate = queuedSupporterOverrides[index];
            if (candidate.RemainingDuration <= TimeSpan.Zero)
            {
                continue;
            }

            if (bestIndex < 0)
            {
                bestIndex = index;
                continue;
            }

            var best = queuedSupporterOverrides[bestIndex];
            var priorityComparison = CompareSupporterOverridePriority(candidate.Rule, best.Rule);
            if (priorityComparison > 0
                || (priorityComparison == 0 && candidate.QueueOrder < best.QueueOrder))
            {
                bestIndex = index;
            }
        }

        return bestIndex;
    }

    private static TriggerRuleSnapshot CreateTimedSupporterOverrideExecutionRule(
        TriggerRuleSnapshot rule,
        TimeSpan duration,
        int cooldownSeconds) =>
        rule with
        {
            DurationSeconds = Math.Max(1, (int)Math.Ceiling(duration.TotalSeconds)),
            CooldownSeconds = cooldownSeconds
        };

    private bool TryHandleStreamStateNotification(EventSubNotification notification)
    {
        if (string.Equals(notification.SubscriptionType, "stream.online", StringComparison.Ordinal))
        {
            isBroadcasterLive = true;
            nextTriggerInfoAnnouncementAt = DateTimeOffset.MinValue;
            StreamStateChanged?.Invoke(true);
            WriteLog("Broadcaster is live on Twitch.");
            return true;
        }

        if (string.Equals(notification.SubscriptionType, "stream.offline", StringComparison.Ordinal))
        {
            isBroadcasterLive = false;
            nextTriggerInfoAnnouncementAt = DateTimeOffset.MinValue;
            StreamStateChanged?.Invoke(false);
            WriteLog("Broadcaster is offline on Twitch.");
            return true;
        }

        return false;
    }

    private async Task RefreshBroadcasterLiveStateAsync(CancellationToken cancellationToken)
    {
        if (activeConfiguration is null || broadcaster is null)
        {
            isBroadcasterLive = false;
            nextTriggerInfoAnnouncementAt = DateTimeOffset.MinValue;
            StreamStateChanged?.Invoke(false);
            return;
        }

        try
        {
            var isLive = await twitchApiClient.IsBroadcasterLiveAsync(
                broadcaster.AccessToken,
                activeConfiguration.TwitchClientId,
                broadcaster.UserId,
                cancellationToken);
            isBroadcasterLive = isLive;
            if (!isLive)
            {
                nextTriggerInfoAnnouncementAt = DateTimeOffset.MinValue;
            }
            StreamStateChanged?.Invoke(isLive);
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            throw;
        }
        catch
        {
        }
    }

    private async Task RefreshChatBadgeCatalogAsync(CancellationToken cancellationToken)
    {
        if (activeConfiguration is null || broadcaster is null)
        {
            return;
        }

        if (!HasScope(broadcaster, "user:read:chat"))
        {
            lock (stateGate)
            {
                chatBadgeImageUrls.Clear();
                chatEmoteImageUrls.Clear();
                chatEmoteImageUrlInsertionOrder.Clear();
                cachedChatEmoteSetIds.Clear();
                cachedChatEmoteSetIdInsertionOrder.Clear();
                thirdPartyChatEmoteImageUrls.Clear();
                thirdPartyChatEmoteCodeInsertionOrder.Clear();
                thirdPartyChatEmoteIndex = new Dictionary<char, IReadOnlyList<ThirdPartyChatEmoteEntry>>();
                nextThirdPartyChatEmoteRefreshAt = DateTimeOffset.MinValue;
            }

            return;
        }

        try
        {
            var globalBadgeSets = await twitchApiClient.GetGlobalChatBadgesAsync(
                broadcaster.AccessToken,
                activeConfiguration.TwitchClientId,
                cancellationToken);
            var channelBadgeSets = await twitchApiClient.GetChannelChatBadgesAsync(
                broadcaster.AccessToken,
                activeConfiguration.TwitchClientId,
                broadcaster.UserId,
                cancellationToken);

            var nextCatalog = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
            IndexBadgeSets(nextCatalog, globalBadgeSets);
            IndexBadgeSets(nextCatalog, channelBadgeSets);

            lock (stateGate)
            {
                chatBadgeImageUrls.Clear();
                foreach (var pair in nextCatalog)
                {
                    chatBadgeImageUrls[pair.Key] = pair.Value;
                }
            }
        }
        catch (Exception ex)
        {
            WriteLog($"Could not refresh Twitch badge catalog yet: {ex.Message}");
        }

        await RefreshThirdPartyChatEmoteCatalogAsync(cancellationToken);
    }

    private async Task ExecuteRuleAsync(TriggerRuleSnapshot rule, BridgeIncomingEvent bridgeEvent, CancellationToken cancellationToken)
    {
        if (IsTimedSupporterOverrideRule(rule))
        {
            await HandleTimedSupporterOverrideTriggerAsync(rule, bridgeEvent, cancellationToken, queuedReplay: false);
            return;
        }

        var now = DateTimeOffset.UtcNow;
        var cooldownSeconds = GetCooldownSeconds(rule);
        var queuedCount = 0;

        lock (stateGate)
        {
            if (cooldownSeconds <= 0)
            {
                cooldowns.Remove(rule.Id);
            }
            else if (cooldowns.TryGetValue(rule.Id, out var cooldownUntil) && cooldownUntil > now)
            {
                queuedCount = EnqueueTrigger(rule, bridgeEvent);
                WriteLog($"Queued '{rule.Name}' because it is still on cooldown for {DescribeDuration((cooldownUntil - now).TotalSeconds)}. {queuedCount} waiting.");
                EnsureQueuedRuleDrain(rule.Id);
                return;
            }
        }

        await ExecuteRuleActionAsync(rule, bridgeEvent, cancellationToken, isTest: false, queuedReplay: false, allowLaneQueue: true);
    }

    // Executes one rule action end-to-end. This is where Crystal Relay checks pause state,
    // applies movement lane rules, sends OSC, starts cooldowns, schedules resets, and logs the trigger.
    private async Task ExecuteRuleActionAsync(
        TriggerRuleSnapshot rule,
        BridgeIncomingEvent? bridgeEvent,
        CancellationToken cancellationToken,
        bool isTest,
        bool queuedReplay,
        bool allowLaneQueue)
    {
        if (!isTest && AreRedeemsPaused())
        {
            LogRedeemsPaused();
            return;
        }

        if (ShouldBlockAvatarChangeDuringActiveScaling(rule, isTest))
        {
            WriteLog($"Blocked avatar-change reward '{rule.Name}' because Avatar Scaling is active. Bits + Subs overrides can still change avatars.");
            return;
        }

        var queuedLaneCount = 0;
        if (allowLaneQueue && TryEnqueueLaneAction(rule, bridgeEvent, isTest, out queuedLaneCount))
        {
            if (!isTest && IsBitsOutfitSetTriggerRule(rule))
            {
                var viewerName = bridgeEvent?.UserDisplayName ?? "Viewer";
                WriteLog($"Queued Bits outfit Set Trigger '{rule.Name}' for {viewerName} until the current outfit restore finishes. Position {queuedLaneCount}.");
            }
            else
            {
                WriteLog(isTest
                    ? $"Queued test trigger for '{rule.Name}' until the current action finishes. {queuedLaneCount} waiting."
                    : $"Queued '{rule.Name}' until the current action finishes. {queuedLaneCount} waiting.");
            }
            return;
        }

        var isMovementStopAction = rule.ActionType == OscActionType.PlayerMovement && IsSoftLockMovement(rule.MovementDirection);
        var cooldownSeconds = GetCooldownSeconds(rule);
        var capturedReturnAvatar = (rule.ActionType is OscActionType.AvatarChange or OscActionType.AvatarRoulet) && rule.DurationSeconds > 0
            ? GetSharedReturnAvatarSnapshot()
            : SharedReturnAvatarSnapshot.Empty;
        if (rule.ActionType is OscActionType.AvatarChange or OscActionType.AvatarRoulet
            && rule.DurationSeconds > 0
            && string.IsNullOrWhiteSpace(capturedReturnAvatar.AvatarId))
        {
            throw new InvalidOperationException("Pick the return avatar first before timed avatar-switch redeems can switch back.");
        }

        if (isMovementStopAction)
        {
            var movementDisplayValue = DescribeMovementAction(rule.MovementDirection);
            if (activeConfiguration?.DesktopModeInputLockEnabled == true)
            {
                try
                {
                    await ExecuteDesktopInputLockAsync(rule, cancellationToken);
                }
                catch (Exception ex)
                {
                    WriteLog($"Crystal Relay could not start the desktop input lock for '{rule.Name}', so it fell back to a VRChat soft lock. {ex.Message}");
                    await ExecuteMovementSoftLockAsync(rule, cancellationToken);
                }
            }
            else
            {
                await ExecuteMovementSoftLockAsync(rule, cancellationToken);
            }

            lock (stateGate)
            {
                if (!isTest)
                {
                    cooldowns.Remove(rule.Id);
                }
            }

            if (!isTest)
            {
                CancelCooldownStateNotification(rule.Id);
                UpdateActiveRuleLockoutState(rule);
            }

            if (isTest)
            {
                WriteLog(queuedReplay
                    ? $"Sent queued test trigger for '{rule.Name}'."
                    : $"Sent a test trigger for '{rule.Name}'.");
            }
            else if (bridgeEvent is not null)
            {
                WriteLog(queuedReplay
                    ? $"{bridgeEvent.UserDisplayName} triggered '{rule.Name}' from the queue."
                    : $"{bridgeEvent.UserDisplayName} triggered '{rule.Name}'.");
            }

            if (!isTest && bridgeEvent is not null)
            {
                await TrySendBotMessageAsync(rule, bridgeEvent, movementDisplayValue, cancellationToken);
            }

            return;
        }

        var action = await ResolveActionAsync(
            rule,
            cancellationToken,
            preferLocalInstantToggleState: rule.ParameterType == OscParameterType.Bool && rule.DurationSeconds <= 0,
            capturedReturnAvatar);
        var laneKeys = GetActionLaneKeys(rule, action);
        var laneLeaseId = laneKeys.Count == 0 ? Guid.Empty : Guid.NewGuid();
        var effectiveTimedActionSeconds = Math.Max(1d, rule.DurationSeconds);
        if (rule.ActionType == OscActionType.SetTrigger && action.SetTriggerRestorePlan is not null)
        {
            var minimumSeconds = SetTriggerDiffObservationDelay.TotalSeconds;
            if (effectiveTimedActionSeconds < minimumSeconds)
            {
                WriteLog($"Set Trigger '{rule.Name}' active time was extended from {DescribeDuration(effectiveTimedActionSeconds)} to {DescribeDuration(minimumSeconds)} so Crystal Relay can re-check LocalAvatarData before restoring.");
                effectiveTimedActionSeconds = minimumSeconds;
            }
        }

        await SendPacketsToVrChatAsync(
            action.Packets,
            cancellationToken,
            rule.ActionType == OscActionType.SetTrigger ? SetTriggerPacketSpacing : null);
        if (rule.ActionType == OscActionType.SetTrigger)
        {
            WriteLog($"Sent Set Trigger '{rule.Name}' outfit values ({action.Packets.Count} param{(action.Packets.Count == 1 ? string.Empty : "s")}).");
        }

        RememberAvatarParameterValues(rule, action.ObservedValues.Count > 0 ? action.ObservedValues : null, action.DisplayValue);
        if (!isTest)
        {
            UpdateActiveRuleLockoutState(rule);
        }

        lock (stateGate)
        {
            foreach (var laneKey in laneKeys)
            {
                actionLanes[laneKey] = new ActiveMovementLaneState(
                    laneLeaseId,
                    DateTimeOffset.UtcNow.AddSeconds(effectiveTimedActionSeconds),
                    rule.Id,
                    false);
            }

            if (!isTest)
            {
                if (cooldownSeconds > 0)
                {
                    cooldowns[rule.Id] = DateTimeOffset.UtcNow.AddSeconds(cooldownSeconds);
                }
                else
                {
                    cooldowns.Remove(rule.Id);
                }
            }
        }

        if (!isTest)
        {
            if (cooldownSeconds > 0)
            {
                ScheduleCooldownStateNotification(rule.Id, TimeSpan.FromSeconds(cooldownSeconds));
            }
            else
            {
                CancelCooldownStateNotification(rule.Id);
            }
        }

        if (rule.ActionType is OscActionType.AvatarChange or OscActionType.AvatarRoulet
            && !string.IsNullOrWhiteSpace(action.AvatarTargetId))
        {
            LogPaidAvatarChangeAllowedDuringActiveScaling(rule);
            SetCurrentVrChatAvatar(
                action.AvatarTargetId,
                notify: true,
                GetAvatarScaleAvatarChangeCarryoverMode(rule));
            if (rule.ActionType == OscActionType.AvatarChange
                && rule.DurationSeconds <= 0)
            {
                SetSharedReturnAvatar(action.AvatarTargetId, action.AvatarTargetName, notify: true);
            }
        }

        if (isTest)
        {
            WriteLog(queuedReplay
                ? $"Sent queued test trigger for '{rule.Name}'."
                : $"Sent a test trigger for '{rule.Name}'.");
        }
        else if (bridgeEvent is not null)
        {
            WriteLog(queuedReplay
                ? $"{bridgeEvent.UserDisplayName} triggered '{rule.Name}' from the queue."
                : $"{bridgeEvent.UserDisplayName} triggered '{rule.Name}'.");
        }

        var lockoutDurationSeconds = isTest ? 0 : GetLockoutDurationSeconds(rule);
        if (!isTest)
        {
            UpdateActiveAvatarSwitchLockoutState(rule);
        }

        var shouldNotifyManagedRewardState = !isTest && cooldownSeconds > 0;
        if ((action.HasResetPackets && effectiveTimedActionSeconds > 0)
            || lockoutDurationSeconds > 0)
        {
            var resetDelaySeconds = action.HasResetPackets
                ? effectiveTimedActionSeconds
                : lockoutDurationSeconds;
            if (rule.ActionType == OscActionType.PlayerMovement
                && rule.MovementDirection == PlayerMovementDirection.Jump)
            {
                ScheduleJumpPulseReset(rule, action, resetDelaySeconds, laneKeys.FirstOrDefault(), laneLeaseId, notifyManagedRewardState: false);
            }
            else
            {
                ScheduleReset(rule, action, resetDelaySeconds, laneKeys, laneLeaseId, notifyManagedRewardState: false);
            }
        }

        if (shouldNotifyManagedRewardState)
        {
            ManagedRewardAvailabilityChanged?.Invoke();
        }

        if (!isTest && bridgeEvent is not null)
        {
            await TrySendBotMessageAsync(rule, bridgeEvent, action.DisplayValue, cancellationToken);
        }
    }

    private async Task<ResolvedRuleAction> ResolveActionAsync(
        TriggerRuleSnapshot rule,
        CancellationToken cancellationToken,
        bool preferLocalInstantToggleState,
        SharedReturnAvatarSnapshot capturedReturnAvatar)
    {
        return rule.ActionType switch
            {
            OscActionType.AvatarParameter => await ResolveAvatarParameterActionAsync(rule, cancellationToken, preferLocalInstantToggleState),
            OscActionType.SetTrigger => await ResolveSetTriggerActionAsync(rule, cancellationToken),
            OscActionType.AvatarChange => ResolveAvatarChangeAction(rule, capturedReturnAvatar),
            OscActionType.AvatarRoulet => ResolveAvatarRouletAction(rule, capturedReturnAvatar),
            OscActionType.PlayerMovement => ResolvePlayerMovementAction(rule),
            _ => throw new InvalidOperationException($"Unsupported OSC action type: {rule.ActionType}")
        };
    }

    private ResolvedRuleAction ResolveAvatarChangeAction(
        TriggerRuleSnapshot rule,
        SharedReturnAvatarSnapshot capturedReturnAvatar)
    {
        return new ResolvedRuleAction(
            vrChatOscClient.BuildAvatarChangePacket(rule.AvatarChangeTargetId),
            rule.DurationSeconds > 0 && !string.IsNullOrWhiteSpace(capturedReturnAvatar.AvatarId)
                ? vrChatOscClient.BuildAvatarChangePacket(capturedReturnAvatar.AvatarId)
                : null,
            string.IsNullOrWhiteSpace(rule.AvatarTargetName) ? "selected avatar" : rule.AvatarTargetName,
            rule.AvatarChangeTargetId,
            rule.AvatarTargetName,
            capturedReturnAvatar.AvatarId,
            capturedReturnAvatar.AvatarName);
    }

    private ResolvedRuleAction ResolveAvatarRouletAction(
        TriggerRuleSnapshot rule,
        SharedReturnAvatarSnapshot capturedReturnAvatar)
    {
        var selectedAvatar = PickAvatarRouletTarget(rule);
        return new ResolvedRuleAction(
            vrChatOscClient.BuildAvatarChangePacket(selectedAvatar.AvatarId),
            !string.IsNullOrWhiteSpace(capturedReturnAvatar.AvatarId)
                ? vrChatOscClient.BuildAvatarChangePacket(capturedReturnAvatar.AvatarId)
                : null,
            selectedAvatar.AvatarName,
            selectedAvatar.AvatarId,
            selectedAvatar.AvatarName,
            capturedReturnAvatar.AvatarId,
            capturedReturnAvatar.AvatarName);
    }

    private ResolvedRuleAction ResolvePlayerMovementAction(TriggerRuleSnapshot rule)
    {
        var inputAddress = rule.MovementDirection switch
        {
            PlayerMovementDirection.Forward => "/input/MoveForward",
            PlayerMovementDirection.Backward => "/input/MoveBackward",
            PlayerMovementDirection.Left => "/input/MoveLeft",
            PlayerMovementDirection.Right => "/input/MoveRight",
            PlayerMovementDirection.Jump => "/input/Jump",
            PlayerMovementDirection.SpinLeft => "/input/LookLeft",
            PlayerMovementDirection.SpinRight => "/input/LookRight",
            _ => throw new InvalidOperationException($"Unsupported movement direction: {rule.MovementDirection}")
        };

        var holdSeconds = Math.Max(1, rule.DurationSeconds);
        var displayValue = rule.MovementDirection == PlayerMovementDirection.Jump
            ? holdSeconds > 1
                ? $"Repeated for {DescribeDuration(holdSeconds)}"
                : "Jumped"
            : $"Held for {DescribeDuration(holdSeconds)}";
        return new ResolvedRuleAction(
            vrChatOscClient.BuildInputButtonPacket(inputAddress, true),
            vrChatOscClient.BuildInputButtonPacket(inputAddress, false),
            displayValue);
    }

    private AvatarRouletSelection PickAvatarRouletTarget(TriggerRuleSnapshot rule)
    {
        var configuredNames = rule.AvatarRouletAvatarNames
            .Select(avatarName => avatarName?.Trim() ?? string.Empty)
            .ToArray();
        var configuredAvatars = rule.AvatarRouletAvatarIds
            .Select((avatarId, index) => new AvatarRouletSelection(
                avatarId?.Trim() ?? string.Empty,
                index < configuredNames.Length ? configuredNames[index] : string.Empty))
            .Where(selection => !string.IsNullOrWhiteSpace(selection.AvatarId))
            .DistinctBy(selection => selection.AvatarId, StringComparer.Ordinal)
            .ToArray();
        if (configuredAvatars.Length == 0)
        {
            throw new InvalidOperationException("Pick at least one avatar in the Avatar Roulette pool first.");
        }

        var currentAvatarId = GetCurrentVrChatAvatarId();
        string previousAvatarId;
        lock (stateGate)
        {
            previousAvatarId = lastAvatarRouletResultIds.TryGetValue(rule.Id, out var previousResult)
                ? previousResult
                : string.Empty;
        }

        var candidates = configuredAvatars
            .Where(selection =>
                !string.Equals(selection.AvatarId, currentAvatarId, StringComparison.Ordinal)
                && !string.Equals(selection.AvatarId, previousAvatarId, StringComparison.Ordinal))
            .ToArray();
        if (candidates.Length == 0)
        {
            candidates = configuredAvatars
                .Where(selection => !string.Equals(selection.AvatarId, currentAvatarId, StringComparison.Ordinal))
                .ToArray();
        }

        if (candidates.Length == 0)
        {
            candidates = configuredAvatars;
        }

        var selectedAvatar = candidates[Random.Shared.Next(candidates.Length)];
        lock (stateGate)
        {
            lastAvatarRouletResultIds[rule.Id] = selectedAvatar.AvatarId;
        }

        return string.IsNullOrWhiteSpace(selectedAvatar.AvatarName)
            || string.Equals(selectedAvatar.AvatarName, selectedAvatar.AvatarId, StringComparison.Ordinal)
            ? selectedAvatar with { AvatarName = "selected avatar" }
            : selectedAvatar;
    }

    private async Task HandleTimedSupporterOverrideTriggerAsync(
        TriggerRuleSnapshot rule,
        BridgeIncomingEvent bridgeEvent,
        CancellationToken cancellationToken,
        bool queuedReplay)
    {
        // Timed supporter overrides behave like one shared priority queue. A new paid event
        // can extend the active override, preempt it, merge into a queued entry, or start
        // a brand-new suppression sequence when nothing else is running.
        var now = DateTimeOffset.UtcNow;
        if (!queuedReplay)
        {
            var cooldownSeconds = GetCooldownSeconds(rule);
            var shouldQueueForCooldown = false;
            var queuedCount = 0;

            lock (stateGate)
            {
                if (cooldownSeconds <= 0)
                {
                    cooldowns.Remove(rule.Id);
                }
                else if (cooldowns.TryGetValue(rule.Id, out var cooldownUntil) && cooldownUntil > now)
                {
                    shouldQueueForCooldown = true;
                    queuedCount = EnqueueTrigger(rule, bridgeEvent);
                    WriteLog($"Queued '{rule.Name}' because it is still on cooldown for {DescribeDuration((cooldownUntil - now).TotalSeconds)}. {queuedCount} waiting.");
                    EnsureQueuedRuleDrain(rule.Id);
                }
            }

            if (shouldQueueForCooldown)
            {
                return;
            }
        }

        ActiveSupporterOverrideState? activeState;
        var queuedIndex = -1;
        var sequenceWasInactive = false;
        var existingRemainingDuration = TimeSpan.Zero;

        lock (stateGate)
        {
            activeState = activeSupporterOverride;
            queuedIndex = GetQueuedSupporterOverrideIndexLocked(rule.Id);
            sequenceWasInactive = activeState is null && queuedSupporterOverrides.Count == 0;
            existingRemainingDuration = GetCurrentSupporterOverrideRemainingDurationLocked(rule.Id, now);
        }

        var requestedDuration = GetSupporterOverrideDuration(rule, bridgeEvent);
        var triggerDuration = ClampSupporterOverrideAddedDuration(rule, requestedDuration, existingRemainingDuration);
        if (triggerDuration <= TimeSpan.Zero)
        {
            WriteLog(TF(
                "Paid override '{0}' is already at its max added time of {1}, so Crystal Relay did not add more time.",
                rule.Name,
                DescribeDuration(Math.Max(1, rule.MaxAccumulatedDurationSeconds))));
            return;
        }

        if (activeState is not null && activeState.ActiveUntil > now)
        {
            if (activeState.Rule.Id == rule.Id)
            {
                await ExtendActiveSupporterOverrideAsync(activeState, rule, bridgeEvent, triggerDuration, cancellationToken);
                return;
            }

            if (CompareSupporterOverridePriority(rule, activeState.Rule) > 0)
            {
                await PreemptActiveSupporterOverrideAsync(activeState, rule, bridgeEvent, triggerDuration, cancellationToken, queuedReplay);
                return;
            }
        }

        if (queuedIndex >= 0)
        {
            ExtendQueuedSupporterOverride(queuedIndex, rule, bridgeEvent, triggerDuration);
            return;
        }

        if (activeState is null && sequenceWasInactive)
        {
            await StartTimedSupporterOverrideAsync(
                rule,
                bridgeEvent,
                triggerDuration,
                GetNextSupporterOverrideQueueOrder(),
                cancellationToken,
                queuedReplay,
                resumedFromQueue: false,
                sequenceWasInactive: true);
            return;
        }

        QueueSupporterOverride(rule, bridgeEvent, triggerDuration);
    }

    private async Task ExtendActiveSupporterOverrideAsync(
        ActiveSupporterOverrideState activeState,
        TriggerRuleSnapshot rule,
        BridgeIncomingEvent bridgeEvent,
        TimeSpan extension,
        CancellationToken cancellationToken)
    {
        CancellationTokenSource? previousCancellation = null;
        DateTimeOffset activeUntil;
        string displayValue;

        lock (stateGate)
        {
            if (!ReferenceEquals(activeSupporterOverride, activeState))
            {
                return;
            }

            previousCancellation = activeState.CompletionCancellation;
            activeState.Event = bridgeEvent;
            activeState.ActiveUntil = activeState.ActiveUntil.Add(extension);
            activeState.CompletionCancellation = runtimeCancellation is null
                ? new CancellationTokenSource()
                : CancellationTokenSource.CreateLinkedTokenSource(runtimeCancellation.Token);
            activeUntil = activeState.ActiveUntil;
            displayValue = activeState.Action.DisplayValue;
        }

        previousCancellation?.Cancel();
        previousCancellation?.Dispose();

        ApplyRuleLockoutUntil(activeState.Rule, activeUntil);
        ScheduleTimedSupporterOverrideCompletion(activeState, activeState.CompletionCancellation);
        WriteLog($"Extended paid override '{activeState.Rule.Name}' by {DescribeDuration(extension.TotalSeconds)}. {DescribeDuration((activeUntil - DateTimeOffset.UtcNow).TotalSeconds)} left.");
        await TrySendBotMessageAsync(rule, bridgeEvent, displayValue, cancellationToken, extension.TotalSeconds);
    }

    private void ExtendQueuedSupporterOverride(
        int queuedIndex,
        TriggerRuleSnapshot rule,
        BridgeIncomingEvent bridgeEvent,
        TimeSpan extension)
    {
        TimeSpan newRemainingDuration;
        lock (stateGate)
        {
            if (queuedIndex < 0 || queuedIndex >= queuedSupporterOverrides.Count)
            {
                return;
            }

            var queuedOverride = queuedSupporterOverrides[queuedIndex];
            queuedOverride.Rule = rule;
            queuedOverride.RemainingDuration += extension;
            queuedOverride.Event = bridgeEvent;
            newRemainingDuration = queuedOverride.RemainingDuration;
        }

        WriteLog($"Added more time to queued paid override '{rule.Name}'. It now has {DescribeDuration(newRemainingDuration.TotalSeconds)} waiting.");
    }

    private void QueueSupporterOverride(TriggerRuleSnapshot rule, BridgeIncomingEvent bridgeEvent, TimeSpan duration)
    {
        var queuedCount = 0;
        lock (stateGate)
        {
            queuedSupporterOverrides.Add(new QueuedSupporterOverrideState(
                rule,
                bridgeEvent,
                duration,
                GetNextSupporterOverrideQueueOrder()));
            queuedCount = queuedSupporterOverrides.Count;
        }

        WriteLog($"Queued paid override '{rule.Name}'. {queuedCount} paid override{(queuedCount == 1 ? string.Empty : "s")} waiting.");
    }

    private async Task PreemptActiveSupporterOverrideAsync(
        ActiveSupporterOverrideState activeState,
        TriggerRuleSnapshot newRule,
        BridgeIncomingEvent bridgeEvent,
        TimeSpan newDuration,
        CancellationToken cancellationToken,
        bool queuedReplay)
    {
        CancellationTokenSource? previousCancellation = null;
        TimeSpan remainingDuration;

        lock (stateGate)
        {
            if (!ReferenceEquals(activeSupporterOverride, activeState))
            {
                return;
            }

            remainingDuration = activeState.ActiveUntil - DateTimeOffset.UtcNow;
            if (remainingDuration < TimeSpan.Zero)
            {
                remainingDuration = TimeSpan.Zero;
            }

            previousCancellation = activeState.CompletionCancellation;
            activeSupporterOverride = null;
            if (remainingDuration > TimeSpan.Zero)
            {
                queuedSupporterOverrides.Add(new QueuedSupporterOverrideState(
                    activeState.Rule,
                    activeState.Event,
                    remainingDuration,
                    activeState.QueueOrder));
            }
        }

        previousCancellation?.Cancel();
        previousCancellation?.Dispose();
        try
        {
            await ResetRuleEffectAsync(
                activeState.Rule,
                activeState.Action.ResetPacket,
                activeState.Action.AvatarResetId,
                activeState.Action.AvatarResetName,
                cancellationToken);
        }
        catch (Exception ex)
        {
            WriteLog($"Could not pause paid override '{activeState.Rule.Name}' cleanly before switching priorities: {ex.Message}");
        }
        ReleaseActiveRuleLockoutState(activeState.Rule.Id, logRelease: false);
        ReleaseActiveAvatarSwitchLockoutState(activeState.Rule.Id, logRelease: false);

        WriteLog($"'{newRule.Name}' outranked '{activeState.Rule.Name}', so Crystal Relay paused the earlier paid override.");
        await StartTimedSupporterOverrideAsync(
            newRule,
            bridgeEvent,
            newDuration,
            GetNextSupporterOverrideQueueOrder(),
            cancellationToken,
            queuedReplay,
            resumedFromQueue: false,
            sequenceWasInactive: false);
    }

    private async Task StartTimedSupporterOverrideAsync(
        TriggerRuleSnapshot rule,
        BridgeIncomingEvent bridgeEvent,
        TimeSpan duration,
        long queueOrder,
        CancellationToken cancellationToken,
        bool queuedReplay,
        bool resumedFromQueue,
        bool sequenceWasInactive)
    {
        var effectiveRule = CreateTimedSupporterOverrideExecutionRule(rule, duration, cooldownSeconds: 0);
        var capturedReturnAvatar = rule.ActionType == OscActionType.AvatarChange
            ? GetSharedReturnAvatarSnapshot()
            : SharedReturnAvatarSnapshot.Empty;
        if (rule.ActionType == OscActionType.AvatarChange
            && string.IsNullOrWhiteSpace(capturedReturnAvatar.AvatarId))
        {
            throw new InvalidOperationException("Pick the return avatar first before timed avatar-switch redeems can switch back.");
        }

        var action = await ResolveActionAsync(
            effectiveRule,
            cancellationToken,
            preferLocalInstantToggleState: false,
            capturedReturnAvatar);

        if (sequenceWasInactive)
        {
            await CancelBlockedChannelPointEffectsForSupporterOverrideAsync(cancellationToken);
        }

        await oscRouterService.SendToVrChatAsync(action.Packet, cancellationToken);
        RememberAvatarParameterValue(rule, action.DisplayValue);

        if (rule.ActionType == OscActionType.AvatarChange && !string.IsNullOrWhiteSpace(action.AvatarTargetId))
        {
            LogPaidAvatarChangeAllowedDuringActiveScaling(rule);
            SetCurrentVrChatAvatar(
                action.AvatarTargetId,
                notify: true,
                GetAvatarScaleAvatarChangeCarryoverMode(rule));
        }

        var completionCancellation = runtimeCancellation is null
            ? new CancellationTokenSource()
            : CancellationTokenSource.CreateLinkedTokenSource(runtimeCancellation.Token);
        var activeUntil = DateTimeOffset.UtcNow.Add(duration);
        var activeState = new ActiveSupporterOverrideState(
            rule,
            bridgeEvent,
            action,
            activeUntil,
            queueOrder,
            completionCancellation);

        lock (stateGate)
        {
            activeSupporterOverride = activeState;
        }

        ApplyRuleLockoutUntil(rule, activeUntil);
        ScheduleTimedSupporterOverrideCompletion(activeState, completionCancellation);

        if (sequenceWasInactive)
        {
            ManagedRewardAvailabilityChanged?.Invoke();
        }

        if (resumedFromQueue)
        {
            WriteLog($"Resumed paid override '{rule.Name}' for {DescribeDuration(duration.TotalSeconds)}.");
        }
        else if (queuedReplay)
        {
            WriteLog($"{bridgeEvent.UserDisplayName} triggered paid override '{rule.Name}' from the queue.");
        }
        else
        {
            WriteLog($"{bridgeEvent.UserDisplayName} triggered paid override '{rule.Name}'.");
        }

        await TrySendBotMessageAsync(rule, bridgeEvent, action.DisplayValue, cancellationToken, duration.TotalSeconds);
    }

    private void ScheduleTimedSupporterOverrideCompletion(
        ActiveSupporterOverrideState activeState,
        CancellationTokenSource completionCancellation)
    {
        _ = Task.Run(async () =>
        {
            try
            {
                var delay = activeState.ActiveUntil - DateTimeOffset.UtcNow;
                if (delay > TimeSpan.Zero)
                {
                    await Task.Delay(delay, completionCancellation.Token);
                }

                await CompleteTimedSupporterOverrideAsync(activeState, completionCancellation, completionCancellation.Token);
            }
            catch (OperationCanceledException)
            {
            }
        }, CancellationToken.None);
    }

    private async Task CompleteTimedSupporterOverrideAsync(
        ActiveSupporterOverrideState activeState,
        CancellationTokenSource completionCancellation,
        CancellationToken cancellationToken)
    {
        // Completion is also the queue handoff point: finish the current reset, start its
        // cooldown, then resume the highest-priority queued paid override if one is waiting.
        lock (stateGate)
        {
            if (!ReferenceEquals(activeSupporterOverride, activeState)
                || !ReferenceEquals(activeState.CompletionCancellation, completionCancellation))
            {
                return;
            }
        }

        try
        {
            await ResetRuleEffectAsync(
                activeState.Rule,
                activeState.Action.ResetPacket,
                activeState.Action.AvatarResetId,
                activeState.Action.AvatarResetName,
                cancellationToken);
        }
        catch (Exception ex)
        {
            WriteLog($"Could not finish paid override '{activeState.Rule.Name}' cleanly: {ex.Message}");
        }

        ReleaseActiveRuleLockoutState(activeState.Rule.Id, logRelease: false);
        ReleaseActiveAvatarSwitchLockoutState(activeState.Rule.Id, logRelease: false);

        TriggerRuleSnapshot? nextRule = null;
        BridgeIncomingEvent? nextEvent = null;
        TimeSpan nextDuration = TimeSpan.Zero;
        long nextQueueOrder = 0;
        var sequenceStillActive = false;
        var now = DateTimeOffset.UtcNow;
        var cooldownSeconds = GetCooldownSeconds(activeState.Rule);

        lock (stateGate)
        {
            if (ReferenceEquals(activeSupporterOverride, activeState)
                && ReferenceEquals(activeState.CompletionCancellation, completionCancellation))
            {
                activeSupporterOverride = null;
            }

            if (cooldownSeconds > 0)
            {
                cooldowns[activeState.Rule.Id] = now.AddSeconds(cooldownSeconds);
            }
            else
            {
                cooldowns.Remove(activeState.Rule.Id);
            }

            while (true)
            {
                var nextIndex = GetNextQueuedSupporterOverrideIndexLocked();
                if (nextIndex < 0)
                {
                    break;
                }

                var queuedOverride = queuedSupporterOverrides[nextIndex];
                queuedSupporterOverrides.RemoveAt(nextIndex);
                var candidateRule = activeConfiguration?.Rules.FirstOrDefault(rule => rule.Id == queuedOverride.Rule.Id) ?? queuedOverride.Rule;
                if (!candidateRule.IsEnabled
                    || !IsTimedSupporterOverrideRule(candidateRule)
                    || queuedOverride.RemainingDuration <= TimeSpan.Zero)
                {
                    continue;
                }

                nextRule = candidateRule;
                nextEvent = queuedOverride.Event;
                nextDuration = queuedOverride.RemainingDuration;
                nextQueueOrder = queuedOverride.QueueOrder;
                break;
            }

            sequenceStillActive = activeSupporterOverride is not null || queuedSupporterOverrides.Count > 0;
        }

        if (cooldownSeconds > 0)
        {
            ScheduleCooldownStateNotification(activeState.Rule.Id, TimeSpan.FromSeconds(cooldownSeconds));
            ApplyRuleLockoutUntil(activeState.Rule, now.AddSeconds(cooldownSeconds));
        }
        else
        {
            CancelCooldownStateNotification(activeState.Rule.Id);
            ReleaseActiveRuleLockoutState(activeState.Rule.Id, logRelease: false);
        }

        completionCancellation.Dispose();

        WriteLog($"Paid override '{activeState.Rule.Name}' finished.");

        if (nextRule is not null
            && nextRule.IsEnabled
            && IsTimedSupporterOverrideRule(nextRule)
            && nextEvent is not null
            && nextDuration > TimeSpan.Zero)
        {
            await StartTimedSupporterOverrideAsync(
                nextRule,
                nextEvent,
                nextDuration,
                nextQueueOrder,
                cancellationToken,
                queuedReplay: true,
                resumedFromQueue: true,
                sequenceWasInactive: false);
            return;
        }

        if (!sequenceStillActive)
        {
            ManagedRewardAvailabilityChanged?.Invoke();
        }
    }

    private async Task CancelBlockedChannelPointEffectsForSupporterOverrideAsync(CancellationToken cancellationToken)
    {
        List<PendingResetState> blockedResets;
        lock (stateGate)
        {
            blockedResets = [];
            foreach (var reset in pendingResets.Values)
            {
                if (ShouldBlockRuleDuringSupporterOverride(reset.Rule))
                {
                    blockedResets.Add(reset);
                }
            }

            foreach (var blockedReset in blockedResets)
            {
                pendingResets.Remove(blockedReset.RuleId);
            }
        }

        foreach (var blockedReset in blockedResets)
        {
            try
            {
                blockedReset.Cancellation.Cancel();
                await ResetRuleEffectAsync(
                    blockedReset.Rule,
                    blockedReset.Packets,
                    blockedReset.ResetObservedValues,
                    blockedReset.AvatarChangeResetId,
                    blockedReset.AvatarChangeResetName,
                    cancellationToken);
                WriteLog($"Canceled '{blockedReset.RuleName}' because a paid override took priority.");
            }
            catch (Exception ex)
            {
                WriteLog($"Could not cancel '{blockedReset.RuleName}' before the paid override started: {ex.Message}");
            }
            finally
            {
                ReleaseActiveRuleLockoutState(blockedReset.RuleId, logRelease: false);
                ReleaseActiveAvatarSwitchLockoutState(blockedReset.RuleId, logRelease: false);
                var releasedLaneKeys = ReleaseMovementLanes(blockedReset.MovementLaneLeaseId, blockedReset.MovementLaneKeys);
                foreach (var releasedLaneKey in releasedLaneKeys)
                {
                    EnsureQueuedLaneDrain(releasedLaneKey);
                }

                blockedReset.Cancellation.Dispose();
            }
        }
    }

    private async Task ResetRuleEffectAsync(
        TriggerRuleSnapshot rule,
        byte[]? resetPacket,
        string avatarResetId,
        string avatarResetName,
        CancellationToken cancellationToken)
    {
        await ResetRuleEffectAsync(
            rule,
            resetPacket is null ? [] : [resetPacket],
            null,
            avatarResetId,
            avatarResetName,
            cancellationToken);
    }

    private async Task ResetRuleEffectAsync(
        TriggerRuleSnapshot rule,
        IReadOnlyList<byte[]> resetPackets,
        IReadOnlyList<OscObservedValue>? resetObservedValues,
        string avatarResetId,
        string avatarResetName,
        CancellationToken cancellationToken)
    {
        if (resetPackets.Count > 0)
        {
            await SendPacketsToVrChatAsync(resetPackets, cancellationToken);
            RememberAvatarParameterValues(rule, resetObservedValues, rule.ResetValue);
        }

        if (resetPackets.Count > 0 && !string.IsNullOrWhiteSpace(avatarResetId))
        {
            LogPaidAvatarChangeAllowedDuringActiveScaling(rule);
            SetCurrentVrChatAvatar(
                avatarResetId,
                notify: true,
                GetAvatarScaleAvatarChangeCarryoverMode(rule));
            SetSharedReturnAvatar(avatarResetId, avatarResetName, notify: true);
        }
    }

    private void ApplyRuleLockoutUntil(TriggerRuleSnapshot rule, DateTimeOffset expiresAt)
    {
        var normalizedRuleIds = rule.TemporarilyDisabledRuleIds
            .Where(ruleId => ruleId != rule.Id)
            .Distinct()
            .ToArray();
        var now = DateTimeOffset.UtcNow;
        if (normalizedRuleIds.Length == 0 || expiresAt <= now)
        {
            ReleaseActiveRuleLockoutState(rule.Id, logRelease: false);
            return;
        }

        var changed = false;
        lock (stateGate)
        {
            var before = GetTemporarilyDisabledRuleIdsLocked(now);
            activeRuleLockouts[rule.Id] = new ActiveRuleLockoutState(rule.Name, expiresAt, normalizedRuleIds);
            var after = GetTemporarilyDisabledRuleIdsLocked(now);
            changed = !before.SetEquals(after);
        }

        ScheduleLockoutStateNotification(rule.Id, expiresAt - now);
        if (changed)
        {
            ManagedRewardAvailabilityChanged?.Invoke();
        }
    }

    private long GetNextSupporterOverrideQueueOrder()
    {
        lock (stateGate)
        {
            return ++nextSupporterOverrideQueueOrder;
        }
    }

    private async Task ExecuteDesktopInputLockAsync(TriggerRuleSnapshot rule, CancellationToken cancellationToken)
    {
        if (runtimeCancellation is null)
        {
            throw new InvalidOperationException("OSC runtime is not ready yet, so Crystal Relay cannot start a desktop input lock.");
        }

        var plan = BuildMovementInputLockPlan(rule.MovementDirection);
        await SendPacketsToVrChatAsync(plan.PacketsByLane.Values.SelectMany(static packets => packets), cancellationToken);

        var leaseId = Guid.NewGuid();
        var cancellation = CancellationTokenSource.CreateLinkedTokenSource(runtimeCancellation.Token);
        var activeLock = new ActiveDesktopInputLockState(
            leaseId,
            rule.Id,
            rule.Name,
            cancellation,
            DateTimeOffset.UtcNow.AddSeconds(Math.Max(1, rule.DurationSeconds)),
            plan.Scope,
            plan.PacketsByLane.ToDictionary(
                pair => pair.Key,
                pair => pair.Value.ToArray(),
                StringComparer.Ordinal));

        List<ActiveDesktopInputLockState> canceledDesktopLocks;
        List<ActiveMovementSoftLockState> canceledSoftLocks;
        lock (stateGate)
        {
            canceledSoftLocks = PreemptMovementSoftLocksLocked(plan.PacketsByLane.Keys);
            canceledDesktopLocks = PreemptDesktopInputLocksLocked(plan.PacketsByLane.Keys);
            activeDesktopInputLocks[leaseId] = activeLock;
            foreach (var laneKey in plan.PacketsByLane.Keys)
            {
                actionLanes[laneKey] = new ActiveMovementLaneState(leaseId, activeLock.BusyUntil, rule.Id, false);
            }
        }

        foreach (var canceledLock in canceledSoftLocks)
        {
            canceledLock.Cancellation.Cancel();
        }

        foreach (var canceledLock in canceledDesktopLocks)
        {
            canceledLock.Cancellation.Cancel();
        }

        await RefreshDesktopInputLockScopeAsync(cancellationToken);

        _ = Task.Run(async () =>
        {
            try
            {
                var remaining = activeLock.BusyUntil - DateTimeOffset.UtcNow;
                if (remaining > TimeSpan.Zero)
                {
                    await Task.Delay(remaining, cancellation.Token);
                }

                var finalPackets = GetDesktopInputLockPackets(activeLock);
                if (finalPackets.Count > 0)
                {
                    await SendPacketsToVrChatAsync(finalPackets, cancellation.Token);
                }
            }
            catch (OperationCanceledException)
            {
            }
            catch (Exception ex)
            {
                WriteLog($"Failed to hold '{rule.Name}' as a desktop input lock: {ex.Message}");
            }
            finally
            {
                var releasedLaneKeys = ReleaseDesktopInputLock(activeLock);
                cancellation.Dispose();
                try
                {
                    await RefreshDesktopInputLockScopeAsync(CancellationToken.None);
                }
                catch (Exception ex)
                {
                    WriteLog($"Crystal Relay could not update the desktop input lock state after '{rule.Name}' ended: {ex.Message}");
                }

                foreach (var releasedLaneKey in releasedLaneKeys)
                {
                    EnsureQueuedLaneDrain(releasedLaneKey);
                }
            }
        }, CancellationToken.None);
    }

    private async Task ExecuteMovementSoftLockAsync(TriggerRuleSnapshot rule, CancellationToken cancellationToken)
    {
        if (runtimeCancellation is null)
        {
            throw new InvalidOperationException("OSC runtime is not ready yet, so Crystal Relay cannot start a movement soft lock.");
        }

        var plan = BuildMovementInputLockPlan(rule.MovementDirection);
        var pulsePacketsByLane = BuildMovementSoftLockPulsePackets(plan, rule.MovementDirection);
        await SendPacketsToVrChatAsync(pulsePacketsByLane.Values.SelectMany(static packets => packets), cancellationToken);

        var leaseId = Guid.NewGuid();
        var cancellation = CancellationTokenSource.CreateLinkedTokenSource(runtimeCancellation.Token);
        var activeLock = new ActiveMovementSoftLockState(
            leaseId,
            rule.Id,
            rule.Name,
            cancellation,
            pulsePacketsByLane.ToDictionary(
                pair => pair.Key,
                pair => pair.Value.ToArray(),
                StringComparer.Ordinal),
            plan.PacketsByLane.ToDictionary(
                pair => pair.Key,
                pair => pair.Value.ToArray(),
                StringComparer.Ordinal));

        var busyUntil = DateTimeOffset.UtcNow.AddSeconds(Math.Max(1, rule.DurationSeconds));
        List<ActiveMovementSoftLockState> canceledLocks;
        lock (stateGate)
        {
            canceledLocks = PreemptMovementSoftLocksLocked(plan.PacketsByLane.Keys);
            activeMovementLocks[leaseId] = activeLock;
            foreach (var laneKey in plan.PacketsByLane.Keys)
            {
                actionLanes[laneKey] = new ActiveMovementLaneState(leaseId, busyUntil, rule.Id, true);
            }
        }

        foreach (var canceledLock in canceledLocks)
        {
            canceledLock.Cancellation.Cancel();
        }

        _ = Task.Run(async () =>
        {
            try
            {
                while (!cancellation.IsCancellationRequested)
                {
                    var remaining = busyUntil - DateTimeOffset.UtcNow;
                    if (remaining <= TimeSpan.Zero)
                    {
                        break;
                    }

                    var delay = remaining < MovementSoftLockPulseInterval
                        ? remaining
                        : MovementSoftLockPulseInterval;
                    if (delay > TimeSpan.Zero)
                    {
                        await Task.Delay(delay, cancellation.Token);
                    }

                    var pulsePackets = GetMovementSoftLockPackets(activeLock);
                    if (pulsePackets.Count == 0)
                    {
                        break;
                    }

                    await SendPacketsToVrChatAsync(pulsePackets, cancellation.Token);
                }

                var finalPackets = GetMovementSoftLockReleasePackets(activeLock);
                if (finalPackets.Count > 0)
                {
                    await SendPacketsToVrChatAsync(finalPackets, cancellation.Token);
                }
            }
            catch (OperationCanceledException)
            {
            }
            catch (Exception ex)
            {
                WriteLog($"Failed to hold '{rule.Name}' as a movement soft lock: {ex.Message}");
            }
            finally
            {
                var releasedLaneKeys = ReleaseMovementSoftLock(activeLock);
                cancellation.Dispose();
                foreach (var releasedLaneKey in releasedLaneKeys)
                {
                    EnsureQueuedLaneDrain(releasedLaneKey);
                }
            }
        }, CancellationToken.None);
    }

    private static bool IsSoftLockMovement(PlayerMovementDirection movementDirection) => movementDirection is
        PlayerMovementDirection.StopMovement
        or PlayerMovementDirection.StopTurning
        or PlayerMovementDirection.StopAll;

    private string? GetMovementLaneKey(PlayerMovementDirection movementDirection) => movementDirection switch
    {
        PlayerMovementDirection.Forward or PlayerMovementDirection.Backward => "player-movement-vertical",
        PlayerMovementDirection.Left or PlayerMovementDirection.Right => "player-movement-horizontal",
        PlayerMovementDirection.Jump => "player-movement-jump",
        PlayerMovementDirection.SpinLeft or PlayerMovementDirection.SpinRight => "player-movement-look",
        _ => null
    };

    private IReadOnlyList<string> GetActionLaneKeys(
        TriggerRuleSnapshot rule,
        ResolvedRuleAction? resolvedAction = null)
    {
        if (rule.ActionType == OscActionType.PlayerMovement && !IsSoftLockMovement(rule.MovementDirection))
        {
            var movementLaneKey = GetMovementLaneKey(rule.MovementDirection);
            return string.IsNullOrWhiteSpace(movementLaneKey) ? [] : [movementLaneKey];
        }

        if (rule.DurationSeconds <= 0 && rule.ActionType != OscActionType.SetTrigger)
        {
            return [];
        }

        if (rule.ActionType == OscActionType.AvatarParameter)
        {
            try
            {
                return [$"avatar-param:{VrChatOscClient.NormalizeAvatarParameterAddress(rule.ParameterName)}"];
            }
            catch (InvalidOperationException)
            {
                return [];
            }
        }

        if (rule.ActionType == OscActionType.SetTrigger)
        {
            var laneKeys = new List<string>();
            if (IsBitsOutfitSetTriggerRule(rule))
            {
                laneKeys.Add(BitsOutfitSetTriggerLaneKey);
            }

            if (rule.AvatarProfileId != Guid.Empty)
            {
                laneKeys.Add($"set-trigger-profile:{rule.AvatarProfileId}");
            }

            laneKeys.AddRange(rule.SetTriggerActions
                .Where(action => !string.IsNullOrWhiteSpace(action.ParameterName))
                .Select(action =>
                {
                    try
                    {
                        return $"avatar-param:{VrChatOscClient.NormalizeAvatarParameterAddress(action.ParameterName)}";
                    }
                    catch (InvalidOperationException)
                    {
                        return string.Empty;
                    }
                })
                .Where(laneKey => !string.IsNullOrWhiteSpace(laneKey))
                .Distinct(StringComparer.Ordinal));

            if (resolvedAction is not null)
            {
                laneKeys.AddRange(resolvedAction.ResetObservedValues
                    .Where(value => !string.IsNullOrWhiteSpace(value.Address))
                    .Select(value => $"avatar-param:{value.Address}"));
            }

            return laneKeys
                .Where(laneKey => !string.IsNullOrWhiteSpace(laneKey))
                .Distinct(StringComparer.Ordinal)
                .ToArray();
        }

        return [];
    }

    private MovementInputLockPlan BuildMovementInputLockPlan(PlayerMovementDirection movementDirection)
    {
        return movementDirection switch
        {
            PlayerMovementDirection.StopMovement => new MovementInputLockPlan(
                "Stop Movement",
                DesktopInputLockScope.Movement,
                new Dictionary<string, byte[][]>(StringComparer.Ordinal)
                {
                    ["player-movement-vertical"] =
                    [
                        vrChatOscClient.BuildInputAxisPacket("/input/Vertical", 0f),
                        vrChatOscClient.BuildInputButtonPacket("/input/MoveForward", false),
                        vrChatOscClient.BuildInputButtonPacket("/input/MoveBackward", false)
                    ],
                    ["player-movement-horizontal"] =
                    [
                        vrChatOscClient.BuildInputAxisPacket("/input/Horizontal", 0f),
                        vrChatOscClient.BuildInputButtonPacket("/input/MoveLeft", false),
                        vrChatOscClient.BuildInputButtonPacket("/input/MoveRight", false)
                    ]
                }),
            PlayerMovementDirection.StopTurning => new MovementInputLockPlan(
                "Stop Turning",
                DesktopInputLockScope.Turning,
                new Dictionary<string, byte[][]>(StringComparer.Ordinal)
                {
                    ["player-movement-look"] =
                    [
                        vrChatOscClient.BuildInputAxisPacket("/input/LookHorizontal", 0f),
                        vrChatOscClient.BuildInputButtonPacket("/input/LookLeft", false),
                        vrChatOscClient.BuildInputButtonPacket("/input/LookRight", false),
                        vrChatOscClient.BuildInputButtonPacket("/input/ComfortLeft", false),
                        vrChatOscClient.BuildInputButtonPacket("/input/ComfortRight", false)
                    ]
                }),
            PlayerMovementDirection.StopAll => new MovementInputLockPlan(
                "Stop All",
                DesktopInputLockScope.Movement | DesktopInputLockScope.Turning,
                new Dictionary<string, byte[][]>(StringComparer.Ordinal)
                {
                    ["player-movement-vertical"] =
                    [
                        vrChatOscClient.BuildInputAxisPacket("/input/Vertical", 0f),
                        vrChatOscClient.BuildInputButtonPacket("/input/MoveForward", false),
                        vrChatOscClient.BuildInputButtonPacket("/input/MoveBackward", false)
                    ],
                    ["player-movement-horizontal"] =
                    [
                        vrChatOscClient.BuildInputAxisPacket("/input/Horizontal", 0f),
                        vrChatOscClient.BuildInputButtonPacket("/input/MoveLeft", false),
                        vrChatOscClient.BuildInputButtonPacket("/input/MoveRight", false)
                    ],
                    ["player-movement-look"] =
                    [
                        vrChatOscClient.BuildInputAxisPacket("/input/LookHorizontal", 0f),
                        vrChatOscClient.BuildInputButtonPacket("/input/LookLeft", false),
                        vrChatOscClient.BuildInputButtonPacket("/input/LookRight", false),
                        vrChatOscClient.BuildInputButtonPacket("/input/ComfortLeft", false),
                        vrChatOscClient.BuildInputButtonPacket("/input/ComfortRight", false)
                    ]
                }),
            _ => throw new InvalidOperationException($"Unsupported stop-input movement action: {movementDirection}")
        };
    }

    private IReadOnlyDictionary<string, byte[][]> BuildMovementSoftLockPulsePackets(
        MovementInputLockPlan plan,
        PlayerMovementDirection movementDirection)
    {
        var packetsByLane = plan.PacketsByLane.ToDictionary(
            pair => pair.Key,
            pair => pair.Value.ToList(),
            StringComparer.Ordinal);

        void AddPacket(string laneKey, byte[] packet)
        {
            if (!packetsByLane.TryGetValue(laneKey, out var packets))
            {
                packets = [];
                packetsByLane[laneKey] = packets;
            }

            packets.Add(packet);
        }

        switch (movementDirection)
        {
            case PlayerMovementDirection.StopMovement:
                AddPacket("player-movement-vertical", vrChatOscClient.BuildInputButtonPacket("/input/MoveForward", true));
                AddPacket("player-movement-vertical", vrChatOscClient.BuildInputButtonPacket("/input/MoveBackward", true));
                AddPacket("player-movement-horizontal", vrChatOscClient.BuildInputButtonPacket("/input/MoveLeft", true));
                AddPacket("player-movement-horizontal", vrChatOscClient.BuildInputButtonPacket("/input/MoveRight", true));
                break;

            case PlayerMovementDirection.StopTurning:
                AddPacket("player-movement-look", vrChatOscClient.BuildInputButtonPacket("/input/LookLeft", true));
                AddPacket("player-movement-look", vrChatOscClient.BuildInputButtonPacket("/input/LookRight", true));
                AddPacket("player-movement-look", vrChatOscClient.BuildInputButtonPacket("/input/ComfortLeft", true));
                AddPacket("player-movement-look", vrChatOscClient.BuildInputButtonPacket("/input/ComfortRight", true));
                break;

            case PlayerMovementDirection.StopAll:
                AddPacket("player-movement-vertical", vrChatOscClient.BuildInputButtonPacket("/input/MoveForward", true));
                AddPacket("player-movement-vertical", vrChatOscClient.BuildInputButtonPacket("/input/MoveBackward", true));
                AddPacket("player-movement-horizontal", vrChatOscClient.BuildInputButtonPacket("/input/MoveLeft", true));
                AddPacket("player-movement-horizontal", vrChatOscClient.BuildInputButtonPacket("/input/MoveRight", true));
                AddPacket("player-movement-look", vrChatOscClient.BuildInputButtonPacket("/input/LookLeft", true));
                AddPacket("player-movement-look", vrChatOscClient.BuildInputButtonPacket("/input/LookRight", true));
                AddPacket("player-movement-look", vrChatOscClient.BuildInputButtonPacket("/input/ComfortLeft", true));
                AddPacket("player-movement-look", vrChatOscClient.BuildInputButtonPacket("/input/ComfortRight", true));
                break;
        }

        return packetsByLane.ToDictionary(
            pair => pair.Key,
            pair => pair.Value.ToArray(),
            StringComparer.Ordinal);
    }

    private async Task SendPacketsToVrChatAsync(
        IEnumerable<byte[]> packets,
        CancellationToken cancellationToken,
        TimeSpan? packetSpacing = null)
    {
        var sentAny = false;
        foreach (var packet in packets)
        {
            if (sentAny && packetSpacing is { } delay && delay > TimeSpan.Zero)
            {
                await Task.Delay(delay, cancellationToken);
            }

            await oscRouterService.SendToVrChatAsync(packet, cancellationToken);
            sentAny = true;
        }
    }

    private List<ActiveMovementSoftLockState> PreemptMovementSoftLocksLocked(IEnumerable<string> laneKeys)
    {
        var canceledLocks = new List<ActiveMovementSoftLockState>();
        foreach (var laneKey in laneKeys.Distinct(StringComparer.Ordinal))
        {
            if (!actionLanes.TryGetValue(laneKey, out var activeLane)
                || !activeLane.IsSoftLock
                || !activeMovementLocks.TryGetValue(activeLane.OwnerId, out var activeLock))
            {
                continue;
            }

            activeLock.PacketsByLane.Remove(laneKey);
            if (activeLock.PacketsByLane.Count == 0)
            {
                activeMovementLocks.Remove(activeLock.LeaseId);
                canceledLocks.Add(activeLock);
            }

            activeLock.ReleasePacketsByLane.Remove(laneKey);
        }

        return canceledLocks
            .DistinctBy(lockState => lockState.LeaseId)
            .ToList();
    }

    private IReadOnlyList<byte[]> GetMovementSoftLockPackets(ActiveMovementSoftLockState activeLock)
    {
        lock (stateGate)
        {
            return [.. activeLock.PacketsByLane.Values.SelectMany(static packets => packets)];
        }
    }

    private IReadOnlyList<byte[]> GetMovementSoftLockReleasePackets(ActiveMovementSoftLockState activeLock)
    {
        lock (stateGate)
        {
            return [.. activeLock.ReleasePacketsByLane.Values.SelectMany(static packets => packets)];
        }
    }

    private IReadOnlyList<string> ReleaseMovementSoftLock(ActiveMovementSoftLockState activeLock)
    {
        List<string> laneKeysToRelease;
        lock (stateGate)
        {
            if (activeMovementLocks.TryGetValue(activeLock.LeaseId, out var currentLock)
                && ReferenceEquals(currentLock, activeLock))
            {
                activeMovementLocks.Remove(activeLock.LeaseId);
            }

            laneKeysToRelease = [.. activeLock.PacketsByLane.Keys];
        }

        return ReleaseMovementLanes(activeLock.LeaseId, laneKeysToRelease);
    }

    private List<ActiveDesktopInputLockState> PreemptDesktopInputLocksLocked(IEnumerable<string> laneKeys)
    {
        var canceledLocks = new List<ActiveDesktopInputLockState>();
        foreach (var laneKey in laneKeys.Distinct(StringComparer.Ordinal))
        {
            if (!actionLanes.TryGetValue(laneKey, out var activeLane)
                || !activeDesktopInputLocks.TryGetValue(activeLane.OwnerId, out var activeLock))
            {
                continue;
            }

            activeLock.PacketsByLane.Remove(laneKey);
            if (activeLock.PacketsByLane.Count == 0)
            {
                activeDesktopInputLocks.Remove(activeLock.LeaseId);
                canceledLocks.Add(activeLock);
            }
        }

        return canceledLocks
            .DistinctBy(lockState => lockState.LeaseId)
            .ToList();
    }

    private IReadOnlyList<byte[]> GetDesktopInputLockPackets(ActiveDesktopInputLockState activeLock)
    {
        lock (stateGate)
        {
            return [.. activeLock.PacketsByLane.Values.SelectMany(static packets => packets)];
        }
    }

    private IReadOnlyList<string> ReleaseDesktopInputLock(ActiveDesktopInputLockState activeLock)
    {
        List<string> laneKeysToRelease;
        lock (stateGate)
        {
            if (activeDesktopInputLocks.TryGetValue(activeLock.LeaseId, out var currentLock)
                && ReferenceEquals(currentLock, activeLock))
            {
                activeDesktopInputLocks.Remove(activeLock.LeaseId);
            }

            laneKeysToRelease = [.. activeLock.PacketsByLane.Keys];
        }

        return ReleaseMovementLanes(activeLock.LeaseId, laneKeysToRelease);
    }

    private async Task RefreshDesktopInputLockScopeAsync(CancellationToken cancellationToken)
    {
        DesktopInputLockScope scope;
        lock (stateGate)
        {
            scope = activeConfiguration?.DesktopModeInputLockEnabled == true
                ? GetCombinedDesktopInputLockScopeLocked()
                : DesktopInputLockScope.None;
        }

        await desktopInputLockService.SetScopeAsync(scope, cancellationToken);
    }

    private DesktopInputLockScope GetCombinedDesktopInputLockScopeLocked()
    {
        var scope = DesktopInputLockScope.None;
        foreach (var activeLock in activeDesktopInputLocks.Values)
        {
            scope |= activeLock.Scope;
        }

        return scope;
    }

    private void ReleaseAllDesktopInputLocks(string reason, bool logRelease)
    {
        List<ActiveDesktopInputLockState> activeLocks;
        lock (stateGate)
        {
            activeLocks = [.. activeDesktopInputLocks.Values];
            activeDesktopInputLocks.Clear();
        }

        var releasedLaneKeys = new HashSet<string>(StringComparer.Ordinal);
        foreach (var activeLock in activeLocks)
        {
            activeLock.Cancellation.Cancel();
            foreach (var releasedLaneKey in ReleaseMovementLanes(activeLock.LeaseId, activeLock.PacketsByLane.Keys))
            {
                releasedLaneKeys.Add(releasedLaneKey);
            }

            activeLock.Cancellation.Dispose();
        }

        try
        {
            desktopInputLockService.ForceReleaseAsync(CancellationToken.None).GetAwaiter().GetResult();
        }
        catch (Exception ex)
        {
            WriteLog($"Crystal Relay could not fully release the desktop input lock: {ex.Message}");
        }

        if (logRelease && activeLocks.Count > 0)
        {
            WriteLog(reason);
        }

        foreach (var releasedLaneKey in releasedLaneKeys)
        {
            EnsureQueuedLaneDrain(releasedLaneKey);
        }
    }

    private void HandleEmergencyDesktopInputUnlock()
    {
        ReleaseAllDesktopInputLocks(
            $"Emergency desktop input unlock triggered with {DesktopInputLockService.EmergencyHotkeyDisplay}.",
            logRelease: true);
    }

    private IReadOnlyList<string> ReleaseMovementLanes(Guid ownerId, IEnumerable<string> laneKeys)
    {
        var releasedLaneKeys = new List<string>();
        if (ownerId == Guid.Empty)
        {
            return releasedLaneKeys;
        }

        lock (stateGate)
        {
            foreach (var laneKey in laneKeys.Distinct(StringComparer.Ordinal))
            {
                if (actionLanes.TryGetValue(laneKey, out var activeLane)
                    && activeLane.OwnerId == ownerId)
                {
                    actionLanes.Remove(laneKey);
                    releasedLaneKeys.Add(laneKey);
                }
            }
        }

        return releasedLaneKeys;
    }

    private async Task<ResolvedRuleAction> ResolveAvatarParameterActionAsync(
        TriggerRuleSnapshot rule,
        CancellationToken cancellationToken,
        bool preferLocalInstantToggleState)
    {
        var address = VrChatOscClient.NormalizeAvatarParameterAddress(rule.ParameterName);

        switch (rule.ParameterType)
        {
            case OscParameterType.Bool when rule.DurationSeconds <= 0:
            {
                // DEV NOTE: Instant bool toggles trust Crystal Relay's local/pushed state
                // before they trust a pulled OSCQuery read. The live OSCQuery tree can
                // sometimes lag one step behind immediately after a toggle, which causes
                // same-to-same sends like true->true. Prefer the last known local/pushed
                // value first, then use a pulled read only as a seed when nothing else exists.
                var hasCurrentValue = false;
                var currentValue = false;
                if (preferLocalInstantToggleState)
                {
                    hasCurrentValue = TryGetLocalInstantToggleState(address, out currentValue);
                }

                if (!hasCurrentValue)
                {
                    hasCurrentValue = TryGetObservedBool(address, out currentValue);
                }

                if (!hasCurrentValue)
                {
                    var refreshedState = await TryRefreshObservedBoolAsync(address, cancellationToken);
                    hasCurrentValue = refreshedState.Success;
                    currentValue = refreshedState.Value;
                }

                if (!hasCurrentValue)
                {
                    currentValue = false;
                    WriteLog($"'{rule.Name}' could not read the current bool state for {address}, so it defaulted to toggling from false to true.");
                }

                var nextValue = !currentValue;
                return new ResolvedRuleAction(
                    vrChatOscClient.BuildAvatarParameterPacket(address, OscParameterType.Bool, nextValue ? "True" : "False"),
                    null,
                    nextValue ? "True" : "False");
            }
            case OscParameterType.Int when rule.DurationSeconds <= 0:
            {
                var resolvedValue = rule.IntZeroDurationMode switch
                {
                    IntZeroDurationMode.Random => Random.Shared.Next(Math.Min(rule.RangeMinimum, rule.RangeMaximum), Math.Max(rule.RangeMinimum, rule.RangeMaximum) + 1),
                    IntZeroDurationMode.Cycle => await ResolveCycledIntValueAsync(rule, address, cancellationToken),
                    _ => int.Parse(rule.ParameterValue, CultureInfo.InvariantCulture)
                };

                return new ResolvedRuleAction(
                    vrChatOscClient.BuildAvatarParameterPacket(address, OscParameterType.Int, resolvedValue.ToString(CultureInfo.InvariantCulture)),
                    null,
                    resolvedValue.ToString(CultureInfo.InvariantCulture));
            }
            default:
            {
                var targetPacket = vrChatOscClient.BuildAvatarParameterPacket(address, rule.ParameterType, rule.ParameterValue);
                var displayValue = rule.ParameterValue;
                var observedValues = Array.Empty<OscObservedValue>();
                if (TryCreateObservedValueFromText(address, rule.ParameterType, rule.ParameterValue, out var targetText, out var targetObservedValue))
                {
                    displayValue = targetText;
                    observedValues = [targetObservedValue];
                }

                byte[]? resetPacket = null;
                var resetObservedValues = Array.Empty<OscObservedValue>();
                if (rule.DurationSeconds > 0 && !string.IsNullOrWhiteSpace(rule.ResetValue))
                {
                    resetPacket = vrChatOscClient.BuildAvatarParameterPacket(address, rule.ParameterType, rule.ResetValue);
                    if (TryCreateObservedValueFromText(address, rule.ParameterType, rule.ResetValue, out _, out var resetObservedValue))
                    {
                        resetObservedValues = [resetObservedValue];
                    }
                }

                return new ResolvedRuleAction(
                    [targetPacket],
                    resetPacket is null ? [] : [resetPacket],
                    displayValue,
                    observedValues: observedValues,
                    resetObservedValues: resetObservedValues);
            }
        }
    }

    private async Task<ResolvedRuleAction> ResolveSetTriggerActionAsync(
        TriggerRuleSnapshot rule,
        CancellationToken cancellationToken)
    {
        if (IsBitsOutfitSetTriggerRule(rule))
        {
            if (string.IsNullOrWhiteSpace(rule.SharedRewardHelpText))
            {
                throw new InvalidOperationException("Set the outfit name before using this Bits Set Trigger.");
            }
        }
        else if (!rule.SharedRewardChoiceEnabled || rule.SharedRewardChoiceNumber <= 0)
        {
            throw new InvalidOperationException("Set Trigger is only available for shared numbered rewards.");
        }

        var configuredActions = rule.SetTriggerActions
            .Where(action => action.ParameterType is OscParameterType.Bool or OscParameterType.Int or OscParameterType.Float
                && !string.IsNullOrWhiteSpace(action.ParameterName)
                && !string.IsNullOrWhiteSpace(action.ParameterValue))
            .ToArray();
        if (configuredActions.Length == 0)
        {
            throw new InvalidOperationException("Add at least one complete Set Trigger parameter before using this redeem.");
        }

        var addresses = new HashSet<string>(StringComparer.Ordinal);
        var preparedActions = new List<SetTriggerPreparedAction>(configuredActions.Length);

        foreach (var childAction in configuredActions)
        {
            var address = VrChatOscClient.NormalizeAvatarParameterAddress(childAction.ParameterName);
            if (VrChatLocalAvatarDataService.IsHeightOrScaleParameter(childAction.ParameterName)
                || VrChatLocalAvatarDataService.IsHeightOrScaleParameter(address))
            {
                throw new InvalidOperationException($"Safe-canceled Set Trigger '{rule.Name}' because {address} is height or avatar scale related. Use Avatar Scaling for height changes.");
            }

            if (!addresses.Add(address))
            {
                throw new InvalidOperationException($"Set Trigger '{rule.Name}' uses {address} more than once. Remove the duplicate before syncing or testing it.");
            }

            if (!TryCreateObservedValueFromText(address, childAction.ParameterType, childAction.ParameterValue, out var targetText, out var targetObservedValue))
            {
                throw new InvalidOperationException($"Set Trigger '{rule.Name}' has an invalid {childAction.ParameterType} value for {address}.");
            }

            preparedActions.Add(new SetTriggerPreparedAction(address, childAction.ParameterType, targetText, targetObservedValue));
        }

        var sourceAvatarId = ResolveSetTriggerSourceAvatarId(rule);
        var preTriggerSnapshot = await TryReadSetTriggerFullRestoreSnapshotAsync(
            rule.Name,
            sourceAvatarId,
            cancellationToken);

        var packets = new List<byte[]>(preparedActions.Count);
        var resetPackets = new List<byte[]>(preparedActions.Count);
        var observedValues = new List<OscObservedValue>(preparedActions.Count);
        var resetObservedValues = new List<OscObservedValue>(preparedActions.Count);
        var configuredRestoreAddresses = preparedActions
            .Select(action => action.Address)
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToArray();

        foreach (var preparedAction in preparedActions)
        {
            if (!preTriggerSnapshot.Values.TryGetValue(preparedAction.Address, out var originalValue)
                || !TryCreateObservedValueFromExisting(preparedAction.Address, preparedAction.ParameterType, originalValue, out var resetText, out var resetObservedValue))
            {
                throw new InvalidOperationException($"Safe-canceled Set Trigger '{rule.Name}' because Crystal Relay could not read the current {preparedAction.ParameterType} value for {preparedAction.Address}.");
            }

            packets.Add(vrChatOscClient.BuildAvatarParameterPacket(preparedAction.Address, preparedAction.ParameterType, preparedAction.TargetText));
            observedValues.Add(preparedAction.TargetObservedValue);
            resetPackets.Add(vrChatOscClient.BuildAvatarParameterPacket(preparedAction.Address, preparedAction.ParameterType, resetText));
            resetObservedValues.Add(resetObservedValue);
        }

        return new ResolvedRuleAction(
            packets,
            resetPackets,
            $"Set Trigger ({packets.Count} params, learning restore diff)",
            observedValues: observedValues,
            resetObservedValues: resetObservedValues,
            setTriggerRestorePlan: new SetTriggerRestorePlan(
                sourceAvatarId,
                preTriggerSnapshot.Values,
                preTriggerSnapshot.LastWriteTimeUtc,
                preTriggerSnapshot.SourcePath,
                configuredRestoreAddresses));
    }

    private string ResolveSetTriggerSourceAvatarId(TriggerRuleSnapshot rule)
    {
        var currentAvatarId = GetCurrentVrChatAvatarId();
        var requiredAvatarId = rule.RequiredAvatarId?.Trim() ?? string.Empty;
        if (rule.AvatarProfileId == Guid.Empty)
        {
            return currentAvatarId;
        }

        if (string.IsNullOrWhiteSpace(requiredAvatarId))
        {
            throw new InvalidOperationException($"Safe-canceled Set Trigger '{rule.Name}' because this Avatar Set does not have a saved VRChat avatar ID.");
        }

        if (string.IsNullOrWhiteSpace(currentAvatarId))
        {
            throw new InvalidOperationException($"Safe-canceled Set Trigger '{rule.Name}' because Crystal Relay does not know the current VRChat avatar yet. Wear the Avatar Set avatar, refresh VRChat/OSC, then try again.");
        }

        if (!string.Equals(currentAvatarId, requiredAvatarId, StringComparison.Ordinal))
        {
            throw new InvalidOperationException($"Safe-canceled Set Trigger '{rule.Name}' because the current avatar does not match this Avatar Set. Current: {DescribeAvatarId(currentAvatarId)}. Required: {DescribeAvatarId(requiredAvatarId)}.");
        }

        return requiredAvatarId;
    }

    private async Task<LocalAvatarDataParameterBatchReadResult> TryReadSetTriggerFullRestoreSnapshotAsync(
        string ruleName,
        string sourceAvatarId,
        CancellationToken cancellationToken)
    {
        var normalizedSourceAvatarId = sourceAvatarId?.Trim() ?? string.Empty;
        if (string.IsNullOrWhiteSpace(normalizedSourceAvatarId))
        {
            throw new InvalidOperationException($"Safe-canceled Set Trigger '{ruleName}' because Crystal Relay does not know the current VRChat avatar yet.");
        }

        var localValues = await vrChatLocalAvatarDataService.TryReadAvatarFullSnapshotValuesAsync(
            normalizedSourceAvatarId,
            cancellationToken);
        if (!localValues.Found)
        {
            throw new InvalidOperationException($"Safe-canceled Set Trigger '{ruleName}' because Crystal Relay could not capture a full LocalAvatarData restore snapshot: {localValues.FailureReason}");
        }

        var ageSeconds = Math.Max(0, (DateTime.UtcNow - localValues.LastWriteTimeUtc).TotalSeconds);
        WriteLog($"Captured full Set Trigger restore snapshot for '{ruleName}' with {localValues.Values.Count} safe typed value(s) from {DescribeLocalAvatarDataSource(localValues.SourcePath)} for avatar {DescribeAvatarId(normalizedSourceAvatarId)}. Cache age: {DescribeDuration(ageSeconds)}.");

        return localValues with
        {
            Values = new Dictionary<string, OscObservedValue>(localValues.Values, StringComparer.OrdinalIgnoreCase),
            MatchedParameterNames = new Dictionary<string, string>(localValues.MatchedParameterNames, StringComparer.OrdinalIgnoreCase)
        };
    }

    private static bool TryCreateObservedValueFromExisting(
        string address,
        OscParameterType expectedType,
        OscObservedValue? observedValue,
        out string valueText,
        out OscObservedValue normalizedValue)
    {
        valueText = string.Empty;
        normalizedValue = default!;
        if (observedValue?.ParameterType != expectedType)
        {
            return false;
        }

        switch (expectedType)
        {
            case OscParameterType.Bool when observedValue.Value is bool boolValue:
                valueText = boolValue ? "True" : "False";
                normalizedValue = new OscObservedValue(address, OscParameterType.Bool, boolValue);
                return true;
            case OscParameterType.Int when observedValue.Value is int intValue:
                valueText = intValue.ToString(CultureInfo.InvariantCulture);
                normalizedValue = new OscObservedValue(address, OscParameterType.Int, intValue);
                return true;
            case OscParameterType.Float when observedValue.Value is float floatValue:
                valueText = floatValue.ToString(CultureInfo.InvariantCulture);
                normalizedValue = new OscObservedValue(address, OscParameterType.Float, floatValue);
                return true;
            default:
                return false;
        }
    }

    private static bool TryCreateObservedValueFromText(
        string address,
        OscParameterType parameterType,
        string rawValue,
        out string valueText,
        out OscObservedValue observedValue)
    {
        valueText = string.Empty;
        observedValue = default!;
        switch (parameterType)
        {
            case OscParameterType.Bool:
                if (bool.TryParse(rawValue, out var boolValue)
                    || TryParseBoolNumber(rawValue, out boolValue))
                {
                    valueText = boolValue ? "True" : "False";
                    observedValue = new OscObservedValue(address, OscParameterType.Bool, boolValue);
                    return true;
                }

                return false;
            case OscParameterType.Int:
                if (int.TryParse(rawValue, NumberStyles.Integer, CultureInfo.InvariantCulture, out var intValue))
                {
                    valueText = intValue.ToString(CultureInfo.InvariantCulture);
                    observedValue = new OscObservedValue(address, OscParameterType.Int, intValue);
                    return true;
                }

                return false;
            case OscParameterType.Float:
                if (float.TryParse(rawValue, NumberStyles.Float | NumberStyles.AllowThousands, CultureInfo.InvariantCulture, out var floatValue))
                {
                    valueText = floatValue.ToString(CultureInfo.InvariantCulture);
                    observedValue = new OscObservedValue(address, OscParameterType.Float, floatValue);
                    return true;
                }

                return false;
            default:
                return false;
        }
    }

    private static bool TryParseBoolNumber(string rawValue, out bool value)
    {
        value = false;
        var normalizedValue = rawValue.Trim();
        if (string.Equals(normalizedValue, "1", StringComparison.Ordinal))
        {
            value = true;
            return true;
        }

        if (string.Equals(normalizedValue, "0", StringComparison.Ordinal))
        {
            return true;
        }

        return false;
    }

    private async Task<int> ResolveCycledIntValueAsync(TriggerRuleSnapshot rule, string address, CancellationToken cancellationToken)
    {
        var minimum = Math.Min(rule.RangeMinimum, rule.RangeMaximum);
        var maximum = Math.Max(rule.RangeMinimum, rule.RangeMaximum);

        var hasCurrentValue = TryGetObservedInt(address, out var currentValue);
        if (!hasCurrentValue)
        {
            var refreshedState = await TryRefreshObservedIntAsync(address, cancellationToken);
            hasCurrentValue = refreshedState.Success;
            currentValue = refreshedState.Value;
        }

        if (!hasCurrentValue)
        {
            return minimum;
        }

        if (currentValue < minimum || currentValue >= maximum)
        {
            return minimum;
        }

        return currentValue + 1;
    }

    private async Task<(bool Success, bool Value)> TryRefreshObservedBoolAsync(string address, CancellationToken cancellationToken)
    {
        try
        {
            var observedValue = await oscRouterService.GetCurrentAvatarParameterValueAsync(address, cancellationToken);
            if (observedValue?.ParameterType == OscParameterType.Bool && observedValue.Value is bool boolValue)
            {
                return (true, boolValue);
            }
        }
        catch (Exception ex)
        {
            WriteLog($"Crystal Relay could not read the live bool state for {address} through OSCQuery. {ex.Message}");
        }

        return (false, false);
    }

    private async Task<(bool Success, int Value)> TryRefreshObservedIntAsync(string address, CancellationToken cancellationToken)
    {
        try
        {
            var observedValue = await oscRouterService.GetCurrentAvatarParameterValueAsync(address, cancellationToken);
            if (observedValue?.ParameterType == OscParameterType.Int && observedValue.Value is int intValue)
            {
                return (true, intValue);
            }
        }
        catch (Exception ex)
        {
            WriteLog($"Crystal Relay could not read the live int state for {address} through OSCQuery. {ex.Message}");
        }

        return (false, 0);
    }

    private void RememberAvatarParameterValues(
        TriggerRuleSnapshot rule,
        IReadOnlyList<OscObservedValue>? observedValues,
        string fallbackResolvedValue)
    {
        if (observedValues is { Count: > 0 })
        {
            foreach (var observedValue in observedValues)
            {
                ObserveOscValue(observedValue);
            }

            return;
        }

        RememberAvatarParameterValue(rule, fallbackResolvedValue);
    }

    private void RememberAvatarParameterValue(TriggerRuleSnapshot rule, string resolvedValue)
    {
        if (rule.ActionType != OscActionType.AvatarParameter || string.IsNullOrWhiteSpace(rule.ParameterName))
        {
            return;
        }

        var address = VrChatOscClient.NormalizeAvatarParameterAddress(rule.ParameterName);
        OscObservedValue? observedValue = rule.ParameterType switch
        {
            OscParameterType.Bool when bool.TryParse(resolvedValue, out var boolValue)
                => new OscObservedValue(address, OscParameterType.Bool, boolValue),
            OscParameterType.Int when int.TryParse(resolvedValue, NumberStyles.Integer, CultureInfo.InvariantCulture, out var intValue)
                => new OscObservedValue(address, OscParameterType.Int, intValue),
            OscParameterType.Float when float.TryParse(resolvedValue, NumberStyles.Float | NumberStyles.AllowThousands, CultureInfo.InvariantCulture, out var floatValue)
                => new OscObservedValue(address, OscParameterType.Float, floatValue),
            _ => null
        };

        if (observedValue is not null)
        {
            ObserveOscValue(observedValue);
        }

        if (rule.ParameterType == OscParameterType.Bool
            && bool.TryParse(resolvedValue, out var resolvedBoolValue))
        {
            lock (stateGate)
            {
                localInstantToggleStates[address] = resolvedBoolValue;
            }
        }
    }

    private async Task TrySendBotMessageAsync(
        TriggerRuleSnapshot rule,
        BridgeIncomingEvent bridgeEvent,
        string resolvedValue,
        CancellationToken cancellationToken,
        double? durationSecondsOverride = null)
    {
        var configuration = activeConfiguration;
        if (configuration is null || broadcaster is null)
        {
            return;
        }

        var message = configuration.SupporterOverrideInfoMessageEnabled && IsSupporterOverrideRule(rule)
            ? BuildSupporterOverrideInfoBotMessage(configuration, rule, bridgeEvent, durationSecondsOverride)
            : BuildBotMessage(rule, bridgeEvent, resolvedValue, durationSecondsOverride);
        if (string.IsNullOrWhiteSpace(message))
        {
            return;
        }

        await TrySendChatMessageWithEffectiveSenderAsync(message, "Bot announcement", cancellationToken);
    }

    private async Task TrySendSharedRewardChoiceHelpAsync(
        string message,
        CancellationToken cancellationToken)
    {
        var configuration = activeConfiguration;
        if (configuration is null || broadcaster is null || string.IsNullOrWhiteSpace(message))
        {
            return;
        }

        await TrySendChatMessageWithEffectiveSenderAsync(message, "Shared reward choice help message", cancellationToken);
    }

    private async Task<bool> TrySendChatMessageWithEffectiveSenderAsync(
        string message,
        string failureContext,
        CancellationToken cancellationToken)
    {
        var configuration = activeConfiguration;
        if (configuration is null || broadcaster is null || string.IsNullOrWhiteSpace(message))
        {
            return false;
        }

        try
        {
            var sender = await ResolveChatMessageSenderAsync(configuration, cancellationToken);
            if (sender is null)
            {
                return false;
            }

            await twitchApiClient.SendChatMessageAsync(
                sender.AccessToken,
                configuration.TwitchClientId,
                broadcaster.UserId,
                sender.UserId,
                message,
                cancellationToken);
            return true;
        }
        catch (Exception ex)
        {
            WriteLog($"{failureContext} failed: {ex.Message}");
            return false;
        }
    }

    private async Task<TwitchAccountSnapshot?> ResolveChatMessageSenderAsync(
        BridgeRuntimeConfiguration configuration,
        CancellationToken cancellationToken)
    {
        if (configuration.UseBroadcasterAsBotSender)
        {
            if (broadcaster is null)
            {
                return null;
            }

            broadcaster = await EnsureAccountReadyAsync(
                broadcaster,
                [TwitchScopes.ChatWrite],
                BridgeAccountRole.Broadcaster,
                cancellationToken);
            return broadcaster;
        }

        if (bot is null)
        {
            return null;
        }

        bot = await EnsureAccountReadyAsync(bot, TwitchScopes.Bot, BridgeAccountRole.Bot, cancellationToken);
        return bot;
    }

    private void ScheduleReset(
        TriggerRuleSnapshot rule,
        ResolvedRuleAction action,
        double delaySeconds,
        IReadOnlyList<string>? laneKeys = null,
        Guid laneLeaseId = default,
        bool notifyManagedRewardState = true)
    {
        if (runtimeCancellation is null)
        {
            return;
        }

        PendingResetState? previousReset = null;
        lock (stateGate)
        {
            if (pendingResets.TryGetValue(rule.Id, out previousReset))
            {
                pendingResets.Remove(rule.Id);
            }
        }

        previousReset?.Cancellation.Cancel();

        var cancellation = CancellationTokenSource.CreateLinkedTokenSource(runtimeCancellation.Token);
        var sourceAvatarId = GetAvatarScopedResetSourceAvatarId(rule);
        var pendingReset = new PendingResetState(
            rule.Id,
            rule.Name,
            rule,
            action.ResetPackets,
            cancellation,
            action.AvatarResetId,
            action.AvatarResetName,
            sourceAvatarId,
            false,
            action.ResetObservedValues,
            laneKeys ?? [],
            laneLeaseId);

        lock (stateGate)
        {
            pendingResets[rule.Id] = pendingReset;
        }

        if (rule.ActionType == OscActionType.SetTrigger && action.HasResetPackets)
        {
            WriteLog(action.SetTriggerRestorePlan is not null
                ? $"Scheduled Set Trigger restore for '{rule.Name}' in {DescribeDuration(delaySeconds)}. Crystal Relay will re-check LocalAvatarData after {DescribeDuration(SetTriggerDiffObservationDelay.TotalSeconds)} before choosing restore params. Source avatar: {DescribeAvatarId(pendingReset.SourceAvatarId)}."
                : $"Scheduled Set Trigger restore for '{rule.Name}' in {DescribeDuration(delaySeconds)} with {action.ResetPackets.Count} param{(action.ResetPackets.Count == 1 ? string.Empty : "s")}. Source avatar: {DescribeAvatarId(pendingReset.SourceAvatarId)}.");
        }

        // Per-rule queue draining is used for cooldowns and temporary disable windows.
        // It waits until the rule is allowed to fire again, then replays queued redeems in order.
        _ = Task.Run(async () =>
        {
            var keepPendingReset = false;
            try
            {
                if (action.SetTriggerRestorePlan is not null)
                {
                    WriteLog($"Set Trigger '{rule.Name}' is waiting {DescribeDuration(SetTriggerDiffObservationDelay.TotalSeconds)} before reading the post-change LocalAvatarData snapshot.");
                    await Task.Delay(SetTriggerDiffObservationDelay, cancellation.Token);

                    var restoreResolution = await ResolveSetTriggerDiffRestoreAsync(
                        rule.Name,
                        action.SetTriggerRestorePlan,
                        action.ResetPackets,
                        action.ResetObservedValues,
                        cancellation.Token);
                    var previousPendingReset = pendingReset;
                    pendingReset = pendingReset with
                    {
                        Packets = restoreResolution.Packets,
                        ResetObservedValues = restoreResolution.ObservedValues
                    };

                    lock (stateGate)
                    {
                        if (pendingResets.TryGetValue(rule.Id, out var currentPendingReset)
                            && ReferenceEquals(currentPendingReset, previousPendingReset))
                        {
                            pendingResets[rule.Id] = pendingReset;
                        }
                    }

                    var remainingDelay = TimeSpan.FromSeconds(Math.Max(1, delaySeconds)) - SetTriggerDiffObservationDelay;
                    if (remainingDelay > TimeSpan.Zero)
                    {
                        await Task.Delay(remainingDelay, cancellation.Token);
                    }
                }
                else
                {
                    await Task.Delay(TimeSpan.FromSeconds(Math.Max(1, delaySeconds)), cancellation.Token);
                }

                if (pendingReset.HasPackets
                    && !string.IsNullOrWhiteSpace(pendingReset.SourceAvatarId)
                    && !string.Equals(GetCurrentVrChatAvatarId(), pendingReset.SourceAvatarId, StringComparison.Ordinal))
                {
                    var currentAvatarId = GetCurrentVrChatAvatarId();
                    keepPendingReset = MarkPendingResetWaitingForSourceAvatarReturn(pendingReset);
                    if (keepPendingReset)
                    {
                        var resetLabel = rule.ActionType == OscActionType.SetTrigger ? "Set Trigger restore" : "Timed reset";
                        WriteLog($"{resetLabel} for '{rule.Name}' is waiting because the current avatar {DescribeAvatarId(currentAvatarId)} does not match the source avatar {DescribeAvatarId(pendingReset.SourceAvatarId)}.");
                        return;
                    }
                }

                if (pendingReset.HasPackets)
                {
                    await SendPacketsToVrChatAsync(
                        pendingReset.Packets,
                        cancellation.Token,
                        rule.ActionType == OscActionType.SetTrigger ? SetTriggerPacketSpacing : null);
                    if (rule.ActionType == OscActionType.SetTrigger)
                    {
                        WriteLog($"Restored Set Trigger '{rule.Name}' with {pendingReset.Packets.Count} param{(pendingReset.Packets.Count == 1 ? string.Empty : "s")}.");
                    }
                }
                if (pendingReset.HasPackets)
                {
                    RememberAvatarParameterValues(rule, pendingReset.ResetObservedValues, rule.ResetValue);
                }
                if (pendingReset.HasPackets && !string.IsNullOrWhiteSpace(pendingReset.AvatarChangeResetId))
                {
                    LogPaidAvatarChangeAllowedDuringActiveScaling(rule);
                    SetCurrentVrChatAvatar(
                        pendingReset.AvatarChangeResetId,
                        notify: true,
                        GetAvatarScaleAvatarChangeCarryoverMode(rule));
                    SetSharedReturnAvatar(pendingReset.AvatarChangeResetId, pendingReset.AvatarChangeResetName, notify: true);
                }
                if (pendingReset.HasPackets)
                {
                    WriteLog($"Reset '{rule.Name}' after {DescribeDuration(delaySeconds)}.");
                }
            }
            catch (OperationCanceledException)
            {
            }
            catch (Exception ex)
            {
                WriteLog($"Failed to reset '{rule.Name}': {ex.Message}");
            }
            finally
            {
                var releasedPendingReset = false;
                if (!keepPendingReset)
                {
                    lock (stateGate)
                    {
                        if (pendingResets.TryGetValue(rule.Id, out var currentPendingReset)
                            && ReferenceEquals(currentPendingReset, pendingReset))
                        {
                            pendingResets.Remove(rule.Id);
                            releasedPendingReset = true;
                        }
                    }

                    if (releasedPendingReset)
                    {
                        var releasedLaneKeys = ReleaseMovementLanes(pendingReset.MovementLaneLeaseId, pendingReset.MovementLaneKeys);
                        if (notifyManagedRewardState)
                        {
                            ManagedRewardAvailabilityChanged?.Invoke();
                        }

                        foreach (var releasedLaneKey in releasedLaneKeys)
                        {
                            EnsureQueuedLaneDrain(releasedLaneKey);
                        }
                    }

                    cancellation.Dispose();
                }
            }
        }, CancellationToken.None);
    }

    private async Task<SetTriggerRestoreResolution> ResolveSetTriggerDiffRestoreAsync(
        string ruleName,
        SetTriggerRestorePlan restorePlan,
        IReadOnlyList<byte[]> fallbackPackets,
        IReadOnlyList<OscObservedValue> fallbackObservedValues,
        CancellationToken cancellationToken)
    {
        var postTriggerSnapshot = await vrChatLocalAvatarDataService.TryReadAvatarFullSnapshotValuesAsync(
            restorePlan.SourceAvatarId,
            cancellationToken);
        if (!postTriggerSnapshot.Found)
        {
            WriteLog($"Set Trigger '{ruleName}' could not re-check LocalAvatarData after {DescribeDuration(SetTriggerDiffObservationDelay.TotalSeconds)}, so Crystal Relay will restore the configured target params only. {postTriggerSnapshot.FailureReason}");
            return new SetTriggerRestoreResolution(fallbackPackets, fallbackObservedValues);
        }

        var postAgeSeconds = Math.Max(0, (DateTime.UtcNow - postTriggerSnapshot.LastWriteTimeUtc).TotalSeconds);
        WriteLog($"Re-checked Set Trigger LocalAvatarData for '{ruleName}' with {postTriggerSnapshot.Values.Count} safe typed value(s) from {DescribeLocalAvatarDataSource(postTriggerSnapshot.SourcePath)}. Cache age: {DescribeDuration(postAgeSeconds)}.");

        var restoreValues = new Dictionary<string, OscObservedValue>(StringComparer.OrdinalIgnoreCase);
        foreach (var originalValue in restorePlan.PreTriggerSnapshotValues.Values)
        {
            if (postTriggerSnapshot.Values.TryGetValue(originalValue.Address, out var currentValue)
                && !AreObservedValuesEquivalent(originalValue, currentValue))
            {
                restoreValues[originalValue.Address] = originalValue;
            }
        }

        var changedCount = restoreValues.Count;
        foreach (var configuredAddress in restorePlan.ConfiguredRestoreAddresses)
        {
            if (restorePlan.PreTriggerSnapshotValues.TryGetValue(configuredAddress, out var originalConfiguredValue))
            {
                restoreValues[originalConfiguredValue.Address] = originalConfiguredValue;
            }
        }

        var restoreResolution = BuildSetTriggerRestoreResolution(restoreValues.Values);
        WriteLog($"Set Trigger '{ruleName}' learned {changedCount} changed LocalAvatarData param{(changedCount == 1 ? string.Empty : "s")} and will restore {restoreResolution.Packets.Count} param{(restoreResolution.Packets.Count == 1 ? string.Empty : "s")}.");
        return restoreResolution;
    }

    private SetTriggerRestoreResolution BuildSetTriggerRestoreResolution(IEnumerable<OscObservedValue> restoreValues)
    {
        var packets = new List<byte[]>();
        var observedValues = new List<OscObservedValue>();

        foreach (var restoreValue in restoreValues.OrderBy(value => value.Address, StringComparer.OrdinalIgnoreCase))
        {
            if (!TryCreateObservedValueFromExisting(restoreValue.Address, restoreValue.ParameterType, restoreValue, out var restoreText, out var normalizedRestoreValue))
            {
                throw new InvalidOperationException($"Crystal Relay could not build a Set Trigger restore packet for {restoreValue.Address}.");
            }

            packets.Add(vrChatOscClient.BuildAvatarParameterPacket(normalizedRestoreValue.Address, normalizedRestoreValue.ParameterType, restoreText));
            observedValues.Add(normalizedRestoreValue);
        }

        return new SetTriggerRestoreResolution(packets, observedValues);
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

    private string GetAvatarScopedResetSourceAvatarId(TriggerRuleSnapshot rule)
    {
        if (rule.ActionType is not (OscActionType.AvatarParameter or OscActionType.SetTrigger)
            || rule.AvatarProfileId == Guid.Empty)
        {
            return string.Empty;
        }

        if (rule.ActionType == OscActionType.SetTrigger
            && !string.IsNullOrWhiteSpace(rule.RequiredAvatarId))
        {
            return rule.RequiredAvatarId.Trim();
        }

        return GetCurrentVrChatAvatarId();
    }

    private void ScheduleJumpPulseReset(
        TriggerRuleSnapshot rule,
        ResolvedRuleAction action,
        double durationSeconds,
        string? laneKey = null,
        Guid laneLeaseId = default,
        bool notifyManagedRewardState = true)
    {
        if (runtimeCancellation is null || action.ResetPacket is null)
        {
            return;
        }

        var effectiveDuration = TimeSpan.FromSeconds(Math.Max(1, durationSeconds));
        var shouldRepeat = effectiveDuration > TimeSpan.FromSeconds(1);
        var cancellation = CancellationTokenSource.CreateLinkedTokenSource(runtimeCancellation.Token);

        _ = Task.Run(async () =>
        {
            try
            {
                var endAt = DateTimeOffset.UtcNow.Add(effectiveDuration);

                await Task.Delay(JumpPulsePressDuration, cancellation.Token);
                if (IsMovementLaneLeaseActive(laneKey, laneLeaseId))
                {
                    await oscRouterService.SendToVrChatAsync(action.ResetPacket, cancellation.Token);
                }

                while (shouldRepeat && IsMovementLaneLeaseActive(laneKey, laneLeaseId))
                {
                    var remaining = endAt - DateTimeOffset.UtcNow;
                    if (remaining <= JumpPulsePressDuration)
                    {
                        break;
                    }

                    var delay = remaining > JumpPulseInterval + JumpPulsePressDuration
                        ? JumpPulseInterval
                        : remaining - JumpPulsePressDuration;
                    if (delay <= TimeSpan.Zero)
                    {
                        break;
                    }

                    await Task.Delay(delay, cancellation.Token);
                    if (!IsMovementLaneLeaseActive(laneKey, laneLeaseId))
                    {
                        break;
                    }

                    await oscRouterService.SendToVrChatAsync(action.Packet, cancellation.Token);
                    await Task.Delay(JumpPulsePressDuration, cancellation.Token);
                    if (!IsMovementLaneLeaseActive(laneKey, laneLeaseId))
                    {
                        break;
                    }

                    await oscRouterService.SendToVrChatAsync(action.ResetPacket, cancellation.Token);
                }

                var finalDelay = endAt - DateTimeOffset.UtcNow;
                if (finalDelay > TimeSpan.Zero)
                {
                    await Task.Delay(finalDelay, cancellation.Token);
                }
            }
            catch (OperationCanceledException)
            {
            }
            catch (Exception ex)
            {
                WriteLog($"Failed to repeat jump movement for '{rule.Name}': {ex.Message}");
            }
            finally
            {
                if (IsMovementLaneLeaseActive(laneKey, laneLeaseId))
                {
                    try
                    {
                        await oscRouterService.SendToVrChatAsync(action.ResetPacket, CancellationToken.None);
                    }
                    catch (Exception ex)
                    {
                        WriteLog($"Failed to release jump movement for '{rule.Name}': {ex.Message}");
                    }
                }

                var releasedLaneKeys = ReleaseMovementLanes(
                    laneLeaseId,
                    string.IsNullOrWhiteSpace(laneKey) ? Array.Empty<string>() : new[] { laneKey });
                if (releasedLaneKeys.Count > 0)
                {
                    if (notifyManagedRewardState)
                    {
                        ManagedRewardAvailabilityChanged?.Invoke();
                    }

                    foreach (var releasedLaneKey in releasedLaneKeys)
                    {
                        EnsureQueuedLaneDrain(releasedLaneKey);
                    }
                }

                cancellation.Dispose();
            }
        }, CancellationToken.None);
    }

    private bool IsMovementLaneLeaseActive(string? laneKey, Guid laneLeaseId)
    {
        if (string.IsNullOrWhiteSpace(laneKey) || laneLeaseId == Guid.Empty)
        {
            return false;
        }

        lock (stateGate)
        {
            return actionLanes.TryGetValue(laneKey, out var activeLane)
                && activeLane.OwnerId == laneLeaseId;
        }
    }

    private bool MarkPendingResetWaitingForSourceAvatarReturn(PendingResetState pendingReset)
    {
        lock (stateGate)
        {
            if (!pendingResets.TryGetValue(pendingReset.RuleId, out var currentPendingReset)
                || !ReferenceEquals(currentPendingReset, pendingReset))
            {
                return false;
            }

            pendingResets[pendingReset.RuleId] = pendingReset with { IsWaitingForSourceAvatarReturn = true };
            return true;
        }
    }

    private void ResumePendingAvatarScopedResetsForCurrentAvatar(string avatarId)
    {
        var normalizedAvatarId = avatarId?.Trim() ?? string.Empty;
        if (string.IsNullOrWhiteSpace(normalizedAvatarId))
        {
            return;
        }

        List<PendingResetState> resetsToResume;
        lock (stateGate)
        {
            resetsToResume = pendingResets.Values
                .Where(reset =>
                    reset.IsWaitingForSourceAvatarReturn
                    && !string.IsNullOrWhiteSpace(reset.SourceAvatarId)
                    && string.Equals(reset.SourceAvatarId, normalizedAvatarId, StringComparison.Ordinal))
                .ToList();
        }

        if (resetsToResume.Count == 0)
        {
            return;
        }

        _ = Task.Run(async () =>
        {
            foreach (var pendingReset in resetsToResume)
            {
                var removedPendingReset = false;
                lock (stateGate)
                {
                    if (pendingResets.TryGetValue(pendingReset.RuleId, out var currentPendingReset)
                        && ReferenceEquals(currentPendingReset, pendingReset))
                    {
                        pendingResets.Remove(pendingReset.RuleId);
                        removedPendingReset = true;
                    }
                }

                if (!removedPendingReset)
                {
                    continue;
                }

                try
                {
                    if (pendingReset.HasPackets)
                    {
                        await SendPacketsToVrChatAsync(pendingReset.Packets, CancellationToken.None);
                    }

                    if (pendingReset.HasPackets)
                    {
                        RememberAvatarParameterValues(pendingReset.Rule, pendingReset.ResetObservedValues, pendingReset.Rule.ResetValue);
                    }

                    if (pendingReset.HasPackets)
                    {
                        WriteLog($"Cleaned up '{pendingReset.RuleName}' after you returned to that avatar.");
                    }
                }
                catch (Exception ex)
                {
                    WriteLog($"Could not clean up '{pendingReset.RuleName}' after returning to that avatar: {ex.Message}");
                }
                finally
                {
                    var releasedLaneKeys = ReleaseMovementLanes(pendingReset.MovementLaneLeaseId, pendingReset.MovementLaneKeys);
                    foreach (var releasedLaneKey in releasedLaneKeys)
                    {
                        EnsureQueuedLaneDrain(releasedLaneKey);
                    }

                    pendingReset.Cancellation.Dispose();
                }
            }
        }, CancellationToken.None);
    }

    private async Task ResetPendingRulesAsync()
    {
        List<PendingResetState> pending;
        ActiveMovementSoftLockState[] activeLocks;
        ActiveDesktopInputLockState[] activeDesktopLocks;
        ActiveSupporterOverrideState? activeSupporterState;
        var lockoutsWereActive = false;
        CancellationTokenSource[] lockoutNotifications;
        CancellationTokenSource[] avatarSwitchLockoutNotifications;
        CancellationTokenSource? avatarScaleRestoreCancellation;

        lock (stateGate)
        {
            pending = [.. pendingResets.Values];
            activeLocks = [.. activeMovementLocks.Values];
            activeDesktopLocks = [.. activeDesktopInputLocks.Values];
            pendingResets.Clear();
            activeMovementLocks.Clear();
            activeDesktopInputLocks.Clear();
            cooldowns.Clear();
            queuedTriggers.Clear();
            drainingQueuedRules.Clear();
            ClearQueuedAvatarScaleOperationsLocked(includeTests: true);
            avatarScaleRestoreCancellation = avatarScaleRestoreSequenceCancellation;
            avatarScaleRestoreSequenceCancellation = null;
            activeAvatarScaleRestoreSequence = null;
            actionLanes.Clear();
            queuedLaneActions.Clear();
            drainingQueuedLanes.Clear();
            recentMessageIds.Clear();
            nextRecentMessagePruneAt = DateTimeOffset.MinValue;
            chatEmoteImageUrls.Clear();
            chatEmoteImageUrlInsertionOrder.Clear();
            cachedChatEmoteSetIds.Clear();
            cachedChatEmoteSetIdInsertionOrder.Clear();
            thirdPartyChatEmoteImageUrls.Clear();
            thirdPartyChatEmoteCodeInsertionOrder.Clear();
            thirdPartyChatEmoteIndex = new Dictionary<char, IReadOnlyList<ThirdPartyChatEmoteEntry>>();
            nextThirdPartyChatEmoteRefreshAt = DateTimeOffset.MinValue;
            lockoutsWereActive = activeRuleLockouts.Count > 0
                || activeAvatarSwitchRuleLockouts.Count > 0
                || activeSupporterOverride is not null
                || queuedSupporterOverrides.Count > 0;
            activeRuleLockouts.Clear();
            activeAvatarSwitchRuleLockouts.Clear();
            lastAvatarRouletResultIds.Clear();
            activeSupporterState = activeSupporterOverride;
            activeSupporterOverride = null;
            queuedSupporterOverrides.Clear();
            nextSupporterOverrideQueueOrder = 0;
            lockoutNotifications = [.. lockoutStateNotifications.Values];
            lockoutStateNotifications.Clear();
            avatarSwitchLockoutNotifications = [.. avatarSwitchLockoutStateNotifications.Values];
            avatarSwitchLockoutStateNotifications.Clear();
            foreach (var notification in cooldownStateNotifications.Values.ToArray())
            {
                notification.Cancel();
                notification.Dispose();
            }
            cooldownStateNotifications.Clear();
        }

        if (lockoutsWereActive)
        {
            ManagedRewardAvailabilityChanged?.Invoke();
        }

        foreach (var notification in lockoutNotifications)
        {
            notification.Cancel();
            notification.Dispose();
        }

        foreach (var notification in avatarSwitchLockoutNotifications)
        {
            notification.Cancel();
            notification.Dispose();
        }

        avatarScaleRestoreCancellation?.Cancel();

        if (activeSupporterState is not null)
        {
            try
            {
                activeSupporterState.CompletionCancellation.Cancel();
                await ResetRuleEffectAsync(
                    activeSupporterState.Rule,
                    activeSupporterState.Action.ResetPacket,
                    activeSupporterState.Action.AvatarResetId,
                    activeSupporterState.Action.AvatarResetName,
                    CancellationToken.None);
            }
            catch
            {
            }
            finally
            {
                activeSupporterState.CompletionCancellation.Dispose();
            }
        }

        foreach (var reset in pending)
        {
            try
            {
                reset.Cancellation.Cancel();
                if (reset.HasPackets)
                {
                    await SendPacketsToVrChatAsync(reset.Packets, CancellationToken.None);
                }
            }
            catch
            {
            }
            finally
            {
                reset.Cancellation.Dispose();
            }
        }

        foreach (var activeLock in activeLocks)
        {
            try
            {
                activeLock.Cancellation.Cancel();
                var finalPackets = GetMovementSoftLockReleasePackets(activeLock);
                if (finalPackets.Count > 0)
                {
                    await SendPacketsToVrChatAsync(finalPackets, CancellationToken.None);
                }
            }
            catch
            {
            }
            finally
            {
                activeLock.Cancellation.Dispose();
            }
        }

        foreach (var activeLock in activeDesktopLocks)
        {
            try
            {
                activeLock.Cancellation.Cancel();
                var finalPackets = GetDesktopInputLockPackets(activeLock);
                if (finalPackets.Count > 0)
                {
                    await SendPacketsToVrChatAsync(finalPackets, CancellationToken.None);
                }
            }
            catch
            {
            }
            finally
            {
                activeLock.Cancellation.Dispose();
            }
        }

        try
        {
            await desktopInputLockService.ForceReleaseAsync(CancellationToken.None);
        }
        catch
        {
        }
    }

    private void ScheduleCooldownStateNotification(Guid ruleId, TimeSpan delay)
    {
        CancellationTokenSource cancellation;

        lock (stateGate)
        {
            if (cooldownStateNotifications.TryGetValue(ruleId, out var existingNotification))
            {
                existingNotification.Cancel();
                existingNotification.Dispose();
            }

            cancellation = new CancellationTokenSource();
            cooldownStateNotifications[ruleId] = cancellation;
        }

        // Movement actions use a separate queue from standard redeems so long-running
        // movement effects do not trample each other while a lane is still busy.
        _ = Task.Run(async () =>
        {
            try
            {
                await Task.Delay(delay, cancellation.Token);

                var shouldNotify = false;
                lock (stateGate)
                {
                    if (!cooldownStateNotifications.TryGetValue(ruleId, out var currentNotification)
                        || !ReferenceEquals(currentNotification, cancellation))
                    {
                        return;
                    }

                    cooldownStateNotifications.Remove(ruleId);
                    if (cooldowns.TryGetValue(ruleId, out var cooldownUntil)
                        && cooldownUntil <= DateTimeOffset.UtcNow)
                    {
                        cooldowns.Remove(ruleId);
                        shouldNotify = true;
                    }
                }

                if (shouldNotify)
                {
                    ManagedRewardAvailabilityChanged?.Invoke();
                }
            }
            catch (OperationCanceledException)
            {
            }
            finally
            {
                cancellation.Dispose();
            }
        }, CancellationToken.None);
    }

    private void CancelCooldownStateNotification(Guid ruleId)
    {
        lock (stateGate)
        {
            if (!cooldownStateNotifications.TryGetValue(ruleId, out var notification))
            {
                return;
            }

            cooldownStateNotifications.Remove(ruleId);
            notification.Cancel();
            notification.Dispose();
        }
    }

    private void ScheduleAvatarScaleEffectStateNotification(Guid ruleId, TimeSpan delay)
    {
        CancellationTokenSource cancellation;

        lock (stateGate)
        {
            if (avatarScaleEffectStateNotifications.TryGetValue(ruleId, out var existingNotification))
            {
                existingNotification.Cancel();
                existingNotification.Dispose();
            }

            cancellation = new CancellationTokenSource();
            avatarScaleEffectStateNotifications[ruleId] = cancellation;
        }

        _ = Task.Run(async () =>
        {
            try
            {
                await Task.Delay(delay, cancellation.Token);

                var shouldNotify = false;
                lock (stateGate)
                {
                    if (!avatarScaleEffectStateNotifications.TryGetValue(ruleId, out var currentNotification)
                        || !ReferenceEquals(currentNotification, cancellation))
                    {
                        return;
                    }

                    avatarScaleEffectStateNotifications.Remove(ruleId);
                    if (activeAvatarScaleEffects.TryGetValue(ruleId, out var activeUntil)
                        && activeUntil <= DateTimeOffset.UtcNow)
                    {
                        activeAvatarScaleEffects.Remove(ruleId);
                        shouldNotify = true;
                    }
                }

                if (shouldNotify)
                {
                    ManagedRewardAvailabilityChanged?.Invoke();
                }
            }
            catch (OperationCanceledException)
            {
            }
            finally
            {
                cancellation.Dispose();
            }
        }, CancellationToken.None);
    }

    private void ScheduleAvatarScaleMasterStateNotification(TimeSpan delay, bool isUnlockNotification)
    {
        if (delay <= TimeSpan.Zero)
        {
            ManagedRewardAvailabilityChanged?.Invoke();
            return;
        }

        CancellationTokenSource cancellation;

        lock (stateGate)
        {
            var existingNotification = isUnlockNotification
                ? avatarScaleMasterUnlockNotification
                : avatarScaleMasterCooldownNotification;
            existingNotification?.Cancel();
            existingNotification?.Dispose();

            cancellation = new CancellationTokenSource();
            if (isUnlockNotification)
            {
                avatarScaleMasterUnlockNotification = cancellation;
            }
            else
            {
                avatarScaleMasterCooldownNotification = cancellation;
            }
        }

        _ = Task.Run(async () =>
        {
            try
            {
                await Task.Delay(delay, cancellation.Token);

                lock (stateGate)
                {
                    if (isUnlockNotification && ReferenceEquals(avatarScaleMasterUnlockNotification, cancellation))
                    {
                        avatarScaleMasterUnlockNotification = null;
                    }
                    else if (!isUnlockNotification && ReferenceEquals(avatarScaleMasterCooldownNotification, cancellation))
                    {
                        avatarScaleMasterCooldownNotification = null;
                    }
                    else
                    {
                        return;
                    }
                }

                ManagedRewardAvailabilityChanged?.Invoke();
            }
            catch (OperationCanceledException)
            {
            }
            finally
            {
                cancellation.Dispose();
            }
        }, CancellationToken.None);
    }

    private void CancelAvatarScaleMasterCooldownNotification()
    {
        lock (stateGate)
        {
            avatarScaleMasterCooldownNotification?.Cancel();
            avatarScaleMasterCooldownNotification?.Dispose();
            avatarScaleMasterCooldownNotification = null;
        }
    }

    private void RefreshActiveRuleLockoutsForConfiguration(
        IReadOnlyList<TriggerRuleSnapshot> rules,
        IReadOnlyList<AvatarScaleRuleSnapshot> avatarScaleRules)
    {
        var now = DateTimeOffset.UtcNow;
        var changed = false;
        var validRulesById = rules.ToDictionary(rule => rule.Id, rule => rule);
        var validScaleRulesById = avatarScaleRules.ToDictionary(rule => rule.Id, rule => rule);
        var validRuleIds = validRulesById.Keys.Concat(validScaleRulesById.Keys).ToHashSet();
        List<CancellationTokenSource>? notificationsToDispose = null;

        lock (stateGate)
        {
            var before = GetTemporarilyDisabledRuleIdsLocked(now);

            foreach (var sourceRuleId in activeRuleLockouts.Keys.ToArray())
            {
                var isAvatarRuleLockout = validRulesById.TryGetValue(sourceRuleId, out var currentRule);
                var isScaleRuleLockout = validScaleRulesById.TryGetValue(sourceRuleId, out var currentScaleRule);
                if (!isAvatarRuleLockout && !isScaleRuleLockout)
                {
                    activeRuleLockouts.Remove(sourceRuleId);
                    if (lockoutStateNotifications.Remove(sourceRuleId, out var removedNotification))
                    {
                        notificationsToDispose ??= [];
                        notificationsToDispose.Add(removedNotification);
                    }
                    continue;
                }

                var disabledRuleIds = isAvatarRuleLockout
                    ? currentRule!.TemporarilyDisabledRuleIds
                    : currentScaleRule!.TemporarilyDisabledRuleIds;
                var sourceName = isAvatarRuleLockout
                    ? currentRule!.Name
                    : currentScaleRule!.Name;
                var lockoutDurationSeconds = isAvatarRuleLockout
                    ? GetLockoutDurationSeconds(currentRule!)
                    : GetAvatarScaleLockoutDurationSeconds(currentScaleRule!);
                var normalizedRuleIds = disabledRuleIds
                    .Where(ruleId => ruleId != sourceRuleId && validRuleIds.Contains(ruleId))
                    .Distinct()
                    .ToArray();

                if (lockoutDurationSeconds <= 0 || normalizedRuleIds.Length == 0)
                {
                    activeRuleLockouts.Remove(sourceRuleId);
                    if (lockoutStateNotifications.Remove(sourceRuleId, out var removedNotification))
                    {
                        notificationsToDispose ??= [];
                        notificationsToDispose.Add(removedNotification);
                    }
                    continue;
                }

                var existingState = activeRuleLockouts[sourceRuleId];
                activeRuleLockouts[sourceRuleId] = new ActiveRuleLockoutState(
                    sourceName,
                    existingState.ExpiresAt,
                    normalizedRuleIds);
            }

            foreach (var scaleRuleId in activeAvatarScaleEffects.Keys.ToArray())
            {
                if (!validScaleRulesById.ContainsKey(scaleRuleId))
                {
                    activeAvatarScaleEffects.Remove(scaleRuleId);
                    if (avatarScaleEffectStateNotifications.Remove(scaleRuleId, out var removedNotification))
                    {
                        notificationsToDispose ??= [];
                        notificationsToDispose.Add(removedNotification);
                    }
                }
            }

            var after = GetTemporarilyDisabledRuleIdsLocked(now);
            changed = !before.SetEquals(after);
        }

        if (changed)
        {
            ManagedRewardAvailabilityChanged?.Invoke();
        }

        if (notificationsToDispose is not null)
        {
            foreach (var notification in notificationsToDispose)
            {
                notification.Cancel();
                notification.Dispose();
            }
        }
    }

    private void RefreshActiveAvatarSwitchLockoutsForConfiguration(IReadOnlyList<TriggerRuleSnapshot> rules)
    {
        var now = DateTimeOffset.UtcNow;
        var changed = false;
        var validRulesById = rules.ToDictionary(rule => rule.Id, rule => rule);
        var masterAvatarSwitchRuleIds = GetMasterAvatarSwitchRuleIds(rules);
        List<CancellationTokenSource>? notificationsToDispose = null;

        lock (stateGate)
        {
            var before = GetTemporarilyDisabledRuleIdsLocked(now);

            foreach (var sourceRuleId in activeAvatarSwitchRuleLockouts.Keys.ToArray())
            {
                if (!validRulesById.TryGetValue(sourceRuleId, out var currentRule)
                    || masterAvatarSwitchRuleIds.Length == 0)
                {
                    activeAvatarSwitchRuleLockouts.Remove(sourceRuleId);
                    if (avatarSwitchLockoutStateNotifications.Remove(sourceRuleId, out var removedNotification))
                    {
                        notificationsToDispose ??= [];
                        notificationsToDispose.Add(removedNotification);
                    }

                    continue;
                }

                var existingState = activeAvatarSwitchRuleLockouts[sourceRuleId];
                activeAvatarSwitchRuleLockouts[sourceRuleId] = new ActiveRuleLockoutState(
                    currentRule.Name,
                    existingState.ExpiresAt,
                    masterAvatarSwitchRuleIds);
            }

            var after = GetTemporarilyDisabledRuleIdsLocked(now);
            changed = !before.SetEquals(after);
        }

        if (changed)
        {
            ManagedRewardAvailabilityChanged?.Invoke();
        }

        if (notificationsToDispose is not null)
        {
            foreach (var notification in notificationsToDispose)
            {
                notification.Cancel();
                notification.Dispose();
            }
        }
    }

    private void RefreshAvatarScaleSupporterGrowthStatesForConfiguration(IReadOnlyList<AvatarScaleRuleSnapshot> avatarScaleRules)
    {
        var validAvatarScaleRuleIds = avatarScaleRules
            .Select(rule => rule.Id)
            .ToHashSet();
        var validRuleIds = avatarScaleRules
            .Where(rule => rule.TriggerType == AvatarScaleTriggerType.SupporterGrowth)
            .Select(rule => rule.Id)
            .ToHashSet();
        List<CancellationTokenSource>? cancellationsToDispose = null;

        lock (stateGate)
        {
            foreach (var ruleId in activeAvatarScaleHeightSessions.Keys.ToArray())
            {
                if (!validAvatarScaleRuleIds.Contains(ruleId))
                {
                    activeAvatarScaleHeightSessions.Remove(ruleId);
                }
            }

            foreach (var ruleId in avatarScaleSupporterGrowthStates.Keys.ToArray())
            {
                if (validRuleIds.Contains(ruleId))
                {
                    continue;
                }

                var state = avatarScaleSupporterGrowthStates[ruleId];
                if (state.SessionCancellation is not null)
                {
                    cancellationsToDispose ??= [];
                    cancellationsToDispose.Add(state.SessionCancellation);
                }

                avatarScaleSupporterGrowthStates.Remove(ruleId);
            }
        }

        if (cancellationsToDispose is null)
        {
            return;
        }

        foreach (var cancellation in cancellationsToDispose)
        {
            cancellation.Cancel();
        }
    }

    private void RefreshAvatarScaleMasterStateForConfiguration(AvatarScaleMasterRewardSnapshot? masterReward)
    {
        if (masterReward?.IsEnabled == true)
        {
            return;
        }

        var changed = false;
        CancellationTokenSource? unlockNotification;
        CancellationTokenSource? cooldownNotification;
        lock (stateGate)
        {
            changed = avatarScaleMasterUnlockUntil > DateTimeOffset.MinValue
                || avatarScaleMasterCooldownUntil > DateTimeOffset.MinValue;
            avatarScaleMasterUnlockUntil = DateTimeOffset.MinValue;
            avatarScaleMasterCooldownUntil = DateTimeOffset.MinValue;
            unlockNotification = avatarScaleMasterUnlockNotification;
            avatarScaleMasterUnlockNotification = null;
            cooldownNotification = avatarScaleMasterCooldownNotification;
            avatarScaleMasterCooldownNotification = null;
        }

        unlockNotification?.Cancel();
        unlockNotification?.Dispose();
        cooldownNotification?.Cancel();
        cooldownNotification?.Dispose();

        if (changed)
        {
            ManagedRewardAvailabilityChanged?.Invoke();
        }
    }

    private void UpdateActiveRuleLockoutState(TriggerRuleSnapshot rule)
    {
        var normalizedRuleIds = rule.TemporarilyDisabledRuleIds
            .Where(ruleId => ruleId != rule.Id)
            .Distinct()
            .ToArray();
        var lockoutDurationSeconds = GetLockoutDurationSeconds(rule);

        if (lockoutDurationSeconds <= 0 || normalizedRuleIds.Length == 0)
        {
            ReleaseActiveRuleLockoutState(rule.Id, logRelease: false);
            return;
        }

        var now = DateTimeOffset.UtcNow;
        var changed = false;
        lock (stateGate)
        {
            var before = GetTemporarilyDisabledRuleIdsLocked(now);
            activeRuleLockouts[rule.Id] = new ActiveRuleLockoutState(
                rule.Name,
                now.AddSeconds(lockoutDurationSeconds),
                normalizedRuleIds);
            var after = GetTemporarilyDisabledRuleIdsLocked(now);
            changed = !before.SetEquals(after);
        }

        ScheduleLockoutStateNotification(rule.Id, TimeSpan.FromSeconds(lockoutDurationSeconds));

        if (changed)
        {
            WriteLog($"'{rule.Name}' temporarily disabled {normalizedRuleIds.Length} linked redeem{(normalizedRuleIds.Length == 1 ? string.Empty : "s")} while it stays active.");
            ManagedRewardAvailabilityChanged?.Invoke();
        }
    }

    private void UpdateActiveAvatarScaleRuleLockoutState(AvatarScaleRuleSnapshot rule)
    {
        var normalizedRuleIds = rule.TemporarilyDisabledRuleIds
            .Where(ruleId => ruleId != rule.Id)
            .Distinct()
            .ToArray();
        var lockoutDurationSeconds = GetAvatarScaleLockoutDurationSeconds(rule);

        if (lockoutDurationSeconds <= 0 || normalizedRuleIds.Length == 0)
        {
            ReleaseActiveRuleLockoutState(rule.Id, logRelease: false);
            return;
        }

        var now = DateTimeOffset.UtcNow;
        var changed = false;
        lock (stateGate)
        {
            var before = GetTemporarilyDisabledRuleIdsLocked(now);
            activeRuleLockouts[rule.Id] = new ActiveRuleLockoutState(
                rule.Name,
                now.AddSeconds(lockoutDurationSeconds),
                normalizedRuleIds);
            var after = GetTemporarilyDisabledRuleIdsLocked(now);
            changed = !before.SetEquals(after);
        }

        ScheduleLockoutStateNotification(rule.Id, TimeSpan.FromSeconds(lockoutDurationSeconds));

        if (changed)
        {
            WriteLog($"Avatar scale '{rule.Name}' temporarily disabled {normalizedRuleIds.Length} linked scale redeem{(normalizedRuleIds.Length == 1 ? string.Empty : "s")} while it stays active.");
            ManagedRewardAvailabilityChanged?.Invoke();
        }
    }

    private void UpdateActiveAvatarSwitchLockoutState(TriggerRuleSnapshot rule)
    {
        if (rule.ActionType != OscActionType.AvatarRoulet)
        {
            ReleaseActiveAvatarSwitchLockoutState(rule.Id, logRelease: false);
            return;
        }

        var cooldownSeconds = GetCooldownSeconds(rule);
        var masterAvatarSwitchRuleIds = activeConfiguration is null
            ? []
            : GetMasterAvatarSwitchRuleIds(activeConfiguration.Rules);
        if (cooldownSeconds <= 0 || masterAvatarSwitchRuleIds.Length == 0)
        {
            ReleaseActiveAvatarSwitchLockoutState(rule.Id, logRelease: false);
            return;
        }

        var now = DateTimeOffset.UtcNow;
        var changed = false;
        lock (stateGate)
        {
            var before = GetTemporarilyDisabledRuleIdsLocked(now);
            activeAvatarSwitchRuleLockouts[rule.Id] = new ActiveRuleLockoutState(
                rule.Name,
                now.AddSeconds(cooldownSeconds),
                masterAvatarSwitchRuleIds);
            var after = GetTemporarilyDisabledRuleIdsLocked(now);
            changed = !before.SetEquals(after);
        }

        ScheduleAvatarSwitchLockoutStateNotification(rule.Id, TimeSpan.FromSeconds(cooldownSeconds));

        if (changed)
        {
            WriteLog($"'{rule.Name}' kept avatar-switch redeems turned off during its cooldown.");
            ManagedRewardAvailabilityChanged?.Invoke();
        }
    }

    private static int GetLockoutDurationSeconds(TriggerRuleSnapshot rule)
    {
        if (rule.TemporarilyDisabledRuleIds.Count == 0)
        {
            return 0;
        }

        var activeDurationSeconds = Math.Max(0, rule.DurationSeconds);
        var cooldownSeconds = GetCooldownSeconds(rule);
        return Math.Max(activeDurationSeconds, cooldownSeconds);
    }

    private static int GetAvatarScaleLockoutDurationSeconds(AvatarScaleRuleSnapshot rule)
    {
        if (rule.TemporarilyDisabledRuleIds.Count == 0)
        {
            return 0;
        }

        return GetAvatarScaleEffectDurationSeconds(rule);
    }

    private static int GetAvatarScaleEffectiveCooldownSeconds(AvatarScaleRuleSnapshot rule)
    {
        return Math.Max(0, rule.CooldownSeconds);
    }

    private static int GetAvatarScaleEffectDurationSeconds(AvatarScaleRuleSnapshot rule)
    {
        var transitionSeconds = Math.Max(0, rule.SmoothTransitionSeconds);
        var activeSeconds = Math.Max(0, rule.ActiveTimeSeconds);
        var restoreTransitionSeconds = activeSeconds > 0 && rule.RestoreMode != AvatarScaleRestoreMode.None
            ? transitionSeconds
            : 0;
        return (int)Math.Ceiling(transitionSeconds + activeSeconds + restoreTransitionSeconds);
    }

    private void ReleaseActiveRuleLockoutState(Guid sourceRuleId, bool logRelease)
    {
        var changed = false;
        ActiveRuleLockoutState? releasedState = null;
        CancellationTokenSource? notificationToDispose = null;

        lock (stateGate)
        {
            var before = GetTemporarilyDisabledRuleIdsLocked(DateTimeOffset.UtcNow);
            if (!activeRuleLockouts.TryGetValue(sourceRuleId, out releasedState))
            {
                return;
            }

            activeRuleLockouts.Remove(sourceRuleId);
            if (lockoutStateNotifications.Remove(sourceRuleId, out var notification))
            {
                notificationToDispose = notification;
            }
            var after = GetTemporarilyDisabledRuleIdsLocked(DateTimeOffset.UtcNow);
            changed = !before.SetEquals(after);
        }

        if (notificationToDispose is not null)
        {
            notificationToDispose.Cancel();
            notificationToDispose.Dispose();
        }

        if (changed)
        {
            if (logRelease && releasedState is not null)
            {
                WriteLog($"Re-enabled {releasedState.DisabledRuleIds.Count} linked redeem{(releasedState.DisabledRuleIds.Count == 1 ? string.Empty : "s")} after '{releasedState.SourceRuleName}' finished.");
            }

            ManagedRewardAvailabilityChanged?.Invoke();
        }
    }

    private void ReleaseActiveAvatarSwitchLockoutState(Guid sourceRuleId, bool logRelease)
    {
        var changed = false;
        ActiveRuleLockoutState? releasedState = null;
        CancellationTokenSource? notificationToDispose = null;

        lock (stateGate)
        {
            var before = GetTemporarilyDisabledRuleIdsLocked(DateTimeOffset.UtcNow);
            if (!activeAvatarSwitchRuleLockouts.TryGetValue(sourceRuleId, out releasedState))
            {
                return;
            }

            activeAvatarSwitchRuleLockouts.Remove(sourceRuleId);
            if (avatarSwitchLockoutStateNotifications.Remove(sourceRuleId, out var notification))
            {
                notificationToDispose = notification;
            }
            var after = GetTemporarilyDisabledRuleIdsLocked(DateTimeOffset.UtcNow);
            changed = !before.SetEquals(after);
        }

        if (notificationToDispose is not null)
        {
            notificationToDispose.Cancel();
            notificationToDispose.Dispose();
        }

        if (changed)
        {
            if (logRelease && releasedState is not null)
            {
                WriteLog($"Avatar-switch redeems came back after '{releasedState.SourceRuleName}' finished cooling down.");
            }

            ManagedRewardAvailabilityChanged?.Invoke();
        }
    }

    private HashSet<Guid> GetTemporarilyDisabledRuleIdsLocked(DateTimeOffset now)
    {
        RemoveExpiredLockoutsLocked(activeRuleLockouts, now);
        RemoveExpiredLockoutsLocked(activeAvatarSwitchRuleLockouts, now);

        var disabledRuleIds = new HashSet<Guid>();
        foreach (var state in activeRuleLockouts.Values)
        {
            foreach (var ruleId in state.DisabledRuleIds)
            {
                disabledRuleIds.Add(ruleId);
            }
        }

        foreach (var state in activeAvatarSwitchRuleLockouts.Values)
        {
            foreach (var ruleId in state.DisabledRuleIds)
            {
                disabledRuleIds.Add(ruleId);
            }
        }

        if (IsSupporterOverrideSequenceActiveLocked(now))
        {
            disabledRuleIds.UnionWith(supporterOverrideBlockedRuleIds);
        }

        return disabledRuleIds;
    }

    private bool TryGetTemporarilyDisabledUntilLocked(Guid ruleId, DateTimeOffset now, out DateTimeOffset temporarilyDisabledUntil)
    {
        RemoveExpiredLockoutsLocked(activeRuleLockouts, now);
        RemoveExpiredLockoutsLocked(activeAvatarSwitchRuleLockouts, now);

        var blockedUntil = DateTimeOffset.MinValue;
        foreach (var state in activeRuleLockouts.Values)
        {
            if (state.ExpiresAt > now
                && state.DisabledRuleIds.Contains(ruleId)
                && state.ExpiresAt > blockedUntil)
            {
                blockedUntil = state.ExpiresAt;
            }
        }

        foreach (var state in activeAvatarSwitchRuleLockouts.Values)
        {
            if (state.ExpiresAt > now
                && state.DisabledRuleIds.Contains(ruleId)
                && state.ExpiresAt > blockedUntil)
            {
                blockedUntil = state.ExpiresAt;
            }
        }

        if (TryGetSupporterOverrideSuppressionUntilLocked(ruleId, now, out var supporterSuppressionUntil)
            && supporterSuppressionUntil > blockedUntil)
        {
            blockedUntil = supporterSuppressionUntil;
        }

        if (blockedUntil <= now)
        {
            temporarilyDisabledUntil = default;
            return false;
        }

        temporarilyDisabledUntil = blockedUntil;
        return true;
    }

    private Guid[] GetMasterAvatarSwitchRuleIds(IReadOnlyList<TriggerRuleSnapshot> rules)
    {
        return rules
            .Where(rule =>
                rule.BelongsToMasterAvatarProfile
                && rule.ActionType is OscActionType.AvatarChange or OscActionType.AvatarRoulet)
            .Select(rule => rule.Id)
            .Distinct()
            .ToArray();
    }

    private HashSet<Guid> GetAvatarSwitchTemporarilyDisabledRuleIdsLocked(DateTimeOffset now)
    {
        RemoveExpiredLockoutsLocked(activeAvatarSwitchRuleLockouts, now);

        if (activeAvatarSwitchRuleLockouts.Count == 0)
        {
            return [];
        }

        var disabledRuleIds = new HashSet<Guid>();
        foreach (var state in activeAvatarSwitchRuleLockouts.Values)
        {
            foreach (var ruleId in state.DisabledRuleIds)
            {
                disabledRuleIds.Add(ruleId);
            }
        }

        return disabledRuleIds;
    }

    private static void RemoveExpiredLockoutsLocked(
        Dictionary<Guid, ActiveRuleLockoutState> lockouts,
        DateTimeOffset now)
    {
        List<Guid>? expiredRuleIds = null;
        foreach (var lockout in lockouts)
        {
            if (lockout.Value.ExpiresAt <= now)
            {
                expiredRuleIds ??= [];
                expiredRuleIds.Add(lockout.Key);
            }
        }

        if (expiredRuleIds is null)
        {
            return;
        }

        foreach (var expiredRuleId in expiredRuleIds)
        {
            lockouts.Remove(expiredRuleId);
        }
    }

    private void ScheduleLockoutStateNotification(Guid ruleId, TimeSpan delay)
    {
        if (delay <= TimeSpan.Zero)
        {
            ReleaseActiveRuleLockoutState(ruleId, logRelease: true);
            return;
        }

        CancellationTokenSource? previousNotification = null;
        var notification = new CancellationTokenSource();

        lock (stateGate)
        {
            if (lockoutStateNotifications.TryGetValue(ruleId, out previousNotification))
            {
                lockoutStateNotifications.Remove(ruleId);
            }

            lockoutStateNotifications[ruleId] = notification;
        }

        previousNotification?.Cancel();
        previousNotification?.Dispose();

        _ = Task.Run(async () =>
        {
            try
            {
                await Task.Delay(delay, notification.Token);

                lock (stateGate)
                {
                    if (lockoutStateNotifications.TryGetValue(ruleId, out var currentNotification)
                        && ReferenceEquals(currentNotification, notification))
                    {
                        lockoutStateNotifications.Remove(ruleId);
                    }
                }

                ReleaseActiveRuleLockoutState(ruleId, logRelease: true);
            }
            catch (OperationCanceledException)
            {
            }
            finally
            {
                notification.Dispose();
            }
        }, CancellationToken.None);
    }

    private void ScheduleAvatarSwitchLockoutStateNotification(Guid ruleId, TimeSpan delay)
    {
        if (delay <= TimeSpan.Zero)
        {
            ReleaseActiveAvatarSwitchLockoutState(ruleId, logRelease: true);
            return;
        }

        CancellationTokenSource? previousNotification = null;
        var notification = new CancellationTokenSource();

        lock (stateGate)
        {
            if (avatarSwitchLockoutStateNotifications.TryGetValue(ruleId, out previousNotification))
            {
                avatarSwitchLockoutStateNotifications.Remove(ruleId);
            }

            avatarSwitchLockoutStateNotifications[ruleId] = notification;
        }

        previousNotification?.Cancel();
        previousNotification?.Dispose();

        _ = Task.Run(async () =>
        {
            try
            {
                await Task.Delay(delay, notification.Token);

                lock (stateGate)
                {
                    if (avatarSwitchLockoutStateNotifications.TryGetValue(ruleId, out var currentNotification)
                        && ReferenceEquals(currentNotification, notification))
                    {
                        avatarSwitchLockoutStateNotifications.Remove(ruleId);
                    }
                }

                ReleaseActiveAvatarSwitchLockoutState(ruleId, logRelease: true);
            }
            catch (OperationCanceledException)
            {
            }
            finally
            {
                notification.Dispose();
            }
        }, CancellationToken.None);
    }

    private async Task<TwitchAccountSnapshot> EnsureAccountReadyAsync(
        TwitchAccountSnapshot account,
        IEnumerable<string> requiredScopes,
        BridgeAccountRole accountRole,
        CancellationToken cancellationToken)
    {
        if (activeConfiguration is null)
        {
            throw new InvalidOperationException("Bridge settings are missing.");
        }

        var shouldRefresh = !string.IsNullOrWhiteSpace(account.RefreshToken)
            && account.AccessTokenExpiresAt is { } expiresAt
            && expiresAt <= DateTimeOffset.UtcNow.Add(AccessTokenRefreshLeadTime);

        if (shouldRefresh)
        {
            var refreshedToken = await twitchApiClient.RefreshAccessTokenAsync(activeConfiguration.TwitchClientId, account.RefreshToken, cancellationToken);
            account = account with
            {
                AccessToken = refreshedToken.AccessToken,
                RefreshToken = refreshedToken.RefreshToken,
                AccessTokenExpiresAt = DateTimeOffset.UtcNow.AddSeconds(refreshedToken.ExpiresIn),
                SessionRenewalDueAt = DateTimeOffset.UtcNow.Add(PublicRefreshSessionWindow),
                Scopes = refreshedToken.Scope
            };
        }

        var validation = await twitchApiClient.ValidateTokenAsync(account.AccessToken, cancellationToken);
        if (validation is null)
        {
            if (string.IsNullOrWhiteSpace(account.RefreshToken))
            {
                throw new InvalidOperationException($"{accountRole} OAuth token expired and no refresh token is available.");
            }

            var refreshedToken = await twitchApiClient.RefreshAccessTokenAsync(activeConfiguration.TwitchClientId, account.RefreshToken, cancellationToken);
            account = account with
            {
                AccessToken = refreshedToken.AccessToken,
                RefreshToken = refreshedToken.RefreshToken,
                AccessTokenExpiresAt = DateTimeOffset.UtcNow.AddSeconds(refreshedToken.ExpiresIn),
                SessionRenewalDueAt = DateTimeOffset.UtcNow.Add(PublicRefreshSessionWindow),
                Scopes = refreshedToken.Scope
            };

            validation = await twitchApiClient.ValidateTokenAsync(account.AccessToken, cancellationToken)
                ?? throw new InvalidOperationException($"Unable to validate the refreshed {accountRole} token.");
        }

        var missingScopes = requiredScopes
            .Where(scope => validation.Scopes.All(existing => !string.Equals(existing, scope, StringComparison.OrdinalIgnoreCase)))
            .ToArray();

        if (missingScopes.Length > 0)
        {
            throw new InvalidOperationException($"{accountRole} login is missing Twitch scopes: {string.Join(", ", missingScopes)}.");
        }

        var user = await twitchApiClient.GetUserAsync(
            account.AccessToken,
            activeConfiguration.TwitchClientId,
            validation.UserId,
            cancellationToken);

        var updatedAccount = account with
        {
            UserId = validation.UserId,
            Login = user?.Login ?? validation.Login,
            DisplayName = user?.DisplayName ?? account.DisplayName ?? validation.Login,
            ProfileImageUrl = user?.ProfileImageUrl ?? account.ProfileImageUrl,
            AccessTokenExpiresAt = DateTimeOffset.UtcNow.AddSeconds(validation.ExpiresIn),
            SessionRenewalDueAt = account.SessionRenewalDueAt ?? DateTimeOffset.UtcNow.Add(PublicRefreshSessionWindow),
            Scopes = validation.Scopes
        };

        AccountUpdated?.Invoke(accountRole, updatedAccount);
        return updatedAccount;
    }

    private async Task StopOscRouterSafelyAsync()
    {
        try
        {
            await oscRouterService.StopAsync();
        }
        catch (Exception ex)
        {
            WriteLog($"OSCQuery shutdown warning: {ex.Message}");
        }
    }

    private static void ValidateConfiguration(BridgeRuntimeConfiguration configuration)
    {
        if (string.IsNullOrWhiteSpace(configuration.TwitchClientId))
        {
            throw new InvalidOperationException("Crystal Relay could not load its built-in Twitch app ID.");
        }

        if (string.IsNullOrWhiteSpace(configuration.Broadcaster.AccessToken))
        {
            throw new InvalidOperationException("Connect your broadcaster Twitch account before starting the bridge.");
        }
    }

    private bool RememberMessage(string messageId)
    {
        var now = DateTimeOffset.UtcNow;
        lock (stateGate)
        {
            if (now >= nextRecentMessagePruneAt)
            {
                var expiresBefore = now - RecentMessageRetention;
                foreach (var expiredKey in recentMessageIds
                             .Where(pair => pair.Value < expiresBefore)
                             .Select(pair => pair.Key)
                             .ToArray())
                {
                    recentMessageIds.Remove(expiredKey);
                }

                nextRecentMessagePruneAt = now.Add(RecentMessagePruneInterval);
            }

            if (recentMessageIds.ContainsKey(messageId))
            {
                return false;
            }

            recentMessageIds[messageId] = now;
            return true;
        }
    }

    private TriggerRuleSnapshot[] SelectMatchingRules(
        RuntimeRuleIndex ruleIndex,
        BridgeIncomingEvent bridgeEvent,
        string currentAvatarId,
        bool avatarChangeTransitionActive,
        IReadOnlyCollection<Guid> temporarilyDisabledRuleIds)
    {
        return bridgeEvent.TriggerType switch
        {
            TwitchTriggerType.ChannelPoints => SelectExactChannelPointMatch(
                ruleIndex,
                bridgeEvent.RewardId,
                bridgeEvent.RewardTitle,
                bridgeEvent.RewardUserInput,
                currentAvatarId,
                temporarilyDisabledRuleIds,
                avatarChangeTransitionActive),
            TwitchTriggerType.Bits => SelectBitsMatchingRules(
                ruleIndex.GetGlobalOverrideRulesByTriggerType(bridgeEvent.TriggerType)
                    .Where(rule => rule.IsEnabled && !temporarilyDisabledRuleIds.Contains(rule.Id))
                    .ToArray(),
                bridgeEvent,
                currentAvatarId),
            TwitchTriggerType.Subscriptions => SelectSubscriptionMatchingRules(
                ruleIndex.GetGlobalOverrideRulesByTriggerType(bridgeEvent.TriggerType)
                    .Where(rule => rule.IsEnabled && !temporarilyDisabledRuleIds.Contains(rule.Id))
                    .ToArray(),
                bridgeEvent.Amount,
                currentAvatarId),
            _ => []
        };
    }

    private TriggerRuleSnapshot[] SelectBitsMatchingRules(
        IReadOnlyList<TriggerRuleSnapshot> rules,
        BridgeIncomingEvent bridgeEvent,
        string currentAvatarId)
    {
        var currentAvatarRules = rules
            .Where(rule => IsSupporterRuleScopedToCurrentAvatar(rule, currentAvatarId))
            .ToArray();
        var globalRules = rules
            .Where(IsGlobalSupporterRule)
            .ToArray();
        var currentAvatarOutfitRules = currentAvatarRules
            .Where(IsBitsOutfitSetTriggerRule)
            .Where(rule => bridgeEvent.Amount >= Math.Max(1, rule.MinimumAmount))
            .Where(rule => !string.IsNullOrWhiteSpace(rule.SharedRewardHelpText))
            .ToArray();
        var globalOutfitRules = globalRules
            .Where(IsBitsOutfitSetTriggerRule)
            .Where(rule => bridgeEvent.Amount >= Math.Max(1, rule.MinimumAmount))
            .Where(rule => !string.IsNullOrWhiteSpace(rule.SharedRewardHelpText))
            .ToArray();

        var choiceText = ExtractBitsOutfitChoiceText(bridgeEvent.MessageText);
        if (!string.IsNullOrWhiteSpace(choiceText))
        {
            var outfitRules = currentAvatarOutfitRules.Length > 0
                ? currentAvatarOutfitRules
                : globalOutfitRules;
            if (outfitRules.Length > 0)
            {
                var match = FindBitsOutfitNameMatch(outfitRules, choiceText);
                if (match.Rule is not null)
                {
                    WriteLog($"Matched Bits outfit '{match.Rule.Name}' from cheer text '{choiceText}' using {match.MatchKind} matching ({match.Score:P0}).");
                    return [match.Rule];
                }

                if (!string.IsNullOrWhiteSpace(match.Diagnostic))
                {
                    WriteLog(match.Diagnostic);
                }

                return [];
            }
        }

        var currentAvatarMatch = SelectBestThresholdMatch(
            currentAvatarRules
                .Where(rule => !IsBitsOutfitSetTriggerRule(rule))
                .ToArray(),
            bridgeEvent.Amount);
        if (currentAvatarMatch.Length > 0)
        {
            return currentAvatarMatch;
        }

        var avatarChangeOverrideMatch = SelectBestThresholdMatch(
            globalRules
                .Where(IsAvatarChangeOverrideRule)
                .ToArray(),
            bridgeEvent.Amount);
        if (avatarChangeOverrideMatch.Length > 0)
        {
            return avatarChangeOverrideMatch;
        }

        return SelectBestThresholdMatch(
            globalRules
                .Where(rule => !IsBitsOutfitSetTriggerRule(rule))
                .Where(rule => !IsAvatarChangeOverrideRule(rule))
                .ToArray(),
            bridgeEvent.Amount);
    }

    private static TriggerRuleSnapshot[] SelectSubscriptionMatchingRules(
        IReadOnlyList<TriggerRuleSnapshot> rules,
        int amount,
        string currentAvatarId)
    {
        var currentAvatarRules = rules
            .Where(rule => IsSupporterRuleScopedToCurrentAvatar(rule, currentAvatarId))
            .ToArray();
        var currentAvatarMatch = SelectBestThresholdMatch(currentAvatarRules, amount);
        if (currentAvatarMatch.Length > 0)
        {
            return currentAvatarMatch;
        }

        var globalRules = rules
            .Where(IsGlobalSupporterRule)
            .ToArray();
        var avatarChangeOverrideMatch = SelectBestThresholdMatch(
            globalRules
                .Where(IsAvatarChangeOverrideRule)
                .ToArray(),
            amount);
        if (avatarChangeOverrideMatch.Length > 0)
        {
            return avatarChangeOverrideMatch;
        }

        return SelectBestThresholdMatch(
            globalRules
                .Where(rule => !IsAvatarChangeOverrideRule(rule))
                .ToArray(),
            amount);
    }

    private static bool IsSupporterRuleScopedToCurrentAvatar(TriggerRuleSnapshot rule, string currentAvatarId)
    {
        if (IsAvatarChangeOverrideRule(rule))
        {
            return false;
        }

        var normalizedCurrentAvatarId = currentAvatarId?.Trim() ?? string.Empty;
        var scopedAvatarId = !string.IsNullOrWhiteSpace(rule.SupporterAvatarId)
            ? rule.SupporterAvatarId.Trim()
            : rule.RequiredAvatarId?.Trim() ?? string.Empty;
        return !string.IsNullOrWhiteSpace(scopedAvatarId)
            && !string.IsNullOrWhiteSpace(normalizedCurrentAvatarId)
            && string.Equals(scopedAvatarId, normalizedCurrentAvatarId, StringComparison.Ordinal);
    }

    private static bool IsGlobalSupporterRule(TriggerRuleSnapshot rule) =>
        IsAvatarChangeOverrideRule(rule);

    private static bool IsAvatarChangeOverrideRule(TriggerRuleSnapshot rule) =>
        rule.ActionType is OscActionType.AvatarChange or OscActionType.AvatarRoulet;

    private static TriggerRuleSnapshot[] SelectExactChannelPointMatch(
        RuntimeRuleIndex ruleIndex,
        string? rewardId,
        string? rewardTitle,
        string? rewardUserInput,
        string currentAvatarId,
        IReadOnlyCollection<Guid> temporarilyDisabledRuleIds,
        bool avatarChangeTransitionActive)
    {
        var normalizedRewardId = rewardId?.Trim() ?? string.Empty;
        var normalizedRewardTitle = NormalizeRewardTitle(rewardTitle);
        var normalizedCurrentAvatarId = currentAvatarId?.Trim() ?? string.Empty;
        if (string.IsNullOrWhiteSpace(normalizedRewardId) && string.IsNullOrWhiteSpace(normalizedRewardTitle))
        {
            return [];
        }

        var activeCandidates = GetActiveChannelPointCandidates(
            ruleIndex,
            normalizedRewardId,
            normalizedRewardTitle,
            normalizedCurrentAvatarId,
            temporarilyDisabledRuleIds,
            avatarChangeTransitionActive);

        if (activeCandidates.Length == 0)
        {
            return [];
        }

        var sharedChoiceCandidates = activeCandidates
            .Where(IsSharedRewardChoiceRule)
            .ToArray();
        if (sharedChoiceCandidates.Length > 0)
        {
            if (!TryParseSharedRewardChoiceNumber(rewardUserInput, out var choiceNumber))
            {
                return [];
            }

            var selectedChoice = sharedChoiceCandidates
                .FirstOrDefault(rule => rule.SharedRewardChoiceNumber == choiceNumber);
            return selectedChoice is null ? [] : [selectedChoice];
        }

        return [activeCandidates[0]];
    }

    private static TriggerRuleSnapshot[] SelectMatchingChatCommandRules(
        RuntimeRuleIndex ruleIndex,
        BridgeIncomingEvent bridgeEvent,
        string currentAvatarId,
        bool avatarChangeTransitionActive,
        IReadOnlyCollection<Guid> temporarilyDisabledRuleIds)
    {
        var normalizedCurrentAvatarId = currentAvatarId?.Trim() ?? string.Empty;
        if (!bridgeEvent.IsChatCommandTrigger || string.IsNullOrWhiteSpace(bridgeEvent.ChatCommandText))
        {
            return [];
        }

        foreach (var rule in ruleIndex.GetChatCommandCandidates(bridgeEvent.ChatCommandText))
        {
            if (!rule.IsEnabled
                || temporarilyDisabledRuleIds.Contains(rule.Id)
                || !UserCanTriggerChatCommand(rule.ChatCommandPermission, bridgeEvent))
            {
                continue;
            }

            if (!AvatarRuleActivationPolicy.IsRuleActiveForCurrentAvatar(
                    rule.IsGlobalOverride,
                    rule.BelongsToMasterAvatarProfile,
                    rule.ActionType,
                    rule.AvatarChangeTargetId,
                    rule.RequiredAvatarId,
                    normalizedCurrentAvatarId,
                    avatarChangeTransitionActive))
            {
                continue;
            }

            return [rule];
        }

        return [];
    }

    private static TriggerRuleSnapshot[] SelectBestThresholdMatch(
        IReadOnlyList<TriggerRuleSnapshot> rules,
        int amount)
    {
        TriggerRuleSnapshot? bestMatch = null;
        var bestMinimumAmount = int.MinValue;

        foreach (var rule in rules)
        {
            var minimumAmount = Math.Max(1, rule.MinimumAmount);
            if (amount < minimumAmount)
            {
                continue;
            }

            if (minimumAmount > bestMinimumAmount)
            {
                bestMinimumAmount = minimumAmount;
                bestMatch = rule;
            }
        }

        return bestMatch is null ? [] : [bestMatch];
    }

    private static BitsOutfitNameMatch FindBitsOutfitNameMatch(
        IReadOnlyList<TriggerRuleSnapshot> rules,
        string choiceText)
    {
        var normalizedChoice = NormalizeBitsOutfitPhrase(choiceText);
        var compactChoice = NormalizeBitsOutfitCompact(choiceText);
        if (string.IsNullOrWhiteSpace(compactChoice))
        {
            return new BitsOutfitNameMatch(null, string.Empty, 0, null);
        }

        var candidates = rules
            .Select(rule => new BitsOutfitNameCandidate(
                rule,
                rule.SharedRewardHelpText.Trim(),
                NormalizeBitsOutfitPhrase(rule.SharedRewardHelpText),
                NormalizeBitsOutfitCompact(rule.SharedRewardHelpText)))
            .Where(candidate => !string.IsNullOrWhiteSpace(candidate.CompactName))
            .ToArray();

        var exactMatches = candidates
            .Where(candidate => string.Equals(candidate.NormalizedName, normalizedChoice, StringComparison.Ordinal))
            .ToArray();
        if (exactMatches.Length == 1)
        {
            return new BitsOutfitNameMatch(exactMatches[0].Rule, "exact", 1, null);
        }

        if (exactMatches.Length > 1)
        {
            return new BitsOutfitNameMatch(
                null,
                "exact",
                0,
                $"Bits outfit cheer text '{choiceText}' matched more than one outfit exactly. Rename one of these outfits so Crystal Relay can choose safely: {DescribeBitsOutfitCandidates(exactMatches)}.");
        }

        var compactMatches = candidates
            .Where(candidate => string.Equals(candidate.CompactName, compactChoice, StringComparison.Ordinal))
            .ToArray();
        if (compactMatches.Length == 1)
        {
            return new BitsOutfitNameMatch(compactMatches[0].Rule, "compact", 1, null);
        }

        if (compactMatches.Length > 1)
        {
            return new BitsOutfitNameMatch(
                null,
                "compact",
                0,
                $"Bits outfit cheer text '{choiceText}' matched more than one outfit after removing spaces and punctuation. Rename one of these outfits so Crystal Relay can choose safely: {DescribeBitsOutfitCandidates(compactMatches)}.");
        }

        var fuzzyMatches = candidates
            .Select(candidate =>
            {
                var distance = CalculateDamerauLevenshteinDistance(compactChoice, candidate.CompactName);
                var length = Math.Max(compactChoice.Length, candidate.CompactName.Length);
                var score = length <= 0 ? 0 : 1d - (distance / (double)length);
                return new BitsOutfitFuzzyCandidate(candidate, distance, score, GetMaximumBitsOutfitFuzzyDistance(length));
            })
            .Where(candidate => candidate.Distance <= candidate.MaximumDistance)
            .OrderBy(candidate => candidate.Distance)
            .ThenByDescending(candidate => candidate.Score)
            .ToArray();

        if (fuzzyMatches.Length == 0)
        {
            return new BitsOutfitNameMatch(
                null,
                "fuzzy",
                0,
                $"Bits outfit cheer text '{choiceText}' did not confidently match any configured outfit name.");
        }

        var best = fuzzyMatches[0];
        var second = fuzzyMatches.Length > 1 ? fuzzyMatches[1] : null;
        if (second is not null
            && (second.Distance - best.Distance <= 1 || best.Score - second.Score < 0.12d))
        {
            return new BitsOutfitNameMatch(
                null,
                "fuzzy",
                best.Score,
                $"Bits outfit cheer text '{choiceText}' was too close to multiple outfits. Crystal Relay did not guess. Close matches: {DescribeBitsOutfitCandidates([best.Candidate, second.Candidate])}.");
        }

        return new BitsOutfitNameMatch(best.Candidate.Rule, "fuzzy", best.Score, null);
    }

    private static string ExtractBitsOutfitChoiceText(string messageText)
    {
        var trimmed = messageText?.Trim() ?? string.Empty;
        if (string.IsNullOrWhiteSpace(trimmed))
        {
            return string.Empty;
        }

        var tokens = trimmed
            .Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
        if (tokens.Length == 0)
        {
            return string.Empty;
        }

        var choiceTokens = new List<string>();
        var strippingLeadingCheerTokens = true;
        for (var tokenIndex = 0; tokenIndex < tokens.Length; tokenIndex++)
        {
            var token = tokens[tokenIndex];
            if (strippingLeadingCheerTokens
                && TryStripLeadingCheerToken(token, allowGenericCheerPrefix: tokens.Length > 1, out var remainder))
            {
                if (!string.IsNullOrWhiteSpace(remainder))
                {
                    choiceTokens.Add(remainder);
                    strippingLeadingCheerTokens = false;
                }

                continue;
            }

            strippingLeadingCheerTokens = false;
            choiceTokens.Add(token);
        }

        return string.Join(' ', choiceTokens).Trim();
    }

    private static bool TryStripLeadingCheerToken(string token, bool allowGenericCheerPrefix, out string remainder)
    {
        remainder = string.Empty;
        var trimmed = token.Trim();
        if (trimmed.Length < 2 || !char.IsLetter(trimmed[0]))
        {
            return false;
        }

        var index = 0;
        while (index < trimmed.Length && char.IsLetter(trimmed[index]))
        {
            index++;
        }

        var prefix = trimmed[..index];
        var digitStart = index;
        while (index < trimmed.Length && char.IsDigit(trimmed[index]))
        {
            index++;
        }

        if (digitStart == 0 || digitStart == index)
        {
            return false;
        }

        if (!allowGenericCheerPrefix
            && !string.Equals(prefix, "cheer", StringComparison.OrdinalIgnoreCase))
        {
            return false;
        }

        remainder = trimmed[index..].Trim();
        return true;
    }

    private static string NormalizeBitsOutfitPhrase(string value)
    {
        var words = (value ?? string.Empty)
            .Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
        return string.Join(' ', words).ToLowerInvariant();
    }

    private static string NormalizeBitsOutfitCompact(string value)
    {
        var builder = new StringBuilder();
        foreach (var character in (value ?? string.Empty).Normalize(NormalizationForm.FormD))
        {
            if (CharUnicodeInfo.GetUnicodeCategory(character) == UnicodeCategory.NonSpacingMark)
            {
                continue;
            }

            if (char.IsLetterOrDigit(character))
            {
                builder.Append(char.ToLowerInvariant(character));
            }
        }

        return builder.ToString();
    }

    private static int GetMaximumBitsOutfitFuzzyDistance(int length)
    {
        if (length <= 3)
        {
            return 0;
        }

        if (length <= 5)
        {
            return 1;
        }

        if (length <= 8)
        {
            return 2;
        }

        return Math.Min(5, (int)Math.Ceiling(length * 0.25d));
    }

    private static int CalculateDamerauLevenshteinDistance(string source, string target)
    {
        if (string.Equals(source, target, StringComparison.Ordinal))
        {
            return 0;
        }

        if (source.Length == 0)
        {
            return target.Length;
        }

        if (target.Length == 0)
        {
            return source.Length;
        }

        var distances = new int[source.Length + 1, target.Length + 1];
        for (var i = 0; i <= source.Length; i++)
        {
            distances[i, 0] = i;
        }

        for (var j = 0; j <= target.Length; j++)
        {
            distances[0, j] = j;
        }

        for (var i = 1; i <= source.Length; i++)
        {
            for (var j = 1; j <= target.Length; j++)
            {
                var cost = source[i - 1] == target[j - 1] ? 0 : 1;
                var distance = Math.Min(
                    Math.Min(distances[i - 1, j] + 1, distances[i, j - 1] + 1),
                    distances[i - 1, j - 1] + cost);

                if (i > 1
                    && j > 1
                    && source[i - 1] == target[j - 2]
                    && source[i - 2] == target[j - 1])
                {
                    distance = Math.Min(distance, distances[i - 2, j - 2] + 1);
                }

                distances[i, j] = distance;
            }
        }

        return distances[source.Length, target.Length];
    }

    private static string DescribeBitsOutfitCandidates(IEnumerable<BitsOutfitNameCandidate> candidates)
    {
        var names = candidates
            .Select(candidate => string.IsNullOrWhiteSpace(candidate.DisplayName) ? candidate.Rule.Name : candidate.DisplayName)
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .Take(4)
            .ToArray();
        return names.Length == 0 ? "none" : string.Join(", ", names);
    }

    private static string NormalizeRewardTitle(string? rewardTitle) => rewardTitle?.Trim() ?? string.Empty;

    private static TriggerRuleSnapshot[] GetActiveChannelPointCandidates(
        RuntimeRuleIndex ruleIndex,
        string normalizedRewardId,
        string normalizedRewardTitle,
        string normalizedCurrentAvatarId,
        IReadOnlyCollection<Guid> temporarilyDisabledRuleIds,
        bool avatarChangeTransitionActive)
    {
        var activeCandidates = new List<TriggerRuleSnapshot>();
        foreach (var rule in ruleIndex.GetChannelPointCandidates(normalizedRewardId, normalizedRewardTitle))
        {
            if (!rule.IsEnabled || temporarilyDisabledRuleIds.Contains(rule.Id))
            {
                continue;
            }

            if (!AvatarRuleActivationPolicy.IsRuleActiveForCurrentAvatar(
                    rule.IsGlobalOverride,
                    rule.BelongsToMasterAvatarProfile,
                    rule.ActionType,
                    rule.AvatarChangeTargetId,
                    rule.RequiredAvatarId,
                    normalizedCurrentAvatarId,
                    avatarChangeTransitionActive))
            {
                continue;
            }

            activeCandidates.Add(rule);
        }

        return [.. activeCandidates];
    }

    private static bool IsSharedRewardChoiceRule(TriggerRuleSnapshot rule) =>
        !rule.IsGlobalOverride
        && rule.TriggerType == TwitchTriggerType.ChannelPoints
        && rule.SharedRewardChoiceEnabled
        && rule.SharedRewardChoiceNumber > 0;

    private static bool TryParseSharedRewardChoiceNumber(string? userInput, out int choiceNumber)
    {
        choiceNumber = 0;
        var normalizedInput = userInput?.Trim() ?? string.Empty;
        if (string.IsNullOrWhiteSpace(normalizedInput))
        {
            return false;
        }

        if (normalizedInput.StartsWith('#'))
        {
            normalizedInput = normalizedInput[1..].TrimStart();
        }

        var firstToken = normalizedInput
            .Split([' ', '\t', '\r', '\n'], StringSplitOptions.RemoveEmptyEntries)
            .FirstOrDefault();
        return int.TryParse(firstToken, NumberStyles.Integer, CultureInfo.InvariantCulture, out choiceNumber)
            && choiceNumber > 0;
    }

    private static bool TryBuildSharedRewardChoiceHelp(
        RuntimeRuleIndex ruleIndex,
        BridgeIncomingEvent bridgeEvent,
        string currentAvatarId,
        bool avatarChangeTransitionActive,
        IReadOnlyCollection<Guid> temporarilyDisabledRuleIds,
        out string helpMessage,
        out string logMessage)
    {
        helpMessage = string.Empty;
        logMessage = string.Empty;

        var rewardId = bridgeEvent.RewardId?.Trim() ?? string.Empty;
        var rewardTitle = NormalizeRewardTitle(bridgeEvent.RewardTitle);
        var normalizedCurrentAvatarId = currentAvatarId?.Trim() ?? string.Empty;
        var sharedChoiceCandidates = GetActiveChannelPointCandidates(
                ruleIndex,
                rewardId,
                rewardTitle,
                normalizedCurrentAvatarId,
                temporarilyDisabledRuleIds,
                avatarChangeTransitionActive)
            .Where(IsSharedRewardChoiceRule)
            .OrderBy(rule => rule.SharedRewardChoiceNumber)
            .ThenBy(rule => rule.Name, StringComparer.CurrentCultureIgnoreCase)
            .ToArray();

        if (sharedChoiceCandidates.Length == 0)
        {
            return false;
        }

        var rewardLabel = !string.IsNullOrWhiteSpace(bridgeEvent.RewardTitle)
            ? ManagedRewardPresentation.StripPrefix(bridgeEvent.RewardTitle)
            : sharedChoiceCandidates[0].ChannelPointRewardTitle;
        if (string.IsNullOrWhiteSpace(rewardLabel))
        {
            rewardLabel = "this redeem";
        }

        var choicesText = BuildSharedRewardChoiceOptionsText(sharedChoiceCandidates);
        if (TryParseSharedRewardChoiceNumber(bridgeEvent.RewardUserInput, out var choiceNumber))
        {
            helpMessage = SanitizeBotMessage(TF("Choice {0} is not available for '{1}'. {2}", choiceNumber, rewardLabel, choicesText));
            logMessage = $"'{rewardLabel}' received unavailable shared reward choice #{choiceNumber}; no OSC action was sent.";
            return true;
        }

        helpMessage = SanitizeBotMessage(TF("Type a number for '{0}'. {1}", rewardLabel, choicesText));
        logMessage = $"'{rewardLabel}' was redeemed without a valid shared reward choice number; no OSC action was sent.";
        return true;
    }

    private static string BuildSharedRewardChoiceOptionsText(IReadOnlyList<TriggerRuleSnapshot> rules)
    {
        var prefix = T("Options") + ": ";
        var selectedOptions = new List<string>();
        var omittedCount = 0;

        for (var index = 0; index < rules.Count; index++)
        {
            var option = DescribeSharedRewardChoiceOption(rules[index]);
            if (string.IsNullOrWhiteSpace(option))
            {
                continue;
            }

            var remainingAfterCandidate = rules.Count - index - 1;
            var candidateOptions = new List<string>(selectedOptions) { option };
            var candidate = prefix + string.Join(" | ", candidateOptions);
            if (remainingAfterCandidate > 0)
            {
                candidate += $" | {TF("and {0} more", remainingAfterCandidate)}";
            }

            if (SanitizeBotMessage(candidate, truncate: false).Length <= TwitchChatMessageMaxCharacters)
            {
                selectedOptions.Add(option);
                omittedCount = remainingAfterCandidate;
                continue;
            }

            omittedCount = rules.Count - selectedOptions.Count;
            break;
        }

        if (selectedOptions.Count == 0)
        {
            return prefix + TF("and {0} more", rules.Count);
        }

        return prefix + string.Join(" | ", selectedOptions)
            + (omittedCount > 0 ? $" | {TF("and {0} more", omittedCount)}" : string.Empty);
    }

    private static string DescribeSharedRewardChoiceOption(TriggerRuleSnapshot rule)
    {
        var label = !string.IsNullOrWhiteSpace(rule.SharedRewardHelpText)
            ? rule.SharedRewardHelpText.Trim()
            : !string.IsNullOrWhiteSpace(rule.Name)
                ? rule.Name.Trim()
                : T("Avatar parameter");

        if (rule.ActionType == OscActionType.AvatarParameter
            && !string.IsNullOrWhiteSpace(rule.ParameterName))
        {
            label = $"{label} ({GetAvatarParameterDisplayName(rule.ParameterName)} -> {rule.ParameterValue})";
        }
        else if (rule.ActionType == OscActionType.SetTrigger
                 && string.IsNullOrWhiteSpace(rule.SharedRewardHelpText))
        {
            label = TF("Set Trigger ({0} params)", rule.SetTriggerActions.Count);
        }

        return TF("{0} = {1}", rule.SharedRewardChoiceNumber, label);
    }

    private static string GetAvatarParameterDisplayName(string address)
    {
        var normalizedAddress = address?.Trim() ?? string.Empty;
        return normalizedAddress.Split('/', StringSplitOptions.RemoveEmptyEntries).LastOrDefault() ?? normalizedAddress;
    }

    private static bool UserCanTriggerChatCommand(ChatCommandPermission permission, BridgeIncomingEvent bridgeEvent) => permission switch
    {
        ChatCommandPermission.Broadcaster => bridgeEvent.UserIsBroadcaster,
        ChatCommandPermission.Moderators => bridgeEvent.UserIsModerator || bridgeEvent.UserIsBroadcaster,
        _ => true
    };

    private static bool UserCanTriggerChatCommand(ChatCommandPermission permission, UniversalIncomingEvent incomingEvent) => permission switch
    {
        ChatCommandPermission.Broadcaster => incomingEvent.UserIsBroadcaster,
        ChatCommandPermission.Moderators => incomingEvent.UserIsModerator || incomingEvent.UserIsBroadcaster,
        _ => true
    };

    private string GetCurrentVrChatAvatarId()
    {
        lock (stateGate)
        {
            return currentVrChatAvatarId;
        }
    }

    private SharedReturnAvatarSnapshot GetSharedReturnAvatarSnapshot()
    {
        lock (stateGate)
        {
            return new SharedReturnAvatarSnapshot(currentSharedReturnAvatarId, currentSharedReturnAvatarName);
        }
    }

    private void SetCurrentVrChatAvatar(
        string avatarId,
        bool notify,
        AvatarScaleAvatarChangeCarryoverMode scaleCarryoverMode = AvatarScaleAvatarChangeCarryoverMode.Auto)
    {
        var normalizedAvatarId = avatarId?.Trim() ?? string.Empty;
        var previousAvatarId = string.Empty;
        double? previousAvatarHeight = null;
        var changed = false;

        lock (stateGate)
        {
            if (string.Equals(currentVrChatAvatarId, normalizedAvatarId, StringComparison.Ordinal))
            {
                return;
            }

            previousAvatarId = currentVrChatAvatarId;
            if (TryGetObservedFloatLocked("/avatar/eyeheight", out var observedHeight))
            {
                previousAvatarHeight = observedHeight;
            }

            currentVrChatAvatarId = normalizedAvatarId;
            avatarParameterValues.Clear();
            avatarScaleValues.Clear();
            localInstantToggleStates.Clear();
            changed = true;
        }

        if (changed && !string.IsNullOrWhiteSpace(normalizedAvatarId))
        {
            ResumePendingAvatarScopedResetsForCurrentAvatar(normalizedAvatarId);
            QueueAvatarScaleAvatarChangeHandling(
                previousAvatarId,
                normalizedAvatarId,
                previousAvatarHeight,
                scaleCarryoverMode);
        }

        if (notify && changed && !string.IsNullOrWhiteSpace(normalizedAvatarId))
        {
            VrChatAvatarChanged?.Invoke(normalizedAvatarId);
        }
    }

    private void QueueAvatarScaleAvatarChangeHandling(
        string previousAvatarId,
        string newAvatarId,
        double? previousAvatarHeight,
        AvatarScaleAvatarChangeCarryoverMode scaleCarryoverMode)
    {
        var cancellationToken = runtimeCancellation?.Token ?? CancellationToken.None;
        _ = Task.Run(
            () => HandleAvatarScaleAvatarChangedAsync(
                previousAvatarId,
                newAvatarId,
                previousAvatarHeight,
                scaleCarryoverMode,
                cancellationToken),
            CancellationToken.None);
    }

    private async Task HandleAvatarScaleAvatarChangedAsync(
        string previousAvatarId,
        string newAvatarId,
        double? previousAvatarHeight,
        AvatarScaleAvatarChangeCarryoverMode scaleCarryoverMode,
        CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(newAvatarId))
        {
            return;
        }

        try
        {
            var carryover = TryCreateAvatarScaleCarryoverSnapshot(
                previousAvatarId,
                previousAvatarHeight,
                scaleCarryoverMode);
            if (carryover is not null)
            {
                RecordPendingAvatarScaleHeightRestore(
                    newAvatarId,
                    carryover.FallbackRestoreHeightMeters,
                    carryover.ActiveUntil,
                    carryover.SourceRuleName);

                RetargetAvatarScaleRestoreSequenceForAvatarChange(newAvatarId);
                await SendAvatarHeightValueAsync(carryover.CarriedHeightMeters, cancellationToken);
                WriteLog($"Carried active avatar scale height {carryover.CarriedHeightMeters:0.###}m onto the new avatar after avatar change.");

                await Task.Delay(TimeSpan.FromMilliseconds(1200), cancellationToken);
                if (string.Equals(GetCurrentVrChatAvatarId(), newAvatarId, StringComparison.Ordinal)
                    && IsAvatarScaleHeightSessionStillActive(carryover.SourceRuleId, carryover.SessionId))
                {
                    await SendAvatarHeightValueAsync(carryover.CarriedHeightMeters, cancellationToken);
                    WriteLog($"Re-applied active avatar scale height {carryover.CarriedHeightMeters:0.###}m after the avatar swap settled.");
                }
                return;
            }

            await RestorePendingAvatarScaleHeightForCurrentAvatarAsync(newAvatarId, cancellationToken);
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
        }
        catch (Exception ex)
        {
            WriteLog($"Crystal Relay could not carry or restore avatar scale height after avatar change: {ex.Message}");
        }
    }

    private AvatarScaleCarryoverSnapshot? TryCreateAvatarScaleCarryoverSnapshot(
        string previousAvatarId,
        double? previousAvatarHeight,
        AvatarScaleAvatarChangeCarryoverMode scaleCarryoverMode)
    {
        if (scaleCarryoverMode == AvatarScaleAvatarChangeCarryoverMode.Skip)
        {
            return null;
        }

        var now = DateTimeOffset.UtcNow;
        lock (stateGate)
        {
            PruneExpiredAvatarScaleHeightSessionsLocked(now);
            ActiveAvatarScaleHeightSessionState? latestSession = null;
            foreach (var session in activeAvatarScaleHeightSessions.Values)
            {
                if (session.ActiveUntil <= now)
                {
                    continue;
                }

                if (latestSession is null || session.ActiveUntil > latestSession.ActiveUntil)
                {
                    latestSession = session;
                }
            }

            if (latestSession is null)
            {
                return null;
            }

            var normalizedPreviousAvatarId = previousAvatarId?.Trim() ?? string.Empty;
            if (!string.IsNullOrWhiteSpace(normalizedPreviousAvatarId)
                && !pendingAvatarScaleHeightRestores.ContainsKey(normalizedPreviousAvatarId))
            {
                var previousRestoreHeight = string.Equals(
                    normalizedPreviousAvatarId,
                    latestSession.OriginalAvatarId,
                    StringComparison.Ordinal)
                    ? latestSession.RestoreHeightMeters
                    : null;

                if (previousRestoreHeight is not null)
                {
                    pendingAvatarScaleHeightRestores[normalizedPreviousAvatarId] = new PendingAvatarScaleHeightRestoreState(
                        previousRestoreHeight.Value,
                        latestSession.ActiveUntil,
                        latestSession.RuleName);
                }
            }

            return new AvatarScaleCarryoverSnapshot(
                latestSession.RuleId,
                latestSession.SessionId,
                latestSession.RuleName,
                latestSession.CarriedHeightMeters,
                latestSession.RestoreHeightMeters ?? 1.6,
                latestSession.ActiveUntil);
        }
    }

    private void RetargetAvatarScaleRestoreSequenceForAvatarChange(string avatarId)
    {
        var normalizedAvatarId = avatarId?.Trim() ?? string.Empty;
        if (string.IsNullOrWhiteSpace(normalizedAvatarId))
        {
            return;
        }

        ActiveAvatarScaleRestoreSequenceState? retargetedSequence = null;
        lock (stateGate)
        {
            var now = DateTimeOffset.UtcNow;
            if (activeAvatarScaleRestoreSequence is null
                || activeAvatarScaleRestoreSequence.ActiveUntil <= now
                || string.Equals(activeAvatarScaleRestoreSequence.AvatarId, normalizedAvatarId, StringComparison.Ordinal))
            {
                return;
            }

            activeAvatarScaleRestoreSequence = activeAvatarScaleRestoreSequence with
            {
                AvatarId = normalizedAvatarId
            };
            retargetedSequence = activeAvatarScaleRestoreSequence;
        }

        if (retargetedSequence is not null)
        {
            WriteLog($"Avatar scale restore from '{retargetedSequence.SourceRuleName}' will now restore the current avatar when the active timer ends.");
        }
    }

    private Guid StartAvatarScaleHeightSession(
        Guid ruleId,
        string ruleName,
        double? restoreHeightMeters,
        double carriedHeightMeters,
        double activeWindowSeconds)
    {
        if (activeWindowSeconds <= 0)
        {
            return Guid.Empty;
        }

        var sessionId = Guid.NewGuid();
        var avatarId = GetCurrentVrChatAvatarId();
        var activeUntil = DateTimeOffset.UtcNow.AddSeconds(Math.Max(0.5, activeWindowSeconds));
        lock (stateGate)
        {
            activeAvatarScaleHeightSessions[ruleId] = new ActiveAvatarScaleHeightSessionState(
                ruleId,
                sessionId,
                string.IsNullOrWhiteSpace(ruleName) ? "Avatar Scale" : ruleName,
                avatarId,
                restoreHeightMeters,
                carriedHeightMeters,
                activeUntil);
        }

        return sessionId;
    }

    private void EndAvatarScaleHeightSession(Guid ruleId, Guid sessionId)
    {
        if (sessionId == Guid.Empty)
        {
            return;
        }

        lock (stateGate)
        {
            if (activeAvatarScaleHeightSessions.TryGetValue(ruleId, out var session)
                && session.SessionId == sessionId)
            {
                activeAvatarScaleHeightSessions.Remove(ruleId);
            }
        }
    }

    private void ScheduleAvatarScaleHeightSessionEnd(
        Guid ruleId,
        Guid sessionId,
        TimeSpan delay,
        CancellationToken cancellationToken)
    {
        if (sessionId == Guid.Empty)
        {
            return;
        }

        var token = runtimeCancellation?.Token ?? cancellationToken;
        _ = Task.Run(async () =>
        {
            try
            {
                await Task.Delay(delay <= TimeSpan.Zero ? TimeSpan.FromMilliseconds(500) : delay, token);
                EndAvatarScaleHeightSession(ruleId, sessionId);
            }
            catch (OperationCanceledException) when (token.IsCancellationRequested)
            {
            }
        }, CancellationToken.None);
    }

    private bool IsAvatarScaleHeightSessionStillActive(Guid ruleId, Guid sessionId)
    {
        if (sessionId == Guid.Empty)
        {
            return false;
        }

        var now = DateTimeOffset.UtcNow;
        lock (stateGate)
        {
            PruneExpiredAvatarScaleHeightSessionsLocked(now);
            return activeAvatarScaleHeightSessions.TryGetValue(ruleId, out var session)
                && session.SessionId == sessionId
                && session.ActiveUntil > now;
        }
    }

    private void PruneExpiredAvatarScaleHeightSessionsLocked(DateTimeOffset now)
    {
        foreach (var ruleId in activeAvatarScaleHeightSessions.Keys.ToArray())
        {
            if (activeAvatarScaleHeightSessions[ruleId].ActiveUntil <= now)
            {
                activeAvatarScaleHeightSessions.Remove(ruleId);
            }
        }
    }

    private double ResolveAvatarScaleRestoreHeightForCurrentAvatar(double fallbackHeight)
    {
        var currentAvatarId = GetCurrentVrChatAvatarId();
        lock (stateGate)
        {
            if (!string.IsNullOrWhiteSpace(currentAvatarId)
                && pendingAvatarScaleHeightRestores.TryGetValue(currentAvatarId, out var pendingRestore))
            {
                return pendingRestore.RestoreHeightMeters;
            }
        }

        return fallbackHeight;
    }

    private void RecordPendingAvatarScaleHeightRestore(
        string avatarId,
        double restoreHeightMeters,
        DateTimeOffset activeUntil,
        string sourceRuleName)
    {
        var normalizedAvatarId = avatarId?.Trim() ?? string.Empty;
        if (string.IsNullOrWhiteSpace(normalizedAvatarId))
        {
            return;
        }

        lock (stateGate)
        {
            pendingAvatarScaleHeightRestores.TryAdd(
                normalizedAvatarId,
                new PendingAvatarScaleHeightRestoreState(restoreHeightMeters, activeUntil, sourceRuleName));
        }
    }

    private void ClearPendingAvatarScaleHeightRestoreForCurrentAvatar()
    {
        var currentAvatarId = GetCurrentVrChatAvatarId();
        if (string.IsNullOrWhiteSpace(currentAvatarId))
        {
            return;
        }

        lock (stateGate)
        {
            pendingAvatarScaleHeightRestores.Remove(currentAvatarId);
        }
    }

    private async Task RestorePendingAvatarScaleHeightForCurrentAvatarAsync(
        string avatarId,
        CancellationToken cancellationToken)
    {
        PendingAvatarScaleHeightRestoreState? pendingRestore;
        var normalizedAvatarId = avatarId?.Trim() ?? string.Empty;
        if (string.IsNullOrWhiteSpace(normalizedAvatarId))
        {
            return;
        }

        lock (stateGate)
        {
            PruneExpiredAvatarScaleHeightSessionsLocked(DateTimeOffset.UtcNow);
            if (activeAvatarScaleHeightSessions.Count > 0
                || !pendingAvatarScaleHeightRestores.TryGetValue(normalizedAvatarId, out pendingRestore))
            {
                return;
            }
        }

        var operation = TryBeginAvatarScaleOperation(
            Guid.Empty,
            $"Pending restore from {pendingRestore.SourceRuleName}",
            AvatarScaleOperationPriority.IdleRestore,
            isTest: false);
        if (operation is null)
        {
            WriteLog($"Deferred pending avatar scale restore from '{pendingRestore.SourceRuleName}' because another scale effect is active.");
            return;
        }

        var restored = false;
        try
        {
            restored = await SendAvatarHeightForOperationAsync(
                operation,
                pendingRestore.RestoreHeightMeters,
                0,
                cancellationToken);
        }
        finally
        {
            EndAvatarScaleOperation(operation);
        }

        if (!restored)
        {
            return;
        }

        lock (stateGate)
        {
            if (pendingAvatarScaleHeightRestores.TryGetValue(normalizedAvatarId, out var current)
                && current == pendingRestore)
            {
                pendingAvatarScaleHeightRestores.Remove(normalizedAvatarId);
            }
        }

        WriteLog($"Restored previous avatar scale height to {pendingRestore.RestoreHeightMeters:0.###}m after returning to an avatar affected by '{pendingRestore.SourceRuleName}'.");
    }

    private void SetSharedReturnAvatar(string avatarId, string avatarName, bool notify)
    {
        var normalizedAvatarId = avatarId?.Trim() ?? string.Empty;
        var normalizedAvatarName = avatarName?.Trim() ?? string.Empty;
        var changed = false;

        lock (stateGate)
        {
            if (string.Equals(currentSharedReturnAvatarId, normalizedAvatarId, StringComparison.Ordinal)
                && string.Equals(currentSharedReturnAvatarName, normalizedAvatarName, StringComparison.Ordinal))
            {
                return;
            }

            currentSharedReturnAvatarId = normalizedAvatarId;
            currentSharedReturnAvatarName = normalizedAvatarName;
            changed = true;
        }

        if (notify && changed && !string.IsNullOrWhiteSpace(normalizedAvatarId))
        {
            SharedReturnAvatarChanged?.Invoke(normalizedAvatarId, normalizedAvatarName);
        }
    }

    private static BridgeIncomingEvent? ParseEvent(EventSubNotification notification)
    {
        var eventData = notification.EventData;
        return notification.SubscriptionType switch
        {
            "channel.channel_points_custom_reward_redemption.add" => new BridgeIncomingEvent(
                TwitchTriggerType.ChannelPoints,
                GetString(eventData, "user_name") ?? "Viewer",
                GetInt(eventData, "reward", "cost"),
                GetString(eventData, "reward", "id"),
                GetString(eventData, "reward", "title"),
                "Channel Points",
                false,
                string.Empty,
                string.Empty,
                string.Empty,
                [],
                false,
                false)
            {
                RewardUserInput = GetString(eventData, "user_input") ?? string.Empty
            },

            "channel.cheer" => new BridgeIncomingEvent(
                TwitchTriggerType.Bits,
                GetBoolean(eventData, "is_anonymous") ? "Anonymous" : GetString(eventData, "user_name") ?? "Viewer",
                GetInt(eventData, "bits"),
                null,
                null,
                "Bits",
                false,
                string.Empty,
                string.Empty,
                string.Empty,
                [],
                false,
                false)
            {
                MessageText = GetString(eventData, "message") ?? ExtractChatMessageText(eventData)
            },

            "channel.subscribe" when !GetBoolean(eventData, "is_gift") => new BridgeIncomingEvent(
                TwitchTriggerType.Subscriptions,
                GetString(eventData, "user_name") ?? "Subscriber",
                1,
                null,
                null,
                "Subscription",
                false,
                string.Empty,
                string.Empty,
                string.Empty,
                [],
                false,
                false),

            "channel.subscription.message" => new BridgeIncomingEvent(
                TwitchTriggerType.Subscriptions,
                GetString(eventData, "user_name") ?? "Subscriber",
                1,
                null,
                null,
                "Subscription",
                false,
                string.Empty,
                string.Empty,
                string.Empty,
                [],
                false,
                false),

            "channel.subscription.gift" => new BridgeIncomingEvent(
                TwitchTriggerType.Subscriptions,
                GetBoolean(eventData, "is_anonymous") ? "Anonymous" : GetString(eventData, "user_name") ?? "Gifter",
                Math.Max(1, GetInt(eventData, "total")),
                null,
                null,
                "Gift Sub",
                false,
                string.Empty,
                string.Empty,
                string.Empty,
                [],
                false,
                false),

            _ => null
        };
    }

    private UniversalIncomingEvent? ParseUniversalEvent(
        EventSubNotification notification,
        BridgeIncomingEvent? chatCommandEvent)
    {
        var eventData = notification.EventData;
        return notification.SubscriptionType switch
        {
            "channel.chat.message" when chatCommandEvent is not null => new UniversalIncomingEvent(
                UniversalTriggerType.ChatCommand,
                chatCommandEvent.UserDisplayName,
                chatCommandEvent.UserId,
                chatCommandEvent.UserLogin,
                0,
                null,
                null,
                chatCommandEvent.ChatCommandText,
                string.Empty,
                0,
                chatCommandEvent.BadgeSetIds,
                chatCommandEvent.UserIsModerator,
                chatCommandEvent.UserIsBroadcaster),

            "channel.channel_points_custom_reward_redemption.add" => new UniversalIncomingEvent(
                UniversalTriggerType.ChannelPointReward,
                GetString(eventData, "user_name") ?? "Viewer",
                GetString(eventData, "user_id") ?? string.Empty,
                GetString(eventData, "user_login") ?? string.Empty,
                GetInt(eventData, "reward", "cost"),
                GetString(eventData, "reward", "id"),
                GetString(eventData, "reward", "title"),
                string.Empty,
                string.Empty,
                0,
                [],
                false,
                false),

            "channel.cheer" => new UniversalIncomingEvent(
                UniversalTriggerType.Bits,
                GetBoolean(eventData, "is_anonymous") ? "Anonymous" : GetString(eventData, "user_name") ?? "Viewer",
                GetString(eventData, "user_id") ?? string.Empty,
                GetString(eventData, "user_login") ?? string.Empty,
                GetInt(eventData, "bits"),
                null,
                null,
                string.Empty,
                string.Empty,
                0,
                [],
                false,
                false),

            "channel.subscribe" when !GetBoolean(eventData, "is_gift") => new UniversalIncomingEvent(
                UniversalTriggerType.Subscription,
                GetString(eventData, "user_name") ?? "Subscriber",
                GetString(eventData, "user_id") ?? string.Empty,
                GetString(eventData, "user_login") ?? string.Empty,
                1,
                null,
                null,
                string.Empty,
                GetString(eventData, "tier") ?? string.Empty,
                0,
                [],
                false,
                false),

            "channel.subscription.message" => new UniversalIncomingEvent(
                UniversalTriggerType.Subscription,
                GetString(eventData, "user_name") ?? "Subscriber",
                GetString(eventData, "user_id") ?? string.Empty,
                GetString(eventData, "user_login") ?? string.Empty,
                1,
                null,
                null,
                string.Empty,
                GetString(eventData, "tier") ?? string.Empty,
                Math.Max(0, Math.Max(GetInt(eventData, "cumulative_months"), GetInt(eventData, "duration_months"))),
                [],
                false,
                false),

            "channel.subscription.gift" => new UniversalIncomingEvent(
                UniversalTriggerType.GiftSubscription,
                GetBoolean(eventData, "is_anonymous") ? "Anonymous" : GetString(eventData, "user_name") ?? "Gifter",
                GetString(eventData, "user_id") ?? string.Empty,
                GetString(eventData, "user_login") ?? string.Empty,
                Math.Max(1, GetInt(eventData, "total")),
                null,
                null,
                string.Empty,
                GetString(eventData, "tier") ?? string.Empty,
                0,
                [],
                false,
                false),

            "channel.follow" => new UniversalIncomingEvent(
                UniversalTriggerType.Follow,
                GetString(eventData, "user_name") ?? "Follower",
                GetString(eventData, "user_id") ?? string.Empty,
                GetString(eventData, "user_login") ?? string.Empty,
                1,
                null,
                null,
                string.Empty,
                string.Empty,
                0,
                [],
                false,
                false),

            _ => null
        };
    }

    private async Task<BridgeChatMessage?> ParseChatMessageAsync(
        EventSubNotification notification,
        CancellationToken cancellationToken)
    {
        if (!string.Equals(notification.SubscriptionType, "channel.chat.message", StringComparison.Ordinal))
        {
            return null;
        }

        var eventData = notification.EventData;
        var displayName = GetString(eventData, "chatter_user_name")
            ?? GetString(eventData, "user_name")
            ?? "Viewer";
        var userId = GetString(eventData, "chatter_user_id")
            ?? GetString(eventData, "user_id")
            ?? string.Empty;
        var userLogin = GetString(eventData, "chatter_user_login")
            ?? GetString(eventData, "user_login")
            ?? string.Empty;
        var messageText = ExtractChatMessageText(eventData);
        if (string.IsNullOrWhiteSpace(messageText))
        {
            return null;
        }

        var userColor = GetString(eventData, "color") ?? string.Empty;
        var badgeImageUrls = ResolveBadgeImageUrls(eventData);
        var badgeSetIds = ResolveBadgeSetIds(eventData);
        var fragments = await ResolveChatFragmentsAsync(eventData, cancellationToken);
        return new BridgeChatMessage(
            displayName,
            userLogin,
            userId,
            messageText,
            userColor,
            badgeImageUrls,
            badgeSetIds,
            fragments,
            DateTimeOffset.Now);
    }

    private async Task<IReadOnlyList<BridgeChatFragment>> ResolveChatFragmentsAsync(
        JsonElement eventData,
        CancellationToken cancellationToken)
    {
        if (!eventData.TryGetProperty("message", out var messageNode))
        {
            return [];
        }

        if (!messageNode.TryGetProperty("fragments", out var fragmentsNode)
            || fragmentsNode.ValueKind != JsonValueKind.Array)
        {
            var fallbackText = ExtractChatMessageText(eventData);
            if (string.IsNullOrWhiteSpace(fallbackText))
            {
                return [];
            }

            await RefreshThirdPartyChatEmoteCatalogAsync(cancellationToken);
            var fallbackFragments = new List<BridgeChatFragment>();
            AddTextOrThirdPartyChatEmoteFragments(
                fallbackFragments,
                fallbackText,
                GetThirdPartyChatEmoteIndexSnapshot());
            return fallbackFragments;
        }

        var parsedFragments = new List<ParsedBridgeChatFragment>();
        var nativeEmoteFragmentCount = 0;

        foreach (var fragmentNode in fragmentsNode.EnumerateArray())
        {
            var text = GetString(fragmentNode, "text") ?? string.Empty;
            if (string.IsNullOrWhiteSpace(text))
            {
                continue;
            }

            var fragmentType = GetString(fragmentNode, "type") ?? string.Empty;
            if (!string.Equals(fragmentType, "emote", StringComparison.OrdinalIgnoreCase))
            {
                parsedFragments.Add(new ParsedBridgeChatFragment(BridgeChatFragmentKind.Text, text, string.Empty));
                continue;
            }

            var emoteId = GetString(fragmentNode, "emote", "id") ?? string.Empty;
            if (string.IsNullOrWhiteSpace(emoteId))
            {
                parsedFragments.Add(new ParsedBridgeChatFragment(BridgeChatFragmentKind.Text, text, string.Empty));
                continue;
            }

            nativeEmoteFragmentCount++;
            parsedFragments.Add(new ParsedBridgeChatFragment(BridgeChatFragmentKind.Emote, text, emoteId));
        }

        await RefreshThirdPartyChatEmoteCatalogAsync(cancellationToken);
        var thirdPartyEmoteIndex = GetThirdPartyChatEmoteIndexSnapshot();
        var resolvedFragments = new List<BridgeChatFragment>(parsedFragments.Count);
        var convertedNativeEmoteFragments = 0;
        foreach (var fragment in parsedFragments)
        {
            if (fragment.Kind != BridgeChatFragmentKind.Emote)
            {
                AddTextOrThirdPartyChatEmoteFragments(resolvedFragments, fragment.Text, thirdPartyEmoteIndex);
                continue;
            }

            var imageUrl = BuildTwitchStaticEmoteImageUrl(fragment.EmoteId);
            convertedNativeEmoteFragments += string.IsNullOrWhiteSpace(imageUrl) ? 0 : 1;

            resolvedFragments.Add(new BridgeChatFragment(
                string.IsNullOrWhiteSpace(imageUrl) ? BridgeChatFragmentKind.Text : BridgeChatFragmentKind.Emote,
                fragment.Text,
                imageUrl));
        }

        if (nativeEmoteFragmentCount > 0)
        {
            LogChatEmoteFragmentDiagnostic(nativeEmoteFragmentCount, convertedNativeEmoteFragments);
        }

        return resolvedFragments;
    }

    private void LogChatEmoteFragmentDiagnostic(int nativeEmoteFragmentCount, int convertedNativeEmoteFragments)
    {
        var now = DateTimeOffset.UtcNow;
        string? message = null;
        lock (stateGate)
        {
            if (now < nextChatEmoteDiagnosticLogAt)
            {
                suppressedChatEmoteDiagnosticLogs++;
                return;
            }

            var suppressedSuffix = suppressedChatEmoteDiagnosticLogs > 0
                ? $" Suppressed {suppressedChatEmoteDiagnosticLogs} similar chat emote diagnostic(s)."
                : string.Empty;
            suppressedChatEmoteDiagnosticLogs = 0;
            nextChatEmoteDiagnosticLogAt = now.Add(ChatEmoteDiagnosticLogThrottle);
            message = $"Twitch Chatbox converted {convertedNativeEmoteFragments}/{nativeEmoteFragmentCount} native Twitch emote fragment(s) to image URLs.{suppressedSuffix}";
        }

        if (!string.IsNullOrWhiteSpace(message))
        {
            WriteLog(message);
        }
    }

    private BridgeIncomingEvent? ParseChatCommandEvent(EventSubNotification notification)
    {
        if (!string.Equals(notification.SubscriptionType, "channel.chat.message", StringComparison.Ordinal))
        {
            return null;
        }

        var eventData = notification.EventData;
        var messageText = ExtractChatMessageText(eventData).Trim();
        if (string.IsNullOrWhiteSpace(messageText))
        {
            return null;
        }

        var userDisplayName = GetString(eventData, "chatter_user_name")
            ?? GetString(eventData, "user_name")
            ?? "Viewer";
        var userId = GetString(eventData, "chatter_user_id")
            ?? GetString(eventData, "user_id")
            ?? string.Empty;
        var userLogin = GetString(eventData, "chatter_user_login")
            ?? GetString(eventData, "user_login")
            ?? string.Empty;
        var badgeSetIds = ResolveBadgeSetIds(eventData);
        var isBroadcaster = badgeSetIds.Contains("broadcaster", StringComparer.OrdinalIgnoreCase)
            || (!string.IsNullOrWhiteSpace(userId)
                && string.Equals(userId, broadcaster?.UserId, StringComparison.Ordinal))
            || (!string.IsNullOrWhiteSpace(userLogin)
                && string.Equals(userLogin, broadcaster?.Login, StringComparison.OrdinalIgnoreCase));
        var isModerator = isBroadcaster || badgeSetIds.Contains("moderator", StringComparer.OrdinalIgnoreCase);

        return new BridgeIncomingEvent(
            TwitchTriggerType.ChannelPoints,
            userDisplayName,
            0,
            null,
            messageText,
            "Chat Command",
            true,
            messageText,
            userId,
            userLogin,
            badgeSetIds,
            isModerator,
            isBroadcaster);
    }

    private bool IsChatEmoteSetCached(string emoteSetId)
    {
        lock (stateGate)
        {
            return cachedChatEmoteSetIds.Contains(emoteSetId.Trim());
        }
    }

    private string ResolveChatEmoteImageUrl(string emoteId)
    {
        if (string.IsNullOrWhiteSpace(emoteId))
        {
            return string.Empty;
        }

        lock (stateGate)
        {
            return chatEmoteImageUrls.TryGetValue(emoteId.Trim(), out var imageUrl)
                ? imageUrl
                : string.Empty;
        }
    }

    private async Task EnsureChatEmoteSetsCachedAsync(
        IEnumerable<string> emoteSetIds,
        CancellationToken cancellationToken)
    {
        if (activeConfiguration is null || broadcaster is null)
        {
            return;
        }

        var requestedSetIds = emoteSetIds
            .Where(emoteSetId => !string.IsNullOrWhiteSpace(emoteSetId))
            .Select(emoteSetId => emoteSetId.Trim())
            .Distinct(StringComparer.Ordinal)
            .ToArray();
        if (requestedSetIds.Length == 0)
        {
            return;
        }

        List<string> missingSetIds;
        lock (stateGate)
        {
            missingSetIds = requestedSetIds
                .Where(emoteSetId => !cachedChatEmoteSetIds.Contains(emoteSetId))
                .ToList();
        }

        if (missingSetIds.Count == 0)
        {
            return;
        }

        foreach (var batch in missingSetIds.Chunk(25))
        {
            TwitchApiClient.ChatEmoteSetListResponse payload;
            try
            {
                payload = await twitchApiClient.GetEmoteSetsAsync(
                    broadcaster.AccessToken,
                    activeConfiguration.TwitchClientId,
                    batch,
                    cancellationToken);
            }
            catch
            {
                return;
            }

            lock (stateGate)
            {
                foreach (var emote in payload.Data)
                {
                    var emoteId = emote.Id?.Trim() ?? string.Empty;
                    if (string.IsNullOrWhiteSpace(emoteId))
                    {
                        continue;
                    }

                    var imageUrl = BuildChatEmoteImageUrl(payload.Template, emote);
                    if (!string.IsNullOrWhiteSpace(imageUrl))
                    {
                        RememberChatEmoteImageUrlLocked(emoteId, imageUrl);
                    }
                }

                foreach (var emoteSetId in batch)
                {
                    RememberChatEmoteSetIdLocked(emoteSetId);
                }
            }
        }
    }

    private void RememberChatEmoteImageUrlLocked(string emoteId, string imageUrl)
    {
        if (!chatEmoteImageUrls.ContainsKey(emoteId))
        {
            chatEmoteImageUrlInsertionOrder.Enqueue(emoteId);
        }

        chatEmoteImageUrls[emoteId] = imageUrl;
        PruneBoundedDictionaryLocked(chatEmoteImageUrls, chatEmoteImageUrlInsertionOrder, MaxChatEmoteImageUrlCacheEntries);
    }

    private void RememberChatEmoteSetIdLocked(string emoteSetId)
    {
        if (cachedChatEmoteSetIds.Add(emoteSetId))
        {
            cachedChatEmoteSetIdInsertionOrder.Enqueue(emoteSetId);
        }

        PruneBoundedSetLocked(cachedChatEmoteSetIds, cachedChatEmoteSetIdInsertionOrder, MaxCachedChatEmoteSetIds);
    }

    private static void PruneBoundedDictionaryLocked<TKey, TValue>(
        IDictionary<TKey, TValue> values,
        Queue<TKey> insertionOrder,
        int maxEntries)
        where TKey : notnull
    {
        while (values.Count > maxEntries && insertionOrder.Count > 0)
        {
            values.Remove(insertionOrder.Dequeue());
        }
    }

    private static void PruneBoundedSetLocked<T>(
        ISet<T> values,
        Queue<T> insertionOrder,
        int maxEntries)
    {
        while (values.Count > maxEntries && insertionOrder.Count > 0)
        {
            values.Remove(insertionOrder.Dequeue());
        }
    }

    private static string BuildChatEmoteImageUrl(
        string template,
        TwitchApiClient.ChatEmoteResponse emote)
    {
        if (!string.IsNullOrWhiteSpace(template))
        {
            var format = emote.Format.Contains("animated", StringComparer.OrdinalIgnoreCase)
                ? "animated"
                : emote.Format.Contains("static", StringComparer.OrdinalIgnoreCase)
                    ? "static"
                : emote.Format.FirstOrDefault()?.Trim() ?? "static";
            var themeMode = emote.ThemeMode.Contains("dark", StringComparer.OrdinalIgnoreCase)
                ? "dark"
                : emote.ThemeMode.FirstOrDefault()?.Trim() ?? "light";
            var scale = emote.Scale.Contains("2.0", StringComparer.OrdinalIgnoreCase)
                ? "2.0"
                : emote.Scale.LastOrDefault()?.Trim() ?? "1.0";

            return template
                .Replace("{{id}}", emote.Id, StringComparison.Ordinal)
                .Replace("{{format}}", format, StringComparison.Ordinal)
                .Replace("{{theme_mode}}", themeMode, StringComparison.Ordinal)
                .Replace("{{scale}}", scale, StringComparison.Ordinal);
        }

        return !string.IsNullOrWhiteSpace(emote.Images.Url2x)
            ? emote.Images.Url2x
            : !string.IsNullOrWhiteSpace(emote.Images.Url1x)
                ? emote.Images.Url1x
                : emote.Images.Url4x ?? string.Empty;
    }

    private async Task RefreshThirdPartyChatEmoteCatalogAsync(CancellationToken cancellationToken)
    {
        if (activeConfiguration is null
            || broadcaster is null
            || string.IsNullOrWhiteSpace(broadcaster.UserId))
        {
            return;
        }

        var now = DateTimeOffset.UtcNow;
        if (now < nextThirdPartyChatEmoteRefreshAt)
        {
            return;
        }

        if (!await thirdPartyChatEmoteRefreshGate.WaitAsync(0, cancellationToken))
        {
            return;
        }

        try
        {
            now = DateTimeOffset.UtcNow;
            if (now < nextThirdPartyChatEmoteRefreshAt)
            {
                return;
            }

            var nextCatalog = new Dictionary<string, string>(StringComparer.Ordinal);
            await AddNativeTwitchNamedChatEmotesAsync(nextCatalog, cancellationToken);
            await AddBttvChatEmotesAsync(nextCatalog, broadcaster.UserId, cancellationToken);
            await AddFrankerFaceZChatEmotesAsync(nextCatalog, broadcaster.UserId, cancellationToken);
            await AddSevenTvChatEmotesAsync(nextCatalog, broadcaster.UserId, cancellationToken);

            var previousCatalogCount = 0;
            lock (stateGate)
            {
                previousCatalogCount = thirdPartyChatEmoteImageUrls.Count;

                if (nextCatalog.Count > 0)
                {
                    thirdPartyChatEmoteImageUrls.Clear();
                    thirdPartyChatEmoteCodeInsertionOrder.Clear();

                    foreach (var pair in nextCatalog)
                    {
                        RememberThirdPartyChatEmoteImageUrlLocked(pair.Key, pair.Value);
                    }

                    thirdPartyChatEmoteIndex = BuildThirdPartyChatEmoteIndex(thirdPartyChatEmoteImageUrls);
                    nextThirdPartyChatEmoteRefreshAt = now.Add(ThirdPartyChatEmoteRefreshInterval);
                }
                else
                {
                    nextThirdPartyChatEmoteRefreshAt = now.Add(ThirdPartyChatEmoteRetryInterval);
                }
            }

            if (nextCatalog.Count > 0)
            {
                WriteLog($"Loaded {nextCatalog.Count} named chat emotes for Twitch Chatbox image matching.");
            }
            else if (previousCatalogCount == 0)
            {
                WriteLog("No named chat emote catalog entries loaded yet, so unmatched emote names will stay as text for now.");
            }
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            throw;
        }
        catch (Exception ex)
        {
            lock (stateGate)
            {
                nextThirdPartyChatEmoteRefreshAt = DateTimeOffset.UtcNow.Add(ThirdPartyChatEmoteRetryInterval);
            }

            WriteLog($"Could not refresh third-party chat emotes yet: {ex.Message}");
        }
        finally
        {
            thirdPartyChatEmoteRefreshGate.Release();
        }
    }

    private async Task AddNativeTwitchNamedChatEmotesAsync(
        IDictionary<string, string> catalog,
        CancellationToken cancellationToken)
    {
        if (activeConfiguration is null || broadcaster is null)
        {
            return;
        }

        try
        {
            var globalEmotes = await twitchApiClient.GetGlobalChatEmotesAsync(
                broadcaster.AccessToken,
                activeConfiguration.TwitchClientId,
                cancellationToken);
            AddTwitchNamedChatEmotes(catalog, globalEmotes);

            var channelEmotes = await twitchApiClient.GetChannelChatEmotesAsync(
                broadcaster.AccessToken,
                activeConfiguration.TwitchClientId,
                broadcaster.UserId,
                cancellationToken);
            AddTwitchNamedChatEmotes(catalog, channelEmotes);

            if (HasScope(broadcaster, TwitchScopes.UserEmotes))
            {
                var userEmotes = await twitchApiClient.GetUserChatEmotesAsync(
                    broadcaster.AccessToken,
                    activeConfiguration.TwitchClientId,
                    broadcaster.UserId,
                    broadcaster.UserId,
                    cancellationToken);
                AddTwitchNamedChatEmotes(catalog, userEmotes);
            }
            else
            {
                WriteLog("Reconnect broadcaster Twitch login to let Twitch Chatbox resolve subscriber and follower emote names.");
            }
        }
        catch
        {
        }
    }

    private static void AddTwitchNamedChatEmotes(
        IDictionary<string, string> catalog,
        TwitchApiClient.ChatEmoteSetListResponse payload)
    {
        foreach (var emote in payload.Data)
        {
            var code = emote.Name?.Trim() ?? string.Empty;
            var emoteId = emote.Id?.Trim() ?? string.Empty;
            if (string.IsNullOrWhiteSpace(code) || string.IsNullOrWhiteSpace(emoteId))
            {
                continue;
            }

            var imageUrl = BuildChatEmoteImageUrl(payload.Template, emote);
            if (string.IsNullOrWhiteSpace(imageUrl))
            {
                imageUrl = BuildTwitchStaticEmoteImageUrl(emoteId);
            }

            if (!string.IsNullOrWhiteSpace(imageUrl))
            {
                catalog[code] = imageUrl;
            }
        }
    }

    private async Task AddBttvChatEmotesAsync(
        IDictionary<string, string> catalog,
        string broadcasterUserId,
        CancellationToken cancellationToken)
    {
        await AddBttvEmotesFromEndpointAsync(
            catalog,
            "https://api.betterttv.net/3/cached/emotes/global",
            cancellationToken);
        await AddBttvEmotesFromEndpointAsync(
            catalog,
            $"https://api.betterttv.net/3/cached/users/twitch/{Uri.EscapeDataString(broadcasterUserId)}",
            cancellationToken);
    }

    private static async Task AddBttvEmotesFromEndpointAsync(
        IDictionary<string, string> catalog,
        string endpoint,
        CancellationToken cancellationToken)
    {
        using var document = await TryFetchJsonDocumentAsync(endpoint, cancellationToken);
        if (document is null)
        {
            return;
        }

        var root = document.RootElement;
        if (root.ValueKind == JsonValueKind.Array)
        {
            AddBttvEmoteArray(catalog, root);
            return;
        }

        if (root.ValueKind != JsonValueKind.Object)
        {
            return;
        }

        if (root.TryGetProperty("channelEmotes", out var channelEmotes)
            && channelEmotes.ValueKind == JsonValueKind.Array)
        {
            AddBttvEmoteArray(catalog, channelEmotes);
        }

        if (root.TryGetProperty("sharedEmotes", out var sharedEmotes)
            && sharedEmotes.ValueKind == JsonValueKind.Array)
        {
            AddBttvEmoteArray(catalog, sharedEmotes);
        }
    }

    private static void AddBttvEmoteArray(IDictionary<string, string> catalog, JsonElement emotesNode)
    {
        foreach (var emoteNode in emotesNode.EnumerateArray())
        {
            var code = GetJsonString(emoteNode, "code");
            var id = GetJsonString(emoteNode, "id");
            if (string.IsNullOrWhiteSpace(code) || string.IsNullOrWhiteSpace(id))
            {
                continue;
            }

            catalog[code.Trim()] = $"https://cdn.betterttv.net/emote/{Uri.EscapeDataString(id.Trim())}/2x";
        }
    }

    private async Task AddFrankerFaceZChatEmotesAsync(
        IDictionary<string, string> catalog,
        string broadcasterUserId,
        CancellationToken cancellationToken)
    {
        await AddFrankerFaceZEmotesFromEndpointAsync(
            catalog,
            "https://api.frankerfacez.com/v1/set/global",
            cancellationToken);
        await AddFrankerFaceZEmotesFromEndpointAsync(
            catalog,
            $"https://api.frankerfacez.com/v1/room/id/{Uri.EscapeDataString(broadcasterUserId)}",
            cancellationToken);
    }

    private static async Task AddFrankerFaceZEmotesFromEndpointAsync(
        IDictionary<string, string> catalog,
        string endpoint,
        CancellationToken cancellationToken)
    {
        using var document = await TryFetchJsonDocumentAsync(endpoint, cancellationToken);
        if (document is null
            || !document.RootElement.TryGetProperty("sets", out var setsNode)
            || setsNode.ValueKind != JsonValueKind.Object)
        {
            return;
        }

        foreach (var setNode in setsNode.EnumerateObject())
        {
            if (!setNode.Value.TryGetProperty("emoticons", out var emoticonsNode)
                || emoticonsNode.ValueKind != JsonValueKind.Array)
            {
                continue;
            }

            foreach (var emoteNode in emoticonsNode.EnumerateArray())
            {
                var code = GetJsonString(emoteNode, "name");
                var imageUrl = ResolveFrankerFaceZImageUrl(emoteNode);
                if (!string.IsNullOrWhiteSpace(code) && !string.IsNullOrWhiteSpace(imageUrl))
                {
                    catalog[code.Trim()] = imageUrl;
                }
            }
        }
    }

    private async Task AddSevenTvChatEmotesAsync(
        IDictionary<string, string> catalog,
        string broadcasterUserId,
        CancellationToken cancellationToken)
    {
        await AddSevenTvEmotesFromEndpointAsync(
            catalog,
            "https://7tv.io/v3/emote-sets/global",
            cancellationToken);
        await AddSevenTvEmotesFromEndpointAsync(
            catalog,
            $"https://7tv.io/v3/users/twitch/{Uri.EscapeDataString(broadcasterUserId)}",
            cancellationToken);
    }

    private static async Task AddSevenTvEmotesFromEndpointAsync(
        IDictionary<string, string> catalog,
        string endpoint,
        CancellationToken cancellationToken)
    {
        using var document = await TryFetchJsonDocumentAsync(endpoint, cancellationToken);
        if (document is null)
        {
            return;
        }

        var root = document.RootElement;
        if (root.TryGetProperty("emote_set", out var emoteSetNode)
            && emoteSetNode.ValueKind == JsonValueKind.Object)
        {
            AddSevenTvEmoteSet(catalog, emoteSetNode);
            return;
        }

        AddSevenTvEmoteSet(catalog, root);
    }

    private static void AddSevenTvEmoteSet(IDictionary<string, string> catalog, JsonElement emoteSetNode)
    {
        if (!emoteSetNode.TryGetProperty("emotes", out var emotesNode)
            || emotesNode.ValueKind != JsonValueKind.Array)
        {
            return;
        }

        foreach (var emoteNode in emotesNode.EnumerateArray())
        {
            var code = GetJsonString(emoteNode, "name");
            var imageUrl = ResolveSevenTvImageUrl(emoteNode);
            if (!string.IsNullOrWhiteSpace(code) && !string.IsNullOrWhiteSpace(imageUrl))
            {
                catalog[code.Trim()] = imageUrl;
            }
        }
    }

    private void RememberThirdPartyChatEmoteImageUrlLocked(string code, string imageUrl)
    {
        var normalizedCode = code.Trim();
        if (normalizedCode.Length == 0 || string.IsNullOrWhiteSpace(imageUrl))
        {
            return;
        }

        if (!thirdPartyChatEmoteImageUrls.ContainsKey(normalizedCode))
        {
            thirdPartyChatEmoteCodeInsertionOrder.Enqueue(normalizedCode);
        }

        thirdPartyChatEmoteImageUrls[normalizedCode] = imageUrl.Trim();
        PruneBoundedDictionaryLocked(
            thirdPartyChatEmoteImageUrls,
            thirdPartyChatEmoteCodeInsertionOrder,
            MaxThirdPartyChatEmoteEntries);
    }

    private IReadOnlyDictionary<char, IReadOnlyList<ThirdPartyChatEmoteEntry>> GetThirdPartyChatEmoteIndexSnapshot()
    {
        lock (stateGate)
        {
            return thirdPartyChatEmoteIndex;
        }
    }

    private static IReadOnlyDictionary<char, IReadOnlyList<ThirdPartyChatEmoteEntry>> BuildThirdPartyChatEmoteIndex(
        IReadOnlyDictionary<string, string> emoteImageUrls)
    {
        return emoteImageUrls
            .Where(pair => !string.IsNullOrWhiteSpace(pair.Key) && !string.IsNullOrWhiteSpace(pair.Value))
            .Select(pair => new ThirdPartyChatEmoteEntry(pair.Key, pair.Value))
            .GroupBy(entry => entry.Code[0])
            .ToDictionary(
                group => group.Key,
                group => (IReadOnlyList<ThirdPartyChatEmoteEntry>)[.. group
                    .OrderByDescending(entry => entry.Code.Length)
                    .ThenBy(entry => entry.Code, StringComparer.Ordinal)],
                EqualityComparer<char>.Default);
    }

    private static void AddTextOrThirdPartyChatEmoteFragments(
        ICollection<BridgeChatFragment> fragments,
        string text,
        IReadOnlyDictionary<char, IReadOnlyList<ThirdPartyChatEmoteEntry>> thirdPartyEmoteIndex)
    {
        if (string.IsNullOrEmpty(text))
        {
            return;
        }

        if (thirdPartyEmoteIndex.Count == 0)
        {
            fragments.Add(new BridgeChatFragment(BridgeChatFragmentKind.Text, text, string.Empty));
            return;
        }

        StringBuilder? pendingText = null;
        var index = 0;
        while (index < text.Length)
        {
            if (TryFindThirdPartyChatEmote(text, index, thirdPartyEmoteIndex, out var emote))
            {
                if (pendingText is { Length: > 0 })
                {
                    fragments.Add(new BridgeChatFragment(BridgeChatFragmentKind.Text, pendingText.ToString(), string.Empty));
                    pendingText.Clear();
                }

                fragments.Add(new BridgeChatFragment(BridgeChatFragmentKind.Emote, emote.Code, emote.ImageUrl));
                index += emote.Code.Length;
                continue;
            }

            pendingText ??= new StringBuilder();
            pendingText.Append(text[index]);
            index++;
        }

        if (pendingText is { Length: > 0 })
        {
            fragments.Add(new BridgeChatFragment(BridgeChatFragmentKind.Text, pendingText.ToString(), string.Empty));
        }
    }

    private static bool TryFindThirdPartyChatEmote(
        string text,
        int startIndex,
        IReadOnlyDictionary<char, IReadOnlyList<ThirdPartyChatEmoteEntry>> thirdPartyEmoteIndex,
        out ThirdPartyChatEmoteEntry emote)
    {
        emote = default;
        if (char.IsWhiteSpace(text[startIndex])
            || !thirdPartyEmoteIndex.TryGetValue(text[startIndex], out var candidates))
        {
            return false;
        }

        foreach (var candidate in candidates)
        {
            if (candidate.Code.Length < 2 || startIndex + candidate.Code.Length > text.Length)
            {
                continue;
            }

            if (string.CompareOrdinal(text, startIndex, candidate.Code, 0, candidate.Code.Length) == 0)
            {
                emote = candidate;
                return true;
            }
        }

        return false;
    }

    private static async Task<JsonDocument?> TryFetchJsonDocumentAsync(
        string endpoint,
        CancellationToken cancellationToken)
    {
        try
        {
            var json = await ThirdPartyChatEmoteHttpClient.GetStringAsync(endpoint, cancellationToken);
            return JsonDocument.Parse(json);
        }
        catch
        {
            return null;
        }
    }

    private static string ResolveFrankerFaceZImageUrl(JsonElement emoteNode)
    {
        if (!emoteNode.TryGetProperty("urls", out var urlsNode) || urlsNode.ValueKind != JsonValueKind.Object)
        {
            return string.Empty;
        }

        foreach (var scale in new[] { "2", "1", "4" })
        {
            var imageUrl = GetJsonString(urlsNode, scale);
            if (!string.IsNullOrWhiteSpace(imageUrl))
            {
                return NormalizeExternalEmoteImageUrl(imageUrl);
            }
        }

        return string.Empty;
    }

    private static string ResolveSevenTvImageUrl(JsonElement emoteNode)
    {
        var emoteId = GetJsonString(emoteNode, "id") ?? GetJsonString(emoteNode, "data", "id") ?? string.Empty;
        var hostNode = default(JsonElement);
        var hasHostNode = emoteNode.TryGetProperty("data", out var dataNode)
            && dataNode.ValueKind == JsonValueKind.Object
            && dataNode.TryGetProperty("host", out hostNode)
            && hostNode.ValueKind == JsonValueKind.Object;

        if (!hasHostNode
            && emoteNode.TryGetProperty("host", out var directHostNode)
            && directHostNode.ValueKind == JsonValueKind.Object)
        {
            hostNode = directHostNode;
            hasHostNode = true;
        }

        if (hasHostNode)
        {
            var hostUrl = GetJsonString(hostNode, "url") ?? string.Empty;
            if (!string.IsNullOrWhiteSpace(hostUrl)
                && hostNode.TryGetProperty("files", out var filesNode)
                && filesNode.ValueKind == JsonValueKind.Array)
            {
                var fileName = PickSevenTvImageFileName(filesNode);
                if (!string.IsNullOrWhiteSpace(fileName))
                {
                    return NormalizeExternalEmoteImageUrl($"{hostUrl.TrimEnd('/')}/{fileName}");
                }
            }
        }

        return string.IsNullOrWhiteSpace(emoteId)
            ? string.Empty
            : $"https://cdn.7tv.app/emote/{Uri.EscapeDataString(emoteId.Trim())}/2x.webp";
    }

    private static string PickSevenTvImageFileName(JsonElement filesNode)
    {
        var fallback = string.Empty;
        foreach (var preferredName in new[] { "2x.gif", "1x.gif", "3x.gif", "4x.gif", "2x.png", "1x.png", "3x.png", "4x.png", "2x.webp", "1x.webp", "3x.webp", "4x.webp" })
        {
            foreach (var fileNode in filesNode.EnumerateArray())
            {
                var name = GetJsonString(fileNode, "name") ?? string.Empty;
                if (string.Equals(name, preferredName, StringComparison.OrdinalIgnoreCase))
                {
                    return name;
                }

                if (string.IsNullOrWhiteSpace(fallback)
                    && (name.EndsWith(".gif", StringComparison.OrdinalIgnoreCase)
                        || name.EndsWith(".png", StringComparison.OrdinalIgnoreCase)
                        || name.EndsWith(".webp", StringComparison.OrdinalIgnoreCase)))
                {
                    fallback = name;
                }
            }
        }

        return fallback;
    }

    private static string NormalizeExternalEmoteImageUrl(string imageUrl)
    {
        var normalized = imageUrl.Trim();
        if (normalized.StartsWith("//", StringComparison.Ordinal))
        {
            return $"https:{normalized}";
        }

        if (normalized.StartsWith("/", StringComparison.Ordinal))
        {
            return string.Empty;
        }

        return normalized.StartsWith("https://", StringComparison.OrdinalIgnoreCase)
            ? normalized
            : string.Empty;
    }

    private static string BuildTwitchStaticEmoteImageUrl(string emoteId) =>
        string.IsNullOrWhiteSpace(emoteId)
            ? string.Empty
            : $"https://static-cdn.jtvnw.net/emoticons/v2/{Uri.EscapeDataString(emoteId.Trim())}/static/dark/2.0";

    private static string? GetJsonString(JsonElement element, params string[] path)
    {
        var current = element;
        foreach (var segment in path)
        {
            if (current.ValueKind != JsonValueKind.Object || !current.TryGetProperty(segment, out current))
            {
                return null;
            }
        }

        return current.ValueKind switch
        {
            JsonValueKind.String => current.GetString(),
            JsonValueKind.Number => current.ToString(),
            _ => null
        };
    }

    private static string ExtractChatMessageText(JsonElement eventData)
    {
        if (!eventData.TryGetProperty("message", out var messageNode))
        {
            return string.Empty;
        }

        if (messageNode.ValueKind == JsonValueKind.String)
        {
            return messageNode.GetString() ?? string.Empty;
        }

        if (messageNode.TryGetProperty("text", out var textNode)
            && textNode.ValueKind == JsonValueKind.String)
        {
            return textNode.GetString() ?? string.Empty;
        }

        if (!messageNode.TryGetProperty("fragments", out var fragmentsNode)
            || fragmentsNode.ValueKind != JsonValueKind.Array)
        {
            return string.Empty;
        }

        var fragments = fragmentsNode
            .EnumerateArray()
            .Select(fragment => GetString(fragment, "text") ?? string.Empty)
            .Where(text => !string.IsNullOrWhiteSpace(text))
            .ToArray();

        return fragments.Length == 0
            ? string.Empty
            : string.Concat(fragments);
    }

    private IReadOnlyList<string> ResolveBadgeImageUrls(JsonElement eventData)
    {
        if (!eventData.TryGetProperty("badges", out var badgesNode)
            || badgesNode.ValueKind != JsonValueKind.Array)
        {
            return [];
        }

        Dictionary<string, string> badgeCatalogSnapshot;
        lock (stateGate)
        {
            badgeCatalogSnapshot = chatBadgeImageUrls.Count == 0
                ? []
                : new Dictionary<string, string>(chatBadgeImageUrls, StringComparer.OrdinalIgnoreCase);
        }

        if (badgeCatalogSnapshot.Count == 0)
        {
            return [];
        }

        var badgeImages = new List<string>();
        foreach (var badgeNode in badgesNode.EnumerateArray())
        {
            var setId = GetString(badgeNode, "set_id");
            var versionId = GetString(badgeNode, "id");
            if (string.IsNullOrWhiteSpace(setId) || string.IsNullOrWhiteSpace(versionId))
            {
                continue;
            }

            var key = $"{setId}:{versionId}";
            if (badgeCatalogSnapshot.TryGetValue(key, out var imageUrl)
                && !string.IsNullOrWhiteSpace(imageUrl))
            {
                badgeImages.Add(imageUrl);
            }
        }

        return badgeImages;
    }

    private static IReadOnlyList<string> ResolveBadgeSetIds(JsonElement eventData)
    {
        if (!eventData.TryGetProperty("badges", out var badgesNode)
            || badgesNode.ValueKind != JsonValueKind.Array)
        {
            return [];
        }

        var badgeSetIds = new List<string>();
        foreach (var badgeNode in badgesNode.EnumerateArray())
        {
            var setId = GetString(badgeNode, "set_id");
            if (!string.IsNullOrWhiteSpace(setId))
            {
                badgeSetIds.Add(setId);
            }
        }

        return badgeSetIds;
    }

    private static void IndexBadgeSets(
        Dictionary<string, string> destination,
        IEnumerable<TwitchApiClient.ChatBadgeSetResponse> badgeSets)
    {
        foreach (var badgeSet in badgeSets)
        {
            if (string.IsNullOrWhiteSpace(badgeSet.SetId))
            {
                continue;
            }

            foreach (var version in badgeSet.Versions)
            {
                if (string.IsNullOrWhiteSpace(version.Id))
                {
                    continue;
                }

                var imageUrl = !string.IsNullOrWhiteSpace(version.ImageUrl2x)
                    ? version.ImageUrl2x
                    : !string.IsNullOrWhiteSpace(version.ImageUrl1x)
                        ? version.ImageUrl1x
                        : version.ImageUrl4x;

                if (string.IsNullOrWhiteSpace(imageUrl))
                {
                    continue;
                }

                destination[$"{badgeSet.SetId}:{version.Id}"] = imageUrl;
            }
        }
    }

    private static bool HasScope(TwitchAccountSnapshot account, string scope) =>
        account.Scopes.Any(existingScope => string.Equals(existingScope, scope, StringComparison.OrdinalIgnoreCase));

    private static string BuildTriggerInfoAnnouncement(
        BridgeRuntimeConfiguration configuration,
        string currentAvatarId)
    {
        var normalizedCurrentAvatarId = currentAvatarId?.Trim() ?? string.Empty;
        if (string.IsNullOrWhiteSpace(normalizedCurrentAvatarId))
        {
            return string.Empty;
        }

        var channelPointSections = configuration.Rules
            .Where(rule => rule.IsEnabled
                && rule.TriggerType == TwitchTriggerType.ChannelPoints
                && rule.ActionType == OscActionType.SetTrigger
                && rule.SharedRewardChoiceEnabled
                && rule.SharedRewardChoiceNumber > 0
                && AvatarRuleActivationPolicy.IsRuleActiveForCurrentAvatar(
                    rule.IsGlobalOverride,
                    rule.BelongsToMasterAvatarProfile,
                    rule.ActionType,
                    rule.AvatarChangeTargetId,
                    rule.RequiredAvatarId,
                    normalizedCurrentAvatarId,
                    avatarChangeTransitionActive: false))
            .GroupBy(rule => string.IsNullOrWhiteSpace(rule.ChannelPointRewardTitle)
                ? rule.AvatarProfileId.ToString("N")
                : ManagedRewardPresentation.NormalizeTitleIdentityKey(rule.ChannelPointRewardTitle))
            .Select(BuildChannelPointOutfitAnnouncementSection)
            .Where(section => !string.IsNullOrWhiteSpace(section))
            .ToArray();

        var bitsOptions = configuration.Rules
            .Where(rule => rule.IsEnabled
                && IsBitsOutfitSetTriggerRule(rule)
                && IsSupporterRuleScopedToCurrentAvatar(rule, normalizedCurrentAvatarId)
                && !string.IsNullOrWhiteSpace(rule.SharedRewardHelpText))
            .OrderBy(rule => Math.Max(1, rule.MinimumAmount))
            .ThenBy(rule => rule.SharedRewardHelpText, StringComparer.CurrentCultureIgnoreCase)
            .Select(rule => TF("{0}+ bits {1}", Math.Max(1, rule.MinimumAmount), rule.SharedRewardHelpText.Trim()))
            .ToArray();

        var sections = new List<string>();
        sections.AddRange(channelPointSections);
        if (bitsOptions.Length > 0)
        {
            sections.Add(TF("Bits outfits: {0}", BuildCompactAnnouncementOptionList(bitsOptions)));
        }

        return sections.Count == 0
            ? string.Empty
            : SanitizeBotMessage(TF("Outfit triggers: {0}", string.Join(" | ", sections)));
    }

    private static string BuildChannelPointOutfitAnnouncementSection(IGrouping<string, TriggerRuleSnapshot> group)
    {
        var rules = group
            .OrderBy(rule => rule.SharedRewardChoiceNumber)
            .ThenBy(rule => rule.SharedRewardHelpText, StringComparer.CurrentCultureIgnoreCase)
            .ToArray();
        if (rules.Length == 0)
        {
            return string.Empty;
        }

        var rewardName = ManagedRewardPresentation.StripPrefix(rules[0].ChannelPointRewardTitle);
        if (string.IsNullOrWhiteSpace(rewardName))
        {
            rewardName = rules[0].AvatarProfileName;
        }

        if (string.IsNullOrWhiteSpace(rewardName))
        {
            rewardName = T("Outfit redeem");
        }

        var options = rules
            .Select(rule => TF("#{0} {1}", rule.SharedRewardChoiceNumber, GetSetTriggerChoiceLabel(rule)))
            .ToArray();
        return TF("{0}: {1}", rewardName.Trim(), BuildCompactAnnouncementOptionList(options));
    }

    private static string GetSetTriggerChoiceLabel(TriggerRuleSnapshot rule)
    {
        if (!string.IsNullOrWhiteSpace(rule.SharedRewardHelpText))
        {
            return rule.SharedRewardHelpText.Trim();
        }

        if (!string.IsNullOrWhiteSpace(rule.Name))
        {
            return rule.Name.Trim();
        }

        return TF("Set Trigger ({0} params)", rule.SetTriggerActions.Count);
    }

    private static string BuildCompactAnnouncementOptionList(IReadOnlyList<string> options, int maxOptions = 5)
    {
        var selected = options
            .Where(option => !string.IsNullOrWhiteSpace(option))
            .Take(maxOptions)
            .ToArray();
        if (selected.Length == 0)
        {
            return string.Empty;
        }

        var omittedCount = Math.Max(0, options.Count - selected.Length);
        return omittedCount > 0
            ? string.Join(", ", selected) + ", " + TF("and {0} more", omittedCount)
            : string.Join(", ", selected);
    }

    private string BuildBotMessage(
        TriggerRuleSnapshot rule,
        BridgeIncomingEvent bridgeEvent,
        string resolvedValue,
        double? durationSecondsOverride)
    {
        var template = string.IsNullOrWhiteSpace(rule.BotMessageTemplate)
            ? "{user} triggered {rule}. Active for {duration}. Cooldown {cooldown}."
            : rule.BotMessageTemplate;
        var rewardLabel = !string.IsNullOrWhiteSpace(rule.ChannelPointRewardTitle)
            ? rule.ChannelPointRewardTitle
            : ChatCommandUtility.IsConfigured(rule.ChatCommandText)
                ? rule.ChatCommandText
                : bridgeEvent.RewardTitle ?? bridgeEvent.TriggerLabel;

        var message = template
            .Replace("{user}", bridgeEvent.UserDisplayName, StringComparison.OrdinalIgnoreCase)
            .Replace("{rule}", rule.Name, StringComparison.OrdinalIgnoreCase)
            .Replace("{trigger}", bridgeEvent.TriggerLabel, StringComparison.OrdinalIgnoreCase)
            .Replace("{amount}", bridgeEvent.Amount.ToString(CultureInfo.InvariantCulture), StringComparison.OrdinalIgnoreCase)
            .Replace("{reward}", rewardLabel, StringComparison.OrdinalIgnoreCase)
            .Replace("{duration}", DescribeDuration(durationSecondsOverride ?? GetBotMessageDurationSeconds(rule, bridgeEvent)), StringComparison.OrdinalIgnoreCase)
            .Replace("{cooldown}", DescribeDuration(GetCooldownSeconds(rule)), StringComparison.OrdinalIgnoreCase)
            .Replace("{parameter}", DescribeActionAddress(rule), StringComparison.OrdinalIgnoreCase)
            .Replace("{value}", resolvedValue, StringComparison.OrdinalIgnoreCase);

        return SanitizeBotMessage(message);
    }

    private static string BuildSupporterOverrideInfoBotMessage(
        BridgeRuntimeConfiguration configuration,
        TriggerRuleSnapshot rule,
        BridgeIncomingEvent bridgeEvent,
        double? durationSecondsOverride)
    {
        var durationText = DescribeDuration(durationSecondsOverride ?? GetBotMessageDurationSeconds(rule, bridgeEvent));
        var amountText = DescribeSupporterOverrideEventAmount(bridgeEvent);
        var prefix = TF(
            "{0} triggered {1} with {2}; added {3}.",
            bridgeEvent.UserDisplayName,
            rule.Name,
            amountText,
            durationText);
        var options = BuildSupporterOverrideOptionDescriptions(configuration.Rules);
        if (options.Count == 0)
        {
            return SanitizeBotMessage(prefix);
        }

        var listPrefix = $"{prefix} {T("Overrides")}: ";
        var selectedOptions = new List<string>();
        var omittedCount = 0;

        for (var index = 0; index < options.Count; index++)
        {
            var remainingAfterCandidate = options.Count - index - 1;
            var candidateOptions = new List<string>(selectedOptions) { options[index] };
            var candidate = listPrefix + string.Join(" | ", candidateOptions);
            if (remainingAfterCandidate > 0)
            {
                candidate += $" | {TF("and {0} more", remainingAfterCandidate)}";
            }

            if (SanitizeBotMessage(candidate, truncate: false).Length <= TwitchChatMessageMaxCharacters)
            {
                selectedOptions.Add(options[index]);
                omittedCount = remainingAfterCandidate;
                continue;
            }

            omittedCount = options.Count - selectedOptions.Count;
            break;
        }

        var message = selectedOptions.Count == 0
            ? listPrefix + TF("and {0} more", options.Count)
            : listPrefix + string.Join(" | ", selectedOptions)
                + (omittedCount > 0 ? $" | {TF("and {0} more", omittedCount)}" : string.Empty);

        return SanitizeBotMessage(message);
    }

    private static IReadOnlyList<string> BuildSupporterOverrideOptionDescriptions(
        IReadOnlyList<TriggerRuleSnapshot> rules)
    {
        return rules
            .Where(rule => rule.IsEnabled && IsSupporterOverrideRule(rule))
            .OrderBy(rule => GetSupporterOverrideListTriggerTypeSortRank(rule.TriggerType))
            .ThenBy(rule => Math.Max(1, rule.MinimumAmount))
            .ThenBy(rule => rule.Name, StringComparer.CurrentCultureIgnoreCase)
            .Select(DescribeSupporterOverrideOption)
            .Where(description => !string.IsNullOrWhiteSpace(description))
            .ToArray();
    }

    private static int GetSupporterOverrideListTriggerTypeSortRank(TwitchTriggerType triggerType) => triggerType switch
    {
        TwitchTriggerType.Bits => 1,
        TwitchTriggerType.Subscriptions => 2,
        _ => 3
    };

    private static string DescribeSupporterOverrideOption(TriggerRuleSnapshot rule)
    {
        var threshold = Math.Max(1, rule.MinimumAmount);
        if (rule.TriggerType == TwitchTriggerType.Bits)
        {
            return rule.AmountScaledDurationEnabled
                ? TF(
                    "Bits {0}+: every {1} bits adds {2}",
                    threshold,
                    Math.Max(1, rule.BitsAmountUnitsPerDuration),
                    DescribeDuration(Math.Max(1, rule.BitsSecondsPerAmountUnit)))
                : TF("Bits {0}+: {1}", threshold, DescribeDuration(rule.DurationSeconds));
        }

        if (rule.TriggerType == TwitchTriggerType.Subscriptions)
        {
            return rule.AmountScaledDurationEnabled
                ? TF(
                    "Subs {0}+: every {1} subs adds {2}",
                    threshold,
                    Math.Max(1, rule.SubscriptionsAmountUnitsPerDuration),
                    DescribeDuration(Math.Max(1, rule.SubscriptionsSecondsPerAmountUnit)))
                : TF("Subs {0}+: {1}", threshold, DescribeDuration(rule.DurationSeconds));
        }

        return string.Empty;
    }

    private static string DescribeSupporterOverrideEventAmount(BridgeIncomingEvent bridgeEvent)
    {
        var amount = Math.Max(1, bridgeEvent.Amount);
        if (bridgeEvent.TriggerType == TwitchTriggerType.Bits)
        {
            return TF("{0} bits", amount);
        }

        if (string.Equals(bridgeEvent.TriggerLabel, "Gift Sub", StringComparison.OrdinalIgnoreCase))
        {
            return amount == 1
                ? T("1 gift sub")
                : TF("{0} gift subs", amount);
        }

        return amount == 1
            ? T("1 sub")
            : TF("{0} subs", amount);
    }

    private static string SanitizeBotMessage(string message, bool truncate = true)
    {
        message = message.Replace(Environment.NewLine, " ", StringComparison.Ordinal).Replace('\n', ' ').Replace('\r', ' ').Trim();
        return truncate && message.Length > TwitchChatMessageMaxCharacters
            ? message[..TwitchChatMessageMaxCharacters]
            : message;
    }

    private static double GetBotMessageDurationSeconds(TriggerRuleSnapshot rule, BridgeIncomingEvent bridgeEvent) =>
        IsTimedSupporterOverrideRule(rule)
            ? GetSupporterOverrideDuration(rule, bridgeEvent).TotalSeconds
            : rule.DurationSeconds;

    private void ApplyChatboxRelayConfiguration(BridgeRuntimeConfiguration configuration)
    {
        if (configuration.ChatboxOscEnabled)
        {
            return;
        }

        CancelChatboxRelayWorker(clearQueuedMessages: true);
    }

    private void TryQueueChatboxRelay(BridgeChatMessage chatMessage)
    {
        if (string.IsNullOrWhiteSpace(chatMessage.MessageText))
        {
            return;
        }

        if (!IsChatboxRelayEnabled())
        {
            CancelChatboxRelayWorker(clearQueuedMessages: true);
            return;
        }

        if (ShouldBlockChatboxRelayMessage(chatMessage))
        {
            LogBlockedChatboxRelayMessage();
            return;
        }

        if (!IsOscActive || !HasDiscoveredVrChat)
        {
            LogChatboxRelayUnavailable("VRChat OSC chat relay is waiting for OSC / VRChat connection.");
            return;
        }

        var relayLine = FormatChatboxRelayLine(chatMessage);
        if (string.IsNullOrWhiteSpace(relayLine))
        {
            return;
        }

        lock (stateGate)
        {
            queuedChatboxRelayMessages.Enqueue(new QueuedChatboxRelayLine(relayLine));
        }

        EnsureChatboxRelayWorker();
    }

    private void EnsureChatboxRelayWorker()
    {
        CancellationTokenSource? relayCancellation;

        lock (stateGate)
        {
            if (chatboxRelayTask is { IsCompleted: false })
            {
                return;
            }

            relayCancellation = runtimeCancellation is null
                ? new CancellationTokenSource()
                : CancellationTokenSource.CreateLinkedTokenSource(runtimeCancellation.Token);
            chatboxRelayCancellation = relayCancellation;
            chatboxRelayTask = Task.Run(async () =>
            {
                try
                {
                    await RunChatboxRelayLoopAsync(relayCancellation.Token);
                }
                finally
                {
                    relayCancellation.Dispose();
                    lock (stateGate)
                    {
                        if (ReferenceEquals(chatboxRelayCancellation, relayCancellation))
                        {
                            chatboxRelayCancellation = null;
                        }
                    }
                }
            }, CancellationToken.None);
        }
    }

    private async Task RunChatboxRelayLoopAsync(CancellationToken cancellationToken)
    {
        while (!cancellationToken.IsCancellationRequested)
        {
            var delaySeconds = 3;
            string relayMessage;

            lock (stateGate)
            {
                if (activeConfiguration?.ChatboxOscEnabled != true)
                {
                    queuedChatboxRelayMessages.Clear();
                    return;
                }

                if (queuedChatboxRelayMessages.Count == 0)
                {
                    return;
                }

                delaySeconds = Math.Clamp(activeConfiguration.ChatboxOscDelaySeconds, 1, 6);
            }

            await Task.Delay(TimeSpan.FromSeconds(delaySeconds), cancellationToken);

            lock (stateGate)
            {
                if (activeConfiguration?.ChatboxOscEnabled != true)
                {
                    queuedChatboxRelayMessages.Clear();
                    return;
                }

                if (queuedChatboxRelayMessages.Count == 0)
                {
                    return;
                }

                relayMessage = BuildNextChatboxRelayPostLocked();
            }

            if (string.IsNullOrWhiteSpace(relayMessage))
            {
                continue;
            }

            if (!IsOscActive || !HasDiscoveredVrChat)
            {
                CancelChatboxRelayWorker(clearQueuedMessages: true);
                LogChatboxRelayUnavailable("VRChat OSC chat relay skipped Twitch chat because OSC / VRChat is not connected yet.");
                return;
            }

            try
            {
                await oscRouterService.SendToVrChatAsync(
                    vrChatOscClient.BuildChatboxInputPacket(relayMessage, sendImmediately: true, playNotification: false),
                    cancellationToken);
            }
            catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
            {
                return;
            }
            catch (Exception ex)
            {
                CancelChatboxRelayWorker(clearQueuedMessages: true);
                LogChatboxRelayUnavailable($"VRChat OSC chat relay skipped Twitch chat because {ex.Message}");
                return;
            }

            var hasMoreQueuedMessages = false;
            lock (stateGate)
            {
                hasMoreQueuedMessages = queuedChatboxRelayMessages.Count > 0;
            }

            if (!hasMoreQueuedMessages)
            {
                return;
            }

            await Task.Delay(TimeSpan.FromSeconds(delaySeconds), cancellationToken);
        }
    }

    private bool IsChatboxRelayEnabled()
    {
        lock (stateGate)
        {
            return activeConfiguration?.ChatboxOscEnabled == true;
        }
    }

    private bool AreRedeemsPaused()
    {
        lock (stateGate)
        {
            return activeConfiguration?.EmergencyRedeemStopEnabled == true;
        }
    }

    private void LogRedeemsPaused()
    {
        var shouldLog = false;

        lock (stateGate)
        {
            var now = DateTimeOffset.UtcNow;
            if (now >= nextRedeemPauseLogAt)
            {
                nextRedeemPauseLogAt = now.Add(RedeemPauseLogThrottle);
                shouldLog = true;
            }
        }

        if (shouldLog)
        {
            WriteLog("Redeems are paused, so Crystal Relay ignored a Twitch event.");
        }
    }

    private void PurgeQueuedRedeemWorkItems()
    {
        var droppedQueuedTriggers = 0;
        var droppedQueuedLaneActions = 0;
        var droppedQueuedScaleRedeems = 0;

        lock (stateGate)
        {
            droppedQueuedTriggers = queuedTriggers.Sum(entry => entry.Value.Count);
            droppedQueuedLaneActions = queuedLaneActions.Sum(entry => entry.Value.Count);
            (droppedQueuedScaleRedeems, _) = ClearQueuedAvatarScaleOperationsLocked(includeTests: false);
            queuedTriggers.Clear();
            queuedLaneActions.Clear();
        }

        var totalDropped = droppedQueuedTriggers + droppedQueuedLaneActions + droppedQueuedScaleRedeems;
        if (totalDropped > 0)
        {
            WriteLog($"Emergency redeem pause cleared {totalDropped} queued redeem action{(totalDropped == 1 ? string.Empty : "s")}.");
        }
    }

    private void CancelChatboxRelayWorker(bool clearQueuedMessages)
    {
        CancellationTokenSource? relayCancellation = null;

        lock (stateGate)
        {
            if (clearQueuedMessages)
            {
                queuedChatboxRelayMessages.Clear();
            }

            relayCancellation = chatboxRelayCancellation;
            chatboxRelayCancellation = null;
            chatboxRelayTask = null;
        }

        relayCancellation?.Cancel();
    }

    private void LogChatboxRelayUnavailable(string message)
    {
        var shouldLog = false;
        lock (stateGate)
        {
            var now = DateTimeOffset.UtcNow;
            if (now >= nextChatboxRelayUnavailableLogAt)
            {
                nextChatboxRelayUnavailableLogAt = now.Add(ChatboxRelayUnavailableLogThrottle);
                shouldLog = true;
            }
        }

        if (shouldLog)
        {
            WriteLog(message);
        }
    }

    private static bool ShouldBlockChatboxRelayMessage(BridgeChatMessage chatMessage)
    {
        return ChatboxRelayModerationFilter.ContainsBlockedRacialContent(chatMessage.UserDisplayName)
            || ChatboxRelayModerationFilter.ContainsBlockedRacialContent(chatMessage.MessageText);
    }

    private void LogBlockedChatboxRelayMessage()
    {
        var shouldLog = false;
        lock (stateGate)
        {
            var now = DateTimeOffset.UtcNow;
            if (now >= nextChatboxRelayBlockedLogAt)
            {
                nextChatboxRelayBlockedLogAt = now.Add(ChatboxRelayBlockedLogThrottle);
                shouldLog = true;
            }
        }

        if (shouldLog)
        {
            WriteLog("Blocked a Twitch chat relay message because it matched the zero-tolerance racial-content filter.");
        }
    }

    private string BuildNextChatboxRelayPostLocked()
    {
        var selectedLines = new List<string>(VrChatChatboxMaxLines);
        var totalCharacters = 0;

        while (queuedChatboxRelayMessages.Count > 0 && selectedLines.Count < VrChatChatboxMaxLines)
        {
            var nextLine = queuedChatboxRelayMessages.Peek().Line;
            if (string.IsNullOrWhiteSpace(nextLine))
            {
                queuedChatboxRelayMessages.Dequeue();
                continue;
            }

            var maxCharactersForLine = selectedLines.Count == 0
                ? VrChatChatboxMaxCharacters
                : VrChatChatboxMaxCharacters - totalCharacters - Environment.NewLine.Length;
            if (maxCharactersForLine <= 0)
            {
                break;
            }

            if (nextLine.Length > maxCharactersForLine)
            {
                if (selectedLines.Count > 0)
                {
                    break;
                }

                nextLine = nextLine[..maxCharactersForLine].TrimEnd();
                if (string.IsNullOrWhiteSpace(nextLine))
                {
                    queuedChatboxRelayMessages.Dequeue();
                    continue;
                }
            }

            queuedChatboxRelayMessages.Dequeue();
            selectedLines.Add(nextLine);
            totalCharacters += nextLine.Length;
            if (selectedLines.Count > 1)
            {
                totalCharacters += Environment.NewLine.Length;
            }
        }

        return selectedLines.Count == 0
            ? string.Empty
            : string.Join(Environment.NewLine, selectedLines);
    }

    private static string FormatChatboxRelayLine(BridgeChatMessage chatMessage)
    {
        var userDisplayName = string.IsNullOrWhiteSpace(chatMessage.UserDisplayName)
            ? "Twitch"
            : chatMessage.UserDisplayName.Trim();
        var messageText = chatMessage.MessageText
            .Replace(Environment.NewLine, " ", StringComparison.Ordinal)
            .Replace('\n', ' ')
            .Replace('\r', ' ')
            .Trim();

        return string.IsNullOrWhiteSpace(messageText)
            ? string.Empty
            : $"{userDisplayName}: {messageText}";
    }

    private void ObserveOscValue(OscObservedValue observedValue)
    {
        if (IsAvatarScaleAddress(observedValue.Address))
        {
            lock (stateGate)
            {
                avatarScaleValues[observedValue.Address] = observedValue;
            }

            AvatarScaleStatusChanged?.Invoke();
            return;
        }

        if (!observedValue.Address.StartsWith("/avatar/parameters/", StringComparison.Ordinal))
        {
            return;
        }

        lock (stateGate)
        {
            avatarParameterValues[observedValue.Address] = observedValue;
            if (observedValue.ParameterType == OscParameterType.Bool && observedValue.Value is bool boolValue)
            {
                localInstantToggleStates[observedValue.Address] = boolValue;
            }
        }
    }

    private static bool IsAvatarScaleAddress(string address) =>
        string.Equals(address, "/avatar/eyeheight", StringComparison.Ordinal)
        || string.Equals(address, "/avatar/eyeheightmin", StringComparison.Ordinal)
        || string.Equals(address, "/avatar/eyeheightmax", StringComparison.Ordinal)
        || string.Equals(address, "/avatar/eyeheightscalingallowed", StringComparison.Ordinal);

    private bool TryGetObservedAvatarParameterValue(
        string address,
        OscParameterType expectedType,
        out OscObservedValue observedValue)
    {
        lock (stateGate)
        {
            if (avatarParameterValues.TryGetValue(address, out var cachedValue)
                && TryCreateObservedValueFromExisting(address, expectedType, cachedValue, out _, out observedValue))
            {
                return true;
            }
        }

        observedValue = default!;
        return false;
    }

    private bool TryGetObservedBool(string address, out bool value)
    {
        lock (stateGate)
        {
            if (TryGetObservedBoolLocked(address, out value))
            {
                return true;
            }
        }

        value = false;
        return false;
    }

    private bool TryGetObservedBoolLocked(string address, out bool value)
    {
        if ((avatarParameterValues.TryGetValue(address, out var observedValue)
                || avatarScaleValues.TryGetValue(address, out observedValue))
            && observedValue.ParameterType == OscParameterType.Bool
            && observedValue.Value is bool boolValue)
        {
            value = boolValue;
            return true;
        }

        value = false;
        return false;
    }

    private bool TryGetLocalInstantToggleState(string address, out bool value)
    {
        lock (stateGate)
        {
            if (localInstantToggleStates.TryGetValue(address, out var localValue))
            {
                value = localValue;
                return true;
            }
        }

        value = false;
        return false;
    }

    private bool TryGetObservedInt(string address, out int value)
    {
        lock (stateGate)
        {
            if (avatarParameterValues.TryGetValue(address, out var observedValue)
                && observedValue.ParameterType == OscParameterType.Int
                && observedValue.Value is int intValue)
            {
                value = intValue;
                return true;
            }
        }

        value = 0;
        return false;
    }

    private bool TryGetObservedFloatLocked(string address, out double value)
    {
        if ((avatarParameterValues.TryGetValue(address, out var observedValue)
                || avatarScaleValues.TryGetValue(address, out observedValue))
            && observedValue.ParameterType == OscParameterType.Float
            && observedValue.Value is float floatValue)
        {
            value = floatValue;
            return true;
        }

        value = 0;
        return false;
    }

    private void WriteLog(string message) => LogWritten?.Invoke(message);

    private void ClearRuntimeState()
    {
        CancellationTokenSource? relayCancellation = null;
        CancellationTokenSource[] cooldownNotifications;
        CancellationTokenSource[] lockoutNotifications;
        CancellationTokenSource[] avatarSwitchLockoutNotifications;
        CancellationTokenSource[] avatarScaleEffectNotifications;
        CancellationTokenSource[] supporterGrowthCancellations;
        CancellationTokenSource? masterUnlockNotification;
        CancellationTokenSource? masterCooldownNotification;
        CancellationTokenSource[] movementLockCancellations;
        CancellationTokenSource[] desktopLockCancellations;
        SemaphoreSlim[] universalQueueGates;
        CancellationTokenSource? supporterOverrideCancellation = null;
        CancellationTokenSource? avatarScaleRestoreCancellation = null;
        lock (stateGate)
        {
            cooldowns.Clear();
            queuedTriggers.Clear();
            drainingQueuedRules.Clear();
            actionLanes.Clear();
            movementLockCancellations = [.. activeMovementLocks.Values.Select(lockState => lockState.Cancellation)];
            activeMovementLocks.Clear();
            desktopLockCancellations = [.. activeDesktopInputLocks.Values.Select(lockState => lockState.Cancellation)];
            activeDesktopInputLocks.Clear();
            queuedLaneActions.Clear();
            drainingQueuedLanes.Clear();
            pendingResets.Clear();
            recentMessageIds.Clear();
            nextRecentMessagePruneAt = DateTimeOffset.MinValue;
            avatarParameterValues.Clear();
            avatarScaleValues.Clear();
            localInstantToggleStates.Clear();
            chatBadgeImageUrls.Clear();
            chatEmoteImageUrls.Clear();
            chatEmoteImageUrlInsertionOrder.Clear();
            cachedChatEmoteSetIds.Clear();
            cachedChatEmoteSetIdInsertionOrder.Clear();
            thirdPartyChatEmoteImageUrls.Clear();
            thirdPartyChatEmoteCodeInsertionOrder.Clear();
            thirdPartyChatEmoteIndex = new Dictionary<char, IReadOnlyList<ThirdPartyChatEmoteEntry>>();
            nextThirdPartyChatEmoteRefreshAt = DateTimeOffset.MinValue;
            activeRuleLockouts.Clear();
            activeAvatarSwitchRuleLockouts.Clear();
            activeAvatarScaleEffects.Clear();
            activeAvatarScaleHeightSessions.Clear();
            pendingAvatarScaleHeightRestores.Clear();
            ClearQueuedAvatarScaleOperationsLocked(includeTests: true);
            activeAvatarScaleOperation = null;
            avatarScaleRestoreCancellation = avatarScaleRestoreSequenceCancellation;
            avatarScaleRestoreSequenceCancellation = null;
            activeAvatarScaleRestoreSequence = null;
            supporterOverrideBlockedRuleIds.Clear();
            supporterOverrideCancellation = activeSupporterOverride?.CompletionCancellation;
            activeSupporterOverride = null;
            avatarScaleMasterUnlockUntil = DateTimeOffset.MinValue;
            avatarScaleMasterCooldownUntil = DateTimeOffset.MinValue;
            masterUnlockNotification = avatarScaleMasterUnlockNotification;
            avatarScaleMasterUnlockNotification = null;
            masterCooldownNotification = avatarScaleMasterCooldownNotification;
            avatarScaleMasterCooldownNotification = null;
            queuedSupporterOverrides.Clear();
            nextSupporterOverrideQueueOrder = 0;
            universalTriggerGlobalDelays.Clear();
            universalTriggerUserDelays.Clear();
            universalQueueGates = [.. universalTriggerQueueGates.Values];
            universalTriggerQueueGates.Clear();
            supporterGrowthCancellations = [.. avatarScaleSupporterGrowthStates.Values
                .Select(state => state.SessionCancellation)
                .Where(cancellation => cancellation is not null)
                .Cast<CancellationTokenSource>()];
            avatarScaleSupporterGrowthStates.Clear();
            queuedChatboxRelayMessages.Clear();
            nextChatboxRelayUnavailableLogAt = DateTimeOffset.MinValue;
            relayCancellation = chatboxRelayCancellation;
            chatboxRelayCancellation = null;
            chatboxRelayTask = null;
            cooldownNotifications = [.. cooldownStateNotifications.Values];
            cooldownStateNotifications.Clear();
            lockoutNotifications = [.. lockoutStateNotifications.Values];
            lockoutStateNotifications.Clear();
            avatarSwitchLockoutNotifications = [.. avatarSwitchLockoutStateNotifications.Values];
            avatarSwitchLockoutStateNotifications.Clear();
            avatarScaleEffectNotifications = [.. avatarScaleEffectStateNotifications.Values];
            avatarScaleEffectStateNotifications.Clear();
        }

        relayCancellation?.Cancel();
        foreach (var notification in cooldownNotifications)
        {
            notification.Cancel();
            notification.Dispose();
        }

        foreach (var movementLockCancellation in movementLockCancellations)
        {
            movementLockCancellation.Cancel();
            movementLockCancellation.Dispose();
        }

        foreach (var desktopLockCancellation in desktopLockCancellations)
        {
            desktopLockCancellation.Cancel();
            desktopLockCancellation.Dispose();
        }

        supporterOverrideCancellation?.Cancel();
        supporterOverrideCancellation?.Dispose();
        avatarScaleRestoreCancellation?.Cancel();
        foreach (var universalQueueGate in universalQueueGates)
        {
            universalQueueGate.Dispose();
        }

        foreach (var supporterGrowthCancellation in supporterGrowthCancellations)
        {
            supporterGrowthCancellation.Cancel();
            supporterGrowthCancellation.Dispose();
        }

        _ = Task.Run(async () =>
        {
            try
            {
                await desktopInputLockService.ForceReleaseAsync(CancellationToken.None);
            }
            catch
            {
            }
        });

        foreach (var notification in lockoutNotifications)
        {
            notification.Cancel();
            notification.Dispose();
        }

        foreach (var notification in avatarSwitchLockoutNotifications)
        {
            notification.Cancel();
            notification.Dispose();
        }

        foreach (var notification in avatarScaleEffectNotifications)
        {
            notification.Cancel();
            notification.Dispose();
        }

        masterUnlockNotification?.Cancel();
        masterUnlockNotification?.Dispose();
        masterCooldownNotification?.Cancel();
        masterCooldownNotification?.Dispose();
    }

    private int EnqueueTrigger(TriggerRuleSnapshot rule, BridgeIncomingEvent bridgeEvent)
    {
        if (!queuedTriggers.TryGetValue(rule.Id, out var queue))
        {
            queue = new Queue<QueuedRuleTrigger>();
            queuedTriggers[rule.Id] = queue;
        }

        queue.Enqueue(new QueuedRuleTrigger(bridgeEvent));
        return queue.Count;
    }

    private bool TryEnqueueLaneAction(
        TriggerRuleSnapshot rule,
        BridgeIncomingEvent? bridgeEvent,
        bool isTest,
        out int queuedCount)
    {
        var laneKeys = GetActionLaneKeys(rule);
        if (laneKeys.Count == 0)
        {
            queuedCount = 0;
            return false;
        }

        var laneKey = string.Empty;

        lock (stateGate)
        {
            var now = DateTimeOffset.UtcNow;
            foreach (var candidateLaneKey in laneKeys)
            {
                if (actionLanes.TryGetValue(candidateLaneKey, out var activeLane) && activeLane.BusyUntil > now)
                {
                    laneKey = candidateLaneKey;
                    break;
                }

                actionLanes.Remove(candidateLaneKey);
            }

            if (string.IsNullOrWhiteSpace(laneKey))
            {
                queuedCount = 0;
                return false;
            }

            if (!queuedLaneActions.TryGetValue(laneKey, out var queue))
            {
                queue = new Queue<QueuedLaneAction>();
                queuedLaneActions[laneKey] = queue;
            }

            queue.Enqueue(new QueuedLaneAction(rule, bridgeEvent, isTest));
            queuedCount = queue.Count;
        }

        EnsureQueuedLaneDrain(laneKey);
        return true;
    }

    private void EnsureQueuedRuleDrain(Guid ruleId)
    {
        var cancellationToken = runtimeCancellation?.Token ?? CancellationToken.None;
        var shouldStart = false;

        lock (stateGate)
        {
            if (drainingQueuedRules.Add(ruleId))
            {
                shouldStart = true;
            }
        }

        if (!shouldStart)
        {
            return;
        }

        _ = Task.Run(async () =>
        {
            try
            {
                while (!cancellationToken.IsCancellationRequested)
                {
                    TriggerRuleSnapshot? ruleSnapshot;
                    QueuedRuleTrigger? queuedTrigger = null;
                    TimeSpan delay = TimeSpan.Zero;
                    var dropQueuedItems = false;
                    var dropCount = 0;

                    lock (stateGate)
                    {
                        if (!queuedTriggers.TryGetValue(ruleId, out var queue) || queue.Count == 0)
                        {
                            break;
                        }

                        ruleSnapshot = activeConfiguration?.Rules.FirstOrDefault(rule => rule.Id == ruleId);
                        if (activeConfiguration?.EmergencyRedeemStopEnabled == true)
                        {
                            dropQueuedItems = true;
                            dropCount = queue.Count;
                            queuedTriggers.Remove(ruleId);
                        }
                        else if (ruleSnapshot is null || !ruleSnapshot.IsEnabled)
                        {
                            dropQueuedItems = true;
                            dropCount = queue.Count;
                            queuedTriggers.Remove(ruleId);
                        }
                        else if (cooldowns.TryGetValue(ruleId, out var cooldownUntil) && cooldownUntil > DateTimeOffset.UtcNow)
                        {
                            delay = cooldownUntil - DateTimeOffset.UtcNow;
                        }
                        else if (TryGetTemporarilyDisabledUntilLocked(ruleId, DateTimeOffset.UtcNow, out var temporarilyDisabledUntil))
                        {
                            delay = temporarilyDisabledUntil - DateTimeOffset.UtcNow;
                        }
                        else
                        {
                            queuedTrigger = queue.Dequeue();
                            if (queue.Count == 0)
                            {
                                queuedTriggers.Remove(ruleId);
                            }
                        }
                    }

                    if (dropQueuedItems)
                    {
                        var reason = AreRedeemsPaused()
                            ? "redeems are paused"
                            : "that redeem is no longer enabled";
                        WriteLog($"Dropped {dropCount} queued trigger{(dropCount == 1 ? string.Empty : "s")} because {reason}.");
                        break;
                    }

                    if (delay > TimeSpan.Zero)
                    {
                        await Task.Delay(delay, cancellationToken);
                        continue;
                    }

                    if (ruleSnapshot is null || queuedTrigger is null)
                    {
                        continue;
                    }

                    try
                    {
                        if (IsTimedSupporterOverrideRule(ruleSnapshot))
                        {
                            await HandleTimedSupporterOverrideTriggerAsync(ruleSnapshot, queuedTrigger.Event, cancellationToken, queuedReplay: true);
                        }
                        else
                        {
                            await ExecuteRuleActionAsync(ruleSnapshot, queuedTrigger.Event, cancellationToken, isTest: false, queuedReplay: true, allowLaneQueue: true);
                        }
                    }
                    catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
                    {
                        throw;
                    }
                    catch (Exception ex)
                    {
                        WriteLog($"Queued trigger for '{ruleSnapshot.Name}' failed: {ex.Message}");
                    }
                }
            }
            catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
            {
            }
            finally
            {
                var restart = false;
                lock (stateGate)
                {
                    drainingQueuedRules.Remove(ruleId);
                    restart = queuedTriggers.TryGetValue(ruleId, out var queue) && queue.Count > 0;
                }

                if (restart)
                {
                    EnsureQueuedRuleDrain(ruleId);
                }
            }
        }, CancellationToken.None);
    }

    private void EnsureQueuedLaneDrain(string laneKey)
    {
        var cancellationToken = runtimeCancellation?.Token ?? CancellationToken.None;
        var shouldStart = false;

        lock (stateGate)
        {
            if (drainingQueuedLanes.Add(laneKey))
            {
                shouldStart = true;
            }
        }

        if (!shouldStart)
        {
            return;
        }

        _ = Task.Run(async () =>
        {
            try
            {
                while (!cancellationToken.IsCancellationRequested)
                {
                    QueuedLaneAction? queuedAction = null;
                    TimeSpan delay = TimeSpan.Zero;
                    var dropQueuedItems = false;
                    var dropCount = 0;

                    lock (stateGate)
                    {
                        if (activeConfiguration?.EmergencyRedeemStopEnabled == true
                            && queuedLaneActions.TryGetValue(laneKey, out var queuedLaneQueue)
                            && queuedLaneQueue.Count > 0)
                        {
                            dropQueuedItems = true;
                            dropCount = queuedLaneQueue.Count;
                            queuedLaneActions.Remove(laneKey);
                        }
                        else if (actionLanes.TryGetValue(laneKey, out var activeLane) && activeLane.BusyUntil > DateTimeOffset.UtcNow)
                        {
                            delay = activeLane.BusyUntil - DateTimeOffset.UtcNow;
                        }
                        else if (queuedLaneActions.TryGetValue(laneKey, out var queue) && queue.Count > 0)
                        {
                            queuedAction = queue.Dequeue();
                            if (queue.Count == 0)
                            {
                                queuedLaneActions.Remove(laneKey);
                            }
                        }
                        else
                        {
                            break;
                        }
                    }

                    if (dropQueuedItems)
                    {
                        WriteLog($"Dropped {dropCount} queued action{(dropCount == 1 ? string.Empty : "s")} because redeems are paused.");
                        break;
                    }

                    if (delay > TimeSpan.Zero)
                    {
                        await Task.Delay(delay, cancellationToken);
                        continue;
                    }

                    if (queuedAction is null)
                    {
                        continue;
                    }

                    var ruleToExecute = queuedAction.Rule;
                    if (!queuedAction.IsTest)
                    {
                        TriggerRuleSnapshot? currentRule;
                        lock (stateGate)
                        {
                            currentRule = activeConfiguration?.Rules.FirstOrDefault(rule => rule.Id == queuedAction.Rule.Id);
                        }

                        if (currentRule is null || !currentRule.IsEnabled)
                        {
                            WriteLog($"Dropped queued action for '{queuedAction.Rule.Name}' because that rule is no longer enabled.");
                            continue;
                        }

                        ruleToExecute = currentRule;
                    }

                    try
                    {
                        if (!queuedAction.IsTest && IsBitsOutfitSetTriggerRule(ruleToExecute))
                        {
                            WriteLog($"Starting queued Bits outfit Set Trigger '{ruleToExecute.Name}'.");
                        }

                        await ExecuteRuleActionAsync(ruleToExecute, queuedAction.Event, cancellationToken, queuedAction.IsTest, queuedReplay: true, allowLaneQueue: true);
                    }
                    catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
                    {
                        throw;
                    }
                    catch (Exception ex)
                    {
                        WriteLog($"Queued action for '{ruleToExecute.Name}' failed: {ex.Message}");
                    }
                }
            }
            catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
            {
            }
            finally
            {
                var restart = false;
                lock (stateGate)
                {
                    drainingQueuedLanes.Remove(laneKey);
                    restart = queuedLaneActions.TryGetValue(laneKey, out var queue) && queue.Count > 0;
                }

                if (restart)
                {
                    EnsureQueuedLaneDrain(laneKey);
                }
            }
        }, CancellationToken.None);
    }

    private static string DescribeDuration(double seconds)
    {
        if (seconds <= 0)
        {
            return "none";
        }

        var span = TimeSpan.FromSeconds(seconds);
        if (span.TotalMinutes >= 1)
        {
            return span.Seconds == 0
                ? $"{(int)span.TotalMinutes}m"
                : $"{(int)span.TotalMinutes}m {span.Seconds}s";
        }

        return $"{Math.Max(1, (int)Math.Round(span.TotalSeconds))}s";
    }

    private static string DescribeAvatarId(string? avatarId)
    {
        var normalizedAvatarId = avatarId?.Trim() ?? string.Empty;
        return string.IsNullOrWhiteSpace(normalizedAvatarId) ? "unknown" : normalizedAvatarId;
    }

    private static string DescribeLocalAvatarDataSource(string? sourcePath)
    {
        var fileName = System.IO.Path.GetFileName(sourcePath ?? string.Empty);
        return string.IsNullOrWhiteSpace(fileName)
            ? "LocalAvatarData\\usr_*\\avtr_*"
            : $"LocalAvatarData\\usr_*\\{fileName}";
    }

    private static string DescribeActionAddress(TriggerRuleSnapshot rule) => rule.ActionType switch
    {
        OscActionType.AvatarChange => "/avatar/change",
        OscActionType.AvatarRoulet => "/avatar/change",
        OscActionType.PlayerMovement => rule.MovementDirection switch
        {
            PlayerMovementDirection.Forward => "/input/MoveForward",
            PlayerMovementDirection.Backward => "/input/MoveBackward",
            PlayerMovementDirection.Left => "/input/MoveLeft",
            PlayerMovementDirection.Right => "/input/MoveRight",
            PlayerMovementDirection.Jump => "/input/Jump",
            PlayerMovementDirection.SpinLeft => "/input/LookLeft",
            PlayerMovementDirection.SpinRight => "/input/LookRight",
            PlayerMovementDirection.StopMovement => "/input movement lock",
            PlayerMovementDirection.StopTurning => "/input turning lock",
            PlayerMovementDirection.StopAll => "/input full lock",
            _ => "/input"
        },
        OscActionType.SetTrigger => "Set Trigger",
        _ => VrChatOscClient.NormalizeAvatarParameterAddress(rule.ParameterName)
    };

    private static int GetCooldownSeconds(TriggerRuleSnapshot rule) =>
        rule.ActionType == OscActionType.PlayerMovement ? 0 : Math.Max(0, rule.CooldownSeconds);

    private static string DescribeMovementAction(PlayerMovementDirection movementDirection) => movementDirection switch
    {
        PlayerMovementDirection.Forward => "Move Forward",
        PlayerMovementDirection.Backward => "Move Backward",
        PlayerMovementDirection.Left => "Move Left",
        PlayerMovementDirection.Right => "Move Right",
        PlayerMovementDirection.Jump => "Jump",
        PlayerMovementDirection.SpinLeft => "Spin Left",
        PlayerMovementDirection.SpinRight => "Spin Right",
        PlayerMovementDirection.StopMovement => "Stop Movement",
        PlayerMovementDirection.StopTurning => "Stop Turning",
        PlayerMovementDirection.StopAll => "Stop All",
        _ => "Movement"
    };

    private static string? GetString(JsonElement element, params string[] path)
    {
        var current = element;
        foreach (var segment in path)
        {
            if (!current.TryGetProperty(segment, out current))
            {
                return null;
            }
        }

        return current.ValueKind == JsonValueKind.Null ? null : current.GetString();
    }

    private static int GetInt(JsonElement element, params string[] path)
    {
        var current = element;
        foreach (var segment in path)
        {
            if (!current.TryGetProperty(segment, out current))
            {
                return 0;
            }
        }

        return current.ValueKind == JsonValueKind.Number && current.TryGetInt32(out var value) ? value : 0;
    }

    private static bool GetBoolean(JsonElement element, params string[] path)
    {
        var current = element;
        foreach (var segment in path)
        {
            if (!current.TryGetProperty(segment, out current))
            {
                return false;
            }
        }

        return current.ValueKind == JsonValueKind.True || (current.ValueKind == JsonValueKind.False ? false : current.GetBoolean());
    }

    private sealed record PendingResetState(
        Guid RuleId,
        string RuleName,
        TriggerRuleSnapshot Rule,
        IReadOnlyList<byte[]> Packets,
        CancellationTokenSource Cancellation,
        string AvatarChangeResetId,
        string AvatarChangeResetName,
        string SourceAvatarId,
        bool IsWaitingForSourceAvatarReturn,
        IReadOnlyList<OscObservedValue> ResetObservedValues,
        IReadOnlyList<string> MovementLaneKeys,
        Guid MovementLaneLeaseId)
    {
        public byte[]? Packet => Packets.FirstOrDefault();

        public bool HasPackets => Packets.Count > 0;
    }

    private sealed record SetTriggerPreparedAction(
        string Address,
        OscParameterType ParameterType,
        string TargetText,
        OscObservedValue TargetObservedValue);

    private sealed record ResolvedRuleAction
    {
        public ResolvedRuleAction(
            byte[] packet,
            byte[]? resetPacket,
            string displayValue,
            string avatarTargetId = "",
            string avatarTargetName = "",
            string avatarResetId = "",
            string avatarResetName = "")
            : this(
                [packet],
                resetPacket is null ? [] : [resetPacket],
                displayValue,
                avatarTargetId,
                avatarTargetName,
                avatarResetId,
                avatarResetName,
                [],
                [])
        {
        }

        public ResolvedRuleAction(
            IReadOnlyList<byte[]> packets,
            IReadOnlyList<byte[]> resetPackets,
            string displayValue,
            string avatarTargetId = "",
            string avatarTargetName = "",
            string avatarResetId = "",
            string avatarResetName = "",
            IReadOnlyList<OscObservedValue>? observedValues = null,
            IReadOnlyList<OscObservedValue>? resetObservedValues = null,
            SetTriggerRestorePlan? setTriggerRestorePlan = null)
        {
            Packets = packets.Count == 0 ? throw new InvalidOperationException("Resolved actions must include at least one OSC packet.") : packets;
            ResetPackets = resetPackets;
            DisplayValue = displayValue;
            AvatarTargetId = avatarTargetId;
            AvatarTargetName = avatarTargetName;
            AvatarResetId = avatarResetId;
            AvatarResetName = avatarResetName;
            ObservedValues = observedValues ?? [];
            ResetObservedValues = resetObservedValues ?? [];
            SetTriggerRestorePlan = setTriggerRestorePlan;
        }

        public IReadOnlyList<byte[]> Packets { get; }

        public IReadOnlyList<byte[]> ResetPackets { get; }

        public string DisplayValue { get; }

        public string AvatarTargetId { get; }

        public string AvatarTargetName { get; }

        public string AvatarResetId { get; }

        public string AvatarResetName { get; }

        public IReadOnlyList<OscObservedValue> ObservedValues { get; }

        public IReadOnlyList<OscObservedValue> ResetObservedValues { get; }

        public SetTriggerRestorePlan? SetTriggerRestorePlan { get; }

        public byte[] Packet => Packets[0];

        public byte[]? ResetPacket => ResetPackets.FirstOrDefault();

        public bool HasResetPackets => ResetPackets.Count > 0;
    }

    private sealed record SetTriggerRestorePlan(
        string SourceAvatarId,
        IReadOnlyDictionary<string, OscObservedValue> PreTriggerSnapshotValues,
        DateTime PreTriggerLastWriteTimeUtc,
        string SourcePath,
        IReadOnlyList<string> ConfiguredRestoreAddresses);

    private sealed record SetTriggerRestoreResolution(
        IReadOnlyList<byte[]> Packets,
        IReadOnlyList<OscObservedValue> ObservedValues);

    private sealed record AvatarRouletSelection(string AvatarId, string AvatarName);

    private sealed record SharedReturnAvatarSnapshot(string AvatarId, string AvatarName)
    {
        public static SharedReturnAvatarSnapshot Empty { get; } = new(string.Empty, string.Empty);
    }

    private readonly record struct ThirdPartyChatEmoteEntry(string Code, string ImageUrl);

    private sealed record ActiveMovementLaneState(
        Guid OwnerId,
        DateTimeOffset BusyUntil,
        Guid RuleId,
        bool IsSoftLock);

    private sealed class ActiveMovementSoftLockState
    {
        public ActiveMovementSoftLockState(
            Guid leaseId,
            Guid ruleId,
            string ruleName,
            CancellationTokenSource cancellation,
            Dictionary<string, byte[][]> packetsByLane,
            Dictionary<string, byte[][]> releasePacketsByLane)
        {
            LeaseId = leaseId;
            RuleId = ruleId;
            RuleName = ruleName;
            Cancellation = cancellation;
            PacketsByLane = packetsByLane;
            ReleasePacketsByLane = releasePacketsByLane;
        }

        public Guid LeaseId { get; }

        public Guid RuleId { get; }

        public string RuleName { get; }

        public CancellationTokenSource Cancellation { get; }

        public Dictionary<string, byte[][]> PacketsByLane { get; }

        public Dictionary<string, byte[][]> ReleasePacketsByLane { get; }
    }

    private sealed class ActiveDesktopInputLockState
    {
        public ActiveDesktopInputLockState(
            Guid leaseId,
            Guid ruleId,
            string ruleName,
            CancellationTokenSource cancellation,
            DateTimeOffset busyUntil,
            DesktopInputLockScope scope,
            Dictionary<string, byte[][]> packetsByLane)
        {
            LeaseId = leaseId;
            RuleId = ruleId;
            RuleName = ruleName;
            Cancellation = cancellation;
            BusyUntil = busyUntil;
            Scope = scope;
            PacketsByLane = packetsByLane;
        }

        public Guid LeaseId { get; }

        public Guid RuleId { get; }

        public string RuleName { get; }

        public CancellationTokenSource Cancellation { get; }

        public DateTimeOffset BusyUntil { get; }

        public DesktopInputLockScope Scope { get; }

        public Dictionary<string, byte[][]> PacketsByLane { get; }
    }

    private sealed record MovementInputLockPlan(
        string DisplayValue,
        DesktopInputLockScope Scope,
        IReadOnlyDictionary<string, byte[][]> PacketsByLane);

    private sealed class ActiveSupporterOverrideState
    {
        public ActiveSupporterOverrideState(
            TriggerRuleSnapshot rule,
            BridgeIncomingEvent @event,
            ResolvedRuleAction action,
            DateTimeOffset activeUntil,
            long queueOrder,
            CancellationTokenSource completionCancellation)
        {
            Rule = rule;
            Event = @event;
            Action = action;
            ActiveUntil = activeUntil;
            QueueOrder = queueOrder;
            CompletionCancellation = completionCancellation;
        }

        public TriggerRuleSnapshot Rule { get; }

        public BridgeIncomingEvent Event { get; set; }

        public ResolvedRuleAction Action { get; }

        public DateTimeOffset ActiveUntil { get; set; }

        public long QueueOrder { get; }

        public CancellationTokenSource CompletionCancellation { get; set; }
    }

    private sealed class RuntimeRuleIndex
    {
        public static RuntimeRuleIndex Empty { get; } = new([]);

        private readonly Dictionary<TwitchTriggerType, List<IndexedRule>> rulesByTriggerType = [];
        private readonly Dictionary<TwitchTriggerType, List<IndexedRule>> globalOverrideRulesByTriggerType = [];
        private readonly Dictionary<string, List<IndexedRule>> channelPointRulesByRewardId = new(StringComparer.Ordinal);
        private readonly Dictionary<string, List<IndexedRule>> channelPointRulesByRewardTitle = new(StringComparer.OrdinalIgnoreCase);
        private readonly Dictionary<string, List<IndexedRule>> chatCommandRulesByCommand = new(StringComparer.OrdinalIgnoreCase);
        private readonly Dictionary<Guid, List<IndexedRule>> rulesByAvatarProfileId = [];

        private RuntimeRuleIndex(IReadOnlyList<TriggerRuleSnapshot> rules)
        {
            for (var index = 0; index < rules.Count; index++)
            {
                var indexedRule = new IndexedRule(rules[index], index);
                Add(rulesByTriggerType, indexedRule.Rule.TriggerType, indexedRule);

                if (indexedRule.Rule.IsGlobalOverride)
                {
                    Add(globalOverrideRulesByTriggerType, indexedRule.Rule.TriggerType, indexedRule);
                }

                if (indexedRule.Rule.AvatarProfileId != Guid.Empty)
                {
                    Add(rulesByAvatarProfileId, indexedRule.Rule.AvatarProfileId, indexedRule);
                }

                if (indexedRule.Rule.TriggerType == TwitchTriggerType.ChannelPoints)
                {
                    var rewardId = indexedRule.Rule.ChannelPointRewardId?.Trim() ?? string.Empty;
                    if (!string.IsNullOrWhiteSpace(rewardId))
                    {
                        Add(channelPointRulesByRewardId, rewardId, indexedRule);
                    }

                    AddChannelPointTitleVariants(indexedRule.Rule.ChannelPointRewardTitle, indexedRule);
                }

                if (indexedRule.Rule.ChatCommandEnabled)
                {
                    var normalizedCommand = ChatCommandUtility.Normalize(indexedRule.Rule.ChatCommandText);
                    if (!string.IsNullOrWhiteSpace(normalizedCommand))
                    {
                        Add(chatCommandRulesByCommand, normalizedCommand, indexedRule);
                    }
                }
            }
        }

        public static RuntimeRuleIndex Create(IReadOnlyList<TriggerRuleSnapshot> rules) => new(rules);

        public IEnumerable<TriggerRuleSnapshot> GetGlobalOverrideRulesByTriggerType(TwitchTriggerType triggerType) =>
            globalOverrideRulesByTriggerType.TryGetValue(triggerType, out var rules)
                ? rules.Select(static indexedRule => indexedRule.Rule)
                : [];

        public IEnumerable<TriggerRuleSnapshot> GetChannelPointCandidates(string rewardId, string rewardTitle)
        {
            var candidatesById = new Dictionary<Guid, IndexedRule>();
            AddCandidates(channelPointRulesByRewardId, rewardId, candidatesById);
            AddCandidates(
                channelPointRulesByRewardTitle,
                ManagedRewardPresentation.NormalizeTitleIdentityKey(rewardTitle),
                candidatesById);

            return candidatesById.Values
                .OrderBy(static indexedRule => indexedRule.Order)
                .Select(static indexedRule => indexedRule.Rule);
        }

        public IEnumerable<TriggerRuleSnapshot> GetChatCommandCandidates(string messageText)
        {
            var normalizedCommand = ChatCommandUtility.Normalize(messageText);
            return !string.IsNullOrWhiteSpace(normalizedCommand)
                && chatCommandRulesByCommand.TryGetValue(normalizedCommand, out var rules)
                    ? rules.Select(static indexedRule => indexedRule.Rule)
                    : [];
        }

        public IEnumerable<TriggerRuleSnapshot> GetRulesByAvatarProfileId(Guid avatarProfileId) =>
            rulesByAvatarProfileId.TryGetValue(avatarProfileId, out var rules)
                ? rules.Select(static indexedRule => indexedRule.Rule)
                : [];

        private static void Add<TKey>(
            Dictionary<TKey, List<IndexedRule>> index,
            TKey key,
            IndexedRule indexedRule)
            where TKey : notnull
        {
            if (!index.TryGetValue(key, out var rules))
            {
                rules = [];
                index[key] = rules;
            }

            rules.Add(indexedRule);
        }

        private void AddChannelPointTitleVariants(string title, IndexedRule indexedRule)
        {
            var titleKey = ManagedRewardPresentation.NormalizeTitleIdentityKey(title);
            if (string.IsNullOrWhiteSpace(titleKey))
            {
                return;
            }

            Add(channelPointRulesByRewardTitle, titleKey, indexedRule);
        }

        private static void AddCandidates(
            Dictionary<string, List<IndexedRule>> index,
            string key,
            IDictionary<Guid, IndexedRule> candidatesById)
        {
            if (string.IsNullOrWhiteSpace(key)
                || !index.TryGetValue(key, out var candidates))
            {
                return;
            }

            foreach (var candidate in candidates)
            {
                candidatesById.TryAdd(candidate.Rule.Id, candidate);
            }
        }

        private sealed record IndexedRule(TriggerRuleSnapshot Rule, int Order);
    }

    private sealed class QueuedSupporterOverrideState
    {
        public QueuedSupporterOverrideState(
            TriggerRuleSnapshot rule,
            BridgeIncomingEvent @event,
            TimeSpan remainingDuration,
            long queueOrder)
        {
            Rule = rule;
            Event = @event;
            RemainingDuration = remainingDuration;
            QueueOrder = queueOrder;
        }

        public TriggerRuleSnapshot Rule { get; set; }

        public BridgeIncomingEvent Event { get; set; }

        public TimeSpan RemainingDuration { get; set; }

        public long QueueOrder { get; }
    }

    private sealed record ActiveRuleLockoutState(string SourceRuleName, DateTimeOffset ExpiresAt, IReadOnlyList<Guid> DisabledRuleIds);

    private sealed record ActiveAvatarScaleHeightSessionState(
        Guid RuleId,
        Guid SessionId,
        string RuleName,
        string OriginalAvatarId,
        double? RestoreHeightMeters,
        double CarriedHeightMeters,
        DateTimeOffset ActiveUntil);

    private sealed record PendingAvatarScaleHeightRestoreState(
        double RestoreHeightMeters,
        DateTimeOffset SourceActiveUntil,
        string SourceRuleName);

    private enum AvatarScaleAvatarChangeCarryoverMode
    {
        Auto,
        ForcePaidOverride,
        Skip
    }

    private sealed record ActiveAvatarScaleRestoreSequenceState(
        long SequenceId,
        string AvatarId,
        double RestoreHeightMeters,
        DateTimeOffset ActiveUntil,
        string SourceRuleName,
        double RestoreSmoothTransitionSeconds,
        bool IsTest);

    private enum AvatarScaleOperationPriority
    {
        IdleRestore = 0,
        TestSimulation = 1,
        LiveRedeem = 2,
        SupporterGrowth = 3
    }

    private sealed record ActiveAvatarScaleOperationTicket(
        long OperationId,
        Guid RuleId,
        string RuleName,
        AvatarScaleOperationPriority Priority,
        bool IsTest);

    private sealed record ActiveAvatarScaleOperationState(
        long OperationId,
        Guid RuleId,
        string RuleName,
        AvatarScaleOperationPriority Priority,
        bool IsTest,
        bool IsTransitionActive,
        DateTimeOffset StartedAt);

    private sealed record AvatarScaleCarryoverSnapshot(
        Guid SourceRuleId,
        Guid SessionId,
        string SourceRuleName,
        double CarriedHeightMeters,
        double FallbackRestoreHeightMeters,
        DateTimeOffset ActiveUntil);

    private sealed class ActiveAvatarScaleSupporterGrowthState
    {
        public double AddedHeightMeters { get; set; }

        public CancellationTokenSource? SessionCancellation { get; set; }
    }

    private sealed record QueuedRuleTrigger(BridgeIncomingEvent Event);

    private sealed class QueuedAvatarScaleOperation
    {
        public QueuedAvatarScaleOperation(
            AvatarScaleRuleSnapshot rule,
            UniversalIncomingEvent incomingEvent,
            bool isTest,
            TaskCompletionSource<bool>? completion)
        {
            Rule = rule;
            IncomingEvent = incomingEvent;
            IsTest = isTest;
            Completion = completion;
        }

        public AvatarScaleRuleSnapshot Rule { get; }

        public UniversalIncomingEvent IncomingEvent { get; }

        public bool IsTest { get; }

        public TaskCompletionSource<bool>? Completion { get; }

        public bool CooldownWaitLogged { get; set; }

        public bool TemporaryDisableWaitLogged { get; set; }
    }

    private sealed record QueuedLaneAction(TriggerRuleSnapshot Rule, BridgeIncomingEvent? Event, bool IsTest);

    private sealed record QueuedChatboxRelayLine(string Line);

    private sealed record BitsOutfitNameCandidate(
        TriggerRuleSnapshot Rule,
        string DisplayName,
        string NormalizedName,
        string CompactName);

    private sealed record BitsOutfitFuzzyCandidate(
        BitsOutfitNameCandidate Candidate,
        int Distance,
        double Score,
        int MaximumDistance);

    private sealed record BitsOutfitNameMatch(
        TriggerRuleSnapshot? Rule,
        string MatchKind,
        double Score,
        string? Diagnostic);

    private sealed record BridgeIncomingEvent(
        TwitchTriggerType TriggerType,
        string UserDisplayName,
        int Amount,
        string? RewardId,
        string? RewardTitle,
        string TriggerLabel,
        bool IsChatCommandTrigger,
        string ChatCommandText,
        string UserId,
        string UserLogin,
        IReadOnlyList<string> BadgeSetIds,
        bool UserIsModerator,
        bool UserIsBroadcaster)
    {
        public string RewardUserInput { get; init; } = string.Empty;

        public string MessageText { get; init; } = string.Empty;
    }

    private sealed record UniversalIncomingEvent(
        UniversalTriggerType TriggerType,
        string UserDisplayName,
        string UserId,
        string UserLogin,
        int Amount,
        string? RewardId,
        string? RewardTitle,
        string ChatMessageText,
        string SubscriptionTier,
        int SubscriptionMonths,
        IReadOnlyList<string> BadgeSetIds,
        bool UserIsModerator,
        bool UserIsBroadcaster)
    {
        public static UniversalIncomingEvent Test { get; } = new(
            UniversalTriggerType.ChatCommand,
            "Local Test",
            string.Empty,
            string.Empty,
            0,
            null,
            null,
            string.Empty,
            string.Empty,
            0,
            [],
            true,
            true);
    }
}

public sealed record BridgeChatMessage(
    string UserDisplayName,
    string UserLogin,
    string UserId,
    string MessageText,
    string UserColor,
    IReadOnlyList<string> BadgeImageUrls,
    IReadOnlyList<string> BadgeSetIds,
    IReadOnlyList<BridgeChatFragment> Fragments,
    DateTimeOffset ReceivedAt);

public sealed record AvatarScaleRuntimeStatus(
    double? CurrentHeightMeters,
    double? MinimumHeightMeters,
    double? MaximumHeightMeters,
    bool? ScalingAllowed);

public enum BridgeChatFragmentKind
{
    Text,
    Emote
}

public sealed record BridgeChatFragment(
    BridgeChatFragmentKind Kind,
    string Text,
    string ImageUrl);

internal sealed record ParsedBridgeChatFragment(
    BridgeChatFragmentKind Kind,
    string Text,
    string EmoteId);
