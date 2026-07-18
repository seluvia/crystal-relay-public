# Reward Fire Sale Popup Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Extract the existing Reward Fire Sale from MainWindow into a dedicated popup window with a new ViewModel, add Cash Payment contribution support, and follow the app's theme system.

**Architecture:** New `RewardFireSaleManagerWindow` + `RewardFireSaleManagerViewModel` follow the pattern of `CashPaymentManagerWindow`/`CashPaymentManagerViewModel`. The ViewModel owns all Fire Sale logic (moved from MainWindowViewModel) and delegates back to MainWindowViewModel for save/sync/logging operations. A new `CashPayment` contribution type is added to the `RewardFireSaleContributionType` enum.

**Tech Stack:** C#, WPF/XAML, .NET 10, DynamicResource theming, ObservableObject pattern

## Global Constraints

- All XAML styling uses `{DynamicResource ...}` bindings — no hardcoded colors
- Window chrome: `WindowStyle="None"` with `shell:WindowChrome`, custom title bar with drag/close/minimize
- Theme lifecycle: `ThemeManager.ApplyToResources(Resources, ThemeManager.CurrentTheme)` in constructor, subscribe to `ThemeManager.ThemeChanged`, unsubscribe in `OnWindowClosed`
- Non-modal window, resizable, `WindowStartupLocation="CenterOwner"`, `Owner = Application.Current?.MainWindow`
- New ViewModel implements `ObservableObject` + `IDisposable`
- Cash Payment contribution: **new** `RewardFireSaleContributionType.CashPayment` enum member, ratio-based progress calculation
- All new settings nullable in `Persisted*` DTO for backward compatibility (defaults apply on deserialize)
- Localization entries added to all `*.extra.json` files (en-US, es-ES, fr-FR, it-IT, ko-KR)

---
### Task 1: Add model properties and CashPayment contribution type

**Files:**
- Modify: `VrcTwitchOscBridge\Models\RewardFireSaleSettings.cs:54-80` (add two new properties)
- Modify: `VrcTwitchOscBridge\Services\BridgeCoordinator.cs:19` (add enum member)
- Modify: `VrcTwitchOscBridge\Services\SettingsStore.cs:1869-1943` (serialization)
- Modify: `VrcTwitchOscBridge\Services\SettingsStore.cs:3705-3749` (persisted DTO)

**Interfaces:**
- Consumes: Existing `RewardFireSaleSettings` model, existing `RewardFireSaleContributionType` enum
- Produces: `RewardFireSaleSettings.CountCashPayments` (bool, default false), `RewardFireSaleSettings.CashPaymentProgressRatio` (int, default 100), `RewardFireSaleContributionType.CashPayment` enum member

- [ ] **Step 1: Add new properties to RewardFireSaleSettings**

    In `Models\RewardFireSaleSettings.cs`, add after the existing field declarations at line 68:

    ```csharp
    private bool countCashPayments;
    private int cashPaymentProgressRatio = 100;
    ```

    Then add the public properties after line 104 (`DiscountManagedPowerUpsEnabled`):

    ```csharp
    public bool CountCashPayments
    {
        get => countCashPayments;
        set => SetProperty(ref countCashPayments, value);
    }

    public int CashPaymentProgressRatio
    {
        get => cashPaymentProgressRatio;
        set => SetProperty(ref cashPaymentProgressRatio, Math.Max(1, value));
    }
    ```

- [ ] **Step 2: Add CashPayment to the enum**

    In `Services\BridgeCoordinator.cs`, change line 19:

    ```csharp
    public enum RewardFireSaleContributionType
    {
        Bits,
        ManagedReward,
        CashPayment
    }
    ```

- [ ] **Step 3: Add new fields to PersistedRewardFireSaleSettings**

    In `Services\SettingsStore.cs`, in the `PersistedRewardFireSaleSettings` class (line 3705), add after `DiscountManagedPowerUpsEnabled`:

    ```csharp
    public bool? CountCashPayments { get; set; }
    public int CashPaymentProgressRatio { get; set; }
    ```

- [ ] **Step 4: Update ToPersistedRewardFireSaleSettings**

    In `Services\SettingsStore.cs`, add to the `ToPersistedRewardFireSaleSettings` method (line 1869):

    ```csharp
    CountCashPayments = settings.CountCashPayments,
    CashPaymentProgressRatio = settings.CashPaymentProgressRatio,
    ```

