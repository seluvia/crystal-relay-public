# Cash Payment Manager Window — Design Spec

## Goal
Replace the inline Cash Payment tab in MainWindow with a dedicated `CashPaymentManagerWindow` that contains only connection/credential configuration (StreamElements, Streamlabs, Ko-fi). Cash Payment rules remain in `AppSettings.CashPaymentRules` and are still managed through the Avatar Scaling Manager's "Cash Payments" source tab.

## Scope
- **New files:** `CashPaymentManagerWindow.xaml`, `CashPaymentManagerWindow.xaml.cs`, `ViewModels/CashPaymentManagerViewModel.cs`
- **Removed from MainWindow:** The entire Cash Payment inline tab (sidebar button behavior, connection panel, rules list, workspace editor, all view-bound Cash Payment properties/commands)
- **Untouched:** `AppSettings.CashPaymentRules`, `CashPaymentProviderService`, `BridgeCoordinator`, `SettingsStore`, `CashPaymentRuleJsonConverter`, Avatar Swap inline editor, Avatar Scaling Cash Payments tab, Test Mode simulation

## Window Design

### Chrome & Theme
- `WindowStyle="None"` with `WindowChrome` (CaptionHeight=0, ResizeBorderThickness=6)
- Same color palette as `AvatarScalingManagerWindow`:
  - `WindowBackgroundBrush: #130B1E`
  - `PanelBrush: #CC1C132B`, `NestedPanelBrush: #B8241739`
  - `AccentBrush: #A855F7`, `HighlightBorderBrush` equivalent
  - `TextBrush: #F5EEFF`, `MutedBrush: #C9B8E3`
  - `InputBrush: #241733`, `InputBorderBrush: #5B3A8E`
  - Same custom scrollbar templates
- Custom title bar with close/minimize buttons, draggable
- Width: 700, Height: 600, MinWidth: 540, MinHeight: 480

### Layout
A single scrollable vertical layout with three provider cards:

1. **StreamElements card** — Enable checkbox, Account ID text field, JWT token text field
2. **Streamlabs card** — Enable checkbox, Access token text field
3. **Ko-fi card** — Enable checkbox, Hosted relay / Local webhook radio buttons, conditional sub-fields:
   - Hosted relay: read-only webhook URL, "Regenerate Ko-fi Relay Link" button, "Open Ko-fi Webhooks Page" button
   - Local webhook: port, path, public URL fields, local URL read-only display
   - Verification token PasswordBox (always visible)

Each card is a `Border` with `CornerRadius="18"`, `NestedPanelBrush` background, `HighlightBorderBrush` border, 18px padding, matching the other manager window card style.

### Sidebar Button
The existing "Cash Payments" button in the MainWindow sidebar (under "Viewer Support") stays but now opens the manager window via `OpenCashPaymentManagerCommand` instead of switching to the inline tab.

## ViewModel Design

### `CashPaymentManagerViewModel`
```
class CashPaymentManagerViewModel : ObservableObject
- Constructor(AppSettings settings, MainWindowViewModel mainWindow)
- Properties bound to Settings.CashPayments.* (same bindings as current inline tab)
- OpenKoFiWebhooksCommand -> calls mainWindow.OpenKoFiWebhooksPage()
- RegenerateKoFiRelayIdentityCommand -> calls Settings.CashPayments.RegenerateKoFiRelayIdentity(),
  then mainWindow.QueueSave(), mainWindow.QueueBridgeRefresh()
- Dispose() unsubscribes view model
```

### MainWindowViewModel changes
- Remove `RuleListView.CashPayments` from the enum
- Remove `IsViewingCashPayments` property
- Remove `RuleListView.CashPayments` from the enum
- Remove `IsViewingCashPayments` property and all its references in help text, parameters, etc.
- Remove `SelectedCashPaymentRule` and all Cash Payment rule CRUD properties/commands
- Remove `CashPaymentProviderOptions`, `CashPaymentActionKindOptions`
- Remove `CashPaymentConnectionsChanged` handler
- Remove `CashPaymentRuleStatusText`, `CashPaymentActionEditorHelpText`, inline help text branches
- Remove `ShowCashPayments()` and `ShowCashPaymentsCommand`
- Add `OpenCashPaymentManagerCommand` that creates/shows `CashPaymentManagerWindow`
- Move `RegenerateKoFiRelayIdentityCommand` logic — CashPaymentManagerViewModel will have its own copy that calls `mainWindow.QueueSave()` / `mainWindow.QueueBridgeRefresh()` via the MainWindowVM reference
- Move `OpenKoFiWebhooksCommand` logic — CashPaymentManagerViewModel will have its own copy that calls `mainWindow.OpenKoFiWebhooksPage()`
- Keep `AddAvatarScalingCashPaymentRuleCommand` and `DeleteCashPaymentRuleByCard()` — these are called by AvatarScalingManagerViewModel

