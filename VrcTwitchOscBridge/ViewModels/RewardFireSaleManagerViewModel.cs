using System;
using System.Collections.ObjectModel;
using System.Collections.Specialized;
using System.ComponentModel;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using System.Windows.Input;
using VrcTwitchOscBridge.Infrastructure;
using VrcTwitchOscBridge.Models;
using VrcTwitchOscBridge.Services;

namespace VrcTwitchOscBridge.ViewModels;

public sealed class RewardFireSaleManagerViewModel : ObservableObject, IDisposable
{
    private readonly MainWindowViewModel? mainWindowViewModel;
    private readonly RewardFireSaleSettings fireSale;
    private bool disposed;
    private CancellationTokenSource? rewardFireSaleExpirationCancellation;
    private CancellationTokenSource? rewardFireSaleFundingCooldownCancellation;
    private DateTimeOffset? rewardFireSaleFundingRewardCooldownUntil;
    private bool suppressRewardFireSaleChangeSideEffects;

    private static readonly TimeSpan ThrottledLogWindow = TimeSpan.FromSeconds(30);

    public RewardFireSaleSettings Settings { get; }

    public RewardFireSaleManagerViewModel(AppSettings appSettings, MainWindowViewModel? mainWindowViewModel)
    {
        ArgumentNullException.ThrowIfNull(appSettings);
        this.mainWindowViewModel = mainWindowViewModel;
        Settings = appSettings.RewardFireSale;
        fireSale = Settings;

        fireSale.PropertyChanged += OnRewardFireSaleChanged;
        fireSale.Tiers.CollectionChanged += OnRewardFireSaleTiersCollectionChanged;
        foreach (var tier in fireSale.Tiers)
            tier.PropertyChanged += OnRewardFireSaleTierChanged;

        if (mainWindowViewModel?.BridgeCoordinator is not null)
        {
            mainWindowViewModel.BridgeCoordinator.RewardFireSaleContributionReceived += OnContributionReceived;
        }

        AddTierCommand = new RelayCommand(AddTier);
        RemoveTierCommand = new RelayCommand(RemoveTier, _ => fireSale.Tiers.Count > 1);
        StopSaleCommand = new RelayCommand(() => StopSale(expired: false), () => fireSale.IsSaleActive);
        ResetProgressCommand = new RelayCommand(ResetProgress, () => fireSale.CurrentProgress > 0);

        NormalizeSettings();
        RestoreStartupState();
        EnsureTierExists();
        ScheduleFireSaleExpiration();
        RefreshStateProperties();
    }

    public bool IsTemporary => fireSale.SaleMode == RewardFireSaleMode.Temporary;

    public IReadOnlyList<RewardFireSaleModeOption> ModeOptions { get; } =
    [
        new RewardFireSaleModeOption(RewardFireSaleMode.Temporary, T("Temporary")),
        new RewardFireSaleModeOption(RewardFireSaleMode.Permanent, T("Permanent"))
    ];

    public string StatusText
    {
        get
        {
            if (!fireSale.IsEnabled)
            {
                return T("Reward Fire Sale is off.");
            }

            if (IsActiveNow())
            {
                var untilText = fireSale.SaleMode == RewardFireSaleMode.Temporary && fireSale.ActiveUntilUtc is { } activeUntil
                    ? TF(" Ends {0}.", activeUntil.ToLocalTime().ToString("g"))
                    : T(" Stays active until stopped.");
                return TF(
                    "Fire Sale active: {0}% off from the {1:N0} goal tier.{2}",
                    fireSale.ActiveDiscountPercent,
                    fireSale.ActiveTierGoalAmount,
                    untilText);
            }

            var nextTier = GetNextTier();
            if (nextTier is null)
            {
                return T("Add a Fire Sale tier to start tracking progress.");
            }

            var remaining = Math.Max(0, nextTier.GoalAmount - fireSale.CurrentProgress);
            return TF(
                "{0:N0} / {1:N0} progress. {2:N0} more to start {3}% off.",
                fireSale.CurrentProgress,
                nextTier.GoalAmount,
                remaining,
                nextTier.DiscountPercent);
        }
    }

    public double ProgressPercent
    {
        get
        {
            var nextTier = GetNextTier();
            if (nextTier is null)
            {
                return 0;
            }

            return Math.Clamp(fireSale.CurrentProgress / (double)Math.Max(1, nextTier.GoalAmount) * 100d, 0d, 100d);
        }
    }

