using System.Net.WebSockets;
using System.IO;
using System.Text;
using System.Text.Json;

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

    private ClientWebSocket? socket;

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
            var message = await ReceiveMessageAsync(cancellationToken);
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
        while (true)
        {
            cancellationToken.ThrowIfCancellationRequested();
            var message = await ReceiveMessageAsync(cancellationToken);
            if (message is null)
            {
                return new EventSubListenResult(false, null, "WebSocket closed.");
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

                    await onNotification(notification);
                    break;
                }

                case "session_reconnect":
                {
                    var reconnectUrl = payload.GetProperty("session").GetProperty("reconnect_url").GetString();
                    return new EventSubListenResult(true, reconnectUrl, "Twitch requested a reconnect.");
                }

                case "revocation":
                    return new EventSubListenResult(false, null, "Twitch revoked one or more subscriptions.");

                default:
                    break;
            }
        }
    }

    public async ValueTask DisposeAsync()
    {
        if (socket is null)
        {
            return;
        }

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
