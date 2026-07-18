# Reward Fire Sale Popup Window Design

## Overview

Extract the Reward Fire Sale feature from its current embedded location in `MainWindow.xaml` and `MainWindowViewModel.cs` into a dedicated popup window with its own ViewModel, following the existing pattern used by CashPaymentManagerWindow, AvatarSwapManagerWindow, etc.

## UI Layout

A non-modal resizable popup window (~720×680, min 540×480) with custom title bar chrome matching the app theme. Organized into clear card-based sections:

### 1. Fire Sale Status Bar
- Active/inactive indicator with dynamic status text (e.g., "Fire Sale active: 25% off — Ends in 4m 32s")
- Progress bar showing current progress toward next tier
- "Current: X / Y" and "Next tier: Z → N% off" labels
- Reset Progress and Stop Fire Sale action buttons

### 2. Goal Sources
- Checkboxes: "Enable Reward Fire Sale", "Count Bits", "Use highest reached tier", "Count Cash Payments"

### 3. Funding Sources (two-column grid)

**Left — Channel Point Funding Reward:**
- Enable/disable checkbox at the top
- Reward Name, Cost (points), Cooldown (seconds), Points per 1 progress
- Dynamic conversion text ("At 100 pts and 10:1 ratio → each redeem adds 10 progress")
- Description textarea (optional)
- Ready/Cooldown color pickers with color swatch preview

**Right — Cash Payments:**
- Ratio input: `$1 = ___ Fire Sale progress`
- Example displayed dynamically
- Connected services read-only display (StreamElements ✓, Streamlabs ✓, Ko-fi ✓)

### 4. Sale Mode
- Combo box: Temporary / Permanent
- Duration (seconds) text input, visible only when Temporary is selected

### 5. Discount Tiers
- "Add Tier" button
- List of tier cards, each with: Goal Amount, Discount %, Delete button
- Minimum 1 tier enforced; tiers add with incremented defaults

## Architecture

### New Files

1. **`ViewModels/RewardFireSaleManagerViewModel.cs`**
   - Implements `ObservableObject` + `IDisposable`
   - Constructor takes `AppSettings` and `MainWindowViewModel?`
   - Owns a reference to `Settings.RewardFireSale` for direct binding
   - Exposes all properties and commands currently in `MainWindowViewModel` that relate to Fire Sale:
     - Settings passthroughs (`IsEnabled`, `CountBits`, `FundingRewardEnabled`, etc.)
     - Computed properties (`RewardFireSaleStatusText`, `RewardFireSaleProgressPercent`, `RewardFireSaleActiveWarningText`, etc.)
     - Commands: `AddRewardFireSaleTierCommand`, `RemoveRewardFireSaleTierCommand`, `StopRewardFireSaleCommand`, `ResetRewardFireSaleProgressCommand`
     - Cash Payment ratio property: `CashPaymentProgressRatio` (new, stored in `RewardFireSaleSettings`)
   - Subscribes to `RewardFireSaleContributionReceived` event (via MainWindowViewModel or BridgeCoordinator)
   - On close/dispose, unsubscribes all event handlers

2. **`RewardFireSaleManagerWindow.xaml`** + **`RewardFireSaleManagerWindow.xaml.cs`**
   - Standard window chrome with `WindowStyle="None"`, `shell:WindowChrome`, custom title bar with drag/close/minimize
   - **All XAML styling uses `{DynamicResource ...}` bindings** — every color, brush, and font family references dynamic resources (e.g., `{DynamicResource TextBrush}`, `{DynamicResource PanelBrush}`, `{DynamicResource HeadingFontFamily}`, `{DynamicResource BodyFontFamily}`) so the window matches the user's selected app theme
   - No hardcoded colors or brushes anywhere in the XAML
   - Constructor takes `RewardFireSaleManagerViewModel`, sets DataContext, then calls:
     ```csharp
     ThemeManager.ApplyToResources(Resources, ThemeManager.CurrentTheme);
     ThemeManager.ThemeChanged += OnThemeManagerThemeChanged;
     Closed += OnWindowClosed;
     ```
   - `OnThemeManagerThemeChanged` dispatches `ThemeManager.ApplyToResources(Resources)` for live theme switching
   - `OnWindowClosed` unsubscribes from `ThemeManager.ThemeChanged` and disposes the ViewModel

