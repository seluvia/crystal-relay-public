# UI Update One-Time Notice

## Problem
A major UI reorganization (3-column layout, Redeem Library nav grid, status cards) has been shipped. Users need to be warned to verify their reward configurations transferred correctly.

## Solution
A one-time "big and noticeable" themed dialog shown on first launch after the update.

## Tracking Flag
- **Setting:** `UiUpdateNoticeShown` (`bool`) in `AppSettings.cs`
- **Serialization:** Same JSON pattern as `AvatarSwapMigrationNoticeShown` / `CashPaymentMigrationNoticeShown` in `SettingsStore.cs`
- **Default:** `false` — notice shows on first launch after update

## Visual Design
- Leverage the existing `ThemedDialogWindow` with a new static method `ShowNotice`
- **Width:** 680px (vs 500px standard)
- **Heading font size:** 28px bold (vs 24px standard)
- **Body font size:** 15px (vs 13px standard)
- **Fine print (bullet list) font size:** 13px, visible by default
- **Visual accent:** 4px-tall accent-colored strip across the top of the content panel, exclusive to notice mode
- **Button:** Single "I Understand" (themed primary button)

### XAML Changes (ThemedDialogWindow.xaml)
- Add `IsNotice` dependency property (bool, default false)
- Conditional accent border strip at top of content panel, bound to `IsNotice`
- Bind `HeaderTextBlock.FontSize` to `HeadingFontSize` property
- Bind `MessageTextBlock.FontSize` to `BodyFontSize` property
- FinePrintTextBlock always visible in notice mode

### Code-Behind Changes (ThemedDialogWindow.xaml.cs)
- Add `IsNotice` property (settable via constructor)
- Add `HeadingFontSize` and `BodyFontSize` properties (default 24 and 13; 28 and 15 for notice)
- Add `ShowNotice` static method:
  - Signature: `ShowNotice(Window? owner, AppTheme theme, string title, string message, string? finePrint = null)`
  - Creates window with Width=680, MinWidth=680, IsNotice=true, single "I Understand" button
  - Shows as modal dialog

## Startup Flow
Follow the existing pattern from `ShowAvatarSwapMigrationNoticeIfNeeded`:

### MainWindowViewModel.cs
- Add `bool ShowUiUpdateNotice => !Settings.UiUpdateNoticeShown`
- Add `RelayCommand DismissUiUpdateNoticeCommand` → calls `DismissUiUpdateNotice()`
- `DismissUiUpdateNotice()` sets `Settings.UiUpdateNoticeShown = true` and saves

### MainWindow.xaml.cs
- Add `ShowUiUpdateNoticeIfNeeded()` method
- Call it at startup after `ShowAvatarSwapMigrationNoticeIfNeeded()` (line ~149)
- If `viewModel.ShowUiUpdateNotice` is true, show `ThemedDialogWindow.ShowNotice(...)`, then dismiss

## Message Content
**Title:** `Major UI Update — Please Verify Your Rewards`

**Body:**
```
Crystal Relay's main layout has been reorganized. Your reward configurations are still here, but some sections may have moved or look different.

Please review each of your reward systems to make sure everything transferred correctly:
```

**Fine print / bullet list:**
```
 • Avatar Sets & Avatar Change
 • Avatar Roulette
 • Bits / Subs / Payment overrides
 • Avatar Scaling
 • Power Ups & Channel Point Rewards
 • Universal Triggers
 • Cash Payment rules
```

**Footer:** `This notice will not appear again.`

## Files Changed
1. `VrcTwitchOscBridge\Models\AppSettings.cs` — add `UiUpdateNoticeShown`
2. `VrcTwitchOscBridge\Services\SettingsStore.cs` — serialize/deserialize new flag
3. `VrcTwitchOscBridge\ThemedDialogWindow.xaml` — add `IsNotice` accent strip, bindable font sizes
4. `VrcTwitchOscBridge\ThemedDialogWindow.xaml.cs` — add `ShowNotice`, `IsNotice`, font size properties
5. `VrcTwitchOscBridge\ViewModels\MainWindowViewModel.cs` — add property + command + handler
6. `VrcTwitchOscBridge\MainWindow.xaml.cs` — add startup call
