using System.Collections.Concurrent;
using System.Diagnostics;
using System.Globalization;
using System.Net.Http;
using System.Text;
using System.Text.Json;
using VrcTwitchOscBridge.Models;
using VrcTwitchOscBridge.Services.Support;

namespace VrcTwitchOscBridge.Services;

public enum OscSessionMode
{
    Stopped,
    OscOnly,
    FullBridge
}

public enum RewardFireSaleContributionType
{
    Bits,
    ManagedReward
}

public sealed record RewardFireSaleContribution(
    RewardFireSaleContributionType Type,
    int Amount,
    string? RewardId,
    string? RewardTitle,
    string UserDisplayName);

/// <summary>
/// Long-running Twitch/OSC runtime for Crystal Relay.
/// This class owns the live bridge loop: EventSub listening, cooldowns, lockouts,
/// queued rule execution, chat relay, and OSC sends into VRChat.
/// </summary>
public sealed class BridgeCoordinator : IAsyncDisposable
{
    private static readonly TimeSpan AccessTokenRefreshLeadTime = TimeSpan.FromMinutes(15);
    private static readonly TimeSpan CachedTokenValidationGraceWindow = TimeSpan.FromMinutes(10);
    private static readonly TimeSpan PublicRefreshSessionWindow = TimeSpan.FromDays(30);
    private static readonly TimeSpan ChatboxRelayUnavailableLogThrottle = TimeSpan.FromSeconds(20);
    private static readonly TimeSpan ChatboxRelayBlockedLogThrottle = TimeSpan.FromSeconds(10);
    private static readonly TimeSpan RedeemPauseLogThrottle = TimeSpan.FromSeconds(15);
    private static readonly TimeSpan PowerUpInactiveAvatarLogThrottle = TimeSpan.FromSeconds(20);
    private static readonly TimeSpan AvatarScalePassiveCarryoverLogThrottle = TimeSpan.FromSeconds(15);
    private static readonly TimeSpan AvatarScaleCarryoverInitialSendDelay = TimeSpan.FromMilliseconds(2500);
    private static readonly TimeSpan AvatarScaleCarryoverApplyInterval = TimeSpan.FromMilliseconds(1000);
    private static readonly TimeSpan MovementSoftLockPulseInterval = TimeSpan.FromMilliseconds(200);
    private static readonly TimeSpan JumpPulsePressDuration = TimeSpan.FromMilliseconds(120);
    private static readonly TimeSpan JumpPulseInterval = TimeSpan.FromMilliseconds(650);
    private static readonly TimeSpan DevRandomAvatarScaleMinimumStepDuration = TimeSpan.FromMilliseconds(90);
    private static readonly TimeSpan DevRandomAvatarScaleMaximumStepDuration = TimeSpan.FromMilliseconds(850);
    private static readonly TimeSpan DevRandomAvatarScaleRestoreTransition = TimeSpan.FromMilliseconds(350);
    private static readonly TimeSpan DevRandomMovementMinimumSliceDuration = TimeSpan.FromSeconds(1);
    private static readonly TimeSpan DevRandomMovementMaximumSliceDuration = TimeSpan.FromSeconds(3);
    private static readonly TimeSpan RecentMessageRetention = TimeSpan.FromMinutes(30);
    private static readonly TimeSpan RecentMessagePruneInterval = TimeSpan.FromMinutes(1);
    private static readonly TimeSpan ThirdPartyChatEmoteRefreshInterval = TimeSpan.FromHours(6);
    private static readonly TimeSpan ThirdPartyChatEmoteRetryInterval = TimeSpan.FromMinutes(10);
    private static readonly TimeSpan ChatEmoteDiagnosticLogThrottle = TimeSpan.FromSeconds(15);
    private static readonly TimeSpan StreamOfflineConfirmationDelay = TimeSpan.FromSeconds(10);
    private static readonly TimeSpan BroadcasterLiveStateRetryDelay = TimeSpan.FromSeconds(2);
    private const int TwitchChatMessageMaxCharacters = 450;
    private const int VrChatChatboxMaxCharacters = 144;
    private const int VrChatChatboxMaxLines = 9;
    private const int MaxChatEmoteImageUrlCacheEntries = 2048;
    private const int MaxCachedChatEmoteSetIds = 512;
    private const int MaxThirdPartyChatEmoteEntries = 8192;
    private const int AvatarScaleSmoothUpdatesPerSecond = 60;
    private const int AvatarScaleSmoothMaxSteps = 600;
    private const int AvatarScaleCarryoverApplyAttemptCount = 4;
    private const int BroadcasterLiveStateCheckAttempts = 3;
    private const string BitsOutfitSetTriggerLaneKey = "set-trigger-bits-outfit";
    private const string AvatarSwitchLaneKey = "avatar-switch";
    private static readonly HashSet<string> ProtectedDevFireSaleBroadcasterIds = new(StringComparer.Ordinal)
    {
        "817261183"
    };
    private static readonly TimeSpan AvatarScaleQueuePollDelay = TimeSpan.FromMilliseconds(250);
    private static readonly TimeSpan ReversePairingHiddenPollDelay = TimeSpan.FromSeconds(1);
    private static readonly TimeSpan SetTriggerDiffObservationDelay = TimeSpan.FromSeconds(70);
    private static readonly TimeSpan SetTriggerPacketSpacing = TimeSpan.FromMilliseconds(80);
    private static readonly TimeSpan TriggerInfoAnnouncementPollInterval = TimeSpan.FromSeconds(15);
    private static readonly HttpClient ThirdPartyChatEmoteHttpClient = CreateThirdPartyChatEmoteHttpClient();
    private static readonly PlayerMovementDirection[] RandomMovementDirections =
    [
        PlayerMovementDirection.Forward,
        PlayerMovementDirection.Backward,
        PlayerMovementDirection.Left,
        PlayerMovementDirection.Right,
        PlayerMovementDirection.Jump,
        PlayerMovementDirection.SpinLeft,
        PlayerMovementDirection.SpinRight
    ];
    private static readonly string[] ManagedSubscriptionTypes =
    [
        "channel.channel_points_custom_reward_redemption.add",
        "channel.custom_power_up_redemption.add",
        "channel.cheer",
        "channel.subscribe",
        "channel.subscription.gift",
        "channel.subscription.message",
        "channel.raid",
        "channel.follow",
        "channel.chat.message",
        "channel.chat.message_delete",
        "channel.chat.clear_user_messages",
        "channel.chat.clear",
        "channel.suspicious_user.update",
        "channel.suspicious_user.message",
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
    private readonly VrChatApiClient vrChatApiClient = new();
    private readonly VrChatOscClient vrChatOscClient = new();
    private readonly OscRouterService oscRouterService = new();
    private readonly VrChatLocalAvatarDataService vrChatLocalAvatarDataService = new();
    private readonly CashPaymentProviderService cashPaymentProviderService = new();
    private readonly DesktopInputLockService desktopInputLockService;
    private readonly WorldCommandBlacklistService worldCommandBlacklistService;
    private readonly VrChatLocalOscCacheService localOscCacheService;
    private readonly IActivityResumeService activityResumeService;
    private WardrobeExecutorService? wardrobeExecutor;
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
    private readonly Dictionary<Guid, ActiveFloatRedeemSessionState> activeFloatRedeemSessions = [];
    private readonly Dictionary<Guid, ActiveFloatGlitchyRedeemSessionState> activeGlitchyRedeemSessions = [];
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
    private readonly Dictionary<Guid, ActiveRuleLockoutState> activeRuleUnlocks = [];
    private readonly Dictionary<Guid, ActiveRuleLockoutState> activeAvatarSwitchRuleLockouts = [];
    private readonly Dictionary<Guid, HashSet<string>> remainingAvatarRouletCandidateIdsByRuleId = [];
    private static readonly ConcurrentDictionary<Guid, List<int>> remainingAvatarRouletIndicesByRouletteId = new();
    private readonly Dictionary<Guid, CancellationTokenSource> cooldownStateNotifications = [];
    private readonly Dictionary<Guid, CancellationTokenSource> lockoutStateNotifications = [];
    private readonly Dictionary<Guid, CancellationTokenSource> avatarSwitchLockoutStateNotifications = [];
    private readonly Queue<QueuedChatboxRelayLine> queuedChatboxRelayMessages = [];
    private readonly List<QueuedSupporterOverrideState> queuedSupporterOverrides = [];
    private readonly Queue<QueuedAvatarSwitchState> queuedAvatarSwitches = [];
    private readonly HashSet<Guid> supporterOverrideBlockedRuleIds = [];
    private readonly Dictionary<Guid, DateTimeOffset> universalTriggerGlobalDelays = [];
    private readonly Dictionary<string, DateTimeOffset> universalTriggerUserDelays = new(StringComparer.Ordinal);
    private readonly Dictionary<Guid, SemaphoreSlim> universalTriggerQueueGates = [];
    private readonly SemaphoreSlim universalTriggerGlobalGate = new(1, 1);
    private readonly Dictionary<string, DateTimeOffset> triggerInfoCommandCooldowns = new(StringComparer.OrdinalIgnoreCase);
    private readonly Dictionary<Guid, DateTimeOffset> powerUpInactiveAvatarLogTimes = [];
    private readonly SemaphoreSlim worldCommandLookupGate = new(1, 1);
    private readonly Dictionary<Guid, ActiveAvatarScaleSupporterGrowthState> avatarScaleSupporterGrowthStates = [];
    private readonly Dictionary<Guid, HashSet<string>> avatarScaleFollowTriggeredUsers = [];
    private readonly Dictionary<Guid, DateTimeOffset> activeAvatarScaleEffects = [];
    private readonly Dictionary<Guid, CancellationTokenSource> avatarScaleEffectStateNotifications = [];
    private readonly Dictionary<Guid, ActiveAvatarScaleHeightSessionState> activeAvatarScaleHeightSessions = [];
    private readonly Dictionary<string, PendingAvatarScaleHeightRestoreState> pendingAvatarScaleHeightRestores = new(StringComparer.Ordinal);
    private readonly Queue<QueuedAvatarScaleOperation> queuedAvatarScaleOperations = [];
    // Avatar scale writes share one OSC parameter, so operations are ordered by priority:
    // transition lock, active supporter growth, live redeem, test/simulated effect, then idle/default restore.
    private ActiveAvatarScaleOperationState? activeAvatarScaleOperation;
    private ActiveAvatarScaleCarryoverState? activeAvatarScaleCarryover;
    private ActiveAvatarScaleRestoreSequenceState? activeAvatarScaleRestoreSequence;
    private PausedAvatarScaleTimerSnapshot? pausedDevAvatarScaleTimerSnapshot;
    private CancellationTokenSource? avatarScaleRestoreSequenceCancellation;
    private CancellationTokenSource? pendingStreamOfflineConfirmation;
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
    private bool hasResolvedBroadcasterLiveState;
    private DateTimeOffset nextTriggerInfoAnnouncementAt = DateTimeOffset.MinValue;
    private DateTimeOffset nextChatboxRelayUnavailableLogAt = DateTimeOffset.MinValue;
    private DateTimeOffset nextChatboxRelayBlockedLogAt = DateTimeOffset.MinValue;
    private DateTimeOffset nextRedeemPauseLogAt = DateTimeOffset.MinValue;
    private DateTimeOffset nextAvatarScalePassiveCarryoverLogAt = DateTimeOffset.MinValue;
    private DateTimeOffset nextRecentMessagePruneAt = DateTimeOffset.MinValue;
    private DateTimeOffset nextThirdPartyChatEmoteRefreshAt = DateTimeOffset.MinValue;
    private DateTimeOffset nextChatEmoteDiagnosticLogAt = DateTimeOffset.MinValue;
    private ActiveSupporterOverrideState? activeSupporterOverride;
    private bool drainingQueuedAvatarSwitches;
    private DateTimeOffset nextWorldCommandAllowedAt = DateTimeOffset.MinValue;
    private DateTimeOffset cachedWorldCommandResultExpiresAt = DateTimeOffset.MinValue;
    private string cachedWorldCommandUserId = string.Empty;
    private VrChatCurrentWorldLookupResult? cachedWorldCommandResult;
    private DateTimeOffset lastAvatarChangeAt = DateTimeOffset.MinValue;
    private static readonly TimeSpan AvatarChangeGracePeriod = TimeSpan.FromSeconds(15);
    private DateTimeOffset avatarScaleMasterUnlockUntil = DateTimeOffset.MinValue;
    private DateTimeOffset avatarScaleMasterCooldownUntil = DateTimeOffset.MinValue;
    private CancellationTokenSource? avatarScaleMasterUnlockNotification;
    private CancellationTokenSource? avatarScaleMasterCooldownNotification;
    private int suppressedChatEmoteDiagnosticLogs;
    private long nextSupporterOverrideQueueOrder;
    private long nextQueuedAvatarSwitchOrder;
    private long nextAvatarScaleOperationId;
    private long nextAvatarScaleRestoreSequenceId;
    private long nextAvatarScaleAvatarChangeSequenceId;

internal BridgeCoordinator(
        DesktopInputLockService desktopInputLockService,
        WorldCommandBlacklistService worldCommandBlacklistService,
        VrChatLocalOscCacheService localOscCacheService,
        IActivityResumeService? activityResumeService = null)
    {
        this.desktopInputLockService = desktopInputLockService;
        this.worldCommandBlacklistService = worldCommandBlacklistService;
        this.localOscCacheService = localOscCacheService;
        this.activityResumeService = activityResumeService ?? new ActivityResumeService();
        oscRouterService.LogWritten += WriteLog;
        oscRouterService.ObservedValueReceived += observedValue => ObserveOscValue(observedValue);
        oscRouterService.DiscoveryStateChanged += OnOscDiscoveryStateChanged;
        desktopInputLockService.EmergencyUnlockTriggered += HandleEmergencyDesktopInputUnlock;
    }

    private WardrobeExecutorService GetWardrobeExecutor()
    {
        if (wardrobeExecutor is null)
        {
            wardrobeExecutor = new WardrobeExecutorService(
                vrChatOscClient,
                oscRouterService,
                localOscCacheService,
                vrChatLocalAvatarDataService,
                WriteLog);
        }
        return wardrobeExecutor;
    }

    private void OnOscDiscoveryStateChanged(OscDiscoveryState state)
    {
        if (state == OscDiscoveryState.Discovered)
        {
            _ = TryResumePendingActivitiesAsync();
        }
    }

    public event Action<string>? LogWritten;

    public event Action<string>? StatusChanged;

    public event Action<BridgeAccountRole, TwitchAccountSnapshot>? AccountUpdated;

    public event Action<BridgeChatMessage>? ChatMessageReceived;

    public event Action<BridgeChatActivity>? ChatActivityReceived;

    public event Action<string>? VrChatAvatarChanged;

    public event Action<string, string>? SharedReturnAvatarChanged;

    public event Action<bool, bool>? StreamStateChanged;

    public event Action? ManagedRewardAvailabilityChanged;

    // Fires when a single rule's cooldown state flips (start or expire) so the
    // view model can PATCH just that one reward's color on Twitch. Carries only
    // the rule id; the view model decides ready-vs-cooldown color from the rule
    // configuration and the current cooldown set.
    public event Action<Guid>? RewardCooldownColorChanged;

    // Mirrors MainWindowViewModel.AvatarScaleMasterRewardOwnerId. The avatar-scaling
    // master reward is identified by a fixed Guid (no TriggerRule behind it), so the
    // per-reward color PATCH path uses this to route events to the master reward.
    private static readonly Guid AvatarScaleMasterRewardOwnerGuid = new("c69a2537-6c74-450f-9c5a-b6d9f04a7d95");

    // Fires only when the master reward's unlock window actually opens or closes
    // (not on every cooldown tick). The view model listens to this to queue a full
    // managed-reward sync so the child avatar-scale rewards become visible on Twitch
    // when the master reward is unlocked and get re-hidden once the unlock window ends.
    public event Action? AvatarScaleMasterRewardUnlockStateChanged;

    public event Action? AvatarScaleStatusChanged;

    public event Action<string>? VrChatOscAvatarChangeReceived;

    public event Func<RewardFireSaleContribution, bool>? RewardFireSaleContributionReceived;

    public event Func<DevFireSaleRequest, bool>? DevFireSaleRequested;

    public event Action? PauseCommandRequested;

    public event Action<string>? GroupToggleRequested;

    public event Action<string, bool>? RedeemControlRequested;

    public bool IsRunning => runtimeTask is { IsCompleted: false };

    public bool IsOscActive => oscRouterService.IsRunning;

    public bool HasDiscoveredVrChat => oscRouterService.HasDiscoveredVrChat;

    public OscSessionMode SessionMode => oscSessionMode;

    public OscDiscoveryState DiscoveryState => oscRouterService.DiscoveryState;

    public string CurrentVrChatAvatarId => GetCurrentVrChatAvatarId();

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

            foreach (var ruleId in activeFloatRedeemSessions.Keys)
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

    public IReadOnlyCollection<Guid> GetActiveFloatBoostMaximumReachedRuleIds()
    {
        lock (stateGate)
        {
            return [.. activeFloatRedeemSessions
                .Where(session => session.Value.BoostMaximumReached)
                .Select(session => session.Key)];
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

        await oscRouterService.StartAsync(GetOscSubscriptionRules(configuration), CancellationToken.None);

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
        await oscRouterService.StartAsync(GetOscSubscriptionRules(configuration), cancellationToken);
        oscRouterService.UpdateRuleSubscriptions(GetOscSubscriptionRules(configuration));
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

        if (!StringComparer.Ordinal.Equals(activeConfiguration.TwitchClientId, configuration.TwitchClientId))
        {
            return false;
        }

        if (!Equals(activeConfiguration.CashPayments, configuration.CashPayments))
        {
            return false;
        }

        var currentBroadcaster = broadcaster ?? activeConfiguration.Broadcaster;
        if (currentBroadcaster is null || configuration.Broadcaster is null)
        {
            return false;
        }

        return CanReuseBroadcasterSession(currentBroadcaster, configuration.Broadcaster);
    }

    private static bool CanReuseBroadcasterSession(TwitchAccountSnapshot? current, TwitchAccountSnapshot? next)
    {
        if (current is null || next is null)
        {
            return false;
        }

        return StringComparer.Ordinal.Equals(current.UserId, next.UserId)
            && StringComparer.Ordinal.Equals(current.AccessToken, next.AccessToken)
            && ScopeSetsEqual(current.Scopes, next.Scopes);
    }

    private static bool ScopeSetsEqual(IEnumerable<string>? currentScopes, IEnumerable<string>? nextScopes)
    {
        var currentSet = (currentScopes ?? [])
            .Where(scope => !string.IsNullOrWhiteSpace(scope))
            .ToHashSet(StringComparer.OrdinalIgnoreCase);
        var nextSet = (nextScopes ?? [])
            .Where(scope => !string.IsNullOrWhiteSpace(scope))
            .ToHashSet(StringComparer.OrdinalIgnoreCase);
        return currentSet.SetEquals(nextSet);
    }

    private void SetActiveConfiguration(BridgeRuntimeConfiguration configuration)
    {
        activeConfiguration = configuration;
        activeRuleIndex = RuntimeRuleIndex.Create(configuration.Rules);
        RefreshAvatarScaleMasterStateForConfiguration(configuration.AvatarScaleMasterReward);
    }

    private static IReadOnlyList<TriggerRuleSnapshot> GetOscSubscriptionRules(BridgeRuntimeConfiguration configuration)
    {
        var cashTriggerRules = configuration.CashPaymentRules
            .Select(rule => rule.TriggerAction)
            .OfType<TriggerRuleSnapshot>();
        return configuration.Rules.Concat(cashTriggerRules).ToArray();
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
        oscRouterService.UpdateRuleSubscriptions(GetOscSubscriptionRules(configuration));
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

        await ExecuteRuleActionAsync(rule, null, cancellationToken, isTest: true, queuedReplay: false, allowLaneQueue: true, isResuming: false);
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
            await ExecuteAvatarScaleRuleAsync(rule, UniversalIncomingEvent.Test, isTest: true, cancellationToken, isResuming: false);
            return;
        }

        await QueueAvatarScaleRuleExecutionAsync(
            rule,
            UniversalIncomingEvent.Test,
            isTest: true,
            waitForCompletion: true,
            cancellationToken);
    }

    public async Task SendTestCashPaymentRuleAsync(CashPaymentRuleSnapshot rule, CancellationToken cancellationToken = default)
    {
        if (!IsOscActive)
        {
            throw new InvalidOperationException("OSC is not running yet, so Crystal Relay cannot send a cash payment test to VRChat.");
        }

        if (rule.ActionKind == CashPaymentActionKind.AvatarScaling && rule.ScaleAction is not null)
        {
            await SendTestAvatarScaleRuleAsync(rule.ScaleAction, cancellationToken);
            return;
        }

        if (rule.TriggerAction is null)
        {
            throw new InvalidOperationException("Finish the cash payment action setup before testing it.");
        }

        await SendTestRuleAsync(rule.TriggerAction, cancellationToken);
    }

    public async Task SendTestPowerUpRuleAsync(PowerUpRuleSnapshot rule, CancellationToken cancellationToken = default)
    {
        if (!IsOscActive)
        {
            throw new InvalidOperationException("OSC is not running yet, so Crystal Relay cannot send a Power Up test to VRChat.");
        }

        if (rule.ActionKind == PowerUpActionKind.AvatarScaling && rule.ScaleAction is not null)
        {
            await SendTestAvatarScaleRuleAsync(rule.ScaleAction, cancellationToken);
            return;
        }

        if (rule.TriggerAction is null)
        {
            throw new InvalidOperationException("Finish the Power Up action setup before testing it.");
        }

        await SendTestRuleAsync(rule.TriggerAction, cancellationToken);
    }

    public async Task SimulateBitsAsync(int bitsAmount, string messageText, CancellationToken cancellationToken = default)
    {
        if (!IsOscActive)
        {
            throw new InvalidOperationException("OSC is not running yet, so Crystal Relay cannot simulate Bits to VRChat.");
        }

        var amount = Math.Max(1, bitsAmount);
        var normalizedMessage = messageText?.Trim() ?? string.Empty;
        var bridgeEvent = new BridgeIncomingEvent(
            TwitchTriggerType.Bits,
            "Local Test",
            amount,
            null,
            null,
            "Simulated Bits",
            false,
            string.Empty,
            string.Empty,
            string.Empty,
            [],
            false,
            true)
        {
            MessageText = normalizedMessage
        };
        var universalEvent = new UniversalIncomingEvent(
            UniversalTriggerType.Bits,
            "Local Test",
            string.Empty,
            string.Empty,
            amount,
            null,
            null,
            normalizedMessage,
            string.Empty,
            0,
            [],
            false,
            true);

        WriteLog($"Simulating {amount:N0} Bits{(string.IsNullOrWhiteSpace(normalizedMessage) ? string.Empty : $" with message '{normalizedMessage}'")}.");
        await HandleSimulatedTwitchEventAsync(bridgeEvent, universalEvent, cancellationToken);
    }

    public async Task SimulateSubscriptionAsync(
        int subscriptionCount,
        string subscriptionTier,
        bool isGiftSubscription,
        CancellationToken cancellationToken = default)
    {
        if (!IsOscActive)
        {
            throw new InvalidOperationException("OSC is not running yet, so Crystal Relay cannot simulate subscriptions to VRChat.");
        }

        var amount = Math.Max(1, subscriptionCount);
        var normalizedTier = subscriptionTier?.Trim() ?? string.Empty;
        var label = isGiftSubscription ? "Simulated Gift Sub" : "Simulated Subscription";
        var bridgeEvent = new BridgeIncomingEvent(
            TwitchTriggerType.Subscriptions,
            isGiftSubscription ? "Local Gifter" : "Local Subscriber",
            amount,
            null,
            null,
            label,
            false,
            string.Empty,
            string.Empty,
            string.Empty,
            [],
            false,
            true)
        {
            SubscriptionTier = normalizedTier
        };
        var universalEvent = new UniversalIncomingEvent(
            isGiftSubscription ? UniversalTriggerType.GiftSubscription : UniversalTriggerType.Subscription,
            isGiftSubscription ? "Local Gifter" : "Local Subscriber",
            string.Empty,
            string.Empty,
            amount,
            null,
            null,
            string.Empty,
            normalizedTier,
            0,
            [],
            false,
            true);

        WriteLog($"Simulating {amount:N0} {(isGiftSubscription ? "gift " : string.Empty)}subscription{(amount == 1 ? string.Empty : "s")}{(string.IsNullOrWhiteSpace(normalizedTier) ? string.Empty : $" at tier {normalizedTier}")}.");
        await HandleSimulatedTwitchEventAsync(bridgeEvent, universalEvent, cancellationToken);
    }

    public async Task SimulateCashPaymentAsync(
        CashPaymentProvider provider,
        decimal amount,
        string currencyCode,
        string messageText,
        CancellationToken cancellationToken = default)
    {
        if (!IsOscActive)
        {
            throw new InvalidOperationException("OSC is not running yet, so Crystal Relay cannot simulate a cash payment to VRChat.");
        }

        var normalizedAmount = Math.Max(0m, amount);
        var normalizedCurrencyCode = string.IsNullOrWhiteSpace(currencyCode) ? "USD" : currencyCode.Trim().ToUpperInvariant();
        var paymentEvent = new CashPaymentEvent(
            Enum.IsDefined(provider) ? provider : CashPaymentProvider.StreamElements,
            $"local-test:{Guid.NewGuid():N}",
            "Local Supporter",
            normalizedAmount,
            normalizedCurrencyCode,
            messageText?.Trim() ?? string.Empty,
            DateTimeOffset.UtcNow);

        WriteLog($"Simulating {DescribeCashPaymentProvider(paymentEvent.Provider)} cash payment of {paymentEvent.Amount:0.##} {paymentEvent.CurrencyCode}.");
        await HandleCashPaymentEventAsync(paymentEvent, cancellationToken);
    }

    /// <summary>
    /// Executes a Wardrobe outfit snapshot. Returns true if applied, false if blocked.
    /// </summary>
    public async Task<bool> ExecuteWardrobeOutfitAsync(
        WardrobeOutfitSnapshot snapshot,
        CancellationToken cancellationToken = default)
    {
        if (!IsOscActive)
        {
            throw new InvalidOperationException("OSC is not running yet, so Crystal Relay cannot send a wardrobe outfit test to VRChat.");
        }

        var vrChatUserId = activeConfiguration?.VrChatSession?.UserId?.Trim() ?? string.Empty;
        if (string.IsNullOrWhiteSpace(vrChatUserId))
        {
            WriteLog("Wardrobe outfit blocked: VRChat user ID not configured.");
            return false;
        }

        var applied = await GetWardrobeExecutor().ExecuteOutfitAsync(snapshot, vrChatUserId, cancellationToken);
        if (applied)
        {
            WriteLog($"Wardrobe outfit '{snapshot.Name}' applied successfully.");
        }

        return applied;
    }

    /// <summary>
    /// Resolves and executes a Wardrobe outfit from a Twitch redemption.
    /// Supports individual outfit rewards and master reward with typed outfit name.
    /// Returns true if a Wardrobe outfit was executed, false otherwise.
    /// </summary>
    private async Task<bool> TryExecuteWardrobeFromRedemptionAsync(
        BridgeRuntimeConfiguration configuration,
        string rewardId,
        string? redemptionInputText,
        CancellationToken cancellationToken)
    {
        foreach (var profile in configuration.AvatarProfiles)
        {
            if (!profile.UseWardrobeMode || !profile.IsEnabled) continue;
            if (string.IsNullOrWhiteSpace(profile.AvatarId)) continue;

            // Check individual outfit rewards
            foreach (var outfit in profile.WardrobeOutfits)
            {
                if (!outfit.IsEnabled) continue;
                if (!string.Equals(outfit.TwitchRewardId, rewardId, StringComparison.Ordinal)) continue;

                if (!BridgeRuntimeConfiguration.TryToWardrobeSnapshot(outfit, profile, out var snapshot)) continue;

                var applied = await ExecuteWardrobeOutfitAsync(snapshot, cancellationToken);
                if (applied)
                {
                    WriteLog($"Wardrobe outfit '{outfit.Name}' fired from individual reward.");
                }
                return true;
            }

            // Check master reward
            if (profile.UseWardrobeMasterReward
                && !string.IsNullOrWhiteSpace(profile.WardrobeMasterRewardId)
                && string.Equals(profile.WardrobeMasterRewardId, rewardId, StringComparison.Ordinal))
            {
                if (string.IsNullOrWhiteSpace(redemptionInputText))
                {
                    WriteLog("Wardrobe master reward redeemed but no outfit name was typed.");
                    return true;
                }

                var inputName = redemptionInputText.Trim();
                var matchedOutfit = profile.WardrobeOutfits
                    .FirstOrDefault(o => o.IsEnabled
                        && string.Equals(o.Name, inputName, StringComparison.OrdinalIgnoreCase));

                if (matchedOutfit is null)
                {
                    WriteLog($"Wardrobe master reward: No outfit found matching '{inputName}'.");
                    return true;
                }

                if (!BridgeRuntimeConfiguration.TryToWardrobeSnapshot(matchedOutfit, profile, out var masterSnapshot)) continue;

                var masterApplied = await ExecuteWardrobeOutfitAsync(masterSnapshot, cancellationToken);
                if (masterApplied)
                {
                    WriteLog($"Wardrobe outfit '{matchedOutfit.Name}' fired from master reward.");
                }
                return true;
            }
        }

        return false;
    }

    private async Task<bool> TryHandleDevChatCommandAsync(
        BridgeIncomingEvent bridgeEvent,
        CancellationToken cancellationToken)
    {
        if (!DevChatCommandParser.IsDevCommandMessage(bridgeEvent.ChatCommandText))
        {
            return false;
        }

        if (!DevChatCommandParser.IsAuthorizedUser(bridgeEvent.UserLogin, bridgeEvent.UserDisplayName))
        {
            return true;
        }

        if (!DevChatCommandParser.TryParse(bridgeEvent.ChatCommandText, out var command, out var diagnostic))
        {
            WriteLog($"Dev chat command skipped: {diagnostic}");
            return true;
        }

        if (command.Kind == DevChatCommandKind.FireSale && IsDevFireSaleProtectedBroadcaster())
        {
            WriteLog("Dev Fire Sale command skipped for a protected broadcaster channel.");
            return true;
        }

        try
        {
            switch (command.Kind)
            {
                case DevChatCommandKind.RelativeAvatarScale:
                    await ExecuteDevRelativeAvatarScaleAsync(
                        command.RelativeHeightMeters,
                        command.DurationSeconds,
                        command.TransitionSeconds,
                        bridgeEvent.UserDisplayName,
                        cancellationToken);
                    break;

                case DevChatCommandKind.RandomAvatarScale:
                    await ExecuteDevRandomAvatarScaleAsync(
                        command.MinimumHeightMeters,
                        command.MaximumHeightMeters,
                        command.DurationSeconds,
                        bridgeEvent.UserDisplayName,
                        cancellationToken);
                    break;

                case DevChatCommandKind.Movement:
                    await ExecuteDevMovementAsync(
                        command.MovementDirection,
                        command.DurationSeconds,
                        bridgeEvent.UserDisplayName,
                        cancellationToken);
                    break;

                case DevChatCommandKind.RandomMovementSequence:
                    await ExecuteDevRandomMovementSequenceAsync(
                        command.DurationSeconds,
                        bridgeEvent.UserDisplayName,
                        cancellationToken);
                    break;

                case DevChatCommandKind.FireSale:
                    RequestDevFireSale(
                        command.DiscountPercent,
                        command.DurationSeconds,
                        bridgeEvent.UserDisplayName);
                    break;
            }
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            throw;
        }
        catch (Exception ex)
        {
            WriteLog($"Dev chat command '{command.CommandText}' failed: {ex.Message}");
        }

        return true;
    }

    private async Task ExecuteDevRelativeAvatarScaleAsync(
        double relativeHeightMeters,
        int durationSeconds,
        double transitionSeconds,
        string userDisplayName,
        CancellationToken cancellationToken)
    {
        if (!IsOscActive)
        {
            WriteLog("Dev avatar scale command skipped because OSC is not running yet.");
            return;
        }

        var scalingAllowed = await TryGetAvatarScalingAllowedAsync(cancellationToken);
        if (scalingAllowed == false)
        {
            WriteLog("Dev avatar scale command skipped because VRChat reports /avatar/eyeheightscalingallowed is false.");
            return;
        }

        var devDuration = TimeSpan.FromSeconds(Math.Max(1, durationSeconds));
        var devRuleName = relativeHeightMeters >= 0 ? "Dev Grow" : "Dev Shrink";
        var previousHeight = await TryGetCurrentAvatarHeightAsync(cancellationToken) ?? 1.6;
        var targetHeight = Math.Clamp(
            previousHeight + relativeHeightMeters,
            AvatarScaleRule.AdvancedMinimumHeightMeters,
            AvatarScaleRule.AdvancedMaximumHeightMeters);
        WriteLog(
            $"Dev avatar scale '{devRuleName}' captured current height {previousHeight:0.###}m, target {targetHeight:0.###}m, duration {DescribeDuration(durationSeconds)}, and transition {DescribeDuration(transitionSeconds)}.");
        var pausedTimer = PauseActiveAvatarScaleTimerForDev(devRuleName, devDuration);
        if (pausedTimer is not null)
        {
            WriteLog($"Dev avatar scale '{devRuleName}' paused {pausedTimer.SourceDescription} at held height {pausedTimer.CarriedHeightMeters:0.###}m with {DescribeDuration(pausedTimer.Remaining.TotalSeconds)} remaining.");
        }

        try
        {
            var operation = await WaitForAvatarScaleOperationSlotAsync(
                Guid.Empty,
                devRuleName,
                AvatarScaleOperationPriority.LiveRedeem,
                isTest: true,
                cancellationToken);
            if (operation is null)
            {
                return;
            }

            try
            {
                if (!await SendAvatarHeightForOperationAsync(
                        operation,
                        targetHeight,
                        transitionSeconds,
                        cancellationToken))
                {
                    return;
                }

                WriteLog(
                    $"{userDisplayName} ran dev avatar scale {relativeHeightMeters:+0.###;-0.###;0}m for {DescribeDuration(durationSeconds)} with {DescribeDuration(transitionSeconds)} transition.");
                await Task.Delay(devDuration, cancellationToken);
            }
            finally
            {
                EndAvatarScaleOperation(operation);
            }
        }
        finally
        {
            if (!cancellationToken.IsCancellationRequested)
            {
                if (pausedTimer is not null)
                {
                    await ResumePausedAvatarScaleTimerAfterDevAsync(pausedTimer, cancellationToken);
                }
                else
                {
                    await RestoreDevAvatarScaleHeightAsync(devRuleName, previousHeight, transitionSeconds, cancellationToken);
                }
            }
        }
    }

    private async Task ExecuteDevRandomAvatarScaleAsync(
        double minimumHeightMeters,
        double maximumHeightMeters,
        int durationSeconds,
        string userDisplayName,
        CancellationToken cancellationToken)
    {
        if (!IsOscActive)
        {
            WriteLog("Dev random avatar scale command skipped because OSC is not running yet.");
            return;
        }

        var scalingAllowed = await TryGetAvatarScalingAllowedAsync(cancellationToken);
        if (scalingAllowed == false)
        {
            WriteLog("Dev random avatar scale command skipped because VRChat reports /avatar/eyeheightscalingallowed is false.");
            return;
        }

        var clampedMinimumHeight = Math.Clamp(
            minimumHeightMeters,
            AvatarScaleRule.AdvancedMinimumHeightMeters,
            AvatarScaleRule.AdvancedMaximumHeightMeters);
        var clampedMaximumHeight = Math.Clamp(
            maximumHeightMeters,
            AvatarScaleRule.AdvancedMinimumHeightMeters,
            AvatarScaleRule.AdvancedMaximumHeightMeters);
        if (clampedMaximumHeight <= clampedMinimumHeight)
        {
            WriteLog("Dev random avatar scale command skipped because the requested height range collapsed after clamping.");
            return;
        }

        var devDuration = TimeSpan.FromSeconds(Math.Max(1, durationSeconds));
        const string devRuleName = "Dev Random Scale";
        var previousHeight = await TryGetCurrentAvatarHeightAsync(cancellationToken) ?? 1.6;
        WriteLog(
            $"Dev random avatar scale captured current height {previousHeight:0.###}m, target range {clampedMinimumHeight:0.###}m-{clampedMaximumHeight:0.###}m, and duration {DescribeDuration(durationSeconds)}.");
        var pausedTimer = PauseActiveAvatarScaleTimerForDev(devRuleName, devDuration);
        if (pausedTimer is not null)
        {
            WriteLog($"Dev random avatar scale paused {pausedTimer.SourceDescription} at held height {pausedTimer.CarriedHeightMeters:0.###}m with {DescribeDuration(pausedTimer.Remaining.TotalSeconds)} remaining.");
        }

        try
        {
            var operation = await WaitForAvatarScaleOperationSlotAsync(
                Guid.Empty,
                devRuleName,
                AvatarScaleOperationPriority.LiveRedeem,
                isTest: true,
                cancellationToken);
            if (operation is null)
            {
                return;
            }

            try
            {
                WriteLog(
                    $"{userDisplayName} ran dev random avatar scale from {clampedMinimumHeight:0.###}m to {clampedMaximumHeight:0.###}m for {DescribeDuration(durationSeconds)}.");
                await RunDevRandomAvatarScaleSequenceAsync(
                    operation,
                    clampedMinimumHeight,
                    clampedMaximumHeight,
                    devDuration,
                    cancellationToken);
            }
            finally
            {
                EndAvatarScaleOperation(operation);
            }
        }
        finally
        {
            if (!cancellationToken.IsCancellationRequested)
            {
                if (pausedTimer is not null)
                {
                    await ResumePausedAvatarScaleTimerAfterDevAsync(pausedTimer, cancellationToken);
                }
                else
                {
                    await RestoreDevAvatarScaleHeightAsync(
                        devRuleName,
                        previousHeight,
                        DevRandomAvatarScaleRestoreTransition.TotalSeconds,
                        cancellationToken);
                }
            }
        }
    }

    private async Task RunDevRandomAvatarScaleSequenceAsync(
        ActiveAvatarScaleOperationTicket operation,
        double minimumHeightMeters,
        double maximumHeightMeters,
        TimeSpan duration,
        CancellationToken cancellationToken)
    {
        await RunRandomAvatarScaleSequenceAsync(
            operation,
            minimumHeightMeters,
            maximumHeightMeters,
            duration,
            maximumSmoothTransitionSeconds: 0.75,
            afterFirstSuccessfulSend: null,
            cancellationToken);
    }

    private async Task RunRandomAvatarScaleSequenceAsync(
        ActiveAvatarScaleOperationTicket operation,
        double minimumHeightMeters,
        double maximumHeightMeters,
        TimeSpan duration,
        double maximumSmoothTransitionSeconds,
        Action? afterFirstSuccessfulSend,
        CancellationToken cancellationToken)
    {
        var endAt = DateTimeOffset.UtcNow.Add(duration);
        Action? firstSendCallback = afterFirstSuccessfulSend;
        void RunAfterFirstSuccessfulSend()
        {
            firstSendCallback?.Invoke();
            firstSendCallback = null;
        }

        while (DateTimeOffset.UtcNow < endAt)
        {
            var remaining = endAt - DateTimeOffset.UtcNow;
            if (remaining <= TimeSpan.Zero)
            {
                break;
            }

            var shouldStutter = remaining >= TimeSpan.FromMilliseconds(250)
                && Random.Shared.NextDouble() < 0.22;
            if (shouldStutter)
            {
                if (!await RunRandomAvatarScaleStutterAsync(
                        operation,
                        minimumHeightMeters,
                        maximumHeightMeters,
                        endAt,
                        RunAfterFirstSuccessfulSend,
                        cancellationToken))
                {
                    return;
                }

                continue;
            }

            var targetHeight = PickDevRandomAvatarScaleHeight(minimumHeightMeters, maximumHeightMeters);
            var minimumTransitionSeconds = Math.Min(0.12, maximumSmoothTransitionSeconds);
            var transitionSeconds = maximumSmoothTransitionSeconds > 0
                && remaining >= TimeSpan.FromMilliseconds(350)
                && Random.Shared.NextDouble() < 0.38
                ? Math.Min(
                    NextDevRandomDouble(minimumTransitionSeconds, maximumSmoothTransitionSeconds),
                    Math.Max(0.05, remaining.TotalSeconds * 0.65))
                : 0;
            if (!await SendAvatarHeightForOperationAsync(
                    operation,
                    targetHeight,
                    transitionSeconds,
                    cancellationToken,
                    RunAfterFirstSuccessfulSend))
            {
                return;
            }

            var holdDuration = NextDevRandomDuration(
                DevRandomAvatarScaleMinimumStepDuration,
                DevRandomAvatarScaleMaximumStepDuration);
            await DelayUntilDevRandomAvatarScaleStepAsync(holdDuration, endAt, cancellationToken);
        }
    }

    private async Task<bool> RunRandomAvatarScaleStutterAsync(
        ActiveAvatarScaleOperationTicket operation,
        double minimumHeightMeters,
        double maximumHeightMeters,
        DateTimeOffset endAt,
        Action afterFirstSuccessfulSend,
        CancellationToken cancellationToken)
    {
        var range = maximumHeightMeters - minimumHeightMeters;
        var baseHeight = PickDevRandomAvatarScaleHeight(minimumHeightMeters, maximumHeightMeters);
        var pulseCount = Random.Shared.Next(2, 5);
        for (var index = 0; index < pulseCount; index++)
        {
            if (DateTimeOffset.UtcNow >= endAt)
            {
                break;
            }

            var offset = NextDevRandomDouble(-range * 0.2, range * 0.2);
            var targetHeight = index % 2 == 0
                ? baseHeight
                : Math.Clamp(baseHeight + offset, minimumHeightMeters, maximumHeightMeters);
            if (!await SendAvatarHeightForOperationAsync(
                    operation,
                    targetHeight,
                    0,
                    cancellationToken,
                    afterFirstSuccessfulSend))
            {
                return false;
            }

            var holdDuration = NextDevRandomDuration(
                TimeSpan.FromMilliseconds(45),
                TimeSpan.FromMilliseconds(140));
            await DelayUntilDevRandomAvatarScaleStepAsync(holdDuration, endAt, cancellationToken);
        }

        return true;
    }

    private static double PickDevRandomAvatarScaleHeight(double minimumHeightMeters, double maximumHeightMeters) =>
        NextDevRandomDouble(minimumHeightMeters, maximumHeightMeters);

    private static double NextDevRandomDouble(double minimum, double maximum)
    {
        if (maximum <= minimum)
        {
            return minimum;
        }

        return minimum + (Random.Shared.NextDouble() * (maximum - minimum));
    }

    private static TimeSpan NextDevRandomDuration(TimeSpan minimum, TimeSpan maximum)
    {
        if (maximum <= minimum)
        {
            return minimum;
        }

        return TimeSpan.FromMilliseconds(NextDevRandomDouble(minimum.TotalMilliseconds, maximum.TotalMilliseconds));
    }

    private static async Task DelayUntilDevRandomAvatarScaleStepAsync(
        TimeSpan delay,
        DateTimeOffset endAt,
        CancellationToken cancellationToken)
    {
        var remaining = endAt - DateTimeOffset.UtcNow;
        if (remaining <= TimeSpan.Zero)
        {
            return;
        }

        var cappedDelay = delay < remaining ? delay : remaining;
        if (cappedDelay > TimeSpan.Zero)
        {
            await Task.Delay(cappedDelay, cancellationToken);
        }
    }

    private async Task ExecuteDevMovementAsync(
        PlayerMovementDirection movementDirection,
        int durationSeconds,
        string userDisplayName,
        CancellationToken cancellationToken)
    {
        await ExecuteDevMovementAsync(
            movementDirection,
            TimeSpan.FromSeconds(Math.Max(1, durationSeconds)),
            userDisplayName,
            cancellationToken);
    }

    private async Task ExecuteDevMovementAsync(
        PlayerMovementDirection movementDirection,
        TimeSpan devDuration,
        string userDisplayName,
        CancellationToken cancellationToken)
    {
        if (!IsOscActive)
        {
            WriteLog("Dev movement command skipped because OSC is not running yet.");
            return;
        }

        var rule = new TriggerRule
        {
            Name = $"Dev {DescribeMovementAction(movementDirection)}",
            TriggerType = TwitchTriggerType.ChannelPoints,
            ActionType = OscActionType.PlayerMovement,
            MovementDirection = movementDirection,
            DurationSeconds = Math.Max(1d, Math.Ceiling(devDuration.TotalSeconds))
        };
        var snapshot = BridgeRuntimeConfiguration.CreateManualTestSnapshot(
            rule,
            isGlobalOverride: true,
            profile: null);

        var movementLabel = DescribeMovementAction(movementDirection);
        var movementLaneKey = GetMovementLaneKey(movementDirection);
        if (!string.IsNullOrWhiteSpace(movementLaneKey))
        {
            var pausedTimer = PauseActiveMovementTimerForDev(
                movementLaneKey,
                devDuration,
                out var waitReason);
            if (!string.IsNullOrWhiteSpace(waitReason))
            {
                WriteLog($"Dev movement '{movementLabel}' is waiting because {waitReason}");
                await SendTestRuleAsync(snapshot, cancellationToken);
                return;
            }

            if (pausedTimer is not null)
            {
                WriteLog($"Dev movement '{movementLabel}' paused {pausedTimer.SourceDescription} with {DescribeDuration(pausedTimer.Remaining.TotalSeconds)} remaining.");
                await SendPacketsToVrChatAsync(pausedTimer.Action.ResetPackets, cancellationToken);
                try
                {
                    await RunDevMovementOverlayAsync(
                        snapshot,
                        devDuration,
                        userDisplayName,
                        drainQueuedLanesOnRelease: false,
                        cancellationToken);
                }
                finally
                {
                    if (!cancellationToken.IsCancellationRequested)
                    {
                        await ResumePausedMovementTimerAfterDevAsync(pausedTimer, cancellationToken);
                    }
                }

                return;
            }
        }

        await RunDevMovementOverlayAsync(
            snapshot,
            devDuration,
            userDisplayName,
            drainQueuedLanesOnRelease: true,
            cancellationToken);
    }

    private async Task ExecuteDevRandomMovementSequenceAsync(
        int durationSeconds,
        string userDisplayName,
        CancellationToken cancellationToken)
    {
        if (!IsOscActive)
        {
            WriteLog("Dev random movement command skipped because OSC is not running yet.");
            return;
        }

        var remainingSeconds = Math.Max(1, durationSeconds);
        PlayerMovementDirection? previousDirection = null;
        var sliceIndex = 1;
        WriteLog($"{userDisplayName} ran dev random movement for {DescribeDuration(remainingSeconds)}.");
        while (remainingSeconds > 0)
        {
            cancellationToken.ThrowIfCancellationRequested();
            var movementDirection = PickRandomMovementDirection(previousDirection);
            previousDirection = movementDirection;
            var sliceSeconds = Math.Min(
                Random.Shared.Next(
                    (int)DevRandomMovementMinimumSliceDuration.TotalSeconds,
                    (int)DevRandomMovementMaximumSliceDuration.TotalSeconds + 1),
                remainingSeconds);
            WriteLog(
                $"Dev random movement slice {sliceIndex}: {DescribeMovementAction(movementDirection)} for {DescribeDuration(sliceSeconds)}.");
            await ExecuteDevMovementAsync(
                movementDirection,
                TimeSpan.FromSeconds(sliceSeconds),
                userDisplayName,
                cancellationToken);
            remainingSeconds -= sliceSeconds;
            sliceIndex++;
        }

        WriteLog("Dev random movement finished; movement/look inputs were released and paused Crystal Relay movement timers were resumed when needed.");
    }

    private PausedAvatarScaleTimerSnapshot? PauseActiveAvatarScaleTimerForDev(
        string devRuleName,
        TimeSpan devDuration)
    {
        CancellationTokenSource? pausedCancellation = null;
        PausedAvatarScaleTimerSnapshot? snapshot = null;
        TimeSpan effectNotificationDelay = TimeSpan.Zero;
        var shouldNotifyManagedRewards = false;

        lock (stateGate)
        {
            var now = DateTimeOffset.UtcNow;
            if (activeAvatarScaleRestoreSequence is not { } sequence || sequence.ActiveUntil <= now)
            {
                return null;
            }

            var carryover = activeAvatarScaleCarryover is { } activeCarryover
                && activeCarryover.RestoreSequenceId == sequence.SequenceId
                && activeCarryover.ActiveUntil > now
                    ? activeCarryover
                    : null;
            var ruleId = carryover?.SourceRuleId ?? Guid.Empty;
            var sessionId = carryover?.SourceSessionId ?? Guid.Empty;
            var remaining = sequence.ActiveUntil - now;
            var effectWasActive = false;
            if (ruleId != Guid.Empty
                && activeAvatarScaleEffects.TryGetValue(ruleId, out var activeEffectUntil)
                && activeEffectUntil > now)
            {
                effectWasActive = true;
                var extendedUntil = now.Add(devDuration).Add(remaining);
                activeAvatarScaleEffects[ruleId] = extendedUntil;
                effectNotificationDelay = extendedUntil - now;
                shouldNotifyManagedRewards = true;
            }

            snapshot = new PausedAvatarScaleTimerSnapshot(
                ruleId,
                sessionId,
                sequence.SequenceId,
                sequence.AvatarId,
                sequence.CarriedHeightMeters,
                sequence.RestoreHeightMeters,
                sequence.SourceRuleName,
                sequence.RestoreSmoothTransitionSeconds,
                sequence.RestoreToPaidGrowthIfActive,
                sequence.IsTest,
                now.Add(devDuration).Add(remaining),
                remaining,
                CountQueuedLiveAvatarScaleOperationsLocked(),
                DescribeAvatarScaleTimerSource(ruleId, sequence.SourceRuleName),
                FindAvatarScaleRuleSnapshot(ruleId),
                effectWasActive);
            pausedDevAvatarScaleTimerSnapshot = snapshot;

            pausedCancellation = avatarScaleRestoreSequenceCancellation;
            avatarScaleRestoreSequenceCancellation = null;
            activeAvatarScaleRestoreSequence = null;

            if (carryover is not null
                && activeAvatarScaleCarryover?.CarryoverId == carryover.CarryoverId)
            {
                activeAvatarScaleCarryover = null;
            }

            if (ruleId != Guid.Empty
                && activeAvatarScaleHeightSessions.TryGetValue(ruleId, out var activeSession)
                && (sessionId == Guid.Empty || activeSession.SessionId == sessionId))
            {
                activeAvatarScaleHeightSessions.Remove(ruleId);
            }
        }

        pausedCancellation?.Cancel();
        if (snapshot.Rule is not null)
        {
            ApplyAvatarScaleRuleLockoutUntil(
                snapshot.Rule,
                DateTimeOffset.UtcNow.Add(devDuration).Add(snapshot.Remaining));
        }

        if (snapshot.EffectWasActive)
        {
            ScheduleAvatarScaleEffectStateNotification(snapshot.RuleId, effectNotificationDelay);
        }

        if (shouldNotifyManagedRewards)
        {
            ManagedRewardAvailabilityChanged?.Invoke();
        }

        return snapshot;
    }

    private async Task ResumePausedAvatarScaleTimerAfterDevAsync(
        PausedAvatarScaleTimerSnapshot snapshot,
        CancellationToken cancellationToken)
    {
        snapshot = GetRetargetedPausedDevAvatarScaleTimerSnapshot(snapshot);
        try
        {
            if (snapshot.Remaining <= TimeSpan.Zero)
            {
                return;
            }

            if (HasNewerLiveAvatarScaleWork(snapshot.QueuedLiveScaleCountAtPause))
            {
                WriteLog($"Dev avatar scale did not resume {snapshot.SourceDescription} because a newer scale reward or payment is waiting.");
                EnsureQueuedAvatarScaleOperationDrain();
                return;
            }

            var operation = await WaitForAvatarScaleOperationSlotAsync(
                Guid.Empty,
                $"Resume {snapshot.SourceRuleName}",
                AvatarScaleOperationPriority.LiveRedeem,
                snapshot.IsTest,
                cancellationToken);
            if (operation is null)
            {
                return;
            }

            try
            {
                if (HasNewerLiveAvatarScaleWork(snapshot.QueuedLiveScaleCountAtPause))
                {
                    WriteLog($"Dev avatar scale did not resume {snapshot.SourceDescription} because a newer scale reward or payment is waiting.");
                    EnsureQueuedAvatarScaleOperationDrain();
                    return;
                }

                if (!await SendAvatarHeightForOperationAsync(
                        operation,
                        snapshot.CarriedHeightMeters,
                        0,
                        cancellationToken))
                {
                    return;
                }

                if (HasNewerLiveAvatarScaleWork(snapshot.QueuedLiveScaleCountAtPause))
                {
                    WriteLog($"Dev avatar scale restored {snapshot.SourceDescription}'s height, then let a newer scale reward or payment take over.");
                    EnsureQueuedAvatarScaleOperationDrain();
                    return;
                }

                if (!TryStartPausedAvatarScaleRestoreSequence(snapshot, out var activeUntil))
                {
                    WriteLog($"Dev avatar scale could not resume {snapshot.SourceDescription} because a newer scale reward or payment is active.");
                    EnsureQueuedAvatarScaleOperationDrain();
                    return;
                }

                WriteLog($"Resumed {snapshot.SourceDescription} at held height {snapshot.CarriedHeightMeters:0.###}m with {DescribeDuration((activeUntil - DateTimeOffset.UtcNow).TotalSeconds)} remaining.");
            }
            finally
            {
                EndAvatarScaleOperation(operation);
            }
        }
        finally
        {
            ClearPausedDevAvatarScaleTimerSnapshot(snapshot);
        }
    }

    private async Task RestoreDevAvatarScaleHeightAsync(
        string devRuleName,
        double previousHeight,
        double transitionSeconds,
        CancellationToken cancellationToken)
    {
        if (HasNewerLiveAvatarScaleWork(queuedLiveScaleCountAtPause: 0))
        {
            WriteLog($"Dev avatar scale '{devRuleName}' left the current scale alone because a newer scale reward or payment is active.");
            EnsureQueuedAvatarScaleOperationDrain();
            return;
        }

        var operation = await WaitForAvatarScaleOperationSlotAsync(
            Guid.Empty,
            $"Restore {devRuleName}",
            AvatarScaleOperationPriority.TestSimulation,
            isTest: true,
            cancellationToken);
        if (operation is null)
        {
            return;
        }

        try
        {
            if (HasNewerLiveAvatarScaleWork(queuedLiveScaleCountAtPause: 0))
            {
                WriteLog($"Dev avatar scale '{devRuleName}' skipped its restore because a newer scale reward or payment is waiting.");
                EnsureQueuedAvatarScaleOperationDrain();
                return;
            }

            if (await SendAvatarHeightForOperationAsync(
                    operation,
                    previousHeight,
                    transitionSeconds,
                    cancellationToken))
            {
                WriteLog($"Dev avatar scale '{devRuleName}' restored the previous height of {previousHeight:0.###}m.");
            }
        }
        finally
        {
            EndAvatarScaleOperation(operation);
        }
    }

    private PausedAvatarScaleTimerSnapshot GetRetargetedPausedDevAvatarScaleTimerSnapshot(
        PausedAvatarScaleTimerSnapshot snapshot)
    {
        lock (stateGate)
        {
            return pausedDevAvatarScaleTimerSnapshot is { } current
                && current.RestoreSequenceId == snapshot.RestoreSequenceId
                ? current
                : snapshot;
        }
    }

    private void ClearPausedDevAvatarScaleTimerSnapshot(PausedAvatarScaleTimerSnapshot snapshot)
    {
        lock (stateGate)
        {
            if (pausedDevAvatarScaleTimerSnapshot is { } current
                && current.RestoreSequenceId == snapshot.RestoreSequenceId)
            {
                pausedDevAvatarScaleTimerSnapshot = null;
            }
        }
    }

    private void RetargetPausedDevAvatarScaleTimerForAvatarChange(string avatarId)
    {
        var normalizedAvatarId = avatarId?.Trim() ?? string.Empty;
        if (string.IsNullOrWhiteSpace(normalizedAvatarId))
        {
            return;
        }

        PausedAvatarScaleTimerSnapshot? retargetedSnapshot = null;
        lock (stateGate)
        {
            if (pausedDevAvatarScaleTimerSnapshot is not { } snapshot
                || string.Equals(snapshot.AvatarId, normalizedAvatarId, StringComparison.Ordinal))
            {
                return;
            }

            pausedDevAvatarScaleTimerSnapshot = snapshot with
            {
                AvatarId = normalizedAvatarId
            };
            retargetedSnapshot = pausedDevAvatarScaleTimerSnapshot;
        }

        if (retargetedSnapshot is not null)
        {
            WriteLog($"Paused avatar scale restore from '{retargetedSnapshot.SourceRuleName}' will resume on the current avatar after the dev height command finishes.");
        }
    }

    private bool TryStartPausedAvatarScaleRestoreSequence(
        PausedAvatarScaleTimerSnapshot snapshot,
        out DateTimeOffset activeUntil)
    {
        activeUntil = DateTimeOffset.MinValue;
        var remaining = snapshot.Remaining <= TimeSpan.Zero
            ? TimeSpan.FromMilliseconds(500)
            : snapshot.Remaining;
        var newCancellation = runtimeCancellation is null
            ? new CancellationTokenSource()
            : CancellationTokenSource.CreateLinkedTokenSource(runtimeCancellation.Token);
        ActiveAvatarScaleRestoreSequenceState? sequence = null;
        var sessionId = snapshot.SessionId == Guid.Empty ? Guid.NewGuid() : snapshot.SessionId;

        lock (stateGate)
        {
            var now = DateTimeOffset.UtcNow;
            if (HasNewerLiveAvatarScaleWorkLocked(snapshot.QueuedLiveScaleCountAtPause, now))
            {
                newCancellation.Dispose();
                return false;
            }

            activeUntil = now.Add(remaining);
            sequence = new ActiveAvatarScaleRestoreSequenceState(
                ++nextAvatarScaleRestoreSequenceId,
                snapshot.AvatarId,
                snapshot.CarriedHeightMeters,
                snapshot.RestoreHeightMeters,
                activeUntil,
                snapshot.SourceRuleName,
                snapshot.RestoreSmoothTransitionSeconds,
                snapshot.RestoreToPaidGrowthIfActive,
                snapshot.IsTest);
            avatarScaleRestoreSequenceCancellation = newCancellation;
            activeAvatarScaleRestoreSequence = sequence;

            if (snapshot.RuleId != Guid.Empty)
            {
                activeAvatarScaleHeightSessions[snapshot.RuleId] = new ActiveAvatarScaleHeightSessionState(
                    snapshot.RuleId,
                    sessionId,
                    snapshot.SourceRuleName,
                    snapshot.AvatarId,
                    snapshot.RestoreHeightMeters,
                    snapshot.CarriedHeightMeters,
                    activeUntil);
                SetActiveAvatarScaleCarryoverLocked(
                    snapshot.RuleId,
                    sessionId,
                    sequence.SequenceId,
                    snapshot.SourceRuleName,
                    snapshot.AvatarId,
                    snapshot.CarriedHeightMeters,
                    snapshot.RestoreHeightMeters,
                    activeUntil,
                    snapshot.RestoreToPaidGrowthIfActive);
                if (snapshot.EffectWasActive)
                {
                    activeAvatarScaleEffects[snapshot.RuleId] = activeUntil;
                }
            }
        }

        _ = Task.Run(() => RunAvatarScaleRestoreSequenceAsync(sequence, newCancellation), CancellationToken.None);
        if (snapshot.RuleId != Guid.Empty)
        {
            ScheduleAvatarScaleHeightSessionEnd(snapshot.RuleId, sessionId, remaining, CancellationToken.None);
            if (snapshot.EffectWasActive)
            {
                ScheduleAvatarScaleEffectStateNotification(snapshot.RuleId, remaining);
            }
        }

        if (snapshot.Rule is not null)
        {
            ApplyAvatarScaleRuleLockoutUntil(snapshot.Rule, activeUntil);
        }

        ManagedRewardAvailabilityChanged?.Invoke();
        return true;
    }

    private PausedMovementTimerSnapshot? PauseActiveMovementTimerForDev(
        string movementLaneKey,
        TimeSpan devDuration,
        out string? waitReason)
    {
        waitReason = null;
        PendingResetState? pendingReset = null;
        PausedMovementTimerSnapshot? snapshot = null;

        lock (stateGate)
        {
            var now = DateTimeOffset.UtcNow;
            if (!actionLanes.TryGetValue(movementLaneKey, out var activeLane)
                || activeLane.BusyUntil <= now)
            {
                actionLanes.Remove(movementLaneKey);
                return null;
            }

            if (activeLane.IsSoftLock
                && activeMovementLocks.TryGetValue(activeLane.OwnerId, out var activeSoftLock))
            {
                waitReason = $"active stop-input lock '{activeSoftLock.RuleName}' is still holding that movement lane.";
                return null;
            }

            if (activeDesktopInputLocks.TryGetValue(activeLane.OwnerId, out var activeDesktopLock))
            {
                waitReason = $"active desktop stop-input lock '{activeDesktopLock.RuleName}' is still holding that movement lane.";
                return null;
            }

            var remaining = activeLane.BusyUntil - now;
            TriggerRuleSnapshot? sourceRule = null;
            ResolvedRuleAction? sourceAction = null;
            if (pendingResets.TryGetValue(activeLane.RuleId, out var candidateReset)
                && candidateReset.MovementLaneLeaseId == activeLane.OwnerId
                && candidateReset.MovementLaneKeys.Contains(movementLaneKey, StringComparer.Ordinal))
            {
                pendingReset = candidateReset;
                pendingResets.Remove(candidateReset.RuleId);
                sourceRule = candidateReset.Rule;
                sourceAction = candidateReset.Action;
            }
            else
            {
                sourceRule = FindTriggerRuleSnapshotForRuntimeRuleId(activeLane.RuleId);
                if (sourceRule is null
                    || sourceRule.ActionType != OscActionType.PlayerMovement
                    || sourceRule.MovementDirection is PlayerMovementDirection.RandomMovement or PlayerMovementDirection.GlitchyMovement)
                {
                    waitReason = "Crystal Relay could not safely snapshot the active movement timer, so the dev movement will run from the queue.";
                    return null;
                }

                sourceAction = ResolvePlayerMovementAction(sourceRule);
            }

            snapshot = new PausedMovementTimerSnapshot(
                sourceRule,
                sourceAction,
                movementLaneKey,
                remaining,
                CountQueuedLiveLaneActionsLocked(movementLaneKey),
                DescribeMovementTimerSource(sourceRule.Id, sourceRule.Name));
            actionLanes.Remove(movementLaneKey);
        }

        pendingReset?.Cancellation.Cancel();
        if (snapshot is not null)
        {
            ApplyRuleLockoutUntil(
                snapshot.Rule,
                DateTimeOffset.UtcNow.Add(devDuration).Add(snapshot.Remaining));
        }

        return snapshot;
    }

    private async Task RunDevMovementOverlayAsync(
        TriggerRuleSnapshot rule,
        TimeSpan duration,
        string userDisplayName,
        bool drainQueuedLanesOnRelease,
        CancellationToken cancellationToken)
    {
        var executionRule = ResolveRandomMovementRule(rule);
        var action = ResolvePlayerMovementAction(executionRule);
        var laneKeys = GetActionLaneKeys(executionRule, action);
        var laneLeaseId = laneKeys.Count == 0 ? Guid.Empty : Guid.NewGuid();
        var activeUntil = DateTimeOffset.UtcNow.Add(duration);

        await SendPacketsToVrChatAsync(action.Packets, cancellationToken);
        lock (stateGate)
        {
            foreach (var laneKey in laneKeys)
            {
                actionLanes[laneKey] = new ActiveMovementLaneState(
                    laneLeaseId,
                    activeUntil,
                    executionRule.Id,
                    false);
            }
        }

        WriteLog($"{userDisplayName} ran dev movement '{DescribeMovementAction(executionRule.MovementDirection)}' for {DescribeDuration(duration.TotalSeconds)}.");

        try
        {
            if (executionRule.MovementDirection == PlayerMovementDirection.Jump)
            {
                await RunDevJumpMovementOverlayAsync(action, duration, laneKeys.FirstOrDefault(), laneLeaseId, cancellationToken);
            }
            else
            {
                await Task.Delay(duration, cancellationToken);
            }
        }
        finally
        {
            try
            {
                if (laneKeys.Count == 0 || laneKeys.Any(laneKey => IsMovementLaneLeaseActive(laneKey, laneLeaseId)))
                {
                    await SendPacketsToVrChatAsync(action.ResetPackets, CancellationToken.None);
                }
            }
            catch (Exception ex)
            {
                WriteLog($"Failed to release dev movement '{executionRule.Name}': {ex.Message}");
            }

            var releasedLaneKeys = ReleaseMovementLanes(laneLeaseId, laneKeys);
            if (drainQueuedLanesOnRelease)
            {
                foreach (var releasedLaneKey in releasedLaneKeys)
                {
                    EnsureQueuedLaneDrain(releasedLaneKey);
                }
            }
        }
    }

    private async Task RunDevJumpMovementOverlayAsync(
        ResolvedRuleAction action,
        TimeSpan duration,
        string? laneKey,
        Guid laneLeaseId,
        CancellationToken cancellationToken)
    {
        var endAt = DateTimeOffset.UtcNow.Add(duration);
        await Task.Delay(JumpPulsePressDuration, cancellationToken);
        if (IsMovementLaneLeaseActive(laneKey, laneLeaseId))
        {
            await SendPacketsToVrChatAsync(action.ResetPackets, cancellationToken);
        }

        while (IsMovementLaneLeaseActive(laneKey, laneLeaseId))
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

            await Task.Delay(delay, cancellationToken);
            if (!IsMovementLaneLeaseActive(laneKey, laneLeaseId))
            {
                break;
            }

            await SendPacketsToVrChatAsync(action.Packets, cancellationToken);
            await Task.Delay(JumpPulsePressDuration, cancellationToken);
            if (IsMovementLaneLeaseActive(laneKey, laneLeaseId))
            {
                await SendPacketsToVrChatAsync(action.ResetPackets, cancellationToken);
            }
        }

        var finalDelay = endAt - DateTimeOffset.UtcNow;
        if (finalDelay > TimeSpan.Zero)
        {
            await Task.Delay(finalDelay, cancellationToken);
        }
    }

    private async Task ResumePausedMovementTimerAfterDevAsync(
        PausedMovementTimerSnapshot snapshot,
        CancellationToken cancellationToken)
    {
        if (snapshot.Remaining <= TimeSpan.Zero)
        {
            return;
        }

        if (HasNewerLiveMovementWork(snapshot.MovementLaneKey, snapshot.QueuedLiveLaneCountAtPause))
        {
            WriteLog($"Dev movement did not resume {snapshot.SourceDescription} because a newer movement reward or payment is waiting.");
            EnsureQueuedLaneDrain(snapshot.MovementLaneKey);
            return;
        }

        var laneKeys = GetActionLaneKeys(snapshot.Rule, snapshot.Action);
        var laneLeaseId = laneKeys.Count == 0 ? Guid.Empty : Guid.NewGuid();
        var activeUntil = DateTimeOffset.UtcNow.Add(snapshot.Remaining);

        await SendPacketsToVrChatAsync(snapshot.Action.Packets, cancellationToken);
        var shouldDropForNewerMovement = false;
        lock (stateGate)
        {
            if (HasNewerLiveMovementWorkLocked(snapshot.MovementLaneKey, snapshot.QueuedLiveLaneCountAtPause, DateTimeOffset.UtcNow))
            {
                shouldDropForNewerMovement = true;
            }
            else
            {
                foreach (var laneKey in laneKeys)
                {
                    actionLanes[laneKey] = new ActiveMovementLaneState(
                        laneLeaseId,
                        activeUntil,
                        snapshot.Rule.Id,
                        false);
                }
            }
        }

        if (shouldDropForNewerMovement)
        {
            try
            {
                await SendPacketsToVrChatAsync(snapshot.Action.ResetPackets, CancellationToken.None);
            }
            catch (Exception ex)
            {
                WriteLog($"Failed to release resumed movement for '{snapshot.Rule.Name}': {ex.Message}");
            }

            WriteLog($"Dev movement restored {snapshot.SourceDescription}'s input, then let a newer movement reward or payment take over.");
            EnsureQueuedLaneDrain(snapshot.MovementLaneKey);
            return;
        }

        if (snapshot.Rule.MovementDirection == PlayerMovementDirection.Jump)
        {
            ScheduleJumpPulseReset(
                snapshot.Rule,
                snapshot.Action,
                snapshot.Remaining.TotalSeconds,
                laneKeys.FirstOrDefault(),
                laneLeaseId,
                notifyManagedRewardState: false);
        }
        else
        {
            ScheduleReset(
                snapshot.Rule,
                snapshot.Action,
                snapshot.Remaining.TotalSeconds,
                laneKeys,
                laneLeaseId,
                notifyManagedRewardState: false);
        }

        ApplyRuleLockoutUntil(snapshot.Rule, activeUntil);
        WriteLog($"Resumed {snapshot.SourceDescription} with {DescribeDuration(snapshot.Remaining.TotalSeconds)} remaining.");
    }

    private bool HasNewerLiveAvatarScaleWork(int queuedLiveScaleCountAtPause)
    {
        lock (stateGate)
        {
            return HasNewerLiveAvatarScaleWorkLocked(queuedLiveScaleCountAtPause, DateTimeOffset.UtcNow);
        }
    }

    private bool HasNewerLiveAvatarScaleWorkLocked(
        int queuedLiveScaleCountAtPause,
        DateTimeOffset now)
    {
        return activeAvatarScaleRestoreSequence is not null
            && activeAvatarScaleRestoreSequence.ActiveUntil > now
            || CountQueuedLiveAvatarScaleOperationsLocked() > queuedLiveScaleCountAtPause;
    }

    private int CountQueuedLiveAvatarScaleOperationsLocked()
    {
        return queuedAvatarScaleOperations.Count(operation => !operation.IsTest);
    }

    private bool HasNewerLiveMovementWork(
        string movementLaneKey,
        int queuedLiveLaneCountAtPause)
    {
        lock (stateGate)
        {
            return HasNewerLiveMovementWorkLocked(
                movementLaneKey,
                queuedLiveLaneCountAtPause,
                DateTimeOffset.UtcNow);
        }
    }

    private bool HasNewerLiveMovementWorkLocked(
        string movementLaneKey,
        int queuedLiveLaneCountAtPause,
        DateTimeOffset now)
    {
        return actionLanes.TryGetValue(movementLaneKey, out var activeLane)
            && activeLane.BusyUntil > now
            || CountQueuedLiveLaneActionsLocked(movementLaneKey) > queuedLiveLaneCountAtPause;
    }

    private int CountQueuedLiveLaneActionsLocked(string movementLaneKey)
    {
        return queuedLaneActions.TryGetValue(movementLaneKey, out var queue)
            ? queue.Count(action => !action.IsTest)
            : 0;
    }

    private AvatarScaleRuleSnapshot? FindAvatarScaleRuleSnapshot(Guid ruleId)
    {
        if (ruleId == Guid.Empty || activeConfiguration is null)
        {
            return null;
        }

        var configuredScaleRule = activeConfiguration.AvatarScaleRules.FirstOrDefault(rule => rule.Id == ruleId);
        if (configuredScaleRule is not null)
        {
            return configuredScaleRule;
        }

        return activeConfiguration.CashPaymentRules
            .Select(rule => rule.ScaleAction)
            .FirstOrDefault(rule => rule?.Id == ruleId);
    }

    private TriggerRuleSnapshot? FindTriggerRuleSnapshotForRuntimeRuleId(Guid ruleId)
    {
        if (ruleId == Guid.Empty || activeConfiguration is null)
        {
            return null;
        }

        var configuredRule = activeConfiguration.Rules.FirstOrDefault(rule => rule.Id == ruleId);
        if (configuredRule is not null)
        {
            return configuredRule;
        }

        return activeConfiguration.CashPaymentRules
            .Select(rule => rule.TriggerAction)
            .FirstOrDefault(rule => rule?.Id == ruleId);
    }

    private string DescribeAvatarScaleTimerSource(Guid ruleId, string fallbackRuleName)
    {
        var paymentRule = activeConfiguration?.CashPaymentRules.FirstOrDefault(rule => rule.ScaleAction?.Id == ruleId);
        return paymentRule is null
            ? $"reward timer '{NormalizeTimerSourceName(fallbackRuleName, "Avatar Scale")}'"
            : $"payment timer '{NormalizeTimerSourceName(paymentRule.Name, "Cash Payment")}'";
    }

    private string DescribeMovementTimerSource(Guid ruleId, string fallbackRuleName)
    {
        var paymentRule = activeConfiguration?.CashPaymentRules.FirstOrDefault(rule => rule.TriggerAction?.Id == ruleId);
        return paymentRule is null
            ? $"reward timer '{NormalizeTimerSourceName(fallbackRuleName, "Movement")}'"
            : $"payment timer '{NormalizeTimerSourceName(paymentRule.Name, "Cash Payment")}'";
    }

    private static string NormalizeTimerSourceName(string? sourceName, string fallback)
    {
        return string.IsNullOrWhiteSpace(sourceName) ? fallback : sourceName.Trim();
    }

    private void ApplyAvatarScaleRuleLockoutUntil(
        AvatarScaleRuleSnapshot rule,
        DateTimeOffset expiresAt)
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

    private bool IsDevFireSaleProtectedBroadcaster()
    {
        var broadcasterUserId = broadcaster?.UserId?.Trim() ?? string.Empty;
        return !string.IsNullOrWhiteSpace(broadcasterUserId)
            && ProtectedDevFireSaleBroadcasterIds.Contains(broadcasterUserId);
    }

    private void RequestDevFireSale(
        int discountPercent,
        int durationSeconds,
        string userDisplayName)
    {
        var request = new DevFireSaleRequest(
            Math.Clamp(discountPercent, 1, 100),
            Math.Max(1, durationSeconds),
            string.IsNullOrWhiteSpace(userDisplayName) ? "Screminpal_" : userDisplayName.Trim());

        if (DevFireSaleRequested?.Invoke(request) != true)
        {
            WriteLog("Dev Fire Sale command skipped because Crystal Relay's Fire Sale state is not ready.");
        }
    }

    private async Task HandleSimulatedTwitchEventAsync(
        BridgeIncomingEvent? bridgeEvent,
        UniversalIncomingEvent? universalEvent,
        CancellationToken cancellationToken)
    {
        var configuration = activeConfiguration
            ?? throw new InvalidOperationException("Crystal Relay runtime is not ready for simulation yet.");
        var ruleIndex = activeRuleIndex;

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
        if (bridgeEvent.TriggerType == TwitchTriggerType.PowerUp)
        {
            await HandlePowerUpEventAsync(
                configuration,
                bridgeEvent,
                currentAvatarId,
                avatarChangeTransitionActive,
                temporarilyDisabledRuleIds,
                avatarScaleHandled,
                cancellationToken);
            return;
        }

        var matchingRules = SelectMatchingRules(
            configuration,
            ruleIndex,
            bridgeEvent,
            currentAvatarId,
            avatarChangeTransitionActive,
            temporarilyDisabledRuleIds);

        if (AreRedeemsPaused())
        {
            LogRedeemsPaused();
            return;
        }

        var fireSaleContributionHandled = TryBuildRewardFireSaleContribution(bridgeEvent, out var fireSaleContribution)
            && RewardFireSaleContributionReceived?.Invoke(fireSaleContribution) == true;

        if (matchingRules.Length == 0)
        {
            if (TryQueueAvatarSwitchTriggerDuringSupporterOverride(
                ruleIndex,
                bridgeEvent,
                currentAvatarId,
                configuration.AvatarChangeCooldownOnlyModeEnabled))
            {
                return;
            }

            if (TryQueueAvatarSwitchTriggerDuringActiveAvatarSwitch(
                ruleIndex,
                bridgeEvent,
                currentAvatarId,
                configuration.AvatarChangeCooldownOnlyModeEnabled))
            {
                return;
            }

            if (!avatarScaleHandled && !fireSaleContributionHandled)
            {
                WriteLog($"No configured rule matched the simulated {bridgeEvent.TriggerLabel} event.");
            }

            return;
        }

        foreach (var rule in matchingRules)
        {
            await ExecuteRuleAsync(rule, bridgeEvent, cancellationToken);
        }
    }

    public AvatarScaleRuntimeStatus GetAvatarScaleRuntimeStatus()
    {
        lock (stateGate)
        {
            var now = DateTimeOffset.UtcNow;
            PruneExpiredAvatarScaleCarryoverLocked(now);
            PruneExpiredAvatarScaleHeightSessionsLocked(now);
            var isActive = activeAvatarScaleCarryover?.ActiveUntil > now
                || activeAvatarScaleHeightSessions.Count > 0
                || activeAvatarScaleRestoreSequence?.ActiveUntil > now
                || avatarScaleSupporterGrowthStates.Values.Any(state => state.PaidActiveUntil > now);

            return new AvatarScaleRuntimeStatus(
                TryGetObservedFloatLocked("/avatar/eyeheight", out var currentHeight) ? currentHeight : null,
                TryGetObservedFloatLocked("/avatar/eyeheightmin", out var minimumHeight) ? minimumHeight : null,
                TryGetObservedFloatLocked("/avatar/eyeheightmax", out var maximumHeight) ? maximumHeight : null,
                TryGetObservedBoolLocked("/avatar/eyeheightscalingallowed", out var scalingAllowed) ? scalingAllowed : null,
                isActive);
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
            CancelPendingStreamOfflineConfirmation();
            isBroadcasterLive = false;
            hasResolvedBroadcasterLiveState = false;
            nextTriggerInfoAnnouncementAt = DateTimeOffset.MinValue;
            oscSessionMode = OscSessionMode.Stopped;
            ClearRuntimeState();
            hasAttemptedResume = false;
            StreamStateChanged?.Invoke(false, false);
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
            CancelPendingStreamOfflineConfirmation();
            isBroadcasterLive = false;
            hasResolvedBroadcasterLiveState = false;
            nextTriggerInfoAnnouncementAt = DateTimeOffset.MinValue;
            oscSessionMode = OscSessionMode.Stopped;
            ClearRuntimeState();
            hasAttemptedResume = false;
            StreamStateChanged?.Invoke(false, false);
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
            CancelPendingStreamOfflineConfirmation();
            isBroadcasterLive = false;
            hasResolvedBroadcasterLiveState = false;
            nextTriggerInfoAnnouncementAt = DateTimeOffset.MinValue;
            oscSessionMode = OscSessionMode.Stopped;
            ClearRuntimeState();
            hasAttemptedResume = false;
            StatusChanged?.Invoke("Background bridge stopped.");
        }
    }

    public async ValueTask DisposeAsync()
    {
        await StopAsync();
        desktopInputLockService.EmergencyUnlockTriggered -= HandleEmergencyDesktopInputUnlock;
        await desktopInputLockService.DisposeAsync();
        twitchApiClient.Dispose();
        vrChatApiClient.Dispose();
        thirdPartyChatEmoteRefreshGate.Dispose();
        worldCommandLookupGate.Dispose();
        await cashPaymentProviderService.DisposeAsync();
        await oscRouterService.DisposeAsync();
    }

    // Main background loop for the live bridge. EventSub is the primary listener,
    // and the validation loop runs beside it to keep Twitch sessions healthy.
    private async Task RunBridgeAsync(CancellationToken cancellationToken)
    {
        var validationTask = Task.Run(() => RunValidationLoopAsync(cancellationToken), cancellationToken);
        var triggerInfoAnnouncementTask = Task.Run(() => RunTriggerInfoAnnouncementLoopAsync(cancellationToken), cancellationToken);
        var cashPaymentTask = Task.Run(() => RunCashPaymentLoopAsync(cancellationToken), cancellationToken);

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

            try
            {
                await cashPaymentTask;
            }
            catch (OperationCanceledException)
            {
            }
            catch (Exception ex)
            {
                WriteLog($"Cash payment listener ended with an error: {SensitiveTextSanitizer.Sanitize(ex.Message)}");
            }

            await ResetPendingRulesAsync();
        }
    }

    private async Task RunCashPaymentLoopAsync(CancellationToken cancellationToken)
    {
        var configuration = activeConfiguration;
        if (configuration is null)
        {
            await Task.Delay(Timeout.InfiniteTimeSpan, cancellationToken);
            return;
        }

        await cashPaymentProviderService.RunAsync(
            configuration.CashPayments,
            HandleCashPaymentEventAsync,
            WriteLog,
            cancellationToken);
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

                WriteLog("Validated the Twitch OAuth sessions.");
            }
            catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
            {
                throw;
            }
            catch (Exception ex)
            {
                WriteLog($"Twitch validation failed: {ex.Message}");
                if (isBroadcasterLive)
                {
                    isBroadcasterLive = false;
                    nextTriggerInfoAnnouncementAt = DateTimeOffset.MinValue;
                    StreamStateChanged?.Invoke(false, false);
                }

                StatusChanged?.Invoke("OAuth session expired. Please reconnect Twitch.");
                runtimeCancellation?.Cancel();
                return;
            }

            if (bot is not null)
            {
                try
                {
                    bot = await EnsureAccountReadyAsync(bot, TwitchScopes.Bot, BridgeAccountRole.Bot, cancellationToken);
                }
                catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
                {
                    throw;
                }
                catch (Exception ex)
                {
                    var broadcasterIsSender = activeConfiguration?.UseBroadcasterAsBotSender ?? false;
                    if (broadcasterIsSender)
                    {
                        WriteLog($"Bot Twitch validation failed (broadcaster is used as chat sender, so the bridge is unaffected): {SensitiveTextSanitizer.Sanitize(ex.Message)}");
                        bot = null;
                    }
                    else
                    {
                        WriteLog($"Bot Twitch validation failed: {SensitiveTextSanitizer.Sanitize(ex.Message)}");
                        StatusChanged?.Invoke(T("Bot Twitch login needs reconnecting. Chat announcements are disabled until then."));
                        bot = null;
                    }
                }
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

    private async Task<bool> TryHandleTriggerInfoReminderCommandAsync(
        BridgeRuntimeConfiguration configuration,
        BridgeIncomingEvent bridgeEvent,
        CancellationToken cancellationToken)
    {
        if (!configuration.TriggerInfoCommandEnabled
            || !bridgeEvent.IsChatCommandTrigger)
        {
            return false;
        }

        var commandText = ChatCommandUtility.Normalize(configuration.TriggerInfoCommandText);
        if (!ChatCommandUtility.IsConfigured(commandText)
            || !ChatCommandUtility.MessageMatches(commandText, bridgeEvent.ChatCommandText))
        {
            return false;
        }

        if (!isBroadcasterLive)
        {
            return true;
        }

        if (!UserCanTriggerChatCommand(configuration.TriggerInfoCommandPermission, bridgeEvent))
        {
            return true;
        }

        var now = DateTimeOffset.UtcNow;
        var cooldownSeconds = Math.Max(0, configuration.TriggerInfoCommandCooldownSeconds);
        lock (stateGate)
        {
            if (triggerInfoCommandCooldowns.TryGetValue(commandText, out var cooldownUntil)
                && cooldownUntil > now)
            {
                return true;
            }

            triggerInfoCommandCooldowns[commandText] = now.AddSeconds(cooldownSeconds);
        }

        var currentAvatarId = GetCurrentVrChatAvatarId();
        var message = BuildTriggerInfoAnnouncement(configuration, currentAvatarId);
        if (string.IsNullOrWhiteSpace(message))
        {
            message = T("No current trigger info is available right now.");
        }

        if (await TrySendChatMessageWithEffectiveSenderAsync(message, "Trigger info reminder command", cancellationToken))
        {
            WriteLog($"{bridgeEvent.UserDisplayName} used the trigger info reminder command.");
        }

        return true;
    }

    private async Task<bool> TryHandleWorldCommandAsync(
        BridgeRuntimeConfiguration configuration,
        BridgeIncomingEvent bridgeEvent,
        CancellationToken cancellationToken)
    {
        if (!configuration.WorldCommandEnabled
            || !bridgeEvent.IsChatCommandTrigger
            || !ChatCommandUtility.MessageMatches(configuration.WorldCommandText, bridgeEvent.ChatCommandText))
        {
            return false;
        }

        if (!UserCanTriggerChatCommand(configuration.WorldCommandPermission, bridgeEvent))
        {
            return true;
        }

        var now = DateTimeOffset.UtcNow;
        var cooldownSeconds = Math.Max(0, configuration.WorldCommandCooldownSeconds);
        lock (stateGate)
        {
            if (nextWorldCommandAllowedAt > now)
            {
                return true;
            }

            nextWorldCommandAllowedAt = now.AddSeconds(cooldownSeconds);
        }

        var message = await BuildWorldCommandMessageAsync(configuration, cooldownSeconds, cancellationToken);
        if (await TrySendChatMessageWithEffectiveSenderAsync(message, "VRChat world command", cancellationToken))
        {
            WriteLog(TF("{0} used the VRChat world command.", bridgeEvent.UserDisplayName));
        }

        return true;
    }

    private bool TryHandlePauseCommand(
        BridgeRuntimeConfiguration configuration,
        BridgeIncomingEvent bridgeEvent)
    {
        if (!configuration.PauseCommandEnabled
            || !bridgeEvent.IsChatCommandTrigger
            || !ChatCommandUtility.MessageMatches(configuration.PauseCommandText, bridgeEvent.ChatCommandText))
        {
            return false;
        }

        if (!UserCanTriggerChatCommand(ChatCommandPermission.Moderators, bridgeEvent))
        {
            return true;
        }

        PauseCommandRequested?.Invoke();
        WriteLog(TF("{0} used the pause command.", bridgeEvent.UserDisplayName));
        return true;
    }

    private bool TryHandleGroupToggleCommand(
        BridgeRuntimeConfiguration configuration,
        BridgeIncomingEvent bridgeEvent)
    {
        if (!configuration.RedeemGroupCommandEnabled
            || !bridgeEvent.IsChatCommandTrigger)
        {
            return false;
        }

        if (!UserCanTriggerChatCommand(ChatCommandPermission.Moderators, bridgeEvent))
        {
            return false;
        }

        var commandText = ChatCommandUtility.Normalize(bridgeEvent.ChatCommandText);
        var group = configuration.RedeemGroups
            .FirstOrDefault(g => ChatCommandUtility.MessageMatches(g.CommandText, commandText));

        if (group is null)
        {
            return false;
        }

        GroupToggleRequested?.Invoke(group.Name);
        WriteLog(TF("{0} toggled the '{1}' redeem group.", bridgeEvent.UserDisplayName, group.Name));
        return true;
    }

    private bool TryHandleRedeemControlCommand(
        BridgeRuntimeConfiguration configuration,
        BridgeIncomingEvent bridgeEvent)
    {
        if (!configuration.RedeemControlCommandEnabled
            || !bridgeEvent.IsChatCommandTrigger)
        {
            return false;
        }

        if (!UserCanTriggerChatCommand(ChatCommandPermission.Moderators, bridgeEvent))
        {
            return false;
        }

        var text = bridgeEvent.ChatCommandText?.Trim() ?? string.Empty;
        if (string.IsNullOrEmpty(text))
        {
            return false;
        }

        var enableMatch = text.StartsWith("!enable ", StringComparison.OrdinalIgnoreCase);
        var disableMatch = text.StartsWith("!disable ", StringComparison.OrdinalIgnoreCase);

        if (!enableMatch && !disableMatch)
        {
            return false;
        }

        var redeemName = enableMatch
            ? text.Substring("!enable ".Length).Trim()
            : text.Substring("!disable ".Length).Trim();

        if (string.IsNullOrEmpty(redeemName))
        {
            return false;
        }

        var enable = enableMatch;
        RedeemControlRequested?.Invoke(redeemName, enable);
        WriteLog(TF("{0} used redeem control: {1} '{2}'.", bridgeEvent.UserDisplayName, enable ? "enable" : "disable", redeemName));
        return true;
    }

    private async Task<string> BuildWorldCommandMessageAsync(
        BridgeRuntimeConfiguration configuration,
        int cooldownSeconds,
        CancellationToken cancellationToken)
    {
        var world = await GetCurrentWorldForCommandAsync(configuration, cooldownSeconds, cancellationToken);
        if (!world.IsAvailable)
        {
            return T("Crystal Relay could not find a public VRChat world to share right now.");
        }

        var blacklistDecision = await worldCommandBlacklistService.EvaluateAsync(
            world.WorldId,
            world.WorldAuthorId,
            cancellationToken);
        if (blacklistDecision.IsBlocked)
        {
            WriteLog(blacklistDecision.IsFailClosed
                ? T("VRChat world command did not share a world because the protected world list is not ready.")
                : T("VRChat world command did not share a world because the protected world list matched it."));
            if (!blacklistDecision.IsFailClosed
                && !string.IsNullOrWhiteSpace(blacklistDecision.Reason))
            {
                return SanitizeBotMessage(TF(
                    "This VRChat world is protected for this reason: {0}. Crystal Relay is not sharing the world link.",
                    blacklistDecision.Reason));
            }

            return T("This VRChat world is protected right now, so Crystal Relay is not sharing the world link.");
        }

        return SanitizeBotMessage(TF("Current VRChat world: {0} - {1}", world.WorldName, world.WorldUrl));
    }

    private async Task<VrChatCurrentWorldLookupResult> GetCurrentWorldForCommandAsync(
        BridgeRuntimeConfiguration configuration,
        int cooldownSeconds,
        CancellationToken cancellationToken)
    {
        if (!configuration.VrChatSession.IsConnected)
        {
            return VrChatCurrentWorldLookupResult.Unavailable;
        }

        var now = DateTimeOffset.UtcNow;
        var vrChatUserId = configuration.VrChatSession.UserId.Trim();
        lock (stateGate)
        {
            if (cachedWorldCommandResult is { IsAvailable: true } cachedResult
                && cachedWorldCommandResultExpiresAt > now
                && string.Equals(cachedWorldCommandUserId, vrChatUserId, StringComparison.Ordinal))
            {
                return cachedResult;
            }
        }

        await worldCommandLookupGate.WaitAsync(cancellationToken);
        try
        {
            now = DateTimeOffset.UtcNow;
            lock (stateGate)
            {
                if (cachedWorldCommandResult is { IsAvailable: true } cachedResult
                    && cachedWorldCommandResultExpiresAt > now
                    && string.Equals(cachedWorldCommandUserId, vrChatUserId, StringComparison.Ordinal))
                {
                    return cachedResult;
                }
            }

            var result = await vrChatApiClient.GetCurrentWorldAsync(
                configuration.VrChatSession.AuthCookie,
                cancellationToken);
            if (result.IsAvailable)
            {
                var cacheSeconds = Math.Max(1, cooldownSeconds);
                lock (stateGate)
                {
                    cachedWorldCommandResult = result;
                    cachedWorldCommandUserId = vrChatUserId;
                    cachedWorldCommandResultExpiresAt = DateTimeOffset.UtcNow.AddSeconds(cacheSeconds);
                }
            }

            return result;
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            throw;
        }
        catch
        {
            WriteLog(T("VRChat world command lookup failed without exposing location details."));
            return VrChatCurrentWorldLookupResult.Unavailable;
        }
        finally
        {
            worldCommandLookupGate.Release();
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
                try
                {
                    var result = await session.ListenAsync(notification => HandleNotificationSafelyAsync(notification, cancellationToken), cancellationToken);

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

    private async Task HandleNotificationSafelyAsync(EventSubNotification notification, CancellationToken cancellationToken)
    {
        try
        {
            await HandleNotificationAsync(notification, cancellationToken);
        }
        catch (Exception ex)
        {
            WriteLog($"Notification handler error (keeping connection open): {SensitiveTextSanitizer.Sanitize(ex.Message)}");
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
                     && SubscriptionConditionMatchesBroadcaster(subscription.Condition, broadcaster.UserId)
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
            if (RequiresChatReadScope(subscriptionType)
                && !HasScope(broadcaster, "user:read:chat"))
            {
                WriteLog($"Broadcaster chat read scope is missing, so Twitch Chatbox subscription '{subscriptionType}' will wait until broadcaster reconnect.");
                continue;
            }

            if (RequiresSuspiciousUsersReadScope(subscriptionType)
                && !HasScope(broadcaster, TwitchScopes.SuspiciousUsersRead))
            {
                WriteLog($"Suspicious chatter subscription '{subscriptionType}' needs the broadcaster to reconnect once for Twitch suspicious-user access.");
                continue;
            }

            if (string.Equals(subscriptionType, "channel.follow", StringComparison.Ordinal))
            {
                if (!HasScope(broadcaster, TwitchScopes.FollowRead))
                {
                    WriteLog("Follow activity needs the broadcaster to reconnect once for Twitch follower-read permission.");
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
                    GetEventSubSubscriptionVersion(subscriptionType),
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
            catch (Exception ex) when (IsChatboxModerationSubscription(subscriptionType))
            {
                WriteLog($"Twitch Chatbox moderation listener '{subscriptionType}' could not start yet: {ex.Message}");
            }
        }
    }

    private static string GetEventSubSubscriptionVersion(string subscriptionType) => subscriptionType switch
    {
        "channel.follow" => "2",
        "channel.custom_power_up_redemption.add" => "beta",
        _ => "1"
    };

    private static bool RequiresChatReadScope(string subscriptionType) =>
        subscriptionType is "channel.chat.message"
            or "channel.chat.message_delete"
            or "channel.chat.clear_user_messages"
            or "channel.chat.clear";

    private static bool RequiresSuspiciousUsersReadScope(string subscriptionType) =>
        subscriptionType is "channel.suspicious_user.update" or "channel.suspicious_user.message";

    private static bool IsChatboxModerationSubscription(string subscriptionType) =>
        RequiresChatReadScope(subscriptionType) || RequiresSuspiciousUsersReadScope(subscriptionType);

    private static bool SubscriptionConditionMatchesBroadcaster(
        TwitchApiClient.EventSubConditionInfo condition,
        string broadcasterUserId)
    {
        return string.Equals(condition.BroadcasterUserId, broadcasterUserId, StringComparison.Ordinal)
            || string.Equals(condition.ToBroadcasterUserId, broadcasterUserId, StringComparison.Ordinal);
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

        var redemptionChatboxMessage = ParseChannelPointRedemptionChatboxMessage(notification);
        if (redemptionChatboxMessage is not null)
        {
            ChatMessageReceived?.Invoke(redemptionChatboxMessage);
        }

        var supportChatboxMessage = ParseSupportEventChatboxMessage(notification);
        if (supportChatboxMessage is not null)
        {
            ChatMessageReceived?.Invoke(supportChatboxMessage);
        }

        var chatActivity = ParseChatActivityNotification(notification);
        if (chatActivity is not null)
        {
            ChatActivityReceived?.Invoke(chatActivity);
        }

        if (TryHandleStreamStateNotification(notification, cancellationToken))
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

        if (bridgeEvent is { IsChatCommandTrigger: true }
            && await TryHandleDevChatCommandAsync(bridgeEvent, cancellationToken))
        {
            return;
        }

        if (bridgeEvent is { IsChatCommandTrigger: true }
            && await TryHandleWorldCommandAsync(configuration, bridgeEvent, cancellationToken))
        {
            return;
        }

        if (bridgeEvent is { IsChatCommandTrigger: true }
            && await TryHandleTriggerInfoReminderCommandAsync(configuration, bridgeEvent, cancellationToken))
        {
            return;
        }

        if (bridgeEvent is { IsChatCommandTrigger: true }
            && TryHandlePauseCommand(configuration, bridgeEvent))
        {
            return;
        }

        if (bridgeEvent is { IsChatCommandTrigger: true }
            && TryHandleGroupToggleCommand(configuration, bridgeEvent))
        {
            return;
        }

        if (bridgeEvent is { IsChatCommandTrigger: true }
            && TryHandleRedeemControlCommand(configuration, bridgeEvent))
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

        // Try Wardrobe first (individual outfit rewards and master reward with typed input)
        if (!bridgeEvent.IsChatCommandTrigger
            && bridgeEvent.TriggerType == TwitchTriggerType.ChannelPoints
            && await TryExecuteWardrobeFromRedemptionAsync(
                configuration,
                bridgeEvent.RewardId?.Trim() ?? string.Empty,
                bridgeEvent.RewardUserInput,
                cancellationToken))
        {
            return;
        }

        var currentAvatarId = GetCurrentVrChatAvatarId();
        var temporarilyDisabledRuleIds = GetTemporarilyDisabledRuleIds();
        var avatarChangeTransitionActive = IsAvatarChangeTransitionActive();
        if (!bridgeEvent.IsChatCommandTrigger
            && bridgeEvent.TriggerType == TwitchTriggerType.ChannelPoints
            && await TryHandleActiveFloatBoostRewardAsync(
                ruleIndex,
                bridgeEvent,
                currentAvatarId,
                avatarChangeTransitionActive,
                temporarilyDisabledRuleIds,
                cancellationToken))
        {
            return;
        }

        if (!bridgeEvent.IsChatCommandTrigger
            && bridgeEvent.TriggerType == TwitchTriggerType.PowerUp)
        {
            await HandlePowerUpEventAsync(
                configuration,
                bridgeEvent,
                currentAvatarId,
                avatarChangeTransitionActive,
                temporarilyDisabledRuleIds,
                avatarScaleHandled,
                cancellationToken);
            return;
        }

        var matchingRules = bridgeEvent.IsChatCommandTrigger
            ? SelectMatchingChatCommandRules(
                configuration,
                ruleIndex,
                bridgeEvent,
                currentAvatarId,
                avatarChangeTransitionActive,
                temporarilyDisabledRuleIds)
            : SelectMatchingRules(
                configuration,
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

        var fireSaleContributionHandled = TryBuildRewardFireSaleContribution(bridgeEvent, out var fireSaleContribution)
            && RewardFireSaleContributionReceived?.Invoke(fireSaleContribution) == true;

        if (matchingRules.Length == 0 && fireSaleContributionHandled)
        {
            return;
        }

        if (matchingRules.Length == 0
            && !bridgeEvent.IsChatCommandTrigger
            && bridgeEvent.TriggerType == TwitchTriggerType.ChannelPoints
            && TryQueueAvatarSwitchTriggerDuringSupporterOverride(
                ruleIndex,
                bridgeEvent,
                currentAvatarId,
                configuration.AvatarChangeCooldownOnlyModeEnabled))
        {
            return;
        }

        if (matchingRules.Length == 0
            && !bridgeEvent.IsChatCommandTrigger
            && bridgeEvent.TriggerType == TwitchTriggerType.ChannelPoints
            && TryQueueAvatarSwitchTriggerDuringActiveAvatarSwitch(
                ruleIndex,
                bridgeEvent,
                currentAvatarId,
                configuration.AvatarChangeCooldownOnlyModeEnabled))
        {
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

    private async Task HandleCashPaymentEventAsync(CashPaymentEvent paymentEvent, CancellationToken cancellationToken)
    {
        var eventKey = string.IsNullOrWhiteSpace(paymentEvent.EventId)
            ? $"cash:{paymentEvent.Provider}:{paymentEvent.UserDisplayName}:{paymentEvent.Amount:0.####}:{paymentEvent.CurrencyCode}:{paymentEvent.ReceivedAt:O}"
            : $"cash:{paymentEvent.Provider}:{paymentEvent.EventId}";
        if (!RememberMessage(eventKey))
        {
            return;
        }

        var configuration = activeConfiguration;
        if (configuration is null)
        {
            return;
        }

        var matchingRules = configuration.CashPaymentRules
            .Where(rule => CashPaymentRuleMatches(rule, paymentEvent))
            .ToArray();
        if (matchingRules.Length == 0)
        {
            WriteLog($"Received {DescribeCashPaymentProvider(paymentEvent.Provider)} cash payment, but no enabled cash payment rule matched its filters.");
            return;
        }

        if (AreRedeemsPaused())
        {
            LogRedeemsPaused();
            return;
        }

        foreach (var rule in matchingRules)
        {
            var amountUnits = Math.Max(1, (int)Math.Floor(paymentEvent.Amount));
            if (rule.ActionKind == CashPaymentActionKind.AvatarScaling && rule.ScaleAction is not null)
            {
                var incomingEvent = new UniversalIncomingEvent(
                    UniversalTriggerType.Bits,
                    string.IsNullOrWhiteSpace(paymentEvent.UserDisplayName) ? "Cash supporter" : paymentEvent.UserDisplayName,
                    string.Empty,
                    string.Empty,
                    amountUnits,
                    null,
                    null,
                    paymentEvent.Message,
                    string.Empty,
                    0,
                    [],
                    false,
                    false);
                WriteLog($"{paymentEvent.UserDisplayName} triggered cash payment scale '{rule.Name}' through {DescribeCashPaymentProvider(paymentEvent.Provider)}.");
                await ExecuteAvatarScaleRuleAsync(rule.ScaleAction, incomingEvent, isTest: false, cancellationToken, isResuming: false);
                continue;
            }

            if (rule.TriggerAction is null)
            {
                continue;
            }

            var bridgeEvent = new BridgeIncomingEvent(
                TwitchTriggerType.Bits,
                string.IsNullOrWhiteSpace(paymentEvent.UserDisplayName) ? "Cash supporter" : paymentEvent.UserDisplayName,
                amountUnits,
                null,
                null,
                DescribeCashPaymentProvider(paymentEvent.Provider),
                false,
                string.Empty,
                string.Empty,
                string.Empty,
                [],
                false,
                false)
            {
                MessageText = paymentEvent.Message,
                RewardUserInput = paymentEvent.Message
            };

            WriteLog($"{bridgeEvent.UserDisplayName} triggered cash payment rule '{rule.Name}' through {DescribeCashPaymentProvider(paymentEvent.Provider)}.");
            await ExecuteRuleAsync(rule.TriggerAction, bridgeEvent, cancellationToken);
        }
    }

    private async Task HandlePowerUpEventAsync(
        BridgeRuntimeConfiguration configuration,
        BridgeIncomingEvent bridgeEvent,
        string currentAvatarId,
        bool avatarChangeTransitionActive,
        IReadOnlyCollection<Guid> temporarilyDisabledRuleIds,
        bool avatarScaleHandled,
        CancellationToken cancellationToken)
    {
        var matchingRules = SelectMatchingPowerUpRules(
            configuration.PowerUpRules,
            bridgeEvent,
            currentAvatarId,
            avatarChangeTransitionActive,
            temporarilyDisabledRuleIds,
            out var inactiveAvatarMatches);

        if (AreRedeemsPaused())
        {
            LogRedeemsPaused();
            return;
        }

        var fireSaleContributionHandled = TryBuildRewardFireSaleContribution(bridgeEvent, out var fireSaleContribution)
            && RewardFireSaleContributionReceived?.Invoke(fireSaleContribution) == true;

        if (matchingRules.Length == 0)
        {
            foreach (var inactiveMatch in inactiveAvatarMatches)
            {
                LogInactivePowerUpAvatarScope(inactiveMatch, currentAvatarId);
            }

            if (!avatarScaleHandled && !fireSaleContributionHandled)
            {
                var label = !string.IsNullOrWhiteSpace(bridgeEvent.RewardTitle)
                    ? bridgeEvent.RewardTitle
                    : bridgeEvent.RewardId ?? "unknown Power Up";
                WriteLog($"No active Power Up rule matched '{label}'.");
            }

            return;
        }

        foreach (var rule in matchingRules)
        {
            if (rule.ActionKind == PowerUpActionKind.AvatarScaling && rule.ScaleAction is not null)
            {
                StartAvatarScaleRuleExecution(rule.ScaleAction, ToUniversalPowerUpEvent(bridgeEvent));
                continue;
            }

            if (rule.TriggerAction is not null)
            {
                await ExecuteRuleAsync(rule.TriggerAction, bridgeEvent, cancellationToken);
            }
        }
    }

    private PowerUpRuleSnapshot[] SelectMatchingPowerUpRules(
        IReadOnlyList<PowerUpRuleSnapshot> rules,
        BridgeIncomingEvent bridgeEvent,
        string currentAvatarId,
        bool avatarChangeTransitionActive,
        IReadOnlyCollection<Guid> temporarilyDisabledRuleIds,
        out IReadOnlyList<PowerUpRuleSnapshot> inactiveAvatarMatches)
    {
        var inactiveMatches = new List<PowerUpRuleSnapshot>();
        var matches = new List<PowerUpRuleSnapshot>();
        foreach (var rule in rules)
        {
            if (!PowerUpIdentityMatches(rule, bridgeEvent))
            {
                continue;
            }

            if (!rule.IsEnabled || temporarilyDisabledRuleIds.Contains(rule.Id))
            {
                continue;
            }

            if (!PowerUpRuleIsActiveForCurrentAvatar(rule, currentAvatarId, avatarChangeTransitionActive))
            {
                inactiveMatches.Add(rule);
                continue;
            }

            matches.Add(rule);
        }

        inactiveAvatarMatches = inactiveMatches;
        return [.. matches];
    }

    private static bool PowerUpIdentityMatches(PowerUpRuleSnapshot rule, BridgeIncomingEvent bridgeEvent)
    {
        var configuredId = rule.PowerUpId.Trim();
        var incomingId = bridgeEvent.RewardId?.Trim() ?? string.Empty;
        if (!string.IsNullOrWhiteSpace(configuredId))
        {
            return string.Equals(configuredId, incomingId, StringComparison.Ordinal);
        }

        var configuredTitle = NormalizePowerUpTitle(rule.PowerUpTitle);
        var incomingTitle = NormalizePowerUpTitle(bridgeEvent.RewardTitle);
        return !string.IsNullOrWhiteSpace(configuredTitle)
            && string.Equals(configuredTitle, incomingTitle, StringComparison.OrdinalIgnoreCase);
    }

    private static bool PowerUpRuleIsActiveForCurrentAvatar(
        PowerUpRuleSnapshot rule,
        string currentAvatarId,
        bool avatarChangeTransitionActive)
    {
        if (rule.TriggerAction is not null)
        {
            return AvatarRuleActivationPolicy.IsRuleActiveForCurrentAvatar(
                rule.TriggerAction.IsGlobalOverride,
                rule.TriggerAction.BelongsToMasterAvatarProfile,
                rule.TriggerAction.ActionType,
                rule.TriggerAction.AvatarChangeTargetId,
                rule.TriggerAction.RequiredAvatarId,
                currentAvatarId,
                avatarChangeTransitionActive);
        }

        if (!rule.AvatarScoped)
        {
            return true;
        }

        var normalizedRequiredAvatarId = rule.AvatarId.Trim();
        var normalizedCurrentAvatarId = currentAvatarId?.Trim() ?? string.Empty;
        return !string.IsNullOrWhiteSpace(normalizedRequiredAvatarId)
            && string.Equals(normalizedRequiredAvatarId, normalizedCurrentAvatarId, StringComparison.Ordinal);
    }

    private void LogInactivePowerUpAvatarScope(PowerUpRuleSnapshot rule, string currentAvatarId)
    {
        var now = DateTimeOffset.UtcNow;
        lock (stateGate)
        {
            if (powerUpInactiveAvatarLogTimes.TryGetValue(rule.Id, out var nextLogAt)
                && nextLogAt > now)
            {
                return;
            }

            powerUpInactiveAvatarLogTimes[rule.Id] = now.Add(PowerUpInactiveAvatarLogThrottle);
        }

        var currentText = string.IsNullOrWhiteSpace(currentAvatarId) ? "no detected avatar" : currentAvatarId;
        var requiredText = rule.TriggerAction?.RequiredAvatarName
            ?? rule.AvatarName
            ?? string.Empty;
        if (string.IsNullOrWhiteSpace(requiredText))
        {
            requiredText = rule.TriggerAction?.RequiredAvatarId ?? rule.AvatarId;
        }

        WriteLog($"Ignored Power Up '{rule.Name}' because it is not active for the current avatar ({currentText}). Required: {requiredText}.");
    }

    private static string NormalizePowerUpTitle(string? title) =>
        ManagedRewardPresentation.NormalizeTitleIdentityKey(title ?? string.Empty);

    private static UniversalIncomingEvent ToUniversalPowerUpEvent(BridgeIncomingEvent bridgeEvent)
    {
        return new UniversalIncomingEvent(
            UniversalTriggerType.Bits,
            bridgeEvent.UserDisplayName,
            bridgeEvent.UserId,
            bridgeEvent.UserLogin,
            Math.Max(1, bridgeEvent.Amount),
            bridgeEvent.RewardId,
            bridgeEvent.RewardTitle,
            string.IsNullOrWhiteSpace(bridgeEvent.MessageText) ? bridgeEvent.RewardUserInput : bridgeEvent.MessageText,
            string.Empty,
            0,
            bridgeEvent.BadgeSetIds,
            bridgeEvent.UserIsModerator,
            bridgeEvent.UserIsBroadcaster);
    }

    private static bool CashPaymentRuleMatches(CashPaymentRuleSnapshot rule, CashPaymentEvent paymentEvent)
    {
        if (!rule.IsEnabled || rule.Provider != paymentEvent.Provider)
        {
            return false;
        }

        if (paymentEvent.Amount < rule.MinimumAmount)
        {
            return false;
        }

        if (rule.MaximumAmount > 0 && paymentEvent.Amount > rule.MaximumAmount)
        {
            return false;
        }

        if (!string.IsNullOrWhiteSpace(rule.CurrencyCode)
            && !string.Equals(rule.CurrencyCode.Trim(), paymentEvent.CurrencyCode?.Trim(), StringComparison.OrdinalIgnoreCase))
        {
            return false;
        }

        return string.IsNullOrWhiteSpace(rule.MessageContains)
            || (!string.IsNullOrWhiteSpace(paymentEvent.Message)
                && paymentEvent.Message.Contains(rule.MessageContains, StringComparison.OrdinalIgnoreCase));
    }

    private static string DescribeCashPaymentProvider(CashPaymentProvider provider) => provider switch
    {
        CashPaymentProvider.Streamlabs => "Streamlabs",
        CashPaymentProvider.KoFi => "Ko-fi",
        _ => "StreamElements"
    };

    private object BuildSubscriptionCondition(string subscriptionType)
    {
        if (broadcaster is null)
        {
            throw new InvalidOperationException("The broadcaster session is missing.");
        }

        if (subscriptionType is "channel.chat.message"
            or "channel.chat.message_delete"
            or "channel.chat.clear_user_messages"
            or "channel.chat.clear")
        {
            return new
            {
                broadcaster_user_id = broadcaster.UserId,
                user_id = broadcaster.UserId
            };
        }

        if (subscriptionType is "channel.suspicious_user.update" or "channel.suspicious_user.message")
        {
            return new
            {
                broadcaster_user_id = broadcaster.UserId,
                moderator_user_id = broadcaster.UserId
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

        if (string.Equals(subscriptionType, "channel.raid", StringComparison.Ordinal))
        {
            return new
            {
                to_broadcaster_user_id = broadcaster.UserId
            };
        }

        return new
        {
            broadcaster_user_id = broadcaster.UserId
        };
    }

    private static bool IsSupporterOverrideRule(TriggerRuleSnapshot rule) =>
        rule.IsGlobalOverride && rule.TriggerType is TwitchTriggerType.Bits or TwitchTriggerType.Subscriptions;

    private static bool IsPaidAvatarChangeBypassRule(TriggerRuleSnapshot rule) =>
        IsSupporterOverrideRule(rule) || rule.TriggerType == TwitchTriggerType.PowerUp;

    private static bool IsTimedSupporterOverrideRule(TriggerRuleSnapshot rule) =>
        (IsSupporterOverrideRule(rule) || IsPowerUpFixedFloatAddRule(rule))
        && rule.ActionType != OscActionType.SetTrigger
        && (rule.AmountScaledDurationEnabled || rule.DurationSeconds > 0);

    private static bool IsSupporterFloatAddRule(TriggerRuleSnapshot rule) =>
        (IsSupporterOverrideRule(rule) || rule.TriggerType == TwitchTriggerType.PowerUp)
        && rule.ActionType == OscActionType.AvatarParameter
        && rule.ParameterType == OscParameterType.Float
        && rule.DurationSeconds > 0
        && rule.SupporterFloatAddEnabled;

    private static bool IsPowerUpFixedFloatAddRule(TriggerRuleSnapshot rule) =>
        rule.TriggerType == TwitchTriggerType.PowerUp
        && rule.ActionType == OscActionType.AvatarParameter
        && rule.ParameterType == OscParameterType.Float
        && rule.DurationSeconds > 0
        && rule.SupporterFloatAddEnabled;

    private static bool IsTimedAvatarSwitchRule(TriggerRuleSnapshot rule) =>
        rule.ActionType is OscActionType.AvatarChange or OscActionType.AvatarRoulet
        && rule.DurationSeconds > 0;

    private static bool IsQueuedAvatarSwitchRule(TriggerRuleSnapshot rule) =>
        rule.TriggerType == TwitchTriggerType.ChannelPoints
        && !rule.IsGlobalOverride
        && IsTimedAvatarSwitchRule(rule);

    private static bool IsPauseableAvatarSwitchReset(PendingResetState reset) =>
        IsQueuedAvatarSwitchRule(reset.Rule)
        && reset.Action.HasResetPackets
        && !string.IsNullOrWhiteSpace(reset.Action.AvatarTargetId);

    private static bool IsBitsOutfitSetTriggerRule(TriggerRuleSnapshot rule) =>
        rule.IsGlobalOverride
        && rule.TriggerType == TwitchTriggerType.Bits
        && rule.ActionType == OscActionType.SetTrigger;

    private static bool IsBitsForceMovementRule(TriggerRuleSnapshot rule) =>
        rule.IsGlobalOverride
        && rule.TriggerType == TwitchTriggerType.Bits
        && rule.ActionType == OscActionType.PlayerMovement;

    private bool ShouldBlockAvatarChangeDuringActiveScaling(TriggerRuleSnapshot rule, bool isTest)
    {
        if (isTest
            || activeConfiguration?.AvatarScaleMasterReward.PreventAvatarChangesDuringActiveScaling != true
            || rule.ActionType is not (OscActionType.AvatarChange or OscActionType.AvatarRoulet)
            || IsPaidAvatarChangeBypassRule(rule))
        {
            return false;
        }

        return IsAvatarScalingActiveForAvatarChangeBlock();
    }

    private bool IsCooldownOnlyDirectAvatarChange(TriggerRuleSnapshot rule) =>
        activeConfiguration?.AvatarChangeCooldownOnlyModeEnabled == true
        && !rule.IsGlobalOverride
        && rule.BelongsToMasterAvatarProfile
        && rule.ActionType is OscActionType.AvatarChange or OscActionType.AvatarRoulet;

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
        IsPaidAvatarChangeBypassRule(rule)
            ? AvatarScaleAvatarChangeCarryoverMode.ForcePaidOverride
            : AvatarScaleAvatarChangeCarryoverMode.Auto;

    private void LogPaidAvatarChangeAllowedDuringActiveScaling(TriggerRuleSnapshot rule)
    {
        if (!IsPaidAvatarChangeBypassRule(rule)
            || rule.ActionType is not (OscActionType.AvatarChange or OscActionType.AvatarRoulet)
            || !IsAvatarScalingActiveForAvatarChangeBlock())
        {
            return;
        }

        WriteLog($"Paid avatar-change '{rule.Name}' is allowed while Avatar Scaling is active, so Crystal Relay will carry the active scale height.");
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
            await universalTriggerGlobalGate.WaitAsync(cancellationToken);
            try
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
            finally
            {
                universalTriggerGlobalGate.Release();
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

        var normalizedAddress = action.OscAddress?.Trim() ?? string.Empty;
        if (normalizedAddress.StartsWith("avatar/parameters/", StringComparison.Ordinal))
        {
            normalizedAddress = $"/{normalizedAddress}";
        }

        return normalizedAddress.StartsWith("/", StringComparison.Ordinal)
            ? vrChatOscClient.BuildPacketForAddress(normalizedAddress, parameterType, rawValue)
            : vrChatOscClient.BuildAvatarParameterPacket(normalizedAddress, parameterType, rawValue);
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
                await ExecuteAvatarScaleRuleAsync(rule, incomingEvent, isTest: false, executionToken, isResuming: false);
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
                        if (activeAvatarScaleOperation is { } activeScaleOperation
                            && (!nextOperation.IsTest
                                || activeScaleOperation.IsTransitionActive
                                || activeScaleOperation.Priority > AvatarScaleOperationPriority.LiveRedeem))
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
                                else if (IsAvatarScaleRewardOverlayBlockedByPaidGrowthLocked(currentRule, now))
                                {
                                    delay = AvatarScaleQueuePollDelay;
                                    if (!nextOperation.PaidGrowthWaitLogged)
                                    {
                                        nextOperation.PaidGrowthWaitLogged = true;
                                        logMessage = $"Queued avatar scale '{currentRule.Name}' is waiting because paid Supporter Growth is active and reward scale changes are disabled.";
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
                            cancellationToken,
                            isResuming: false);
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
        CancellationToken cancellationToken,
        bool isResuming = false)
    {
        if (rule.TriggerType == AvatarScaleTriggerType.SupporterGrowth)
        {
            await ExecuteSupporterGrowthAvatarScaleRuleAsync(rule, incomingEvent, isTest, cancellationToken);
            return true;
        }

        if (rule.TriggerType == AvatarScaleTriggerType.Follow && !isTest && !isResuming)
        {
            lock (stateGate)
            {
                if (!avatarScaleFollowTriggeredUsers.TryGetValue(rule.Id, out var triggeredUsers))
                {
                    triggeredUsers = new HashSet<string>(StringComparer.Ordinal);
                    avatarScaleFollowTriggeredUsers[rule.Id] = triggeredUsers;
                }

                if (!string.IsNullOrWhiteSpace(incomingEvent.UserId) && triggeredUsers.Contains(incomingEvent.UserId))
                {
                    WriteLog($"Avatar scale '{rule.Name}' skipped because {incomingEvent.UserDisplayName} has already triggered this follow rule.");
                    return true;
                }
            }
        }

        var cooldownSeconds = isResuming ? 0 : GetAvatarScaleEffectiveCooldownSeconds(rule);
        if (!isTest && !isResuming)
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
            AvatarScaleOperationPriority.LiveRedeem,
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
            if (rule.ScaleMode == AvatarScaleMode.GlitchyRandomHeight)
            {
                return await ExecuteGlitchyAvatarScaleRuleAsync(
                    operation,
                    rule,
                    incomingEvent,
                    isTest,
                    cooldownSeconds,
                    cancellationToken);
            }

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

                if (!isResuming)
                {
                    UpdateActiveAvatarScaleRuleLockoutState(rule);
                }

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

                if (!isResuming)
                {
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

                        if (rule.TriggerType == AvatarScaleTriggerType.Follow && !string.IsNullOrWhiteSpace(incomingEvent.UserId))
                        {
                            if (!avatarScaleFollowTriggeredUsers.TryGetValue(rule.Id, out var triggeredUsers))
                            {
                                triggeredUsers = new HashSet<string>(StringComparer.Ordinal);
                                avatarScaleFollowTriggeredUsers[rule.Id] = triggeredUsers;
                            }
                            triggeredUsers.Add(incomingEvent.UserId);
                        }
                    }

                    if (cooldownSeconds > 0)
                    {
                        ScheduleCooldownStateNotification(rule.Id, TimeSpan.FromSeconds(cooldownSeconds));
                        ManagedRewardAvailabilityChanged?.Invoke();
                        NotifyRewardCooldownColorChanged(rule.Id);
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
                ScheduleAvatarScaleRestoreSequence(rule, isTest, targetHeight);
                if (!isTest && !isResuming)
                {
                    var effectDurationSeconds = GetAvatarScaleEffectDurationSeconds(rule);
                    var expiresAt = effectDurationSeconds > 0 ? DateTimeOffset.UtcNow.AddSeconds(effectDurationSeconds) : (DateTimeOffset?)null;
                    await activityResumeService.RecordActivityStartedAsync(new ResumeActivity
                    {
                        Type = ResumeActivityType.AvatarScale,
                        RuleId = rule.Id,
                        ExpiresAt = expiresAt,
                        CurrentValue = targetHeight,
                        Payload = new Dictionary<string, object>
                        {
                            ["scaleMode"] = rule.ScaleMode.ToString(),
                            ["targetHeight"] = targetHeight
                        }
                    }, GetCurrentVrChatAvatarId());
                }
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

    private async Task<bool> ExecuteGlitchyAvatarScaleRuleAsync(
        ActiveAvatarScaleOperationTicket operation,
        AvatarScaleRuleSnapshot rule,
        UniversalIncomingEvent incomingEvent,
        bool isTest,
        int cooldownSeconds,
        CancellationToken cancellationToken)
    {
        if (rule.ActiveTimeSeconds <= 0)
        {
            WriteLog($"Avatar scale '{rule.Name}' skipped because Glitchy Random Height needs Active Time above 0 seconds.");
            return true;
        }

        var minimumHeight = ApplyAvatarScaleHeightLimits(
            rule,
            Math.Min(rule.MinimumHeightMeters, rule.MaximumHeightMeters),
            "glitchy minimum height");
        var maximumHeight = ApplyAvatarScaleHeightLimits(
            rule,
            Math.Max(rule.MinimumHeightMeters, rule.MaximumHeightMeters),
            "glitchy maximum height");
        if (maximumHeight <= minimumHeight)
        {
            WriteLog($"Avatar scale '{rule.Name}' skipped because the Glitchy Random Height range collapsed after height limits were applied.");
            return true;
        }

        var duration = TimeSpan.FromSeconds(Math.Max(1, rule.ActiveTimeSeconds));
        var heightSessionId = Guid.Empty;
        var heightSessionEndScheduled = false;
        var rewardStateStarted = false;
        var restoreScheduled = false;
        var firstScaleSendStarted = false;

        void StartRewardStateAfterFirstScaleSend()
        {
            firstScaleSendStarted = true;
            if (!restoreScheduled)
            {
                restoreScheduled = true;
                ScheduleAvatarScaleRestoreSequence(rule, isTest, PickDevRandomAvatarScaleHeight(minimumHeight, maximumHeight));
            }

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
            var carriedHeight = PickDevRandomAvatarScaleHeight(minimumHeight, maximumHeight);
            heightSessionId = StartAvatarScaleHeightSession(
                rule.Id,
                rule.Name,
                rule.RestoreHeightMeters,
                carriedHeight,
                effectDurationSeconds);
            if (heightSessionId != Guid.Empty)
            {
                heightSessionEndScheduled = true;
                ScheduleAvatarScaleHeightSessionEnd(
                    rule.Id,
                    heightSessionId,
                    TimeSpan.FromSeconds(Math.Max(0.5, effectDurationSeconds)),
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
                NotifyRewardCooldownColorChanged(rule.Id);
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

        try
        {
            await RunRandomAvatarScaleSequenceAsync(
                operation,
                minimumHeight,
                maximumHeight,
                duration,
                Math.Clamp(rule.SmoothTransitionSeconds, 0, 30),
                StartRewardStateAfterFirstScaleSend,
                cancellationToken);

            if (!firstScaleSendStarted)
            {
                return false;
            }

            WriteLog(isTest
                ? $"Sent glitchy avatar scale test/simulated effect for '{rule.Name}' between {minimumHeight:0.###}m and {maximumHeight:0.###}m for {DescribeDuration(duration.TotalSeconds)}."
                : $"{incomingEvent.UserDisplayName} triggered glitchy avatar scale '{rule.Name}' between {minimumHeight:0.###}m and {maximumHeight:0.###}m for {DescribeDuration(duration.TotalSeconds)}.");
            return true;
        }
        finally
        {
            if (!heightSessionEndScheduled)
            {
                EndAvatarScaleHeightSession(rule.Id, heightSessionId);
            }
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
        bool isTest,
        double carriedHeightMeters)
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
                carriedHeightMeters,
                ApplyAvatarScaleHeightLimits(rule, restoreHeight, "return height"),
                now.AddSeconds(Math.Max(0.001, rule.ActiveTimeSeconds)),
                sourceRuleName,
                Math.Max(0, rule.SmoothTransitionSeconds),
                RestoreToPaidGrowthIfActive: true,
                isTest);
            previousCancellation = avatarScaleRestoreSequenceCancellation;
            avatarScaleRestoreSequenceCancellation = newCancellation;
            activeAvatarScaleRestoreSequence = sequence;
            UpdateActiveAvatarScaleCarryoverRestoreSequenceLocked(rule.Id, sequence);
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
                var currentAvatarId = GetCurrentVrChatAvatarId();
                if (!string.Equals(currentAvatarId, sequence.AvatarId, StringComparison.Ordinal))
                {
                    if (string.IsNullOrWhiteSpace(currentAvatarId))
                    {
                        ClearAvatarScaleRestoreSequenceIfCurrent(sequence.SequenceId);
                        WriteLog($"Avatar scale restore from '{sequence.SourceRuleName}' was skipped because Crystal Relay no longer knows the current avatar.");
                        return;
                    }

                    RetargetAvatarScaleRestoreSequenceForAvatarChange(currentAvatarId);
                    lock (stateGate)
                    {
                        if (activeAvatarScaleRestoreSequence?.SequenceId != sequence.SequenceId)
                        {
                            return;
                        }

                        sequence = activeAvatarScaleRestoreSequence;
                    }
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

                    var restoreHeightMeters = sequence.RestoreHeightMeters;
                    var restoringToPaidGrowth = false;
                    if (sequence.RestoreToPaidGrowthIfActive
                        && TryGetActiveSupporterGrowthPaidTargetHeight(out var paidTargetHeight, out _))
                    {
                        restoreHeightMeters = paidTargetHeight;
                        restoringToPaidGrowth = true;
                    }

                    if (!await SendAvatarHeightForOperationAsync(
                            operation,
                            restoreHeightMeters,
                            sequence.RestoreSmoothTransitionSeconds,
                            cancellationToken))
                    {
                        await Task.Delay(AvatarScaleQueuePollDelay, cancellationToken);
                        continue;
                    }

                    ClearPendingAvatarScaleHeightRestoreForCurrentAvatar();
                    ClearAvatarScaleRestoreSequenceIfCurrent(sequence.SequenceId);
                    WriteLog(restoringToPaidGrowth
                        ? $"Avatar scale returned to the active paid Supporter Growth height of {restoreHeightMeters:0.###}m after the reward timer ended."
                        : $"Avatar scale returned to the configured return height of {restoreHeightMeters:0.###}m after the inactive timer ended.");
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

            if (activeAvatarScaleCarryover?.RestoreSequenceId == sequenceId)
            {
                activeAvatarScaleCarryover = null;
            }
        }
    }

    private async Task ExecuteSupporterGrowthAvatarScaleRuleAsync(
        AvatarScaleRuleSnapshot rule,
        UniversalIncomingEvent incomingEvent,
        bool isTest,
        CancellationToken cancellationToken)
    {
        var bitHeightDirection = 1;
        if (!isTest
            && incomingEvent.TriggerType == UniversalTriggerType.Bits
            && !TryResolveSupporterGrowthBitsHeightDirection(
                rule,
                incomingEvent.ChatMessageText,
                out bitHeightDirection,
                out var directionDiagnostic))
        {
            WriteLog(directionDiagnostic ?? TF("Avatar scale '{0}' skipped because the cheer text matched both grow and shrink keywords.", rule.Name));
            return;
        }

        var addedHeight = GetSupporterGrowthHeightAdd(rule, incomingEvent, isTest, bitHeightDirection);
        if (addedHeight == 0)
        {
            WriteLog($"Avatar scale '{rule.Name}' skipped because this supporter event does not match a configured tier or bits range.");
            return;
        }

        var addedPaidSeconds = GetSupporterGrowthAddedTimeSeconds(rule, incomingEvent, isTest);
        if (addedPaidSeconds <= 0)
        {
            WriteLog($"Avatar scale '{rule.Name}' skipped because this supporter event has no paid active time configured.");
            return;
        }

        var operationPriority = isTest
            ? AvatarScaleOperationPriority.LiveRedeem
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
                        rule.SupporterGrowthTransitionSeconds,
                        cancellationToken))
                {
                    return;
                }

                await Task.Delay(TimeSpan.FromSeconds(Math.Max(1, addedPaidSeconds)), cancellationToken);
                if (!IsAvatarScaleOperationCurrent(operation))
                {
                    WriteLog($"Supporter growth test/simulated effect '{rule.Name}' skipped its paid timer reset because a newer scale effect is active.");
                    return;
                }

                await SendAvatarHeightForOperationAsync(
                    operation,
                    normalHeight,
                    rule.SupporterGrowthTransitionSeconds,
                    cancellationToken);

                WriteLog($"Sent supporter growth test/simulated effect for '{rule.Name}' to {testTargetHeight:0.###}m (+{addedHeight:0.###}m) for {DescribeDuration(addedPaidSeconds)}.");
                return;
            }

            CancellationTokenSource? previousSessionCancellation;
            CancellationTokenSource sessionCancellation;
            double totalAddedHeight;
            double targetHeight;
            double remainingPaidSeconds;
            DateTimeOffset paidActiveUntil;

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
                    ? Math.Clamp(
                        requestedAddedHeight,
                        -Math.Abs(rule.SupporterGrowthMaxAddedHeightMeters),
                        Math.Abs(rule.SupporterGrowthMaxAddedHeightMeters))
                    : requestedAddedHeight;
                totalAddedHeight = state.AddedHeightMeters;
                targetHeight = ApplyAvatarScaleHeightLimits(rule, normalHeight + totalAddedHeight, "supporter growth height");

                var now = DateTimeOffset.UtcNow;
                var currentRemainingSeconds = Math.Max(0, (state.PaidActiveUntil - now).TotalSeconds);
                remainingPaidSeconds = CalculateSupporterGrowthPaidRemainingSeconds(
                    currentRemainingSeconds,
                    addedPaidSeconds,
                    rule);
                paidActiveUntil = now.AddSeconds(remainingPaidSeconds);
                state.PaidActiveUntil = paidActiveUntil;
                state.CurrentTargetHeightMeters = targetHeight;
                state.NormalHeightMeters = normalHeight;
                state.AllowRewardScaleOverlay = rule.SupporterGrowthAllowRewardScaleOverlay;
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
                    rule.SupporterGrowthTransitionSeconds,
                    paidActiveUntil,
                    rule.SupporterGrowthAllowRewardScaleOverlay,
                    sessionCancellation),
                CancellationToken.None);

            WriteLog($"{incomingEvent.UserDisplayName} changed supporter growth '{rule.Name}' by {addedHeight:+0.###;-0.###;0}m and added {DescribeDuration(addedPaidSeconds)} for a target of {targetHeight:0.###}m. Paid time remaining: {DescribeDuration(remainingPaidSeconds)}.");
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
        DateTimeOffset paidActiveUntil,
        bool allowRewardScaleOverlay,
        CancellationTokenSource sessionCancellation)
    {
        var heightSessionId = Guid.Empty;
        var transitionOperationEnded = false;
        ActiveAvatarScaleOperationTicket? restoreOperation = null;
        try
        {
            var cancellationToken = sessionCancellation.Token;
            var activeWindowSeconds = Math.Max(1, (paidActiveUntil - DateTimeOffset.UtcNow).TotalSeconds + smoothTransitionSeconds);
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

            if (allowRewardScaleOverlay)
            {
                EndAvatarScaleOperation(operation);
                transitionOperationEnded = true;
            }

            var remainingDelay = paidActiveUntil - DateTimeOffset.UtcNow;
            if (remainingDelay > TimeSpan.Zero)
            {
                await Task.Delay(remainingDelay, cancellationToken);
            }

            lock (stateGate)
            {
                if (!avatarScaleSupporterGrowthStates.TryGetValue(ruleId, out var state)
                    || !ReferenceEquals(state.SessionCancellation, sessionCancellation))
                {
                    return;
                }
            }

            if (transitionOperationEnded)
            {
                restoreOperation = await WaitForAvatarScaleOperationSlotAsync(
                    ruleId,
                    ruleName,
                    AvatarScaleOperationPriority.SupporterGrowth,
                    isTest: false,
                    cancellationToken);
                if (restoreOperation is null)
                {
                    return;
                }
            }
            else if (!IsAvatarScaleOperationCurrent(operation))
            {
                WriteLog($"Supporter growth '{ruleName}' skipped its paid timer reset because a newer scale effect is active.");
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
            var resetOperation = restoreOperation ?? operation;
            if (await SendAvatarHeightForOperationAsync(resetOperation, restoreHeight, smoothTransitionSeconds, cancellationToken))
            {
                ClearPendingAvatarScaleHeightRestoreForCurrentAvatar();
                WriteLog($"Supporter growth '{ruleName}' returned to normal height after paid active time ended.");
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
            if (restoreOperation is not null)
            {
                EndAvatarScaleOperation(restoreOperation);
            }

            if (!transitionOperationEnded)
            {
                EndAvatarScaleOperation(operation);
            }

            sessionCancellation.Dispose();
        }
    }

    private static double GetSupporterGrowthHeightAdd(
        AvatarScaleRuleSnapshot rule,
        UniversalIncomingEvent incomingEvent,
        bool isTest,
        int bitHeightDirection = 1)
    {
        if (isTest)
        {
            return rule.SupporterGrowthTier1HeightMeters;
        }

        return incomingEvent.TriggerType switch
        {
            UniversalTriggerType.Bits => GetSupporterGrowthBitsHeightAdd(rule, incomingEvent.Amount, bitHeightDirection),
            UniversalTriggerType.Subscription => GetSupporterGrowthTierHeightAdd(rule, incomingEvent.SubscriptionTier),
            UniversalTriggerType.GiftSubscription => GetSupporterGrowthTierHeightAdd(rule, incomingEvent.SubscriptionTier)
                * Math.Max(1, incomingEvent.Amount),
            _ => 0
        };
    }

    private static double GetSupporterGrowthAddedTimeSeconds(
        AvatarScaleRuleSnapshot rule,
        UniversalIncomingEvent incomingEvent,
        bool isTest)
    {
        if (isTest)
        {
            return Math.Max(0, rule.SupporterGrowthTier1Seconds);
        }

        return incomingEvent.TriggerType switch
        {
            UniversalTriggerType.Bits => GetSupporterGrowthBitsTimeSeconds(rule, incomingEvent.Amount),
            UniversalTriggerType.Subscription => GetSupporterGrowthTierTimeSeconds(rule, incomingEvent.SubscriptionTier),
            UniversalTriggerType.GiftSubscription => GetSupporterGrowthTierTimeSeconds(rule, incomingEvent.SubscriptionTier)
                * Math.Max(1, incomingEvent.Amount),
            _ => 0
        };
    }

    private static double GetSupporterGrowthBitsTimeSeconds(AvatarScaleRuleSnapshot rule, int bits)
    {
        if (bits <= 0)
        {
            return 0;
        }

        var bitsUnit = Math.Max(1, rule.SupporterGrowthBitsTimerUnit);
        var secondsPerUnit = Math.Max(0, rule.SupporterGrowthSecondsPerBitsUnit);
        return bits / (double)bitsUnit * secondsPerUnit;
    }

    private static double GetSupporterGrowthTierTimeSeconds(AvatarScaleRuleSnapshot rule, string tier)
    {
        return tier.Trim() switch
        {
            "1000" => rule.SupporterGrowthTier1Seconds,
            "2000" => rule.SupporterGrowthTier2Seconds,
            "3000" => rule.SupporterGrowthTier3Seconds,
            _ => rule.SupporterGrowthTier1Seconds
        };
    }

    private static double CalculateSupporterGrowthPaidRemainingSeconds(
        double currentRemainingSeconds,
        double addedSeconds,
        AvatarScaleRuleSnapshot rule)
    {
        var maxSeconds = Math.Max(1, rule.SupporterGrowthMaxPaidTimeSeconds);
        var softCapSeconds = Math.Clamp(rule.SupporterGrowthSoftCapSeconds, 0, maxSeconds);
        var multiplier = Math.Clamp(rule.SupporterGrowthSoftCapMultiplierPercent, 0, 100) / 100d;
        var remaining = Math.Clamp(currentRemainingSeconds, 0, maxSeconds);
        var addition = Math.Max(0, addedSeconds);
        var fullCapacity = Math.Max(0, softCapSeconds - remaining);
        var fullAddition = Math.Min(addition, fullCapacity);
        var reducedAddition = Math.Max(0, addition - fullAddition) * multiplier;
        return Math.Clamp(remaining + fullAddition + reducedAddition, 0, maxSeconds);
    }

    private static double GetSupporterGrowthBitsHeightAdd(AvatarScaleRuleSnapshot rule, int bits, int direction)
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
                return Math.Abs(range.HeightAddedMeters) * (direction < 0 ? -1 : 1);
            }
        }

        return 0;
    }

    private static bool TryResolveSupporterGrowthBitsHeightDirection(
        AvatarScaleRuleSnapshot rule,
        string messageText,
        out int direction,
        out string? diagnostic)
    {
        direction = 1;
        diagnostic = null;

        var cheerText = ExtractBitsOutfitChoiceText(messageText);
        var growMatched = ContainsSupporterGrowthBitsKeyword(cheerText, rule.SupporterGrowthGrowKeyword);
        var shrinkMatched = ContainsSupporterGrowthBitsKeyword(cheerText, rule.SupporterGrowthShrinkKeyword);
        if (growMatched && shrinkMatched)
        {
            diagnostic = TF(
                "Avatar scale '{0}' skipped because cheer text matched both Supporter Growth keywords ('{1}' and '{2}').",
                rule.Name,
                rule.SupporterGrowthGrowKeyword,
                rule.SupporterGrowthShrinkKeyword);
            return false;
        }

        direction = shrinkMatched ? -1 : 1;
        return true;
    }

    private static bool ContainsSupporterGrowthBitsKeyword(string messageText, string keyword)
    {
        var normalizedMessage = NormalizeSupporterGrowthBitsKeywordText(messageText);
        var normalizedKeyword = NormalizeSupporterGrowthBitsKeywordText(keyword);
        if (string.IsNullOrWhiteSpace(normalizedMessage)
            || string.IsNullOrWhiteSpace(normalizedKeyword))
        {
            return false;
        }

        return string.Equals(normalizedMessage, normalizedKeyword, StringComparison.Ordinal)
            || normalizedMessage.StartsWith(normalizedKeyword + " ", StringComparison.Ordinal)
            || normalizedMessage.EndsWith(" " + normalizedKeyword, StringComparison.Ordinal)
            || normalizedMessage.Contains(" " + normalizedKeyword + " ", StringComparison.Ordinal);
    }

    private static string NormalizeSupporterGrowthBitsKeywordText(string value)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return string.Empty;
        }

        var builder = new StringBuilder(value.Length);
        var previousWasSpace = true;
        foreach (var character in value.Trim().ToLowerInvariant())
        {
            if (char.IsLetterOrDigit(character))
            {
                builder.Append(character);
                previousWasSpace = false;
            }
            else if (!previousWasSpace)
            {
                builder.Append(' ');
                previousWasSpace = true;
            }
        }

        return builder.ToString().Trim();
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

    private bool IsAvatarScaleRewardOverlayBlockedByPaidGrowthLocked(
        AvatarScaleRuleSnapshot rule,
        DateTimeOffset now)
    {
        if (rule.TriggerType is not (AvatarScaleTriggerType.ChannelPointReward or AvatarScaleTriggerType.ChatCommand))
        {
            return false;
        }

        return avatarScaleSupporterGrowthStates.Values.Any(state =>
            state.PaidActiveUntil > now
            && !state.AllowRewardScaleOverlay);
    }

    private bool TryGetActiveSupporterGrowthPaidTargetHeight(
        out double targetHeightMeters,
        out string ruleName)
    {
        var now = DateTimeOffset.UtcNow;
        lock (stateGate)
        {
            Guid selectedRuleId = Guid.Empty;
            ActiveAvatarScaleSupporterGrowthState? selectedState = null;
            foreach (var pair in avatarScaleSupporterGrowthStates)
            {
                var state = pair.Value;
                if (state.PaidActiveUntil <= now)
                {
                    continue;
                }

                if (selectedState is null || state.PaidActiveUntil > selectedState.PaidActiveUntil)
                {
                    selectedRuleId = pair.Key;
                    selectedState = state;
                }
            }

            if (selectedState is not null)
            {
                targetHeightMeters = selectedState.CurrentTargetHeightMeters;
                ruleName = activeConfiguration?.AvatarScaleRules.FirstOrDefault(rule => rule.Id == selectedRuleId)?.Name
                    ?? "Supporter Growth";
                return true;
            }
        }

        targetHeightMeters = 0;
        ruleName = string.Empty;
        return false;
    }

    private async Task SendAvatarHeightValueAsync(double heightMeters, CancellationToken cancellationToken)
    {
        var floatValue = (float)heightMeters;
        var packet = vrChatOscClient.BuildPacketForAddress(
            "/avatar/eyeheight",
            OscParameterType.Float,
            floatValue.ToString("G9", CultureInfo.InvariantCulture));
        await oscRouterService.SendToVrChatAsync(packet, cancellationToken);
        ObserveOscValue(
            new OscObservedValue("/avatar/eyeheight", OscParameterType.Float, floatValue),
            updateActiveAvatarScaleCarryover: true);
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
            if (TryGetActiveAvatarScaleCarriedHeightLocked(out var carriedHeight))
            {
                return carriedHeight;
            }

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
            AvatarScaleMode.GlitchyRandomHeight => Random.Shared.NextDouble()
                * (Math.Max(rule.MinimumHeightMeters, rule.MaximumHeightMeters) - Math.Min(rule.MinimumHeightMeters, rule.MaximumHeightMeters))
                + Math.Min(rule.MinimumHeightMeters, rule.MaximumHeightMeters),
            AvatarScaleMode.RelativeHeight => rule.RelativeHeightDirectionId == (int)AvatarScaleRelativeHeightDirection.Subtract
                ? ClampRelativeScaleTarget(rule, currentHeight, current - rule.RelativeHeightMeters)
                : ClampRelativeScaleTarget(rule, currentHeight, current + rule.RelativeHeightMeters),
            AvatarScaleMode.Multiplier => rule.MultiplierDirectionId == (int)AvatarScaleMultiplierDirection.Divide
                ? current / Math.Max(0.01, rule.HeightMultiplier)
                : current * Math.Max(0.01, rule.HeightMultiplier),
            AvatarScaleMode.Preset => AvatarScaleRule.GetPresetHeight(rule.Preset),
            _ => rule.TargetHeightMeters
        };
    }

    private static bool IsAutoBypassingVrChatLimits(AvatarScaleRuleSnapshot rule)
    {
        if (rule.BypassVrChatScaleLimits)
        {
            return true;
        }

        return rule.ScaleMode is AvatarScaleMode.RelativeHeight or AvatarScaleMode.Multiplier
            && rule.RelativeMinimumHeightMeters > 0
            && rule.RelativeMaximumHeightMeters > 0
            && rule.RelativeMinimumHeightMeters < rule.RelativeMaximumHeightMeters;
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

        if (rule.RelativeHeightDirectionId == (int)AvatarScaleRelativeHeightDirection.Subtract
            && currentHeight.Value <= rule.RelativeMinimumHeightMeters)
        {
            limitMessage = $"the current height is already at or below the relative minimum of {rule.RelativeMinimumHeightMeters:0.###}m.";
            return true;
        }

        if (rule.RelativeHeightDirectionId != (int)AvatarScaleRelativeHeightDirection.Subtract
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

        if (rule.RelativeHeightDirectionId == (int)AvatarScaleRelativeHeightDirection.Subtract)
        {
            return Math.Max(targetHeight, rule.RelativeMinimumHeightMeters);
        }

        if (rule.RelativeHeightDirectionId != (int)AvatarScaleRelativeHeightDirection.Subtract)
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
        if (!IsAutoBypassingVrChatLimits(rule))
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
        var wasUnlocked = false;
        DateTimeOffset unlockUntil;
        var cooldownUntil = masterReward.CooldownSeconds > 0
            ? now.AddSeconds(masterReward.CooldownSeconds)
            : DateTimeOffset.MinValue;

        lock (stateGate)
        {
            wasUnlocked = avatarScaleMasterUnlockUntil > now;
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
        // PATCH the master reward's background color to its cooldown color right away so
        // the Twitch reward visibly changes the moment it is redeemed, matching the
        // per-rule flow where NotifyRewardCooldownColorChanged fires immediately on trigger.
        NotifyRewardCooldownColorChanged(AvatarScaleMasterRewardOwnerGuid);
        // Only fire the unlock-state-changed event on a real transition (was locked, now
        // unlocked). Re-redeeming while already unlocked just extends the window and should
        // not trigger another full managed-reward sync.
        if (!wasUnlocked)
        {
            AvatarScaleMasterRewardUnlockStateChanged?.Invoke();
        }
    }

    private static bool SupporterGrowthEventMatches(
        AvatarScaleRuleSnapshot rule,
        UniversalIncomingEvent incomingEvent)
    {
        var bitHeightDirection = 1;
        if (incomingEvent.TriggerType == UniversalTriggerType.Bits
            && !TryResolveSupporterGrowthBitsHeightDirection(
                rule,
                incomingEvent.ChatMessageText,
                out bitHeightDirection,
                out _))
        {
            return false;
        }

        return GetSupporterGrowthHeightAdd(rule, incomingEvent, isTest: false, bitHeightDirection) != 0;
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

    private static TimeSpan GetSupporterOverrideDuration(
        TriggerRuleSnapshot rule,
        BridgeIncomingEvent bridgeEvent)
    {
        var perEventAddSeconds = SupportOverrideDurationMath.ComputePerEventAddSeconds(
            rule,
            bridgeEvent.Amount,
            bridgeEvent.SubscriptionTier);

        return TimeSpan.FromSeconds(
            Math.Min(Math.Max(1, perEventAddSeconds), TimeSpan.MaxValue.TotalSeconds));
    }

    private static bool TryResolveSupporterFloatAddAmount(
        TriggerRuleSnapshot rule,
        BridgeIncomingEvent bridgeEvent,
        out double addValue,
        out string diagnostic)
    {
        addValue = 0;
        diagnostic = string.Empty;
        var amount = Math.Max(1, bridgeEvent.Amount);
        foreach (var range in rule.SupporterFloatAddRanges)
        {
            var minimumAmount = Math.Max(1, range.MinimumAmount);
            var maximumAmount = Math.Max(0, range.MaximumAmount);
            if (amount < minimumAmount || (maximumAmount > 0 && amount > maximumAmount))
            {
                continue;
            }

            if (!FloatValueModeConverter.TryParseNormalized(rule.FloatValueMode, range.AddValue, out var parsedAddValue))
            {
                diagnostic = $"Ignored '{rule.Name}' because its supporter float add value is invalid.";
                return false;
            }

            addValue = Math.Max(0, parsedAddValue);
            return true;
        }

        diagnostic = $"Ignored '{rule.Name}' because {amount:N0} did not match any supporter float add range.";
        return false;
    }

    private static bool TryResolveSupporterFloatAddBounds(
        TriggerRuleSnapshot rule,
        out double lowerBound,
        out double upperBound)
    {
        lowerBound = 0;
        upperBound = 1;
        if (!FloatValueModeConverter.TryParseNormalized(rule.FloatValueMode, rule.SupporterFloatAddMinimumValue, out var minimumValue)
            || !FloatValueModeConverter.TryParseNormalized(rule.FloatValueMode, rule.SupporterFloatAddMaximumValue, out var maximumValue))
        {
            return false;
        }

        lowerBound = Math.Min(minimumValue, maximumValue);
        upperBound = Math.Max(minimumValue, maximumValue);
        return true;
    }

    private static string FormatSupporterFloatValue(TriggerRuleSnapshot rule, double normalizedValue)
    {
        var displayText = FloatValueModeConverter.ToDisplayText(rule.FloatValueMode, normalizedValue);
        return rule.FloatValueMode == FloatValueMode.Percent ? $"{displayText}%" : displayText;
    }

    private static double GetSupporterOverrideAmountScaledDurationSeconds(
        TriggerRuleSnapshot rule,
        BridgeIncomingEvent bridgeEvent)
    {
        var amount = Math.Max(1, bridgeEvent.Amount);
        if (rule.TriggerType == TwitchTriggerType.Subscriptions)
        {
            return amount * GetSupporterOverrideSubscriptionSecondsPerSub(rule, bridgeEvent.SubscriptionTier);
        }

        return (double)amount / Math.Max(1, rule.BitsAmountUnitsPerDuration) * Math.Max(1, rule.BitsSecondsPerAmountUnit);
    }

    private static int GetSupporterOverrideSubscriptionSecondsPerSub(
        TriggerRuleSnapshot rule,
        string subscriptionTier)
    {
        return subscriptionTier?.Trim() switch
        {
            "2000" => Math.Max(1, rule.SubscriptionTier2SecondsPerSub),
            "3000" => Math.Max(1, rule.SubscriptionTier3SecondsPerSub),
            _ => Math.Max(1, rule.SubscriptionTier1SecondsPerSub)
        };
    }

    private static bool IsSubscriptionTierEnabled(TriggerRuleSnapshot rule, string tier)
    {
        return tier?.Trim() switch
        {
            "1000" => rule.SubscriptionTier1Enabled,
            "2000" => rule.SubscriptionTier2Enabled,
            "3000" => rule.SubscriptionTier3Enabled,
            _ => true
        };
    }

    private (bool enabled, int seconds) ResolveOverrideCap(TriggerRuleSnapshot rule)
    {
        var profile = activeConfiguration?.FindAvatarSwapProfileForRule(rule.Rule);
        if (profile is not null)
        {
            var capEnabled = rule.TriggerType switch
            {
                TwitchTriggerType.Bits => profile.BitsMaxSwapTimeEnabled,
                TwitchTriggerType.Subscriptions or TwitchTriggerType.GiftSubscription => profile.SubsMaxSwapTimeEnabled,
                _ => false
            };
            return (capEnabled, profile.MaxSwapTimeSeconds);
        }
        return (rule.MaxAccumulatedDurationEnabled, rule.MaxAccumulatedDurationSeconds);
    }

    private static TimeSpan ClampSupporterOverrideAddedDuration(
        TriggerRuleSnapshot rule,
        TimeSpan requestedDuration,
        TimeSpan existingRemainingDuration,
        bool capEnabled,
        int capSeconds) =>
        SupportOverrideCapMath.ClampAddedDuration(capEnabled, capSeconds, requestedDuration, existingRemainingDuration);

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

    private bool TryQueueAvatarSwitchTriggerDuringSupporterOverride(
        RuntimeRuleIndex ruleIndex,
        BridgeIncomingEvent bridgeEvent,
        string currentAvatarId,
        bool avatarChangeCooldownOnlyModeEnabled)
    {
        if (bridgeEvent.IsChatCommandTrigger || bridgeEvent.TriggerType != TwitchTriggerType.ChannelPoints)
        {
            return false;
        }

        var now = DateTimeOffset.UtcNow;
        HashSet<Guid> supporterBlockedRuleIds;
        lock (stateGate)
        {
            supporterBlockedRuleIds = GetSupporterOverrideBlockedRuleIdsLocked(now);
        }

        if (supporterBlockedRuleIds.Count == 0)
        {
            return false;
        }

        var queuedRule = SelectPaidSuppressedAvatarSwitchMatch(
            ruleIndex,
            bridgeEvent,
            currentAvatarId,
            supporterBlockedRuleIds,
            avatarChangeCooldownOnlyModeEnabled);
        if (queuedRule is null)
        {
            return false;
        }

        var queuedCount = QueueAvatarSwitchTrigger(queuedRule, bridgeEvent);
        WriteLog($"{bridgeEvent.UserDisplayName} queued avatar switch '{queuedRule.Name}' until the paid override finishes. {queuedCount} avatar switch{(queuedCount == 1 ? string.Empty : "es")} waiting.");
        EnsureQueuedAvatarSwitchDrain();
        return true;
    }

    private bool TryQueueAvatarSwitchTriggerDuringActiveAvatarSwitch(
        RuntimeRuleIndex ruleIndex,
        BridgeIncomingEvent bridgeEvent,
        string currentAvatarId,
        bool avatarChangeCooldownOnlyModeEnabled)
    {
        if (bridgeEvent.IsChatCommandTrigger || bridgeEvent.TriggerType != TwitchTriggerType.ChannelPoints)
        {
            return false;
        }

        var now = DateTimeOffset.UtcNow;
        lock (stateGate)
        {
            if (!IsAvatarSwitchSequenceActiveLocked(now))
            {
                return false;
            }
        }

        var queuedRule = SelectQueuedAvatarSwitchMatch(
            ruleIndex,
            bridgeEvent,
            currentAvatarId,
            avatarChangeCooldownOnlyModeEnabled);
        if (queuedRule is null)
        {
            return false;
        }

        var queuedCount = QueueAvatarSwitchTrigger(queuedRule, bridgeEvent);
        WriteLog($"{bridgeEvent.UserDisplayName} queued avatar switch '{queuedRule.Name}' until the current avatar switch finishes. {queuedCount} avatar switch{(queuedCount == 1 ? string.Empty : "es")} waiting.");
        EnsureQueuedAvatarSwitchDrain();
        return true;
    }

    private bool IsAvatarSwitchSequenceActiveLocked(DateTimeOffset now)
    {
        return queuedAvatarSwitches.Count > 0
            || pendingResets.Values.Any(IsPauseableAvatarSwitchReset)
            || (actionLanes.TryGetValue(AvatarSwitchLaneKey, out var activeLane) && activeLane.BusyUntil > now);
    }

    private TriggerRuleSnapshot? SelectQueuedAvatarSwitchMatch(
        RuntimeRuleIndex ruleIndex,
        BridgeIncomingEvent bridgeEvent,
        string currentAvatarId,
        bool avatarChangeCooldownOnlyModeEnabled)
    {
        var normalizedRewardId = bridgeEvent.RewardId?.Trim() ?? string.Empty;
        var normalizedRewardTitle = NormalizeRewardTitle(bridgeEvent.RewardTitle);
        if (string.IsNullOrWhiteSpace(normalizedRewardId) && string.IsNullOrWhiteSpace(normalizedRewardTitle))
        {
            return null;
        }

        var sharedReturnAvatar = GetSharedReturnAvatarSnapshot();
        var activationAvatarId = !string.IsNullOrWhiteSpace(sharedReturnAvatar.AvatarId)
            ? sharedReturnAvatar.AvatarId
            : currentAvatarId?.Trim() ?? string.Empty;
        var candidates = ruleIndex.GetChannelPointCandidates(normalizedRewardId, normalizedRewardTitle)
            .Where(rule => rule.IsEnabled
                && IsQueuedAvatarSwitchRule(rule)
                && AvatarRuleActivationPolicy.IsRuleActiveForCurrentAvatar(
                    rule.IsGlobalOverride,
                    rule.BelongsToMasterAvatarProfile,
                    rule.ActionType,
                    rule.AvatarChangeTargetId,
                    rule.RequiredAvatarId,
                    activationAvatarId,
                    avatarChangeTransitionActive: false,
                    avatarChangeCooldownOnlyModeEnabled))
            .ToArray();
        if (candidates.Length == 0)
        {
            return null;
        }

        var sharedChoiceCandidates = candidates
            .Where(IsSharedRewardChoiceRule)
            .ToArray();
        if (sharedChoiceCandidates.Length > 0)
        {
            if (!TryParseSharedRewardChoiceNumber(bridgeEvent.RewardUserInput, out var choiceNumber))
            {
                return null;
            }

            return sharedChoiceCandidates.FirstOrDefault(rule => rule.SharedRewardChoiceNumber == choiceNumber);
        }

        return candidates[0];
    }

    private TriggerRuleSnapshot? SelectPaidSuppressedAvatarSwitchMatch(
        RuntimeRuleIndex ruleIndex,
        BridgeIncomingEvent bridgeEvent,
        string currentAvatarId,
        IReadOnlySet<Guid> supporterBlockedRuleIds,
        bool avatarChangeCooldownOnlyModeEnabled)
    {
        var normalizedRewardId = bridgeEvent.RewardId?.Trim() ?? string.Empty;
        var normalizedRewardTitle = NormalizeRewardTitle(bridgeEvent.RewardTitle);
        if (string.IsNullOrWhiteSpace(normalizedRewardId) && string.IsNullOrWhiteSpace(normalizedRewardTitle))
        {
            return null;
        }

        var sharedReturnAvatar = GetSharedReturnAvatarSnapshot();
        var activationAvatarId = !string.IsNullOrWhiteSpace(sharedReturnAvatar.AvatarId)
            ? sharedReturnAvatar.AvatarId
            : currentAvatarId?.Trim() ?? string.Empty;
        var candidates = ruleIndex.GetChannelPointCandidates(normalizedRewardId, normalizedRewardTitle)
            .Where(rule => rule.IsEnabled
                && supporterBlockedRuleIds.Contains(rule.Id)
                && IsQueuedAvatarSwitchRule(rule)
                && AvatarRuleActivationPolicy.IsRuleActiveForCurrentAvatar(
                    rule.IsGlobalOverride,
                    rule.BelongsToMasterAvatarProfile,
                    rule.ActionType,
                    rule.AvatarChangeTargetId,
                    rule.RequiredAvatarId,
                    activationAvatarId,
                    avatarChangeTransitionActive: false,
                    avatarChangeCooldownOnlyModeEnabled))
            .ToArray();
        if (candidates.Length == 0)
        {
            return null;
        }

        var sharedChoiceCandidates = candidates
            .Where(IsSharedRewardChoiceRule)
            .ToArray();
        if (sharedChoiceCandidates.Length > 0)
        {
            if (!TryParseSharedRewardChoiceNumber(bridgeEvent.RewardUserInput, out var choiceNumber))
            {
                return null;
            }

            return sharedChoiceCandidates.FirstOrDefault(rule => rule.SharedRewardChoiceNumber == choiceNumber);
        }

        return candidates[0];
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
            DurationSeconds = Math.Max(1d, Math.Ceiling(duration.TotalSeconds)),
            CooldownSeconds = cooldownSeconds
        };

    private bool TryHandleStreamStateNotification(EventSubNotification notification, CancellationToken cancellationToken)
    {
        if (string.Equals(notification.SubscriptionType, "stream.online", StringComparison.Ordinal))
        {
            if (!StreamNotificationMatchesBroadcaster(notification))
            {
                return true;
            }

            CancelPendingStreamOfflineConfirmation();
            SetBroadcasterLiveState(true, "EventSub stream.online", resetTriggerAnnouncementSchedule: true);
            return true;
        }

        if (string.Equals(notification.SubscriptionType, "stream.offline", StringComparison.Ordinal))
        {
            if (!StreamNotificationMatchesBroadcaster(notification))
            {
                return true;
            }

            QueueStreamOfflineConfirmation(cancellationToken);
            return true;
        }

        return false;
    }

    private async Task RefreshBroadcasterLiveStateAsync(CancellationToken cancellationToken)
    {
        if (activeConfiguration is null || broadcaster is null)
        {
            CancelPendingStreamOfflineConfirmation();
            ResetBroadcasterLiveState();
            return;
        }

        try
        {
            var isLive = await QueryBroadcasterLiveStateWithRetryAsync(cancellationToken);
            if (isLive)
            {
                CancelPendingStreamOfflineConfirmation();
            }

            SetBroadcasterLiveState(isLive, "Helix stream status check");
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            throw;
        }
        catch (Exception ex)
        {
            WriteLog($"Twitch stream status check could not complete, so Crystal Relay kept the last known stream state. Source: Helix stream status check. {SensitiveTextSanitizer.Sanitize(ex.Message)}");
        }
    }

    private bool StreamNotificationMatchesBroadcaster(EventSubNotification notification)
    {
        var expectedBroadcasterId = broadcaster?.UserId?.Trim() ?? string.Empty;
        var notificationBroadcasterId = GetString(notification.EventData, "broadcaster_user_id")?.Trim() ?? string.Empty;
        if (string.IsNullOrWhiteSpace(expectedBroadcasterId)
            || string.IsNullOrWhiteSpace(notificationBroadcasterId))
        {
            WriteLog("Ignored Twitch stream status notification because Crystal Relay could not verify the broadcaster ID.");
            return false;
        }

        if (string.Equals(expectedBroadcasterId, notificationBroadcasterId, StringComparison.Ordinal))
        {
            return true;
        }

        WriteLog("Ignored Twitch stream status notification because it did not match the connected broadcaster.");
        return false;
    }

    private void QueueStreamOfflineConfirmation(CancellationToken cancellationToken)
    {
        CancelPendingStreamOfflineConfirmation();

        var confirmationCancellation = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        pendingStreamOfflineConfirmation = confirmationCancellation;
        WriteLog("Twitch reported the broadcaster offline; confirming before updating stream status. Source: EventSub stream.offline.");

        _ = Task.Run(async () =>
        {
            try
            {
                await Task.Delay(StreamOfflineConfirmationDelay, confirmationCancellation.Token);
                var isLive = await QueryBroadcasterLiveStateWithRetryAsync(confirmationCancellation.Token);
                if (isLive)
                {
                    SetBroadcasterLiveState(true, "Helix confirmation after EventSub stream.offline");
                    WriteLog("Twitch offline notification was not confirmed, so Crystal Relay kept the stream status live. Source: Helix confirmation after EventSub stream.offline.");
                    return;
                }

                SetBroadcasterLiveState(false, "EventSub stream.offline confirmed by Helix");
            }
            catch (OperationCanceledException) when (confirmationCancellation.IsCancellationRequested)
            {
            }
            catch (Exception ex)
            {
                WriteLog($"Twitch offline confirmation could not complete, so Crystal Relay kept the last known stream state. Source: EventSub stream.offline confirmation. {SensitiveTextSanitizer.Sanitize(ex.Message)}");
            }
            finally
            {
                if (ReferenceEquals(pendingStreamOfflineConfirmation, confirmationCancellation))
                {
                    pendingStreamOfflineConfirmation = null;
                }

                confirmationCancellation.Dispose();
            }
        }, CancellationToken.None);
    }

    private async Task<bool> QueryBroadcasterLiveStateWithRetryAsync(CancellationToken cancellationToken)
    {
        if (activeConfiguration is null || broadcaster is null)
        {
            return false;
        }

        Exception? lastException = null;
        for (var attempt = 1; attempt <= BroadcasterLiveStateCheckAttempts; attempt++)
        {
            try
            {
                var isLive = await twitchApiClient.IsBroadcasterLiveAsync(
                    broadcaster.AccessToken,
                    activeConfiguration.TwitchClientId,
                    broadcaster.UserId,
                    cancellationToken);
                if (isLive || attempt == BroadcasterLiveStateCheckAttempts)
                {
                    return isLive;
                }
            }
            catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
            {
                throw;
            }
            catch (Exception ex)
            {
                lastException = ex;
                if (attempt == BroadcasterLiveStateCheckAttempts)
                {
                    throw;
                }
            }

            await Task.Delay(BroadcasterLiveStateRetryDelay, cancellationToken);
        }

        if (lastException is not null)
        {
            throw lastException;
        }

        return false;
    }

    private void SetBroadcasterLiveState(
        bool isLive,
        string source,
        bool resetTriggerAnnouncementSchedule = false)
    {
        var stateChanged = !hasResolvedBroadcasterLiveState || isBroadcasterLive != isLive;
        var streamEnded = hasResolvedBroadcasterLiveState && isBroadcasterLive && !isLive;
        isBroadcasterLive = isLive;
        hasResolvedBroadcasterLiveState = true;
        if (resetTriggerAnnouncementSchedule || !isLive)
        {
            nextTriggerInfoAnnouncementAt = DateTimeOffset.MinValue;
        }

        StreamStateChanged?.Invoke(isLive, streamEnded);
        if (stateChanged)
        {
            WriteLog($"Broadcaster is {(isLive ? "live" : "offline")} on Twitch. Source: {source}.");
        }
    }

    private void ResetBroadcasterLiveState()
    {
        isBroadcasterLive = false;
        hasResolvedBroadcasterLiveState = false;
        nextTriggerInfoAnnouncementAt = DateTimeOffset.MinValue;
        StreamStateChanged?.Invoke(false, false);
    }

    private void CancelPendingStreamOfflineConfirmation()
    {
        var pending = pendingStreamOfflineConfirmation;
        pendingStreamOfflineConfirmation = null;
        if (pending is null)
        {
            return;
        }

        try
        {
            pending.Cancel();
        }
        catch (ObjectDisposedException)
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

        await ExecuteRuleActionAsync(rule, bridgeEvent, cancellationToken, isTest: false, queuedReplay: false, allowLaneQueue: true, isResuming: false);
    }

    // Executes one rule action end-to-end. This is where Crystal Relay checks pause state,
    // applies movement lane rules, sends OSC, starts cooldowns, schedules resets, and logs the trigger.
    private async Task ExecuteRuleActionAsync(
        TriggerRuleSnapshot rule,
        BridgeIncomingEvent? bridgeEvent,
        CancellationToken cancellationToken,
        bool isTest,
        bool queuedReplay,
        bool allowLaneQueue,
        bool isResuming = false)
    {
        if (!isTest && AreRedeemsPaused())
        {
            LogRedeemsPaused();
            return;
        }

        if (ShouldBlockAvatarChangeDuringActiveScaling(rule, isTest))
        {
            WriteLog($"Blocked avatar-change reward '{rule.Name}' because Avatar Scaling is active. Paid Bits, Subs, and Power Up avatar changes can still change avatars.");
            return;
        }

        var suppressSharedReturnAvatarUpdate = IsCooldownOnlyDirectAvatarChange(rule);
        if (suppressSharedReturnAvatarUpdate)
        {
            rule = rule with { DurationSeconds = 0 };
        }

        var executionRule = ResolveRandomMovementRule(rule);
        var queuedLaneCount = 0;
        if (allowLaneQueue && TryEnqueueLaneAction(executionRule, bridgeEvent, isTest, out queuedLaneCount))
        {
            if (!isTest && IsBitsOutfitSetTriggerRule(executionRule))
            {
                var viewerName = bridgeEvent?.UserDisplayName ?? "Viewer";
                WriteLog($"Queued Bits outfit Set Trigger '{executionRule.Name}' for {viewerName} until the current outfit restore finishes. Position {queuedLaneCount}.");
            }
            else
            {
                WriteLog(isTest
                    ? $"Queued test trigger for '{executionRule.Name}' until the current action finishes. {queuedLaneCount} waiting."
                    : $"Queued '{executionRule.Name}' until the current action finishes. {queuedLaneCount} waiting.");
            }
            return;
        }

        var isMovementStopAction = executionRule.ActionType == OscActionType.PlayerMovement && IsSoftLockMovement(executionRule.MovementDirection);
        var cooldownSeconds = GetCooldownSeconds(executionRule);
        var capturedReturnAvatar = (executionRule.ActionType is OscActionType.AvatarChange or OscActionType.AvatarRoulet) && executionRule.DurationSeconds > 0
            ? GetSharedReturnAvatarSnapshot()
            : SharedReturnAvatarSnapshot.Empty;
        if (executionRule.ActionType is OscActionType.AvatarChange or OscActionType.AvatarRoulet
            && executionRule.DurationSeconds > 0
            && string.IsNullOrWhiteSpace(capturedReturnAvatar.AvatarId))
        {
            throw new InvalidOperationException("Pick the return avatar first before timed avatar-switch redeems can switch back.");
        }

        if (isMovementStopAction)
        {
            var movementDisplayValue = DescribeMovementAction(executionRule.MovementDirection);
            if (activeConfiguration?.DesktopModeInputLockEnabled == true)
            {
                try
                {
                    await ExecuteDesktopInputLockAsync(executionRule, cancellationToken);
                }
                catch (Exception ex)
                {
                    WriteLog($"Crystal Relay could not start the desktop input lock for '{executionRule.Name}', so it fell back to a VRChat soft lock. {ex.Message}");
                    await ExecuteMovementSoftLockAsync(executionRule, cancellationToken, false);
                }
            }
            else
            {
                await ExecuteMovementSoftLockAsync(executionRule, cancellationToken, false);
            }

            lock (stateGate)
            {
                if (!isTest)
                {
                    cooldowns.Remove(rule.Id);
                }
            }

            if (!isTest && !isResuming)
            {
                CancelCooldownStateNotification(rule.Id);
                UpdateActiveRuleLockoutState(executionRule);
            }

            if (isTest)
            {
                WriteLog(queuedReplay
                    ? $"Sent queued test trigger for '{executionRule.Name}'."
                    : $"Sent a test trigger for '{executionRule.Name}'.");
            }
            else if (bridgeEvent is not null && !isResuming)
            {
                WriteLog(queuedReplay
                    ? $"{bridgeEvent.UserDisplayName} triggered '{executionRule.Name}' from the queue."
                    : $"{bridgeEvent.UserDisplayName} triggered '{executionRule.Name}'.");
            }

            if (!isTest && !isResuming && bridgeEvent is not null)
            {
                await TrySendBotMessageAsync(executionRule, bridgeEvent, movementDisplayValue, cancellationToken);
            }

            return;
        }

        if (executionRule.ActionType == OscActionType.PlayerMovement
            && executionRule.MovementDirection == PlayerMovementDirection.GlitchyMovement)
        {
            await ExecuteGlitchyMovementRuleActionAsync(
                executionRule,
                bridgeEvent,
                cancellationToken,
                isTest,
                queuedReplay,
                cooldownSeconds,
                false);
            return;
        }

        if (IsTimedFloatAvatarParameterRule(executionRule))
        {
            await ExecuteTimedFloatAvatarParameterRuleActionAsync(
                executionRule,
                bridgeEvent,
                cancellationToken,
                isTest,
                queuedReplay,
                laneKeys: null,
                laneLeaseId: Guid.Empty,
                cooldownSeconds);
            return;
        }

        var action = await ResolveActionAsync(
            executionRule,
            cancellationToken,
            preferLocalInstantToggleState: executionRule.ParameterType == OscParameterType.Bool && executionRule.DurationSeconds <= 0,
            capturedReturnAvatar);
        var laneKeys = GetActionLaneKeys(executionRule, action);
        var laneLeaseId = laneKeys.Count == 0 ? Guid.Empty : Guid.NewGuid();
        var effectiveTimedActionSeconds = Math.Max(1d, executionRule.DurationSeconds);
        if (executionRule.ActionType == OscActionType.SetTrigger && action.SetTriggerRestorePlan is not null)
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
            executionRule.ActionType == OscActionType.SetTrigger ? SetTriggerPacketSpacing : null);
        if (executionRule.ActionType == OscActionType.SetTrigger)
        {
            WriteLog($"Sent Set Trigger '{rule.Name}' outfit values ({action.Packets.Count} param{(action.Packets.Count == 1 ? string.Empty : "s")}).");
        }

        RememberAvatarParameterValues(executionRule, action.ObservedValues.Count > 0 ? action.ObservedValues : null, action.DisplayValue);
        if (!isTest)
        {
            UpdateActiveRuleLockoutState(executionRule);
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

            if (!isTest && !isResuming)
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

        if (!isTest && !isResuming)
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

        if (executionRule.ActionType is OscActionType.AvatarChange or OscActionType.AvatarRoulet
            && !string.IsNullOrWhiteSpace(action.AvatarTargetId))
        {
            LogPaidAvatarChangeAllowedDuringActiveScaling(rule);
            SetCurrentVrChatAvatar(
                action.AvatarTargetId,
                notify: true,
            GetAvatarScaleAvatarChangeCarryoverMode(rule));
            if (executionRule.ActionType == OscActionType.AvatarChange
                && executionRule.DurationSeconds <= 0
                && !suppressSharedReturnAvatarUpdate)
            {
                SetSharedReturnAvatar(action.AvatarTargetId, action.AvatarTargetName, notify: true);
            }
        }

        if (!isTest && !isResuming && executionRule.ActionType is OscActionType.AvatarChange or OscActionType.AvatarRoulet)
        {
            var expiresAt = executionRule.DurationSeconds > 0 ? DateTimeOffset.UtcNow.AddSeconds(executionRule.DurationSeconds) : (DateTimeOffset?)null;
            await activityResumeService.RecordActivityStartedAsync(new ResumeActivity
            {
                Type = ResumeActivityType.AvatarChange,
                RuleId = rule.Id,
                ExpiresAt = expiresAt,
                Payload = new Dictionary<string, object>
                {
                    ["avatarTargetId"] = action.AvatarTargetId ?? string.Empty
                }
            }, action.AvatarTargetId ?? string.Empty);
        }

        if (isTest)
        {
            WriteLog(queuedReplay
                ? $"Sent queued test trigger for '{rule.Name}'."
                : $"Sent a test trigger for '{rule.Name}'.");
        }
        else if (bridgeEvent is not null && !isResuming)
        {
            WriteLog(queuedReplay
                ? $"{bridgeEvent.UserDisplayName} triggered '{rule.Name}' from the queue."
                : $"{bridgeEvent.UserDisplayName} triggered '{rule.Name}'.");
        }

        var lockoutDurationSeconds = isTest ? 0 : GetLockoutDurationSeconds(executionRule);
        if (!isTest && !isResuming)
        {
            UpdateActiveAvatarSwitchLockoutState(executionRule);
        }

        var shouldNotifyManagedRewardState = !isTest && !isResuming && cooldownSeconds > 0;
        if ((action.HasResetPackets && effectiveTimedActionSeconds > 0)
            || lockoutDurationSeconds > 0)
        {
            var resetDelaySeconds = action.HasResetPackets
                ? effectiveTimedActionSeconds
                : lockoutDurationSeconds;
            if (executionRule.ActionType == OscActionType.PlayerMovement
                && executionRule.MovementDirection == PlayerMovementDirection.Jump)
            {
                ScheduleJumpPulseReset(executionRule, action, resetDelaySeconds, laneKeys.FirstOrDefault(), laneLeaseId, notifyManagedRewardState: false, isTest: isTest);
            }
            else
            {
                ScheduleReset(executionRule, action, resetDelaySeconds, laneKeys, laneLeaseId, notifyManagedRewardState: false, isTest: isTest);
            }
        }

        if (!isTest && !isResuming && executionRule.ActionType == OscActionType.PlayerMovement)
        {
            var expiresAt = executionRule.DurationSeconds > 0 ? DateTimeOffset.UtcNow.AddSeconds(executionRule.DurationSeconds) : (DateTimeOffset?)null;
            await activityResumeService.RecordActivityStartedAsync(new ResumeActivity
            {
                Type = ResumeActivityType.Movement,
                RuleId = rule.Id,
                ExpiresAt = expiresAt,
                Payload = new Dictionary<string, object>
                {
                    ["movementDirection"] = executionRule.MovementDirection.ToString()
                }
            }, GetCurrentVrChatAvatarId());
        }

        if (shouldNotifyManagedRewardState)
        {
            ManagedRewardAvailabilityChanged?.Invoke();
            NotifyRewardCooldownColorChanged(executionRule.Id);
        }

        if (!isTest && !isResuming && bridgeEvent is not null)
        {
            await TrySendBotMessageAsync(executionRule, bridgeEvent, action.DisplayValue, cancellationToken);
        }
    }

    private async Task ExecuteGlitchyMovementRuleActionAsync(
        TriggerRuleSnapshot rule,
        BridgeIncomingEvent? bridgeEvent,
        CancellationToken cancellationToken,
        bool isTest,
        bool queuedReplay,
        int cooldownSeconds,
        bool isResuming = false)
    {
        var laneKeys = GetGlitchyMovementLaneKeys();
        var laneLeaseId = laneKeys.Count == 0 ? Guid.Empty : Guid.NewGuid();
        var activeSeconds = Math.Max(1, rule.DurationSeconds);
        var activeUntil = DateTimeOffset.UtcNow.AddSeconds(activeSeconds);
        var sequenceCancellation = runtimeCancellation is null
            ? new CancellationTokenSource()
            : CancellationTokenSource.CreateLinkedTokenSource(runtimeCancellation.Token);

        lock (stateGate)
        {
            foreach (var laneKey in laneKeys)
            {
                actionLanes[laneKey] = new ActiveMovementLaneState(
                    laneLeaseId,
                    activeUntil,
                    rule.Id,
                    false);
            }

            if (!isTest && !isResuming)
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
            UpdateActiveRuleLockoutState(rule);
            if (cooldownSeconds > 0)
            {
                ScheduleCooldownStateNotification(rule.Id, TimeSpan.FromSeconds(cooldownSeconds));
                ManagedRewardAvailabilityChanged?.Invoke();
                NotifyRewardCooldownColorChanged(rule.Id);
            }
            else
            {
                CancelCooldownStateNotification(rule.Id);
            }
        }

        _ = Task.Run(
            () => RunGlitchyMovementSequenceAsync(
                rule,
                TimeSpan.FromSeconds(activeSeconds),
                laneKeys,
                laneLeaseId,
                sequenceCancellation),
            CancellationToken.None);

        if (isTest)
        {
            WriteLog(queuedReplay
                ? $"Sent queued glitchy movement test for '{rule.Name}' for {DescribeDuration(activeSeconds)}."
                : $"Sent a glitchy movement test for '{rule.Name}' for {DescribeDuration(activeSeconds)}.");
        }
        else if (bridgeEvent is not null)
        {
            WriteLog(queuedReplay
                ? $"{bridgeEvent.UserDisplayName} triggered glitchy movement '{rule.Name}' from the queue for {DescribeDuration(activeSeconds)}."
                : $"{bridgeEvent.UserDisplayName} triggered glitchy movement '{rule.Name}' for {DescribeDuration(activeSeconds)}.");
        }

        if (!isTest && bridgeEvent is not null)
        {
            await TrySendBotMessageAsync(rule, bridgeEvent, DescribeMovementAction(rule.MovementDirection), cancellationToken);
        }
    }

    private async Task RunGlitchyMovementSequenceAsync(
        TriggerRuleSnapshot rule,
        TimeSpan duration,
        IReadOnlyList<string> laneKeys,
        Guid laneLeaseId,
        CancellationTokenSource sequenceCancellation)
    {
        var cancellationToken = sequenceCancellation.Token;
        var endAt = DateTimeOffset.UtcNow.Add(duration);
        PlayerMovementDirection? previousDirection = null;

        try
        {
            while (!cancellationToken.IsCancellationRequested)
            {
                var remaining = endAt - DateTimeOffset.UtcNow;
                if (remaining <= TimeSpan.Zero)
                {
                    break;
                }

                var movementDirection = PickRandomMovementDirection(previousDirection);
                previousDirection = movementDirection;
                var movementRule = rule with { MovementDirection = movementDirection };
                var action = ResolvePlayerMovementAction(movementRule);
                var movementLaneKey = GetMovementLaneKey(movementDirection);
                var sliceSeconds = Math.Min(
                    Random.Shared.Next(
                        (int)DevRandomMovementMinimumSliceDuration.TotalSeconds,
                        (int)DevRandomMovementMaximumSliceDuration.TotalSeconds + 1),
                    remaining.TotalSeconds);
                if (sliceSeconds <= 0)
                {
                    break;
                }

                var sliceDuration = TimeSpan.FromSeconds(sliceSeconds);

                try
                {
                    await SendPacketsToVrChatAsync(action.Packets, cancellationToken);
                    if (movementDirection == PlayerMovementDirection.Jump)
                    {
                        await RunDevJumpMovementOverlayAsync(
                            action,
                            sliceDuration,
                            movementLaneKey,
                            laneLeaseId,
                            cancellationToken);
                    }
                    else
                    {
                        await Task.Delay(sliceDuration, cancellationToken);
                    }
                }
                finally
                {
                    try
                    {
                        if (laneKeys.Count == 0 || laneKeys.Any(laneKey => IsMovementLaneLeaseActive(laneKey, laneLeaseId)))
                        {
                            await SendPacketsToVrChatAsync(action.ResetPackets, CancellationToken.None);
                        }
                    }
                    catch (Exception ex)
                    {
                        WriteLog($"Failed to release glitchy movement slice for '{rule.Name}': {ex.Message}");
                    }
                }
            }
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
        }
        catch (Exception ex)
        {
            WriteLog($"Glitchy movement '{rule.Name}' failed: {ex.Message}");
        }
        finally
        {
            var releasedLaneKeys = ReleaseMovementLanes(laneLeaseId, laneKeys);
            foreach (var releasedLaneKey in releasedLaneKeys)
            {
                EnsureQueuedLaneDrain(releasedLaneKey);
            }

            sequenceCancellation.Dispose();
        }
    }

    private static bool IsTimedFloatAvatarParameterRule(TriggerRuleSnapshot rule) =>
        rule.ActionType == OscActionType.AvatarParameter
        && rule.ParameterType == OscParameterType.Float
        && rule.DurationSeconds > 0
        && (rule.FloatTransitionInSeconds > 0
            || rule.FloatTransitionOutSeconds > 0
            || rule.ActiveFloatBoostRewardEnabled);

    private static bool IsActiveFloatBoostRule(TriggerRuleSnapshot rule) =>
        rule.TriggerType == TwitchTriggerType.ChannelPoints
        && rule.ActionType == OscActionType.AvatarParameter
        && rule.ParameterType == OscParameterType.Float
        && rule.DurationSeconds > 0
        && rule.ActiveFloatBoostRewardEnabled
        && (!string.IsNullOrWhiteSpace(rule.ActiveFloatBoostRewardId)
            || !string.IsNullOrWhiteSpace(rule.ActiveFloatBoostRewardTitle));

    private static bool TryResolveActiveFloatBoostMaximum(
        TriggerRuleSnapshot rule,
        out double maximumValue)
    {
        maximumValue = 1d;
        if (!FloatValueModeConverter.TryParseNormalized(rule.FloatValueMode, rule.ActiveFloatBoostMinimumValue, out var minimumValue)
            || !FloatValueModeConverter.TryParseNormalized(rule.FloatValueMode, rule.ActiveFloatBoostMaximumValue, out var configuredMaximumValue))
        {
            return false;
        }

        maximumValue = Math.Max(minimumValue, configuredMaximumValue);
        return true;
    }

    private static bool IsAtOrAboveActiveFloatBoostMaximum(double currentValue, double maximumValue) =>
        currentValue >= maximumValue - 0.000001d;

    private async Task ExecuteTimedFloatAvatarParameterRuleActionAsync(
        TriggerRuleSnapshot rule,
        BridgeIncomingEvent? bridgeEvent,
        CancellationToken cancellationToken,
        bool isTest,
        bool queuedReplay,
        IReadOnlyList<string>? laneKeys,
        Guid laneLeaseId,
        int cooldownSeconds)
    {
        if (!FloatValueModeConverter.TryParseNormalized(rule.FloatValueMode, rule.ParameterValue, out var targetValue))
        {
            throw new InvalidOperationException("Enter a valid float trigger value before using this rule.");
        }

        if (!FloatValueModeConverter.TryParseNormalized(rule.FloatValueMode, rule.ResetValue, out var resetValue))
        {
            throw new InvalidOperationException("Enter a valid float reset value before using this timed rule.");
        }

        var address = VrChatOscClient.NormalizeAvatarParameterAddress(rule.ParameterName);
        laneKeys ??= GetActionLaneKeys(rule);
        laneLeaseId = laneKeys.Count == 0 ? Guid.Empty : Guid.NewGuid();
        var inSeconds = Math.Clamp(rule.FloatTransitionInSeconds, 0, 30);
        var outSeconds = Math.Clamp(rule.FloatTransitionOutSeconds, 0, 30);
        var activeSeconds = Math.Max(1, rule.DurationSeconds);
        var totalActiveSeconds = inSeconds + activeSeconds + outSeconds;
        var activeUntil = DateTimeOffset.UtcNow.AddSeconds(totalActiveSeconds);
        var completionCancellation = runtimeCancellation is null
            ? new CancellationTokenSource()
            : CancellationTokenSource.CreateLinkedTokenSource(runtimeCancellation.Token);
        var boostMaximumReached = IsActiveFloatBoostRule(rule)
            && TryResolveActiveFloatBoostMaximum(rule, out var maximumBoostValue)
            && IsAtOrAboveActiveFloatBoostMaximum(targetValue, maximumBoostValue);
        var session = new ActiveFloatRedeemSessionState(
            rule,
            address,
            targetValue,
            resetValue,
            activeUntil,
            completionCancellation,
            laneKeys,
            laneLeaseId,
            isTest,
            boostMaximumReached);

        ActiveFloatRedeemSessionState? previousSession = null;
        lock (stateGate)
        {
            if (activeFloatRedeemSessions.TryGetValue(rule.Id, out previousSession))
            {
                activeFloatRedeemSessions.Remove(rule.Id);
            }

            activeFloatRedeemSessions[rule.Id] = session;
            foreach (var laneKey in laneKeys)
            {
                actionLanes[laneKey] = new ActiveMovementLaneState(
                    laneLeaseId,
                    activeUntil,
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

        previousSession?.CompletionCancellation.Cancel();
        previousSession?.CompletionCancellation.Dispose();

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

            ApplyRuleLockoutUntil(rule, activeUntil);
            ManagedRewardAvailabilityChanged?.Invoke();
        }

        try
        {
            var startValue = await TryGetCurrentAvatarFloatValueAsync(address, targetValue, cancellationToken);
            await session.SendGate.WaitAsync(cancellationToken);
            try
            {
                await SendFloatAvatarParameterValueAsync(
                    address,
                    startValue,
                    targetValue,
                    inSeconds,
                    cancellationToken);
                session.CurrentValue = targetValue;
            }
            finally
            {
                session.SendGate.Release();
            }
        }
        catch
        {
            FinishActiveFloatRedeemSession(session, completionCancellation, notifyManagedRewardState: !isTest);
            throw;
        }

        RememberAvatarParameterValue(rule, FloatValueModeConverter.ToOscText(targetValue));

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

        ScheduleActiveFloatRedeemCompletion(session, completionCancellation, activeSeconds, outSeconds);

        if (!isTest && bridgeEvent is not null)
        {
            await TrySendBotMessageAsync(rule, bridgeEvent, FloatValueModeConverter.ToOscText(targetValue), cancellationToken);
        }
    }

    private void ScheduleActiveFloatRedeemCompletion(
        ActiveFloatRedeemSessionState session,
        CancellationTokenSource completionCancellation,
        double activeSeconds,
        double transitionSeconds)
    {
        _ = Task.Run(async () =>
        {
            var shouldNotifyManagedRewardState = !session.IsTest;
            try
            {
                await Task.Delay(TimeSpan.FromSeconds(Math.Max(1, activeSeconds)), completionCancellation.Token);

                // If the avatar recently changed, defer the reset until the grace period ends.
                // This prevents reset packets from being lost during world transitions.
                if (IsInAvatarChangeGracePeriod())
                {
                    var graceRemaining = AvatarChangeGracePeriod - (DateTimeOffset.UtcNow - lastAvatarChangeAt);
                    WriteLog($"Float redeem '{session.Rule.Name}' completion is deferred because the avatar recently changed. The reset will be retried after the grace period. ({DescribeDuration(graceRemaining.TotalSeconds)} remaining)");
                    ScheduleActiveFloatRedeemCompletionAfterGracePeriod(session, completionCancellation, activeSeconds, transitionSeconds);
                    return;
                }

                await session.SendGate.WaitAsync(completionCancellation.Token);
                try
                {
                    await SendFloatAvatarParameterValueAsync(
                        session.Address,
                        session.CurrentValue,
                        session.ResetValue,
                        transitionSeconds,
                        completionCancellation.Token);
                    session.CurrentValue = session.ResetValue;
                }
                finally
                {
                    session.SendGate.Release();
                }

                RememberAvatarParameterValue(session.Rule, FloatValueModeConverter.ToOscText(session.ResetValue));
                WriteLog($"Reset '{session.Rule.Name}' after {DescribeDuration(activeSeconds + transitionSeconds + transitionSeconds)}.");
            }
            catch (OperationCanceledException)
            {
                shouldNotifyManagedRewardState = false;
            }
            catch (Exception ex)
            {
                WriteLog($"Failed to reset '{session.Rule.Name}': {ex.Message}");
            }
            finally
            {
                FinishActiveFloatRedeemSession(session, completionCancellation, shouldNotifyManagedRewardState);
            }
        }, CancellationToken.None);
    }

    private void ScheduleActiveFloatRedeemCompletionAfterGracePeriod(
        ActiveFloatRedeemSessionState session,
        CancellationTokenSource completionCancellation,
        double activeSeconds,
        double transitionSeconds)
    {
        _ = Task.Run(async () =>
        {
            var shouldNotifyManagedRewardState = !session.IsTest;
            try
            {
                var graceRemaining = AvatarChangeGracePeriod - (DateTimeOffset.UtcNow - lastAvatarChangeAt);
                if (graceRemaining > TimeSpan.Zero)
                {
                    await Task.Delay(graceRemaining, completionCancellation.Token);
                }

                // If we're still in the grace period (avatar changed again), re-defer
                if (IsInAvatarChangeGracePeriod())
                {
                    ScheduleActiveFloatRedeemCompletionAfterGracePeriod(session, completionCancellation, activeSeconds, transitionSeconds);
                    return;
                }

                await session.SendGate.WaitAsync(completionCancellation.Token);
                try
                {
                    await SendFloatAvatarParameterValueAsync(
                        session.Address,
                        session.CurrentValue,
                        session.ResetValue,
                        transitionSeconds,
                        completionCancellation.Token);
                    session.CurrentValue = session.ResetValue;
                }
                finally
                {
                    session.SendGate.Release();
                }

                RememberAvatarParameterValue(session.Rule, FloatValueModeConverter.ToOscText(session.ResetValue));
                WriteLog($"Reset '{session.Rule.Name}' after {DescribeDuration(activeSeconds + transitionSeconds + transitionSeconds)} (deferred completion after avatar change grace period).");
            }
            catch (OperationCanceledException)
            {
                shouldNotifyManagedRewardState = false;
            }
            catch (Exception ex)
            {
                WriteLog($"Failed to reset '{session.Rule.Name}' during grace period deferral: {ex.Message}");
            }
            finally
            {
                FinishActiveFloatRedeemSession(session, completionCancellation, shouldNotifyManagedRewardState);
            }
        }, CancellationToken.None);
    }

    private async Task ApplyActiveFloatBoostRewardAsync(
        TriggerRuleSnapshot rule,
        BridgeIncomingEvent bridgeEvent,
        CancellationToken cancellationToken)
    {
        ActiveFloatRedeemSessionState? session;
        lock (stateGate)
        {
            activeFloatRedeemSessions.TryGetValue(rule.Id, out session);
        }

        if (session is null)
        {
            WriteLog($"Ignored active boost reward '{bridgeEvent.RewardTitle}' because '{rule.Name}' is not active.");
            return;
        }

        if (!FloatValueModeConverter.TryParseNormalized(rule.FloatValueMode, rule.ActiveFloatBoostAddValue, out var addValue)
            || !FloatValueModeConverter.TryParseNormalized(rule.FloatValueMode, rule.ActiveFloatBoostMinimumValue, out var minimumValue)
            || !FloatValueModeConverter.TryParseNormalized(rule.FloatValueMode, rule.ActiveFloatBoostMaximumValue, out var maximumValue))
        {
            WriteLog($"Ignored active boost reward for '{rule.Name}' because its boost amount or min/max value is invalid.");
            return;
        }

        var lowerBound = Math.Min(minimumValue, maximumValue);
        var upperBound = Math.Max(minimumValue, maximumValue);
        var transitionSeconds = Math.Clamp(rule.FloatTransitionOutSeconds, 0, 30);
        var activeSeconds = Math.Max(1, rule.DurationSeconds);
        var oldCompletionCancellation = session.CompletionCancellation;
        var newCompletionCancellation = runtimeCancellation is null
            ? new CancellationTokenSource()
            : CancellationTokenSource.CreateLinkedTokenSource(runtimeCancellation.Token);
        var newActiveUntil = DateTimeOffset.UtcNow.AddSeconds(transitionSeconds + activeSeconds + transitionSeconds);
        double boostedValue;
        bool boostMaximumReached;

        lock (stateGate)
        {
            if (!activeFloatRedeemSessions.TryGetValue(rule.Id, out var currentSession)
                || !ReferenceEquals(currentSession, session))
            {
                newCompletionCancellation.Dispose();
                return;
            }

            boostedValue = Math.Clamp(session.CurrentValue + addValue, lowerBound, upperBound);
            boostMaximumReached = IsAtOrAboveActiveFloatBoostMaximum(boostedValue, upperBound);
            session.ActiveUntil = newActiveUntil;
            session.CompletionCancellation = newCompletionCancellation;
            session.BoostMaximumReached = boostMaximumReached;
            foreach (var laneKey in session.MovementLaneKeys)
            {
                actionLanes[laneKey] = new ActiveMovementLaneState(
                    session.MovementLaneLeaseId,
                    newActiveUntil,
                    rule.Id,
                    false);
            }
        }

        oldCompletionCancellation.Cancel();
        oldCompletionCancellation.Dispose();

        ApplyRuleLockoutUntil(rule, newActiveUntil);
        ManagedRewardAvailabilityChanged?.Invoke();

        await session.SendGate.WaitAsync(cancellationToken);
        try
        {
            await SendFloatAvatarParameterValueAsync(
                session.Address,
                session.CurrentValue,
                boostedValue,
                transitionSeconds,
                cancellationToken);
            session.CurrentValue = boostedValue;
        }
        finally
        {
            session.SendGate.Release();
        }

        RememberAvatarParameterValue(rule, FloatValueModeConverter.ToOscText(boostedValue));
        ScheduleActiveFloatRedeemCompletion(session, newCompletionCancellation, activeSeconds, transitionSeconds);
        WriteLog($"{bridgeEvent.UserDisplayName} boosted '{rule.Name}' to {FloatValueModeConverter.ToOscText(boostedValue)} and refreshed its active timer.");
    }

    private void FinishActiveFloatRedeemSession(
        ActiveFloatRedeemSessionState session,
        CancellationTokenSource completionCancellation,
        bool notifyManagedRewardState)
    {
        var releasedSession = false;
        lock (stateGate)
        {
            if (activeFloatRedeemSessions.TryGetValue(session.Rule.Id, out var currentSession)
                && ReferenceEquals(currentSession, session)
                && ReferenceEquals(session.CompletionCancellation, completionCancellation))
            {
                activeFloatRedeemSessions.Remove(session.Rule.Id);
                releasedSession = true;
            }
        }

        if (!releasedSession)
        {
            return;
        }

        completionCancellation.Dispose();
        session.SendGate.Dispose();
        var releasedLaneKeys = ReleaseMovementLanes(session.MovementLaneLeaseId, session.MovementLaneKeys);
        ReleaseActiveRuleLockoutState(session.Rule.Id, logRelease: true);
        if (notifyManagedRewardState)
        {
            ManagedRewardAvailabilityChanged?.Invoke();
        }

        foreach (var releasedLaneKey in releasedLaneKeys)
        {
            EnsureQueuedLaneDrain(releasedLaneKey);
        }
    }

    private async Task<double> TryGetCurrentAvatarFloatValueAsync(
        string address,
        double fallbackValue,
        CancellationToken cancellationToken)
    {
        lock (stateGate)
        {
            if (TryGetObservedFloatLocked(address, out var observedValue))
            {
                return observedValue;
            }
        }

        try
        {
            var observedValue = await oscRouterService.GetCurrentAvatarParameterValueAsync(address, cancellationToken);
            if (observedValue?.ParameterType == OscParameterType.Float && observedValue.Value is float floatValue)
            {
                ObserveOscValue(new OscObservedValue(address, OscParameterType.Float, floatValue));
                return floatValue;
            }
        }
        catch (Exception ex)
        {
            WriteLog($"Crystal Relay could not read the live float state for {address} through OSCQuery. {ex.Message}");
        }

        return fallbackValue;
    }

    private async Task SendSupporterFloatAddValueAsync(
        TriggerRuleSnapshot rule,
        double targetValue,
        CancellationToken cancellationToken)
    {
        var address = VrChatOscClient.NormalizeAvatarParameterAddress(rule.ParameterName);
        await SendSingleFloatAvatarParameterValueAsync(address, targetValue, cancellationToken);
    }

    private async Task SendFloatAvatarParameterValueAsync(
        string address,
        double startValue,
        double targetValue,
        double transitionSeconds,
        CancellationToken cancellationToken)
    {
        if (transitionSeconds <= 0)
        {
            await SendSingleFloatAvatarParameterValueAsync(address, targetValue, cancellationToken);
            return;
        }

        var duration = TimeSpan.FromSeconds(Math.Max(0.001, transitionSeconds));
        var steps = Math.Clamp(
            (int)Math.Ceiling(duration.TotalSeconds * AvatarScaleSmoothUpdatesPerSecond),
            1,
            AvatarScaleSmoothMaxSteps);
        var stopwatch = Stopwatch.StartNew();

        for (var step = 1; step <= steps; step++)
        {
            var scheduledElapsed = TimeSpan.FromSeconds(duration.TotalSeconds * step / steps);
            var waitTime = scheduledElapsed - stopwatch.Elapsed;
            if (waitTime > TimeSpan.Zero)
            {
                await Task.Delay(waitTime, cancellationToken);
            }

            var linearProgress = step == steps
                ? 1
                : Math.Clamp(stopwatch.Elapsed.TotalSeconds / duration.TotalSeconds, 0, 1);
            var easedProgress = SmoothStep(linearProgress);
            var value = startValue + ((targetValue - startValue) * easedProgress);
            await SendSingleFloatAvatarParameterValueAsync(address, value, cancellationToken);
        }
    }

    private async Task ExecuteFloatAvatarParameterWithTransitionAsync(
        TriggerRuleSnapshot rule,
        CancellationToken cancellationToken)
    {
        var address = VrChatOscClient.NormalizeAvatarParameterAddress(rule.ParameterName);
        var sourceRule = rule.Rule;

        // Resolve the target value: Set mode uses ParameterValue, all other
        // action modes compute the next value from the current OSC reading.
        double targetValue;
        if (sourceRule.FloatActionMode == FloatActionMode.Set)
        {
            if (!FloatValueModeConverter.TryParseNormalized(rule.FloatValueMode, rule.ParameterValue, out targetValue))
            {
                return;
            }
        }
        else
        {
            var currentForCompute = await TryGetCurrentAvatarFloatValueAsync(address, 0.0, cancellationToken);
            targetValue = FloatActionDispatch.ComputeNext(sourceRule, currentForCompute).nextValue;
        }

        var inSeconds = Math.Clamp(rule.FloatTransitionInSeconds, 0, 30);
        var currentValue = await TryGetCurrentAvatarFloatValueAsync(address, targetValue, cancellationToken);

        if (inSeconds <= 0 || Math.Abs(currentValue - targetValue) < 0.000001d)
        {
            await SendSingleFloatAvatarParameterValueAsync(address, targetValue, cancellationToken);
            return;
        }

        await SendFloatAvatarParameterValueAsync(address, currentValue, targetValue, inSeconds, cancellationToken);
    }

    private async Task SendSingleFloatAvatarParameterValueAsync(
        string address,
        double value,
        CancellationToken cancellationToken)
    {
        var clampedValue = Math.Clamp(value, 0d, 1d);
        await oscRouterService.SendToVrChatAsync(
            vrChatOscClient.BuildAvatarParameterPacket(address, OscParameterType.Float, FloatValueModeConverter.ToOscText(clampedValue)),
            cancellationToken);
        ObserveOscValue(new OscObservedValue(address, OscParameterType.Float, (float)clampedValue));
    }

    private async Task<ResolvedRuleAction> ResolveActionAsync(
        TriggerRuleSnapshot rule,
        CancellationToken cancellationToken,
        bool preferLocalInstantToggleState,
        SharedReturnAvatarSnapshot capturedReturnAvatar)
    {
        if (rule.ActionType is OscActionType.AvatarChange or OscActionType.AvatarRoulet)
        {
            if (rule.ActionType == OscActionType.AvatarRoulet)
            {
                var roulette = activeConfiguration?.FindRouletteProfileForRule(rule.Rule);
                if (roulette != null)
                {
                    return ResolveRouletteProfileAction(roulette, rule);
                }
            }
            var parentProfile = activeConfiguration?.FindAvatarSwapProfileForRule(rule.Rule);
            if (parentProfile != null)
            {
                return ResolveAvatarSwapAction(parentProfile, rule, capturedReturnAvatar);
            }
        }

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

    private ResolvedRuleAction ResolveAvatarSwapAction(
        AvatarSwapProfileSnapshot profile,
        TriggerRuleSnapshot rule,
        SharedReturnAvatarSnapshot capturedReturnAvatar)
    {
        _ = rule;
        if (!profile.IsEnabled || string.IsNullOrWhiteSpace(profile.TargetAvatarId))
        {
            return new ResolvedRuleAction(
                vrChatOscClient.BuildAvatarChangePacket(string.Empty),
                null,
                profile.TargetAvatarName,
                profile.TargetAvatarId,
                profile.TargetAvatarName,
                capturedReturnAvatar.AvatarId,
                capturedReturnAvatar.AvatarName);
        }

        var returnAvatarId = capturedReturnAvatar.AvatarId;
        var returnAvatarName = !string.IsNullOrWhiteSpace(capturedReturnAvatar.AvatarName)
            ? capturedReturnAvatar.AvatarName
            : (activeConfiguration?.MasterAvatarSwapReturnName ?? profile.TargetAvatarName);
        var hasReturn = !string.IsNullOrWhiteSpace(returnAvatarId);

        return new ResolvedRuleAction(
            vrChatOscClient.BuildAvatarChangePacket(profile.TargetAvatarId),
            hasReturn
                ? vrChatOscClient.BuildAvatarChangePacket(returnAvatarId)
                : null,
            profile.TargetAvatarName,
            profile.TargetAvatarId,
            profile.TargetAvatarName,
            returnAvatarId,
            returnAvatarName);
    }

    private ResolvedRuleAction ResolveRouletteProfileAction(
        AvatarRouletteProfileSnapshot roulette,
        TriggerRuleSnapshot rule)
    {
        _ = rule;
        if (roulette.Pool is null || roulette.Pool.Count == 0)
        {
            throw new InvalidOperationException(
                $"Avatar Roulette profile '{roulette.Name}' has no avatars in the pool.");
        }

        var picked = PickAvatarRouletTarget(roulette);
        if (picked is null)
        {
            throw new InvalidOperationException(
                $"Avatar Roulette profile '{roulette.Name}' has no avatars in the pool.");
        }

        var returnAvatarId = !string.IsNullOrWhiteSpace(roulette.ReturnAvatarId)
            ? roulette.ReturnAvatarId
            : (activeConfiguration?.MasterAvatarSwapReturnId ?? picked.AvatarId);
        var returnAvatarName = !string.IsNullOrWhiteSpace(roulette.ReturnAvatarName)
            ? roulette.ReturnAvatarName
            : (activeConfiguration?.MasterAvatarSwapReturnName ?? picked.AvatarName);
        var hasReturn = !string.IsNullOrWhiteSpace(returnAvatarId)
            && !string.Equals(returnAvatarId, picked.AvatarId, StringComparison.Ordinal);

        return new ResolvedRuleAction(
            vrChatOscClient.BuildAvatarChangePacket(picked.AvatarId),
            hasReturn
                ? vrChatOscClient.BuildAvatarChangePacket(returnAvatarId)
                : null,
            string.IsNullOrWhiteSpace(picked.AvatarName) ? "selected avatar" : picked.AvatarName,
            picked.AvatarId,
            picked.AvatarName,
            hasReturn ? returnAvatarId : string.Empty,
            hasReturn ? returnAvatarName : string.Empty);
    }

    private ResolvedRuleAction ResolveAvatarRouletAction(
        TriggerRuleSnapshot rule,
        SharedReturnAvatarSnapshot capturedReturnAvatar)
    {
        var roulette = activeConfiguration?.FindRouletteProfileForRule(rule.Rule);
        if (roulette != null)
        {
            return ResolveRouletteProfileAction(roulette, rule);
        }

        var selectedAvatar = PickAvatarRouletTargetFromRule(rule);
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

    private TriggerRuleSnapshot ResolveRandomMovementRule(TriggerRuleSnapshot rule)
    {
        if (rule.ActionType != OscActionType.PlayerMovement
            || rule.MovementDirection != PlayerMovementDirection.RandomMovement)
        {
            return rule;
        }

        var selectedMovementDirection = PickRandomMovementDirection();
        WriteLog($"Random Movement '{rule.Name}' rolled {DescribeMovementAction(selectedMovementDirection)}.");
        return rule with { MovementDirection = selectedMovementDirection };
    }

    private static PlayerMovementDirection PickRandomMovementDirection(
        PlayerMovementDirection? previousDirection = null)
    {
        if (previousDirection is null || RandomMovementDirections.Length <= 1)
        {
            return RandomMovementDirections[Random.Shared.Next(RandomMovementDirections.Length)];
        }

        PlayerMovementDirection selectedDirection;
        do
        {
            selectedDirection = RandomMovementDirections[Random.Shared.Next(RandomMovementDirections.Length)];
        }
        while (selectedDirection == previousDirection.Value);

        return selectedDirection;
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
            PlayerMovementDirection.RandomMovement => throw new InvalidOperationException("Random Movement must be resolved before sending movement input."),
            PlayerMovementDirection.GlitchyMovement => throw new InvalidOperationException("Glitchy Movement must be resolved before sending movement input."),
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

    private RouletteAvatarEntrySnapshot? PickAvatarRouletTarget(AvatarRouletteProfileSnapshot roulette)
    {
        if (roulette.Pool is null || roulette.Pool.Count == 0)
        {
            return null;
        }

        lock (stateGate)
        {
            var bag = remainingAvatarRouletIndicesByRouletteId.GetOrAdd(roulette.Id, _ => new List<int>());
            if (bag.Count == 0)
            {
                for (var i = 0; i < roulette.Pool.Count; i++)
                {
                    bag.Add(i);
                }
                for (var i = bag.Count - 1; i > 0; i--)
                {
                    var j = Random.Shared.Next(i + 1);
                    (bag[i], bag[j]) = (bag[j], bag[i]);
                }
            }

            var idx = bag[bag.Count - 1];
            bag.RemoveAt(bag.Count - 1);
            return roulette.Pool[idx];
        }
    }

    private AvatarRouletSelection PickAvatarRouletTargetFromRule(TriggerRuleSnapshot rule)
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

        var configuredAvatarIds = configuredAvatars
            .Select(selection => selection.AvatarId)
            .ToHashSet(StringComparer.Ordinal);
        var currentAvatarId = GetCurrentVrChatAvatarId();
        AvatarRouletSelection selectedAvatar;
        lock (stateGate)
        {
            if (!remainingAvatarRouletCandidateIdsByRuleId.TryGetValue(rule.Id, out var remainingAvatarIds))
            {
                remainingAvatarIds = new HashSet<string>(configuredAvatarIds, StringComparer.Ordinal);
                remainingAvatarRouletCandidateIdsByRuleId[rule.Id] = remainingAvatarIds;
            }
            else
            {
                remainingAvatarIds.IntersectWith(configuredAvatarIds);
                if (remainingAvatarIds.Count == 0)
                {
                    remainingAvatarIds.UnionWith(configuredAvatarIds);
                }
            }

            var remainingCandidates = configuredAvatars
                .Where(selection => remainingAvatarIds.Contains(selection.AvatarId))
                .ToArray();
            var candidates = remainingCandidates
                .Where(selection => !string.Equals(selection.AvatarId, currentAvatarId, StringComparison.Ordinal))
                .ToArray();
            if (candidates.Length == 0)
            {
                candidates = remainingCandidates.Length == 0
                    ? configuredAvatars
                    : remainingCandidates;
            }

            selectedAvatar = candidates[Random.Shared.Next(candidates.Length)];
            remainingAvatarIds.Remove(selectedAvatar.AvatarId);
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
        var supporterFloatAddAmount = 0d;
        if (IsSupporterFloatAddRule(rule)
            && !TryResolveSupporterFloatAddAmount(rule, bridgeEvent, out supporterFloatAddAmount, out var supporterFloatAddDiagnostic))
        {
            WriteLog(supporterFloatAddDiagnostic);
            return;
        }

        if (rule.TriggerType == TwitchTriggerType.Subscriptions
            && !IsSubscriptionTierEnabled(rule, bridgeEvent.SubscriptionTier))
        {
            return;
        }

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

        var (capEnabled, capSeconds) = ResolveOverrideCap(rule);
        var requestedDuration = GetSupporterOverrideDuration(rule, bridgeEvent);
        var triggerDuration = ClampSupporterOverrideAddedDuration(rule, requestedDuration, existingRemainingDuration, capEnabled, capSeconds);
        if (triggerDuration <= TimeSpan.Zero)
        {
            WriteLog(TF(
                "Paid override '{0}' is already at its max added time of {1}, so Crystal Relay did not add more time.",
                rule.Name,
                DescribeDuration(Math.Max(1, capSeconds))));
            return;
        }

        if (activeState is not null && activeState.ActiveUntil > now)
        {
            if (activeState.Rule.Id == rule.Id)
            {
                await ExtendActiveSupporterOverrideAsync(activeState, rule, bridgeEvent, triggerDuration, supporterFloatAddAmount, cancellationToken);
                return;
            }

            if (CompareSupporterOverridePriority(rule, activeState.Rule) > 0)
            {
                await PreemptActiveSupporterOverrideAsync(activeState, rule, bridgeEvent, triggerDuration, supporterFloatAddAmount, cancellationToken, queuedReplay);
                return;
            }
        }

        if (queuedIndex >= 0)
        {
            ExtendQueuedSupporterOverride(queuedIndex, rule, bridgeEvent, triggerDuration, supporterFloatAddAmount);
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
                sequenceWasInactive: true,
                supporterFloatAddAmount,
                supporterFloatAddResumeValue: null);
            return;
        }

        QueueSupporterOverride(rule, bridgeEvent, triggerDuration, supporterFloatAddAmount, supporterFloatAddResumeValue: null);
    }

    private async Task ExtendActiveSupporterOverrideAsync(
        ActiveSupporterOverrideState activeState,
        TriggerRuleSnapshot rule,
        BridgeIncomingEvent bridgeEvent,
        TimeSpan extension,
        double supporterFloatAddAmount,
        CancellationToken cancellationToken)
    {
        CancellationTokenSource? previousCancellation = null;
        DateTimeOffset activeUntil;
        string displayValue;
        double? boostedValue = null;

        lock (stateGate)
        {
            if (!ReferenceEquals(activeSupporterOverride, activeState))
            {
                return;
            }

            if (IsSupporterFloatAddRule(rule))
            {
                if (!TryResolveSupporterFloatAddBounds(rule, out var lowerBound, out var upperBound))
                {
                    WriteLog($"Ignored '{rule.Name}' because its supporter float add min/max value is invalid.");
                    return;
                }

                var currentValue = activeState.SupporterFloatAddCurrentValue ?? lowerBound;
                boostedValue = Math.Clamp(currentValue + supporterFloatAddAmount, lowerBound, upperBound);
                activeState.SupporterFloatAddCurrentValue = boostedValue;
                displayValue = FloatValueModeConverter.ToOscText(boostedValue.Value);
            }
            else
            {
                displayValue = activeState.Action.DisplayValue;
            }

            previousCancellation = activeState.CompletionCancellation;
            activeState.Event = bridgeEvent;
            activeState.ActiveUntil = activeState.ActiveUntil.Add(extension);
            activeState.CompletionCancellation = runtimeCancellation is null
                ? new CancellationTokenSource()
                : CancellationTokenSource.CreateLinkedTokenSource(runtimeCancellation.Token);
            activeUntil = activeState.ActiveUntil;
        }

        previousCancellation?.Cancel();
        previousCancellation?.Dispose();

        if (boostedValue is { } nextValue)
        {
            await SendSupporterFloatAddValueAsync(rule, nextValue, cancellationToken);
            RememberAvatarParameterValue(rule, FloatValueModeConverter.ToOscText(nextValue));
            WriteLog($"{bridgeEvent.UserDisplayName} added {FormatSupporterFloatValue(rule, supporterFloatAddAmount)} to '{rule.Name}' ({FormatSupporterFloatValue(rule, nextValue)}).");
        }

        ApplyRuleLockoutUntil(activeState.Rule, activeUntil);
        ScheduleTimedSupporterOverrideCompletion(activeState, activeState.CompletionCancellation);
        WriteLog($"Extended paid override '{activeState.Rule.Name}' by {DescribeDuration(extension.TotalSeconds)}. {DescribeDuration((activeUntil - DateTimeOffset.UtcNow).TotalSeconds)} left.");
        await TrySendBotMessageAsync(rule, bridgeEvent, displayValue, cancellationToken, extension.TotalSeconds);
    }

    private void ExtendQueuedSupporterOverride(
        int queuedIndex,
        TriggerRuleSnapshot rule,
        BridgeIncomingEvent bridgeEvent,
        TimeSpan extension,
        double supporterFloatAddAmount)
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
            if (IsSupporterFloatAddRule(rule))
            {
                queuedOverride.SupporterFloatAddAmount += supporterFloatAddAmount;
            }

            newRemainingDuration = queuedOverride.RemainingDuration;
        }

        WriteLog($"Added more time to queued paid override '{rule.Name}'. It now has {DescribeDuration(newRemainingDuration.TotalSeconds)} waiting.");
    }

    private void QueueSupporterOverride(
        TriggerRuleSnapshot rule,
        BridgeIncomingEvent bridgeEvent,
        TimeSpan duration,
        double supporterFloatAddAmount,
        double? supporterFloatAddResumeValue)
    {
        var queuedCount = 0;
        lock (stateGate)
        {
            queuedSupporterOverrides.Add(new QueuedSupporterOverrideState(
                rule,
                bridgeEvent,
                duration,
                GetNextSupporterOverrideQueueOrder(),
                supporterFloatAddAmount,
                supporterFloatAddResumeValue));
            queuedCount = queuedSupporterOverrides.Count;
        }

        WriteLog($"Queued paid override '{rule.Name}'. {queuedCount} paid override{(queuedCount == 1 ? string.Empty : "s")} waiting.");
    }

    private int QueueAvatarSwitchTrigger(TriggerRuleSnapshot rule, BridgeIncomingEvent bridgeEvent)
    {
        lock (stateGate)
        {
            queuedAvatarSwitches.Enqueue(QueuedAvatarSwitchState.ForTrigger(
                rule,
                bridgeEvent,
                ++nextQueuedAvatarSwitchOrder));
            return queuedAvatarSwitches.Count;
        }
    }

    private int QueuePausedAvatarSwitchLocked(PendingResetState pendingReset, TimeSpan remainingDuration)
    {
        queuedAvatarSwitches.Enqueue(QueuedAvatarSwitchState.ForPausedSwitch(
            pendingReset.Rule,
            pendingReset.Action,
            remainingDuration,
            ++nextQueuedAvatarSwitchOrder));
        return queuedAvatarSwitches.Count;
    }

    private async Task PreemptActiveSupporterOverrideAsync(
        ActiveSupporterOverrideState activeState,
        TriggerRuleSnapshot newRule,
        BridgeIncomingEvent bridgeEvent,
        TimeSpan newDuration,
        double supporterFloatAddAmount,
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
                    activeState.QueueOrder,
                    supporterFloatAddAmount: 0,
                    supporterFloatAddResumeValue: activeState.SupporterFloatAddCurrentValue));
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
            sequenceWasInactive: false,
            supporterFloatAddAmount,
            supporterFloatAddResumeValue: null);
    }

    private async Task StartTimedSupporterOverrideAsync(
        TriggerRuleSnapshot rule,
        BridgeIncomingEvent bridgeEvent,
        TimeSpan duration,
        long queueOrder,
        CancellationToken cancellationToken,
        bool queuedReplay,
        bool resumedFromQueue,
        bool sequenceWasInactive,
        double supporterFloatAddAmount,
        double? supporterFloatAddResumeValue)
    {
        var effectiveRule = rule;
        double? supporterFloatAddCurrentValue = null;
        if (IsSupporterFloatAddRule(rule))
        {
            if (!TryResolveSupporterFloatAddBounds(rule, out var lowerBound, out var upperBound))
            {
                WriteLog($"Ignored '{rule.Name}' because its supporter float add min/max value is invalid.");
                return;
            }

            var address = VrChatOscClient.NormalizeAvatarParameterAddress(rule.ParameterName);
            var startValue = supporterFloatAddResumeValue
                ?? await TryGetCurrentAvatarFloatValueAsync(address, lowerBound, cancellationToken);
            supporterFloatAddCurrentValue = Math.Clamp(startValue + supporterFloatAddAmount, lowerBound, upperBound);
            effectiveRule = rule with
            {
                ParameterValue = FloatValueModeConverter.ToDisplayText(rule.FloatValueMode, supporterFloatAddCurrentValue.Value)
            };
        }

        effectiveRule = CreateTimedSupporterOverrideExecutionRule(effectiveRule, duration, cooldownSeconds: 0);
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
            completionCancellation,
            supporterFloatAddCurrentValue);

        lock (stateGate)
        {
            activeSupporterOverride = activeState;
        }

        ApplyRuleLockoutUntil(rule, activeUntil);
        ScheduleTimedSupporterOverrideCompletion(activeState, completionCancellation);
        EnsureQueuedAvatarSwitchDrain();

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

        if (supporterFloatAddCurrentValue is { } targetValue && supporterFloatAddAmount > 0)
        {
            WriteLog($"{bridgeEvent.UserDisplayName} added {FormatSupporterFloatValue(rule, supporterFloatAddAmount)} to '{rule.Name}' ({FormatSupporterFloatValue(rule, targetValue)}).");
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

    private void ScheduleTimedSupporterOverrideCompletionAfterGracePeriod(
        ActiveSupporterOverrideState activeState,
        CancellationTokenSource completionCancellation)
    {
        _ = Task.Run(async () =>
        {
            try
            {
                // Wait for the grace period to end
                var graceRemaining = AvatarChangeGracePeriod - (DateTimeOffset.UtcNow - lastAvatarChangeAt);
                if (graceRemaining > TimeSpan.Zero)
                {
                    await Task.Delay(graceRemaining, completionCancellation.Token);
                }

                // Check if we should still proceed (state might have changed)
                lock (stateGate)
                {
                    if (!ReferenceEquals(activeSupporterOverride, activeState)
                        || !ReferenceEquals(activeState.CompletionCancellation, completionCancellation))
                    {
                        return;
                    }
                }

                // Now complete the timed override
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

        // If the avatar recently changed, defer the reset until the grace period ends.
        // This prevents reset packets from being lost during world transitions.
        if (IsInAvatarChangeGracePeriod())
        {
            var graceRemaining = AvatarChangeGracePeriod - (DateTimeOffset.UtcNow - lastAvatarChangeAt);
            WriteLog($"Paid override '{activeState.Rule.Name}' completion is deferred because the avatar recently changed. The reset will be retried after the grace period. ({DescribeDuration(graceRemaining.TotalSeconds)} remaining)");
            ScheduleTimedSupporterOverrideCompletionAfterGracePeriod(activeState, completionCancellation);
            return;
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
        double nextSupporterFloatAddAmount = 0;
        double? nextSupporterFloatAddResumeValue = null;
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
                nextSupporterFloatAddAmount = queuedOverride.SupporterFloatAddAmount;
                nextSupporterFloatAddResumeValue = queuedOverride.SupporterFloatAddResumeValue;
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
                sequenceWasInactive: false,
                nextSupporterFloatAddAmount,
                nextSupporterFloatAddResumeValue);
            return;
        }

        if (!sequenceStillActive)
        {
            ManagedRewardAvailabilityChanged?.Invoke();
            EnsureQueuedLaneDrain(AvatarSwitchLaneKey);
            EnsureQueuedAvatarSwitchDrain();
        }
    }

    private async Task CancelBlockedChannelPointEffectsForSupporterOverrideAsync(CancellationToken cancellationToken)
    {
        List<PendingResetState> blockedResets;
        List<PendingResetState> pausedAvatarSwitches;
        List<(PendingResetState Reset, TimeSpan RemainingDuration, int QueueCount)> queuedPausedAvatarSwitches;
        lock (stateGate)
        {
            blockedResets = [];
            pausedAvatarSwitches = [];
            queuedPausedAvatarSwitches = [];
            var now = DateTimeOffset.UtcNow;
            foreach (var reset in pendingResets.Values)
            {
                if (ShouldBlockRuleDuringSupporterOverride(reset.Rule))
                {
                    if (IsPauseableAvatarSwitchReset(reset))
                    {
                        var remainingDuration = reset.DueAt - now;
                        if (remainingDuration > TimeSpan.Zero)
                        {
                            pausedAvatarSwitches.Add(reset);
                            var queueCount = QueuePausedAvatarSwitchLocked(reset, remainingDuration);
                            queuedPausedAvatarSwitches.Add((reset, remainingDuration, queueCount));
                        }
                        else
                        {
                            blockedResets.Add(reset);
                        }
                    }
                    else
                    {
                        blockedResets.Add(reset);
                    }
                }
            }

            foreach (var blockedReset in blockedResets.Concat(pausedAvatarSwitches))
            {
                pendingResets.Remove(blockedReset.RuleId);
            }
        }

        foreach (var pausedAvatarSwitch in pausedAvatarSwitches)
        {
            pausedAvatarSwitch.Cancellation.Cancel();
            ReleaseActiveRuleLockoutState(pausedAvatarSwitch.RuleId, logRelease: false);
            ReleaseActiveAvatarSwitchLockoutState(pausedAvatarSwitch.RuleId, logRelease: false);
            var releasedLaneKeys = ReleaseMovementLanes(pausedAvatarSwitch.MovementLaneLeaseId, pausedAvatarSwitch.MovementLaneKeys);
            foreach (var releasedLaneKey in releasedLaneKeys)
            {
                if (!string.Equals(releasedLaneKey, AvatarSwitchLaneKey, StringComparison.Ordinal))
                {
                    EnsureQueuedLaneDrain(releasedLaneKey);
                }
            }

            pausedAvatarSwitch.Cancellation.Dispose();
        }

        foreach (var (reset, remainingDuration, queueCount) in queuedPausedAvatarSwitches)
        {
            WriteLog($"Paused avatar switch '{reset.RuleName}' because a paid override took priority. It will resume with {DescribeDuration(remainingDuration.TotalSeconds)} left. {queueCount} avatar switch{(queueCount == 1 ? string.Empty : "es")} waiting.");
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
        if (rule.SpecialRulePairingMode == SpecialRulePairingMode.ShowPairedWhileActive)
        {
            ApplyRuleUnlockUntil(rule, expiresAt);
            return;
        }

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
            activeRuleUnlocks.Remove(rule.Id);
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

    private void ApplyRuleUnlockUntil(TriggerRuleSnapshot rule, DateTimeOffset expiresAt)
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
            activeRuleLockouts.Remove(rule.Id);
            activeRuleUnlocks[rule.Id] = new ActiveRuleLockoutState(rule.Name, expiresAt, normalizedRuleIds);
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

    private async Task ExecuteMovementSoftLockAsync(TriggerRuleSnapshot rule, CancellationToken cancellationToken, bool isResuming = false)
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

    private IReadOnlyList<string> GetGlitchyMovementLaneKeys() =>
        [.. RandomMovementDirections
            .Select(GetMovementLaneKey)
            .Where(laneKey => !string.IsNullOrWhiteSpace(laneKey))
            .Select(laneKey => laneKey!)
            .Distinct(StringComparer.Ordinal)];

    private IReadOnlyList<string> GetActionLaneKeys(
        TriggerRuleSnapshot rule,
        ResolvedRuleAction? resolvedAction = null)
    {
        if (IsQueuedAvatarSwitchRule(rule))
        {
            return [AvatarSwitchLaneKey];
        }

        if (rule.ActionType == OscActionType.PlayerMovement && !IsSoftLockMovement(rule.MovementDirection))
        {
            if (rule.MovementDirection == PlayerMovementDirection.GlitchyMovement)
            {
                return GetGlitchyMovementLaneKeys();
            }

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

        if (rule.ParameterType == OscParameterType.Float)
        {
            return await ResolveFloatActionAsync(rule, cancellationToken).ConfigureAwait(false);
        }

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
                var targetValue = ResolveAvatarParameterPacketValue(rule.ParameterType, rule.FloatValueMode, rule.ParameterValue);
                var targetPacket = vrChatOscClient.BuildAvatarParameterPacket(address, rule.ParameterType, targetValue);
                var displayValue = targetValue;
                var observedValues = Array.Empty<OscObservedValue>();
                if (TryCreateObservedValueFromText(address, rule.ParameterType, targetValue, out var targetText, out var targetObservedValue))
                {
                    displayValue = targetText;
                    observedValues = [targetObservedValue];
                }

                byte[]? resetPacket = null;
                var resetObservedValues = Array.Empty<OscObservedValue>();
                if (rule.DurationSeconds > 0 && !string.IsNullOrWhiteSpace(rule.ResetValue))
                {
                    var resetValue = ResolveAvatarParameterPacketValue(rule.ParameterType, rule.FloatValueMode, rule.ResetValue);
                    resetPacket = vrChatOscClient.BuildAvatarParameterPacket(address, rule.ParameterType, resetValue);
                    if (TryCreateObservedValueFromText(address, rule.ParameterType, resetValue, out _, out var resetObservedValue))
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

    private async Task<ResolvedRuleAction> ResolveFloatActionAsync(
        TriggerRuleSnapshot rule,
        CancellationToken cancellationToken)
    {
        var address = VrChatOscClient.NormalizeAvatarParameterAddress(rule.ParameterName);
        var sourceRule = rule.Rule;

        var fallback = FloatValueModeConverter.TryParseNormalized(
            rule.FloatValueMode, rule.ParameterValue, out var fallbackValue) ? fallbackValue : 0.0;
        var currentValue = await TryGetCurrentAvatarFloatValueAsync(address, fallback, cancellationToken).ConfigureAwait(false);

        var (nextValue, configuredReset) = FloatActionDispatch.ComputeNext(sourceRule, currentValue);
        var targetPacket = vrChatOscClient.BuildAvatarParameterPacket(
            address, OscParameterType.Float,
            FloatValueModeConverter.ToOscText(nextValue));

        if (sourceRule.FloatActionMode == FloatActionMode.Pulse)
        {
            var pulseReset = configuredReset ?? ClampAvatarFloatValue(currentValue);
            var pulsePacket = vrChatOscClient.BuildAvatarParameterPacket(
                address, OscParameterType.Float,
                FloatValueModeConverter.ToOscText(nextValue));
            ScheduleFloatPulseRestore(sourceRule, address, pulsePacket, pulseReset);
            return new ResolvedRuleAction(
                packets: new[] { pulsePacket },
                resetPackets: Array.Empty<byte[]>(),
                displayValue: FloatValueModeConverter.ToOscText(nextValue));
        }

        var effectiveReset = configuredReset ?? (sourceRule.DurationSeconds > 0 ? ClampAvatarFloatValue(currentValue) : (double?)null);
        var resetPackets = effectiveReset.HasValue
            ? new[]
              {
                  vrChatOscClient.BuildAvatarParameterPacket(
                      address, OscParameterType.Float,
                      FloatValueModeConverter.ToOscText(effectiveReset.Value))
              }
            : Array.Empty<byte[]>();

        if (sourceRule.FloatActionMode == FloatActionMode.Glitchy && sourceRule.DurationSeconds > 0)
        {
            return ResolveGlitchyFloatSession(rule, address, nextValue, effectiveReset ?? ClampAvatarFloatValue(currentValue));
        }

        return new ResolvedRuleAction(
            packets: new[] { targetPacket },
            resetPackets: resetPackets,
            displayValue: FloatValueModeConverter.ToOscText(nextValue));
    }

    private static double ClampAvatarFloatValue(double value)
    {
        if (double.IsNaN(value) || double.IsInfinity(value)) return 0.0;
        return Math.Clamp(value, 0.0, 1.0);
    }

    private void ScheduleFloatPulseRestore(
        TriggerRule sourceRule, string address, byte[] initialPacket, double resetValue)
    {
        var seconds = Math.Max(0.0, sourceRule.FloatPulseSeconds);
        _ = Task.Run(async () =>
        {
            try
            {
                await Task.Delay(TimeSpan.FromSeconds(seconds)).ConfigureAwait(false);
                var resetPacket = vrChatOscClient.BuildAvatarParameterPacket(
                    address, OscParameterType.Float,
                    FloatValueModeConverter.ToOscText(resetValue));
                await oscRouterService.SendToVrChatAsync(resetPacket).ConfigureAwait(false);
                ObserveOscValue(new OscObservedValue(address, OscParameterType.Float, (float)resetValue));
            }
            catch (TaskCanceledException) { }
            catch (Exception ex)
            {
                WriteLog($"Pulse restore for '{sourceRule.Name}' failed: {ex.Message}");
            }
        });
    }

    private ResolvedRuleAction ResolveGlitchyFloatSession(
        TriggerRuleSnapshot rule, string address, double nextValue, double resetValue)
    {
        var leaseId = Guid.NewGuid();
        var session = new ActiveFloatGlitchyRedeemSessionState
        {
            Rule = rule,
            Address = address,
            Min = rule.Rule.FloatRangeMin,
            Max = rule.Rule.FloatRangeMax,
            IntervalMs = rule.Rule.FloatGlitchyIntervalMs,
            ActiveUntil = DateTimeOffset.UtcNow.AddSeconds(rule.Rule.DurationSeconds),
            ResetValue = resetValue,
            LeaseId = leaseId,
        };

        lock (stateGate)
        {
            if (activeGlitchyRedeemSessions.TryGetValue(rule.Rule.Id, out var prior))
            {
                prior.CompletionCancellation.Cancel();
                prior.CompletionCancellation.Dispose();
                activeGlitchyRedeemSessions.Remove(rule.Rule.Id);
            }
            activeGlitchyRedeemSessions[rule.Rule.Id] = session;
        }

        _ = Task.Run(() => RunGlitchyLoopAsync(session));
        return new ResolvedRuleAction(
            packets: new[] { vrChatOscClient.BuildAvatarParameterPacket(
                address, OscParameterType.Float,
                FloatValueModeConverter.ToOscText(nextValue)) },
            resetPackets: Array.Empty<byte[]>(),
            displayValue: FloatValueModeConverter.ToOscText(nextValue));
    }

    private async Task RunGlitchyLoopAsync(ActiveFloatGlitchyRedeemSessionState session)
    {
        try
        {
            while (!session.CompletionCancellation.IsCancellationRequested
                   && DateTimeOffset.UtcNow < session.ActiveUntil)
            {
                await Task.Delay(session.IntervalMs, session.CompletionCancellation.Token)
                    .ConfigureAwait(false);
                if (session.CompletionCancellation.IsCancellationRequested) break;
                var value = Random.Shared.NextDouble() * (session.Max - session.Min) + session.Min;
                await SendSingleFloatAvatarParameterValueAsync(
                    session.Address, value, session.CompletionCancellation.Token)
                    .ConfigureAwait(false);
            }
            if (!session.CompletionCancellation.IsCancellationRequested)
            {
                await SendSingleFloatAvatarParameterValueAsync(
                    session.Address, session.ResetValue, session.CompletionCancellation.Token)
                    .ConfigureAwait(false);
            }
        }
        catch (TaskCanceledException) { /* expected on stop */ }
        catch (Exception ex)
        {
            WriteLog($"Glitchy loop for '{session.Rule.Name}' failed: {ex.Message}");
        }
        finally
        {
            lock (stateGate)
            {
                if (activeGlitchyRedeemSessions.TryGetValue(session.Rule.Id, out var current)
                    && current.LeaseId == session.LeaseId)
                {
                    activeGlitchyRedeemSessions.Remove(session.Rule.Id);
                }
            }
            session.CompletionCancellation.Dispose();
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
            if (rule.UsesSharedNumberedOutfitReward)
            {
                throw new InvalidOperationException("Set Trigger shared outfits need an outfit number.");
            }
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
        var restoreMode = NormalizeSetTriggerRestoreMode(rule.SetTriggerRestoreMode);
        var preTriggerSnapshot = await TryReadSetTriggerRestoreSnapshotAsync(
            rule.Name,
            sourceAvatarId,
            preparedActions,
            restoreMode,
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
            restoreMode == SetTriggerRestoreMode.ConfiguredOnly
                ? $"Set Trigger ({packets.Count} params)"
                : $"Set Trigger ({packets.Count} params, learning restore diff)",
            observedValues: observedValues,
            resetObservedValues: resetObservedValues,
            setTriggerRestorePlan: restoreMode == SetTriggerRestoreMode.ConfiguredOnly
                ? null
                : new SetTriggerRestorePlan(
                    sourceAvatarId,
                    preTriggerSnapshot.Values,
                    preTriggerSnapshot.LastWriteTimeUtc,
                    preTriggerSnapshot.SourcePath,
                    restoreMode,
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

    private async Task<LocalAvatarDataParameterBatchReadResult> TryReadSetTriggerRestoreSnapshotAsync(
        string ruleName,
        string sourceAvatarId,
        IReadOnlyList<SetTriggerPreparedAction> preparedActions,
        SetTriggerRestoreMode restoreMode,
        CancellationToken cancellationToken)
    {
        var normalizedSourceAvatarId = sourceAvatarId?.Trim() ?? string.Empty;
        if (string.IsNullOrWhiteSpace(normalizedSourceAvatarId))
        {
            throw new InvalidOperationException($"Safe-canceled Set Trigger '{ruleName}' because Crystal Relay does not know the current VRChat avatar yet.");
        }

        var localValues = await TryReadSetTriggerRestoreSnapshotValuesAsync(
            normalizedSourceAvatarId,
            BuildSetTriggerRestoreRequests(preparedActions),
            restoreMode,
            cancellationToken);
        if (!localValues.Found)
        {
            throw new InvalidOperationException($"Safe-canceled Set Trigger '{ruleName}' because Crystal Relay could not capture a {DescribeSetTriggerRestoreMode(restoreMode)} LocalAvatarData restore snapshot: {localValues.FailureReason}");
        }

        var ageSeconds = Math.Max(0, (DateTime.UtcNow - localValues.LastWriteTimeUtc).TotalSeconds);
        WriteLog($"Captured {DescribeSetTriggerRestoreMode(restoreMode)} Set Trigger restore snapshot for '{ruleName}' with {localValues.Values.Count} safe typed value(s) from {DescribeLocalAvatarDataSource(localValues.SourcePath)} for avatar {DescribeAvatarId(normalizedSourceAvatarId)}. Cache age: {DescribeDuration(ageSeconds)}.");

        return localValues with
        {
            Values = new Dictionary<string, OscObservedValue>(localValues.Values, StringComparer.OrdinalIgnoreCase),
            MatchedParameterNames = new Dictionary<string, string>(localValues.MatchedParameterNames, StringComparer.OrdinalIgnoreCase)
        };
    }

    private Task<LocalAvatarDataParameterBatchReadResult> TryReadSetTriggerRestoreSnapshotValuesAsync(
        string sourceAvatarId,
        IReadOnlyList<LocalAvatarDataParameterRequest> requests,
        SetTriggerRestoreMode restoreMode,
        CancellationToken cancellationToken)
    {
        return restoreMode switch
        {
            SetTriggerRestoreMode.FullSafeDiff => vrChatLocalAvatarDataService.TryReadAvatarFullSnapshotValuesAsync(
                sourceAvatarId,
                cancellationToken),
            SetTriggerRestoreMode.ConfiguredOnly => vrChatLocalAvatarDataService.TryReadAvatarParameterValuesAsync(
                sourceAvatarId,
                requests,
                cancellationToken),
            _ => vrChatLocalAvatarDataService.TryReadAvatarOutfitSnapshotValuesAsync(
                sourceAvatarId,
                requests,
                cancellationToken)
        };
    }

    private static SetTriggerRestoreMode NormalizeSetTriggerRestoreMode(SetTriggerRestoreMode restoreMode) =>
        Enum.IsDefined(restoreMode) ? restoreMode : SetTriggerRestoreMode.ConfiguredAndRelated;

    private static string DescribeSetTriggerRestoreMode(SetTriggerRestoreMode restoreMode) => restoreMode switch
    {
        SetTriggerRestoreMode.FullSafeDiff => "full safe diff",
        SetTriggerRestoreMode.ConfiguredOnly => "configured-parameter",
        _ => "configured and related outfit"
    };

    private static IReadOnlyList<LocalAvatarDataParameterRequest> BuildSetTriggerRestoreRequests(
        IReadOnlyList<SetTriggerPreparedAction> preparedActions)
    {
        return [.. preparedActions.Select(action => new LocalAvatarDataParameterRequest(action.Address, action.ParameterType))];
    }

    private static IReadOnlyList<LocalAvatarDataParameterRequest> BuildSetTriggerRestoreRequests(
        IReadOnlyList<string> configuredAddresses,
        IReadOnlyDictionary<string, OscObservedValue> snapshotValues)
    {
        var requests = new List<LocalAvatarDataParameterRequest>(configuredAddresses.Count);
        foreach (var address in configuredAddresses)
        {
            if (snapshotValues.TryGetValue(address, out var observedValue))
            {
                requests.Add(new LocalAvatarDataParameterRequest(address, observedValue.ParameterType));
            }
        }

        return requests;
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

    private static string ResolveAvatarParameterPacketValue(
        OscParameterType parameterType,
        FloatValueMode floatValueMode,
        string rawValue)
    {
        if (parameterType != OscParameterType.Float)
        {
            return rawValue;
        }

        if (!FloatValueModeConverter.TryParseNormalized(floatValueMode, rawValue, out var normalizedValue))
        {
            throw new InvalidOperationException("Enter a valid float value before testing or redeeming this rule.");
        }

        return FloatValueModeConverter.ToOscText(normalizedValue);
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
        bool notifyManagedRewardState = true,
        bool isTest = false)
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
        var dueAt = DateTimeOffset.UtcNow.AddSeconds(Math.Max(1, delaySeconds));
        var pendingReset = new PendingResetState(
            rule.Id,
            rule.Name,
            rule,
            action,
            action.ResetPackets,
            cancellation,
            dueAt,
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

                if (TryGetReadyQueuedAvatarSwitchNameForDirectTransition(pendingReset, out var queuedAvatarSwitchName))
                {
                    WriteLog($"Skipped return avatar for '{rule.Name}' because queued avatar switch '{queuedAvatarSwitchName}' is ready.");
                    return;
                }

                // If the avatar recently changed, defer the reset until the grace period ends.
                // This prevents reset packets from being lost during world transitions.
                // Tests skip this check so the active-time reset always fires on schedule.
                if (!isTest && IsInAvatarChangeGracePeriod())
                {
                    var graceRemaining = AvatarChangeGracePeriod - (DateTimeOffset.UtcNow - lastAvatarChangeAt);
                    keepPendingReset = MarkPendingResetWaitingForSourceAvatarReturn(pendingReset);
                    if (keepPendingReset)
                    {
                        var resetLabel = rule.ActionType == OscActionType.SetTrigger ? "Set Trigger restore" : "Timed reset";
                        WriteLog($"{resetLabel} for '{rule.Name}' is deferred because the avatar recently changed. Waiting for the grace period to end. ({DescribeDuration(graceRemaining.TotalSeconds)} remaining)");
                        return;
                    }
                }

                if (!isTest
                    && pendingReset.HasPackets
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
                            if (string.Equals(releasedLaneKey, AvatarSwitchLaneKey, StringComparison.Ordinal))
                            {
                                EnsureQueuedAvatarSwitchDrain();
                            }
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
        var postTriggerSnapshot = await TryReadSetTriggerRestoreSnapshotValuesAsync(
            restorePlan.SourceAvatarId,
            BuildSetTriggerRestoreRequests(restorePlan.ConfiguredRestoreAddresses, restorePlan.PreTriggerSnapshotValues),
            restorePlan.RestoreMode,
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

        var restoreResolution = BuildSetTriggerRestoreResolution(
            restoreValues.Values,
            restorePlan.ConfiguredRestoreAddresses);
        WriteLog($"Set Trigger '{ruleName}' learned {changedCount} changed LocalAvatarData param{(changedCount == 1 ? string.Empty : "s")} and will restore {restoreResolution.Packets.Count} param{(restoreResolution.Packets.Count == 1 ? string.Empty : "s")}.");
        return restoreResolution;
    }

    private SetTriggerRestoreResolution BuildSetTriggerRestoreResolution(
        IEnumerable<OscObservedValue> restoreValues,
        IReadOnlyList<string> configuredRestoreAddresses)
    {
        var packets = new List<byte[]>();
        var observedValues = new List<OscObservedValue>();
        var valuesByAddress = restoreValues
            .GroupBy(value => value.Address, StringComparer.OrdinalIgnoreCase)
            .ToDictionary(group => group.Key, group => group.First(), StringComparer.OrdinalIgnoreCase);
        var orderedRestoreValues = new List<OscObservedValue>(valuesByAddress.Count);

        foreach (var configuredAddress in configuredRestoreAddresses)
        {
            if (valuesByAddress.Remove(configuredAddress, out var configuredValue))
            {
                orderedRestoreValues.Add(configuredValue);
            }
        }

        orderedRestoreValues.AddRange(valuesByAddress.Values.OrderBy(value => value.Address, StringComparer.OrdinalIgnoreCase));

        foreach (var restoreValue in orderedRestoreValues)
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
        bool notifyManagedRewardState = true,
        bool isTest = false)
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

    private bool TryGetReadyQueuedAvatarSwitchNameForDirectTransition(
        PendingResetState pendingReset,
        out string queuedRuleName)
    {
        queuedRuleName = string.Empty;
        if (!IsPauseableAvatarSwitchReset(pendingReset))
        {
            return false;
        }

        lock (stateGate)
        {
            if (queuedAvatarSwitches.Count == 0)
            {
                return false;
            }

            var now = DateTimeOffset.UtcNow;
            if (IsSupporterOverrideSequenceActiveLocked(now))
            {
                return false;
            }

            var nextSwitch = queuedAvatarSwitches.Peek();
            var currentRule = activeConfiguration?.Rules.FirstOrDefault(rule => rule.Id == nextSwitch.Rule.Id);
            if (currentRule is null || !currentRule.IsEnabled || !IsQueuedAvatarSwitchRule(currentRule))
            {
                return false;
            }

            if (TryGetTemporarilyDisabledUntilLocked(currentRule.Id, now, out var temporarilyDisabledUntil)
                && temporarilyDisabledUntil > now)
            {
                return false;
            }

            if (cooldowns.TryGetValue(currentRule.Id, out var cooldownUntil) && cooldownUntil > now)
            {
                return false;
            }

            queuedRuleName = currentRule.Name;
            return true;
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

    private void CancelStalePendingResets(string newAvatarId)
    {
        List<PendingResetState> staleResets;
        lock (stateGate)
        {
            staleResets = pendingResets.Values
                .Where(reset =>
                    reset.IsWaitingForSourceAvatarReturn
                    && !string.IsNullOrWhiteSpace(reset.SourceAvatarId)
                    && !string.Equals(reset.SourceAvatarId, newAvatarId, StringComparison.Ordinal))
                .ToList();
        }

        foreach (var staleReset in staleResets)
        {
            staleReset.Cancellation.Cancel();
            lock (stateGate)
            {
                pendingResets.Remove(staleReset.RuleId);
            }
            WriteLog($"Cancelled stale pending reset for '{staleReset.RuleName}' because the source avatar will not return after the world change.");
            var releasedLaneKeys = ReleaseMovementLanes(staleReset.MovementLaneLeaseId, staleReset.MovementLaneKeys);
            foreach (var releasedLaneKey in releasedLaneKeys)
            {
                EnsureQueuedLaneDrain(releasedLaneKey);
            }
            staleReset.Cancellation.Dispose();
        }
    }

    private void ResumePendingAvatarScopedResetsForCurrentAvatar(string avatarId)
    {
        var normalizedAvatarId = avatarId?.Trim() ?? string.Empty;
        if (string.IsNullOrWhiteSpace(normalizedAvatarId))
        {
            return;
        }

        CancelStalePendingResets(normalizedAvatarId);

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
                || activeRuleUnlocks.Count > 0
                || activeAvatarSwitchRuleLockouts.Count > 0
                || activeSupporterOverride is not null
                || queuedSupporterOverrides.Count > 0
                || queuedAvatarSwitches.Count > 0;
            activeRuleLockouts.Clear();
            activeRuleUnlocks.Clear();
            activeAvatarSwitchRuleLockouts.Clear();
            remainingAvatarRouletCandidateIdsByRuleId.Clear();
            queuedAvatarSwitches.Clear();
            drainingQueuedAvatarSwitches = false;
            activeSupporterState = activeSupporterOverride;
            activeSupporterOverride = null;
            queuedSupporterOverrides.Clear();
            nextSupporterOverrideQueueOrder = 0;
            nextQueuedAvatarSwitchOrder = 0;
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

    private void NotifyRewardCooldownColorChanged(Guid ruleId)
    {
        try
        {
            RewardCooldownColorChanged?.Invoke(ruleId);
        }
        catch (Exception ex)
        {
            DebugLogService.Write($"Failed to notify reward cooldown color change for rule {ruleId}: {ex.Message}");
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
                    NotifyRewardCooldownColorChanged(ruleId);
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
                    NotifyRewardCooldownColorChanged(ruleId);
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
                NotifyRewardCooldownColorChanged(AvatarScaleMasterRewardOwnerGuid);
                if (isUnlockNotification)
                {
                    // The unlock window just closed, so the child avatar-scale rewards need
                    // to be re-hidden on Twitch. The cooldown notification that follows does
                    // not change the unlock state and must not queue another sync.
                    AvatarScaleMasterRewardUnlockStateChanged?.Invoke();
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

                if (isAvatarRuleLockout
                    && currentRule!.SpecialRulePairingMode == SpecialRulePairingMode.ShowPairedWhileActive)
                {
                    var existingLockoutState = activeRuleLockouts[sourceRuleId];
                    var normalizedUnlockRuleIds = currentRule.TemporarilyDisabledRuleIds
                        .Where(ruleId => ruleId != sourceRuleId && validRulesById.ContainsKey(ruleId))
                        .Distinct()
                        .ToArray();
                    activeRuleLockouts.Remove(sourceRuleId);
                    if (GetPairingLockoutDurationSeconds(currentRule) > 0
                        && normalizedUnlockRuleIds.Length > 0
                        && existingLockoutState.ExpiresAt > now)
                    {
                        activeRuleUnlocks[sourceRuleId] = new ActiveRuleLockoutState(
                            currentRule.Name,
                            existingLockoutState.ExpiresAt,
                            normalizedUnlockRuleIds);
                    }
                    else if (lockoutStateNotifications.Remove(sourceRuleId, out var removedNotification))
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
                    ? GetPairingLockoutDurationSeconds(currentRule!)
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

            foreach (var sourceRuleId in activeRuleUnlocks.Keys.ToArray())
            {
                if (!validRulesById.TryGetValue(sourceRuleId, out var currentRule))
                {
                    activeRuleUnlocks.Remove(sourceRuleId);
                    if (lockoutStateNotifications.Remove(sourceRuleId, out var removedNotification))
                    {
                        notificationsToDispose ??= [];
                        notificationsToDispose.Add(removedNotification);
                    }
                    continue;
                }

                var normalizedRuleIds = currentRule.TemporarilyDisabledRuleIds
                    .Where(ruleId => ruleId != sourceRuleId && validRulesById.ContainsKey(ruleId))
                    .Distinct()
                    .ToArray();
                var lockoutDurationSeconds = GetPairingLockoutDurationSeconds(currentRule);

                if (currentRule.SpecialRulePairingMode != SpecialRulePairingMode.ShowPairedWhileActive)
                {
                    var unlockStateToMove = activeRuleUnlocks[sourceRuleId];
                    activeRuleUnlocks.Remove(sourceRuleId);
                    if (lockoutDurationSeconds > 0
                        && normalizedRuleIds.Length > 0
                        && unlockStateToMove.ExpiresAt > now)
                    {
                        activeRuleLockouts[sourceRuleId] = new ActiveRuleLockoutState(
                            currentRule.Name,
                            unlockStateToMove.ExpiresAt,
                            normalizedRuleIds);
                    }
                    else if (lockoutStateNotifications.Remove(sourceRuleId, out var removedNotification))
                    {
                        notificationsToDispose ??= [];
                        notificationsToDispose.Add(removedNotification);
                    }

                    continue;
                }

                if (lockoutDurationSeconds <= 0 || normalizedRuleIds.Length == 0)
                {
                    activeRuleUnlocks.Remove(sourceRuleId);
                    if (lockoutStateNotifications.Remove(sourceRuleId, out var removedNotification))
                    {
                        notificationsToDispose ??= [];
                        notificationsToDispose.Add(removedNotification);
                    }
                    continue;
                }

                var existingUnlockState = activeRuleUnlocks[sourceRuleId];
                activeRuleUnlocks[sourceRuleId] = new ActiveRuleLockoutState(
                    currentRule.Name,
                    existingUnlockState.ExpiresAt,
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

            if (activeAvatarScaleCarryover is not null
                && !validAvatarScaleRuleIds.Contains(activeAvatarScaleCarryover.SourceRuleId))
            {
                activeAvatarScaleCarryover = null;
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

            foreach (var ruleId in avatarScaleFollowTriggeredUsers.Keys.ToArray())
            {
                if (!validAvatarScaleRuleIds.Contains(ruleId))
                {
                    avatarScaleFollowTriggeredUsers.Remove(ruleId);
                }
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
        if (rule.SpecialRulePairingMode == SpecialRulePairingMode.ShowPairedWhileActive)
        {
            UpdateActiveRuleUnlockState(rule);
            return;
        }

        var normalizedRuleIds = rule.TemporarilyDisabledRuleIds
            .Where(ruleId => ruleId != rule.Id)
            .Distinct()
            .ToArray();
        var lockoutDurationSeconds = GetPairingLockoutDurationSeconds(rule);

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
            activeRuleUnlocks.Remove(rule.Id);
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
            WriteLog($"'{rule.Name}' temporarily disabled {normalizedRuleIds.Length} linked redeem{(normalizedRuleIds.Length == 1 ? string.Empty : "s")} while it cools down.");
            ManagedRewardAvailabilityChanged?.Invoke();
        }
    }

    private void UpdateActiveRuleUnlockState(TriggerRuleSnapshot rule)
    {
        var normalizedRuleIds = rule.TemporarilyDisabledRuleIds
            .Where(ruleId => ruleId != rule.Id)
            .Distinct()
            .ToArray();
        var lockoutDurationSeconds = GetPairingLockoutDurationSeconds(rule);

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
            activeRuleLockouts.Remove(rule.Id);
            activeRuleUnlocks[rule.Id] = new ActiveRuleLockoutState(
                rule.Name,
                now.AddSeconds(lockoutDurationSeconds),
                normalizedRuleIds);
            var after = GetTemporarilyDisabledRuleIdsLocked(now);
            changed = !before.SetEquals(after);
        }

        ScheduleLockoutStateNotification(rule.Id, TimeSpan.FromSeconds(lockoutDurationSeconds));

        if (changed)
        {
            WriteLog($"'{rule.Name}' temporarily revealed {normalizedRuleIds.Length} paired redeem{(normalizedRuleIds.Length == 1 ? string.Empty : "s")} for its cooldown.");
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
        var isCooldownOnlyDirectAvatarChange = IsCooldownOnlyDirectAvatarChange(rule);
        if (rule.ActionType != OscActionType.AvatarRoulet && !isCooldownOnlyDirectAvatarChange)
        {
            ReleaseActiveAvatarSwitchLockoutState(rule.Id, logRelease: false);
            return;
        }

        var cooldownSeconds = GetCooldownSeconds(rule);
        var masterAvatarSwitchRuleIds = activeConfiguration is null
            ? []
            : GetMasterAvatarSwitchRuleIds(activeConfiguration.Rules);
        if (isCooldownOnlyDirectAvatarChange)
        {
            masterAvatarSwitchRuleIds = [.. masterAvatarSwitchRuleIds.Where(ruleId => ruleId != rule.Id)];
        }

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
            WriteLog(isCooldownOnlyDirectAvatarChange
                ? $"'{rule.Name}' kept other avatar-change redeems turned off during its cooldown."
                : $"'{rule.Name}' kept avatar-switch redeems turned off during its cooldown.");
            ManagedRewardAvailabilityChanged?.Invoke();
        }
    }

    private static double GetLockoutDurationSeconds(TriggerRuleSnapshot rule)
    {
        if (rule.TemporarilyDisabledRuleIds.Count == 0)
        {
            return 0d;
        }

        var activeDurationSeconds = Math.Max(0d, rule.DurationSeconds);
        var cooldownSeconds = (double)GetCooldownSeconds(rule);
        return Math.Max(activeDurationSeconds, cooldownSeconds);
    }

    private static double GetPairingLockoutDurationSeconds(TriggerRuleSnapshot rule)
    {
        if (rule.TemporarilyDisabledRuleIds.Count == 0)
        {
            return 0d;
        }

        return (double)GetCooldownSeconds(rule);
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
        var transitionSeconds = rule.ScaleMode == AvatarScaleMode.GlitchyRandomHeight
            ? Math.Clamp(rule.GlitchyRandomHeightTransitionSeconds, 0, 30)
            : Math.Max(0, rule.SmoothTransitionSeconds);
        var activeSeconds = Math.Max(0, rule.ActiveTimeSeconds);
        var restoreTransitionSeconds = activeSeconds > 0 && rule.RestoreMode != AvatarScaleRestoreMode.None
            ? Math.Max(0, rule.SmoothTransitionSeconds)
            : 0;
        return (int)Math.Ceiling(transitionSeconds + activeSeconds + restoreTransitionSeconds);
    }

    private void ReleaseActiveRuleLockoutState(Guid sourceRuleId, bool logRelease)
    {
        var changed = false;
        ActiveRuleLockoutState? releasedLockoutState = null;
        ActiveRuleLockoutState? releasedUnlockState = null;
        CancellationTokenSource? notificationToDispose = null;

        lock (stateGate)
        {
            var now = DateTimeOffset.UtcNow;
            var hadLockout = activeRuleLockouts.TryGetValue(sourceRuleId, out releasedLockoutState);
            var hadUnlock = activeRuleUnlocks.TryGetValue(sourceRuleId, out releasedUnlockState);
            if (!hadLockout && !hadUnlock)
            {
                return;
            }

            var before = BuildTemporarilyDisabledRuleIdsLocked(GetPairingReleaseComparisonTime(now, releasedLockoutState, releasedUnlockState));
            activeRuleLockouts.Remove(sourceRuleId);
            activeRuleUnlocks.Remove(sourceRuleId);
            if (lockoutStateNotifications.Remove(sourceRuleId, out var notification))
            {
                notificationToDispose = notification;
            }
            var after = GetTemporarilyDisabledRuleIdsLocked(now);
            changed = !before.SetEquals(after);
        }

        if (notificationToDispose is not null)
        {
            notificationToDispose.Cancel();
            notificationToDispose.Dispose();
        }

        if (changed)
        {
            if (logRelease && releasedLockoutState is not null)
            {
                WriteLog($"Re-enabled {releasedLockoutState.DisabledRuleIds.Count} linked redeem{(releasedLockoutState.DisabledRuleIds.Count == 1 ? string.Empty : "s")} after '{releasedLockoutState.SourceRuleName}' finished.");
            }

            if (logRelease && releasedUnlockState is not null)
            {
                WriteLog($"Hid {releasedUnlockState.DisabledRuleIds.Count} reveal-paired redeem{(releasedUnlockState.DisabledRuleIds.Count == 1 ? string.Empty : "s")} after '{releasedUnlockState.SourceRuleName}' finished.");
            }

            ManagedRewardAvailabilityChanged?.Invoke();
        }
    }

    private static DateTimeOffset GetPairingReleaseComparisonTime(
        DateTimeOffset now,
        ActiveRuleLockoutState? releasedLockoutState,
        ActiveRuleLockoutState? releasedUnlockState)
    {
        var comparisonTime = now;
        if (releasedLockoutState is not null && releasedLockoutState.ExpiresAt <= now)
        {
            comparisonTime = releasedLockoutState.ExpiresAt.AddTicks(-1);
        }

        if (releasedUnlockState is not null && releasedUnlockState.ExpiresAt <= now)
        {
            var unlockComparisonTime = releasedUnlockState.ExpiresAt.AddTicks(-1);
            if (unlockComparisonTime < comparisonTime)
            {
                comparisonTime = unlockComparisonTime;
            }
        }

        return comparisonTime;
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
        RemoveExpiredLockoutsLocked(activeRuleUnlocks, now);
        RemoveExpiredLockoutsLocked(activeAvatarSwitchRuleLockouts, now);

        return BuildTemporarilyDisabledRuleIdsLocked(now);
    }

    private HashSet<Guid> BuildTemporarilyDisabledRuleIdsLocked(DateTimeOffset now)
    {
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

        disabledRuleIds.UnionWith(GetReversePairingHiddenRuleIdsLocked(now));

        if (IsSupporterOverrideSequenceActiveLocked(now))
        {
            disabledRuleIds.UnionWith(supporterOverrideBlockedRuleIds);
        }

        return disabledRuleIds;
    }

    private bool TryGetTemporarilyDisabledUntilLocked(Guid ruleId, DateTimeOffset now, out DateTimeOffset temporarilyDisabledUntil)
    {
        RemoveExpiredLockoutsLocked(activeRuleLockouts, now);
        RemoveExpiredLockoutsLocked(activeRuleUnlocks, now);
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

        if (IsRuleHiddenByReversePairingLocked(ruleId, now))
        {
            var nextPollAt = now.Add(ReversePairingHiddenPollDelay);
            temporarilyDisabledUntil = blockedUntil > nextPollAt ? blockedUntil : nextPollAt;
            return true;
        }

        if (blockedUntil <= now)
        {
            temporarilyDisabledUntil = default;
            return false;
        }

        temporarilyDisabledUntil = blockedUntil;
        return true;
    }

    private HashSet<Guid> GetReversePairingHiddenRuleIdsLocked(DateTimeOffset now)
    {
        if (activeConfiguration is null)
        {
            return [];
        }

        var unlockedRuleIds = new HashSet<Guid>();
        foreach (var unlockState in activeRuleUnlocks.Values)
        {
            if (unlockState.ExpiresAt <= now)
            {
                continue;
            }

            foreach (var ruleId in unlockState.DisabledRuleIds)
            {
                unlockedRuleIds.Add(ruleId);
            }
        }

        var hiddenRuleIds = new HashSet<Guid>();
        foreach (var sourceRule in activeConfiguration.Rules)
        {
            if (sourceRule.SpecialRulePairingMode != SpecialRulePairingMode.ShowPairedWhileActive)
            {
                continue;
            }

            foreach (var pairedRuleId in sourceRule.TemporarilyDisabledRuleIds)
            {
                if (pairedRuleId != Guid.Empty
                    && pairedRuleId != sourceRule.Id
                    && !unlockedRuleIds.Contains(pairedRuleId))
                {
                    hiddenRuleIds.Add(pairedRuleId);
                }
            }
        }

        return hiddenRuleIds;
    }

    private bool IsRuleHiddenByReversePairingLocked(Guid ruleId, DateTimeOffset now)
    {
        if (activeConfiguration is null)
        {
            return false;
        }

        foreach (var unlockState in activeRuleUnlocks.Values)
        {
            if (unlockState.ExpiresAt > now && unlockState.DisabledRuleIds.Contains(ruleId))
            {
                return false;
            }
        }

        return activeConfiguration.Rules.Any(sourceRule =>
            sourceRule.SpecialRulePairingMode == SpecialRulePairingMode.ShowPairedWhileActive
            && sourceRule.Id != ruleId
            && sourceRule.TemporarilyDisabledRuleIds.Contains(ruleId));
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

        var requiredScopesArray = requiredScopes.ToArray();
        var shouldRefresh = !string.IsNullOrWhiteSpace(account.RefreshToken)
            && (string.IsNullOrWhiteSpace(account.AccessToken)
                || account.AccessTokenExpiresAt is { } expiresAt
                    && expiresAt <= DateTimeOffset.UtcNow.Add(AccessTokenRefreshLeadTime));
        var refreshedThisAttempt = false;

        if (shouldRefresh)
        {
            try
            {
                account = await RefreshAccountTokenAndPersistAsync(account, accountRole, cancellationToken);
                refreshedThisAttempt = true;
            }
            catch (TwitchApiException ex) when (CanUseCachedAccountAfterTemporaryValidationFailure(account, requiredScopesArray, ex))
            {
                WriteLog($"{accountRole} Twitch token refresh was temporarily unavailable, so Crystal Relay kept using the saved valid session.");
                return account;
            }
        }

        TwitchApiClient.TokenValidationResponse? validation;
        try
        {
            validation = await twitchApiClient.ValidateTokenAsync(account.AccessToken, cancellationToken);
        }
        catch (TwitchApiException ex) when (CanUseCachedAccountAfterTemporaryValidationFailure(account, requiredScopesArray, ex))
        {
            WriteLog($"{accountRole} Twitch token validation was temporarily unavailable, so Crystal Relay kept using the saved valid session.");
            return account;
        }

        if (validation is null)
        {
            if (string.IsNullOrWhiteSpace(account.RefreshToken))
            {
                throw new InvalidOperationException($"{accountRole} OAuth token expired and no refresh token is available.");
            }

            if (refreshedThisAttempt)
            {
                throw new InvalidOperationException($"Unable to validate the refreshed {accountRole} token.");
            }

            account = await RefreshAccountTokenAndPersistAsync(account, accountRole, cancellationToken);
            try
            {
                validation = await twitchApiClient.ValidateTokenAsync(account.AccessToken, cancellationToken)
                    ?? throw new InvalidOperationException($"Unable to validate the refreshed {accountRole} token.");
            }
            catch (TwitchApiException ex) when (CanUseCachedAccountAfterTemporaryValidationFailure(account, requiredScopesArray, ex))
            {
                WriteLog($"{accountRole} Twitch token refreshed, but validation was temporarily unavailable. Crystal Relay kept the refreshed session for now.");
                return account;
            }
        }

        if (!string.Equals(validation.ClientId, activeConfiguration.TwitchClientId, StringComparison.Ordinal))
        {
            throw new InvalidOperationException($"{accountRole} OAuth token belongs to a different Twitch app.");
        }

        var missingScopes = GetMissingScopes(validation.Scopes, requiredScopesArray);

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

    private async Task<TwitchAccountSnapshot> RefreshAccountTokenAndPersistAsync(
        TwitchAccountSnapshot account,
        BridgeAccountRole accountRole,
        CancellationToken cancellationToken)
    {
        var refreshedToken = await RefreshAccountTokenAsync(account.RefreshToken, accountRole, cancellationToken);
        var refreshedAccount = account with
        {
            AccessToken = refreshedToken.AccessToken,
            RefreshToken = string.IsNullOrWhiteSpace(refreshedToken.RefreshToken)
                ? account.RefreshToken
                : refreshedToken.RefreshToken,
            AccessTokenExpiresAt = DateTimeOffset.UtcNow.AddSeconds(refreshedToken.ExpiresIn),
            SessionRenewalDueAt = DateTimeOffset.UtcNow.Add(PublicRefreshSessionWindow),
            Scopes = refreshedToken.Scope.Count > 0 ? refreshedToken.Scope : account.Scopes
        };

        if (accountRole == BridgeAccountRole.Broadcaster)
        {
            AccountUpdated?.Invoke(accountRole, refreshedAccount);
        }

        return refreshedAccount;
    }

    private async Task<TwitchApiClient.TokenExchangeResponse> RefreshAccountTokenAsync(
        string refreshToken,
        BridgeAccountRole accountRole,
        CancellationToken cancellationToken)
    {
        if (activeConfiguration is null)
        {
            throw new InvalidOperationException("Bridge settings are missing.");
        }

        try
        {
            if (accountRole == BridgeAccountRole.Broadcaster)
            {
                return await twitchApiClient.RefreshBroadcasterAccessTokenAsync(activeConfiguration.TwitchClientId, refreshToken, cancellationToken);
            }

            return await twitchApiClient.RefreshAccessTokenAsync(activeConfiguration.TwitchClientId, refreshToken, cancellationToken);
        }
        catch (TwitchApiException ex) when (ex.StatusCode is System.Net.HttpStatusCode.BadRequest or System.Net.HttpStatusCode.Unauthorized)
        {
            throw new TwitchAccountReconnectRequiredException(accountRole, ex);
        }
    }

    private static bool CanUseCachedAccountAfterTemporaryValidationFailure(
        TwitchAccountSnapshot account,
        IReadOnlyCollection<string> requiredScopes,
        TwitchApiException exception)
    {
        return IsTemporaryTokenValidationFailure(exception)
            && !string.IsNullOrWhiteSpace(account.AccessToken)
            && !string.IsNullOrWhiteSpace(account.UserId)
            && account.AccessTokenExpiresAt is { } expiresAt
            && expiresAt > DateTimeOffset.UtcNow.Add(CachedTokenValidationGraceWindow)
            && GetMissingScopes(account.Scopes, requiredScopes).Length == 0;
    }

    private static bool IsTemporaryTokenValidationFailure(TwitchApiException exception)
    {
        var statusCode = (int)exception.StatusCode;
        return exception.StatusCode == System.Net.HttpStatusCode.RequestTimeout
            || exception.StatusCode == System.Net.HttpStatusCode.TooManyRequests
            || statusCode >= 500;
    }

    private static string[] GetMissingScopes(IEnumerable<string> existingScopes, IEnumerable<string> requiredScopes)
    {
        return requiredScopes
            .Where(scope => existingScopes.All(existing => !string.Equals(existing, scope, StringComparison.OrdinalIgnoreCase)))
            .ToArray();
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

        if (string.IsNullOrWhiteSpace(configuration.Broadcaster.AccessToken)
            && string.IsNullOrWhiteSpace(configuration.Broadcaster.RefreshToken))
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
        BridgeRuntimeConfiguration configuration,
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
                avatarChangeTransitionActive,
                configuration.AvatarChangeCooldownOnlyModeEnabled),
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

    private async Task<bool> TryHandleActiveFloatBoostRewardAsync(
        RuntimeRuleIndex ruleIndex,
        BridgeIncomingEvent bridgeEvent,
        string currentAvatarId,
        bool avatarChangeTransitionActive,
        IReadOnlyCollection<Guid> temporarilyDisabledRuleIds,
        CancellationToken cancellationToken)
    {
        var normalizedRewardId = bridgeEvent.RewardId?.Trim() ?? string.Empty;
        var normalizedRewardTitle = NormalizeRewardTitle(bridgeEvent.RewardTitle);
        var normalizedCurrentAvatarId = currentAvatarId?.Trim() ?? string.Empty;
        var candidates = ruleIndex.GetActiveFloatBoostCandidates(normalizedRewardId, normalizedRewardTitle)
            .Where(rule => rule.IsEnabled
                && !temporarilyDisabledRuleIds.Contains(rule.Id)
                && AvatarRuleActivationPolicy.IsRuleActiveForCurrentAvatar(
                    rule.IsGlobalOverride,
                    rule.BelongsToMasterAvatarProfile,
                    rule.ActionType,
                    rule.AvatarChangeTargetId,
                    rule.RequiredAvatarId,
                    normalizedCurrentAvatarId,
                    avatarChangeTransitionActive))
            .ToArray();
        if (candidates.Length == 0)
        {
            return false;
        }

        var parentRule = candidates.FirstOrDefault(rule =>
        {
            lock (stateGate)
            {
                return activeFloatRedeemSessions.ContainsKey(rule.Id);
            }
        });

        if (parentRule is null)
        {
            WriteLog($"Ignored active boost reward '{bridgeEvent.RewardTitle}' because its float redeem is not active.");
            return true;
        }

        await ApplyActiveFloatBoostRewardAsync(parentRule, bridgeEvent, cancellationToken);
        return true;
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
        var forceMovementRules = globalRules
            .Where(IsBitsForceMovementRule)
            .Where(rule => bridgeEvent.Amount >= Math.Max(1, rule.MinimumAmount))
            .Where(rule => rule.BitsKeywordEnabled && !string.IsNullOrWhiteSpace(rule.SupporterKeywordText))
            .ToArray();

        var choiceText = ExtractBitsOutfitChoiceText(bridgeEvent.MessageText);
        string? noOutfitMatchDiagnostic = null;
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

                if (match.IsAmbiguous)
                {
                    WriteLog(match.Diagnostic ?? $"Bits outfit cheer text '{choiceText}' was too close to multiple outfits. Crystal Relay did not guess.");
                    return [];
                }

                noOutfitMatchDiagnostic = match.Diagnostic;
            }

            if (forceMovementRules.Length > 0)
            {
                var match = FindBitsForceMovementKeywordMatch(forceMovementRules, choiceText);
                if (match.Rule is not null)
                {
                    WriteLog($"Matched Bits force movement '{match.Rule.Name}' from cheer text '{choiceText}' using {match.MatchKind} matching ({match.Score:P0}).");
                    return [match.Rule];
                }

                if (match.IsAmbiguous)
                {
                    WriteLog(match.Diagnostic ?? $"Bits force movement cheer text '{choiceText}' was too close to multiple movement words. Crystal Relay did not guess.");
                    return [];
                }

                if (!string.IsNullOrWhiteSpace(match.Diagnostic))
                {
                    WriteLog(match.Diagnostic);
                }
            }
            else if (!string.IsNullOrWhiteSpace(noOutfitMatchDiagnostic))
            {
                WriteLog(noOutfitMatchDiagnostic);
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
                .Where(rule => !IsBitsForceMovementRule(rule))
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
        IsAvatarChangeOverrideRule(rule) || IsBitsForceMovementRule(rule);

    private static bool IsAvatarChangeOverrideRule(TriggerRuleSnapshot rule) =>
        rule.ActionType is OscActionType.AvatarChange or OscActionType.AvatarRoulet;

    private static TriggerRuleSnapshot[] SelectExactChannelPointMatch(
        RuntimeRuleIndex ruleIndex,
        string? rewardId,
        string? rewardTitle,
        string? rewardUserInput,
        string currentAvatarId,
        IReadOnlyCollection<Guid> temporarilyDisabledRuleIds,
        bool avatarChangeTransitionActive,
        bool avatarChangeCooldownOnlyModeEnabled)
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
            avatarChangeTransitionActive,
            avatarChangeCooldownOnlyModeEnabled);

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
        BridgeRuntimeConfiguration configuration,
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
                    avatarChangeTransitionActive,
                    configuration.AvatarChangeCooldownOnlyModeEnabled))
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
                $"Bits outfit cheer text '{choiceText}' matched more than one outfit exactly. Rename one of these outfits so Crystal Relay can choose safely: {DescribeBitsOutfitCandidates(exactMatches)}.",
                true);
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
                $"Bits outfit cheer text '{choiceText}' matched more than one outfit after removing spaces and punctuation. Rename one of these outfits so Crystal Relay can choose safely: {DescribeBitsOutfitCandidates(compactMatches)}.",
                true);
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
                $"Bits outfit cheer text '{choiceText}' was too close to multiple outfits. Crystal Relay did not guess. Close matches: {DescribeBitsOutfitCandidates([best.Candidate, second.Candidate])}.",
                true);
        }

        return new BitsOutfitNameMatch(best.Candidate.Rule, "fuzzy", best.Score, null);
    }

    private static BitsOutfitNameMatch FindBitsForceMovementKeywordMatch(
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
            .Where(rule => rule.BitsKeywordEnabled && !string.IsNullOrWhiteSpace(rule.SupporterKeywordText))
            .Select(rule => new BitsOutfitNameCandidate(
                rule,
                rule.SupporterKeywordText.Trim(),
                NormalizeBitsOutfitPhrase(rule.SupporterKeywordText),
                NormalizeBitsOutfitCompact(rule.SupporterKeywordText)))
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
                $"Bits force movement cheer text '{choiceText}' matched more than one movement word exactly. Rename one of these words so Crystal Relay can choose safely: {DescribeBitsOutfitCandidates(exactMatches)}.",
                true);
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
                $"Bits force movement cheer text '{choiceText}' matched more than one movement word after removing spaces and punctuation. Rename one of these words so Crystal Relay can choose safely: {DescribeBitsOutfitCandidates(compactMatches)}.",
                true);
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
                $"Bits force movement cheer text '{choiceText}' did not confidently match any configured movement word.");
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
                $"Bits force movement cheer text '{choiceText}' was too close to multiple movement words. Crystal Relay did not guess. Close matches: {DescribeBitsOutfitCandidates([best.Candidate, second.Candidate])}.",
                true);
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
        bool avatarChangeTransitionActive,
        bool avatarChangeCooldownOnlyModeEnabled)
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
                    avatarChangeTransitionActive,
                    avatarChangeCooldownOnlyModeEnabled))
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
        && rule.SharedRewardChoiceNumber > 0
        && (rule.ActionType != OscActionType.SetTrigger || rule.UsesSharedNumberedOutfitReward);

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
                avatarChangeTransitionActive,
                avatarChangeCooldownOnlyModeEnabled: false)
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

    private bool IsInAvatarChangeGracePeriod()
    {
        lock (stateGate)
        {
            return (DateTimeOffset.UtcNow - lastAvatarChangeAt) < AvatarChangeGracePeriod;
        }
    }

    private SharedReturnAvatarSnapshot GetSharedReturnAvatarSnapshot()
    {
        lock (stateGate)
        {
            return new SharedReturnAvatarSnapshot(currentSharedReturnAvatarId, currentSharedReturnAvatarName);
        }
    }

    private bool hasAttemptedResume;

    public async Task TryResumePendingActivitiesAsync()
    {
        if (hasAttemptedResume)
        {
            return;
        }

        if (!HasDiscoveredVrChat)
        {
            WriteLog("Activity resume skipped: OSC not discovered yet.");
            return;
        }

        var currentAvatarId = GetCurrentVrChatAvatarId();
        if (string.IsNullOrWhiteSpace(currentAvatarId))
        {
            WriteLog("Activity resume skipped: current avatar ID is empty.");
            return;
        }

        if (!activityResumeService.HasPendingResume)
        {
            WriteLog("Activity resume skipped: no pending resume file found.");
            hasAttemptedResume = true;
            return;
        }

        if (!activityResumeService.IsPendingForAvatar(currentAvatarId))
        {
            WriteLog($"Activity resume skipped: current avatar '{currentAvatarId}' does not match saved avatar.");
            return;
        }

        hasAttemptedResume = true;
        var pendingActivities = activityResumeService.GetPendingActivities();
        if (pendingActivities.Count == 0)
        {
            return;
        }

        WriteLog("Resuming saved activities...");
        foreach (var activity in pendingActivities)
        {
            try
            {
                await ResumeActivityAsync(activity);
            }
            catch (Exception ex)
            {
                WriteLog($"Failed to resume activity {activity.Type} for rule {activity.RuleId}: {ex.Message}");
            }
        }

        WriteLog("Saved activities resumed.");
    }

    private async Task ResumeActivityAsync(ResumeActivity activity)
    {
        if (activeConfiguration is null)
        {
            return;
        }

        var cancellationToken = runtimeCancellation?.Token ?? CancellationToken.None;

        switch (activity.Type)
        {
            case ResumeActivityType.AvatarScale:
                {
                    var rule = activeConfiguration.AvatarScaleRules.FirstOrDefault(r => r.Id == activity.RuleId);
                    if (rule is null)
                    {
                        return;
                    }

                    var currentHeight = await TryGetCurrentAvatarHeightAsync(cancellationToken);
                    var savedHeight = activity.CurrentValue ?? 0;
                    var heightMatches = currentHeight.HasValue
                        && Math.Abs(currentHeight.Value - savedHeight) < 0.01;

                    if (heightMatches)
                    {
                        var effectDurationSeconds = GetAvatarScaleEffectDurationSeconds(rule);
                        var activeWindowSeconds = effectDurationSeconds;
                        var heightSessionId = StartAvatarScaleHeightSession(
                            rule.Id,
                            rule.Name,
                            rule.RestoreHeightMeters,
                            savedHeight,
                            activeWindowSeconds);
                        if (heightSessionId != Guid.Empty)
                        {
                            ScheduleAvatarScaleHeightSessionEnd(
                                rule.Id,
                                heightSessionId,
                                TimeSpan.FromSeconds(Math.Max(0.5, activeWindowSeconds)),
                                cancellationToken);
                        }

                        ScheduleAvatarScaleRestoreSequence(rule, isTest: false, savedHeight);
                        WriteLog($"Skipped OSC send for '{rule.Name}' during resume ΓÇö avatar is already at {savedHeight:0.###}m. Carryover timer rearmed for {rule.ActiveTimeSeconds}s.");
                    }
                    else
                    {
                        var incomingEvent = UniversalIncomingEvent.Test;
                        await ExecuteAvatarScaleRuleAsync(
                            rule,
                            incomingEvent,
                            isTest: false,
                            cancellationToken,
                            isResuming: true);
                    }

                    break;
                }

            case ResumeActivityType.Movement:
                {
                    var rule = activeConfiguration.Rules.FirstOrDefault(r => r.Id == activity.RuleId);
                    if (rule is null)
                    {
                        return;
                    }

                    await ExecuteRuleActionAsync(
                        rule,
                        null,
                        cancellationToken,
                        isTest: false,
                        queuedReplay: false,
                        allowLaneQueue: false,
                        isResuming: true);
                    break;
                }

            case ResumeActivityType.AvatarChange:
                {
                    var rule = activeConfiguration.Rules.FirstOrDefault(r => r.Id == activity.RuleId);
                    if (rule is null)
                    {
                        return;
                    }

                    await ExecuteRuleActionAsync(
                        rule,
                        null,
                        cancellationToken,
                        isTest: false,
                        queuedReplay: false,
                        allowLaneQueue: false,
                        isResuming: true);
                    break;
                }
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
            lastAvatarChangeAt = DateTimeOffset.UtcNow;
            ResumePendingAvatarScopedResetsForCurrentAvatar(normalizedAvatarId);
            _ = TryResumePendingActivitiesAsync();
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
        long sequenceId;
        lock (stateGate)
        {
            sequenceId = ++nextAvatarScaleAvatarChangeSequenceId;
        }

        var cancellationToken = runtimeCancellation?.Token ?? CancellationToken.None;
        _ = Task.Run(
            () => HandleAvatarScaleAvatarChangedAsync(
                sequenceId,
                previousAvatarId,
                newAvatarId,
                previousAvatarHeight,
                scaleCarryoverMode,
                cancellationToken),
            CancellationToken.None);
    }

    private async Task HandleAvatarScaleAvatarChangedAsync(
        long sequenceId,
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
                RetargetPausedDevAvatarScaleTimerForAvatarChange(newAvatarId);
                WriteLog($"Avatar scale carryover from '{carryover.SourceRuleName}' is waiting {AvatarScaleCarryoverInitialSendDelay.TotalSeconds:0.#}s for the new avatar to finish loading before applying {carryover.CarriedHeightMeters:0.###}m.");
                await Task.Delay(AvatarScaleCarryoverInitialSendDelay, cancellationToken);
                for (var attempt = 1; attempt <= AvatarScaleCarryoverApplyAttemptCount; attempt++)
                {
                    if (!IsAvatarScaleAvatarChangeHandlingCurrent(sequenceId))
                    {
                        WriteLog($"Avatar scale carryover from '{carryover.SourceRuleName}' stopped because a newer avatar swap started.");
                        return;
                    }

                    if (!string.Equals(GetCurrentVrChatAvatarId(), newAvatarId, StringComparison.Ordinal))
                    {
                        WriteLog($"Avatar scale carryover from '{carryover.SourceRuleName}' stopped because the current avatar changed before the height could finish applying.");
                        return;
                    }

                    if (!IsAvatarScaleCarryoverStillActive(carryover))
                    {
                        WriteLog($"Avatar scale carryover from '{carryover.SourceRuleName}' stopped because the active scale timer ended.");
                        return;
                    }

                    await SendAvatarHeightValueAsync(carryover.CarriedHeightMeters, cancellationToken);
                    WriteLog($"Applied active avatar scale height {carryover.CarriedHeightMeters:0.###}m to the new avatar from '{carryover.SourceRuleName}' ({attempt}/{AvatarScaleCarryoverApplyAttemptCount}) with {DescribeDuration((carryover.ActiveUntil - DateTimeOffset.UtcNow).TotalSeconds)} remaining.");

                    if (attempt < AvatarScaleCarryoverApplyAttemptCount)
                    {
                        await Task.Delay(AvatarScaleCarryoverApplyInterval, cancellationToken);
                    }
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

    private bool IsAvatarScaleAvatarChangeHandlingCurrent(long sequenceId)
    {
        lock (stateGate)
        {
            return nextAvatarScaleAvatarChangeSequenceId == sequenceId;
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
            PruneExpiredAvatarScaleCarryoverLocked(now);
            if (activeAvatarScaleCarryover is { } activeCarryover)
            {
                var fallbackPreviousAvatarId = previousAvatarId?.Trim() ?? string.Empty;
                if (!string.IsNullOrWhiteSpace(fallbackPreviousAvatarId)
                    && !pendingAvatarScaleHeightRestores.ContainsKey(fallbackPreviousAvatarId))
                {
                    pendingAvatarScaleHeightRestores[fallbackPreviousAvatarId] = new PendingAvatarScaleHeightRestoreState(
                        activeCarryover.RestoreHeightMeters,
                        activeCarryover.ActiveUntil,
                        activeCarryover.SourceRuleName);
                }

                return new AvatarScaleCarryoverSnapshot(
                    activeCarryover.SourceRuleId,
                    activeCarryover.SourceSessionId,
                    activeCarryover.RestoreSequenceId,
                    activeCarryover.CarryoverId,
                    activeCarryover.SourceRuleName,
                    activeCarryover.CarriedHeightMeters,
                    activeCarryover.RestoreHeightMeters,
                    activeCarryover.ActiveUntil);
            }

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
                if (activeAvatarScaleRestoreSequence is null
                    || activeAvatarScaleRestoreSequence.ActiveUntil <= now)
                {
                    if (pausedDevAvatarScaleTimerSnapshot is not { } pausedSnapshot
                        || pausedSnapshot.ActiveUntil <= now)
                    {
                        return null;
                    }

                    var fallbackPreviousAvatarId = previousAvatarId?.Trim() ?? string.Empty;
                    if (!string.IsNullOrWhiteSpace(fallbackPreviousAvatarId)
                        && !pendingAvatarScaleHeightRestores.ContainsKey(fallbackPreviousAvatarId))
                    {
                        pendingAvatarScaleHeightRestores[fallbackPreviousAvatarId] = new PendingAvatarScaleHeightRestoreState(
                            pausedSnapshot.RestoreHeightMeters,
                            pausedSnapshot.ActiveUntil,
                            pausedSnapshot.SourceRuleName);
                    }

                    return new AvatarScaleCarryoverSnapshot(
                        pausedSnapshot.RuleId,
                        pausedSnapshot.SessionId,
                        pausedSnapshot.RestoreSequenceId,
                        Guid.Empty,
                        pausedSnapshot.SourceRuleName,
                        pausedSnapshot.CarriedHeightMeters,
                        pausedSnapshot.RestoreHeightMeters,
                        pausedSnapshot.ActiveUntil);
                }

                {
                    var activeSequence = activeAvatarScaleRestoreSequence;
                    if (activeSequence is null)
                    {
                        return null;
                    }

                    var fallbackPreviousAvatarId = previousAvatarId?.Trim() ?? string.Empty;
                    if (!string.IsNullOrWhiteSpace(fallbackPreviousAvatarId)
                        && !pendingAvatarScaleHeightRestores.ContainsKey(fallbackPreviousAvatarId))
                    {
                        pendingAvatarScaleHeightRestores[fallbackPreviousAvatarId] = new PendingAvatarScaleHeightRestoreState(
                            activeSequence.RestoreHeightMeters,
                            activeSequence.ActiveUntil,
                            activeSequence.SourceRuleName);
                    }

                    return new AvatarScaleCarryoverSnapshot(
                        Guid.Empty,
                        Guid.Empty,
                        activeSequence.SequenceId,
                        Guid.Empty,
                        activeSequence.SourceRuleName,
                        activeSequence.CarriedHeightMeters,
                        activeSequence.RestoreHeightMeters,
                        activeSequence.ActiveUntil);
                }
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
                activeAvatarScaleRestoreSequence?.SequenceId ?? 0,
                Guid.Empty,
                latestSession.RuleName,
                latestSession.CarriedHeightMeters,
                latestSession.RestoreHeightMeters ?? 1.6,
                latestSession.ActiveUntil);
        }
    }

    private bool IsAvatarScaleCarryoverStillActive(AvatarScaleCarryoverSnapshot carryover)
    {
        var now = DateTimeOffset.UtcNow;
        if (carryover.ActiveUntil <= now)
        {
            return false;
        }

        lock (stateGate)
        {
            PruneExpiredAvatarScaleCarryoverLocked(now);
            if (carryover.CarryoverId != Guid.Empty
                && activeAvatarScaleCarryover?.CarryoverId == carryover.CarryoverId
                && activeAvatarScaleCarryover.ActiveUntil > now)
            {
                return true;
            }

            PruneExpiredAvatarScaleHeightSessionsLocked(now);
            if (carryover.SessionId != Guid.Empty
                && activeAvatarScaleHeightSessions.TryGetValue(carryover.SourceRuleId, out var session)
                && session.SessionId == carryover.SessionId
                && session.ActiveUntil > now)
            {
                return true;
            }

            if (carryover.RestoreSequenceId > 0
                && pausedDevAvatarScaleTimerSnapshot is { } pausedSnapshot
                && pausedSnapshot.RestoreSequenceId == carryover.RestoreSequenceId
                && pausedSnapshot.ActiveUntil > now)
            {
                return true;
            }

            return carryover.RestoreSequenceId > 0
                && activeAvatarScaleRestoreSequence?.SequenceId == carryover.RestoreSequenceId
                && activeAvatarScaleRestoreSequence.ActiveUntil > now;
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
        ActiveAvatarScaleCarryoverState? retargetedCarryover = null;
        lock (stateGate)
        {
            var now = DateTimeOffset.UtcNow;
            if (activeAvatarScaleCarryover is not null
                && activeAvatarScaleCarryover.ActiveUntil > now
                && !string.Equals(activeAvatarScaleCarryover.AvatarId, normalizedAvatarId, StringComparison.Ordinal))
            {
                activeAvatarScaleCarryover = activeAvatarScaleCarryover with
                {
                    AvatarId = normalizedAvatarId
                };
                retargetedCarryover = activeAvatarScaleCarryover;
            }

            if (activeAvatarScaleRestoreSequence is null
                || activeAvatarScaleRestoreSequence.ActiveUntil <= now
                || string.Equals(activeAvatarScaleRestoreSequence.AvatarId, normalizedAvatarId, StringComparison.Ordinal))
            {
                retargetedSequence = null;
            }
            else
            {
                activeAvatarScaleRestoreSequence = activeAvatarScaleRestoreSequence with
                {
                    AvatarId = normalizedAvatarId
                };
                retargetedSequence = activeAvatarScaleRestoreSequence;
            }
        }

        if (retargetedCarryover is not null)
        {
            WriteLog($"Active avatar scale carryover from '{retargetedCarryover.SourceRuleName}' is now tracking the current avatar.");
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
        var sourceRuleName = string.IsNullOrWhiteSpace(ruleName) ? "Avatar Scale" : ruleName;
        lock (stateGate)
        {
            activeAvatarScaleHeightSessions[ruleId] = new ActiveAvatarScaleHeightSessionState(
                ruleId,
                sessionId,
                sourceRuleName,
                avatarId,
                restoreHeightMeters,
                carriedHeightMeters,
                activeUntil);
            SetActiveAvatarScaleCarryoverLocked(
                ruleId,
                sessionId,
                activeAvatarScaleRestoreSequence?.SequenceId ?? 0,
                sourceRuleName,
                avatarId,
                carriedHeightMeters,
                restoreHeightMeters ?? 1.6,
                activeUntil,
                restoreToPaidGrowthIfActive: true);
        }

        WriteLog($"Recorded active avatar scale carryover for '{sourceRuleName}' at {carriedHeightMeters:0.###}m for {DescribeDuration((activeUntil - DateTimeOffset.UtcNow).TotalSeconds)}.");
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
                if (activeAvatarScaleCarryover?.SourceSessionId == sessionId
                    && activeAvatarScaleCarryover.RestoreSequenceId <= 0)
                {
                    activeAvatarScaleCarryover = null;
                }
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

        PruneExpiredAvatarScaleCarryoverLocked(now);
    }

    private void PruneExpiredAvatarScaleCarryoverLocked(DateTimeOffset now)
    {
        if (activeAvatarScaleCarryover?.ActiveUntil <= now)
        {
            activeAvatarScaleCarryover = null;
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
            var now = DateTimeOffset.UtcNow;
            PruneExpiredAvatarScaleHeightSessionsLocked(now);
            if (!pendingAvatarScaleHeightRestores.TryGetValue(normalizedAvatarId, out pendingRestore))
            {
                return;
            }

            if (activeAvatarScaleHeightSessions.Count > 0
                || activeAvatarScaleRestoreSequence?.ActiveUntil > now
                || pendingRestore.SourceActiveUntil > now)
            {
                WriteLog($"Deferred pending avatar scale restore from '{pendingRestore.SourceRuleName}' because Avatar Scaling active time is still running.");
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

            "channel.custom_power_up_redemption.add" => new BridgeIncomingEvent(
                TwitchTriggerType.PowerUp,
                GetString(eventData, "user_name") ?? "Viewer",
                GetPowerUpBitsCost(eventData),
                GetString(eventData, "custom_power_up", "id")
                    ?? GetString(eventData, "power_up", "id")
                    ?? GetString(eventData, "id"),
                GetString(eventData, "custom_power_up", "title")
                    ?? GetString(eventData, "power_up", "title")
                    ?? GetString(eventData, "title"),
                "Power Up",
                false,
                string.Empty,
                GetString(eventData, "user_id") ?? string.Empty,
                GetString(eventData, "user_login") ?? string.Empty,
                [],
                false,
                false)
            {
                RewardUserInput = GetString(eventData, "user_input") ?? string.Empty,
                MessageText = GetString(eventData, "user_input") ?? string.Empty
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
                false)
            {
                SubscriptionTier = GetString(eventData, "tier") ?? string.Empty
            },

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
                false)
            {
                SubscriptionTier = GetString(eventData, "tier") ?? string.Empty
            },

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
                false)
            {
                SubscriptionTier = GetString(eventData, "tier") ?? string.Empty
            },

            _ => null
        };
    }

    private static bool TryBuildRewardFireSaleContribution(
        BridgeIncomingEvent incomingEvent,
        out RewardFireSaleContribution contribution)
    {
        contribution = default!;
        if (incomingEvent.IsChatCommandTrigger)
        {
            return false;
        }

        if (incomingEvent.TriggerType is TwitchTriggerType.Bits or TwitchTriggerType.PowerUp && incomingEvent.Amount > 0)
        {
            contribution = new RewardFireSaleContribution(
                RewardFireSaleContributionType.Bits,
                incomingEvent.Amount,
                incomingEvent.TriggerType == TwitchTriggerType.PowerUp ? incomingEvent.RewardId : null,
                incomingEvent.TriggerType == TwitchTriggerType.PowerUp ? incomingEvent.RewardTitle : null,
                incomingEvent.UserDisplayName);
            return true;
        }

        if (incomingEvent.TriggerType == TwitchTriggerType.ChannelPoints)
        {
            var rewardId = incomingEvent.RewardId?.Trim() ?? string.Empty;
            var rewardTitle = incomingEvent.RewardTitle?.Trim() ?? string.Empty;
            if (string.IsNullOrWhiteSpace(rewardId) && string.IsNullOrWhiteSpace(rewardTitle))
            {
                return false;
            }

            contribution = new RewardFireSaleContribution(
                RewardFireSaleContributionType.ManagedReward,
                Math.Max(0, incomingEvent.Amount),
                rewardId,
                rewardTitle,
                incomingEvent.UserDisplayName);
            return true;
        }

        return false;
    }

    private static int GetPowerUpBitsCost(JsonElement eventData)
    {
        var bits = GetInt(eventData, "custom_power_up", "bits");
        if (bits > 0)
        {
            return bits;
        }

        bits = GetInt(eventData, "custom_power_up", "cost");
        if (bits > 0)
        {
            return bits;
        }

        bits = GetInt(eventData, "power_up", "bits");
        if (bits > 0)
        {
            return bits;
        }

        bits = GetInt(eventData, "bits");
        return bits > 0 ? bits : Math.Max(0, GetInt(eventData, "cost"));
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
                GetString(eventData, "message") ?? ExtractChatMessageText(eventData),
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
            DateTimeOffset.Now)
        {
            MessageId = GetString(eventData, "message_id") ?? string.Empty,
            MessageType = GetString(eventData, "message_type") ?? string.Empty,
            SourceBroadcasterUserId = GetString(eventData, "source_broadcaster_user_id") ?? string.Empty,
            SourceBroadcasterUserLogin = GetString(eventData, "source_broadcaster_user_login") ?? string.Empty,
            SourceBroadcasterUserName = GetString(eventData, "source_broadcaster_user_name") ?? string.Empty,
            SourceMessageId = GetString(eventData, "source_message_id") ?? string.Empty,
            IsSourceOnly = GetBoolean(eventData, "is_source_only")
        };
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
                parsedFragments.Add(new ParsedBridgeChatFragment(BridgeChatFragmentKind.Text, text, string.Empty, string.Empty));
                continue;
            }

            var emoteId = GetString(fragmentNode, "emote", "id") ?? string.Empty;
            if (string.IsNullOrWhiteSpace(emoteId))
            {
                parsedFragments.Add(new ParsedBridgeChatFragment(BridgeChatFragmentKind.Text, text, string.Empty, string.Empty));
                continue;
            }

            var emoteSetId = GetString(fragmentNode, "emote", "emote_set_id") ?? string.Empty;
            nativeEmoteFragmentCount++;
            parsedFragments.Add(new ParsedBridgeChatFragment(BridgeChatFragmentKind.Emote, text, emoteId, emoteSetId));
        }

        var nativeEmoteSetIds = parsedFragments
            .Where(fragment => fragment.Kind == BridgeChatFragmentKind.Emote
                && !string.IsNullOrWhiteSpace(fragment.EmoteSetId))
            .Select(fragment => fragment.EmoteSetId)
            .Distinct(StringComparer.Ordinal)
            .ToArray();
        if (nativeEmoteSetIds.Length > 0)
        {
            await EnsureChatEmoteSetsCachedAsync(nativeEmoteSetIds, cancellationToken);
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

            var imageUrl = ResolveChatEmoteImageUrl(fragment.EmoteId);
            if (string.IsNullOrWhiteSpace(imageUrl))
            {
                imageUrl = BuildTwitchStaticEmoteImageUrl(fragment.EmoteId);
            }

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

    private static BridgeChatMessage? ParseChannelPointRedemptionChatboxMessage(EventSubNotification notification)
    {
        if (!string.Equals(notification.SubscriptionType, "channel.channel_points_custom_reward_redemption.add", StringComparison.Ordinal))
        {
            return null;
        }

        var eventData = notification.EventData;
        var rewardTitle = GetString(eventData, "reward", "title")?.Trim() ?? string.Empty;
        var rewardInput = GetString(eventData, "user_input")?.Trim() ?? string.Empty;
        if (string.IsNullOrWhiteSpace(rewardTitle))
        {
            rewardTitle = T("Channel Point Reward");
        }

        var userDisplayName = GetString(eventData, "user_name") ?? "Viewer";
        var userId = GetString(eventData, "user_id") ?? string.Empty;
        var userLogin = GetString(eventData, "user_login") ?? string.Empty;
        var rewardCost = Math.Max(0, GetInt(eventData, "reward", "cost"));
        var rewardId = GetString(eventData, "reward", "id") ?? string.Empty;
        var messageText = string.IsNullOrWhiteSpace(rewardInput)
            ? rewardTitle
            : $"{rewardTitle}: {rewardInput}";

        return new BridgeChatMessage(
            userDisplayName,
            userLogin,
            userId,
            messageText,
            string.Empty,
            [],
            [],
            [],
            DateTimeOffset.Now)
        {
            Kind = BridgeChatMessageKind.ChannelPointRedemption,
            RewardId = rewardId,
            RewardTitle = rewardTitle,
            RewardCost = rewardCost,
            RewardUserInput = rewardInput
        };
    }

    private static BridgeChatMessage? ParseSupportEventChatboxMessage(EventSubNotification notification)
    {
        var eventData = notification.EventData;
        return notification.SubscriptionType switch
        {
            "channel.cheer" => BuildSupportChatboxMessage(
                BridgeChatMessageKind.BitsCheer,
                GetBoolean(eventData, "is_anonymous") ? "Anonymous" : GetString(eventData, "user_name") ?? "Viewer",
                GetString(eventData, "user_login") ?? string.Empty,
                GetString(eventData, "user_id") ?? string.Empty,
                Math.Max(1, GetInt(eventData, "bits")),
                string.Empty,
                0,
                GetString(eventData, "message") ?? ExtractChatMessageText(eventData)),

            "channel.subscribe" when !GetBoolean(eventData, "is_gift") => BuildSupportChatboxMessage(
                BridgeChatMessageKind.Subscription,
                GetString(eventData, "user_name") ?? "Subscriber",
                GetString(eventData, "user_login") ?? string.Empty,
                GetString(eventData, "user_id") ?? string.Empty,
                1,
                GetString(eventData, "tier") ?? string.Empty,
                0,
                string.Empty),

            "channel.subscription.message" => BuildSupportChatboxMessage(
                BridgeChatMessageKind.Resubscription,
                GetString(eventData, "user_name") ?? "Subscriber",
                GetString(eventData, "user_login") ?? string.Empty,
                GetString(eventData, "user_id") ?? string.Empty,
                1,
                GetString(eventData, "tier") ?? string.Empty,
                Math.Max(0, Math.Max(GetInt(eventData, "cumulative_months"), GetInt(eventData, "duration_months"))),
                ExtractChatMessageText(eventData)),

            "channel.subscription.gift" => BuildSupportChatboxMessage(
                BridgeChatMessageKind.GiftSubscription,
                GetBoolean(eventData, "is_anonymous") ? "Anonymous" : GetString(eventData, "user_name") ?? "Gifter",
                GetString(eventData, "user_login") ?? string.Empty,
                GetString(eventData, "user_id") ?? string.Empty,
                Math.Max(1, GetInt(eventData, "total")),
                GetString(eventData, "tier") ?? string.Empty,
                0,
                string.Empty),

            "channel.raid" => BuildSupportChatboxMessage(
                BridgeChatMessageKind.Raid,
                GetString(eventData, "from_broadcaster_user_name") ?? "Raider",
                GetString(eventData, "from_broadcaster_user_login") ?? string.Empty,
                GetString(eventData, "from_broadcaster_user_id") ?? string.Empty,
                Math.Max(1, GetInt(eventData, "viewers")),
                string.Empty,
                0,
                string.Empty),

            _ => null
        };
    }

    private static BridgeChatActivity? ParseChatActivityNotification(EventSubNotification notification)
    {
        var eventData = notification.EventData;
        var now = DateTimeOffset.Now;

        if (string.Equals(notification.SubscriptionType, "channel.chat.message_delete", StringComparison.Ordinal))
        {
            var displayName = GetString(eventData, "target_user_name") ?? "Viewer";
            return new BridgeChatActivity(
                BridgeChatActivityKind.MessageDeleted,
                TF("Deleted a message from {0}.", displayName),
                now)
            {
                TargetUserDisplayName = displayName,
                TargetUserLogin = GetString(eventData, "target_user_login") ?? string.Empty,
                TargetUserId = GetString(eventData, "target_user_id") ?? string.Empty,
                MessageId = GetString(eventData, "message_id") ?? string.Empty
            };
        }

        if (string.Equals(notification.SubscriptionType, "channel.chat.clear_user_messages", StringComparison.Ordinal))
        {
            var displayName = GetString(eventData, "target_user_name") ?? "Viewer";
            return new BridgeChatActivity(
                BridgeChatActivityKind.UserMessagesCleared,
                TF("Cleared recent chat from {0}.", displayName),
                now)
            {
                TargetUserDisplayName = displayName,
                TargetUserLogin = GetString(eventData, "target_user_login") ?? string.Empty,
                TargetUserId = GetString(eventData, "target_user_id") ?? string.Empty
            };
        }

        if (string.Equals(notification.SubscriptionType, "channel.chat.clear", StringComparison.Ordinal))
        {
            return new BridgeChatActivity(
                BridgeChatActivityKind.ChatCleared,
                T("Chat was cleared by a moderator."),
                now);
        }

        if (string.Equals(notification.SubscriptionType, "channel.follow", StringComparison.Ordinal))
        {
            var displayName = GetString(eventData, "user_name") ?? "Follower";
            return new BridgeChatActivity(
                BridgeChatActivityKind.Follow,
                TF("{0} followed.", displayName),
                now)
            {
                TargetUserDisplayName = displayName,
                TargetUserLogin = GetString(eventData, "user_login") ?? string.Empty,
                TargetUserId = GetString(eventData, "user_id") ?? string.Empty
            };
        }

        if (string.Equals(notification.SubscriptionType, "channel.suspicious_user.update", StringComparison.Ordinal))
        {
            var displayName = GetString(eventData, "user_name") ?? "Viewer";
            var status = NormalizeSuspiciousStatus(GetString(eventData, "low_trust_status"));
            return new BridgeChatActivity(
                BridgeChatActivityKind.SuspiciousUserUpdated,
                FormatSuspiciousStatusActivity(displayName, status),
                now)
            {
                TargetUserDisplayName = displayName,
                TargetUserLogin = GetString(eventData, "user_login") ?? string.Empty,
                TargetUserId = GetString(eventData, "user_id") ?? string.Empty,
                SuspiciousStatus = status
            };
        }

        if (string.Equals(notification.SubscriptionType, "channel.suspicious_user.message", StringComparison.Ordinal))
        {
            var displayName = GetString(eventData, "user_name") ?? "Viewer";
            var status = NormalizeSuspiciousStatus(GetString(eventData, "low_trust_status"));
            return new BridgeChatActivity(
                BridgeChatActivityKind.SuspiciousUserMessage,
                TF("Suspicious chatter message from {0}.", displayName),
                now)
            {
                TargetUserDisplayName = displayName,
                TargetUserLogin = GetString(eventData, "user_login") ?? string.Empty,
                TargetUserId = GetString(eventData, "user_id") ?? string.Empty,
                SuspiciousStatus = status
            };
        }

        return null;
    }

    private static string NormalizeSuspiciousStatus(string? status)
    {
        var normalized = status?.Trim() ?? string.Empty;
        return normalized switch
        {
            _ when normalized.Equals("ACTIVE_MONITORING", StringComparison.OrdinalIgnoreCase)
                || normalized.Equals("active_monitoring", StringComparison.OrdinalIgnoreCase) => "ACTIVE_MONITORING",
            _ when normalized.Equals("RESTRICTED", StringComparison.OrdinalIgnoreCase)
                || normalized.Equals("restricted", StringComparison.OrdinalIgnoreCase) => "RESTRICTED",
            _ when normalized.Equals("NO_TREATMENT", StringComparison.OrdinalIgnoreCase)
                || normalized.Equals("no_treatment", StringComparison.OrdinalIgnoreCase) => "NO_TREATMENT",
            _ => normalized.ToUpperInvariant()
        };
    }

    private static string FormatSuspiciousStatusActivity(string displayName, string status) => status switch
    {
        "ACTIVE_MONITORING" => TF("Marked {0} for suspicious-user monitoring.", displayName),
        "RESTRICTED" => TF("Restricted {0} through Twitch suspicious-user tools.", displayName),
        "NO_TREATMENT" => TF("Cleared suspicious-user status for {0}.", displayName),
        _ => TF("Updated suspicious-user status for {0}.", displayName)
    };

    private static BridgeChatMessage BuildSupportChatboxMessage(
        BridgeChatMessageKind kind,
        string userDisplayName,
        string userLogin,
        string userId,
        int amount,
        string tier,
        int months,
        string supportMessage)
    {
        var normalizedUserDisplayName = string.IsNullOrWhiteSpace(userDisplayName) ? "Viewer" : userDisplayName.Trim();
        var normalizedAmount = Math.Max(0, amount);
        var normalizedSupportMessage = supportMessage?.Trim() ?? string.Empty;
        var messageText = kind switch
        {
            BridgeChatMessageKind.BitsCheer => TF("{0} cheered {1:N0} Bits", normalizedUserDisplayName, normalizedAmount),
            BridgeChatMessageKind.Subscription => TF("{0} subscribed", normalizedUserDisplayName),
            BridgeChatMessageKind.Resubscription => TF("{0} resubbed", normalizedUserDisplayName),
            BridgeChatMessageKind.GiftSubscription => TF("{0} gifted {1:N0} subs", normalizedUserDisplayName, normalizedAmount),
            BridgeChatMessageKind.Raid => TF("{0} raided with {1:N0} viewers", normalizedUserDisplayName, normalizedAmount),
            _ => normalizedSupportMessage
        };

        return new BridgeChatMessage(
            normalizedUserDisplayName,
            userLogin?.Trim() ?? string.Empty,
            userId?.Trim() ?? string.Empty,
            string.IsNullOrWhiteSpace(messageText) ? normalizedUserDisplayName : messageText,
            string.Empty,
            [],
            [],
            [],
            DateTimeOffset.Now)
        {
            Kind = kind,
            SupportAmount = normalizedAmount,
            SupportTier = tier?.Trim() ?? string.Empty,
            SupportMonths = Math.Max(0, months),
            SupportMessage = normalizedSupportMessage
        };
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
            || !HasThirdPartyEmoteBoundaryBefore(text, startIndex)
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
                if (!HasThirdPartyEmoteBoundaryAfter(text, startIndex + candidate.Code.Length))
                {
                    continue;
                }

                emote = candidate;
                return true;
            }
        }

        return false;
    }

    private static bool HasThirdPartyEmoteBoundaryBefore(string text, int startIndex)
    {
        if (startIndex <= 0)
        {
            return true;
        }

        return IsThirdPartyEmoteBoundary(text[startIndex - 1]);
    }

    private static bool HasThirdPartyEmoteBoundaryAfter(string text, int endIndex)
    {
        if (endIndex >= text.Length)
        {
            return true;
        }

        return IsThirdPartyEmoteBoundary(text[endIndex]);
    }

    private static bool IsThirdPartyEmoteBoundary(char character)
    {
        return char.IsWhiteSpace(character)
            || character is '!' or '?' or ',' or ';' or '"' or '\'' or '(' or ')' or '[' or ']' or '{' or '}' or '<' or '>';
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
        string? fallbackMessage = null;
        for (var maxOptions = 4; maxOptions >= 1; maxOptions--)
        {
            var sections = BuildTriggerInfoAnnouncementSections(configuration, currentAvatarId, maxOptions);
            if (sections.Count == 0)
            {
                return string.Empty;
            }

            var candidate = SanitizeBotMessage(TF("Crystal Relay triggers: {0}", string.Join(" | ", sections)), truncate: false);
            fallbackMessage = candidate;
            if (candidate.Length <= TwitchChatMessageMaxCharacters)
            {
                return candidate;
            }
        }

        return string.IsNullOrWhiteSpace(fallbackMessage)
            ? string.Empty
            : SanitizeBotMessage(fallbackMessage);
    }

    private static IReadOnlyList<string> BuildTriggerInfoAnnouncementSections(
        BridgeRuntimeConfiguration configuration,
        string currentAvatarId,
        int maxOptions)
    {
        var normalizedCurrentAvatarId = currentAvatarId?.Trim() ?? string.Empty;
        if (string.IsNullOrWhiteSpace(normalizedCurrentAvatarId))
        {
            return [];
        }

        var channelPointSections = configuration.Rules
            .Where(rule => rule.IsEnabled
                && rule.TriggerType == TwitchTriggerType.ChannelPoints
                && rule.ActionType == OscActionType.SetTrigger
                && rule.UsesSharedNumberedOutfitReward
                && rule.PostOutfitChoiceListToTwitchChat
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
            .Select(group => BuildChannelPointOutfitAnnouncementSection(group, maxOptions))
            .Where(section => !string.IsNullOrWhiteSpace(section))
            .ToArray();

        var bitsOptions = configuration.Rules
            .Where(rule => rule.IsEnabled
                && IsBitsOutfitSetTriggerRule(rule)
                && IsSupporterRuleScopedToCurrentAvatar(rule, normalizedCurrentAvatarId)
                && !string.IsNullOrWhiteSpace(rule.SharedRewardHelpText))
            .OrderBy(rule => Math.Max(1, rule.MinimumAmount))
            .ThenBy(rule => rule.SharedRewardHelpText, StringComparer.CurrentCultureIgnoreCase)
            .Select(rule => TF("Cheer{0} {1}", Math.Max(1, rule.MinimumAmount), rule.SharedRewardHelpText.Trim()))
            .ToArray();

        var sections = new List<string>();
        if (channelPointSections.Length > 0)
        {
            sections.Add(TF("Outfits: {0}", string.Join("; ", channelPointSections)));
        }

        if (bitsOptions.Length > 0)
        {
            sections.Add(TF("Bits outfits: {0}", BuildCompactAnnouncementOptionList(bitsOptions, maxOptions)));
        }

        var supporterGrowthBits = BuildSupporterGrowthBitsAnnouncementOptions(configuration.AvatarScaleRules);
        if (supporterGrowthBits.Count > 0)
        {
            sections.Add(TF("Scale bits: {0}", BuildCompactAnnouncementOptionList(supporterGrowthBits, maxOptions)));
        }

        var supporterGrowthSubs = BuildSupporterGrowthSubscriptionAnnouncementOptions(configuration.AvatarScaleRules);
        if (supporterGrowthSubs.Count > 0)
        {
            sections.Add(TF("Subs/gifts: {0}", BuildCompactAnnouncementOptionList(supporterGrowthSubs, maxOptions)));
        }

        var forceMovementOptions = BuildForceMovementAnnouncementOptions(configuration.Rules);
        if (forceMovementOptions.Count > 0)
        {
            sections.Add(TF("Force movement: {0}", BuildCompactAnnouncementOptionList(forceMovementOptions, maxOptions)));
        }

        var paidOptions = BuildCurrentAvatarSupporterAnnouncementOptions(configuration.Rules, normalizedCurrentAvatarId);
        if (paidOptions.Count > 0)
        {
            sections.Add(TF("Paid triggers: {0}", BuildCompactAnnouncementOptionList(paidOptions, maxOptions)));
        }

        return sections;
    }

    private static string BuildChannelPointOutfitAnnouncementSection(
        IGrouping<string, TriggerRuleSnapshot> group,
        int maxOptions)
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
        return TF("{0}: {1}", rewardName.Trim(), BuildCompactAnnouncementOptionList(options, maxOptions));
    }

    private static IReadOnlyList<string> BuildSupporterGrowthBitsAnnouncementOptions(
        IReadOnlyList<AvatarScaleRuleSnapshot> rules)
    {
        var options = new List<string>();
        foreach (var rule in rules
                     .Where(rule => rule.IsEnabled && rule.TriggerType == AvatarScaleTriggerType.SupporterGrowth)
                     .OrderBy(rule => rule.Name, StringComparer.CurrentCultureIgnoreCase))
        {
            var growKeyword = string.IsNullOrWhiteSpace(rule.SupporterGrowthGrowKeyword)
                ? "grow"
                : rule.SupporterGrowthGrowKeyword.Trim();
            var shrinkKeyword = string.IsNullOrWhiteSpace(rule.SupporterGrowthShrinkKeyword)
                ? "shrink"
                : rule.SupporterGrowthShrinkKeyword.Trim();
            var keywordText = string.Equals(growKeyword, shrinkKeyword, StringComparison.OrdinalIgnoreCase)
                ? growKeyword
                : $"{growKeyword}/{shrinkKeyword}";
            var timeSuffix = rule.SupporterGrowthSecondsPerBitsUnit > 0
                ? TF(", +{0}/{1} bits", DescribeDuration(rule.SupporterGrowthSecondsPerBitsUnit), Math.Max(1, rule.SupporterGrowthBitsTimerUnit))
                : string.Empty;

            foreach (var range in rule.SupporterGrowthBitRanges
                         .Where(range => range.HeightAddedMeters > 0)
                         .OrderBy(range => Math.Max(1, range.MinimumBits)))
            {
                options.Add(TF(
                    "Cheer{0} {1} (+/-{2}m{3})",
                    Math.Max(1, range.MinimumBits),
                    keywordText,
                    Math.Abs(range.HeightAddedMeters).ToString("0.###", CultureInfo.InvariantCulture),
                    timeSuffix));
            }
        }

        return options;
    }

    private static IReadOnlyList<string> BuildSupporterGrowthSubscriptionAnnouncementOptions(
        IReadOnlyList<AvatarScaleRuleSnapshot> rules)
    {
        var options = new List<string>();
        foreach (var rule in rules
                     .Where(rule => rule.IsEnabled && rule.TriggerType == AvatarScaleTriggerType.SupporterGrowth)
                     .OrderBy(rule => rule.Name, StringComparer.CurrentCultureIgnoreCase))
        {
            AddSupporterGrowthTierAnnouncementOption(options, "T1", rule.SupporterGrowthTier1HeightMeters, rule.SupporterGrowthTier1Seconds);
            AddSupporterGrowthTierAnnouncementOption(options, "T2", rule.SupporterGrowthTier2HeightMeters, rule.SupporterGrowthTier2Seconds);
            AddSupporterGrowthTierAnnouncementOption(options, "T3", rule.SupporterGrowthTier3HeightMeters, rule.SupporterGrowthTier3Seconds);
        }

        return options;
    }

    private static void AddSupporterGrowthTierAnnouncementOption(
        ICollection<string> options,
        string tierLabel,
        double heightMeters,
        int seconds)
    {
        var parts = new List<string>();
        if (heightMeters > 0)
        {
            parts.Add($"+{heightMeters.ToString("0.###", CultureInfo.InvariantCulture)}m");
        }

        if (seconds > 0)
        {
            parts.Add("+" + DescribeDuration(seconds));
        }

        if (parts.Count > 0)
        {
            options.Add(TF("{0} {1} each", tierLabel, string.Join(" ", parts)));
        }
    }

    private static IReadOnlyList<string> BuildForceMovementAnnouncementOptions(IReadOnlyList<TriggerRuleSnapshot> rules)
    {
        return rules
            .Where(rule => rule.IsEnabled && IsBitsForceMovementRule(rule) && rule.BitsKeywordEnabled && !string.IsNullOrWhiteSpace(rule.SupporterKeywordText))
            .OrderBy(rule => Math.Max(1, rule.MinimumAmount))
            .ThenBy(rule => rule.SupporterKeywordText, StringComparer.CurrentCultureIgnoreCase)
            .Select(rule => TF("Cheer{0} {1}", Math.Max(1, rule.MinimumAmount), rule.SupporterKeywordText.Trim()))
            .ToArray();
    }

    private static IReadOnlyList<string> BuildCurrentAvatarSupporterAnnouncementOptions(
        IReadOnlyList<TriggerRuleSnapshot> rules,
        string currentAvatarId)
    {
        return rules
            .Where(rule => rule.IsEnabled
                && IsSupporterRuleScopedToCurrentAvatar(rule, currentAvatarId)
                && rule.TriggerType is TwitchTriggerType.Bits or TwitchTriggerType.Subscriptions
                && !IsBitsOutfitSetTriggerRule(rule))
            .OrderBy(rule => GetSupporterOverrideListTriggerTypeSortRank(rule.TriggerType))
            .ThenBy(rule => Math.Max(1, rule.MinimumAmount))
            .ThenBy(rule => rule.Name, StringComparer.CurrentCultureIgnoreCase)
            .Select(rule => TF("{0} ({1})", rule.Name, DescribeSupporterOverrideOption(rule)))
            .Where(option => !string.IsNullOrWhiteSpace(option))
            .ToArray();
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
            .Replace("{cooldown}", GetBotMessageCooldownText(rule), StringComparison.OrdinalIgnoreCase)
            .Replace("{parameter}", DescribeActionAddress(rule), StringComparison.OrdinalIgnoreCase)
            .Replace("{value}", resolvedValue, StringComparison.OrdinalIgnoreCase);

        return SanitizeBotMessage(message);
    }

    private static string GetBotMessageCooldownText(TriggerRuleSnapshot rule)
    {
        if (rule.BotMessageCooldownSeconds.HasValue)
        {
            return DescribeDuration(rule.BotMessageCooldownSeconds.Value);
        }

        return rule.UsesLinkedChannelPointReward
            ? T("Twitch cooldown unknown")
            : DescribeDuration(GetCooldownSeconds(rule));
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
        if (IsBitsForceMovementRule(rule))
        {
            var keyword = !rule.BitsKeywordEnabled || string.IsNullOrWhiteSpace(rule.SupporterKeywordText)
                ? T("movement word")
                : rule.SupporterKeywordText.Trim();
            return TF(
                "Cheer{0} {1}: {2} for {3}",
                threshold,
                keyword,
                DescribeMovementAction(rule.MovementDirection),
                DescribeDuration(Math.Max(1, rule.DurationSeconds)));
        }

        if (rule.TriggerType == TwitchTriggerType.Bits)
        {
            return rule.AmountScaledDurationEnabled
                ? rule.DurationSeconds > 0
                    ? TF(
                        "Bits {0}+: start {1}, then every {2} bits adds {3}",
                        threshold,
                        DescribeDuration(rule.DurationSeconds),
                        Math.Max(1, rule.BitsAmountUnitsPerDuration),
                        DescribeDuration(Math.Max(1, rule.BitsSecondsPerAmountUnit)))
                    : TF(
                        "Bits {0}+: every {1} bits adds {2}",
                        threshold,
                        Math.Max(1, rule.BitsAmountUnitsPerDuration),
                        DescribeDuration(Math.Max(1, rule.BitsSecondsPerAmountUnit)))
                : TF("Bits {0}+: {1}", threshold, DescribeDuration(rule.DurationSeconds));
        }

        if (rule.TriggerType == TwitchTriggerType.Subscriptions)
        {
            return rule.AmountScaledDurationEnabled
                ? rule.DurationSeconds > 0
                    ? TF(
                        "Subs {0}+: start {1}, then T1 {2}, T2 {3}, T3 {4}",
                        threshold,
                        DescribeDuration(rule.DurationSeconds),
                        DescribeDuration(Math.Max(1, rule.SubscriptionTier1SecondsPerSub)),
                        DescribeDuration(Math.Max(1, rule.SubscriptionTier2SecondsPerSub)),
                        DescribeDuration(Math.Max(1, rule.SubscriptionTier3SecondsPerSub)))
                    : TF(
                        "Subs {0}+: T1 {1}, T2 {2}, T3 {3}",
                        threshold,
                        DescribeDuration(Math.Max(1, rule.SubscriptionTier1SecondsPerSub)),
                        DescribeDuration(Math.Max(1, rule.SubscriptionTier2SecondsPerSub)),
                        DescribeDuration(Math.Max(1, rule.SubscriptionTier3SecondsPerSub)))
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
        var droppedQueuedAvatarSwitches = 0;
        var droppedQueuedScaleRedeems = 0;

        lock (stateGate)
        {
            droppedQueuedTriggers = queuedTriggers.Sum(entry => entry.Value.Count);
            droppedQueuedLaneActions = queuedLaneActions.Sum(entry => entry.Value.Count);
            droppedQueuedAvatarSwitches = queuedAvatarSwitches.Count;
            (droppedQueuedScaleRedeems, _) = ClearQueuedAvatarScaleOperationsLocked(includeTests: false);
            queuedTriggers.Clear();
            queuedLaneActions.Clear();
            queuedAvatarSwitches.Clear();
        }

        var totalDropped = droppedQueuedTriggers + droppedQueuedLaneActions + droppedQueuedAvatarSwitches + droppedQueuedScaleRedeems;
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

    private void ObserveOscValue(
        OscObservedValue observedValue,
        bool updateActiveAvatarScaleCarryover = false)
    {
        if (string.Equals(observedValue.Address, "/avatar/change", StringComparison.Ordinal))
        {
            string? avatarId = observedValue.ParameterType == OscParameterType.String && observedValue.Value is string s
                ? s.Trim()
                : null;
            if (!string.IsNullOrEmpty(avatarId) && avatarId.StartsWith("avtr_", StringComparison.Ordinal))
            {
                try
                {
                    VrChatOscAvatarChangeReceived?.Invoke(avatarId);
                }
                catch (Exception ex)
                {
                    WriteLog($"VrChatOscAvatarChangeReceived subscriber threw (ignoring to keep the OSC receive loop alive): {ex.Message}");
                }
            }
            return;
        }

        if (IsAvatarScaleAddress(observedValue.Address))
        {
            string? passiveCarryoverDiagnostic = null;
            lock (stateGate)
            {
                avatarScaleValues[observedValue.Address] = observedValue;
                if (string.Equals(observedValue.Address, "/avatar/eyeheight", StringComparison.Ordinal)
                    && observedValue.ParameterType == OscParameterType.Float
                    && observedValue.Value is float heightMeters)
                {
                    if (updateActiveAvatarScaleCarryover)
                    {
                        UpdateActiveAvatarScaleCarriedHeightLocked(heightMeters);
                    }
                    else
                    {
                        passiveCarryoverDiagnostic = TryCreatePassiveAvatarScaleCarryoverDiagnosticLocked(heightMeters);
                    }
                }
            }

            if (!string.IsNullOrWhiteSpace(passiveCarryoverDiagnostic))
            {
                WriteLog(passiveCarryoverDiagnostic);
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

    private bool TryGetActiveAvatarScaleCarriedHeightLocked(out double carriedHeightMeters)
    {
        var now = DateTimeOffset.UtcNow;
        PruneExpiredAvatarScaleCarryoverLocked(now);
        if (activeAvatarScaleCarryover is not null
            && activeAvatarScaleCarryover.ActiveUntil > now)
        {
            carriedHeightMeters = activeAvatarScaleCarryover.CarriedHeightMeters;
            return true;
        }

        carriedHeightMeters = 0;
        return false;
    }

    private string? TryCreatePassiveAvatarScaleCarryoverDiagnosticLocked(double observedHeightMeters)
    {
        var now = DateTimeOffset.UtcNow;
        PruneExpiredAvatarScaleCarryoverLocked(now);
        if (activeAvatarScaleCarryover is null
            || activeAvatarScaleCarryover.ActiveUntil <= now
            || Math.Abs(activeAvatarScaleCarryover.CarriedHeightMeters - observedHeightMeters) < 0.001
            || now < nextAvatarScalePassiveCarryoverLogAt)
        {
            return null;
        }

        nextAvatarScalePassiveCarryoverLogAt = now.Add(AvatarScalePassiveCarryoverLogThrottle);
        return $"Ignored passive /avatar/eyeheight read of {observedHeightMeters:0.###}m while active scale carryover from '{activeAvatarScaleCarryover.SourceRuleName}' is holding {activeAvatarScaleCarryover.CarriedHeightMeters:0.###}m for {DescribeDuration((activeAvatarScaleCarryover.ActiveUntil - now).TotalSeconds)}.";
    }

    private void UpdateActiveAvatarScaleCarriedHeightLocked(double heightMeters)
    {
        var now = DateTimeOffset.UtcNow;
        PruneExpiredAvatarScaleCarryoverLocked(now);
        foreach (var pair in activeAvatarScaleHeightSessions.ToArray())
        {
            var session = pair.Value;
            if (session.ActiveUntil <= now)
            {
                continue;
            }

            activeAvatarScaleHeightSessions[pair.Key] = session with
            {
                CarriedHeightMeters = heightMeters
            };
        }

        if (activeAvatarScaleCarryover is not null
            && activeAvatarScaleCarryover.ActiveUntil > now)
        {
            activeAvatarScaleCarryover = activeAvatarScaleCarryover with
            {
                CarriedHeightMeters = heightMeters
            };
        }

        if (activeAvatarScaleRestoreSequence is not null
            && activeAvatarScaleRestoreSequence.ActiveUntil > now)
        {
            activeAvatarScaleRestoreSequence = activeAvatarScaleRestoreSequence with
            {
                CarriedHeightMeters = heightMeters
            };
        }
    }

    private void UpdateActiveAvatarScaleCarryoverRestoreSequenceLocked(
        Guid ruleId,
        ActiveAvatarScaleRestoreSequenceState sequence)
    {
        var now = DateTimeOffset.UtcNow;
        PruneExpiredAvatarScaleCarryoverLocked(now);
        if (activeAvatarScaleCarryover is not null
            && activeAvatarScaleCarryover.SourceRuleId == ruleId
            && activeAvatarScaleCarryover.ActiveUntil > now)
        {
            activeAvatarScaleCarryover = activeAvatarScaleCarryover with
            {
                AvatarId = sequence.AvatarId,
                RestoreSequenceId = sequence.SequenceId,
                CarriedHeightMeters = sequence.CarriedHeightMeters,
                RestoreHeightMeters = sequence.RestoreHeightMeters,
                ActiveUntil = sequence.ActiveUntil,
                RestoreToPaidGrowthIfActive = sequence.RestoreToPaidGrowthIfActive
            };
            return;
        }

        SetActiveAvatarScaleCarryoverLocked(
            ruleId,
            Guid.Empty,
            sequence.SequenceId,
            sequence.SourceRuleName,
            sequence.AvatarId,
            sequence.CarriedHeightMeters,
            sequence.RestoreHeightMeters,
            sequence.ActiveUntil,
            sequence.RestoreToPaidGrowthIfActive);
    }

    private void SetActiveAvatarScaleCarryoverLocked(
        Guid ruleId,
        Guid sessionId,
        long restoreSequenceId,
        string sourceRuleName,
        string avatarId,
        double carriedHeightMeters,
        double restoreHeightMeters,
        DateTimeOffset activeUntil,
        bool restoreToPaidGrowthIfActive)
    {
        var now = DateTimeOffset.UtcNow;
        if (activeUntil <= now)
        {
            return;
        }

        var carryoverId = activeAvatarScaleCarryover is { } existing
            && existing.SourceRuleId == ruleId
            && (sessionId == Guid.Empty || existing.SourceSessionId == sessionId)
            ? existing.CarryoverId
            : Guid.NewGuid();

        activeAvatarScaleCarryover = new ActiveAvatarScaleCarryoverState(
            carryoverId,
            ruleId,
            sessionId,
            restoreSequenceId,
            string.IsNullOrWhiteSpace(sourceRuleName) ? "Avatar Scale" : sourceRuleName,
            avatarId?.Trim() ?? string.Empty,
            carriedHeightMeters,
            restoreHeightMeters,
            activeUntil,
            restoreToPaidGrowthIfActive);
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
        CancellationTokenSource[] activeFloatRedeemCancellations;
        SemaphoreSlim[] activeFloatRedeemGates;
        CancellationTokenSource[] activeGlitchyRedeemCancellations;
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
            activeFloatRedeemCancellations = [.. activeFloatRedeemSessions.Values.Select(session => session.CompletionCancellation)];
            activeFloatRedeemGates = [.. activeFloatRedeemSessions.Values.Select(session => session.SendGate)];
            activeFloatRedeemSessions.Clear();
            activeGlitchyRedeemCancellations = [.. activeGlitchyRedeemSessions.Values.Select(session => session.CompletionCancellation)];
            activeGlitchyRedeemSessions.Clear();
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
            activeRuleUnlocks.Clear();
            activeAvatarSwitchRuleLockouts.Clear();
            activeAvatarScaleEffects.Clear();
            activeAvatarScaleHeightSessions.Clear();
            pendingAvatarScaleHeightRestores.Clear();
            ClearQueuedAvatarScaleOperationsLocked(includeTests: true);
            activeAvatarScaleOperation = null;
            activeAvatarScaleCarryover = null;
            nextAvatarScaleAvatarChangeSequenceId++;
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
            queuedAvatarSwitches.Clear();
            drainingQueuedAvatarSwitches = false;
            nextSupporterOverrideQueueOrder = 0;
            nextQueuedAvatarSwitchOrder = 0;
            universalTriggerGlobalDelays.Clear();
            universalTriggerUserDelays.Clear();
            triggerInfoCommandCooldowns.Clear();
            powerUpInactiveAvatarLogTimes.Clear();
            nextWorldCommandAllowedAt = DateTimeOffset.MinValue;
            cachedWorldCommandResultExpiresAt = DateTimeOffset.MinValue;
            cachedWorldCommandUserId = string.Empty;
            cachedWorldCommandResult = null;
            universalQueueGates = [.. universalTriggerQueueGates.Values];
            universalTriggerQueueGates.Clear();
            supporterGrowthCancellations = [.. avatarScaleSupporterGrowthStates.Values
                .Select(state => state.SessionCancellation)
                .Where(cancellation => cancellation is not null)
                .Cast<CancellationTokenSource>()];
            avatarScaleSupporterGrowthStates.Clear();
            avatarScaleFollowTriggeredUsers.Clear();
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

        foreach (var activeFloatRedeemCancellation in activeFloatRedeemCancellations)
        {
            activeFloatRedeemCancellation.Cancel();
            activeFloatRedeemCancellation.Dispose();
        }

        foreach (var activeFloatRedeemGate in activeFloatRedeemGates)
        {
            activeFloatRedeemGate.Dispose();
        }

        foreach (var activeGlitchyRedeemCancellation in activeGlitchyRedeemCancellations)
        {
            activeGlitchyRedeemCancellation.Cancel();
            activeGlitchyRedeemCancellation.Dispose();
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
        universalTriggerGlobalGate.Dispose();

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
                            await ExecuteRuleActionAsync(ruleSnapshot, queuedTrigger.Event, cancellationToken, isTest: false, queuedReplay: true, allowLaneQueue: true, isResuming: false);
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
                            var candidateAction = queue.Peek();
                            if (!candidateAction.IsTest)
                            {
                                var now = DateTimeOffset.UtcNow;
                                var currentRule = activeConfiguration?.Rules.FirstOrDefault(rule => rule.Id == candidateAction.Rule.Id);
                                if (currentRule is null || !currentRule.IsEnabled)
                                {
                                    queuedAction = queue.Dequeue();
                                    if (queue.Count == 0)
                                    {
                                        queuedLaneActions.Remove(laneKey);
                                    }
                                }
                                else if (TryGetTemporarilyDisabledUntilLocked(currentRule.Id, now, out var temporarilyDisabledUntil)
                                    && temporarilyDisabledUntil > now)
                                {
                                    delay = temporarilyDisabledUntil - now;
                                }
                                else
                                {
                                    queuedAction = queue.Dequeue();
                                    if (queue.Count == 0)
                                    {
                                        queuedLaneActions.Remove(laneKey);
                                    }
                                }
                            }
                            else
                            {
                                queuedAction = queue.Dequeue();
                                if (queue.Count == 0)
                                {
                                    queuedLaneActions.Remove(laneKey);
                                }
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

                        await ExecuteRuleActionAsync(ruleToExecute, queuedAction.Event, cancellationToken, queuedAction.IsTest, queuedReplay: true, allowLaneQueue: true, isResuming: false);
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

    private void EnsureQueuedAvatarSwitchDrain()
    {
        var cancellationToken = runtimeCancellation?.Token ?? CancellationToken.None;
        var shouldStart = false;

        lock (stateGate)
        {
            if (!drainingQueuedAvatarSwitches && queuedAvatarSwitches.Count > 0)
            {
                drainingQueuedAvatarSwitches = true;
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
                    QueuedAvatarSwitchState? queuedSwitch = null;
                    TimeSpan delay = TimeSpan.Zero;
                    string? logMessage = null;
                    var dropQueuedItems = false;
                    var dropCount = 0;

                    lock (stateGate)
                    {
                        if (queuedAvatarSwitches.Count == 0)
                        {
                            break;
                        }

                        var now = DateTimeOffset.UtcNow;
                        var nextSwitch = queuedAvatarSwitches.Peek();
                        if (activeConfiguration?.EmergencyRedeemStopEnabled == true)
                        {
                            dropQueuedItems = true;
                            dropCount = queuedAvatarSwitches.Count;
                            queuedAvatarSwitches.Clear();
                        }
                        else if (IsSupporterOverrideSequenceActiveLocked(now))
                        {
                            delay = GetSupporterOverrideSequenceEndsAtLocked(now) - now;
                            if (!nextSwitch.SupporterWaitLogged)
                            {
                                nextSwitch.SupporterWaitLogged = true;
                                logMessage = $"Queued avatar switch '{nextSwitch.Rule.Name}' is waiting for the paid override queue to finish.";
                            }
                        }
                        else if (actionLanes.TryGetValue(AvatarSwitchLaneKey, out var activeLane)
                            && activeLane.BusyUntil > now)
                        {
                            delay = activeLane.BusyUntil - now;
                            if (!nextSwitch.AvatarLaneWaitLogged)
                            {
                                nextSwitch.AvatarLaneWaitLogged = true;
                                logMessage = $"Queued avatar switch '{nextSwitch.Rule.Name}' is waiting for the current avatar switch to finish.";
                            }
                        }
                        else if (nextSwitch.Kind == QueuedAvatarSwitchKind.PendingTrigger)
                        {
                            var currentRule = activeConfiguration?.Rules.FirstOrDefault(rule => rule.Id == nextSwitch.Rule.Id);
                            if (currentRule is null || !currentRule.IsEnabled || !IsQueuedAvatarSwitchRule(currentRule))
                            {
                                queuedAvatarSwitches.Dequeue();
                                logMessage = $"Dropped queued avatar switch '{nextSwitch.Rule.Name}' because that rule is no longer enabled.";
                            }
                            else if (TryGetTemporarilyDisabledUntilLocked(currentRule.Id, now, out var temporarilyDisabledUntil)
                                && temporarilyDisabledUntil > now)
                            {
                                delay = temporarilyDisabledUntil - now;
                                if (!nextSwitch.TemporaryDisableWaitLogged)
                                {
                                    nextSwitch.TemporaryDisableWaitLogged = true;
                                    logMessage = $"Queued avatar switch '{currentRule.Name}' is waiting for disable pairing to clear for {DescribeDuration(delay.TotalSeconds)}.";
                                }
                            }
                            else if (cooldowns.TryGetValue(currentRule.Id, out var cooldownUntil) && cooldownUntil > now)
                            {
                                delay = cooldownUntil - now;
                                if (!nextSwitch.CooldownWaitLogged)
                                {
                                    nextSwitch.CooldownWaitLogged = true;
                                    logMessage = $"Queued avatar switch '{currentRule.Name}' is waiting for cooldown to clear for {DescribeDuration(delay.TotalSeconds)}.";
                                }
                            }
                            else
                            {
                                queuedSwitch = queuedAvatarSwitches.Dequeue();
                                queuedSwitch.Rule = currentRule;
                            }
                        }
                        else
                        {
                            var currentRule = activeConfiguration?.Rules.FirstOrDefault(rule => rule.Id == nextSwitch.Rule.Id);
                            if (currentRule is null || !currentRule.IsEnabled || !IsQueuedAvatarSwitchRule(currentRule))
                            {
                                queuedAvatarSwitches.Dequeue();
                                logMessage = $"Dropped paused avatar switch '{nextSwitch.Rule.Name}' because that rule is no longer enabled.";
                            }
                            else
                            {
                                queuedSwitch = queuedAvatarSwitches.Dequeue();
                            }
                        }
                    }

                    if (!string.IsNullOrWhiteSpace(logMessage))
                    {
                        WriteLog(logMessage);
                    }

                    if (dropQueuedItems)
                    {
                        WriteLog($"Dropped {dropCount} queued avatar switch{(dropCount == 1 ? string.Empty : "es")} because redeems are paused.");
                        break;
                    }

                    if (delay > TimeSpan.Zero)
                    {
                        await Task.Delay(delay, cancellationToken);
                        continue;
                    }

                    if (queuedSwitch is null)
                    {
                        continue;
                    }

                    try
                    {
                        await ExecuteQueuedAvatarSwitchAsync(queuedSwitch, cancellationToken);
                    }
                    catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
                    {
                        throw;
                    }
                    catch (Exception ex)
                    {
                        WriteLog($"Queued avatar switch '{queuedSwitch.Rule.Name}' failed: {ex.Message}");
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
                    drainingQueuedAvatarSwitches = false;
                    restart = queuedAvatarSwitches.Count > 0;
                }

                if (restart)
                {
                    EnsureQueuedAvatarSwitchDrain();
                }
            }
        }, CancellationToken.None);
    }

    private async Task ExecuteQueuedAvatarSwitchAsync(QueuedAvatarSwitchState queuedSwitch, CancellationToken cancellationToken)
    {
        if (queuedSwitch.Kind == QueuedAvatarSwitchKind.PausedActiveSwitch)
        {
            await ResumePausedAvatarSwitchAsync(queuedSwitch, cancellationToken);
            return;
        }

        await ExecuteQueuedAvatarSwitchTriggerAsync(queuedSwitch, cancellationToken);
    }

    private async Task ResumePausedAvatarSwitchAsync(QueuedAvatarSwitchState queuedSwitch, CancellationToken cancellationToken)
    {
        if (queuedSwitch.Action is null)
        {
            return;
        }

        var rule = queuedSwitch.Rule;
        var action = queuedSwitch.Action;
        var remainingSeconds = Math.Max(1d, queuedSwitch.RemainingDuration.TotalSeconds);
        var laneKeys = GetActionLaneKeys(rule, action);
        var laneLeaseId = laneKeys.Count == 0 ? Guid.Empty : Guid.NewGuid();
        await SendPacketsToVrChatAsync(action.Packets, cancellationToken);
        RememberAvatarParameterValues(rule, action.ObservedValues.Count > 0 ? action.ObservedValues : null, action.DisplayValue);

        lock (stateGate)
        {
            foreach (var laneKey in laneKeys)
            {
                actionLanes[laneKey] = new ActiveMovementLaneState(
                    laneLeaseId,
                    DateTimeOffset.UtcNow.AddSeconds(remainingSeconds),
                    rule.Id,
                    false);
            }
        }

        if (!string.IsNullOrWhiteSpace(action.AvatarTargetId))
        {
            SetCurrentVrChatAvatar(
                action.AvatarTargetId,
                notify: true,
                GetAvatarScaleAvatarChangeCarryoverMode(rule));
        }

        ScheduleReset(rule, action, remainingSeconds, laneKeys, laneLeaseId, notifyManagedRewardState: false);
        WriteLog($"Resumed paused avatar switch '{rule.Name}' for {DescribeDuration(remainingSeconds)}.");
        ManagedRewardAvailabilityChanged?.Invoke();
        await Task.Delay(TimeSpan.FromSeconds(remainingSeconds), cancellationToken);
    }

    private async Task ExecuteQueuedAvatarSwitchTriggerAsync(QueuedAvatarSwitchState queuedSwitch, CancellationToken cancellationToken)
    {
        var rule = queuedSwitch.Rule;
        if (ShouldBlockAvatarChangeDuringActiveScaling(rule, isTest: false))
        {
            WriteLog($"Dropped queued avatar switch '{rule.Name}' because Avatar Scaling is active.");
            return;
        }

        await ExecuteRuleActionAsync(
            rule,
            queuedSwitch.Event,
            cancellationToken,
            isTest: false,
            queuedReplay: true,
            allowLaneQueue: false,
            isResuming: false);
        await Task.Delay(TimeSpan.FromSeconds(Math.Max(1, rule.DurationSeconds)), cancellationToken);
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
            PlayerMovementDirection.RandomMovement => "/input/random movement",
            PlayerMovementDirection.GlitchyMovement => "/input/glitchy movement",
            _ => "/input"
        },
        OscActionType.SetTrigger => "Set Trigger",
        _ => VrChatOscClient.NormalizeAvatarParameterAddress(rule.ParameterName)
    };

    private static int GetCooldownSeconds(TriggerRuleSnapshot rule) =>
        rule.ActionType == OscActionType.PlayerMovement && !IsBitsForceMovementRule(rule)
            ? 0
            : Math.Max(0, rule.CooldownSeconds);

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
        PlayerMovementDirection.RandomMovement => "Random Movement",
        PlayerMovementDirection.GlitchyMovement => "Glitchy Movement",
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

        return current.ValueKind switch
        {
            JsonValueKind.True => true,
            JsonValueKind.False => false,
            _ => false
        };
    }

    private sealed record PendingResetState(
        Guid RuleId,
        string RuleName,
        TriggerRuleSnapshot Rule,
        ResolvedRuleAction Action,
        IReadOnlyList<byte[]> Packets,
        CancellationTokenSource Cancellation,
        DateTimeOffset DueAt,
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

    private sealed record PausedAvatarScaleTimerSnapshot(
        Guid RuleId,
        Guid SessionId,
        long RestoreSequenceId,
        string AvatarId,
        double CarriedHeightMeters,
        double RestoreHeightMeters,
        string SourceRuleName,
        double RestoreSmoothTransitionSeconds,
        bool RestoreToPaidGrowthIfActive,
        bool IsTest,
        DateTimeOffset ActiveUntil,
        TimeSpan Remaining,
        int QueuedLiveScaleCountAtPause,
        string SourceDescription,
        AvatarScaleRuleSnapshot? Rule,
        bool EffectWasActive);

    private sealed record PausedMovementTimerSnapshot(
        TriggerRuleSnapshot Rule,
        ResolvedRuleAction Action,
        string MovementLaneKey,
        TimeSpan Remaining,
        int QueuedLiveLaneCountAtPause,
        string SourceDescription);

    private sealed class ActiveFloatGlitchyRedeemSessionState
    {
        public TriggerRuleSnapshot Rule { get; init; } = null!;
        public string Address { get; init; } = string.Empty;
        public double Min { get; init; }
        public double Max { get; init; }
        public int IntervalMs { get; init; }
        public DateTimeOffset ActiveUntil { get; init; }
        public double ResetValue { get; init; }
        public CancellationTokenSource CompletionCancellation { get; init; } = new();
        public List<string> LaneKeys { get; init; } = new();
        public Guid LeaseId { get; init; }
        public bool IsTest { get; init; }
    }

    private sealed class ActiveFloatRedeemSessionState
    {
        public ActiveFloatRedeemSessionState(
            TriggerRuleSnapshot rule,
            string address,
            double currentValue,
            double resetValue,
            DateTimeOffset activeUntil,
            CancellationTokenSource completionCancellation,
            IReadOnlyList<string> movementLaneKeys,
            Guid movementLaneLeaseId,
            bool isTest,
            bool boostMaximumReached)
        {
            Rule = rule;
            Address = address;
            CurrentValue = currentValue;
            ResetValue = resetValue;
            ActiveUntil = activeUntil;
            CompletionCancellation = completionCancellation;
            MovementLaneKeys = movementLaneKeys;
            MovementLaneLeaseId = movementLaneLeaseId;
            IsTest = isTest;
            BoostMaximumReached = boostMaximumReached;
        }

        public TriggerRuleSnapshot Rule { get; }

        public string Address { get; }

        public double CurrentValue { get; set; }

        public double ResetValue { get; }

        public DateTimeOffset ActiveUntil { get; set; }

        public CancellationTokenSource CompletionCancellation { get; set; }

        public IReadOnlyList<string> MovementLaneKeys { get; }

        public Guid MovementLaneLeaseId { get; }

        public bool IsTest { get; }

        public bool BoostMaximumReached { get; set; }

        public SemaphoreSlim SendGate { get; } = new(1, 1);
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
        SetTriggerRestoreMode RestoreMode,
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
            CancellationTokenSource completionCancellation,
            double? supporterFloatAddCurrentValue)
        {
            Rule = rule;
            Event = @event;
            Action = action;
            ActiveUntil = activeUntil;
            QueueOrder = queueOrder;
            CompletionCancellation = completionCancellation;
            SupporterFloatAddCurrentValue = supporterFloatAddCurrentValue;
        }

        public TriggerRuleSnapshot Rule { get; }

        public BridgeIncomingEvent Event { get; set; }

        public ResolvedRuleAction Action { get; }

        public DateTimeOffset ActiveUntil { get; set; }

        public long QueueOrder { get; }

        public CancellationTokenSource CompletionCancellation { get; set; }

        public double? SupporterFloatAddCurrentValue { get; set; }
    }

    private sealed class RuntimeRuleIndex
    {
        public static RuntimeRuleIndex Empty { get; } = new([]);

        private readonly Dictionary<TwitchTriggerType, List<IndexedRule>> rulesByTriggerType = [];
        private readonly Dictionary<TwitchTriggerType, List<IndexedRule>> globalOverrideRulesByTriggerType = [];
        private readonly Dictionary<string, List<IndexedRule>> channelPointRulesByRewardId = new(StringComparer.Ordinal);
        private readonly Dictionary<string, List<IndexedRule>> channelPointRulesByRewardTitle = new(StringComparer.OrdinalIgnoreCase);
        private readonly Dictionary<string, List<IndexedRule>> activeFloatBoostRulesByRewardId = new(StringComparer.Ordinal);
        private readonly Dictionary<string, List<IndexedRule>> activeFloatBoostRulesByRewardTitle = new(StringComparer.OrdinalIgnoreCase);
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

                    if (IsActiveFloatBoostRule(indexedRule.Rule))
                    {
                        var boostRewardId = indexedRule.Rule.ActiveFloatBoostRewardId?.Trim() ?? string.Empty;
                        if (!string.IsNullOrWhiteSpace(boostRewardId))
                        {
                            Add(activeFloatBoostRulesByRewardId, boostRewardId, indexedRule);
                        }

                        AddActiveFloatBoostTitleVariants(indexedRule.Rule.ActiveFloatBoostRewardTitle, indexedRule);
                    }
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

        public IEnumerable<TriggerRuleSnapshot> GetActiveFloatBoostCandidates(string rewardId, string rewardTitle)
        {
            var candidatesById = new Dictionary<Guid, IndexedRule>();
            AddCandidates(activeFloatBoostRulesByRewardId, rewardId, candidatesById);
            AddCandidates(
                activeFloatBoostRulesByRewardTitle,
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

        private void AddActiveFloatBoostTitleVariants(string title, IndexedRule indexedRule)
        {
            var titleKey = ManagedRewardPresentation.NormalizeTitleIdentityKey(title);
            if (string.IsNullOrWhiteSpace(titleKey))
            {
                return;
            }

            Add(activeFloatBoostRulesByRewardTitle, titleKey, indexedRule);
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
            long queueOrder,
            double supporterFloatAddAmount,
            double? supporterFloatAddResumeValue)
        {
            Rule = rule;
            Event = @event;
            RemainingDuration = remainingDuration;
            QueueOrder = queueOrder;
            SupporterFloatAddAmount = supporterFloatAddAmount;
            SupporterFloatAddResumeValue = supporterFloatAddResumeValue;
        }

        public TriggerRuleSnapshot Rule { get; set; }

        public BridgeIncomingEvent Event { get; set; }

        public TimeSpan RemainingDuration { get; set; }

        public long QueueOrder { get; }

        public double SupporterFloatAddAmount { get; set; }

        public double? SupporterFloatAddResumeValue { get; set; }
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
        double CarriedHeightMeters,
        double RestoreHeightMeters,
        DateTimeOffset ActiveUntil,
        string SourceRuleName,
        double RestoreSmoothTransitionSeconds,
        bool RestoreToPaidGrowthIfActive,
        bool IsTest);

    private sealed record ActiveAvatarScaleCarryoverState(
        Guid CarryoverId,
        Guid SourceRuleId,
        Guid SourceSessionId,
        long RestoreSequenceId,
        string SourceRuleName,
        string AvatarId,
        double CarriedHeightMeters,
        double RestoreHeightMeters,
        DateTimeOffset ActiveUntil,
        bool RestoreToPaidGrowthIfActive);

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
        long RestoreSequenceId,
        Guid CarryoverId,
        string SourceRuleName,
        double CarriedHeightMeters,
        double FallbackRestoreHeightMeters,
        DateTimeOffset ActiveUntil);

    private sealed class ActiveAvatarScaleSupporterGrowthState
    {
        public double AddedHeightMeters { get; set; }

        public double CurrentTargetHeightMeters { get; set; }

        public double NormalHeightMeters { get; set; }

        public DateTimeOffset PaidActiveUntil { get; set; }

        public bool AllowRewardScaleOverlay { get; set; } = true;

        public CancellationTokenSource? SessionCancellation { get; set; }
    }

    private sealed record QueuedRuleTrigger(BridgeIncomingEvent Event);

    private enum QueuedAvatarSwitchKind
    {
        PausedActiveSwitch,
        PendingTrigger
    }

    private sealed class QueuedAvatarSwitchState
    {
        private QueuedAvatarSwitchState(
            QueuedAvatarSwitchKind kind,
            TriggerRuleSnapshot rule,
            BridgeIncomingEvent? @event,
            ResolvedRuleAction? action,
            TimeSpan remainingDuration,
            long queueOrder)
        {
            Kind = kind;
            Rule = rule;
            Event = @event;
            Action = action;
            RemainingDuration = remainingDuration;
            QueueOrder = queueOrder;
        }

        public static QueuedAvatarSwitchState ForPausedSwitch(
            TriggerRuleSnapshot rule,
            ResolvedRuleAction action,
            TimeSpan remainingDuration,
            long queueOrder) =>
            new(
                QueuedAvatarSwitchKind.PausedActiveSwitch,
                rule,
                null,
                action,
                remainingDuration,
                queueOrder);

        public static QueuedAvatarSwitchState ForTrigger(
            TriggerRuleSnapshot rule,
            BridgeIncomingEvent @event,
            long queueOrder) =>
            new(
                QueuedAvatarSwitchKind.PendingTrigger,
                rule,
                @event,
                null,
                TimeSpan.Zero,
                queueOrder);

        public QueuedAvatarSwitchKind Kind { get; }

        public TriggerRuleSnapshot Rule { get; set; }

        public BridgeIncomingEvent? Event { get; }

        public ResolvedRuleAction? Action { get; }

        public TimeSpan RemainingDuration { get; }

        public long QueueOrder { get; }

        public bool SupporterWaitLogged { get; set; }

        public bool AvatarLaneWaitLogged { get; set; }

        public bool CooldownWaitLogged { get; set; }

        public bool TemporaryDisableWaitLogged { get; set; }
    }

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

        public bool PaidGrowthWaitLogged { get; set; }
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
        string? Diagnostic,
        bool IsAmbiguous = false);

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

        public string SubscriptionTier { get; init; } = string.Empty;
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
    DateTimeOffset ReceivedAt)
{
    public BridgeChatMessageKind Kind { get; init; } = BridgeChatMessageKind.Chat;

    public string RewardId { get; init; } = string.Empty;

    public string RewardTitle { get; init; } = string.Empty;

    public int RewardCost { get; init; }

    public string RewardUserInput { get; init; } = string.Empty;

    public int SupportAmount { get; init; }

    public string SupportTier { get; init; } = string.Empty;

    public int SupportMonths { get; init; }

    public string SupportMessage { get; init; } = string.Empty;

    public string MessageId { get; init; } = string.Empty;

    public string MessageType { get; init; } = string.Empty;

    public string SourceBroadcasterUserId { get; init; } = string.Empty;

    public string SourceBroadcasterUserLogin { get; init; } = string.Empty;

    public string SourceBroadcasterUserName { get; init; } = string.Empty;

    public string SourceMessageId { get; init; } = string.Empty;

    public bool IsSourceOnly { get; init; }
}

public enum BridgeChatMessageKind
{
    Chat,
    ChannelPointRedemption,
    BitsCheer,
    Subscription,
    Resubscription,
    GiftSubscription,
    Raid
}

public sealed record BridgeChatActivity(
    BridgeChatActivityKind Kind,
    string MessageText,
    DateTimeOffset ReceivedAt)
{
    public string TargetUserDisplayName { get; init; } = string.Empty;

    public string TargetUserLogin { get; init; } = string.Empty;

    public string TargetUserId { get; init; } = string.Empty;

    public string MessageId { get; init; } = string.Empty;

    public string SuspiciousStatus { get; init; } = string.Empty;
}

public enum BridgeChatActivityKind
{
    ChannelPointRedemption,
    SupportEvent,
    Follow,
    MessageDeleted,
    UserMessagesCleared,
    ChatCleared,
    Timeout,
    Ban,
    MessagePurged,
    SuspiciousUserUpdated,
    SuspiciousUserMessage,
    ModerationFailure
}

public sealed record AvatarScaleRuntimeStatus(
    double? CurrentHeightMeters,
    double? MinimumHeightMeters,
    double? MaximumHeightMeters,
    bool? ScalingAllowed,
    bool IsActive);

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
    string EmoteId,
    string EmoteSetId);