    public string ActiveWarningText => T("Fire Sale Test Mode warning: starting or stopping a Fire Sale changes Crystal Relay-owned Twitch reward costs. Linked rewards stay listen-only. Stop the sale or let the timer expire to restore normal prices.");

    public string FundingRewardConversionText
    {
        get
        {
            return TF(
                "At {0:N0} points and {1:N0}:1 conversion, each redeem adds {2:N0} Fire Sale progress.",
                Math.Max(1, fireSale.FundingRewardCost),
                Math.Max(1, fireSale.RewardPointsPerProgressUnit),
                GetFundingProgressPerRedeem());
        }
    }

    public string FundingRewardPrompt => BuildFundingRewardPrompt();

    public ICommand AddTierCommand { get; }
    public ICommand RemoveTierCommand { get; }
    public ICommand StopSaleCommand { get; }
    public ICommand ResetProgressCommand { get; }

    private bool OnContributionReceived(RewardFireSaleContribution contribution)
    {
        return RunOnUi(() => HandleContribution(contribution));
    }

    private bool HandleContribution(RewardFireSaleContribution contribution)
    {
        ExpireIfNeeded();
        var isFundingReward = contribution.Type == RewardFireSaleContributionType.ManagedReward
            && IsFundingReward(contribution.RewardId, contribution.RewardTitle);
        if (!fireSale.IsEnabled)
            return isFundingReward;
        if (IsActiveNow() && !CanAdvanceToLaterTier())
        {
            AppendThrottledLog("reward-fire-sale-active-progress-paused",
                "Reward Fire Sale is already active at its final available tier, so new Bits and funding reward redeems are not adding progress right now.");
            return isFundingReward;
        }
        var contributionAmount = ResolveContributionAmount(contribution);
        if (contributionAmount <= 0)
            return isFundingReward;
        fireSale.CurrentProgress += contributionAmount;
        if (isFundingReward)
            StartFundingRewardCooldown();
        AppendLog($"Reward Fire Sale added {contributionAmount:N0} progress from {contribution.UserDisplayName}. Total: {fireSale.CurrentProgress:N0}.");
        ActivateIfGoalReached();
        RefreshStateProperties();
        QueueSave();
        return isFundingReward;
    }

    private void NormalizeSettings()
    {
        var changed = false;
        if (fireSale.Tiers.Count == 0)
        {
            fireSale.Tiers.Add(new RewardFireSaleTier());
            changed = true;
        }

        foreach (var tier in fireSale.Tiers)
        {
            var goal = tier.GoalAmount;
            var discount = tier.DiscountPercent;
            tier.GoalAmount = Math.Max(1, goal);
            tier.DiscountPercent = Math.Clamp(discount, 1, 100);
            changed |= goal != tier.GoalAmount || discount != tier.DiscountPercent;
        }

        if (fireSale.TemporaryDurationSeconds <= 0)
        {
            fireSale.TemporaryDurationSeconds = 300;
            changed = true;
        }

        var fundingTitle = fireSale.FundingRewardTitle;
        fireSale.FundingRewardTitle = string.IsNullOrWhiteSpace(fundingTitle) ? "Fire Sale Fund" : fundingTitle.Trim();
        changed |= !string.Equals(fundingTitle, fireSale.FundingRewardTitle, StringComparison.Ordinal);

        fireSale.FundingRewardDescription ??= string.Empty;

        var fundingCost = fireSale.FundingRewardCost;
        fireSale.FundingRewardCost = Math.Max(1, fundingCost <= 0 ? 100 : fundingCost);
        changed |= fundingCost != fireSale.FundingRewardCost;

        var fundingCooldown = fireSale.FundingRewardCooldownSeconds;
        fireSale.FundingRewardCooldownSeconds = Math.Max(0, fundingCooldown);
        changed |= fundingCooldown != fireSale.FundingRewardCooldownSeconds;

        var fundingReadyColor = fireSale.FundingRewardReadyColor;
        fireSale.FundingRewardReadyColor = ManagedRewardPresentation.NormalizeReadyBackgroundColor(fundingReadyColor);
        changed |= !string.Equals(fundingReadyColor, fireSale.FundingRewardReadyColor, StringComparison.OrdinalIgnoreCase);

        var fundingCooldownColor = fireSale.FundingRewardCooldownColor;
        fireSale.FundingRewardCooldownColor = ManagedRewardPresentation.NormalizeCooldownBackgroundColor(fundingCooldownColor);
        changed |= !string.Equals(fundingCooldownColor, fireSale.FundingRewardCooldownColor, StringComparison.OrdinalIgnoreCase);

        var conversion = fireSale.RewardPointsPerProgressUnit;
        fireSale.RewardPointsPerProgressUnit = Math.Max(1, conversion <= 0 ? 10 : conversion);
        changed |= conversion != fireSale.RewardPointsPerProgressUnit;
    }

