# Make Live Feedback Heartbeat Always-On

## Problem
Live Feedback Heartbeat helps the developer find live Crystal Relay streams for feedback, support, and community interaction. It should always be enabled — there is no valid reason for a user to turn it off. Users who had it disabled in a previous version should be automatically re-enabled on upgrade.

## Changes

### MainWindow.xaml
- Keep the "Live Feedback Heartbeat" heading and its description paragraph.
- Update the description to remove "You can turn this off anytime in Settings." since that's no longer true.
- Remove the Grid row containing the "Enable Live Feedback Heartbeat" label, sub-description, and ToggleButton.

### AppSettings.cs
- `LiveFeedbackHeartbeatEnabled` property always returns `true`.
- Setter is a no-op — nothing can disable it at the code level.

### SettingsStore.cs
- On load: always override the setting to `true`, regardless of what the saved profile says.
- On save: always write `true`.

### No changes needed
- `LiveFeedbackHeartbeatService.cs` already gates on `state.Enabled`; since the setting is always true, the service always considers it enabled.
- `MainWindowViewModel.cs` event listener for `LiveFeedbackHeartbeatEnabled` is harmless dead code (property no longer fires changes).

## Files Modified
1. `VrcTwitchOscBridge/MainWindow.xaml`
2. `VrcTwitchOscBridge/Models/AppSettings.cs`
3. `VrcTwitchOscBridge/Services/SettingsStore.cs`
