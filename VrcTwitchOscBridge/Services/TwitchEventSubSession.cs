using System.Net.WebSockets;
using System.IO;
using System.Text;
using System.Text.Json;
using System.Threading.Channels;

namespace VrcTwitchOscBridge.Services;

// One EventSub notification payload passed back to the bridge loop.
public sealed record EventSubNotification(string MessageId, string SubscriptionType, JsonElement EventData);

// Result of one EventSub listen cycle, including whether Twitch asked the client to reconnect.
public sealed record EventSubListenResult(bool ReconnectRequested, string? ReconnectUrl, string Reason);

/// <summary>
/// Handles one Twitch EventSub websocket session.
/// The bridge loop creates this class, connects, listens for messages, and then reconnects as needed.
/// </summary>
public sealed class TwitchEventSubSession : IAsyncDisposable
{
    private const string DefaultWebSocketUrl = "wss://eventsub.wss.twitch.tv/ws";
    private const string AllowedReconnectHost = "eventsub.wss.twitch.tv";
    private const int MaxWebSocketMessageBytes = 262144;
    private const int DefaultNotificationQueueCapacity = 64;

    private ClientWebSocket? socket;
    private readonly Func<CancellationToken, Task<string?>> receiveMessageAsync;
    private readonly int notificationQueueCapacity;
    private CancellationTokenSource? receiveStopCancellation;
    private Task? detachedReceiveTask;

    public TwitchEventSubSession()
    {
        receiveMessageAsync = ReceiveMessageAsync;
        notificationQueueCapacity = DefaultNotificationQueueCapacity;
    }

    internal TwitchEventSubSession(
        Func<CancellationToken, Task<string?>> receiveMessageAsync,
        int notificationQueueCapacity)
    {
        ArgumentNullException.ThrowIfNull(receiveMessageAsync);
        if (notificationQueueCapacity <= 0)
        {
            throw new ArgumentOutOfRangeException(nameof(notificationQueueCapacity));
        }

        this.receiveMessageAsync = receiveMessageAsync;
        this.notificationQueueCapacity = notificationQueueCapacity;
    }

    public string SessionId { get; private set; } = string.Empty;

    // Connect waits until Twitch sends the session_welcome payload because the session ID
    // from that message is required for EventSub subscription registration.
    public async Task ConnectAsync(string? connectionUrl, CancellationToken cancellationToken = default)
    {
        socket = new ClientWebSocket();
        socket.Options.KeepAliveInterval = TimeSpan.FromSeconds(20);
        await socket.ConnectAsync(ResolveConnectionUri(connectionUrl), cancellationToken);

        while (true)
        {
            var message = await receiveMessageAsync(cancellationToken);
            if (message is null)
            {
                throw new InvalidOperationException("Twitch closed the EventSub socket before the session was ready.");
            }

            using var document = JsonDocument.Parse(message);
            var metadata = document.RootElement.GetProperty("metadata");
            var messageType = metadata.GetProperty("message_type").GetString();
            if (!string.Equals(messageType, "session_welcome", StringComparison.Ordinal))
            {
                continue;
            }

            SessionId = document.RootElement
                .GetProperty("payload")
                .GetProperty("session")
                .GetProperty("id")
                .GetString() ?? string.Empty;

            return;
        }
    }