- [ ] **Step 5: Update ToRewardFireSaleSettings**

    In `Services\SettingsStore.cs`, add to the `ToRewardFireSaleSettings` method (line 1906):

    ```csharp
    CountCashPayments = settings.CountCashPayments ?? false,
    CashPaymentProgressRatio = settings.CashPaymentProgressRatio <= 0 ? 100 : settings.CashPaymentProgressRatio,
    ```

- [ ] **Step 6: Build to verify**

    ```powershell
    dotnet build "E:\!!!Program to work on\Proper Crystal Relay\VrcTwitchOscBridge\VrcTwitchOscBridge.csproj" --no-restore
    ```

---
### Task 2: Add Cash Payment contribution to BridgeCoordinator

**Files:**
- Modify: `VrcTwitchOscBridge\Services\BridgeCoordinator.cs:3568-3656`

**Interfaces:**
- Consumes: `RewardFireSaleSettings.CountCashPayments`, `RewardFireSaleSettings.CashPaymentProgressRatio`, `config.RewardFireSale`
- Produces: Fires `RewardFireSaleContributionReceived` with `CashPayment` type from `HandleCashPaymentEventAsync`

- [ ] **Step 1: Add Fire Sale contribution check in HandleCashPaymentEventAsync**

    In `Services\BridgeCoordinator.cs`, at the end of `HandleCashPaymentEventAsync` (after the `foreach` loop, before the closing brace at line 3656), add:

    ```csharp
    if (configuration.RewardFireSale.CountCashPayments)
    {
        var cashAmountUnits = Math.Max(1, (int)Math.Floor(paymentEvent.Amount));
        var cashContribution = new RewardFireSaleContribution(
            RewardFireSaleContributionType.CashPayment,
            cashAmountUnits,
            null,
            null,
            string.IsNullOrWhiteSpace(paymentEvent.UserDisplayName) ? "Cash supporter" : paymentEvent.UserDisplayName);
        RewardFireSaleContributionReceived?.Invoke(cashContribution);
    }
    ```

- [ ] **Step 2: Build to verify**

    ```powershell
    dotnet build "E:\!!!Program to work on\Proper Crystal Relay\VrcTwitchOscBridge\VrcTwitchOscBridge.csproj" --no-restore
    ```

---
### Task 3: Create RewardFireSaleManagerViewModel

**Files:**
- Create: `ViewModels\RewardFireSaleManagerViewModel.cs`
- Modify: `VrcTwitchOscBridge\ViewModels\MainWindowViewModel.cs` (delegate calls — keep ApplyRewardFireSaleDiscount in MainWindowViewModel)

**Interfaces:**
- Consumes: `AppSettings.RewardFireSale`, `MainWindowViewModel` for save/sync/log/cancellation helpers
- Produces: All Fire Sale commands, computed properties, event handlers now on this ViewModel

