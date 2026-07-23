# XSOverlay VR Notifications Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Add XSOverlay WebSocket-based VR toast notifications for Twitch follows, raids, and gift subs with per-event enable toggles, configurable timeout, and debounce windows.

**Architecture:** New `XSOverlaySettings` model in AppSettings, new `XSOverlayNotificationService` with WebSocket connection + per-event-type debounce system, hooked into `BridgeCoordinator` events from `MainWindowViewModel`, settings UI as a card in App Settings.

**Tech Stack:** .NET 10 / C#, WPF, System.Net.WebSockets, System.Text.Json

## Global Constraints

- All new `.cs` and `.xaml` files must be explicitly added to `VrcTwitchOscBridge.csproj` (EnableDefaultItems=false)
- Localization keys go in `Resources/Localization/en-US.json` first
- Follow existing code patterns: ObservableObject for models, no DI container (direct instantiation)
- WebSocket connects to `ws://localhost:42070/?client=CrystalRelay`
- Never log secrets, tokens, or auth data

---

### Task 1: XSOverlaySettings Model + AppSettings Integration

**Files:**
- Create: `VrcTwitchOscBridge\Models\XSOverlaySettings.cs`
- Modify: `VrcTwitchOscBridge\Models\AppSettings.cs`
- Modify: `VrcTwitchOscBridge\VrcTwitchOscBridge.csproj`

**Interfaces:**
- Produces: `XSOverlaySettings` class with observable properties, integrated into `AppSettings` as `AppSettings.XSOverlay`

- [ ] **Step 1: Create `Models\XSOverlaySettings.cs`**

```csharp
using System.Text.Json.Serialization;
using CommunityToolkit.Mvvm.ComponentModel;

namespace VrcTwitchOscBridge.Models;

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

    public bool EnableFollowNotifications
    {
        get => _enableFollowNotifications;
        set => SetProperty(ref _enableFollowNotifications, value);
    }

    public bool EnableRaidNotifications
    {
        get => _enableRaidNotifications;
        set => SetProperty(ref _enableRaidNotifications, value);
    }

    public bool EnableGiftSubNotifications
    {
        get => _enableGiftSubNotifications;
        set => SetProperty(ref _enableGiftSubNotifications, value);
    }

    public float FollowTimeout
    {
        get => _followTimeout;
        set => SetProperty(ref _followTimeout, value);
    }

    public float RaidTimeout
    {
        get => _raidTimeout;
        set => SetProperty(ref _raidTimeout, value);
    }

    public float GiftSubTimeout
    {
        get => _giftSubTimeout;
        set => SetProperty(ref _giftSubTimeout, value);
    }

    public float FollowDebounceWindow
    {
        get => _followDebounceWindow;
        set => SetProperty(ref _followDebounceWindow, value);
    }

    public float RaidDebounceWindow
    {
        get => _raidDebounceWindow;
        set => SetProperty(ref _raidDebounceWindow, value);
    }

    public float GiftSubDebounceWindow
    {
        get => _giftSubDebounceWindow;
        set => SetProperty(ref _giftSubDebounceWindow, value);
    }
}
```

- [ ] **Step 2: Add property to `AppSettings.cs`**

Find `public sealed class AppSettings : ObservableObject` and add near the other feature settings:

```csharp
public XSOverlaySettings XSOverlay { get; set; } = new();
```