    private void RestoreStartupState()
    {
        if (!fireSale.IsSaleActive)
        {
            return;
        }

        if (fireSale.SaleMode == RewardFireSaleMode.Temporary
            && fireSale.ActiveUntilUtc is { } activeUntil
            && activeUntil <= DateTimeOffset.UtcNow)
        {
            fireSale.IsSaleActive = false;
            fireSale.ActiveDiscountPercent = 0;
            fireSale.ActiveTierGoalAmount = 0;
            fireSale.ActiveUntilUtc = null;
            AppendLog("Reward Fire Sale expired while Crystal Relay was closed. Normal reward prices will be restored on the next reward sync.");
        }
    }

    private void EnsureTierExists()
    {
        if (fireSale.Tiers.Count > 0)
        {
            return;
        }

        fireSale.Tiers.Add(new RewardFireSaleTier());
    }

    private void AddTier()
    {
        var lastTier = fireSale.Tiers
            .OrderBy(tier => tier.GoalAmount)
            .LastOrDefault();
        var nextGoal = lastTier is null ? 5000 : Math.Max(1, lastTier.GoalAmount + 5000);
        var nextDiscount = lastTier is null ? 25 : Math.Clamp(lastTier.DiscountPercent + 10, 1, 100);
        fireSale.Tiers.Add(new RewardFireSaleTier
        {
            GoalAmount = nextGoal,
            DiscountPercent = nextDiscount
        });
        AppendLog($"Added Reward Fire Sale tier {nextGoal:N0} = {nextDiscount}% off.");
    }

    private void RemoveTier(object? target)
    {
        if (target is not RewardFireSaleTier tier || fireSale.Tiers.Count <= 1)
        {
            return;
        }

        fireSale.Tiers.Remove(tier);
        AppendLog($"Removed Reward Fire Sale tier {tier.GoalAmount:N0} = {tier.DiscountPercent}% off.");
    }

    private void ResetProgress()
    {
        fireSale.CurrentProgress = 0;
        RefreshStateProperties();
        QueueSave();
        AppendLog("Reward Fire Sale progress reset.");
    }

    private void StopSale() => StopSale(expired: false);

    private void StopSale(bool expired)
    {
        if (!fireSale.IsSaleActive)
        {
            return;
        }

        CancelAndDisposeQueuedCancellationSource(ref rewardFireSaleExpirationCancellation);
        fireSale.IsSaleActive = false;
        fireSale.ActiveDiscountPercent = 0;
        fireSale.ActiveTierGoalAmount = 0;
        fireSale.ActiveUntilUtc = null;
        RefreshStateProperties();
        QueueSave(0);
        QueueManagedRewardSync(0, MainWindowViewModel.ManagedRewardSyncReason.FireSaleChanged);
        AppendLog(expired
            ? "Reward Fire Sale ended and Crystal Relay queued normal reward prices to restore."
            : "Reward Fire Sale stopped and Crystal Relay queued normal reward prices to restore.");
    }

    private void ActivateIfGoalReached()
    {
        var reachedTier = GetReachedTier();
        if (reachedTier is null)
        {
            return;
        }

        var saleWasActive = IsActiveNow();
        if (saleWasActive
            && reachedTier.GoalAmount <= fireSale.ActiveTierGoalAmount
            && reachedTier.DiscountPercent <= fireSale.ActiveDiscountPercent)
        {
            return;
        }

        suppressRewardFireSaleChangeSideEffects = true;
        try
        {
            fireSale.IsSaleActive = true;
            fireSale.ActiveDiscountPercent = reachedTier.DiscountPercent;
            fireSale.ActiveTierGoalAmount = reachedTier.GoalAmount;
            if (!saleWasActive)
            {
                fireSale.ActiveUntilUtc = fireSale.SaleMode == RewardFireSaleMode.Temporary
                    ? DateTimeOffset.UtcNow.AddSeconds(Math.Max(1, fireSale.TemporaryDurationSeconds))
                    : null;
            }

            if (!fireSale.MultiTierEnabled || IsFinalTier(reachedTier))
            {
                fireSale.CurrentProgress = 0;
            }
        }
        finally
        {
            suppressRewardFireSaleChangeSideEffects = false;
        }

        if (!saleWasActive)
        {
            ScheduleFireSaleExpiration();
        }

        RefreshStateProperties();
        QueueSave(0);
        QueueManagedRewardSync(0, MainWindowViewModel.ManagedRewardSyncReason.FireSaleChanged);
        AppendLog(saleWasActive
            ? $"Reward Fire Sale upgraded: {reachedTier.DiscountPercent}% off from the {reachedTier.GoalAmount:N0} goal tier."
            : $"Reward Fire Sale started: {reachedTier.DiscountPercent}% off Crystal Relay-owned VRC rewards.");
    }

