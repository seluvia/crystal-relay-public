# Avatar Swap Manager: Per-Type Rule Sections

## Problem

The Avatar Swap Manager window (`AvatarSwapManagerWindow.xaml`) has four rule sections — 🏆 **Channel Points** / 💎 **Bits** / ⭐ **Subs** / 💵 **Payment** — but all four render through one shared `InlineAvatarSwapRuleRowControl`. That control shows Channel-Points-style fields (Twitch Reward Name, Cost, Description, Sync Mode, Existing Reward picker, Shared/Numbered Reward, Reward Colors) for every section. Bits, Subs, and Payment rules get irrelevant fields and the meaningful fields never appear.

The data model already supports all the right fields:

- **Bits / Subs fields** live on `TriggerRule` (`Models/TriggerRule.cs`): `MinimumAmount`, `BitsAmountUnitsPerDuration`, `BitsSecondsPerAmountUnit`, `SubscriptionsAmountUnitsPerDuration`, `SubscriptionsSecondsPerAmountUnit`, `SubscriptionTier1/2/3SecondsPerSub`, `MaxAccumulatedDurationEnabled`, `MaxAccumulatedDurationSeconds`, `SupporterKeywordText`, `IsGiftSubscription`.
- **Payment fields** live on the separate `CashPaymentRule` model (`Models/CashPaymentRule.cs`): `Provider`, `MinAmount`, `MaxAmount`, `CurrencyCode`, `MessageContains`, `CooldownSeconds`, `IsEnabled`, plus a wrapped `TriggerAction : TriggerRule` and `ScaleAction : AvatarScaleRule`.

The full editors for Bits + Subs, Cash Payment, and Power Up **already exist** in the main Redeem Library (`MainWindow.xaml:6804+` for Cash Payment, `MainWindow.xaml:4864+` for Power Up, `UserControls/AvatarSwapRuleEditorControl.xaml` for TriggerRule). The Avatar Swap Manager should reuse them instead of inventing a third editor.

The underlying bug is in `AvatarSwapManagerViewModel.AddPaymentRule()` (line 384): it creates a `TriggerRule` with `TriggerType = ChannelPoints` (not a payment type) and only tags it with `Source = CashPayment`. The `PaymentRules` collection on `AvatarSwapProfile` (`Models/AvatarSwapProfile.cs:20`) is typed as `ObservableCollection<TriggerRule>` instead of `ObservableCollection<CashPaymentRule>`.

## Goal

Give every section in the Avatar Swap Manager its own proper, type-specific row + editor, with a smart per-type summary in the collapsed list, and unify the edit experience with the existing full-page editors.

## Scope

- **In scope:** the four sections in `AvatarSwapManagerWindow.xaml` (Channel Points, Bits, Subs, Payment) and the data model for `AvatarSwapProfile.PaymentRules`.
- **In scope:** the "Roulette Triggers" list inside the same window — currently uses the same shared row control. Switched to a per-type card to stay consistent.
- **In scope:** one-time data migration for `AvatarSwapProfile.PaymentRules` from `TriggerRule` to `CashPaymentRule` on settings load.
- **Out of scope:** the main Redeem Library tabs (Bits + Subs, Cash Payments, Power Up, Movement Redeems, Universal Triggers, Avatar Scaling, Reward Fire Sale) — they are unchanged. The Power Up tab continues to use its existing inline editor. The Cash Payment tab will internally switch from an inline `DataTemplate` to a new reusable `UserControl`, but with no user-visible behavior change.
- **Out of scope:** Twitch Custom Power-up rules, Wardrobe, Avatar Sets, Avatar Roulette, Fooma import, Universal Triggers manager.

## Design

### A. Data Model

1. Change `Models/AvatarSwapProfile.cs:20` from:
   ```csharp
   public ObservableCollection<TriggerRule> PaymentRules { get; } = new();
   ```
   to:
   ```csharp
   public ObservableCollection<CashPaymentRule> PaymentRules { get; } = new();
   ```

2. `BitsRules` and `SubsRules` stay as `ObservableCollection<TriggerRule>`. All required fields are already on `TriggerRule`.

