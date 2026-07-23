# XSOverlay VR Notifications Integration

## Overview

Add in-VR toast notifications to Crystal Relay via XSOverlay's WebSocket API for three Twitch event types: follows, raids, and gift subs. Users can enable/disable each event type and configure timeout duration and debounce window per type.

## Architecture

A new `XSOverlayNotificationService` class manages a WebSocket connection to XSOverlay and receives Twitch events forwarded from `BridgeCoordinator`. Debounce per event type consolidates bursts into a single toast.

## Files to Create

### `Services/XSOverlayNotificationService.cs`

Central service with these responsibilities:

**WebSocket lifecycle:**
- Connect to `ws://localhost:42070/?client=CrystalRelay` on startup
- Auto-reconnect on disconnect with exponential backoff (1s, 2s, 4s, max 30s)
- Send `SendNotification` commands as JSON via the WebSocket API format

**Event hook interface:**
- `QueueFollowEvent(string userName)` — called for `BridgeChatActivityKind.Follow`
- `QueueRaidEvent(string broadcasterName, int viewerCount)` — called for `BridgeChatMessageKind.Raid`
- `QueueGiftSubEvent(string gifterName, int amount, string tier)` — called for `BridgeChatMessageKind.GiftSubscription`

Each method feeds into the debounce system.

**Debounce system:**
- Per-event-type `DebounceContext` with:
  - `AccumulatedData` — merged event info during the window
  - `Timer` — resets on each new event, fires when window expires
  - Immutable per-type config: timeout, debounce window, enabled flag
- When timer fires, build a consolidated notification and send via WebSocket

**Notification format (WebSocket):**
```json
{
  "messageType": 1,
  "title": "Crystal Relay",
  "content": "...",
  "timeout": <user-configured>,
  "height": 175,
  "opacity": 1,
  "volume": 0.7,
  "audioPath": "default",
  "icon": "default",
  "useBase64Icon": false,
  "sourceApp": "Crystal Relay"
}
```

Wrapped in the XSO API envelope:
```json
{
  "sender": "crystal_relay",
  "target": "xsoverlay",
  "command": "SendNotification",
  "jsonData": "<serialized notification>",
  "rawData": null
}
```

### `Models/XSOverlaySettings.cs`

A new settings class nested in `AppSettings`:
```csharp
public sealed class XSOverlaySettings : ObservableObject
{
    private bool _enableFollowNotifications = true;
    private bool _enableRaidNotifications = true;
    private bool _enableGiftSubNotifications = true;
    private float _followTimeout = 4f;
    private float _raidTimeout = 6f;
    private float _giftSubTimeout = 5f;
    private float _followDebounceWindow = 3f;
    private float _raidDebounceWindow = 3f;
    private float _giftSubDebounceWindow = 3f;

    // ... observable properties ...
}
```

## Files to Modify

### `Models/AppSettings.cs`
- Add `public XSOverlaySettings XSOverlay { get; set; } = new();`

### `ViewModels/MainWindowViewModel.cs`
- Instantiate `XSOverlayNotificationService`
- Subscribe to `BridgeCoordinator.ChatMessageReceived` and `BridgeCoordinator.ChatActivityReceived`
- Forward matching events to the notification service
- Add XSOverlay settings section to the Settings UI (XAML section in `MainWindow.xaml`)

### `MainWindow.xaml`
- Add a collapsible "XSOverlay Notifications" settings card (following the pattern of other settings cards)
- Controls: per-event enable toggle, timeout slider/field, debounce window slider/field

### `MainWindow.xaml.cs`
- Wire the notification service lifecycle (start on app launch, stop on close)

## Data Flow

```
Twitch EventSub WS → TwitchEventSubSession
  → BridgeCoordinator.HandleNotificationAsync()
    → ParseSupportEventChatboxMessage()  // raids, gift subs
    → ParseChatActivityNotification()    // follows
    → ChatMessageReceived / ChatActivityReceived events
      → MainWindowViewModel forwards to
        → XSOverlayNotificationService.Queue*Event()
          → DebounceContext accumulates
            → Timer fires → send WebSocket JSON
              → XSOverlay → VR toast
```

## Debounce Behavior

| Event Type | Default Window | Default Timeout | Consolidation |
|-----------|---------------|-----------------|---------------|
| Follow | 3.0s | 4.0s | "User +N others followed" |
| Raid | 3.0s | 6.0s | Last raid wins (raids don't burst) |
| GiftSub | 3.0s | 5.0s | "User gifted N subs" (total accumulated) |

## Settings UI

A single card in the Settings panel with:
- Card header: "XSOverlay Notifications" with icon
- Three sub-sections (follow, raid, gift sub), each with:
  - Toggle switch (enable/disable)
  - Timeout slider (1.0–30.0s, step 0.5)
  - Debounce window slider (0.5–10.0s, step 0.5)

## Error Handling

- WebSocket connection failures are silent — log to debug log, don't block the app
- Auto-reconnect with backoff, max 30s interval
- If XSOverlay is not running, the WebSocket connection will fail — service stays dormant until next retry
- Disposed on app shutdown

## Future Extensions

The WebSocket connection can later be reused for:
- Sending other notification types (bits, subs, channel points)
- Querying SteamVR device battery for About page
- Theme/date queries
- Haptic feedback on Twitch events