    // Listen processes EventSub messages until Twitch closes the socket or asks for a reconnect.
    public async Task<EventSubListenResult> ListenAsync(
        Func<EventSubNotification, Task> onNotification,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(onNotification);

        var notificationChannel = Channel.CreateBounded<EventSubNotification>(
            new BoundedChannelOptions(notificationQueueCapacity)
            {
                FullMode = BoundedChannelFullMode.Wait,
                SingleReader = true,
                SingleWriter = true,
                AllowSynchronousContinuations = false
            });
        var receiveCancellation = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        receiveStopCancellation = receiveCancellation;
        var notificationWorker = ProcessNotificationsAsync(
            notificationChannel.Reader,
            onNotification,
            cancellationToken);
        EventSubListenResult? listenResult = null;

        try
        {
            while (true)
            {
                cancellationToken.ThrowIfCancellationRequested();
                var message = await receiveMessageAsync(receiveCancellation.Token);
                if (message is null)
                {
                    listenResult = new EventSubListenResult(false, null, "WebSocket closed.");
                    break;
                }

                using var document = JsonDocument.Parse(message);
                var root = document.RootElement;
                var metadata = root.GetProperty("metadata");
                var payload = root.GetProperty("payload");
                var messageType = metadata.GetProperty("message_type").GetString();

                switch (messageType)
                {
                    case "session_keepalive":
                    case "session_welcome":
                        break;

                    case "notification":
                    {
                        var notification = new EventSubNotification(
                            metadata.GetProperty("message_id").GetString() ?? Guid.NewGuid().ToString("N"),
                            metadata.GetProperty("subscription_type").GetString() ?? string.Empty,
                            payload.GetProperty("event").Clone());

                        // Wait rather than drop when the bounded queue is full. Twitch
                        // notifications are important events, so backpressure is explicit.
                        await notificationChannel.Writer.WriteAsync(notification, receiveCancellation.Token);
                        break;
                    }

                    case "session_reconnect":
                    {
                        var reconnectUrl = payload.GetProperty("session").GetProperty("reconnect_url").GetString();
                        listenResult = new EventSubListenResult(true, reconnectUrl, "Twitch requested a reconnect.");
                        detachedReceiveTask = ContinueReceivingAfterReconnectAsync(
                            notificationChannel.Writer,
                            receiveCancellation);
                        break;
                    }

                    case "revocation":
                        listenResult = new EventSubListenResult(false, null, "Twitch revoked one or more subscriptions.");
                        break;

                    default:
                        break;
                }

                if (listenResult is not null)
                {
                    break;
                }
            }

            return listenResult ?? throw new InvalidOperationException("The EventSub listen loop ended without a result.");
        }
        finally
        {
            if (listenResult?.ReconnectRequested != true)
            {
                notificationChannel.Writer.TryComplete();
                await notificationWorker;
                receiveCancellation.Dispose();
                if (ReferenceEquals(receiveStopCancellation, receiveCancellation))
                {
                    receiveStopCancellation = null;
                }
            }

            cancellationToken.ThrowIfCancellationRequested();
        }
    }

    private async Task ContinueReceivingAfterReconnectAsync(
        ChannelWriter<EventSubNotification> notificationWriter,
        CancellationTokenSource receiveCancellation)
    {
        try
        {
            while (true)
            {
                var message = await receiveMessageAsync(receiveCancellation.Token);
                if (message is null)
                {
                    return;
                }

                using var document = JsonDocument.Parse(message);
                var root = document.RootElement;
                var metadata = root.GetProperty("metadata");
                var payload = root.GetProperty("payload");
                var messageType = metadata.GetProperty("message_type").GetString();

                switch (messageType)
                {
                    case "notification":
                    {
                        var notification = new EventSubNotification(
                            metadata.GetProperty("message_id").GetString() ?? Guid.NewGuid().ToString("N"),
                            metadata.GetProperty("subscription_type").GetString() ?? string.Empty,
                            payload.GetProperty("event").Clone());
                        await notificationWriter.WriteAsync(notification, receiveCancellation.Token);
                        break;
                    }

                    case "revocation":
                        return;
                }
            }
        }
        catch (OperationCanceledException) when (receiveCancellation.IsCancellationRequested)
        {
        }
        catch
        {
            // The replacement session owns reconnect errors after the handoff signal.
        }
        finally
        {
            notificationWriter.TryComplete();
            receiveCancellation.Dispose();
            if (ReferenceEquals(receiveStopCancellation, receiveCancellation))
            {
                receiveStopCancellation = null;
            }
        }
    }