    private bool IsActiveNow()
    {
        if (!fireSale.IsEnabled || !fireSale.IsSaleActive || fireSale.ActiveDiscountPercent <= 0)
        {
            return false;
        }

        return fireSale.SaleMode != RewardFireSaleMode.Temporary
            || fireSale.ActiveUntilUtc is null
            || fireSale.ActiveUntilUtc > DateTimeOffset.UtcNow;
    }

    private bool IsFinalTier(RewardFireSaleTier tier)
    {
        var finalTier = GetFinalTier();
        return finalTier is not null && finalTier.GoalAmount == tier.GoalAmount;
    }

    private bool CanAdvanceToLaterTier()
    {
        if (!fireSale.MultiTierEnabled || !IsActiveNow())
        {
            return false;
        }

        var finalTier = GetFinalTier();
        return finalTier is not null
            && fireSale.ActiveTierGoalAmount < finalTier.GoalAmount
            && fireSale.CurrentProgress < finalTier.GoalAmount;
    }

    private RewardFireSaleTier? GetReachedTier()
    {
        var eligibleTiers = GetValidTiers()
            .Where(tier => fireSale.CurrentProgress >= tier.GoalAmount)
            .ToArray();
        if (eligibleTiers.Length == 0)
        {
            return null;
        }

        return fireSale.MultiTierEnabled
            ? eligibleTiers.OrderByDescending(tier => tier.GoalAmount).First()
            : eligibleTiers.OrderBy(tier => tier.GoalAmount).First();
    }

    private RewardFireSaleTier? GetNextTier()
    {
        return GetValidTiers()
            .Where(tier => fireSale.CurrentProgress < tier.GoalAmount)
            .OrderBy(tier => tier.GoalAmount)
            .FirstOrDefault()
            ?? GetValidTiers().OrderByDescending(tier => tier.GoalAmount).FirstOrDefault();
    }

    private RewardFireSaleTier? GetFinalTier() =>
        GetValidTiers().OrderByDescending(tier => tier.GoalAmount).FirstOrDefault();

    private IReadOnlyList<RewardFireSaleTier> GetValidTiers()
    {
        EnsureTierExists();
        return fireSale.Tiers
            .Where(tier => tier.GoalAmount > 0 && tier.DiscountPercent > 0)
            .OrderBy(tier => tier.GoalAmount)
            .ToArray();
    }

    private void ExpireIfNeeded()
    {
        if (!fireSale.IsSaleActive
            || fireSale.SaleMode != RewardFireSaleMode.Temporary
            || fireSale.ActiveUntilUtc is not { } activeUntil
            || activeUntil > DateTimeOffset.UtcNow)
        {
            return;
        }

        StopSale(expired: true);
    }

    private void ScheduleFireSaleExpiration()
    {
        CancelAndDisposeQueuedCancellationSource(ref rewardFireSaleExpirationCancellation);
        if (!fireSale.IsSaleActive
            || fireSale.SaleMode != RewardFireSaleMode.Temporary
            || fireSale.ActiveUntilUtc is not { } activeUntil)
        {
            return;
        }

        var delay = activeUntil - DateTimeOffset.UtcNow;
        if (delay <= TimeSpan.Zero)
        {
            StopSale(expired: true);
            return;
        }

        rewardFireSaleExpirationCancellation = new CancellationTokenSource();
        var cancellationToken = rewardFireSaleExpirationCancellation.Token;
        _ = Task.Run(async () =>
        {
            try
            {
                await Task.Delay(delay, cancellationToken);
                RunOnUi(() => StopSale(expired: true));
            }
            catch (OperationCanceledException)
            {
            }
        }, CancellationToken.None);
    }