Also add the necessary using at top of AppSettings.cs if not already present:
```csharp
using VrcTwitchOscBridge.Models;
```
(May already be present since it's in the same namespace.)

- [ ] **Step 3: Add to `.csproj`**

In `VrcTwitchOscBridge.csproj`, add after the last `Models\` entry (around line 232):

```xml
<Compile Include="Models\XSOverlaySettings.cs" />
```

- [ ] **Step 4: Build to verify**

```powershell
dotnet build "E:\!!!Program to work on\Proper Crystal Relay\VrcTwitchOscBridge\VrcTwitchOscBridge.csproj" --no-restore
```

Expected: Build succeeds with no errors.

---

### Task 2: XSOverlayNotificationService

**Files:**
- Create: `VrcTwitchOscBridge\Services\XSOverlayNotificationService.cs`
- Modify: `VrcTwitchOscBridge\VrcTwitchOscBridge.csproj`

**Interfaces:**
- Consumes: `XSOverlaySettings` (from Task 1)
- Produces: `XSOverlayNotificationService` class with `QueueFollowEvent(string)`, `QueueRaidEvent(string, int)`, `QueueGiftSubEvent(string, int, string)` methods
- Produces: `Start()`, `Stop()` lifecycle methods

- [ ] **Step 1: Create `Services\XSOverlayNotificationService.cs`**

```csharp
using System;
using System.Collections.Generic;
using System.Net.WebSockets;
using System.Text;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using VrcTwitchOscBridge.Models;

namespace VrcTwitchOscBridge.Services;

public sealed class XSOverlayNotificationService : IDisposable
{
    private const string WebSocketUrl = "ws://localhost:42070/?client=CrystalRelay";
    private const float DefaultVolume = 0.7f;
    private const float DefaultOpacity = 1f;
    private const float DefaultHeight = 175f;
    private const string DefaultAudio = "default";
    private const string DefaultIcon = "default";
    private const string SourceApp = "Crystal Relay";

    private readonly XSOverlaySettings _settings;
    private ClientWebSocket? _webSocket;
    private CancellationTokenSource? _cts;
    private readonly DebounceContext _followDebounce;
    private readonly DebounceContext _raidDebounce;
    private readonly DebounceContext _giftSubDebounce;
    private bool _disposed;

    private sealed class DebounceContext
    {
        private readonly XSOverlayNotificationService _owner;
        private readonly Func<XSOverlaySettings, bool> _enabledGetter;
        private readonly Func<XSOverlaySettings, float> _windowGetter;
        private readonly Func<XSOverlaySettings, float> _timeoutGetter;
        private readonly Func<DebounceAccumulator, string> _messageBuilder;
        private Timer? _timer;
        private DebounceAccumulator? _accumulator;
        private readonly object _lock = new();

        public DebounceContext(
            XSOverlayNotificationService owner,
            Func<XSOverlaySettings, bool> enabledGetter,
            Func<XSOverlaySettings, float> windowGetter,
            Func<XSOverlaySettings, float> timeoutGetter,
            Func<DebounceAccumulator, string> messageBuilder)
        {
            _owner = owner;
            _enabledGetter = enabledGetter;
            _windowGetter = windowGetter;
            _timeoutGetter = timeoutGetter;
            _messageBuilder = messageBuilder;
        }

        public void Enqueue(Action<DebounceAccumulator> accumulate)
        {
            if (!_enabledGetter(_owner._settings))
                return;

            lock (_lock)
            {
                _accumulator ??= new DebounceAccumulator();
                accumulate(_accumulator);
                ResetTimer();
            }
        }

        private void ResetTimer()
        {
            _timer?.Dispose();
            var windowMs = (int)(_windowGetter(_owner._settings) * 1000);
            _timer = new Timer(OnTimerFired, null, windowMs, Timeout.Infinite);
        }

        private void OnTimerFired(object? state)
        {
            DebounceAccumulator? snapshot;
            lock (_lock)
            {
                snapshot = _accumulator;
                _accumulator = null;
                _timer?.Dispose();
                _timer = null;
            }

            if (snapshot is null)
                return;

            var timeout = _timeoutGetter(_owner._settings);
            var message = _messageBuilder(snapshot);
            _ = _owner.SendNotificationAsync(message, timeout);
        }
    }

    public sealed class DebounceAccumulator
    {
        public int TotalCount { get; set; }
        public string? FirstUserName { get; set; }
        public int UniqueUserCount { get; set; }
        public string? LatestUserName { get; set; }
        public int LatestViewerCount { get; set; }
        public string? LatestGifterName { get; set; }
        public int GiftSubTotal { get; set; }
        public string? Tier { get; set; }
    }

    public XSOverlayNotificationService(XSOverlaySettings settings)
    {
        _settings = settings;
        _followDebounce = new DebounceContext(this,
            s => s.EnableFollowNotifications,
            s => s.FollowDebounceWindow,
            s => s.FollowTimeout,
            acc => acc.TotalCount > 1
                ? $"{acc.FirstUserName} +{acc.TotalCount - 1} others followed"
                : $"{acc.FirstUserName} followed");

        _raidDebounce = new DebounceContext(this,
            s => s.EnableRaidNotifications,
            s => s.RaidDebounceWindow,
            s => s.RaidTimeout,
            acc => $"{acc.LatestUserName} raided with {acc.LatestViewerCount:N0} viewers");

        _giftSubDebounce = new DebounceContext(this,
            s => s.EnableGiftSubNotifications,
            s => s.GiftSubDebounceWindow,
            s => s.GiftSubTimeout,
            acc => $"{acc.LatestGifterName} gifted {acc.GiftSubTotal:N0} subs");
    }

    public void Start()
    {
        _cts = new CancellationTokenSource();
        _ = ConnectAndRunAsync(_cts.Token);
    }

    public void Stop()
    {
        _cts?.Cancel();
        _cts?.Dispose();
        _cts = null;
    }

    private async Task ConnectAndRunAsync(CancellationToken ct)
    {
        var retryDelay = TimeSpan.FromSeconds(1);
        const int maxRetryDelaySeconds = 30;

        while (!ct.IsCancellationRequested)
        {
            try
            {
                _webSocket?.Dispose();
                _webSocket = new ClientWebSocket();
                await _webSocket.ConnectAsync(new Uri(WebSocketUrl), ct);
                retryDelay = TimeSpan.FromSeconds(1);

                // Keep connection alive; wait for close
                var buffer = new byte[4096];
                while (_webSocket.State == WebSocketState.Open && !ct.IsCancellationRequested)
                {
                    var result = await _webSocket.ReceiveAsync(new ArraySegment<byte>(buffer), ct);
                    if (result.MessageType == WebSocketMessageType.Close)
                        break;
                }
            }
            catch (OperationCanceledException)
            {
                break;
            }
            catch
            {
                // Connection failed or lost - retry with backoff
            }

            if (!ct.IsCancellationRequested)
            {
                try
                {
                    await Task.Delay(retryDelay, ct);
                    retryDelay = TimeSpan.FromSeconds(Math.Min(retryDelay.TotalSeconds * 2, maxRetryDelaySeconds));
                }
                catch (OperationCanceledException)
                {
                    break;
                }
            }
        }
    }

    private async Task SendNotificationAsync(string messageText, float timeout)
    {
        if (_webSocket?.State != WebSocketState.Open)
            return;

        try
        {
            var notification = new XSONotificationObject
            {
                messageType = 1,
                title = "Crystal Relay",
                content = messageText,
                timeout = timeout,
                height = DefaultHeight,
                opacity = DefaultOpacity,
                volume = DefaultVolume,
                audioPath = DefaultAudio,
                icon = DefaultIcon,
                useBase64Icon = false,
                sourceApp = SourceApp
            };

            var apiObject = new XSOApiObject
            {
                sender = "crystal_relay",
                target = "xsoverlay",
                command = "SendNotification",
                jsonData = JsonSerializer.Serialize(notification),
                rawData = null
            };

            var json = JsonSerializer.Serialize(apiObject);
            var bytes = Encoding.UTF8.GetBytes(json);
            await _webSocket.SendAsync(new ArraySegment<byte>(bytes), WebSocketMessageType.Text, true, CancellationToken.None);
        }
        catch
        {
            // Silently fail - XSOverlay might not be running
        }
    }

    public void QueueFollowEvent(string userName)
    {
        _followDebounce.Enqueue(acc =>
        {
            acc.TotalCount++;
            acc.FirstUserName ??= userName;
            acc.UniqueUserCount = acc.TotalCount;
        });
    }

    public void QueueRaidEvent(string broadcasterName, int viewerCount)
    {
        _raidDebounce.Enqueue(acc =>
        {
            acc.LatestUserName = broadcasterName;
            acc.LatestViewerCount = viewerCount;
        });
    }

    public void QueueGiftSubEvent(string gifterName, int amount, string tier)
    {
        _giftSubDebounce.Enqueue(acc =>
        {
            acc.LatestGifterName = gifterName;
            acc.GiftSubTotal += amount;
            acc.Tier = tier;
        });
    }

    public void Dispose()
    {
        if (_disposed)
            return;
        _disposed = true;
        Stop();
        _webSocket?.Dispose();
    }

    // WebSocket API JSON structures
    private struct XSONotificationObject
    {
        public int messageType { get; set; }
        public string title { get; set; }
        public string content { get; set; }
        public float timeout { get; set; }
        public float height { get; set; }
        public float opacity { get; set; }
        public float volume { get; set; }
        public string audioPath { get; set; }
        public string icon { get; set; }
        public bool useBase64Icon { get; set; }
        public string sourceApp { get; set; }
    }

    private struct XSOApiObject
    {
        public string sender { get; set; }
        public string target { get; set; }
        public string command { get; set; }
        public string? jsonData { get; set; }
        public string? rawData { get; set; }
    }
}
```

- [ ] **Step 2: Add to `.csproj`**

In `VrcTwitchOscBridge.csproj`, add after the last `Services\` entry:

```xml
<Compile Include="Services\XSOverlayNotificationService.cs" />
```

- [ ] **Step 3: Build to verify**

```powershell
dotnet build "E:\!!!Program to work on\Proper Crystal Relay\VrcTwitchOscBridge\VrcTwitchOscBridge.csproj" --no-restore
```

Expected: Build succeeds with no errors.

---

### Task 3: Wire Events in MainWindowViewModel + Lifecycle

**Files:**
- Modify: `VrcTwitchOscBridge\ViewModels\MainWindowViewModel.cs`
- Modify: `VrcTwitchOscBridge\MainWindow.xaml.cs`

**Interfaces:**
- Consumes: `XSOverlayNotificationService` from Task 2
- Consumes: `BridgeCoordinator.ChatMessageReceived` and `BridgeCoordinator.ChatActivityReceived` events

- [ ] **Step 1: In `MainWindowViewModel.cs`, add field and instantiate the service**

Find the services section around the existing `bridgeCoordinator` field. Add:

```csharp
private readonly XSOverlayNotificationService _xsOverlayNotificationService;
```

After `bridgeCoordinator` is constructed (around line 626), add:

```csharp
_xsOverlayNotificationService = new XSOverlayNotificationService(Settings.XSOverlay);
```

- [ ] **Step 2: Subscribe to BridgeCoordinator events**

After the instantiation, add the event subscriptions:

```csharp
bridgeCoordinator.ChatMessageReceived += OnBridgeChatMessageReceived;
bridgeCoordinator.ChatActivityReceived += OnBridgeChatActivityReceived;
```

- [ ] **Step 3: Add event handlers**

Add these methods to the class:

```csharp
private void OnBridgeChatMessageReceived(BridgeChatMessage message)
{
    if (message.Kind == BridgeChatMessageKind.Raid)
    {
        _xsOverlayNotificationService.QueueRaidEvent(message.UserDisplayName, message.SupportAmount);
    }
    else if (message.Kind == BridgeChatMessageKind.GiftSubscription)
    {
        _xsOverlayNotificationService.QueueGiftSubEvent(message.UserDisplayName, message.SupportAmount, message.SupportTier);
    }
}

private void OnBridgeChatActivityReceived(BridgeChatActivity activity)
{
    if (activity.Kind == BridgeChatActivityKind.Follow)
    {
        _xsOverlayNotificationService.QueueFollowEvent(activity.TargetUserDisplayName);
    }
}
```

- [ ] **Step 4: Start/stop the service with the bridge**

Find where the bridge starts (likely in a Start or Initialize method) and add:

```csharp
_xsOverlayNotificationService.Start();
```

Find where the bridge stops and add:

```csharp
_xsOverlayNotificationService.Stop();
```

- [ ] **Step 5: Dispose in MainWindow.xaml.cs**

If `MainWindowViewModel` implements `IDisposable` or has a cleanup path, add disposal. Otherwise, in `MainWindow.xaml.cs` `OnClosed` or similar, add:

```csharp
if (DataContext is MainWindowViewModel vm)
{
    vm.Dispose(); // or call cleanup
}
```

- [ ] **Step 6: Build to verify**

```powershell
dotnet build "E:\!!!Program to work on\Proper Crystal Relay\VrcTwitchOscBridge\VrcTwitchOscBridge.csproj" --no-restore
```

Expected: Build succeeds with no errors.

---

### Task 4: Settings UI in MainWindow.xaml

**Files:**
- Modify: `VrcTwitchOscBridge\MainWindow.xaml`

- [ ] **Step 1: Add XSOverlay settings card in App Settings section**

Find the App Settings section in `MainWindow.xaml` (the `Border` with `Visibility="{Binding IsSettingsAppSectionSelected...}"`). After the last card in that section, add a new card:

```xml
<Border Margin="0,14,0,0" Background="{DynamicResource NestedPanelBrush}" BorderBrush="{DynamicResource InputBorderBrush}" BorderThickness="1" CornerRadius="18" Padding="16"
        Visibility="{Binding IsSettingsAppSectionSelected, Converter={StaticResource BoolToVisibilityConverter}}">
    <StackPanel>
        <TextBlock Text="{loc:Translate 'XSOverlay Notifications'}"
                   Style="{StaticResource HeadingTextStyle}"
                   FontSize="17"
                   FontWeight="SemiBold"
                   Foreground="{DynamicResource TextBrush}"
                   TextWrapping="Wrap" />
        <TextBlock Margin="0,8,0,0"
                   Text="{loc:Translate 'Send VR toast notifications to XSOverlay for Twitch events.'}"
                   Foreground="{DynamicResource MutedBrush}"
                   TextWrapping="Wrap" />

        <!-- Follow -->
        <Grid Margin="0,12,0,0">
            <Grid.ColumnDefinitions>
                <ColumnDefinition Width="*" />
                <ColumnDefinition Width="Auto" />
            </Grid.ColumnDefinitions>
            <Grid.RowDefinitions>
                <RowDefinition Height="Auto" />
                <RowDefinition Height="Auto" />
                <RowDefinition Height="Auto" />
            </Grid.RowDefinitions>

            <TextBlock Grid.Row="0" Grid.Column="0" Text="{loc:Translate 'Follows'}" VerticalAlignment="Center"
                       Foreground="{DynamicResource TextBrush}" FontWeight="SemiBold" />
            <ToggleButton Grid.Row="0" Grid.Column="1" VerticalAlignment="Center"
                          Style="{StaticResource RuleToggleStyle}"
                          IsChecked="{Binding Settings.XSOverlay.EnableFollowNotifications, UpdateSourceTrigger=PropertyChanged}" />

            <StackPanel Grid.Row="1" Grid.Column="0" Grid.ColumnSpan="2" Orientation="Horizontal" Margin="0,6,0,0">
                <TextBlock Text="{loc:Translate 'Timeout:'}" Foreground="{DynamicResource MutedBrush}" VerticalAlignment="Center" />
                <Slider Minimum="1" Maximum="30" TickFrequency="0.5" IsSnapToTickEnabled="True" Width="120" Margin="8,0"
                        Value="{Binding Settings.XSOverlay.FollowTimeout, UpdateSourceTrigger=PropertyChanged}" />
                <TextBlock Text="{Binding Settings.XSOverlay.FollowTimeout, StringFormat={}{0:N1}s}"
                           Foreground="{DynamicResource TextBrush}" VerticalAlignment="Center" Width="40" />
            </StackPanel>

            <StackPanel Grid.Row="2" Grid.Column="0" Grid.ColumnSpan="2" Orientation="Horizontal" Margin="0,4,0,0">
                <TextBlock Text="{loc:Translate 'Debounce:'}" Foreground="{DynamicResource MutedBrush}" VerticalAlignment="Center" />
                <Slider Minimum="0.5" Maximum="10" TickFrequency="0.5" IsSnapToTickEnabled="True" Width="120" Margin="8,0"
                        Value="{Binding Settings.XSOverlay.FollowDebounceWindow, UpdateSourceTrigger=PropertyChanged}" />
                <TextBlock Text="{Binding Settings.XSOverlay.FollowDebounceWindow, StringFormat={}{0:N1}s}"
                           Foreground="{DynamicResource TextBrush}" VerticalAlignment="Center" Width="40" />
            </StackPanel>
        </Grid>

        <!-- Raid -->
        <Grid Margin="0,12,0,0">
            <Grid.ColumnDefinitions>
                <ColumnDefinition Width="*" />
                <ColumnDefinition Width="Auto" />
            </Grid.ColumnDefinitions>
            <Grid.RowDefinitions>
                <RowDefinition Height="Auto" />
                <RowDefinition Height="Auto" />
                <RowDefinition Height="Auto" />
            </Grid.RowDefinitions>

            <TextBlock Grid.Row="0" Grid.Column="0" Text="{loc:Translate 'Raids'}" VerticalAlignment="Center"
                       Foreground="{DynamicResource TextBrush}" FontWeight="SemiBold" />
            <ToggleButton Grid.Row="0" Grid.Column="1" VerticalAlignment="Center"
                          Style="{StaticResource RuleToggleStyle}"
                          IsChecked="{Binding Settings.XSOverlay.EnableRaidNotifications, UpdateSourceTrigger=PropertyChanged}" />

            <StackPanel Grid.Row="1" Grid.Column="0" Grid.ColumnSpan="2" Orientation="Horizontal" Margin="0,6,0,0">
                <TextBlock Text="{loc:Translate 'Timeout:'}" Foreground="{DynamicResource MutedBrush}" VerticalAlignment="Center" />
                <Slider Minimum="1" Maximum="30" TickFrequency="0.5" IsSnapToTickEnabled="True" Width="120" Margin="8,0"
                        Value="{Binding Settings.XSOverlay.RaidTimeout, UpdateSourceTrigger=PropertyChanged}" />
                <TextBlock Text="{Binding Settings.XSOverlay.RaidTimeout, StringFormat={}{0:N1}s}"
                           Foreground="{DynamicResource TextBrush}" VerticalAlignment="Center" Width="40" />
            </StackPanel>

            <StackPanel Grid.Row="2" Grid.Column="0" Grid.ColumnSpan="2" Orientation="Horizontal" Margin="0,4,0,0">
                <TextBlock Text="{loc:Translate 'Debounce:'}" Foreground="{DynamicResource MutedBrush}" VerticalAlignment="Center" />
                <Slider Minimum="0.5" Maximum="10" TickFrequency="0.5" IsSnapToTickEnabled="True" Width="120" Margin="8,0"
                        Value="{Binding Settings.XSOverlay.RaidDebounceWindow, UpdateSourceTrigger=PropertyChanged}" />
                <TextBlock Text="{Binding Settings.XSOverlay.RaidDebounceWindow, StringFormat={}{0:N1}s}"
                           Foreground="{DynamicResource TextBrush}" VerticalAlignment="Center" Width="40" />
            </StackPanel>
        </Grid>

        <!-- Gift Subs -->
        <Grid Margin="0,12,0,0">
            <Grid.ColumnDefinitions>
                <ColumnDefinition Width="*" />
                <ColumnDefinition Width="Auto" />
            </Grid.ColumnDefinitions>
            <Grid.RowDefinitions>
                <RowDefinition Height="Auto" />
                <RowDefinition Height="Auto" />
                <RowDefinition Height="Auto" />
            </Grid.RowDefinitions>

            <TextBlock Grid.Row="0" Grid.Column="0" Text="{loc:Translate 'Gift Subs'}" VerticalAlignment="Center"
                       Foreground="{DynamicResource TextBrush}" FontWeight="SemiBold" />
            <ToggleButton Grid.Row="0" Grid.Column="1" VerticalAlignment="Center"
                          Style="{StaticResource RuleToggleStyle}"
                          IsChecked="{Binding Settings.XSOverlay.EnableGiftSubNotifications, UpdateSourceTrigger=PropertyChanged}" />

            <StackPanel Grid.Row="1" Grid.Column="0" Grid.ColumnSpan="2" Orientation="Horizontal" Margin="0,6,0,0">
                <TextBlock Text="{loc:Translate 'Timeout:'}" Foreground="{DynamicResource MutedBrush}" VerticalAlignment="Center" />
                <Slider Minimum="1" Maximum="30" TickFrequency="0.5" IsSnapToTickEnabled="True" Width="120" Margin="8,0"
                        Value="{Binding Settings.XSOverlay.GiftSubTimeout, UpdateSourceTrigger=PropertyChanged}" />
                <TextBlock Text="{Binding Settings.XSOverlay.GiftSubTimeout, StringFormat={}{0:N1}s}"
                           Foreground="{DynamicResource TextBrush}" VerticalAlignment="Center" Width="40" />
            </StackPanel>

            <StackPanel Grid.Row="2" Grid.Column="0" Grid.ColumnSpan="2" Orientation="Horizontal" Margin="0,4,0,0">
                <TextBlock Text="{loc:Translate 'Debounce:'}" Foreground="{DynamicResource MutedBrush}" VerticalAlignment="Center" />
                <Slider Minimum="0.5" Maximum="10" TickFrequency="0.5" IsSnapToTickEnabled="True" Width="120" Margin="8,0"
                        Value="{Binding Settings.XSOverlay.GiftSubDebounceWindow, UpdateSourceTrigger=PropertyChanged}" />
                <TextBlock Text="{Binding Settings.XSOverlay.GiftSubDebounceWindow, StringFormat={}{0:N1}s}"
                           Foreground="{DynamicResource TextBrush}" VerticalAlignment="Center" Width="40" />
            </StackPanel>
        </Grid>
    </StackPanel>
</Border>
```

- [ ] **Step 2: Build to verify**

```powershell
dotnet build "E:\!!!Program to work on\Proper Crystal Relay\VrcTwitchOscBridge\VrcTwitchOscBridge.csproj" --no-restore
```

Expected: Build succeeds with no errors.

---

### Task 5: Localization Keys

**Files:**
- Modify: `VrcTwitchOscBridge\Resources\Localization\en-US.json`

- [ ] **Step 1: Add localization entries to `en-US.json`**

Add the following keys (inserted alphabetically):

```json
  "Crystal Relay": "Crystal Relay",
  "Debounce:": "Debounce:",
  "Follows": "Follows",
  "Gift Subs": "Gift Subs",
  "Raids": "Raids",
  "Send VR toast notifications to XSOverlay for Twitch events.": "Send VR toast notifications to XSOverlay for Twitch events.",
  "Timeout:": "Timeout:",
  "XSOverlay Notifications": "XSOverlay Notifications",
```

- [ ] **Step 2: Build to verify**

```powershell
dotnet build "E:\!!!Program to work on\Proper Crystal Relay\VrcTwitchOscBridge\VrcTwitchOscBridge.csproj" --no-restore
```

Expected: Build succeeds with no errors.