- [ ] **Step 1: Create RewardFireSaleManagerViewModel**

    Create `ViewModels\RewardFireSaleManagerViewModel.cs`:

    ```csharp
    using System;
    using System.Collections.ObjectModel;
    using System.ComponentModel;
    using System.Linq;
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

        private static readonly Guid RewardFireSaleFundingRewardOwnerId = new("f31cdb57-052f-4dd4-96d3-1c2b044e2fd9");
        private static readonly TimeSpan ThrottledLogWindow = TimeSpan.FromSeconds(30);

        public RewardFireSaleSettings Settings { get; }

        public RewardFireSaleManagerViewModel(AppSettings appSettings, MainWindowViewModel? mainWindowViewModel)
        {
            ArgumentNullException.ThrowIfNull(appSettings);
            this.mainWindowViewModel = mainWindowViewModel;
            Settings = appSettings.RewardFireSale;
            fireSale = Settings;

            // Wire up property change handlers
            fireSale.PropertyChanged += OnRewardFireSaleChanged;
            fireSale.Tiers.CollectionChanged += OnRewardFireSaleTiersCollectionChanged;
            foreach (var tier in fireSale.Tiers)
                tier.PropertyChanged += OnRewardFireSaleTierChanged;

            // Wire up contribution event
            if (mainWindowViewModel?.BridgeCoordinator is not null)
            {
                mainWindowViewModel.BridgeCoordinator.RewardFireSaleContributionReceived += OnContributionReceived;
            }

            NormalizeSettings();
            RestoreStartupState();
            EnsureTierExists();
            ScheduleFireSaleExpiration();
            RefreshStateProperties();
        }

        // --- Computed Properties ---

        public bool IsTemporary => fireSale.SaleMode == RewardFireSaleMode.Temporary;

        public IReadOnlyList<RewardFireSaleModeOption> ModeOptions { get; } =
        [
            new RewardFireSaleModeOption(RewardFireSaleMode.Temporary, Resources["Temporary"] ?? "Temporary"),
            new RewardFireSaleModeOption(RewardFireSaleMode.Permanent, Resources["Permanent"] ?? "Permanent")
        ];

        public string StatusText { get; private set; } = string.Empty;
        public double ProgressPercent { get; private set; }
        public string ActiveWarningText { get; private set; } = string.Empty;
        public string FundingRewardConversionText { get; private set; } = string.Empty;
        public string FundingRewardPrompt { get; private set; } = string.Empty;

        // --- Commands ---

        public System.Windows.Input.ICommand AddTierCommand { get; }
        public System.Windows.Input.ICommand RemoveTierCommand { get; }
        public System.Windows.Input.ICommand StopSaleCommand { get; }
        public System.Windows.Input.ICommand ResetProgressCommand { get; }

        // --- Event Handlers ---

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
                    "Reward Fire Sale is already active at its final available tier, so new Bits and funding reward redeems are not adding progress right now.",
                    ThrottledRewardSyncLogWindow);
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

        // --- Methods (same logic as MainWindowViewModel.RewardFireSale* methods) ---

        private void AddTier() { /* same as AddRewardFireSaleTier */ }
        private void RemoveTier(object? target) { /* same */ }
        private void ResetProgress() { /* same */ }
        private void StopSale() => StopSale(expired: false);
        private void StopSale(bool expired) { /* same */ }
        private void ActivateIfGoalReached() { /* same */ }
        private bool IsActiveNow() { /* same */ }
        private bool IsFinalTier(RewardFireSaleTier tier) { /* same */ }
        private bool CanAdvanceToLaterTier() { /* same */ }
        private RewardFireSaleTier? GetReachedTier() { /* same */ }
        private RewardFireSaleTier? GetNextTier() { /* same */ }
        private RewardFireSaleTier? GetFinalTier() { /* same */ }
        private void ExpireIfNeeded() { /* same */ }
        private void ScheduleFireSaleExpiration() { /* same */ }
        private int ResolveContributionAmount(RewardFireSaleContribution c)
        {
            if (c.Type == RewardFireSaleContributionType.Bits)
                return fireSale.CountBits ? Math.Max(0, c.Amount) : 0;

            if (c.Type == RewardFireSaleContributionType.CashPayment)
                return fireSale.CountCashPayments ? Math.Max(0, c.Amount) * fireSale.CashPaymentProgressRatio : 0;

            if (!fireSale.FundingRewardEnabled || !IsFundingReward(c.RewardId, c.RewardTitle))
                return 0;

            return GetFundingProgressPerRedeem();
        }
        private bool IsFundingReward(string? id, string? title) { /* same */ }
        private int GetFundingProgressPerRedeem() { /* same */ }
        private void StartFundingRewardCooldown() { /* same */ }
        private bool IsFundingRewardOnCooldown() { /* same */ }
        private void ClearFundingRewardCooldown(bool queueSync) { /* same */ }
        private void ScheduleFundingRewardCooldownEnd(int sec) { /* same */ }
        private void RefreshStateProperties() { /* same */ }
        private void NormalizeSettings() { /* same */ }
        private void RestoreStartupState() { /* same */ }
        private void EnsureTierExists() { /* same */ }
        private IReadOnlyList<RewardFireSaleTier> GetValidTiers() { /* same */ }

        // --- Property Change Handlers ---

        private void OnRewardFireSaleChanged(object? sender, PropertyChangedEventArgs e) { /* same as RewardFireSaleChanged */ }
        private void OnRewardFireSaleTiersCollectionChanged(object? sender, System.Collections.Specialized.NotifyCollectionChangedEventArgs e) { /* same */ }
        private void OnRewardFireSaleTierChanged(object? sender, PropertyChangedEventArgs e) { /* same */ }

        // --- Helpers (delegate to MainWindowViewModel) ---

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
        private void QueueManagedRewardSync(int delayMilliseconds = 1100, ManagedRewardSyncReason reason = ManagedRewardSyncReason.SettingsEdit) => mainWindowViewModel?.QueueManagedRewardSync(delayMilliseconds, reason);
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
    ```

    This is a structural overview — the actual file will contain all the copy-pasted logic from MainWindowViewModel with the following changes:
    - `ResolveContributionAmount` adds handling for `RewardFireSaleContributionType.CashPayment`
    - All `mainWindowViewModel` calls go through delegation helpers
    - No `isInitialized` / `isShuttingDown` checks (ViewModel is only alive while window is open)