    private int ResolveContributionAmount(RewardFireSaleContribution c)
    {
        if (c.Type == RewardFireSaleContributionType.Bits)
        {
            return fireSale.CountBits ? Math.Max(0, c.Amount) : 0;
        }

        if (c.Type == RewardFireSaleContributionType.CashPayment)
        {
            return fireSale.CountCashPayments ? Math.Max(0, c.Amount) * fireSale.CashPaymentProgressRatio : 0;
        }

        if (!fireSale.FundingRewardEnabled || !IsFundingReward(c.RewardId, c.RewardTitle))
        {
            return 0;
        }

        return GetFundingProgressPerRedeem();
    }

    private bool IsFundingReward(string? rewardId, string? rewardTitle)
    {
        var savedRewardId = fireSale.FundingRewardId?.Trim() ?? string.Empty;
        var incomingRewardId = rewardId?.Trim() ?? string.Empty;
        if (!string.IsNullOrWhiteSpace(savedRewardId)
            && string.Equals(savedRewardId, incomingRewardId, StringComparison.Ordinal))
        {
            return true;
        }

        return ManagedRewardPresentation.HasSameTitleIdentity(rewardTitle, fireSale.FundingRewardTitle);
    }

    private int GetFundingProgressPerRedeem()
    {
        return Math.Max(1, (int)Math.Floor(Math.Max(1, fireSale.FundingRewardCost) / (double)Math.Max(1, fireSale.RewardPointsPerProgressUnit)));
    }

    private void StartFundingRewardCooldown()
    {
        var cooldownSeconds = Math.Max(0, fireSale.FundingRewardCooldownSeconds);
        if (cooldownSeconds <= 0)
        {
            ClearFundingRewardCooldown(queueSync: false);
            return;
        }

        rewardFireSaleFundingRewardCooldownUntil = DateTimeOffset.UtcNow.AddSeconds(cooldownSeconds);
        ScheduleFundingRewardCooldownEnd(cooldownSeconds);
        QueueManagedRewardSync(0, MainWindowViewModel.ManagedRewardSyncReason.FireSaleChanged);
    }

    private bool IsFundingRewardOnCooldown()
    {
        if (rewardFireSaleFundingRewardCooldownUntil is not { } cooldownUntil)
        {
            return false;
        }

        if (cooldownUntil > DateTimeOffset.UtcNow
            && fireSale.FundingRewardCooldownSeconds > 0)
        {
            return true;
        }

        ClearFundingRewardCooldown(queueSync: false);
        return false;
    }

    private void ClearFundingRewardCooldown(bool queueSync)
    {
        rewardFireSaleFundingRewardCooldownUntil = null;
        CancelAndDisposeQueuedCancellationSource(ref rewardFireSaleFundingCooldownCancellation);
        if (queueSync)
        {
            QueueManagedRewardSync(0, MainWindowViewModel.ManagedRewardSyncReason.FireSaleChanged);
        }
    }

    private void ScheduleFundingRewardCooldownEnd(int cooldownSeconds)
    {
        var cooldownCancellation = ReplaceQueuedCancellationSource(ref rewardFireSaleFundingCooldownCancellation);
        _ = Task.Run(async () =>
        {
            try
            {
                await Task.Delay(TimeSpan.FromSeconds(Math.Max(1, cooldownSeconds)), cooldownCancellation.Token);
                RunOnUi(() =>
                {
                    rewardFireSaleFundingRewardCooldownUntil = null;
                    QueueManagedRewardSync(0, MainWindowViewModel.ManagedRewardSyncReason.FireSaleChanged);
                });
            }
            catch (OperationCanceledException)
            {
            }
            finally
            {
                DisposeCompletedQueuedCancellationSource(ref rewardFireSaleFundingCooldownCancellation, cooldownCancellation);
            }
        }, CancellationToken.None);
    }

    private string BuildFundingRewardPrompt()
    {
        var configuredDescription = fireSale.FundingRewardDescription?.Trim() ?? string.Empty;
        return string.IsNullOrWhiteSpace(configuredDescription)
            ? TF(
                "Adds {0:N0} Fire Sale progress toward the next discount goal.",
                GetFundingProgressPerRedeem())
            : configuredDescription;
    }