3. Settings migration runs once on first load after the model change. A new `MigrateV4ToV5` step is added to the existing `Services/AvatarSwapMigrationService.cs` (which already uses a `CurrentMigrationVersion` int and a chain of `MigrateVNT oV(N+1)` helpers — e.g., `MigrateLegacyToV3`, `MigrateV3ToV4`). The new step detects legacy `TriggerRule` entries where `Source == TriggerRuleSource.CashPayment` and converts each to a fresh `CashPaymentRule`:
   - `Name` ← old rule's `Name`
   - `Provider` ← `CashPaymentProvider.StreamElements` (first option in the dropdown)
   - `MinAmount` ← old rule's `MinimumAmount`
   - `IsEnabled` ← `true`
   - `ActionKind` ← `CashPaymentActionKind.TriggerAction`
   - `TriggerAction` ← new `TriggerRule` with:
     - `ActionType` ← old rule's `ActionType`
     - `AvatarChangeTargetId` ← old rule's `AvatarChangeTargetId` (preserves the avatar swap target)
     - `AvatarTargetName` ← old rule's `AvatarTargetName`
   - All other `TriggerRule` fields on the legacy entry (`BotMessageTemplate`, `ChannelPointReward*`, `RewardSyncMode`, `RewardColors`, `ChatCommand*`, `SharedReward*`, `DeleteManagedRewardWhenInactive`) are dropped. A debug log line is written: `"Avatar Swap migration: dropped legacy payment rule fields for {Name}"`.

4. The existing `AvatarChangeToAvatarSwapMigrationVersion` int on `AppSettings.cs` is bumped from `4` to `5` (following the established pattern in `AvatarSwapMigrationService.cs`). The new `MigrateV4ToV5` step runs only when the saved version is `< 5`. After running, the version is set to `5` and the migrated settings are saved.

5. `AvatarSwapManagerViewModel.AddPaymentRule()` is rewritten to create a real `CashPaymentRule` with the same defaults as the migration. `TriggerType = ChannelPoints` is no longer set.

6. The settings save format naturally changes: `PaymentRules` is now serialized as `CashPaymentRule` objects instead of `TriggerRule` objects. The existing `CashPaymentRule` model already serializes its fields correctly.

### B. Per-Type Row Controls (collapsed list)

**Delete** the current `UserControls/InlineAvatarSwapRuleRowControl.xaml` and `.cs`. It is only used in `AvatarSwapManagerWindow.xaml`.

**Create five new compact row UserControls** under `UserControls/`, one per trigger type. Each shows a smart summary line and a single Edit + Delete button pair. No inline expand.

| Control | Data type | Smart summary format |
|---|---|---|
| `InlineChannelPointRuleRowControl` | `TriggerRule` (TriggerType=ChannelPoints) | `🏆 {Name} — {Cost} pts` |
| `InlineBitsRuleRowControl` | `TriggerRule` (TriggerType=Bits) | `💎 {Name} — Min {MinAmount} bits, {Seconds}s per {Units} bits, cap {Max}s, keyword: {kw}` |
| `InlineSubsRuleRowControl` | `TriggerRule` (TriggerType=Subscriptions) | `⭐ {Name} — T1:{t1}s T2:{t2}s T3:{t3}s, sub-type: {regular/gift/both}, keyword: {kw}` |
| `InlinePaymentRuleRowControl` | `CashPaymentRule` | `💵 {Name} — {Provider} {Currency} {Min}-{Max} match: '{MessageContains}'` |
| `InlineRouletteRuleRowControl` | `TriggerRule` (roulette triggers, typically Channel Points) | Same format as Channel Points |