- [ ] **Step 2: Build to verify**

    ```powershell
    dotnet build "E:\!!!Program to work on\Proper Crystal Relay\VrcTwitchOscBridge\VrcTwitchOscBridge.csproj" --no-restore
    ```

---
### Task 4: Create RewardFireSaleManagerWindow

**Files:**
- Create: `RewardFireSaleManagerWindow.xaml`
- Create: `RewardFireSaleManagerWindow.xaml.cs`
- Modify: `VrcTwitchOscBridge\VrcTwitchOscBridge.csproj` (add new XAML files as Page)

**Interfaces:**
- Consumes: `RewardFireSaleManagerViewModel`
- Produces: A themed, resizable popup window with the Fire Sale UI

- [ ] **Step 1: Create RewardFireSaleManagerWindow.xaml.cs**

    ```csharp
    using System;
    using System.Windows;
    using System.Windows.Input;
    using VrcTwitchOscBridge.Services;
    using VrcTwitchOscBridge.ViewModels;

    namespace VrcTwitchOscBridge;

    public partial class RewardFireSaleManagerWindow : Window
    {
        public RewardFireSaleManagerWindow(RewardFireSaleManagerViewModel viewModel)
        {
            DataContext = viewModel ?? throw new ArgumentNullException(nameof(viewModel));
            InitializeComponent();
            ThemeManager.ApplyToResources(Resources, ThemeManager.CurrentTheme);
            ThemeManager.ThemeChanged += OnThemeChanged;
            Closed += OnClosed;
        }

        private void OnTitleBarMouseDown(object sender, MouseButtonEventArgs e)
        {
            if (e.ChangedButton != MouseButton.Left || e.ButtonState != MouseButtonState.Pressed)
                return;
            try { DragMove(); }
            catch (InvalidOperationException) { }
        }

        private void OnCloseClicked(object sender, RoutedEventArgs e) => Close();
        private void OnMinimizeClicked(object sender, RoutedEventArgs e) => WindowState = WindowState.Minimized;

        private void OnThemeChanged(object? sender, EventArgs e)
        {
            Dispatcher.BeginInvoke(() => ThemeManager.ApplyToResources(Resources));
        }

        private void OnClosed(object? sender, EventArgs e)
        {
            ThemeManager.ThemeChanged -= OnThemeChanged;
            Closed -= OnClosed;
            if (DataContext is IDisposable disposable)
                disposable.Dispose();
        }
    }
    ```