    private static async Task ProcessNotificationsAsync(
        ChannelReader<EventSubNotification> notificationReader,
        Func<EventSubNotification, Task> onNotification,
        CancellationToken cancellationToken)
    {
        var dispatchGate = new NotificationDispatchGate();
        using var cancellationRegistration = cancellationToken.Register(dispatchGate.Stop);

        try
        {
            await foreach (var notification in notificationReader.ReadAllAsync(cancellationToken))
            {
                cancellationToken.ThrowIfCancellationRequested();

                Task handlerTask;
                try
                {
                    if (!dispatchGate.TryBegin(
                            () => onNotification(notification),
                            cancellationToken,
                            out handlerTask))
                    {
                        return;
                    }

                    await handlerTask;
                }
                catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
                {
                    return;
                }
                catch (Exception)
                {
                    // A single application notification must not stop the receive pump.
                    // The bridge callback logs its own sanitized handler diagnostics.
                }
            }
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
        }
    }

    private sealed class NotificationDispatchGate
    {
        private readonly object gate = new();
        private bool stopped;

        public void Stop()
        {
            if (Monitor.IsEntered(gate))
            {
                stopped = true;
                return;
            }

            lock (gate)
            {
                stopped = true;
            }
        }

        public bool TryBegin(
            Func<Task> handler,
            CancellationToken cancellationToken,
            out Task handlerTask)
        {
            lock (gate)
            {
                if (stopped || cancellationToken.IsCancellationRequested)
                {
                    handlerTask = Task.CompletedTask;
                    return false;
                }

                handlerTask = handler();
                return true;
            }
        }
    }

    public async ValueTask DisposeAsync()
    {
        receiveStopCancellation?.Cancel();

        if (socket is not null)
        {
            try
            {
                if (socket.State == WebSocketState.Open || socket.State == WebSocketState.CloseReceived)
                {
                    await socket.CloseAsync(WebSocketCloseStatus.NormalClosure, "Closing", CancellationToken.None);
                }
            }
            catch
            {
            }
            finally
            {
                socket.Dispose();
                socket = null;
            }
        }

        if (detachedReceiveTask is not null)
        {
            try
            {
                await detachedReceiveTask;
            }
            catch
            {
            }
            finally
            {
                detachedReceiveTask = null;
            }
        }
    }

    // Receives one full websocket message, enforcing a size cap so a bad endpoint cannot
    // grow memory usage without limit.
    private async Task<string?> ReceiveMessageAsync(CancellationToken cancellationToken)
    {
        if (socket is null)
        {
            return null;
        }

        var buffer = new byte[4096];
        using var stream = new MemoryStream();

        while (true)
        {
            var result = await socket.ReceiveAsync(buffer, cancellationToken);

            if (result.MessageType == WebSocketMessageType.Close)
            {
                return null;
            }

            if (result.Count > 0)
            {
                if (stream.Length + result.Count > MaxWebSocketMessageBytes)
                {
                    throw new InvalidDataException($"Twitch EventSub message exceeded the {MaxWebSocketMessageBytes} byte safety limit.");
                }

                stream.Write(buffer, 0, result.Count);
            }

            if (result.EndOfMessage)
            {
                break;
            }
        }

        return Encoding.UTF8.GetString(stream.ToArray());
    }

    // Only secure Twitch EventSub websocket URLs are accepted, including reconnect URLs.
    private static Uri ResolveConnectionUri(string? connectionUrl)
    {
        var uriText = string.IsNullOrWhiteSpace(connectionUrl)
            ? DefaultWebSocketUrl
            : connectionUrl.Trim();

        if (!Uri.TryCreate(uriText, UriKind.Absolute, out var uri))
        {
            throw new InvalidOperationException("Twitch EventSub returned an invalid reconnect URL.");
        }

        if (!string.Equals(uri.Scheme, "wss", StringComparison.OrdinalIgnoreCase))
        {
            throw new InvalidOperationException("Twitch EventSub reconnects must use a secure WebSocket URL.");
        }

        if (!string.Equals(uri.Host, AllowedReconnectHost, StringComparison.OrdinalIgnoreCase))
        {
            throw new InvalidOperationException("Twitch EventSub reconnect URL did not point back to Twitch.");
        }

        if (!string.IsNullOrWhiteSpace(uri.UserInfo))
        {
            throw new InvalidOperationException("Twitch EventSub reconnect URL contained unexpected credentials.");
        }

        return uri;
    }
}