Empty values are omitted from the summary (e.g., a Bits rule with no keyword set just doesn't show "keyword: ...").

Each row VM is a small `ObservableObject`:

```csharp
public interface IRuleRowViewModel
{
    object Rule { get; }                  // TriggerRule or CashPaymentRule
    string Summary { get; }               // smart per-type summary
    bool IsEnabled { get; }               // reflects rule's IsEnabled
    ICommand EditCommand { get; }         // sets AvatarSwapManagerViewModel.SelectedRule = this
    ICommand DeleteCommand { get; }       // removes from the right collection
    void RefreshSummary();                // called by the VM when the rule's properties change
}
```

The row VM subscribes to `INotifyPropertyChanged` on its `Rule` and calls `RefreshSummary()` from the relevant property setters. This keeps the summary live as the user edits in the full editor (since the underlying rule is the same instance across both views).

Concretely:

- `InlineChannelPointRuleRowViewModel : ObservableObject, IRuleRowViewModel` — backs onto a `TriggerRule`
- `InlineBitsRuleRowViewModel` — same
- `InlineSubsRuleRowViewModel` — same
- `InlinePaymentRuleRowViewModel` — backs onto a `CashPaymentRule`
- `InlineRouletteRuleRowViewModel` — backs onto a `TriggerRule`

The five `ObservableCollection<InlineAvatarSwapRuleRowViewModel>` properties on `AvatarSwapManagerViewModel` (`ChannelPointRows`, `BitsRows`, `SubsRows`, `PaymentRows`, `RouletteTriggerRows`) are replaced by typed collections of the new VMs.

### C. Right-Pane Full Editor Integration

The right pane of `AvatarSwapManagerWindow.xaml` (lines 314–396) currently has two `Border`s with `IsSwapEditorOpen` / `IsRouletteEditorOpen` visibility. The new design has a single `ContentControl` whose content is one of:

1. **The 4-section list view** (the current right-pane XAML, repurposed to use the new typed rows)
2. **The full editor for a selected rule** (one `DataTemplate` per row VM type)

```xaml
<ContentControl Content="{Binding RightPaneContent}">
    <ContentControl.Resources>
        <DataTemplate DataType="{x:Type vm:RuleListPaneViewModel}">
            <!-- existing 4-section list XAML, updated to use new row controls -->
        </DataTemplate>
        <DataTemplate DataType="{x:Type vm:InlineChannelPointRuleRowViewModel}">
            <Border ...>
                <DockPanel>
                    <Button DockPanel.Dock="Top"
                            Content="← Back to {SwapName}"
                            Command="{Binding DataContext.BackToListCommand, RelativeSource={...}}" />
                    <userControls:AvatarSwapRuleEditorControl DataContext="{Binding Rule}" />
                </DockPanel>
            </Border>
        </DataTemplate>
        <DataTemplate DataType="{x:Type vm:InlineBitsRuleRowViewModel}">
            <Border ...>
                <DockPanel>
                    <Button DockPanel.Dock="Top" Content="← Back to {SwapName}" ... />
                    <userControls:AvatarSwapRuleEditorControl DataContext="{Binding Rule}" />
                </DockPanel>
            </Border>
        </DataTemplate>
        <DataTemplate DataType="{x:Type vm:InlineSubsRuleRowViewModel}">
            <!-- same as above, but with the EditorMode set to Subs -->
        </DataTemplate>
        <DataTemplate DataType="{x:Type vm:InlinePaymentRuleRowViewModel}">
            <Border ...>
                <DockPanel>
                    <Button DockPanel.Dock="Top" Content="← Back to {SwapName}" ... />
                    <userControls:CashPaymentRuleEditorControl DataContext="{Binding Rule}" />
                </DockPanel>
            </Border>
        </DataTemplate>
        <DataTemplate DataType="{x:Type vm:InlineRouletteRuleRowViewModel}">
            <Border ...>
                <DockPanel>
                    <Button DockPanel.Dock="Top" Content="← Back to {RouletteName}" ... />
                    <userControls:AvatarSwapRuleEditorControl DataContext="{Binding Rule}" />
                </DockPanel>
            </Border>
        </DataTemplate>
    </ContentControl.Resources>
</ContentControl>
```

The `AvatarSwapRuleEditorControl` already handles ChannelPoints, Bits, and Subs via the `IsViewing*` flags on its DataContext's `MainWindowViewModel`. For the AvatarSwapManager context, those flags don't apply. The editor is reused **as-is** with a small addition: when hosted in the AvatarSwapManager, the rule's `TriggerType` alone drives the right sub-template (e.g., `UsesAmountScaledDuration`, `UsesSupporterFloatAdd`, the Supporter Avatar Scope panel, the Bits amount / Sub tier panels). The existing `IsViewing*` visibility triggers in `AvatarSwapRuleEditorControl.xaml` are reviewed; any that gate Bits/Subs/Supporter-specific sections purely on `IsViewingSupporterOverrides` get a parallel `OR` against `DataContext.TriggerType is TwitchTriggerType.Bits or Subscriptions` so the same section appears in the AvatarSwapManager context. The Power Up and Cash Payment sections of the editor are already gated on `IsViewingPowerUps` / `IsViewingCashPayments` and **remain collapsed** in the AvatarSwapManager context (those trigger types are not used in AvatarSwapProfile rules).

### D. New `CashPaymentRuleEditorControl` UserControl

**Extract** the existing inline `DataTemplate DataType="CashPaymentRule"` from `MainWindow.xaml` (lines 6804–7600, including the "Payment Match" card, the "Cash Action" family picker card, and the "Avatar Scaling Action" sub-card) into a new reusable `UserControls/CashPaymentRuleEditorControl.xaml` + `.cs`.

- The new UserControl has `DataContext = CashPaymentRule` and exposes a `Rule` property of type `CashPaymentRule` for binding clarity.
- `MainWindow.xaml` (the main Cash Payments tab) replaces its inline `ContentControl ContentTemplate` with `<userControls:CashPaymentRuleEditorControl DataContext="{Binding SelectedCashPaymentRule}" />`. No behavior change for the main tab.
- The "Test Cash Rule" button, "Payment Match" card, "Cash Action" family picker, and Avatar Scaling section all move into the new UserControl unchanged.
- Any `RelativeSource={RelativeSource AncestorType=Window}` bindings inside the moved XAML are converted to `RelativeSource AncestorType=UserControl` or to direct property bindings on the UserControl's `Rule`.

### E. AvatarSwapManagerViewModel Changes

New properties:

```csharp
public object? RightPaneContent { get; }   // RuleListPaneViewModel | IRuleRowViewModel
public IRuleRowViewModel? SelectedRule { get; set; }   // set by row Edit button, drives right pane
public RelayCommand BackToListCommand { get; }          // clears SelectedRule
```

New typed collections (replacing the existing 5):

```csharp
public ObservableCollection<InlineChannelPointRuleRowViewModel> ChannelPointRows { get; } = new();
public ObservableCollection<InlineBitsRuleRowViewModel> BitsRows { get; } = new();
public ObservableCollection<InlineSubsRuleRowViewModel> SubsRows { get; } = new();
public ObservableCollection<InlinePaymentRuleRowViewModel> PaymentRows { get; } = new();
public ObservableCollection<InlineRouletteRuleRowViewModel> RouletteTriggerRows { get; } = new();
```

`IsSwapEditorOpen` / `IsRouletteEditorOpen` are replaced by `RightPaneContent` having a non-null value. `OpenSwapEditor` and `OpenRouletteEditor` set `RightPaneContent = new RuleListPaneViewModel(...)`.

`RebuildRows()` is updated to construct the typed row VMs.

`BeginInlineEdit` / `CommitInlineEdit` / `CancelInlineEdit` are removed (the row no longer expands inline).

`AddChannelPointRule / AddBitsRule / AddSubsRule / AddPaymentRule` are updated to:
1. Create the appropriate rule (`TriggerRule` with the right `TriggerType`, or `CashPaymentRule` with the migration defaults).
2. Add it to the right `SelectedSwapCard.Profile` collection.
3. Add a typed row VM to the right `ObservableCollection<...>Rows`.
4. For `AddPaymentRule`, the new `CashPaymentRule` defaults: `Provider = StreamElements`, `MinAmount = 0`, `MaxAmount = 0`, `CurrencyCode = ""`, `MessageContains = ""`, `IsEnabled = true`, `Name = "New Cash Payment Swap"`, `ActionKind = TriggerAction`, `TriggerAction` is a fresh `TriggerRule` with `ActionType = OscActionType.AvatarChange`, `AvatarChangeTargetId = SelectedSwapCard.Profile.TargetAvatarId`, `AvatarTargetName = SelectedSwapCard.Profile.TargetAvatarName`.

`DeleteRule(rowVm)` checks the type of `rowVm.Rule` to find the right source collection and remove from it.

### F. AvatarSwapRuleEditorControl.XAML Triggers

The existing editor uses `DataContext.IsViewing*` flags from `MainWindowViewModel` to gate many sections. The new design must also support the AvatarSwapManager context. A new `IsInAvatarSwapManager` boolean property is added to `AvatarSwapRuleEditorControl` (set in code-behind from the host window's `DataContext`):

```csharp
public bool IsInAvatarSwapManager
{
    get => (bool)GetValue(IsInAvatarSwapManagerProperty);
    set => SetValue(IsInAvatarSwapManagerProperty, value);
}
```

The `AvatarSwapManagerWindow` sets this to `true` on the editor's Loaded event. The XAML triggers in `AvatarSwapRuleEditorControl.xaml` that gate Bits/Subs/Supporter sections are updated to also fire when `DataContext.TriggerType is TwitchTriggerType.Bits or Subscriptions` (alongside their existing `IsViewingSupporterOverrides` check). Power Up and Cash Payment gates stay as-is (those trigger types are not used in AvatarSwapProfile rules).

### G. Settings Save Format

- The serialized JSON for `AvatarSwapProfile.PaymentRules` changes from `TriggerRule[]` to `CashPaymentRule[]`. The `JsonSerializer` handles this automatically once the property type changes.
- Older settings files containing `TriggerRule` payment entries are caught by the migration in section A.3.
- New settings saves write the `CashPaymentRule` format.

### H. Localization

New `en-US` keys (mirrored to all 14 `.extra.json` files):

1. `"Back to {0}"` — back button label, with `{0}` = avatar swap name
2. `"Back to {0} (Roulette)"` — back button label for roulette triggers, with `{0}` = roulette name

All other field labels, section headers, and tooltips are reused from existing keys (`Minimum Amount`, `Active Time (seconds)`, `Cooldown (seconds)`, `Provider`, `Minimum Amount`, `Maximum Amount`, `Currency`, `Message Contains`, `Bits`, `Subs`, etc.).

## Files Changed

| File | Change |
|---|---|
| `Models/AvatarSwapProfile.cs` | Change `PaymentRules` type to `ObservableCollection<CashPaymentRule>` |
| `Models/AppSettings.cs` | Bump migration version constant on `AvatarSwapMigrationService` to `5` |
| `ViewModels/AvatarSwapManagerViewModel.cs` | Rewrite AddPaymentRule; replace typed collections; remove inline edit state machine; add `RightPaneContent`, `SelectedRule`, `BackToListCommand`; update RebuildRows / DeleteRule |
| `UserControls/InlineAvatarSwapRuleRowControl.xaml` + `.cs` | **Deleted** |
| `UserControls/InlineChannelPointRuleRowControl.xaml` + `.cs` | **New** — collapsed card, smart summary, Edit + Delete |
| `UserControls/InlineChannelPointRuleRowViewModel.cs` | **New** |
| `UserControls/InlineBitsRuleRowControl.xaml` + `.cs` | **New** |
| `UserControls/InlineBitsRuleRowViewModel.cs` | **New** |
| `UserControls/InlineSubsRuleRowControl.xaml` + `.cs` | **New** |
| `UserControls/InlineSubsRuleRowViewModel.cs` | **New** |
| `UserControls/InlinePaymentRuleRowControl.xaml` + `.cs` | **New** |
| `UserControls/InlinePaymentRuleRowViewModel.cs` | **New** |
| `UserControls/InlineRouletteRuleRowControl.xaml` + `.cs` | **New** |
| `UserControls/InlineRouletteRuleRowViewModel.cs` | **New** |
| `UserControls/CashPaymentRuleEditorControl.xaml` + `.cs` | **New** — extracted from MainWindow.xaml |
| `UserControls/AvatarSwapRuleEditorControl.xaml` + `.cs` | Add `IsInAvatarSwapManager` DP; broaden supporter triggers to also fire on `TriggerType is Bits or Subscriptions` |
| `AvatarSwapManagerWindow.xaml` | Replace 5 `ItemsControl` row blocks with the 4 new controls + the new roulette control; replace the right pane with a single `ContentControl` driven by `RightPaneContent` |
| `AvatarSwapManagerWindow.xaml.cs` | Wire up `IsInAvatarSwapManager = true` on the editor's Loaded event |
| `MainWindow.xaml` | Swap the inline `DataTemplate DataType="CashPaymentRule"` for `<userControls:CashPaymentRuleEditorControl DataContext="{Binding SelectedCashPaymentRule}" />` |
| `ViewModels/InlineAvatarSwapRuleRowViewModel.cs` | **Deleted** (replaced by 5 typed VMs) |
| `Services/AvatarSwapMigrationService.cs` | Add `MigrateV4ToV5` step; bump `CurrentMigrationVersion` to `5`; add conversion of legacy `TriggerRule` payment entries to `CashPaymentRule` |
| `Localization/en-US.extra.json` | Add `"Back to {0}"` and `"Back to {0} (Roulette)"` |
| `Localization/{de-DE,es-ES,fr-FR,it-IT,ja-JP,ko-KR,pl-PL,pt-BR,ru-RU,sv-SE,th-TH,zh-CN,zh-TW}.extra.json` | Add same 2 keys (placeholder values; translator can localize later) |
| `VrcTwitchOscBridge.csproj` | No new items expected (XAML files are auto-included by the `EnableDefaultPageItems=false` project; check the `<Page>` and `<Compile>` lists and add the new files explicitly) |

## Verification

1. **Build:** `dotnet build "E:\!!!Program to work on\Proper Crystal Relay\VrcTwitchOscBridge\VrcTwitchOscBridge.csproj" --no-restore` succeeds.
2. **Unit tests:** Existing test project still passes. If any tests reference `InlineAvatarSwapRuleRowViewModel` or `InlineAvatarSwapRuleRowControl`, update or remove them.
3. **Manual visual check 1 — Channel Points row:** Open Avatar Swap Manager → pick an avatar swap → Channel Points section shows `🏆 {Name} — {Cost} pts` with Edit + Delete buttons.
4. **Manual visual check 2 — Bits row:** Add a new Bits rule → shows `💎 {Name} — Min {N} bits`. Set `BitsAmountUnitsPerDuration` and `BitsSecondsPerAmountUnit` → summary updates to `+ {S}s per {U} bits`. Set `MaxAccumulatedDurationSeconds` → summary updates to `cap {Max}s`. Set `SupporterKeywordText` → summary updates to `keyword: {kw}`. Open Edit → the existing full TriggerRule editor appears with the supporter settings panel.
5. **Manual visual check 3 — Subs row:** Same drill. Setting `SubscriptionTier1/2/3SecondsPerSub` shows up in the summary as `T1:{t1}s T2:{t2}s T3:{t3}s`. `IsGiftSubscription` toggles between `sub-type: regular`, `sub-type: gift`, `sub-type: regular+gift`.
6. **Manual visual check 4 — Payment row:** Add a new Cash Payment rule → shows `💵 New Cash Payment Swap — StreamElements USD 0-0 match: ''`. Set `Provider = Ko-fi`, `MinAmount = 5`, `MaxAmount = 50`, `CurrencyCode = USD`, `MessageContains = 'cheer'` → summary updates to `💵 {Name} — Ko-fi USD 5-50 match: 'cheer'`. Open Edit → the new `CashPaymentRuleEditorControl` appears with Payment Match + Cash Action sections.
7. **Manual visual check 5 — Back button:** From any full editor view, click "← Back to {SwapName}" → returns to the 4-section list, the previously selected row is still highlighted, the editor's changes persist.
8. **Migration test:** Take a settings file that has `AvatarSwapProfiles[0].PaymentRules` containing old `TriggerRule` entries, and `AvatarChangeToAvatarSwapMigrationVersion = 4`. Launch the app. The `MigrateV4ToV5` step runs once; `CurrentMigrationVersion` is bumped to `5`. Check the debug log for `"Avatar Swap migration: dropped legacy payment rule fields for ..."`. Open the Avatar Swap Manager for that profile → the payment section shows `CashPaymentRule` rows with `Provider = StreamElements`, `Name` preserved, and the avatar swap target intact (verify by opening Edit → Action Kind = Trigger Action → Avatar Change target = original avatar). Save the settings. Quit. Relaunch with the same settings file (now with `AvatarChangeToAvatarSwapMigrationVersion = 5`) → migration does not run a second time.
9. **Save round-trip:** With migrated rules, save settings, quit, restart, reopen → all rules still present and editable.
10. **Main tab unchanged:** Open the main Cash Payments tab in the Redeem Library → looks and behaves identically (uses the new `CashPaymentRuleEditorControl`, but the user-facing experience is the same).
11. **Localization audit:** Run the localization audit to verify the 2 new keys are present in all 14 `.extra.json` files and have no empty values.
12. **Themed UI:** Switch through several themes (Void Crystal, Bubblegum, Moon Bunny Wink, Stinky Online) → all 4 row cards and the full editor keep the right colors and don't have visible WPF-default chrome.
13. **Edge case — empty state:** Open Avatar Swap Manager for a fresh avatar with no rules → all 4 sections show "no rules yet, click + Add ..." (or whatever the existing empty-state pattern is). The "← Back" button is hidden when the right pane is in list mode.

## Out of Scope / Future

- The Power Up editor in the main Redeem Library could be similarly extracted into a reusable UserControl in a future change. Not done here.
- The Movement Redeems editor is already unified with the shared `AvatarSwapRuleEditorControl`. No change.
- The Universal Triggers manager is fully isolated and uses its own editor. No change.
- The "Roulette Triggers" per-type card is the minimum viable form (a copy of the Channel Points card). If roulette gains Bits/Subs triggers in the future, the card can be expanded.