    private void RefreshStateProperties()
    {
        RaisePropertyChanged(nameof(StatusText));
        RaisePropertyChanged(nameof(ProgressPercent));
        RaisePropertyChanged(nameof(IsTemporary));
        RaisePropertyChanged(nameof(FundingRewardConversionText));
        RaisePropertyChanged(nameof(FundingRewardPrompt));
        if (StopSaleCommand is RelayCommand stopCmd)
            stopCmd.NotifyCanExecuteChanged();
        if (ResetProgressCommand is RelayCommand resetCmd)
            resetCmd.NotifyCanExecuteChanged();
        if (RemoveTierCommand is RelayCommand removeCmd)
            removeCmd.NotifyCanExecuteChanged();
    }

    private void OnRewardFireSaleChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (sender is not RewardFireSaleSettings changedSale)
        {
            return;
        }

        if (suppressRewardFireSaleChangeSideEffects)
        {
            return;
        }

        if (e.PropertyName == nameof(RewardFireSaleSettings.Tiers))
        {
            UnwireTiers(changedSale);
            WireTiers(changedSale);
        }

        if (e.PropertyName == nameof(RewardFireSaleSettings.IsEnabled)
            && !changedSale.IsEnabled
            && changedSale.IsSaleActive)
        {
            StopSale(expired: false);
        }

        if (e.PropertyName == nameof(RewardFireSaleSettings.SaleMode))
        {
            RaisePropertyChanged(nameof(IsTemporary));
            if (changedSale.IsSaleActive)
            {
                if (changedSale.SaleMode == RewardFireSaleMode.Temporary
                    && (changedSale.ActiveUntilUtc is null || changedSale.ActiveUntilUtc <= DateTimeOffset.UtcNow))
                {
                    changedSale.ActiveUntilUtc = DateTimeOffset.UtcNow.AddSeconds(Math.Max(1, changedSale.TemporaryDurationSeconds));
                }

                ScheduleFireSaleExpiration();
            }
        }

        if (e.PropertyName == nameof(RewardFireSaleSettings.FundingRewardCooldownSeconds)
            && changedSale.FundingRewardCooldownSeconds <= 0)
        {
            ClearFundingRewardCooldown(queueSync: true);
        }

        if (e.PropertyName == nameof(RewardFireSaleSettings.IsSaleActive)
            || e.PropertyName == nameof(RewardFireSaleSettings.ActiveDiscountPercent)
            || e.PropertyName == nameof(RewardFireSaleSettings.ActiveTierGoalAmount)
            || e.PropertyName == nameof(RewardFireSaleSettings.ActiveUntilUtc)
            || e.PropertyName == nameof(RewardFireSaleSettings.CurrentProgress)
            || e.PropertyName == nameof(RewardFireSaleSettings.IsEnabled)
            || e.PropertyName == nameof(RewardFireSaleSettings.FundingRewardCost)
            || e.PropertyName == nameof(RewardFireSaleSettings.FundingRewardCooldownSeconds)
            || e.PropertyName == nameof(RewardFireSaleSettings.FundingRewardDescription)
            || e.PropertyName == nameof(RewardFireSaleSettings.RewardPointsPerProgressUnit))
        {
            RefreshStateProperties();
        }

        if (e.PropertyName == nameof(RewardFireSaleSettings.IsSaleActive)
            || e.PropertyName == nameof(RewardFireSaleSettings.ActiveDiscountPercent)
            || e.PropertyName == nameof(RewardFireSaleSettings.IsEnabled)
            || e.PropertyName == nameof(RewardFireSaleSettings.FundingRewardEnabled)
            || e.PropertyName == nameof(RewardFireSaleSettings.FundingRewardTitle)
            || e.PropertyName == nameof(RewardFireSaleSettings.FundingRewardCost)
            || e.PropertyName == nameof(RewardFireSaleSettings.FundingRewardCooldownSeconds)
            || e.PropertyName == nameof(RewardFireSaleSettings.FundingRewardDescription)
            || e.PropertyName == nameof(RewardFireSaleSettings.FundingRewardReadyColor)
            || e.PropertyName == nameof(RewardFireSaleSettings.FundingRewardCooldownColor)
            || e.PropertyName == nameof(RewardFireSaleSettings.RewardPointsPerProgressUnit))
        {
            QueueManagedRewardSync(0, MainWindowViewModel.ManagedRewardSyncReason.FireSaleChanged);
        }