### Changes to Existing Files

3. **`MainWindowViewModel.cs`**
   - Add `_rewardFireSaleManagerWindow` field
   - Add `OpenRewardFireSaleManagerCommand` → `OpenRewardFireSaleManager()` method (follows CashPaymentManager pattern: activate if open, else create+show)
   - Remove the `IsViewingRewardFireSale`-related state and all Fire Sale-specific commands/properties/methods from the ViewModel. These move to `RewardFireSaleManagerViewModel`.

4. **`MainWindow.xaml`**
   - Change the "Reward Fire Sale" sidebar button from `ShowRewardFireSaleCommand` to `OpenRewardFireSaleManagerCommand`
   - Remove the embedded Fire Sale workspace Border (lines 3814-4142) and the sidebar status panel (lines 3598-3645)

5. **`Models/RewardFireSaleSettings.cs`**
   - Add `CashPaymentProgressRatio` property (int, default 100)
   - Add `CountCashPayments` property (bool, default false)

6. **Cash Payment contribution flow:**
   - In the existing Cash Payment handler pipeline (in `BridgeCoordinator.cs` or `CashPaymentManagerViewModel`), after a cash payment is processed for its normal triggers, check `Settings.RewardFireSale.CountCashPayments`
   - If enabled, calculate progress: `amount = Math.Floor(cashAmount * Settings.RewardFireSale.CashPaymentProgressRatio)`
   - Fire `RewardFireSaleContributionReceived` with a new contribution type `CashPayment` added to the enum
   - `RewardFireSaleManagerViewModel.HandleRewardFireSaleContribution` handles this type like Bits (just adds progress, no reward lookup needed)

### Data Flow

```
Twitch Bits  ─┐
               ├──→ BridgeCoordinator.TryBuildRewardFireSaleContribution
Funding Reward─┘                              │
                                               ↓
                              RewardFireSaleContributionReceived event
                                               ↓
Cash Payment ──→ Cash Payment handler          │
                  checks CountCashPayments ────┘
                                               ↓
                  RewardFireSaleManagerViewModel
                  HandleRewardFireSaleContribution()
                                               ↓
                  Resolves amount (Bits × CountBits, Funding reward × conversion ratio, Cash × cash ratio)
                                               ↓
                  Adds to CurrentProgress → checks tiers → activates/upgrades sale
                                               ↓
                  Applies ActiveDiscountPercent via ApplyRewardFireSaleDiscount()
                                               ↓
                  Queues ManagedRewardSyncReason.FireSaleChanged → Twitch API sync
```

### New / Extended Types

**`RewardFireSaleContributionType`** (in `BridgeCoordinator.cs`):
- Add member: `CashPayment`

**`RewardFireSaleSettings`** (new properties):

| Property | Type | Default | Description |
|----------|------|---------|-------------|
| `CountCashPayments` | bool | false | Whether cash payments contribute |
| `CashPaymentProgressRatio` | int | 100 | Progress per $1 of cash payment |

### Localization

Add new keys to all `*.extra.json` files:
- "Count Cash Payments"
- "Cash Payment Ratio"
- "$1 = ___ Fire Sale progress"
- "Connected Services"
- Existing keys already present for all other sections

### Persistence

- `RewardFireSaleSettings` already serialized through `SettingsStore` via `PersistedRewardFireSaleSettings`
- New `CountCashPayments` and `CashPaymentProgressRatio` fields added to the persistence DTO
- No migration needed for existing users (defaults are false/100)

## Implementation Order

1. Add new properties to `RewardFireSaleSettings` model
2. Create `RewardFireSaleManagerViewModel` with all properties/commands
3. Wire up event subscription and cash payment contribution handling
4. Create `RewardFireSaleManagerWindow` XAML + code-behind
5. Add command/field to `MainWindowViewModel` for opening the window
6. Update `MainWindow.xaml` sidebar button
7. Optionally remove embedded Fire Sale content
8. Add localization entries
9. Build and verify

## Open Questions (resolved via mockup)

- ✅ Channel Point Funding Reward has its own enable checkbox
- ✅ Cash Payments get a ratio input ($1 = X progress)
- ✅ Layout uses light/readable card-based sections
- ✅ Non-modal window, resizable
