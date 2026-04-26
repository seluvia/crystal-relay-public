using System.Globalization;
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
    private static readonly TimeSpan RecentMessageRetention = TimeSpan.FromMinutes(30);
    private static readonly TimeSpan RecentMessagePruneInterval = TimeSpan.FromMinutes(1);
    private const int TwitchChatMessageMaxCharacters = 450;
    private const int VrChatChatboxMaxCharacters = 144;
    private const int VrChatChatboxMaxLines = 9;
    private const int MaxChatEmoteImageUrlCacheEntries = 2048;
    private const int MaxCachedChatEmoteSetIds = 512;
    private static readonly string[] ManagedSubscriptionTypes =
    [
        "channel.channel_points_custom_reward_redemption.add",
        "channel.cheer",
        "channel.subscribe",
        "channel.subscription.gift",
        "channel.subscription.message",
        "channel.chat.message",
        "stream.online",
        "stream.offline"
    ];

    private static string T(string sourceText) => LocalizationService.Translate(sourceText);

    private static string TF(string sourceFormat, params object[] args) => LocalizationService.Format(sourceFormat, args);

    private readonly TwitchApiClient twitchApiClient = new();
    private readonly VrChatOscClient vrChatOscClient = new();
    private readonly OscRouterService oscRouterService = new();
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
    private readonly Dictionary<string, bool> localInstantToggleStates = [];
    private readonly Dictionary<string, string> chatBadgeImageUrls = [];
    private readonly Dictionary<string, string> chatEmoteImageUrls = [];
    private readonly Queue<string> chatEmoteImageUrlInsertionOrder = [];
    private readonly HashSet<string> cachedChatEmoteSetIds = [];
    private readonly Queue<string> cachedChatEmoteSetIdInsertionOrder = [];
    private readonly Dictionary<Guid, ActiveRuleLockoutState> activeRuleLockouts = [];
    private readonly Dictionary<Guid, ActiveRuleLockoutState> activeAvatarSwitchRuleLockouts = [];
    private readonly Dictionary<Guid, string> lastAvatarRouletResultIds = [];
    private readonly Dictionary<Guid, CancellationTokenSource> cooldownStateNotifications = [];
    private readonly Dictionary<Guid, CancellationTokenSource> lockoutStateNotifications = [];
    private readonly Dictionary<Guid, CancellationTokenSource> avatarSwitchLockoutStateNotifications = [];
    private readonly Queue<QueuedChatboxRelayLine> queuedChatboxRelayMessages = [];
    private readonly List<QueuedSupporterOverrideState> queuedSupporterOverrides = [];
    private readonly HashSet<Guid> supporterOverrideBlockedRuleIds = [];

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
    private DateTimeOffset nextChatboxRelayUnavailableLogAt = DateTimeOffset.MinValue;
    private DateTimeOffset nextChatboxRelayBlockedLogAt = DateTimeOffset.MinValue;
    private DateTimeOffset nextRedeemPauseLogAt = DateTimeOffset.MinValue;
    private DateTimeOffset nextRecentMessagePruneAt = DateTimeOffset.MinValue;
    private ActiveSupporterOverrideState? activeSupporterOverride;
    private long nextSupporterOverrideQueueOrder;

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
    }

    private void ClearActiveConfiguration()
    {
        activeConfiguration = null;
        activeRuleIndex = RuntimeRuleIndex.Empty;
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
        RefreshActiveRuleLockoutsForConfiguration(configuration.Rules);
        RefreshActiveAvatarSwitchLockoutsForConfiguration(configuration.Rules);
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
        await oscRouterService.DisposeAsync();
    }

    // Main background loop for the live bridge. EventSub is the primary listener,
    // and the validation loop runs beside it to keep Twitch sessions healthy.
    private async Task RunBridgeAsync(CancellationToken cancellationToken)
    {
        var validationTask = Task.Run(() => RunValidationLoopAsync(cancellationToken), cancellationToken);

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

            try
            {
                var condition = BuildSubscriptionCondition(subscriptionType);
                var created = await twitchApiClient.CreateEventSubSubscriptionAsync(
                    broadcaster.AccessToken,
                    activeConfiguration.TwitchClientId,
                    sessionId,
                    subscriptionType,
                    "1",
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
        var configuration = activeConfiguration;
        var ruleIndex = activeRuleIndex;
        if (bridgeEvent is null || configuration is null)
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
            && bridgeEvent.TriggerType == TwitchTriggerType.ChannelPoints)
        {
            var rewardLabel = !string.IsNullOrWhiteSpace(bridgeEvent.RewardTitle)
                ? bridgeEvent.RewardTitle
                : bridgeEvent.RewardId ?? "unknown reward";
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

        return new
        {
            broadcaster_user_id = broadcaster.UserId
        };
    }

    private static bool IsSupporterOverrideRule(TriggerRuleSnapshot rule) =>
        rule.IsGlobalOverride && rule.TriggerType is TwitchTriggerType.Bits or TwitchTriggerType.Subscriptions;

    private static bool IsTimedSupporterOverrideRule(TriggerRuleSnapshot rule) =>
        IsSupporterOverrideRule(rule) && (rule.AmountScaledDurationEnabled || rule.DurationSeconds > 0);

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
            StreamStateChanged?.Invoke(true);
            WriteLog("Broadcaster is live on Twitch.");
            return true;
        }

        if (string.Equals(notification.SubscriptionType, "stream.offline", StringComparison.Ordinal))
        {
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

        var queuedLaneCount = 0;
        if (allowLaneQueue && TryEnqueueLaneAction(rule, bridgeEvent, isTest, out queuedLaneCount))
        {
            WriteLog(isTest
                ? $"Queued test trigger for '{rule.Name}' until the current movement action finishes. {queuedLaneCount} waiting."
                : $"Queued '{rule.Name}' until the current movement action finishes. {queuedLaneCount} waiting.");
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

            CancelCooldownStateNotification(rule.Id);
            UpdateActiveRuleLockoutState(rule);

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
        var laneKey = rule.ActionType == OscActionType.PlayerMovement
            ? GetMovementLaneKey(rule.MovementDirection)
            : null;
        var laneLeaseId = laneKey is null ? Guid.Empty : Guid.NewGuid();
        await oscRouterService.SendToVrChatAsync(action.Packet, cancellationToken);
        RememberAvatarParameterValue(rule, action.DisplayValue);
        UpdateActiveRuleLockoutState(rule);

        lock (stateGate)
        {
            if (laneKey is not null)
            {
                actionLanes[laneKey] = new ActiveMovementLaneState(
                    laneLeaseId,
                    DateTimeOffset.UtcNow.AddSeconds(Math.Max(1, rule.DurationSeconds)),
                    rule.Id,
                    false);
            }

            if (cooldownSeconds > 0)
            {
                cooldowns[rule.Id] = DateTimeOffset.UtcNow.AddSeconds(cooldownSeconds);
            }
            else if (!isTest)
            {
                cooldowns.Remove(rule.Id);
            }
        }

        if (cooldownSeconds > 0)
        {
            ScheduleCooldownStateNotification(rule.Id, TimeSpan.FromSeconds(cooldownSeconds));
        }
        else
        {
            CancelCooldownStateNotification(rule.Id);
        }

        if (rule.ActionType is OscActionType.AvatarChange or OscActionType.AvatarRoulet
            && !string.IsNullOrWhiteSpace(action.AvatarTargetId))
        {
            SetCurrentVrChatAvatar(action.AvatarTargetId, notify: true);
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

        var lockoutDurationSeconds = GetLockoutDurationSeconds(rule);
        UpdateActiveAvatarSwitchLockoutState(rule);
        var shouldNotifyManagedRewardState = cooldownSeconds > 0 || action.ResetPacket is not null || lockoutDurationSeconds > 0;
        if ((action.ResetPacket is not null && rule.DurationSeconds > 0)
            || lockoutDurationSeconds > 0)
        {
            var resetDelaySeconds = action.ResetPacket is not null
                ? Math.Max(1, rule.DurationSeconds)
                : lockoutDurationSeconds;
            ScheduleReset(rule, action, resetDelaySeconds, laneKey, laneLeaseId);
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
            PlayerMovementDirection.SpinLeft => "/input/LookLeft",
            PlayerMovementDirection.SpinRight => "/input/LookRight",
            _ => throw new InvalidOperationException($"Unsupported movement direction: {rule.MovementDirection}")
        };

        var holdSeconds = Math.Max(1, rule.DurationSeconds);
        return new ResolvedRuleAction(
            vrChatOscClient.BuildInputButtonPacket(inputAddress, true),
            vrChatOscClient.BuildInputButtonPacket(inputAddress, false),
            $"Held for {DescribeDuration(holdSeconds)}");
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
            SetCurrentVrChatAvatar(action.AvatarTargetId, notify: true);
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
                    blockedReset.Packet,
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
                var releasedLaneKeys = ReleaseMovementLanes(blockedReset.MovementLaneLeaseId, [blockedReset.MovementLaneKey]);
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
        if (resetPacket is not null)
        {
            await oscRouterService.SendToVrChatAsync(resetPacket, cancellationToken);
        }

        if (rule.ActionType == OscActionType.AvatarParameter
            && resetPacket is not null
            && !string.IsNullOrWhiteSpace(rule.ResetValue))
        {
            RememberAvatarParameterValue(rule, rule.ResetValue);
        }

        if (resetPacket is not null && !string.IsNullOrWhiteSpace(avatarResetId))
        {
            SetCurrentVrChatAvatar(avatarResetId, notify: true);
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
        PlayerMovementDirection.SpinLeft or PlayerMovementDirection.SpinRight => "player-movement-look",
        _ => null
    };

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

    private async Task SendPacketsToVrChatAsync(IEnumerable<byte[]> packets, CancellationToken cancellationToken)
    {
        foreach (var packet in packets)
        {
            await oscRouterService.SendToVrChatAsync(packet, cancellationToken);
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
                var resetPacket = rule.DurationSeconds > 0 && !string.IsNullOrWhiteSpace(rule.ResetValue)
                    ? vrChatOscClient.BuildAvatarParameterPacket(address, rule.ParameterType, rule.ResetValue)
                    : null;

                return new ResolvedRuleAction(
                    vrChatOscClient.BuildAvatarParameterPacket(address, rule.ParameterType, rule.ParameterValue),
                    resetPacket,
                    rule.ParameterValue);
            }
        }
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
        if (configuration is null || broadcaster is null || bot is null)
        {
            return;
        }

        try
        {
            bot = await EnsureAccountReadyAsync(bot, TwitchScopes.Bot, BridgeAccountRole.Bot, cancellationToken);
            var message = configuration.SupporterOverrideInfoMessageEnabled && IsSupporterOverrideRule(rule)
                ? BuildSupporterOverrideInfoBotMessage(configuration, rule, bridgeEvent, durationSecondsOverride)
                : BuildBotMessage(rule, bridgeEvent, resolvedValue, durationSecondsOverride);
            if (string.IsNullOrWhiteSpace(message))
            {
                return;
            }

            await twitchApiClient.SendChatMessageAsync(
                bot.AccessToken,
                configuration.TwitchClientId,
                broadcaster.UserId,
                bot.UserId,
                message,
                cancellationToken);
        }
        catch (Exception ex)
        {
            WriteLog($"Bot announcement failed: {ex.Message}");
        }
    }

    private void ScheduleReset(
        TriggerRuleSnapshot rule,
        ResolvedRuleAction action,
        double delaySeconds,
        string? laneKey = null,
        Guid laneLeaseId = default)
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
        var pendingReset = new PendingResetState(
            rule.Id,
            rule.Name,
            rule,
            action.ResetPacket,
            cancellation,
            action.AvatarResetId,
            action.AvatarResetName,
            rule.ActionType == OscActionType.AvatarParameter && rule.AvatarProfileId != Guid.Empty
                ? GetCurrentVrChatAvatarId()
                : string.Empty,
            false,
            laneKey ?? string.Empty,
            laneLeaseId);

        lock (stateGate)
        {
            pendingResets[rule.Id] = pendingReset;
        }

        // Per-rule queue draining is used for cooldowns and temporary disable windows.
        // It waits until the rule is allowed to fire again, then replays queued redeems in order.
        _ = Task.Run(async () =>
        {
            var keepPendingReset = false;
            try
            {
                await Task.Delay(TimeSpan.FromSeconds(Math.Max(1, delaySeconds)), cancellation.Token);

                if (action.ResetPacket is not null
                    && !string.IsNullOrWhiteSpace(pendingReset.SourceAvatarId)
                    && !string.Equals(GetCurrentVrChatAvatarId(), pendingReset.SourceAvatarId, StringComparison.Ordinal))
                {
                    keepPendingReset = MarkPendingResetWaitingForSourceAvatarReturn(pendingReset);
                    if (keepPendingReset)
                    {
                        WriteLog($"'{rule.Name}' finished while you were on another avatar, so Crystal Relay will clean it up when you return.");
                        return;
                    }
                }

                if (action.ResetPacket is not null)
                {
                    await oscRouterService.SendToVrChatAsync(action.ResetPacket, cancellation.Token);
                }
                if (rule.ActionType == OscActionType.AvatarParameter
                    && action.ResetPacket is not null
                    && !string.IsNullOrWhiteSpace(rule.ResetValue))
                {
                    RememberAvatarParameterValue(rule, rule.ResetValue);
                }
                if (action.ResetPacket is not null && !string.IsNullOrWhiteSpace(pendingReset.AvatarChangeResetId))
                {
                    SetCurrentVrChatAvatar(pendingReset.AvatarChangeResetId, notify: true);
                    SetSharedReturnAvatar(pendingReset.AvatarChangeResetId, pendingReset.AvatarChangeResetName, notify: true);
                }
                if (action.ResetPacket is not null)
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
                        var releasedLaneKeys = ReleaseMovementLanes(pendingReset.MovementLaneLeaseId, [pendingReset.MovementLaneKey]);
                        ManagedRewardAvailabilityChanged?.Invoke();
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
                    if (pendingReset.Packet is not null)
                    {
                        await oscRouterService.SendToVrChatAsync(pendingReset.Packet, CancellationToken.None);
                    }

                    if (pendingReset.Rule.ActionType == OscActionType.AvatarParameter
                        && pendingReset.Packet is not null
                        && !string.IsNullOrWhiteSpace(pendingReset.Rule.ResetValue))
                    {
                        RememberAvatarParameterValue(pendingReset.Rule, pendingReset.Rule.ResetValue);
                    }

                    if (pendingReset.Packet is not null)
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
            actionLanes.Clear();
            queuedLaneActions.Clear();
            drainingQueuedLanes.Clear();
            recentMessageIds.Clear();
            nextRecentMessagePruneAt = DateTimeOffset.MinValue;
            chatEmoteImageUrls.Clear();
            chatEmoteImageUrlInsertionOrder.Clear();
            cachedChatEmoteSetIds.Clear();
            cachedChatEmoteSetIdInsertionOrder.Clear();
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
                if (reset.Packet is not null)
                {
                    await oscRouterService.SendToVrChatAsync(reset.Packet, CancellationToken.None);
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

    private void RefreshActiveRuleLockoutsForConfiguration(IReadOnlyList<TriggerRuleSnapshot> rules)
    {
        var now = DateTimeOffset.UtcNow;
        var changed = false;
        var validRulesById = rules.ToDictionary(rule => rule.Id, rule => rule);
        List<CancellationTokenSource>? notificationsToDispose = null;

        lock (stateGate)
        {
            var before = GetTemporarilyDisabledRuleIdsLocked(now);

            foreach (var sourceRuleId in activeRuleLockouts.Keys.ToArray())
            {
                if (!validRulesById.TryGetValue(sourceRuleId, out var currentRule))
                {
                    activeRuleLockouts.Remove(sourceRuleId);
                    if (lockoutStateNotifications.Remove(sourceRuleId, out var removedNotification))
                    {
                        notificationsToDispose ??= [];
                        notificationsToDispose.Add(removedNotification);
                    }
                    continue;
                }

                var normalizedRuleIds = currentRule.TemporarilyDisabledRuleIds
                    .Where(ruleId => ruleId != currentRule.Id && validRulesById.ContainsKey(ruleId))
                    .Distinct()
                    .ToArray();

                var lockoutDurationSeconds = GetLockoutDurationSeconds(currentRule);
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
                    currentRule.Name,
                    existingState.ExpiresAt,
                    normalizedRuleIds);
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

    private static TriggerRuleSnapshot[] SelectMatchingRules(
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
                currentAvatarId,
                temporarilyDisabledRuleIds,
                avatarChangeTransitionActive),
            TwitchTriggerType.Bits or TwitchTriggerType.Subscriptions => SelectBestThresholdMatch(
                ruleIndex.GetGlobalOverrideRulesByTriggerType(bridgeEvent.TriggerType)
                    .Where(rule => rule.IsEnabled && !temporarilyDisabledRuleIds.Contains(rule.Id))
                    .ToArray(),
                bridgeEvent.Amount),
            _ => []
        };
    }

    private static TriggerRuleSnapshot[] SelectExactChannelPointMatch(
        RuntimeRuleIndex ruleIndex,
        string? rewardId,
        string? rewardTitle,
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

            return [rule];
        }

        return [];
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

    private static string NormalizeRewardTitle(string? rewardTitle) => rewardTitle?.Trim() ?? string.Empty;

    private static bool UserCanTriggerChatCommand(ChatCommandPermission permission, BridgeIncomingEvent bridgeEvent) => permission switch
    {
        ChatCommandPermission.Broadcaster => bridgeEvent.UserIsBroadcaster,
        ChatCommandPermission.Moderators => bridgeEvent.UserIsModerator || bridgeEvent.UserIsBroadcaster,
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

    private void SetCurrentVrChatAvatar(string avatarId, bool notify)
    {
        var normalizedAvatarId = avatarId?.Trim() ?? string.Empty;
        var changed = false;

        lock (stateGate)
        {
            if (string.Equals(currentVrChatAvatarId, normalizedAvatarId, StringComparison.Ordinal))
            {
                return;
            }

            currentVrChatAvatarId = normalizedAvatarId;
            avatarParameterValues.Clear();
            localInstantToggleStates.Clear();
            changed = true;
        }

        if (changed && !string.IsNullOrWhiteSpace(normalizedAvatarId))
        {
            ResumePendingAvatarScopedResetsForCurrentAvatar(normalizedAvatarId);
        }

        if (notify && changed && !string.IsNullOrWhiteSpace(normalizedAvatarId))
        {
            VrChatAvatarChanged?.Invoke(normalizedAvatarId);
        }
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
                false),

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
                false),

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
            return string.IsNullOrWhiteSpace(fallbackText)
                ? []
                : [new BridgeChatFragment(BridgeChatFragmentKind.Text, fallbackText, string.Empty)];
        }

        var parsedFragments = new List<ParsedBridgeChatFragment>();
        var missingEmoteSetIds = new HashSet<string>(StringComparer.Ordinal);

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
            var emoteSetId = GetString(fragmentNode, "emote", "emote_set_id") ?? string.Empty;
            if (string.IsNullOrWhiteSpace(emoteId))
            {
                parsedFragments.Add(new ParsedBridgeChatFragment(BridgeChatFragmentKind.Text, text, string.Empty));
                continue;
            }

            parsedFragments.Add(new ParsedBridgeChatFragment(BridgeChatFragmentKind.Emote, text, emoteId));

            if (!string.IsNullOrWhiteSpace(emoteSetId) && !IsChatEmoteSetCached(emoteSetId))
            {
                missingEmoteSetIds.Add(emoteSetId);
            }
        }

        if (missingEmoteSetIds.Count > 0)
        {
            await EnsureChatEmoteSetsCachedAsync(missingEmoteSetIds, cancellationToken);
        }

        var resolvedFragments = new List<BridgeChatFragment>(parsedFragments.Count);
        foreach (var fragment in parsedFragments)
        {
            if (fragment.Kind != BridgeChatFragmentKind.Emote)
            {
                resolvedFragments.Add(new BridgeChatFragment(fragment.Kind, fragment.Text, string.Empty));
                continue;
            }

            var imageUrl = ResolveChatEmoteImageUrl(fragment.EmoteId);
            resolvedFragments.Add(new BridgeChatFragment(
                string.IsNullOrWhiteSpace(imageUrl) ? BridgeChatFragmentKind.Text : BridgeChatFragmentKind.Emote,
                fragment.Text,
                imageUrl));
        }

        return resolvedFragments;
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
            var format = emote.Format.Contains("static", StringComparer.OrdinalIgnoreCase)
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

    private static string ExtractChatMessageText(JsonElement eventData)
    {
        if (!eventData.TryGetProperty("message", out var messageNode))
        {
            return string.Empty;
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

        lock (stateGate)
        {
            droppedQueuedTriggers = queuedTriggers.Sum(entry => entry.Value.Count);
            droppedQueuedLaneActions = queuedLaneActions.Sum(entry => entry.Value.Count);
            queuedTriggers.Clear();
            queuedLaneActions.Clear();
        }

        var totalDropped = droppedQueuedTriggers + droppedQueuedLaneActions;
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

    private bool TryGetObservedBool(string address, out bool value)
    {
        lock (stateGate)
        {
            if (avatarParameterValues.TryGetValue(address, out var observedValue)
                && observedValue.ParameterType == OscParameterType.Bool
                && observedValue.Value is bool boolValue)
            {
                value = boolValue;
                return true;
            }
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

    private void WriteLog(string message) => LogWritten?.Invoke(message);

    private void ClearRuntimeState()
    {
        CancellationTokenSource? relayCancellation = null;
        CancellationTokenSource[] cooldownNotifications;
        CancellationTokenSource[] lockoutNotifications;
        CancellationTokenSource[] avatarSwitchLockoutNotifications;
        CancellationTokenSource[] movementLockCancellations;
        CancellationTokenSource[] desktopLockCancellations;
        CancellationTokenSource? supporterOverrideCancellation = null;
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
            localInstantToggleStates.Clear();
            chatBadgeImageUrls.Clear();
            chatEmoteImageUrls.Clear();
            chatEmoteImageUrlInsertionOrder.Clear();
            cachedChatEmoteSetIds.Clear();
            cachedChatEmoteSetIdInsertionOrder.Clear();
            activeRuleLockouts.Clear();
            activeAvatarSwitchRuleLockouts.Clear();
            supporterOverrideBlockedRuleIds.Clear();
            supporterOverrideCancellation = activeSupporterOverride?.CompletionCancellation;
            activeSupporterOverride = null;
            queuedSupporterOverrides.Clear();
            nextSupporterOverrideQueueOrder = 0;
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
        if (rule.ActionType != OscActionType.PlayerMovement || IsSoftLockMovement(rule.MovementDirection))
        {
            queuedCount = 0;
            return false;
        }

        var laneKey = GetMovementLaneKey(rule.MovementDirection);
        if (string.IsNullOrWhiteSpace(laneKey))
        {
            queuedCount = 0;
            return false;
        }

        lock (stateGate)
        {
            if (!actionLanes.TryGetValue(laneKey, out var activeLane) || activeLane.BusyUntil <= DateTimeOffset.UtcNow)
            {
                actionLanes.Remove(laneKey);
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
                        WriteLog($"Dropped {dropCount} queued movement action{(dropCount == 1 ? string.Empty : "s")} because redeems are paused.");
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
                        await ExecuteRuleActionAsync(ruleToExecute, queuedAction.Event, cancellationToken, queuedAction.IsTest, queuedReplay: true, allowLaneQueue: false);
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
            PlayerMovementDirection.SpinLeft => "/input/LookLeft",
            PlayerMovementDirection.SpinRight => "/input/LookRight",
            PlayerMovementDirection.StopMovement => "/input movement lock",
            PlayerMovementDirection.StopTurning => "/input turning lock",
            PlayerMovementDirection.StopAll => "/input full lock",
            _ => "/input"
        },
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
        byte[]? Packet,
        CancellationTokenSource Cancellation,
        string AvatarChangeResetId,
        string AvatarChangeResetName,
        string SourceAvatarId,
        bool IsWaitingForSourceAvatarReturn,
        string MovementLaneKey,
        Guid MovementLaneLeaseId);

    private sealed record ResolvedRuleAction(
        byte[] Packet,
        byte[]? ResetPacket,
        string DisplayValue,
        string AvatarTargetId = "",
        string AvatarTargetName = "",
        string AvatarResetId = "",
        string AvatarResetName = "");

    private sealed record AvatarRouletSelection(string AvatarId, string AvatarName);

    private sealed record SharedReturnAvatarSnapshot(string AvatarId, string AvatarName)
    {
        public static SharedReturnAvatarSnapshot Empty { get; } = new(string.Empty, string.Empty);
    }

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
        private readonly Dictionary<string, List<IndexedRule>> channelPointRulesByRewardTitle = new(StringComparer.Ordinal);
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
            AddCandidates(channelPointRulesByRewardTitle, rewardTitle, candidatesById);

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
            var strippedTitle = ManagedRewardPresentation.StripPrefix(title);
            if (string.IsNullOrWhiteSpace(strippedTitle))
            {
                return;
            }

            Add(channelPointRulesByRewardTitle, strippedTitle, indexedRule);
            var managedTitle = ManagedRewardPresentation.BuildTitle(strippedTitle);
            if (!string.Equals(managedTitle, strippedTitle, StringComparison.Ordinal))
            {
                Add(channelPointRulesByRewardTitle, managedTitle, indexedRule);
            }
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

    private sealed record QueuedRuleTrigger(BridgeIncomingEvent Event);

    private sealed record QueuedLaneAction(TriggerRuleSnapshot Rule, BridgeIncomingEvent? Event, bool IsTest);

    private sealed record QueuedChatboxRelayLine(string Line);

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
        bool UserIsBroadcaster);
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