### MainWindow.xaml changes
- Replace sidebar Cash Payments button Command from `ShowCashPaymentsCommand` to `OpenCashPaymentManagerCommand`
- Remove the active-tab DataTrigger on that button
- Remove the entire Cash Payment connection panel (~lines 3680-3795)
- Remove the Cash Payment rules ListBox (~lines 3898-3930)
- Remove the Cash Payment workspace editor (~lines 6073-6420)
- Remove any empty-state triggers referencing `CashPaymentRules.Count`

## One-Time Migration Notice (3.1.8 → 3.1.9)

When a user upgrades from 3.1.8 to 3.1.9 for the first time, a `MessageBox` appears on startup informing them:

> "Cash Payments has moved into its own window and no longer has its own rules tab. Cash Payment rules are now managed through the Avatar Scaling Manager's 'Cash Payments' source tab. All Cash Payment connections (StreamElements, Streamlabs, Ko-fi) can be found by clicking Cash Payments in the sidebar. This notice will not appear again."

This follows the exact pattern used by `AvatarSwapMigrationNoticeShown`:

### AppSettings.cs
- Add `public bool CashPaymentMigrationNoticeShown { get; set; }` (defaults to `false`)

### SettingsStore.cs
- Load: `settings.CashPaymentMigrationNoticeShown = profile.CashPaymentMigrationNoticeShown ?? settings.CashPaymentMigrationNoticeShown;`
- Save: `CashPaymentMigrationNoticeShown = settings.CashPaymentMigrationNoticeShown,`
- DTO: Add `public bool? CashPaymentMigrationNoticeShown { get; set; }` with `[JsonPropertyName("cashPaymentMigrationNoticeShown")]`

### MainWindowViewModel.cs
- Add `ShowCashPaymentMigrationNotice` property (mirrors `ShowMigrationNotice` pattern, checks `!Settings.CashPaymentMigrationNoticeShown`)
- Add `DismissCashPaymentMigrationNoticeCommand` and `DismissCashPaymentMigrationNotice()`

### MainWindow.xaml.cs
- Add `ShowCashPaymentMigrationNoticeIfNeeded()` method (mirrors `ShowAvatarSwapMigrationNoticeIfNeeded`)
- Call it from `OnLoaded()` after `ShowAvatarSwapMigrationNoticeIfNeeded()`

## Safety / Backward Compatibility

### AvatarSwapRuleEditorControl.xaml
Four DataTriggers reference `IsViewingCashPayments` with `FallbackValue=False`. Since the property will no longer exist, the fallback ensures:
- Title defaults to "Redeem Editor" (correct)
- Twitch trigger section stays visible (correct for Avatar Swap)
- Cooldown combo stays hidden (correct)
No changes needed.

### Avatar Scaling Manager
Fully independent — uses `Settings.CashPaymentRules` directly and calls `mainWindowViewModel.AddAvatarScalingCashPaymentRuleCommand` / `DeleteCashPaymentRuleByCard()` on MainWindowVM. These command/method references stay on MainWindowViewModel.

## Files Changed/Removed

### New
- `VrcTwitchOscBridge\CashPaymentManagerWindow.xaml`
- `VrcTwitchOscBridge\CashPaymentManagerWindow.xaml.cs`
- `VrcTwitchOscBridge\ViewModels\CashPaymentManagerViewModel.cs`

### Modified
- `VrcTwitchOscBridge\MainWindow.xaml` — remove Cash Payment inline sections, replace sidebar button command
- `VrcTwitchOscBridge\ViewModels\MainWindowViewModel.cs` — remove all Cash Payment inline tab members, add OpenCashPaymentManagerCommand

### Unchanged (verified)
- `VrcTwitchOscBridge\Models\CashPaymentRule.cs` — data model stays
- `VrcTwitchOscBridge\Services\CashPaymentProviderService.cs` — runtime service stays
- `VrcTwitchOscBridge\Services\BridgeCoordinator.cs` — orchestrator stays
- `VrcTwitchOscBridge\Services\SettingsStore.cs` — persistence stays
- `VrcTwitchOscBridge\Services\CashPaymentRuleJsonConverter.cs` — JSON converter stays
- `VrcTwitchOscBridge\Services\AvatarSwapMigrationService.cs` — legacy migration stays
- `VrcTwitchOscBridge\UserControls\InlineCashPaymentRuleEditorControl.xaml` — Avatar Swap inline editor stays
- `VrcTwitchOscBridge\UserControls\InlinePaymentRuleRowViewModel.cs` — Avatar Swap row VM stays
- `VrcTwitchOscBridge\AvatarScalingManagerWindow.xaml` — scaling Cash Payment tab stays
- `VrcTwitchOscBridge\ViewModels\AvatarScalingManagerViewModel.cs` — scaling Cash Payment source VM stays
- `VrcTwitchOscBridge\AvatarSwapManagerWindow.xaml` — swap manager stays
- `VrcTwitchOscBridge\TestModeWindow.xaml` — test simulation stays
- `VrcTwitchOscBridge\UserControls\AvatarSwapRuleEditorControl.xaml` — fallback bindings handle removal gracefully