        QueueSave();
    }

    private void OnRewardFireSaleTiersCollectionChanged(object? sender, NotifyCollectionChangedEventArgs e)
    {
        if (e.NewItems is not null)
        {
            foreach (RewardFireSaleTier tier in e.NewItems)
            {
                tier.PropertyChanged += OnRewardFireSaleTierChanged;
            }
        }

        if (e.OldItems is not null)
        {
            foreach (RewardFireSaleTier tier in e.OldItems)
            {
                tier.PropertyChanged -= OnRewardFireSaleTierChanged;
            }
        }

        RefreshStateProperties();
        QueueSave();
    }

    private void OnRewardFireSaleTierChanged(object? sender, PropertyChangedEventArgs e)
    {
        RefreshStateProperties();
        QueueSave();
    }

    private void WireTiers(RewardFireSaleSettings sale)
    {
        sale.PropertyChanged += OnRewardFireSaleChanged;
        sale.Tiers.CollectionChanged += OnRewardFireSaleTiersCollectionChanged;
        foreach (var tier in sale.Tiers)
        {
            tier.PropertyChanged += OnRewardFireSaleTierChanged;
        }
    }

    private void UnwireTiers(RewardFireSaleSettings sale)
    {
        sale.PropertyChanged -= OnRewardFireSaleChanged;
        sale.Tiers.CollectionChanged -= OnRewardFireSaleTiersCollectionChanged;
        foreach (var tier in sale.Tiers)
        {
            tier.PropertyChanged -= OnRewardFireSaleTierChanged;
        }
    }

    private static string T(string sourceText) => LocalizationService.Translate(sourceText);
    private static string TF(string sourceFormat, params object[] args) => LocalizationService.Format(sourceFormat, args);

    private void RunOnUi(Action action)
    {
        var dispatcher = System.Windows.Application.Current?.Dispatcher;
        if (dispatcher is null || dispatcher.CheckAccess())
        {
            action();
            return;
        }
        dispatcher.InvokeAsync(action);
    }

    private bool RunOnUi(Func<bool> action)
    {
        var dispatcher = System.Windows.Application.Current?.Dispatcher;
        if (dispatcher is null || dispatcher.CheckAccess())
        {
            return action();
        }
        return dispatcher.Invoke(action);
    }

    private void QueueSave(int delayMilliseconds = 500) => mainWindowViewModel?.QueueSave(delayMilliseconds);
    private void QueueManagedRewardSync(int delayMilliseconds = 1100, MainWindowViewModel.ManagedRewardSyncReason reason = MainWindowViewModel.ManagedRewardSyncReason.SettingsEdit) => mainWindowViewModel?.QueueManagedRewardSync(delayMilliseconds, reason);
    private void AppendLog(string message) => mainWindowViewModel?.AppendLog(message);
    private void AppendThrottledLog(string key, string message) => mainWindowViewModel?.AppendThrottledLog(key, message, ThrottledLogWindow);

    private void CancelAndDisposeQueuedCancellationSource(ref CancellationTokenSource? cts)
    {
        cts?.Cancel();
        cts?.Dispose();
        cts = null;
    }

    private CancellationTokenSource ReplaceQueuedCancellationSource(ref CancellationTokenSource? cts)
    {
        CancelAndDisposeQueuedCancellationSource(ref cts);
        cts = new CancellationTokenSource();
        return cts;
    }

    private void DisposeCompletedQueuedCancellationSource(ref CancellationTokenSource? cts, CancellationTokenSource completedSource)
    {
        if (cts == completedSource)
        {
            cts = null;
            completedSource.Dispose();
        }
    }

    public void Dispose()
    {
        if (disposed) return;
        disposed = true;
        fireSale.PropertyChanged -= OnRewardFireSaleChanged;
        fireSale.Tiers.CollectionChanged -= OnRewardFireSaleTiersCollectionChanged;
        foreach (var tier in fireSale.Tiers)
            tier.PropertyChanged -= OnRewardFireSaleTierChanged;
        if (mainWindowViewModel?.BridgeCoordinator is not null)
            mainWindowViewModel.BridgeCoordinator.RewardFireSaleContributionReceived -= OnContributionReceived;
        CancelAndDisposeQueuedCancellationSource(ref rewardFireSaleExpirationCancellation);
        CancelAndDisposeQueuedCancellationSource(ref rewardFireSaleFundingCooldownCancellation);
    }
}