- [ ] **Step 2: Create RewardFireSaleManagerWindow.xaml**

    Full XAML for the popup window with these sections:

    **Window chrome:**
    - Width 720, Height 680, MinWidth 540, MinHeight 480
    - `WindowStyle="None"`, `WindowStartupLocation="CenterOwner"`, `ResizeMode="CanResize"`
    - `shell:WindowChrome CaptionHeight="0" CornerRadius="0" GlassFrameThickness="0" ResizeBorderThickness="6"`
    - All brushes use `{DynamicResource ...}` — no hardcoded colors
    - Custom title bar with `DragMove()` on mouse down, close and minimize buttons

    **Title bar:**
    - "Reward Fire Sale" text, themed close/minimize buttons
    - Icon from app assets

    **Status section:**
    - Border with status text, progress bar, Reset/Stop buttons
    - ProgressBar with `{DynamicResource AccentBrush}` foreground
    - Uses bound properties: `StatusText`, `ProgressPercent`

    **Goal Sources section:**
    - CheckBox `CountBits` → `{Binding Settings.CountCashPayments}` (wait, that's wrong — CountBits should bind to Settings.CountBits)
    - CheckBox `CountCashPayments` → `{Binding Settings.CountCashPayments}`
    - CheckBox `MultiTierEnabled` → `{Binding Settings.MultiTierEnabled}`

    **Funding Sources (two-column grid):**

    Left — **Channel Point Funding Reward:**
    - CheckBox `FundingRewardEnabled` to enable/disable the section
    - When disabled, the section fields are dimmed or collapsed
    - Reward Name textbox → `Settings.FundingRewardTitle`
    - Cost textbox → `Settings.FundingRewardCost`
    - Cooldown textbox → `Settings.FundingRewardCooldownSeconds`
    - Points per progress textbox → `Settings.RewardPointsPerProgressUnit`
    - Conversion text → `FundingRewardConversionText`
    - Description textarea → `Settings.FundingRewardDescription`
    - Auto-prompt preview → `FundingRewardPrompt`
    - Ready/Cooldown color swatches with Choose Color buttons (same pattern as existing MainWindow.xaml lines 3977-4036)

    Right — **Cash Payments:**
    - Ratio textbox → `Settings.CashPaymentProgressRatio`
    - Example text (dynamically generated or static)
    - Connected services read-only display

    **Sale Mode section:**
    - ComboBox → `{Binding ModeOptions}` with `SelectedValue={Binding Settings.SaleMode}`
    - Duration textbox → `Settings.TemporaryDurationSeconds` (visible when Temporary)

    **Discount Tiers section:**
    - "Add Tier" button → `AddTierCommand`
    - ItemsControl bound to `Settings.Tiers` with same item template as existing MainWindow.xaml lines 4081-4142 (Goal Amount, Discount %, Delete button)

- [ ] **Step 3: Add XAML files to VrcTwitchOscBridge.csproj**

    The project has `EnableDefaultPageItems=false`, so add to the `.csproj`:

    ```xml
    <Page Include="RewardFireSaleManagerWindow.xaml">
      <Generator>MSBuild:Compile</Generator>
    </Page>
    ```

    (Note: the `.xaml.cs` file is automatically picked up by the build as a `Compile` item if it follows naming convention, otherwise add it explicitly.)

- [ ] **Step 4: Build to verify**

    ```powershell
    dotnet build "E:\!!!Program to work on\Proper Crystal Relay\VrcTwitchOscBridge\VrcTwitchOscBridge.csproj" --no-restore
    ```

---
### Task 5: Update MainWindowViewModel and MainWindow.xaml

**Files:**
- Modify: `VrcTwitchOscBridge\ViewModels\MainWindowViewModel.cs`
- Modify: `VrcTwitchOscBridge\MainWindow.xaml`

**Interfaces:**
- Consumes: `RewardFireSaleManagerWindow`, `RewardFireSaleManagerViewModel`
- Produces: Cleaned-up MainWindow with Fire Sale tab opening the popup; `BridgeCoordinator` property exposed for the new ViewModel

- [ ] **Step 1: Add opening command to MainWindowViewModel**

    Add a field and command (alongside existing ones like `_cashPaymentManagerWindow`):

    ```csharp
    private RewardFireSaleManagerWindow? _rewardFireSaleManagerWindow;

    private void OpenRewardFireSaleManager()
    {
        if (_rewardFireSaleManagerWindow is { IsVisible: true })
        {
            _rewardFireSaleManagerWindow.Activate();
            return;
        }

        var managerVm = new RewardFireSaleManagerViewModel(Settings, this);
        _rewardFireSaleManagerWindow = new RewardFireSaleManagerWindow(managerVm)
        {
            Owner = System.Windows.Application.Current?.MainWindow,
        };
        _rewardFireSaleManagerWindow.Closed += (_, _) => _rewardFireSaleManagerWindow = null;
        _rewardFireSaleManagerWindow.Show();
    }
    ```

    Register the command:
    ```csharp
    OpenRewardFireSaleManagerCommand = new RelayCommand(OpenRewardFireSaleManager);
    ```

    Add public property:
    ```csharp
    public RelayCommand OpenRewardFireSaleManagerCommand { get; }
    ```

    **Also: Expose necessary members for the new ViewModel** — make these private members `internal`:
    - `void AppendThrottledLog(string key, string message, TimeSpan throttleWindow)` (line 16669)
    - `void QueueSave(int delayMilliseconds = 500)` (line 9554)
    - `void QueueManagedRewardSync(int delayMilliseconds = 1100, ManagedRewardSyncReason reason = ManagedRewardSyncReason.SettingsEdit)` (line 10907)
    - `void AppendLog(string message)` (line 16649)

    **BridgeCoordinator access:**
    ```csharp
    public BridgeCoordinator BridgeCoordinator => bridgeCoordinator;
    ```

- [ ] **Step 2: Remove embedded Fire Sale code from MainWindowViewModel**

    Remove all of the following from `MainWindowViewModel.cs`:
    - Remove `RewardFireSaleFundingRewardOwnerId` field (moved to new VM)
    - Remove `suppressRewardFireSaleChangeSideEffects` field (moved)
    - Remove `rewardFireSaleExpirationCancellation`, `rewardFireSaleFundingCooldownCancellation`, `rewardFireSaleFundingRewardCooldownUntil` (moved)
    - Remove `ShowRewardFireSaleCommand` and `ShowRewardFireSale()` method
    - Remove `IsViewingRewardFireSale` property
    - Remove `IsRewardFireSaleTemporary` property
    - Remove `RewardFireSaleModeOptions` property
    - Remove all `RewardFireSale*` computed properties (lines 1444-1520)
    - Remove all `RewardFireSale*` commands (lines 3166-3172)
    - Remove `NormalizeRewardFireSaleSettings()` reference at line 3221 (remove the call)
    - Remove `RestoreRewardFireSaleStartupState()` reference at line 3222
    - Remove `ScheduleRewardFireSaleExpirationIfNeeded()` reference at line 3232
    - Remove all `RewardFireSale*` methods (lines 5511-6072)
    - Remove `WireRewardFireSale` / `UnwireRewardFireSale` methods and their call sites
    - Remove `RewardFireSaleChanged`, `RewardFireSaleTiersCollectionChanged`, `RewardFireSaleTierChanged` handlers
    - Remove `ApplyRewardFireSaleDiscount` method (keep it! — it's used in the managed reward sync loop)

    Wait — `ApplyRewardFireSaleDiscount` is called from the managed reward sync code in MainWindowViewModel. It MUST stay. It just reads `Settings.RewardFireSale.ActiveDiscountPercent` to calculate the discount. It doesn't need to be in the new VM.

- [ ] **Step 3: Remove reference to IsViewingRewardFireSale in RuleListView enum**

    Check if `RuleListView.RewardFireSale` is used elsewhere. If it's only used for the embedded view, remove the enum member and all references.

- [ ] **Step 4: Update MainWindow.xaml sidebar button**

    Change the "Reward Fire Sale" button (lines 3548-3562):
    - Change `Command` from `{Binding ShowRewardFireSaleCommand}` to `{Binding OpenRewardFireSaleManagerCommand}`
    - Remove `DataTrigger` for `IsViewingRewardFireSale` (or change to a simpler style)

- [ ] **Step 5: Remove embedded Fire Sale workspace from MainWindow.xaml**

    Remove the entire Fire Sale Border (lines 3814-4142) and the sidebar status panel (lines 3598-3645).

    Also remove any remaining references to `IsViewingRewardFireSale` in the XAML DataTriggers.

- [ ] **Step 6: Build to verify**

    ```powershell
    dotnet build "E:\!!!Program to work on\Proper Crystal Relay\VrcTwitchOscBridge\VrcTwitchOscBridge.csproj" --no-restore
    ```

---
### Task 6: Add localization entries

**Files:**
- Modify: `Resources\Localization\en-US.extra.json`
- Modify: `Resources\Localization\es-ES.extra.json`
- Modify: `Resources\Localization\fr-FR.extra.json`
- Modify: `Resources\Localization\it-IT.extra.json`
- Modify: `Resources\Localization\ko-KR.extra.json`

- [ ] **Step 1: Add new keys to en-US**

    Add to `en-US.extra.json`:

    ```json
    "Count Cash Payments": "Count Cash Payments",
    "Cash Payment Ratio": "Cash Payment Ratio",
    "$1 = {0} Fire Sale progress": "$1 = {0} Fire Sale progress",
    "Connected Services": "Connected Services"
    ```

- [ ] **Step 2: Add new keys to non-English files**

    Add the same keys with translated values to es-ES, fr-FR, it-IT, ko-KR.

- [ ] **Step 3: Run localization audit to verify**

    ```powershell
    dotnet run --project "E:\!!!Program to work on\Proper Crystal Relay\LocalizationAudit"
    ```

---
### Task 7: Final build and verification

- [ ] **Step 1: Full build**

    ```powershell
    dotnet build "E:\!!!Program to work on\Proper Crystal Relay\VrcTwitchOscBridge\VrcTwitchOscBridge.csproj" --no-restore
    ```

- [ ] **Step 2: Verify the build produces no errors and no warnings related to the changes**

    Check output for error codes CS* (compilation errors) and XAML parse errors.
